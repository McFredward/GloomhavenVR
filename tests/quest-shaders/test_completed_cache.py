"""Cache provenance contracts; runtime controls live in the real Unity tiny fixture."""
import hashlib
import importlib.util
from pathlib import Path
import re
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
spec = importlib.util.spec_from_file_location("completed_cache_fixture", Path(__file__).with_name("run_completed_cache_fixture.py"))
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)


class CompletedCacheContracts(unittest.TestCase):
    def test_migration_pins_exact_known_consumer_and_unchanged_native_verifiers(self):
        helper = (EDITOR / "QuestCampaignShaderCache.cs").read_text()
        for constant, filename in (("MigrationConsumer", "QuestCampaignShaderValidation.cs"), ("VulkanVerifier", "QuestVulkanShaderValidation.cs"), ("SmolvVerifier", "QuestSmolvDecoder.cs")):
            pin = re.search(r'\b' + constant + r' = "([a-f0-9]{64})";', helper).group(1)
            self.assertEqual(pin, hashlib.sha256((EDITOR / filename).read_bytes()).hexdigest())

    def test_private_fixture_never_overwrites_an_existing_project(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory) / "fixture"
            fixture.prepare(project)
            shader = project / "Assets/QuestOriginalCampaign/CacheFixture.shader"
            before = shader.read_bytes()
            with self.assertRaisesRegex(ValueError, "existing projects"):
                fixture.prepare(project)
            self.assertEqual(shader.read_bytes(), before)
            self.assertIn("QuestPrivate/Cache", before.decode())
            self.assertFalse((project / "Assets/QuestRecoveredBundles").exists())

    def test_graphics_cache_controls_are_real_and_preserve_material_validation(self):
        consumer = (EDITOR / "QuestCampaignShaderValidation.cs").read_text()
        self.assertLess(consumer.index("VerifyImportedMaterials(input, shaders);"), consumer.index("LastValidationCacheReused = true;"))
        witness = (Path(__file__).parent / "UnityHost/QuestShaderCacheWitness.cs").read_text()
        for control in ("shader-drift-real-revalidation", "include-drift-real-revalidation", "meta-drift-real-revalidation", "graphics-settings-real-revalidation", "helper-drift", "material-identity-still-checked-on-hit", "mod-profile-packaging-do-not-invalidate"):
            self.assertIn(control, witness)
        self.assertIn("LastNativeCompileCount==0", witness)
        self.assertIn("RepairOwnedOutputs", witness)


if __name__ == "__main__":
    unittest.main()
