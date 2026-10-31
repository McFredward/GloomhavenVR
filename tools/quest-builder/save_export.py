"""Byte-preserving native Gloomhaven save snapshots and recoverable replacement.

No serializer, provider service or campaign mutation runs here. A snapshot keeps
GlobalData, root data, every campaign/checkpoint and excluded-mode records together.
Import replaces a complete stopped-game snapshot only with explicit permission,
retaining the previous complete root. Merging serialized global indexes as files
would silently lose campaigns and is intentionally unsupported.
"""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import tempfile
import uuid
import zipfile

from storage import BuildError

FORMAT = "gloomhaven-native-dat-snapshot-v1"
MAX_FILES = 20000
MAX_FILE_BYTES = 128 * 1024 * 1024
MAX_TOTAL_BYTES = 1024 * 1024 * 1024
ROOT_PATTERN = r"GloomSaves(?:OpenBeta|ClosedBeta|Dev|LocalDev|InEditor)?"
JOURNAL = ".quest-save-transfer.json"
MARKER = ".quest-save-snapshot.json"
BACKUP_FOLDER = "QuestSaveBackups"


def _hash(raw: bytes) -> str:
    return hashlib.sha256(raw).hexdigest()


def _safe_path(path: Path) -> Path:
    path = path.absolute()
    if any(parent.is_symlink() for parent in (path, *path.parents)):
        raise BuildError("Save transfer must not follow filesystem links.")
    return path


def _relative(raw: str) -> str:
    if not isinstance(raw, str) or not raw or len(raw.encode()) > 1024 or "\\" in raw or ":" in raw:
        raise BuildError("Invalid relative save filename.")
    path = PurePosixPath(raw)
    if path.is_absolute() or any(part in ("", ".", "..") or part.endswith((".", " "))
                                 or any(ord(char) < 32 for char in part) for part in raw.split("/")):
        raise BuildError("Save snapshot contains a non-portable or escaping path.")
    reserved = {"con", "prn", "aux", "nul", *["com"+str(n) for n in range(1,10)], *["lpt"+str(n) for n in range(1,10)]}
    if any(part.split(".")[0].casefold() in reserved for part in path.parts):
        raise BuildError("Save snapshot contains a reserved Windows filename.")
    return path.as_posix()


def save_root(path: Path) -> Path:
    path = _safe_path(path)
    if not re.fullmatch(ROOT_PATTERN, path.name):
        path = path / "GloomSaves"
    if not re.fullmatch(ROOT_PATTERN, path.name):
        raise BuildError("Select the native GloomSaves root or its persistent-data parent.")
    return _safe_path(path)


def _inventory(root: Path) -> list[tuple[str, Path, os.stat_result]]:
    if not root.is_dir():
        raise BuildError("Native GloomSaves directory was not found.")
    rows = [];names=set();total=0
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise BuildError("Native save snapshot must not contain links.")
        if path.is_dir():
            continue
        info=path.stat()
        if not stat.S_ISREG(info.st_mode):
            raise BuildError("Native save snapshot contains a non-regular file.")
        relative=_relative(path.relative_to(root).as_posix())
        if relative==MARKER or relative.endswith(".ghvr-save-backup") or ".ghvr-save-write-" in relative:
            continue
        if relative.casefold() in names:
            raise BuildError("Save snapshot has filenames that collide on Windows.")
        names.add(relative.casefold());total+=info.st_size
        if info.st_size>MAX_FILE_BYTES or total>MAX_TOTAL_BYTES or len(rows)>=MAX_FILES:
            raise BuildError("Native save snapshot exceeds the bounded transfer limits.")
        rows.append((relative,path,info))
    if not {"GlobalData.dat","GloomSaven.dat"} <= {row[0] for row in rows}:
        raise BuildError("A complete native snapshot requires GlobalData.dat and GloomSaven.dat.")
    return rows


def _unchanged(before: os.stat_result, after: os.stat_result) -> bool:
    return (before.st_size,before.st_mtime_ns,before.st_ino)==(after.st_size,after.st_mtime_ns,after.st_ino)


def _write_json(path: Path, value: dict) -> None:
    path=_safe_path(path);path.parent.mkdir(parents=True,exist_ok=True)
    temporary=path.with_name(path.name+".new-"+uuid.uuid4().hex)
    try:
        with temporary.open("x",encoding="utf-8") as stream:
            json.dump(value,stream,indent=2,ensure_ascii=False);stream.write("\n");stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,path)
    finally:
        temporary.unlink(missing_ok=True)


def export_snapshot(source: Path, destination: Path) -> dict:
    root=save_root(source);destination=_safe_path(destination)
    if destination.exists() or destination==root or root in destination.parents:
        raise BuildError("Save export needs a new archive outside the native save root.")
    rows=_inventory(root);destination.parent.mkdir(parents=True,exist_ok=True)
    temporary=destination.with_name(destination.name+".new-"+uuid.uuid4().hex);records=[]
    try:
        with zipfile.ZipFile(temporary,"x",zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
            for relative,path,info in rows:
                raw=path.read_bytes()
                if len(raw)!=info.st_size or not _unchanged(info,path.stat()):
                    raise BuildError("Native save changed during export; close the PC game/stop the Quest app and retry.")
                archive.writestr("files/"+relative,raw)
                records.append({"path":relative,"bytes":len(raw),"sha256":_hash(raw)})
            current=_inventory(root)
            if [row[0] for row in rows]!=[row[0] for row in current] or any(not _unchanged(left[2],right[2]) for left,right in zip(rows,current)):
                raise BuildError("Native save tree changed during export; close the game and retry.")
            manifest={"schema":1,"format":FORMAT,"saveRoot":root.name,
                      "createdUtc":datetime.now(timezone.utc).isoformat(),"files":records,
                      "nativeBytesUnchanged":True,"containsGlobalIndex":True,
                      "preservesExcludedModeRecords":True,"cloudServicesUsed":False}
            archive.writestr("save-manifest.json",json.dumps(manifest,indent=2,ensure_ascii=False)+"\n")
        # Validate the emitted snapshot before publishing the new archive.
        validate_snapshot(temporary)
        os.replace(temporary,destination)
        return {"schema":1,"archive":str(destination),"files":len(records),
                "nativeBytes":sum(row["bytes"] for row in records),"saveRoot":root.name,"nativeBytesUnchanged":True}
    finally:
        temporary.unlink(missing_ok=True)


def validate_snapshot(path: Path, stage: Path | None = None) -> dict:
    path=_safe_path(path)
    if path.stat().st_size>MAX_TOTAL_BYTES+16*1024*1024:
        raise BuildError("Save snapshot archive is too large.")
    try:
        with zipfile.ZipFile(path) as archive:
            entries=archive.infolist()
            if len(entries)>MAX_FILES+1 or len({entry.filename for entry in entries})!=len(entries):
                raise BuildError("Save snapshot contains too many or duplicate members.")
            manifest_info=archive.getinfo("save-manifest.json")
            if manifest_info.file_size>8*1024*1024:
                raise BuildError("Save snapshot manifest is unexpectedly large.")
            manifest=json.loads(archive.read(manifest_info))
            if (not isinstance(manifest,dict) or manifest.get("schema")!=1 or manifest.get("format")!=FORMAT
                    or not re.fullmatch(ROOT_PATTERN,manifest.get("saveRoot","")) or not isinstance(manifest.get("files"),list)):
                raise BuildError("Unsupported native save snapshot format.")
            records=manifest["files"];expected={"save-manifest.json"};names=set();total=0
            if len(records)>MAX_FILES:
                raise BuildError("Save snapshot file count exceeds the transfer limit.")
            for row in records:
                if not isinstance(row,dict) or set(row)!={"path","bytes","sha256"}:
                    raise BuildError("Malformed native save snapshot file record.")
                relative=_relative(row["path"])
                if relative.casefold() in names or relative==MARKER or ".ghvr-save-" in relative:
                    raise BuildError("Save snapshot file collision or internal transfer marker.")
                names.add(relative.casefold());size=row["bytes"];total+=size if type(size) is int else 0
                if type(size) is not int or not 0<=size<=MAX_FILE_BYTES or total>MAX_TOTAL_BYTES or not re.fullmatch(r"[0-9a-f]{64}",str(row["sha256"])):
                    raise BuildError("Native save snapshot hash/size is invalid.")
                name="files/"+relative;expected.add(name);entry=archive.getinfo(name)
                if entry.is_dir() or entry.file_size!=size or stat.S_ISLNK(entry.external_attr>>16) or entry.flag_bits&1:
                    raise BuildError("Save snapshot member is linked, encrypted or has inconsistent size.")
                hasher=hashlib.sha256();destination=None;stream=None
                try:
                    if stage is not None:
                        destination=_safe_path(stage/PurePosixPath(relative));destination.parent.mkdir(parents=True,exist_ok=True)
                        stream=destination.open("xb")
                    with archive.open(entry) as member:
                        for chunk in iter(lambda:member.read(1024*1024),b""):
                            hasher.update(chunk)
                            if stream:stream.write(chunk)
                    if stream:stream.flush();os.fsync(stream.fileno())
                finally:
                    if stream:stream.close()
                if hasher.hexdigest()!=row["sha256"]:
                    raise BuildError("Native save snapshot payload hash failed: "+relative)
            if {entry.filename for entry in entries}!=expected or not {"GlobalData.dat","GloomSaven.dat"}<={row['path'] for row in records}:
                raise BuildError("Native save snapshot has missing global/root data or unexpected archive members.")
            return manifest
    except (KeyError,zipfile.BadZipFile,json.JSONDecodeError,TypeError,ValueError) as error:
        if isinstance(error,BuildError):raise
        raise BuildError("Invalid or damaged native save snapshot.") from error


def import_snapshot(source: Path, destination: Path, replace_existing: bool = False) -> dict:
    source=_safe_path(source);root=save_root(destination);parent=_safe_path(root.parent)
    manifest=validate_snapshot(source)
    if manifest["saveRoot"]!=root.name:
        raise BuildError("Save release-channel root differs; select its matching native GloomSaves directory.")
    if root.exists() and (not root.is_dir() or any(root.iterdir())) and not replace_existing:
        raise BuildError("Existing native saves are preserved. Use explicit --replace to import a complete snapshot with backup.")
    parent.mkdir(parents=True,exist_ok=True)
    if (parent/JOURNAL).exists():
        raise BuildError("A previous save transfer has a pending recovery journal; restore it before starting another import.")
    stage=parent/(".quest-save-import-"+uuid.uuid4().hex)
    identifier=datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")+"-"+uuid.uuid4().hex
    backup=parent/BACKUP_FOLDER/identifier/root.name
    moved_old=False;installed=False
    try:
        stage.mkdir();validate_snapshot(source,stage)
        # The marker is outside the native serialized format and only identifies
        # this completed snapshot transaction for crash recovery.
        marker={"schema":1,"format":FORMAT,"transferId":identifier,"saveRoot":root.name,"files":manifest["files"]}
        _write_json(stage/MARKER,marker)
        journal={"schema":1,"format":FORMAT,"transferId":identifier,"saveRoot":root.name,
                 "staging":stage.name,"backup":backup.relative_to(parent).as_posix()}
        _write_json(parent/JOURNAL,journal)
        if root.exists():
            _safe_path(backup).parent.mkdir(parents=True,exist_ok=True);os.replace(root,backup);moved_old=True
        os.replace(stage,root);installed=True
        (parent/JOURNAL).unlink()
        return {"schema":1,"saveRoot":str(root),"files":len(manifest["files"]),
                "backup":str(backup) if moved_old else None,"nativeBytesUnchanged":True,
                "globalIndexReplaced":True,"cloudServicesUsed":False}
    except BaseException:
        if moved_old and not installed and not root.exists():
            os.replace(backup,root);(parent/JOURNAL).unlink(missing_ok=True)
        elif not moved_old and not installed:
            (parent/JOURNAL).unlink(missing_ok=True)
        raise
    finally:
        if stage.exists() and not (parent/JOURNAL).exists():shutil.rmtree(stage)
