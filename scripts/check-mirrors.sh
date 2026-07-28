#!/usr/bin/env bash
# Mirrored-constant lint — the cheap alternative to merging constants across a layer.
#
# WHY A LINT AND NOT A SHARED CONSTANT
# ------------------------------------
# INVARIANTS §15 lists two mirrored-constant pairs as "Tier 2 and safe to merge".
# REVIEW-Hands-Board-Core §P3 verified both and recommended AGAINST merging: they
# straddle Hands <-> Board, so a shared constant means either promoting a Hands
# `private const` to `internal` (leaking an interactor detail into the frozen P2
# surface) or inventing a Core constants file that neither owner reads naturally.
# Both make the layering worse, and the second guarantees nobody finds the value
# when tuning.
#
# The failure mode is not "there are two constants". It is "someone tunes one copy".
# A shared constant fixes that; so does this, at nil risk and with the layering
# intact. Each site keeps its own doc comment explaining what the number means
# THERE, which a shared constant would have flattened.
#
# Each group below must hold ONE value across every listed site.
# Run by scripts/refactor-guard.sh check.
set -uo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
S="$ROOT/src/GloomhavenVR"

# group-name : file:ConstName [file:ConstName ...]
#
# Note the FOUR-way group. REVIEW-Hands-Board-Core §P3 named three PAIRS; a repo-wide
# sweep for the value found two more copies of the fingertip radius — WorldUI's
# ButtonCluster and Cards' PlayTray — each of whose doc comment already says
# "mirror of PokeInteractor.FingertipRadius". So the fingertip radius has FOUR copies
# across FOUR subsystems (Hands/Board/WorldUI/Cards), not the two the review named: the
# review undercounted, which makes the lint worth more, not less.
#
# Re-verified at Batch D by sweeping every `const float ... = 0.008f` in src/. The only
# other 8 mm constants are DecisionDockSurface.BarClearanceMeters (grab-bar-to-prompt gap)
# and ButtonTuning.DefaultRoundTravel (authored cluster-cap travel) — same number, unrelated
# meaning, deliberately NOT in this group. An earlier draft of this header said "FIVE-way";
# the table below has always listed four sites and four is correct.
MIRRORS=(
  "grab reach (INVARIANTS §15) : Hands/Interact/ProximityGrabber.cs:ReachMeters Board/FigureGrab/FigureGrabDriver.cs:ReachMeters"
  "fingertip contact radius (INVARIANTS §3) : Hands/Interact/PokeInteractor.cs:FingertipRadius Board/BoardClickDriver.cs:ContactDepth WorldUI/ButtonCluster.cs:FingertipRadius Cards/PlayTray.7.Nested.cs:FingertipRadius"
  "poke release range (INVARIANTS §3) : Hands/Interact/PokeInteractor.cs:ReleaseRange Board/BoardClickDriver.cs:ReleaseDepth"
  # Thumbstick scrolling exists at THREE sites, one per UI presentation: converted
  # world-space canvases (RayUguiDriver), the floated results window (ModalFallback) and
  # the flat composite that carries the main menu (FlatScreen). Same gesture, same felt
  # speed — the whole point is that a list scrolls identically wherever it is shown, so
  # the deadzone and the notch rate must be tuned together.
  "thumbstick scroll deadzone : Hands/Interact/RayUguiDriver.cs:ScrollDeadzone WorldUI/ModalFallback.5.ResultsScroll.cs:ResultsScrollDeadzone WorldUI/FlatScreen.6.Pointer.cs:StickScrollDeadzone"
  "thumbstick scroll speed (wheel notches/s) : Hands/Interact/RayUguiDriver.cs:ScrollNotchesPerSecond WorldUI/ModalFallback.5.ResultsScroll.cs:ResultsScrollNotchesPerSecond WorldUI/FlatScreen.6.Pointer.cs:StickScrollNotchesPerSecond"

  # The graphics-jobs handshake: the PRELOADER publishes what the engine actually booted
  # with (read before it edits boot.config) and the PLUGIN reports it. They are separate
  # assemblies and the plugin deliberately does not link the patcher — it has to degrade to
  # "unknown" when the patcher is absent, which a hard reference could not express — so the
  # variable NAME is duplicated as a literal. A rename on one side alone would silently turn
  # the diagnostic back into the thing it was built to replace: a line reporting a state it
  # cannot see. That happened once already (2026-07-28) and this is why it cannot again.
  "graphics-jobs session handshake (env var name) : Preload/Patcher.cs:SessionStateVariable Core/OpenXRBootstrap.cs:SessionGraphicsJobsVariable"
)

fail=0
for entry in "${MIRRORS[@]}"; do
    group="${entry%% : *}"; sites="${entry#* : }"
    first=""; report=""
    for site in $sites; do
        file="$S/${site%%:*}"; name="${site##*:}"
        # A site may live in the sibling preloader project (it is a separate assembly
        # the plugin deliberately does not link — see the graphics-jobs group below).
        [[ "${site}" == Preload/* ]] && file="$ROOT/src/GloomhavenVR.${site%%:*}"
        # float OR string: the graphics-jobs handshake mirrors an environment-variable NAME,
        # which is exactly as breakable by a rename as a tuning constant is by a retune.
        value="$(sed -nE "s/.*const[[:space:]]+(float|string)[[:space:]]+${name}[[:space:]]*=[[:space:]]*([^;]+);.*/\2/p" "$file")"
        if [[ -z "$value" ]]; then
            echo "error: mirrored constant ${site} not found — did it move or get renamed?" >&2
            fail=1; continue
        fi
        report+="    ${site} = ${value}"$'\n'
        [[ -z "$first" ]] && first="$value"
        [[ "$value" != "$first" ]] && fail=2
    done
    if [[ $fail -eq 2 ]]; then
        echo "error: mirrored constants disagree — ${group}" >&2
        printf '%s' "$report" >&2
        echo "  These are deliberately NOT merged (layering: Hands <-> Board); the price of" >&2
        echo "  that decision is that they must be tuned together. Change all of them, or" >&2
        echo "  document why they may now diverge." >&2
        fail=1
    fi
done

[[ $fail -eq 0 ]] && echo "mirrors: ${#MIRRORS[@]} mirrored-constant groups agree"
exit $fail
