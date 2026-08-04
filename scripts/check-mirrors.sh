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
  # THIRD site added 2026-08 with the fan-sweep unification: Cards/FanSweep.PalmReachMeters is the
  # PALM candidacy gate of every card fan's hand sweep, and its whole contract is "a card the sweep
  # considers is exactly a card the ProximityGrabber could take" — pop and grab must not disagree.
  # That contract IS this number being equal to the grabber's reach, so a retune of one and not the
  # other silently re-opens "what lights up is not what I grab". (It replaced THREE unlinted copies:
  # CardsDriver.ContactPalmReach, PileBrowser.ContactPalmReach, ItemsPile.ContactPalmReach.)
  "grab reach (INVARIANTS §15) : Hands/Interact/ProximityGrabber.cs:ReachMeters Board/FigureGrab/FigureGrabDriver.cs:ReachMeters Cards/FanSweep.cs:PalmReachMeters"
  "fingertip contact radius (INVARIANTS §3) : Hands/Interact/PokeInteractor.cs:FingertipRadius Board/BoardClickDriver.cs:ContactDepth WorldUI/ButtonCluster.cs:FingertipRadius Cards/PlayTray.7.Nested.cs:FingertipRadius"
  "poke release range (INVARIANTS §3) : Hands/Interact/PokeInteractor.cs:ReleaseRange Board/BoardClickDriver.cs:ReleaseDepth"
  # Thumbstick scrolling exists at THREE sites, one per UI presentation: converted
  # world-space canvases (RayUguiDriver), the floated results window (ModalFallback) and
  # the flat composite that carries the main menu (FlatScreen). Same gesture, same felt
  # speed — the whole point is that a list scrolls identically wherever it is shown, so
  # the deadzone and the notch rate must be tuned together.
  "thumbstick scroll deadzone : Hands/Interact/RayUguiDriver.cs:ScrollDeadzone WorldUI/ModalFallback.5.ResultsScroll.cs:ResultsScrollDeadzone WorldUI/FlatScreen.6.Pointer.cs:StickScrollDeadzone"
  "thumbstick scroll speed (wheel notches/s) : Hands/Interact/RayUguiDriver.cs:ScrollNotchesPerSecond WorldUI/ModalFallback.5.ResultsScroll.cs:ResultsScrollNotchesPerSecond WorldUI/FlatScreen.6.Pointer.cs:StickScrollNotchesPerSecond"

  # The remote board's INERT keycaps (Net/RemoteBoardFurniture.InertCap) rebuild the local
  # board's beveled-keycap look — same mesh (CardMesh.BuildBeveledKeycap), same materials
  # (PlayTray.NewKeycapMaterial) — but the tint RECIPE and the cap seat live as private
  # authored constants inside PlayTray.BoardButton, and promoting them was rejected there
  # ("Promoting them would mean widening that surface"). The price is these mirrors: retune
  # the local keycap look and the remote boards must follow, or the two boards drift apart.
  # (The two Color statics of the recipe — WallWarm / BevelHighlight — cannot be linted by
  # this float/string-only extractor; they are called out as mirrors in both doc comments.)
  "keycap cap seat Z : Cards/PlayTray.7.Nested.cs:CapRestZ Net/RemoteBoardFurniture.cs:CapRestZ"
  "keycap bevel width : Cards/PlayTray.6.Build.cs:SquareCapBevel Net/RemoteBoardFurniture.cs:CapBevel"
  "keycap wall tint factor : Cards/PlayTray.7.Nested.cs:WallTintFactor Net/RemoteBoardFurniture.cs:WallTintFactor"
  "keycap wall warm lerp : Cards/PlayTray.7.Nested.cs:WallWarmLerp Net/RemoteBoardFurniture.cs:WallWarmLerp"
  "keycap bevel highlight lerp : Cards/PlayTray.7.Nested.cs:BevelLerp Net/RemoteBoardFurniture.cs:BevelLerp"
  # Same story for the FOLLOW/PIN toggle's two STATE colours: the idle parchment
  # (PlayTray.BoardButton.IdleColor) and the accent brass (the _accentColor BuildDashboardControls
  # hands its pin button) are Color statics, which this float/string extractor cannot read — they
  # are mirrored as Net/RemoteBoardFurniture.PinIdleColor / PinAccentColor and called out in both
  # doc comments. The remote cap switches between them from the synced pinned bit, so retuning the
  # local pair and not the remote pair makes a peer's toggle read the wrong state colour.

  # The DOCK FIT, mirrored by the remote board's widget mirror. A peer's initiative track and
  # objectives panel are fitted by Net/RemoteWidgetMirror with the same three numbers
  # WorldUI's TrayMountedPanelSurface fits the local docks with — that is what makes a mirrored
  # panel the same SIZE, and (because the panel's lift above its mount is half its own height)
  # the same POSITION. They are private to their own layers by design; the price is these
  # mirrors. A drift here reproduces exactly the defect the mirror was rewritten to fix
  # (a peer's track floating far above their board).
  "dock fit floor : WorldUI/Surfaces/TablePanelSurfaces.cs:MinDensityScale Net/RemoteWidgetMirror.cs:MinDensityScale"
  "dock fit ceiling : WorldUI/Surfaces/TablePanelSurfaces.cs:MaxDensityScale Net/RemoteWidgetMirror.cs:MaxDensityScale"
  "content-fit alpha floor : WorldUI/CanvasConversion.3.Fit.cs:FitMinAlpha Net/RemoteWidgetMirror.cs:FitMinAlpha"

  # The DECISION DOCK's prompt anchor, mirrored by the remote board (decision-mirror round):
  # the owner's widget block hangs (bar bottom − BarClearanceMeters − DecisionGap) below the
  # board, and Net/RemoteBoardFurniture derives a peer's mirrored row from the same clearance.
  # (The bar-zone half-height and the 0.7 cluster dock scale are mirrored too — private
  # authored values inside PlayTray/ButtonCluster, called out in both doc comments; the zone
  # half is a derived expression the float extractor cannot read.) A drift here re-opens the
  # "detached ENTSCHEIDUNGEN plate" defect: the mirrored row stops hanging where the owner's
  # buttons really are.
  "decision prompt bar clearance : WorldUI/Surfaces/DecisionDockSurface.cs:BarClearanceMeters Net/RemoteBoardFurniture.cs:BarClearanceMeters"
  "cluster proud seat : WorldUI/ButtonCluster.cs:ClusterProudOffset Net/RemoteBoardFurniture.cs:ClusterProudLift"
  "cluster dock scale : Cards/PlayTray.1.Core.cs:ButtonClusterMountScale Net/RemoteBoardFurniture.cs:ClusterDockScale"

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
