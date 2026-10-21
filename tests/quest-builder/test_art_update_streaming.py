"""Large authored mod banks remain file-backed during compact APK publication."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import Mock, patch
import zipfile
from types import SimpleNamespace
import shutil

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import apk_update as core
import storage
import update_driver as driver
import test_apk_update as fixtures


class ArtUpdateStreamingTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.ApkUpdateTests("runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.archive = self.fixture.root / "quest-mod-content.zip"
        payload = b"current authored bank bytes" * 100000
        member = "StreamingAssets/gloomhavenvr.bundle"
        with zipfile.ZipFile(self.archive, "w") as archive:
            archive.writestr(member, payload)
        self.mod = {"schema": 1, "inputKey": self.fixture.input["inputKey"], "archive": self.archive.name,
                    "archiveSha256": storage.digest(self.archive),
                    "files": [{"path": member, "size": len(payload), "sha256": hashlib.sha256(payload).hexdigest()}]}
        self.installation = {"schema": 1, "inputKey": self.fixture.input["inputKey"],
                             "game": {"schema": 1, "archiveSha256": "5" * 64}, "mod": {"previous": True}}
        self.fixture.values[core.INSTALLATION] = storage.canonical(self.installation)
        self.resource = "assets/bin/Data/resources.assets"
        self.fixture.values[self.resource] = b"original Resource fixture; native registry has a separate suite"
        self.fixture.write_zip(self.fixture.base, self.fixture.values)
        self.resource_bytes = b"qualified new mod TextAsset fixture; serialized writer tested separately"
        stamp = core._base_stamp(self.fixture.base)
        proof = stamp, self.resource, hashlib.sha256(self.resource_bytes).hexdigest()
        core._resource_proofs.add(proof)
        core._resource_value_proofs[proof] = {"quest-mod-content": hashlib.sha256(core._json_bytes(self.mod)).hexdigest()}
        self.addCleanup(core._resource_proofs.discard, proof)
        self.addCleanup(core._resource_value_proofs.pop, proof, None)
        self.replacements = {core.MOD_ARCHIVE: self.archive, self.resource: self.resource_bytes,
                             core.INSTALLATION: storage.canonical({**self.installation, "mod": self.mod})}

    def test_art_bank_streams_with_exact_manifest_and_keeps_original_game(self):
        with patch.object(Path, "read_bytes", side_effect=AssertionError("Do not buffer the complete authored bank")):
            result = self.fixture.publish(self.replacements)
        with zipfile.ZipFile(self.fixture.output) as archive:
            self.assertEqual(hashlib.sha256(archive.read(core.MOD_ARCHIVE)).hexdigest(), self.mod["archiveSha256"])
            self.assertEqual(json.loads(archive.read(core.INSTALLATION))["game"], self.installation["game"])
        self.assertIn(core.MOD_ARCHIVE, result["replacedMembers"])
        self.assertEqual(self.fixture.compressed(self.fixture.base, "assets/game-textures.resource"),
                         self.fixture.compressed(self.fixture.output, "assets/game-textures.resource"))

    def test_streamed_art_cannot_change_game_contract_or_bypass_resource_proof(self):
        changed = copy.deepcopy(self.installation)
        changed.update(mod=self.mod)
        changed["game"]["archiveSha256"] = "0" * 64
        self.fixture.output.write_bytes(b"previous published APK")
        with self.assertRaisesRegex(storage.BuildError, "changed the original game"):
            self.fixture.publish({**self.replacements, core.INSTALLATION: storage.canonical(changed)})
        with self.assertRaisesRegex(storage.BuildError, "Resources TextAsset proof"):
            self.fixture.publish({name: value for name, value in self.replacements.items() if name != self.resource})
        self.assertEqual(self.fixture.output.read_bytes(), b"previous published APK")

    def test_damaged_art_source_is_rejected_without_replacing_prior_apk(self):
        self.fixture.output.write_bytes(b"previous published APK")
        self.archive.write_bytes(b"damaged archive")
        with self.assertRaisesRegex(storage.BuildError, "Mod archive replacement changed during publication"):
            self.fixture.publish(self.replacements)
        self.assertEqual(self.fixture.output.read_bytes(), b"previous published APK")

    def delivery_fixture(self):
        output = self.fixture.root / "work"
        inputs = {"target": "game", "inputKey": self.fixture.input["inputKey"],
                  "game": {"key": "6" * 64}, "mod": {"files": [
                      {"path": "unity/GloomhavenVR.Assets/Assets/board.prefab", "size": 10, "sha256": "7" * 64}]}}
        tools = {"editor": str(self.fixture.root / "Unity"), "editorSha256": "8" * 64}
        def package(project, selected, _output, _source, _editor, *, owned_game_source):
            self.assertEqual(owned_game_source, self.fixture.game)
            archive = project / "Assets/StreamingAssets/quest-mod-content.zip"
            archive.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.archive, archive)
            storage.write_json(project / "Assets/Quest/Resources/quest-mod-content.json", self.mod)
            storage.write_json(project / "Assets/Quest/Resources/quest-mod-bundles.json", {"nativeBankProof": "fixture"})
            return self.mod
        builder = SimpleNamespace(Stages=storage.Stages, value_hash=storage.value_hash, digest=storage.digest,
                                  _ordinary_owned=storage._ordinary_owned, BuildError=storage.BuildError,
                                  mod_bank_recipe=Mock(return_value={"actualRecipe": "unchanged"}),
                                  package_mod_content=Mock(side_effect=package))
        return builder, inputs, output, tools

    def test_late_signing_failure_reuses_closed_art_without_validation_or_repack(self):
        builder, inputs, output, tools = self.delivery_fixture()
        with self.assertRaisesRegex(storage.BuildError, "later signing failed"):
            first = driver.authored_art_delivery(builder, inputs, output, self.fixture.root, self.fixture.game, tools)
            raise storage.BuildError("later signing failed")
        self.assertEqual(builder.package_mod_content.call_count, 1)
        # New host/code-only source identity does not affect the art producer.
        selected = copy.deepcopy(inputs)
        selected["mod"]["files"].append({"path": "src/GloomhavenVR/Net/NetProtocol.cs", "size": 12, "sha256": "9" * 64})
        with patch.object(builder, "package_mod_content", side_effect=AssertionError("Do not validate/repack completed art")), \
             patch.object(storage, "digest", side_effect=AssertionError("Warm stage must use its native file witnesses")):
            retained = driver.authored_art_delivery(builder, selected, output, self.fixture.root / "new-host-source", self.fixture.game, tools)
        self.assertEqual(first, retained)
        self.assertEqual(first[0].read_bytes(), self.archive.read_bytes())

    def test_damaged_archive_repeats_only_its_own_delivery(self):
        builder, inputs, output, tools = self.delivery_fixture()
        archive, _, _ = driver.authored_art_delivery(builder, inputs, output, self.fixture.root, self.fixture.game, tools)
        archive.write_bytes(b"damaged retained archive")
        restored = driver.authored_art_delivery(builder, inputs, output, self.fixture.root, self.fixture.game, tools)
        self.assertEqual(restored[0].read_bytes(), self.archive.read_bytes())
        self.assertEqual(builder.package_mod_content.call_count, 2)

    def test_real_art_or_editor_changes_get_independent_producer_scope(self):
        builder, inputs, output, tools = self.delivery_fixture()
        first = driver.authored_art_delivery(builder, inputs, output, self.fixture.root, self.fixture.game, tools)
        changed = copy.deepcopy(inputs)
        changed["mod"]["files"][0]["sha256"] = "0" * 64
        second = driver.authored_art_delivery(builder, changed, output, self.fixture.root, self.fixture.game, tools)
        third = driver.authored_art_delivery(builder, changed, output, self.fixture.root, self.fixture.game,
                                            {**tools, "editorSha256": "1" * 64})
        self.assertEqual(len({first[0], second[0], third[0]}), 3)
        self.assertEqual(builder.package_mod_content.call_count, 3)


if __name__ == "__main__":
    unittest.main()
