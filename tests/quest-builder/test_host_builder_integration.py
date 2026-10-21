"""Builder identity delegation, argument routing and real command failure metrics."""
from contextlib import ExitStack, nullcontext
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
    def test_measured_scheduling_allows_player_and_cached_results_need_no_capacity_probe(self):
        class NativeCommandReached(Exception): pass
        gib = builder.host_resources.GIB
        for commit, cached in ((None, False), (41, False), (42, False), (None, True)):
            with self.subTest(commit=commit, cached=cached), tempfile.TemporaryDirectory() as folder, ExitStack() as scope:
                root = Path(folder); source = root / "source"; project = root / "project"
                (source / "scripts").mkdir(parents=True)
                (source / "scripts/build-quest-native.py").write_text("# command is intercepted")
                args = SimpleNamespace(target="game", validate_campaign_shaders=False, dotnet="unused")
                scope.enter_context(patch.dict(os.environ, {
                    "GHVR_QUEST_VALIDATE_IMPORTED_ASSETS": "0", "GHVR_QUEST_VERIFICATION": ""}))
                inputs = {"inputKey": "a" * 64, "target": "game", "profile": {"isDummy": True}, "game": {"key": "c" * 64}}
                tools = {"key": "b" * 64, "androidNdk": "unused", "editor": str(root / "Unity")}
                provenance = {"schema": 1, "fixture": "unchanged inputs"}
                key = builder.value_hash({"input": inputs["inputKey"], "toolchain": tools["key"], "recipe": builder.RECIPE,
                                          "buildProvenance": builder.value_hash(provenance), "validateCampaignShaders": False})
                apk = root / "builds" / key / "GloomhavenVR-Quest.apk"
                apk.parent.mkdir(parents=True); apk.write_bytes(b"previous player")
                report = Path(str(apk) + ".build.json"); report.write_bytes(b"previous report")
                host = {"platform": "win32", "effectiveCpus": 32, "totalMemoryBytes": 32 * gib,
                        "availableMemoryBytes": 29 * gib, "commitHeadroomBytes": commit * gib if commit else None}
                policy = builder.host_resources.choose_jobs(host, "il2cpp", 32)
                class Stages:
                    def __init__(self, output): self.output = output
                    def run(self, stage, identity, action):
                        return {"details": {}} if cached else action()
                    def path(self, stage, identity): return self.output / "cache/verified-build.json"
                for name, result in (("toolchain", tools), ("weave", None), ("signing", (root / "key", {})),
                                     ("recover_delivery_pending", None), ("project_content_transaction", nullcontext())):
                    scope.enter_context(patch.object(builder, name, return_value=result))
                # Independent media producers own their own ready cache. This
                # test intercepts only native scheduling, never real sidecars.
                helpers = {name: Mock() for name in ("figure_banks", "audio_banks", "movie_banks")}
                for name in ("figure_banks", "audio_banks"):
                    helpers[name].produce.return_value = (root / name, {"fixture": name})
                helpers["movie_banks"].produce.return_value = {"metadata": {}, "files": []}
                actual_local = builder._local_helper
                scope.enter_context(patch.object(builder, "_local_helper", side_effect=lambda name:
                    helpers[name] if name in helpers else actual_local(name)))
                scope.enter_context(patch.object(builder, "Stages", Stages))
                scope.enter_context(patch.object(builder.build_provenance, "capture", return_value=provenance))
                budgets = scope.enter_context(patch.object(builder.host_resources, "phase_budget", return_value=policy))
                commands = scope.enter_context(patch.object(builder, "command", side_effect=NativeCommandReached))
                if cached:
                    self.assertEqual(builder.build(args, inputs, root, source, root / "game", project), apk)
                    budgets.assert_not_called(); commands.assert_not_called()
                else:
                    with self.assertRaises(NativeCommandReached):
                        builder.build(args, inputs, root, source, root / "game", project)
                    self.assertEqual(commands.call_count, 1)
                    self.assertEqual(commands.call_args.args[0][1], str(source / "scripts/build-quest-native.py"))
                    self.assertTrue(policy["nativeLaunchAllowed"])
                    self.assertGreaterEqual(policy["jobs"], 1)
                if cached:
                    self.assertEqual(apk.read_bytes(), b"previous player")
                    self.assertEqual(report.read_bytes(), b"previous report")

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
            # Pin the host rather than depending on Linux DISPLAY/xvfb or this
            # machine's available RAM. JobQueue and import-process counts differ.
            def jobs(_host, phase):
                return {"jobs": 3 if phase == "unity-import" else 7,
                        "memoryKnown": True, "memoryInsufficient": False, "additionalWorkerReserveBytes": 0}
            with patch.object(builder.host_resources, "detect_host", return_value={"platform": "win32", "effectiveCpus": 16}), \
                 patch.object(builder.host_resources, "choose_jobs", side_effect=jobs), \
                 patch.object(builder.subprocess, "run", side_effect=run):
                result = builder.command(arguments, log, env=environment)
            self.assertEqual(calls[0][0], [*arguments, "-job-worker-count", "7",
                "-refreshImportMode", "OutOfProcessPerQueue", "-desiredWorkerCount", "3", "-standbyWorkerCount", "0"])
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

    def test_unity_memory_retry_records_failed_then_successful_child_attempts(self):
        with tempfile.TemporaryDirectory() as folder, ExitStack() as scope:
            root = Path(folder); log = root / "retry.log"
            arguments = [str(root / "Unity"), "-batchmode", "-projectPath", str(root / "project"),
                "-refreshImportMode", "OutOfProcessPerQueue", "-job-worker-count", "3",
                "-desiredWorkerCount", "2", "-standbyWorkerCount", "0",
                "-executeMethod", "GloomhavenVR.Quest.Editor.QuestBuild.CompileStartupSdk"]
            calls = []
            def run(argv, **kwargs):
                calls.append(list(argv))
                kwargs["stdout"].write("System.OutOfMemoryException" if len(calls) == 1 else "completed retry")
                return SimpleNamespace(returncode=1 if len(calls) == 1 else 0)
            scope.enter_context(patch.object(builder.host_resources, "unity_launch_arguments", side_effect=lambda argv, _: list(argv)))
            scope.enter_context(patch.object(builder.subprocess, "run", side_effect=run))
            scope.enter_context(patch.object(builder.unity_work, "publish_plan", return_value=None))
            scope.enter_context(patch.object(builder.unity_work, "finish_plan"))
            wait = scope.enter_context(patch.object(builder.project_access, "wait_for_editor"))
            with builder.host_resources.resource_context(root, 1):
                self.assertEqual(builder.command(arguments, log), "completed retry")
            wait.assert_called_once()
            self.assertEqual(len(calls), 2)
            self.assertEqual(calls[1][calls[1].index("-refreshImportMode") + 1], "InProcess")
            self.assertEqual(calls[1][calls[1].index("-job-worker-count") + 1], "1")
            records = [json.loads(line) for line in (root / "evidence/resource-events.jsonl").read_text().splitlines()]
            finished = [row for row in records if row.get("event") == "phase" and row.get("state") == "finished"]
            self.assertEqual([row["outcome"] for row in finished], ["failed", "succeeded"])
            self.assertNotEqual(finished[0]["id"], finished[1]["id"])

    def test_exact_command_observer_keeps_original_closures_but_unknown_retry_edits_do_not(self):
        identity = builder.preparation_identity
        raw = (ROOT / identity.BUILDER).read_bytes()
        changed = raw.replace(b"if error is not nonzero_error: raise", b"if False: raise")
        self.assertNotEqual(raw, changed)
        for prefix, expected in ((False, "6905242806bb695c86e84be373112f21804c425afa710398cb40b665f4d608d1"),
                                 (True, "cc09e2c539f383d447f494d386bd5fa6dd8df9989a964543d98c33f4bea6385f")):
            self.assertEqual(identity.builder_producer_digest(raw, original_prefix=prefix), expected)
            self.assertNotEqual(identity.builder_producer_digest(changed, original_prefix=prefix), expected)

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
            release = SimpleNamespace(owned_derived_source=builder._release.owned_derived_source,
                                      verified_source_inventory=Mock(side_effect=AssertionError("Git checkout must stay mutable")))
            with patch.object(builder, "git_output", git), patch.object(builder, "_release", release):
                inventory, commit, dirty = builder.source_inventory(root)
            self.assertEqual(commit, "b" * 40); self.assertTrue(dirty)
            self.assertEqual(inventory, storage.inventory(root, ["src/current.cs"]))
            (root / ".git").rmdir()
            with self.assertRaisesRegex(storage.BuildError, "Git checkout or a verified"):
                builder.source_inventory(root)


if __name__ == "__main__": unittest.main()
