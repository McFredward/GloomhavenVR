#!/usr/bin/env python3
"""Explicit projection-probe owner, unchanged-scene callback ports and real Unity/net472 types."""
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


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'.planning/debug/steamlink-projection-probe')
    parser.add_argument('--skip-typecheck',action='store_true',help='Run affected behavioral checks only; inherit an unchanged prior real-Unity typecheck explicitly.')
    parser.add_argument('--only-mutants',help='Comma-separated affected semantic controls; inherit already passing positives and other controls.')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()))
    log=run/'completion.log';log.write_text('Projection probe evidence: '+str(run)+'\n')
    source=ROOT/'src/GloomhavenVR/Core/SteamLink/SteamLinkProjectionProbe.cs'
    runtime=ROOT/'src/GloomhavenVR/Core/SteamLink/SteamLinkSceneRuntime.cs'
    ui=ROOT/'src/GloomhavenVR/WorldUI/Options/VROptionsTab.SteamLinkProjectionProbe.cs'
    cheats=ROOT/'src/GloomhavenVR/WorldUI/Options/VROptionsTab.Cheats.cs'
    loc=ROOT/'src/GloomhavenVR/Core/Loc/Loc.SteamLinkProjectionProbe.cs'
    inputs=[source,runtime,ui,cheats,loc,*sorted(p for p in HERE.iterdir() if p.is_file())]
    pins={str(p.relative_to(ROOT)):sha(p) for p in inputs};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    copies=run/'sources';copies.mkdir()
    for i,path in enumerate(inputs):shutil.copyfile(path,copies/(str(i)+'-'+path.name))
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    unity=Path('/home/claw/unity-2021.3.5/Editor/Data/Managed/UnityEngine')
    if not (unity/'UnityEngine.CoreModule.dll').is_file():unity=Path.home()/'.nuget/packages/unityengine.modules/2021.3.5/lib/net35'
    assert (unity/'UnityEngine.CoreModule.dll').is_file(),'Actual Unity2021.3.5 metadata missing'
    (run/'metadata-pins.json').write_text(json.dumps({str(unity/'UnityEngine.CoreModule.dll'):sha(unity/'UnityEngine.CoreModule.dll')},indent=2)+'\n')
    children=[];steps=[];code=1
    def execute(command,name,expected=None):
        with (run/(name+'.log')).open('w') as output:
            child=subprocess.Popen([str(c) for c in command],stdout=output,stderr=subprocess.STDOUT,start_new_session=True);children.append(child)
            try:status=child.wait(timeout=90)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
        output=(run/(name+'.log')).read_text()
        assert (status==0) if expected is None else (status!=0 and expected in output and 'error CS' not in output),name+' failed: '+str(run)
        steps.append({'name':name,'returncode':status,'expected_semantic_failure':expected})
    projects={}
    def command(kind,mutant=None,property_name="SourceFile"):
        project=projects[kind]
        result=[dotnet,'build' if kind=='Typecheck' else 'run','--project',project] if kind!='Typecheck' else [dotnet,'build',project]
        result+=['-c','Release','-p:SourceRoot='+str(ROOT),'-p:FixtureDir='+str(HERE),'-p:FrameworkReferenceDir='+str(framework),'-p:UnityManaged='+str(unity),'-p:UseSharedCompilation=false']
        if mutant:result+=['-p:'+property_name+'='+str(mutant)]
        return result
    try:
        for kind in (['Fixture','Callback'] if args.skip_typecheck else ['Typecheck','Fixture','Callback']):
            folder=run/('owned-'+kind);folder.mkdir();project=folder/(kind+'.csproj');shutil.copyfile(HERE/(kind+'.csproj'),project);projects[kind]=project
            if not args.only_mutants:execute(command(kind),kind)
        original=source.read_text();runtime_text=runtime.read_text()
        ui_text=ui.read_text()
        mutations={
            'DebugGate':('Fixture',original,'VRLog.WantsDebug && VRSession.IsRunning','VRSession.IsRunning','Debug logging is mandatory','SourceFile'),
            'SourceGate':('Fixture',original,' && SteamLinkSceneRuntime.ProjectionProbeSourceReady;',';','actual admitted robust v3 source is mandatory','SourceFile'),
            'ImmediateStart':('Fixture',original,'_worker = Task.Run(() => RunAsync(epoch, stop));','_worker = Task.CompletedTask;','launch starts immediately without a Tick or menu close','SourceFile'),
            'ReopenStop':('Fixture',original,'(menuOpen && _menuHasClosed)','(menuOpen && _menuHasClosed && _epoch == ulong.MaxValue)','reopening menu immediately revokes preview','SourceFile'),
            'LateRevoke':('Fixture',original,'lock (_gate) if (_epoch == epoch) { _wanted = false; _phase = Phase.Stopping; }','lock (_gate) if (_epoch == ulong.MaxValue) { _wanted = false; _phase = Phase.Stopping; }','EOF/exit/stopped/failure revokes preview ownership before delayed cleanup: eof','SourceFile'),
            'AutoClose':('Fixture',ui_text,'try { Close(); }','try { _ = IsOpen; }','accepted direct action automatically closes options','UIFile'),
            'LocalMetadata':('Fixture',original,'input.StartsWith("SFNP_PROJECTION_LOCAL_V2 ", StringComparison.Ordinal)','input.StartsWith("SFNP_PROJECTION_IGNORED_V2 ", StringComparison.Ordinal)','known bounded native witness/refusal metadata preserved','SourceFile'),
            'LocalNotReady':('Fixture',original,'if (line == ReadyLine)','if (line == ReadyLine || line.StartsWith("SFNP_PROJECTION_LOCAL_V2 ", StringComparison.Ordinal))','local visibility metadata never grants Ready','SourceFile'),
            'KeepGame':('Callback',runtime_text,'_pack!.TryRender(source, destination, eye)','_pack!.TryRender(SteamLinkProjectionProbe.ApiReady ? new RenderTexture() : source, destination, eye)','READY never substitutes the actual scene source: ready/Left','SourceFile'),
        }
        if args.only_mutants:
            selected=set(args.only_mutants.split(','))
            assert selected and selected<=mutations.keys(),'Unknown semantic controls requested'
            mutations={name:case for name,case in mutations.items() if name in selected}
        for name,(kind,content,before,after,expected,property_name) in mutations.items():
            assert content.count(before)==1,(name,content.count(before))
            mutant=copies/(name+'.cs');mutant.write_text(content.replace(before,after))
            execute(command(kind,mutant,property_name),name,expected)
        assert cheats.read_text().count('rows += BuildSteamLinkProjectionProbe();')==1
        assert ui_text.count('if (!SteamLinkProjectionProbe.Start()) return;')==1
        assert 'if (ContentRoot == null || !CheatsAvailable) return 0;' in ui_text
        assert 'Loc.SteamLinkProjectionProbeStop, SteamLinkProjectionProbe.Stop' in ui_text
        assert '&& _pack!.Profile == SceneAtlasProfile.RobustProfile;' in runtime_text
        assert 'TransportPrepared(source.width, source.height)' in runtime_text
        assert runtime_text.count('_pack!.TryRender(source, destination, eye)')==1
        for removed in ['RenderTexture','Graphics.','GL.','InputForFrame','FrameLatch']:
            assert removed not in original,'Preview must never own or change images: '+removed
        assert 'SteamLinkProjectionProbe.InputForFrame' not in runtime_text
        assert pins=={str(p.relative_to(ROOT)):sha(p) for p in inputs},'Sources changed during run'
        code=0
    finally:
        reaped=all(c.poll() is not None for c in children)
        retained=cleanup_completed_run('steamlink-projection-probe',code,log,ROOT,process_reaped=reaped)
        removed=[]
        if reaped:
            for path in projects.values():
                for cache in ['bin','obj']:
                    folder=path.parent/cache
                    if folder.is_dir() and not folder.is_symlink():shutil.rmtree(folder);removed.append(str(folder))
        (run/'cleanup.json').write_text(json.dumps({'api':retained,'all_children_reaped':reaped,'owned_caches_removed':removed},indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code':code,'steps':steps,'scope':'ACTUAL_DIRECT_START_OWNER_PROTOCOL_UI_AND_UNCHANGED_SCENE_CALLBACK_PORTS_REAL_UNITY_NET472','helper_or_OpenVR_executed':False,'headset_picture_proven':False,'typecheck_reused_not_rerun':args.skip_typecheck or bool(args.only_mutants),'mutation_subset':args.only_mutants},indent=2)+'\n')
        print(json.dumps({'run':str(run),'exit_code':code}))
    return code

if __name__=='__main__':raise SystemExit(main())
