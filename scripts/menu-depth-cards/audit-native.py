#!/usr/bin/env python3
"""Read-only audit of exact original card fields and prefab/component boundaries.

Uses the developer UnityPy environment. Emits metadata and hashes, never artwork,
meshes or gameplay execution. Runtime property targetGraphic is serialized by the
original Button as m_TargetGraphic. A root Image is accepted only for the exact
UIScenarioAttackModifier owner proved by the native scene authoring.
"""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import struct
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
import UnityPy
from UnityPy.classes import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

FIELDS = {
    'FullAbilityCard': [('headerImage',), ('buttonsHolderImage',),
        ('topActionButton', 'actionButton', 'm_TargetGraphic'),
        ('topActionButton', 'defaultActionButton', 'm_TargetGraphic'),
        ('bottomActionButton', 'actionButton', 'm_TargetGraphic'),
        ('bottomActionButton', 'defaultActionButton', 'm_TargetGraphic')],
    'MiniAbilityCard': [('miniAbilityBackgroundImage',)],
    'ItemCardUI': [('cardBackground',)],
    'AbilityCardUI': [('fullAbilityCard',), ('miniAbilityCard',), ('button',)],
    'UIPartyCharacterAbilityCardsDisplay': [('abilityCardsPanel', 'm_Content'), ('fullCardAreaToFit',)],
    'MonsterRoundCardUI': [('background',), ('$rootImage',)],
    'MonsterBaseUI': [('backgroundImage',), ('textBackgroundOpacityImage',)],
    'UIPerkAttackModifier': [('button', 'm_TargetGraphic')],
    'UIScenarioAttackModifier': [('$rootImage',)],
    'AttackModifierCardGUI': [('m_MathModifier',), ('m_Shuffle',)],
    'AttackModifier': [('mainImage',)],
}


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-data', type=Path, default=ROOT / 'ressources/GH_Data')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args(); data = args.game_data
    profile_path = ROOT / 'scripts/menu726-native-profile.py'
    spec = importlib.util.spec_from_file_location('native_profile', profile_path)
    profile = importlib.util.module_from_spec(spec); spec.loader.exec_module(profile)
    generator = TypeTreeGenerator('2021.3.5f1'); generator.load_local_dll_folder(str(data / 'Managed'))
    files = [data / 'StreamingAssets/aa/StandaloneWindows64/misc_gui_assets_all.bundle',
             *[data / ('level' + str(i)) for i in range(13)]]
    report = {'unity': '2021.3.5f1', 'managed_sha256': sha(data / 'Managed/GH.Runtime.dll'),
              'auditor_dependency_sha256': sha(profile_path), 'sources': {}, 'owners': [], 'material_shaders': []}

    def tree(reader):
        return profile.authored_tree(reader) if reader.type.name == 'MonoBehaviour' else reader.read_typetree()

    def pointer(value, owner):
        if not isinstance(value, dict) or not value.get('m_PathID'): return None
        return PPtr(m_FileID=value['m_FileID'], m_PathID=value['m_PathID'], assetsfile=owner.assets_file).deref()

    def script(reader):
        if reader.type.name != 'MonoBehaviour': return reader.type.name
        raw = reader.get_raw_data()
        file_id, path_id = struct.unpack_from('<iq', raw, 16)
        return PPtr(m_FileID=file_id, m_PathID=path_id, assetsfile=reader.assets_file).read().m_ClassName

    def components(owner):
        go = pointer(tree(owner)['m_GameObject'], owner)
        return [pointer(value['component'], go) for value in tree(go)['m_Component']]

    def follow(owner, path):
        if path == ('$rootImage',):
            return next((part for part in components(owner) if script(part) == 'Image'), None)
        current = owner
        for name in path:
            value = tree(current).get(name)
            current = pointer(value, current)
            if current is None: return None
        return current

    def identity(reader):
        if reader is None: return None
        d = tree(reader); go = pointer(d.get('m_GameObject'), reader)
        result = {'serialized_file': reader.assets_file.name, 'path_id': reader.path_id,
                  'class': script(reader), 'name': tree(go).get('m_Name') if go else d.get('m_Name')}
        for key in ['m_Enabled', 'm_IsActive', 'm_Color', 'm_Type', 'm_RaycastTarget', 'm_Maskable', 'm_Sprite', 'm_Material',
                    'm_RenderMode', 'm_OverrideSorting', 'm_SortingOrder', 'm_LocalPosition',
                    'm_LocalScale', 'm_SizeDelta', 'm_Pivot', 'm_AnchorMin', 'm_AnchorMax']:
            if key in d: result[key] = d[key]
        if reader.type.name == 'MonoBehaviour':
            events = {}
            for key in ('onMouseEnter', 'onMouseExit'):
                if key in d:
                    events[key] = d[key]
            if events: result['pointer_events'] = events
        return result

    shaders = set()
    def material_shader(graphic):
        material = pointer(tree(graphic).get('m_Material'), graphic)
        if material is None: return
        shader = pointer(tree(material).get('m_Shader'), material)
        key = (shader.assets_file.name, shader.path_id)
        if key in shaders: return
        shaders.add(key); compiled = shader.read()
        report['material_shaders'].append({'material': identity(material), 'shader_file': key[0], 'shader_path_id': key[1],
            'shader_sha256': hashlib.sha256(shader.get_raw_data()).hexdigest(), 'name': compiled.m_ParsedForm.m_Name,
            'passes': [{'name': pas.m_State.m_Name,
                'z_test': {'name': pas.m_State.zTest.name, 'value': pas.m_State.zTest.val},
                'z_write': {'name': pas.m_State.zWrite.name, 'value': pas.m_State.zWrite.val},
                'compiled_parameter_names': [name for name, index in pas.m_NameIndices]}
                for sub in compiled.m_ParsedForm.m_SubShaders for pas in sub.m_Passes]})

    for source in files:
        relative = str(source.relative_to(data)); report['sources'][relative] = sha(source)
        environment = UnityPy.load(str(source)); environment.typetree_generator = generator
        for owner in environment.objects:
            if owner.type.name != 'MonoBehaviour': continue
            name = script(owner)
            if name not in FIELDS: continue
            for path in FIELDS[name]:
                target = follow(owner, path)
                if target is not None and script(target) == 'Image': material_shader(target)
            report['owners'].append({'source': relative, 'owner': identity(owner),
                'serialized_sha256': hashlib.sha256(owner.get_raw_data()).hexdigest(),
                'root_components': [identity(part) for part in components(owner)],
                'fields': {'.'.join(path): identity(follow(owner, path)) for path in FIELDS[name]}})
    by_id = {(row['owner']['serialized_file'], row['owner']['path_id']): row for row in report['owners']}
    report['character_hover_prefabs'] = []
    for row in report['owners']:
        if row['owner']['class'] != 'AbilityCardUI' or row['owner']['name'] not in ('AbilityCard', 'AbilityCard_gamepad'): continue
        full = row['fields']['fullAbilityCard']; mini = row['fields']['miniAbilityCard']
        assert full and full['class'] == 'FullAbilityCard' and mini and mini['class'] == 'MiniAbilityCard'
        full_row = by_id[(full['serialized_file'], full['path_id'])]
        button = row['fields']['button']; assert button
        for event, method in [('onMouseEnter', 'OnPointerEnter'), ('onMouseExit', 'OnPointerExit')]:
            calls = button.get('pointer_events', {}).get(event, {}).get('m_PersistentCalls', {}).get('m_Calls', [])
            assert any(call.get('m_MethodName') == method and call.get('m_Target', {}).get('m_PathID') == row['owner']['path_id']
                       for call in calls), ('Missing original hover callback', row['owner'], method)
        report['character_hover_prefabs'].append({'row': row['owner'], 'full': full, 'mini': mini,
            'row_button': button, 'full_root_components': full_row['root_components'],
            'full_printed_graphics': full_row['fields']})
    assert len(report['character_hover_prefabs']) == 2, 'Missing exact native pointer/gamepad ability hover prefab'
    assert any(row['owner']['class'] == 'UIPartyCharacterAbilityCardsDisplay' for row in report['owners']), 'Missing character ability overview authoring'
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2, allow_nan=False) + '\n')
    counts = {name: sum(row['owner']['class'] == name for row in report['owners']) for name in FIELDS}
    print(json.dumps({'metadata': str(args.output), 'native_owners': counts}, indent=2))

if __name__ == '__main__':
    main()
