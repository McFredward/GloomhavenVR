#!/usr/bin/env bash
# GloomhavenVR — build + the warning gate, for CI and for anyone who wants the same
# verdict locally.
#
#   scripts/ci-build.sh [Debug|Release]     (default: Release)
#
# WHY A GATE ON THE WARNING COUNT
# -------------------------------
# The tree builds with EXACTLY six warnings and they are all pre-existing
# nullable-analysis complaints:
#
#   CS8602  Net/RemotePickBanner.cs
#   CS8602  Net/RemoteHandFan.cs
#   CS8604  WorldUI/Surfaces/StatPanelSurface.cs     (two sites)
#
# It was SIX until 2026-08-25, when WorldUI/ButtonCluster.cs was deleted outright (the user
# retired the turn-flow cap group it drew) and took its two CS8602 sites with it. That is the
# ratchet turning in the intended direction — a warning that goes away because its file does is
# still one fewer place a seventh can hide.
#
# `TreatWarningsAsErrors` IS NOW ON (2026-08-27, once the count reached zero — this comment
# used to say it "cannot be turned on while those six exist", and that condition is met).
# The compiler stops a new warning before this script runs, which is earlier and unmissable.
#
# This gate stays anyway, and not out of sentiment: it still catches the case where the
# property is dropped from the csproj, ignored by a different SDK, or disabled for one
# project — i.e. exactly the ways a compiler-side setting goes quiet without anyone noticing.
# Two levers, the same rule as everywhere else in this repository.
#
# Line numbers are deliberately NOT checked: they move whenever the surrounding code
# is edited and a gate that cries wolf gets switched off. What IS checked is the
# count (4), the diagnostic codes (only CS8602/CS8604) and the FILES (only those
# three). A new warning of any other kind, in any other file, or a fifth of the same
# kind, fails the build.
#
# Fix the six and this script tells you to lower the number — that is the intended
# ratchet direction. It reached zero on 2026-08-27 and the direction is now one-way.
set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${1:-Release}"
LOG="$ROOT/build-ci.log"

# ZERO, since 2026-08-27. The six became four and the four became none: three were the
# compiler failing to carry a null-state the code guarantees (suppressed with `!` and a reason
# at each site, identical IL), and the fourth was a REAL unguarded dereference in a log line —
# see RemotePickBanner. EXPECT_CODES and EXPECT_FILES are empty on purpose: with the count at
# zero, ANY warning of ANY kind in ANY file fails this gate, which is the strongest form it has
# ever had. If a warning is ever accepted again, put its code and file back here rather than
# raising the count alone.
EXPECT_WARNINGS=0
EXPECT_CODES=""
EXPECT_FILES=""

set -o pipefail
bash "$ROOT/scripts/build.sh" "$CONFIG" 2>&1 | tee "$LOG"
build_status=$?
set +o pipefail

if [[ $build_status -ne 0 ]]; then
    echo >&2
    echo "error: the build itself failed (exit $build_status)." >&2
    exit 1
fi

# The MSBuild epilogue prints one "N Warning(s)" / "N Error(s)" pair per invocation;
# build.sh invokes dotnet once, so the last pair is the whole solution's.
warnings="$(sed -n 's/^ *\([0-9][0-9]*\) Warning(s)$/\1/p' "$LOG" | tail -1)"
errors="$(sed -n 's/^ *\([0-9][0-9]*\) Error(s)$/\1/p' "$LOG" | tail -1)"

if [[ -z "$warnings" || -z "$errors" ]]; then
    echo >&2
    echo "error: could not find the MSBuild warning/error summary in $LOG." >&2
    echo "       The build output format changed — this gate must be repaired, not skipped." >&2
    exit 1
fi

fail=0

if [[ "$errors" != "0" ]]; then
    echo >&2
    echo "error: $errors compile error(s)." >&2
    fail=1
fi

if [[ "$warnings" != "$EXPECT_WARNINGS" ]]; then
    echo >&2
    echo "error: $warnings warning(s), expected exactly $EXPECT_WARNINGS." >&2
    if [[ "$warnings" -lt "$EXPECT_WARNINGS" ]]; then
        echo "       Fewer than expected — someone fixed one. Lower EXPECT_WARNINGS in" >&2
        echo "       scripts/ci-build.sh to $warnings so the ratchet cannot slip back." >&2
    fi
    fail=1
fi

# Identity check, location-insensitive.
mapfile -t seen < <(grep -o 'warning CS[0-9]\{4\}' "$LOG" | awk '{print $2}' | sort -u)
for code in "${seen[@]}"; do
    if ! grep -qw -- "$code" <<<"$EXPECT_CODES"; then
        echo >&2
        echo "error: unexpected diagnostic $code — the six known warnings are $EXPECT_CODES only." >&2
        grep -m3 "warning $code" "$LOG" >&2
        fail=1
    fi
done

mapfile -t files < <(grep -o '[A-Za-z0-9_]*\.cs([0-9]*,[0-9]*): warning CS' "$LOG" \
                     | sed 's/(.*//' | sort -u)
for f in "${files[@]}"; do
    if ! grep -qw -- "$f" <<<"$EXPECT_FILES"; then
        echo >&2
        echo "error: warning in an unexpected file: $f" >&2
        grep -m3 "$f(" "$LOG" | grep warning >&2
        fail=1
    fi
done

echo
if [[ $fail -ne 0 ]]; then
    echo "BUILD GATE FAILED (see above). Full log: $LOG" >&2
    exit 1
fi
echo "build gate ok: 0 errors, exactly $EXPECT_WARNINGS warnings, all known ($EXPECT_CODES in $EXPECT_FILES)."
