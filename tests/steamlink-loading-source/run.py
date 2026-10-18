#!/usr/bin/env python3
"""Complete real SceneHeadOutput + production GPU components, explicit XR/host boundaries."""
import argparse
import hashlib
import importlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
PRODUCTION = 'src/GloomhavenVR/Core/SteamLink'
FILES = ('SteamLinkSceneRuntime.cs', 'SceneEyeCoverage.cs', 'SceneEyeOutput.cs',
         'SceneTransparentMaterials.cs', 'SceneAtlasProfile.cs')
OLD_OWNER = '''        if (_output != null && _output.Head == head)
        {
            _output.TickCold();
            if (!_output.Ready)
            {
                Refuse(head, string.IsNullOrEmpty(_output.Failure)
                    ? "The current head source was closed or its material scope failed." : _output.Failure);
                return false;
            }
            return _output.Ready;
        }
'''


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def execute(command, log, **kwargs):
    with log.open('w') as stream:
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                   start_new_session=True, **kwargs)
        try:
            return process.wait(timeout=240)
        except BaseException:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            raise


def freeze_runtime(text, mutant):
    begin = text.index('        if (_output != null && _output.Head == head)')
    end = text.index('        Exit();', begin)
    original = text[begin:end]
    assert 'if (_output.Ready) return true;' in original, 'Source lacks the transient-close recovery under test'
    if mutant:
        text = text[:begin] + OLD_OWNER + text[end:]
    assert text.count('Head.stereoActiveEye') == 1
    text = text.replace('Head.stereoActiveEye', 'LoadingSourceBoundary.EyeMetadata(Head)')
    assert text.count('head.stereoEnabled') == 1
    text = text.replace('head.stereoEnabled', 'LoadingSourceBoundary.StereoEnabled(head)')
    text = text.replace('using UnityEngine.XR;',
                        'using UnityEngine.XR;\nusing XRSettings = LoadingSourceBoundary.XrSettings;')
    marker = '[RequireComponent(typeof(Camera))]'
    assert text.count(marker) == 1
    text = text.replace(marker, '[ExecuteInEditMode]\n' + marker)
    return text, {
        'actual_source_methods_replaced': [],
        'xr_settings_alias': 'explicit owned MultiPass metadata, no XR session',
        'active_eye_metadata_reads': 1,
        'stereo_attachment_metadata': 'explicit ready XR attachment; actual Editor camera has no XR device',
        'editor_callback_activation': 'ExecuteInEditMode on otherwise real SceneHeadOutput',
        'old_owner_mutant': mutant,
        'old_owner_original_block': OLD_OWNER if mutant else None,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--bank', type=Path)
    parser.add_argument('--output', type=Path, default=ROOT/'.planning/debug/steamlink-loading-source')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--mode', choices=('both', 'positive', 'old-owner'), default='both')
    args = parser.parse_args()
    source = args.source_root.resolve(strict=True)
    bank = (args.bank or source/'src/GloomhavenVR/Assets/SteamLink/scene.bundle').resolve(strict=True)
    assert bank.is_file() and args.unity.is_file()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    copies = run/'sources'; copies.mkdir()
    inputs = [source/PRODUCTION/name for name in FILES]
    overlay = source/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader'
    inputs += [overlay, bank, *[p for p in HERE.iterdir() if p.is_file()]]
    pins = {str(p): sha(p) for p in inputs}
    for path in inputs:
        shutil.copy2(path, copies/(path.name+'-'+pins[str(path)][:12]))
    (run/'source-pins.json').write_text(json.dumps(pins, indent=2)+'\n')
    frozen_bank = run/'actual-shipping-scene.bundle'; shutil.copy2(bank, frozen_bank)
    sys.path.insert(0, str(ROOT/'scripts'))
    cleanup = importlib.import_module('town_run_retention').cleanup_completed_run
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    managed = args.unity.parent/'Data/Managed'
    framework = Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    overall = {'scope': 'ACTUAL_SCENE_HEAD_OUTPUT_LOADING_LIFECYCLE_GAMMA_UNITY',
               'source_root': str(source), 'shipping_bank_sha256': sha(bank), 'stages': [],
               'xr_executed': False, 'mixed_reality_owner_executed': False,
               'receiver_executed': False, 'headset_picture_proven': False}
    code = 1
    try:
        modes = ('positive', 'old-owner') if args.mode == 'both' else (args.mode,)
        for mode in modes:
            case = run/mode; case.mkdir()
            completion = case/'completion.log'; completion.write_text('evidence: '+str(run)+'\n')
            frozen = case/'frozen'; frozen.mkdir()
            for name in FILES:
                text = (source/PRODUCTION/name).read_text()
                if name == 'SteamLinkSceneRuntime.cs':
                    text, transforms = freeze_runtime(text, mode == 'old-owner')
                    (case/'transform-manifest.json').write_text(json.dumps(transforms, indent=2)+'\n')
                (frozen/name).write_text(text)
            project = case/'owned-unity-project'
            build = case/'managed-build'; build.mkdir()
            case_code = 1
            try:
                shutil.copy2(HERE/'Fixture.csproj', build/'Fixture.csproj')
                status = execute([dotnet, 'build', '--disable-build-servers', str(build/'Fixture.csproj'),
                                  '-c', 'Release', '--configfile', str(HERE/'NuGet.Config'),
                                  '-p:FrozenSource='+str(frozen), '-p:FixtureDir='+str(HERE),
                                  '-p:UnityManaged='+str(managed), '-p:FrameworkReferenceDir='+str(framework),
                                  '-p:UseSharedCompilation=false'], case/'compile.log')
                assert status == 0, 'Actual production source compile failed: '+str(case)
                dll = build/'bin/Release/net472/SteamLinkLoadingSourceFixture.dll'
                shutil.copy2(dll, case/dll.name)
                assets = project/'Assets'; (assets/'Editor').mkdir(parents=True); (assets/'Plugins').mkdir()
                shutil.copy2(dll, assets/'Plugins'/dll.name)
                shutil.copy2(HERE/'EditorRunner.cs', assets/'Editor/EditorRunner.cs')
                for shader in HERE.glob('*.shader'):
                    shutil.copy2(shader, assets/shader.name)
                shutil.copy2(overlay, assets/'OriginalOverlay.shader')
                packages = project/'Packages'; packages.mkdir()
                (packages/'manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.xr":"1.0.0"}}\n')
                settings = project/'ProjectSettings'; settings.mkdir()
                (settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
                (settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
                env = os.environ.copy()
                env['VK_DRIVER_FILES'] = '/usr/share/vulkan/icd.d/intel_icd.json'
                status = execute(['xvfb-run', '-a', str(args.unity), '-batchmode', '-force-vulkan',
                                  '-projectPath', str(project), '-executeMethod', 'LoadingSourceEditorRunner.Start',
                                  '-loadingEvidence', str(case), '-sceneBank', str(frozen_bank),
                                  '-logFile', str(case/'unity.log')], case/'launcher.log', env=env)
                gpu = json.loads((case/'gpu-result.json').read_text())
                if mode == 'old-owner':
                    failure = (case/'failure.txt').read_text()
                    assert status != 0 and gpu['passed'] is False
                    assert 'Normal camera re-enable must prepare a fresh scene output' in failure, failure
                    assert gpu['guard_cells'] == 12 and gpu['normal_closes'] == 1, 'Mutant failed before the semantic recovery assertion'
                else:
                    assert status == 0 and gpu['passed'] is True, str(case)
                overall['stages'].append({'mode': mode, 'unity_exit_code': status,
                                          'expected_outcome_matched': True, 'gpu': gpu,
                                          'tested_assembly_sha256': sha(case/dll.name)})
                case_code = 0
            finally:
                receipt = cleanup('steamlink-loading-source', case_code, completion, ROOT, process_reaped=True)
                removed = []
                for owned in (project, build):
                    if owned.exists():
                        assert owned.is_dir() and not owned.is_symlink() and run in owned.parents
                        shutil.rmtree(owned); removed.append(str(owned.relative_to(run)))
                receipt['exact_owned_project_fallback'] = removed
                (case/'cleanup.json').write_text(json.dumps(receipt, indent=2)+'\n')
        assert pins == {str(p): sha(p) for p in inputs}, 'Source changed through child reap'
        code = 0
    finally:
        overall['exit_code'] = code
        overall['source_hashes_unchanged'] = pins == {str(p): sha(p) for p in inputs}
        (run/'result.json').write_text(json.dumps(overall, indent=2)+'\n')
        print(json.dumps({'evidence': str(run), 'exit_code': code}))
    return code


if __name__ == '__main__':
    raise SystemExit(main())
