#!/usr/bin/env python3
"""Actual owned-menu lifecycle and fresh both-eye coverage raster in Unity2021."""
import argparse
import hashlib
import importlib.util
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
GPU = ROOT / 'tests/steamlink-loading-source'
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def rig_sources(mode, source):
    rig = (source / 'src/GloomhavenVR/Rig/VRRigDriver.cs').read_text()
    camera = (source / 'src/GloomhavenVR/Rig/VRRigDriver.HeadCamera.cs').read_text()
    recenter = (source / 'src/GloomhavenVR/Rig/VRRigDriver.Recenter.cs').read_text()
    assert '        UpdateRigLifetime(desired, controller, sceneRecheck);' in rig
    lifetime = method(rig, '    private void UpdateRigLifetime(')
    if mode == 'anchor-teardown':
        clause = '''            else if (_kind == RigKind.Menu)
                RefreshMenuAnchor(sceneRecheck);
'''
        assert lifetime.count(clause) == 1
        lifetime = lifetime.replace(clause, '').replace('&& _kind == RigKind.Map)',
                    '&& (_kind == RigKind.Menu || _kind == RigKind.Map))')
    refresh = method(rig, '    private void RefreshMenuAnchor(')
    if mode == 'rebind-motion':
        refresh = refresh.replace('        _anchor = successor;', '''        _rigRoot!.transform.position = position;
        _pendingRecenter = true;
        RigPoseVersion++;
        _anchor = successor;''')
    if mode == 'clear-owner':
        refresh = refresh.replace('        _anchor = successor;', '''        _camera!.clearFlags = successor.clearFlags;
        _anchor = successor;''')
    live_start = rig.index('    private static bool IsLiveMenuAnchor(')
    live = rig[live_start:rig.index(';', live_start) + 1]
    pieces = [lifetime, refresh, live]
    pieces += [method(rig, '    private ' + signature) for signature in (
        'void BuildMenuRig(', 'static Camera? ResolveMenuCamera(', 'void TearDownRig(', 'void OnDestroy(')]
    pieces += [method(camera, '    private ' + signature) for signature in (
        'void CreateHeadCamera(', 'GameObject CreateRigRoot(', 'void TickClipPlanes(', 'void TickHeadClearColor(')]
    pieces += [method(recenter, '    private void RecenterMenu(')]
    return '''#nullable enable
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.SpatialTracking;
using UnityEngine.XR;
namespace GloomhavenVR.Rig { internal sealed partial class VRRigDriver {
''' + '\n\n'.join(pieces) + '\n}}\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output', type=Path, default=ROOT / '.planning/debug/menu-rig-lifecycle')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--mode', choices=('all', 'production', 'anchor-teardown', 'rebind-motion', 'clear-owner'), default='all')
    args = parser.parse_args()
    source = args.source_root.resolve(strict=True)
    spec = importlib.util.spec_from_file_location('loading_source_harness', GPU / 'run.py')
    helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    common = Path(subprocess.check_output(['git', 'rev-parse', '--git-common-dir'], cwd=ROOT, text=True).strip())
    integration = (ROOT / common).resolve().parent if not common.is_absolute() else common.resolve().parent
    cleanup_root = integration if run.is_relative_to(integration / '.planning/debug') else ROOT
    production = source / helper.PRODUCTION
    inputs = [production / name for name in helper.FILES]
    inputs += [source / 'src/GloomhavenVR/Rig' / name for name in (
        'VRRigDriver.cs', 'VRRigDriver.HeadCamera.cs', 'VRRigDriver.Recenter.cs')]
    bank = source / 'src/GloomhavenVR/Assets/SteamLink/scene.bundle'
    overlay = source / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader'
    targets = source / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.NativeTargets.cs'
    stack = source / 'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.2.CameraStack.cs'
    inputs.append(stack)
    if targets.is_file(): inputs.append(targets)
    target_method = re.search(r'    private RenderTexture\? TargetFor\(CapturedCamera c\) =>\s*\n        .*?;', stack.read_text())
    assert target_method, 'Actual capture routing method drift'
    inputs += [bank, overlay, GPU / 'run.py', *GPU.glob('*.shader'), *[p for p in HERE.iterdir() if p.is_file()]]
    pins = {str(path): sha(path) for path in inputs}
    copies = run / 'sources'; copies.mkdir()
    for index, path in enumerate(inputs): shutil.copyfile(path, copies / (str(index) + '-' + path.name))
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    frozen_bank = run / 'actual-shipping-scene.bundle'; shutil.copyfile(bank, frozen_bank)
    managed = args.unity.parent / 'Data/Managed'
    metadata = [managed / 'UnityEngine' / ('UnityEngine.' + name + 'Module.dll')
                for name in ('Core', 'XR', 'VR', 'AssetBundle', 'JSONSerialize')]
    metadata_pins = {str(path): sha(path) for path in metadata}
    (run / 'metadata-pins.json').write_text(json.dumps(metadata_pins, indent=2) + '\n')
    framework = Path.home() / '.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    overall = {'scope': 'ACTUAL_UNITY_OWNED_MENU_HEAD_AND_COVERAGE_LIFECYCLE', 'cases': [], 'exit_code': 1,
               'source_root': str(source), 'xr_executed': False, 'headset_picture_proven': False, 'native_receiver_executed': False,
               'mixed_reality_owner_executed': False}
    modes = ('production', 'anchor-teardown', 'rebind-motion', 'clear-owner') if args.mode == 'all' else (args.mode,)
    expected = {'anchor-teardown': 'native menu anchor gap must retain current coverage source',
                'rebind-motion': 'menu swap has no new head/pose generation',
                'clear-owner': 'camera clear/culling/stereo/extent permissions preserved'}
    try:
        for mode in modes:
            case = run / mode; case.mkdir()
            completion = case / 'completion.log'; completion.write_text('Evidence: ' + str(run) + '\n')
            frozen = case / 'frozen'; frozen.mkdir()
            for name in helper.FILES:
                text = (production / name).read_text()
                if name == 'SteamLinkSceneRuntime.cs':
                    text, transforms = helper.freeze_runtime(text, False)
                    (case / 'transform-manifest.json').write_text(json.dumps(transforms, indent=2) + '\n')
                if name == 'SceneEyeOutput.cs':
                    expression = '            => Render(source, destination, eye);'
                    assert text.count(expression) == 1
                    text = text.replace(expression, '            => LoadingSourceBoundary.OutputObserved(Render(source, destination, eye), eye);')
                    (case / 'output-observer.json').write_text(json.dumps({
                        'actual_method_executed': 'SceneEyeOutput.Render',
                        'observation': 'count successful returned same-eye renders after unchanged actual method returns',
                        'retained_images_or_masks': False}, indent=2) + '\n')
                (frozen / name).write_text(text)
            (frozen / 'MenuLifecycle.cs').write_text(rig_sources(mode, source))
            (frozen / 'CapturedTarget.cs').write_text('using UnityEngine;\nnamespace GloomhavenVR.WorldUI { internal sealed partial class FlatScreen {\n' + target_method.group() + '\n}}\n')
            if targets.is_file():
                (frozen / 'NativeTargets.cs').write_text(targets.read_text())
            else:
                (frozen / 'NativeTargets.cs').write_text('using UnityEngine;\nnamespace GloomhavenVR.WorldUI { internal sealed partial class FlatScreen {\n private static RenderTexture? DesktopScrubTarget;\n internal static bool OwnsPresentationTarget(Camera camera) => DesktopScrubTarget != null && camera.targetTexture == DesktopScrubTarget;\n}}\n')
            (frozen / 'SourceCapabilities.cs').write_text('internal static class MenuFixtureSource { internal static bool QuestResolverAvailable => ' + str(targets.is_file()).lower() + '; }\n')
            build = case / 'managed-build'; build.mkdir()
            shutil.copyfile(HERE / 'Fixture.csproj', build / 'Fixture.csproj')
            case_code = 1
            try:
                status = helper.execute([dotnet, 'build', '--disable-build-servers', str(build / 'Fixture.csproj'),
                    '-c', 'Release', '--configfile', str(HERE / 'NuGet.Config'), '-p:FrozenSource=' + str(frozen),
                    '-p:FixtureDir=' + str(HERE), '-p:UnityManaged=' + str(managed),
                    '-p:FrameworkReferenceDir=' + str(framework), '-p:UseSharedCompilation=false'], case / 'compile.log')
                assert status == 0, 'Actual production extraction must compile 0W0E: ' + str(case)
                dll = build / 'bin/Release/net472/MenuRigLifecycleFixture.dll'; shutil.copyfile(dll, case / dll.name)
                project = case / 'owned-unity-project'; assets = project / 'Assets'
                (assets / 'Plugins').mkdir(parents=True); (assets / 'Editor').mkdir()
                shutil.copyfile(dll, assets / 'Plugins' / dll.name)
                shutil.copyfile(HERE / 'EditorRunner.cs', assets / 'Editor/EditorRunner.cs')
                for path in GPU.glob('*.shader'): shutil.copyfile(path, assets / path.name)
                shutil.copyfile(overlay, assets / 'OriginalOverlay.shader')
                (project / 'Packages').mkdir(); (project / 'Packages/manifest.json').write_text(
                    '{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.xr":"1.0.0"}}\n')
                settings = project / 'ProjectSettings'; settings.mkdir()
                (settings / 'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
                (settings / 'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
                (settings / 'TagManager.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!78 &1\nTagManager:\n  serializedVersion: 2\n  tags:\n  - UICamera\n')
                env = os.environ.copy(); env['VK_DRIVER_FILES'] = '/usr/share/vulkan/icd.d/intel_icd.json'
                status = helper.execute(['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-vulkan',
                    '-projectPath', str(project), '-executeMethod', 'MenuRigEditorRunner.Start',
                    '-loadingEvidence', str(case), '-sceneBank', str(frozen_bank), '-logFile', str(case / 'unity.log')],
                    case / 'launcher.log', env=env)
                gpu = json.loads((case / 'gpu-result.json').read_text())
                if mode == 'production': assert status == 0 and gpu['passed'], 'Production lifecycle failed: ' + str(case)
                else:
                    assert status != 0 and not gpu['passed'] and expected[mode] in (case / 'failure.txt').read_text(), str(case)
                    if mode == 'anchor-teardown': assert gpu['native_anchor_gap_closed_source'], 'Original mutant did not reproduce source close'
                overall['cases'].append({'mode': mode, 'unity_exit_code': status, 'expected_outcome_matched': True,
                    'assembly_sha256': sha(case / dll.name), 'gpu': gpu})
                case_code = 0
            finally:
                receipt = cleanup_completed_run('menu-rig-lifecycle', case_code, completion, cleanup_root, process_reaped=True)
                (case / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
        assert pins == {str(path): sha(path) for path in inputs}, 'Source inputs changed through child reap'
        assert metadata_pins == {str(path): sha(path) for path in metadata}, 'Borrowed metadata changed'
        overall['exit_code'] = 0
    finally:
        (run / 'result.json').write_text(json.dumps(overall, indent=2) + '\n')
        print(json.dumps({'evidence': str(run), 'exit_code': overall['exit_code']}))
    return overall['exit_code']


if __name__ == '__main__': raise SystemExit(main())
