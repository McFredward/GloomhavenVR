#!/usr/bin/env bash
# Run production policy/adapter against deterministic clocks and monitor boundaries.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
python3 "$root/scripts/test-perf-figure-measurement.py"
