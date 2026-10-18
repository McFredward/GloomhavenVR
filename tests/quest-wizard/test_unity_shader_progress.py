"""Unity's emitted compiler counters are observed without another compile pass."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
from processes import ProgressParser, Supervisor
from state import Store, stage_progress
import wizard


SOURCE = "mod-bundle-6f0a347ff362.log"
START = 'Compiling shader "GloomhavenVR/WorldSimpleMaterial" pass "FORWARD" (vp)'
STRIPPED = "12288 / 12288 variants left after stripping, processed in 0.00 seconds"
FINISHED = ("finished in 158.20 seconds. Local cache hits 0 (0.00s CPU time), "
            "remote cache hits 0 (0.00s CPU time), compiled 12288 variants (1423.51s CPU time), skipped 0 variants")


def event(phase, **values):
    return "GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": phase, **values})


class ShaderCounterTests(unittest.TestCase):
    def setUp(self): self.parser = ProgressParser()

    def start(self, source=SOURCE, start=START, stripped=STRIPPED):
        self.parser.parse(start, source)
        return self.parser.parse(stripped, source)

    def test_captured_long_pass_has_real_live_progress_before_completion(self):
        # These exact lines occur at 6983-6988 in capture123047's Unity log.
        start = self.parser.parse(START, SOURCE)
        self.assertEqual(start["operation"], "mod-banks")
        self.assertIsNone(start["total"])
        pending = self.parser.parse(STRIPPED, SOURCE)
        self.assertEqual((pending["done"], pending["total"]), (0, 12288))
        first = self.parser.parse("[ 60s] 4903 / 12288 variants ready", SOURCE)
        later = self.parser.parse("[120s] 9467 / 12288 variants ready", SOURCE)
        self.assertEqual((first["done"], later["done"]), (4903, 9467))
        self.assertAlmostEqual(stage_progress(**{key: first[key] for key in ("phase", "done", "total", "unit", "detail")})["percent"], 39.90071614583333)
        final = self.parser.parse(FINISHED, SOURCE)
        self.assertEqual((final["done"], final["total"]), (12288, 12288))
        self.assertEqual(final["status"], "complete")
        self.assertIn("completed passes 1", final["detail"])
        self.assertIn("completed variants 12288", final["detail"])

    def test_stripping_count_is_pending_work_and_not_finished_variants(self):
        value = self.start(stripped="6 / 384 variants left after stripping, processed in 0.00 seconds")
        self.assertEqual((value["done"], value["total"]), (0, 6))
        self.assertIn("completed variants 0", value["detail"])

    def test_cache_and_actual_outputs_are_counted_once(self):
        self.start(stripped="12 / 12 variants left after stripping, processed in 0.00 seconds")
        summary = ("finished in 1.10 seconds. Local cache hits 4 (0.00s CPU time), "
                   "remote cache hits 2 (0.00s CPU time), compiled 5 variants (2.00s CPU time), skipped 1 variants")
        value = self.parser.parse(summary, SOURCE)
        self.assertEqual((value["done"], value["total"]), (12, 12))
        self.assertIsNone(self.parser.parse(summary, SOURCE))

    def test_orphan_counters_cannot_create_an_invented_compilation(self):
        for line in (STRIPPED, "[ 60s] 4903 / 12288 variants ready", FINISHED):
            with self.subTest(line=line): self.assertIsNone(self.parser.parse(line, SOURCE))
        self.parser.parse(START, SOURCE)
        self.assertIsNone(self.parser.parse("[ 60s] 4903 / 12288 variants ready", SOURCE))

    def test_changed_totals_overshoot_regression_and_bad_summary_are_rejected(self):
        self.start()
        self.parser.parse("[ 60s] 4903 / 12288 variants ready", SOURCE)
        for line in ("[120s] 2 / 8 variants ready", "[120s] 13000 / 12288 variants ready",
                     "[120s] 4000 / 12288 variants ready", "[120s] 1 / 0 variants ready",
                     FINISHED.replace("compiled 12288", "compiled 12287")):
            with self.subTest(line=line): self.assertIsNone(self.parser.parse(line, SOURCE))
        self.assertEqual(self.parser.parse("[180s] 9467 / 12288 variants ready", SOURCE)["done"], 9467)

    def test_repeated_shader_pass_resets_only_current_pass(self):
        self.start(); self.parser.parse(FINISHED, SOURCE)
        again = self.start()
        self.assertEqual((again["done"], again["total"]), (0, 12288))
        self.assertIn("pass #2", again["detail"])
        self.assertIn("completed passes 1", again["detail"])
        self.assertIn("completed variants 12288", again["detail"])
        self.assertEqual(again["status"], "progress")

    def test_ready_total_and_successful_pass_never_close_parent_operation(self):
        self.start()
        ready = self.parser.parse("[180s] 12288 / 12288 variants ready", SOURCE)
        self.assertEqual(ready["status"], "progress")
        value = self.parser.parse(FINISHED, SOURCE)
        self.assertEqual(value["status"], "complete")
        self.assertEqual(value["operation"], "mod-banks")

    def test_failure_cannot_be_followed_by_false_pass_completion(self):
        self.start(); self.parser.parse("[ 60s] 4903 / 12288 variants ready", SOURCE)
        failure = self.parser.parse("Shader error in 'GloomhavenVR/WorldSimpleMaterial': syntax error", SOURCE)
        self.assertEqual((failure["done"], failure["total"], failure["status"]), (4903, 12288, "failed"))
        self.assertIsNone(self.parser.parse(FINISHED, SOURCE))
        retry = self.start()
        self.assertIn("pass #2", retry["detail"])
        self.assertIn("completed passes 0", retry["detail"])

    def test_other_concurrent_log_has_independent_shader_state(self):
        self.start()
        other = "unity-build-current.log"
        self.start(other, stripped="6 / 6 variants left after stripping, processed in 0.00 seconds")
        actual = self.parser.parse("[ 60s] 4903 / 12288 variants ready", SOURCE)
        self.assertEqual(actual["operation"], "mod-banks")
        self.assertIsNone(self.parser.parse("[ 60s] 4903 / 12288 variants ready", other))
        value = self.parser.parse("[ 60s] 3 / 6 variants ready", other)
        self.assertEqual(value["operation"], "unity-import")
        self.assertEqual(value["done"], 3)

    def test_player_owner_follows_real_boundary_in_same_source(self):
        source = "unity-build.log"
        for owner in ("content-bank", "player"):
            self.parser.parse(event("operation:" + owner, operation=owner, status="start"), source)
            value = self.start(source)
            self.assertEqual(value["operation"], owner)
            self.assertIn("pass #1", value["detail"])
            self.parser.parse(FINISHED, source)
        self.assertEqual(self.start(SOURCE)["operation"], "mod-banks")

    def test_public_editor_shader_task_preserves_actual_steps(self):
        source = "unity-build.log"
        self.parser.parse(event("operation:content-bank", operation="content-bank", status="start"), source)
        value = self.parser.parse(event("unity-progress", done=31, total=100, unit="steps",
                                       detail="Unity: Compiling shaders — Vulkan", status="progress"), source)
        self.assertEqual(value["phase"], "unity-shader-task")
        self.assertEqual((value["done"], value["total"], value["operation"]), (31, 100, "content-bank"))
        self.assertEqual(value["unit"], "steps")
        self.assertEqual(value["status"], "progress")
        ordinary = self.parser.parse(event("unity-progress", done=31, total=100, unit="steps",
                                          detail="Unity: Importing textures", status="progress"), source)
        self.assertEqual(ordinary["phase"], "unity-progress")
        asset_task = self.parser.parse(event("unity-progress", done=31, total=100, unit="steps",
                                            detail="Unity: Importing assets — Compiling shader preview", status="progress"), source)
        self.assertEqual(asset_task["phase"], "unity-progress")

    def test_unknown_task_and_unknown_source_remain_without_invented_owner_or_total(self):
        value = self.parser.parse(event("unity-progress", detail="Unity: Shader compilation", status="progress"), "unknown.log")
        self.assertEqual(value["phase"], "unity-shader-task")
        self.assertIsNone(value["done"]); self.assertIsNone(value["total"])
        self.assertNotIn("operation", value)
        self.assertEqual(self.start("update-code-unity.log")["operation"], "update-code")

    def test_mod_log_lifecycle_does_not_reassign_bank_to_player(self):
        self.parser.parse(event("operation:player", operation="player", status="start"), SOURCE)
        self.assertEqual(self.start()["operation"], "mod-banks")
        malformed = self.parser.parse(event("unity-progress", done=31, total=10, unit="steps",
                                            detail="Unity: Compiling shaders", status="progress"), SOURCE)
        self.assertIsNone(malformed)


class CompilerSupervisionTests(unittest.TestCase):
    def test_current_mod_bank_log_is_read_live_and_old_attempt_ignored(self):
        with tempfile.TemporaryDirectory() as directory:
            store = Store(Path(directory) / "owned")
            saved = store.create(wizard.choices({"gameRoot": str(Path(directory) / "Game")}))
            folder = store.root / "build/logs"; folder.mkdir(parents=True)
            old = folder / "mod-bundle-old.log"; old.write_text(START + "\n" + STRIPPED + "\n" + FINISHED + "\n")
            os.utime(old, (1, 1))
            current = folder / SOURCE
            lines = [START, STRIPPED, "[ 60s] 4903 / 12288 variants ready"]
            code = ("from pathlib import Path;import sys,time;"
                    "Path(sys.argv[1]).write_text(sys.argv[2]);time.sleep(.2)")
            supervisor = Supervisor(store, saved["session"], poll=.01); supervisor.set_stage("build")
            with store.active(saved):
                store.operation(saved["session"], "build", "mod-banks")
                supervisor.run([sys.executable, "-I", "-c", code, str(current), "\n".join(lines) + "\n"],
                               store.session_dir(saved["session"]) / "logs/build.log")
            row = next(item for item in store.load(saved["session"])["stages"] if item["id"] == "build")
            self.assertEqual((row["progress"]["done"], row["progress"]["total"]), (4903, 12288))
            self.assertEqual(row["progress"]["phase"], "unity-shader-compile")
            self.assertEqual(row["progress"]["stageOperation"], "mod-banks")
            self.assertNotIn("mod-banks", row["progressPlan"]["completed"])
            self.assertNotIn("unity-import", row["progressPlan"]["completed"])
            self.assertIn("completed variants 0", row["progress"]["detail"])


if __name__ == "__main__": unittest.main()
