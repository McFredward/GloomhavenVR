#!/usr/bin/env python3
"""Run the original ritual proof with the narrowed local wrist-focus contract.

Reuses the existing native-callback and real Unity fixture. Adds one causal control
which restores build638's broad wrist election; unchanged donation, native UI and
shared physical-purse proof remain in the positive run. The three retained638
controls check that shrinking the radius cannot reintroduce shared gaze/disposal
ownership. This is a focused subset, not a new complete ritual suite pass.
"""
import importlib.util
from pathlib import Path
import sys

SCRIPT = Path(__file__).resolve().with_name('check-town-ritual-transactions.py')
spec = importlib.util.spec_from_file_location('ritual_transactions', SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
original_mutations = module.mutations
module.mutations = lambda: original_mutations() + [(
    'temple-broad-wrist-radius', 'TempleApproach.cs',
    '_purseFocus ? 1.65f : 1.45f', '_purseFocus ? 2.6f : 2.4f',
    'local purse radius respects close entry hysteresis without broad shared attention')]
if '--only-mutation' not in sys.argv:
    for name in ('temple-broad-wrist-radius', 'temple-shared-eye-wrist-coupling',
                 'temple-shared-eye-render-coupling', 'temple-retiring-wrist-false'):
        sys.argv.extend(('--only-mutation', name))
module.main()
