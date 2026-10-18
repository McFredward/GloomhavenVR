#!/usr/bin/env python3
"""Focused real Unity pack/zero-base proof, not independent Steam Link transport."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[2]
HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'.planning/debug/steamlink-projection-witness')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()))
    log=run/'completion.log';log.write_text('evidence: '+str(run)+'\n')
    paths=[ROOT/'src/GloomhavenVR/Core/SteamLink/SceneEyeOutput.cs',ROOT/'src/GloomhavenVR/Core/SteamLink/SceneAtlasProfile.cs',
           ROOT/'unity/Assets/Shaders/SteamLinkSceneRobustAtlas.shader',HERE/'EditorRunner.cs',HERE/'run.py']
    digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
    pins={str(p.relative_to(ROOT)):digest(p) for p in paths}
    (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    sources=run/'sources';sources.mkdir()
    for path in paths:shutil.copyfile(path,sources/path.name)
    project=run/'unity-project';editor=project/'Assets/Editor';editor.mkdir(parents=True)
    shaders=project/'Assets/Shaders';shaders.mkdir()
    for path in paths[:-1]:shutil.copyfile(path,(shaders if path.suffix=='.shader' else editor)/path.name)
    (project/'Packages').mkdir();(project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.imageconversion":"1.0.0"}}\n')
    settings=project/'ProjectSettings';settings.mkdir()
    (settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
    child=None;code=1
    try:
        env=dict(os.environ);env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
        command=['xvfb-run','-a','-s','-screen 0 800x600x24','/home/claw/unity-2021.3.5/Editor/Unity',
                 '-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','ProjectionWitnessRunner.Start',
                 '-witnessOutput',str(run),'-logFile',str(run/'unity.log')]
        with (run/'launch.log').open('w') as output:
            child=subprocess.Popen(command,stdout=output,stderr=subprocess.STDOUT,start_new_session=True,env=env)
            try:code=child.wait(timeout=240)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
        assert code==0,'Actual GPU witness failed; see '+str(run)
        result=json.loads((run/'gpu-result.json').read_text());assert result['passed']
        assert pins=={str(p.relative_to(ROOT)):digest(p) for p in paths},'Source changed before test completed'
        code=0
    finally:
        reaped=child is None or child.poll() is not None
        cleanup=cleanup_completed_run('steamlink-projection-witness',code,log,ROOT,process_reaped=reaped)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':cleanup,'all_children_reaped':reaped},indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code':code,'scope':'ACTUAL_UNITY_ZERO_INPUT_PACK_AND_ANALYTIC_TRANSPORT_WITNESS_ONLY'},indent=2)+'\n')
        print(json.dumps({'run':str(run),'exit_code':code}))
    return code


if __name__=='__main__':raise SystemExit(main())
