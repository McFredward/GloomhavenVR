#!/usr/bin/env python3
"""Bounded async lease/epoch and exact helper protocol; no SteamVR/settings writes."""
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
ROOT=Path(__file__).resolve().parents[2];HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(ROOT/'scripts'))
from town_run_retention import cleanup_completed_run


def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output',type=Path,default=ROOT/'.planning/debug/steamlink-stream-quality')
    parser.add_argument('--native-transcript',type=Path,action='append',default=[])
    args=parser.parse_args();args.output.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()))
    log=run/'completion.log';log.write_text('Async stream quality focused controls: '+str(run)+'\n')
    source=ROOT/'src/GloomhavenVR/Core/SteamLink/SteamLinkStreamQuality.cs'
    inputs=[source,*[p for p in HERE.iterdir() if p.is_file()]];pins={str(p.relative_to(ROOT)):digest(p) for p in inputs}
    (run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n');copies=run/'sources';copies.mkdir()
    for p in inputs:shutil.copyfile(p,copies/p.name)
    transcripts=[]
    for i,p in enumerate(args.native_transcript):
        target=run/('native-'+str(i)+'.jsonl');shutil.copyfile(p,target);assert digest(p)==digest(target);transcripts.append(target)
    (run/'native-transcript-pins.json').write_text(json.dumps({str(p):digest(p) for p in transcripts},indent=2)+'\n')
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet');children=[];code=1
    def execute(command,target):
        with target.open('w') as out:
            child=subprocess.Popen(command,stdout=out,stderr=subprocess.STDOUT,start_new_session=True);children.append(child)
            try:return child.wait(timeout=90)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
    try:
        framework=Path.home()/'.nuget/packages/microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
        for name in ['Typecheck','Fixture']:
            d=run/name;d.mkdir();project=d/(name+'.csproj');shutil.copyfile(HERE/(name+'.csproj'),project)
            command=[dotnet,'build' if name=='Typecheck' else 'run','--project',str(project)] if name=='Fixture' else [dotnet,'build',str(project)]
            command += ['-c','Release','-p:SourceRoot='+str(ROOT),'-p:FixtureDir='+str(HERE),'-p:FrameworkReferenceDir='+str(framework),'-p:UseSharedCompilation=false']
            if name=='Fixture' and transcripts:command += ['--',*[str(p) for p in transcripts]]
            status=execute(command,run/(name+'.log'));assert status==0,name+' failed; see '+str(run)
        assert pins=={str(p.relative_to(ROOT)):digest(p) for p in inputs},'Source changed before children reaped'
        code=0
    finally:
        reaped=all(c.poll() is not None for c in children);receipt=cleanup_completed_run('steamlink-stream-quality',code,log,ROOT,process_reaped=reaped)
        if reaped:
            for name in ['Typecheck','Fixture']:
                for cache in ['bin','obj']:
                    p=run/name/cache
                    if p.is_dir() and not p.is_symlink():shutil.rmtree(p)
        (run/'cleanup.json').write_text(json.dumps({'retention_api':receipt,'all_children_reaped':reaped},indent=2)+'\n')
        (run/'result.json').write_text(json.dumps({'exit_code':code,'scope':'STATE_AND_PROTOCOL_ONLY_NO_PROCESS_OR_OPENVR_SETTINGS_EXECUTED'},indent=2)+'\n')
        print(json.dumps({'run':str(run),'exit_code':code}))
    return code


if __name__=='__main__':raise SystemExit(main())
