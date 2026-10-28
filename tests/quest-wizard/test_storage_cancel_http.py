"""Storage cancellation witnesses one worker, reaps it and retains diagnostics."""
import http.client
import io
import json
from pathlib import Path
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import server
from state import Cancelled, Store, WizardError, atomic_json, file_lock


class StorageCancelHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.temp.name) / "workspace")
        self.http = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui")
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True)
        self.thread.start()
        self.entered, self.stopping, self.release, self.build_release = (threading.Event() for _ in range(4))
        self.plan = {"id": "confirmed-preview", "mode": "build-cache", "files": 4, "bytes": 1024,
                     "paths": [{"path": "build/cache/owned", "files": 4, "bytes": 1024}]}
        self.helper = SimpleNamespace(plan=self.slow_plan, execute=self.slow_delete,
                                     has_completed_build=lambda _: True, inventory=self.slow_inventory)
        self.patch = mock.patch.dict(sys.modules, {"cleanup": self.helper})
        self.patch.start()

    def tearDown(self):
        self.release.set(); self.build_release.set()
        for job in self.http.jobs.values(): job.join(5)
        self.http.shutdown(); self.http.close_owned(); self.thread.join(5)
        self.patch.stop(); self.temp.cleanup()

    def request(self, route, body=None, *, token=True, binary=False):
        conn = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Origin": self.http.origin, "X-Quest-Token": self.http.token if token else "wrong"}
        if body is not None: headers["Content-Type"] = "application/json"
        conn.request("GET" if body is None else "POST", route, None if body is None else json.dumps(body), headers)
        response = conn.getresponse(); raw = response.read(); code = response.status; conn.close()
        return code, raw if binary else json.loads(raw)

    def wait(self, target, status):
        end = time.monotonic() + 3
        while time.monotonic() < end:
            value = self.request("/api/storage")[1]["state" if target == "cleanup" else "inventory"]
            if value["status"] == status: return value
            time.sleep(.01)
        self.fail(f"{target} did not reach {status}")

    def wait_for_cancel(self, cancel):
        self.entered.set()
        if not cancel.wait(3): raise AssertionError("Cancellation was not delivered")
        self.stopping.set()
        self.release.wait(3)
        raise Cancelled()

    def slow_plan(self, root, mode, *, progress, cancel):
        with file_lock(root / "run.lock"):
            for count in range(500):
                progress({"phase": "cleanup-scan", "done": count, "total": None, "files": count,
                          "folders": 23, "bytes": count * 1024, "elapsedSeconds": 7,
                          "metadataQueries": count + 1, "processedEntries": count + 5,
                          "subphase": "fingerprint",
                          "currentPath": "build/cache/owned", "detail": "build/cache/owned"})
            self.wait_for_cancel(cancel)

    def slow_delete(self, root, selected, *, progress, cancel):
        with file_lock(root / "run.lock"):
            progress({"phase": "cleanup-delete", "done": 2, "total": 5, "unit": "entries",
                      "files": 1, "bytes": 256, "folders": 1, "currentPath": "build/cache/owned/a"})
            try: self.wait_for_cancel(cancel)
            except Cancelled as error:
                error.parameters["result"] = {"deletedFiles": 1, "freedBytes": 256, "sessionsRemoved": []}
                raise

    def slow_inventory(self, root, *, progress, cancel):
        progress({"phase": "storage-inventory", "done": 371712, "files": 371712, "bytes": 2 ** 30,
                  "folders": 500, "elapsedSeconds": 19, "currentPath": "build/projects/owned"})
        self.wait_for_cancel(cancel)

    def cancel(self, target):
        current = self.request("/api/storage")[1]["state" if target == "cleanup" else "inventory"]
        return self.request("/api/storage/cancel", {"target": target, "operationId": current["operationId"]})

    def test_preview_cancel_waits_for_worker_lock_release_and_retains_bounded_progress(self):
        self.assertEqual(self.request("/api/storage/plan", {"mode": "build-cache"})[0], 200)
        self.assertTrue(self.entered.wait(2))
        self.assertEqual(self.cancel("cleanup")[0], 200); self.assertTrue(self.stopping.wait(2))
        self.assertEqual(self.request("/api/storage")[1]["state"]["status"], "cancelling")
        self.release.set(); state = self.wait("cleanup", "cancelled")
        self.assertFalse(self.http.storage_job.is_alive())
        with file_lock(self.store.root / "run.lock"): pass
        self.assertEqual(state["files"], 499); self.assertEqual(state["selection"]["mode"], "build-cache")
        self.assertEqual(state["metadataQueries"], 500); self.assertEqual(state["processedEntries"], 504)
        self.assertIsNone(self.http.storage_plan)
        rows = [json.loads(row) for row in (self.store.root / "logs/storage-cleanup.log").read_text().splitlines()]
        self.assertLessEqual(sum(row["event"] == "progress" for row in rows), 2)
        progress_row = next(row for row in rows if row["event"] == "progress")
        self.assertEqual(progress_row["metadataQueries"], 1); self.assertEqual(progress_row["processedEntries"], 5)
        self.assertEqual(progress_row["subphase"], "fingerprint")
        self.assertEqual(next(row for row in rows if row["event"] == "cancelled")["progress"]["files"], 499)
        self.assertNotIn("failed", [row["event"] for row in rows])
        code, raw = self.request("/api/support", {}, binary=True)
        self.assertEqual(code, 200)
        with zipfile.ZipFile(io.BytesIO(raw)) as archive:
            self.assertIn('"event": "cancelled"', archive.read("wizard/storage-cleanup.log").decode())

    def test_inventory_cancellation_never_cancels_an_independent_live_build(self):
        build = threading.Thread(target=self.build_release.wait, args=(3,)); build.start()
        self.http.jobs["1" * 32] = build
        with mock.patch.object(self.store, "cancel", side_effect=AssertionError("Must not cancel build")):
            self.assertEqual(self.request("/api/storage/inventory", {})[0], 200)
            self.assertTrue(self.entered.wait(2)); self.cancel("inventory"); self.release.set()
            state = self.wait("inventory", "cancelled")
            self.assertTrue(build.is_alive()); self.assertFalse(self.http.inventory_job.is_alive())
            self.assertEqual(state["done"], 371712)

    def test_partial_deletion_survives_cancellation_and_requires_a_new_preview(self):
        self.helper.plan = lambda root, mode, **_: self.plan
        self.request("/api/storage/plan", {"mode": "build-cache"}); self.wait("cleanup", "preview")
        self.request("/api/storage/clean", {"planId": self.plan["id"]}); self.assertTrue(self.entered.wait(2))
        self.cancel("cleanup"); self.release.set(); state = self.wait("cleanup", "cancelled")
        self.assertEqual(state["result"]["freedBytes"], 256)
        self.assertEqual(state["selection"]["mode"], "build-cache")
        self.assertEqual(self.request("/api/storage/clean", {"planId": self.plan["id"]})[0], 400)
        self.request("/api/storage/plan", {"mode": "build-cache"})
        self.assertEqual(self.wait("cleanup", "preview")["plan"]["id"], self.plan["id"])

    def test_unrepaired_file_failure_retains_counters_and_actionable_error_parameters(self):
        def fail(root, mode, *, progress, cancel):
            progress({"phase": "cleanup-check", "files": 123, "done": 123, "total": None,
                      "bytes": 2048, "folders": 3, "currentPath": "source/owned"})
            raise WizardError("cleanup_file_busy", "Close the program using this file, refresh the preview and retry.",
                              "Das Programm mit Dateizugriff schließen, die Vorschau erneuern und wiederholen.",
                              path="source/owned/locked.bin", retryable=True, recovery="refresh-preview",
                              result={"deletedFiles": 2, "freedBytes": 512, "sessionsRemoved": []})
        self.helper.plan = fail
        self.request("/api/storage/plan", {"mode": "build-cache"}); state = self.wait("cleanup", "failed")
        self.assertEqual(state["files"], 123)
        self.assertTrue(state["error"]["parameters"]["retryable"])
        self.assertEqual(state["result"]["deletedFiles"], 2)
        self.assertEqual(state["selection"]["mode"], "build-cache")
        rows = [json.loads(row) for row in (self.store.root / "logs/storage-cleanup.log").read_text().splitlines()]
        self.assertEqual(rows[-1]["progress"]["files"], 123)

    def test_cancellation_is_authenticated_and_an_old_operation_cannot_stop_a_new_one(self):
        self.request("/api/storage/plan", {"mode": "build-cache"}); self.assertTrue(self.entered.wait(2))
        current = self.request("/api/storage")[1]["state"]
        body = {"target": "cleanup", "operationId": current["operationId"]}
        self.assertEqual(self.request("/api/storage/cancel", body, token=False)[0], 403)
        for invalid in ({}, {**body, "target": "build"}, {**body, "operationId": "wrong"}, {**body, "path": "/"}):
            self.assertEqual(self.request("/api/storage/cancel", invalid)[0], 400)
        self.assertEqual(self.request("/api/storage/cancel", {**body, "operationId": "0" * 32})[1]["code"], "cleanup_cancel_stale")
        self.assertFalse(self.http.storage_stop.is_set())
        self.cancel("cleanup"); self.release.set(); self.wait("cleanup", "cancelled")
        self.helper.plan = lambda root, mode, **_: self.plan
        self.request("/api/storage/plan", {"mode": "build-cache"}); self.wait("cleanup", "preview")
        self.assertEqual(self.request("/api/storage/cancel", body)[1]["code"], "cleanup_cancel_stale")

    def test_owned_cached_measurement_is_loaded_as_stale_without_a_tree_walk(self):
        atomic_json(self.store.root / "logs/storage-analysis.json", {"schema": 1, "status": "complete",
                    "totalBytes": 1234, "files": 500, "completedAt": 123.0, "runs": []})
        with mock.patch.object(self.helper, "inventory", side_effect=AssertionError("No hidden scan")):
            other = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui")
            try:
                self.assertEqual(other.inventory_state["status"], "stale")
                self.assertEqual(other.storage_status()["inventory"]["totalBytes"], 1234)
                self.assertIsNone(other.inventory_job)
            finally: other.close_owned()


if __name__ == "__main__": unittest.main()
