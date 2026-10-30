#!/usr/bin/env bash
# Focus the existing real release/install fixture on the new companion contract.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python3 "$repo_root/tests/GloomhavenVR.NativeVideoTests/MoviePackagingTests.py" "$repo_root" --menu-depth-only
