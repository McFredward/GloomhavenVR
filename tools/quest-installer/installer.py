"""Install one verified local Quest APK over identity-checked USB or Wi-Fi ADB.

Prefer an authorized USB Quest without restarting adbd or requiring Wi-Fi.
USB transfers retain their transport; completed installations also remember
verified wireless access for later log collection without the cable.
Windows and Linux provision official Android platform-tools locally if ADB is missing.
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


class SigningMismatch(InstallError):
    """Android rejected this exact package update because its signer changed."""


class ConnectionRequired(InstallError):
    """Installation awaits a headset/authorization action; its build is retained."""
    def __init__(self, message, reason="missing-or-unauthorized"):
        super().__init__(message)
        self.reason = reason


def connection_unavailable(text):
    """Transport/host-daemon failures, never an Android file permission error."""
    return bool(re.search(r"device offline|device unauthorized|no devices/emulators found|device '[^'\n]+' not found|"
                          r"cannot connect to daemon|daemon (?:still )?not running|failed to start daemon|"
                          r"connection (?:reset|refused)|closed without a complete shell-v2 response|"
                          r"ADB server closed binary upload", str(text), re.I))


_LAST_CONTENT_PROGRESS = None


def signing_recovery_module():
    # Isolated launcher imports do not put sibling modules on sys.path.
    spec = importlib.util.spec_from_file_location(
        "_ghvr_quest_signing_recovery", REPO / "tools/quest-installer/signing_recovery.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def cleanup_completed_install(adb, address, hardware, directory, source):
    """Best-effort exact scratch cleanup; never replay game or save payloads."""
    try:
        spec = importlib.util.spec_from_file_location(
            "_ghvr_quest_device_cleanup", REPO / "tools/quest-installer/device_cleanup.py")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        result = module.cleanup_after_install(adb, address, hardware, directory, source)
    except (OSError, ValueError, RuntimeError, ImportError) as error:
        result = {"complete": False, "deferred": "owned-scratch-cleanup-unavailable", "detail": str(error)[:512]}
    # Failure to inspect optional scratch never invalidates a finished install.
    # Full, user-confirmed inventory lives in the independent device-tools page.
    try:
        with adb.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps({"completedInstallerScratchCleanup": result}) + "\n")
    except OSError:
        pass
    return result


def content_access_module():
    spec = importlib.util.spec_from_file_location(
        "_ghvr_quest_content_access", REPO / "tools/quest-installer/content_access.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def remember_wireless_after_install(adb, address, hardware, settings):
    """Optional convenience after the entire cable transaction is committed."""
    try:
        spec = importlib.util.spec_from_file_location(
            "_ghvr_quest_wireless", REPO / "tools/quest-installer/wireless.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        settings, result = module.remember_after_usb(adb, address, hardware, settings)
    except (OSError, ImportError, ValueError, RuntimeError) as error:
        result = {"status": "unavailable", "reason": "optional-wireless-setup-unavailable", "detail": str(error)[:512]}
    with adb.log.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps({"wirelessAfterInstallation": result}) + "\n")
    print(result.get("message", {}).get("en", "Installation completed; wireless setup remains available in device tools."), flush=True)
    return settings, result


_DIGEST_CACHE = {}
_INPUT_CACHE_PATH = None


def input_cache_module():
    name = "_ghvr_quest_input_cache"
    if name not in sys.modules:
        spec = importlib.util.spec_from_file_location(name, REPO / "tools/quest-installer/input_cache.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        sys.modules[name] = module
    return sys.modules[name]


def _digest_witness(path):
    row = path.stat()
    return (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns, row.st_ctime_ns)


def digest(path):
    """Reuse a fully computed digest while the exact stat witness remains valid.

    Selection and the pre-installation boundary both resolve the same APK and
    complete content ZIP. Reading their unchanged 14 GB twice provides no new
    evidence. Cache only after an unchanged before/after stat witness, invalidate
    on file replacement or mutation. A small local retry cache additionally
    preserves that same witness across CLI attempts; expected SHA comparisons
    and the independent owned-game qualification remain mandatory.
    """
    path = Path(path)
    before = _digest_witness(path)
    key = (str(path.absolute()), *before)
    if key in _DIGEST_CACHE: return _DIGEST_CACHE[key]
    if _INPUT_CACHE_PATH:
        found = input_cache_module().lookup(_INPUT_CACHE_PATH, path, before)
        if found:
            _DIGEST_CACHE[key] = found
            return found
    value = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            value.update(block)
    if _digest_witness(path) != before:
        raise InstallError("The selected input changed while its checksum was being qualified; retry with the completed file.")
    if len(_DIGEST_CACHE) >= 32: _DIGEST_CACHE.pop(next(iter(_DIGEST_CACHE)))
    result = value.hexdigest(); _DIGEST_CACHE[key] = result
    if _INPUT_CACHE_PATH: input_cache_module().remember(_INPUT_CACHE_PATH, path, before, result)
    return result


def read_json(path):
    value = json.loads(path.read_text(encoding="utf-8-sig"))
    if not isinstance(value, dict) or value.get("schema") != 1:
        raise InstallError("Unsupported JSON schema: " + str(path))
    return value


def builder_module():
    # The builder's historical profile/storage/startup imports must neither consume
    # nor replace an application's existing stdlib profile or cached test module.
    # Preparation's identity/refresh imports its reviewed overlay directly. Register
    # those dependencies before loading builder even in a Git-free isolated launch.
    missing = object()
    names = ["profile", "storage"]
    names += [name for name in ("script_order", "media", "shaders", "dlcs", "audio", "sprites", "ui_assets", "staging_resume", "full_assets", "campaign", "mod_assets", "build_provenance", "import_workspace", "native_plugins", "script_remap_resume", "editor_overlay", "preparation_identity", "preparation_metadata", "figure_banks")
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
                # B690's Wizard compares figure requirements after these local
                # aliases have been restored. Retain the exact selected helper
                # while its storage functions still bind to this Builder, just
                # like the Builder's eagerly captured APK/update dependencies.
                # Older source snapshots without figure support remain valid.
                if "figure_banks" in names:
                    module.figure_banks = sys.modules["figure_banks"]
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
    package: Path | None = None
    member: str | None = None


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
        profile = value.get("profile", {})
        update_name = "assets/Quest/update-manifest.json"
        retained = False
        if update_name in archive.namelist():
            if archive.getinfo(update_name).file_size > 8 * 1024 * 1024:
                raise InstallError("APK update provenance exceeds its bound.")
            update = json.loads(archive.read(update_name))
            if (update.get("schema") != 1 or update.get("gameInputKey") != key
                    or not re.fullmatch(r"[0-9a-f]{64}", str(update.get("updateKey", "")))
                    or type(update.get("modBuild")) is not int or update["modBuild"] <= 0):
                raise InstallError("APK update provenance differs from the original game content.")
            build = update["modBuild"]; profile = update.get("profile", profile); retained = True
        return {"modBuild": build, "inputKey": key,
                "isDummy": bool(profile.get("isDummy")), "retainedGameContent": retained,
                "isDiagnostic": value.get("target") != "game"}


def portable_packages():
    """Capture canonical helpers through the existing isolated Builder loader."""
    helper = getattr(builder_module(), "portable_packages", None)
    if helper is None:
        raise InstallError("This Builder does not include its portable package reader; use the current complete Builder archive.")
    return helper


def content_available(bank):
    return (bank.package or bank.path).is_file()


def content_stream(bank):
    if bank.package:
        return portable_packages().open_content({"path": bank.package, "member": bank.member,
            "size": bank.size, "sha256": bank.sha256, "archive": bank.archive})
    return bank.path.open("rb")


def apk_content(apk, identity, *, package=None, output=None):
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
        if package is not None or (output is not None and not path.is_file()):
            # Portable packages retain the complete bank as a STORED outer
            # member. Read it only if the installed per-file receipt needs
            # repair; never extract or hash an 11-GB duplicate at selection.
            try:
                found = portable_packages().content_source(apk, output or apk.parent,
                                                           source_package=package)
            except (RuntimeError, OSError, ValueError) as error:
                if package is not None: raise InstallError(str(error)) from error
            else:
                if (found["size"], found["sha256"], found["archive"]) != (row["size"], row["sha256"], row["archive"]):
                    raise InstallError("Portable game bank differs from the signed APK delivery contract.")
                if found.get("member"):
                    outer = Path(found["path"])
                    result.append(ContentFile(outer, row["archive"], row["sha256"], row["size"], outer, found["member"]))
                    continue
                path = Path(found["path"])
        if not path.exists() and identity.get("retainedGameContent"):
            # The signed inventory still requires the complete installed game.
            # Device qualification happens before replacing the APK.
            result.append(ContentFile(path, row["archive"], row["sha256"], row["size"]))
            continue
        if (not path.is_file() or path.is_symlink() or (output is None and path.resolve().parent != apk.parent.resolve())
                or path.stat().st_size != row["size"]):
            raise InstallError("The complete Campaign bank is missing or differs from this APK: " + str(path))
        bank = ContentFile(path, row["archive"], row["sha256"], row["size"])
        # The signed per-file inventory qualifies bytes during actual delivery.
        # Scanning the whole bank first duplicates that work and delays warm
        # installations even when no game files need to be transferred.
        proof = Source("apk", apk, apk, "", "", False, False,
                       identity.get("modBuild"), identity["inputKey"], (bank,))
        if installation_manifest(proof) is None and digest(path) != row["sha256"]:
            raise InstallError("The complete Campaign bank differs from this APK: " + str(path))
        result.append(bank)
    return tuple(result)


def resolve_source(kind, path, check_zip=True):
    path = Path(path).expanduser().resolve()
    details = {}
    manifest = None
    package = None
    output = None
    imported_hash = None
    package_stamp = None
    if kind in ("apk", "package") and path.suffix.lower() == ".zip":
        kind = "package"
    if kind == "package":
        package = path
        output = (_INPUT_CACHE_PATH.parent if _INPUT_CACHE_PATH else REPO / ".planning/debug/quest3") / "portable"
        helper = portable_packages()
        try:
            descriptor = helper.inspect(package)
            package_stamp = _digest_witness(package)
            evidence = hashlib.sha256(json.dumps({"manifest": descriptor, "stamp": package_stamp}, sort_keys=True).encode()).hexdigest()
            apk = helper.import_apk(package, output, progress=lambda value:
                print("GHVRQ_PROGRESS " + json.dumps({"schema": 1, "operation": "qualify",
                    "phase": value["phase"], "done": value["completed"], "total": value["total"],
                    "unit": "bytes", "detail": value["detail"]}), flush=True))
            imported_hash = helper.imported_apk_digest(apk, output)
        except (RuntimeError, ValueError, OSError) as error:
            raise InstallError("Portable Quest package could not be opened: " + str(error)) from error
        details = {"package": PACKAGE, "apkSha256": descriptor["apk"]["sha256"]}
    elif kind == "output-root":
        manifest = path / "latest-build.json"
        evidence = digest(manifest)
        helper = builder_module()
        output = path
        # This loader's private module cannot affect a running Builder. Its
        # explicit APK digest shares the same witnessed installer read/cache.
        helper.digest = digest
        apk, details = helper.verified_latest_build(path)
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
    actual = imported_hash or digest(apk)
    if kind != "apk" and (not re.fullmatch(r"[0-9a-f]{64}", str(details.get("apkSha256", "")))
                              or actual != details["apkSha256"]):
        raise InstallError("APK SHA-256 differs from its build evidence; restore or rebuild it.")
    if manifest and digest(manifest) != evidence:
        raise InstallError("The selected build evidence changed while being read; retry.")
    if check_zip:
        with zipfile.ZipFile(apk) as archive:
            # Completed outputs have an independently verified build receipt
            # and exact expected SHA. Decompressing the entire APK again adds
            # no new evidence. A manually selected APK needs one CRC success,
            # cached separately from its digest (which precedes CRC checking).
            zip_cache = _INPUT_CACHE_PATH.with_name("qualified-apks.json") if _INPUT_CACHE_PATH else None
            apk_stamp = _digest_witness(apk)
            qualified = kind != "apk" or (zip_cache and input_cache_module().lookup(zip_cache, apk, apk_stamp) == actual)
            if "AndroidManifest.xml" not in archive.namelist() or (not qualified and archive.testzip()):
                raise InstallError("The APK ZIP payload is incomplete or corrupt.")
            if zip_cache: input_cache_module().remember(zip_cache, apk, apk_stamp, actual)
    identity = apk_identity(apk)
    report = details.get("buildReport", {})
    expected_key = details.get("inputKey") or report.get("inputKey")
    expected_build = details.get("modBuild")
    if identity and ((expected_key and identity["inputKey"] != expected_key)
                     or (expected_build and identity["modBuild"] != expected_build)):
        raise InstallError("The APK embedded build differs from its handoff evidence.")
    if package is not None and _digest_witness(package) != package_stamp:
        raise InstallError("The selected portable package changed during APK qualification; retry.")
    return Source(kind, path, apk, actual, evidence,
                  bool(details.get("isDiagnostic", identity.get("isDiagnostic", False))), bool(details.get("isDummy", identity.get("isDummy"))),
                  identity.get("modBuild"), identity.get("inputKey"), apk_content(apk, identity, package=package, output=output))


def select_source(args, config):
    for kind in ("output-root", "handoff", "apk", "package"):
        explicit = getattr(args, kind.replace("-", "_"), None)
        if explicit:
            return resolve_source(kind, explicit)
    remembered = config.get("source")
    if isinstance(remembered, dict) and remembered.get("kind") == "package":
        return resolve_source("package", remembered["path"])
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


def managed_linux_adb():
    spec = importlib.util.spec_from_file_location(
        "_ghvr_quest_adb_bootstrap", REPO / "tools/quest-installer/adb_bootstrap.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    try:
        return module.ensure_linux_adb(REPO / "scripts")
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
        if sys.platform == "linux":
            candidates += [Path.home() / "Android/Sdk/platform-tools/adb",
                           Path.home() / ".android/sdk/platform-tools/adb"]
    for value in candidates:
        if value and Path(value).expanduser().is_file():
            # Check the managed lexical path before resolving links; a moved
            # or linked cache must go through ownership/integrity validation.
            candidate = Path(os.path.abspath(Path(value).expanduser()))
            if sys.platform in ("win32", "linux"):
                try:
                    candidate.relative_to(REPO / "scripts/.quest-adb")
                except ValueError:
                    pass
                else:
                    return managed_windows_adb() if sys.platform == "win32" else managed_linux_adb()
            return candidate.resolve()
    if sys.platform == "win32" and not explicit:
        return managed_windows_adb()
    if sys.platform == "linux" and not explicit:
        return managed_linux_adb()
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
        self.wait_enabled = False
        self.connection_config = {}
        self.connection_operation = "connect"
        self._transport_aliases = {}
        self._probing_connection = False

    def ensure_owned_server(self):
        if not os.environ.get("GHVRQ_ADB_LIFETIME"):
            return
        name = "_ghvr_quest_adb_lifetime"
        module = sys.modules.get(name)
        if module is None:
            spec = importlib.util.spec_from_file_location(name, REPO / "tools/quest-installer/adb_lifetime.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            sys.modules[name] = module
        try: module.ensure_server(self.executable)
        except (OSError, RuntimeError, ValueError) as error:
            raise InstallError("The Wizard's local ADB server could not start: " + str(error)) from error

    def transport(self, serial):
        return self._transport_aliases.get(serial, serial)

    def wait_for_connection(self, serial=None):
        """Pause this child, retain its measured work and qualify the same Quest.

        The Wizard owns cancellation of this process. Only safe content calls
        retry here; uninstall/restore commands keep their transaction owner.
        Capture193713's local server returns on the next call: try start-server
        before asking for a cable, without killing a shared ADB server.
        """
        if not self.wait_enabled:
            raise ConnectionRequired("The Quest connection is unavailable. " + HELP, "offline-or-unauthorized")
        previous = _LAST_CONTENT_PROGRESS
        fields = dict(previous or {"schema": 1, "operation": self.connection_operation})
        fields.update(phase="install-connection-wait", status="waiting",
                      detail="Reconnect and unlock the same Quest; accept USB debugging. Installation continues automatically.")
        print("GHVRQ_PROGRESS " + json.dumps(fields), flush=True)
        print("Quest connection required: reconnect the same Quest by USB and accept USB debugging. Waiting to continue automatically.", flush=True)
        self._probing_connection = True
        try:
            while True:
                try:
                    self.run("start-server", timeout=15, check_text=False)
                    address, hardware = choose_device(self, argparse.Namespace(host=None, serial=None, setup=False), self.connection_config)
                    if serial: self._transport_aliases[serial] = address
                    self.connection_config = connection_settings(self.connection_config, address, hardware, self.executable)
                    print("Quest connection restored; continuing retained installation.", flush=True)
                    if previous: print("GHVRQ_PROGRESS " + json.dumps(previous), flush=True)
                    else: print('GHVRQ_PROGRESS {"schema":1,"phase":"install-connect","operation":"connect","status":"progress"}', flush=True)
                    return address, hardware
                except (InstallError, OSError, subprocess.TimeoutExpired):
                    # No network scanning or adoption of an unrelated headset.
                    time.sleep(2)
        finally:
            self._probing_connection = False

    def binary_stream(self, serial, local, script, timeout, report=None):
        self.ensure_owned_server()
        # Isolated -I launchers do not put sibling modules on sys.path. Cache
        # this process's current helper; it is independent of game build input.
        name = "_ghvr_quest_adb_binary"
        module = sys.modules.get(name)
        if module is None:
            spec = importlib.util.spec_from_file_location(name, REPO / "tools/quest-installer/adb_binary.py")
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            sys.modules[name] = module
        return module.stream(self, self.transport(serial), local, script, timeout, report)

    def run(self, *args, timeout=20, positive=None, check_text=True, input_text=None, empty_exit_codes=(), reconnect=False):
        while True:
            try:
                return self._run_once(*args, timeout=timeout, positive=positive, check_text=check_text,
                                      input_text=input_text, empty_exit_codes=empty_exit_codes)
            except ConnectionRequired:
                if not reconnect or not self.wait_enabled or self._probing_connection: raise
                serial = str(args[1]) if len(args) > 1 and args[0] == "-s" else None
                self.wait_for_connection(serial)

    def _run_once(self, *args, timeout=20, positive=None, check_text=True, input_text=None, empty_exit_codes=()):
        self.ensure_owned_server()
        operands = list(map(str, args))
        serial = operands[1] if len(operands) > 1 and operands[0] == "-s" else None
        if serial: operands[1] = self.transport(serial)
        command = [self.executable, *operands]
        with self.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(command) + "\n")
            if input_text is not None: stream.write(json.dumps({"stdinScript": input_text}, ensure_ascii=False) + "\n")
        try:
            options = {"capture_output": True, "timeout": timeout, "shell": False}
            if input_text is None: options.update(text=True, encoding="utf-8", errors="replace")
            else:
                # Binary stdin preserves LF on Windows; CRLF breaks remote sh.
                options.update(text=False, input=input_text.encode("utf-8"))
            result = self.runner(command, **options)
        except subprocess.TimeoutExpired as error:
            raise ConnectionRequired("ADB timed out. Reconnect the same Quest and accept USB debugging. " + HELP,
                                     "offline-or-unauthorized") from error
        except OSError as error:
            with self.log.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps({"processStartError": type(error).__name__, "winError": getattr(error, "winerror", None), "message": str(error)}) + "\n")
            raise InstallError("ADB could not start: " + str(error) + ". Its command and error were retained in the ADB log.") from error
        def decoded(value): return value.decode("utf-8", errors="replace") if isinstance(value, bytes) else value or ""
        raw = decoded(result.stdout) + "\n" + decoded(result.stderr)
        with self.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps({"exitCode": result.returncode}) + "\n")
            stream.write(raw + "\n")
        # pidof returns one while a just-started app has no PID yet. Only an
        # explicitly permitted, entirely empty result represents that expected
        # observation; offline/authorization errors and every other status fail.
        if result.returncode in empty_exit_codes and not raw.strip(): return ""
        operands = tuple(map(str, args))
        action = operands[2] if operands[:1] == ("-s",) and len(operands) > 2 else operands[0] if operands else None
        if (action == "install" and re.search(
                r"Failure\s*\[INSTALL_FAILED_UPDATE_INCOMPATIBLE(?:\s*[:\]])", raw)):
            raise SigningMismatch("Android rejected the package update because its signing certificate differs.")
        if result.returncode and connection_unavailable(raw):
            raise ConnectionRequired("The Quest connection is unavailable. " + HELP, "offline-or-unauthorized")
        if (result.returncode or (check_text and re.search(r"\b(error|failure|failed|unauthorized|cannot|unable)\b", raw, re.I))
                or (positive and not re.search(positive, raw, re.I | re.M))):
            context = " ".join(operands)[:320]
            raise InstallError("ADB failed: command " + context + "; exit " + str(result.returncode)
                               + "; " + (raw.strip()[:1200] or "no output was returned"))
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
    devices = adb.devices()

    def usb_quests(current):
        usb = [serial for serial, state in current.items() if state == "device"
               and ":" not in serial and not serial.startswith("adb-")]
        if args.serial:
            if args.serial not in usb:
                raise ConnectionRequired("The requested USB serial is missing or unauthorized. " + HELP)
            usb = [args.serial]
        quests = []
        for serial in usb:
            try: quests.append((serial, adb.identity(serial)))
            except InstallError as error:
                if "not a recognized Meta Quest" not in str(error): raise
        if expected and not args.setup:
            matched = [item for item in quests if item[1] == expected]
            if matched: quests = matched
        return quests

    def direct_usb(quests):
        if len(quests) > 1:
            raise ConnectionRequired("Multiple USB Quests are connected. Select the intended headset with --serial USB_SERIAL.", "ambiguous")
        if not quests: return None
        serial, hardware = quests[0]
        if expected and hardware != expected and not args.setup:
            raise ConnectionRequired("The USB Quest differs from the remembered headset. Connect the previously selected Quest or choose a new headset explicitly.", "different-headset")
        print("Using the authorized USB Quest directly; no Wi-Fi setup is required.", flush=True)
        return serial, hardware

    # Capture170604 lists both transports of the same Quest. The old path found
    # USB, then restarted adbd in TCP mode anyway. Default/USB selections must
    # stay on the cable; an explicit wireless request keeps its intended target.
    quests = usb_quests(devices)
    if quests and not (args.setup or args.host):
        selected = [item for item in quests if not expected or item[1] == expected]
        if selected: return direct_usb(selected)
    if address and not args.setup and not args.serial:
        address = endpoint(address)
        try:
            return address, adb.connect(address, expected)
        except InstallError as error:
            if "different headset" in str(error) or "not a recognized Meta Quest" in str(error):
                raise
            print("Wi-Fi reconnect unavailable; checking an authorized USB Quest.")
            # A cable can have been attached during the failed reconnect.
            devices = adb.devices(); quests = usb_quests(devices)
    if quests and not args.setup: return direct_usb(quests)
    # Capture095538 completed the Windows APK but a new Wizard device directory
    # had no remembered endpoint. Reuse an already authorized Wi-Fi connection
    # before demanding USB again. Do not discover/probe network addresses or
    # override explicit USB setup/serial/host choices; qualify the actual Quest
    # and stable hardware identity before any app mutation.
    if not quests and not (args.setup or args.serial or args.host):
        wireless = {}
        for address, state in sorted(devices.items()):
            if state != "device" or ":" not in address:
                continue
            try:
                address = endpoint(address)
            except InstallError:
                continue
            try:
                hardware = adb.identity(address)
            except InstallError as error:
                if "not a recognized Meta Quest" in str(error):
                    continue
                raise
            if not expected or hardware == expected:
                wireless.setdefault(hardware, address)
        if len(wireless) > 1:
            raise ConnectionRequired("Multiple Wi-Fi Quests are connected. Disconnect other headsets or choose the intended address with --host HOST[:PORT].", "ambiguous")
        if wireless:
            hardware, address = next(iter(wireless.items()))
            print("Reusing the already authorized Wi-Fi Quest.")
            return address, adb.connect(address, hardware)
    if not quests:
        raise ConnectionRequired("No authorized USB Quest is available. " + HELP)
    if len(quests) != 1:
        raise ConnectionRequired("Multiple USB Quests are connected. Select the intended headset with --serial USB_SERIAL.", "ambiguous")
    serial, hardware = quests[0]
    if expected and hardware != expected and not args.setup:
        raise ConnectionRequired("The USB Quest differs from the remembered headset. Connect the previously selected Quest or choose a new headset explicitly.", "different-headset")
    route = adb.run("-s", serial, "shell", "ip", "route")
    ips = set(re.findall(r"\bdev\s+wlan\S*\b[^\n]*\bsrc\s+(\d+\.\d+\.\d+\.\d+)\b", route))
    if len(ips) != 1:
        raise ConnectionRequired("Could not determine one Quest Wi-Fi IPv4 address. Connect the Quest and PC to the same Wi-Fi.", "wifi-unavailable")
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
    # Explicit wireless setup can restart the USB transport. If it is reachable
    # again, retain the same hardware identity and install through the cable.
    recovered = [item for item in usb_quests(adb.devices()) if item[1] == hardware]
    if recovered: return direct_usb(recovered)
    raise ConnectionRequired("USB enabled Wi-Fi ADB, but connection failed. Keep the Quest connected by USB and retry. " + str(last), "wifi-unavailable")


def connection_settings(config, serial, hardware, executable):
    """A USB serial is not a network endpoint; retain any previous Wi-Fi hint."""
    settings = dict(config, schema=1, deviceSerial=hardware, adb=str(executable), adbSerial=serial,
                    transport="wifi" if ":" in serial or serial.startswith("adb-") else "usb")
    if settings["transport"] == "wifi": settings["endpoint"] = endpoint(serial)
    elif config.get("deviceSerial") and config["deviceSerial"] != hardware:
        settings.pop("endpoint", None)
    return settings


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
_FILE_JOURNAL = ".quest-installation.accepted.jsonl"
_UNREAD_RECEIPT = object()
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
        if kind == "mod":
            legacy = ("StreamingAssets/gloomhavenvr.bundle", "StreamingAssets/ghvr-town.bundle", "StreamingAssets/ghvr-town-voices.bundle")
            current = legacy + ("StreamingAssets/ghvr-environment.bundle",)
            if tuple(row["path"] for row in value["files"]) not in (legacy, current):
                raise InstallError("The full-game mod inventory must contain its complete native-bank paths/order.")
    if (len(source.content) != 1 or not manifest["game"].get("externalDelivery")
            or source.content[0].sha256 != manifest["game"]["archiveSha256"]):
        raise InstallError("Installation game inventory differs from the adjacent owned bank.")
    return manifest


def _pull_content_receipt(adb, address, remote, local):
    try:
        adb.run("-s", address, "pull", remote, local, timeout=30, check_text=False, reconnect=True)
    except ConnectionRequired:
        raise  # A disconnected Quest is not evidence that its content vanished.
    except InstallError:
        return None
    if not local.is_file() or local.stat().st_size > _RECEIPT_LIMIT:
        return None
    return decode_content_receipt(local.read_bytes())


def _content_shell(adb, address, lines, timeout=180, reconnect=True):
    # Every interpolated operand is shell quoted, and paths have already passed
    # bounded containment validation. No user text is executable shell syntax.
    # A 128-file stat batch exceeds Windows CreateProcess's 32767-character
    # limit with the real B625 bank. Keep argv short; send LF script over stdin.
    return adb.run("-s", address, "shell", "-T", "sh", "-s",
                   input_text="set -eu\n" + "\n".join(lines) + "\n", timeout=timeout, check_text=False, reconnect=reconnect)


def _content_bank_root(remote):
    prefix = "/sdcard/Android/data/" + PACKAGE + "/files/"
    if not remote.startswith(prefix) or any(c in remote for c in "\0\r\n"):
        raise InstallError("Content upload is outside this app's generated banks.")
    parts = remote[len(prefix):].split("/")
    if any(p in ("", ".", "..") for p in parts):
        raise InstallError("Unsafe content upload path.")
    if parts[0] in ("quest-mod-resources", "quest-owned-game") and len(parts) > 1:
        return prefix + parts[0]
    if parts[0] == "quest-install-input" and len(parts) > 2 and re.fullmatch(r"[0-9a-f]{64}", parts[1]):
        return prefix + "/".join(parts[:2])
    raise InstallError("Content upload is outside this app's generated banks.")


def _stream_content_file(adb, address, local, remote, row, timeout, report=None, checkpoint=None):
    """Transfer, qualify and publish one file in a single shell-v2 operation.

    Exact binary EOF, size, SHA and a unique acknowledgement precede completion.
    A lost acknowledgement qualifies only this operation's staging/destination;
    it cannot resend an already published file or invalidate the entire bank.
    """
    local = Path(local); root = _content_bank_root(remote)
    if local.is_symlink() or not local.is_file() or local.stat().st_size != row["size"]:
        raise InstallError("Local content changed before binary upload.")
    token = uuid.uuid4().hex
    stage = root + "/StreamingAssets/.quest-install-upload-" + token
    ready = stage + ".ready"
    stage_q, ready_q, remote_q = map(shlex.quote, (stage, ready, remote))
    parts = remote.split("/")
    parents = list(dict.fromkeys([*["/".join(parts[:end]) for end in range(len(root.split("/")), len(parts))],
                                  root + "/StreamingAssets"]))
    checks = ["test ! -L " + shlex.quote(p) for p in parents]
    checks += ["test ! -L " + stage_q, "test ! -L " + ready_q, "test ! -L " + remote_q]
    # A partial stdin transfer has no unique post-EOF marker. Exit 76 is only
    # an empty, known incomplete operation; integrity failures remain errors.
    publication = [*checks,
        "if test -f " + stage_q + "; then",
        "test -f " + ready_q + " || exit 76",
        "test \"$(cat " + ready_q + ")\" = " + token,
        "ghvr_publish_source=" + stage_q + "; ghvr_incomplete=1",
        "else test -f " + remote_q + " || exit 76; ghvr_publish_source=" + remote_q + "; ghvr_incomplete=76; fi",
        'test "$(stat -c %s "$ghvr_publish_source")" = ' + str(row["size"]) + ' || exit "$ghvr_incomplete"',
        'test "$(sha256sum "$ghvr_publish_source" | cut -d\' \' -f1)" = ' + row["sha256"]
        + " || { if test \"$ghvr_incomplete\" = 76; then exit 76; fi; printf '%s\\n' 'Binary content checksum differs.' >&2; exit 1; }",
        "if test -f " + stage_q + "; then mv -f " + stage_q + " " + remote_q + "; fi"]
    if checkpoint:
        journal_q = shlex.quote(root + "/" + _FILE_JOURNAL)
        publication += ["test ! -L " + journal_q,
            # The leading newline isolates a previous torn append from the
            # next complete row. Readers ignore incomplete/foreign claims.
            "printf '\\n%s\\n' " + shlex.quote(json.dumps(dict(row, key=checkpoint), separators=(",", ":"))) + " >> " + journal_q]
    expected = [token, str(row["size"]), row["sha256"]]
    publication += ["rm -f " + ready_q, "printf '%s\\n' " + " ".join(expected)]
    preflight = [*checks, "mkdir -p " + shlex.quote(remote.rsplit("/", 1)[0]),
        "mkdir -p " + shlex.quote(root + "/StreamingAssets"),
        "test ! -e " + stage_q, "test ! -e " + ready_q,
        "cat > " + stage_q, "printf %s " + token + " > " + ready_q]
    script = "set -eu\n umask 022\n" + "\n".join(preflight + publication) + "\n"
    initial = local.stat(); published = False; disconnected = False

    def log_event(value):
        with adb.log.open("a", encoding="utf-8") as log: log.write(json.dumps(value) + "\n")

    def cleanup(reconnect=False):
        _content_shell(adb, address, ["test ! -L " + stage_q, "test ! -L " + ready_q,
                                     "rm -f " + stage_q + " " + ready_q], timeout=30, reconnect=reconnect)

    try:
        while True:
            log_event({"binaryContentUpload": "shell-v2", "transport": address, "local": str(local),
                       "remote": remote, "expectedBytes": row["size"], "expectedSha256": row["sha256"]})
            try:
                callback = (lambda done, total: report(done, total, remote)) if report else None
                result = adb.binary_stream(address, local, script, timeout, callback)
                message = result.stderr[-16384:]
                log_event({"binaryUploadExit": result.returncode, "stderr": message})
                if result.returncode:
                    if connection_unavailable(message):
                        raise ConnectionRequired(message + ". " + HELP, "offline-or-unauthorized")
                    raise InstallError("Binary content upload failed; completed files and saves remain retained: " + message[:1200])
                proof = getattr(result, "stdout", "").strip().splitlines()
            except (OSError, RuntimeError, subprocess.TimeoutExpired) as error:
                if isinstance(error, InstallError) and not isinstance(error, ConnectionRequired): raise
                lost = (isinstance(error, (ConnectionRequired, ConnectionError, TimeoutError, subprocess.TimeoutExpired))
                        or connection_unavailable(error))
                log_event({"binaryUploadError": str(error)[:16384], "connectionUnavailable": lost,
                           **({"binaryUploadTimeout": timeout} if isinstance(error, subprocess.TimeoutExpired) else {})})
                if not lost:
                    raise InstallError("Binary content transfer did not complete: " + str(error)[:1200]) from error
                disconnected = True
                if not getattr(adb, "wait_enabled", False):
                    detail = "Binary transfer timed out" if isinstance(error, subprocess.TimeoutExpired) else str(error)[:1200]
                    raise ConnectionRequired("The Quest connection is unavailable: " + detail + ". " + HELP,
                                             "offline-or-unauthorized") from error
                adb.wait_for_connection(address)
                # This probe replays only a qualified single-file publication,
                # never uninstall, save restoration or an arbitrary mutation.
                proof = adb.run("-s", address, "shell", "-T", "sh", "-s",
                    input_text="set -eu\n" + "\n".join(publication) + "\n", timeout=max(600, timeout),
                    check_text=False, reconnect=True, empty_exit_codes=(76,),
                    positive=r"\A" + re.escape("\n".join(expected)) + r"\s*\Z").splitlines()
                disconnected = False
                if not proof:
                    # Only an empty exit 76 reaches this branch. Preserve all
                    # committed files; remove this incomplete operation only.
                    cleanup(reconnect=True)
                    continue
            current = local.stat()
            if (initial.st_size, initial.st_mtime_ns, initial.st_ctime_ns) != (current.st_size, current.st_mtime_ns, current.st_ctime_ns):
                raise InstallError("Local content changed during binary upload.")
            if proof != expected:
                raise InstallError("Binary content differs from its exact size/checksum or operation acknowledgement; no completion receipt or launch was allowed.")
            published = True
            if report: report(row["size"], row["size"], remote)
            return
    finally:
        # A dropped connection may have left verified staging or a published
        # file. Preserve it for the same-operation reconnect/probe, never delete
        # it merely because its acknowledgement was lost.
        if not published and not disconnected:
            try: cleanup()
            except InstallError as error: log_event({"retainedUploadStaging": str(error)[:1000]})


def _prepare_content_parents(adb, address, root, rows):
    """Create only manifested parents in shell, without SYNC ownership writes.

    Capture 20261014T200458 proves shell mkdir and a small ordinary push work,
    but SYNC's secure_mkdirs rejects the first fresh StreamingAssets directory.
    AOSP's secure_mkdirs chowns directories only when it creates them; existing
    directories avoid that failing metadata operation. Prepare exact ancestors
    before the fast transfer, never discover or traverse an installed tree.
    """
    parents = {root}
    for row in rows:
        parts = row["path"].split("/")
        parents.update(root + "/" + "/".join(parts[:end]) for end in range(1, len(parts)))
    ordered = sorted(parents, key=lambda path: (path.count("/"), path))
    for offset in range(0, len(ordered), 128):
        lines = []
        for path in ordered[offset:offset + 128]:
            quoted = shlex.quote(path)
            lines += ["test ! -L " + quoted, "mkdir -p " + quoted,
                      "test ! -L " + quoted, "test -d " + quoted]
        _content_shell(adb, address, lines, timeout=30)
    for offset in range(0, len(rows), 128):
        _content_shell(adb, address, ["test ! -L " + shlex.quote(root + "/" + row["path"])
                                    for row in rows[offset:offset + 128]], timeout=30)


def _checkpoint_content_files(adb, address, root, rows, checkpoint):
    """Journal only this interrupted batch's independently qualified files."""
    if not checkpoint or not rows:
        return
    if not re.fullmatch(r"[0-9a-f]{64}", checkpoint):
        raise InstallError("Invalid content checkpoint identity.")
    journal = shlex.quote(root + "/" + _FILE_JOURNAL)
    for offset in range(0, len(rows), 128):
        lines = ["test ! -L " + journal]
        lines += ["printf '\\n%s\\n' " + shlex.quote(json.dumps(dict(row, key=checkpoint), separators=(",", ":")))
                  + " >> " + journal for row in rows[offset:offset + 128]]
        _content_shell(adb, address, lines, timeout=30)


def _push_content(adb, address, local, remote, *, timeout, rows, report=None, checkpoint=None):
    """Keep fast SYNC delivery; repair only observed ownership metadata errors."""
    local = Path(local)
    raw_addresses = getattr(adb, "_raw_content_addresses", set())
    if local.is_dir():
        if not rows or any(not _content_path(row.get("path")) for row in rows):
            raise InstallError("Directory upload requires exact manifested content paths.")
        for row in rows: _content_bank_root(remote.rstrip("/") + "/" + row["path"])
        root = _content_bank_root(remote.rstrip("/") + "/" + rows[0]["path"])
        if address not in raw_addresses:
            _prepare_content_parents(adb, address, root, rows)
    else: _content_bank_root(remote)
    interrupted_sync = False
    if address not in raw_addresses:
        try:
            adb.run("-s", address, "push", local, remote, timeout=timeout)
            return False  # The caller still owns normal sync verification.
        except InstallError as error:
            if isinstance(error, ConnectionRequired) and getattr(adb, "wait_enabled", False):
                adb.wait_for_connection(address)
                # Sync left an uncertain partial batch. Switch this attempt to
                # atomic single-file delivery instead of blindly replaying it.
            elif not re.search(r"remote (?:fchown(?:\(\))? failed(?: uid: -?[0-9]+ gid: -?[0-9]+)?|"
                               r"secure_mkdirs(?:\(\))? failed):\s*Operation not permitted", str(error)):
                raise
            else:
                print("Android rejected sync ownership metadata; using verified binary content upload.", flush=True)
            raw_addresses = {*raw_addresses, address}; adb._raw_content_addresses = raw_addresses
            interrupted_sync = True
    if local.is_dir():
        accepted = {}
        if interrupted_sync:
            # A failed recursive push can have completed earlier files. Qualify
            # only this bounded batch, then journal them before further writes;
            # do not resend accepted bytes or scan/hash the installed bank.
            sizes = _remote_content_sizes(adb, address, root, rows)
            candidates = [row for row in rows if sizes.get(row["path"]) == row["size"]]
            accepted = _matching_remote_content(adb, address, root, candidates)
            _checkpoint_content_files(adb, address, root, list(accepted.values()), checkpoint)
        for row in rows:
            target = remote.rstrip("/") + "/" + row["path"]
            if row["path"] in accepted:
                if report: report(row["size"], row["size"], target)
                continue
            while True:
                try:
                    _stream_content_file(adb, address, local.parent / row["path"], target, row, timeout, report, checkpoint)
                    break
                except ConnectionRequired:
                    if not getattr(adb, "wait_enabled", False): raise
                    adb.wait_for_connection(address)
    else:
        if len(rows) != 1: raise InstallError("A content file requires one exact upload inventory row.")
        while True:
            try:
                _stream_content_file(adb, address, local, remote, rows[0], timeout, report, checkpoint)
                break
            except ConnectionRequired:
                if not getattr(adb, "wait_enabled", False): raise
                adb.wait_for_connection(address)
    return True  # Every raw file was verified once before its atomic rename.


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
            lines.append("test ! -L " + target)
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
    _content_shell(adb, address, ["test ! -L " + shlex.quote(remote + ".upload"),
                                "test ! -L " + shlex.quote(remote)], timeout=30)
    row = {"size": local.stat().st_size, "sha256": digest(local)}
    verified = _push_content(adb, address, local, remote + ".upload", timeout=30, rows=[row])
    if not verified:
        proof = _content_shell(adb, address, ["test ! -L " + shlex.quote(remote + ".upload"),
                              "stat -c %s " + shlex.quote(remote + ".upload"),
                              "sha256sum " + shlex.quote(remote + ".upload")], timeout=30).splitlines()
        if proof != [str(row["size"]), row["sha256"] + "  " + remote + ".upload"]:
            raise InstallError("Content receipt upload did not preserve its exact bytes.")
    # Replaying after lost acknowledgement is harmless only if the destination
    # already contains this exact small receipt. Never replay an unqualified mv.
    upload_q, remote_q = shlex.quote(remote + ".upload"), shlex.quote(remote)
    _content_shell(adb, address, ["test ! -L " + upload_q, "test ! -L " + remote_q,
        "if test -f " + upload_q + "; then mv -f " + upload_q + " " + remote_q + "; else",
        "test -f " + remote_q,
        "test \"$(sha256sum " + remote_q + " | cut -d' ' -f1)\" = " + row["sha256"], "fi"], timeout=30)


def _pull_file_journal(adb, address, root, manifest, temporary):
    """Read only compact, completed per-file claims for this exact bank."""
    local = temporary / "accepted.jsonl"
    journal = root + "/" + _FILE_JOURNAL
    _content_shell(adb, address, ["test ! -L " + shlex.quote(journal)], timeout=30)
    try:
        adb.run("-s", address, "pull", journal, local, timeout=30, check_text=False, reconnect=True)
    except ConnectionRequired: raise
    except InstallError: return {}
    if not local.is_file() or local.stat().st_size > _RECEIPT_LIMIT: return {}
    wanted = {row["path"]: row for row in manifest["files"]}
    key, accepted = content_key(manifest), {}
    for line in local.read_bytes().splitlines(keepends=True):
        if not line.endswith(b"\n"): continue  # Interrupted append never owns completion.
        try: row = json.loads(line)
        except (ValueError, UnicodeError): continue
        if not isinstance(row, dict) or row.get("key") != key: continue
        path = row.get("path")
        if isinstance(path, str) and path in wanted and {k: row.get(k) for k in ("path", "size", "sha256")} == wanted[path]:
            accepted[path] = wanted[path]
    return accepted


def _content_progress(done, total, detail, *, complete=False):
    global _LAST_CONTENT_PROGRESS
    _LAST_CONTENT_PROGRESS = {"schema": 1, "phase": "install-content", "operation": "content",
          "done": done, "total": total, "unit": "bytes", "status": "complete" if complete else "progress",
          "detail": detail[:1024]}
    print("GHVRQ_PROGRESS " + json.dumps(_LAST_CONTENT_PROGRESS), flush=True)


def _install_content_tree(adb, address, source, manifest, archive, root, temporary, repair, progress_scope=None,
                          observed_receipt=_UNREAD_RECEIPT):
    rows = manifest["files"]
    members = _validate_zip_members(archive, manifest)
    base_bytes, total_bytes = progress_scope or (0, sum(row["size"] for row in rows))
    _content_shell(adb, address, ["mkdir -p " + shlex.quote(root), "test ! -L " + shlex.quote(root)], timeout=30)
    committed = (_pull_content_receipt(adb, address, root + "/" + _RECEIPT, temporary / "committed.bin")
                 if observed_receipt is _UNREAD_RECEIPT else observed_receipt)
    if (committed and committed["key"] == content_key(manifest) and not repair
            and committed["files"] == {row["path"]: row for row in rows}):
        print("Reusing completed file-backed content: " + root.rsplit("/", 1)[-1] + ".")
        _content_progress(base_bytes + sum(row["size"] for row in rows), total_bytes, "Reusing completed " + root.rsplit("/", 1)[-1])
        return {"contentKey": committed["key"], "files": len(rows), "reusedFiles": len(rows), "uploadedFiles": 0}
    inventory = {}
    for receipt in (committed, _pull_content_receipt(adb, address, root + "/" + _RECEIPT + ".previous", temporary / "previous.bin"),
                    _pull_content_receipt(adb, address, root + "/" + _RECEIPT + ".pending", temporary / "pending.bin")):
        if receipt:
            inventory.update(receipt["files"])
    inventory.update(_pull_file_journal(adb, address, root, manifest, temporary))
    # Never stat thousands of missing/unclaimed files on a first installation.
    # Existing claims need metadata only; hashing is an explicit repair action.
    claimed = [row for row in rows if inventory.get(row["path"]) == row]
    sizes = _remote_content_sizes(adb, address, root, claimed)
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
    _content_progress(base_bytes + sum(row["size"] for row in retained.values()), total_bytes,
                      "Preparing " + root.rsplit("/", 1)[-1] + "; saved content is retained")
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
            transferred = {}; accepted_bytes = sum(row["size"] for row in retained.values())
            def report(done, total, target):
                transferred[target] = done
                _content_progress(base_bytes + accepted_bytes + sum(transferred.values()), total_bytes,
                                  "Transferring and verifying game content: " + target.rsplit("/", 1)[-1])
            verified = _push_content(adb, address, stage, root + "/", timeout=transfer_timeout, rows=batch,
                                     report=report, checkpoint=content_key(manifest))
            if not verified: _verify_remote_content(adb, address, root, batch)
            retained.update((row["path"], row) for row in batch)
            _publish_content_receipt(adb, address, root, manifest, retained, temporary, _RECEIPT + ".pending")
            uploaded += len(batch)
            # Retain the log token, but qualify its bank-local count. The
            # structured counter spans BOTH banks; legacy text parsing must not
            # mistake a finished four-file mod bank for complete game delivery.
            print("Installed content files: " + str(len(retained)) + "/" + str(len(rows)) + ". Bank: " + root.rsplit("/", 1)[-1] + ".")
            _content_progress(base_bytes + sum(row["size"] for row in retained.values()), total_bytes,
                              "Verified content files: " + str(len(retained)) + "/" + str(len(rows)) + " in " + root.rsplit("/", 1)[-1])
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
    _content_shell(adb, address, ["rm -f " + shlex.quote(root + "/" + _RECEIPT + suffix) for suffix in (".pending", ".previous")]
                   + ["rm -f " + shlex.quote(root + "/" + _FILE_JOURNAL)], timeout=30)
    return {"contentKey": content_key(manifest), "files": len(rows), "reusedFiles": len(rows) - uploaded, "uploadedFiles": uploaded}


def install_file_content(adb, address, source, repair=False, *, hardware=None):
    manifest = installation_manifest(source)
    if manifest is None:
        return None
    adb.run("-s", address, "shell", "am", "force-stop", PACKAGE, timeout=30, reconnect=True)
    base = "/sdcard/Android/data/" + PACKAGE + "/files/"
    records = []
    total_bytes = sum(row["size"] for kind in ("mod", "game") for row in manifest[kind]["files"])
    completed_bytes = 0
    access_api = content_access_module()
    access_type = (access_api.BankAccess if getattr(adb, "app_storage_diagnostics", False) is True
                   else access_api.RuntimeBankAccess)
    with tempfile.TemporaryDirectory(prefix="ghvr-quest-install-") as name, zipfile.ZipFile(source.apk) as apk, \
            access_type(adb, address, signing_recovery_module().AppStorageView, hardware=hardware) as access:
        temporary = Path(name)
        mod_info = apk.getinfo("assets/quest-mod-content.zip")
        if mod_info.file_size > 4 * 1024 * 1024 * 1024:
            raise InstallError("Mod content archive exceeds its bounded size.")
        mod_path = temporary / "quest-mod-content.zip"
        for kind, path, root in (("mod", mod_path, base + "quest-mod-resources"),
                                ("game", source.content[0].path, base + "quest-owned-game")):
            _content_shell(adb, address, ["test ! -L " + shlex.quote(root)], timeout=30)
            committed = _pull_content_receipt(adb, address, root + "/" + _RECEIPT, temporary / "warm-bank.bin")
            if (committed and committed["key"] == content_key(manifest[kind]) and not repair
                    and committed["files"] == {row["path"]: row for row in manifest[kind]["files"]}):
                app_access = access.qualify(root, manifest[kind], _RECEIPT)
                print("Reusing completed file-backed content: " + root.rsplit("/", 1)[-1] + ".")
                result = {"contentKey": committed["key"], "files": len(manifest[kind]["files"]),
                          "reusedFiles": len(manifest[kind]["files"]), "uploadedFiles": 0, **app_access}
                records.append({"archive": manifest[kind]["archive"], "sha256": manifest[kind]["archiveSha256"],
                                "size": mod_info.file_size if kind == "mod" else source.content[0].size,
                                "remote": root, "fileBacked": True, **result})
                completed_bytes += sum(row["size"] for row in manifest[kind]["files"])
                _content_progress(completed_bytes, total_bytes, "Reusing completed " + root.rsplit("/", 1)[-1])
                continue
            if kind == "mod":
                with apk.open(mod_info) as origin, mod_path.open("wb") as destination:
                    shutil.copyfileobj(origin, destination, 1024 * 1024)
                if digest(mod_path) != manifest["mod"]["archiveSha256"]:
                    raise InstallError("The embedded mod bank differs from its inventory.")
            if kind == "game" and not path.exists():
                retained = require_installed_game(adb, address, source, temporary, repair=repair)
                retained.update(access.qualify(root, manifest[kind], _RECEIPT))
                records.append({"archive": manifest[kind]["archive"], "sha256": manifest[kind]["archiveSha256"],
                                "size": source.content[0].size, "remote": root, "fileBacked": True, **retained})
                completed_bytes += sum(row["size"] for row in manifest[kind]["files"])
                _content_progress(completed_bytes, total_bytes, "Reusing completed game content")
                continue
            stream = content_stream(source.content[0]) if kind == "game" else path.open("rb")
            with stream as body, zipfile.ZipFile(body) as archive:
                result = _install_content_tree(adb, address, source, manifest[kind], archive, root, temporary, repair,
                                               progress_scope=(completed_bytes, total_bytes), observed_receipt=committed)
            result.update(access.qualify(root, manifest[kind], _RECEIPT))
            completed_bytes += sum(row["size"] for row in manifest[kind]["files"])
            records.append({"archive": manifest[kind]["archive"], "sha256": manifest[kind]["archiveSha256"],
                            "size": source.content[0].size if kind == "game" else path.stat().st_size,
                            "remote": root, "fileBacked": True, **result})
    _content_progress(completed_bytes, total_bytes, "Complete game content installed and verified", complete=True)
    return records


def require_installed_game(adb, address, source, temporary, *, repair=False):
    """An APK-only update requires the exact already installed game inventory."""
    manifest = installation_manifest(source)
    if manifest is None or not source.content or repair:
        raise InstallError("An APK-only update requires installed game content. For a new installation or repair, provide the original complete content ZIP.")
    root = "/sdcard/Android/data/" + PACKAGE + "/files/quest-owned-game"
    committed = _pull_content_receipt(adb, address, root + "/" + _RECEIPT, Path(temporary) / "retained-game.bin")
    expected = manifest["game"]
    if not committed or committed["key"] != content_key(expected) or committed["files"] != {row["path"]: row for row in expected["files"]}:
        raise InstallError("This Quest does not have the complete original game content for this APK. Keep the current installation and supply its complete content ZIP or run a full build.")
    return {"contentKey": committed["key"], "files": len(expected["files"]), "reusedFiles": len(expected["files"]), "uploadedFiles": 0}


def install_content(adb, address, source, previous=None, repair=False, *, hardware=None):
    prepared = install_file_content(adb, address, source, repair=repair, hardware=hardware)
    if prepared is not None:
        return prepared
    if any(bank.package for bank in source.content):
        raise InstallError("Portable installation requires the APK's complete file-backed installation inventory. Update this older APK with the current Builder before exporting it.")
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
        verified = _push_content(adb, address, bank.path, partial, timeout=transfer_timeout,
                                 rows=[{"size": bank.size, "sha256": bank.sha256}])
        if not verified:
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
            adb.run("-s", address, "shell", "rm", "-f", remote, timeout=30, reconnect=True)


def connected_recovery(adb, address, recovery, *, detected=False):
    """Resume the signing transaction's own journal after a connection loss.

    Recovery wraps its underlying transport exception. Inspect that cause;
    never retry a data-policy choice or integrity failure as a disconnect.
    Its committed uninstall/restore states own the next permitted mutation.
    """
    while True:
        try: return recovery.recover(detected=detected)
        except RuntimeError as error:
            cause, seen, lost = error, set(), False
            while cause is not None and id(cause) not in seen:
                seen.add(id(cause))
                lost = lost or isinstance(cause, ConnectionRequired)
                cause = cause.__cause__
            if not lost or not adb.wait_enabled: raise
            adb.wait_for_connection(address)


def install_connected_apk(adb, address, source, timeout):
    """On lost APK acknowledgement, qualify only the uncertain package once."""
    while True:
        try:
            adb.run("-s", address, "install", "-r", source.apk, timeout=timeout, positive=r"^Success\s*$")
            return
        except ConnectionRequired:
            if not adb.wait_enabled: raise
            adb.wait_for_connection(address)
            paths = adb.run("-s", address, "shell", "pm", "path", "--user", "0", PACKAGE,
                            check_text=False, reconnect=True, empty_exit_codes=(1,)).splitlines()
            if len(paths) == 1 and re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", paths[0]):
                remote = paths[0][8:]
                actual = adb.run("-s", address, "shell", "sha256sum", remote,
                                 timeout=600, check_text=False, reconnect=True)
                if actual == source.sha256 + "  " + remote:
                    print("The replacement APK finished before its connection acknowledgement; continuing without reinstalling.", flush=True)
                    return


def installed_apk_witness(adb, address):
    """Observe the immutable installed APK, without reading its payload."""
    raw = adb.run("-s", address, "shell", "pm", "path", "--user", "0", PACKAGE,
                  check_text=False, reconnect=True, empty_exit_codes=(1,))
    if not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", raw): return None
    path = raw[8:]
    stamp = adb.run("-s", address, "shell", "stat", "-c", "%d:%i:%s:%Y:%Z", path,
                    check_text=False, reconnect=True)
    if not re.fullmatch(r"[0-9]+:[0-9]+:[0-9]+:-?[0-9]+:-?[0-9]+", stamp): return None
    return {"path": path, "stat": stamp}


def retain_installed_apk(adb, address, hardware, source, previous):
    """Retain an exact completed APK across host-only repairs and updates.

    Old receipts lack a physical installed-file witness: hash that one APK
    once on the headset, without pulling/reinstalling it. Later unchanged
    path/inode/size/mtime/ctime witnesses avoid another multi-GB read. A source,
    headset or physical identity change never inherits this shortcut.
    """
    if (not isinstance(previous, dict) or previous.get("deviceSerial") != hardware
            or previous.get("package") != PACKAGE or previous.get("apkSha256") != source.sha256
            or previous.get("inputKey") != source.input_key): return None
    before = installed_apk_witness(adb, address)
    if before is None or int(before["stat"].split(":")[2]) != source.apk.stat().st_size: return None
    if previous.get("installedApkWitness") != before:
        print("Qualifying the previously installed APK once; no APK or game files are transferred.", flush=True)
        result = adb.run("-s", address, "shell", "sha256sum", before["path"],
                         timeout=600, check_text=False, reconnect=True)
        if result != source.sha256 + "  " + before["path"]: return None
    if adb.identity(address) != hardware or installed_apk_witness(adb, address) != before:
        raise InstallError("The installed APK or Quest changed during reuse qualification; completed files remain retained.")
    print("Reusing the exact installed APK; no APK installation or signing migration is needed.", flush=True)
    return before


def release_retained_storage_wait(adb, address, hardware):
    """Release only a durable, source-bound wait owned by an interrupted probe.

    Ordinary installs never create a debugger wait. A previous advanced probe
    may have left one behind; reconcile its exact receipt without starting or
    attaching another process. The view retains foreign/changed selections.
    """
    marker = adb.log.parent / (".manual-storage-debug-" + hardware + ".json")
    if marker.exists():
        with signing_recovery_module().AppStorageView(adb, address, hardware) as view:
            view._reconcile_manual_debug_wait()


def repair_current_access(adb, address, hardware, source, config_directory):
    """Retry only current small-file access when the same headset reconnects."""
    api = signing_recovery_module()
    while True:
        try:
            return api.repairs_current_external_access(adb, address, hardware, source, config_directory)
        except RuntimeError as error:
            if content_access_module()._transport_failure(error) and getattr(adb, "wait_enabled", False):
                adb.wait_for_connection(address)
                continue
            if isinstance(error, api.CaptureRepairError):
                print("GHVRQ_ACTION " + json.dumps(error.action), flush=True)
            raise


def repair_installed_access(adb, address, source, *, config_directory=None, hardware=None):
    """Repair a matching installed package without installing/copying payloads."""
    manifest = installation_manifest(source)
    if manifest is None or not source.mod_build:
        raise InstallError("App-access repair requires a stamped, file-backed Quest APK.")
    record = adb.run("-s", address, "shell", "dumpsys", "package", PACKAGE,
                     timeout=30, check_text=False, reconnect=True)
    expected = "0.1.0.B" + str(source.mod_build) + "." + source.input_key[:12]
    if (re.findall(r"\bversionCode=(\d+)", record) != [str(source.mod_build)]
            or re.findall(r"\bversionName=([^\s]+)", record) != [expected]):
        raise InstallError("The installed APK differs from the selected build; app data was retained. Select the installed APK for access repair.")
    paths = adb.run("-s", address, "shell", "pm", "path", "--user", "0", PACKAGE,
                    timeout=30, check_text=False, reconnect=True).splitlines()
    if len(paths) != 1 or not re.fullmatch(r"package:/data/app/[A-Za-z0-9_./=+~-]+/base\.apk", paths[0]):
        raise InstallError("The installed APK cannot be identified for access repair.")
    # One exact APK identity read admits this exceptional legacy repair. It does
    # not scan/copy the external 11-GB bank and never accepts a version alone.
    remote = paths[0][8:]
    print("Confirming the installed APK for access repair; game content is not read or copied.", flush=True)
    actual = adb.run("-s", address, "shell", "sha256sum", remote,
                    timeout=600, check_text=False, reconnect=True)
    if actual != source.sha256 + "  " + remote:
        raise InstallError("The installed APK checksum differs from the selected APK; no app data was changed.")
    adb.run("-s", address, "shell", "am", "force-stop", PACKAGE, timeout=30, reconnect=True)
    records = []
    with tempfile.TemporaryDirectory(prefix="ghvr-quest-access-") as name, \
            content_access_module().BankAccess(adb, address, signing_recovery_module().AppStorageView, hardware=hardware) as access:
        for kind, bank in (("mod", "quest-mod-resources"), ("game", "quest-owned-game")):
            root = "/sdcard/Android/data/" + PACKAGE + "/files/" + bank
            saved = _pull_content_receipt(adb, address, root + "/" + _RECEIPT, Path(name) / (kind + ".bin"))
            if (not saved or saved["key"] != content_key(manifest[kind])
                    or saved["files"] != {row["path"]: row for row in manifest[kind]["files"]}):
                raise InstallError("The installed " + bank + " lacks its complete matching receipt. No large content verification or copy was started; use normal installation to deliver its missing files.")
            records.append({"bank": bank, **access.qualify(root, manifest[kind], _RECEIPT)})
    if config_directory is not None:
        records.append({"bank": "current-app-data", **repair_current_access(
            adb, address, hardware or adb.identity(address), source, config_directory)})
    return records


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    source = result.add_mutually_exclusive_group()
    source.add_argument("--output-root", type=Path)
    source.add_argument("--handoff", type=Path)
    source.add_argument("--apk", type=Path)
    source.add_argument("--package", type=Path, help="Single portable ZIP containing the APK and complete game content.")
    result.add_argument("--config", type=Path, default=REPO / ".planning/debug/quest3/wireless-install.json")
    result.add_argument("--adb", type=Path)
    result.add_argument("--host")
    result.add_argument("--serial")
    result.add_argument("--setup", action="store_true")
    result.add_argument("--no-launch", action="store_true")
    result.add_argument("--diagnose-app-storage", action="store_true",
                        help="Advanced, optional app-origin storage diagnostics; may temporarily use the Android debugger. Normal installation leaves app opens to ordinary game startup.")
    result.add_argument("--wait-for-connection", action="store_true",
                        help="Keep the Wizard installation paused until the same authorized Quest reconnects.")
    result.add_argument("--repair-content", action="store_true", help="Verify installed game bytes and repair missing/changed files before launch.")
    result.add_argument("--repair-app-access", action="store_true",
                        help="Repair app access to the matching already-installed APK's content; no APK install, build, or game-bank transfer.")
    result.add_argument("--recovery-data-policy", choices=("all", "saves"), default="saves",
                        help="Signing migration: retain saves/settings without copying game banks or the original APK; all is the explicit legacy full rollback policy.")
    result.add_argument("--allow-content-reinstall", metavar="TRANSACTION",
                        help="Explicitly allow required game-content reinstallation if Android cannot retain its directories by direct rename; saves/settings remain protected.")
    recovery_choice = result.add_mutually_exclusive_group()
    recovery_choice.add_argument("--manual-storage-launch", metavar="TRANSACTION",
                                 help="Wait for a user launch from the Quest library with the debugger armed before restoring retained data.")
    recovery_choice.add_argument("--reset-app-data", metavar="TRANSACTION",
                                 help="Explicitly delete this Quest transaction's app data and reinstall the completed APK; retained PC backups are not restored automatically.")
    result.add_argument("--recovery-device", metavar="SERIAL", help="Bind an explicit recovery choice to its original headset.")
    result.add_argument("--dry-run", action="store_true")
    return result


def main(argv=None, runner=None):
    global _LAST_CONTENT_PROGRESS, _INPUT_CACHE_PATH
    _LAST_CONTENT_PROGRESS = None
    _DIGEST_CACHE.clear()
    args = parser().parse_args(argv)
    log = None
    try:
        config_path = args.config.expanduser().resolve()
        if bool(args.recovery_device) != bool(args.manual_storage_launch or args.reset_app_data):
            raise InstallError("An explicit recovery choice requires its original headset identity.")
        if args.repair_app_access and (args.manual_storage_launch or args.reset_app_data):
            raise InstallError("A recovery choice needs normal installation, not access-only repair. No app data was removed.")
        _INPUT_CACHE_PATH = None if args.dry_run else config_path.parent / "qualified-inputs.json"
        config = read_json(config_path) if config_path.is_file() else {}
        source = select_source(args, config)
        reset_source_available = all(content_available(bank) for bank in source.content)
        if args.reset_app_data and not reset_source_available:
            raise InstallError("Clean reinstallation needs the complete local game-content package before deleting Quest app data. Select the original complete ZIP/output, then retry. No app data was removed; no APK rebuild is needed when that package is available.")
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
            missing_bank = any(not content_available(bank) for bank in source.content)
            print("Access-only repair: no APK installation or game-bank transfer." if args.repair_app_access
                  else "Retaining the complete game content already installed on this Quest." if missing_bank
                  else "Complete owned Campaign bank: " + str(sum(bank.size for bank in source.content)) + " bytes; installed automatically with the APK.")
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
        adb.app_storage_diagnostics = args.diagnose_app_storage or args.repair_app_access
        # Old signing recovery may offer an explicitly approved diagnostic
        # launch if direct save restoration is unavailable. Never arm that
        # debugger as an automatic fallback during ordinary installation.
        adb.storage_debugger_allowed = (args.diagnose_app_storage or args.repair_app_access
                                        or args.manual_storage_launch is not None)
        adb.wait_enabled = args.wait_for_connection
        adb.connection_config = dict(config)
        try: address, hardware = choose_device(adb, args, config)
        except ConnectionRequired:
            if not adb.wait_enabled: raise
            address, hardware = adb.wait_for_connection()
        adb.connection_config = connection_settings(config, address, hardware, executable)
        previous = None
        if receipt.is_file():
            try:
                prior = read_json(receipt)
                if prior.get("deviceSerial") == hardware and prior.get("package") == PACKAGE: previous = prior
            except (OSError, ValueError): pass
        delivery_manifest = installation_manifest(source)
        file_backed = bool(source.mod_build and source.mod_build >= 625 and delivery_manifest
                           and all(isinstance(delivery_manifest.get(kind), dict) for kind in ("mod", "game")))
        package_witness = None
        # Re-read both the receipt and APK immediately before the only app mutation.
        if resolve_source(source.kind, source.path, check_zip=False) != source:
            raise InstallError("The APK or source evidence changed during device setup; retry with the intended build.")
        adb.manual_storage_identity = {"apkSha256": source.sha256, "inputKey": source.input_key, "transaction": None}
        if args.repair_app_access:
            if args.repair_content:
                raise InstallError("Access-only repair cannot be combined with full content verification.")
            content = repair_installed_access(adb, address, source, config_directory=config_path.parent, hardware=hardware)
            if not args.no_launch:
                adb.run("-s", address, "shell", "am", "start", *signing_recovery_module().launcher_arguments(ACTIVITY), timeout=30, reconnect=True)
            write_json(config_path.parent / "app-access-last-repair.json", {
                "schema": 1, "hardwareSerial": hardware, "inputKey": source.input_key,
                "apkSha256": source.sha256, "content": content, "launched": not args.no_launch,
                "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
            print("App access repaired without APK installation or game-content transfer. Hardware startup remains to be tested.", flush=True)
            return 0
        # Budget one MiB/second plus two minutes for Android package processing.
        # Retain the small-APK floor and a bounded thirty-minute maximum.
        transfer_seconds = (source.apk.stat().st_size + 1024 * 1024 - 1) // (1024 * 1024)
        install_timeout = min(1800, max(180, 120 + transfer_seconds))
        recovery_api = signing_recovery_module()
        if args.recovery_device and args.recovery_device != hardware:
            raise InstallError("The recovery choice belongs to a different Quest. No app data was removed.")
        recovery = recovery_api.Recovery(adb, address, hardware, source, config_path.parent,
                                         install_timeout=install_timeout, decode_receipt=decode_content_receipt,
                                         data_policy=args.recovery_data_policy, allow_content_reinstall=args.allow_content_reinstall,
                                         manual_storage_launch=args.manual_storage_launch, reset_app_data=args.reset_app_data,
                                         reset_source_available=reset_source_available)
        adb.connection_operation = "apk"
        migration = connected_recovery(adb, address, recovery)
        adb.manual_storage_identity["transaction"] = migration.get("transaction") if migration else None
        release_retained_storage_wait(adb, address, hardware)
        if migration is None:
            if any(not content_available(bank) for bank in source.content):
                with tempfile.TemporaryDirectory(prefix="ghvr-quest-retained-") as temporary:
                    require_installed_game(adb, address, source, Path(temporary), repair=args.repair_content)
            package_witness = retain_installed_apk(adb, address, hardware, source, previous) if file_backed else None
            if package_witness is None:
                try:
                    install_connected_apk(adb, address, source, install_timeout)
                except SigningMismatch:
                    migration = connected_recovery(adb, address, recovery, detected=True)
        # Only the transaction owner can enable a later storage view's manual
        # launch. Arbitrary CLI input cannot grant it to another APK/headset.
        adb.manual_storage_identity = {"apkSha256": source.sha256, "inputKey": source.input_key,
                                       "transaction": migration.get("transaction") if migration else None}
        if args.diagnose_app_storage and migration and (migration.get("manualStorageLaunch") is True or migration.get("saveDataReset") is True):
            adb.manual_storage_launch = True
        # B618+ uses its build number as Android versionCode. Querying that
        # small package record confirms installation without another multi-GB
        # hash/read of the headset's APK before every hardware test.
        installed_version = None
        if source.mod_build and source.mod_build >= 618:
            # Dumpsys fields such as Dexopt's "[location is error]" are data,
            # not command failures. Require a successful exit and exact stamps.
            package_info = adb.run("-s", address, "shell", "dumpsys", "package", PACKAGE,
                                   timeout=30, check_text=False, reconnect=True)
            versions = re.findall(r"\bversionCode=(\d+)\b", package_info)
            names = re.findall(r"\bversionName=([^\s]+)", package_info)
            expected_name = "0.1.0.B" + str(source.mod_build) + "." + source.input_key[:12]
            if versions != [str(source.mod_build)] or names != [expected_name]:
                raise InstallError("Installed Android build is " + str(versions or "unknown")
                                   + "; expected B" + str(source.mod_build) + ". No launch or success receipt was written.")
            installed_version = int(versions[0])
            print("Confirmed installed build: B" + str(installed_version) + " (input " + source.input_key[:12] + ")")
            if file_backed:
                uids = re.findall(r"^\s*appId=([0-9]+)\s*$", package_info, re.M)
                if len(uids) != 1:
                    raise InstallError("The installed Quest package UID is unavailable; its APK and game content remain retained.")
                spec = importlib.util.spec_from_file_location(
                    "_ghvr_quest_android_storage", REPO / "tools/quest-installer/android_storage.py")
                module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
                adb.storage_metadata_fixup = module.StorageMetadata(adb, address, hardware, int(uids[0]))
                if package_witness is None: package_witness = installed_apk_witness(adb, address)
        content_records = install_content(adb, address, source, previous, repair=args.repair_content, hardware=hardware)
        if args.diagnose_app_storage and installation_manifest(source) is not None and not (migration and migration.get("saveDataReset") is True):
            repair_current_access(adb, address, hardware, source, config_path.parent)
        clean_previous_content(adb, address, previous, content_records)
        if not args.no_launch:
            adb.run("-s", address, "shell", "am", "start", "-W", *recovery_api.launcher_arguments(ACTIVITY),
                    timeout=45, positive=r"^Status:\s*ok\s*$", reconnect=True)
        address = adb.transport(address)
        settings = connection_settings(config, address, hardware, executable)
        settings["source"] = source.setting()
        write_json(config_path, settings)
        write_json(receipt, {**settings, "apk": str(source.apk), "apkSha256": source.sha256,
                             "package": PACKAGE, "launched": not args.no_launch,
                             "isDiagnostic": source.diagnostic, "isDummy": source.dummy,
                             "modBuild": source.mod_build, "inputKey": source.input_key,
                             "installedVersionCode": installed_version,
                             **({"installedApkWitness": package_witness} if package_witness else {}),
                             "content": content_records,
                             **({"signingRecovery": migration} if migration else {}),
                             "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())})
        recovery.complete()
        cleanup_completed_install(adb, address, hardware, config_path.parent, source)
        settings, wireless = remember_wireless_after_install(adb, address, hardware, settings)
        write_json(config_path, settings)
        completed = read_json(receipt)
        completed.update(settings, wirelessAccess=wireless)
        write_json(receipt, completed)
        print(("Installed with app data reset by explicit choice; PC backups retained" if migration and migration.get("saveDataReset") is True
               else "Installed with app data retained") + (" and launched" if not args.no_launch else "")
              + ": " + address + ". Device behavior remains for hardware testing.")
        return 0
    except ConnectionRequired as error:
        print("GHVRQ_ACTION " + json.dumps({"schema": 1, "kind": "quest-connection", "reason": error.reason}), flush=True)
        print("Quest connection required: " + str(error), file=sys.stderr)
        if log: print("ADB log: " + str(log), file=sys.stderr)
        return 1
    except (InstallError, OSError, ValueError, KeyError, TypeError, AttributeError, RuntimeError, zipfile.BadZipFile) as error:
        print("Quest installation stopped: " + str(error), file=sys.stderr)
        if log:
            print("ADB log: " + str(log), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
