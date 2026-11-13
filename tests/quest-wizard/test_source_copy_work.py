"""Retained source qualification performs one read and still repairs corruption."""
import hashlib
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/quest-wizard'))
import provision
from state import Store
import wizard

class SourceCopyWorkTests(unittest.TestCase):
    def test_same_release_source_is_qualified_once_per_retained_file(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); source = root / 'release'; source.mkdir()
            raw = b'current authored builder source'
            (source / 'helper.py').write_bytes(raw)
            rows = [{'path': 'helper.py', 'size': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}]
            release = types.SimpleNamespace(verified_source_inventory=lambda _: (rows, 'a' * 40, False))
            builder = types.SimpleNamespace(source_inventory=lambda _: (rows, 'a' * 40, False))
            store = Store(root / 'owned'); saved = store.create(wizard.choices({'gameRoot': str(root / 'Game')}))
            def derived(checkout, *args):
                path = checkout / 'derived.json'; path.write_text('{}'); return path
            with patch.object(provision.discovery, 'local_support_module', return_value=release), \
                    patch.object(provision.discovery, 'builder', return_value=builder), \
                    patch.object(provision, 'derive_runtime_dependencies', side_effect=derived):
                _, details = provision.release_source(store, saved['session'], saved['choices'], {}, object(), source)
                retained = Path(details['sourceRoot']) / 'helper.py'
                with patch.object(provision, 'digest', wraps=provision.digest) as read, \
                        patch.object(provision.shutil, 'copyfile', wraps=provision.shutil.copyfile) as copy:
                    provision.release_source(store, saved['session'], saved['choices'], {}, object(), source)
                self.assertEqual([call.args[0] for call in read.call_args_list], [retained])
                copy.assert_not_called()
                retained.write_bytes(b'corrupted retained source')
                with patch.object(provision.shutil, 'copyfile', wraps=provision.shutil.copyfile) as copy:
                    provision.release_source(store, saved['session'], saved['choices'], {}, object(), source)
                self.assertEqual(retained.read_bytes(), raw)
                copy.assert_called_once()

if __name__ == '__main__': unittest.main()
