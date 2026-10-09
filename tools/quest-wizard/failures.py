"""Bounded failure context from known logs in the owned conversion workspace."""
from __future__ import annotations
import errno
import json
from pathlib import Path
import re
import shutil

from state import WizardError, ordinary


def disk_full(error):
    return isinstance(error, OSError) and (error.errno == errno.ENOSPC or getattr(error, "winerror", None) in (39, 112))


def disk_full_error(root, stage, *, error=None, **parameters):
    root = ordinary(root)
    free = shutil.disk_usage(root).free
    gib = free / 1024 ** 3
    return WizardError("workspace_space_exhausted",
                       f"The build drive ran out of space ({gib:.1f} GiB free at {root}). Free space and continue; keep the workspace so completed work can be reused.",
                       f"Auf dem Build-Laufwerk fehlt Speicherplatz ({gib:.1f} GiB frei in {root}). Platz freigeben und fortsetzen; den Arbeitsordner behalten, damit abgeschlossene Arbeit wiederverwendet werden kann.",
                       workspaceRoot=str(root), freeBytes=free, failureStage=stage,
                       completedWorkRetained=True, **({"cause": str(error), "errorNumber": error.errno,
                                                       "winError": getattr(error, "winerror", None)} if error else {}),
                       **parameters)


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
    disk_failure = False
    for path in logs:
        if not path.is_file() or path.stat().st_mtime < started: continue
        text = tail(path)
        disk_failure |= bool(re.search(r"(?i)(no space left on device|disk (?:is )?full|\[Errno 28\]|\[WinError (?:39|112)\]|not enough space on (?:the )?disk)", text))
        for line in text.splitlines():
            if re.search(r"(?i)(FAILED:|Quest builder:|(?:Error|Exception):|error (?:CS|MSB|BC)\d+|fatal error:)", line):
                error_line = line.strip()[:2048]
    parameters["cause"] = error_line or parameters.get("builderError") or "No error summary was emitted; inspect the retained tool log."
    parameters["logs"] = [str(path) for path in logs]
    if disk_failure or re.search(r"(?i)(no space left on device|\[Errno 28\]|\[WinError (?:39|112)\])", parameters.get("builderError", "")):
        failed_stage = parameters.pop("failureStage")
        return disk_full_error(root, failed_stage, **parameters)
    import_failure = ("helper import failed before game conversion" in parameters.get("builderError", "")
                      or "ModuleNotFoundError:" in parameters["cause"])
    certificate_failure = re.search(r"(?i)(CERTIFICATE_VERIFY_FAILED|certificate verify failed|TLS certificate (?:verification|validation) failed)",
                                    parameters["cause"] + " " + parameters.get("builderError", ""))
    if certificate_failure:
        en = "An HTTPS download for a build dependency failed certificate verification. Keep the workspace so completed conversions can be reused. Check the system date and any HTTPS proxy/filter, then retry; the precise download error is in the logs."
        de = "Ein HTTPS-Download für eine Build-Abhängigkeit ist an der Zertifikatsprüfung gescheitert. Arbeitsordner behalten, damit fertige Konvertierungen wiederverwendet werden. Systemdatum und gegebenenfalls HTTPS-Proxy/Filter prüfen, dann erneut versuchen; der genaue Downloadfehler steht im Protokoll."
        parameters["completedWorkRetained"] = True
    elif parameters["failureStage"] == "recovery" and import_failure:
        en = "The builder could not load its own staging helper. Keep the workspace: completed game exports remain available. Save the diagnostic package and resume with the corrected builder."
        de = "Der Builder konnte ein eigenes Hilfsmodul für die Projektvorbereitung nicht laden. Arbeitsordner behalten: abgeschlossene Spieleexports bleiben erhalten. Diagnosepaket speichern und mit dem korrigierten Builder fortsetzen."
    elif parameters["failureStage"] == "recovery" and "Recovery resume evidence" in parameters.get("builderError", ""):
        en = "The saved game export's evidence could not be read or verified. Keep the workspace and save the diagnostic package; it contains the file sizes and precise cause. Continue with the corrected builder."
        de = "Der Nachweis des gespeicherten Spieleexports konnte nicht gelesen oder geprüft werden. Arbeitsordner behalten und Diagnosepaket speichern; es enthält die Dateigrößen und genaue Ursache. Mit dem korrigierten Builder fortsetzen."
    elif parameters["failureStage"] == "recovery":
        en = "Game asset conversion failed. Save the diagnostic package; keep the workspace and resume with the corrected builder."
        de = "Die Konvertierung der Spielassets ist fehlgeschlagen. Diagnosepaket speichern; den Arbeitsordner behalten und mit dem korrigierten Builder fortsetzen."
    else:
        en = f"The tool {executable} failed in this step (exit {exit_code}). Save the diagnostic package before retrying."
        de = f"Das Werkzeug {executable} ist in diesem Schritt fehlgeschlagen (Fehlercode {exit_code}). Vor einem erneuten Versuch das Diagnosepaket speichern."
    return WizardError("build_tool_failed" if stage in ("inspect", "build") else "child_failed", en, de, **parameters)
