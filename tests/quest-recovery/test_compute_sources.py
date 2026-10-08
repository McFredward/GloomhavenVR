"""Pinned-source, ABI and immutable-input controls for the real Android crash."""
import hashlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import compute_sources as c


def digest(data):
    return hashlib.sha256(data).hexdigest()


# Small generated ABI fixtures, not redistributed owned shader/assembly bytes.
OBJECT = """%YAML 1.1
--- !u!72 &7200000
ComputeShader:
  m_Name: EyeHistogram
  kernels:
      name: KEyeHistogram
      threadGroupSize: 100000001000000001000000
          - name: _Source
          - name: _Histogram
    constantBuffers:
    - name: Params
      byteSize: 16
      params:
      - name: _ScaleOffsetRes
        type: 0
        offset: 0
        arraySize: 0
        rowCount: 1
        colCount: 4
"""
SOURCE = b"""#include "Common.cginc"
#include "EyeAdaptation.cginc"
#pragma kernel KEyeHistogram
RWStructuredBuffer<uint> _Histogram;
Texture2D<float4> _Source;
CBUFFER_START(Params)
float4 _ScaleOffsetRes;
CBUFFER_END
[numthreads(HISTOGRAM_THREAD_X,HISTOGRAM_THREAD_Y,1)]
void KEyeHistogram() {}
"""


class ComputeRestorationContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.project = Path(self.temp.name) / "generated"
        self.cache = Path(self.temp.name) / "cache"
        self.sources = self.cache / ("postprocessing-v1-" + c.COMMIT)
        self.public = {c.SHADERS + "EyeHistogram.compute": SOURCE,
                       c.SHADERS + "Common.cginc": b'#include "UnityCG.cginc"\n',
                       c.SHADERS + "EyeAdaptation.cginc": b"#define HISTOGRAM_BINS 64\n#define HISTOGRAM_THREAD_X 16\n#define HISTOGRAM_THREAD_Y 16\n",
                       "LICENSE": b"The MIT License (MIT)\nUnity Technologies\n"}
        self.hashes = {name: digest(data) for name, data in self.public.items()}
        for name, data in self.public.items():
            p = self.sources / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)
        self.original = OBJECT.encode()
        self.runtime = b"generated ABI fixture, not an original assembly"
        self.patches = [patch.object(c, "FILES", self.hashes),
                        patch.object(c, "ORIGINAL_SHA256", digest(self.original)),
                        patch.object(c, "RUNTIME_SHA256", digest(self.runtime))]
        for p in self.patches:
            p.start()
        self.addCleanup(lambda: [p.stop() for p in self.patches])
        self.addCleanup(self.temp.cleanup)
        (self.project / "Assets/Quest").mkdir(parents=True)
        self.write(c.ORIGINAL, self.original)
        self.write(c.ORIGINAL + ".meta", ("fileFormatVersion: 2\nguid: " + c.ORIGINAL_GUID +
                   "\nNativeFormatImporter:\n  mainObjectFileID: 7200000\n").encode())
        self.write(c.RUNTIME, self.runtime)
        self.write(c.BINDINGS, json.dumps({"schema": 1, "assetPaths": ["Assets/menu.prefab"], "bindings": []}).encode())
        self.write("Assets/menu.prefab", b"m_MethodName: OriginalCallback\nm_Target: {fileID: 123}\n")

    def write(self, relative, data):
        p = self.project / relative
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_bytes(data)
        return p

    def snapshot(self):
        return {p.relative_to(self.project).as_posix(): p.read_bytes() for p in self.project.rglob("*") if p.is_file()}

    def reject_without_write(self):
        before = self.snapshot()
        with self.assertRaises(c.ComputeSourceError):
            c.restore(self.project, self.cache)
        self.assertEqual(self.snapshot(), before)

    def test_restore_preserves_owned_bytes_identity_callbacks_and_source_hashes(self):
        before = self.snapshot()
        receipt = c.restore(self.project, self.cache)
        self.assertEqual((self.project / c.BACKUP).read_bytes(), before[c.ORIGINAL])
        self.assertEqual((self.project / (c.BACKUP + ".meta")).read_bytes(), before[c.ORIGINAL + ".meta"])
        self.assertEqual((self.project / "Assets/menu.prefab").read_bytes(), before["Assets/menu.prefab"])
        self.assertEqual((self.project / c.BINDINGS).read_bytes(), before[c.BINDINGS])
        self.assertEqual((self.project / c.RUNTIME).read_bytes(), before[c.RUNTIME])
        self.assertFalse((self.project / c.ORIGINAL).exists())
        self.assertIn("guid: " + c.ORIGINAL_GUID, (self.project / (c.TARGET + ".meta")).read_text())
        self.assertNotIn("currentAPIs", (self.project / (c.TARGET + ".meta")).read_text())
        self.assertEqual(receipt["resourcesKey"], "Shaders/EyeHistogram")
        self.assertEqual(receipt["abi"]["threadGroup"], [16, 16, 1])
        for path, expected in receipt["sourceFiles"].items():
            self.assertEqual(c.sha(self.project / path), expected)
        self.assertFalse(receipt["fullGameReady"])
        self.assertFalse(receipt["androidShaderCompiled"])
        self.assertFalse(receipt["originalPixelParityVerified"])

    def test_only_owned_script_path_field_is_updated_when_present(self):
        bindings = {"schema": 1, "assetPaths": [c.ORIGINAL, "Assets/menu.prefab"], "bindings": [{"callback": "Keep"}]}
        self.write(c.BINDINGS, json.dumps(bindings).encode())
        c.restore(self.project, self.cache)
        after = json.loads((self.project / c.BINDINGS).read_text())
        self.assertEqual(after["assetPaths"], [c.TARGET, "Assets/menu.prefab"])
        self.assertEqual(after["bindings"], bindings["bindings"])

    def test_identical_retry_keeps_first_provenance_and_every_byte(self):
        first = c.restore(self.project, self.cache)
        before = self.snapshot()
        self.assertEqual(c.restore(self.project, self.cache), first)
        self.assertEqual(self.snapshot(), before)

    def test_retry_rejects_missing_guid_as_explicit_domain_error(self):
        c.restore(self.project, self.cache)
        self.write(c.TARGET + ".meta", b"ComputeShaderImporter:\n")
        self.reject_without_write()

    def test_retry_rejects_reduced_evidence_file_inventory(self):
        c.restore(self.project, self.cache)
        r = json.loads((self.project / c.RECEIPT).read_text())
        r["sourceFiles"].pop("Assets/Resources/shaders/Common.cginc")
        self.write(c.RECEIPT, json.dumps(r).encode())
        self.reject_without_write()

    def test_retry_rejects_shader_or_backup_mutation(self):
        c.restore(self.project, self.cache)
        for path in [c.TARGET, c.BACKUP, c.BACKUP + ".meta", c.BINDINGS]:
            p = self.project / path
            old = p.read_bytes()
            p.write_bytes(old + b"corruption")
            self.reject_without_write()
            p.write_bytes(old)

    def test_unknown_native_compute_is_not_silently_dropped(self):
        self.write("Assets/Unknown.asset", b"--- !u!72 &7200000\nComputeShader:\n")
        self.reject_without_write()

    def test_unknown_source_compute_is_not_implicitly_approved(self):
        self.write("Assets/Unknown.COMPUTE", b"#pragma kernel Unknown\n")
        self.reject_without_write()

    def test_raw_recovery_project_is_rejected(self):
        (self.project / "Assets/Quest").rmdir()
        self.reject_without_write()

    def test_wrong_owned_runtime_or_recovered_object_is_rejected(self):
        for path in [c.RUNTIME, c.ORIGINAL]:
            old = (self.project / path).read_bytes()
            self.write(path, old + b"modified")
            self.reject_without_write()
            self.write(path, old)

    def test_wrong_original_guid_is_rejected(self):
        p = self.project / (c.ORIGINAL + ".meta")
        p.write_text(p.read_text().replace(c.ORIGINAL_GUID, "a" * 32))
        self.reject_without_write()

    def test_serialized_compute_dependency_requires_new_explicit_audit(self):
        self.write("Assets/references.prefab", ("shader: {fileID: 7200000, guid: " + c.ORIGINAL_GUID + "}\n").encode())
        self.reject_without_write()

    def test_addressable_compute_dependency_requires_new_explicit_audit(self):
        self.write("Assets/QuestOriginalStartup/startup-addressables.json", json.dumps({"entries": [{"recoveredGuid": c.ORIGINAL_GUID}]}).encode())
        self.reject_without_write()

    def test_occupied_source_destination_is_rejected(self):
        self.write("Assets/Resources/shaders/Common.cginc", b"another shader family")
        self.reject_without_write()

    def test_unrecognized_binding_schema_is_rejected(self):
        self.write(c.BINDINGS, b'{"schema": 2, "assetPaths": []}')
        self.reject_without_write()

    def test_symlink_asset_is_rejected(self):
        (self.project / "Assets/linked.asset").symlink_to(self.project / c.ORIGINAL)
        self.reject_without_write()

    def test_corrupt_cached_public_source_is_rejected_before_project_write(self):
        (self.sources / (c.SHADERS + "Common.cginc")).write_bytes(b"corrupt")
        self.reject_without_write()

    def test_unpinned_download_cannot_enter_cache_or_project(self):
        (self.sources / (c.SHADERS + "EyeHistogram.compute")).unlink()
        with patch.object(c.urllib.request, "urlopen", return_value=io.BytesIO(b"untrusted download")):
            self.reject_without_write()
        self.assertFalse((self.sources / (c.SHADERS + "EyeHistogram.compute")).exists())

    def test_original_kernel_buffer_params_and_dispatch_negative_controls(self):
        c.validate_original(OBJECT)
        mutations = [("KEyeHistogram", "WrongKernel"), ("_Source", "WrongSource"),
                     ("_Histogram", "WrongBuffer"), ("byteSize: 16", "byteSize: 32"),
                     ("colCount: 4", "colCount: 3"),
                     ("100000001000000001000000", "080000001000000001000000"),
                     ("100000001000000001000000", "10")]
        for original, wrong in mutations:
            with self.subTest(wrong=wrong), self.assertRaises(c.ComputeSourceError):
                c.validate_original(OBJECT.replace(original, wrong))

    def test_source_include_and_abi_must_match_even_if_bytes_are_pinned(self):
        p = self.sources / (c.SHADERS + "EyeHistogram.compute")
        for data in [SOURCE + b'#include "Unreviewed.cginc"\n', SOURCE.replace(b"Texture2D<float4>", b"Texture2D<float3>")]:
            p.write_bytes(data)
            self.hashes[c.SHADERS + "EyeHistogram.compute"] = digest(data)
            with self.assertRaises(c.ComputeSourceError):
                c.validate_sources(self.sources)

    def test_failure_after_original_move_rolls_back_all_owned_bytes(self):
        before = self.snapshot()
        with patch.object(c.shutil, "copyfile", side_effect=OSError("injected disk failure")):
            with self.assertRaises(OSError):
                c.restore(self.project, self.cache)
        self.assertEqual(self.snapshot(), before)


class ActualOwnedSourceAudit(unittest.TestCase):
    def test_real_original_object_and_official_source_if_available(self):
        owned = Path("/home/claw/quest3-local/recovery/startup-project-v4")
        public = Path("/home/claw/quest3-tools/official-postprocessing-v1") / c.COMMIT
        if not owned.is_dir() or not public.is_dir():
            self.skipTest("Private owned recovery and audited official cache are not available")
        c.require_hash(owned / c.ORIGINAL, c.ORIGINAL_SHA256)
        c.require_hash(owned / c.RUNTIME, c.RUNTIME_SHA256)
        c.validate_original((owned / c.ORIGINAL).read_text())
        c.validate_sources(public)


if __name__ == "__main__":
    unittest.main()
