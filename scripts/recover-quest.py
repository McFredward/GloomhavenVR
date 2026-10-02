#!/usr/bin/env python3
"""Recover an owned Unity installation into an untracked local Quest project."""
from pathlib import Path
import runpy
import sys

if __name__ == "__main__":
    tool = Path(__file__).resolve().parents[1] / "tools/quest-recovery"
    sys.path.insert(0, str(tool))
    runpy.run_path(str(tool / "recover.py"), run_name="__main__")
