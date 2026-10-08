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

    def test_oversized_work_fails_before_worker_or_publication(self):
        with self.assertRaisesRegex(storage.BuildError,'memory budget'):
            asset_jobs.ordered_pipeline([1],lambda index:Job(index,3),lambda *args:self.fail('oversized worker'),
                lambda *args:self.fail('oversized publication'),jobs=4,byte_budget=2)

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

    def test_prepare_stop_iteration_is_failure_not_false_inventory_completion(self):
        def prepare(index):raise StopIteration('native object missing')
        with self.assertRaisesRegex(StopIteration,'native object missing'):
            asset_jobs.ordered_pipeline([0],prepare,None,None,jobs=1)


class CommandTests(unittest.TestCase):
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
