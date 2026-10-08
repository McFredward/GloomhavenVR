"""Owned original extraction and same-index tiers; no game/Unity build fixture."""
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import types
import unittest
import zipfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import environment_bank as bank
import storage
import builder
import mod_assets


class MeshHandler:
    def __init__(self, mesh): self.mesh = mesh
    def process(self):
        size = self.mesh.grid
        self.m_Vertices = [(float(x), 0., float(z)) for z in range(size + 1) for x in range(size + 1)]
        self.m_Normals = [(0., 1., 0.)] * len(self.m_Vertices)
        self.m_Tangents = self.m_Colors = []
        for index in range(8): setattr(self, "m_UV" + str(index), [])
    def get_triangles(self):
        width = self.mesh.grid + 1; triangles = []
        for z in range(self.mesh.grid):
            for x in range(self.mesh.grid):
                a = z * width + x; triangles.extend([(a, a + width, a + 1), (a + 1, a + width, a + width + 1)])
        return [triangles]


class OwnedEnvironmentTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(); self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name); self.source = self.root / "source"; self.game = self.root / "owned-game"
        self.authored = self.root / "compiler-project"; self.output = self.root / "build"
        self.calls = []; self.simplifications = []; self.fail_bundle = None; self.fail_tier = None
        for relative in bank.PRODUCERS:
            target = self.source / relative; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(ROOT / relative, target)
        for index in range(2):
            path = self.game / bank.PCG_ROOT / ("fixture-" + str(index) + ".bundle")
            path.parent.mkdir(parents=True, exist_ok=True); path.write_text(json.dumps({"name": "floor_fixture_" + str(index), "grid": 18}))
        self.refresh()
        self.original_source = storage.inventory(self.source); self.original_game = storage.inventory(self.game)
        self.helper = bank._helper

    def refresh(self):
        self.source_files = storage.inventory(self.source)
        game_files = storage.inventory(self.game)
        self.game_info = {"key": storage.value_hash(game_files), "files": game_files}

    def load(self, raw, **kwargs):
        data = json.loads(raw); self.calls.append(data["name"])
        if data["name"] == self.fail_bundle: raise RuntimeError("native fixture interruption")
        n = data["grid"]; vector = lambda x, y, z: types.SimpleNamespace(x=x, y=y, z=z)
        mesh = types.SimpleNamespace(m_Name=data["name"], m_IsReadable=True, grid=n,
            m_LocalAABB=types.SimpleNamespace(m_Center=vector(n / 2, 0, n / 2), m_Extent=vector(n / 2, 0, n / 2)),
            m_VertexData=types.SimpleNamespace(m_VertexCount=(n + 1) ** 2),
            m_SubMeshes=[types.SimpleNamespace(indexCount=n * n * 6, topology=0)], m_BindPose=[], m_Shapes=types.SimpleNamespace(channels=[]))
        assets_file = object()
        obj = types.SimpleNamespace(type=types.SimpleNamespace(name="Mesh"), path_id=200, assets_file=assets_file, read=lambda: mesh)
        filter = types.SimpleNamespace(type=types.SimpleNamespace(name="MeshFilter"), read=lambda: types.SimpleNamespace(m_Mesh=types.SimpleNamespace(path_id=200)))
        use = {"route": ["floor_template", data["name"]], "components": ["Transform", "MeshFilter", "MeshRenderer"],
               "leafComponents": ["Transform", "MeshFilter", "MeshRenderer"], "scripts": data.get("scripts", []), "unresolved": False}
        return types.SimpleNamespace(objects=[obj, filter], original_uses={(id(assets_file), 200): [use]})

    def helpers(self, path, function):
        module = self.helper(path, function)
        if function == "extract_bundle":
            module.UnityPy = types.SimpleNamespace(load=self.load, __version__="nonproprietary native fixture")
            module.MeshHandler = MeshHandler
            module.original_uses = lambda environment: environment.original_uses
        else:
            simplify = module.simplify
            def observed(data, tier, role="none"):
                self.simplifications.append(tier)
                if self.fail_tier == tier: raise RuntimeError("derivative fixture interruption")
                return simplify(data, tier, role)
            module.simplify = observed
        return module

    def stage(self, authored=None):
        with patch.object(bank, "_helper", side_effect=self.helpers):
            return bank.stage(self.source, self.game, authored or self.authored, self.output, self.game_info, self.source_files)

    def test_actual_original_streams_and_same_index_tiers_are_source_bound_and_read_only(self):
        generated = self.stage()
        self.assertEqual(self.calls, ["floor_fixture_0", "floor_fixture_1"])
        self.assertEqual(self.simplifications, [50, 0, 50, 0])
        files = bank.validated_records(self.authored, generated)
        index = json.loads((self.authored / bank.ROOT / "index.json").read_text())
        self.assertEqual(len(index["entries"]), 2)
        for entry in index["entries"]:
            self.assertEqual([v["tier"] for v in entry["variants"]], [100, 50, 0])
            for variant in entry["variants"]:
                data = (self.authored / bank.ROOT / variant["file"]).read_bytes()
                self.assertEqual(data[:5], b"GHEM1")
                self.assertEqual(storage.digest(self.authored / bank.ROOT / variant["file"]), variant["sha256"])
        self.assertEqual(len(files), 16)
        self.assertEqual(storage.inventory(self.source), self.original_source)
        self.assertEqual(storage.inventory(self.game), self.original_game)

    def test_bundle_failure_reuses_completed_native_bundle_without_reextracting_it(self):
        self.fail_bundle = "floor_fixture_1"
        with self.assertRaisesRegex(RuntimeError, "native fixture interruption"): self.stage()
        self.calls.clear(); self.fail_bundle = None
        self.stage()
        self.assertEqual(self.calls, ["floor_fixture_1"])
        self.assertEqual(storage.inventory(self.game), self.original_game)

    def test_partial_derivative_failure_retains_each_completed_tier(self):
        self.fail_tier = 0
        with self.assertRaisesRegex(RuntimeError, "derivative fixture interruption"): self.stage()
        self.calls.clear(); self.simplifications.clear(); self.fail_tier = None
        self.stage()
        self.assertEqual(self.calls, [])
        self.assertEqual(self.simplifications, [0, 50, 0])

    def test_unrelated_mod_changes_reuse_every_native_and_derivative_producer(self):
        original = self.stage(); self.calls.clear(); self.simplifications.clear()
        (self.source / "FutureMod.cs").write_text("ordinary future mod source")
        self.refresh()
        self.assertEqual(self.stage(), original)
        self.assertEqual((self.calls, self.simplifications), ([], []))

    def test_changed_simplifier_reuses_native_bytes_and_recomputes_only_derived_tiers(self):
        self.stage(); self.calls.clear(); self.simplifications.clear()
        generator = self.source / bank.PRODUCERS[1]
        generator.write_text(generator.read_text() + "\n# audited next producer fixture\n")
        self.refresh(); self.stage(self.root / "next-compiler-project")
        self.assertEqual(self.calls, [])
        self.assertEqual(self.simplifications, [50, 0, 50, 0])

    def test_global_census_refuses_safe_mesh_reused_under_protected_other_bundle(self):
        paths = sorted((self.game / bank.PCG_ROOT).glob("*.bundle"))
        paths[0].write_text(json.dumps({"name": "floor_shared", "grid": 18}))
        paths[1].write_text(json.dumps({"name": "floor_shared", "grid": 18, "scripts": ["InventoryController"]}))
        self.refresh(); generated = self.stage()
        index = json.loads((self.authored / bank.ROOT / "index.json").read_text())
        self.assertEqual(len(index["entries"]), 1)
        self.assertEqual(index["entries"][0]["role"], "none")
        self.assertEqual(len(index["entries"][0]["sources"]), 2)
        self.assertFalse(index["entries"][0]["ornament"])
        origins = json.loads((self.authored / bank.ROOT / bank.ORIGINS).read_text())
        self.assertTrue(origins["completePcgCensus"])
        self.assertEqual(len(origins["sources"]), 2)
        self.assertRegex(origins["censusSha256"], bank.HASH)
        bank.validated_records(self.authored, generated)

    def test_changed_cross_bundle_ancestry_does_not_reuse_prior_safe_role_derivative(self):
        paths = sorted((self.game / bank.PCG_ROOT).glob("*.bundle"))
        for path in paths: path.write_text(json.dumps({"name": "floor_shared", "grid": 18}))
        self.refresh(); self.stage()
        original = json.loads((self.authored / bank.ROOT / "index.json").read_text())["entries"][0]
        self.assertEqual(original["role"], "floor")
        self.calls.clear(); self.simplifications.clear()
        paths[1].write_text(json.dumps({"name": "floor_shared", "grid": 18, "scripts": ["InventoryController"]}))
        self.refresh(); target = self.root / "protected-census-project"
        self.stage(target)
        changed = json.loads((target / bank.ROOT / "index.json").read_text())["entries"][0]
        self.assertEqual(changed["role"], "none")
        self.assertNotEqual(changed["roleEvidenceSha256"], original["roleEvidenceSha256"])
        self.assertEqual(self.calls, ["floor_shared"])
        self.assertEqual(self.simplifications, [50, 0])

    def test_geometry_dependency_change_reuses_native_but_runs_new_derived_producer(self):
        self.stage(); self.calls.clear(); self.simplifications.clear()
        geometry = self.source / bank.PRODUCERS[3]
        # The actual import must resolve the new snapshot's helper, rather than
        # the module cached by the earlier source's helper. This visible producer
        # returns only exact native streams, without pretending they are reduced.
        geometry.write_text(geometry.read_text() + "\n\ndef simplify(data, tier, role='none'):\n    return None, None\n")
        self.refresh(); target = self.root / "changed-geometry-project"
        self.stage(target)
        self.assertEqual(self.calls, [])
        self.assertEqual(self.simplifications, [50, 0, 50, 0])
        index = json.loads((target / bank.ROOT / "index.json").read_text())
        for entry in index["entries"]: self.assertEqual([row["tier"] for row in entry["variants"]], [100])

    def test_role_dependency_change_is_a_distinct_source_census_producer(self):
        self.stage(); self.calls.clear(); self.simplifications.clear()
        roles = self.source / bank.PRODUCERS[2]
        roles.write_text(roles.read_text() + "\n\ndef classify(name, uses):\n    return 'none', ['changed-role-contract']\n")
        self.refresh(); target = self.root / "changed-role-project"
        self.stage(target)
        self.assertEqual(self.calls, ["floor_fixture_0", "floor_fixture_1"])
        index = json.loads((target / bank.ROOT / "index.json").read_text())
        for entry in index["entries"]: self.assertEqual(entry["role"], "none")

    def test_corrupt_completed_native_bytes_fail_visibly_without_reexport_or_source_write(self):
        self.stage(); self.calls.clear()
        native = next((self.output / "cache/environment-native").glob("*/*.bytes")); native.write_bytes(b"changed")
        with self.assertRaisesRegex(storage.BuildError, "Completed environment source output changed"): self.stage()
        self.assertEqual(self.calls, [])
        self.assertEqual(storage.inventory(self.game), self.original_game)

    def test_changed_owned_source_bytes_and_missing_producer_api_are_explicit(self):
        source = self.game / self.game_info["files"][0]["path"]
        raw = source.read_bytes(); source.write_bytes(b"X" * len(raw))
        with self.assertRaisesRegex(ValueError, "changed before extraction"): self.stage()
        self.assertFalse((self.authored / bank.ROOT / "index.json").exists())
        source.write_bytes(raw)
        (self.source / bank.PRODUCERS[1]).write_text("# drifted producer without prepare_mesh API\n")
        self.refresh()
        with self.assertRaisesRegex(storage.BuildError, "producer API changed"): self.stage()

    def test_generated_compiler_input_and_original_index_binding_are_not_optional(self):
        generated = self.stage()
        controls = [dict(generated, association="unknown producer"), dict(generated, gameKey="invalid"),
                    dict(generated, files=generated["files"] + [generated["files"][0]])]
        for changed in controls:
            with self.subTest(control=changed["association"]), self.assertRaises(storage.BuildError): bank.validated_records(self.authored, changed)
        index = self.authored / bank.ROOT / "index.json"; index.write_text("{}")
        with self.assertRaisesRegex(storage.BuildError, "compiler input changed"): bank.validated_records(self.authored, generated)

    def test_owned_cli_process_uses_explicit_paths_and_protects_originals(self):
        self.stage()
        native = self.root / "cli-work/native"; native.mkdir(parents=True)
        extracted = []
        for receipt_path in (self.output / "cache/environment-native").glob("*/complete.json"):
            receipt = json.loads(receipt_path.read_text())
            extracted.extend(receipt["extracted"]["meshes"])
            for row in receipt["files"]: shutil.copyfile(receipt_path.parent / row["path"], native / row["path"])
        storage.write_json(native / "sources.json", {"format": 1, "unitypy": "fixture", "ambiguousRejected": [], "meshes": extracted, "catalog": [], "completePcgCensus": True})
        target = self.root / "cli-prepared"
        base = [sys.executable, "-I", "-B", "-X", "utf8", str(self.source / bank.PRODUCERS[1]),
                "--source-root", str(self.source), "--game-data", str(self.game), "--skip-extract"]
        result = subprocess.run(base + ["--output-dir", str(native.parent), "--prepared-dir", str(target)],
                                capture_output=True, text=True, timeout=20)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(len(json.loads((target / "index.json").read_text())["entries"]), 2)
        unsafe = self.game / "must-not-be-created"
        result = subprocess.run(base + ["--output-dir", str(unsafe), "--prepared-dir", str(target)],
                                capture_output=True, text=True, timeout=20)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must not write", result.stderr)
        self.assertFalse(unsafe.exists())
        result = subprocess.run([sys.executable, "-I", "-B", str(self.source / bank.PRODUCERS[0]),
                                 "--source-root", str(self.source), "--game-data", str(self.game), "--output-dir", str(unsafe)],
                                capture_output=True, text=True, timeout=20)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must not write", result.stderr)
        self.assertFalse(unsafe.exists())
        self.assertEqual(storage.inventory(self.source), self.original_source)
        self.assertEqual(storage.inventory(self.game), self.original_game)

    def test_actual_package_retry_keeps_library_and_producers_then_delivers_separate_environment_bank(self):
        # This boundary fixture stands in for Unity compilation only. It executes
        # the actual immutable source snapshot, producers, qualification and ZIP.
        prefix = "unity/GloomhavenVR.Assets/"
        authored_source = self.source / prefix
        assets = ["Assets/Editor/QuestModBundles.cs", "Assets/Bundle/Hands/Fixture.prefab"]
        assets += list(mod_assets.REQUIRED_TOWN) + list(mod_assets.ENVIRONMENT_SHADERS)
        assets += [mod_assets.TOWN_ROOT + "Audio/fixture.wav", mod_assets.TOWN_ROOT + "Audio/fixture.json"]
        for relative in assets:
            target = authored_source / relative; target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("controlled immutable compiler input:" + relative)
            target.with_name(target.name + ".meta").write_text("controlled source GUID:" + relative)
        self.refresh()
        game_snapshot = self.output / "inputs/game" / self.game_info["key"]
        shutil.copytree(self.game, game_snapshot)
        inputs = {"target": "game", "inputKey": "9" * 64, "game": self.game_info, "mod": {"files": self.source_files}}
        editor = self.root / "controlled-unity"; editor.write_text("not a Unity SDK")
        project = self.root / "player-project"; attempts = []
        names = [mod_assets.MAIN, mod_assets.TOWN, mod_assets.VOICES, mod_assets.ENVIRONMENT]
        def compile_fixture(argv, log, *, env):
            authored = Path(argv[argv.index("-projectPath") + 1]); bundles = Path(env["GHVR_QUEST_MOD_BUNDLE_OUTPUT"])
            library = authored / "Library/imported.cache"
            attempts.append(authored)
            if len(attempts) == 1:
                library.parent.mkdir(parents=True); library.write_bytes(b"retained native import cache")
                bundles.mkdir(parents=True); (bundles / "unqualified.bundle").write_bytes(b"partial output")
                raise storage.BuildError("interrupted native compiler fixture")
            self.assertEqual(library.read_bytes(), b"retained native import cache")
            self.assertFalse(bundles.exists())
            self.assertEqual((self.calls, self.simplifications), ([], []))
            files = [row for row in storage.inventory(authored) if row["path"].startswith(("Assets/", "Packages/", "ProjectSettings/"))]
            paths = [row["path"] for row in files if not row["path"].endswith(".meta")]
            selected = [
                [path for path in paths if path.startswith("Assets/Bundle/") and not path.startswith((mod_assets.TOWN_ROOT, mod_assets.ENVIRONMENT_ROOT)) and path not in mod_assets.ENVIRONMENT_SHADERS],
                list(mod_assets.REQUIRED_TOWN),
                [path for path in paths if path.startswith(mod_assets.TOWN_ROOT + "Audio/")],
                [path for path in paths if path.startswith(mod_assets.ENVIRONMENT_ROOT) or path in mod_assets.ENVIRONMENT_SHADERS],
            ]
            bundles.mkdir(parents=True); records = []; banks = []
            for name, selection in zip(names, selected):
                target = bundles / name; target.write_bytes(b"UnityFS\0controlled compiler fixture:" + name.encode())
                record = storage.record_file(target, name); records.append(record)
                banks.append({"bundleName": name, "assetNames": selection, "requiredAssetNames": selection, "dependencies": [], "bundle": record})
            receipt = {"schema": 1, "target": "Android", "unityVersion": "2021.3.5f1", "bundleName": mod_assets.MAIN,
                "graphicsApi": "Vulkan", "colorSpace": "Linear", "stereoRenderingPath": "SinglePass",
                "typeTreesEnabled": True, "chunkBasedCompression": True, "townBanksIncluded": True,
                "environmentBankIncluded": True, "environmentVariantsKept": True,
                "assetNames": selected[0], "requiredAssetNames": selected[0], "bundle": records[0], "bundles": records,
                "banks": banks, "sourceFiles": files}
            storage.write_json(bundles / "quest-mod-bundles.json", receipt)
        with patch.object(bank, "_helper", side_effect=self.helpers), patch.object(builder, "_local_helper", return_value=bank), \
                patch.object(builder, "unity_launcher", return_value=["controlled-unity"]), patch.object(builder, "command", side_effect=compile_fixture):
            with self.assertRaisesRegex(storage.BuildError, "interrupted native compiler"):
                builder.package_mod_content(project, inputs, self.output, self.source, editor)
            self.assertEqual(len(self.calls), 2); self.calls.clear(); self.simplifications.clear()
            manifest = builder.package_mod_content(project, inputs, self.output, self.source, editor)
            self.assertEqual(len(attempts), 2)
            self.assertEqual([row["path"] for row in manifest["files"]], ["StreamingAssets/" + name for name in names])
            with zipfile.ZipFile(project / "Assets/StreamingAssets/quest-mod-content.zip") as archive:
                self.assertEqual(archive.namelist(), [row["path"] for row in manifest["files"]])
                for row in manifest["files"]:
                    self.assertEqual(storage.digest(attempts[-1].parent / "bundles" / Path(row["path"]).name), row["sha256"])
            builder.package_mod_content(project, inputs, self.output, self.source, editor)
            self.assertEqual(len(attempts), 2)  # qualified native banks are not compiled twice
        self.assertEqual(storage.inventory(self.game), self.original_game)

    def test_library_recovery_survives_process_death_after_rename(self):
        output = self.output; key = "8" * 64; root = output / "cache/mod-bundle" / key
        with builder._mod_bank_workspace(output, root, key, self.source_files):
            library = root / "project/Library/imported.cache"; library.parent.mkdir(parents=True)
            library.write_bytes(b"retained despite hard death")
        code = ("import sys,os,json;from pathlib import Path;sys.path.insert(0,sys.argv[1]);import builder,storage;"
                "files=json.loads(sys.argv[6]);"
                "context=builder._mod_bank_workspace(Path(sys.argv[2]),Path(sys.argv[3]),sys.argv[4],files);"
                "context.__enter__();copy=storage.shutil.copyfile;"
                "storage.shutil.copyfile=lambda a,b:(copy(a,b),os._exit(77));"
                "storage.snapshot(Path(sys.argv[5]),files,Path(sys.argv[3])/'project')")
        run = subprocess.run([sys.executable, "-I", "-B", "-X", "utf8", "-c", code,
                              str(ROOT / "tools/quest-builder"), str(output), str(root), key,
                              str(self.source), json.dumps(self.source_files)], timeout=20)
        self.assertEqual(run.returncode, 77)
        self.assertFalse((root / "project/Library").exists())
        self.assertEqual((root / "Library/imported.cache").read_bytes(), b"retained despite hard death")
        self.assertTrue((root / "project.staging.json").is_file())
        with builder._mod_bank_workspace(output, root, key, self.source_files):
            storage.snapshot(self.source, self.source_files, root / "project")
        self.assertEqual((root / "project/Library/imported.cache").read_bytes(), b"retained despite hard death")
        self.assertFalse((root / "Library").exists())
        self.assertTrue(storage.verify_files(root / "project", self.source_files))


if __name__ == "__main__": unittest.main()
