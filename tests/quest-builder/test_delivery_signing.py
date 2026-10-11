"""Delivery command selection and actual SDK repack/sign/certificate boundaries.

The native proof compiles only a tiny Android manifest with aapt. It does not
import game assets, start Unity or assert headset rendering/playability.
"""
import hashlib
import json
import os
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update
import builder
import storage
import update_driver as driver


class SigningSelectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.context = SimpleNamespace(BuildError=storage.BuildError, signing=Mock(), command=Mock())

    def tools(self, windows=False, *, zipalign=True):
        selected = {"key": "immutable selected toolchain provenance", "jdk": str(self.root)}
        for name in ("java", "keytool", "aapt", "apksigner", "zipalign"):
            filename = "apksigner.jar" if name == "apksigner" else name + (".exe" if windows else "")
            path = self.root / filename; path.write_bytes(b"selected command fixture")
            if name != "zipalign" or zipalign: selected[name] = str(path)
        return selected

    def test_legacy_linux_and_windows_maps_resolve_the_same_selected_sdk(self):
        for windows in (False, True):
            with self.subTest(windows=windows):
                selected = self.tools(windows, zipalign=False); before = dict(selected)
                normalized = driver.normalized_signing_tools(self.context, selected)
                self.assertEqual(normalized["zipalign"], str(self.root / ("zipalign.exe" if windows else "zipalign")))
                self.assertEqual(selected, before)
                self.assertEqual(normalized["key"], selected["key"])

    def test_explicit_command_is_retained_instead_of_adjacent_fallback(self):
        tools = self.tools(); separate = self.root / "selected zipalign"; separate.write_bytes(b"explicit command")
        tools["zipalign"] = str(separate)
        self.assertEqual(driver.normalized_signing_tools(self.context, tools)["zipalign"], str(separate))
        separate.unlink()
        with self.assertRaisesRegex(storage.BuildError, "missing: zipalign"):
            driver.normalized_signing_tools(self.context, tools)

    def test_absent_adjacent_command_does_not_search_a_different_sdk_or_path(self):
        tools = self.tools(zipalign=False); (self.root / "zipalign").unlink()
        with patch.object(driver.shutil, "which", side_effect=AssertionError("No unrelated SDK fallback")):
            with self.assertRaisesRegex(storage.BuildError, "missing: zipalign.*completed Player is retained"):
                driver._signers(self.context, tools, self.root / "output")
        self.context.signing.assert_not_called(); self.context.command.assert_not_called()
        self.assertFalse((self.root / "output").exists())

    def test_incomplete_selected_commands_fail_before_signing_or_repack(self):
        for name in ("java", "keytool", "apksigner", "zipalign"):
            for invalid in (None, "", str(self.root / "missing command")):
                with self.subTest(name=name, invalid=invalid):
                    tools = self.tools(); tools[name] = invalid
                    with self.assertRaisesRegex(storage.BuildError, "missing: " + name):
                        driver._signers(self.context, tools, self.root / "output")
        self.context.signing.assert_not_called(); self.context.command.assert_not_called()

    def test_no_aapt_cannot_guess_a_legacy_zipalign(self):
        tools = self.tools(zipalign=False); del tools["aapt"]
        with self.assertRaisesRegex(storage.BuildError, "lacks aapt"):
            driver.normalized_signing_tools(self.context, tools)

    def test_signing_callbacks_freeze_commands_and_keep_password_out_of_arguments(self):
        tools = self.tools(); key = self.root / "quest.keystore"; key.write_bytes(b"key fixture")
        self.context.signing.return_value = (key, {"password": "fixture private password", "alias": "quest"})
        sign, _ = driver._signers(self.context, tools, self.root / "output")
        expected = tools["zipalign"]; tools["zipalign"] = "mutated unselected executable"
        apk = self.root / "temporary.apk"; apk.write_bytes(b"APK before signing")
        def command(argv, _log, **_kwargs):
            if argv[0] == expected: Path(argv[-1]).write_bytes(b"aligned fixture")
            elif "--out" in argv: Path(argv[argv.index("--out") + 1]).write_bytes(b"signed fixture")
        self.context.command.side_effect = command
        sign(apk)
        self.assertEqual(self.context.command.call_args_list[0].args[0][0], expected)
        self.assertEqual(apk.read_bytes(), b"signed fixture")
        self.assertNotIn("fixture private password", str([call.args for call in self.context.command.call_args_list]))
        self.assertEqual(list(self.root.glob("*.aligned.apk")), [])
        self.assertEqual(list(self.root.glob("*.signed.apk")), [])

    def test_signing_tool_selection_does_not_mutate_the_full_builder_map(self):
        tools = self.tools(zipalign=False); before = dict(tools)
        self.context.toolchain = Mock(return_value=tools)
        selected = driver.signing_tools(self.context, SimpleNamespace(apk_tools_json=None), self.root / "output")
        self.assertIn("zipalign", selected); self.assertEqual(tools, before)

    def test_native_failure_keeps_original_player_and_reports_a_bounded_redacted_tail(self):
        tools = self.tools(); key = self.root / "quest.keystore"; key.write_bytes(b"key fixture")
        password = "fixture private password"
        self.context.signing.return_value = (key, {"password": password, "alias": "quest"})
        sign, _ = driver._signers(self.context, tools, self.root / "output")
        apk = self.root / "temporary.apk"; apk.write_bytes(b"retained original Player")
        def fail(argv, log, **kwargs):
            log.parent.mkdir(parents=True, exist_ok=True)
            log.write_text("old line\n" * 4000 + "Unable to open archive " + password)
            raise storage.BuildError(Path(argv[0]).name + " exited with 1; inspect " + str(log))
        self.context.command.side_effect = fail
        with self.assertRaisesRegex(storage.BuildError, "zipalign exited with 1; inspect") as failure:
            sign(apk)
        self.assertIn("Unable to open archive [redacted]", str(failure.exception))
        self.assertNotIn(password, str(failure.exception))
        self.assertLess(len(str(failure.exception)), 6600)
        self.assertEqual(apk.read_bytes(), b"retained original Player")
        self.assertFalse(apk.with_suffix(".aligned.apk").exists())
        self.assertFalse(apk.with_suffix(".signed.apk").exists())


DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
ANDROID = DATA / "PlaybackEngines/AndroidPlayer"
SUFFIX = ".exe" if os.name == "nt" else ""
BUILD_TOOLS = ANDROID / "SDK/build-tools/30.0.2"
JDK = ANDROID / "OpenJDK"
NATIVE_FILES = [BUILD_TOOLS / (name + SUFFIX) for name in ("aapt", "zipalign")] + [
    BUILD_TOOLS / "lib/apksigner.jar", JDK / "bin" / ("java" + SUFFIX),
    JDK / "bin" / ("keytool" + SUFFIX), ANDROID / "SDK/platforms/android-30/android.jar"]


@unittest.skipUnless(all(path.is_file() for path in NATIVE_FILES), "Pinned Unity Android SDK/JDK commands are unavailable")
class ActualDeliverySigningTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest signing "); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.output = self.root / "work"
        self.base = self.root / "base.apk"
        self.tools = {"aapt": str(BUILD_TOOLS / ("aapt" + SUFFIX)),
                      "apksigner": str(BUILD_TOOLS / "lib/apksigner.jar"),
                      "java": str(JDK / "bin" / ("java" + SUFFIX)),
                      "keytool": str(JDK / "bin" / ("keytool" + SUFFIX)), "key": "legacy toolchain without zipalign"}
        self.context = builder._module_context()
        self.manifest = self.root / "AndroidManifest.xml"
        self.manifest.write_text('<manifest xmlns:android="http://schemas.android.com/apk/res/android" '
            'package="dev.gloomhavenvr.quest" android:versionCode="667" android:versionName="signing-fixture">'
            '<uses-sdk android:minSdkVersion="29" android:targetSdkVersion="30"/>'
            '<application android:label="Delivery signing fixture"/></manifest>')
        builder.command([self.tools["aapt"], "package", "-f", "-M", str(self.manifest), "-I", str(NATIVE_FILES[-1]),
                         "-F", str(self.base)], self.output / "logs/fixture-aapt.log")
        owned = {"path": "Managed/GH.Runtime.dll", "size": 12, "sha256": "b" * 64}
        inputs = {"schema": 1, "target": "game", "inputKey": "a" * 64, "game": {"files": [owned]}, "mod": {"modBuild": 667}}
        library = bytearray(64); library[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", library, 18, 183)
        self.payload = (b"retained compressed original payload" * 20000)
        with zipfile.ZipFile(self.base, "a", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in {apk_update.INPUT: storage.canonical(inputs),
                    apk_update.INSTALLATION: storage.canonical({"schema": 1, "inputKey": "a" * 64}),
                    apk_update.LIBRARY: bytes(library), apk_update.METADATA: struct.pack("<II", 0xFAB11BAF, 29),
                    "assets/fixture.resource": self.payload}.items(): archive.writestr(name, value)
        self.update = {"schema": 1, "gameInputKey": "a" * 64, "updateKey": "c" * 64, "modBuild": 667}

    @staticmethod
    def compressed(path, name):
        with zipfile.ZipFile(path) as archive, path.open("rb") as raw:
            info = archive.getinfo(name); raw.seek(info.header_offset)
            header = struct.unpack("<4s5H3I2H", raw.read(30)); raw.seek(header[-2] + header[-1], 1)
            return raw.read(info.compress_size)

    def publish(self, base, destination, output, *, check_base=False):
        sign, verify = driver._signers(self.context, self.tools, output, base_apk=base if check_base else None)
        return apk_update.repack(base, destination, {apk_update.PROFILE: b'{"displayName":"Signing Fixture Owner"}'},
                                 update_manifest=self.update, signer=sign, verifier=verify)

    def test_actual_sdk_repack_align_sign_verify_and_same_key_update_keep_payloads(self):
        before = self.base.read_bytes(); selected = dict(self.tools)
        first = self.root / "capable.apk"; self.publish(self.base, first, self.output)
        second = self.root / "profile-update.apk"; self.publish(first, second, self.output, check_base=True)
        self.assertEqual(self.base.read_bytes(), before); self.assertEqual(self.tools, selected)
        for apk in (first, second):
            builder.command([str(BUILD_TOOLS / ("zipalign" + SUFFIX)), "-c", "-p", "4", str(apk)],
                            self.output / "logs/fixture-alignment-check.log")
            self.assertEqual(self.compressed(self.base, "assets/fixture.resource"), self.compressed(apk, "assets/fixture.resource"))
            with zipfile.ZipFile(apk) as archive:
                self.assertIsNone(archive.testzip()); self.assertEqual(archive.read("assets/fixture.resource"), self.payload)
                self.assertEqual(json.loads(archive.read(apk_update.UPDATE)), self.update)
        certificate = json.loads((self.output / "signing/certificate.json").read_text())["sha256"]
        self.assertRegex(certificate, r"^[a-f0-9]{64}$")
        self.assertRegex((self.output / "logs/update-signature.log").read_text(),
                         r"Verified using v[23] scheme \(APK Signature Scheme v[23]\): true")
        secret = json.loads((self.output / "signing/local-key.json").read_text())["password"]
        self.assertNotIn(secret, "".join(path.read_text(errors="replace") for path in (self.output / "logs").glob("*.log")))
        self.assertEqual(list(self.root.glob("*.aligned.apk")), []); self.assertEqual(list(self.root.glob("*.signed.apk")), [])

    def test_actual_wrong_certificate_refuses_update_before_repack(self):
        signed = self.root / "signed.apk"; self.publish(self.base, signed, self.output)
        before = hashlib.sha256(signed.read_bytes()).hexdigest()
        other = self.root / "other work"; builder.signing(other, self.tools)
        with self.assertRaisesRegex(storage.BuildError, "does not belong to this APK"):
            driver._signers(self.context, self.tools, other, base_apk=signed)
        self.assertEqual(hashlib.sha256(signed.read_bytes()).hexdigest(), before)
        self.assertFalse((other / "logs/update-align.log").exists())
        self.assertFalse((other / "logs/update-sign.log").exists())

    def test_actual_sdk_accepts_sparse_zip32_offsets_above_two_gib(self):
        path = self.root / "large-offset.apk"
        with apk_update._ApkZip32(path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.fp.seek(zipfile.ZIP64_LIMIT + 123456)
            archive.start_dir = archive.fp.tell()
            archive.writestr("assets/payload", b"sparse central directory native proof" * 100)
        builder.command([str(BUILD_TOOLS / ("zipalign" + SUFFIX)), "-c", "-p", "4", str(path)],
                        self.output / "logs/fixture-large-offset-check.log")
        with zipfile.ZipFile(path) as archive:
            self.assertGreater(archive.getinfo("assets/payload").header_offset, zipfile.ZIP64_LIMIT)
            self.assertEqual(archive.read("assets/payload"), b"sparse central directory native proof" * 100)


if __name__ == "__main__": unittest.main()
