"""Isolated immutable snapshots and hash-verified stage receipts."""

from __future__ import annotations

from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import shutil
import uuid


class BuildError(RuntimeError):
    """An actionable conversion failure, safe to display without credentials."""


def digest(path: Path) -> str:
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def canonical(value) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def value_hash(value) -> str:
    return hashlib.sha256(canonical(value)).hexdigest()


def write_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp-" + uuid.uuid4().hex)
    try:
        with temp.open("wb") as stream:
            stream.write(canonical(value) + b"\n")
        temp.replace(path)
    finally:
        temp.unlink(missing_ok=True)


def record_file(path: Path, relative: str) -> dict:
    before = path.stat()
    hashed = digest(path)
    after = path.stat()
    if (before.st_size, before.st_mtime_ns) != (after.st_size, after.st_mtime_ns):
        raise BuildError("An input changed while being hashed: " + relative + "; retry after the edit completes.")
    return {"path": relative, "sha256": hashed, "size": after.st_size}


def inventory(root: Path, paths: list[str] | None = None) -> list[dict]:
    if paths is None:
        paths = sorted(str(p.relative_to(root).as_posix()) for p in root.rglob("*") if p.is_file())
    result = []
    for relative in sorted(set(paths)):
        rel = Path(relative)
        if rel.is_absolute() or ".." in rel.parts:
            raise BuildError("Unsafe input path: " + relative)
        path = root / rel
        if not path.is_file():
            raise BuildError("A selected input is missing: " + relative)
        # Source DLL links created by worktree-setup are deliberate read-only dependencies.
        # Other symlinks are not followed into arbitrary private directories.
        contained_link = path.is_symlink() and root.resolve() in path.resolve().parents
        if path.is_symlink() and not contained_link and not relative.startswith(("libs/RuntimeDeps/", "libs/Natives/")):
            raise BuildError("A conversion input is an unsupported symlink: " + relative)
        result.append(record_file(path, rel.as_posix()))
    return result


def verify_files(root: Path, records: list[dict]) -> bool:
    for item in records:
        path = root / item["path"]
        if not path.is_file() or path.is_symlink() or path.stat().st_size != item["size"]:
            return False
        if digest(path) != item["sha256"]:
            return False
    return True


def snapshot(source: Path, records: list[dict], destination: Path) -> None:
    receipt = destination / ".snapshot.json"
    if receipt.is_file():
        prior = json.loads(receipt.read_text(encoding="utf-8"))
        if prior.get("files") == records and verify_files(destination, records):
            return
        raise BuildError("Immutable snapshot is corrupt: " + str(destination) + "; remove this snapshot and retry.")
    if destination.exists():
        raise BuildError("An incomplete snapshot occupies " + str(destination) + "; remove it and retry.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    temp = destination.with_name(destination.name + ".tmp-" + uuid.uuid4().hex)
    try:
        for item in records:
            original = source / item["path"]
            copied = temp / item["path"]
            copied.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(original, copied)
            if copied.stat().st_size != item["size"] or digest(copied) != item["sha256"]:
                raise BuildError("An input changed while copied: " + item["path"] + "; retry after editing stops.")
        write_json(temp / ".snapshot.json", {"schema": 1, "files": records})
        temp.replace(destination)
    finally:
        if temp.exists():
            shutil.rmtree(temp)


def ensure_output(output: Path, repo: Path, game_data: Path | None = None) -> Path:
    raw = output.absolute()
    resolved = raw.resolve()
    if raw != resolved:
        raise BuildError("Output paths must not use symlinks; select a separate real directory.")
    protected = [repo.resolve()]
    if game_data:
        protected.append(game_data.resolve())
    for source in protected:
        if resolved == source or resolved in source.parents:
            raise BuildError("Output must not contain or replace a source checkout/game installation.")
    if game_data and (game_data.resolve() in resolved.parents):
        raise BuildError("Output must not be inside the original game installation.")
    if repo.resolve() in resolved.parents and not (
            resolved == repo.resolve() / ".planning/quest3-local" or
            repo.resolve() / ".planning/quest3-local" in resolved.parents):
        raise BuildError("In-repository output is allowed only under ignored .planning/quest3-local.")
    marker = resolved / ".quest-builder-output.json"
    if marker.exists():
        if marker.is_symlink():
            raise BuildError("Output marker must not be a symlink.")
        try:
            value = json.loads(marker.read_text(encoding="utf-8"))
        except (ValueError, OSError) as exc:
            raise BuildError("The output-directory marker is invalid.") from exc
        if value != {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"}:
            raise BuildError("The directory belongs to a different build operation.")
    else:
        if resolved.exists() and any(resolved.iterdir()):
            raise BuildError("Output must be empty or an existing marked Quest-builder output directory.")
        resolved.mkdir(parents=True, exist_ok=True)
        write_json(marker, {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"})
    return resolved


@contextmanager
def output_lock(output: Path):
    lock = output / ".builder.lock"
    try:
        fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    except FileExistsError as exc:
        raise BuildError("This output is locked by another builder. If it has exited, remove .builder.lock and retry.") from exc
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            stream.write(str(os.getpid()))
        yield
    finally:
        lock.unlink(missing_ok=True)


class Stages:
    def __init__(self, output: Path):
        self.output = output

    def path(self, name: str, key: str) -> Path:
        return self.output / "receipts" / name / (key + ".json")

    def valid(self, name: str, key: str) -> dict | None:
        path = self.path(name, key)
        if not path.is_file():
            return None
        try:
            value = json.loads(path.read_text(encoding="utf-8"))
            if value.get("schema") != 1 or value.get("stage") != name or value.get("key") != key:
                return None
            records = value["outputs"]
            if not records:
                return None
            for item in records:
                candidate = (self.output / item["path"]).resolve()
                if self.output.resolve() not in candidate.parents:
                    return None
            return value if verify_files(self.output, records) else None
        except (ValueError, OSError, KeyError):
            return None

    def run(self, name: str, key: str, action) -> dict:
        prior = self.valid(name, key)
        if prior:
            print(name + ": reusing verified output", flush=True)
            return prior
        self.path(name, key).unlink(missing_ok=True)
        print(name + ": running", flush=True)
        try:
            paths, details = action()
            records = [record_file(p, p.relative_to(self.output).as_posix()) for p in paths]
            if not records:
                raise BuildError(name + " produced no verifiable files.")
            value = {"schema": 1, "stage": name, "key": key, "outputs": records, "details": details}
            write_json(self.path(name, key), value)
            return value
        except BaseException as exc:
            # Failed/cancelled stages never acquire a successful receipt.
            write_json(self.output / "last-failure.json", {"schema": 1, "stage": name, "key": key,
                       "error": type(exc).__name__, "message": str(exc)})
            raise
