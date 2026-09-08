# Refactor 2026-09 — lane **cards** review

> Base `b40f8564` (the BRIEF commit, ModBuild 480). Worktree
> `/home/claw/gloomhaven_vr/.claude/worktrees/agent-aee5a4c7040d06352`, branch
> `worktree-agent-aee5a4c7040d06352`. Guard baseline taken at that HEAD: 719 types, 412 config
> keys, 150 harmony patches, 4 681 log tokens.
>
> File set: `src/GloomhavenVR/Cards/**` (70 641 lines, 66 files), `src/GloomhavenVR/Plugin.cs`
> (1 112 — the ONLY root file, see §7), `scripts/`, `.github/workflows/`, `docs/`.
>
> Written incrementally (a partial review on disk survives a session kill). §0 says what each
> version covers.

## 0. Coverage and method

| version | covered |
|---|---|
| v1 | census instruments; every R3/R5 (2026-09-07) item whose file is in this set, re-derived at 480; the 2026-08 `REVIEW-Cards.md` dead-code and doc lists re-checked; an unreferenced/write-only sweep over Cards + Plugin (regex, then every survivor verified by hand); `CardsGameApi`, `CardFan`, `CardsConfig.Bind`, `Plugin.Awake` read for structure; `ActiveCardSet`, `CardFlightLedger`, `CardsModule` read in full |
| v3 | the scripts half ACTED ON: seven commits, each with its positive and control (§5.12 is the ledger). One finding measured and NOT acted on — §5.7's collection-write half, which needs a tree-wide rebaseline (§5.7a) |
| v2 | the three mirror pairs side by side (→ `NEEDED-OUTSIDE-cards.md` §1); the scripts/CI half from the one sequential helper (§5), every checker run from a foreign cwd against a planted positive and an unmodified control |

Instruments at HEAD (`hygiene2.py Cards + Plugin.cs`): 1 134 methods, 90.8 % ≤ 50 code lines,
20 over 100, 5 over 200 (`CardsConfig.Bind` 1 055, `CardsDriver.Rebuild` 313, `OnCardReleased`
258, `CardMesh.BuildBeveledKeycap` 232, `PileBrowser.TryRaycast` 212). `dupes2.py Cards 12`: **2**
groups (19 + 14 lines), both examined in §2/§5. `loadbearing.py`: 18 load-bearing instrument
writes, all already in `INSTRUMENT-WRITES.baseline`; none new.

**What landed between the reviews and this base** (`git log 9f646c86..b40f8564`) and therefore
must NOT be re-reported: R5 F2 (`CardsGameApi.RulesEngineBusy` now reads the game's three-term
expression, `c52f0fd8`), R5 F5 (every `CardsGameApi` owner gate now goes through `ControlsActor`,
marked `// F5` at 8 sites), R5 F3 (`TickTakeDamageOptions` closed on 2026-09-08 from the decompiled
`StateMachine.Enter` gamepad gate — the comment at `6.Flows.cs:1644-1653` is the record), R3 F1/F2
(`ce19071f`, `3d8324de`: `BurnLookPolicy.IsLost`/`ForCard` take the owner and walk
`LostAbilityCards` first), R3 F3/F4 (`639152d5`, `85ae0f62`), R3 N2 (`BurnLookPolicy.cs:78`, `:506`
carry the 2026-09-07 correction).

---

## 1. Findings, ranked by risk reduced

Format per brief §3.1: file:line · class · tier · evidence · action · guard expectation.

### 1.1 `CardsGameApi.GetActiveHalves` ↔ `ActiveCardSet.ActiveHalves` — one concept, two expressions, and the code itself says which way to fold

- **Where:** `Cards/CardsGameApi.cs:4256-4287` (owner's board, called from
  `Driver/CardsDriver.6.Flows.cs:3367`) and `Cards/ActiveCardSet.cs:178-216` (mirror, called from
  `Net/Remote/RemoteActiveCardPulse.cs`).
- **Class:** parallel-construction. **Tier 2.**
- **Evidence:** the `dupes2` 14-line group. Side by side after normalisation:

  | `CardsGameApi.GetActiveHalves(hand, card, out, out)` | `ActiveCardSet.ActiveHalves(actor, card, out, out)` |
  |---|---|
  | `top=false; bottom=false;` | same |
  | `actor = hand.PlayerActor; if (actor==null \|\| card==null) return;` | `if (actor==null \|\| card==null) return;` |
  | `bonuses = actor.CharacterClass.FindCasterActiveBonuses(actor);` | `try { klass = SafeClass(actor); if (klass==null) return; bonuses = klass.FindCasterActiveBonuses(actor);` |
  | loop: same four branches, byte-identical | same, inside the `try` |
  | — | `} catch { top=false; bottom=false; }` |
  | `if (!top && !bottom) { top=true; bottom=true; }` | same |

  Two differences, both in the mirror's favour: `SafeClass` (a null-safe class read) and a
  `try/catch` around the game's list allocation. The mirror's own doc block
  (`ActiveCardSet.cs:171-177`) says verbatim: *"THE OWED CHANGE, and it is two lines: make that
  method's body `ActiveHalves(hand?.PlayerActor, card, out top, out bottom)` … It is not folded
  into this method here because that file belongs to another lane this round."* Both files are
  this lane's now. This is the user's standing 1:1 rule applied to the pulse: the owner's board
  and every mirror must resolve the SAME half from the SAME expression.
- **Hard-won difference check:** the only behavioural delta is the `try/catch`. Today an exception
  from `FindCasterActiveBonuses` on the owner's path propagates out of `UpdateActive` into the
  driver's per-tick guard (`CardsDriver.2.Update.cs:840`); after the fold it is swallowed and the
  card is drawn fully lit ("this card is doing something"). That is the mirror's documented stance
  and the safer one for a presentation read. Recorded, not hidden.
- **Action:** make `GetActiveHalves` delegate to `ActiveCardSet.ActiveHalves(hand.PlayerActor, …)`
  (the "two lines" the mirror owes); keep the owner-side doc, corrected to say it is the same
  expression. Fold the ONE sentence at `ActiveCardSet.cs:171-177` ("not folded … another lane").
- **Guard:** `CHANGED GloomhavenVR.Cards/CardsGameApi.cs` only (one method body).

### 1.2 Two short-rest flights announce on the wire around the read-only-focus guard (R3 N4 — still open, unreachable today)

- **Where:** `Driver/CardsDriver.5.Interactions.cs:1731` and `:1853` call `Net.NetCardFx.Report`
  directly; the other seven producers go through `CardsDriver.ReportCardFx`
  (`4.Rebuild.cs:3442-3447`), whose whole body is the `Board.CharacterFocus.ReadOnlyView` early
  return.
- **Class:** risk-gap. **Tier 3 — NOT acted on.**
- **Evidence:** `4.Rebuild.cs:1240` (`shortRested = pick || readOnly ? null : …`) means
  `PresentShortRestCard` never runs under a read-only focus, so the bypass has no reachable input
  today (R3 N4 already established this). Brief §1.1 admits a defect only with a stated input →
  wrong output; there is none.
- **Action (for the user to decide):** the two-token diff is
  `Net.NetCardFx.Report(Net.CardFxAnchor.Discard, Net.CardFxAnchor.Slot0)` →
  `ReportCardFx(Net.CardFxAnchor.Discard, Net.CardFxAnchor.Slot0)` at `:1731`, and the mirror at
  `:1853`. Defence in depth; a compiled change with no observable difference. Left in place.

### 1.3 Dead members — verified against brief §5 (Harmony surface, Unity messages, reflection, config, tokens, debug menu, wire pins, instrument reads)

Sweep method: every `private` declaration in Cards + Plugin, occurrences counted over the whole
set with comments stripped, then each survivor opened. **Two classes of false positive the sweep
produced and a human had to remove:** Harmony patch methods (`DamageFlowPatches.SkipEnterOne` … —
`[HarmonyPrefix]` targets, live) and members used only inside interpolated strings
(`SiteName`, `PackingLabel`, `PhaseName`, `CountHandWidgets`, `_gazeYawDeg` … — all live). The
survivors:

| member | evidence | tier | action |
|---|---|---|---|
| `VRCard._heldRot` (`VRCard.cs:1252`, written `:1385`) | 1 write, 0 reads anywhere in `src/`; `TickHeldPose` derives rotation from the head billboard. The 2026-08 review said this copy "IS used" — it was then; it is not at 480. | 0 | remove field + write |
| `ItemsPile.ItemChip._heldRot` (`ItemsPile.cs:4936`, written `:6225`) | same shape; the comment at `:4933` calls it a "mirror of `VRCard._heldPos/_heldRot/_heldScale`" — fix the sentence to name the two that remain | 0 | remove field + write, fix comment |
| `PlayTray._itemUseSlotGlow` (`PlayTray.1.Core.cs:315`, written `4.Slots.cs:587`, nulled `1.Core.cs:1545`) | 2 writes, 0 reads; the pulse is driven by `reveal.Track(field)` | 0 | remove |
| `PlayTray.SquareCapThickness` (`PlayTray.6.Build.cs:737`) | `const`, 0 references; caps take `WorldUI.ButtonTuning.DashboardDepth` (`7.Nested.cs:1347`). Its doc ("thickness of the SQUARE Confirm/Undo keycaps") is therefore false | 0 | remove; keep the 0.014→0.03→0.036 history as one sentence pointing at `[BoardButtons]` depth |
| `VRCard._visualRoot` (`VRCard.cs:215`) | read only inside `Build` (`:404-424`) | 1 | a local — optional, low value |
| `VRCard.ApplyRenderOnTop` (`VRCard.cs:750`) | 0 callers — **KEEP**, standing ruling: registry §14 was corrected in phase 4 and names it "retained but inactive" | — | none |
| `PlayTray.LogSeatOccupancy` (`PlayTray.6.Build.cs:606-660`) | 0 callers. An INSTRUMENT with no caller: its doc describes a real trap (`RestButtonOffset_Steel.x = −0.44` pushes the cluster off the board) and promises a named log line — which cannot print. Its body writes no field. | risk-gap | **finding, not a fix**: wiring it into `BuildButtons` adds log lines (Tier 3 behaviour); deleting it loses the trap's record. Decide. |

Every 2026-08 dead-code item (§3.2–3.8 of that review) is gone at 480 — `SelectedCount`,
`InitiativeValue`, `ReadyWidget`, `UndoWidget`, `ShortRestWidget`, `GetInitiativeOrder`,
`ActorInitiative`, `IsPlayer`, `ActiveCount`, `SetPopped`, `ReclaimedFromDialog`,
`SpawnConsumedPlume`, `_plume`, `_hasClip`, `GenericClusterButtonSize`, `ReseatProud`,
`SeatOnBoardFace`, `SeatStandoff`, `PlayTray.RenderOnTop`: 0 hits each.

### 1.4 Comments falsified by source (brief §1.3 — fix the sentence, never delete)

| where | claim | falsified by | tier |
|---|---|---|---|
| `Cards/CardsGameApi.cs:1866` | *"`scripts/check-game-expression-subset.py` is the gate that refuses a bare read from being written again"* | no such file; the gate is `scripts/check-mirrors.sh` PART 3 ("the rules-engine busy triple", scope `Cards/CardsGameApi.cs`) | 0 |
| `Cards/CardFlightLedger.cs:92-150` (the flight table, R3 N5) | 20 `file:line` citations | every one has moved: `TryStartFlyToPile` 4.Rebuild:**2566** (says 2423), `TryStartBurnFly` :**2975** (2786), `LaunchBurnFlight` :**3906** (3561), `BurnSlab` :**4036** (3624), `FlyLockedPicksToPile` 5.Interactions:**1445** (1384), `FlyShortRestCardToDiscard` :**1841** (1781), `DrainPickReturnFlight` 4.Rebuild:**3301** (3075), `ActivePileViewer.Relayout` :**243** (195), `LaunchActiveFlights` 6.Flows:**3594** (2745), `StartBrowseCollapse` :**2997** (2162), `ReturnCardToPile` :**3091** (~2245), `VRCard.FlyToPile` :**1585** (1547), `FlyFromPile` :**1639** (1601), `SetHome` :**879** (877), `RemoteBurnFx.Handover` :**1398** (944), `RemoteCardFx.Play` :**184** (167), `CardFan.Relayout`/`BeginSwapOut`/`TickCollapse` :**1917/2475/2228** (1878/2436/2189), `PlayTray.PlaceCard` 4.Slots:**1193** (1204), `ItemChip.BeginEmerge`/`BeginCollapse` :**5663/5696** (5642/5675), `RemoteBrowserFan.BeginEmerge`/`BeginCollapse` :**577/1037** (556/1028), `RemoteHandFan.BeginSwap` :**630** (618), short-rest present 5.Interactions:**1660** (1667). Row 2's `IsFreshBurn` gate is real (`4.Rebuild.cs:2914`) | 0 |
| `Cards/CardFlightLedger.cs:97` (row 7, R3 N6) | *"the 4 Hz walk of the peer's host-replicated Lost pile"* | `RemoteBurnFx.WatchSeconds = 0.08f` (`:135`) — 12.5 Hz; 4 Hz was the retired `RemoteBoardContent.DefaultRefreshSeconds` | 0 |
| `Cards/Patches/HandSuppressionPatches.cs:46` (`STALE-DOC-REFS.md` row) | `<c>Core.VREvents.HandShown</c>` — demoted because the cref did not resolve | `VREvents.HandShown` exists (`Core/Events/VREvents.cs:244`); the sentence is TRUE, only the qualification was wrong. Restore `<see cref="VREvents.HandShown"/>` and clear the row | 0 |
| `Cards/Piles/ItemsPile.cs:4933` | *"mirror of `VRCard._heldPos/_heldRot/_heldScale`"* | `_heldRot` is write-only on both sides (§1.3) | 0 |
| `Cards/Tray/PlayTray.6.Build.cs:731-736` | `SquareCapThickness` doc, present tense | unread const (§1.3) | 0 |

Policy note for the ledger: `INVARIANTS-2026-08-SplitTargets.md` says *"symbol names only, never
line numbers"* for exactly this reason. The minimal fix re-greps the numbers once and adds one
sentence saying they are as of ModBuild 480 — a future reader then knows to re-grep rather than
trust. Rewriting the table without numbers would be a rewrite, not a correction.

### 1.5 Structure — the split targets, and what each split would break

**`CardsConfig.Bind` (`CardsConfig.cs:636-1850`, 1 055 code lines).** Tier 1 candidate. Read for
locals crossing section boundaries: three (`pileNames`/`stepSeeds`/`radiusSeeds` at `:911-913`,
consumed by the loop at `:914` only; `boardScaleMigrated` `:1502`, `decisionYRebased` `:1537`,
each consumed by its own `if` immediately below). Everything else is a `_file.Bind(…)` into a
static field. Sections already exist as `// ----` dividers at `:1174`, `:1182`, `:1250`
(the per-board loop), `:1561`, `:1598`, `:1710`, `:1741`, `:1778`, `:1812`. A verbatim split into
ordered private static methods called in sequence from `Bind` keeps: the cfg key ORDER (registry
§8 — order of `Bind` calls is the order in the player's file), every literal, every default,
every description. What the checkers see: `check-surface.py` `BIND` regex is per-file text
(`scripts/check-surface.py:134`), `check-options-coverage.py`'s `ENTRY_DECL`/`ASSIGN_BIND` are
file-local (`:404-407`) — a same-file extraction is invisible to both, which is the point.
**Guard:** `CHANGED GloomhavenVR.Cards/CardsConfig.cs` only (new private methods); config-key
census unchanged at 412. **Verification the guard cannot do:** the ORDER — a before/after diff of
the decompiled `Bind` body's `Bind(` call sequence (the snapshot keeps string literals, so the key
order is diffable in `.guard/current`).

**`Plugin.Awake` (`Plugin.cs:386-953`, 380 code lines).** Same shape: a straight sequence of
`Config.Bind` per section (`[General]`, `[Core]`, `[Rig]`, the ModBuild 230 one-shot, `[MapRoom]`,
`[Compat]`, `[Hands]`, `[Dev]`) with one local (`delayFrames`, at the tail). The top comment
(`:390-397`) makes the ORDER load-bearing — `ReadRawConfigValueBeforeBind` must precede the first
`Bind` — and `RegisterModules` is already the documented order contract. Tier 1 by the same
recipe; `Config` is an instance property, so instance methods. Lower value than `CardsConfig`
(the file is 1 112 lines, navigable). **Guard:** `CHANGED GloomhavenVR/Plugin.cs` only.

**`CardsGameApi` (4 297 lines, static class) and `CardFan` (2 889, sealed instance class)** —
the 2026-08 deferred split targets with no invariant entry. Entries drafted in §3 below, which is
the deliverable the brief asks for BEFORE a split. **Neither is split this round** (reasons in §3):
the guard cannot see what the two file-scoped gates pin, and a split that silently narrows a gate
is worse than a long file.

**The big methods** — `CardsDriver.Rebuild` (313), `OnCardReleased` (258), `CardMesh.BuildBeveledKeycap`
(232), `PileBrowser.TryRaycast` (212): not touched. The 2026-08 review already established that
`Rebuild`'s `switch` assigns three locals read afterwards and cannot be extracted verbatim (§7.3
there), that `TryRaycast` must not be merged with `CardFan.TryRaycast` (§6.2 — resting vs live
rect, invariant §1), and `BuildBeveledKeycap` is one winding-ordered vertex list (registry §7,
memory `winding-bug-class`): a "verbatim" extraction that moves a vertex emit across a submesh
boundary is a Tier 3 change wearing Tier 1 clothes. Leave.

### 1.6 Deadlock class (`.planning/deadlock-class-audit.md` §0 — a gate reading a SUBSET of the game's expression)

Checked in this file set at 480: `CardsGameApi.RulesEngineBusy` (three terms, closed);
`TickTakeDamageOptions` recomputes the game's OWN formula (`hasEnough… && ThisPlayerHasTakeDamageControl`)
and writes it back — not a subset; `PickFlowWatch` (R5 §6 clean list) unchanged. No new instance
found. `check-mirrors.sh` PART 3 now holds the class for `Cards/CardsGameApi.cs` and
`Board/FigureGrab/`.

---

## 2. Negative results (checked, and NOT to be acted on)

- **`CardFan.TryRaycast` tail ↔ `ActivePileViewer.TryRaycast` tail** (`dupes2` 19-line group,
  `CardFan.cs:2869-2887` / `ActivePileViewer.cs:424-441`). The identical lines are the
  sticky-incumbent/nearest bookkeeping; the hit test above them differs exactly as invariant §1
  requires (resting rect + 1.10 accept margin vs live `InverseTransformPoint` + exact
  half-extents). Extracting the tail into a helper would put the two raycasters one refactor away
  from "simplifying" the head too. 2026-08 §6.2 stands.
- **`VRCard.ApplyRenderOnTop` / `RestoreRenderOnTop` / `CardMesh.HeldCardRenderQueue`** — kept by
  the phase-4 ruling (registry §14).
- **R5 F6 (`AnimateCardsLost`'s untimed park)** — `DamageFlowPatches.cs:255` alerts and
  deliberately repairs nothing; the reason (presentation code must not write the game's animation
  list) is sound and a remedy is a design decision, not a refactor.
- **R5 W3/W4/W5/W6/W7 watchdogs** in `CardsDriver.6.Flows`, `PickFlowPatches`,
  `DamageFlowPatches` — each already prints its premise; none reads a subset.
- **`SetRenderOnTop`'s five no-op call sites** — the record of a rejected approach (charter §2).
- **The `Log*` methods in `INSTRUMENT-WRITES.baseline`** (`LogFanWithheld`, `LogFlightRefused`,
  `LogPickSource`, `LogActionSelectionState`, `LogOverlayHeldByExit`, `LogFanState`,
  `LogExhaustedBoardClear`, `LogFanMode`) — load-bearing writes; none moved, none gated.

---

## 3. Invariant entries for the two uncovered split targets (registry format: symbol names, never line numbers)

### 3.1 `CardsGameApi` — Cards

**What a FILE split breaks, and nothing else in the tree will tell you:**

- **`scripts/check-mirrors.sh` PART 3 scopes the subset-guard "the rules-engine busy triple" to
  the FILE `Cards/CardsGameApi.cs`.** A file in scope that stops reading the trigger term is simply
  skipped — so moving `RulesEngineBusy` (or any new reader of `IsProcessingOrMessagesQueued`) into
  `CardsGameApi.2.Busy.cs` passes the gate and silently takes the expression OUT of coverage. Rule:
  a split of this type changes the PART 3 scope entry in the same commit (to the new file, or to
  `Cards/` as a directory, which the gate accepts unmarked). `Breaks if:` a partial is created
  without touching `check-mirrors.sh`.
- **Static state.** Nine private statics with no initialiser dependencies: the six `_own*`
  reflection handles + `_ownReflected` (the ownership registry), `_storyDelayFirstSeen`,
  `_storyDelayStaleReported`, `_soloHostRescueLogged`. `check-partial-order.py` proves compile
  order is inert today; keep it so (no static initialiser may reference another part's static).
- **`ControlsActor` / `LocalControlsActor` are the owner gate** — 8 sites inside the file marked
  `// F5`, 13 outside (`Net/RevealGate`, `Net/DecisionLabelMask`, `Board/SelectionOwnershipFallback`,
  `Board/CharacterFocus` ×2, …). The fallback `answerable ? byList : actor.IsUnderMyControl` is
  the shipped-before behaviour and is the ONLY place the stale-able flag may be read for an
  ownership decision in this file. `Breaks if:` a new site reads `IsUnderMyControl` directly
  (R5 F5's shape), or the reflection block and its consumers land in different parts without the
  `_ownReflected` latch travelling with the handles.
- **Cross-file couplings:** `tests/GloomhavenVR.WireTests/CapLabelFitVectors.cs` cites
  `CardsGameApi.cs:2509/2558/2632` for the EN keycap words (comments, not pins — but the vectors
  ARE the strings; a split must not reword `Confirm`/`Undo`/`Skip`); `HandSuppressionPatches`
  raises `HandShown`, which this file's hand-lookup section documents as the rebuild edge;
  `Net/Remote/RemoteActiveCardPulse` mirrors `GetActiveHalves` (§1.1 folds it).
- **Rejected alternative, recorded in the file:** a bare `IsProcessingOrMessagesQueued` read
  (ModBuild 479 → 480) — `RulesEngineBusy`'s doc is the record; do not "simplify" it back.
- **Confidence:** high (each rule is a gate or a comment at the site).

### 3.2 `CardFan` — Cards

- **`scripts/check-mirrors.sh` pins SEVEN constants by FILE and NAME:**
  `Cards/CardFan.cs:GazeBiasDeadzoneDeg / GazeBiasReleaseDeg / GazeBiasFullDeg / GazeBiasMaxYawDeg /
  GazeBiasGain / GazeBiasSmoothing / ZStagger` against `Net/Remote/RemoteHandFan.cs` (1:1 groups).
  The extractor reads the named file; a constant moved to `CardFan.2.Gaze.cs` fails the gate with
  "did it move or get renamed?" — loud, so a split is safe, but the pins move in the same commit.
- **Instance fields with initialisers** (`_hoveredIndex = -1`, `_pokeHoveredIndex = -1`,
  `_insertGap = -1`, `_openElapsed = -1f`, `_closeElapsed = -1f`, `_gazeEdgeX = 0.1f`,
  `_layoutGazeX = float.NaN`, `_lastGazeRelayoutTime = float.NegativeInfinity`,
  `_depths = new float[16]`, `_loggedApex`, `_lastFanParamSig`, `_swapElapsed = -1f`, the two
  `List<VRCard>`) — all constants, no cross-field dependency; CHARTER §3 rule: every one stays in
  the primary file in its original order.
- **Cross-file couplings:** `CardFan.Current` (static) is read by the net presence sender for the
  hand-card COUNT (backs only, never identity — the anti-cheat line); `Root` is the seam
  `WorldUI.AvatarMirror` re-emits the arc under; `Hand` is read by `Hands.HandGhosts`;
  `HighlightedIndex` rides the wire as a bare index. None of these may change type or meaning in
  a split. `SetHovered`/`SetInsertionGap` are the driver's two per-frame writes
  (`CardsDriver.3.Laser` / `4.Rebuild`); FRAME-ORDER.lock orders their callers, not the fan.
- **Rejected alternatives at the site:** the allocation-rule header (the "no allocations in
  Tick" claim was false and was replaced by the gate-ORDER rule — diagnostics sit behind the
  change gate); `FanGazeApexFollow = 0` must stay a byte-exact revert (registry §4).
- **Confidence:** high.

---

## 4. NEEDED-OUTSIDE (see `NEEDED-OUTSIDE-cards.md`; v2 adds the mirror-pair side-by-sides)

- `.planning/deadlock-class-audit.md` §5 corrections listed in R5 are a planning doc outside any
  lane's set; nothing in this lane depends on it.

## 5. Scripts half — every checker made to fail before it was believed

Method (helper, read-only, `…/scratchpad/cr/`): every checker copied with `src/`, `docs/`, `tests/`,
`libs/`, `prebuilt/`, `.planning/refactor*`, run from `/tmp` unmodified (negative control — all 19
runnable gates exit 0 with full counts) and with a planted synthetic positive, restored after each.
Not runnable there: `wire-tests.sh`, `package-release.sh`, `release.yml`, the guard's real compiled
diff — reasoned from code; the guard's `snapshot()` was exercised with stubbed `dotnet`/`ilspycmd`.
Any script change is **Tier 3** for the report: each line names what changes for the user.

### 5.1 `scripts/refactor-guard.sh:100-101` — a FAILED build never trips "build failed"; the guard decompiles the STALE DLL and can print "no compiled behaviour differs"

- **Class:** defect. **Evidence:** `dotnet build … | grep -E "error|Build FAILED" && { …; exit 1; }`
  under `set -euo pipefail`: the pipeline's status is dotnet's non-zero exit whenever dotnet fails,
  so `&&` skips the block; `set -e` does not fire on the left of `&&`. The block fires only when
  dotnet exits 0 AND its output contains "error". Stubbed runs: `{echo "error CS1"; exit 1}` → rc 0,
  continued to `ilspycmd` on the stale file; `{exit 1}` → same; `{echo "error-prone"; exit 0}` →
  rc 1 (a FALSE failure). **Consequence:** a change that does not compile pastes a green
  `--summary` into its commit message. `ci-build.sh` (which tests `$?`) is the only thing that would
  catch it. **Action:** test the exit status, not a grep. **User observes:** a red build now says
  `error: build failed` and stops; nothing else changes. Same function, `:123`:
  `grep -rlZ 'built 20\|BuildTimeUtc' … | xargs` — a zero-match grep under `pipefail` aborts the
  guard with NO message; `|| true` as the masking below it already has.

### 5.2 `scripts/check-surface.py:134` — 22 `[Comfort]` keys are invisible to the config-key census; removing one passes

- **Class:** risk-gap (class d). `BIND` requires `\.Bind("Section","Key"`; `Rig/ComfortSettings.cs`
  binds 22 keys as `X = Bind("Key", …)` through a helper that supplies the section (`:121`, `:512`).
  `check-options-coverage.py:180-192` documents exactly this blind spot and expands the helper
  itself. **Positive:** rename `Bind("WorldGrabEnabled",` → `…X` → `configKeys 412 -> 412`, rc 0.
  **Control:** `"Rig", "SpawnInCircle"` → `…X` → `1 removed`, rc 1. Interpolated `$"…{var}"` keys
  are also uncounted. **Action:** expand the helper form and the interpolated form the way
  options-coverage does. **User observes:** `config keys 412` becomes ~430+ on the next baseline;
  a removed Comfort key then fails.

### 5.3 `scripts/check-surface.py:149` — two of the eleven "OWED ON HARDWARE" tokens in brief §4 are protected by nothing

- `TOKEN = \b[A-Z][A-Z0-9_]{1,}(?:[ \-][A-Z0-9_]{2,})+\b`: `GATE 3` (`Net/Remote/RemoteUseBarSymbols.cs:209`)
  fails on the one-character `3`; `Remote BURN look` (`Net/Remote/RemoteCardArt.cs`) has one shouted
  word. Census grep: 0 each; the other nine: 1 each. **Positive:** `GATE 3 (slot count)` → `GATE 4`
  → rc 0; `Remote BURN look armed` → `…primed` → rc 0. **Control:** `HELD BAR HIDE REFUSED` →
  `…DECLINED` → `REMOVED — LOG GREP TOKEN`, rc 1. **Action:** a `PINNED_TOKENS` list for markers
  that do not fit the shape (lists exactly what the brief lists). Also invisible: three
  `[HarmonyPatch(…)]` attributes whose argument text contains `]` (`PropInfoSurface.cs:1025`,
  `TakeDamagePanelSafety.cs:217,221`) — `patch-inventory.sh check` catches their removal, but the
  surface count `harmonyPatches 150` undercounts by 3.

### 5.4 `scripts/log-triage.py` — four registry tokens are written at `VRLog.Info`, which the DEFAULT level does not print; the flagship `decisive` instrument is one of them

- `Defaults.Plugin.cs:23` `LogLevel = Info`; `Core/VRLog.cs:58-61`: `Note` prints at Info,
  `Info`/`Warn`/`Debug` only at Debug. `Pile fan content` (`Cards/Driver/CardsDriver.6.Flows.cs:3288`,
  THIS lane), `USE BARS: docked`, `Pick banner SENT`, `DOCK MIRROR` (`RemoteUseBarSymbols.cs:468`;
  Note at `RemoteBoardFurniture.cs:3853/3862`) are Info. None carries `// HW-VERIFY`, so
  `check-hw-verify.py` cannot see them. On a default-level drop they read as `SILENT — … nothing
  exercised it, or it is unreachable, or its gate never opened` — the third cause (tier) is not in
  the list, and `SILENT ON THE QUESTION` can never fire for `Pile fan content`. **Action:** give
  `Instrument` a `tier` (or grep the source for the call's `VRLog.<X>`) and print "Debug-tier:
  silence is expected at the default level". Separately (Cards lane): decide whether
  `Pile fan content` becomes `Note` + `// HW-VERIFY` — a log-level change is Tier 3 behaviour.
- `--expect-build`: verified on synthetic logs — no banner → `STALE OR WRONG DROP` (good); two
  builds → both named + STALE (good); relaunch same build → one build (good); host 480 + peer 479
  → `NOT THE SAME BUILD` (good); 481 vs expected 480 → says "STALE" for a NEWER build (cosmetic).
  **Defects:** (i) `--token` ad-hoc mode returns before `report()`, so `--expect-build` is silently
  ignored; (ii) `BUILD_RE` (`:357`) is not anchored on `GloomhavenVR] ` unlike every other grep —
  a foreign line containing `GloomhavenVR ModBuild 478` reads as a second build (latent: no
  runtime line prints that shape today); (iii) `c8de9e00` shipped with no test log.

### 5.5 `scripts/refactor-guard.sh:339` — `check` WITHOUT `--summary` always exits 0

- `diff -ru … || true` is the last command. Only the `--summary` branch's last `[[ … ]] && echo`
  carries the verdict (rc 1 when types changed). Anyone chaining `refactor-guard.sh check && …`
  gets a pass on a differing snapshot. **Action:** exit with the differing-file count, or document
  that only `--summary` has a verdict in its exit code.

### 5.6 `scripts/check-partial-order.py:130` — a type-qualified cross-part read is invisible

- `IDENT = (?<![\w.])(\w+)` skips identifiers preceded by `.`, so `static readonly int A = ZzT.B + 1;`
  (or `GloomhavenVR.ZzT.B`) is not a reference to `B`. **Positive:** unqualified `B + 1` →
  `UNDECLARED CROSS-PART`, rc 1; `ZzT.B + 1` → rc 0; `GloomhavenVR.ZzT.B` → rc 0; one-line body →
  refused, rc 1 (good). **Action:** accept a `.`-preceded identifier when the preceding token is the
  type's own (or namespace-qualified) name. The 20 multi-part types use `WallSegmentFade.X`-style
  self-references heavily.

### 5.7 `scripts/check-instrument-writes.py:88-92, 417-425` — a collection mutation or a property write inside a diagnostic is not a "write"; an EMPTY `src/` is reported as good news

- **Positive:** `LogZz(){ _zzFlag = true; }` read in `Tick` → `NEW LOAD-BEARING WRITE`, rc 1;
  `_zzList.Add(1); ZzProp = true;` read in `Tick` → rc 0, nothing reported. The
  `a-write-inside-a-logger` class includes `.Add/.Clear/.Remove` on a field. **Action:** count
  `field.(Add|Remove|Clear|Insert|Enqueue|Push|Set…)(` inside an instrument as a write; include
  auto-properties. **Empty-tree control** (class c/d): on an empty `src/` it exits 0 with
  `65 baseline entries no longer load-bearing (good — rebaseline when convenient)`; likewise
  `check-partial-order.py` (`0 multi-part types`, rc 0) and `check-enum-arrays.py` (`0 literal-sized
  array(s)`, rc 0). `check-hw-verify`, `check-mirror-dials`, `check-tune-fields`, `check-surface`
  all refuse an empty input. **Action:** a floor per checker, as `check-tune-fields` has.

### 5.7a The collection-write half, MEASURED and NOT acted on — it needs a rebaseline that crosses every lane

The floors landed (`0c41e6cc`). The other half of §5.7 — counting `_field.Add(…)` / `.Clear()`
inside a diagnostic as a write — was implemented and measured on a scratch copy of the tree
rather than committed, because of what the measurement says:

    before: 558 fields written by diagnostics, 65 load-bearing, 493 instrument-only
    after:  672 fields written by diagnostics, 102 load-bearing, 570 instrument-only
    → 39 NEW load-bearing pairs, and the gate is baseline-gated, so it exits 1 until
      `--baseline` is re-run

The rebaseline rewrites `INSTRUMENT-WRITES.baseline` with entries naming **other lanes'
files** — `FadeDriver` (11 of the 39), `ModalFallback` (3), `MixedReality`, `MapIconHover…`,
`MapLocationInteractor`, `PerfMonitor`, `GrabbableProp`, `ActorPropBody`, `NetCardFx`, … —
which brief §2 forbids this lane from touching. It is the integrator's to take, once, with
each lane reading its own new entries.

**And the measurement carries a second finding that argues for doing it carefully rather than
quickly.** A large share of the 39 are *scratch buffers*, not state: `FadeDriver::_matScratch`,
`::_subtreeScratch`, `ActorPropBody::RendererScratch`, `MixedReality::UnbackedScratch`, and in
this lane `ActiveCardSet::s_sb` / `::s_sort` (`ActiveCardSet.cs:314-315`) — a `List` and a
`StringBuilder` that `FormatModel` clears, fills and hands to `Format()`, both cleared again
before return (`:524`, `:542`). They read as "load-bearing" only because `Format` does not match
`DIAG_NAME` (`Log|Census|Report|Dump|Describe|Diag|Trace|Probe|Audit|Explain|Print`) and was not
promoted, so its reads count as "outside any diagnostic". Landing the change as implemented
would put ~a dozen buffers into a registry whose entire value is that every line in it is a real
constraint. **The shape worth landing** is the mutating-call scan PLUS either a `Format`/`Fmt`/
`Text`/`Describe` seed in `DIAG_NAME`, or an exemption for a field that is cleared on both sides
of its use within one call. `NetCardFx::s_queue <- Report` is the one hit that looks like the
genuine article and should be read first.

**Not a defect in the shipped mod.** Nothing here says any of the 39 is wrong today; it says the
gate cannot currently see that class at all.

### 5.8 `.github/workflows/ci.yml` — nine runner-capable gates never run there

| gate | guard | ci | release | runner-capable |
|---|---|---|---|---|
| patch-inventory, frame-order, mirrors, wire-coverage, card-identity-mask, mirror-dials, enum-arrays, bundle-format | ✓ | ✓ | ✓ | yes |
| wire-tests.sh | ✓ | compile only | compile only | no (real `UnityEngine.CoreModule.dll`) |
| check-partial-order.py | ✓ | — | — | **yes** (src + PARTIAL-ORDER.allow, tracked) |
| check-instrument-writes.py | ✓ | — | — | **yes** (src + INSTRUMENT-WRITES.baseline) |
| check-remote-defaults.py, check-tune-fields.py, check-desync-surface.py, check-hw-verify.py, check-options-coverage.py | ✓ | — | — | **yes** (src / docs only) |
| check-surface.py diff | ✓ | — | — | **yes on a PR**: snapshot at `git merge-base HEAD origin/dev` in a second worktree (ROOT is `__file__`-relative), diff against the PR's; needs `fetch-depth: 0` |
| compiled diff (ilspycmd) | ✓ | — | — | not attempted (ilspycmd as a dotnet tool + `-r libs/RefAsm` + a merge-base build) |
| check-docs-i18n.py | — | — | — | **yes**, no caller anywhere; green on a runner's inputs |
| check-refasm.py, ci-build.sh | — | ✓ | ✓ | yes |
| rebase-defaults.py check | — | skipped w/ notice | skipped w/ notice | no (`.planning/debug` gitignored) |

Count correction: `refactor-guard.sh check` runs **17** gates + the compiled diff (brief §3.3 says
16; the script's own header lists 14 and says "four"); CI runs 8 fully + 1 compile-only (the brief's
"9 of 19"). **Action:** one `run:` block of the eight pure-python gates (~10 s) in `ci.yml`, plus the
merge-base surface diff on `pull_request`. **User observes:** a PR that removes a config key or
moves a static initialiser across partials now fails on GitHub, not only on the desk.

### 5.9 Smaller

- `scripts/package-release.sh:173`, `release.yml:252` — `unzip -l | grep -q` under `pipefail`
  (class b, latent: the listing is ~15 lines and fits the pipe buffer; `seq 200000 | grep -q '^1$'`
  → rc 141). Herestring/file idiom as `check-mirrors.sh` PART 3 documents.
- `refactor-guard.sh:328` `--summary` parser: `rel="${rel%% and *}"` truncates any path containing
  ` and ` (theoretical — ilspycmd `-p` folders are namespaces).
- Doc drift (this lane's files): `ci.yml:87` / `release.yml:192` step name "exactly 6 known
  warnings" vs `ci-build.sh:51` `EXPECT_WARNINGS=0` (the header `:9-14` still says six, `:101`
  prints `the six known warnings are  only` with an empty list); `refactor-guard.sh:24` "four
  checkers" / header lists 14 of 17; `docs/CI-CD.md:410` lists `rebase-defaults.py check` as a CI
  step (it is skipped with a notice on a runner); `CI-CD.md:291,543` "200,000+ assertions … no
  figure is frozen here" vs `ci.yml:186` "146,839"; `check-remote-defaults.py:49` "What is genuinely
  no longer covered: nothing" — falsified by `check-mirror-dials.py`'s own header (R2).
  No `scripts/<name>` cited by `docs/CI-CD.md`, `DEVELOPING.md`, `TESTING-*.md` is missing (22 names).

### 5.10 Negative results (tried to break, could not)

Class (a) repeated named group: none in `scripts/*.py` (`check-partial-order.py:115` keeps the `+`
inside the group as documented). Class (b): the only `| grep -q` sites are `package-release.sh:173`
and a comment. Class (c): every checker resolves ROOT from `__file__`/`BASH_SOURCE`; all 19 ran from
`/tmp` with full counts. Failed on their positive and passed their control: check-frame-order (both
levers), check-mirrors (all three parts; P3 with the companion at 4 code sites), patch-inventory
(INERT detection, bracketed attributes), check-card-identity-mask, check-mirror-dials,
check-enum-arrays, check-tune-fields, check-remote-defaults, check-wire-coverage,
check-options-coverage, check-refasm (the real DLL → `1 of 1 … executable code`, rc 1),
check-bundle-format, check-docs-i18n, ci-build.sh (1 warning → rc 1; failed build → rc 1).
log-triage anchoring (`GloomhavenVR] ` + 200-char window) behaves as documented.

### 5.11 Order of work (by risk reduced), each its own commit with positive + control in the message

1. 5.1 guard build-status (the umbrella gate's central promise) · 2. 5.3 pinned tokens (the
brief's own list) · 3. 5.2 Comfort/interpolated keys · 4. 5.8 CI wiring · 5. 5.7 floors +
collection writes · 6. 5.6 qualified refs · 7. 5.4 log-triage tier/anchor/`--token` ·
8. 5.5 exit code · 9. 5.9 docs.

### 5.12 What was done — the ledger

| finding | commit | positive → control |
|---|---|---|
| 5.1 guard build status, the `\|xargs` abort, 5.5 exit code, the header count | `e464f786` | three stubbed dotnets (fails loudly / fails silently / prints "error" but succeeds) → a clean stub still snapshots |
| 5.2 + 5.3 the census's blind keys and the two unpinnable markers | `07c383ce` (content `83c06d0f`) | a renamed `[Comfort]` key, a renamed interpolated per-board key, a reworded `GATE 3` and `Remote BURN look` → the unmodified tree, and the OLD baseline vs the NEW census (0 removed, 213+2 added) |
| 5.8 nine gates into CI + the PR-base surface diff | `dca03c86` (content `83c06d0f`) | all nine run at this HEAD; the PR step rehearsed with `BASE_SHA=b40f8564` → both workflows parse, 27 + 25 steps |
| 5.7 floors on three gates that called an empty census good news | `0c41e6cc` | an empty `src/` → the real tree |
| 5.6 `MyType.Field` cross-part dependency | `f33e7d9f` | the same dependency written three ways → the real tree, **which caught a false-positive class in the first version of the fix** |
| 5.4 log-triage tier / anchor / `--token` / wording | `7587e1e5` | four synthetic logs, before-and-after on each → a matching `--expect-build` on the same drop |
| 5.9 the six-warning-era docs and the empty-list messages | `583b6e66` | `ci-build.sh` re-run; both YAMLs re-parsed; the two edited gates re-run |
| 5.9 `unzip -l \| grep -q` under pipefail | `059aecf0` | a 299 KB listing → a 204 B listing, both containing the path (rc 141 vs rc 0) |
| 5.7a collection writes | **not committed** | measured: +39 pairs, needs a cross-lane rebaseline — §5.7a |

## 6. Hardware-observable changes

None from Tier 0–2. Tier 3 (scripts) entries go to `HARDWARE-REGRESSION-2026-09.md` § cards when made.

## 7. Anything in the brief that was wrong

1. *"`src/GloomhavenVR/*.cs` (Plugin.cs and the other root files)"* — there is exactly ONE root
   file, `Plugin.cs`. The census's "(root) 5 of 363" line under `hygiene2.py` for this lane is the
   19 Cards root files, not `src/GloomhavenVR/*.cs`.
2. *"`CardsGameApi.ControlsActor` is the owner gate at 18 sites"* — at 480 it is 8 inside the
   file + 13 outside = 21 call sites (`grep -rn 'ControlsActor('`), the 8 inside marked `// F5`.
3. The brief's census line "`CardsDriver.4.Rebuild.Rebuild` (313)" and "`OnCardReleased` (258)"
   agree with the instrument; "`CardsConfig.Bind` (1 055 code lines)" agrees.
4. `dupes2.py <your paths> 12` takes ONE path argument; `dupes2.py src/GloomhavenVR/Cards
   src/GloomhavenVR/*.cs 12` dies on `int('src/GloomhavenVR/Plugin.cs')`. Run it per path.
5. §3.3 "16 checkers + the compiled diff": it is 17 (see §5.8). §4's "`check-surface.py` fails on
   it" is true for 9 of the 11 owed tokens and for none of the 22 `[Comfort]` keys (§5.2, §5.3).
6. Sub-agent fan-out (brief §3.1 "read-only sub-agents … fine and encouraged") was withdrawn by
   the user mid-round: at most one sub-agent alive at a time.
