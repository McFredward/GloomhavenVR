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


def room_meshes(go, frame, path, ratio, output):
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
            triangles = v[faces]
            for high in (1.55, 2.1):
                q = triangles[(triangles[:, :, 1].max(1) > .04) & (triangles[:, :, 1].min(1) < high)][:, :, [0, 2]]
                if len(q):
                    output.append((path, high, q, q.min(1), q.max(1)))
    for child in t.m_Children:
        room_meshes(child.read().m_GameObject.read(), frame, path, ratio, output)


def part(origin, yaw, limits, padding):
    xlo, xhi, zlo, zhi = limits
    xlo -= padding; xhi += padding; zlo -= padding; zhi += padding
    angle = math.radians(yaw)
    direction = np.array([math.sin(angle), math.cos(angle)])
    right = np.array([direction[1], -direction[0]])
    return np.array([origin + right*x + direction*z for x, z in
                     ((xlo, zlo), (xhi, zlo), (xhi, zhi), (xlo, zhi))])


def parts(origin, yaw, role, service, padding):
    merchant = role == 'visitor' or service == 1
    spans = [('top', (-1.27, 1.27, -.422, .405) if merchant else (-.75, .75, -.362, .338))]
    if merchant:
        for x in (-.68, .68):
            spans.append(('drawer', (x-.538, x+.538, -.82, .416)))
    if role == 'resident':
        spans.append(('actor', (-.60, .60, .30, 1.20)))
    if role == 'resident' and service == 3:
        spans.append(('lantern', (.49, .87, .28, .86)))
    return [(name, part(origin, yaw, limits, padding)) for name, limits in spans]


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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--environment-bundle', type=Path, required=True)
    parser.add_argument('--poses', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    payload = args.environment_bundle.read_bytes()
    bundle_hash = hashlib.sha256(payload).hexdigest()
    env = UnityPy.load(payload)
    measurements = list(csv.reader(args.poses.read_text().splitlines()))
    report = {'bundleSha256': bundle_hash, 'posesSha256': hashlib.sha256(args.poses.read_bytes()).hexdigest(),
              'paddingMetres': .05, 'rooms': {}, 'exclusions': list(NON_SOLID)}
    for key, asset in env.container.items():
        label = 'forest' if key.endswith('env_swamp.prefab') else 'cellar' if key.endswith('env_cellar.prefab') else None
        if label is None:
            continue
        meshes = []
        room_meshes(asset.read(), np.eye(4), '', (9 if label == 'forest' else 6.5)/5.4, meshes)
        hits, placed = [], []
        rows = [row for row in measurements if row[0] == label]
        if len(rows) != 6:
            raise SystemExit(f'{label}: expected three residents and three visitor poses, got {len(rows)}')
        for _, role, identifier, x, z, yaw in rows:
            origin = np.array([float(x), float(z)])
            identifier = int(identifier)
            identity = role + identifier.__str__()
            for name, polygon in parts(origin, float(yaw), role, identifier, .05):
                for mesh_name, height, triangles, low, high in meshes:
                    if height != (2.1 if name == 'actor' else 1.55):
                        continue
                    count = intersects(polygon, triangles, low, high)
                    if count:
                        hits.append([identity, name, mesh_name, count])
                for other, other_name, other_polygon in placed:
                    if other != identity and not separated(polygon, other_polygon):
                        hits.append([identity, name, other, other_name])
                placed.append((identity, name, polygon))
        report['rooms'][label] = {'poses': rows, 'meshSlices': len(meshes), 'triangleSlices': sum(len(t) for _, _, t, _, _ in meshes),
                                  'paddedParts': len(placed), 'contacts': hits}
        print(label, 'poses', len(rows), 'parts', len(placed), 'contacts', len(hits))
    if hashlib.sha256(args.environment_bundle.read_bytes()).hexdigest() != bundle_hash:
        raise SystemExit('Environment bundle changed during collision audit')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2)+'\n')
    if len(report['rooms']) != 2 or any(value['contacts'] for value in report['rooms'].values()):
        raise SystemExit('FAIL: room geometry contacts or missing room assets')
    print('PASS: actual scene triangles and simultaneously opened station envelopes')


if __name__ == '__main__':
    main()
