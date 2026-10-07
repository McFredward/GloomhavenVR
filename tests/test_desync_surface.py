"""Prevent inventory ownership errors from hiding new network receiver patches."""
import contextlib
import importlib.util
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('desync_surface', ROOT / 'scripts/check-desync-surface.py')
SURFACE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(SURFACE)


class ReceiverOwnershipTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        folder = Path(self.directory.name)
        self.inventory = folder / 'inventory.md'
        self.ledger = folder / 'ledger.md'
        self.addCleanup(patch.stopall)
        patch.object(SURFACE, 'INVENTORY', self.inventory).start()
        patch.object(SURFACE, 'LEDGER', self.ledger).start()

    def write_inventory(self, rows):
        self.inventory.write_text('\n'.join(rows), encoding='utf-8')

    def test_nested_receivers_do_not_attach_to_previous_nonreceiver(self):
        self.write_inventory([
            '| `GamepadGuard`<sub>Guard.cs:1</sub> | `GamepadConnectionBox.Update()` | prefix |',
            '| `Departure.PlayerLeftSeam`<sub>Departure.cs:2</sub> | `UIReadyToggle.OnPlayerLeft()` | prefix |',
            '| &nbsp; | `UIReadyToggle.OnPlayerLeft()` | postfix |',
            '| `Outer.Inner.ReadySeam`<sub>Ready.cs:3</sub> | `UIReadyToggle.ReadyUp()` | prefix |',
        ])
        self.assertEqual(SURFACE.patched_classes(), {
            'Departure.PlayerLeftSeam': ('Departure.cs', {'UIReadyToggle'}),
            'Outer.Inner.ReadySeam': ('Ready.cs', {'UIReadyToggle'}),
        })

    def test_only_explicit_continuation_marker_inherits_owner(self):
        self.write_inventory([
            '| `ExistingPatch`<sub>Existing.cs:1</sub> | `CardsHandManager.ShowHands()` | prefix |',
            '| &nbsp; | `Choreographer.ProcessMessage()` | postfix |',
            '| `Unrecognized-Patch`<sub>Other.cs:2</sub> | `UIReadyToggle.ReadyUp()` | prefix |',
            '| &nbsp; | `UIReadyToggle.Reset()` | postfix |',
            '| `FollowingPatch`<sub>Following.cs:3</sub> | `SceneController.LoadScene()` | prefix |',
            '| | `UIReadyToggle.Initialize()` | prefix |',
        ])
        self.assertEqual(SURFACE.patched_classes(), {
            'ExistingPatch': ('Existing.cs', {'CardsHandManager', 'Choreographer'}),
            'FollowingPatch': ('Following.cs', {'SceneController'}),
        })

    def test_each_qualified_seam_requires_its_own_review(self):
        seams = ['Initialize', 'PlayerLeft', 'ExplicitInput', 'ProgressEnd',
                 'CancelProgress', 'ReadyUp', 'Reset']
        self.write_inventory([
            f'| `Departure.{seam}Seam`<sub>Departure.cs:{index + 1}</sub> | `UIReadyToggle.Run()` | prefix |'
            for index, seam in enumerate(seams)
        ])
        rows = [f'| `Departure.{seam}Seam` | UIReadyToggle | **SELF-GUARDED** | Reviewed |'
                for seam in seams]
        self.ledger.write_text('\n'.join(rows[:-1]), encoding='utf-8')
        error = io.StringIO()
        with contextlib.redirect_stderr(error):
            self.assertEqual(SURFACE.main(), 1)
        self.assertIn('Departure.ResetSeam', error.getvalue())
        self.assertNotIn('Departure.PlayerLeftSeam', error.getvalue())
        self.ledger.write_text('\n'.join(rows), encoding='utf-8')
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(SURFACE.main(), 0)

    def test_short_and_qualified_ledger_names_remain_distinct(self):
        self.ledger.write_text(
            '| `ReadySeam` | UIReadyToggle | **CANNOT-THROW** | Fields |\n'
            '| `Outer.ReadySeam` | UIReadyToggle | **SELF-GUARDED** | Probe |\n', encoding='utf-8')
        self.assertEqual(SURFACE.ledger_verdicts(), {
            'ReadySeam': 'CANNOT-THROW', 'Outer.ReadySeam': 'SELF-GUARDED',
        })


if __name__ == '__main__':
    unittest.main()
