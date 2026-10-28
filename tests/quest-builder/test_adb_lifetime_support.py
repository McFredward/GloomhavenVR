"""Own ADB lifetime evidence remains bounded and available from every view."""
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
import support
from storage import write_json


class AdbLifetimeSupportTests(unittest.TestCase):
    def test_home_and_run_export_keep_only_two_recent_guard_logs(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            write_json(root / 'wizard-owner.json', {'schema': 1, 'owner': 'GloomhavenVR.QuestWizard'})
            session = 'a' * 32
            write_json(root / 'sessions' / session / 'state.json', {'session': session, 'status': 'cancelled', 'stages': []})
            logs = root / 'logs'; logs.mkdir()
            names = []
            for index in range(4):
                name = 'adb-lifetime-' + str(index) * 12 + '.log'; names.append(name)
                path = logs / name
                path.write_text('server-started owned endpoint\naccess_token=PRIVATE TOKEN\n')
                os.utime(path, ns=(1_000_000_000 + index, 1_000_000_000 + index))
            (logs / 'adb-lifetime-untrusted.log').write_text('UNRELATED MUST NOT SHIP')
            actual_digest = support.digest
            def diagnostic_only(path):
                self.assertTrue(Path(path).name.startswith('quest-build-support-') and Path(path).suffix == '.zip')
                return actual_digest(path)
            with patch.object(support, 'digest', side_effect=diagnostic_only):
                for selected in (None, session):
                    result = support.export_support(root, selected)
                    with zipfile.ZipFile(result['path']) as archive:
                        included = [name for name in archive.namelist() if 'adb-lifetime-' in name]
                        self.assertEqual(set(included), {'wizard/' + name for name in names[-2:]})
                        text = '\n'.join(archive.read(name).decode() for name in included)
                        self.assertIn('owned endpoint', text)
                        self.assertNotIn('PRIVATE TOKEN', text)
                        self.assertNotIn('UNRELATED MUST NOT SHIP', text)
                        self.assertEqual(json.loads(archive.read('diagnostic.json'))['scope'], 'workspace' if selected is None else 'session')

    def test_linked_lifetime_log_is_not_followed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            logs = root / 'logs'; logs.mkdir()
            target = root / 'private.log'; target.write_text('NOT OWNED EVIDENCE')
            try: (logs / ('adb-lifetime-' + 'a' * 12 + '.log')).symlink_to(target)
            except (OSError, NotImplementedError): self.skipTest('Symlinks unavailable')
            self.assertEqual(support.adb_lifetime_logs(root), [])


if __name__ == '__main__': unittest.main()
