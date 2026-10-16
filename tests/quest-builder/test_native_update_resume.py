"""Completed native Players survive host-only parser repairs without Unity.

Tiny APKs exercise real native-pair receipts, assembly/callback/sidecar gates.
The serialized roster decoder is injected; actual UnityPy roster admission has
its own fixture. No original game content or Android execution is involved.
"""
import copy
import hashlib
import json
from pathlib import Path
import struct
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import apk_update as core
import native_update_resume as resume
import storage
import update_driver as driver


class NativeUpdateResumeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name); self.output = self.folder / "build"
        self.source = self.folder / "source"; self.project = self.output / "updates/code-projects" / ("6" * 64)
        self.base = self.folder / "base.apk"
        self.builder = SimpleNamespace(apk_updates=core, BuildError=storage.BuildError, value_hash=storage.value_hash,
            _ordinary_owned=storage._ordinary_owned, digest=storage.digest, write_json=storage.write_json,
            Stages=storage.Stages, ORIGINAL_UGUI_SHA256="3" * 64, UGUI_LAYOUT_SOURCE_SHA256="4" * 64,
            build_progress=SimpleNamespace(operation=Mock(), event=Mock()))
        storage.write_json(self.source / "unity/GloomhavenVR.Quest/Packages/manifest.json", {"dependencies": {}})
        for name in ("unity/GloomhavenVR.Quest/Assets/Quest/Runtime/Fixture.cs",
                     "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs",
                     "src/GloomhavenVR/Core/Loc/QuestText.cs"):
            path = self.source / name; path.parent.mkdir(parents=True, exist_ok=True); path.write_text("// " + name)
        self.row = {"assembly": "GH.Runtime.dll", "namespace": "", "class": "OriginalWidget",
                    "propertiesHash": "1" * 32, "executionOrder": 0}
        managed = [{"path": "Managed/" + name, "size": 1, "sha256": "7" * 64} for name in ("GH.Runtime.dll", "GH.Shared.dll")]
        self.inputs = {"game": {"key": "b" * 64, "unityVersion": "2021.3.5f1", "files": managed},
            "profile": {"schema": 1, "displayName": "Fixture owner", "provider": "steam", "steamId": "0"},
            "mod": {"modBuild": 673, "key": "c" * 64, "files": [storage.record_file(path, path.relative_to(self.source).as_posix())
                    for path in sorted(self.source.rglob("*")) if path.is_file()]}}
        self.abi = driver.package_fingerprint(self.builder, self.source, self.inputs)
        capsule = {"schema": 1, "scope": "quest-code-update-v1", "inputKey": "a" * 64,
            "unityVersion": "2021.3.5f1", "packageAbi": self.abi, "metadataVersion": 29,
            "managedKey": storage.value_hash(managed), "scriptTypes": [self.row], "modSchema": 1, "dynamicProfile": True}
        lib = bytearray(64); lib[:6] = b"\x7fELF\x02\x01"; struct.pack_into("<H", lib, 18, 183)
        self.values = {core.INPUT: storage.canonical({"schema": 1, "target": "game", "inputKey": "a" * 64,
                    "game": {"files": managed}, "mod": {"modBuild": 672}}),
            core.INSTALLATION: storage.canonical({"schema": 1, "inputKey": "a" * 64}),
            core.CAPSULE: storage.canonical(capsule), core.LIBRARY: bytes(lib),
            core.METADATA: struct.pack("<II", 0xFAB11BAF, 29),
            core.ASSEMBLIES: storage.canonical({"names": ["GH.Runtime.dll"], "types": [16]}),
            core.INITIALIZERS: storage.canonical({"root": []}),
            core.MANAGED_RESOURCES + "original-resources.dat": b"original managed resources"}
        self.write_zip(self.base, self.values)
        prior = {"schema": 1, "mode": "update-mod", "baseApk": str(self.base), "sourcePackage": None,
            "baseApkSha256": storage.digest(self.base), "gameInputKey": "a" * 64,
            "inputs": copy.deepcopy(self.inputs), "updateStrategy": "compact-code", "codeChanged": True,
            "artChanged": False, "nativeHelperChanged": False, "rebuildReasons": [],
            "updateRecipe": dict(resume._LEGACY_HOST)}
        self.prior_root, self.prior = self.write_plan(prior)
        current = copy.deepcopy(prior)
        current["nativeCompilerRecipe"] = resume.compiler_recipe(self.builder, driver.__file__)
        current["inputs"]["mod"]["files"].extend([
            {"path": "tools/quest-builder/apk_update.py", "size": 10, "sha256": "d" * 64},
            {"path": "tools/quest-builder/update_driver.py", "size": 20, "sha256": "e" * 64},
            {"path": "tools/quest-builder/support.py", "size": 30, "sha256": "f" * 64},
            {"path": "tools/quest-wizard/update_failure_guidance.py", "size": 40, "sha256": "0" * 64},
            {"path": "quest-builder-release.json", "size": 50, "sha256": "1" * 64}])
        current["updateRecipe"]["apk_update.py"] = "d" * 64
        current["updateRecipe"]["update_driver.py"] = "e" * 64
        self.root, self.current = self.write_plan(current)
        self.inputs = self.current["inputs"]
        # The legacy failed collector has no outer stage receipt. The compiler's
        # own matched native receipt and ready project owner are still present.
        self.compiled = self.prior_root / "code-player.apk"
        self.receipt = Path(str(self.compiled) + ".code-build.json")
        values = dict(self.values)
        values[core.INITIALIZERS] = storage.canonical({"root": [{"assemblyName": "QuestGame.Campaign",
            "nameSpace": "GloomhavenVR.Quest", "className": "QuestOfflineProfile", "methodName": "Initialize",
            "loadTypes": 1, "isUnityClass": False}]})
        self.write_zip(self.compiled, values)
        self.native_receipt = {"schema": 1, "scope": "quest-code-update-v1", "buildResult": "Succeeded",
            "originalAssetsImported": False, "unityVersion": "2021.3.5f1", "packageAbi": self.abi,
            "abi": "arm64-v8a", "files": [{"path": name, "size": len(values[name]),
                    "sha256": hashlib.sha256(values[name]).hexdigest()} for name in (core.LIBRARY, core.METADATA)]}
        storage.write_json(self.receipt, self.native_receipt)
        producers = [*sorted((self.source / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime").glob("*.cs")),
                      self.source / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCodeUpdateBuild.cs",
                      self.source / "src/GloomhavenVR/Core/Loc/QuestText.cs"]
        self.owner = {"schema": 1, "gameInputKey": "a" * 64, "codeKey": self.prior_root.name,
            "sourceKey": storage.value_hash([{"path": p.relative_to(self.source).as_posix(), "sha256": storage.digest(p)} for p in producers]),
            "profileKey": storage.value_hash(self.inputs["profile"]), "packageAbi": self.abi, "versionCode": 673,
            "versionName": "0.1.0.B673." + self.prior_root.name[:12], "packagesKey": storage.value_hash(None)}
        self.marker = self.project / "QuestCodeUpdate/code-project-owner.json"
        storage.write_json(self.marker, {"state": "ready", "owner": self.owner})
        self.roster = patch.object(core, "script_roster", return_value={"unityVersion": "2021.3.5f1", "scriptTypes": [self.row]})
        self.roster.start(); self.addCleanup(self.roster.stop)
        self.helper = patch.object(driver, "_native_resume", return_value=resume)
        self.helper.start(); self.addCleanup(self.helper.stop)

    @staticmethod
    def write_zip(path, values):
        path.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED) as stream:
            for name, value in values.items(): stream.writestr(name, value)

    def write_plan(self, value):
        value = copy.deepcopy(value)
        key = storage.value_hash({name: body for name, body in value.items() if name not in ("baseApk", "sourcePackage", "updateKey")})
        value["updateKey"] = key
        root = self.output / "updates" / key; storage.write_json(root / "plan.json", value)
        return root, value

    def rewrite_current(self, mutate):
        current = copy.deepcopy(self.current); mutate(current)
        self.root, self.current = self.write_plan(current); self.inputs = self.current["inputs"]

    def compile(self):
        return driver._compile_code(self.builder, SimpleNamespace(), self.inputs, self.output, self.source,
                                    self.folder / "game", self.project, self.base, {}, self.root)

    def test_legacy_successful_pair_survives_parser_and_support_repairs_without_any_compiler(self):
        original = self.compiled.read_bytes(), self.receipt.read_bytes()
        self.assertFalse(storage.Stages(self.output).valid("update-code", self.prior_root.name))
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("No weaving, SDK or native compiler")):
            result = self.compile()
        self.assertTrue(result["nativePairQualified"])
        self.assertEqual(original, (self.compiled.read_bytes(), self.receipt.read_bytes()))
        receipt = storage.Stages(self.output).valid("update-code", self.root.name)
        self.assertEqual({row["path"] for row in receipt["outputs"]}, {
            self.compiled.relative_to(self.output).as_posix(), self.receipt.relative_to(self.output).as_posix()})
        self.assertFalse((self.root / "code-player.apk").exists())
        # The next resume follows the closed output scope, not an absent copy.
        self.marker.unlink()
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Already closed")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_post_collection_rebuild_overlay_does_not_invalidate_original_plan(self):
        prior = copy.deepcopy(self.prior); prior["rebuildDecisionRequired"] = True
        prior["rebuildReasons"] = ["historical script parser conflict"]
        storage.write_json(self.prior_root / "plan.json", prior)
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Keep the successful Player")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_real_input_changes_use_the_incremental_compiler_instead_of_old_pair(self):
        mutations = {
            "game": lambda p: p["inputs"]["game"].update(key="9" * 64),
            "base": lambda p: p.update(baseApkSha256="9" * 64),
            "profile": lambda p: p["inputs"]["profile"].update(displayName="Different owner"),
            "version": lambda p: p["inputs"]["mod"].update(modBuild=674),
            "runtime": lambda p: p["inputs"]["mod"]["files"].append({"path": "src/New.cs", "size": 5, "sha256": "9" * 64}),
            "weaver": lambda p: p["inputs"]["mod"]["files"].append({"path": "tools/QuestWeaver/New.cs", "size": 5, "sha256": "9" * 64}),
            "native": lambda p: p.update(nativeHelperChanged=True),
            "art": lambda p: p.update(artChanged=True),
            "settings": lambda p: p["inputs"]["mod"]["files"].append({"path": "unity/GloomhavenVR.Quest/ProjectSettings/Settings.asset", "size": 5, "sha256": "9" * 64}),
        }
        original = copy.deepcopy(self.current)
        for name, mutate in mutations.items():
            with self.subTest(name=name):
                self.current = copy.deepcopy(original); self.rewrite_current(mutate)
                with patch.object(driver, "_build_code_player", side_effect=storage.BuildError("ordinary incremental compiler")) as build:
                    with self.assertRaisesRegex(storage.BuildError, "ordinary incremental compiler"): self.compile()
                self.assertEqual(build.call_count, 1)
        self.assertTrue(self.compiled.exists()); self.assertTrue(self.receipt.exists())

    def test_unknown_legacy_host_recipe_is_not_adopted(self):
        prior = copy.deepcopy(self.prior); prior["updateRecipe"]["apk_update.py"] = "9" * 64
        self.prior_root, self.prior = self.write_plan(prior)
        # Point to a genuine alternate plan with the same complete native files.
        alternate = self.prior_root / "code-player.apk"; alternate.write_bytes(self.compiled.read_bytes())
        Path(str(alternate) + ".code-build.json").write_bytes(self.receipt.read_bytes())
        self.owner["codeKey"] = self.prior_root.name; self.owner["versionName"] = "0.1.0.B673." + self.prior_root.name[:12]
        storage.write_json(self.marker, {"state": "ready", "owner": self.owner})
        with patch.object(driver, "_build_code_player", side_effect=storage.BuildError("ordinary incremental compiler")):
            with self.assertRaisesRegex(storage.BuildError, "ordinary incremental compiler"): self.compile()

    def test_new_recipe_reuses_future_parser_only_repair_without_legacy_hash_alias(self):
        prior = copy.deepcopy(self.prior)
        prior["nativeCompilerRecipe"] = self.current["nativeCompilerRecipe"]
        prior["updateRecipe"] = {"apk_update.py": "8" * 64, "update_driver.py": "9" * 64}
        previous_root = self.prior_root
        self.prior_root, self.prior = self.write_plan(prior)
        for name in ("code-player.apk", "code-player.apk.code-build.json"):
            (self.prior_root / name).write_bytes((previous_root / name).read_bytes())
        self.owner["codeKey"] = self.prior_root.name
        self.owner["versionName"] = "0.1.0.B673." + self.prior_root.name[:12]
        storage.write_json(self.marker, {"state": "ready", "owner": self.owner})
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Matching native production recipe")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_changed_actual_compile_recipe_cannot_use_legacy_bridge(self):
        changed = "9" * 64
        self.rewrite_current(lambda plan: plan.update(nativeCompilerRecipe=changed))
        with patch.object(resume, "compiler_recipe", return_value=changed), \
             patch.object(driver, "_build_code_player", side_effect=storage.BuildError("ordinary incremental compiler")):
            with self.assertRaisesRegex(storage.BuildError, "ordinary incremental compiler"): self.compile()

    def test_linked_output_cannot_be_adopted(self):
        original = self.folder / "unowned-player.apk"
        self.compiled.rename(original); self.compiled.symlink_to(original)
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Linked output is evidence only")):
            with self.assertRaisesRegex(storage.BuildError, "cannot be links"): self.compile()
        self.assertFalse(storage.Stages(self.output).path("update-code", self.root.name).exists())

    def test_corrupt_candidate_retains_other_valid_outputs_without_scanning(self):
        self.receipt.unlink(); before = self.compiled.read_bytes()
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Do not discard completed APK")):
            with self.assertRaisesRegex(storage.BuildError, "Player is incomplete"): self.compile()
        self.assertEqual(self.compiled.read_bytes(), before)
        self.assertFalse(self.receipt.exists())

    def test_closed_stage_outside_declared_native_output_scope_is_rejected(self):
        wrong = self.output / "other/code-player.apk"; wrong.parent.mkdir(parents=True); wrong.write_bytes(b"unqualified")
        sidecar = Path(str(wrong) + ".code-build.json"); sidecar.write_text("{}")
        storage.Stages(self.output).run("update-code", self.root.name, lambda: ([wrong, sidecar], {}))
        with patch.object(core, "collect_code_update", side_effect=AssertionError("Scope must close before APK reads")), \
             patch.object(driver, "_build_code_player", side_effect=AssertionError("Wrong scope is not a rebuild request")):
            with self.assertRaisesRegex(storage.BuildError, "unexpected output scope"): self.compile()

    def test_tampered_prior_plan_cannot_adopt_native_pair(self):
        prior = copy.deepcopy(self.prior); prior["baseApkSha256"] = "9" * 64
        storage.write_json(self.prior_root / "plan.json", prior)
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Do not overwrite evidence")):
            with self.assertRaisesRegex(storage.BuildError, "plan changed"): self.compile()
        self.assertFalse(storage.Stages(self.output).path("update-code", self.root.name).exists())

    def test_wrong_owner_abi_or_captured_source_cannot_be_adopted(self):
        for name, value in (("packageAbi", "9" * 64), ("sourceKey", "9" * 64), ("profileKey", "9" * 64), ("versionCode", 674)):
            with self.subTest(name=name):
                owner = dict(self.owner); owner[name] = value
                storage.write_json(self.marker, {"state": "ready", "owner": owner})
                with patch.object(driver, "_build_code_player", side_effect=AssertionError("No evidence overwrite")):
                    with self.assertRaisesRegex(storage.BuildError, "owner differs"): self.compile()

    def test_corrupt_native_receipt_is_not_replaced_or_rebuilt(self):
        data = copy.deepcopy(self.native_receipt); data["files"][0]["sha256"] = "9" * 64
        storage.write_json(self.receipt, data); before = self.compiled.read_bytes(), self.receipt.read_bytes()
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Do not replace a suspect native pair")):
            with self.assertRaisesRegex(storage.BuildError, "differs from its closed compiler receipt"): self.compile()
        self.assertEqual(before, (self.compiled.read_bytes(), self.receipt.read_bytes()))
        self.assertFalse(storage.Stages(self.output).path("update-code", self.root.name).exists())

    def test_current_gates_still_reject_real_script_layout_or_callback_failure(self):
        with patch.object(core, "script_roster", return_value={"unityVersion": "2021.3.5f1", "scriptTypes": []}), \
             patch.object(driver, "_build_code_player", side_effect=AssertionError("Do not rebuild silently")):
            with self.assertRaisesRegex(core.RebuildRequired, "changes or removes a serialized script"): self.compile()
        values = dict(self.values); self.write_zip(self.compiled, values)
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Do not rebuild silently")):
            with self.assertRaisesRegex(storage.BuildError, "offline-profile startup boundary"): self.compile()
        self.assertFalse(storage.Stages(self.output).path("update-code", self.root.name).exists())

    def test_no_folder_search_and_no_adoption_of_unowned_player(self):
        self.marker.unlink()
        with patch.object(driver, "_build_code_player", side_effect=storage.BuildError("ordinary incremental compiler")):
            with self.assertRaisesRegex(storage.BuildError, "ordinary incremental compiler"): self.compile()
        self.assertTrue(self.compiled.exists())

    def test_new_production_receipt_survives_qualification_failure_before_outer_receipt(self):
        self.marker.unlink()
        current_compiled = self.root / "code-player.apk"; current_compiled.write_bytes(self.compiled.read_bytes())
        current_receipt = Path(str(current_compiled) + ".code-build.json"); current_receipt.write_bytes(self.receipt.read_bytes())
        def produce(*args): return [current_compiled, current_receipt], {"nativePlayerProduced": True}
        with patch.object(driver, "_build_code_player", side_effect=produce) as build, \
             patch.object(core, "collect_code_update", side_effect=storage.BuildError("controlled parser repair required")):
            with self.assertRaisesRegex(storage.BuildError, "controlled parser"): self.compile()
        self.assertEqual(build.call_count, 1)
        self.assertTrue(storage.Stages(self.output).valid("update-code-production", self.root.name))
        self.assertFalse(storage.Stages(self.output).path("update-code", self.root.name).exists())
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Production already completed")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_production_stage_cannot_reference_a_different_updates_native_pair(self):
        storage.Stages(self.output).run("update-code-production", self.root.name,
            lambda: ([self.compiled, self.receipt], {"nativePlayerProduced": True}))
        with patch.object(core, "collect_code_update", side_effect=AssertionError("Current production scope first")):
            with self.assertRaisesRegex(storage.BuildError, "unexpected output scope"): self.compile()

    def test_cached_stage_cannot_bypass_current_plan_source_identity(self):
        storage.Stages(self.output).run("update-code", self.root.name,
            lambda: ([self.compiled, self.receipt], {"nativePairQualified": True}))
        different = copy.deepcopy(self.inputs); different["mod"]["modBuild"] = 674
        with patch.object(core, "collect_code_update", side_effect=AssertionError("Current source must qualify first")):
            with self.assertRaisesRegex(storage.BuildError, "actual production inputs or recipe"):
                driver._compile_code(self.builder, SimpleNamespace(), different, self.output, self.source,
                    self.folder / "game", self.project, self.base, {}, self.root)

    def test_legacy_bridge_is_exactly_the_reviewed_current_production_recipe(self):
        self.assertEqual(resume.compiler_recipe(self.builder, driver.__file__), resume._LEGACY_RECIPE)
        self.assertEqual(resume._LEGACY_HOST, {"update_driver.py": "e0756f5738d1f3791197769d15b059c4f166e4576aac57f1f44d71828e65f894",
            "apk_update.py": "0febdc91294da2b64843491fd50dcba4aa6fc8f1882a02310799beaba5e01af3"})


if __name__ == "__main__": unittest.main()
