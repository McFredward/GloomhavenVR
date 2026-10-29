"""Actual pre-Editor import observations and isolated live child-log discovery."""
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import LogTail, ProgressParser, Supervisor
from state import Store, stage_progress
import wizard


def boundary(operation, status="start"):
    return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "operation:" + operation,
        "operation": operation, "status": status, "done": None, "total": None})


# Verbatim shape from the pinned Unity2021.3 cold/warm import logs. The action
# name is misleading: the recorded artifact and elapsed seconds prove return.
IMPORTED = ("Start importing Assets/Quest/Editor/QuestBuild.cs using Guid(abded73fad6bdd68ca887cdf036eca5a) "
            "Importer(-1,00000000000000000000000000000000)  -> "
            "(artifact id: '407e642b75cfddd074ce52710bf1ce32') in 0.001092 seconds ")
IMPORT_LOG = "package-import-88ecccea2cf9.log"


class ImportParserTests(unittest.TestCase):
    def test_artifact_and_duration_count_actual_completed_imports(self):
        parser = ProgressParser()
        first = parser.parse(IMPORTED, IMPORT_LOG)
        self.assertEqual(first["phase"], "unity-asset-import")
        self.assertEqual(first["operation"], "unity-import")
        self.assertEqual((first["done"], first["total"], first["unit"]), (1, None, "assets"))
        self.assertIn("QuestBuild.cs", first["detail"])
        self.assertIn("0.001092 s", first["detail"])
        self.assertIsNone(stage_progress(**{key: first[key] for key in ("phase", "done", "total", "unit", "detail")})["percent"])
        second = parser.parse(IMPORTED, IMPORT_LOG)
        self.assertEqual(second["done"], 2)  # A real reimport is another completed call.

    def test_bare_start_and_refresh_are_activity_not_completion(self):
        parser = ProgressParser()
        started = parser.parse("Start importing Assets/Game/Character.asset", IMPORT_LOG)
        self.assertEqual((started["phase"], started["done"], started["total"]), ("unity-import-activity", 0, None))
        parser.parse(IMPORTED, IMPORT_LOG)
        refreshed = parser.parse("Asset Pipeline Refresh: Total: 17.787 seconds - Initiated by InitialRefreshV2(ForceSynchronousImport)", IMPORT_LOG)
        self.assertEqual((refreshed["phase"], refreshed["done"], refreshed["total"]), ("unity-import-activity", 1, None))
        self.assertNotEqual(refreshed.get("status"), "complete")

    def test_import_counter_source_isolation_and_supplied_totals_preserved(self):
        parser = ProgressParser()
        parser.parse(IMPORTED, IMPORT_LOG)
        other = parser.parse(IMPORTED, "mod-bundle-current.log")
        self.assertEqual((other["done"], other["operation"]), (1, "mod-banks"))
        counted = parser.parse("Imported 4 assets of 8", IMPORT_LOG)
        self.assertEqual((counted["done"], counted["total"], counted["operation"]), (4, 8, "unity-import"))
        self.assertIsNone(parser.parse("Imported 9 assets of 8", IMPORT_LOG))

    def test_bee_package_children_and_final_player_keep_actual_owners(self):
        parser = ProgressParser()
        action = "[340/344    0s] Csc Library/Bee/artifacts/1300b0aE.dag/Assembly-CSharp-Editor.dll (+2 others)"
        self.assertEqual(parser.parse(action, IMPORT_LOG)["operation"], "unity-import")
        self.assertEqual(parser.parse(action, "package-api-abc.log")["operation"], "package-api")
        self.assertEqual(parser.parse("[3/344 0s] WriteText Library/Bee/A.rsp", IMPORT_LOG)["done"], 3)
        parser.parse(boundary("unity-validation"), "build.log")
        self.assertEqual(parser.parse(action, "unity-build-abc.log")["operation"], "unity-validation")
        self.assertEqual(parser.parse(IMPORTED, "unity-build-abc.log")["operation"], "unity-validation")
        self.assertEqual(parser.parse("Asset Pipeline Refresh: Total: 1.2 seconds", "unity-build-abc.log")["operation"], "unity-validation")
        parser.parse(boundary("player"), "unity-build-abc.log")
        self.assertEqual(parser.parse(action, "unity-build-abc.log")["operation"], "player")

    def test_actual_android_actions_and_busy_preserve_only_measured_completed_work(self):
        parser = ProgressParser()
        parser.parse(boundary("player"), "build.log")
        for action in ("C_Android_arm64", "Link_Android_arm64", "UnityLinker", "WriteResponseFile",
                       "MovedFromExtractor", "MakeLump", "ActionGenerateProjectFiles", "NdkObjCopy"):
            real = parser.parse("[69/7818 0s] " + action + " Library/Bee/current", "unity-build-abc.log")
            self.assertEqual((real["done"], real["total"], real["operation"]), (69, 7818, "player"))
        busy = parser.parse("[BUSY      33s] C_Android_arm64 Library/Bee/GH.Runtime7.o", "unity-build-abc.log")
        self.assertEqual((busy["done"], busy["total"], busy["phase"]), (69, 7818, real["phase"]))
        self.assertIn("33 s", busy["detail"])
        orphan = parser.parse("[BUSY      14s] UnityLinker Library/Bee/ManagedStripped", IMPORT_LOG)
        self.assertEqual((orphan["phase"], orphan["done"], orphan["total"]), ("unity-bee-activity", None, None))
        self.assertIsNone(parser.parse("[BUSY 8s] ArbitraryTask file", IMPORT_LOG))

    def test_content_pack_sidecar_keeps_real_byte_counts_and_closed_owner_guard(self):
        parser = ProgressParser()
        line = "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "content-pack-write", "done": 50,
            "total": 100, "unit": "bytes", "operation": "content-bank"})
        real = parser.parse(line, "content-pack-abc.log")
        self.assertEqual((real["done"], real["total"], real["operation"]), (50, 100, "content-bank"))
        parser.parse(boundary("content-bank", "complete"), "build.log")
        self.assertIsNone(parser.parse(line, "content-pack-abc.log"))

    def test_closed_owner_late_logs_cannot_reopen_and_new_runner_boundary_can(self):
        parser = ProgressParser()
        parser.parse(boundary("unity-import"), "build.log")
        parser.parse(IMPORTED, IMPORT_LOG)
        parser.parse(boundary("unity-import", "complete"), "build.log")
        parser.parse(boundary("package-api"), "build.log")
        for late in (IMPORTED, "[1/2 0s] Csc old.dll", "[BUSY 8s] Csc old.dll", boundary("unity-import"),
                     "Asset Pipeline Refresh: Total: 3.0 seconds", "GHVRQ_PROGRESS " + json.dumps({
                         "schema": 1, "phase": "unity-progress", "done": 1, "total": 2, "operation": "unity-import"})):
            with self.subTest(line=late): self.assertIsNone(parser.parse(late, IMPORT_LOG))
        self.assertEqual(parser.active_operation, "package-api")
        parser.parse(boundary("unity-import"), "build.log")
        self.assertEqual(parser.parse(IMPORTED, IMPORT_LOG)["operation"], "unity-import")

    def test_incomplete_or_malformed_duration_never_counts_as_completion(self):
        parser = ProgressParser()
        for broken in (IMPORTED.replace("0.001092", "NaN"), IMPORTED.replace("0.001092", "1.2.3"),
                       IMPORTED.split(" -> ", 1)[0]):
            observed = parser.parse(broken, IMPORT_LOG)
            self.assertEqual(observed["done"], 0)
            self.assertEqual(observed["phase"], "unity-import-activity")

    def test_interleaved_importer_warning_requires_its_observed_completion_suffix(self):
        parser = ProgressParser()
        suffix = " -> (artifact id: 'c4dd691bf707d17013c2ffec953c2da7') in 0.043416 seconds"
        self.assertIsNone(parser.parse(suffix, IMPORT_LOG))
        started = IMPORTED.split(" -> ", 1)[0] + " Main Object Name differs from filename"
        self.assertEqual(parser.parse(started, IMPORT_LOG)["done"], 0)
        self.assertIsNone(parser.parse("#0 GetStacktrace(int)", IMPORT_LOG))
        complete = parser.parse(suffix, IMPORT_LOG)
        self.assertEqual(complete["done"], 1)
        self.assertIn("QuestBuild.cs", complete["detail"])
        self.assertIsNone(parser.parse(suffix, IMPORT_LOG))


class LiveImportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.store = Store(self.root / "owned")
        self.state = self.store.create(wizard.choices({"gameRoot": str(self.root / "Game")}))
        self.session = self.state["session"]
        self.folder = self.store.root / "build/logs"
        self.folder.mkdir(parents=True)
        self.supervisor = Supervisor(self.store, self.session, poll=.01)
        self.supervisor.set_stage("build")

    def tearDown(self): self.temp.cleanup()

    def test_real_child_import_log_is_discovered_and_progress_visible_before_exit(self):
        log = self.folder / IMPORT_LOG
        cache = self.store.root / "build/projects/retained/Library/Artifacts/owned"
        cache.parent.mkdir(parents=True); cache.write_bytes(b"already imported")
        stamp = cache.stat().st_mtime_ns
        boundary = json.dumps({"schema": 1, "phase": "operation:unity-import",
                               "operation": "unity-import", "status": "start"})
        script = ("from pathlib import Path;import sys,time;"
                  "print('GHVRQ_PROGRESS '+sys.argv[3],flush=True);"
                  "Path(sys.argv[1]).write_text(sys.argv[2]+'\\n'+sys.argv[2]+'\\n');time.sleep(.2)")
        seen = []
        def poll(process):
            row = self.store._state(self.session)["stages"][5]
            if row["progress"].get("phase") == "unity-asset-import": seen.append(dict(row["progress"]))
        with self.store.active(self.state):
            self.supervisor.run([sys.executable, "-I", "-c", script, str(log), IMPORTED, boundary],
                                self.store.session_dir(self.session) / "logs/build.log", on_poll=poll)
        self.assertTrue(seen)
        self.assertEqual(seen[-1]["done"], 2)
        self.assertEqual(seen[-1]["reportedOperation"], "unity-import")
        self.assertEqual(cache.read_bytes(), b"already imported")
        self.assertEqual(cache.stat().st_mtime_ns, stamp)

    def test_known_launch_api_logs_discovered_stale_archives_and_unknown_logs_ignored(self):
        started = time.time() - .01
        names = ("package-import-launch-abc.log", "package-api-abc.log", "content-pack-abc.log")
        for name in names: (self.folder / name).write_text("[1/2 0s] Csc current.dll\n")
        for name in ("package-import-old.log", "package-import-abc.memory-attempt-1.log", "unknown-current.log"):
            path = self.folder / name; path.write_text("[9/10 0s] Csc stale.dll\n")
            if name == "package-import-old.log": os.utime(path, (1, 1))
        tails = {}; parser = ProgressParser()
        with self.store.active(self.state), mock.patch.object(self.store, "progress", wraps=self.store.progress) as progress:
            self.supervisor._tail_progress(tails, parser, started)
        self.assertEqual({path.name for path in tails}, set(names))
        self.assertEqual({call.kwargs["operation"] for call in progress.call_args_list}, {"unity-import", "package-api", "content-bank"})
        self.assertTrue(all(call.kwargs["done"] == 1 for call in progress.call_args_list))

    def test_asset_batches_publish_latest_count_without_per_asset_state_writes(self):
        path = self.folder / IMPORT_LOG
        path.write_text((IMPORTED + "\n") * 250)
        parser = ProgressParser(); tails = {}
        with self.store.active(self.state), mock.patch.object(self.store, "progress", wraps=self.store.progress) as progress:
            self.supervisor._tail_progress(tails, parser, 1, final=True)
        self.assertEqual(progress.call_count, 1)
        self.assertEqual(progress.call_args.kwargs["done"], 250)
        self.assertIsNone(progress.call_args.kwargs["total"])

    def test_closed_child_tail_never_overwrites_current_runner_detail(self):
        root_log = self.store.session_dir(self.session) / "logs/build.log"
        root_log.parent.mkdir()
        root_log.write_text(boundary("unity-import", "complete") + "\n" + boundary("package-api") + "\n")
        path = self.folder / IMPORT_LOG; path.write_text(IMPORTED + "\n")
        with self.store.active(self.state):
            self.supervisor._tail_progress({root_log: LogTail(root_log)}, ProgressParser(), 1)
        row = self.store.load(self.session)["stages"][5]
        self.assertEqual(row["progress"]["phase"], "operation:package-api")
        self.assertEqual(row["progress"]["reportedOperation"], "package-api")


if __name__ == "__main__": unittest.main()
