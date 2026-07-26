#!/usr/bin/env bash
# Behaviour fingerprint for refactoring — proves what a change really touched.
#
# The mod has no automated tests; every behaviour was won on hardware. So a
# refactor cannot be verified by running it — it has to be verified by showing
# that the COMPILED code is unchanged wherever we did not intend a change.
#
# How: build Release, decompile the DLL back to C# with ilspycmd (-p groups the
# output by namespace/type, so it is independent of our source file layout),
# mask the build timestamp (the only nondeterministic content), and diff against
# a stored baseline.
#
#   Pure code MOVE (file split, partial class, reordering)  -> empty diff.
#   Method extraction / rename of internals                 -> diff confined to that type.
#   Anything else showing up                                -> collateral damage. Stop.
#
# Usage:
#   scripts/refactor-guard.sh baseline    # snapshot current HEAD as the reference
#   scripts/refactor-guard.sh check       # diff working tree against the snapshot
#   scripts/refactor-guard.sh check --summary   # only list the changed types
#
# The snapshot lives in .planning/refactor/.guard/ (gitignored).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GUARD="$ROOT/.planning/refactor/.guard"
BASE="$GUARD/baseline"
CURR="$GUARD/current"
DLL="$ROOT/src/GloomhavenVR/bin/Release/net472/GloomhavenVR.dll"
REFS="$ROOT/ressources/Managed"

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:${DOTNET_ROOT:-$HOME/.dotnet}/tools:$PATH"

command -v dotnet    >/dev/null 2>&1 || { echo "error: dotnet SDK not found" >&2; exit 1; }
command -v ilspycmd  >/dev/null 2>&1 || { echo "error: ilspycmd not found (dotnet tool install -g ilspycmd)" >&2; exit 1; }

# Decompile the freshly built DLL into $1, with volatile content masked.
snapshot() {
    local out="$1"
    dotnet build "$ROOT/GloomhavenVR.sln" -c Release -v quiet --nologo \
        | grep -E "error|Build FAILED" && { echo "error: build failed" >&2; exit 1; }
    rm -rf "$out"; mkdir -p "$out"
    ilspycmd -p -o "$out" -r "$REFS" "$DLL" >/dev/null
    # The build stamp is the only thing that differs between two builds of the
    # same source — mask it so it never shows up as a false positive.
    grep -rlZ 'built 20\|BuildTimeUtc' "$out" 2>/dev/null \
        | xargs -0 -r sed -i -E 's/[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2} UTC/<BUILD-TIME>/g'
    # The commit hash is baked in too, and changes with every commit.
    grep -rlZ 'build [0-9a-f]\{9\}' "$out" 2>/dev/null \
        | xargs -0 -r sed -i -E 's/build [0-9a-f]{9} \[[^]]*\]/build <COMMIT>/g'
}

case "${1:-check}" in
    baseline)
        snapshot "$BASE"
        echo "baseline: $(find "$BASE" -name '*.cs' | wc -l) types from $(git -C "$ROOT" rev-parse --short HEAD)"
        git -C "$ROOT" rev-parse HEAD > "$GUARD/baseline.rev"
        ;;
    check)
        [[ -d "$BASE" ]] || { echo "error: no baseline — run 'refactor-guard.sh baseline' first" >&2; exit 1; }
        snapshot "$CURR"
        if [[ "${2:-}" == "--summary" ]]; then
            echo "=== types whose compiled form changed vs $(cut -c1-9 < "$GUARD/baseline.rev" 2>/dev/null) ==="
            diff -rq "$BASE" "$CURR" | sed -E 's|.*/baseline/||; s| and .*||; s|^Files ||' || true
            echo "=== $(diff -r "$BASE" "$CURR" | grep -c '^[<>]' || true) changed lines total ==="
        else
            diff -ru "$BASE" "$CURR" || true
        fi
        ;;
    *)
        echo "usage: refactor-guard.sh {baseline|check [--summary]}" >&2; exit 1 ;;
esac
