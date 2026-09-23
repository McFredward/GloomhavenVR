#!/usr/bin/env bash
# GloomhavenVR — the committed AssetBundle must be readable by the GAME's runtime.
#
# The game is Unity 2021.3.5f1. A bundle written by a newer 2021.3.x editor gets a
# UnityFS **format 8** wrapper (BlockInfoNeedPaddingAtStart), and the older runtime
# rejects it outright:
#
#   Unable to read header from archive file: ...\gloomhavenvr.bundle
#   [Hands] AssetBundle.LoadFromFile failed — procedural hands active
#
# i.e. every 3D asset in the mod silently degrades to its procedural fallback. That is
# a total loss of the bundle with no crash and no error in the mod's own log, so it is
# easy to ship. It has happened twice.
#
# Two machine-checkable facts in the 30-byte header settle it:
#   offset 8, big-endian uint32  -> the wrapper format, must be 7
#   offset 12, NUL-terminated    -> "5.x.x", then the editor version that wrote it
#
# Build with /home/claw/unity-2021.3.5 (the game-exact editor), NOT unity-2021.3
# (2021.3.45f1). See prebuilt/README.md.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUNDLE="${1:-$ROOT/prebuilt/gloomhavenvr.bundle}"

if [[ ! -f "$BUNDLE" ]]; then
    echo "error: no bundle at $BUNDLE" >&2; exit 1
fi

magic="$(head -c 7 "$BUNDLE")"
if [[ "$magic" != "UnityFS" ]]; then
    echo "error: $BUNDLE is not a UnityFS archive (magic='$magic')" >&2; exit 1
fi

fmt=$((16#$(dd if="$BUNDLE" bs=1 skip=8 count=4 status=none | xxd -p)))
# The editor version follows the "5.x.x" generation string at offset 12. It is
# NUL-terminated and the very next header field is binary, so cut at the first NUL
# rather than stripping them (a stray 0x01 would otherwise ride along).
ver="$(dd if="$BUNDLE" bs=1 skip=18 count=20 status=none | tr '\0' '\n' | head -1)"

if [[ "$fmt" != "7" ]]; then
    echo "error: bundle wrapper is format $fmt, the 2021.3.5f1 runtime needs 7" >&2
    echo "       written by Unity '$ver' — rebuild with /home/claw/unity-2021.3.5," >&2
    echo "       or downgrade the wrapper with unity/repack-bundle/repack_fmt7.py" >&2
    echo "       (the repack cannot fix shaders compiled by the newer editor)." >&2
    exit 1
fi

if [[ "$ver" != "2021.3.5f1" ]]; then
    # Format 7 with a foreign editor means someone repacked: loadable, but the shaders
    # inside were still compiled by that editor and can fail on the game's GPU path.
    echo "warning: format 7 but written by Unity '$ver', not 2021.3.5f1 —" >&2
    echo "         loadable, but watch for pink materials (shader not supported)." >&2
fi

echo "bundle OK: UnityFS format $fmt, Unity $ver, $(stat -c%s "$BUNDLE") bytes"
if [[ $# -eq 0 ]]; then
    bash "${BASH_SOURCE[0]}" "$ROOT/prebuilt/ghvr-town.bundle"
fi
