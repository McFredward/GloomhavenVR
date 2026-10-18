"""Reject Unity Player success with actual native shader compiler failures."""
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import builder
from storage import BuildError


class PlayerShaderLogTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "native-player.log"

    def tearDown(self):
        self.temp.cleanup()

    def test_build_success_does_not_override_native_shader_failure(self):
        self.path.write_text("Shader error in 'Original': missing native bank (on vulkan)\nBuild completed with a result of 'Succeeded'\n")
        with self.assertRaisesRegex(BuildError, "1 native shader compiler errors"):
            builder.validate_player_shader_log(self.path)

    def test_compute_errors_and_every_repeated_error_are_counted(self):
        self.path.write_text("Compute shader error in 'Original': native failure\n" * 8)
        with self.assertRaises(BuildError) as failed:
            builder.validate_player_shader_log(self.path)
        self.assertIn("8 native shader compiler errors", str(failed.exception))
        self.assertEqual(str(failed.exception).count("native failure"), 5)

    def test_clean_native_compile_records_only_its_actual_scope(self):
        self.path.write_text("Shader warning in 'Original': precision\nBuild completed with a result of 'Succeeded'\n")
        result = builder.validate_player_shader_log(self.path)
        self.assertEqual(result["nativeCompilerErrorCount"], 0)
        self.assertFalse(result["originalShaderMatrixRerun"])
        self.assertFalse(result["hardwareVerified"])

    def test_missing_or_linked_native_log_is_not_a_clean_compile(self):
        with self.assertRaises(BuildError):
            builder.validate_player_shader_log(self.path)
        self.path.write_text("Succeeded\n")
        linked = self.path.with_name("linked.log")
        linked.symlink_to(self.path)
        with self.assertRaises(BuildError):
            builder.validate_player_shader_log(linked)


if __name__ == "__main__":
    unittest.main()
