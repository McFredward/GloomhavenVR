"""Portable original Mono compiler inputs for updates of an installed Quest APK.

IL2CPP metadata is not the original managed IL. Generate this small, exact seed
only from the user's selected PC installation, then retain it inside their APK.
The game-free Builder distribution contains this recipe, never these DLLs.
No player data, identity, signing key or complete game asset bank is included.
"""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import zipfile

MEMBER = "assets/Quest/update-seed.zip"
MANIFEST = "assets/Quest/update-seed.json"
SCOPE = "quest-portable-update-seed-v1"
MAX_TOTAL = 256 * 1048576
MAX_MEMBER = 64 * 1048576
MAX_JSON = 2 * 1048576
MAX_FILES = 1024
_SHA = re.compile(r"[0-9a-f]{64}\Z")
_storage_helper = None


class SeedError(ValueError):
    """A portable compiler seed is absent, incompatible or damaged."""


def _canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")


def _key(value):
    return hashlib.sha256(_canonical(value)).hexdigest()


def _storage():
    # Installer aliases exist only during its isolated import. Capture our exact
    # standard-library sibling privately rather than importing a global storage.
    global _storage_helper
    if _storage_helper is None:
        spec = importlib.util.spec_from_file_location("quest_update_seed_storage", Path(__file__).with_name("storage.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        _storage_helper = module
    return _storage_helper


def _stamp(path):
    value = Path(path).lstat()
    if (not stat.S_ISREG(value.st_mode) or value.st_nlink != 1
            or getattr(value, "st_file_attributes", 0) & 0x400):
        raise SeedError("Compiler seed input is not a regular unlinked file: " + Path(path).name)
    strong = _storage()._file_witness_stamp(path, value)
    return list(strong) if strong is not None else None


def _observation(path):
    value = Path(path).lstat()
    return [value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns], _stamp(path)


def _name(name):
    reserved = {"con", "prn", "aux", "nul", *("com" + str(i) for i in range(1, 10)), *("lpt" + str(i) for i in range(1, 10))}
    if (not isinstance(name, str) or len(name) > 512 or not name.startswith("Managed/")
            or not name.endswith(".dll") or any(ord(c) < 32 or ord(c) == 127 for c in name)
            or any(c in name for c in "\\:<>|?*")
            or any(part in ("", ".", "..") or part.endswith((".", " "))
                   or part.split(".")[0].casefold() in reserved for part in name.split("/"))):
        raise SeedError("Compiler seed contains an unsafe or unsupported assembly path.")
    return name


def _identity(inputs):
    if not isinstance(inputs, dict) or not _SHA.fullmatch(inputs.get("inputKey", "")):
        raise SeedError("Compiler seed needs the immutable original game input identity.")
    files = inputs.get("game", {}).get("files")
    if not isinstance(files, list):
        raise SeedError("Compiler seed has no original managed assembly inventory.")
    rows, seen = [], set()
    for row in files:
        if not isinstance(row, dict) or not isinstance(row.get("path"), str):
            raise SeedError("Compiler seed original inventory is invalid.")
        if not row["path"].startswith("Managed/"):
            continue
        name = _name(row["path"])
        if (name.casefold() in seen or type(row.get("size")) is not int or not 0 < row["size"] <= MAX_MEMBER
                or not isinstance(row.get("sha256"), str) or not _SHA.fullmatch(row["sha256"])):
            raise SeedError("Compiler seed has ambiguous, excessive or invalid original assembly evidence.")
        seen.add(name.casefold())
        rows.append({"path": name, "size": row["size"], "sha256": row["sha256"]})
    rows.sort(key=lambda row: row["path"])
    total = sum(row["size"] for row in rows)
    if (not 2 <= len(rows) <= MAX_FILES or not 0 < total <= MAX_TOTAL
            or not {"Managed/GH.Runtime.dll", "Managed/GH.Shared.dll"}.issubset(row["path"] for row in rows)):
        raise SeedError("Compiler seed lacks bounded exact original game assemblies; use the matching PC installation.")
    return {"schema": 1, "scope": SCOPE, "inputKey": inputs["inputKey"],
            "managedKey": _key(rows), "files": rows, "bytes": total}


def _json(raw):
    if len(raw) > MAX_JSON:
        raise SeedError("Compiler seed metadata exceeds its bound.")
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:
                raise SeedError("Compiler seed metadata contains duplicate keys.")
            result[key] = value
        return result
    value = json.loads(raw, object_pairs_hook=unique)
    if not isinstance(value, dict):
        raise SeedError("Compiler seed metadata must be an object.")
    return value


def _read(path):
    if not path.exists():
        return None
    before = _observation(path)
    with path.open("rb") as stream:
        value = _json(stream.read(MAX_JSON + 1))
    if before != _observation(path):
        raise SeedError("Compiler seed metadata changed while reading.")
    return value


def _receipt(path):
    # Interrupted or damaged optional proofs lose their fast stamp path, never
    # their exact owner or the unchanged seed/assembly bytes. Rehash only this
    # bounded subset and write a fresh proof after successful qualification.
    try:
        return _read(path) or {}
    except (SeedError, ValueError, UnicodeError):
        return {}


def _plain(entry):
    mode = entry.external_attr >> 16
    if (entry.is_dir() or entry.flag_bits & 1 or stat.S_ISLNK(mode)
            or stat.S_IFMT(mode) not in (0, stat.S_IFREG)):
        raise SeedError("Compiler seed rejects directories, links and encrypted entries.")


def _pair(archive):
    selected = {}
    for entry in archive.infolist():
        folded = entry.filename.casefold()
        if folded not in (MEMBER.casefold(), MANIFEST.casefold()):
            continue
        if entry.filename not in (MEMBER, MANIFEST) or folded in selected:
            raise SeedError("APK has ambiguous compiler seed members.")
        selected[folded] = entry
        _plain(entry)
    if not selected:
        return None
    if len(selected) != 2:
        raise SeedError("APK contains an incomplete compiler seed; repair it from the matching PC installation.")
    data, metadata = selected[MEMBER.casefold()], selected[MANIFEST.casefold()]
    if not 0 < data.file_size <= MAX_TOTAL or not 0 < metadata.file_size <= MAX_JSON:
        raise SeedError("APK compiler seed exceeds its bounded archive or metadata size.")
    return data, metadata


def _metadata(archive, base):
    pair = _pair(archive)
    if pair is None:
        return None
    data, metadata = pair
    expected = _identity(base["manifest"])
    value = _json(archive.read(metadata))
    if (set(value) != {*expected, "archiveSha256", "archiveBytes"}
            or any(value.get(name) != body for name, body in expected.items())
            or type(value.get("archiveBytes")) is not int or value["archiveBytes"] != data.file_size
            or not isinstance(value.get("archiveSha256"), str) or not _SHA.fullmatch(value["archiveSha256"])
            or base.get("inputKey") != expected["inputKey"]):
        raise SeedError("APK compiler seed does not match its immutable original assembly inventory.")
    return value


def inspect_seed(apk, base):
    """Bounded discovery only; complete bytes are checked on first extraction."""
    try:
        before = _observation(apk)
        with zipfile.ZipFile(apk) as archive:
            metadata = _metadata(archive, base)
        if before != _observation(apk):
            raise SeedError("The selected APK changed while inspecting its compiler seed.")
        if metadata is None:
            return {"available": False, "state": "missing", "bytes": 0,
                    "reason": "This older Quest installation has no original Mono compiler seed. Update it once using the matching PC game installation; later mod updates can use the Quest alone."}
        return {"available": True, "state": "available", "bytes": metadata["bytes"],
                "archiveBytes": metadata["archiveBytes"], "managedFiles": len(metadata["files"]),
                "inputKey": metadata["inputKey"], "reason": None}
    except (SeedError, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        return {"available": False, "state": "invalid", "bytes": 0, "reason": str(error)}


def _owner(builder, root, identity):
    root = builder._ordinary_owned(root)
    marker = builder._ordinary_owned(root / ".owner.json")
    expected = {"schema": 1, "scope": SCOPE, "identity": identity}
    if marker.exists():
        if _read(marker) != expected:
            raise SeedError("Compiler seed cache belongs to different inputs; no existing files were replaced.")
    else:
        if root.exists() and any(root.iterdir()):
            raise SeedError("Compiler seed cache has no ownership record; no existing files were replaced.")
        builder.write_json(marker, expected)
    return root


def _closed(builder, path, size, digest, proof):
    path = builder._ordinary_owned(path)
    if not path.exists():
        return False
    before = _observation(path)
    current = before[1]
    if before[0][2] != size:
        return False
    accepted = (current is not None and proof == {"sha256": digest, "stamp": current}) or _hash_file(path) == digest
    if before != _observation(path):
        raise SeedError("Compiler seed file changed while qualifying: " + path.name)
    return {"sha256": digest, "stamp": current} if accepted else False


def _hash_file(path):
    before = _observation(path)
    result = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1048576), b""):
            result.update(chunk)
    if before != _observation(path):
        raise SeedError("Compiler seed file changed while reading: " + Path(path).name)
    return result.hexdigest()


def _copy_checked(source, target, size, digest, *, progress=None):
    temporary = target.with_name(target.name + ".partial")
    total, checksum = 0, hashlib.sha256()
    try:
        # Never open a borrowed hardlinked partial for writing.
        if temporary.exists():
            _stamp(temporary)
            temporary.unlink()
        with temporary.open("xb") as output:
            while chunk := source.read(1048576):
                total += len(chunk)
                if total > size:
                    raise SeedError("Compiler seed member expanded beyond its declared bound.")
                checksum.update(chunk)
                output.write(chunk)
                if progress:
                    progress(len(chunk))
            output.flush()
            os.fsync(output.fileno())
        if total != size or checksum.hexdigest() != digest:
            raise SeedError("Compiler seed member does not match its original SHA-256.")
        if target.exists():
            _stamp(target)
        temporary.replace(target)
        return {"sha256": digest, "stamp": _stamp(target)}
    finally:
        temporary.unlink(missing_ok=True)


def build(builder, data, inputs, output):
    """Generate only exact original managed IL; return APK replacement members."""
    try:
        identity = _identity(inputs)
        root = _owner(builder, Path(output) / "cache/update-seeds" / _key(identity), identity)
        archive, manifest, receipt = [builder._ordinary_owned(root / name) for name in ("update-seed.zip", "update-seed.json", "build-proof.json")]
        previous = _receipt(receipt)
        old_manifest = _receipt(manifest)
        sources = {}
        if (old_manifest is not None and all(old_manifest.get(name) == value for name, value in identity.items())
                and type(old_manifest.get("archiveBytes")) is int and 0 < old_manifest["archiveBytes"] <= MAX_TOTAL
                and isinstance(old_manifest.get("archiveSha256"), str) and _SHA.fullmatch(old_manifest["archiveSha256"])):
            archive_proof = _closed(builder, archive, old_manifest["archiveBytes"], old_manifest["archiveSha256"], previous.get("archive"))
            if archive_proof:
                for row in identity["files"]:
                    source = builder._ordinary_owned(Path(data) / row["path"])
                    proof = _closed(builder, source, row["size"], row["sha256"], previous.get("sources", {}).get(row["path"]))
                    if not proof:
                        raise SeedError("The PC game differs from its immutable compiler seed input: " + row["path"])
                    sources[row["path"]] = proof
                builder.write_json(receipt, {"sources": sources, "archive": archive_proof})
                return {MEMBER: archive, MANIFEST: _canonical(old_manifest)}
        temporary = builder._ordinary_owned(root / "update-seed.zip.partial")
        if temporary.exists():
            _stamp(temporary)
            temporary.unlink()
        try:
            with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=1) as stream:
                for row in identity["files"]:
                    source = builder._ordinary_owned(Path(data) / row["path"])
                    before = _observation(source)
                    checksum, total = hashlib.sha256(), 0
                    entry = zipfile.ZipInfo(row["path"], (1980, 1, 1, 0, 0, 0))
                    entry.compress_type = zipfile.ZIP_DEFLATED
                    entry._compresslevel = 1
                    entry.external_attr = (stat.S_IFREG | 0o644) << 16
                    with source.open("rb") as original, stream.open(entry, "w") as target:
                        for chunk in iter(lambda: original.read(1048576), b""):
                            total += len(chunk)
                            if total > row["size"]:
                                raise SeedError("Original assembly changed while packaging its compiler seed.")
                            checksum.update(chunk)
                            target.write(chunk)
                    if (total != row["size"] or checksum.hexdigest() != row["sha256"] or before != _observation(source)):
                        raise SeedError("Original assembly changed while packaging its compiler seed: " + row["path"])
                    sources[row["path"]] = {"sha256": row["sha256"], "stamp": before[1]}
            if archive.exists():
                _stamp(archive)
            temporary.replace(archive)
        finally:
            temporary.unlink(missing_ok=True)
        before = _observation(archive)
        value = {**identity, "archiveSha256": _hash_file(archive), "archiveBytes": before[0][2]}
        if value["archiveBytes"] > MAX_TOTAL:
            raise SeedError("Generated compiler seed archive exceeds its portable size bound.")
        if before != _observation(archive):
            raise SeedError("Generated compiler seed changed before publication.")
        builder.write_json(manifest, value)
        builder.write_json(receipt, {"sources": sources, "archive": {"sha256": value["archiveSha256"], "stamp": before[1]}})
        return {MEMBER: archive, MANIFEST: _canonical(value)}
    except (SeedError, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile) as error:
        raise builder.BuildError("Portable Quest compiler seed could not be prepared: " + str(error)) from error


def extract(builder, apk, base, output, *, progress=None):
    """Extract/reuse a bounded compiler subset; repair only altered cache files.

    The caller has qualified the installed APK and holds the workspace lock.
    Stamps are exact FileId/ChangeTime on NTFS/REFS, inode/ctime on POSIX. Unknown
    file systems reread bounded seed files instead of trusting creation times.
    """
    try:
        before = _observation(apk)
        with zipfile.ZipFile(apk) as outer:
            metadata = _metadata(outer, base)
            if metadata is None:
                return inspect_seed(apk, base)
            owner = {"inputKey": metadata["inputKey"], "archiveSha256": metadata["archiveSha256"],
                     "managedKey": metadata["managedKey"]}
            root = _owner(builder, Path(output) / "cache/device-update-seeds" / _key(owner), owner)
            archive, receipt = [builder._ordinary_owned(root / name) for name in ("update-seed.zip", "extract-proof.json")]
            previous = _receipt(receipt)
            archive_proof = _closed(builder, archive, metadata["archiveBytes"], metadata["archiveSha256"], previous.get("archive"))
            if not archive_proof:
                with outer.open(MEMBER) as source:
                    archive_proof = _copy_checked(source, archive, metadata["archiveBytes"], metadata["archiveSha256"])
            proofs, repairs, reused = {}, 0, 0
            with zipfile.ZipFile(archive) as seed:
                entries = seed.infolist()
                expected = {row["path"]: row for row in metadata["files"]}
                if len(entries) != len(expected):
                    raise SeedError("Compiler seed archive has an unexpected member count.")
                seen = set()
                for entry in entries:
                    _plain(entry)
                    name = _name(entry.filename)
                    if (name.casefold() in seen or name not in expected or entry.file_size != expected[name]["size"]):
                        raise SeedError("Compiler seed archive differs from the immutable original member roster.")
                    seen.add(name.casefold())
                for index, row in enumerate(metadata["files"], 1):
                    target = builder._ordinary_owned(root / "game" / row["path"])
                    target.parent.mkdir(parents=True, exist_ok=True)
                    proof = _closed(builder, target, row["size"], row["sha256"], previous.get("files", {}).get(row["path"]))
                    if not proof:
                        with seed.open(row["path"]) as source:
                            proof = _copy_checked(source, target, row["size"], row["sha256"])
                        repairs += 1
                    else:
                        reused += 1
                    proofs[row["path"]] = proof
                    if progress:
                        progress(index, len(expected), row["path"])
                    if index % 16 == 0:
                        builder.write_json(receipt, {"archive": archive_proof, "files": proofs})
            if before != _observation(apk):
                raise SeedError("The selected APK changed while extracting its compiler seed.")
            builder.write_json(receipt, {"archive": archive_proof, "files": proofs})
            return {"available": True, "state": "available", "dataRoot": str(root / "game"),
                    "bytes": metadata["bytes"], "reusedFiles": reused, "repairedFiles": repairs,
                    "proof": {"schema": 1, "scope": SCOPE, "inputKey": metadata["inputKey"],
                              "archiveSha256": metadata["archiveSha256"],
                              "managedKey": metadata["managedKey"], "managedFiles": metadata["files"],
                              "source": "qualified-quest-installation", "originalMonoInputsQualified": True}}
    except (SeedError, OSError, ValueError, KeyError, TypeError, zipfile.BadZipFile, RuntimeError) as error:
        raise builder.BuildError("Portable Quest compiler seed could not be read: " + str(error)) from error
