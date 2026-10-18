#!/usr/bin/env python3
"""Bounded actual compiled v3 producer: independent4040/8x sources and codec inputs."""
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


def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()));log=run/'completion.log';log.write_text('Bounded robust source: '+str(run)+'\n')
    production=ROOT/'src/GloomhavenVR/Core/SteamLink';profile=ROOT/'tests/steamlink-scene-payload/profile-v3.json';bank=ROOT/'src/GloomhavenVR/Assets/SteamLink/scene.bundle'
    inputs=[*[p for p in HERE.iterdir() if p.is_file()],profile,bank,bank.parent/'provenance.json',
        *[production/name for name in ['SceneEyeOutput.cs','SceneAtlasProfile.cs','ScenePayloadAssets.cs','SteamLinkSceneRuntime.cs']],
        ROOT/'tests/steamlink-scene-output/profile_controls.py',ROOT/'unity/Assets/Shaders/SteamLinkSceneRobustAtlas.shader']
    pins={str(p.relative_to(ROOT)):sha(p) for p in inputs};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    copies=run/'sources';copies.mkdir()
    for source in inputs:shutil.copyfile(source,copies/(source.name+'-'+sha(source)[:12]))
    p=json.loads(profile.read_text());assert sha(profile)=='cf5f71fb00877573a9d2aa8b69405ec7af03523563e216b4e07738605a11863a'
    def vector(value):return 'new Vector2('+','.join(str(v)+'f' for v in value)+')'
    constants='using UnityEngine;\ninternal static class RobustProfile {internal static readonly Vector2[] Origins={'+','.join(vector(v) for v in p['coverage']['data_origins'])+'};internal static readonly Vector2[] HeaderOrigins={'+','.join(vector(v) for v in p['guard']['origins'])+'};internal static readonly int[] Codes={'+','.join(map(str,p['guard']['codes']))+'};}\n'
    (copies/'RobustProfile.cs').write_text(constants)
    project=run/'owned-unity-project';assets=project/'Assets';(assets/'Editor').mkdir(parents=True)
    for source in [HERE/'Fixture.cs',HERE/'EditorRunner.cs',production/'SceneEyeOutput.cs',production/'SceneAtlasProfile.cs']:shutil.copyfile(source,assets/'Editor'/source.name)
    for source in HERE.glob('*.shader'):shutil.copyfile(source,assets/source.name)
    (assets/'Editor/RobustProfile.cs').write_text(constants)
    packages=project/'Packages';packages.mkdir();(packages/'manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}\n')
    settings=project/'ProjectSettings';settings.mkdir();(settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');(settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
    copied_bank=run/'scene.bundle';shutil.copyfile(bank,copied_bank)
    code=1;children=[]
    def execute(command,target,timeout):
        with target.open('w') as out:
            child=subprocess.Popen(command,stdout=out,stderr=subprocess.STDOUT,start_new_session=True,env=env);children.append(child)
            try:return child.wait(timeout=timeout)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
    env=dict(os.environ);env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
    try:
        assert execute([sys.executable,'-B',str(ROOT/'tests/steamlink-scene-output/profile_controls.py')],run/'profile-controls.log',30)==0,'Production/profile binding failed'
        command=['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','RobustSourceEditorRunner.Start','-robustOutput',str(run),'-sceneBank',str(copied_bank),'-logFile',str(run/'unity.log')]
        (run/'command.json').write_text(json.dumps(command,indent=2)+'\n')
        status=execute(command,run/'launcher.log',240)
        assert (run/'gpu-result.json').is_file(),'Actual GPU result missing'
        result=json.loads((run/'gpu-result.json').read_text());assert status==0 and result['passed'],'Actual robust GPU proof failed'
        assert result['actual_samples']==8 and result['robust_draws']==2 and len(result['headers'])==24
        # Actual PNG orientation/header probes are verified in the final emitted
        # image too, so the codec consumer need not infer Unity's row convention.
        from PIL import Image
        for eye in ['left','right']:
            image=Image.open(run/(eye+'-v3-carrier.png')).convert('RGB');assert image.size==(4040,4040)
            for origin,expected in zip(p['guard']['origins'],p['guard']['codes']):
                point=tuple(int((v+64)/3168*4040) for v in origin)
                assert max(abs(c-expected) for c in image.getpixel(point))<=1,'Saved PNG top-down header convention differs'
        assert pins=={str(p.relative_to(ROOT)):sha(p) for p in inputs},'Frozen source/bank changed through child reap'
        deliverables=[run/(eye+'-'+name+'.png') for eye in ['left','right'] for name in ['source','v3-carrier','v2-carrier']]
        (run/'codec-inputs.json').write_text(json.dumps({'scope':'ACTUAL_COMPILED_UNITY_PRODUCER_NOT_IDEAL_REPAINT','profile_sha256':sha(profile),'bank_sha256':sha(bank),'coordinate_convention':'PNG top-down; source_q normalized, scene_atlas/mask_atlas virtual3168 pixels','probe_file':'gpu-result.json','images':{path.name:{'sha256':sha(path),'bytes':path.stat().st_size} for path in deliverables}},indent=2)+'\n')
        code=0
    finally:
        reaped=all(child.poll() is not None for child in children)
        receipt=cleanup_completed_run('steamlink-robust-coverage',code,log,ROOT,process_reaped=reaped)
        if reaped and project.is_dir() and not project.is_symlink():shutil.rmtree(project)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'all_children_reaped':reaped,'owned_project_removed':not project.exists()},indent=2)+'\n')
        (run/'results.json').write_text(json.dumps({'exit_code':code,'scope':'BOUNDED_ACTUAL_V3_PRODUCER_PAIR_4040_8X_NOT_XR_CODEC_OR_HEADSET','source_hashes_unchanged':{str(p.relative_to(ROOT)):sha(p)==pins[str(p.relative_to(ROOT))] for p in inputs}},indent=2)+'\n')
        print(json.dumps({'run':str(run),'exit_code':code}))
    return code


if __name__=='__main__':raise SystemExit(main())
