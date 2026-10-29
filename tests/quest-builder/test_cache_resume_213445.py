"""A published compact Player retains its completed compiler after host repairs.

Real tiny ZIPs and pair receipts qualify provenance without any game asset scan,
new Unity invocation, SDK work or native compilation. No Android execution occurs.
"""
import copy
import ast
import hashlib
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
sys.path.insert(0, str(ROOT / "tests/quest-builder"))
import apk_update as core
import native_update_resume as resume
import storage
import update_driver as driver
import test_native_update_resume as fixtures


class PublishedNativeResumeTests(unittest.TestCase):
    setUp = fixtures.NativeUpdateResumeTests.setUp
    write_zip = staticmethod(fixtures.NativeUpdateResumeTests.write_zip)
    write_plan = fixtures.NativeUpdateResumeTests.write_plan
    rewrite_current = fixtures.NativeUpdateResumeTests.rewrite_current
    compile = fixtures.NativeUpdateResumeTests.compile

    def publish(self, *, old_recipe=False, changed_helper=True):
        prior = copy.deepcopy(self.prior)
        prior["nativeHelperChanged"] = changed_helper
        self.prior_root, self.prior = self.write_plan(prior)
        # The fixtures keep one exact stable compiler owner, as Windows does.
        old_compiled = self.compiled
        self.compiled = self.prior_root / "code-player.apk"
        self.receipt = Path(str(self.compiled) + ".code-build.json")
        import zipfile
        with zipfile.ZipFile(old_compiled) as apk:
            values = {row.filename: apk.read(row) for row in apk.infolist()}
        # Distinguish completed native code from the original base pair.
        values[core.LIBRARY] = values[core.LIBRARY][:-1] + b"\x01"
        values[core.METADATA] += b"compiled metadata fixture"
        values["assets/bin/Data/unity_app_guid"] = b"00000000-0000-4000-8000-000000000002"
        values["assets/huge-game-bank-fixture.bin"] = b"unrelated original content" * 4096
        self.write_zip(self.compiled, values)
        self.native_receipt["files"] = [{"path": name, "size": len(values[name]),
            "sha256": hashlib.sha256(values[name]).hexdigest()} for name in (core.LIBRARY, core.METADATA)]
        storage.write_json(self.receipt, self.native_receipt)
        self.owner["codeKey"] = self.prior_root.name
        self.owner["versionName"] = "0.1.0.B673." + self.prior_root.name[:12]
        storage.write_json(self.marker, {"state": "ready", "owner": self.owner})
        published = copy.deepcopy(self.current)
        published["nativeHelperChanged"] = changed_helper
        published["nativeCompilerRecipe"] = resume._MEDIA_SCOPE_PREVIOUS if old_recipe else self.current["nativeCompilerRecipe"]
        self.published_root, self.published = self.write_plan(published)
        # INPUT still describes the immutable original conversion. UPDATE carries
        # the source/profile that actually produced the newly published Player.
        original = json.loads(values[core.INPUT])
        original["game"] = copy.deepcopy(self.inputs["game"])
        values[core.INPUT] = storage.canonical(original)
        self.update = {"schema": 1, "mode": "update-mod", "gameInputKey": self.current["gameInputKey"],
            "updateKey": self.published_root.name, "baseApkSha256": self.prior["baseApkSha256"],
            "modBuild": self.inputs["mod"]["modBuild"], "mod": copy.deepcopy(self.published["inputs"]["mod"]),
            "profile": copy.deepcopy(self.inputs["profile"])}
        values[core.UPDATE] = storage.canonical(self.update)
        # Reproduce the actual bad publication: native code/metadata were
        # replaced, but the old extraction identity remained in the base APK.
        values["assets/bin/Data/unity_app_guid"] = self.values["assets/bin/Data/unity_app_guid"]
        self.published_values = values
        self.base = self.folder / "published-B673.apk"
        self.write_zip(self.base, values)
        self.rewrite_current(lambda plan: plan.update(baseApk=str(self.base),
            baseApkSha256=storage.digest(self.base), nativeHelperChanged=False))

    def rewrite_published(self, mutate):
        plan = copy.deepcopy(self.published); mutate(plan)
        self.published_root, self.published = self.write_plan(plan)
        self.update["updateKey"] = self.published_root.name
        self.published_values[core.UPDATE] = storage.canonical(self.update)
        self.write_zip(self.base, self.published_values)
        self.rewrite_current(lambda plan: plan.update(baseApkSha256=storage.digest(self.base)))

    def expect_incremental(self):
        with patch.object(driver, "_build_code_player", side_effect=storage.BuildError("incremental compiler required")) as compiler:
            with self.assertRaisesRegex(storage.BuildError, "incremental compiler required"):
                self.compile()
        self.assertEqual(compiler.call_count, 1)

    def test_published_legacy_player_reuses_pair_without_weaving_sdk_or_native_compiler(self):
        self.publish()
        before = self.compiled.read_bytes(), self.receipt.read_bytes()
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("No compiler after completed publication")):
            result = self.compile()
        self.assertTrue(result["nativePairQualified"])
        self.assertEqual(result["replacements"]["assets/bin/Data/unity_app_guid"],
                         b"00000000-0000-4000-8000-000000000002")
        self.assertEqual(before, (self.compiled.read_bytes(), self.receipt.read_bytes()))
        self.assertFalse((self.root / "code-player.apk").exists())
        self.assertTrue(storage.Stages(self.output).valid("update-code", self.root.name))
        # A closed repair receipt also resumes without the old project marker.
        self.marker.unlink()
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Already qualified")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_current_recipe_published_player_also_reuses(self):
        self.publish(old_recipe=False, changed_helper=False)
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Same current producer")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_same_original_base_keeps_completed_current_pair(self):
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("No publication required")):
            self.assertTrue(self.compile()["nativePairQualified"])

    def test_previous_explicit_recipe_uses_only_reviewed_bridge(self):
        self.publish()
        self.assertNotEqual(resume._PREVIOUS_RECIPE, resume._LEGACY_RECIPE)
        self.assertEqual(self.current["nativeCompilerRecipe"],
                         "d2cd18309f9bd7d58b847015f3521c6b11bfca580604172ae0fe54c3579fa992")
        self.assertFalse(resume._same_recipe({"nativeCompilerRecipe": resume._MEDIA_SCOPE_PREVIOUS},
                                            self.current["nativeCompilerRecipe"]))
        self.assertTrue(resume._same_recipe({"nativeCompilerRecipe": resume._MEDIA_SCOPE_PREVIOUS}, resume._MEDIA_SCOPE_CURRENT))
        self.assertTrue(resume._same_recipe({"nativeCompilerRecipe": resume._PREVIOUS_RECIPE}, resume._LEGACY_RECIPE))
        self.assertFalse(resume._same_recipe({"nativeCompilerRecipe": resume._PREVIOUS_RECIPE}, "9" * 64))
        self.assertFalse(resume._same_recipe({"nativeCompilerRecipe": "9" * 64}, resume._LEGACY_RECIPE))

    def test_published_pre_sky_recipe_requires_the_new_resource_compiler(self):
        self.publish(old_recipe=True)
        self.expect_incremental()

    def test_unsigned_claims_or_foreign_descendant_cannot_adopt(self):
        for name, value in (("mode", "update-profile"), ("baseApkSha256", "9" * 64),
                            ("gameInputKey", "9" * 64), ("modBuild", 674),
                            ("profile", {"displayName": "Other owner"}), ("updateKey", "9" * 64)):
            with self.subTest(name=name):
                self.publish()
                self.update[name] = value
                self.published_values[core.UPDATE] = storage.canonical(self.update)
                self.write_zip(self.base, self.published_values)
                self.rewrite_current(lambda plan: plan.update(baseApkSha256=storage.digest(self.base)))
                if name == "gameInputKey":
                    with patch.object(driver, "_build_code_player", side_effect=AssertionError("Invalid signed game identity")):
                        with self.assertRaisesRegex(storage.BuildError, "Signed APK update provenance is invalid"):
                            self.compile()
                else:
                    self.expect_incremental()

    def test_current_base_pair_must_match_completed_receipt(self):
        for member in (core.LIBRARY, core.METADATA):
            with self.subTest(member=member):
                self.publish()
                body = self.published_values[member]
                self.published_values[member] = body[:-1] + bytes([body[-1] ^ 1])
                self.write_zip(self.base, self.published_values)
                self.rewrite_current(lambda plan: plan.update(baseApkSha256=storage.digest(self.base)))
                self.expect_incremental()

    def test_published_plan_producer_or_original_game_drift_cannot_adopt(self):
        for name, mutate in (
            ("base", lambda p: p.update(baseApkSha256="9" * 64)),
            ("game", lambda p: p["inputs"]["game"].update(key="9" * 64)),
            ("profile", lambda p: p["inputs"]["profile"].update(displayName="Other owner")),
            ("art", lambda p: p.update(artChanged=True)),
            ("helper", lambda p: p.update(nativeHelperChanged=False)),
            ("recipe", lambda p: p.update(nativeCompilerRecipe="9" * 64)),
            ("runtime", lambda p: p["inputs"]["mod"]["files"].append({"path": "src/Other.cs", "size": 1, "sha256": "9" * 64})),
        ):
            with self.subTest(name=name):
                self.publish()
                self.rewrite_published(mutate)
                self.expect_incremental()

    def test_update_source_claim_must_equal_exact_published_plan(self):
        self.publish()
        self.update["mod"]["key"] = "9" * 64
        self.published_values[core.UPDATE] = storage.canonical(self.update)
        self.write_zip(self.base, self.published_values)
        self.rewrite_current(lambda plan: plan.update(baseApkSha256=storage.digest(self.base)))
        self.expect_incremental()

    def test_missing_retained_publication_plan_does_not_search_folders(self):
        self.publish()
        (self.published_root / "plan.json").unlink()
        with patch.object(Path, "glob", side_effect=AssertionError("No historical folder search")), \
             patch.object(Path, "rglob", side_effect=AssertionError("No historical folder search")):
            self.expect_incremental()

    def test_missing_signed_update_does_not_adopt_same_version_native_code(self):
        self.publish()
        self.published_values.pop(core.UPDATE)
        self.write_zip(self.base, self.published_values)
        self.rewrite_current(lambda plan: plan.update(baseApkSha256=storage.digest(self.base)))
        self.expect_incremental()

    def test_current_producer_profile_art_and_abi_drift_remain_rejected(self):
        self.publish()
        original = copy.deepcopy(self.current)
        for name, mutate in (
            ("profile", lambda p: p["inputs"]["profile"].update(displayName="Other owner")),
            ("art", lambda p: p.update(artChanged=True)),
            ("helper", lambda p: p.update(nativeHelperChanged=True)),
            ("runtime", lambda p: p["inputs"]["mod"]["files"].append({"path": "src/Other.cs", "size": 1, "sha256": "9" * 64})),
        ):
            with self.subTest(name=name):
                self.current = copy.deepcopy(original); self.rewrite_current(mutate)
                self.expect_incremental()
        self.current = copy.deepcopy(original); self.inputs = self.current["inputs"]
        self.root = self.output / "updates" / self.current["updateKey"]
        self.owner["packageAbi"] = "9" * 64
        storage.write_json(self.marker, {"state": "ready", "owner": self.owner})
        with patch.object(driver, "_build_code_player", side_effect=AssertionError("Owner drift preserves evidence")):
            with self.assertRaisesRegex(storage.BuildError, "owner differs"):
                self.compile()

    def test_real_current_collector_failure_still_blocks_publication(self):
        self.publish()
        with patch.object(core, "script_roster", return_value={"unityVersion": "2021.3.5f1", "scriptTypes": []}), \
             patch.object(driver, "_build_code_player", side_effect=AssertionError("Keep completed evidence")):
            with self.assertRaisesRegex(core.RebuildRequired, "changes or removes a serialized script"):
                self.compile()
        self.assertFalse(storage.Stages(self.output).valid("update-code", self.root.name))

    def test_published_pair_comparison_reads_no_other_payload(self):
        self.publish()
        import zipfile
        actual_open = zipfile.ZipFile.open
        reads = []
        def bounded(apk, name, *args, **kwargs):
            member = getattr(name, "filename", name); reads.append(member)
            if member.startswith("assets/huge-game-bank"):
                raise AssertionError("No original game bank reads")
            return actual_open(apk, name, *args, **kwargs)
        with patch.object(zipfile.ZipFile, "open", bounded), \
             patch.object(driver, "_build_code_player", side_effect=AssertionError("Use completed pair")):
            self.assertTrue(self.compile()["nativePairQualified"])
        self.assertIn(core.LIBRARY, reads); self.assertIn(core.METADATA, reads)

    def test_excluded_publication_functions_are_outside_native_producer_call_graph(self):
        tree = ast.parse(Path(core.__file__).read_text(encoding="utf-8"))
        functions = {node.name: node for node in tree.body if isinstance(node, ast.FunctionDef)}
        pending = ["stage_code_project", "code_build_env"]; reached = set()
        while pending:
            name = pending.pop()
            if name in reached:
                continue
            reached.add(name)
            pending.extend(node.func.id for node in ast.walk(functions[name])
                if isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
                and node.func.id in functions and node.func.id not in reached)
        self.assertTrue({"stage_code_project", "code_build_env"}.issubset(reached))
        self.assertFalse({"_replaceable", "repack", "collect_code_update"} & reached)


if __name__ == "__main__": unittest.main()
