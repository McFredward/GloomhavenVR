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
    'DialogSurface.cs': 'Surfaces/DialogSurface.cs',
    'PanelGraphicMaterial.cs': 'Sharpness/PanelGraphicMaterial.cs',
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
    quest_copy = root / 'src/GloomhavenVR/Core/QuestTextureCopy.cs'
    quest_sources = [quest_copy] if quest_copy.is_file() else []
    for file in quest_sources:
        originals[file.name] = file.read_text()
    modal = root / 'src/GloomhavenVR/WorldUI/Modal'
    screen = (modal / 'ModalFallback.12.ScreenBind.cs').read_text()
    conversion = (modal / 'ModalFallback.8.Convert.cs').read_text()
    admit = conversion[conversion.index('private static bool TryConvertWindow('):conversion.index('        string name = window.name;', conversion.index('private static bool TryConvertWindow('))]
    originals['NativeAdmission.cs'] = 'using UnityEngine.UI; namespace GloomhavenVR.WorldUI { internal static partial class ModalFallback {\n' + method(screen, 'private static bool FloatWantedFor(') + '\n' + admit + 'AdmissionAttempts++; return false; } }}\n'
    supersample_core = (root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs').read_text()
    supersample_capture = (root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs').read_text()
    kernels = [method(supersample_core, 'private static bool Eligible(')]
    for signature in ('private static bool BuildCamera(', 'private static void ApplyCaptureClear(',
                      'private static bool BuildDisplay(', 'private static void SyncProjection(',
                      'private static void SyncDisplayPose(', 'private static void ResolveAndMip(',
                      'private static void NeutraliseGrabPassBlur(', 'private static float AskedMipLodOffset(',
                      'private static float ApplyMipLodOffset(', 'private static void BindDisplayTarget('):
        kernels.append(method(supersample_capture, signature))
    for name in ('MinAuthoredPx', 'MipLodOffsetMin', 'MipLodOffsetMax', 'MipLodOffsetEpsilon'):
        declaration = re.findall(r'private const float ' + name + r'\s*=\s*[^;]+;', supersample_core + supersample_capture)
        if len(declaration) != 1: raise RuntimeError('Supersample constant binding drift: ' + name)
        kernels.append(declaration[0])
    for value_type in ('Vector3', 'Quaternion'):
        start = supersample_capture.index('private static bool SameComponents(' + value_type)
        kernels.append(supersample_capture[start:supersample_capture.index(';', start) + 1])
    originals['SupersampleKernel.cs'] = ('using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI {'
        'internal static partial class PanelSupersample {\n' + '\n'.join(kernels) + '\n}}\n')
    converter_core=(root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs').read_text()
    centering=converter_core[converter_core.index('        target.SetParent(hostRect, worldPositionStays: false);'):converter_core.index('        AdoptNestedCanvases(panel);')]
    originals['NativeTargetFrame739.cs']='using UnityEngine;namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {internal static void NormalizeTarget739(RectTransform target,RectTransform hostRect,Vector2 size){'+centering+'}}}'
    adopt = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.2.Adopt.cs').read_text()
    lifecycle = (root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs').read_text()
    background = [method(adopt, 'private static void HideFullScreenBackground(')]
    for name in ('BackgroundCoverFraction', 'BackgroundOpaqueAlpha', 'CanvasSweepIntervalFrames'):
        declaration = re.findall(r'private const (?:float|int) ' + name + r'\s*=\s*[^;]+;', adopt)
        if len(declaration) != 1: raise RuntimeError('Native backdrop constant binding drift: ' + name)
        background.append(declaration[0])
    restore = method(lifecycle, 'for (int i = 0; i < panel.HiddenBackgrounds.Count; i++)')
    originals['NativeBackgroundKernel739.cs'] = ('using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;'
        'namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {'
        'private static readonly List<Graphic> BgGraphicScratch=new(64);private static readonly Vector3[] BgCornerScratch=new Vector3[4];'
        'internal static void SweepBackgrounds739(ConvertedPanel panel){if(panel.HideBackground)HideFullScreenBackground(panel,false);}'
        'internal static void RestoreBackgrounds739(ConvertedPanel panel){' + restore + 'panel.HiddenBackgrounds.Clear();}'
        + '\n'.join(background) + '}}')
    order_source=(root / 'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.8.Order.cs').read_text()
    order_methods=[method(order_source,signature) for signature in ('internal static void TickPanelOrder(', 'private static void StampShownPose(', 'private static float PanelEyeDistance(', 'private static void ApplyPanelOrder(', 'internal static void RegisterOrderFollower(ConvertedPanel panel, Canvas canvas, int offset)')]
    for name in ('PanelOrderBase','PanelOrderStep','PanelOrderMaxRank','OrderSwapMarginMeters','OrderSwapStableFrames'):
        declaration=re.findall(r'(?:private|internal) const (?:int|float) '+name+r'\s*=\s*[^;]+;',order_source)
        if len(declaration)!=1:raise RuntimeError('Ordering constant binding drift: '+name)
        order_methods.append(declaration[0])
    originals['NativeOrderKernel739.cs']=('using System;using System.Collections.Generic;using UnityEngine;namespace GloomhavenVR.WorldUI {internal static partial class CanvasConversion {'
        'private static readonly List<ConvertedPanel> OrderedPanels=new(32),StandaloneOrderPanels=new(1);'
        'private static List<ConvertedPanel> Active=>ActivePanels;private static float s_orderSwapMarginScale=1,s_orderSwapMarginWorld=.02f;private static float OrderSwapMargin=>s_orderSwapMarginWorld;'
        'private static int s_orderDistanceMeasures=0,s_orderWrites=0,s_orderResorts=0,s_orderHashNodes=0;internal static int ShownStampFrame {get;private set;}'
        'private static void TickFurnitureOrder(Vector3 eye){}private static void BuildSeenThroughLadder(Vector3 eye){}private static void LogPanelOrder(){}'
        'private static void CheckFollowerOffset(int offset,string name,bool allowBehind){}'
        'internal static void ResetOrder739(){foreach(var p in ActivePanels){p.OrderListed=false;p.OrderSwapPeer=null;p.OrderSwapStreak=0;}OrderedPanels.Clear();}'
        + '\n'.join(order_methods)+'}}')
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
        ('persistent-capture-bypassed', 'ConfirmationOnOwner.cs', 'useModLayer: true', 'useModLayer: false', 'owned confirmation keeps final pixels independent of its native full-frame GrabPass'),
        ('standalone-capture-bypassed', 'DialogSurface.cs', 'useModLayer: true', 'useModLayer: false', 'standalone confirmation keeps final pixels independent of its native full-frame GrabPass'),
        ('confirmation-blur-allowed', 'SupersampleKernel.cs', 'if (PanelGraphicMaterial.NeutraliseGrabPass(t.GetComponent<Graphic>()))', 'if (PanelGraphicMaterial.NeutraliseGrabPass(null))', 'owned confirmation keeps final pixels independent of its native full-frame GrabPass'),
        ('persistent-backdrop-retained', 'ConfirmationOnOwner.cs', 'transparentBackground: true', 'transparentBackground: false', 'owned direct confirmation has no full-screen black or GrabPass surface outside the native small box'),
        ('standalone-backdrop-retained', 'DialogSurface.cs', 'transparentBackground: true', 'transparentBackground: false', 'standalone direct confirmation has no full-screen black or GrabPass surface outside the native small box'),
        ('persistent-owner-tier-lost', 'ConfirmationOnOwner.cs', 'sortingOrder: panel.BaseSortingOrder + 1', 'sortingOrder: 0', 'owned direct confirmation retains every front and rear native foreground pixel during motion'),
        ('persistent-lease-not-retired', 'ConfirmationOnOwner.cs', 'if (PanelAlive(seat.PersistentMember)) return false;', 'if (seat.PersistentMember.IsAlive) return false;', 'native close restores exact persistent dialog home while owner remains open'),
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
        for file in [*(root / 'src/GloomhavenVR/WorldUI' / v for v in FILES.values()), *quest_sources, *(root / 'src/GloomhavenVR/WorldUI/Conversion' / name for name in ('CanvasConversion.1.Core.cs','CanvasConversion.2.Adopt.cs','CanvasConversion.3.Fit.cs','CanvasConversion.4.Lifecycle.cs','CanvasConversion.8.Order.cs')), root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.1.Core.cs', root / 'src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.2.Capture.cs', modal / 'ModalFallback.12.ScreenBind.cs', modal / 'ModalFallback.8.Convert.cs', Path(__file__), *fixture.rglob('*.cs'), *fixture.glob('*.py'), *fixture.glob('*.shader'), fixture / 'LevelUpNative.csproj', *(managed / v for v in ('GH.Runtime.dll', 'UnityEngine.UI.dll', 'Unity.TextMeshPro.dll'))]:
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
        for shader in fixture.glob('*.shader'): shutil.copyfile(shader, project / 'Assets/Resources' / shader.name)
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
        receipt = cleanup_completed_run('levelup-native-runtime', code, log, ROOT, process_reaped=reaped)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')


if __name__ == '__main__': main()
