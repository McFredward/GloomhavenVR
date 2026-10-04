#!/usr/bin/env python3
"""Read all original actor/scenery prefab identities without modifying game assets.

Run with UnityPy installed. This is a provenance census, not an FPS benchmark.
Only exported model hierarchies count as resident cosmetics; separate attack,
condition, projectile and loot prefabs in the same bundle are not model children.
"""
import argparse
from collections import Counter
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


def scenery_renderer(component):
    """Record each real PCG mesh separately from the enclosing floor/wall prefab."""
    data = component.read()
    game_object = data.m_GameObject.read()
    components = [slot.component for slot in game_object.m_Component]
    mesh_filter = next((pointer.read() for pointer in components if pointer.type.name == 'MeshFilter'), None)
    mesh_name = '<external>'
    if mesh_filter is not None:
        try:
            mesh_name = mesh_filter.m_Mesh.read().m_Name
        except Exception:
            pass
    transform = next((pointer.read() for pointer in components if pointer.type.name == 'Transform'), None)
    route = [game_object.m_Name]
    ancestry_components = set()
    while transform is not None and transform.m_Father.path_id:
        transform = transform.m_Father.read()
        parent = transform.m_GameObject.read()
        route.append(parent.m_Name)
        ancestry_components.update(slot.component.type.name for slot in parent.m_Component)
    return {'path': '/'.join(reversed(route)), 'mesh': mesh_name,
            'components': sorted(pointer.type.name for pointer in components),
            'ancestry_components': sorted(ancestry_components)}


def small_dressing_candidate(name):
    """Select review candidates; runtime admits only the resulting closed mesh identities."""
    lower = name.lower()
    excluded = ('skinbone', 'skeleton', 'coffin', 'сoffin', 'cross', 'corpse', 'cadaver',
        'burns', '_puddles', 'lava', 'volcanic', '_wall', '_pillar', '_floor_basic',
        '_floor_base', 'terrain_', '_banner', '_altar', 'large_stone_urn',
        '_shelves_stone', '_floorshelf_stone', '_smallshelf_stone', '_wallshelf_stone',
        'grass', 'plants', 'leaves', 'foliage', '(clone)', ' simplified mesh')
    if any(token in lower for token in excluded if not (token == '_wall' and 'wallchains' in lower)): return False
    tokens = ('clutter', 'scatter', 'debris', 'skull', 'bonepile', '_bone', '_paper',
        '_pages', 'parchment', '_scroll', '_cup', '_pot', '_urn', '_vase', '_bottle',
        '_book', 'wallchains', 'cobweb', '_jugs', '_plate', '_oiler', '_balance',
        '_flask', '_inkpot', '_potion')
    return any(token in lower for token in tokens) or lower.startswith(
        ('book_', 'bottle_', 'flask_', 'inkpot_', 'potion_'))


def component_fact(pointer):
    kind = pointer.type.name
    data = pointer.read()
    fact = {'type': kind}
    if kind == 'MonoBehaviour':
        try: fact['script'] = data.m_Script.read().m_ClassName
        except Exception: fact['script'] = '<external>'
    elif kind == 'Animator':
        fact['controller'] = data.m_Controller.path_id
    elif kind.endswith('Collider'):
        fact['trigger'] = data.m_IsTrigger
        fact['enabled'] = data.m_Enabled
    return fact


def enrich_small_dressing(result, bundles):
    """Read the actual hierarchy for small meshes and retained mixed-composite members.

    All stored bundle hashes are checked before reusing an older complete census. This
    avoids repeatedly decoding material-only bundles without trusting stale identities.
    """
    names = {row['mesh'] for row in result['scenery_renderers'] if small_dressing_candidate(row['mesh'])}
    names -= {row['mesh'] for row in scenery_review(result)['composite_dressing']}
    selected = sorted({row['bundle'] for row in result['scenery_renderers'] if row['mesh'] in names})
    records = []
    for name in selected:
        env = UnityPy.load(str(bundles / name))
        for reader in env.objects:
            if reader.type.name != 'MeshRenderer': continue
            data = reader.read(); obj = data.m_GameObject.read()
            pointers = [slot.component for slot in obj.m_Component]
            filter = next((pointer.read() for pointer in pointers if pointer.type.name == 'MeshFilter'), None)
            if filter is None: continue
            try: mesh = filter.m_Mesh.read()
            except Exception: continue
            if mesh.m_Name not in names: continue
            extent = mesh.m_LocalAABB.m_Extent
            size = [2 * extent.x, 2 * extent.y, 2 * extent.z]
            # An identity describing a whole large body/building is not small floor/shelf clutter.
            if size[1] > 2 or max(size) > 3: continue
            if any(token in mesh.m_Name.lower() for token in ('vase', 'urn', 'pot')) and size[1] > 1: continue
            transform = next(pointer.read() for pointer in pointers if pointer.type.name == 'Transform')
            chain = []
            while True:
                node = transform.m_GameObject.read()
                chain.append({'name': node.m_Name,
                    'components': [component_fact(slot.component) for slot in node.m_Component]})
                if not transform.m_Father.path_id: break
                transform = transform.m_Father.read()
            fact = scenery_renderer(reader)
            fact.update({'bundle': name, 'size': size, 'chain': chain})
            records.append(fact)
    result['small_dressing_containers'] = sorted({row['chain'][-1]['name'] for row in records})
    representatives = {}
    for row in records:
        # Prefer a normal generated-decoration copy over an obstacle/damage-dependent copy.
        scripts = [f.get('script', '') for node in row['chain'] for f in node['components']]
        score = sum(script not in ('MaterialLoader', 'DetailsDisabler', 'ImportantObjectsShadowsDisabler',
            'PropObjectsShadowsDisabler', 'DetailLevelDisableProvider') for script in scripts)
        prior = representatives.get(row['mesh'])
        if prior is None or score < prior[0]: representatives[row['mesh']] = (score, row)
    return [representatives[name][1] for name in sorted(representatives)]


def scenery_review(result):
    """Document candidate semantics separately from the runtime gameplay/collision vetoes.

    A role in this census is not permission to hide every enclosing prefab. Anonymous,
    effect, gameplay and structural names remain explicit retained categories. The small
    detached-composite list is supported by actual original mesh/child records, rather
    than allowing every object whose name contains "Bone" or "Rubble".
    """
    extra_composites = {'CR_ST_FloorShelf_Stone_Bone_Skull', 'CR_ST_Shelves_Stone_Bone_Bone',
        'CR_ST_SmallShelf_Stone_Bone_Bone', 'CR_ST_WallShelf_Stone_Bone_Bone',
        'CR_ST_WallShelf_Stone_Bone_Skull', 'TERRAIN_Crypt_Rubble_Bits',
        'TERRAIN_DU_Rubble_Skulls', 'TERRAIN_Town_Ext_Rubble_Bits',
        'TERRAIN_Town_Rubble_Bits', 'TERR_Forest_Rubble_Stones'}
    meshes = {row['mesh'] for row in result['scenery_renderers'] if
        (row['mesh'].startswith(('CR_OS_Floor', 'CR_OS_Pillar', 'CR_OS_Wall'))
         and any(token in row['mesh'] for token in ('_Bone', '_Skull')))
        or row['mesh'].startswith('CR_INT_Stone_Floor_01_Rubble_')
        or row['mesh'] in extra_composites}
    dressing = ('clutter', 'scatter', 'debris', 'skull', 'bonepile', '_bone', '_paper', '_pages',
        'parchment', '_scroll', '_cup', '_pot', '_urn', '_vase', '_bottle', '_barrel', '_book',
        '_candle', '_banner', '_flag', '_carpet', '_curtain', '_furniture', '_chair',
        'candelabra', 'wallchains', 'cobweb', '_smallrock', '_stones')
    vegetation = ('_tree', 'bush', '_plants', '_vines', '_ivy', '_roots', '_leaves', '_fern',
        '_shrub', '_reed', '_flower', '_foliage', '_geranium', '_moss')
    geometry = ('_wall', '_pillar', '_door', '_arch', '_floor', '_platform', '_roof', '_bridge', 'terrain', '_stoneblock')
    gameplay = ('_obst', '_chest', 'treasurecrate', '_prop_scen', '_destruct', 'doorlock')
    def clean(name):
        return re.sub(r'(?: \(\d+\)|\(Clone\)| simplified mesh)$', '', name)
    small_names = {row['mesh'] for row in result.get('small_dressing', [])}
    def role(name):
        original = clean(name)
        name = original.lower()
        if original in meshes: return 'detached-composite-dressing'
        if original in small_names: return 'exact-small-dressing'
        if name.startswith('pcg_'): return 'generation-container'
        if any(token in name for token in gameplay): return 'gameplay-protected'
        if any(token in name for token in ('water', 'toxic', 'lava', 'sludge', 'hotcoals')): return 'water-or-effect-protected'
        if any(token in name for token in ('_grass', '_longgrass', '_grassy')): return 'grass-candidate-with-core-veto'
        if any(token in name for token in vegetation): return 'vegetation-candidate-with-core-veto'
        if any(token in name for token in dressing): return 'dressing-candidate-with-core-veto'
        if any(token in name for token in geometry): return 'structural-or-ground-protected'
        return 'unknown-preserved'
    composites = []
    for mesh in sorted(meshes):
        candidates = [row for row in result['scenery_renderers'] if row['mesh'] == mesh]
        example = next((row for row in candidates if 'MonoBehaviour' not in row['components']
            and 'Animator' not in row['ancestry_components'] and len(row['path'].split('/')) > 1), candidates[0])
        composites.append(example)
    # Closed original identities establish the retained ground mesh behind a shared
    # prefab collider. This is collision representation, never a cosmetic admission.
    ground_exclusions = ('Grass', 'Grassy', 'LongGrass', 'Tree', 'Bush', 'Plants', 'Vines',
        'Ivy', 'Roots', 'Leaves', 'Fern', 'Shrub', 'Reed', 'Flower', 'Foliage', 'Geranium',
        'Moss', 'Scatter', 'Detail', 'Clutter', 'Stalagmites', 'Crystal', 'Debris',
        'SmallRock', 'Stones', 'Vase', 'Urn', 'Pot', 'Barrel', 'Bottle', 'Candle', 'Book',
        'Banner', 'Skull', 'Bone', 'Paper', 'Pages', 'Parchment', 'Scroll', 'Cup', 'Carpet',
        'Curtain', 'Furniture', 'Chair', 'Candelabra', 'WallChains', 'Cobweb', 'Rubble')
    ground = {}
    for row in result['scenery_renderers']:
        mesh = row['mesh']
        if (mesh.startswith(('FR_', 'CR_', 'CV_', 'EN_', 'TO_', 'ST_', 'SB_', 'SE_',
                             'CS_', 'CT_', 'DLC_', 'PR_', 'GH_'))
            and '_Floor_' in mesh and not any(token in mesh for token in ground_exclusions)):
            ground.setdefault(mesh, row)
    projectors = {}
    for row in result['scenery_projectors']:
        projectors.setdefault(clean(row['path'].split('/')[-1]), row)
    classification = [[name, role(name)] for name in result['scenery_names']]
    return {'bundle_count': len(result['scenery_bundles']), 'renderer_count': len(result['scenery_renderers']),
        'projector_count': len(result['scenery_projectors']), 'classification': classification,
        'counts': dict(Counter(category for _, category in classification)),
        'composite_dressing': composites, 'ground_cores': [ground[name] for name in sorted(ground)],
        'projectors': list(projectors.values()), 'small_dressing': result.get('small_dressing', []),
        'small_dressing_containers': result.get('small_dressing_containers', []),
        'retained_furniture_cores': [row for mesh, row in sorted({row['mesh']: row for row in result['scenery_renderers']
            if (re.fullmatch(r'CR_ST_Shelf_(?:Alchemy|Books|Scrolls|Skulls|Pots|UrnsPots|Urns)_\d+', row['mesh'])
                or (row['mesh'].startswith(('CR_', 'TO_', 'FR_', 'CV_', 'ST_', 'DLC_', 'SE_', 'SB_', 'CT_', 'CS_', 'EN_', 'PR_', 'GH_'))
                    and any(token in row['mesh'] for token in ('_Table', '_Chair', '_Bench', '_Furniture',
                        '_Shelves_Stone', '_WallShelf_Stone', '_FloorShelf_Stone', '_SmallShelf_Stone'))
                    and '_Props_' not in row['mesh'] and 'simplified mesh' not in row['mesh']))
            and row['mesh'] not in {item['mesh'] for item in result.get('small_dressing', [])}
            and row['mesh'] not in meshes}.items())]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--provenance-output', type=Path, help='Write compact checked-in identity metadata, never artwork/geometry')
    parser.add_argument('--scenery-input', type=Path, help='Reuse a complete census only after checking every original bundle hash')
    parser.add_argument('--actors-only', action='store_true')
    parser.add_argument('--scenery-only', action='store_true', help='Skip the independent actor census')
    parser.add_argument('--scenery-renderers', action='store_true', help='Include original PCG renderer ancestry and mesh identities')
    args = parser.parse_args()
    bundles = args.source_root / 'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'
    result = {'unitypy': UnityPy.__version__, 'actor_bundles': [], 'models': [], 'scenery_bundles': [], 'scenery_names': [], 'scenery_renderers': [], 'scenery_projectors': []}
    if args.scenery_input:
        result = json.loads(args.scenery_input.read_text())
        for record in result['scenery_bundles']:
            path = bundles / record['bundle']
            if hashlib.sha256(path.read_bytes()).hexdigest() != record['sha256']:
                raise SystemExit('Original bundle changed: ' + record['bundle'])
        result['small_dressing'] = enrich_small_dressing(result, bundles)
        result['scenery_review'] = scenery_review(result)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + '\n')
        if args.provenance_output:
            previous = json.loads(args.provenance_output.read_text())
            previous['scenery_review'] = result['scenery_review']
            args.provenance_output.write_text(json.dumps(previous, separators=(',', ':')) + '\n')
        print('Reviewed ' + str(len(result['small_dressing'])) + ' exact original small dressing meshes; all census bundle hashes match')
        return
    actor_paths = sorted(path for path in bundles.glob('*_assets_all.bundle')
                         if path.name.startswith(('hero_', 'npc_')))
    for index, path in enumerate([] if args.scenery_only else actor_paths):
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
                elif (args.scenery_renderers or args.provenance_output) and obj.type.name == 'MeshRenderer':
                    fact = scenery_renderer(obj)
                    fact['bundle'] = str(path.relative_to(bundles))
                    result['scenery_renderers'].append(fact)
                elif (args.scenery_renderers or args.provenance_output) and obj.type.name == 'Projector':
                    fact = scenery_renderer(obj)
                    fact['bundle'] = str(path.relative_to(bundles))
                    result['scenery_projectors'].append(fact)
            if (index + 1) % 100 == 0:
                print(f'Scenery: {index + 1}/{len(paths)}', flush=True)
        result['scenery_names'] = sorted(names)
        if args.scenery_renderers or args.provenance_output:
            result['small_dressing'] = enrich_small_dressing(result, bundles)
            result['scenery_review'] = scenery_review(result)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    if args.provenance_output:
        compact = {'unitypy': result['unitypy'], 'actor_bundles': result['actor_bundles'],
            'models': [{key: record[key] for key in ('bundle', 'model', 'particles', 'mesh_fx')}
                       for record in result['models']],
            'scenery_bundles': [record for record in result['scenery_bundles'] if '.asset.bundle' in record['bundle']],
            'scenery_names': result['scenery_names']}
        if 'scenery_review' in result:
            compact['scenery_review'] = result['scenery_review']
        args.provenance_output.parent.mkdir(parents=True, exist_ok=True)
        args.provenance_output.write_text(json.dumps(compact, separators=(',', ':')) + '\n')
    print(f'Wrote {len(result["models"])} original model hierarchies and {len(result["scenery_names"])} scenery identities to {args.output}')


if __name__ == '__main__':
    main()
