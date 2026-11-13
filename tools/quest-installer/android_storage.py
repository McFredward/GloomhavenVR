"""Reconcile Android-owned directory metadata without rebuilding or debugging.

Capture20261016 proves that shell delivery succeeded but the real app cannot
enter its mod bank. Android14 binds app-specific directories around FUSE; chmod
through the shell's FUSE alias can be a no-op. The platform's fixupAppDir method
uses the installed package UID and vold's lower-filesystem ownership/ACL repair.
Only manifested bank parents are submitted, with no payload reads or copies.
The platform also normalizes immediate children of its path ancestors; it does
not recursively inspect unknown saves. Completion acknowledges the request,
not successful app-origin opens. Existing Unity startup owns that observation.
"""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import shlex
import uuid

PACKAGE = "dev.gloomhavenvr.quest"
BASE = "/storage/emulated/0/Android/data/" + PACKAGE + "/files/"
SHELL_BASE = "/sdcard/Android/data/" + PACKAGE + "/files/"
MARKER = ".quest-storage-access-v1"
ASSET = Path(__file__).with_name("android") / "StorageAccess.dex"


class StorageMetadata:
    def __init__(self, adb, address, hardware, uid, *, asset=ASSET):
        if type(uid) is not int or not 10000 <= uid < 100000:
            raise RuntimeError("Android storage reconciliation requires this installed user0 package UID.")
        self.adb, self.address, self.hardware, self.uid = adb, address, hardware, uid
        self.asset = Path(asset)
        self.stage = None

    def _guard(self):
        if self.adb.identity(self.address) != self.hardware:
            raise RuntimeError("The Quest changed before its app storage metadata could be reconciled.")

    def _shell(self, lines, *, timeout=60):
        self._guard()
        return self.adb.run("-s", self.address, "shell", "-T", "sh", "-s",
                            input_text="set -eu\n" + "\n".join(lines) + "\n",
                            timeout=timeout, check_text=False, reconnect=True)

    @staticmethod
    def _directories(root, manifest):
        if root not in (SHELL_BASE + "quest-mod-resources", SHELL_BASE + "quest-owned-game"):
            raise RuntimeError("Android storage reconciliation refuses an unexpected content root.")
        rows = manifest.get("files") if isinstance(manifest, dict) else None
        if not isinstance(rows, list) or not 0 < len(rows) <= 32768:
            raise RuntimeError("Android storage reconciliation requires a bounded content inventory.")
        directories = {root, root + "/StreamingAssets"}
        for row in rows:
            path = row.get("path") if isinstance(row, dict) else None
            if (not isinstance(path, str) or not path.startswith("StreamingAssets/")
                    or any(c in path for c in "\x00\r\n\t\\") or len(path.encode("utf-8")) > 8192
                    or any(part in ("", ".", "..") for part in path.split("/"))):
                raise RuntimeError("Android storage reconciliation refuses a noncanonical content member.")
            directories.update(root + "/" + str(parent) for parent in PurePosixPath(path).parents
                               if str(parent) != ".")
        if len(directories) > 32768:
            raise RuntimeError("Android storage reconciliation exceeds its bounded parent inventory.")
        # Android/vold repairs each requested path's ancestors and their direct
        # children already. Submit only leaves, rather than repeating an
        # explicit request for every ancestor too. Files directly in an ancestor
        # are covered by the same platform operation without reading payloads.
        ancestors = {str(parent) for directory in directories for parent in PurePosixPath(directory).parents}
        return sorted(directories - ancestors)

    def _prepare(self, digest):
        if self.stage is not None: return
        token = uuid.uuid4().hex
        stage = "/data/local/tmp/.ghvrq-storage-" + token
        self._shell(["test ! -L /data/local/tmp", "test ! -e " + stage,
                     "umask 077", "mkdir " + stage], timeout=30)
        self.stage = stage
        target = stage + "/StorageAccess.dex"
        self._guard()
        self.adb.run("-s", self.address, "push", self.asset, target, timeout=30, reconnect=True)
        result = self._shell(["test ! -L " + target, "test -f " + target,
                              "chmod 444 " + target, "sha256sum " + target], timeout=30)
        if not re.fullmatch(digest + r"\s+" + re.escape(target) + r"\s*", result):
            raise RuntimeError("The small Android storage helper changed in transfer; no platform repair was requested.")

    def qualify(self, root, manifest, receipt):
        directories = self._directories(root, manifest)
        if receipt != ".quest-installation.receipt":
            raise RuntimeError("Android storage reconciliation refuses an unexpected receipt.")
        if not self.asset.is_file() or not 112 <= self.asset.stat().st_size <= 256 * 1024:
            raise RuntimeError("The Builder's Android storage helper is missing or invalid; keep the APK and update the Builder.")
        digest = hashlib.sha256(self.asset.read_bytes()).hexdigest()
        # A separate host repair-cache key is independent of the enclosing APK
        # update identity. No original game bytes enter this hash.
        identity = {"schema": 1, "archive": manifest["archive"], "files": manifest["files"]}
        key = hashlib.sha256(json.dumps(identity, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()
        stamp = "GHVRQ_STORAGE_ACCESS_V1 " + str(self.uid) + " " + digest + " " + key
        marker = root + "/" + MARKER
        quoted = shlex.quote(marker)
        present = self._shell(["test ! -L " + shlex.quote(root), "test ! -L " + quoted,
                               "if test -f " + quoted + "; then head -c 1024 " + quoted + "; fi"], timeout=30)
        if present == stamp:
            return {"platformStorageMetadata": "retained", "storageDirectoriesRequested": 0}
        self._prepare(digest)
        paths = [BASE + path[len(SHELL_BASE):] for path in directories]
        protocol = "GHVRQ_STORAGE_FIXUP_V1\n" + "\n".join(paths) + "\nEND\n"
        if len(protocol.encode("utf-8")) > 4 * 1024 * 1024:
            raise RuntimeError("Android storage reconciliation exceeds its bounded request size.")
        print("GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "install-content-access",
              "operation": "content", "done": 0, "total": len(paths), "unit": "directories",
              "status": "progress", "detail": "Reconciling Android directory ownership; existing game bytes are retained."}), flush=True)
        raw = self._shell(["printf '%s' " + shlex.quote(protocol)
                           + " | CLASSPATH=" + shlex.quote(self.stage + "/StorageAccess.dex")
                           + " app_process /system/bin dev.gloomhavenvr.tools.StorageAccess"], timeout=120)
        expected = ["GHVRQ_STORAGE_BEGIN\t" + str(len(paths))]
        expected += ["GHVRQ_STORAGE_DONE\t" + str(index) + "\t" + path for index, path in enumerate(paths, 1)]
        expected += ["GHVRQ_STORAGE_COMPLETE\t" + str(len(paths))]
        if raw.splitlines() != expected:
            raise RuntimeError("Android did not acknowledge the complete storage reconciliation; existing game files remain retained. " + raw[-1024:])
        self._shell(["test ! -L " + quoted, "umask 022", "printf '%s' " + shlex.quote(stamp) + " > " + quoted], timeout=30)
        print("GHVRQ_PROGRESS " + json.dumps({"schema": 1, "phase": "install-content-access",
              "operation": "content", "done": len(paths), "total": len(paths), "unit": "directories",
              "status": "complete", "detail": "Android directory reconciliation requested; real app access remains for normal startup."}), flush=True)
        return {"platformStorageMetadata": "requested", "storageDirectoriesRequested": len(paths)}

    def close(self):
        if self.stage is None: return
        stage = self.stage
        try:
            self._shell(["test ! -L " + stage, "test ! -L " + stage + "/StorageAccess.dex",
                         "rm -f " + stage + "/StorageAccess.dex", "rmdir " + stage], timeout=30)
            self.stage = None
        except RuntimeError as error:
            # Only this shell-owned tiny helper remains; no game or save data
            # is parked here. Preserve its exact location in the owned ADB log.
            with self.adb.log.open("a", encoding="utf-8") as stream:
                stream.write(json.dumps({"storageHelperCleanupPending": stage, "reason": str(error)[:512]}) + "\n")
