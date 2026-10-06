"""Static guest-import/native-binding gate for the actual staged Wine worker.

Wrapper names alone are insufficient: GO resolves a native symbol, GOM resolves
an exported my_ adapter, and GO2 resolves its explicit target. Android's libc
wrapper opens the global process and brings pthread/dl/m/bsd wrapper maps into
scope. The pinned ld-linux wrapper handles the bundled libgcc's TLS import.
This verifies availability, never Android execution or implementation semantics.
"""
from __future__ import annotations
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess

WRAPPERS = ("libc", "libpthread", "libdl", "libm", "libbsd", "ldlinux")
DEFINES = ("ANDROID", "ARM64", "DYNAREC")
# The actual original DLL's static PE imports reach USER32/win32u and WS2_32.
# The other eight libraries are explicitly forced to emulation by bridge.c.
REQUIRED = ("bin/wine64", "bin/wineserver", "lib/wine/x86_64-unix/ntdll.so",
            "lib/wine/x86_64-unix/win32u.so", "lib/wine/x86_64-unix/ws2_32.so", "lib/libgcc_s.so.1",
            "lib/libfontconfig.so.1", "lib/libfreetype.so.6", "lib/libexpat.so.1", "lib/libpng16.so.16",
            "lib/libz.so.1", "lib/libbrotlidec.so.1", "lib/libbrotlicommon.so.1", "lib/libbz2.so.1.0")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def elf(path: Path) -> dict:
    """Read bounded ELF64 little-endian dynamic/version tables without host tools."""
    raw = Path(path).read_bytes()
    def unpack(fmt, offset):
        size = struct.calcsize(fmt)
        if offset < 0 or offset + size > len(raw):
            raise RuntimeError("ELF ABI table is outside its file: " + str(path))
        return struct.unpack_from(fmt, raw, offset)
    if len(raw) < 64 or raw[:6] != b"\x7fELF\x02\x01":
        raise RuntimeError("ABI gate requires ELF64 little-endian input: " + str(path))
    section_offset = unpack("<Q", 40)[0]
    stride, count, strings_index = unpack("<HHH", 58)
    if stride != 64 or not 0 < count <= 16384 or strings_index >= count:
        raise RuntimeError("ELF ABI section inventory differs: " + str(path))
    sections = [unpack("<IIQQQQIIQQ", section_offset + i * stride) for i in range(count)]
    def data(s):
        offset, length = s[4:6]
        if offset + length > len(raw):
            raise RuntimeError("ELF ABI section is outside its file: " + str(path))
        return raw[offset:offset + length]
    def string(s, offset):
        block = data(s)
        end = block.find(b"\0", offset)
        if not 0 <= offset <= end:
            raise RuntimeError("ELF ABI string is outside its table: " + str(path))
        return block[offset:end].decode("utf-8", errors="strict")
    by_name = {string(sections[strings_index], s[0]): s for s in sections}
    syms = by_name.get(".dynsym")
    if syms is None or syms[9] != 24 or syms[5] % 24 or syms[6] >= count:
        raise RuntimeError("ELF dynamic ABI symbols are absent or malformed: " + str(path))
    strings = sections[syms[6]]; versions = {}; needed = []
    for s in sections:
        if s[1] == 6:
            for pos in range(s[4], s[4] + s[5], 16):
                tag, value = unpack("<QQ", pos)
                if tag == 1: needed.append(string(sections[s[6]], value))
        elif s[1] in (0x6ffffffe, 0x6ffffffd):
            pos = s[4]; visited = set()
            while True:
                if pos in visited or not s[4] <= pos < s[4] + s[5]:
                    raise RuntimeError("ELF ABI version chain is cyclic or nonlocal: " + str(path))
                visited.add(pos)
                if s[1] == 0x6ffffffe:
                    _, amount, filename, aux, following = unpack("<HHIII", pos)
                    cursor = pos + aux; library = string(sections[s[6]], filename)
                    for _ in range(amount):
                        _, _, index, name, step = unpack("<IHHII", cursor)
                        versions[index & 0x7fff] = (library, string(sections[s[6]], name)); cursor += step
                else:
                    _, _, index, _, _, aux, following = unpack("<HHHHIII", pos)
                    name, _ = unpack("<II", pos + aux)
                    versions[index & 0x7fff] = (None, string(sections[s[6]], name))
                if not following: break
                pos += following
    vtable = by_name.get(".gnu.version"); result = []
    for index in range(syms[5] // 24):
        name, info, visibility, section, _, size = unpack("<IBBHQQ", syms[4] + index * 24)
        if not name or info >> 4 not in (1, 2): continue
        version_bits = unpack("<H", vtable[4] + index * 2)[0] if vtable else 0
        version_index = version_bits & 0x7fff
        # Indices 0/1 mean local/global, even if a version-definition table
        # describes its base SONAME at index 1. That SONAME is not a symbol ABI
        # version (zlib/png keep many ordinary exports at global index 1).
        provider, version = versions.get(version_index, (None, None)) if version_index > 1 else (None, None)
        result.append(dict(name=string(strings, name), bind="weak" if info >> 4 == 2 else "strong",
                           undefined=section == 0, versionLibrary=provider, version=version,
                           defaultVersion=not bool(version_bits & 0x8000), size=size, visible=visibility & 3 in (0, 3)))
    return dict(path=str(path), sha256=hashlib.sha256(raw).hexdigest(), machine=unpack("<H", 18)[0],
                needed=needed, symbols=result)


def freeze_headers(source: Path, destination: Path) -> list[dict]:
    destination.mkdir(parents=True, exist_ok=True)
    records = []
    for lib in WRAPPERS:
        name = "wrapped" + lib + "_private.h"
        path = destination / name
        path.write_bytes((source / "src/wrapped" / name).read_bytes())
        records.append(dict(path=name, sha256=digest(path)))
    return records


def wrapper_maps(directory: Path, records: list[dict], clang: str) -> dict:
    if {r["path"] for r in records} != {"wrapped" + lib + "_private.h" for lib in WRAPPERS} or len(records) != len(WRAPPERS):
        raise RuntimeError("Procedural ABI wrapper inventory differs.")
    macros = "\n".join("#define " + k + "(N,W" + (",O" if k.endswith("2") else "")
                          + ") ROW(" + k + ",#N,#W," + ("#O" if k.endswith("2") else "#N") + ")"
                          for k in ("GO", "GOW", "GOM", "GOWM", "GO2", "GOW2"))
    macros += "\n" + "\n".join("#define " + k + "(N,S) ROW(" + k + ",#N,#S,#N)"
                                for k in ("DATA", "DATAB", "DATAV", "DATAM")) + "\n"
    rows = {}
    for record in records:
        path = directory / record["path"]
        if not path.is_file() or path.is_symlink() or digest(path) != record["sha256"]:
            raise RuntimeError("Procedural ABI compiled wrapper header differs: " + record["path"])
        command = [clang, "-E", "-P", "-x", "c", "-"] + ["-D" + d for d in DEFINES]
        text = subprocess.check_output(command, input=macros + path.read_text(), text=True)
        for kind, name, signature, target in re.findall(r'ROW\((\w+),\s*"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\)', text):
            if kind in ("GOM", "GOWM", "DATAM"): target = "my_" + name
            rows.setdefault(name, []).append(dict(library=path.stem, kind=kind, signature=signature, target=target))
    return rows


def inspect(wine: Path, box: Path, headers: Path, records: list[dict], clang: str, sysroot: Path) -> dict:
    maps = wrapper_maps(headers, records, clang)
    native = elf(box)
    if native["machine"] != 183: raise RuntimeError("ABI gate requires the actual Android ARM64 Box64.")
    box_exports = {s["name"] for s in native["symbols"] if not s["undefined"] and s["visible"]}
    host_exports = {}; host_records = []
    # Resolve the actual Box64 host ELF's transitive Bionic dependencies against
    # the public API29 stubs. An unknown host library is a hard failure.
    queue = list(native["needed"]); visited = set()
    for name in ("libc.so", "libm.so", "libdl.so"):
        if name not in queue: queue.append(name)
    while queue:
        name = queue.pop()
        if name in visited: continue
        if Path(name).name != name: raise RuntimeError("Nonlocal native ABI dependency: " + name)
        visited.add(name); path = sysroot / name
        if not path.is_file(): raise RuntimeError("NDK API29 lacks native ABI dependency: " + name)
        host = elf(path)
        if host["machine"] != 183: raise RuntimeError("ABI host dependency is not ARM64: " + name)
        host_records.append(dict(path=name, sha256=host["sha256"]))
        for s in host["symbols"]:
            if not s["undefined"] and s["visible"]: host_exports.setdefault(s["name"], []).append(name)
        queue.extend(host["needed"])
    for s in native["symbols"]:
        if s["undefined"] and s["bind"] == "strong" and s["name"] not in host_exports:
            raise RuntimeError("Box64 host import absent from API29: " + s["name"])
    guests = {name: elf(wine / name) for name in REQUIRED}
    if any(g["machine"] != 62 for g in guests.values()):
        raise RuntimeError("Required procedural Wine ELF is not x86_64.")
    guest_names = {Path(name).name for name in guests}
    wrapped_sonames = {"libc.so.6", "libpthread.so.0", "libdl.so.2", "libm.so.6", "libbsd.so.0", "ld-linux-x86-64.so.2"}
    for name, guest in guests.items():
        for needed in guest["needed"]:
            if needed not in guest_names and needed not in wrapped_sonames:
                raise RuntimeError("Required Wine dependency is outside the qualified worker scope: " + name + ":" + needed)
    definitions = {}
    for name, guest in guests.items():
        for symbol in guest["symbols"]:
            if not symbol["undefined"] and symbol["visible"]:
                definitions.setdefault((symbol["name"], symbol["version"]), []).append(name)
                if symbol["version"] and symbol["defaultVersion"]:
                    definitions.setdefault((symbol["name"], None), []).append(name)
    imports = []; missing = []; required_exports = set()
    for name, guest in guests.items():
        for symbol in guest["symbols"]:
            if not symbol["undefined"]: continue
            candidates = []
            for mapping in maps.get(symbol["name"], ()):
                target = mapping["target"]
                manual = mapping["kind"] in ("GOM", "GOWM", "DATAM")
                resolution = "box64-export" if target in box_exports else (
                    "ndk-api29-export" if not manual and target in host_exports else "unresolved")
                if resolution == "box64-export": required_exports.add(target)
                candidates.append(dict(**mapping, resolution=resolution, hostLibraries=host_exports.get(target, [])))
            available = any(c["resolution"] != "unresolved" for c in candidates)
            providers = definitions.get((symbol["name"], symbol["version"]), [])
            resolution = "native-wrapper" if available else "guest-export" if providers else (
                "allowed-weak-null" if symbol["bind"] == "weak" else "unresolved-required")
            item = dict(symbol, path=name, wrapperCandidates=candidates, guestProviders=providers, resolution=resolution)
            imports.append(item)
            if resolution == "unresolved-required": missing.append(name + ":" + symbol["name"])
    report = dict(schema=1, status="failed" if missing else "passed", scope="headless-original-worker-and-forced-fontconfig",
                  nativeSha256=native["sha256"], ndkApi=29, hostLibraries=host_records,
                  wrapperDefines=list(DEFINES), wrapperHeaders=records,
                  requiredElves=[dict(path=name, sha256=g["sha256"], needed=g["needed"]) for name, g in guests.items()],
                  importedOccurrences=len(imports), imports=imports, missing=missing,
                  requiredMappedBox64Exports=sorted(required_exports), androidExecutionVerified=False,
                  exclusions=["Other Unix Wine modules including dnsapi, display/audio/USB/smartcard modules are not proved loadable by this gate."],
                  limitations=["Static symbol availability does not prove guest execution, locale behavior outside C.UTF-8, native callbacks or hardware performance."])
    return report


def require_passed(report: dict) -> None:
    if report.get("status") != "passed" or report.get("missing"):
        raise RuntimeError("Procedural guest ABI imports are unresolved: " + ", ".join(report.get("missing", ())))
