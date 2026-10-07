"""Durable, ordered preparation checkpoints for an owned mutable Unity project.

Receipts name actual outputs, not the entire imported project. Later phases can
replace an earlier output contract; only its latest owner is checked on resume.
An unfinished destructive phase restores its bounded undo set before retrying.
No Library, original game file, or source checkout belongs to that undo set.
"""
from __future__ import annotations

import hashlib
from contextlib import contextmanager
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import sqlite3

from storage import BuildError, CONTENT_PATHS, _ordinary_owned, digest, value_hash, write_json

SCHEMA = 1
OWNER = "Quest preparation substage journal"


class Contracts(list):
    def __init__(self, paths, *, absent=()):
        super().__init__(paths)
        self.absent = set(absent)


def _relative(name):
    if not isinstance(name, str) or not name or "\\" in name or ":" in name:
        raise BuildError("Preparation contract has an invalid relative path.")
    path = PurePosixPath(name)
    if path.is_absolute() or any(part in ("", ".", "..") for part in name.split("/")):
        raise BuildError("Preparation contract escapes its generated project.")
    if path.parts[0] in ("Library", "Temp", "Logs", ".git"):
        raise BuildError("Preparation must not capture Unity caches or repository files.")
    return name


def _stamp(path):
    stat = _ordinary_owned(path).stat()
    return (stat.st_dev, stat.st_ino, stat.st_size, stat.st_mtime_ns, stat.st_ctime_ns)


def copy_changed(source, target, *, observed=None):
    """Resume an immutable copy by bytes, and reject mutations/short writes.

    Completed same-size/time copies are compared before retention. Source and
    target stamps here use the same stat API, including on Windows CPython.
    Writes use a sibling temporary, so a killed copy never becomes a whole file.
    """
    source, target = _ordinary_owned(Path(source)), _ordinary_owned(Path(target))
    before = _stamp(source)
    if not source.is_file(): raise BuildError("Preparation source is not an ordinary file: " + source.name)
    if target.exists() and target.is_file() and target.stat().st_size == before[2]:
        retained = _stamp(target)
        with source.open("rb") as left, target.open("rb") as right:
            same = True
            checksum = hashlib.sha256()
            while True:
                a, b = left.read(1024 * 1024), right.read(1024 * 1024)
                if a != b: same = False; break
                checksum.update(a)
                if not a: break
        if before != _stamp(source): raise BuildError("Preparation source changed during copy: " + source.name)
        if retained != _stamp(target):
            raise BuildError("Preparation target changed while qualifying its retained bytes: " + target.name)
        if same:
            if observed: observed(checksum.hexdigest())
            return str(target)
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = _ordinary_owned(target.with_name(target.name + ".quest-prepare-copy"))
    try:
        with source.open("rb") as original, temporary.open("wb") as destination:
            size = 0
            checksum = hashlib.sha256()
            for chunk in iter(lambda: original.read(1024 * 1024), b""):
                if destination.write(chunk) != len(chunk):
                    raise BuildError("Preparation copy wrote fewer bytes than requested: " + target.name)
                size += len(chunk)
                checksum.update(chunk)
            destination.flush()
        if size != before[2] or before != _stamp(source) or temporary.stat().st_size != size:
            raise BuildError("Preparation source changed during copy: " + source.name)
        shutil.copystat(source, temporary)
        published = _stamp(temporary)
        if before != _stamp(source):
            raise BuildError("Preparation source changed before publishing its copy: " + source.name)
        os.replace(temporary, target)
        # Rename can update ctime, but the accepted writer's identity, size and
        # final mtime must survive publication. Capture these only after closing
        # the writer, including on Windows. Never attach the streamed source hash
        # to a concurrent replacement/mutation of the destination.
        if _stamp(target)[:4] != published[:4]:
            raise BuildError("Preparation target changed while publishing its copy: " + target.name)
        if observed: observed(checksum.hexdigest())
    finally:
        temporary.unlink(missing_ok=True)
    return str(target)


class Preparation:
    def __init__(self, output, project, *, input_key, target, recipe, source_files=(), progress=None, reset=None, content_proofs=()):
        self.output, self.project = Path(output), _ordinary_owned(Path(project))
        self.progress, self.checked, self.index = progress, {}, 0
        self.copy_qualified = set()
        self.copy_counter = None
        self.content_proofs = {}
        for proof in content_proofs:
            if proof.get("path") not in CONTENT_PATHS or proof.get("inputKey") != input_key or proof.get("project") != self.project.relative_to(self.output).as_posix():
                raise BuildError("Preparation mutable-content proof has an unknown scope or input.")
            self.content_proofs[proof["path"]] = proof
        if self.content_proofs and set(self.content_proofs) != set(CONTENT_PATHS):
            raise BuildError("Preparation mutable-content proofs require the exact archive/manifest pair.")
        self.group = None
        relative = self.project.relative_to(self.output).as_posix()
        self.root = _ordinary_owned(self.output / "cache/prepare-resume" / self.project.name)
        self.journal = _ordinary_owned(self.root / "journal.json")
        self.identity = {"schema": SCHEMA, "owner": OWNER, "project": relative,
                         "inputKey": input_key, "target": target, "recipe": recipe}
        self.sources = [(Path(path), dict(row)) for path, row in source_files]
        # Read-only immutable snapshot files were already hashed while selecting
        # inputs. Store metadata qualification once, not a tree sweep per phase.
        self.source_stamps = {str(path): list(_stamp(path)) for path, _ in self.sources}
        if self.journal.exists():
            value = self._read(self.journal)
            if value.get("owner") != OWNER or value.get("project") != relative or value.get("schema") != SCHEMA:
                raise BuildError("Preparation journal ownership differs; existing files were retained.")
            if not re.fullmatch(r"[0-9a-f]{64}", value.get("inputKey", "")) or value.get("target") not in ("game", "startup", "probe") or type(value.get("recipe")) is not int or not isinstance(value.get("steps"), list):
                raise BuildError("Preparation journal identity/order is invalid; existing files were retained.")
            for step in value["steps"]:
                if not isinstance(step, dict) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", step.get("name", "")):
                    raise BuildError("Preparation journal step ownership differs; existing files were retained.")
            if any(value.get(key) != expected for key, expected in self.identity.items()):
                if reset is None: raise BuildError("Preparation inputs changed; owned project requires a new preparation.")
                reset()
                self._discard_owned_journal(value)
                value = None
            elif value.get("sources") != self.source_stamps:
                # A snapshot is immutable. Re-hash only metadata changes to allow
                # harmless copied timestamps while rejecting an edited source.
                for path, row in self.sources:
                    if value.get("sources", {}).get(str(path)) != self.source_stamps[str(path)]:
                        if path.stat().st_size != row["size"] or digest(path) != row["sha256"]:
                            raise BuildError("Preparation source changed since its checkpoint: " + path.name)
                value["sources"] = self.source_stamps
        else:
            value = None
            if self.project.exists() and reset is not None: reset()
        self.value = value or {**self.identity, "sources": self.source_stamps, "steps": [], "pending": None}
        if not isinstance(self.value.get("steps"), list): raise BuildError("Preparation journal has no ordered steps.")
        write_json(self.journal, self.value)
        self._rollback_pending()
        try:
            self._qualify()
        except BuildError:
            # A corrupt base overlay is repairable from the exact selected
            # snapshot. This differs from an interrupted later substage, whose
            # good base/native/audio outputs must remain in place.
            if getattr(self, "invalid_step", None) == "base-project" and reset is not None:
                reset()
                self.value["steps"] = []
                self.checked.clear()
                write_json(self.journal, self.value)
            else: raise
        # A kill after receipt publication can leave an already committed undo
        # directory. Its successful named step proves this private ownership.
        for step in self.value["steps"]:
            undo = _ordinary_owned(self.root / ("undo-" + step["name"]))
            if undo.exists(): shutil.rmtree(undo)
        copy_path = _ordinary_owned(self.root / ("copy-" + value_hash(self.identity) + ".sqlite"))
        self.copies = sqlite3.connect(copy_path)
        self.copies.execute("CREATE TABLE IF NOT EXISTS copies (path TEXT PRIMARY KEY, source TEXT, target TEXT, sha256 TEXT)")
        self.copy_writes = 0

    def copy(self, source, target):
        """Retain qualified base-copy files, committing in bounded small batches."""
        source, target = Path(source), Path(target)
        relative = _relative(target.relative_to(self.project).as_posix())
        before = list(_stamp(source))
        previous = self.copies.execute("SELECT source,target,sha256 FROM copies WHERE path=?", (relative,)).fetchone()
        if previous and json.loads(previous[0]) == before and target.is_file() and json.loads(previous[1]) == list(_stamp(target)):
            stamp = _stamp(target)
            # Across process restarts, metadata alone is never content proof.
            # Read only the retained target, once as it is consumed, rather than
            # sweeping the project or reading both immutable copies beforehand.
            if relative in self.copy_qualified or digest(target) == previous[2]:
                if stamp != _stamp(target): raise BuildError("Retained preparation copy changed while read: " + relative)
                self.copy_qualified.add(relative)
                self.checked[relative] = (stamp, previous[2])
                if self.copy_counter: self.copy_counter.add(1, relative)
                return str(target)
        def observed(checksum):
            if before != list(_stamp(source)): raise BuildError("Preparation copy source changed: " + source.name)
            stamp = _stamp(target)
            self.copies.execute("INSERT OR REPLACE INTO copies VALUES (?,?,?,?)",
                                (relative, json.dumps(before), json.dumps(stamp), checksum))
            self.checked[relative] = (stamp, checksum)
            self.copy_qualified.add(relative)
            self.copy_writes += 1
            if self.copy_writes % 128 == 0: self.copies.commit()
        result = copy_changed(source, target, observed=observed)
        if self.copy_counter: self.copy_counter.add(1, relative)
        return result

    def begin_copy(self, total):
        if self.progress:
            self.copy_counter = self.progress.Counter("prepare-project-copy", total, "files", "Copying qualified project inputs")

    def end_copy(self):
        if self.copy_counter: self.copy_counter.finish()
        self.copy_counter = None

    def _read(self, path):
        if not path.is_file(): raise BuildError("Preparation journal is not an ordinary file.")
        try: return json.loads(path.read_text(encoding="utf-8"))
        except (ValueError, OSError) as error: raise BuildError("Preparation journal cannot be read: " + path.name) from error

    def _path(self, relative): return _ordinary_owned(self.project / _relative(relative))

    def _discard_owned_journal(self, value):
        pending = value.get("pending")
        if pending:
            name = pending.get("name", "")
            if not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", name): raise BuildError("Preparation undo ownership differs.")
            undo = _ordinary_owned(self.root / ("undo-" + name))
            if undo.exists(): shutil.rmtree(undo)
        for step in value.get("steps", []):
            name = step.get("name", "")
            if not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", name): raise BuildError("Preparation undo ownership differs.")
            undo = _ordinary_owned(self.root / ("undo-" + name))
            if undo.exists(): shutil.rmtree(undo)
        prior_identity = {key: value[key] for key in self.identity}
        prior_copy = self.root / ("copy-" + value_hash(prior_identity) + ".sqlite")
        for suffix in ("", "-journal", "-wal", "-shm"):
            _ordinary_owned(Path(str(prior_copy) + suffix)).unlink(missing_ok=True)
        self.journal.unlink(missing_ok=True)

    def _observe(self, relative):
        path = self._path(relative)
        if not path.exists(): return {"path": relative, "absent": True}
        if not path.is_file(): raise BuildError("Preparation output is not a file: " + relative)
        before = _stamp(path)
        content_proof = self.content_proofs.get(relative)
        if content_proof and list(before) == content_proof["stamp"] and before[2] == content_proof["size"]:
            # The caller just hashed this exact coherent pair under its output
            # lock. These are invocation-local producer proofs, never old stats.
            self.checked[relative] = (before, content_proof["sha256"])
        previous = self.checked.get(relative)
        checksum = previous[1] if previous and previous[0] == before else digest(path)
        if before != _stamp(path): raise BuildError("Preparation output changed while read: " + relative)
        self.checked[relative] = (before, checksum)
        return {"path": relative, "size": before[2], "sha256": checksum}

    def _qualify(self):
        latest = {}
        for index, step in enumerate(self.value["steps"]):
            if not isinstance(step, dict) or not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", step.get("name", "")):
                raise BuildError("Preparation checkpoint has an unknown step.")
            if not isinstance(step.get("outputs"), list) or not step["outputs"]:
                raise BuildError("Preparation checkpoint has no output contracts: " + step["name"])
            for row in step["outputs"]:
                relative = _relative(row.get("path"))
                latest[relative] = (index, row)
        counter = self.progress.Counter("prepare-resume-verify-files", len(latest), "files", "Qualifying retained preparation contracts") if self.progress else None
        replaced = False
        for relative, (index, row) in latest.items():
            observed = self._observe(relative)
            proof = self.content_proofs.get(relative)
            if proof and observed == {"path": relative, "size": proof["size"], "sha256": proof["sha256"]}:
                # Addressables owns this accepted mutable pair after preparation.
                # Refresh only its latest phase owner; other assets stay exact.
                if row != observed: row.clear(); row.update(observed); replaced = True
            if observed != row:
                name = self.value["steps"][index]["name"]
                self.invalid_step = name
                raise BuildError("Retained preparation output changed in " + name + ": " + relative +
                                 "; completed steps and Unity Library were retained. Restore the file or use a fresh output folder.")
            if counter: counter.add(1, relative)
        if counter: counter.finish()
        if replaced: write_json(self.journal, self.value)

    def _emit(self, operation, status, detail):
        if self.progress:
            self.progress.event("operation:" + operation, 1 if status in ("complete", "reuse") else 0,
                                1, "operations", detail, status=status, operation=operation)

    @contextmanager
    def operation(self, name, count):
        if self.group is not None or type(count) is not int or count < 1:
            raise BuildError("Preparation operation requires an exact positive substage count.")
        self.group = {"name": name, "done": 0, "total": count, "reused": True}
        self._emit(name, "start", "Preparing operation: " + name)
        try:
            yield
            if self.group["done"] != count: raise BuildError("Preparation operation did not finish every planned substage: " + name)
            self._emit(name, "reuse" if self.group["reused"] else "complete", "Preparation operation finished: " + name)
        except BaseException as error:
            self._emit(name, "failed", "Preparation operation failed: " + name + "; " + str(error))
            raise
        finally:
            self.group = None

    def _substage(self, name, operation, status):
        if self.group is None or self.group["name"] != operation:
            raise BuildError("Preparation substage is outside its planned operation: " + name)
        if status in ("complete", "reuse"): self.group["done"] += 1
        if status != "reuse": self.group["reused"] = False
        if self.progress:
            self.progress.event("prepare-substage:" + name, self.group["done"], self.group["total"], "steps",
                                "Preparation substage: " + name, status=status, operation=operation)

    def _rollback_pending(self):
        pending = self.value.get("pending")
        if not pending: return
        name = pending.get("name", "")
        if not re.fullmatch(r"[a-z][a-z0-9-]{0,79}", name): raise BuildError("Preparation undo ownership differs.")
        undo = _ordinary_owned(self.root / ("undo-" + name))
        rows = pending.get("undo", [])
        if not isinstance(rows, list): raise BuildError("Preparation undo records are invalid.")
        # Check all retained originals before changing the first generated file.
        files = []
        for row in rows:
            self._path(row["path"])
            if row.get("directory"):
                for directory in row.get("directories", []): self._path(directory)
                files.extend(row["files"])
            elif not row.get("absent"): files.append(row)
        for row in files:
            self._path(row["path"])
            if row.get("absent"): continue
            saved = _ordinary_owned(undo / row["backup"])
            if saved.parent != undo or not saved.is_file() or saved.stat().st_size != row["size"] or digest(saved) != row["sha256"]:
                raise BuildError("Preparation undo bytes changed; no project file was overwritten: " + row["path"])
        for row in reversed(rows):
            target = self._path(row["path"])
            if row.get("directory"):
                if target.exists(): shutil.rmtree(target)
                target.mkdir(parents=True)
                for directory in row["directories"]: self._path(directory).mkdir(parents=True, exist_ok=True)
                for member in row["files"]: copy_changed(undo / member["backup"], self._path(member["path"]))
            elif row.get("absent"):
                if target.is_dir(): shutil.rmtree(target)
                else: target.unlink(missing_ok=True)
            else:
                copy_changed(undo / row["backup"], target)
        # Keep the accepted pending journal until restoration is entirely done;
        # a second interruption can safely restore the same original files.
        self.value["pending"] = None
        write_json(self.journal, self.value)
        if undo.exists(): shutil.rmtree(undo)

    def _undo(self, name, paths):
        root = _ordinary_owned(self.root / ("undo-" + name))
        if root.exists(): shutil.rmtree(root)  # same exact journal-owned basename, before any phase mutation
        root.mkdir(parents=True)
        rows = []
        sequence = 0
        def preserve(relative):
            nonlocal sequence
            path = self._path(relative)
            saved = root / str(sequence)
            copy_changed(path, saved, observed=lambda checksum: self.checked.__setitem__(relative, (_stamp(path), checksum)))
            row = {**self._observe(relative), "backup": str(sequence)}
            sequence += 1
            return row
        for relative in dict.fromkeys(paths):
            path = self._path(relative)
            if not path.exists(): rows.append({"path": relative, "absent": True}); continue
            if path.is_dir():
                directories, members = [], []
                for member in path.rglob("*"):
                    member = _ordinary_owned(member)
                    name = member.relative_to(self.project).as_posix()
                    if member.is_dir(): directories.append(name)
                    elif member.is_file(): members.append(preserve(name))
                    else: raise BuildError("Preparation undo subtree contains a non-file: " + name)
                rows.append({"path": relative, "directory": True, "directories": directories, "files": members})
            elif path.is_file(): rows.append(preserve(relative))
            else: raise BuildError("Preparation undo can preserve only owned files/directories: " + relative)
        return rows

    def run(self, name, operation, action, contracts, *, mutations=()):
        """Run one real function, committing only after its contracts qualify."""
        if self.index < len(self.value["steps"]):
            step = self.value["steps"][self.index]
            if (step["name"], step["operation"]) != (name, operation):
                raise BuildError("Preparation phase order changed; use a new recipe/input identity.")
            self.index += 1
            self._substage(name, operation, "reuse")
            return step.get("result")
        self._substage(name, operation, "start")
        paths = mutations() if callable(mutations) else mutations
        undo = self._undo(name, paths) if paths else []
        self.value["pending"] = {"name": name, "operation": operation, "undo": undo}
        write_json(self.journal, self.value)
        try:
            result = action()
            paths = contracts(result) if callable(contracts) else contracts
            outputs = [self._observe(_relative(str(path))) for path in dict.fromkeys(paths)]
            allowed_absent = getattr(paths, "absent", set())
            for row in outputs:
                if row.get("absent") and row["path"] not in allowed_absent:
                    raise BuildError("Preparation substage is missing its required output: " + name + ": " + row["path"])
            if not outputs or all(row.get("absent") for row in outputs):
                raise BuildError("Preparation substage produced no verifiable output: " + name)
            self.assert_sources()
            step = {"name": name, "operation": operation, "outputs": outputs}
            self.value["steps"].append(step)
            self.value["pending"] = None
            write_json(self.journal, self.value)
            self.index += 1
            self.copies.commit()
            root = self.root / ("undo-" + name)
            if root.exists(): shutil.rmtree(root)
            self._substage(name, operation, "complete")
            return result
        except BaseException as error:
            self._substage(name, operation, "failed")
            raise

    def assert_sources(self):
        # Stat the declared immutable files, not a recursive project/source scan.
        # A changed source is never accepted merely because an old inputKey exists.
        for path, _ in self.sources:
            if list(_stamp(path)) != self.source_stamps[str(path)]:
                raise BuildError("Preparation source changed while running: " + path.name)

    def finish(self):
        if self.value.get("pending") or self.index != len(self.value["steps"]):
            raise BuildError("Preparation contains an unfinished checkpoint.")
        self.assert_sources()
        self.copies.commit()

    def close(self):
        self.copies.commit()
        self.copies.close()


def manifest_contracts(project, manifests, *, extra=()):
    """Resolve paths already declared by bounded phase evidence, without globbing."""
    project, result, absent = Path(project), list(manifests) + list(extra), set()
    def visit(value):
        if isinstance(value, dict):
            for key in ("assetPath", "asset", "path", "metaPath", "sourceScene", "source"):
                name = value.get(key)
                if isinstance(name, str) and name.startswith(("Assets/", "Packages/", "ProjectSettings/", "QuestRecovery/", "QuestStartupEvidence/", "QuestCampaignEvidence/")):
                    if not (project / _relative(name)).is_dir(): result.append(name)
                elif isinstance(name, str) and name.startswith("StreamingAssets/"):
                    result.append(_relative("Assets/" + name))
            if isinstance(value.get("files"), dict):
                for name in value["files"]:
                    if name.startswith("Assets/"): result.append(_relative(name))
            if "originalPath" in value and "sha256" in value and "size" in value:
                name = _relative(value["originalPath"])
                if name != value.get("assetPath"):
                    result.append(name); absent.add(name)
            for name in value.get("manifestSha256", {}): result.append(_relative(name))
            for key in ("updatedManifests", "refreshedManifests"):
                for name in value.get(key, []):
                    if isinstance(name, str): result.append(_relative(name))
            for name, destination in value.get("pathMap", {}).items():
                result.extend((_relative(name), _relative(destination)))
                result.extend((_relative(name + ".meta"), _relative(destination + ".meta")))
                if name != destination: absent.update((name, name + ".meta"))
            for nested in value.values(): visit(nested)
        elif isinstance(value, list):
            for nested in value: visit(nested)
    for name in manifests:
        path = _ordinary_owned(project / _relative(name))
        if not path.is_file(): raise BuildError("Preparation phase evidence is missing: " + name)
        visit(json.loads(path.read_text(encoding="utf-8")))
    # Movie staging removes exactly these importer inputs; delivered file-backed
    # bytes and consuming scene contracts are retained independently.
    for name in list(result):
        if name.startswith("Assets/") and Path(name).suffix.lower() in (".mp4", ".mov", ".webm", ".ogv") and not (project / name).exists():
            absent.add(name)
    return Contracts(list(dict.fromkeys(result)), absent=absent)
