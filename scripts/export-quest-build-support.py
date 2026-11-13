"""Save the latest local Wizard's bounded, redacted diagnostic ZIP."""
from pathlib import Path
import sys
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools/quest-builder'))
from support import main
if __name__ == '__main__': raise SystemExit(main())
