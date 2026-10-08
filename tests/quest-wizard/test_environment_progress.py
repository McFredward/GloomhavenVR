"""Actual environment item counters own half of one open preparation checkpoint."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import state
import wizard


class EnvironmentProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / "owned")
        self.saved = self.store.create(wizard.choices({"gameRoot": str(self.root / "Game")}))
        self.session = self.saved["session"]
        patcher = mock.patch("state.PROGRESS_INTERVAL", 0)
        patcher.start(); self.addCleanup(patcher.stop)
        self.store.begin_stage(self.session, "build", "fixture-input")

    def row(self):
        return self.store.load(self.session)["stages"][5]

    def open_bank(self, done=1, total=4):
        self.store.operation(self.session, "build", "mod-banks")
        self.store.progress(self.session, "build", "prepare-substage:mod-resource-banks", done, total,
                            "checkpoints", operation="mod-banks", status="start")

    def items(self, name, done, total, **options):
        return self.store.progress(self.session, "build", "prepare-items:" + name, done, total, "items", **options)

    def fraction(self):
        return self.row()["progressPlan"].get("liveFractions", {}).get("mod-banks", 0.)

    def test_bundles_and_meshes_continuously_advance_only_half_of_the_open_checkpoint(self):
        self.open_bank()
        previous = self.row()["progress"]["stagePercent"]
        for name, count, expected in (("environment-bundles", 40, .275),
                                      ("environment-bundles", 100, .3125),
                                      ("environment-meshes", 0, .3125),
                                      ("environment-meshes", 40, .3375),
                                      ("environment-meshes", 100, .375)):
            with self.subTest(name=name, count=count):
                self.items(name, count, 100)
                row = self.row()
                self.assertAlmostEqual(self.fraction(), expected)
                self.assertGreaterEqual(row["progress"]["stagePercent"], previous)
                previous = row["progress"]["stagePercent"]
                self.assertEqual(row["progress"]["buildOverview"]["active"], "mod-banks")
                self.assertEqual(row["progress"]["activeWork"]["done"], 1)
                self.assertEqual(row["progress"]["activeWork"]["total"], 4)
                self.assertAlmostEqual(row["progress"]["activeWork"]["percent"], expected * 100)
                self.assertNotIn("mod-banks", row["progressPlan"]["completed"])
                self.assertTrue(row["progressPlan"]["preparationScopes"]["mod-banks"]["open"])
        self.assertEqual(self.row()["progressPlan"]["preparationScopes"]["mod-banks"]["itemFraction"], .5)
        self.assertEqual(self.row()["progress"]["done"], 100)
        self.assertEqual(self.row()["progress"]["total"], 100)

    def test_geometry_success_or_reuse_never_closes_checkpoint_or_parent(self):
        for status in ("complete", "reuse"):
            with self.subTest(status=status):
                self.open_bank()
                self.items("environment-bundles", 2, 2, status=status)
                self.items("environment-meshes", 10, 10, status=status)
                row = self.row()
                self.assertEqual(self.fraction(), .375)
                self.assertTrue(row["progressPlan"]["preparationScopes"]["mod-banks"]["open"])
                self.assertNotIn("mod-banks", row["progressPlan"]["completed"])
                self.assertEqual(row["progress"]["buildOverview"]["groups"][2]["operations"][-2]["status"], "running")

    def test_qualified_checkpoint_close_advances_its_remaining_share_and_rejects_late_items(self):
        self.open_bank()
        self.items("environment-meshes", 100, 100)
        geometry = self.row()["progress"]["stagePercent"]
        self.store.progress(self.session, "build", "prepare-substage:mod-resource-banks", 2, 4,
                            "checkpoints", operation="mod-banks", status="complete")
        closed = self.row()
        self.assertEqual(self.fraction(), .5)
        self.assertGreater(closed["progress"]["stagePercent"], geometry)
        self.assertFalse(closed["progressPlan"]["preparationScopes"]["mod-banks"]["open"])
        self.assertNotIn("mod-banks", closed["progressPlan"]["completed"])
        for name in ("environment-bundles", "environment-meshes"):
            self.items(name, 100, 100, operation="mod-banks", status="complete")
            self.assertEqual(self.row()["progress"]["stagePercent"], closed["progress"]["stagePercent"])
            self.assertEqual(self.fraction(), .5)
        self.store.operation(self.session, "build", "mod-banks", complete=True)
        self.assertIn("mod-banks", self.row()["progressPlan"]["completed"])

    def test_unopened_and_wrong_checkpoint_or_operation_counters_cannot_add_credit(self):
        self.store.operation(self.session, "build", "mod-banks")
        before = self.row()["progress"]["stagePercent"]
        for name in ("environment-bundles", "environment-meshes"):
            self.items(name, 100, 100, operation="mod-banks", status="reuse")
            self.assertEqual(self.row()["progress"]["stagePercent"], before)
        self.store.progress(self.session, "build", "prepare-substage:other-resource-bank", 0, 4,
                            "checkpoints", operation="mod-banks", status="start")
        for name in ("environment-bundles", "environment-meshes"):
            self.items(name, 100, 100)
            self.assertEqual(self.row()["progress"]["stagePercent"], before)
        self.open_bank()
        before = self.row()["progress"]["stagePercent"]
        self.items("environment-meshes", 100, 100, operation="textures")
        self.assertEqual(self.row()["progress"]["stagePercent"], before)
        self.assertEqual(self.row()["progress"]["buildOverview"]["active"], "mod-banks")
        self.assertEqual(self.fraction(), .25)

    def test_an_old_open_scope_cannot_reopen_after_the_actual_owner_moves_on(self):
        self.open_bank()
        self.items("environment-bundles", 25, 100)
        self.store.operation(self.session, "build", "player")
        before = self.row()["progress"]["stagePercent"]
        for name in ("environment-bundles", "environment-meshes"):
            self.items(name, 100, 100, operation="mod-banks")
            row = self.row()
            self.assertEqual(row["progress"]["stagePercent"], before)
            self.assertEqual(row["progress"]["buildOverview"]["active"], "player")
            self.assertNotIn("player", row["progressPlan"]["completed"])

    def test_actual_failed_runner_retry_has_fresh_environment_scope_and_current_item_counts(self):
        calls = []
        def action(name):
            def run(saved, supervisor):
                calls.append(name)
                if name == "build":
                    attempt = saved["stages"][5]["attempts"]
                    if attempt > 1:
                        before = self.row()["progress"]["stagePercent"]
                        self.assertEqual(before, previous)
                        self.assertEqual(self.row()["progressPlan"]["preparationScopes"], {})
                        self.assertIsNone(self.row()["progress"]["buildOverview"]["active"])
                        self.items("environment-meshes", 100, 100, operation="mod-banks", status="reuse")
                        self.assertEqual(self.row()["progress"]["stagePercent"], before)
                        self.assertIsNone(self.row()["progress"]["buildOverview"]["active"])
                    self.open_bank()
                    self.items("environment-bundles", 100 if attempt == 1 else 20, 100)
                    if attempt == 1:
                        self.items("environment-meshes", 100, 100)
                        raise state.WizardError("build_tool_failed", "Fixture compiler failure after complete geometry")
                    row = self.row()
                    self.assertEqual(row["progressPlan"]["fractions"]["mod-banks"], .375)
                    self.assertEqual(self.fraction(), .2625)
                    self.assertEqual(row["progressPlan"]["preparationScopes"]["mod-banks"]["items"], {"done": 20, "total": 100})
                    self.assertEqual(row["progress"]["done"], 20)
                    self.assertEqual(row["progress"]["activeWork"]["percent"], 26.25)
                    self.assertEqual(row["progress"]["stagePercent"], previous)
                output = self.store.session_dir(self.session) / (name + ".txt")
                output.write_text(name)
                return [output], {}
            return run
        actions = {name: action(name) for name in state.STAGES}
        self.assertEqual(wizard.Engine(self.store, actions=actions).run(self.session)["status"], "failed")
        previous = self.row()["progress"]["stagePercent"]
        self.store = state.Store(self.store.root)
        self.assertEqual(wizard.Engine(self.store, actions=actions).run(self.session)["status"], "complete")
        self.assertTrue(all(calls.count(name) == 1 for name in state.STAGES[:5]))
        self.assertEqual(calls.count("build"), 2)
        self.assertGreater(previous, 0)


if __name__ == "__main__": unittest.main()
