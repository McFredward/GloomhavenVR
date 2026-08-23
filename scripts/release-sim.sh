#!/usr/bin/env bash
# GloomhavenVR — offline simulation of the release branch topology.
#
#   scripts/release-sim.sh            simulate the CURRENT design (tag-only)
#   scripts/release-sim.sh --old      simulate the PREVIOUS design (bump commit on main)
#   scripts/release-sim.sh --race     two releases fired before the first bump lands
#   scripts/release-sim.sh --keep     leave the scratch repositories on disk
#
# WHY THIS EXISTS
# ---------------
# The release pipeline cannot be run here: it needs GitHub. But the defect that made
# the previous design fail was not in the build, it was in the BRANCH TOPOLOGY, and
# topology is pure git. So it can be reproduced exactly, offline, in a scratch repo.
#
# The defect: `.github/workflows/release.yml` committed the version bump onto `main`
# and pushed `HEAD:main`. `main` then carried a commit `dev` had never seen, so the
# release procedure — `git push origin dev:main` — became a NON-FAST-FORWARD from the
# second release onwards and the remote rejected it. The workflow's own "refuse if
# origin/main moved" guard could not see it: the thing that moved `main` was the
# previous run of that same workflow. The pipeline therefore worked exactly once.
#
# `--old` reproduces that rejection. The default reproduces the fix: three consecutive
# releases, every `dev:main` push a fast-forward, `main` never carrying a commit that
# `dev` does not have.
#
# What is simulated: the git operations of the workflow and of the release procedure,
# with the REAL scripts/bump-version.sh copied into the scratch repository so the
# version arithmetic under test is the shipped one. What is not simulated: the build,
# the packaging and the GitHub API — none of which touch a ref.
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE=new
KEEP=0
RELEASES=3
for arg in "$@"; do
    case "$arg" in
        --old)  MODE=old ;;
        --new)  MODE=new ;;
        --race) MODE=race ;;
        --keep) KEEP=1 ;;
        -n[0-9]*) RELEASES="${arg#-n}" ;;
        *) echo "error: unknown argument '$arg' (see the header)" >&2; exit 1 ;;
    esac
done

TMP="$(mktemp -d "${TMPDIR:-/tmp}/ghvr-release-sim.XXXXXX")"
cleanup() { [[ "$KEEP" == 1 ]] || rm -rf "$TMP"; }
trap cleanup EXIT
[[ "$KEEP" == 1 ]] && echo "scratch: $TMP"

ORIGIN="$TMP/origin.git"
WORK="$TMP/work"        # the maintainer's clone — where `git push origin dev:main` runs
RUNNER="$TMP/runner"    # a fresh clone per release, standing in for the GitHub runner

G() { git -C "$1" "${@:2}"; }

say()  { printf '\n\033[1m%s\033[0m\n' "$*"; }
step() { printf '  %s\n' "$*"; }
ok()   { printf '  \033[32m✓\033[0m %s\n' "$*"; }
bad()  { printf '  \033[31m✗\033[0m %s\n' "$*"; }

# ---------------------------------------------------------------------------------
# Scratch repository: a bare origin (which rejects non-fast-forwards exactly as GitHub
# does) and a working clone with `dev` and `main` at the same commit — the state
# docs/CI-CD.md §1 describes before the first release.
# ---------------------------------------------------------------------------------
git init -q --bare "$ORIGIN"
git -C "$ORIGIN" symbolic-ref HEAD refs/heads/dev   # `dev` is the default branch (§2.2)
git init -q "$WORK"
G "$WORK" config user.name  'Maintainer'
G "$WORK" config user.email 'maintainer@example.invalid'
G "$WORK" symbolic-ref HEAD refs/heads/dev

mkdir -p "$WORK/src/GloomhavenVR" "$WORK/scripts"
cat > "$WORK/src/GloomhavenVR/GloomhavenVR.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Version>0.1.0</Version>
  </PropertyGroup>
</Project>
EOF
# The real script, under test.
cp "$REPO/scripts/bump-version.sh" "$WORK/scripts/bump-version.sh"
chmod +x "$WORK/scripts/bump-version.sh"

G "$WORK" add -A
G "$WORK" commit -q -m 'initial: the project'
G "$WORK" remote add origin "$ORIGIN"
G "$WORK" push -q origin dev:dev
G "$WORK" push -q origin dev:main
G "$WORK" fetch -q origin

say "SETUP  ($MODE design, $RELEASES releases)"
step "origin: $ORIGIN"
step "dev and main both at $(G "$WORK" rev-parse --short dev), <Version>0.1.0"

read_version_at() {   # read_version_at <repo> <rev>
    G "$1" show "$2:src/GloomhavenVR/GloomhavenVR.csproj" \
      | sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' | head -1
}

# ---------------------------------------------------------------------------------
# The runner. Both variants get a pristine clone checked out at the commit `main`
# points at, which is what actions/checkout produces for a push event.
# ---------------------------------------------------------------------------------
#
# The optional argument is the commit the push event carried (github.sha). A queued run
# builds the commit that TRIGGERED it, not whatever `main` has drifted to since — which
# is the whole point of the --race case below.
fresh_runner() {
    rm -rf "$RUNNER"
    git clone -q "$ORIGIN" "$RUNNER"
    G "$RUNNER" config user.name  'github-actions[bot]'
    G "$RUNNER" config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    G "$RUNNER" checkout -q "${1:-$(G "$RUNNER" rev-parse origin/main)}"
}

# PREVIOUS design: bump, COMMIT ON MAIN, tag, push HEAD:main + tag.
release_old() {
    fresh_runner
    local sha version
    sha="$(G "$RUNNER" rev-parse HEAD)"
    version="$(cd "$RUNNER" && bash scripts/bump-version.sh --patch)"
    G "$RUNNER" add src/GloomhavenVR/GloomhavenVR.csproj
    G "$RUNNER" commit -q -m "chore(release): v$version [skip ci]"
    G "$RUNNER" tag -a "v$version" -m "GloomhavenVR $version"
    # The old guard: refuse if origin/main moved away from the built commit.
    G "$RUNNER" fetch -q origin main
    if [[ "$(G "$RUNNER" rev-parse origin/main)" != "$sha" ]]; then
        bad "workflow refused: main moved while building"; return 1
    fi
    G "$RUNNER" push -q --atomic origin HEAD:main "refs/tags/v$version"
    ok "released v$version — and pushed a commit onto main that dev does not have"
}

# CURRENT design: read the version, tag the pushed commit, push ONLY the tag, then
# bump on dev.
release_new() {
    fresh_runner "${1:-}"
    local sha version next
    sha="$(G "$RUNNER" rev-parse HEAD)"
    version="$(cd "$RUNNER" && bash scripts/bump-version.sh)"        # read, do not write

    if G "$RUNNER" rev-parse -q --verify "refs/tags/v$version" >/dev/null; then
        bad "workflow refused before building: tag v$version already exists"; return 1
    fi
    G "$RUNNER" fetch -q --no-tags origin dev
    if ! G "$RUNNER" merge-base --is-ancestor "$sha" origin/dev; then
        bad "workflow refused before building: $(G "$RUNNER" rev-parse --short "$sha") is not contained in dev"; return 1
    fi
    if [[ -n "$(G "$RUNNER" status --porcelain --untracked-files=no)" ]]; then
        bad "workflow refused: dirty tree"; return 1
    fi

    G "$RUNNER" tag -a "v$version" -m "GloomhavenVR $version" "$sha"
    G "$RUNNER" push -q origin "refs/tags/v$version"
    ok "released v$version at $(G "$RUNNER" rev-parse --short "$sha") — TAG ONLY, no commit pushed to main"

    # Bookkeeping, after publication, onto dev.
    G "$RUNNER" fetch -q --no-tags origin dev
    if [[ "$(read_version_at "$RUNNER" origin/dev)" != "$version" ]]; then
        step "dev already moved past $version — no bump written"
        return 0
    fi
    G "$RUNNER" checkout -q -B release-bump origin/dev
    next="$(cd "$RUNNER" && bash scripts/bump-version.sh --patch)"
    G "$RUNNER" add src/GloomhavenVR/GloomhavenVR.csproj
    G "$RUNNER" commit -q -m "chore(release): set <Version> to $next after v$version"
    G "$RUNNER" push -q origin HEAD:dev
    ok "bumped dev to <Version>$next  (commit lands on dev, never on main)"
}

# ---------------------------------------------------------------------------------
# The release procedure, run by hand: `git push origin dev:main`. The bare origin
# rejects a non-fast-forward, exactly as GitHub does.
# ---------------------------------------------------------------------------------
push_dev_to_main() {
    local out rc
    G "$WORK" fetch -q origin
    if G "$WORK" merge-base --is-ancestor origin/main dev 2>/dev/null; then
        step "git push origin dev:main   (fast-forward: main $(G "$WORK" rev-parse --short origin/main) is an ancestor of dev)"
    else
        step "git push origin dev:main   (NOT a fast-forward: main $(G "$WORK" rev-parse --short origin/main) is not an ancestor of dev)"
    fi
    set +e
    out="$(G "$WORK" push origin dev:main 2>&1)"; rc=$?
    set -e
    if [[ $rc -ne 0 ]]; then
        bad "REJECTED by the remote:"
        printf '      %s\n' "$out" | sed 's/^      $//'
        return 1
    fi
    ok "accepted"
    return 0
}

# ---------------------------------------------------------------------------------
# --race: two releases fired before the first one's bump has landed on dev.
#
# `concurrency: cancel-in-progress: false` serialises the two runs, so run A completes
# in full (including the bump onto dev) before run B starts. But run B builds the
# commit that TRIGGERED it, and that commit predates the bump — so its csproj still
# carries the version A just released. The claim under test is that B refuses BEFORE
# building anything, and that the remedy is the ordinary release command.
# ---------------------------------------------------------------------------------
if [[ "$MODE" == race ]]; then
    say "RACE  two dev:main pushes in quick succession"

    C0="$(G "$WORK" rev-parse dev)"
    step "push #1: main -> $(G "$WORK" rev-parse --short "$C0")   (run A queued)"
    G "$WORK" push -q origin dev:main

    echo "hotfix" >> "$WORK/src/GloomhavenVR/notes.txt"
    G "$WORK" add -A
    G "$WORK" commit -q -m 'feat: one more thing'
    G "$WORK" push -q origin dev:dev
    C1="$(G "$WORK" rev-parse dev)"
    step "push #2: main -> $(G "$WORK" rev-parse --short "$C1")   (run B queued behind A)"
    G "$WORK" push -q origin dev:main

    step "run A executes (at $(G "$WORK" rev-parse --short "$C0")):"
    release_new "$C0"

    step "run B executes (at $(G "$WORK" rev-parse --short "$C1")):"
    if release_new "$C1"; then
        echo "RACE FAILED: run B published a second release it should have refused." >&2
        exit 1
    fi

    step "remedy — the ordinary release command, with dev now carrying A's bump:"
    G "$WORK" fetch -q origin
    G "$WORK" checkout -q dev
    G "$WORK" reset -q --hard origin/dev
    push_dev_to_main
    release_new

    say "RESULT"
    G "$WORK" fetch -q --tags --force origin
    MAIN="$(G "$WORK" rev-parse origin/main)"
    DEV="$(G "$WORK" rev-parse origin/dev)"
    step "tags on origin: $(G "$WORK" tag --list 'v*' --sort=v:refname | tr '\n' ' ')"
    step "main  $(G "$WORK" rev-parse --short "$MAIN")  <Version>$(read_version_at "$WORK" "$MAIN")"
    step "dev   $(G "$WORK" rev-parse --short "$DEV")  <Version>$(read_version_at "$WORK" "$DEV")"
    AHEAD="$(G "$WORK" rev-list --count "$DEV..$MAIN")"
    if [[ "$AHEAD" -ne 0 ]]; then
        bad "main has $AHEAD commit(s) dev does not have"; exit 1
    fi
    ok "main has 0 commits that dev does not — nothing wedged, nothing half-published"
    echo
    echo "RACE: run B refused before building; one push recovered it; two releases published."
    exit 0
fi

FAILED_AT=0
for i in $(seq 1 "$RELEASES"); do
    say "RELEASE $i"

    # A release is normally preceded by some work on dev.
    if [[ $i -gt 1 ]]; then
        G "$WORK" checkout -q dev
        G "$WORK" pull -q --ff-only origin dev 2>/dev/null || G "$WORK" reset -q --hard origin/dev
        echo "feature $i" >> "$WORK/src/GloomhavenVR/notes.txt"
        G "$WORK" add -A
        G "$WORK" commit -q -m "feat: work for release $i"
        G "$WORK" push -q origin dev:dev
        step "one commit of work landed on dev"
    fi

    if ! push_dev_to_main; then
        FAILED_AT=$i
        break
    fi

    if [[ "$MODE" == old ]]; then release_old || { FAILED_AT=$i; break; }
    else                          release_new || { FAILED_AT=$i; break; }
    fi
done

# ---------------------------------------------------------------------------------
say "RESULT"
G "$WORK" fetch -q --tags --force origin
MAIN="$(G "$WORK" rev-parse origin/main)"
DEV="$(G "$WORK" rev-parse origin/dev)"

step "tags on origin: $(G "$WORK" tag --list 'v*' --sort=v:refname | tr '\n' ' ')"
step "main  $(G "$WORK" rev-parse --short "$MAIN")  <Version>$(read_version_at "$WORK" "$MAIN")"
step "dev   $(G "$WORK" rev-parse --short "$DEV")  <Version>$(read_version_at "$WORK" "$DEV")"

AHEAD="$(G "$WORK" rev-list --count "$DEV..$MAIN")"
if [[ "$AHEAD" -eq 0 ]]; then
    ok "main has 0 commits that dev does not — main is a strict fast-forward of dev"
else
    bad "main has $AHEAD commit(s) dev does not have:"
    G "$WORK" log --format='      %h %s' "$DEV..$MAIN"
fi

echo
if [[ "$MODE" == old ]]; then
    if [[ "$FAILED_AT" -ne 0 ]]; then
        echo "OLD DESIGN: wedged at release $FAILED_AT, as expected. The bug is real."
        exit 0
    fi
    echo "OLD DESIGN did NOT wedge — the simulation no longer reproduces the bug." >&2
    exit 1
else
    if [[ "$FAILED_AT" -eq 0 && "$AHEAD" -eq 0 ]]; then
        echo "NEW DESIGN: $RELEASES consecutive releases, every dev:main push a fast-forward."
        exit 0
    fi
    echo "NEW DESIGN FAILED (wedged at release $FAILED_AT, main ahead by $AHEAD)." >&2
    exit 1
fi
