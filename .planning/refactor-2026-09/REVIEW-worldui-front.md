# Review — lane worldui-front (refactor 2026-09)

> Phase 1 deliverable per `BRIEF.md` §3.1. Written against base `b40f8564` (the brief commit,
> descendant of `60beaa1f` / ModBuild 480). **Committed before any `src/` change.** Incremental:
> each commit of this file adds a sub-area; the reading log in §6 says what has been read and how.
>
> File set: `WorldUI/{Modal,MapRoom,Surfaces,Composites,Tooltips,Buttons,FlatScreen}/` and the 14
> `WorldUI/*.cs` root files — 116 files, ≈117 k lines. Instruments run at the base
> (`census-2026-08/hygiene2.py`, `dupes2.py 12`, `loadbearing.py` over `WorldUI/`, filtered to this
> set), plus a null `refactor-guard.sh check --summary` on the untouched tree: **all 16 checkers
> green, 0 moved / 0 changed, surfaces 412 keys / 150 patches / 4 681 tokens unchanged**.
>
> Format per finding: **ID · file:line · class · tier · evidence · action · guard expectation ·
> risk if wrong.** Ranked by risk reduced. A finding I will NOT act on is still listed, with the
> reason (`PLAN-2026-08.md` §0.2).

---

## 0. Headline

1. **One config description lies since ModBuild 480 and one half-lies** (F1). The shared-window
   pose law (`b27bbb9e`, R2 F8) moved the blue-window ring radius and the scenario bar height onto
   shipped constants. `ScenarioWindowBoardClearanceMeters` got the "BOUND BUT INERT" text;
   `SharedWindowArcRadiusMeters` did not — its description still tells every player
   *"MULTIPLAYER: this value is part of the shared placement, so all players in a session should
   leave it at the same number"*, and it has **no consumer at all** (`Defaults.WorldUI.cs:215`
   says so; `ArcSeats.cs:6103-6111` reads the constant and only *prints* the dial).
   `MapRoomWindowBarHeightMeters` still claims the same multiplayer coupling while shared windows
   ignore it. A knob that lies is the class the brief's §5.4 precedent exists for.
2. **The map room's first arc allocator is still in the tree, uncalled, ~350 lines** (D1).
   `ModalFallback.4.Tick.cs` still carries `TryClaimArcSlot` (162 code lines), `NarrowArcClaim`,
   `ArcClaimAngleDeg`, `FloatedArcSlotCapacity/Occupied` and `TryGetFloatedEnsembleBounds` — every
   caller went to `ArcSeats.TryClaimArcSeat` in `cf35c0de` (ModBuild 234) and `1f5f50ef`
   (ModBuild 192) and the bodies were left. The 2026-08 census (`PLAN-2026-08.md` §0.2, "100 names
   mentioned exactly once") missed them because they are mentioned in *comments* 2-3 times each;
   a census that strips comments finds 28 candidates in this set, of which 11 are real (§3).
3. **One per-frame scene sweep survives on the flat 2D map path** (T1): `DetectActiveMap` runs
   `FindObjectOfType<MapChoreographer>()` from `EnsureAlbedoReady`, which `EndStackSync` calls
   every frame while the flat map albedo path is engaged — the `_fastMapChoreo` cache one screen
   above it exists for exactly this and is not consulted. Off by default (`[Rig] Vanilla2DMap` =
   false) but a defect of the class this project has paid for three times
   (`findobjectsoftype-is-the-default-suspect`).
4. **The text-duplication census is right about this set, and the named families resolve into
   three genuinely mergeable mechanisms** (§2): the hover-panel anti-churn watch
   (`PropInfoSurface`↔`StatPanelSurface`, 6 members byte-identical after normalisation), the
   moved-subtree layer record (`LoadoutConfirmPark`↔`StoryComposite`↔`HintOnOwnerComposite`, three
   verbatim copies), and the floated-window-by-ID lookup (`CharacterWindow()`, two verbatim copies).
   The 2026-07 verdict on the first pair ("per-surface tunables, do not merge") is re-judged under
   BRIEF §1.2: the *mechanism* merges, the three constants stay per-surface as constructor
   arguments — which is exactly the shape that verdict said it wanted and did not have.
5. **The painted-union measurement exists four times and must not be merged** (F6): each copy
   carries a hard-won filter the others do not. Documented, cross-referenced, left.
6. `STALE-DOC-REFS.md` names **eight** lines in this set (the brief says four): all eight have a
   resolution (§4) and every one is a comment edit with an empty guard diff.
7. **Standing rulings hold.** Yaw-only: every window-facing writer in the set routes through
   `HeadFacing.YawOnly`, `ModalFallback.Upright` (the last step of `ComputeHmdPose`, with a one-shot
   Warn if anything upstream tilts), `MapRoomSeat.Rotation` or `Quaternion.Euler(0, yaw, 0)`.
   Shared size/pose: `SharedWindowSizeLaw` and `ArcSeats.TrySharedAnchor*` read only shipped
   constants and the shared frame; no `Config.*.Value` reaches a shared size or pose (grep over
   `Modal/Shared*`, `ArcSeats`, all of `Composites/`, the dock surfaces and the shared map-room
   files). Options key: `CloseStickyFloatsExceptEscMenu` is scoped to `IsEscMenuSubWindow` since
   2026-09-03 and `MenuExclusivity` arbitrates by place. Nothing in this set fades a floor tile.

---

## 1. Defects, risk-gaps, doc-drift that misleads the player or the next author

### F1 · `WorldUIConfig.cs:814-832` (`SharedWindowArcRadiusMeters` Bind description), `:330-345` (field doc), `:833-851` (`MapRoomWindowBarHeightMeters`) · **doc-drift (user-facing config text)** · Tier 0

**Evidence.** Since `b27bbb9e` (ModBuild 480):
- `ArcSeats.cs:6103-6111`: *"THE RADIUS IS THE SHIPPED CONSTANT AND NOT THIS CLIENT'S DIAL … `float
  radius = Mathf.Max(Defaults.SharedWindowArcRadiusMeters, 0.05f)`"*; the dial is read only into
  `radiusDial` for the spawn line (`:6232-6233`). `Defaults.WorldUI.cs:215-217`: *"[WorldUI]
  SharedWindowArcRadiusMeters is still bound and still shown, but it now has no consumer at all"*.
  Repo grep confirms: no reader outside `ArcSeats` (log only), `WorldUIConfig` and the options
  step table.
- The Bind description still ends *"MULTIPLAYER: this value is part of the shared placement, so all
  players in a session should leave it at the same number — a different value on one client seats
  that client's copy somewhere else until somebody drags it."* — the exact advice R2 F8 retired.
- The field's own XML doc (`:330-345`) says *"Read ONCE per shared window, at its spawn placement,
  by `ModalFallback.TrySharedAnchorOnTable` … two clients with DIFFERENT values do not"* — false
  on both counts (it is not read for the pose; the method lives in `ArcSeats`).
- `MapRoomWindowBarHeightMeters`: `ArcSeats.ResolveMapRoomBarHeightMeters(…, sharedWindow)` takes
  `Defaults.MapRoomWindowBarHeightMeters` for a shared window and the dial only for LOCAL windows
  (`:4850-4870`). Its description still says *"MULTIPLAYER: this value is part of the shared
  placement … a different value on one client hangs that client's copy of a shared window at a
  different height"*. Half true: the dial still moves every local map-room window.
- The precedent text is already in the same file: `ScenarioWindowBoardClearanceMeters` (`:866-874`)
  *"as of ModBuild 480 this dial is BOUND BUT INERT …"*.

**Action.** Reword the two English descriptions (radius → BOUND BUT INERT, same paragraph shape
as the precedent; bar height → "shared windows use the shipped 0.60 m; this dial moves YOUR local
map-room windows only") and the field doc. Keys, defaults, ranges untouched.
**Guard.** `CHANGED` confined to `WorldUIConfig` (Bind descriptions are string literals and DO
appear in the snapshot — CHARTER §3); `check-surface` 412 → 412.
**NEEDED-OUTSIDE (core).** `Core/Loc/Loc.ConfigDescriptions.German.cs:2547` carries the German
`MapRoomWindowBarHeightMeters` text with the same false multiplayer sentence; there is no German
entry for `SharedWindowArcRadiusMeters` (falls back to the English bind text, so the English fix
covers it). Exact diff in `NEEDED-OUTSIDE-worldui-front.md`.
**Risk if wrong.** None to behaviour.

### T1 · `FlatScreen/FlatScreenStereo.3.Map.cs:984-1023` (`DetectActiveMap`) · **defect (per-frame scene sweep)** · Tier 3

**Evidence.** `DetectActiveMap` opens with `Object.FindObjectOfType<MapChoreographer>()` and has
no cache. Callers: `EnsureAlbedoReady` (`:943`), which is the last term of
`:541 if (!_mapBaseCapture || MapRoomOwnsParchment || mapSource == null || !EnsureAlbedoReady())`
inside the per-frame stack sync, and `FindWorldMapRenderer` (`:1028`). Input/state → wrong
output: `[Rig] Vanilla2DMap = true` (the flat map path), stereo engaged, `_mapBaseCapture` true →
one full-scene `FindObjectOfType` **every frame** for as long as the campaign map is open. The
sibling `TickFastMapEngage` (`:203-211`) already caches `_fastMapChoreo` and re-finds only every
`FastMapFindIntervalFrames` (10) frames; `ReleaseAlbedo`'s scene exit nulls it (`:2334`).
**Action.** `DetectActiveMap` reads `_fastMapChoreo` when it is alive and falls back to the same
`FindObjectOfType` (writing the cache) when it is not — the cold path is byte-for-byte today's,
the warm path is one field read. No change to what is detected or logged (`MAP SELECT (ISSUE 3)`).
**Guard.** `CHANGED` confined to `FlatScreenStereo`. **Hardware line:** open the flat 2D map with
`[Rig] Vanilla2DMap = true`, switch world↔city: `MAP SELECT (ISSUE 3): active map = CITY/WORLD`
still prints on the switch.
**Risk if wrong.** A destroyed-but-not-null choreographer would be caught by the Unity null test
(same test `TickFastMapEngage` relies on). Low.

### F8 · `Modal/ModalFallback.1.Core.cs:10-13` · **doc-drift** · Tier 0

*"The nine parts concatenate back into the original member order"* — `ModalFallback` has 13
numbered parts plus `SharedQuestCornerSeat.cs` (self-described "part 14"), `AssignmentWindows.cs`
and `MandatoryDecision.cs` (16 files declare `partial class ModalFallback`). The compile-order
argument still holds for the numbered ones; the sentence is simply out of date. Comment-only.

### F9 · `Modal/ModalFallback.7.Close.cs:1401-1417` · **doc-drift + dead** · Tier 0

`IsMultiplayerReadyToggle` / `IsQuestCardWindow` are documented *"Read by `MapTravelConfirm`,
which parks it into the quest window"* — no caller exists anywhere (`occ` over `src/`, `tests/`,
`docs/`, `.planning/`); `MapTravelConfirm` carries its own IS-A tests since `c43fa571` (ModBuild
226, the same commit that added these two). Removed with D1's commit.

---

## 2. Duplication and parallel construction

The census groups for this set, each diffed after normalisation (comments stripped, whitespace
collapsed); the verdict is per group.

### F2 · `Surfaces/PropInfoSurface.cs:78-128, 165-312, 630-665` ↔ `Surfaces/StatPanelSurface.cs:100-147, 471-560, 915-940, 1063-1095` · **duplication** · Tier 2

**Evidence (normalised diff).** Byte-identical: the three constants (`ReleaseDelaySeconds` 0.3,
`ChurnWindowSeconds` 2, `ChurnWarnCount` 5), `MipRescanInterval` 0.5, the nested `Watch` class
(StatPanel's lacks only the `IsTextInfo` flag), `ScheduleRelease`, `CountConversion` (including
the warning string), `RescanMips`, `Release`, `DetachWatch`, and the attach/listen + hysteresis
release + raycaster-dark halves of `TickWatch`. **Not identical, must stay per surface:** the
`Convert(...)` call (`sortingOrder: StatPanelSortingOrder` — a named invariant) and the
`Choreographer.s_Choreographer != null` gate in StatPanel's `TickWatch`; PropInfo's `IsTextInfo`.
Both files already carry the 2026-07 cross-reference block (`PropInfoSurface.cs:60-75`,
`StatPanelSurface.cs:86-98`) saying the pair is *"equal today by history, not by contract"*.

**Re-judgement under BRIEF §1.2.** The 2026-07 objection was that a shared core *"would have to
take all three as arguments, which is two call sites with the same three numbers rather than one
implementation"*. That is precisely the design that keeps the constants per-surface tunables
(INVARIANTS: StatPanel's three breakers are independent by design) while the mechanism — the
thing that can drift — exists once. The user's standing preference (`redundancy-audit.md` §0,
*"alles nochmal neu … schreiben obwohl doch das meiste schon … gebaut wurde"*) points the same way.

**Action.** New `Surfaces/HoverWindowWatch.cs`: the `Watch` state (with `IsTextInfo`), constructed
with `(releaseDelay, churnWindow, churnWarn, mipRescanInterval)`; instance methods
`ScheduleRelease`, `CountConversion(name)`, `RescanMips(name)`, `Release()`, `Detach()`, and an
`Attach(Component? live)` for the listener half of `TickWatch`. Each surface keeps its own
constants, its own `TickWatch` body around the `Convert` call, and its own doc blocks (the
cross-reference paragraphs are rewritten to point at the shared type instead of at each other).
**Guard.** `CHANGED` in `PropInfoSurface`, `StatPanelSurface`; `ADDED` `HoverWindowWatch`; the
nested `PropInfoSurface.Watch` / `StatPanelSurface.Watch` disappear. Nothing else. The warning
string is a log token (`convert/release churn`) and moves verbatim.
**Risk if wrong.** Low and detectable: the constants stay where they are and keep their values;
the only new coupling is that both surfaces call one body that was already textually identical.

### F3 · `Composites/LoadoutConfirmPark.cs:349-354, 1055-1095, 2834-2837` ↔ `Composites/StoryComposite.cs:793-804, 4304-4384, 4608-4610, 4844-4847` ↔ `Composites/HintOnOwnerComposite.cs:238-241, 746-786, 951` · **parallel construction (3 copies)** · Tier 2

**Evidence.** `LayerTx`/`LayerWas`/`_layerWritten`/`_layerSkipped` + `WriteLayerWalk` +
`RestoreLayers` are byte-identical across the three files (verified for all three;
LoadoutConfirmPark's doc even says *"`StoryComposite.LayerTx`/`LayerWas`, for its reason …"*).
The only per-class difference is the `WriteLayers` entry (Story walks its `Moved` list and the dock
root; the other two walk one root) and the `LayersDrifted` sentinel choice. Each class also prints
`{LayerTx.Count} transform(s), {_layerSkipped} foreign render subtree(s)` in its own report line.
**Action.** `Composites/MovedSubtreeLayers.cs`: one instance per composite with `Begin(layer)`,
`Walk(root)`, `Restore()`, `Written`, `Count`, `Skipped`, `Clear()`. Each composite's
`WriteLayers` keeps its own shape and calls into it. Three static records stay three instances —
the composites can be parked at once.
**Guard.** `CHANGED` in the three composites, `ADDED` `MovedSubtreeLayers`. The report strings
keep their exact text (the counts are read through the record).
**Risk if wrong.** Low: the walk and the guarded restore are the two halves that were identical;
the sentinels that differ stay in the owners.

### F4 · `Composites/LoadoutConfirmPark.cs:866-892` ↔ `Composites/StoryComposite.cs:1947-1969, 2474` · **duplication** · Tier 2

**Evidence.** `CharacterWindow()` — "the floated window whose `ID == UIWindowID.PartyPanel`", with
its `FloatScratch` list, try/catch/finally — diff = 0 after normalisation (22/22 lines). Same idea
with other predicates: `StoryComposite.cs:2006-2019`, `QuestJourneyCurtain.cs:751-760, 825-841`.
**Action.** `ModalFallback.FindFloatedWindow(UIWindowID id)` (Modal/ is this lane's) beside
`CollectFloatedWindows`, allocation-free on a ModalFallback-owned scratch; the two copies become
one-line calls. The predicate-differing loops stay (they are not the same question).
**Guard.** `CHANGED` `ModalFallback` (one added method), `LoadoutConfirmPark`, `StoryComposite`.
**Risk if wrong.** Nil; the catch-and-null semantics move verbatim.

### F5 · `Surfaces/DamageTooltipSurface.cs:316-355` ↔ `Surfaces/DecisionDockSurface.cs:2770-2812` · **duplication** · Tier 2 (optional, last)

**Evidence.** `ApplyFocusHide`/`RestoreFocusHide` bookkeeping around the already-shared
`CanvasConversion.ApplyOwnerRenderHide`/`LiftOwnerRenderHide`: lists, two counts, the
`MrBacking.PlateObjectName` diagnostic, the `first` reset. 22/26 identical; DecisionDock
additionally nulls `RowBottomUpMeters` (a hard-won line — INVARIANTS "RowTopUpMeters survives the
focus hide") and names its flag `_rowHiddenForFocus`.
**Action.** If time permits: `Surfaces/OwnerRenderHideRecord` holding the lists/counts/plate flag
with `Apply(panel)` / `Lift(panel)`; the `RowBottomUpMeters = null` line stays at the DecisionDock
call site. **Deferred behind F2-F4** — 26 lines, and the differing line sits inside the block.

### F6 · `Composites/LoadoutConfirmPark.cs:1737-1809` (`TryFreeLane` sweep) / `:2040-2118` (`TryPaintedBounds`) / `StoryComposite.cs:4520-4590` / `EnchantressComposite.cs:730-830` · **parallel construction — DO NOT MERGE** · documented

**Evidence.** Four "painted union of a subtree in window-local space" sweeps. Diffs: Loadout's
lane sweep tests `ReferenceEquals(rt, exclude)` with a non-null `exclude` and keeps per-graphic
min/max; its bounds sweep tolerates a null `exclude`; Story's has the `IsTheReadyRow` exclusion
(hardware evidence in its doc) and `CountsAsPaintedHere`; Enchantress adds the PLATE test
(`PlateWidthFraction`/`PlateHeightFraction`) and the `_leftmostInk` attribution. Each difference
is a recorded fix. Enchantress already cross-references Story. **Left as is**; F3/F4 remove the
parts that are genuinely identical.

### F7 · `Modal/ArcSeats.cs:2600-2627` ↔ `:3058-3081` · **duplication (prologue)** · not acted on

The spawn path (`TryClaimArcSeat`) and the re-place path (`TryReseatArcClaimOnDrawnContent`) share
the 15-line corner/channel/free-interval prologue; the search itself is *"since ModBuild 241
literally the same method"*. The prologue differs by `outwardWouldFit` (spawn-only report) and the
`-1` vs `slot` argument. **Not merged**: it would be a helper with nine `out` parameters inside the
389-line method the brief names as a Tier-1 target; see §5 for the split verdict.

---

## 3. Dead code — the comment-stripping census

Method: every `private`/`internal` member declared in this set, counted by identifier over the
comment-stripped text of `src/` + `tests/` (the 2026-08 census counted raw text, which is why a
member mentioned twice in its own doc comment read as live). 28 candidates; each then checked
against BRIEF §5 with `occ.py` (every occurrence in `src/`, `tests/`, `docs/`, `.planning/`) and
`log -S`.

| # | member(s) | verdict | §5 evidence |
|---|---|---|---|
| D1 | `ModalFallback.4.Tick.cs`: `TryClaimArcSlot` (:1645, 162 code lines), `NarrowArcClaim` (:1880), `ArcClaimAngleDeg` (:1234), `FloatedArcSlotCapacity` / `FloatedArcSlotsOccupied` (:1991-2001), `_ensembleCorners` + `TryGetFloatedEnsembleBounds` (:2004-2050) | **dead, Tier 0** | no caller in `src/`/`tests/`; mentioned only in their own docs and one comment in `GuildmasterDestinations.cs:640`. Not Harmony, not Unity messages, not reflection/nameof, no config, no log token (their log strings are inside the dead bodies — `check-surface` will report the tokens `THE REGISTRY IS FULL` etc. as REMOVED only if no live copy exists; `ArcSeats` carries the live spawn line, verified before the commit), not debug-menu, not a wire vector, not an instrument write. `log -S'TryClaimArcSlot('`: callers removed in `cf35c0de` (ModBuild 234, `ArcSeats.TryClaimArcSeat` took over) and `1f5f50ef` (ModBuild 192). Helpers they alone used (`ArcAngleIsFree`, `SpreadAngleDeg`, `ArcWorstOverlapDeg`, `PermanentOverlapDeg/Text`, `DistanceThatWouldFitMeters`, `FirstFreeClaimIndex`, `OverlapPullWorld`, `FallbackHalfWidthWorld`, `IsPermanentPanel`…) are re-censused after the removal and go in the same commit if orphaned; `_arcCandidates`, `_arcClaims`, `CountArcClaims`, `HalfAngleDeg`, `IsHoverCardPanel`, `LogArcGeometryOnce` stay (live in `ArcSeats`). |
| D2 | `ModalFallback.7.Close.cs`: `IsMultiplayerReadyToggle` (:1408), `IsQuestCardWindow` (:1416) | **dead, Tier 0** (F9) | no caller; added `c43fa571` with a doc naming a reader that never called them. |
| D3 | log-material accessors with no reader: `MapButtonRail.CapCount` (:497), `KeycapPress.CooldownLeft` (:158), `SurfaceGrabBar.IsBuilt` (:184), `MapRoomDriver.IsCityMap` (:179), `MapParchment.MapGo` (:64), `MapLocationInteractor.RegisteredCount` (:120), `MapTableLegs.TriangleCount` (:606) | **dead, Tier 0** (one commit) | each a one-line `internal` getter/const, no reader anywhere; none is a log token, config, patch, message, reflection target or test pin. |
| D4 | `ModalFallback.7.Close.cs:1766 ReleaseFloatsExcept` | **keep** | no caller BY DESIGN — its 30-line doc records why it stays ("wholesale teardown … room teardown, scene change") and forbids the two uses it was written for. Deleting it would delete the ruling. |
| D5 | `PropInfoSurface.ShowHeldProp` / `ClearHeldProp` (:397/:410) | **keep** | an API waiting on lane Board (`.planning/LANE-PROPINFO-357-NEEDED-OUTSIDE.md`); its doc states the dock works without it. Re-raised in NEEDED-OUTSIDE. |
| D6 | `TrayControlDockSurface.ShortRestDocked` (:119) | **keep** | INVARIANTS "the four `false` constants … removing them silently hides board buttons"; `RestControls.cs:16/551` and `docs/img/README.md:679` cite it by name. |
| D7 | Harmony patch bodies: `AfterButtonPrompt`, `AfterPopupPrompt`, `AfterReadyUp`, `EncounterPrefix`, `InstantMovePrefix`, `TeleportPrefix`, `TimedMovePrefix`, `UntimedMovePrefix`, `TrackCharacter_Prefix` | **live** | reached by name from Harmony (`docs/PATCH-INVENTORY.md` rows 152-160). |

False positives corrected on the way: the raw-text census of `FlatScreenStereo.3.Map.cs` reports
`BuildOverrideMaterials`, `EnsureAlbedoCamera`, `_uvDebugTex`, `AppendReportPeers`,
`WindScan*`/`_wind*` as dead — all live. Cause: a `//` comment at `:863` contains `Bundle/**.shader`
and one at `:1555` contains `*Wind*/*Cloud*`, so a stripper that removes `/* … */` before `//`
swallows lines 863-1470 and 1555-1590. Recorded so the next census does not repeat it.

---

## 4. `STALE-DOC-REFS.md` — the eight lines in this set (the brief says four)

All comment-only, guard **empty** by construction; each restores a `<see cref>` the compiler
will verify (CS1574 is an error), or rewrites the sentence.

| file (current line) | demoted symbol | what took the job | fix |
|---|---|---|---|
| `Buttons/SoftCueArt.cs:502` | `MinAlpha`, `MaxAlpha` | the `minAlpha`/`maxAlpha` arguments of `SoftFramePulse.Init(Graphic, Color, float, float, float, float)` (`:543-549`, stored in `_minAlpha`/`_maxAlpha`) | "between the `minAlpha` and `maxAlpha` handed to `<see cref="Init"/>`" |
| `MrBacking.cs:268` | `Label(TMP_Text?, bool)` | `MrBacking.Label(TMP_Text? label, bool fades = false)` exists (`:351`) and the class doc at `:110` already crefs it successfully | restore the cref |
| `WorldUIConfig.cs:343` | `Panels` | `ConfigCatalog.ConfigTopic.Panels` (`Options/ConfigCatalog.cs:85-90`, same namespace) | `<see cref="ConfigCatalog.ConfigTopic.Panels"/>` |
| `MapRoom/MapIconLayer.cs:1057` | `_bakeAsked` | renamed to the static `BakeAsked` set (`:479`) | cref `BakeAsked` |
| `MapRoom/MapTravelConfirm.cs:2606` | `AppendRecommendation` | replaced at ModBuild 197 by `ZeroCheck` (`:2823-2856`): the suggestion became a residual self-check | rewrite the list item: "see `ZeroCheck` — the two numbers he used to type in ARE the zero since 197, so the same arithmetic prints as a residual that must read (0.000, 0.000)" |
| `Surfaces/TablePanelSurfaces.cs:585`, `:598` | `Place` | `InitiativeTrackSurface` does not override `Place`; the pose is derived in `TrayMountedPanelSurface.Place` (`:205`) | `<see cref="TrayMountedPanelSurface.Place"/>` ×2 |

Then delete the eight lines from `STALE-DOC-REFS.md` (this lane's entries only).

---

## 5. The Tier-1 targets the brief names — verdicts

| target | verdict | why |
|---|---|---|
| `ModalFallback.4.Tick.cs:2682 Tick` (417) | **no split** | phase order is locked in `FRAME-ORDER.lock`; the 21 phases are already extracted methods and the body IS the ordering. |
| `ArcSeats.cs` (6 587 lines, `TryClaimArcSeat` 389) | **no split this round** | the file is one static partial of `ModalFallback` already (all state `static`, `check-partial-order` green); a further cut buys navigation only, and the method's 389 lines are one search with its report — F7 explains why the only extractable prologue is not worth nine `out` parameters. |
| `ActorBars.MeasureAnchorOffsetWU` (388) | **no split** | ~half of it is the attributable report string the boss-dragon round asked for (`:1440-1473` doc); extracting the text into a builder moves nothing a reader needs and puts the measurement and its line in two places. |
| `WorldUIConfig.Bind` (464) | **no split** | descriptions are literals in the snapshot; a split of `Bind` into per-topic helpers is `CHANGED` in `WorldUIConfig` with 50 string literals moving — nothing to gain against a config seam where a mistake costs a player his value. |
| `EyeReachCensus.LogEyeReachCensus` (403) | **leave** | grep of `:395-870` finds no field write — a pure diagnostic (its own doc: *"writes nothing but its own scratch buffers"*), triggered by an options tap, not per frame. Not load-bearing; not retired (a working instrument is kept — BRIEF §4). |
| `StoryComposite` (4 869) | **no split this round** | static class, 15 `catch (Exception)` sites, six `Report*` methods whose verdict fields the mechanism reads (`loadbearing.py`): a partial split is safe in principle (every field is `static`, initialisers are literals) but the reading a safe cut needs is the reading F3/F4 do first. Shrunk by F3/F4 instead. |
| `MapTableLegs` (2 952) | **no split this round** | `Build` (179) and `Report` (240) are the two big members; `Report` is the round-11 census the user reads. Not read deeply enough to cut safely — deferred again, with the same reason as 2026-08. |

---

## 6. Instruments, sweeps and gates — verified negatives

- **Load-bearing instrument writes** (`loadbearing.py`): the census lists `Report*` methods in
  `StoryComposite`, `LoadoutConfirmPark`, `QuestJourneyCurtain`, `EnchantressComposite`,
  `MapQuestReadyRoster`, `MapTravelConfirm`, `MapIconLayer`, `MapIconHoverAnimation`,
  `MapRoomHand`, `TablePanelSurfaces`, `FreeLabelOrder` whose fields are read outside. None is
  retired or gated by this lane; the accepted baseline (`INSTRUMENT-WRITES.baseline`: `ActorBars::LogSize`,
  `MapRoomHand::Probe`, `ModalFallback::LogMapRoomBarHeight`) is untouched and
  `check-instrument-writes` stays at 65.
- **Scene sweeps** (`FindObjectsOfType`/`FindObjectOfType`, 43 sites): every site except T1 is
  one-shot (`_sweepTried`, `_baselineDone`, `s_mapSceneReported`), cadence-gated
  (`_videoSweepFrame`/30, `FastMapFindIntervalFrames`/10, `_hudSweepDue`/budget,
  `_canvasSearchNextFrame`, `_bootSearchCooldown`, `MapRoomDriver.FindIntervalFrames`,
  `_windNextScanFrame`/15 with 20 attempts), a fallback behind a null cache
  (`_bannerFallback`, `_iconChoreo` under `MapIconCacheOn`, `slots == null`), inside a diagnostic
  that prints once (`LogRenderOrder`, `LogMapAcquisitionCandidates` after 240 fail ticks,
  `EyeReachCensus`), or build-time (`MapTableLegs`, `NativeButtonSkin.Sample`).
- **Empty `catch`**: none in the set. 143 `catch (Exception)` sites, all with a body.
- **Sentinel arithmetic**: the three `int.MinValue` sentinels are either compared for identity
  (`_fastMapFindFrame != int.MinValue &&`, `SharedWindowIdentity._lastApplyFrame`) or halved
  (`int.MinValue / 2`) before subtraction. No `now - int.MinValue`.
- **`ModalFallback` partials**: every field is `static`; no cross-part static-initialiser
  dependency (`check-partial-order`: 0 declared, none found).
- **Config keys**: every `ConfigEntry` bound in this set (`WorldUIConfig` 50, `ButtonTuning` 8,
  `DecisionDockSurface` 2) has a reader outside its file or is read inside it. The one that has
  none in *practice* is F1's radius (its only outside read is the log).
- **Composites / dock surfaces / shared map-room files read no live dial** except the surfaces'
  own `ConfigEnabled` toggle (`DecisionDock.Value`).

---

## 7. Reading log

| area | state |
|---|---|
| `INVARIANTS-WorldUI.md`, `REVIEW-WorldUI.md`, `CHARTER.md`, `BRIEF.md`, `LOG-2026-08.md` §3-5, `redundancy-audit.md` §0, `FRAME-ORDER.lock`, the three `.allow` files, `INSTRUMENT-WRITES.baseline`, `STALE-DOC-REFS.md` | read in full |
| Modal: `SharedWindowSizeLaw`, `SharedWindowSize`, `SharedWindows`, `SharedWindowIdentity`, `SharedQuestCornerSeat`, `MenuExclusivity`, `ModalFallback.1.Core`, `HeadFacing` | read in full |
| Modal: `ModalFallback.4.Tick` 860-1300, 1600-2075, 2480-2750; `.7.Close` 1-300, 1395-1425, 1735-1775; `.9.Spawn` 848-1100, 1330-1440, 1680-1760; `ArcSeats` 2590-2632, 3050-3090, 4840-4875, 5560-5680, 6090-6125, 6550-6587 | read |
| Surfaces: `PropInfoSurface` 60-320, 380-500, 620-670; `StatPanelSurface` 95-160, 380-570, 900-945, 1055-1103; `DamageTooltipSurface` 280-365; `DecisionDockSurface` 2740-2820; `TablePanelSurfaces` 540-600; class docs of all 19 files | read |
| Composites: `LoadoutConfirmPark` 845-900, 1040-1100, 1700-1810, 2020-2110; `StoryComposite` 1930-1980, 4290-4395, 4550-4605; `EnchantressComposite` 740-840; `HintOnOwnerComposite` 740-790 | read |
| Root: `WorldUIModule` in full; `WorldUIConfig` 322-410, 814-880; `ActorBars` 1440-1500; `EyeReachCensus` 380-400 + a write grep of 395-870; class docs of all 14 | read |
| MapRoom, Tooltips, Buttons, FlatScreen: class docs of every file; the sweep sites in context; `FlatScreenStereo.3.Map` 976-1024, 1560-1600 | read |
| greps over the whole set: rotation writers, `Config.*.Value`, `FindObjectsOfType`, `catch (Exception)`, `int.MinValue`, `TODO/FIXME/HACK` (none), block comments | done |
| **not read line by line**: the bodies of the MapRoom files (28 k), `UseBarsSurface`, `FloatingDecisionSurfaces`, `CombatLogSurface`, `EnemyRevealSurface`, `InitiativeReorderSlide`, `TablePanelSurfaces` beyond the cited ranges, `WorldTooltips`, `TooltipOnWindow`, `NativeButtonSkin`, the FlatScreen bodies (reviewed 2026-07 and unchanged in shape since), `AvatarMirror`, `WristHud`, `LoadingIndicator`, `MrBacking` | **honest gap** — a defect that needs the body read is not in this document |

---

## 8. Phase 2 — what was done

| # | commit | item | tier | guard verdict |
|---|---|---|---|---|
| 1 | `3e4fe175` | D2 + D3 — two IS-A helpers and six log-material accessors with no reader | 0 | `CHANGED` 7 types, each a pure removal (added 0); surfaces 412/150/4681 unchanged |
| 2 | `42413238` | D1 — the map room's first arc allocator, 561 lines, uncalled since ModBuild 234 | 0 | `CHANGED` `ModalFallback` only (added 0 / removed 356); **surface check RED on six log tokens** — see below |
| 3 | `35f78b1d` | §4's eight cref restorations, F8, the eight `STALE-DOC-REFS` lines retired, F6's cross-references | 0 | **empty** — "no compiled behaviour differs from the baseline" |
| 4 | `03984032` | F1 — the two lying config descriptions and the field doc (landed inside the integrator's snapshot of a limit kill) | 0 | `CHANGED` `WorldUIConfig` only (Bind descriptions are literals); tokens 4675 → 4678, 0 removed |
| 5 | `044cff9d` | F4 — `ModalFallback.FindFloatedWindow` | 2 | `CHANGED` `ModalFallback` + 2 composites |
| 6 | `100c79b0` | F3 — `MovedSubtreeLayers` | 2 | `CHANGED` 3 composites, `NEW` 1 type |
| 7 | `daef9bc6` | F2 — `HoverWindowWatch` | 2 | `CHANGED` 2 surfaces, `NEW` 1 type |
| 8 | `4e88cb0e` | T1 — the per-frame map sweep | 3 | `CHANGED` `FlatScreenStereo` only; one hardware line |
| — | not done | F5 `OwnerRenderHideRecord` | 2 | **deferred** — 26 lines, and the one differing line (`RowBottomUpMeters = null`, a named invariant) sits INSIDE the shared block, so the extraction is worth less than the reading it needs. Left with F6's cross-reference note. |

### 8.1 The one red gate, stated rather than routed around

`42413238` (D1) is the only commit in this lane whose `check-surface.py` verdict is RED. It
reports six log grep tokens REMOVED — `IM SICHTFELD`, `IN THE CONE ANYWAY AND OVERLAPS`,
`IT IS PLACED`, `NEAREST THE CENTRE`, `THE CONE ANYWAY`, `WHEN THE CONE IS FULL`.

Every one of the six lived **only inside the removed bodies** — `TryClaimArcSlot`'s `why`
strings and `LogArcGeometryOnce`'s report — i.e. in a line that has been unreachable since
`cf35c0de` (ModBuild 234) and therefore cannot have appeared in any hardware log for 250
builds. `ArcSeats` prints the live spawn and geometry lines with its own wording, and the
token `MAP ROOM WINDOW SLOTS` itself survives on two live `Warn` lines. `grep -rF` over
`.planning/`, `docs/` and `scripts/` finds no document, backlog item or script that greps any
of the six.

**The checker cannot tell a token in unreachable code from a live one**, and that is a real
blind spot rather than a nuisance: its own rule is "a removal is the failure", and it is right
to be. The lane's judgement is that removing 561 lines of dead allocator is worth six tokens
that no log can contain — but the integrator has the deciding vote, the commit says so in its
own message, and **reverting that one commit costs nothing**: the sibling Tier-0 commit
(`3e4fe175`) is independent of it.

### 8.2 Full suite at the last commit (brief §3.3)

```
refactor-guard.sh check --summary   16 checkers green; 0 moved, 8 changed, 2 added
                                    (every changed type named in the commit that changed it)
                                    configKeys 412 -> 412 · harmonyPatches 150 -> 150
                                    logTokens 4675 -> 4678 (0 removed, 3 added)
EXPECT_WARNINGS=0 ci-build.sh       build gate ok: 0 errors, exactly 0 warnings
check-docs-i18n.py                  4 user-facing docs in English and German — all agree
wire-tests.sh                       210 164 assertions passed
```
