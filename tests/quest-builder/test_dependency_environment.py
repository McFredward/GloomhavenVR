"""Fresh stdlib launchers hand real retained staging to an isolated build ABI.

External tools/native assets use declared nonproprietary fixtures. The actual
CLI, dependency owner, closed metadata consumer, staging journal, canonical
witness and copy writer execute; no Unity Editor or game export is performed.
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
from unittest.mock import patch
import venv

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import dependencies
import storage


class EnvironmentTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.repo = self.root / "source"
        self.repo.mkdir()
        self.game = self.root / "pc-game"
        (self.game / "Managed").mkdir(parents=True)
        (self.game / "Managed/GH.Runtime.dll").write_bytes(b"owned original fixture")
        (self.game / "Managed/GH.Shared.dll").write_bytes(b"owned shared fixture")
        for name in ("globalgamemanagers", "resources.assets"):
            (self.game / name).write_bytes(b"owned original fixture")
        (self.game / "StreamingAssets/Rulebase").mkdir(parents=True)
        self.output = storage.ensure_output(self.root / "output", self.repo, self.game)
        self.arguments = ["prepare", "--repo-root", str(self.repo), "--game-root", str(self.game),
                          "--output-root", str(self.output), "--target", "game"]

    def test_handoff_releases_output_lock_and_preserves_arguments_and_child_failure(self):
        executable = self.root / "private-build-python"
        calls = []
        def launch(arguments):
            calls.append(arguments)
            def wait():
                with storage.output_lock(self.output):
                    return 17
            return types.SimpleNamespace(wait=wait, terminate=lambda: self.fail("completed child terminated"))
        prior = self.output / "last-failure.json"
        storage.write_json(prior, {"error": "old attempt"})
        before = list(sys.path)
        with patch.object(dependencies, "python_environment", return_value=executable) as environment, \
             patch.object(builder.subprocess, "Popen", side_effect=launch), \
             patch.object(builder, "inspect_inputs", side_effect=AssertionError("launcher began conversion")):
            self.assertEqual(builder.main(self.arguments), 17)
        environment.assert_called_once_with(self.output / "tool-cache", self.repo, procedural=True, activate=False)
        self.assertEqual(calls, [[str(executable), "-I", "-B", "-X", "utf8", str(Path(builder.__file__).resolve()), *self.arguments]])
        self.assertEqual(sys.path, before)
        self.assertFalse(prior.exists())
        self.assertFalse((self.output / ".builder.lock").exists())

    def test_interruption_terminates_and_reaps_the_supervised_build_child(self):
        waited = []
        def wait():
            waited.append(1)
            if len(waited) == 1: raise KeyboardInterrupt()
            return 130
        child = types.SimpleNamespace(wait=wait, terminate=lambda: waited.append("terminated"))
        with patch.object(dependencies, "python_environment", return_value=self.root / "build-python"), \
             patch.object(builder.subprocess, "Popen", return_value=child):
            self.assertEqual(builder.main(self.arguments), 130)
        self.assertEqual(waited, [1, "terminated", 1])

    def test_already_handed_off_cli_qualifies_once_and_passes_its_current_environment(self):
        executable = self.root / "owned-build-python"
        with patch.object(sys, "executable", str(executable)), \
             patch.object(dependencies, "python_environment", return_value=executable) as environment, \
             patch.object(builder.subprocess, "Popen", side_effect=AssertionError("recursive handoff")), \
             patch.object(builder, "inspect_inputs", return_value={"inputKey": "a" * 64}), \
             patch.object(builder, "snapshot_inputs", return_value=(self.repo, self.game)), \
             patch.object(builder, "prepare", return_value=self.output / "prepared") as prepare:
            self.assertEqual(builder.main(self.arguments), 0)
        environment.assert_called_once_with(self.output / "tool-cache", self.repo, procedural=True, activate=False)
        self.assertEqual(prepare.call_args.kwargs, {"conversion_python": executable})

    def test_provisioning_failure_replaces_old_diagnostics_before_any_recovery_work(self):
        storage.write_json(self.output / "last-failure.json", {"error": "old failure"})
        with patch.object(dependencies, "python_environment", side_effect=storage.BuildError("private package setup failed")), \
             patch.object(builder, "inspect_inputs", side_effect=AssertionError("conversion started")):
            self.assertEqual(builder.main(self.arguments), 1)
        failure = json.loads((self.output / "last-failure.json").read_text())
        self.assertEqual(failure["stage"], "builder-python")
        self.assertEqual(failure["message"], "private package setup failed")

    def test_read_only_inspection_does_not_provision_or_launch_a_build_environment(self):
        with patch.object(dependencies, "python_environment", side_effect=AssertionError("read-only package setup")), \
             patch.object(builder, "inspect_inputs", return_value={"inputKey": "a" * 64}) as inspect, \
             patch.object(builder.subprocess, "Popen", side_effect=AssertionError("read-only handoff")):
            self.assertEqual(builder.main(["inspect", *self.arguments[1:]]), 0)
        inspect.assert_called_once()

    def test_real_private_environment_can_be_qualified_without_polluting_launcher_imports(self):
        paths = []
        for relative in ("tools/quest-builder/requirements.txt", "tools/quest-builder/requirements-bootstrap.txt",
                         "tools/quest-builder/requirements-source.txt", "tools/quest-procedural-runtime/requirements.txt"):
            path = self.repo / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_text("# fixture requirements\n")
            paths.append(path)
        abi = dependencies.python_abi()
        root = self.output / "tool-cache" / ("build-python-" + storage.value_hash(abi)[:16])
        venv.EnvBuilder(with_pip=False, symlinks=os.name != "nt").create(root)
        storage.write_json(root / ".ghvr-build-python.json", {"schema": 1, "owner": "GloomhavenVR builder",
            "requirementsKey": dependencies.requirements_key(paths), "pythonAbi": abi})
        executable = root / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
        before = list(sys.path)
        run = subprocess.run
        def probe_only(arguments, **kwargs):
            self.assertEqual(arguments[1:3], ["-I", "-c"], "qualified packages reinstalled")
            return run(arguments, **kwargs)
        with patch.object(dependencies.subprocess, "run", side_effect=probe_only), \
             patch.object(dependencies.site, "addsitedir", side_effect=AssertionError("launcher polluted")):
            self.assertEqual(dependencies.python_environment(self.output / "tool-cache", self.repo, activate=False), executable)
        self.assertEqual(sys.path, before)


class FreshCliStagingTests(unittest.TestCase):
    def run_cli(self, retained):
        spec = importlib.util.spec_from_file_location("quest_completed_raw_fixture", ROOT / "tests/quest-builder/test_completed_raw.py")
        module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
        fixture = module.CompletedRawTests("test_real_complete_result_is_adopted_without_redundant_raw_asset_reads")
        fixture.setUp(); self.addCleanup(fixture.doCleanups)
        source = fixture.root / "selected-source"
        for directory in ("quest-builder", "quest-recovery"):
            for path in (ROOT / "tools" / directory).glob("*.py"):
                target = source / "tools" / directory / path.name
                target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(path, target)
        requirement_paths = []
        for relative in ("tools/quest-builder/requirements.txt", "tools/quest-builder/requirements-bootstrap.txt",
                         "tools/quest-builder/requirements-source.txt", "tools/quest-procedural-runtime/requirements.txt"):
            path = source / relative; path.parent.mkdir(parents=True, exist_ok=True); path.write_text("# fixture requirements\n")
            requirement_paths.append(path)
        output = fixture.root / "output"
        storage.write_json(output / ".quest-builder-output.json", {"schema": 1, "purpose": "GloomhavenVR local Quest conversion"})
        original = fixture.root / "pc-game"
        shutil.copytree(fixture.game, original)
        (original / "Managed/GH.Shared.dll").write_bytes(b"owned shared fixture")
        for name in ("globalgamemanagers", "resources.assets"):
            (original / name).write_bytes(b"owned original fixture")
        (original / "StreamingAssets/Rulebase").mkdir(parents=True)
        guid = "a" * 32
        fixture.file(Path(str(fixture.asset) + ".meta"), ("guid: " + guid + "\n").encode())
        fixture.checkpoint["identities"] = [{"path": "Assets/native.asset", "guid": guid,
            "objects": [{"classId": 48, "collection": "original.bundle", "pathId": 1, "fileId": 1}]}]
        fixture.checkpoint["files"] = [row for row in fixture.records(fixture.raw) if row["path"] != "quest-full-recovery-progress.json"]
        storage.write_json(fixture.checkpoint_path, fixture.checkpoint)
        fixture.inputs["inputKey"] = "5" * 64
        storage.write_json(source / "fixture.json", {"inputs": fixture.inputs, "game": str(fixture.game),
            "workspace": str(fixture.workspace), "rawResult": fixture.result, "retained": retained,
            "nativeHash": storage.digest(fixture.game / "original.bundle")})
        if not retained: (fixture.workspace / "full-recovery.json").unlink()
        launcher = fixture.root / "stdlib-launcher"
        venv.EnvBuilder(with_pip=False, symlinks=os.name != "nt").create(launcher)
        launcher_python = launcher / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
        self.assertEqual(subprocess.check_output([str(launcher_python), "-I", "-c",
            "import importlib.util;print(importlib.util.find_spec('UnityPy') is None)"], text=True).strip(), "True")
        abi = dependencies.python_abi()
        runtime = output / "tool-cache" / ("build-python-" + storage.value_hash(abi)[:16])
        venv.EnvBuilder(with_pip=False, symlinks=os.name != "nt").create(runtime)
        runtime_python = runtime / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
        packages = Path(subprocess.check_output([str(runtime_python), "-I", "-c",
            "import sysconfig;print(sysconfig.get_path('purelib'))"], text=True).strip())
        (packages / "yaml.py").write_text("fixture_dependency = 'private YAML fixture'\n")
        (packages / "UnityPy.py").write_text('''from pathlib import Path
import json,sys,types
def load(path):
    print('QUEST_FIXTURE_NATIVE=' + json.dumps(dict(executable=sys.executable,
        utf8=sys.flags.utf8_mode, noBytecode=sys.dont_write_bytecode)), flush=True)
    raw = Path(path).read_bytes()
    obj = types.SimpleNamespace(path_id=1, assets_file=types.SimpleNamespace(name='original.bundle'),
        type=types.SimpleNamespace(name='Shader'), get_raw_data=lambda: raw,
        read=lambda: types.SimpleNamespace(m_ParsedForm=types.SimpleNamespace(m_Name='fixture shader')))
    return types.SimpleNamespace(objects=[obj])
''')
        storage.write_json(runtime / ".ghvr-build-python.json", {"schema": 1, "owner": "GloomhavenVR builder",
            "requirementsKey": dependencies.requirements_key(requirement_paths), "pythonAbi": abi})
        cli = source / "tools/quest-builder/builder.py"
        original_code = cli.read_text()
        instrumentation = '''
# Nonproprietary subprocess fixture seams: actual CLI and derived stage run.
if __name__ == "__main__":
    import canonical_contracts
    _fixture = json.loads((REPO / "fixture.json").read_text())
    def _fixture_inputs(*a, **k):
        # This executes only inside the handed-off process. A downstream Python
        # tool must inherit the private interpreter, not parent-only site paths.
        child = subprocess.check_output([sys.executable, "-I", "-B", "-X", "utf8", "-c",
            "import json,sys,yaml;print(json.dumps([sys.prefix,yaml.fixture_dependency]))"], text=True)
        print("QUEST_FIXTURE_CHILD=" + child.strip(), flush=True)
        return _fixture["inputs"]
    inspect_inputs = _fixture_inputs
    snapshot_inputs = lambda *a, **k: (REPO, Path(_fixture["game"]))
    def _fixture_select(output, inputs, source, game, recipe, *, qualifications):
        return Path(_fixture["workspace"])
    recovery_resume.select_workspace = _fixture_select
    owned_tmp_source_archive = lambda *a: REPO / "public-tmp-fixture.zip"
    import dependencies
    dependencies.dotnet10 = lambda *a, **k: Path(sys.executable)
    def _fixture_export(arguments, log):
        if _fixture["retained"]: raise AssertionError("closed raw export was replayed")
        print("QUEST_FIXTURE_EXPORT=" + arguments[0], flush=True)
        write_json(Path(_fixture["workspace"]) / "full-recovery.json", _fixture["rawResult"])
        return "qualified external exporter fixture"
    command = _fixture_export
    _canonical, _catalogs, *_ = full_assets._modules()
    _catalogs.associate = lambda *a: dict(catalogSha256=digest(Path(_fixture["game"]) / "StreamingAssets/aa/catalog.json"), entries=[])
    canonical_contracts.CONTRACTS = [dict(collection="original.bundle", pathId=1, name="fixture shader",
        canonicalGuid="b" * 32, canonicalPath="Assets/Shader/Fixture.shader", originalObjectSha256=_fixture["nativeHash"])]
    def _fixture_stop_after_copy(*a, **k):
        raise BuildError("fixture stop after real canonical witness and retained copy")
    _canonical.apply = _fixture_stop_after_copy

'''
        # Only declared game/native/external-tool witnesses are replaced. The
        # production stage, dependency setup, helper imports and writer are exact.
        cli.write_text(original_code.replace('if __name__ == "__main__":\n    raise SystemExit(main())',
            instrumentation + 'if __name__ == "__main__":\n    raise SystemExit(main())'))
        # Canonical contracts are imported only by the instrumented CLI, after
        # the exact production helper preflight has registered their directory.
        cli.write_text(cli.read_text().replace("    import canonical_contracts\n    _fixture", "    full_assets._modules()\n    import canonical_contracts\n    _fixture"))
        result = subprocess.run([str(launcher_python), "-I", "-B", "-X", "utf8", str(cli), "prepare",
            "--repo-root", str(source), "--game-root", str(original), "--output-root", str(output),
            "--target", "game", "--dotnet", str(launcher_python)], text=True, capture_output=True,
            env={**os.environ, "GHVRQ_WIZARD_PROGRESS": "1"}, timeout=60)
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("fixture stop after real canonical witness and retained copy", result.stderr)
        native = [json.loads(line.split("=", 1)[1]) for line in result.stdout.splitlines() if line.startswith("QUEST_FIXTURE_NATIVE=")]
        self.assertEqual(len(native), 1, result.stdout + result.stderr)
        self.assertEqual(Path(native[0]["executable"]).absolute(), runtime_python.absolute())
        children = [json.loads(line.split("=", 1)[1]) for line in result.stdout.splitlines() if line.startswith("QUEST_FIXTURE_CHILD=")]
        self.assertEqual(children, [[str(runtime), "private YAML fixture"]])
        self.assertEqual((native[0]["utf8"], native[0]["noBytecode"]), (1, True))
        self.assertEqual(result.stdout.count("QUEST_FIXTURE_EXPORT="), 0 if retained else 1)
        if not retained: self.assertIn("QUEST_FIXTURE_EXPORT=" + str(runtime_python), result.stdout)
        self.assertEqual(fixture.asset.read_bytes(), b"actual retained raw asset")
        staged = list((output / "cache/recovery").glob("*/project/Assets/native.asset"))
        self.assertEqual(len(staged), 1)
        self.assertEqual(staged[0].read_bytes(), fixture.asset.read_bytes())
        self.assertFalse((output / ".builder.lock").exists())
        failure = json.loads((output / "last-failure.json").read_text())
        self.assertEqual(failure["message"], "fixture stop after real canonical witness and retained copy")
        self.assertEqual(result.stdout.count('"phase":"builder-python-handoff"'), 2)

    def test_retained_raw_cli_stages_real_bytes_with_private_native_dependencies(self):
        self.run_cli(retained=True)

    def test_cold_raw_cli_and_later_python_children_use_the_same_private_dependencies(self):
        self.run_cli(retained=False)


if __name__ == "__main__": unittest.main()
