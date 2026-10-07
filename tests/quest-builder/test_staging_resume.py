"""Real interrupted filesystem writers resume without resetting prior stages."""
import hashlib
import io
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import contextlib

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import full_assets
from staging_resume import Journal, _dispatcher
from storage import BuildError


def sha(raw): return hashlib.sha256(raw).hexdigest()


class ResumeTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.output = self.root / "stage"
        self.original = self.root / "original"; self.original.mkdir()
        self.identity = {"source": str(self.original), "game": str(self.root / "game"), "recipe": "fixture-v1"}
        self.events = io.StringIO()

    def journal(self, **kwargs):
        return Journal(self.output, self.identity, full_assets._StageProofs(), **kwargs)

    def baseline(self):
        with self.journal() as journal:
            def copy():
                for name, raw in (("asset", b"original"), ("asset.meta", b"native GUID"), ("nested/child", b"directory original")):
                    target = self.output / name
                    target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(raw)
                    journal.proofs.digest(target); journal.record(target)
                return {"complete": True}
            journal.run("copy", 0, copy, copy=True)

    def test_partial_copy_keeps_accepted_bytes_and_only_opens_missing_source(self):
        first, second = self.original / "first", self.original / "second"
        first.write_bytes(b"first completed bytes"); second.write_bytes(b"second bytes")
        rows = [{"path": path.name, "bytes": path.stat().st_size, "sha256": sha(path.read_bytes())} for path in (first, second)]
        with self.journal() as journal:
            def interrupted():
                full_assets._resume_copy(first, self.output / "first", rows[0], journal.proofs, journal)
                raise RuntimeError("interrupted after one accepted copy")
            with self.assertRaisesRegex(RuntimeError, "interrupted"):
                journal.run("copy", 0, interrupted, copy=True)
        stamp = (self.output / "first").stat()
        opening = Path.open; reads = []
        def observe(path, mode="r", *args, **kwargs):
            if mode == "rb": reads.append(path)
            return opening(path, mode, *args, **kwargs)
        with mock.patch.object(Path, "open", observe), self.journal() as journal:
            def resume():
                for source, row in zip((first,second),rows):
                    full_assets._resume_copy(source,self.output/row["path"],row,journal.proofs,journal)
                return {"complete": True}
            journal.run("copy", 0, resume, copy=True)
        self.assertNotIn(first, reads)
        self.assertEqual(reads.count(self.output / "first"), 1)
        self.assertEqual(reads.count(second), 1)
        self.assertEqual((self.output / "first").stat().st_ino, stamp.st_ino)
        self.assertEqual((self.output / "first").read_bytes(), first.read_bytes())

    def test_uncommitted_complete_copy_is_qualified_and_partial_copy_is_repaired(self):
        original = self.original / "asset"; original.write_bytes(b"complete owned data")
        row = {"path": "asset", "bytes": original.stat().st_size, "sha256": sha(original.read_bytes())}
        with self.journal() as journal:
            (self.output / "asset").write_bytes(b"partial")
            full_assets._resume_copy(original,self.output/"asset",row,journal.proofs,journal)
        self.assertEqual((self.output / "asset").read_bytes(), original.read_bytes())
        # A fresh physical copy not present in SQLite survived a killed batch.
        self.output.joinpath("uncommitted").write_bytes(original.read_bytes())
        row["path"] = "uncommitted"
        with self.journal() as journal, mock.patch.object(full_assets,"_copy_recovered",side_effect=AssertionError("unnecessary copy")):
            full_assets._resume_copy(original,self.output/"uncommitted",row,journal.proofs,journal)

    def test_checkpoint_bytes_takes_precedence_and_invalid_size_is_rejected(self):
        original = self.original / "asset"; original.write_bytes(b"bytes receipt")
        row = {"path":"asset", "bytes":len(original.read_bytes())+1,"size":len(original.read_bytes()),"sha256":sha(original.read_bytes())}
        with self.journal() as journal, self.assertRaisesRegex(BuildError,"size differs"):
            full_assets._resume_copy(original,self.output/"asset",row,journal.proofs,journal)
        self.assertFalse((self.output/"asset").exists())

    def test_actual_writes_moves_deletes_and_directory_moves_roll_back(self):
        self.baseline()
        with self.journal() as journal:
            def fail():
                (self.output/"asset").write_bytes(b"partial native output")
                (self.output/"asset.meta").rename(self.output/"changed.meta")
                (self.output/"nested").rename(self.output/"moved-folder")
                (self.output/"new-directory").mkdir()
                (self.output/"new-directory/new-file").write_bytes(b"new incomplete data")
                (self.output/"changed.meta").unlink()
                raise RuntimeError("actual native mutation failure")
            with self.assertRaises(RuntimeError): journal.run("native",1,fail)
        self.assertEqual((self.output/"asset").read_bytes(),b"original")
        self.assertEqual((self.output/"asset.meta").read_bytes(),b"native GUID")
        self.assertEqual((self.output/"nested/child").read_bytes(),b"directory original")
        self.assertFalse((self.output/"changed.meta").exists())
        self.assertFalse((self.output/"moved-folder").exists())
        self.assertFalse((self.output/"new-directory").exists())
        self.assertIsNone(_dispatcher().owner)

    def test_killed_phase_rolls_back_on_next_invocation_without_original_changes(self):
        self.baseline()
        outside = self.original / "game-data"; outside.write_bytes(b"read only original")
        script = """
import os,sys,json
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from staging_resume import Journal
from full_assets import _StageProofs
output=Path(sys.argv[2]); identity=json.loads(sys.argv[3])
with Journal(output,identity,_StageProofs()) as journal:
    def killed():
        (output/'asset').write_bytes(b'killed partial native output')
        (output/'asset.meta').rename(output/'renamed.meta')
        (output/'nested/child').unlink()
        (output/'brand-new').write_bytes(b'unfinished')
        os._exit(17)
    journal.run('guid',1,killed)
"""
        result=subprocess.run([sys.executable,"-c",script,str(ROOT/"tools/quest-builder"),str(self.output),json.dumps(self.identity)],capture_output=True,text=True)
        self.assertEqual(result.returncode,17,result.stderr)
        self.assertEqual((self.output/"asset").read_bytes(),b"killed partial native output")
        with self.journal() as journal:
            self.assertEqual((self.output/"asset").read_bytes(),b"original")
            self.assertEqual((self.output/"asset.meta").read_bytes(),b"native GUID")
            self.assertEqual((self.output/"nested/child").read_bytes(),b"directory original")
            self.assertFalse((self.output/"brand-new").exists())
            self.assertFalse((self.output/"renamed.meta").exists())
            self.assertEqual(journal.db.execute("SELECT COUNT(*) FROM phases").fetchone()[0],1)
        self.assertEqual(outside.read_bytes(),b"read only original")

    def test_runtime_task_accept_keeps_finished_task_after_later_failure(self):
        self.baseline()
        with self.journal() as journal:
            def runtime():
                (self.output/"asset").write_bytes(b"completed runtime override")
                journal.accept()
                (self.output/"asset.meta").write_bytes(b"unfinished override")
                raise RuntimeError("next runtime task failed")
            with self.assertRaises(RuntimeError): journal.run("runtime",1,runtime)
        with self.journal():
            self.assertEqual((self.output/"asset").read_bytes(),b"completed runtime override")
            self.assertEqual((self.output/"asset.meta").read_bytes(),b"native GUID")

    def test_native_auxiliary_is_owned_before_mutation_and_only_it_is_removed(self):
        self.baseline()
        workspace=self.output.with_name(self.output.name+"-native-restoration")
        unrelated=self.root/"unrelated"; unrelated.mkdir(); (unrelated/"keep").write_bytes(b"preserved")
        with self.journal() as journal:
            def native():
                workspace.mkdir(); (workspace/"scratch").write_bytes(b"owned only")
                raise RuntimeError("native preparation failed")
            with self.assertRaises(RuntimeError): journal.run("native",1,native,auxiliary=workspace)
        self.assertFalse(workspace.exists())
        self.assertEqual((unrelated/"keep").read_bytes(),b"preserved")
        workspace.mkdir(); (workspace/"unknown").write_bytes(b"foreign")
        with self.journal() as journal, self.assertRaisesRegex(BuildError,"fresh journal owner"):
            journal.run("native",1,lambda:None,auxiliary=workspace)
        self.assertEqual((workspace/"unknown").read_bytes(),b"foreign")

    def test_hook_is_single_scoped_dispatcher_and_protects_originals(self):
        state=_dispatcher()
        original=self.original/"input"; original.write_bytes(b"original")
        for _ in range(3):
            with self.journal() as journal:
                with self.assertRaisesRegex(BuildError,"original input"):
                    journal.run("runtime",0,lambda: original.write_bytes(b"wrong"))
            self.assertIs(_dispatcher(),state)
            self.assertIsNone(getattr(state,"owner",None))
        self.assertEqual(original.read_bytes(),b"original")

    def test_external_writer_and_symlink_fail_before_mutation(self):
        with self.journal() as journal:
            with self.assertRaisesRegex(BuildError,"explicit declared transaction"):
                journal.run("native",0,lambda:subprocess.run([sys.executable,"-c","pass"]))
            with self.assertRaisesRegex(BuildError,"linked output"):
                journal.run("native",0,lambda:os.symlink(self.original,self.output/"bad-link"))
        self.assertFalse((self.output/"bad-link").exists())

    def test_corrupt_or_unknown_receipts_preserve_owned_files(self):
        self.baseline()
        (self.output/"asset").write_bytes(b"unexpected external mutation")
        with self.assertRaisesRegex(BuildError,"Retained staged bytes differ"):
            self.journal()
        self.assertEqual((self.output/"asset").read_bytes(),b"unexpected external mutation")
        owner=self.output.with_name(self.output.name+".staging-resume")/"owner.json"
        owner.write_text('{"unknown":true}')
        with self.assertRaisesRegex(BuildError,"inputs/owner differ"): self.journal()
        self.assertEqual((self.output/"asset").read_bytes(),b"unexpected external mutation")

    def test_previous_copy_can_only_be_adopted_with_exact_parent_owner(self):
        self.output.mkdir(); (self.output/"partial").write_bytes(b"retained")
        owner={"schema":1,"owner":"Quest recovered Campaign stage","key":"exact","gameKey":"owned game"}
        (self.output.parent/"stage-owner.json").write_text(json.dumps(owner))
        with self.assertRaisesRegex(BuildError,"exact parent owner"): self.journal()
        with self.journal(resume_owner=owner): pass
        self.assertEqual((self.output/"partial").read_bytes(),b"retained")

    def test_previous_transformed_stage_is_never_adopted_as_raw_copy(self):
        path=self.output/"QuestRecovery/canonical-guid-restoration.json"
        path.parent.mkdir(parents=True); path.write_text('{}')
        owner={"owner":"fixture exact"}
        (self.output.parent/"stage-owner.json").write_text(json.dumps(owner))
        with self.assertRaisesRegex(BuildError,"transformed staging files"):
            self.journal(resume_owner=owner)
        self.assertEqual(path.read_text(),'{}')

    def test_phase_context_hash_corruption_is_explicit_without_restarting(self):
        self.baseline()
        with self.journal() as journal:
            with journal.db: journal.db.execute("UPDATE phases SET payload='{}'")
        with self.journal() as journal, self.assertRaisesRegex(BuildError,"phase receipt is corrupt"):
            journal.run("copy",0,lambda:self.fail("completed phase restarted"),copy=True)
        self.assertEqual((self.output/"asset").read_bytes(),b"original")


if __name__ == "__main__": unittest.main()
