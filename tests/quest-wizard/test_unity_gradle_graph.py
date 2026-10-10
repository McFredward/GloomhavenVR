"""Run the production observer against the pinned Gradle graph, without an APK."""
from pathlib import Path
import importlib.util
import json
import os
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DATA = Path("/home/claw/unity-2021.3.5/Editor/Data/PlaybackEngines/AndroidPlayer")


class ActualGradleGraphTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.java = DATA / "OpenJDK/bin/java"
        cls.jar = DATA / "Tools/gradle/lib/gradle-launcher-6.1.1.jar"
        cls.dotnet = shutil.which("dotnet") or "/home/claw/.dotnet/dotnet"
        if not cls.java.is_file() or not cls.jar.is_file() or not Path(cls.dotnet).is_file():
            raise unittest.SkipTest("Pinned Gradle/OpenJDK and .NET SDK required")
        cls.temporary = tempfile.TemporaryDirectory(prefix="quest-real-gradle-")
        cls.addClassCleanup(cls.temporary.cleanup)
        cls.directory = Path(cls.temporary.name)
        spec = importlib.util.spec_from_file_location("editor_spies", Path(__file__).with_name("test_editor_phase_progress.py"))
        spies = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(spies)
        code = cls.directory / "counter"
        code.mkdir()
        (code / "Counter.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><NoWarn>CS0067;CS0649</NoWarn></PropertyGroup></Project>')
        (code / "Program.cs").write_text(spies.STUBS)
        (code / "QuestWizardProgress.cs").write_bytes((ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestWizardProgress.cs").read_bytes())
        result = subprocess.run([cls.dotnet, "build", str(code / "Counter.csproj"), "--verbosity", "quiet"], text=True, capture_output=True)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)
        cls.counter = code / "bin/Debug/net8.0/Counter.dll"

    def graph(self, name, tasks):
        project = self.directory / name
        project.mkdir()
        sidecar = project / "progress ' live $.jsonl"
        script = project / "observe.gradle"
        result = subprocess.run([self.dotnet, str(self.counter), str(script), str(sidecar)], text=True, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        (project / "settings.gradle").write_text("rootProject.name = 'counter-proof'\n")
        (project / "build.gradle").write_text("apply from: 'observe.gradle'\n" + tasks)
        return project, sidecar

    def run_gradle(self, project, task):
        environment = os.environ.copy()
        environment["JAVA_HOME"] = str(DATA / "OpenJDK")
        return subprocess.run([str(self.java), "-classpath", str(self.jar), "org.gradle.launcher.GradleMain",
            "--offline", "--no-daemon", "--console=plain", "--gradle-user-home", str(self.directory / "gradle-home"), task],
            cwd=project, env=environment, text=True, capture_output=True, timeout=45)

    @staticmethod
    def rows(sidecar):
        return [json.loads(line.removeprefix("GHVRQ_PROGRESS ")) for line in sidecar.read_text().splitlines()]

    def test_actual_graph_total_success_retained_and_skipped_tasks(self):
        project, sidecar = self.graph("success", """
task first { outputs.file('first.txt'); doLast { file('first.txt').text = '1' } }
task second(dependsOn:first) { outputs.file('second.txt'); doLast { file('second.txt').text = '2' } }
task third(dependsOn:second) { onlyIf { false }; doLast { throw new RuntimeException('must be skipped') } }
""")
        for attempt in range(2):
            sidecar.write_text("")
            result = self.run_gradle(project, "third")
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            rows = self.rows(sidecar)
            self.assertEqual((rows[0]["done"], rows[0]["total"]), (0, 3))
            self.assertTrue(all(row["total"] == 3 and row["operation"] == "player" for row in rows))
            self.assertEqual([row["done"] for row in rows], sorted(row["done"] for row in rows))
            self.assertEqual(rows[-1]["done"], 3)
            self.assertEqual(rows[-1]["status"], "progress", "Graph result cannot close the Player owner")
            self.assertIn("retained/skipped", rows[-1]["detail"])
            if attempt:
                self.assertIn("UP-TO-DATE", result.stdout)

    def test_failed_last_task_does_not_complete_graph(self):
        project, sidecar = self.graph("failure", "task first {}\ntask broken(dependsOn:first) { doLast { throw new RuntimeException('intentional graph control') } }\n")
        result = self.run_gradle(project, "broken")
        self.assertNotEqual(result.returncode, 0)
        rows = self.rows(sidecar)
        self.assertEqual(rows[0]["total"], 2)
        self.assertEqual((rows[-1]["done"], rows[-1]["total"], rows[-1]["status"]), (1, 2, "failed"))


if __name__ == "__main__":
    unittest.main()
