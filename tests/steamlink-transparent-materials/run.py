#!/usr/bin/env python3
"""Actual shipping-bank/head-only material-scope GPU fixture, not native VFX proof."""
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
sys.dont_write_bytecode=True
HERE=Path(__file__).resolve().parent


def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--bank',type=Path,default=Path('/home/claw/gloomhaven_vr/src/GloomhavenVR/Assets/SteamLink/scene.bundle'))
    parser.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    args=parser.parse_args();root=HERE.parents[1]
    sys.path.insert(0,str(root/'scripts'));cleanup=importlib.import_module('town_run_retention').cleanup_completed_run
    args.output.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()));(run/'.keep').touch()
    log=run/'completion.log';log.write_text(str(run)+'\n');copies=run/'sources';copies.mkdir()
    live=[HERE/'Fixture.cs',HERE/'variants.json',Path(__file__),root/'src/GloomhavenVR/Core/SteamLink/SceneTransparentMaterials.cs']
    manifest=json.loads((HERE/'variants.json').read_text());selected={'GloomhavenVR/Overlay','GloomhavenVR/EnvParticleAlpha','GloomhavenVR/EnvParticleAdd'}
    selected_entries=[row for row in manifest['variants'] if row['original_shader'] in selected]
    pins={str(p):sha(p) for p in live};pins[str(args.bank)]=sha(args.bank)
    frozen_bank=run/'actual-shipping-scene.bundle';shutil.copyfile(args.bank,frozen_bank)
    project=run/'owned-unity-project';assets=project/'Assets';(assets/'Editor').mkdir(parents=True)
    shutil.copyfile(HERE/'Fixture.cs',assets/'Editor/Fixture.cs')
    shutil.copyfile(live[3],assets/'Editor/SceneTransparentMaterials.cs')
    for row in selected_entries:
        source=root/row['source'];assert sha(source)==row['source_sha256'];pins[str(source)]=sha(source)
        folder=assets/'Originals'/source.parent.name;folder.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,folder/source.name)
        for include in source.parent.glob('*.cginc'):
            pins[str(include)]=sha(include);shutil.copyfile(include,folder/include.name)
    for path,pin in pins.items():
        source=Path(path);dest=copies/(source.name+'-'+pin[:12]);shutil.copyfile(source,dest)
    (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    packages=project/'Packages';packages.mkdir();(packages/'manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0"}}\n')
    settings=project/'ProjectSettings';settings.mkdir();(settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
    output=run/'gpu.json';code=1
    command=['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','TransparentMaterialFixture.Start','-transparentResult',str(output),'-transparentBank',str(frozen_bank),'-logFile',str(run/'unity.log')]
    env=os.environ.copy()
    if Path('/usr/share/vulkan/icd.d/intel_icd.json').is_file():env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
    try:
        with (run/'launcher.log').open('w') as stream:
            process=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,env=env,start_new_session=True)
            try:status=process.wait(timeout=240)
            except BaseException:
                if process.poll() is None:os.killpg(process.pid,signal.SIGKILL)
                process.wait();raise
        assert all(sha(Path(p))==pin for p,pin in pins.items()),'Frozen input changed through child reap'
        assert status==0 and output.is_file(),'Actual shipping-bank GPU fixture failed: '+str(run)
        assert json.loads(output.read_text())['passed'];code=0
    finally:
        receipt=cleanup('steamlink-transparent-materials',code,log,root,process_reaped=True)
        if project.is_dir() and not project.is_symlink():shutil.rmtree(project)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'owned_project_removed':str(project)},indent=2)+'\n')
        (run/'results.json').write_text(json.dumps({'exit_code':code,'scope':'ACTUAL_SHIPPING_BANK_OWNED_HEAD_ONLY_MATERIAL_GPU','shipping_bank_sha256':pins[str(args.bank)],'source_hashes_unchanged':all(sha(Path(p))==pin for p,pin in pins.items()),'xr_executed':False,'native_vfx_certified':False},indent=2)+'\n')
        print('Evidence: '+str(run))
    return code


if __name__=='__main__':raise SystemExit(main())
