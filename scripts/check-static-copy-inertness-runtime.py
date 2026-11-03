#!/usr/bin/env python3
"""Exercise both real static-copy admission consumers against original native components.

Actual GH.Runtime UIWindow/PersistentAbilityPopup/Singleton types and Unity
activation/destruction execute. Original native TMP font plus exact image/layout/
scroll-offset witnesses are compared in final pixels. Native scene bootstrap,
unchanged addressable portrait detachment and final conversion are explicit ports.
One destruction-endpoint refusal is injected to exercise fail-closed lifecycle;
the production stripping and both admission decisions remain real.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import tempfile
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    assert source.count(signature) == 1, 'Source binding drift: ' + signature
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def execute(command, log, timeout=240):
    environment = dict(os.environ)
    environment.update(DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER='1', MSBUILDDISABLENODEREUSE='1')
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT,
                                 env=environment, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/static-copy-inertness-runtime')
    parser.add_argument('--case', action='append', help='Partial development variant selection')
    args = parser.parse_args()
    root = args.source_root.resolve()
    fixture = ROOT / 'scripts/static-copy-inertness-runtime'
    game = root / 'ressources/GH_Data/Managed'
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    stat_path = root / 'src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs'
    prop_path = root / 'src/GloomhavenVR/WorldUI/Surfaces/PropInfoSurface.cs'
    stat, prop = stat_path.read_text(), prop_path.read_text()
    stat_methods = '\n'.join(method(stat, signature) for signature in (
        'private static void BuildStaticCopy(', 'internal static bool StripLogicComponents(',
        'private static bool IsSnapshotPresentation(', 'private static bool HasSnapshotDependent(',
        'private static void DestroyCopy('))
    stat_methods += '\n' + re.search(r'private static bool CopyWasRefused\([\s\S]*?;', stat)[0]
    # Exact production lifecycle call sites, including the requeue path which is not
    # needed to bootstrap the renderer witness. The consumer methods execute intact.
    assert '&& !CopyWasRefused(ActorStatPanel.Instance, _held2Actor)' in stat
    assert '_held2Sequence = secondary?.Seq ?? 0;' in stat
    assert '_refusedCopySource = null;' in method(stat, 'public void Shutdown(')
    conversion = root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs'
    convert = method(conversion.read_text(), 'internal static ConvertedPanel? Convert(')
    assert 'target.SetParent(hostRect, worldPositionStays: false);' in convert, 'Actual conversion activation/reparent boundary drift'
    prop_methods = '\n'.join(method(prop, signature) for signature in (
        'internal static void SnapshotDisplacedCard(', 'internal static void DropSecondCard(',
        'private static int CountFilledFields('))
    header = '#nullable enable\nusing System;using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;using GloomhavenVR.Hands;using Object=UnityEngine.Object;\nnamespace GloomhavenVR.WorldUI.Surfaces {\n'
    sources = {
        'Figure.cs': header + 'internal sealed partial class StatPanelSurface {\n' + stat_methods + '\n}}',
        'Prop.cs': header + 'internal sealed partial class PropInfoSurface {\n' + prop_methods + '\n}}',
    }
    legacy = (fixture / 'LegacyStrip.fixture').read_text()
    variants = [
        ('production', '', '', '', ''),
        ('legacy-singleton-only', 'Figure.cs', '', '', 'figure snapshot has no surviving original PersistentAbilityPopup controller'),
        ('figure-admission-bypass', 'Figure.cs', 'if (!StripLogicComponents(copy, out int stripped))',
         'if (!StripLogicComponents(copy, out int stripped) && bool.Parse("false"))', 'rejected figure snapshot never enters conversion admission'),
        ('prop-admission-bypass', 'Prop.cs', 'if (!StatPanelSurface.StripLogicComponents(copy, out stripped))',
         'if (!StatPanelSurface.StripLogicComponents(copy, out stripped) && bool.Parse("false"))', 'rejected prop snapshot never enters conversion admission'),
        ('figure-retry-loop', 'Figure.cs', 'if (CopyWasRefused(real, actor))',
         'if (CopyWasRefused(real, actor) && bool.Parse("false"))', 'same refused figure hold is built/reported only once'),
        ('prop-retry-loop', 'Prop.cs', '&& _refusedCopySide == side && _refusedCopyLabel == label)',
         '&& _refusedCopySide == side && _refusedCopyLabel == label && bool.Parse("false"))', 'same refused prop hold is built/reported only once'),
    ]
    if args.case:
        assert set(args.case) <= {v[0] for v in variants}, 'Unknown variant'
        variants = [v for v in variants if v[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    print('Evidence: ' + str(run), flush=True)
    status = 1
    previous = {}
    def interrupted(signum, frame):
        raise KeyboardInterrupt
    for kind in (signal.SIGTERM, signal.SIGINT):
        previous[kind] = signal.signal(kind, interrupted)
    try:
        proof = {str(p): hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in [stat_path, prop_path, conversion, Path(__file__), *fixture.rglob('*'),
                           game / 'GH.Runtime.dll', game / 'ScenarioRuleLibrary.dll',
                           game / 'UnityEngine.UI.dll', game / 'Unity.TextMeshPro.dll'] if p.is_file()}
        inputs = run / 'native-inputs'
        # Existing original-font exporter prefix, ending before the unrelated native
        # confirmation census. All exact prefix bytes are retained with the run.
        exporter = root / 'scripts/levelup-native-runtime/export_inputs.py'
        prefix = exporter.read_text().split('# The confirmation\'s full-screen backdrop', 1)[0]
        assert len(prefix) < len(exporter.read_text()), 'Font-only exporter boundary drift'
        (run / 'font-export-source.py').write_text(prefix)
        code = execute([str(Path.home() / 'unitypy-venv/bin/python'), str(run / 'font-export-source.py'), str(root), str(inputs)], run / 'font-export.log')
        assert code == 0, 'Original font export failed: ' + str(run)
        proof[str(exporter)] = hashlib.sha256(exporter.read_bytes()).hexdigest()
        retained = run / 'Sources'
        retained.mkdir()
        for name, source in sources.items():
            (retained / name).write_text(source)
        project = run / 'unity'
        for directory in ['Assets/Editor', 'Assets/Plugins', 'Assets/Resources/OriginalShaders', 'Packages', 'ProjectSettings']:
            (project / directory).mkdir(parents=True)
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'inputs': str(inputs), 'cases': []}
        for name, filename, old, new, expected in variants:
            case = run / name
            generated = case / 'production'
            generated.mkdir(parents=True)
            for target, source in sources.items():
                if target == 'Figure.cs':
                    if name == 'legacy-singleton-only':
                        old_block = method(source, 'internal static bool StripLogicComponents(')
                        source = source.replace(old_block, legacy)
                    else:
                        # Only inject a failing Unity destruction endpoint. Neither pure
                        # presentation classification nor either consumer is substituted.
                        source = source.replace('Object.DestroyImmediate(component);',
                            'if (SnapshotFault.RefuseProbe && component is SnapshotProbe) continue;\nObject.DestroyImmediate(component);')
                if target == filename and old:
                    assert source.count(old) == 1, 'Control binding drift: ' + name
                    source = source.replace(old, new, 1)
                (generated / target).write_text(source)
            csproj = case / 'Snapshot.csproj'
            shutil.copyfile(fixture / 'Snapshot.csproj', csproj)
            assembly = 'StaticCopy_' + name.replace('-', '_')
            code = execute([dotnet, 'build', str(csproj), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                '-p:UseSharedCompilation=false', '-nodeReuse:false', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(generated),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(game)], case / 'build.log', 120)
            assert code == 0, 'Compilation is never an accepted causal control: ' + str(case / 'build.log')
            dll = case / 'bin/Release/netstandard2.1' / (assembly + '.dll')
            target = project / 'Assets/Plugins' / dll.name
            shutil.copyfile(dll, target)
            manifest['cases'].append({'name': name, 'dll': str(target), 'expected': expected})
        for dll in game.glob('*.dll'):
            if (dll.name.startswith(('UnityEngine.', 'System.')) and dll.name not in ('UnityEngine.UI.dll', 'System.Runtime.CompilerServices.Unsafe.dll')) or dll.name in ('UnityEngine.dll', 'mscorlib.dll', 'netstandard.dll'):
                continue
            shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
            proof[str(dll)] = hashlib.sha256(dll.read_bytes()).hexdigest()
        shaders = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
        source = (shaders / 'ActorBarSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarSDF"', 'Shader "TextMeshPro/Distance Field"', 1)
        source = source.replace('\n\t\t\t#include "ActorBarForeground.cginc"', '', 1)
        source = re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);', '', source)
        source = re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;', '', source)
        source = re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);', '', source)
        source = source.replace('\n\t\t\t\tComputeScreenPos(vPosition),', '').replace('Fallback "GloomhavenVR/ActorBarMobileSDF"', 'Fallback "TextMeshPro/Mobile/Distance Field"')
        provenance = json.loads((shaders / 'source-provenance.json').read_text())
        record = next(r for r in provenance['sources'] if r['file'] == 'ActorBarSDF.shader')
        original = source
        for pragma in record['retained_feature_pragmas']:
            before = pragma.strip().replace('shader_feature', 'multi_compile')
            assert original.count(before) == 1
            original = original.replace(before, pragma.strip())
        assert hashlib.sha256(original.encode()).hexdigest() == record['normalized_original_sha256'], 'Original TMP shader binding drift'
        (project / 'Assets/Resources/OriginalShaders/NativeSDF.shader').write_text(source)
        for name in ['TMPro.cginc', 'TMPro_Properties.cginc']:
            shutil.copyfile(shaders / name, project / 'Assets/Resources/OriginalShaders' / name)
        shutil.copyfile(fixture / 'Editor/SnapshotRunner.cs', project / 'Assets/Editor/SnapshotRunner.cs')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0', 'com.unity.modules.imageconversion': '1.0.0', 'com.unity.modules.animation': '1.0.0'}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        for p in [*inputs.glob('*'), *(shaders / name for name in ('ActorBarSDF.shader', 'source-provenance.json', 'TMPro.cginc', 'TMPro_Properties.cginc'))]:
            if p.is_file():
                proof[str(p)] = hashlib.sha256(p.read_bytes()).hexdigest()
        (run / 'source-hashes.json').write_text(json.dumps(proof, indent=2) + '\n')
        (run / 'limits.txt').write_text(__doc__ + '\nFigure-overlay-runtime preparation slice is unchanged from qualified756: SHA25632c85723fb6a20ac091a947fbd0cb4c2b105a091d69265ce38ee61986f861764; parent validates its remaining input bindings. No hardware stall reproduction or ACK/rule-engine repair is claimed.\n')
        manifest_path = run / 'manifest.json'
        manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-disable-assembly-updater', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'SnapshotRunner.Start', '-snapshotManifest', str(manifest_path), '-logFile', str(run / 'Unity.log')]
        if not os.environ.get('DISPLAY'):
            command = ['xvfb-run', '-a', *command]
        code = execute(command, run / 'launcher.log')
        result = run / 'results.txt'
        text = result.read_text() if result.is_file() else ''
        print(text, end='')
        assert code == 0 and result.is_file(), 'Actual Unity proof failed: ' + str(run)
        assert text.count('PASS negative control ') == sum(bool(v[4]) for v in variants), 'A compiling control did not execute'
        status = 0
    finally:
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'coverage': 'partial' if args.case else 'production-and-five-controls', 'scope': __doc__}, indent=2) + '\n')
        receipt = cleanup_completed_run('static-copy-inertness-runtime', status, completion, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        for kind, handler in previous.items():
            signal.signal(kind, handler)


if __name__ == '__main__':
    main()
