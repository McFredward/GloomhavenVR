"""Exercise generated movie delivery and the original scene data-source seam."""

import json
from pathlib import Path
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import startup
import storage
from test_media import fixture


class StartupMovieDeliveryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.project, self.game = self.root / "project", self.root / "owned-game"
        self.scenes = ["Assets/Scenes/" + name + ".unity" for name in startup.SCENE_NAMES]
        for relative in self.scenes:
            path = self.project / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n")
        storage.write_json(self.project / startup.REPORT, {"selectedScenes": self.scenes})
        self.clip = self.add_clip("original-intro", "a" * 32, b"owned H264 MP4 fixture")
        self.add_player(self.scenes[1], "a" * 32)
        self.ambient = self.game / "StreamingAssets/Movies/Ambient/menu.mov"
        self.ambient.parent.mkdir(parents=True)
        self.ambient.write_bytes(b"original MP4/H264 with authored mov extension")
        narrative = self.game / "StreamingAssets/Movies/CP_Intro/story.mov"
        narrative.parent.mkdir(parents=True)
        narrative.write_bytes(b"not part of startup menu closure")
        self.originals = {path: path.read_bytes() for path in self.game.rglob("*") if path.is_file()}

    def tearDown(self):
        self.temp.cleanup()

    def add_clip(self, name, guid, payload):
        path = self.project / ("Assets/VideoClip/" + name + ".mp4")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
        Path(str(path) + ".meta").write_text("fileFormatVersion: 2\nguid: " + guid + "\nVideoClipImporter:\n  importAudio: 1\n")
        return path

    def add_player(self, relative, guid, name="Original Player", identity=58):
        path = self.project / relative
        with path.open("a") as output:
            output.write(f"""--- !u!1 &{identity + 1}
GameObject:
  m_Name: {name}
--- !u!4 &{identity + 2}
Transform:
  m_GameObject: {{fileID: {identity + 1}}}
  m_Father: {{fileID: 0}}
--- !u!328 &{identity}
VideoPlayer:
  m_GameObject: {{fileID: {identity + 1}}}
  m_VideoClip: {{fileID: 32900000, guid: {guid}, type: 3}}
  m_TargetCamera: {{fileID: 27}}
  m_RenderMode: 1
  m_DataSource: 0
  m_Url:
  m_AudioOutputMode: 2
  m_PlayOnAwake: 0
  m_WaitForFirstFrame: 1
--- !u!114 &{identity + 3}
MonoBehaviour:
  _videoDuration: 8
  _player: {{fileID: {identity}}}
  _logoShowingDuration: 2.2
""")

    def test_original_bytes_and_native_callback_fields_are_retained(self):
        original = (self.project / self.scenes[1]).read_text()
        expected = original.replace("m_VideoClip: {fileID: 32900000, guid: " + "a" * 32 + ", type: 3}", "m_VideoClip: {fileID: 0}").replace("m_DataSource: 0", "m_DataSource: 1")
        source = self.clip.read_bytes()
        result = startup.stage_startup_movies(self.project, self.game)
        self.assertEqual((self.project / self.scenes[1]).read_text(), expected)
        self.assertEqual(result["totalBytes"], len(source) + self.ambient.stat().st_size)
        self.assertFalse(result["fullGameReady"])
        self.assertEqual(result["scope"], "original-startup-menu-movies")
        clip = result["clips"][0]
        self.assertEqual(clip["guid"], "a" * 32)
        self.assertEqual(clip["name"], "original-intro")
        self.assertEqual(clip["bindings"], [{"scene": "Intro", "playerName": "Original Player", "playerPath": "Original Player", "playerFileId": "58", "sourceScene": self.scenes[1]}])
        staged = self.project / "Assets" / clip["path"]
        self.assertEqual(staged.read_bytes(), source)
        self.assertEqual(storage.digest(staged), clip["sha256"])
        self.assertEqual(clip["delivery"], "original")
        self.assertEqual(clip["originalSha256"], clip["sha256"])
        self.assertEqual(clip["originalSize"], clip["size"])
        self.assertFalse(self.clip.exists())
        self.assertFalse(Path(str(self.clip) + ".meta").exists())
        self.assertEqual([row["path"] for row in result["externalMovies"]], ["StreamingAssets/Movies/Ambient/menu.mov"])
        self.assertFalse((self.project / "Assets/StreamingAssets/Movies/CP_Intro").exists())
        self.assertEqual(json.loads((self.project / "Assets/Quest/Resources" / startup.MOVIES_REPORT).read_text()), result)
        self.assertEqual({path: path.read_bytes() for path in self.originals}, self.originals)

    def test_derived_movie_manifest_names_actual_delivery_and_original_provenance(self):
        source = fixture()
        self.clip.write_bytes(source)
        before = (self.project / self.scenes[1]).read_text()
        result = startup.stage_startup_movies(self.project, self.game)
        clip = result["clips"][0]
        staged = self.project / "Assets" / clip["path"]
        self.assertEqual(clip["delivery"], "android-mp4-tmcd-remux-v1")
        self.assertEqual(clip["sha256"], storage.digest(staged))
        self.assertNotEqual(clip["sha256"], clip["originalSha256"])
        self.assertEqual(clip["originalSize"], len(source))
        self.assertEqual(clip["size"], staged.stat().st_size)
        self.assertTrue(clip["mediaProof"]["mediaPayloadUnchanged"])
        expected = before.replace("m_VideoClip: {fileID: 32900000, guid: " + "a" * 32 + ", type: 3}", "m_VideoClip: {fileID: 0}").replace("m_DataSource: 0", "m_DataSource: 1")
        self.assertEqual((self.project / self.scenes[1]).read_text(), expected)
        self.assertEqual(result["totalBytes"], clip["size"] + self.ambient.stat().st_size)

    def test_native_menu_alternatives_and_promotion_bindings_are_complete(self):
        other = self.ambient.with_name("alternative.mov")
        other.write_bytes(b"another authored random menu alternative")
        trailer = self.add_clip("gameplay-trailer", "b" * 32, b"original promotion video")
        self.add_player(self.scenes[2], "b" * 32)
        self.add_player(self.scenes[3], "b" * 32)
        result = startup.stage_startup_movies(self.project, self.game)
        self.assertEqual(len(result["externalMovies"]), 2)
        self.assertEqual(len(result["clips"]), 2)
        self.assertEqual([item["scene"] for item in result["clips"][1]["bindings"]], ["Gloomhaven_unified", "MainMenu"])
        self.assertFalse(trailer.exists())

    def test_missing_ambient_closure_fails_before_rewriting_scene(self):
        original = (self.project / self.scenes[1]).read_bytes()
        self.ambient.unlink()
        with self.assertRaisesRegex(storage.BuildError, "Movies/Ambient"):
            startup.stage_startup_movies(self.project, self.game)
        self.assertEqual((self.project / self.scenes[1]).read_bytes(), original)
        self.assertTrue(self.clip.exists())

    def test_unrecovered_clip_and_duplicate_guid_fail_closed(self):
        self.add_player(self.scenes[3], "c" * 32)
        with self.assertRaisesRegex(storage.BuildError, "unrecovered"):
            startup.stage_startup_movies(self.project, self.game)
        self.add_clip("duplicate", "a" * 32, b"different source")
        with self.assertRaisesRegex(storage.BuildError, "duplicate GUID"):
            startup.stage_startup_movies(self.project, self.game)

    def test_existing_clip_url_or_unknown_import_shape_is_not_rewritten(self):
        path = self.project / self.scenes[1]
        path.write_text(path.read_text().replace("  m_Url:\n", "  m_Url: authored-other-source\n"))
        with self.assertRaisesRegex(storage.BuildError, "unexpected data-source"):
            startup.stage_startup_movies(self.project, self.game)
        self.assertTrue(self.clip.exists())

    def test_additional_serialized_reference_prevents_importer_removal(self):
        original = (self.project / self.scenes[1]).read_bytes()
        asset = self.project / "Assets/reference.asset"
        asset.write_text("%YAML\n  referencedVideo: {fileID: 32900000, guid: " + "a" * 32 + ", type: 3}\n")
        with self.assertRaisesRegex(storage.BuildError, "retain unsupported clip references"):
            startup.stage_startup_movies(self.project, self.game)
        self.assertTrue(self.clip.exists())
        self.assertTrue(Path(str(self.clip) + ".meta").exists())
        self.assertEqual((self.project / self.scenes[1]).read_bytes(), original)
        self.assertFalse((self.project / "Assets/StreamingAssets/QuestOriginalMovies").exists())

    def test_copy_cache_reuses_only_verified_bytes(self):
        target = self.root / "copied/menu.mov"
        first = startup._copy_movie(self.ambient, target, "StreamingAssets/Movies/Ambient/menu.mov")
        timestamp = target.stat().st_mtime_ns
        self.assertEqual(startup._copy_movie(self.ambient, target, first["path"]), first)
        self.assertEqual(target.stat().st_mtime_ns, timestamp)
        target.write_bytes(b"corrupt cached copy")
        self.assertEqual(startup._copy_movie(self.ambient, target, first["path"]), first)
        self.assertEqual(target.read_bytes(), self.ambient.read_bytes())

    def test_unsafe_scene_or_project_source_overlap_is_rejected(self):
        with self.assertRaisesRegex(storage.BuildError, "separate"):
            startup.stage_startup_movies(self.game, self.game)
        storage.write_json(self.project / startup.REPORT, {"selectedScenes": ["../" + path for path in self.scenes]})
        with self.assertRaisesRegex(storage.BuildError, "unsafe scene"):
            startup.stage_startup_movies(self.project, self.game)

    def test_linked_source_and_destination_are_rejected(self):
        if sys.platform == "win32":
            self.skipTest("Symbolic links require Windows developer privileges.")
        original = self.root / "outside.mov"
        original.write_bytes(b"unrelated private input")
        self.ambient.unlink()
        self.ambient.symlink_to(original)
        with self.assertRaisesRegex(storage.BuildError, "missing or linked"):
            startup.stage_startup_movies(self.project, self.game)
        self.assertEqual(original.read_bytes(), b"unrelated private input")
        self.ambient.unlink()
        self.ambient.write_bytes(b"restored owned menu bytes")
        outside = self.root / "outside"; outside.mkdir()
        linked = self.root / "linked"; linked.symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(storage.BuildError, "destinations must not be symbolic links"):
            startup._copy_movie(self.ambient, linked / "new-directory/menu.mov", "StreamingAssets/Movies/Ambient/menu.mov")
        self.assertEqual(list(outside.iterdir()), [])


if __name__ == "__main__":
    unittest.main()
