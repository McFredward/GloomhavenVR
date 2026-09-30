#!/usr/bin/env bash
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)"
if ! command -v pwsh >/dev/null 2>&1; then
    echo 'PowerShell Frame archive: skipped (pwsh is unavailable on this host).'
    exit 0
fi

scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
pwsh -NoProfile -File "$root/scripts/test-frame-archive.ps1" -OutputZip "$scratch/frame.zip"
python3 "$root/scripts/check-frame-launchers.py" "$scratch/frame.zip"
