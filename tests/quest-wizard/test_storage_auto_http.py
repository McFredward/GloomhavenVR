"""One-click cleanup uses the safe producer policy without a manual preview."""
import sys
from pathlib import Path
import threading
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import test_storage_http as fixtures
from state import WizardError


class AutomaticStorageHttpTests(fixtures.StorageHttpTests):
    # The inherited tests qualify unchanged manual preview admission as well.
    def setUp(self):
        super().setUp()
        self.policy = {"status": "preserved", "messages": {
            "en": "Fast update data is retained.", "de": "Daten für schnelle Updates bleiben erhalten."}}
        self.auto_plan = {**self.plan, "mode": "safe", "fastUpdateImpact": self.policy,
                          "protectedPaths": ["build/updates/code-projects"], "skipped": []}
        self.executed = []

    def install_automatic_policy(self):
        def select(root, mode, *, progress, cancel):
            self.assertEqual(root, self.store.root)
            self.assertEqual(mode, "safe")
            self.entered.set()
            progress({"phase": "cleanup-discover", "files": 0, "done": 0, "total": None})
            self.release.wait(5)
            return self.auto_plan
        def remove(root, plan, *, progress, cancel):
            self.assertEqual(plan, self.auto_plan)
            self.executed.append(plan)
            progress({"phase": "cleanup-delete", "done": 1, "total": 1})
            return {"freedBytes": 200, "deletedFiles": 1, "invalidatedStages": [], "sessionsRemoved": []}
        self.helper.plan = select
        self.helper.execute = remove

    def test_one_click_plans_and_deletes_without_inventory_or_confirmation(self):
        self.install_automatic_policy()
        self.helper.inventory = mock.Mock(side_effect=AssertionError("No whole-workspace scan is needed"))
        self.assertEqual(self.request("/api/storage/auto", {})[0], 200)
        self.assertTrue(self.entered.wait(2))
        current = self.request("/api/storage")[1]["state"]
        self.assertEqual(current["status"], "planning")
        self.assertTrue(current["automatic"])
        self.assertEqual(current["selection"]["mode"], "safe")
        self.assertEqual(self.executed, [])
        self.release.set()
        done = self.wait_status("complete")["state"]
        self.assertTrue(done["automatic"])
        self.assertEqual(done["result"]["freedBytes"], 200)
        self.assertEqual(done["result"]["fastUpdateImpact"], self.policy)
        self.assertEqual(done["result"]["protectedPaths"], self.auto_plan["protectedPaths"])
        self.assertEqual(len(self.executed), 1)
        self.assertIsNone(self.http.storage_plan)
        self.helper.inventory.assert_not_called()
        log = (self.store.root / "logs/storage-cleanup.log").read_text()
        self.assertIn('"event": "automatic-selection"', log)
        self.assertIn('"mode": "safe"', log)

    def test_auto_route_rejects_unauthenticated_or_client_selected_deletions(self):
        self.install_automatic_policy()
        self.assertEqual(self.request("/api/storage/auto", {}, token=False)[0], 403)
        for body in ({"mode": "build-cache"}, {"planId": self.plan["id"]}, {"paths": ["build"]}):
            self.assertEqual(self.request("/api/storage/auto", body)[0], 400)
        self.assertFalse(self.entered.is_set())
        self.assertIsNone(self.http.storage_job)

    def test_auto_refuses_a_policy_that_could_remove_fast_update_data(self):
        self.install_automatic_policy()
        self.auto_plan["fastUpdateImpact"] = {"status": "slower"}
        self.release.set()
        self.assertEqual(self.request("/api/storage/auto", {})[0], 200)
        failure = self.wait_status("failed")["state"]
        self.assertEqual(failure["error"]["code"], "cleanup_automatic_policy")
        self.assertEqual(self.executed, [])

    def test_cancellation_after_selection_never_starts_deletion(self):
        self.install_automatic_policy()
        self.assertEqual(self.request("/api/storage/auto", {})[0], 200)
        self.assertTrue(self.entered.wait(2))
        current = self.request("/api/storage")[1]["state"]
        self.assertEqual(self.request("/api/storage/cancel", {
            "target": "cleanup", "operationId": current["operationId"]})[0], 200)
        self.release.set()
        self.http.storage_job.join(5)
        current = self.request("/api/storage")[1]["state"]
        self.assertEqual(current["status"], "cancelled")
        self.assertEqual(self.executed, [])

    def test_auto_does_not_replace_an_active_build(self):
        self.install_automatic_policy()
        active = threading.Thread(target=self.release.wait, args=(5,))
        active.start()
        try:
            self.http.jobs[self.session] = active
            code, value = self.request("/api/storage/auto", {})
            self.assertEqual(code, 400)
            self.assertEqual(value["code"], "already_running")
            self.assertFalse(self.entered.is_set())
            self.assertEqual(self.executed, [])
        finally:
            self.release.set()
            active.join(5)

    def test_partial_deletion_failure_keeps_its_result_visible(self):
        self.install_automatic_policy()
        def interrupted(*args, **kwargs):
            error = WizardError("cleanup_io_error", "A file is in use.", "Eine Datei wird gerade verwendet.")
            error.parameters["result"] = {"freedBytes": 50, "deletedFiles": 1, "sessionsRemoved": []}
            raise error
        self.helper.execute = interrupted
        self.release.set()
        self.assertEqual(self.request("/api/storage/auto", {})[0], 200)
        failure = self.wait_status("failed")["state"]
        self.assertEqual(failure["result"]["freedBytes"], 50)
        self.assertEqual(failure["error"]["code"], "cleanup_io_error")
        self.assertTrue(failure["automatic"])


if __name__ == "__main__":
    unittest.main()
