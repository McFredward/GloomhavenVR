"""Regression coverage for hosted/local SDK selection in the player-help fixture."""
import importlib.util
import os
from pathlib import Path
import unittest
from unittest import mock


ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "player_settings_help", ROOT / "scripts/check-player-settings-help.py")
help_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(help_check)


class PlayerHelpDotnetTests(unittest.TestCase):
    def test_explicit_host_overrides_path_and_root(self):
        with mock.patch.dict(os.environ, {"DOTNET": "/explicit/dotnet", "DOTNET_ROOT": "/sdk"}, clear=True), \
                mock.patch.object(help_check.shutil, "which", return_value="/installed/dotnet") as lookup:
            self.assertEqual(help_check.resolve_dotnet(), "/explicit/dotnet")
            lookup.assert_not_called()

    def test_bad_explicit_host_is_not_silently_replaced(self):
        with mock.patch.dict(os.environ, {"DOTNET": "/missing/dotnet"}, clear=True), \
                mock.patch.object(help_check.shutil, "which", return_value="/installed/dotnet"):
            self.assertEqual(help_check.resolve_dotnet(), "/missing/dotnet")

    def test_hosted_sdk_wins_over_nonexistent_home_or_root(self):
        with mock.patch.dict(os.environ, {"HOME": "/empty/runner", "DOTNET_ROOT": "/absent/sdk"}, clear=True), \
                mock.patch.object(help_check.shutil, "which", return_value="/installed/dotnet"):
            self.assertEqual(help_check.resolve_dotnet(), "/installed/dotnet")

    def test_empty_override_falls_back_to_installed_sdk(self):
        with mock.patch.dict(os.environ, {"DOTNET": ""}, clear=True), \
                mock.patch.object(help_check.shutil, "which", return_value="/installed/dotnet"):
            self.assertEqual(help_check.resolve_dotnet(), "/installed/dotnet")

    def test_home_sdk_remains_local_fallback(self):
        with mock.patch.dict(os.environ, {}, clear=True), \
                mock.patch.object(help_check.shutil, "which", return_value=None), \
                mock.patch.object(help_check.Path, "home", return_value=Path("/local/user")):
            self.assertEqual(help_check.resolve_dotnet(), "/local/user/.dotnet/dotnet")


if __name__ == "__main__":
    unittest.main()
