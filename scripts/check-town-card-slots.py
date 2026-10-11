#!/usr/bin/env python3
"""Validate offered-card native enhancement-point ownership, live updates and inert clones."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def method(source, signature):
    start = source.index('    ' + signature)
    return source[start:source.index('\n    }', start) + 6]

def expression(source, signature):
    start = source.index('    ' + signature)
    return source[start:source.index(';', start) + 1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-card-slots')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--unity-ui', type=Path)
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/town-card-slots-runtime'
    base = args.source_root / 'src/GloomhavenVR'
    slots = (base / 'WorldUI/TownServices/TownServiceCardSlots.cs').read_text()
    mirror = (base / 'Net/Remote/RemoteWidgetMirror.cs').read_text()
    neutral = 'using System; using System.Collections.Generic; using UnityEngine; using UnityEngine.UI; using Object = UnityEngine.Object; namespace GloomhavenVR.Net; internal sealed partial class RemoteWidgetMirror {\n'
    neutral += method(mirror, 'internal static void Neutralize(') + '\n'
    neutral += expression(mirror, 'private static bool IsPresentation(') + '\n'
    neutral += expression(mirror, 'private static bool IsStockLayout(') + '\n'
    neutral += method(mirror, 'private static bool InsideAny(') + '\n'
    neutral += method(mirror, 'private static bool IsSelfOrDescendant(') + '\n}\n'
    sources = {'Slots.cs': slots.replace('Time.unscaledTime', 'SlotsClock.Now'), 'Neutralize.cs': neutral}
    hashes = {'TownServiceCardSlots.cs': hashlib.sha256(slots.encode()).hexdigest(), 'RemoteWidgetMirror.cs': hashlib.sha256(mirror.encode()).hexdigest()}
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': hashes}, indent=2) + '\n')
    ui = args.unity_ui or next((p for p in (args.source_root / 'unity/GloomhavenVR.Assets/Library/ScriptAssemblies/UnityEngine.UI.dll', args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll') if p.is_file()), None)
    if ui is None or not args.unity.is_file(): parser.error('Real Unity 2021.3.5 and UnityEngine.UI.dll are required')
    variants = [('production', None, '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('ignore-pooled-source', 'Slots.cs', '_points[i].Source != slot.assignedPoints[i].transform', 'false', 'same-count pooled point replacement cannot retain stale original content'),
            ('ignore-reclaim', 'Slots.cs', 'handoff.Card != null ? handoff.NativeSlot : null', 'handoff.NativeSlot', 'physical card reclaim removes every slot mirror immediately'),
            ('no-live-animation', 'Slots.cs', '_mirror.TickLive();', '// negative: no live point animation', 'enhancement removal reaches displayed original point on the next frame'),
            ('no-structural-refresh', 'Slots.cs', '_refreshAt = SlotsClock.Now + .1f; _mirror.Refresh(Source);', '_refreshAt = SlotsClock.Now + .1f;', 'native point hierarchy refresh discovers new animation descendants'),
            ('partial-point-list', 'Slots.cs', 'if (point == null) { slot = null; break; }', 'if (point == null && SlotsClock.Now < 0f) { slot = null; break; }', 'transient missing native point hides stale slot display atomically'),
            ('leaked-point-mirror', 'Slots.cs', '_mirror.Destroy(); if (_mount != null)', 'if (_mount != null)', 'changing point count retires all previous mirrors'),
            ('laser-blocker', 'Neutralize.cs', 'cg.blocksRaycasts = false;', 'cg.blocksRaycasts = true;', 'native point clone cannot intercept laser or pointer'),
        ]
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for path, text in sources.items():
            if path == filename:
                if text.count(before) != 1: raise SystemExit('Production mutation binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Slots.csproj'; shutil.copyfile(fixture / 'Slots.csproj', project)
        assembly = 'TownSlots_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ui)], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a successful negative control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2))
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.modules.physics":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run([str(args.unity), '-batchmode', '-nographics', '-projectPath', str(project), '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see ' + str(run / 'unity.log'))
    print('PASS: ' + str(len(variants)) + ' production/negative variants; evidence: ' + str(run))

if __name__ == '__main__': main()
