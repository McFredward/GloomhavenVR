"""Real ZIP publication, live child sidecar, and optional observer containment."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import types
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import native_content_pack as packer

PHASES = ("native-content-source-hash", "native-content-native-hash", "native-content-write", "native-content-final-hash")


class ContentProgress(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.output = Path(self.temp.name) / "output"
        self.output.mkdir()
        (self.output / ".quest-builder-output.json").write_text(json.dumps(
            {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"}))
        self.project = self.output / "projects" / ("a" * 64); self.project.mkdir(parents=True)
        self.native = self.project / "native"; self.native.mkdir()
        self.bundle = self.native / "bank.bundle"; self.bundle.write_bytes(b"UnityFS fixture" * 99)
        self.archive = self.project / "Assets/StreamingAssets/quest-startup-content.zip"
        self.archive.parent.mkdir(parents=True)
        self.manifest = self.project / packer.MANIFEST; self.manifest.parent.mkdir(parents=True)
        self.original = b"owned original video bytes" * 81
        with zipfile.ZipFile(self.archive, "w", compression=zipfile.ZIP_DEFLATED) as stream:
            stream.writestr("StreamingAssets/movie.mp4", self.original)
        self.manifest.write_text(json.dumps({"schema": 1, "inputKey": "b" * 64,
            "archive": self.archive.name, "archiveSha256": packer.digest(self.archive),
            "files": [{"path": "StreamingAssets/movie.mp4", "size": len(self.original),
                       "sha256": hashlib.sha256(self.original).hexdigest()}]}))
        self.apk = self.output / "builds" / ("c" * 64) / "GloomhavenVR-Quest.apk"
        self.log = self.output / "logs" / ("content-pack-" + "c" * 12 + ".log")
        self.env = {"GHVRQ_WIZARD_PROGRESS": "1", "GHVR_QUEST_CONTENT_PROGRESS_LOG": str(self.log),
                    "GHVR_QUEST_OUTPUT_APK": str(self.apk)}
        self.request = {"schema": 1, "projectRoot": str(self.project), "nativeRoot": str(self.native)}

    def tearDown(self): self.temp.cleanup()

    def events(self):
        return [json.loads(line.removeprefix("GHVRQ_PROGRESS ")) for line in self.log.read_text().splitlines()]

    def test_four_real_passes_count_original_reads_native_reads_writes_and_publication(self):
        old_size = self.archive.stat().st_size; native_size = self.bundle.stat().st_size
        with patch.dict(os.environ, self.env): result = packer.pack(self.request)
        rows = self.events(); starts = [row for row in rows if row["status"] == "start"]
        complete = [row for row in rows if row["status"] == "complete"]
        self.assertEqual([row["phase"] for row in starts], list(PHASES))
        self.assertEqual([row["phase"] for row in complete], list(PHASES))
        self.assertEqual([row["total"] for row in complete], [old_size, native_size,
                         len(self.original) + native_size, self.archive.stat().st_size])
        for row in rows:
            self.assertEqual(row["operation"], "content-bank"); self.assertEqual(row["unit"], "bytes")
            self.assertLessEqual(row["done"], row["total"])
        self.assertTrue(all(row["done"] == row["total"] for row in complete))
        self.assertEqual(result["archiveSha256"], packer.digest(self.archive))

    def test_reuse_counts_entry_validation_and_does_not_repeat_the_final_archive_hash(self):
        packer.pack(self.request); before = self.archive.read_bytes(), self.manifest.read_bytes()
        expected = sum(row["size"] for row in json.loads(self.manifest.read_text())["files"])
        with patch.dict(os.environ, self.env), patch.object(packer, "digest", wraps=packer.digest) as digest:
            result = packer.pack(self.request)
        self.assertTrue(result["reused"]); self.assertEqual(before, (self.archive.read_bytes(), self.manifest.read_bytes()))
        completed = {row["phase"]: row for row in self.events() if row["status"] == "complete"}
        self.assertEqual(completed["native-content-write"]["total"], expected)
        self.assertEqual(completed["native-content-final-hash"]["total"], 0)
        self.assertEqual(sum(call.args[0] == self.archive for call in digest.call_args_list), 1)

    def test_failed_copy_never_completes_the_active_write_or_final_hash(self):
        before = self.archive.read_bytes(), self.manifest.read_bytes()
        with patch.dict(os.environ, self.env), patch.object(packer, "write_entry", side_effect=OSError("fixture write interrupted")):
            with self.assertRaises(OSError): packer.pack(self.request)
        rows = self.events(); self.assertEqual(rows[-1]["status"], "failed")
        self.assertEqual(rows[-1]["phase"], "native-content-write")
        self.assertFalse(any(row["status"] == "complete" and row["phase"] in PHASES[2:] for row in rows))
        self.assertEqual(before, (self.archive.read_bytes(), self.manifest.read_bytes()))

    def test_source_hash_failure_never_completes_source_or_later_phases(self):
        value = json.loads(self.manifest.read_text()); value["archiveSha256"] = "0" * 64
        self.manifest.write_text(json.dumps(value))
        with patch.dict(os.environ, self.env), self.assertRaisesRegex(ValueError, "archive changed"):
            packer.pack(self.request)
        self.assertTrue(self.events()); self.assertFalse(any(row["status"] == "complete" for row in self.events()))

    def test_disabled_and_unowned_log_paths_create_no_sidecar_or_external_write(self):
        original = self.archive.read_bytes(), self.manifest.read_bytes()
        target = self.output.parent / "external.log"; target.write_text("keep")
        for changes in ({"GHVRQ_WIZARD_PROGRESS": "0"},
                        {"GHVR_QUEST_CONTENT_PROGRESS_LOG": str(target)},
                        {"GHVR_QUEST_CONTENT_PROGRESS_LOG": str(self.output / "logs/wrong.log")},
                        {"GHVR_QUEST_OUTPUT_APK": str(self.output.parent / "outside.apk")}):
            with self.subTest(changes=changes):
                self.archive.write_bytes(original[0]); self.manifest.write_bytes(original[1])
                with patch.dict(os.environ, {**self.env, **changes}): packer.pack(self.request)
                self.assertFalse(self.log.exists()); self.assertEqual(target.read_text(), "keep")
        marker = self.output / ".quest-builder-output.json"; marker.unlink()
        with patch.dict(os.environ, self.env): packer.pack(self.request)
        self.assertFalse(self.log.exists())

    @unittest.skipIf(os.name == "nt", "Local symlink permissions differ")
    def test_symlink_and_hardlink_sidecar_cannot_truncate_other_files(self):
        self.log.parent.mkdir(); target = self.output.parent / "external.log"; target.write_text("keep")
        for link in (lambda: self.log.symlink_to(target), lambda: os.link(target, self.log)):
            link()
            with patch.dict(os.environ, self.env): packer.pack(self.request)
            self.assertEqual(target.read_text(), "keep"); self.log.unlink()

    def test_observer_write_failure_keeps_content_successful(self):
        with patch.dict(os.environ, self.env), patch.object(packer.os, "open", side_effect=OSError("fixture logging unavailable")):
            result = packer.pack(self.request)
        self.assertTrue(result["allEntryBytesVerified"]); self.assertFalse(self.log.exists())

    def test_live_observer_io_failure_isolated_after_the_log_is_opened(self):
        actual = packer.os.fdopen
        class BrokenLog:
            def __init__(self, stream): self.stream = stream
            def tell(self): return self.stream.tell()
            def write(self, line): raise OSError("fixture volume became unavailable")
            def close(self): self.stream.close()
        def broken(handle, *args, **kwargs): return BrokenLog(actual(handle, *args, **kwargs))
        with patch.dict(os.environ, self.env), patch.object(packer.os, "fdopen", side_effect=broken):
            result = packer.pack(self.request)
        self.assertTrue(result["allEntryBytesVerified"])
        self.assertEqual(result["archiveSha256"], packer.digest(self.archive))

    def test_bounded_sidecar_retains_the_latest_phase_without_changing_content(self):
        with patch.dict(os.environ, self.env), patch.object(packer, "PROGRESS_LOG_BYTES", 700):
            result = packer.pack(self.request)
        self.assertLessEqual(self.log.stat().st_size, 700)
        self.assertEqual(self.events()[-1]["phase"], "native-content-final-hash")
        self.assertEqual(self.events()[-1]["status"], "complete")
        self.assertTrue(result["allEntryBytesVerified"])

    def test_observed_archive_and_manifest_bytes_equal_previous_released_algorithm(self):
        previous = subprocess.run(["git", "show", "8063c8fc8:tools/quest-builder/native_content_pack.py"],
                                  cwd=ROOT, check=True, capture_output=True, text=True).stdout
        module = types.ModuleType("previous_native_content_pack"); exec(compile(previous, "released-packer", "exec"), module.__dict__)
        old = self.archive.read_bytes(), self.manifest.read_bytes()
        module.pack(self.request); expected = self.archive.read_bytes(), self.manifest.read_bytes()
        self.archive.write_bytes(old[0]); self.manifest.write_bytes(old[1])
        with patch.dict(os.environ, self.env): packer.pack(self.request)
        self.assertEqual(expected, (self.archive.read_bytes(), self.manifest.read_bytes()))

    def test_actual_child_sidecar_advances_before_buffered_stdout_is_consumed(self):
        # Real ZIP I/O remains the work; delay its reads so the parent can prove
        # it sees counts while C#-style stdout buffering has produced no receipt.
        self.bundle.write_bytes(os.urandom(24 * 1048576))
        request = self.project / "request.json"; request.write_text(json.dumps(self.request))
        child = r'''
import json,sys,time
from pathlib import Path
sys.path.insert(0,sys.argv[1]);import native_content_pack as p
real=p.stream_hash
class Slow:
 def __init__(self,s):self.s=s
 def read(self,n):
  b=self.s.read(n)
  if b:time.sleep(.04)
  return b
def slow(s,progress=None):return real(Slow(s),progress)
p.stream_hash=slow
raise SystemExit(p.main(['--request',sys.argv[2]]))
'''
        env = {**os.environ, **self.env}
        process = subprocess.Popen([sys.executable, "-I", "-B", "-c", child,
                                   str(ROOT / "tools/quest-builder"), str(request)],
                                  env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        observed = []
        try:
            deadline = time.monotonic() + 15
            while time.monotonic() < deadline and process.poll() is None:
                if self.log.is_file():
                    for line in self.log.read_text().splitlines():
                        try: row = json.loads(line.removeprefix("GHVRQ_PROGRESS "))
                        except ValueError: continue
                        if row["phase"] == "native-content-native-hash" and 0 < row["done"] < row["total"]:
                            observed.append(row)
                if observed: break
                time.sleep(.03)
            self.assertTrue(observed); self.assertIsNone(process.poll())
            stdout, stderr = process.communicate(timeout=15)
            self.assertEqual(process.returncode, 0, stderr)
            self.assertEqual(len(stdout.splitlines()), 1)
            self.assertEqual(json.loads(stdout)["archiveSha256"], packer.digest(self.archive))
        finally:
            if process.poll() is None: process.kill(); process.communicate()


if __name__ == "__main__": unittest.main()
