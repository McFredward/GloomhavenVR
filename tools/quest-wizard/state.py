"""Owned local wizard sessions and atomic, hash-verified stage receipts."""
from __future__ import annotations
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import re
import time
import uuid

STAGES = ("tools", "source", "unity", "profile", "inspect", "build", "install")


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


def digest(path, algorithm="sha256"):
    result = hashlib.new(algorithm)
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1048576), b""): result.update(block)
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
        value = read_json(self.session_dir(session) / "state.json")
        if value.get("session") != session: raise WizardError("invalid_session", "Wizard session identity changed.")
        return value

    def save(self, state):
        state["updated"] = time.time()
        atomic_json(self.session_dir(state["session"]) / "state.json", state)

    def amend(self, session, choices):
        # Never edit choices under an active run. The same kernel lock is held
        # by the engine through child shutdown and receipt publication.
        with file_lock(self.root / "run.lock"):
            state = self.load(session)
            if state["choices"] == choices: return state
            state.update(choices=choices, choicesKey=value_hash(choices), status="ready", needsActions=[])
            for row in state["stages"]: row.update(status="pending", details={})
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
        state["lastEvent"] += 1
        item = {"sequence": state["lastEvent"], "time": time.time(), "code": code, "parameters": parameters}
        if stage: item["stage"] = stage
        state["events"] = (state["events"] + [item])[-128:]
        completed = sum(row["status"] == "complete" for row in state["stages"])
        state["progress"].update(completed=completed, phase=stage,
                                 percent=100 if state["status"] == "complete" else None)
        self.save(state)
        return item

    def cancel(self, session):
        self.load(session)
        atomic_json(self.session_dir(session) / "cancel.json", {"schema": 1, "session": session, "time": time.time()})

    def check_cancel(self, session):
        if (self.session_dir(session) / "cancel.json").exists(): raise Cancelled()

    def clear_cancel(self, session): (self.session_dir(session) / "cancel.json").unlink(missing_ok=True)

    def receipt(self, session, stage):
        if stage not in STAGES: raise WizardError("invalid_stage", "Invalid wizard stage.")
        return self.session_dir(session) / "receipts" / (stage + ".json")

    def records(self, paths):
        result = []
        for raw in paths:
            path = ordinary(raw)
            if self.root not in path.parents or not path.is_file():
                raise WizardError("receipt_path", "Stage outputs must be regular files inside this wizard workspace.")
            before = path.stat(); hashed = digest(path); after = path.stat()
            if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
                raise WizardError("output_changed", "A stage output changed while being recorded.")
            result.append({"path": path.relative_to(self.root).as_posix(), "size": after.st_size, "sha256": hashed})
        return result

    def valid(self, session, stage, key):
        path = self.receipt(session, stage)
        if not path.is_file(): return None
        try:
            value = read_json(path, limit=64 * 1048576)
            if value.get("stage") != stage or value.get("key") != key or not value.get("outputs"): return None
            for row in value["outputs"]:
                raw = row["path"]
                if not isinstance(raw, str): return None
                relative = PurePosixPath(raw)
                if (relative.is_absolute() or PureWindowsPath(raw).drive
                        or "\\" in raw or ":" in raw or ".." in relative.parts): return None
                target = ordinary(self.root / relative)
                if not target.is_file() or target.stat().st_size != row["size"] or digest(target) != row["sha256"]: return None
            return value
        except (OSError, ValueError, TypeError, KeyError, WizardError): return None

    def publish(self, session, stage, key, paths, details):
        value = {"schema": 1, "stage": stage, "key": key, "outputs": self.records(paths), "details": details}
        if not value["outputs"]: raise WizardError("empty_stage", "Stage completed without verified output.")
        atomic_json(self.receipt(session, stage), value)
        return value
