#!/usr/bin/env python3
"""Run one portable Quest conversion contract suite without original game files."""
import argparse
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("suite", choices=("builder", "recovery", "installer", "network"))
args = parser.parse_args()
suite = unittest.defaultTestLoader.discover(str(ROOT / "tests" / ("quest-" + args.suite)), pattern="test_*.py")
result = unittest.TextTestRunner(verbosity=1).run(suite)
sys.exit(0 if result.wasSuccessful() else 1)
