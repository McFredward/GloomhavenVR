#!/usr/bin/env python3
"""Exercise explicit confirmation handoffs with real Unity GL uGUI raycasts.

Bind unchanged production UguiPointer raycast/merge/sort methods, the actual stable
order resolver, full transfer helper and registry. Fixture ledgers model exact
conversion writer operations; original Unity Images, Buttons, GraphicRaycasters,
CanvasGroups and EventSystem run natively. Tracking and broader panel conversion
are explicit boundaries. The companion flat-confirmation-lifecycle suite binds
actual game confirmation controllers and native fade callbacks; this proof does
not substitute active flags for geometric hit testing or native disablement.
"""
import argparse
import atexit
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def cleanup_generated(run):
    # Keep source copies, hashes, build logs and Unity verdicts after every outcome.
    # Generated imports and compiler caches must not accumulate on worker disks.
    shutil.rmtree(run / 'unity', ignore_errors=True)
    for case in run.iterdir():
        if case.is_dir():
            for cache in ('bin', 'obj'):
                shutil.rmtree(case / cache, ignore_errors=True)


def method(text, signature):
    start = text.index('    ' + signature)
    brace = text.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def sources(root):
    base = root / 'src/GloomhavenVR'
    transfer = (base / 'WorldUI/Conversion/CanvasConversion.SeatedSubtree.cs').read_text()
    registry = (base / 'Hands/Interact/UguiPokeSurfaces.cs').read_text()
    pointer = (base / 'Hands/Interact/UguiPointer.cs').read_text()
    # The production pointer's native raycast/merge/depth-sort methods run unchanged.
    # Tracking and logging ports are irrelevant to this screen-point proof and explicit.
    bound_pointer = '''using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace GloomhavenVR.Hands.Interact;
internal sealed class UguiPointer {
private readonly bool _farRay=true;
private readonly HandSide _side=HandSide.Left;
private readonly List<RaycastResult> _hits=new();
private PointerEventData? _data;
private int _lastPokeHitCount,_lastPokePadRank;
private GameObject? _lastPokeRunnerUp;
private PointerEventData GetData()=>_data??=new PointerEventData(EventSystem.current);
private bool TryAimRay(out Vector3 origin,out Vector3 direction){origin=direction=default;return false;}
'''
    for signature in ('internal bool TryRaycast(Canvas canvas, Vector2 screenPos, out RaycastResult topHit)',
                      'private bool TryRaycastTop(GraphicRaycaster raycaster, PointerEventData data, out RaycastResult top)',
                      'private static bool Beats(in RaycastResult challenger, in RaycastResult incumbent)',
                      'private static int StableOrder(in RaycastResult hit)'):
        bound_pointer += method(pointer, signature) + '\n'
    bound_pointer += 'internal void ReadUnused(){_=_lastPokeHitCount;_=_lastPokePadRank;_=_lastPokeRunnerUp;}\n}\n'
    order = (base / 'WorldUI/Conversion/CanvasConversion.8.Order.cs').read_text()
    order_method = method(order, 'internal static bool BaseSortingOrderOf(GameObject? raycasterGo, out int baseOrder)')
    order_bound = 'using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class CanvasConversion {\n' + order_method + '\n}\n'
    bound = {'Transfer.cs': transfer, 'Registry.cs': registry, 'Pointer.cs': bound_pointer, 'OrderBase.cs': order_bound}
    hashes = {str(path.relative_to(root)): hashlib.sha256(path.read_bytes()).hexdigest() for path in (
        base / 'WorldUI/Conversion/CanvasConversion.SeatedSubtree.cs',
        base / 'WorldUI/Conversion/CanvasConversion.2.Adopt.cs',
        base / 'WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs',
        base / 'WorldUI/Conversion/CanvasConversion.8.Order.cs',
        base / 'Hands/Interact/UguiPokeSurfaces.cs', base / 'Hands/Interact/UguiPointer.cs')}
    return bound, hashes


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/seated-subtree657')
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    atexit.register(cleanup_generated, run)
    bound, hashes = sources(args.source_root)
    (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
    variants = [('production', '', '', ''),
        ('unmapped-stable-tier', 'RegisterSeatedInputOrder(root, destination);', '// no stable moved-dialog input tier', 'The production UguiPointer resolves the original confirm button immediately after seating'),
        ('mapped-native-overlays', '&& !record.KeepOverrideSorting && !record.ConcededOverrideSorting;', ';', 'Dropdown overlays retain the existing authored input comparison'),
        ('live-override-bypass', 'nested == null || nested.overrideSorting ||', 'nested == null ||', 'A later native sorting override retires the ordinary seated input comparison'),
        ('later-concession-bypass', 'if (record.KeepOverrideSorting || record.ConcededOverrideSorting) return false;', '// ignored new native sorting concession', 'A later recorded native concession retires the seated input comparison'),
        ('omitted-transfer', 'if (root == null) return;', 'if (root == null || Application.isPlaying) return;', 'The production UguiPointer resolves the original confirm button immediately after seating'),
        ('stale-canvas-owner', 'previous.AdoptedCanvases.RemoveAt(i);', '// retained foreign canvas', 'Only the destination owns the shared native canvas'),
        ('stale-pointer-owner', 'Hands.Interact.UguiPokeSurfaces.UnregisterNested(previous.HostCanvas, record.Canvas);', '// retained foreign hit registration', 'The old window no longer raycasts the moved native subtree'),
        ('stale-layer-owner', 'previous.Relayered.RemoveAt(i);', '// retained foreign layer snapshot', 'Old release cannot relayer the moved original image and button'),
        ('lost-native-camera', 'record.Canvas.worldCamera = record.OriginalWorldCamera;', '// lost native camera restore', 'Native home restores the original camera after final release')]
    cases = []
    fixture = ROOT / 'scripts/seated-subtree-runtime'
    (run / 'fixture-hashes.json').write_text(json.dumps({str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in (Path(__file__).resolve(), fixture / 'Program.cs', fixture / 'Editor/TransferRunner.cs')}, indent=2) + '\n')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    for name, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for filename, content in bound.items():
            if before and before in content:
                if content.count(before) != 1: raise RuntimeError('Mutation seam drift: ' + name)
                content = content.replace(before, after, 1)
            (production / filename).write_text(content)
        project = build / 'Transfer.csproj'
        shutil.copyfile(ROOT / 'scripts/town-visitor-motion-runtime/Motion.csproj', project)
        assembly = 'SeatedSubtree_' + name.replace('-', '_')
        result = subprocess.run([shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet'), 'build', str(project), '-c', 'Release',
            '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failures cannot pass mutation controls')
        cases.append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest = run / 'manifest.json'
    manifest.write_text(json.dumps({'result': str(run / 'results.txt'), 'cases': cases}, indent=2))
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True); (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(ROOT / 'scripts/seated-subtree-runtime/Editor/TransferRunner.cs', project / 'Assets/Editor/TransferRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = [str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'TransferRunner.Start',
        '-interactionManifest', str(manifest), '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    report = run / 'results.txt'
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL Unity run: ' + str(run / 'unity.log'))
    shutil.rmtree(project)
    for name, *_ in variants:
        for cache in ('bin', 'obj'): shutil.rmtree(run / name / cache, ignore_errors=True)
    print('PASS: actual uGUI raycast transfer, native state rollback and nine causal controls; ' + str(run))


if __name__ == '__main__':
    main()
