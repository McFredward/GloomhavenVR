#!/usr/bin/env bash
# Mirrored-constant AND mirrored-expression lint — the cheap alternative to merging a value, and
# the enforcement of a merge once one has happened.
#
# PART 1 (MIRRORS) lints CONSTANTS that are deliberately NOT merged: they must hold one value.
# PART 2 (EXPRESSIONS), added 2026-09-07 with the sharing ruling, lints an expression that HAS
# been merged: a second implementation of it must fail the build. See that part's own header —
# every duplication defect the 2026-09-07 review found was in an expression, not a number.
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
  # 2026-09-07, the scenario SPECIAL RULES round. The rules section is stacked under the objectives
  # on BOTH boards — locally by ScenarioRulesSurface.MountOffset, on a peer's board by
  # RemoteObjectivesPanel.SeatRules — and this gap is the only thing that decides how far under.
  # It straddles WorldUI <-> Net, so a shared constant would mean routing a surface's protected
  # geometry into Net/ (rejected there for the same reason ObjectivesDensityScale is a linted-style
  # copy). It MUST match: an owner and a peer that disagreed about it would render one panel as two
  # different pictures, which is the 1:1 ruling broken by a number nobody would think to check.
  "objectives→rules stack gap (1:1) : WorldUI/Surfaces/TablePanelSurfaces.cs:StackGapMeters Net/Remote/RemoteObjectivesPanel.cs:ScenarioRulesStackGap"
  # Same pair, second number: the section's HEIGHT BUDGET. The dock fit divides this by the measured
  # content, so two different budgets render one paragraph at two different sizes on the two boards
  # — and this one also has a geometry job (clearing the element board's top edge), so a drift here
  # is a panel sitting over the elements on one client and not the other.
  "objectives→rules height budget (1:1) : WorldUI/Surfaces/TablePanelSurfaces.cs:RulesBudgetMeters Net/Remote/RemoteObjectivesPanel.cs:ScenarioRulesBudget"
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
  # THE ABILITY CARD'S RETURN FLIGHT (report item 2, 2026-09-05). "Ich sehe bei den remote Karten
  # nicht die Animation wie die Karte in die Hand zurueckkehrt, wenn man die Karte in die Hand nimmt
  # und irgendwo loslaesst." The owner's card is carried home by VRCard's standing home-lerp with
  # _releaseGlide holding it on UNSCALED time for this window; the mirrored fan replays the same
  # window on the same exponential. Retune one copy and a peer watches the card settle at a
  # different speed from the player who let go of it — the 1:1 divergence, one surface over from the
  # item chip's own entry directly above.
  "ability card release glide seconds : Cards/VRCard.cs:ReleaseGlideSeconds Net/Remote/RemoteHandFan.cs:ReleaseGlideSeconds"

  # THE "browse card release glide seconds" GROUP IS GONE (2026-09-07, the 1:1 re-audit), and this
  # is the sixth time a group has closed by DELETING the copy rather than keeping it in step. It was
  # added earlier the same day, pairing Cards/VRCard.cs:ReleaseGlideSeconds with
  # Net/Remote/RemoteBrowserFan.cs:ReleaseGlideSeconds. The copy armed a per-seat WINDOW whose only
  # job was to beat that fan's settled hard-assert branch, so a released slab could ease home
  # instead of teleporting. The re-audit deleted the hard assert instead — the mirrored arc now
  # eases EVERY slab EVERY frame, which is what the owner's PileBrowser.Relayout does at all five of
  # its `instant: false` call sites — and the window lost its only reader.
  #
  # THE MIRROR CONTRACT DID NOT SURVIVE EITHER, WHICH IS THE MORE USEFUL HALF. The OWNER's constant
  # names the window in which VRCard's home-lerp runs on UNSCALED time after a release. The whole
  # Net mirror already ticks on unscaled time (NetAvatarDriver.cs:868), so on that side there was
  # nothing for the window to switch: the copy named a term that does not exist there. What is
  # really mirrored is the RATE (the owner's [Cards] CardLerpSpeed, off record 28 — a wired dial,
  # not a constant, so not this file's business) and the SEED pose. RemoteHandFan's and
  # RemoteItemFan's copies are UNTOUCHED and their group above still stands: they have live readers.

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

  # ── FOUR GROUPS ADDED BY THE 2026-09-07 1:1 RE-AUDIT ────────────────────────────────────────────
  # The audit's finding was that this file pins 21 numbers while the Net mirrors re-spell dozens.
  # These four are the ones whose BOTH halves are already NAMED constants, so they cost one line
  # each. They all agree today; that is the point — a group is added while a pair agrees, not after
  # it has drifted.
  #
  # THE FIRST ONE HAS ALREADY DRIFTED ONCE, AND ITS OWN DOC COMMENT RECORDS IT. RemoteCardFx's arc
  # fraction sat at 0.28f under a comment reading "the value VRCard.FlyArcHeightFraction uses
  # locally, so the bow matches", while that constant had been 0.55f since it was raised for user
  # issue 3. Every mirrored play, discard and burn flight bowed at 50.9 % of the height its owner
  # watched, for every build until a review happened to catch it. That is exactly the failure this
  # file exists to prevent, already realised once, and it was unlinted the whole time.
  # THREE sites, not two: RemoteBrowserFan.CollapseArcFraction is the browse fold's copy of the
  # same term and it carried the SAME stale 0.28f under the SAME comment naming the constant it
  # was supposed to be. One defect, two files, one review to catch both — which is the argument
  # for the group rather than against it.
  "card flight arc fraction (1:1) : Cards/VRCard.cs:FlyArcHeightFraction Net/Remote/RemoteCardFx.cs:ArcFraction Net/Remote/RemoteBrowserFan.cs:CollapseArcFraction"
  # …and the FLOOR beside it. CardsDriver.BoardArcMin is `boardScale * CardHeight * 1.5f` with
  # the 1.5 as an INLINE literal, so the owner's half cannot be linted until somebody names it;
  # the two mirrors that copied it can be held to each other in the meantime, which at least
  # means a retune of one is caught by the other.
  "card flight arc floor, in card heights (1:1) : Net/Remote/RemoteCardFx.cs:MinArcCardHeights Net/Remote/RemoteBrowserFan.cs:CollapseMinArcCardHeights"
  # The RECESS CARD METRIC. RemoteControlBoard.CardW's own comment says "Defaults.CardWidth x
  # PlayTray.SlotScale" and NetProtocol.SlotCardWidthLegacy is the identical product — it is the
  # value a pre-record peer's recess cards are drawn at, so the two must be one number or an old
  # peer's board draws its cards at a size no sender ever meant.
  "legacy recess card width (1:1) : Net/NetProtocol.cs:SlotCardWidthLegacy Net/Remote/RemoteControlBoard.cs:CardW"
  # The two round recesses' PITCH — the owner's authored tray spacing and the mirror's fallback
  # layout for the same board. A drift puts a peer's two played cards further apart on every other
  # player's copy of their board than on their own, which is the 1:1 ruling's own object.
  "play slot spacing (1:1) : Cards/Tray/PlayTray.6.Build.cs:SlotSpacing Net/Remote/RemoteControlBoard.cs:SlotSpacing"
  # The pile stack's COUNT RING band, one number out of the ~40 that RemoteControlBoard.PileCounter
  # hand-copies from PileViewer.PileStack. It is the only one of the forty whose two halves are both
  # named constants; the rest are inline literals on one side or the other, and an inline literal is
  # not merely unlinted but INVISIBLE — a grep for the name returns nothing, so no reader of one
  # site ever learns the other exists. NAMING THEM IS THE PREREQUISITE FOR LINTING THEM and is
  # tracked as such: slab count/thickness/jitter/step/tilt, the 0.45 darken lerp, the count fit
  # (w*0.9, h*0.62, 0.30), the caption seat and fit, EmberColor, RingRevealSeconds 0.28 and all four
  # curves are the list.
  "pile count-ring band fraction (1:1) : Cards/Piles/PileViewer.cs:RingBandFraction Net/Remote/RemoteControlBoard.cs:RingBandFraction"

  # ── THE FANS, ONE MIRROR AT A TIME (same 2026-09-07 re-audit) ──────────────────────────────────
  # PAIRED PER FAN, NEVER MERGED ACROSS FANS, and that is deliberate. The hand fan, the pile-browse
  # fan and the item fan all happen to stagger at 0.004 and two of them happen to span 110 degrees,
  # but "two numbers are equal today" is not "two numbers must be equal" — merging those would
  # assert that retuning the ITEM fan's span must move the BROWSE fan's, which nobody has ruled and
  # which this project has a name for ("two fans, one name"). Each group below is one mirror
  # against ITS OWN owner, which is exactly the 1:1 contract and nothing more.
  #
  # NOT LISTED, BECAUSE THEY NEED NO LINT: the pop lift and pop scale (every fan already ALIASES
  # Cards.VRCard.PopUp / PopScale) and the browse arch/tilt factors (RemoteBrowserFan aliases
  # RemotePileFronts, which reads Cards/Piles/PileFanShape). A real shared constant beats a group;
  # this file keeps saying so and those five are the proof.
  "hand fan gaze-bias deadzone (1:1) : Cards/CardFan.cs:GazeBiasDeadzoneDeg Net/Remote/RemoteHandFan.cs:GazeBiasDeadzoneDeg"
  "hand fan gaze-bias release (1:1) : Cards/CardFan.cs:GazeBiasReleaseDeg Net/Remote/RemoteHandFan.cs:GazeBiasReleaseDeg"
  "hand fan gaze-bias full angle (1:1) : Cards/CardFan.cs:GazeBiasFullDeg Net/Remote/RemoteHandFan.cs:GazeBiasFullDeg"
  "hand fan gaze-bias max yaw (1:1) : Cards/CardFan.cs:GazeBiasMaxYawDeg Net/Remote/RemoteHandFan.cs:GazeBiasMaxYawDeg"
  "hand fan gaze-bias gain (1:1) : Cards/CardFan.cs:GazeBiasGain Net/Remote/RemoteHandFan.cs:GazeBiasGain"
  "hand fan gaze-bias smoothing (1:1) : Cards/CardFan.cs:GazeBiasSmoothing Net/Remote/RemoteHandFan.cs:GazeBiasSmoothing"
  # The per-card Z step that keeps a fan's DRAW ORDER stable. One pair per fan: a drift reverses the
  # overlap on a peer's copy of a fan its owner is reading front-to-back.
  "hand fan draw-order Z step (1:1) : Cards/CardFan.cs:ZStagger Net/Remote/RemoteHandFan.cs:ZStagger"
  "browse fan draw-order Z step (1:1) : Cards/Piles/PileBrowser.cs:ZStagger Net/Remote/RemoteBrowserFan.cs:ZStagger"
  "item fan draw-order Z step (1:1) : Cards/Piles/ItemsPile.cs:ZStagger Net/Remote/RemoteItemFan.cs:ZStagger"
  # …and each fan's ARC SPAN and card enlargement — the two numbers that decide how wide and how big
  # the fan reads. A peer whose browse fan spanned a different arc from its owner's would be looking
  # at a different picture of the same pile, which is the ruling's own words.
  "browse fan arc span (1:1) : Cards/Piles/PileBrowser.cs:MaxArcDegrees Net/Remote/RemoteBrowserFan.cs:MaxArcDegrees"
  "item fan arc span (1:1) : Cards/Piles/ItemsPile.cs:MaxArcDegrees Net/Remote/RemoteItemFan.cs:MaxArcDegrees"
  "browse fan card enlargement (1:1) : Cards/Piles/PileBrowser.cs:CardScale Net/Remote/RemoteBrowserFan.cs:CardScale"
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


# ==============================================================================================
# PART 2 — MIRRORED EXPRESSIONS (2026-09-07, the sharing ruling)
# ==============================================================================================
#
# Everything above lints CONSTANTS. Every duplication defect found in the 2026-09-07 review was
# in an EXPRESSION, and the worst of them had survived eight hardware rounds:
#
#   VRCard.FlyToPile eases a card flight with SMOOTHERSTEP and bows it with FlyArcOffset ON THAT
#   EASED TERM. RemoteCardFx, RemoteBurnFx and RemoteBrowserFan each wrote out plain SMOOTHSTEP
#   along the chord and bowed with sin(pi*t) on the RAW t. Four implementations of "the card
#   flight", three of them a different curve from the one the owner watches — same duration, same
#   peak, ~0.8 m apart at t = 0.25 on the arc the ModBuild 476 session measured. All three carried
#   a comment asserting they flew "the same shape as VRCard's fly".
#
# The maintainer's ruling: PURE MATH IS SHARED, CALLS GO DOWN (Net/ may call Cards/; Cards/ must
# never learn a mirror exists), THE DRAWN OBJECT IS NEVER SHARED. Once a shared expression exists,
# a SECOND IMPLEMENTATION of it must fail the build — which is this part.
#
# HOW A GROUP IS SHAPED, and why it is SCOPED rather than repo-wide. The smoothstep polynomial
# appears ~20 times in this repo (sound envelopes, fan weights, the window materialise field) and
# `Mathf.Sin(t * Mathf.PI)` appears in every card fan's SWAP DUCK. Those are different expressions
# that happen to share a formula, and a lint that fired on them would be noise, and noise gets
# deleted. So a group names the SURFACES that draw the one thing (here: a card flight) and forbids
# a hand-written curve THERE. Deliberately NOT in scope: RemoteHandFan / CardFan / ItemsPile (the
# fan swap-duck bow, a different animation over a different population — see the sharing ruling's
# "if you cannot show the population is the same, do not merge"), and RemoteBrowserFan's own
# Mathf.Sin(rad) fan ARCH layout, which is trigonometry and not an ease.
#
# Each group is one line, four | -separated fields:
#   name | WHAT TO CALL INSTEAD | ERE the copy would match | files in scope (relative to src/GloomhavenVR)
# The message tells the reader what to call, never what to delete — the whole point is that the
# next person writing a flight finds the one implementation instead of writing a fifth.
#
# Comments are stripped before matching, because this repository's comments quote the very
# expressions being censused (the paragraph above is itself an example, and the mirrors' own doc
# comments now spell out the curve they no longer fly).
EXPRESSIONS=(
  # The EASE. One implementation: VRCard.SmootherStep, reached from Net/ through
  # RemoteFlightCurve.Ease. Catches a hand-written smoothstep, a hand-written smootherstep, Unity's
  # Mathf.SmoothStep, and a hand-written quadratic ease-out — a flight surface easing with any of
  # the four is a second curve, whichever one it is.
  #
  # THE EASE-OUT ARM WAS ADDED AFTER THE GATE MISSED ITS OWN ROUND'S FOURTH COPY. The first draft
  # matched smoothstep and the sine bow, because those were the three mirrors it was seeded from.
  # CardsDriver's BurnSlab was in scope and PASSED: it eased with the quadratic instead and fed
  # FlyArcOffset the raw t, so it matched neither pattern while being exactly the defect the round
  # was about. A gate that is silent on the case its own round found is the "instrument shipped and
  # lying" shape, so the pattern was widened in the commit that fixed the site. Read it as a
  # standing instruction: when a new flight curve turns up, the fix and the arm that would have
  # caught it land together.
  "mirrored flight ease | VRCard.SmootherStep — in Cards/ call it directly, from Net/ go through RemoteFlightCurve.Ease(t); never the reverse | [A-Za-z_][A-Za-z0-9_.]* *\* *[A-Za-z_][A-Za-z0-9_.]* *\* *\( *3f? *-|\* *6f? *- *15f?|Mathf\.SmoothStep *\(|1f? *- *\( *1f? *- *[A-Za-z_][A-Za-z0-9_.]* *\) *\* | Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteBrowserFan.cs Cards/Driver/CardsDriver.4.Rebuild.cs"
  # The BOW. One implementation: VRCard.FlyArcOffset, reached from Net/ through
  # RemoteFlightCurve.Pose (which also carries the chord, so the bow and the slide can never again
  # be fed two different parameters — that mismatch, not the formula, was the visible defect).
  # Catches a half-sine bow and the parabola written out by hand.
  "mirrored flight bow | VRCard.FlyArcOffset(eased, up, arc) — in Cards/ call it directly, from Net/ go through RemoteFlightCurve.Pose(eased, from, to, up, arc); never the reverse | Mathf\.Sin *\([^)]*Mathf\.PI|4f? *\* *[A-Za-z_][A-Za-z0-9_.]* *\* *\( *1f? *- | Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteBrowserFan.cs Cards/Driver/CardsDriver.4.Rebuild.cs"
  # The ROTATION, and this one forbids a SOURCE rather than a formula - which is the only shape
  # that can catch it. A mirrored flight's pose is the OWNER's; nothing about it may be a
  # function of who is watching. RemoteCardFx billboarded a FACELESS slab at Camera.main until
  # 2026-09-07, so one flight tumbled differently on every watcher's machine and matched none of
  # them to the owner, whose VRCard.FlyToPile locks the captured rotation for the whole arc. It
  # fired on 3 of the host's 8 mirrored flights in the ModBuild 476 session. A local-camera read
  # cannot be 1:1 BY CONSTRUCTION, so there is no value to lint and no formula to share - only a
  # source to forbid.
  #
  # A BILLBOARD IS STILL ALLOWED; IT MUST JUST FACE THE OWNER'S HEAD. RemoteBrowserFan.TryFanPose
  # is the worked example (it reads _owner.HeadHolder and so reproduces the picture the owner is
  # reading rather than composing a new one per viewer), which is why its own
  # Quaternion.LookRotation is not what this group matches.
  "mirrored flight rotation | the OWNER's pose - _owner.BoardRotation, or _owner.HeadHolder for a billboard, never the local camera | Camera\.main|VRRigDriver\.HeadCamera | Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteBrowserFan.cs"
  # ── THE CARD-FACE DECISION (2026-09-07 face round) ───────────────────────────────────────────
  # Closed from TWELVE surfaces answering in FOUR ways down to one call: RevealGate.CardFaces (all
  # four overloads) answers both halves at once — WHICH face, and WHICH RULE chose it. A surface
  # that re-derives the rule by chaining FaceRule members through a conditional is answering the
  # question again instead of asking it, which is the same shape as the flight curve above: several
  # lookalike implementations of one question, each under a comment saying it matched.
  #
  # WHAT THIS PERMITS ON PURPOSE: a bare `= FaceRule.Something` INITIALISER seeding an `out`
  # parameter before the CardFaces call (RemoteBurnFx does exactly that). What it refuses is a
  # FaceRule member on either side of a `?` or a `:` — a decision, not a seed.
  #
  # RemoteControlBoard.cs IS NOW IN SCOPE, AND ITS ADMISSION IS THE GROUP'S ONLY REAL TEST SO FAR.
  # This group was written on the ModBuild 477 base, where that file still held two FaceRule ladders
  # (:2408-2410 and :2547-2549), and it was left out of the scope with a note saying "add it once
  # the face round lands". The face round landed, routed both ladders through RevealGate.CardFaces,
  # and the file was added here WITHOUT changing the pattern: it passes. That is worth more than
  # either change on its own — the lane that consolidated the decision and the lint that forbids a
  # second one agree, character for character, about where the single implementation lives. A
  # repo-wide sweep at the same moment finds ZERO FaceRule conditionals anywhere outside
  # Net/RevealGate.cs, and the only surviving RevealGate.ShowRoundCardFronts callers are the five
  # named in the group below, all of them outside a card surface.
  "mirrored card-face decision | RevealGate.CardFaces — ask it rather than re-deriving its FaceRule | FaceRule\\.[A-Za-z_][A-Za-z0-9_]* *[?:]|[?:] *([A-Za-z_][A-Za-z0-9_]*\\.)*FaceRule\\. | Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteBoardCard.cs Net/Remote/RemoteHandFan.cs Net/Remote/RemoteBrowserFan.cs Net/Remote/RemotePileFronts.cs Net/Remote/RemoteHeldCardFace.cs Net/Remote/RemoteActiveCards.cs Net/Remote/RemoteControlBoard.cs"
  # ── THE CARD'S LOOK (2026-09-07 evening round) ───────────────────────────────────────────────
  # The face round closed one question across twelve surfaces and nobody did the same for the
  # LOOK, so four of the maintainer's eight items that evening were the same defect again, one
  # surface at a time: a peer's discard fan drew fresh cards where the owner saw them greyed, a
  # burnt card alternated fire-on/fire-off forever, an active card went grey and then blue again a
  # round later, and a card taken into the hand lost its char entirely.
  #
  # ONE DURABLE IMPLEMENTATION: Cards.Art.BurnLookPolicy.ForCard / ForActivatedCard, which read the
  # card's pile and its spent halves and answer BurnLookPolicy.Look. Net/ reaches it through the
  # single map in Net.Remote.UsedCardLook.FromPolicy — calls go DOWN, per the sharing ruling. Any
  # OTHER mirrored surface that reaches for ECardPile itself is re-deriving that answer, which is
  # how RemotePileFronts came to carry `content == Content.Burnt` as a stand-in for it and shipped
  # a discard fan that asked for no look at all under a comment claiming the owner's own fan showed
  # those cards fresh. (It does not: SetPile(Discarded) runs GhostOutOnTimeline.)
  #
  # RemoteBoardCard.cs IS DELIBERATELY OUT OF SCOPE, and the exclusion is the interesting part.
  # Its ResolveUsedCardLook is the RECESS's live-ramp resolver, and its pile switch is that
  # surface's own membership test — is this card still seated in the round, or has it left — not a
  # second durable answer to "what look does this card wear". The sharing ruling's own bar applies
  # here in the direction that REFUSES a merge: two questions that look alike over different
  # populations stay two questions. If that file ever yields a look straight out of a pile test
  # without going through the policy, this note is the thing that has gone stale, not the group.
  "the durable card look | Cards.BurnLookPolicy.ForCard / ForActivatedCard — from Net/ go through UsedCardLook.FromPolicy; never re-derive the pile-to-look answer | ECardPile\\.(Lost|PermanentlyLost|Discarded|Activated) | Net/Remote/RemotePileFronts.cs Net/Remote/RemoteHeldCardFace.cs Net/Remote/RemoteActiveCards.cs Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteHandFan.cs Net/Remote/RemoteBrowserFan.cs Net/Remote/RemoteCardArt.cs"
  # ── THE ONE-WRITER HOLD FOR A MIRRORED LOOK (2026-09-07 evening round) ────────────────────────
  # CardHalfTone.NormalizeCardFx swaps a clone's card-FX materials to a shared rest copy; the
  # mirrored surfaces re-assert a settled burn at 4 Hz. Left to fight they alternate per rebuild,
  # and CardHalfTone.HoldCardFxLook's own doc had written that failure down IN ADVANCE — "the user
  # sees a flicker instead of an answer" — a full build before the maintainer reported exactly
  # that, in exactly those terms, for a peer's burnt fan.
  #
  # The hold shipped with ONE caller (the active cell) and three surfaces that needed it forgot to
  # take it. So it no longer belongs to the surfaces at all: RemoteCardArt takes it inside
  # SetAbilityCardFxProgress / ClearAbilityCardFx / DestroyClone, the one choke point through which
  # any mirrored look is ever written. A new mirrored surface now gets it for free and CANNOT omit
  # it — which is the difference between a convention and a construction, and this group is what
  # keeps it a construction.
  "the mirrored-look one-writer hold | RemoteCardArt.SetAbilityCardFxProgress — it takes the hold at the write choke point; a surface that takes it by hand is a surface that can forget it | CardHalfTone\\.HoldCardFxLook | Net/Remote/RemotePileFronts.cs Net/Remote/RemoteHeldCardFace.cs Net/Remote/RemoteActiveCards.cs Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteHandFan.cs Net/Remote/RemoteBrowserFan.cs Net/Remote/RemoteBoardCard.cs Net/Remote/RemoteControlBoard.cs"
  # …AND THE POPULATION TERM BEHIND IT. RevealGate.ShowRoundCardFronts is the PHASE predicate — ONE
  # input to the face question — so a card SURFACE that asks it directly is deciding a face from one
  # term of the rule instead of taking the rule. The legitimate remaining callers are all outside
  # this scope on purpose and are listed here so nobody adds them: RemoteControlBoard.cs:723 (the
  # population term it hands INTO CardFaces), RemoteBoardVisibility.cs:136 (a board-VISIBILITY
  # decision, not a face), RemoteHandFan.cs:1445/1771 and RemoteInitiativeTrack.cs:2471.
  # Cards/Driver/** and Board/CharacterFocus.cs are the OWNER's own board and no business of the
  # mirror's. This group is the weaker of the two — a call site is not by itself a second
  # implementation — so it is scoped tightly and says so.
  "mirrored card-face population term | RevealGate.CardFaces — it takes the population; do not ask the phase predicate on a card surface | RevealGate\\.ShowRoundCardFronts *\\( | Net/Remote/RemoteCardFx.cs Net/Remote/RemoteBurnFx.cs Net/Remote/RemoteBoardCard.cs Net/Remote/RemoteBrowserFan.cs Net/Remote/RemotePileFronts.cs Net/Remote/RemoteHeldCardFace.cs Net/Remote/RemoteActiveCards.cs"
)

xfail=0
for entry in "${EXPRESSIONS[@]}"; do
    # The PATTERN field contains '|' alternations of its own, so the fields are peeled off by
    # position — name and call from the FRONT, scope from the BACK, and whatever is left in the
    # middle is the pattern, alternations intact. A plain 4-way split would cut the ERE in half.
    xgroup="${entry%%|*}"; rest="${entry#*|}"
    xcall="${rest%%|*}"; rest="${rest#*|}"
    xscope="${rest##*|}"; xpat="${rest%|*}"
    # trim the padding spaces around each field
    xgroup="${xgroup%"${xgroup##*[![:space:]]}"}"
    xcall="${xcall#"${xcall%%[![:space:]]*}"}"; xcall="${xcall%"${xcall##*[![:space:]]}"}"
    xpat="${xpat#"${xpat%%[![:space:]]*}"}";    xpat="${xpat%"${xpat##*[![:space:]]}"}"
    xscope="${xscope#"${xscope%%[![:space:]]*}"}"
    for site in $xscope; do
        file="$S/$site"
        if [[ ! -f "$file" ]]; then
            echo "error: expression-group scope file ${site} not found — did it move or get renamed?" >&2
            xfail=1; continue
        fi
        # Blank out COMMENTS and STRING LITERALS so the lint reads CODE only: whole-line // and ///
        # comments, block-comment continuation lines, any trailing // tail, and then every "..."
        # span. The string pass is not optional and this repository has a named bug class for
        # skipping it — "a token quoted in its own explanation". Nine log lines in the very files
        # scoped below contain RevealGate.ShowRoundCardFronts( INSIDE A STRING, explaining what the
        # real call means; a lint that read those as call sites would fire on every one of them and
        # be switched off within the week. Stripping can only REMOVE text, so it costs recall and
        # never precision. Substitutions, never deletions, so grep -n still reports the file's own
        # line numbers.
        hits="$(sed -E -e 's://.*$::' -e 's:^[[:space:]]*\*.*$::' -e 's:^[[:space:]]*/\*.*$::' \
                       -e 's:"([^"\\]|\\.)*"::g' "$file" \
                | grep -nE "$xpat" || true)"
        [[ -z "$hits" ]] && continue
        echo "error: a SECOND implementation of a shared expression — ${xgroup}" >&2
        while IFS= read -r hit; do
            echo "    ${site}:${hit}" >&2
        done <<<"$hits"
        echo "  CALL ${xcall}" >&2
        echo "  This expression has ONE implementation and every mirror is meant to call it." >&2
        echo "  WHY THIS GATE EXISTS: four copies of the card flight existed until 2026-09-07 and" >&2
        echo "  three were a DIFFERENT CURVE from the owner's, which no reading on either machine" >&2
        echo "  could see (every arc line printed the PEAK, identical under any symmetric ease)." >&2
        echo "  Each copy carried a comment asserting it matched. A second implementation is not" >&2
        echo "  caught by review and it is not caught by a log; it is caught here or not at all." >&2
        echo "  IF THIS SITE REALLY NEEDS SOMETHING ELSE it is not this expression: give it its" >&2
        echo "  own name, say in one line what population it serves and why the owner's answer" >&2
        echo "  does not apply to it, and take the file out of this group's scope above." >&2
        xfail=1
    done
done

[[ $xfail -ne 0 ]] && fail=1


# ==============================================================================================
# PART 3 — SUBSET GUARDS (2026-09-08, after the review round that found two of them)
# ==============================================================================================
#
# Part 2 forbids a SECOND implementation of an expression this repository owns. This part
# forbids something else: a guard that takes a SUBSET of an expression THE GAME owns.
#
# The 2026-09-07 review found the shape twice, and both had shipped:
#
#   R5 F1. WorldspacePanelUIController.FlowControlActive() is one field read —
#   `m_AttackModBar.IsFlowActive`. The GAME never uses it alone: all five of its own sites pair
#   it with the health bar, as `!FlowControlActive() && !m_HealthBar.IsAnimated` (:683, :698,
#   :711) or `FlowControlActive() || m_HealthBar.IsAnimated` (:724, :732). The mod reads the
#   flow half at four sites and `IsAnimated` at NONE — `grep -rn IsAnimated src/` returns zero.
#   So every one of those guards is open for the whole of a health-bar animation, which is the
#   half of the pair that runs on damage.
#
#   R5 F2. ScenarioRuleClient.IsProcessingOrMessagesQueued, likewise. Five game sites
#   (SkipButton.cs:162, ReadyButton.cs:501, UndoButton.cs:294, ActionProcessor.cs:365 in its De
#   Morgan form, SceneController.cs:1479 in its ThreadIsSleeping form) all write
#       (!IsProcessingOrMessagesQueued
#        || GameState.WaitingForPlayerToSelectDamageResponse
#        || GameState.WaitingForPlayerActorToAvoidDamageResponse)
#   The mod's FigureBusy.cs:334-337 takes all three and is the worked example. CardsGameApi.cs
#   :1874 takes the first alone, and therefore reads BUSY for the entire time the rules engine
#   is waiting for a human to answer a damage prompt — which is exactly when a hand-off matters.
#
# WHY IT IS A DIFFERENT CHECK. There is nothing to compare two copies of: there is ONE mod copy
# and it is a subset of the game's expression. The only machine-checkable statement is "these
# terms travel together" — a companion test, per file, over a named scope. That is weaker than
# Part 2 and it is scoped and documented as such.
#
# A group is one line, FIVE | -separated fields:
#   name | THE GAME'S FULL EXPRESSION (what to write) | trigger ERE | companion ERE | files
#
# The fields are peeled from the ENDS — name from the front, then scope, companion and trigger
# from the back — so only THE GAME'S FULL EXPRESSION may contain a `|` of its own, and it needs
# to: the game writes these as `||` chains and quoting the real form is the whole message. The
# trigger and the companion are single identifiers by construction, which is the shape this part
# checks; a `|` in either would be silently mis-peeled, so do not write one.
#
# A file entry ending in `/` means every .cs under that directory — it may NOT be marked, since
# a review convicts a call site and not a folder. A single-file entry prefixed `~` is KNOWN-OPEN:
# it violates today, a review has convicted it, and a lane is repairing it. A `~` file that STOPS
# violating FAILS — deleting the tilde is the last step of the fix, so the marker cannot outlive
# the defect. A `~` file listed inside a directory that is also in scope stays marked (the mark
# wins the merge). Everything else in scope must carry both terms or neither.
SUBSETS=(
  # KNOWN-OPEN at the moment this part was written (base f37049b5, ModBuild 479): all three
  # marked files take the flow half alone. `IsAnimated` appears nowhere in src/, so the
  # companion is currently absent by construction rather than by oversight. The rest of
  # Board/FigureGrab/ is in scope unmarked, so the next file to reach for the term is caught.
  "the game's flow-control pair | !FlowControlActive() && !m_HealthBar.IsAnimated (the game's own form at WorldspacePanelUIController.cs:683/698/711; :724/:732 write the OR form). The bar is not free to be moved while EITHER is running | FlowControlActive *\\( | IsAnimated | ~WorldUI/ActorBars.cs ~Board/FigureGrab/FigureBusy.cs ~Board/FigureGrab/FigureStallWatchdog.cs Board/FigureGrab/ "
  # KNOWN-OPEN: Cards/CardsGameApi.cs:1874. Board/FigureGrab/ is in scope UNMARKED because
  # FigureBusy.cs:334-337 already takes all three terms and is the worked example — the group
  # passes there, which is the point of scoping it in.
  "the rules-engine busy triple | (!ScenarioRuleClient.IsProcessingOrMessagesQueued || GameState.WaitingForPlayerToSelectDamageResponse || GameState.WaitingForPlayerActorToAvoidDamageResponse) — the game's own form at SkipButton.cs:162, ReadyButton.cs:501, UndoButton.cs:294 | IsProcessingOrMessagesQueued | WaitingForPlayerActorToAvoidDamageResponse | ~Cards/CardsGameApi.cs Board/FigureGrab/ "
)

sfail=0
for entry in "${SUBSETS[@]}"; do
    sgroup="${entry%%|*}"; rest="${entry#*|}"
    sscope="${rest##*|}"; rest="${rest%|*}"
    scomp="${rest##*|}";  rest="${rest%|*}"
    strig="${rest##*|}";  sfull="${rest%|*}"
    for v in sgroup sfull strig scomp sscope; do
        printf -v "$v" '%s' "$(echo "${!v}" | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//')"
    done

    # site -> 0/1. A directory contributes its files unmarked; an explicit `~file` overrides,
    # whichever order they are listed in, so a marked file cannot be silently un-marked by a
    # directory entry added later.
    declare -A scope_mark=()
    for site in $sscope; do
        marked=0
        [[ "$site" == "~"* ]] && { marked=1; site="${site#\~}"; }
        if [[ "$site" == */ ]]; then
            if [[ $marked -eq 1 ]]; then
                echo "error: subset-guard '${sgroup}' marks a DIRECTORY (~${site}) KNOWN-OPEN." >&2
                echo "       A review convicts a call site, not a folder. List the files." >&2
                sfail=1; continue
            fi
            while IFS= read -r f; do
                rel="${f#$S/}"
                [[ -n "${scope_mark[$rel]:-}" ]] || scope_mark["$rel"]=0
            done < <(find "$S/$site" -name '*.cs' 2>/dev/null | sort)
        else
            [[ $marked -eq 1 ]] && scope_mark["$site"]=1 || scope_mark["$site"]="${scope_mark[$site]:-0}"
        fi
    done
    if [[ ${#scope_mark[@]} -eq 0 ]]; then
        echo "error: subset-guard scope for '${sgroup}' matched no files — did a directory move?" >&2
        sfail=1; continue
    fi

    for site in $(printf '%s\n' "${!scope_mark[@]}" | sort); do
        marked="${scope_mark[$site]}"
        file="$S/$site"
        [[ -f "$file" ]] || { echo "error: subset-guard scope file ${site} not found" >&2; sfail=1; continue; }
        # Same strip as Part 2, and for the same reason: FigureBusy.cs quotes BOTH of these
        # expressions in its own class doc, and a lint that read prose would call the file
        # compliant on the strength of a comment. Substitutions only, so line numbers survive.
        code="$(sed -E -e 's://.*$::' -e 's:^[[:space:]]*\*.*$::' -e 's:^[[:space:]]*/\*.*$::' \
                       -e 's:"([^"\\]|\\.)*"::g' "$file")"
        # HERESTRINGS, NOT PIPES, AND THIS IS NOT A STYLE CHOICE. This file runs under
        # `set -o pipefail`, and `printf … | grep -q` makes grep exit at the FIRST match, which
        # SIGPIPEs printf (141) and makes the whole pipeline report failure — so the companion
        # test read FALSE on every file that actually had the companion. It was caught by this
        # group's own negative-control plant (both terms present, must pass) failing; a gate
        # whose negative control is not run ships lying in exactly this way.
        hits="$(grep -nE "$strig" <<<"$code" || true)"
        has_companion=0
        grep -qE "$scomp" <<<"$code" && has_companion=1

        if [[ -z "$hits" ]]; then
            if [[ $marked -eq 1 ]]; then
                echo "error: subset-guard '${sgroup}' — ${site} is marked KNOWN-OPEN with '~' but no" >&2
                echo "       longer reads the term at all. Delete the '~' entry from the group above:" >&2
                echo "       a marker that outlives its defect is how this file stops meaning anything." >&2
                sfail=1
            fi
            continue
        fi
        if [[ $has_companion -eq 1 ]]; then
            if [[ $marked -eq 1 ]]; then
                echo "error: subset-guard '${sgroup}' — ${site} is marked KNOWN-OPEN with '~' and now" >&2
                echo "       carries the companion term. The fix landed; delete the '~' from the group" >&2
                echo "       above so the file is held to the rule from here on." >&2
                sfail=1
            fi
            continue
        fi
        [[ $marked -eq 1 ]] && continue    # convicted, in repair, expected

        echo "error: a guard took ONE term of an expression THE GAME writes with more — ${sgroup}" >&2
        while IFS= read -r hit; do
            echo "    ${site}:${hit}" >&2
        done <<<"$hits"
        echo "  THE GAME WRITES: ${sfull}" >&2
        echo "  This file reads the first term and never mentions '${scomp}'. Every state the" >&2
        echo "  missing term covers is a state this guard is open in — and it is open exactly" >&2
        echo "  when the missing term is the one that is running, which is the case nobody tests." >&2
        echo "  WHY THIS GATE EXISTS: two guards shipped this way and the 2026-09-07 review found" >&2
        echo "  both. Neither is visible in a build, in a golden vector or in a log: the mod never" >&2
        echo "  reads the term it is missing, so no instrument can print it." >&2
        echo "  IF THE SUBSET IS DELIBERATE, say in one line at the call site which states the" >&2
        echo "  missing term covers and why they do not matter here, and take the file out of the" >&2
        echo "  group's scope above." >&2
        sfail=1
    done
done

[[ $sfail -ne 0 ]] && fail=1
[[ $fail -eq 0 ]] && echo "mirrors: ${#MIRRORS[@]} mirrored-constant groups agree; ${#EXPRESSIONS[@]} shared-expression groups have one implementation each; ${#SUBSETS[@]} subset-guard groups keep the game's terms together"
exit $fail
