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
python3 - "$scratch/frame.zip" "$scratch/extracted" <<'PY'
import pathlib
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as archive:
    archive.extractall(sys.argv[2])
base = pathlib.Path(sys.argv[2]) / 'BepInEx/plugins/GloomhavenVR/FrameSetup'
for name in ('install-steam-frame.sh', 'GloomhavenVR-Setup.desktop'):
    if not (base / name).is_file():
        raise SystemExit(f'error: extracted Frame launcher is missing: {name}')
print('PowerShell Frame archive: both launchers extract into FrameSetup.')
PY
