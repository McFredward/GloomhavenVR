#!/usr/bin/env bash
# GloomhavenVR — render the GitHub release body.
#
#   scripts/release-notes.sh <version> [<output file>]     (default output: RELEASE_NOTES.md)
#
# WHO READS THIS FILE'S OUTPUT: a player, on the release page, deciding whether to
# download. Not a developer. So the body is: what this is, what changed, how to
# install it, one sentence about playing together, and a link to the README.
#
# THE COMMIT SUBJECTS ARE NOT PLAYER-READABLE AND THERE IS NO WAY TO MAKE THEM SO.
# They are written for developers ("feat(worldui,net): ModBuild 239 — a union that
# counted invisible ink") and no amount of filtering turns one into a sentence a
# player understands. Pretending otherwise — auto-grouping them under friendly
# headings, stripping the prefixes — produces something that LOOKS like release
# notes and still says nothing. So:
#   * if packaging/release-highlights/<version>.md exists, it is included verbatim
#     as the "What's new" section. That file is where a HUMAN writes the player's
#     sentences, and it is the only honest source of them.
#   * the raw commit subjects go in a collapsed <details> block, labelled as what
#     they are: the developers' own notes. A player can ignore it; a developer
#     opening the release page still has the list.
# When no highlights file exists the release simply has no prose change list, and
# that is more honest than a machine-mangled one.
#
# THE FIRST RELEASE IS THE AWKWARD CASE and it is handled explicitly. There is no
# previous tag, so the "range" is the entire project history — over two thousand
# commits at the time of writing. Two reasons not to print it:
#   * a GitHub release body is capped at 125,000 characters and 2,000 commit subjects
#     blow straight through it, so the release API call would FAIL, and
#   * nobody reads it.
# So with no previous tag this prints a one-line statement of scale and a link. With
# a previous tag it prints the real list, capped at 100 entries with an explicit
# "and N more".
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

# Hand-written player-facing notes for this version, if somebody wrote them.
HIGHLIGHTS="$ROOT/packaging/release-highlights/$VERSION.md"

# The developer change list, resolved ONCE so both the "What's new" wording and the
# collapsed block agree about whether there is anything in it. `shown` is the count
# AFTER the release-chore commits are dropped, which is what the reader will see.
COUNT=0
SUBJECTS=""
SHOWN=0
if [[ -n "$PREV" ]]; then
    COUNT="$(git -C "$ROOT" rev-list --count "$PREV..HEAD")"
    SUBJECTS="$(git -C "$ROOT" log --no-merges --format='- %s' "$PREV..HEAD" \
                | grep -v '^- chore(release): ' \
                | head -"$MAX_COMMITS" || true)"
    SHOWN="$(printf '%s' "$SUBJECTS" | grep -c . || true)"
fi

{
    echo "**GloomhavenVR $VERSION** — play Gloomhaven (digital) as a room-scale VR board game."
    echo "You stand at the table and pick your cards up with your hands."
    echo
    echo "## What's new"
    echo

    if [[ -f "$HIGHLIGHTS" ]]; then
        cat "$HIGHLIGHTS"
        echo
    elif [[ -z "$PREV" ]]; then
        total="$(git -C "$ROOT" rev-list --count HEAD)"
        first_date="$(git -C "$ROOT" log --reverse --format=%cs | head -1)"
        echo "This is the first release, so there is nothing to compare it against — it is"
        echo "$total commits of work from $first_date to $DATE, published for the first time."
        if [[ -n "$REPO_URL" ]]; then
            echo "Start with the [README]($REPO_URL#readme): it covers what the mod does, how"
            echo "to install it, and the rough edges that are known about."
        else
            echo "Start with the README: it covers what the mod does, how to install it, and"
            echo "the rough edges that are known about."
        fi
        echo
    elif [[ "$SHOWN" -eq 0 ]]; then
        echo "Nothing changed since \`$PREV\` except the version number — this is the same"
        echo "build, re-released."
        echo
    else
        echo "No plain-language summary was written for this release. The full list of changes"
        echo "is at the bottom of this page, but be warned: it is the developers' own notes,"
        echo "in their own shorthand."
        echo
    fi

    echo "## Install"
    echo
    echo "Download \`GloomhavenVR-$VERSION.zip\` below and unpack it into your Gloomhaven"
    echo "folder — the one with \`GH.exe\` in it. Everything the mod needs is in that archive;"
    echo "there is nothing else to download. \`INSTALL.txt\` inside it walks you through the"
    echo "one other thing you need (a free program called BepInEx, installed once) and the"
    echo "headset setting to check first."
    echo
    if [[ -n "$PREV" ]]; then
        echo "**Updating from an older version?** Unpack this zip over the top of the old one."
        echo "Nothing to uninstall first. The game will close and reopen by itself once on the"
        echo "first start after that — that is meant to happen. Your settings and save games are"
        echo "untouched."
        echo
    fi
    echo "**Playing with other people:** everyone in a session needs this same version of the"
    echo "mod. If someone has a different one the game tells you, rather than letting the"
    echo "session go quietly wrong. People without the mod at all can still play with you."
    echo
    echo "Everything else — the controls, all the settings, the known rough edges and what to"
    if [[ -n "$REPO_URL" ]]; then
        echo "send if something breaks — is in the [README]($REPO_URL#readme)."
    else
        echo "send if something breaks — is in the README."
    fi
    echo

    # ---- the developers' own change log, collapsed --------------------------------------
    if [[ -n "$PREV" ]]; then
        echo "---"
        echo
        # An empty list (a version bump and nothing else) must not become an empty <details>
        # whose summary claims a commit count. The footer only SAYS "nothing changed" when the
        # body said so too — a hand-written highlights file above must not be contradicted six
        # lines below it by a sentence generated from the commit range.
        if [[ "$SHOWN" -eq 0 ]]; then
            if [[ -f "$HIGHLIGHTS" ]]; then
                echo "<sub>Built from \`$SHORT\` ($DATE).</sub>"
            else
                echo "<sub>Nothing but the version number changed since \`$PREV\`. Built from"
                echo "\`$SHORT\` ($DATE).</sub>"
            fi
        else
            echo "<details>"
            echo "<summary>Technical change log — $SHOWN change(s) since <code>$PREV</code>, written by and for developers</summary>"
            echo
            printf '%s\n' "$SUBJECTS"
            if [[ "$COUNT" -gt "$MAX_COMMITS" ]]; then
                echo "- … and $((COUNT - MAX_COMMITS)) more."
            fi
            if [[ -n "$REPO_URL" ]]; then
                echo
                echo "Full comparison: $REPO_URL/compare/$PREV...v$VERSION"
            fi
            echo
            echo "</details>"
            echo
            echo "<sub>Built from \`$SHORT\` ($DATE).</sub>"
        fi
    else
        echo "---"
        echo
        if [[ -n "$REPO_URL" ]]; then
            echo "<sub>Built from \`$SHORT\` ($DATE). Full history:"
            echo "$REPO_URL/commits/$SHA</sub>"
        else
            echo "<sub>Built from \`$SHORT\` ($DATE).</sub>"
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

if [[ ! -f "$HIGHLIGHTS" && -n "$PREV" && "$SHOWN" -gt 0 ]]; then
    echo "note: no player-facing summary for this release. Write a few plain sentences in" >&2
    echo "      packaging/release-highlights/$VERSION.md and re-run — they become the" >&2
    echo "      \"What's new\" section. Without it the release page only carries the" >&2
    echo "      collapsed developer change log." >&2
fi
