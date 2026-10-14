"""Separate ordinary content delivery from optional app-context diagnostics.

Ordinary installation qualifies transferred bytes and compact receipts in its
caller. Actual app access is checked by the existing Unity startup at its own
Application.persistentDataPath (Android getExternalFilesDir). A failed run-as
view is not proof that the real app cannot read that scoped-storage directory.
Do not start or attach a debugger just to repeat that check from the PC.

BankAccess remains an explicitly selected diagnostic/legacy access-repair tool.
Its sampled app-origin opens and metadata-only repair never claim that shell
readability proves app readability. Neither mode scans unmanifested save data.
"""
from pathlib import PurePosixPath
import shlex
import uuid


PACKAGE = "dev.gloomhavenvr.quest"
BASE = "/sdcard/Android/data/" + PACKAGE + "/files/"


def _transport_failure(error):
    seen = set()
    while error is not None and id(error) not in seen:
        seen.add(id(error))
        if type(error).__name__ == "ConnectionRequired": return True
        error = error.__cause__
    return False


class RuntimeBankAccess:
    """Defer app-origin opens to normal startup without weakening delivery proofs."""
    def __init__(self, adb, address, view_factory=None, *, hardware=None):
        self.adb, self.address = adb, address
        self.hardware = hardware or adb.identity(address)

    def __enter__(self): return self

    def __exit__(self, kind, error, traceback): return False

    def qualify(self, root, manifest, receipt):
        if (root not in (BASE + "quest-mod-resources", BASE + "quest-owned-game")
                or receipt != ".quest-installation.receipt"
                or not isinstance(manifest, dict) or not isinstance(manifest.get("files"), list)
                or not manifest["files"]):
            raise RuntimeError("Runtime content access refuses an unexpected delivery boundary.")
        while True:
            try:
                if self.adb.identity(self.address) != self.hardware:
                    raise RuntimeError("The headset changed before content delivery completed.")
                # Never present this deferred observation as an app-origin
                # verification. The caller still owns real transfer/integrity
                # checks and the game opens the files through its own Context.
                return {"appAccess": "runtime-pending", "appAccessVerified": False, "accessRepairFiles": 0}
            except RuntimeError as error:
                if not _transport_failure(error) or not getattr(self.adb, "wait_enabled", False): raise
                self.adb.wait_for_connection(self.address)


class BankAccess:
    def __init__(self, adb, address, view_factory, *, hardware=None):
        self.adb, self.address = adb, address
        self.view = view_factory(adb, address, hardware or adb.identity(address))
        self.mode = None

    def __enter__(self):
        return self

    def __exit__(self, kind, error, traceback):
        return self.view.__exit__(kind, error, traceback)

    def _root(self, root):
        if root not in (BASE + "quest-mod-resources", BASE + "quest-owned-game"):
            raise RuntimeError("App content access refuses an unexpected bank root.")
        if self.mode is None:
            failure = None
            for mode in ("app-direct", "app-process", "app-jdwp"):
                try:
                    parent = self.view._access_root(BASE.rstrip("/"), mode)
                    self.view._context_shell("test -d " + shlex.quote(parent)
                                             + "\ntest -x " + shlex.quote(parent), mode)
                    self.mode = mode
                    break
                except RuntimeError as error:
                    if _transport_failure(error): raise
                    failure = error
            if self.mode is None:
                raise RuntimeError("The app's own Android content view is unavailable; "
                                   "installed game data is retained. " + str(failure)) from failure
        self.view._device_guard()
        return self.view._access_root(root, self.mode)

    def _probe(self, root, rows, receipt):
        actual = self._root(root)
        self.view._check_external_root(actual, self.mode)
        # The manifested ancestor paths are known; no recursive tree walk.
        sampled = list(dict.fromkeys(rows[i]["path"] for i in (0, len(rows) // 2, len(rows) - 1)))
        token = uuid.uuid4().hex
        lines = []
        directories = {actual, actual + "/StreamingAssets"}
        for relative in sampled:
            directories.update(actual + "/" + str(parent) for parent in PurePosixPath(relative).parents
                               if str(parent) != ".")
        for directory in sorted(directories):
            quoted = shlex.quote(directory)
            lines += ["test ! -L " + quoted, "test -d " + quoted, "test -r " + quoted, "test -x " + quoted]
        for relative in [receipt, *sampled]:
            quoted = shlex.quote(actual + "/" + relative)
            lines += ["test ! -L " + quoted, "test -f " + quoted, "test -r " + quoted,
                      "head -c 1 " + quoted + " >/dev/null"]
        lines.append("printf '%s' " + shlex.quote(token))
        if self.view._context_shell("\n".join(lines), self.mode) != token:
            raise RuntimeError("App content access did not acknowledge its bounded open checks.")

    def qualify(self, root, manifest, receipt):
        while True:
            try:
                return self._qualify(root, manifest, receipt)
            except RuntimeError as error:
                if not _transport_failure(error) or not getattr(self.adb, "wait_enabled", False): raise
                self.adb.wait_for_connection(self.address)
                self.view._close_storage_process()
                self.mode = None

    def _qualify(self, root, manifest, receipt):
        rows = manifest["files"]
        # Keep namespace/device failures distinct from a bank access failure.
        self._root(root)
        try:
            self._probe(root, rows, receipt)
            return {"appAccess": "verified", "accessRepairFiles": 0}
        except RuntimeError as error:
            if _transport_failure(error): raise
            first_error = error
        initial_mode = self.mode
        for mode in ({"app-direct": ("app-process", "app-jdwp"), "app-process": ("app-jdwp",)}.get(initial_mode, ())):
            # The proc pathname seam still executes in run-as's origin. A real
            # app-origin child is necessary on scoped FUSE variants that reject
            # both aliases, even with the same UID. Exhaust it before chmod.
            self.mode = mode
            try:
                self._probe(root, rows, receipt)
                return {"appAccess": "verified", "accessRepairFiles": 0}
            except RuntimeError as error:
                if _transport_failure(error): raise
                # A failed app-origin launch must not disable an otherwise
                # usable direct view's metadata-only permission repair.
                if mode == "app-jdwp":
                    try:
                        actual = self._root(root)
                        self.view._check_external_root(actual, mode)
                    except RuntimeError as boundary:
                        if _transport_failure(boundary): raise
                        self.mode = initial_mode
        print("Repairing app access to retained " + root.rsplit("/", 1)[-1]
              + "; only manifested file/directory permissions are touched.", flush=True)
        directories = {root, root + "/StreamingAssets"}
        for row in rows:
            directories.update(root + "/" + str(parent) for parent in PurePosixPath(row["path"]).parents
                               if str(parent) != ".")
        paths = [(name, "u+rwx,a+rx", "-d") for name in sorted(directories, key=lambda name: (name.count("/"), name))]
        paths += [(root + "/" + name, "a+r", "-f") for name in [receipt, *[row["path"] for row in rows]]]
        # Reject links before chmod. Never dereference unmanifested descendants.
        for offset in range(0, len(paths), 128):
            self.view._device_guard()
            self.view._check_external_root(root, "shell")
            lines = []
            for path, mode, kind in paths[offset:offset + 128]:
                quoted = shlex.quote(path)
                lines += ["test ! -L " + quoted, "test " + kind + " " + quoted,
                          "chmod " + mode + " " + quoted]
            self.view.shell("\n".join(lines), timeout=60)
        try:
            self._probe(root, rows, receipt)
        except RuntimeError as error:
            if _transport_failure(error): raise
            after_error = error
            if self.mode in ("app-direct", "app-process"):
                # A bank may need both mode repair and the app's actual mount
                # view; the pre-repair process probe cannot prove this case.
                for mode in ({"app-direct": ("app-process", "app-jdwp"), "app-process": ("app-jdwp",)}[self.mode]):
                    self.mode = mode
                    try:
                        self._probe(root, rows, receipt)
                        return {"appAccess": "repaired", "accessRepairFiles": len(rows)}
                    except RuntimeError as error:
                        if _transport_failure(error): raise
                        after_error = error
            raise RuntimeError("Android still denies the app access to " + root.rsplit("/", 1)[-1]
                               + " after targeted permission repair. The APK and game data are retained; "
                               "no full content copy was attempted. Original check: " + str(first_error)
                               + "; after repair: " + str(after_error)) from after_error
        return {"appAccess": "repaired", "accessRepairFiles": len(rows)}
