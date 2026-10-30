#!/usr/bin/env bash
set -euo pipefail

script_root="$(dirname "$(readlink -f "${BASH_SOURCE[0]}")")/.."
exec "${GHVR_UNITYPY_PYTHON:-/home/claw/unitypy-venv/bin/python}" -B \
    "$script_root/tests/menu-depth-native-card/check.py" "$@"
