#!/usr/bin/env bash
# Exercise the production window-space policy, including a defect-detecting negative control.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:$PATH"
project="$root/tests/GloomhavenVR.WindowReflowTests/GloomhavenVR.WindowReflowTests.csproj"
python3 "$root/tests/GloomhavenVR.WindowReflowTests/source_contract.py" "$root"
dotnet run --project "$project" --configuration Release
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
cp "$project" "$root/tests/GloomhavenVR.WindowReflowTests/"*.cs "$fixture/"
python3 - "$root" "$fixture" <<'PY'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/WorldUI/Modal/WindowReflowLayout.cs').read_text()
old = 'if (!collision || members == 0) return false;'
assert s.count(old) == 1
(dest/'broken.cs').write_text(s.replace(old, 'if ((!collision || members == 0) && count < 0) return false;'))
PY
if dotnet run --project "$fixture/GloomhavenVR.WindowReflowTests.csproj" --configuration Release \
    -p:LayoutSource="$fixture/broken.cs" -p:PoseSource="$root/src/GloomhavenVR/WorldUI/Modal/WindowReflowPose.cs" >"$fixture/output" 2>&1; then
    cat "$fixture/output"
    echo 'ERROR: unnecessary-rearrangement negative control was not detected' >&2
    exit 1
fi
if ! rg -q 'Separated windows must not move' "$fixture/output"; then
    cat "$fixture/output"
    exit 1
fi
python3 - "$root" "$fixture" <<'PYCODE'
from pathlib import Path
import sys
root, dest = map(Path, sys.argv[1:])
s = (root/'src/GloomhavenVR/WorldUI/Modal/WindowReflowPose.cs').read_text()
old = 'return targetCentre - targetRotation * centreOffset;'
assert s.count(old) == 1
(dest/'broken-pose.cs').write_text(s.replace(old,
    'return targetCentre - targetRotation * (centreOffset * 0f);'))
PYCODE
if dotnet run --project "$fixture/GloomhavenVR.WindowReflowTests.csproj" --configuration Release \
    -p:LayoutSource="$root/src/GloomhavenVR/WorldUI/Modal/WindowReflowLayout.cs" \
    -p:PoseSource="$fixture/broken-pose.cs" >"$fixture/pose-output" 2>&1; then
    cat "$fixture/pose-output"
    echo 'ERROR: off-centre pivot negative control was not detected' >&2
    exit 1
fi
if ! rg -q 'Off-centre hit rectangles must finish at the packed centre after yaw changes' "$fixture/pose-output"; then
    cat "$fixture/pose-output"
    exit 1
fi
echo 'Window reflow: 2 runtime negative controls passed.'
