"""A finished Android Player survives a missing Quest and explicit install skip."""
import copy
import hashlib
import http.client
import json
import os
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import failures
import server
import state
import wizard
from processes import Supervisor

MISSING = ("No authorized USB Quest is available. Unlock the Quest, enable developer mode, "
           "connect USB and accept its USB debugging prompt.")


class InstallationFailureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.log = self.root / "install.log"
        self.started = time.time() - 1

    def failure(self, text):
        self.log.write_text(text)
        return failures.tool_failure(self.root, "install", self.log, self.started, "python.exe", 1)

    def test_actual_installer_prefix_is_actionable_and_keeps_the_finished_build(self):
        error = self.failure("Selected local APK: completed.apk\nQuest installation stopped: " + MISSING + "\nADB log: retained.log\n")
        self.assertEqual(error.code, "quest_connection_required")
        self.assertEqual(error.parameters["cause"], MISSING)
        self.assertEqual(error.parameters["failureStage"], "install")
        for field in ("completedWorkRetained", "completedBuildRetained", "canSkipInstall"):
            self.assertTrue(error.parameters[field])
        self.assertEqual(error.parameters["retryAction"], "install")
        self.assertIn("APK ist fertig", error.message["de"])
        self.assertIn("USB-Debugging", error.message["de"])

    def test_current_connection_prompt_is_actionable_without_failure_prefix(self):
        error = self.failure('GHVRQ_ACTION {"schema":1,"kind":"quest-connection","reason":"missing-or-unauthorized"}\n'
                             "Quest connection required: " + MISSING + "\nADB log: retained.log\n")
        self.assertEqual(error.code, "quest_connection_required")
        self.assertTrue(error.parameters["completedBuildRetained"])
        self.assertEqual(error.parameters["retryAction"], "install")

    def test_only_known_connection_refusals_are_prerequisites(self):
        for cause, reason in ((MISSING, "missing-or-unauthorized"),
            ("The requested USB serial is missing or unauthorized. Unlock the Quest.", "missing-or-unauthorized"),
            ("The Wi-Fi Quest is offline or unauthorized. Unlock the Quest.", "offline-or-unauthorized"),
            ("Multiple USB Quests are connected. Select the intended headset with --serial USB_SERIAL.", "ambiguous"),
            ("Multiple Wi-Fi Quests are connected. Disconnect other headsets or choose the intended address with --host HOST[:PORT].", "ambiguous"),
            ("Could not determine one Quest Wi-Fi IPv4 address. Connect the Quest and PC to the same Wi-Fi.", "wifi-unavailable"),
            ("USB enabled Wi-Fi ADB, but connection failed. Keep both devices on the same Wi-Fi.", "wifi-unavailable")):
            with self.subTest(cause=cause):
                error = self.failure("Quest installation stopped: " + cause)
                self.assertEqual(error.code, "quest_connection_required")
                self.assertEqual(error.parameters["connectionReason"], reason)

    def test_corruption_signature_mismatch_and_unknown_refusals_keep_the_exact_cause(self):
        for cause in ("Signing-key mismatch. Use the original key.",
                      "The APK ZIP payload is incomplete or corrupt.",
                      "The Wi-Fi address belongs to a different headset. Use --setup.",
                      "Unexpected offline index while validating the original bank."):
            with self.subTest(cause=cause):
                error = self.failure("Quest installation stopped: " + cause)
                self.assertEqual(error.code, "installation_tool_failed")
                self.assertEqual(error.parameters["cause"], cause)
                self.assertNotIn("connectionReason", error.parameters)
                self.assertTrue(error.parameters["completedBuildRetained"])

    def test_signing_recovery_safety_refusal_names_actual_reason_without_usb_or_rebuild_advice(self):
        cause = ("Signing migration could not safely continue: private save data is inaccessible. "
                 "Retained backup: C:\\Users\\Owner\\.ghvrq\\device\\signing-migrations\\device-1")
        error = self.failure("Quest installation stopped: " + cause)
        self.assertEqual(error.code, "quest_signing_recovery_required")
        self.assertEqual(error.parameters["cause"], cause)
        self.assertTrue(error.parameters["signingRecovery"])
        self.assertTrue(error.parameters["completedBuildRetained"])
        self.assertEqual(error.parameters["retryAction"], "install")
        self.assertNotIn("connectionReason", error.parameters)
        self.assertIn("nicht neu gebaut", error.message["de"])
        self.assertNotIn("USB-Debugging", error.message["de"])
        for text in ("Earlier: Quest installation stopped: " + cause,
                     "Quest installation stopped: Signing migration could not safely continue.unrecognized"):
            self.assertNotEqual(self.failure(text).code, "quest_signing_recovery_required")

    def test_multiline_transfer_refusal_retains_each_actual_error_and_its_current_backup_folder(self):
        for folder in ("C:\\Users\\Owner\\.ghvrq\\device\\signing-recovery\\Quest\\transaction",
                       "/home/player/.ghvrq/device/signing-recovery/Quest/transaction"):
            with self.subTest(folder=folder):
                text = ("Quest installation stopped: Signing migration could not safely continue: ADB data transfer failed: "
                    "tar: can't open './files/tombstone_00': Permission denied\r\n\r\n"
                    "tar: can't open './files/tombstone_01': Permission denied\r\n\r\n"
                    "tar: had errors\r\n\r\n. Original recovery data is retained at " + folder +
                    ". Retry with the same APK and headset.\r\nADB log: ignored.log\r\n")
                error = self.failure(text)
                self.assertEqual(error.code, "quest_signing_recovery_required")
                self.assertIn("tombstone_00", error.parameters["cause"])
                self.assertIn("tombstone_01", error.parameters["cause"])
                self.assertNotIn("ignored.log", error.parameters["cause"])
                self.assertEqual(error.parameters["backupPath"], folder)
                self.assertIn("Diagnosepaket", error.message["de"])
                self.assertNotIn("Zugriffs-", error.message["de"])

    def test_current_app_storage_wait_is_a_retained_action_with_specific_guidance(self):
        action = {"schema": 1, "kind": "signing-repair-wait", "reason": "restore-app-access",
                  "saveDataProtected": True, "attempts": 2, "totalAttempts": 2, "root": "external"}
        text = ("GHVRQ_ACTION " + json.dumps(action) + "\nQuest installation stopped: "
                "Signing migration could not safely continue: The replacement app cannot yet restore its "
                "external saves/settings through its own storage view. Original recovery data is retained "
                "at /owned/current. Retry with the same APK and headset.\n")
        error = self.failure(text)
        self.assertEqual(error.code, "quest_signing_repair_wait")
        self.assertEqual(error.parameters["repairReason"], "restore-app-access")
        self.assertEqual(error.parameters["backupPath"], "/owned/current")
        self.assertEqual(error.parameters["retryAction"], "install")
        self.assertTrue(error.parameters["completedBuildRetained"])
        self.assertTrue(error.parameters["saveDataProtected"])
        self.assertIn("Quest aufsetzen", error.message["de"])
        self.assertIn("Wear and unlock", error.message["en"])
        self.assertNotIn("connectionReason", error.parameters)
        for field, value in (("root", "unrelated"), ("saveDataProtected", False),
                             ("attempts", 1), ("reason", "arbitrary-repair")):
            invalid = dict(action, **{field: value})
            with self.subTest(field=field):
                unknown = self.failure(text.replace(json.dumps(action), json.dumps(invalid)))
                self.assertNotEqual(unknown.code, "quest_signing_repair_wait")

    def test_running_app_storage_refusal_does_not_ask_for_another_connection_or_wakeup(self):
        action = {"schema": 1, "kind": "signing-repair-wait", "reason": "restore-app-storage-unavailable",
                  "saveDataProtected": True, "attempts": 3, "totalAttempts": 3, "root": "external"}
        error = self.failure("GHVRQ_ACTION " + json.dumps(action) + "\nQuest installation stopped: "
                             "Signing migration could not safely continue: App-origin storage is unavailable. "
                             "Original recovery data is retained at /owned/current. Retry with the same APK and headset.\n")
        self.assertEqual(error.code, "quest_signing_repair_wait")
        self.assertEqual(error.parameters["repairReason"], action["reason"])
        self.assertEqual(error.parameters["backupPath"], "/owned/current")
        self.assertEqual(error.parameters["retryAction"], "install")
        self.assertTrue(error.parameters["completedBuildRetained"])
        self.assertNotIn("connectionReason", error.parameters)
        self.assertIn("Die Quest ist erreichbar", error.message["de"])
        self.assertNotIn("aufsetzen", error.message["de"])
        self.assertNotIn("unlock", error.message["en"])
        self.assertIn("diagnostics", error.message["en"])

    def test_awake_no_process_refusal_keeps_finished_build_and_asks_for_startup_diagnostics(self):
        action = {"schema": 1, "kind": "signing-repair-wait", "reason": "restore-app-launch-unavailable",
                  "saveDataProtected": True, "attempts": 2, "totalAttempts": 2, "root": "external"}
        error = self.failure("GHVRQ_ACTION " + json.dumps(action) + "\nQuest installation stopped: "
                             "Signing migration could not safely continue: No process started on the awake Quest. "
                             "Original recovery data is retained at /owned/current. Retry with the same APK and headset.\n")
        self.assertEqual(error.code, "quest_signing_repair_wait")
        self.assertEqual(error.parameters["repairReason"], action["reason"])
        self.assertEqual(error.parameters["backupPath"], "/owned/current")
        self.assertTrue(error.parameters["completedBuildRetained"])
        self.assertEqual(error.parameters["retryAction"], "install")
        self.assertNotIn("connectionReason", error.parameters)
        self.assertIn("erreichbar und wach", error.message["de"])
        self.assertNotIn("aufsetzen", error.message["de"])
        self.assertNotIn("unlock", error.message["en"])

    def test_backup_reference_is_scoped_to_the_last_real_refusal_and_remains_bounded(self):
        text = ("Quest installation stopped: Signing migration could not safely continue: stale failure\n"
                ". Original recovery data is retained at /private/old. Retry with the same APK and headset.\n"
                "Quest installation stopped: Signing migration could not safely continue: current failure\nADB log: ignored.log")
        error = self.failure(text)
        self.assertNotIn("backupPath", error.parameters)
        self.assertNotIn("stale failure", error.parameters["cause"])
        error = self.failure("Quest installation stopped: Signing migration could not safely continue: failed\n" +
                             "x" * 5000 + "\n. Original recovery data is retained at /owned/current. Retry with the same APK and headset.")
        self.assertLessEqual(len(error.parameters["cause"]), 2048)
        self.assertEqual(error.parameters["backupPath"], "/owned/current")

    def test_restore_choices_require_a_current_complete_device_transaction_declaration(self):
        action = {"schema": 1, "kind": "signing-repair-wait", "reason": "restore-app-launch-unavailable",
                  "saveDataProtected": True, "attempts": 2, "totalAttempts": 2, "root": "external",
                  "requiresRestoreChoice": True, "manualLaunchAvailable": True, "resetAppDataAvailable": True,
                  "transaction": "a" * 32, "deviceSerial": "Quest-serial_01"}
        def declared(value):
            return self.failure("GHVRQ_ACTION " + json.dumps(value) + "\nQuest installation stopped: "
                "Signing migration could not safely continue: No process started. Original recovery data is retained "
                "at /owned/current. Retry with the same APK and headset.\n")
        error = declared(action)
        self.assertEqual(error.code, "quest_signing_repair_wait")
        self.assertTrue(error.parameters["requiresRestoreChoice"])
        self.assertTrue(error.parameters["manualLaunchAvailable"])
        self.assertTrue(error.parameters["resetAppDataAvailable"])
        self.assertEqual(error.parameters["restoreChoiceTransaction"], action["transaction"])
        self.assertEqual(error.parameters["restoreChoiceDeviceSerial"], action["deviceSerial"])
        self.assertIn("gesonderter ausdrücklicher Bestätigung", error.message["de"])
        self.assertIn("not be restored automatically", error.message["en"])
        for reason in ("restore-app-access", "restore-app-storage-unavailable", "restore-app-launch-unavailable"):
            self.assertTrue(declared(dict(action, reason=reason)).parameters["requiresRestoreChoice"])
        for field, bad in (("requiresRestoreChoice", False), ("manualLaunchAvailable", 1), ("resetAppDataAvailable", 1),
                           ("deviceSerial", "../foreign"), ("transaction", "b" * 31),
                           ("reason", "backup-census-mismatch"), ("saveDataProtected", False)):
            with self.subTest(field=field):
                rejected = declared(dict(action, **{field: bad}))
                self.assertNotIn("requiresRestoreChoice", rejected.parameters)
        missing = dict(action); del missing["deviceSerial"]
        self.assertNotIn("requiresRestoreChoice", declared(missing).parameters)
        self.assertNotIn("requiresRestoreChoice", declared(dict(action, manualLaunchAvailable=False, resetAppDataAvailable=False)).parameters)
        self.assertNotIn("requiresRestoreChoice", declared(dict(action, arbitrary="untrusted")).parameters)
        stale = "GHVRQ_ACTION " + json.dumps(action) + "\nQuest installation stopped: old\n" \
            "Quest installation stopped: Signing migration could not safely continue: current refusal\n"
        self.assertNotIn("requiresRestoreChoice", self.failure(stale).parameters)

    def test_other_stage_unanchored_and_stale_lines_cannot_claim_connection_failure(self):
        for text in ("Earlier: Quest installation stopped: " + MISSING,
                     "Quest installation stopped: No authorized USB Quest is available.unrecognized"):
            with self.subTest(text=text):
                self.assertNotEqual(self.failure(text).code, "quest_connection_required")
        self.log.write_text("Quest installation stopped: " + MISSING)
        self.assertEqual(failures.tool_failure(self.root, "build", self.log, self.started, "python.exe", 1).code, "build_tool_failed")
        os.utime(self.log, (1, 1))
        self.assertEqual(failures.tool_failure(self.root, "install", self.log, self.started, "python.exe", 1).code, "child_failed")

    def test_only_bounded_tail_is_read_and_the_last_actual_refusal_wins(self):
        cause = "The APK ZIP payload is incomplete or corrupt."
        error = self.failure("Quest installation stopped: " + MISSING + "\n" + "x" * 20000 + "\nQuest installation stopped: " + cause)
        self.assertEqual(error.code, "installation_tool_failed")
        self.assertEqual(error.parameters["cause"], cause)
        self.assertLessEqual(len(error.parameters["cause"]), 2048)

    @unittest.skipIf(os.name == "nt", "Link privileges vary on Windows")
    def test_links_and_hardlinks_are_never_used_as_current_attempt_evidence(self):
        private = self.root / "private.log"; private.write_text("Quest installation stopped: " + MISSING)
        self.log.symlink_to(private)
        with self.assertRaises(state.WizardError) as error:
            failures.tool_failure(self.root, "install", self.log, self.started, "python.exe", 1)
        self.assertEqual(error.exception.code, "linked_path")
        self.log.unlink(); os.link(private, self.log)
        error = failures.tool_failure(self.root, "install", self.log, self.started, "python.exe", 1)
        self.assertEqual(error.code, "child_failed")


class InstallationRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / "owned")
        self.repo = self.root / "Current Builder"; self.repo.mkdir()
        self.game = self.root / "Game"; self.game.mkdir()
        self.saved = self.store.create(wizard.choices({"gameRoot": str(self.game), "ownedDlc": ["jotl", "solo"]}))
        self.session = self.saved["session"]
        self.engine = wizard.Engine(self.store, self.repo)
        self.input_key = "a" * 64
        self.apk = self.store.root / "build/builds/player/complete.apk"
        self.apk.parent.mkdir(parents=True); self.apk.write_bytes(b"completed signed player")
        profile = self.store.session_dir(self.session) / "profile.json"
        state.atomic_json(profile, {"schema": 1, "provider": "steam", "displayName": "Owned player", "steamId": "76561198000000001"})
        details = {"tools": {"dotnet8": "removed-sdk"}, "source": {"sourceRoot": str(self.root / "Old source")},
            "unity": {"unityEditor": "Removed Unity.exe"}, "profile": {"profilePath": str(profile)},
            "inspect": {"inputKey": self.input_key, "gameKey": "b" * 64, "modBuild": 665},
            "build": {"inputKey": self.input_key, "apkSha256": hashlib.sha256(self.apk.read_bytes()).hexdigest(),
                      "modBuild": 665, "apk": str(self.apk), "outputRoot": str(self.store.root / "build")}}
        for row in self.saved["stages"][:-1]:
            key = self.engine.key(self.saved, row["id"])
            row.update(status="complete", attempts=1, details=details[row["id"]], progressKey=key)
            self.saved.setdefault("completed", {})[row["id"]] = {"key": key, "details": copy.deepcopy(details[row["id"]])}
            output = self.store.session_dir(self.session) / (row["id"] + ".output")
            output.write_text(row["id"])
            self.store.publish(self.session, row["id"], key, [output], details[row["id"]])
        self.saved["stages"][-1].update(status="failed", attempts=1)
        self.saved.update(status="failed", needsActions=[{"code": "child_failed", "stage": "install", "message": {"en": "Old generic error", "de": "Alter generischer Fehler"}}])
        self.store.save(self.saved)
        self.original_prior = copy.deepcopy(self.saved["stages"][:-1])
        self.prior_receipts = {stage: self.store.receipt(self.session, stage).read_bytes() for stage in state.STAGES[:-1]}

    def forbid_earlier_work(self):
        patches = [mock.patch("wizard.discovery.builder", side_effect=AssertionError("No Player selector or old source import")),
                   mock.patch.object(self.engine, "build_work_key", side_effect=AssertionError("No earlier profile read"))]
        for name in ("qualify", "check_downloads", "stage_tools", "stage_source", "stage_unity", "stage_profile", "stage_inspect", "stage_build", "stage_finish_build"):
            patches.append(mock.patch.object(self.engine, name, side_effect=AssertionError("No earlier work: " + name)))
        original = self.store.valid
        def valid(session, stage, key, **kwargs):
            self.assertEqual(stage, "install", "No earlier receipt qualification")
            return original(session, stage, key, **kwargs)
        patches.append(mock.patch.object(self.store, "valid", side_effect=valid))
        for patch in patches: patch.start(); self.addCleanup(patch.stop)

    def installer(self, *, failure=None):
        commands = []
        def run(argv, log, **kwargs):
            commands.append(argv)
            self.assertEqual(argv[4], str(self.repo / "scripts/install-quest-wireless.py"))
            if failure: raise failure
            result = self.store.root / "device/wireless-last-install.json"
            state.atomic_json(result, {"schema": 1, "inputKey": self.input_key,
                "apkSha256": self.saved["completed"]["build"]["details"]["apkSha256"], "launched": True})
        self.engine.supervisor_factory = lambda *_: SimpleNamespace(set_stage=lambda *_: None, run=run)
        return commands

    def assert_prior_retained(self, current):
        self.assertEqual(current["completed"]["build"], self.saved["completed"]["build"])
        for stage, content in self.prior_receipts.items():
            self.assertEqual(self.store.receipt(self.session, stage).read_bytes(), content)
        self.assertTrue(all(row["status"] == "complete" and row["attempts"] == 1 for row in current["stages"][:-1]))
        self.assertEqual(self.apk.read_bytes(), b"completed signed player")

    def test_first_pass_finished_build_retries_only_install_after_archive_replacement(self):
        self.forbid_earlier_work(); commands = self.installer()
        current = self.engine.run(self.session)
        self.assertEqual(current["status"], "complete")
        self.assertEqual(len(commands), 1)
        self.assert_prior_retained(current)
        event = next(row for row in current["events"] if row["code"] == "installation_resume_started")
        self.assertEqual(event["parameters"]["modBuild"], 665)
        self.assertFalse(event["parameters"]["earlierStagesChecked"])
        guidance = next(row for row in current["events"] if row["code"] == "installation_guidance")
        self.assertIn("USB-Debugging", guidance["parameters"]["message"]["de"])
        self.assertTrue(guidance["parameters"]["automaticReconnect"])

    def test_resumed_player_also_retries_only_install_without_selector_or_bank_reads(self):
        self.saved["completed"]["build"]["details"]["resumedCompletedPlayer"] = True
        self.store.save(self.saved)
        self.forbid_earlier_work(); commands = self.installer()
        self.assertEqual(self.engine.run(self.session)["status"], "complete")
        self.assertEqual(len(commands), 1)

    def test_real_supervised_child_failure_becomes_blocked_with_completed_build_green(self):
        self.forbid_earlier_work()
        def supervisor(store, session):
            real = Supervisor(store, session, poll=.01)
            def run(argv, log, **kwargs):
                real.set_stage("install")
                real.run([sys.executable, "-c", "import sys;print('Quest installation stopped: '+sys.argv[1],file=sys.stderr);sys.exit(1)", MISSING], log)
            return SimpleNamespace(set_stage=lambda *_: None, run=run)
        self.engine.supervisor_factory = supervisor
        current = self.engine.run(self.session)
        self.assertEqual(current["status"], "blocked")
        self.assertEqual(current["needsActions"][0]["code"], "quest_connection_required")
        self.assert_prior_retained(current)
        self.assertFalse(self.store.receipt(self.session, "install").exists())
        self.assertIn(MISSING, (self.store.session_dir(self.session) / "logs/progress.log").read_text())

    def test_real_install_refusal_remains_failed_and_never_changes_the_finished_build(self):
        self.forbid_earlier_work()
        error = state.WizardError("installation_tool_failed", "Corrupt APK", cause="Original bank differs")
        self.installer(failure=error)
        current = self.engine.run(self.session)
        self.assertEqual(current["status"], "failed")
        self.assertEqual(current["needsActions"][0]["parameters"]["cause"], "Original bank differs")
        self.assert_prior_retained(current)

    def test_interrupted_signing_recovery_retries_only_install_and_retains_backup(self):
        self.forbid_earlier_work()
        backup = self.store.root / "device/signing-migrations/headset/data.tar"
        backup.parent.mkdir(parents=True); backup.write_bytes(b"retained save transaction")
        cause = "Signing migration could not safely continue: restore access failed. Retained backup: " + str(backup.parent)
        self.installer(failure=failures.installation_failure("Quest installation stopped: " + cause, {"failureStage": "install"}))
        blocked = self.engine.run(self.session)
        self.assertEqual(blocked["status"], "blocked")
        self.assertEqual(blocked["needsActions"][0]["code"], "quest_signing_recovery_required")
        self.assert_prior_retained(blocked)
        self.assertFalse(self.store.receipt(self.session, "install").exists())
        commands = self.installer()
        resumed = self.engine.run(self.session)
        self.assertEqual(resumed["status"], "complete")
        self.assertEqual(len(commands), 1)
        self.assertEqual(backup.read_bytes(), b"retained save transaction")
        self.assert_prior_retained(resumed)

    def test_explicit_skip_publishes_small_real_receipt_without_amending_or_reading_prior_outputs(self):
        self.forbid_earlier_work()
        with mock.patch.object(self.store, "amend", side_effect=AssertionError("No reset")), \
             mock.patch.object(self.store, "valid", side_effect=AssertionError("No validation")):
            current = self.engine.skip_installation(self.session)
        self.assertEqual(current["status"], "complete")
        self.assertEqual(current["stages"][:-1], self.original_prior)
        self.assertFalse(current["choices"]["install"])
        self.assertEqual(current["choicesKey"], state.value_hash(current["choices"]))
        self.assertEqual(current["progress"]["percent"], 100)
        self.assertFalse(current["needsActions"])
        self.assert_prior_retained(current)
        receipt = state.read_json(self.store.receipt(self.session, "install"))
        self.assertEqual(receipt["details"], {"requested": False})
        self.assertEqual(len(receipt["outputs"]), 1)
        result = state.read_json(self.store.session_dir(self.session) / "installation.json")
        self.assertEqual(result, {"schema": 1, "requested": False})
        self.assertNotIn("hardwareVerified", result)

    def test_skip_accepts_current_connection_block_or_legacy_generic_install_failure(self):
        for code in ("child_failed", "quest_connection_required", "installation_tool_failed"):
            with self.subTest(code=code):
                self.saved.update(status="blocked" if code == "quest_connection_required" else "failed",
                                  needsActions=[{"code": code, "stage": "install"}])
                self.saved["stages"][-1]["status"] = self.saved["status"]
                self.store.save(self.saved)
                self.assertEqual(self.engine.skip_installation(self.session)["status"], "complete")

    def test_skip_rejects_earlier_failures_active_and_complete_runs_without_mutating_state(self):
        for change in ("active", "earlier", "complete", "changed-choices"):
            with self.subTest(change=change):
                saved = copy.deepcopy(self.saved)
                if change == "active": saved["status"] = saved["stages"][-1]["status"] = "running"
                elif change == "earlier": saved["stages"][4]["status"] = "failed"
                elif change == "complete": saved["status"] = saved["stages"][-1]["status"] = "complete"
                else: saved["choices"]["gameRoot"] += " changed"
                self.store.save(saved)
                # Running live owner is modelled with the kernel lock below;
                # an unowned stale running state is interrupted on load.
                with self.assertRaises(state.WizardError) as error:
                    self.engine.skip_installation(self.session)
                self.assertEqual(error.exception.code, "installation_skip_unavailable")
                self.assertFalse(self.store.receipt(self.session, "install").exists())

    def test_same_kernel_lock_prevents_skip_while_another_runner_owns_workspace(self):
        with state.file_lock(self.store.root / "run.lock"):
            with self.assertRaises(state.WizardError) as error: self.engine.skip_installation(self.session)
        self.assertEqual(error.exception.code, "already_running")
        self.assertEqual(self.store.load(self.session)["status"], "failed")

    def test_changed_inputs_or_incomplete_prerequisite_cannot_take_install_only_lane(self):
        for change in ("game", "provider", "profile", "dlc", "missing-owner", "wrong-input"):
            with self.subTest(change=change):
                saved = copy.deepcopy(self.saved)
                if change == "game": saved["choices"]["gameRoot"] += " changed"
                elif change == "provider": saved["choices"]["provider"] = "gog"
                elif change == "profile": saved["choices"]["steamId"] = "76561198000000002"
                elif change == "dlc": saved["choices"]["ownedDlc"] = ["solo"]
                elif change == "missing-owner": saved["completed"].pop("unity")
                else: saved["completed"]["build"]["details"]["inputKey"] = "e" * 64
                self.assertFalse(self.engine.installation_resume_candidate(saved))


class InstallationHttpTests(unittest.TestCase):
    forbid_earlier_work = InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = InstallationRecoveryTests.assert_prior_retained

    def setUp(self):
        InstallationRecoveryTests.setUp(self)
        ui = self.root / "UI"; ui.mkdir(); (ui / "index.html").write_text("<html>local</html>")
        self.http = server.LocalServer(self.store, ui, engine_factory=lambda *_: self.engine)
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True); self.thread.start()
        self.addCleanup(self.close)

    def close(self):
        self.http.shutdown(); self.http.close_owned(); self.thread.join()

    def request(self, body=None, *, method="POST", authenticated=True):
        connection = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Content-Type": "application/json", "Origin": self.http.origin,
                   "X-Quest-Token": self.http.token if authenticated else "wrong"}
        connection.request(method, "/api/install/skip", None if body is None else json.dumps(body), headers)
        response = connection.getresponse(); value = json.loads(response.read()); code = response.status; connection.close()
        return code, value

    def test_skip_route_requires_exact_body_and_loopback_authorization(self):
        for body in ({"session": self.session, "choices": {"install": False}}, {}, {"session": "invalid"}):
            with self.subTest(body=body): self.assertEqual(self.request(body)[0], 400)
        self.assertEqual(self.request({"session": self.session}, authenticated=False)[0], 403)
        self.assertEqual(self.request(method="GET")[0], 400)
        self.assertFalse(self.store.receipt(self.session, "install").exists())

    def test_skip_route_returns_current_visible_complete_state_and_preserves_build(self):
        self.forbid_earlier_work()
        with mock.patch.object(self.http, "visible_state", side_effect=lambda _, value: value):
            code, result = self.request({"session": self.session})
        self.assertEqual(code, 200)
        self.assertEqual(result["event"], "status")
        self.assertEqual(result["session"], self.session)
        self.assertEqual(result["state"]["status"], "complete")
        self.assert_prior_retained(result["state"])

    def test_local_live_job_and_storage_job_prevent_skip_before_engine_or_state_change(self):
        for slot in ("jobs", "storage_job"):
            with self.subTest(slot=slot):
                job = SimpleNamespace(is_alive=lambda: True)
                if slot == "jobs": self.http.jobs["another"] = job
                else: self.http.storage_job = job
                with mock.patch.object(self.engine, "skip_installation", side_effect=AssertionError("Must not call engine")):
                    code, result = self.request({"session": self.session})
                self.assertEqual(code, 400)
                self.assertEqual(result["code"], "already_running")
                self.http.jobs.clear(); self.http.storage_job = None
        self.assertEqual(self.store.load(self.session)["status"], "failed")


if __name__ == "__main__": unittest.main()
