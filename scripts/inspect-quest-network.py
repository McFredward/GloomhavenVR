#!/usr/bin/env python3
"""Inspect private original multiplayer prerequisites without contacting services."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools/quest-network"))
from audit import main

if __name__ == "__main__":
    raise SystemExit(main())
