"""Real child handoff and measured complete compute orchestration boundaries."""
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import campaign_compute
from storage import BuildError

PACKAGE = ROOT / "tools/quest-compute"
spec = importlib.util.spec_from_file_location("quest_compute_progress_test", PACKAGE / "__init__.py",
    submodule_search_locations=[str(PACKAGE)])
module = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = module
spec.loader.exec_module(module)
from quest_compute_progress_test import recovery, references


def events(output):
    return [json.loads(line.split(" ", 1)[1]) for line in output.splitlines()
        if line.startswith("GHVRQ_PROGRESS ")]


class ComputeChildProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.log = self.directory / "recovery.log"

    def test_progress_reaches_parent_before_child_exit_and_full_log_survives(self):
        script = self.directory / "child.py"
        acknowledgement = self.directory / "observed"
        script.write_text("""import json, pathlib, sys, time
print('original converter output', flush=True)
print('GHVRQ_PROGRESS ' + json.dumps(dict(schema=1, phase='prepare-items:campaign-compute-kernels',
 done=1, total=36, unit='kernels', detail='Fixture / Kernel', status='progress')), flush=True)
deadline = time.monotonic() + 5
while not pathlib.Path(sys.argv[1]).exists():
 if time.monotonic() > deadline: sys.exit(41)
 time.sleep(.005)
print('child observed live acknowledgement', flush=True)
""", encoding="utf-8")
        observer = campaign_compute.build_progress.event
        output = io.StringIO()

        def observe(*args, **kwargs):
            result = observer(*args, **kwargs)
            acknowledgement.write_text("observed")
            return result

        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                patch.object(campaign_compute.build_progress, "event", side_effect=observe):
            campaign_compute.recovery_process([sys.executable, str(script), str(acknowledgement)], self.log)
        values = events(output.getvalue())
        self.assertEqual([(row["done"], row["total"]) for row in values], [(1, 36)])
        self.assertEqual(acknowledgement.read_text(), "observed")
        self.assertIn(b"original converter output\n", self.log.read_bytes())
        self.assertIn(b"child observed live acknowledgement\n", self.log.read_bytes())
        self.assertIn(b"GHVRQ_PROGRESS ", self.log.read_bytes())

    def test_failure_keeps_utf8_error_and_rejects_invalid_progress_counts(self):
        script = self.directory / "bad.py"
        script.write_text("""import json, sys
print('GHVRQ_PROGRESS invalid json', flush=True)
print('GHVRQ_PROGRESS ' + json.dumps(dict(schema=1, phase='prepare-items:campaign-compute-kernels',
 done=37, total=36, unit='kernels', status='progress')), flush=True)
sys.stdout.buffer.write('ComputeRecoveryError: ursprüngliche Bindung fehlt\\n'.encode('utf-8'))
sys.stdout.flush()
sys.exit(7)
""", encoding="utf-8")
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                self.assertRaisesRegex(BuildError, "exit 7.*ursprüngliche Bindung fehlt.*recovery.log"):
            campaign_compute.recovery_process([sys.executable, str(script)], self.log)
        self.assertEqual(events(output.getvalue()), [])
        self.assertIn("ursprüngliche Bindung fehlt".encode(), self.log.read_bytes())
        self.assertIn(b"GHVRQ_PROGRESS invalid json", self.log.read_bytes())

    def test_long_converter_line_is_logged_without_forwarding_invented_work(self):
        script = self.directory / "long.py"
        script.write_text("import sys\nsys.stdout.write('x' * 140000 + '\\n')\n", encoding="utf-8")
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output):
            campaign_compute.recovery_process([sys.executable, str(script)], self.log)
        self.assertEqual(self.log.read_bytes(), b"x" * 140000 + b"\n")
        self.assertEqual(output.getvalue(), "")


class CompleteComputeCounterTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        self.source = self.directory / "source"
        assets = self.source / "Assets"
        assets.mkdir(parents=True)
        self.originals = []
        for index, name in enumerate(sorted(recovery.NAMES)):
            path = assets / (name + ".asset")
            path.write_text("%YAML 1.1\n--- !u!72 &7200000\n" + name + "\n", encoding="utf-8")
            Path(str(path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + f"{index + 1:032x}" +
                "\nNativeFormatImporter:\n  mainObjectFileID: 7200000\n", encoding="utf-8")
            count = 3 if index < 10 else 2
            self.originals.append({"name": name, "kernels": [{"name": "FixtureKernel" + str(kernel),
                "code": b"fixture instruction bytes", "threadGroups": [1, 1, 1],
                "requirements": 0, "interface": {}} for kernel in range(count)]})
        (assets / "Other.asset").write_text("%YAML 1.1\n--- !u!114 &11400000\n", encoding="utf-8")
        runtime = assets / "Plugins/Unity.Postprocessing.Runtime.dll"
        runtime.parent.mkdir()
        runtime.write_bytes(b"fixture original postprocessing runtime")
        self.runtime_hash = hashlib.sha256(runtime.read_bytes()).hexdigest()
        self.graphics_path = self.directory / "graphics.py"
        self.graphics_path.write_text("# fixture original converter", encoding="utf-8")
        self.code = self.directory / "translated.hlsl"
        self.code.write_text("fixture original translated HLSL", encoding="utf-8")
        self.graphics = types.SimpleNamespace(translate=lambda *args, **kwargs: {
            "hlslPath": str(self.code), "originalDxbcSha256": "a" * 64,
            "spirvSha256": "b" * 64, "translatedHlslSha256": "c" * 64})

    def run_stage(self, name, enabled):
        by_name = {row["name"]: row for row in self.originals}
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1" if enabled else "0"), redirect_stdout(output), \
                patch.object(recovery, "parse", side_effect=lambda text: by_name[text.splitlines()[-1]]), \
                patch.object(recovery, "POST_PROCESSING_SHA256", self.runtime_hash), \
                patch.object(recovery, "load_graphics", return_value=self.graphics), \
                patch.object(recovery, "restore", side_effect=lambda hlsl, *args: (hlsl, {"fixture": True})):
            manifest = recovery.stage(self.source, self.directory / name, graphics_module=self.graphics_path)
        return manifest, events(output.getvalue())

    def test_actual_13_shader_36_kernel_census_has_same_artifacts_with_observation_enabled(self):
        before = {path.relative_to(self.source).as_posix(): path.read_bytes()
            for path in self.source.rglob("*") if path.is_file()}
        disabled, absent = self.run_stage("disabled", False)
        enabled, observed = self.run_stage("enabled", True)
        self.assertEqual(absent, [])
        self.assertEqual(enabled, disabled)
        self.assertEqual(enabled["shaderCount"], 13)
        self.assertEqual(enabled["kernelCount"], 36)
        self.assertEqual(len(enabled["files"]), 26)
        disabled_bytes = {path.relative_to(self.directory / "disabled").as_posix(): path.read_bytes()
            for path in (self.directory / "disabled").rglob("*") if path.is_file()}
        enabled_bytes = {path.relative_to(self.directory / "enabled").as_posix(): path.read_bytes()
            for path in (self.directory / "enabled").rglob("*") if path.is_file()}
        self.assertEqual(enabled_bytes, disabled_bytes)
        self.assertEqual(before, {path.relative_to(self.source).as_posix(): path.read_bytes()
            for path in self.source.rglob("*") if path.is_file()})
        completed = {row["phase"]: row for row in observed if row["status"] == "complete"}
        self.assertEqual(completed["prepare-items:campaign-compute-inventory"]["total"], 14)
        self.assertEqual(completed["prepare-items:campaign-compute-kernels"]["done"], 36)
        self.assertEqual(completed["prepare-items:campaign-compute-shaders"]["done"], 13)
        self.assertEqual(completed["prepare-items:campaign-compute-inputs"]["done"], 26)
        kernel_events = [row for row in observed if row["phase"] == "prepare-items:campaign-compute-kernels"]
        self.assertEqual([row["done"] for row in kernel_events], sorted(row["done"] for row in kernel_events))
        self.assertEqual(len([row for row in kernel_events if row["detail"] and "/" in row["detail"]
            and row["status"] == "progress"]), 36)

    def test_invalid_original_census_never_claims_inventory_complete(self):
        (self.source / "Assets/EyeHistogram.asset").unlink()
        output = io.StringIO()
        by_name = {row["name"]: row for row in self.originals}
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                patch.object(recovery, "parse", side_effect=lambda text: by_name[text.splitlines()[-1]]), \
                self.assertRaisesRegex(recovery.ComputeRecoveryError, "exact 13"):
            recovery.inventory(self.source)
        self.assertFalse(any(row["phase"] == "prepare-items:campaign-compute-inventory" and
            row["status"] == "complete" for row in events(output.getvalue())))
        self.assertFalse((self.directory / "overlay").exists())


class ComputeReferenceProgressTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.project = Path(self.temp.name)
        target, owner = "1" * 32, "2" * 32
        self.source = self.project / "Assets/Native/Kernel.compute"
        self.source.parent.mkdir(parents=True)
        self.source.write_text("#pragma kernel OriginalKernel\n", encoding="utf-8")
        self.meta = Path(str(self.source) + ".meta")
        self.meta.write_text("fileFormatVersion: 2\nguid: " + target + "\nComputeShaderImporter:\n", encoding="utf-8")
        self.owner = self.project / "Assets/Owner.asset"
        self.owner.write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &11400000\n"
            "MonoBehaviour:\n  field: {fileID: 7200000, guid: " + target + ", type: 2}\n", encoding="utf-8")
        Path(str(self.owner) + ".meta").write_text("fileFormatVersion: 2\nguid: " + owner +
            "\nNativeFormatImporter:\n  mainObjectFileID: 11400000\n", encoding="utf-8")
        self.manifest = {"shaders": [{"guid": target, "classId": 72, "localFileId": 7200000,
            "originalPath": "Assets/Native/Kernel.asset", "assetPath": "Assets/Native/Kernel.compute",
            "sourceSha256": references.digest(self.source.read_bytes()), "metaSha256": references.digest(self.meta.read_bytes())}],
            "pathMap": {"Assets/Native/Kernel.asset": "Assets/Native/Kernel.compute"}}
        identity = self.project / references.IDENTITIES
        identity.parent.mkdir()
        identity.write_text(json.dumps({"schema": 1, "identities": [
            {"guid": target, "path": "Assets/Native/Kernel.asset", "objects": [
                {"classId": 72, "fileId": 7200000, "collection": "fixture-cab", "pathId": 123}]},
            {"guid": owner, "path": "Assets/Owner.asset", "objects": [
                {"classId": 114, "fileId": 11400000, "collection": "fixture-cab", "pathId": 456}]}]}), encoding="utf-8")

    def test_measured_reference_scan_has_identical_dry_run_proof_and_exact_owner_write(self):
        before = self.owner.read_bytes()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="0"):
            disabled = references.repair(self.project, self.manifest, expected_count=1, apply=False)
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output):
            enabled = references.repair(self.project, self.manifest, expected_count=1, apply=False)
            applied = references.repair(self.project, self.manifest, expected_count=1)
        self.assertEqual(enabled, disabled)
        self.assertEqual(applied["changedReferenceCount"], 1)
        self.assertEqual(self.owner.read_bytes(), before.replace(b"type: 2}", b"type: 3}"))
        self.assertTrue((self.project / references.RECEIPT).is_file())
        completed = {row["phase"]: row for row in events(output.getvalue()) if row["status"] == "complete"}
        self.assertEqual(completed["prepare-items:campaign-compute-reference-scan"]["done"], 5)
        self.assertEqual(completed["prepare-items:campaign-compute-reference-index"]["done"], 2)
        self.assertEqual(completed["prepare-items:campaign-compute-reference-targets"]["done"], 1)
        self.assertEqual(completed["prepare-items:campaign-compute-reference-write"]["done"], 1)
        self.assertEqual(completed["prepare-items:campaign-compute-reference-identities"]["done"],
            (self.project / references.IDENTITIES).stat().st_size)
        self.assertEqual(completed["prepare-items:campaign-compute-reference-identity-verify"]["done"],
            (self.project / references.IDENTITIES).stat().st_size)

    def test_bad_owner_cannot_complete_scan_or_start_writes(self):
        self.owner.write_bytes(self.owner.read_bytes().replace(b"type: 2}", b"type: 1}"))
        before = self.owner.read_bytes()
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                self.assertRaisesRegex(references.ComputeRecoveryError, "reference type"):
            references.repair(self.project, self.manifest, expected_count=1)
        self.assertFalse(any(row["phase"] == "prepare-items:campaign-compute-reference-scan" and
            row["status"] == "complete" for row in events(output.getvalue())))
        self.assertFalse(any(row["phase"] == "prepare-items:campaign-compute-reference-write"
            for row in events(output.getvalue())))
        self.assertEqual(before, self.owner.read_bytes())
        self.assertFalse((self.project / references.RECEIPT).exists())


class AppliedCompleteOverlayProgressTests(unittest.TestCase):
    def setUp(self):
        self.fixture = CompleteComputeCounterTests(methodName="runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.doCleanups)
        self.project = self.fixture.source
        self.cache = self.fixture.directory / "cache"
        identities = []
        for index, row in enumerate(self.fixture.originals):
            identities.append({"guid": f"{index + 1:032x}", "path": "Assets/" + row["name"] + ".asset",
                "objects": [{"classId": 72, "fileId": 7200000, "collection": "fixture-cab", "pathId": index + 1}]})
        identity = self.project / references.IDENTITIES
        identity.parent.mkdir()
        identity.write_text(json.dumps({"schema": 1, "identities": identities}), encoding="utf-8")
        self.loader = campaign_compute.campaign_shaders.load

    def load(self, source, relative, extra=None):
        if relative == "tools/quest-shaders/converters.py":
            return types.SimpleNamespace(ensure=lambda *args: {"vkd3d": "fixture-vkd3d", "spirv_cross": "fixture-spirv-cross"})
        return self.loader(source, relative, extra)

    def child(self, *args):
        # The orchestration adapter executes the real overlay stage; only the
        # proprietary parser/native instruction translator use deterministic fixtures.
        self.fixture.run_stage(self.cache / "overlay", False)

    def test_parent_verifies_applies_and_publishes_actual_26_file_overlay_with_measured_counts(self):
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                patch.object(campaign_compute.campaign_shaders, "load", side_effect=self.load), \
                patch.object(campaign_compute, "recovery_process", side_effect=self.child), \
                patch.object(campaign_compute, "remap_manifests", return_value=[]) as remap:
            result = campaign_compute.stage(ROOT, self.project, self.cache)
        remap.assert_called_once_with(self.project, result["pathMap"], progress_scope="campaign-compute")
        self.assertEqual(result["nativeComputeReferenceTypes"]["targetCount"], 13)
        self.assertEqual(result["nativeComputeReferenceTypes"]["ownerCount"], 0)
        for original, generated in result["pathMap"].items():
            self.assertFalse((self.project / original).exists())
            self.assertFalse((self.project / (original + ".meta")).exists())
            self.assertTrue((self.project / generated).is_file())
            self.assertTrue((self.project / (generated + ".meta")).is_file())
        self.assertTrue((self.project / "Assets/QuestOriginalCampaign/campaign-computes.json").is_file())
        self.assertTrue((self.project / "QuestStartupEvidence/compute-source-restoration.json").is_file())
        completed = {row["phase"]: row for row in events(output.getvalue()) if row["status"] == "complete"}
        self.assertEqual(completed["prepare-items:campaign-compute-verify"]["done"], 52)
        self.assertEqual(completed["prepare-items:campaign-compute-apply"]["done"], 52)
        self.assertEqual(completed["prepare-items:campaign-compute-publish"]["done"], 2)

    def test_changed_overlay_never_starts_mutating_originals_or_claims_verification_complete(self):
        before = {path.relative_to(self.project).as_posix(): path.read_bytes()
            for path in self.project.rglob("*") if path.is_file()}
        def changed_child(*args):
            self.child()
            path = next((self.cache / "overlay/Assets").glob("*.compute"))
            path.write_bytes(path.read_bytes() + b"changed")
        output = io.StringIO()
        with patch.dict(os.environ, GHVRQ_WIZARD_PROGRESS="1"), redirect_stdout(output), \
                patch.object(campaign_compute.campaign_shaders, "load", side_effect=self.load), \
                patch.object(campaign_compute, "recovery_process", side_effect=changed_child), \
                self.assertRaisesRegex(BuildError, "differs from its exact native proof"):
            campaign_compute.stage(ROOT, self.project, self.cache)
        self.assertEqual(before, {path.relative_to(self.project).as_posix(): path.read_bytes()
            for path in self.project.rglob("*") if path.is_file()})
        self.assertFalse(any(row["phase"] == "prepare-items:campaign-compute-verify" and row["status"] == "complete"
            for row in events(output.getvalue())))
        self.assertFalse(any(row["phase"] == "prepare-items:campaign-compute-apply"
            for row in events(output.getvalue())))


if __name__ == "__main__":
    unittest.main()
