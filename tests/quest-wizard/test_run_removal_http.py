"""Explicit stopped-run previews remove only the confirmed owned session."""
import http.client
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import cleanup
import discovery
import server
from state import Store, atomic_json, file_lock


class RunRemovalHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.temp.name) / "owned")
        self.original = Path(self.temp.name) / "Game"
        self.original.mkdir()
        self.original_file = self.original / "original.bin"
        self.original_file.write_bytes(b"original PC game")
        self.session = self.create("cancelled")
        self.http = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui",
            discover=lambda repo, store: {"schema": 1, "event": "discovery"})
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.http.shutdown()
        self.http.close_owned()
        self.thread.join(5)
        cleanup._plans.clear()
        self.temp.cleanup()

    def create(self, status):
        value = self.store.create(server.choices({"gameRoot": str(self.original)}))
        value["status"] = status
        self.store.save(value)
        return value["session"]

    def request(self, route, body=None, *, token=True):
        connection = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Origin": self.http.origin, "X-Quest-Token": self.http.token if token else "wrong"}
        if body is not None:
            headers["Content-Type"] = "application/json"
        connection.request("GET" if body is None else "POST", route,
                           None if body is None else json.dumps(body), headers)
        response = connection.getresponse()
        value = json.loads(response.read())
        code = response.status
        connection.close()
        return code, value

    def wait_status(self, *wanted):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            value = self.request("/api/storage")[1]
            if value["state"]["status"] in wanted:
                return value["state"]
            time.sleep(.01)
        self.fail("Stopped-run removal did not reach " + str(wanted))

    def preview(self, *, session=None, remove_data=False):
        code, _ = self.request("/api/storage/plan", {
            "mode": "session", "session": session or self.session, "removeData": remove_data})
        self.assertEqual(code, 200)
        current = self.wait_status("preview", "failed")
        self.assertEqual(current["status"], "preview", current)
        return current["plan"]

    def test_lists_all_inactive_runs_not_only_eight_recent_items(self):
        retained = {self.session}
        for _ in range(11):
            retained.add(self.create("failed"))
        complete = self.create("complete")
        code, value = self.request("/api/storage/runs")
        self.assertEqual(code, 200)
        self.assertEqual(value["event"], "runs")
        self.assertEqual({row["session"] for row in value["runs"]}, retained)
        self.assertNotIn(complete, retained)
        self.assertEqual(value["latestSession"], complete)
        self.assertEqual(self.request("/api/storage/runs", token=False)[0], 403)
        self.assertEqual(self.request("/api/storage/runs?path=../")[0], 400)
        self.assertTrue(self.request("/api/discover")[1]["capabilities"]["runRemoval"])

    def test_stale_running_status_is_reconciled_but_kernel_owned_run_stays_active(self):
        stale = self.create("running")
        with file_lock(self.store.root / "run.lock"):
            value = self.request("/api/storage/runs")[1]
            self.assertNotIn(stale, {row["session"] for row in value["runs"]})
        value = self.request("/api/storage/runs")[1]
        self.assertEqual(next(row for row in value["runs"] if row["session"] == stale)["status"], "interrupted")

    def test_cleanup_choice_is_required_and_paths_cannot_be_supplied(self):
        invalid = [
            {"mode": "session", "session": self.session},
            {"mode": "session", "session": self.session, "removeData": "false"},
            {"mode": "session", "session": "../Game", "removeData": True},
            {"mode": "session", "session": self.session, "removeData": True, "path": str(self.original)},
        ]
        for body in invalid:
            self.assertEqual(self.request("/api/storage/plan", body)[0], 400)
        self.assertIsNone(self.http.storage_job)
        self.assertTrue(self.store.session_dir(self.session).is_dir())

    def test_preview_never_deletes_and_confirmation_removes_only_selected_run(self):
        survivor = self.session
        selected = self.create("blocked")
        opaque = self.store.root / "build/cache/unproven.bin"
        opaque.parent.mkdir(parents=True)
        opaque.write_bytes(b"reusable unknown data")
        self.http.job_errors[selected] = RuntimeError("old saved job")
        plan = self.preview(session=selected)
        self.assertEqual(plan["mode"], "session")
        self.assertEqual(plan["session"], selected)
        self.assertIs(plan["removeData"], False)
        self.assertTrue(self.store.session_dir(selected).is_dir())
        self.assertEqual(self.request("/api/storage/clean", {"planId": "wrong"})[0], 400)
        self.assertTrue(self.store.session_dir(selected).is_dir())
        self.assertEqual(self.request("/api/storage/clean", {"planId": plan["id"]})[0], 200)
        result = self.wait_status("complete", "failed")
        self.assertEqual(result["status"], "complete", result)
        self.assertEqual(result["result"]["sessionsRemoved"], [selected])
        self.assertFalse(self.store.session_dir(selected).exists())
        self.assertTrue(self.store.session_dir(survivor).is_dir())
        self.assertTrue(opaque.is_file())
        self.assertEqual(self.original_file.read_bytes(), b"original PC game")
        self.assertNotIn(selected, self.http.job_errors)
        recent, latest = discovery.recent_sessions(self.store)
        self.assertEqual(latest, survivor)
        self.assertEqual({row["session"] for row in recent}, {survivor})
        self.assertIn('"mode": "session"', (self.store.root / "logs/storage-cleanup.log").read_text())

    def test_remove_last_run_clears_resume_selection_and_retains_deliverables(self):
        apk = self.store.root / "build/builds" / ("a" * 64) / "owned.apk"
        apk.parent.mkdir(parents=True)
        apk.write_bytes(b"existing signed APK")
        bank = apk.with_suffix(".zip")
        bank.write_bytes(b"existing game bank")
        key = self.store.root / "build/signing/user.keystore"
        key.parent.mkdir(parents=True)
        key.write_bytes(b"local signing identity")
        plan = self.preview(remove_data=True)
        self.assertEqual(self.request("/api/storage/clean", {"planId": plan["id"]})[0], 200)
        current = self.wait_status("complete", "failed")
        self.assertEqual(current["status"], "complete", current)
        self.assertEqual(discovery.recent_sessions(self.store), ([], None))
        self.assertEqual(self.request("/api/storage/runs")[1]["runs"], [])
        self.assertTrue(apk.is_file() and bank.is_file() and key.is_file())

    def test_completed_run_and_actual_active_build_cannot_be_discarded(self):
        complete = self.create("complete")
        self.assertEqual(self.request("/api/storage/plan", {
            "mode": "session", "session": complete, "removeData": True})[0], 200)
        self.assertEqual(self.wait_status("failed")["status"], "failed")
        self.assertTrue(self.store.session_dir(complete).is_dir())
        stopped = threading.Event()
        job = threading.Thread(target=stopped.wait)
        self.http.jobs[self.session] = job
        job.start()
        try:
            self.assertEqual(self.request("/api/storage/plan", {
                "mode": "session", "session": self.session, "removeData": True})[0], 400)
        finally:
            stopped.set()
            job.join(5)
        self.assertTrue(self.store.session_dir(self.session).is_dir())


if __name__ == "__main__":
    unittest.main()
