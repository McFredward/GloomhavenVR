"""Stream independent movie updates while retaining original compressed records."""
import copy
import hashlib
import json
from pathlib import Path
import sys
import unittest
from unittest.mock import patch
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-builder"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import apk_update as core
import storage
import test_apk_update as fixtures


class MovieUpdateFilesTests(unittest.TestCase):
    def setUp(self):
        self.fixture = fixtures.ApkUpdateTests("runTest")
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.movie = "Heroes/Doomstalker.sbs-v1.mov"
        self.intro = "CP_Intro/GH_CP_Intro.sbs-v2.mov"
        self.bank = "ghvr-quest-movies.bundle"
        self.metadata = {"schema": 1, "target": "Android", "producerKey": "3" * 64, "files": []}

    def replacements(self, names=None):
        result = {}
        manifest = copy.deepcopy(self.metadata)
        for index, name in enumerate(names or (self.movie, self.bank)):
            path = self.fixture.root / (str(index) + ".payload")
            payload = (b"owned local movie bytes\0" + name.encode()) * (50000 if name.endswith(".mov") else 17)
            path.write_bytes(payload)
            manifest["files"].append({"path": name, "size": len(payload), "sha256": hashlib.sha256(payload).hexdigest()})
            result[core.MOVIE_PREFIX + name] = path
        result[core.MOVIES] = storage.canonical(manifest)
        return result, manifest

    def test_new_movie_and_shader_stream_without_whole_file_reads(self):
        replacements, manifest = self.replacements()
        with patch.object(Path, "read_bytes", side_effect=AssertionError("No film may be buffered in RAM")):
            result = self.fixture.publish(replacements)
        with zipfile.ZipFile(self.fixture.output) as archive:
            self.assertEqual(json.loads(archive.read(core.MOVIES)), manifest)
            for name, path in replacements.items():
                if isinstance(path, Path):
                    self.assertEqual(hashlib.sha256(archive.read(name)).hexdigest(), storage.digest(path))
            self.assertEqual(archive.read(core.LIBRARY), bytes(self.fixture.library))
            self.assertEqual(archive.read(core.METADATA), self.fixture.metadata)
        self.assertIn(core.MOVIE_PREFIX + self.movie, result["replacedMembers"])
        for name in ("assets/bin/Data/level0", "assets/game-textures.resource"):
            self.assertEqual(self.fixture.compressed(self.fixture.base, name), self.fixture.compressed(self.fixture.output, name))
        self.assertEqual(core.inspect_base(self.fixture.output)["movieBanks"], manifest)

    def test_one_changed_movie_keeps_other_movie_and_original_compressed_bytes(self):
        replacements, manifest = self.replacements((self.movie, self.intro))
        self.fixture.values.update({name: path.read_bytes() if isinstance(path, Path) else path for name, path in replacements.items()})
        self.fixture.write_zip(self.fixture.base, self.fixture.values)
        changed = replacements[core.MOVIE_PREFIX + self.movie]
        changed.write_bytes(b"changed one film" * 10000)
        updated_manifest = copy.deepcopy(manifest)
        updated_manifest["producerKey"] = "4" * 64
        updated_manifest["files"][0].update(size=changed.stat().st_size, sha256=storage.digest(changed))
        self.fixture.publish({core.MOVIE_PREFIX + self.movie: changed, core.MOVIES: storage.canonical(updated_manifest)})
        self.assertEqual(self.fixture.compressed(self.fixture.base, core.MOVIE_PREFIX + self.intro),
                         self.fixture.compressed(self.fixture.output, core.MOVIE_PREFIX + self.intro))
        self.assertEqual(self.fixture.compressed(self.fixture.base, "assets/game-textures.resource"),
                         self.fixture.compressed(self.fixture.output, "assets/game-textures.resource"))
        # Missing payload cannot be certified by merely editing its manifest.
        updated_manifest["files"][1]["sha256"] = "5" * 64
        with self.assertRaisesRegex(storage.BuildError, "missing or changed replacement"):
            self.fixture.publish({core.MOVIE_PREFIX + self.movie: changed, core.MOVIES: storage.canonical(updated_manifest)})

    def test_movie_metadata_is_mandatory_and_file_hashes_are_proved_during_copy(self):
        replacements, manifest = self.replacements()
        self.fixture.output.write_bytes(b"previous successful APK")
        with self.assertRaisesRegex(storage.BuildError, "require their signed manifest"):
            self.fixture.publish({name: value for name, value in replacements.items() if name != core.MOVIES})
        manifest["files"][0]["sha256"] = "0" * 64
        replacements[core.MOVIES] = storage.canonical(manifest)
        with self.assertRaisesRegex(storage.BuildError, "Movie replacement changed during publication"):
            self.fixture.publish(replacements)
        self.assertEqual(self.fixture.output.read_bytes(), b"previous successful APK")

    def test_signed_movie_corruption_does_not_replace_previous_output(self):
        replacements, _ = self.replacements()
        self.fixture.output.write_bytes(b"previous successful APK")
        def corrupt(path):
            with zipfile.ZipFile(path) as archive:
                values = {name: archive.read(name) for name in archive.namelist()}
            values[core.MOVIE_PREFIX + self.movie] = b"broken payload"
            self.fixture.write_zip(path, values)
        with self.assertRaisesRegex(storage.BuildError, "Signed movie replacement identity"):
            core.repack(self.fixture.base, self.fixture.output, replacements, update_manifest=self.fixture.provenance,
                        signer=corrupt, verifier=lambda _: None)
        self.assertEqual(self.fixture.output.read_bytes(), b"previous successful APK")

    def test_invalid_movie_inventory_and_unqualified_names_are_rejected(self):
        replacements, manifest = self.replacements()
        for invalid in ({"path": "../outside.mov"}, {"size": -1}, {"sha256": "wrong"}):
            changed = copy.deepcopy(manifest)
            changed["files"][0].update(invalid)
            with self.subTest(invalid=invalid), self.assertRaisesRegex(storage.BuildError, "invalid file"):
                self.fixture.publish({**replacements, core.MOVIES: storage.canonical(changed)})
        duplicate = copy.deepcopy(manifest)
        duplicate["files"].append(duplicate["files"][0])
        with self.assertRaisesRegex(storage.BuildError, "invalid file"):
            self.fixture.publish({**replacements, core.MOVIES: storage.canonical(duplicate)})
        with self.assertRaisesRegex(storage.BuildError, "Unsafe APK replacement scope"):
            self.fixture.publish({"assets/QuestMovies/Heroes/Unreviewed.mov": b"unqualified"})
        with self.assertRaisesRegex(storage.BuildError, "bounded streamed file"):
            self.fixture.publish({**replacements, core.MOVIE_PREFIX + self.movie: b"buffered film"})


if __name__ == "__main__":
    unittest.main()
