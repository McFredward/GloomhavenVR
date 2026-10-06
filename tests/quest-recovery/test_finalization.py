"""Native evidence integrity, bounded measured activity and atomic finalization."""
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import bundle_recovery as bundles
import export_identity
import native_evidence as native
import recover


class Fixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.output = self.root / "project"; self.output.mkdir()
        self.evidence = self.root / "Evidence"; self.evidence.mkdir()
    def tearDown(self): self.temp.cleanup()
    def recipe(self, base, name="original.yaml", path_id=1, data=b"original native fields\n"):
        directory = base / "QuestRecovery/NativeRecipes"; directory.mkdir(parents=True, exist_ok=True)
        path = directory / name; path.write_bytes(data)
        row = {"collection": "native-core", "pathId": path_id, "classId": 114,
               "yamlPath": name, "yamlSha256": hashlib.sha256(data).hexdigest()}
        with (directory / "index.jsonl").open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(row) + "\n")
        return path, row
    def redirects(self, base, path_id=1):
        path = base / "QuestRecovery/native-redirect-identities.jsonl"; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"collection": "native-core", "pathId": path_id}) + "\n")
        return path
    def records(self):
        return [{"path": path.relative_to(self.output).as_posix(), "bytes": path.stat().st_size,
                 "sha256": recover.sha256(path)} for path in sorted(self.output.rglob("*")) if path.is_file()]


class NativeMergeTests(Fixture):
    def test_invocation_recipe_proof_is_reused_then_invalidated_by_real_modification(self):
        copied, _ = self.recipe(self.output); self.recipe(self.evidence)
        proofs = native.FileProofs()
        with patch.object(native, "_hash_recipe", wraps=native._hash_recipe) as hashes:
            native.merge(self.output, self.evidence, proofs=proofs)
            native.merge(self.output, self.evidence, proofs=proofs)
        self.assertEqual(sum(call.args[0] == copied for call in hashes.call_args_list), 1)
        copied.write_bytes(b"changed native fields!\n")
        with self.assertRaisesRegex(recover.RecoveryError, "Original native recipe changed"):
            native.merge(self.output, self.evidence, proofs=proofs)

    def test_fresh_invocation_never_adopts_a_previous_stat_only_recipe_proof(self):
        copied, _ = self.recipe(self.output); self.recipe(self.evidence)
        native.merge(self.output, self.evidence, proofs=native.FileProofs())
        with patch.object(native, "_hash_recipe", wraps=native._hash_recipe) as hashes:
            native.merge(self.output, self.evidence, proofs=native.FileProofs())
        self.assertEqual(sum(call.args[0] == copied for call in hashes.call_args_list), 1)

    def test_matching_retained_and_incoming_recipe_are_hashed_once_each_without_recopy(self):
        copied, _ = self.recipe(self.output); source, _ = self.recipe(self.evidence)
        # Duplicate index rows do not multiply file work.
        index = source.parent / "index.jsonl"; index.write_bytes(index.read_bytes() * 2)
        with patch.object(native, "sha256", wraps=recover.sha256) as hashes, patch.object(native, "_copy_recipe", side_effect=AssertionError("unchanged bytes must not be rewritten")):
            records = native.merge(self.output, self.evidence)
        self.assertEqual([call.args[0] for call in hashes.call_args_list], [copied, source])
        self.assertEqual(len(records), 2)
        self.assertEqual(len((copied.parent / "index.jsonl").read_text().splitlines()), 1)
        bundles.verified_records(self.output, records)

    def test_copied_new_recipe_has_exact_receipt_without_an_additional_read(self):
        source, row = self.recipe(self.evidence)
        with patch.object(native, "sha256", side_effect=AssertionError("copy hashes its actual bytes")):
            records = native.merge(self.output, self.evidence)
        target = self.output / "QuestRecovery/NativeRecipes" / source.name
        self.assertEqual(target.read_bytes(), source.read_bytes())
        self.assertEqual(next(item for item in records if item["path"].endswith(".yaml"))["sha256"], row["yamlSha256"])
        bundles.verified_records(self.output, records)

    def test_retained_file_corruption_fails_before_index_publication(self):
        path, _ = self.recipe(self.output); self.recipe(self.evidence)
        before = (path.parent / "index.jsonl").read_bytes(); path.write_bytes(b"corrupted evidence")
        with self.assertRaises(recover.RecoveryError): native.merge(self.output, self.evidence)
        self.assertEqual((path.parent / "index.jsonl").read_bytes(), before)

    def test_incoming_corruption_fails_even_when_its_named_output_exists_without_old_index(self):
        source, _ = self.recipe(self.evidence)
        target = self.output / "QuestRecovery/NativeRecipes" / source.name; target.parent.mkdir(parents=True)
        target.write_bytes(source.read_bytes()); source.write_bytes(b"corrupted incoming source")
        with self.assertRaises(recover.RecoveryError): native.merge(self.output, self.evidence)
        self.assertFalse((target.parent / "index.jsonl").exists())

    def test_corrupt_new_copy_does_not_publish_recipe_or_index(self):
        source, _ = self.recipe(self.evidence); source.write_bytes(b"source changed after original capture")
        with self.assertRaises(recover.RecoveryError): native.merge(self.output, self.evidence)
        self.assertEqual(list((self.output / "QuestRecovery/NativeRecipes").iterdir()), [])

    def test_atomic_index_cancellation_preserves_previous_index(self):
        path, _ = self.recipe(self.output); self.recipe(self.evidence, "second.yaml", 2)
        before = (path.parent / "index.jsonl").read_bytes(); replacing = native.os.replace
        def cancel(source, target):
            if Path(target).name == "index.jsonl": raise KeyboardInterrupt()
            return replacing(source, target)
        with patch.object(native.os, "replace", side_effect=cancel), self.assertRaises(KeyboardInterrupt):
            native.merge(self.output, self.evidence)
        self.assertEqual((path.parent / "index.jsonl").read_bytes(), before)
        self.assertEqual(list(path.parent.glob("*.tmp-*")), [])

    def test_windows_escape_and_case_collisions_are_rejected_on_every_host(self):
        for name in ("../outside.yaml", "..\\outside.yaml", "C:outside.yaml"):
            with self.subTest(name=name), self.assertRaises(recover.RecoveryError):
                native._recipe_path(self.evidence, {"yamlPath": name})
        self.recipe(self.output, "Recipe.yaml"); self.recipe(self.evidence, "recipe.yaml")
        with self.assertRaises(recover.RecoveryError): native.merge(self.output, self.evidence)

    def test_small_recipe_work_is_rate_limited_instead_of_emitting_per_copy(self):
        for index in range(100): self.recipe(self.evidence, str(index) + ".yaml", index)
        stream = io.StringIO()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), patch.object(recover.build_progress.time, "monotonic", return_value=0), contextlib.redirect_stdout(stream):
            records = native.merge(self.output, self.evidence)
        events = [json.loads(line.removeprefix(recover.build_progress.PREFIX)) for line in stream.getvalue().splitlines() if line.startswith(recover.build_progress.PREFIX)]
        self.assertLess(len(events), 12)
        completed = next(row for row in events if row["phase"] == "recovery-native-recipe-merge" and row["status"] == "complete")
        self.assertEqual((completed["done"], completed["total"]), (100, 100))
        self.assertEqual(len(records), 101)


class JournalNativeRestartTests(Fixture):
    def setUp(self):
        super().setUp(); self.workspace = self.root / "workspace"; self.workspace.mkdir()
        self.old, _ = self.recipe(self.output); self.redirects(self.output)
        self.new, _ = self.recipe(self.evidence, "second.yaml", 2); self.redirects(self.evidence, 2)
        self.progress = {"schema": 1, "completedGroups": [], "files": self.records(), "identities": []}
        self.checkpoint = self.output / "quest-full-recovery-progress.json"; recover.write_json(self.checkpoint, self.progress)
    def begin(self): bundles.begin_merge(self.output, self.workspace, 0, [], self.evidence, {})

    def test_windows_unsafe_recipe_refuses_journal_before_output_mutation(self):
        index = self.new.parent / "index.jsonl"; row = json.loads(index.read_text())
        row["yamlPath"] = "..\\outside.yaml"; index.write_text(json.dumps(row) + "\n")
        before = self.records()
        with self.assertRaises(recover.RecoveryError): self.begin()
        self.assertFalse((self.workspace / "merge-pending.json").exists())
        self.assertEqual(self.records(), before)

    def test_interrupted_native_indexes_and_recipe_restore_then_replay_identically(self):
        old_index = (self.old.parent / "index.jsonl").read_bytes()
        old_redirect = (self.output / bundles.MUTABLE_NATIVE_INDICES[0]).read_bytes()
        self.begin(); first = native.merge(self.output, self.evidence)
        # No new checkpoint yet: the durable journal rolls back only this batch.
        bundles.recover_merge(self.output, self.workspace)
        self.assertEqual((self.old.parent / "index.jsonl").read_bytes(), old_index)
        self.assertEqual((self.output / bundles.MUTABLE_NATIVE_INDICES[0]).read_bytes(), old_redirect)
        self.assertFalse((self.old.parent / self.new.name).exists())
        self.begin(); self.assertEqual(native.merge(self.output, self.evidence), first)
        bundles.verified_records(self.output, first)

    def test_corrupted_recipe_after_published_checkpoint_keeps_journal_for_repair(self):
        self.begin(); records = native.merge(self.output, self.evidence)
        self.progress.update(completedGroups=[0], files=records); bundles.write_checkpoint(self.checkpoint, self.progress)
        (self.old.parent / self.new.name).write_bytes(b"new file corrupted after commit")
        with self.assertRaises(recover.RecoveryError): bundles.recover_merge(self.output, self.workspace)
        self.assertTrue((self.workspace / "merge-pending.json").exists())
        self.assertTrue((self.workspace / "merge-index-1.backup").exists())

    def test_completed_writer_checkpoint_cleans_journal_without_cold_output_scan(self):
        proofs = native.FileProofs(); self.begin()
        records = native.merge(self.output, self.evidence, proofs=proofs)
        self.progress.update(completedGroups=[0], files=records)
        bundles.write_checkpoint(self.checkpoint, self.progress, proofs=proofs)
        with patch.object(bundles, "verified_records", side_effect=AssertionError("Already qualified writer output must not be rescanned")):
            bundles.commit_merge(self.output, self.workspace, 0, self.progress, proofs)
        self.assertFalse((self.workspace / "merge-pending.json").exists())
        bundles.verified_records(self.output, records)

    def test_modified_writer_checkpoint_refuses_cleanup_without_silently_readopting_it(self):
        proofs = native.FileProofs(); self.begin()
        self.progress["completedGroups"] = [0]
        bundles.write_checkpoint(self.checkpoint, self.progress, proofs=proofs)
        with self.checkpoint.open("ab") as stream: stream.write(b" ")
        with self.assertRaisesRegex(recover.RecoveryError, "checkpoint changed before journal cleanup"):
            bundles.commit_merge(self.output, self.workspace, 0, self.progress, proofs)
        self.assertTrue((self.workspace / "merge-pending.json").exists())

    def test_second_merge_cannot_silently_replace_unfinished_journal(self):
        self.begin(); journal = self.workspace / "merge-pending.json"; before = journal.read_bytes()
        with self.assertRaisesRegex(recover.RecoveryError, "previous recovery merge is unfinished"):
            self.begin()
        self.assertEqual(journal.read_bytes(), before)

    def test_rollback_reads_unchanged_files_once_and_restores_only_mutable_indexes(self):
        self.begin(); native.merge(self.output, self.evidence)
        with patch.object(bundles, "_hash_file", wraps=bundles._hash_file) as hashes:
            result = bundles.recover_merge(self.output, self.workspace)
        self.assertEqual(result, self.progress)
        self.assertEqual(sum(call.args[0] == self.old for call in hashes.call_args_list), 1)
        self.assertFalse((self.old.parent / self.new.name).exists())



class CheckpointActivityTests(Fixture):
    def test_streamed_checkpoint_has_same_data_and_real_record_total(self):
        path = self.root / "checkpoint.json"
        value = {"schema": 1, "files": [{"path": "Assets/" + str(index), "sha256": "b" * 64} for index in range(1000)],
                 "identities": [{"collection": "é", "objects": [{"pathId": 17}]}], "completedGroups": [0, 1]}
        stream = io.StringIO()
        proofs = native.FileProofs()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(stream):
            published = bundles.write_checkpoint(path, value, proofs=proofs)
        self.assertEqual(json.loads(path.read_text()), value)
        self.assertEqual(published, recover.sha256(path))
        self.assertEqual(proofs.published_digest(path), published)
        complete = [json.loads(line.removeprefix(recover.build_progress.PREFIX)) for line in stream.getvalue().splitlines() if line.startswith(recover.build_progress.PREFIX)][-1]
        self.assertEqual((complete["phase"], complete["done"], complete["total"]), ("recovery-checkpoint-write", 1004, 1004))
        self.assertLess(path.stat().st_size, len(json.dumps(value, indent=2)))

    def test_checkpoint_publication_interruption_keeps_prior_valid_bytes(self):
        path = self.root / "checkpoint.json"; recover.write_json(path, {"previous": "durable"})
        with patch.object(bundles.os, "replace", side_effect=KeyboardInterrupt()), self.assertRaises(KeyboardInterrupt):
            bundles.write_checkpoint(path, {"next": [1, 2, 3]})
        self.assertEqual(json.loads(path.read_text()), {"previous": "durable"})
        self.assertEqual(list(self.root.glob("*.tmp-*")), [])

    def test_large_verification_reports_actual_file_bytes_and_rejects_mutation(self):
        path = self.output / "large.asset"; path.write_bytes(b"original" * (2 * 1048576))
        records = self.records(); stream = io.StringIO()
        with patch.dict(os.environ, {recover.build_progress.ENV: "1"}), contextlib.redirect_stdout(stream): bundles.verified_records(self.output, records)
        events = [json.loads(line.removeprefix(recover.build_progress.PREFIX)) for line in stream.getvalue().splitlines() if line.startswith(recover.build_progress.PREFIX)]
        complete = next(row for row in events if row["phase"] == "recovery-checkpoint-file-hash" and row["status"] == "complete")
        self.assertEqual((complete["done"], complete["total"]), (path.stat().st_size, path.stat().st_size))
        with path.open("r+b") as stream: stream.write(b"changed!")
        with self.assertRaises(recover.RecoveryError): bundles.verified_records(self.output, records)


if __name__ == "__main__": unittest.main()
