"""Build742's additional menu resolve stays inside compact resource updates."""
import copy
from pathlib import Path
import sys
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / "tools/quest-builder"), str(Path(__file__).parent)]
import apk_update
import menu_depth_resources as menu
import preparation_identity as identity
import recovery_resume
import release
import sky_resources as sky
import storage
import test_update_driver as fixtures
import update_driver as driver

RESOLVE = menu.RENDERING + "MenuDepthHeadResolve.shader"


class Dev742UpdateScopeTests(unittest.TestCase):
    def test_public_release_requires_the_new_shader_without_publishing_native_banks(self):
        self.assertTrue(release.selected(RESOLVE))
        self.assertIn(RESOLVE, release.REQUIRED)
        self.assertNotIn(RESOLVE, release.AUTHORED_RESOURCES)
        self.assertFalse(release.selected(menu.RENDERING + "menu-depth.bundle"))

    def test_complete_menu_recipe_and_compact_source_snapshot_include_the_fifth_shader(self):
        expected = menu.plan(ROOT)
        records = expected["sources"]
        names = {row["path"] for row in records}
        self.assertIn(RESOLVE, names)
        self.assertEqual(set(menu.SHADERS), {"MenuDepthDisplay.shader", "MenuDepthGraphic.shader",
            "MenuDepthCharacter.shader", "MenuDepthHeadResolve.shader"})
        self.assertEqual(menu.plan(ROOT / "absent", records), expected)
        with self.assertRaises(storage.BuildError):
            menu.plan(ROOT / "absent", [row for row in records if row["path"] != RESOLVE])
        selected = driver._update_source_rows(records, art_changed=False, menu_sources=records)
        self.assertEqual(selected, records)

    def test_only_exact_release_selector_bytes_retain_original_preparation_scope(self):
        current, previous = identity.HEAD_RESOLVE_DELIVERY_ROWS["tools/quest-builder/release.py"]
        self.assertEqual(storage.record_file(ROOT / current["path"], current["path"]), current)
        builder_row = storage.record_file(ROOT / identity.BUILDER, identity.BUILDER)

        def scoped(row):
            inputs = {"schema": 1, "target": "game", "game": {"key": "a" * 64},
                "profile": {}, "mod": {"files": [builder_row, row], "modBuild": 743}}
            return identity._scope(inputs, ROOT, recovery_resume)

        self.assertEqual(scoped(current), scoped(previous))
        self.assertNotEqual(scoped({**current, "sha256": "f" * 64}), scoped(previous))
        self.assertNotEqual(scoped({**current, "size": current["size"] + 1}), scoped(previous))

    def test_resolve_only_update_preserves_campaign_art_native_media_and_story(self):
        fixture = fixtures.UpdateDriverTests("runTest")
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        runtime = {"path": "src/GloomhavenVR/WorldUI/MenuDepth/MenuDepthDisplay.cs", "size": 10,
                   "sha256": "1" * 64}
        before = [runtime]
        after = [runtime, storage.record_file(ROOT / RESOLVE, RESOLVE)]
        fixture.current_mod["files"] = copy.deepcopy(before)
        fixture.write_apk()
        with patch.object(driver, "package_fingerprint", return_value=fixture.capsule["packageAbi"]):
            plan = driver.update_requirements(fixture.builder, ROOT, apk_update.inspect_base(fixture.apk),
                after, 743)
        self.assertEqual(plan["rebuildReasons"], [])
        self.assertEqual(plan["updateStrategy"], "compact-code")
        self.assertTrue(plan["codeChanged"])
        for name in ("artChanged", "nativeHelperChanged", "figureBanksChanged", "audioBanksChanged",
                     "movieBanksChanged", "menuStoryChanged"):
            self.assertFalse(plan[name], name)

    def test_sky_recipe_keeps_complete_helper_identity_and_unknown_edits_distinct(self):
        current = sky.plan(ROOT)
        records = copy.deepcopy(current["sources"])
        helper = next(row for row in records if row["path"] == "tools/quest-builder/menu_depth_resources.py")
        helper["sha256"] = "f" * 64
        self.assertNotEqual(sky.plan(ROOT / "absent", records)["recipeKey"], current["recipeKey"])
        self.assertEqual({row["path"] for row in current["sources"]},
            {row["path"] for row in driver._update_source_rows(current["sources"], art_changed=False,
                                                               sky_sources=current["sources"])})


if __name__ == "__main__":
    unittest.main()
