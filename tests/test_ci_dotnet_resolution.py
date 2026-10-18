"""Exercise SDK selection through legitimate evaluation to the first real build."""
import contextlib
import io
import json
import os
from pathlib import Path
import runpy
import sys
import subprocess
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ("check-graphics-profiles.py", "check-render-quality-runtime.py")


class FirstBuildReached(Exception):
    """Stop only at the real script's subprocess boundary, before compilation."""


class DotnetResolutionTests(unittest.TestCase):
    def probe(self, script, *, override=None, path_install=False, missing_sdk=False):
        with tempfile.TemporaryDirectory(prefix="ghvr-ci-dotnet-resolution-") as directory:
            root = Path(directory)
            home = root / "runner-home"
            home.mkdir()
            binary_directory = root / "setup-dotnet"
            binary_directory.mkdir()
            path_binary = binary_directory / "dotnet"
            if path_install:
                path_binary.write_text("#!/bin/sh\nexit 73\n", encoding="utf-8")
                path_binary.chmod(0o755)
            environment = {**os.environ, "HOME": str(home), "PATH": str(binary_directory)}
            environment.pop("DOTNET", None)
            if override is not None:
                environment["DOTNET"] = override
            commands = []

            def capture_until_build(arguments, **unused):
                commands.append(list(arguments))
                if missing_sdk:
                    raise FileNotFoundError(arguments[0])
                if arguments[1] == "msbuild":
                    # The render-quality suite evaluates its cold-reference
                    # policy before compiling. Admit only that read-only query,
                    # not an arbitrary msbuild invocation or a skipped build.
                    self.assertEqual(script, "check-render-quality-runtime.py")
                    self.assertEqual(len(commands), 1)
                    self.assertEqual(Path(arguments[2]), ROOT / "tests" /
                                     "GloomhavenVR.RenderQualityTests" / "Typecheck.csproj")
                    self.assertIn("-getProperty:AutomaticallyUseReferenceAssemblyPackages,FrameworkPathOverride",
                                  arguments)
                    self.assertTrue(any(option.startswith("-p:FrameworkReferenceDir=")
                                        for option in arguments[3:]))
                    self.assertFalse(any(option.lower().startswith((
                        "-t:", "-target:", "/t:", "/target:", "-restore", "/restore",
                        "-gettargetresult:", "/gettargetresult:",
                    )) for option in arguments[3:]))
                    return subprocess.CompletedProcess(arguments, 0, json.dumps({"Properties": {
                        "AutomaticallyUseReferenceAssemblyPackages": "true",
                        "FrameworkPathOverride": "",
                    }}), "")
                self.assertEqual(arguments[1], "build")
                self.assertEqual(Path(arguments[2]).suffix, ".csproj")
                self.assertTrue(Path(arguments[2]).is_file())
                raise FirstBuildReached()

            arguments = [script, "--output-dir", str(root / "evidence")]
            with mock.patch.dict(os.environ, environment, clear=True), \
                    mock.patch.object(sys, "argv", arguments), \
                    mock.patch("subprocess.run", side_effect=capture_until_build), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(FileNotFoundError if missing_sdk else FirstBuildReached):
                    runpy.run_path(str(ROOT / "scripts" / script), run_name="__main__")
            if missing_sdk:
                self.assertEqual(len(commands), 1)
            else:
                expected = ["msbuild", "build"] if script == "check-render-quality-runtime.py" else ["build"]
                self.assertEqual([command[1] for command in commands], expected)
            # A correct preflight executable does not justify a different SDK
            # at the actual compiler boundary, or vice versa.
            for command in commands:
                self.assertEqual(command[0], commands[-1][0])
            return commands[-1][0], str(path_binary), str(home / ".dotnet/dotnet")

    def test_explicit_override_precedes_path(self):
        for script in SCRIPTS:
            with self.subTest(script=script):
                selected, _, _ = self.probe(script, override="/explicit/sdk/dotnet", path_install=True)
                self.assertEqual(selected, "/explicit/sdk/dotnet")

    def test_path_install_works_without_home_sdk(self):
        for script in SCRIPTS:
            with self.subTest(script=script):
                selected, path_binary, _ = self.probe(script, path_install=True)
                self.assertEqual(selected, path_binary)

    def test_empty_override_uses_path(self):
        for script in SCRIPTS:
            with self.subTest(script=script):
                selected, path_binary, _ = self.probe(script, override="", path_install=True)
                self.assertEqual(selected, path_binary)

    def test_missing_path_preserves_local_fallback(self):
        for script in SCRIPTS:
            with self.subTest(script=script):
                selected, _, home_binary = self.probe(script)
                self.assertEqual(selected, home_binary)

    def test_missing_sdk_does_not_silently_skip_validation(self):
        for script in SCRIPTS:
            with self.subTest(script=script):
                selected, _, home_binary = self.probe(script, missing_sdk=True)
                self.assertEqual(selected, home_binary)


if __name__ == "__main__":
    unittest.main()
