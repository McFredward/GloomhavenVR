"""Preserve Android ABI selection without Windows batch-wrapper path parsing."""
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_native_compiler", ROOT / "scripts/build-quest-native.py")
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)


class CompilerSelection(unittest.TestCase):
    def test_executable_receives_literal_paths_and_original_android_target(self):
        with tempfile.TemporaryDirectory(prefix="quest compiler & 100% ") as folder:
            ndk = Path(folder)
            for platform, host, name in (("win32", "windows-x86_64", "clang++.exe"),
                                         ("linux", "linux-x86_64", "clang++"),
                                         ("darwin", "darwin-x86_64", "clang++")):
                toolchain = ndk / "toolchains/llvm/prebuilt" / host
                (toolchain / "bin").mkdir(parents=True)
                (toolchain / "sysroot").mkdir()
                compiler = toolchain / "bin" / name
                compiler.write_bytes(b"controlled executable selection")
                command = native.compiler_command(ndk, platform)
                self.assertEqual(command, [str(compiler), "--target=aarch64-linux-android29",
                                           "--sysroot=" + str(toolchain / "sysroot")])
                self.assertFalse(any(value.endswith((".cmd", ".bat")) for value in command))

    def test_batch_wrapper_cannot_substitute_for_executable(self):
        with tempfile.TemporaryDirectory() as folder:
            ndk = Path(folder)
            toolchain = ndk / "toolchains/llvm/prebuilt/windows-x86_64"
            (toolchain / "bin").mkdir(parents=True)
            (toolchain / "sysroot").mkdir()
            (toolchain / "bin/aarch64-linux-android29-clang++.cmd").write_text("controlled batch wrapper")
            with self.assertRaisesRegex(RuntimeError, "compiler/sysroot is missing"):
                native.compiler_command(ndk, "win32")

    def test_unknown_platform_rejected_before_command_execution(self):
        with self.assertRaisesRegex(RuntimeError, "Unsupported NDK build host"):
            native.compiler_command(Path("unused"), "unsupported")


if __name__ == "__main__":
    unittest.main()
