"""Finite figure producer identity and cheap durable file witnesses."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import figure_banks as figures
import storage


class FigureBankTests(unittest.TestCase):
    def test_timestamp_refresh_is_hash_qualified_once_then_persisted(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); part = root / 'bank'; part.write_bytes(b'known-native-derived-part')
            row = storage.record_file(part, 'bank'); row['stamp'] = figures._stamp(part)
            storage.write_json(root / 'completion.json', {'plan': {'key': 'source'}, 'files': [row]})
            observed = part.stat(); os.utime(part, ns=(observed.st_atime_ns, observed.st_mtime_ns + 1000000000))
            with patch.object(figures, 'record_file', wraps=storage.record_file) as hashed:
                self.assertIsNotNone(figures._receipt(root, {'key': 'source'})); self.assertEqual(hashed.call_count, 1)
                self.assertIsNotNone(figures._receipt(root, {'key': 'source'})); self.assertEqual(hashed.call_count, 1)
            part.write_bytes(b'corrupt-same-length-bank!')
            self.assertIsNone(figures._receipt(root, {'key': 'source'}))

    def test_original_identity_changes_recipe_does_not_include_unrelated_host_code(self):
        row = {'path': figures.SOURCE_ROOT + 'hero_example_assets_all.bundle', 'sha256': 'a' * 64, 'size': 44}
        first = figures.plan(ROOT, {'files': [row]})
        self.assertEqual(first, figures.plan(ROOT, {'files': [row]}))
        self.assertNotEqual(first['producerKey'], figures.plan(ROOT, {'files': [{**row, 'sha256': 'b' * 64}]})['producerKey'])
        self.assertFalse(any(name in json.dumps(first['recipe']) for name in ('update_driver.py', 'NetProtocol.cs', 'QuestGameBootstrap.cs')))
        self.assertEqual(first['recipe']['tiers'], [75, 45, 20, 5])
        self.assertEqual(first['recipe']['target'], 'Android')

    def test_only_original_flat_native_bundle_paths_are_admitted(self):
        for name in ('hero_x/../hero_example_assets_all.bundle', 'hero_x\\bad_assets_all.bundle'):
            with self.assertRaises(storage.BuildError):
                figures.plan(ROOT, {'files': [{'path': figures.SOURCE_ROOT + name, 'sha256': 'a' * 64, 'size': 44}]})


if __name__ == '__main__': unittest.main()
