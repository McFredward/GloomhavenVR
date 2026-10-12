"""Verified native archive repack using the standard ZIP/ZIP64 implementation.

Unity 2021's bundled Mono does not reliably honor ZIP NoCompression. Native
Addressables banks are already Unity-compressed; ZIP_STORED preserves their exact
bytes without a second compressor. Original files keep the Deflate policy.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import struct
import sys
import time
import uuid
import zipfile

MANIFEST = "Assets/Quest/Resources/quest-startup-content.json"
RECEIPT = "QuestStartupEvidence/native-content-pack.json"
NATIVE = "StreamingAssets/aa/"
PROGRESS_LOG_BYTES = 8 * 1024 * 1024


class ProgressSink:
    """Optional live observation confined to the caller's marked output logs."""
    def __init__(self, request):
        self.stream, self.active = None, None
        if os.environ.get("GHVRQ_WIZARD_PROGRESS") != "1": return
        raw = os.environ.get("GHVR_QUEST_CONTENT_PROGRESS_LOG")
        apk_raw = os.environ.get("GHVR_QUEST_OUTPUT_APK")
        if not raw or not apk_raw or not isinstance(request, dict): return
        handle = None
        try:
            project = ordinary(Path(request["projectRoot"]))
            apk, log = ordinary(Path(apk_raw)), ordinary(Path(raw))
            if not all(path.is_absolute() and ".." not in path.parts for path in (project, apk, log)): return
            if not re.fullmatch(r"[0-9a-f]{64}", project.name) or project.parent.name != "projects": return
            output = project.parent.parent
            if (apk.name != "GloomhavenVR-Quest.apk" or not re.fullmatch(r"[0-9a-f]{64}", apk.parent.name)
                    or apk.parent.parent != output / "builds"
                    or log != output / "logs" / ("content-pack-" + apk.parent.name[:12] + ".log")): return
            marker = read_json(output / ".quest-builder-output.json", 65536)
            if marker != {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"}: return
            log.parent.mkdir(exist_ok=True)
            if log.exists() and (not log.is_file() or log.stat().st_nlink != 1): return
            flags = os.O_WRONLY | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_CLOEXEC", 0)
            handle = os.open(log, flags, 0o600)
            info = os.fstat(handle)
            if (not stat.S_ISREG(info.st_mode) or info.st_nlink != 1
                    or getattr(info, "st_file_attributes", 0) & 0x400): return
            # Validate the opened file before truncation, including hard links.
            os.ftruncate(handle, 0)
            self.stream = os.fdopen(handle, "w", encoding="utf-8", newline="\n")
            handle = None
        except (OSError, ValueError, TypeError, KeyError):
            pass  # Diagnostics cannot prevent the owned content build.
        finally:
            if handle is not None:
                try: os.close(handle)
                except OSError: pass

    def __enter__(self): return self

    def __exit__(self, kind, error, traceback):
        if kind and self.active is not None:
            self.active.report("failed", force=True)
        if self.stream:
            try: self.stream.close()
            except OSError: pass
        self.stream = None

    def emit(self, counter, status):
        if self.stream is None: return
        detail = re.sub(r"[\x00-\x1f]", " ", counter.detail)[:512]
        event = {"schema": 1, "phase": counter.phase, "done": counter.done,
                 "total": counter.total, "unit": "bytes", "detail": detail,
                 "status": status, "operation": "content-bank"}
        try:
            line = "GHVRQ_PROGRESS " + json.dumps(event, ensure_ascii=False, separators=(",", ":")) + "\n"
            if self.stream.tell() + len(line.encode("utf-8")) > PROGRESS_LOG_BYTES:
                self.stream.seek(0); self.stream.truncate()
            self.stream.write(line)
            self.stream.flush()
        except (OSError, ValueError, UnicodeError):
            try: self.stream.close()
            except OSError: pass
            self.stream = None

    def counter(self, phase, total, detail):
        counter = ByteCounter(self, phase, total, detail)
        self.active = counter
        counter.report("start", force=True)
        return counter


class ByteCounter:
    """Report actual bytes from existing reads/writes, at most twice a second."""
    def __init__(self, sink, phase, total, detail):
        self.sink, self.phase, self.total, self.detail = sink, phase, total, detail
        self.done, self.last, self.closed = 0, 0., False

    def add(self, count, detail=None):
        self.done += count
        if detail is not None: self.detail = detail
        # A concurrently growing input is still validated by the packer. Keep
        # the observation truthful and bounded without changing that policy.
        self.total = max(self.total, self.done)
        self.report("progress")

    def report(self, status, *, force=False):
        if self.sink.stream is None: return
        now = time.monotonic()
        if not force and now - self.last < .5: return
        self.last = now
        self.sink.emit(self, status)

    def finish(self):
        if self.done != self.total: return
        self.report("complete", force=True)
        self.closed = True
        if self.sink.active is self: self.sink.active = None


def digest(path, progress=None):
    with path.open("rb") as stream:
        return stream_hash(stream, progress)


def stream_hash(stream, progress=None):
    sha = hashlib.sha256()
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        sha.update(block)
        if progress: progress.add(len(block))
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
        if entry.flag_bits & (1 | 0x20 | 0x40) or entry.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
            raise ValueError("Owned content ZIP uses unsupported compression or encryption.")


def write_entry(archive, row, stream, progress=None):
    info = zipfile.ZipInfo(row["path"], (2020, 1, 1, 0, 0, 0)); info.file_size = row["size"]
    info.compress_type = zipfile.ZIP_STORED if row["path"].startswith(NATIVE) and row["path"].endswith(".bundle") else zipfile.ZIP_DEFLATED
    sha, size = hashlib.sha256(), 0
    # Native output can exceed4GiB. Local ZIP64 fields avoid a late size failure.
    with archive.open(info, "w", force_zip64=True) as output:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            sha.update(block); size += len(block); output.write(block)
            if progress: progress.add(len(block), row["path"])
    if size != row["size"] or sha.hexdigest() != row["sha256"]:
        raise ValueError("Owned content bytes changed during native catalog packaging.")


def full_validation():
    """Developer-only whole-entry readback; normal builds keep archive SHA proof."""
    return (os.environ.get("GHVR_QUEST_VERIFICATION") == "full" or
            os.environ.get("GHVR_QUEST_VALIDATE_IMPORTED_ASSETS") == "1")


def file_identity(path):
    """Use the existing stdlib observer's FileID/change-time on Windows."""
    import storage
    value = ordinary(path).lstat()
    if not stat.S_ISREG(value.st_mode): raise ValueError("Owned content member is not a regular file.")
    portable = (value.st_dev, value.st_ino, value.st_size, value.st_mtime_ns, value.st_ctime_ns)
    stamp = storage._file_witness_stamp(path, value)
    if stamp is None and sys.platform == "win32" and value.st_nlink > 1:
        # Content transactions already hardlink a read-only ZIP preimage. The
        # persistent witness policy correctly excludes such files. For this
        # one open-source read, directory FileID/ChangeTime still detects any
        # mutation; never seed a persistent proof from this observation.
        try:
            observer = storage._windows_metadata or storage._WindowsFileMetadata()
            entries = observer.directory(path.parent)
            stamp = entries.get(path.name) if entries is not None else None
        except (OSError, AttributeError):
            pass
    return portable, stamp


def copy_compressed_entry(original, archive, entry, progress=None):
    """Preserve qualified Deflate/STORED payload bytes without a decompressor.

    Rebuild local headers from the validated central-directory sizes/CRC. Remove
    the input data descriptor and regenerate ZIP64 fields for the new offset.
    ZipFile writes the central directory and ZIP64 end records on close.
    """
    if entry.flag_bits & (1 | 0x20 | 0x40) or entry.compress_type not in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED):
        raise ValueError("Owned content ZIP uses unsupported compression or encryption.")
    stream = original.fp; stream.seek(entry.header_offset)
    header = stream.read(30)
    if len(header) != 30 or header[:4] != b"PK\x03\x04":
        raise ValueError("Owned content ZIP lost its local entry header.")
    fields = struct.unpack("<4s5H3I2H", header)
    name_size, extra_size = fields[-2:]
    name = stream.read(name_size)
    encoding = "utf-8" if fields[2] & 0x800 else "cp437"
    if (name.decode(encoding) != entry.filename or fields[3] != entry.compress_type
            or fields[2] != entry.flag_bits):
        raise ValueError("Owned content ZIP local header differs from its directory.")
    stream.seek(extra_size, 1)
    if stream.tell() + entry.compress_size > original.start_dir:
        raise ValueError("Owned content ZIP payload leaves its directory boundary.")
    info = copy.copy(entry); info.flag_bits &= ~8
    # Old ZIP64 extra fields contain input offsets/sizes. FileHeader and the
    # standard central writer regenerate them for this output, including >4GB.
    extra, cursor = bytearray(), 0
    while cursor < len(entry.extra):
        if cursor + 4 > len(entry.extra): raise ValueError("Malformed ZIP extra field.")
        kind, length = struct.unpack_from("<HH", entry.extra, cursor)
        end = cursor + 4 + length
        if end > len(entry.extra): raise ValueError("Malformed ZIP extra field.")
        if kind != 1: extra.extend(entry.extra[cursor:end])
        cursor = end
    info.extra = bytes(extra); info.header_offset = archive.fp.tell()
    archive._writecheck(info); archive._didModify = True
    archive.fp.write(info.FileHeader(info.file_size > zipfile.ZIP64_LIMIT or
                                     info.compress_size > zipfile.ZIP64_LIMIT))
    remaining = info.compress_size
    while remaining:
        block = stream.read(min(1024 * 1024, remaining))
        if not block: raise ValueError("Truncated owned ZIP compressed payload.")
        archive.fp.write(block); remaining -= len(block)
        if progress: progress.add(len(block), info.filename)
    archive.filelist.append(info); archive.NameToInfo[info.filename] = info
    archive.start_dir = archive.fp.tell()


def pack(request):
    # C# buffers stdout until child exit. A caller-owned sidecar makes measured
    # work visible during that wait without changing the single stdout receipt.
    with ProgressSink(request) as progress:
        return _pack(request, progress)


def _pack(request, progress):
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
    manifest_path = project / MANIFEST
    manifest_identity = file_identity(manifest_path); manifest = read_json(manifest_path)
    validate_files(manifest.get("files"))
    if not isinstance(manifest.get("inputKey"), str) or not re.fullmatch(r"[0-9a-f]{64}", manifest["inputKey"]):
        raise ValueError("Owned content manifest lost its exact input identity.")
    name = relative(manifest.get("archive"))
    if "/" in name or not name.endswith(".zip"):
        raise ValueError("Owned content archive must be an exact ZIP filename.")
    archive_path = ordinary(project / "Assets/StreamingAssets" / name)
    if not archive_path.is_file():
        raise ValueError("Owned content archive changed before native catalog packaging.")
    source_identity = file_identity(archive_path)
    source_hash = progress.counter("native-content-source-hash", archive_path.stat().st_size, archive_path.name)
    if digest(archive_path, source_hash) != manifest.get("archiveSha256"):
        raise ValueError("Owned content archive changed before native catalog packaging.")
    source_hash.finish()
    retained = [row for row in manifest["files"] if not row["path"].startswith(NATIVE)]
    sources, added, native_files = {}, [], []
    for path in sorted(native.rglob("*")):
        ordinary(path)
        if path.is_dir() or path.name.endswith(".meta"):
            continue
        if not path.is_file():
            raise ValueError("Native output contains a non-file member.")
        member = relative(NATIVE + path.relative_to(native).as_posix())
        native_files.append((member, path, path.stat().st_size))
    native_hash = progress.counter("native-content-native-hash", sum(size for _, _, size in native_files), "Native Android content")
    for member, path, size in native_files:
        native_hash.detail = member
        row = {"path": member, "size": size, "sha256": digest(path, native_hash)}
        sources[member] = path; added.append(row)
    if not added:
        raise ValueError("Native startup Addressables output is empty.")
    native_hash.finish()
    desired = sorted(retained + added, key=lambda row: row["path"]); validate_files(desired)
    reused = sorted(manifest["files"], key=lambda row: row["path"]) == desired
    temporary = archive_path.with_name(archive_path.name + ".repack-" + uuid.uuid4().hex)
    try:
        with zipfile.ZipFile(archive_path) as original:
            verify_entries(original, manifest["files"])
            full = full_validation()
            retained_bytes = sum(row["size"] if full else original.getinfo(row["path"]).compress_size
                                 for row in retained)
            writes = progress.counter("native-content-write",
                sum(row["size"] for row in desired) if reused and full else
                0 if reused else retained_bytes + sum(row["size"] for row in added),
                "Validating retained ZIP entries" if reused and full else
                "Reusing qualified archive" if reused else "Writing native Android content")
            if reused and full:
                for row in desired:
                    writes.detail = row["path"]
                    with original.open(row["path"]) as stream:
                        if stream_hash(stream, writes) != row["sha256"]:
                            raise ValueError("Owned content ZIP file hash differs from its manifest.")
            elif not reused:
                with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as output:
                    for row in retained:
                        if full:
                            with original.open(row["path"]) as stream:
                                write_entry(output, row, stream, writes)
                        else:
                            copy_compressed_entry(original, output, original.getinfo(row["path"]), writes)
                    for row in added:
                        with sources[row["path"]].open("rb") as stream:
                            write_entry(output, row, stream, writes)
        writes.finish()
        # Bind the earlier SHA qualification to the exact closed source that
        # was raw-copied. A replacement during native hashing never becomes a
        # newly accepted archive. Unknown Windows stamps use a byte fallback.
        if (file_identity(archive_path) != source_identity or
                file_identity(manifest_path) != manifest_identity):
            raise ValueError("Owned content archive/manifest changed during native catalog packaging.")
        if os.name == "nt" and source_identity[1] is None:
            if digest(archive_path) != manifest["archiveSha256"]:
                raise ValueError("Owned content archive changed during native catalog packaging.")
        if not reused:
            # The existing durable builder journal recovers interruptions between
            # publishing this mutable archive/manifest pair.
            final_hash = progress.counter("native-content-final-hash", temporary.stat().st_size, archive_path.name)
            manifest["files"] = desired; manifest["archiveSha256"] = digest(temporary, final_hash)
            final_hash.finish()
            if (file_identity(archive_path) != source_identity or
                    file_identity(manifest_path) != manifest_identity):
                raise ValueError("Owned content archive/manifest changed before publication.")
            os.replace(temporary, archive_path); publish_json(manifest_path, manifest)
        else:
            # Source qualification already hashed the unchanged archive.
            progress.counter("native-content-final-hash", 0, "Retained archive hash already qualified").finish()
        receipt = {"schema": 1, "scope": "native-content-repack", "inputKey": manifest["inputKey"],
                   "reused": reused, "fileCount": len(desired),
                   "nativeBundleCount": sum(row["path"].endswith(".bundle") for row in added),
                   "nativeBundleBytes": sum(row["size"] for row in added if row["path"].endswith(".bundle")),
                   "nativeBundleCompression": "preserved-existing" if reused else "ZIP_STORED",
                   "retainedCompression": "preserved-existing" if reused or not full else "ZIP_DEFLATED",
                   "archiveSha256": manifest["archiveSha256"], "archiveSize": archive_path.stat().st_size,
                   "manifestSha256": digest(manifest_path), "elapsedSeconds": time.monotonic() - started,
                   "allEntryBytesVerified": full, "archiveBytesVerified": True,
                   "retainedEntryReadback": "full" if full else "omitted-qualified-archive",
                   "compressedRecordsCopied": not reused and not full, "headsetVerified": False}
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
