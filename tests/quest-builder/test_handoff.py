"""Exercise the Windows archive's actual provenance and installer payload."""

import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
import handoff
from storage import BuildError, digest, record_file, write_json


class HandoffTests(unittest.TestCase):
    def test_archive_carries_bound_evidence_and_actual_installer_hashes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo, output, destination = (root / part for part in ("repo", "output", "hardware"))
            apk = output / "builds/player.apk"
            apk.parent.mkdir(parents=True)
            inputs = {"inputKey": "frozen-input", "target": "game", "mod": {"modBuild": 623}}
            with zipfile.ZipFile(apk, "w") as archive:
                archive.writestr("assets/Quest/input-manifest.json", json.dumps(inputs))
            metadata = {"inputKey": inputs["inputKey"], "target": inputs["target"]}
            write_json(Path(str(apk) + ".build.json"), metadata)
            provenance = apk.parent / "build-provenance.json"
            proof = {"schema": 1, "runtime": {"sourceCommit": "frozen-source"}}
            write_json(provenance, proof)
            bank = apk.parent / "GloomhavenVR-Quest-content.zip"
            with zipfile.ZipFile(bank, "w") as archive:
                archive.writestr("owned-content", b"local test fixture")
            for prefix in ("install-quest-wireless", "collect-quest-logs", "quest-saves"):
                for suffix in (".py", ".ps1", ".cmd"):
                    file = repo / "scripts" / (prefix + suffix)
                    file.parent.mkdir(parents=True, exist_ok=True)
                    file.write_text("fixture " + file.name)
            for directory in ("tools/quest-installer", "tools/quest-builder"):
                file = repo / directory / "fixture.py"
                file.parent.mkdir(parents=True)
                file.write_text("fixture source")
            details = {"apkSha256": digest(apk), "buildReport": metadata, "buildProvenance": proof,
                       "contentFiles": [record_file(bank, bank.relative_to(output).as_posix())],
                       "buildEvidenceFiles": [record_file(provenance, provenance.relative_to(output).as_posix())]}
            packaged = handoff.package(repo, output, apk, details, destination)
            with zipfile.ZipFile(packaged) as archive:
                self.assertIsNone(archive.testzip())
                base = "GloomhavenVR-Quest-Test/"
                payload = base + ".planning/debug/quest3/"
                saved = json.loads(archive.read(payload + "handoff.json"))
                self.assertEqual(saved["buildProvenance"], proof)
                self.assertEqual(json.loads(archive.read(payload + "build-provenance.json")), proof)
                self.assertEqual(saved["buildEvidenceFiles"][0]["path"], "build-provenance.json")
                self.assertEqual(len(saved["packageSourceFiles"]), 11)
                import hashlib
                for row in saved["packageSourceFiles"]:
                    actual = archive.read(base + row["path"])
                    self.assertEqual(hashlib.sha256(actual).hexdigest(), row["sha256"])
                    self.assertEqual(len(actual), row["size"])
            provenance.write_text("corrupted proof")
            with self.assertRaisesRegex(BuildError, "copy differs"):
                handoff.package(repo, output, apk, details, destination)


if __name__ == "__main__":
    unittest.main()
