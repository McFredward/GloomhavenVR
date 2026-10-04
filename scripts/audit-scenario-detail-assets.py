#!/usr/bin/env python3
"""Read all original actor/scenery prefab identities without modifying game assets.

Run with UnityPy installed. This is a provenance census, not an FPS benchmark.
Only exported model hierarchies count as resident cosmetics; separate attack,
condition, projectile and loot prefabs in the same bundle are not model children.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

import UnityPy


def original_models(env):
    seen = set()
    for address, pointer in env.container.items():
        if pointer.type.name != 'GameObject' or '/Characters/' not in address:
            continue
        model = pointer.read()
        if pointer.path_id not in seen:
            seen.add(pointer.path_id)
            yield address, pointer, model.m_Name


def hierarchy(root):
    stack = [(root, [])]
    while stack:
        pointer, ancestry = stack.pop()
        obj = pointer.read()
        path = ancestry + [obj.m_Name]
        components = [slot.component for slot in obj.m_Component]
        yield path, components
        for pointer in components:
            if pointer.type.name == 'Transform':
                for child in reversed(pointer.read().m_Children):
                    stack.append((child.read().m_GameObject, path))
                break


def renderer(component):
    data = component.read()
    materials = []
    for pointer in data.m_Materials:
        try:
            materials.append(pointer.read().m_Name)
        except Exception:
            materials.append('<external>')
    return {'kind': component.type.name, 'materials': materials}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--provenance-output', type=Path, help='Write compact checked-in identity metadata, never artwork/geometry')
    parser.add_argument('--actors-only', action='store_true')
    args = parser.parse_args()
    bundles = args.source_root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'
    result = {'unitypy': UnityPy.__version__, 'actor_bundles': [], 'models': [], 'scenery_bundles': [], 'scenery_names': []}
    actor_paths = sorted(path for path in bundles.glob('*_assets_all.bundle')
                         if path.name.startswith(('hero_', 'npc_')))
    for index, path in enumerate(actor_paths):
        env = UnityPy.load(str(path))
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        result['actor_bundles'].append({'bundle': path.name, 'sha256': digest})
        for address, model, name in original_models(env):
            record = {'bundle': path.name, 'address': address, 'model': name, 'particles': [], 'mesh_fx': [], 'renderers': []}
            for names, components in hierarchy(model):
                route = '/'.join(names)
                resident_idle = any(re.search(r'(?:^P_.*(?:Idle|idle)|^P_FlameDemon$)', n) for n in names[1:])
                for component in components:
                    kind = component.type.name
                    if kind == 'ParticleSystem':
                        data = component.read()
                        record['particles'].append({'path': route, 'idle': resident_idle,
                            'loop': data.looping, 'stop_action': data.stopAction,
                            'collision': data.CollisionModule.enabled, 'trigger': data.TriggerModule.enabled})
                    elif kind in ('MeshRenderer', 'SkinnedMeshRenderer', 'TrailRenderer', 'LineRenderer'):
                        fact = renderer(component)
                        fact.update({'path': route, 'idle': resident_idle})
                        record['renderers'].append(fact)
                        if resident_idle:
                            record['mesh_fx'].append(fact)
            result['models'].append(record)
        if (index + 1) % 25 == 0:
            print(f'Actors: {index + 1}/{len(actor_paths)}', flush=True)
    if not args.actors_only:
        names = set()
        # Native PCG databases live in a nested addressable path, not alongside the
        # many material-only bundles. Missing rglob here would audit just 28 names.
        paths = sorted(path for path in bundles.rglob('*.bundle') if path.name.startswith('pcg_'))
        for index, path in enumerate(paths):
            env = UnityPy.load(str(path))
            result['scenery_bundles'].append({'bundle': str(path.relative_to(bundles)), 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
            for obj in env.objects:
                if obj.type.name in ('GameObject', 'Mesh'):
                    names.add(obj.read().m_Name)
            if (index + 1) % 100 == 0:
                print(f'Scenery: {index + 1}/{len(paths)}', flush=True)
        result['scenery_names'] = sorted(names)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    if args.provenance_output:
        compact = {'unitypy': result['unitypy'], 'actor_bundles': result['actor_bundles'],
            'models': [{key: record[key] for key in ('bundle', 'model', 'particles', 'mesh_fx')}
                       for record in result['models']],
            'scenery_bundles': [record for record in result['scenery_bundles'] if '.asset.bundle' in record['bundle']],
            'scenery_names': result['scenery_names']}
        args.provenance_output.parent.mkdir(parents=True, exist_ok=True)
        args.provenance_output.write_text(json.dumps(compact, separators=(',', ':')) + '\n')
    print(f'Wrote {len(result["models"])} original model hierarchies and {len(result["scenery_names"])} scenery identities to {args.output}')


if __name__ == '__main__':
    main()
