#!/usr/bin/env bash
# GloomhavenVR — render honest release notes from the commit range.
#
#   scripts/release-notes.sh <version> [<output file>]     (default output: RELEASE_NOTES.md)
#
# No marketing copy is invented. The body is: what this release is, what is in the
# zip, the multiplayer compatibility number, and the commit subjects since the
# previous tag.
#
# THE FIRST RELEASE IS THE AWKWARD CASE and it is handled explicitly. There is no
# previous tag, so the "range" is the entire project history — over two thousand
# commits at the time of writing. Two reasons not to print it:
#   * a GitHub release body is capped at 125,000 characters and 2,000 commit subjects
#     blow straight through it, so the release API call would FAIL, and
#   * nobody reads it.
# So with no previous tag this prints a one-line statement of scale and a link, and
# says plainly that it is a first release. With a previous tag it prints the real
# list, capped at 100 entries with an explicit "and N more".
#
# NOTE: `pipefail` is deliberately OFF. Every list here is `git log … | head -N`, and
# `head` closing the pipe kills git with SIGPIPE (141); with pipefail that becomes a
# failed pipeline and `set -e` aborts the release for no reason. Nothing in this
# script depends on a mid-pipe exit status.
set -eu

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:?usage: release-notes.sh <version> [output file]}"
OUT="${2:-$ROOT/RELEASE_NOTES.md}"

MAX_COMMITS=100
MAX_BYTES=100000          # GitHub's hard limit is 125,000; leave room.

REPO_URL="$(git -C "$ROOT" remote get-url origin 2>/dev/null || echo '')"
REPO_URL="${REPO_URL%.git}"
REPO_URL="${REPO_URL/git@github.com:/https://github.com/}"

SHA="$(git -C "$ROOT" rev-parse HEAD)"
SHORT="$(git -C "$ROOT" rev-parse --short=9 HEAD)"
DATE="$(git -C "$ROOT" show -s --format=%cs HEAD)"

# Previous release tag: highest vN.N.N that is NOT the one we are creating now.
PREV="$(git -C "$ROOT" tag --list 'v*' --sort=-v:refname \
        | grep -vx "v$VERSION" | head -1 || true)"

MODBUILD="$(sed -n 's/.*public const ushort ModBuild = \([0-9]*\);.*/\1/p' \
            "$ROOT/src/GloomhavenVR/Net/NetProtocol.cs" | head -1)"

{
    echo "GloomhavenVR $VERSION — a room-scale VR mod for Gloomhaven (digital)."
    echo
    echo "Built from \`$SHORT\` ($DATE)."
    if [[ -n "$MODBUILD" ]]; then
        echo
        echo "**Multiplayer:** ModBuild $MODBUILD. Every player in a session must run the same"
        echo "ModBuild; the game shows a version-mismatch dialog otherwise."
    fi
    echo
    echo "## Install"
    echo
    echo "Download \`GloomhavenVR-$VERSION.zip\`, unpack it into the Gloomhaven install folder"
    echo "(the one containing \`Gloomhaven.exe\`) and read \`INSTALL.txt\` from the archive."
    echo "The archive contains the plugin, the BepInEx preloader, the OpenXR natives, the"
    echo "managed Unity XR assemblies and the asset bundle — nothing else has to be fetched."
    echo
    echo "## Changes"
    echo

    if [[ -z "$PREV" ]]; then
        total="$(git -C "$ROOT" rev-list --count HEAD)"
        first_date="$(git -C "$ROOT" log --reverse --format=%cs | head -1)"
        echo "First release. There is no previous tag to compare against, so there is no"
        echo "meaningful change list: this is $total commits of development from $first_date"
        echo "to $DATE published for the first time."
        if [[ -n "$REPO_URL" ]]; then
            echo
            echo "Full history: $REPO_URL/commits/$SHA"
        fi
    else
        count="$(git -C "$ROOT" rev-list --count "$PREV..HEAD")"
        if [[ "$count" -eq 0 ]]; then
            echo "No new commits since \`$PREV\`. This is a rebuild of the same source at a"
            echo "new version number."
        else
            echo "$count commit(s) since \`$PREV\`."
            echo
            # Drop the release chore commit itself — it is noise in its own notes.
            git -C "$ROOT" log --no-merges --format='- %s' "$PREV..HEAD" \
                | grep -v '^- chore(release): ' \
                | head -"$MAX_COMMITS"
            if [[ "$count" -gt "$MAX_COMMITS" ]]; then
                echo "- … and $((count - MAX_COMMITS)) more."
            fi
            if [[ -n "$REPO_URL" ]]; then
                echo
                echo "Full comparison: $REPO_URL/compare/$PREV...v$VERSION"
            fi
        fi
    fi
} > "$OUT"

# A body over the API limit fails the release call outright, which would be a very
# confusing way to lose a release that built perfectly.
size="$(wc -c < "$OUT")"
if [[ "$size" -gt "$MAX_BYTES" ]]; then
    head -c "$MAX_BYTES" "$OUT" > "$OUT.tmp"
    printf '\n\n… release notes truncated at %d bytes (GitHub release body limit).\n' "$MAX_BYTES" >> "$OUT.tmp"
    mv "$OUT.tmp" "$OUT"
    echo "warning: release notes truncated from $size bytes" >&2
fi

echo "Release notes written to $OUT ($(wc -c < "$OUT") bytes, previous tag: ${PREV:-none})"
