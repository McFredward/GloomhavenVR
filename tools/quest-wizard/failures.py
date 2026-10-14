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

DELIVERY_LOGS = frozenset({'update-align.log', 'update-sign.log', 'update-signature.log',
                          'update-base-signature.log', 'update-key-certificate.log'})


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


def delivery_failure_logs(root, message, started):
    """Follow only the exact current host command named by the build failure."""
    match = re.fullmatch(r'[^\r\n]*; inspect ([^\r\n]+[\\/](?P<name>[^\\/]+\.log))', message.splitlines()[0] if message else '')
    if not match or match['name'] not in DELIVERY_LOGS: return []
    path = root / 'build/logs' / match['name']
    normalize = lambda value: os.path.normcase(str(value).replace('\\', '/'))
    if normalize(match[1]) != normalize(path): return []
    text = current_tail(path, started)
    return [(path, text)] if text is not None else []


def safe_delivery_summary(text):
    """Keep the bounded native cause without exposing signer credentials."""
    text = re.sub(r'(?s)-----BEGIN [^-]+-----.*?-----END [^-]+-----', '[redacted key block]', text)
    safe = []
    for line in text.splitlines():
        if (re.search(r'(?i)(password|passwd|secret|token|credential|authorization|cookie|steamid|providerid|accountid|displayname|personaname)\s*["\x27]?\s*[:=]', line)
                or re.fullmatch(r'[A-Z][A-Z0-9_]{2,}=.*', line.strip())
                or '-----BEGIN ' in line or '-----END ' in line):
            continue
        line = re.sub(r'(?i)Bearer\s+[A-Za-z0-9._~+/=-]+', 'Bearer [redacted]', line)
        line = re.sub(r'\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\b', '[redacted JWT]', line)
        line = re.sub(r'\b7656119\d{10}\b', '[profile-id]', line)
        safe.append(line)
    selected, rank = error_summary('\n'.join(safe))
    if rank: return selected, rank
    # zipalign's messages do not consistently contain an Error: prefix. Prefer
    # its actual refusal over usage or the outer python.exe exit wrapper.
    for line in reversed(safe):
        if re.search(r'(?i)unable to|cannot|failed|invalid|not found|already exists|out of memory', line):
            return line.strip()[:2048], 2
    for line in reversed(safe):
        if line.strip() and not line.startswith('[redacted'):
            return line.strip()[:2048], 1
    return '', 0


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


def installation_failure(text, parameters):
    """Only the current installer child can declare a connection prerequisite.

    Capture095538 contains a completed signed/validated665 Player, then the
    installer's stderr prefix which the generic compiler classifier misses.
    Match that complete anchored prefix, not arbitrary ADB output mentioning
    an offline device: a corrupt APK or signer refusal remains an install error.
    The caller supplies a bounded, current-attempt ordinary-file tail.
    """
    lines = text.splitlines()
    starts = [index for index, line in enumerate(lines) if line.startswith(("Quest installation stopped: ", "Quest connection required: "))]
    if not starts: return None
    index = starts[-1]
    prefix = "Quest connection required: " if lines[index].startswith("Quest connection required: ") else "Quest installation stopped: "
    parts = [lines[index][len(prefix):].strip()]
    for line in lines[index + 1:]:
        if line.startswith(("ADB log: ", "GHVRQ_PROGRESS ", "GHVRQ_ACTION ")): break
        if line.strip(): parts.append(line.strip())
    # Capture112552's ADB tar error contains several lines. Its retained
    # backup location occurs after those errors, so reading only the prefix
    # discarded precisely the recovery information the player needs.
    primary = parts[0]
    current_error = "\n".join(parts)
    cause = current_error[:2048]
    reason = next((reason for prefix, reason in (
        ("No authorized USB Quest is available.", "missing-or-unauthorized"),
        ("The requested USB serial is missing or unauthorized.", "missing-or-unauthorized"),
        ("The Wi-Fi Quest is offline or unauthorized.", "offline-or-unauthorized"),
        ("Multiple USB Quests are connected.", "ambiguous"),
        ("Multiple Wi-Fi Quests are connected.", "ambiguous"),
        ("Could not determine one Quest Wi-Fi IPv4 address.", "wifi-unavailable"),
        ("USB enabled Wi-Fi ADB, but connection failed.", "wifi-unavailable"),
        ("The Quest connection is unavailable.", "offline-or-unauthorized"),
        ("The USB Quest differs from the remembered headset.", "different-headset"),
    ) if primary == prefix or primary.startswith(prefix + " ")), None)
    fields = dict(parameters, cause=cause, completedWorkRetained=True,
                  completedBuildRetained=True, retryAction="install", canSkipInstall=True)
    if primary.startswith("Signing migration could not safely continue:"):
        backup = re.search(r"Original recovery data is retained at ([^\r\n]{1,2048}?)(?=\. Retry with the same APK and headset\.)", current_error)
        if backup is None: backup = re.search(r"(?:^|\n| )Retained backup: ([^\r\n]{1,2048})", current_error)
        if backup and not any(ord(char) < 32 or ord(char) == 127 for char in backup[1]):
            fields["backupPath"] = backup[1]
        # Only the current installer's bounded action, immediately before its
        # final refusal, can request a policy decision. An earlier failure,
        # malformed JSON or arbitrary log text must never grant that authority.
        previous = starts[-2] + 1 if len(starts) > 1 else 0
        declared = [line[len("GHVRQ_ACTION "):] for line in lines[previous:index]
                    if line.startswith("GHVRQ_ACTION ")]
        action = None
        if declared and len(declared[-1]) <= 2048:
            try:
                value = json.loads(declared[-1])
                content_fields = {"schema", "kind", "reason", "saveDataProtected", "estimatedBytes", "transaction", "deviceSerial"}
                if (isinstance(value, dict) and set(value) == content_fields
                        and type(value["schema"]) is int and value["schema"] == 1
                        and value["kind"] == "signing-content-reinstall"
                        and value["reason"] == "native-rename-unavailable" and value["saveDataProtected"] is True
                        and type(value["estimatedBytes"]) is int and 0 <= value["estimatedBytes"] <= 2 ** 50
                        and isinstance(value["transaction"], str) and re.fullmatch(r"[0-9a-f]{32}", value["transaction"])
                        and isinstance(value["deviceSerial"], str) and re.fullmatch(r"[A-Za-z0-9_.:-]{1,256}", value["deviceSerial"])):
                    return WizardError("quest_signing_content_reinstall_required",
                        "Android cannot move the existing game content without copying it. The current app, saves and settings are retained. Allow a new transfer of the game content to continue, or finish without installing. The APK is already built; Unity work is not repeated.",
                        "Android kann die vorhandenen Spielinhalte nicht ohne Kopie verschieben. Die aktuelle App, Spielstände und Einstellungen bleiben erhalten. Eine erneute Übertragung der Spielinhalte erlauben oder ohne Installation abschließen. Die APK ist bereits gebaut; Unity-Arbeit wird nicht wiederholt.",
                        **dict(fields, signingRecovery=True, saveDataProtected=True, requiresContentReinstall=True,
                               estimatedBytes=value["estimatedBytes"], contentReinstallReason=value["reason"],
                               contentReinstallTransaction=value["transaction"], contentReinstallDeviceSerial=value["deviceSerial"]))
                repair_fields = {"schema", "kind", "reason", "saveDataProtected", "attempts", "totalAttempts", "root"}
                if (isinstance(value, dict) and set(value) == repair_fields
                        and type(value["schema"]) is int and value["schema"] == 1
                        and value["kind"] == "signing-repair-wait"
                        and value["reason"] in ("backup-census-mismatch", "backup-transfer-unavailable", "restore-app-access", "restore-app-storage-unavailable", "restore-app-launch-unavailable")
                        and value["saveDataProtected"] is True
                        and value["root"] in ("private", "device-protected", "external")
                        and type(value["attempts"]) is int and type(value["totalAttempts"]) is int
                        and 1 <= value["attempts"] == value["totalAttempts"] <= 64):
                    restore = value["reason"] == "restore-app-access"
                    storage = value["reason"] == "restore-app-storage-unavailable"
                    launch = value["reason"] == "restore-app-launch-unavailable"
                    return WizardError("quest_signing_repair_wait",
                        ("The Quest is reachable and awake, but Android did not start the app for storage restoration. Retry installation. If the app still does not start, export installation diagnostics. The completed APK and original save backups are retained; no APK rebuild or save reset is performed."
                         if launch else "The Quest is reachable and the APK is ready, but automatic restoration could not access the app's external storage. Keep the retained backups and retry installation. If access remains unavailable, export diagnostics for this restoration step. The APK is not rebuilt and saves are not reset."
                         if storage else "The APK is ready, but Android cannot yet open the app's writable storage to restore its saves and settings. Wear and unlock the Quest, keep it awake and connected, then retry installation. Retained backups are kept; the APK is not rebuilt and saves are not reset."
                         if restore else "The completed APK and existing saves are retained. Automatic repair tried the available safe backup transfers, but this data backup is not ready. Keep the Quest awake and connected and retry installation, or finish without installing. No save reset is performed."),
                        ("Die Quest ist erreichbar und wach. Android hat die App zum Wiederherstellen der Daten dennoch nicht gestartet. Installation erneut versuchen. Startet die App weiterhin nicht, die Installationsdiagnose exportieren. Die fertige APK und ursprünglichen Spielstand-Sicherungen bleiben erhalten; die APK wird nicht neu gebaut und Spielstände werden nicht zurückgesetzt."
                         if launch else "Die Quest ist erreichbar und die APK ist fertig. Die automatische Wiederherstellung konnte den externen App-Speicher noch nicht öffnen. Vorhandene Sicherungen behalten und die Installation erneut versuchen. Bleibt der Zugriff gesperrt, das Diagnosepaket für diesen Wiederherstellungsschritt exportieren. Die APK wird nicht neu gebaut und Spielstände werden nicht zurückgesetzt."
                         if storage else "Die APK ist fertig. Android kann den beschreibbaren App-Speicher zum Wiederherstellen der Spielstände und Einstellungen noch nicht öffnen. Quest aufsetzen, entsperren, eingeschaltet und verbunden lassen, dann die Installation erneut versuchen. Vorhandene Sicherungen bleiben erhalten; die APK wird nicht neu gebaut und Spielstände werden nicht zurückgesetzt."
                         if restore else "Die fertige APK und vorhandene Spielstände bleiben erhalten. Die automatische Reparatur hat die verfügbaren sicheren Übertragungswege versucht; diese Datensicherung ist noch nicht bereit. Quest eingeschaltet und verbunden lassen und die Installation erneut versuchen oder ohne Installation abschließen. Spielstände werden nicht zurückgesetzt."),
                        **dict(fields, signingRecovery=True, saveDataProtected=True,
                               repairReason=value["reason"], repairRoot=value["root"],
                               repairAttempts=value["attempts"], repairTotalAttempts=value["totalAttempts"]))
                expected = {"schema", "kind", "reason", "policy", "saveDataProtected"}
                if (isinstance(value, dict) and set(value) == expected
                        and type(value["schema"]) is int and value["schema"] == 1
                        and value["kind"] == "signing-data-policy"
                        and value["reason"] in ("save-data-inaccessible", "non-save-inaccessible")
                        and value["policy"] in ("all", "saves")
                        and value["saveDataProtected"] is True
                        and (value["reason"] != "non-save-inaccessible" or value["policy"] == "all")):
                    action = value
            except (ValueError, TypeError): pass
        if action:
            saves = action["reason"] == "save-data-inaccessible"
            return WizardError("quest_signing_data_required",
                ("A save folder cannot be read safely. The existing installation, current and archived saves, retained backups and completed APK stay protected. Keep the Quest awake and connected and retry, or finish without installing. The wizard will not remove the app or reset saves."
                 if saves else "Some files outside the save folders cannot be read. Current and archived saves stay protected. You can retry preserving all files, or choose to keep saves and continue; inaccessible files outside save folders, including diagnostic or settings files, will not be retained. The completed APK is not rebuilt."),
                ("Ein Spielstandordner kann nicht sicher gelesen werden. Die vorhandene Installation, aktuelle und gespeicherte Spielstände, vorhandene Sicherungen und die fertige APK bleiben geschützt. Quest eingeschaltet und verbunden lassen und erneut versuchen oder ohne Installation abschließen. Der Wizard entfernt die App nicht und setzt keine Spielstände zurück."
                 if saves else "Einige Dateien außerhalb der Spielstandordner können nicht gelesen werden. Aktuelle und gespeicherte Spielstände bleiben geschützt. Erneut alle Dateien sichern oder Spielstände behalten und fortfahren; nicht lesbare andere Dateien, etwa Diagnose- oder Einstellungsdateien, werden dabei nicht übernommen. Die fertige APK wird nicht neu gebaut."),
                **dict(fields, signingRecovery=True, recoveryDataReason=action["reason"],
                       recoveryDataPolicy=action["policy"], saveDataProtected=True))
        # An actual current signing migration refusal is not a connection
        # problem. Never tell the player to rebuild or remove the installed
        # application: the installer retains its recovery journal and backups.
        return WizardError("quest_signing_recovery_required",
            "The APK is ready. Automatic signing recovery could not safely finish. Keep the Quest awake and connected and retry installation. If the same cause remains, save the diagnostic package so this specific recovery can be repaired. Keep any backups shown below; the saved recovery resumes and the APK build is not repeated.",
            "Die APK ist fertig. Die automatische Signatur-Wiederherstellung konnte nicht sicher abgeschlossen werden. Quest eingeschaltet und verbunden lassen und die Installation erneut versuchen. Bleibt dieselbe Ursache bestehen, das Diagnosepaket für eine gezielte Reparatur speichern. Die unten genannten Sicherungen behalten; die gespeicherte Wiederherstellung wird fortgesetzt und die APK nicht neu gebaut.",
            **dict(fields, signingRecovery=True))
    if reason:
        return WizardError("quest_connection_required",
            "The APK is ready. Connect the intended Quest with a USB data cable, unlock it, enable developer mode and accept USB debugging in the headset. Then retry installation. An authorized USB connection is used directly; Wi-Fi is optional. The completed APK and existing progress stay retained.",
            "Die APK ist fertig. Die gewünschte Quest per USB-Datenkabel anschließen, entsperren, Entwicklermodus aktivieren und USB-Debugging in der Brille bestätigen. Danach die Installation erneut versuchen. Eine freigegebene USB-Verbindung wird direkt genutzt; WLAN ist optional. Die fertige APK und der bisherige Fortschritt bleiben erhalten.",
            **dict(fields, connectionReason=reason))
    return WizardError("installation_tool_failed",
        "The APK build completed, but installation failed. The exact installer cause is shown below. Keep the workspace and retry installation after resolving it, or finish without installing.",
        "Die APK wurde fertig gebaut, aber die Installation ist fehlgeschlagen. Der genaue Installerfehler steht unten. Arbeitsordner behalten und nach Behebung die Installation erneut versuchen oder ohne Installation abschließen.",
        **fields)


def tool_failure(root, stage, log, started, executable, exit_code):
    """Read only this attempt's failure and explicitly named child logs."""
    parameters = {"executable": executable, "exitCode": exit_code, "log": str(log), "failureStage": stage}
    resources, failure_code = {}, None
    logs = [ordinary(log)]
    child_logs = []
    delivery_logs = []
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
                        delivery_logs = delivery_failure_logs(root, parameters["builderError"], started)
        except (OSError, ValueError, WizardError): pass
    error_line, error_rank = "", 0
    unity_cause = False
    disk_failure = False
    installer_error = None
    for path in logs:
        text = current_tail(path, started)
        if text is None: continue
        if stage == "install":
            installer_error = installation_failure(text, dict(parameters, logs=[str(log)]))
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
    for path, text in delivery_logs:
        logs.append(path)
        disk_failure |= bool(re.search(r"(?i)(no space left on device|disk (?:is )?full|\[Errno 28\]|\[WinError (?:39|112)\]|not enough space on (?:the )?disk)", text))
        summary, rank = safe_delivery_summary(text)
        if rank: error_line, error_rank = summary, rank
    parameters["cause"] = error_line or parameters.get("builderError") or "No error summary was emitted; inspect the retained tool log."
    parameters["logs"] = [str(path) for path in logs]
    if disk_failure or re.search(r"(?i)(no space left on device|\[Errno 28\]|\[WinError (?:39|112)\])", parameters.get("builderError", "")):
        failed_stage = parameters.pop("failureStage")
        return disk_full_error(root, failed_stage, **parameters)
    if installer_error: return installer_error
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
    elif delivery_logs:
        en = "The APK could not be aligned, signed or checked. The precise tool failure is shown below. Keep the workspace: the completed Android build remains available for another attempt."
        de = "Die APK konnte nicht ausgerichtet, signiert oder geprüft werden. Der genaue Werkzeugfehler steht unten. Arbeitsordner behalten: Der fertige Android-Build bleibt für einen weiteren Versuch erhalten."
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
