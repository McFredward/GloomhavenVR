#!/usr/bin/env python3
"""Read original starter-view scroll fields and card EventTrigger entries; never export/write assets."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import sys
sys.dont_write_bytecode = True
import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(); data = ROOT / 'ressources/GH_Data'
    spec = importlib.util.spec_from_file_location('profile', ROOT / 'scripts/menu726-native-profile.py')
    profile = importlib.util.module_from_spec(spec); spec.loader.exec_module(profile)
    generator = TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(data / 'Managed'))
    result = {'files': {}, 'viewers': [], 'card_hover_adapters': []}
    for source in [data / 'level9', data / 'level10', data / 'StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle']:
        result['files'][str(source)] = hashlib.file_digest(source.open('rb'), 'sha256').hexdigest()
        env = UnityPy.load(str(source)); env.typetree_generator = generator
        trees = {}; kinds = {}
        def tree(item):
            key = (item.assets_file.name, item.path_id)
            if key not in trees:
                trees[key] = profile.authored_tree(item) if item.type.name == 'MonoBehaviour' else item.read_typetree()
            return trees[key]
        def ptr(value, owner):
            return PPtr(m_FileID=value['m_FileID'], m_PathID=value['m_PathID'], assetsfile=owner.assets_file).deref() if value and value.get('m_PathID') else None
        def kind(item):
            key = (item.assets_file.name, item.path_id)
            if key not in kinds:
                if item.type.name != 'MonoBehaviour': kinds[key] = item.type.name
                else:
                    f, i = struct.unpack_from('<iq', item.get_raw_data(), 16)
                    kinds[key] = PPtr(m_FileID=f, m_PathID=i, assetsfile=item.assets_file).read().m_ClassName
            return kinds[key]
        def components(go): return [ptr(item['component'], go) for item in tree(go)['m_Component']]
        for item in env.objects:
            if item.type.name != 'MonoBehaviour': continue
            name = kind(item)
            if name == 'UICharacterCreatorCardsViewer':
                scroll = ptr(tree(item)['cardContainer'], item); serialized = tree(scroll)
                assert kind(scroll) == 'ExtendedScrollRect' and serialized['m_Vertical'] and not serialized['m_Horizontal']
                result['viewers'].append({'file': str(source), 'path_id': item.path_id,
                    'scroll_path_id': scroll.path_id, 'fields': serialized,
                    'sha256': hashlib.sha256(item.get_raw_data()).hexdigest()})
            if name != 'FullAbilityCard': continue
            go = ptr(tree(item)['m_GameObject'], item)
            transform = next(c for c in components(go) if c.type.name == 'RectTransform')
            def walk(rect, route):
                node = ptr(tree(rect)['m_GameObject'], rect); path = route + '/' + tree(node)['m_Name']
                for component in components(node):
                    if kind(component) != 'EventTrigger': continue
                    entries = tree(component)['m_Delegates']; ids = [entry['eventID'] for entry in entries]
                    assert 7 not in ids, (path, ids)  # Unity EventTriggerType.Scroll
                    result['card_hover_adapters'].append({'file': str(source), 'full_card_path_id': item.path_id,
                        'path': path, 'event_trigger_path_id': component.path_id, 'event_ids': ids,
                        'methods': [call['m_MethodName'] for entry in entries
                            for call in entry['callback']['m_PersistentCalls']['m_Calls']],
                        'sha256': hashlib.sha256(component.get_raw_data()).hexdigest()})
                for child in tree(rect).get('m_Children', []): walk(ptr(child, rect), path)
            walk(transform, '')
    assert len(result['viewers']) == 2
    assert len({row['full_card_path_id'] for row in result['card_hover_adapters']}) == 4
    assert any(row['path'].endswith('/Top button') for row in result['card_hover_adapters'])
    for name in ['UICharacterCreatorCardsViewer', 'ExtendedScrollRect']:
        source = ROOT / 'decompiled/GH.Runtime' / (name + '.cs')
        result['files'][str(source)] = hashlib.sha256(source.read_bytes()).hexdigest()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps({'viewers': len(result['viewers']), 'pointer_only_card_adapters': len(result['card_hover_adapters'])}))


if __name__ == '__main__': main()
