"""Interrupted preparation with real files/receipts and a real case migration.

Original codec/Unity/native compilers use declared small fixture adapters. The
builder, durable journal, copy/undo, ZIP packing, plugin importer edits, case-path
subprocess, final settings and outer receipt checks execute their real code.
"""
from contextlib import ExitStack
import hashlib
import importlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import storage

resume = builder.prepare_resume


class JournalTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.project = self.root / "projects/fixture"

    def tearDown(self): self.temp.cleanup()

    def put(self, name, data):
        path = self.project / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def journal(self, **kwargs):
        return resume.Preparation(self.root, self.project, input_key="a" * 64,
                                  target="game", recipe=1, **kwargs)

    def test_finished_native_audio_retained_after_destructive_texture_failure(self):
        calls = []
        original = self.put("Assets/Texture.png", b"original image")
        library = self.put("Library/imported", b"keep import")
        def pipeline(journal, fail):
            for name in ("native", "audio"):
                with journal.operation(name, 1):
                    journal.run(name, name, lambda name=name: (calls.append(name), self.put("Assets/" + name, name.encode())), ["Assets/" + name])
            def texture():
                calls.append("textures")
                self.assertEqual(original.read_bytes(), b"original image")
                original.unlink()
                self.put("Assets/Texture.asset", b"native image")
                if fail: raise storage.BuildError("decoder interrupted")
            with journal.operation("textures", 1):
                journal.run("textures", "textures", texture, resume.Contracts(["Assets/Texture.png", "Assets/Texture.asset"], absent=["Assets/Texture.png"]),
                            mutations=["Assets/Texture.png", "Assets/Texture.asset"])
        first = self.journal()
        with self.assertRaisesRegex(storage.BuildError, "interrupted"): pipeline(first, True)
        first.close()
        second = self.journal()
        pipeline(second, False); second.finish(); second.close()
        self.assertEqual(calls, ["native", "audio", "textures", "textures"])
        self.assertEqual(library.read_bytes(), b"keep import")
        self.assertFalse(original.exists())

    def test_latest_owner_contract_accepts_real_later_rewrite(self):
        first = self.journal()
        with first.operation("graphics", 2):
            first.run("original", "graphics", lambda: self.put("Assets/shared", b"before"), ["Assets/shared"])
            first.run("derived", "graphics", lambda: self.put("Assets/shared", b"after"), ["Assets/shared"])
        first.close()
        second = self.journal()
        with second.operation("graphics", 2):
            second.run("original", "graphics", lambda: self.fail("original repeated"), ["Assets/shared"])
            second.run("derived", "graphics", lambda: self.fail("derived repeated"), ["Assets/shared"])
        second.finish(); second.close()

    def test_corrupted_completed_output_is_not_reported_as_reuse(self):
        first = self.journal()
        with first.operation("native-runtime", 1):
            first.run("native", "native-runtime", lambda: self.put("Assets/native", b"valid"), ["Assets/native"])
        first.close()
        self.put("Assets/native", b"wrong")
        with self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed in native"):
            self.journal()
        self.assertEqual((self.project / "Assets/native").read_bytes(), b"wrong")

    def test_missing_output_does_not_acquire_successful_checkpoint(self):
        first = self.journal()
        with self.assertRaisesRegex(storage.BuildError, "missing its required output"):
            with first.operation("audio", 1): first.run("audio", "audio", lambda: self.put("Assets/receipt", b"{}"), ["Assets/receipt", "Assets/missing.ogg"])
        self.assertEqual(first.value["steps"], [])
        self.assertEqual(first.value["pending"]["name"], "audio")
        first.close()

    def test_ownership_change_preserves_project_and_library(self):
        first = self.journal(); first.close()
        sentinel = self.put("Library/expensive", b"keep")
        journal = next((self.root / "cache/prepare-resume").rglob("journal.json"))
        value = json.loads(journal.read_text()); value["owner"] = "someone else"
        storage.write_json(journal, value)
        with self.assertRaisesRegex(storage.BuildError, "ownership differs"): self.journal()
        self.assertEqual(sentinel.read_bytes(), b"keep")

    def test_changed_immutable_source_rejects_same_input_resume(self):
        source = self.root / "owned-game.bin"; source.write_bytes(b"original")
        records = [(source, storage.record_file(source, "owned-game.bin"))]
        first = self.journal(source_files=records); first.close()
        source.write_bytes(b"modified")
        with self.assertRaisesRegex(storage.BuildError, "source changed since its checkpoint"): self.journal(source_files=records)

    def test_source_mutation_during_function_cannot_commit(self):
        source = self.root / "source.cs"; source.write_bytes(b"original")
        first = self.journal(source_files=[(source, storage.record_file(source, "source.cs"))])
        def action():
            self.put("Assets/result", b"result")
            source.write_bytes(b"modified")
        with self.assertRaisesRegex(storage.BuildError, "source changed while running"):
            with first.operation("graphics", 1): first.run("graphics", "graphics", action, ["Assets/result"])
        self.assertEqual(first.value["steps"], []); first.close()

    def test_existing_owned_directory_undo_removes_only_incomplete_mutations(self):
        original = self.put("Packages/ugui/Runtime/source.cs", b"original")
        self.put("Packages/ugui/empty/placeholder", b"placeholder")
        library = self.put("Library/imported", b"expensive")
        first = self.journal()
        def action():
            original.write_bytes(b"modified")
            self.put("Packages/ugui/Added/new.cs", b"incomplete")
            raise storage.BuildError("UGUI interrupted")
        with self.assertRaisesRegex(storage.BuildError, "UGUI interrupted"):
            with first.operation("mod-banks", 1): first.run("ugui", "mod-banks", action, ["Packages/ugui/Runtime/source.cs"], mutations=["Packages/ugui"])
        first.close(); second = self.journal()
        self.assertEqual(original.read_bytes(), b"original")
        self.assertFalse((self.project / "Packages/ugui/Added").exists())
        self.assertEqual(library.read_bytes(), b"expensive"); second.close()

    def test_absent_owned_directory_undo_removes_failed_created_tree(self):
        first = self.journal()
        def action():
            self.put("Assets/ShaderPrograms/new.hlsl", b"partial")
            raise storage.BuildError("shader interrupted")
        with self.assertRaises(storage.BuildError):
            with first.operation("graphics", 1): first.run("shader", "graphics", action, ["Assets/ShaderPrograms/new.hlsl"], mutations=["Assets/ShaderPrograms"])
        first.close(); second = self.journal()
        self.assertFalse((self.project / "Assets/ShaderPrograms").exists()); second.close()

    def test_movie_undo_reports_streamed_bytes_before_action_and_retains_original_on_retry(self):
        from types import SimpleNamespace
        rows = []
        class Counter:
            def __init__(self, phase, total, unit, detail=None):
                self.phase, self.total, self.done = phase, total, 0
                rows.append((phase, 0, total))
            def add(self, amount, detail=None):
                self.done += amount; rows.append((self.phase, self.done, self.total))
            def finish(self): self.assert_total()
            def assert_total(self):
                if self.done != self.total: raise AssertionError("Backup progress differs from actual streamed bytes")
        progress = SimpleNamespace(Counter=Counter, event=lambda *_args, **_kwargs: None)
        data = b"original movie packets" * 100000
        original = self.put("Assets/VideoClip/Intro.mov", data)
        first = self.journal(progress=progress)
        def action():
            counts = [done for phase, done, total in rows if phase == "prepare-items:startup-movies-backup"]
            self.assertGreater(len(counts), 2)
            self.assertEqual(counts[0], 0); self.assertEqual(counts[-1], len(data))
            self.assertTrue(any(0 < done < len(data) for done in counts))
            original.unlink()
            raise storage.BuildError("Movie action interrupted")
        with self.assertRaisesRegex(storage.BuildError, "Movie action interrupted"):
            with first.operation("startup-content", 1):
                first.run("startup-movies", "startup-content", action, ["Assets/VideoClip/Intro.mov"],
                          mutations=["Assets/VideoClip/Intro.mov", "Assets/VideoClip/not-created.mov"])
        first.close()
        second = self.journal()
        self.assertEqual(original.read_bytes(), data)
        self.assertEqual(second.value["steps"], [])
        self.assertIsNone(second.value["pending"])
        second.close()

    def test_corrupted_undo_preflight_changes_no_project_file(self):
        original = self.put("Assets/input", b"original")
        first = self.journal()
        def action(): original.write_bytes(b"partial"); raise storage.BuildError("interrupted")
        with self.assertRaises(storage.BuildError):
            with first.operation("textures", 1): first.run("texture", "textures", action, ["Assets/input"], mutations=["Assets/input"])
        undo = first.root / "undo-texture/0"; first.close(); undo.write_bytes(b"invalid")
        with self.assertRaisesRegex(storage.BuildError, "undo bytes changed"): self.journal()
        self.assertEqual(original.read_bytes(), b"partial")

    def test_copy_cold_same_stamp_corruption_is_repaired_by_actual_bytes(self):
        source = self.root / "source"; source.write_bytes(b"original")
        first = self.journal(); target = self.project / "Assets/copied"
        first.copy(source, target); first.close()
        stamp = resume._stamp(target)
        target.write_bytes(b"modified")
        actual_stamp = resume._stamp
        disguising = True
        def disguised(path): return stamp if path == target and disguising else actual_stamp(path)
        original_copy = resume.copy_changed
        def actual_writer(*args, **kwargs):
            nonlocal disguising
            disguising = False
            return original_copy(*args, **kwargs)
        with patch.object(resume, "_stamp", disguised), patch.object(resume, "copy_changed", actual_writer):
            second = self.journal(); second.copy(source, target); second.close()
        self.assertEqual(target.read_bytes(), b"original")

    def test_retained_copy_mutation_during_read_is_not_published_as_reuse(self):
        source = self.root / "source"; source.write_bytes(b"original")
        target = self.put("Assets/copied", b"original")
        opening = Path.open
        class MutatingReader:
            def __init__(self, stream): self.stream, self.mutated = stream, False
            def __enter__(self): return self
            def __exit__(self, *args): self.stream.close()
            def read(self, count):
                value = self.stream.read(count)
                if value and not self.mutated:
                    self.mutated = True
                    with opening(target, "wb") as writer: writer.write(b"modified")
                return value
        def open_file(path, mode="r", *args, **kwargs):
            stream = opening(path, mode, *args, **kwargs)
            return MutatingReader(stream) if path == target and mode == "rb" else stream
        accepted = []
        with patch.object(Path, "open", open_file), self.assertRaisesRegex(storage.BuildError, "target changed while qualifying"):
            resume.copy_changed(source, target, observed=accepted.append)
        self.assertEqual(accepted, [])
        self.assertEqual(target.read_bytes(), b"modified")

    def test_replaced_copy_mutation_cannot_receive_streamed_source_proof(self):
        source = self.root / "source"; source.write_bytes(b"original")
        target = self.put("Assets/copied", b"previous")
        replace = os.replace
        def mutate_after_replace(original, destination):
            replace(original, destination)
            Path(destination).write_bytes(b"modified")
        accepted = []
        with patch.object(os, "replace", mutate_after_replace), self.assertRaisesRegex(storage.BuildError, "target changed while publishing"):
            resume.copy_changed(source, target, observed=accepted.append)
        self.assertEqual(accepted, [])
        self.assertEqual(target.read_bytes(), b"modified")
        self.assertEqual(list(target.parent.glob("*.quest-prepare-copy")), [])

    def test_source_mutation_during_copystat_preserves_previous_target(self):
        source = self.root / "source"; source.write_bytes(b"original")
        target = self.put("Assets/copied", b"previous")
        copystat = shutil.copystat
        def mutate_after_copystat(original, destination):
            copystat(original, destination)
            Path(original).write_bytes(b"modified")
        accepted = []
        with patch.object(shutil, "copystat", mutate_after_copystat), self.assertRaisesRegex(storage.BuildError, "source changed before publishing"):
            resume.copy_changed(source, target, observed=accepted.append)
        self.assertEqual(accepted, [])
        self.assertEqual(target.read_bytes(), b"previous")
        self.assertEqual(list(target.parent.glob("*.quest-prepare-copy")), [])

    def test_two_killed_destructive_attempts_reuse_native_audio_and_library(self):
        original = self.put("Assets/Texture.png", b"original image")
        library = self.put("Library/imported", b"retained Unity import")
        with_source = self.root / "original-game"; with_source.write_bytes(b"untouched source")
        first = self.journal()
        for name in ("native", "audio"):
            with first.operation(name, 1):
                first.run(name, name, lambda name=name: self.put("Assets/" + name, name.encode()), ["Assets/" + name])
        first.close()
        native_stamp = (self.project / "Assets/native").stat()
        script = """
import os,sys
from pathlib import Path
sys.path.insert(0,sys.argv[1])
from prepare_resume import Preparation
root=Path(sys.argv[2]); project=root/'projects/fixture'
journal=Preparation(root,project,input_key='a'*64,target='game',recipe=1)
for name in ('native','audio'):
    with journal.operation(name,1):
        journal.run(name,name,lambda: (_ for _ in ()).throw(AssertionError('completed producer reran')),['Assets/'+name])
def killed():
    assert (project/'Assets/Texture.png').read_bytes()==b'original image'
    (project/'Assets/Texture.png').unlink()
    (project/'Assets/Texture.asset').write_bytes(b'incomplete derived image')
    os._exit(int(sys.argv[3]))
with journal.operation('textures',1):
    journal.run('texture','textures',killed,['Assets/Texture.asset'],mutations=['Assets/Texture.png','Assets/Texture.asset'])
"""
        for returncode in (17, 18):
            child = subprocess.run([sys.executable, "-c", script, str(ROOT / "tools/quest-builder"), str(self.root), str(returncode)], capture_output=True, text=True)
            self.assertEqual(child.returncode, returncode, child.stderr)
            self.assertFalse(original.exists())
        last = self.journal()
        self.assertEqual(original.read_bytes(), b"original image")
        self.assertFalse((self.project / "Assets/Texture.asset").exists())
        for name in ("native", "audio"):
            with last.operation(name, 1):
                last.run(name, name, lambda: self.fail("completed producer reran"), ["Assets/" + name])
        with last.operation("textures", 1):
            last.run("texture", "textures", lambda: self.put("Assets/Texture.asset", b"accepted derived image"), ["Assets/Texture.asset"], mutations=["Assets/Texture.png", "Assets/Texture.asset"])
        last.finish(); last.close()
        self.assertEqual((self.project / "Assets/native").stat().st_ino, native_stamp.st_ino)
        self.assertEqual(library.read_bytes(), b"retained Unity import")
        self.assertEqual(with_source.read_bytes(), b"untouched source")

    def test_copy_cold_consumes_only_target_once_then_retains_current_proof(self):
        source = self.root / "source"; source.write_bytes(b"original")
        first = self.journal(); target = self.project / "Assets/copied"
        first.copy(source, target)
        # An older copy ledger has no durable byte witness. Migration consumes
        # its retained target once, then the same proof serves later consumers.
        first.copies.execute("DELETE FROM validated_file_witnesses"); first.close()
        with patch.dict(storage._invocation_file_proofs, clear=True):
            second = self.journal()
            with patch.object(resume, "digest", wraps=resume.digest) as hashed, patch.object(resume, "copy_changed", wraps=resume.copy_changed) as copied:
                second.copy(source, target); second.copy(source, target)
                self.assertEqual(hashed.call_args_list, [unittest.mock.call(target)])
                copied.assert_not_called()
            second.close()

    def test_warm_copy_witness_survives_restart_without_reading_source_or_target(self):
        source = self.root / "source"; source.write_bytes(b"same immutable source")
        first = self.journal(); target = self.project / "Assets/copied"
        first.copy(source, target); first.close()
        with patch.dict(storage._invocation_file_proofs, clear=True):
            second = self.journal()
            with patch.object(resume, "digest", side_effect=AssertionError("Warm bytes must not be read")), patch.object(resume, "copy_changed", side_effect=AssertionError("Completed copy must not repeat")):
                second.copy(source, target); second.copy(source, target)
            self.assertEqual(second.witnesses.counters, {"files_read": 0, "bytes_read": 0, "cache_hits": 2})
            second.close()

    def test_warm_completed_output_contracts_require_no_second_byte_walk(self):
        first = self.journal()
        with first.operation("native-runtime", 1):
            first.run("native-runtime", "native-runtime", lambda: self.put("Assets/native", b"native output" * 1024), ["Assets/native"])
        first.close()
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(resume, "digest", side_effect=AssertionError("Warm contract byte walk")):
            second = self.journal()
            with second.operation("native-runtime", 1):
                second.run("native-runtime", "native-runtime", lambda: self.fail("Closed native producer repeated"), [])
            second.finish(); second.close()
        self.assertEqual(second.witnesses.counters, {"files_read": 0, "bytes_read": 0, "cache_hits": 1})

    def test_preserved_size_mtime_cannot_hide_changed_completed_output(self):
        first = self.journal()
        with first.operation("native-runtime", 1):
            first.run("native-runtime", "native-runtime", lambda: self.put("Assets/native", b"original"), ["Assets/native"])
        first.close()
        path = self.project / "Assets/native"; before = path.stat()
        path.write_bytes(b"modified"); os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
        with patch.dict(storage._invocation_file_proofs, clear=True), self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed"):
            self.journal()
        self.assertEqual(path.read_bytes(), b"modified")

    def test_unsupported_metadata_seam_falls_back_to_actual_copy_bytes(self):
        source = self.root / "source"; source.write_bytes(b"original")
        first = self.journal(); target = self.project / "Assets/copied"
        first.copy(source, target); first.close()
        with patch.object(storage, "_file_witness_stamp", return_value=None):
            second = self.journal()
            with patch.object(resume, "digest", wraps=resume.digest) as reads:
                second.copy(source, target); second.copy(source, target)
                self.assertEqual(reads.call_args_list, [unittest.mock.call(target), unittest.mock.call(target)])
            self.assertEqual(second.witnesses.counters["cache_hits"], 0)
            second.close()

    def test_authorized_base_reset_refreshes_the_owned_witness_directory(self):
        source = self.root / "source"; source.write_bytes(b"original")
        first = self.journal(); target = self.project / "Assets/copied"
        with first.operation("project-files", 1):
            first.run("base-project", "project-files", lambda: first.copy(source, target), ["Assets/copied"])
        first.close(); target.write_bytes(b"corrupted")
        resets = []
        def reset():
            resets.append(True); shutil.rmtree(self.project); self.project.mkdir(parents=True)
        second = self.journal(reset=reset)
        self.assertEqual(resets, [True])
        self.assertEqual(second.value["steps"], [])
        self.assertEqual(second.copies.execute("SELECT COUNT(*) FROM validated_file_witnesses WHERE owner=?", (second.witnesses.namespace,)).fetchone()[0], 0)
        with second.operation("project-files", 1):
            second.run("base-project", "project-files", lambda: second.copy(source, target), ["Assets/copied"])
        second.finish(); second.close()
        self.assertEqual(target.read_bytes(), b"original")
        last = self.journal(); last.close()

    def test_short_write_retains_old_target_and_cleans_temporary(self):
        source = self.root / "source"; source.write_bytes(b"new original")
        target = self.put("Assets/copied", b"previous")
        original_open = Path.open
        class ShortWriter:
            def __init__(self, stream): self.stream = stream
            def __enter__(self): return self
            def __exit__(self, *args): self.stream.close()
            def write(self, data): return self.stream.write(data[:3])
            def flush(self): self.stream.flush()
        def open_file(path, *args, **kwargs):
            stream = original_open(path, *args, **kwargs)
            return ShortWriter(stream) if path.name.endswith(".quest-prepare-copy") and args and args[0] == "wb" else stream
        with patch.object(Path, "open", open_file), self.assertRaisesRegex(storage.BuildError, "fewer bytes"):
            resume.copy_changed(source, target)
        self.assertEqual(target.read_bytes(), b"previous")
        self.assertEqual(list(target.parent.glob("*.quest-prepare-copy")), [])

    def test_phase_order_and_source_path_escape_are_rejected(self):
        first = self.journal()
        with first.operation("audio", 1): first.run("audio", "audio", lambda: self.put("Assets/audio", b"valid"), ["Assets/audio"])
        first.close(); second = self.journal()
        with self.assertRaisesRegex(storage.BuildError, "phase order changed"):
            with second.operation("audio", 1): second.run("different", "audio", lambda: self.fail("replayed"), ["Assets/audio"])
        second.close()
        for path in ("../original", "Library/imported", "Assets/../original", "C:/original"):
            with self.subTest(path=path), self.assertRaises(storage.BuildError): resume._relative(path)


class CompatiblePreparationTests(unittest.TestCase):
    """Only explicitly proven producer-compatible input repairs can rebind work."""
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.project = self.root / "projects/fixture"

    def put(self, name, data):
        path = self.project / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data)
        return path

    def journal(self, key="a", **kwargs):
        return resume.Preparation(self.root, self.project, input_key=key * 64, target="game", recipe=1, **kwargs)

    def completed_prefix(self, *, pending=False, sources=()):
        journal = self.journal(source_files=sources)
        with journal.operation("project-files", 1):
            journal.run("base-project", "project-files", lambda: self.put("Assets/Quest/Runtime/Probe.cs", b"same template"), ["Assets/Quest/Runtime/Probe.cs"])
        with journal.operation("startup-content", 3):
            for name in ("post-effects", "loading-resources", "startup-movies"):
                journal.run(name, "startup-content", lambda name=name: self.put("Assets/" + name, name.encode()), ["Assets/" + name])
        if pending:
            def interrupted():
                self.put("Assets/native-sprites.json", b"complete producer receipt; old consumer failed")
                raise storage.BuildError("old contract consumer failed")
            with self.assertRaisesRegex(storage.BuildError, "old contract consumer"):
                with journal.operation("startup-content", 1):
                    journal.run("native-sprites", "startup-content", interrupted, ["Assets/native-sprites.json"])
        journal.close()
        return journal

    def test_proven_rebind_preserves_four_closed_steps_pending_outputs_and_library(self):
        first = self.completed_prefix(pending=True)
        self.put("Library/imported", b"retained Unity state")
        old_stamps = {row["path"]: (self.project / row["path"]).stat().st_mtime_ns for step in first.value["steps"] for row in step["outputs"]}
        second = self.journal("b", compatible_input_key="a" * 64, reset=lambda: self.fail("Project reset"))
        self.assertEqual(second.value["inputKey"], "b" * 64)
        self.assertIsNone(second.value["pending"])
        self.assertEqual([step["name"] for step in second.value["steps"]], ["base-project", "post-effects", "loading-resources", "startup-movies"])
        with second.operation("project-files", 1):
            second.run("base-project", "project-files", lambda: self.fail("Base repeated"), [])
        with second.operation("startup-content", 4):
            for name in ("post-effects", "loading-resources", "startup-movies"):
                second.run(name, "startup-content", lambda: self.fail("Closed conversion repeated"), [])
            second.run("native-sprites", "startup-content", lambda: self.put("Assets/native-sprites.json", b"new valid output contracts"), ["Assets/native-sprites.json"])
        second.finish(); second.close()
        self.assertEqual((self.project / "Library/imported").read_bytes(), b"retained Unity state")
        self.assertEqual({name: (self.project / name).stat().st_mtime_ns for name in old_stamps}, old_stamps)

    def test_proven_key_migration_transfers_existing_byte_witnesses_without_payload_reads(self):
        self.completed_prefix(pending=True)
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(resume, "digest", side_effect=AssertionError("Proved compatible migration must retain byte witnesses")):
            second = self.journal("b", compatible_input_key="a" * 64)
            self.assertEqual(second.witnesses.counters, {"files_read": 0, "bytes_read": 0, "cache_hits": 4})
            second.close()
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(resume, "digest", side_effect=AssertionError("Transferred proof lost on next restart")):
            third = self.journal("b"); third.close()
        self.assertEqual(third.witnesses.counters, {"files_read": 0, "bytes_read": 0, "cache_hits": 4})

    def test_compatible_migration_without_old_byte_witness_reads_legacy_contracts_once(self):
        self.completed_prefix()
        prior = self.journal(); prior.copies.execute("DELETE FROM validated_file_witnesses"); prior.close()
        with patch.dict(storage._invocation_file_proofs, clear=True):
            second = self.journal("b", compatible_input_key="a" * 64)
            self.assertEqual(second.witnesses.counters["files_read"], 4)
            self.assertGreater(second.witnesses.counters["bytes_read"], 0)
            second.close()
        with patch.dict(storage._invocation_file_proofs, clear=True), patch.object(resume, "digest", side_effect=AssertionError("Legacy migration must persist its actual proof")):
            third = self.journal("b"); third.close()

    def test_changed_old_witness_never_bootstraps_a_compatible_key_migration(self):
        first = self.completed_prefix(); before = first.journal.read_bytes()
        path = self.project / "Assets/loading-resources"; original = path.stat()
        path.write_bytes(b"wrong"); os.utime(path, ns=(original.st_atime_ns, original.st_mtime_ns))
        with patch.dict(storage._invocation_file_proofs, clear=True), self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed"):
            self.journal("b", compatible_input_key="a" * 64)
        self.assertEqual(first.journal.read_bytes(), before)

    def test_old_witness_corruption_forces_byte_qualification_before_transfer(self):
        self.completed_prefix(); previous = self.journal()
        previous.copies.execute("UPDATE validated_file_witnesses SET check_hash=?", ("bad",)); previous.close()
        with patch.dict(storage._invocation_file_proofs, clear=True):
            second = self.journal("b", compatible_input_key="a" * 64)
            self.assertEqual(second.witnesses.counters["files_read"], 4)
            second.close()

    def test_wrong_prior_key_and_other_identity_changes_do_not_reset_or_rebind(self):
        first = self.completed_prefix(); before = first.journal.read_bytes()
        for parameters in ({"compatible_input_key": "c" * 64}, {"compatible_input_key": "invalid"}):
            with self.subTest(parameters=parameters), self.assertRaises(storage.BuildError):
                self.journal("b", reset=lambda: self.fail("Reset before identity guard"), **parameters)
            self.assertEqual(first.journal.read_bytes(), before)
        for target, recipe in (("startup", 1), ("game", 2)):
            with self.subTest(target=target, recipe=recipe), self.assertRaisesRegex(storage.BuildError, "journal identity"):
                resume.Preparation(self.root, self.project, input_key="b" * 64, target=target, recipe=recipe,
                                   compatible_input_key="a" * 64, reset=lambda: self.fail("Reset before guard"))
            self.assertEqual(first.journal.read_bytes(), before)

    def test_corrupt_prefix_rejects_migration_even_when_base_reset_is_available(self):
        first = self.completed_prefix(); before = first.journal.read_bytes()
        self.put("Assets/Quest/Runtime/Probe.cs", b"corrupt template")
        with self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed"):
            self.journal("b", compatible_input_key="a" * 64, reset=lambda: self.fail("Corrupt prefix must not be erased"))
        self.assertEqual(first.journal.read_bytes(), before)

    def test_changed_immutable_source_rejects_migration_before_journal_publication(self):
        source = self.root / "snapshot-original"; source.write_bytes(b"same immutable source")
        sources = [(source, {"size": source.stat().st_size, "sha256": storage.digest(source)})]
        first = self.completed_prefix(sources=sources); before = first.journal.read_bytes()
        source.write_bytes(b"modified source")
        with self.assertRaisesRegex(storage.BuildError, "source changed since its checkpoint"):
            self.journal("b", compatible_input_key="a" * 64, source_files=sources)
        self.assertEqual(first.journal.read_bytes(), before)

    def test_corrupt_tail_rejected_before_trimming_completed_steps(self):
        first = self.completed_prefix(); next_run = self.journal()
        next_run.index = len(next_run.value["steps"])
        with next_run.operation("mod-banks", 1):
            next_run.run("startup-archive", "mod-banks", lambda: self.put("Assets/archive", b"closed archive"), ["Assets/archive"])
        next_run.close(); before = first.journal.read_bytes()
        self.put("Assets/archive", b"corrupt archive")
        with self.assertRaisesRegex(storage.BuildError, "Retained preparation output changed in startup-archive"):
            self.journal("b", compatible_input_key="a" * 64)
        self.assertEqual(first.journal.read_bytes(), before)

    def test_tail_owning_later_prefix_rewrites_is_retained_and_reported_unsupported(self):
        first = self.completed_prefix(); next_run = self.journal(); next_run.index = len(next_run.value["steps"])
        with next_run.operation("mod-banks", 2):
            next_run.run("startup-archive", "mod-banks", lambda: self.put("Assets/archive", b"closed archive"), ["Assets/archive"])
            next_run.run("package-settings", "mod-banks", lambda: self.put("Assets/Quest/Runtime/Probe.cs", b"later accepted template"), ["Assets/Quest/Runtime/Probe.cs"])
        next_run.close(); before = first.journal.read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "later owner.*package-settings"):
            self.journal("b", compatible_input_key="a" * 64)
        self.assertEqual(first.journal.read_bytes(), before)
        self.assertEqual((self.project / "Assets/Quest/Runtime/Probe.cs").read_bytes(), b"later accepted template")

    def test_independent_archive_tail_rebuilds_with_new_real_input_key(self):
        first = self.completed_prefix(); previous = self.journal(); previous.index = len(previous.value["steps"])
        with previous.operation("mod-banks", 1):
            previous.run("startup-archive", "mod-banks", lambda: self.put("Assets/archive", b"a" * 64), ["Assets/archive"])
        previous.close()
        second = self.journal("b", compatible_input_key="a" * 64)
        self.assertEqual(len(second.value["steps"]), 4)
        second.index = len(second.value["steps"])
        with second.operation("mod-banks", 1):
            second.run("startup-archive", "mod-banks", lambda: self.put("Assets/archive", second.identity["inputKey"].encode()), ["Assets/archive"])
        second.finish(); second.close()
        self.assertEqual((self.project / "Assets/archive").read_bytes(), b"b" * 64)
        last = self.journal("b"); last.close()


class PreparationPipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.source, self.game, self.recovered, self.output = [self.root / name for name in ("source", "game", "recovered", "output")]
        for path in (self.source, self.game, self.recovered): path.mkdir()
        self.calls, self.failure = {}, None
        self.write(self.source, "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/Probe.cs", b"class Probe {}")
        self.write(self.source, "unity/GloomhavenVR.Quest/Packages/manifest.json", b'{"dependencies":{}}')
        self.write(self.source, "src/GloomhavenVR/Core/Loc/QuestText.cs", b"class QuestText {}")
        self.write(self.source, "src/GloomhavenVR/Assets/GloomhavenVR_logo.png", b"fixture logo")
        path = self.source / "tools/quest-recovery/case_paths.py"; path.parent.mkdir(parents=True)
        shutil.copyfile(ROOT / "tools/quest-recovery/case_paths.py", path)
        self.write(self.game, "original.bin", b"owned read-only original")
        self.editor = self.root / "Unity"; self.editor.write_bytes(b"fixture Unity tool")
        self.output = storage.ensure_output(self.output, self.source, self.game)
        self.profile_key = "b" * 64
        self.write(self.output, "identities/" + self.profile_key + "/quest-profile.json", b'{"displayName":"fixture"}')
        self.write(self.output, "identities/" + self.profile_key + "/quest-steam-logo.png", b"fixture logo")
        self.report = {"schema": 1, "target": "campaign", "selectedScenes": ["Assets/Scene/Menu.unity"],
                       "readiness": {"fullOriginalCatalogRecovered": True}, "unresolvedAddressables": [], "missingReferences": {}}
        self.document(self.recovered, builder.startup.REPORT, self.report)
        self.document(self.recovered, "Packages/manifest.json", {"dependencies": {}})
        self.write(self.recovered, "ProjectSettings/ProjectSettings.asset", b"PlayerSettings:\n  m_ActiveColorSpace: 0\n  m_BuildTargetGraphicsAPIs: []\n  m_Other: 0\n")
        self.rows = []
        for index, (path, kind, data) in enumerate([
                ("Assets/Texture/Cube.png", 89, b"cube pixels"), ("Assets/Texture/Hdr.png", 28, b"HDR pixels"),
                ("Assets/Audio/bundled.ogg", 83, b"original audio"), ("Assets/Audio/startup.wav", 83, b"startup audio"),
                ("Assets/Shader/Original.shader", 48, b"original shader"), ("Assets/Compute/Original.asset", 72, b"original compute"),
                ("Assets/VideoClip/Intro.mov", 329, b"movie pixels"), ("Assets/Scene/Menu.unity", 1, b"original scene"),
                ("Assets/Material/One.mat", 21, b"ordinary material"), ("Assets/Material/one.mat", 21, b"ordinary material")]):
            guid = format(index + 1, "032x")
            self.write(self.recovered, path, data)
            self.write(self.recovered, path + ".meta", ("fileFormatVersion: 2\nguid: " + guid + "\n").encode())
            self.rows.append({"path": path, "guid": guid, "objects": [{"classId": kind, "collection": "cab-fixture", "pathId": index, "fileId": 1}]})
        for name in ("GH.Runtime", "UnityEngine.UI"):
            self.write(self.recovered, "Assets/Plugins/" + name + ".dll", b"original assembly")
            self.write(self.recovered, "Assets/Plugins/" + name + ".dll.meta", ("guid: " + hashlib.md5(name.encode()).hexdigest() +
                       "\nPluginImporter:\n  isExplicitlyReferenced: 0\n  enabled: 1\n  executionOrder: 0\n").encode())
        self.document(self.recovered, "QuestRecovery/original-asset-identities.json", {"schema": 1, "identities": self.rows})
        self.document(self.recovered, "Assets/QuestOriginalCampaign/campaign-addressables.json", {"schema": 1, "entries": []})
        self.document(self.recovered, "Assets/QuestOriginalCampaign/script-bindings.json", {"schema": 1, "assetPaths": []})
        self.write(self.recovered, "Assets/StreamingAssets/Rulebase/Base.ruleset", b"owned rules")
        mod, game = storage.inventory(self.source), storage.inventory(self.game)
        self.inputs = {"game": {"key": storage.value_hash(game), "files": game, "unityVersion": "2021.3.5f1"},
                       "mod": {"key": storage.value_hash(mod), "files": mod, "modBuild": 638}, "profileKey": self.profile_key,
                       "profile": {"isDummy": True, "dlcOwnership": {"installedAppIds": []}}, "campaignProject": {"key": "c" * 64},
                       "proceduralBackend": "proton-arm64ec-fex", "target": "game"}
        self.inputs["inputKey"] = storage.value_hash(self.inputs)
        self.args = builder.parser().parse_args(["prepare", "--target", "game", "--campaign-project", str(self.recovered),
                                                "--unity-editor", str(self.editor), "--dotnet", sys.executable])
        self.stack = ExitStack()
        for module, name, function in [
            (builder.campaign, "inspect_project", lambda *a, **k: {}), (builder.campaign, "verify_copy", lambda *a, **k: None),
            (builder.full_assets, "repair_reused_stage", lambda *a, **k: None),
            (importlib.import_module("dependencies"), "python_environment", lambda *a, **k: Path(sys.executable)),
            (builder, "toolchain", lambda *a: {"androidNdk": str(self.root / "ndk")}),
            (builder.post_effects, "restore_post_effects", self.post_effects), (builder.startup, "stage_startup_movies", self.movies),
            (importlib.import_module("full_sprites"), "stage", lambda project, *a, **k: self.evidence(project, "sprites", "Assets/QuestOriginalCampaign/native-sprites.json", {"assets": []})),
            (builder, "restore_loading_sprite_geometry", lambda project, *a: self.evidence(project, "loading-sprite", "QuestStartupEvidence/loading-sprite-geometry.json", {"assets": []})),
            (builder, "stage_startup_audio", self.startup_audio),
            (builder.ui_assets, "stage_campaign_recipe_manifest", lambda project: self.evidence(project, "ui-recipes", builder.ui_assets.RECIPES, {"recipes": []})),
            (builder.ui_assets, "stage_startup_ui", lambda project: self.evidence(project, "ui", builder.ui_assets.RECEIPT, {"shaders": [{"assetPath": "Assets/Shader/Original.shader"}]})),
            (builder.ui_assets, "stage_startup_blur", lambda project: self.evidence(project, "blur", "QuestStartupEvidence/original-ui-blur.json", {})),
            (builder.dlcs, "stage", self.dlc), (importlib.import_module("campaign_native"), "stage", self.native),
            (importlib.import_module("campaign_shaders"), "original_cab_bundles", lambda *a: {}),
            (importlib.import_module("full_audio"), "stage", self.audio), (importlib.import_module("full_textures"), "stage", self.cubemaps),
            (importlib.import_module("full_texture2d"), "audit", self.texture_audit),
            (importlib.import_module("full_texture2d"), "restore_float_textures", self.texture2d),
            (importlib.import_module("campaign_compute"), "stage", self.compute),
            (importlib.import_module("campaign_shaders"), "stage", self.shaders),
            (builder, "original_builtin_modules", lambda *a: {}), (builder, "restore_ugui_layout_gate", self.ugui),
            (builder, "package_mod_content", self.mod_banks), (builder.startup, "stage_startup_script_orders", self.orders)]:
            self.stack.enter_context(patch.object(module, name, function))

    def tearDown(self): self.stack.close(); self.temp.cleanup()
    def write(self, root, name, data):
        path = root / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(data); return path
    def document(self, root, name, value): storage.write_json(root / name, {"schema": 1, **value}); return value
    def tick(self, name): self.calls[name] = self.calls.get(name, 0) + 1
    def evidence(self, project, name, path, value): self.tick(name); return self.document(project, path, value)
    def asset(self, project, name): return {"assetPath": name, "sha256": storage.digest(project / name)}
    def post_effects(self, project, *_):
        self.write(project, "Assets/Shader/Original.shader", b"portable post-effect")
        return self.evidence(project, "post-effects", builder.post_effects.RECEIPT, {"shaders": [self.asset(project, "Assets/Shader/Original.shader")]})
    def movies(self, project, *_):
        self.write(project, "Assets/Scene/Menu.unity", b"file-backed movie scene")
        old = project / "Assets/VideoClip/Intro.mov"; old.unlink(); Path(str(old) + ".meta").unlink()
        self.write(project, "Assets/StreamingAssets/Movies/Intro.mov", b"movie pixels")
        return self.evidence(project, "movies", "Assets/Quest/Resources/" + builder.startup.MOVIES_REPORT,
                             {"clips": [{"path": "StreamingAssets/Movies/Intro.mov", "source": "Assets/VideoClip/Intro.mov", "bindings": [{"sourceScene": "Assets/Scene/Menu.unity"}]}]})
    def startup_audio(self, project, *_):
        self.write(project, "Assets/Audio/startup.wav", b"fixed original WAV")
        self.document(project, builder.AUDIO_RESOURCE, {})
        return self.evidence(project, "startup-audio", builder.AUDIO_REPORT, {"clips": [self.asset(project, "Assets/Audio/startup.wav")]})
    def dlc(self, project, ownership):
        self.document(project, "Assets/Quest/Resources/quest-dlc-ownership.json", ownership)
        return self.evidence(project, "dlc", "QuestStartupEvidence/dlc-content-selection.json", {})
    def native(self, source, project, *_a, **_k):
        self.write(project, "Assets/Quest/Plugins/Android/arm64-v8a/runtime.so", b"fixture native")
        payload = [{"path": "guest.bin", "sha256": hashlib.sha256(b"guest bytes").hexdigest(), "size": 11}]
        for name in ("Assets/Quest/Resources/quest-procedural-runtime.json", "Assets/StreamingAssets/ProceduralRuntime/runtime-manifest.json"):
            self.document(project, name, {"files": payload})
        self.write(project, "Assets/StreamingAssets/ProceduralRuntime/guest.bin", b"guest bytes")
        for name in ("Assets/Quest/Resources/quest-procedural-native.json", "Assets/StreamingAssets/Quest/procedural-native.json"): self.document(project, name, {})
        return self.evidence(project, "native", "QuestCampaignEvidence/native-runtime.json", {"nativeFiles": [{"path": "Assets/Quest/Plugins/Android/arm64-v8a/runtime.so"}]})
    def audio(self, project, *_a, **_k):
        self.write(project, "Assets/Audio/bundled.ogg", b"portable original Vorbis")
        return self.evidence(project, "audio", "Assets/QuestOriginalCampaign/bundled-audio.json", {"assets": [self.asset(project, "Assets/Audio/bundled.ogg")]})
    def convert(self, project, old, new, data):
        self.write(project, new, data); self.write(project, new + ".meta", (project / (old + ".meta")).read_bytes())
        (project / old).unlink(); (project / (old + ".meta")).unlink()
        path = project / "QuestRecovery/original-asset-identities.json"; document = json.loads(path.read_text())
        for row in document["identities"]:
            if row["path"] == old: row["path"] = new
        storage.write_json(path, document)
        return {"assets": [self.asset(project, new)], "pathMap": {old: new}, "updatedManifests": [{"path": "QuestRecovery/original-asset-identities.json"}]}
    def cubemaps(self, project, *_a, **_k):
        value = self.convert(project, "Assets/Texture/Cube.png", "Assets/Texture/Cube.asset", b"native cube")
        self.document(project, "Assets/QuestOriginalCampaign/native-platform-images.json", {"assets": []})
        self.document(project, "Assets/QuestOriginalCampaign/native-texture-references.json", {"owners": []})
        return self.evidence(project, "cubemaps", "Assets/QuestOriginalCampaign/native-cubemaps.json", value)
    def texture_audit(self, project, *_a, output=None, **_k):
        return self.evidence(project, "texture-audit", output.relative_to(project).as_posix(), {"assets": [{"assetPath": "Assets/Texture/Hdr.png", "nativeFloatOrHdr": True}]})
    def texture2d(self, project, *_a, **_k):
        self.tick("textures")
        value = self.convert(project, "Assets/Texture/Hdr.png", "Assets/Texture/Hdr.texture2D", b"native float texture")
        if self.failure == "textures": raise storage.BuildError("fixture textures failure")
        return self.document(project, "Assets/QuestOriginalCampaign/native-texture2d.json", value)
    def compute(self, source, project, *_):
        value = self.convert(project, "Assets/Compute/Original.asset", "Assets/Compute/Original.compute", b"original compute instructions")
        self.document(project, "QuestStartupEvidence/compute-source-restoration.json", {})
        return self.evidence(project, "compute", "Assets/QuestOriginalCampaign/campaign-computes.json", value)
    def shaders(self, source, project, *_):
        self.tick("shaders")
        self.write(project, "Assets/Shader/Original.shader", b"all native shader instructions")
        self.write(project, "Assets/QuestOriginalCampaign/ShaderPrograms/native.hlsl", b"native program")
        if self.failure == "graphics": raise storage.BuildError("fixture graphics failure")
        self.document(project, "QuestCampaignEvidence/shader-reconstruction.json", {})
        return self.document(project, "Assets/QuestOriginalCampaign/campaign-shaders.json", {"shaders": [self.asset(project, "Assets/Shader/Original.shader")],
                             "programs": [self.asset(project, "Assets/QuestOriginalCampaign/ShaderPrograms/native.hlsl")]})
    def ugui(self, project, *_):
        self.write(project, "Packages/com.unity.ugui/Runtime/UI/Core/Layout/LayoutRebuilder.cs", b"Enable guard")
        self.evidence(project, "ugui", "QuestStartupEvidence/ugui-layout-gate.json", {})
    def mod_banks(self, project, *_):
        self.tick("mod-banks")
        if self.failure == "mod-banks": raise storage.BuildError("fixture mod-bank failure")
        self.write(project, "Assets/StreamingAssets/quest-mod-content.zip", b"mod bank bytes")
        self.document(project, "Assets/Quest/Resources/quest-mod-content.json", {})
        self.document(project, "Assets/Quest/Resources/quest-mod-bundles.json", {})
    def orders(self, project, *_):
        self.tick("script-orders")
        meta = project / "Assets/Plugins/GH.Runtime.dll.meta"
        meta.write_bytes(meta.read_bytes().replace(b"executionOrder: 0", b"executionOrder: 100"))
        if self.failure == "orders": raise storage.BuildError("fixture script-orders failure")
        self.document(project, "Assets/QuestOriginalStartup/script-orders.json", {})
        return self.document(project, "QuestStartupEvidence/script-orders-source.json", {"plugins": [{"path": meta.relative_to(project).as_posix()}]})
    def run_prepare(self): return builder.prepare(self.args, self.inputs, self.output, self.source, self.game)
    def project(self): return self.output / "projects" / builder.import_workspace.workspace_key(self.inputs, "game")

    def test_real_prepare_graphics_failure_retains_completed_native_audio_compute_and_library(self):
        self.failure = "graphics"
        with self.assertRaisesRegex(storage.BuildError, "graphics failure"): self.run_prepare()
        imported = self.write(self.project(), "Library/imported.bin", b"expensive Unity Library")
        first = dict(self.calls)
        self.failure = None; project = self.run_prepare()
        for name in ("native", "audio", "compute", "movies", "texture-audit", "textures"):
            self.assertEqual(self.calls[name], first[name], name)
        self.assertEqual(self.calls["shaders"], 2)
        self.assertEqual(imported.read_bytes(), b"expensive Unity Library")
        self.assertEqual(self.run_prepare(), project)
        self.assertEqual(self.calls["shaders"], 2)
        self.assertEqual((self.game / "original.bin").read_bytes(), b"owned read-only original")

    def test_real_prepare_texture_failure_retries_only_float_restore_not_audit_cube_native_or_audio(self):
        self.failure = "textures"
        with self.assertRaisesRegex(storage.BuildError, "textures failure"): self.run_prepare()
        self.failure = None; self.run_prepare()
        for name in ("cubemaps", "texture-audit", "audio", "native"): self.assertEqual(self.calls[name], 1, name)
        self.assertEqual(self.calls["textures"], 2)
        self.assertEqual((self.project() / "Assets/Texture/Hdr.texture2D").read_bytes(), b"native float texture")

    def test_real_prepare_mod_bank_failure_keeps_archive_cleanup_and_ugui(self):
        self.failure = "mod-banks"
        with self.assertRaisesRegex(storage.BuildError, "mod-bank failure"): self.run_prepare()
        archive = self.project() / storage.CONTENT_PATHS[0]
        checksum, stamp = storage.digest(archive), archive.stat().st_mtime_ns
        self.assertFalse((self.project() / "Assets/StreamingAssets/Rulebase/Base.ruleset").exists())
        self.failure = None; self.run_prepare()
        self.assertEqual((storage.digest(archive), archive.stat().st_mtime_ns), (checksum, stamp))
        self.assertEqual(self.calls["ugui"], 1); self.assertEqual(self.calls["mod-banks"], 2)

    def test_real_case_migration_and_importer_edits_survive_later_failure_and_cold_completed_recheck(self):
        self.failure = "orders"
        with self.assertRaisesRegex(storage.BuildError, "script-orders failure"): self.run_prepare()
        project = self.project(); migrated = json.loads((project / "QuestStartupEvidence/case-path-migration.json").read_text())
        self.assertGreater(len(migrated["files"]), 0)
        before = storage.digest(project / "QuestStartupEvidence/case-path-migration.json")
        imported = self.write(project, "Library/imported.bin", b"keep warm")
        self.failure = None; self.run_prepare()
        # Force the real outer preparation to reconsider its durable substages;
        # all later ownership changes remain valid after actual case renames,
        # compiler importer edits, script order and final PlayerSettings changes.
        key = storage.value_hash({"input": self.inputs["inputKey"], "recipe": builder.RECIPE})
        storage.Stages(self.output).path("prepare", key).unlink()
        counts = dict(self.calls); self.run_prepare()
        self.assertEqual(self.calls, counts)
        self.assertEqual(storage.digest(project / "QuestStartupEvidence/case-path-migration.json"), before)
        self.assertIn(b"isExplicitlyReferenced: 1", (project / "Assets/Plugins/GH.Runtime.dll.meta").read_bytes())
        self.assertIn(b"executionOrder: 100", (project / "Assets/Plugins/GH.Runtime.dll.meta").read_bytes())
        self.assertEqual(imported.read_bytes(), b"keep warm")

    def test_actual_accepted_content_repack_survives_prepare_recheck_without_rehashing_archive(self):
        project = self.run_prepare()
        archive, manifest = [project / name for name in storage.CONTENT_PATHS]
        with storage.project_content_transaction(self.output, project, self.inputs["inputKey"]):
            archive.write_bytes(archive.read_bytes() + b"accepted native repack")
            content = json.loads(manifest.read_text())
            content["archiveSha256"] = storage.digest(archive)
            storage.write_json(manifest, content)
        accepted = archive.read_bytes()
        counts = dict(self.calls)
        key = storage.value_hash({"input": self.inputs["inputKey"], "recipe": builder.RECIPE})
        storage.Stages(self.output).path("prepare", key).unlink()
        with patch.object(resume, "digest", wraps=resume.digest) as hashed:
            self.run_prepare()
            self.assertNotIn(archive, [call.args[0] for call in hashed.call_args_list])
        self.assertEqual(self.calls, counts)
        self.assertEqual(archive.read_bytes(), accepted)


if __name__ == "__main__": unittest.main()
