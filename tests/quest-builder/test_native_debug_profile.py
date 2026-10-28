"""Keep optimized Android code and source lines with compact native debug data.

The optional pinned NDK check compiles a small original fixture, not game assets.
The large original dispatcher's measured peak belongs in private build evidence.
"""
import importlib.util
import os
from pathlib import Path
import shlex
import struct
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
spec = importlib.util.spec_from_file_location("quest_native_debug_profile", ROOT / "tools/quest-builder/native_admission.py")
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


def elf_sections(path):
    data = path.read_bytes()
    if data[:6] != b"\x7fELF\x02\x01":
        raise AssertionError("Expected a little-endian 64-bit ELF object")
    header = struct.unpack_from("<16sHHIQQQIHHHHHH", data)
    rows = [struct.unpack_from("<IIQQQQIIQQ", data, header[6] + index * header[11])
            for index in range(header[12])]
    names_row = rows[header[13]]
    names = data[names_row[4]:names_row[4] + names_row[5]]
    return {names[row[0]:names.find(b"\0", row[0])].decode():
            (row[2], data[row[4]:row[4] + row[5]]) for row in rows}


class NativeDebugProfileTests(unittest.TestCase):
    def test_player_flags_are_scoped_and_do_not_mutate_the_callers_environment(self):
        original = {"PATH": "controlled host path", "UNITY_IL2CPP_ANDROID_USE_LLD_LINKER": "1",
                    "IL2CPP_ADDITIONAL_ARGS": "--compiler-flags=-g"}
        before = dict(original)
        for target in ("startup", "game"):
            env = native.player_environment(original, target=target)
            self.assertEqual(env["PATH"], before["PATH"])
            self.assertEqual(env["UNITY_IL2CPP_ANDROID_USE_LLD_LINKER"], "1")
            option, = shlex.split(env["IL2CPP_ADDITIONAL_ARGS"])
            self.assertTrue(option.startswith("--compiler-flags="))
            flags = shlex.split(option.split("=", 1)[1])
            self.assertIn("-gline-tables-only", flags)
            self.assertIn("-fbracket-depth=1024", flags)
            self.assertFalse(any(flag in flags for flag in ("-O0", "-Og", "-g", "-g0")))
            self.assertEqual(original, before)
        for target in ("diagnostic", "desktop", ""):
            self.assertEqual(native.player_environment(original, target=target), before)

    @unittest.skipUnless(os.environ.get("GHVR_QUEST_TEST_NDK"), "Set GHVR_QUEST_TEST_NDK for a pinned native compiler check")
    def test_actual_arm64_compiler_retains_code_and_line_table_data(self):
        ndk = Path(os.environ["GHVR_QUEST_TEST_NDK"])
        host = "windows-x86_64" if sys.platform == "win32" else "darwin-x86_64" if sys.platform == "darwin" else "linux-x86_64"
        toolchain = ndk / "toolchains/llvm/prebuilt" / host
        compiler = toolchain / "bin" / ("clang++.exe" if sys.platform == "win32" else "clang++")
        option, = shlex.split(native.player_environment({}, target="game")["IL2CPP_ADDITIONAL_ARGS"])
        flags = shlex.split(option.split("=", 1)[1])
        with tempfile.TemporaryDirectory(prefix="quest native profile & paths ") as folder:
            work = Path(folder)
            source = work / "native fixture.cpp"
            source.write_text('extern "C" __attribute__((noinline)) int quest_reduce(const int *values, int n) {\n'
                              '  int result = 7; for (int i = 0; i < n; ++i) result = (result * 3) ^ values[i];\n'
                              '  return result;\n}\n', encoding="utf-8")
            base = [str(compiler), "--target=aarch64-linux-android29", "--sysroot=" + str(toolchain / "sysroot"),
                    "-Os", "-fPIC", "-std=c++11", "-c", str(source)]
            compact = work / "compact.o"
            plain = work / "plain.o"
            subprocess.run([*base, "-g", *flags, "-o", str(compact)], check=True, capture_output=True)
            subprocess.run([*base, "-g0", "-o", str(plain)], check=True, capture_output=True)
            compact_sections = elf_sections(compact)
            plain_sections = elf_sections(plain)
            code = [name for name, (section_flags, _) in compact_sections.items() if section_flags & 4]
            self.assertTrue(any(compact_sections[name][1] for name in code))
            for name in code:
                self.assertEqual(compact_sections[name][1], plain_sections[name][1], name)
            self.assertGreater(len(compact_sections[".debug_line"][1]), 0)
            self.assertNotIn(".debug_line", plain_sections)


if __name__ == "__main__":
    unittest.main()
