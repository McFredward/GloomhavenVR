#!/usr/bin/env bash
set -euo pipefail
repo="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 "$repo/scripts/check-town-onboarding624.py" --source-root "$repo" "$@"
