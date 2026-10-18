"""Focused ownership and compiler-callback controls, no full shader sweep."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderStrip.cs"


class KeywordStripControls(unittest.TestCase):
    def test_actual_callback_mutation_and_fail_closed_ownership(self):
        compiler, runtime = shutil.which("mcs"), shutil.which("mono")
        if not compiler or not runtime:
            self.skipTest("Mono C# compiler/runtime required for focused callback controls.")
        with tempfile.TemporaryDirectory(prefix="quest-keyword-strip-") as directory:
            output = Path(directory) / "controls.exe"
            subprocess.run([compiler, "-define:UNITY_EDITOR,GHVR_QUEST_GAME", "-out:" + str(output), str(SOURCE),
                            str(Path(__file__).parent / "UnityHost/QuestKeywordStripControls.cs")], check=True, capture_output=True, text=True)
            result = subprocess.run([runtime, str(output), directory], check=True, capture_output=True, text=True)
            self.assertIn("PASS 15 bounded callback", result.stdout)

    def test_desktop_and_startup_have_no_shader_callback(self):
        compiler = shutil.which("mcs")
        if not compiler:
            self.skipTest("Mono C# compiler required.")
        with tempfile.TemporaryDirectory(prefix="quest-keyword-no-game-") as directory:
            for flags in ("UNITY_EDITOR", "GHVR_QUEST_GAME", ""):
                command = [compiler, "-target:library", "-out:" + str(Path(directory) / "inactive.dll"), str(SOURCE)]
                if flags:
                    command.append("-define:" + flags)
                subprocess.run(command, check=True, capture_output=True, text=True)


if __name__ == "__main__":
    unittest.main()
