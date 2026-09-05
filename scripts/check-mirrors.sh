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
# Note the THREE-way group. REVIEW-Hands-Board-Core §P3 named three PAIRS; a repo-wide
# sweep for the value found two more copies of the fingertip radius — WorldUI's
# ButtonCluster and Cards' PlayTray — each of whose doc comment already says
# "mirror of PokeInteractor.FingertipRadius". The review undercounted, which makes the
# lint worth more, not less. The WorldUI copy went with ButtonCluster.cs on 2026-08-25
# (the turn-flow cap group the user retired), so the radius now has THREE copies across
# THREE subsystems — one FEWER place to retune, which is the direction this file wants.
#
# Re-verified at Batch D by sweeping every `const float ... = 0.008f` in src/. The only
# other 8 mm constant is DecisionDockSurface.BarClearanceMeters (grab-bar-to-prompt gap) —
# same number, unrelated meaning, deliberately NOT in this group. (ButtonTuning's
# DefaultRoundTravel was a second such near-miss and retired with [RoundButtons].) An earlier
# draft of this header said "FIVE-way"; the table below listed four sites until the WorldUI
# one was deleted with its file, and lists three now.
MIRRORS=(
  # THIRD site added 2026-08 with the fan-sweep unification: Cards/FanSweep.PalmReachMeters is the
  # PALM candidacy gate of every card fan's hand sweep, and its whole contract is "a card the sweep
  # considers is exactly a card the ProximityGrabber could take" — pop and grab must not disagree.
  # That contract IS this number being equal to the grabber's reach, so a retune of one and not the
  # other silently re-opens "what lights up is not what I grab". (It replaced THREE unlinted copies:
  # CardsDriver.ContactPalmReach, PileBrowser.ContactPalmReach, ItemsPile.ContactPalmReach.)
  # THE 1:1 RULE'S OWN OBJECT. The user's ruling is about the control board: a peer sees the
  # owner's board at the same position AND THE SAME SIZE. These are the board's dimensions,
  # and they were written out three times — once locally and once in each of the two remote
  # renderers — with nothing tying them together. RemoteControlBoard's own declaration states
  # the intent ("in the same card real-metre units as the local board (PlayTray BoardW/H), so
  # scaling by the owner's BoardScale reproduces their board's world size"), which is exactly
  # the kind of agreement that survives until someone retunes one of the three. Added by the
  # 2026-08-27 1:1 audit.
  "control board width (the 1:1 rule's own object) : Cards/Tray/PlayTray.6.Build.cs:BoardW Net/Remote/RemoteControlBoard.cs:BoardW Net/Remote/RemoteBoardFurniture.cs:BoardW"
  "control board height (the 1:1 rule's own object) : Cards/Tray/PlayTray.6.Build.cs:BoardH Net/Remote/RemoteControlBoard.cs:BoardH Net/Remote/RemoteBoardFurniture.cs:BoardH"
  "grab reach (INVARIANTS §15) : Hands/Interact/ProximityGrabber.cs:ReachMeters Board/FigureGrab/FigureGrabDriver.cs:ReachMeters Cards/FanSweep.cs:PalmReachMeters"
  # FOURTH SITE added 2026-09-05 with the map table's depth-fire (R5): MapButtonRail's caps now
  # measure their own travel from the fingertip, which they could only start doing by carrying this
  # radius. It is a copy rather than a reference on PokeInteractor.FingertipRadius's own written
  # ruling (exporting it "would either leak an interactor private onto the frozen P2 surface or
  # hide the number in a Core file nobody opens when tuning"), which is exactly the trade this
  # group is the price of. It must match: the interactor decides at this radius that the finger is
  # touching the cap at all, and the rail measures the cap's travel from the same radius, so a
  # mismatch is a cap that moves before it is hovered or one that can never reach its fire depth.
  "fingertip contact radius (INVARIANTS §3) : Hands/Interact/PokeInteractor.cs:FingertipRadius Board/BoardClickDriver.cs:ContactDepth Cards/Tray/PlayTray.7.Nested.cs:FingertipRadius WorldUI/MapRoom/MapButtonRail.cs:FingertipRadius"
  "poke release range (INVARIANTS §3) : Hands/Interact/PokeInteractor.cs:ReleaseRange Board/BoardClickDriver.cs:ReleaseDepth"
  # ModBuild 200: the fixed-fit branch reports legibility in arc-minutes, which needs the reading
  # distance. ModalFallback owns the real one and keeps it private, so the fit carries a copy. The
  # copy is DIAGNOSTIC-ONLY — no placement, scale or seat reads it — so a drift costs a wrong number
  # in a log line and can never move a window. Linted anyway, because a legibility figure nobody can
  # trust is worse than none: the whole "is the text big enough" argument is settled from that line.
  "window reading distance : WorldUI/Modal/ModalFallback.1.Core.cs:WindowDistanceMeters WorldUI/Conversion/CanvasConversion.3.Fit.cs:FixedFitReadingDistanceMeters"
  # THE "map room table rim" GROUP IS GONE, and deleting it rather than keeping it is the outcome
  # this file keeps recommending. ModBuild 197 added it for a pair — MapLocationInteractor.TableRimMeters
  # and MapRoomBenches.TableRimMeters — on the premise that the map room's table is not an object and
  # both sites therefore had to DEFINE it as the parchment's AABB widened by the same rim. ModBuild 198
  # falsified the premise: the bench class's own neighbour survey found the game's tabletop standing
  # right there ('GH_Map_TableTop_Lg', a 1.55 x 2.30 m slab flush under the map — the MAP SCENE REPORT
  # census that said otherwise is scoped to the map ROOTS, and the table is a sibling). The benches
  # were removed by user ruling and their replacement, MapTableLegs, measures the real renderer instead
  # of modelling one, so the rim now has exactly ONE site (MapLocationInteractor's laser footprint)
  # and there is nothing left to drift.
  # Thumbstick scrolling exists at THREE sites, one per UI presentation: converted
  # world-space canvases (RayUguiDriver), the floated results window (ModalFallback) and
  # the flat composite that carries the main menu (FlatScreen). Same gesture, same felt
  # speed — the whole point is that a list scrolls identically wherever it is shown, so
  # the deadzone and the notch rate must be tuned together.
  # FOURTH SITE since 2026-08-23: the laser-carry REEL (PanelGrabHandle.TickCarryReel) winds a
  # laser-held window's distance with the same thumb, in the same posture — trigger held down,
  # hand pointing at a surface — as all three scroll paths. Its comment says it mirrors
  # RayUguiDriver's number; this is that claim made machine-checked rather than left as prose.
  # No new GROUP: this is one more site on the value the group already owns.
  "thumbstick scroll deadzone : Hands/Interact/RayUguiDriver.cs:ScrollDeadzone WorldUI/Modal/ModalFallback.5.ResultsScroll.cs:ResultsScrollDeadzone WorldUI/FlatScreen/FlatScreen.6.Pointer.cs:StickScrollDeadzone WorldUI/Grab/PanelGrab.cs:ReelDeadzone"
  "thumbstick scroll speed (wheel notches/s) : Hands/Interact/RayUguiDriver.cs:ScrollNotchesPerSecond WorldUI/Modal/ModalFallback.5.ResultsScroll.cs:ResultsScrollNotchesPerSecond WorldUI/FlatScreen/FlatScreen.6.Pointer.cs:StickScrollNotchesPerSecond"

  # The remote board's INERT keycaps (Net/RemoteBoardFurniture.InertCap) rebuild the local
  # board's beveled-keycap look — same mesh (CardMesh.BuildBeveledKeycap), same materials
  # (PlayTray.NewKeycapMaterial) — but the tint RECIPE and the cap seat live as private
  # authored constants inside PlayTray.BoardButton, and promoting them was rejected there
  # ("Promoting them would mean widening that surface"). The price is these mirrors: retune
  # the local keycap look and the remote boards must follow, or the two boards drift apart.
  # (The two Color statics of the recipe — WallWarm / BevelHighlight — cannot be linted by
  # this float/string-only extractor; they are called out as mirrors in both doc comments.)
  "keycap cap seat Z : Cards/Tray/PlayTray.7.Nested.cs:CapRestZ Net/Remote/RemoteBoardFurniture.cs:CapRestZ"
  # THE "keycap bevel width" GROUP IS GONE (2026-08-25, board-button round 2), and again by
  # DELETING BOTH COPIES rather than keeping them in step — which is the outcome this file keeps
  # recommending. The pair was PlayTray.SquareCapBevel and RemoteBoardFurniture.CapBevel, one 7 mm
  # chamfer width in METERS on each side. Round 2 replaced the single chamfer with a five-zone
  # SIGNET profile whose zones are FRACTIONS of the cap's own short side (Cards/Caps/CapFaceLayout.cs:
  # BezelChamfer/BezelRim/BezelStep), computed inside CardMesh.BuildBeveledKeycap — which both
  # boards already call. Neither side passes a bevel any more, so there is nothing left to mirror.
  # (The fixed 7 mm was itself the defect: it is 0.159 of the Bronze cap's short side and 0.113 of
  # Steel's, so "one constant" was never one proportion.)
  "keycap wall tint factor : Cards/Tray/PlayTray.7.Nested.cs:WallTintFactor Net/Remote/RemoteBoardFurniture.cs:WallTintFactor"
  "keycap wall warm lerp : Cards/Tray/PlayTray.7.Nested.cs:WallWarmLerp Net/Remote/RemoteBoardFurniture.cs:WallWarmLerp"
  "keycap bevel highlight lerp : Cards/Tray/PlayTray.7.Nested.cs:BevelLerp Net/Remote/RemoteBoardFurniture.cs:BevelLerp"
  # THE MIRRORED KEYCAP PRESS SPRING IS GONE (2026-08-25, the board-button overhaul), and this is
  # the third time a group has been closed by DELETING the second copy rather than by keeping it
  # in step. It was one pair this extractor could not reach anyway, for the opposite reason to the
  # colours below: the LOCAL half was not a named constant at all, just an inline
  # `Time.deltaTime * 6f` in BoardButton.Update, with Net/RemoteCapFx.PressDecayPerSecond naming
  # it on the other side so that the remote code was at least readable. The user asked for the
  # press animation to be made good ("Auch Drück-Animation … soll gut funktionieren"), and a
  # linear decay from an instantaneous drop has no shape to improve — so the shape became a
  # function, WorldUI.ButtonTuning.PressDepth01, which BOTH sides now call off a phase in seconds.
  # One recipe, two consumers, nothing to drift. The AUTHORED travels and the ButtonAnim durations
  # the same 2026-08-08 round mirrored ARE machine-checked — by scripts/check-remote-defaults.py,
  # which is the right lint for a Defaults-backed pair.
  #
  # RemoteCapFx.AppearFadeFloor USED TO BE the second such pair (the local half being an inline
  # `Mathf.SmoothStep(0.15f, 1f, k)`). It is GONE — and deleted rather than linted, which is what
  # this file keeps recommending. The 2026-08-09 invisible-cap round replaced that
  # multiply-toward-black fade with the shared assembly ramp in WorldUI/ButtonTuning.cs
  # (AssemblyColor / AssemblyPhase / AssemblyDust), which the board keycaps, the cluster caps AND
  # the remote board's inert caps all CALL. One recipe, three consumers, nothing to drift — the
  # same resolution DecisionDockSurface.BarClearanceMeters got.
  #
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
  "dock fit floor : WorldUI/Surfaces/TablePanelSurfaces.cs:MinDensityScale Net/Remote/RemoteWidgetMirror.cs:MinDensityScale"
  "dock fit ceiling : WorldUI/Surfaces/TablePanelSurfaces.cs:MaxDensityScale Net/Remote/RemoteWidgetMirror.cs:MaxDensityScale"
  # The CONTENT-FIT ALPHA FLOOR is no longer a mirror and the group is GONE, which is the fix this
  # file keeps recommending rather than a coverage loss. The 2026-09-04 redundancy survey (row R27)
  # found the house "is this graphic painting?" floor of 0.05 existing FIVE times — CanvasConversion
  # .FitMinAlpha, RemoteWidgetMirror.FitMinAlpha (the two this group linted), PanelInkBounds
  # .FaintAlphaFloor, EnemyRevealSurface.DrawAlphaFloor, and a bare inline literal inside
  # ModalFallback.DrawsAnythingLoose that no lint could ever have seen. FitMinAlpha had been
  # `internal` since ModBuild 291 for exactly this, so the other four now ALIAS it and the inline
  # one names it. One value, five readers, nothing left to drift and nothing left to lint — the
  # same resolution DecisionDockSurface.BarClearanceMeters and RemoteCapFx.AppearFadeFloor got.

  # The DECISION DOCK's prompt anchor, mirrored by the remote board (decision-mirror round):
  # the owner's widget block hangs (bar bottom − BarClearanceMeters − DecisionGap) below the
  # board, and Net/RemoteBoardFurniture derives a peer's mirrored row from the same clearance.
  # (The bar-zone half-height and the 0.7 cluster dock scale are mirrored too — private
  # authored values inside PlayTray/ButtonCluster, called out in both doc comments; the zone
  # half is a derived expression the float extractor cannot read.) A drift here re-opens the
  # "detached ENTSCHEIDUNGEN plate" defect: the mirrored row stops hanging where the owner's
  # buttons really are.
  # (The bar clearance itself is no longer a MIRROR: the 1:1 decision-button round made
  # DecisionDockSurface.BarClearanceMeters internal and Net/RemoteBoardFurniture now aliases it
  # directly — `const float BarClearanceMeters = DecisionDockSurface.BarClearanceMeters` — so there
  # is one value and nothing left to drift. Deleting the group is the fix the lint exists to
  # provoke; the entry stayed listed only while two literals really existed.)
  # THE ITEM-USE RECESS, mirrored by the remote board (item-clip round, 2026-08-09). A card the
  # owner lays into their use recess is now drawn lying in the MIRRORED recess on every peer's copy
  # of that board (wire record 26), and to lie in it the same way it has to be fitted to the same
  # plate by the same rule. The plate factor is authored in PlayTray.BuildItemUseSlot and mirrored
  # twice — ItemsPile reads it to fit the owner's card, RemoteBoardFurniture builds the mirrored
  # plate from it — and the fill fraction and the settle duration are one value each across the
  # local card and its ghost. Retune one copy and a peer's card either overhangs the gold rim or
  # arrives on a different animation from the one its owner is watching, which is exactly the
  # divergence the 1:1 ruling forbids.
  "item-use recess inner plate : Cards/Piles/ItemsPile.cs:UseSlotInnerFactor Net/Remote/RemoteBoardFurniture.cs:UseSlotInnerFactor"
  "item-use recess card fill : Cards/Piles/ItemsPile.cs:UseSlotFillFraction Net/Remote/RemoteItemFan.cs:UseSlotFillFraction"
  "item-use clip settle seconds : Cards/Piles/ItemsPile.cs:ClipSettleSeconds Net/Remote/RemoteItemFan.cs:ClipSettleSeconds"
  "item chip release glide seconds : Cards/Piles/ItemsPile.cs:ReleaseGlideSeconds Net/Remote/RemoteItemFan.cs:ReleaseGlideSeconds"

  # THE TWO "cluster" GROUPS ARE GONE (2026-08-25), and this is what deleting a group looks
  # like when the ORIGINAL is deleted rather than retuned. "cluster proud seat" paired
  # ButtonCluster.ClusterProudOffset with RemoteBoardFurniture.ClusterProudLift, and "cluster dock
  # scale" paired PlayTray.ButtonClusterMountScale with RemoteBoardFurniture.ClusterDockScale. Both
  # existed so the peer's copy of the turn-flow SKIP cap could be seated exactly where the owner's
  # docked cluster put it. The user retired that cluster ("Ich möchte daher, dass die Button-Gruppe
  # der 'Überspringen Buttons' komplett verschwindet"); the skip cap is a generic board keycap in
  # the board's own third recess, so BOTH sides now solve its seat through the ONE shared
  # Cards.BoardAnchors — no constant, no copy, and therefore nothing left to lint.

  # The graphics-jobs handshake: the PRELOADER publishes what the engine actually booted
  # with (read before it edits boot.config) and the PLUGIN reports it. They are separate
  # assemblies and the plugin deliberately does not link the patcher — it has to degrade to
  # "unknown" when the patcher is absent, which a hard reference could not express — so the
  # variable NAME is duplicated as a literal. A rename on one side alone would silently turn
  # the diagnostic back into the thing it was built to replace: a line reporting a state it
  # cannot see. That happened once already (2026-07-28) and this is why it cannot again.
  "graphics-jobs session handshake (env var name) : Preload/Patcher.cs:SessionStateVariable Core/Startup/OpenXRBootstrap.cs:SessionGraphicsJobsVariable"

  # THE TWO BEAM TERMS (R28, added 2026-09-05). Neither was in any lint group, and the audit's
  # reading of the cost is exact: retune the reach in one place and it changes for four of six
  # controls. They are NOT merged into one constant, for this file's own standing reason — each
  # site's doc comment says what the number means THERE, and the layering is worth keeping — but
  # they must then be tuned together, which is exactly what a group is for.
  #
  # TWO SITES HAD TO BE NAMED BEFORE THEY COULD BE LINTED AT ALL, and that is the cheapest half of
  # the whole row: MapButtonRail carried the reach as a bare inline `20f` inside TickLaser, and had
  # no epsilon at all because it had no occluder veto to apply one to (see R4). An inline literal
  # is not merely unlinted, it is INVISIBLE: a grep for the constant name returns nothing, so the
  # site appears in no census of the term and no reader of the other five ever learns it exists.
  #
  # WHO IS DELIBERATELY NOT IN THE REACH GROUP. CombatLogSurface.MaxCapLaserMeters was a sixth 20 m
  # copy and left on 2026-09-05 (R12): it is a KEYCAP's reach and not the beam's, and it now reads
  # PlayTray.BoardButton.CapLaserReachMeters — a real shared constant, which needs no lint.
  # FlatScreen.6.Pointer.cs:68 holds an inline 0.005f that belongs in the epsilon group; it is not
  # here only because naming it is an edit to a file this round does not own.
  "beam reach, real metres at rig scale 1 : Hands/Interact/RayInteractor.cs:MaxDistanceMeters Hands/Interact/RayUguiDriver.cs:MaxDistanceMeters Hands/Interact/RayGrabDriver.cs:MaxDistanceMeters WorldUI/MapRoom/MapLocationInteractor.cs:MaxPickMeters WorldUI/MapRoom/MapButtonRail.cs:MaxCapLaserMeters"
  "beam occlusion epsilon, real metres at rig scale 1 : Hands/Interact/RayInteractor.cs:FanOcclusionEpsilonMeters Hands/Interact/RayUguiDriver.cs:OcclusionEpsilonMeters Hands/Interact/RayGrabDriver.cs:OcclusionEpsilonMeters Cards/Driver/CardsDriver.3.Laser.cs:FanOcclusionSlackMeters WorldUI/MapRoom/MapButtonRail.cs:SolidOccluderEpsilonMeters"
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
