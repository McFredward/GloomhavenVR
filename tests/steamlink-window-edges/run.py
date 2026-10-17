#!/usr/bin/env python3
"""Actual UI/window and shipping-bank spatial coverage reconstruction fixture."""
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
    parser.add_argument('--bank',type=Path,default=HERE.parents[1]/'src/GloomhavenVR/Assets/SteamLink/scene.bundle')
    parser.add_argument('--subset',choices=['full','clipping','edge-witness','comparison','unsharp'],default='full')
    parser.add_argument('--expect-linear-rejection',action='store_true')
    parser.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    args=parser.parse_args();root=HERE.parents[1]
    if args.expect_linear_rejection and args.subset!='edge-witness':parser.error('--expect-linear-rejection requires --subset edge-witness')
    sys.path.insert(0,str(root/'scripts'));cleanup=importlib.import_module('town_run_retention').cleanup_completed_run
    args.output.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()));(run/'.keep').touch()
    log=run/'completion.log';log.write_text(str(run)+'\n');copies=run/'sources';copies.mkdir()
    live=[HERE/'Fixture.cs',HERE/'Probe.shader',HERE/'LinearControl.shader',HERE/'SpatialComparison.shader',Path(__file__)]+[root/'src/GloomhavenVR/Core/SteamLink'/name for name in ['SceneEyeCoverage.cs','SceneEyeOutput.cs','SceneAtlasProfile.cs']]
    live += [root/'src/GloomhavenVR/WorldUI/MrBackingMaterialise.cs',root/'src/GloomhavenVR/WorldUI/Materialise/WindowMaterialiseField.cs']
    original=root/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader';live.append(original)
    pins={str(p):sha(p) for p in live};pins[str(args.bank)]=sha(args.bank)
    frozen_bank=run/'actual-shipping-scene.bundle';shutil.copyfile(args.bank,frozen_bank)
    project=run/'owned-unity-project';assets=project/'Assets';(assets/'Editor').mkdir(parents=True)
    for source in live:
        if source.suffix=='.cs' and source.name not in ['MrBackingMaterialise.cs','WindowMaterialiseField.cs']:shutil.copyfile(source,assets/'Editor'/source.name)
        elif source.suffix=='.shader':shutil.copyfile(source,assets/source.name)
    for path,pin in pins.items():
        source=Path(path);dest=copies/(source.name+'-'+pin[:12]);shutil.copyfile(source,dest)
    (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    packages=project/'Packages';packages.mkdir();(packages/'manifest.json').write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.ugui":"1.0.0"}}\n')
    settings=project/'ProjectSettings';settings.mkdir();(settings/'ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (settings/'ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: 0\n')
    output=run/'gpu.json';code=1;children=[]
    def execute(command, logfile, timeout, child_env=None):
        with logfile.open('w') as stream:
            child=subprocess.Popen(command,stdout=stream,stderr=subprocess.STDOUT,env=child_env,start_new_session=True);children.append(child)
            try:return child.wait(timeout=timeout)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
    command=['xvfb-run','-a',str(args.unity),'-batchmode','-force-vulkan','-projectPath',str(project),'-executeMethod','WindowEdgeFixture.Start','-windowResult',str(output),'-windowBank',str(frozen_bank),'-logFile',str(run/'unity.log')]
    if args.subset=='unsharp':command+=['-windowUnsharpOnly']
    elif args.subset=='comparison':command+=['-windowCompareOnly']
    elif args.subset=='clipping':command+=['-windowClipOnly']
    elif args.subset=='edge-witness':command+=['-windowEdgeOnly']
    env=os.environ.copy()
    if Path('/usr/share/vulkan/icd.d/intel_icd.json').is_file():env['VK_DRIVER_FILES']='/usr/share/vulkan/icd.d/intel_icd.json'
    try:
        # Compile unchanged production C#10 bodies with Roslyn against the actual
        # Unity2021.3.5 API. The Editor's C#8 frontend cannot parse file namespaces.
        framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        managed=args.unity.parent/'Data/Managed/UnityEngine'
        friend=run/'friend.cs';friend.write_text('[assembly:System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]\n')
        binary=run/'window-materialise-production.dll'
        compile_command=[str(Path.home()/'.dotnet/dotnet'),str(Path.home()/'.dotnet/sdk/8.0.423/Roslyn/bincore/csc.dll'),'-nologo','-noconfig','-nostdlib+','-target:library','-langversion:latest','-nullable:enable','-out:'+str(binary),'-r:'+str(framework/'mscorlib.dll'),'-r:'+str(framework/'System.dll'),'-r:'+str(managed/'UnityEngine.CoreModule.dll'),str(friend),str(live[-3]),str(live[-2])]
        (run/'production-helper-command.json').write_text(json.dumps(compile_command,indent=2)+'\n')
        assert execute(compile_command,run/'production-helper-compile.log',30)==0,'Actual production helper compilation failed'
        (assets/'Plugins').mkdir();shutil.copyfile(binary,assets/'Plugins'/binary.name)
        status=execute(command,run/'launcher.log',240,env)
        assert all(sha(Path(p))==pin for p,pin in pins.items()),'Frozen input changed through child reap'
        assert output.is_file(),'Actual shipping-bank GPU fixture has no result: '+str(run)
        result=json.loads(output.read_text())
        if args.expect_linear_rejection:
            assert status!=0 and not result['passed'] and 'Sharper aggregate 10-90 edge width' in result['failure'],'Linear regression did not fail causally'
        else:assert status==0 and result['passed'],'Actual shipping-bank GPU fixture failed: '+str(run)
        code=0
    finally:
        receipt=cleanup('steamlink-window-edges',code,log,root,process_reaped=all(child.poll() is not None for child in children))
        if project.is_dir() and not project.is_symlink():shutil.rmtree(project)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'owned_project_removed':str(project)},indent=2)+'\n')
        (run/'results.json').write_text(json.dumps({'exit_code':code,'scope':'ACTUAL_WINDOW_UI_AND_SHIPPING_ATLAS_GPU','shipping_bank_sha256':pins[str(args.bank)],'source_hashes_unchanged':all(sha(Path(p))==pin for p,pin in pins.items()),'subset':args.subset,'expected_linear_rejection':args.expect_linear_rejection,'xr_executed':False,'native_vfx_certified':False},indent=2)+'\n')
        print('Evidence: '+str(run))
    return code


if __name__=='__main__':raise SystemExit(main())
