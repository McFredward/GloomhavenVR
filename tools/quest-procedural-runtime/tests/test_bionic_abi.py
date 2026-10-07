import concurrent.futures
import ctypes
import hashlib
import json
import locale
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import abi_audit
import box64_bionic_abi as adaptation


class RandomData(ctypes.Structure):
    _fields_ = [("fptr", ctypes.POINTER(ctypes.c_int32)), ("rptr", ctypes.POINTER(ctypes.c_int32)),
                ("state", ctypes.POINTER(ctypes.c_int32)), ("rand_type", ctypes.c_int),
                ("rand_deg", ctypes.c_int), ("rand_sep", ctypes.c_int), ("end_ptr", ctypes.POINTER(ctypes.c_int32))]


class BionicAdapters(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.compiler = shutil.which("cc")
        cls.gnu = ctypes.CDLL(None)
        if not cls.compiler or not hasattr(cls.gnu, "initstate_r"):
            raise unittest.SkipTest("Native host GNU oracle/compiler unavailable.")
        cls.temp = tempfile.TemporaryDirectory(prefix="quest-abi-oracle-")
        cls.directory = Path(cls.temp.name)
        fixture = cls.directory / "fixture.c"
        fixture.write_text('#define _GNU_SOURCE\n#include <errno.h>\n#include <time.h>\n#include <stdio.h>\n#include <stdlib.h>\n'
                           'int *__errno(void) { return __errno_location(); }\n'
                           'void __assert2(const char *f,int l,const char *fn,const char *m) { '
                           'fprintf(stderr,"ASSERT[%s][%d][%s][%s]\\n",f,l,fn,m);abort(); }\n'
                           '#include "' + (ROOT / "bionic_abi.c").as_posix() + '"\n'
                           'int test_errno(void) { return errno; }\n'
                           'void test_clear_errno(void) { errno=0; }\n'
                           'int test_thread_errno(int v) { errno=v; struct timespec t={0,20000000}; nanosleep(&t,0);return errno; }\n'
                           'uintptr_t test_ctype_pointer(void) { return (uintptr_t)my___ctype_b_loc(); }\n')
        shared = cls.directory / "fixture.so"
        subprocess.run([cls.compiler, "-std=c11", "-O2", "-shared", "-fPIC", "-pthread", "-Wall", "-Wextra", "-Werror",
                        str(fixture), "-o", str(shared)], check=True)
        cls.adapter = ctypes.CDLL(str(shared))
        for lib, prefix in [(cls.gnu, ""), (cls.adapter, "my_")]:
            getattr(lib, prefix + "initstate_r").argtypes = [ctypes.c_uint32, ctypes.c_void_p, ctypes.c_size_t, ctypes.POINTER(RandomData)]
            getattr(lib, prefix + "random_r").argtypes = [ctypes.POINTER(RandomData), ctypes.POINTER(ctypes.c_int32)]

    @classmethod
    def tearDownClass(cls):
        cls.temp.cleanup()

    def test_all_384_ctype_entries_against_gnu_locale_oracle(self):
        before = locale.setlocale(locale.LC_CTYPE)
        try:
            locale.setlocale(locale.LC_CTYPE, "C.UTF-8")
            for name, kind in [("__ctype_b_loc", ctypes.c_uint16), ("__ctype_tolower_loc", ctypes.c_int32), ("__ctype_toupper_loc", ctypes.c_int32)]:
                native, adapted = getattr(self.gnu, name), getattr(self.adapter, "my_" + name)
                native.restype = adapted.restype = ctypes.POINTER(ctypes.POINTER(kind))
                first, second = native()[0], adapted()[0]
                for value in range(-128, 256):
                    self.assertEqual(first[value], second[value], (name, value))
        finally:
            locale.setlocale(locale.LC_CTYPE, before)

    def test_random_all_state_thresholds_high_seeds_wraps_and_caller_bytes(self):
        self.assertEqual(ctypes.sizeof(RandomData), 48)
        self.assertEqual(RandomData.end_ptr.offset, 40)
        # Every transition, signed seed boundary and many pointer wraps. Original
        # GNU functions are the oracle, not an implementation-mirroring model.
        for size in [8, 9, 31, 32, 33, 63, 64, 65, 127, 128, 129, 255, 256, 257, 512]:
            for seed in [0, 1, 2, 127773, 2147483647, 2147483648, 4294967294, 4294967295]:
                with self.subTest(size=size, seed=seed):
                    first, second = ctypes.create_string_buffer(b"\xa5" * 520), ctypes.create_string_buffer(b"\xa5" * 520)
                    a, b = RandomData(), RandomData()
                    self.assertEqual(self.gnu.initstate_r(seed, first, size, ctypes.byref(a)), 0)
                    self.assertEqual(self.adapter.my_initstate_r(seed, second, size, ctypes.byref(b)), 0)
                    self.assertEqual(first.raw, second.raw)
                    x, y = ctypes.c_int32(), ctypes.c_int32()
                    for _ in range(2048):
                        self.assertEqual(self.gnu.random_r(ctypes.byref(a), ctypes.byref(x)), 0)
                        self.assertEqual(self.adapter.my_random_r(ctypes.byref(b), ctypes.byref(y)), 0)
                        self.assertEqual(x.value, y.value)
                    self.assertEqual(first.raw, second.raw)
                    for pointer in ["fptr", "rptr", "state", "end_ptr"]:
                        pa, pb = getattr(a, pointer), getattr(b, pointer)
                        self.assertEqual((ctypes.cast(pa, ctypes.c_void_p).value or 0) - ctypes.addressof(first) if pa else None,
                                         (ctypes.cast(pb, ctypes.c_void_p).value or 0) - ctypes.addressof(second) if pb else None)

    def test_state_reinitialization_preserves_gnu_old_state_metadata(self):
        a, b = RandomData(), RandomData()
        old_a, old_b = ctypes.create_string_buffer(256), ctypes.create_string_buffer(256)
        new_a, new_b = ctypes.create_string_buffer(64), ctypes.create_string_buffer(64)
        self.gnu.initstate_r(19, old_a, 256, ctypes.byref(a)); self.adapter.my_initstate_r(19, old_b, 256, ctypes.byref(b))
        x, y = ctypes.c_int32(), ctypes.c_int32()
        for _ in range(17):
            self.gnu.random_r(ctypes.byref(a), ctypes.byref(x)); self.adapter.my_random_r(ctypes.byref(b), ctypes.byref(y))
        self.gnu.initstate_r(21, new_a, 64, ctypes.byref(a)); self.adapter.my_initstate_r(21, new_b, 64, ctypes.byref(b))
        self.assertEqual(old_a.raw, old_b.raw); self.assertEqual(new_a.raw, new_b.raw)

    def test_random_invalid_inputs_set_native_thread_errno(self):
        for size in [0, 1, 7]:
            a, b = RandomData(), RandomData(); state = ctypes.create_string_buffer(8)
            self.adapter.test_clear_errno()
            self.assertEqual(self.adapter.my_initstate_r(1, state, size, ctypes.byref(b)), -1)
            actual = self.adapter.test_errno()
            self.adapter.test_clear_errno()
            self.assertEqual(self.gnu.initstate_r(1, state, size, ctypes.byref(a)), -1)
            self.assertEqual(actual, self.adapter.test_errno())
        value = ctypes.c_int32()
        for data, result in [(None, ctypes.byref(value)), (ctypes.byref(RandomData()), None)]:
            self.adapter.test_clear_errno()
            self.assertEqual(self.adapter.my_random_r(data, result), -1)
            self.assertEqual(self.adapter.test_errno(), 22)

    def test_native_errno_and_ctype_pointer_are_thread_local(self):
        self.adapter.test_ctype_pointer.restype = ctypes.c_size_t
        def run(value):
            pointer = self.adapter.test_ctype_pointer()
            return pointer, self.adapter.test_thread_errno(value)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            results = list(pool.map(run, [701, 702, 703, 704]))
        self.assertEqual([value for _, value in results], [701, 702, 703, 704])
        self.assertEqual(len({pointer for pointer, _ in results}), 4)

    def test_catalog_free_gettext_retains_pointer_and_null(self):
        self.adapter.my_dcgettext.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int]
        self.adapter.my_dcgettext.restype = ctypes.c_void_p
        text = ctypes.create_string_buffer(b"Original font diagnostic")
        self.assertEqual(self.adapter.my_dcgettext(None, text, 5), ctypes.addressof(text))
        self.assertIsNone(self.adapter.my_dcgettext(None, None, 5))

    def test_assert_reorders_four_guest_arguments_and_never_returns(self):
        main = self.directory / "assert-main.c"
        main.write_text('#include <stdint.h>\nextern void my___assert_fail(const char*,const char*,uint32_t,const char*);\n'
                        'int main(void) {my___assert_fail("condition", "source.c", 431, "test_fn");return 7;}\n')
        exe = self.directory / "assert-main"
        subprocess.run([self.compiler, str(main), str(self.directory / "fixture.so"), "-o", str(exe)], check=True)
        def no_core():
            import resource
            resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
        run = subprocess.run([str(exe)], capture_output=True, text=True, preexec_fn=no_core)
        self.assertEqual(run.returncode, -6)
        self.assertEqual(run.stderr.strip(), "ASSERT[source.c][431][test_fn][condition]")


class AdapterRecipe(unittest.TestCase):
    def test_pinned_complete_source_and_linux_branch_unchanged(self):
        source = (ROOT / "tests/pinned-wrappedlibc.c.txt").read_bytes()
        header = (ROOT / "tests/pinned-wrappedlibc_private.h.txt").read_bytes()
        changed, mapped = adaptation.adapt(source, header)
        self.assertEqual(changed.replace(b'#ifdef ANDROID\n#include "quest_bionic_abi.c"', b'#ifdef ANDROID'), source)
        cc = shutil.which("cc")
        self.assertTrue(cc)
        macro = '\n'.join('#define ' + kind + '(...) ROW(' + kind + ',__VA_ARGS__)'
                          for kind in ['GO','GOW','GOM','GOWM','GO2','GOW2','DATA','DATAB','DATAM','DATAV']) + '\n'
        def cpp(data, android=False):
            return subprocess.check_output([cc, "-E", "-P", "-x", "c", "-"] + (["-DANDROID"] if android else []),
                                           input=macro+data.decode(), text=True)
        self.assertEqual(cpp(header), cpp(mapped))
        self.assertIn("ROW(GO2,__errno_location, pFv, __errno)", cpp(mapped, True))
        for bad_source, bad_header in [(source+b"\n",header),(source,header+b"\n")]:
            with self.assertRaisesRegex(RuntimeError, "audited source"): adaptation.adapt(bad_source,bad_header)
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary); wrapped = directory / "src/wrapped"; wrapped.mkdir(parents=True)
            (wrapped / "wrappedlibc.c").write_bytes(source); (wrapped / "wrappedlibc_private.h").write_bytes(header)
            adaptation.apply(directory)
            self.assertEqual((wrapped / "quest_bionic_abi.c").read_bytes(), (ROOT / "bionic_abi.c").read_bytes())
            with self.assertRaises(RuntimeError): adaptation.apply(directory)

    def test_all_actual_adapter_exports_are_required(self):
        adaptation.require_exports(set(adaptation.REQUIRED_EXPORTS))
        for symbol in adaptation.REQUIRED_EXPORTS:
            with self.assertRaisesRegex(RuntimeError, symbol): adaptation.require_exports(set(adaptation.REQUIRED_EXPORTS)-{symbol})

    def test_real_ndk_adapter_compile_and_public_export_symbols(self):
        ndk = Path(os.environ.get("GHVRQ_TEST_NDK", "/home/claw/unity-2021.3.5/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"))
        tools = ndk / "toolchains/llvm/prebuilt/linux-x86_64/bin"
        if not (tools / "clang").is_file(): self.skipTest("Actual Android NDK fixture unavailable.")
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary) / "adapter.so"
            subprocess.run([str(tools/"clang"),"--target=aarch64-linux-android29", "--sysroot="+str(tools.parent/"sysroot"),
                            "-std=c11","-O2","-fPIC","-shared","-fvisibility=hidden","-Wall","-Wextra","-Werror",
                            "-Wl,--no-undefined",str(ROOT/"bionic_abi.c"),"-o",str(output)],check=True)
            parsed=abi_audit.elf(output);self.assertEqual(parsed["machine"],183)
            adaptation.require_exports({s["name"] for s in parsed["symbols"] if not s["undefined"] and s["visible"]})


class ImportAndLaunchContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.cc = shutil.which("cc")
        ndk = Path(os.environ.get("GHVRQ_TEST_NDK", "/home/claw/unity-2021.3.5/Editor/Data/PlaybackEngines/AndroidPlayer/NDK"))
        cls.tools = ndk / "toolchains/llvm/prebuilt/linux-x86_64/bin"
        if not cls.cc or not (cls.tools / "clang").is_file():
            raise unittest.SkipTest("Real host/NDK compilers required for independent ELF binding controls.")
        cls.temporary = tempfile.TemporaryDirectory(prefix="quest-abi-elf-controls-")
        cls.directory = Path(cls.temporary.name)
        cls.source = cls.directory / "guest.c"
        # This is a constructed guest, never executed or accepted as delivered
        # Wine. Its real GNU imports and version tables exercise the gate.
        cls.source.write_text('#define _GNU_SOURCE\n#include <stdlib.h>\n#include <stdint.h>\n#include <ctype.h>\n#include <assert.h>\n#include <errno.h>\n#include <libintl.h>\n'
                              'int guest_probe(unsigned seed) {struct random_data d={0};char state[256];int32_t value;'
                              'initstate_r(seed,state,sizeof state,&d);random_r(&d,&value);'
                              'if(seed==123) __assert_fail("c","f",2,"fn");'
                              'errno=value;return (*__ctype_b_loc())[seed&255]+(*__ctype_tolower_loc())[seed&255]'
                              '+(*__ctype_toupper_loc())[seed&255]+*dcgettext("d","m",5);}\n')
        cls.guest = cls.directory / "guest.so"
        subprocess.run([cls.cc, "-O0", "-shared", "-fPIC", "-fno-stack-protector", str(cls.source), "-o", str(cls.guest)], check=True)
        cls.native = cls.directory / "native.so"
        cls.compile_adapter(ROOT / "bionic_abi.c", cls.native)

    @classmethod
    def compile_adapter(cls, source, output):
        subprocess.run([str(cls.tools/"clang"),"--target=aarch64-linux-android29",
                        "--sysroot="+str(cls.tools.parent/"sysroot"),"-std=c11","-O2","-fPIC","-shared",
                        "-fvisibility=hidden","-Wall","-Wextra","-Werror","-Wl,--no-undefined",
                        str(source),"-o",str(output)],check=True)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def scope(self, directory):
        wine = directory / "wine"
        for name in abi_audit.REQUIRED:
            target = wine / name; target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(self.guest, target)
        headers = directory / "headers"; headers.mkdir()
        _, table = adaptation.adapt((ROOT/"tests/pinned-wrappedlibc.c.txt").read_bytes(),
                                    (ROOT/"tests/pinned-wrappedlibc_private.h.txt").read_bytes())
        records = []
        for wrapper in abi_audit.WRAPPERS:
            path = headers / ("wrapped" + wrapper + "_private.h")
            path.write_bytes(table if wrapper == "libc" else b"/* Empty constructed fixture map. */\n")
            records.append(dict(path=path.name,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
        return wine, headers, records

    def inspect(self,wine,headers,records,native=None):
        return abi_audit.inspect(wine,native or self.native,headers,records,str(self.tools/"clang"),
                                 self.tools.parent/"sysroot/usr/lib/aarch64-linux-android/29")

    def test_actual_elf_binding_positive_and_strong_vs_weak(self):
        with tempfile.TemporaryDirectory() as temporary:
            wine,headers,records=self.scope(Path(temporary));report=self.inspect(wine,headers,records)
            abi_audit.require_passed(report)
            names={r["name"] for r in report["imports"] if r["bind"]=="strong"}
            self.assertTrue({"__errno_location","__assert_fail","__ctype_b_loc","__ctype_tolower_loc","initstate_r","random_r","dcgettext"} <= names)
            errno=next(r for r in report["imports"] if r["name"]=="__errno_location")
            self.assertEqual(errno["versionLibrary"],"libc.so.6")
            self.assertEqual(errno["version"],"GLIBC_2.2.5")
            self.assertEqual(errno["wrapperCandidates"][0]["target"],"__errno")
            self.assertTrue(any(r["resolution"]=="allowed-weak-null" for r in report["imports"]))
            self.assertFalse(report["androidExecutionVerified"])

    def test_real_missing_manual_export_and_wrong_go_target_fail_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory=Path(temporary);wine,headers,records=self.scope(directory)
            code=directory/"missing.c";code.write_text('#define my_random_r renamed_random_r\n'+(ROOT/"bionic_abi.c").read_text())
            binary=directory/"missing.so";self.compile_adapter(code,binary)
            failed=self.inspect(wine,headers,records,binary)
            self.assertTrue(any(s.endswith(":random_r") for s in failed["missing"]))
            with self.assertRaisesRegex(RuntimeError,"random_r"):abi_audit.require_passed(failed)
            path=headers/"wrappedlibc_private.h"
            path.write_text(path.read_text().replace("GO2(__errno_location, pFv, __errno)","GO2(__errno_location, pFv, absent_bionic_symbol)"))
            for record in records:
                if record["path"]==path.name:record["sha256"]=hashlib.sha256(path.read_bytes()).hexdigest()
            failed=self.inspect(wine,headers,records)
            self.assertTrue(any(s.endswith(":__errno_location") for s in failed["missing"]))

    def test_frozen_header_tamper_missing_file_architecture_and_native_dependency(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory=Path(temporary);wine,headers,records=self.scope(directory)
            path=headers/"wrappedlibc_private.h";original=path.read_bytes();path.write_bytes(original+b"\n")
            with self.assertRaisesRegex(RuntimeError,"header differs"):self.inspect(wine,headers,records)
            path.write_bytes(original)
            with self.assertRaisesRegex(RuntimeError,"actual Android ARM64"):self.inspect(wine,headers,records,self.guest)
            bad=directory/"unknown-host.so";bad.write_bytes(self.native.read_bytes().replace(b"libc.so\x00",b"xxxx.so\x00"))
            with self.assertRaisesRegex(RuntimeError,"lacks native ABI dependency"):self.inspect(wine,headers,records,bad)
            (wine/abi_audit.REQUIRED[-1]).unlink()
            with self.assertRaises(OSError):self.inspect(wine,headers,records)

    def test_real_elf_base_soname_is_not_a_symbol_version(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory=Path(temporary);source=directory/"version.c";script=directory/"version.map"
            source.write_text('int plain(void){return 1;} int versioned(void){return 2;}\n')
            script.write_text('ACTUAL_ABI_1 {global:versioned;};\n')
            binary=directory/"version.so"
            subprocess.run([self.cc,"-shared","-fPIC",str(source),"-Wl,-soname,version.so",
                            "-Wl,--version-script="+str(script),"-o",str(binary)],check=True)
            parsed={s["name"]:s for s in abi_audit.elf(binary)["symbols"]}
            self.assertIsNone(parsed["plain"]["version"])
            self.assertEqual(parsed["versioned"]["version"],"ACTUAL_ABI_1")
            self.assertTrue(parsed["versioned"]["defaultVersion"])

    def test_actual_bridge_log_restarts_retains_bounded_tail_and_spawn_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory=Path(temporary);source=directory/"log.c"
            source.write_text('#include "'+(ROOT/"bridge.c").as_posix()+'"\n'
                              'int main(int count,char **args) {if(count!=2)return 9;int f=begin_worker_log(args[1]);if(f<0)return 8;'
                              'dprintf(f,"current-only\\n");close(f);return 0;}\n')
            exe=directory/"log";subprocess.run([self.cc,"-O2","-pthread","-Wall","-Wextra","-Werror",str(source),"-o",str(exe)],check=True)
            log=directory/"procedural-worker.log";log.write_bytes(b"old-start-main\n"+b"x"*(300*1024)+b"old-tail\n")
            subprocess.run([str(exe),str(log)],check=True)
            self.assertIn("launch UTC=",log.read_text());self.assertIn("parentPID=",log.read_text())
            self.assertIn("nativeInput=unbound-host-fixture",log.read_text())
            self.assertNotIn("old-start-main",log.read_text());self.assertNotIn("old-tail",log.read_text())
            previous=directory/"procedural-worker.previous.log";self.assertEqual(previous.stat().st_size,256*1024)
            self.assertTrue(previous.read_bytes().endswith(b"old-tail\n"))
            subprocess.run([str(exe),str(log)],check=True)
            self.assertEqual(log.read_text().count("launch UTC="),1)
            self.assertLess(previous.stat().st_size,1024)
            target=directory/"safe.txt";target.write_text("preserved")
            alias=directory/"symlink.log";alias.symlink_to(target)
            self.assertEqual(subprocess.run([str(exe),str(alias)]).returncode,8)
            self.assertEqual(target.read_text(),"preserved")


if __name__ == "__main__":
    unittest.main()
