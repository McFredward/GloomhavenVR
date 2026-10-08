#!/usr/bin/env python3
"""Connect the remembered Quest over Wi-Fi and update its latest local APK."""
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools/quest-installer"))
from installer import main

if __name__ == "__main__":
    raise SystemExit(main())
