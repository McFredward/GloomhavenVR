#!/usr/bin/env bash
# Thin wrapper around scripts/patch-inventory.py — see that file for the rationale.
#
#   scripts/patch-inventory.sh generate   rewrite docs/PATCH-INVENTORY.md from source
#   scripts/patch-inventory.sh check      fail on drift / an unregistered patch class
#
# `check` is run by scripts/refactor-guard.sh before every build, so a Tier-1 file
# move that drops a PatchAll reference fails the same command every refactor
# commit already runs.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$ROOT/scripts/patch-inventory.py" "${1:-check}"
