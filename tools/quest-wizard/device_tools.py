"""Independent Quest connection and bounded runtime captures for the local wizard.

Reuse the installer's identity-checked wireless setup and the standalone collector's
fixed diagnostic allowlist. No APK, content bank, save or profile is transferred.
The HTTP owner supplies job exclusion and cancellation; polling only reads cached
state and never probes ADB or walks the filesystem again.
"""
from __future__ import annotations

import copy
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import threading
import time
from types import SimpleNamespace
import uuid
import zipfile

import discovery
from state import Cancelled, WizardError, atomic_json, ordinary

CAPTURE_ID = re.compile(r"[0-9a-f]{32}\Z")
CAPTURE_NAME = re.compile(r"quest-capture-[0-9]{8}T[0-9]{6}Z-[0-9a-f]{8}\.zip\Z")
MAX_CAPTURE_BYTES = 40 * 1048576
MAX_STATE_BYTES = 128 * 1024


def _modules(repo):
    """Bind the real collector to its installer without polluting other imports."""
    with discovery._LOADER_LOCK:
        missing = object()
        names = ("_quest_wizard_device_installer", "installer", "_quest_wizard_device_collector")
        prior = {name: sys.modules.get(name, missing) for name in names}
        try:
            def load(name, filename):
                spec = importlib.util.spec_from_file_location(name, repo / "tools/quest-installer" / filename)
                module = importlib.util.module_from_spec(spec)
                sys.modules[name] = module
                spec.loader.exec_module(module)
                return module
            installer = load(names[0], "installer.py")
            sys.modules["installer"] = installer
            collector = load(names[2], "collector.py")
            return installer, collector
        finally:
            for name, value in prior.items():
                if value is missing: sys.modules.pop(name, None)
                else: sys.modules[name] = value


def _options(value, capture=False):
    allowed = {"host", "serial"} | (set() if capture else {"setupWireless"})
    if not isinstance(value, dict) or set(value) - allowed:
        raise WizardError("device_options", "Unsupported Quest connection options.", "Ungültige Quest-Verbindungsoptionen.")
    for key in ("host", "serial"):
        if key in value and (not isinstance(value[key], str) or len(value[key]) > 256):
            raise WizardError("device_options", "Invalid Quest address or serial.", "Ungültige Quest-Adresse oder Seriennummer.")
    if "setupWireless" in value and type(value["setupWireless"]) is not bool:
        raise WizardError("device_options", "Wireless setup must be a yes/no choice.", "WLAN-Einrichtung muss eine Ja/Nein-Auswahl sein.")
    serial = value.get("serial", "").strip()
    if serial and not re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", serial):
        raise WizardError("device_options", "Invalid Quest serial.", "Ungültige Quest-Seriennummer.")
    return {"host": value.get("host", "").strip() or None,
            "serial": serial or None, "setup": value.get("setupWireless", False)}


class DeviceTools:
    def __init__(self, repo, state_root, runner=None):
        self.repo, self.root = Path(repo), ordinary(state_root)
        self.directory = ordinary(self.root / "device")
        self.directory.mkdir(parents=True, exist_ok=True)
        self.config = ordinary(self.directory / "wireless-install.json")
        self.captures = ordinary(self.directory / "captures")
        self.captures.mkdir(exist_ok=True)
        self.runner = runner
        self.lock = threading.RLock()
        self.state = {"status": "idle", "operation": None, "devices": [], "endpoint": None,
                      "serial": None, "message": {"en": "Connect an authorized Quest to collect runtime logs.",
                      "de": "Eine autorisierte Quest verbinden, um Laufzeit-Logs zu speichern."},
                      "progress": {"done": 0, "total": 1, "percent": 0, "label": "idle"}}
        # A remembered endpoint is only presentation here. The actual operation
        # still verifies hardware identity through the shared installer.
        try:
            config = self._config()
            self.state["endpoint"] = config.get("endpoint")
            self.state["hardwareSerial"] = config.get("deviceSerial")
        except (OSError, ValueError, WizardError): pass

    def status(self):
        with self.lock: return copy.deepcopy(self.state)

    def _publish(self, callback=None, **values):
        with self.lock:
            self.state.update(values)
            result = copy.deepcopy(self.state)
        if callback: callback(result)
        return result

    def _step(self, progress, done, total, label, **values):
        return self._publish(progress, progress={"done": done, "total": total,
            "percent": 100 if done == total else done / total * 100, "label": label}, **values)

    def _config(self):
        ordinary(self.config)
        if not self.config.exists(): return {"schema": 1}
        if self.config.stat().st_size > MAX_STATE_BYTES:
            raise WizardError("device_config", "Remembered Quest settings are too large.", "Gespeicherte Quest-Einstellungen sind zu groß.")
        value = json.loads(self.config.read_text(encoding="utf-8-sig"))
        if not isinstance(value, dict) or value.get("schema") != 1:
            raise WizardError("device_config", "Remembered Quest settings are invalid.", "Gespeicherte Quest-Einstellungen sind ungültig.")
        return value

    @staticmethod
    def _cancel(cancel):
        if cancel is not None and cancel.is_set(): raise Cancelled()

    def _runner(self, cancel):
        """Terminate and reap a current ADB child when the browser cancels a job."""
        def run(command, **options):
            self._cancel(cancel)
            if self.runner is not None: return self.runner(command, **options)
            timeout = options.pop("timeout", 20)
            capture = options.pop("capture_output", False)
            payload = options.pop("input", None)
            if capture: options.update(stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            if payload is not None: options["stdin"] = subprocess.PIPE
            started = time.monotonic()
            process = subprocess.Popen(command, **options)
            try:
                first = True
                while True:
                    self._cancel(cancel)
                    remaining = timeout - (time.monotonic() - started)
                    if remaining <= 0: raise subprocess.TimeoutExpired(command, timeout)
                    try:
                        stdout, stderr = process.communicate(input=payload if first else None, timeout=min(.2, remaining))
                        return subprocess.CompletedProcess(command, process.returncode, stdout, stderr)
                    except subprocess.TimeoutExpired:
                        first = False
            except BaseException:
                if process.poll() is None:
                    process.terminate()
                    try: process.communicate(timeout=2)
                    except subprocess.TimeoutExpired:
                        process.kill(); process.communicate()
                raise
        return run

    def _begin(self, operation, progress, cancel):
        self._cancel(cancel)
        self._step(progress, 0, 4 if operation == "connect" else 27, "adb-setup",
            status="connecting", operation=operation, capture=None,
            message={"en": "Preparing Android platform-tools; missing tools are downloaded automatically.",
                     "de": "Android platform-tools vorbereiten; fehlende Werkzeuge werden automatisch heruntergeladen."})
        installer, collector = _modules(self.repo)
        config = self._config()
        executable = installer.adb_path(None, config.get("adb"))
        self._cancel(cancel)
        log = ordinary(self.directory / ("device-tools-" + uuid.uuid4().hex + ".log"))
        adb = installer.Adb(executable, log, self._runner(cancel))
        devices = adb.devices()
        rows = [{"serial": serial, "state": state,
                 "kind": "wifi" if ":" in serial or serial.startswith("adb-") else "usb",
                 "model": None} for serial, state in sorted(devices.items())]
        self._step(progress, 1, 4 if operation == "connect" else 27, "device-discovery", devices=rows)
        return installer, collector, config, adb

    def _failure(self, error, progress):
        if isinstance(error, Cancelled):
            return self._publish(progress, status="idle", message=error.message)
        # Missing authorization/network is an actionable wait, not a lost build.
        detail = str(error)[:1400]
        return self._publish(progress, status="waiting", message={
            "en": "Quest connection needs attention. Unlock it, enable developer mode, connect USB and accept USB debugging. Then retry. " + detail,
            "de": "Quest-Verbindung benötigt eine Aktion. Quest entsperren, Entwicklermodus aktivieren, USB anschließen und USB-Debugging bestätigen. Danach erneut versuchen. " + detail})

    def connect(self, options, progress=None, cancel=None):
        selected = _options(options)
        try:
            installer, _, config, adb = self._begin("connect", progress, cancel)
            if selected["serial"] and ":" in selected["serial"] and not selected["setup"]:
                # The device picker can contain an already connected Wi-Fi
                # serial. The shared installer reserves --serial for USB;
                # qualify that selection through its normal --host path.
                if selected["host"] and installer.endpoint(selected["host"]) != installer.endpoint(selected["serial"]):
                    raise WizardError("device_selection", "The selected Quest and address differ.", "Die ausgewählte Quest und Adresse unterscheiden sich.")
                selected["host"] = selected["serial"]; selected["serial"] = None
            if selected["host"]: selected["host"] = installer.endpoint(selected["host"])
            self._step(progress, 2, 4, "quest-connect", message={
                "en": "Checking USB first, then authorized Wi-Fi. Unlock the Quest and accept USB debugging.",
                "de": "Zuerst USB, danach freigegebenes WLAN prüfen. Quest entsperren und USB-Debugging bestätigen."})
            address, hardware = installer.choose_device(adb, SimpleNamespace(**selected), config)
            self._cancel(cancel)
            self._step(progress, 3, 4, "identity-confirmed", endpoint=address, serial=address, hardwareSerial=hardware)
            config = installer.connection_settings(config, address, hardware, adb.executable)
            transport = config["transport"]
            self._publish(progress, transport=transport)
            atomic_json(self.config, config)
            return self._step(progress, 4, 4, "connected", status="connected", message={
                "en": "Quest connected over " + ("USB" if transport == "usb" else "Wi-Fi") + ". The verified headset identity is remembered.",
                "de": "Quest über " + ("USB" if transport == "usb" else "WLAN") + " verbunden. Die geprüfte Headset-Identität ist gespeichert."})
        except (Cancelled, WizardError, OSError, ValueError, RuntimeError) as error:
            return self._failure(error, progress)

    def capture(self, options, progress=None, cancel=None):
        selected = _options(options, capture=True)
        try:
            installer, collector, config, adb = self._begin("capture", progress, cancel)
            if selected["host"]: selected["host"] = installer.endpoint(selected["host"])
            serial, hardware, transport = collector.choose_capture_device(adb, SimpleNamespace(**selected), config)
            self._cancel(cancel)
            self._step(progress, 2, 27, "runtime-logs", status="capturing", serial=serial, hardwareSerial=hardware,
                message={"en": "Collecting bounded runtime logs. APKs, game assets and savegames are not copied.",
                         "de": "Begrenzte Laufzeit-Logs erfassen. APKs, Spielassets und Spielstände werden nicht kopiert."})
            identity = uuid.uuid4().hex
            stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + identity[:8]
            archive = ordinary(self.captures / ("quest-capture-" + stamp + ".zip"))
            counter = {"done": 2}
            owner = self
            def advance(label):
                owner._cancel(cancel)
                counter["done"] += 1
                owner._step(progress, min(26, counter["done"]), 27, label)
            class ProgressCapture(collector.Capture):
                def read(self, *arguments, **keywords):
                    owner._cancel(cancel)
                    result = super().read(*arguments, **keywords)
                    # Top-level fixed metadata observations, distinct from a
                    # log's internal transfer/fallback commands.
                    if (arguments[:2] == ("shell", "getprop") or arguments[:2] in
                            (("shell", "pidof"), ("shell", "logcat"), ("shell", "dumpsys"), ("shell", "pm"))):
                        advance(" ".join(map(str, arguments[:3])))
                    elif (arguments[:2] == ("shell", "stat") and arguments[-1] in
                            tuple(collector.REMOTE_FILES + "/" + item for item in collector.DELIVERY_FILES)):
                        advance("delivery-metadata")
                    return result
                def app_file(self, name):
                    owner._step(progress, min(25, counter["done"]), 27, name)
                    super().app_file(name); advance(name)
                def procedural_file(self, name):
                    owner._step(progress, min(25, counter["done"]), 27, name)
                    super().procedural_file(name); advance(name)
            with tempfile.TemporaryDirectory(prefix="capture-", dir=self.captures) as temporary:
                directory = Path(temporary)
                manifest = {"schema": 1, "capturedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
                    "device": {"adbSerial": serial, "hardwareSerial": hardware, "transport": transport},
                    "limits": {"appFileBytes": collector.MAX_FILE, "logcatBytes": collector.MAX_LOGCAT,
                        "appHistoryBytes": collector.MAX_HISTORY, "proceduralFileBytes": collector.MAX_FILE},
                    "files": [], "errors": []}
                capture = ProgressCapture(adb, serial, directory, manifest)
                collector.collect(capture, self.config)
                usable = any(row["bytes"] and row["kind"] in ("recent-main-crash-logcat", "owned-app-logcat-history",
                    "app-file-pull", "app-file-run-as", "procedural-file-run-as") for row in manifest["files"])
                manifest["status"] = "partial" if manifest["errors"] else "complete"
                if not usable: manifest["status"] = "no-readable-logs"
                installer.write_json(directory / "manifest.json", manifest)
                # Preserve the shared ADB command evidence inside this capture.
                log = directory / "adb-commands.log"
                log.write_bytes(adb.log.read_bytes()[:4 * 1048576])
                self._step(progress, 26, 27, "capture-archive")
                self._cancel(cancel)
                with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as zipped:
                    for path in sorted(directory.iterdir()):
                        if path.is_file(): zipped.write(path, path.name)
                if archive.stat().st_size > MAX_CAPTURE_BYTES:
                    archive.unlink(); raise WizardError("capture_size", "The runtime capture exceeded its size limit.")
            receipt = {"schema": 1, "id": identity, "name": archive.name, "bytes": archive.stat().st_size}
            atomic_json(ordinary(self.captures / (identity + ".json")), receipt)
            message = {"en": "Runtime capture saved. Download the ZIP to attach it to a bug report.",
                       "de": "Laufzeit-Capture gespeichert. Die ZIP herunterladen und dem Fehlerbericht beilegen."}
            if not usable: message = {"en": "Capture saved, but no readable runtime logs were available. Start the Quest app and collect again.",
                                     "de": "Capture gespeichert, aber keine lesbaren Laufzeit-Logs verfügbar. Quest-App starten und erneut erfassen."}
            return self._step(progress, 27, 27, "capture-complete", status="complete", message=message,
                capture={"id": identity, "name": archive.name, "path": str(archive), "status": manifest["status"]})
        except (Cancelled, WizardError, OSError, ValueError, RuntimeError) as error:
            return self._failure(error, progress)

    def download(self, identity):
        if not isinstance(identity, str) or not CAPTURE_ID.fullmatch(identity):
            raise WizardError("capture_id", "Invalid capture selection.", "Ungültige Capture-Auswahl.")
        try:
            receipt = ordinary(self.captures / (identity + ".json"))
            if receipt.stat().st_size > MAX_STATE_BYTES: raise ValueError("oversized receipt")
            value = json.loads(receipt.read_text(encoding="utf-8"))
            if (value.get("schema") != 1 or value.get("id") != identity or not CAPTURE_NAME.fullmatch(value.get("name", ""))
                    or type(value.get("bytes")) is not int or not 0 < value["bytes"] <= MAX_CAPTURE_BYTES):
                raise ValueError("invalid receipt")
            archive = ordinary(self.captures / value["name"])
            if not archive.is_file() or archive.stat().st_size != value["bytes"]: raise ValueError("capture changed")
            # Small central-directory qualification; no multi-gigabyte game hash.
            with zipfile.ZipFile(archive) as zipped:
                if "manifest.json" not in zipped.namelist(): raise ValueError("manifest missing")
                if any(Path(name).name != name for name in zipped.namelist()): raise ValueError("unsafe capture names")
            return archive
        except (OSError, ValueError, TypeError, KeyError, AttributeError, zipfile.BadZipFile) as error:
            raise WizardError("capture_unavailable", "The selected runtime capture is unavailable. Collect logs again.",
                              "Das ausgewählte Laufzeit-Capture ist nicht verfügbar. Logs erneut erfassen.") from error
