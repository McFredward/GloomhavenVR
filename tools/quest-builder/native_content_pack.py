"""Verified native archive repack using the standard ZIP/ZIP64 implementation.

Unity 2021's bundled Mono does not reliably honor ZIP NoCompression. Native
Addressables banks are already Unity-compressed; ZIP_STORED preserves their exact
bytes without a second compressor. Original files keep the Deflate policy.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import sys
import time
import uuid
import zipfile

MANIFEST = "Assets/Quest/Resources/quest-startup-content.json"
RECEIPT = "QuestStartupEvidence/native-content-pack.json"
NATIVE = "StreamingAssets/aa/"


def digest(path):
    with path.open("rb") as stream:
        return stream_hash(stream)


def stream_hash(stream):
    sha = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        sha.update(block)
    return sha.hexdigest()


def ordinary(path):
    for current in (path, *path.parents):
        if current.exists():
            info = current.lstat()
            if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                raise ValueError("Content packer paths must be ordinary, without links or junctions.")
    return path


def read_json(path, bound=64 * 1024 * 1024):
    ordinary(path)
    if not path.is_file() or path.stat().st_size > bound:
        raise ValueError("Content packer JSON is missing or exceeds its bound.")
    with path.open(encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict) or value.get("schema") != 1:
        raise ValueError("Content packer JSON must declare schema1.")
    return value


def publish_json(path, value):
    ordinary(path); path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".write-" + uuid.uuid4().hex)
    try:
        with temporary.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, indent=2, ensure_ascii=False)
            stream.write("\n"); stream.flush(); os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def relative(value):
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise ValueError("Content manifest path is invalid.")
    if PurePosixPath(value).is_absolute() or any(part in ("", ".", "..") for part in value.split("/")):
        raise ValueError("Content manifest path leaves the exact relative namespace.")
    return value


def validate_files(files):
    if not isinstance(files, list) or not files:
        raise ValueError("Owned content manifest has no exact file inventory.")
    paths = set()
    for row in files:
        if not isinstance(row, dict) or set(row) != {"path", "size", "sha256"}:
            raise ValueError("Owned content file record is invalid.")
        name = relative(row["path"])
        if name in paths:
            raise ValueError("Owned content has a duplicate file record.")
        paths.add(name)
        if type(row["size"]) is not int or row["size"] < 0 or not isinstance(row["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", row["sha256"]):
            raise ValueError("Owned content file lacks its exact size/SHA256.")


def verify_entries(archive, files):
    entries = archive.infolist(); by_name = {entry.filename: entry for entry in entries}
    if len(by_name) != len(entries) or set(by_name) != {row["path"] for row in files}:
        raise ValueError("Owned content ZIP entry set differs from its exact manifest.")
    for row in files:
        entry = by_name[row["path"]]
        if entry.is_dir() or entry.file_size != row["size"] or stat.S_ISLNK(entry.external_attr >> 16):
            raise ValueError("Owned content ZIP entry size/type differs from its manifest.")


def write_entry(archive, row, stream):
    info = zipfile.ZipInfo(row["path"], (2020, 1, 1, 0, 0, 0)); info.file_size = row["size"]
    info.compress_type = zipfile.ZIP_STORED if row["path"].startswith(NATIVE) and row["path"].endswith(".bundle") else zipfile.ZIP_DEFLATED
    sha, size = hashlib.sha256(), 0
    # Native output can exceed4GiB. Local ZIP64 fields avoid a late size failure.
    with archive.open(info, "w", force_zip64=True) as output:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(block); size += len(block); output.write(block)
    if size != row["size"] or sha.hexdigest() != row["sha256"]:
        raise ValueError("Owned content bytes changed during native catalog packaging.")


def pack(request):
    started = time.monotonic()
    if not isinstance(request, dict) or set(request) != {"schema", "projectRoot", "nativeRoot"} or request["schema"] != 1:
        raise ValueError("Native content packer requires its exact schema1 request.")
    for field in ("projectRoot", "nativeRoot"):
        if not isinstance(request[field], str) or not Path(request[field]).is_absolute():
            raise ValueError("Native content packer roots must be absolute owned paths.")
    project, native = (ordinary(Path(request[field])) for field in ("projectRoot", "nativeRoot"))
    project, native = project.resolve(), native.resolve()
    if not project.is_dir() or not native.is_dir() or not native.is_relative_to(project):
        raise ValueError("Native Addressables output must exist inside its owned project.")
    manifest_path = project / MANIFEST; manifest = read_json(manifest_path)
    validate_files(manifest.get("files"))
    if not isinstance(manifest.get("inputKey"), str) or not re.fullmatch(r"[0-9a-f]{64}", manifest["inputKey"]):
        raise ValueError("Owned content manifest lost its exact input identity.")
    name = relative(manifest.get("archive"))
    if "/" in name or not name.endswith(".zip"):
        raise ValueError("Owned content archive must be an exact ZIP filename.")
    archive_path = ordinary(project / "Assets/StreamingAssets" / name)
    if not archive_path.is_file() or digest(archive_path) != manifest.get("archiveSha256"):
        raise ValueError("Owned content archive changed before native catalog packaging.")
    retained = [row for row in manifest["files"] if not row["path"].startswith(NATIVE)]
    sources, added = {}, []
    for path in sorted(native.rglob("*")):
        ordinary(path)
        if path.is_dir() or path.name.endswith(".meta"):
            continue
        if not path.is_file():
            raise ValueError("Native output contains a non-file member.")
        member = relative(NATIVE + path.relative_to(native).as_posix())
        row = {"path": member, "size": path.stat().st_size, "sha256": digest(path)}
        sources[member] = path; added.append(row)
    if not added:
        raise ValueError("Native startup Addressables output is empty.")
    desired = sorted(retained + added, key=lambda row: row["path"]); validate_files(desired)
    reused = sorted(manifest["files"], key=lambda row: row["path"]) == desired
    temporary = archive_path.with_name(archive_path.name + ".repack-" + uuid.uuid4().hex)
    try:
        with zipfile.ZipFile(archive_path) as original:
            verify_entries(original, manifest["files"])
            if reused:
                for row in desired:
                    with original.open(row["path"]) as stream:
                        if stream_hash(stream) != row["sha256"]:
                            raise ValueError("Owned content ZIP file hash differs from its manifest.")
            else:
                with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as output:
                    for row in retained:
                        with original.open(row["path"]) as stream:
                            write_entry(output, row, stream)
                    for row in added:
                        with sources[row["path"]].open("rb") as stream:
                            write_entry(output, row, stream)
        if not reused:
            # The existing durable builder journal recovers interruptions between
            # publishing this mutable archive/manifest pair.
            manifest["files"] = desired; manifest["archiveSha256"] = digest(temporary)
            os.replace(temporary, archive_path); publish_json(manifest_path, manifest)
        receipt = {"schema": 1, "scope": "native-content-repack", "inputKey": manifest["inputKey"],
                   "reused": reused, "fileCount": len(desired),
                   "nativeBundleCount": sum(row["path"].endswith(".bundle") for row in added),
                   "nativeBundleBytes": sum(row["size"] for row in added if row["path"].endswith(".bundle")),
                   "nativeBundleCompression": "preserved-existing" if reused else "ZIP_STORED",
                   "retainedCompression": "preserved-existing" if reused else "ZIP_DEFLATED",
                   "archiveSha256": manifest["archiveSha256"], "archiveSize": archive_path.stat().st_size,
                   "manifestSha256": digest(manifest_path), "elapsedSeconds": time.monotonic() - started,
                   "allEntryBytesVerified": True, "headsetVerified": False}
        publish_json(project / RECEIPT, receipt)
        return receipt
    finally:
        temporary.unlink(missing_ok=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("--request", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        receipt = pack(read_json(args.request, 65536))
    except Exception as error:
        print("Native content packer stopped: " + str(error)[:2048], file=sys.stderr); return 1
    print(json.dumps({key: receipt[key] for key in ("schema", "reused", "fileCount", "archiveSha256")})); return 0


if __name__ == "__main__":
    raise SystemExit(main())
