#!/usr/bin/env bash
# Validate release history and prepare local dev bookkeeping. This script never pushes.
set -euo pipefail

release_check_provenance() {
    local repo="$1" candidate dev main
    candidate="$(git -C "$repo" rev-parse --verify "$2^{commit}")"
    dev="$(git -C "$repo" rev-parse --verify "$3^{commit}")"
    main="$(git -C "$repo" rev-parse --verify "$4^{commit}")"
    if ! git -C "$repo" merge-base --is-ancestor "$candidate" "$main"; then
        echo 'error: release candidate is not contained in current main' >&2
        return 1
    fi
    if git -C "$repo" merge-base --is-ancestor "$candidate" "$dev"; then
        return 0
    fi

    # Protected main requires a PR. Its merge commit does not exist on dev yet, but
    # its exact source tree must already have been integrated there. Do not accept a
    # squash/cherry-pick merely because it happens to have equal files, or a merge
    # whose resolution introduces changes outside its tested dev parent.
    local -a parents
    read -r -a parents <<< "$(git -C "$repo" show -s --format=%P "$candidate")"
    if [[ "${#parents[@]}" != 2 ]] \
        || ! git -C "$repo" merge-base --is-ancestor "${parents[1]}" "$dev" \
        || [[ "$(git -C "$repo" rev-parse "$candidate^{tree}")" != "$(git -C "$repo" rev-parse "${parents[1]}^{tree}")" ]]; then
        echo 'error: release candidate is neither dev history nor an unchanged merge of a dev ancestor' >&2
        return 1
    fi
}

release_prepare_dev() {
    local repo="$1" candidate="$2" dev="$3" main="$4" version="$5"
    release_check_provenance "$repo" "$candidate" "$dev" "$main"
    if [[ -n "$(git -C "$repo" status --porcelain --untracked-files=no)" ]]; then
        echo 'error: dev bookkeeping requires a clean tracked tree' >&2
        return 1
    fi
    git -C "$repo" checkout -q -B release-bump "$dev"
    if ! git -C "$repo" merge-base --is-ancestor "$candidate" HEAD; then
        local before
        before="$(git -C "$repo" rev-parse HEAD^{tree})"
        # Provenance established that all released content already exists in a dev
        # ancestor. Record the PR merge in dev history using an ordinary merge, and
        # fail closed if it unexpectedly changes any current development content.
        if ! git -C "$repo" merge --no-ff --no-edit "$candidate"; then
            git -C "$repo" merge --abort
            echo 'error: release ancestry could not be merged into dev without conflict' >&2
            return 1
        fi
        if [[ "$(git -C "$repo" rev-parse HEAD^{tree})" != "$before" ]]; then
            echo 'error: merging release ancestry unexpectedly changed dev content; nothing pushed' >&2
            return 1
        fi
    fi

    local current next
    current="$(bash "$repo/scripts/bump-version.sh")"
    if [[ "$current" == "$version" ]]; then
        next="$(bash "$repo/scripts/bump-version.sh" --patch)"
        git -C "$repo" add src/GloomhavenVR/GloomhavenVR.csproj
        git -C "$repo" commit -m "chore(release): set <Version> to $next after v$version"
    else
        echo "dev already names $current; preserving that version and recording release ancestry only"
    fi
}

case "${1:-}" in
    check) [[ "$#" == 5 ]] || exit 2; release_check_provenance "$2" "$3" "$4" "$5" ;;
    prepare) [[ "$#" == 6 ]] || exit 2; release_prepare_dev "$2" "$3" "$4" "$5" "$6" ;;
    *) echo 'usage: release-provenance.sh check|prepare REPO CANDIDATE DEV MAIN [RELEASE_VERSION]' >&2; exit 2 ;;
esac
