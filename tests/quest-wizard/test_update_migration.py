"""Real parser/store/workflow transitions for retained full Mod updates.

These bounded fixtures do not compile Unity, sign a real APK or claim a Quest
outcome. They exercise the production plan, state and subprocess argument seams.
"""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import qualification
import stage_plan
import state
import wizard
from processes import ProgressParser


class UpdateMigrationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.apk = self.root / "base.apk"; self.apk.write_bytes(b"selected old APK fixture")
        self.store = state.Store(self.root / "owned")
        self.saved = self.store.create(wizard.choices({"mode": "update-mod", "gameRoot": str(self.root / "PC"), "baseApk": str(self.apk)}))
        self.session = self.saved["session"]
        self.store.begin_stage(self.session, "build", "same-key", work_key="same-work")
        interval = patch.object(state, "PROGRESS_INTERVAL", 0); interval.start(); self.addCleanup(interval.stop)

    def row(self):
        return next(row for row in self.store.load(self.session)["stages"] if row["id"] == "build")

    def feed(self, **value):
        fields = ProgressParser().parse("GHVRQ_PROGRESS " + json.dumps({"schema": 1, **value}), "build.log")
        self.assertIsNotNone(fields)
        return self.store.progress(self.session, "build", **fields)

    def migrate(self):
        return self.feed(phase="update-rebuild-required", detail="Quest shader sources changed", operation="update-code", status="progress")

    def test_late_source_proven_migration_keeps_bar_and_counts_actual_new_work(self):
        self.store.operation(self.session, "build", "update-owned-game", complete=True)
        self.store.operation(self.session, "build", "update-mod-source", complete=True)
        self.store.operation(self.session, "build", "update-code")
        self.store.progress(self.session, "build", "unity-progress", 1, 2, "tasks", operation="update-code")
        before = self.row()["progress"]["stagePercent"]
        migrated = self.migrate()
        self.assertEqual(migrated["stagePercent"], before)
        self.assertEqual(migrated["stageTotal"], len(stage_plan.PLANS["build"]))
        self.assertEqual(migrated["buildOverview"]["workflow"], "build")
        self.assertEqual(migrated["buildOverview"]["updateMigration"]["priorCompleted"], ["update-owned-game", "update-mod-source"])
        operations = [item for group in migrated["buildOverview"]["groups"] for item in group["operations"]]
        self.assertEqual(tuple(item["id"] for item in operations), stage_plan.PLANS["build"])
        self.assertTrue(all(item["status"] == "pending" and not item["closed"] for item in operations))
        self.assertNotIn("update-code", migrated.get("activeWork", {}))
        next_work = self.feed(phase="game-hash", done=1, total=4, unit="bytes", operation="game-inputs", status="progress")
        self.assertGreater(next_work["stagePercent"], before)
        self.assertEqual(next_work["stageOperation"], "game-inputs")

    def test_predeclared_migration_does_not_close_full_inputs_from_compact_preflight(self):
        self.migrate()
        preflight = self.feed(phase="operation:update-owned-game", done=1, total=1, unit="operations", operation="update-owned-game", status="complete")
        self.assertEqual(preflight["stagePercent"], 0)
        self.assertEqual(preflight["buildOverview"]["done"], 0)
        self.assertEqual(preflight["migrationSetupOperation"], "update-owned-game")
        self.assertEqual(self.row()["workMode"], "update-mod")
        self.store.operation(self.session, "build", "recovery")
        before = self.row()["progress"]["stagePercent"]
        self.feed(phase="recovery-plan", done=0, total=2, unit="batches", operation="recovery", status="progress")
        self.feed(phase="recovery-section:source", done=0, total=1, unit="sections", operation="recovery", status="start")
        value = self.feed(phase="recovery-source-hash", done=1, total=2, unit="bytes", operation="recovery", status="progress")
        self.assertGreater(value["stagePercent"], before)
        self.assertEqual(value["stageOperation"], "recovery")

    def test_corrected_compatibility_replaces_old_fallback_without_resetting_total_bar(self):
        self.migrate()
        self.store.operation(self.session, 'build', 'recovery', complete=True)
        self.store.operation(self.session, 'build', 'textures', complete=True)
        before = self.row()['progress']['stagePercent']
        value = self.feed(phase='update-compact-qualified', status='progress')
        self.assertEqual(value['stagePercent'], before)
        self.assertEqual(stage_plan.workflow(self.row()), 'update-mod')
        self.assertEqual(value['stageTotal'], len(stage_plan.UPDATE_PLANS['update-mod']))
        self.assertEqual(value['buildOverview']['done'], 0)
        self.store.operation(self.session, 'build', 'update-owned-game', complete=True)
        self.assertGreater(self.row()['progress']['stagePercent'], before)
        self.assertNotIn('recovery', self.row()['progressPlan']['completed'])

    def test_duplicate_notice_and_retry_keep_only_current_full_live_phase(self):
        self.migrate()
        self.store.operation(self.session, "build", "textures")
        before = self.row()["progress"]["stagePercent"]
        repeated = self.migrate()
        self.assertEqual(repeated["stagePercent"], before)
        self.assertEqual(repeated["stageOperation"], "textures")
        saved = self.store.load(self.session); saved["status"] = "failed"; saved["stages"][5]["status"] = "failed"; self.store.save(saved)
        self.store.begin_run(self.session)
        self.store.begin_stage(self.session, "build", "same-key", work_key="same-work")
        row = self.row()
        self.assertEqual(stage_plan.workflow(row), "build")
        self.assertEqual(row["progress"]["stagePercent"], before)
        self.assertIsNone(row["progress"]["buildOverview"]["active"])
        operations = [item for group in row["progress"]["buildOverview"]["groups"] for item in group["operations"]]
        self.assertFalse(any(item["status"] == "failed" for item in operations))
        self.store.operation(self.session, "build", "unity-import")
        self.assertEqual(self.row()["progress"]["stageOperation"], "unity-import")

    def test_changed_explicit_selection_clears_migration_and_unrelated_modes_reject_notice(self):
        self.migrate()
        selected = copy.deepcopy(self.saved["choices"]); selected["baseApk"] = str(self.root / "different.apk")
        self.store.amend(self.session, selected)
        self.assertEqual(stage_plan.workflow(self.row()), "update-mod")
        self.assertEqual(self.row()["progress"]["stageTotal"], 6)
        selected["mode"] = "update-profile"; self.store.amend(self.session, selected)
        with self.assertRaises(state.WizardError): self.migrate()
        self.assertEqual(stage_plan.workflow(self.row()), "update-profile")

    def test_finalization_still_requires_verified_stage_completion(self):
        self.migrate()
        for operation in stage_plan.PLANS["build"]:
            result = self.store.operation(self.session, "build", operation, complete=True)
            self.assertLess(result["stagePercent"], 100)
        saved = self.store.load(self.session); saved["stages"][5]["status"] = "complete"; self.store.save(saved)
        self.assertEqual(self.row()["progress"]["stagePercent"], 100)
        self.assertEqual(self.row()["progress"]["buildOverview"]["done"], 23)

    def stage_context(self):
        saved = self.store.load(self.session)
        saved["completed"] = {
            "source": {"details": {"sourceRoot": str(ROOT)}},
            "tools": {"details": {"dotnet8": "local-dotnet8", "dotnet10": "local-dotnet10"}},
            "unity": {"details": {"unityEditor": "local-Unity"}},
            "profile": {"details": {"profilePath": str(self.root / "profile.json"), "steamLogo": str(self.root / "logo.png")}},
            "inspect": {"details": {"inputKey": "a" * 64, "gameKey": "b" * 64, "modBuild": 671,
                                      "updateStrategy": "cached-full-build", "requiresUnity": True,
                                      "rebuildReasons": ["Changed native runtime"]}}}
        self.store.save(saved)
        return saved

    def test_inspect_full_strategy_pauses_for_decision_without_full_capacity_reserve(self):
        saved = self.stage_context(); engine = wizard.Engine(self.store)
        root = self.store.root / "build"
        plan = {"schema": 1, "mode": "update-mod", "updateKey": "c" * 64, "gameInputKey": "a" * 64,
                "baseApkSha256": "d" * 64, "inputs": {"game": {"key": "b" * 64}, "mod": {"modBuild": 671}},
                "updateStrategy": "cached-full-build", "requiresUnity": True, "rebuildReasons": ["Changed native runtime"]}
        state.atomic_json(root / "latest-update-input.json", {"schema": 1, "plan": "updates/" + "c" * 64 + "/plan.json"})
        state.atomic_json(root / "updates" / ("c" * 64) / "plan.json", plan)
        supervisor = Mock()
        with self.store.active(saved), patch.object(provision_environment(), "environment", return_value={}):
            with self.assertRaises(state.WizardError) as caught:
                engine.stage_inspect(saved, supervisor)
            self.assertEqual(caught.exception.code, "update_rebuild_decision_required")
            self.assertTrue(caught.exception.parameters["updateGuidance"]["newBuild"])
            self.assertIn("Changed native runtime", caught.exception.parameters["cause"])
            supervisor.run.assert_called_once()
            with patch.object(qualification, "qualify", return_value={"workspaceRoot": str(self.store.root), "freeBytes": 1, "requiredFreeBytes": 1}) as capacity:
                engine.qualify(saved, stage="build")
            self.assertEqual(capacity.call_args.kwargs["mode"], "update-mod")
        self.assertEqual(json.loads((root / "updates" / ("c" * 64) / "plan.json").read_text()), plan)

    def test_saved_full_strategy_cannot_launch_child_or_convert_original_contents(self):
        saved = self.stage_context(); engine = wizard.Engine(self.store)
        self.migrate()  # Historical fallback display is not current consent.
        saved = self.store.load(self.session)
        supervisor = Mock()
        with self.store.active(saved):
            with self.assertRaises(state.WizardError) as caught:
                engine.stage_build(saved, supervisor)
        supervisor.run.assert_not_called()
        self.assertEqual(caught.exception.code, "update_rebuild_decision_required")
        self.assertEqual(self.row()["progress"]["stageTotal"], len(stage_plan.UPDATE_PLANS["update-mod"]))
        self.assertEqual(stage_plan.workflow(self.row()), "update-mod")
        self.assertEqual(self.apk.read_bytes(), b"selected old APK fixture")

    def test_saved_false_fallback_does_not_reserve_full_recovery_space_before_current_inspect(self):
        saved = self.stage_context(); engine = wizard.Engine(self.store)
        result = {'workspaceRoot': str(self.store.root), 'freeBytes': 50, 'requiredFreeBytes': 1}
        with patch.object(qualification, 'qualify', return_value=result) as capacity:
            for stage in ('tools', 'inspect'):
                engine.qualify(saved, stage=stage)
                self.assertEqual(capacity.call_args.kwargs['mode'], 'update-mod')
            engine.qualify(saved, stage='build')
            self.assertEqual(capacity.call_args.kwargs['mode'], 'update-mod')


def provision_environment():
    return wizard.provision


if __name__ == "__main__": unittest.main()
