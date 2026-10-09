"""Bounded failure context from known logs in the owned conversion workspace."""
from __future__ import annotations
import errno
import json
import os
from pathlib import Path
import re
import shutil
import stat

from state import WizardError, ordinary


def memory_resources(value):
    """Expose only bounded, measured capacity fields from the current attempt."""
    if not isinstance(value, dict): return {}
    source = {**(value.get("host") if isinstance(value.get("host"), dict) else {}),
              **(value.get("policy") if isinstance(value.get("policy"), dict) else {}), **value}
    fields = {}
    for name in ("totalMemoryBytes", "availableMemoryBytes", "commitHeadroomBytes",
                 "requiredCommitHeadroomBytes", "largestWorkerReserveBytes", "parentReserveBytes"):
        number = source.get(name)
        if type(number) is int and 0 <= number <= 2 ** 64 - 1: fields[name] = number
    if "commitHeadroomBytes" not in fields:
        number = source.get("availableCommitBytes")
        if type(number) is int and 0 <= number <= 2 ** 64 - 1: fields["commitHeadroomBytes"] = number
    return fields


def native_memory_error(parameters, resources):
    """A retryable host prerequisite; it is never failed game conversion."""
    measured = memory_resources(resources)
    def amount(name, unknown):
        return f"{measured[name] / 1024 ** 3:.1f} GiB" if name in measured else unknown
    en = ("Native compilation cannot start with the current memory capacity. "
          f"Installed RAM: {amount('totalMemoryBytes', 'unknown')}; "
          f"Available RAM: {amount('availableMemoryBytes', 'unknown')}; "
          f"available commit (RAM plus paging capacity): {amount('commitHeadroomBytes', 'not reported')}; "
          f"required headroom: {amount('requiredCommitHeadroomBytes', 'unknown')}. "
          "Close other memory-intensive programs; on Windows, check that the paging file is enabled and has space to grow. "
          "Then continue in the same workspace. Completed conversions remain saved.")
    de = ("Die native Kompilierung kann mit der aktuellen Speicherkapazität nicht starten. "
          f"Installierter RAM: {amount('totalMemoryBytes', 'unbekannt')}; "
          f"Verfügbarer RAM: {amount('availableMemoryBytes', 'unbekannt')}; "
          f"verfügbarer Commit (RAM und Auslagerungskapazität): {amount('commitHeadroomBytes', 'nicht gemeldet')}; "
          f"benötigter Spielraum: {amount('requiredCommitHeadroomBytes', 'unbekannt')}. "
          "Andere speicherintensive Programme schließen; unter Windows prüfen, ob die Auslagerungsdatei aktiviert ist und Platz zum Wachsen hat. "
          "Danach im selben Arbeitsordner fortsetzen. Fertige Konvertierungen bleiben gespeichert.")
    return WizardError("native_memory_unavailable", en, de,
                       **dict(parameters, failureStage="native-memory-check",
                              completedWorkRetained=True, resources=measured))


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


def current_tail(path, started, limit=8192):
    """Optional diagnostics must not follow links or replace the real failure."""
    try:
        path = ordinary(path)
        descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) |
                             getattr(os, "O_NONBLOCK", 0) | getattr(os, "O_BINARY", 0))
        with os.fdopen(descriptor, "rb") as stream:
            observed = os.fstat(stream.fileno())
            if not stat.S_ISREG(observed.st_mode) or observed.st_nlink != 1 or observed.st_mtime < started:
                return None
            stream.seek(max(0, observed.st_size - limit))
            return stream.read(limit).decode("utf-8", errors="replace")
    except (OSError, WizardError):
        return None


def unity_failure_logs(root, message, started, key=None):
    """Follow one named current launcher and its exact Editor sibling, never a glob.

    Capture200517's wrapper names unity-launch, but the sprite exception is in
    unity-build. Both logs must belong to this workspace and attempt. A previous
    memory-attempt archive or another build key cannot supply the present cause.
    """
    match = re.fullmatch(r"[^\r\n]*; inspect (.+[\\/](?P<name>(?:unity-launch|package-import-launch|package-api)-"
                         r"(?P<key>[0-9a-f]{12})\.log|update-(?:code|sdk)-launch\.log))", message)
    if not match: return []
    if key is not None:
        if not isinstance(key, str) or not re.fullmatch(r"[0-9a-f]{64}", key): return []
        if match["key"] is not None and match["key"] != key[:12]: return []
    launcher = root / "build/logs" / match["name"]
    normalize = lambda value: os.path.normcase(str(value).replace("\\", "/"))
    if normalize(match[1]) != normalize(launcher): return []
    text = current_tail(launcher, started)
    if text is None: return []
    logs = [(launcher, text)]
    sibling = ("unity-build-" + match["key"] + ".log" if match["name"].startswith("unity-launch-") else
               "package-import-" + match["key"] + ".log" if match["name"].startswith("package-import-launch-") else
               match["name"].replace("-launch.log", "-unity.log") if match["name"].startswith("update-") else None)
    if sibling:
        editor = root / "build/logs" / sibling
        text = current_tail(editor, started, limit=65536)
        if text is not None: logs.append((editor, text))
    return logs


def error_summary(text, *, unity=False):
    """Prefer the actual compiler/Editor exception to a generic wrapper line."""
    selected, rank = "", 0
    for line in text.splitlines():
        line = line.strip()
        if unity and re.match(r"\[Licensing(?:::Module|Client)\]", line): continue
        specific = re.search(r"(?:^|\s)(?:[\w.]*Exception:|error (?:CS|MSB|BC)\d+|fatal error:|Shader error in )", line, re.I)
        ordinary_error = re.search(r"FAILED:|Quest builder:|(?:Error|Exception):", line, re.I)
        candidate_rank = 2 if specific else 1 if ordinary_error else 0
        if candidate_rank and candidate_rank >= rank:
            selected, rank = line[:2048], candidate_rank
    return selected, rank


def tool_failure(root, stage, log, started, executable, exit_code):
    """Read only this attempt's failure and explicitly named child logs."""
    parameters = {"executable": executable, "exitCode": exit_code, "log": str(log), "failureStage": stage}
    resources, failure_code = {}, None
    logs = [ordinary(log)]
    child_logs = []
    failure_path = root / "build/last-failure.json"
    failure_text = current_tail(failure_path, started, limit=65537) if stage in ("inspect", "build") else None
    if failure_text is not None:
        try:
            if failure_path.stat().st_size <= 65536:
                failure = json.loads(failure_text)
                if isinstance(failure, dict) and failure.get("schema") == 1:
                    resources = memory_resources(failure.get("resources"))
                    failure_code = failure.get("code")
                    name = failure.get("stage")
                    if isinstance(name, str) and re.fullmatch(r"[a-z][a-z0-9-]{0,80}", name):
                        parameters["failureStage"] = name
                    parameters["builderError"] = str(failure.get("message", ""))[:8192]
                    key = failure.get("key")
                    if name == "recovery" and isinstance(key, str) and re.fullmatch(r"[0-9a-f]{64}", key):
                        logs.append(ordinary(root / "build/logs" / ("recovery-" + key[:12] + ".log")))
                    if stage == "build" and name in ("build", "update-mod", "update-profile"):
                        child_logs = unity_failure_logs(root, parameters["builderError"], started, key)
        except (OSError, ValueError, WizardError): pass
    error_line, error_rank = "", 0
    unity_cause = False
    disk_failure = False
    for path in logs:
        text = current_tail(path, started)
        if text is None: continue
        disk_failure |= bool(re.search(r"(?i)(no space left on device|disk (?:is )?full|\[Errno 28\]|\[WinError (?:39|112)\]|not enough space on (?:the )?disk)", text))
        for line in text.splitlines():
            if line.startswith("resources: "):
                try:
                    observed = json.loads(line[len("resources: "):])
                    if isinstance(observed, dict) and observed.get("phase") == "il2cpp":
                        resources = {**memory_resources(observed), **resources}
                except (ValueError, TypeError): pass
        summary, rank = error_summary(text)
        if rank and rank >= error_rank: error_line, error_rank = summary, rank
    for path, text in child_logs:
        logs.append(path)
        disk_failure |= bool(re.search(r"(?i)(no space left on device|disk (?:is )?full|\[Errno 28\]|\[WinError (?:39|112)\]|not enough space on (?:the )?disk)", text))
        is_unity = not path.name.startswith("package-api-")
        summary, rank = error_summary(text, unity=is_unity)
        if rank and rank >= error_rank:
            error_line, error_rank, unity_cause = summary, rank, is_unity
    parameters["cause"] = error_line or parameters.get("builderError") or "No error summary was emitted; inspect the retained tool log."
    parameters["logs"] = [str(path) for path in logs]
    if disk_failure or re.search(r"(?i)(no space left on device|\[Errno 28\]|\[WinError (?:39|112)\])", parameters.get("builderError", "")):
        failed_stage = parameters.pop("failureStage")
        return disk_full_error(root, failed_stage, **parameters)
    if failure_code == "native_memory_unavailable" or "Available RAM/commit is insufficient or unknown for the large native game compiler" in (parameters["cause"] + " " + parameters.get("builderError", "")):
        return native_memory_error(parameters, resources)
    import_failure = ("helper import failed before game conversion" in parameters.get("builderError", "")
                      or "ModuleNotFoundError:" in parameters["cause"])
    certificate_failure = re.search(r"(?i)(CERTIFICATE_VERIFY_FAILED|certificate verify failed|TLS certificate (?:verification|validation) failed)",
                                    parameters["cause"] + " " + parameters.get("builderError", ""))
    if certificate_failure:
        en = "An HTTPS download for a build dependency failed certificate verification. Keep the workspace so completed conversions can be reused. Check the system date and any HTTPS proxy/filter, then retry; the precise download error is in the logs."
        de = "Ein HTTPS-Download für eine Build-Abhängigkeit ist an der Zertifikatsprüfung gescheitert. Arbeitsordner behalten, damit fertige Konvertierungen wiederverwendet werden. Systemdatum und gegebenenfalls HTTPS-Proxy/Filter prüfen, dann erneut versuchen; der genaue Downloadfehler steht im Protokoll."
        parameters["completedWorkRetained"] = True
    elif unity_cause:
        en = "Unity failed while preparing or building the Quest project. The precise cause is shown below. Keep the workspace and save the diagnostic package before retrying."
        de = "Unity konnte das Quest-Projekt nicht vorbereiten oder bauen. Die genaue Ursache steht unten. Arbeitsordner behalten und vor einem erneuten Versuch das Diagnosepaket speichern."
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
