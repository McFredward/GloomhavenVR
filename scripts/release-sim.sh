#!/usr/bin/env bash
# Offline Git tests of the production provenance/bookkeeping helper. All pushes are
# confined to a newly created local bare repository; no GitHub credentials are used.
# --old reproduces the historical main-version-bump failure. --race also runs the
# complete current suite, whose concurrent-dev and duplicate-tag cases cover races.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
keep=0
mode=new
releases=3
for arg in "$@"; do
    case "$arg" in
        --keep) keep=1 ;;
        --old) mode=old ;;
        --new|--race) mode=new ;;
        -n[0-9]*) releases="${arg#-n}" ;;
        *) echo "unknown argument: $arg" >&2; exit 2 ;;
    esac
done
scratch="$(mktemp -d "${TMPDIR:-/tmp}/ghvr-release-sim.XXXXXX")"
trap '[[ "$keep" == 1 ]] || rm -rf "$scratch"' EXIT
[[ "$keep" != 1 ]] || echo "scratch: $scratch"
origin="$scratch/origin.git"
work="$scratch/work"
runner="$scratch/runner"
helper="$repo_root/scripts/release-provenance.sh"
assertions=0
check() {
    local reason="$1"; shift
    if ! "$@"; then echo "FAIL: $reason" >&2; exit 1; fi
    assertions=$((assertions + 1))
}
git init -q --bare "$origin"
git init -q -b dev "$work"
git -C "$work" config user.name Tester
git -C "$work" config user.email tester@example.invalid
mkdir -p "$work/src/GloomhavenVR" "$work/scripts"
printf '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>\n' > "$work/src/GloomhavenVR/GloomhavenVR.csproj"
cp "$repo_root/scripts/bump-version.sh" "$work/scripts/"
git -C "$work" add .
git -C "$work" commit -qm initial
git -C "$work" remote add origin "$origin"
git -C "$work" push -q origin dev dev:main
git -C "$origin" symbolic-ref HEAD refs/heads/dev
git clone -q "$origin" "$runner"
git -C "$runner" config user.name Tester
git -C "$runner" config user.email tester@example.invalid

fetch_tips() {
    git -C "$runner" fetch -q origin
    dev_tip="$(git -C "$runner" rev-parse origin/dev)"
    main_tip="$(git -C "$runner" rev-parse origin/main)"
}
work_on_dev() {
    git -C "$work" fetch -q origin
    git -C "$work" checkout -q -B dev origin/dev
}
feature() {
    work_on_dev
    printf '%s\n' "$1" >> "$work/features.txt"
    git -C "$work" add features.txt
    git -C "$work" commit -qm "$1"
    git -C "$work" push -q origin dev
}
pr_merge() {
    git -C "$work" fetch -q origin
    git -C "$work" checkout -q -B main origin/main
    git -C "$work" merge -q --no-ff --no-edit origin/dev
    git -C "$work" push -q origin main
    fetch_tips
    candidate="$main_tip"
    release_version="$(git -C "$runner" show "$candidate:src/GloomhavenVR/GloomhavenVR.csproj" | sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p')"
}
prepare() {
    bash "$helper" prepare "$runner" "$candidate" "$dev_tip" "$main_tip" "$release_version" > "$scratch/prepare.log" 2>&1 || {
        cat "$scratch/prepare.log"; exit 1;
    }
}
expect_refused() {
    local reason="$1" bad_candidate="$2" main="$3"
    if bash "$helper" check "$runner" "$bad_candidate" "$dev_tip" "$main" > "$scratch/refusal.log" 2>&1; then
        echo "FAIL: $reason accepted" >&2; exit 1
    fi
    check "$reason failed for provenance" grep -q '^error: release candidate' "$scratch/refusal.log"
}

if [[ "$mode" == old ]]; then
    git -C "$runner" checkout -q -B main origin/main
    bash "$runner/scripts/bump-version.sh" --patch > /dev/null
    git -C "$runner" commit -qam 'old bookkeeping on main'
    git -C "$runner" push -q origin main
    feature 'new dev work'
    if git -C "$work" push origin dev:main > "$scratch/old.log" 2>&1; then
        echo 'FAIL: old design unexpectedly accepted second release' >&2; exit 1
    fi
    check 'old design rejected non-fast-forward' grep -Eq 'rejected|fetch first' "$scratch/old.log"
    echo 'Old release topology reproduced: main bookkeeping blocks the next direct release.'
    exit 0
fi

fetch_tips
check 'existing dev/main ancestry accepted' bash "$helper" check "$runner" "$main_tip" "$dev_tip" "$main_tip"
for ((round = 1; round <= releases; round++)); do
    feature "release $round"
    pr_merge
    check 'protected-main PR merge accepted' bash "$helper" check "$runner" "$candidate" "$dev_tip" "$main_tip"
    check 'merge has exact reviewed dev tree' test "$(git -C "$runner" rev-parse "$candidate^{tree}")" = "$(git -C "$runner" rev-parse "$dev_tip^{tree}")"
    if git -C "$runner" merge-base --is-ancestor "$candidate" "$dev_tip"; then
        echo 'FAIL: fixture lacks main-only PR merge commit'; exit 1
    fi
    assertions=$((assertions + 1))
    git -C "$runner" tag -a "v$release_version" -m "release $release_version" "$candidate"
    git -C "$runner" push -q origin "refs/tags/v$release_version"
    if [[ "$round" == 1 ]]; then
        # Dev moves while the release is built; accept the source parent as an
        # ancestor, not merely as the exact current dev tip.
        feature 'work during release build'
        fetch_tips
        check 'advancing dev accepted' bash "$helper" check "$runner" "$candidate" "$dev_tip" "$main_tip"
    fi
    prepare
    if [[ "$round" == 1 ]]; then
        # Another dev push wins after our preparation. Its version/content must
        # survive the retry, with no force-push and no duplicate patch bump.
        feature 'work racing bookkeeping'
        if git -C "$runner" push origin HEAD:dev > "$scratch/race.log" 2>&1; then
            echo 'FAIL: stale bookkeeping push overwrote concurrent dev'; exit 1
        fi
        assertions=$((assertions + 1))
        fetch_tips
        prepare
        check 'concurrent feature preserved' grep -q 'work racing bookkeeping' "$runner/features.txt"
        check 'earlier feature preserved' grep -q 'work during release build' "$runner/features.txt"
    fi
    git -C "$runner" push -q origin HEAD:dev
    check 'bookkeeping contains release merge' git -C "$runner" merge-base --is-ancestor "$candidate" HEAD
    check 'version advanced once' test "$(bash "$runner/scripts/bump-version.sh")" = "0.1.$round"
    check 'tag still names exact main release' test "$(git -C "$runner" rev-parse "v$release_version^{}")" = "$candidate"
    check 'release tree remains unchanged' test "$(git -C "$origin" rev-parse refs/heads/main)" = "$candidate"
done

# A manual minor version advance must not suppress ancestry bookkeeping.
feature 'next candidate'
pr_merge
work_on_dev
bash "$work/scripts/bump-version.sh" --minor > /dev/null
git -C "$work" commit -qam 'next minor version chosen by maintainer'
git -C "$work" push -q origin dev
fetch_tips
before="$(git -C "$runner" rev-parse "$dev_tip^{tree}")"
prepare
check 'manual version preserved' test "$(bash "$runner/scripts/bump-version.sh")" = '0.2.0'
check 'already advanced version still records release ancestry' git -C "$runner" merge-base --is-ancestor "$candidate" HEAD
check 'ancestry-only bookkeeping preserves exact dev tree' test "$(git -C "$runner" rev-parse HEAD^{tree})" = "$before"
git -C "$runner" push -q origin HEAD:dev
fetch_tips

# Construct deliberately invalid commit graphs without altering the real fixtures.
# Main membership is checked independently of exact content/provenance.
unrelated="$(printf 'unrelated\n' | git -C "$runner" commit-tree "$dev_tip^{tree}")"
expect_refused 'unrelated same-tree main commit' "$unrelated" "$unrelated"
expect_refused 'candidate absent from main' "$unrelated" "$main_tip"
squash="$(printf 'same-tree squash\n' | git -C "$runner" commit-tree "$dev_tip^{tree}" -p "$main_tip")"
expect_refused 'same-tree squash with no dev ancestry' "$squash" "$squash"
# A resolution-only edit on main would not have been tested in the dev source tree.
wrong_merge="$(printf 'extra merge resolution\n' | git -C "$runner" commit-tree "$candidate^{tree}" -p "$main_tip" -p "$dev_tip")"
expect_refused 'merge with different dev tree' "$wrong_merge" "$wrong_merge"
# Even a matching merge must identify a dev ancestor, not an unrelated lookalike.
wrong_parent="$(printf 'unrelated merge source\n' | git -C "$runner" commit-tree "$dev_tip^{tree}" -p "$main_tip" -p "$unrelated")"
expect_refused 'merge from unrelated same-tree source' "$wrong_parent" "$wrong_parent"

# Keep the same-version race refusal pinned to the workflow's early existing-tag gate.
check 'first release tag exists for duplicate-version refusal' test -n "$(git -C "$runner" rev-parse --verify refs/tags/v0.1.0)"
python3 - "$repo_root/.github/workflows/release.yml" "$repo_root/.github/workflows/ci.yml" "$repo_root/scripts/test-suites.json" <<'PY'
from pathlib import Path
import json
import re
import sys
release, ci = (Path(p).read_text() for p in sys.argv[1:3])
manifest = json.loads(Path(sys.argv[3]).read_text())
assert release.index('refs/tags/v$VERSION') < release.index('name: Build ('), 'duplicate tag must fail before build'
assert 'scripts/release-provenance.sh check' in release
assert '"$PROVENANCE_SCRIPT" prepare' in release
# Runtime suites now execute through the shared manifest in a matrix job. Ensure
# ripgrep is installed IN THAT JOB before its runner, not merely somewhere earlier
# in the workflow, and preserve the original map-button coverage requirement.
match = re.search(r'^  runtime_checks:\n(.*?)(?=^  \w+:\n|\Z)', ci, re.M | re.S)
assert match, 'CI must retain the independent runtime job'
runtime = match.group(1)
tools = 'install --no-install-recommends --yes ripgrep'
runner = 'python3 scripts/run-test-suites.py --group ci'
assert tools in runtime and runner in runtime, 'runtime shard must install tools and execute the CI suite group'
assert runtime.index(tools) < runtime.index(runner), 'runtime tools must precede suite execution'
map_suites = [suite for suite in manifest['suites']
              if suite['command'] == ['bash', 'scripts/map-button-tests.sh'] and 'ci' in suite['groups']]
assert len(map_suites) == 1, 'map-button regression must run exactly once in the CI inventory'
assert release.index('scripts/ci-proof-reuse.py --mode release') < release.index('name: Build (')
assert 'bash scripts/map-button-tests.sh' not in release, 'release reuses verified exact-tree full CI'
assert 'run-test-suites.py --group ci' not in release, 'release must not repeat the CI matrix'
assert 'scripts/ci-build.sh Release' in release and 'scripts/package-release.sh' in release
assert 'git push origin HEAD:dev' in release and 'git push --force' not in release
PY
assertions=$((assertions + 1))
echo "Release topology tests: $assertions assertions passed; PR merges, unchanged provenance, advancing dev, retry and version preservation covered."
