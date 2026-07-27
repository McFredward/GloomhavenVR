#!/usr/bin/env bash
# Make a fresh git worktree buildable.
#
# Three things the build needs are deliberately gitignored, so a worktree created by
# `git worktree add` cannot build until they are linked in from the main checkout:
#
#   libs/RuntimeDeps/           harvested Unity XR assemblies (large, licence-bound)
#   ressources/                 the game's Managed folder (the reference assemblies)
#   Directory.Build.props.user  the per-machine GameManaged path
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

# Link the CONTENTS of a directory that itself is tracked.
#
# libs/RuntimeDeps and libs/Natives exist in git (a .gitkeep and a README) while their
# actual payload — the harvested Unity XR assemblies and the OpenXR natives — is ignored.
# So the directory is always present in a fresh worktree and `link` short-circuits on it,
# leaving the build to fail with "libs/RuntimeDeps is not populated" — which reads like a
# repo problem and is not. (Found by the WorldUI worker, whose first guard baseline died
# on exactly this after the script reported success.)
link_contents() {
    local rel="$1" n=0
    [[ -d "$MAIN/$rel" ]] || { echo "warn: $rel missing in the main checkout — skipped" >&2; return 0; }
    mkdir -p "$HERE/$rel"
    local f base
    for f in "$MAIN/$rel"/*; do
        [[ -e "$f" ]] || continue
        base="$(basename "$f")"
        case "$base" in .gitkeep|README.md) continue ;; esac
        [[ -e "$HERE/$rel/$base" ]] && continue
        ln -s "$f" "$HERE/$rel/$base"
        n=$((n+1))
    done
    echo "linked  $rel ($n entries)"
}

link_contents libs/RuntimeDeps
link_contents libs/Natives
link ressources
link Directory.Build.props.user

echo "worktree ready — 'bash scripts/build.sh Release' should now work"
