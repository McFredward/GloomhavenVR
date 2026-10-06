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
import shlex
import struct
import tempfile
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
    # The builder's historical profile/storage/startup imports must neither consume
    # nor replace an application's existing stdlib profile or cached test module.
    missing = object()
    names = ["profile", "storage"]
    names += [name for name in ("script_order", "media", "shaders", "dlcs", "audio", "sprites", "ui_assets", "full_assets", "campaign", "mod_assets", "build_provenance", "import_workspace")
              if (REPO / "tools/quest-builder" / (name + ".py")).is_file()]
    names += ["startup", "builder"]
    aliases = [name for name in names if name != "builder"] + ["_ghvr_wireless_" + name for name in names]
    previous = {name: sys.modules.get(name, missing) for name in aliases}
    try:
        for name in names:
            spec = importlib.util.spec_from_file_location(
                "_ghvr_wireless_" + name, REPO / "tools/quest-builder" / (name + ".py"))
            module = importlib.util.module_from_spec(spec)
            # Dataclasses resolve postponed annotations through their defining
            # module while executing; retain that alias only during this load.
            sys.modules[spec.name] = module
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
class ContentFile:
    path: Path
    archive: str
    sha256: str
    size: int


@dataclass(frozen=True)
class Source:
    kind: str
    path: Path
    apk: Path
    sha256: str
    evidence: str
    diagnostic: bool
    dummy: bool
    mod_build: int | None = None
    input_key: str | None = None
    content: tuple[ContentFile, ...] = ()

    def setting(self):
        return {"kind": self.kind, "path": str(self.path)}


def apk_identity(apk):
    """Read the stamped build from the APK itself, never from its filename."""
    with zipfile.ZipFile(apk) as archive:
        name = "assets/Quest/input-manifest.json"
        if name not in archive.namelist():
            return {}
        info = archive.getinfo(name)
        if info.file_size > 8 * 1024 * 1024:
            raise InstallError("The APK build stamp exceeds its bounded manifest size.")
        value = json.loads(archive.read(name))
        build = value.get("mod", {}).get("modBuild")
        key = value.get("inputKey")
        if value.get("schema") != 1 or type(build) is not int or build <= 0 or not re.fullmatch(r"[0-9a-f]{64}", str(key)):
            raise InstallError("The APK has an invalid embedded Quest build stamp.")
        return {"modBuild": build, "inputKey": key,
                "isDummy": bool(value.get("profile", {}).get("isDummy")),
                "isDiagnostic": value.get("target") != "game"}


def apk_content(apk, identity):
    """Only the selected APK may declare its adjacent owned-content bank."""
    with zipfile.ZipFile(apk) as archive:
        name = "assets/Quest/content-delivery.json"
        if name not in archive.namelist():
            return ()
        if not identity or archive.getinfo(name).file_size > 65536:
            raise InstallError("Campaign delivery requires a bounded, stamped APK manifest.")
        value = json.loads(archive.read(name))
    if (value.get("schema") != 1 or value.get("package") != PACKAGE
            or value.get("inputKey") != identity["inputKey"]
            or not isinstance(value.get("files"), list) or len(value["files"]) != 1):
        raise InstallError("Campaign delivery differs from the selected APK identity.")
    result = []
    for row in value["files"]:
        if (not isinstance(row, dict) or row.get("file") != "GloomhavenVR-Quest-content.zip"
                or row.get("archive") != "quest-startup-content.zip"
                or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))
                or type(row.get("size")) is not int or row["size"] < 22):
            raise InstallError("Campaign delivery has an invalid content bank.")
        path = apk.parent / row["file"]
        if (not path.is_file() or path.is_symlink() or path.resolve().parent != apk.parent.resolve()
                or path.stat().st_size != row["size"] or digest(path) != row["sha256"]):
            raise InstallError("The complete Campaign bank is missing or differs from this APK: " + str(path))
        result.append(ContentFile(path, row["archive"], row["sha256"], row["size"]))
    return tuple(result)


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
    identity = apk_identity(apk)
    report = details.get("buildReport", {})
    expected_key = details.get("inputKey") or report.get("inputKey")
    expected_build = details.get("modBuild")
    if identity and ((expected_key and identity["inputKey"] != expected_key)
                     or (expected_build and identity["modBuild"] != expected_build)):
        raise InstallError("The APK embedded build differs from its handoff evidence.")
    return Source(kind, path, apk, actual, evidence,
                  bool(details.get("isDiagnostic", identity.get("isDiagnostic", False))), bool(details.get("isDummy", identity.get("isDummy"))),
                  identity.get("modBuild"), identity.get("inputKey"), apk_content(apk, identity))


def select_source(args, config):
    for kind in ("output-root", "handoff", "apk"):
        explicit = getattr(args, kind.replace("-", "_"))
        if explicit:
            return resolve_source(kind, explicit)
    remembered = config.get("source")
    candidates = []
    output = REPO / ".planning/quest3-local"
    if (output / "latest-build.json").is_file():
        candidates.append(("output-root", output))
    handoff = REPO / ".planning/debug/quest3/handoff.json"
    if handoff.is_file():
        candidates.append(("handoff", handoff))
    if remembered:
        candidates.append((remembered["kind"], Path(remembered["path"])))
    # Merging successive Windows archives intentionally leaves older APKs in
    # place. A remembered path must not pin the installer to yesterday's build.
    directories = {handoff.parent}
    for kind, path in candidates:
        if kind in ("handoff", "apk"):
            directories.add(path.parent)
    known = {(kind, path.resolve()) for kind, path in candidates}
    ignored = []
    for directory in sorted(directories):
        if directory.is_dir():
            for apk in sorted(directory.glob("GloomhavenVR-Quest-*.apk")):
                if ("apk", apk.resolve()) not in known:
                    try:
                        if apk_identity(apk):
                            candidates.append(("apk", apk))
                    except (OSError, ValueError, KeyError, TypeError, AttributeError, RuntimeError, zipfile.BadZipFile) as error:
                        ignored.append(apk.name + ": " + str(error)[:160])
    # Inspect small embedded manifests first; hash/CRC only the selected APK.
    ranked = []
    for index, (kind, path) in enumerate(candidates):
        try:
            if kind == "handoff":
                value = read_json(path)
                apk = path.parent / value["apk"]
                parent = path.parent.resolve()
            elif kind == "output-root":
                # Do not hash all old output-root artifacts just to rank them.
                # The winning source still receives full builder receipt checks.
                value = read_json(path / "latest-build.json")
                apk = path / value["apk"]
                parent = path.resolve()
            else:
                apk, parent = path, path.parent.resolve()
            if parent not in apk.resolve().parents:
                raise InstallError("Automatic APK candidate escapes its source directory.")
            identity = apk_identity(apk)
            ranked.append((identity.get("modBuild", 0), -index, (kind, path)))
        except (OSError, ValueError, KeyError, TypeError, AttributeError, RuntimeError, zipfile.BadZipFile) as error:
            ignored.append(path.name + ": " + str(error)[:160])
    if ignored:
        print("Ignored " + str(len(ignored)) + " unavailable automatic build candidate(s): " + "; ".join(ignored[:3]))
    if ranked:
        chosen = max(ranked, key=lambda row: row[:2])[2]
        return resolve_source(*chosen)
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


# Native installation receipt v1 is shared with QuestGameContent.Delivery.cs.
# The private completion receipt is a cache, not an anti-piracy credential.
_RECEIPT = ".quest-installation.receipt"
_RECEIPT_LIMIT = 8 * 1024 * 1024
_INSTALL_BATCH_BYTES = 256 * 1024 * 1024


def _receipt_string(value):
    encoded = value.encode("utf-8")
    return struct.pack("<i", len(encoded)) + encoded


def content_key(manifest):
    rows = sorted(manifest["files"], key=lambda row: row["path"].encode("utf-16-be"))
    data = bytearray(struct.pack("<i", 1) + _receipt_string(manifest["archive"]) + struct.pack("<i", len(rows)))
    for row in rows:
        data.extend(_receipt_string(row["path"]) + struct.pack("<q", row["size"]) + _receipt_string(row["sha256"]))
    return hashlib.sha256(data).hexdigest()


def encode_content_receipt(manifest, files):
    data = bytearray(struct.pack("<ii", 0x49514847, 1) + _receipt_string(content_key(manifest)) + struct.pack("<i", len(files)))
    for row in files.values():
        data.extend(_receipt_string(row["path"]) + _receipt_string(row["sha256"]) + struct.pack("<q", row["size"])
                    + struct.pack("<QQqqqqq", 0, 0, row["size"], 0, 0, 0, 0))
    if len(data) > _RECEIPT_LIMIT - 32:
        raise InstallError("Installation receipt exceeds its bounded size.")
    return bytes(data) + hashlib.sha256(data).digest()


def decode_content_receipt(data):
    if not 48 <= len(data) <= _RECEIPT_LIMIT or hashlib.sha256(data[:-32]).digest() != data[-32:]:
        return None
    payload = data[:-32]
    offset = 0

    def number(fmt):
        nonlocal offset
        result = struct.unpack_from(fmt, payload, offset)
        offset += struct.calcsize(fmt)
        return result[0] if len(result) == 1 else result

    def string(maximum):
        nonlocal offset
        size = number("<i")
        if not 0 <= size <= maximum or size > len(payload) - offset:
            raise ValueError("Invalid receipt string")
        result = payload[offset:offset + size].decode("utf-8")
        offset += size
        return result

    try:
        if number("<ii") != (0x49514847, 1):
            return None
        key, count = string(64), number("<i")
        if not re.fullmatch(r"[0-9a-f]{64}", key) or not 0 < count <= 32768:
            return None
        rows = {}
        for _ in range(count):
            path, sha, size = string(8192), string(64), number("<q")
            identity = number("<QQqqqqq")
            if not _content_path(path) or not re.fullmatch(r"[0-9a-f]{64}", sha) or size < 0 or identity[2] != size or path in rows:
                return None
            rows[path] = {"path": path, "sha256": sha, "size": size}
        return {"key": key, "files": rows} if offset == len(payload) else None
    except (ValueError, UnicodeError, struct.error):
        return None


def _content_path(path):
    return (isinstance(path, str) and path.startswith("StreamingAssets/") and len(path.encode("utf-8")) <= 8192
            and not any(char in path for char in "\\:\0\r\n\t")
            and all(part not in ("", ".", "..") for part in path.split("/")))


def installation_manifest(source):
    with zipfile.ZipFile(source.apk) as apk:
        name = "assets/Quest/installation-manifest.json"
        if name not in apk.namelist():
            return None  # Older local test packages retain their archive delivery.
        if apk.getinfo(name).file_size > _RECEIPT_LIMIT:
            raise InstallError("Installation manifest exceeds its bounded size.")
        manifest = json.loads(apk.read(name))
    if manifest.get("schema") != 1 or manifest.get("inputKey") != source.input_key:
        raise InstallError("Installation inventory differs from the selected APK.")
    for kind, archive in (("game", "quest-startup-content.zip"), ("mod", "quest-mod-content.zip")):
        value = manifest.get(kind)
        if (not isinstance(value, dict) or value.get("schema") != 1 or value.get("inputKey") != source.input_key
                or value.get("archive") != archive or not re.fullmatch(r"[0-9a-f]{64}", str(value.get("archiveSha256", "")))
                or not isinstance(value.get("files"), list) or not 0 < len(value["files"]) <= 32768):
            raise InstallError("Invalid " + kind + " installation inventory.")
        paths = set()
        for row in value["files"]:
            if (not isinstance(row, dict) or not _content_path(row.get("path"))
                    or row["path"].casefold() in paths or type(row.get("size")) is not int or row["size"] < 0
                    or not re.fullmatch(r"[0-9a-f]{64}", str(row.get("sha256", "")))):
                raise InstallError("Invalid or duplicate installed content path.")
            paths.add(row["path"].casefold())
        if kind == "mod" and paths != {"streamingassets/" + name for name in
                ("gloomhavenvr.bundle", "ghvr-town.bundle", "ghvr-town-voices.bundle")}:
            raise InstallError("The full-game mod inventory must contain its three native banks.")
    if (len(source.content) != 1 or not manifest["game"].get("externalDelivery")
            or source.content[0].sha256 != manifest["game"]["archiveSha256"]):
        raise InstallError("Installation game inventory differs from the adjacent owned bank.")
    return manifest


def _pull_content_receipt(adb, address, remote, local):
    try:
        adb.run("-s", address, "pull", remote, local, timeout=30, check_text=False)
    except InstallError:
        return None
    if not local.is_file() or local.stat().st_size > _RECEIPT_LIMIT:
        return None
    return decode_content_receipt(local.read_bytes())


def _content_shell(adb, address, lines, timeout=180):
    # Every interpolated operand is shell quoted, and paths have already passed
    # bounded containment validation. No user text is executable shell syntax.
    return adb.run("-s", address, "shell", "sh", "-c", shlex.quote("set -eu\n" + "\n".join(lines)),
                   timeout=timeout, check_text=False)


def _remote_content_sizes(adb, address, root, rows):
    result = {}
    for offset in range(0, len(rows), 128):
        batch = rows[offset:offset + 128]
        lines = []
        parents = set()
        for row in batch:
            parts = row["path"].split("/")
            parents.update(root + "/" + "/".join(parts[:end]) for end in range(1, len(parts)))
        for parent in sorted(parents):
            lines.append("test ! -L " + shlex.quote(parent))
        for index, row in enumerate(batch):
            target = shlex.quote(root + "/" + row["path"])
            lines.append("if test -f " + target + " && ! test -L " + target + "; then printf '" + str(index)
                         + "\\t'; stat -c %s " + target + "; fi")
        raw = _content_shell(adb, address, lines, timeout=30)
        for line in raw.splitlines():
            parts = line.split("\t")
            if len(parts) != 2 or not all(re.fullmatch(r"[0-9]+", part) for part in parts):
                raise InstallError("Unexpected device content metadata; installation stopped.")
            index, size = map(int, parts)
            if index >= len(batch) or batch[index]["path"] in result:
                raise InstallError("Duplicate or invalid device content metadata.")
            result[batch[index]["path"]] = size
    return result


def _matching_remote_content(adb, address, root, rows):
    matched = {}
    for offset in range(0, len(rows), 32):
        batch = rows[offset:offset + 32]
        lines = []
        for row in batch:
            target = shlex.quote(root + "/" + row["path"])
            lines.append("if test -f " + target + "; then sha256sum " + target + "; fi")
        raw = _content_shell(adb, address, lines, timeout=600)
        wanted = {root + "/" + row["path"]: row for row in batch}
        observed = set()
        for line in raw.splitlines():
            match = re.fullmatch(r"([0-9a-f]{64})  (.+)", line)
            if not match or match[2] not in wanted or match[2] in observed:
                raise InstallError("Unexpected or duplicate device content checksum.")
            observed.add(match[2])
            row = wanted[match[2]]
            if match[1] == row["sha256"]:
                matched[row["path"]] = row
    return matched


def _verify_remote_content(adb, address, root, rows):
    if len(_matching_remote_content(adb, address, root, rows)) != len(rows):
        raise InstallError("Installed file verification failed; the app was not launched. Rerun the installer to repair it.")


def _validate_zip_members(archive, manifest):
    wanted = {row["path"]: row for row in manifest["files"]}
    actual = {}
    for info in archive.infolist():
        if (info.filename not in wanted or info.filename in actual or info.file_size != wanted[info.filename]["size"]
                or info.is_dir() or (info.external_attr >> 16) & 0o170000 == 0o120000):
            raise InstallError("Owned content archive differs from its exact file inventory.")
        actual[info.filename] = info
    if actual.keys() != wanted.keys():
        raise InstallError("Owned content archive is missing manifested files.")
    return actual


def _publish_content_receipt(adb, address, root, manifest, rows, temporary, final):
    local = temporary / "receipt.bin"
    local.write_bytes(encode_content_receipt(manifest, rows))
    remote = root + "/" + final
    adb.run("-s", address, "push", local, remote + ".upload", timeout=30)
    _content_shell(adb, address, ["mv -f " + shlex.quote(remote + ".upload") + " " + shlex.quote(remote)], timeout=30)


def _install_content_tree(adb, address, source, manifest, archive, root, temporary, repair):
    rows = manifest["files"]
    members = _validate_zip_members(archive, manifest)
    _content_shell(adb, address, ["mkdir -p " + shlex.quote(root), "test ! -L " + shlex.quote(root)], timeout=30)
    committed = _pull_content_receipt(adb, address, root + "/" + _RECEIPT, temporary / "committed.bin")
    if committed and committed["key"] == content_key(manifest) and not repair:
        print("Reusing completed file-backed content: " + root.rsplit("/", 1)[-1] + ".")
        return {"contentKey": committed["key"], "files": len(rows), "reusedFiles": len(rows), "uploadedFiles": 0}
    inventory = {}
    for receipt in (committed, _pull_content_receipt(adb, address, root + "/" + _RECEIPT + ".previous", temporary / "previous.bin"),
                    _pull_content_receipt(adb, address, root + "/" + _RECEIPT + ".pending", temporary / "pending.bin")):
        if receipt:
            inventory.update(receipt["files"])
    sizes = _remote_content_sizes(adb, address, root, rows)
    retained = {row["path"]: row for row in rows if inventory.get(row["path"]) == row and sizes.get(row["path"]) == row["size"]}
    if repair and retained:
        # Repair verifies all claimed bytes at installation time, never at launch.
        retained = _matching_remote_content(adb, address, root, list(retained.values()))
    needed = [row for row in rows if row["path"] not in retained]
    if needed:
        # Invalidate completion BEFORE any replacement write. Retain only files
        # which this operation accepts; a failed repair must never revive a stale
        # claim for the same-sized corrupt file on the next installer run.
        _publish_content_receipt(adb, address, root, manifest, retained, temporary, _RECEIPT + ".previous")
        _content_shell(adb, address, ["rm -f " + shlex.quote(root + "/" + _RECEIPT)], timeout=30)
    print("Preparing " + str(len(needed)) + " changed files on the PC; " + str(len(retained)) + " files remain installed.")
    uploaded = 0
    while needed:
        batch, size = [], 0
        while needed and (not batch or (len(batch) < 256 and size + needed[0]["size"] <= _INSTALL_BATCH_BYTES)):
            row = needed.pop(0); batch.append(row); size += row["size"]
        stage = temporary / "StreamingAssets"
        stage.mkdir()
        try:
            for row in batch:
                target = temporary / row["path"]
                target.parent.mkdir(parents=True, exist_ok=True)
                sha, written = hashlib.sha256(), 0
                with archive.open(members[row["path"]]) as origin, target.open("wb") as destination:
                    for block in iter(lambda: origin.read(1024 * 1024), b""):
                        sha.update(block); written += len(block); destination.write(block)
                if written != row["size"] or sha.hexdigest() != row["sha256"]:
                    raise InstallError("PC extraction differs from the APK inventory: " + row["path"])
            transfer_timeout = min(21600, max(180, 120 + (size + 1048575) // 1048576))
            adb.run("-s", address, "push", stage, root + "/", timeout=transfer_timeout)
            _verify_remote_content(adb, address, root, batch)
            retained.update((row["path"], row) for row in batch)
            _publish_content_receipt(adb, address, root, manifest, retained, temporary, _RECEIPT + ".pending")
            uploaded += len(batch)
            print("Installed content files: " + str(len(retained)) + "/" + str(len(rows)) + ".")
        finally:
            shutil.rmtree(stage)
    _publish_content_receipt(adb, address, root, manifest, retained, temporary, _RECEIPT)
    obsolete = [row for path, row in inventory.items() if path not in retained]
    if obsolete:
        # Delete only earlier manifested files, after the replacement tree is
        # committed. Unknown files and original saves are outside this scope.
        _remote_content_sizes(adb, address, root, obsolete)  # Reject linked parents.
        for offset in range(0, len(obsolete), 128):
            _content_shell(adb, address, ["rm -f " + shlex.quote(root + "/" + row["path"])
                                         for row in obsolete[offset:offset + 128]], timeout=30)
    _content_shell(adb, address, ["rm -f " + shlex.quote(root + "/" + _RECEIPT + suffix) for suffix in (".pending", ".previous")], timeout=30)
    return {"contentKey": content_key(manifest), "files": len(rows), "reusedFiles": len(rows) - uploaded, "uploadedFiles": uploaded}


def install_file_content(adb, address, source, repair=False):
    manifest = installation_manifest(source)
    if manifest is None:
        return None
    adb.run("-s", address, "shell", "am", "force-stop", PACKAGE, timeout=30)
    base = "/sdcard/Android/data/" + PACKAGE + "/files/"
    records = []
    with tempfile.TemporaryDirectory(prefix="ghvr-quest-install-") as name, zipfile.ZipFile(source.apk) as apk:
        temporary = Path(name)
        mod_info = apk.getinfo("assets/quest-mod-content.zip")
        if mod_info.file_size > 4 * 1024 * 1024 * 1024:
            raise InstallError("Mod content archive exceeds its bounded size.")
        mod_path = temporary / "quest-mod-content.zip"
        with apk.open(mod_info) as origin, mod_path.open("wb") as destination:
            shutil.copyfileobj(origin, destination, 1024 * 1024)
        if digest(mod_path) != manifest["mod"]["archiveSha256"]:
            raise InstallError("The embedded mod bank differs from its inventory.")
        for kind, path, root in (("mod", mod_path, base + "quest-mod-resources"),
                                ("game", source.content[0].path, base + "quest-owned-game")):
            with zipfile.ZipFile(path) as archive:
                result = _install_content_tree(adb, address, source, manifest[kind], archive, root, temporary, repair)
            records.append({"archive": manifest[kind]["archive"], "sha256": manifest[kind]["archiveSha256"],
                            "size": path.stat().st_size, "remote": root, "fileBacked": True, **result})
    return records


def install_content(adb, address, source, previous=None, repair=False):
    prepared = install_file_content(adb, address, source, repair=repair)
    if prepared is not None:
        return prepared
    if not source.content:
        return []
    # Only this installed game's private external directory is touched.
    directory = "/sdcard/Android/data/" + PACKAGE + "/files/quest-install-input/" + source.input_key
    adb.run("-s", address, "shell", "am", "force-stop", PACKAGE, timeout=30)
    adb.run("-s", address, "shell", "mkdir", "-p", directory, timeout=30)
    records = []
    for bank in source.content:
        if bank.path.stat().st_size != bank.size or digest(bank.path) != bank.sha256:
            raise InstallError("The Campaign content bank changed during device setup.")
        remote = directory + "/" + bank.archive
        partial = remote + ".upload"
        reused = False
        previous_banks = previous.get("content", []) if isinstance(previous, dict) else []
        for saved in previous_banks:
            previous_remote = str(saved.get("remote", ""))
            safe = re.fullmatch(r"/sdcard/Android/data/" + re.escape(PACKAGE) + r"/files/quest-install-input/[0-9a-f]{64}/quest-startup-content\.zip", previous_remote)
            if not safe or saved.get("sha256") != bank.sha256 or saved.get("size") != bank.size:
                continue
            try:
                result = adb.run("-s", address, "shell", "sha256sum", previous_remote, timeout=600, check_text=False)
            except InstallError:
                continue
            if not re.fullmatch(bank.sha256 + r"\s+" + re.escape(previous_remote) + r"\s*", result.strip()):
                continue
            if previous_remote != remote:
                adb.run("-s", address, "shell", "mv", "-f", previous_remote, remote, timeout=30)
            print("Reusing the verified Campaign content already on this Quest.")
            reused = True
            break
        if reused:
            records.append({"archive": bank.archive, "size": bank.size, "sha256": bank.sha256, "remote": remote})
            continue
        print("Installing complete Campaign content: " + str(bank.size) + " bytes over ADB...")
        transfer_timeout = min(21600, max(180, 120 + (bank.size + 1048575) // 1048576))
        adb.run("-s", address, "push", bank.path, partial, timeout=transfer_timeout)
        result = adb.run("-s", address, "shell", "sha256sum", partial, timeout=600, check_text=False)
        if not re.fullmatch(bank.sha256 + r"\s+" + re.escape(partial) + r"\s*", result.strip()):
            raise InstallError("The uploaded Campaign content does not match this APK; the app was not launched.")
        adb.run("-s", address, "shell", "mv", "-f", partial, remote, timeout=30)
        records.append({"archive": bank.archive, "size": bank.size, "sha256": bank.sha256, "remote": remote})
    return records


def clean_previous_content(adb, address, previous, installed):
    if not isinstance(previous, dict) or not installed:
        return
    current = {row["remote"] for row in installed}
    for row in previous.get("content", []):
        remote = str(row.get("remote", ""))
        if (remote not in current and re.fullmatch(r"/sdcard/Android/data/" + re.escape(PACKAGE)
                + r"/files/quest-install-input/[0-9a-f]{64}/quest-startup-content\.zip", remote)):
            # Only an earlier successful installation's exact bank is removed,
            # after every new bank has passed its device checksum.
            adb.run("-s", address, "shell", "rm", "-f", remote, timeout=30)


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
    result.add_argument("--repair-content", action="store_true", help="Verify installed game bytes and repair missing/changed files before launch.")
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
        protected.update(bank.path for bank in source.content)
        if config_path in protected or receipt in protected:
            raise InstallError("Installer settings/receipt must not overwrite the APK or its build evidence.")
        address = endpoint(args.host or config["endpoint"]) if args.host or config.get("endpoint") else None
        print("Selected local APK: " + str(source.apk))
        print("Embedded build: " + ("B" + str(source.mod_build) + " (input " + source.input_key[:12] + ")" if source.mod_build else "legacy unstamped local APK"))
        print("SHA-256: " + source.sha256 + (" (DIAGNOSTIC)" if source.diagnostic else "")
              + (" (DUMMY IDENTITY)" if source.dummy else ""))
        if source.content:
            print("Complete owned Campaign bank: " + str(sum(bank.size for bank in source.content)) + " bytes; installed automatically with the APK.")
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
        # Budget one MiB/second plus two minutes for Android package processing.
        # Retain the small-APK floor and a bounded thirty-minute maximum.
        transfer_seconds = (source.apk.stat().st_size + 1024 * 1024 - 1) // (1024 * 1024)
        install_timeout = min(1800, max(180, 120 + transfer_seconds))
        adb.run("-s", address, "install", "-r", source.apk, timeout=install_timeout, positive=r"^Success\s*$")
        # B618+ uses its build number as Android versionCode. Querying that
        # small package record confirms installation without another multi-GB
        # hash/read of the headset's APK before every hardware test.
        installed_version = None
        if source.mod_build and source.mod_build >= 618:
            # Dumpsys fields such as Dexopt's "[location is error]" are data,
            # not command failures. Require a successful exit and exact stamps.
            package_info = adb.run("-s", address, "shell", "dumpsys", "package", PACKAGE,
                                   timeout=30, check_text=False)
            versions = re.findall(r"\bversionCode=(\d+)\b", package_info)
            names = re.findall(r"\bversionName=([^\s]+)", package_info)
            expected_name = "0.1.0.B" + str(source.mod_build) + "." + source.input_key[:12]
            if versions != [str(source.mod_build)] or names != [expected_name]:
                raise InstallError("Installed Android build is " + str(versions or "unknown")
                                   + "; expected B" + str(source.mod_build) + ". No launch or success receipt was written.")
            installed_version = int(versions[0])
            print("Confirmed installed build: B" + str(installed_version) + " (input " + source.input_key[:12] + ")")
        previous = None
        if receipt.is_file():
            try:
                prior = read_json(receipt)
                if prior.get("deviceSerial") == hardware and prior.get("package") == PACKAGE: previous = prior
            except (OSError, ValueError):
                pass
        content_records = install_content(adb, address, source, previous, repair=args.repair_content)
        clean_previous_content(adb, address, previous, content_records)
        if not args.no_launch:
            adb.run("-s", address, "shell", "am", "start", "-W", "-n", ACTIVITY,
                    timeout=45, positive=r"^Status:\s*ok\s*$")
        settings = {"schema": 1, "source": source.setting(), "adb": str(executable),
                    "endpoint": address, "deviceSerial": hardware}
        write_json(config_path, settings)
        write_json(receipt, {**settings, "apk": str(source.apk), "apkSha256": source.sha256,
                             "package": PACKAGE, "launched": not args.no_launch,
                             "isDiagnostic": source.diagnostic, "isDummy": source.dummy,
                             "modBuild": source.mod_build, "inputKey": source.input_key,
                             "installedVersionCode": installed_version,
                             "content": content_records,
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
