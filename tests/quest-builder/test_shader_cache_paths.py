"""Short cache roots preserve original shader identities and old cache data."""
from pathlib import Path, PureWindowsPath
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import builder
import campaign_shaders
from storage import BuildError


class CampaignShaderCachePaths(unittest.TestCase):
    def test_actual_longest_original_shader_fits_normal_wizard_root(self):
        # Name/relative length witnessed in the complete native shader manifest.
        # No original shader is renamed to shorten the cache path.
        shader = ("Assets/QuestRecoveredBundles/" + "a" * 32 +
                  "/UI_Inverted soft alpha mask (compatible with canvas group).shader.meta")
        root = PureWindowsPath("C:/Users/McFredward/.ghvrq/build")
        key = "b" * 64
        old = root / "tool-cache/campaign-shaders" / key / "overlay" / shader
        current = campaign_shaders.cache_overlay(builder.campaign_shader_cache(root, key)) / shader
        self.assertEqual(len(str(old).encode("utf-16-le")) // 2, 266)
        self.assertEqual(len(str(current).encode("utf-16-le")) // 2, 246)
        self.assertEqual(len(str(old)) - len(str(current)), 20)
        self.assertLess(len(str(current)), 260)
        self.assertEqual(current.parts[-2:], old.parts[-2:])

    def test_cache_selection_keeps_old_native_evidence_and_game_identity(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            key = "a" * 64
            previous = root / "tool-cache/campaign-shaders" / key / "overlay"
            previous.mkdir(parents=True)
            evidence = previous / "native-proof.json"
            evidence.write_bytes(b"retained earlier native evidence")
            cache = builder.campaign_shader_cache(root, key)
            self.assertEqual(cache, root / "tool-cache/cs" / key)
            self.assertEqual(campaign_shaders.cache_overlay(cache), cache / "o")
            self.assertFalse(cache.exists())
            self.assertEqual(evidence.read_bytes(), b"retained earlier native evidence")
            self.assertNotEqual(cache, builder.campaign_shader_cache(root, "b" * 64))
            for invalid in (None, 1, [], "../outside", "A" * 64, "a" * 63):
                with self.subTest(invalid=invalid), self.assertRaises(BuildError):
                    builder.campaign_shader_cache(root, invalid)

    def test_stage_replaces_only_current_overlay_and_preserves_exact_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source, project, game = (root / name for name in ("source", "project", "game"))
            cache = builder.campaign_shader_cache(root, "a" * 64)
            previous = cache / "overlay"
            previous.mkdir(parents=True)
            (previous / "original-evidence").write_bytes(b"old proof")
            current = campaign_shaders.cache_overlay(cache)
            current.mkdir()
            (current / "interrupted-output").write_bytes(b"unfinished generated output")
            manifest = {"graphicsApi": "Vulkan", "compilerPlatform": "Vulkan",
                        "shaders": [{"guid": "b" * 32, "assetPath": "Assets/Original.shader"}],
                        "materials": []}

            def native_inventory(*args, **kwargs):
                (cache / "original-shader-inventory.json").write_bytes(b"{}"); return {}

            def restore(actual_project, inventory, actual_cache, overlay, **kwargs):
                self.assertEqual((actual_project, actual_cache, overlay), (project, cache, current))
                self.assertFalse((overlay / "interrupted-output").exists())
                (overlay / "Assets").mkdir(parents=True)
                (overlay / "Assets/Original.shader").write_bytes(b"exact original program fixture")
                return manifest

            converter = SimpleNamespace(ensure=lambda *args: {})
            producer = SimpleNamespace(restore_project=restore)
            with patch.object(campaign_shaders, "load", side_effect=[converter, producer]), \
                    patch.object(campaign_shaders, "original_cab_bundles", return_value={"original": "owned"}), \
                    patch.object(campaign_shaders.full_shaders, "inventory", side_effect=native_inventory), \
                    patch.object(campaign_shaders, "preserve_sources", return_value={}):
                self.assertEqual(campaign_shaders.stage(source, project, game, cache), manifest)
            self.assertEqual((project / "Assets/Original.shader").read_bytes(), b"exact original program fixture")
            self.assertEqual((previous / "original-evidence").read_bytes(), b"old proof")


if __name__ == "__main__":
    unittest.main()
