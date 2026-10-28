"""Exact closed-bank proofs and production Editor continuation after pack failure."""
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

from test_standalone_packer_recovery import fixture, packer
from test_package_editor_contract import declaration
from test_addressables_minimum_work import EDITOR, MONO, MCS, SHIMS, PROGRAM


class NativeBankCheckpointTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-bank-proof-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.native, self.archive, self.manifest, self.request = fixture(self.root)
        self.environment = patch.dict(os.environ, {"GHVR_QUEST_NATIVE_BANK_KEY": "b" * 64,
            "GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "0", "GHVR_QUEST_VERIFICATION": "normal",
            "GHVR_QUEST_VALIDATE_IMPORTED_ASSETS": "0"})
        self.environment.start(); self.addCleanup(self.environment.stop)

    def record(self):
        return packer.native_checkpoint(self.request, "record")

    def check(self):
        return packer.native_checkpoint(self.request, "check")

    def test_unchanged_closed_bank_reuses_measured_native_hashes_without_payload_reads(self):
        self.record()
        digest = packer.digest
        def no_native_read(path, progress=None):
            if self.native in path.parents: raise AssertionError("Unchanged native payload rehashed")
            return digest(path, progress)
        with patch.object(packer, "digest", side_effect=no_native_read):
            self.assertTrue(self.check()["reused"])
            self.assertFalse(packer.pack(self.request)["reused"])

    def test_whole_request_key_can_change_only_while_exact_native_compiler_key_matches(self):
        self.record()
        value = json.loads(self.manifest.read_text()); value["inputKey"] = "c" * 64
        self.manifest.write_text(json.dumps(value))
        checked = self.check()
        self.assertTrue(checked["reused"]); self.assertEqual(checked["inputKey"], "c" * 64)
        with patch.dict(os.environ, {"GHVR_QUEST_NATIVE_BANK_KEY": "d" * 64}):
            self.assertFalse(self.check()["reused"])

    def test_changed_native_bytes_with_restored_mtime_are_not_reused(self):
        self.record()
        path = self.native / "original.bundle"
        before = path.stat()
        original = path.read_bytes(); path.write_bytes(original[:-1] + b"X")
        os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
        result = self.check()
        self.assertFalse(result["reused"])
        self.assertIn("Cooked native bytes differ", result["reason"])
        self.assertEqual(path.read_bytes(), original[:-1] + b"X")

    def test_unchanged_bytes_with_changed_stamp_are_qualified_once_then_reused(self):
        self.record()
        path = self.native / "original.bundle"; path.write_bytes(path.read_bytes())
        with patch.object(packer, "digest", wraps=packer.digest) as reads:
            self.assertTrue(self.check()["reused"])
        self.assertTrue(any(call.args[0] == path for call in reads.call_args_list))
        with patch.object(packer, "digest", side_effect=AssertionError("qualified payload reread")):
            self.assertTrue(self.check()["reused"])

    def test_missing_extra_or_broken_catalog_returns_compile_needed_without_destructive_repair(self):
        self.record(); original = (self.native / "catalog.json").read_bytes()
        cases = ("missing", "extra", "catalog")
        for case in cases:
            with self.subTest(case=case):
                if case == "missing": (self.native / "original.bundle").rename(self.root / "held.bundle")
                elif case == "extra": (self.native / "unused.bundle").write_bytes(b"extra")
                else: (self.native / "catalog.json").write_text('{"m_InternalIds":["missing.bundle"]}')
                result = self.check(); self.assertFalse(result["reused"]); self.assertTrue(result["reason"])
                if case == "missing": (self.root / "held.bundle").rename(self.native / "original.bundle")
                elif case == "extra": (self.native / "unused.bundle").unlink()
                else: (self.native / "catalog.json").write_bytes(original)

    def test_legacy_bank_needs_explicit_source_and_successful_log_adoption_authorization(self):
        self.assertFalse(self.check()["reused"])
        self.assertFalse((self.root / packer.NATIVE_CHECKPOINT).exists())
        with patch.dict(os.environ, {"GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "1"}):
            self.assertTrue(self.check()["reused"])
        receipt = json.loads((self.root / packer.NATIVE_CHECKPOINT).read_text())
        self.assertTrue(receipt["legacyAdopted"])
        self.assertTrue(receipt["compilerCompleted"])

    def test_legacy_adoption_rejects_partial_output_and_unqualified_compiler_identity(self):
        (self.native / "original.bundle").unlink()
        with patch.dict(os.environ, {"GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "1"}):
            self.assertFalse(self.check()["reused"])
        self.assertFalse((self.root / packer.NATIVE_CHECKPOINT).exists())
        with patch.dict(os.environ, {"GHVR_QUEST_NATIVE_BANK_KEY": "unknown"}):
            self.assertFalse(self.check()["reused"])

    def test_corrupt_closed_receipt_never_seeds_native_hash_trust(self):
        self.record()
        path = self.root / packer.NATIVE_CHECKPOINT
        receipt = json.loads(path.read_text()); receipt["files"][0]["sha256"] = "0" * 64
        path.write_text(json.dumps(receipt))
        self.assertFalse(self.check()["reused"])

    def test_unknown_windows_clock_requires_real_payload_reads_on_each_resume(self):
        observer = type("UnknownMetadata", (), {"stamp": lambda self, path: None})()
        with patch.object(packer.sys, "platform", "win32"), patch.object(packer, "_windows_metadata", observer):
            self.record()
            for _ in range(2):
                with patch.object(packer, "digest", wraps=packer.digest) as reads:
                    self.assertTrue(self.check()["reused"])
                self.assertEqual({call.args[0] for call in reads.call_args_list}, {
                    self.native / name for name in ("original.bundle", "catalog.json", "settings.json")})

    def test_true_windows_change_clock_rejects_restored_write_and_creation_time(self):
        class Observer:
            revision = 101
            def stamp(self, path):
                value = path.stat()
                return ("win32-v1", value.st_dev, str(value.st_ino), value.st_size, value.st_mtime_ns, self.revision)
        observer = Observer()
        with patch.object(packer.sys, "platform", "win32"), patch.object(packer, "_windows_metadata", observer):
            self.record()
            path = self.native / "original.bundle"; before = path.stat()
            original = path.read_bytes(); path.write_bytes(original[:-1] + b"X")
            os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
            observer.revision += 1
            checked = self.check()
        self.assertFalse(checked["reused"])
        self.assertIn("Cooked native bytes differ", checked["reason"])

    @unittest.skipIf(os.name == "nt", "Requires fixture hardlink permission")
    def test_native_hardlinks_never_seed_persistent_closed_byte_trust(self):
        os.link(self.native / "original.bundle", self.root / "native-preimage.bundle")
        self.record()
        receipt = json.loads((self.root / packer.NATIVE_CHECKPOINT).read_text())
        self.assertIsNone(next(row for row in receipt["files"] if row["path"] == "original.bundle")["stamp"])
        with patch.object(packer, "digest", wraps=packer.digest) as reads:
            self.assertTrue(self.check()["reused"])
        self.assertEqual([call.args[0] for call in reads.call_args_list], [self.native / "original.bundle"])


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono compiler unavailable")
class ActualEditorPackContinuationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-native-resume-editor-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.native, self.archive, self.manifest, self.request = fixture(self.root)
        actors = self.root / "Assets/Actors"; actors.mkdir()
        (actors / "Hero.prefab").write_text("unmodified original actor fixture")
        mapping = self.root / "Assets/QuestOriginalCampaign/campaign-addressables.json"
        mapping.parent.mkdir()
        mapping.write_text(json.dumps({"schema": 1, "associatedEntryCount": 1, "unresolvedEntryCount": 0,
            "entries": [{"assetPath": "Assets/Actors/Hero.prefab", "recoveredGuid": "a" * 32,
                "sourceBundle": "original-core", "status": "associated", "keys": ["hero"],
                "labels": ["always_loaded_base", "always_loaded_standalone", "always_loaded_base_high"]}]}))
        helper = self.root / "QuestStartupEvidence/native_content_pack.py"
        helper.parent.mkdir(); shutil.copyfile(packer.__file__, helper)
        wrapper = helper.with_name("controlled-downstream-failure.py")
        wrapper.write_text("import pathlib,runpy,sys\n"
            "marker=pathlib.Path('fail-pack.once')\n"
            "if '--native-checkpoint' not in sys.argv and marker.exists():\n"
            " marker.unlink();print(\"Native content packer stopped: No module named 'storage'\",file=sys.stderr);raise SystemExit(1)\n"
            "runpy.run_path(" + repr(str(helper)) + ",run_name='__main__')\n")
        self.env = {**os.environ, "GHVR_QUEST_TARGET": "game", "GHVR_QUEST_VERIFICATION": "normal",
            "GHVR_QUEST_VALIDATE_IMPORTED_ASSETS": "0", "GHVR_QUEST_CONTENT_PACK_PYTHON": sys.executable,
            "GHVR_QUEST_CONTENT_PACK_HELPER": str(wrapper), "GHVR_QUEST_NATIVE_BANK_KEY": "b" * 64,
            "GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT": "0"}
        source = (EDITOR / "QuestStartupAddressablesBuild.cs").read_text()
        names = ("sealed class Association", "sealed class Associations", "sealed class ContentFile",
            "sealed class ContentManifest", "sealed class Alias", "sealed class RuntimeManifest", "sealed class NativeShaderReceipt",
            "sealed class NativePackRequest", "sealed class NativePackReceipt", "sealed class NativeBankCheck",
            "sealed class NativeBankCheckpoint", "public static void Build()",
            "internal static AddressableAssetGroup GetOrCreateOwnedGroup(", "static string NativeKey(",
            "static string StableName(", "static void RecordOmittedNativeShaderAudit()",
            "static bool CheckNativeContentCheckpoint(", "static void RecordNativeContentCheckpoint(",
            "static void RepackContent(", "static void InvokeContentHelper(", "static string QuoteArgument(")
        shims = SHIMS.replace('BuildPath="Native/Android"', 'BuildPath="Library/com.unity.addressables/aa/Android"')
        shims = shims.replace('File.WriteAllText(Path.Combine(Addressables.BuildPath,"settings.json"),"{}");',
            'File.WriteAllText(Path.Combine(Addressables.BuildPath,"settings.json"),"{\\\"m_CatalogLocations\\\":[]}");'
            'File.WriteAllText(Path.Combine(Addressables.BuildPath,"catalog.json"),"{\\\"m_InternalIds\\\":[\\\"original.bundle\\\"]}");'
            'File.WriteAllText(Path.Combine(Addressables.BuildPath,"original.bundle"),"UnityFS cooked native fixture");')
        shims = shims.replace('public static class QuestWizardProgress {',
            'public static class QuestWizardProgress {'
            'public static string RunObservedProcess(System.Diagnostics.Process process){'
            'process.Start();var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();'
            'process.WaitForExit();output.GetAwaiter().GetResult();return error.GetAwaiter().GetResult();}')
        production = shims + "\nnamespace GloomhavenVR.Quest.Editor {public static class QuestBuild {public static bool ImportedAssetValidationRequested(){return false;}}"
        production += "public static class QuestStartupAddressablesBuild {" + "\n".join(declaration(source, marker) for marker in names)
        production += 'static void AddCampaignShaderRetention(AddressableAssetSettings value){Spy.Call("shader-retention");}'
        production += 'static void ValidateNativeCampaignShaders(string value){Spy.Call("native-audit");}}}\n' + PROGRAM
        path = self.root / "Resume.cs"; path.write_text(production)
        self.exe = self.root / "Resume.exe"
        compiled = subprocess.run([str(MONO), str(MCS), "-langversion:latest", "-r:System.Web.Extensions",
            "-nowarn:0649", "-out:" + str(self.exe), str(path)], capture_output=True, text=True, timeout=30)
        self.assertEqual(compiled.returncode, 0, compiled.stdout + compiled.stderr)

    def run_editor(self):
        result = subprocess.run([str(MONO), str(self.exe)], cwd=self.root, env=self.env,
                                capture_output=True, text=True, timeout=45)
        return result.returncode, json.loads(result.stdout)

    def test_actual_editor_reuses_successful_bank_after_real_isolated_child_pack_failure(self):
        (self.root / "fail-pack.once").write_text("controlled captured downstream failure")
        failed, first = self.run_editor()
        self.assertEqual(failed, 2, first)
        self.assertIn("No module named 'storage'", first["message"])
        self.assertEqual(first["calls"]["native-build"], 1)
        self.assertTrue((self.root / packer.NATIVE_CHECKPOINT).is_file())
        resumed, second = self.run_editor()
        self.assertEqual(resumed, 0, second)
        self.assertNotIn("native-build", second["calls"])
        self.assertTrue((self.root / packer.RECEIPT).is_file())
        runtime = json.loads((self.root / "Assets/Quest/Resources/quest-startup-addressables.json").read_text())
        self.assertEqual(runtime["inputKey"], "a" * 64)
        self.assertEqual(runtime["aliases"][0]["key"], "hero")

    def test_actual_editor_adopts_only_explicitly_qualified_legacy_successful_output(self):
        self.env["GHVR_QUEST_NATIVE_BANK_LEGACY_ADOPT"] = "1"
        result, value = self.run_editor()
        self.assertEqual(result, 0, value)
        self.assertNotIn("native-build", value["calls"])
        receipt = json.loads((self.root / packer.NATIVE_CHECKPOINT).read_text())
        self.assertTrue(receipt["legacyAdopted"])

    def test_actual_editor_cooks_again_when_closed_bytes_changed(self):
        first, value = self.run_editor(); self.assertEqual(first, 0, value)
        path = self.native / "original.bundle"; path.write_bytes(b"damaged native output")
        resumed, value = self.run_editor(); self.assertEqual(resumed, 0, value)
        self.assertEqual(value["calls"]["native-build"], 1)


if __name__ == "__main__":
    unittest.main()
