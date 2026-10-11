"""Save-protected installation decisions retain the closed Player frontier."""
import copy
import http.client
import json
import os
from pathlib import Path
import sys
import threading
from types import SimpleNamespace
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_installation_recovery as fixture
from test_installation_recovery import failures, server, state, wizard


def refusal(reason="non-save-inaccessible", policy="all"):
    action = {"schema": 1, "kind": "signing-data-policy", "reason": reason,
              "policy": policy, "saveDataProtected": True}
    return ("GHVRQ_ACTION " + json.dumps(action) + "\nQuest installation stopped: "
            "Signing migration could not safely continue: external access denied. "
            "Original recovery data is retained at /owned/backup. Retry with the same APK and headset.\nADB log: current.log\n")


class DataPolicyTests(unittest.TestCase):
    setUp = fixture.InstallationRecoveryTests.setUp
    forbid_earlier_work = fixture.InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = fixture.InstallationRecoveryTests.assert_prior_retained
    installer = fixture.InstallationRecoveryTests.installer

    def pause(self, reason="non-save-inaccessible", policy="all"):
        if policy == "all":
            self.saved["installationRecoveryPolicy"] = dict(self.engine.installation_policy(self.saved), policy="all")
            self.store.save(self.saved)
        self.installer(failure=failures.installation_failure(refusal(reason, policy), {"failureStage": "install"}))
        return self.engine.run(self.session)

    def test_current_and_archived_saves_are_default_without_changing_any_build_key(self):
        self.forbid_earlier_work()
        before = {row["id"]: self.engine.key(self.saved, row["id"]) for row in self.saved["stages"]}
        commands = self.installer(); current = self.engine.run(self.session)
        self.assertEqual(commands[0][-2:], ["--recovery-data-policy", "saves"])
        self.assertEqual(current["installationRecoveryPolicy"]["policy"], "saves")
        self.assertEqual({row["id"]: self.engine.key(current, row["id"]) for row in current["stages"]}, before)
        self.assertEqual(current["choices"], self.saved["choices"])
        self.assert_prior_retained(current)

    def test_protected_save_refusal_pauses_without_reset_rebuild_or_uninstall(self):
        self.forbid_earlier_work(); current = self.pause("save-data-inaccessible", "saves")
        self.assertEqual(current["status"], "blocked")
        action = current["needsActions"][0]
        self.assertEqual(action["code"], "quest_signing_data_required")
        self.assertTrue(action["parameters"]["saveDataProtected"])
        self.assertIn("nicht", action["message"]["de"])
        self.assertEqual(current["installationAction"]["reason"], "save-data-inaccessible")
        nonce = current["installationAction"]["nonce"]
        with self.assertRaises(state.WizardError) as error:
            self.engine.approve_installation_policy(self.session, nonce, "saves")
        self.assertEqual(error.exception.code, "installation_action_stale")
        self.assert_prior_retained(current)

    def test_explicit_compromise_is_scoped_and_resumes_only_installation(self):
        self.forbid_earlier_work(); paused = self.pause()
        before_keys = {row["id"]: self.engine.key(paused, row["id"]) for row in paused["stages"]}
        nonce = paused["installationAction"]["nonce"]
        # Reopen from durable state, as after closing/replacing the Builder.
        self.store = state.Store(self.store.root); self.engine = wizard.Engine(self.store, self.repo)
        with mock.patch.object(self.store, "amend", side_effect=AssertionError("No build reset")):
            accepted = self.engine.approve_installation_policy(self.session, nonce, "saves")
        self.assertEqual(accepted["installationRecoveryPolicy"]["policy"], "saves")
        self.assertNotIn("installationAction", accepted)
        self.assertEqual({row["id"]: self.engine.key(accepted, row["id"]) for row in accepted["stages"]}, before_keys)
        self.assert_prior_retained(accepted)
        self.forbid_earlier_work(); commands = self.installer()
        self.assertEqual(self.engine.run(self.session)["status"], "complete")
        self.assertEqual(commands[0][-2:], ["--recovery-data-policy", "saves"])
        self.assert_prior_retained(self.store.load(self.session))
        with self.assertRaises(state.WizardError): self.engine.approve_installation_policy(self.session, nonce, "saves")

    def test_retry_all_preserves_the_stricter_choice_and_invalidates_old_nonce(self):
        self.forbid_earlier_work(); paused = self.pause()
        nonce = paused["installationAction"]["nonce"]
        commands = self.installer(); current = self.engine.run(self.session)
        self.assertEqual(commands[0][-2:], ["--recovery-data-policy", "all"])
        self.assertNotIn("installationAction", current)
        with self.assertRaises(state.WizardError): self.engine.approve_installation_policy(self.session, nonce, "saves")
        self.assert_prior_retained(current)

    def test_stale_foreign_or_changed_action_never_changes_policy(self):
        self.forbid_earlier_work(); paused = self.pause()
        for change in ("session", "input", "apk", "build", "attempt", "nonce", "reason", "choice", "active", "prior", "marker"):
            with self.subTest(change=change):
                current = copy.deepcopy(paused)
                if change == "session": current["installationAction"]["session"] = "b" * 32
                elif change == "input": current["installationAction"]["inputKey"] = "f" * 64
                elif change == "apk": current["installationAction"]["apkSha256"] = "f" * 64
                elif change == "build": current["installationAction"]["buildKey"] = "f" * 64
                elif change == "attempt": current["stages"][-1]["attempts"] += 1
                elif change == "nonce": current["installationAction"]["nonce"] = "f" * 32
                elif change == "reason": current["installationAction"]["reason"] = "save-data-inaccessible"
                elif change == "choice": current["choices"]["gameRoot"] += " changed"
                elif change == "active": current["status"] = current["stages"][-1]["status"] = "complete"
                elif change == "prior": current["stages"][3]["status"] = "failed"
                else: current["needsActions"][0]["parameters"]["saveDataProtected"] = False
                self.store.save(current)
                with self.assertRaises(state.WizardError):
                    self.engine.approve_installation_policy(self.session, paused["installationAction"]["nonce"], "saves")
                self.assertEqual(self.store.load(self.session)["installationRecoveryPolicy"]["policy"], "all")
        self.assert_prior_retained(paused)

    def test_live_kernel_owner_blocks_approval_before_any_mutation(self):
        self.forbid_earlier_work(); current = self.pause()
        with state.file_lock(self.store.root / "run.lock"):
            with self.assertRaises(state.WizardError) as error:
                self.engine.approve_installation_policy(self.session, current["installationAction"]["nonce"], "saves")
        self.assertEqual(error.exception.code, "already_running")
        self.assertEqual(self.store.load(self.session)["installationRecoveryPolicy"]["policy"], "all")

    def test_installation_decision_cannot_follow_a_different_completed_apk(self):
        for change in ("apkSha256", "inputKey", "session", "buildKey", "schema", "extra"):
            with self.subTest(change=change):
                saved = copy.deepcopy(self.saved)
                saved["installationRecoveryPolicy"] = dict(self.engine.installation_policy(self.saved), policy="all")
                if change == "extra": saved["installationRecoveryPolicy"]["resetSaves"] = True
                elif change == "schema": saved["installationRecoveryPolicy"]["schema"] = True
                else: saved["installationRecoveryPolicy"][change] = "foreign"
                with self.assertRaises(state.WizardError) as error: self.engine.installation_policy(saved)
                self.assertEqual(error.exception.code, "installation_policy_invalid")

    def test_child_policy_mismatch_cannot_declare_a_compromise(self):
        self.forbid_earlier_work()
        self.installer(failure=failures.installation_failure(refusal(), {"failureStage": "install"}))
        current = self.engine.run(self.session)
        self.assertEqual(current["needsActions"][0]["code"], "installation_action_invalid")
        self.assertNotIn("installationAction", current)
        self.assert_prior_retained(current)

    def test_new_build_selection_clears_only_the_old_installation_decision(self):
        self.forbid_earlier_work(); paused = self.pause()
        unchanged = self.store.amend(self.session, copy.deepcopy(paused["choices"]))
        self.assertEqual(unchanged["installationAction"], paused["installationAction"])
        self.assertEqual(unchanged["installationRecoveryPolicy"], paused["installationRecoveryPolicy"])
        selected = dict(paused["choices"], gameRoot=str(self.root / "Another owned game"))
        amended = self.store.amend(self.session, selected)
        self.assertNotIn("installationAction", amended)
        self.assertNotIn("installationRecoveryPolicy", amended)
        self.assertEqual(amended["status"], "ready")
        for stage, content in self.prior_receipts.items(): self.assertEqual(self.store.receipt(self.session, stage).read_bytes(), content)
        self.assertEqual(self.apk.read_bytes(), b"completed signed player")

    def test_explicit_finish_retains_current_and_archived_backups(self):
        self.forbid_earlier_work(); current = self.pause("save-data-inaccessible", "saves")
        backup = self.store.root / "device/retained-history.tar"
        backup.parent.mkdir(parents=True); backup.write_bytes(b"current and archived saves")
        current = self.engine.skip_installation(self.session)
        self.assertEqual(current["status"], "complete")
        self.assertNotIn("installationAction", current)
        self.assertEqual(backup.read_bytes(), b"current and archived saves")
        self.assert_prior_retained(current)

    def test_bounded_current_action_is_the_only_installation_authority(self):
        original = json.loads(refusal().splitlines()[0][len("GHVRQ_ACTION "):])
        for value in (None, [], dict(original, resetSaves=True), dict(original, schema=True),
                      dict(original, saveDataProtected=False), dict(original, reason="unknown"),
                      dict(original, policy="saves"), dict(original, policy="reset")):
            with self.subTest(value=value):
                text = "GHVRQ_ACTION " + json.dumps(value) + "\n" + refusal().split("\n", 1)[1]
                self.assertEqual(failures.installation_failure(text, {}).code, "quest_signing_recovery_required")
        for prefix in (refusal() + "Quest installation stopped: Signing migration could not safely continue: current new cause\n",
                       "GHVRQ_ACTION " + json.dumps(original) + "\nGHVRQ_ACTION invalid\n" + refusal().split("\n", 1)[1],
                       "GHVRQ_ACTION " + "x" * 3000 + "\n" + refusal().split("\n", 1)[1]):
            self.assertEqual(failures.installation_failure(prefix, {}).code, "quest_signing_recovery_required")
        accepted = failures.installation_failure(refusal("save-data-inaccessible", "saves"), {})
        self.assertEqual(accepted.code, "quest_signing_data_required")
        self.assertNotIn("GHVRQ_ACTION", accepted.parameters["cause"])
        self.assertTrue(accepted.parameters["saveDataProtected"])

    def test_actual_supervised_refusal_is_blocked_with_completed_stages_green(self):
        self.forbid_earlier_work()
        def supervisor(store, session):
            actual = fixture.Supervisor(store, session, poll=.01)
            def run(argv, log, **kwargs):
                self.assertEqual(argv[-2:], ["--recovery-data-policy", "saves"])
                actual.set_stage("install")
                actual.run([sys.executable, "-c", "import sys;print(sys.argv[1]);sys.exit(1)",
                            refusal("save-data-inaccessible", "saves")], log)
            return SimpleNamespace(set_stage=lambda *_: None, run=run)
        self.engine.supervisor_factory = supervisor
        current = self.engine.run(self.session)
        self.assertEqual(current["status"], "blocked")
        self.assertEqual(current["needsActions"][0]["code"], "quest_signing_data_required")
        self.assert_prior_retained(current)


class DataPolicyHttpTests(unittest.TestCase):
    setUp = fixture.InstallationHttpTests.setUp
    close = fixture.InstallationHttpTests.close
    forbid_earlier_work = fixture.InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = fixture.InstallationRecoveryTests.assert_prior_retained
    installer = fixture.InstallationRecoveryTests.installer
    pause = DataPolicyTests.pause

    def request(self, body, *, authenticated=True, method="POST"):
        connection = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Content-Type": "application/json", "Origin": self.http.origin,
                   "X-Quest-Token": self.http.token if authenticated else "wrong"}
        connection.request(method, "/api/install/data-policy", json.dumps(body), headers)
        response = connection.getresponse(); value = json.loads(response.read()); code = response.status; connection.close()
        return code, value

    def test_exact_authenticated_body_is_required_before_current_action_changes(self):
        self.forbid_earlier_work(); paused = self.pause()
        body = {"session": self.session, "nonce": paused["installationAction"]["nonce"], "policy": "saves"}
        for extra in ({}, dict(body, resetSaves=True), dict(body, nonce="invalid"), dict(body, policy="all")):
            self.assertEqual(self.request(extra)[0], 400)
        self.assertEqual(self.request(body, authenticated=False)[0], 403)
        self.assertEqual(self.request(body, method="GET")[0], 400)
        self.assertEqual(self.store.load(self.session)["installationRecoveryPolicy"]["policy"], "all")
        with mock.patch.object(self.http, "visible_state", side_effect=lambda _, value: value):
            code, result = self.request(body)
        self.assertEqual(code, 200)
        self.assertEqual(result["state"]["installationRecoveryPolicy"]["policy"], "saves")
        self.assert_prior_retained(result["state"])
        self.assertEqual(self.request(body)[0], 400, "Consumed compromise nonce cannot be replayed")

    def test_server_jobs_and_storage_owners_block_approval(self):
        self.forbid_earlier_work(); paused = self.pause()
        body = {"session": self.session, "nonce": paused["installationAction"]["nonce"], "policy": "saves"}
        for slot in ("jobs", "storage_job"):
            with self.subTest(slot=slot):
                live = SimpleNamespace(is_alive=lambda: True)
                if slot == "jobs": self.http.jobs["other"] = live
                else: self.http.storage_job = live
                self.assertEqual(self.request(body)[1]["code"], "already_running")
                self.http.jobs.clear(); self.http.storage_job = None
        self.assertEqual(self.store.load(self.session)["installationRecoveryPolicy"]["policy"], "all")


if __name__ == "__main__": unittest.main()
