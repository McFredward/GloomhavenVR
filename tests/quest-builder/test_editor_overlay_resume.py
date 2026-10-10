"""Crash-safe exact Editor updates retain real completed27-owner preparation."""
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import editor_overlay as overlay
import preparation_identity as identity
import preparation_metadata as metadata
import recovery_resume
import storage

spec = importlib.util.spec_from_file_location("editor_completed_fixture", ROOT / "tests/quest-builder/test_completed_preparation_resume.py")
completed = importlib.util.module_from_spec(spec); spec.loader.exec_module(completed)


def record(name, raw):
    return {"path": name, "size": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}


class EditorOverlayTests(unittest.TestCase):
    def setUp(self):
        self.fixture = completed.CompletedPreparationTests("test_runtime_update_keeps_all_twenty_seven_asset_steps_and_later_owners")
        sources = {name: (ROOT / name).read_bytes() for name in overlay.TARGETS}
        self.metas = {}
        for index, name in enumerate(overlay.TARGETS, 1):
            raw = ("fileFormatVersion: 2\nguid: " + format(index, "032x") + "\n").encode()
            sources[name + ".meta"] = raw
            self.metas[name[len(overlay.PREFIX):] + ".meta"] = raw
        self.fixture.editor_sources = sources
        self.fixture.setUp(); self.addCleanup(self.fixture.doCleanups)
        self.f, self.project, self.journal = self.fixture.f, self.fixture.project, self.fixture.journal
        self.pairs = {}
        self.original = {}
        for name in overlay.TARGETS:
            before = sources[name]
            after = before + b"\n// Exact fixture late Editor repair; no asset producer change.\n"
            self.pairs[name] = (record(name, before), record(name, after))
            self.original[name] = before
            self.f.write(self.f.source, name, after)
        self.f.stack.enter_context(patch.object(overlay, "REVIEWED", self.pairs))
        # The real completed-owner selector reads this exact reviewed source
        # profile. Unknown edits and new earlier consumers remain unaliased.
        self.current = self.fixture.updated()

    def assert_retained(self):
        self.fixture.assert_retained(self.current)
        value = json.loads(self.journal.read_text())
        latest = metadata._latest(value)
        for name, (_, after) in self.pairs.items():
            relative = name[len(overlay.PREFIX):]
            self.assertEqual((self.project / relative).read_bytes(), (self.f.source / name).read_bytes())
            self.assertEqual(latest[relative][1], {**after, "path": relative})
            self.assertEqual(value["steps"][latest[relative][0]]["name"], "base-project")
            self.assertEqual((self.project / (relative + ".meta")).read_bytes(), self.metas[relative + ".meta"])
        self.assertFalse((self.journal.parent / "metadata-refresh.json").exists())

    def test_exact_reviewed_editor_updates_keep_all_twenty_seven_owners_and_library(self):
        self.assertEqual(identity.rebind_key(self.f.output, self.project, self.current, self.f.source,
                target="game", recipe=builder.RECIPE, recovery=recovery_resume), self.fixture.before["inputKey"])
        with patch.object(builder.prepare_resume, "copy_changed", side_effect=AssertionError("No retained producer can recopy its project")):
            self.f.run_prepare()
        self.assert_retained()
        before = {name: (self.project / name[len(overlay.PREFIX):]).stat().st_mtime_ns for name in overlay.TARGETS}
        self.f.run_prepare()
        self.assert_retained()
        self.assertEqual(before, {name: (self.project / name[len(overlay.PREFIX):]).stat().st_mtime_ns for name in overlay.TARGETS})

    def test_counted_loading_consumer_repairs_only_its_script_with_all_asset_owners_retained(self):
        target = overlay.PREVIOUS_COUNTED_LOADING_SCRIPT["path"]
        for name in overlay.TARGETS:
            if name != target:
                before = self.original[name]
                self.f.write(self.f.source, name, before)
                self.pairs[name] = (record(name, before), record(name, before))
        previous = record(target, self.original[target])
        with patch.object(overlay, "PREVIOUS_COUNTED_LOADING_SCRIPT", previous):
            self.current = self.fixture.updated()
            self.assertEqual([row[0] for row in overlay.changes(self.fixture.before, self.current)], [target])
            actual = overlay.publish
            published = []
            def publish(path, raw):
                published.append(Path(path))
                actual(path, raw)
            with patch.object(overlay, "publish", publish), patch.object(
                    builder.prepare_resume, "copy_changed", side_effect=AssertionError("No completed asset producer repeat")):
                self.f.run_prepare()
            self.assert_retained()
            self.assertEqual(published, [self.project / target[len(overlay.PREFIX):]])
            self.f.run_prepare()
            self.assert_retained()
            self.assertEqual(len(published), 1)

    def test_every_script_publication_cut_replays_without_repeated_asset_work(self):
        actual = overlay.publish
        for cut in range(len(overlay.TARGETS)):
            with self.subTest(cut=cut):
                # Each cut gets a fresh independent completed preparation.
                test = EditorOverlayTests("test_exact_reviewed_editor_updates_keep_all_twenty_seven_owners_and_library")
                test.setUp()
                try:
                    target = test.project / overlay.TARGETS[cut][len(overlay.PREFIX):]
                    def killed(path, raw):
                        actual(path, raw)
                        if Path(path) == target: raise RuntimeError("kill after Editor publication")
                    with patch.object(overlay, "publish", killed):
                        with self.assertRaisesRegex(RuntimeError, "kill after Editor"):
                            test.f.run_prepare()
                    self.assertEqual(json.loads(test.journal.read_text())["inputKey"], test.fixture.before["inputKey"])
                    test.f.run_prepare(); test.assert_retained()
                finally: test.doCleanups()

    def test_cuts_before_first_file_and_after_journal_replay_the_bounded_transaction(self):
        actual = metadata.write_json
        transaction = self.journal.parent / "metadata-refresh.json"
        def killed(path, value):
            actual(path, value)
            if Path(path) == transaction: raise RuntimeError("kill after durable plan")
        with patch.object(metadata, "write_json", killed):
            with self.assertRaisesRegex(RuntimeError, "durable plan"): self.f.run_prepare()
        for name, before in self.original.items():
            self.assertEqual((self.project / name[len(overlay.PREFIX):]).read_bytes(), before)
        def killed_journal(path, value):
            actual(path, value)
            if Path(path) == self.journal: raise RuntimeError("kill after refreshed journal")
        with patch.object(metadata, "write_json", killed_journal):
            with self.assertRaisesRegex(RuntimeError, "refreshed journal"): self.f.run_prepare()
        self.assertEqual(json.loads(self.journal.read_text())["inputKey"], self.current["inputKey"])
        actual_digest = builder.prepare_resume.digest
        permitted = set(metadata.PATHS) | {name[len(overlay.PREFIX):] for name in overlay.TARGETS}
        def only_bounded(path):
            relative = Path(path).relative_to(self.project).as_posix()
            if relative not in permitted: self.fail("A published warm owner must not reopen original payloads: " + relative)
            return actual_digest(path)
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(builder.prepare_resume, "digest", only_bounded):
            self.f.run_prepare()
        self.assert_retained()

    def test_changed_last_script_is_reconstructed_before_complete_reviewed_editor_update(self):
        last = self.project / overlay.TARGETS[-1][len(overlay.PREFIX):]
        last.write_bytes(b"unknown project Editor bytes")
        previous = self.f.output / "inputs/mod" / self.fixture.before["mod"]["key"] / overlay.TARGETS[-1]
        immutable = previous.read_bytes()
        actual = overlay.publish
        published = []
        def repaired_before_overlay(path, raw):
            if not published:
                self.assertEqual(last.read_bytes(), immutable)
            published.append(Path(path))
            actual(path, raw)
        with patch.object(overlay, "publish", repaired_before_overlay), patch.object(
                builder.prepare_resume, "copy_changed", side_effect=AssertionError("No complete project repeat")):
            self.f.run_prepare()
        self.assert_retained()
        self.assertEqual(previous.read_bytes(), immutable)
        self.assertEqual(json.loads(self.journal.read_text())["fileRepairs"]["count"], 1)
        self.assertEqual(len(published), len(overlay.TARGETS))

    def test_unknown_editor_source_game_template_or_earlier_consumer_refuses_reuse(self):
        baseline = copy.deepcopy(self.current)
        for change in ("editor", "game", "template", "consumer"):
            with self.subTest(change=change):
                current = copy.deepcopy(baseline)
                if change == "game": current["game"]["files"][0]["sha256"] = "f" * 64
                else:
                    name = {"editor": overlay.TARGETS[-1], "template": "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/Probe.cs", "consumer": identity.BUILDER}[change]
                    row = next(row for row in current["mod"]["files"] if row["path"] == name)
                    if change == "consumer":
                        original = (self.f.source / name).read_bytes()
                        changed = original.replace(b"    def generate_files(resume):", b"    def generate_files(resume):\n        (source / 'unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestBuild.cs').read_bytes()")
                        self.assertNotEqual(original, changed)
                        (self.f.source / name).write_bytes(changed)
                        row.update(record(name, changed))
                    else: row["sha256"] = "f" * 64
                current["mod"]["key"] = storage.value_hash({"files": current["mod"]["files"]})
                current["game"]["key"] = storage.value_hash({"files": current["game"]["files"]})
                current.pop("inputKey"); current["inputKey"] = storage.value_hash(current)
                storage.write_json(self.f.output / "manifests" / (current["inputKey"] + ".json"), current)
                self.assertIsNone(identity.rebind_key(self.f.output, self.project, current, self.f.source,
                        target="game", recipe=builder.RECIPE, recovery=recovery_resume))
                self.assertEqual(json.loads(self.journal.read_text()), self.fixture.value)
                if change == "consumer": (self.f.source / identity.BUILDER).write_bytes(original)

    def test_corrupt_script_payload_missing_source_or_changed_target_never_replays(self):
        actual = metadata.write_json
        transaction = self.journal.parent / "metadata-refresh.json"
        def killed(path, value):
            actual(path, value)
            if Path(path) == transaction: raise RuntimeError("fixture cut")
        with patch.object(metadata, "write_json", killed):
            with self.assertRaisesRegex(RuntimeError, "fixture cut"): self.f.run_prepare()
        plan = json.loads(transaction.read_text())
        snapshot = {name: (self.project / name).read_bytes() for name in metadata.PATHS}
        for change in ("payload", "path", "owner", "extra", "new-input"):
            with self.subTest(change=change):
                bad = copy.deepcopy(plan)
                row = bad["files"][-1]
                if change == "payload": row["replacement"] = "aW52YWxpZA=="
                elif change == "path": row["path"] = "Library/forbidden"
                elif change == "owner": row["step"] = 26
                elif change == "extra": bad["files"].append(copy.deepcopy(row))
                else: bad["toInputKey"] = "f" * 64
                storage.write_json(transaction, bad)
                with self.assertRaisesRegex(storage.BuildError, "transaction|contract|write set"):
                    self.f.run_prepare()
                self.assertEqual(snapshot, {name: (self.project / name).read_bytes() for name in metadata.PATHS})
                self.assertEqual(json.loads(self.journal.read_text()), self.fixture.value)
        storage.write_json(transaction, plan)
        self.f.run_prepare(); self.assert_retained()


if __name__ == "__main__": unittest.main()
