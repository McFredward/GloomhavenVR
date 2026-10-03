"""Asset-set corruption must fail before a release or developer install writes files."""
import importlib.util
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
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


@unittest.skipUnless(shutil.which('pwsh') or shutil.which('powershell'),
                     'PowerShell is required to execute the Windows installer preflight')
class PowerShellFigureBankTests(unittest.TestCase):
    """Execute the unmodified complete installer block, not a Python regex copy.

    The extracted block has its own source hash for review. A second test invokes
    the entire install.ps1 with a deliberately missing game, proving that the real preflight
    accepts the shipped bank and stops before build/download/game writes.
    """

    @classmethod
    def setUpClass(cls):
        cls.shell = shutil.which('pwsh') or shutil.which('powershell')
        cls.temp = tempfile.TemporaryDirectory(prefix='ghvr-figure-ps-')
        cls.addClassCleanup(cls.temp.cleanup)
        cls.folder = Path(cls.temp.name)
        source = (ROOT / 'scripts/install.ps1').read_text(encoding='utf-8-sig')
        start = '# Prepared native figure derivatives ship as independently indexed parts.'
        end = '\nWrite-Host "Asset bundle source: $bundle"'
        if source.count(start) != 1 or source.count(end) != 1:
            raise AssertionError('Complete PowerShell figure preflight extraction boundary drifted')
        begin = source.index(start)
        cls.block = source[begin:source.index(end, begin)]
        cls.harness = cls.folder / 'original-preflight.ps1'
        cls.harness.write_text(
            'param([Parameter(Mandatory=$true)][string]$RepoRoot)\n'
            '$ErrorActionPreference = "Stop"\n$root = $RepoRoot\n' + cls.block,
            encoding='utf-8')
        shipped = json.loads((ROOT / 'prebuilt/ghvr-figure-meshes-index.json').read_text())['entries']
        cls.shipped_report = f"{len(shipped)} derivatives in {len({row['bank'] for row in shipped})} indexed parts."
        print('PowerShell figure preflight SHA256: ' + hashlib.sha256(cls.block.encode()).hexdigest())

    def setUp(self):
        self.fixture = tempfile.TemporaryDirectory(dir=self.folder)
        self.addCleanup(self.fixture.cleanup)
        self.repo = Path(self.fixture.name)
        self.prebuilt = self.repo / 'prebuilt'
        self.prebuilt.mkdir()
        self.legacy = 'ghvr-figure-meshes-20-00.bundle'
        self.distance = 'ghvr-figure-meshes-distance-00.bundle'
        self.entries = [
            {'mesh': 'figure-0123456789abcdef-20', 'bank': self.legacy},
            {'mesh': 'figure-0123456789abcdef-5', 'bank': self.distance},
            {'mesh': 'figure-0123456789abcdef-75', 'bank': self.distance},
        ]
        self.header = b'UnityFS\0' + (7).to_bytes(4, 'big') + b'5.x.x\x002021.3.5f1\0' + b'\0'*40
        for name in (self.legacy, self.distance):
            (self.prebuilt / name).write_bytes(self.header)
        self.write()

    def write(self):
        (self.prebuilt / 'ghvr-figure-meshes-index.json').write_text(
            json.dumps({'entries': self.entries}), encoding='utf-8')

    def invoke(self, repo=None):
        return subprocess.run(
            [self.shell, '-NoLogo', '-NoProfile', '-NonInteractive', '-File',
             str(self.harness), '-RepoRoot', str(repo or self.repo)],
            text=True, capture_output=True, timeout=30,
            env=dict(os.environ, NO_COLOR='1', TERM='dumb'))

    def reject(self, message):
        result = self.invoke()
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(message, result.stdout + result.stderr)
        self.assertNotIn('Figure mesh sources:', result.stdout)

    def test_actual_shipped_bank_passes_original_powershell_block(self):
        result = self.invoke(ROOT)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(self.shipped_report, result.stdout)

    def test_mixed_distance_parts_and_numeric_far_part(self):
        far = 'ghvr-figure-meshes-5-00.bundle'
        (self.prebuilt / far).write_bytes(self.header)
        self.entries.append({'mesh': 'figure-fedcba9876543210-5', 'bank': far})
        self.write()
        result = self.invoke()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('4 derivatives in 3 indexed parts.', result.stdout)

    def test_legacy_numeric_part_still_requires_exact_tier(self):
        self.entries[0]['mesh'] = 'figure-fedcba9876543210-45'
        self.write()
        self.reject('Invalid figure mesh part filename or tier.')

    def test_unknown_tier_remains_invalid(self):
        self.entries[1]['mesh'] = 'figure-fedcba9876543210-10'
        self.write()
        self.reject('Invalid figure mesh identity.')

    def test_invalid_part_filename(self):
        self.entries[1]['bank'] = '../' + self.distance
        self.write()
        self.reject('Invalid figure mesh part filename or tier.')

    def test_duplicate_identity(self):
        self.entries.append(dict(self.entries[1]))
        self.write()
        self.reject('Duplicate figure mesh identity.')

    def test_missing_part(self):
        (self.prebuilt / self.distance).unlink()
        self.reject('Missing or orphan figure mesh parts.')

    def test_orphan_part(self):
        (self.prebuilt / 'ghvr-figure-meshes-distance-01.bundle').write_bytes(self.header)
        self.reject('Missing or orphan figure mesh parts.')

    def test_same_count_foreign_part_cannot_replace_missing_part(self):
        (self.prebuilt / self.distance).rename(self.prebuilt / 'ghvr-figure-meshes-distance-01.bundle')
        self.reject('Orphan figure mesh part:')

    def test_wrong_bundle_headers(self):
        variants = {
            'signature': b'Foreign\0' + self.header[8:],
            'wrapper': self.header[:8] + (8).to_bytes(4, 'big') + self.header[12:],
            'editor': self.header.replace(b'2021.3.5f1', b'2021.3.45f1'),
            'truncated': self.header[:28],
        }
        for label, header in variants.items():
            with self.subTest(label=label):
                (self.prebuilt / self.distance).write_bytes(header)
                self.reject('Incompatible figure mesh part:')

    def test_complete_installer_passes_current_bank_before_read_only_missing_game_stop(self):
        game = self.repo / 'untouched-game'
        game.mkdir()
        sentinel = game / 'untouched.txt'
        sentinel.write_text('preserve game files', encoding='utf-8')
        props = ROOT / 'Directory.Build.props.user'
        props_before = props.read_bytes() if props.is_file() else None
        # No GH.exe exists, so the full script must stop before game/dependency writes.
        # Keep PATH: PowerShell's .NET global-tool launcher also needs dotnet to start.
        # A machine without the build SDK may stop at its earlier toolchain guard.
        result = subprocess.run(
            [self.shell, '-NoLogo', '-NoProfile', '-NonInteractive', '-File',
             str(ROOT / 'scripts/install.ps1'), '-GamePath', str(game), '-NoPackage'],
            text=True, capture_output=True, timeout=30,
            env=dict(os.environ, NO_COLOR='1', TERM='dumb'))
        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn(self.shipped_report, result.stdout)
        combined = result.stdout + result.stderr
        self.assertTrue(any(message in combined for message in (
            '.NET SDK not found.', 'git not found.', 'No compatible .NET SDK can be selected',
            'Gloomhaven install (GH.exe missing).')), combined)
        self.assertNotIn('Configuring game references', result.stdout)
        self.assertEqual(list(game.iterdir()), [sentinel])
        self.assertEqual(sentinel.read_text(encoding='utf-8'), 'preserve game files')
        self.assertEqual(props.read_bytes() if props.is_file() else None, props_before)


if __name__ == '__main__':
    unittest.main()
