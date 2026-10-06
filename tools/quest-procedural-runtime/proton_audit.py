"""Inspect the actual staged Bionic Wine/ARM64EC/FEX files before APK packaging.

This is a static loader/import gate, not proof that Android permits Wine's PE
mapping, FEX JIT, callbacks or synthesis. It reads the binaries themselves and
does not execute archive code, rely on filename architecture labels or substitute
desktop glibc libraries for Android dependencies.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
from pathlib import Path, PurePosixPath
import re
import struct


BACKEND = "proton-arm64ec-fex"
MAX_BINARY = 128 * 1024 * 1024
MAX_ITEMS = 1_048_576
SYSTEM_LIBRARIES = frozenset(("libc.so", "libdl.so", "libm.so", "liblog.so", "libandroid.so"))
PROPRIETARY_HELPERS = ("libredirect", "libsteambootstrap", "libkgslshim", "libevshim", "libPackageHelpers")
FEX_EXPORTS = frozenset(("ProcessInit", "ProcessTerm", "ThreadInit", "ThreadTerm",
                         "BeginSimulation", "DispatchJump", "ExitToX64", "ResetToConsistentState"))
# A PE import can load its Unix companion with __wine_unix_call even though
# that ELF is not visible in DT_NEEDED. The pinned archive inventory closes this
# second graph. Drivers outside the worker graph must be explicitly excluded.
WINE_UNIX_MEMBERS = frozenset((
    "crypt32", "opencl", "dwrite", "wineps", "winebth", "winspool", "odbc32",
    "ws2_32", "win32u", "windows.media.speech", "winebus", "msv1_0", "winex11",
    "nsiproxy", "dnsapi", "bcrypt", "opengl32", "localspl", "winegstreamer",
    "netapi32", "kerberos", "qcap", "winealsa", "winepulse", "winedmo",
    "winevulkan", "ctapi32", "ntdll", "lsteamclient", "avicap32", "mountmgr", "secur32"))


class AuditError(RuntimeError):
    """A staged binary or its required dependency is not qualified."""


def _sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _raw(path: Path) -> bytes:
    if path.is_symlink() or not path.is_file() or not 0 < path.stat().st_size <= MAX_BINARY:
        raise AuditError("Audit requires a bounded regular file: " + str(path))
    return path.read_bytes()


def _relative(root: Path, name: str) -> Path:
    # The manifest is shared across Windows builders and Android. Require one
    # portable representation; never follow a staged escape or symlink.
    if not isinstance(name, str) or "\\" in name or "\0" in name:
        raise AuditError("Invalid staged path.")
    parts = PurePosixPath(name).parts
    if not parts or PurePosixPath(name).is_absolute() or any(p in (".", "..") for p in parts):
        raise AuditError("Nonlocal staged path: " + name)
    path = root.joinpath(*parts)
    current = root
    for part in parts:
        current /= part
        if current.is_symlink():
            raise AuditError("Staged audit path is a symlink: " + name)
    return path


def inspect_pe(path: Path, *, view: str = "arm64ec") -> dict:
    """Read PE32+ imports, exports, API-set v6 and genuine CHPE code metadata.

    ARM64EC PE files may report AMD64 or ARM64 in the COFF machine field. The
    load-config CHPE pointer and bounded code map distinguish them from the
    original x64 owner/worker. A nonzero but invalid pointer is rejected.
    """
    raw = bytearray(_raw(Path(path)))
    original_digest = hashlib.sha256(raw).hexdigest()
    if view not in ("arm64ec", "native"):
        raise AuditError("Unknown PE import view: " + view)

    def unpack(fmt: str, offset: int):
        amount = struct.calcsize(fmt)
        if offset < 0 or offset + amount > len(raw):
            raise AuditError("PE table is outside its file: " + str(path))
        return struct.unpack_from(fmt, raw, offset)

    if len(raw) < 64 or raw[:2] != b"MZ":
        raise AuditError("PE signature is absent: " + str(path))
    pe_offset = unpack("<I", 60)[0]
    if raw[pe_offset:pe_offset + 4] != b"PE\0\0":
        raise AuditError("PE COFF signature is absent: " + str(path))
    machine, section_count, _, _, _, optional_size, _ = unpack("<HHIIIHH", pe_offset + 4)
    optional = pe_offset + 24
    if optional_size < 112 or unpack("<H", optional)[0] != 0x20b:
        raise AuditError("The x64/ARM64EC gate requires PE32+: " + str(path))
    if not 0 < section_count <= 4096 or optional + optional_size > len(raw):
        raise AuditError("Invalid PE section inventory: " + str(path))
    image_base = unpack("<Q", optional + 24)[0]
    image_size, header_size = unpack("<II", optional + 56)
    entrypoint = unpack("<I", optional + 16)[0]
    directory_count = unpack("<I", optional + 108)[0]
    if directory_count > (optional_size - 112) // 8:
        raise AuditError("PE directory inventory exceeds its optional header: " + str(path))
    sections = []
    for i in range(section_count):
        pos = optional + optional_size + i * 40
        name, virtual_size, address, size, offset, _, _, _, _, flags = unpack("<8sIIIIIIHHI", pos)
        if offset + size > len(raw) or address + max(size, virtual_size) > image_size:
            raise AuditError("PE section is outside its image: " + str(path))
        sections.append(dict(name=name.rstrip(b"\0").decode("ascii", errors="strict"),
                             address=address, virtualSize=virtual_size, size=size, offset=offset, flags=flags))

    def file_offset(rva: int, amount: int = 1) -> int:
        if amount < 0 or rva < 0 or rva + amount > image_size:
            raise AuditError("PE RVA is outside its image: " + str(path))
        if rva < header_size and rva + amount <= min(header_size, len(raw)):
            return rva
        matches = [s for s in sections if s["address"] <= rva and rva + amount <= s["address"] + s["size"]]
        if len(matches) != 1:
            raise AuditError("PE RVA has no unique file-backed section: " + str(path))
        return matches[0]["offset"] + rva - matches[0]["address"]

    def rva_unpack(fmt: str, rva: int):
        return unpack(fmt, file_offset(rva, struct.calcsize(fmt)))

    def string(rva: int) -> str:
        pos = file_offset(rva)
        end = raw.find(b"\0", pos, min(len(raw), pos + 4096))
        if end < 0:
            raise AuditError("PE string is unterminated: " + str(path))
        file_offset(rva, end - pos + 1)
        return raw[pos:end].decode("ascii", errors="strict")

    def directory(index: int) -> tuple[int, int]:
        if index >= directory_count:
            return 0, 0
        rva, size = unpack("<II", optional + 112 + index * 8)
        if bool(rva) != bool(size):
            raise AuditError("Incomplete PE directory: " + str(path))
        if rva:
            file_offset(rva, size)
        return rva, size

    # ARM64X changes the PE header/export table for an EC consumer. Inspecting
    # only the normal ARM64 table rejects valid FEX imports. Materialize the
    # actual bounded DynamicValue fixups in memory, never in the staged file.
    # Wire layout follows IMAGE_DYNAMIC_RELOCATION{64,TABLE} and ARM64X fixup
    # definitions (LLVM COFF.h / COFFObjectFile.cpp, llvmorg-20.1.0).
    arm64x_fixups = []
    fixup_extents = []
    config_rva, config_size = directory(10)
    if config_rva and config_size >= 232:
        config_declared = rva_unpack("<I", config_rva)[0]
        if config_declared > config_size:
            raise AuditError("PE load-config size exceeds its directory: " + str(path))
        if config_declared >= 232:
            dynamic_va = rva_unpack("<Q", config_rva + 192)[0]
            dynamic_offset, dynamic_section = rva_unpack("<IH", config_rva + 224)
            if dynamic_va and dynamic_section:
                raise AuditError("Ambiguous PE dynamic relocation location: " + str(path))
            if dynamic_va:
                if dynamic_va < image_base:
                    raise AuditError("Invalid dynamic relocation pointer: " + str(path))
                table = dynamic_va - image_base
            elif dynamic_section:
                if not 1 <= dynamic_section <= len(sections):
                    raise AuditError("Invalid dynamic relocation section: " + str(path))
                section = sections[dynamic_section - 1]
                if dynamic_offset + 8 > section["size"]:
                    raise AuditError("Dynamic relocation table is outside its section: " + str(path))
                table = section["address"] + dynamic_offset
            else:
                table = 0
            if table:
                version, length = rva_unpack("<II", table)
                if version != 1:
                    raise AuditError("Unsupported dynamic relocation table version: " + str(path))
                file_offset(table, 8 + length)
                cursor, end = table + 8, table + 8 + length
                while cursor < end:
                    if cursor + 12 > end:
                        raise AuditError("Truncated dynamic relocation record: " + str(path))
                    symbol, amount = rva_unpack("<QI", cursor)
                    cursor += 12
                    record_end = cursor + amount
                    if record_end > end:
                        raise AuditError("Dynamic relocation record exceeds its table: " + str(path))
                    if symbol != 6:
                        cursor = record_end
                        continue
                    while cursor < record_end:
                        if cursor + 8 > record_end:
                            raise AuditError("Truncated ARM64X page header: " + str(path))
                        page, block_size = rva_unpack("<II", cursor)
                        if page & 0xfff or block_size <= 8 or block_size % 4 or cursor + block_size > record_end:
                            raise AuditError("Invalid ARM64X page block: " + str(path))
                        block_end = cursor + block_size
                        cursor += 8
                        while cursor < block_end:
                            word = rva_unpack("<H", cursor)[0]
                            cursor += 2
                            if not word:
                                if cursor != block_end:
                                    raise AuditError("Unexpected ARM64X padding: " + str(path))
                                break
                            kind, argument = (word >> 12) & 3, word >> 14
                            target = page + (word & 0xfff)
                            size = 4 if kind == 2 else 1 << argument
                            if kind > 2 or (kind == 1 and not argument) or target % size:
                                raise AuditError("Invalid ARM64X fixup: " + str(path))
                            target_offset = file_offset(target, size)
                            if any(target_offset < end and start < target_offset + size for start, end in fixup_extents):
                                raise AuditError("Overlapping ARM64X fixup extents: " + str(path))
                            fixup_extents.append((target_offset, target_offset + size))
                            if kind == 0:
                                replacement = bytes(size)
                            elif kind == 1:
                                if cursor + size > block_end:
                                    raise AuditError("Truncated ARM64X value: " + str(path))
                                pos = file_offset(cursor, size)
                                replacement = bytes(raw[pos:pos + size])
                                cursor += size
                            else:
                                if cursor + 2 > block_end:
                                    raise AuditError("Truncated ARM64X delta: " + str(path))
                                delta = rva_unpack("<H", cursor)[0] * (8 if argument & 2 else 4)
                                if argument & 1:
                                    delta = -delta
                                cursor += 2
                                replacement = struct.pack("<I", (unpack("<I", target_offset)[0] + delta) & 0xffffffff)
                            arm64x_fixups.append(dict(rva=target, kind=kind, size=size, value=replacement.hex()))
                            if len(arm64x_fixups) > MAX_ITEMS:
                                raise AuditError("Unbounded ARM64X fixup inventory: " + str(path))
                            if view == "arm64ec":
                                raw[target_offset:target_offset + size] = replacement
    view_machine = unpack("<H", pe_offset + 4)[0]
    imports = []
    for index, stride, delayed in ((1, 20, False), (13, 32, True)):
        address, length = directory(index)
        if not address:
            continue
        terminated = False
        for pos in range(address, address + length - stride + 1, stride):
            values = rva_unpack("<" + "I" * (stride // 4), pos)
            if not any(values):
                terminated = True
                break
            if delayed:
                attributes, name, _, iat, lookup, _, _, _ = values
                if attributes != 1:
                    raise AuditError("Unsupported non-RVA PE delay imports: " + str(path))
            else:
                lookup, _, _, name, iat = values
            dll = string(name).lower()
            if Path(dll).name != dll or "/" in dll or "\\" in dll or not dll.endswith((".dll", ".drv", ".exe", ".sys")):
                raise AuditError("Nonlocal imported PE DLL: " + dll)
            lookup = lookup or iat
            functions = []
            for n in range(MAX_ITEMS):
                value = rva_unpack("<Q", lookup + n * 8)[0]
                if not value:
                    break
                if value & (1 << 63):
                    if value & ~((1 << 63) | 0xffff):
                        raise AuditError("Invalid PE import ordinal: " + str(path))
                    functions.append(dict(ordinal=value & 0xffff))
                else:
                    functions.append(dict(name=string(value + 2)))
            else:
                raise AuditError("Unbounded PE import thunk table: " + str(path))
            imports.append(dict(dll=dll, delayed=delayed, symbols=functions))
        if not terminated:
            raise AuditError("PE import descriptor table is unterminated: " + str(path))

    exports = {}
    ordinal_exports = {}
    export_rva, export_size = directory(0)
    if export_rva:
        (_, _, _, _, _, ordinal_base, function_count, name_count,
         functions, names, ordinals) = rva_unpack("<IIHHIIIIIII", export_rva)
        if function_count > MAX_ITEMS or name_count > MAX_ITEMS:
            raise AuditError("Unbounded PE export inventory: " + str(path))
        file_offset(functions, function_count * 4)
        file_offset(names, name_count * 4)
        file_offset(ordinals, name_count * 2)
        for i in range(function_count):
            target = rva_unpack("<I", functions + i * 4)[0]
            if not target:
                continue
            if export_rva <= target < export_rva + export_size:
                value = dict(forwarder=string(target))
            else:
                file_offset(target)
                value = dict(rva=target)
            ordinal_exports[ordinal_base + i] = value
        for i in range(name_count):
            name = string(rva_unpack("<I", names + i * 4)[0])
            ordinal_index = rva_unpack("<H", ordinals + i * 2)[0]
            if ordinal_index >= function_count or name in exports:
                raise AuditError("Invalid PE named export inventory: " + str(path))
            value = ordinal_exports.get(ordinal_base + ordinal_index)
            if value is None:
                raise AuditError("Named PE export has no target: " + str(path))
            exports[name] = value

    chpe = None
    config_rva, config_size = directory(10)
    if config_rva:
        declared_size = rva_unpack("<I", config_rva)[0]
        if declared_size > config_size or declared_size < 4:
            raise AuditError("PE load-config size exceeds its directory: " + str(path))
        if declared_size >= 208:
            pointer = rva_unpack("<Q", config_rva + 200)[0]
            if pointer:
                if pointer < image_base:
                    raise AuditError("Invalid CHPE metadata pointer: " + str(path))
                rva = pointer - image_base
                version, code_map, amount = rva_unpack("<III", rva)
                if version not in (1, 2) or not 0 < amount <= MAX_ITEMS:
                    raise AuditError("Unsupported or empty CHPE code metadata: " + str(path))
                file_offset(code_map, amount * 8)
                kinds = set()
                for i in range(amount):
                    start, length = rva_unpack("<II", code_map + i * 8)
                    kind = start & 3
                    start &= ~3
                    if kind > 2 or not length or start + length > image_size:
                        raise AuditError("Invalid CHPE code range: " + str(path))
                    # One CodeMap range may span adjoining .text/.hexpthk
                    # sections. Verify their file-backed executable union.
                    cursor = start
                    while cursor < start + length:
                        matches = [s for s in sections if s["address"] <= cursor < s["address"] + s["size"] and s["flags"] & 0x20000000]
                        if len(matches) != 1:
                            raise AuditError("CHPE code range is outside executable sections: " + str(path))
                        cursor = min(start + length, matches[0]["address"] + matches[0]["size"])
                    kinds.add(kind)
                chpe = dict(pointer=pointer, rva=rva, version=version,
                            codeMapRva=code_map, codeRangeCount=amount, codeKinds=sorted(kinds))

    api_sets = {}
    for section in sections:
        if section["name"] != ".apiset":
            continue
        base = section["address"]
        version, size, _, count, entries, _, _ = rva_unpack("<7I", base)
        if version != 6 or not 28 <= size <= section["size"] or count > MAX_ITEMS:
            raise AuditError("Unsupported API-set schema (requires actual v6): " + str(path))

        def api_bytes(offset: int, length: int) -> bytes:
            if offset < 0 or length < 0 or offset + length > size:
                raise AuditError("API-set value is outside its namespace: " + str(path))
            if not length:
                return b""
            pos = file_offset(base + offset, length)
            return raw[pos:pos + length]

        for i in range(count):
            values = struct.unpack("<6I", api_bytes(entries + i * 24, 24))
            _, name_pos, name_length, _, value_pos, value_count = values
            if value_count > MAX_ITEMS:
                raise AuditError("Unbounded API-set provider inventory: " + str(path))
            name = api_bytes(name_pos, name_length).decode("utf-16-le").lower() + ".dll"
            providers = []
            for j in range(value_count):
                _, alias_pos, alias_length, host_pos, host_length = struct.unpack("<5I", api_bytes(value_pos + j * 20, 20))
                alias = api_bytes(alias_pos, alias_length).decode("utf-16-le").lower()
                host = api_bytes(host_pos, host_length).decode("utf-16-le").lower()
                if host and (Path(host).name != host or not host.endswith((".dll", ".drv")) or "/" in host or "\\" in host):
                    raise AuditError("Invalid API-set provider name: " + str(path))
                providers.append(dict(alias=alias, host=host))
            if name in api_sets:
                raise AuditError("Duplicate API-set contract: " + name)
            api_sets[name] = providers

    return dict(path=str(path), sha256=original_digest, machine=machine, viewMachine=view_machine,
                view=view, arm64xFixups=arm64x_fixups,
                entrypoint=entrypoint, imageBase=image_base, imageSize=image_size, chpe=chpe,
                imports=imports, exports=exports, ordinalExports=ordinal_exports, apiSets=api_sets)


def _elf(path: Path) -> dict:
    # Reuse the independently tested ELF version/symbol reader; no readelf,
    # subprocess architecture labels or target executable are involved.
    module_path = Path(__file__).with_name("abi_audit.py")
    spec = importlib.util.spec_from_file_location("quest_proton_elf_reader", module_path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    raw = _raw(path)
    try:
        result = module.elf(path)
    except (RuntimeError, UnicodeError, struct.error, IndexError) as exc:
        raise AuditError("Malformed native ELF: " + str(path) + ": " + str(exc)) from exc
    header_offset = struct.unpack_from("<Q", raw, 32)[0]
    stride, count = struct.unpack_from("<HH", raw, 54)
    if stride != 56 or not 0 < count <= 16384 or header_offset + count * stride > len(raw):
        raise AuditError("ELF program inventory is invalid: " + str(path))
    interpreter = None
    for i in range(count):
        kind, _, offset, _, _, size, _, _ = struct.unpack_from("<IIQQQQQQ", raw, header_offset + i * stride)
        if offset + size > len(raw):
            raise AuditError("ELF program segment is outside its file: " + str(path))
        if kind == 3:
            if interpreter is not None or not 0 < size <= 4096 or raw[offset + size - 1] != 0:
                raise AuditError("ELF interpreter inventory is invalid: " + str(path))
            interpreter = raw[offset:offset + size - 1].decode("ascii", errors="strict")
    section_offset = struct.unpack_from("<Q", raw, 40)[0]
    section_stride, section_count, _ = struct.unpack_from("<HHH", raw, 58)
    soname = None
    paths = []
    for i in range(section_count):
        section = struct.unpack_from("<IIQQQQIIQQ", raw, section_offset + i * section_stride)
        if section[1] != 6:
            continue
        linked = struct.unpack_from("<IIQQQQIIQQ", raw, section_offset + section[6] * section_stride)
        strings = raw[linked[4]:linked[4] + linked[5]]
        for pos in range(section[4], section[4] + section[5], 16):
            tag, value = struct.unpack_from("<QQ", raw, pos)
            if tag not in (14, 15, 29):
                continue
            end = strings.find(b"\0", value)
            if not 0 <= value <= end:
                raise AuditError("ELF dynamic string is outside its table: " + str(path))
            text = strings[value:end].decode("ascii", errors="strict")
            if tag == 14:
                if soname is not None:
                    raise AuditError("Duplicate ELF SONAME: " + str(path))
                soname = text
            else:
                paths.append(dict(kind="rpath" if tag == 15 else "runpath", value=text))
    result.update(interpreter=interpreter, soname=soname, searchPaths=paths)
    return result


def _match(expected, actual, label: str) -> None:
    """Compare a supplied lock subset without trusting a manifest status flag."""
    if isinstance(expected, dict):
        if not isinstance(actual, dict):
            raise AuditError("Provenance shape differs: " + label)
        for key, value in expected.items():
            if key not in actual:
                raise AuditError("Provenance field is absent: " + label + "." + key)
            _match(value, actual[key], label + "." + key)
    elif expected != actual:
        raise AuditError("Pinned provenance differs: " + label)


def audit_stage(stage_root: Path, launcher_path: Path | None = None, *,
                expected_provenance: dict | None = None,
                system_library_directory: Path) -> dict:
    """Return a hash-bound static report for runtime.stage.json and actual files.

    system_library_directory is the selected NDK's public API29 AArch64 stub
    directory. A failure is returned as status=failed with bounded context; use
    require_passed before exposing a stage as reusable or copying it into Unity.
    """
    root = Path(stage_root).resolve()
    report = dict(schema=1, backend=BACKEND, status="failed", androidExecutionVerified=False,
                  missing=[], nativeLibraries=[], systemLibraries=[], peClosure=[], apiSets=[],
                  optionalUnixExcluded=[], importedNativeOccurrences=0, importedPeOccurrences=0,
                  limitations=["Static binary/import availability does not establish Android namespace loading, anonymous executable mappings, FEX JIT, callback ABI, original synthesis output, latency or headset performance.",
                               "Dynamic DLL loads outside the documented bootstrap/engine/FEX closure need additional runtime evidence."])
    try:
        _audit(root, launcher_path, expected_provenance, Path(system_library_directory), report)
        report["status"] = "passed"
    except (AuditError, OSError, ValueError, KeyError, TypeError, struct.error, UnicodeError) as exc:
        report["missing"].append(str(exc)[:2048])
    return report


def _audit(root: Path, launcher_path, expected_provenance, system_dir: Path, report: dict) -> None:
    manifest_path = _relative(root, "runtime.stage.json")
    if manifest_path.stat().st_size > 1024 * 1024:
        raise AuditError("Proton stage manifest is unbounded.")
    manifest = json.loads(_raw(manifest_path))
    report["manifestSha256"] = _sha(manifest_path)
    if manifest.get("schema") != 1 or manifest.get("backend") != BACKEND:
        raise AuditError("Proton stage manifest schema/backend differs.")
    provenance = manifest["provenance"]
    if expected_provenance is not None:
        _match(expected_provenance, provenance, "provenance")
    for component in ("wine", "fex"):
        record = provenance[component]
        if not isinstance(record, dict) or not any(re.fullmatch(r"[0-9a-f]{64}", str(v)) for k, v in record.items() if k.lower().endswith("sha256")):
            raise AuditError("Component provenance lacks a pinned artifact digest: " + component)
        if not any(re.fullmatch(r"[0-9a-f]{40}", str(v)) for k, v in record.items() if k.lower() in ("sourcecommit", "commit", "sourcerevision", "revision")):
            raise AuditError("Component provenance lacks a source revision: " + component)
    report["provenance"] = provenance
    prefix = manifest["prefix"]
    if prefix.get("mode") != "source-owned-private" or prefix.get("zDrive") != "/" or prefix.get("architecture") != "win64":
        raise AuditError("Proton prefix does not retain the private absolute Z: contract.")
    choices = manifest["choices"]
    if any(choices.get(k) is not True for k in ("noNtsync", "noFsync", "noEsync")) or choices.get("fexUnixCompanion") is not False:
        raise AuditError("Unqualified synchronization/FEX Unix-companion choice.")
    report["choices"] = choices
    excluded = choices.get("optionalUnixExcluded", [])
    if not isinstance(excluded, list) or len(excluded) != len(set(excluded)):
        raise AuditError("Optional Unix exclusion inventory differs.")
    normalized_excluded = set()
    for name in excluded:
        if not isinstance(name, str):
            raise AuditError("Invalid optional Unix exclusion path.")
        if "/" in name:
            if not name.startswith("lib/wine/aarch64-unix/") or PurePosixPath(name).parts[-1] in (".", ".."):
                raise AuditError("Unknown optional Unix exclusion path: " + name)
            short = PurePosixPath(name).name
        else:
            short = name
        if short in ("wine", "wine-preloader"):
            # The APK has its own fixed-path native launch shim. These original
            # ELF endpoints are explicitly replaced, not needed shared modules.
            continue
        normalized_excluded.add(Path(short).stem if short.endswith(".so") else short)
    if not normalized_excluded <= WINE_UNIX_MEMBERS:
        raise AuditError("Unknown optional Unix module exclusion.")
    report["optionalUnixExcluded"] = sorted(excluded)
    native_dir = _relative(root, manifest["nativeDirectory"])
    payload_dir = _relative(root, manifest["payloadDirectory"])
    wine_root = _relative(root, manifest["wineRoot"])
    if not native_dir.is_dir() or not payload_dir.is_dir() or not wine_root.is_dir():
        raise AuditError("Proton staged directory is absent.")
    native = {}
    wine_native = {}
    records = manifest["nativeLibraries"]
    if not isinstance(records, list) or not records or len(records) > 128:
        raise AuditError("Native package inventory is absent or unbounded.")
    for item in records:
        path = _relative(root, item["path"])
        if path.parent != native_dir or not re.fullmatch(r"lib[a-zA-Z0-9_.-]+\.so", path.name):
            raise AuditError("Native ELF is outside APK lib*.so placement: " + item["path"])
        if any(token.lower() in path.name.lower() for token in PROPRIETARY_HELPERS):
            raise AuditError("Proprietary launcher helper is not part of this backend: " + path.name)
        value = _elf(path)
        if value["machine"] != 183 or value["interpreter"] not in (None, "/system/bin/linker64"):
            raise AuditError("Native Wine component is not Android ARM64: " + path.name)
        if item.get("sha256") != value["sha256"] or not re.fullmatch(r"[0-9a-f]{64}", item.get("sourceSha256", "")):
            raise AuditError("Native staged/source digest differs: " + path.name)
        if path.name in native:
            raise AuditError("Duplicate native staged filename: " + path.name)
        if value["soname"] not in (None, path.name):
            raise AuditError("Native SONAME does not match APK filename: " + path.name)
        native[path.name] = value
        if item.get("wineName"):
            name = item["wineName"]
            if Path(name).name != name or name in wine_native:
                raise AuditError("Invalid native Wine logical mapping: " + str(name))
            wine_native[name] = path.name
        report["nativeLibraries"].append(dict(path=item["path"], sha256=value["sha256"], sourceSha256=item["sourceSha256"],
                                              sourceMember=item.get("sourceMember"), wineName=item.get("wineName"),
                                              needed=value["needed"], soname=value["soname"], interpreter=value["interpreter"],
                                              searchPaths=value["searchPaths"], neededNameEdits=item.get("neededNameEdits", [])))
    actual_native = {p.name for p in native_dir.iterdir() if p.is_file()}
    if actual_native != set(native) or any(p.is_symlink() or p.is_dir() for p in native_dir.iterdir()):
        raise AuditError("Unlisted or nonregular native package file.")
    for role in ("launcher", "server"):
        path = _relative(root, manifest[role])
        if path.parent != native_dir or path.name not in native:
            raise AuditError("Proton launch endpoint is outside nativeLibraryDir: " + role)
    if launcher_path is not None and Path(launcher_path).resolve() != _relative(root, manifest["launcher"]).resolve():
        raise AuditError("Audited launcher differs from runtime launch endpoint.")
    if "ntdll.so" not in wine_native:
        raise AuditError("Native Wine ntdll mapping is absent.")

    systems = {}
    queue = {name for value in native.values() for name in value["needed"]}
    while queue:
        name = queue.pop()
        if name in native or name in systems:
            continue
        if name not in SYSTEM_LIBRARIES or Path(name).name != name:
            raise AuditError("Native DT_NEEDED is outside the Android worker closure: " + name)
        value = _elf(system_dir / name)
        if value["machine"] != 183:
            raise AuditError("NDK system provider is not ARM64: " + name)
        systems[name] = value
        queue.update(value["needed"])
        report["systemLibraries"].append(dict(path=name, sha256=value["sha256"], needed=value["needed"]))
    definitions = {}
    for name, value in {**native, **systems}.items():
        for symbol in value["symbols"]:
            if symbol["undefined"] or not symbol["visible"]:
                continue
            definitions.setdefault((symbol["name"], symbol["version"]), set()).add(name)
            if symbol["version"] is None or symbol["defaultVersion"]:
                definitions.setdefault((symbol["name"], None), set()).add(name)
    def dependency_scope(name: str) -> set[str]:
        scope = {name, _relative(root, manifest["launcher"]).name}
        queue = [name]
        while queue:
            current = queue.pop()
            for needed in {**native, **systems}[current]["needed"]:
                if needed not in scope:
                    scope.add(needed)
                    queue.append(needed)
        return scope

    native_imports = []
    for name, value in native.items():
        scope = dependency_scope(name)
        for symbol in value["symbols"]:
            if symbol["version"] and symbol["version"].startswith("GLIBC"):
                raise AuditError("Android native binary imports/exports glibc ABI: " + name + ":" + symbol["name"])
            if not symbol["undefined"]:
                continue
            providers = definitions.get((symbol["name"], symbol["version"]), set()) & scope
            if symbol["versionLibrary"]:
                providers = providers & {symbol["versionLibrary"]}
            if not providers and symbol["bind"] == "strong":
                raise AuditError("Native strong import is unresolved: " + name + ":" + symbol["name"])
            native_imports.append(dict(path=name, name=symbol["name"], version=symbol["version"],
                                       providers=sorted(providers), resolution="native-export" if providers else "allowed-weak-null"))
    report["nativeImports"] = native_imports
    report["importedNativeOccurrences"] = len(native_imports)

    pe_dir = wine_root / "lib/wine/aarch64-windows"
    if not pe_dir.is_dir() or pe_dir.is_symlink():
        raise AuditError("Wine ARM64EC PE directory is absent.")
    available = {}
    for path in pe_dir.iterdir():
        if path.suffix.lower() not in (".dll", ".exe", ".drv", ".sys"):
            continue
        if path.name.lower() in available or path.is_symlink():
            raise AuditError("Ambiguous or symlink Wine PE member: " + path.name)
        available[path.name.lower()] = path
    for role in ("worker", "engine"):
        path = _relative(root, manifest[role])
        value = inspect_pe(path)
        if value["machine"] != 0x8664 or value["chpe"] is not None:
            raise AuditError("Original owner/worker is not plain x64 PE: " + role)
        name = path.name.lower()
        if name in available:
            raise AuditError("Owner/worker shadows a Wine PE member: " + name)
        available[name] = path
    schemas = inspect_pe(pe_dir / "apisetschema.dll")
    api_sets = schemas["apiSets"]
    if not api_sets:
        raise AuditError("Actual Wine API-set namespace is absent.")
    report["apiSetSchema"] = dict(sha256=schemas["sha256"], contracts=len(api_sets))
    parsed = {}
    used_api_sets = {}

    def resolve_dll(name: str, importer: str) -> str:
        name = name.lower()
        if name in available:
            return name
        if name not in api_sets:
            raise AuditError("Required PE DLL/API-set is absent: " + importer + ":" + name)
        values = api_sets[name]
        specific = [p["host"] for p in values if p["alias"] == importer]
        defaults = [p["host"] for p in values if not p["alias"]]
        providers = specific or defaults
        if len(set(providers)) != 1 or providers[0] not in available:
            raise AuditError("API-set provider is absent or ambiguous: " + name)
        used_api_sets[name] = providers[0]
        return providers[0]

    def load(name: str, view: str) -> dict:
        key = (name, view)
        if key not in parsed:
            value = inspect_pe(available[name], view=view)
            owner = name in {_relative(root, manifest[k]).name.lower() for k in ("worker", "engine")}
            if not owner:
                if view == "native":
                    if value["machine"] not in (0xaa64, 0xa64e):
                        raise AuditError("Native bootstrap dependency is not ARM64 PE: " + name)
                else:
                    # Wine's no-entrypoint data/forwarder DLLs contain no code
                    # requiring an EC thunk. Native ARM64 executables are audited
                    # separately and must never be silently accepted here.
                    forwarders_only = value["entrypoint"] == 0 and all("forwarder" in e for e in value["ordinalExports"].values())
                    qualified = value["chpe"] is not None and 1 in value["chpe"]["codeKinds"]
                    if value["machine"] not in (0x8664, 0xaa64, 0xa641, 0xa64e) or not (qualified or forwarders_only):
                        raise AuditError("Required Wine/FEX code DLL has no qualified ARM64EC metadata: " + name)
            parsed[key] = value
        return parsed[key]

    def resolve_symbol(dll: str, symbol: dict, importer: str, view: str, stack=()) -> dict:
        target = resolve_dll(dll, importer)
        key = (target, symbol.get("name", symbol.get("ordinal")))
        if key in stack or len(stack) >= 64:
            raise AuditError("PE export forwarder is cyclic or unbounded: " + target)
        value = load(target, view)
        exports = value["exports"] if "name" in symbol else value["ordinalExports"]
        entry = exports.get(key[1])
        if entry is None:
            raise AuditError("Required PE export is absent: " + importer + ":" + target + ":" + str(key[1]))
        if "forwarder" in entry:
            forwarder = entry["forwarder"]
            if "." not in forwarder:
                raise AuditError("Malformed PE export forwarder: " + forwarder)
            library, function = forwarder.rsplit(".", 1)
            if not library.lower().endswith(".dll"):
                library += ".dll"
            forwarded = dict(ordinal=int(function[1:])) if function.startswith("#") else dict(name=function)
            return resolve_symbol(library, forwarded, target, view, (*stack, key))
        return dict(dll=target, symbol=key[1], rva=entry["rva"], view=view)

    roots = [_relative(root, manifest[k]).name.lower() for k in ("worker", "engine")]
    roots += ["ntdll.dll", "kernel32.dll", "libarm64ecfex.dll"]
    bootstraps = choices.get("bootstrapPeRoots", [])
    if not isinstance(bootstraps, list) or len(bootstraps) != len(set(bootstraps)):
        raise AuditError("Native PE bootstrap roots are invalid.")
    for name in bootstraps:
        if name not in available or name not in ("wineboot.exe", "services.exe", "winedevice.exe"):
            raise AuditError("Unknown native ARM64 PE bootstrap root: " + str(name))
        value = inspect_pe(available[name], view="native")
        if value["machine"] != 0xaa64 or value["chpe"] is not None:
            raise AuditError("Declared Wine bootstrap is not plain native ARM64: " + name)
    queue = [(name, "arm64ec") for name in roots] + [(name, "native") for name in bootstraps]
    visited = set()
    pe_imports = []
    while queue or set(parsed) - visited:
        if not queue:
            queue.extend(set(parsed) - visited)
        name, view = queue.pop()
        key = (name, view)
        if key in visited:
            continue
        if name not in available:
            raise AuditError("Required PE root is absent: " + name)
        visited.add(key)
        value = load(name, view)
        for entry in value["imports"]:
            provider = resolve_dll(entry["dll"], name)
            queue.append((provider, view))
            for symbol in entry["symbols"]:
                result = resolve_symbol(entry["dll"], symbol, name, view)
                pe_imports.append(dict(path=name, view=view, requestedDll=entry["dll"], delayed=entry["delayed"], requested=symbol, resolved=result))
    fex = parsed[("libarm64ecfex.dll", "arm64ec")]
    if provenance["fex"].get("dllSha256", fex["sha256"]) != fex["sha256"]:
        raise AuditError("Pinned FEX ARM64EC DLL digest differs.")
    if not FEX_EXPORTS <= set(fex["exports"]):
        raise AuditError("FEX ARM64EC dynamic entry ABI is incomplete: " + ",".join(sorted(FEX_EXPORTS - set(fex["exports"]))))
    # Include PE modules reached through forwarders in the Unix-companion graph.
    reached_unix = {Path(name).stem for name, _ in visited if Path(name).stem in WINE_UNIX_MEMBERS}
    mapped_unix = {Path(name).stem for name in wine_native if name.endswith(".so")}
    if reached_unix - mapped_unix:
        raise AuditError("Reached PE Unix companion is absent: " + ",".join(sorted(reached_unix - mapped_unix)))
    if normalized_excluded & reached_unix or (normalized_excluded | mapped_unix) != WINE_UNIX_MEMBERS:
        raise AuditError("Optional Unix module closure/exclusion inventory differs.")
    report["peClosure"] = [dict(path=str(available[name].relative_to(root)), view=view, sha256=parsed[(name, view)]["sha256"],
                                     machine=parsed[(name, view)]["machine"], viewMachine=parsed[(name, view)]["viewMachine"],
                                     chpe=parsed[(name, view)]["chpe"], arm64xFixupCount=len(parsed[(name, view)]["arm64xFixups"]),
                                     imports=[e["dll"] for e in parsed[(name, view)]["imports"]], exports=len(parsed[(name, view)]["exports"])) for name, view in sorted(visited)]
    report["peImports"] = pe_imports
    report["importedPeOccurrences"] = len(pe_imports)
    report["apiSets"] = [dict(contract=k, provider=v) for k, v in sorted(used_api_sets.items())]
    report["requiredUnixCompanions"] = sorted(reached_unix)
    report["unreachedPeMembers"] = sorted(set(available) - {name for name, _ in visited})


def require_passed(report: dict) -> None:
    if report.get("status") != "passed" or report.get("missing"):
        raise AuditError("Proton staged binary audit failed: " + "; ".join(report.get("missing", ())))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stage-root", type=Path, required=True)
    parser.add_argument("--system-library-directory", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args()
    report = audit_stage(args.stage_root, system_library_directory=args.system_library_directory)
    args.report.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(report["status"] + ": " + ("; ".join(report["missing"]) if report["missing"] else "static ARM64 Bionic + ARM64EC import closure; Android execution unverified"))
    return 0 if report["status"] == "passed" else 1


if __name__ == "__main__":
    raise SystemExit(main())
