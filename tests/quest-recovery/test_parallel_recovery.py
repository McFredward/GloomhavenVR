"""Closed-output concurrency, deterministic merge, sampling and cache footprint."""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import bundle_recovery as bundles
import full_recovery
import native_evidence
import recover
import recovery_workers as workers

GIB = 1024 ** 3


class ParallelRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.source = self.root / "GH_Data"; self.source.mkdir()
        (self.source / "original-core").write_bytes(b"original PC serialized data")
        (self.source / "Managed").mkdir()
        (self.source / "Managed/GH.Runtime.dll").write_bytes(b"metadata-only fixture; never executed")
        for name in ("globalgamemanagers", "level0", "resources.assets", "ScriptingAssemblies.json"):
            (self.source / name).write_bytes(b"owned serialization fixture")
        self.core = self.root / "core"; self.asset(self.core, "a" * 32, "native-core", 1)
        self.identity = self.root / "core-identities.jsonl"
        self.identity.write_text(json.dumps(self.row(self.core, "a" * 32, "native-core", 1)) + "\n")
        self.groups = []
        for index in range(3):
            path = self.source / (str(index) + ".bundle"); path.write_bytes(b"UnityFS\0" + bytes([index]))
            self.groups.append({"index": index, "bundles": [{"path": path.name, "bytes": path.stat().st_size,
                "sha256": recover.sha256(path)}]})
        self.plan = {"catalogSha256": "c" * 64, "groups": self.groups}
        self.attempts, self.active, self.peak, self.finishes, self.lock = {}, 0, 0, [], threading.Lock()
        self.before = recover.source_inventory(self.source)
        self.before_stamps = self.source_stamps()

    def tearDown(self): self.temp.cleanup()

    def source_stamps(self):
        return {path.relative_to(self.source).as_posix(): (value.st_dev, value.st_ino, value.st_size,
            value.st_mtime_ns, value.st_ctime_ns) for path in self.source.rglob("*") if path.is_file()
            for value in [path.stat()]}

    def row(self, project, guid, collection, identity):
        return {"guid": guid, "path": str(project / "Assets" / (collection + ".mat")),
            "objects": [{"collection": collection, "pathId": identity, "fileId": 2100000,
                "classId": 21, "className": "Material"}]}

    def asset(self, project, guid, collection, identity):
        row = self.row(project, guid, collection, identity); path = Path(row["path"])
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("%YAML 1.1\n--- !u!21 &2100000\nMaterial:\n  m_Name: " + collection + "\n")
        path.with_name(path.name + ".meta").write_text("guid: " + guid + "\n")
        return row

    def exporter(self, command, stage, target, log, evidence, settings, **kwargs):
        index = int(target.parent.name.split("-")[-1])
        cancellation = kwargs["cancellation"]; environment = kwargs["environment"]
        with self.lock:
            self.active += 1; self.peak = max(self.peak, self.active)
            self.attempts[index] = self.attempts.get(index, 0) + 1
        try:
            self.assertEqual(environment["QUEST_EXPORT_BUNDLE_ONLY"], "1")
            self.assertEqual(environment["DOTNET_PROCESSOR_COUNT"], "4")
            self.assertEqual(Path(environment["QUEST_EXPORT_IDENTITIES"]).parent, target.parent)
            self.assertEqual((stage / "original-core").read_bytes(), b"original PC serialized data")
            self.assertEqual([path.name for path in (stage / "SelectedBundles").iterdir()], [str(index) + ".bundle"])
            for _ in range(8 - index * 2):
                time.sleep(.012); cancellation.check()
            project = target / "ExportedProject"
            row = self.asset(project, str(index + 1) * 32, "original-bundle-" + str(index), index + 2)
            Path(environment["QUEST_EXPORT_IDENTITIES"]).write_text(json.dumps(row) + "\n")
            recipes = evidence / "QuestRecovery/NativeRecipes"; recipes.mkdir(parents=True)
            recipe = recipes / ("original-" + str(index) + ".yaml"); recipe.write_bytes(b"original native fields")
            (recipes / "index.jsonl").write_text(json.dumps({"collection": "original-bundle-" + str(index),
                "pathId": index + 2, "classId": 21, "yamlPath": recipe.name,
                "yamlSha256": recover.sha256(recipe)}) + "\n")
            log.write_text("AssetRipper exported actual fixture identities\n")
            with self.lock: self.finishes.append(index)
            return project, [], []
        finally:
            with self.lock: self.active -= 1

    def convert(self, name, jobs, export=None, merge=None, audit=None):
        policy = {"jobs": jobs, "memoryBudgetBytes": jobs * 3 * GIB, "workerProcessorCount": 4}
        with patch.object(workers, "policy", return_value=policy), \
             patch.object(bundles, "repair_managed_plugins"), \
             patch.object(bundles, "run_export", side_effect=export or self.exporter), \
             patch.object(bundles, "audit_asset_references", side_effect=audit or AssertionError("Default optional audit must be omitted")):
            if merge:
                with patch.object(bundles, "merge_export", side_effect=merge):
                    return bundles.run_recovery(self.source, self.core, self.identity, self.root / name,
                        self.root / (name + "-batches"), [], bundle_plan=self.plan)
            return bundles.run_recovery(self.source, self.core, self.identity, self.root / name,
                self.root / (name + "-batches"), [], bundle_plan=self.plan)

    def test_isolated_parallel_exports_match_serial_bytes_and_prune_committed_duplicates(self):
        environment = dict(os.environ)
        start = time.monotonic(); serial = self.convert("serial", 1); serial_time = time.monotonic() - start
        self.assertEqual(self.peak, 1)
        self.peak = 0; self.finishes = []
        start = time.monotonic(); parallel = self.convert("parallel", 3); parallel_time = time.monotonic() - start
        self.assertEqual(self.peak, 3); self.assertEqual(self.finishes[0], 2)
        self.assertLess(parallel_time, serial_time * .85)
        self.assertEqual(serial["files"], parallel["files"])
        self.assertEqual(serial["identities"], parallel["identities"])
        self.assertEqual(parallel["completedGroups"], [0, 1, 2])
        self.assertEqual(parallel["assetReferences"]["status"], "skipped")
        for index in range(3):
            batch = self.root / "parallel-batches" / ("batch-" + str(index).zfill(3))
            self.assertFalse((batch / "Export").exists()); self.assertFalse((batch / "Input").exists())
            self.assertEqual(list((batch / "Evidence/QuestRecovery/NativeRecipes").iterdir()),
                [batch / "Evidence/QuestRecovery/NativeRecipes/index.jsonl"])
            self.assertTrue((self.root / "parallel/QuestRecovery/NativeRecipes" / ("original-" + str(index) + ".yaml")).is_file())
        self.assertEqual(recover.source_inventory(self.source), self.before)
        self.assertEqual(self.source_stamps(), self.before_stamps)
        self.assertEqual(dict(os.environ), environment)

    def test_interrupted_ordered_merge_retains_closed_siblings_without_second_export(self):
        original = bundles.merge_export
        def stop(output, project, rows, canonical):
            result = original(output, project, rows, canonical)
            if "original-bundle-1" in rows[0]["path"]: raise KeyboardInterrupt()
            return result
        with self.assertRaises(KeyboardInterrupt): self.convert("resume", 3, merge=stop)
        saved = json.loads((self.root / "resume/quest-full-recovery-progress.json").read_text())
        self.assertEqual(saved["completedGroups"], [0]); self.assertEqual(self.attempts, {0: 1, 1: 1, 2: 1})
        result = self.convert("resume", 3, export=lambda *args, **kwargs: self.fail("Closed export restarted"))
        self.assertEqual(result["completedGroups"], [0, 1, 2]); self.assertEqual(self.active, 0)

    def test_allocation_failure_retries_only_failed_batch_and_retains_successful_siblings(self):
        jobs, _ = workers.modules(); failures = [False]
        def pressure(*args, **kwargs):
            if args[2].parent.name == "batch-000" and not failures[0]:
                failures[0] = True; time.sleep(.015)
                raise jobs.MemoryPressure("Actual isolated exporter allocation diagnostic")
            return self.exporter(*args, **kwargs)
        result = self.convert("pressure", 3, export=pressure)
        self.assertEqual(result["completedGroups"], [0, 1, 2]); self.assertEqual(self.attempts, {0: 1, 1: 1, 2: 1})
        self.assertEqual(self.active, 0); self.assertTrue(failures[0])

    def test_input_links_share_only_private_serialized_copy_and_reject_links(self):
        shared = self.root / "private-input"; shared.mkdir(); value = shared / "serialized"; value.write_bytes(b"private owned copy")
        stage = self.root / "worker-input"; workers.isolate_input(shared, stage)
        self.assertEqual(value.stat().st_ino, (stage / value.name).stat().st_ino)
        self.assertNotEqual(value.stat().st_ino, (self.source / "original-core").stat().st_ino)
        (shared / "unsafe").symlink_to(self.source / "original-core")
        with self.assertRaises(recover.RecoveryError): workers.isolate_input(shared, self.root / "rejected-input")

    def test_file_proofs_forget_pruned_batch_but_retain_canonical_files(self):
        proofs = native_evidence.FileProofs()
        current = self.root / "canonical"; current.write_bytes(b"current")
        temporary = self.root / "batch/item"; temporary.parent.mkdir(); temporary.write_bytes(b"temporary")
        proofs.digest(current); proofs.digest(temporary)
        proofs.forget_tree(temporary.parent)
        self.assertEqual(set(proofs._files), {current})

    def test_developer_full_audit_remains_opt_in_and_reports_actual_evaluated_counts(self):
        with patch.dict(os.environ, {"GHVRQ_FULL_ASSET_AUDIT": "1"}):
            result = self.convert("audited", 1, audit=recover.audit_asset_references)
        self.assertNotIn("status", result["assetReferences"])
        self.assertEqual(result["assetReferences"]["missingGuidCount"], 0)
        self.assertEqual(result["assetReferences"]["duplicateGuidCount"], 0)

    def test_export_resume_sample_is_bounded_and_never_hashes_unconsumed_files(self):
        directory = self.root / "batch"; directory.mkdir()
        project = directory / "Export/ExportedProject"; rows = []
        for index in range(70):
            rows.append(self.asset(project, format(index + 1, "032x"), "asset-" + str(index), index + 1))
        (directory / "identities.jsonl").write_text("".join(json.dumps(row) + "\n" for row in rows))
        (directory / "export.log").write_text("closed export")
        ignored = project / "ignored-debug-output.bin"; ignored.write_bytes(b"unconsumed")
        with patch.object(bundles, "sha256", wraps=recover.sha256) as hashes:
            records = bundles.export_receipt_samples(directory)
        self.assertEqual(len(records), 130)
        self.assertFalse(any(call.args[0] == ignored for call in hashes.call_args_list))
        samples = [row for row in records if row["path"].endswith(".mat")]
        self.assertEqual(len(samples), 64)
        bad = directory / samples[0]["path"]; bad.write_bytes(b"changed retained sampled input")
        with self.assertRaisesRegex(recover.RecoveryError, "checkpoint file changed"):
            bundles.verified_records(directory, records)

    def prepare_full(self, mutate_original=False):
        def core_export(command, stage, target, log, evidence, settings, **kwargs):
            project = target / "ExportedProject"
            row = self.asset(project, "a" * 32, "native-core", 1)
            Path(kwargs["environment"]["QUEST_EXPORT_IDENTITIES"]).write_text(json.dumps(row) + "\n")
            data = evidence / "QuestRecovery"; data.mkdir(parents=True, exist_ok=True)
            (data / "original-script-identities.json").write_text("[]")
            log.write_text("Core export completed")
            return project, [], []
        def metadata(source, destination, cache, dotnet=None):
            destination.write_text('{"originalTypes":[]}')
        def export(*args, **kwargs):
            result = self.exporter(*args, **kwargs)
            if mutate_original and args[2].parent.name == "batch-000":
                path = self.source / "original-core"
                path.write_bytes(b"x" * path.stat().st_size)
            return result
        policy = {"jobs": 3, "memoryBudgetBytes": 9 * GIB, "workerProcessorCount": 4}
        with patch.object(full_recovery, "build_tool", return_value=([], {"source": {"fixture": "pinned"}})), \
             patch.object(full_recovery, "catalog_bundle_plan", return_value=self.plan), \
             patch.object(full_recovery, "run_export", side_effect=core_export), \
             patch.object(full_recovery, "repair_managed_plugins"), \
             patch.object(full_recovery, "managed_inventory", side_effect=metadata), \
             patch.object(full_recovery, "serialized_members", return_value=[]), \
             patch.object(bundles, "repair_managed_plugins"), \
             patch.object(bundles, "run_export", side_effect=export), \
             patch.object(workers, "policy", return_value=policy):
            return full_recovery.prepare(self.source, self.root / "full", self.root / "tools", "dotnet")

    def test_full_prepare_source_change_guard_accepts_private_hardlink_creation_and_deletion(self):
        result = self.prepare_full()
        self.assertTrue(result["fullOriginalCatalogRecovered"])
        self.assertEqual(self.source_stamps(), self.before_stamps)
        self.assertFalse((self.root / "full/CoreInput").exists())
        self.assertFalse((self.root / "full/CoreEvidence").exists())
        checkpoint = json.loads((Path(result["recoveryProject"]) / "quest-full-recovery-progress.json").read_text())
        self.assertFalse(checkpoint["finalSourceCheck"]["fullContentRecheck"])
        self.assertLessEqual(checkpoint["finalSourceCheck"]["sampledBytes"], 128 * 1048576)

    def test_full_prepare_source_change_guard_rejects_actual_same_size_original_byte_change(self):
        with self.assertRaisesRegex(recover.RecoveryError, "Original game files changed"):
            self.prepare_full(mutate_original=True)


class RecoveryBudgetTests(unittest.TestCase):
    def test_real_available_memory_selects_workers_without_machine_specific_two_job_cap(self):
        _, resources = workers.modules()
        host = {"effectiveCpus": 32, "platform": "win32", "totalMemoryBytes": 64 * GIB,
            "availableMemoryBytes": 48 * GIB, "commitHeadroomBytes": 80 * GIB}
        generous = resources.choose_jobs(host, "asset-recovery")
        constrained = resources.choose_jobs({**host, "availableMemoryBytes": 5 * GIB}, "asset-recovery")
        self.assertGreater(generous["jobs"], 2); self.assertEqual(constrained["jobs"], 1)
        self.assertTrue(constrained["nativeLaunchAllowed"])

    def test_checkpoint_sampling_has_total_byte_limit_besides_file_limit(self):
        records = [{"path": "Assets/large-" + str(index), "bytes": 8 * 1048576,
            "sha256": format(index, "064x")} for index in range(100)]
        records.append({"path": bundles.MUTABLE_NATIVE_INDICES[0], "bytes": 40 * 1048576, "sha256": "a" * 64})
        samples = bundles.resume_samples(records)
        optional = [row for row in samples if row["path"] not in bundles.MUTABLE_NATIVE_INDICES]
        self.assertLessEqual(sum(row["bytes"] for row in optional), 32 * 1048576)
        self.assertIn(records[-1], samples)


class ExportProcessTests(unittest.TestCase):
    """Exercise real child processes and HTTP, without a game/exporter SDK."""
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.stage = self.root / "Input/GH_Data"; self.stage.mkdir(parents=True)
        self.server = self.root / "fixture-exporter.py"
        self.server.write_text('''import argparse,json,os,time
from pathlib import Path
from urllib.parse import parse_qs
from http.server import BaseHTTPRequestHandler,HTTPServer
p=argparse.ArgumentParser();p.add_argument('--headless',action='store_true');p.add_argument('--port',type=int);p.add_argument('--log-path');a=p.parse_args()
class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args): pass
 def do_GET(self): self.send_response(200);self.end_headers();self.wfile.write(b'')
 def do_POST(self):
  value=parse_qs(self.rfile.read(int(self.headers.get('Content-Length',0))).decode())
  if self.path=='/Export/UnityProject':
   Path(a.log_path).write_text('pid='+str(os.getpid())+'\\n')
   if os.environ.get('FIXTURE_ALLOC')=='1': print('System.OutOfMemoryException',flush=True);os._exit(10)
   time.sleep(float(os.environ.get('FIXTURE_DELAY','.01')))
   project=Path(value['Path'][0])/'ExportedProject';(project/'Assets').mkdir(parents=True)
   asset=project/'Assets/original.mat';asset.write_text('%YAML 1.1\\nMaterial: {}\\n')
   asset.with_name(asset.name+'.meta').write_text('guid: '+'a'*32+'\\n')
   Path(os.environ['QUEST_EXPORT_IDENTITIES']).write_text(json.dumps({'original':str(asset),'processorCount':os.environ.get('DOTNET_PROCESSOR_COUNT')})+'\\n')
  self.send_response(200);self.end_headers();self.wfile.write(b'')
HTTPServer(('127.0.0.1',a.port),Handler).serve_forever()
''')

    def tearDown(self): self.temp.cleanup()

    def run_export(self, extra=None, cancellation=None):
        environment = dict(os.environ); environment.update(extra or {})
        environment.update(QUEST_EXPORT_IDENTITIES=str(self.root / "identities.jsonl"), DOTNET_PROCESSOR_COUNT="3")
        return recover.run_export([sys.executable, "-I", "-B", str(self.server)], self.stage,
            self.root / "Export", self.root / "export.log", self.root / "Evidence", {},
            require_scene_settings=False, environment=environment, cancellation=cancellation)

    def test_process_receives_explicit_environment_without_changing_parent(self):
        before = dict(os.environ)
        jobs, _ = workers.modules()
        result = self.run_export(cancellation=jobs.Cancellation())
        self.assertTrue((result[0] / "Assets/original.mat").is_file())
        self.assertEqual(json.loads((self.root / "identities.jsonl").read_text())["processorCount"], "3")
        self.assertEqual(dict(os.environ), before)

    def test_real_allocation_diagnostic_becomes_recoverable_pressure(self):
        jobs, _ = workers.modules()
        with self.assertRaises(jobs.MemoryPressure):
            self.run_export({"FIXTURE_ALLOC": "1"}, jobs.Cancellation())

    def test_cancel_during_blocking_http_stops_and_reaps_exporter(self):
        jobs, _ = workers.modules(); cancellation = jobs.Cancellation()
        def cancel():
            deadline = time.monotonic() + 5
            while not (self.root / "export.log").is_file() and time.monotonic() < deadline: time.sleep(.01)
            cancellation.event.set()
        watcher = threading.Thread(target=cancel); watcher.start()
        start = time.monotonic()
        try:
            with self.assertRaises(jobs.Cancelled): self.run_export({"FIXTURE_DELAY": "10"}, cancellation)
        finally: watcher.join()
        self.assertLess(time.monotonic() - start, 3)
        pid = int((self.root / "export.log").read_text().strip().split("=")[1])
        if os.name != "nt":
            with self.assertRaises(ProcessLookupError): os.kill(pid, 0)


if __name__ == "__main__": unittest.main()
