"""Asset-set corruption must fail before a release or developer install writes files."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('figure_bank_check', ROOT / 'scripts/check-figure-mesh-bank.py')
validator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validator)


class FigureBankTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.name = 'ghvr-figure-meshes-20-00.bundle'
        self.entry = {'mesh': 'figure-0123456789abcdef-20', 'bank': self.name}
        (self.folder / self.name).write_bytes(b'UnityFS\0' + (7).to_bytes(4, 'big') + b'5.x.x\x002021.3.5f1\0' + b'\0'*40)
        self.write([self.entry])

    def write(self, entries):
        (self.folder / 'ghvr-figure-meshes-index.json').write_text(json.dumps({'entries': entries}))

    def reject(self):
        with self.assertRaises((ValueError, OSError)):
            validator.validate(self.folder)

    def test_game_exact_complete_set(self):
        self.assertEqual(validator.validate(self.folder), (1, 1))

    def test_missing_part(self):
        (self.folder / self.name).unlink()
        self.reject()

    def test_unindexed_part(self):
        (self.folder / 'ghvr-figure-meshes-20-01.bundle').write_bytes(b'unknown')
        self.reject()

    def test_duplicate_mesh(self):
        self.write([self.entry, self.entry])
        self.reject()

    def test_wrong_tier(self):
        self.entry['mesh'] = 'figure-0123456789abcdef-75'
        self.write([self.entry])
        self.reject()

    def test_path_escape(self):
        self.entry['bank'] = '../' + self.name
        self.write([self.entry])
        self.reject()

    def test_newer_editor_wrapper(self):
        (self.folder / self.name).write_bytes(b'UnityFS\0' + (8).to_bytes(4, 'big') + b'5.x.x\x002021.3.45f1\0')
        self.reject()

    def test_foreign_editor(self):
        (self.folder / self.name).write_bytes(b'UnityFS\0' + (7).to_bytes(4, 'big') + b'5.x.x\x002021.3.45f1\0')
        self.reject()

    def test_both_packagers_require_index_and_parts(self):
        for script in ['install.ps1', 'package-release.sh']:
            source = (ROOT / 'scripts' / script).read_text()
            self.assertIn('ghvr-figure-meshes-index.json', source)
            self.assertIn('ghvr-figure-meshes-*.bundle', source)
        self.assertIn('check-figure-mesh-bank.py', (ROOT / 'scripts/check-bundle-format.sh').read_text())


if __name__ == '__main__':
    unittest.main()
