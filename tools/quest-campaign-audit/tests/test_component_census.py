"""Focused controls for exact serialized script identity evidence."""
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

MODULE = Path(__file__).resolve().parents[1] / "component_census.py"
SPEC = importlib.util.spec_from_file_location("quest_campaign_component_census", MODULE)
AUDIT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(AUDIT)


class CensusTests(unittest.TestCase):
    def test_exact_guid_and_file_id_exclude_unrelated_components(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "QuestRecovery").mkdir()
            (root / "Assets/Plugins").mkdir(parents=True)
            (root / "Assets/Scenes").mkdir()
            rows = [{"assembly": "PhotonVoice.dll", "namespace": "Photon.Voice.Unity",
                     "name": "WebRtcAudioDsp", "fileId": 929713116}]
            (root / "QuestRecovery/original-script-identities.json").write_text(json.dumps(rows))
            guid = "0123456789abcdef0123456789abcdef"
            (root / "Assets/Plugins/PhotonVoice.dll.meta").write_text("guid: " + guid + "\n")
            (root / "Assets/Scenes/Fixture.unity").write_text(
                "m_Script: {fileID: 929713116, guid: " + guid + ", type: 3}\n"
                "m_Script: {fileID: 929713116, guid: ffffffffffffffffffffffffffffffff, type: 3}\n"
                "m_Script: {fileID: 123, guid: " + guid + ", type: 3}\n")
            (root / "Assets/Scenes/ignored.txt").write_text("m_Script: {fileID: 929713116, guid: " + guid + "}\n")
            result = AUDIT.census(root)
            self.assertEqual(result["inspectedAssets"], 1)
            self.assertEqual(result["targets"][0]["serializedReferences"], 1)
            self.assertEqual(result["targets"][0]["sampleAssets"], ["Assets/Scenes/Fixture.unity"])
            self.assertIn("dynamic construction", result["evidenceLimit"])

    def test_missing_recovered_guid_fails_instead_of_claiming_unused(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            (root / "QuestRecovery").mkdir()
            (root / "Assets/Plugins").mkdir(parents=True)
            rows = [{"assembly": "PhotonVoice.dll", "namespace": "Photon.Voice.Unity",
                     "name": "WebRtcAudioDsp", "fileId": 929713116}]
            (root / "QuestRecovery/original-script-identities.json").write_text(json.dumps(rows))
            with self.assertRaisesRegex(ValueError, "no recovered GUID"):
                AUDIT.census(root)

    def test_all_capture_components_and_all_alembic_types_are_in_scope(self):
        self.assertTrue(AUDIT.concerned({"assembly": "GH.Runtime.FirstPass.dll",
            "namespace": "RenderHeads.Media.AVProMovieCapture", "name": "CaptureFromScreen"}))
        self.assertTrue(AUDIT.concerned({"assembly": "Unity.Formats.Alembic.Runtime.dll",
            "namespace": "UnityEngine.Formats.Alembic.Importer", "name": "AlembicStreamPlayer"}))
        self.assertFalse(AUDIT.concerned({"assembly": "GH.Runtime.dll", "namespace": "RenderHeads.Media.AVProMovieCapture",
            "name": "CaptureFromScreen"}))


if __name__ == "__main__":
    unittest.main()
