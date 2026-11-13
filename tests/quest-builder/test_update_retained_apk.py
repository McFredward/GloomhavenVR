"""Keep an already owned signed Player across genuinely host-only updates.

These fixtures run the real tiny APK, owned-PC qualification and durable stage
witnesses. Native signing and binary Android serialization are injected; no
Unity build or headset success is claimed.
"""
import copy
import json
from pathlib import Path
from unittest.mock import Mock, patch
import unittest
import zipfile

import test_update_driver as fixture

driver = fixture.driver
apk_update = fixture.apk_update
storage = fixture.storage
ROOT = fixture.ROOT


class RetainedUpdateTests(unittest.TestCase):
    setUp = fixture.UpdateDriverTests.setUp
    write_apk = fixture.UpdateDriverTests.write_apk

    def own_current_apk(self):
        self.apk = self.output / "builds" / ("e" * 64) / "GloomhavenVR-Quest.apk"
        self.apk.parent.mkdir(parents=True)
        self.args.base_apk = str(self.apk)
        self.args.command = "update-mod"
        self.args.update_kind = "update-mod"
        self.builder.mod_build = lambda _: self.current_mod["modBuild"]
        self.builder.source_inventory = lambda _: (
            [*copy.deepcopy(self.current_mod["files"]),
             {"path": "tools/quest-installer/content_access.py", "size": 17, "sha256": "9" * 64}],
            "8" * 40, False)
        self.write_apk()
        bank = self.apk.with_name("GloomhavenVR-Quest-content.zip")
        with zipfile.ZipFile(bank, "w") as archive:
            archive.writestr("StreamingAssets/owned.bundle", b"original game data" * 8192)
        # A current player already contains the original managed compiler seed.
        # Omitting it models an older player that intentionally needs one PC-side
        # seed migration/repack, so it cannot prove the no-payload-change path.
        seed = driver._seed(self.builder)
        members = seed.build(self.builder, self.game, self.manifest, self.output)
        self.write_apk(extra={**{name: value.read_bytes() if isinstance(value, Path) else value
                                for name, value in members.items()},
            "assets/Quest/content-delivery.json": storage.canonical({
            "schema": 1, "package": fixture.portable.PACKAGE, "inputKey": self.manifest["inputKey"],
            "files": [{"file": fixture.portable.CONTENT, "size": bank.stat().st_size,
                       "sha256": storage.digest(bank), "archive": fixture.portable.ARCHIVE}]})})
        self.assertTrue(seed.inspect_seed(self.apk, apk_update.inspect_base(self.apk))["available"])
        details = {"inputKey": self.manifest["inputKey"], "modBuild": self.current_mod["modBuild"],
                   "apkSha256": storage.digest(self.apk), "isDiagnostic": False, "isDummy": False,
                   "package": fixture.portable.PACKAGE,
                   "contentFiles": [storage.record_file(bank, bank.relative_to(self.output).as_posix())]}
        report = Path(str(self.apk) + ".build.json")
        storage.write_json(report, {"schema": 1, "buildResult": "Succeeded", **details})
        storage.Stages(self.output).run("build", "e" * 64, lambda: ([self.apk, bank, report], details))
        storage.write_json(self.output / "latest-build.json", {"schema": 1,
            "apk": self.apk.relative_to(self.output).as_posix(), "receipt": "receipts/build/" + "e" * 64 + ".json",
            "details": details})
        return bank, report

    def admitted(self):
        return patch.multiple(driver, package_fingerprint=Mock(return_value=self.capsule["packageAbi"]),
                              signing_tools=Mock(return_value={}),
                              _signers=Mock(return_value=(lambda _: None, lambda _: None)))

    def test_host_repair_keeps_exact_current_apk_bank_and_original_receipts(self):
        bank, report = self.own_current_apk()
        originals = {path: path.read_bytes() for path in
                     (self.apk, bank, report, self.output / "latest-build.json",
                      self.output / ("receipts/build/" + "e" * 64 + ".json"))}
        with self.admitted():
            initial = driver.inspect(self.builder, self.args, ROOT, self.output)
            actual_digest = storage.digest
            def refuse_payload_read(path, *args, **kwargs):
                if Path(path) in (self.apk, bank):
                    raise AssertionError("Existing reliable byte witnesses must avoid another payload read")
                return actual_digest(path, *args, **kwargs)
            storage._invocation_file_proofs.clear()
            with patch.object(storage, "digest", side_effect=refuse_payload_read), \
                 patch.object(apk_update, "repack", side_effect=AssertionError("No changed payload needs repacking")), \
                 patch.object(driver, "_compile_code", side_effect=AssertionError("No changed IL2CPP inputs")), \
                 patch.object(driver, "_game_subset", side_effect=AssertionError("No new owned-game snapshot")):
                selected = driver.run(self.builder, self.args, ROOT, self.output)
        self.assertEqual(selected, self.apk)
        for path, original in originals.items(): self.assertEqual(path.read_bytes(), original, str(path))
        current = json.loads((self.output / "updates" / initial["updateKey"] / "unchanged-apk.json").read_text())
        self.assertTrue(current["retainedApk"]); self.assertTrue(current["noPayloadChanges"])
        self.assertEqual(current["signedModSource"], self.current_mod)
        self.assertEqual(current["hostModSource"]["files"][-1]["path"], "tools/quest-installer/content_access.py")
        self.assertNotEqual(current["hostModSource"]["files"], current["signedModSource"]["files"])
        self.assertEqual(current["retainedBuildReceipt"], "receipts/build/" + "e" * 64 + ".json")
        retained_bank = fixture.portable.content_source(selected, self.output)
        self.assertEqual(retained_bank["path"], bank); self.assertIsNone(retained_bank["member"])
        self.assertFalse((self.output / "updates" / initial["updateKey"] / "GloomhavenVR-Quest.apk").exists())
        self.assertFalse((self.output / "updates/game-inputs").exists())
        self.assertFalse((self.output / "updates/source-inputs").exists())

    def test_owned_pc_presence_remains_required_even_for_no_payload_change(self):
        self.own_current_apk()
        (self.game / "Managed/GH.Runtime.dll").unlink()
        with self.admitted(), patch.object(apk_update, "repack", side_effect=AssertionError("No APK admitted without ownership")):
            with self.assertRaisesRegex(storage.BuildError, "does not match this APK's original assembly"):
                driver.run(self.builder, self.args, ROOT, self.output)
        self.assertEqual(list((self.output / "updates").glob("*/unchanged-apk.json")), [])

    def test_profile_change_still_rewrites_the_dynamic_profile_and_signs(self):
        self.own_current_apk(); self.profile["displayName"] = "Different Owner"
        with self.admitted(), patch.object(apk_update, "patch_resource_json", return_value={}), \
             patch.object(apk_update, "update_android_versions", return_value=b"AXML separately tested"), \
             patch.object(apk_update, "repack", wraps=apk_update.repack) as repack:
            result = driver.run(self.builder, self.args, ROOT, self.output)
        self.assertNotEqual(result, self.apk); self.assertEqual(repack.call_count, 1)
        with zipfile.ZipFile(result) as archive:
            update = json.loads(archive.read(apk_update.UPDATE))
        self.assertEqual(update["profile"]["displayName"], "Different Owner")

    def test_changed_runtime_source_cannot_retain_the_old_native_pair(self):
        self.own_current_apk()
        self.builder.source_inventory = lambda _: ([*self.current_mod["files"],
            {"path": "src/GloomhavenVR/Core/Plugin.cs", "size": 17, "sha256": "9" * 64}], "8" * 40, False)
        with self.admitted():
            plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        self.assertTrue(plan["codeChanged"]); self.assertTrue(plan["requiresUnity"])
        self.assertIsNone(driver._retain_current_apk(self.builder, plan, apk_update.inspect_base(self.apk), self.output))

    def test_foreign_signing_identity_cannot_retain_the_existing_signed_player(self):
        self.own_current_apk()
        with self.admitted(): plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        for alteration in ({"mode": "new-owner", "certificateSha256": "1" * 64},
                           {"certificateSha256": "1" * 64}, {"certificateSha256": None}):
            changed = copy.deepcopy(plan); changed["signing"].update(alteration)
            with self.subTest(alteration=alteration):
                self.assertIsNone(driver._retain_current_apk(self.builder, changed, apk_update.inspect_base(self.apk), self.output))

    def test_noncurrent_or_changed_build_evidence_keeps_existing_owned_output_route(self):
        self.own_current_apk()
        with self.admitted(): plan = driver.inspect(self.builder, self.args, ROOT, self.output)
        latest = self.output / "latest-build.json"
        for value in ([], {"schema": 1, "apk": "builds/other.apk"},
                      {**json.loads(latest.read_text()), "details": {"apkSha256": "0" * 64}}):
            storage.write_json(latest, value)
            with self.subTest(value=value):
                self.assertIsNone(driver._retain_current_apk(self.builder, plan, apk_update.inspect_base(self.apk), self.output))


if __name__ == "__main__": unittest.main()
