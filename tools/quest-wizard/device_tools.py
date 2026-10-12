"""Independent Quest connection and bounded runtime captures for the local wizard.

Reuse the installer's identity-checked wireless setup and the standalone collector's
fixed diagnostic allowlist. No APK, content bank, save or profile is transferred.
The HTTP owner supplies job exclusion and cancellation; polling only reads cached
state and never probes ADB or walks the filesystem again.

The maintainer's 2026-10-12 report found silent waits inside the previous4/27
completion counters. Count the collector's actual observation targets and expose
the current command's measured activity without inferring completion from time.
"""
from __future__ import annotations

import copy
from contextlib import contextmanager
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
MAX_PROGRESS_LOG_BYTES = 2 * 1048576
HEARTBEAT_SECONDS = .5


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
        self.started = None
        self.remote_sizes = {}
        self.recorded_progress = None
        self.recorded_at = 0
        self.state_path = ordinary(self.directory / "device-tools-state.json")
        self.progress_log = ordinary(self.directory / "device-tools-progress.jsonl")
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
            self.state["updatedAt"] = time.time()
            if self.started is not None:
                self.state["elapsedSeconds"] = round(time.monotonic() - self.started, 3)
            result = copy.deepcopy(self.state)
            # Keep the current job identifiable in a Builder support export,
            # including jobs that have not reached a command's completion log.
            # Diagnostics must never prevent the actual connection/capture.
            activity = result.get("activity") or {}
            signature = (result.get("status"), result.get("operation"), result.get("log"),
                         result.get("progress", {}).get("done"), activity.get("label"))
            record = signature != self.recorded_progress or time.monotonic() - self.recorded_at >= 1
            try:
                if record:
                    atomic_json(self.state_path, result)
                    ordinary(self.progress_log)
                    if self.progress_log.exists() and self.progress_log.stat().st_size > MAX_PROGRESS_LOG_BYTES:
                        self.progress_log.unlink()
                    event = {key: result[key] for key in ("updatedAt", "elapsedSeconds", "status", "operation", "progress", "activity") if key in result}
                    with self.progress_log.open("a", encoding="utf-8") as stream:
                        stream.write(json.dumps(event, sort_keys=True) + "\n")
                    self.recorded_progress, self.recorded_at = signature, time.monotonic()
            except (OSError, ValueError, WizardError): pass
        if callback: callback(result)
        return result

    def _step(self, progress, done, total, label, **values):
        return self._publish(progress, progress={"done": done, "total": total,
            "percent": 100 if done == total else done / total * 100, "label": label}, activity=None, **values)

    @contextmanager
    def _watch(self, callback, label, observe=None):
        """Show measured activity while a synchronous tool owns its real work.

        Unknown totals remain unknown. Elapsed time is an activity signal, never
        a percentage estimate. Only our private download/pull file and captured
        command output provide byte counts; no APK or game bank is inspected.
        """
        started = time.monotonic()
        stop = threading.Event()
        def pulse():
            activity = {"label": label, "elapsedSeconds": round(time.monotonic() - started, 3)}
            if observe:
                try: activity.update(observe())
                except (OSError, ValueError, WizardError): pass
            self._publish(callback, activity=activity)
        pulse()
        def monitor():
            while not stop.wait(HEARTBEAT_SECONDS): pulse()
        thread = threading.Thread(target=monitor, name="quest-device-activity", daemon=False)
        thread.start()
        try: yield
        finally:
            stop.set(); thread.join()

    @staticmethod
    def _command_label(command):
        args = list(map(str, command[1:]))
        if args[:1] == ["-s"]: args = args[2:]
        if args[:1] == ["devices"]: return "device-discovery"
        if args[:1] == ["connect"]: return "wifi-connect"
        if args[:1] == ["tcpip"]: return "wifi-setup"
        if args[:1] == ["pull"]: return "log-transfer"
        if args[:2] == ["shell", "sha256sum"]: return "installed-apk-hash"
        if args[:2] == ["shell", "getprop"]: return "device-properties"
        if args[:2] == ["shell", "logcat"]: return "logcat-history" if "-s" in args else "logcat"
        if args[:2] == ["shell", "dumpsys"] or args[:2] == ["shell", "pm"]: return "installed-package"
        if "head" in args: return "log-transfer"
        if "stat" in args: return "log-metadata"
        return "quest-command"

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

    def _runner(self, cancel, progress=None):
        """Terminate and reap a current ADB child when the browser cancels a job."""
        def run(command, **options):
            self._cancel(cancel)
            args = list(map(str, command[1:]))
            if args[:1] == ["-s"]: args = args[2:]
            observation = {"command": self._command_label(command), "receivedBytes": 0}
            if args[:1] == ["pull"]: observation["detail"] = Path(args[1]).name
            elif "head" in args or "stat" in args or args[:2] == ["shell", "sha256sum"]:
                observation["detail"] = Path(args[-1]).name
            elif args[:2] == ["shell", "getprop"] and len(args) > 2:
                observation["detail"] = args[2]
            pull = Path(args[2]) if args[:1] == ["pull"] and len(args) == 3 else None
            expected = self.remote_sizes.get(args[1]) if pull else None
            if "head" in args and "-c" in args:
                try: expected = int(args[args.index("-c") + 1])
                except (ValueError, IndexError): pass
            # The collector bounds every log target. Do not accidentally turn a
            # future unrelated ADB command into an unbounded filesystem probe.
            if type(expected) is not int or not 0 < expected <= MAX_CAPTURE_BYTES: expected = None
            def observed():
                value = copy.deepcopy(observation)
                if pull:
                    ordinary(pull)
                    value["receivedBytes"] = min(MAX_CAPTURE_BYTES, pull.stat().st_size) if pull.is_file() else 0
                if expected:
                    value["totalBytes"] = expected
                    value["percent"] = min(99, value["receivedBytes"] / expected * 100)
                return value
            def complete(result):
                if args[:4] == ["shell", "stat", "-c", "%s"] and result.returncode == 0:
                    output = result.stdout.decode("utf-8", errors="replace") if isinstance(result.stdout, bytes) else (result.stdout or "")
                    if re.fullmatch(r"\s*\d{1,19}\s*", output): self.remote_sizes[args[-1]] = int(output.strip())
                return result
            if self.runner is not None:
                with self._watch(progress, self._command_label(command), observed):
                    return complete(self.runner(command, **options))
            timeout = options.pop("timeout", 20)
            capture = options.pop("capture_output", False)
            payload = options.pop("input", None)
            if capture: options.update(stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            if payload is not None: options["stdin"] = subprocess.PIPE
            started = time.monotonic()
            process = subprocess.Popen(command, **options)
            try:
                with self._watch(progress, self._command_label(command), observed):
                    first = True
                    while True:
                        self._cancel(cancel)
                        remaining = timeout - (time.monotonic() - started)
                        if remaining <= 0: raise subprocess.TimeoutExpired(command, timeout)
                        try:
                            stdout, stderr = process.communicate(input=payload if first else None, timeout=min(.2, remaining))
                            return complete(subprocess.CompletedProcess(command, process.returncode, stdout, stderr))
                        except subprocess.TimeoutExpired as waiting:
                            first = False
                            partial = waiting.stdout or b""
                            observation["receivedBytes"] = len(partial.encode("utf-8") if isinstance(partial, str) else partial)
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
        self.started = time.monotonic()
        self.remote_sizes = {}
        self.total = 4 if operation == "connect" else None
        self._publish(progress, progress={"done": 0, "total": self.total, "percent": 0, "label": "adb-setup"}, activity=None,
            status="connecting", operation=operation, capture=None, startedAt=time.time(), log=None,
            message={"en": "Preparing Android platform-tools; missing tools are downloaded automatically.",
                     "de": "Android platform-tools vorbereiten; fehlende Werkzeuge werden automatisch heruntergeladen."})
        installer, collector = _modules(self.repo)
        self.targets = self._capture_targets(collector)
        if operation == "capture":
            self.total = len(self.targets)
            self._step(progress, 0, self.total, "adb-setup")
        config = self._config()
        def download_observation():
            cache = self.repo / "scripts/.quest-adb"
            ordinary(cache)
            if not cache.is_dir(): return {}
            # The bootstrap creates a random private *.partial owner. Observe
            # only the current download; never walk a user's SDK or game files.
            files = [path for path in cache.glob("download-*.partial") if path.is_file()]
            if len(files) != 1: return {}
            ordinary(files[0])
            if files[0].stat().st_mtime < self.state["startedAt"]: return {}
            spec = importlib.util.spec_from_file_location("_quest_device_download_limits", self.repo / "tools/quest-installer/adb_bootstrap.py")
            bootstrap = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(bootstrap)
            total = bootstrap.BYTES if sys.platform == "win32" else bootstrap.LINUX["bytes"] if sys.platform == "linux" else None
            value = {"label": "adb-download", "receivedBytes": files[0].stat().st_size}
            if total: value.update(totalBytes=total, percent=min(99, value["receivedBytes"] / total * 100))
            return value
        with self._watch(progress, "adb-setup", download_observation):
            executable = installer.adb_path(None, config.get("adb"))
        self._cancel(cancel)
        log = ordinary(self.directory / ("device-tools-" + uuid.uuid4().hex + ".log"))
        self._step(progress, 1, self.total, "device-discovery", log=str(log))
        adb = installer.Adb(executable, log, self._runner(cancel, progress))
        devices = adb.devices()
        rows = [{"serial": serial, "state": state,
                 "kind": "wifi" if ":" in serial or serial.startswith("adb-") else "usb",
                 "model": None} for serial, state in sorted(devices.items())]
        self._step(progress, 2, self.total, "quest-connect", devices=rows)
        return installer, collector, config, adb

    @staticmethod
    def _capture_targets(collector):
        # The capture plan counts actual observations, not variable fallback
        # commands. All installed APK hashes form one explicit identity task.
        files = tuple(collector.APP_FILES)
        procedural = tuple(collector.PROCEDURAL_APP_FILES)
        delivery = tuple(collector.DELIVERY_FILES)
        return ("adb-setup", "device-discovery", "quest-select",
            "ro.product.model", "ro.product.name", "ro.build.version.release", "ro.build.version.incremental", "ro.build.fingerprint",
            "process", "logcat", "logcat-history", "package", "installed-apks") + files + procedural + tuple("delivery:" + name for name in delivery) + (
            "metadata-summary", "capture-archive", "capture-receipt")

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
        archive = None
        try:
            installer, collector, config, adb = self._begin("capture", progress, cancel)
            if selected["host"]: selected["host"] = installer.endpoint(selected["host"])
            serial, hardware, transport = collector.choose_capture_device(adb, SimpleNamespace(**selected), config)
            self._cancel(cancel)
            self._step(progress, 3, self.total, "runtime-logs", status="capturing", serial=serial, hardwareSerial=hardware,
                message={"en": "Collecting bounded runtime logs. APKs, game assets and savegames are not copied.",
                         "de": "Begrenzte Laufzeit-Logs erfassen. APKs, Spielassets und Spielstände werden nicht kopiert."})
            identity = uuid.uuid4().hex
            stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + identity[:8]
            archive = ordinary(self.captures / ("quest-capture-" + stamp + ".zip"))
            completed = set(self.targets[:3])
            owner = self
            def advance(label):
                owner._cancel(cancel)
                if label not in self.targets: raise ValueError("Unknown capture progress target")
                if label in completed: return
                completed.add(label)
                owner._step(progress, len(completed), self.total, label)
            class ProgressCapture(collector.Capture):
                def read(self, *arguments, **keywords):
                    owner._cancel(cancel)
                    result = super().read(*arguments, **keywords)
                    # Top-level fixed metadata observations, distinct from a
                    # log's internal transfer/fallback commands.
                    if arguments[:2] == ("shell", "getprop"): advance(arguments[2])
                    elif arguments[:2] == ("shell", "pidof"): advance("process")
                    elif arguments[:2] == ("shell", "logcat"): advance("logcat-history" if "-s" in arguments else "logcat")
                    elif arguments[:2] == ("shell", "dumpsys"): advance("package")
                    elif (arguments[:2] == ("shell", "stat") and arguments[-1] in
                            tuple(collector.REMOTE_FILES + "/" + item for item in collector.DELIVERY_FILES)):
                        advance("delivery:" + arguments[-1][len(collector.REMOTE_FILES) + 1:])
                    return result
                def app_file(self, name):
                    advance("installed-apks")
                    owner._step(progress, len(completed), owner.total, name)
                    super().app_file(name); advance(name)
                def procedural_file(self, name):
                    owner._step(progress, len(completed), owner.total, name)
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
                advance("metadata-summary")
                self._step(progress, len(completed), self.total, "capture-archive")
                self._cancel(cancel)
                files = sorted(path for path in directory.iterdir() if path.is_file())
                total_bytes = sum(path.stat().st_size for path in files)
                archive_bytes = 0
                with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as zipped:
                    for path in files:
                        with path.open("rb") as source, zipped.open(path.name, "w") as target:
                            for block in iter(lambda: source.read(1048576), b""):
                                self._cancel(cancel)
                                target.write(block); archive_bytes += len(block)
                                self._publish(progress, activity={"label": "capture-archive", "detail": path.name,
                                    "receivedBytes": archive_bytes, "totalBytes": total_bytes,
                                    "percent": min(99, archive_bytes / total_bytes * 100) if total_bytes else 0})
                if archive.stat().st_size > MAX_CAPTURE_BYTES:
                    archive.unlink(); raise WizardError("capture_size", "The runtime capture exceeded its size limit.")
                advance("capture-archive")
            if completed != set(self.targets) - {"capture-receipt"}:
                raise ValueError("The capture progress plan has unfinished observations")
            receipt = {"schema": 1, "id": identity, "name": archive.name, "bytes": archive.stat().st_size}
            atomic_json(ordinary(self.captures / (identity + ".json")), receipt)
            message = {"en": "Runtime capture saved. Download the ZIP to attach it to a bug report.",
                       "de": "Laufzeit-Capture gespeichert. Die ZIP herunterladen und dem Fehlerbericht beilegen."}
            if not usable: message = {"en": "Capture saved, but no readable runtime logs were available. Start the Quest app and collect again.",
                                     "de": "Capture gespeichert, aber keine lesbaren Laufzeit-Logs verfügbar. Quest-App starten und erneut erfassen."}
            return self._step(progress, self.total, self.total, "capture-complete", status="complete", message=message,
                capture={"id": identity, "name": archive.name, "path": str(archive), "status": manifest["status"]})
        except (Cancelled, WizardError, OSError, ValueError, RuntimeError) as error:
            if archive is not None:
                try: archive.unlink(missing_ok=True)
                except OSError: pass
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
