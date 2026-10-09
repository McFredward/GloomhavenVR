"""Exercise executable selection at the actual CI suites' first build boundary."""
import contextlib
import io
import os
from pathlib import Path
import runpy
import sys
import tempfile
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parents[1]
SCRIPTS = ("check-graphics-profiles.py", "check-render-quality-runtime.py")


class FirstBuildReached(Exception):
    """Stop only at the real script's subprocess boundary, before compilation."""


class DotnetResolutionTests(unittest.TestCase):
    def probe(self, script, *, override=None, path_install=False):
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
            command = []

            def capture_first_build(arguments, **unused):
                command.extend(arguments)
                raise FirstBuildReached()

            arguments = [script, "--output-dir", str(root / "evidence")]
            with mock.patch.dict(os.environ, environment, clear=True), \
                    mock.patch.object(sys, "argv", arguments), \
                    mock.patch("subprocess.run", side_effect=capture_first_build), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(FirstBuildReached):
                    runpy.run_path(str(ROOT / "scripts" / script), run_name="__main__")
            self.assertEqual(command[1], "build")
            return command[0], str(path_binary), str(home / ".dotnet/dotnet")

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


if __name__ == "__main__":
    unittest.main()
