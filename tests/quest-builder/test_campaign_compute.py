"""Exercise delivery closure across actual ZIP entries and repeated compiled objects."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import campaign_compute
from storage import BuildError


class DeliveredComputeTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.apk, self.bank = self.root / "player.apk", self.root / "content.zip"
        self.a = {"m_Name": "EyeHistogram", "variants": ["native GLES bytes A"]}
        self.b = {"m_Name": "Waveform", "variants": ["native GLES bytes B"]}
        self.manifest = {"shaders": [{"name": row["m_Name"]} for row in (self.a, self.b)]}

    def tearDown(self):
        self.temp.cleanup()

    def write(self, bank):
        with zipfile.ZipFile(self.apk, "w") as archive:
            archive.writestr("assets/bin/Data/resources.assets", json.dumps([self.a]))
            archive.writestr("assets/unrelated.json", "not a serialized player asset")
        with zipfile.ZipFile(self.bank, "w") as archive:
            archive.writestr("StreamingAssets/aa/Android/native.bundle", json.dumps(bank))
            archive.writestr("StreamingAssets/Rulebase/unused.yml", "not a native bank")

    def collect(self):
        return campaign_compute.collect_delivered(self.apk, [self.bank], self.manifest, json.loads)

    def test_reads_both_delivered_sources_and_deduplicates_exact_objects(self):
        self.write([self.a, self.b])
        objects, proof = self.collect()
        self.assertEqual({row["m_Name"] for row in objects}, {"EyeHistogram", "Waveform"})
        self.assertEqual(proof["actualSerializedPayloadCount"], 2)
        self.assertEqual(len(proof["actualObjectOrigins"]), 3)

    def test_missing_object_cannot_pass_a_valid_apk_and_bank(self):
        self.write([self.a])
        with self.assertRaisesRegex(BuildError, "omit original.*Waveform"):
            self.collect()

    def test_conflicting_cooked_duplicates_are_rejected(self):
        self.write([{**self.a, "variants": ["different native bytes"]}, self.b])
        with self.assertRaisesRegex(BuildError, "different compiled programs"):
            self.collect()

    def test_unowned_object_is_not_ignored(self):
        self.write([self.b, {"m_Name": "InventedReplacement"}])
        with self.assertRaisesRegex(BuildError, "unowned compute"):
            self.collect()


if __name__ == "__main__":
    unittest.main()
