"""The new dev menu keeps numeric Content outside native/game conversion.

These are host planning/closure controls. Actual serialized delivery, Android
shader compilation and static weaving have separate native-consumer evidence.
"""
import copy
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / "tools/quest-builder"), str(Path(__file__).parent)]
import apk_update
import native_update_resume
import storage
import update_driver as driver
import test_update_driver as fixtures


def rows(*names, digest="1"):
    return [{"path": name, "size": 10, "sha256": digest * 64} for name in names]


class MenuUpdateScopeTests(unittest.TestCase):
    def test_story_files_are_delivery_content_while_unknown_sources_stay_compiler_inputs(self):
        prefix = "src/GloomhavenVR/Assets/MenuDepth/Story/"
        story = rows(prefix + "manifest.json", prefix + "story.gmd")
        self.assertEqual(driver._code_rows(story), [])
        self.assertEqual(driver._native_producer_rows(story), [])
        self.assertEqual(driver.rebuild_reasons(story, []), [])
        self.assertEqual(driver._art_rows(story), [])
        for name in (prefix + "Future.cs", prefix + "Future.bundle",
                     "tools/quest-menu-depth/Editor/MenuDepthResourceBuilder.cs",
                     "tools/quest-builder/menu_depth_resources.py",
                     "src/GloomhavenVR/Assets/MenuDepth/Rendering/MenuDepthDisplay.shader"):
            with self.subTest(name=name):
                self.assertEqual(driver._code_rows(rows(name)), rows(name))

    def test_compiler_snapshot_retains_tiny_resource_closure_without_loose_story(self):
        runtime = "src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthDisplay.cs"
        story = "src/GloomhavenVR/Assets/MenuDepth/Story/story.gmd"
        converter = "prebuilt/quest-converters-win64-v1.zip"
        helper = "tools/quest-shaders/produce.py"
        files = rows(runtime, story, converter, helper, "tools/quest-movies/large.depth.gz")
        selected = driver._update_source_rows(files, art_changed=False, menu_sources=rows(converter, helper))
        self.assertEqual({row["path"] for row in selected}, {runtime, converter, helper})
        self.assertNotIn(story, {row["path"] for row in driver._update_source_rows(files, art_changed=True)})

    def test_story_only_change_requests_no_unity_or_original_asset_work(self):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        old = rows("src/GloomhavenVR/Assets/MenuDepth/Story/story.gmd")
        new = rows("src/GloomhavenVR/Assets/MenuDepth/Story/story.gmd", digest="2")
        fixture.current_mod["files"] = copy.deepcopy(old)
        fixture.write_apk()
        base = apk_update.inspect_base(fixture.apk)
        with patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
            result = driver.update_requirements(fixture.builder, ROOT, base, new, fixture.current_mod["modBuild"])
        self.assertTrue(result["menuStoryChanged"])
        self.assertEqual(result["rebuildReasons"], [])
        for name in ("codeChanged", "artChanged", "requiresUnity", "nativeHelperChanged",
                     "figureBanksChanged", "audioBanksChanged", "movieBanksChanged"):
            self.assertFalse(result[name], name)

    def test_story_selector_is_bound_by_the_actual_compiler_recipe(self):
        import tempfile
        source = Path(driver.__file__).read_text()
        changed = source.replace('name.endswith(".gmd") or name == ', 'name.endswith(".anything") or name == ')
        self.assertNotEqual(source, changed)
        builder = SimpleNamespace(apk_updates=apk_update, value_hash=storage.value_hash, BuildError=storage.BuildError)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "driver.py"
            path.write_text(source)
            before = native_update_resume.compiler_recipe(builder, path)
            path.write_text(changed)
            self.assertNotEqual(before, native_update_resume.compiler_recipe(builder, path))


if __name__ == "__main__":
    unittest.main()
