"""Real host API contracts, pressure controls, literal subprocesses and evidence."""
import ctypes
import importlib.util
import io
import json
import os
import shlex
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("quest_host_resource_test", ROOT / "tools/quest-builder/host_resources.py")
resources = importlib.util.module_from_spec(spec)
spec.loader.exec_module(resources)
GIB = resources.GIB
UNITY_DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
BEE = UNITY_DATA / ("bee_backend_real" if (UNITY_DATA / "bee_backend_real").is_file() else "bee_backend")
DOTNET = Path(os.environ.get("GHVR_QUEST_TEST_DOTNET", "/home/claw/.dotnet/dotnet"))


def host(total, available, cores=32, commit=None):
    return {"schema": 1, "platform": "win32", "logicalCpus": cores, "effectiveCpus": cores,
            "totalMemoryBytes": total * GIB, "availableMemoryBytes": available * GIB,
            "commitHeadroomBytes": commit * GIB if commit is not None else None,
            "diskFreeBytes": 200 * GIB, "warnings": []}


class HostResources(unittest.TestCase):
    def setUp(self):
        self.environment = patch.dict(os.environ, {}, clear=True)
        self.environment.start()
        self.addCleanup(self.environment.stop)

    def test_large_native_peak_limits_override_and_fast_machine_scales(self):
        low = resources.choose_jobs(host(32, 29), "il2cpp", 32)
        medium = resources.choose_jobs(host(64, 56), "il2cpp", 32)
        large = resources.choose_jobs(host(128, 112), "il2cpp", 32)
        self.assertEqual((low["jobs"], medium["jobs"], large["jobs"]), (1, 1, 3))
        # One remains the minimum dispatch count, but does not imply sufficient
        # physical RAM. Paging/low-memory handling must consume this warning.
        self.assertTrue(low["memoryInsufficient"])
        self.assertFalse(medium["memoryInsufficient"])
        self.assertFalse(large["memoryInsufficient"])
        self.assertGreaterEqual(low["largestWorkerReserveBytes"], 32 * GIB)
        self.assertGreaterEqual(low["additionalWorkerReserveBytes"], 28 * GIB)
        self.assertGreater(resources.choose_jobs(host(256, 236), "il2cpp")["jobs"], 4)
        pressure = resources.choose_jobs(host(128, 15), "il2cpp", 32)
        self.assertEqual(pressure["jobs"], 1)
        self.assertTrue(pressure["memoryInsufficient"])
        self.assertEqual(resources.choose_jobs(host(128, 112), "il2cpp", 2)["jobs"], 2)
        self.assertEqual(resources.choose_jobs(host(32, 29, commit=96), "il2cpp", 32)["jobs"], 1)
        self.assertTrue(resources.choose_jobs(host(32, 29, commit=96), "il2cpp", 32)["memoryInsufficient"])

    def test_windows_single_job_paging_requires_known_sufficient_commit(self):
        constrained = host(32, 29, commit=96)
        policy = resources.choose_jobs(constrained, "il2cpp", 32)
        self.assertEqual(policy["jobs"], 1)
        self.assertTrue(policy["memoryInsufficient"])
        self.assertTrue(policy["pagingRequired"])
        self.assertTrue(policy["nativeLaunchAllowed"])
        self.assertEqual(policy["requiredCommitHeadroomBytes"], 42 * GIB)
        self.assertFalse(policy["pagingExecutionVerified"])
        for commit in (None, 41):
            rejected = resources.choose_jobs(host(32, 29, commit=commit), "il2cpp", 32)
            self.assertFalse(rejected["nativeLaunchAllowed"])
            self.assertFalse(rejected["pagingRequired"])
            self.assertEqual(rejected["jobs"], 1)
        threshold = resources.choose_jobs(host(32, 29, commit=42), "il2cpp", 32)
        self.assertTrue(threshold["pagingRequired"])
        self.assertTrue(threshold["nativeLaunchAllowed"])
        linux = resources.choose_jobs({**constrained, "platform": "linux"}, "il2cpp", 32)
        self.assertFalse(linux["pagingRequired"])
        self.assertFalse(linux["nativeLaunchAllowed"])
        unknown = resources.choose_jobs({**constrained, "availableMemoryBytes": None}, "il2cpp", 32)
        self.assertFalse(unknown["nativeLaunchAllowed"])
        unknown_commit = resources.choose_jobs(host(128, 112), "il2cpp", 32)
        self.assertFalse(unknown_commit["nativeLaunchAllowed"])
        physical = resources.choose_jobs(host(128, 112, commit=200), "il2cpp", 32)
        self.assertEqual(physical["jobs"], 3)
        self.assertTrue(physical["nativeLaunchAllowed"])
        self.assertFalse(physical["pagingRequired"])
        with tempfile.TemporaryDirectory() as folder, resources.resource_context(folder, 32), \
             patch.object(resources, "detect_host", return_value=constrained), patch("sys.stdout", new_callable=io.StringIO) as console:
            resources.phase_budget("il2cpp", folder)
            metrics = json.loads((Path(folder) / "build-metrics.json").read_text())
            self.assertEqual(metrics["policies"]["il2cpp"]["jobs"], 1)
            self.assertTrue(metrics["policies"]["il2cpp"]["pagingRequired"])
            self.assertIn("may be significantly slower", console.getvalue())
            self.assertIn("not yet qualified", console.getvalue())

    def test_commit_affinity_and_unknown_memory_are_conservative(self):
        constrained = host(128, 112, cores=4, commit=28)
        policy = resources.choose_jobs(constrained, "il2cpp", 256)
        self.assertEqual(policy["jobs"], 1)
        self.assertTrue(policy["memoryInsufficient"])
        unknown = {**host(128, 112), "availableMemoryBytes": None, "commitHeadroomBytes": None}
        self.assertEqual(resources.choose_jobs(unknown, "box64", 32)["jobs"], 1)
        self.assertFalse(resources.choose_jobs(unknown, "box64")["memoryKnown"])
        self.assertEqual(resources.choose_jobs(host(128, 112, 2), "opus", 32)["jobs"], 2)

    def test_independent_asset_codec_policy_scales_without_relaxing_native_peak(self):
        medium=resources.choose_jobs(host(32,28,cores=32,commit=40),'asset-codec',32)
        fast=resources.choose_jobs(host(128,112,cores=64,commit=160),'asset-codec',64)
        self.assertGreater(medium['jobs'],1);self.assertEqual(fast['jobs'],16)
        self.assertEqual(resources.PROFILES['il2cpp'][:2],(32*GIB,28*GIB))
        self.assertEqual(medium['parentReserveBytes'],6*GIB)
        unknown={**host(128,112),'availableMemoryBytes':None,'commitHeadroomBytes':None}
        self.assertEqual(resources.choose_jobs(unknown,'asset-codec',64)['jobs'],1)
        self.assertEqual(resources.choose_jobs(host(128,112,cores=2),'asset-codec',64)['jobs'],2)

    def test_native_profiles_differ_and_shared_override_is_validated(self):
        os.environ[resources.JOBS_ENV] = "9"
        self.assertEqual(resources.choose_jobs(host(128, 112), "opus")["jobs"], 9)
        self.assertGreater(resources.choose_jobs(host(64, 56), "box64")["jobs"], 4)
        self.assertGreater(resources.choose_jobs(host(64, 56), "opus")["jobs"],
                           resources.choose_jobs(host(64, 56), "il2cpp")["jobs"])
        for invalid in ("0", "-1", "1.5", "NaN", "1025", True, "1; malicious"):
            with self.subTest(value=invalid), self.assertRaises(resources.ResourceError):
                resources.parse_jobs(invalid)
        with self.assertRaises(resources.ResourceError):
            resources.choose_jobs(host(32, 29), "unrecognized")

    def test_windows_api_handles_pointer_sized_masks_and_commit_headroom(self):
        class Function:
            def __init__(self, call): self.call = call
            def __call__(self, *args): return self.call(*args)
        class Kernel:
            def memory(self, pointer):
                state = pointer._obj
                self.length = state.length
                state.totalPhys, state.availPhys, state.availPageFile = 64 * GIB, 52 * GIB, 28 * GIB
                return 1
            def affinity(self, process, process_mask, system_mask):
                self.process = process
                process_mask._obj.value = (1 << 40) | (1 << 41)
                return 1
        kernel = Kernel()
        kernel.GlobalMemoryStatusEx = Function(kernel.memory)
        kernel.GetCurrentProcess = Function(lambda: (1 << 63) - 1)
        kernel.GetProcessAffinityMask = Function(kernel.affinity)
        state = resources._windows_snapshot(kernel)
        self.assertEqual(kernel.length, ctypes.sizeof(resources.MemoryStatus))
        self.assertEqual(state["affinityCpus"], 2)
        self.assertEqual(state["commitHeadroomBytes"], 28 * GIB)
        self.assertEqual(kernel.GetCurrentProcess.restype, ctypes.c_void_p)
        self.assertEqual(kernel.GetProcessAffinityMask.argtypes[0], ctypes.c_void_p)
        kernel.GlobalMemoryStatusEx = Function(lambda _: 0)
        with self.assertRaises(OSError): resources._windows_snapshot(kernel)

    def test_linux_cgroup_ancestor_memory_and_cpu_limits(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); proc = root / "proc"; proc.mkdir(); (proc / "self").mkdir()
            (proc / "meminfo").write_text("MemTotal: 67108864 kB\nMemAvailable: 58720256 kB\n")
            group = root / "cgroup"; child = group / "build"; child.mkdir(parents=True)
            (proc / "self/cgroup").write_text("0::/build\n")
            for path, limit, used, quota in ((group, 32, 8, "400000 100000"), (child, 20, 4, "200000 100000")):
                (path / "memory.max").write_text(str(limit * GIB)); (path / "memory.current").write_text(str(used * GIB))
                (path / "cpu.max").write_text(quota)
            state = resources._linux_snapshot(proc, group)
            self.assertEqual(state["totalMemoryBytes"], 20 * GIB)
            self.assertEqual(state["availableMemoryBytes"], 16 * GIB)
            self.assertEqual(state["quotaCpus"], 2)

    def test_detection_checks_nearest_existing_drive_and_degrades_without_memory(self):
        with tempfile.TemporaryDirectory() as folder, patch.object(resources, "_linux_snapshot", side_effect=OSError), \
             patch.object(resources.sys, "platform", "linux"), patch.object(resources.os, "cpu_count", return_value=64), \
             patch.object(resources.os, "sched_getaffinity", return_value={1, 2}, create=True):
            state = resources.detect_host(Path(folder) / "not-created/yet")
            self.assertEqual(state["effectiveCpus"], 2)
            self.assertIsNone(state["availableMemoryBytes"])
            self.assertGreater(state["diskFreeBytes"], 0)
            self.assertIn("memory-detection-unavailable", state["warnings"])

    def test_phase_metrics_keep_jobs_timings_failure_and_restore_environment(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            original = dict(os.environ)
            with resources.resource_context(root, 7), patch.object(resources, "detect_host", return_value=host(128, 112)):
                os.environ[resources.INPUT_ENV] = "a" * 64
                policy = resources.phase_budget("opus", root)
                self.assertEqual(policy["jobs"], 7)
                with resources.timed_phase("codec", log=root / "codec.log"): pass
                with self.assertRaises(RuntimeError), resources.timed_phase("broken"):
                    raise RuntimeError("private arguments must not enter metrics")
            self.assertEqual(dict(os.environ), original)
            events = [json.loads(line) for line in (root / "evidence/resource-events.jsonl").read_text().splitlines()]
            ends = [row for row in events if row.get("state") == "finished"]
            self.assertEqual([row["outcome"] for row in ends], ["succeeded", "failed"])
            self.assertTrue(all(row["durationSeconds"] >= 0 for row in ends))
            summary = json.loads((root / "build-metrics.json").read_text())
            self.assertEqual(summary["policies"]["opus"]["jobs"], 7)
            self.assertEqual(summary["inputKey"], "a" * 64)
            self.assertNotIn("private arguments", json.dumps(summary))
            self.assertNotIn(str(root), json.dumps(summary))


@unittest.skipUnless(DOTNET.is_file() and sys.platform == "linux", "Native apphost test requires the installed .NET8 Linux SDK")
class NativeApphost(unittest.TestCase):
    @unittest.skipUnless(BEE.is_file(), "Actual installed Unity Bee backend unavailable")
    def test_actual_native_bee_threads_are_parsed_before_target_and_bound_execution(self):
        with tempfile.TemporaryDirectory(prefix="quest-native-bee-") as folder:
            root = Path(folder)
            launcher = resources.prepare_bee_launcher(DOTNET, root)
            for threads in (1, 2):
                work = root / str(threads); work.mkdir()
                script = work / "action.py"
                script.write_text('import json,sys,time\nfrom pathlib import Path\n'
                    'start=time.monotonic();time.sleep(0.2);end=time.monotonic()\n'
                    'Path(sys.argv[1]+".done").write_text(json.dumps(dict(start=start,end=end)))\n')
                nodes = [{"Annotation": "all", "Action": "", "Inputs": [], "Outputs": [],
                          "ToBuildDependencies": [1, 2, 3, 4], "AllowUnexpectedOutput": True, "DebugActionIndex": 0}]
                for index in range(1, 5):
                    nodes.append({"Annotation": "tiny-action-" + str(index),
                                  "Action": shlex.quote(sys.executable) + " " + shlex.quote(str(script)) + " " + str(index),
                                  "Inputs": [str(script)], "Outputs": [str(index) + ".done"],
                                  "ToBuildDependencies": [], "AllowUnexpectedOutput": False, "DebugActionIndex": index})
                graph = {"Nodes": nodes, "FileSignatures": [], "StatSignatures": [], "GlobSignatures": [],
                         "ContentDigestExtensions": [], "EmitDataForBeeWhy": 0, "NamedNodes": {"all": 0},
                         "DefaultNodes": [0], "SharedResources": [], "Scanners": [],
                         "Identifier": str(work / "graph.json"), "RelativePathToRoot": "."}
                for field in ("StructuredLogFileName", "StateFileName", "StateFileNameTmp", "StateFileNameMapped",
                              "ScanCacheFileName", "ScanCacheFileNameTmp", "DigestCacheFileName", "DigestCacheFileNameTmp"):
                    graph[field] = str(work / field)
                (work / "graph.json").write_text(json.dumps(graph))
                environment = {**os.environ, "DOTNET_ROOT": str(DOTNET.parent), "GHVRQ_BEE_REAL_PATH": str(BEE),
                               "GHVRQ_BEE_THREADS": str(threads)}
                result = subprocess.run([str(launcher), "--dagfile=" + str(work / "graph.dag"),
                                         "--dagfilejson=" + str(work / "graph.json"), "all"],
                                        cwd=work, env=environment, capture_output=True, text=True, timeout=20)
                self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
                intervals = [json.loads(path.read_text()) for path in work.glob("*.done")]
                self.assertEqual(len(intervals), 4)
                events = sorted([(row["start"], 1) for row in intervals] + [(row["end"], -1) for row in intervals])
                active, peak = 0, 0
                for _, delta in events: active += delta; peak = max(peak, active)
                self.assertEqual(peak, threads, "actual native backend ignored its thread cap")

    def test_real_apphost_forwards_literal_args_stdio_eof_and_exit_and_reuses_cache(self):
        with tempfile.TemporaryDirectory(prefix="Quest host & 100% ") as folder:
            root = Path(folder)
            launcher = resources.prepare_bee_launcher(DOTNET, root)
            backend = root / "native backend"
            backend.write_text("#!" + sys.executable + "\nimport sys,json\n"
                               "print(json.dumps(sys.argv[1:]),flush=True)\n"
                               "print('native stderr',file=sys.stderr,flush=True)\n"
                               "print('stdin=' + sys.stdin.read(),flush=True)\nraise SystemExit(17)\n")
            backend.chmod(0o700)
            env = {**os.environ, "DOTNET_ROOT": str(DOTNET.parent), "GHVRQ_BEE_REAL_PATH": str(backend), "GHVRQ_BEE_THREADS": "6"}
            args = ["--stdin-canary", "--dagfile=C:\\literal path & 100%\\a.dag", 'quote " and $(literal)']
            result = subprocess.run([str(launcher), *args], env=env, input="s\n", capture_output=True, text=True, timeout=20)
            self.assertEqual(result.returncode, 17, result.stdout + result.stderr)
            self.assertEqual(json.loads(result.stdout.splitlines()[0]), ["--threads=6", *args])
            self.assertIn("stdin=s", result.stdout)
            self.assertIn("native stderr", result.stderr)
            conflicts = subprocess.run([str(launcher), "--threads=32"], env=env, capture_output=True, text=True, timeout=10)
            self.assertEqual(conflicts.returncode, 126)
            self.assertIn("Unexpected existing Bee thread override", conflicts.stderr)
            with patch.object(resources.subprocess, "run", side_effect=AssertionError("cache hit must not compile")), \
                 patch.dict(os.environ, {resources.JOBS_ENV: "1"}):
                self.assertEqual(resources.prepare_bee_launcher(DOTNET, root), launcher)
            for bad in ("0", "-1", "1025"):
                failed = subprocess.run([str(launcher)], env={**env, "GHVRQ_BEE_THREADS": bad}, capture_output=True, text=True, timeout=10)
                self.assertEqual(failed.returncode, 126)
                self.assertNotIn("native stderr", failed.stderr)
            launcher.write_bytes(launcher.read_bytes() + b"modified")
            with self.assertRaisesRegex(resources.ResourceError, "differs from its receipt"):
                resources.prepare_bee_launcher(DOTNET, root)


if __name__ == "__main__": unittest.main()
