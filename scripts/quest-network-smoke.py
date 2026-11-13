#!/usr/bin/env python3
"""Explicitly run the original Photon connection on a desktop Unity editor."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools/quest-network"))
from smoke import main

if __name__ == "__main__":
    raise SystemExit(main())
