"""Workspace-bound portable export adapter; payload work stays in the Builder."""
from __future__ import annotations

import json
from pathlib import Path
import stat
import threading
import time

import discovery
from state import WizardError, atomic_json, ordinary, read_json

_destination_hints = {}
_destination_hints_lock = threading.Lock()


def _hint_stamp(path):
    """Metadata qualifies a UI hint only; actual export owns all byte checks."""
    try: info = ordinary(path).lstat()
    except FileNotFoundError: return None
    if not stat.S_ISREG(info.st_mode): raise ValueError("Export hint is not an ordinary file.")
    return (info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns, info.st_ctime_ns)


class PackageTools:
    def __init__(self, repo, workspace):
        self.repo, self.workspace = Path(repo), ordinary(workspace)
        self.receipt = self.workspace / "packages/latest-export.json"
        self.log = self.workspace / "logs/package-export.jsonl"

    def log_event(self, event, **details):
        # Qualify all owned paths before creating a folder or rotating a log.
        # Existing links must never redirect even a failed export's diagnostics.
        log = ordinary(self.log)
        previous = ordinary(log.with_name("package-export.previous.jsonl"))
        parent = ordinary(log.parent)
        parent.mkdir(parents=True, exist_ok=True)
        if log.exists() and log.stat().st_size > 1048576:
            log.replace(previous)
        with log.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps({"time": time.time(), "event": event, **details}, ensure_ascii=False) + "\n")

    def latest(self):
        """A UI hint only; imports independently qualify the signed package."""
        try:
            value = read_json(self.receipt, limit=65536)
            result, stamp = value["result"], value["stamp"]
            path = ordinary(result["path"]); info = path.lstat()
            if (value.get("schema") != 1 or not stat.S_ISREG(info.st_mode)
                    or info.st_size != result["size"]
                    or stamp != [info.st_size, info.st_mtime_ns, info.st_ctime_ns]):
                return None
            return result
        except (OSError, ValueError, KeyError, TypeError, WizardError):
            return None

    def defaults(self, store):
        selected = discovery.default_apk(store)
        build = selected.get("modBuild") if selected else None
        name = "GloomhavenVR-Quest" + ("-B" + str(build) if type(build) is int else "") + ".zip"
        destination = self.workspace / "exports" / name
        # A personalized workspace APK can still use the bank inside a prior
        # export. Its input field is an APK, so the UI cannot see this collision.
        # Resolve the signed bank's bounded locator only when the proposed ZIP
        # already exists; never hash/copy the bank just to suggest a filename.
        if selected and destination.exists():
            fallback = destination.with_name(destination.stem + "-copy.zip")
            try:
                key = (str(self.workspace), selected["path"], _hint_stamp(Path(selected["path"])),
                       str(destination), _hint_stamp(destination))
                with _destination_hints_lock: cached = _destination_hints.get(key)
                if cached and cached["stamp"] == _hint_stamp(cached["source"]):
                    return {"source": selected["path"], "destination": cached["destination"], "latestExport": self.latest()}
                helper = discovery.builder(self.repo)
                source = helper.portable_packages.content_source(Path(selected["path"]), self.workspace / "build")
                protected = Path(source["path"])
                if destination == protected or destination.samefile(protected):
                    destination = fallback
                hint = {"destination": str(destination), "source": protected, "stamp": _hint_stamp(protected)}
                with _destination_hints_lock:
                    if len(_destination_hints) >= 8: _destination_hints.pop(next(iter(_destination_hints)))
                    _destination_hints[key] = hint
            except (OSError, ValueError, RuntimeError, WizardError):
                # This remains a hint. Actual export independently qualifies
                # the APK and refuses replacing either exact payload source.
                destination = fallback
        return {"source": selected["path"] if selected else "",
                "destination": str(destination), "latestExport": self.latest()}

    def export(self, options, *, progress, cancel):
        helper = discovery.builder(self.repo)
        source = ordinary(options["source"])
        destination = ordinary(options["destination"])
        if source.suffix.lower() not in (".apk", ".zip") or destination.suffix.lower() != ".zip":
            raise WizardError("package_path", "Select an APK or Quest package and a ZIP destination.",
                              "Eine APK oder ein Quest-Paket und eine ZIP-Zieldatei auswählen.")
        if source == destination:
            raise WizardError("package_path", "Source and destination must differ.",
                              "Quelle und Zieldatei müssen sich unterscheiden.")
        self.log_event("export_started", source=str(source), destination=str(destination))
        # Import only the APK when exporting an existing portable package.
        # The package module streams its retained content bank directly.
        package = source if source.suffix.lower() == ".zip" else None
        imported = 0
        initial_total = 0
        if package:
            manifest = helper.portable_packages.inspect(package)
            imported = manifest["apk"]["size"]
            initial_total = imported + manifest["apk"]["size"] + manifest["content"]["size"] + len(
                json.dumps(manifest, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8"))
        def importing(value):
            completed = value.get("completed", 0)
            combined = {**value, "completed": completed, "total": initial_total,
                        "percent": min(99.9, completed * 100 / max(1, initial_total))}
            self.log_event("export_progress", **combined); progress(combined)
        apk = helper.portable_packages.import_apk(source, self.workspace / "build", progress=importing,
                           cancel=cancel.is_set) if package else source
        def measured(value):
            combined = {**value}
            if imported:
                completed, total = imported + value.get("completed", 0), imported + value.get("total", 0)
                combined.update(completed=completed, total=total, percent=min(99.9, completed * 100 / max(1, total)))
            self.log_event("export_progress", **combined)
            progress(combined)
        result = helper.portable_packages.export_package(apk, destination, self.workspace / "build",
                    source_package=package, progress=measured, cancel=cancel.is_set)
        path = ordinary(result["path"]); info = path.lstat()
        atomic_json(self.receipt, {"schema": 1, "result": result,
                                  "stamp": [info.st_size, info.st_mtime_ns, info.st_ctime_ns]})
        self.log_event("export_complete", result=result)
        return result
