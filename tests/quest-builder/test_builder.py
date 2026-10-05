"""Focused execution and defect controls for the local Quest build pipeline."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import profile as identity
import storage


def png():
    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload) & 0xFFFFFFFF)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">II", 16, 16) + b"\x08\x06\0\0\0") +
            chunk(b"IDAT", zlib.compress((b"\0" + b"\0" * (16 * 4)) * 16)) + chunk(b"IEND", b""))


def arm64_elf_header():
    header = bytearray(64)
    header[:7] = b"\x7fELF\x02\x01\x01"
    struct.pack_into("<HHI", header, 16, 3, 183, 1)
    struct.pack_into("<H", header, 52, 64)
    return bytes(header)


class Temporary(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()


class IdentityTests(Temporary):
    def test_full_id_does_not_become_account_number(self):
        result = identity.validate_identity({"steamId": str(identity.STEAM_INDIVIDUAL_BASE + 0xF1234567),
                                             "displayName": "A quote: \"; ü"})
        self.assertEqual(result["accountId"], 0xF1234567)
        self.assertGreater(int(result["steamId"]), 0xFFFFFFFF)

    def test_identity_rejects_mismatch_and_credentials(self):
        for extra in ({"accountId": 12}, {"token": "not-a-real-token"}, {"accountId": True}):
            with self.subTest(extra=list(extra)), self.assertRaises(identity.ProfileError):
                identity.validate_identity({"steamId": str(identity.STEAM_INDIVIDUAL_BASE + 1),
                                            "displayName": "fixture", **extra})

    def test_zero_only_through_explicit_dummy_factory(self):
        with self.assertRaises(identity.ProfileError):
            identity.validate_identity(identity.dummy_identity())
        result = identity.dummy_identity()
        self.assertEqual(result["steamId"], "0")
        self.assertTrue(result["isDummy"])
        self.assertIn("DUMMY", result["displayName"])

    def test_vdf_escapes_unicode_and_ambiguity(self):
        steam = self.root / "Steam/config"
        steam.mkdir(parents=True)
        one = str(identity.STEAM_INDIVIDUAL_BASE + 1)
        two = str(identity.STEAM_INDIVIDUAL_BASE + 2)
        (steam / "loginusers.vdf").write_text(
            '// remembered accounts\n"users" {"' + one + '" {"PersonaName" "quote \\\"ü\\\"" "MostRecent" "1"} "' +
            two + '" {"PersonaName" "Second" "MostRecent" "0"}}', encoding="utf-8")
        with self.assertRaises(identity.ProfileError):
            identity.capture_steam_profile(steam.parent, None)
        result = identity.capture_steam_profile(steam.parent, one)
        self.assertEqual(result["displayName"], 'quote "ü"')
        self.assertNotIn("MostRecent", result)
        self.assertEqual(result["source"], "loginusers.vdf")

    def test_vdf_malformed_controls(self):
        for raw in ('"users" {', '"users" {} "users" {}', '"users" {"value" }', 'unquoted {}'):
            with self.subTest(raw=raw), self.assertRaises(identity.ProfileError):
                identity.parse_vdf(raw)

    def test_windows_active_account_selected_and_mismatch_blocked(self):
        steam = self.root / "Steam"
        (steam / "config").mkdir(parents=True)
        one, two = (str(identity.STEAM_INDIVIDUAL_BASE + n) for n in (1, 2))
        (steam / "config/loginusers.vdf").write_text('"users" {"' + one + '" {"PersonaName" "One"} "' +
                                                   two + '" {"PersonaName" "Two"}}')
        with patch.object(identity, "windows_steam_context", return_value=(steam, two)):
            selected = identity.capture_steam_profile(steam, None)
            self.assertEqual(selected["steamId"], two)
            self.assertEqual(selected["accountId"], 2)
            self.assertIn("active", selected["source"])
            with self.assertRaises(identity.ProfileError):
                identity.capture_steam_profile(steam, one)

    def test_png_corruption_is_not_a_valid_profile_resource(self):
        logo = self.root / "logo.png"
        broken = bytearray(png())
        broken[29] ^= 1
        logo.write_bytes(broken)
        with self.assertRaises(identity.ProfileError):
            identity.read_logo(logo)

    def test_logo_and_profile_shape(self):
        logo = self.root / "logo.png"
        logo.write_bytes(png())
        profile = self.root / "profile.json"
        profile.write_text(json.dumps({"steamId": str(identity.STEAM_INDIVIDUAL_BASE + 1), "displayName": "fixture"}))
        value, content = identity.load_profile(profile, None, None, logo)
        self.assertEqual(value["logoSha256"], storage.digest(logo))
        self.assertEqual(content, png())
        logo.write_bytes(b"not PNG")
        with self.assertRaises(identity.ProfileError):
            identity.read_logo(logo)


class SnapshotTests(Temporary):
    @unittest.skipIf(os.name == "nt", "Requires symlink creation rights")
    def test_internal_unity_shared_source_link_is_materialized(self):
        source = self.root / "source"
        source.mkdir()
        (source / "Shared.cs").write_text("public class Shared {}")
        (source / "Link.cs").symlink_to("Shared.cs")
        records = storage.inventory(source)
        target = self.root / "snapshot"
        storage.snapshot(source, records, target)
        self.assertFalse((target / "Link.cs").is_symlink())
        self.assertEqual((target / "Link.cs").read_text(), (target / "Shared.cs").read_text())
        (self.root / "private").write_text("fixture secret")
        (source / "Escape.cs").symlink_to("../private")
        with self.assertRaises(storage.BuildError):
            storage.inventory(source)

    def test_snapshot_is_copied_not_linked_and_rejects_tampering(self):
        source = self.root / "source"
        source.mkdir()
        (source / "original").write_bytes(b"first")
        records = storage.inventory(source)
        target = self.root / "snapshots" / storage.value_hash(records)
        storage.snapshot(source, records, target)
        self.assertNotEqual((source / "original").stat().st_ino, (target / "original").stat().st_ino)
        (source / "original").write_bytes(b"after")
        self.assertEqual((target / "original").read_bytes(), b"first")
        storage.snapshot(source, records, target)  # Existing immutable contents, not live originals.
        (target / "original").write_bytes(b"taint")
        with self.assertRaises(storage.BuildError):
            storage.snapshot(source, records, target)

    def test_change_during_copy_leaves_no_success(self):
        source = self.root / "source"
        source.mkdir()
        (source / "file").write_bytes(b"before")
        records = storage.inventory(source)
        (source / "file").write_bytes(b"changed")
        target = self.root / "snapshots/hash"
        with self.assertRaises(storage.BuildError):
            storage.snapshot(source, records, target)
        self.assertFalse(target.exists())
        self.assertFalse((target.with_name(target.name + ".staging") / ".snapshot.json").exists())

    def test_stage_reuse_invalidates_changed_outputs(self):
        output = self.root / "output"
        output.mkdir()
        artifact = output / "artifact"
        calls = []

        def run():
            calls.append(1)
            artifact.write_bytes(b"first")
            return [artifact], {"proof": "fixture"}

        stages = storage.Stages(output)
        stages.run("fixture", "a", run)
        stages.run("fixture", "a", run)
        self.assertEqual(len(calls), 1)
        artifact.write_bytes(b"other")
        stages.run("fixture", "a", run)
        self.assertEqual(len(calls), 2)

    def test_failed_stage_removes_previous_success(self):
        output = self.root / "output"
        output.mkdir()
        artifact = output / "artifact"
        artifact.write_bytes(b"first")
        stages = storage.Stages(output)
        stages.run("fixture", "a", lambda: ([artifact], {}))
        artifact.unlink()

        def fail():
            raise storage.BuildError("deliberate negative control")

        with self.assertRaises(storage.BuildError):
            stages.run("fixture", "a", fail)
        self.assertFalse(stages.path("fixture", "a").exists())
        self.assertEqual(json.loads((output / "last-failure.json").read_text())["stage"], "fixture")

    def test_external_receipt_cannot_claim_arbitrary_files(self):
        output = self.root / "output"
        output.mkdir()
        artifact = self.root / "outside"
        artifact.write_bytes(b"first")
        stages = storage.Stages(output)
        storage.write_json(stages.path("fixture", "a"), {"schema": 1, "stage": "fixture", "key": "a",
            "outputs": [storage.record_file(artifact, "../outside")]})
        self.assertIsNone(stages.valid("fixture", "a"))


class SafetyAndProcessTests(Temporary):
    def test_output_safety_controls(self):
        repo = self.root / "repo"
        game = self.root / "game"
        repo.mkdir()
        game.mkdir()
        for path in (repo, self.root, game / "converted", repo / "src/generated"):
            with self.subTest(path=path), self.assertRaises(storage.BuildError):
                storage.ensure_output(path, repo, game)
        output = repo / ".planning/quest3-local"
        storage.ensure_output(output, repo, game)
        self.assertTrue((output / ".quest-builder-output.json").exists())
        storage.ensure_output(output, repo, game)
        foreign = self.root / "foreign"
        foreign.mkdir()
        (foreign / "keep").write_text("unrelated")
        with self.assertRaises(storage.BuildError):
            storage.ensure_output(foreign, repo, game)
        self.assertEqual((foreign / "keep").read_text(), "unrelated")

    @unittest.skipIf(os.name == "nt", "Requires symlink creation rights")
    def test_output_symlink_is_rejected(self):
        repo = self.root / "repo"
        real = self.root / "real"
        repo.mkdir()
        real.mkdir()
        link = self.root / "alias"
        link.symlink_to(real, target_is_directory=True)
        with self.assertRaises(storage.BuildError):
            storage.ensure_output(link, repo)

    def test_lock_exclusion_and_exception_release(self):
        output = self.root / "output"
        output.mkdir()
        with self.assertRaises(RuntimeError):
            with storage.output_lock(output):
                with self.assertRaises(storage.BuildError):
                    with storage.output_lock(output):
                        self.fail("second builder acquired the output")
                raise RuntimeError("interrupt fixture")
        self.assertFalse((output / ".builder.lock").exists())

    def test_real_subprocess_failure_and_literal_arguments(self):
        text = builder.command([sys.executable, "-c", "import sys; print(sys.argv[1])", 'literal $(echo secret); quote "'],
                               self.root / "success.log")
        self.assertIn('literal $(echo secret); quote "', text)
        with self.assertRaises(storage.BuildError):
            builder.command([sys.executable, "-c", "raise SystemExit(17)"], self.root / "failure.log")

    def test_game_detection_requires_real_content(self):
        data = self.root / "game/GH_Data"
        (data / "Managed").mkdir(parents=True)
        for name in ("GH.Runtime.dll", "GH.Shared.dll"):
            (data / "Managed" / name).write_bytes(b"fixture")
        with self.assertRaises(storage.BuildError):
            builder.game_data(data.parent)
        for name in ("globalgamemanagers", "resources.assets"):
            (data / name).write_bytes(b"Unity 2021.3.5f1 fixture")
        (data / "StreamingAssets/Rulebase").mkdir(parents=True)
        self.assertEqual(builder.game_data(data.parent), data)
        self.assertEqual(builder.original_version(data), "2021.3.5f1")


class ApkTests(Temporary):
    def setUp(self):
        super().setUp()
        self.output = self.root
        self.inputs = {"target": "probe", "inputKey": "chosen", "profileKey": "identity", "profile": {"isDummy": True}}
        self.tools = {"unityVersion": "2021.3.5f1", "java": "fixture-java", "apksigner": "fixture-apksigner.jar", "aapt": "fixture-aapt"}
        profile = self.output / "identities/identity/quest-profile.json"
        storage.write_json(profile, identity.dummy_identity())
        self.apk = self.output / "fixture.apk"
        self.evidence = self.output / "fixture.apk.build.json"
        self.metadata = {"schema": 1, "target": "probe", "inputKey": "chosen", "package": builder.PACKAGE,
            "profileSha256": storage.digest(profile), "unityVersion": "2021.3.5f1", "buildResult": "Succeeded"}
        storage.write_json(self.evidence, self.metadata)

    def fixture_apk(self, extra=None, omit=None, malformed=None):
        with zipfile.ZipFile(self.apk, "w") as archive:
            for name in ("AndroidManifest.xml", "lib/arm64-v8a/libil2cpp.so", "lib/arm64-v8a/libunity.so",
                         "lib/arm64-v8a/libghvr_quest_passthrough.so", "lib/arm64-v8a/libUnityOpenXR.so",
                         "lib/arm64-v8a/libopenxr_loader.so",
                         "assets/bin/Data/Managed/Metadata/global-metadata.dat"):
                if name == omit:
                    continue
                data = arm64_elf_header() if name.endswith(".so") else b"fixture only"
                if malformed and name == "lib/arm64-v8a/libghvr_quest_passthrough.so":
                    data = malformed
                archive.writestr(name, data)
            if extra:
                archive.writestr(extra, b"fixture only")

    def tool_output(self, argv, *args, **kwargs):
        if argv[0] == "fixture-java":
            self.assertEqual(argv[1:3], ["-jar", "fixture-apksigner.jar"])
            return "Signer #1 certificate SHA-256 digest: " + "a" * 64
        return "package: name='" + builder.PACKAGE + "' versionCode='1'"

    def campaign_fixture(self, omit=None, input_key=None):
        self.inputs["target"] = "game"
        self.metadata["target"] = "game"
        self.metadata["graphicsApi"] = "Vulkan"
        storage.write_json(self.evidence, self.metadata)
        self.fixture_apk()
        bank = self.apk.parent / "GloomhavenVR-Quest-content.zip"
        with zipfile.ZipFile(bank, "w") as archive:
            archive.writestr("StreamingAssets/Rulebase/fixture", b"owned fixture")
        with zipfile.ZipFile(self.apk, "a") as archive:
            for name in ("libQuestApparance.so", "libquest_box64.so", "libquest_wineserver.so", "libopus_egpv.so"):
                if name != omit: archive.writestr("lib/arm64-v8a/" + name, arm64_elf_header())
            archive.writestr("assets/Quest/content-delivery.json", json.dumps({"schema": 1, "package": builder.PACKAGE,
                "inputKey": input_key or self.inputs["inputKey"], "files": [{"file": bank.name, "archive": "quest-startup-content.zip",
                "sha256": storage.digest(bank), "size": bank.stat().st_size}]}))
        return bank

    def test_campaign_native_abis_and_adjacent_bank_are_required(self):
        bank = self.campaign_fixture()
        with patch.object(builder, "command", self.tool_output):
            evidence = builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)
        self.assertFalse(evidence["isDiagnostic"])
        self.assertEqual(evidence["contentFiles"], [storage.record_file(bank, bank.name)])
        bank.write_bytes(b"changed")
        with self.assertRaisesRegex(storage.BuildError, "content bank changed"):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_campaign_requires_original_depth_compatible_graphics_backend(self):
        self.campaign_fixture()
        self.metadata["graphicsApi"] = "OpenGLES3"
        storage.write_json(self.evidence, self.metadata)
        with self.assertRaisesRegex(storage.BuildError, "selected inputs: graphicsApi"):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_campaign_rejects_missing_executable_or_wrong_bank_owner(self):
        self.campaign_fixture(omit="libquest_box64.so")
        with self.assertRaisesRegex(storage.BuildError, "native game ABI"):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)
        self.campaign_fixture(input_key="another-build")
        with self.assertRaisesRegex(storage.BuildError, "delivery differs"):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_evidence_and_native_architecture_controls(self):
        self.fixture_apk()
        with patch.object(builder, "command", self.tool_output):
            details = builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)
        self.assertTrue(details["isDiagnostic"])
        self.assertTrue(details["isDummy"])
        self.assertEqual(details["apkSha256"], storage.digest(self.apk))
        self.metadata["inputKey"] = "stale"
        storage.write_json(self.evidence, self.metadata)
        with self.assertRaises(storage.BuildError):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)
        self.metadata["inputKey"] = "chosen"
        storage.write_json(self.evidence, self.metadata)
        self.fixture_apk("lib/x86/libunity.so")
        with self.assertRaises(storage.BuildError):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_changed_signing_certificate_is_rejected(self):
        self.fixture_apk()
        storage.write_json(self.output / "signing/certificate.json", {"sha256": "b" * 64})
        with patch.object(builder, "command", self.tool_output), self.assertRaises(storage.BuildError):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_actual_build_provenance_survives_validation_and_mismatch_rejects(self):
        provenance = {"schema": 1, "runtime": {"sourceCommit": "frozen-runtime"},
                      "stagedEditorSources": [{"path": "Assets/Quest/Editor/Correction.cs", "sha256": "c" * 64}]}
        self.metadata["buildProvenance"] = provenance
        storage.write_json(self.evidence, self.metadata)
        self.fixture_apk()
        with patch.object(builder, "command", self.tool_output):
            details = builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output, provenance)
        self.assertEqual(details["buildProvenance"], provenance)
        self.assertEqual(details["buildReport"]["buildProvenance"], provenance)
        self.metadata["buildProvenance"] = {"schema": 1, "runtime": {"sourceCommit": "different-runtime"}}
        storage.write_json(self.evidence, self.metadata)
        with self.assertRaisesRegex(storage.BuildError, "build-tool provenance"):
            builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output, provenance)

    def test_signer_receives_literal_apk_path_through_java(self):
        self.fixture_apk()
        renamed = self.apk.with_name("Campaign & 100% literal.apk")
        self.apk.rename(renamed)
        calls = []
        def run(argv, *args, **kwargs):
            calls.append(argv)
            return self.tool_output(argv, *args, **kwargs)
        with patch.object(builder, "command", run):
            builder.validate_apk(renamed, self.evidence, self.inputs, self.tools, self.output)
        self.assertEqual(calls[0], ["fixture-java", "-jar", "fixture-apksigner.jar", "verify",
                                   "--verbose", "--print-certs", str(renamed)])

    def test_quest_passthrough_openxr_and_loader_are_mandatory(self):
        for name in ("libghvr_quest_passthrough.so", "libUnityOpenXR.so", "libopenxr_loader.so"):
            self.fixture_apk(omit="lib/arm64-v8a/" + name)
            with self.subTest(name=name), self.assertRaises(storage.BuildError):
                builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_mislabeled_arm64_libraries_fail_elf_validation(self):
        valid = arm64_elf_header()
        bad_class = bytearray(valid)
        bad_class[4] = 1
        bad_endian = bytearray(valid)
        bad_endian[5] = 2
        wrong_machine = bytearray(valid)
        struct.pack_into("<H", wrong_machine, 18, 62)  # x86-64 inside an ARM64 path.
        executable = bytearray(valid)
        struct.pack_into("<H", executable, 16, 2)
        for content in (bytes(bad_class), bytes(bad_endian), bytes(wrong_machine), bytes(executable), valid[:32], b"not ELF"):
            self.fixture_apk(malformed=content)
            with self.subTest(content=content[:24].hex()), self.assertRaises(storage.BuildError):
                builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)

    def test_required_eye_tracking_and_eye_permission_rejected_optional_feature_allowed(self):
        self.fixture_apk()
        for line in ("uses-feature: name='oculus.software.eye_tracking'",
                     "uses-permission: name='com.oculus.permission.EYE_TRACKING'",
                     "uses-permission-sdk-23: name='com.oculus.permission.EYE_TRACKING'"):
            def tool_output(argv, *args, **kwargs):
                result = self.tool_output(argv, *args, **kwargs)
                return result + ("\n" + line if argv[0] == "fixture-aapt" else "")
            with self.subTest(line=line), patch.object(builder, "command", tool_output), self.assertRaises(storage.BuildError):
                builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)
        def optional_feature(argv, *args, **kwargs):
            result = self.tool_output(argv, *args, **kwargs)
            return result + ("\nuses-feature-not-required: name='oculus.software.eye_tracking'" if argv[0] == "fixture-aapt" else "")
        with patch.object(builder, "command", optional_feature):
            self.assertTrue(builder.validate_apk(self.apk, self.evidence, self.inputs, self.tools, self.output)["isDiagnostic"])

    def test_device_ambiguity_does_not_install(self):
        fake_adb = self.root / "adb"
        fake_adb.write_text("fixture")
        args = argparse.Namespace(adb=str(fake_adb), serial=None)
        calls = []

        def result(argv, *a, **k):
            calls.append(argv)
            return "List of devices attached\none device\ntwo device\n"

        with patch.object(builder, "verified_latest_build", return_value=(self.apk, {})), \
                patch.object(builder, "command", result), self.assertRaises(storage.BuildError):
            builder.install(args, self.output)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][-1], "devices")


class DevelopmentAndDeploymentTests(Temporary):
    def setUp(self):
        super().setUp()
        self.repo = self.root / "repo"
        self.repo.mkdir()
        subprocess.run(["git", "init", "-q", str(self.repo)], check=True)
        files = {
            ".gitignore": "*.env\nbin/\nobj/\n*.dll\n",
            "src/GloomhavenVR/Net/NetProtocol.cs": "public const ushort ModBuild = 607;\n",
            "src/GloomhavenVR/Core/Loc/QuestText.cs": "public static class QuestText {}\n",
            "unity/GloomhavenVR.Quest/Assets/Quest/Runtime/Probe.cs": "public class Probe {}\n",
            "unity/GloomhavenVR.Quest/Packages/manifest.json": "{\"dependencies\":{}}\n",
            "unity/GloomhavenVR.Quest/ProjectSettings/ProjectVersion.txt": "m_EditorVersion: 2021.3.5f1\n",
        }
        for name, raw in files.items():
            destination = self.repo / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            destination.write_text(raw)
        subprocess.run(["git", "-C", str(self.repo), "add", "."], check=True)
        subprocess.run(["git", "-C", str(self.repo), "-c", "user.name=Fixture", "-c",
                        "user.email=fixture@example.invalid", "commit", "-qm", "fixture"], check=True)
        self.data = self.root / "game/GH_Data"
        (self.data / "Managed").mkdir(parents=True)
        for name in ("GH.Runtime.dll", "GH.Shared.dll"):
            (self.data / "Managed" / name).write_bytes(b"fixture managed binary")
        for name in ("globalgamemanagers", "resources.assets"):
            (self.data / name).write_bytes(b"2021.3.5f1 original bytes")
        (self.data / "StreamingAssets/Rulebase").mkdir(parents=True)
        self.logo = self.root / "logo.png"
        self.logo.write_bytes(png())
        self.output = storage.ensure_output(self.root / "output", self.repo, self.data)
        self.args = builder.parser().parse_args([
            "prepare", "--repo-root", str(self.repo), "--game-root", str(self.data),
            "--output-root", str(self.output), "--target", "probe", "--dummy-profile", "--steam-logo", str(self.logo)])

    def test_startup_restores_post_effects_before_movie_or_native_staging(self):
        inputs = builder.inspect_inputs(self.args, self.repo, self.output, self.data)
        source, game = builder.snapshot_inputs(inputs, self.output, self.repo, self.data)
        self.args.target = "startup"
        inputs["startupProject"] = {"key": "a" * 64}
        (self.output / "inputs/startup" / inputs["startupProject"]["key"] / "Assets").mkdir(parents=True)
        with patch.object(builder.startup, "inspect_project") as inspect, \
                patch.object(builder.post_effects, "restore_post_effects", side_effect=storage.BuildError("shader-adapter-boundary")) as restore, \
                patch.object(builder.startup, "stage_startup_movies") as movies:
            with self.assertRaisesRegex(storage.BuildError, "shader-adapter-boundary"):
                builder.prepare(self.args, inputs, self.output, source, game)
            inspect.assert_called_once()
            self.assertTrue(inspect.call_args.kwargs["snapshot_receipt"])
            restore.assert_called_once()
            self.assertEqual(restore.call_args.args[1], self.output / "tool-cache/legacy-post-effects")
            self.assertTrue((restore.call_args.args[0] / "Assets/Quest").is_dir())
            movies.assert_not_called()
            key = storage.value_hash({"input": inputs["inputKey"], "recipe": builder.RECIPE})
            self.assertIsNone(storage.Stages(self.output).valid("prepare", key))

    def test_n_to_n_plus_one_captures_new_code_art_config_without_recipe_edit(self):
        first = builder.inspect_inputs(self.args, self.repo, self.output, self.data)
        source, game = builder.snapshot_inputs(first, self.output, self.repo, self.data)
        project = builder.prepare(self.args, first, self.output, source, game)
        self.assertEqual((project / "Assets/Quest/Runtime/QuestText.cs").read_text(), "public static class QuestText {}\n")
        self.assertEqual(builder.prepare(self.args, first, self.output, source, game), project)
        # Ordinary untracked additions use normal source/resource directories.
        # The builder itself and its recipe remain unchanged throughout the rehearsal.
        for name, text in {
            "src/GloomhavenVR/Patches/NewPatch.cs": "public class NewPatch {}\n",
            "src/GloomhavenVR/Defaults/NewOptions.cs": "public class NewOptions {}\n",
            "src/GloomhavenVR/Assets/new-art.png": "fixture image payload",
            "unity/GloomhavenVR.Assets/Assets/Shader/NewShader.shader": "Shader \"fixture/new\" {}\n",
        }.items():
            path = self.repo / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text)
        (self.repo / "src/GloomhavenVR/Net/NetProtocol.cs").write_text("public const ushort ModBuild = 608;\n")
        (self.repo / "src/GloomhavenVR/private.env").write_text("SECRET=fixture-only")
        second = builder.inspect_inputs(self.args, self.repo, self.output, self.data)
        self.assertEqual(first["game"]["key"], second["game"]["key"])
        self.assertNotEqual(first["mod"]["key"], second["mod"]["key"])
        self.assertEqual(second["mod"]["modBuild"], 608)
        self.assertTrue(second["mod"]["dirty"])
        names = {item["path"] for item in second["mod"]["files"]}
        self.assertIn("src/GloomhavenVR/Patches/NewPatch.cs", names)
        self.assertIn("src/GloomhavenVR/Defaults/NewOptions.cs", names)
        self.assertIn("src/GloomhavenVR/Assets/new-art.png", names)
        self.assertIn("unity/GloomhavenVR.Assets/Assets/Shader/NewShader.shader", names)
        self.assertNotIn("src/GloomhavenVR/private.env", names)
        new_source, new_game = builder.snapshot_inputs(second, self.output, self.repo, self.data)
        self.assertEqual(new_game, game)
        self.assertNotEqual(new_source, source)
        self.assertFalse((new_source / "src/GloomhavenVR/private.env").exists())
        new_project = builder.prepare(self.args, second, self.output, new_source, new_game)
        self.assertNotEqual(project, new_project)
        (new_project / "Library").mkdir()
        (new_project / "Library/imported-artifact").write_bytes(b"retain expensive import")
        (new_project / "Assets/Quest/Runtime/Probe.cs").write_text("changed after selection")
        builder.prepare(self.args, second, self.output, new_source, new_game)
        self.assertEqual((new_project / "Assets/Quest/Runtime/Probe.cs").read_text(), "public class Probe {}\n")
        self.assertEqual((new_project / "Library/imported-artifact").read_bytes(), b"retain expensive import")

    def test_probe_slice_hash_invalidation_and_no_managed_executable_ingress(self):
        assets = self.root / "probe-assets"
        (assets / "Resources").mkdir(parents=True)
        (assets / "Resources/quest-original-model.prefab").write_text("native model fixture")
        (assets / "Resources/quest-original-model.prefab.meta").write_text("guid: " + "a" * 32)
        self.args.probe_assets = assets
        first = builder.inspect_inputs(self.args, self.repo, self.output, self.data)
        source, game = builder.snapshot_inputs(first, self.output, self.repo, self.data, assets)
        project = builder.prepare(self.args, first, self.output, source, game)
        self.assertEqual((project / "Assets/Quest/Recovered/Resources/quest-original-model.prefab").read_text(),
                         "native model fixture")
        (assets / "Resources/quest-original-model.prefab").write_text("revised native model fixture")
        second = builder.inspect_inputs(self.args, self.repo, self.output, self.data)
        self.assertNotEqual(first["probeAssets"]["key"], second["probeAssets"]["key"])
        self.assertEqual(first["game"]["key"], second["game"]["key"])
        (assets / "Unexpected.cs").write_text("public class Unexpected {}")
        with self.assertRaises(storage.BuildError):
            builder.inspect_inputs(self.args, self.repo, self.output, self.data)

    def test_rewritten_original_dll_keeps_exact_meta_and_no_duplicate_type_location(self):
        staged = self.root / "woven"
        staged.mkdir()
        (staged / "GH.Runtime.dll").write_bytes(b"woven original")
        (staged / "GloomhavenVR.dll").write_bytes(b"new mod")
        (staged / "QuestWeaver.Runtime.dll").write_bytes(b"generated runtime")
        (staged / "link.xml").write_text("<linker/>")
        project = self.root / "project"
        plugins = project / "Assets/Plugins"
        plugins.mkdir(parents=True)
        original = plugins / "GH.Runtime.dll"
        original.write_bytes(b"recovered original")
        meta = Path(str(original) + ".meta")
        meta.write_text("guid: " + "b" * 32)
        builder.deploy_woven_assemblies(staged, self.data, project)
        self.assertEqual(original.read_bytes(), b"woven original")
        self.assertEqual(meta.read_text(), "guid: " + "b" * 32)
        self.assertEqual(len(list((project / "Assets").rglob("GH.Runtime.dll"))), 1)
        self.assertEqual((plugins / "QuestGame/GloomhavenVR.dll").read_bytes(), b"new mod")
        self.assertEqual((project / "Assets/Quest/Generated/link.xml").read_text(), "<linker/>")
        meta.unlink()
        with self.assertRaises(storage.BuildError):
            builder.deploy_woven_assemblies(staged, self.data, project)

    def test_real_recovery_process_zero_is_not_full_game_readiness(self):
        launcher = self.repo / "tools/quest-recovery/full_recovery.py"
        launcher.parent.mkdir(parents=True)
        launcher.write_text(
            'import argparse,json\nfrom pathlib import Path\np=argparse.ArgumentParser()\n'
            'p.add_argument("--game-data");p.add_argument("--output-project");p.add_argument("--workspace");'
            'p.add_argument("--tool-cache");p.add_argument("--dotnet");p.add_argument("--managed-dotnet")\n'
            'a=p.parse_args();root=Path(a.output_project);(root/"Assets").mkdir(parents=True)\n'
            '(Path(a.workspace)/"full-recovery.json").write_text(json.dumps({"schema":1,'
            '"fullOriginalCatalogRecovered":False,"androidPlayerBuilt":False}))\n')
        with patch("dependencies.python_environment", return_value=Path(sys.executable)), \
                patch("dependencies.dotnet10", return_value=Path(sys.executable)):
            result = builder.main(["prepare", "--repo-root", str(self.repo), "--game-root", str(self.data),
                                   "--output-root", str(self.output), "--target", "game", "--dummy-profile",
                                   "--steam-logo", str(self.logo), "--dotnet", sys.executable])
        self.assertEqual(result, 1)
        failure = json.loads((self.output / "last-failure.json").read_text())
        self.assertEqual(failure["stage"], "recovery")
        self.assertIn("did not finish the original catalog", failure["message"])
        self.assertFalse((self.output / "projects").exists())
        self.assertEqual(list((self.output / "receipts").glob("build/*.json")), [])


if __name__ == "__main__":
    unittest.main()
