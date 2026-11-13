"""Recipient personalization: actual SDK certificates, owned files and cold keys.

Only tiny Android archives are signed. Original game/resource conversion and
Unity rendering are outside these controls; the existing binary-writer suite
qualifies Resources separately.
"""
import copy
import json
import os
import tempfile
from pathlib import Path
import sys
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
import test_delivery_signing as delivery


class SignerInvocationProofTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name); self.source = self.output / "source.apk"
        self.source.write_bytes(b"exact selected signed input")
        self.tools = {}
        for name in ("java", "keytool", "apksigner", "zipalign"):
            path = self.output / name; path.write_bytes(b"selected command")
            self.tools[name] = str(path)
        (self.output / "signing").mkdir()
        for name in ("quest.keystore", "local-key.json"):
            (self.output / "signing" / name).write_bytes(b"selected private input")
        self.context = builder._module_context()
        self.args = SimpleNamespace(signing_root=None, signing_mode="auto")
        self.identity = {"schema": 1, "requestedMode": "auto", "mode": "new-owner",
                         "sourceCertificateSha256": "a" * 64, "certificateSha256": "b" * 64}
        self.callbacks = (Mock(), Mock())

    def qualify(self):
        def native(*args, **kwargs):
            kwargs["identity_out"].update(self.identity)
            return self.callbacks
        with patch.object(driver, "signing_tools", return_value=self.tools), patch.object(driver, "_signers", side_effect=native):
            return driver.select_signing(self.context, self.args, self.output, self.source)

    def test_same_native_proof_is_used_only_within_this_invocation(self):
        identity = self.qualify()
        with patch.object(driver, "_signers", side_effect=AssertionError("Unchanged same-invocation source needs no second signature scan")):
            self.assertEqual(driver._run_signers(self.context, self.args, self.output, self.source, self.tools, identity), self.callbacks)
        fresh_args = SimpleNamespace(signing_root=None, signing_mode="auto")
        with patch.object(driver, "_signers", return_value=self.callbacks) as native:
            driver._run_signers(self.context, fresh_args, self.output, self.source, self.tools, identity)
        native.assert_called_once()

    def test_actual_same_size_restored_mtime_edits_reverify_source_key_and_tool(self):
        for path in (self.source, self.output / "signing/local-key.json", Path(self.tools["apksigner"])):
            with self.subTest(path=path.name):
                identity = self.qualify(); before = path.stat(); raw = path.read_bytes()
                path.write_bytes(bytes([raw[0] ^ 1]) + raw[1:]); os.utime(path, ns=(before.st_atime_ns, before.st_mtime_ns))
                with patch.object(driver, "_signers", return_value=self.callbacks) as native:
                    driver._run_signers(self.context, self.args, self.output, self.source, self.tools, identity)
                native.assert_called_once()
                self.assertEqual(native.call_args.kwargs["expected_identity"], identity)

    def test_unknown_change_metadata_never_authorizes_cached_callbacks(self):
        with patch.object(self.context.apk_input_hashes.storage, "_file_witness_stamp", return_value=None):
            identity = self.qualify()
            with patch.object(driver, "_signers", return_value=self.callbacks) as native:
                driver._run_signers(self.context, self.args, self.output, self.source, self.tools, identity)
        native.assert_called_once()


@unittest.skipUnless(all(path.is_file() for path in delivery.NATIVE_FILES), "Pinned Unity Android SDK/JDK commands are unavailable")
class ActualPortableSigningTests(unittest.TestCase):
    setUp = delivery.ActualDeliverySigningTests.setUp
    compressed = staticmethod(delivery.ActualDeliverySigningTests.compressed)
    publish = delivery.ActualDeliverySigningTests.publish

    def test_actual_recipient_uses_own_key_without_original_secrets(self):
        source = self.root / "shared.apk"; self.publish(self.base, source, self.output)
        source_certificate = json.loads((self.output / "signing/certificate.json").read_text())["sha256"]
        source_hash = storage.digest(source)
        recipient = self.root / "recipient"; identity = {}
        sign, verify = driver._signers(self.context, self.tools, recipient, base_apk=source,
                                      signing_mode="auto", identity_out=identity)
        self.assertEqual(identity["mode"], "new-owner")
        self.assertEqual(identity["sourceCertificateSha256"], source_certificate)
        self.assertNotEqual(identity["certificateSha256"], source_certificate)
        personalized = self.root / "personalized.apk"
        manifest = {**self.update, "signing": identity, "sourceCertificateSha256": source_certificate}
        result = apk_update.repack(source, personalized, {apk_update.PROFILE: b'{"displayName":"Recipient"}'},
                                  update_manifest=manifest, signer=sign, verifier=verify)
        self.assertEqual(result["certificateSha256"], identity["certificateSha256"])
        self.assertEqual(storage.digest(source), source_hash)
        self.assertEqual(self.compressed(source, "assets/fixture.resource"),
                         self.compressed(personalized, "assets/fixture.resource"))
        _, _ = driver._signers(self.context, self.tools, recipient, base_apk=personalized,
                              signing_mode="auto", identity_out=(same := {}))
        self.assertEqual(same["mode"], "original")
        with zipfile.ZipFile(personalized) as archive:
            self.assertEqual(json.loads(archive.read(apk_update.UPDATE))["signing"], identity)
            self.assertFalse(any("keystore" in name or "local-key" in name for name in archive.namelist()))
            content = b"".join(archive.read(name) for name in archive.namelist())
        for output in (self.output, recipient):
            password = json.loads((output / "signing/local-key.json").read_text())["password"]
            self.assertNotIn(password.encode(), content)
            self.assertNotIn(password, "".join(path.read_text(errors="replace") for path in (output / "logs").glob("*.log")))

    def test_invalid_source_never_creates_a_recipient_key(self):
        recipient = self.root / "recipient"
        with self.assertRaises(storage.BuildError):
            driver._signers(self.context, self.tools, recipient, base_apk=self.base, signing_mode="new-owner")
        self.assertFalse((recipient / "signing").exists())

    def test_strict_original_mode_and_changed_qualified_identity_still_refuse(self):
        source = self.root / "shared.apk"; self.publish(self.base, source, self.output)
        recipient = self.root / "recipient"
        with self.assertRaisesRegex(storage.BuildError, "original signing folder"):
            driver._signers(self.context, self.tools, recipient, base_apk=source)
        self.assertFalse((recipient / "signing").exists())
        builder.signing(recipient, self.tools)
        with self.assertRaisesRegex(storage.BuildError, "does not belong"):
            driver._signers(self.context, self.tools, recipient, base_apk=source, signing_mode="original")
        expected = {}
        driver._signers(self.context, self.tools, recipient, base_apk=source, signing_mode="auto", identity_out=expected)
        changed = {**expected, "certificateSha256": "0" * 64}
        with self.assertRaisesRegex(storage.BuildError, "changed after update qualification"):
            driver._signers(self.context, self.tools, recipient, base_apk=source,
                            signing_mode="auto", expected_identity=changed)
        self.assertFalse((recipient / "logs/update-sign.log").exists())

    def owned_source(self):
        self.game = self.root / "PC"
        rows = []
        for name in ("GH.Runtime.dll", "GH.Shared.dll"):
            dll = self.game / "Managed" / name; dll.parent.mkdir(parents=True, exist_ok=True)
            dll.write_bytes(("local owned " + name).encode())
            rows.append(storage.record_file(dll, "Managed/" + name))
        self.profile = {"schema": 1, "provider": "steam", "steamId": "76561197960265729",
                        "accountId": 1, "displayName": "Recipient"}
        ownership = {"schema": 1, "provider": "steam", "steamId": self.profile["steamId"],
                     "appId": 780290, "ownedMask": 0, "installedAppIds": []}
        inputs = {"schema": 1, "target": "game", "inputKey": "a" * 64,
                  "game": {"key": "b" * 64, "files": rows},
                  "mod": {"key": "c" * 64, "files": [], "modBuild": 667},
                  "profile": {**self.profile, "dlcOwnership": ownership}}
        capsule = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": inputs["inputKey"],
                   "unityVersion": "2021.3.5f1", "metadataVersion": 29, "packageAbi": "f" * 64,
                   "managedKey": storage.value_hash(apk_update._managed(inputs)),
                   "scriptTypes": [{"assembly": "GH.Runtime.dll", "namespace": "", "class": "Fixture",
                                    "propertiesHash": "1" * 32, "executionOrder": 0}],
                   "modSchema": 1, "dynamicProfile": True}
        with zipfile.ZipFile(self.base) as archive:
            contents = {name: archive.read(name) for name in archive.namelist()}
        contents.update({apk_update.INPUT: storage.canonical(inputs), apk_update.CAPSULE: storage.canonical(capsule)})
        with zipfile.ZipFile(self.base, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name, value in contents.items(): archive.writestr(name, value)
        source = self.root / "shared.apk"; self.publish(self.base, source, self.output)
        context = builder._module_context()
        context.game_data = lambda path: Path(path)
        context.selected_profile = lambda args: (copy.deepcopy(self.profile), b"local logo")
        context.dlcs = SimpleNamespace(capture=lambda *args: copy.deepcopy(ownership))
        context.build_progress = SimpleNamespace(operation=Mock(), event=Mock())
        args = SimpleNamespace(command="update-profile", update_kind="update-profile", game_root=str(self.game),
                               base_apk=str(source), signing_root=None, signing_mode="auto")
        return source, context, args

    def test_actual_profile_only_new_owner_run_and_fast_closed_retry(self):
        source, context, args = self.owned_source()
        recipient = self.root / "recipient"
        tools = driver.normalized_signing_tools(context, self.tools)
        context.command = Mock(wraps=builder.command)
        with patch.object(driver, "signing_tools", return_value=tools), \
             patch.object(context.apk_updates, "patch_resource_json", return_value={}), \
             patch.object(driver, "_compile_code", side_effect=AssertionError("Profile only must not invoke Unity")):
            output = driver.run(context, args, ROOT, recipient)
            source_verifies = [call for call in context.command.call_args_list
                               if "verify" in call.args[0] and call.args[0][-1] == str(source)]
            self.assertEqual(len(source_verifies), 1, "One invocation must not rescan the signed source twice")
            report = json.loads(Path(str(output) + ".build.json").read_text())
            first_plan = driver.inspect(context, args, ROOT, recipient)
            with patch.object(context.apk_updates, "repack", side_effect=AssertionError("The closed recipient update must be reused")):
                self.assertEqual(driver.run(context, args, ROOT, recipient), output)
            self.assertEqual(first_plan["updateKey"], report["updateKey"])
            self.assertEqual(report["signing"]["mode"], "new-owner")
            self.assertNotEqual(report["certificateSha256"], report["sourceCertificateSha256"])
            args.signing_mode = "new-owner"
            changed_plan = driver.inspect(context, args, ROOT, recipient)
            self.assertNotEqual(changed_plan["updateKey"], first_plan["updateKey"])
            other_plan = driver.inspect(context, args, ROOT, self.root / "second recipient")
            self.assertNotEqual(other_plan["signing"]["certificateSha256"], changed_plan["signing"]["certificateSha256"])
            self.assertEqual(other_plan["signing"]["sourceCertificateSha256"], changed_plan["signing"]["sourceCertificateSha256"])
            self.assertNotEqual(other_plan["updateKey"], changed_plan["updateKey"])
            with zipfile.ZipFile(output) as archive:
                self.assertEqual(json.loads(archive.read(apk_update.PROFILE))["displayName"], "Recipient")
                self.assertEqual(archive.read("assets/fixture.resource"), self.payload)
            (self.game / "Managed/GH.Runtime.dll").unlink()
            with patch.object(driver, "select_signing", side_effect=AssertionError("Owned PC presence must precede signing")):
                with self.assertRaisesRegex(storage.BuildError, "does not match this APK's original assembly"):
                    driver.run(context, args, ROOT, recipient)

    def test_zip_preflight_retains_original_package_and_reports_extraction_progress(self):
        source, context, _ = self.owned_source()
        package = self.root / "shared.zip"; package.write_bytes(b"reader independently qualifies format")
        progress, cancel = Mock(), Mock(return_value=False)
        context.portable_packages = SimpleNamespace(import_apk=Mock(return_value=source))
        with self.assertRaisesRegex(storage.BuildError, "requires the Builder workspace"):
            driver.preflight(context, self.game, package)
        base, _, _ = driver.preflight(context, self.game, package, "update-profile", output=self.root / "recipient",
                                     progress=progress, cancel=cancel)
        self.assertEqual(base["resolvedApk"], str(source)); self.assertEqual(base["sourcePackage"], str(package))
        context.portable_packages.import_apk.assert_called_once_with(package, self.root / "recipient", progress=progress, cancel=cancel)


if __name__ == "__main__": unittest.main()
