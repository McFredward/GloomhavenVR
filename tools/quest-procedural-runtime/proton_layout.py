"""Strict, source-owned Android layout for the pinned ARM64EC Wine runtime.

Wine Unix code remains native Bionic ELF. Its executable/library names must fit
Android's lib*.so package inventory; only dynamic loader strings are relocated.
PE engine/worker bytes and Wine code/program offsets are never rewritten.
"""
from __future__ import annotations
import hashlib
import io
from pathlib import Path, PurePosixPath
import posixpath
import struct
import tarfile

BACKEND = "proton-arm64ec-fex"
UNIX_PREFIX = "lib/wine/aarch64-unix/"
PE_PREFIX = "lib/wine/aarch64-windows/"
DISABLED_DRIVERS = ("winex11", "winealsa", "winepulse", "winegstreamer", "winedmo",
                    "winebus", "winevulkan", "lsteamclient")
# All source-owned filename mappings are short enough for existing DT_NEEDED
# strings. Windows PE module names remain unchanged.
NATIVE_MAP = {
    "ntdll.so": "libqn.so", "win32u.so": "libqw.so", "ws2_32.so": "libqs.so",
    "crypt32.so": "libq00.so", "opencl.so": "libq01.so", "dwrite.so": "libq02.so",
    "wineps.so": "libq03.so", "winebth.so": "libq04.so", "winspool.so": "libq05.so",
    "odbc32.so": "libq06.so", "windows.media.speech.so": "libq07.so",
    "msv1_0.so": "libq08.so", "nsiproxy.so": "libq09.so", "dnsapi.so": "libq10.so",
    "bcrypt.so": "libq11.so", "opengl32.so": "libq12.so", "localspl.so": "libq13.so",
    "netapi32.so": "libq14.so", "kerberos.so": "libq15.so", "qcap.so": "libq.so",
    "avicap32.so": "libq17.so", "secur32.so": "libq18.so", "mountmgr.so": "libq19.so",
    "ctapi32.so": "libq20.so",
}


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def patch_native(data: bytes, names: dict[str, str] = NATIVE_MAP) -> tuple[bytes, list[dict]]:
    """Relocate only referenced ELF dynamic strings, preserving all offsets.

    Replacing a bare SONAME without also qualifying each DT_NEEDED import would
    leave an apparently valid but unlaunchable APK. The independent auditor
    subsequently checks actual bindings of the resulting native inventory.
    """
    if len(data) < 64 or data[:6] != b"\x7fELF\x02\x01" or struct.unpack_from("<H", data, 18)[0] != 183:
        raise RuntimeError("Pinned Proton Unix payload is not little-endian ARM64 ELF.")
    phoff = struct.unpack_from("<Q", data, 32)[0]
    phsize, phcount = struct.unpack_from("<HH", data, 54)
    if phsize < 56 or phcount > 1024 or phoff + phsize * phcount > len(data):
        raise RuntimeError("Pinned Proton ELF program headers are invalid.")
    loads, dynamic = [], None
    for index in range(phcount):
        kind, _flags, offset, virtual, _physical, size, _mem, _align = struct.unpack_from("<IIQQQQQQ", data, phoff + index * phsize)
        if offset + size > len(data):
            raise RuntimeError("Pinned Proton ELF segment exceeds its file.")
        if kind == 1:
            loads.append((virtual, size, offset))
        elif kind == 2:
            if dynamic is not None:
                raise RuntimeError("Pinned Proton ELF has multiple dynamic segments.")
            dynamic = offset, size
    if dynamic is None:
        raise RuntimeError("Pinned Proton core ELF has no dynamic loader metadata.")
    records = []
    for offset in range(dynamic[0], dynamic[0] + dynamic[1], 16):
        if offset + 16 > len(data):
            raise RuntimeError("Pinned Proton ELF dynamic metadata is truncated.")
        tag, value = struct.unpack_from("<qQ", data, offset)
        if not tag:
            break
        records.append((tag, value))
    strings = [value for tag, value in records if tag == 5]
    lengths = [value for tag, value in records if tag == 10]
    if len(strings) != 1 or len(lengths) != 1:
        raise RuntimeError("Pinned Proton ELF dynamic string table is ambiguous.")
    mapped = [offset + strings[0] - virtual for virtual, size, offset in loads if virtual <= strings[0] < virtual + size]
    if len(mapped) != 1 or mapped[0] + lengths[0] > len(data):
        raise RuntimeError("Pinned Proton ELF dynamic string table is outside its file.")
    result, edits, seen = bytearray(data), [], set()
    for tag, value in records:
        if tag not in (1, 14, 15, 29):  # NEEDED, SONAME, RPATH, RUNPATH only
            continue
        if value >= lengths[0]:
            raise RuntimeError("Pinned Proton ELF dynamic string index is out of bounds.")
        start = mapped[0] + value
        end = data.find(b"\0", start, mapped[0] + lengths[0])
        if end < 0:
            raise RuntimeError("Pinned Proton ELF dynamic string is unterminated.")
        original = data[start:end].decode("ascii")
        replacement = "$ORIGIN" if tag in (15, 29) else names.get(original, original)
        if replacement == original or start in seen:
            continue
        encoded = replacement.encode("ascii")
        if len(encoded) > end - start:
            raise RuntimeError("Android native Wine name does not fit its bounded ELF string slot.")
        result[start:end] = encoded + b"\0" * (end - start - len(encoded))
        seen.add(start)
        edits.append(dict(offset=start, tag=tag, original=original, replacement=replacement))
    if len(result) != len(data):
        raise RuntimeError("Android Wine ELF relocation changed file/program offsets.")
    return bytes(result), edits


def member_path(name: str) -> str:
    path = PurePosixPath(name)
    if path.is_absolute() or ".." in path.parts or "\\" in name or "\0" in name:
        raise RuntimeError("Pinned Proton archive contains a nonlocal path.")
    normalized = posixpath.normpath(name)
    if normalized.startswith("../"):
        raise RuntimeError("Pinned Proton archive path escapes its root.")
    return normalized.removeprefix("./")


def stage_wine(archive: Path, stage: Path) -> dict:
    """Select native core + complete ARM64 PE/data tree, with no symlink extraction.

    Retaining the complete consistent 64-bit Wine PE tree is intentional. The
    strict recursive owner/worker import proof is not permission to delete Wine
    initialization programs and dynamic service dependencies. 32-bit support and
    optional desktop graphics/audio/Steam Unix drivers are outside this worker.
    """
    import zstandard
    native, payload = stage / "native", stage / "payload/wine"
    native.mkdir(parents=True, exist_ok=True)
    payload.mkdir(parents=True, exist_ok=True)
    records, omitted, seen, selected = [], [], set(), set()
    # Streaming bounds memory independently of archive decompressed size.
    with archive.open("rb") as source, zstandard.ZstdDecompressor().stream_reader(source) as decoded:
        with tarfile.open(fileobj=decoded, mode="r|") as package:
            for item in package:
                name = member_path(item.name)
                if name in seen:
                    raise RuntimeError("Pinned Proton archive contains duplicate logical paths.")
                seen.add(name)
                if not (item.isfile() or item.isdir() or item.issym() or item.islnk()):
                    raise RuntimeError("Pinned Proton archive contains a special filesystem member.")
                if item.issym() or item.islnk():
                    target = item.linkname if item.islnk() else posixpath.join(posixpath.dirname(name), item.linkname)
                    member_path(posixpath.normpath(target))
                    # No installed symlink privilege is required on Windows.
                    continue
                if not item.isfile():
                    continue
                if item.size > 64 * 1024 * 1024:
                    raise RuntimeError("Pinned Proton member exceeds its bounded size.")
                if name == "bin/wineserver":
                    destination = native / "libquest_proton_server.so"
                    data = package.extractfile(item).read()
                    changed, edits = patch_native(data)
                    destination.write_bytes(changed)
                    records.append(dict(path="native/" + destination.name, wineName="wineserver",
                                        sourceMember=name, sourceSha256=sha(data), sha256=sha(changed), neededNameEdits=edits))
                elif name.startswith(UNIX_PREFIX):
                    short = name[len(UNIX_PREFIX):]
                    if short not in NATIVE_MAP:
                        omitted.append(name)
                        continue
                    selected.add(short)
                    data = package.extractfile(item).read()
                    changed, edits = patch_native(data)
                    destination = native / NATIVE_MAP[short]
                    destination.write_bytes(changed)
                    records.append(dict(path="native/" + destination.name, wineName=short,
                                        sourceMember=name, sourceSha256=sha(data), sha256=sha(changed), neededNameEdits=edits))
                elif name.startswith(PE_PREFIX) or name.startswith("share/wine/"):
                    data = package.extractfile(item).read()
                    destination = payload / name
                    destination.parent.mkdir(parents=True, exist_ok=True)
                    destination.write_bytes(data)
    if selected != set(NATIVE_MAP) or not any(item["wineName"] == "wineserver" for item in records):
        raise RuntimeError("Pinned Proton archive does not contain the complete expected native inventory.")
    # Wine init_paths canonicalizes dladdr's filename. This source-owned inert
    # marker supplies only the logical dirname; it is never an executable ELF.
    origin = payload / UNIX_PREFIX / "ntdll.so"
    origin.parent.mkdir(parents=True, exist_ok=True)
    origin.write_text("GHPR logical Wine origin; native code resides in nativeLibraryDir.\n")
    return {"nativeLibraries": records, "optionalUnixExcluded": sorted(
                PurePosixPath(name).stem for name in omitted if name.endswith(".so")),
            "omittedArchiveMembers": omitted,
            "peFiles": sum(1 for path in (payload / PE_PREFIX).rglob("*") if path.is_file())}


def stage_fex(archive: Path, stage: Path, ar_member) -> dict:
    """Extract only the official ARM64EC PE DLL and public notices, never Linux SOs."""
    compressed = ar_member(archive, "data.tar")
    if compressed[:4] == b"\x28\xb5\x2f\xfd":
        import zstandard
        compressed = zstandard.ZstdDecompressor().decompress(compressed, max_output_size=64 * 1024 * 1024)
    selected = "usr/lib/wine/aarch64-windows/libarm64ecfex.dll"
    destination = stage / "payload/wine" / PE_PREFIX / "libarm64ecfex.dll"
    notices = stage / "payload/licenses"
    notices.mkdir(parents=True, exist_ok=True)
    found, seen = False, set()
    with tarfile.open(fileobj=io.BytesIO(compressed)) as package:
        for item in package:
            name = member_path(item.name)
            if name in seen:
                raise RuntimeError("Pinned FEX archive contains duplicate logical paths.")
            seen.add(name)
            if item.issym() or item.islnk():
                target = item.linkname if item.islnk() else posixpath.join(posixpath.dirname(name), item.linkname)
                member_path(posixpath.normpath(target))
            if not item.isfile():
                continue
            if item.size > 32 * 1024 * 1024:
                raise RuntimeError("Pinned FEX member exceeds its bounded size.")
            if name == selected:
                destination.parent.mkdir(parents=True, exist_ok=True)
                destination.write_bytes(package.extractfile(item).read())
                found = True
            elif name in ("usr/share/doc/fex-emu-wine/copyright", "usr/share/doc/fex-emu-wine/changelog.Debian.gz"):
                (notices / ("FEX-" + PurePosixPath(name).name)).write_bytes(package.extractfile(item).read())
    if not found:
        raise RuntimeError("Pinned official FEX package lacks its ARM64EC PE DLL.")
    return dict(path=destination.relative_to(stage).as_posix(), sha256=sha(destination.read_bytes()),
                size=destination.stat().st_size, linuxUnixLibBundled=False)
