"""Actual qualification events retain input ownership after progress rotation."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import qualification
import state
import wizard


class StorageProvenanceTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.store = state.Store(self.root / "owned")
        self.saved = self.store.create(wizard.choices({"gameRoot": str(self.root / "PC")}))
        self.engine = wizard.Engine(self.store)

    def test_low_space_preserves_actual_game_key_after_event_ring_rotates(self):
        game_key = "c287a983b0fd58fb4f7bd31271ecf06e4fe0e1ccee064e6fb4437e32ac4ce60b"
        failure = state.WizardError("workspace_build_space_low", "Space needed", freeBytes=100,
                                    estimatedBytes=200, spaceEstimate={"cacheGameKey": game_key})
        before_key = self.saved["choicesKey"]
        with mock.patch.object(qualification, "qualify", side_effect=failure):
            with self.assertRaises(state.WizardError):
                self.engine.qualify(self.saved)
        for _ in range(130):
            self.store.record(self.saved["session"], "fixture_later_observation")
        restarted = state.Store(self.store.root).load(self.saved["session"])
        self.assertFalse(any(row["code"] == "space_estimate_warning" for row in restarted["events"]))
        self.assertEqual(restarted["storageProvenance"], {"schema": 1, "games": [{"gameKey": game_key}]})
        self.assertEqual(restarted["choicesKey"], before_key)

    def test_successful_qualification_retains_distinct_inputs_without_recipe_changes(self):
        expected = ["a" * 64, "b" * 64]
        for key in [expected[0], expected[0], expected[1]]:
            observed = {"workspaceRoot": str(self.store.root), "freeBytes": 200, "requiredFreeBytes": 100,
                        "spaceEstimate": {"cacheGameKey": key}}
            with mock.patch.object(qualification, "qualify", return_value=observed):
                self.engine.qualify(self.saved, stage="build")
        restarted = self.store.load(self.saved["session"])
        self.assertEqual([row["gameKey"] for row in restarted["storageProvenance"]["games"]], expected)
        self.assertEqual(restarted.get("completed", {}), self.saved.get("completed", {}))
        self.assertTrue(all("key" not in row for row in restarted["stages"]))


if __name__ == "__main__":
    unittest.main()
