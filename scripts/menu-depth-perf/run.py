#!/usr/bin/env python3
"""Execute actual menu-depth discovery and native uGUI/owner lifecycle under heavy UI load."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers, cleanup_project_editors


def execute(argv, log, timeout=240):
    with log.open('w') as output:
        child = subprocess.Popen(argv, cwd=ROOT, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
                child.wait()


def replace_once(code, before, after):
    assert code.count(before) == 1, (before, code.count(before))
    return code.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    parser.add_argument('--baseline', default='c5f70993d', help='Pinned actual shipped Build724 source commit')
    args = parser.parse_args()
    output = ROOT / '.planning/debug/menu-depth-perf'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n'); print('Evidence: ' + str(run), flush=True)
    (run / 'editor-environment.json').write_text(json.dumps({key: os.environ.get(key)
        for key in ('ADB_SERVER_SOCKET', 'ADB_MDNS_AUTO_CONNECT')}, indent=2) + '\n')
    status = 1
    project = run / 'unity'
    try:
        source = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
        paths = list(source.glob('*.cs')) + [ROOT / 'src/GloomhavenVR/Cards/Art/CardFaceMipBake.cs']
        sources = {path.name: path.read_text() for path in paths}
        fixture = ROOT / 'tests/GloomhavenVR.MenuDepthPerformanceTests'
        baseline = {path.name: subprocess.check_output(['git', 'show', args.baseline + ':' + str(path.relative_to(ROOT))], cwd=ROOT, text=True)
                    for path in paths}
        cases = [
            ('production', '', '', '', ''),
            ('baseline724', '', '', '', ''),
            ('periodic-root-sweeps', 'MenuDepthDriver.cs',
             '        FlushArrivals();',
             '        FlushArrivals();\n        if (Time.unscaledTime >= _restoredCensus)\n        {\n            _restoredCensus = Time.unscaledTime + .25f;\n            IReadOnlyList<ConvertedPanel> restoredPanels = CanvasConversion.ActivePanels;\n            for (int i = 0; i < restoredPanels.Count; i++) if (restoredPanels[i].Target != null) Discover(restoredPanels[i].Target);\n        }',
             'stable overloaded native UI never performs periodic root hierarchy censuses'),
            ('periodic-global-sweeps', 'MenuDepthCharacters.cs',
             '        RetireStale(maintainResources);',
             '        RetireStale(maintainResources);\n        if (Time.unscaledTime >= _restoredCensus)\n        { _restoredCensus = Time.unscaledTime + .25f; Discover(); }',
             'stable overloaded native UI never performs periodic global character or RawImage sweeps'),
            ('lost-graphic-birth', 'MenuDepthDriver.cs',
             '        if (hierarchyChanged) { MovedGraphics.Add(graphic); PendingGraphics.Add(graphic); }',
             '        if (!KnownGraphics.Contains(graphic)) return;\n        if (hierarchyChanged) { MovedGraphics.Add(graphic); PendingGraphics.Add(graphic); }',
             'a late active native owner without a lifecycle method binds through its real Image enable before first render'),
            ('lost-hierarchy-change', 'MenuDepthDriver.cs',
             'if (hierarchyChanged) { MovedGraphics.Add(graphic); PendingGraphics.Add(graphic); }',
             'if (hierarchyChanged) { /* restored stale parent ownership */ }',
             'active same-canvas ancestor reparent changes cached closed-old to open-new native window ownership'),
            ('lost-raw-texture-arrival', 'MenuDepthCharacterDisplayObserver.cs',
             '            AccessTools.PropertySetter(typeof(UnityEngine.UI.RawImage), "texture"),',
             '            null, // restored missing native RawImage texture event',
             'a native RawImage texture assigned after its first census binds on the first ready Late Tick'),
            ('lost-camera-target-arrival', 'MenuDepthCharacters.cs',
             '            manager.Holder = holder; manager.Target = target;',
             '            if (manager.Target == null && target != null && manager.Holder != null) continue;\n            manager.Holder = holder; manager.Target = target;',
             'camera target assigned after actual native Display and unchanged RawImage content binds on the first ready Late Tick'),
            ('repeated-null-display-scan', 'MenuDepthCharacters.cs',
             '        DisplayRequests[key] = target;',
             '        if (target != null) DisplayRequests[key] = target;',
             'repeated actual native Display while target is null scans each requester identity only once'),
            ('missing-first-draw-pump', 'MenuDepthDriver.cs',
             'MenuDepthSources.SetDiscoveryPump(CapturePump);', 'MenuDepthSources.SetDiscoveryPump(null);',
             'the first actual head draw flushes native Display or texture arrivals after the last Tick from an empty registry'),
            ('lost-cold-inactive-image', 'MenuDepthCharacters.cs',
             'Object.FindObjectsOfType<RawImage>(true)', 'Object.FindObjectsOfType<RawImage>()',
             'cold census registers every preexisting native pooled RawImage including inactive consumers'),
        ]
        if args.case:
            selected = set(args.case); assert selected <= {item[0] for item in cases}
            cases = [item for item in cases if item[0] in selected]
        original_fixture = fixture
        fixture = run / 'source/fixture'; fixture.mkdir(parents=True)
        for path in original_fixture.glob('*'):
            if path.is_file(): shutil.copyfile(path, fixture / path.name)
        bundle = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering/menu-depth.bundle'
        game = ROOT / 'ressources/GH_Data/Managed'
        pins = [game / 'GH.Runtime.dll', game / 'UnityEngine.UI.dll', game / 'GH.Runtime.FirstPass.dll', *paths, *original_fixture.glob('*'), Path(__file__), Path(__file__).parent / 'Editor/Runner.cs',
                ROOT / 'scripts/menu-depth-character/project_compilers.py', bundle]
        hashes = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in pins if path.is_file()}
        (run / 'source-pins.json').write_text(json.dumps({'sha256': hashes, 'baseline': args.baseline,
            'boundaries': 'Actual GH.Runtime typed owners, UIWindow registry/lifecycle, actual native Character3DDisplayManager.Display cached-original fast path, real uGUI hierarchy/enable/texture events, all production driver/discovery/catalogue/mesh/source code. Only session/config/converted panel roster and unrelated card services are supplied. Native UIWindow/manager Start and Beautify shader initialization are suppressed because no complete gameplay scene/native shader resources exist; authored owner fields and native cached model membership with authored primitive mesh are fixture inputs. No discovery notification is called by the test.',
            'limits': 'CPU scheduling under explicitly constructed heavy native UI, not an imported full game scene, XR or headset locomotion. Canvas drawing is disabled only during CPU timing; two actual first-head draws execute original native cached-model Display and real character source rendering from an initially empty registry, after last Tick. Separate borrowed-RT reentry guard also uses an explicit valid lease identity; independent character GPU suites cover broader visual projection.'}, indent=2) + '\n')
        game = ROOT / 'ressources/GH_Data/Managed'
        harmony = Path('/home/claw/.nuget/packages/lib.harmony/2.4.2/lib/net472')
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'gameManaged': str(game),
                    'harmonyRuntime': str(harmony), 'cases': []}
        for directory in ('Assets/Editor', 'ProjectSettings', 'Packages'): (project / directory).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0', 'com.unity.modules.assetbundle': '1.0.0', 'com.unity.modules.imageconversion': '1.0.0', 'com.unity.modules.physics': '1.0.0'}}) + '\n')
        shutil.copyfile(Path(__file__).parent / 'Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        for name, filename, before, after, expected in cases:
            case = run / name; bound = case / 'production'; bound.mkdir(parents=True)
            codes = baseline if name == 'baseline724' else sources
            source_hashes = {}
            for actual, code in codes.items():
                if actual == filename:
                    code = replace_once(code, before, after)
                    if name.startswith('periodic-'):
                        class_name = 'MenuDepthDriver' if actual == 'MenuDepthDriver.cs' else 'MenuDepthCharacters'
                        code = replace_once(code, 'internal static class ' + class_name + '\n{',
                            'internal static class ' + class_name + '\n{\n    private static float _restoredCensus;')
                (bound / actual).write_text(code); source_hashes[actual] = hashlib.sha256(code.encode()).hexdigest()
            (case / 'compiled-source-sha256.json').write_text(json.dumps(source_hashes, indent=2) + '\n')
            csproj = case / 'MenuDepthPerformance.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'MenuDepthPerformance_' + name.replace('-', '_')
            argv = ['/home/claw/.dotnet/dotnet', 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                    '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                    '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(bound),
                    '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed', '-p:GameManaged=' + str(game), '-p:DepthBundle=' + str(bundle)]
            if execute(argv, case / 'build.log'):
                raise RuntimeError('Compilation failure is not a control pass: ' + str(case))
            retained = run / 'assemblies' / (assembly + '.dll'); retained.parent.mkdir(exist_ok=True)
            shutil.copyfile(case / 'bin/Release/net472' / retained.name, retained)
            manifest['cases'].append({'name': name, 'dll': str(retained), 'expected': expected, 'baseline': name == 'baseline724'})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        argv = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
                '-projectPath', str(project), '-executeMethod', 'MenuDepthPerformanceRunner.Start',
                '-menuDepthPerfManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        (run / 'command.json').write_text(json.dumps(argv, indent=2) + '\n')
        status = execute(argv, run / 'launch.log', 420)
        (run / 'exit-code.txt').write_text(str(status) + '\n')
        if (run / 'results.txt').exists(): print((run / 'results.txt').read_text(), end='')
        stable = all(hashlib.sha256((ROOT / path).read_bytes()).hexdigest() == digest for path, digest in hashes.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(hashes)}, indent=2) + '\n')
        if not stable: raise RuntimeError('Pinned production/fixture source changed during the run')
        if status: raise RuntimeError('Native menu performance/lifecycle proof failed: ' + str(run))
        if {'production', 'baseline724'} <= {item[0] for item in cases}:
            old = json.loads((run / 'baseline724/statistics.json').read_text())
            new = json.loads((run / 'production/statistics.json').read_text())
            comparison = {'baseline': old, 'production': new,
                          'mean_ratio': new['meanMs'] / old['meanMs'], 'p99_ratio': new['p99Ms'] / old['p99Ms'],
                          'claim': 'Observed CPU Tick durations in the same overloaded native-uGUI fixture; no headset performance or GPU-time inference.'}
            (run / 'comparison.json').write_text(json.dumps(comparison, indent=2) + '\n')
            if not comparison['mean_ratio'] < .5:
                raise RuntimeError('Measured native workload did not show the required twofold mean discovery improvement')
        return 0
    except BaseException:
        status = 1
        raise
    finally:
        editors = cleanup_project_editors(project, '/home/claw/unity-2021.3.5/Editor/Unity', launcher_reaped=True)
        (run / 'editor-cleanup.json').write_text(json.dumps(editors, indent=2) + '\n')
        if editors['remaining']: raise RuntimeError('Owned Editor survived reaping; caches retained')
        compilers = cleanup_project_compilers(project, editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']: raise RuntimeError('Owned compiler survived reaping; caches retained')
        receipt = cleanup_completed_run('menu-depth-performance', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__': raise SystemExit(main())
