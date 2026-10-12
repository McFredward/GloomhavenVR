"""Workspace-bound portable export adapter; payload work stays in the Builder."""
from __future__ import annotations

import json
from pathlib import Path
import stat
import time

import discovery
from state import WizardError, atomic_json, ordinary, read_json


class PackageTools:
    def __init__(self, repo, workspace):
        self.repo, self.workspace = Path(repo), ordinary(workspace)
        self.receipt = self.workspace / "packages/latest-export.json"
        self.log = self.workspace / "logs/package-export.jsonl"

    def log_event(self, event, **details):
        self.log.parent.mkdir(parents=True, exist_ok=True)
        if self.log.exists() and self.log.stat().st_size > 1048576:
            self.log.replace(self.log.with_name("package-export.previous.jsonl"))
        with self.log.open("a", encoding="utf-8") as stream:
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
        return {"source": selected["path"] if selected else "",
                "destination": str(self.workspace / "exports" / name), "latestExport": self.latest()}

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
