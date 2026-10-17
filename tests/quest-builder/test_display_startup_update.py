"""The managed pre-display callback keeps the existing native plugin and banks."""
import copy
import hashlib
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage
import update_driver as driver


class DisplayStartupUpdateTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.source = Path(self.temporary.name)
        self.path = self.source / driver._DISPLAY_FEATURE
        self.path.parent.mkdir(parents=True)
        current = (ROOT / driver._DISPLAY_FEATURE).read_bytes()
        self.baseline = current.replace(driver._DISPLAY_STARTUP_HOOK, b"", 1)
        self.assertEqual(len(self.baseline), driver._DISPLAY_FEATURE_BASE["size"])
        self.assertEqual(hashlib.sha256(self.baseline).hexdigest(), driver._DISPLAY_FEATURE_BASE["sha256"])
        anchor = b"        protected override bool OnInstanceCreate(ulong instance)"
        self.updated = self.baseline.replace(anchor, driver._DISPLAY_STARTUP_HOOK + anchor)
        self.previous = [copy.deepcopy(driver._DISPLAY_FEATURE_BASE)]
        self.builder = SimpleNamespace(_ordinary_owned=storage._ordinary_owned, BuildError=storage.BuildError)
        self.write(self.updated)

    def write(self, body):
        self.path.write_bytes(body)
        self.current = [storage.record_file(self.path, driver._DISPLAY_FEATURE)]

    def proof(self):
        return driver._managed_display_startup(self.builder, self.source, self.current, self.previous)

    def test_reviewed_callback_keeps_plugin_and_payload_but_remains_compiled_code(self):
        self.assertEqual(self.proof(), {driver._DISPLAY_FEATURE})
        self.assertTrue(driver.rebuild_reasons(self.current, self.previous))
        self.assertEqual(driver.rebuild_reasons(self.current, self.previous, managed_startup=self.proof()), [])
        self.assertNotEqual(driver._code_rows(self.current), driver._code_rows(self.previous))
        self.assertEqual(driver._payload_rows(self.current), [])

    def test_changed_native_library_declaration_never_uses_managed_exception(self):
        self.write(self.updated.replace(b'const string Library = "ghvr_quest_passthrough";', b'const string Library = "other";'))
        self.assertEqual(self.proof(), set())
        self.assertTrue(driver.rebuild_reasons(self.current, self.previous, managed_startup=self.proof()))

    def test_changed_import_signature_argument_or_native_lifecycle_remains_unsupported(self):
        for before, after in ((b"ghvr_quest_enable(int enabled)", b"ghvr_quest_enable(long enabled)"),
                              (b"ghvr_quest_session(session)", b"ghvr_quest_session(0)"),
                              (b"ghvr_quest_destroy_session();", b"ghvr_quest_destroy_instance();")):
            with self.subTest(before=before):
                self.assertIn(before, self.updated)
                self.write(self.updated.replace(before, after))
                self.assertEqual(self.proof(), set())

    def test_wrong_callback_or_duplicate_callback_is_not_admitted(self):
        for body in (self.updated.replace(b"PrepareDisplayBeforeStart();", b"OtherNativeSetup();"),
                     self.updated + driver._DISPLAY_STARTUP_HOOK):
            self.write(body)
            self.assertEqual(self.proof(), set())

    def test_unknown_missing_or_duplicated_historical_source_cannot_qualify(self):
        for rows in ([], self.previous * 2, [{**self.previous[0], "sha256": "f" * 64}],
                     [{**self.previous[0], "size": self.previous[0]["size"] + 1}]):
            self.previous = rows
            self.assertEqual(self.proof(), set())

    def test_current_source_mutation_is_an_explicit_provenance_failure(self):
        self.path.write_bytes(self.updated + b"// concurrent change\n")
        with self.assertRaisesRegex(storage.BuildError, "source changed"):
            self.proof()

    def test_native_program_edits_still_require_rebuild_after_callback_qualification(self):
        before = {"path": "tools/quest-native/passthrough.cpp", "size": 10, "sha256": "a" * 64}
        after = {**before, "sha256": "b" * 64}
        reasons = driver.rebuild_reasons([*self.current, after], [*self.previous, before], managed_startup=self.proof())
        self.assertEqual(len(reasons), 1)
        self.assertIn("passthrough.cpp", reasons[0])
        self.assertNotIn("QuestPassthroughFeature.cs", reasons[0])

    def test_same_completed_source_needs_no_compatibility_exception(self):
        self.previous = copy.deepcopy(self.current)
        self.assertEqual(self.proof(), set())
        self.assertEqual(driver.rebuild_reasons(self.current, self.previous), [])


if __name__ == "__main__":
    unittest.main()
