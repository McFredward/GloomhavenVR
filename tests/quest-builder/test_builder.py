"""Focused execution and defect controls for the local Quest build pipeline."""

import argparse
import importlib.util
import json
import os
from pathlib import Path
import struct
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
        self.assertEqual(list(target.parent.iterdir()), [])

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
        self.tools = {"unityVersion": "2021.3.5f1", "apksigner": "fixture-apksigner", "aapt": "fixture-aapt"}
        profile = self.output / "identities/identity/quest-profile.json"
        storage.write_json(profile, identity.dummy_identity())
        self.apk = self.output / "fixture.apk"
        self.evidence = self.output / "fixture.apk.build.json"
        self.metadata = {"schema": 1, "target": "probe", "inputKey": "chosen", "package": builder.PACKAGE,
            "profileSha256": storage.digest(profile), "unityVersion": "2021.3.5f1", "buildResult": "Succeeded"}
        storage.write_json(self.evidence, self.metadata)

    def fixture_apk(self, extra=None):
        with zipfile.ZipFile(self.apk, "w") as archive:
            for name in ("AndroidManifest.xml", "lib/arm64-v8a/libil2cpp.so", "lib/arm64-v8a/libunity.so",
                         "assets/bin/Data/Managed/Metadata/global-metadata.dat"):
                archive.writestr(name, b"fixture only")
            if extra:
                archive.writestr(extra, b"fixture only")

    def tool_output(self, argv, *args, **kwargs):
        if argv[0] == "fixture-apksigner":
            return "Signer #1 certificate SHA-256 digest: " + "a" * 64
        return "package: name='" + builder.PACKAGE + "' versionCode='1'"

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


if __name__ == "__main__":
    unittest.main()
