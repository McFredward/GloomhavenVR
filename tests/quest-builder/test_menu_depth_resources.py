"""Bounded resource identity, installed-only reuse and loose story delivery."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import menu_depth_resources as resources


def sidecar(values):
    header = struct.pack("<i", len(values))
    for name, value in values:
        encoded = name.encode("utf-8")
        header += struct.pack("<ii", len(value), len(encoded)) + encoded
    return struct.pack("<i", len(header)) + header + b"".join(value for _, value in values)


class MenuDepthResources(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def recipe_source(self):
        source = self.root / "source"
        for row in resources.plan(ROOT)["sources"]:
            target = source / row["path"]
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / row["path"], target)
        return source

    def test_resource_directory_uses_unity_actual_contiguous_offsets(self):
        raw = sidecar([("first", b"123"), (resources.BUNDLE_RESOURCE, b"UnityFS\0native"), ("last", b"end")])
        entries = resources.resource_records(raw)
        self.assertEqual(raw[slice(entries[resources.BUNDLE_RESOURCE][0], sum(entries[resources.BUNDLE_RESOURCE]))], b"UnityFS\0native")
        self.assertEqual(raw[slice(entries["last"][0], sum(entries["last"]))], b"end")

    def test_resource_directory_rejects_causal_corruption(self):
        good = sidecar([("first", b"value"), ("last", b"end")])
        mutations = [good[:-1], good + b"tail", sidecar([("same", b"a"), ("same", b"b")])]
        wrong_header = bytearray(good); struct.pack_into("<i", wrong_header, 0, 4)
        wrong_length = bytearray(good); struct.pack_into("<i", wrong_length, 12, -1)
        wrong_count = bytearray(good); struct.pack_into("<i", wrong_count, 4, 4097)
        mutations += [wrong_header, wrong_length, wrong_count]
        for raw in mutations:
            with self.subTest(raw=bytes(raw[:16])), self.assertRaises(resources.BuildError):
                resources.resource_records(raw)

    def test_late_native_audit_uses_the_captured_storage_abi(self):
        with patch.dict(sys.modules):
            sys.modules.pop("storage", None)
            auditor = resources._load(ROOT, "tools/quest-builder/campaign_native_shaders.py")
            self.assertTrue(issubclass(auditor.ClosureError, resources.BuildError))
            self.assertNotIn("storage", sys.modules)

    def test_producer_identity_excludes_unrelated_code_and_story(self):
        source = self.recipe_source()
        initial = resources.plan(source)
        (source / "unrelated.cs").write_text("changed runtime code")
        (source / "story.gmd").write_bytes(b"numeric-only")
        self.assertEqual(resources.plan(source), initial)
        shader = source / resources.RENDERING / resources.SHADERS[0]
        shader.write_text(shader.read_text() + "\n// changed compiler input\n")
        self.assertNotEqual(resources.plan(source)["producerKey"], initial["producerKey"])
        # Immutable input rows remain usable in a compiler-only snapshot. They
        # are supplied only after the containing Builder qualifies source bytes.
        self.assertEqual(resources.plan(self.root / "absent-snapshot", initial["sources"]), initial)
        with self.assertRaises(resources.BuildError):
            resources.plan(self.root / "absent-snapshot", initial["sources"][:-1])

    def test_recipe_rejects_original_native_pin_drift(self):
        source = self.recipe_source()
        path = source / resources.RENDERING / "NativeCard.provenance.json"
        value = json.loads(path.read_text()); value["source_shader_path_id"] += 1
        path.write_text(json.dumps(value))
        with self.assertRaises(resources.BuildError):
            resources.plan(source)

    def test_installed_only_recovers_exact_bank_without_pc_or_unity(self):
        expected = resources.plan(ROOT)
        raw = b"fixture Android bank; platform parser exercised by separate actual-bank proof"
        audit = {"target": "Android", "platform": 13, "graphicsApi": "Vulkan"}
        value = {"schema": 1, **expected, "bundle": {"path": resources.BANK, "size": len(raw),
            "sha256": hashlib.sha256(raw).hexdigest()}, "nativePlatformAudit": audit}
        # 'sources' is extra public metadata; the closed recipe remains exact.
        apk = self.root / "base.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            archive.writestr(resources.RESOURCE_MEMBER, sidecar([(resources.BUNDLE_RESOURCE, raw),
                (resources.PROVENANCE_RESOURCE, json.dumps(value).encode())]))
        with patch.object(resources, "inspect_bank", return_value=audit) as inspect:
            first = resources.produce(None, ROOT, self.root / "output", base_apk=apk)
            self.assertTrue(first["reused"])
            self.assertEqual(first["bundle"].read_bytes(), raw)
            second = resources.produce(None, ROOT, self.root / "output")
            self.assertEqual(second, first)
            self.assertEqual(inspect.call_count, 3)
        first["bundle"].write_bytes(raw + b"corrupt")
        with self.assertRaises(resources.BuildError):
            resources.produce(None, ROOT, self.root / "output")

    def test_missing_matching_resource_demands_bounded_pc_generation(self):
        apk = self.root / "old.apk"
        with zipfile.ZipFile(apk, "w") as archive:
            archive.writestr(resources.RESOURCE_MEMBER, sidecar([("old", b"unchanged")]))
        with self.assertRaisesRegex(resources.BuildError, "owned PC game once"):
            resources.produce(None, ROOT, self.root / "output", base_apk=apk)

    def story_source(self):
        source = self.root / "story-source"
        root = source / resources.STORY
        root.mkdir(parents=True)
        name = "a" * 32 + ".gmd"
        raw = b"GMD1" + b"\0" * 28
        (root / name).write_bytes(raw)
        (root / "manifest.json").write_text(json.dumps({"schema": 1, "status": "complete", "format": "GMD1", "images": [
            {"depth_file": name, "depth_bytes": len(raw), "depth_sha256": hashlib.sha256(raw).hexdigest()}]}))
        return source

    def test_story_survives_real_archive_publication_with_unchanged_banks(self):
        source = self.story_source()
        records = resources.story_records(source)
        self.assertEqual(records[0]["path"], resources.STORY_DESTINATION + "manifest.json")
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("StreamingAssets/gloomhavenvr.bundle", b"retained Android bank")
            resources.write_story(archive, source, records)
        with zipfile.ZipFile(stream) as archive:
            self.assertEqual(archive.read("StreamingAssets/gloomhavenvr.bundle"), b"retained Android bank")
            for row in records:
                self.assertEqual(hashlib.sha256(archive.read(row["path"])).hexdigest(), row["sha256"])
        field = source / resources.STORY / ("a" * 32 + ".gmd")
        field.write_bytes(b"changed numeric field")
        with self.assertRaises(resources.BuildError):
            resources.story_records(source)

    def test_story_rejects_unmanifested_or_unsafe_fields(self):
        source = self.story_source()
        root = source / resources.STORY
        (root / ("b" * 32 + ".gmd")).write_bytes(b"unmanifested")
        with self.assertRaises(resources.BuildError):
            resources.story_records(source)
        (root / ("b" * 32 + ".gmd")).unlink()
        manifest = root / "manifest.json"
        value = json.loads(manifest.read_text()); value["images"][0]["depth_file"] = "../escape.gmd"
        manifest.write_text(json.dumps(value))
        with self.assertRaises(resources.BuildError):
            resources.story_records(source)

    def test_story_rejects_changed_bytes_during_actual_transfer(self):
        source = self.story_source()
        rows = resources.story_records(source)
        original = zipfile.ZipFile.open
        def mutate(archive, name, *args, **kwargs):
            if isinstance(name, zipfile.ZipInfo) and name.filename.endswith(".gmd"):
                path = source / resources.STORY / Path(name.filename).name
                path.write_bytes(b"changed after qualification")
            return original(archive, name, *args, **kwargs)
        with zipfile.ZipFile(io.BytesIO(), "w") as archive, patch.object(zipfile.ZipFile, "open", mutate):
            with self.assertRaisesRegex(resources.BuildError, "archive transfer"):
                resources.write_story(archive, source, rows)
