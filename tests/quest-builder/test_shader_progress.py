"""Measured Shader aliases/cache/reconstruction; deterministic non-game adapters."""
from contextlib import redirect_stdout
import hashlib
import io
import json
import os
import struct
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import attrs

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
sys.path.insert(0, str(ROOT / "tools/quest-shaders"))
import campaign_shaders
import full_shaders
import produce
from storage import BuildError


def events(output):
    return [json.loads(line.split(" ", 1)[1]) for line in output.splitlines()
        if line.startswith("GHVRQ_PROGRESS ")]


@attrs.define
class Form:
    m_Name: str
    m_SubShaders: list = attrs.field(factory=list)


class ShaderFixture(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project, self.game, self.cache = (self.root / name for name in ("project", "game", "cache"))
        (self.project / "Assets").mkdir(parents=True)
        self.game.mkdir()
        (self.game / "original.bundle").write_bytes(b"fixture owned native container")
        self.owner_map = {"fixture-cab": "original.bundle"}
        self.identities = []
        self.originals = []
        for index, name in enumerate(("OriginalA", "OriginalB"), 1):
            guid = f"{index:032x}"
            path = "Assets/" + name + ".shader"
            (self.project / path).write_text('Shader "' + name + '" { }\n', encoding="utf-8")
            (self.project / (path + ".meta")).write_text("fileFormatVersion: 2\nguid: " + guid + "\nShaderImporter:\n", encoding="utf-8")
            self.identities.append({"guid": guid, "path": path, "objects": [
                {"classId": 48, "fileId": 4800000, "collection": "fixture-cab", "pathId": index}]})
            shader = SimpleNamespace(m_ParsedForm=Form(name))
            self.originals.append(SimpleNamespace(type=48, path_id=index, assets_file=SimpleNamespace(name="fixture-cab"),
                read=lambda shader=shader: shader, get_raw_data=lambda name=name: (name + " original serialized Shader").encode()))
        (self.project / "Assets/Original.mat").write_text("  m_Shader: {fileID: 4800000, guid: " + f"{1:032x}" + ", type: 3}\n", encoding="utf-8")
        self.identities.append({"guid": f"{3:032x}", "path": "Assets/Original.mat", "objects": [
            {"classId": 21, "fileId": 2100000, "collection": "fixture-cab", "pathId": 3}]})
        self.identity = self.project / "identities.json"
        self.identity.write_text(json.dumps({"schema": 1, "identities": self.identities}), encoding="utf-8")
        self.unity = SimpleNamespace(load=lambda path: SimpleNamespace(objects=self.originals))
        self.translate_calls = []

    def programs(self, shader, unity, **kwargs):
        result = []
        for stage, tier in (("vertex", 0), ("fragment", 0), ("vertex", 1)):
            raw = ("fixture original " + stage).encode()
            result.append({"subshader": 0, "pass": 0, "stage": stage, "blobIndex": 1 if stage == "vertex" else 2,
                "hardwareTier": tier, "keywords": [], "interface": {"buffers": [], "bindings": []},
                "originalDxbcSha256": hashlib.sha256(raw).hexdigest(), "raw": raw, "dxbc": raw,
                "programVersion": 1, "programType": 1})
        return result

    def translate(self, raw, cache, *args):
        self.translate_calls.append(raw)
        cache.mkdir(parents=True, exist_ok=True)
        key = hashlib.sha256(raw).hexdigest()
        path = cache / (key + ".hlsl")
        path.write_text("float4 NativeOriginal(float4 value) { return value * 0.5f; }\nvoid fixture_main() { }\n", encoding="utf-8")
        (cache / (key + ".dxbc")).write_bytes(raw)
        return {"hlslPath": str(path), "originalDxbcSha256": key,
            "translatedHlslSha256": full_shaders._hash(path), "inputSignature": []}

    def inventory(self, enabled=True, module=full_shaders):
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1" if enabled else "0"), redirect_stdout(output), \
                patch.object(module, "original_programs", side_effect=self.programs), \
                patch.object(module, "dxbc_container", side_effect=lambda raw: (raw, {})), \
                patch.object(module, "translate", side_effect=self.translate), \
                patch.object(module, "native_buffer_layouts", return_value=[]):
            report = module.inventory(self.project, self.game, self.identity, self.owner_map,
                self.cache, unitypy=self.unity, bind_programs=True)
        return report, events(output.getvalue())

    def reconstruct(self, enabled=True, module=produce, name="overlay"):
        output = io.StringIO()
        native = SimpleNamespace(dxbc_container=lambda raw: (raw, {}),
            portable_sampling_interface=lambda text: (text, []), stereo_wrapper=lambda text, *args, **kwargs: text)
        # Stage-link/driver behavior is covered by the existing Shader suites;
        # this adapter isolates counters, actual cache bytes and output identity.
        def source(form, row, cache, includes, graphics_api):
            return ('Shader "' + row["originalName"] + '" {\n' + "".join('#include "' + value + '"\n'
                for value in includes.values()) + '}\n', [{"coverageKind": "original-native"}])
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1" if enabled else "0"), redirect_stdout(output), \
                patch.object(module, "recovery_module", return_value=native), \
                patch.object(module, "shader_source", side_effect=source):
            manifest = module.restore_project(self.project, self.cache / "original-shader-inventory.json",
                self.cache, self.root / name)
        return manifest, events(output.getvalue())


class ShaderInventoryProgress(ShaderFixture):
    def test_shader_and_alias_counters_keep_exact_warm_cache_outputs_and_read_ledger_once(self):
        disabled, absent = self.inventory(False)
        self.assertEqual(absent, [])
        self.assertEqual(len(self.translate_calls), 2)
        reads = []
        original_open = Path.open
        def observe(path, *args, **kwargs):
            if path == self.identity: reads.append(path)
            return original_open(path, *args, **kwargs)
        with patch.object(Path, "open", observe):
            enabled, measured = self.inventory(True)
        self.assertEqual(len(reads), 1)
        self.assertEqual(enabled, disabled)
        self.assertEqual(len(self.translate_calls), 2)
        self.assertEqual(enabled["shaderCount"], 2)
        self.assertEqual(enabled["originalProgramAliasCount"], 6)
        self.assertEqual(enabled["uniqueOriginalProgramCount"], 2)
        completed = [row for row in measured if row["status"] == "complete"]
        self.assertEqual([row["done"] for row in completed if row["phase"] == "prepare-items:campaign-shaders-variants"], [3, 3])
        self.assertEqual([row["done"] for row in completed if row["phase"] == "prepare-items:campaign-shaders-inventory"], [2])
        self.assertEqual([row["done"] for row in completed if row["phase"] == "prepare-items:campaign-shaders-materials"], [1])
        boundaries = [row["done"] for row in measured if row["phase"] == "prepare-items:campaign-shaders-inventory"]
        self.assertEqual(boundaries, sorted(boundaries))
        self.assertLess(len(measured), 40)

    def test_blocked_original_does_not_claim_shader_inventory_complete(self):
        self.programs = lambda *args: (_ for _ in ()).throw(full_shaders.ShaderRecoveryError("fixture original interface unavailable"))
        report, measured = self.inventory()
        self.assertEqual(report["blockedShaderCount"], 2)
        self.assertFalse(any(row["phase"] == "prepare-items:campaign-shaders-inventory" and row["status"] == "complete"
            for row in measured))
        self.assertTrue(any(row["phase"] == "prepare-items:campaign-shaders-inventory" and row["status"] == "failed"
            for row in measured))
        self.assertEqual(self.translate_calls, [])

    def test_missing_actual_native_object_cannot_close_container_or_inventory_counter(self):
        self.originals.pop()
        with self.assertRaisesRegex(full_shaders.ShaderRecoveryError, "missing from their actual CAB"):
            self.inventory()

    def test_existing_binary_material_branch_reports_actual_material_and_container_counts(self):
        (self.project / "Assets/Original.mat").write_bytes(b"\xfffixture original binary material")
        self.originals.append(SimpleNamespace(type=21, path_id=3, assets_file=SimpleNamespace(name="fixture-cab"),
            read=lambda: SimpleNamespace(m_Shader=SimpleNamespace(m_PathID=1, m_FileID=0))))
        report, measured = self.inventory()
        self.assertEqual(report["materialCount"], 1)
        self.assertEqual(report["materials"][0]["shaderGuid"], f"{1:032x}")
        completed = {row["phase"]: row for row in measured if row["status"] == "complete"}
        self.assertEqual(completed["prepare-items:campaign-shaders-binary-materials"]["done"], 1)
        self.assertEqual(completed["prepare-items:campaign-shaders-material-containers"]["done"], 1)


class NativeAliasExtractionProgress(unittest.TestCase):
    def test_real_native_directory_decoder_keeps_aliases_and_decodes_each_block_once(self):
        from UnityPy.helpers import CompressionHelper
        from UnityPy.export import ShaderConverter
        raw = struct.pack("<6i", 202012090, 1, 0, 0, 0, 0) + struct.pack("<ii", 0, 4) + b"DXBC"
        bank = struct.pack("<4i", 1, 16, len(raw), 0) + raw
        variants = [SimpleNamespace(m_BlobIndex=0, m_ShaderHardwareTier=tier, m_KeywordIndices=[]) for tier in (0, 1)]
        owner = SimpleNamespace(m_SubPrograms=variants, m_CommonParameters=None)
        shader = SimpleNamespace(platforms=[4], compressedBlob=bank, offsets=[[0]], compressedLengths=[[len(bank)]],
            decompressedLengths=[[len(bank)]], m_ParsedForm=SimpleNamespace(m_Name="Fixture/Original", m_KeywordNames=[],
                m_SubShaders=[SimpleNamespace(m_Passes=[SimpleNamespace(progVertex=owner, m_NameIndices=[])])]))
        output = io.StringIO()
        constructor = ShaderConverter.ShaderSubProgram
        with patch.object(CompressionHelper, "decompress_lz4", side_effect=lambda data, size: data), \
                patch.object(ShaderConverter, "ShaderSubProgram", wraps=constructor) as decode, \
                patch.object(full_shaders, "dxbc_container", side_effect=lambda data: (data, {})), \
                patch.object(full_shaders, "parameter_delta", return_value={"buffers": [], "bindings": []}):
            original = full_shaders.original_programs(shader)
            with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output):
                observed = full_shaders.original_programs(shader, progress_scope="campaign-shaders-extract")
        self.assertEqual(original, observed)
        self.assertEqual(len(observed), 2)
        self.assertEqual(decode.call_count, 2)  # One original block per invocation, despite two native aliases.
        self.assertEqual([row["hardwareTier"] for row in observed], [0, 1])
        completed = [row for row in events(output.getvalue()) if row["status"] == "complete"]
        self.assertEqual([(row["done"], row["total"]) for row in completed], [(2, 2)])


class ShaderReconstructionProgress(ShaderFixture):
    def test_unique_programs_shader_meta_materials_and_manifest_are_unchanged_with_progress(self):
        self.inventory(False)
        disabled, absent = self.reconstruct(False, name="disabled")
        enabled, measured = self.reconstruct(True, name="enabled")
        self.assertEqual(absent, [])
        self.assertEqual(enabled, disabled)
        def files(path):
            return {item.relative_to(path).as_posix(): item.read_bytes() for item in path.rglob("*") if item.is_file()}
        self.assertEqual(files(self.root / "enabled"), files(self.root / "disabled"))
        self.assertEqual(len(enabled["programs"]), 2)
        self.assertEqual(len(enabled["shaders"]), 2)
        self.assertEqual(len(enabled["materials"]), 1)
        completed = {row["phase"]: row for row in measured if row["status"] == "complete"}
        self.assertEqual(completed["prepare-items:campaign-shaders-programs"]["done"], 2)
        self.assertEqual(completed["prepare-items:campaign-shaders-sources"]["done"], 2)

    def test_corrupt_bound_bytes_never_complete_programs_or_start_source_reconstruction(self):
        report, _ = self.inventory(False)
        bound = Path(report["shaders"][0]["variants"][0]["boundHlslPath"])
        bound.write_bytes(bound.read_bytes() + b"unauthorized change")
        output = io.StringIO()
        with redirect_stdout(output), self.assertRaisesRegex(produce.ValidationError, "Bound original shader bytes changed"):
            self.reconstruct()
        self.assertFalse(any(path.is_file() for path in (self.root / "overlay").rglob("*")))


class ShaderWrapperProgress(ShaderFixture):
    def run_wrapper(self, owned_map):
        report, _ = self.inventory(False)
        manifest, _ = self.reconstruct(False, name="prepared")
        producer = SimpleNamespace(restore_project=lambda project, inventory, cache, overlay, **kwargs: self.copy_overlay(overlay, manifest))
        converter = SimpleNamespace(ensure=lambda *args: {})
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                patch.object(campaign_shaders, "load", side_effect=lambda root, relative, *extra: converter if "converters" in relative else producer), \
                patch.object(campaign_shaders, "original_cab_bundles", return_value=self.owner_map) as scan, \
                patch.object(campaign_shaders, "preserve_sources", return_value={}), \
                patch.object(campaign_shaders.full_shaders, "inventory", return_value=report):
            result = campaign_shaders.stage(ROOT, self.project, self.game, self.cache, cab_bundles=owned_map)
        return result, events(output.getvalue()), scan.call_count

    def copy_overlay(self, output, manifest):
        import shutil
        shutil.copytree(self.root / "prepared", output)
        return manifest

    def test_retained_ownership_avoids_rescan_and_copy_counter_uses_declared_artifact_count(self):
        result, measured, scans = self.run_wrapper(self.owner_map)
        self.assertEqual(scans, 0)
        self.assertEqual(len(result["shaders"]), 2)
        completed = {row["phase"]: row for row in measured if row["status"] == "complete"}
        self.assertEqual(completed["prepare-items:campaign-shaders-copy"]["done"], 6)
        self.assertEqual(completed["prepare-items:campaign-shaders-publish"]["done"], 2)
        self.assertTrue(any(row["phase"] == "prepare-items:campaign-shaders-bundles" and row["status"] == "reuse"
            for row in measured))

    def test_default_keeps_original_cab_capture_and_empty_provided_capture_is_rejected(self):
        result, measured, scans = self.run_wrapper(None)
        self.assertEqual(scans, 1)
        self.assertEqual(len(result["shaders"]), 2)
        with self.assertRaisesRegex(BuildError, "empty or invalid"):
            self.run_wrapper({})

    def test_default_and_retained_capture_produce_identical_project_outputs(self):
        before = {path.relative_to(self.project).as_posix(): path.read_bytes()
            for path in self.project.rglob("*") if path.is_file()}
        default, _, default_scans = self.run_wrapper(None)
        after_default = {path.relative_to(self.project).as_posix(): path.read_bytes()
            for path in self.project.rglob("*") if path.is_file()}
        for path in self.project.rglob("*"):
            if path.is_file() and path.relative_to(self.project).as_posix() not in before:
                path.unlink()
        for name, content in before.items():
            (self.project / name).write_bytes(content)
        retained, _, retained_scans = self.run_wrapper(self.owner_map)
        self.assertEqual(default_scans, 1)
        self.assertEqual(retained_scans, 0)
        self.assertEqual(default, retained)
        self.assertEqual(after_default, {path.relative_to(self.project).as_posix(): path.read_bytes()
            for path in self.project.rglob("*") if path.is_file()})

    def test_optional_bundle_counter_counts_original_nested_bundle_ownership(self):
        folder = self.game / "StreamingAssets/aa/StandaloneWindows64"
        folder.mkdir(parents=True)
        for name in ("a.bundle", "nested/b.bundle"):
            path = folder / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"fixture original owned bundle")
        members = SimpleNamespace(serialized_members=lambda path: [{"name": "CAB-" + ("1" if path.name == "a.bundle" else "2") * 32}])
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                patch.object(campaign_shaders, "load", return_value=members):
            result = campaign_shaders.original_cab_bundles(ROOT, self.game, observe=True)
        self.assertEqual(len(result), 2)
        completed = [row for row in events(output.getvalue()) if row["status"] == "complete"]
        self.assertEqual([(row["done"], row["total"]) for row in completed], [(2, 2)])


if __name__ == "__main__":
    unittest.main()
