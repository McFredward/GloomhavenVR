"""Real preparation undo/journal transactions for one late Story owner.

The original bank/native-prefix bytes and Unity Library are represented by tiny
files. The production Preparation API performs every reuse, backup, rollback,
contract observation and atomic journal publication; no conversion tool runs.
"""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import storage

resume = builder.prepare_resume


class TailRefreshTests(unittest.TestCase):
    OUTPUTS = ("Assets/StreamingAssets/quest-mod-content.zip", "Assets/Quest/Resources/quest-mod-content.json",
               "Assets/Quest/Resources/quest-mod-bundles.json", "QuestStartupEvidence/menu-story-content.json")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.project = self.root / "projects/fixture"
        self.calls = {"base": 0, "settings": 0, "story": 0}
        self.library = self.put("Library/retained-native-import", b"unchanged expensive original import")

    def put(self, name, body):
        path = self.project / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(body)
        return path

    def journal(self):
        return resume.Preparation(self.root, self.project, input_key="a" * 64, target="game", recipe=1)

    def story(self, generation, *, fail=False):
        self.calls["story"] += 1
        for index, name in enumerate(self.OUTPUTS):
            self.put(name, ("native banks plus supplied Story " + str(generation) + " " + name).encode())
            if index == 0 and fail:
                raise RuntimeError("interrupted independent Story delivery")

    def pipeline(self, journal, generation=None, *, refresh=False, fail=False):
        def base():
            self.calls["base"] += 1
            self.put("Assets/original-native-asset", b"exact original native asset")
            for name in self.OUTPUTS[:3]: self.put(name, b"closed original bank prefix")
        with journal.operation("project-files", 1):
            journal.run("base-project", "project-files", base,
                        ["Assets/original-native-asset", *self.OUTPUTS[:3]])
        def settings():
            self.calls["settings"] += 1
            self.put("ProjectSettings/ProjectSettings.asset", b"retained accepted final settings")
        with journal.operation("preparation-contracts", 1 + (generation is not None)):
            journal.run("final-settings", "preparation-contracts", settings, ["ProjectSettings/ProjectSettings.asset"])
            if generation is not None:
                journal.run("menu-story-content", "preparation-contracts", lambda: self.story(generation, fail=fail),
                            self.OUTPUTS, mutations=self.OUTPUTS, refresh_tail=refresh)

    def seed(self, generation=None):
        journal = self.journal()
        try:
            self.pipeline(journal, generation)
            journal.finish()
            return copy.deepcopy(journal.value)
        finally: journal.close()

    def test_first_story_migration_appends_one_step_and_keeps_exact_accepted_prefix(self):
        old = self.seed()
        original = self.project / "Assets/original-native-asset"
        original_proof = original.read_bytes(), original.stat().st_mtime_ns
        journal = self.journal()
        try:
            self.pipeline(journal, 1, refresh=True)
            journal.finish()
            self.assertEqual(journal.value["steps"][:-1], old["steps"])
            self.assertEqual(journal.value["steps"][-1]["name"], "menu-story-content")
        finally: journal.close()
        self.assertEqual(self.calls, {"base": 1, "settings": 1, "story": 1})
        self.assertEqual((original.read_bytes(), original.stat().st_mtime_ns), original_proof)
        self.assertEqual(self.library.read_bytes(), b"unchanged expensive original import")

    def test_warm_identical_story_reuses_without_backup_action_or_journal_rewrite(self):
        self.seed(1)
        journal = self.journal()
        path = journal.journal
        before = path.read_bytes(), path.stat().st_mtime_ns
        try:
            with patch.object(journal, "_undo", side_effect=AssertionError("no unchanged archive backup")), \
                 patch.object(self, "story", side_effect=AssertionError("no unchanged Story writer")):
                self.pipeline(journal, 1)
                journal.finish()
        finally: journal.close()
        self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), before)

    def test_changed_story_replaces_its_tail_once_without_repeating_any_prefix(self):
        old = self.seed(1)
        journal = self.journal()
        try:
            self.pipeline(journal, 2, refresh=True)
            journal.finish()
            self.assertEqual(journal.value["steps"][:-1], old["steps"][:-1])
            self.assertEqual(len(journal.value["steps"]), len(old["steps"]))
            self.assertNotEqual(journal.value["steps"][-1], old["steps"][-1])
        finally: journal.close()
        self.assertEqual(self.calls, {"base": 1, "settings": 1, "story": 2})
        # A new constructor must qualify the replacement's actual bytes and its
        # latest ownership, while the original prefix still has its old hashes.
        retained = self.journal()
        try:
            self.pipeline(retained, 2)
            retained.finish()
        finally: retained.close()

    def test_interrupted_refresh_keeps_old_tail_and_undo_then_restores_before_retry(self):
        old = self.seed(1)
        before = {name: (self.project / name).read_bytes() for name in self.OUTPUTS}
        journal = self.journal()
        path = journal.journal
        try:
            with self.assertRaisesRegex(RuntimeError, "interrupted independent"):
                self.pipeline(journal, 2, refresh=True, fail=True)
            pending = json.loads(path.read_text())
            self.assertEqual(pending["steps"], old["steps"])
            self.assertEqual(pending["pending"]["name"], "menu-story-content")
            self.assertEqual(len(pending["pending"]["undo"]), len(self.OUTPUTS))
            with self.assertRaisesRegex(storage.BuildError, "Reopen preparation"):
                journal.run("menu-story-content", "preparation-contracts", lambda: self.story(2),
                            self.OUTPUTS, mutations=self.OUTPUTS, refresh_tail=True)
        finally: journal.close()
        recovered = self.journal()
        try:
            self.assertEqual({name: (self.project / name).read_bytes() for name in self.OUTPUTS}, before)
            self.assertIsNone(recovered.value["pending"])
            self.pipeline(recovered, 2, refresh=True)
            recovered.finish()
            self.assertEqual(recovered.value["steps"][:-1], old["steps"][:-1])
            self.assertEqual(len(recovered.value["steps"]), len(old["steps"]))
        finally: recovered.close()
        self.assertEqual(self.calls, {"base": 1, "settings": 1, "story": 3})

    def test_non_tail_refresh_rejects_before_action_or_mutation(self):
        self.seed(1)
        journal = self.journal()
        before = journal.journal.read_bytes()
        try:
            with self.assertRaisesRegex(storage.BuildError, "only the matching final"), journal.operation("project-files", 1):
                journal.run("base-project", "project-files", lambda: self.fail("never rerun original conversion"),
                            ["Assets/original-native-asset"], mutations=["Assets/original-native-asset"], refresh_tail=True)
            self.assertEqual(journal.journal.read_bytes(), before)
            self.assertFalse((journal.root / "undo-base-project").exists())
        finally: journal.close()

    def test_failed_journal_publication_retains_old_tail_in_memory_and_on_disk(self):
        old = self.seed(1)
        journal = self.journal()
        actual = resume.write_json
        def stopped(path, value):
            if Path(path) == journal.journal and value.get("pending") is None:
                raise RuntimeError("interrupted atomic tail publication")
            return actual(path, value)
        try:
            with patch.object(resume, "write_json", side_effect=stopped), \
                 self.assertRaisesRegex(RuntimeError, "atomic tail publication"):
                self.pipeline(journal, 2, refresh=True)
            self.assertEqual(journal.value["steps"], old["steps"])
            self.assertEqual(json.loads(journal.journal.read_text())["steps"], old["steps"])
            self.assertIsNotNone(journal.value["pending"])
            self.assertTrue((journal.root / "undo-menu-story-content").is_dir())
        finally: journal.close()
        recovered = self.journal()
        try:
            self.pipeline(recovered, 2, refresh=True)
            recovered.finish()
            self.assertEqual(recovered.value["steps"][:-1], old["steps"][:-1])
        finally: recovered.close()


if __name__ == "__main__":
    unittest.main()
