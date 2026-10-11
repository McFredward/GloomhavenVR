#!/usr/bin/env python3
"""Portable entry point for the local Quest builder."""

from pathlib import Path
import runpy
import sys

location = Path(__file__).resolve().parents[1] / "tools/quest-builder"
sys.path.insert(0, str(location))
runpy.run_path(str(location / "builder.py"), run_name="__main__")
