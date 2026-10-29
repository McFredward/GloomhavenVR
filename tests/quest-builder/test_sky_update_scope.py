"""Independent sky resources retain compact updates and exact source closure."""
import ast
import copy
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / "tools/quest-builder"), str(Path(__file__).parent)]
import apk_update
import release
import storage
import update_driver as driver
import test_update_driver as fixtures

SKY = "src/GloomhavenVR/Core/Environment/NativeDefaultSky.cs"
MENU = "src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthDisplay.cs"
PROVENANCE = "src/GloomhavenVR/Assets/DefaultSky/native-default-sky.provenance.json"
EXPORTER = "tools/default-sky-native/export.py"
EDITOR = "tools/quest-default-sky/Editor/DefaultSkyResourceBuilder.cs"
HELPER = "tools/quest-builder/sky_resources.py"


def rows(*names, digest="1"):
    return [{"path": name, "size": 10, "sha256": digest * 64} for name in names]


class SkyUpdateScopeTests(unittest.TestCase):
    def test_game_free_release_requires_public_recipe_and_excludes_native_bank(self):
        for name in (PROVENANCE, EXPORTER, EDITOR, HELPER):
            with self.subTest(name=name):
                self.assertTrue(release.selected(name))
                self.assertIn(name, release.REQUIRED)
        self.assertTrue(release.selected(SKY))
        for name in ("src/GloomhavenVR/Assets/DefaultSky/native-default-sky.bundle",
                     "tools/default-sky-native/private-native.bundle",
                     "tools/quest-default-sky/private-android.bundle"):
            with self.subTest(name=name):
                self.assertFalse(release.selected(name))
                self.assertNotIn(name, release.AUTHORED_RESOURCES)

    def test_sky_runtime_and_recipes_are_code_without_original_art_producers(self):
        files = rows(SKY, PROVENANCE, EXPORTER, EDITOR, HELPER,
                     "src/GloomhavenVR/Assets/DefaultSky/Future.cs",
                     "src/GloomhavenVR/Assets/DefaultSky/Future.bundle")
        self.assertEqual(driver._code_rows(files), sorted(files, key=lambda row: row["path"]))
        self.assertEqual(driver._art_rows(files), [])
        self.assertEqual(driver._payload_rows(files), [])
        self.assertEqual(driver._native_rows(files), [])
        self.assertEqual(driver.rebuild_reasons(files, []), [])

    def test_compact_snapshot_uses_exact_sky_and_menu_producer_source_union(self):
        shared = "tools/quest-shaders/produce.py"
        converter = "prebuilt/quest-converters-win64-v1.zip"
        template = "src/GloomhavenVR/Assets/QuestLoading/ghvr-quest-loading.bundle"
        story = "src/GloomhavenVR/Assets/MenuDepth/Story/known.gmd"
        unwanted = "tools/default-sky-native/unconsumed-report.py"
        sky_names = (PROVENANCE, EXPORTER, EDITOR, HELPER, shared, converter, template)
        files = rows(SKY, MENU, story, unwanted, *sky_names,
                     "tools/quest-movies/large.depth.gz", "tools/quest-builder/menu_depth_resources.py")
        selected = driver._update_source_rows(files, art_changed=False,
            menu_sources=rows("tools/quest-builder/menu_depth_resources.py", shared), sky_sources=rows(*sky_names))
        expected = {SKY, MENU, *sky_names, "tools/quest-builder/menu_depth_resources.py"}
        self.assertEqual({row["path"] for row in selected}, expected)
        self.assertTrue(all(row in files for row in selected))

    def test_sky_recipe_only_change_keeps_original_campaign_and_media(self):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp(); self.addCleanup(fixture.doCleanups)
        old = rows(SKY, PROVENANCE, EXPORTER, EDITOR, HELPER)
        fixture.current_mod["files"] = copy.deepcopy(old); fixture.write_apk()
        current = [row if row["path"] != EXPORTER else {**row, "sha256": "2" * 64} for row in old]
        with patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
            result = driver.update_requirements(fixture.builder, ROOT, apk_update.inspect_base(fixture.apk),
                current, fixture.current_mod["modBuild"])
        self.assertEqual(result["rebuildReasons"], [])
        self.assertEqual(result["updateStrategy"], "compact-code")
        self.assertTrue(result["codeChanged"])
        for name in ("artChanged", "nativeHelperChanged", "figureBanksChanged", "audioBanksChanged",
                     "movieBanksChanged", "menuStoryChanged"):
            self.assertFalse(result[name], name)

    def test_resource_dependencies_close_all_four_current_cache_families(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder)
            files = rows(SKY, MENU)
            menu_key, recipe_key, producer_key = "a" * 64, "b" * 64, "c" * 64
            builder = SimpleNamespace(
                menu_depth_resources=SimpleNamespace(plan=Mock(return_value={"producerKey": menu_key})),
                sky_resources=SimpleNamespace(plan=Mock(return_value={"recipeKey": recipe_key})))
            retained = ["cache/menu-depth-content/" + "d" * 64, "cache/retained-mod-content/" + "e" * 64]
            sky = "cache/default-sky-resources/" + recipe_key + "/" + producer_key
            menu = "cache/menu-depth-resources/" + menu_key
            for name in (*retained, sky, menu): (output / name).mkdir(parents=True)
            self.assertEqual(driver._complete_resource_inputs(builder, ROOT, files, output, retained, sky_resource=sky),
                sorted([*retained, sky, menu]))
            builder.sky_resources.plan.assert_called_once_with(ROOT, files)
            self.assertIsNone(driver._complete_resource_inputs(builder, ROOT, files, output, retained))
            wrong_recipe = sky.replace(recipe_key, "f" * 64)
            (output / wrong_recipe).mkdir(parents=True)
            self.assertIsNone(driver._complete_resource_inputs(builder, ROOT, files, output, retained,
                sky_resource=wrong_recipe))
            (output / menu).rmdir()
            self.assertIsNone(driver._complete_resource_inputs(builder, ROOT, files, output, retained, sky_resource=sky))

    def test_legacy_menu_dependency_roster_does_not_require_a_new_sky(self):
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder)
            key = "a" * 64
            menu = "cache/menu-depth-resources/" + key
            (output / menu).mkdir(parents=True)
            builder = SimpleNamespace(menu_depth_resources=SimpleNamespace(plan=lambda *args: {"producerKey": key}))
            self.assertEqual(driver._complete_resource_inputs(builder, ROOT, rows(MENU), output, ["retained"]),
                [menu, "retained"])

    def test_code_player_hands_qualified_apk_to_weave_and_keeps_portable_zip_selection(self):
        class HandoffComplete(Exception):
            pass
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder)
            selected_zip = str(output / "selected-portable.zip")
            qualified_apk = output / "device-inputs/imported/base.apk"
            args = SimpleNamespace(base_apk=selected_zip)
            observed = []
            def weave(actual_args, *unused):
                observed.append(actual_args._quest_resource_base_apk)
                self.assertEqual(actual_args.base_apk, selected_zip)
                raise HandoffComplete()
            builder = SimpleNamespace(apk_updates=apk_update, weave=weave)
            inputs = {"profile": {}, "mod": {"modBuild": 741}}
            root = output / "updates" / ("a" * 64)
            with patch.object(apk_update, "stage_code_project") as stage, self.assertRaises(HandoffComplete):
                driver._build_code_player(builder, args, inputs, output, output / "source", output / "game",
                    output / "project", qualified_apk, {}, root, "abi", output / "compiled.apk", output / "compiled.json")
            self.assertEqual(observed, [qualified_apk])
            self.assertEqual(stage.call_args.args[2], qualified_apk)
            self.assertEqual(args.base_apk, selected_zip)

    def test_actual_weave_uses_qualified_apk_for_both_resources_and_binds_sky_bytes(self):
        class CompileBoundary(Exception):
            pass
        # Execute the actual consumer functions with closed external ports.
        # Eager helper loading and real shader production have separate suites.
        path = ROOT / "tools/quest-builder/builder.py"
        tree = ast.parse(path.read_text())
        functions = [node for node in tree.body if isinstance(node, ast.FunctionDef)
                     and node.name in {"weave", "has_menu_depth"}]
        self.assertEqual({node.name for node in functions}, {"weave", "has_menu_depth"})
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder)
            menu_root = output / "cache/menu-depth-resources" / ("a" * 64)
            sky_root = output / "cache/default-sky-resources" / ("b" * 64) / ("c" * 64)
            menu_root.mkdir(parents=True); sky_root.mkdir(parents=True)
            resources = []
            for root in (menu_root, sky_root):
                bank, provenance = root / "private.bundle", root / "private.provenance.json"
                bank.write_bytes(b"qualified Android bank"); provenance.write_bytes(b"qualified provenance")
                resources.append({"bundle": bank, "provenance": provenance, "producerKey": root.name})
            menu = SimpleNamespace(produce=Mock(return_value=resources[0]))
            sky = SimpleNamespace(produce=Mock(return_value=resources[1]))
            commands, keys = [], []
            def command(argv, *unused, **kwargs):
                commands.append(argv)
                raise CompileBoundary()
            def run(stage, key, action):
                self.assertEqual(stage, "weave"); keys.append(key)
                return action()
            namespace = {"Path": Path, "tool_path": lambda value, unused: Path(value),
                "build_progress": SimpleNamespace(event=Mock()), "menu_depth_resources": menu,
                "sky_resources": sky, "RECIPE": 1, "digest": storage.digest, "value_hash": storage.value_hash,
                "Stages": lambda unused: SimpleNamespace(run=run), "command": command}
            namespace["_module_context"] = lambda: SimpleNamespace(**namespace)
            exec(compile(ast.Module(body=functions, type_ignores=[]), str(path), "exec"), namespace)
            builder = SimpleNamespace(apk_updates=apk_update, weave=namespace["weave"])
            args = SimpleNamespace(base_apk=str(output / "selected.zip"), target="game", command="update-mod",
                dotnet="fixture-dotnet", unity_editor="fixture-unity", installed_game=True)
            qualified_apk = output / "imports/base.apk"
            source, game, project = (output / name for name in ("source", "compiler-seed", "project"))
            inputs = {"profile": {}, "profileKey": "d" * 64, "game": {"key": "e" * 64},
                "mod": {"key": "f" * 64, "modBuild": 741, "files": rows(MENU, SKY)}}
            def build():
                with patch.object(apk_update, "stage_code_project"), self.assertRaises(CompileBoundary):
                    driver._build_code_player(builder, args, inputs, output, source, game, project,
                        qualified_apk, {}, output / "updates" / ("1" * 64), "abi",
                        output / "compiled.apk", output / "compiled.json")
            build()
            for producer in (menu, sky):
                self.assertEqual(producer.produce.call_args.kwargs["base_apk"], qualified_apk)
                self.assertEqual(producer.produce.call_args.kwargs["source_files"], inputs["mod"]["files"])
            self.assertEqual(sky.produce.call_args.kwargs["project"], project)
            self.assertEqual(sky.produce.call_args.kwargs["game"], game)
            for resource, prefix in zip(resources, ("MenuDepth", "DefaultSky")):
                self.assertIn("-p:GloomhavenVR" + prefix + "Bundle=" + str(resource["bundle"]), commands[0])
                self.assertIn("-p:GloomhavenVR" + prefix + "Provenance=" + str(resource["provenance"]), commands[0])
            self.assertEqual(args.base_apk, str(output / "selected.zip"))
            self.assertEqual(args._quest_sky_resource, sky_root.relative_to(output).as_posix())
            original = keys[-1]
            for name in ("bundle", "provenance", "producerKey"):
                with self.subTest(changed=name):
                    if name == "producerKey":
                        saved = resources[1][name]; resources[1][name] = "9" * 64
                    else:
                        saved = resources[1][name].read_bytes(); resources[1][name].write_bytes(saved + b" changed")
                    try:
                        build()
                        self.assertNotEqual(keys[-1], original)
                    finally:
                        if name == "producerKey": resources[1][name] = saved
                        else: resources[1][name].write_bytes(saved)


if __name__ == "__main__":
    unittest.main()
