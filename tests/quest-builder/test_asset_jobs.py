"""Actual bounded work, ordered parent publication and supervised cancellation."""
from dataclasses import dataclass
import hashlib
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/quest-builder'))
import asset_jobs
import storage


@dataclass
class Job:
    index: int
    memory_bytes: int = 1


class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.parent=threading.get_ident()

    def test_parallel_work_is_real_bounded_and_publication_stays_ordered_in_parent(self):
        active,peak=0,0;lock=threading.Lock();prepared=[];finished=[];published=[]
        def prepare(index):
            self.assertEqual(threading.get_ident(),self.parent);prepared.append(index);return Job(index)
        def execute(job,cancel):
            nonlocal active,peak
            self.assertNotEqual(threading.get_ident(),self.parent)
            with lock:active+=1;peak=max(peak,active)
            time.sleep(.03 if job.index%2==0 else .01)
            with lock:active-=1;finished.append(job.index)
            return job.index*7
        def publish(job,result):
            self.assertEqual(threading.get_ident(),self.parent);published.append((job.index,result))
        asset_jobs.ordered_pipeline(range(8),prepare,execute,publish,jobs=3,byte_budget=2)
        self.assertEqual(peak,2);self.assertEqual(prepared,list(range(8)))
        self.assertEqual(published,[(i,i*7) for i in range(8)])
        self.assertLess(finished.index(1),finished.index(0))

    def test_serial_and_parallel_results_are_equal(self):
        outputs=[]
        for workers in (1,4):
            result=[]
            asset_jobs.ordered_pipeline(range(10),Job,lambda job,cancel:hashlib.sha256(str(job.index).encode()).hexdigest(),
                lambda job,value:result.append((job.index,value)),jobs=workers)
            outputs.append(result)
        self.assertEqual(*outputs)

    def test_oversized_item_runs_alone_and_small_items_keep_parallel_work(self):
        active,peak=0,0;lock=threading.Lock();published=[];isolated=[]
        def execute(job,cancel):
            nonlocal active,peak
            with lock:
                active+=1;peak=max(active,peak)
                if job.memory_bytes>2:isolated.append(active)
            time.sleep(.01)
            with lock:active-=1
            return job.index
        with mock.patch.object(asset_jobs,'_pressure_event') as evidence:
            asset_jobs.ordered_pipeline(range(6),lambda index:Job(index,3 if index==2 else 1),
                execute,lambda job,value:published.append(value),jobs=4,byte_budget=2)
        self.assertEqual(peak,2);self.assertEqual(isolated,[1]);self.assertEqual(published,list(range(6)))
        evidence.assert_called_once_with('serial-oversized',estimated_bytes=3)
        # A zero detected budget still allows one disk-backed attempt rather
        # than turning an advisory estimate into an unconditional build stop.
        output=[]
        with mock.patch.object(asset_jobs,'_pressure_event'):
            asset_jobs.ordered_pipeline([4],Job,lambda job,cancel:job.index,
                lambda job,value:output.append(value),jobs=4,byte_budget=0)
        self.assertEqual(output,[4])

    def test_actual_allocation_failure_retries_alone_retaining_successful_siblings(self):
        active=0;lock=threading.Lock();attempts={};isolated=[];published=[]
        def execute(job,cancel):
            nonlocal active
            with lock:
                active+=1;attempts[job.index]=attempts.get(job.index,0)+1
                attempt=attempts[job.index]
                if job.index==0 and attempt>1:isolated.append(active)
            try:
                time.sleep(.02)
                if job.index==0 and attempt==1:raise asset_jobs.MemoryPressure('witnessed allocation failure')
                return job.index
            finally:
                with lock:active-=1
        with mock.patch.object(asset_jobs,'_pressure_event') as evidence:
            asset_jobs.ordered_pipeline(range(6),Job,execute,
                lambda job,value:published.append(value),jobs=3,byte_budget=8)
        self.assertEqual(isolated,[1]);self.assertEqual(published,list(range(6)))
        self.assertEqual(attempts,{0:2,1:1,2:1,3:1,4:1,5:1})
        self.assertEqual([call.args[0] for call in evidence.call_args_list],['retrying','recovered'])

    def test_repeated_allocation_pressure_waits_until_cancelled_without_publishing(self):
        token=asset_jobs.Cancellation();waiting=threading.Event();failures=[];attempts=[]
        def execute(job,cancel):attempts.append(job.index);raise asset_jobs.MemoryPressure('allocation failure')
        def observe(kind,**value):
            if kind=='waiting':waiting.set()
        def run():
            try:
                with mock.patch.object(asset_jobs,'_pressure_event',side_effect=observe):
                    asset_jobs.ordered_pipeline([0],Job,execute,
                        lambda *args:self.fail('failed output published'),jobs=3,cancellation=token)
            except BaseException as error:failures.append(error)
        thread=threading.Thread(target=run);thread.start()
        try:
            self.assertTrue(waiting.wait(3));token.event.set();thread.join(3)
            self.assertFalse(thread.is_alive());self.assertEqual(len(failures),1)
            self.assertIsInstance(failures[0],asset_jobs.Cancelled);self.assertEqual(attempts,[0,0])
        finally:token.event.set();thread.join(5)

    def test_parent_failure_cancels_and_joins_running_workers(self):
        stopped=threading.Event();token=asset_jobs.Cancellation()
        def execute(job,cancel):
            if job.index==1:
                cancel.event.wait(1);stopped.set();cancel.check()
            return job.index
        def publish(*args):raise RuntimeError('publisher failed')
        with self.assertRaisesRegex(RuntimeError,'publisher failed'):
            asset_jobs.ordered_pipeline([0,1],Job,execute,publish,jobs=2,cancellation=token)
        self.assertTrue(token.event.is_set())
        # Pending work may be cancelled before launch; no live worker survives.
        self.assertFalse(any(t.name.startswith('quest-asset-codec') for t in threading.enumerate()))

    def test_original_failure_survives_other_worker_cancellation(self):
        def execute(job,cancel):
            if job.index==0:cancel.event.wait(2);cancel.check()
            raise ValueError('second codec failed')
        with self.assertRaisesRegex(ValueError,'second codec failed'):
            asset_jobs.ordered_pipeline([0,1],Job,execute,lambda *args:self.fail('failed work published'),jobs=2)

    def test_success_preserves_external_cancellation_token(self):
        token=asset_jobs.Cancellation()
        asset_jobs.ordered_pipeline([0],Job,lambda job,cancel:job.index,lambda *args:None,jobs=1,cancellation=token)
        self.assertFalse(token.event.is_set())

    def test_invalid_counts_and_memory_estimates_are_rejected(self):
        for count in (0,-1,True,1025):
            with self.assertRaises(storage.BuildError):asset_jobs.ordered_pipeline([],Job,None,None,jobs=count)
        for memory in (-1,True,float('nan'),1.5):
            with self.assertRaisesRegex(storage.BuildError,'memory estimate'):
                asset_jobs.ordered_pipeline([0],lambda index:Job(index,memory),None,None,jobs=1)
        for budget in (-1,True,float('nan'),1.5):
            with self.assertRaisesRegex(storage.BuildError,'memory budget'):
                asset_jobs.ordered_pipeline([],Job,None,None,jobs=1,byte_budget=budget)

    def test_prepare_stop_iteration_is_failure_not_false_inventory_completion(self):
        def prepare(index):raise StopIteration('native object missing')
        with self.assertRaisesRegex(StopIteration,'native object missing'):
            asset_jobs.ordered_pipeline([0],prepare,None,None,jobs=1)


class CommandTests(unittest.TestCase):
    def test_pressure_progress_is_neutral_measured_and_never_claims_texture_completion(self):
        progress=mock.Mock();progress.enabled.return_value=True
        with mock.patch.object(asset_jobs,'build_progress',progress),mock.patch.dict(os.environ,{},clear=True), \
             mock.patch('sys.stdout',new_callable=__import__('io').StringIO):
            asset_jobs._pressure_event('retrying',attempt=1,estimated_bytes=9)
            asset_jobs._pressure_event('waiting',attempt=2,estimated_bytes=9,wait_seconds=2)
            asset_jobs._pressure_event('recovered',attempt=2,estimated_bytes=9)
        self.assertEqual([call.args[0] for call in progress.event.call_args_list],
                         ['asset-memory-retry','asset-memory-wait','asset-memory-retry'])
        self.assertEqual([call.kwargs['status'] for call in progress.event.call_args_list],
                         ['start','progress','complete'])
        self.assertTrue(all(len(call.args)==1 for call in progress.event.call_args_list))
        self.assertTrue(all(call.kwargs['nativeMemory']['compilerProfile']=='independent-codec'
                            for call in progress.event.call_args_list))

    def test_only_witnessed_allocation_failures_receive_retry_classification(self):
        for code,text in ((1,'Unhandled exception. System.OutOfMemoryException: Insufficient memory'),
                          (1,'LLVM ERROR: out of memory'),(1,'MemoryError'),
                          (0xc0000017,''),(-1073741801,''),(0xc000012d,'')):
            self.assertTrue(asset_jobs.allocation_failure(code,text),(code,text))
        for code,text in ((137,''),(-9,''),(1,'decoder format mismatch'),(1,'file not found'),
                          (1,'this fixture is not an allocation failure')):
            self.assertFalse(asset_jobs.allocation_failure(code,text),(code,text))
        with self.assertRaises(asset_jobs.MemoryPressure):
            asset_jobs.run_command([sys.executable],('-c',
                'import sys;sys.stderr.write("System.OutOfMemoryException\\n"+"stack frame\\n"*3000);sys.exit(1)'),
                asset_jobs.Cancellation())

    @unittest.skipIf(os.name=='nt','Actual constrained child allocation uses POSIX RLIMIT_AS')
    def test_actual_low_memory_child_failure_recovers_without_reexecuting_good_output(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);published=[]
            code=('import resource,sys;from pathlib import Path;'
                  'marker=Path(sys.argv[1]);first=not marker.exists();marker.write_text("attempt");'
                  'resource.setrlimit(resource.RLIMIT_AS,(32*1024*1024,32*1024*1024)) if first else None;'
                  'pixels=bytearray(64*1024*1024);Path(sys.argv[2]).write_bytes(b"accepted output")')
            def execute(job,cancel):
                asset_jobs.run_command([sys.executable],('-c',code,root/'attempt',root/'output'),cancel)
                return (root/'output').read_bytes()
            with mock.patch.object(asset_jobs,'_pressure_event') as evidence:
                asset_jobs.ordered_pipeline([0],Job,execute,
                    lambda job,result:published.append(result),jobs=2,byte_budget=1)
            self.assertEqual(published,[b'accepted output'])
            self.assertEqual([call.args[0] for call in evidence.call_args_list],['retrying','recovered'])

    def test_actual_command_has_literal_args_and_bounded_diagnostics(self):
        token=asset_jobs.Cancellation()
        result=asset_jobs.run_command([sys.executable],('-c','import sys; print(sys.argv[1]);print("x"*4000)','literal & 100% $value'),token)
        self.assertEqual(len(result),2000)
        with self.assertRaisesRegex(storage.BuildError,'fixture codec failure'):
            asset_jobs.run_command([sys.executable],('-c','import sys;sys.stderr.write("fixture codec failure");sys.exit(7)'),token)

    def test_actual_cancel_terminates_and_reaps_codec(self):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        marker=Path(temporary.name)/'started';token=asset_jobs.Cancellation();failures=[]
        code='import os,sys,time;from pathlib import Path;Path(sys.argv[1]).write_text(str(os.getpid()));time.sleep(30)'
        def run():
            try:asset_jobs.run_command([sys.executable],('-c',code,marker),token)
            except BaseException as error:failures.append(error)
        worker=threading.Thread(target=run);worker.start()
        try:
            deadline=time.monotonic()+3
            while not marker.exists() and time.monotonic()<deadline:time.sleep(.01)
            self.assertTrue(marker.exists());pid=int(marker.read_text());token.event.set();worker.join(3)
            self.assertFalse(worker.is_alive());self.assertEqual(len(failures),1);self.assertIsInstance(failures[0],asset_jobs.Cancelled)
            if os.name!='nt':
                with self.assertRaises(ProcessLookupError):os.kill(pid,0)
        finally:token.event.set();worker.join(5)


class CacheTests(unittest.TestCase):
    def setUp(self):
        temporary=tempfile.TemporaryDirectory();self.addCleanup(temporary.cleanup)
        self.root=Path(temporary.name);storage._invocation_file_proofs.clear();self.addCleanup(storage._invocation_file_proofs.clear)

    def test_only_parent_accepted_exact_codec_outputs_are_reused(self):
        raw=b'original blocks';pixels=b'actual decoded pixels';fingerprint='c'*64
        with asset_jobs.CodecCache(self.root,fingerprint) as cache:
            work=cache.prepare('original-guid',raw,('bc6h-2d',1,1,1),len(pixels));self.assertFalse(work.cached)
            work.path.write_bytes(pixels)
            self.assertFalse(cache.prepare('original-guid',raw,('bc6h-2d',1,1,1),len(pixels)).cached)
            cache.accept(work,hashlib.sha256(pixels).hexdigest(),cache.witnesses.current(work.path))
        storage._invocation_file_proofs.clear()
        with asset_jobs.CodecCache(self.root,fingerprint) as cache:
            warm=cache.prepare('original-guid',raw,('bc6h-2d',1,1,1),len(pixels));self.assertTrue(warm.cached)
            self.assertEqual(cache.witnesses.counters['bytes_read'],0)
            with mock.patch.object(asset_jobs,'run_command',side_effect=AssertionError('replayed accepted decoder')):
                self.assertEqual(cache.execute(warm,['unused'],asset_jobs.Cancellation()),warm.path)
            self.assertFalse(cache.prepare('original-guid',raw,('bc6h-2d',2,1,1),len(pixels)).cached)
            self.assertFalse(cache.prepare('original-guid',raw+b'x',('bc6h-2d',1,1,1),len(pixels)).cached)
        with asset_jobs.CodecCache(self.root,'d'*64) as cache:
            self.assertFalse(cache.prepare('original-guid',raw,('bc6h-2d',1,1,1),len(pixels)).cached)

    def test_corrupt_output_invalidates_cache_even_with_retained_size_and_mtime(self):
        with asset_jobs.CodecCache(self.root,'c'*64) as cache:
            work=cache.prepare('guid',b'blocks',('bc6h-2d',1,1,1),8);work.path.write_bytes(b'pixels00')
            cache.accept(work,hashlib.sha256(b'pixels00').hexdigest(),cache.witnesses.current(work.path))
            stamp=work.path.stat();work.path.write_bytes(b'badbytes');os.utime(work.path,ns=(stamp.st_atime_ns,stamp.st_mtime_ns))
            self.assertFalse(cache.prepare('guid',b'blocks',('bc6h-2d',1,1,1),8).cached)

    def test_unproven_codec_identity_never_reuses_previous_process_output(self):
        with asset_jobs.CodecCache(self.root,None) as cache:
            work=cache.prepare('guid',b'blocks',('bc6h-2d',1,1,1),8);work.path.write_bytes(b'pixels00')
            cache.accept(work,hashlib.sha256(b'pixels00').hexdigest(),cache.witnesses.current(work.path))
            self.assertFalse(cache.prepare('guid',b'blocks',('bc6h-2d',1,1,1),8).cached)

    def test_linked_cache_input_cannot_overwrite_original(self):
        original=self.root/'original';original.write_bytes(b'original')
        with asset_jobs.CodecCache(self.root,'c'*64) as cache:
            work=cache.prepare('guid',b'blocks',('bc6h-2d',1,1,1),8)
            input_path=Path(work.arguments[1]);input_path.unlink();os.link(original,input_path)
            with self.assertRaisesRegex(storage.BuildError,'regular owned'):
                cache.prepare('guid',b'blocks',('bc6h-2d',1,1,1),8)
        self.assertEqual(original.read_bytes(),b'original')


if __name__=='__main__':unittest.main()
