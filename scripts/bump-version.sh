#!/usr/bin/env bash
# GloomhavenVR — read or bump the release version.
#
#   scripts/bump-version.sh              print the current version, change nothing
#   scripts/bump-version.sh --patch      bump MAJOR.MINOR.PATCH -> MAJOR.MINOR.(PATCH+1)
#   scripts/bump-version.sh --minor      bump -> MAJOR.(MINOR+1).0
#   scripts/bump-version.sh --major      bump -> (MAJOR+1).0.0
#   scripts/bump-version.sh --set 1.2.3  set an exact version
#
# In every bumping mode the NEW version is printed on stdout and nothing else is, so
# a workflow can do:  VERSION="$(scripts/bump-version.sh --patch)"
#
# THE ONE SOURCE OF TRUTH is <Version> in src/GloomhavenVR/GloomhavenVR.csproj.
# scripts/package-release.sh already reads it with the same sed expression to name the
# zip, so the tag, the release title, the zip name and the DLL's stamped version all
# come from this single line by construction — they cannot drift apart.
#
# WHO CALLS THE BUMPING MODES. Only .github/workflows/release.yml, and only AFTER the
# release is published, and only against `dev`. A release publishes the number that is
# ALREADY in the csproj — the release run reads it with the no-argument mode above and
# writes nothing — and the bump that follows prepares the NEXT one. The number in the
# csproj on `dev` is therefore always the version the next release will carry, never
# the one that was just released.
#
# The bump must never be committed onto `main`. `main` is only ever a fast-forward of
# `dev`; a commit made directly on it makes the next `git push origin dev:main` a
# non-fast-forward, which is precisely the wedge this arrangement was written to undo.
#
# NOT the same thing as NetProtocol.ModBuild. That is the multiplayer wire-compat
# counter and is bumped by hand on every shared build; this is the user-facing
# release number. Do not couple them.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CSPROJ="$ROOT/src/GloomhavenVR/GloomhavenVR.csproj"

[[ -f "$CSPROJ" ]] || { echo "error: $CSPROJ not found" >&2; exit 1; }

read_version() {
    sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$CSPROJ" | head -1
}

CURRENT="$(read_version)"
if [[ -z "$CURRENT" ]]; then
    echo "error: no <Version> element in $CSPROJ" >&2
    exit 1
fi
if [[ ! "$CURRENT" =~ ^([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
    echo "error: <Version> is '$CURRENT', which is not MAJOR.MINOR.PATCH." >&2
    echo "       Refusing to guess how to bump it — fix the csproj by hand." >&2
    exit 1
fi
MAJOR="${BASH_REMATCH[1]}"; MINOR="${BASH_REMATCH[2]}"; PATCH="${BASH_REMATCH[3]}"

MODE="${1:---show}"
case "$MODE" in
    --show|"") echo "$CURRENT"; exit 0 ;;
    --patch)   NEW="$MAJOR.$MINOR.$((PATCH + 1))" ;;
    --minor)   NEW="$MAJOR.$((MINOR + 1)).0" ;;
    --major)   NEW="$((MAJOR + 1)).0.0" ;;
    --set)
        NEW="${2:-}"
        [[ "$NEW" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || {
            echo "error: --set needs MAJOR.MINOR.PATCH, got '${2:-}'" >&2; exit 1; }
        ;;
    *) echo "error: unknown mode '$MODE' (see the header of this script)" >&2; exit 1 ;;
esac

# Replace only the FIRST <Version> line, and only in this csproj. Anchored on the
# exact current value so a partially edited file cannot be silently mangled.
tmp="$(mktemp)"
awk -v old="<Version>$CURRENT</Version>" -v new="<Version>$NEW</Version>" '
    !done && index($0, old) { sub(old, new); done = 1 }
    { print }
' "$CSPROJ" > "$tmp"
mv "$tmp" "$CSPROJ"

AFTER="$(read_version)"
if [[ "$AFTER" != "$NEW" ]]; then
    echo "error: bump did not take — csproj still says '$AFTER', wanted '$NEW'." >&2
    exit 1
fi
echo "$NEW"
