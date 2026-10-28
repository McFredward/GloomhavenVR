#!/usr/bin/env python3
"""Execute production level-up composition with the real game widget in Unity.

Game session/owner fields and the existing conversion service are explicit ports.
Actual shipped ExtendedButton/UI/TMP, native sprite/font data, event dispatch,
Unity scene lifetime, owner matrices and GPU pixels execute unchanged. This is
not a native scenario bootstrap, HMD, multiplayer or hardware performance test.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    'LevelUpComposite.cs': 'Composites/LevelUpComposite.cs',
    'MovedSubtreeLayers.cs': 'Composites/MovedSubtreeLayers.cs',
    'ConfirmationOnOwner.cs': 'Composites/ConfirmationOnOwner.cs',
    'ConfirmationOnOwner.Origins.cs': 'Composites/ConfirmationOnOwner.Origins.cs',
    'AnnouncementContinue.cs': 'Modal/AnnouncementContinue.cs',
    'AnnouncementContinueView.cs': 'Modal/AnnouncementContinueView.cs',
}


def method(source, signature):
    if source.count(signature) != 1: raise RuntimeError('Source binding drift: ' + signature)
    start = source.index(signature); end = source.index('{', start) + 1; depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end]


def main():
    def terminated(signum, frame): raise SystemExit(128 + signum)
    signal.signal(signal.SIGTERM, terminated)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/levelup-native-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    if not os.environ.get('ADB_SERVER_SOCKET'):
        parser.error('Use the private task ADB_SERVER_SOCKET; a default shared ADB server is not permitted.')
    root = args.source_root.resolve(); fixture = ROOT / 'scripts/levelup-native-runtime'
    originals = {name: (root / 'src/GloomhavenVR/WorldUI' / relative).read_text() for name, relative in FILES.items()}
    modal = root / 'src/GloomhavenVR/WorldUI/Modal'
    screen = (modal / 'ModalFallback.12.ScreenBind.cs').read_text()
    conversion = (modal / 'ModalFallback.8.Convert.cs').read_text()
    admit = conversion[conversion.index('private static bool TryConvertWindow('):conversion.index('        string name = window.name;', conversion.index('private static bool TryConvertWindow('))]
    originals['NativeAdmission.cs'] = 'using UnityEngine.UI; namespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n' + method(screen, 'private static bool FloatWantedFor(') + '\n' + admit + 'AdmissionAttempts++; return false; } }}\n'
    cases = [('production', {}, '')]
    changes = [
        ('unsafe-active-clone', 'AnnouncementContinueView.cs', 'staging.SetActive(false);', '/* active staging regression */', 'native input controller clones never enable'),
        ('native-graphics-lost', 'AnnouncementContinueView.cs', '|| component is Graphic', '/* native graphics dropped */', 'native Continue retains complete original hierarchy geometry and font'),
        ('source-callback-retained', 'AnnouncementContinueView.cs', '_button.onClick = new Button.ButtonClickedEvent();', '_button.onClick.RemoveAllListeners();', 'native Continue clone never retains serialized reward callbacks'),
        ('tracker-relay-lost', 'AnnouncementContinueView.cs', 'copy.AddComponent<AnnouncementNativeContinuePointer>().Press = Continue;', 'copy.AddComponent<AnnouncementNativeContinuePointer>().Press = null;', 'native button gate false cannot veto the original ready tracker pointer permission'),
        ('localization-per-frame', 'AnnouncementContinueView.cs', '_continue = onContinue;', '_continue = onContinue; _button!.TextLanguageKey = "GUI_CONTINUE";', 'steady native Continue does not rewrite localization every frame'),
        ('pooled-text-birth-ignored', 'LevelUpComposite.cs', 'TMPro_EventManager.TEXT_CHANGED_EVENT.Add(PreviewTextChanged);', '/* native text birth observation omitted */', 'same pooled preview root accepts later native text births'),
        ('text-ink-ignored', 'LevelUpComposite.cs', 'Bounds ink = text.textBounds;', 'Bounds ink = default;', 'complete original card text remains readable'),
        ('glyphless-ink-admitted', 'LevelUpComposite.cs', 'if (!HasRenderedInk(ink)) continue;', '/* original TMP sentinel edges admitted */', 'glyphless TMP cannot eject either original card list after the reveal'),
        ('cross-scene-rejected', 'ConfirmationOnOwner.cs', '(separateScene && request.Source is not UILevelUpWindow\n                && request.Source is not UIResetLevelUpWindow)', 'separateScene', 'same native confirmation joins level-up before independent floating placement'),
        ('persistent-scene-stolen', 'ConfirmationOnOwner.cs', 'if (separateScene)\n            {', 'if (bool.Parse("false"))\n            {', 'persistent native dialog never inherits the scene-owned level-up lifetime'),
        ('owner-loss-closes-native', 'ConfirmationOnOwner.cs', 'Seat seat = Seats[index];', 'Seat seat = Seats[index]; if (!SourceAlive(seat.Request)) seat.Box.IsOpen = false;', 'owner scene unload restores the original persistent dialog'),
        ('generic-float-readmission', 'NativeAdmission.cs', 'window == null || ConfirmationOnOwner.IsOwned(window)', 'window == null', 'owned confirmation cannot be readmitted as a generic floating window'),
        ('direct-convert-readmission', 'NativeAdmission.cs', 'if (ConfirmationOnOwner.IsOwned(window)) return true;', '/* semantic admission guard omitted */', 'direct generic conversion acknowledges existing embedded presentation'),
        ('owner-resize-ignored', 'ConfirmationOnOwner.cs', 'host.localScale = seat.Mount.lossyScale;', '/* accepted owner scale omitted */', 'persistent overlay copies only accepted owner world matrix'),
    ]
    for name, file, before, after, expected in changes:
        if originals[file].count(before) != 1:
            raise SystemExit('Mutation binding drift: ' + name)
        cases.append((name, {file: originals[file].replace(before, after)}, expected))
    if args.case:
        unknown = set(args.case) - {case[0] for case in cases}
        if unknown: parser.error('Unknown cases: ' + str(unknown))
        cases = [case for case in cases if case[0] in args.case]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('Evidence: ' + str(run), flush=True)
    log = run / 'completion.log'; log.write_text('Evidence: ' + str(run) + '\n')
    code = 1; reaped = True; project = run / 'unity'; process = None
    spec = importlib.util.spec_from_file_location('project_compilers', ROOT / 'scripts/menu-depth-character/project_compilers.py')
    compilers = importlib.util.module_from_spec(spec); spec.loader.exec_module(compilers)
    try:
        managed = root / 'ressources/GH_Data/Managed'; dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        inputs = run / 'native-inputs'
        result = subprocess.run([str(Path.home() / 'unitypy-venv/bin/python'), str(fixture / 'export_inputs.py'), str(root), str(inputs)], capture_output=True, text=True)
        (run / 'native-export.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise RuntimeError('Native export failed: ' + str(run / 'native-export.log'))
        manifest = {'result': str(run / 'results.txt'), 'evidence': str(run), 'inputs': str(inputs), 'managed': str(managed.resolve()), 'cases': []}
        hashes = {}
        for file in [*(root / 'src/GloomhavenVR/WorldUI' / v for v in FILES.values()), root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs', modal / 'ModalFallback.12.ScreenBind.cs', modal / 'ModalFallback.8.Convert.cs', Path(__file__), *fixture.rglob('*.cs'), *fixture.glob('*.py'), fixture / 'LevelUpNative.csproj', *(managed / v for v in ('GH.Runtime.dll', 'UnityEngine.UI.dll', 'Unity.TextMeshPro.dll'))]:
            hashes[str(file)] = hashlib.sha256(file.read_bytes()).hexdigest()
        (run / 'source-pins.json').write_text(json.dumps({'sha256': hashes, 'coverage': 'partial' if args.case else 'production-and-controls', 'limits': __doc__}, indent=2) + '\n')
        env = os.environ.copy(); env.update(UseSharedCompilation='false', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
        assemblies = run / 'executed-assemblies'; assemblies.mkdir()
        for name, replacements, expected in cases:
            build = run / name; production = build / 'production'; production.mkdir(parents=True)
            for file, source in originals.items(): (production / file).write_text(replacements.get(file, source))
            csproj = build / 'LevelUpNative.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'LevelUpNative_' + name.replace('-', '_')
            result = subprocess.run([dotnet, 'build', str(csproj), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false', '-nodeReuse:false', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(managed.resolve())], capture_output=True, text=True, env=env)
            (build / 'build.log').write_text(result.stdout + result.stderr)
            if result.returncode: raise RuntimeError('Compilation failure is not a successful defect control: ' + str(build / 'build.log'))
            output = build / 'bin/Release/net472'; dll = assemblies / (assembly + '.dll'); shutil.copyfile(output / dll.name, dll)
            if 'harmony' not in manifest:
                runtime = run / 'harmony-runtime'; runtime.mkdir()
                for item in output.glob('*.dll'):
                    if item.name != dll.name: shutil.copyfile(item, runtime / item.name)
                manifest['harmony'] = str(runtime / '0Harmony.dll')
            manifest['cases'].append({'name': name, 'dll': str(dll), 'sha256': hashlib.sha256(dll.read_bytes()).hexdigest(), 'expected': expected})
        for path in ('Assets/Editor', 'Assets/Plugins', 'Assets/Resources/OriginalShaders', 'Packages', 'ProjectSettings'): (project / path).mkdir(parents=True)
        shutil.copyfile(fixture / 'Editor/LevelUpRunner.cs', project / 'Assets/Editor/LevelUpRunner.cs')
        for name in ('Unity.TextMeshPro.dll', 'UnityEngine.UI.dll'): shutil.copyfile(managed / name, project / 'Assets/Plugins' / name)
        shaders = root / 'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars'
        source = (shaders / 'ActorBarSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarSDF"', 'Shader "TextMeshPro/Distance Field"', 1)
        source = source.replace('\n\t\t\t#include "ActorBarForeground.cginc"', '', 1)
        source = re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);', '', source)
        source = re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;', '', source)
        source = re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);', '', source)
        source = source.replace('\n\t\t\t\tComputeScreenPos(vPosition),', '')
        source = source.replace('Fallback "GloomhavenVR/ActorBarMobileSDF"', 'Fallback "TextMeshPro/Mobile/Distance Field"')
        provenance = json.loads((shaders / 'source-provenance.json').read_text())
        record = next(item for item in provenance['sources'] if item['file'] == 'ActorBarSDF.shader')
        original = source
        for pragma in record['retained_feature_pragmas']:
            before = pragma.strip().replace('shader_feature', 'multi_compile')
            if original.count(before) != 1: raise RuntimeError('Canonical shader pragma binding drift')
            original = original.replace(before, pragma.strip())
        if hashlib.sha256(original.encode()).hexdigest() != record['normalized_original_sha256']:
            raise RuntimeError('Canonical original TMP shader differs from its verified upstream source')
        (run / 'canonical-source-provenance.json').write_text(json.dumps({'normalized_original_sha256': record['normalized_original_sha256'], 'runtime': 'Original source with the same retained keyword branches; native Windows shader bytecode is not executed.'}, indent=2) + '\n')
        (project / 'Assets/Resources/OriginalShaders/NativeSDF.shader').write_text(source)
        for name in ('TMPro.cginc', 'TMPro_Properties.cginc'): shutil.copyfile(shaders / name, project / 'Assets/Resources/OriginalShaders' / name)
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {name: '1.0.0' for name in ('com.unity.modules.ui', 'com.unity.modules.imgui', 'com.unity.modules.jsonserialize', 'com.unity.modules.physics', 'com.unity.modules.imageconversion')}}))
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
        command = [str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(project), '-executeMethod', 'LevelUpRunner.Start', '-levelUpManifest', str(path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a', *command]
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True, env=env); reaped = False
        try: result = process.wait(timeout=420)
        finally:
            try: os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError: pass
            process.wait(); reaped = True
            editor_dll = project / 'Library/ScriptAssemblies/Assembly-CSharp-Editor.dll'
            if editor_dll.exists(): shutil.copyfile(editor_dll, assemblies / 'Assembly-CSharp-Editor.dll')
            (run / 'executed-pins.json').write_text(json.dumps({str(p.relative_to(run)): hashlib.sha256(p.read_bytes()).hexdigest() for folder in (assemblies, run / 'harmony-runtime') for p in folder.glob('*.dll')}, indent=2) + '\n')
            editor = compilers.cleanup_project_editors(project, args.unity, launcher_reaped=True)
            (run / 'editor-cleanup.json').write_text(json.dumps(editor, indent=2) + '\n')
            if editor['remaining']: raise RuntimeError('Exact private editor failed to stop')
            compiler = compilers.cleanup_project_compilers(project, editor_reaped=True)
            (run / 'compiler-cleanup.json').write_text(json.dumps(compiler, indent=2) + '\n')
            if compiler['remaining']: raise RuntimeError('Exact private compiler failed to stop')
        report = Path(manifest['result'])
        if report.is_file(): print(report.read_text(), end='')
        if result or not report.is_file(): raise RuntimeError('Unity failed: ' + str(run / 'unity.log'))
        code = 0
        print(('PARTIAL PASS' if args.case else 'PASS') + ': ' + str(len(cases)) + ' actual native/Unity variants; ' + str(run))
    finally:
        if process is not None and process.poll() is None:
            os.killpg(process.pid, signal.SIGKILL); process.wait(); reaped = True
        receipt = cleanup_completed_run('levelup-native-runtime', code, log, root, process_reaped=reaped)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__': main()
