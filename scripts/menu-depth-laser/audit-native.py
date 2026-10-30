#!/usr/bin/env python3
"""Read only the native Further Ability row's exact serialized input ownership."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import sys

sys.dont_write_bytecode = True
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    dependency = ROOT / 'scripts/menu726-native-profile.py'
    spec = importlib.util.spec_from_file_location('native_profile', dependency)
    profile = importlib.util.module_from_spec(spec); spec.loader.exec_module(profile)
    data = ROOT / 'ressources/GH_Data'
    generator = TypeTreeGenerator('2021.3.5f1')
    generator.load_local_dll_folder(str(data / 'Managed'))
    asset = data / 'level9'
    environment = UnityPy.load(str(asset)); environment.typetree_generator = generator

    def kind(reader):
        return reader.parse_monobehaviour_head().m_Script.read().m_ClassName if reader.type.name == 'MonoBehaviour' else reader.type.name

    def parts(reader):
        return [entry.component.deref() for entry in reader.read().m_Component]

    def pointer(owner, field):
        return profile.pointer(profile.authored_tree(owner)[field], owner).deref()

    def component(go, name):
        return next(part for part in parts(go) if kind(part) == name)

    def identity(reader):
        return {'identity': profile.object_id(reader), 'authored': profile.authored_tree(reader)}

    records = []
    for inventory in environment.objects:
        if inventory.type.name != 'MonoBehaviour' or kind(inventory) != 'UILevelUpCardInventory': continue
        row = pointer(inventory, 'cardPrefab'); row_go = pointer(row, 'm_GameObject')
        button = pointer(row, 'button'); holder = pointer(row, 'cardHolder')
        holder_go = pointer(holder, 'm_GameObject')
        image = component(row_go, 'Image'); rect = component(row_go, 'RectTransform')
        group = component(holder_go, 'CanvasGroup')
        button_data = profile.authored_tree(button)
        assert pointer(button, 'm_GameObject') == row_go, 'Native row button must belong to the row root'
        for event, method in [('onMouseEnter', 'MouseEnter'), ('onMouseExit', 'MouseExit')]:
            calls = button_data[event]['m_PersistentCalls']['m_Calls']
            assert any(call['m_Target']['m_PathID'] == row.path_id and call['m_MethodName'] == method for call in calls)
        records.append({'inventory': identity(inventory), 'row': identity(row), 'button': identity(button),
            'row_image': identity(image), 'row_rect': identity(rect), 'holder': identity(holder), 'holder_group': identity(group)})
    assert records, 'Missing native More Cards authoring'
    row = records[0]
    rect = row['row_rect']['authored']; group = row['holder_group']['authored']
    button = row['button']['authored']; image = row['row_image']['authored']
    summary = {'width': rect['m_SizeDelta']['x'], 'height': rect['m_SizeDelta']['y'],
        'pivot': rect['m_Pivot'], 'rowAlpha': image['m_Color']['a'], 'rowRaycast': bool(image['m_RaycastTarget']),
        'holderBlocksRaycasts': bool(group['m_BlocksRaycasts']), 'holderInteractable': bool(group['m_Interactable']),
        'scaleNonInteractable': bool(button['scaleNonInteractable']), 'highlightScale': button['highlightScaleFactor'],
        'hoverMovement': button['hoverMovement']}
    assert summary['rowAlpha'] == 0 and summary['rowRaycast'] and not summary['holderBlocksRaycasts']
    report = {'asset': str(asset), 'asset_sha256': hashlib.sha256(asset.read_bytes()).hexdigest(),
        'auditor_dependency_sha256': hashlib.sha256(dependency.read_bytes()).hexdigest(),
        'summary': summary, 'records': records}
    args.output.write_text(json.dumps(report, indent=2) + '\n')
    print('PASS original row button/events, transparent input graphic and non-raycasting card holder:', len(records), 'inventory')


if __name__ == '__main__':
    main()
