import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('wizard_artwork', Path(__file__).resolve().parents[1] / 'artwork.py')
artwork = importlib.util.module_from_spec(spec)
spec.loader.exec_module(artwork)


class ArtworkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.project = Path(self.temp.name)
        self.key = 'a' * 64
        self.relative = 'Assets/Texture2D/Campaign_Background.png'
        self.data = b'\x89PNG\r\n\x1a\n' + struct.pack('>I', 13) + b'IHDR' + struct.pack('>II', 192, 128) + b'fixture-owned-by-tests'
        self.path = self.project / self.relative
        self.path.parent.mkdir(parents=True)
        self.path.write_bytes(self.data)
        self.evidence = {'schema': 1, 'target': 'campaign', 'sourceBuilderFingerprint': self.key,
                         'readiness': {'fullOriginalCatalogRecovered': True},
                         'files': [{'path': self.relative, 'size': len(self.data), 'sha256': hashlib.sha256(self.data).hexdigest()}]}
        self.save()

    def save(self):
        (self.project / 'quest-campaign-report.json').write_text(json.dumps(self.evidence), encoding='utf-8')

    def test_exact_source_witness_and_revalidated_response(self):
        rows = artwork.verified_project_artwork(self.project, self.key)
        self.assertEqual(len(rows), 1)
        self.assertEqual(rows[0]['width'], 192)
        self.assertEqual(rows[0]['altCode'], 'ownedArtwork')
        self.assertEqual(len(rows[0]['id']), 32)
        self.assertEqual(artwork.read_artwork(rows[0]), self.data)
        self.path.write_bytes(self.data[:-1] + b'X')
        self.assertIsNone(artwork.read_artwork(rows[0]))
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key), [])

    def test_wrong_owner_missing_or_partial_evidence_fails_closed(self):
        self.assertEqual(artwork.verified_project_artwork(self.project, 'b' * 64), [])
        self.evidence['readiness']['fullOriginalCatalogRecovered'] = False
        self.save()
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key), [])
        (self.project / 'quest-campaign-report.json').write_text('[]')
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key), [])

    def test_noncanonical_paths_and_symlinks_are_not_served(self):
        for relative in ['../outside.png', 'Assets/Texture2D/../Campaign_Background.png',
                         'Assets//Texture2D/Campaign_Background.png', 'Assets/./Texture2D/Campaign_Background.png',
                         '/Assets/Texture2D/Campaign_Background.png', 'Assets\\Texture2D\\Campaign_Background.png']:
            with self.subTest(path=relative):
                self.assertIsNone(artwork._owned_file(self.project, relative))
        original = self.path.read_bytes()
        self.path.unlink()
        outside = self.project / 'private.png'
        outside.write_bytes(original)
        self.path.symlink_to(outside)
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key), [])

    def test_limits_bad_dimensions_and_invalid_descriptor(self):
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key, limit=0), [])
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key, limit='bad'), [])
        self.assertEqual(artwork.verified_project_artwork(None, self.key), [])
        self.assertIsNone(artwork.read_artwork({'size': 100, 'projectRoot': None}))
        self.evidence['files'][0]['sha256'] = None
        self.save()
        self.assertEqual(artwork.verified_project_artwork(self.project, self.key), [])


if __name__ == '__main__':
    unittest.main()
