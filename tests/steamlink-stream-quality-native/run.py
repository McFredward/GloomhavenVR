#!/usr/bin/env python3
"""Focused public OpenVR function-table lease controls, not a headset result."""
import argparse,hashlib,importlib.util,json,os,pathlib,shutil,signal,subprocess,sys,tempfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
HERE=pathlib.Path(__file__).resolve().parent

def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--output',type=pathlib.Path,required=True);parser.add_argument('--sanitize',action='store_true');parser.add_argument('--rebuild-native',action='store_true');args=parser.parse_args()
    args.output.mkdir(parents=True,exist_ok=True);out=pathlib.Path(tempfile.mkdtemp(prefix='run-',dir=args.output.resolve()));cache=out/'owned-cache';cache.mkdir();records=[];code=1;log=out/'run.log'
    env=dict(os.environ,PYTHONDONTWRITEBYTECODE='1');env.update(ASAN_OPTIONS='detect_leaks=1:abort_on_error=1',UBSAN_OPTIONS='halt_on_error=1:print_stacktrace=1')
    def run(name,cmd,timeout=90,expected=0):
        target=out/(name+'.log')
        with target.open('wb') as sink:
            child=subprocess.Popen(cmd,cwd=ROOT,env=env,stdout=sink,stderr=subprocess.STDOUT,start_new_session=True)
            try:result=child.wait(timeout=timeout)
            except BaseException:
                if child.poll() is None:os.killpg(child.pid,signal.SIGKILL)
                child.wait();raise
        records.append({'name':name,'exit_code':result,'expected_exit_code':expected,'log':target.name})
        if result!=expected:raise RuntimeError(name+' failed; see '+str(target))
    try:
        flags=['-fsanitize=address,undefined','-fno-omit-frame-pointer','-g'] if args.sanitize else ['-O2']
        binary=cache/'fixture';run('native-build',[os.environ.get('CC','cc'),'-std=c11','-Wall','-Wextra','-Werror',*flags,str(HERE/'NativeFixture.c'),'-o',str(binary)])
        run('native-lease',[str(binary)],20)
        for name in ['transcript','transcript-error','transcript-conflict','transcript-absent','transcript-snapshot-error']:
            run(name,[str(binary),'--'+name],10)
            lines=[json.loads(x) for x in (out/(name+'.log')).read_text().splitlines()]
            assert all(x['schema']=='ghvr-steamlink-quality-v1' for x in lines)
            assert len(lines)==(1 if name in ('transcript-error','transcript-snapshot-error') else 2)
            assert set(lines[0])==({'schema','event','error','restored','conflicts','errors'} if name in ('transcript-error','transcript-snapshot-error') else {'schema','event','applied','requested_width','applied_width','previous_foveation_mode','previous_sharpening','previous_automatic_width','previous_uniform_width','previous_absent_mask'})
            if len(lines)==2:assert set(lines[1])=={'schema','event','ok','conflicts','errors'}
        # Inspect the real shipping resource/pins, not a separate fixture binary.
        asset=ROOT/'src/GloomhavenVR/Assets/SteamLink/stream-quality.exe';provenance=json.loads(asset.with_name('stream-quality-provenance.json').read_text())
        assert provenance['source_sha256']==hashlib.sha256((ROOT/'tools/steamlink-stream-quality.c').read_bytes()).hexdigest()
        assert provenance['script_sha256']==hashlib.sha256((ROOT/'scripts/build-steamlink-stream-quality.sh').read_bytes()).hexdigest()
        assert provenance['output_bytes']==asset.stat().st_size and provenance['output_sha256']==hashlib.sha256(asset.read_bytes()).hexdigest()
        if args.rebuild_native:
            rebuilt=cache/'stream-quality.exe';run('rebuild-pe',['bash',str(ROOT/'scripts/build-steamlink-stream-quality.sh'),str(rebuilt)])
            assert rebuilt.read_bytes()==asset.read_bytes(),'shipping PE resource rebuild differs'
        source=(ROOT/'tools/steamlink-stream-quality.c').read_text()
        assert 'CreateMutexW(NULL,FALSE,L"Local\\\\GloomhavenVR.SteamLink.StreamQuality.v1")' in source
        assert 'WaitForSingleObject(mutex,0)' in source and 'lock!=WAIT_OBJECT_0' in source and 'lock==WAIT_ABANDONED' in source
        assert 'VRApplication_Scene' not in source and 'Sleep(' not in source and 'TerminateProcess' not in source
        # Plant blind restoration into the actual source, then require the same
        # public-table controls to reject it at a preserved manual-edit check.
        protected='if(current!=lease->applied[key]){out->conflicts++;continue;}'
        assert source.count(protected)==1
        mutant=out/'restore-mutant.c';mutant.write_text(source.replace(protected,'if(false){out->conflicts++;continue;}'))
        fixture=out/'mutant-fixture.c';fixture.write_text((HERE/'NativeFixture.c').read_text().replace('#include "../../tools/steamlink-stream-quality.c"','#include "'+str(mutant)+'"'))
        mutated=cache/'mutant';run('mutant-build',[os.environ.get('CC','cc'),'-std=c11','-O2','-Wall','-Wextra','-Werror',str(fixture),'-o',str(mutated)])
        run('blind-restore-rejected',[str(mutated)],20,expected=1)
        assert 'quality check' in (out/'blind-restore-rejected.log').read_text()
        # Reintroduce the exact Build700 missing-setting refusal in a frozen
        # source, proving the added fixture actually detects this hardware class.
        handled='if(status==5){lease.absent[key]=true;lease.before[key]=0;}'
        assert source.count(handled)==1
        absent_mutant=out/'absent-mutant.c';absent_mutant.write_text(source.replace(handled,'if(status==5){out.error="snapshot";goto done;}'))
        absent_fixture=out/'absent-mutant-fixture.c';absent_fixture.write_text((HERE/'NativeFixture.c').read_text().replace('#include "../../tools/steamlink-stream-quality.c"','#include "'+str(absent_mutant)+'"'))
        absent_binary=cache/'absent-mutant';run('absent-mutant-build',[os.environ.get('CC','cc'),'-std=c11','-O2','-Wall','-Wextra','-Werror',str(absent_fixture),'-o',str(absent_binary)])
        run('absent-setting-regression-rejected',[str(absent_binary)],20,expected=1)
        assert 'quality check' in (out/'absent-setting-regression-rejected.log').read_text()
        code=0
    except BaseException as exc:records.append({'failure':str(exc)});print(str(exc),file=sys.stderr)
    finally:
        (out/'result.json').write_text(json.dumps({'schema':'ghvr-steamlink-quality-native-tests-v1','exit_code':code,'runs':records,'windows_or_proton_execution':False,'mutex_kernel_execution':False,'hardware_verified':False},indent=2)+'\n');log.write_text('All focused children reaped; no actual SteamVR calls.\n')
        spec=importlib.util.spec_from_file_location('retention',ROOT/'scripts/town_run_retention.py');retention=importlib.util.module_from_spec(spec);sys.modules[spec.name]=retention;spec.loader.exec_module(retention)
        receipt=retention.cleanup_completed_run('steamlink-stream-quality-native',code,log,ROOT,process_reaped=True)
        receipt['exact_private_fallback']=str(cache);shutil.rmtree(cache);(out/'cleanup.json').write_text(json.dumps(receipt,indent=2)+'\n')
    print('evidence: '+str(out));return code
if __name__=='__main__':raise SystemExit(main())
