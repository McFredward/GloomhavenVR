#!/usr/bin/env bash
# Behaviour fingerprint for refactoring — proves what a change really touched.
#
# Automated source, wire and presentation tests cover specific contracts; headset
# behavior still needs hardware evidence. The compiled comparison additionally shows
# which production types changed, including changes outside the intended scope.
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
# `check` first runs SEVENTEEN checkers for the things the compiled form CANNOT show
# (CHARTER §3b; the count grew from four over three refactor rounds and this header lagged
# behind it). Each is pure text or a separate project, so none of them affects the
# snapshot; each can be run on its own:
#
#   scripts/patch-inventory.sh check   a patch class nobody registers ships INERT, and
#                                      docs/PATCH-INVENTORY.md must match the source
#   scripts/check-frame-order.sh       a per-frame reorder is an ordinary in-type diff,
#                                      i.e. the PASS condition for a Tier-1 motion
#   scripts/check-mirrors.sh           two constants deliberately not merged must not drift
#   scripts/wire-tests.sh              byte-exact packets: a Write+TryRead change made in
#                                      lockstep is invisible to a round trip and to the guard
#   scripts/check-bundle-format.sh     a bundle built by the WRONG editor loads nowhere and
#                                      fails silently into the procedural fallback
#   scripts/check-surface.py           a REMOVED config key reverts a player's tuned value with
#                                      no message; a reworded log marker costs a hardware round
#   scripts/check-partial-order.py     static field initialisers run in COMPILE order across the
#                                      parts of a partial type, and MSBuild sorts the glob
#   scripts/check-instrument-writes.py a diagnostic that writes state the MECHANISM reads can no
#                                      longer be gated off or retired — baseline, fails on new
#   scripts/check-hw-verify.py         a line a hardware round is WAITING ON must survive the
#                                      default log level; 331's quiet log silenced every one of
#                                      them and the next test round answered nothing.
#   scripts/check-desync-surface.py    a patch on a type the game dispatches NETWORK ACTIONS
#                                      into can turn its own exception into the GAME's
#                                      "Desynchronization occurred" dialog; each must be judged.
#   scripts/check-card-identity-mask.py  the FACE question and the NAMING question must stay two
#                                      predicates; folding them re-opens the ModBuild 477 leak
#   scripts/check-mirror-dials.py      a mirror that reads a LIVE config dial is reading the
#                                      VIEWER's copy; every such read carries a recorded verdict
#   scripts/check-enum-arrays.py       an array sized by a LITERAL against an enum that grew —
#                                      the index throws into a swallowing catch and a surface dies
#   scripts/check-tune-fields.py       a record-28 field id outside every width range silently
#                                      kills the WHOLE record; the sampler must also ascend
#   scripts/check-remote-defaults.py   a frozen remote constant must resolve to the same Defaults
#                                      entry as the local bind it mirrors
#   scripts/check-wire-coverage.py     a board-affecting dial ADDED with no wire record and no
#                                      annotated opt-out is a 1:1 breach nobody's own headset sees
#   scripts/check-options-coverage.py  a curated key nothing binds, a caption Loc lacks, or a key
#                                      joining a curated family without joining its heading
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GUARD="$ROOT/.planning/refactor/.guard"
BASE="$GUARD/baseline"
CURR="$GUARD/current"
DLL="$ROOT/src/GloomhavenVR/bin/Release/net472/GloomhavenVR.dll"
REFS="$ROOT/ressources/GH_Data/Managed"
if [[ ! -d "$REFS" ]]; then
    REFS="$ROOT/ressources/Managed"
fi

ROOT_FOR_HINT="$ROOT"

# A FRESH WORKTREE IS NOT A FAILING CHANGE, and until this guard existed it looked exactly
# like one. Both of the per-machine, gitignored inputs below are linked in by
# scripts/worktree-setup.sh, and neither absence announces itself usefully on its own:
# a missing game Managed folder surfaces as a bare "The directory ... does not exist", and a
# missing Directory.Build.props.user surfaces as a BadImageFormatException reading
# "Reference assemblies cannot be loaded for execution" — which names neither the file nor
# the remedy. Two parallel workers read those as real gate failures and went looking for a
# defect in their own change; one of them reported it, which is why this is here.
_worktree_hint() {
    echo "error: $1" >&2
    echo "       This looks like a worktree that was never set up, not a failing change." >&2
    echo "       Run:  bash scripts/worktree-setup.sh" >&2
    echo "       (it links libs/RuntimeDeps, libs/Natives, ressources/ and" >&2
    echo "        Directory.Build.props.user in from the main checkout)" >&2
    exit 1
}
[[ -d "$REFS" ]] \
    || _worktree_hint "game references are missing (expected ressources/GH_Data/Managed or ressources/Managed)"
[[ -e "$ROOT_FOR_HINT/Directory.Build.props.user" ]] \
    || _worktree_hint "Directory.Build.props.user is missing (the per-machine GameManaged path)"

if ! command -v dotnet >/dev/null 2>&1 && [[ -x "$HOME/.dotnet/dotnet" ]]; then
    export DOTNET_ROOT="$HOME/.dotnet"
fi
export PATH="${DOTNET_ROOT:-$HOME/.dotnet}:${DOTNET_ROOT:-$HOME/.dotnet}/tools:$PATH"

command -v dotnet    >/dev/null 2>&1 || { echo "error: dotnet SDK not found" >&2; exit 1; }
command -v ilspycmd  >/dev/null 2>&1 || { echo "error: ilspycmd not found (dotnet tool install -g ilspycmd)" >&2; exit 1; }

# Decompile the freshly built DLL into $1, with volatile content masked.
snapshot() {
    local out="$1"
    # TEST THE EXIT STATUS, NOT A GREP. This used to be
    #     dotnet build … | grep -E "error|Build FAILED" && { echo "error: build failed"; exit 1; }
    # and under `set -euo pipefail` that can NEVER fire on a failed build: pipefail makes the
    # pipeline's status dotnet's non-zero exit whenever dotnet fails, so `&&` skips the block, and
    # `set -e` does not act on the left-hand side of `&&`. The block fired only when dotnet exited
    # ZERO and happened to print the word "error". So a change that did not compile ran straight
    # on to ilspycmd, decompiled the PREVIOUS successful build still sitting at $DLL, and printed
    # "no compiled behaviour differs from the baseline" — a green verdict on a red build. Found by
    # the 2026-09 refactor's tooling review with three stubbed dotnets: {echo "error CS1"; exit 1}
    # continued, {exit 1} continued, {echo "error-prone"; exit 0} aborted. Now: exit status, and
    # the compiler's own error lines are echoed so the reason is in the same terminal.
    local build_out
    if ! build_out="$(dotnet build "$ROOT/GloomhavenVR.sln" -c Release -v quiet --nologo 2>&1)"; then
        printf '%s\n' "$build_out" | grep -E "error|Build FAILED" >&2 || printf '%s\n' "$build_out" | tail -n 20 >&2
        echo "error: build failed — nothing was snapshotted; the DLL at $DLL is the PREVIOUS build" >&2
        exit 1
    fi
    rm -rf "$out"; mkdir -p "$out"
    # HIDE THE XML DOC FILE FROM ilspycmd, and this line is load-bearing.
    #
    # GenerateDocumentationFile went on at 2026-08-27 so the compiler would check crefs. It also
    # writes GloomhavenVR.xml next to the DLL — and ilspycmd, finding it, folds every doc comment
    # back into the decompiled output. That silently DESTROYED this tool's most useful property:
    # comments do not reach the assembly, so "guard diff empty" used to PROVE a change was
    # comment-only, which is the entire Tier-0 argument of PLAN-2026-08.md. With the docs in the
    # snapshot, a comment edit is indistinguishable from a code edit.
    #
    # Caught the first time it mattered: a comments-only commit reported several hundred CHANGED
    # types, and the whole-snapshot diff showed every one of them was an ADDED doc comment.
    # Moving the file aside for the duration of the decompile keeps both properties — the compiler
    # still checks the crefs, and the snapshot still contains only what the assembly carries.
    local doc="${DLL%.dll}.xml"
    local docstash=""
    if [[ -f "$doc" ]]; then docstash="$doc.guardhidden"; mv "$doc" "$docstash"; fi
    ilspycmd -p -o "$out" -r "$REFS" "$DLL" >/dev/null
    if [[ -n "$docstash" ]]; then mv "$docstash" "$doc"; fi
    # The build stamp is the only thing that differs between two builds of the
    # same source — mask it so it never shows up as a false positive.
    # `|| true`: a zero-match grep exits 1, and under pipefail + `set -e` that would abort the
    # whole guard with no message the day the stamp stops matching. Same shape as the mask below.
    grep -rlZ 'built 20\|BuildTimeUtc' "$out" 2>/dev/null \
        | xargs -0 -r sed -i -E 's/[0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}:[0-9]{2}:[0-9]{2} UTC/<BUILD-TIME>/g' || true
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
    #
    # Found independently by the Batch A and Batch B workers, on the same day, because
    # both were running the tool constantly. Nobody had noticed while only taking
    # baselines on a clean tree — which is exactly the usage the defect was invisible to.
    # The branch token is optional in the pattern: BuildInfo carries the hash without it.
    find "$out" -name '*.cs' -print0 \
        | xargs -0 -r sed -i -E 's/\b[0-9a-f]{9,40}\b/<COMMIT>/g; s/<COMMIT>-dirty/<COMMIT>/g; s/build <COMMIT>( \[[^]]*\])?/build <COMMIT>/g'
    # ...and the SHORT hash, which the pattern above does not reach. BuildInfo.Commit is
    # `git rev-parse --short HEAD` — SEVEN characters — so it survived the 9-or-more rule and
    # every commit produced a permanent false CHANGED on BuildInfo.cs. Same defect class as the
    # `-dirty` suffix and the `[branch]` token above, same reason it matters: a checker that
    # always shows one red line teaches the reader to skim past the line that is real.
    #
    # Lowering the generic threshold to 7 would start masking ordinary hex words, so the two
    # revisions that can actually appear are masked BY VALUE: the baseline's recorded rev and
    # the current HEAD. Prefixes from 7 characters up, longest first so a shorter prefix cannot
    # eat the head of a longer match.
    local revs=""
    [[ -f "$GUARD/baseline.rev" ]] && revs="$(cat "$GUARD/baseline.rev")"
    revs="$revs $(git -C "$ROOT" rev-parse HEAD 2>/dev/null || true)"
    for rev in $revs; do
        [[ -n "$rev" ]] || continue
        for len in 40 12 10 9 8 7; do
            local short="${rev:0:$len}"
            [[ ${#short} -eq $len ]] || continue
            find "$out" -name '*.cs' -print0 \
                | xargs -0 -r sed -i "s/\\b$short\\b/<COMMIT>/g"
        done
    done
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
#
# MOVED IS A FAILURE ON A TYPE FILE — PLAN-2026-08.md R1, and it cost a near-regression:
# field initialisers run in declaration order, across partials that order is COMPILE order,
# so a reordered initialiser also classifies MOVED. CanvasConversion's field table came back
# with two statics swapped under a rule that accepted MOVED.
#
# THE ONE EXCEPTION, and it is not a type file. `GloomhavenVR.csproj` in the snapshot is
# SYNTHESISED BY ilspycmd from the assembly's AssemblyRef metadata table, whose order follows
# the order in which the compiler first touched each referenced assembly — i.e. SOURCE COMPILE
# ORDER. Move a file to another folder and that order changes, so a pure folder restructure
# reports `MOVED GloomhavenVR.csproj` with every type file byte-identical. The CLR resolves
# references by name; the table's order is not observable.
#
# The classifier already separates the two cases correctly: an ADDED or REMOVED reference is a
# genuine difference and comes back CHANGED, never MOVED. So `MOVED GloomhavenVR.csproj` alone,
# with 0 changed, is the expected result of moving files and nothing else. Verified when Core/
# was restructured: 6 differing lines, one <Reference> block, the two files identical as
# multisets, and no other file in the snapshot differing at all.
classify() {
    if diff -q <(sort "$1") <(sort "$2") >/dev/null 2>&1; then echo "MOVED  "; else echo "CHANGED"; fi
}

case "${1:-check}" in
    baseline)
        # A FRESH WORKTREE'S BASELINE IS A SYMLINK INTO THE MAIN CHECKOUT, and writing THROUGH it
        # would re-baseline every other worktree at once. scripts/worktree-setup.sh links
        # .guard/baseline and .guard/baseline.rev on purpose, so that `check` works in a new
        # worktree instead of aborting with "no baseline" — but a link that is fine to READ is not
        # fine to WRITE: five lanes ran in parallel on 2026-09-05 and any one of them taking a
        # baseline would have moved the reference under the other four, silently, with the damage
        # only visible as a `check` that suddenly reports nothing. This is the same hazard class as
        # the shared git stash, and the remedy is the same: break the shared link before writing,
        # so a baseline taken here is LOCAL to here. Nothing is lost — the main checkout keeps its
        # own, and this worktree simply stops borrowing it the moment it has one of its own.
        for shared in "$BASE" "$GUARD/baseline.rev"; do
            if [[ -L "$shared" ]]; then
                echo "note: $(basename "$shared") was linked from the main checkout; unlinking so this baseline stays local" >&2
                rm -f "$shared"
            fi
        done
        snapshot "$BASE"
        echo "baseline: $(find "$BASE" -name '*.cs' | wc -l) types from $(git -C "$ROOT" rev-parse --short HEAD)"
        git -C "$ROOT" rev-parse HEAD > "$GUARD/baseline.rev"
        # The compiled form cannot show a removed config key or a reworded log marker, and both
        # are silent failures on a USER'S machine (CHARTER §3b.3). check-surface.py censuses them
        # from the source text; it needs a before-snapshot to diff against, and this is it.
        python3 "$ROOT/scripts/check-surface.py" snapshot "$GUARD/surface.json"
        ;;
    check)
        [[ -d "$BASE" ]] || { echo "error: no baseline — run 'refactor-guard.sh baseline' first" >&2; exit 1; }
        # --- the blind spots (CHARTER §3b) -------------------------------------------
        # These fourteen read-only gates share no generated outputs. The source group in
        # test-suites.json runs them with bounded concurrency and retains each full log;
        # any failed gate prevents wire tests, surface comparison and compiled snapshot.
        # Historical reasons for the individual gates remain below. Build/snapshot and
        # baseline writes are deliberately outside the parallel group.
        # These run BEFORE the build, because they check things the compiled form
        # cannot show: a patch class nobody registers still compiles and ships inert,
        # and a reordered per-frame step is an ordinary in-type diff. Both are text
        # checks; neither reaches the DLL, so neither affects the snapshot below.
        # A file RENAME can change a value. Static field initialisers run in declaration order,
        # which across the parts of a partial type is COMPILE order — and MSBuild sorts the glob
        # OrdinalIgnoreCase, so WallSegmentFade.cs compiles in the MIDDLE of its own fifteen
        # parts. This asserts no initialiser depends on another part, which makes the order
        # irrelevant instead of merely stable.
        # Instrumentation is ~9.5 % of all method code here and grows every hardware round. A
        # diagnostic that WRITES state something non-diagnostic READS cannot be switched off or
        # deleted — deleting one such Log* method once nearly latched the wall fade off forever.
        # The 66 that exist today are accepted in a baseline and are Phase 5's work list; this
        # fails only on a NEW one.
        # The OTHER half of the same guarantee. check-remote-defaults.py catches a mirrored
        # constant drifting from the default it copies; this catches a board-affecting dial being
        # ADDED with no wire coverage and no annotated opt-out — the failure that actually kept
        # happening, because from inside your own headset your board is always right.
        # …and the THIRD half of it, which the first two could not see: a dial that IS wired, to an
        # id outside every width range or out of the sampler's ascending order. Neither is visible
        # in a build, a golden vector or the config surface, and the first one silently stops the
        # WHOLE board-tuning record — see NetProtocol.TuneNeverLive248 for the build it shipped on.
        # An exception thrown from a mod patch that sits on a type the game dispatches NETWORK
        # ACTIONS into is not logged as a mod bug: ActionProcessor catches it and shows the player
        # the GAME's "Desynchronization occurred" dialog, then kills the session. The mod patches
        # the five heaviest receivers in that table. Every such patch must carry a recorded verdict
        # in docs/NET-ACTION-SURFACE.md; this fails on a NEW one nobody has read.
        # ModBuild 331 made the log quiet, correctly, by moving VRLog.Info to the DEBUG tier. The
        # first hardware test after it (334) came back with FIFTEEN mod lines and answered NOTHING:
        # every question in the backlog was written with VRLog.Info. A line a hardware round is
        # waiting on must survive the DEFAULT level, and nothing noticed that it no longer did.
        # ModBuild 339 built the bar-height dial the user asked for; his report after ModBuild
        # 347 was that he COULD NOT FIND IT. It had gone in uncurated, so it fell through to the
        # raw Erweitert list among five hundred others. A setting that exists and cannot be
        # reached is not a shipped setting. This fails on a curated key nothing binds, a caption
        # Loc.cs lacks, an unargued duplicate, and -- the actual 339 defect -- a key joining a
        # curated family WITHOUT joining its heading. That last one is a frozen backlog gated on
        # the DELTA, the same idiom check-instrument-writes.py uses.
        # "May a peer SEE this card's face" and "may a prompt NAME it in words" must never
        # become one predicate again. The lint existed — in tests/GloomhavenVR.WireTests, which
        # is compile-only in both workflows because its NEIGHBOURS need the game's real
        # UnityEngine.CoreModule.dll. This one is a pure text lint over two files and runs
        # anywhere; it now runs in ci.yml and release.yml too. See review R1 F2, 2026-09-07.
        # The 1:1 question none of the three existing guards can express. They ask "is this
        # value declared once" and "is it on the wire"; this asks WHOSE COPY the mirror reads at
        # runtime. Review R2, 2026-09-07, ran a 1:1 audit with all three green and found four
        # confirmed viewer-dial reads on the mirror side that no gate had any shape for.
        # An enum grew by two members and the latch arrays keyed by it kept their literal size.
        # The index throws, a catch swallows it and logs at the DEBUG tier, and two of six
        # card-FX surfaces were dead with nothing in the log at the shipped level (R2 F1, 479).
        python3 "$ROOT/scripts/run-test-suites.py" --group source \
            || { echo "error: a source gate failed (see per-suite logs above)" >&2; exit 1; }
        "$ROOT/scripts/wire-tests.sh" \
            || { echo "error: the wire format changed (see above)" >&2; exit 1; }
        "$ROOT/scripts/check-bundle-format.sh" \
            || { echo "error: the committed bundle cannot be read by the game (see above)" >&2; exit 1; }
        # The user-facing surface the assembly does not carry: config keys (a removed key does not
        # error — the player's tuned value is simply never read again and their setting reverts
        # without a message) and log markers (a marker that quietly changed spelling reads as "the
        # feature did not run" and costs a hardware round). REMOVAL fails; additions are free.
        [[ -f "$GUARD/surface.json" ]] \
            || { echo "error: no surface baseline — run 'refactor-guard.sh baseline' first" >&2; exit 1; }
        python3 "$ROOT/scripts/check-surface.py" snapshot "$GUARD/surface.current.json" \
            || { echo "error: surface census failed" >&2; exit 1; }
        python3 "$ROOT/scripts/check-surface.py" diff "$GUARD/surface.json" "$GUARD/surface.current.json" \
            || { echo "error: a config key, log marker or patch registration was REMOVED (see above)" >&2; exit 1; }
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
            # The exit code carries the verdict here too. `diff … || true` made plain `check`
            # exit 0 on a differing snapshot, so `refactor-guard.sh check && commit` passed on
            # anything; only --summary had a real status. Plain check has no MOVED/CHANGED
            # classification, so ANY difference is non-zero — use --summary for the distinction.
            if diff -ru "$BASE" "$CURR"; then
                echo "=== no compiled behaviour differs from the baseline ==="
            else
                echo "=== the compiled form differs from the baseline (see above; --summary classifies) ===" >&2
                exit 1
            fi
        fi
        ;;
    *)
        echo "usage: refactor-guard.sh {baseline|check [--summary]}" >&2; exit 1 ;;
esac
