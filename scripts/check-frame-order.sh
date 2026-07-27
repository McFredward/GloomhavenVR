#!/usr/bin/env bash
# Thin wrapper around scripts/check-frame-order.py — see that file for the rationale.
#
#   scripts/check-frame-order.sh           fail if a locked per-frame order moved
#   scripts/check-frame-order.sh --list    print what is locked today
#
# Run by scripts/refactor-guard.sh before every build, because a reorder is exactly
# what that guard is structurally blind to (CHARTER §3b.2).
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$ROOT/scripts/check-frame-order.py" "$@"
