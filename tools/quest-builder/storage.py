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
            stream.flush()
            os.fsync(stream.fileno())
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
    temp = _ordinary_owned(destination.with_name(destination.name + ".staging"))
    owner = _ordinary_owned(destination.with_name(destination.name + ".staging.json"))
    expected = {"schema": 1, "owner": "Quest input snapshot", "destination": destination.name, "files": records}
    if owner.exists():
        if json.loads(owner.read_text()) != expected: raise BuildError("Snapshot staging belongs to different inputs.")
    elif temp.exists(): raise BuildError("Snapshot staging has no matching ownership record.")
    else: write_json(owner, expected)
    # The owner is durable before any directory/member creation. A killed copy
    # resumes only this exact known file set; original inputs stay read-only.
    for item in records:
        original = source / item["path"]
        copied = _ordinary_owned(temp / item["path"])
        copied.parent.mkdir(parents=True, exist_ok=True)
        if not copied.is_file() or copied.stat().st_size != item["size"] or digest(copied) != item["sha256"]:
            shutil.copyfile(original, copied)
            if copied.stat().st_size != item["size"] or digest(copied) != item["sha256"]:
                raise BuildError("An input changed while copied: " + item["path"] + "; retry after editing stops.")
    write_json(temp / ".snapshot.json", {"schema": 1, "files": records})
    temp.replace(destination)
    owner.unlink()


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


def _ordinary_owned(path: Path) -> Path:
    path = Path(path).absolute()
    for part in (path, *path.parents):
        if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
            raise BuildError("Generated workspace paths cannot be links: " + str(path))
    return path


@contextmanager
def output_lock(output: Path):
    """The persistent kernel guard is distinct from its transient status record.

    Never unlink a locked inode: a concurrent builder could otherwise lock a
    replacement file while this process still owns the first. Hard death frees
    the kernel guard; the retained status record cannot permanently block reuse.
    """
    guard = _ordinary_owned(output / ".builder.guard")
    lock = _ordinary_owned(output / ".builder.lock")
    stream = guard.open("a+b")
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
        stream.close(); raise BuildError("This output is locked by another running builder.") from exc
    nonce = uuid.uuid4().hex
    try:
        if lock.is_file():
            raw = lock.read_text(encoding="utf-8").strip()
            # Compatibility with the earlier O_EXCL/PID lock: only a dead
            # legacy owner can be recovered. PID checks never authorize killing.
            if raw.isdecimal():
                try: os.kill(int(raw), 0)
                except ProcessLookupError: pass
                except (PermissionError, OSError) as exc: raise BuildError("A legacy builder still owns this output.") from exc
                else: raise BuildError("A legacy builder still owns this output.")
            else:
                try: previous = json.loads(raw)
                except ValueError as exc: raise BuildError("Unrecognized builder lock record; retain it for inspection.") from exc
                if not isinstance(previous, dict) or previous.get("schema") != 1 or previous.get("lock") != "kernel-guard":
                    raise BuildError("Unrecognized builder lock record; retain it for inspection.")
        write_json(lock, {"schema": 1, "lock": "kernel-guard", "pid": os.getpid(), "nonce": nonce})
        yield
    finally:
        if lock.is_file():
            try:
                retained = json.loads(lock.read_text())
                if isinstance(retained, dict) and retained.get("nonce") == nonce: lock.unlink()
            except (ValueError, OSError): pass
        try:
            stream.seek(0)
            if os.name == "nt": msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(stream, fcntl.LOCK_UN)
        finally: stream.close()


def project_generation_paths(output: Path, project: Path, input_key: str):
    output, project = _ordinary_owned(output), _ordinary_owned(project)
    # The exact recipe is caller-owned; the path remains strictly confined to
    # one SHA256-named generated child, never an arbitrary user folder.
    if project.parent != output / "projects" or len(project.name) != 64 or any(c not in "0123456789abcdef" for c in project.name):
        raise BuildError("Unexpected generated project identity.")
    marker = _ordinary_owned(output / "cache/project-lifecycle" / project.name / "owner.json")
    backup = marker.parent / "Library"
    expected = {"schema": 2, "owner": "Quest generated project", "project": project.relative_to(output).as_posix(), "workspaceKey": project.name}
    if marker.exists():
        _ordinary_owned(marker)
        prior = json.loads(marker.read_text())
        legacy = {"schema": 1, "owner": "Quest generated project", "project": project.relative_to(output).as_posix(), "inputKey": input_key}
        if prior == legacy: write_json(marker, expected)
        elif prior != expected: raise BuildError("Generated project ownership changed.")
    else:
        if project.exists():
            settings = project / "QuestBuilderSettings.json"
            if not settings.is_file() or json.loads(settings.read_text()).get("inputKey") != input_key:
                raise BuildError("Existing incomplete project has no matching preparation owner.")
        write_json(marker, expected)
    return marker, _ordinary_owned(backup)


def restore_project_library(output: Path, project: Path, input_key: str):
    _, backup = project_generation_paths(output, project, input_key)
    if backup.exists():
        destination = _ordinary_owned(project / "Library")
        if destination.exists(): raise BuildError("Two retained Unity Libraries exist; neither was deleted.")
        project.mkdir(parents=True, exist_ok=True)
        os.replace(backup, destination)


@contextmanager
def regenerate_project(output: Path, project: Path, input_key: str):
    """Repair only this owned generated project while retaining its imported Library.

    The Library rename and ownership record survive hard process death. A later
    prepare restores it before checking/repairing the immutable stage contract.
    """
    restore_project_library(output, project, input_key)
    _, backup = project_generation_paths(output, project, input_key)
    library = _ordinary_owned(project / "Library")
    if library.exists(): os.replace(library, backup)
    try:
        if project.exists(): shutil.rmtree(project)
        yield
    finally:
        if backup.exists():
            project.mkdir(parents=True, exist_ok=True)
            if library.exists(): raise BuildError("Preparation created an unexpected second Unity Library; retained both.")
            os.replace(backup, library)


CONTENT_PATHS = ("Assets/StreamingAssets/quest-startup-content.zip", "Assets/Quest/Resources/quest-startup-content.json")


def recover_project_content(output: Path, project: Path, input_key: str):
    """Rollback an interrupted native repack/exclusion to its verified entry pair."""
    project_generation_paths(output, project, input_key)
    root = output / "cache/project-content-transactions" / project.name
    journal = _ordinary_owned(root / "pending.json")
    if not journal.exists(): return
    value = json.loads(journal.read_text())
    journal_key = value.get("inputKey")
    if value.get("schema") != 1 or not isinstance(journal_key, str) or len(journal_key) != 64 or any(c not in "0123456789abcdef" for c in journal_key) or value.get("project") != project.relative_to(output).as_posix():
        raise BuildError("Content recovery journal differs from its generated project.")
    if not isinstance(value.get("files"), list) or len(value["files"]) != 2 or {row.get("path") for row in value["files"]} != set(CONTENT_PATHS):
        raise BuildError("Content journal must preserve the exact archive/manifest pair.")
    for row in value["files"]:
        if row["path"] not in CONTENT_PATHS: raise BuildError("Content journal names an unsupported mutable output.")
        saved = _ordinary_owned(root / row["backup"])
        if saved.parent != root or not saved.is_file() or saved.stat().st_size != row["size"] or digest(saved) != row["sha256"]:
            raise BuildError("Retained content recovery bytes changed; no archive was overwritten.")
    manifest_row = next(row for row in value["files"] if row["path"] == CONTENT_PATHS[1])
    manifest = json.loads((root / manifest_row["backup"]).read_text())
    archive_row = next(row for row in value["files"] if row["path"] == CONTENT_PATHS[0])
    if manifest.get("inputKey") != journal_key or manifest.get("archive") != "quest-startup-content.zip" or manifest.get("archiveSha256") != archive_row["sha256"]:
        raise BuildError("Retained content backups do not prove their original transaction identity.")
    for row in value["files"]:
        target = _ordinary_owned(project / row["path"]); target.parent.mkdir(parents=True, exist_ok=True)
        # A copy fallback is used when the filesystem does not offer hardlinks.
        temp = target.with_name(target.name + ".restore-" + uuid.uuid4().hex)
        saved = root / row["backup"]
        try:
            try: os.link(saved, temp)
            except OSError: shutil.copyfile(saved, temp)
            os.replace(temp, target)
        finally: temp.unlink(missing_ok=True)
    excluded = _ordinary_owned(project / "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip")
    excluded.unlink(missing_ok=True)
    for path in (project / "Assets/StreamingAssets").glob("quest-startup-content.zip.repack-*"):
        suffix = path.name.rsplit("-", 1)[-1]
        if len(suffix) == 32 and all(c in "0123456789abcdef" for c in suffix): _ordinary_owned(path).unlink()
    journal.unlink()
    for row in value["files"]: (root / row["backup"]).unlink(missing_ok=True)


def content_records(project: Path, input_key: str):
    files = [record_file(_ordinary_owned(project / relative), relative) for relative in CONTENT_PATHS]
    manifest = json.loads((project / CONTENT_PATHS[1]).read_text())
    if manifest.get("inputKey") != input_key or manifest.get("archive") != "quest-startup-content.zip" or manifest.get("archiveSha256") != files[0]["sha256"]:
        raise BuildError("Content archive/manifest pair does not match this build.")
    return files


def project_content_valid(output: Path, project: Path, input_key: str):
    """A separate mutable ledger permits completed native repacks to stay warm."""
    ledger = output / "cache/project-content-transactions" / project.name / "complete.json"
    try:
        current = content_records(project, input_key)
        if ledger.exists():
            prior = json.loads(_ordinary_owned(ledger).read_text())
            return prior == {"schema": 1, "inputKey": input_key, "files": current}
        # Adopt a coherent older prepared project without invalidating Library.
        write_json(ledger, {"schema": 1, "inputKey": input_key, "files": current})
        return True
    except (OSError, ValueError, BuildError): return False


def publish_project_content(output: Path, project: Path, input_key: str):
    write_json(output / "cache/project-content-transactions" / project.name / "complete.json",
               {"schema": 1, "inputKey": input_key, "files": content_records(project, input_key)})


@contextmanager
def project_content_transaction(output: Path, project: Path, input_key: str):
    """Build-owned mutable outputs never invalidate immutable preparation receipts."""
    recover_project_content(output, project, input_key)
    root = output / "cache/project-content-transactions" / project.name
    root.mkdir(parents=True, exist_ok=True)
    files = []
    for index, row in enumerate(content_records(project, input_key)):
        relative = row["path"]
        target = _ordinary_owned(project / relative)
        saved = _ordinary_owned(root / ("content-" + str(index) + ".backup"))
        if saved.exists(): saved.unlink()  # previous death before journal publication, owned basename
        if target.suffix == ".zip":
            try:
                os.link(target, saved)
                if not os.path.samestat(target.stat(), saved.stat()): raise BuildError("Content hardlink identity changed.")
            except OSError:
                shutil.copyfile(target, saved)
                if digest(saved) != row["sha256"]: raise BuildError("Content changed while preserving recovery bytes.")
        else: shutil.copyfile(target, saved)
        if target.suffix != ".zip" and digest(saved) != row["sha256"]: raise BuildError("Content changed while preserving recovery bytes.")
        files.append({**row, "backup": saved.name})
    journal = root / "pending.json"
    write_json(journal, {"schema": 1, "project": project.relative_to(output).as_posix(), "inputKey": input_key, "files": files})
    try:
        yield
        # Commit only after the player and delivered content have passed their
        # original gates. A failed stage retains a restartable archive pair.
        publish_project_content(output, project, input_key)
        journal.unlink()
        for row in files: (root / row["backup"]).unlink(missing_ok=True)
    except BaseException:
        # Keep the journal on failed/cancelled builds. Rollback runs under the
        # next output lock, after the supervisor has stopped every owned child.
        raise


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
