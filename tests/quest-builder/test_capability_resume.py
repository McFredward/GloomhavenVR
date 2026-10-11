"""Exact completed signing work resumes independently of later delivery gates.

Native signature/Unity-format coverage lives in test_delivery_signing and the
retained real-Player proof. These cases use explicit injected signing callbacks.
"""
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'tools/quest-builder'))
import apk_update
import storage
import update_driver as driver
import test_update_driver as fixture


class CapabilityResumeTests(unittest.TestCase):
    def setUp(self):
        self.write_apk = lambda: fixture.UpdateDriverTests.write_apk(self)
        fixture.UpdateDriverTests.setUp(self)
        # The fixture input Player is outside the build output; publish a copy
        # inside the real stage owner before exercising exact receipt reuse.
        self.apk = self.output / 'builds/player/GloomhavenVR-Quest.apk'
        self.apk.parent.mkdir(parents=True)
        self.write_apk()
        self.signed = Mock(); self.verified = Mock()
        self.builder.record_file = storage.record_file
        def callbacks(*args, **kwargs):
            for name, value in {'quest.keystore': b'fixture selected signing key',
                                'local-key.json': b'{"password":"fixture secret","alias":"quest"}',
                                'certificate.json': b'{"sha256":"' + b'a' * 64 + b'"}'}.items():
                path = self.output / 'signing' / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_bytes(value)
            return self.signed, self.verified
        self.callbacks = Mock(side_effect=callbacks)
        self.original_make_capsule = apk_update.make_capsule
        self.addCleanup(patch.stopall)
        patch.object(driver, 'package_fingerprint', return_value=self.capsule['packageAbi']).start()
        patch.object(driver, '_signers', self.callbacks).start()
        self.roster = patch.object(apk_update, 'make_capsule', return_value=self.capsule).start()

    def build(self):
        return driver.add_update_capability(self.builder, self.apk, self.manifest, self.output, self.root, {})

    def test_completed_capability_receipt_reuses_exact_bytes_without_hash_or_commands(self):
        result = self.build(); before = self.apk.read_bytes()
        self.assertEqual(self.signed.call_count, 1); self.assertEqual(self.verified.call_count, 1)
        with patch.object(apk_update, 'repack', side_effect=AssertionError('No recopy of signed Player')), \
             patch.object(storage, 'digest', side_effect=AssertionError('No repeat APK/key hashing')), \
             patch.object(driver, '_signers', side_effect=AssertionError('No native command rerun')):
            self.assertEqual(self.build(), result)
        self.assertEqual(self.roster.call_count, 1); self.assertEqual(self.apk.read_bytes(), before)
        receipt = next((self.output / 'receipts/player-update-capability').glob('*.json'))
        value = json.loads(receipt.read_text())
        self.assertEqual(len(value['outputs']), 4)
        self.assertNotIn('fixture secret', receipt.read_text())

    def test_atomic_replacement_without_receipt_adopts_only_matching_local_certificate(self):
        result = self.build()
        for receipt in (self.output / 'receipts/player-update-capability').glob('*.json'): receipt.unlink()
        self.callbacks.reset_mock()
        with patch.object(apk_update, 'make_capsule', side_effect=AssertionError('No registry reread')), \
             patch.object(apk_update, 'repack', side_effect=AssertionError('No recopy of completed Player')):
            self.assertEqual(self.build(), result)
        self.assertEqual(self.callbacks.call_count, 1)
        self.assertEqual(self.callbacks.call_args.kwargs, {'base_apk': self.apk})

    def test_changed_signing_key_is_not_trusted_as_closed_work(self):
        self.build(); self.callbacks.reset_mock()
        (self.output / 'signing/quest.keystore').write_bytes(b'changed signing key')
        with patch.object(driver, '_signers', side_effect=storage.BuildError('selected key does not belong')):
            with self.assertRaisesRegex(storage.BuildError, 'does not belong'): self.build()
        self.assertTrue(self.apk.is_file())

    def test_broken_capability_json_is_repaired_without_changing_original_payloads(self):
        self.build()
        with zipfile.ZipFile(self.apk) as original:
            values = {name: original.read(name) for name in original.namelist()}
        values[apk_update.CAPSULE] = b'{broken capsule'
        with zipfile.ZipFile(self.apk, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for name, body in values.items(): archive.writestr(name, body)
        with self.assertRaises(storage.BuildError): apk_update.inspect_base(self.apk)
        self.assertEqual(apk_update.inspect_base(self.apk, validate_update_contracts=False)['inputKey'], self.manifest['inputKey'])
        with patch.object(apk_update, 'script_roster', return_value={
                'unityVersion': self.capsule['unityVersion'], 'scriptTypes': self.capsule['scriptTypes']}):
            repaired = self.original_make_capsule(self.apk, package_abi=self.capsule['packageAbi'],
                dynamic_profile=True, repair_update_contracts=True)
        self.assertEqual(repaired, self.capsule)
        result = self.build()
        self.assertEqual(result['packageAbi'], self.capsule['packageAbi'])
        self.assertEqual(self.signed.call_count, 2)
        self.assertTrue(self.roster.call_args.kwargs['repair_update_contracts'])
        with zipfile.ZipFile(self.apk) as final:
            self.assertEqual(json.loads(final.read(apk_update.PROFILE)), self.manifest['profile'])
            self.assertEqual(final.read('assets/fixture.resource'), values['assets/fixture.resource'])
            self.assertEqual(final.read(apk_update.LIBRARY), values[apk_update.LIBRARY])


if __name__ == '__main__': unittest.main()
