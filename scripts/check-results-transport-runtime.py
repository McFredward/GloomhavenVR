#!/usr/bin/env python3
"""Execute exact Results ingress, packet emission and retry kernels with declared native ports."""
import argparse, hashlib, json, os
from pathlib import Path
import re, shutil, signal, subprocess, tempfile
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
def member(text, signature):
    start=text.index(signature); at=text.index('{',start); depth=0
    for end in range(at,len(text)):
        depth+=(text[end]=='{')-(text[end]=='}')
        if depth==0:return text[start:end+1]
    raise ValueError(signature)
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/results-transport-runtime');p.add_argument('--native-samples',nargs=2,type=Path,metavar=('ENABLED','DISABLED'));args=p.parse_args()
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()));completion=run/'completion.log';completion.write_text('evidence: '+str(run)+'\n');print('evidence: '+str(run),flush=True)
    source=ROOT/'src/GloomhavenVR/Net';driver=(source/'Avatar/NetAvatarDriver.Results.cs').read_text();transport=(source/'FfsNetTransport.cs').read_text()
    kernel='using System;using System.Collections.Generic;using GloomhavenVR.Core;namespace GloomhavenVR.Net;\n'
    kernel+='internal sealed partial class ResultsIngress {\n'
    for name in ['_pendingResults','_resultsRevisions','_resultsBases','_resultsLatestData','_resultsQueued']:
        kernel+=next(line for line in driver.splitlines() if 'private readonly Dictionary' in line and ' '+name+' ' in line)+'\n'
    for name in ['_resultsPeers','_resultsPresentation','_resultsSendProgress','_resultsReceiveProgress','_sentResults','_resultsPublishingAuthor','_resultsRevision','_nextResultsCapture']:
        kernel+=next(line for line in driver.splitlines() if ' '+name+' ' in line or ' '+name+',' in line or ' '+name+';' in line)+'\n'
    for signature in ['private void TickResultsSend(', 'private static ScenarioResultsState CopyResultsPose(', 'private static bool SameResults(', 'private bool QueueResults(', 'private void QueueResultsReady(']:
        kernel+=member(driver,signature)+(';' if 'CopyResultsPose' in signature else '')+'\n'
    kernel+='}\n'
    kernel+='internal sealed partial class FfsNetTransport {\n'
    kernel+=transport[transport.index('    private readonly ExtrasFragments _fragments'):transport.index('    private readonly ExtrasSendScheduler _extrasQueue')]+'\n'
    for signature in ['internal void TickFragments(', 'private void SendToken(', 'internal bool TrySendResults(', 'internal bool SendSharedWindowScroll(', 'private bool SendReliableTownPacket(', 'private void RaiseReceived(']:
        kernel+=member(transport,signature)+'\n'
    kernel+='}\n'
    # Only the original native clock and diagnostic sink are substituted. The
    # actual admission, reflection call, cloned bytes and finally flags execute.
    kernel=kernel.replace('VRLog.Error(', 'ResultsDiagnostics.Error(').replace('VRLog.Warn(', 'ResultsDiagnostics.Warn(').replace('UnityEngine.Time.unscaledTime', 'ResultsClock.Now')
    progress=(source/'ScenarioResultsProgress.cs').read_text().replace('VRLog.Wants(VRLogLevel.Info)','ResultsDiagnostics.WantsProgress').replace('VRLog.Note(', 'ResultsDiagnostics.Note(')
    paths=[source/'Avatar/NetAvatarDriver.Results.cs',source/'FfsNetTransport.cs',Path(__file__),*sorted((ROOT/'scripts/results-transport-runtime').glob('*.cs'))]
    project=ROOT/'tests/GloomhavenVR.WireTests/GloomhavenVR.WireTests.csproj';paths.append(project)
    for name in ['ScenarioResultsState.cs','ScenarioResultsDelta.cs','ScenarioResultsProgress.cs','ExtrasSendQueue.cs','ExtrasFragments.cs','PresentationBatch.cs','SharedWindowScrollCodec.cs'] :paths.append(source/name)
    pins={str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths};(run/'source-pins.json').write_text(json.dumps(pins,indent=2)+'\n');(run/'ActualKernels.cs').write_text(kernel)
    samples=[]
    if args.native_samples:
        for i,path in enumerate(args.native_samples):
            data=path.read_bytes();copy=run/('native-sample-'+str(i)+'.bin');copy.write_bytes(data)
            samples.append({'original':str(path.resolve()),'sha256':hashlib.sha256(data).hexdigest(),'copy':str(copy)})
        (run/'native-sample-pins.json').write_text(json.dumps(samples,indent=2)+'\n')
    status=1;child=None;results=[]
    try:
        variants=[('production','','',''),('lossy-results','            && !PresentationBatch.HasResultsPage(bytes, bytes.Length)','', 'Results emission requests native reliable ordered delivery'),('lost-failed-page','_failedResultsPage = page;', '_failedResultsPage = null;', 'native rejection retains the exact pending Results page'),('missing-delta-expand','complete = ScenarioResultsDelta.Expand(basis, latest);','complete = latest;','a sparse picture waits without replacing the complete visible native graph'),('no-latest-coalescing','queue.Clear(); queue.Add(state);','queue.Add(state);','two same-frame complete updates publish only the latest coherent picture')]
        variants += [
            ('lossy-shared-scroll','_sendArgs[2] = false;','_sendArgs[2] = true;','shared scroll uses native reliable ordered delivery'),
            ('lost-scroll-author-route','_sendArgs[3] = hostOnly;','_sendArgs[3] = false;','shared scroll requests route to the native host when requested'),
            ('unvalidated-shared-scroll','SharedWindowScrollCodec.TryRead(payload, length, out _)','length > 0','malformed scroll input never reaches the native socket'),
            ('borrowed-scroll-buffer','var bytes = new byte[length];\n            Buffer.BlockCopy(payload, 0, bytes, 0, length);','var bytes = payload;','shared scroll emission owns immutable bytes after caller reuse'),
            ('delta-as-full-basis','if (publisher && wire.BaseRevision == 0) _resultsSourceBasis','if (publisher) _resultsSourceBasis','both clients converge to latest Stats sample after coalescing and same-snapshot page reordering'),
        ]
        if samples:
            variants=[('native-starvation-before','','','native sampled Stats must eventually replace the completed heading'),('native-starvation-production','','','')]
            variants += [('native-'+name,'','','new key or author immediately supersedes the old in-flight full') for name in ['lost-new-key-priority','lost-new-author-priority']]
            variants += [('native-lost-close-priority','','','native close immediately supersedes the old in-flight full'),('native-unbounded-full-waiters','','','newer full originals coalesce to one bounded waiter')]
        dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
        for name,before,after,failure in variants:
            case=run/name;case.mkdir();code=kernel
            if before:
                assert code.count(before)==1,(name,code.count(before));code=code.replace(before,after)
            (case/'ActualKernels.cs').write_text(code)
            (case/'ActualProgress.cs').write_text(progress)
            for path in (ROOT/'scripts/results-transport-runtime').glob('*.cs'):shutil.copyfile(path,case/path.name)
            # Preserve the actual compiled project inside its owned run. Absolute
            # linked inputs keep the original wire project semantics and let the
            # canonical completion API identify only this case's bin/obj caches.
            project_text=re.sub(r'<Compile Include="([^"$][^"]*)"',lambda match:'<Compile Include="'+str(project.parent/match[1])+'"',project.read_text())
            if samples:
                queue=(source/'ExtrasSendQueue.cs').read_text()
                if name=='native-starvation-before':
                    before='|| older.Key != state.Key || (state.Revision > older.Revision\n                && (!older.HasPresentation || older.BaseRevision != 0 || !older.Visible || !state.Visible))))'
                    after='|| older.Key != state.Key || state.Revision > older.Revision))'
                    assert queue.count(before)==1;queue=queue.replace(before,after)
                queue_mutations={'native-lost-new-key-priority':('|| older.Key != state.Key','|| false'), 'native-lost-new-author-priority':('older.Author != state.Author','false'), 'native-lost-close-priority':(' || !state.Visible))))','))))')}
                if name in queue_mutations:
                    before,after=queue_mutations[name];assert queue.count(before)==1;queue=queue.replace(before,after)
                if name=='native-unbounded-full-waiters':
                    body=member(queue,'internal void EnqueueResults(');assert body.count('_pending.Clear();')==1
                    queue=queue.replace(body,body.replace('_pending.Clear();',''))
                (case/'ActualExtrasSendQueue.cs').write_text(queue)
                project_text=re.sub(r'\s*<Compile Include="\$\(RepoRoot\)src[/\\]GloomhavenVR[/\\]Net[/\\]ExtrasSendQueue.cs"[^>]*/>','',project_text)
                assert 'Net/ExtrasSendQueue.cs' not in project_text and 'Net\\ExtrasSendQueue.cs' not in project_text
            local_project=case/'WireRuntime.csproj';local_project.write_text(project_text)
            target=case/'Overlay.targets';target.write_text('<Project><ItemGroup>'+''.join('<Compile Include="'+str(x)+'" />' for x in case.glob('*.cs'))+'</ItemGroup></Project>')
            cmd=[dotnet,'build',str(local_project),'-c','Release','--nologo','-v','quiet','--disable-build-servers','-nr:false','-p:StartupObject=ResultsTransportProof',f'-p:CustomAfterMicrosoftCommonTargets={target}',f'-p:OutputPath={case}/bin/',f'-p:BaseIntermediateOutputPath={case}/obj/']
            with (case/'build.log').open('w') as log:child=subprocess.Popen(cmd,stdout=log,stderr=subprocess.STDOUT,start_new_session=True);rc=child.wait(timeout=180)
            if rc:raise RuntimeError('Compilation is not accepted as a causal failure: '+name)
            environment=os.environ.copy()
            if samples:
                environment['GVR_RESULTS_NATIVE_ENABLED']=samples[0]['copy'];environment['GVR_RESULTS_NATIVE_DISABLED']=samples[1]['copy']
            with (case/'execution.log').open('w') as log:child=subprocess.Popen([dotnet,str(case/'bin/GloomhavenVR.WireTests.dll')],stdout=log,stderr=subprocess.STDOUT,start_new_session=True,env=environment);rc=child.wait(timeout=30)
            text=(case/'execution.log').read_text();passed=rc!=0 and failure in text if failure else rc==0 and 'PASS ' in text
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
