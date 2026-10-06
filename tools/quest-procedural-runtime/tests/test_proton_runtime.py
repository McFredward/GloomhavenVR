"""Actual loader/layout boundary witnesses; no Unity or translated guest execution."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import runtime
import proton_layout
import proton_runtime
import proton_audit


class SelectionContracts(unittest.TestCase):
    def test_backend_default_explicit_old_option_and_unknown_rejection(self):
        with patch.dict(os.environ, {}, clear=True):
            self.assertEqual(runtime.backend_choice(), "proton-arm64ec-fex")
            self.assertEqual(runtime.backend_choice("box64-wine9"), "box64-wine9")
            with self.assertRaisesRegex(RuntimeError, "no automatic fallback"):
                runtime.backend_choice("proton-if-it-works")
        with patch.dict(os.environ, {"GHVRQ_PROCEDURAL_BACKEND": "box64-wine9"}):
            self.assertEqual(runtime.backend_choice(), "box64-wine9")
            self.assertEqual(runtime.backend_choice("proton-arm64ec-fex"), "proton-arm64ec-fex")

    def test_proton_failure_never_silently_invokes_old_builder(self):
        fake = type("Builder", (), {"build": lambda *a, **kw: (_ for _ in ()).throw(RuntimeError("loader qualification failed"))})
        with patch.object(runtime, "_abi_tools", return_value=fake), patch.object(runtime, "_build_box64") as old:
            with self.assertRaisesRegex(RuntimeError, "qualification failed"):
                runtime.build(Path("cache"), Path("ndk"), backend="proton-arm64ec-fex")
            old.assert_not_called()

    def test_archive_paths_and_equal_size_native_names(self):
        self.assertEqual(proton_layout.member_path("./lib/wine/aarch64-windows/ntdll.dll"),
                         "lib/wine/aarch64-windows/ntdll.dll")
        for path in ("../outside", "/outside", "lib/../../outside", "lib\\outside", "a\0b"):
            with self.subTest(path=path), self.assertRaisesRegex(RuntimeError, "nonlocal"):
                proton_layout.member_path(path)
        self.assertEqual(len(set(proton_layout.NATIVE_MAP.values())), len(proton_layout.NATIVE_MAP))
        for original, target in proton_layout.NATIVE_MAP.items():
            self.assertLessEqual(len(target), len(original))
            self.assertTrue(target.startswith("lib") and target.endswith(".so"))

    def test_public_pins_keep_actual_artifact_and_source_provenance_separate(self):
        self.assertEqual(proton_runtime.LOCK["fex"]["dllSha256"], "89240b8cbc70703c7dc87739910d40fdc5c7ea004b2df5c8c47f607bd87a0c89")
        self.assertEqual(proton_runtime.LOCK["wine"]["sha256"], "fffa467241bdae3eacd6ceb7e8096bb7793d617ce53a198dae8bc63a3453f595")
        self.assertIn("sourcePackageSha256", proton_runtime.LOCK["fex"])
        self.assertNotIn("binaryGitAttestation", proton_runtime.LOCK["fex"])

    def test_qualified_cache_reuses_only_exact_source_inventory(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            required = {"native/" + name for name in proton_layout.NATIVE_MAP.values()}
            required |= {"native/libQuestApparance.so", "native/libquest_proton.so", "native/libquest_proton_server.so",
                         "payload/ApparanceWorker.exe", "payload/wine/lib/wine/aarch64-windows/ntdll.dll",
                         "payload/wine/lib/wine/aarch64-windows/libarm64ecfex.dll"}
            records = []
            for name in sorted(required):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b"Cache identity fixture only; never compiled, staged or executed.")
                records.append(dict(path=name, sha256=runtime.digest(path), size=path.stat().st_size))
            receipt = dict(inputKey="key", backend=proton_runtime.BACKEND, sources={"source": "sha"}, ndkSha256="ndk",
                           wine=proton_runtime.LOCK["wine"], fex=proton_runtime.LOCK["fex"], artifacts=records)
            def qualify(value=receipt):
                proton_runtime.qualify_cache(root, value, "key", {"source": "sha"}, "ndk", digest=runtime.digest)
            qualify()
            for field in ("inputKey", "sources", "ndkSha256", "wine", "fex", "backend"):
                with self.subTest(field=field), self.assertRaisesRegex(RuntimeError, "source/backend"):
                    qualify(dict(receipt, **{field: "different"}))
            with self.assertRaisesRegex(RuntimeError, "duplicate"):
                qualify(dict(receipt, artifacts=records + records[:1]))
            with self.assertRaisesRegex(RuntimeError, "nonlocal"):
                qualify(dict(receipt, artifacts=[dict(path="../secret", size=1, sha256="x")]))
            altered = root / records[0]["path"]
            original = altered.read_bytes()
            altered.write_bytes(b"tampered")
            with self.assertRaisesRegex(RuntimeError, "source receipt"):
                qualify()
            altered.write_bytes(original)
            extra = root / "native/libunlisted.so"
            extra.write_bytes(b"unlisted")
            with self.assertRaisesRegex(RuntimeError, "unlisted"):
                qualify()
            extra.unlink()
            altered.unlink()
            if os.name != "nt":
                outside = root / "outside"
                outside.write_bytes(original)
                altered.symlink_to(outside)
                with self.assertRaisesRegex(RuntimeError, "symlink"):
                    qualify()


class ActualLoaderBoundary(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler = shutil.which("cc")
        if not compiler or not sys.platform.startswith("linux"):
            raise unittest.SkipTest("Actual ELF process/dynamic-loader witness requires Linux and a C compiler.")
        cls.temporary = tempfile.TemporaryDirectory(prefix="GHPR Proton & Paths ")
        cls.root = Path(cls.temporary.name)
        cls.native = cls.root / "native"
        cls.wine = cls.root / "wine"
        cls.native.mkdir()
        origin = cls.wine / "lib/wine/aarch64-unix/ntdll.so"
        origin.parent.mkdir(parents=True)
        origin.write_text("inert logical origin\n")
        pe = cls.wine / "lib/wine/aarch64-windows"
        pe.mkdir(parents=True)
        (pe / "libarm64ecfex.dll").write_text("Prefix-link fixture only; never executed or delivered.")
        (cls.root / "proton_native_map.h").write_text(proton_runtime._native_header(proton_layout))
        args = [compiler, "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror"]
        subprocess.run(args + ["-fPIE", "-pie", "-fvisibility=hidden", "-Wl,--export-dynamic", "-I", str(cls.root),
                              str(ROOT / "proton_launcher.c"), "-ldl", "-pthread", "-o", str(cls.native / "libquest_proton.so")], check=True)
        server = cls.root / "server.c"
        server.write_text("int main(void) { return 42; }\n")
        subprocess.run(args + [str(server), "-o", str(cls.native / "libquest_proton_server.so")], check=True)
        fixture = cls.root / "native_fixture.c"
        fixture.write_text(r'''
#define _GNU_SOURCE
#include <assert.h>
#include <dlfcn.h>
#include <spawn.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <unistd.h>
extern const void *wine_main_preload_info;
extern char **environ;
static const unsigned char frame[20] = {'G','H','P','R',1,0,1,0,1,0,0,0,0,0,0,0,0,0,0,0};
void __wine_main(int argc, char **argv) {
    assert(argc == 2 && !strcmp(argv[0], "wine"));
    assert(wine_main_preload_info == NULL);
    Dl_info info; char expected[4096];
    snprintf(expected, sizeof expected, "%s/lib/wine/aarch64-unix/ntdll.so", getenv("GHPR_WINE_ROOT"));
    assert(dladdr(__wine_main, &info) && !strcmp(info.dli_fname, expected));
    assert(dlopen(expected, RTLD_NOW));
    assert(dlopen("ntdll.so", RTLD_NOW));
    assert(dlopen("libm.so.6", RTLD_NOW));
    char link[4096];
    snprintf(expected, sizeof expected, "%s/dosdevices/z:", getenv("WINEPREFIX"));
    ssize_t count = readlink(expected, link, sizeof link - 1); assert(count == 1 && link[0] == '/');
    snprintf(expected, sizeof expected, "%s/system.reg", getenv("WINEPREFIX"));
    FILE *registry = fopen(expected, "r"); assert(registry);
    count = (ssize_t)fread(link, 1, sizeof link - 1, registry); link[count] = 0; fclose(registry);
    assert(strstr(link, "[Software\\\\Microsoft\\\\Wow64\\\\amd64]") && strstr(link, "libarm64ecfex.dll"));
    if (strcmp(argv[1], "nested-fixture")) {
        snprintf(expected, sizeof expected, "%s/lib/wine/../../bin/wineserver", getenv("GHPR_WINE_ROOT"));
        char *server[] = {expected, NULL}; pid_t pid = 0; int status;
        assert(!posix_spawn(&pid, expected, NULL, NULL, server, environ));
        assert(waitpid(pid, &status, 0) == pid && WIFEXITED(status) && WEXITSTATUS(status) == 42);
        pid = fork(); assert(pid >= 0);
        if (!pid) {
            snprintf(expected, sizeof expected, "%s/lib/wine/aarch64-unix/wine", getenv("GHPR_WINE_ROOT"));
            char *child[] = {expected, "nested-fixture", NULL};
            execv(expected, child); _exit(123);
        }
        assert(waitpid(pid, &status, 0) == pid && WIFEXITED(status) && !WEXITSTATUS(status));
        char *unchanged[] = {"/bin/true", NULL};
        assert(!posix_spawn(&pid, "/bin/true", NULL, NULL, unchanged, environ));
        assert(waitpid(pid, &status, 0) == pid && WIFEXITED(status) && !WEXITSTATUS(status));
    }
    assert(write(STDOUT_FILENO, frame, sizeof frame) == sizeof frame);
    exit(0);
}
''')
        subprocess.run(args + ["-shared", "-fPIC", str(fixture), "-ldl", "-o", str(cls.native / "libqn.so")], check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def launch(self, state, **changes):
        state.mkdir(exist_ok=True)
        env = dict(os.environ, GHPR_NATIVE_DIR=str(self.native), GHPR_WINE_ROOT=str(self.wine),
                   GHPR_STATE_DIR=str(state), WINEPREFIX=str(state / "proton-prefix"))
        env.update(changes)
        return subprocess.run([str(self.native / "libquest_proton.so"), "contract-fixture"], env=env,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=15)

    def test_actual_producer_loader_nested_exec_and_server_paths(self):
        state = self.root / "state-valid"
        result = self.launch(state)
        self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
        # Stdout remains exclusively framed binary data even during a nested
        # Wine child launch. Launcher/backend diagnostics live only on stderr.
        self.assertEqual(result.stdout, b"GHPR\x01\x00\x01\x00\x01" + b"\0" * 11 +
                                       b"GHPR\x01\x00\x01\x00\x01" + b"\0" * 11)
        self.assertIn(b"backend=proton-arm64ec-fex", result.stderr)
        registry = state / "proton-prefix/system.reg"
        system32 = state / "proton-prefix/drive_c/windows/system32"
        self.assertTrue(system32.is_dir())
        self.assertFalse(system32.is_symlink())
        self.assertTrue((system32 / "libarm64ecfex.dll").is_symlink())
        marker = system32 / "new-private-wine-file"
        marker.write_text("A Wine initialization output must stay private.")
        self.assertFalse((self.wine / "lib/wine/aarch64-windows" / marker.name).exists())
        digest = hashlib.sha256(registry.read_bytes()).hexdigest()
        self.assertEqual(self.launch(state).returncode, 0)
        self.assertEqual(hashlib.sha256(registry.read_bytes()).hexdigest(), digest)

    def test_wrong_prefix_and_regular_drive_are_not_overwritten(self):
        state = self.root / "state-wrong"
        result = self.launch(state, WINEPREFIX=str(self.root / "other-prefix"))
        self.assertEqual(result.returncode, 126)
        self.assertEqual(result.stdout, b"")
        prefix = state / "proton-prefix/dosdevices"
        prefix.mkdir(parents=True)
        target = prefix / "z:"
        target.write_text("retain unrelated regular file")
        result = self.launch(state)
        self.assertEqual(result.returncode, 126)
        self.assertEqual(target.read_text(), "retain unrelated regular file")

    def test_prefix_symlink_is_not_followed(self):
        state = self.root / "state-symlink"
        state.mkdir()
        unrelated = self.root / "unrelated"
        unrelated.mkdir()
        (state / "proton-prefix").symlink_to(unrelated, target_is_directory=True)
        result = self.launch(state)
        self.assertEqual(result.returncode, 126)
        self.assertEqual(list(unrelated.iterdir()), [])


class ActualNativeElfRelocation(unittest.TestCase):
    def test_real_ndk_dynamic_strings_only_and_program_offsets_preserved(self):
        ndk = os.environ.get("QUEST_PROTON_NDK")
        if not ndk:
            self.skipTest("set QUEST_PROTON_NDK for actual Android dynamic string relocation")
        root = runtime.ndk_bin(Path(ndk))
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            source = output / "fixture.c"
            source.write_text("__attribute__((visibility(\"default\"))) int fixture(void){return 42;}\n")
            original_file = output / "ntdll.so"
            subprocess.run([runtime.tool(root, "clang"), "--target=aarch64-linux-android29", "-fPIC", "-shared",
                            "-Wl,-soname,ntdll.so", "-Wl,-rpath,/data/data/com.termux/files/usr/lib", str(source),
                            "-o", str(original_file)], check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            original = original_file.read_bytes()
            changed, edits = proton_layout.patch_native(original)
            final = output / "libqn.so"
            final.write_bytes(changed)
            before, after = proton_audit._elf(original_file), proton_audit._elf(final)
            self.assertEqual(before["soname"], "ntdll.so")
            self.assertEqual(after["soname"], "libqn.so")
            self.assertEqual(after["searchPaths"], [{"kind": "runpath", "value": "$ORIGIN"}])
            self.assertEqual(before["needed"], after["needed"])
            self.assertEqual(len(original), len(changed))
            phoff = struct.unpack_from("<Q", original, 32)[0]
            phsize, phcount = struct.unpack_from("<HH", original, 54)
            self.assertEqual(original[:64], changed[:64])
            self.assertEqual(original[phoff:phoff + phsize * phcount], changed[phoff:phoff + phsize * phcount])
            # NDK21 puts dynamic strings and code in one RX load segment.
            # Independently select executable *sections*, rather than confusing
            # that loader segment's metadata with its actual instructions.
            shoff = struct.unpack_from("<Q", original, 40)[0]
            shsize, shcount = struct.unpack_from("<HH", original, 58)
            self.assertEqual(original[shoff:shoff + shsize * shcount], changed[shoff:shoff + shsize * shcount])
            sections = 0
            for index in range(shcount):
                _name, kind, flags, _address, offset, size, _link, _info, _align, _entries = struct.unpack_from("<IIQQQQIIQQ", original, shoff + index * shsize)
                if kind != 8 and flags & 4:  # SHF_EXECINSTR, never SHT_NOBITS
                    sections += 1
                    self.assertEqual(original[offset:offset + size], changed[offset:offset + size])
            self.assertGreater(sections, 0)
            self.assertEqual({e["replacement"] for e in edits}, {"libqn.so", "$ORIGIN"})
            with self.assertRaisesRegex(RuntimeError, "does not fit"):
                proton_layout.patch_native(original, {"ntdll.so": "lib_unacceptably_long_name.so"})
            with self.assertRaisesRegex(RuntimeError, "ARM64"):
                proton_layout.patch_native(original[:18] + b"\x3e\x00" + original[20:])


if __name__ == "__main__":
    unittest.main()
