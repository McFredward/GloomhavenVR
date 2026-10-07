"""Drive production native recipes without compiling/downloading game payloads."""
import ast
from contextlib import nullcontext
import importlib.util
import io
import json
from pathlib import Path, PureWindowsPath
import tarfile
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]


def load(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module


voice = load("quest_scheduled_voice", "tools/quest-network/native.py")
procedural = load("quest_scheduled_procedural", "tools/quest-procedural-runtime/runtime.py")


def elf():
    result = bytearray(64); result[:6] = b"\x7fELF\x02\x01"; result[18:20] = b"\xb7\x00"
    return bytes(result) + b"/system/bin/linker64"


class NativeSchedules(unittest.TestCase):
    def test_production_opus_recipe_uses_shared_policy_and_keeps_artifact_contract_clean(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); ndk = root / "ndk"; ndk.mkdir(); (ndk / "source.properties").write_bytes(b"NDK r21")
            source = root / "opus"; source.mkdir(); (source / "COPYING").write_bytes(b"codec license")
            calls = []
            def run(arguments, **kwargs):
                calls.append(arguments)
                if "--build" in arguments:
                    build = Path(arguments[arguments.index("--build") + 1]); build.mkdir(); (build / "libopus.a").write_bytes(b"fixture archive")
                elif "-o" in arguments:
                    Path(arguments[-1]).write_bytes(elf())
            scheduler = SimpleNamespace(phase_budget=lambda phase, path: {"jobs": 11}, timed_phase=lambda *a, **k: nullcontext())
            symbols = "\n".join("T " + name for name in voice.CTL_EXPORTS + voice.ORIGINAL_EXPORTS)
            with patch.object(voice, "fetch_source", return_value=source), patch.object(voice, "cmake_command", return_value="cmake"), \
                 patch.object(voice, "android_compiler", return_value=(["clang"], "nm")), patch.object(voice, "_resource_tools", return_value=scheduler), \
                 patch.object(voice.subprocess, "run", side_effect=run), patch.object(voice.subprocess, "check_output", return_value=symbols):
                result = voice.build_network_native(root / "cache", ndk)
            builds = [command for command in calls if "--build" in command]
            self.assertEqual(builds[0][-2:], ["--parallel", "11"])
            contract = json.loads(result.with_name("native-voice.json").read_text())
            self.assertNotIn("jobs", contract)
            self.assertNotIn("host", contract)
            with patch.object(voice, "_resource_tools", side_effect=AssertionError("warm native cache should not rebuild")):
                self.assertEqual(voice.build_network_native(root / "cache", ndk), result)

    def test_production_box64_recipe_uses_shared_policy_and_warm_receipt_without_rebuild(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); ndk = root / "ndk"; ndk.mkdir(); (ndk / "source.properties").write_bytes(b"NDK r21")
            archive = root / "box64.tar.gz"
            with tarfile.open(archive, "w:gz") as package:
                item = tarfile.TarInfo("box64-" + procedural.LOCK["box64"]["revision"] + "/CMakeLists.txt")
                item.size = 1; package.addfile(item, io.BytesIO(b"x"))
            tools = root / "tools"; tools.mkdir()
            for name in ("clang", "ld.lld", "llvm-strip", "llvm-nm"): (tools / name).write_bytes(b"controlled tool")
            calls = []
            def run(arguments, **kwargs):
                calls.append(arguments)
                if "--build" in arguments:
                    build = Path(arguments[arguments.index("--build") + 1]); build.mkdir(); (build / "box64").write_bytes(elf())
                elif "-o" in arguments:
                    Path(arguments[arguments.index("-o") + 1]).write_bytes(elf())
                else:
                    for arg in arguments:
                        if arg.startswith("/out:"): Path(arg[5:]).write_bytes(b"MZ controlled worker")
            scheduler = SimpleNamespace(phase_budget=lambda phase, path: {"jobs": 9}, timed_phase=lambda *a, **k: nullcontext())
            guest = SimpleNamespace(apply=lambda *a: None, require_exports=lambda *a: None, REQUIRED_EXPORTS=("guest_entry",), SOURCE_SHA256="a" * 64)
            symbols = "\n".join("T " + name for name in procedural.EXPORTS + ("quest_apparance_configure", "quest_apparance_last_error", "guest_entry"))
            with patch.object(procedural, "fetch", return_value=archive), patch.object(procedural, "ndk_bin", return_value=tools), \
                 patch.object(procedural, "_guest_patch"), patch.object(procedural, "_host_patch"), \
                 patch.object(procedural, "_glibc_guest_tools", return_value=guest), \
                 patch.object(procedural, "_voice_tools", return_value=SimpleNamespace(cmake_command=lambda: "cmake")), \
                 patch.object(procedural, "_resource_tools", return_value=scheduler), patch.object(procedural.subprocess, "run", side_effect=run), \
                 patch.object(procedural.subprocess, "check_output", return_value=symbols):
                output, contract = procedural.build(root / "cache", ndk)
            builds = [command for command in calls if "--build" in command]
            self.assertEqual(builds[0][-2:], ["--parallel", "9"])
            self.assertNotIn("jobs", contract)
            self.assertNotIn("host", contract)
            with patch.object(procedural, "_resource_tools", side_effect=AssertionError("warm native cache should not rebuild")):
                warm, receipt = procedural.build(root / "cache", ndk)
            self.assertEqual(warm, output); self.assertEqual(receipt, contract)

    def test_actual_guest_inventory_expressions_are_canonical_for_windows_paths(self):
        # Execute the two production metadata expressions with Windows path
        # semantics. str(relative) was the defect; PureWindowsPath lets this
        # exact serialization regression run without a Windows build/download.
        text = (ROOT / "tools/quest-procedural-runtime/runtime.py").read_text()
        expressions = [node.value for node in ast.walk(ast.parse(text)) if isinstance(node, ast.keyword) and node.arg == "path"
                       and "relative_to(payload)" in ast.unparse(node.value)]
        self.assertEqual(len(expressions), 2)
        base = PureWindowsPath("C:/Quest/payload")
        expected = "guest-libraries/libc.so.6"
        path = base / expected
        for expression in expressions:
            result = eval(compile(ast.Expression(expression), "production-manifest-expression", "eval"),
                          {"path": path, "destination": path, "payload": base})
            self.assertEqual(result, expected)

    def test_direct_native_loader_is_independent_of_working_directory_and_sys_path(self):
        self.assertEqual(voice._resource_tools().parse_jobs("7"), 7)
        self.assertEqual(procedural._resource_tools().parse_jobs("8"), 8)


if __name__ == "__main__": unittest.main()
