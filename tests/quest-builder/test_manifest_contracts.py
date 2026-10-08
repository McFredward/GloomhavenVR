"""Original CAB provenance must not become generated-project output contracts."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import prepare_resume as resume
from storage import BuildError, write_json


class ManifestRoles(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(); self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name); self.project = self.root / "projects/fixture"
        self.receipt = "Assets/QuestOriginalCampaign/native-sprites.json"
        self.asset = "Assets/Sprite/Original.asset"
        self.source = "StreamingAssets/aa/StandaloneWindows64/actor_portrats_assets_portrait_ancientartillery.bundle"

    def put(self, path, data=b"produced original fields"):
        file = self.project / path; file.parent.mkdir(parents=True, exist_ok=True); file.write_bytes(data)

    def manifest(self, **overrides):
        document = {"schema": 1, "nativeSpriteCount": 1, "restoredNonPackedSpriteCount": 1,
                    "preservedPackedSpriteCount": 0,
                    "sourceContainers": [{"path": self.source, "sha256": "a" * 64}],
                    "assets": [{"assetPath": self.asset, "sourceContainer": self.source,
                                "sourceContainerSha256": "a" * 64, "vertices": [{"x": 0, "y": 0}],
                                "uv": [{"x": 0.5, "y": 0.5}]}],
                    "source": "original-native-CAB-pathID-rect-offset-border-pivot-and-drawing-streams"}
        document.update(overrides); write_json(self.project / self.receipt, document)
        return resume.manifest_contracts(self.project, [self.receipt])

    def journal(self):
        return resume.Preparation(self.root, self.project, input_key="b" * 64, target="game", recipe=1)

    def test_real_sprite_producer_shape_commits_and_resumes_actual_outputs(self):
        self.put(self.asset)
        first = self.journal()
        with first.operation("startup-content", 1):
            first.run("native-sprites", "startup-content", lambda: self.manifest(), lambda result: result)
        first.finish(); first.close()
        self.assertFalse((self.project / ("Assets/" + self.source)).exists())
        self.assertEqual([row["path"] for row in first.value["steps"][0]["outputs"]], [self.receipt, self.asset])
        second = self.journal()
        with second.operation("startup-content", 1):
            second.run("native-sprites", "startup-content", lambda: self.fail("Completed producer repeated"), [])
        second.finish(); second.close()

    def test_real_ordinary_texture_audit_uses_same_provenance_role(self):
        self.receipt = "Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json"
        self.asset = "Assets/Texture/Original.png"
        paths = self.manifest(ordinaryTexture2DCount=1, embeddedNativeTextures=[{"assetPath": "Assets/Texture/Embedded.asset"}])
        self.assertEqual(paths, [self.receipt, self.asset, "Assets/Texture/Embedded.asset"])

    def test_missing_real_output_still_fails_and_preserves_pending_checkpoint(self):
        first = self.journal()
        with self.assertRaisesRegex(BuildError, "missing its required output.*Original.asset"):
            with first.operation("startup-content", 1):
                first.run("native-sprites", "startup-content", lambda: self.manifest(), lambda result: result)
        self.assertEqual(first.value["steps"], [])
        self.assertEqual(first.value["pending"]["name"], "native-sprites")
        first.close()

    def test_generated_streaming_output_remains_required_outside_source_role(self):
        copied = "StreamingAssets/QuestOriginalMovies/Intro.mp4"
        paths = self.manifest(movies=[{"path": copied}])
        self.assertIn("Assets/" + copied, paths)
        # Original importer movie removal may be absent; delivered bytes may not.
        self.assertNotIn("Assets/" + copied, paths.absent)

    def test_source_and_delivered_copy_with_same_relative_name_keep_output_contract(self):
        paths = self.manifest(assets=[{"assetPath": "Assets/" + self.source}])
        self.assertIn("Assets/" + self.source, paths)

    def test_only_explicit_removed_importer_source_is_optional(self):
        copied, importer = "StreamingAssets/QuestOriginalMovies/Intro.mov", "Assets/VideoClip/Intro.mov"
        self.put("Assets/" + copied, b"delivered movie bytes")
        paths = self.manifest(clips=[{"path": copied, "source": importer,
                                     "bindings": [{"sourceScene": "Assets/Scene/Menu.unity"}]}])
        self.assertEqual(paths.absent, {importer})
        self.assertIn("Assets/" + copied, paths)
        self.assertIn("Assets/Scene/Menu.unity", paths)

    def test_source_role_rejects_ambiguous_or_malformed_declarations(self):
        for row in ({"path": self.source}, {"path": self.source, "sha256": "a" * 64, "assetPath": self.asset},
                    {"path": self.source, "sha256": "a" * 64, "outputs": [self.asset]},
                    {"path": self.asset, "sha256": "a" * 64}, {"path": self.source, "sha256": "wrong"}):
            with self.subTest(row=row), self.assertRaises(BuildError): self.manifest(sourceContainers=[row])
        for value in (None, {}, "source"):
            with self.subTest(value=value), self.assertRaises(BuildError): self.manifest(sourceContainers=value)

    def test_source_role_rejects_traversal_platform_paths_and_cache_scope(self):
        for path in ("../outside", "StreamingAssets/../../outside", "C:/game/source.bundle", "C:source.bundle",
                     "\\server\\source", "/outside", "StreamingAssets\\bundle", "Library/source", "Assets", "Packages/source"):
            with self.subTest(path=path), self.assertRaises(BuildError):
                self.manifest(sourceContainers=[{"path": path, "sha256": "a" * 64}])

    def test_duplicate_original_source_with_different_bytes_is_rejected(self):
        with self.assertRaisesRegex(BuildError, "disagrees on original bytes"):
            self.manifest(sourceContainers=[{"path": self.source, "sha256": "a" * 64}, {"path": self.source, "sha256": "c" * 64}])

    def test_generated_output_traversal_is_not_hidden_by_valid_source_provenance(self):
        with self.assertRaises(BuildError): self.manifest(assets=[{"assetPath": "Assets/../outside.asset"}])


if __name__ == "__main__": unittest.main()
