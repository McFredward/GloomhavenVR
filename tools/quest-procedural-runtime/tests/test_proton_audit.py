"""Independent binary fixtures and actual toolchain controls for Proton staging."""
from __future__ import annotations

import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("quest_proton_audit", ROOT / "proton_audit.py")
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


def pe_bytes(*, machine=0xaa64, hybrid=True, exports=None, imports=None,
             api_sets=None, alternate_exports=None, delayed=None):
    """Construct PE tables independently; these bytes are never executable tests."""
    raw = bytearray(0xb400)
    raw[:2] = b"MZ"
    struct.pack_into("<I", raw, 60, 0x80)
    raw[0x80:0x84] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", raw, 0x84, machine, 2, 0, 0, 0, 240, 0x2022)
    optional = 0x98
    struct.pack_into("<H", raw, optional, 0x20b)
    struct.pack_into("<I", raw, optional + 16, 0x1100)
    struct.pack_into("<Q", raw, optional + 24, 0x180000000)
    struct.pack_into("<II", raw, optional + 56, 0xc000, 0x400)
    struct.pack_into("<I", raw, optional + 108, 16)
    struct.pack_into("<8sIIIIIIHHI", raw, optional + 240, b".text", 0xa000, 0x1000, 0xa000, 0x400, 0, 0, 0, 0, 0x60000020)
    struct.pack_into("<8sIIIIIIHHI", raw, optional + 280, b".apiset" if api_sets else b".data", 0x1000, 0xb000, 0x1000, 0xa400, 0, 0, 0, 0, 0x40000040)
    position = 0x1400

    def allocate(data=b"", amount=None, alignment=4):
        nonlocal position
        position = (position + alignment - 1) // alignment * alignment
        rva = position
        if amount is None:
            amount = len(data)
        position += amount
        if position >= 0xb000:
            raise AssertionError("Fixture table too large")
        offset = rva - 0xc00
        raw[offset:offset + len(data)] = data
        return rva

    def put(fmt, rva, *values):
        struct.pack_into(fmt, raw, rva - 0xc00, *values)

    def text(value):
        return allocate(value.encode("ascii") + b"\0", alignment=1)

    def directory(index, rva, size):
        struct.pack_into("<II", raw, optional + 112 + index * 8, rva, size)

    def export_table(values):
        start = allocate(amount=40)
        names = sorted(values)
        functions = allocate(amount=len(names) * 4)
        pointers = allocate(amount=len(names) * 4)
        ordinals = allocate(amount=len(names) * 2)
        for i, name in enumerate(names):
            target = text(values[name]) if isinstance(values[name], str) else 0x1100 + i * 4
            put("<I", functions + i * 4, target)
            put("<I", pointers + i * 4, text(name))
            put("<H", ordinals + i * 2, i)
        put("<IIHHIIIIIII", start, 0, 0, 0, 0, 0, 1, len(names), len(names), functions, pointers, ordinals)
        return start, position - start

    if exports:
        directory(0, *export_table(exports))
    for index, values, delay in ((1, imports, False), (13, delayed, True)):
        if not values:
            continue
        stride = 32 if delay else 20
        descriptors = allocate(amount=(len(values) + 1) * stride)
        directory(index, descriptors, (len(values) + 1) * stride)
        for n, (name, symbols) in enumerate(values.items()):
            lookup = allocate(amount=(len(symbols) + 1) * 8, alignment=8)
            for i, symbol in enumerate(symbols):
                target = (1 << 63) | symbol if isinstance(symbol, int) else allocate(b"\0\0" + symbol.encode("ascii") + b"\0", alignment=2)
                put("<Q", lookup + i * 8, target)
            name_rva = text(name)
            if delay:
                put("<8I", descriptors + n * stride, 1, name_rva, 0, lookup, lookup, 0, 0, 0)
            else:
                put("<5I", descriptors + n * stride, lookup, 0, 0, name_rva, lookup)
    config = None
    if hybrid:
        config = allocate(amount=320, alignment=8)
        directory(10, config, 320)
        put("<I", config, 320)
        code = allocate(struct.pack("<II", 0x1001, 0x200))
        meta = allocate(struct.pack("<III", 2, code, 1))
        put("<Q", config + 200, 0x180000000 + meta)
    if alternate_exports is not None:
        if config is None:
            raise AssertionError("ARM64X fixture requires CHPE")
        export_rva, size = export_table(alternate_exports)
        # A genuine header-RVA VALUE fixup replaces the export data directory
        # and COFF machine, using one v1 ARM64X dynamic relocation page.
        header_rva = optional + 112
        data = struct.pack("<HII", 0xd000 | header_rva, export_rva, size)
        data += struct.pack("<HH", 0x5000 | 0x84, 0x8664)
        if (8 + len(data)) % 4:
            data += b"\0\0"
        block = struct.pack("<II", 0, 8 + len(data)) + data
        record = struct.pack("<QI", 6, len(block)) + block
        dynamic = allocate(struct.pack("<II", 1, len(record)) + record)
        put("<IH", config + 224, dynamic - 0x1000, 1)
    if api_sets:
        namespace = bytearray(0x1000)
        count = len(api_sets)
        pointer = 28 + count * 24

        def api_allocate(data):
            nonlocal pointer
            offset = pointer
            namespace[offset:offset + len(data)] = data
            pointer += len(data)
            return offset

        for i, (name, provider) in enumerate(api_sets.items()):
            name_raw = name.removesuffix(".dll").encode("utf-16-le")
            host_raw = provider.encode("utf-16-le")
            name_offset = api_allocate(name_raw)
            host_offset = api_allocate(host_raw)
            value_offset = api_allocate(struct.pack("<5I", 0, 0, 0, host_offset, len(host_raw)))
            struct.pack_into("<6I", namespace, 28 + i * 24, 0, name_offset, len(name_raw), len(name_raw), value_offset, 1)
        struct.pack_into("<7I", namespace, 0, 6, pointer, 0, count, 28, 0, 1)
        raw[0xa400:0xb400] = namespace
    return bytes(raw)


def elf_bytes(*, machine=183, needed=(), exports=(), imports=(), weak=(), soname=None,
              glibc=False):
    """Minimal ELF64 dynamic tables independent of the production reader."""
    strings = bytearray(b"\0")

    def string(name):
        position = len(strings)
        strings.extend(name.encode("ascii") + b"\0")
        return position

    symbols = bytearray(24)
    versions = [0]
    for name, defined, is_weak in [(x, True, False) for x in exports] + [(x, False, False) for x in imports] + [(x, False, True) for x in weak]:
        symbols.extend(struct.pack("<IBBHQQ", string(name), (2 if is_weak else 1) << 4 | 2, 0, 1 if defined else 0, 0x1000 if defined else 0, 4))
        versions.append(2 if glibc and not defined else 1)
    dynamic = b"".join(struct.pack("<QQ", 1, string(x)) for x in needed)
    if soname:
        dynamic += struct.pack("<QQ", 14, string(soname))
    dynamic += bytes(16)
    need_version = b""
    if glibc:
        library = string("libc.so")
        name = string("GLIBC_2.2.5")
        need_version = struct.pack("<HHIII", 1, 1, library, 16, 0) + struct.pack("<IHHII", 0, 0, 2, name, 0)
    section_names = b"\0.shstrtab\0.dynstr\0.dynsym\0.dynamic\0.gnu.version\0.gnu.version_r\0"
    contents = [b"", section_names, bytes(strings), bytes(symbols), dynamic,
                struct.pack("<" + "H" * len(versions), *versions), need_version]
    kinds = [0, 3, 3, 11, 6, 0x6fffffff, 0x6ffffffe if glibc else 0]
    names = [0] + [section_names.index(x) for x in (b".shstrtab", b".dynstr", b".dynsym", b".dynamic", b".gnu.version", b".gnu.version_r")]
    links = [0, 0, 0, 2, 2, 3, 2]
    strides = [0, 0, 0, 24, 16, 2, 0]
    raw = bytearray(0x100)
    sections = []
    for index, data in enumerate(contents):
        position = len(raw)
        raw.extend(data)
        while len(raw) % 8:
            raw.append(0)
        sections.append(struct.pack("<IIQQQQIIQQ", names[index], kinds[index], 0, 0,
                                    position, len(data), links[index], 0, 1, strides[index]))
    section_offset = len(raw)
    raw.extend(b"".join(sections))
    raw[:16] = b"\x7fELF\x02\x01\x01" + bytes(9)
    struct.pack_into("<HHIQQQIHHHHHH", raw, 16, 3, machine, 1, 0, 64, section_offset, 0, 64, 56, 1, 64, len(sections), 1)
    struct.pack_into("<IIQQQQQQ", raw, 64, 1, 5, 0, 0, 0, len(raw), len(raw), 0x1000)
    return bytes(raw)


class StageFixture:
    def __init__(self, directory):
        self.root = Path(directory)
        self.native = self.root / "native"
        self.pe = self.root / "payload/wine/lib/wine/aarch64-windows"
        self.system = self.root / "ndk29"
        self.native.mkdir()
        self.pe.mkdir(parents=True)
        self.system.mkdir()
        (self.system / "libc.so").write_bytes(elf_bytes(exports=("memcpy",)))
        self.manifest = dict(schema=1, backend=AUDIT.BACKEND, nativeDirectory="native", payloadDirectory="payload",
                             wineRoot="payload/wine", worker="payload/ApparanceWorker.exe", engine="payload/ApparanceEngine.dll",
                             launcher="native/libquest_proton.so", server="native/libquest_proton_server.so",
                             provenance={k: dict(sha256="a" * 64, sourceCommit="b" * 40, url="https://example.invalid/" + k) for k in ("wine", "fex")},
                             prefix=dict(mode="source-owned-private", zDrive="/", architecture="win64"),
                             choices=dict(noNtsync=True, noFsync=True, noEsync=True, fexUnixCompanion=False,
                                          optionalUnixExcluded=sorted(AUDIT.WINE_UNIX_MEMBERS - {"ntdll"})), nativeLibraries=[])
        for name in ("libquest_proton.so", "libquest_proton_server.so", "libqn.so"):
            data = elf_bytes(needed=("libc.so",), imports=("memcpy",), soname=name)
            (self.native / name).write_bytes(data)
            item = dict(path="native/" + name, sourceSha256=hashlib.sha256(data).hexdigest(), sha256=hashlib.sha256(data).hexdigest(), sourceMember="source/" + name)
            if name == "libqn.so":
                item["wineName"] = "ntdll.so"
            self.manifest["nativeLibraries"].append(item)
        (self.pe / "ntdll.dll").write_bytes(pe_bytes(exports={"NtFixture": None}))
        (self.pe / "kernel32.dll").write_bytes(pe_bytes(exports={"KernelFixture": "ntdll.NtFixture"}, imports={"ntdll.dll": ["NtFixture"]}))
        (self.pe / "libarm64ecfex.dll").write_bytes(pe_bytes(machine=0x8664, exports={k: None for k in AUDIT.FEX_EXPORTS}, imports={"ntdll.dll": ["NtFixture"]}))
        (self.pe / "apisetschema.dll").write_bytes(pe_bytes(hybrid=False, api_sets={"api-ms-win-fixture-l1-1-0.dll": "ntdll.dll"}))
        (self.root / "payload/ApparanceWorker.exe").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"kernel32.dll": ["KernelFixture"]}))
        (self.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"api-ms-win-fixture-l1-1-0.dll": ["NtFixture"]}))
        self.save()

    def save(self):
        (self.root / "runtime.stage.json").write_text(json.dumps(self.manifest), encoding="utf-8")

    def native_replace(self, name, data):
        (self.native / name).write_bytes(data)
        for item in self.manifest["nativeLibraries"]:
            if item["path"] == "native/" + name:
                item["sha256"] = hashlib.sha256(data).hexdigest()
        self.save()

    def audit(self, **kwargs):
        self.save()
        return AUDIT.audit_stage(self.root, system_library_directory=self.system, **kwargs)


class ProtonBinaryAuditTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="quest-proton-binary-audit-")
        self.addCleanup(self.directory.cleanup)
        self.fixture = StageFixture(self.directory.name)

    def reject(self, token, **kwargs):
        report = self.fixture.audit(**kwargs)
        self.assertEqual("failed", report["status"], report)
        self.assertIn(token, "; ".join(report["missing"]))
        with self.assertRaises(AUDIT.AuditError):
            AUDIT.require_passed(report)
        self.assertFalse(report["androidExecutionVerified"])

    def test_actual_binary_closure_positive_and_complete_hashes(self):
        report = self.fixture.audit(expected_provenance=self.fixture.manifest["provenance"])
        AUDIT.require_passed(report)
        self.assertEqual("passed", report["status"])
        self.assertEqual(5, len(report["peClosure"]))
        self.assertEqual(3, report["importedNativeOccurrences"])
        self.assertEqual(4, report["importedPeOccurrences"])
        self.assertEqual(["ntdll"], report["requiredUnixCompanions"])
        self.assertEqual([dict(contract="api-ms-win-fixture-l1-1-0.dll", provider="ntdll.dll")], report["apiSets"])
        self.assertFalse(report["androidExecutionVerified"])
        self.assertTrue(all(len(x["sha256"]) == 64 for x in report["nativeLibraries"] + report["peClosure"]))

    def test_amd64_machine_does_not_qualify_fex_without_chpe(self):
        (self.fixture.pe / "libarm64ecfex.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, exports={k: None for k in AUDIT.FEX_EXPORTS}))
        self.reject("ARM64EC metadata")

    def test_wrong_native_architecture(self):
        self.fixture.native_replace("libqn.so", elf_bytes(machine=62, soname="libqn.so"))
        self.reject("not Android ARM64")

    def test_unresolved_strong_native_import(self):
        self.fixture.native_replace("libqn.so", elf_bytes(needed=("libc.so",), imports=("not_in_bionic",), soname="libqn.so"))
        self.reject("strong import is unresolved")

    def test_unresolved_weak_native_import_allowed(self):
        self.fixture.native_replace("libqn.so", elf_bytes(needed=("libc.so",), weak=("optional_probe",), soname="libqn.so"))
        report = self.fixture.audit()
        AUDIT.require_passed(report)
        self.assertIn("allowed-weak-null", [x["resolution"] for x in report["nativeImports"]])

    def test_glibc_symbol_version_rejected_even_when_name_exists(self):
        self.fixture.native_replace("libqn.so", elf_bytes(needed=("libc.so",), imports=("memcpy",), soname="libqn.so", glibc=True))
        self.reject("glibc ABI")

    def test_linux_dt_needed_rejected(self):
        self.fixture.native_replace("libqn.so", elf_bytes(needed=("libc.so.6",), soname="libqn.so"))
        self.reject("DT_NEEDED")

    def test_proprietary_native_helper_is_never_mandatory(self):
        self.fixture.native_replace("libqn.so", elf_bytes(needed=("libredirect.so",), soname="libqn.so"))
        self.reject("DT_NEEDED")

    def test_soname_must_match_android_filename(self):
        self.fixture.native_replace("libqn.so", elf_bytes(needed=("libc.so",), soname="ntdll.so"))
        self.reject("SONAME")

    def test_stage_digest_mutation_rejected(self):
        with (self.fixture.native / "libqn.so").open("ab") as stream:
            stream.write(b"changed")
        self.reject("digest differs")

    def test_lock_digest_and_source_revision_mismatch_rejected(self):
        self.reject("Pinned provenance differs", expected_provenance={"wine": {"sha256": "c" * 64}})
        self.reject("Pinned provenance differs", expected_provenance={"fex": {"sourceCommit": "c" * 40}})

    def test_missing_source_revision_rejected(self):
        del self.fixture.manifest["provenance"]["fex"]["sourceCommit"]
        self.reject("source revision")

    def test_unlisted_native_file_rejected(self):
        (self.fixture.native / "libunlisted.so").write_bytes(elf_bytes())
        self.reject("Unlisted")

    def test_manifest_escape_rejected(self):
        self.fixture.manifest["nativeLibraries"][0]["path"] = "../outside/libescape.so"
        self.reject("Nonlocal")

    def test_symlink_native_entry_rejected(self):
        path = self.fixture.native / "libqn.so"
        destination = self.fixture.root / "outside.so"
        path.rename(destination)
        try:
            path.symlink_to(destination)
        except OSError:
            self.skipTest("platform has no symlink privilege")
        self.reject("symlink")

    def test_launcher_must_be_actual_native_file(self):
        self.reject("launch endpoint", launcher_path=self.fixture.root / "payload/ApparanceWorker.exe")

    def test_private_prefix_and_absolute_z_contract(self):
        self.fixture.manifest["prefix"]["zDrive"] = "/data/data/app.gamenative/files/imagefs/"
        self.reject("absolute Z:")

    def test_unknown_sync_or_unixlib_choice_rejected(self):
        self.fixture.manifest["choices"]["fexUnixCompanion"] = True
        self.reject("Unix-companion")

    def test_required_pe_export_missing(self):
        (self.fixture.pe / "ntdll.dll").write_bytes(pe_bytes(exports={"Other": None}))
        self.reject("PE export is absent")

    def test_required_pe_import_dll_missing(self):
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"unknown.dll": ["DoWork"]}))
        self.reject("DLL/API-set is absent")

    def test_unknown_api_set_is_not_assumed_ucrtbase(self):
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"api-ms-win-unknown-l1-1-0.dll": ["NtFixture"]}))
        self.reject("DLL/API-set is absent")

    def test_delayed_pe_import_is_required(self):
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, delayed={"missing_delayed.dll": ["Hidden"]}))
        self.reject("DLL/API-set is absent")

    def test_import_ordinal_checked_against_actual_exports(self):
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"ntdll.dll": [999]}))
        self.reject("PE export is absent")
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"ntdll.dll": [1]}))
        AUDIT.require_passed(self.fixture.audit())

    def test_forwarded_exports_retain_actual_provider_contract(self):
        (self.fixture.pe / "kernel32.dll").write_bytes(pe_bytes(exports={"KernelFixture": "missing.Missing"}))
        self.reject("DLL/API-set is absent")

    def test_forwarded_exports_cycle_rejected(self):
        (self.fixture.pe / "kernel32.dll").write_bytes(pe_bytes(exports={"KernelFixture": "kernel32.KernelFixture"}))
        self.reject("forwarder is cyclic")

    def test_required_pe_unix_companion_cannot_be_marked_optional(self):
        (self.fixture.pe / "ws2_32.dll").write_bytes(pe_bytes(exports={"SocketFixture": None}))
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0x8664, hybrid=False, imports={"ws2_32.dll": ["SocketFixture"]}))
        self.reject("Unix companion is absent")

    def test_optional_driver_exclusion_is_explicit(self):
        self.fixture.manifest["choices"]["optionalUnixExcluded"].remove("winex11")
        self.reject("exclusion inventory")

    def test_fex_dynamic_export_abi_required(self):
        values = {k: None for k in AUDIT.FEX_EXPORTS - {"DispatchJump"}}
        (self.fixture.pe / "libarm64ecfex.dll").write_bytes(pe_bytes(machine=0x8664, exports=values))
        self.reject("dynamic entry ABI")

    def test_plain_x64_owner_not_rebuilt_as_arm64(self):
        (self.fixture.root / "payload/ApparanceEngine.dll").write_bytes(pe_bytes(machine=0xaa64))
        self.reject("not plain x64")

    def test_actual_arm64x_view_resolves_only_view_export(self):
        path = self.fixture.pe / "ntdll.dll"
        path.write_bytes(pe_bytes(exports={"NativeOnly": None}, alternate_exports={"NtFixture": None}))
        original = path.read_bytes()
        native = AUDIT.inspect_pe(path, view="native")
        ec = AUDIT.inspect_pe(path)
        self.assertIn("NativeOnly", native["exports"])
        self.assertNotIn("NtFixture", native["exports"])
        self.assertIn("NtFixture", ec["exports"])
        self.assertEqual(2, len(ec["arm64xFixups"]))
        self.assertEqual(0x8664, ec["viewMachine"])
        self.assertEqual(hashlib.sha256(original).hexdigest(), ec["sha256"])
        self.assertEqual(original, path.read_bytes())
        AUDIT.require_passed(self.fixture.audit())

    def test_arm64x_fixup_corruption_rejected(self):
        path = self.fixture.pe / "ntdll.dll"
        raw = bytearray(pe_bytes(exports={"NativeOnly": None}, alternate_exports={"NtFixture": None}))
        optional = 0x98
        config = struct.unpack_from("<I", raw, optional + 112 + 10 * 8)[0] - 0xc00
        dynamic_offset, section = struct.unpack_from("<IH", raw, config + 224)
        self.assertEqual(1, section)
        table = 0x400 + dynamic_offset
        mutations = [(table, "<I", 99), (table + 8, "<Q", 99), (table + 28, "<H", 0xf000)]
        for offset, fmt, value in mutations:
            changed = bytearray(raw)
            struct.pack_into(fmt, changed, offset, value)
            path.write_bytes(changed)
            report = self.fixture.audit()
            self.assertEqual("failed", report["status"], report)
        path.write_bytes(raw[:-1])
        self.reject("section is outside")

    def test_chpe_pointer_version_and_code_extent_controls(self):
        path = self.fixture.pe / "libarm64ecfex.dll"
        raw = bytearray(pe_bytes(machine=0x8664, exports={k: None for k in AUDIT.FEX_EXPORTS}))
        config = struct.unpack_from("<I", raw, 0x98 + 112 + 80)[0] - 0xc00
        pointer = struct.unpack_from("<Q", raw, config + 200)[0]
        metadata = pointer - 0x180000000 - 0xc00
        code = struct.unpack_from("<I", raw, metadata + 4)[0] - 0xc00
        for offset, fmt, value, token in [(config + 200, "<Q", 1, "metadata pointer"),
                                          (metadata, "<I", 99, "code metadata"),
                                          (code + 4, "<I", 0xffffffff, "code range")]:
            changed = bytearray(raw)
            struct.pack_into(fmt, changed, offset, value)
            path.write_bytes(changed)
            self.reject(token)

    def test_actual_pinned_artifacts_when_supplied(self):
        root = os.environ.get("QUEST_PROTON_ACTUAL_ARTIFACTS")
        if not root:
            self.skipTest("set QUEST_PROTON_ACTUAL_ARTIFACTS for pinned public artifact inspection")
        root = Path(root)
        fex_path = root / "libarm64ecfex.dll"
        wine_path = root / "wine/lib/wine/aarch64-windows/ntdll.dll"
        schema_path = root / "wine/lib/wine/aarch64-windows/apisetschema.dll"
        self.assertEqual("89240b8cbc70703c7dc87739910d40fdc5c7ea004b2df5c8c47f607bd87a0c89", hashlib.sha256(fex_path.read_bytes()).hexdigest())
        self.assertEqual("d3430f6cdc186209335258930dd44dc225e1abedccefdbebc2db0254da8fdde9", hashlib.sha256(wine_path.read_bytes()).hexdigest())
        fex = AUDIT.inspect_pe(fex_path)
        native = AUDIT.inspect_pe(wine_path, view="native")
        ec = AUDIT.inspect_pe(wine_path)
        self.assertEqual({"ProcessPendingCrossProcessEmulatorWork", "RtlIsEcCode"}, {s["name"] for e in fex["imports"] for s in e["symbols"]} - set(native["exports"]))
        self.assertEqual(set(), {s["name"] for e in fex["imports"] for s in e["symbols"]} - set(ec["exports"]))
        self.assertEqual(11, len(ec["arm64xFixups"]))
        self.assertEqual(1472, len(ec["exports"]))
        self.assertEqual(672, len(AUDIT.inspect_pe(schema_path)["apiSets"]))
        self.assertTrue(AUDIT.FEX_EXPORTS <= set(fex["exports"]))

    def test_actual_ndk_compilation_and_public_bionic_exports_when_supplied(self):
        root = os.environ.get("QUEST_PROTON_NDK")
        if not root:
            self.skipTest("set QUEST_PROTON_NDK for actual Android helper compilation")
        tool = Path(root) / "toolchains/llvm/prebuilt"
        hosts = [p for p in tool.iterdir() if p.is_dir() and (p / "sysroot/usr/lib/aarch64-linux-android/29/libc.so").is_file()]
        self.assertEqual(1, len(hosts))
        compiler = hosts[0] / "bin/clang"
        if os.name == "nt":
            compiler = compiler.with_suffix(".exe")
        source = self.fixture.root / "helper.c"
        destination = self.fixture.root / "libfixture.so"
        source.write_text('#include <string.h>\n#include <errno.h>\n__attribute__((visibility("default"))) int quest_fixture(void *d,const void *s){memcpy(d,s,4);return errno;}\n', encoding="utf-8")
        subprocess.run([str(compiler), "--target=aarch64-linux-android29", "-shared", "-fPIC", "-fno-builtin", "-Wl,-soname,libfixture.so", "-o", str(destination), str(source)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        value = AUDIT._elf(destination)
        self.assertEqual(183, value["machine"])
        self.assertIn("quest_fixture", {s["name"] for s in value["symbols"] if not s["undefined"]})
        self.assertIn("memcpy", {s["name"] for s in value["symbols"] if s["undefined"]})
        stubs = hosts[0] / "sysroot/usr/lib/aarch64-linux-android/29"
        exports = {s["name"] for name in value["needed"] for s in AUDIT._elf(stubs / name)["symbols"] if not s["undefined"] and s["visible"]}
        self.assertFalse({s["name"] for s in value["symbols"] if s["undefined"] and s["bind"] == "strong"} - exports)


if __name__ == "__main__":
    unittest.main()
