"""Remove only receipt-owned, inactive Quest installer scratch artifacts.

The 2026-10-14 installation report does not establish accumulated device bytes.
Canonical game banks and accepted/pending receipts are useful resume data. This
explicit inventory therefore stats exact scratch paths from source/device-bound
host logs and the recovery journal; it never hashes or walks game payloads.
Unknown legacy parking directories remain protected, including after the host's
.ghvrq folder has been deleted. Explicit cleanup can also inventory only our
reserved UUID-upload namespace under the fixed bank StreamingAssets directories;
it checks every ancestor and retains a device/stat-bound one-shot preview.
"""
from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shlex
import stat
import uuid

PACKAGE = "dev.gloomhavenvr.quest"
EXTERNAL = "/sdcard/Android/data/" + PACKAGE
HEX32 = r"[0-9a-f]{32}"
HEX64 = r"[0-9a-f]{64}"
UPLOAD = re.compile(re.escape(EXTERNAL) + r"/files/(?:quest-owned-game|quest-mod-resources)/StreamingAssets/\.quest-install-upload-" + HEX32 + r"(?:\.ready)?\Z")
PRIVATE_STAGE = re.compile(r"/data/(?:user/0|data)/" + re.escape(PACKAGE) + r"/cache/\.quest-restore-stage-" + HEX32 + r"\Z")
JDWP = re.compile(r"cache/\.ghvrq-jdwp-" + HEX32 + r"\Z")
TERMINAL = {"complete", "reset-complete"}
MAX_LOG_BYTES = 4 * 1024 * 1024
MAX_LOGS = 24
MAX_TARGETS = 256


class CleanupError(RuntimeError):
    pass


def _physical(path):
    path = Path(path).absolute()
    for parent in (path, *path.parents):
        if parent.is_symlink():
            raise CleanupError("Device cleanup refuses a linked local ownership path.")
    return path


def _read(path, maximum):
    path = _physical(path)
    before = path.stat()
    if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > maximum:
        raise CleanupError("Device cleanup ownership evidence is not a bounded independent file.")
    data = path.read_bytes()
    if _stamp(path.stat()) != _stamp(before):
        raise CleanupError("Device cleanup ownership evidence changed while being read.")
    return data, _stamp(before)


def _stamp(row):
    return (row.st_dev, row.st_ino, row.st_size, row.st_mtime_ns, row.st_ctime_ns)


def _row(path, kind, reason, size=None, count=None):
    return {"id": hashlib.sha256((kind + "\0" + path).encode()).hexdigest()[:20],
            "kind": kind, "reason": reason, "path": path, "bytes": size, "files": count,
            "label": {"en": path, "de": path}}


class DeviceCleanup:
    def __init__(self, adb, address, hardware, directory):
        if not re.fullmatch(r"[A-Za-z0-9_-]+", str(hardware)):
            raise CleanupError("Invalid Quest cleanup identity.")
        self.adb, self.address, self.hardware = adb, address, hardware
        self.directory = _physical(directory)
        self._plan = None
        self._owned = {}
        self._witnesses = {}

    def reconnect(self, adb, address):
        if adb.identity(address) != self.hardware:
            raise CleanupError("Reconnect the same Quest before device cleanup.")
        self.adb, self.address = adb, address

    def _guard(self):
        if self.adb.identity(self.address) != self.hardware:
            raise CleanupError("The connected headset changed; no cleanup was performed.")

    def _shell(self, script, *, private=False):
        self._guard()
        command = ("run-as", PACKAGE, "sh", "-s") if private else ("sh", "-s")
        result = self.adb.run("-s", self.address, "shell", "-T", *command,
                              input_text="set -eu\n" + script + "\n", timeout=30, check_text=False)
        if not isinstance(result, str) or len(result) > 65536:
            raise CleanupError("Quest cleanup returned an invalid metadata response.")
        return result.strip()

    @staticmethod
    def _tick(progress, cancel, done, total, detail):
        if cancel: cancel()
        if progress: progress({"done": done, "total": total, "detail": detail, "unit": "tasks"})

    def _evidence(self, *, automatic=False):
        self._witnesses = {}
        targets = {}
        journal = None
        source = self.directory / "signing-recovery" / self.hardware / "active.json"
        if source.exists() or source.is_symlink():
            data, witness = _read(source, 32 * 1024 * 1024)
            journal = json.loads(data)
            if (not isinstance(journal, dict) or journal.get("schema") != 1 or journal.get("package") != PACKAGE
                    or journal.get("deviceSerial") != self.hardware
                    or not re.fullmatch(HEX32, str(journal.get("transaction", "")))
                    or not re.fullmatch(HEX64, str(journal.get("apkSha256", "")))
                    or not re.fullmatch(HEX64, str(journal.get("inputKey", "")))):
                raise CleanupError("The retained recovery journal has an invalid cleanup identity.")
            self._witnesses[source] = witness
            txn = journal["transaction"]
            targets["/data/local/tmp/.ghvr-rename-" + txn] = {"kind": "rename-helper", "private": False,
                "protected": None if journal.get("state") in TERMINAL else "pending-recovery"}
            remote = journal.get("appContextStage")
            if remote:
                if not isinstance(remote, str) or not PRIVATE_STAGE.fullmatch(remote):
                    raise CleanupError("The retained private scratch path is outside its owned scope.")
                targets[remote] = {"kind": "restore-upload", "private": True,
                    "protected": None if journal.get("state") in TERMINAL else "pending-recovery"}
        # Each log begins with an exact selected APK hash. Only its verified
        # ro.serialno command binds script tokens to this physical headset.
        logs = [] if automatic else sorted(self.directory.glob("wireless-install-*.log"), key=lambda p: p.name, reverse=True)[:MAX_LOGS]
        limited = False
        for path in logs:
            if len(targets) >= MAX_TARGETS:
                limited = True
                break
            path = _physical(path)
            before = path.stat()
            if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1:
                continue
            with path.open("rb") as stream:
                header = stream.readline(16385)
                if len(header) > 16384:
                    continue
                try: receipt = json.loads(header)
                except (ValueError, UnicodeError): continue
                if not isinstance(receipt, dict) or not re.fullmatch(HEX64, str(receipt.get("apkSha256", ""))):
                    continue
                if before.st_size > MAX_LOG_BYTES:
                    stream.seek(-MAX_LOG_BYTES, 2)
                    stream.readline()  # discard the first partial JSON line
                text = stream.read(MAX_LOG_BYTES).decode("utf-8", errors="replace")
            if _stamp(path.stat()) != _stamp(before):
                raise CleanupError("An installation is still writing its cleanup evidence.")
            lines = text.splitlines()
            addresses, scripts = set(), []
            command = None
            for index, line in enumerate(lines):
                try: value = json.loads(line)
                except ValueError: continue
                if isinstance(value, list):
                    command = value
                    if (len(value) >= 6 and value[1] == "-s" and value[-3:] == ["shell", "getprop", "ro.serialno"]
                            and index + 2 < len(lines) and lines[index + 1].strip() == '{"exitCode": 0}'
                            and lines[index + 2].strip() == self.hardware):
                        addresses.add(value[2])
                elif isinstance(value, dict) and isinstance(value.get("stdinScript"), str) and command:
                    if len(command) >= 4 and command[1] == "-s":
                        scripts.append((command[2], value["stdinScript"]))
            known = {}
            for address, script in scripts:
                if address not in addresses:
                    continue
                # Only the uploader's exclusive-create/cat stage has this exact
                # shape. Neither .upload filenames nor arbitrary game paths do.
                for candidate in re.findall(re.escape(EXTERNAL) + r"/files/[^\s'\";]+", script):
                    if not UPLOAD.fullmatch(candidate) or candidate.endswith(".ready"):
                        continue
                    token = candidate.rsplit("-", 1)[-1]
                    if ("cat > " + shlex.quote(candidate) not in script
                            or "test ! -e " + shlex.quote(candidate) not in script
                            or "printf %s " + token + " > " + shlex.quote(candidate + ".ready") not in script):
                        continue
                    known[candidate] = {"kind": "content-upload", "private": False, "protected": None}
                    known[candidate + ".ready"] = {"kind": "upload-marker", "private": False, "protected": None}
                for relative in re.findall(r"cache/\.ghvrq-jdwp-" + HEX32, script):
                    if not JDWP.fullmatch(relative) or "mkdir " + shlex.quote(relative) not in script:
                        continue
                    # Private recovery jobs may still be valuable or in flight.
                    # A terminal matching journal proves the storage owner closed.
                    terminal = bool(journal and journal.get("state") in TERMINAL
                                    and journal.get("apkSha256") == receipt["apkSha256"])
                    known["/data/user/0/" + PACKAGE + "/" + relative] = {
                        "kind": "debugger-scratch", "private": True, "tree": True,
                        "protected": None if terminal else "pending-or-unproved-recovery"}
            if known:
                self._witnesses[path] = _stamp(before)
                for candidate, owner in known.items():
                    if len(targets) >= MAX_TARGETS:
                        limited = True
                        break
                    targets.setdefault(candidate, owner)
        return targets, journal, limited

    @staticmethod
    def _parents(path):
        # /sdcard itself is Android's intentional platform alias. Everything
        # beneath that alias, and every private cache ancestor, must be ordinary.
        stop = "/sdcard" if path.startswith("/sdcard/") else "/data"
        parents = list(PurePosixPath(path).parents)
        return list(reversed([str(p) for p in parents if str(p) not in ("/", ".") and str(p) != stop]))

    def _prefix(self, path):
        lines = []
        for parent in self._parents(path):
            quoted = shlex.quote(parent)
            lines += ["if test -L " + quoted + "; then printf GHVRQ_LINK; exit 0; fi",
                      "if ! test -e " + quoted + "; then printf GHVRQ_ABSENT; exit 0; fi",
                      "test -d " + quoted]
        return lines

    def _probe(self, path, owner):
        quoted = shlex.quote(path)
        lines = self._prefix(path) + ["if test -L " + quoted + "; then printf GHVRQ_LINK; exit 0; fi",
            "if ! test -e " + quoted + "; then printf GHVRQ_ABSENT; exit 0; fi"]
        if owner.get("tree"):
            lines += ["test -d " + quoted,
                "find " + quoted + " -mindepth 1 -maxdepth 1 -print | head -n 7",
                "printf '\\nGHVRQ_TREE\\n'", "stat -c %d:%i:%s:%Y:%Z:%h " + quoted]
        else:
            lines += ["test -f " + quoted, "stat -c %d:%i:%s:%Y:%Z:%h " + quoted]
        observed = self._shell("\n".join(lines), private=owner["private"])
        if observed in ("GHVRQ_LINK", "GHVRQ_ABSENT"):
            return {"status": observed[6:].lower()}
        if owner.get("tree"):
            listing, separator, witness = observed.partition("\nGHVRQ_TREE\n")
            allowed = {path + "/" + name for name in ("job.sh", "stdout", "stderr", "status", "status.part")}
            children = listing.splitlines() if listing else []
            if not separator or len(children) > 5 or any(item not in allowed for item in children):
                return {"status": "unknown-members"}
            total, members = 0, []
            for child in children:
                item = self._probe(child, {"private": True})
                if item.get("status") != "file":
                    return {"status": "unknown-members"}
                total += item["bytes"]
                members.append({"path": child, "witness": item["witness"]})
            return {"status": "tree", "bytes": total, "files": len(members), "witness": witness, "members": members}
        if not re.fullmatch(r"[0-9]+:[0-9]+:[0-9]+:-?[0-9]+:-?[0-9]+:1", observed):
            return {"status": "unsafe-metadata"}
        return {"status": "file", "bytes": int(observed.split(":")[2]), "files": 1, "witness": observed}

    def _parked(self):
        root = "/sdcard/Download/.ghvr-quest-recovery/" + self.hardware
        prefix = self._prefix(root)
        reply = self._shell("\n".join(prefix + ["if test -L " + shlex.quote(root)
            + "; then printf GHVRQ_LINK; elif ! test -e " + shlex.quote(root)
            + "; then printf GHVRQ_ABSENT; else test -d " + shlex.quote(root)
            + "; find " + shlex.quote(root) + " -mindepth 1 -maxdepth 1 -print | head -n 65; fi"]))
        if reply == "GHVRQ_ABSENT": return []
        if reply == "GHVRQ_LINK": return [_row(root, "recovery-parking", "linked-path")]
        paths = reply.splitlines()
        if len(paths) > 64: return [_row(root, "recovery-parking", "inventory-limit")]
        if any(not re.fullmatch(re.escape(root) + "/" + HEX32, path) for path in paths):
            return [_row(root, "recovery-parking", "unknown-members")]
        return [_row(path, "recovery-parking", "unique-or-pending-recovery") for path in paths]

    def _reserved_uploads(self):
        found, unknown = {}, []
        for bank in ("quest-mod-resources", "quest-owned-game"):
            root = EXTERNAL + "/files/" + bank + "/StreamingAssets"
            quoted = shlex.quote(root)
            lines = self._prefix(root) + ["if test -L " + quoted
                + "; then printf GHVRQ_LINK; elif ! test -e " + quoted
                + "; then printf GHVRQ_ABSENT; else test -d " + quoted
                + "; find " + quoted + " -mindepth 1 -maxdepth 1 -name '.quest-install-upload-*' -print | head -n "
                + str(MAX_TARGETS + 1) + "; fi"]
            try: reply = self._shell("\n".join(lines))
            except (OSError, RuntimeError):
                unknown.append(_row(root, "content-upload", "metadata-unavailable"))
                continue
            if reply == "GHVRQ_ABSENT": continue
            if reply == "GHVRQ_LINK":
                unknown.append(_row(root, "content-upload", "linked-path"))
                continue
            paths = reply.splitlines()
            if len(paths) > MAX_TARGETS:
                unknown.append(_row(root, "content-upload", "inventory-limit"))
            for path in paths[:MAX_TARGETS]:
                if UPLOAD.fullmatch(path):
                    found[path] = {"kind": "upload-marker" if path.endswith(".ready") else "content-upload",
                                   "private": False, "protected": None}
                else:
                    unknown.append(_row(root, "content-upload", "unknown-members"))
        return found, unknown

    def preview(self, progress=None, cancel=None, *, automatic=False):
        self._plan = None
        self._guard()
        self._tick(progress, cancel, 0, 3, "Reading source-bound installer ownership receipts")
        targets, journal, limited = self._evidence(automatic=automatic)
        extra_unknown = []
        if not automatic:
            discovered, extra_unknown = self._reserved_uploads()
            # Existing reserved files take priority over historical log entries
            # that may already be absent. A bounded cleanup can then make real
            # progress even when more than one preview's limit accumulated.
            combined = {**discovered, **targets}
            limited = limited or len(combined) > MAX_TARGETS
            targets = dict(list(combined.items())[:MAX_TARGETS])
        self._tick(progress, cancel, 1, len(targets) + 3, "Checking that the Quest app is stopped")
        active = self.adb.run("-s", self.address, "shell", "pidof", PACKAGE,
                              empty_exit_codes=(1,), check_text=False).strip()
        if active and not re.fullmatch(r"[0-9]+(?: [0-9]+)*", active):
            raise CleanupError("The Quest app's current process state could not be identified.")
        plan = {"schema": 1, "planId": uuid.uuid4().hex, "deviceSerial": self.hardware,
            "bytes": 0, "files": 0, "candidates": [], "protected": [], "unknown": extra_unknown, "complete": True, "scope": "owned-installer-scratch",
            "sizeKind": "logical-file-bytes", "automatic": automatic}
        for name in ("quest-mod-resources", "quest-owned-game"):
            plan["protected"].append(_row(EXTERNAL + "/files/" + name, "installed-content", "required-game-or-resume-data"))
        plan["protected"].append(_row(EXTERNAL, "app-data", "saves-settings-and-current-content"))
        if journal:
            plan["protected"].append(_row(str(self.directory / "signing-recovery" / self.hardware / journal["transaction"]),
                                          "pc-backup", "original-save-backup"))
        self._owned = {}
        for index, (path, owner) in enumerate(targets.items(), 2):
            self._tick(progress, cancel, index, len(targets) + 3, path)
            if active or owner.get("protected"):
                plan["protected"].append(_row(path, owner["kind"], "app-running" if active else owner["protected"]))
                continue
            try: observed = self._probe(path, owner)
            except (OSError, RuntimeError) as error:
                plan["unknown"].append(_row(path, owner["kind"], "metadata-unavailable"))
                continue
            if observed["status"] == "absent":
                continue
            if observed["status"] not in ("file", "tree"):
                plan["protected"].append(_row(path, owner["kind"], observed["status"]))
                continue
            row = _row(path, owner["kind"], "inactive-owned-scratch", observed["bytes"], observed["files"])
            plan["candidates"].append(row)
            plan["bytes"] += row["bytes"]
            plan["files"] += row["files"]
            self._owned[row["id"]] = {"path": path, "owner": owner, "observed": observed}
        if not automatic:
            try: plan["protected"].extend(self._parked())
            except (OSError, RuntimeError):
                plan["unknown"].append(_row("/sdcard/Download/.ghvr-quest-recovery/" + self.hardware,
                                             "recovery-parking", "metadata-unavailable"))
        if limited:
            plan["unknown"].append(_row(str(self.directory), "ownership-evidence", "inventory-limit"))
        plan["complete"] = not bool(plan["unknown"])
        self._tick(progress, cancel, len(targets) + 3, len(targets) + 3, "Quest temporary-file inventory completed")
        self._plan = copy.deepcopy(plan)
        return plan

    def _clean_guard(self):
        self._guard()
        for path, expected in self._witnesses.items():
            path = _physical(path)
            if not path.is_file() or _stamp(path.stat()) != expected:
                raise CleanupError("Installer ownership changed; refresh the cleanup preview.")
        active = self.adb.run("-s", self.address, "shell", "pidof", PACKAGE,
                              empty_exit_codes=(1,), check_text=False).strip()
        if active:
            raise CleanupError("Close GloomhavenVR on the Quest before deleting installer temporary files.")

    def clean(self, plan, progress=None, cancel=None):
        if self._plan is None or plan != self._plan:
            raise CleanupError("Refresh the Quest cleanup preview before deleting temporary files.")
        self._clean_guard()
        result = {"schema": 1, "deviceSerial": self.hardware, "freedBytes": 0, "deletedFiles": 0,
                  "sizeKind": "logical-file-bytes",
                  "removed": [], "retained": [], "protected": copy.deepcopy(plan["protected"]),
                  "unknown": copy.deepcopy(plan["unknown"])}
        self._plan = None  # one-shot consent/ownership plan, including partial failure
        for index, row in enumerate(plan["candidates"]):
            self._tick(progress, cancel, index, len(plan["candidates"]), row["path"])
            owned = self._owned[row["id"]]
            path, owner, before = owned["path"], owned["owner"], owned["observed"]
            try:
                self._clean_guard()
                observed = self._probe(path, owner)
                if observed.get("status") == "absent":
                    continue
                if observed != before:
                    result["retained"].append(dict(row, reason="metadata-changed"))
                    continue
                lines = self._prefix(path)
                members = before.get("members") or [{"path": path, "witness": before["witness"]}]
                # Recheck every member before the first deletion. No recursive
                # rm follows unknown siblings, links, or a replaced destination.
                for member in members:
                    quoted = shlex.quote(member["path"])
                    lines += ["test ! -L " + quoted, "test -f " + quoted,
                              "test \"$(stat -c %d:%i:%s:%Y:%Z:%h " + quoted + ")\" = " + shlex.quote(member["witness"])]
                if owner.get("tree"):
                    lines += ["test ! -L " + shlex.quote(path), "test -d " + shlex.quote(path),
                              "test \"$(stat -c %d:%i:%s:%Y:%Z:%h " + shlex.quote(path) + ")\" = " + shlex.quote(before["witness"])]
                lines += ["rm -f " + shlex.quote(item["path"]) for item in members]
                if owner.get("tree"): lines.append("rmdir " + shlex.quote(path))
                lines.append("printf GHVRQ_CLEANED")
                if self._shell("\n".join(lines), private=owner["private"]) != "GHVRQ_CLEANED":
                    raise CleanupError("Quest cleanup did not acknowledge this exact temporary file.")
                result["freedBytes"] += row["bytes"]
                result["deletedFiles"] += row["files"]
                result["removed"].append(row)
            except (OSError, RuntimeError) as error:
                result["retained"].append(dict(row, reason="cleanup-unavailable"))
        self._tick(progress, cancel, len(plan["candidates"]), len(plan["candidates"]), "Quest temporary-file cleanup completed")
        result["complete"] = not bool(result["retained"] or result["unknown"])
        return result


def cleanup_after_install(adb, address, hardware, directory, source):
    """Best-effort cleanup of this completed recovery's exact tiny artifacts.

    No historical log scan, bank census, debugger setting mutation or game file
    removal runs here. The full explicit cleanup remains a separate user action.
    Missing/changed ownership is deferred and must not invalidate installation.
    Call only after storage views have closed and recovery.complete() committed.
    """
    try:
        cleanup = DeviceCleanup(adb, address, hardware, directory)
        path = cleanup.directory / "signing-recovery" / hardware / "active.json"
        if not path.exists():
            return {"schema": 1, "deviceSerial": hardware, "freedBytes": 0, "deletedFiles": 0, "complete": True}
        raw, _ = _read(path, 32 * 1024 * 1024)
        journal = json.loads(raw)
        if (not isinstance(journal, dict) or journal.get("state") not in TERMINAL
                or journal.get("apkSha256") != getattr(source, "sha256", None)
                or journal.get("inputKey") != getattr(source, "input_key", None)):
            return {"schema": 1, "deviceSerial": hardware, "freedBytes": 0, "deletedFiles": 0,
                    "complete": False, "deferred": "pending-or-different-recovery"}
        return cleanup.clean(cleanup.preview(automatic=True))
    except (OSError, ValueError, RuntimeError) as error:
        return {"schema": 1, "deviceSerial": hardware, "freedBytes": 0, "deletedFiles": 0,
                "complete": False, "deferred": "owned-scratch-cleanup-unavailable", "detail": str(error)[:512]}
