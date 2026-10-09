"""Native capacity stops before recovery, preserves finished outputs and context."""
import contextlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import host_resources
import native_admission
import preparation_identity
import progress
import storage

GIB = 1024 ** 3


def captured_host():
    return {"platform": "win32", "totalMemoryBytes": 68438433792,
            "availableMemoryBytes": 35246387200, "commitHeadroomBytes": 26303938560,
            "effectiveCpus": 16, "logicalCpus": 16, "affinityCpus": 16,
            "diskFreeBytes": 301799550976, "warnings": []}


class NativeAdmissionTests(unittest.TestCase):
    def test_captured_host_rejects_with_exact_snapshot_and_typed_context(self):
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            with patch.object(host_resources, "detect_host", return_value=captured_host()), contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaises(native_admission.NativeMemoryUnavailable) as error:
                    native_admission.require_capacity(host_resources, progress, output, target="game")
            self.assertEqual(error.exception.code, "native_memory_unavailable")
            self.assertEqual(error.exception.resources["host"]["commitHeadroomBytes"], 26303938560)
            self.assertEqual(error.exception.resources["policy"]["requiredCommitHeadroomBytes"], 40 * GIB)
            storage.write_json(output / "last-failure.json", {"stage": "build", "message": "outer wrapper"})
            native_admission.persist_failure(error.exception, output, storage.write_json)
            saved = json.loads((output / "last-failure.json").read_text())
            self.assertEqual(saved["failureStage"], "native-memory-check")
            self.assertTrue(saved["retained"])
            self.assertEqual(saved["resources"], error.exception.resources)

    def test_smaller_ram_has_one_measured_paging_worker_when_commit_is_sufficient(self):
        with tempfile.TemporaryDirectory() as temporary:
            for ram in (16, 32):
                host = captured_host()
                host.update(totalMemoryBytes=ram * GIB, availableMemoryBytes=(ram - 4) * GIB,
                            commitHeadroomBytes=48 * GIB)
                with self.subTest(ram=ram), patch.object(host_resources, "detect_host", return_value=host), contextlib.redirect_stdout(io.StringIO()):
                    policy = native_admission.require_capacity(host_resources, progress, Path(temporary), target="game")
                self.assertEqual(policy["jobs"], 1)
                self.assertTrue(policy["pagingRequired"])

    def test_actual_cli_stops_before_snapshot_prepare_or_unity_and_keeps_originals(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            output = root / "owned-output"
            original = root / "owned-game"; original.mkdir()
            (original / "original.bin").write_bytes(b"owned original bytes")
            dependencies = types.SimpleNamespace(python_environment=lambda *a, **k: Path(sys.executable))
            with patch.dict(sys.modules, dependencies=dependencies), \
                    patch.object(builder, "game_data", return_value=original), \
                    patch.object(builder, "inspect_inputs", return_value={"inputKey": "a" * 64}), \
                    patch.object(builder.host_resources, "detect_host", return_value=captured_host()), \
                    patch.object(builder, "snapshot_inputs") as snapshot, \
                    patch.object(builder, "prepare") as prepare, \
                    patch.object(builder, "build") as build, \
                    contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                result = builder.main(["build", "--game-root", str(original), "--output-root", str(output)])
            self.assertEqual(result, 1)
            snapshot.assert_not_called(); prepare.assert_not_called(); build.assert_not_called()
            self.assertEqual((original / "original.bin").read_bytes(), b"owned original bytes")
            saved = json.loads((output / "last-failure.json").read_text())
            self.assertEqual(saved["code"], "native_memory_unavailable")
            self.assertEqual(saved["stage"], "native-memory-check")

    def test_reviewed_readonly_preflight_does_not_invalidate_preceding_builder(self):
        previous = subprocess.check_output(["git", "show", "ee5b916b7:" + preparation_identity.BUILDER], cwd=ROOT)
        current = (ROOT / preparation_identity.BUILDER).read_bytes()
        self.assertNotEqual(previous, current)
        self.assertEqual(preparation_identity.builder_producer_digest(previous),
                         preparation_identity.builder_producer_digest(current))
        for before, after in (
            (b"target=args.target)", b"target='unqualified')"),
            (b"policy = native_admission.require_capacity(host_resources, build_progress, output, target=args.target)",
             b"policy = native_admission.require_capacity(other_resources, build_progress, output, target=args.target)"),
            (b"    def generate_files(resume):", b"    def generate_files(resume):\n        native_admission.require_capacity(host_resources, build_progress, output, target=args.target)"),
        ):
            with self.subTest(change=after):
                changed = current.replace(before, after)
                self.assertNotEqual(current, changed)
                self.assertNotEqual(preparation_identity.builder_producer_digest(current),
                                    preparation_identity.builder_producer_digest(changed))


if __name__ == "__main__":
    unittest.main()
