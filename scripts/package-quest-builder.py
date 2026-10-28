"""Assemble the public Windows and Linux Quest builder, without owned game files."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools/quest-builder'))
from release import main
if __name__ == '__main__': raise SystemExit(main())
