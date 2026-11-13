"""Independent JobQueue/import budgets and the exact 2021.3 native CLI contract.

The opt-in real Editor fixture checks sequential/parallel texture output parity.
It is a small import proof, not a Windows whole-game timing or peak-RAM claim.
"""
import hashlib
import importlib.util
import io
import json
import os
import re
from pathlib import Path
import signal
import shutil
import struct
import subprocess
import tempfile
import time
import unittest
from unittest.mock import patch
import zlib

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_unity_throughput_resources", ROOT / "tools/quest-builder/host_resources.py")
resources = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resources)
GIB = resources.GIB
EDITOR = Path(os.environ.get("GHVR_QUEST_TEST_UNITY", "/home/claw/unity-2021.3.5/Editor/Unity"))


def host(total=64, available=36, commit=28, cores=16, platform="win32"):
    return {"schema": 1, "platform": platform, "logicalCpus": cores, "effectiveCpus": cores,
            "totalMemoryBytes": total * GIB, "availableMemoryBytes": None if available is None else available * GIB,
            "commitHeadroomBytes": None if commit is None else commit * GIB,
            "diskFreeBytes": 200 * GIB, "warnings": []}


def option(arguments, name):
    return arguments[arguments.index(name) + 1]


class UnityThroughput(unittest.TestCase):
    def setUp(self):
        environment = patch.dict(os.environ, {}, clear=True)
        environment.start()
        self.addCleanup(environment.stop)

    def launch(self, snapshot, extra=()):
        with patch.object(resources, "detect_host", return_value=snapshot), patch("sys.stdout", new_callable=io.StringIO):
            return resources.unity_launch_arguments(["Unity.exe", "-batchmode", *extra], Path.cwd())

    def test_actual_captured_available_capacity_no_longer_caps_lightweight_threads_as_editor_children(self):
        snapshot = host()
        snapshot.update(totalMemoryBytes=68438433792, availableMemoryBytes=38927679488,
                        commitHeadroomBytes=28578942976)
        arguments = self.launch(snapshot)
        self.assertEqual(option(arguments, "-job-worker-count"), "15")
        self.assertEqual(option(arguments, "-desiredWorkerCount"), "9")
        self.assertEqual(option(arguments, "-refreshImportMode"), "OutOfProcessPerQueue")
        self.assertEqual(option(arguments, "-standbyWorkerCount"), "0")
        self.assertEqual(resources.PROFILES["unity-import"][:2], (2 * GIB, 2 * GIB))
        self.assertEqual(resources.PROFILES["il2cpp"][:2], (2 * GIB, 2 * GIB))

    def test_low_and_unknown_memory_keeps_existing_in_process_importer(self):
        for snapshot in (host(8, 5, 5), host(64, None, None), host(64, 56, None)):
            arguments = self.launch(snapshot)
            self.assertEqual(option(arguments, "-refreshImportMode"), "InProcess")
            self.assertEqual(option(arguments, "-desiredWorkerCount"), "1")
        self.assertEqual(option(self.launch(host(8, 5, 5)), "-job-worker-count"), "1")

    def test_import_workers_are_bounded_by_commit_physical_ram_affinity_and_explicit_jobs(self):
        enough = resources.choose_jobs(host(128, 112, 112, 64), "unity-import", 64)
        self.assertEqual(enough["jobs"], 16)
        pressure = resources.choose_jobs(host(128, 112, 11, 64), "unity-import", 64)
        self.assertEqual(pressure["jobs"], 1)
        physical = resources.choose_jobs(host(128, 11, 112, 64), "unity-import", 64)
        self.assertEqual(physical["jobs"], 1)
        self.assertEqual(resources.choose_jobs(host(128, 112, 112, 2), "unity-import", 64)["jobs"], 2)
        self.assertEqual(resources.choose_jobs(host(128, 112, 112, 64), "unity-import", 3)["jobs"], 3)
        os.environ[resources.JOBS_ENV] = "2"
        arguments = self.launch(host(128, 112, 112, 64))
        self.assertEqual(option(arguments, "-job-worker-count"), "2")
        self.assertEqual(option(arguments, "-desiredWorkerCount"), "2")

    def test_existing_native_single_worker_retry_cannot_reenable_parallel_imports(self):
        arguments = self.launch(host(128, 112, 112), ("-job-worker-count", "1"))
        self.assertEqual(option(arguments, "-job-worker-count"), "1")
        self.assertEqual(option(arguments, "-desiredWorkerCount"), "1")
        self.assertEqual(option(arguments, "-refreshImportMode"), "InProcess")
        serial = self.launch(host(), ("-refreshImportMode", "InProcess"))
        self.assertEqual(option(serial, "-refreshImportMode"), "InProcess")
        self.assertEqual(option(serial, "-desiredWorkerCount"), "1")
        bounded = self.launch(host(), ("-job-worker-count", "3", "-desiredWorkerCount", "2"))
        self.assertEqual(option(bounded, "-job-worker-count"), "3")
        self.assertEqual(option(bounded, "-desiredWorkerCount"), "2")
        for name in ("-job-worker-count", "-desiredWorkerCount"):
            with self.assertRaises(resources.ResourceError): self.launch(host(), (name,))

    def test_headless_linux_uses_isolated_display_or_safe_serial_fallback(self):
        snapshot = host(64, 56, None, platform="linux")
        with patch.object(resources.shutil, "which", side_effect=lambda name: "/usr/bin/" + name):
            arguments = self.launch(snapshot)
            self.assertEqual(arguments[:2], ["/usr/bin/xvfb-run", "-a"])
            self.assertEqual(option(arguments, "-refreshImportMode"), "OutOfProcessPerQueue")
        with patch.object(resources.shutil, "which", return_value=None):
            arguments = self.launch(snapshot)
            self.assertEqual(arguments[0], "Unity.exe")
            self.assertEqual(option(arguments, "-refreshImportMode"), "InProcess")
        os.environ["DISPLAY"] = ":fixture"
        with patch.object(resources.shutil, "which", side_effect=AssertionError("existing display must stay untouched")):
            self.assertEqual(self.launch(snapshot)[0], "Unity.exe")

    def test_policy_logs_effective_scopes_and_never_calls_jobqueue_shader_processes(self):
        with tempfile.TemporaryDirectory() as folder:
            os.environ[resources.EVIDENCE_ENV] = folder
            arguments = self.launch(host(), ("-job-worker-count", "1"))
            metrics = json.loads((Path(folder) / "build-metrics.json").read_text())
            queue = metrics["policies"]["unity"]
            imports = metrics["policies"]["unity-import"]
            self.assertEqual(queue["jobs"], 1)
            self.assertEqual(queue["schedulingScope"], "JobQueue-threads")
            self.assertEqual(imports["parallelImportWorkerProcesses"], 0)
            self.assertEqual(imports["refreshImportMode"], "InProcess")
            self.assertNotIn("shaderCompilerProcesses", queue)
            self.assertNotIn("-force-glcore", arguments)
            self.assertNotIn("-force-vulkan", arguments)

    def import_command(self):
        return ["Unity.exe", "-batchmode", "-quit", "-projectPath", "C:/work & spaces/project",
                "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdk",
                "-buildTarget", "Android", "-logFile", "C:/work & spaces/current-import.log",
                "-refreshImportMode", "OutOfProcessPerQueue", "-desiredWorkerCount", "9",
                "-job-worker-count", "15", "-standbyWorkerCount", "0"]

    def test_witnessed_initial_parallel_import_oom_retries_serial_once_without_changing_build_inputs(self):
        original = self.import_command()
        before = original.copy()
        retry = resources.unity_import_retry_arguments(original, memory_failure=True)
        self.assertEqual(original, before)
        self.assertIsNotNone(retry)
        expected = original.copy()
        for name, value in (("-refreshImportMode", "InProcess"), ("-desiredWorkerCount", "1"), ("-job-worker-count", "1")):
            expected[expected.index(name) + 1] = value
        self.assertEqual(retry, expected)
        self.assertIsNone(resources.unity_import_retry_arguments(retry, memory_failure=True))
        with patch.object(resources, "detect_host", return_value=host(128, 112, 112, 64)), patch("sys.stdout", new_callable=io.StringIO):
            qualified = resources.unity_launch_arguments(retry, Path.cwd())
        self.assertEqual(qualified, retry)
        wrapped = resources.unity_import_retry_arguments(["xvfb-run", "-a", *original], memory_failure=True)
        self.assertEqual(wrapped[:2], ["xvfb-run", "-a"])
        self.assertEqual(wrapped[2:], expected)

    def test_import_retry_never_guesses_memory_failure_or_duplicates_the_native_player_supervisor(self):
        for witness in (False, None, 1, "out of memory"):
            self.assertIsNone(resources.unity_import_retry_arguments(self.import_command(), memory_failure=witness))
        for method in ("GloomhavenVR.Quest.Editor.QuestBuildConcurrency.Build", "GloomhavenVR.Quest.Editor.QuestBuild.Build",
                       "DifferentNamespace.CompileStartupSdk", "GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdkExtra"):
            arguments = self.import_command()
            arguments[arguments.index("-executeMethod") + 1] = method
            self.assertIsNone(resources.unity_import_retry_arguments(arguments, memory_failure=True))
        for name, value in (("-refreshImportMode", "InProcess"), ("-desiredWorkerCount", "1")):
            arguments = self.import_command()
            arguments[arguments.index(name) + 1] = value
            self.assertIsNone(resources.unity_import_retry_arguments(arguments, memory_failure=True))
        arguments = self.import_command()
        arguments[0] = "different-tool.exe"
        self.assertIsNone(resources.unity_import_retry_arguments(arguments, memory_failure=True))

    def test_import_retry_rejects_malformed_or_ambiguous_existing_worker_policies(self):
        for name in ("-desiredWorkerCount", "-job-worker-count"):
            for value in ("0", "1025", "invalid", ""):
                arguments = self.import_command()
                arguments[arguments.index(name) + 1] = value
                self.assertIsNone(resources.unity_import_retry_arguments(arguments, memory_failure=True))
            duplicate = [*self.import_command(), name, "3"]
            self.assertIsNone(resources.unity_import_retry_arguments(duplicate, memory_failure=True))
            arguments = self.import_command()
            index = arguments.index(name)
            del arguments[index:index + 2]
            arguments.append(name)
            self.assertIsNone(resources.unity_import_retry_arguments(arguments, memory_failure=True))
        duplicate = [*self.import_command(), "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdk"]
        self.assertIsNone(resources.unity_import_retry_arguments(duplicate, memory_failure=True))
        missing_queue = self.import_command()
        index = missing_queue.index("-job-worker-count")
        del missing_queue[index:index + 2]
        repaired = resources.unity_import_retry_arguments(missing_queue, memory_failure=True)
        self.assertEqual(option(repaired, "-job-worker-count"), "1")


@unittest.skipUnless(os.environ.get("GHVRQ_TEST_UNITY_THROUGHPUT") == "1" and EDITOR.is_file(),
                     "Opt-in actual Unity 2021.3 import fixture")
class ActualUnityThroughput(unittest.TestCase):
    def test_native_2021_cli_parallel_workers_and_identical_texture_results(self):
        # Different evidenced runs avoid overwriting retained reports on another
        # invocation. All subprocesses are reaped before central cache cleanup.
        base = ROOT / ".planning/debug/quest-unity-throughput"
        base.mkdir(parents=True, exist_ok=True)
        rows = []
        retention_spec = importlib.util.spec_from_file_location("quest_throughput_retention", ROOT / "scripts/town_run_retention.py")
        retention = importlib.util.module_from_spec(retention_spec)
        retention_spec.loader.exec_module(retention)
        for count in (1, 3):
            run = Path(tempfile.mkdtemp(prefix="run-", dir=base))
            project = run / "fixture"
            for name in ("Assets/Editor", "Assets/Images", "Packages", "ProjectSettings"):
                (project / name).mkdir(parents=True, exist_ok=True)
            (project / "Packages/manifest.json").write_text('{"dependencies":{}}')
            (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
            (project / "Assets/Editor/ThroughputWitness.cs").write_text(ACTUAL_EDITOR_FIXTURE)
            for index in range(24):
                pixels = b"".join(b"\0" + bytes((x + index * 7) % 256 for x in range(512 * 3)) for _ in range(512))
                png = b"\x89PNG\r\n\x1a\n" + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 512, 512, 8, 2, 0, 0, 0))
                png += png_chunk(b"IDAT", zlib.compress(pixels, 1)) + png_chunk(b"IEND", b"")
                path = project / ("Assets/Images/image-" + str(index) + ".png")
                path.write_bytes(png)
                path.with_suffix(".png.meta").write_text("fileFormatVersion: 2\nguid: " + hashlib.sha256(path.name.encode()).hexdigest()[:32] + "\nTextureImporter:\n  serializedVersion: 11\n  maxTextureSize: 2048\n  nPOTScale: 0\n  textureType: 0\n  textureShape: 1\n  mipmaps:\n    enableMipMap: 1\n")
            mode = "InProcess" if count == 1 else "OutOfProcessPerQueue"
            log = run / "editor.log"
            command = [str(EDITOR), "-batchmode", "-quit", "-projectPath", str(project),
                       "-executeMethod", "ThroughputWitness.Run", "-logFile", str(log),
                       "-job-worker-count", "11", "-refreshImportMode", mode,
                       "-desiredWorkerCount", str(count), "-standbyWorkerCount", "0"]
            if os.sys.platform == "linux" and not os.environ.get("DISPLAY"):
                self.assertIsNotNone(shutil.which("xvfb-run"))
                command = [shutil.which("xvfb-run"), "-a", *command]
            process = None
            exit_code = 1
            started = time.monotonic()
            harness = run / "harness.log"
            harness.write_text("evidence: " + str(run) + "\n")
            try:
                with (run / "launch.log").open("w") as output:
                    process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
                    exit_code = process.wait(timeout=180)
                self.assertEqual(exit_code, 0, log.read_text(errors="replace")[-4000:])
                receipt = json.loads((project / "witness.json").read_text())
                self.assertEqual(receipt["mode"], mode)
                self.assertEqual(receipt["desired"], count)
                self.assertEqual(receipt["jobs"], 11)
                self.assertEqual(len(receipt["images"]), 24)
                if count > 1:
                    text = log.read_text(errors="replace")
                    workers = sorted(set(int(value) for value in re.findall(r"Worker ready: AssetImportWorker([0-9]+)", text)))
                    self.assertGreaterEqual(len(workers), 2, "Native import did not actually admit multiple workers")
                    receipt["observedImportWorkers"] = workers
                receipt["fixtureDurationSeconds"] = round(time.monotonic() - started, 3)
                (run / "result.json").write_text(json.dumps(receipt))
                rows.append(receipt)
            finally:
                if process is not None:
                    if process.poll() is None:
                        if os.name == "posix": os.killpg(process.pid, signal.SIGTERM)
                        else: process.terminate()
                        try: process.wait(timeout=10)
                        except subprocess.TimeoutExpired:
                            if os.name == "posix": os.killpg(process.pid, signal.SIGKILL)
                            else: process.kill()
                            process.wait()
                    exit_code = process.returncode
                cleaned = retention.cleanup_completed_run("quest-unity-throughput", exit_code, harness, ROOT, process_reaped=True)
                (run / "cleanup.json").write_text(json.dumps(cleaned))
        self.assertEqual(rows[0]["images"], rows[1]["images"])


def png_chunk(kind, value):
    return struct.pack(">I", len(value)) + kind + value + struct.pack(">I", zlib.crc32(kind + value) & 0xffffffff)


ACTUAL_EDITOR_FIXTURE = r'''
using System; using System.IO; using UnityEditor; using UnityEngine; using Unity.Jobs.LowLevel.Unsafe;
public static class ThroughputWitness {
 [Serializable] public class Receipt { public string mode; public int desired, jobs; public string[] images; }
 public static void Run() {
  if(Application.unityVersion != "2021.3.5f1") throw new Exception("wrong Editor");
  var receipt = new Receipt { mode = AssetDatabase.ActiveRefreshImportMode.ToString(), desired = AssetDatabase.DesiredWorkerCount,
   jobs = JobsUtility.JobWorkerCount, images = new string[24] };
  for(int i=0; i<24; i++) {
   string path = "Assets/Images/image-" + i + ".png";
   var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
   if(texture == null || texture.width != 512 || texture.height != 512) throw new Exception("invalid texture " + path + " actual=" + (texture == null ? "null" : texture.width + ":" + texture.height));
   receipt.images[i] = texture.width + ":" + texture.height + ":" + texture.format + ":" + texture.mipmapCount + ":" + texture.imageContentsHash;
  }
  Debug.Log("THROUGHPUT_WITNESS mode=" + receipt.mode + " desired=" + receipt.desired + " jobs=" + receipt.jobs);
  File.WriteAllText("witness.json", JsonUtility.ToJson(receipt));
 }
}
'''


if __name__ == "__main__": unittest.main()
