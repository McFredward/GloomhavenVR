#!/usr/bin/env python3
"""Execute exact Results ingress, packet emission and retry kernels with declared native ports."""
import argparse, hashlib, json, os
from pathlib import Path
import shutil, signal, subprocess, tempfile
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
def member(text, signature):
    start=text.index(signature); at=text.index('{',start); depth=0
    for end in range(at,len(text)):
        depth+=(text[end]=='{')-(text[end]=='}')
        if depth==0:return text[start:end+1]
    raise ValueError(signature)
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/results-transport-runtime');args=p.parse_args()
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()));completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n');print('evidence: '+str(run),flush=True)
    source=ROOT/'src/GloomhavenVR/Net';driver=(source/'Avatar/NetAvatarDriver.Results.cs').read_text();transport=(source/'FfsNetTransport.cs').read_text()
    kernel='using System;using System.Collections.Generic;using GloomhavenVR.Core;namespace GloomhavenVR.Net;\n'
    kernel+='internal sealed partial class ResultsIngress {\n'
    for name in ['_pendingResults','_resultsRevisions','_resultsBases','_resultsLatestData','_resultsQueued']:
        kernel+=next(line for line in driver.splitlines() if 'private readonly Dictionary' in line and ' '+name+' ' in line)+'\n'
    kernel+=member(driver,'private bool QueueResults(')+'\n'+member(driver,'private void QueueResultsReady(')+'\n}\n'
    kernel+='internal sealed partial class FfsNetTransport {\n'+member(transport,'internal void TickFragments(')+'\n'+member(transport,'private void SendToken(')+'\n}\n'
    kernel=kernel.replace('VRLog.Error(', 'ResultsDiagnostics.Error(')
    paths=[source/'Avatar/NetAvatarDriver.Results.cs',source/'FfsNetTransport.cs',Path(__file__),*sorted((ROOT/'scripts/results-transport-runtime').glob('*.cs'))]
    for name in ['ScenarioResultsState.cs','ScenarioResultsDelta.cs','ExtrasSendQueue.cs','ExtrasFragments.cs','PresentationBatch.cs'] :paths.append(source/name)
    pins={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n');(run/'ActualKernels.cs').write_text(kernel)
    status=1;child=None;results=[]
    try:
        variants=[('production','','',''),('lossy-results','            && !PresentationBatch.HasResultsPage(bytes, bytes.Length)','', 'Results emission requests native reliable ordered delivery'),('lost-failed-page','_failedResultsPage = page;', '_failedResultsPage = null;', 'native rejection retains the exact pending Results page'),('missing-delta-expand','complete = ScenarioResultsDelta.Expand(basis, latest);','complete = latest;','a sparse picture waits without replacing the complete visible native graph'),('no-latest-coalescing','queue.Clear(); queue.Add(state);','queue.Add(state);','two same-frame complete updates publish only the latest coherent picture')]
        dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
        for name,before,after,failure in variants:
            case=run/name;case.mkdir();code=kernel
            if before:
                assert code.count(before)==1,(name,code.count(before));code=code.replace(before,after)
            (case/'ActualKernels.cs').write_text(code)
            for path in (ROOT/'scripts/results-transport-runtime').glob('*.cs'):shutil.copyfile(path,case/path.name)
            target=case/'Overlay.targets';target.write_text('<Project><ItemGroup>'+''.join('<Compile Include="'+str(x)+'" />' for x in case.glob('*.cs'))+'</ItemGroup></Project>')
            cmd=[dotnet,'build',str(ROOT/'tests/GloomhavenVR.WireTests/GloomhavenVR.WireTests.csproj'),'-c','Release','--nologo','-v','quiet','--disable-build-servers','-nr:false','-p:StartupObject=ResultsTransportProof',f'-p:CustomAfterMicrosoftCommonTargets={target}',f'-p:OutputPath={case}/bin/',f'-p:BaseIntermediateOutputPath={case}/obj/']
            with (case/'build.log').open('w') as log:child=subprocess.Popen(cmd,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);rc=child.wait(timeout=180)
            if rc:raise RuntimeError('Compilation is not accepted as a causal failure: '+name)
            with (case/'execution.log').open('w') as log:child=subprocess.Popen([dotnet,str(case/'bin/GloomhavenVR.WireTests.dll')],stdout=log,stderr=subprocess.STDOUT,start_new_session=True);rc=child.wait(timeout=30)
            text=(case/'execution.log').read_text();passed=rc==0 and 'PASS ' in text if name=='production' else rc!=0 and failure in text
            results.append({'case':name,'passed':passed,'exit_code':rc,'expected':failure});print(name+': '+text.strip(),flush=True)
            if not passed:raise RuntimeError('Unexpected result: '+name)
        status=0
    finally:
        if child and child.poll() is None:os.killpg(child.pid,signal.SIGKILL);child.wait()
        unchanged=all(hashlib.sha256((ROOT/p).read_bytes()).hexdigest()==h for p,h in pins.items());(run/'results.json').write_text(json.dumps({'passed':status==0 and unchanged,'source_hashes_unchanged':unchanged,'cases':results,'limits':'Exact production source kernels; native socket/reflection targets, authority census and diagnostics are explicit ports. No Photon or HMD claim.'},indent=2)+'\n')
        cleanup=cleanup_completed_run('results-transport-runtime',status,completion,ROOT,process_reaped=True)
        (run/'cleanup.json').write_text(json.dumps(cleanup,indent=2)+'\n')
    return status
if __name__=='__main__':raise SystemExit(main())
