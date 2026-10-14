"""Authenticated standalone tools share build ownership without amending receipts."""
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
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import server
from state import Store, WizardError, file_lock


class DeviceHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.store = Store(Path(self.temp.name) / "owned")
        self.entered, self.release = threading.Event(), threading.Event()
        self.calls = []
        self.identity = "a" * 32
        self.capture_path = self.store.root / "device/captures/quest-capture-fixture.zip"
        self.capture_path.parent.mkdir(parents=True)
        with zipfile.ZipFile(self.capture_path, "w") as archive:
            archive.writestr("summary.json", '{"hardwareVerified":false}')
        self.adapter = SimpleNamespace(connect=self.connect, capture=self.capture, start=self.start, download=self.download,
                                       storage_preview=self.storage_preview, storage_clean=self.storage_clean)
        self.http = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui",
                                       device_factory=self.factory,
                                       discover=lambda *_: {"schema": 1, "event": "discover", "capabilities": {}})
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True)
        self.thread.start()

    def tearDown(self):
        self.release.set()
        self.http.shutdown()
        self.http.close_owned()
        self.thread.join(5)
        self.temp.cleanup()

    def factory(self, repo, root):
        self.assertEqual(root, self.store.root)
        self.calls.append("factory")
        return self.adapter

    def connect(self, options, *, progress, cancel):
        self.calls.append(("connect", options))
        self.entered.set()
        progress({"progress": {"done": 2, "total": 4, "percent": 50},
                  "message": {"en": "Waiting for USB authorization", "de": "USB-Freigabe erforderlich"}})
        while not self.release.wait(.01):
            if cancel.is_set(): raise WizardError("device_cancelled", "Quest operation cancelled.")
        return {"status": "connected", "endpoint": "192.0.2.1:5555", "serial": "fixture"}

    def capture(self, options, *, progress, cancel):
        self.calls.append(("capture", options))
        return {"status": "complete", "capture": {"id": self.identity, "name": self.capture_path.name}}

    def start(self, options, *, progress, cancel):
        self.calls.append(("start", options))
        return {"status": "debug-wait" if options["waitForDebugger"] else "started",
                "launch": {"waitForDebugger": options["waitForDebugger"], "processId": 123}}

    def download(self, identifier):
        if identifier != self.identity: raise WizardError("capture_unavailable", "Unknown capture.")
        return self.capture_path

    def storage_preview(self, options, *, progress, cancel):
        self.calls.append(("storage-preview", options))
        self.entered.set()
        progress({"status": "scanning", "progress": {"done": 3, "total": 6, "percent": 50}})
        while not self.release.wait(.01):
            if cancel.is_set(): raise WizardError("device_cancelled", "Quest operation cancelled.")
        return {"status": "storage-preview", "storagePlan": {"planId": "c" * 32, "bytes": 1024}}

    def storage_clean(self, options, *, progress, cancel):
        self.calls.append(("storage-clean", options))
        return {"status": "storage-complete", "storageResult": {"freedBytes": 1024, "deletedFiles": 1}}

    def request(self, route, body=None, *, token=True, raw=False):
        connection = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Origin": self.http.origin, "X-Quest-Token": self.http.token if token else "wrong"}
        if body is not None: headers["Content-Type"] = "application/json"
        connection.request("GET" if body is None else "POST", route,
                           None if body is None else json.dumps(body), headers)
        response = connection.getresponse()
        payload = response.read()
        result = (response.status, payload if raw else json.loads(payload))
        connection.close()
        return result

    def wait_status(self, wanted):
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline:
            value = self.request("/api/device/status")[1]
            if value["state"]["status"] == wanted: return value
            time.sleep(.01)
        self.fail("Device operation did not reach " + wanted)

    def test_tools_are_available_without_build_session_and_discovery_advertises_them(self):
        self.assertEqual(self.request("/api/device/status")[1]["state"]["status"], "idle")
        capabilities = self.request("/api/discover")[1]["capabilities"]
        self.assertTrue(capabilities["deviceTools"])
        self.assertTrue(capabilities["storageInventory"])
        self.assertEqual(self.calls, [])

    def test_authorization_and_request_fields_precede_adapter_creation(self):
        options = {"host": "", "serial": "", "setupWireless": True}
        self.assertEqual(self.request("/api/device/connect", options, token=False)[0], 403)
        for bad in ({**options, "command": "shell"}, {**options, "host": []},
                    {**options, "setupWireless": 1}, {"host": ""}):
            self.assertEqual(self.request("/api/device/connect", bad)[0], 400)
        self.assertEqual(self.request("/api/device/capture", {"host": "", "serial": "", "path": "../"})[0], 400)
        self.assertEqual(self.calls, [])

    def test_start_requires_authentication_and_exact_explicit_boolean_before_adapter_creation(self):
        options = {"host": "", "serial": "", "waitForDebugger": False}
        self.assertEqual(self.request("/api/device/start", options, token=False)[0], 403)
        for bad in ({"host": "", "serial": ""}, {**options, "waitForDebugger": 0},
                    {**options, "waitForDebugger": "true"}, {**options, "host": []},
                    {**options, "command": "shell"}):
            self.assertEqual(self.request("/api/device/start", bad)[0], 400)
        self.assertEqual(self.calls, [])
        self.assertEqual(self.request("/api/device/start", options)[0], 200)
        self.wait_status("started")
        options["waitForDebugger"] = True
        self.assertEqual(self.request("/api/device/start", options)[0], 200)
        self.wait_status("debug-wait")
        self.assertEqual([row[1] for row in self.calls if isinstance(row, tuple) and row[0] == "start"],
                         [{"host": "", "serial": "", "waitForDebugger": False}, options])

    def test_connection_reports_progress_and_rejects_competing_build_cleanup_and_device_jobs(self):
        options = {"host": "", "serial": "", "setupWireless": True}
        self.assertEqual(self.request("/api/device/connect", options)[0], 200)
        self.assertTrue(self.entered.wait(2))
        self.assertEqual(self.request("/api/device/status")[1]["state"]["progress"]["percent"], 50)
        state = self.store.create(server.choices({"gameRoot": str(Path(self.temp.name) / "game")}))
        self.assertEqual(self.request("/api/run", {"session": state["session"]})[0], 400)
        self.assertEqual(self.request("/api/storage/plan", {"mode": "duplicates"})[0], 400)
        self.assertEqual(self.request("/api/device/capture", {"host": "", "serial": ""})[0], 400)
        self.assertEqual(self.store.load(state["session"])["status"], "ready")
        self.release.set()
        self.wait_status("connected")

    def test_cancellation_does_not_touch_existing_build_state(self):
        state = self.store.create(server.choices({"gameRoot": str(Path(self.temp.name) / "game")}))
        before = self.store.load(state["session"])
        self.request("/api/device/connect", {"host": "", "serial": "", "setupWireless": False})
        self.assertTrue(self.entered.wait(2))
        self.assertEqual(self.request("/api/device/cancel", {})[0], 200)
        self.wait_status("cancelled")
        self.assertEqual(self.store.load(state["session"]), before)

    def test_another_process_lock_prevents_backend_commands(self):
        with file_lock(self.store.root / "run.lock"):
            self.request("/api/device/connect", {"host": "", "serial": "", "setupWireless": False})
            state = self.wait_status("failed")["state"]
            self.assertEqual(state["error"]["code"], "already_running")
        self.assertFalse(self.entered.is_set())
        self.assertEqual(self.calls, ["factory"])

    def test_capture_download_is_opaque_authenticated_and_never_accepts_host_paths(self):
        self.assertEqual(self.request("/api/device/capture?id=" + self.identity)[0], 400)
        self.request("/api/device/capture", {"host": "", "serial": ""})
        self.wait_status("complete")
        self.assertEqual(self.request("/api/device/capture?id=" + self.identity, token=False)[0], 403)
        self.assertEqual(self.request("/api/device/capture?id=../private")[0], 400)
        self.assertEqual(self.request("/api/device/capture?id=" + self.identity + "&path=../private")[0], 400)
        self.assertEqual(self.request("/api/device/capture?id=" + "b" * 32)[0], 400)
        code, raw = self.request("/api/device/capture?id=" + self.identity, raw=True)
        self.assertEqual(code, 200)
        self.assertEqual(raw, self.capture_path.read_bytes())

    def test_inventory_is_asynchronous_and_polling_does_not_repeat_scan(self):
        scans = []
        def inventory(root, *, progress, cancel):
            scans.append(root)
            progress({"done": 2, "total": None})
            self.entered.set()
            self.release.wait(5)
            return {"totalBytes": 42, "categories": [{"id": "apk-content", "bytes": 42, "files": 1}]}
        with mock.patch.dict(sys.modules, {"cleanup": SimpleNamespace(inventory=inventory)}):
            self.assertEqual(self.request("/api/storage/inventory", {"path": "/"})[0], 400)
            self.assertEqual(self.request("/api/storage/inventory", {}, token=False)[0], 403)
            self.assertEqual(self.request("/api/storage/inventory", {})[0], 200)
            self.assertTrue(self.entered.wait(2))
            self.assertEqual(self.request("/api/storage")[1]["inventory"]["done"], 2)
            self.assertEqual(self.request("/api/storage/inventory", {})[0], 200)
            for _ in range(3): self.assertEqual(self.request("/api/storage")[1]["inventory"]["status"], "scanning")
            self.assertEqual(len(scans), 1)
            self.release.set()
            self.http.inventory_job.join(5)
            value = self.request("/api/storage")[1]["inventory"]
            self.assertEqual(value["status"], "complete")
            self.assertEqual(value["totalBytes"], 42)
            self.assertEqual(len(scans), 1)

    def test_device_cleanup_preview_is_authenticated_cancelable_and_exclusive(self):
        options = {"host": "", "serial": ""}
        self.assertEqual(self.request("/api/device/storage-preview", options, token=False)[0], 403)
        self.assertEqual(self.request("/api/device/storage-preview", {**options, "path": "/"})[0], 400)
        self.assertEqual(self.request("/api/device/storage-clean", {"planId": "../"})[0], 400)
        self.assertEqual(self.calls, [])
        self.request("/api/device/storage-preview", options)
        self.assertTrue(self.entered.wait(2))
        self.assertEqual(self.request("/api/device/status")[1]["state"]["progress"]["percent"], 50)
        self.assertEqual(self.request("/api/device/storage-clean", {"planId": "c" * 32})[0], 400)
        self.request("/api/device/cancel", {})
        self.wait_status("cancelled")

    def test_device_cleanup_routes_pass_only_the_exact_preview_nonce_to_owner(self):
        self.release.set()
        self.request("/api/device/storage-preview", {"host": "", "serial": ""})
        self.wait_status("storage-preview")
        self.assertEqual(self.request("/api/device/storage-clean", {"planId": "c" * 32, "path": "/"})[0], 400)
        self.request("/api/device/storage-clean", {"planId": "c" * 32})
        self.wait_status("storage-complete")
        self.assertIn(("storage-clean", {"planId": "c" * 32}), self.calls)


if __name__ == "__main__": unittest.main()
