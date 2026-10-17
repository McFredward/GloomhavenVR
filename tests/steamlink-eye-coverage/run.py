#!/usr/bin/env python3
"""Execute the actual runtime component in an owned Camera.Render GPU fixture."""
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


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--backdrop-only', action='store_true', help='Run only the new depthless owned-backdrop controls')
    parser.add_argument('--owner-only', action='store_true', help='Run only exact owner-getter controls, without raster cases')
    args = parser.parse_args()
    parser.error('Select one focused subset') if args.backdrop_only and args.owner_only else None
    root = HERE.parents[1]
    sys.path.insert(0, str(root/'scripts'))
    cleanup = importlib.import_module('town_run_retention').cleanup_completed_run
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    (run/'.keep').touch()
    log = run/'completion.log'; log.write_text('Owned runtime component evidence: '+str(run)+'\n')
    sources = [HERE/'Fixture.cs', HERE/'Surface.shader', Path(__file__),
               root/'src/GloomhavenVR/Core/SteamLink/SceneEyeCoverage.cs',
               root/'unity/Assets/Shaders/SteamLinkSceneCoverage.shader',
               root/'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs',
               root/'src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs']
    pins = {str(p.relative_to(root)):sha(p) for p in sources}
    frozen = run/'sources'; frozen.mkdir()
    for path in sources:
        dest = frozen/path.relative_to(root); dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(path,dest)
    (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    project = run/'owned-unity-project'
    assets = project/'Assets'; (assets/'Editor').mkdir(parents=True)
    shutil.copyfile(HERE/'Fixture.cs',assets/'Editor/Fixture.cs')
    shutil.copyfile(sources[3],assets/'Editor/SceneEyeCoverage.cs')
    shutil.copyfile(HERE/'Surface.shader',assets/'Surface.shader')
    shutil.copyfile(sources[4],assets/'SteamLinkSceneCoverage.shader')
    owner_source=sources[5].read_text()
    owner_begin=owner_source.index('    private static FlatScreen? _coverageOwner;')
    owner_end=owner_source.index('    private const float ScreenAspect',owner_begin)
    owner_excerpt=owner_source[owner_begin:owner_end]
    assert '_coverageOwner = this;' in owner_source
    assert 'if (ReferenceEquals(_coverageOwner, this)) _coverageOwner = null;' in sources[6].read_text()
    # Compile the actual getter with explicit test-only owner construction. The
    # complete game's Show/Hide methods are not replaced or claimed by this test.
    owner_fixture='''using UnityEngine;
namespace GloomhavenVR.WorldUI {
internal sealed class FlatScreen {
'''+owner_excerpt+'''
private bool _visible;
private Material _screenMaterial;
private GameObject _backQuad;
private Renderer _backRenderer, _quadRenderer;
internal FlatScreen(bool visible, Material material, Renderer screen, Renderer back) {
_coverageOwner=this;_visible=visible;_screenMaterial=material;_quadRenderer=screen;
_backRenderer=back;_backQuad=back != null ? back.gameObject : null;
RefreshOpaqueBackdropMaterial();
}
internal void Refresh() { RefreshOpaqueBackdropMaterial(); }
internal void Visible(bool value) { _visible=value; }
internal void Release() { if (ReferenceEquals(_coverageOwner,this)) _coverageOwner=null; }
}}
'''
    (assets/'Editor/OwnerExcerpt.cs').write_text(owner_fixture)
    (run/'owner-excerpt.cs').write_text(owner_fixture)
    packages = project/'Packages';packages.mkdir();(packages/'manifest.json').write_text('{"dependencies":{}}\n')
    settings = project/'ProjectSettings';settings.mkdir()
    (settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
    output = run/'gpu.json'
    command = ['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project),
               '-executeMethod','CoverageRuntimeFixture.Start','-coverageRuntimeResult',str(output),
               '-logFile',str(run/'unity.log')]
    if args.backdrop_only:command.append('-coverageBackdropOnly')
    if args.owner_only:command.append('-coverageOwnerOnly')
    env = os.environ.copy()
    if Path('/usr/share/vulkan/icd.d/intel_icd.json').is_file():env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
    code = 1
    try:
        with (run/'launcher.log').open('w') as stream:
            process = subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,env=env,start_new_session=True)
            try: status=process.wait(timeout=240)
            except BaseException:
                if process.poll() is None:os.killpg(process.pid,signal.SIGKILL)
                process.wait();raise
        assert all(sha(root/name)==pin for name,pin in pins.items()),'Inputs changed before child reap'
        assert status==0 and output.is_file(),'Actual runtime GPU fixture failed: '+str(run)
        result=json.loads(output.read_text());assert result['passed'],result.get('failure')
        code=0
    finally:
        receipt=cleanup('steamlink-eye-coverage',code,log,root,process_reaped=True)
        if project.is_dir() and not project.is_symlink():shutil.rmtree(project)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'owned_project_removed':str(project)},indent=2)+'\n')
        (run/'results.json').write_text(json.dumps({'exit_code':code,'scope':'EXACT_OWNER_GETTER_ACTUAL_UNITY_NO_RASTER' if args.owner_only else 'ACTUAL_RUNTIME_COMPONENT_OWNED_GAMMA_SDR_GPU',
            'source_hashes_unchanged':all(sha(root/name)==pin for name,pin in pins.items()),
            'production_hook_attached':False,'xr_executed':False,'windows_executed':False},indent=2)+'\n')
        print('Evidence: '+str(run))
    return code


if __name__=='__main__':raise SystemExit(main())
