"""Collect private, bounded Quest diagnostics without installing or restarting an app."""
import argparse
import base64
import binascii
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import struct
import sys
import tempfile
import time
import uuid
import zipfile
import zlib

import installer

PROBE_APP_FILES = ("quest-hardware.log", "quest-hardware.log.previous", "quest-hardware-storage.json", "quest-hardware-state.json")
STARTUP_APP_FILES = ("quest-startup.log", "quest-startup.previous.log", "quest-startup-state.json")
APP_FILES = PROBE_APP_FILES + STARTUP_APP_FILES
# Internal state uses a real Linux filesystem, outside Android/data. Capture
# these fixed logs only; never enumerate Wine's prefix or transfer DLLs/saves.
PROCEDURAL_APP_FILES = {
    "quest-procedural-worker.log": "files/quest-procedural-state/procedural-worker.log",
    "quest-procedural-worker.previous.log": "files/quest-procedural-state/procedural-worker.previous.log",
    "quest-procedural-engine.log": "files/quest-procedural-state/wine-prefix/drive_c/log.txt",
}
# Each diagnostic target emits its own files; absence is an explicit availability
# gap, while a known-present file that cannot be transferred remains an error.
OPTIONAL_APP_FILES = APP_FILES
REMOTE_FILES = "/sdcard/Android/data/" + installer.PACKAGE + "/files"
MAX_FILE = 2 * 1024 * 1024
MAX_LOGCAT = 4 * 1024 * 1024
MAX_METADATA = 128 * 1024
# B613's 3000 global lines covered roughly two seconds of OS traffic and
# missed Unity startup. Query the retained ring separately with fixed tags,
# then retain only the selected app's context, including terminated processes.
MAX_HISTORY = 4 * 1024 * 1024
MAX_HISTORY_LINES = 8000
MAX_HISTORY_PIDS = 8
HISTORY_TAGS = ("Unity:V", "AndroidRuntime:E", "ActivityManager:I")
DELIVERY_FILES = ("quest-mod-content.zip.download", "quest-mod-resources/StreamingAssets/gloomhavenvr.bundle")
THREADTIME = re.compile(r"^\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?\s+(\d+)\s+\d+\s+[VDIWEF]\s+([^:]+):\s?(.*)$")
APP_LOG_PREFIXES = ("[Quest startup]", "[GloomhavenVR Quest]", "[GloomhavenVR]")


def menu_thumbnail_png(pixels):
    """Retain the bounded producer sample; PNG rows run from top to bottom."""
    if len(pixels) != 32 * 16 * 4:
        raise ValueError("Menu sample is not the fixed 32x16 RGBA8 layout.")
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))
    rows = b"".join(b"\0" + pixels[row * 128:(row + 1) * 128] for row in range(15, -1, -1))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 32, 16, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(rows)) + chunk(b"IEND", b""))


def retain_menu_thumbnails(capture):
    """Decode only the current startup log; no additional headset/ADB queries."""
    log = capture.directory / "quest-startup.log"
    if not log.is_file() or log.stat().st_size > MAX_FILE: return
    text = log.read_text(encoding="utf-8", errors="replace")
    banner = re.search(r"\[Quest startup\]\s+ModBuild=(\d+)\s+input=([0-9a-f]{64})\b", text)
    if not banner: return
    installed = capture.manifest.get("app", {})
    if installed.get("modBuild") and (int(banner[1]) != installed["modBuild"]
            or not banner[2].startswith(installed.get("inputKeyPrefix", "unavailable"))):
        capture.manifest["presentationThumbnailNote"] = "Current log differs from installed build; its images were not decoded."
        return
    pattern = (r"\[Quest startup\] presentation menu-thumbnail scene=(MainMenu(?:_gamepad)?) sample=2"
               r" role=(glass|background-left|background-right) target=[^\r\n ]+"
               r" width=32 height=16 format=RGBA8-linear bottomRowFirst=true base64=([A-Za-z0-9+/=]{1,3000})(?:\r?\n|$)")
    decoded = set()
    for match in re.finditer(pattern, text):
        scene, role, encoded = match.groups()
        if role in decoded: continue
        try:
            pixels = base64.b64decode(encoded, validate=True)
            png = menu_thumbnail_png(pixels)
        except (ValueError, binascii.Error):
            capture.manifest.setdefault("presentationThumbnailsUnavailable", []).append({"role": role, "reason": "Invalid bounded RGBA8 record."})
            continue
        name = "quest-menu-" + role + ".png"
        path = capture.directory / name; path.write_bytes(png)
        capture.manifest["files"].append({"path": name, "kind": "decoded-menu-thumbnail", "bytes": len(png),
            "sha256": installer.digest(path), "sourceFile": log.name, "scene": scene,
            "modBuild": int(banner[1]), "inputKey": banner[2], "width": 32, "height": 16,
            "note": "Downsampled native capture pixels; not an eye image or final color/readability proof."})
        decoded.add(role)


def app_history(output, current_pids):
    """Select owned Unity/crash context from a bounded, tagged threadtime dump."""
    package = re.escape(installer.PACKAGE)
    start = re.compile(r"\bStart proc (\d+):" + package + r"(?=[/\s]|$)")
    any_start = re.compile(r"\bStart proc (\d+):([^/\s]+)")
    crash = re.compile(r"^Process:\s*" + package + r",\s*PID:\s*(\d+)\b")
    records, observed, sources = [], [], {}
    for line in output.splitlines():
        parsed = THREADTIME.fullmatch(line)
        if parsed is None:
            continue
        pid, tag, message = parsed.groups()
        tag = tag.strip()
        identified = None
        source = None
        if tag == "ActivityManager":
            found = start.search(message)
            if found:
                identified, source = found.group(1), "package-process-start"
        elif tag == "AndroidRuntime":
            found = crash.match(message)
            if found:
                identified, source = found.group(1), "package-crash"
        elif tag == "Unity" and message.startswith(APP_LOG_PREFIXES):
            identified, source = pid, "owned-log-prefix"
        process_start = any_start.search(message) if tag == "ActivityManager" else None
        records.append((line, pid, tag, identified, process_start))
        if identified:
            # Repeated banners must not consume the bounded process history.
            if identified in observed:
                observed.remove(identified)
            observed.append(identified)
            sources[identified] = source
    for pid in current_pids:
        if pid in observed:
            observed.remove(pid)
        observed.append(pid)
        sources[pid] = "current-process-query"
    selected = observed[-MAX_HISTORY_PIDS:]
    wanted = set(selected)
    active = set(wanted)
    lines = []
    for line, pid, tag, identified, process_start in records:
        if process_start and process_start.group(1) in wanted:
            started_pid = process_start.group(1)
            if process_start.group(2) == installer.PACKAGE:
                active.add(started_pid)
            else:
                # A retained OS event can prove that a PID was reused by another
                # app. Do not carry our earlier process identity into its logs.
                active.discard(started_pid)
        if identified in wanted and tag in ("Unity", "AndroidRuntime"):
            active.add(identified)
        if (tag in ("Unity", "AndroidRuntime") and pid in active) or (tag == "ActivityManager" and identified in wanted):
            lines.append(line)
    truncated = len(lines) > MAX_HISTORY_LINES
    if truncated:
        # Keep first initialization AND final events rather than dropping the
        # beginning again after a noisy app-specific stream reaches its cap.
        early = MAX_HISTORY_LINES // 2
        late = MAX_HISTORY_LINES - early - 1
        lines = lines[:early] + ["[Quest capture] App history middle omitted at bounded line limit."] + (lines[-late:] if late else [])
    content = "\n".join(lines) + ("\n" if lines else "")
    bytes_truncated = len(content.encode("utf-8")) > MAX_HISTORY
    if bytes_truncated:
        content = content.encode("utf-8")[:MAX_HISTORY].decode("utf-8", errors="ignore")
    details = {"processIds": selected, "processSources": {pid: sources[pid] for pid in selected},
               "retainedLines": len(content.splitlines()), "middleOmitted": truncated, "bytesTruncated": bytes_truncated,
               "note": "Retained logcat ring only; evicted startup events cannot be recovered. Selected app processes can be historical."}
    return content, details


def choose_capture_device(adb, args, config):
    """Reuse verified Wi-Fi or read USB directly; collection never enables TCP/IP."""
    expected = config.get("deviceSerial")
    address = args.host or config.get("endpoint")
    if address and not args.setup and not args.serial:
        address = installer.endpoint(address)
        try:
            return address, adb.connect(address, expected), "wifi"
        except installer.InstallError as error:
            if "different headset" in str(error) or "not a recognized Meta Quest" in str(error):
                raise
            print("Wi-Fi reconnect unavailable; checking a connected Quest for read-only collection.")
    devices = adb.devices()
    candidates = [serial for serial, state in devices.items() if state == "device"]
    if args.serial:
        if args.serial not in candidates:
            raise installer.InstallError("The requested Quest serial is unavailable or unauthorized. " + installer.HELP)
        candidates = [args.serial]
    if args.setup:
        candidates = [serial for serial in candidates if ":" not in serial and not serial.startswith("adb-")]
    quests = []
    for serial in candidates:
        try:
            quests.append((serial, adb.identity(serial)))
        except installer.InstallError as error:
            if "not a recognized Meta Quest" not in str(error):
                raise
    if not quests:
        raise installer.InstallError("No authorized Quest is available for collection. " + installer.HELP)
    if len({hardware for _, hardware in quests}) != 1:
        raise installer.InstallError("Multiple Quests are connected. Choose the intended headset with --serial SERIAL.")
    quests.sort(key=lambda item: ":" in item[0] or item[0].startswith("adb-"))
    serial, hardware = quests[0]
    if expected and hardware != expected and not args.setup:
        raise installer.InstallError("The connected Quest differs from the remembered headset. Use --setup --serial SERIAL for the intended USB Quest.")
    transport = "wifi" if ":" in serial or serial.startswith("adb-") else "usb"
    return serial, hardware, transport


class Capture:
    def __init__(self, adb, serial, directory, manifest):
        self.adb, self.serial, self.directory, self.manifest = adb, serial, directory, manifest

    def read(self, *arguments, limit=MAX_METADATA, timeout=20, include_stderr=False):
        """Keep partial output on failure; content can legitimately contain 'Error'."""
        command = [self.adb.executable, "-s", self.serial, *map(str, arguments)]
        error = None
        try:
            result = self.adb.runner(command, capture_output=True, text=True, timeout=timeout,
                                     encoding="utf-8", errors="replace", shell=False)
            output = result.stdout or ""
            stderr = result.stderr or ""
            if result.returncode:
                error = "exit=" + str(result.returncode) + " " + (stderr.strip() or output.strip())[:500]
            elif re.match(r"^(adb: error:|error:|run-as:|head:)", stderr.strip(), re.I):
                error = stderr.strip()[:500]
        except subprocess.TimeoutExpired as failure:
            output = failure.stdout or ""
            if isinstance(output, bytes):
                output = output.decode("utf-8", errors="replace")
            stderr, error = "", "command timed out"
        except OSError as failure:
            output, stderr, error = "", "", str(failure)
        if include_stderr:
            # Windows platform-tools prints successful pull receipts on stderr.
            # Include that stream only for command status, never in app/log data.
            output += ("\n" if output and stderr else "") + stderr
            if not error and re.search(r"^\s*(adb:\s*error:|error:|failed to\b)", output, re.I | re.M):
                error = "ADB reported a transfer error: " + output.strip()[:500]
        if len(output.encode("utf-8")) > limit:
            output = output.encode("utf-8")[:limit].decode("utf-8", errors="ignore")
            error = (error + "; " if error else "") + "output truncated at " + str(limit) + " bytes"
        with self.adb.log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps({"command": command, "error": error, "stderr": stderr[:500]}) + "\n")
        return output, error

    def failure(self, item, reason):
        self.manifest["errors"].append({"item": item, "reason": str(reason)[:800]})

    def save(self, name, content, kind):
        path = self.directory / name
        if isinstance(content, bytes):
            path.write_bytes(content)
        else:
            path.write_text(content, encoding="utf-8")
        self.manifest["files"].append({"path": name, "kind": kind, "bytes": path.stat().st_size,
                                       "sha256": installer.digest(path)})

    def app_file(self, name):
        if name not in APP_FILES:
            raise installer.InstallError("App diagnostic filename is not allowlisted")
        target = self.directory / name
        remote = REMOTE_FILES + "/" + name
        failures = []
        known_present = False
        try:
            size = self.adb.run("-s", self.serial, "shell", "stat", "-c", "%s", remote, timeout=10)
            if not size.isdigit():
                raise installer.InstallError("Remote file size was not a number")
            expected_size = int(size)
            known_present = True
            if expected_size == 0:
                self.failure(name, "remote file is empty")
                return
            if expected_size > MAX_FILE:
                self.failure(name, "remote file exceeds the bounded capture limit")
                return
            pulled, pull_error = self.read("pull", remote, target, limit=4096, timeout=30, include_stderr=True)
            if pull_error or not re.search(r"\b1 file pulled,\s*0 skipped\b", pulled, re.I):
                raise installer.InstallError(pull_error or "ADB pull did not confirm success")
            if not target.is_file() or target.stat().st_size > MAX_FILE:
                raise installer.InstallError("Pulled file was missing or too large")
            if target.stat().st_size != expected_size:
                raise installer.InstallError("Pulled file size did not match the remote size probe")
            reported_size = re.search(r"\((\d+) bytes in\b", pulled)
            if reported_size and int(reported_size.group(1)) != expected_size:
                raise installer.InstallError("ADB pull receipt byte count did not match the file")
            self.manifest["files"].append({"path": name, "kind": "app-file-pull", "bytes": target.stat().st_size,
                                           "sha256": installer.digest(target)})
            return
        except (installer.InstallError, OSError) as error:
            target.unlink(missing_ok=True)
            failures.append(str(error))
        # Fixed allowlisted paths only. run-as can be unavailable in non-debuggable
        # players; keep logcat and report that failure instead of changing the app.
        for path in (remote, "files/" + name):
            content, error = self.read("exec-out", "run-as", installer.PACKAGE, "head", "-c", MAX_FILE + 1, path, limit=MAX_FILE)
            if not error and not re.match(r"^(run-as:|head:|Error:)", content):
                self.save(name, content, "app-file-run-as")
                return
            failures.append(error or content[:300])
        reason = "; ".join(failures)
        if name in OPTIONAL_APP_FILES and not known_present:
            # Probe and original-game players emit different diagnostic files.
            # Retain unavailable targets without claiming their contents exist.
            self.manifest.setdefault("optionalFilesUnavailable", []).append({"item": name, "reason": reason[:800]})
        else:
            self.failure(name, reason)

    def procedural_file(self, name):
        """Read one fixed private log as bytes through diagnostic APK run-as."""
        if name not in PROCEDURAL_APP_FILES:
            raise installer.InstallError("Internal procedural diagnostic filename is not allowlisted")
        remote = PROCEDURAL_APP_FILES[name]
        output, error = self.read("exec-out", "run-as", installer.PACKAGE, "stat", "-c", "%s", remote, limit=256, timeout=10)
        size = output.strip()
        if error or not re.fullmatch(r"\d{1,19}", size) or int(size) > 9223372036854775807:
            # Old probes/startup builds and non-debuggable APKs cannot expose
            # these files. Their existing captures remain complete and useful.
            self.manifest.setdefault("proceduralFilesUnavailable", []).append({"item": name, "reason": (error or "Optional internal size query returned no usable byte count.")[:800]})
            return
        source_size = int(size)
        retained = min(source_size, MAX_FILE)
        content, error, stderr = b"", None, ""
        if retained:
            command = [self.adb.executable, "-s", self.serial, "exec-out", "run-as", installer.PACKAGE, "head", "-c", str(retained), remote]
            try:
                # Preserve Windows/ANSI log bytes. The host need not know the
                # original engine's encoding to retain an exact diagnostic file.
                result = self.adb.runner(command, capture_output=True, text=False, timeout=20, shell=False)
                content = result.stdout or b""
                stderr = (result.stderr or b"").decode("utf-8", errors="replace")
                if result.returncode or re.match(r"^(adb: error:|error:|run-as:|head:)", stderr.strip(), re.I):
                    error = "exit=" + str(result.returncode) + " " + (stderr.strip() or content[:500].decode("utf-8", errors="replace"))[:500]
                elif len(content) != retained:
                    error = "Internal log transfer byte count did not match its bounded size probe."
            except subprocess.TimeoutExpired:
                error = "Internal log transfer timed out."
            except OSError as failure:
                error = str(failure)
            with self.adb.log.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps({"command": command, "error": error, "stderr": stderr[:500]}) + "\n")
        if error:
            # A successful size probe proved presence. Do not disguise a failed
            # transfer as the optional absence of an older diagnostic target.
            self.failure(name, error)
            return
        self.save(name, content, "procedural-file-run-as")
        self.manifest["files"][-1].update(sourceBytes=source_size, truncated=source_size > retained,
                                          retained="first-bytes", sourcePath=remote)


def local_provenance(config_path, capture):
    rows = []
    for kind, path in (("last-install", config_path.parent / "wireless-last-install.json"),
                       ("local-handoff", config_path.parent / "handoff.json")):
        if not path.is_file():
            continue
        try:
            value = installer.read_json(path)
            details = value.get("details", value)
            row = {"kind": kind, "sourceFile": path.name}
            for key in ("apkSha256", "certificateSha256", "package", "isDiagnostic", "isDummy", "completedUtc", "modBuild", "installedVersionCode"):
                if key in details:
                    row[key] = details[key]
            report = details.get("buildReport", {})
            row["inputKey"] = details.get("inputKey") or report.get("inputKey")
            rows.append(row)
        except (OSError, ValueError, KeyError, TypeError, AttributeError, installer.InstallError) as error:
            capture.failure(kind, error)
    return rows


def collect(capture, config_path):
    manifest = capture.manifest
    properties = {}
    for key in ("ro.product.model", "ro.product.name", "ro.build.version.release", "ro.build.version.incremental", "ro.build.fingerprint"):
        text, error = capture.read("shell", "getprop", key)
        properties[key] = text.strip()
        if error:
            capture.failure(key, error)
    manifest["device"]["properties"] = properties
    pids, pid_error = capture.read("shell", "pidof", installer.PACKAGE)
    manifest["app"] = {"package": installer.PACKAGE, "processIds": re.findall(r"\b\d+\b", pids),
                       "processQueryError": pid_error, "installedApks": []}
    output, error = capture.read("shell", "logcat", "-d", "-t", "3000", "-b", "main", "-b", "crash", "-v", "threadtime", limit=MAX_LOGCAT, timeout=30)
    if output.strip():
        capture.save("logcat.txt", output, "recent-main-crash-logcat")
    if error:
        capture.failure("logcat", error)
    history, history_error = capture.read("shell", "logcat", "-d", "-s", "-b", "main", "-b", "crash", "-v", "threadtime",
                                          *HISTORY_TAGS, limit=MAX_HISTORY, timeout=30)
    current_pids = manifest["app"]["processIds"] if not pid_error and re.fullmatch(r"\s*\d+(?:\s+\d+)*\s*", pids) else []
    selected_history, history_details = app_history(history, current_pids)
    manifest["logcatHistory"] = history_details
    if selected_history:
        capture.save("app-logcat-history.txt", selected_history, "owned-app-logcat-history")
    if history_error:
        capture.failure("app-logcat-history", history_error)
    package, error = capture.read("shell", "dumpsys", "package", installer.PACKAGE)
    capture.save("package.txt", package, "installed-package-query")
    if error:
        capture.failure("package", error)
    manifest["app"]["versionName"] = next(iter(re.findall(r"\bversionName=([^\s]+)", package)), None)
    manifest["app"]["versionCode"] = next(iter(re.findall(r"\bversionCode=(\d+)", package)), None)
    stamp = re.fullmatch(r"0\.1\.0\.B(\d+)\.([0-9a-f]{12})", manifest["app"]["versionName"] or "")
    if stamp and stamp.group(1) == manifest["app"]["versionCode"]:
        manifest["app"]["modBuild"] = int(stamp.group(1))
        manifest["app"]["inputKeyPrefix"] = stamp.group(2)
    paths, error = capture.read("shell", "pm", "path", installer.PACKAGE)
    if error:
        capture.failure("installed-apk-path", error)
    for line in paths.splitlines()[:4]:
        match = re.fullmatch(r"package:(/data/app/[A-Za-z0-9_./+=~-]+\.apk)", line.strip())
        if not match or ".." in PurePosixPath(match.group(1)).parts:
            capture.failure("installed-apk-path", "Unexpected installed APK path; no file query was sent.")
            continue
        path = match.group(1)
        info = {"path": path}
        for command, key in (("stat", "bytes"), ("sha256sum", "sha256")):
            args = ("shell", command, "-c", "%s", path) if command == "stat" else ("shell", command, path)
            text, error = capture.read(*args, timeout=30)
            pattern = r"^(\d+)\s*$" if key == "bytes" else r"^([0-9a-f]{64})(?:\s|$)"
            found = re.match(pattern, text.strip())
            if not error and found:
                info[key] = int(found.group(1)) if key == "bytes" else found.group(1)
            else:
                capture.failure("installed-apk-" + key, error or "The optional query returned no usable value.")
        manifest["app"]["installedApks"].append(info)
    for name in APP_FILES:
        capture.app_file(name)
    for name in PROCEDURAL_APP_FILES:
        capture.procedural_file(name)
    retain_menu_thumbnails(capture)
    # Stat only these two fixed delivery paths. No archive, bundle, save or
    # profile content is transferred, and absence remains optional for old apps.
    manifest["startupDelivery"] = []
    for relative in DELIVERY_FILES:
        text, error = capture.read("shell", "stat", "-c", "%s", REMOTE_FILES + "/" + relative, limit=256, timeout=10)
        size = text.strip()
        row = {"path": relative, "available": False}
        if not error and re.fullmatch(r"\d{1,19}", size) and int(size) <= 9223372036854775807:
            row.update(available=True, bytes=int(size))
        else:
            row["reason"] = (error or "Optional size query returned no usable byte count.")[:256]
        manifest["startupDelivery"].append(row)
    manifest["localProvenance"] = local_provenance(config_path, capture)
    banners = []
    for name in ("logcat.txt", "app-logcat-history.txt", "quest-hardware.log", "quest-hardware.log.previous", "quest-startup.log", "quest-startup.previous.log"):
        path = capture.directory / name
        if path.is_file():
            for build, key in re.findall(r"\[(?:GloomhavenVR Quest|Quest startup)\]\s+ModBuild=(\d+)\s+input=([0-9a-f]{64})\b", path.read_text(encoding="utf-8", errors="replace"))[-8:]:
                banners.append({"file": name, "modBuild": int(build), "inputKey": key})
    manifest["observedAppBanners"] = banners
    for row in manifest["localProvenance"]:
        row["matchesInstalledApkHash"] = bool(row.get("apkSha256") and any(item.get("sha256") == row["apkSha256"] for item in manifest["app"]["installedApks"]))
        row["matchesObservedBannerInput"] = bool(row.get("inputKey") and any(item["inputKey"] == row["inputKey"] for item in banners))
        row["matchesInstalledAndroidBuild"] = (row.get("modBuild") == manifest["app"].get("modBuild")
            and bool(row.get("inputKey")) and row["inputKey"].startswith(manifest["app"].get("inputKeyPrefix", "unavailable")))
    manifest["provenanceNote"] = "Local receipts describe PC artifacts. Observed log banners can be historical; installed APK hashes are queried separately."


def parser():
    result = argparse.ArgumentParser(description=__doc__)
    result.add_argument("--output-root", type=Path, default=installer.REPO / ".planning/debug/quest3/captures")
    result.add_argument("--config", type=Path, default=installer.REPO / ".planning/debug/quest3/wireless-install.json")
    result.add_argument("--adb", type=Path)
    result.add_argument("--host")
    result.add_argument("--serial")
    result.add_argument("--setup", action="store_true", help="Prefer authorized USB directly; do not change Wi-Fi or remembered settings.")
    return result


def main(argv=None, runner=None):
    args = parser().parse_args(argv)
    try:
        config_path = args.config.expanduser().resolve()
        config = installer.read_json(config_path) if config_path.is_file() else {}
        executable = installer.adb_path(args.adb, config.get("adb"))
        output = args.output_root.expanduser().resolve()
        output.mkdir(parents=True, exist_ok=True)
        stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime()) + "-" + uuid.uuid4().hex[:8]
        archive = output / ("quest-capture-" + stamp + ".zip")
        with tempfile.TemporaryDirectory(prefix="capture-", dir=output) as temporary:
            directory = Path(temporary)
            adb = installer.Adb(executable, directory / "adb-commands.log", runner)
            serial, hardware, transport = choose_capture_device(adb, args, config)
            manifest = {"schema": 1, "capturedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "device": {"adbSerial": serial, "hardwareSerial": hardware, "transport": transport},
                        "limits": {"appFileBytes": MAX_FILE, "logcatBytes": MAX_LOGCAT, "logcatLines": 3000,
                                   "appHistoryBytes": MAX_HISTORY, "appHistoryLines": MAX_HISTORY_LINES, "appHistoryProcesses": MAX_HISTORY_PIDS,
                                   "startupDeliveryPaths": len(DELIVERY_FILES), "proceduralFileBytes": MAX_FILE,
                                   "proceduralPaths": len(PROCEDURAL_APP_FILES)}, "files": [], "errors": []}
            capture = Capture(adb, serial, directory, manifest)
            collect(capture, config_path)
            usable = any(row["bytes"] and row["kind"] in ("recent-main-crash-logcat", "owned-app-logcat-history", "app-file-pull", "app-file-run-as", "procedural-file-run-as") for row in manifest["files"])
            manifest["status"] = "partial" if manifest["errors"] else "complete"
            if not usable:
                manifest["status"] = "no-readable-logs"
            installer.write_json(directory / "manifest.json", manifest)
            with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as zipped:
                for path in sorted(directory.iterdir()):
                    zipped.write(path, path.name)
        print("Private Quest capture saved: " + str(archive))
        if manifest.get("app", {}).get("modBuild"):
            print("Observed installed Android build: B" + str(manifest["app"]["modBuild"])
                  + " (input " + manifest["app"]["inputKeyPrefix"] + ")")
        print("Collection: " + manifest["status"] + "; " + str(len(manifest["errors"])) + " reported errors; " + str(len(manifest.get("optionalFilesUnavailable", []))) + " optional files unavailable. No upload was made.")
        return 0 if usable else 1
    except (installer.InstallError, OSError, ValueError, TypeError, KeyError, AttributeError, RuntimeError) as error:
        print("Quest log collection stopped: " + str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
