# Review D — worldui-frame lane, sub-set `src/GloomhavenVR/Board/FigureGrab/` (37 files)

Reviewer: sub-reviewer D (read-only). Worktree: `/home/claw/gloomhaven_vr/.claude/worktrees/agent-a236ff8d959582747`, branch `boards-texture-rework`, HEAD 7842c10b.
Written incrementally, file by file. Findings are appended as found; ranking is applied in the final "Ranked index" section at the end.

## Findings

<!-- prep notes (verified before reading code) -->
- Prep: `STALE-DOC-REFS.md` lists NO line under `Board/FigureGrab/` — nothing to clear in this set.
- Prep: `VRLog.cs` verified — `Info`/`Warn`/`Debug` gate on `Level >= Debug` (:155/:165/:175); `Note` on `>= Info` (:140); `Alert` on `>= Warning`; `Error` on `>= Error`. Shipped default `Level = Info` (:91). So `Info`/`Warn` print NOTHING at the shipped level.
- Prep: `ActorBehaviour_HeldTransform_Patch` registered exactly once: `src/GloomhavenVR/Board/BoardModule.cs:118 VRSession.Harmony?.PatchAll(typeof(FigureGrab.ActorBehaviour_HeldTransform_Patch))`. No second `PatchAll` names it. REVIEW §2.11 "leave it in `Board/FigureGrab/`" still holds.
- Prep: `check-mirrors.sh` PART 3 lines 574-640 read; `FigureBusy.cs:334-337` still carries all three rules-engine terms (`IsProcessingOrMessagesQueued`, `WaitingForPlayerToSelectDamageResponse`, `WaitingForPlayerActorToAvoidDamageResponse`) — confirmed at HEAD.
- Prep: PropAnimBelt history read — 6082b83b (cleanup) then 13 further rounds up to 25541168 (ModBuild 471). Later commits retired: `PropOcclusionGate` + dial, `PropAnimBelt.PositionSweep` (a9c6fc8a). Live at 25541168 per its own message: HOME TWIN (+ property-block, provenance, census clauses), RENDER-PASS PROBE, PHOTOMETER (+ stillness clause), OVERLAY PULSES high-water census.

<!-- file read: FigureGrabbable.cs 1800 lines, whole -->

### D-01 — `FigureGrabbable.cs` holds TWO top-level types (`FigureGrabbable` + `FigureBody`)
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/FigureGrabbable.cs:1471-1800` (`internal static class FigureBody`)
- **Class:** structure-naming
- **Tier:** 1 motion
- **Evidence:** `FigureBody` (the live-bone / baked-skin extent rule, 330 lines with its own 80-line doc) is consumed by `WorldUI.ActorBars` and `FigureGrabDriver` — neither of which has anything to do with `FigureGrabbable`'s grab/release/glide. A reader grepping for the health-bar anchor rule lands in the grabbable's file. The doc itself says "one rule, shared by the two subsystems".
- **Proposed action:** move `FigureBody` verbatim into `Board/FigureGrab/FigureBody.cs` (whole-type move; `s_bakeScratch`/`BakedVertices` are static, no field-initialiser ordering hazard). Optional; low value; do only if a Tier-1 commit is being made in this folder anyway.
- **Guard expectation:** empty (whole type moved; `ilspycmd -p` files types separately).
- **Risk if wrong:** none observable; a `MOVED` on the csproj only.
- **Cross-lane:** none (ActorBars is worldui-front but references the type by name, which is unchanged).

### D-02 — INVARIANTS §8/§14/§15 entries naming `FigureGrabbable` — all VERIFIED STILL TRUE at HEAD (leave-alone)
- **File:line:** `FigureGrabbable.cs:1106-1146` (`ApplyRenderOnTop` — `return;` then `#pragma warning disable CS0162` block; `RestoreRenderers` :1149 called from `TryBeginGlide` :1249 and `Restore` :1352); `:1376` (`Transform? parent = _origParent != null ? _origParent : null;`); `:1246` (`TryBeginGlide` keeps the actor in `HeldFigures`; `HeldFigures.Remove` only in `FinishGlide` :1345 and `Restore` :1403); `:1257` (`OnGrab` → `if (_glideActive) FinishGlide(...)` before capturing `_origLocal*`); `:1305` (`HeldGlideMath.Sample(_glideStartTime, Time.unscaledTime, …)` — unscaled time); `:1449` (`AuthoritativeCellChanged` per-frame re-derivation).
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** each "Where" line of the §8 entries re-read against source; the fake-null unwrap and the CS0162 block carry their own KEEP comments (Batch D). `_heldBaseScale` named in §8 entry 3 no longer exists — it became `_heldLocalScale` + `_stretch` (the ModBuild 108 latch); the invariant's INTENT (capture at grab, never compound on live-tune) is still met by `HeldLocalScale() => _heldLocalScale * _stretch` and `ApplyHeldPose` writing that frozen product.
- **Proposed action:** none — leave alone. Doc-drift on the INVARIANTS side only: §8 "`_heldBaseScale` is captured at grab" should read "`_heldLocalScale` (the anchor-local size latch, ModBuild 108) is captured at grab; `ApplyHeldPose` writes `_heldLocalScale * _stretch`". The `.planning` file is the integrator's.
- **Guard expectation:** empty.
- **Risk if wrong:** n/a.
- **Cross-lane:** none.

### D-03 — R10 (prop vs figure walk-in highlight edges) — RESOLVED, verified at HEAD
- **File:line:** `FigureGrabbable.cs:466` (`WalkInHighlightEdges.NoteHover(hand.Side, highlighted ? this : null)`), `:561` (`WalkInHighlightEdges.Forget(this)`), `:1064` (`WalkInHighlightEdges.Tick()` from `TickHeldScale`); partner `GrabbableProp.cs` (to be confirmed below) + `WalkInHighlightEdges.cs`.
- **Class:** parallel-construction (resolved)
- **Tier:** n/a
- **Evidence:** `TickHighlightMode` no longer exists in `FigureGrabbable.cs` (grep: 0 hits); the mechanism is `WalkInHighlightEdges` typed on `IWalkInHighlightTarget`, which `FigureGrabbable` implements (:44, :580-594). The redundancy-audit's R10 row (`FigureGrabbable.cs:418/:576`) is stale by line and by mechanism.
- **Proposed action:** none in code. Record "R10 no longer true" for the audit.
- **Guard expectation:** empty.
- **Risk if wrong:** n/a.
- **Cross-lane:** none.

<!-- file read: GrabbableProp.cs 2384 lines, whole -->

### D-04 — `GrabbableProp` doc says prop holds are LOCAL-ONLY / "no wire record" while the code reads `NetHeldProps.Owns` ("record 37")
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/GrabbableProp.cs:112-115` (class doc "MULTIPLAYER. Local-only in this build … no packet is sent and none is expected"), `:1916-1920` ("The prop hold stays LOCAL-ONLY in this build"), `:2148-2156` ("NO WIRE RECORD IS NEEDED … HeldProps sends nothing, CanGrab consults no remote lock … a peer therefore never draws a held prop AT ALL"), and the LOG PROSE at `:2234-2235` ("Map-item holds are local-only, so no peer sees this size and the trim cannot be a desync"). Contradicted by `:291-299` (`CanGrab` → `NetHeldProps.Owns(_prop)`, "MP GRAB-LOCK (record 37)"), `:376` (`AllowsHand` → same), `:1975` (`Net.NetProps.CompletePendingThaw`), `NetHeldProps.cs` and `Net/NetProps.cs` (see D-05).
- **Class:** doc-drift
- **Tier:** n/a (comment fix only) — BUT see D-05 for the behavioural consequence.
- **Evidence:** four sentences assert a property (local-only) the same file's own code refutes three times. The 2026-09-06 commits added the remote grab-lock and the peer thaw hand-off.
- **Proposed action:** rewrite the four sentences: "MULTIPLAYER (since ModBuild ~47x, record 37): a held prop's pose is sent by `Net/NetProps`; a peer-held prop is grab-locked here via `NetHeldProps.Owns`. The stretch factor is [sent / NOT sent — see D-05]." The `:2234` sentence is inside a `VRLog.Note` string after the `[Size] … grab-time size CLAMP` token; the token itself must not change — only the trailing clause "Map-item holds are local-only, so no peer sees this size and the trim cannot be a desync" should be corrected, and only after D-05 is answered.
- **Guard expectation:** the `:2234` string literal change shows as `CHANGED` confined to `GrabbableProp`; the XML-doc changes are invisible to the guard.
- **Risk if wrong:** none for the doc; the log-string edit must keep the `[Size]` / `grab-time size CLAMP` tokens byte-identical.
- **Cross-lane:** none for the doc.

### D-05 — Prop STRETCH factor: is it on the wire? (1:1 ruling) — QUESTION for the integrator, pending `Net/NetProps.cs` read
- **File:line:** `GrabbableProp.cs:2148-2156` (argues no record is needed BECAUSE peers never draw a held prop); `GrabbableProp.cs:2172` (`internal float Stretch => _stretch;` — grep for readers outside this file needed); partner `Net/NetProps.cs` (outside the lane).
- **Class:** risk-gap (potential defect)
- **Tier:** 3 if real
- **Evidence:** if `Net/NetProps` now streams a held prop's pose to peers (record 37) but not `HeldSizeFactor`-style measured size, then a stretched chest is rendered at the un-stretched size on every peer — exactly the ModBuild 156 figure desync ("Die Größen der Figuren synchronisieren nicht richtig") re-created on props. The figure path fixed it by sending the MEASURED lossyScale ÷ home (`FigureGrabbable.HeldSizeFactorOf`). To be confirmed by grepping `Net/NetProps.cs` for `Stretch`/`lossyScale`/`scale` — recorded below once read.
- **Proposed action:** integrator to verify; if the prop record carries no size term, this is a NEEDED-OUTSIDE(net) design item, not a lane fix (new wire field = ModBuild bump = forbidden this round).
- **Guard expectation:** n/a.
- **Risk if wrong:** a false alarm costs one grep.
- **Cross-lane:** NEEDED-OUTSIDE: `src/GloomhavenVR/Net/NetProps.cs` — answer whether the held-prop record carries size.

### D-06 — `HeldPoseReport.EmitForProp` sits BELOW the `_grabLogsLeft` budget early-return; the figure twin is unconditional
- **File:line:** `GrabbableProp.cs:659-661` (`if (_grabLogsLeft <= 0) return; _grabLogsLeft--;`) … `:683` (`HeldPoseReport.EmitForProp(hand.Side, Label, anchor);`); partner `FigureGrabbable.cs:684` (`HeldPoseReport.EmitForFigure(...)` — no budget in front of it).
- **Class:** risk-gap (instrument asymmetry)
- **Tier:** 3 (one-line move; no design decision) — confirm against `HeldPoseReport.cs`'s own budget first (read below).
- **Evidence:** the comment at `:686-689` says "Same tokens, same tier, one emitter, both call sites" — but the prop call site is reached only for the first 4 prop grabs per scenario (`LogBudget = 4`), the figure call site for every figure grab. After the 4th prop grab in a scenario the handedness line is emitted for figures only. If `HeldPoseReport` carries its own per-session budget the prop side is double-gated and the doc claim "both call sites" is false in the steady state.
- **Proposed action:** move `HeldPoseReport.EmitForProp(...)` above the `if (_grabLogsLeft <= 0) return;` (i.e. directly after `PropAnimBelt.Engage`), so both call sites are gated by `HeldPoseReport`'s own rule only. Zero tuning change, zero token change.
- **Guard expectation:** `CHANGED` confined to `GrabbableProp` (statement reorder in `OnGrab`).
- **Risk if wrong:** at most a few extra `Note` lines per scenario.
- **Cross-lane:** none.

### D-07 — `GrabbableProp` class doc quotes a rotation expression that is not the code
- **File:line:** `GrabbableProp.cs:43-49` ("`_uprightBase * (HeldUpright ? HeldUprightRotation(side) : HeldPalmRotation())`") vs `:765` (`_uprightBase * PropHeldPose.HeldRotationFor(side, PropHeldPose.HeldUpright)`).
- **Class:** doc-drift
- **Tier:** n/a
- **Evidence:** `HeldPalmRotation` / `HeldUprightRotation` no longer appear in this file (the 2026-09-05 `HeldPoseMirror.Rotation` merge, see `:734-737`).
- **Proposed action:** replace the quoted expression with "`_uprightBase * PropHeldPose.HeldRotationFor(side, PropHeldPose.HeldUpright)` (= `HeldPoseMirror.Rotation`, the same function the figure uses)".
- **Guard expectation:** empty.
- **Risk if wrong:** none.
- **Cross-lane:** none.

### D-08 — Instrument-write census for `GrabbableProp` (loadbearing.py rows)
- **File:line:** `GrabbableProp.cs:1081-1104` (`LogCardRoute` writes `_cardRouteLogsLeft`, `CardRouteKinds`); `:1686-1759` (`EmitWatch` nulls `_watch*`, decrements `_watchesLeft`); `:1794-1845` (`TickProbe` writes `_probeFrame`, `_probesLeft`); `:508-549` (`OnGrabHighlight` writes `_highlightLogsLeft`, `HighlightKinds`).
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** every field written inside these bodies is read ONLY by the same instrument, its arming twin (`BeginWatch` :1548 reads `_watchesLeft`; that arming is itself diagnostic-only) or `ResetLogBudgets`. No mechanism (pose, glide, layers, freeze, info card) reads any of them. `EmitWatch` is called from `TryBeginGlide`/`Restore`/`TickHeld` but nulling `_watch*` has no mechanical effect. NOT load-bearing; the loadbearing.py row for `LogCardRoute` is an upper-bound false positive.
- **Proposed action:** none.
- **Guard expectation:** n/a.
- **Risk if wrong:** n/a.
- **Cross-lane:** none.

- **D-05 RESOLVED (no defect):** `Net/NetProps.cs:251 SampleHeldStretch` sends the MEASURED `lossyScale.x / home` (record 37, field 4), the same rule as `FigureGrabbable.HeldSizeFactorOf`; the receiver applies `HomeLocalScale * StretchApplied` (:486). 1:1 holds for prop size. D-05 is downgraded to doc-drift and folded into D-04: the "local-only / sends nothing" sentences are stale in `GrabbableProp.cs:112-115, :1916-1920, :2148-2156, :2234`, `PropGrab.cs:51-53` ("MULTIPLAYER. Local-only and additive: nothing is sent"), `StretchTarget.cs:49` ("a PROP hold sends nothing at all and no peer…"). Correct sentence for all: "MULTIPLAYER: since record 37 (`Net/NetProps`) a held prop's pose AND its measured size stream to peers; `NetHeldProps.Owns` grab-locks a peer-held prop here."
- **D-06 CONFIRMED:** `HeldPoseReport.cs:55/:58` carries its own budgets (`_propLogsLeft = 8`, `_figureLogsLeft = 8`, per SESSION — no reset found by grep). The prop call site is therefore double-gated: `GrabbableProp.LogBudget = 4` per scenario in front of `HeldPoseReport`'s 8 per session. After the 4th prop grab of a scenario the prop handedness line is silent while the figure line still prints (up to 8). The move proposed in D-06 stands.

<!-- file read: PropGrab.cs 841 lines, whole -->

### D-09 — `PropGrab.cs` has two ORPHANED `<summary>` blocks (a doc for a deleted constant, and a doc attached to the wrong member)
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/PropGrab.cs:62-85` (a `<summary>` for the deleted `SettleScanBudget` constant, followed at `:87` by a second `<summary>` — so `IdleSweepScans` carries two); `:166-176` (a `<summary>` describing "The collider this prop actually REGISTERED against … `PickColliderOf`", followed at `:177` by the `<summary>` of `NearestInReach`; the member it describes, `PickColliderOf`, sits undocumented at `:198`). `:339` and `:355` refer the reader to "`SettleScanBudget`'s note", a symbol that no longer exists.
- **Class:** doc-drift
- **Tier:** n/a (comment motion only)
- **Evidence:** `grep -n SettleScanBudget PropGrab.cs` → only the two prose references; no declaration. The compiler does not warn on a doubled `<summary>`, so `GenerateDocumentationFile` is blind to this.
- **Proposed action:** (a) turn `:62-85` into a `//` block headed "THE SETTLE BUDGET (`SettleScanBudget`) IS GONE (ModBuild 367)…" so it no longer looks like the doc of `IdleSweepScans`; change `:339`/`:355` to "see the ModBuild 367 note above `IdleSweepScans`". (b) move `:166-176` down to directly above `internal static Collider? PickColliderOf` at `:198`.
- **Guard expectation:** empty (XML docs are not in the snapshot).
- **Risk if wrong:** none.
- **Cross-lane:** none.

### D-10 — `PropGrab.Scan` (:293-574) — leave alone (structure)
- **File:line:** `PropGrab.cs:293-574`
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** ~280 source lines but ~110 code lines; the four phases are labelled, the ledger was already extracted (`ReportLiftLedger`), and the one long expression is a single `VRLog.Note` string (an HW-VERIFY line). A split would move log-string fragments, not logic.
- **Proposed action:** none.
- **Guard expectation:** n/a.
- **Risk if wrong:** n/a.
- **Cross-lane:** none.

<!-- files read: HeldProps.cs 305, HeldFigures.cs 171, NetHeldFigures.cs 78, NetHeldProps.cs 121, HeldPoseMirror.cs 262, PropHeldPose.cs 343, HeldPoseReport.cs 212 — whole -->

- D-04 family, two more sites: `HeldPoseMirror.cs:200-202` ("held map items are local-only in this build and put nothing on the wire at all") and `PropHeldPose.cs:61-67` + `:168-172` ("Prop holds are local-only in this build BY DESIGN … nothing about a held prop goes on the wire … When the prop hold does get a wire record, these keys follow the standing ruling"). Both stale since record 37 (`HeldProps.cs:42-51` is the up-to-date account and can be cited). The PropHeldPose sentence should now read: "MULTIPLAYER: a held prop's WORLD pose goes on the wire (record 37), so the OWNER's PropHeld* dials drive what every viewer sees; a viewer's own copy of these keys is never read for a remote hold."

### D-11 — `PropHeldPose.Alike` doc says the MIRROR "is what ships"; the key ships TRUE (same-in-both-hands)
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/PropHeldPose.cs:280-284` ("Hold the item identically in both hands (both take the MIRRORED form), or mirror it between them, which is what ships.") vs `:29-31` ("SameInBothHands … was flipped to TRUE"), `:239` (Bind description: "ON (the default)"), `Defaults.PropHeldSameInBothHands` (to be confirmed by grep).
- **Class:** doc-drift
- **Tier:** n/a
- **Evidence:** three statements in the same file; the accessor doc is the odd one out and was evidently written before the flip.
- **Proposed action:** change the accessor doc to "…or mirror it between them (OFF — how ModBuild 349-434 held it). Ships ON."
- **Guard expectation:** empty.
- **Risk if wrong:** none.
- **Cross-lane:** none.

### D-12 — Pair verdicts (user's standing complaint) — part 1: held registries and pose
- **File:line:** `HeldProps.cs` ↔ `HeldFigures.cs`; `NetHeldProps.cs` ↔ `NetHeldFigures.cs`; `PropHeldPose.cs` ↔ `FigureGrabConfig.HeldOffsetFor/HeldRotationFor`; `HeldPoseMirror.cs` (shared); `HeldPoseReport.cs` (shared).
- **Class:** parallel-construction (audit)
- **Tier:** n/a
- **Evidence / verdicts:**
  - `HeldFigures` vs `NetHeldFigures` — **deliberately separate** (`HeldFigures.cs:337-344` "DO NOT MERGE … Sharing one store would couple local grab lifetime to the wire"). Redundancy-audit §4.14 still true at HEAD.
  - `HeldProps` vs `HeldFigures` — **shared shape, deliberately separate.** Both are ordered parallel lists in grab order with reference-identity `IndexOf`, `TryGetSlot`, order-preserving `Add`, `Remove`, `Clear` (~45 lines each of identical logic). They differ by key type (`CObjectProp` vs `ActorBehaviour`), by payload (`Visuals`+`HomeWorldScales` vs `_current`+`PinAnimatedRoots`) and by consumer (`NetProps` vs the Harmony patch gate). A generic `OrderedHeldSlots<TKey,TPayload>` would save ~40 lines and add a generic instantiation to two hot `Owns` paths (`HeldProps.OwnsRendererOf` is asked ~3000×/rescan). NOT worth it; the reason is written at `HeldProps.cs:12-25`.
  - `NetHeldProps` vs `NetHeldFigures` — **deliberately separate**: id-keyed (FNV of `PropGuid`, survives a state re-key) vs reference-keyed; the reason is at `NetHeldProps.cs:571-581`.
  - `PropHeldPose` vs `FigureGrabConfig` held pose — **deliberately separate dials, SHARED mechanism** (`HeldPoseMirror` is the one copy of the mirror, upright base, rotation composition; both `*.HeldOffsetFor/HeldRotationFor` are one-liners onto it). Redundancy-audit §4.15 still true; the 2026-09-05 merge closed the duplication the audit had open.
  - Handedness line — **shared** (`HeldPoseReport`, one emitter, two call sites) — but see D-06 for the prop-side double gate.
- **Proposed action:** none beyond D-04/D-06/D-11.
- **Guard expectation:** n/a.
- **Risk if wrong:** n/a.
- **Cross-lane:** none.

<!-- files read: FigureGrabConfig.cs 778, FigureBusy.cs 576, FigureStallWatchdog.cs 162, WalkInHighlightEdges.cs 143, PropReach.cs 881, PropLift.cs 270, FigureHighlight.cs 801 — whole -->

### D-13 — `FigureStallWatchdog` FIRED line is `VRLog.Warn` — silent at the shipped log level, while its own doc says the line is how "a hardware log tells us the prevention leaked"
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/FigureStallWatchdog.cs:151-159` (`VRLog.Warn("FigureGrab", "TURN STALL WATCHDOG FIRED (repair #…) … THIS LINE MEANS THE PREVENTION IN FigureBusy LEAKED — treat it as a defect report")`); the class doc `:60-61` ("It logs at WARN with the full picture whenever it fires, so a hardware log tells us the prevention leaked instead of quietly papering over it"); secondary `:145` (the `FinalizeAttackFlow threw` line, also `Warn`).
- **Class:** risk-gap
- **Tier:** 3 (one-word promotion; no design decision — the doc already states the intent)
- **Evidence:** `VRLog.Warn` gates on `Level >= Debug` (`Core/VRLog.cs:155`); shipped `[General] LogLevel = Info`. So on the user's rig the watchdog repairs the hang and prints NOTHING: exactly the "quietly papering over it" the doc says it must not do. This is the same shape as redundancy-audit §6.2 (`ViewConeProbe`) and §6.3 (`EscMenuShowSafety`), which the audit already judged should be `Alert`. The player-facing consequence (a session-killing deadlock recovered by a backstop) is the documented definition of `Alert` ("the short list a player can act on").
- **Proposed action:** `:151` `VRLog.Warn(` → `VRLog.Alert(`; `:145` likewise. Token `TURN STALL WATCHDOG FIRED` unchanged (it is in `.planning/refactor/.guard/surface.json`; a tier change keeps the string). Fix the doc sentence at `:60` to "It logs at ALERT (visible at the shipped Info level)…".
- **Guard expectation:** `CHANGED` confined to `FigureStallWatchdog` (one call target).
- **Risk if wrong:** one extra warning line per genuine repair — which is the point.
- **Cross-lane:** none.

### D-14 — `FigureBusy.OwnAnimationPlaying` re-runs `GetComponentsInChildren<Animator>()` on EVERY call for an actor that has no controller-bearing Animator (the cache cannot hold a negative)
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/FigureBusy.cs:439-445` (`if (!AnimatorCache.TryGetValue(id, out entry) || entry.animator == null) { entry = ResolveAnimator(root); AnimatorCache[id] = entry; }`), `:464-480` (`ResolveAnimator` returns `(null!, false)` when no Animator with a controller exists — and that is what gets cached, so `entry.animator == null` is true on the very next call).
- **Class:** risk-gap (per-frame allocation on a hot path — the `findobjectsoftype-is-the-default-suspect` class)
- **Tier:** 3 — needs a small design decision (see below), so a finding, not a commit.
- **Evidence:** state: an adopted actor whose root subtree holds no `Animator` with a `runtimeAnimatorController` (the health-prop `PropDummyObject` actors that `ActorPropBody` exists for — `FigureHighlight`'s `RootFallback` verdict describes exactly "a CObjectActor whose visible geometry is an AttachedProp outside its own root"; or any figure whose Animator is momentarily absent). → every `IsBusy`/`HoldMustEnd` call (`FigureGrabbable.CanGrab` :358 and `AllowsHand` :401/:403 — per hand per frame from the `ProximityGrabber` for in-reach candidates; `FigureGrabDriver.cs:1104` per HELD figure per frame; `:1327` per adopted candidate) reaches `OwnAnimationPlaying` (clause 4 is the last clause, so it runs whenever clauses 1-3 are false, i.e. the normal case) → `ResolveAnimator` → `root.GetComponentsInChildren<Animator>()` allocates an array per call. For a held health prop that is at least two allocations per frame for the whole hold. The audit line `MINIATURE AUDIT … 0 Animator(s) with a controller under the actor` is the cheap falsifier on a hardware log.
- **Proposed action:** cache the negative: make the tuple `(Animator? animator, bool hasIdleVocabulary, bool resolved)` and re-resolve only when `!resolved || (entry.animator == null && entry.hadAnimator)` — i.e. re-probe when a PREVIOUSLY FOUND Animator died, never when none was found. Design point for the integrator: an Animator that streams in AFTER first adoption would then stay "no animator → idle (fail open)" until `ClearCache` — the same fail-open the doc already accepts for controllers without the idle vocabulary, and clauses 1-3 still cover game dependency. Alternative with zero policy change: re-probe negatives on a cadence (e.g. once per second per actor).
- **Guard expectation:** `CHANGED` confined to `FigureBusy`.
- **Risk if wrong:** a late-arriving Animator is not consulted by clause 4 until the next scenario — the grab-flash case the doc describes, on health props only.
- **Cross-lane:** none.

### D-15 — `FigureGrabConfig.Active*` pre-Bind fallbacks disagree with `Defaults/` (the `a-clamp-fallback-is-not-a-default` class, uncommented)
- **File:line:** `FigureGrabConfig.cs:369-374` — `ActiveHeldSide → 0f` (Defaults 0.03), `ActiveHeldUp → 0.03f` (Defaults 0.01), `ActiveHeldForward → 0.03f` (Defaults 0.05), `ActiveHeldTilt → 0f` (Defaults 17), `ActiveHeldFaceYaw → 0f` (Defaults −133), `ActiveHeldRoll → 0f`.
- **Class:** risk-gap (latent; reachable only before `Bind()`), doc-drift by omission
- **Tier:** 2 (replace six literals with the `Defaults.*` constants they are supposed to be — not a tuning change: the shipped value is the Defaults one, the literal is a pre-Bind seed that predates `Defaults/`)
- **Evidence:** `PropHeldPose.cs:256-259` documents the rule this file breaks ("falls back to the SHIPPED default rather than to a literal invented here: a fallback that disagrees with Defaults/ is a second shipped value nobody can find"). `redundancy-audit §4.16` accepts the WallSegmentFade case ONLY because it is commented; these six carry no comment. `StyleOr` is reached pre-Bind by nothing today (Bind runs at module start), so the observable effect is nil — it is the next reader who is at risk.
- **Proposed action:** `StyleOr(StyleHeldOffsetSide, HeldOffsetSide, Defaults.HeldOffsetSide)` etc., `Defaults.FigureGrab_HeldTiltDegrees`, `Defaults.HeldFaceYawDegrees`, and for roll `Defaults.HeldRollDegrees_ByStyle[…]`-consistent `0f` (roll ships 0 per style; keep `0f` and say so). Or, minimal: add the "PRE-BIND fallback, NOT the shipped default" comment the WallSegmentFade site carries.
- **Guard expectation:** `CHANGED` confined to `FigureGrabConfig` (constant operands).
- **Risk if wrong:** none observable (pre-Bind only).
- **Cross-lane:** none (reads `Defaults`, does not edit it).

### D-16 — `FigureGrabConfig.StretchLimits` doc: "only the gesture factor rides the wire (record 30)" is stale since ModBuild 157
- **File:line:** `FigureGrabConfig.cs:204-208` ("MULTIPLAYER: the bounds are a LOCAL presentation choice — only the gesture factor rides the wire (record 30) and `NetProtocol.EncodeHeldStretch` clamps the outgoing value…").
- **Class:** doc-drift
- **Tier:** n/a
- **Evidence:** `FigureGrabbable.HeldSizeFactorOf` (:1009-1020 region) is the send-side sample and is `lossyScale ÷ homeWorldScale` — the MEASURED size, which INCLUDES the grab-time clamp and the zoom; the `_latchTotalRatio` note says explicitly that sending `_stretch` alone was the 2026-08-15 desync. The encoder-clamp sentence is still right.
- **Proposed action:** "MULTIPLAYER: the bounds are a LOCAL presentation choice — what rides the wire (record 30) is the MEASURED held size (`FigureGrabbable.HeldSizeFactorOf` = lossyScale ÷ board-home size, so the clamp is already inside it), and `NetProtocol.EncodeHeldStretch` clamps…".
- **Guard expectation:** empty.
- **Risk if wrong:** none.
- **Cross-lane:** none.

### D-17 — Orphaned/doubled `<summary>` blocks and small doc-drift (roll-up)
- **File:line:** `FigureHighlight.cs:143-153` (two `<summary>` on `ModOwnedPrefix`; the first describes a constant that moved to `VRLayers.ModOwnedNamePrefix`); `PropReach.cs:312-314` (`own` param doc says "as PropGrab.Scan already resolved it (`GetComponentInChildren<Collider>()`)" — it is `PropReach.OwnPickShape` since ModBuild 445, as `:455` says); `PropReach.cs:24-29`/`:44-51` are explicitly historical and fine.
- **Class:** doc-drift
- **Tier:** n/a
- **Proposed action:** merge the two `ModOwnedPrefix` summaries into one (keep the "why" prose as `<para>`); reword the `own` param doc.
- **Guard expectation:** empty.
- **Risk if wrong:** none.
- **Cross-lane:** none.

### D-18 — Pair verdicts part 2: reach, lift, highlight, walk-in
- **Class:** parallel-construction (audit) — **Tier:** n/a
- `PropReach`/`PropFootprint` + `GrabbableProp.AllowsHand` (per-prop election) vs `FigureGrabDriver.SelectByOffsetAnchor` + `FigureReachVolume` (central election) — **deliberately separate** (redundancy-audit §4.13, quoted verbatim at `WalkInHighlightEdges.cs:11-15` and `GrabbableProp.cs:348-360`). Shared: the dial (`PickRadiusRealMeters`), the exit hysteresis constant (`FigureGrabDriver.PickExitFactor`), the pick-shape predicate (`VRInteractables.IsUsablePickShape`), the pinch point (`GrabAnchor.TransformPoint(HeldOffsetFor)`). Still parallel: the Schmitt trigger itself (`_inReach[side]` in `GrabbableProp.AllowsHand` :401-408 vs the driver's per-figure latch) — 6 lines; not worth a helper.
- `PropLift.MayBeLifted` — **no figure twin** (figures are gated by `FigureBusy`; a prop's liftability is static scenario data). Correctly separate.
- Highlight — **shared** (`FigureHighlight.Apply` takes a plain root; props pass nulls). Walk-in edges — **shared** (`WalkInHighlightEdges`, D-03).
- `FigureBusy` / `FigureStallWatchdog` — figure-only by nature (a prop has no turn machine dependency); no prop twin needed.
- **Proposed action:** none.

<!-- files read: FigureOverlay.cs 1548, FigureGhosts.cs 208, PropGhosts.cs 148, FigureRingSuppressor.cs 112 — whole -->

### D-19 — `PropGhosts.NotifyHeld` is SILENT when the ghost cannot be built; `FigureGhosts.NotifyHeld` prints an HW-VERIFY `NO ghost for …` line
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/PropGhosts.cs:68-72` (`if (ghost == null) { Object.Destroy(mat); return; }` — no log) vs `FigureGhosts.cs:120-127` (`VRLog.Note("FigureGrab", $"NO ghost for {who} — {report}")`, marked HW-VERIFY). Also `PropGhosts.cs:62-63` (missing shader → silent) vs `FigureGhosts.cs:109-110` (same, also silent — parity there).
- **Class:** risk-gap (instrument asymmetry on a user-parity feature: "Es soll wie Figuren reagieren … einen Geist hinterlassen")
- **Tier:** 3 (additive log line; no design decision)
- **Evidence:** `BuildFrozenGhost` returns null with a `report` ("NOTHING LEFT TO TINT — …") that names the cause; on the prop path that string is computed and thrown away. A prop whose ghost silently fails (every renderer VFX/non-surface, or an inactive subtree) leaves no line to grep, while the same failure on a figure does. The user's "ich sehe zwar einen Geist" report class would be undiagnosable for props.
- **Proposed action:** in `PropGhosts.NotifyHeld` before `return`: `VRLog.Note("FigureGrab", $"[Props] NO ghost for '{prop.InstanceName}' ({prop.ObjectType}) — {ghostReport}");` with the same HW-VERIFY comment the figure line carries.
- **Guard expectation:** `CHANGED` confined to `PropGhosts`.
- **Risk if wrong:** one extra Note line per failed prop ghost.
- **Cross-lane:** none.

### D-20 — `FigureOverlay.CloneRenderersSharingBones` — possible dead code (Tier 0 candidate; §5 checklist pending the grep below)
- **File:line:** `FigureOverlay.cs:100-166`; the only other mention found so far is `FigureHighlight.cs:717-720` ("This duplicates `CloneRenderersSharingBones`'s per-renderer body rather than calling it … The ghost path in FigureOverlay is untouched") and INVARIANTS §8 ("The highlight container is a SIBLING of the Animator object — Where: … `FigureOverlay.CloneRenderersSharingBones`"). The ghost path is `BuildFrozenGhost` (Instantiate + tint), NOT this method.
- **Class:** dead (candidate) / duplication
- **Tier:** 0 if unreferenced; else 2 (its body is a verbatim twin of `FigureHighlight.CloneOne` :722-771 — same skinned branch, same static-mesh branch incl. `MatchCloneWorldScale`, differing only in the container-parenting comment and `Fill`/`FillMaterials` naming).
- **Evidence:** checklist run below (grep of `src/`, `tests/`, `.planning`, `docs`; `git log -S`).
- **Proposed action:** if unreferenced: delete the method (Tier 0) and correct the two sentences that name it (`FigureHighlight.cs:717` → "This is the per-renderer clone; there is no shared copy — the ghost path Instantiates instead", INVARIANTS §8 Where line). If referenced: extract the per-renderer body into one `FigureOverlay.CloneRendererInto(r, container, mat)` and have both call it (Tier 2, bodies quoted above are identical).
- **Guard expectation:** Tier 0: the member disappears from `FigureOverlay`, nothing else.
- **Risk if wrong:** none (the highlight uses `CloneOne`).
- **Cross-lane:** none.

### D-21 — INVARIANTS §8/§14 entries naming `FigureOverlay`/`FigureGhosts`/`FigureRingSuppressor` — VERIFIED STILL TRUE (leave-alone)
- `FigureOverlay.BuildFrozenGhost` :182-431 — Animator kept with `applyRootMotion=false`, `fireEvents=false`, `AlwaysAnimate` (:218-225); `ParticleSystem.Stop+Destroy` BEFORE the renderer sweep (:244-250); `mb.enabled = false` then `Destroy(mb)` (:255-256); ring twin keeps original materials (:330-337). Order is single-method-local and stated in-source — NOT to be locked (FRAME-ORDER.lock note) and NOT to be reordered (§8 veto).
- `MakeOverlayMaterial` :68-69 — `_ZTest LEqual` + `_ZWrite 0`; null shader → callers skip (`FigureHighlight.Apply` :347-351, `FigureGhosts.NotifyHeld` :108-110, `PropGhosts.NotifyHeld` :61-63).
- `FigureGhosts.NotifyHeld` called from `FigureGrabbable.OnGrab` BEFORE the reparent (:189-190 vs :205); `Tick` reconciles on `HeldFigures.Owns || NetHeldFigures.Owns` (:166). `Ghost.Pos/Rot` read every Tick (:174) — §15 "NOT vestigial" still true.
- `FigureRingSuppressor.Restore` :103-110 — the `ActorBehaviour key = actor;` alias before the Unity `!= null` still there (§14.3).
- `OverlayMaterialOwner`/`OverlayPulse.OnDestroy` destroy their material (:1388-1395, :1408-1415).
- **Proposed action:** none.

### D-22 — Pair verdict: `PropGhosts` vs `FigureGhosts`
- **Class:** parallel-construction (audit) — **Tier:** n/a
- **Evidence:** the GHOST (builder, material, tint, depth prepass, visibility probe) is SHARED (`FigureOverlay.BuildFrozenGhost`, `FigureGhosts.GhostTint` — the prop's own copy of the four tint numbers was removed 2026-09-05). What is parallel is the ~40-line keyed registry (`Dictionary<ActorBehaviour,Ghost>` re-asserting pose each Tick vs `Dictionary<CObjectProp,GameObject>` membership only) with the reason at `PropGhosts.cs:15-27` (key type; a prop clone has no Animator to walk it off the cell). **Deliberately separate; leave.** The one asymmetry worth acting on is D-19.

- **D-20 CONFIRMED (Tier 0):** `CloneRenderersSharingBones` — §5 checklist: (1) not a Harmony target/patch method; (2) not a Unity message, not serialized; (3) no `nameof`/string/`AccessTools`/`Traverse`/`GetMethod` reference — the only hits in `src/`, `tests/`, `.planning`, `docs` are its own declaration, one `<see cref>` in its class doc (`FigureOverlay.cs:16`), one in `FigureHighlight.cs:717`, and the INVARIANTS §8 "Where" line; (4) not a config key; (5) not a log token; (6) not in the debug menu; (7) not pinned by `tests/` (the csproj links only `FigureStretchMath.cs`, `HeldPoseMirror.cs`, `FigureGlowGrade.cs` from this folder); (8) writes no field. `git log -S` shows it was introduced in `bcc92604` and never deliberately removed. Zero callers. Safe to delete together with the two doc sentences; INVARIANTS §8 "Where" should read "`FigureHighlight.Apply` / `FigureHighlight.CloneOne`".

<!-- files read: ActorPropBody.cs 1029, PropVisualLookup.cs 283, FigureStretch.cs 553, StretchCaptureWatch.cs 429, StretchTarget.cs 220 — whole -->

### D-23 — `STRETCH CAPTURE ON` (HW-VERIFY) ALWAYS prints "NO renderer survived the sanity ceiling … CENTRE distance": the caller passes placeholders for the field the line exists for
- **File:line:** `src/GloomhavenVR/Board/FigureGrab/FigureStretch.cs:363-366` (`StretchCaptureWatch.NoteCaptured(hand.Side, target.Label, surfaceReal, FigureGrabConfig.StretchReachRealMeters, float.PositiveInfinity, "n/a", …, centreFallback: false)`); `StretchCaptureWatch.cs:187` (`w.BodyRadiusMm = float.IsPositiveInfinity(bodyRadiusReal) ? -1f : …`); `:334-337` (`w.BodyRadiusMm < 0f ? "NO renderer survived the sanity ceiling, so the test fell back to the CENTRE distance" : $"'{w.BodyRenderer}' at {w.BodyRadiusMm:F0} mm implied body radius"`); `:339` (`w.CentreFallback ? ", CENTRE fallback in force" : ""` — never true).
- **Class:** defect (an instrument that lies — "an instrument shipped and lying")
- **Tier:** 3 (minimal fix, no design decision; the values already exist)
- **Evidence:** state: any capture episode (free hand enters the shell of the other hand's held object). → `CaptureDistanceReal` (:492-535) computes `widest` (the real implied body radius of the widest surviving renderer) and `any`, hands them to `target.NoteCaptureVolume(any ? widest : +Inf, ceiling)` and returns the distance only. → `TickHand` then calls `NoteCaptured` with literal `float.PositiveInfinity` and `"n/a"` for `bodyRadiusReal`/`bodyRenderer`, so `BodyRadiusMm = -1` unconditionally. → `EmitCaptureOn` prints the "NO renderer survived … CENTRE distance" branch on EVERY episode, and the class doc's own decisive field ("the radius and NAME of the widest renderer the shell was drawn around … 'name the blocker, not the number'") is never printed. The "CENTRE fallback in force" suffix is dead by construction (`centreFallback: false` literal). Observed: every `STRETCH CAPTURE ON` line in any log since ModBuild 400 claims a centre fallback; expected: the widest renderer's name and radius, and the fallback clause only when `any == false`.
- **Proposed action:** give `CaptureDistanceReal` three `out` parameters (`out float widestReal, out string widestName, out bool centreFallback`) filled from the loop it already runs (`widest`, the `r.name` at the `widest` update, `!any`), and pass them at `:363-366` in place of `float.PositiveInfinity, "n/a"` and `centreFallback: false`. No token changes; the ON line's text is unchanged, it just takes the other branch when the data says so.
- **Guard expectation:** `CHANGED` confined to `FigureStretch`.
- **Risk if wrong:** none — the line becomes true; nothing mechanical reads `StretchCaptureWatch`.
- **Cross-lane:** none.

### D-24 — R42 / redundancy-audit §6.1 (`ActorPropBody.LogCensus` cadence) — NO LONGER TRUE (fixed), verified at HEAD
- **File:line:** `ActorPropBody.cs:917-1000` — `Census.Exhausted` first (:922), the cadence gate (:925-926), the walk, then `_nextCensus = now + CensusIntervalSeconds;` at `:972` BEFORE `Census.Wants(signature, now)` at `:973`; `VRLogThrottle Census = new(CensusHeartbeatSeconds = 60f, CensusLogBudget = 12)` (:122-123) carries the change gate, the heartbeat and the budget.
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** the audit's `:904/:908/:933/:935` shape is gone; the ordering comment at `:950-971` records the fix and the R42 merge. Both audit rows for this file are closed.
- **Proposed action:** none; record "no longer true" in the audit.

### D-25 — Instrument-write census for `ActorPropBody`, `PropVisualLookup`, `StretchCaptureWatch` (loadbearing.py rows)
- `ActorPropBody.LogHoldPicture` writes `held.PictureLogged` and `_holdPictureLogsLeft` — both read ONLY by `LogHoldPicture` and reset in `Clear`; the mechanism (`Hold`/`Release`) never reads them. NOT load-bearing. `ActorPropBody.LogCensus` writes `_nextCensus` — read only by itself and `Clear`. NOT load-bearing (it gates the instrument's own walk). `_attachedActors`/`_resolved`/`ResolvedNames` are written by `Resolve` (mechanism) and only READ by the census — the correct direction.
- `PropVisualLookup.ReportRekeyOnce` writes `_loggedRekey` — read only by itself and `Reset`. NOT load-bearing. `RekeyedThisScenario` is written by the mechanism (`Resolve`) and read by the census — correct direction.
- `StretchCaptureWatch` — every `Note*`/`Emit*` writes only its own `HandWatch` counters; `FigureStretch` never reads them (its doc says so at `:57-60`). NOT load-bearing.
- `FigureStretch.CaptureDistanceReal` writes `_boundsWarnTarget` (a once-per-target throttle for a `VRLog.Warn`) — read only by the same throttle. NOT load-bearing.
- **Proposed action:** none.

### D-26 — `StretchTarget` adapter exists because of an ownership rule that has expired; an `IStretchable` interface on both grabbables would remove the four re-pointed adapters and the `Owner` identity dance
- **File:line:** `StretchTarget.cs:16-24` ("declaring the interface on it is an edit to a file this lane does not own … The interface can replace this later without touching the gesture; see `.planning/LANE-PROPS-357-NEEDED-OUTSIDE.md`"), `:33-39` (allocation-free re-pointing and why `Owner` exists), `:131-219` (two forwarding subclasses, 90 lines). Both grabbables already expose the ten members with the exact signatures (`FigureGrabbable.cs:1000-1160` region, `GrabbableProp.cs:2169-2303`).
- **Class:** structure-naming (parallel construction of a seam)
- **Tier:** 2 (the two adapter bodies are pure forwarders; the interface makes them disappear)
- **Evidence:** all three files are in one folder of one lane now; the stated reason is gone — the same "stated reason expired" shape `FigureGhosts.cs:33-35` records for the tint. Cost today: four static adapters, `Bind`, `ClearAll`, `Owner`, and `TickActive`'s `ReferenceEquals(target.Owner, st.TargetOwner)` (:412) — all of which exist only because adapters are not identities.
- **Proposed action:** `internal interface IStretchable { … ten members … }` implemented by `FigureGrabbable` and `GrabbableProp`; `StretchTarget.HeldBy(side)` becomes `(IStretchable?)FigureGrabbable.HeldBy(side) ?? GrabbableProp.HeldBy(side)`; `HandState.Target` typed `IStretchable?`, `TargetOwner` and `Owner` deleted (the reference IS the identity), `ClearAll`/`Bind` deleted. Optional; the adapters are correct. If done, keep the "FIGURES BEFORE PROPS" order note.
- **Guard expectation:** `CHANGED` confined to `FigureStretch`, `StretchTarget` (disappears), `FigureGrabbable`, `GrabbableProp` (interface declaration only).
- **Risk if wrong:** a re-grab inside one gesture must still end the gesture — with a real reference as identity that comparison is `ReferenceEquals(target, st.Target)`, which is what `Owner` was emulating.
- **Cross-lane:** none.

### D-27 — `FigureStretch.cs:147-151` MULTIPLAYER doc: "`NetFigures.EaseSlot` … already reconstructs boardSize × zoom ratio from data it has" — stale since ModBuild 157 (D-16 family)
- **Class:** doc-drift — **Tier:** n/a
- **Evidence:** `FigureGrabbable._heldLocalScale` note (:150-158 region): "Builds through 156 rebuilt it receive-side as homeLocalScale × (senderRigScaleNow / senderRigScaleAtHoldStart) × stretch … peers rendered a deep-zoom grab up to 3.33× too large" — the reconstruction was REMOVED; the wire now carries `HeldSizeFactorOf` (measured). `StretchTarget.cs:46-51` repeats the old figure sentence and adds the stale prop sentence (D-04).
- **Proposed action:** "MULTIPLAYER: the MEASURED held size (`FigureGrabbable.HeldSizeFactorOf`, lossyScale ÷ board-home) rides record 30 for figures and record 37 field 4 for map items; the peer multiplies it into its own copy's board-home scale (`NetFigures.EaseSlot` / `NetProps`)."

<!-- files read: FigureStretchMath.cs 177, HeldGlideMath.cs 66, HeldPropCard.cs 218, FigureGlowGrade.cs 116, FigureClothHands.cs 334, ActorBehaviour_HeldTransform_Patch.cs 70, FigureCloth.cs 1-950 — whole/partial -->

### D-28 — loadbearing.py row "`EnemyInfoPhaseSkip.LogOnce._logged` read from `FigureClothHands.cs:268`" is a NAME COLLISION, not a cross-class read
- **File:line:** `FigureClothHands.cs:161` (`private static bool _logged;` — this class's own once-per-session latch), `:268-270` (read + set inside `Attach`, guarding one `VRLog.Info`).
- **Class:** leave-alone (instrument census false positive)
- **Tier:** n/a
- **Evidence:** `FigureClothHands._logged` is a private static of THIS class; `EnemyInfoPhaseSkip` (another lane) has an unrelated field with the same name. The census keys on the bare identifier. The field is written only by the log branch in `Attach` and read only there. NOT load-bearing; the mechanism (`Attach`'s return value, `_attachedRoot`) does not depend on it.
- **Proposed action:** none in code. The integrator may want the census to key on `Type::field` (it already prints that form in the baseline).

### D-29 — INVARIANTS §8 entries naming the Harmony patch and the pins — VERIFIED STILL TRUE (leave-alone)
- `ActorBehaviour_HeldTransform_Patch.cs:40-48` — whole-method prefix on `Update` and `LateUpdate`, gate `!HeldFigures.Owns && !NetHeldFigures.Owns`; `FixedUpdate` NOT patched; `SetHilighted` prefix routes to `FigureRingSuppressor.RecordGameIntent` for held actors (:58-69). `HeldFigures.PinAnimatedRoots` / `NetHeldFigures.PinAnimatedRoots` zero `m_AnimatedGameObject.localPosition` (`HeldFigures.cs:445-456`, `NetHeldFigures.cs:530-540`); both are called from `FigureGrabDriver.LateUpdate` (FRAME-ORDER.lock row `FigureGrabDriver.LateUpdate`, marker present at `FigureGrabDriver.cs` — confirmed by `check-frame-order` being green at HEAD per the last commit messages).
- `HeldFigures.cs:465-476` / `HeldProps.cs:292-304` — reference-identity `IndexOf` (the fake-null discipline).
- **Proposed action:** none.

### D-30 — Small Tier-0 note: `StretchCaptureWatch.HandState_Captured_Note` (unused private const string)
- **File:line:** `StretchCaptureWatch.cs:67-70`.
- **Class:** dead (documented on purpose) — **Tier:** 0, but **leave**: its own doc says it exists so the class doc can point at a private member without widening visibility — the same shape as `HandRig.PalmNormal` (INVARIANTS §15 "documented contract statements that cost one line"). Not reached by anything; costs one line. Not worth a commit.

<!-- files read: FigureCloth.cs 950-1910 (whole now), PropAnimBelt.cs 3625 — whole -->

### D-31 — `PropAnimBelt` live-vs-spent inventory (part 1: `PropAnimBelt.cs`) and per-frame cost
- **File:line:** `PropAnimBelt.cs` — mechanism: `Engage` :333, `Tick` :355 (rescan every `RescanFrames = 45`), `Release` :387, `Restore` :1056; instruments: `Announce` :2082 (`HELD-PROP ANIMATION HUSH`, budget 4/session), `AnnounceRestore` :2419 (`HELD-PROP HUSH RESTORE`, 3/session), verdict window `ArmVerdict` :2173 → `SampleVerdict` :2286 (360 frames, `VerdictBudget = 3`/session, one per prop kind) → `EmitHomeTwin` :2457 (`HELD-PROP HOME TWIN`), `TickBoard` :3224 → `EmitBoard` :3321 (`BOARD PROP STANDING WATCH`, 20 s, 2/session), plus the RenderPass and Photometer partials (D-33/D-34).
- **Class:** leave-alone (instrument inventory) with three doc-drift items
- **Tier:** n/a
- **Evidence — what is LIVE at HEAD and what it costs:**
  - Steady state (nothing held, no window armed): `Tick` = a `Count` compare; `TickBoard` = one bool; `TickPhotometer` (see D-34). Zero allocation.
  - During a hold: `Apply` re-walk every 45 frames — 7 `GetComponentsInChildren` (three of them the allocating array overload in `TakeEmitters<T>` :626) + `GetBehaviours` per animator (:648, allocates). Documented and bounded (≤2 props).
  - During a verdict window (≤3 per session, 360 frames each): per frame `VTable.Sample` (up to 16 mats × 96 slots of `Material.Get*` :1007-1049), `SampleTwin` (the same again on the twin + two `GetComponentsInChildren` walks of the twin subtree :2751-2769 + a light-probe read every 5 frames), `SampleMaterialAttachment` (mats × renderers `GetSharedMaterials`), `SampleBlocks` (`HasPropertyBlock` per renderer), `SampleRenderPassUpdate`. Heavy (thousands of native calls/frame) but bounded to ~1080 frames per session and allocation-free. Arm-time: ONE `FindObjectsOfType<Renderer>` (:1362) and two typed `FindObjectsOfType` for Chronos (:3454-3457) — 3× per session at most. Acceptable; not a steady-state cost.
  - During a standing watch (≤2 per session, 20 s each): two `GetComponentsInChildren` walks of ONE prop subtree per frame (:3296-3308). Bounded.
  - Every per-frame instrument here is gated by a per-session budget that is re-armed only by `Reset` (new scenario), so the worst case is per scenario, not per session — still bounded.
- **SPENT but still coded — strand 5 (`ObjectOcclusionVolume`):** the suppression is OFF (`CountEmitters` only, :583) and the occlusion channel was EXCLUDED BY EXPERIMENT in `a9c6fc8a` ("Do not re-chase it"). What remains is a cheap count (keep — it is a population fact) plus ~60 lines of prose that describe the ModBuild 459 null-perturbation AS THE CURRENT BUILD: `:565-582` (comment), and inside two HW-VERIFY log strings — `Announce` :2137-2150 ("*** STRAND 5 OFF - NULL PERTURBATION *** … IF THE WHITE IS GONE FOR THE USER ON THIS BUILD, THAT STRAND WAS THE PAINTER AND ELEVEN ROUNDS END; IF IT IS UNCHANGED, THE STRAND IS … DELETED OUTRIGHT") and `AppendAsymmetries` :3074-3083 ("THE ONE THAT MATTERS THIS BUILD … THAT ZERO IS HOW A READER CONFIRMS THE EXPERIMENT RAN"). Both sentences are now false about the build a reader is holding.
- **Proposed action:** (a) leave every instrument in place — none is per-frame in the steady state and each token is HW-VERIFY. (b) Doc-drift fix ONLY, no token change: rewrite the two log-string clauses to past tense ("strand 5 was switched off in ModBuild 459; the occlusion channel was excluded by experiment in ModBuild 467 — the count stays as the population fact"), and the `:565-582` comment likewise. Tokens `HELD-PROP ANIMATION HUSH`, `HELD-PROP HOME TWIN` etc. untouched. (c) Class doc `:65-70` "THE INSTRUMENT HALF IS TWO LINES" → six lines (HUSH, HUSH RESTORE, HOME TWIN, BOARD PROP STANDING WATCH, RENDER-PASS PROBE, FLASH PHOTOMETER); `:72` "Nine rounds" → "twenty-one rounds".
- **Guard expectation:** `CHANGED` confined to `PropAnimBelt` (string literals).
- **Risk if wrong:** a reader of the next hardware log still believes an experiment is running that ended two builds ago.
- **Cross-lane:** none.

### D-32 — Orphaned `<summary>` blocks and empty section headers left by the 6082b83b cleanup (roll-up, `PropAnimBelt.cs`)
- **File:line:** `:238-244` (a `<summary>` "THE PRE-COUNT FOR STRAND 6 … (material, property) slots … READ … CHANGED" for fields that no longer exist, immediately followed by the `<summary>` of `RewindStateRead`); `:258-262` ("The properties the rewind moved, named with their before→after VALUES…" — orphan, precedes `RestoreForeignEmitters`); `:586-595` (two `<summary>` before `CountEmitters` — the first describes `TakeEmitters`, which sits undocumented at `:621`); `:1450-1453` (two `<summary>` before `SampleMaterialAttachment` — the first, "One frame of the comparison…", belongs to `SampleTwin` at `:1559`, which is undocumented); `:1184` ("// ---- the census ----" header with nothing under it), `:1216-1217` ("ROUND EIGHT: the roster, the per-frame property block, and the timeline" header, empty), `:2036-2037` ("the restore, and its own falsifier" header, empty), `:2593-2595` (three blank lines).
- **Class:** doc-drift — **Tier:** n/a
- **Proposed action:** delete the two orphan field summaries (their members were deleted on purpose in 6082b83b), move the two misplaced method summaries to their members, delete the three empty headers.
- **Guard expectation:** empty.
- **Risk if wrong:** none.

### D-33 — `FigureCloth` — leave alone; instrument census
- **File:line:** `FigureCloth.cs` (1910 lines). Instruments: `LogGesture` :1635 (`CLOTH GESTURE`, `VRLog.Info`, 24/session), `LogOnce` :1719 (`FIGURE SCALE`, `VRLog.Info`, per figure, cap 6), `DescribeConfig` :1845 (`CLOTH CONFIG`, `VRLog.Info`).
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** `LogGesture` writes `_gestureLines` (read only by itself); `LogOnce` writes `_censused`/`_loggedEmpty` (read only by itself). The mechanism fields it READS (`GestureFrames`, `MinWeightReached`, `StretchWrites`, `MinStretchWritten`, `FoundCloths`) are written by the mechanism (`Note`/`Suspend`/`Advance`/`ApplyStretch`) — correct direction. NOT load-bearing. All three lines are `Info` (debug tier) and their docs do not claim to be shipped-level evidence — honest. The one `Warn`-class item worth noting: none; the class prints nothing at the shipped level, and the class's own falsifiers are the `PerfMonitor` counters (`FigureGrab.ClothCooks` etc.), which print through the Perf summary. `Advance` is the single writer of `Cloth.coefficients` as its doc claims (verified: `StepCook` :940, `StepLiveCook` :1116 also write it — the doc at `:1392-1394` "The ONLY place in this file that writes to a Cloth" is stale since ModBuild 289/291; the sentence should read "the only place the RAMP writes; the two cook sequences write on their own frames"). Minor doc-drift.
- **Proposed action:** fix the one sentence at `:1392-1394`. Nothing else.

<!-- files read: PropAnimBelt.RenderPass.cs 999, PropAnimBelt.Photometer.cs 2212 — whole. FigureGrabDriver.cs skimmed by grep for cross-references only (integrator read it). -->

### D-34 — `PropAnimBelt.RenderPass` / `PropAnimBelt.Photometer` — live-vs-spent inventory (part 2), cost, and what is owed on hardware
- **File:line:** `PropAnimBelt.RenderPass.cs:394 ArmRenderPass` → `:567 OnRenderPassProbe` (Camera.onPreRender, armed for the 360-frame verdict window) → `:783 EmitRenderPass` (`HELD-PROP RENDER-PASS PROBE`, unhooks at `:462`); `PropAnimBelt.Photometer.cs:509 ArmPhotometer` (35 s wall-clock window, `PhBudget = 2` per SESSION — deliberately not re-armed by `Reset`, `:273`) → `:747 OnPhotometerPreRender` (command buffer on the head camera, capture every 3rd frame) → `:994 OnPhotometerRead` → `:1087 StorePhotometerSample` → `:1379 PhConsiderBlink` / `:1428 PhBeginBlink` (writes `Renderer.forceRenderingOff` for 6 frames, twice per window) → `:1652 EmitPhotometer` (`HELD-PROP FLASH PHOTOMETER`, 560 lines, disarms at `:684` first); `:1500 CensusPropAnimators` (static census, once per arm).
- **Class:** leave-alone (instrument inventory)
- **Tier:** n/a
- **Evidence:**
  - NOTHING in these two files is spent. The last hardware log quoted anywhere is ModBuild 470 (`25541168`'s message); the ModBuild 471 additions (`AppendPropertyTableProvenance`, `SampleBlocks`/`AppendPropertyBlocks`, `AppendPropertyCensus`, `AppendPhotometerStillness`) have not been read against a log yet. Every emitted line is HW-VERIFY; every token appears in `.planning/held-prop-flash-experiments.md`.
  - Steady-state cost: `TickPhotometer` = one bool (`:717`); the onPreRender hook exists ONLY while armed (`_rpHooked`/`_phHooked` are removed by the disarms, which run from `CloseVerdict`/`EmitPhotometer`, which run from `Release`, `ReleaseAll`, `Reset` and the window timers). Verified: no path retires a belt without `CloseVerdict` (`Tick` :366, `Release` :399, `ReleaseAll` :413).
  - Armed cost (bounded, ≤3 verdicts + ≤2 photometers per session): RenderPass — per camera pass (~9 cameras) ~16 `GetSharedMaterials` + 52 `Shader.GetGlobal*` + compare loops; `NoteCamera` does one `GetComponents<MonoBehaviour>` + reflection `GetMethod` per NEW camera name only. Photometer — every 3rd frame three `CopyTexture` + one `Blit` + four `RequestAsyncReadback`; the callback walks 1024 px × 4. `CensusPropAnimators` walks the held prop AND the twin once (`GetBehaviours`, allocating) at arm.
  - THE ONE PLAYER-VISIBLE SIDE EFFECT: the photometer's bisection turns the held prop's renderers OFF for 6 frames, twice per armed window (`PhBeginBlink`), i.e. the prop VANISHES for ~0.15 s twice in the first two prop holds of a session. This is intended and documented (`b834e5c5`: "The blink is a deliberate visible artefact of ~0.15 s, twice per armed window"), restored object-for-object with a foreign-write counter. It is an experiment the user has been told about; it must not be "fixed" by a refactor. Recorded here so nobody files it as a flicker regression.
  - `RpGlobalNames`/`RpGlobalKinds` (`:256-309`) are two parallel arrays of 52 that must stay aligned by INDEX; I counted both at 52 (aligned by count). The doc at `:311-313` says "adding a name is one edit and not three" — it is two edits (name AND kind), and a kind that mismatches its name reads a constant zero and silently blinds that slot (e.g. a texture read through `GetGlobalVector`). Minor instrument hazard; a `(string, RpKind)[]` pair array would remove it at zero behaviour cost (Tier 1, `CHANGED` confined to `PropAnimBelt`).
- **Proposed action:** none on the instruments. Optional Tier-1: fuse the two parallel global arrays into one tuple array. Do NOT split `EmitPhotometer` (560 lines): it is one HW-VERIFY string whose clause ORDER is the reading protocol (census → picture → placement → origin → noise floor → coherence → stillness → bisection); a split invites a clause reorder the guard cannot see, for a method that will be deleted whole when the defect closes.
- **Guard expectation:** empty (no change recommended).
- **Risk if wrong:** n/a.
- **Cross-lane:** none.

### D-35 — `FigureGrabConfig.Bind` (:495-777, 242 lines) — leave alone (the named starting point)
- **Class:** leave-alone — **Tier:** n/a
- **Evidence:** the method is a linear `Bind` list whose ORDER is what `dev.gloomhavenvr.figuregrab.cfg` is written in; `PropHeldPose.Bind(config)` (`:733`) is already the one split that buys something (its own section doc). The remaining body is: 11 top-level binds, 6 legacy binds, a per-style loop of 9 binds, 27 `SettingChanged += Reapply` hooks, one `HeldFigureInfo` hook. A reader is not lost — each group carries its own paragraph. A Tier-1 split into `BindLegacy`/`BindPerStyle`/`WireLiveTune` would keep cfg order only if the call order is kept, and CHARTER §2 puts the burden on the change. The one real defect in the file is D-15 (pre-Bind literal fallbacks), which is not in `Bind`.
- **Proposed action:** none.

### D-36 — `ActorPropBody.LeafOf` (:379-513) — leave alone (the named starting point)
- **Class:** leave-alone — **Tier:** n/a
- **Evidence:** two routes (animator handle first, PCG partition as fallback), each refusal named; memoised per visual with a 1 s retry on refusal (`LeafEntry.RetryAt`), so the two `GetComponentsInChildren` walks run at most once per second per unresolved door and once ever per resolved one. `Hold`/`Release` are symmetric (`SetParent(worldPositionStays:true)` in, local TRS + fake-null-unwrapped parent out, `MonitorMovement` handed back AFTER the transform — the ModBuild 400 order, documented). Nothing to move.
- **Proposed action:** none.

- **D-31 correction:** the substring `STRAND 5 OFF - NULL PERTURBATION` IS a grep token (`.planning/held-prop-flash-experiments.md:93`, `:2228` greps for it verbatim). It must stay byte-identical; only the trailing prose ("IF THE WHITE IS GONE FOR THE USER ON THIS BUILD, THAT STRAND WAS THE PAINTER AND ELEVEN ROUNDS END; IF IT IS UNCHANGED …") may be rewritten to past tense. Same for `AppendAsymmetries` — keep "ObjectOcclusionVolume registrations SUPPRESSED: 0" intact, rewrite the "THIS BUILD" / "THE EXPERIMENT RAN" sentences.

---

## Ranked index (by risk reduced)

| # | ID | Title | Class | Tier |
|---|---|---|---|---|
| 1 | D-23 | `STRETCH CAPTURE ON` always prints the CENTRE-fallback branch — caller passes `+Inf` / `"n/a"` / `false` placeholders for the field the line exists for | defect (lying instrument) | 3 |
| 2 | D-13 | `FigureStallWatchdog` FIRED line is `VRLog.Warn` (silent at shipped level) while its doc says it is how a log shows the prevention leaked | risk-gap | 3 (one word) |
| 3 | D-14 | `FigureBusy.OwnAnimationPlaying` re-runs `GetComponentsInChildren<Animator>` every call for an actor with no controller-bearing Animator (negative never cached) | risk-gap (per-frame alloc) | 3 (needs a decision) |
| 4 | D-06 | Prop `HeldPoseReport.EmitForProp` is double-gated behind `_grabLogsLeft` (4/scenario) — figure twin is not; "both call sites" claim false after the 4th prop grab | risk-gap (instrument asymmetry) | 3 (one-line move) |
| 5 | D-19 | `PropGhosts.NotifyHeld` is silent when the ghost cannot be built; `FigureGhosts` prints the HW-VERIFY `NO ghost for` line | risk-gap (instrument asymmetry) | 3 (additive line) |
| 6 | D-15 | `FigureGrabConfig.Active*` pre-Bind literal fallbacks disagree with `Defaults/` (0.03 vs 0.01, 0.03 vs 0.05, 0 vs 0.03, 0 vs 17, 0 vs -133), uncommented | risk-gap (latent) | 2 |
| 7 | D-20 | `FigureOverlay.CloneRenderersSharingBones` has ZERO callers (checklist run); its twin `FigureHighlight.CloneOne` is the live copy | dead | 0 |
| 8 | D-04/05 | "prop holds are local-only / no wire record" in 9 sites (`GrabbableProp` x4 incl. one log clause, `PropGrab`, `StretchTarget`, `HeldPoseMirror`, `PropHeldPose` x2) — false since record 37; prop size IS on the wire (no 1:1 defect) | doc-drift | n/a |
| 9 | D-26 | `StretchTarget` adapter exists for an ownership rule that expired; an `IStretchable` interface removes 4 re-pointed adapters + the `Owner` identity dance | structure / parallel seam | 2 |
| 10 | D-31/34 | `PropAnimBelt` trio: every remaining probe is LIVE (ModBuild 471 unread on hardware); strand-5 prose describes a ModBuild 459 experiment as the current build; parallel global arrays; photometer blink is a deliberate visible artefact | leave-alone + doc-drift | n/a |
| 11 | D-16/27 | "only the gesture factor rides the wire (record 30)" / "`NetFigures.EaseSlot` reconstructs" — stale since ModBuild 157 (`FigureGrabConfig`, `FigureStretch`, `StretchTarget`) | doc-drift | n/a |
| 12 | D-01 | `FigureBody` (330 lines, consumed by `ActorBars` + driver) lives inside `FigureGrabbable.cs` | structure | 1 |
| 13 | D-09/17/32 | Orphaned/doubled `<summary>` blocks and empty headers (`PropGrab` x2, `FigureHighlight`, `PropAnimBelt` x4 + 3 headers) | doc-drift | n/a |
| 14 | D-07/11/33 | Small doc-drift: quoted rotation expression; `Alike` "mirror ships"; `FigureCloth.Advance` "ONLY place that writes a Cloth" | doc-drift | n/a |
| 15 | D-02/03/21/24/29 | INVARIANTS §6/§8/§14/§15 and audit R10/R42/§6.1 — all VERIFIED (R10, R42, §6.1 are "no longer true" = fixed) | leave-alone | n/a |
| 16 | D-08/25/28 | loadbearing.py rows for this folder are all NOT load-bearing (own-instrument reads only; one is a name collision) | leave-alone | n/a |
| 17 | D-10/12/18/22/30/35/36 | Named starting points and figure/prop pair verdicts — leave alone with reasons | leave-alone | n/a |

## Files read (whole, line counts at HEAD 7842c10b)

| file | lines | file | lines |
|---|---|---|---|
| FigureGrabbable.cs | 1800 | FigureGhosts.cs | 208 |
| GrabbableProp.cs | 2384 | PropGhosts.cs | 148 |
| PropGrab.cs | 841 | FigureRingSuppressor.cs | 112 |
| HeldProps.cs | 305 | ActorPropBody.cs | 1029 |
| HeldFigures.cs | 171 | PropVisualLookup.cs | 283 |
| NetHeldFigures.cs | 78 | FigureStretch.cs | 553 |
| NetHeldProps.cs | 121 | StretchCaptureWatch.cs | 429 |
| HeldPoseMirror.cs | 262 | StretchTarget.cs | 220 |
| PropHeldPose.cs | 343 | FigureStretchMath.cs | 177 |
| FigureGrabConfig.cs | 778 | HeldGlideMath.cs | 66 |
| FigureBusy.cs | 576 | HeldPropCard.cs | 218 |
| FigureStallWatchdog.cs | 162 | HeldPoseReport.cs | 212 |
| PropReach.cs | 881 | FigureGlowGrade.cs | 116 |
| PropLift.cs | 270 | FigureCloth.cs | 1910 |
| WalkInHighlightEdges.cs | 143 | FigureClothHands.cs | 334 |
| FigureHighlight.cs | 801 | ActorBehaviour_HeldTransform_Patch.cs | 70 |
| FigureOverlay.cs | 1548 | PropAnimBelt.cs | 3625 |
| PropAnimBelt.RenderPass.cs | 999 | PropAnimBelt.Photometer.cs | 2212 |
| FigureGrabDriver.cs | 2099 — SKIMMED by grep only (read by the integrator) | | |

36 files whole = 24 385 lines; plus the driver skim. Also read: `BRIEF.md`, `CHARTER.md`, `INVARIANTS-Hands-Board-Core.md` §6/§8/§14/§15/§16, `REVIEW-Hands-Board-Core.md` §2/§4, `redundancy-audit.md` (R10, R42, §4.13-16, §6), `STALE-DOC-REFS.md`, `FRAME-ORDER.lock`, `INSTRUMENT-WRITES.baseline`, `check-mirrors.sh` PART 3, `VRLog.cs` tiers, `Net/NetProps.cs` (grep), `Defaults.Board.cs` (grep), the 14 post-cleanup commit messages on `PropAnimBelt.*`.

## Verified still true / no longer true

**Still true at HEAD:**
- INVARIANTS §6 `FigureGrabDriver.LateUpdate` runs after the Animator — marker at `FigureGrabDriver.cs:410`, pins at `:416/:418`, `FigureRingSuppressor.Tick` in LateUpdate (FRAME-ORDER.lock row intact).
- INVARIANTS §8: held rotation fixed anchor-local (`ApplyHeldPose`); left mirrors right via `HeldPoseMirror`; size captured at grab (`_heldLocalScale`, see the `_heldBaseScale` rename note in D-02); glide keeps the actor in `HeldFigures` until arrival; re-grab finishes the glide first; unscaled cubic ease-out (`HeldGlideMath`); `AuthoritativeCellChanged` per-frame; `Restore` fake-null unwrap; offset-anchor suppression via `AllowsHand`; whole-method Update/LateUpdate prefix, no FixedUpdate; `PinAnimatedRoots` one write; `SetHilighted` routed to `RecordGameIntent`; `FigureRingSuppressor.Restore` key alias; ghost explicit-spawn/reconcile-despawn; overlay `_ZTest LEqual` / `_ZWrite 0` + skip without bundle shader; highlight container sibling of the Animator object; frozen ghost keeps Animator, destroys PS before renderers, `enabled=false` before `Destroy`; `OverlayMaterialOwner` / `OverlayPulse` own their material; `ApplyRenderOnTop` no-op with retained block.
- INVARIANTS §15 KEEP: `ApplyRenderOnTop` block + `RestoreRenderers` (two release paths); `FigureGrabConfig.HeldOffset` (still zero readers, still the doc anchor); `[FigureGrab]` LEGACY keys `HeldOffsetForward/Up/Side`, `HeldTiltDegrees`, `HeldFaceYawDegrees` and the per-style `{Style}HeldTiltDegrees/FaceYawDegrees/RollDegrees` still bound and labelled LEGACY; `HeldUpright` still global; `FigureGhosts.Ghost.Pos/Rot` still read every Tick.
- REVIEW §2.1 (`ApplyRenderOnTop`), §2.11 (patch stays in `Board/FigureGrab/`, registered once at `BoardModule.cs:118`), P3 mirrored `ReachMeters` 0.13 (driver `:112` still says so).
- redundancy-audit §4.13 (two elections), §4.14 (`HeldFigures` vs `NetHeldFigures`), §4.15 (`PropHeldPose` own dials) — all deliberate, reasons still at the sites.
- `check-mirrors.sh` PART 3: `FigureBusy.cs:334-337` carries all three rules-engine terms; the two `~` KNOWN-OPEN marks' reasons still hold (the grab-start gate asks only the flow; the watchdog calls `FinalizeAttackFlow`, which has no `IsAnimated` equivalent). Do NOT add `IsAnimated`.

**No longer true (fixed since the audit was written — record as closed):**
- redundancy-audit R10 (`FigureGrabbable.cs:418/:576 TickHighlightMode` vs `GrabbableProp.cs:392` one-way gate) — resolved 2026-09-05 by `WalkInHighlightEdges`; `TickHighlightMode` no longer exists.
- redundancy-audit R42 row for `ActorPropBody.cs:931` (no heartbeat) — now `VRLogThrottle Census = new(60 s heartbeat, 12 budget)` at `:122`.
- redundancy-audit §6.1 (`ActorPropBody.cs:933` cadence advanced only on the logging branch) — `_nextCensus` now advances at `:972` BEFORE `Census.Wants` at `:973`; the driver's sibling `_nextPropCensus` (`FigureGrabDriver.cs:742-744`) is still correct.
- INVARIANTS §15 "`[FigureGrab] HeldScale` kept bound as LEGACY" — the `HeldScale` family was DELETED in the 2026-08 dead-settings sweep (`FigureGrabConfig.cs:376-385` records why); the §15 list should drop it.
- INVARIANTS §8 "`_heldBaseScale`" — renamed/replaced by `_heldLocalScale` + `_stretch` (ModBuild 108); intent unchanged.
- INVARIANTS §8 "The highlight container … Where: `FigureOverlay.CloneRenderersSharingBones`" — that method has no callers (D-20); the live clone is `FigureHighlight.CloneOne`.

## What I did not find

- No `FindObjectsOfType` on a per-frame path in this folder. The three that exist (`PropAnimBelt.FindHomeTwin`, `ArmClocks` x2) are arm-time, at most 3 per session, and documented as such.
- No cadence gate advanced only on the logging branch (the §6.1 case is fixed; `PropGrab.Scan`, `ActorPropBody.LogCensus`, `StretchCaptureWatch.Tick`, `GrabbableProp.TickInfo` all advance first).
- No fake-null dictionary hazard: every `Dictionary<UnityObject,...>` remove uses the CLR reference (`FigureGhosts.Destroy`, `FigureRingSuppressor.Restore`, `ActorPropBody.Release`), every list identity is `ReferenceEquals` (`HeldFigures/HeldProps.IndexOf`, `PropAnimBelt.Contains`).
- No exception path that leaves state latched: `GrabbableProp.TryPushRichInfo` catches and falls back; `FigureBody.TryMeshTopY` catches the bake; the two render-loop callbacks (`OnRenderPassProbe`, `OnPhotometerPreRender`) are wrapped whole and count faults; `FigureStallWatchdog` catches per bar.
- No load-bearing instrument write in this folder: every `Log*/Report*/Census*/Emit*/Describe*/Announce*/Note*` body writes only its own budget/latch fields, read by itself or a `Reset`/`Clear` (D-08, D-25, D-28, D-33; `PropAnimBelt.Announce*`/`Emit*` write only `_logsLeft`/`_restoreLogsLeft`/`_verdictsLeft`/`_boardLeft`/`_phBudgetLeft` and the `KindsDone` rosters). The `INSTRUMENT-WRITES.baseline` having no FigureGrab entry is correct.
- No config key bound and unread: all `[FigureGrab]` keys are read (the LEGACY ones once, as seeds, and say so). No Bind description states a default other than the bound one.
- No game-behaviour assertion I could cheaply falsify against `decompiled/`; the ones I checked by grep (`PropDummyObject` exists in `PropHealthDetails.cs` / `CClass.cs`; the `ScenarioRuleClient` / `GameState` damage-wait terms) hold.
- No text-identical body worth merging beyond D-20/D-26: the grab/release/glide/restore lifecycle in `FigureGrabbable` vs `GrabbableProp` (~250 lines each) is PARALLEL but not identical (actor vs visual root; `HeldFigures` vs `HeldProps`; stat panel vs info card; cloth notes; layer parking; Apparance freeze; `PropAnimBelt`); the shared halves were already extracted 2026-09-05 (`HeldGlideMath`, `HeldPoseMirror`, `FigureStretchMath`, `HeldPoseReport`, `WalkInHighlightEdges`, `StretchTarget`). A further merge would be a `HeldTransformState` composition object owning origin-capture / reparent / glide / restore for both — a design decision (the class doc argues for the sibling), so it is recorded here and not proposed as a commit.
- No `STALE-DOC-REFS.md` line names a file in this set.
- No spent probe in `PropAnimBelt.*` at HEAD: the cleanup commit `6082b83b` and `a9c6fc8a` (PositionSweep, PropOcclusionGate) retired everything that had answered; the remaining instruments were added or extended in `d5d777b5` / `15ab3161` / `25541168` (ModBuilds 469-471) and are unread on hardware. Only the strand-5 PROSE is stale (D-31).
- Searches that came back empty: `grep -rn TickHighlightMode src/` (0); `grep -rn CloneRenderersSharingBones src/ tests/` -> declaration + 2 doc refs only; `grep -rn 'FigureGrabConfig.HeldOffset' src/` (0 readers); `grep -rln 'TURN STALL WATCHDOG' .planning docs` -> only the guard baseline/surface (so a tier promotion breaks no doc grep); `grep -rln 'STRETCH CAPTURE ON' .planning docs` -> guard files only.
