#!/usr/bin/env python3
"""Actual monitor owner/parser plus net472 checks; no native API or headset."""
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


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--output',type=Path,default=ROOT/'.planning/debug/steamlink-compositor-monitor')
    parser.add_argument('--native-transcript',type=Path,action='append',default=[])
    parser.add_argument('--repair-only',action='store_true')
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()))
    log=run/'completion.log';log.write_text('Debug-only public compositor monitor evidence: '+str(run)+'\n')
    source=ROOT/'src/GloomhavenVR/Core/SteamLink/SteamLinkCompositorMonitor.cs'
    quality=ROOT/'src/GloomhavenVR/Core/SteamLink/SteamLinkStreamQuality.cs'
    inputs=[source,quality,*sorted(path for path in HERE.iterdir() if path.is_file())]
    pins={str(path.relative_to(ROOT)):digest(path) for path in inputs}
    (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n')
    copies=run/'sources';copies.mkdir()
    for path in inputs:shutil.copyfile(path,copies/path.name)
    transcripts=[]
    for index,path in enumerate(args.native_transcript):
        target=run/('native-'+str(index)+'.jsonl');shutil.copyfile(path,target);transcripts.append(target)
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
    children=[];steps=[];code=1
    def execute(command,name,expected=None):
        with (run/(name+'.log')).open('w') as output:
            child=subprocess.Popen([str(item) for item in command],stdout=output,stderr=subprocess.STDOUT,start_new_session=True);children.append(child)
            try:status=child.wait(timeout=90)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
        text=(run/(name+'.log')).read_text()
        assert (status==0) if expected is None else (status!=0 and expected in text and 'error CS' not in text),name+' failed; see '+str(run)
        steps.append({'name':name,'command':[str(item) for item in command],'returncode':status,'semantic_negative_control':expected})
    try:
        project_dirs={}
        for name in ['Typecheck','Fixture']:
            project_dirs[name]=run/name;project_dirs[name].mkdir()
            project=project_dirs[name]/(name+'.csproj');shutil.copyfile(HERE/(name+'.csproj'),project)
            command=[dotnet,'build',project] if name=='Typecheck' else [dotnet,'run','--project',project]
            command+=['-c','Release','-p:SourceRoot='+str(ROOT),'-p:FixtureDir='+str(HERE),'-p:FrameworkReferenceDir='+str(framework),'-p:UseSharedCompilation=false']
            if name=='Fixture' and (transcripts or args.repair_only):command+=['--',*(['--repair-only'] if args.repair_only else []),*transcripts]
            execute(command,name)
        original=source.read_text()
        mutations={
            'DebugGate':('=> debug && requested;','=> requested;','Debug and real MR eligibility are both mandatory'),
            'TickRetry':('if (_attempted) { _phase = Phase.Spent; return; }','if (_attempted && _epoch == ulong.MaxValue) { _phase = Phase.Spent; return; }','Start failure is not retried every Tick'),
            'UnavailableZero':('if (fields[key + "_error"] != "0") fields[key] = "null";','if (fields[key + "_error"] == "never") fields[key] = "null";','Unavailable setting is null, never invented zero'),
            'SkipEof':('stopped = await child.StopAsync().ConfigureAwait(false);','stopped = true;','Off requests cooperative EOF exactly once'),
        }
        for name,(before,after,expected) in ({} if args.repair_only else mutations).items():
            assert original.count(before)==1
            mutant=copies/(name+'.cs');mutant.write_text(original.replace(before,after))
            command=[dotnet,'run','--project',project_dirs['Fixture']/'Fixture.csproj','-c','Release','-p:SourceRoot='+str(ROOT),'-p:FixtureDir='+str(HERE),'-p:SourceFile='+str(mutant),'-p:UseSharedCompilation=false']
            execute(command,name,expected)
        wiring=quality.read_text()
        assert wiring.count('SteamLinkCompositorMonitor.Tick(Requested(true));')==1,'Edge quality opt-out must not hide native MR diagnostics'
        assert 'SteamLinkCompositorMonitor.Reset(); State.Reset();' in wiring,'Actual shutdown resets observation and stream lease'
        assert pins=={str(path.relative_to(ROOT)):digest(path) for path in inputs},'Source changed during focused run'
        code=0
    finally:
        reaped=all(child.poll() is not None for child in children)
        cleanup=cleanup_completed_run('steamlink-compositor-monitor',code,log,ROOT,process_reaped=reaped)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':cleanup,'all_children_reaped':reaped},indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code':code,'steps':steps,'repair_only':args.repair_only,'native_transcripts':{str(path):digest(path) for path in transcripts},'scope':'ACTUAL_PRODUCTION_MONITOR_OWNER_PARSER_AND_NET472_NO_NATIVE_PROCESS_OR_OPENVR'},indent=2)+'\n')
        print(json.dumps({'run':str(run),'exit_code':code}))
    return code


if __name__=='__main__':raise SystemExit(main())
