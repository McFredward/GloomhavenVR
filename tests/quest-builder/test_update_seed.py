"""Exact original IL survives portable updates; corrupt caches repair locally.

Synthetic DLL byte fixtures exercise the real producer/archive/receipt paths.
They do not establish IL2CPP recompilation or Android/headset installation.
"""
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import storage
import update_seed as seed


class UpdateSeedTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game, self.output = self.root / "owned-game", self.root / "output"
        self.inputs = {"inputKey": "a" * 64, "game": {"files": []},
                       "profile": {"displayName": "Private local user"}}
        self.builder = SimpleNamespace(BuildError=storage.BuildError, _ordinary_owned=storage._ordinary_owned,
                                       write_json=storage.write_json)
        self.data = {"Managed/GH.Runtime.dll": b"MZ-original-runtime\0" * 351,
                     "Managed/GH.Shared.dll": b"MZ-original-shared\0" * 111,
                     "Managed/UnityEngine.CoreModule.dll": b"MZ-original-unity\0" * 219}
        self.add_files(self.data)
        # These bytes must never become a seed member or seed manifest field.
        self.private = self.game / "private-signing.key"
        self.private.write_bytes(b"Not a compiler input")
        self.inputs["game"]["files"].append(storage.record_file(self.private, self.private.name))
        self.apk = self.root / "installed.apk"
        self.base = {"inputKey": self.inputs["inputKey"], "manifest": self.inputs}

    def add_files(self, values):
        for name, value in values.items():
            path = self.game / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(value)
            self.inputs["game"]["files"].append(storage.record_file(path, name))

    def delivery(self):
        values = seed.build(self.builder, self.game, self.inputs, self.output)
        return {name: value.read_bytes() if isinstance(value, Path) else value for name, value in values.items()}

    def write_apk(self, delivery=None):
        values = self.delivery() if delivery is None else delivery
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_STORED) as stream:
            for name, raw in {**values, "lib/arm64-v8a/libil2cpp.so": b"Synthetic retained player",
                              "assets/Quest/retained-assets.bin": b"Synthetic retained asset bank"}.items():
                stream.writestr(name, raw)

    def extract(self):
        return seed.extract(self.builder, self.apk, self.base, self.output)

    def rewrite_seed(self, pairs):
        delivery = self.delivery()
        payload = io.BytesIO()
        with zipfile.ZipFile(payload, "w", compression=zipfile.ZIP_DEFLATED) as stream:
            with warnings.catch_warnings():
                warnings.simplefilter("ignore", UserWarning)
                for name, raw in pairs:
                    stream.writestr(name, raw)
        delivery[seed.MEMBER] = payload.getvalue()
        metadata = json.loads(delivery[seed.MANIFEST])
        metadata.update(archiveBytes=len(payload.getvalue()), archiveSha256=hashlib.sha256(payload.getvalue()).hexdigest())
        delivery[seed.MANIFEST] = storage.canonical(metadata)
        self.write_apk(delivery)

    def test_seed_contains_only_exact_original_dlls_and_is_deterministic(self):
        first = self.delivery()
        second = seed.build(self.builder, self.game, self.inputs, self.root / "independent-output")
        self.assertEqual(first[seed.MEMBER], second[seed.MEMBER].read_bytes())
        self.assertEqual(first[seed.MANIFEST], second[seed.MANIFEST])
        with zipfile.ZipFile(io.BytesIO(first[seed.MEMBER])) as archive:
            self.assertEqual(set(self.data), set(archive.namelist()))
            for name, expected in self.data.items():
                self.assertEqual(expected, archive.read(name))
        self.assertNotIn(b"Private local user", first[seed.MANIFEST])
        self.assertNotIn(b"private-signing", first[seed.MANIFEST])
        self.write_apk(first)
        self.assertEqual("available", seed.inspect_seed(self.apk, self.base)["state"])
        result = self.extract()
        self.assertEqual(3, result["repairedFiles"])
        self.assertEqual(sum(map(len, self.data.values())), result["bytes"])
        for name, expected in self.data.items():
            self.assertEqual(expected, (Path(result["dataRoot"]) / name).read_bytes())

    def test_old_installed_apk_describes_one_time_pc_seed_requirement(self):
        self.write_apk({})
        with patch.object(seed, "_hash_file", side_effect=AssertionError("Legacy discovery must not scan assets")):
            result = self.extract()
        self.assertEqual("missing", result["state"])
        self.assertIn("Update it once", result["reason"])
        self.assertFalse(self.output.exists())

    def test_seed_identity_is_stable_across_reuse_and_new_apk_code(self):
        self.write_apk()
        first = self.extract()
        self.write_apk()
        with patch.object(seed, "_hash_file", side_effect=AssertionError("No full byte reads for unchanged closed files")), \
             patch.object(seed, "_copy_checked", side_effect=AssertionError("No extraction of unchanged closed files")):
            second = self.extract()
            self.delivery()
        self.assertEqual(first["proof"], second["proof"])
        self.assertEqual(3, second["reusedFiles"])
        self.assertEqual(0, second["repairedFiles"])

    def test_repair_changes_only_the_single_bad_cached_assembly(self):
        self.write_apk()
        first = self.extract()
        bad = Path(first["dataRoot"]) / "Managed/GH.Runtime.dll"
        old_time = bad.stat().st_mtime_ns
        bad.write_bytes(b"X" * bad.stat().st_size)
        os.utime(bad, ns=(old_time, old_time))
        with patch.object(seed, "_copy_checked", wraps=seed._copy_checked) as copied:
            second = self.extract()
        self.assertEqual(1, copied.call_count)
        self.assertEqual(bad, copied.call_args.args[1])
        self.assertEqual(self.data["Managed/GH.Runtime.dll"], bad.read_bytes())
        self.assertEqual((2, 1), (second["reusedFiles"], second["repairedFiles"]))

    def test_metadata_change_with_unchanged_bytes_rehashes_only_one_file(self):
        self.write_apk()
        result = self.extract()
        path = Path(result["dataRoot"]) / "Managed/GH.Shared.dll"
        value = path.stat()
        os.utime(path, ns=(value.st_atime_ns, value.st_mtime_ns + 10000000))
        with patch.object(seed, "_hash_file", wraps=seed._hash_file) as hashed, \
             patch.object(seed, "_copy_checked", side_effect=AssertionError("Same bytes need no extraction")):
            self.extract()
        self.assertEqual([path], [item.args[0] for item in hashed.call_args_list])

    def test_missing_proof_requalifies_bounded_files_without_copying_again(self):
        self.write_apk()
        result = self.extract()
        receipt = Path(result["dataRoot"]).parent / "extract-proof.json"
        receipt.write_text("interrupted JSON{")
        with patch.object(seed, "_copy_checked", side_effect=AssertionError("Keep good existing bytes")), \
             patch.object(seed, "_hash_file", wraps=seed._hash_file) as hashed:
            repaired = self.extract()
        self.assertEqual(4, hashed.call_count)
        self.assertEqual(3, repaired["reusedFiles"])

    def test_unknown_filesystem_stamps_use_hashes_instead_of_false_fast_trust(self):
        self.write_apk()
        self.extract()
        with patch.object(seed._storage(), "_file_witness_stamp", return_value=None), \
             patch.object(seed, "_hash_file", wraps=seed._hash_file) as hashed:
            self.extract()
        self.assertEqual(4, hashed.call_count)

    def test_corrupt_cached_archive_repairs_it_without_recopied_dlls(self):
        self.write_apk()
        result = self.extract()
        archive = Path(result["dataRoot"]).parent / "update-seed.zip"
        archive.write_bytes(b"x" * archive.stat().st_size)
        with patch.object(seed, "_copy_checked", wraps=seed._copy_checked) as copied:
            repaired = self.extract()
        self.assertEqual(1, copied.call_count)
        self.assertEqual(archive, copied.call_args.args[1])
        self.assertEqual(3, repaired["reusedFiles"])

    def test_partial_extraction_resumes_completed_files_without_recopy(self):
        self.add_files({"Managed/Fixture" + str(i) + ".dll": b"MZ" + bytes([i]) * 25 for i in range(20)})
        self.write_apk()
        def interrupted(index, total, name):
            if index == 18:
                raise RuntimeError("Synthetic interrupted operation")
        with self.assertRaisesRegex(storage.BuildError, "interrupted"):
            seed.extract(self.builder, self.apk, self.base, self.output, progress=interrupted)
        with patch.object(seed, "_copy_checked", wraps=seed._copy_checked) as copied:
            result = self.extract()
        self.assertEqual(5, copied.call_count)
        self.assertEqual((18, 5), (result["reusedFiles"], result["repairedFiles"]))

    def test_original_source_hash_mismatch_does_not_produce_an_archive(self):
        (self.game / "Managed/GH.Runtime.dll").write_bytes(b"MZ changed original")
        with self.assertRaisesRegex(storage.BuildError, "Original assembly changed"):
            self.delivery()
        self.assertEqual([], list(self.output.rglob("update-seed.zip")))

    def test_unsupported_non_dll_managed_input_is_not_silently_omitted(self):
        self.inputs["game"]["files"].append({"path": "Managed/Required.resources", "size": 5, "sha256": "b" * 64})
        with self.assertRaisesRegex(storage.BuildError, "unsupported assembly path"):
            self.delivery()

    def test_foreign_or_excessive_manifest_never_creates_extraction_cache(self):
        delivery = self.delivery()
        for mutation in (lambda value: value.update(inputKey="b" * 64),
                         lambda value: value["files"][0].update(size=seed.MAX_MEMBER + 1),
                         lambda value: value.update(profile={"displayName": "Unexpected"})):
            with self.subTest(mutation=mutation):
                changed = json.loads(delivery[seed.MANIFEST])
                mutation(changed)
                self.write_apk({**delivery, seed.MANIFEST: storage.canonical(changed)})
                self.assertEqual("invalid", seed.inspect_seed(self.apk, self.base)["state"])
                with self.assertRaises(storage.BuildError):
                    self.extract()
        self.assertFalse((self.output / "cache/device-update-seeds").exists())

    def test_outer_seed_archive_digest_is_required_before_any_dll_write(self):
        delivery = self.delivery()
        delivery[seed.MEMBER] = b"X" * len(delivery[seed.MEMBER])
        self.write_apk(delivery)
        with self.assertRaisesRegex(storage.BuildError, "SHA-256"):
            self.extract()
        self.assertFalse(list((self.output / "cache/device-update-seeds").rglob("*.dll")))

    def test_each_extracted_dll_must_match_the_immutable_original_hash(self):
        pairs = [(name, b"X" * len(raw) if name.endswith("GH.Runtime.dll") else raw) for name, raw in self.data.items()]
        self.rewrite_seed(pairs)
        self.assertTrue(seed.inspect_seed(self.apk, self.base)["available"])
        with self.assertRaisesRegex(storage.BuildError, "SHA-256"):
            self.extract()
        self.assertFalse(list((self.output / "cache/device-update-seeds").rglob("GH.Runtime.dll")))

    def test_nested_zip_roster_rejects_traversal_duplicates_reserved_and_links(self):
        for malicious in ("Managed/../escape.dll", "Managed/CON.dll", "Managed/GH.RUNTIME.dll", "../escape.dll",
                          "Managed/Other.dll", "Managed\\Other.dll"):
            with self.subTest(member=malicious):
                self.rewrite_seed([(malicious, self.data["Managed/GH.Runtime.dll"]), *list(self.data.items())[1:]])
                with self.assertRaises(storage.BuildError):
                    self.extract()
        link = zipfile.ZipInfo("Managed/GH.Runtime.dll")
        link.create_system = 3
        link.external_attr = (stat.S_IFLNK | 0o777) << 16
        self.rewrite_seed([(link, self.data["Managed/GH.Runtime.dll"]), *list(self.data.items())[1:]])
        with self.assertRaisesRegex(storage.BuildError, "links"):
            self.extract()
        self.assertFalse((self.root / "escape.dll").exists())

    def test_duplicate_exact_assembly_entries_cannot_replace_a_missing_original(self):
        runtime = ("Managed/GH.Runtime.dll", self.data["Managed/GH.Runtime.dll"])
        self.rewrite_seed([runtime, runtime, ("Managed/GH.Shared.dll", self.data["Managed/GH.Shared.dll"])])
        with self.assertRaisesRegex(storage.BuildError, "immutable original member roster"):
            self.extract()

    def test_build_cache_manifest_cannot_add_identity_or_private_fields(self):
        self.delivery()
        manifest = next((self.output / "cache/update-seeds").rglob("update-seed.json"))
        value = json.loads(manifest.read_bytes())
        value["unexpectedPrivateField"] = "Not a compiler input"
        storage.write_json(manifest, value)
        refreshed = self.delivery()
        self.assertNotIn(b"unexpectedPrivateField", refreshed[seed.MANIFEST])

    def test_duplicate_outer_members_and_partial_seed_are_invalid(self):
        delivery = self.delivery()
        self.write_apk(delivery)
        with zipfile.ZipFile(self.apk, "a") as archive:
            archive.writestr(seed.MEMBER.upper(), delivery[seed.MEMBER])
        self.assertEqual("invalid", seed.inspect_seed(self.apk, self.base)["state"])
        self.write_apk({seed.MANIFEST: delivery[seed.MANIFEST]})
        self.assertEqual("invalid", seed.inspect_seed(self.apk, self.base)["state"])

    def test_duplicate_json_fields_are_not_an_identity_override(self):
        delivery = self.delivery()
        raw = delivery[seed.MANIFEST]
        delivery[seed.MANIFEST] = b'{"inputKey":"' + b"b" * 64 + b'",' + raw[1:]
        self.write_apk(delivery)
        self.assertIn("duplicate keys", seed.inspect_seed(self.apk, self.base)["reason"])

    def test_unowned_extraction_root_is_never_overwritten(self):
        self.write_apk()
        result = self.extract()
        parent = Path(result["dataRoot"]).parent
        marker = parent / ".owner.json"
        marker.unlink()
        original = (parent / "game/Managed/GH.Runtime.dll").read_bytes()
        with self.assertRaisesRegex(storage.BuildError, "no ownership record"):
            self.extract()
        self.assertEqual(original, (parent / "game/Managed/GH.Runtime.dll").read_bytes())

    def test_foreign_owner_blocks_a_cache_even_when_all_dlls_are_correct(self):
        self.write_apk()
        result = self.extract()
        marker = Path(result["dataRoot"]).parent / ".owner.json"
        marker.write_text('{"scope":"Another tool"}')
        with self.assertRaisesRegex(storage.BuildError, "different inputs"):
            self.extract()

    def test_hardlink_or_symlink_cannot_redirect_a_targeted_repair(self):
        if not hasattr(os, "link"):
            self.skipTest("Hard links unavailable")
        self.write_apk()
        result = self.extract()
        target = Path(result["dataRoot"]) / "Managed/GH.Runtime.dll"
        target.unlink()
        os.link(self.private, target)
        before = self.private.read_bytes()
        with self.assertRaises(storage.BuildError):
            self.extract()
        self.assertEqual(before, self.private.read_bytes())
        target.unlink()
        if hasattr(os, "symlink"):
            target.symlink_to(self.private)
            with self.assertRaises(storage.BuildError):
                self.extract()
            self.assertEqual(before, self.private.read_bytes())

    def test_source_change_during_streaming_is_not_published(self):
        source = self.game / "Managed/GH.Runtime.dll"
        real_open = Path.open
        class ChangingReader:
            def __init__(self, stream): self.stream = stream
            def __enter__(self): return self
            def __exit__(self, *args): return self.stream.__exit__(*args)
            def read(self, size):
                value = self.stream.read(size)
                if not value:
                    descriptor = os.open(source, os.O_WRONLY)
                    try: os.write(descriptor, b"Changed original")
                    finally: os.close(descriptor)
                return value
        def opening(path, mode="r", *args, **kwargs):
            result = real_open(path, mode, *args, **kwargs)
            return ChangingReader(result) if path == source and mode == "rb" else result
        with patch.object(Path, "open", opening), self.assertRaisesRegex(storage.BuildError, "changed while packaging"):
            self.delivery()
        self.assertEqual([], list(self.output.rglob("update-seed.zip")))


if __name__ == "__main__":
    unittest.main()
