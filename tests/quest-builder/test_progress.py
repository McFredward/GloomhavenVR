"""Real file/tool work produces optional measured progress without changing receipts."""
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage
import dependencies

progress = storage.build_progress


def messages(stream):
    return [json.loads(line[len(progress.PREFIX):]) for line in stream.getvalue().splitlines() if line.startswith(progress.PREFIX)]


class ProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.stream = io.StringIO()
        self.environment = mock.patch.dict(os.environ, {progress.ENV: "1"})
        self.environment.start()

    def tearDown(self): self.environment.stop(); self.temp.cleanup()

    def test_non_wizard_cli_is_quiet_without_opt_in(self):
        file = self.root / "owned"; file.write_bytes(b"original")
        with mock.patch.dict(os.environ, {progress.ENV: "0"}), contextlib.redirect_stdout(self.stream):
            original = storage.inventory(self.root)
            progress.event("test", 1, 2, "files")
        self.assertEqual(self.stream.getvalue(), "")
        self.assertEqual(original[0]["sha256"], hashlib.sha256(b"original").hexdigest())

    def test_byte_hash_counts_match_actual_chunks_and_manifest_is_unchanged(self):
        raw = b"x" * (2 * 1048576 + 17)
        (self.root / "owned").write_bytes(raw)
        with contextlib.redirect_stdout(self.stream), mock.patch.object(progress.time, "monotonic", side_effect=range(100)):
            inventory = storage.inventory(self.root, phase="game-hash")
        events = messages(self.stream)
        self.assertEqual(inventory, [{"path": "owned", "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}])
        self.assertEqual(events[0]["done"], 0); self.assertEqual(events[0]["total"], len(raw))
        self.assertTrue(any(event["done"] == 1048576 for event in events))
        self.assertEqual(events[-1]["done"], len(raw)); self.assertEqual(events[-1]["status"], "complete")
        self.assertEqual((self.root / "owned").read_bytes(), raw)

    def test_counter_holds_last_count_until_verification(self):
        counter = progress.Counter("copy", 2, "files", stream=self.stream, interval=0)
        counter.add(1); counter.add(1)
        self.assertFalse(any(event["done"] == 2 for event in messages(self.stream)))
        counter.finish()
        self.assertEqual(messages(self.stream)[-1]["done"], 2)

    def test_unknown_work_does_not_create_a_percentage_or_success_counter(self):
        progress.event("import", detail="Importing original assets", stream=self.stream)
        counter = progress.Counter("unknown", stream=self.stream)
        counter.finish()
        self.assertTrue(all(event["done"] is None and event["total"] is None for event in messages(self.stream)))

    def test_malformed_and_overshoot_counts_rejected(self):
        for done, total in ((True, 1), (-1, 2), (3, 2), (float("nan"), 2), (1, float("inf")), (10 ** 200, 2)):
            with self.subTest(done=done, total=total), self.assertRaises(ValueError):
                progress.event("copy", done, total, stream=self.stream)
        counter = progress.Counter("copy", 2, stream=self.stream)
        with self.assertRaises(ValueError): counter.update(3)
        with self.assertRaises(ValueError): counter.add(True)
        with self.assertRaises(ValueError): counter.finish()

    def test_details_are_bounded_and_no_command_environment_is_serialized(self):
        progress.event("copy", 1, 2, "files", "\x00" + "x" * 2000, stream=self.stream)
        value = messages(self.stream)[0]
        self.assertEqual(value["detail"], "x" * 1024)
        self.assertEqual(set(value), {"schema", "phase", "done", "total", "unit", "detail", "status"})

    def test_incomplete_hash_never_emits_complete_event(self):
        path = self.root / "input"; path.write_bytes(b"original")
        with contextlib.redirect_stdout(self.stream), mock.patch.object(storage, "record_file", side_effect=KeyboardInterrupt):
            with self.assertRaises(KeyboardInterrupt): storage.inventory(self.root)
        self.assertFalse(any(event["status"] == "complete" for event in messages(self.stream)))
        self.assertEqual(messages(self.stream)[-1]["status"], "failed")

    def test_snapshot_resume_counts_only_verified_files_and_keeps_receipt_identical(self):
        source = self.root / "source"; source.mkdir(); (source / "a").write_bytes(b"one"); (source / "b").write_bytes(b"two")
        with contextlib.redirect_stdout(io.StringIO()): records = storage.inventory(source)
        destination = self.root / "snapshot"
        original = storage.shutil.copyfile; calls = []
        def interrupted(first, second):
            calls.append(first.name)
            if len(calls) == 2: raise KeyboardInterrupt
            return original(first, second)
        with contextlib.redirect_stdout(self.stream), mock.patch.object(storage.shutil, "copyfile", interrupted):
            with self.assertRaises(KeyboardInterrupt): storage.snapshot(source, records, destination, phase="game-snapshot")
        self.assertFalse(any(event["phase"] == "game-snapshot" and event["status"] == "complete" for event in messages(self.stream)))
        self.assertFalse((destination / ".snapshot.json").exists())
        with contextlib.redirect_stdout(self.stream), mock.patch.object(storage.shutil, "copyfile", wraps=original) as copying:
            storage.snapshot(source, records, destination, phase="game-snapshot")
        self.assertEqual(copying.call_count, 1)
        self.assertEqual(json.loads((destination / ".snapshot.json").read_text()), {"schema": 1, "files": records})
        done = [event for event in messages(self.stream) if event["phase"] == "game-snapshot"][-1]
        self.assertEqual((done["done"], done["total"], done["unit"]), (2, 2, "files"))

    def test_failed_stage_has_no_success_receipt_or_success_progress(self):
        stages = storage.Stages(self.root)
        def failure(): raise RuntimeError("fixture failure")
        with contextlib.redirect_stdout(self.stream), self.assertRaises(RuntimeError): stages.run("original-import", "key", failure)
        self.assertFalse(stages.path("original-import", "key").exists())
        self.assertFalse(any(event["status"] == "complete" for event in messages(self.stream)))
        self.assertEqual(messages(self.stream)[-1]["status"], "failed")

    def test_stage_success_and_reuse_keep_existing_record_order(self):
        stages = storage.Stages(self.root); first = self.root / "z"; second = self.root / "a"
        first.write_bytes(b"z"); second.write_bytes(b"a")
        with contextlib.redirect_stdout(self.stream):
            receipt = stages.run("assets", "key", lambda: ([first, second], {"exact": True}))
            reused = stages.run("assets", "key", mock.Mock(side_effect=AssertionError("must reuse")))
        self.assertEqual(receipt, reused)
        self.assertEqual([row["path"] for row in receipt["outputs"]], ["z", "a"])
        self.assertEqual(messages(self.stream)[-1]["status"], "reuse")

    def test_pinned_download_bytes_match_resume_offset_and_checksum(self):
        raw = b"official SDK" * 100; archive = self.root / "sdk.zip"; archive.with_suffix(".zip.download").write_bytes(raw[:3])
        response = io.BytesIO(raw[3:]); response.status = 206
        response.headers = {"Content-Range": "bytes 3-" + str(len(raw) - 1) + "/" + str(len(raw))}
        spec = {"url": "https://official.test/sdk.zip", "hash": hashlib.sha512(raw).hexdigest()}
        with contextlib.redirect_stdout(self.stream), mock.patch.object(dependencies.urllib.request, "urlopen", return_value=response):
            dependencies.download_sdk(spec, archive)
        self.assertEqual(archive.read_bytes(), raw)
        events = [event for event in messages(self.stream) if event["phase"] == "recovery-sdk-download"]
        self.assertEqual(events[-1]["done"], len(raw)); self.assertEqual(events[-1]["total"], len(raw))

    def test_failed_download_checksum_does_not_publish_download_completion(self):
        response = io.BytesIO(b"wrong"); response.status = 200; response.headers = {"Content-Length": "5"}
        archive = self.root / "sdk.zip"; spec = {"url": "https://official.test/sdk.zip", "hash": hashlib.sha512(b"right").hexdigest()}
        with contextlib.redirect_stdout(self.stream), mock.patch.object(dependencies.urllib.request, "urlopen", return_value=response):
            with self.assertRaises(storage.BuildError): dependencies.download_sdk(spec, archive)
        self.assertFalse(archive.exists())
        self.assertFalse(any(event["phase"] == "recovery-sdk-download" and event["status"] == "complete" for event in messages(self.stream)))

    def test_recovery_source_inventory_keeps_original_hash_encoding(self):
        sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
        spec = importlib.util.spec_from_file_location("quest_recovery_progress_fixture", ROOT / "tools/quest-recovery/recover.py")
        recovery = importlib.util.module_from_spec(spec); spec.loader.exec_module(recovery)
        (self.root / "a").write_bytes(b"a"); (self.root / "z").write_bytes(b"z")
        with contextlib.redirect_stdout(self.stream): rows, fingerprint = recovery.source_inventory(self.root)
        expected = [{"path": name, "bytes": 1, "sha256": hashlib.sha256(name.encode()).hexdigest()} for name in ("a", "z")]
        self.assertEqual(rows, expected)
        self.assertEqual(fingerprint, hashlib.sha256(json.dumps(expected, sort_keys=True, separators=(",", ":")).encode()).hexdigest())
        self.assertEqual(messages(self.stream)[-1]["done"], 2)

    def test_real_identity_batch_resume_counts_only_committed_batches(self):
        # Reuse the actual journal/identity fixture, not an imitation of its
        # algorithm. It interrupts after the second GUID merge, verifies the
        # previous checkpoint, then resumes the retained exporter output.
        fixture_path = ROOT / "tests/quest-recovery/test_restart.py"
        sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
        spec = importlib.util.spec_from_file_location("quest_progress_real_recovery_fixture", fixture_path)
        fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
        case = fixture.FullBatchRestartTests("test_two_real_identity_batches_reuse_completed_export_after_interrupted_merge")
        result = unittest.TestResult()
        with contextlib.redirect_stdout(self.stream): case.run(result)
        self.assertTrue(result.wasSuccessful(), result.errors + result.failures)
        batches = [event for event in messages(self.stream) if event["phase"] == "recovery-batches"]
        self.assertEqual(sum(event["status"] == "complete" for event in batches), 1)
        self.assertTrue(any(event["done"] == 1 and event["total"] == 2 for event in batches))
        self.assertEqual((batches[-1]["done"], batches[-1]["total"]), (2, 2))

    def test_minimal_copied_storage_stays_usable_without_progress_module(self):
        minimal = self.root / "minimal"; minimal.mkdir()
        (minimal / "storage.py").write_bytes(Path(storage.__file__).read_bytes())
        input_file = self.root / "input"; input_file.write_bytes(b"input")
        script = "import sys;from pathlib import Path;sys.path.insert(0,sys.argv[1]);import storage;print(storage.digest(Path(sys.argv[2])))"
        result = subprocess.check_output([sys.executable, "-I", "-B", "-c", script, str(minimal), str(input_file)], text=True)
        self.assertEqual(result.strip(), hashlib.sha256(b"input").hexdigest())


if __name__ == "__main__": unittest.main()
