"""Explicit storage-recovery choices never reset the completed Player frontier."""
import copy
import json
from pathlib import Path
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_installation_recovery as fixture
from test_installation_recovery import failures, server, state, wizard

TRANSACTION = "a" * 32
HEADSET = "owned-Quest3"


def refusal():
    return state.WizardError("quest_signing_repair_wait", "App storage unavailable", failureStage="install",
        completedBuildRetained=True, canSkipInstall=True, retryAction="install", saveDataProtected=True,
        repairReason="restore-app-launch-unavailable", requiresRestoreChoice=True,
        manualLaunchAvailable=True, resetAppDataAvailable=True,
        restoreChoiceTransaction=TRANSACTION, restoreChoiceDeviceSerial=HEADSET)


class RestorationChoiceTests(unittest.TestCase):
    setUp = fixture.InstallationRecoveryTests.setUp
    forbid_earlier_work = fixture.InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = fixture.InstallationRecoveryTests.assert_prior_retained
    installer = fixture.InstallationRecoveryTests.installer

    def pause(self):
        self.installer(failure=refusal())
        return self.engine.run(self.session)

    def test_automatic_retry_never_grants_manual_or_reset_consent(self):
        self.forbid_earlier_work()
        self.pause()
        commands = self.installer()
        self.assertEqual(self.engine.run(self.session)["status"], "complete")
        for switch in ("--manual-storage-launch", "--reset-app-data", "--recovery-device"):
            self.assertNotIn(switch, commands[0])
        self.assert_prior_retained(self.store.load(self.session))

    def test_choices_survive_restart_and_execute_only_current_installation(self):
        for choice, switch in (("manual-launch", "--manual-storage-launch"), ("reset-app-data", "--reset-app-data")):
            with self.subTest(choice=choice):
                self.setUp()
                self.forbid_earlier_work()
                paused = self.pause()
                nonce = paused["installationAction"]["nonce"]
                before = {row["id"]: self.engine.key(paused, row["id"]) for row in paused["stages"]}
                self.engine = wizard.Engine(state.Store(self.store.root), self.repo)
                accepted = self.engine.approve_restoration_choice(self.session, nonce, choice)
                approval = accepted["installationRestoreApproval"]
                self.assertEqual((approval["choice"], approval["transaction"], approval["deviceSerial"]),
                                 (choice, TRANSACTION, HEADSET))
                self.assertNotIn("installationAction", accepted)
                self.assertEqual(before, {row["id"]: self.engine.key(accepted, row["id"]) for row in accepted["stages"]})
                self.assert_prior_retained(accepted)
                self.forbid_earlier_work()
                commands = self.installer()
                self.assertEqual(self.engine.run(self.session)["status"], "complete")
                argv = commands[0]
                self.assertEqual(argv[argv.index(switch) + 1], TRANSACTION)
                self.assertEqual(argv[argv.index("--recovery-device") + 1], HEADSET)
                self.assertNotIn("--reset-app-data" if choice == "manual-launch" else "--manual-storage-launch", argv)
                with self.assertRaises(state.WizardError):
                    self.engine.approve_restoration_choice(self.session, nonce, choice)

    def test_foreign_stale_missing_or_unoffered_choice_cannot_reset(self):
        self.forbid_earlier_work()
        paused = self.pause()
        nonce = paused["installationAction"]["nonce"]
        mutations = [lambda x: x["installationAction"].update(nonce="0" * 32),
                     lambda x: x["installationAction"].update(deviceSerial="other"),
                     lambda x: x["installationAction"].update(transaction="b" * 32),
                     lambda x: x["installationAction"].update(attempt=0),
                     lambda x: x["installationAction"].update(reason="backup-transfer-unavailable"),
                     lambda x: x["completed"]["build"]["details"].update(apkSha256="c" * 64),
                     lambda x: x["needsActions"][0]["parameters"].update(resetAppDataAvailable=False),
                     lambda x: x.update(status="running"),
                     lambda x: x.pop("installationAction")]
        for mutate in mutations:
            changed = copy.deepcopy(paused); mutate(changed); self.store.save(changed)
            with self.assertRaises(state.WizardError):
                self.engine.approve_restoration_choice(self.session, nonce, "reset-app-data")
            self.assertNotIn("installationRestoreApproval", self.store.load(self.session))
        self.store.save(paused)
        for choice in ("", "reset", "defer-restoration", None):
            with self.assertRaises(state.WizardError):
                self.engine.approve_restoration_choice(self.session, nonce, choice)

    def test_revised_choices_revoke_old_approval(self):
        self.forbid_earlier_work()
        paused = self.pause()
        accepted = self.engine.approve_restoration_choice(self.session, paused["installationAction"]["nonce"], "reset-app-data")
        changes = dict(accepted["choices"], language="en" if accepted["choices"]["language"] == "de" else "de")
        changed = self.store.amend(self.session, changes)
        self.assertNotIn("installationRestoreApproval", changed)


class RestorationChoiceApiTests(unittest.TestCase):
    setUp = fixture.InstallationHttpTests.setUp
    close = fixture.InstallationHttpTests.close
    forbid_earlier_work = fixture.InstallationRecoveryTests.forbid_earlier_work
    assert_prior_retained = fixture.InstallationRecoveryTests.assert_prior_retained
    installer = fixture.InstallationRecoveryTests.installer
    def request(self, value=None, method="POST"):
        # Reuse the real localhost authorization fixture, changing only route.
        import http.client
        connection = http.client.HTTPConnection("127.0.0.1", self.http.server_port, timeout=5)
        headers = {"Origin": self.http.origin, "X-Quest-Token": self.http.token, "Content-Type": "application/json"}
        connection.request(method, "/api/install-restore-choice", body=json.dumps(value or {}), headers=headers)
        response = connection.getresponse(); body = json.loads(response.read()); code = response.status
        connection.close(); return code, body

    def test_actual_route_approves_once_without_starting_a_device_job(self):
        self.forbid_earlier_work()
        self.installer(failure=refusal()); paused = self.engine.run(self.session)
        with mock.patch.object(self.http, "visible_state", side_effect=lambda _, value: value):
            code, value = self.request({"session": self.session, "nonce": paused["installationAction"]["nonce"],
                                        "choice": "reset-app-data"})
        self.assertEqual(code, 200)
        self.assertEqual(value["state"]["installationRestoreApproval"]["choice"], "reset-app-data")
        self.assert_prior_retained(value["state"])
        self.assertFalse(self.http.jobs)

    def test_active_operation_and_extra_body_fields_cannot_grant_consent(self):
        self.forbid_earlier_work()
        self.installer(failure=refusal()); paused = self.engine.run(self.session)
        body = {"session": self.session, "nonce": paused["installationAction"]["nonce"], "choice": "reset-app-data"}
        self.assertEqual(self.request(dict(body, force=True))[0], 400)
        from types import SimpleNamespace
        self.http.jobs["another"] = SimpleNamespace(is_alive=lambda: True)
        self.assertEqual(self.request(body)[1]["code"], "already_running")
        self.http.jobs.clear()
        self.assertNotIn("installationRestoreApproval", self.store.load(self.session))

if __name__ == "__main__": unittest.main()
