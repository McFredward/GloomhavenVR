"""Exercise the actual card fixtures up to their first hosted SDK invocation."""
import contextlib
import io
import os
from pathlib import Path
import runpy
import shutil
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
FIXTURES = ("GloomhavenVR.ActiveDismissTests", "GloomhavenVR.SelectionLateJoinTests")
SOURCES = (
    "Cards/CardsGameApi.cs", "Cards/Patches/ActiveDismissConfirmation.cs",
    "Cards/Driver/CardsDriver.6.Flows.cs", "Cards/Driver/CardsDriver.2.Update.cs",
)


class FirstLaunchReached(Exception):
    """No compiler process was started; only executable selection is under test."""


class CardRunnerDotnetTests(unittest.TestCase):
    def probe(self, fixture, *, override=None, installed=False):
        with tempfile.TemporaryDirectory(prefix="ghvr-card-sdk-") as directory:
            root = Path(directory)
            target = root / "tests" / fixture
            shutil.copytree(ROOT / "tests" / fixture, target)
            for relative in SOURCES:
                source = Path("src/GloomhavenVR") / relative
                (root / source).parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / source, root / source)
            (root / "scripts").mkdir()
            shutil.copyfile(ROOT / "scripts/town_run_retention.py", root / "scripts/town_run_retention.py")
            home = root / "empty-home"
            home.mkdir()
            sdk = root / "hosted sdk"
            sdk.mkdir()
            binary = sdk / "dotnet"
            if installed:
                binary.write_text("#!/bin/sh\nexit 73\n")
                binary.chmod(0o755)
            environment = {**os.environ, "HOME": str(home), "PATH": str(sdk),
                           "DOTNET_ROOT": "/preserved/hosted/sdk"}
            environment.pop("DOTNET", None)
            if override is not None:
                environment["DOTNET"] = override
            captured = []

            def first_launch(command, **options):
                captured.append((command, options.get("env", dict(os.environ))))
                self.assertEqual(command[1], "run")
                self.assertTrue(Path(command[command.index("--project") + 1]).is_file())
                raise FirstLaunchReached()

            with mock.patch.dict(os.environ, environment, clear=True), \
                    mock.patch("subprocess.Popen", side_effect=first_launch), \
                    mock.patch("sys.argv", [str(target / "run.py")]), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(FirstLaunchReached):
                    runpy.run_path(str(target / "run.py"), run_name="__main__")
            self.assertEqual(len(captured), 1)
            command, child_environment = captured[0]
            self.assertEqual(child_environment["DOTNET_ROOT"], environment["DOTNET_ROOT"],
                             "Fixture must preserve the SDK root supplied by the host")
            return command[0], str(binary), str(home / ".dotnet/dotnet")

    def test_hosted_path_without_home_sdk(self):
        for fixture in FIXTURES:
            with self.subTest(fixture=fixture):
                selected, binary, _ = self.probe(fixture, installed=True)
                self.assertEqual(selected, binary)

    def test_explicit_sdk_is_preserved_even_when_missing(self):
        for fixture in FIXTURES:
            with self.subTest(fixture=fixture):
                selected, _, _ = self.probe(fixture, installed=True, override="/explicit/sdk/dotnet")
                self.assertEqual(selected, "/explicit/sdk/dotnet")

    def test_empty_override_uses_installed_sdk(self):
        for fixture in FIXTURES:
            with self.subTest(fixture=fixture):
                selected, binary, _ = self.probe(fixture, installed=True, override="")
                self.assertEqual(selected, binary)

    def test_missing_installed_sdk_reaches_the_local_fallback(self):
        for fixture in FIXTURES:
            with self.subTest(fixture=fixture):
                selected, _, fallback = self.probe(fixture)
                self.assertEqual(selected, fallback)


if __name__ == "__main__":
    unittest.main()
