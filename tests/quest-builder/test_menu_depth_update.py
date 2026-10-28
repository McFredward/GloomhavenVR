"""Real native MiniAPK -> retained mod ZIP -> supplied Story -> consumer proof."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import struct
import sys
import tempfile
from types import ModuleType, SimpleNamespace
import unittest
from unittest.mock import patch
import zipfile

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
HELPER_ROOT = Path(os.environ.get("GHVR_MENU_HELPER_SOURCE", ROOT))
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import apk_update
import menu_depth_update as update
import storage
from test_script_roster_conflicts_205558 import serialized


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


UNITY_AVAILABLE = importlib.util.find_spec("UnityPy") is not None
MANAGER = "assets/bin/Data/globalgamemanagers"
RESOURCE = "assets/bin/Data/resources.assets"
CAMPAIGN = "assets/quest-campaign-content.zip"


@unittest.skipUnless(UNITY_AVAILABLE, "Pinned UnityPy is required for real native MiniAPK fixtures")
class MenuDepthUpdateTests(unittest.TestCase):
    def setUp(self):
        from UnityPy.enums import ClassIDType
        self.kinds = ClassIDType
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source, self.output = self.root / "source", self.root / "output"
        self.apk, self.original = self.root / "base.apk", self.root / "original-mod.zip"
        self.resources = load("menu_update_fixture_resources", HELPER_ROOT / "tools/quest-builder/menu_depth_resources.py")
        self.content = load("menu_update_fixture_content", HELPER_ROOT / "tools/quest-builder/menu_depth_content.py")
        self.contract = self.content._installation()
        self.builder = SimpleNamespace(BuildError=storage.BuildError, _ordinary_owned=storage._ordinary_owned,
            digest=storage.digest, value_hash=storage.value_hash, write_json=storage.write_json, Stages=storage.Stages,
            apk_updates=apk_update, menu_depth_resources=self.resources, menu_depth_content=self.content)
        self.banks = {name: ("unchanged original mod bank " + name).encode() * 100 for name in self.contract.MOD_PATHS}
        with zipfile.ZipFile(self.original, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, body in self.banks.items(): archive.writestr(name, body)
        records = [{"path": name, "size": len(body), "sha256": self.sha(body)} for name, body in self.banks.items()]
        self.manifest = {"schema": 1, "inputKey": "a" * 64, "archive": update.ARCHIVE_NAME,
                         "archiveSha256": storage.digest(self.original), "files": records}
        receipt_rows = [{**row, "path": Path(row["path"]).name} for row in records]
        self.receipt = {"schema": 1, "bundle": receipt_rows[0], "bundles": receipt_rows,
                        "sourceFiles": [{"path": "Assets/original-bank-fixture", "size": 1, "sha256": "b" * 64}]}
        self.base = {"installation": {"mod": copy.deepcopy(self.manifest)}}
        story = self.source / self.resources.STORY
        story.mkdir(parents=True)
        body = b"supplied original-art numeric depth"
        name = "1" * 32 + ".gmd"
        (story / name).write_bytes(body)
        storage.write_json(story / "manifest.json", {"schema": 1, "status": "complete", "format": "GMD1",
            "images": [{"source_id": "1" * 32, "depth_file": name, "depth_bytes": len(body), "depth_sha256": self.sha(body)}]})
        self.inputs = {"mod": {"files": [storage.record_file(path, path.relative_to(self.source).as_posix())
                                        for path in sorted(story.iterdir())]}}
        self.write_apk()

    @staticmethod
    def sha(body): return hashlib.sha256(body).hexdigest()

    def native_resources(self):
        return serialized([(7 + i, self.kinds.TextAsset, {"m_Name": name, "m_Script": json.dumps(value)})
            for i, (name, value) in enumerate((("quest-mod-content", self.manifest), ("quest-mod-bundles", self.receipt)))])

    def write_apk(self, archive_body=None, native=None):
        manager = {"m_Container": [(name, {"m_FileID": 1, "m_PathID": 7 + i})
            for i, name in enumerate(("quest-mod-content", "quest-mod-bundles"))], "m_DependentAssets": []}
        with zipfile.ZipFile(self.apk, "w", compression=zipfile.ZIP_STORED) as apk:
            apk.writestr(MANAGER, serialized([(1, self.kinds.ResourceManager, manager)], ["resources.assets"]))
            apk.writestr(RESOURCE, self.native_resources() if native is None else native)
            apk.writestr(update.ARCHIVE_MEMBER, self.original.read_bytes() if archive_body is None else archive_body)
            apk.writestr(CAMPAIGN, b"original Campaign must remain unopened")

    def corrupt_member_crc(self, member):
        with zipfile.ZipFile(self.apk) as apk: info = apk.getinfo(member)
        raw = bytearray(self.apk.read_bytes())
        name, extra = struct.unpack_from("<HH", raw, info.header_offset + 26)
        raw[info.header_offset + 30 + name + extra] ^= 1
        self.apk.write_bytes(raw)

    def deliver(self, **kwargs):
        return update.delivery(self.builder, self.source, self.inputs, self.output, self.apk, self.base, **kwargs)

    def retained(self): return self.output / "cache/retained-mod-content" / self.manifest["archiveSha256"]

    @staticmethod
    def compressed(path, name):
        with zipfile.ZipFile(path) as archive, path.open("rb") as raw:
            info = archive.getinfo(name)
            raw.seek(info.header_offset)
            header = raw.read(30)
            name_size, extra_size = struct.unpack_from("<HH", header, 26)
            raw.seek(name_size + extra_size, 1)
            return raw.read(info.compress_size)

    def test_real_native_delivery_preserves_original_art_and_never_reads_campaign(self):
        self.corrupt_member_crc(CAMPAIGN)
        with zipfile.ZipFile(self.apk) as apk:
            with self.assertRaises(zipfile.BadZipFile): apk.read(CAMPAIGN)
        before = self.apk.read_bytes(), copy.deepcopy(self.base)
        archive, manifest, receipt, fast = self.deliver()
        self.assertEqual(manifest["files"][:len(self.banks)], self.manifest["files"])
        self.assertEqual(receipt["bundles"], self.receipt["bundles"])
        for name in self.banks: self.assertEqual(self.compressed(archive, name), self.compressed(self.original, name))
        self.assertEqual(manifest["archiveSha256"], storage.digest(archive))
        with zipfile.ZipFile(archive) as actual:
            self.assertEqual(actual.namelist(), [row["path"] for row in manifest["files"]])
            self.contract.validate_story_rows(manifest["files"][len(self.banks):], actual.read(self.contract.STORY_MANIFEST))
        owner = json.loads((self.retained() / "owner.json").read_text())
        self.assertEqual(owner, {"schema": 1, "scope": update.SCOPE, "archiveSha256": self.manifest["archiveSha256"],
            "files": [{"path": update.ARCHIVE_NAME, "size": self.original.stat().st_size, "sha256": self.manifest["archiveSha256"]}]})
        self.assertTrue(storage.Stages(self.output).valid("retained-mod-content", self.manifest["archiveSha256"]))
        self.assertEqual(set(fast), {self.retained().relative_to(self.output).as_posix(), archive.parent.relative_to(self.output).as_posix()})
        self.assertEqual(before, (self.apk.read_bytes(), self.base))

    def test_warm_reuse_reads_no_apk_mod_payload_and_does_not_rewrite_story(self):
        first = self.deliver()
        original_open, original_digest = zipfile.ZipFile.open, self.builder.digest
        retained = self.retained() / update.ARCHIVE_NAME
        def guarded_open(archive, name, *args, **kwargs):
            if Path(archive.filename) == self.apk and getattr(name, "filename", name) == update.ARCHIVE_MEMBER:
                raise AssertionError("warm retained archive must not read the APK mod payload")
            return original_open(archive, name, *args, **kwargs)
        def guarded_digest(path):
            if Path(path) == retained: raise AssertionError("native warm witnesses must not rescan the retained archive")
            return original_digest(path)
        with patch.object(zipfile.ZipFile, "open", guarded_open), patch.object(self.builder, "digest", guarded_digest), \
             patch.object(self.resources, "write_story", side_effect=AssertionError("closed Story stage must stay warm")):
            self.assertEqual(self.deliver(), first)

    def test_delivered_archive_and_native_contracts_pass_the_actual_installation_consumer(self):
        archive, manifest, receipt, _ = self.deliver()
        prior = set(apk_update._resource_proofs)
        def clear_proofs():
            for key in apk_update._resource_proofs - prior:
                apk_update._resource_proofs.discard(key); apk_update._resource_value_proofs.pop(key, None)
        self.addCleanup(clear_proofs)
        replacements = apk_update.patch_resource_json(self.apk,
            {"quest-mod-content": manifest, "quest-mod-bundles": receipt})
        game = self.root / "unchanged-adjacent-game.zip"
        body, name = b"original Rulebase fixture", "StreamingAssets/Rulebase/Campaign.ruleset"
        with zipfile.ZipFile(game, "w") as external: external.writestr(name, body)
        game_manifest = {"schema": 1, "inputKey": self.manifest["inputKey"], "archive": "quest-startup-content.zip",
            "archiveSha256": storage.digest(game), "externalDelivery": True,
            "files": [{"path": name, "size": len(body), "sha256": self.sha(body)}]}
        delivered = self.root / "delivered-fixture.apk"
        with zipfile.ZipFile(self.apk) as original, zipfile.ZipFile(delivered, "w") as final:
            final.writestr(MANAGER, original.read(MANAGER))
            final.writestr(RESOURCE, replacements[RESOURCE])
            final.write(archive, update.ARCHIVE_MEMBER)
            final.writestr(self.contract.MANIFEST, json.dumps({"schema": 1,
                "inputKey": self.manifest["inputKey"], "game": game_manifest, "mod": manifest}))
        with zipfile.ZipFile(delivered) as final:
            checked = self.contract.validate(final, self.manifest["inputKey"], game,
                {"archive": game_manifest["archive"], "sha256": game_manifest["archiveSha256"]})
        self.assertTrue(checked["modEntryBytesVerified"])
        self.assertEqual(checked["modFileCount"], len(manifest["files"]))
        self.assertEqual(apk_update.read_resource_json(delivered, ("quest-mod-content", "quest-mod-bundles")),
                         {"quest-mod-content": manifest, "quest-mod-bundles": receipt})

    def test_bad_apk_mod_crc_fails_without_publishing_a_partial_cache(self):
        self.corrupt_member_crc(update.ARCHIVE_MEMBER)
        with self.assertRaises(storage.BuildError): self.deliver()
        self.assertFalse((self.retained() / update.ARCHIVE_NAME).exists())
        self.assertFalse((self.retained() / "owner.json").exists())
        self.assertFalse((self.retained() / (update.ARCHIVE_NAME + ".partial")).exists())
        self.assertIsNone(storage.Stages(self.output).valid("retained-mod-content", self.manifest["archiveSha256"]))

    def test_signed_sha_mismatch_fails_before_story_publication(self):
        self.write_apk(archive_body=b"different bank payload with a correct ZIP CRC")
        with self.assertRaisesRegex(storage.BuildError, "signed SHA-256"): self.deliver()
        self.assertFalse((self.retained() / update.ARCHIVE_NAME).exists())

    def test_native_installation_mismatch_fails_before_any_mod_payload_read(self):
        self.base["installation"]["mod"]["archiveSha256"] = "f" * 64
        with self.assertRaisesRegex(storage.BuildError, "base installation contract"): self.deliver()
        self.assertFalse(self.retained().exists())

    def test_declared_mod_extent_is_bounded_before_streaming(self):
        self.assertEqual(update.MAX_MOD_ARCHIVE_BYTES, self.contract.MAXIMUM_MOD_ARCHIVE_BYTES)
        with patch.object(update, "MAX_MOD_ARCHIVE_BYTES", self.original.stat().st_size - 1), \
             self.assertRaisesRegex(storage.BuildError, "bounded retained mod archive"):
            self.deliver()
        self.assertFalse(self.retained().exists())

    def test_mutated_apk_during_streaming_is_rejected_and_partial_removed(self):
        original_open = zipfile.ZipFile.open
        apk_path = self.apk
        class MutatingInput:
            def __init__(self, stream): self.stream, self.changed = stream, False
            def __enter__(self): self.stream.__enter__(); return self
            def __exit__(self, *args): return self.stream.__exit__(*args)
            def read(self, amount):
                chunk = self.stream.read(amount)
                if chunk and not self.changed:
                    self.changed = True
                    info = apk_path.stat(); os.utime(apk_path, ns=(info.st_atime_ns, info.st_mtime_ns + 1000000))
                return chunk
        def open_input(archive, name, *args, **kwargs):
            stream = original_open(archive, name, *args, **kwargs)
            return MutatingInput(stream) if Path(archive.filename) == self.apk and getattr(name, "filename", name) == update.ARCHIVE_MEMBER else stream
        with patch.object(zipfile.ZipFile, "open", open_input), self.assertRaisesRegex(storage.BuildError, "changed while extracting"):
            self.deliver()
        self.assertFalse((self.retained() / update.ARCHIVE_NAME).exists())
        self.assertFalse((self.retained() / (update.ARCHIVE_NAME + ".partial")).exists())

    def test_short_destination_write_cannot_acquire_a_closed_receipt(self):
        original_open = Path.open
        class ShortOutput:
            def __init__(self, stream): self.stream = stream
            def __enter__(self): self.stream.__enter__(); return self
            def __exit__(self, *args): return self.stream.__exit__(*args)
            def write(self, data): return self.stream.write(data[:-1])
            def __getattr__(self, name): return getattr(self.stream, name)
        def open_output(path, mode="r", *args, **kwargs):
            stream = original_open(path, mode, *args, **kwargs)
            return ShortOutput(stream) if path.name == update.ARCHIVE_NAME + ".partial" and mode == "wb" else stream
        with patch.object(Path, "open", open_output), self.assertRaisesRegex(storage.BuildError, "complete input chunk"):
            self.deliver()
        self.assertFalse((self.retained() / update.ARCHIVE_NAME).exists())

    def test_corrupted_retained_archive_reextracts_just_the_qualified_mod_payload(self):
        first = self.deliver()
        (self.retained() / update.ARCHIVE_NAME).write_bytes(b"damaged cache")
        original_open, reads = zipfile.ZipFile.open, []
        def observed(archive, name, *args, **kwargs):
            if Path(archive.filename) == self.apk: reads.append(getattr(name, "filename", name))
            return original_open(archive, name, *args, **kwargs)
        with patch.object(zipfile.ZipFile, "open", observed): second = self.deliver()
        self.assertEqual(first, second)
        self.assertEqual(reads.count(update.ARCHIVE_MEMBER), 1)
        self.assertNotIn(CAMPAIGN, reads)

    def test_failed_story_keeps_verified_retained_bank_for_retry(self):
        original_rows = copy.deepcopy(self.inputs["mod"]["files"])
        self.inputs["mod"]["files"][0]["sha256"] = "f" * 64
        with self.assertRaisesRegex(storage.BuildError, "source snapshot"): self.deliver()
        self.assertTrue(storage.Stages(self.output).valid("retained-mod-content", self.manifest["archiveSha256"]))
        self.inputs["mod"]["files"] = original_rows
        original_open = zipfile.ZipFile.open
        def guarded(archive, name, *args, **kwargs):
            if Path(archive.filename) == self.apk and getattr(name, "filename", name) == update.ARCHIVE_MEMBER:
                raise AssertionError("retry must retain already qualified banks")
            return original_open(archive, name, *args, **kwargs)
        with patch.object(zipfile.ZipFile, "open", guarded): self.deliver()

    def test_existing_replacement_map_is_unchanged_and_conflicting_art_is_rejected(self):
        replacements = {"assets/Quest/offline-profile.json": b"existing code/profile replacement"}
        before = dict(replacements)
        self.deliver(replacements=replacements)
        self.assertEqual(replacements, before)
        with self.assertRaisesRegex(storage.BuildError, "separate authored mod archive"):
            self.deliver(replacements={update.ARCHIVE_MEMBER: self.original})

    def isolated_builder(self, *, lazy_reader=False):
        """Execute the real Wizard -> installer -> selected Builder loader."""
        repo = self.root / ("lazy-loader" if lazy_reader else "selected-loader")
        destination = repo / "tools/quest-builder"
        shutil.copytree(HELPER_ROOT / "tools/quest-builder", destination,
                        ignore=shutil.ignore_patterns("__pycache__", "bin", "obj", "Library", "Temp"))
        for name in ("menu_depth_update.py", "apk_update.py"):
            shutil.copy2(ROOT / "tools/quest-builder" / name, destination / name)
        if lazy_reader:
            path = destination / "apk_update.py"; code = path.read_text()
            eager = "from script_order import _Reader as _ResourceReader\n"
            self.assertEqual(code.count(eager), 1)
            code = code.replace(eager, "")
            marker = '    import UnityPy\n    if not 48 <= len(raw) <= MAX_MEMBER:'
            self.assertEqual(code.count(marker), 1)
            code = code.replace(marker, '    import UnityPy\n    from script_order import _Reader as _ResourceReader\n    if not 48 <= len(raw) <= MAX_MEMBER:')
            path.write_text(code)
        (repo / "tools/quest-installer").mkdir()
        shutil.copy2(HELPER_ROOT / "tools/quest-installer/installer.py", repo / "tools/quest-installer/installer.py")
        wizard_path = HELPER_ROOT / "tools/quest-wizard"
        sys.path.insert(0, str(wizard_path))
        try:
            discovery = load("menu_update_fixture_wizard", wizard_path / "discovery.py")
            return discovery.builder(repo)
        finally:
            sys.path.remove(str(wizard_path))

    def test_real_wizard_capture_survives_foreign_global_aliases_and_keeps_error_abi(self):
        aliases = {name: ModuleType("unrelated_application_" + name) for name in ("storage", "profile", "script_order")}
        with patch.dict(sys.modules, aliases):
            selected = self.isolated_builder(); captured = selected._module_context()
            for name, module in aliases.items(): self.assertIs(sys.modules[name], module)
            self.assertIs(captured.menu_depth_resources._storage.BuildError, captured.BuildError)
            actual = captured.menu_depth_update.delivery(captured, self.source, self.inputs, self.output, self.apk, self.base)
            self.assertTrue(actual[0].is_file())
            bad = bytearray(self.native_resources()); struct.pack_into(">I", bad, 20, 1)
            self.write_apk(native=bytes(bad))
            with self.assertRaises(captured.BuildError) as failure:
                captured.menu_depth_update.delivery(captured, self.source, self.inputs, self.output, self.apk, self.base)
            self.assertIs(type(failure.exception), captured.BuildError)

    def test_causal_lazy_reader_control_fails_after_real_wizard_restores_aliases(self):
        aliases = {name: ModuleType("unrelated_application_" + name) for name in ("storage", "profile", "script_order")}
        with patch.dict(sys.modules, aliases):
            selected = self.isolated_builder(lazy_reader=True); captured = selected._module_context()
            with self.assertRaisesRegex(ImportError, "_Reader"):
                captured.menu_depth_update.delivery(captured, self.source, self.inputs, self.output, self.apk, self.base)


if __name__ == "__main__": unittest.main()
