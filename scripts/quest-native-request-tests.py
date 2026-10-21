#!/usr/bin/env python3
"""Exercise actual original Apparance scheduling/Unity callbacks with controlled native IO."""
import argparse,ctypes,hashlib,importlib.util,json,os,shutil,subprocess,sys,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run
spec=importlib.util.spec_from_file_location('quest_owned_reap',ROOT/'scripts/quest-world-material-tests.py')
owned=importlib.util.module_from_spec(spec);spec.loader.exec_module(owned)

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--control',choices=('fairness','scope'))
    args=parser.parse_args()
    base=ROOT/'.planning/debug/quest-native-requests';base.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=base));project=run/'unity';editor=project/'Assets/Runtime';editor.mkdir(parents=True)
    plugins=project/'Assets/Plugins';plugins.mkdir()
    log=run/'runner.log';log.write_text('Quest native request evidence: '+str(run)+'\n');print(log.read_text(),end='',flush=True)
    ctypes.CDLL(None).prctl(36,1,0,0,0)
    original=(ROOT/'ressources').resolve()/'GH_Data/Managed'
    fixture=ROOT/'tests/QuestScenarioEvidence.Tests'
    dotnet=Path.home()/'.dotnet';env=dict(os.environ,DOTNET_ROOT=str(dotnet),MSBUILDDISABLENODEREUSE='1')
    csc=sorted((dotnet/'sdk').glob('8.*/Roslyn/bincore/csc.dll'))[-1]
    boundary=plugins/'NativeRequestsBoundary.dll'
    compiler=[str(dotnet/'dotnet'),str(csc),'-noconfig','-nostdlib','-target:library','-warnaserror','-out:'+str(boundary),'-r:'+str(original/'mscorlib.dll'),'-r:'+str(original/'System.dll'),'-r:'+str(original/'System.Core.dll'),str(fixture/'NativeRequestsBoundary.cs')]
    code=1;process=None
    try:
        with (run/'boundary-compile.log').open('w') as output:subprocess.run(compiler,env=env,check=True,stdout=output,stderr=subprocess.STDOUT,timeout=60)
        tool=run/'patcher';tool.mkdir();cecil=Path.home()/'.nuget/packages/mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll'
        (tool/'patcher.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><UseSharedCompilation>false</UseSharedCompilation><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup><ItemGroup><Reference Include="Mono.Cecil"><HintPath>'+str(cecil)+'</HintPath></Reference></ItemGroup></Project>')
        shutil.copy2(fixture/'NativeRequestsPatch.cs',tool/'Program.cs')
        with (run/'patcher-build.log').open('w') as output:subprocess.run([str(dotnet/'dotnet'),'build',str(tool/'patcher.csproj'),'-c','Release','-p:ImportDirectoryBuildProps=false','-p:ImportDirectoryBuildTargets=false'],env=env,check=True,stdout=output,stderr=subprocess.STDOUT,timeout=90)
        with (run/'patcher.log').open('w') as output:subprocess.run([str(dotnet/'dotnet'),str(tool/'bin/Release/net8.0/patcher.dll'),str(original/'Apparance.Net.dll'),str(boundary),str(plugins/'Apparance.Net.dll'),str(run/'original-bodies.json')],env=env,check=True,stdout=output,stderr=subprocess.STDOUT,timeout=30)
        shutil.copy2(original/'Apparance.Unity.dll',plugins/'Apparance.Unity.dll')
        for folder,name in [('harmonyx/2.7.0/lib/net45','0Harmony.dll'),('monomod.runtimedetour/21.12.13.1/lib/net452','MonoMod.RuntimeDetour.dll'),('monomod.utils/21.12.13.1/lib/net452','MonoMod.Utils.dll')]:shutil.copy2(Path.home()/'.nuget/packages'/folder/name,plugins/name)
        for binary in (Path.home()/'.nuget/packages/mono.cecil/0.11.4/lib/net40').glob('Mono.Cecil*.dll'):shutil.copy2(binary,plugins/binary.name)
        source=(ROOT/'src/GloomhavenVR/Core/QuestProceduralRequests.cs').read_text()
        if args.control=='fairness':
            before='Submit.Invoke(native, DeltaTime);';assert source.count(before)==1;source=source.replace(before,'GC.KeepAlive(native); /* injected submission starvation */',1)
        elif args.control=='scope':
            before='room.visibility != ProceduralMapTile.Visibility.All';assert source.count(before)==1;source=source.replace(before,'false',1)
        (editor/'Requests.cs').write_text('#nullable enable\n'+source.replace('namespace GloomhavenVR.Core;','namespace GloomhavenVR.Core {')+'\n}\n')
        native_source=(ROOT/'ressources').resolve().parent/'decompiled/GH.Runtime/ProceduralBase.cs'
        shutil.copy2(native_source,editor/'ProceduralBase.cs');shutil.copy2(native_source.with_name('IProceduralContentMonitor.cs'),editor/'IProceduralContentMonitor.cs');shutil.copy2(fixture/'NativeRequestsProbe.cs',editor/'Probe.cs')
        (project/'Packages').mkdir();(project/'Packages/manifest.json').write_text('{"dependencies":{}}\n')
        (project/'ProjectSettings').mkdir();(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (run/'inputs.json').write_text(json.dumps({'originalAssemblies':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [original/'Apparance.Net.dll',original/'Apparance.Unity.dll']},'originalProceduralBaseSource':str(native_source),'originalProceduralBaseSha256':hashlib.sha256(native_source.read_bytes()).hexdigest(),'originalAssemblyMethodsPreserved':True,'nativeInterop':'Controlled twelve-import boundary; original engine DLL not executed in this fixture.'},indent=2)+'\n')
        command=['/home/claw/unity-2021.3.5/Editor/Unity','-batchmode','-nographics','-projectPath',str(project),'-executeMethod','QuestNativeRequestsProbe.Run','-logFile',str(run/'unity.log')]
        process=subprocess.Popen(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,start_new_session=True)
        code=process.wait(timeout=150)
        if hashlib.sha256((plugins/'Apparance.Unity.dll').read_bytes()).digest() != hashlib.sha256((original/'Apparance.Unity.dll').read_bytes()).digest():
            raise AssertionError('Original Apparance.Unity assembly changed during import')
        if args.control:
            expected='Real engine postfix submits only the already queued wall through original Update and BeginContentUpdate'
            if code==0 or ('InvalidOperationException: '+expected) not in (run/'unity.log').read_text():raise AssertionError('Actual native scheduling control did not fail its exact assertion')
            (run/'control.json').write_text(json.dumps({'control':args.control,'rejected':True,'expected':expected},indent=2)+'\n');print('PASS native scheduling control rejected: '+args.control,flush=True);code=0
        else:
            if code:raise RuntimeError('Actual native request fixture failed: '+str(run/'unity.log'))
            report=json.loads((project/'ProbeOutput/result.json').read_text());assert report['assertions']>=15 and report['actualOriginalManagedAssemblies'] is True
            (run/'source-hashes.json').write_text(json.dumps({'source':hashlib.sha256(source.encode()).hexdigest(),'assertions':report['assertions'],'headsetVerified':False},indent=2)+'\n');print('PASS actual native requests: '+str(report['assertions'])+' assertions',flush=True)
    except BaseException:
        code=1
        raise
    finally:
        if process is not None and process.poll() is None:process.kill();process.wait()
        (run/'process-cleanup.json').write_text(json.dumps(owned.reap_owned(process.pid if process else 1073741823),indent=2)+'\n')
        (run/'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-native-requests',code,log,ROOT,process_reaped=True),indent=2)+'\n')

if __name__=='__main__':main()
