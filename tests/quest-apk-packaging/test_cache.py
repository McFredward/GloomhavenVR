"""Exercise the production callback against real isolated filesystem state."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
MONO = Path(os.environ.get("GHVR_QUEST_TEST_MONO", "/home/claw/unity-2021.3.5/Editor/Data/MonoBleedingEdge/bin/mono"))
MCS = Path(os.environ.get("GHVR_QUEST_TEST_MCS", "/home/claw/unity-2021.3.5/Editor/Data/MonoBleedingEdge/lib/mono/4.5/mcs.exe"))


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono compiler is unavailable")
class CacheTests(unittest.TestCase):
    def test_actual_production_filesystem_boundary(self):
        with tempfile.TemporaryDirectory(prefix="quest APK cache & ") as folder:
            exe = Path(folder) / "CacheWitness.exe"
            subprocess.run([str(MONO), str(MCS), "-r:System.Xml", "-out:" + str(exe),
                str(ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestAndroidManifest.cs"),
                str(Path(__file__).with_name("Stubs.cs")),
                str(Path(__file__).with_name("CacheWitness.cs"))], check=True, capture_output=True, text=True)
            result = subprocess.run([str(MONO), str(exe), str(Path(folder) / "state")],
                check=True, capture_output=True, text=True)
            self.assertIn("PASS actual generated-cache boundary", result.stdout)


if __name__ == "__main__":
    unittest.main()
