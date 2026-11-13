"""Targeted retained-file repairs through real manifests, witnesses and journals.

The portable scene bytes stand in for private original LightingData. Its actual
ownership is resolved through the same case migration and script-order identity
census as production; its unchanged meta deliberately has no invented owner.
Reconstruction uses immutable external files. No phase action or Unity Library
reset can conceal a failed resume in this fixture.
"""
import copy
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import prepare_resume as resume
import preparation_identity
import storage
from test_post_effect_import_resume import SCHEDULE


class TargetedPreparationRepairTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="quest-targeted-repair-")
        self.addCleanup(self.temporary.cleanup)
        self.output = Path(self.temporary.name)
        self.project = self.output / "projects" / ("b" * 64)
        self.sources = self.output / "immutable-original-export"
        self.assets = ["Assets/Scenes/Bootstrap/LightingData.asset",
                       "Assets/Scenes/MainMenu/LightingData.asset"]
        self.originals = {}
        self.original_metas = {}
        self.calls = []
        first = self.open()
        for relative in self.assets:
            raw = ("%YAML 1.1\n--- !u!1120 &11200000\nLightingDataAsset:\n"
                   "  m_Name: " + relative + "\n  m_Lightmaps: []\n").encode()
            self.originals[relative] = raw
            self.put(relative, raw)
            metadata = b"guid: " + hashlib.md5(relative.encode()).hexdigest().encode() + b"\nNativeFormatImporter:\n"
            self.original_metas[relative] = metadata
            self.put(relative + ".meta", metadata)
            source = self.sources / relative
            source.parent.mkdir(parents=True, exist_ok=True)
            source.write_bytes(raw)
            source.with_name(source.name + ".meta").write_bytes(metadata)
        scene_manifest = "Assets/QuestOriginalCampaign/campaign-scenes.json"
        case_receipt = "QuestStartupEvidence/case-path-migration.json"
        script_manifest = "Assets/QuestOriginalStartup/script-orders.json"
        script_receipt = "QuestStartupEvidence/script-orders-source.json"
        self.put(scene_manifest, json.dumps({"schema": 1, "scenes": [{"assetPath": name} for name in self.assets]}).encode())
        self.put(case_receipt, json.dumps({"schema": 1, "pathMap": {}, "manifestSha256": {
            scene_manifest: storage.record_file(self.project / scene_manifest, scene_manifest)["sha256"]}}).encode())
        self.put(script_manifest, json.dumps({"schema": 1, "entries": [], "excluded": []}).encode())
        self.put(script_receipt, json.dumps({"schema": 1, "plugins": [], "assetIdentityEvidence": [
            storage.record_file(self.project / name, name) for name in self.assets]}).encode())
        for name, operation in SCHEDULE:
            sentinel = "QuestFixture/closed-" + name
            self.put(sentinel, ("owned " + name).encode())
            paths = [sentinel]
            if name == "case-paths": paths = resume.manifest_contracts(self.project, [case_receipt, scene_manifest])
            if name == "script-orders": paths = resume.manifest_contracts(self.project, [script_receipt, script_manifest])
            first.value["steps"].append({"name": name, "operation": operation,
                "outputs": [first._observe(path) for path in paths]})
        storage.write_json(first.journal, first.value)
        self.journal = first.journal
        self.initial = copy.deepcopy(first.value)
        first.close()
        self.put("Library/expensive-import", b"retained imported artifacts")
        self.good = {path: (path.read_bytes(), path.stat().st_mtime_ns)
                     for path in self.project.rglob("*") if path.is_file() and path.relative_to(self.project).as_posix() not in self.assets}
        self.assertTrue(preparation_identity._completed_game_preparation(self.initial, "game"))

    def put(self, relative, raw):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(raw)
        return path

    def open(self, **kwargs):
        return resume.Preparation(self.output, self.project, input_key="a" * 64, target="game", recipe=1, **kwargs)

    def provider(self, relative, row, owner, stage):
        self.calls.append((relative, owner))
        if relative not in self.originals: return None
        self.assertEqual(owner, "script-orders")
        source = self.sources / relative
        raw = source.read_bytes()
        if len(raw) != row["size"] or hashlib.sha256(raw).hexdigest() != row["sha256"]:
            raise storage.BuildError("immutable source does not match the original producer")
        stage.write_bytes(raw)
        result = {"size": len(raw), "sha256": hashlib.sha256(raw).hexdigest(),
                  "source": str(source), "recipe": "qualified-original-file", "detail": relative}
        if not (self.project / (relative + ".meta")).exists():
            name = relative + ".meta"
            metadata = source.with_name(source.name + ".meta").read_bytes()
            if metadata != self.original_metas[relative]: raise storage.BuildError("immutable metadata changed")
            metadata_stage = (self.project / name).with_name(".quest-repair-" + hashlib.sha256(name.encode()).hexdigest()[:20] + ".part")
            metadata_stage.write_bytes(metadata)
            result["companions"] = [{"path": name, "size": len(metadata), "sha256": hashlib.sha256(metadata).hexdigest(),
                                      "stagingPath": str(metadata_stage), "source": str(source) + ".meta"}]
        return result

    def binary_import(self, *names):
        for relative in names or self.assets:
            self.put(relative, b"UnityFS\x00native binary lighting\x00" + relative.encode())

    def assert_retained(self):
        current = json.loads(self.journal.read_text())
        self.assertEqual(current["steps"], self.initial["steps"])
        self.assertIsNone(current["pending"])
        for relative in self.assets:
            self.assertEqual((self.project / relative).read_bytes(), self.originals[relative])
            self.assertNotIn(relative + ".meta", {row["path"] for step in current["steps"] for row in step["outputs"]})
        for path, expected in self.good.items():
            self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), expected)
        self.assertFalse((self.journal.parent / "repair.json").exists())
        self.assertFalse(list(self.project.rglob(".quest-repair-*.part")))

    def test_all_changed_lighting_files_restore_without_repeating_any_phase(self):
        self.binary_import()
        guards = []
        second = self.open(repair_provider=self.provider, repair_guard=lambda: guards.append("waited"))
        self.assertEqual(second.repaired, set(self.assets))
        second.close()
        self.assertEqual(self.calls, [(name, "script-orders") for name in self.assets])
        self.assertEqual(guards, ["waited"])
        self.assert_retained()
        self.assertEqual(json.loads(self.journal.read_text())["fileRepairs"]["count"], 2)
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(
                resume, "digest", side_effect=AssertionError("warm retained data must not be read")):
            third = self.open(repair_provider=lambda *_args: self.fail("warm run must not reconstruct"))
            self.assertEqual(third.repaired, set())
            third.close()

    def test_missing_file_and_missing_directory_restore_only_their_members(self):
        for relative in self.assets:
            (self.project / relative).unlink()
        (self.project / self.assets[1]).parent.joinpath("LightingData.asset.meta").unlink()
        (self.project / self.assets[1]).parent.rmdir()
        metadata_path = self.project / (self.assets[1] + ".meta")
        metadata_bytes = self.good.pop(metadata_path)[0]
        second = self.open(repair_provider=self.provider); second.close()
        self.assertEqual(metadata_path.read_bytes(), metadata_bytes)
        self.assertNotIn(self.assets[1] + ".meta", {row["path"] for step in json.loads(self.journal.read_text())["steps"] for row in step["outputs"]})
        self.assert_retained()

    def test_no_provider_preserves_prior_explicit_error(self):
        self.binary_import(self.assets[0])
        with self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed in script-orders"):
            self.open()
        self.assertEqual(self.calls, [])

    def test_provider_does_not_convert_an_unknown_output_into_success(self):
        relative = "QuestFixture/closed-loading-sprite"
        self.put(relative, b"corrupt receipt")
        with self.assertRaisesRegex(storage.BuildError, "changed in loading-sprite"):
            self.open(repair_provider=self.provider)
        self.assertEqual((self.project / relative).read_bytes(), b"corrupt receipt")

    def test_wrong_reconstruction_and_wrong_claim_cannot_replace_target(self):
        for kind in ("wrong-bytes", "wrong-claim"):
            with self.subTest(kind=kind):
                self.binary_import(self.assets[0]); before = (self.project / self.assets[0]).read_bytes()
                def provider(relative, row, _owner, stage):
                    stage.write_bytes(b"wrong reconstructed bytes" if kind == "wrong-bytes" else self.originals[relative])
                    return {"size": row["size"], "sha256": row["sha256"] if kind == "wrong-bytes" else "f" * 64}
                with self.assertRaisesRegex(storage.BuildError, "repair (staging bytes|provider)" ):
                    self.open(repair_provider=provider)
                self.assertEqual((self.project / self.assets[0]).read_bytes(), before)
                self.assertFalse((self.journal.parent / "repair.json").exists())

    def test_concurrent_editor_write_during_reconstruction_is_retained(self):
        self.binary_import(self.assets[0])
        changed = b"a concurrent Editor saved this"
        def provider(relative, row, owner, stage):
            result = self.provider(relative, row, owner, stage)
            self.put(relative, changed)
            return result
        with self.assertRaisesRegex(storage.BuildError, "changed while its targeted repair was reconstructed"):
            self.open(repair_provider=provider)
        self.assertEqual((self.project / self.assets[0]).read_bytes(), changed)

    def test_concurrent_original_meta_edit_cannot_be_hidden_by_asset_restoration(self):
        self.binary_import(self.assets[0])
        before = (self.project / self.assets[0]).read_bytes()
        def provider(relative, row, owner, stage):
            result = self.provider(relative, row, owner, stage)
            self.put(relative + ".meta", b"guid: " + b"f" * 32 + b"\nNativeFormatImporter:\n")
            return result
        with self.assertRaisesRegex(storage.BuildError, "GUID metadata changed while its targeted repair was reconstructed"):
            self.open(repair_provider=provider)
        self.assertEqual((self.project / self.assets[0]).read_bytes(), before)

    def test_linked_generated_target_is_never_overwritten(self):
        self.binary_import(self.assets[0])
        target = self.project / self.assets[0]
        external = self.output / "external.bin"; external.write_bytes(target.read_bytes())
        target.unlink(); os.link(external, target)
        with self.assertRaisesRegex(storage.BuildError, "ordinary unlinked generated file"):
            self.open(repair_provider=self.provider)
        self.assertEqual(external.read_bytes(), target.read_bytes())

    def test_publication_cuts_resume_staged_file_without_reconstruction(self):
        for timing in ("before", "after"):
            with self.subTest(timing=timing):
                self.binary_import(self.assets[0]); self.calls.clear()
                original_replace = os.replace
                target = self.project / self.assets[0]
                def cut(source, destination):
                    if Path(destination) == target and Path(source).name.startswith(".quest-repair-"):
                        if timing == "after": original_replace(source, destination)
                        raise KeyboardInterrupt("atomic publication cut")
                    return original_replace(source, destination)
                with patch.object(resume.os, "replace", side_effect=cut), self.assertRaises(KeyboardInterrupt):
                    self.open(repair_provider=self.provider)
                self.assertTrue((self.journal.parent / "repair.json").exists())
                second = self.open(repair_provider=lambda *_args: self.fail("staged repair must replay")); second.close()
                self.assertEqual(len(self.calls), 1)
                self.assert_retained()

    def test_journal_cuts_do_not_double_count_completed_file_repairs(self):
        for timing in ("before", "after"):
            with self.subTest(timing=timing):
                self.binary_import(self.assets[0]); self.calls.clear()
                old_count = json.loads(self.journal.read_text()).get("fileRepairs", {}).get("count", 0)
                original_write = resume.write_json
                def cut(path, value):
                    if Path(path) == self.journal and value.get("fileRepairs", {}).get("count", 0) > old_count:
                        if timing == "after": original_write(path, value)
                        raise KeyboardInterrupt("journal publication cut")
                    return original_write(path, value)
                with patch.object(resume, "write_json", side_effect=cut), self.assertRaises(KeyboardInterrupt):
                    self.open(repair_provider=self.provider)
                second = self.open(repair_provider=lambda *_args: self.fail("published file must replay")); second.close()
                self.assertEqual(json.loads(self.journal.read_text())["fileRepairs"]["count"], old_count + 1)
                self.assert_retained()

    def test_missing_interrupted_staging_reconstructs_only_that_file_once(self):
        self.binary_import(self.assets[0])
        original_write = resume.write_json
        def cut(path, value):
            original_write(path, value)
            if Path(path).name == "repair.json": raise KeyboardInterrupt("staging cut")
        with patch.object(resume, "write_json", side_effect=cut), self.assertRaises(KeyboardInterrupt):
            self.open(repair_provider=self.provider)
        control = json.loads((self.journal.parent / "repair.json").read_text())
        (self.project / control["stage"]).unlink()
        second = self.open(repair_provider=self.provider); second.close()
        self.assertEqual(self.calls, [(self.assets[0], "script-orders")] * 2)
        self.assert_retained()

    def test_corrupted_interrupted_stage_reconstructs_once_without_a_retry_loop(self):
        self.binary_import(self.assets[0])
        native_write = resume.write_json
        def cut(path, value):
            native_write(path, value)
            if Path(path).name == "repair.json": raise KeyboardInterrupt("staging cut")
        with patch.object(resume, "write_json", side_effect=cut), self.assertRaises(KeyboardInterrupt):
            self.open(repair_provider=self.provider)
        control = json.loads((self.journal.parent / "repair.json").read_text())
        (self.project / control["stage"]).write_bytes(b"corrupt cache")
        second = self.open(repair_provider=self.provider); second.close()
        self.assertEqual(self.calls, [(self.assets[0], "script-orders")] * 2)
        self.assert_retained()

    def test_original_meta_and_asset_publication_cuts_keep_the_original_guid(self):
        relative = self.assets[0]
        meta = self.project / (relative + ".meta")
        expected_meta = self.good.pop(meta)[0]
        for member in (meta, self.project / relative):
            for timing in ("before", "after"):
                with self.subTest(member=member.name, timing=timing):
                    self.binary_import(relative)
                    meta.unlink(missing_ok=True)
                    self.calls.clear()
                    replace = os.replace
                    def cut(source, destination):
                        if Path(destination) == member and Path(source).name.startswith(".quest-repair-"):
                            if timing == "after": replace(source, destination)
                            raise KeyboardInterrupt("companion publication cut")
                        return replace(source, destination)
                    with patch.object(resume.os, "replace", side_effect=cut), self.assertRaises(KeyboardInterrupt):
                        self.open(repair_provider=self.provider)
                    second = self.open(repair_provider=lambda *_args: self.fail("durable companion must replay"))
                    self.assertEqual(second.repaired, {relative, relative + ".meta"})
                    second.close()
                    self.assertEqual(meta.read_bytes(), expected_meta)
                    self.assertEqual(self.calls, [(relative, "script-orders")])
                    self.assert_retained()

    def test_windows_flush_uses_a_real_writable_staging_descriptor(self):
        self.binary_import(self.assets[0])
        native_fsync = os.fsync
        flushed = []
        def flush(descriptor):
            # A zero-byte write is an actual descriptor access check without
            # changing the stage. Linux rejects it with EBADF for O_RDONLY;
            # Windows FlushFileBuffers likewise requires GENERIC_WRITE.
            self.assertEqual(os.write(descriptor, b""), 0)
            native_fsync(descriptor)
            flushed.append(descriptor)
        with patch.object(resume.os, "fsync", side_effect=flush):
            second = self.open(repair_provider=self.provider); second.close()
        # The shared os module also flushes atomic JSON controls; every flushed
        # descriptor, including the actual staging file, must be writable.
        self.assertGreaterEqual(len(flushed), 1)
        self.assert_retained()

    def test_repaired_batch_publishes_the_large_ownership_journal_once(self):
        self.binary_import()
        writes = []
        native_write = resume.write_json
        def record(path, value):
            if Path(path) == self.journal and value.get("fileRepairs", {}).get("count"):
                writes.append(value["fileRepairs"]["count"])
            return native_write(path, value)
        with patch.object(resume, "write_json", side_effect=record):
            second = self.open(repair_provider=self.provider); second.close()
        self.assertEqual(writes, [2])
        self.assert_retained()

    def test_changed_bytes_with_unsupported_metadata_cannot_be_overwritten(self):
        self.binary_import(self.assets[0])
        saved = (self.project / self.assets[0]).stat()
        changed = bytearray((self.project / self.assets[0]).read_bytes()); changed[-1] ^= 1
        def provider(relative, row, owner, stage):
            result = self.provider(relative, row, owner, stage)
            self.put(relative, bytes(changed))
            os.utime(self.project / relative, ns=(saved.st_atime_ns, saved.st_mtime_ns))
            return result
        with patch.object(storage.ValidatedFileWitnesses, "current", return_value=None), self.assertRaisesRegex(
                storage.BuildError, "changed while its targeted repair was reconstructed"):
            self.open(repair_provider=provider)
        self.assertEqual((self.project / self.assets[0]).read_bytes(), bytes(changed))

    def test_repair_progress_reports_file_and_byte_work(self):
        self.binary_import()
        events = []
        class Counter:
            def __init__(self, phase, total, unit, detail):
                self.phase, self.total, self.done = phase, total, 0
            def add(self, count, detail=None):
                self.done += count
                events.append((self.phase, self.done, self.total, detail))
            def finish(self):
                if self.done != self.total: raise AssertionError("counter did not finish measured work")
        progress = SimpleNamespace(Counter=Counter, event=lambda phase, done, total, _unit, detail, **kwargs:
                                   events.append((phase, done, total, detail)))
        second = self.open(repair_provider=self.provider, progress=progress); second.close()
        file_events = [item for item in events if item[0] == "prepare-resume-repair-files"]
        self.assertEqual(file_events[-1][1:3], (2, 2))
        self.assertTrue(all(item[2] == 2 for item in file_events))
        self.assertEqual([item[1] for item in file_events], sorted(item[1] for item in file_events))
        byte_events = [item for item in events if item[0] == "prepare-resume-repair-bytes"]
        self.assertEqual(len(byte_events), 2)
        self.assertEqual([item[1] for item in byte_events], [len(self.originals[name]) for name in self.assets])


if __name__ == "__main__": unittest.main()
