#!/usr/bin/env python3
"""Audit measured production station poses against the original room mesh triangles.

Requires developer-only UnityPy and numpy. Supply the .poses.csv exported by the real
workspace fixture; no gameplay data is changed. This is a conservative collision audit,
not a headset/rendering claim or an alpha-tested foliage intersection oracle.
"""
import argparse
import csv
import hashlib
import json
import math
import re
from pathlib import Path
import numpy as np
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler


def matrix(t):
    x, y, z, w = t.m_LocalRotation.x, t.m_LocalRotation.y, t.m_LocalRotation.z, t.m_LocalRotation.w
    r = np.array([[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w, 0],
                  [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w, 0],
                  [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y, 0], [0, 0, 0, 1.]], dtype=float)
    s, p = t.m_LocalScale, t.m_LocalPosition
    r[:3, :3] *= np.array([s.x, s.y, s.z])[None, :]
    r[:3, 3] = [p.x, p.y, p.z]
    return r


NON_SOLID = ('ground', 'floor', 'fog', 'canopy', 'leaf', 'leaves', 'shadow', 'shaft',
             'glow', 'cobweb', 'candle', 'fire', 'splash', 'frost', 'ceiling', 'rat',
             'haunt', 'flame', 'strand', 'growth', 'grass', 'fungus', 'ivy', 'moss')


PROP_PREFIXES = ('Barrel', 'Table', 'Stool', 'Shelf', 'Bucket', 'Jug', 'Log', 'LeanTree',
                 'Stump', 'Axe', 'Rocks', 'Branches', 'Fern', 'Shrub')


def expand_geometry(vertices, faces, name, pivot, expansion):
    """Mirror production room-shell expansion, preserving discrete props/tree islands."""
    result = vertices.copy()
    if name in ('TrunksNear', 'TrunksFar'):
        parents = list(range(len(vertices)))
        def find(index):
            while parents[index] != index:
                parents[index] = parents[parents[index]]
                index = parents[index]
            return index
        for triangle in faces:
            root = find(int(triangle[0]))
            for index in triangle[1:]:
                parents[find(int(index))] = root
        groups = {}
        for index in range(len(vertices)):
            groups.setdefault(find(index), []).append(index)
        for indices in groups.values():
            group = vertices[indices]
            centre = (group.min(0) + group.max(0)) * .5
            result[np.ix_(indices, [0, 2])] += centre[[0, 2]] * (expansion-1)
    elif name.startswith(PROP_PREFIXES):
        result[:, [0, 2]] += pivot[[0, 2]] * (expansion-1)
    else:
        result[:, [0, 2]] *= expansion
    return result


def room_meshes(go, frame, path, ratio, output, expansion=1.):
    t = next(c.component.read() for c in go.m_Component if c.component.type.name == 'Transform')
    frame = frame @ matrix(t)
    path += '/' + go.m_Name
    solid = 'RoomGeo/' in path and not any(key in go.m_Name.lower() for key in NON_SOLID)
    if solid:
        for component in go.m_Component:
            if component.component.type.name != 'MeshFilter':
                continue
            ref = component.component.read().m_Mesh
            if not ref.path_id:
                continue
            handler = MeshHandler(ref.read())
            handler.process()
            v = np.asarray(handler.m_Vertices)
            v = (np.column_stack((v, np.ones(len(v)))) @ frame.T)[:, :3] / ratio
            faces = np.concatenate([np.asarray(indices) for indices in handler.get_triangles()])
            v = expand_geometry(v, faces, go.m_Name, frame[:3, 3]/ratio, expansion)
            triangles = v[faces]
            for high in (1.55, 2.1):
                q = triangles[(triangles[:, :, 1].max(1) > .04) & (triangles[:, :, 1].min(1) < high)][:, :, [0, 2]]
                if len(q):
                    output.append((path, high, q, q.min(1), q.max(1)))
    for child in t.m_Children:
        room_meshes(child.read().m_GameObject.read(), frame, path, ratio, output, expansion)


def part(origin, yaw, limits, padding):
    xlo, xhi, zlo, zhi = limits
    xlo -= padding; xhi += padding; zlo -= padding; zhi += padding
    angle = math.radians(yaw)
    direction = np.array([math.sin(angle), math.cos(angle)])
    right = np.array([direction[1], -direction[0]])
    return np.array([origin + right*x + direction*z for x, z in
                     ((xlo, zlo), (xhi, zlo), (xhi, zhi), (xlo, zhi))])


def parts(origin, yaw, role, service, padding, returns=0):
    merchant = role == 'visitor' or service == 1
    spans = [('top', (-1.90, 1.90, -1.45, .45) if merchant else (-.87, .87, -.43, .50))]
    if role == 'resident':
        spans.append(('actor', (-.60, .60, .30, 1.20)))
    if role == 'resident' and service == 3:
        spans.append(('lantern', (.49, .87, .28, .86)))
    result = [(name, part(origin, yaw, limits, padding)) for name, limits in spans]
    if merchant:
        for index in range(returns):
            pair, side = index//2, -1 if index % 2 == 0 else 1
            x, z = side*(2.85+.25*pair), -.25+pair*1.40
            angle = math.radians(yaw)
            position = origin + np.array([math.cos(angle)*x+math.sin(angle)*z,
                                          -math.sin(angle)*x+math.cos(angle)*z])
            result.append((f'return{index}', part(position, yaw-side*10, (-.71,.71,-.56,.56), padding)))
    return result


def intersects(poly, triangles, low, high):
    if (high.max(0) < poly.min(0)).any() or (low.min(0) > poly.max(0)).any():
        return 0
    tri = triangles[(high >= poly.min(0)).all(1) & (low <= poly.max(0)).all(1)]
    if not len(tri):
        return 0
    live = np.ones(len(tri), bool)
    for edge in np.roll(poly, -1, axis=0) - poly:
        axis = np.array([-edge[1], edge[0]])
        a, b = poly @ axis, tri @ axis
        live &= (b.max(1) >= a.min()) & (b.min(1) <= a.max())
    for i in range(3):
        edge = tri[:, (i+1) % 3] - tri[:, i]
        axis = np.stack([-edge[:, 1], edge[:, 0]], axis=1)
        a, b = np.einsum('ij,kj->ki', poly, axis), np.einsum('kij,kj->ki', tri, axis)
        live &= (b.max(1) >= a.min(1)) & (b.min(1) <= a.max(1))
    return int(live.sum())


def separated(a, b):
    for poly in (a, b):
        for edge in np.roll(poly, -1, axis=0)-poly:
            axis = np.array([-edge[1], edge[0]])
            x, y = a@axis, b@axis
            if x.max() <= y.min() or y.max() <= x.min():
                return True
    return False


def native_furniture(path, data_root, measurements, returns=0):
    rows = json.loads(path.read_text())
    if {row['level'] for row in rows} != {4, 5, 9, 10}:
        raise SystemExit('Native audit requires both Guildmaster and both campaign scene exports')
    verified = {}
    result = {'exportSha256': hashlib.sha256(path.read_bytes()).hexdigest(), 'levels': {}}

    def transform(row):
        frame = np.eye(4)
        rotation = np.array([0., 0., 0., 1.])
        for node in reversed(row['chain']):
            x, y, z, w = node['rotation']
            q = np.array([x, y, z, w])
            a, b = rotation[:3], q[:3]
            rotation = np.append(rotation[3]*b + q[3]*a + np.cross(a, b), rotation[3]*q[3] - a@b)
            r = np.array([[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w, 0],
                          [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w, 0],
                          [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y, 0], [0, 0, 0, 1.]])
            r[:3, :3] *= np.array(node['scale'])[None, :]
            r[:3, 3] = node['position']
            frame = frame @ r
        vertices = []
        for mesh in row['meshes']:
            c, e = np.array(mesh['center']), np.array(mesh['extent'])
            v = np.array([c+e*np.array([x, y, z]) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)])
            vertices.extend((np.column_stack((v, np.ones(8))) @ frame.T)[:, :3])
        return np.array(vertices), rotation

    # Both custom environments use identical production poses, so native furniture is
    # evaluated once per original scene, independent of the local reading-side angle.
    poses = [row for row in measurements if row[0] == 'cellar']
    forest = [row for row in measurements if row[0] == 'forest']
    for a, b in zip(poses, forest):
        if a[1:3] != b[1:3] or not np.allclose(np.array(a[3:], float), np.array(b[3:], float), atol=.0001):
            raise SystemExit('Native audit requires the same shared town layout in both rooms')
    for level in sorted(set(row['level'] for row in rows)):
        scene = [row for row in rows if row['level'] == level]
        hashes = set(row['levelSha256'] for row in scene)
        if len(hashes) != 1:
            raise SystemExit('Inconsistent native source hashes')
        expected = next(iter(hashes))
        source = data_root / ('level' + str(level))
        if hashlib.sha256(source.read_bytes()).hexdigest() != expected:
            raise SystemExit(f'Native scene {level} does not match measured source')
        verified[str(level)] = expected
        parchment = next(row for row in scene if row['name'] in ('GH_Map_Scene_WORLDMAP', 'GH_Campaign_Map'))
        v, rotation = transform(parchment)
        center = (v.min(0) + v.max(0)) * .5
        # Same widest-AABB/1.20 and quaternion Y-twist as MapRoomDriver/SkyAlternative.
        scale = np.clip(max((v.max(0)-v.min(0))[[0, 2]]) / 1.20, 1., 2000.)
        yaw = 2 * math.atan2(rotation[1], rotation[3]) if math.hypot(rotation[1], rotation[3]) > 1e-6 else 0.
        projection = np.array([[math.cos(yaw), math.sin(yaw)], [-math.sin(yaw), math.cos(yaw)]])
        measured, skipped, hits = [], [], []
        for row in scene:
            if row is parchment:
                continue
            if not row['authoredActiveHierarchy']:
                skipped.append(row['name'])
                continue
            v, _ = transform(row)
            xz = ((v-center)/scale)[:, [0, 2]] @ projection
            low, high = xz.min(0), xz.max(0)
            rectangle = np.array([[low[0], low[1]], [high[0], low[1]], [high[0], high[1]], [low[0], high[1]]])
            measured.append({'name': row['name'], 'minimum': low.tolist(), 'maximum': high.tolist()})
            for _, role, identifier, x, z, heading in poses:
                for part_name, poly in parts(np.array([float(x), float(z)]), float(heading), role, int(identifier), .05, returns):
                    if not separated(poly, rectangle):
                        hits.append([role, identifier, part_name, row['name']])
        result['levels'][str(level)] = {'scale': float(scale), 'parchmentCenter': center.tolist(),
                                      'furniture': measured, 'authoredInactive': skipped, 'contacts': hits}
        print('native', level, 'active furniture', len(measured), 'contacts', len(hits))
    result['sourceHashes'] = verified
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--environment-bundle', type=Path, required=True)
    parser.add_argument('--poses', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--room-expansion', type=float, default=1., help='Actual production horizontal room scale multiplier')
    parser.add_argument('--return-modules', type=int, default=0, help='64-card return tables per merchant workspace, including all four users')
    parser.add_argument('--visitor-count', type=int, choices=range(4), default=3, help='Additional simultaneous merchant workspaces')
    parser.add_argument('--merchant-layout-source', type=Path, default=Path(__file__).resolve().parents[1] / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantCounter.cs')
    parser.add_argument('--native-geometry', type=Path, help='Original native renderer bounds/ancestor export')
    parser.add_argument('--native-data-root', type=Path, help='Read-only original GH_Data to verify native scene hashes')
    args = parser.parse_args()
    if args.room_expansion < 1 or args.return_modules < 0:
        parser.error('Room expansion must be at least one; return count must be nonnegative')
    return_source_hash = None
    if args.return_modules:
        source = args.merchant_layout_source.read_text()
        compact = re.sub(r'\s+', '', source)
        expected = ('intpair=ordinal/2;', 'floatside=ordinal%2==0?-1f:1f;',
                    'position=newVector3(side*(2.85f+.25f*pair),0f,-.25f+pair*1.40f);',
                    'rotation=Quaternion.Euler(0f,-side*10f,0f);')
        if any(value not in compact for value in expected):
            parser.error('Production return layout changed; update the measured polygon transform binding')
        return_source_hash = hashlib.sha256(source.encode()).hexdigest()
    payload = args.environment_bundle.read_bytes()
    bundle_hash = hashlib.sha256(payload).hexdigest()
    env = UnityPy.load(payload)
    measurements = list(csv.reader(args.poses.read_text().splitlines()))
    measurements = [row for row in measurements if row[1] == 'resident' or int(row[2]) <= args.visitor_count]
    report = {'bundleSha256': bundle_hash, 'posesSha256': hashlib.sha256(args.poses.read_bytes()).hexdigest(),
              'paddingMetres': .05, 'roomExpansion':args.room_expansion,
              'returnModules':args.return_modules, 'returnLayoutSourceSha256':return_source_hash, 'visitorCount':args.visitor_count, 'rooms': {}, 'exclusions': list(NON_SOLID)}
    # Recent UnityPy loads external cabs lazily while constructing the container index.
    # Let that bounded discovery finish before taking the immutable audit snapshot.
    for attempt in range(4):
        try:
            containers = list(env.container.items())
            break
        except RuntimeError as error:
            if 'dictionary changed size during iteration' not in str(error) or attempt == 3:
                raise
    for key, asset in containers:
        label = 'forest' if key.endswith('env_swamp.prefab') else 'cellar' if key.endswith('env_cellar.prefab') else None
        if label is None:
            continue
        meshes = []
        room_meshes(asset.read(), np.eye(4), '', (9 if label == 'forest' else 6.5)/5.4, meshes,args.room_expansion)
        hits, placed = [], []
        rows = [row for row in measurements if row[0] == label]
        if len(rows) != 3 + args.visitor_count:
            raise SystemExit(f'{label}: expected three residents and requested visitor poses, got {len(rows)}')
        for _, role, identifier, x, z, yaw in rows:
            origin = np.array([float(x), float(z)])
            identifier = int(identifier)
            identity = role + identifier.__str__()
            for name, polygon in parts(origin, float(yaw), role, identifier, .05,args.return_modules):
                for mesh_name, height, triangles, low, high in meshes:
                    if height != (2.1 if name == 'actor' else 1.55):
                        continue
                    count = intersects(polygon, triangles, low, high)
                    if count:
                        hits.append([identity, name, mesh_name, count])
                for other, other_name, other_polygon in placed:
                    if (other != identity or name.startswith('return') or other_name.startswith('return')) and not separated(polygon, other_polygon):
                        hits.append([identity, name, other, other_name])
                placed.append((identity, name, polygon))
        report['rooms'][label] = {'poses': rows, 'meshSlices': len(meshes), 'triangleSlices': sum(len(t) for _, _, t, _, _ in meshes),
                                  'paddedParts': len(placed), 'contacts': hits}
        print(label, 'poses', len(rows), 'parts', len(placed), 'contacts', len(hits))
    if hashlib.sha256(args.environment_bundle.read_bytes()).hexdigest() != bundle_hash:
        raise SystemExit('Environment bundle changed during collision audit')
    if args.native_geometry:
        if not args.native_data_root:
            parser.error('--native-geometry requires --native-data-root')
        report['nativeFurniture'] = native_furniture(args.native_geometry, args.native_data_root, measurements, args.return_modules)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2)+'\n')
    if len(report['rooms']) != 2 or any(value['contacts'] for value in report['rooms'].values()):
        raise SystemExit('FAIL: room geometry contacts or missing room assets')
    if any(level['contacts'] for level in report.get('nativeFurniture', {}).get('levels', {}).values()):
        raise SystemExit('FAIL: native furniture contacts')
    print('PASS: actual scene triangles and simultaneously opened station envelopes')


if __name__ == '__main__':
    main()
