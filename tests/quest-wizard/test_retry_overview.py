"""Actual runner retries distinguish live work from compatible retained evidence."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import stage_plan
import state
import wizard


class RunnerRetryOverviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / "owned")
        self.saved = self.store.create(wizard.choices({"gameRoot": str(self.root / "Game")}))
        self.session = self.saved["session"]
        self.interval = mock.patch("state.PROGRESS_INTERVAL", 0)
        self.interval.start(); self.addCleanup(self.interval.stop)
        self.calls = []

    def build_row(self):
        return self.store.load(self.session)["stages"][5]

    def action(self, name, build):
        def run(saved, supervisor):
            self.calls.append(name)
            if name == "build": build(saved)
            target = self.store.session_dir(self.session) / (name + ".txt")
            target.write_text(name)
            return [target], {}
        return run

    def engine(self, build, repo=ROOT):
        return wizard.Engine(self.store, repo, actions={name: self.action(name, build) for name in state.STAGES})

    def raw_and_staging(self):
        self.store.operation(self.session, "build", "recovery")
        self.store.progress(self.session, "build", "recovery-plan", 16, 16, "batches", status="reuse")
        for section in stage_plan.RECOVERY_SECTIONS[:-1]:
            self.store.progress(self.session, "build", "recovery-section:" + section, 1, 1, "sections", status="reuse")
            if section == "batches": self.store.progress(self.session, "build", "recovery-batches", 16, 16, "batches", status="reuse")
        self.store.progress(self.session, "build", "recovery-section:staging", status="start")

    def fail_at_copy(self, *_):
        self.raw_and_staging()
        for name in ("catalog", "canonical"):
            self.store.progress(self.session, "build", "staging-section:" + name, 1, 1, "steps", status="complete")
        self.store.progress(self.session, "build", "staging-section:copy", 0, 1, "steps", status="start")
        self.store.progress(self.session, "build", "staging-copy", 80, 100, "files", "old copy item")
        raise state.WizardError("build_tool_failed", "First attempt copy failure")

    def assert_no_stale_live_work(self, row):
        overview = row["progress"]["buildOverview"]
        self.assertIsNone(overview["active"])
        self.assertIsNone(overview["recovery"]["section"])
        self.assertIsNone(overview["recovery"]["stagingCounter"])
        self.assertNotIn("activeWork", row["progress"])
        for items in [overview["recovery"]["sections"], overview["recovery"]["staging"],
                      *[group["operations"] for group in overview["groups"]]]:
            self.assertTrue(all(item["status"] not in ("running", "checking", "failed") for item in items))

    def test_real_retries_reset_before_prerequisites_and_report_only_the_new_failure(self):
        self.assertEqual(self.engine(self.fail_at_copy).run(self.session)["status"], "failed")
        previous = self.build_row()["progress"]["stagePercent"]
        self.store = state.Store(self.store.root)
        observed = []
        original_valid = self.store.valid

        def valid(session, stage, key, **options):
            if stage == "tools" and options.get("report_progress"):
                queued = self.build_row()
                self.assertEqual(queued["status"], "pending")
                self.assertEqual(queued["progress"]["stagePercent"], previous)
                self.assertEqual(queued["progress"]["phase"], "pending")
                self.assert_no_stale_live_work(queued)
                self.assertTrue(all(item["status"] == "retained" for item in queued["progress"]["buildOverview"]["recovery"]["staging"][:2]))
                observed.append("prerequisites")
            return original_valid(session, stage, key, **options)

        def second(saved):
            self.assert_no_stale_live_work(self.build_row())
            self.store.operation(self.session, "build", "recovery")
            overview = self.build_row()["progress"]["buildOverview"]
            self.assertIsNone(overview["recovery"]["section"])
            self.assertTrue(all(item["status"] not in ("running", "failed") for item in overview["recovery"]["staging"]))
            self.raw_and_staging()
            for name in ("catalog", "canonical", "copy", "runtime", "guid", "layout"):
                self.store.progress(self.session, "build", "staging-section:" + name, 1, 1, "steps", status="reuse")
            self.store.progress(self.session, "build", "staging-section:native", 0, 1, "steps", status="start")
            raise state.WizardError("build_tool_failed", "Second attempt native failure")

        with mock.patch.object(self.store, "valid", side_effect=valid):
            failed = self.engine(second).run(self.session)
        self.assertEqual(observed, ["prerequisites"])
        self.assertEqual(failed["status"], "failed")
        self.assertEqual(failed["needsActions"][0]["message"]["en"], "Second attempt native failure")
        rows = self.build_row()["progress"]["buildOverview"]["recovery"]["staging"]
        self.assertEqual([item["id"] for item in rows if item["status"] == "failed"], ["native"])
        self.assertEqual(next(item for item in rows if item["id"] == "copy")["status"], "reused")

        self.store = state.Store(self.store.root)
        def third(saved):
            self.assert_no_stale_live_work(self.build_row())
            self.store.operation(self.session, "build", "game-inputs")
            self.assertEqual(self.build_row()["progress"]["buildOverview"]["active"], "game-inputs")
            self.assertEqual(self.build_row()["progress"]["buildOverview"]["recovery"]["section"], None)
        self.assertEqual(self.engine(third).run(self.session)["status"], "complete")
        self.assertEqual(self.calls.count("build"), 3)
        self.assertTrue(all(self.calls.count(name) == 1 for name in state.STAGES[:5]))

    def test_new_live_copy_counts_do_not_show_an_old_incomplete_fraction(self):
        self.assertEqual(self.engine(self.fail_at_copy).run(self.session)["status"], "failed")
        previous = self.build_row()["progress"]["stagePercent"]
        def retry(saved):
            self.raw_and_staging()
            self.store.progress(self.session, "build", "staging-section:copy", 0, 1, "steps", status="start")
            self.store.progress(self.session, "build", "staging-copy", 10, 100, "files", "current copy item")
            row = self.build_row(); overview = row["progress"]["buildOverview"]
            self.assertEqual(row["progress"]["stagePercent"], previous)
            self.assertEqual(overview["recovery"]["stagingCounter"]["done"], 10)
            self.assertEqual(next(item for item in overview["recovery"]["staging"] if item["id"] == "copy")["percent"], 10)
            self.assertEqual(row["progress"]["detail"], "current copy item")
            self.store.progress(self.session, "build", "staging-section:runtime", 0, 1, "steps", status="start")
            self.assertIsNone(self.build_row()["progress"]["buildOverview"]["recovery"]["stagingCounter"])
            self.store.operation(self.session, "build", "audio")
            self.assertIsNone(self.build_row()["progress"]["buildOverview"]["recovery"]["stagingCounter"])
        self.assertEqual(self.engine(retry).run(self.session)["status"], "complete")

    def test_actual_codec_retry_uses_current_item_fraction_and_retains_overall_high_water(self):
        def codec(count):
            self.store.operation(self.session, "build", "audio")
            self.store.progress(self.session, "build", "prepare-substage:bundled-audio", 0, 1, "checkpoints", operation="audio", status="start")
            self.store.progress(self.session, "build", "prepare-items:bundled-audio", count, 100, "items")
        def first(saved):
            codec(90)
            raise state.WizardError("build_tool_failed", "First codec attempt failure")
        self.assertEqual(self.engine(first).run(self.session)["status"], "failed")
        previous = self.build_row()["progress"]["stagePercent"]
        self.store = state.Store(self.store.root)
        def retry(saved):
            codec(10)
            row = self.build_row()
            self.assertEqual(row["progress"]["stagePercent"], previous)
            self.assertEqual(row["progressPlan"]["fractions"]["audio"], .9)
            self.assertEqual(row["progress"]["activeWork"]["percent"], 10)
            self.assertEqual(next(item for group in row["progress"]["buildOverview"]["groups"] for item in group["operations"] if item["id"] == "audio")["percent"], 10)
        self.assertEqual(self.engine(retry).run(self.session)["status"], "complete")

    def test_changed_real_source_dependency_has_no_false_retained_build_credit(self):
        release = self.root / "release"; release.mkdir()
        manifest = release / "quest-builder-release.json"
        state.atomic_json(manifest, {"sourceCommit": "a" * 40})
        engine = self.engine(self.fail_at_copy, release)
        source_action = engine.actions["source"]
        def source(saved, supervisor):
            paths, details = source_action(saved, supervisor)
            paths[0].write_text(manifest.read_text())
            return paths, details
        engine.actions["source"] = source
        self.assertEqual(engine.run(self.session)["status"], "failed")
        self.assertGreater(self.build_row()["progress"]["stagePercent"], 0)
        state.atomic_json(manifest, {"sourceCommit": "b" * 40})
        def current(saved):
            row = self.build_row()
            self.assertEqual(row["progress"]["stagePercent"], 0)
            self.assertEqual(row["progressPlan"]["completed"], [])
            self.assertEqual(row["progress"]["buildOverview"]["recovery"]["batches"], {"done": None, "total": None})
            self.assert_no_stale_live_work(row)
        engine.actions["build"] = self.action("build", current)
        self.assertEqual(engine.run(self.session)["status"], "complete")

    def test_process_death_keeps_accepted_history_but_new_runner_discards_live_pointers(self):
        script = r"""
import os,sys
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from state import Store,STAGES
from wizard import Engine
store=Store(Path(sys.argv[2]));session=sys.argv[3]
def action(name):
 def run(saved,supervisor):
  if name=='build':
   store.operation(session,name,'recovery')
   store.progress(session,name,'recovery-plan',16,16,'batches')
   store.progress(session,name,'recovery-section:staging',status='start')
   store.progress(session,name,'staging-section:copy',0,1,'steps',status='start')
   store.progress(session,name,'staging-copy',80,100,'files','interrupted item')
   os._exit(27)
  target=store.session_dir(session)/(name+'.txt');target.write_text(name)
  return [target],{}
 return run
Engine(store,actions={name:action(name) for name in STAGES}).run(session)
"""
        process = subprocess.run([sys.executable, "-I", "-B", "-c", script, str(ROOT / "tools/quest-wizard"), str(self.store.root), self.session], capture_output=True, text=True, timeout=20)
        self.assertEqual(process.returncode, 27, process.stderr)
        self.store = state.Store(self.store.root)
        interrupted = self.store.load(self.session)
        self.assertEqual(interrupted["status"], "interrupted")
        previous = self.build_row()["progress"]["stagePercent"]
        def resumed(saved):
            row = self.build_row()
            self.assert_no_stale_live_work(row)
            self.assertEqual(row["progress"]["stagePercent"], previous)
            self.assertNotIn("interrupted item", str(row["progress"]))
            self.store.operation(self.session, "build", "recovery")
            self.assertIsNone(self.build_row()["progress"]["buildOverview"]["recovery"]["section"])
        self.assertEqual(self.engine(resumed).run(self.session)["status"], "complete")


if __name__ == "__main__": unittest.main()
