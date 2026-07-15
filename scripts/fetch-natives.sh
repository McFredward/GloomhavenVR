#!/usr/bin/env bash
# Fetch the OpenXR native plugins the mod ships (libs/Natives/).
#
# The two Windows natives are PREBUILT inside the com.unity.xr.openxr package —
# no Unity editor is involved in producing them (unity/HARVESTING.md, "Partial
# shortcut"). We download them version-exact from the needle-mirror repo at the
# pinned package tag and verify their SHA256 against the hashes recorded in
# libs/Natives/README.md.
#
# Usage: scripts/fetch-natives.sh [--force]
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEST="$ROOT/libs/Natives"
OPENXR_VERSION="1.10.0"
BASE="https://github.com/needle-mirror/com.unity.xr.openxr/raw/$OPENXR_VERSION"

# file relative-path-in-package -> expected sha256 (recorded 2026-07-15 from tag 1.10.0)
FILES=(
    "Runtime/windows/x64/UnityOpenXR.dll"
    "RuntimeLoaders/windows/x64/openxr_loader.dll"
)
SHA256=(
    "2275da2750ebc9c815386604f73f0450b03fed6f44dafdeb15e978633e4866f5"
    "c008f1f429eb89ad1ebb959a74425c1b8df78abe6b1a08287ed44900d49881d3"
)

FORCE="${1:-}"
mkdir -p "$DEST"

for i in "${!FILES[@]}"; do
    rel="${FILES[$i]}"
    expected="${SHA256[$i]}"
    name="$(basename "$rel")"
    out="$DEST/$name"

    if [[ -f "$out" && "$FORCE" != "--force" ]]; then
        actual="$(sha256sum "$out" | cut -d' ' -f1)"
        if [[ "$actual" == "$expected" ]]; then
            echo "ok       $name (already present, hash verified)"
            continue
        fi
        echo "refetch  $name (hash mismatch: $actual)"
    fi

    echo "fetch    $name  <-  $BASE/$rel"
    curl -fsSL -o "$out.tmp" "$BASE/$rel"
    actual="$(sha256sum "$out.tmp" | cut -d' ' -f1)"
    if [[ "$actual" != "$expected" ]]; then
        echo "error: SHA256 mismatch for $name" >&2
        echo "  expected: $expected" >&2
        echo "  actual:   $actual" >&2
        echo "If the pin was intentionally bumped, update the hashes in this script AND libs/Natives/README.md." >&2
        rm -f "$out.tmp"
        exit 1
    fi
    mv "$out.tmp" "$out"
    echo "ok       $name ($actual)"
done

echo
echo "Natives ready in libs/Natives/ (OpenXR plugin $OPENXR_VERSION):"
ls -l "$DEST"/*.dll
