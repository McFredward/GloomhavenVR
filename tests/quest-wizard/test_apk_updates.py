"""Actual mode/progress/argument seams, without Unity, downloads or headset claims."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import provision
import stage_plan
import state
import wizard


class UpdateWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.store = state.Store(self.root / "work")
        self.apk = self.root / "base.apk"; self.apk.write_bytes(b"basis fixture bytes")
        self.saved = self.store.create(wizard.choices({"mode": "update-profile", "gameRoot": str(self.root / "PC"),
                                                     "baseApk": str(self.apk), "acceptUnityTerms": False}))
        self.engine = wizard.Engine(self.store)

    def test_same_apk_path_with_new_bytes_invalidates_inspect(self):
        before = self.engine.key(self.saved, "inspect")
        self.apk.write_bytes(b"replacement basis at the same path")
        self.assertNotEqual(before, self.engine.key(self.saved, "inspect"))

    def test_profile_arguments_have_no_unity_or_dotnet_dependencies(self):
        self.saved["completed"] = {
            "source": {"details": {"sourceRoot": str(ROOT)}}, "tools": {"details": {"apkTools": {}}},
            "profile": {"details": {"profilePath": str(self.root / "profile.json"), "steamLogo": str(self.root / "logo.png")}}}
        args = self.engine.arguments(self.saved, "build")
        self.assertIn("update-profile", args); self.assertIn("--apk-tools-json", args)
        self.assertNotIn("--unity-editor", args); self.assertNotIn("--dotnet", args)
        self.assertIn("--game-root", args); self.assertIn("--base-apk", args)

    def test_profile_source_and_unity_markers_require_no_compile_or_terms(self):
        with self.store.active(self.saved), patch.object(provision, "source_checkout", side_effect=AssertionError("No source compiler")):
            source, source_details = self.engine.stage_source(self.saved, Mock())
            paths, unity_details = self.engine.stage_unity(self.saved, Mock())
        self.assertFalse(unity_details["required"])
        self.assertEqual(source_details["sourceRoot"], str(ROOT))
        self.assertTrue(all(path.is_file() for path in [*source, *paths]))

    def test_signing_downloads_have_one_advancing_overall_tools_plan(self):
        with self.store.active(self.saved):
            self.store.begin_stage(self.saved["session"], "tools", "fixture")
            row = next(row for row in self.saved["stages"] if row["id"] == "tools")
            self.store.operation(self.saved["session"], "tools", "qualify", complete=True)
            prior = row["progress"]["stagePercent"]
            for tool in ("apkJdk", "apkBuildTools"):
                self.store.progress(self.saved["session"], "tools", "tool-download-" + tool, 1, 2, "bytes")
                self.assertGreater(row["progress"]["stagePercent"], prior)
                self.store.progress(self.saved["session"], "tools", "tool-download-" + tool, 2, 2, "bytes")
                self.store.progress(self.saved["session"], "tools", "tool-extract-" + tool, 2, 2, "files")
                self.store.operation(self.saved["session"], "tools", "verify-" + tool, complete=True)
                prior = row["progress"]["stagePercent"]
            self.assertGreater(prior, 80); self.assertLess(prior, 100)
            self.store.operation(self.saved["session"], "tools", "output-verify", complete=True)
            self.assertEqual(row["progress"]["stagePercent"], 99.9)


if __name__ == "__main__": unittest.main()
