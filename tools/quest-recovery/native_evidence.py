"""Capture and merge source-derived redirect and stripped-field evidence."""
import hashlib
import json
import os
from pathlib import Path
import stat
import time
import uuid

from recover import RecoveryError, sha256, ordinary_path, build_progress


LARGE_FILE = 8 * 1048576


class FileProofs:
    """Reuse witnessed hashes only inside one recovery invocation.

    The maintainer's repeated checkpoint report exposed growing full-output
    reads before and after every batch. A fresh process still hashes retained
    bytes; within that process, unchanged files keep the hash actually read or
    produced by its own writer. These proofs are never serialized. File identity,
    size, modification and change stamps invalidate a proof after a local write.
    """
    def __init__(self):
        self._files = {}

    @staticmethod
    def _stamp(path):
        value = ordinary_path(path).stat()
        if not stat.S_ISREG(value.st_mode):
            raise RecoveryError("Qualified recovery evidence is not a regular file: " + str(path))
        return value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns

    def digest(self, path, hasher=sha256):
        path = ordinary_path(path)
        before = self._stamp(path)
        previous = self._files.get(path)
        if previous is not None and previous[0] == before:
            return previous[1]
        digest = hasher(path)
        if self._stamp(path) != before:
            raise RecoveryError("Recovery evidence changed while being read: " + str(path))
        self._files[path] = before, digest
        return digest

    def remember(self, path, digest, size):
        """Record bytes just published by an invocation-owned streaming writer."""
        path = ordinary_path(path)
        stamp = self._stamp(path)
        if stamp[2] != size:
            raise RecoveryError("Published recovery evidence changed size: " + str(path))
        self._files[path] = stamp, digest

    def published_digest(self, path):
        """Require an unchanged writer/read proof, rather than adopting new bytes."""
        path = ordinary_path(path)
        previous = self._files.get(path)
        if previous is None or self._stamp(path) != previous[0]:
            raise RecoveryError("Published recovery checkpoint changed before journal cleanup: " + str(path))
        return previous[1]


def environment(directory):
    root = Path(directory) / "QuestRecovery"
    root.mkdir(parents=True, exist_ok=True)
    return {"QUEST_EXPORT_REDIRECT_IDENTITIES": str(root / "native-redirect-identities.jsonl"),
            "QUEST_EXPORT_NATIVE_RECIPES": str(root / "NativeRecipes")}


def _recipe_path(directory, row):
    name = row["yamlPath"]
    if (not isinstance(name, str) or Path(name).name != name or Path(name).suffix != ".yaml"
            or any(character in name for character in ("/", "\\", ":"))):
        raise RecoveryError("Native recipe escaped its captured evidence directory.")
    return ordinary_path(directory / name)


def _hash_recipe(path):
    size = path.stat().st_size
    counter = build_progress.Counter("recovery-native-recipe-hash", size, "bytes", path.name) if size >= LARGE_FILE else None
    digest = sha256(path, progress=counter.add if counter else None)
    if counter: counter.finish()
    return digest


def _read_index(path):
    # Stream the potentially large index rather than retaining its text and a
    # second splitlines copy alongside the decoded original identity rows.
    counter = build_progress.Counter("recovery-native-index-read", path.stat().st_size, "bytes", path.name)
    with path.open("rb") as stream:
        for raw in stream:
            row = json.loads(raw)
            counter.add(len(raw), path.name)
            yield row
    counter.finish()


def _copy_recipe(original, copied, expected):
    # Hash exactly the bytes copied. The old merger hashed before copy and again
    # after it, while rewriting even recipes already verified in the destination.
    # A private temporary file preserves the previous index/recipe on cancellation.
    temporary = copied.with_name(copied.name + ".tmp-" + uuid.uuid4().hex)
    size = original.stat().st_size
    counter = build_progress.Counter("recovery-native-recipe-copy", size, "bytes", original.name) if size >= LARGE_FILE else None
    digest = hashlib.sha256()
    try:
        with original.open("rb") as source, temporary.open("xb") as destination:
            for raw in iter(lambda: source.read(4 * 1048576), b""):
                destination.write(raw); digest.update(raw)
                if counter: counter.add(len(raw), original.name)
            destination.flush(); os.fsync(destination.fileno())
        if digest.hexdigest() != expected:
            raise RecoveryError("Captured original native recipe changed.")
        os.replace(temporary, copied)
        if counter: counter.finish()
    finally:
        temporary.unlink(missing_ok=True)
    return copied.stat().st_size


def _write_index(destination, values):
    temporary = destination.with_name(destination.name + ".tmp-" + uuid.uuid4().hex)
    counter = build_progress.Counter("recovery-native-index-write", len(values), "rows", destination.name)
    digest = hashlib.sha256(); size = 0
    try:
        with temporary.open("xb") as stream:
            for key in sorted(values):
                raw = (json.dumps(values[key], sort_keys=True) + "\n").encode("utf-8")
                stream.write(raw); digest.update(raw); size += len(raw)
                counter.add(1, destination.name)
            stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, destination)
        counter.finish()
    finally:
        temporary.unlink(missing_ok=True)
    return digest.hexdigest(), size


def merge(project, evidence, *, proofs=None):
    """Preserve exact native recipes, never the random exported GUID graph.

    The 2026-10-06 Windows report ends after collection 2761/2761. Native
    evidence merge follows that event and previously gave no activity while
    repeatedly hashing all retained recipes. That source-proven silent work is
    not evidence that the user's process deadlocked. Verify each distinct input
    recipe once, retain unchanged copies, and report actual index/file/byte work.
    Mutable indexes remain atomically replaced under the caller's merge journal.
    """
    project, evidence = ordinary_path(project), ordinary_path(evidence)
    proofs = proofs if proofs is not None else FileProofs()
    target = ordinary_path(project / "QuestRecovery")
    target.mkdir(exist_ok=True)
    records = {}
    started = time.monotonic()
    print("[Quest full recovery] Merging captured native indexes and recipes.", flush=True)
    indices = (("native-redirect-identities.jsonl", ("collection", "pathId")),
               ("NativeRecipes/index.jsonl", ("collection", "pathId")))
    for slot, (filename, keys) in enumerate(indices):
        build_progress.event("recovery-native-index-set", slot, len(indices), "indexes", filename, status="start")
        source = ordinary_path(evidence / "QuestRecovery" / filename)
        if not source.is_file():
            build_progress.event("recovery-native-index-set", slot + 1, len(indices), "indexes", filename, status="reuse")
            continue
        destination = ordinary_path(target / filename)
        destination.parent.mkdir(parents=True, exist_ok=True)
        values, recipes_by_name = {}, {}
        for path in (destination, source):
            if not path.is_file():
                continue
            for row in _read_index(path):
                key = tuple(row[name].casefold() if name == "collection" else row[name] for name in keys)
                previous = values.get(key)
                if previous is not None and previous != row:
                    raise RecoveryError("Native source evidence disagrees across export batches: " + repr(key))
                values[key] = row
                if filename.startswith("NativeRecipes/"):
                    original = _recipe_path(path.parent, row)
                    name = original.name.casefold()
                    expected, candidates = recipes_by_name.setdefault(name, (row["yamlSha256"], {}))
                    if expected != row["yamlSha256"]:
                        raise RecoveryError("Original native recipe changed across bounded exports.")
                    candidates[original] = row
        counter = build_progress.Counter("recovery-native-recipe-merge", len(recipes_by_name), "files")
        for _, (expected, candidates) in sorted(recipes_by_name.items()):
            first = next(iter(candidates)); copied = ordinary_path(destination.parent / first.name)
            # Windows names must identify the same recipe on all builder hosts.
            if any(path.name != copied.name for path in candidates):
                raise RecoveryError("Native recipe filenames collide case-insensitively.")
            copied_now = not copied.exists()
            if not copied_now:
                if proofs.digest(copied, _hash_recipe) != expected:
                    raise RecoveryError("Original native recipe changed across bounded exports.")
            else:
                size = _copy_recipe(first, copied, expected)
                proofs.remember(copied, expected, size)
            for original in candidates:
                if original == copied:
                    continue
                # The freshly copied first source was hashed while copying.
                if original == first and copied_now:
                    continue
                if proofs.digest(original, _hash_recipe) != expected:
                    raise RecoveryError("Captured original native recipe changed.")
            records[copied.relative_to(project).as_posix()] = {"path": copied.relative_to(project).as_posix(),
                                                             "sha256": expected, "bytes": copied.stat().st_size}
            counter.add(1, copied.name)
        counter.finish()
        digest, size = _write_index(destination, values)
        proofs.remember(destination, digest, size)
        relative = destination.relative_to(project).as_posix()
        records[relative] = {"path": relative, "sha256": digest, "bytes": size}
        build_progress.event("recovery-native-index-set", slot + 1, len(indices), "indexes", filename, status="complete")
    print("[Quest full recovery] Native evidence merged:", len(records), "files;",
          sum(row["bytes"] for row in records.values()), "bytes;", round(time.monotonic() - started, 3), "seconds.", flush=True)
    return [records[key] for key in sorted(records)]


def recipes(project):
    directory = Path(project) / "QuestRecovery/NativeRecipes"
    index = directory / "index.jsonl"
    if not index.is_file():
        return []
    result = []
    for line in index.read_text().splitlines():
        row = json.loads(line)
        relative = Path(row["yamlPath"])
        if relative.name != str(relative) or relative.suffix != ".yaml":
            raise RecoveryError("Native recipe escaped its original evidence directory.")
        path = directory / relative
        if sha256(path) != row["yamlSha256"]:
            raise RecoveryError("Original native recipe changed after recovery.")
        result.append({**row, "yamlPath": str(path)})
    return result
