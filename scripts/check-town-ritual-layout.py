#!/usr/bin/env python3
"""Verify original enhancement folio and native temple offerings against stand bounds in Unity."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import re
import zipfile
from collections import defaultdict

ROOT = Path(__file__).resolve().parents[1]

def native_counts(root, output):
    # Archive members are read-only original game references, never repackaged in the mod.
    base = root / 'ressources/GH_Data/StreamingAssets/Rulebase'
    cards = defaultdict(set)
    max_slots = max_area = 0
    slot_example = ''
    archives = [*base.glob('*.ruleset'), *base.glob('DLC/*/*.ruleset')]
    evidence = {'archives': {}, 'temple_active_blessings': {}}
    for archive in archives:
        evidence['archives'][str(archive.relative_to(base))] = hashlib.sha256(archive.read_bytes()).hexdigest()
        with zipfile.ZipFile(archive) as source:
            for name in source.namelist():
                if not name.startswith('AbilityCard/'): continue
                text = source.read(name).decode('utf-8-sig')
                character = re.search(r'^Character:\s*(\w+)', text, re.M)
                identity = re.search(r'^ID:\s*(-?\d+)', text, re.M)
                if character and identity and character[1] != 'All': cards[character[1]].add(int(identity[1]))
                for match in re.finditer(r'^\s+\w*Enhancements:\s*(\d+)', text, re.M):
                    if int(match[1]) > max_slots: max_slots, slot_example = int(match[1]), name
                for match in re.finditer(r'^\s+AreaEffect:\s*([^\r\n]+)', text, re.M):
                    max_area = max(max_area, len(re.findall(r",E(?:[|\"']|$)", match[1])))
    for mode in ('Campaign', 'Guildmaster'):
        archive = base / (mode + '.ruleset')
        evidence['archives'][archive.name] = hashlib.sha256(archive.read_bytes()).hexdigest()
        with zipfile.ZipFile(archive) as source:
            text = source.read('Temple/Temple.yml').decode('utf-8-sig')
        evidence['temple_active_blessings'][mode] = len(re.findall(r'^\s*- Condition:', text, re.M))
    evidence['class_card_counts'] = {key: len(value) for key, value in cards.items()}
    evidence['largest_area_slot_count'] = max_area
    evidence['largest_mainline_slot_count'] = max_slots
    evidence['two_slot_example'] = slot_example
    # Execute only this pure static query from the read-only original DLL, over every
    # enum combination. Empty existing-condition sets produce the largest legal lists.
    probe = output / 'native-probe'
    shutil.copytree(ROOT / 'scripts/town-ritual-layout-runtime/NativeProbe', probe)
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    result = subprocess.run([dotnet, 'run', '--project', str(probe / 'Probe.csproj'), '-c', 'Release', '--',
        str(root / 'ressources/GH_Data/Managed')], capture_output=True, text=True)
    (output / 'native-probe.log').write_text(result.stdout + result.stderr)
    if result.returncode: raise SystemExit('Native pure-option query failed; see ' + str(output / 'native-probe.log'))
    options = json.loads(result.stdout.splitlines()[-1])
    (output / 'native-option-counts.json').write_text(json.dumps(options, indent=2) + '\n')
    evidence['native_method_combinations'] = options['calls']
    evidence['max_options_per_slot'] = options['maximum']
    evidence['original_assembly_sha256'] = hashlib.sha256((root / 'ressources/GH_Data/Managed/ScenarioRuleLibrary.dll').read_bytes()).hexdigest()
    evidence['native_rune_upper_count'] = options['maximum'] * max_slots
    evidence['source_rule'] = 'CAbility.GetValidEnhancements Attack: 14 types; EnhancementUtils.GetEnhancementsCompatibleWithEnhancementButtonLine expands each slot outside gamepad mode.'
    if max(map(len, cards.values())) != 31 or max_slots != 2 or max_area > 2 or options['maximum'] != 14 or set(evidence['temple_active_blessings'].values()) != {1}:
        raise SystemExit('Original native stock changed; refresh layout evidence before claiming coverage.')
    (output / 'native-counts.json').write_text(json.dumps(evidence, indent=2) + '\n')

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-ritual-layout')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    native_counts(args.source_root, run)
    native_python = Path(os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python')))
    subprocess.run([str(native_python), str(ROOT / 'scripts/town-ritual-layout-runtime/export-native-book.py'),
        str(args.source_root), str(run / 'native-book.obj')], check=True)
    base = args.source_root / 'src/GloomhavenVR/WorldUI/TownServices'
    sources = {name: (base / name).read_text() for name in ('TownServiceRitualLayout.cs', 'TownServiceBookInk.cs', 'TownServiceTempleBowl.cs')}
    variants = [('production', None, '', '', ''),
        ('folio-width', 'TownServiceRitualLayout.cs', '.60f, .55f', '1.40f, .55f', 'complete native folio sections stay within the stand width'),
        ('folio-under-table', 'TownServiceRitualLayout.cs', '.32f, .33f, -.16f', '.32f, .05f, -.16f', 'native folio stays above tabletop and below resident face'),
        ('folio-rear-decoration', 'TownServiceRitualLayout.cs', '.32f, .33f, -.16f', '.32f, .33f, .65f', 'native folio clears front edge and rear decoration'),
        ('folio-controls-overlap', 'TownServiceRitualLayout.cs', '.43f, .036f, -.20f', '.19f, .036f, -.20f', 'native folio content and original controls never overlap'),
        ('detached-bowl', 'TownServiceTempleBowl.cs', 'frame.SetParent(priest, false);', 'frame.SetParent(null, false);', 'both owners donate into the actual shared priest bowl, never a relocated workspace'),
        ('flat-purse', 'TownServiceRitualLayout.cs', 'Quaternion.identity, new Vector2(.125f, .15f)', 'Quaternion.Euler(90f, 0f, 0f), new Vector2(.125f, .15f)', 'purse rests upright above the hand rather than lying like a card'),
        ('floating-ink', 'TownServiceBookInk.cs', '_position = surface + normal * .00065f;', '_position = surface + normal * .020f;', 'ink is attached within one millimetre of the actual original page'),
        ('white-ui-ink', 'TownServiceBookInk.cs', 'new Color(.12f, .065f, .027f, 1f)', 'Color.white', 'book text is printed dark ink rather than white floating UI'),
        ('unwarped-ink', 'TownServiceBookInk.cs', '_text.transform.InverseTransformPoint(_stationToWorld.MultiplyPoint3x4(surface + normal * .00065f))', 'vertices[index]', 'glyph vertices follow curved original pages within 1.5 millimetres'),
        ('unwrapped-ink', 'TownServiceBookInk.cs', 'text.enableWordWrapping = true;', 'text.enableWordWrapping = false;', 'native description wraps on its own page'),
        ('striped-parchment', 'TownServiceBookInk.cs', 'indices.Add(a); indices.Add(b); indices.Add(a + 1);',
         'if (column != 3) { indices.Add(a); indices.Add(b); indices.Add(a + 1); }',
         'blank parchment has no missing interior grid cells or visible dark strips'),
        ('overlapping-purses', 'TownServiceRitualLayout.cs', ') * .145f,', ') * .04f,', 'all native blessing purses have separate reachable bodies')]
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    fixture = ROOT / 'scripts/town-ritual-layout-runtime'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for label, filename, before, after, expected in variants:
        build = run / label
        production = build / 'production'
        production.mkdir(parents=True)
        for name, source in sources.items():
            if name == filename:
                if source.count(before) != 1: raise SystemExit('Production binding drift: ' + label)
                source = source.replace(before, after)
            (production / name).write_text(source)
        project = build / 'Layout.csproj'
        shutil.copyfile(fixture / 'Layout.csproj', project)
        assembly = 'TownRitualLayout_' + label.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(args.source_root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': label, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(source.encode()).hexdigest() for name, source in sources.items()}, indent=2))
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest))
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    shutil.copyfile(ROOT / 'scripts/town-activity-runtime/Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project), '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(path), '-layoutEvidence', str(run), '-nativeBookObj', str(run / 'native-book.obj'), '-logFile', str(run / 'unity.log')], timeout=240, stdout=subprocess.DEVNULL)
    evidence = Path(manifest['result'])
    if evidence.exists(): print(evidence.read_text())
    if result.returncode or not evidence.exists(): raise SystemExit('FAIL: ' + str(run / 'unity.log'))
    print(f'PASS: native-count ritual layout proof and {len(variants)-1} compiled negative controls; evidence: {run}')

if __name__ == '__main__': main()
