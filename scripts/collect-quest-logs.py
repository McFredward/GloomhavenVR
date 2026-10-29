#!/usr/bin/env python3
"""Collect private Quest diagnostics to a timestamped local ZIP."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools/quest-installer"))
from collector import main

if __name__ == "__main__":
    raise SystemExit(main())
