#!/usr/bin/env python3
"""Audit original character authoring without Unity or production projection code.

Run with the developer UnityPy interpreter and the read-only game reference. The
report follows each CharacterManager's actual Addressables GUID to its visual
prefab, records the original transform/bone/mesh identities, and decodes mesh
vertices only to retain scalar bounds. It does not export geometry or artwork.

The imported, unanimated mesh frame is evidence about authored units and roots;
it is not a live skinned/cloth silhouette or an envelope of every animation.
No mesh-name/body-name classifier, character-size table, fit, or centering rule
is used. All skinned renderer members, including equipment and LODs, are listed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, str(Path(__file__).resolve().parent / 'menu-depth'))
from addressables import locations

import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.MeshHelper import MeshHandler
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeNode import TypeTreeNode


def sha(path: Path) -> str:
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, allow_nan=False) + '\n')


def vector(value, axes='xyz') -> list[float]:
    return [float(getattr(value, axis)) for axis in axes]


def identity() -> list[list[float]]:
    return [[float(i == j) for j in range(4)] for i in range(4)]


def multiply(a, b):
    return [[sum(a[i][k] * b[k][j] for k in range(4)) for j in range(4)] for i in range(4)]


def transform(t):
    x, y, z, w = vector(t.m_LocalRotation, 'xyzw')
    scale, position = vector(t.m_LocalScale), vector(t.m_LocalPosition)
    matrix = [[1-2*y*y-2*z*z, 2*x*y-2*z*w, 2*x*z+2*y*w, position[0]],
              [2*x*y+2*z*w, 1-2*x*x-2*z*z, 2*y*z-2*x*w, position[1]],
              [2*x*z-2*y*w, 2*y*z+2*x*w, 1-2*x*x-2*y*y, position[2]],
              [0., 0., 0., 1.]]
    for i in range(3):
        for j in range(3):
            matrix[i][j] *= scale[j]
    return matrix


def bounds(points):
    if not points:
        return None
    return {'min': [min(p[i] for p in points) for i in range(3)],
            'max': [max(p[i] for p in points) for i in range(3)]}


def aabb(value):
    center, extent = vector(value.m_Center), vector(value.m_Extent)
    return {'min': [c-e for c, e in zip(center, extent)],
            'max': [c+e for c, e in zip(center, extent)]}


def authored_tree(reader):
    if reader.serialized_type.nodes:
        return reader.read_typetree()
    # UnityPy's reflection generator calls List<string> itself "string" and
    # omits serialized boolean alignment in this native no-typetree corpus.
    # Rebuild private nodes from the exact reflected schema, then require full
    # object-byte consumption. Never relax check_read or mutate cached nodes.
    nodes = []
    for original in reader._get_typetree_node(None).to_dict_list():
        node = {key: value for key, value in original.items() if key != 'm_Children'}
        children = original['m_Children']
        if (node['m_Type'] == 'string' and children and children[0].m_Name == 'Array'
                and children[0].m_Children[-1].m_Type == 'string'):
            node['m_Type'] = 'vector'
        if node['m_Type'] == 'UInt8':
            node['m_MetaFlag'] = node.get('m_MetaFlag', 0) | 16384
        nodes.append(node)
    return reader.read_typetree(nodes=TypeTreeNode.from_list(nodes))


def pointer(raw, reader):
    return PPtr(m_FileID=raw['m_FileID'], m_PathID=raw['m_PathID'], assetsfile=reader.assets_file)


def object_id(reader):
    return {'serialized_file': reader.assets_file.name, 'path_id': reader.path_id,
            'object_sha256': hashlib.sha256(reader.get_raw_data()).hexdigest()}


def trs(t):
    return {'position': vector(t.m_LocalPosition), 'rotation_xyzw': vector(t.m_LocalRotation, 'xyzw'),
            'scale': vector(t.m_LocalScale), **object_id(t.object_reader)}


def game_transform(go):
    return next(slot.component.read() for slot in go.m_Component
                if slot.component.type.name in ('Transform', 'RectTransform'))


def model_profile(reference, mesh_cache):
    renderers, animators, transforms = [], [], []
    root = reference.read()

    def visit(ref, parent, route, active):
        go = ref.read()
        components = [slot.component for slot in go.m_Component]
        t = game_transform(go)
        frame = multiply(parent, transform(t))
        path = route + [go.m_Name]
        active = active and bool(go.m_IsActive)
        transforms.append({'path': '/'.join(path), 'local': trs(t), 'matrix_in_prefab_root': frame})
        for component in components:
            kind = component.type.name
            if kind == 'Animator':
                data = component.read()
                animators.append({'path': '/'.join(path), **object_id(component.deref()),
                                  'avatar_path_id': data.m_Avatar.path_id,
                                  'controller_path_id': data.m_Controller.path_id})
            if kind != 'SkinnedMeshRenderer':
                continue
            data = component.read()
            if not data.m_Mesh.path_id:
                renderers.append({'path': '/'.join(path), **object_id(component.deref()), 'mesh': None})
                continue
            mesh_reader = data.m_Mesh.deref()
            key = (mesh_reader.assets_file.name, mesh_reader.path_id)
            if key not in mesh_cache:
                mesh = mesh_reader.read()
                handler = MeshHandler(mesh)
                handler.process()
                vertices = handler.m_Vertices or []
                if not vertices and mesh.m_VertexData.m_VertexCount:
                    raise ValueError('Nonempty native mesh did not decode: ' + mesh.m_Name)
                mesh_cache[key] = (vertices, {'name': mesh.m_Name, **object_id(mesh_reader),
                    'vertices': len(vertices), 'bind_pose_count': len(mesh.m_BindPose),
                    'mesh_local_aabb': aabb(mesh.m_LocalAABB), 'decoded_vertex_bounds': bounds(vertices)})
            vertices, metadata = mesh_cache[key]
            x, y, z = frame[:3]
            points = [[x[0]*v[0]+x[1]*v[1]+x[2]*v[2]+x[3],
                       y[0]*v[0]+y[1]*v[1]+y[2]*v[2]+y[3],
                       z[0]*v[0]+z[1]*v[1]+z[2]*v[2]+z[3]] for v in vertices]
            bone = data.m_RootBone.read() if data.m_RootBone.path_id else None
            renderers.append({'path': '/'.join(path), **object_id(component.deref()),
                'serialized_active_ancestry': active, 'serialized_enabled': bool(data.m_Enabled),
                'cloth_component': any(c.type.name == 'Cloth' for c in components),
                'mesh': metadata, 'imported_vertices_in_prefab_root': bounds(points),
                'renderer_local_aabb': aabb(data.m_AABB),
                'root_bone': None if bone is None else {'name': bone.m_GameObject.read().m_Name, 'local': trs(bone)},
                'bone_path_ids': [b.path_id for b in data.m_Bones]})
        for child in t.m_Children:
            visit(child.read().m_GameObject, frame, path, active)

    visit(reference, identity(), [], True)
    return {'name': root.m_Name, **object_id(reference.deref()), 'root_local': trs(game_transform(root)),
            'transforms': transforms, 'animators': animators, 'skinned_renderers': renderers}


def native_apertures(source_files, generator):
    candidates, targets, managers = [], set(), []
    for path in source_files:
        environment = UnityPy.load(str(path))
        environment.typetree_generator = generator
        for reader in environment.objects:
            if reader.type.name != 'MonoBehaviour':
                continue
            head = reader.parse_monobehaviour_head()
            if not head.m_Script.path_id:
                continue
            kind = head.m_Script.read().m_ClassName
            if kind == 'Character3DDisplayManager':
                go = head.m_GameObject.read()
                camera = next(c.component.read() for c in go.m_Component if c.component.type.name == 'Camera')
                target = camera.m_TargetTexture.deref()
                targets.add((target.assets_file.name, target.path_id))
                data = authored_tree(reader)
                holder = pointer(data['character3DHolder'], reader).read()
                managers.append({'source': path.name, **object_id(reader), 'camera_root': trs(game_transform(go)),
                                 'holder': trs(holder), 'target': object_id(target)})
            elif kind == 'RawImage':
                data = authored_tree(reader)
                if data['m_Texture']['m_PathID']:
                    candidates.append((path.name, reader, head, data))
    result = []
    for source, reader, head, data in candidates:
        texture_reader = pointer(data['m_Texture'], reader).deref()
        if (texture_reader.assets_file.name, texture_reader.path_id) not in targets:
            continue
        texture = texture_reader.read()
        t = game_transform(head.m_GameObject.read())
        ancestry = []
        while True:
            go = t.m_GameObject.read()
            row = {'name': go.m_Name, 'local': trs(t)}
            if hasattr(t, 'm_SizeDelta'):
                row.update({'size_delta': vector(t.m_SizeDelta, 'xy'),
                            'anchor_min': vector(t.m_AnchorMin, 'xy'), 'anchor_max': vector(t.m_AnchorMax, 'xy')})
            for slot in go.m_Component:
                if slot.component.type.name != 'MonoBehaviour':
                    continue
                h = slot.component.deref().parse_monobehaviour_head()
                if not h.m_Script.path_id:
                    continue
                kind = h.m_Script.read().m_ClassName
                if kind in ('AspectRatioFitter', 'CanvasScaler', 'Mask', 'RectMask2D'):
                    d = authored_tree(slot.component.deref())
                    row[kind] = {k: v for k, v in d.items() if k in (
                        'm_AspectRatio', 'm_AspectMode', 'm_ReferenceResolution', 'm_UiScaleMode', 'm_MatchWidthOrHeight')}
            ancestry.append(row)
            if not t.m_Father.path_id:
                break
            t = t.m_Father.read()
        result.append({'source': source, **object_id(reader),
            'path': '/'.join(row['name'] for row in reversed(ancestry)),
            'texture': {'name': texture.m_Name, **object_id(texture_reader),
                        'width': texture.m_Width, 'height': texture.m_Height},
            'uv_rect': data['m_UVRect'], 'ancestry': ancestry})
    return managers, result


def audit(root: Path):
    data_root = root / 'ressources/GH_Data'
    bundles = data_root / 'StreamingAssets/aa/StandaloneWindows64'
    catalog = data_root / 'StreamingAssets/aa/catalog.json'
    _, records = locations(json.loads(catalog.read_text()))
    guid_paths = {}
    for row in records:
        for key in row['keys']:
            if isinstance(key, str) and re.fullmatch('[0-9a-fA-F]{32}', key) and row['internal_id'].startswith('Assets/'):
                if key in guid_paths and guid_paths[key] != row['internal_id']:
                    raise ValueError('Conflicting native GUID: ' + key)
                guid_paths[key] = row['internal_id']
    generator = TypeTreeGenerator('2021.3.5f1')
    generator.load_local_dll_folder(str(data_root / 'Managed'))
    scene = UnityPy.load(str(data_root / 'level1'))
    scene.typetree_generator = generator
    tools = next(o for o in scene.objects if o.path_id == 11386)
    owner = authored_tree(tools)
    if pointer(owner['m_Script'], tools).read().m_ClassName != 'UIInfoTools':
        raise ValueError('The pinned native scene owner is no longer UIInfoTools')
    configs = []

    def config(reader, source, address=None):
        d = authored_tree(reader)
        configs.append({'source': source, 'address': address, **object_id(reader), **{
            k: d[k] for k in ('m_Name', 'character', 'alternativeSkins', 'customAssemblyCharacter3DPosition',
                'guildmasterAssemblyCharacterPlacement', 'campaignAssemblyCharacterPlacement', 'assemblyCharacter3DCompanions')}})

    for ref in owner['characterConfigsUI']:
        reader = pointer(ref, tools).deref()
        config(reader, reader.assets_file.name)
    dlc_configs = sorted(bundles.glob('dlc_*_misc_gui_assets_all.bundle'))
    for path in dlc_configs:
        environment = UnityPy.load(str(path))
        for address, ref in environment.container.items():
            if ref.type.name != 'MonoBehaviour':
                continue
            if ref.deref().parse_monobehaviour_head().m_Script.read().m_ClassName == 'CharacterConfigUI':
                config(ref.deref(), path.name, address)
    paths = sorted([*bundles.glob('hero_*_assets_all.bundle'), *bundles.glob('dlc_*_hero_*_assets_all.bundle'),
                    bundles / 'npc_beasttyrantbear_assets_all.bundle'])
    models, mesh_cache = [], {}
    for path in paths:
        environment = UnityPy.load(str(path))
        by_address = {address.lower(): ref for address, ref in environment.container.items()}
        for address, ref in environment.container.items():
            if ref.type.name != 'GameObject':
                continue
            go = ref.read()
            for slot in go.m_Component:
                if slot.component.type.name != 'MonoBehaviour':
                    continue
                reader = slot.component.deref()
                head = reader.parse_monobehaviour_head()
                if head.m_Script.read().m_ClassName != 'CharacterManager':
                    continue
                d = authored_tree(reader)
                guid = d['CharacterChildPrefabReference']['m_AssetGUID']
                child_address = guid_paths[guid]
                child = by_address[child_address.lower()]
                models.append({'bundle': path.name, 'address': address, 'name': go.m_Name,
                    'manager': object_id(reader), 'root': trs(game_transform(go)), 'native_height': d['Height'],
                    'child_guid': guid, 'child_address': child_address,
                    'weapon_lists': d['WeaponLists'], 'visual': model_profile(child, mesh_cache)})
    scene_paths = sorted([p for p in data_root.glob('level[0-9]*') if '.' not in p.name]
                         + list(data_root.glob('sharedassets*.assets')) + [data_root / 'resources.assets'])
    managers, apertures = native_apertures(scene_paths, generator)
    inputs = [catalog, *paths, *dlc_configs, *scene_paths, data_root / 'globalgamemanagers.assets']
    inputs += [data_root / 'Managed' / name for name in (
        'GH.Runtime.dll', 'ScenarioRuleLibrary.dll', 'UnityEngine.UI.dll', 'UnityEngine.CoreModule.dll',
        'Unity.Addressables.dll', 'Unity.ResourceManager.dll', 'mscorlib.dll')]
    sources = [root / 'decompiled/GH.Runtime' / name for name in (
        'Character3DDisplayManager.cs', 'CharacterManager.cs', 'CharacterConfigUI.cs', 'AssetBundleManager.cs')]
    pins = [{'path': str(p.relative_to(root)), 'bytes': p.stat().st_size, 'sha256': sha(p)}
            for p in sorted(set(inputs + sources))]
    return {'schema': 1, 'auditor_sha256': sha(Path(__file__)),
        'addressables_decoder_sha256': sha(Path(__file__).parent / 'menu-depth/addressables.py'),
        'unitypy_version': UnityPy.__version__, 'unity_version': '2021.3.5f1',
        'scope': 'Original base/DLC hero forms and their skins, configured BeastTyrant bear, native placement configs and camera-target-bound preview widgets.',
        'limits': 'Read-only native authoring audit; imported mesh vertices and serialized transforms are not animated/cloth silhouettes, runtime fitting, headset or multiplayer evidence.',
        'native_order_contract': 'Character3DDisplayManager.CreateCharacter appends the main CharacterManager first, then configured companions. CreateModel preserves prefab scale and sets native placement and rotation.',
        'inputs': pins, 'config_owner': object_id(tools), 'configs': configs, 'models': models,
        'native_displays': managers, 'native_apertures': apertures}


def compact(report):
    return {key: value for key, value in report.items() if key not in ('models', 'native_apertures')} | {
        'models': [{key: value for key, value in model.items() if key not in ('visual', 'weapon_lists')} | {
            'visual_root': model['visual']['root_local'],
            'skinned_meshes': [{'path': r['path'], 'path_id': r['path_id'], 'mesh': r['mesh'],
                               'imported_vertices_in_prefab_root': r.get('imported_vertices_in_prefab_root')}
                              for r in model['visual']['skinned_renderers']]}
                   for model in report['models']],
        'native_apertures': [{key: value for key, value in aperture.items() if key != 'ancestry'} | {
            'layout_controls': [{key: value for key, value in row.items() if key != 'local'}
                for row in aperture['ancestry'] if any(key in row for key in (
                    'AspectRatioFitter', 'CanvasScaler', 'Mask', 'RectMask2D'))]}
            for aperture in report['native_apertures']]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--provenance-output', type=Path)
    parser.add_argument('--verify', type=Path, help='Compare exact native metadata against a retained audit.')
    args = parser.parse_args()
    report = audit(args.source_root.resolve())
    metadata = compact(report)
    write(args.output_dir / 'report.json', report)
    write(args.output_dir / 'native-authoring.json', metadata)
    if args.verify and json.loads(args.verify.read_text()) != metadata:
        raise ValueError('Native authoring metadata differs from the retained audit')
    if args.provenance_output:
        write(args.provenance_output, metadata)
    print(json.dumps({'status': 'verified' if args.verify else 'audited', 'configs': len(report['configs']),
        'model_forms': len(report['models']), 'native_displays': len(report['native_displays']),
        'native_apertures': len(report['native_apertures']), 'input_pins': len(report['inputs']),
        'skinned_renderers': sum(len(m['visual']['skinned_renderers']) for m in report['models']),
        'evidence': str(args.output_dir.resolve())}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
