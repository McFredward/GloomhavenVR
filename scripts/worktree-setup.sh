#!/usr/bin/env bash
# Make a fresh git worktree buildable.
#
# Three things the build needs are deliberately gitignored, so a worktree created by
# `git worktree add` cannot build until they are linked in from the main checkout:
#
#   libs/RuntimeDeps/*          harvested Unity XR assemblies (large, licence-bound)
#   libs/Natives/*              openxr_loader.dll / UnityOpenXR.dll (the preloader payload)
#   ressources/                 the game's Managed folder (the reference assemblies)
#   Directory.Build.props.user  the per-machine GameManaged path
#
# Note the two libs/ entries are the DIRECTORY CONTENTS, not the directories: the
# directories themselves are tracked (.gitkeep + README) and therefore already exist in a
# fresh worktree — see link_contents below for why that mattered.
#
# Without them `scripts/build.sh` fails on its first line with "libs/RuntimeDeps is not
# populated", which is a confusing way to learn that your worktree is fine and your
# environment is not. Every parallel worker hit this.
#
# Usage (from inside the worktree):  bash scripts/worktree-setup.sh [path-to-main-checkout]
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MAIN="${1:-}"

if [[ -z "$MAIN" ]]; then
    # A worktree's .git is a file pointing at <main>/.git/worktrees/<name>.
    if [[ -f "$HERE/.git" ]]; then
        gitdir="$(sed -n 's/^gitdir: //p' "$HERE/.git")"
        MAIN="$(cd "${gitdir%%/.git/worktrees/*}" && pwd)"
    else
        echo "error: not a worktree, and no main checkout given" >&2; exit 1
    fi
fi

[[ -d "$MAIN" ]] || { echo "error: main checkout not found: $MAIN" >&2; exit 1; }
[[ "$MAIN" != "$HERE" ]] || { echo "already the main checkout — nothing to do"; exit 0; }

link() {
    local rel="$1"
    [[ -e "$HERE/$rel" ]] && return 0
    [[ -e "$MAIN/$rel" ]] || { echo "warn: $rel missing in the main checkout too — skipped" >&2; return 0; }
    mkdir -p "$(dirname "$HERE/$rel")"
    ln -s "$MAIN/$rel" "$HERE/$rel"
    echo "linked  $rel"
}

# Link the CONTENTS of a directory, not the directory itself.
#
# libs/RuntimeDeps and libs/Natives are TRACKED (each holds a .gitkeep and a README), so
# git creates them in every fresh worktree and link()'s `-e` test skips them — silently,
# because a skip is the normal outcome for an already-satisfied link. The worktree then
# reports "ready" and the very next build fails with "libs/RuntimeDeps is not populated",
# which is exactly the confusing failure this script exists to prevent. The payload files
# (the harvested XR assemblies and the native loaders) are the gitignored part, so they
# are what has to be linked.
link_contents() {
    local rel="$1" n=0 f base
    [[ -d "$MAIN/$rel" ]] || { echo "warn: $rel missing in the main checkout — skipped" >&2; return 0; }
    mkdir -p "$HERE/$rel"
    for f in "$MAIN/$rel"/*; do
        [[ -e "$f" ]] || continue
        base="$(basename "$f")"
        case "$base" in .gitkeep|README.md) continue ;; esac
        [[ -e "$HERE/$rel/$base" ]] && continue
        ln -s "$f" "$HERE/$rel/$base"
        n=$((n + 1))
    done
    echo "linked  $rel ($n file(s))"
}

link_contents libs/RuntimeDeps
link_contents libs/Natives
link ressources
link Directory.Build.props.user
# The tuned dev cfgs `scripts/rebase-defaults.py check` reads. Gitignored like the rest, so a
# fresh worktree has none and that gate — one of the four every change must pass — cannot run
# at all. Two workers in a row hit this and linked it by hand before reporting; linking it here
# costs a line and removes the stumble.
link .planning/debug/default
# The refactor guard's compiled-form BASELINE (gitignored, so a fresh worktree has none and
# `refactor-guard.sh check` aborts with "no baseline" — another of the gates every change must
# pass). Linked ENTRY BY ENTRY on purpose, never the .guard directory itself: `current/` is the
# scratch the check rewrites on every run and must stay LOCAL to the worktree. A worker who
# linked the whole directory by hand once wrote through it and then took the main checkout's
# baseline down with the link on cleanup, which cost a re-baseline to notice.
link .planning/refactor/.guard/baseline
link .planning/refactor/.guard/baseline.rev

echo "worktree ready — 'bash scripts/build.sh Release' should now work"
