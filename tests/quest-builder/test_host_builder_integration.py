"""Builder identity delegation, argument routing and real command failure metrics."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import storage


class BuilderHostIntegration(unittest.TestCase):
    def test_cli_and_env_override_are_bounded_and_not_content_options(self):
        args = builder.parser().parse_args(["build", "--jobs", "12"])
        self.assertEqual(args.jobs, 12)
        with tempfile.TemporaryDirectory() as folder, patch.dict(os.environ, {builder.host_resources.JOBS_ENV: "8"}):
            with builder.host_resources.resource_context(Path(folder), args.jobs):
                self.assertEqual(os.environ[builder.host_resources.JOBS_ENV], "12")
            self.assertEqual(os.environ[builder.host_resources.JOBS_ENV], "8")
        with self.assertRaises(SystemExit): builder.parser().parse_args(["build", "--jobs", "0"])

    def test_unity_jobqueue_cap_is_distinct_from_native_backend_and_arguments_are_literal(self):
        with tempfile.TemporaryDirectory(prefix="Quest job & 100% ") as folder:
            root = Path(folder); log = root / "queue.log"; calls = []
            def run(argv, **kwargs):
                calls.append((argv, kwargs)); kwargs["stdout"].write("queue complete")
                return SimpleNamespace(returncode=0)
            arguments = [str(root / "Unity"), "-batchmode", "-projectPath", str(root / 'quoted " $(&) project')]
            environment = {"GHVRQ_BEE_THREADS": "2", "GHVRQ_BEE_LAUNCHER": str(root / "native host")}
            with patch.object(builder.host_resources, "phase_budget", return_value={"jobs": 7}), patch.object(builder.subprocess, "run", side_effect=run):
                result = builder.command(arguments, log, env=environment)
            self.assertEqual(calls[0][0], [*arguments, "-job-worker-count", "7"])
            self.assertEqual(calls[0][1]["env"], environment)
            self.assertNotIn("--threads", json.dumps(calls[0][0]))
            self.assertEqual(arguments[-1], str(root / 'quoted " $(&) project'))
            self.assertEqual(result, "queue complete")

    def test_real_nonzero_exit_emits_failed_phase_and_keeps_original_error(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with builder.host_resources.resource_context(root, 1), self.assertRaisesRegex(storage.BuildError, "exited with 17"):
                builder.command([sys.executable, "-c", "raise SystemExit(17)"], root / "command.log")
            records = [json.loads(line) for line in (root / "evidence/resource-events.jsonl").read_text().splitlines()]
            self.assertEqual(records[-1]["outcome"], "failed")
            self.assertNotIn("SystemExit", json.dumps(records))

    def test_git_free_release_delegates_exact_validator_and_adds_owned_local_dependencies(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / "quest-builder-release.json").write_text("verified delegated fixture")
            source = root / "src/current.cs"; source.parent.mkdir(); source.write_bytes(b"current mod source")
            records = storage.inventory(root, ["src/current.cs", "quest-builder-release.json"])
            refs = root / "libs/RuntimeDeps"; refs.mkdir(parents=True)
            (refs / "Unity.XR.OpenXR.dll").write_bytes(b"owner-derived local assembly")
            (refs / "wizard-dependencies.json").write_text('{"kind":"local"}')
            verifier = Mock(return_value=(records, "a" * 40, False))
            release = SimpleNamespace(verified_source_inventory=verifier)
            with patch.object(builder, "_release", release), patch.object(builder, "git_output", side_effect=AssertionError("Git must not run for release")):
                inventory, commit, dirty = builder.source_inventory(root)
                self.assertEqual(commit, "a" * 40); self.assertFalse(dirty)
                self.assertEqual({row["path"] for row in inventory}, {row["path"] for row in records} |
                                 {"libs/RuntimeDeps/Unity.XR.OpenXR.dll", "libs/RuntimeDeps/wizard-dependencies.json"})
                verifier.assert_called_once_with(root)
                # A rejection never degrades into permissive source enumeration.
                verifier.side_effect = storage.BuildError("unlisted or changed release source")
                with self.assertRaisesRegex(storage.BuildError, "unlisted or changed"):
                    builder.source_inventory(root)
                verifier.side_effect = None
                foreign = root / "outside.dll"; foreign.write_bytes(b"not a generated reference")
                (refs / "linked.dll").symlink_to(foreign)
                with self.assertRaisesRegex(storage.BuildError, "cannot be links"):
                    builder.source_inventory(root)

    def test_git_inventory_behavior_unchanged_and_missing_release_fails_closed(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / ".git").mkdir(); (root / "src").mkdir(); (root / "src/current.cs").write_bytes(b"modified source")
            git = Mock(side_effect=[b"src/current.cs\0", b"b" * 40 + b"\n", b" M src/current.cs\n"])
            with patch.object(builder, "git_output", git), patch.object(builder, "_release", SimpleNamespace(verified_source_inventory=Mock(side_effect=AssertionError("Git checkout must stay mutable")))):
                inventory, commit, dirty = builder.source_inventory(root)
            self.assertEqual(commit, "b" * 40); self.assertTrue(dirty)
            self.assertEqual(inventory, storage.inventory(root, ["src/current.cs"]))
            (root / ".git").rmdir()
            with self.assertRaisesRegex(storage.BuildError, "Git checkout or a verified"):
                builder.source_inventory(root)


if __name__ == "__main__": unittest.main()
