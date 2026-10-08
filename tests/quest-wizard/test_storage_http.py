"""Responsive authenticated cleanup ownership; deletion safety has separate tests."""
import http.client
import json
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
import server
from state import Store, WizardError


class StorageHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.temp.name) / "state")
        self.session = self.store.create(server.choices({"gameRoot": str(Path(self.temp.name) / "Game")}))["session"]
        self.http = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui")
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True)
        self.thread.start()
        self.release = threading.Event()
        self.entered = threading.Event()
        self.plan = {"id": "a" * 64, "mode": "duplicates", "paths": [], "bytes": 0, "files": 0, "exact": True}
        self.helper = SimpleNamespace(plan=self.plan_action, execute=self.delete_action)
        self.patch = mock.patch.dict(sys.modules, {"cleanup": self.helper})
        self.patch.start()

    def tearDown(self):
        self.release.set()
        self.http.shutdown()
        self.http.close_owned()
        self.thread.join(5)
        self.patch.stop()
        self.temp.cleanup()

    def plan_action(self, root, mode, progress):
        self.entered.set()
        progress({"phase": "scan", "done": 3, "total": None, "detail": "Owned folder metadata"})
        self.release.wait(5)
        return {**self.plan, "mode": mode}

    def delete_action(self, root, selected, progress):
        self.assertEqual(selected, self.plan)
        progress({"phase": "delete", "done": 2, "total": 2})
        return {"freedBytes": 10, "deletedFiles": 2, "invalidatedStages": []}

    def request(self, route, body=None, token=True):
        connection = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Origin": self.http.origin, "X-Quest-Token": self.http.token if token else "wrong"}
        if body is not None: headers["Content-Type"] = "application/json"
        connection.request("GET" if body is None else "POST", route, None if body is None else json.dumps(body), headers)
        response = connection.getresponse()
        value = json.loads(response.read())
        code = response.status
        connection.close()
        return code, value

    def wait_status(self, wanted):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            value = self.request("/api/storage")[1]
            if value["state"]["status"] == wanted: return value
            time.sleep(.01)
        self.fail("Storage job did not reach " + wanted)

    def test_authenticated_storage_location_is_available_without_a_build_session(self):
        code, value = self.request("/api/storage")
        self.assertEqual(code, 200)
        self.assertEqual(value["workspaceRoot"], str(self.store.root))
        self.assertGreater(value["freeBytes"], 0)
        self.assertEqual(self.request("/api/storage", token=False)[0], 403)
        self.assertEqual(self.request("/api/storage/plan", {"mode": "duplicates"}, token=False)[0], 403)
        self.assertIsNone(self.http.storage_job)

    def test_scan_remains_responsive_and_cannot_delete_without_current_preview(self):
        code, _ = self.request("/api/storage/plan", {"mode": "duplicates"})
        self.assertEqual(code, 200)
        self.assertTrue(self.entered.wait(2))
        value = self.request("/api/storage")[1]
        self.assertEqual(value["state"]["status"], "planning")
        self.assertEqual(value["state"]["done"], 3)
        self.assertEqual(self.request("/api/run", {"session": self.session})[0], 400)
        self.assertEqual(self.request("/api/storage/clean", {"planId": self.plan["id"]})[0], 400)
        self.release.set()
        self.wait_status("preview")
        self.assertEqual(self.request("/api/storage/clean", {"planId": "wrong"})[0], 400)
        self.assertEqual(self.request("/api/storage/clean", {"planId": self.plan["id"], "path": "../Game"})[0], 400)
        self.assertEqual(self.request("/api/storage/clean", {"planId": self.plan["id"]})[0], 200)
        result = self.wait_status("complete")["state"]["result"]
        self.assertEqual(result["freedBytes"], 10)
        logged = (self.store.root / "logs/storage-cleanup.log").read_text()
        self.assertIn('"event": "preview"', logged)
        self.assertIn('"freedBytes": 10', logged)
        self.assertNotIn(self.plan["id"], logged)
        self.assertEqual(self.request("/api/storage/clean", {"planId": self.plan["id"]})[0], 400)

    def test_active_build_blocks_cleanup_before_scanning(self):
        active = threading.Thread(target=self.release.wait, args=(5,))
        active.start()
        self.http.jobs[self.session] = active
        self.assertEqual(self.request("/api/storage/plan", {"mode": "duplicates"})[0], 400)
        self.assertFalse(self.entered.is_set())
        self.assertIsNone(self.http.storage_job)
        self.release.set()
        active.join(5)

    def test_cleanup_failures_are_visible_and_a_fresh_preview_is_required(self):
        self.release.set()
        self.request("/api/storage/plan", {"mode": "duplicates"})
        self.wait_status("preview")
        def fail(*_args, **_kwargs):
            raise WizardError("cleanup_changed", "Files changed; refresh the preview.", "Dateien geändert; Vorschau erneuern.")
        self.helper.execute = fail
        self.request("/api/storage/clean", {"planId": self.plan["id"]})
        value = self.wait_status("failed")
        self.assertEqual(value["state"]["error"]["code"], "cleanup_changed")
        self.assertIn("Vorschau", value["state"]["error"]["message"]["de"])
        self.assertIsNone(self.http.storage_plan)
        self.assertEqual(self.request("/api/storage/clean", {"planId": self.plan["id"]})[0], 400)


if __name__ == "__main__": unittest.main()
