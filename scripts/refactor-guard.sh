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
#
# `check` first runs four checkers for the things the compiled form CANNOT show
# (CHARTER §3b). Each is pure text or a separate project, so none of them affects the
# snapshot; each can be run on its own:
#
#   scripts/patch-inventory.sh check   a patch class nobody registers ships INERT, and
#                                      docs/PATCH-INVENTORY.md must match the source
#   scripts/check-frame-order.sh       a per-frame reorder is an ordinary in-type diff,
#                                      i.e. the PASS condition for a Tier-1 motion
#   scripts/check-mirrors.sh           two constants deliberately not merged must not drift
#   scripts/wire-tests.sh              byte-exact packets: a Write+TryRead change made in
#                                      lockstep is invisible to a round trip and to the guard
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
    # The commit hash is baked in too, and changes with every commit — in the startup log
    # line, in BuildInfo, AND in AssemblyInformationalVersion. Mask every form: any run of
    # 9+ hex characters that looks like a git object id.
    #
    # The `-dirty` suffix and the `[branch]` token are part of the SAME build stamp
    # (csproj appends them from `git status --porcelain` / `rev-parse --abbrev-ref`).
    # They were not masked, so every `check` run against an uncommitted working tree —
    # i.e. every check that has any reason to be run — reported BuildInfo and Plugin as
    # CHANGED. Two permanent false positives are worse than none: they train the reader
    # to skim past the one line that matters. Masked here for the same reason the hash is.
    find "$out" -name '*.cs' -print0 \
        | xargs -0 -r sed -i -E 's/\b[0-9a-f]{9,40}\b/<COMMIT>/g; s/<COMMIT>-dirty/<COMMIT>/g; s/build <COMMIT> \[[^]]*\]/build <COMMIT>/g'
}

# Classify one changed file: MOVED if the two versions are permutations of each other
# (same multiset of lines, different order), CHANGED otherwise.
#
# This distinction is the whole point for a refactor whose main tool is moving code.
# ilspycmd emits members in SOURCE order, so splitting a class into partials or
# reordering members reshuffles the snapshot even though nothing about the compiled
# behaviour changed. Without this, every Tier 1 motion looks like a failure and the
# signal is lost in noise.
#
# It is not a proof of safety: reordering two statements that DO depend on each other
# is also a permutation. It narrows "what changed" to "only the order changed", which
# is exactly the question a human then has to answer.
classify() {
    if diff -q <(sort "$1") <(sort "$2") >/dev/null 2>&1; then echo "MOVED  "; else echo "CHANGED"; fi
}

case "${1:-check}" in
    baseline)
        snapshot "$BASE"
        echo "baseline: $(find "$BASE" -name '*.cs' | wc -l) types from $(git -C "$ROOT" rev-parse --short HEAD)"
        git -C "$ROOT" rev-parse HEAD > "$GUARD/baseline.rev"
        ;;
    check)
        [[ -d "$BASE" ]] || { echo "error: no baseline — run 'refactor-guard.sh baseline' first" >&2; exit 1; }
        # --- the blind spots (CHARTER §3b) -------------------------------------------
        # These run BEFORE the build, because they check things the compiled form
        # cannot show: a patch class nobody registers still compiles and ships inert,
        # and a reordered per-frame step is an ordinary in-type diff. Both are text
        # checks; neither reaches the DLL, so neither affects the snapshot below.
        "$ROOT/scripts/patch-inventory.sh" check \
            || { echo "error: Harmony patch surface drifted (see above)" >&2; exit 1; }
        "$ROOT/scripts/check-frame-order.sh" \
            || { echo "error: frame ordering drifted (see above)" >&2; exit 1; }
        "$ROOT/scripts/check-mirrors.sh" \
            || { echo "error: mirrored constants drifted (see above)" >&2; exit 1; }
        "$ROOT/scripts/wire-tests.sh" \
            || { echo "error: the wire format changed (see above)" >&2; exit 1; }
        snapshot "$CURR"
        if [[ "${2:-}" == "--summary" ]]; then
            echo "=== compiled form vs $(cut -c1-9 < "$GUARD/baseline.rev" 2>/dev/null) ==="
            moved=0; changed=0; added=0
            while IFS= read -r line; do
                case "$line" in
                    Files\ *)
                        rel="${line#Files }"; rel="${rel%% and *}"; rel="${rel#$BASE/}"
                        verdict="$(classify "$BASE/$rel" "$CURR/$rel")"
                        [[ "$verdict" == "MOVED  " ]] && moved=$((moved+1)) || changed=$((changed+1))
                        echo "  $verdict  $rel" ;;
                    Only\ in\ *)
                        added=$((added+1)); echo "  NEW/GONE $line" ;;
                esac
            done < <(diff -rq "$BASE" "$CURR" 2>/dev/null || true)
            echo "=== $moved moved (order only), $changed changed, $added added/removed ==="
            [[ $changed -eq 0 && $added -eq 0 ]] && echo "=== no compiled behaviour differs from the baseline ==="
        else
            diff -ru "$BASE" "$CURR" || true
        fi
        ;;
    *)
        echo "usage: refactor-guard.sh {baseline|check [--summary]}" >&2; exit 1 ;;
esac
