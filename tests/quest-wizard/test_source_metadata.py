"""Displayed mod identity belongs to the selected source and saved session."""
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import discovery
import server
import state
import wizard


class SourceMetadataTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
    def tearDown(self): self.temp.cleanup()
    def source(self, name, build, commit=None):
        root = self.root / name
        project = root / "src/GloomhavenVR"
        (project / "Net").mkdir(parents=True)
        (project / "GloomhavenVR.csproj").write_text("<Project><PropertyGroup><Version>1.1.0</Version></PropertyGroup></Project>")
        (project / "Net/NetProtocol.cs").write_text("public const ushort ModBuild = " + str(build) + ";")
        if commit: state.atomic_json(root / "quest-builder-release.json", {"schema": 1, "sourceCommit": commit})
        return root
    def test_shipped_source_identity_ignores_installed_mod_dll(self):
        source = self.source("release", 627, "a" * 40)
        installed = self.root / "Game/BepInEx/plugins/GloomhavenVR.dll"
        installed.parent.mkdir(parents=True); installed.write_bytes(b"different installed mod build")
        result = discovery.mod_source(source)
        self.assertEqual(result, {"kind": "bundled-release", "modVersion": "1.1.0",
                                  "modBuild": 627, "sourceCommit": "a" * 40})
    def test_resolved_commit_prevents_git_call_and_invalid_commit_is_not_displayed(self):
        source = self.source("checkout", 630)
        with mock.patch.object(discovery.subprocess, "run", side_effect=AssertionError("unexpected git lookup")):
            self.assertEqual(discovery.mod_source(source, "b" * 40)["sourceCommit"], "b" * 40)
            self.assertIsNone(discovery.mod_source(source, "not-a-commit")["sourceCommit"])
    def test_missing_or_oversized_metadata_does_not_invent_version(self):
        source = self.source("large", 627)
        (source / "src/GloomhavenVR/GloomhavenVR.csproj").write_bytes(b"x" * 1048577)
        (source / "src/GloomhavenVR/Net/NetProtocol.cs").unlink()
        result = discovery.mod_source(source)
        self.assertIsNone(result["modVersion"]); self.assertIsNone(result["modBuild"])
        self.assertIsNone(result["sourceCommit"])
    def test_saved_session_uses_its_resolved_source_after_launch_zip_update(self):
        launch = self.source("new-launch-release", 629, "c" * 40)
        selected = self.source("prior-session-release", 627, "d" * 40)
        store = state.Store(self.root / "owned")
        saved = store.create(wizard.choices({"gameRoot": str(self.root / "Game")}))
        saved["completed"] = {"source": {"details": {"sourceRoot": str(selected), "commit": "d" * 40}}}
        with mock.patch.object(server, "REPO", launch):
            http = server.LocalServer(store, ROOT / "tools/quest-wizard-ui")
            try:
                self.assertEqual(http.visible_state(saved["session"], saved)["modSource"]["modBuild"], 627)
                with mock.patch.object(discovery, "mod_source", side_effect=AssertionError("repeated identity lookup")):
                    self.assertEqual(http.visible_state(saved["session"], saved)["modSource"]["sourceCommit"], "d" * 40)
            finally: http.close_owned()


if __name__ == "__main__": unittest.main()
