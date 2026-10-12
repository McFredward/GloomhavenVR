"""Production HTTP export ownership and bounded update/package input seams."""
import http.client
import importlib.util
import json
import shutil
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
import discovery
import host_paths
import package_tools
import qualification
import server
from state import Store, WizardError, file_lock
import wizard


class PortablePackageHttpTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name); self.store = Store(self.root / "workspace")
        self.source = self.root / "player.apk"; self.source.write_bytes(b"owned apk fixture")
        self.destination = self.store.root / "exports/player.zip"
        self.entered, self.release = threading.Event(), threading.Event()
        self.calls = []
        helper = SimpleNamespace(portable_packages=SimpleNamespace(export_package=self.export))
        self.loader = mock.patch.object(discovery, "builder", return_value=helper); self.loader.start()
        self.http = server.LocalServer(self.store, ROOT / "tools/quest-wizard-ui",
            discover=lambda *_: {"schema": 1, "event": "discovery", "capabilities": {}})
        self.thread = threading.Thread(target=self.http.serve_forever, daemon=True); self.thread.start()

    def tearDown(self):
        self.release.set(); self.http.shutdown(); self.http.close_owned(); self.thread.join(5)
        self.loader.stop(); self.temp.cleanup()

    def request(self, route, body=None, token=True):
        connection = http.client.HTTPConnection(*self.http.server_address, timeout=3)
        headers = {"Origin": self.http.origin, "X-Quest-Token": self.http.token if token else "wrong"}
        if body is not None: headers["Content-Type"] = "application/json"
        connection.request("GET" if body is None else "POST", route, None if body is None else json.dumps(body), headers)
        response = connection.getresponse(); result = response.status, json.loads(response.read()); connection.close()
        return result

    def export(self, apk, destination, output, *, source_package, progress, cancel):
        self.calls.append((apk, destination, output, source_package)); self.entered.set()
        progress({"phase": "portable-export", "completed": 7, "total": 20, "percent": 35, "detail": "content.zip"})
        while not self.release.wait(.01):
            if cancel(): raise RuntimeError("Export cancelled")
        destination.parent.mkdir(parents=True, exist_ok=True); destination.write_bytes(b"actual result fixture")
        return {"schema": 1, "package": "dev.gloomhavenvr.quest", "path": str(destination),
                "size": destination.stat().st_size, "sha256": "a" * 64, "apk": {}, "content": {}}

    def start(self):
        status, value = self.request("/api/package/export", {"source": str(self.source), "destination": str(self.destination)})
        self.assertEqual(status, 200); self.assertTrue(self.entered.wait(2)); return value

    def wait(self, status):
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            value = self.request("/api/package/status")[1]
            if value["state"]["status"] == status: return value
            time.sleep(.01)
        self.fail("Package state never reached " + status)

    def test_actual_http_progress_and_completed_receipt_survive_server_restart(self):
        self.start(); value = self.request("/api/package/status")[1]
        self.assertEqual(value["state"]["progress"]["percent"], 35)
        self.assertEqual(value["state"]["progress"]["completed"], 7)
        self.release.set(); result = self.wait("complete")
        self.assertEqual(result["state"]["progress"]["percent"], 100)
        self.assertEqual(package_tools.PackageTools(ROOT, self.store.root).latest()["path"], str(self.destination))
        self.destination.write_bytes(b"replaced")
        self.assertIsNone(package_tools.PackageTools(ROOT, self.store.root).latest())
        self.assertEqual(self.calls[0][2], self.store.root / "build")
        self.assertIsNone(self.calls[0][3])

    def test_export_excludes_build_device_cleanup_and_other_server_run_lock(self):
        self.start()
        for route, body in (("/api/package/export", {"source": str(self.source), "destination": str(self.destination)}),
                            ("/api/device/connect", {"host": "", "serial": "", "setupWireless": False}),
                            ("/api/storage/plan", {"mode": "duplicates"})):
            status, result = self.request(route, body)
            self.assertNotEqual(status, 200); self.assertEqual(result["code"], "already_running")
        with self.assertRaises(WizardError):
            with file_lock(self.store.root / "run.lock"): pass

    def test_cancel_retains_original_and_last_result_then_retry_finishes(self):
        self.start(); self.request("/api/package/cancel", {})
        self.wait("cancelled"); self.assertEqual(self.source.read_bytes(), b"owned apk fixture")
        self.http.package_job.join(3)
        self.release.set(); self.start(); self.wait("complete")
        log = (self.store.root / "logs/package-export.jsonl").read_text()
        self.assertIn("export_cancelled", log); self.assertIn("export_complete", log)

    def test_token_and_path_bounds_prevent_unrequested_payload_work(self):
        self.assertNotEqual(self.request("/api/package/status", token=False)[0], 200)
        for body in ({}, {"source": str(self.source), "destination": str(self.destination), "extra": "anything"},
                     {"source": "x" * 32769, "destination": str(self.destination)}):
            self.assertNotEqual(self.request("/api/package/export", body)[0], 200)
        self.assertEqual(self.calls, [])

    def test_default_is_workspace_export_and_metadata_polls_read_no_payload(self):
        with mock.patch.object(discovery, "default_apk", return_value={"path": str(self.source), "modBuild": 671}), \
             mock.patch.object(Path, "read_bytes", side_effect=AssertionError("No payload stream during empty discovery")):
            value = self.request("/api/package/status")[1]
        self.assertEqual(value["defaults"]["source"], str(self.source))
        self.assertEqual(value["defaults"]["destination"], str(self.store.root / "exports/GloomhavenVR-Quest-B671.zip"))

    def test_production_api_exports_and_reexports_real_portable_core_bytes(self):
        path = ROOT / "tests/quest-builder/test_portable_package.py"
        spec = importlib.util.spec_from_file_location("wizard_real_package_fixture", path)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        fixture = module.PortablePackageTests(); fixture.setUp(); self.addCleanup(fixture.doCleanups)
        discovery.builder.return_value.portable_packages = module.portable
        self.source = fixture.ap
        status, _ = self.request("/api/package/export", {"source": str(self.source), "destination": str(self.destination)})
        self.assertEqual(status, 200); first = self.wait("complete")["state"]["result"]
        import zipfile
        with zipfile.ZipFile(self.destination) as archive:
            self.assertEqual(archive.read(module.portable.APK), fixture.ap.read_bytes())
            self.assertEqual(archive.read(module.portable.CONTENT), fixture.bank.read_bytes())
            self.assertFalse(any("keystore" in name or "local-key" in name for name in archive.namelist()))
        target = self.store.root / "exports/second.zip"
        self.request("/api/package/export", {"source": str(self.destination), "destination": str(target)})
        second = self.wait("complete")["state"]["result"]
        self.assertEqual(first["apk"], second["apk"]); self.assertEqual(first["content"], second["content"])
        self.assertEqual(module.portable.inspect(target)["content"]["sha256"], fixture.delivery["sha256"])
        self.assertFalse(any(path.name == module.portable.CONTENT for path in (self.store.root / "build").rglob("*")))


class PackageInputTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name); self.store = Store(self.root / "workspace")
        self.package = self.root / "selected.zip"; self.package.write_bytes(b"metadata fixture")
        package_tools._destination_hints.clear()
        self.addCleanup(package_tools._destination_hints.clear)

    def test_inspect_key_reads_bounded_package_identity_without_extracting_apk(self):
        selected = self.store.create(wizard.choices({"gameRoot": str(self.root / "PC"), "mode": "update-profile",
                                                    "baseApk": str(self.package), "profileName": "Recipient"}))
        inspect = mock.Mock(return_value={"apk": {"sha256": "a" * 64}})
        module = SimpleNamespace(portable_packages=SimpleNamespace(inspect=inspect,
                                import_apk=mock.Mock(side_effect=AssertionError("Key must not copy payload"))))
        with mock.patch.object(discovery, "builder", return_value=module):
            first = wizard.Engine(self.store).key(selected, "inspect")
            inspect.return_value = {"apk": {"sha256": "b" * 64}}
            self.assertNotEqual(first, wizard.Engine(self.store).key(selected, "inspect"))
        self.assertEqual(inspect.call_count, 2)

    def test_package_space_uses_apk_size_not_entire_content_container(self):
        with self.package.open("wb") as stream: stream.truncate(13 * qualification.GIB)
        inspect = mock.Mock(return_value={"apk": {"size": 2 * qualification.GIB}})
        helper = SimpleNamespace(game_data=lambda _: self.root,
                                 portable_packages=SimpleNamespace(inspect=inspect))
        with mock.patch.object(qualification, "space_estimate", side_effect=AssertionError("No game bank walk")):
            result = qualification.qualify(self.store.root, game_root=self.root, mode="update-profile",
                            helper=helper, base_apk=self.package, free_bytes=12 * qualification.GIB)
        self.assertEqual(result["requiredFreeBytes"], 9 * qualification.GIB)
        self.assertTrue(result["buildSpaceCheckPassed"])
        self.assertEqual(result["spaceEstimate"]["apkBytes"], 2 * qualification.GIB)

    def test_original_zip_choice_and_signing_mode_are_preserved_in_arguments(self):
        saved = self.store.create(wizard.choices({"gameRoot": str(self.root / "PC"), "mode": "update-profile",
            "baseApk": str(self.package), "signingMode": "auto"}))
        saved["completed"] = {"source": {"details": {"sourceRoot": str(ROOT)}}, "tools": {"details": {}},
                              "profile": {"details": {"profilePath": str(self.root / "profile.json"), "steamLogo": str(self.root / "logo.png")}}}
        arguments = wizard.Engine(self.store).arguments(saved, "build")
        self.assertEqual(arguments[arguments.index("--base-apk") + 1], str(self.package))
        self.assertEqual(arguments[arguments.index("--signing-mode") + 1], "auto")
        self.assertNotIn("--unity-editor", arguments)
        helper = SimpleNamespace(portable_packages=SimpleNamespace(inspect=lambda _: {"apk": {"sha256": "a" * 64}}))
        with mock.patch.object(discovery, "builder", return_value=helper):
            engine = wizard.Engine(self.store); before = engine.key(saved, "inspect")
            saved["choices"]["signingMode"] = "original"
            self.assertNotEqual(before, engine.key(saved, "inspect"))

    def test_linux_package_open_and_zip_save_do_not_use_shell(self):
        for name in ("zenity", "kdialog"):
            with mock.patch.object(host_paths, "linux_picker", return_value=(name, "/usr/bin/" + name)), \
                 mock.patch.object(host_paths.subprocess, "run", return_value=SimpleNamespace(returncode=0, stdout=str(self.package))) as run:
                self.assertEqual(host_paths.browse_linux("apk"), str(self.package))
                self.assertIn("*.zip", " ".join(run.call_args.args[0]))
                self.assertEqual(host_paths.browse_linux("export"), str(self.package))
                self.assertIn("--save" if name == "zenity" else "--getsavefilename", run.call_args.args[0])
                self.assertNotIn("shell", run.call_args.kwargs)

    def test_updated_workspace_default_preserves_source_export_after_cache_cleanup(self):
        path = ROOT / "tests/quest-builder/test_portable_package.py"
        spec = importlib.util.spec_from_file_location("wizard_closed_update_package_fixture", path)
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        fixture = module.PortablePackageTests(); fixture.setUp(); self.addCleanup(fixture.doCleanups)
        fixture.output = self.store.root / "build"; fixture.output.mkdir()
        protected = self.store.root / "exports/GloomhavenVR-Quest-B671.zip"
        module.portable.export_package(fixture.ap, protected, fixture.output)
        imported = module.portable.import_apk(protected, fixture.output)
        updated = fixture.closed_update("b" * 64, base=imported, package=protected)
        before = protected.read_bytes()
        # The durable closed locator also works after deleting import data.
        shutil.rmtree(fixture.output / "cache"); shutil.rmtree(fixture.output / "imports")
        helper = SimpleNamespace(portable_packages=module.portable)
        adapter = package_tools.PackageTools(ROOT, self.store.root)
        with mock.patch.object(discovery, "builder", return_value=helper), \
             mock.patch.object(discovery, "default_apk", return_value={"path": str(updated), "modBuild": 671}), \
             mock.patch.object(module.portable, "open_content", side_effect=AssertionError("Default selection must not read bank payload")):
            defaults = adapter.defaults(self.store)
            with mock.patch.object(module.portable, "content_source", side_effect=AssertionError("Unchanged UI hint must not repeat locator/cache work")):
                self.assertEqual(package_tools.PackageTools(ROOT, self.store.root).defaults(self.store), defaults)
        destination = protected.with_name("GloomhavenVR-Quest-B671-copy.zip")
        self.assertEqual(defaults["source"], str(updated))
        self.assertEqual(defaults["destination"], str(destination))
        with mock.patch.object(discovery, "builder", return_value=helper):
            result = adapter.export({"source": defaults["source"], "destination": defaults["destination"]},
                                    progress=lambda _: None, cancel=threading.Event())
        self.assertEqual(protected.read_bytes(), before)
        self.assertEqual(result["apk"]["sha256"], module.storage.digest(updated))
        self.assertEqual(result["content"], fixture.delivery)

    def test_default_hint_rechecks_changed_source_and_falls_back_safely_on_transient_lookup(self):
        apk = self.root / "current.apk"; apk.write_bytes(b"current APK fixture")
        destination = self.store.root / "exports/GloomhavenVR-Quest-B671.zip"
        destination.parent.mkdir(); destination.write_bytes(b"existing export fixture")
        source = self.root / "bank.zip"; source.write_bytes(b"first bank fixture")
        locator = mock.Mock(return_value={"path": source})
        helper = SimpleNamespace(portable_packages=SimpleNamespace(content_source=locator))
        with mock.patch.object(discovery, "builder", return_value=helper), \
             mock.patch.object(discovery, "default_apk", return_value={"path": str(apk), "modBuild": 671}):
            first = package_tools.PackageTools(ROOT, self.store.root).defaults(self.store)
            self.assertEqual(first["destination"], str(destination))
            self.assertEqual(package_tools.PackageTools(ROOT, self.store.root).defaults(self.store), first)
            self.assertEqual(locator.call_count, 1)
            source.write_bytes(b"changed bank fixture")
            locator.side_effect = OSError("Cache cleanup is in progress")
            fallback = package_tools.PackageTools(ROOT, self.store.root).defaults(self.store)
            self.assertEqual(fallback["destination"], str(destination.with_name(destination.stem + "-copy.zip")))
            self.assertEqual(locator.call_count, 2)
            self.assertEqual(destination.read_bytes(), b"existing export fixture")


class PackageLogTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.workspace = self.root / "owned"; self.workspace.mkdir()
        self.adapter = package_tools.PackageTools(ROOT, self.workspace)
        self.foreign = self.root / "foreign.jsonl"; self.foreign.write_bytes(b"unrelated retained evidence\n")

    def test_linked_log_rotation_and_parent_refuse_before_foreign_or_owned_bytes_change(self):
        for linked in ("log", "rotation", "parent"):
            with self.subTest(linked=linked):
                workspace = self.workspace / linked; workspace.mkdir()
                adapter = package_tools.PackageTools(ROOT, workspace)
                if linked == "parent":
                    foreign_folder = self.root / "foreign-folder"; foreign_folder.mkdir()
                    marker = foreign_folder / "marker.txt"; marker.write_bytes(b"retained folder evidence")
                    adapter.log.parent.symlink_to(foreign_folder, target_is_directory=True)
                    protected = {entry.name: entry.read_bytes() for entry in foreign_folder.iterdir()}
                else:
                    adapter.log.parent.mkdir()
                    if linked == "log": adapter.log.symlink_to(self.foreign)
                    else:
                        adapter.log.write_bytes(b"x" * (1048576 + 1))
                        adapter.log.with_name("package-export.previous.jsonl").symlink_to(self.foreign)
                        before = adapter.log.read_bytes()
                with self.assertRaises(WizardError) as error:
                    adapter.log_event("export_started", source="selected.apk")
                self.assertEqual(error.exception.code, "linked_path")
                self.assertEqual(self.foreign.read_bytes(), b"unrelated retained evidence\n")
                if linked == "rotation": self.assertEqual(adapter.log.read_bytes(), before)
                if linked == "parent":
                    self.assertEqual({entry.name: entry.read_bytes() for entry in foreign_folder.iterdir()}, protected)

    def test_regular_logs_create_and_rotate_only_the_exact_owned_files(self):
        self.adapter.log_event("export_started", source="selected.apk")
        value = json.loads(self.adapter.log.read_text())
        self.assertEqual(value["event"], "export_started")
        original = b"x" * (1048576 + 1); self.adapter.log.write_bytes(original)
        self.adapter.log_event("export_complete", size=123)
        self.assertEqual(self.adapter.log.with_name("package-export.previous.jsonl").read_bytes(), original)
        self.assertEqual(json.loads(self.adapter.log.read_text())["size"], 123)
        self.assertEqual(self.foreign.read_bytes(), b"unrelated retained evidence\n")


if __name__ == "__main__": unittest.main()
