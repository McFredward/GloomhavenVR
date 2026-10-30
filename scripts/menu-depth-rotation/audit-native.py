#!/usr/bin/env python3
"""Read exact original character image ancestry and native pointer handlers."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--native-profile', type=Path, help='Optional pinned prior census; otherwise inspect all13 original scenes.')
    args = parser.parse_args()
    profile_path = ROOT / 'scripts/menu726-native-profile.py'
    spec = importlib.util.spec_from_file_location('rotation_native_profile', profile_path)
    profile = importlib.util.module_from_spec(spec); spec.loader.exec_module(profile)
    data = ROOT / 'ressources/GH_Data'
    generator = TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(data / 'Managed'))
    inputs = [Path(__file__), profile_path, data / 'Managed/GH.Runtime.dll', data / 'Managed/UnityEngine.UI.dll']
    if args.native_profile:
        known = json.loads(args.native_profile.read_text()); apertures = known['native_apertures']; inputs.append(args.native_profile)
    else:
        scenes = [data / ('level' + str(i)) for i in range(13)]; inputs += scenes
        _, apertures = profile.native_apertures(scenes, generator)
    environments = {}; rows = []
    source_root = ROOT / 'decompiled/GH.Runtime'
    manager_path = source_root / 'Character3DDisplayManager.cs'; inputs.append(manager_path)
    native_manager = manager_path.read_text()
    assert not re.search(r'\b(?:public|protected)\s+(?:\w+\s+)*(?:Rotate\w*|On\w*Drag\w*)\s*\(', native_manager)
    source_paths = {}
    for path in source_root.rglob('*.cs'):
        source_paths.setdefault(path.stem, []).append(path)
    inherited = {}

    def handlers(kind, visited=None):
        if kind in inherited: return inherited[kind]
        visited = set() if visited is None else visited
        if kind in visited: return []
        visited.add(kind)
        if kind in ['Button', 'Toggle', 'Slider', 'Scrollbar', 'Dropdown', 'TMP_Dropdown', 'InputField', 'TMP_InputField', 'ScrollRect']:
            return ['UnityEngine.UI.' + kind + ':built-in pointer control']
        if kind == 'EventTrigger': return ['UnityEngine.EventSystems.EventTrigger:serialized event entries']
        candidates = source_paths.get(kind, [])
        if not candidates: return []
        found = []
        # A short generic base name can occur in more than one namespace. Include
        # every declaration conservatively; an ambiguity must never hide input.
        for path in candidates:
            inputs.append(path); text = path.read_text()
            for event in ['OnPointerDown', 'OnPointerUp', 'OnPointerClick', 'OnBeginDrag', 'OnDrag', 'OnSelect']:
                if re.search(r'\bvoid\s+(?:\w+\.)?' + event + r'\s*\(', text): found.append(kind + ':' + event)
            header = re.search(r'\bclass\s+' + re.escape(kind) + r'(?:\s*<[^>]+>)?\s*:\s*([^\n{]+)', text)
            if header:
                for base in header.group(1).split(','):
                    parent = re.match(r'\s*(\w+)', base)
                    if parent: found += handlers(parent.group(1), visited)
        inherited[kind] = sorted(set(found)); return inherited[kind]

    for aperture in apertures:
        name = aperture['source']
        if name not in environments:
            path = data / name; inputs.append(path)
            environment = UnityPy.load(str(path)); environment.typetree_generator = generator
            environments[name] = environment
        environment = environments[name]
        reader = next(r for r in environment.objects if r.assets_file.name == name and r.path_id == aperture['path_id'])
        head = reader.parse_monobehaviour_head(); raw = profile.authored_tree(reader)
        transform = profile.game_transform(head.m_GameObject.read()); ancestry = []
        while True:
            gameobject = transform.m_GameObject.read(); parts = []
            for slot in gameobject.m_Component:
                if slot.component.type.name != 'MonoBehaviour': continue
                component = slot.component.deref(); component_head = component.parse_monobehaviour_head()
                if not component_head.m_Script.path_id: continue
                kind = component_head.m_Script.read().m_ClassName
                entry = {'kind': kind, 'path_id': component.path_id, 'pointer_handlers': handlers(kind)}
                if kind in ['Button', 'ExtendedButton', 'EventTrigger', 'ClickTracker', 'ClickTrackerExtended']:
                    entry['serialized'] = profile.authored_tree(component)
                parts.append(entry)
            ancestry.append({'name': gameobject.m_Name, 'transform_id': transform.object_reader.path_id if hasattr(transform, 'object_reader') else None,
                             'components': parts})
            if not transform.m_Father.path_id: break
            transform = transform.m_Father.read()
        classes = {part['kind'] for node in ancestry for part in node['components']}
        eligible = bool(classes & {'UIAdventurePartyAssemblyCharacterDisplay', 'UICharacterCreatorWindow'})
        rows.append({'scene': name, 'raw_id': reader.path_id, 'path': aperture['path'], 'raycast_target': raw['m_RaycastTarget'],
                     'eligible_interactive_owner': eligible, 'ancestry': ancestry,
                     'pointer_handlers': [value for node in ancestry for part in node['components'] for value in part['pointer_handlers']]})
    shared_path = ROOT / 'src/GloomhavenVR/WorldUI/Modal/SharedWindows.cs'; inputs.append(shared_path)
    shared = shared_path.read_text(); kinds = shared[shared.index('internal enum SharedWindowKind'):shared.index('internal static class SharedWindows')]
    assert not any(name in kinds for name in ['CharacterCreator', 'PartyAssembly', 'CharacterDisplay'])
    report = {'scope': 'All12 original RT-bound apertures; eligibility is exact native interactive owner ancestry, never a name.',
              'apertures': rows, 'inputs': [{'path': str(path), 'bytes': path.stat().st_size, 'sha256': digest(path)}
                  for path in sorted(set(inputs))], 'native_manager_rotation_api': False,
              'known_private_types': ['UIAdventurePartyAssemblyCharacterDisplay', 'UICharacterCreatorWindow'],
              'excluded_automatic_types': ['UICharacterCreatorConfirmationBox', 'UIRetirementManager']}
    report['private_content_contract'] = 'Current shared kinds are story/map story/quest confirmation/encounter/video/reward, not party or creation. The existing composed-loadout content exception is documented in SharedWindows. This adds no local-only exception to shared content.'
    for row in rows:
        if row['eligible_interactive_owner']:
            assert row['pointer_handlers'] and set(row['pointer_handlers']) == {'UIWindow:OnPointerDown', 'UIWindow:OnSelect'}, row
            assert row['raycast_target'] == 0
    report['input_contract'] = 'All eligible original portrait ancestries have UIWindow.OnPointerDown/OnSelect focus only; no pointer click/up/drag handler. Forward the original pointer focus and retain its disablement; the selection handler ancestry is unchanged. Automatic presentations are not enrolled.'
    args.output.parent.mkdir(parents=True, exist_ok=True); args.output.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'apertures': len(rows), 'eligible': sum(r['eligible_interactive_owner'] for r in rows),
        'with_pointer_handlers': [(r['scene'], r['raw_id'], r['pointer_handlers']) for r in rows if r['pointer_handlers']]}))


if __name__ == '__main__':
    main()
