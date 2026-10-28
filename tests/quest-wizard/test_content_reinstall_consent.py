"""Large content redeployment requires current transaction-specific consent."""
import copy
import http.client
import json
from pathlib import Path
import sys
import threading
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_installation_recovery as fixture
from test_installation_recovery import failures, server, state, wizard


def refusal(**changes):
    action = {"schema": 1, "kind": "signing-content-reinstall", "reason": "native-rename-unavailable",
              "saveDataProtected": True, "estimatedBytes": 11169001665,
              "transaction": "b" * 32, "deviceSerial": "QUEST_TEST_123"}
    action.update(changes)
    return ("GHVRQ_ACTION " + json.dumps(action) + "\nQuest installation stopped: "
            "Signing migration could not safely continue: Native directory rename is unavailable. "
            "Original recovery data is retained at /owned/backup. Retry with the same APK and headset.\nADB log: current.log\n")


class ContentReinstallConsentTests(unittest.TestCase):
    setUp = fixture.InstallationRecoveryTests.setUp
    forbid_earlier_work = fixture.InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = fixture.InstallationRecoveryTests.assert_prior_retained
    installer = fixture.InstallationRecoveryTests.installer

    def pause(self):
        self.installer(failure=failures.installation_failure(refusal(), {"failureStage": "install"}))
        return self.engine.run(self.session)

    def test_unsupported_move_pauses_and_does_not_authorize_redeployment_or_repeat_unity(self):
        self.forbid_earlier_work()
        paused = self.pause()
        self.assertEqual(paused["status"], "blocked")
        self.assertEqual(paused["needsActions"][0]["code"], "quest_signing_content_reinstall_required")
        self.assertEqual(paused["installationAction"]["kind"], "content-reinstall")
        self.assertNotIn("installationContentReinstallApproval", paused)
        self.assert_prior_retained(paused)

    def test_explicit_consent_survives_reopen_and_retries_only_the_installer_for_one_transaction(self):
        self.forbid_earlier_work()
        paused = self.pause()
        keys = {stage: self.engine.key(paused, stage) for stage in state.STAGES}
        nonce = paused["installationAction"]["nonce"]
        self.store = state.Store(self.store.root)
        self.engine = wizard.Engine(self.store, self.repo)
        with mock.patch.object(self.store, "amend", side_effect=AssertionError("No full build reset")):
            accepted = self.engine.approve_content_reinstallation(self.session, nonce)
        self.assertEqual(accepted["installationContentReinstallApproval"]["transaction"], "b" * 32)
        self.assertEqual(accepted["installationContentReinstallApproval"]["deviceSerial"], "QUEST_TEST_123")
        self.assertEqual({stage: self.engine.key(accepted, stage) for stage in state.STAGES}, keys)
        self.forbid_earlier_work()
        commands = self.installer()
        finished = self.engine.run(self.session)
        self.assertEqual(finished["status"], "complete")
        self.assertEqual(commands[0][-2:], ["--allow-content-reinstall", "b" * 32])
        self.assert_prior_retained(finished)
        with self.assertRaises(state.WizardError): self.engine.approve_content_reinstallation(self.session, nonce)

    def test_normal_retry_cannot_silently_consume_pending_consent(self):
        self.forbid_earlier_work()
        paused = self.pause()
        nonce = paused["installationAction"]["nonce"]
        commands = self.installer(failure=failures.installation_failure(refusal(), {"failureStage": "install"}))
        retried = self.engine.run(self.session)
        self.assertNotIn("--allow-content-reinstall", commands[0])
        self.assertNotEqual(retried["installationAction"]["nonce"], nonce)
        with self.assertRaises(state.WizardError): self.engine.approve_content_reinstallation(self.session, nonce)

    def test_stale_changed_or_foreign_frontier_never_grants_consent(self):
        self.forbid_earlier_work()
        paused = self.pause()
        nonce = paused["installationAction"]["nonce"]
        for change in ("transaction", "deviceSerial", "attempt", "nonce", "kind", "reason", "apk", "input", "build", "session", "choices", "needed", "active", "prior"):
            with self.subTest(change=change):
                altered = copy.deepcopy(paused)
                if change in ("transaction", "deviceSerial", "nonce", "kind", "reason"):
                    altered["installationAction"][change] = "f" * 32
                elif change == "attempt": altered["stages"][-1]["attempts"] += 1
                elif change in ("apk", "input", "build", "session"):
                    altered["installationAction"][{"apk": "apkSha256", "input": "inputKey", "build": "buildKey", "session": "session"}[change]] = "f" * 64
                elif change == "choices": altered["choices"]["gameRoot"] += " changed"
                elif change == "needed": altered["needsActions"][0]["parameters"]["requiresContentReinstall"] = False
                elif change == "active": altered["status"] = altered["stages"][-1]["status"] = "complete"
                else: altered["stages"][0]["status"] = "failed"
                self.store.save(altered)
                with self.assertRaises(state.WizardError): self.engine.approve_content_reinstallation(self.session, nonce)
                self.assertNotIn("installationContentReinstallApproval", self.store.load(self.session))

    def test_approval_is_removed_when_user_selects_a_different_build(self):
        self.forbid_earlier_work()
        paused = self.pause()
        self.engine.approve_content_reinstallation(self.session, paused["installationAction"]["nonce"])
        amended = self.store.amend(self.session, {**paused["choices"], "gameRoot": paused["choices"]["gameRoot"] + " changed"})
        self.assertNotIn("installationContentReinstallApproval", amended)

    def test_changed_approval_binding_is_rejected_before_invoking_installation(self):
        paused = self.pause()
        accepted = self.engine.approve_content_reinstallation(self.session, paused["installationAction"]["nonce"])
        accepted["installationContentReinstallApproval"]["apkSha256"] = "e" * 64
        self.store.save(accepted)
        commands = self.installer()
        blocked = self.engine.run(self.session)
        self.assertEqual(blocked["status"], "blocked")
        self.assertEqual(commands, [])
        self.assert_prior_retained(blocked)

    def test_malformed_untrusted_or_old_installer_actions_do_not_create_consent(self):
        for values in ({"estimatedBytes": True}, {"estimatedBytes": -1}, {"saveDataProtected": False},
                       {"transaction": "bad"}, {"deviceSerial": "../private"}, {"schema": True}, {"extra": "unknown"}):
            with self.subTest(values=values):
                failure = failures.installation_failure(refusal(**values), {"failureStage": "install"})
                self.assertNotEqual(failure.code, "quest_signing_content_reinstall_required")
        raw = refusal() + "Quest installation stopped: Connection failed.\n"
        self.assertNotEqual(failures.installation_failure(raw, {"failureStage": "install"}).code,
                            "quest_signing_content_reinstall_required")

    def test_http_requires_exact_authenticated_current_approval(self):
        paused = self.pause()
        httpd = server.LocalServer(self.store, Path(__file__).resolve().parents[2] / "tools/quest-wizard-ui",
                                  engine_factory=lambda _: self.engine)
        thread = threading.Thread(target=httpd.serve_forever, daemon=True)
        thread.start()
        try:
            def request(body, token=True):
                connection = http.client.HTTPConnection(*httpd.server_address, timeout=3)
                connection.request("POST", "/api/install/content-reinstall", json.dumps(body),
                    {"Origin": httpd.origin, "X-Quest-Token": httpd.token if token else "bad", "Content-Type": "application/json"})
                response = connection.getresponse()
                result = response.status, json.loads(response.read())
                connection.close()
                return result
            body = {"session": self.session, "nonce": paused["installationAction"]["nonce"]}
            self.assertEqual(request(body, False)[0], 403)
            self.assertEqual(request({**body, "command": "uninstall"})[0], 400)
            code, result = request(body)
            self.assertEqual(code, 200)
            self.assertEqual(result["event"], "status")
            self.assertEqual(result["state"]["installationContentReinstallApproval"]["transaction"], "b" * 32)
            self.assertEqual(request(body)[0], 400)
        finally:
            httpd.shutdown(); httpd.close_owned(); thread.join(5)


if __name__ == "__main__": unittest.main()
