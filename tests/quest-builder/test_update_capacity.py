"""Actual shared capacity recipe, selected output roots and private imports."""
import json
from pathlib import Path
import shutil
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage
import update_capacity


class UpdateCapacityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.output = self.root / "arbitrary-output-name"; self.output.mkdir()
        self.game = self.root / "owned-PC"; self.game.mkdir()
        for name in ("one", "two", "three"):
            (self.game / name).write_bytes((name * 100).encode())
        self.helper = SimpleNamespace(write_json=storage.write_json, _ordinary_owned=storage._ordinary_owned,
            value_hash=storage.value_hash, BuildError=storage.BuildError, game_data=lambda path: Path(path),
            import_workspace=SimpleNamespace(workspace_key=lambda *args: "f" * 64))

    def module(self):
        return update_capacity.qualification_module(self.helper)

    def test_builder_and_wizard_use_identical_reserve_without_global_aliases(self):
        modules = dict(sys.modules); paths = list(sys.path)
        module = update_capacity.qualification_module(self.helper)
        self.assertEqual(paths, sys.path)
        self.assertTrue(all(sys.modules.get(name) is value for name, value in modules.items()))
        self.assertEqual(module.space_estimate(self.output, self.game, ROOT, helper=self.helper, build_output=self.output)["additionalEstimatedBytes"],
                         20 * module.GIB + sum(path.stat().st_size for path in self.game.iterdir()) * 8)
        with patch.object(module.shutil, "disk_usage", return_value=shutil._ntuple_diskusage(400 * module.GIB, 0, 400 * module.GIB)), \
             patch.object(update_capacity, "qualification_module", return_value=module):
            result = update_capacity.require_incremental_space(self.helper, self.output, self.game, ROOT)
        self.assertEqual(result["requiredFreeBytes"], result["spaceEstimate"]["additionalEstimatedBytes"])
        receipt = json.loads((self.output / "logs/update-rebuild-capacity.json").read_text())
        self.assertEqual(receipt["status"], "qualified")

    def test_actual_arbitrary_output_cache_credit_is_used_without_a_full_hash(self):
        module = self.module(); rows = storage.inventory(self.game); key = storage.value_hash({"files": rows})
        manifest = "manifests/" + "a" * 64 + ".json"
        storage.write_json(self.output / "latest-input.json", {"sourceGameRoot": str(self.game), "manifest": manifest})
        storage.write_json(self.output / manifest, {"game": {"key": key, "files": rows}})
        snapshot = self.output / "inputs/game" / key; snapshot.mkdir(parents=True)
        (snapshot / "retained-fixture").write_bytes(b"retained" * 2000)
        with patch.object(storage, "digest", side_effect=AssertionError("Capacity never hashes retained game caches")), \
             patch.object(module.shutil, "disk_usage", return_value=shutil._ntuple_diskusage(400 * module.GIB, 0, 400 * module.GIB)), \
             patch.object(update_capacity, "qualification_module", return_value=module):
            result = update_capacity.require_incremental_space(self.helper, self.output, self.game, ROOT)
        estimate = result["spaceEstimate"]
        self.assertTrue(estimate["cacheReuseCandidate"])
        self.assertGreater(estimate["cacheCreditBytes"], 0)
        self.assertLess(estimate["additionalEstimatedBytes"], estimate["freshEstimatedBytes"])

    def test_low_space_preserves_workspace_and_emits_actual_capacity_numbers(self):
        module = self.module(); retained = self.output / "retained.apk"; retained.write_bytes(b"verified older output")
        with patch.object(module.shutil, "disk_usage", return_value=shutil._ntuple_diskusage(400 * module.GIB, 398 * module.GIB, 2 * module.GIB)), \
             patch.object(update_capacity, "qualification_module", return_value=module):
            with self.assertRaisesRegex(storage.BuildError, "Not enough free space"):
                update_capacity.require_incremental_space(self.helper, self.output, self.game, ROOT)
        self.assertEqual(retained.read_bytes(), b"verified older output")
        receipt = json.loads((self.output / "logs/update-rebuild-capacity.json").read_text())
        self.assertEqual(receipt["status"], "workspace_build_space_low")
        self.assertEqual(receipt["freeBytes"], 2 * module.GIB)
        self.assertGreater(receipt["requiredBytes"], 20 * module.GIB)
        self.assertTrue(receipt["completedWorkRetained"])

    def test_incomplete_bounded_census_never_claims_a_smaller_safe_reserve(self):
        module = self.module(); module.MAX_SCAN_FILES = 1
        with patch.object(module.shutil, "disk_usage", return_value=shutil._ntuple_diskusage(400 * module.GIB, 0, 400 * module.GIB)), \
             patch.object(update_capacity, "qualification_module", return_value=module):
            with self.assertRaisesRegex(storage.BuildError, "bounded game-file scan"):
                update_capacity.require_incremental_space(self.helper, self.output, self.game, ROOT)
        receipt = json.loads((self.output / "logs/update-rebuild-capacity.json").read_text())
        self.assertEqual(receipt["status"], "workspace_space_estimate_incomplete")


if __name__ == "__main__": unittest.main()
