#!/usr/bin/env python3
"""Conversion contract tests, including stale/invalid input negative controls."""
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import recover
import md4
import probe_slice


class RecoveryContracts(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)

    def tearDown(self):
        self.temp.cleanup()

    def fake_game(self):
        data = self.root / "owned/GH_Data"
        (data / "Managed").mkdir(parents=True)
        for name in ("Managed/GH.Runtime.dll", "globalgamemanagers", "level0", "resources.assets", "ScriptingAssemblies.json"):
            (data / name).write_bytes(b"original-" + name.encode())
        return data

    def test_original_game_discovery_and_incomplete_input(self):
        game = self.fake_game()
        self.assertEqual(recover.resolve_game_data(game.parent), game)
        (game / "level0").unlink()
        with self.assertRaisesRegex(recover.RecoveryError, "missing level0"):
            recover.resolve_game_data(game)

    def test_output_never_overwrites_source_or_nonempty_output(self):
        game = self.fake_game()
        for output in (game, game / "Converted", game.parent):
            with self.assertRaisesRegex(recover.RecoveryError, "read-only"):
                recover.validate_output(game, output)
        output = self.root / "converted"
        output.mkdir()
        (output / "keep.txt").write_text("unrelated")
        with self.assertRaisesRegex(recover.RecoveryError, "already contains"):
            recover.validate_output(game, output)
        self.assertEqual((output / "keep.txt").read_text(), "unrelated")

    def test_source_hash_changes_for_bytes_not_only_file_size(self):
        game = self.fake_game()
        first, before = recover.source_inventory(game)
        original = (game / "level0").read_bytes()
        (game / "level0").write_bytes(bytes([original[0] ^ 1]) + original[1:])
        second, after = recover.source_inventory(game)
        self.assertNotEqual(before, after)
        self.assertEqual([x["bytes"] for x in first], [x["bytes"] for x in second])

    def test_staging_excludes_desktop_plugins_and_unselected_streaming(self):
        game = self.fake_game()
        (game / "Plugins").mkdir()
        (game / "Plugins/native.dll").write_bytes(b"windows native")
        (game / "StreamingAssets/aa").mkdir(parents=True)
        bundle = game / "StreamingAssets/aa/actor.bundle"
        bundle.write_bytes(b"UnityFS\0owned bundle")
        source_hash = recover.source_inventory(game)[1]
        stage = self.root / "stage/GH_Data"
        recover.stage_input(game, stage, ["StreamingAssets/aa/actor.bundle"])
        self.assertFalse((stage / "Plugins").exists())
        self.assertFalse((stage / "StreamingAssets").exists())
        self.assertEqual((stage / "SelectedBundles/StreamingAssets/aa/actor.bundle").read_bytes(), bundle.read_bytes())
        self.assertEqual(source_hash, recover.source_inventory(game)[1])
        with self.assertRaisesRegex(recover.RecoveryError, "existing file"):
            recover.stage_input(game, self.root / "invalid-stage", ["../outside.bundle"])

    def test_archive_path_traversal_is_rejected_before_extraction(self):
        archive = self.root / "malicious.zip"
        with zipfile.ZipFile(archive, "w") as content:
            content.writestr("../outside.txt", "unexpected")
        destination = self.root / "extract"
        destination.mkdir()
        with self.assertRaisesRegex(recover.RecoveryError, "escapes"):
            recover.safe_extract(archive, destination)
        self.assertFalse((self.root / "outside.txt").exists())

    def test_resume_detects_changed_inputs_changed_outputs_and_new_files(self):
        project = self.root / "project"
        project.mkdir()
        asset = project / "scene.unity"
        asset.write_text("original scene")
        value = {"recipeVersion": recover.RECIPE_VERSION, "sourceHash": "source", "toolLockHash": "tool",
                 "selectedBundles": [], "outputInventory": recover.project_inventory(project)}
        recover.write_json(project / recover.RECEIPT, value)
        self.assertEqual(recover.verify_resume(project, "source", "tool", []), value)
        with self.assertRaisesRegex(recover.RecoveryError, "inputs/recipe differ"):
            recover.verify_resume(project, "different", "tool", [])
        asset.write_text("modified scene")
        with self.assertRaisesRegex(recover.RecoveryError, "modified or lost"):
            recover.verify_resume(project, "source", "tool", [])
        asset.write_text("original scene")
        (project / "unrecorded.dll").write_bytes(b"unexpected code")
        with self.assertRaisesRegex(recover.RecoveryError, "unrecorded content"):
            recover.verify_resume(project, "source", "tool", [])

    def test_unresolved_script_is_not_silently_accepted_or_fabricated(self):
        project = self.root / "project"
        plugins = project / "Assets/Plugins"
        plugins.mkdir(parents=True)
        (plugins / "Game.dll").write_bytes(b"metadata fixture only")
        (plugins / "Game.dll.meta").write_text("guid: " + "1" * 32 + "\n")
        fid = md4.script_file_id("Example", "Component")
        scene = project / "Assets/Example.unity"
        scene.write_text("m_Script: {fileID: " + str(fid) + ", guid: " + "1" * 32 + ", type: 3}\n")
        types = {"Game.dll": {"name": "Game", "types": [{"namespace": "Example", "name": "Component", "nested": False}]}}
        proof = recover.audit_script_bindings(project, types)
        self.assertEqual(proof["scriptReferenceCount"], 1)
        self.assertEqual(proof["unexpectedUnresolvedCount"], 0)
        types["Game.dll"]["types"] = []
        failed = recover.audit_script_bindings(project, types)
        self.assertEqual(failed["unexpectedUnresolvedCount"], 1)
        identities = [{"assembly": "Game.dll", "namespace": "Example", "name": "Component", "fileId": fid}]
        original_orphan = recover.audit_script_bindings(project, types, identities)
        self.assertEqual(original_orphan["unresolvedCount"], 1)
        self.assertEqual(original_orphan["unexpectedUnresolvedCount"], 0)
        self.assertFalse(original_orphan["unityImportVerified"])
        self.assertEqual(original_orphan["status"], "original-orphans-retained")

    def test_unity_md4_identifiers_match_independent_rfc_and_original_vectors(self):
        vectors = {b"": "31d6cfe0d16ae931b73c59d7e0c089c0", b"a": "bde52cb31de33e46245e05fbdbd6fb24",
                   b"abc": "a448017aaf21d8525fc10ae87aa6729d", b"message digest": "d9130a8164549fe818874806e1c7014b",
                   b"abcdefghijklmnopqrstuvwxyz": "d79e1c308aa5bbcdeea8ed63df412da9"}
        for text, expected in vectors.items():
            self.assertEqual(md4.digest(text).hex(), expected)
        # These IDs were independently read from the owned Bootstrap scene and
        # its serialized original MonoScript metadata, not emitted by the test.
        self.assertEqual(md4.script_file_id("", "Bootstrap"), -664525519)
        self.assertEqual(md4.script_file_id("", "PlatformLayer"), 354539500)

    def test_probe_strips_callbacks_and_native_physics_without_touching_skin(self):
        text = ("%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Component:\n"
                "  - component: {fileID: 4}\n  - component: {fileID: 114}\n  - component: {fileID: 136}\n"
                "--- !u!4 &4\nTransform:\n  m_GameObject: {fileID: 1}\n"
                "--- !u!114 &114\nMonoBehaviour:\n  m_Script: {fileID: 1, guid: " + "1" * 32 + ", type: 3}\n"
                "--- !u!136 &136\nCapsuleCollider:\n  m_GameObject: {fileID: 1}\n"
                "--- !u!137 &137\nSkinnedMeshRenderer:\n  m_RootBone: {fileID: 4}\n")
        value, removed = probe_slice.strip_callbacks(text, True)
        self.assertEqual({x["classId"] for x in removed}, {114, 136})
        self.assertNotIn("m_Script:", value)
        self.assertNotIn("CapsuleCollider:", value)
        self.assertNotIn("component: {fileID: 114}", value)
        self.assertIn("SkinnedMeshRenderer:\n  m_RootBone: {fileID: 4}", value)

    def test_controller_callback_removal_retains_native_states_and_empty_array(self):
        text = ("%YAML 1.1\n--- !u!1102 &1102\nAnimatorState:\n  m_Name: Idle\n"
                "  m_StateMachineBehaviours:\n  - {fileID: 114}\n  m_Position: {x: 0}\n"
                "--- !u!114 &114\nMonoBehaviour:\n  m_Enabled: 1\n")
        value, removed = probe_slice.strip_callbacks(text, False)
        self.assertEqual(len(removed), 1)
        self.assertIn("m_Name: Idle", value)
        self.assertIn("m_StateMachineBehaviours: []", value)
        self.assertNotIn("MonoBehaviour:", value)

    def test_missing_essential_probe_mesh_hard_fails_instead_of_fake_model(self):
        project = self.root / "project"
        assets = project / "Assets"
        assets.mkdir(parents=True)
        prefab = assets / "Original.prefab"
        prefab.write_text("%YAML 1.1\n--- !u!137 &137\nSkinnedMeshRenderer:\n"
                          "  m_Mesh: {fileID: 4300000, guid: " + "1" * 32 + ", type: 2}\n"
                          "--- !u!95 &95\nAnimator:\n  m_Enabled: 1\n")
        prefab.with_name(prefab.name + ".meta").write_text("guid: " + "2" * 32 + "\n")
        with self.assertRaisesRegex(recover.RecoveryError, "Essential mesh"):
            probe_slice.create_slice(project, "Assets/Original.prefab", self.root / "slice")

    def test_serialized_layout_failures_remain_a_reported_blocker(self):
        log = self.root / "export.log"
        log.write_text("Import [Error] : Unable to read MonoBehaviour Structure, because script Example layout mismatched binary content.\n"
                       "Export : Finished exporting assets\n")
        proof = recover.audit_export_log(log)
        self.assertEqual(proof["monoBehaviourLayoutFailureCount"], 1)
        self.assertEqual(proof["status"], "blocked-serialized-behaviour-recovery")


if __name__ == "__main__":
    unittest.main()
