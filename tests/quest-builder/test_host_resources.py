"""Real host API contracts, pressure controls, literal subprocesses and evidence."""
import ctypes
import importlib.util
import json
import os
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
        self.assertEqual((low["jobs"], medium["jobs"], large["jobs"]), (1, 3, 7))
        self.assertFalse(low["memoryInsufficient"])
        self.assertGreaterEqual(low["largestWorkerReserveBytes"], 16 * GIB)
        self.assertGreaterEqual(low["additionalWorkerReserveBytes"], 12 * GIB)
        self.assertGreater(large["jobs"], 4)
        pressure = resources.choose_jobs(host(128, 15), "il2cpp", 32)
        self.assertEqual(pressure["jobs"], 1)
        self.assertTrue(pressure["memoryInsufficient"])
        self.assertEqual(resources.choose_jobs(host(128, 112), "il2cpp", 2)["jobs"], 2)

    def test_commit_affinity_and_unknown_memory_are_conservative(self):
        constrained = host(128, 112, cores=4, commit=28)
        policy = resources.choose_jobs(constrained, "il2cpp", 256)
        self.assertEqual(policy["jobs"], 1)
        self.assertTrue(policy["memoryInsufficient"])
        unknown = {**host(128, 112), "availableMemoryBytes": None, "commitHeadroomBytes": None}
        self.assertEqual(resources.choose_jobs(unknown, "box64", 32)["jobs"], 1)
        self.assertFalse(resources.choose_jobs(unknown, "box64")["memoryKnown"])
        self.assertEqual(resources.choose_jobs(host(128, 112, 2), "opus", 32)["jobs"], 2)

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
            self.assertEqual(json.loads(result.stdout.splitlines()[0]), [*args, "--threads=6"])
            self.assertIn("stdin=s", result.stdout)
            self.assertIn("native stderr", result.stderr)
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
