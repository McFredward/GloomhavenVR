"""Owned local wizard sessions and atomic, hash-verified stage receipts."""
from __future__ import annotations
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import math
import threading
import time
import uuid

STAGES = ("tools", "source", "unity", "profile", "inspect", "build", "install")
PROGRESS_INTERVAL = 0.5
PROGRESS_LOG_BYTES = 8 * 1048576


def stage_progress(phase="pending", done=0, total=1, unit="stages", detail=None):
    """Only observed quantities produce a percentage; an unknown total stays null."""
    if not isinstance(phase, str) or not phase or len(phase) > 160:
        raise WizardError("invalid_progress", "Invalid progress phase.")
    for count in (done, total):
        if count is not None and (type(count) not in (int, float) or not math.isfinite(count) or count < 0):
            raise WizardError("invalid_progress", "Progress counts must be finite and non-negative.")
    if done is not None and total is not None and done > total:
        raise WizardError("invalid_progress", "Progress exceeds its observed total.")
    if unit is not None and (not isinstance(unit, str) or len(unit) > 64):
        raise WizardError("invalid_progress", "Invalid progress unit.")
    if detail is not None and not isinstance(detail, str):
        raise WizardError("invalid_progress", "Invalid progress detail.")
    detail = None if detail is None else re.sub(r"[\x00-\x08\x0b-\x1f]", "", detail)[:1024]
    percent = None if done is None or total is None else (100.0 if total == 0 else round(done * 100 / total, 1))
    return {"phase": phase, "done": done, "total": total, "unit": unit,
            "percent": percent, "detail": detail, "updatedAt": time.time()}


class WizardError(RuntimeError):
    def __init__(self, code, en, de=None, **parameters):
        super().__init__(en)
        self.code, self.message = code, {"en": en, "de": de or en}
        self.parameters = parameters


class Cancelled(WizardError):
    def __init__(self):
        super().__init__("cancelled", "Cancelled; completed work is retained.", "Abgebrochen; abgeschlossene Arbeit bleibt erhalten.")


def canonical(value):
    return json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(",", ":")).encode("utf-8")


def value_hash(value):
    return hashlib.sha256(canonical(value)).hexdigest()


def digest(path, algorithm="sha256", progress=None):
    result = hashlib.new(algorithm)
    done = 0
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1048576), b""):
            result.update(block); done += len(block)
            if progress: progress(done)
    return result.hexdigest()


def ordinary(path):
    path = Path(path).absolute()
    for entry in (path, *path.parents):
        if entry.is_symlink() or (hasattr(entry, "is_junction") and entry.is_junction()):
            raise WizardError("linked_path", "Wizard-owned paths cannot use links.", "Wizard-Arbeitsordner dürfen keine Verknüpfungen verwenden.")
    return path


def atomic_json(path, value):
    path = ordinary(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temp.open("xb") as stream:
            stream.write(canonical(value) + b"\n"); stream.flush(); os.fsync(stream.fileno())
        os.replace(temp, path)
    finally: temp.unlink(missing_ok=True)


def read_json(path, limit=2 * 1048576):
    path = ordinary(path)
    if path.stat().st_size > limit:
        raise WizardError("state_size", "Wizard state exceeds its supported size.", "Der Wizard-Status ist unerwartet groß.")
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict) or value.get("schema") != 1:
        raise WizardError("state_schema", "Unsupported wizard state.", "Nicht unterstützter Wizard-Status.")
    return value


@contextmanager
def file_lock(path):
    """Kernel-owned lock: hard process death releases it without deleting data."""
    path = ordinary(path); path.parent.mkdir(parents=True, exist_ok=True)
    stream = path.open("a+b")
    if stream.seek(0, os.SEEK_END) == 0: stream.write(b"0"); stream.flush()
    stream.seek(0)
    try:
        if os.name == "nt":
            import msvcrt
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except OSError as exc:
        stream.close()
        raise WizardError("already_running", "Another wizard run owns this workspace.", "Ein anderer Wizard-Lauf verwendet diesen Arbeitsordner.") from exc
    try: yield stream
    finally:
        try:
            stream.seek(0)
            if os.name == "nt": msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(stream, fcntl.LOCK_UN)
        finally: stream.close()


class Store:
    def __init__(self, root):
        self._lock = threading.RLock()
        self._live = {}
        self._progress_saved = {}
        self.root = ordinary(root)
        marker = self.root / "wizard-owner.json"
        if self.root.exists() and not marker.is_file():
            if any(self.root.iterdir()):
                raise WizardError("unowned_workspace", "Select a new wizard workspace.", "Bitte einen neuen Wizard-Arbeitsordner wählen.")
        self.root.mkdir(parents=True, exist_ok=True)
        expected = {"schema": 1, "owner": "GloomhavenVR.QuestWizard"}
        if marker.exists():
            if read_json(marker) != expected: raise WizardError("unowned_workspace", "Workspace ownership differs.")
        else: atomic_json(marker, expected)

    def session_dir(self, session):
        if not isinstance(session, str) or not re.fullmatch(r"[0-9a-f]{32}", session):
            raise WizardError("invalid_session", "Invalid wizard session ID.", "Ungültige Wizard-Sitzung.")
        return ordinary(self.root / "sessions" / session)

    def load(self, session):
        with self._lock:
            value = read_json(self.session_dir(session) / "state.json")
        if value.get("session") != session: raise WizardError("invalid_session", "Wizard session identity changed.")
        if value.get("status") == "running":
            try:
                with file_lock(self.root / "run.lock"):
                    # Re-read after acquiring ownership: another process may
                    # have completed between the first read and the lock.
                    value = read_json(self.session_dir(session) / "state.json")
                    if value.get("status") == "running":
                        value["status"] = "interrupted"
                        for row in value["stages"]:
                            if row["status"] == "running": row["status"] = "interrupted"
                        value["needsActions"] = [{"code": "interrupted", "message": {
                            "en": "The previous run stopped. Continue to verify and resume its retained work.",
                            "de": "Der vorige Lauf wurde beendet. Fortsetzen prüft und verwendet die erhaltene Arbeit."}}]
                        self.event(value, "interrupted")
            except WizardError as error:
                if error.code != "already_running": raise
        for row in value["stages"]:
            row.setdefault("progress", stage_progress("complete", 1, 1) if row["status"] == "complete" else stage_progress())
        return value

    @contextmanager
    def active(self, state):
        """Share the running engine's state with progress producers, never status readers."""
        session = state["session"]
        with self._lock:
            self._live[session] = state
        try: yield state
        finally:
            with self._lock:
                self.save(state)
                self._live.pop(session, None)

    def _state(self, session):
        return self._live.get(session) or read_json(self.session_dir(session) / "state.json")

    def _row(self, state, stage):
        if stage not in STAGES: raise WizardError("invalid_stage", "Invalid wizard stage.")
        return next(row for row in state["stages"] if row["id"] == stage)

    def save(self, state):
        with self._lock:
            state["updated"] = time.time()
            for row in state["stages"]:
                row.setdefault("progress", stage_progress("complete", 1, 1) if row["status"] == "complete" else stage_progress())
            atomic_json(self.session_dir(state["session"]) / "state.json", state)

    def amend(self, session, choices):
        # Never edit choices under an active run. The same kernel lock is held
        # by the engine through child shutdown and receipt publication.
        with file_lock(self.root / "run.lock"):
            state = self.load(session)
            if state["choices"] == choices: return state
            state.update(choices=choices, choicesKey=value_hash(choices), status="ready", needsActions=[])
            for row in state["stages"]:
                row.update(status="pending", details={}, progress=stage_progress())
                row.pop("waiting", None)
            self.event(state, "choices_updated")
            return state

    def create(self, choices):
        session = uuid.uuid4().hex
        state = {"schema": 1, "session": session, "choices": choices, "choicesKey": value_hash(choices),
                 "status": "ready", "stages": [{"id": name, "status": "pending", "attempts": 0} for name in STAGES],
                 "needsActions": [], "lastEvent": 0, "events": [], "created": time.time(),
                 "progress": {"completed": 0, "total": len(STAGES), "phase": None, "percent": None}}
        self.save(state)
        atomic_json(self.root / "latest-session.json", {"schema": 1, "session": session})
        return state

    def event(self, state, code, stage=None, **parameters):
        with self._lock: return self._event(state, code, stage, **parameters)

    def _event(self, state, code, stage=None, **parameters):
        state["lastEvent"] += 1
        item = {"sequence": state["lastEvent"], "time": time.time(), "code": code, "parameters": parameters}
        if stage: item["stage"] = stage
        state["events"] = (state["events"] + [item])[-128:]
        completed = sum(row["status"] == "complete" for row in state["stages"])
        state["progress"].update(completed=completed, phase=stage,
                                 percent=100 if state["status"] == "complete" else None)
        self.save(state)
        self._log(state["session"], item)
        return item

    def _log(self, session, item):
        """Bounded diagnostic history supplements the small polling/event state."""
        path = self.session_dir(session) / "logs/progress.log"
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.exists() and path.stat().st_size >= PROGRESS_LOG_BYTES:
            os.replace(path, path.with_name("progress.previous.log"))
        with path.open("ab") as stream: stream.write(canonical(item) + b"\n")

    def record(self, session, code, stage=None, **parameters):
        with self._lock: return self._event(self._state(session), code, stage, **parameters)

    def progress(self, session, stage, phase, done=None, total=None, unit=None, detail=None):
        value = stage_progress(phase, done, total, unit, detail)
        with self._lock:
            state = self._state(session); row = self._row(state, stage)
            old = row.get("progress", {}); row["progress"] = value
            clock = time.monotonic(); last = self._progress_saved.get((session, stage), 0)
            changed_phase = old.get("phase") != phase or old.get("total") != total
            completed = total is not None and done == total and old.get("done") != done
            if changed_phase or completed or clock - last >= PROGRESS_INTERVAL:
                self._progress_saved[(session, stage)] = clock
                self._event(state, "stage_progress", stage, **value)
            return dict(value)

    def waiting(self, session, stage, code, message, action="unity-open"):
        if not isinstance(message, dict) or set(message) != {"en", "de"} or any(not isinstance(x, str) or len(x) > 4096 for x in message.values()):
            raise WizardError("invalid_wait", "Waiting messages require English and German text.")
        if not isinstance(code, str) or not re.fullmatch(r"[a-z][a-z0-9_]{0,99}", code) or action not in ("unity-open", "unity-check"):
            raise WizardError("invalid_wait", "Invalid prerequisite action.")
        with self._lock:
            state = self._state(session); row = self._row(state, stage)
            old = row.get("waiting", {})
            waiting = {"stage": stage, "code": code, "message": dict(message), "action": action,
                       "checkAction": "unity-check", "since": old.get("since", time.time()) if old.get("code") == code else time.time(),
                       "nonce": old.get("nonce", uuid.uuid4().hex) if old.get("code") == code else uuid.uuid4().hex}
            row["waiting"] = waiting
            state["needsActions"] = [item for item in state.get("needsActions", []) if item.get("stage") != stage] + [waiting]
            if waiting != old: self._event(state, "stage_waiting", stage, reason=code, action=action)
            return dict(waiting)

    def clear_waiting(self, session, stage):
        with self._lock:
            state = self._state(session); row = self._row(state, stage)
            had_wait = row.pop("waiting", None)
            state["needsActions"] = [item for item in state.get("needsActions", []) if item.get("stage") != stage]
            if had_wait: self._event(state, "stage_wait_cleared", stage)

    def cancel(self, session):
        self.load(session)
        atomic_json(self.session_dir(session) / "cancel.json", {"schema": 1, "session": session, "time": time.time()})

    def check_cancel(self, session):
        if (self.session_dir(session) / "cancel.json").exists(): raise Cancelled()

    def clear_cancel(self, session): (self.session_dir(session) / "cancel.json").unlink(missing_ok=True)

    def receipt(self, session, stage):
        if stage not in STAGES: raise WizardError("invalid_stage", "Invalid wizard stage.")
        return self.session_dir(session) / "receipts" / (stage + ".json")

    def records(self, paths, progress=None):
        paths = [ordinary(raw) for raw in paths]
        total = sum(path.stat().st_size for path in paths)
        done = 0
        result = []
        for raw in paths:
            path = ordinary(raw)
            if self.root not in path.parents or not path.is_file():
                raise WizardError("receipt_path", "Stage outputs must be regular files inside this wizard workspace.")
            before = path.stat()
            hashed = digest(path, progress=lambda count: progress(done + count, total, path.name) if progress else None)
            after = path.stat(); done += after.st_size
            if progress: progress(done, total, path.name)
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                raise WizardError("output_changed", "A stage output changed while being recorded.")
            result.append({"path": path.relative_to(self.root).as_posix(), "size": after.st_size, "sha256": hashed})
        return result

    def valid(self, session, stage, key, *, report_progress=False):
        path = self.receipt(session, stage)
        if not path.is_file(): return None
        try:
            value = read_json(path, limit=64 * 1048576)
            if value.get("stage") != stage or value.get("key") != key or not value.get("outputs"): return None
            if any(type(row.get("size")) is not int or row["size"] < 0 for row in value["outputs"]): return None
            total = sum(row["size"] for row in value["outputs"]); done = 0
            for row in value["outputs"]:
                raw = row["path"]
                if not isinstance(raw, str): return None
                relative = PurePosixPath(raw)
                if (relative.is_absolute() or PureWindowsPath(raw).drive
                        or "\\" in raw or ":" in raw or ".." in relative.parts): return None
                target = ordinary(self.root / relative)
                if not target.is_file() or target.stat().st_size != row["size"]: return None
                def report(count):
                    self.check_cancel(session)
                    self.progress(session, stage, "receipt-verify", done + count, total, "bytes", target.name)
                if digest(target, progress=report if report_progress else None) != row["sha256"]: return None
                done += row["size"]
            return value
        except Cancelled: raise
        except (OSError, ValueError, TypeError, KeyError, WizardError): return None

    def publish(self, session, stage, key, paths, details):
        def report(done, total, name):
            self.check_cancel(session)
            self.progress(session, stage, "output-verify", done, total, "bytes", name)
        value = {"schema": 1, "stage": stage, "key": key, "outputs": self.records(paths, report), "details": details}
        if not value["outputs"]: raise WizardError("empty_stage", "Stage completed without verified output.")
        atomic_json(self.receipt(session, stage), value)
        return value
