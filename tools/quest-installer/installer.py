"""Install one verified local Quest APK over remembered, identity-checked Wi-Fi ADB.

Only a successful run changes remembered settings. USB is needed once to enable
the headset's TCP transport, and again after a reboot or changed Wi-Fi address.
Windows provisions official Android platform-tools locally if ADB is missing.
No APKs, accounts or signing material are downloaded.
"""
from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
import importlib.util
import ipaddress
import json
import os
from pathlib import Path, PureWindowsPath
import re
import shutil
import subprocess
import sys
import time
import uuid
import zipfile

REPO = Path(__file__).resolve().parents[2]
PACKAGE = "dev.gloomhavenvr.quest"
ACTIVITY = PACKAGE + "/com.unity3d.player.UnityPlayerActivity"
HELP = "Unlock the Quest, enable developer mode, connect USB and accept its USB debugging prompt."


class InstallError(RuntimeError):
    """A source or device is unsafe or not ready for this operation."""


def digest(path):
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def read_json(path):
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict) or value.get("schema") != 1:
        raise InstallError("Unsupported JSON schema: " + str(path))
    return value


def builder_module():
    # The builder's historical profile/storage import names must neither consume
    # nor replace an application's existing stdlib profile or cached test module.
    missing = object()
    previous = {name: sys.modules.get(name, missing) for name in ("profile", "storage")}
    try:
        for name in ("profile", "storage", "builder"):
            spec = importlib.util.spec_from_file_location(
                "_ghvr_wireless_" + name, REPO / "tools/quest-builder" / (name + ".py"))
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            if name == "builder":
                return module
            sys.modules[name] = module
    finally:
        for name, value in previous.items():
            if value is missing:
                sys.modules.pop(name, None)
            else:
                sys.modules[name] = value


@dataclass(frozen=True)
class Source:
    kind: str
    path: Path
    apk: Path
    sha256: str
    evidence: str
    diagnostic: bool
    dummy: bool

    def setting(self):
        return {"kind": self.kind, "path": str(self.path)}


def resolve_source(kind, path, check_zip=True):
    path = Path(path).expanduser().resolve()
    details = {}
    manifest = None
    if kind == "output-root":
        manifest = path / "latest-build.json"
        evidence = digest(manifest)
        apk, details = builder_module().verified_latest_build(path)
        apk = apk.resolve()
    elif kind == "handoff":
        manifest = path
        evidence = digest(path)
        value = read_json(path)
        # The actual private hardware handoff is flat; older wrapped receipts
        # remain readable without dropping the expected APK hash or package.
        details = value.get("details", value)
        relative = Path(value["apk"])
        if relative.is_absolute() or PureWindowsPath(value["apk"]).is_absolute():
            raise InstallError("The handoff must identify a relative APK inside its containing directory.")
        apk = (path.parent / relative).resolve()
        if path.parent not in apk.parents:
            raise InstallError("The handoff APK escapes its containing directory.")
        if not isinstance(details, dict) or details.get("package") != PACKAGE:
            raise InstallError("The handoff package is not " + PACKAGE)
        report = details.get("buildReport", {})
        if not isinstance(report, dict) or (report and report.get("buildResult") != "Succeeded"):
            raise InstallError("The handoff does not describe a successful Android build.")
    elif kind == "apk":
        apk, evidence = path, "manual-local-file"
    else:
        raise InstallError("Unsupported remembered APK source; select --handoff, --output-root or --apk.")
    if not apk.is_file() or apk.suffix.lower() != ".apk":
        raise InstallError("A local .apk file is required: " + str(apk))
    if kind != "apk" and details.get("package") != PACKAGE:
        raise InstallError("The selected build package is not " + PACKAGE)
    actual = digest(apk)
    if kind != "apk" and (not re.fullmatch(r"[0-9a-f]{64}", str(details.get("apkSha256", "")))
                              or actual != details["apkSha256"]):
        raise InstallError("APK SHA-256 differs from its build evidence; restore or rebuild it.")
    if manifest and digest(manifest) != evidence:
        raise InstallError("The selected build evidence changed while being read; retry.")
    if check_zip:
        with zipfile.ZipFile(apk) as archive:
            if "AndroidManifest.xml" not in archive.namelist() or archive.testzip():
                raise InstallError("The APK ZIP payload is incomplete or corrupt.")
    return Source(kind, path, apk, actual, evidence,
                  bool(details.get("isDiagnostic")), bool(details.get("isDummy")))


def select_source(args, config):
    for kind in ("output-root", "handoff", "apk"):
        explicit = getattr(args, kind.replace("-", "_"))
        if explicit:
            return resolve_source(kind, explicit)
    remembered = config.get("source")
    if remembered:
        return resolve_source(remembered["kind"], remembered["path"])
    output = REPO / ".planning/quest3-local"
    if (output / "latest-build.json").is_file():
        return resolve_source("output-root", output)
    handoff = REPO / ".planning/debug/quest3/handoff.json"
    if handoff.is_file():
        return resolve_source("handoff", handoff)
    raise InstallError("No local Quest build found. Supply --handoff, --output-root or --apk.")


def managed_windows_adb():
    spec = importlib.util.spec_from_file_location(
        "_ghvr_quest_adb_bootstrap", REPO / "tools/quest-installer/adb_bootstrap.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    try:
        return module.ensure_windows_adb(REPO / "scripts")
    except module.AdbBootstrapError as error:
        raise InstallError(str(error)) from error


def adb_path(explicit, remembered=None):
    if explicit:
        candidates = [explicit]
    else:
        candidates = [remembered, shutil.which("adb")]
        for variable in ("ANDROID_HOME", "ANDROID_SDK_ROOT", "SDK_ROOT"):
            if os.environ.get(variable):
                candidates.append(Path(os.environ[variable]) / "platform-tools/adb.exe")
                candidates.append(Path(os.environ[variable]) / "platform-tools/adb")
        local = Path(os.environ.get("LOCALAPPDATA", Path.home() / "AppData/Local"))
        roaming = Path(os.environ.get("APPDATA", Path.home() / "AppData/Roaming"))
        candidates += [local / "Android/Sdk/platform-tools/adb.exe",
                       roaming / "SideQuest/platform-tools/adb.exe",
                       roaming / "SideQuest/platform-tools/adb",
                       local / "SideQuest/platform-tools/adb.exe",
                       local / "Programs/SideQuest/resources/app.asar.unpacked/build/platform-tools/adb.exe"]
    for value in candidates:
        if value and Path(value).expanduser().is_file():
            # Check the managed lexical path before resolving links; a moved
            # or linked cache must go through ownership/integrity validation.
            candidate = Path(os.path.abspath(Path(value).expanduser()))
            if sys.platform == "win32":
                try:
                    candidate.relative_to(REPO / "scripts/.quest-adb")
                except ValueError:
                    pass
                else:
                    return managed_windows_adb()
            return candidate.resolve()
    if sys.platform == "win32" and not explicit:
        return managed_windows_adb()
    if explicit:
        raise InstallError("The supplied ADB executable was not found: " + str(explicit))
    raise InstallError("ADB was not found. Install Android platform-tools or supply --adb PATH.")


def endpoint(value):
    # IPv4 and hostnames deliberately exclude shell syntax, IPv6 and ADB service
    # discovery strings. Commands use argument arrays, never a shell.
    match = re.fullmatch(r"([A-Za-z0-9][A-Za-z0-9.-]*)(?::([0-9]+))?", value)
    if not match or not 1 <= int(match.group(2) or 5555) <= 65535:
        raise InstallError("--host must be an IPv4 address or hostname, optionally followed by :PORT.")
    return match.group(1) + ":" + str(int(match.group(2) or 5555))


class Adb:
    def __init__(self, executable, log, runner=None):
        self.executable, self.log = str(executable), log
        self.runner = runner or subprocess.run

    def run(self, *args, timeout=20, positive=None, check_text=True):
        command = [self.executable, *map(str, args)]
        with self.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(command) + "\n")
        try:
            result = self.runner(command, capture_output=True, text=True, timeout=timeout,
                                 encoding="utf-8", errors="replace", shell=False)
        except subprocess.TimeoutExpired as error:
            raise InstallError("ADB timed out; check the headset and Wi-Fi. No data was removed.") from error
        raw = (result.stdout or "") + "\n" + (result.stderr or "")
        with self.log.open("a", encoding="utf-8") as stream:
            stream.write(raw + "\n")
        if "INSTALL_FAILED_UPDATE_INCOMPATIBLE" in raw:
            raise InstallError("Signing-key mismatch. Use an APK built with the installed app's original key. "
                               "The existing app and its data were retained; the installer will not uninstall it.")
        if (result.returncode or (check_text and re.search(r"\b(error|failure|failed|unauthorized|cannot|unable)\b", raw, re.I))
                or (positive and not re.search(positive, raw, re.I | re.M))):
            raise InstallError("ADB failed: " + raw.strip()[:1200])
        return raw.strip()

    def devices(self):
        # Unauthorized/offline entries are observations, not command failures.
        lines = self.run("devices", "-l", check_text=False).splitlines()
        return {parts[0]: parts[1] for line in lines
                if len(parts := line.split()) >= 2 and parts[1] in ("device", "offline", "unauthorized")}

    def identity(self, serial):
        model = self.run("-s", serial, "shell", "getprop", "ro.product.model")
        product = self.run("-s", serial, "shell", "getprop", "ro.product.name")
        if "quest" not in model.lower() and "quest" not in product.lower():
            raise InstallError("Selected device is not a recognized Meta Quest (model=" + model + ").")
        hardware = self.run("-s", serial, "shell", "getprop", "ro.serialno")
        if not re.fullmatch(r"[A-Za-z0-9_-]+", hardware) or hardware.lower() in ("unknown", "null"):
            raise InstallError("The Quest did not expose a stable ro.serialno; installation was stopped.")
        return hardware

    def connect(self, address, expected=None):
        self.run("connect", address, positive=r"\b(?:already )?connected to\s+" + re.escape(address))
        if self.devices().get(address) != "device":
            raise InstallError("The Wi-Fi Quest is offline or unauthorized. " + HELP)
        hardware = self.identity(address)
        if expected and hardware != expected:
            raise InstallError("The Wi-Fi address belongs to a different headset. Use --setup with the intended USB Quest.")
        return hardware


def choose_device(adb, args, config):
    address = endpoint(args.host) if args.host else config.get("endpoint")
    expected = config.get("deviceSerial")
    if address and not args.setup:
        address = endpoint(address)
        try:
            return address, adb.connect(address, expected)
        except InstallError as error:
            if "different headset" in str(error) or "not a recognized Meta Quest" in str(error):
                raise
            print("Wi-Fi reconnect unavailable; checking an authorized USB Quest.")
    devices = adb.devices()
    usb = [serial for serial, state in devices.items() if state == "device"
           and ":" not in serial and not serial.startswith("adb-")]
    if args.serial:
        if args.serial not in usb:
            raise InstallError("The requested USB serial is missing or unauthorized. " + HELP)
        usb = [args.serial]
    quests = []
    for serial in usb:
        try:
            quests.append((serial, adb.identity(serial)))
        except InstallError as error:
            if "not a recognized Meta Quest" not in str(error):
                raise
    if not quests:
        raise InstallError("No authorized USB Quest is available. " + HELP)
    if len(quests) != 1:
        raise InstallError("Multiple USB Quests are connected. Select the intended headset with --serial USB_SERIAL.")
    serial, hardware = quests[0]
    if expected and hardware != expected and not args.setup:
        raise InstallError("The USB Quest differs from the remembered headset. Use --setup --serial USB_SERIAL to change it.")
    route = adb.run("-s", serial, "shell", "ip", "route")
    ips = set(re.findall(r"\bdev\s+wlan\S*\b[^\n]*\bsrc\s+(\d+\.\d+\.\d+\.\d+)\b", route))
    if len(ips) != 1:
        raise InstallError("Could not determine one Quest Wi-Fi IPv4 address. Connect the Quest and PC to the same Wi-Fi.")
    ip = ipaddress.IPv4Address(ips.pop())
    if ip.is_loopback or ip.is_unspecified or ip.is_multicast:
        raise InstallError("The Quest reported an invalid Wi-Fi address.")
    port = address.rsplit(":", 1)[1] if address else "5555"
    address = str(ip) + ":" + port
    adb.run("-s", serial, "tcpip", port, positive=r"restarting in TCP mode")
    last = None
    for attempt in range(3):
        try:
            return address, adb.connect(address, hardware)
        except InstallError as error:
            last = error
            if "different headset" in str(error) or "not a recognized Meta Quest" in str(error):
                raise
            if attempt < 2:
                time.sleep(1)
    raise InstallError("USB enabled Wi-Fi ADB, but connection failed. Keep both devices on the same Wi-Fi. " + str(last))


def write_json(path, value):
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    source = result.add_mutually_exclusive_group()
    source.add_argument("--output-root", type=Path)
    source.add_argument("--handoff", type=Path)
    source.add_argument("--apk", type=Path)
    result.add_argument("--config", type=Path, default=REPO / ".planning/debug/quest3/wireless-install.json")
    result.add_argument("--adb", type=Path)
    result.add_argument("--host")
    result.add_argument("--serial")
    result.add_argument("--setup", action="store_true")
    result.add_argument("--no-launch", action="store_true")
    result.add_argument("--dry-run", action="store_true")
    return result


def main(argv=None, runner=None):
    args = parser().parse_args(argv)
    log = None
    try:
        config_path = args.config.expanduser().resolve()
        config = read_json(config_path) if config_path.is_file() else {}
        source = select_source(args, config)
        receipt = config_path.parent / "wireless-last-install.json"
        protected = {source.apk, source.path / "latest-build.json" if source.kind == "output-root" else source.path}
        if config_path in protected or receipt in protected:
            raise InstallError("Installer settings/receipt must not overwrite the APK or its build evidence.")
        address = endpoint(args.host or config["endpoint"]) if args.host or config.get("endpoint") else None
        print("Selected local APK: " + str(source.apk))
        print("SHA-256: " + source.sha256 + (" (DIAGNOSTIC)" if source.diagnostic else "")
              + (" (DUMMY IDENTITY)" if source.dummy else ""))
        if args.dry_run:
            print("Dry run: no ADB command or settings write. Endpoint: " + str(address or "USB setup required"))
            return 0
        executable = adb_path(args.adb, config.get("adb"))
        config_path.parent.mkdir(parents=True, exist_ok=True)
        log = config_path.parent / ("wireless-install-" + time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
                                    + "-" + uuid.uuid4().hex[:8] + ".log")
        log.write_text(json.dumps({"source": source.setting(), "apkSha256": source.sha256,
                                   "isDiagnostic": source.diagnostic, "isDummy": source.dummy}) + "\n",
                       encoding="utf-8")
        adb = Adb(executable, log, runner)
        address, hardware = choose_device(adb, args, config)
        # Re-read both the receipt and APK immediately before the only app mutation.
        if resolve_source(source.kind, source.path, check_zip=False) != source:
            raise InstallError("The APK or source evidence changed during device setup; retry with the intended build.")
        adb.run("-s", address, "install", "-r", source.apk, timeout=180, positive=r"^Success\s*$")
        if not args.no_launch:
            adb.run("-s", address, "shell", "am", "start", "-W", "-n", ACTIVITY,
                    timeout=45, positive=r"^Status:\s*ok\s*$")
        settings = {"schema": 1, "source": source.setting(), "adb": str(executable),
                    "endpoint": address, "deviceSerial": hardware}
        write_json(config_path, settings)
        write_json(receipt, {**settings, "apk": str(source.apk), "apkSha256": source.sha256,
                             "package": PACKAGE, "launched": not args.no_launch,
                             "isDiagnostic": source.diagnostic, "isDummy": source.dummy,
                             "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
        print("Installed with app data retained" + (" and launched" if not args.no_launch else "")
              + ": " + address + ". Device behavior remains for hardware testing.")
        return 0
    except (InstallError, OSError, ValueError, KeyError, TypeError, AttributeError, RuntimeError, zipfile.BadZipFile) as error:
        print("Quest installation stopped: " + str(error), file=sys.stderr)
        if log:
            print("ADB log: " + str(log), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
