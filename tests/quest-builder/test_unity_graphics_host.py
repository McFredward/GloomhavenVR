"""Campaign compiler hosts retain the witnessed compute backend across platforms."""
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import builder
from storage import BuildError


class GraphicsHostTests(unittest.TestCase):
    def test_windows_campaign_uses_witnessed_backend_without_shell_wrapping(self):
        with patch.object(builder.sys, "platform", "win32"):
            self.assertEqual(builder.unity_launcher("C:/Unity Editor/Unity.exe", graphics=True),
                             ["C:/Unity Editor/Unity.exe", "-batchmode", "-force-glcore"])

    def test_linux_requires_real_graphics_host_and_other_targets_remain_headless(self):
        with patch.dict(builder.os.environ, {}, clear=True), patch.object(builder.sys, "platform", "linux"), patch.object(builder.shutil, "which", return_value=None):
            with self.assertRaises(BuildError):
                builder.unity_launcher("/Unity", graphics=True)
            self.assertEqual(builder.unity_launcher("/Unity"), ["/Unity", "-batchmode", "-nographics"])
        with patch.dict(builder.os.environ, {}, clear=True), patch.object(builder.sys, "platform", "linux"), patch.object(builder.shutil, "which", side_effect=lambda name: "/" + name):
            self.assertEqual(builder.unity_launcher("/Unity", graphics=True),
                             ["/xvfb-run", "-a", "/Unity", "-batchmode", "-force-glcore"])


if __name__ == "__main__":
    unittest.main()
