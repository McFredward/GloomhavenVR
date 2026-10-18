"""Finite early-import denominators, live epochs and crash-safe warm coverage."""
import importlib.util
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
from state import Store
import wizard
spec = importlib.util.spec_from_file_location("work_observer_source", ROOT / "tools/quest-builder/unity_work.py")
work = importlib.util.module_from_spec(spec); spec.loader.exec_module(work)


def imported(path, duration="0.001092"):
    return "Start importing " + path + " using Guid(" + "a" * 32 + ") Importer(-1," + "0" * 32 + ")  -> " + \
           "(artifact id: '" + "b" * 32 + "') in " + duration + " seconds"


def boundary(operation, status="start"):
    return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "operation:" + operation,
        "operation": operation, "status": status, "done": None, "total": None})


class Progress:
    def event(self, *args, **kwargs): pass


class WorkObserverTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.store = Store(self.root / "owned")
        self.state = self.store.create(wizard.choices({"gameRoot": str(self.root / "Game")})); self.session = self.state["session"]
        self.project = self.store.root / "build/projects/retained"
        for relative in ("Assets/Figures/Same.asset", "Assets/Cards/Same.asset", "Assets/Quest/Editor/QuestBuild.cs"):
            path = self.project / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(b"source")
            path.with_name(path.name + ".meta").write_text("guid: " + "a" * 32)
        self.native = self.store.root / "build/logs/package-import-abc.log"
        self.launch = self.native.with_name("package-import-launch-abc.log")
        self.argv = ["Unity", "-projectPath", str(self.project), "-logFile", str(self.native), "-buildTarget", "Android"]
        self.parser = ProgressParser()
        self.supervisor = Supervisor(self.store, self.session, poll=.01); self.supervisor.set_stage("build")

    def tearDown(self): self.parser.close_coverage(); self.temp.cleanup()

    def bind(self, *, source=None):
        plan = work.publish_plan(self.argv, self.launch, Progress())
        log = source or self.native; log.write_text("current Unity invocation\n")
        observed = self.parser.observe_plan(log)
        self.assertEqual((observed["done"], observed["total"]), (plan["done"], plan["total"]))
        return plan, observed

    def test_new_plan_supplies_fixed_total_and_unique_full_paths(self):
        plan, initial = self.bind(); self.assertEqual((initial["done"], initial["total"]), (0, 3))
        one = self.parser.parse(imported("Assets/Figures/Same.asset"), self.native.name)
        repeat = self.parser.parse(imported("Assets/Figures/Same.asset"), self.native.name)
        other = self.parser.parse(imported("Assets/Cards/Same.asset"), self.native.name)
        self.assertEqual((one["done"], repeat["done"], other["done"], other["total"]), (1, 1, 2, 3))
        self.assertIn("[asset-coverage]", other["detail"]); self.assertIn("Assets/Cards/Same.asset", other["detail"])
        self.assertEqual(other["operation"], "unity-import")

    def test_split_suffix_and_unknown_package_path_cannot_inflate_denominator(self):
        self.bind()
        complete = imported("Assets/Figures/Same.asset")
        prefix, suffix = complete.split("  -> ", 1)
        self.parser.parse(prefix + " Main Object Name differs", self.native.name)
        self.parser.parse("#0 importer warning stack", self.native.name)
        result = self.parser.parse("  -> " + suffix, self.native.name)
        self.assertEqual((result["done"], result["total"]), (1, 3))
        result = self.parser.parse(imported("Packages/com.unresolved/New.cs"), self.native.name)
        self.assertEqual((result["done"], result["total"]), (1, 3))
        self.assertIsNone(self.parser.parse("  -> " + suffix, self.native.name))

    def test_native_true_total_supersedes_inventory_and_remains_during_activity(self):
        self.bind()
        native = self.parser.parse("Importing 4 assets of 8", self.native.name)
        self.assertEqual((native["done"], native["total"]), (4, 8))
        self.assertIn("[unity-native-total]", native["detail"])
        later = self.parser.parse(imported("Assets/Figures/Same.asset"), self.native.name)
        refreshed = self.parser.parse("Asset Pipeline Refresh: Total: 1.5 seconds", self.native.name)
        self.assertEqual((later["done"], later["total"], refreshed["done"], refreshed["total"]), (4, 8, 4, 8))

    def test_coverage_total_never_finishes_parent_before_process_result(self):
        self.bind()
        for path in ("Assets/Figures/Same.asset", "Assets/Cards/Same.asset", "Assets/Quest/Editor/QuestBuild.cs"):
            result = self.parser.parse(imported(path), self.native.name)
        self.assertEqual((result["done"], result["total"], result["status"]), (3, 3, "progress"))
        self.assertNotIn("unity-import", self.parser.closed_operations)

    def test_flush_then_crash_resume_retains_witnessed_sources_without_payload_reads(self):
        self.bind(); self.parser.parse(imported("Assets/Figures/Same.asset"), self.native.name)
        self.parser.flush_coverage(); self.parser.close_coverage(); self.parser = ProgressParser()
        original = Path.open
        def guarded(path, *args, **kwargs):
            if path.is_relative_to(self.project / "Assets"): raise AssertionError("Payload re-read")
            return original(path, *args, **kwargs)
        with mock.patch.object(Path, "open", guarded): plan, initial = self.bind()
        self.assertEqual((initial["done"], initial["total"]), (1, 3))
        result = self.parser.parse(imported("Assets/Cards/Same.asset"), self.native.name)
        self.assertEqual(result["done"], 2)

    def test_launch_and_editor_aliases_share_one_completed_path_set(self):
        self.bind(); self.launch.write_text("live launch")
        self.parser.observe_plan(self.launch)
        self.parser.parse(imported("Assets/Figures/Same.asset"), self.native.name)
        same = self.parser.parse(imported("Assets/Figures/Same.asset"), self.launch.name)
        self.assertEqual((same["done"], same["total"]), (1, 3))
        self.assertEqual(len(self.parser.coverage_invocations), 1)

    def test_new_invocation_resets_pending_suffix_and_keeps_native_owner_correct(self):
        self.bind(); line = imported("Assets/Figures/Same.asset")
        self.parser.parse(line.split("  -> ", 1)[0], self.native.name)
        self.parser.parse("Importing 4 assets of 8", self.native.name)
        self.bind()
        self.assertIsNone(self.parser.parse("  -> " + line.split("  -> ", 1)[1], self.native.name))
        result = self.parser.parse(imported("Assets/Cards/Same.asset"), self.native.name)
        self.assertEqual((result["done"], result["total"]), (1, 3))

    def test_stale_sidecar_and_unwritten_previous_log_cannot_bind_coverage(self):
        self.native.parent.mkdir(parents=True, exist_ok=True); self.native.write_text(imported("Assets/Figures/Same.asset"))
        plan = work.publish_plan(self.argv, self.launch, Progress())
        self.assertIsNone(self.parser.observe_plan(self.native))
        self.native.write_text("new log")
        self.assertIsNone(self.parser.observe_plan(self.native, started=plan["createdAt"] + 1))
        self.assertFalse(self.parser.import_coverage)

    def test_replaced_database_and_invalid_small_control_fall_back_without_exception(self):
        plan, _ = self.bind(); self.parser.close_coverage(); self.parser = ProgressParser()
        control = work.sidecar(self.native); control.write_bytes(b"x" * (work.MAX_CONTROL_BYTES + 1))
        self.assertIsNone(self.parser.observe_plan(self.native))
        control.write_text("{}")
        self.assertIsNone(self.parser.observe_plan(self.native))

    def test_live_tail_uses_plan_before_import_and_batches_state_writes(self):
        plan = work.publish_plan(self.argv, self.launch, Progress())
        self.native.write_text((imported("Assets/Figures/Same.asset") + "\n") * 250 + imported("Assets/Cards/Same.asset") + "\n")
        with self.store.active(self.state), mock.patch.object(self.store, "progress", wraps=self.store.progress) as progress:
            self.supervisor._tail_progress({}, self.parser, plan["createdAt"] - .01, final=True)
        self.assertEqual(progress.call_count, 2)  # One plan and one latest batch observation.
        self.assertEqual((progress.call_args.kwargs["done"], progress.call_args.kwargs["total"]), (2, 3))
        self.parser.close_coverage(); self.parser = ProgressParser()
        resumed, initial = self.bind(); self.assertEqual(initial["done"], 2)

    def test_current_gradle_sidecar_is_live_incremental_and_plain_tasks_preserve_total(self):
        native = self.native.with_name("unity-build-player.log")
        argv = list(self.argv); argv[argv.index(str(self.native))] = str(native)
        plan = work.publish_plan(argv, self.launch, Progress()); native.write_text("current Player invocation")
        side = Path(str(native) + ".gradle-progress.jsonl")
        def graph(done, status="progress"):
            return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "unity-gradle-tasks",
                "operation": "player", "done": done, "total": 12, "unit": "tasks", "status": status}) + "\n"
        side.write_text(graph(0, "start") + graph(3))
        self.parser.parse(boundary("player"), "build.log")
        tails = {}
        with self.store.active(self.state), mock.patch.object(self.store, "progress", wraps=self.store.progress) as progress:
            self.supervisor._tail_progress(tails, self.parser, plan["createdAt"] - .01)
            self.assertEqual((progress.call_args.kwargs["done"], progress.call_args.kwargs["total"]), (3, 12))
            self.assertIn(side, tails); offset = tails[side].offset
            calls = progress.call_count
            self.supervisor._tail_progress(tails, self.parser, plan["createdAt"] - .01)
            self.assertEqual(progress.call_count, calls)
            self.assertEqual(tails[side].offset, offset)
            with side.open("a") as stream: stream.write(graph(5))
            self.supervisor._tail_progress(tails, self.parser, plan["createdAt"] - .01)
            self.assertEqual((progress.call_args.kwargs["done"], progress.call_args.kwargs["total"]), (5, 12))
        self.assertIsNone(self.parser.parse(graph(0, "start"), native.name))
        plain = self.parser.parse("> Task :launcher:compileReleaseJavaWithJavac", native.name)
        self.assertEqual((plain["done"], plain["total"]), (5, 12))
        footer = self.parser.parse("8 actionable tasks: 6 executed, 2 up-to-date", native.name)
        self.assertEqual((footer["done"], footer["total"]), (5, 12))
        self.assertNotIn("player", self.parser.closed_operations)

    def test_gradle_distinct_graph_epoch_resets_only_child_and_rejects_retired_echo(self):
        self.parser.parse(boundary("player"), "build.log")
        def graph(epoch, done, status="progress"):
            return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "unity-gradle-tasks",
                "operation": "player", "done": done, "total": 12, "unit": "tasks", "status": status,
                "detail": "[gradle-graph:" + epoch + "] actual graph"})
        self.parser.parse(graph("first", 0, "start"), "unity-build-player.log")
        self.parser.parse(graph("first", 5), "unity-build-player.log")
        self.assertIsNone(self.parser.parse(graph("first", 0, "start"), "unity-build-player.log"))
        new = self.parser.parse(graph("second", 0, "start"), "unity-build-player.log")
        self.assertEqual((new["done"], new["total"]), (0, 12))
        self.assertIsNone(self.parser.parse(graph("first", 12), "unity-build-player.log"))
        self.assertNotIn("player", self.parser.closed_operations)

    def test_scoped_editor_task_ids_and_shader_compile_identity_are_retained(self):
        def task(name):
            return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "unity-progress:42:3",
                "operation": "player", "done": 5, "total": 17, "unit": "steps", "detail": name + " — detail"})
        normal = self.parser.parse(task("Asset import"), "unity-build-player.log")
        shader = self.parser.parse(task("ShaderCompile"), "unity-build-player.log")
        self.assertEqual(normal["phase"], "unity-progress:42:3")
        self.assertEqual(shader["phase"], "unity-shader-task:42:3")
        self.assertEqual((shader["done"], shader["total"]), (5, 17))

    def test_stale_gradle_sidecar_cannot_supply_this_invocations_total(self):
        side = Path(str(self.native) + ".gradle-progress.jsonl")
        self.native.parent.mkdir(parents=True, exist_ok=True)
        side.write_text("GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "unity-gradle-tasks",
            "operation": "player", "done": 12, "total": 12, "unit": "tasks"}) + "\n")
        os.utime(side, (1, 1))
        plan, _ = self.bind(); tails = {}
        with self.store.active(self.state): self.supervisor._tail_progress(tails, self.parser, plan["createdAt"] - .01)
        self.assertNotIn(side, tails)
        self.assertFalse(self.parser.gradle_counters)

    def test_closed_operation_cannot_accept_late_coverage_or_new_plan(self):
        self.bind(); self.parser.parse(boundary("unity-import", "complete"), "build.log")
        self.assertIsNone(self.parser.parse(imported("Assets/Figures/Same.asset"), self.native.name))
        self.parser.flush_coverage()
        # The ignored late log must not add durable completion to the next attempt.
        self.parser.close_coverage(); self.parser = ProgressParser()
        plan, _ = self.bind(); self.assertEqual(plan["done"], 0)


if __name__ == "__main__": unittest.main()
