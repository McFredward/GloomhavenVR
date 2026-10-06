"""Bounded failure context from known logs in the owned conversion workspace."""
from __future__ import annotations
import json
from pathlib import Path
import re

from state import WizardError, ordinary


def tail(path, limit=8192):
    path = ordinary(path)
    if not path.is_file(): return ""
    with path.open("rb") as stream:
        stream.seek(max(0, path.stat().st_size - limit))
        return stream.read(limit).decode("utf-8", errors="replace")


def tool_failure(root, stage, log, started, executable, exit_code):
    """Read only this attempt's failure and explicitly named child logs."""
    parameters = {"executable": executable, "exitCode": exit_code, "log": str(log), "failureStage": stage}
    logs = [ordinary(log)]
    failure_path = ordinary(root / "build/last-failure.json")
    if stage in ("inspect", "build") and failure_path.is_file() and failure_path.stat().st_mtime >= started:
        try:
            if failure_path.stat().st_size <= 65536:
                failure = json.loads(failure_path.read_text(encoding="utf-8"))
                if isinstance(failure, dict) and failure.get("schema") == 1:
                    name = failure.get("stage")
                    if isinstance(name, str) and re.fullmatch(r"[a-z][a-z0-9-]{0,80}", name):
                        parameters["failureStage"] = name
                    parameters["builderError"] = str(failure.get("message", ""))[:8192]
                    key = failure.get("key")
                    if name == "recovery" and isinstance(key, str) and re.fullmatch(r"[0-9a-f]{64}", key):
                        logs.append(ordinary(root / "build/logs" / ("recovery-" + key[:12] + ".log")))
        except (OSError, ValueError): pass
    error_line = ""
    for path in logs:
        if not path.is_file() or path.stat().st_mtime < started: continue
        text = tail(path)
        for line in text.splitlines():
            if re.search(r"(?i)(FAILED:|Quest builder:|(?:Error|Exception):|error (?:CS|MSB|BC)\d+|fatal error:)", line):
                error_line = line.strip()[:2048]
    parameters["cause"] = error_line or parameters.get("builderError") or "No error summary was emitted; inspect the retained tool log."
    parameters["logs"] = [str(path) for path in logs]
    if parameters["failureStage"] == "recovery":
        en = "Game asset conversion failed. Save the diagnostic package; keep the workspace and resume with the corrected builder."
        de = "Die Konvertierung der Spielassets ist fehlgeschlagen. Diagnosepaket speichern; den Arbeitsordner behalten und mit dem korrigierten Builder fortsetzen."
    else:
        en = f"The tool {executable} failed in this step (exit {exit_code}). Save the diagnostic package before retrying."
        de = f"Das Werkzeug {executable} ist in diesem Schritt fehlgeschlagen (Fehlercode {exit_code}). Vor einem erneuten Versuch das Diagnosepaket speichern."
    return WizardError("build_tool_failed" if stage in ("inspect", "build") else "child_failed", en, de, **parameters)
