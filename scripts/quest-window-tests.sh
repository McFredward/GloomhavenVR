#!/usr/bin/env bash
# Actual shared-window routing and conversion lifetime, with duplicate-ID negative controls.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 "$repo_root/tests/GloomhavenVR.QuestWindowTests/run.py"
