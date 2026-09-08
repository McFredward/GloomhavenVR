# Review — lane `worldui-frame` (refactor 2026-09)

> Phase-1 deliverable per `BRIEF.md` §3.1. Base: `b40f8564` (the brief commit, a descendant of
> `60beaa1f`). Guard baseline taken at that HEAD (`.guard/baseline.rev` = HEAD).
> **No `src/` file is changed by the commits that carry this document.**
>
> File set (123 k lines, 173 files): `WorldUI/{Conversion,Options,Patches,Sharpness,Materialise,Grab}/`,
> `Board/` (incl. `FigureGrab/`, `Patches/`), `Hands/` (incl. `Interact/`).
>
> Written incrementally — one section per sub-area, committed as each is finished, because a
> session limit killed the first reading pass (four parallel sub-readers) and everything that was
> not on disk was lost. §0 states what has been read and by whom.

---

## 0. Method, instruments, coverage

### 0.1 Instruments (brief §3.1), run over the whole lane set

`hygiene2.py` — 2 059 methods; 61.7 % ≤ 20 code lines, 2.4 % over 100, **8 over 200**:

| code lines | method |
|---|---|
| 510 | `Board/FigureGrab/PropAnimBelt.Photometer.cs:1652 EmitPhotometer` |
| 450 | `WorldUI/Sharpness/PanelSupersample.3.Report.cs:22 Report` |
| 242 | `Board/FigureGrab/FigureGrabConfig.cs:495 Bind` |
| 238 | `Board/FigureGrab/FigureGrabDriver.cs:725 LogPropCensus` |
| 224 | `WorldUI/Conversion/CanvasConversion.3.Fit.cs:4040 LogFixedFit` |
| 219 | `WorldUI/Conversion/CanvasConversion.3.Fit.cs:3338 MeasureFixedFitParts` |
| 216 | `WorldUI/Conversion/CanvasConversion.3.Fit.cs:6120 TickHitRect` |
| 208 | `Hands/HandsConfig.cs:369 Bind` |

Five of the eight are diagnostics or `Bind` monoliths (the brief's calibration holds for this lane).

`dupes2.py … 12` — **33 groups ≥ 12 lines, 32 cross-file**, in three families only:
1. `Options/VROptionsTab.6.BoardTopic.cs:334-382` ↔ `Options/VROptionsTab.7.TopicTrees.cs:604-651` (the brief's 21-line clone; 20 overlapping windows);
2. `Sharpness/PanelSupersample.4.Content.cs:2048-2074, :1194-1210` ↔ `Conversion/PanelInkBounds.cs:926-950, :719-735` (an ink-measure clone across two sub-folders — see the Sharpness and Conversion sections);
3. `Sharpness/PanelSamplingProbe.cs:629-647` ↔ `Sharpness/PanelSupersample.3.Report.cs:1455-1469`;
plus one 13-line internal clone `Options/VROptionsTab.2.Rows.cs:1560-1572` ↔ `:1664-1676`.

`loadbearing.py` — 85 fields written inside diagnostic-named methods, 36 flagged "read outside a
diagnostic". The instrument is textual and over-reports: 6 of the 36 are the discard `_`, and most
of the rest are the instrument's own throttle stamps read by a RESET in `ReleaseAll`/`Clear` (e.g.
`FigureGrabDriver::_propCensusWalksLeft` read at `:2091` = the re-arm in `ReleaseAll`), which is
not a mechanism read. The genuinely load-bearing ones in this lane that `INSTRUMENT-WRITES.baseline`
already lists: `CanvasConversion::s_orderHashNodes <- LogPanelOrder`, `PanelGrabHandle::_reelSelfTicks
<- ReportReelReleased`, `PanelSamplingProbe::Surfaces <- Report`, `PanelSupersample::FrameSb <-
ReportCaptureFrame`, `RenderTargetProbe::Sb <- BehaviourList`, `UiSoundEar::_stateLogged <-
ReportState`, `OpenState::Esc/Mp/Opt <- OpenState`. Per-file verdicts are in the sections below.

`scripts/patch-inventory.sh check` — 107 classes / 165 methods, all registered exactly once. The
two classes the `uniq -c` over `PatchAll(typeof(` shows twice (`CQuestStateExtensions_CheckRequirements_Patch`,
`UnityGameEditorRuntime_LoadScenario_Patch`) are one live registration in `WorldUIModule.cs:74-75`
plus a COMMENTED-OUT copy in `Options/VROptionsTab.Cheats.cs:49-50` — not a double apply.

### 0.2 Log tiers — the one systemic fact every instrument finding below depends on

`Core/VRLog.cs` (the 2026-08-30 re-decision, ModBuild 331): `VRLog.Error` prints from `Error`,
`Alert` from `Warning`, `Note` from `Info` (**the shipped default**, `Defaults.Plugin.LogLevel =
Info`); `VRLog.Warn`, `VRLog.Info` and `VRLog.Debug` all gate on `Level >= Debug` and print
NOTHING at the shipped level. Its own doc says promotion (`Warn → Alert`, `Info → Note`) "is the
ongoing work, and it is one word per line". Consequence for this review:

- every `INVARIANTS-Hands-Board-Core.md` §15 "log lines that are grep tokens" in this lane
  (`uGUI hover ENTER/EXIT`, `uGUI click:`, `GRAB STATE heal:`, `ray ON/OFF — <reason>`,
  `Ghost hand ON/OFF`, `FIST <side>`, `squeeze released`, `index touch source:`,
  `Global skinWeights raised`, `LASER INFO SUPPRESSION`, `stable hex decal ZTest=`) is at
  `Info`/`Warn`/`Debug` at HEAD, i.e. a grep token of a DEBUG-level log. That is the user's
  ruling, not a defect; but the invariants document states it as if the shipped log carried them.
  → recorded under "anything the brief/docs got wrong" (the file is outside this lane's edit set);
- a code comment that says a line is "Info on purpose — BepInEx drops Debug" or "so hardware logs
  carry it" is a FALSIFIED claim at HEAD (RayUguiDriver has already corrected one such sentence
  and left two more standing — F-07). Those are doc-drift findings in this lane's files;
- `scripts/check-hw-verify.py` pins the `// HW-VERIFY` lines to a printing tier, so the owed
  hardware lines are safe by gate; the review only proposes promotion where a site's own doc
  claims a hardware-report role AND the line is edge-gated/throttled (never a per-frame line).

### 0.3 Coverage

| sub-area | lines | read by | status |
|---|---|---|---|
| `Hands/Interact/` (14 files) | 7 016 | integrator, whole | §1 done |
| `Board/FigureGrab/FigureGrabDriver.cs` | 2 099 | integrator, whole | §2 done |
| `Board/FigureGrab/` (other 36 files) | 24 385 | sub-reader D, whole | §3 done |
| `Hands/` root (11 files) | 4 773 | integrator, whole | §4 done |
| `Board/` root + `Board/Patches/` | 11 450 | sub-reader E, whole | §5 done |
| `WorldUI/Conversion/` + `Materialise/` | 25 904 | sub-reader A (11 files whole; instrument/exception/declaration level for the rest — see A-26) | §6, F-46…F-63 |
| `WorldUI/Options/` + `Grab/` + `Patches/` | 33 372 | sub-reader B (1 file whole, 8 at declaration/instrument level with named ranges, the rest by six automated scans plus targeted reads) | §7, F-73…F-88 |
| `WorldUI/Sharpness/` | 13 799 | the integrator (instruments/latches/teardown/config whole; sampling maths scanned) | §8, F-64…F-72 |

Prior-round documents read in full: `BRIEF.md`, `CHARTER.md`, `INVARIANTS-WorldUI.md`,
`INVARIANTS-Hands-Board-Core.md`, `REVIEW-WorldUI.md`, `REVIEW-Hands-Board-Core.md`,
`redundancy-audit.md`, `FRAME-ORDER.lock`, `STALE-DOC-REFS.md`, `INSTRUMENT-WRITES.baseline`,
`check-mirrors.sh` PART 3.

Finding IDs: `F-nn`, ranked within each section by risk reduced. Tiers per CHARTER §4 (0 dead ·
1 motion · 2 dedup · 3 defect); "act" = this lane will commit it in phase 2, "defer" = finding only.

---

## 1. `Hands/Interact/` — ray, grab, poke, uGUI pointer

Read whole: `RayInteractor.cs` (1 277), `ProximityGrabber.cs` (1 267), `RayUguiDriver.cs` (894),
`RayGrabDriver.cs` (238), `PokeInteractor.cs` (720), `UguiPointer.cs` (1 362), `PalmGate.cs` (262),
`VRInteractables.cs` (378), `UguiPokeSurfaces.cs` (166), `UiScrollFocus.cs` (193), `IGrabbable.cs`
(165), `IPickProvider.cs`, `IPokeable.cs`, `PokeOnlyTarget.cs`.

### F-01 — A trigger pull during a grip-only bar's hover tail grabs nothing and clicks nothing
- **File:line:** `Hands/Interact/ProximityGrabber.cs:244-289` (the tail branch) against `:314-321`
  (the non-tail grip-only branch) and `RayUguiDriver.cs:341-345` / `RayGrabDriver.cs:185-189`
  (the consumers of `TriggerGrabOffered`).
- **Class:** defect. **Tier:** 3. **Act.**
- **Evidence (from source):** state = `Highlighted` is a grip-only target (a window's or the tray's
  drag bar, `GrabWithGrip == true`) that has just left the election and is inside its 0.20 s hover
  tail (`_highlightGrabbable == false`), and `UpdateHighlight` collected a `_triggerCandidate`
  (a figure/card in reach that passed every gate). Line 244 then publishes
  `_triggerGrabOffered = true` (its second disjunct, `Highlighted.GrabWithGrip`, is true in the
  tail). On the same frame `RayUguiDriver.Tick:341` and `RayGrabDriver.Tick:185` read that offer
  from the previous frame and YIELD the press / the laser carry ("uGUI press YIELDED to the hand").
  The trigger edge then reaches the tail branch at `:268`: `GripDown` is false, so `:280` logs
  `grab refused — '<bar>' is no longer the elected candidate … the glow is dropped now`, drops the
  highlight and returns. **No `TryTriggerFallThrough`, no `TryTriggerGrab`** — the pull neither
  clicked the panel (yielded) nor grabbed the figure (refused). One frame later the highlight is
  null, the edge is gone. The non-tail branch (`:314-321`) does fall the trigger through, which is
  the ModBuild 359 ruling ("zwei verschiedene Tasten … greiftaste für den Balken und trigger für
  die Karte") — the tail branch predates the fall-through and was never given the mirror case.
  `TriggerGrabOffered`'s own doc (`:173-176`) states the offer is real "since the trigger now FALLS
  THROUGH a grip-only highlight … the drivers must yield to it" — the yield happens, the grab does
  not.
- **Proposed action:** in the tail branch, before the refusal: `if (_hand.TriggerDown &&
  Highlighted.GrabWithGrip && _triggerCandidate != null) { TryTriggerGrab(_triggerCandidate,
  "proximity fall-through", clearHighlight: false); return; }` — the exact mirror of the grip
  fall-through four lines above it, with the same `clearHighlight:false` reason. Refusal wording
  untouched.
- **Guard expectation:** `CHANGED` confined to `ProximityGrabber` (one method).
- **Risk if wrong:** a trigger pull inside a bar's tail takes the figure behind the bar instead of
  doing nothing — which is what the published offer already told the two ray drivers it would do.
- **Hardware line:** hover a floated window's drag bar with the palm, move the hand onto a
  miniature within 0.2 s and pull the trigger: the mini is grabbed. Tokens: `uGUI press YIELDED` /
  `laser-carry YIELDED` (Note) followed by NO `is no longer the elected candidate` refusal for a
  `GrabWithGrip` target.

### F-02 — `GRAB STATE heal` is a force-release that prints nothing at the shipped level
- **File:line:** `Hands/Interact/ProximityGrabber.cs:748, :751` (`VRLog.Warn`).
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (promote to `Note`).**
- **Evidence:** `HealDeadHeld` is the structural self-heal for a stale hold (INVARIANTS §4: "a
  stale `Held` refuses EVERY subsequent grab and keeps `RayInteractor.Active` false — laser gone").
  Its doc says "force-release with a Warn instead of latching"; INVARIANTS §15 lists `GRAB STATE
  heal:` as a hardware grep token. Since ModBuild 331 `Warn` gates on `Debug`, so a session in
  which the mod healed a stuck hold — the one event that explains "the laser vanished and came
  back" — leaves no line in the default log. It is edge-triggered (at most once per stuck hold).
- **Proposed action:** `VRLog.Warn` → `VRLog.Note` on both lines; wording untouched; mark the first
  `// HW-VERIFY`-free (it is not owed, it is evidence).
- **Guard:** `CHANGED` confined to `ProximityGrabber.HealDeadHeld` (the call target changes).
- **Hardware line:** none to provoke; the token `GRAB STATE heal:` now appears in a default log if
  a hold is ever healed.

### F-03 — `Poke WITHHELD` / `Poke press CANCELLED` claim to answer "reagiert nicht" from the log and cannot
- **File:line:** `Hands/Interact/PokeInteractor.cs:263-288` (`LogGripGate`, `VRLog.Info`, throttled
  1 s per hand), `:616-622` (`Poke press CANCELLED`, `VRLog.Info`, edge).
- **Class:** doc-drift + risk-gap. **Tier:** 0 (sentence) / 3 (one word). **Act (promote both to
  `Note`, keep wording).**
- **Evidence:** `LogGripGate`'s doc: "A 'der Button reagiert nicht' report has to be answerable
  from the log alone — the same reason and the same throttle the two keycap sites carry." The
  keycap sites it names print at the shipped level; this one does not (`Info` = debug tier). The
  cancel line's own comment: "it is the line that answers 'the button flashed pressed and then did
  nothing'" — also silent at the shipped level. Both are the falsifier for the grip-chord ruling
  ("sollen nur auf physisches Drücken reagieren wenn die Greiftaste gedrückt ist"), i.e. exactly
  the report class that produced them. Both are throttled/edge, so promotion costs at most one
  line per second per hand while a fingertip rests on a panel with the grip open.
- **Guard:** `CHANGED` confined to `PokeInteractor` (two call targets).
- **Hardware line:** touch a converted panel's button with the grip OPEN: `Poke WITHHELD (…)`
  appears once per second in the default log; close the grip: it stops.

### F-04 — `RayUguiDriver` carries two "Info on purpose — BepInEx drops Debug" claims it has already falsified once
- **File:line:** `Hands/Interact/RayUguiDriver.cs:568-574` (`LogSettingsExemption` doc) and
  `:595-618` (`LogSettingsClickTrace` doc: "one Info line per trigger press … makes the next log
  decisive"); compare `:267-274`, where the same file records that the identical sentence on the
  SUPPRESSED line "was a stale claim rather than a choice" and promoted that line to `Note`.
- **Class:** doc-drift. **Tier:** 0. **Act (fix the two sentences; do not promote).**
- **Evidence:** at HEAD both methods call `VRLog.Info`, which prints only at `Debug`. The
  exemption line is a steady state while the beam crosses a blocking float (throttled 1 s); the
  click trace is a multi-line payload per press (0.25 s throttle). Neither is owed on hardware
  (no `HW-VERIFY`), the ruling they prove (settings never input-blocked, 2026-08-02) has been
  accepted, so the honest fix is the sentence: "Debug tier since ModBuild 331; set `[General]
  LogLevel = Debug` to capture it", not a promotion of a chatty line.
- **Guard:** empty (comments).

### F-05 — `PalmGate` open/close lines promise pitch-invariance proof "in hardware logs" at debug tier
- **File:line:** `Hands/Interact/PalmGate.cs:48-49` (class doc) vs `:230, :236` (`VRLog.Info`).
- **Class:** doc-drift. **Tier:** 0. **Act (sentence).**
- **Evidence:** "Open/close logs include the current PITCH of the finger axis to prove
  pitch-invariance in hardware logs" — true only of a `Debug`-level log since 331. The v4 gate is
  accepted; the line is per gesture, not owed. Fix the sentence.
- **Guard:** empty.

### F-06 — INVARIANTS entry "The gate measures the VISUAL hand frame (`HandRig.Root`)" is superseded at HEAD
- **File:line:** `Hands/Interact/PalmGate.cs:37-44, :164-175` vs `INVARIANTS-Hands-Board-Core.md`
  §5 "The gate measures the VISUAL hand frame".
- **Class:** doc-drift (in a planning doc outside this lane's edit set). **Tier:** n/a. **Defer →
  report §"wrong in the brief/docs".**
- **Evidence:** the gate now builds its frame from `_hand.transform.rotation *
  HandsConfig.ShippedSeatRotation(style)` and the class doc records that reading `HandRig.Root`
  "was tried and rejected: a cosmetic re-seat then silently re-tuned the gesture". The invariant's
  "Breaks if: switched back to `_hand.transform.rotation`" is now literally what the code does
  (times the shipped seat). Also: the same entry's "(that is what `UseDevicePalmNormal` used to
  select — now vestigial, see §11)" — P8 is DONE: the field is gone (`:42-44` says so; grep of
  `src/` finds no `UseDevicePalmNormal`).
- **Proposed action:** none in code. The registry entry needs rewriting by whoever owns
  `.planning/refactor/INVARIANTS-*.md`.

### F-07 — `VisualsAllowed` + `UiTargets` registry: retired mechanism kept for reversibility — leave alone
- **File:line:** `Hands/Interact/RayInteractor.cs:467-485, :872-886, :945-957`.
- **Class:** leave-alone (looks dead, is a documented policy seam). **Tier:** n/a.
- **Evidence:** `VisualsAllowed` returns `true` unconditionally (user ruling 2026-08 "der Laser ist
  ausnahmslos da"); `UiTargets` is still populated by `FlatScreen.RegisterUiTarget` and read by
  nobody. The file states both are kept "purely for reversibility of that policy" and "so a future
  policy change slots back in at this one seam". §5 check: `RegisterUiTarget` has a live caller in
  `WorldUI/FlatScreen` (another lane); deleting the registry is a cross-lane API removal for ~15
  lines. Not worth a NEEDED-OUTSIDE.
- **Proposed action:** none.

### F-08 — `UguiPokeSurfaces.Unregister` uses `List<Canvas>.Remove` (Unity equality) — harmless, note only
- **File:line:** `Hands/Interact/UguiPokeSurfaces.cs:703`.
- **Class:** leave-alone. **Tier:** n/a.
- **Evidence:** INVARIANTS §2 explains why `UguiPointer.RemoveByRef` exists (two DESTROYED objects
  compare equal under `UnityEngine.Object.Equals`, so `List.Remove` can drop the wrong one). The
  same call shape is here. It is harmless by construction: the only way two entries compare equal
  without being the same reference is when both are destroyed, and `Prune()` (called by both
  drivers on the first dead sighting) removes every destroyed entry anyway — the "wrong" removal
  only changes which dead entry dies first. Not a defect; recorded so nobody "fixes" it into a
  behaviour change or, worse, flags `RemoveByRef` as redundant because this site "gets away with
  it".

### F-09 — Invariants re-verified at HEAD (all still true unless listed above)
`UiHitOverride` projection-only (`RayInteractor.cs:971-977`); `SuppressFarClick` separate latch
(`:182-184`); two-frame freshness (`:362-364`, `FigureGrabDriver.cs:1143`); `Active` level-derived
with the five-row truth table (`:559`, `:583-589`); `SyncActiveState` change-deduped (`:1099`);
fan occluder computed once, three consumers (`:755-791`, `RayUguiDriver.cs:209`,
`RayGrabDriver.cs:143`); `UpdateModalPickBlock` is GONE — replaced by `UpdateCommitSuppression`
(`:912-933`, "the beam always picks; only COMMITS are modal-gated", user ruling 2026-08) — the
INVARIANTS §1 entries "Modal pick-block keys on BlockingWindowModalActive" and "computed once per
frame and shared" describe the retired mechanism (the shared-per-frame half survives in
`_commitPolicyFrame`); ray visuals at sortingOrder 5000 + queue 4600 (`:465, :1085`); angular
reticle with the rig-scale-multiplied clamps (`:437-453, :1012-1027`); `VisualsAllowed` early-out
on `HasFreshUiHit` — RETIRED with the cone gate (F-07); knuckle origin (`:990-1003`, now the real
knuckle, not the projection — the file explains the change); lazy visuals re-layered (`:1066-1067`).
`UguiPointer`: leaf→common-root hover walk (`:540-587`), recorded chain (`:89, :545`), instance-id
refcounts with `is null` (`:1196-1251`, plus the new `UguiPressTracker` twin), `RemoveByRef`
(`:651`), four pointer ids (`:28-34`), nested canvases merged not registered (`:250-270`), `Beats`
tie to the challenger (`:392-399`, now through `StableOrder` because converted hosts' live
sortingOrder is rewritten per frame — a documented extension), depth pick far-ray only (`:225`),
`useDragThreshold = false` + `Drag` before the release check (`:810`, `RayUguiDriver.cs:509-516`),
`Scroll` clears delta (`:863`), click only on the same handler (`:890`), provenance tag (`:189`).
`PokeInteractor`: three press modes (`:543-662`), deliberate keeps the plain PressThrough
(`:443-444`), 0.8× headroom (`:541`), canvas switch cancels (`:488-498`), cooldown stamped only on
fire (`:582, :638, :646`), front side only, lazy prune, `SafeExit` — plus the grip chord
(`:260`) added since. `ProximityGrabber`: defers to the beam but not to a panel hover (`:498-509`),
`GrabWithGrip` per object and the grip branch before the trigger branch (`:314`), symmetric
`releaseOnTriggerUp` (`:227`), `HealDeadHeld` (`:724`), honest affordance (`RayGrabDriver.cs:78`),
bar-only laser grab (`:106`), 2.5 cm switch margin (`:1150-1157`), `Collider.Raycast` (`:109`).
`PalmGate`: v4 transport (`:183-213`), hysteresis floor (`:226`), busy gate after the transport
(`:220`). FRAME-ORDER markers `VRHand.UpdateBody.*` are in `VRHand.cs` (Hands root, pending).
Mirrored constants `ReachMeters` (`ProximityGrabber.cs:52` ↔ `FigureGrabDriver.cs:112`) and
`FingertipRadius/ReleaseRange` (`PokeInteractor.cs:58-60`) carry the lint cross-reference.

### F-10 — What was not found in `Hands/Interact/`
No dead private member (every `private` field/method has a reader — verified per file by name);
no verbatim duplicate ≥ 12 lines; no `FindObjectsOfType` on a per-frame path; no `== null`
dictionary key on a `UnityEngine.Object` (all `is null`/instance-id); no comment naming a symbol
that does not exist except the deliberate `ReticleOverride` do-not-resurrect note
(`RayInteractor.cs:46-55`). REVIEW-Hands-Board-Core P13 (`UguiHoverTracker`/`DepthPortraitPicks`
out of `UguiPointer.cs`) was NOT done and is still a free empty-diff move — deferred: `UguiPointer.cs`
is 1 362 lines, the three registries are 190 of them and are read together with the pointer that
owns them; the 2026-08 plan already rated it "would not object to a leave-it call".

---

## 2. `Board/FigureGrab/FigureGrabDriver.cs` (read whole by the integrator)

### F-11 — `REACHED AND MISSED`, `FIGURE REACH …`, `pinch candidate`, `PICK VOLUME`, `grab REFUSED on` are documented as the boss-dragon round's instruments and print only at `Debug`
- **File:line:** `Board/FigureGrab/FigureGrabDriver.cs:1501` (`NoteReachMiss`, "the instrument the
  boss-dragon report needed and did not have", throttled 1 s/hand, gated on a real reach), `:1874`
  (`LogFigureReach`, once per adoption), `:2043` (`LogElection`, change-gated), `:2019`
  (`LogPickVolume`, change-gated), `:1925` (`NoteBusyRefusal`, throttled 1 s/hand — "the LEGIBLE
  half" of the turn-deadlock gate, user report 2026-08-11), `:1624, :1655, :1715, :1731` (reach
  extension lines).
- **Class:** doc-drift (the docs assert a hardware role) + risk-gap (the ONE line a "kann die
  Figur nicht greifen" report is answered by is silent). **Tier:** 0 / 3.
- **Evidence:** all `VRLog.Info`. `LogPropCensus` (`:1032`) in the same file was promoted to
  `Note` with the reason stated at `:720-723` ("Info is the DEBUG tier and is absent from a
  default-level log, which is why the ModBuild 334 hardware log carried none of this subsystem's
  diagnostics"). The reach/refusal lines were written for exactly the same purpose and left at the
  debug tier.
- **Proposed action:** promote `NoteBusyRefusal` (`:1925`) and `NoteReachMiss` (`:1501`) to `Note`
  — both are throttled to 1/s/hand, gated on a trigger EDGE and, for the miss, on a real reach into
  a drawn body (the 192-line noise case is documented as fixed at `:1424-1444`). Leave
  `LogFigureReach` (one line per figure adoption, up to ~20 per scenario, 800 chars each),
  `LogElection` and `LogPickVolume` at `Info` and fix their doc sentences instead: they are
  per-adoption/per-zoom bulk. **Act** for the two promotions + three sentences.
- **Guard:** `CHANGED` confined to `FigureGrabDriver` (two call targets).
- **Hardware line:** pull the trigger at a resolving enemy: `grab REFUSED on … turn-deadlock gate`
  once per second in the default log; reach into a boss above its pick collider and pull:
  `REACHED AND MISSED`.

### F-12 — The frame-order marker matches the lock; the census and instrument writes are self-contained
`Update` (`:321`) carries the exact `[FigureGrab.StallWatchdog, …, FigureGrab.LaserGrab]` list of
`FRAME-ORDER.lock`; `LateUpdate` (`:410`) the exact LateUpdate-required triple. `LogPropCensus`
writes `_propCensusWalksLeft/_propCensusWalksSpent/_lastCensusRegistered/_nextPropCensus/
_lastPropCensus` and the only non-diagnostic readers are the RESET lines in `ReleaseAll`
(`:2091-2093`) — not load-bearing; `loadbearing.py`'s flag is the over-report its doc warns of.
`ReleaseAll` deliberately does not reset `_propCensusWalksSpent` (the ceiling is per driver
instance by doc, `:684-687`) and does not reset `_lastCensusRegistered` — harmless: the next tick
compares against `PropGrab.Registered` (0 after `PropGrab.ReleaseAll`) and re-arms, which is the
intended behaviour on a new board. `_reachMissSuppressed` is a session counter by doc. No finding.

### F-13 — Two candidate Tier-1 motions in this file, both declined
`LogPropCensus` (238 code lines, of which ~110 are ONE string literal that is the census's
reading guide) and the REACH EXTENSION block (`:193-291`, `:1590-1768`) are the two seams a
reader notices. Declined: the census string is deliberately one line (its own text says "read it
like this" and the line is the greppable unit — splitting it moves a `Note` token); the reach
extension shares `Adopted` and the scratch lists with the registry and a partial split would put
the six per-figure fields in one file and their only writer in another. CHARTER §2: burden not met.

---

## 3. `Board/FigureGrab/` (remaining 36 files) — see the section of the same name below

## 4. `Hands/` root — see the section of the same name below

## 5. `Board/` root + `Board/Patches/` — see the section of the same name below

## 6. `WorldUI/Conversion/` + `WorldUI/Materialise/` — see the section of the same name below

## 7. `WorldUI/Options/` + `WorldUI/Grab/` + `WorldUI/Patches/` — see the section of the same name below

## 8. `WorldUI/Sharpness/` — see the section of the same name below

---

## 9. Standing items carried from the brief (status at HEAD, verified so far)

| item | status |
|---|---|
| redundancy-audit §6.1 `ActorPropBody.cs:933` cadence advanced only on the log branch | **FIXED at HEAD** (`:950-975`: `_nextCensus = now + …` before `Census.Wants`, with the survey cited) |
| redundancy-audit §6.3 `EscMenuShowSafety.cs:140/:155` swallowed exception silent at default | **FIXED at HEAD** (both `VRLog.Alert`, ModBuild 439) |
| redundancy-audit §6.4 / R30(a) `FocusDriver.Carrier` no repeat line | **FIXED at HEAD** (`:236-262`: shared `TickGuard.NoteThrow`, `Error` tier) |
| redundancy-audit R42 `ActorPropBody` census without heartbeat | **FIXED at HEAD** (`VRLogThrottle`/`Census.Wants`, `:970-973`) |
| REVIEW-Hands-Board-Core P8 `PalmGate.UseDevicePalmNormal` | **DONE** (field gone; `PalmGate.cs:42-44`) |
| REVIEW-Hands-Board-Core P13 registries out of `UguiPointer.cs` | not done, deferred (F-10) |
| `check-mirrors.sh` PART 3 `~FigureBusy.cs` / `~FigureStallWatchdog.cs` | pending (sub-reader D) |
| `PlacementDiagnostics` KEEP ruling | not re-raised |

<!-- §4 body appended by the integrator; the "pending" heading above is superseded by this section. -->

## 4. `Hands/` root (read whole by the integrator)

`VRHand.cs` (1 180), `HandsDriver.cs` (327), `HandsModule.cs` (81), `VRHands.cs` (52), `HandRig.cs`
(151), `HandStyle.cs` (50), `VRHaptics.cs` (51), `HandGhost.cs` (1 101), `HandVisuals.cs` (835),
`HandsConfig.cs` (628), `FingerCurler.cs` (317).

### F-14 — `[Hands] GhostHandOnFan`'s description says "OFF by default"; it ships ON
- **File:line:** `Hands/HandsConfig.cs:396-403` (the `Bind` description: "OFF by default; nothing
  about the hands changes until you enable it") vs `Defaults/Defaults.Hands.cs:18`
  (`GhostHandOnFan = true`) and the field doc at `:140-146` ("Both toggles SHIP ON … they shipped
  off when the feature was new").
- **Class:** doc-drift (the redundancy audit's R8 shape: a tooltip stating a default the mod does
  not ship, printed one line above the real `Standard: …` line). **Tier:** 0. **Act.**
- **Evidence:** the description is a string literal argument to `Bind`, so it IS in the compiled
  snapshot and in the player's `.cfg`; the sentence is false for every fresh install since the
  toggle flipped. The German twin is `Core/Loc/Loc.ConfigDescriptions.German.cs:2840` (lane core).
- **Proposed action:** replace the sentence with "ON by default." in the English `Bind` text;
  NEEDED-OUTSIDE for the German entry if it carries the same claim.
- **Guard expectation:** `CHANGED` confined to `HandsConfig.Bind` (one string literal).
- **Risk if wrong:** none to behaviour.

### F-15 — Three `Bind` descriptions and the bind log line still claim the seat keys are "seeded on first run from the old global seat + trims"
- **File:line:** `Hands/HandsConfig.cs:517-518` (`{s}VerticalOffset`: "supersedes the old shared
  HandVerticalOffset + trim; seeded on first run"), `:523-525` (`{s}ForwardOffset`, same),
  `:612` (the `[Hands] Per-style seat controls bound:` line ends "(first run seeds from the old
  global seat + trims)") — against `:203-207` ("THESE ARE MEASURED, NOT DERIVED. They used to be
  seeded at runtime from the old shared [Hands] seat keys … entries that were retired and have
  since been deleted from Plugin") and `:157-163` (the legacy entries are "deleted from Plugin
  entirely (2026-08 dead-settings sweep)").
- **Class:** doc-drift in user-facing text. **Tier:** 0. **Act (the two descriptions); the log
  line's parenthetical is inside a `VRLog.Info` string whose prefix nothing greps for — fix the
  parenthetical only, keep the prefix byte-identical.**
- **Guard expectation:** `CHANGED` confined to `HandsConfig.Bind` (string literals).

### F-16 — `TickGripLaserFalsifier` / `SampleLaserFlight` — a probe that answered, kept; note only
- **File:line:** `Hands/VRHand.cs:515-519, :554-557, :562-666`.
- **Class:** leave-alone. **Tier:** n/a.
- **Evidence:** the falsifier for the 2026-08-24 grip-held laser suppression; its doc says "at
  most one Info line per hand per 0.25 s … transitions inside that window are still printed (at
  Debug …)". Since 331 `Info` and `Debug` are the same tier, so the two-tier throttle is moot but
  harmless. Cost: four field reads at the top of `UpdateBody` and one bool compare at the end,
  per hand per frame; the string is built only on a transition. Nothing reads its state. Not
  retired: it is the only line that names WHAT the grip press cancelled, and the ruling it
  guards ("nur während die Greiftaste gedrückt gehalten wird") is one a future poke change could
  silently break.

### F-17 — Hands root: verified and not found
`FRAME-ORDER` markers `VRHand.UpdateBody.pose` (`:521`) and `.interactors` (`:539`) match the lock
byte for byte. `SetTracked(false)` cancels Poke/RayUgui/RayGrab/Grabber (`:950-956`); `PalmGate`
handles pose loss itself (`_uref = null`, `PalmGate.cs:139`) but keeps `IsOpen` — the fan stays
where the hand was last seen for the 0.35 s pose grace and beyond; no report, no finding.
`HandGhost`: `sharedMaterials` assignment (`:450`), clones destroyed on `Release` (`:359-364`),
`HandsDriver.TearDown` calls `HandGhosts.Shutdown()` before `Destroy(_handsRoot)` (`:280, :291`),
`IsAttachment` root-first (`:742-755`), `CanBlend` + `SwapToBlendableShader` + the `_FadeAlpha`
write (`:895-919`), `GHOST HAND DEPTH/BLEND/ORDER` at `Note` with `HW-VERIFY`. `HandGhost.Engaged`
/`RendererCount` (REVIEW P7): GONE. `EnforceGlobalSkinWeights` per frame (`HandsDriver.cs:172`) +
`smr.quality = Bone4` (`HandVisuals.cs:133`). `HandRig.PalmNormal` (`:590`), `HandPose.OpenPalm`
(`VRHand.cs:22`), `StyleCurlScale` (`FingerCurler.cs:190`) carry their KEEP notes. `HandVisuals`
re-layers the glove after the tree sweep (`:124`); sockets before `ApplyStyleScale` (`:150-153`).
`VRHaptics.Play` is the single haptics call site. `HandsModule.Shutdown` is symmetric with `Init`.
No dead member, no clone, no per-frame allocation found. Config: every `[Hands]`/`[WristHud]` key
bound here has a live reader (`SeatXxxSafe`/`StyleValue`/`HandGhosts`/`FingerCurler`).

<!-- §3 body appended by the integrator from sub-reader D's file (36 files read whole, 24 385 lines); the "pending" heading above is superseded. -->

## 3. `Board/FigureGrab/` (remaining 36 files) — sub-reader D, integrated

Read whole: every file except `FigureGrabDriver.cs` (§2). D's own IDs are kept in brackets; D's
full file is committed beside this one as `REVIEW-worldui-frame-subreader-D.md`.

### F-18 [D-23] — `STRETCH CAPTURE ON` (HW-VERIFY) always prints the "NO renderer survived … CENTRE distance" branch
- **File:line:** `Board/FigureGrab/FigureStretch.cs:363-366` (`StretchCaptureWatch.NoteCaptured(…,
  float.PositiveInfinity, "n/a", …, centreFallback: false)`) vs `StretchCaptureWatch.cs:187`
  (`BodyRadiusMm = IsPositiveInfinity(bodyRadiusReal) ? -1f : …`), `:334-339` (the branch and the
  never-true `CentreFallback` suffix); `FigureStretch.CaptureDistanceReal:492-535` computes
  `widest`/`any` and throws them away after `target.NoteCaptureVolume(...)`.
- **Class:** defect (an instrument that lies). **Tier:** 3. **Act.**
- **Evidence:** every capture episode → the caller passes placeholders → `BodyRadiusMm = -1`
  unconditionally → `EmitCaptureOn` prints the fallback sentence on EVERY line since ModBuild 400;
  the field the class doc calls decisive ("the radius and NAME of the widest renderer the shell
  was drawn around") is never printed. Expected: the widest renderer's name and radius, the
  fallback clause only when `any == false`.
- **Proposed action:** three `out` parameters on `CaptureDistanceReal` (`widestReal`, `widestName`,
  `centreFallback = !any`) filled from the loop it already runs, passed at `:363-366`. No token
  change; the line takes the other branch when the data says so.
- **Guard:** `CHANGED` confined to `FigureStretch`. **Hardware line:** capture a held figure with
  the free hand: `STRETCH CAPTURE ON … '<renderer>' at N mm implied body radius`.

### F-19 [D-13] — `TURN STALL WATCHDOG FIRED` is `VRLog.Warn` — silent at the shipped level — while its doc says the line is how a log shows the prevention leaked
- **File:line:** `Board/FigureGrab/FigureStallWatchdog.cs:151-159` (and the `FinalizeAttackFlow
  threw` line at `:145`); class doc `:60-61`.
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (`Warn` → `Alert` on both, doc sentence).**
- **Evidence:** the watchdog repairs a session-killing turn deadlock and prints nothing on the
  user's rig — "quietly papering over it", the exact thing its doc forbids. Same shape as
  redundancy-audit §6.2/§6.3, both already judged `Alert`. Token `TURN STALL WATCHDOG FIRED`
  unchanged; `grep -rln` over `.planning docs` finds only the guard files.
- **Guard:** `CHANGED` confined to `FigureStallWatchdog`. **Hardware line:** none to provoke.

### F-20 [D-14] — `FigureBusy.OwnAnimationPlaying` re-runs `GetComponentsInChildren<Animator>` on every call for an actor with no controller-bearing Animator
- **File:line:** `Board/FigureGrab/FigureBusy.cs:439-445` (`if (!AnimatorCache.TryGetValue(id, out
  entry) || entry.animator == null) { entry = ResolveAnimator(root); … }`), `:464-480`
  (`ResolveAnimator` returns `(null!, false)` when none exists — and that is what is cached, so
  the negative is re-resolved on the very next call).
- **Class:** risk-gap (per-frame allocation on the hot election path). **Tier:** 3 — **defer**
  (needs a decision: caching the negative makes a late-arriving Animator invisible to clause 4
  until `ClearCache`; the alternative is a cadence re-probe).
- **Evidence:** `IsBusy`/`HoldMustEnd` reach clause 4 in the normal case (clauses 1-3 false) from
  `FigureGrabbable.CanGrab/AllowsHand` (per hand per frame per in-reach candidate) and
  `FigureGrabDriver.cs:1104` (per held figure per frame); for a health-prop `PropDummyObject`
  actor that is at least two array allocations per frame for the whole hold. Falsifier on a
  hardware log: `MINIATURE AUDIT … 0 Animator(s) with a controller under the actor`.

### F-21 [D-06] — Prop `HeldPoseReport.EmitForProp` is double-gated; the figure twin is not
- **File:line:** `Board/FigureGrab/GrabbableProp.cs:659-661` (`_grabLogsLeft` budget, 4 per
  scenario) ahead of `:683` (`HeldPoseReport.EmitForProp`), vs `FigureGrabbable.cs:684`
  (unconditional); `HeldPoseReport.cs:55/:58` carries its own 8-per-session budgets.
- **Class:** risk-gap (instrument asymmetry; the comment at `:686-689` "one emitter, both call
  sites" is false after the 4th prop grab of a scenario). **Tier:** 3 (one-line move). **Act.**
- **Proposed action:** move the `EmitForProp` call above the `_grabLogsLeft` early-return.
- **Guard:** `CHANGED` confined to `GrabbableProp.OnGrab`.

### F-22 [D-19] — `PropGhosts.NotifyHeld` is silent when the ghost cannot be built; `FigureGhosts` prints the HW-VERIFY `NO ghost for …` line
- **File:line:** `Board/FigureGrab/PropGhosts.cs:68-72` vs `FigureGhosts.cs:120-127`.
- **Class:** risk-gap (parity feature "Es soll wie Figuren reagieren … einen Geist hinterlassen").
  **Tier:** 3 (additive `Note`). **Act.**
- **Proposed action:** before the `return`: `VRLog.Note("FigureGrab", $"[Props] NO ghost for
  '{prop.InstanceName}' ({prop.ObjectType}) — {report}")` with the figure line's HW-VERIFY comment.
- **Guard:** `CHANGED` confined to `PropGhosts`. **Hardware line:** a prop whose ghost fails now
  names its cause.

### F-23 [D-15] — `FigureGrabConfig.Active*` pre-Bind literal fallbacks disagree with `Defaults/`, uncommented
- **File:line:** `Board/FigureGrab/FigureGrabConfig.cs:369-374` (`0f/0.03f/0.03f/0f/0f/0f` vs
  `Defaults` 0.03/0.01/0.05/17/−133/0).
- **Class:** risk-gap (latent — reachable only before `Bind`, which runs at module start).
  **Tier:** 2. **Act (minimal form: point the six literals at the `Defaults.*` they stand for;
  the shipped value is unchanged because `Bind` has always run first — this is the
  `a-clamp-fallback-is-not-a-default` class, and `PropHeldPose.cs:256-259` states the rule this
  file breaks).**
- **Guard:** `CHANGED` confined to `FigureGrabConfig` (constant operands).

### F-24 [D-20] — `FigureOverlay.CloneRenderersSharingBones` has zero callers
- **File:line:** `Board/FigureGrab/FigureOverlay.cs:100-166`; its verbatim twin
  `FigureHighlight.CloneOne:722-771` is the live copy (`FigureHighlight.cs:717` says so).
- **Class:** dead. **Tier:** 0. **Act.**
- **§5 checklist (run by D):** not a Harmony target; not a Unity message; no `nameof`/string/
  `AccessTools`/`Traverse` reference (only its declaration, one `<see cref>` at `FigureOverlay.cs:16`,
  `FigureHighlight.cs:717` prose, INVARIANTS §8 "Where"); not a config key; not a log token; not
  debug-menu; `tests/` links only `FigureStretchMath.cs`, `HeldPoseMirror.cs`, `FigureGlowGrade.cs`
  from this folder; writes no field. `git log -S`: introduced in `bcc92604`, never removed.
- **Proposed action:** delete the method; fix the two prose sentences (`FigureOverlay.cs:16`,
  `FigureHighlight.cs:717`); INVARIANTS §8 "Where" → `FigureHighlight.Apply/CloneOne` (report).
- **Guard:** the member disappears from `FigureOverlay`, nothing else.

### F-25 [D-04/05/10/16/27] — "prop holds are local-only / no wire record" at nine sites is false since record 37; prop SIZE is on the wire (no 1:1 defect)
- **File:line:** `GrabbableProp.cs:112-115, :1916-1920, :2148-2156` and the log clause at `:2234`
  ("Map-item holds are local-only, so no peer sees this size") after the `[Size] … grab-time size
  CLAMP` token; `PropGrab.cs:51-53`; `StretchTarget.cs:46-51`; `HeldPoseMirror.cs:200-202`;
  `PropHeldPose.cs:61-67, :168-172`; `FigureGrabConfig.cs:204-208` and `FigureStretch.cs:147-151`
  ("only the gesture factor rides the wire … `NetFigures.EaseSlot` reconstructs" — stale since
  ModBuild 157: the wire carries the MEASURED size, `FigureGrabbable.HeldSizeFactorOf`).
- **Class:** doc-drift. **Tier:** 0. **Act (sentences; the `:2234` clause is inside a `Note`
  string — keep the token, rewrite the trailing clause only).**
- **Evidence (D read `Net/NetProps.cs`):** `SampleHeldStretch:251` sends `lossyScale.x / home`
  (record 37 field 4); the receiver applies `HomeLocalScale * StretchApplied` (`:486`);
  `NetHeldProps.Owns` grab-locks a peer-held prop (`GrabbableProp.cs:291-299, :376`).
- **Guard:** empty for the XML docs; `CHANGED` confined to `GrabbableProp` for the one literal.

### F-26 [D-26] — `StretchTarget` adapters exist for an ownership rule that expired
- **File:line:** `Board/FigureGrab/StretchTarget.cs:16-24, :33-39, :131-219`.
- **Class:** structure-naming / parallel seam. **Tier:** 2. **Defer** (correct today; an
  `IStretchable` interface on both grabbables removes four re-pointed adapters, `Owner`, `Bind`,
  `ClearAll` and the `ReferenceEquals(target.Owner, …)` dance — a design change across three
  types, not a dedup with a nil difference).

### F-27 [D-31/D-34] — `PropAnimBelt` trio: every remaining probe is LIVE; the strand-5 prose describes a ModBuild 459 experiment as the current build
- **File:line:** `PropAnimBelt.cs:565-582` (comment), `:2137-2150` (`Announce`, after the grep token
  `STRAND 5 OFF - NULL PERTURBATION`, which `.planning/held-prop-flash-experiments.md:93, :2228`
  greps verbatim), `:3074-3083` (`AppendAsymmetries`); class doc `:65-72` ("THE INSTRUMENT HALF IS
  TWO LINES" — it is six; "Nine rounds" — twenty-one).
- **Class:** leave-alone (instruments) + doc-drift. **Tier:** 0 for the prose. **Act (prose only,
  tokens byte-identical; past tense for the strand-5 sentences).**
- **Evidence:** D inventoried every instrument: steady-state cost is one bool/compare per frame;
  the armed windows (≤3 verdicts + ≤2 photometers per session) are heavy but bounded and every
  token is HW-VERIFY and unread on hardware since ModBuild 471 (`d5d777b5`/`15ab3161`/`25541168`).
  The photometer's bisection turns the held prop's renderers OFF for 6 frames twice per armed
  window — a DELIBERATE visible artefact (`b834e5c5`), recorded so nobody files it as flicker.
  `RpGlobalNames`/`RpGlobalKinds` are two parallel 52-entry arrays aligned by index (a
  `(string, RpKind)[]` would remove the hazard at zero behaviour cost — optional Tier 1, not done).
  **Do NOT split `EmitPhotometer` (560 lines):** one HW-VERIFY string whose clause order is the
  reading protocol; it is deleted whole when the defect closes.

### F-28 [D-09/D-17/D-32/D-07/D-11/D-33] — orphaned or doubled `<summary>` blocks and small doc-drift (roll-up)
`PropGrab.cs:62-85` (doc of the deleted `SettleScanBudget` now sits on `IdleSweepScans`; `:339/:355`
cite the dead symbol), `:166-176` (the `PickColliderOf` doc sits on `NearestInReach`);
`FigureHighlight.cs:143-153` (two summaries on `ModOwnedPrefix`); `PropReach.cs:312-314` (`own`
param doc names the pre-445 resolver); `PropAnimBelt.cs:238-244, :258-262` (docs of fields
deleted in `6082b83b`), `:586-595`, `:1450-1453` (misplaced method summaries), three empty section
headers; `GrabbableProp.cs:43-49` (quotes `HeldPalmRotation`/`HeldUprightRotation`, gone since the
`HeldPoseMirror` merge); `PropHeldPose.cs:280-284` (`Alike` doc says the mirror "is what ships";
`SameInBothHands` ships true, `:29-31`); `FigureCloth.cs:1392-1394` ("the ONLY place in this file
that writes to a Cloth" — `StepCook:940`/`StepLiveCook:1116` also do). **Tier 0, guard empty. Act.**

### F-29 [D-01] — `FigureBody` (330 lines, consumed by `ActorBars` and the driver) lives inside `FigureGrabbable.cs`
- **Class:** structure-naming. **Tier:** 1 (whole-type move; guard empty). **Act only if a Tier-1
  commit is made in this folder anyway** — the value is discoverability of the health-bar anchor
  rule, which a reader currently finds inside the grabbable's file.

### F-30 [D-02/03/12/18/21/22/24/25/28/29/30/35/36] — verified, pair verdicts, and leave-alone
- **Invariants still true:** every §6/§8/§14/§15 entry naming this folder (D lists each `Where`
  line with its HEAD line number). Superseded wording for the report: §8 "`_heldBaseScale`" is now
  `_heldLocalScale` + `_stretch` (ModBuild 108, intent unchanged); §15 "`[FigureGrab] HeldScale`
  kept bound as LEGACY" — the `HeldScale` family was deleted in the 2026-08 dead-settings sweep
  (`FigureGrabConfig.cs:376-385`); §8 "Where: `CloneRenderersSharingBones`" → F-24.
- **Figure ↔ prop pairs (the user's standing complaint):** SHARED already — `HeldPoseMirror`,
  `HeldGlideMath`, `FigureStretchMath`, `HeldPoseReport`, `WalkInHighlightEdges` (R10 is CLOSED:
  `TickHighlightMode` no longer exists), `FigureHighlight.Apply`, `FigureOverlay.BuildFrozenGhost`
  + `FigureGhosts.GhostTint`, `VRInteractables.IsUsablePickShape`, `PickRadiusRealMeters`,
  `PickExitFactor`, `StretchTarget`. DELIBERATELY SEPARATE with the reason at the site —
  `HeldFigures`/`NetHeldFigures` (`HeldFigures.cs:337-344`), `HeldProps`/`HeldFigures`
  (`HeldProps.cs:12-25`; a generic `OrderedHeldSlots<,>` would put a generic instantiation on a
  ~3000×/rescan `Owns` path), `NetHeldProps`/`NetHeldFigures` (`NetHeldProps.cs:571-581`, id- vs
  reference-keyed), `PropHeldPose` vs `FigureGrabConfig` dials (audit §4.15), the two elections
  (audit §4.13, quoted at `WalkInHighlightEdges.cs:11-15`), `PropGhosts` vs `FigureGhosts`
  registries (`PropGhosts.cs:15-27`). STILL PARALLEL and not mergeable without a design: the
  grab/release/glide/restore lifecycle (~250 lines each, actor-vs-visual-root, stat panel vs info
  card, layer parking, Apparance freeze) — a `HeldTransformState` composition would be the shape;
  recorded, not proposed.
- **`check-mirrors.sh` PART 3:** `FigureBusy.cs:334-337` carries all three rules-engine terms; the
  two `~` marks' reasons hold (grab-start asks the flow only; the watchdog calls
  `FinalizeAttackFlow`, which has no `IsAnimated` equivalent). Do not add `IsAnimated`.
- **Instrument writes:** none load-bearing (D-08/25/28/33 — every `Log*/Emit*/Announce*` writes
  its own budget/latch; `loadbearing.py`'s `EnemyInfoPhaseSkip.LogOnce._logged ← FigureClothHands`
  row is a bare-identifier name collision). The empty FigureGrab section of
  `INSTRUMENT-WRITES.baseline` is correct.
- **Named starting points declined:** `FigureGrabConfig.Bind` (cfg order = statement order;
  `PropHeldPose.Bind` is already the one split that bought something), `ActorPropBody.LeafOf`
  (memoised, 1 s retry, symmetric hold/release), `PropGrab.Scan` (~110 code lines, one HW-VERIFY
  string), `EmitPhotometer` (F-27). `StretchCaptureWatch.HandState_Captured_Note` (unused private
  const) is a documented doc-anchor, same shape as `HandRig.PalmNormal` — keep.
- **Not found:** per-frame `FindObjectsOfType` (three arm-time sites, ≤3/session); a cadence
  advanced only on the log branch; a fake-null dictionary key; an exception path that latches;
  a bound-but-unread `[FigureGrab]` key; a `Bind` description stating a wrong default.

<!-- §5 body appended by the integrator from sub-reader E's file (25 files read whole, 11 450 lines); the "pending" heading above is superseded. -->

## 5. `Board/` root + `Board/Patches/` — sub-reader E, integrated

All 25 files read whole. E's IDs in brackets; E's full file is committed beside this one as
`REVIEW-worldui-frame-subreader-E.md`. **No Tier-3 defect (a wrong output from a stated input) was
found in this set** — every Tier-3 item below is a log-tier promotion or a one-line reset, and none
changes a value, an order, a patch target or a wire byte.

### F-31 [E-14] — `CharacterFocus.Reset()` never clears `_lastOwnershipCensus`, so the one HW-VERIFY falsifier this file has can stay silent for a whole second scenario
- **File:line:** `Board/CharacterFocus.cs:2032-2047` (`Reset`) vs `:1328` (`_lastOwnershipCensus`),
  `:1417-1419` (the change gate), `:1421-1431` (the `HW-VERIFY` `[Ownership] HAND FAN` Note).
- **Class:** risk-gap. **Tier:** 3 (one assignment). **Act.**
- **Evidence:** the census is change-gated on a signature of {actor name, online,
  `IsUnderMyControl`, answerable, byList, claimants, localId, readOnly, duplicate} and nulled only
  on the "no hand" branch (`:1364`). `Reset()` ("scenario teardown, session end, module shutdown")
  clears every other latch — `_loggedFocusId`, `_loggedFloorId`, `_lastRefusal*`, `_followedTurnId`,
  both peer dictionaries — but not this one. Second scenario in one session, same party, same
  assignment ⇒ the first signature equals the last of the previous scenario ⇒ the line its own doc
  calls THE FALSIFIER for the 2026-09-07 duplicate-claimant report does not print for that scenario
  at all, and a missing falsifier reads exactly like "the duplicate did not happen".
- **Proposed action:** add `_lastOwnershipCensus = null;` to `Reset()` beside `_loggedFloorId = null;`.
- **Guard:** `CHANGED` confined to `CharacterFocus.Reset`.
- **Hardware line:** the `[Ownership] HAND FAN` census now prints once per scenario, not once per
  session-with-the-same-assignment.

### F-32 [E-19] — `EnemyInfoPhaseSkip` presses the host's "Fortfahren" (a `ConfirmAction` on the wire) and records it only at the debug tier
- **File:line:** `Board/Patches/EnemyInfoPhaseSkip.cs:258-274` (the SKIPPED branch → `LogOnce`,
  `VRLog.Info` at `:368`), `:406` (`the empty-phase skip threw and was DISARMED`, `Warn`, once);
  and `Board/Patches/PickPhaseInitiativeTrack.cs:534` (`the decision-flow track refill threw and is
  backing off for 60 s`, `Warn`, once).
- **Class:** risk-gap. **Tier:** 3 (tier promotions). **Act.**
- **Evidence:** the SKIPPED branch calls `button.OnClickInternal()`, which runs
  `Synchronizer.SendGameAction(GameActionType.ConfirmAction, …)` for the whole table (class doc
  `:61-68`). A mod-initiated phase advance that a default-level log cannot show is unattributable
  when the user asks why the enemy-info screen vanished. One line per reveal at most (`_logged`),
  host only. `:406` and `PickPhaseInitiativeTrack:534` are self-disarms — `VRLog`'s own definition
  of `Error`.
- **Proposed action:** emit the SKIPPED line through `Note` (minimal form: a `bool shipped = false`
  parameter on `LogOnce`, `true` at the one call site; the three no-action verdicts at `:193/:212/
  :224` stay `Info`); `:406` and `PickPhaseInitiativeTrack:534` `Warn` → `Error`.
- **Guard:** `CHANGED` confined to `EnemyInfoPhaseSkip` (+ `PickPhaseInitiativeTrack`).

### F-33 [E-04] — `BoardPing`'s whole "SILENT-GATE FIX" chain, including two self-disarms, prints nothing at the shipped level
- **File:line:** `Board/BoardPing.cs:111, :119` (`[Ping] press rejected —`, `Info`), `:171`
  (neither `UIScenarioMultiplayerController` nor `PingManager`, `Warn`), `:185` (`game ping call
  threw`, `Warn`), `:253` (`VR pings stay LOCAL-ONLY`, `Warn`), `:259` (`hex ping disabled`, `Warn`).
- **Class:** risk-gap. **Tier:** 3 (tier promotions). **Act.**
- **Evidence:** class doc `:36-39`: "every rejection between the A-press and the actual ping call
  used to be a silent return, which made 'the joining peer cannot ping at all' undiagnosable from
  logs. A press is an explicit user action now: each rejected press logs its reason exactly once."
  At the shipped level the chain is exactly as silent as before the fix (this file is 0 shipped /
  10 debug). `:253`/`:259` are feature self-disarms, i.e. `Alert` by definition.
- **Proposed action:** `:259`, `:253`, `:171` → `Alert`; `:185` → `Error`; `:111`, `:119` → `Note`
  (one line per rejected press). `[Ping]` token untouched.
- **Guard:** `CHANGED` confined to `BoardPing`.

### F-34 [E-01] — `BoardClickDriver.DecideTap` swallows a throwing target test at `Warn`
- **File:line:** `Board/BoardClickDriver.cs:366-371`.
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (`Warn` → `Error`).**
- **Evidence:** a fingertip tap whose decision table threw is silently turned into a click and the
  default log carries nothing — the redundancy-audit §6.3 shape ("a swallowed throw must never be
  silent"), which was fixed to `Alert` in `EscMenuShowSafety` in ModBuild 439. Bounded by taps.
- **Guard:** `CHANGED` confined to `BoardClickDriver`.

### F-35 [E-15] — The character-focus evidence chain is entirely at the debug tier
- **File:line:** `Board/CharacterFocus.cs:1089` (`switch REFUSED`), `:694` (`FOCUS PIN engaged`),
  `:1715` (`SELECTION GUARD`), `:1051` (`now looking at`); also `:1989/:2018` (`[Focus] peer cue`),
  `Board/FocusDriver.cs:396/:429` (`[Focus] the game is waiting on`).
- **Class:** risk-gap. **Tier:** 3 (tier promotions). **Act on the four named; defer the two
  observer-side ones to the integrator's volume call.**
- **Evidence:** every line's own doc names it as hardware evidence — `LogRefusal` `:1071-1077`
  ("Format is fixed by the 2026-08-08 ruling … grep the log for `switch REFUSED`"), `LogFloor`
  `:1700-1703`, `LogPeerCue` `:1973-1978` ("so a hardware round can be read from BOTH machines"),
  `FocusDriver.TickAttentionLog` `:375-380`; `LocalFloorHand`'s evidence paragraph `:1632-1639`
  quotes `[Focus] cleared` / `now looking at` lines read off `LogOutput.log` — lines that have not
  appeared in a default log since the 2026-08-30 tier re-decision. All edge- or change-gated (the
  refusal additionally rate-limited to 5 s): tens per session, inside `Note`'s stated budget.
- **Proposed action:** `Info` → `Note` at `:1089`, `:694`, `:1715`, `:1051`. Leave the four
  `AUTO-FOLLOW` variants, `cleared`, the MR palette pair and `LogFocusOnce` at `Info`.
- **Guard:** `CHANGED` confined to `CharacterFocus`.

### F-36 [E-05] — Two lines INVARIANTS §15 lists as hardware grep tokens are at the debug tier
- **File:line:** `Board/HexHighlightFix.cs:663` (`stable hex decal ZTest=`), `:363` (`HEX PROJECTOR
  guard [install]`).
- **Class:** risk-gap. **Tier:** 3 (two words). **Act (`Info` → `Note`).**
- **Evidence:** `INVARIANTS-Hands-Board-Core.md:2267` lists the first under "log lines that are
  grep tokens, not debug residue"; both docs (`:657-659`, `:288-289`) say the next hardware log
  needs them. Both are once-per-session (a `_lastLoggedZTest` latch; `MaxProjectorLogLines = 4`).
- **Guard:** `CHANGED` confined to `HexHighlightFix` (nested patch type for `:663`).

### F-37 [E-17] — `PingNameTag_Patch` self-disarm and swallowed throw at the debug tier
- **File:line:** `Board/Patches/PingNameTag.cs:53` (`ping name tags disabled`, `Warn`), `:68`
  (`name-tag postfix failed (suppressed)`, `Warn`), `:452` (`game tooltip clone unavailable`,
  `Warn`, once — its own doc says "the design regression should be visible in logs").
- **Class:** risk-gap. **Tier:** 3. **Act (`:53` → `Alert`, `:68` → `Error`, `:452` → `Note`;
  leave the two reflection fallbacks at `:601/:606`).**
- **Guard:** `CHANGED` confined to `PingNameTag_Patch` / `PingNameTag`.

### F-38 [E-02] — The two fingertip hardware-evidence lines print nothing at the shipped level
- **File:line:** `Board/BoardClickDriver.cs:565-567` (`LogTapDecision`), `:637` (`LogTouchCommit`,
  un-throttled branch).
- **Class:** risk-gap. **Tier:** 3. **Act (`Info` → `Note` on both; the throttled repeat at `:632`
  stays; `FINGERTIP PING:` at `:592` is redundant with the `[Tap] → PING` line and stays).**
- **Evidence:** both docs call themselves the hardware proof of the fingertip rule
  (`:554-558`, `:614-618`); the 2026-09-03 report they answer ("Der 'Mit der Fingerspitze
  auswählen' Test ist nicht erfolgreich obwohl ich mit der Fingerspitze einen Ping ausgelöst habe")
  was diagnosed by reasoning because the proof line is invisible. Bounded by the commit edge plus a
  0.15 s cooldown per hand.
- **Guard:** `CHANGED` confined to `BoardClickDriver`.

### F-39 [E-20/E-03/E-07] — Four `Reset()` bodies leave a one-shot latch set across a scenario or a hot reload
- **File:line:** `Board/Patches/EnemyInfoPhaseSkip.cs:125-131` and
  `Board/Patches/PickPhaseInitiativeTrack.cs:342-349` (neither clears `_reportedThrow`, so the
  SECOND scenario that throws never reports it — `a-held-instrument-reads-as-dead`);
  `Board/HexHighlightFix.cs:456-502` (`_knobsBypassLogged`); `Board/AoeControl.cs:91-95`
  (`_stickHand`, `_loggedClaimThrow` — but `Reset()` is ALSO called on a stick-hand change at
  `:179`, which relies on not clearing them, so this one needs a separate shutdown path, not a
  change to `Reset`).
- **Class:** risk-gap (minor). **Tier:** 3. **Act on the three that are unambiguous
  (`_reportedThrow` ×2, `_knobsBypassLogged`); leave `AoeControl` (a change there would re-emit an
  edge line on every hand change — the finding is recorded, the fix needs a second method).**
- **Guard:** `CHANGED` confined to the three types' `Reset`.

### F-40 [E-11] — The rectangle FALLBACK board frame is never adopted into the furniture band
- **File:line:** `Board/FocusDriver.cs:610-617` (`PlayTray.AdoptFurniture(_boardFrame?.RootObject)`
  runs before the `_rectFrame = WorldFrame.Build(...)` fallback and is passed null on that path),
  `Board/FocusCue.cs:533-537`.
- **Class:** risk-gap. **Tier:** 3. **Defer** — reachable only when the bundled board asset is
  missing (a degraded install), and whether the fallback board has a furniture band at all is a
  design question. `BoardFrame.cs:26-54` states the mechanism the fallback misses (a transparent
  renderer at `sortingOrder 0` is painted before every converted panel ≥ 100).

### F-41 [E-22] — Two unreferenced accessors, one with a doc that names a caller that does not use it
- **File:line:** `Board/BoardFrame.cs:407-410` (`internal MeshRenderer? Renderer`),
  `Board/FocusCue.cs:533-537` (`internal Renderer[] Renderers`).
- **Class:** dead + doc-drift. **Tier:** 0. **Act on the doc; leave the members.**
- **§5 checklist (run by E):** neither is a Harmony target, Unity message, serialized field,
  reflection/`nameof`/string reach, config key, log token, debug-menu reference or `tests/` pin;
  neither is the only writer of an instrument-read field. Callers: `BoardFrame.Renderer` 0 outside
  its file (`FocusDriver.cs:610` and `Net/Remote/RemoteFocusOutline.cs:88-95` both go through
  `RootObject`); `WorldFrame.Renderers` 0.
- **Proposed action:** `BoardFrame.Renderer`'s doc claims it exists "for the caller that must seat
  it on a draw-order ladder (the LOCAL board registers it as furniture …)" — false about the
  mechanism: the SUBTREE is registered. Correct the sentence. Keep both members (the Batch-D
  precedent for documented one-line API statements); `WorldFrame.Renderers` is the natural seam if
  F-40 is ever acted on.

### F-42 [E-06] — `HexHighlightFix`'s class doc still calls three constants "config" and speaks of "live config edits"
- **File:line:** `Board/HexHighlightFix.cs:28`, `:47-50`, `:52`, `:617-618`, `:651` against
  `:170/:183/:194` (all three `private const` since the 2026-08-22 settings audit, stated at
  `:150-158` and `:222-227`) and `:657-659` ("ONE line per session now, not one per change").
- **Class:** doc-drift. **Tier:** 0. **Act.** **Guard:** empty.

### F-43 [E-13] — STALE-DOC-REFS `CharacterFocus.cs:935 PinRefusal` — cleared
- **File:line:** the demotion now sits at `Board/CharacterFocus.cs:1076`.
- **Class:** doc-drift. **Tier:** 0. **Act (and delete the row from `STALE-DOC-REFS.md`).**
- **Evidence:** `PinRefusal` never existed as a member (it arrived as prose in `3087dd30`,
  ModBuild 140). The job is split by design (`:642-644`) across `PinRefuses(CPlayerActor wanted,
  out CPlayerActor? pinned)` (`:646`, the allocation-free predicate) and `PinReason(CPlayerActor?
  pinned)` (`:653`, the sentence `LogRefusal` prints).
- **Corrected sentence:** `(<see cref="PinReason"/>, minted when <see cref="PinRefuses"/> says no —
  the actor-dependent one, bounded by a live hex pick belonging to one of this player's characters)`.

### F-44 [E-21] — INVARIANTS §7's select-guard RULE is superseded, and §13's Board table predates eight registered classes
- **File:line:** `Board/Patches/SelectionGuardPatches.cs:98-104` (the focus branch returns false
  FIRST), `:153-168` (the original action-phase reject, now reachable only for an exhausted hero's
  stale portrait) vs `INVARIANTS-Hands-Board-Core.md:1122-1139`.
- **Class:** doc-drift (planning doc — NEEDED-OUTSIDE). **Tier:** n/a.
- **Evidence:** since the 2026-08-08 free-focus ruling a laser click is first offered to
  `CharacterFocus.TryFocus` as a read-only VIEW change; the guard's reject is the fourth branch.
  The patch target and the "Breaks if" veto are unchanged. §13's table lacks `HoverPickPatch`,
  `ProjectorModifier_Awake_Patch`, `InteractabilityManager_PortraitFocusBypass`,
  `Choreographer_TileHandler_OwnershipGuard`, `AllCardsViewerBlock`,
  `CharacterManager_OnControlReleased_Fallback`, `PingNameTag_Patch`,
  `InitiativeTrack_ShowMonsterClasses_ArmSkip`/`_Update_TickSkip`; `docs/PATCH-INVENTORY.md` is the
  current inventory.

### F-45 [E-08/E-09/E-10/E-12/E-16/E-18/E-23] — verified, and what was NOT found
- **Redundancy audit rows CLOSED at HEAD:** R14 (`PingNameTag` unranked) — the two-line fix is IN
  (`PingNameTag.cs:511-528` ranks both routes through `Net.BoardVisual.OrderWithPanels` every Tick;
  the audit's `rg -c` = 0 is stale). R30(a) / §6.4 (`FocusDriver.Carrier`) — now
  `TickGuard.NoteThrow`, shared store, shared 10 s window, `Error` tier. R42
  (`SelectionReadyHighlighter`) — now a `VRLogThrottle(30 s heartbeat)` with the hash prefilter
  correctly OR'd with `HeartbeatDue`. No NEEDED-OUTSIDE for R14 after all (E-23:
  `BoardVisual.cs:158`'s exemption list is for subtrees under a board root; a ping tag is a free
  GameObject at the hex and is never walked by it).
- **Instrument writes:** none load-bearing — every "outside" reader `loadbearing.py` names is a
  RESET (`CharacterFocus.Reset`, `ResolveHand` re-arming `_loggedFloorId`, `BoardClickDriver.Reset`,
  `HoverPickPatch.Reset`, `HexHoverClear`'s InScenario-false reset, `SelectionReadyHighlighter.Tick`)
  or a bare-identifier name collision. Nothing for `INSTRUMENT-WRITES.baseline`.
- **Gates run read-only, all PASS:** `check-mirrors.sh` (41 mirrored-constant groups agree, 7
  shared-expression groups, 2 subset-guard groups — including the `PokeInteractor` ↔
  `BoardClickDriver` 0.008/0.02 pair, whose cross-reference comment is present at
  `BoardClickDriver.cs:110-115`); `check-mirror-dials.py` (8 reads, 0 owner violations, the two OPEN
  rows are not in `Board/`); `check-hw-verify.py` (555 marked lines, all at a shipped tier);
  `docs/PATCH-INVENTORY.md` rows 41-64 agree with the source and every Board patch class is
  registered exactly once.
- **Invariants verified true at HEAD:** §6 `BoardDriver.Update` (marker at `:37` byte-identical to
  the lock); §7 in full — the MF choke point, `BoardPick` frame-memoisation and lazy compute,
  `InScenario` ≠ `Active`, near-beats-far, the 3 cm origin lift, the 1000f far re-raycast, the
  off-screen cursor, the tile-GameObject centre, the `IsPointerOverUI` predicate identical to
  `TickFar`'s, the CommonLoop postfix's verbatim bookkeeping and always-consume, the pending
  click's self-expiry, the near-click suppression, placement armed under the three game predicates,
  `CameraArrivalGuard`, AoE (repeat ≥ 0.3, melee unhandled, redraw matched), `TargetingUx`,
  `HexHoverClear`, the select guard's seam, `BoardPing`, `SelectionReadyHighlighter`; §9 TickGuard
  at every driver; §10 `HexHighlightFix` in full; §13's listed rows; §14.1 level-not-latch and
  §14.3 fake-null discipline; §15's KEEP list (`PlacementDiagnostics` decision note confirmed at
  `:12-34` and NOT re-raised, `TargetingUx._suppressionLogged`, `BoardPick.TryGetCursorWorld`).
  Two entries have MOVED ON without breaking: `BoardPick`'s outer gate is now
  `VRModeStateMachine.ScenarioBoardExists` (ModBuild 178), and AoE's stick is the NON-turning hand
  (`ResolveRotationHand`, ModBuild 138) rather than `VRHands.Primary`.
- **Not found:** no per-frame `FindObjectsOfType` (the one sweep is event-driven); no fake-null
  dictionary hazard; no exception path that latches (every catch fails open to the vanilla answer
  and consumes its one-shot); no cadence advanced only on the log branch; no 1:1 breach (every
  peer-side derivation reads the OWNER's record); no parallel construction (three tile resolutions
  examined and rejected as deliberately different, mirroring `Controller.LateUpdate` verbatim; two
  row-visibility helpers are different pools by design; `SelectionOwnershipFallback.LocallyOwned`
  vs `CharacterFocus.IsForeign` are one term and its negation, each with its own reason paragraph
  — a 3-line helper buys nothing); no unread config key and no `Bind` description stating a wrong
  default. `FocusCue.cs` holds three top-level types (`FocusCue`, `UiRing`, `WorldFrame`) — a free
  empty-diff motion, **not** recommended (CHARTER §2: the class doc names both builders).

<!-- §6 body appended by the integrator from sub-reader A's file (27 files, 25 904 lines); the "pending" heading above is superseded. Sub-reader A's own file is committed verbatim beside this one as REVIEW-worldui-frame-subreader-A.md, including its coverage confession (A-26) and its empty searches. -->

## 6. `WorldUI/Conversion/` + `WorldUI/Materialise/`

`Conversion/` is 22 files / 22 067 lines (not the 28 the brief names — `CanvasConversion` is eleven
partials `.1` … `.9g`), `Materialise/` is 5 / 3 837. `CanvasConversion.3.Fit.cs` alone is 6 941.
Findings F-46 … F-63; `[A-nn]` cites the sub-reader file.

### F-46 [A-05/A-10] — DEFECT: the panel ladder's new bound check accuses a by-design offset, at a printing tier, once per session
- **File:line:** `WorldUI/Conversion/CanvasConversion.8.Order.cs:300-314` (`CheckFollowerOffset`,
  `offset >= 0 && offset < PanelOrderStep`) and `:355` (`RegisterOrderFollower(…, Renderer, …)`
  calls it unconditionally) vs `WorldUI/Materialise/WindowMaterialiseDebris.cs:113`
  (`private const int DebrisBehindOrderOffset = -1;`) and `:519` (the registration).
- **Class:** defect (+ parallel construction of "what is a legal follower offset"). **Tier:** 3.
- **Evidence:** the debris cloud is deliberately split into two renderers *"at offset +1 and -1.
  The window is drawn between them"* (`WindowMaterialiseDebris.cs:85-108` — a converted panel
  writes no depth, so `sortingOrder` is the ONLY way to put geometry behind it). `-1` fails the
  bound, so every session in which a window materialises with debris on emits, at `Alert` and
  marked `// HW-VERIFY`, `PANEL ORDER FOLLOWER OUT OF BAND: 'Behind' registered at offset -1 …
  Nothing is clamped: the fix is the offset.` The offset is then applied anyway (`:719`). It is
  **not** an unseen-caller miss: `PanelOrderStep`'s own doc at `:158-166` enumerates the ten
  follower literals and names *"`WindowMaterialiseDebris`'s +/-1"*, and the very next sentence —
  *"2 < 4 < 10 < 12 < 16 is consistent today"* — silently drops the `-1` it just listed. The
  contradiction is inside one commit (`756bda65`, the R41 remedy). And the bound is not vacuous:
  the furniture band occupies `slot-5 … slot-2` (`.9.Furniture.cs:109/:120/:248`) and `slot-1` is
  reserved by `:113`'s doc for the free-floating plates `Net.BoardVisual.OrderWithPanels` resolves,
  so the debris behind-half **ties** with that reserved slot.
- **Proposed action — a FINDING, not a commit (BRIEF §1: it needs a design decision).** Three
  resolutions: (a) widen the bound to accept `-1` and concede the tie (touches
  `Net/Board/BoardVisual.cs`, lane **net**); (b) widen `FurnitureBandWidth` 5 → 6 and give the
  debris its own slot (moves a number, and a draw order); (c) a named `allowBehind` exemption
  parameter on the `Renderer` overload, defaulted false, passed true from the one materialise site.
  **A and the integrator both recommend (c)**: it is the only one that changes no number and no
  draw order — it removes a false alarm and documents the real, narrow, never-observed tie at the
  exemption site instead of shouting it at the player once a session. Both files are in this lane.
- **Guard:** for (c), `CHANGED` confined to `CanvasConversion` and `WindowMaterialiseDebris`.

### F-47 [A-12] — DEFECT: two unrelated exception paths share ONE report latch
- **File:line:** `WorldUI/Conversion/CanvasConversion.3.Fit.cs:5981`
  (`private static bool s_hitRectFaultLogged;`), set at `:6082` (`TryMeasureDrawnContent`) and at
  `:6464` (`TickHitRect`).
- **Class:** defect / risk-gap (`a-cap-that-goes-silent`). **Tier:** 3. **Act.**
- **Evidence:** the two catches have different consequences and each says *"Logged once per
  session"* — the arc-booking one (*"the map room's arc reservations fall back to the HOST RECT"*)
  and the input one (*"Windows that draw outside their own frame simply stay unclickable there"*).
  Sharing one latch means whichever throws first permanently silences the other, and the silenced
  one is the player-reportable failure. Both are also `VRLog.Warn`, i.e. invisible at the shipped
  `LogLevel = Info`.
- **Proposed action:** split into `s_drawnContentFaultLogged` + `s_hitRectFaultLogged`, one per
  verdict class; both texts byte-identical (no grep token reworded). Promote the INPUT one to
  `Alert` (latched to one line per session, names an unclickable window).
- **Guard:** `CHANGED` confined to `CanvasConversion`.

### F-48 [A-01] — the only FAILING branch of the "Quest verwerfen" deadlock fix is silent at the shipped level
- **File:line:** `WorldUI/Conversion/CanvasConversion.1.Core.cs:576-581` (`VRLog.Warn`,
  `HOST SCENE PIN FAILED`) against its two siblings `:549` `HOST SCENE PINNED` and `:566`
  `HOST SCENE PIN SKIPPED`, both `Note` and both `// HW-VERIFY`.
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (`Warn` → `Alert`, add `// HW-VERIFY`).**
- **Evidence:** the comment above the `try` says *"Failure is never fatal: the worst case is the
  pre-fix behaviour, and it is logged."* It is logged only at `LogLevel = Debug`. The failure it
  guards is the hard deadlock documented at `:266-292` — the worst class of bug this subsystem has
  shipped. **Guard:** `CHANGED` confined to `CanvasConversion`, one call site.

### F-49 [A-04] — the reveal deadline's FORCED branch prints nothing at the shipped level
- **File:line:** `WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs:202` (`VRLog.Warn`,
  `MODAL REVEAL: '…' FORCED after {waitedMs} ms`); the settled sibling at `:178` is `Info` and
  **stays** (it fires once per float).
- **Class:** risk-gap. **Tier:** 3. **Act (`Warn` → `Alert` at `:202` only).**
- **Evidence:** the line's own text says the window *"may show one visible correction"* and that
  *"any pose re-place still pending is now permanently SKIPPED"* — the exact "window appears in the
  wrong place / snaps" symptom the reveal gate exists for; and `TickRevealGate`'s doc already says
  *"a `Warn` names what was still pending"*, i.e. the doc believes this line is heard.
- **Judgement:** on a rig that habitually misses the 600 ms budget this gets busier — that is the
  condition the line exists to surface. **Guard:** `CHANGED` confined to `CanvasConversion`.

### F-50 [A-09] — the one-eye visibility-write detector is silent at the shipped level
- **File:line:** `WorldUI/Conversion/CanvasConversion.6.Hide.cs:147` (`AssertNotInRenderPhase`,
  `VRLog.Warn`, latched by `s_renderPhaseViolationLogged`, appends `Environment.StackTrace`).
- **Class:** risk-gap. **Tier:** 3. **Act (`Warn` → `Alert`).**
- **Evidence:** it detects the `aliasing-is-per-eye` class (a visibility write landing between the
  two MultiPass eye passes) and is written to be read off a hardware log. The rule it violates is
  stated 120 lines below it in the same file (`:265-269`): *"AND SAY SO AT A TIER THE SHIPPED
  DEFAULT PRINTS. … reading it here would repeat exactly the mistake that made the 392 round
  unreadable."* The latch bounds the cost at one line per session.
- **Guard:** `CHANGED` confined to `CanvasConversion`.

### F-51 [A-13] — `.3.Fit.cs`: 28 of 31 `VRLog` calls are at a tier the shipped log drops — the ranked judgement list
- **File:line:** `WorldUI/Conversion/CanvasConversion.3.Fit.cs`. The three PRINTING lines all carry
  `// HW-VERIFY`, so nothing marked is mis-tiered; the gap is in unmarked lines whose own prose
  claims a user-report role.
- **Class:** risk-gap (bundled). **Tier:** 3 (each is one word). **Act on 1-3; 4-5 integrator's
  call; leave the remaining nine.**
  1. `:5616` `MODAL WINDOW: '…' pre-reveal first fit found NOTHING MEASURABLE by its reveal
     deadline` — latched per panel; its text ends *"If this window is a SHARED one its half-size is
     a term of the shared anchor, so this line is the reason its home looks wrong"* — a 1:1
     standing-ruling surface.
  2. `:2934` `FIXED FIT CONCEDED` — latched per window; names the 2026-08-22 report's symptom.
  3. `:6465` `HIT RECT: … swallowed` — see F-47; names an unclickable window; latched.
  4. `:5010` `one-shot fit is INCONSISTENT ACROSS OPENS` — the text asks the reader to *"Report
     this line"*, which a player at the shipped level cannot do.
  5. `:5172` `one-shot fit REJECTED by the validity check` — capped at 2 corrections per open.

  Leave `:4722`, `:4845`, `:4908`, `:4930`, `:5211`, `:5291`, `:5322`, `:5708`, `:6083` (per-frame
  capable or hunt commentary). **No text changes anywhere.**
- **Guard:** `CHANGED` confined to `CanvasConversion`, N call sites.

### F-52 [A-15] — R27 is CLOSED, but the same defect survives one constant pair over, with a false reason
- **File:line:** `WorldUI/Conversion/PanelInkBounds.cs:176-180` (`PlateWidthFraction = 0.80f`,
  `PlateHeightFraction = 0.95f`) vs `CanvasConversion.3.Fit.cs:1843-1844`
  (`private const float FixedFitPlateWidthFraction/…HeightFraction`, same values).
- **Class:** duplication + doc-drift. **Tier:** 2. **Act.**
- **Evidence:** R27 itself is FIXED and must not be re-raised — `PanelInkBounds.cs:193` reads
  `private const float FaintAlphaFloor = CanvasConversion.FitMinAlpha;`, a real compile-time
  reference, and `scripts/check-mirrors.sh:177-180` carries the account. The plate pair beside it
  is the row R27 did not cover, and its stated reason — *"Restated rather than referenced because
  those are private to another lane's file"* — is **false**: both files are
  `WorldUI/Conversion/`, one namespace, one assembly, and per BRIEF §2 one lane. The drift is
  load-bearing: `Ink.Plates` became a handle in ModBuild 447 and decides, through
  `GrabBarLayout.SolveSpan`, whether the grab bar's width comes from the frame or from the union
  (`PanelInkBounds.cs:88-100`).
- **Proposed action:** `.3.Fit.cs:1843-1844` `private` → `internal`; `PanelInkBounds.cs:178-179`
  reference them; rewrite the doc at `:176-180`. **No value changes** — a `const` reference folds
  to the identical literal.
- **Guard:** name-only `CHANGED` on `CanvasConversion` + `PanelInkBounds`. **Any numeric
  difference means the change is wrong — revert.** That is the check.

### F-53 [A-25] — "BY VALUE, because it is private": one shape, four sites, three of them a fiction
- **Class:** duplication + doc-drift. **Tier:** 2. A grep for `BY VALUE|Restated rather than
  referenced|restated here because|private to another` over the set returns exactly these.

  | # | copy | source | same lane? | status |
  |---|---|---|---|---|
  | 1 | `PanelInkBounds.cs:193 FaintAlphaFloor` | `.3.Fit.cs:82 FitMinAlpha` (`internal`) | yes | **FIXED** — a real reference. Only the class comment at `:81` still calls it a borrowing "with the same standing risk of drift". Doc fix. |
  | 2 | `PanelInkBounds.cs:178-179` plate fractions | `.3.Fit.cs:1843-1844` (`private`) | yes | **LIVE COPY** — F-52. |
  | 3 | `PanelInkBounds.cs:936-950` signature tail | `Sharpness/PanelSupersample.4.Content.cs:2048-2073` (`private`) | yes | **LIVE CLONE** — F-54. |
  | 4 | `.3.Fit.cs:5946 HitRectShrinkDeadBandPx = 32f` | `Grab/GrabbableModal.cs:420 InkReleaseDeadBandPx = 32f` (`private`) | yes | **LIVE COPY.** |
  | — | `PanelInkBounds.cs:834` (a form of `LoadoutConfirmPark`'s) | `WorldUI/Composites/` | **NO** | a real boundary (lane worldui-front) — correctly left a copy. |

- **Why #4 is the sharpest:** `GrabbableModal.cs:336` says *"NOTHING WAS TRADED AWAY:
  `InkReleaseConsecutive` is still 3 and `InkReleaseDeadBandPx` is still 32"* — a hardware-settled
  number with a named round behind it. `.3.Fit.cs:5946`'s own doc says the point is that *"all
  three instruments agree about what 'smaller' means"*, and nothing enforces it. Fix: `internal
  const` on `GrabbableModal.InkReleaseDeadBandPx`, then reference it. `WorldUI/Grab/` is in this
  lane but belongs to §7, so it lands there — **not a NEEDED-OUTSIDE entry.**
- **Guard:** name-only. Any numeric difference = revert.

### F-54 [A-16] — R7 resolved: the Sharpness twin is a DIFFERENT question; only the signature is one concept
- **File:line:** `WorldUI/Conversion/PanelInkBounds.cs:936-950` (`ComputeActiveSetSignature`) and
  `:727-735` vs `WorldUI/Sharpness/PanelSupersample.4.Content.cs:2048-2074` and `:1194-1210`.
- **Class:** parallel construction. **Tier:** 2 for the signature; **leave-alone** for the walks.
- **Evidence:** the signature tail is the same fourteen statements in the same order — the
  `NewPartyDisplayUI.PartyDisplay` fetch in a try/catch, the null guard, `sig = sig*31 +
  (int)display.ActiveDisplay`, then the SAME SIX `MixSubView` calls in the SAME ORDER. The INK
  walks are not: five terms differ by design (faint content REJECTED vs never cropped; full-frame
  plates EXCLUDED vs included; empty `TMP_Text` excluded vs included; memoised vs deliberately not
  — `PanelInkBounds.cs:471-481` says why; and the questions themselves are *"what does this window
  PAINT"* vs *"what must the CAPTURE FRAME cover"*). Merging the walks would re-create the ModBuild
  234/239 defects.
- **Contract handed to the Sharpness half (§8):** hoist ONLY the shared party-display tail into one
  `internal static` helper, leaving each caller its own part one (they differ — the ink one hashes
  the active set to `SignatureDepth = 2` with a `TransientFamilies` exemption and a `SigMemo`, the
  Sharpness one hashes direct children). **The order of the six `MixSubView` calls is load-bearing**
  (a rolling `sig*31` hash): change it and every window in both subsystems sees a phantom
  generation event. The guard cannot see that — diff the six-call order by eye.
- Also: `PanelInkBounds.cs:742`'s *"private to another lane's file"* is false the same way F-52's
  is (`:834` is correct and stays).

### F-55 [A-08] — `UIWindow._disableCanvas` is read two ways in one partial type, and the reflective one's stated reason is false
- **File:line:** `WorldUI/Conversion/CanvasConversion.6.Hide.cs:661-698` (`ReadsDisableCanvas`, via
  a cached `AccessTools.Field`) vs `CanvasConversion.4.Lifecycle.cs:236` (a direct field read).
- **Class:** parallel construction + doc-drift. **Tier:** 2 → downgraded to **0, comment only.**
- **Evidence:** `.6.Hide.cs:661-664` says *"There is no public accessor, so the field is read once
  through a cached `FieldInfo`"*. False about this build: `GloomhavenVR.csproj:118` publicizes
  `GH.Runtime`, and `.4.Lifecycle.cs:236` reads the same private field directly with a green build.
- **Proposed action — the COMMENT, not the code (a negative result).** The reflective path is
  genuinely the better one: its failure answer (`false` ⇒ "restore the canvas") is the safe
  direction, whereas a `MissingFieldException` out of `Release`'s hot path would abort a release
  mid-way and leak a host. Say that instead. **If** the integrator ever unifies, the direction is
  toward the reflective reader, never the direct one — and that is a behaviour change on a patched
  game, so Tier 3 and the user's call. **Guard:** empty.

### F-56 [A-19] — dead-code sweep of the set: four accessors dead, one kept-and-documented, one INERT
- **Class:** dead. **Tier:** 0. BRIEF §5 items 1-8 run on each, plus `git log -S`.
- **(a) `CanvasConversion.6.Hide.cs:569 RevealWithheldCanvases` / `:572 RevealWithheldName`** —
  no reference anywhere in `src/` or `tests/`; not a Harmony target, Unity message, serialized
  field, reflection/`nameof` reach, config key, log token or debug-menu entry; the token the docs
  grep is the line text `REVEAL RESTORE WITHHELD`, which stays. Backing fields ARE live
  (`RevealWithholdClause`, `:578`). **Delete the two accessors, keep the fields.**
- **(b) `PanelPlacement.cs:859 HeadEyeHeight.TrackedCount` / `:862 UntrackedCount`** — same shape;
  their siblings `Comparisons` and `HaveMeasured` do have external readers, which is why only these
  two fell out. Backing `_tracked`/`_untracked` read at `:937-938`. **Delete the two accessors.**
- **(c) `CanvasConversion.9c.SubViewBurst.cs:233 SubViewSetInFlight`** — no caller; introduced by
  `9f42b347` together with the distinction its doc draws against `SubViewBurstRunning`
  (`two-fans-one-name`), and the consumer it names was never written. **KEEP and say so** — two
  lines whose whole value is that named distinction; deleting it invites the next author to
  re-derive the coarse predicate from the fine one, which is the failure the memory entry records.
- **(d) `CanvasConversion.4.Lifecycle.cs:2120 SetSoftLock` — NOT dead, INERT, and this is the
  interesting one.** It is the ONLY writer of `SoftLocks` (`.1.Core.cs:50`), and `SoftLocks` is
  read by a MECHANISM: `EffectiveLock => _uiLocked || SoftLocks.Count > 0` → `IsLockedNow` → six
  call sites in `Cards/`, `WorldUI/Surfaces/` (×4) and `WorldUI/Modal/`, plus the raycaster writes.
  **So `EffectiveLock`'s second term has been permanently false since `00c51010` removed the phase
  banner** — and the doc still describes that caller as live. **Keep the method, fix the doc**
  ("NO CALLER AT HEAD … not dead — INERT"). Deleting cascades into four members for a capability
  the module layer may want back, and `REVIEW-WorldUI.md:549` still lists it as core surface.

### F-57 [A-20] — `PanelPlacement.cs` declares THREE unrelated top-level types
- **File:line:** `WorldUI/Conversion/PanelPlacement.cs:28` (`PanelPlacement`), `:204`
  (`PanelPoseWatch`), `:827` (`HeadEyeHeight`).
- **Class:** structure-naming. **Tier:** 1. **Act (move `HeadEyeHeight` and `PanelPoseWatch` to
  their own files, verbatim, no member reordering).**
- **Evidence:** `HeadEyeHeight` is a head-tracking measurement instrument with its own session
  counters and describe string; it has nothing to do with panel placement beyond being consulted by
  it. The guard's snapshot already files it separately
  (`.guard/baseline/GloomhavenVR.WorldUI/HeadEyeHeight.cs`) — which is exactly what makes the move
  free. `ConvertedPanel.cs` has the same file shape but there the three structs are that class's
  own record types — **leave that one alone.**
- **Guard:** **empty** (CHARTER §3: "move a whole type to another file → nothing at all"). Anything
  reported means a field order moved and the commit must be reverted. `.csproj` globs, so no
  project edit.

### F-58 [A-21] — `CanvasConversion.3.Fit.cs` (6 941 lines): split verdict is SPLIT THE FILE, NEVER THE TYPE — and the recommendation is LEAVE IT
- **Class:** structure-naming (a recommendation of "leave it" is a finding). **Tier:** 1.
- **The four seams are real and contiguous:** A measure + general fit (`~1-1240`, `4432-5560`);
  B the fixed-size window (`~1770-4430`, `6876-6941`); C the hit rect (`~5820-6875`); D the
  conversion-frame guard (`~4617-4757`).
- **What forbids separate TYPES:** 52 mutable statics and 57 consts with real cross-seam sharing —
  `ClipperMemo` and `AuthoredOffsetMemo` are written by seam A and cleared by A, B **and** C under
  a single-threaded non-re-entrant contract (INVARIANTS-WorldUI §11); `TransientMemo` is declared
  in B and cleared by A (a backwards dependency); the 18 `s_last*` measure fields are written by A
  and read from A and B; `CornerScratch` is reused twice inside one call. As separate types that
  contract becomes a cross-type protocol no single diff can show.
- **If the integrator wants the file split** it is `.3.Fit.cs` (A+D) / `.3b.FixedFit.cs` (B) /
  `.3c.HitRect.cs` (C), all `internal static partial class CanvasConversion`, members verbatim.
  Care: `:3636 TransientFamilyNames = TransientFamilies.Names` (an initialiser reading another
  type's static) and `:1797 FixedFitWidthPx` (a const of consts, safe). Gate:
  `python3 scripts/check-partial-order.py`, green at HEAD.
- **Recommendation: LEAVE IT.** The seams are contiguous, each carries a region banner, and only
  five methods exceed 150 code lines — two are documented diagnostics and three are single decision
  procedures whose steps must be read in order. CHARTER §2's default answer applies.

### F-59 [A-02/A-17/A-18] — three `STALE-DOC-REFS.md` rows cleared
- **Class:** doc-drift. **Tier:** 0/1. **Act (fix each cref, delete each row).**
- `CanvasConversion.1.Core.cs:32` `Core.VREvents.UiLockChanged` — the symbol MOVED namespace, it
  did not die: alive at `Core/Events/VREvents.cs:238`, raised at `:264`. The cref failed only
  because this file's namespace is `GloomhavenVR.WorldUI`, so `Core.VREvents` does not resolve and
  `Core.Events.VREvents` does. The chain the sentence describes is exactly true at HEAD.
- `WindowMaterialiseDebris.cs:143` (now `:161`) `<c>Half</c>` — an enum replaced by the two `int`
  constants on the two lines immediately below it (`HalfFront = 0`, `HalfBehind = 1`), which are
  precisely the order the sentence describes. Row still valid, line moved.
- `WindowMaterialiseField.cs:17` `WindowMaterialiseDebris` — **true about the code and false about
  the symbol**: `WindowMaterialiseDebris.cs:73` declares `internal static partial class
  WindowMaterialise`, i.e. the file is named after its subject, not its type. The member is
  `WindowMaterialise.TryBuildDebris` (`:295`), the type it produces is `WindowMaterialise.DebrisCloud`.
  **Recorded, not proposed:** two of `Materialise/`'s five file names are not types, and
  `WindowMaterialiseVisibility.cs` declares two unrelated types and no type of its own — that is why
  this cref went stale, but CHARTER §2 says leave it.
- **Guard:** empty (doc comments are not in the ilspy snapshot).

### F-60 [A-23] — the shared registries have drifted against HEAD in four places, all naming files in this set
- **Class:** doc-drift. **Tier:** n/a (shared artefacts; recorded, and each names a file in my set).
- `INVARIANTS-WorldUI.md` §2, two entries ("invisible clippers must not stamp depth", "the depth
  mask is PER-GRAPHIC") — `CollectVisibleMaskRects`, `MaskMinAlpha` and the per-graphic depth quad
  **do not exist at HEAD**; the replacement is the essay at `.8.Order.cs:13-90` and its ruling *"NO
  DEPTH WRITING ANYWHERE BETWEEN PANELS — ORDER THEM INSTEAD."* Mark **SUPERSEDED, never delete** —
  they are the record of two shipped-and-failed attempts, and `.8.Order.cs`'s header depends on that
  history being findable.
- `INVARIANTS-WorldUI.md` §11, the fit/depth-mask pair — half of it is gone with the same cause;
  the surviving half (the fit clamps) is TRUE.
- `INVARIANTS-WorldUI.md` §11, `ClipperMemo` + the scratch buffers — the RULE holds (no coroutine,
  no `async`, no second driver anywhere in the set), but the inventory has drifted: it names
  `RectScratch`, gone, and gives the memo's second clearing site as `CollectVisibleMaskRects`, gone.
  At HEAD `ClipperMemo` is cleared at FOUR sites (`.3.Fit.cs:446`, `:757`, `:3400`, `:6534`) and the
  live buffer population has roughly tripled — the entry is worth MORE after the update, not less.
- `REVIEW-WorldUI.md` §4.1/§4.2 are **DONE** (`ConvertedPanel` and its three records are in their
  own file; `CanvasConversion` is eleven partials; `check-partial-order.py` exit 0), and **§7.3 is
  superseded** — the ladder it says has "no shared constant" has one plus a registration-seam check.

### F-61 [A-11] — the one `INSTRUMENT-WRITES.baseline` row for this set no longer meets the file's own admission test
- **File:line:** the row `CanvasConversion::s_orderHashNodes <- LogPanelOrder`; source at
  `.8.Order.cs:765` (write) and `:625` (read).
- **Class:** doc-drift (against the baseline, not source). **Tier:** n/a — reported.
- **Evidence:** the baseline admits an entry when *"something that is not a diagnostic reads a
  field it writes"*. The only reader is `Core.PerfMonitor.Count("Order.HashNodes", …)`, itself a
  diagnostic that early-outs while `[Perf] Attribution` is off. All four occurrences of the
  identifier are inside `.8.Order.cs`. Deleting `LogPanelOrder` would make one perf counter read 0
  and change no behaviour. Subtlety worth recording: since the S2 throttle hoist (`:753-758`) the
  field is written only on the ~1-in-135 frames that pass the throttle, so `Order.HashNodes`
  legitimately reports 0 on the others.
- **Do NOT delete or gate `LogPanelOrder`** — the `PANEL DRAW ORDER` line is the documented answer
  to every "X draws over Y" report. The integrator may retire the row.

### F-62 [A-03 corrected / A-14] — two prior-audit rows are CLOSED at HEAD; do not re-open either
- **R41** ("the ladder's one invariant is a private constant and ten hand-copied literals") — fixed
  by `756bda65` (2026-09-05): `PanelOrderStep` is `internal const int … = 16` at `.8.Order.cs:176`
  with a runtime bound check at `:300-314`, and the ten consumers now derive or cite it
  (`Grab/GrabBarLayout.cs:110`, `Grab/GrabbableModal.cs:137`/`:1619`, `Grab/ModalCloseButton.cs:184`,
  `Tooltips/WorldTooltips.cs:718`, `Surfaces/SurfaceGrabBar.cs:124`,
  `Surfaces/TablePanelSurfaces.cs:2583`, `Hands/HandGhost.cs:133`, `WorldUI/FreeLabelOrder.cs:90`,
  `Net/Board/BoardVisual.cs:105`, `MapRoom/MapRoomHand.3.Wrist.cs:128`). The audit's "no assert" and
  "`private const`" clauses are both stale. **The remedy's own bound is F-46.**
- **redundancy-audit §6.5** ("`9d` is outside the pre-veil chain; `PreVeilAlpha`'s doc miscounts") —
  closed by ModBuild 439. `9d` exports `PreFlashVeilAlpha` (`:737`) and
  `WindowMaterialiseRunner.cs:366-368` asks all three veils nested in one expression. Verified
  independently of the doc: `.SetAlpha(` across `src/` returns writers in exactly four files —
  `9d`, `9e`, `9g` and the runner. The audit's "fourth veil" (`ModalFallback.11.PreConvertHide`)
  works on `Canvas.enabled`, not `CanvasRenderer.SetAlpha`, so it is correctly outside the chain.
  The reconciliation is bidirectional too (`Runner.cs:556` clamps through the seat veil; `9e:591`
  defers to a foreign value) — `a-hide-saved-a-foreign-value`, already respected.

### F-63 [A-22/A-24/A-26] — verified, and what was NOT found
- **Config keys.** The set binds exactly four, all in `WindowMaterialise.cs` (`:159`, `:170`,
  `:181`, `:191`), all READ, each through a null-coalesce onto its `Defaults.*` value. **No inert
  key; no prose default contradicting a bound default.**
- **Instrument writes.** Every `Log*`/`Report*`/`Describe*`/`Diagnose*`/`Verify*` body grepped for
  a field write and each field's readers traced. Beyond F-61 every one is a throttle or a
  change-gate read only by another diagnostic. **Nothing new belongs in the baseline.**
- **Per-frame scene sweeps.** `FindObjectsOfType` / `FindObjectOfType` /
  `Resources.FindObjectsOfTypeAll`: **zero** in either folder (the one textual hit, `.9d:491`, is a
  comment explaining why the game's own `_uiWindows` registry is enumerated).
- **Fake-null dictionary keys.** The three `Dictionary<CanvasRenderer, …>` veil tables are drained
  by an explicit lift/release path and never probed with `== null` as a key; the four
  `Dictionary<int, …>` tables are keyed on `GetInstanceID()` and pruned on a destroyed-`Owner`
  test — the correct shape. `PanelPlacement.Entries` is keyed on a plain managed class and pruned.
  **No hazard found** — recorded as a positive result, since the project has paid for this class.
- **Mirror pairs.** None in this set. **`check-mirrors.sh` PART 3:** the one candidate chased was
  `RevealRestoreWithheld`'s terms against `decompiled/GH.Runtime/…/UIWindow.cs:119/348/572/592` —
  the mod's four terms are a strict SUPERSET of what the game's `OnTransitionStarted` consults.
  **No missing term.**
- **Comments asserting a game behaviour, checked against `decompiled/`:** the five-step proof at
  `.9d:33-95` matches the decompiled source line for line, as does `ObjectPool.cs:468`'s
  `SetParent` contract cited by `.2.Adopt.cs:820-835`. **No falsified game assertion.**
- **`ApplyFitConverging`** (`.3.Fit.cs:4517-4580`) — checked for the classic converging-loop bugs
  and clean: hard-capped at `FitApplyIterations = 3`, `resized` short-circuits the re-measure, an
  explicit `FlushPendingLayout` precedes it (with a comment naming why a render-hidden panel would
  otherwise report false convergence), and the tolerance has a 2 px absolute floor under the
  relative fraction. Its one allocation is on the applied-fit path, which the damping makes rare.
- **Empty searches, so nobody redoes them:** no `[DefaultExecutionOrder]`; no coroutine and no
  `async` anywhere in the set (so the scratch buffers' non-re-entrancy contract is not violated by
  anything shipped); no `Object.Destroy` of a game-owned object outside the guarded
  `DestroyHostSafely` path; no write to game state from a `Log*`/`Report*` body; no `PanelSlot`
  enum value without a consumer (`CombatLog` and `ButtonCluster` are reached from
  `Options/DevPanels.cs:23` — BRIEF §5 item 6, a deliberate debug feature).
- **Where the reading is thinnest, stated plainly (A-26):** `WindowMaterialise.cs` (1 247),
  `WindowMaterialiseRunner.cs` (777) and `WindowMaterialiseVisibility.cs` (582) were read at the
  instrument / exception-path / veil-reconciliation / config-key level, not whole — the largest
  under-read area of the set. Inside `.3.Fit.cs`, the interiors of `SolveSubViewPlacement`,
  `EnsureColumnSeam`/`MaybeReDeriveSeamOnNarrowing` and the body of `LogHitRect` were not read
  line by line; none writes game state or holds a latch.
- **Also verified, one line each, in the sub-reader file:** all 27 `INVARIANTS-WorldUI` §2 entries
  (two superseded — F-60) and all six §11 entries naming `CanvasConversion`.

<!-- §8 body, read by the integrator directly (no sub-reader); the "pending" heading above is superseded. §7 arrives after this section, so the finding numbers are not in section order — F-64…F-72 are §8's. -->

## 8. `WorldUI/Sharpness/` (read by the integrator)

Nine files, 13 799 lines: `PanelSupersample.2.Capture.cs` (3 158), `.1.Core.cs` (2 907),
`.4.Content.cs` (2 776), `.3.Report.cs` (1 481), `.5.Isolation.cs` (332), `PanelSamplingProbe.cs`
(991), `RenderTargetProbe.cs` (980), `PanelMipBake.cs` (889), `CameraOrderProbe.cs` (285).

**How it was read (honest coverage).** Instruments, exception paths, latches, teardown and config
first — all 69 `VRLog` sites, every `catch`, every one-shot `bool`, both `Shutdown` bodies,
`StandDownAll`, `EnsureArrivalWatch`/`TickArrivals`, `Engage`/`StandDown`, the layer pool, the
signature, and every declaration in `.1.Core.cs`'s dial and `Entry` regions. The arithmetic
interiors of `MeasureFrame`, `ResolveRate`, the over-paint commit and the three probes' sampling
maths were scanned, not reasoned through line by line — that is the thin part of this pass, and
`PanelSamplingProbe.cs` / `RenderTargetProbe.cs` are the thinnest files in it.

### F-64 — the sharpness subsystem can say it CRASHED but not that it RAN, and the rule it breaks is written in its own dial region
- **File:line:** tier census across `WorldUI/Sharpness/`: **5 `Alert`, 1 `Note`, 30 `Info`, 33
  `Warn`** — so 63 of 69 lines print nothing at the shipped `[General] LogLevel = Info`. The six
  that print are `.2.Capture.cs:2898/:3034/:3096/:3121/:3148` (refused layer restores, the throw
  ledger, the missing-parts line, the per-site throw cap, the session kill switch) and
  `.1.Core.cs:2254` (`STRADDLE ENDED AT SHOW`).
- **Class:** risk-gap (systemic). **Tier:** 3 (each fix is one word).
- **Evidence — what is invisible.** Every line that says the feature engaged, stood down or refused
  a window is at a dropped tier, and each one spells out the user-visible consequence in its own
  text: `.1.Core.cs:2890` `PANEL SUPERSAMPLE engaged on '…'` (`Info`); the ten-second per-panel
  state report `.3.Report.cs:460` (`Info`); the layer-pool census `.5.Isolation.cs:196` (`Info`);
  and at `Warn` — `:2326` *"stands down: the capture-layer POOL is empty … THE CONSEQUENCE: every
  floated window keeps being rasterized directly into the eye at ~1.86 authored pixels per rendered
  pixel, i.e. exactly today's behaviour including the reported text and edge shimmer"*; `:2701`
  *"stands down: there is no head camera … floated windows keep today's direct rendering"*; `:2767`
  and `:2791` *"refused '…' … THE CONSEQUENCE: this window keeps today's direct rendering and will
  still shimmer"*; `:2639` the cap line; `.5.Isolation.cs:184` the empty-pool line;
  `.2.Capture.cs:705` and `:763` the render-target stand-downs; and in `PanelMipBake.cs` `:502`,
  `:565`, `:598`, `:792`, each of which ends *"THE CONSEQUENCE: … aliased for up to ~1 s"*.
  **So a hardware log for the report this subsystem exists to answer — "the text shimmers" — cannot
  distinguish "supersampling ran and did not help" from "supersampling never engaged on that
  window".**
- **The rule is stated in the same file.** `.1.Core.cs:404-406`, documenting
  `ReportIntervalSeconds`: *"How often the per-panel state line is printed, whether or not anything
  changed. **A silent path and a path that never ran must never look the same (the
  RenderTargetProbe rule).**"* The line that implements that sentence is `VRLog.Info`.
- **Proposed action — promote a NAMED, BOUNDED set, not the tier wholesale.** All of the following
  are latched or bounded, so the worst case is a handful of lines per session:
  1. `.1.Core.cs:2326` (`_noLayerLogged`, once) and `:2701` (`_noHeadLogged`, once) — the two
     subsystem stand-downs.
  2. `.1.Core.cs:2767` and `:2791` — the per-window refusals; bounded by `Refused` (an instance-id
     set consulted at `:2615`), i.e. once per window per session.
  3. `.2.Capture.cs:705` and `:763` — the render-target stand-downs, on the same `Refused` path.
  4. `.5.Isolation.cs:184` — the empty-pool line, once.
  5. `PanelMipBake.cs:502` (`s_pumpFailed`, once), `:565` (`s_arrivalErrorLogged`, once), `:598`
     (`s_panelCapLogged`, once), `:792` (`watch.CapLogged`, once per panel).
  6. `.1.Core.cs:2890` `engaged on '…'` → `Note`. This is the one non-refusal promotion and it is
     the one that makes the rest readable: without it a log with no stand-down line is still
     ambiguous. It fires once per window per engage, and `Refused`/`Entries` bound re-entry.
  **Leave at their present tier:** the ten-second per-panel report (`.3.Report.cs:460`) and the
  `.3.Report.cs:1369` summary — they are periodic by construction and promoting them would put a
  multi-line block into every player's log every ten seconds; the pool census
  (`.5.Isolation.cs:196`, a once-per-session `Info`) is a judgement call for the integrator, and I
  lean to promoting it too, since it is the evidence the empty-pool line at `:184` refers the
  reader to ("The one-time POOL census line above lists exactly which layers are named and by
  whom") — a referring line at a printing tier pointing at a referent at a dropped one.
- **Guard:** `CHANGED` confined to `PanelSupersample` and `PanelMipBake`. No text changes.

### F-65 — DEFECT: two stand-down latches are never cleared, and their own sibling proves the intent
- **File:line:** `WorldUI/Sharpness/PanelSupersample.1.Core.cs:981-983` declares `_noLayerLogged`,
  `_noHeadLogged`, `_capLogged`. `_capLogged` is cleared in `StandDownAll`
  (`.2.Capture.cs:2818`, `:2845`). `_noLayerLogged` and `_noHeadLogged` are cleared **nowhere** —
  not in `StandDownAll`, not in `Shutdown` (`.1.Core.cs:2270-2295`).
- **Class:** defect (`a-held-instrument-reads-as-dead`, and the same shape as F-39). **Tier:** 3.
  **Act.**
- **Evidence:** `Shutdown`'s own comment (`:2278-2283`) states the rule and then misses these two:
  *"A HOT RELOAD IS A NEW SESSION FOR THE FAILURE LEDGERS. Everything they hold is 'since this
  process last started this path', and the whole point of reloading the plugin is to test a build
  that may have fixed the throw — a stale four-strike refusal would make the new code read as still
  broken while never having run."* It then clears `Refused`, `Failures`, `WindowFailures`,
  `MissingParts` and `_pathDisabled` — and leaves the two log latches set. `_noHeadLogged` is the
  worse of the two, because its own line ends *"Retried on the next tick"*: the head camera goes
  away and comes back on every VR off/on and every scene change, so after the first null the
  subsystem never again explains why it is standing down, for the life of the process.
- **Proposed action:** clear both in `Shutdown`, beside the ledgers, with the reason on the same
  comment. (Not in `StandDownAll`: that runs whenever the dial is switched off, and re-printing the
  pool/head explanation on every toggle is noise — `_capLogged` is cleared there because the cap is
  a per-population fact, which these two are not.)
- **Guard:** `CHANGED` confined to `PanelSupersample`.

### F-66 — the shutdown failure line names leaked mod state and is at a dropped tier
- **File:line:** `WorldUI/Sharpness/PanelSupersample.1.Core.cs:2291` (`VRLog.Warn`).
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (`Warn` → `Alert`).**
- **Evidence:** the text is *"PANEL SUPERSAMPLE shutdown failed — a floated window may keep a mod
  capture layer or a camera may keep a narrowed culling mask until the next scene load."* That is
  mod state left on the GAME's objects: a narrowed `cullingMask` on a game camera is exactly the
  class of leak that produces "half the world is invisible" reports, and it is announced only at
  `LogLevel = Debug`. It fires at most once per shutdown.
- **Guard:** `CHANGED` confined to `PanelSupersample`, one call site.

### F-67 — the ModBuild 219 removal left a whole mechanism's skeleton behind: a dead struct, three write-only fields, two unread consts, an orphaned `<summary>` and a truncation flag that does not exist
- **File:line:** `WorldUI/Sharpness/PanelSupersample.1.Core.cs:1029-1038` (`private readonly struct
  CullPair` + its constructor), `:1950/:1954/:1955` (`Entry.SubMeshCullLatched`,
  `…LatchedNote`, `…LatchedNamed`), `:1973` (`Entry.CullPairCollections`), `:753`
  (`SubMeshCullSettleFrames = 30`), `:763` (`MaxCullNamed = 3`), `:752-759` (the orphaned
  `<summary>`), and `.4.Content.cs:1462-1464` (the three resets) and `:1595`
  (`e.CullPairCollections++`).
- **Class:** dead + doc-drift (with one claim that is a risk-gap). **Tier:** 0. **Act.**
- **Evidence, member by member (BRIEF §5's eight items run on each; none is a Harmony target, a
  Unity message, serialized, reached by reflection/`nameof`/a string, a config key, a log grep
  token, a debug-menu entry or pinned by `tests/` — verified by `grep -rn` over `src/`, `tests/`,
  `scripts/` and every `.planning/refactor/*.allow`, `.baseline` and `.lock`):
  - `CullPair` is **never constructed**. There is no `List<CullPair>` and no other use anywhere in
    `src/`. The "pair cache" its doc describes (*"Held in a cache rather than re-walked, because the
    walk that finds them costs a measured ~1.7 ms"*) does not exist.
  - `SubMeshCullLatched`, `SubMeshCullLatchedNote`, `SubMeshCullLatchedNamed` are **written only to
    their zero value** (`.4.Content.cs:1462-1464`) and never incremented, appended to or read. Three
    fields and three lines of reset code that can only ever move zero to zero.
  - `CullPairCollections` is incremented once (`.4.Content.cs:1595`) and **never read** — and the
    comment above the increment, *"The pair cache is now this walk's, and it covers every text
    component the walk reached"*, describes the cache that no longer exists.
  - `SubMeshCullSettleFrames` and `MaxCullNamed` have **no consumer**.
  - `:752-759` is a `<summary>` with **no member under it** — the next thing in the file is another
    `<summary>` (`MaxCullNamed`). It documents a pair-cache cap ("512 is two orders above anything
    measured") that no longer exists, and it asserts a field that never existed at HEAD:
    *"When it bites, `Entry.CullPairsTruncated` says so and every count below it is a LOWER BOUND —
    the standing rule that a truncated instrument must never read clean."* **There is no
    `CullPairsTruncated` and no cap. The standing rule the sentence invokes is not implemented by
    anything this paragraph describes** — that is the risk-gap half, and it is why this is worth a
    finding rather than a tidy-up: a reader auditing "is our truncated instrument honest?" finds a
    paragraph saying yes.
  - `.1.Core.cs:740` and `:2460` and `.4.Content.cs:137` and `:1833` name `RepairSubMeshCull` /
    `ScanTmpSubMeshes`, and no such members exist.
- **Cause:** `44084c18 refactor(worldui): ModBuild 219 — remove sixteen builds of diagnostic
  apparatus for a solved defect` (confirmed with `git log -S'CullPair'`). The removal took the
  users and left the declarations, the resets, the dials and the prose.
- **Proposed action:** delete `CullPair`, the three `SubMeshCullLatched*` fields and their three
  resets, `CullPairCollections` and its increment, and `SubMeshCullSettleFrames` / `MaxCullNamed`;
  delete the orphaned `<summary>` at `:752-759`; and correct the four prose references (see F-69).
  Nothing here is a log token: the strings the docs grep are the LINE texts, which are untouched.
- **Guard:** `CHANGED` confined to `PanelSupersample` (an `Entry` layout change is expected and is
  the correct signature of removing four instance fields).

### F-68 — `CameraOrderProbe` breaks the exact rule its own class doc was written to enforce, and the game state it MUTATES is announced at a dropped tier
- **File:line:** `WorldUI/Sharpness/CameraOrderProbe.cs:216` (`LogShape`, `VRLog.Info` — *"the
  baseline that proves the probe ran"*), `:98` (armed, `Info`), `:243` (the hoist actually applied,
  `Info`), `:272` (the restore, `Info`); only `:174` (the observation) is even at `Warn`.
- **Class:** risk-gap. **Tier:** 3. **Act (`:216` and `:243` → `Note`; `:174` → `Alert`).**
- **Evidence:** the class doc says, in capitals, *"THE PROBE PRINTS ITS BASELINE EVEN WHEN IT FINDS
  NOTHING. Every distinct render-order SHAPE is logged once (up to `MaxShapesLogged`), so a log
  always says what the order actually was — a scan that only speaks up on a hit cannot be told
  apart from one that never ran, **and this project has already paid for that lesson once**."* At
  the shipped level it prints neither the baseline nor the hit. Worse, `:243` is the line that
  reports **a write to a game camera's `depth`** — the probe's correction — and a mod that
  re-orders a game camera and does not say so at a tier the player's log carries is a change with
  no record. Bounded by construction: `MaxShapesLogged = 6`, `Reported` is a one-shot instance-id
  set, and the hoist happens once per camera.
- **Guard:** `CHANGED` confined to `CameraOrderProbe`.

### F-69 [STALE-DOC-REFS] — the seven Sharpness rows, resolved
- **Class:** doc-drift. **Tier:** 0/1. **Act (fix each cref, delete each row).**
  | row | verdict at HEAD |
  |---|---|
  | `.1.Core.cs:421` `MarkGeometryDirty` | the member is **`NoticeGeometry`** (`.2.Capture.cs:1223`, called at `:1068`), which is already cref'd correctly eight other places in the same folder. |
  | `.1.Core.cs:745` `CullPairsTruncated` | **no such member ever existed at HEAD** — see F-67; the whole paragraph goes. |
  | `.1.Core.cs:1015`, `.1.Core.cs:1927`, `.4.Content.cs:137` `RepairSubMeshCull` | no such member; the sub-mesh cull census and its unconditional repair now run inside `ScanTmpText` (`.4.Content.cs:1619`) and `NoteRendererState` (`:1828`), reached from `MeasureContent(e, repairAll: true)`. |
  | `.2.Capture.cs:2162` `ScanTmpMesh` | the member is **`ScanTmpText`** (`.4.Content.cs:1619`). |
  | `.4.Content.cs:1892` `ReportSubMeshCull` | no such member; the REGENERATION field it names is assembled by the per-panel report in `PanelSupersample.3.Report.cs`. |
  Two further prose references not in the table but in the same class: `.1.Core.cs:740`/`:2460`
  (`RepairSubMeshCull`) and `.4.Content.cs:1833` (`ScanTmpSubMeshes`).
- **Guard:** empty (doc comments are not in the ilspy snapshot).

### F-70 — redundancy-audit **R16 is CLOSED at HEAD**; do not re-open it
- **File:line:** `WorldUI/Sharpness/PanelSupersample.1.Core.cs:612`
  `private static float FrameBudgetMs => Core.PerfMonitor.BudgetMilliseconds;`
- **Evidence:** R16's complaint was a hardcoded `1000f / 90f` disagreeing with `[Perf] SUMMARY`'s
  live-refresh-rate budget. Fixed by ModBuild 439, with the reasoning at `:599-611` — including the
  falsifier (*"At 90 Hz the number is unchanged, which is what makes an unchanged reading on a
  90 Hz session evidence"*) and the deliberate NON-merge of the report window (*"the line states its
  window in its own text … coupling the cadence would have made a shipped log string ('this 10 s
  window') false the moment anyone tuned the interval"*). That second half is a correct application
  of the no-reworded-token rule and must not be "finished".
- **Class:** leave-alone. **Tier:** n/a. `an-audit-is-a-snapshot`, for the third time this round.

### F-71 [pairs with F-54] — the Sharpness side of the R7 clone, confirmed, with the same false lane claim
- **File:line:** `WorldUI/Sharpness/PanelSupersample.4.Content.cs:2032-2075`
  (`ActiveSetSignature`) and `:2077-2084` (`MixSubView`) vs
  `WorldUI/Conversion/PanelInkBounds.cs:936-950` and `:1003-1010`.
- **Class:** parallel-construction. **Tier:** 2.
- **Evidence:** `MixSubView` is **byte-identical** in the two files (seven lines each, same body,
  same `IsUnder` helper beside it), and the tail of `ActiveSetSignature` — the
  `NewPartyDisplayUI.PartyDisplay` fetch in a try/catch, the null guard, `sig = sig*31 +
  (int)display.ActiveDisplay`, then the six `MixSubView` calls in the order `CharacterSelector,
  PerkManager, AbilityCardsDisplay, EnhancementCardsDisplay, ItemInventoryDisplay,
  BattleGoalWindow`, inside a second try/catch — is the same fourteen statements. **Part one
  genuinely differs** and must stay per-caller: this side hashes the ACTIVE DIRECT CHILDREN of the
  conversion target by instance id in sibling order; the ink side runs `HashActiveSet` to
  `SignatureDepth = 2` with a transient-family exemption, a hashed-count-per-level rule and a memo.
  The class doc here says so itself, and then explains the copy with the same fiction F-52/F-53
  found twice next door: *"It is a LOCAL DERIVATION and does not read `CanvasConversion`'s `fx`
  state: **that is another lane's file** and exposes no accessor for it."* `WorldUI/Conversion/` and
  `WorldUI/Sharpness/` are both lane worldui-frame (BRIEF §2).
- **Proposed action:** exactly F-54's contract — hoist the party-display tail plus `MixSubView`/
  `IsUnder` into one `internal static` helper; keep each caller's part one; **preserve the six-call
  order byte-for-byte** (a rolling `sig*31` hash: reordering it makes every window in both
  subsystems see one phantom generation event on the build that lands). Fix the two "another lane's
  file" sentences (`.4.Content.cs:2029-2030` and `:460`) while there.
- **Guard:** `CHANGED` confined to `PanelSupersample` and `PanelInkBounds`. The six-call order must
  be diffed by eye — the guard cannot see a hash reorder.

### F-72 — verified, and what was NOT found
- **Config keys.** `Sharpness/` **binds nothing**: it reads six keys through `WorldUIConfig`
  (`PanelSupersample`, `PanelSupersampleFactor`, `PanelMipBake`, `PanelMipLodOffset`,
  `CanvasScaleMm`, `NeutraliseGrabPassBlur`), all bound in another file. No inert key here, and no
  `Bind` description to contradict.
- **Latches and teardown.** `_pathDisabled` (the four-strike kill switch) is correctly cleared by
  `Shutdown` with a comment explaining why a hot reload must clear it; the per-site throw ledger is
  **capped, not latched**, and `.2.Capture.cs:3114` records why (*"The ModBuild 478 `_errorLogged`
  bool silenced EVERY later failure at EVERY site"*) — that is the F-47/F-65 lesson already learned
  in this folder, which makes F-65 an omission rather than a disagreement. `PanelMipBake`'s
  `ArrivalPump.OnDestroy` correctly re-arms `s_pumpInstalled` so a ScriptEngine reload does not
  leave the seam permanently off.
- **The `DontDestroyOnLoad` pump.** `PanelMipBake.EnsureArrivalWatch` (`:482-487`) creates a
  `HideAndDontSave` GameObject and there is no teardown path for it, but this is **not** a leak
  finding: `TickArrivals` (`:545-552`) stands the watches down and hands every swapped sprite back
  the moment the dial goes false, and `s_lastTickFrame` makes the double owner (the module's
  LateTail step and the pump) a no-op. The one drift is a sentence: `:476-478` says *"OFF means OFF,
  **including the pump object** and its arm line"*, which is true only before the first install —
  once installed the object survives a dial toggle, inert. One-line doc fix, folded into F-69's
  commit.
- **Per-frame scene sweeps.** No `FindObjectsOfType` / `FindObjectOfType` /
  `Resources.FindObjectsOfTypeAll` anywhere in the folder.
- **Fake-null keys.** The per-panel tables are keyed on `GetInstanceID()` (`Refused`, `WatchByHost`,
  `LiveHosts`); `Hoisted` in `CameraOrderProbe` is keyed on `Camera` but drained on restore and
  never probed with `== null` as a key. No hazard found.
- **Writes to game state from presentation code.** Two exist and both are legitimate, documented
  and reversible: `CameraOrderProbe`'s camera-`depth` hoist (recorded before it is applied, original
  restored on teardown, head camera and mod cameras excluded) and the culling-mask narrowing
  `PanelSupersample.RestoreMaskedCameras` hands back. Neither writes RULE state. **F-68 is about
  the first one's tier, not its legitimacy.**
- **Mirror pairs / wire.** None in this folder; `check-mirrors.sh`, `check-remote-defaults.py` and
  `check-mirror-dials.py` have nothing here.
- **Empty searches, so nobody redoes them:** no `[DefaultExecutionOrder]`; no `async`; the only
  coroutine-shaped thing is `ArrivalPump.LateUpdate`, a `MonoBehaviour` message with a one-line
  body; no `Bind(` call; no tuning literal proposed for change anywhere in this section.

<!-- §7 body appended by the integrator from sub-reader B's file (50 files, 33 372 lines); the "pending" heading above is superseded. B's own file is committed verbatim beside this one as REVIEW-worldui-frame-subreader-B.md, including its coverage table, its fourteen empty searches and its "if only three things land" list. Numbering note: §7 was read after §8, so its findings are F-73…F-88 and §8's are F-64…F-72. -->

## 7. `WorldUI/Options/` + `WorldUI/Grab/` + `WorldUI/Patches/`

50 files, 33 372 lines. Read by sub-reader B (base `e26371e4`). Tier census over the whole set:
**114 `Warn` + 96 `Info` + 5 `Debug` silent, against 46 `Note` + 13 `Error` + 5 `Alert` printing.**
Findings F-73 … F-88; `[B-nn]` cites the sub-reader file.

### F-73 [B-04] — DEFECT: `LogOptionsKey`'s `beforeNames` loop dereferences a captured `UIWindow` with no null test, where both sibling loops in the same method have one
- **File:line:** `WorldUI/Options/OptionsToggle.cs:389-391`
  (`sb.Append(…).Append(_censusBefore[i].name)`) against `:368-369` (`if (w == null || w.IsOpen)
  continue;`) and `:380-381` (`if (w == null || _censusBefore.Contains(w)) continue;`).
- **Class:** defect (fake-null hazard). **Tier:** 3. **Act.**
- **Evidence:** `CensusOthers(_censusBefore)` (`:277`, body `:341-353`) captures live `UIWindow`
  REFERENCES, not names. Between that capture and `LogOptionsKey` the tap runs `CloseAll(menu, st)`
  (`:287`) or `OpenMenu` (`:302`) — the game's whole `Hide()`/`Show()` cascade, **on this call
  stack**, which the method's own comment at `:330-333` says is the point (*"anything the mod's
  CloseAll or the game's own Show/Hide cascade did to another window on this call stack is visible
  right here"*). A destroyed `UnityEngine.Object` is fake-null for `==` but **throwing** for member
  access, so `.name` raises `MissingReferenceException`: the tap frame aborts, `LogTapCost` (`:336`)
  never runs, and the instrument that adjudicates the 2026-09-03 options-key ruling goes silent on
  exactly the press that broke it.
- **Honest caveat, in B's own words:** the destruction is INFERRED, not demonstrated — no game path
  in `decompiled/` was found that destroys a registered `UIWindow` inside `Hide()`. **The
  demonstrable half is the internal inconsistency**: two loops in this method treat destruction as
  possible and the third does not.
- **Proposed action:** mirror the sibling guard —
  `UIWindow bw = _censusBefore[i]; if (bw == null) continue;` — keeping the separator logic keyed
  off whether anything has been appended. No string literal changes.
- **Guard:** `CHANGED` confined to `OptionsToggle`.

### F-74 [B-11 + B-11b] — EIGHT one-shot self-disarms at a tier no hardware log carries, against one sibling that was corrected and carries the paragraph explaining why
- **Class:** risk-gap + parallel construction (one concept, nine implementations, one of them
  fixed). **Tier:** 3 — eight one-word promotions. **Act.**
  | # | site | latch | what is lost, in the line's own words |
  |---|---|---|---|
  | 1 | `Options/VROptionsTab.1.Inject.cs:1436-1440` | `_degraded` | *"no VR settings menu this session … The pause-menu VR row is not injected either"* |
  | 2 | `Patches/InputFieldFocusWatch.cs:165-167` | `_degraded` | the push seam; falls back to the sweep this same file records at **90-99 ms/s, worst 23 ms in one frame, the mod's most expensive step by a wide margin** |
  | 3 | `Patches/EscMenuInputBlock.cs:145-146` | `_degraded` | *"Game's controller ESC-menu paths left vanilla (X may double-act)"* |
  | 4 | `Patches/SettingsClickExemption.cs:242-245` | `_degraded` | pause/options clicks stay vanilla-gated, *"or killed by a gate NullReferenceException during confirm waits"* |
  | 5 | `Patches/Character3DDisplayRefcount.cs:252-258` | `_resolved`+`_standDown` | the refcount gate; the first window to close *"blanks the model out of the render texture the second window is still showing"* |
  | 6 | `Patches/PartyPreviewStorm.cs:227-233` | `_resolved`+`_standDown` | the redundant-rebuild suppression |
  | 7 | `Grab/UiSoundEar.cs:325-329` | `_fieldResolved` | *"**Every button hover/click sound will stay INAUDIBLE** while the mod owns the AudioListener"* |
  | 8 | `Grab/UiSoundEar.cs:673-678` | `_failureLogged` | *"button hover/click sounds may stay inaudible. This is logged once per session"* |
- **The reference implementation is three files away.** `Patches/EscMenuShowSafety.cs:114-124` is
  the identical *"log the first failure and thereafter stay silent"* shape at **`VRLog.Alert`** with
  `// HW-VERIFY`, and its sibling `Report` carries the rule at `:136-142`: *"AT THE ALERT TIER, NOT
  Warn (ModBuild 439, survey item B3) … `VRLog.Warn` and `VRLog.Info` both gate on `Level >=
  VRLogLevel.Debug`, so at the shipped default these lines printed NOTHING and the doc's own
  requirement was false for eight builds. The wordings are untouched; only the tier moved."*
- **No flood argument exists against any of the eight:** each latch is set BEFORE its emitter, so
  none can print more than once per session. Sites 1, 3 and 4 name consequences under the standing
  ruling *"it must ALWAYS be possible to open the options menu"* — the same ruling
  `EscMenuShowSafety` cites. Site 7's consequence is a **verbatim standing user report** quoted in
  its own file (`:684-687`, ModBuild 195): *"Immer noch keine Geräusche wenn ich die physischen
  buttons drücke wie zB 'Händler'"* — if it fires, the answer to the next round's "still no sounds"
  is already in the code and invisible in the log.
- **Proposed action:** `Warn` → `Alert` at all eight. **No string changes.** `// HW-VERIFY` on
  sites 1, 2, 5 and 7 only (the four whose absence is unreadable against a real symptom); leave the
  others unmarked so the marked-site count stays meaningful.
- **Guard:** `CHANGED` confined to seven types, one call target each, no literal differing.

### F-75 [B-07] — the two ModBuild 336 HW-VERIFY verdicts are unreachable on their own failure path, so their ABSENCE cannot be read
- **File:line:** `WorldUI/Options/VROptionsTab.1.Inject.cs:600-616` + its `catch` `:618-623`, and
  the twin `:662-681` + `:683-687`; latches at `:146-147`.
- **Class:** risk-gap. **Tier:** 3. **Act.**
- **Evidence:** both Notes are `// HW-VERIFY` and both say why — `:603-605`: *"this is the verdict
  the ModBuild 336 round is waiting on — it must stay at a tier the default log level prints, **or
  the round comes back unable to say whether the two windows were separated at all**."* Each Note
  is the LAST statement of its `try`, after the mutating calls (`win.escapeKeyAction = None;
  UIWindowManager.UnregisterEscapable(win);` at `:594-598`; `_inputArea.Destroy();` at `:660`). If
  one of those throws, control leaves for a `catch` whose `VRLog.Warn` prints nothing — **and the
  latch is never set, so the Note is not printed on this open or any later one that also throws.**
  Three distinct states (the patch never ran; `IsStandalone` was false so `LeaveInputAreaStack`
  returned at `:656`; it threw) produce one identical reading: an empty log. This is exactly the
  ambiguity `Patches/MenuExitLatchGuard.ArmOnce` (`:87-113`) was added to remove for its own guard.
- **Proposed action, two parts:** (1) `:620` `Warn` → `Alert` — at most once per session; (2)
  `:685` gets its own one-shot latch (`_loggedAreaLeaveFailed`) and THEN `Alert` — it runs on every
  open, so **do not promote it without the latch.** Strings untouched.
- **Guard:** `CHANGED` confined to `VROptionsTab`, two call targets and one new `private static bool`.

### F-76 [B-20] — `VROptionsTab._degraded` is a process-lifetime latch on a class whose stated job is to RE-inject per options window — A QUESTION FOR THE USER, not a commit
- **File:line:** `WorldUI/Options/VROptionsTab.1.Inject.cs:135` (declaration), `:255-257` (`Tick`'s
  FIRST statement is `if (_degraded) return;`), `:168` (`CanOpen => !_degraded && …`), `:1430-1441`
  (`Degrade`), `:1452-1470` (`Forget`), `:1497-1525` (`Shutdown`), and the five `Degrade(...)` call
  sites at `:308`, `:315`, `:327`, `:363`, `:393`.
- **Class:** defect. **Tier:** 3. **DO NOT CHANGE THE LATCH IN THIS PHASE (BRIEF §1).**
- **Evidence:** the class doc states the design at `:250-252` — *"**Re-injects when the options
  window is replaced** — it is a `Singleton` that does not survive every scene, and a stale
  reference would leave the tab silently missing for the rest of the session"* — and `Tick`
  implements it at `:263-272`. But the re-injection machinery sits downstream of a latch that is
  never cleared: `Forget` clears eleven other flags (`_host`, `_toggle`, `_window`, `ContentRoot`,
  `TabBarRoot`, `IsStandalone`, `_onHidden`, `_hiddenHooked`, `_selectOnShow`, `_showHooked`,
  `_loggedShowRemedy`, `_inputArea`) and not `_degraded`, and `Shutdown`'s `finally { Forget(); }`
  therefore does not either. Four of the five `Degrade` reasons are measurements of ONE options-
  window instance, and the fifth is a catch-all around the whole injection. **The sibling class does
  the opposite:** `WorldUI/Options/VRMenuEntry.cs:950` clears its own `_degraded = false` in its
  reset. Two classes, one folder, one concept, opposite lifetimes, neither saying why.
- **What B is NOT claiming:** no concrete input was found that makes one of the five reasons fire
  transiently. The demonstrable half is the contradiction between `:250-252` and `:256`, and the
  asymmetry against `VRMenuEntry.cs:950`.
- **What lands now:** F-74 site 1's tier (so the state is at least readable), and one comment above
  `:135` recording that the latch is process-lifetime, that `Forget` does not clear it, and that
  `VRMenuEntry` chose the opposite — so the next reader sees a decision rather than an omission.
- **THE QUESTION FOR THE USER:** should a single failed injection disable the VR settings menu for
  the rest of the process, or only for that options-window instance? Changing it risks an injection
  retry loop on a genuinely broken game build, which is the reason the latch exists.

### F-77 [B-16] — `ConfirmationBoxRescue`'s "that arm of the deadlock guard is INERT" is the ONE silent line in a file otherwise entirely at printing tiers
- **File:line:** `WorldUI/Patches/ConfirmationBoxRescue.cs:456-458` (`VRLog.Warn`); resolver at
  `:430`, called from `:364` and `:388`.
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (`Warn` → `Alert`).**
- **Evidence:** every other `VRLog` call in the file prints — `:209` `Error`, `:224` `Alert`,
  `:236` `Error`, `:246`/`:253` `Note`, `:258` `Error`, `:343` `Note` (HW-VERIFY `CONFIRMATION
  RESCUE ARMED`). The single exception reports **the guard being switched off**. The file's own
  markers say what is at stake: `:222` *"THE LINE THAT DECIDES THE NEXT HARDWARE ROUND"*, `:341`
  *"proof the rescue is LIVE. Its absence means the patch never ran at all"* — and with one arm
  inert the log carries neither the ARMED line for that arm nor any explanation. Bounded by
  construction: it is a Harmony `TargetMethod` resolver with two call sites at patch registration,
  **at most two lines per session**. Not `Error`: the mod still works, the arm is simply absent,
  which is `Alert`'s documented meaning.

### F-78 [B-05] — the ESC-menu resolution chain: two of six anomaly lines promoted, four left alone by name
- **File:line:** `WorldUI/Options/OptionsToggle.cs:573-577`, `:681-687`, `:674-676`, `:705-711`,
  `:550-557`, `:279-281`.
- **Class:** risk-gap. **Tier:** 3. **Act on two.**
- **Evidence:** three separate planning documents triage from tokens this chain emits —
  `.planning/OPTIONS-MENU-NEVER-BLOCKED.md:147` names `[OptionsToggle] X tap on UIScenarioEscMenu
  (source: …)` as the line identifying WHICH menu was driven; `.planning/STATE.md:1215` reasons
  from it; `WorldUI/Modal/ModalFallback.7.Close.cs:253` reconstructs the ModBuild 407 story-window
  deadlock from it — and a default-level log contains none of them. **But the gap is narrower than
  "six silent lines"**: `LogOptionsKey`'s HW-VERIFY `Note` (`:427-445`) already prints which menu
  and what the press did, and `OPTIONS TAP: … DID NOT OPEN` is already `Error` (`:327`).
  - `:573` *no ESCMenu object exists* → **`Alert` + `// HW-VERIFY`.** This IS "the X button did
    nothing", a standing user ruling, and the only branch with no printing counterpart (it
    `return null`s before `LogOptionsKey` is reached). Gated on `ShortTapThisFrame`, ~1/tap.
  - `:681` `SECOND CHANCE TOOK` → **`Alert`.** A rescue that fired is an anomaly with a crash
    history (ModBuild 289/290) and `LogOptionsKey` reports only the eventual success.
  - **Left alone, each with its reason:** `:674` (the `:327` `Error` covers the outcome), `:705`
    `ESC MENU RESOLVED` (change-gated; the right fix would be adding `_menuSource` to the printing
    Note, which is a change to a surface-checked string and therefore not a refactor-phase move),
    `:550` `cache DROPPED` (change-gated detail), `:279` `[OptionsToggle] X tap` (~1/tap and
    verbose; the Note answers the same question inside the cap).

### F-79 [B-13 + B-17] — `MainMenuLogoSwap`'s watch states its own guarantee in the log string, and breaks it on exactly the two branches it covers
- **File:line:** `WorldUI/Patches/MainMenuLogoSwap.cs:1902-1907` and `:1924-1925` (both
  `VRLog.Warn`) against the closing `VRLog.Note` at `:2006`; and `:270-275` (`RE-ASSERT FIRED`,
  `VRLog.Warn`).
- **Class:** risk-gap. **Tier:** 3 (three one-word promotions). **Act.**
- **Evidence:** the ARMED line's own string says *"A CLOSING LINE IS PRINTED EITHER WAY — if
  nothing ever reverted it will say so, **because a guard that only speaks when it fires is
  indistinguishable from a guard that never ran.**"* `ArmWatch` has three exits: manager null/
  disabled → `Warn` then `return` (no coroutine ⇒ no closing line); `StartCoroutine` throws →
  `Warn` (same); normal → the closing `Note` prints. So in exactly the two cases the guarantee
  exists to cover, a default log contains **no watch line at all** — the reading the sentence says
  it prevents. Both are one-shot (`ArmWatch` is called once from the `Awake` postfix chain).
  `RE-ASSERT FIRED` says *"that is hypothesis (B), **and this line is the proof**"* and is a
  per-graphic one-shot; the closing Note already answers WHETHER (B) is live, but only this line
  names WHICH graphic and path reverted.
- **Proposed action:** `:1902`, `:1924` and `:270` `Warn` → `Alert`. Strings untouched. `:1913`
  (ARMED) may stay at `Info`: once the two failure branches print, a missing ARMED line plus a
  missing closing line is no longer ambiguous.

### F-80 [B-12] — the attribution pair that directs the next hardware round, and only one half of it should be promoted
- **File:line:** `WorldUI/Patches/PartyPreviewStorm.cs:257-283` (`VRLog.Info`) and
  `WorldUI/Patches/Character3DDisplayRefcount.cs:370`/`:391` (`VRLog.Info`).
- **Class:** risk-gap. **Tier:** 3. **Act on ONE of the two.**
- **Evidence:** the storm line says what it is for, inside the string: *"HOW TO READ IT. This line
  is the ATTRIBUTION for the CHARACTER 3D CADENCE line … **(3) Both counters at 0 while the user
  reports flicker means neither cause is live and the next round must not be spent here.**"* A line
  that directs where a hardware round is spent, at a tier no hardware log carries.
- **The throttle, checked rather than assumed:** `MaybeReport` (`:238-284`) advances its window
  start BEFORE the nothing-happened early-out (`:245-249`), so the cadence cannot be defeated the
  way `ActorPropBody.cs:933` was — B checked that specific inversion here and it is correct — and
  the line is suppressed entirely when both counters are zero. Ceiling: one line per 5 s, and only
  while the player hovers the roster.
- **Proposed action:** promote `PARTY PREVIEW STORM` (`:257`) `Info` → `Note` + `// HW-VERIFY`;
  **leave `CHARACTER 3D CADENCE` at `Info`** — its 1 s window makes it four times noisier and the
  storm line names its verdict in prose, so one printing half decides the round. `:391` is
  per-event and stays. Widening `ReportSeconds` would be a tuning change and is out of scope.

### F-81 [B-02] — `GrabbableModal`'s "NEVER SILENTLY" fallback is silent, which is what its own comment forbids
- **File:line:** `WorldUI/Grab/GrabbableModal.cs:940-956` (the `if (!inkPivot)` branch); the
  epilogue at `:970-977`.
- **Class:** risk-gap. **Tier:** 3 (one word). **Act (`:944` `Warn` → `Alert` + `// HW-VERIFY`).**
- **Evidence:** the comment above the emitter is
  `// NEVER SILENTLY. A fallback here reproduces the exact defect this change fixes, and it` /
  `// would look identical to the fix not working at all.` — and the emitter one line below is
  `VRLog.Warn`. The branch sits BELOW the epsilon early-out at `:931`, so it fires only on releases
  that actually turn the window: one call per release gesture, never per frame.
- **The epilogue at `:970` stays at `Info`** — it is the ModBuild 240 acceptance measurement, and
  its verdict half already prints (`WindowReFacePolicy.cs:132-140`, an HW-VERIFY `Note` on every
  release). One line per release from an instrument that already has a printing verdict is not
  worth a second.

### F-82 [B-01, closes F-53 #4] — `InkReleaseDeadBandPx` confirmed still `private` and still copied by value; both files are this lane
- **File:line:** `WorldUI/Grab/GrabbableModal.cs:420` (`private const float InkReleaseDeadBandPx =
  32f`) vs `WorldUI/Conversion/CanvasConversion.3.Fit.cs:5946` (`HitRectShrinkDeadBandPx = 32f`).
- **Class:** duplication. **Tier:** 2. **Act.**
- **Evidence:** `GrabbableModal.cs:336` — *"NOTHING WAS TRADED AWAY: `InkReleaseConsecutive` is
  still 3 and `InkReleaseDeadBandPx` is still 32"* — a hardware-settled number with a named round
  behind it; and `.3.Fit.cs:5946`'s own doc says the point is that *"all three instruments agree
  about what 'smaller' means"*, with nothing enforcing it. B confirmed at HEAD, after the
  concurrent `.3.Fit.cs` edit, that neither file has changed this pair.
- **Proposed action:** `private const` → `internal const` on `GrabbableModal.InkReleaseDeadBandPx`,
  then `HitRectShrinkDeadBandPx = GrabbableModal.InkReleaseDeadBandPx;`. Values unchanged; a const
  reference folds to the identical literal, so **any numeric difference in the guard means the
  change is wrong.**

### F-83 [B-08 + B-15] — FIVE double-`<summary>` sites, demonstrated from the generated XML rather than inferred
- **Class:** structure-naming / doc-drift. **Tier:** 0. **Act.** **Guard:** empty.
- A `<summary>` whose member was deleted or moved silently becomes the doc of whatever member
  follows, and the compiler is happy either way. B scanned the whole sub-set for `</summary>`
  immediately followed by a second `<summary>` — exactly five sites — and checked each against
  `bin/Release/net472/GloomhavenVR.xml`:
  - **`Options/ConfigCatalog.cs:1695-1707`** — `Tooltip`'s doc is stranded on `Hint`, which now
    carries two summaries; `grep` for `Tooltip`'s member entry in the XML returns **0**.
  - **`Options/ConfigCatalog.cs:1375-1379`** — `LeadingWord`'s doc is stranded on the
    `HashSet<string> VariantWords` FIELD (the text describes a method over a key); the XML has
    **0** doc members for `LeadingWord` (`:1461`). Move it down.
  - **`Grab/GrabbableModal.cs:3233-3257`** — the largest. Twenty-five lines describing the
    `GRAB BAR CLEARS THE INK` reporter (*"THE FALSIFIER, read back off the transform that was just
    written … Silence on this cost another build."*) sit on `MouseoverLedger` (`:3275`), a
    four-line expression-bodied string builder that reads no transform, is not rate-limited and
    prints nothing. The member described is `ReportBarPlacement` (`:3523`), which has **0** doc
    members in the XML. Move it down.
  - **`Options/VROptionsTab.10.Skin.cs:211-221`** — nothing stolen (both blocks belong to the
    member), but the first is PRE-move text carrying **the same false lane claim F-52/F-53/F-54
    found four times next door**: *"the modal close X (WorldUI/Grab/ModalCloseButton.cs, not
    converted this round — it is another lane's file)"*. `WorldUI/Grab/` is THIS lane, and it WAS
    converted — `ModalCloseButton.cs:433` reads `colors.fadeDuration =
    UguiTintFeel.HoverTintFadeSeconds;`, and a whole-repo grep for `fadeDuration` finds three
    assignment sites and **not one bare literal**, i.e. R20 is CLOSED. Delete the stale sentence
    pair, keep the accurate `MOVED to UguiTintFeel` note, and keep the reasoning about why the
    three PALETTES stay separate — that is a live decision.
  - **`Grab/PanelGrab.cs:408`** — a `<summary>` written with `<paramref>` tags for `Init`'s own
    parameters, sitting above `Init`'s real summary. Turn it into `<param>` tags or fold it in.

### F-84 [B-09] — STALE-DOC-REFS `ConfigCatalog.cs:166 ResolveStep` — the corrected cref is `ResolveSteps`
- **File:line:** `WorldUI/Options/ConfigCatalog.cs:166`, plus two prose restatements of the stale
  singular at `:690` and `WorldUI/Options/ConfigSteps.cs:640` (which already spells it correctly).
- **Class:** doc-drift. **Tier:** 0. **Act (fix the cref, delete the row).**
- **Evidence:** the live member is `private static void ResolveSteps(List<ConfigItem> items)` at
  `:815`, called once from `:379`. No `ResolveStep` exists anywhere in `src/`. The sentence around
  the demoted cref is still TRUE — *"the step depends on the largest magnitude in the entry's
  FAMILY, which is not knowable until every entry has been read"* is exactly what `ResolveSteps`
  does in its second pass. **This is the only STALE-DOC-REFS row naming a file in this sub-set.**

### F-85 [B-10] — `VariantTiles.ResetVariantTiles` is INERT, not dead, and its doc asserts a participation that does not exist
- **File:line:** `WorldUI/Options/VariantTiles.cs:675-678`.
- **Class:** dead → reclassified INERT + doc-drift. **Tier:** 0. **Act on the doc; keep the method.**
- **§5 checklist, run item by item:** not a Harmony target (absent from `docs/PATCH-INVENTORY.md`),
  not a Unity message (the type is `static`), no reflection/`nameof`/string reach (a whole-repo grep
  excluding `bin/`, `obj/` and the guard baseline returns ONE hit: the declaration), not a config
  key, emits no log token, not a debug-menu entry, not pinned by `tests/`, and not the only writer
  of anything (`TileSprites` is written at `:671` by `TileSprite`). **B names the near-miss that
  makes the checklist worth running:** `ConfigSteps.ExplicitKeys` (`ConfigSteps.cs:563`) looks
  equally callerless in `src/` and is pinned TWICE by
  `tests/GloomhavenVR.WireTests/ConfigStepVectors.cs:367` and `:470`.
- **The false claim:** the doc says it drops the sprites *"matching `WorldUIAssets.Reset`'s
  contract"*. `WorldUIAssets.Reset` (`WorldUI/WorldUIAssets.cs:104-111`) is called on shutdown and
  clears `_bundle`, `_probed`, `_gameFont` — it does **not** call this, and nothing else does.
- **And there is no defect behind it**, which is why this is a comment and not a wiring change: the
  `Sprite`s are built over `Texture2D`s from `Core/EmbeddedTexture.cs`, whose `_cache` is never
  cleared on a module reset, so the cached sprites stay valid across a hot reload — a small leak,
  not a blank tile. If anyone ever wires it, the call belongs in `WorldUIAssets.Reset` or
  `WorldUIModule.Shutdown`, **both lane worldui-front** ⇒ a NEEDED-OUTSIDE entry, not an in-lane edit.

### F-86 [B-19] — SIX `INVARIANTS-WorldUI.md` entries naming this sub-set are superseded, four of them describing a mechanism that no longer exists at all
- **Class:** doc-drift. **Tier:** 0. This is §7's counterpart to F-60, and it matters for the same
  reason: an invariant file is what the next round is told not to break.
  | entry | verdict at HEAD |
  |---|---|
  | `:308-311` "the depth mask is PER-GRAPHIC" (`CollectVisibleMaskRects`, cap 256) | **GONE.** `grep -rn 'CollectVisibleMaskRects' src/` returns nothing; `GrabbableModal.cs:711` records the removal (*"the `depthMask` parameter is gone WITH THE MASK ITSELF … now done by the draw ladder"*) and `ConvertedPanel.cs:1013` lists the five removed members by name. |
  | `:568-573` "`HostLateSync` exists because Update order is undefined" | **GONE from `GrabbableModal`.** It survives only in `Surfaces/SurfaceGrabBar.cs:552/:648` (lane worldui-front), and the entry's whole "Why" reasons from the depth mask. **Plus one LIVE stale cref in this lane:** `WorldUI/Conversion/CanvasConversion.2.Adopt.cs:1186` still writes `<c>GrabbableModal.HostLateSync</c>`. |
  | `:584-589` "the depth-mask quad's render state is exact" (queue 2999, +2 mm) | **GONE.** `WorldUI/MrBacking.cs:60` already speaks of it in the past tense. |
  | `:1726-1731` "`DepthMaskQuadPaddingPx` is 3, `DepthMaskMaxQuads` 256" | **GONE.** Neither identifier exists anywhere in `src/`. |
  | `:592-597` "the grab bar sorts at 1100" | **SUPERSEDED.** `GrabbableModal.cs:129-137`: *"this is no longer an absolute value (it was 1100 …). A fixed 1100 would have made the bar pierce every nearer panel"*; it rides the ladder at `BarOrderOffset` = 4. |
  | `:1697-1702` "laser carry translates only, then RETURNS" | **RULE TRUE, CREFS STALE.** `PanelGrab.cs:874-883` is intact, but `GrabCarriesYaw` is now `IPanelGrabOwner.CarryMode` / `enum PanelCarryMode` (`PanelGrab.cs:28`, `:75-81`). |
- **Partly superseded, lower value:** `:539-544` (the X button now has TWO canvases — the invisible
  HitPlane at 1100, `ModalCloseButton.cs:497`, and the visible X on the ladder at `XOrderOffset` +2,
  `:452`); `:1719-1724`/`:1396-1401` (`BarColliderPad` moved to `Core/GrabBarVisual.cs:78` and is
  described there in the past tense; `BarWidthFraction`/`ZoneWidthFraction` now live in
  `Grab/GrabBarLayout.cs:70/:76` — the RULE holds, only the "Where" is stale). And §1923 item 8
  states `InputModeGuard.Active` as `ForceMouseMode && ConversionActive`; at HEAD
  `Grab/InputModeGuard.cs:38` is `=> WorldUIConfig.ConversionActive` because *"`[WorldUI]
  ForceMouseMode` is GONE, user ruling 2026-08-13"*. Item 6's ladder still lists "depth mask 2999".
- **Proposed action:** RETIRE the four depth-mask entries — **mark them REMOVED with the round that
  removed them and a pointer to `CanvasConversion.8.Order.cs`, do not delete them**, for the same
  reason the `renderOnTop` entry at `:576` exists: to stop a future round "restoring" a mechanism
  that was deliberately taken out. Correct the 1100 entry, the two laser crefs, §1923 item 8 and
  item 6's ladder; widen the X-button entry to name both canvases. Separately fix the one live
  source cref at `CanvasConversion.2.Adopt.cs:1186` (same lane, comment-only).

### F-87 [B-03/B-06/B-14/B-18] — considered and left alone, each with the reason
- **`WINDOW RE-FACE` prints `RE-FACING` for sub-epsilon releases** (`Grab/WindowReFacePolicy.cs:131-141`
  vs `GrabbableModal.cs:929-932`): the policy is asked first and the epsilon test runs after, so a
  release that was already facing the player logs `RE-FACING` and then writes no rotation. **Leave
  it** — the line's stated job is to report WHICH RULE DECIDED, moving the Note below the epsilon
  test would be a structure change in three owners (one of them worldui-front) for a wording
  nuance, and the token is surface-checked. Recorded so nobody reads a turnless `RE-FACING` as a
  fault.
- **`LogOptionsKey` does not count a DESTROYED window** (`OptionsToggle.cs:368-369`): the
  `w == null` arm skips it, so a tap that destroyed a foreign window reads `TOUCHED NOTHING ELSE` —
  the ruling's own falsifier reading clean on a WORSE violation. **Leave it**: the fix needs a new
  verdict word inside a `check-surface.py`-owned string, which §4 forbids this phase. Recorded so a
  later round adds a `DESTROYED:` clause deliberately instead of discovering it from a log.
- **`MainMenuLogoSwap.cs` holds two top-level types, 2 039 lines** (`:126`, `:770`): **declined.**
  They are one concept split by role — the swap/restore bookkeeping and the measurement half — and
  the second reaches into the first on nearly every method (`MainMenuLogoSwap.Records`,
  `.SweepScene`, `.RunWatchTick`), which is the opposite of an unrelated neighbour. Contrast F-57,
  where `HeadEyeHeight` consults `PanelPlacement` and nothing more.
- **`OPTION NAME TOO LONG`** (`VROptionsTab.2.Rows.cs:393-401`, `Warn`) is the only reporter of the
  2026-08-03 "ellipsis is banned" ruling and no hardware log has carried one. **Leave it anyway:**
  it is deduped per KEY, not per session, over 362 localized names in two languages, so a German
  menu browse could emit dozens at once — precisely the flood ModBuild 331 removed. The right fix
  is a per-session SUMMARY line, which is a new instrument, not a tier change. Recorded so the next
  round starts from that design. (The remedy itself — shortening a name — is `Core/Loc`, lane core.)

### F-88 [B, closing] — verified, and the fourteen searches that came back empty
- **Prior-audit rows CLOSED at HEAD:** R1, R2, R18, R20, R41 (the assert now exists — and F-46 is
  about that assert), R45, and redundancy-audit §6.3. `an-audit-is-a-snapshot`, for the fourth and
  fifth time this round.
- **Checkers run read-only and green:** `check-hw-verify.py`, `check-options-coverage.py`,
  `check-instrument-writes.py`.
- **Empty searches, so nobody redoes them:** no empty `catch` anywhere in the 50 files (all 158
  `catch` sites log something); `DepthMaskQuadPaddingPx` / `DepthMaskMaxQuads` /
  `CollectVisibleMaskRects` — zero hits in `src/`; `GrabCarriesYaw` as a live member — zero;
  `ForceMouseMode` as a live config key — zero (six tombstone comments, no `Bind`); **the
  `ActorPropBody.cs:933` cadence-gate inversion, checked on EVERY `_next*`/`_last*` pair in the
  set — none is inverted**, every one advances before the expensive work and before every early
  return; no per-frame `FindObjectsOfType` (nine call sites, all tap-, scan-budget- or
  teardown-gated); no bound config key that nothing reads (the set contains exactly ONE `.Bind(` —
  `[Cheats] Enabled` — everything else is bound elsewhere and reached through `ConfigCatalog`); no
  curated/topic-tree row naming a missing key; no `List<UnityObject>` fake-null collision (three
  candidates chased and each cleared, with the reason); Unity objects as dictionary KEYS are safe
  because `GetHashCode()` returns the stable instance id (`MenuRowSeat.Originals` has no prune
  analogue — bounded growth, not a wrong result, recorded not raised); the only live copy-by-value
  in the set is F-82; **eight `Reset()` bodies checked and seven are complete** — the one exception
  is F-76; and `NonDominantHold.Reset()` vs `OptionsToggle._spentPressId` is NOT a cross-reset latch
  (it is an instance field on an object `Shutdown` destroys, and the test is `==`, not `<=`).
- **Volume discipline:** eighteen promotions are proposed across §7 and **every one is behind a
  one-shot latch, a per-release/per-tap edge, or a Harmony `TargetMethod` resolver. No
  per-frame-capable line is proposed.** The lines deliberately left silent are named individually in
  F-78, F-80, F-81 and F-87.
- **Coverage, honestly:** `OptionsToggle.cs` (936) was read whole; `ConfigCatalog.cs`,
  `MenuRowSeat.cs`, `VariantTiles.cs`, `VRMenuEntry.cs`, `VROptionsTab.1.Inject.cs`, `DevPanels.cs`
  and `VariantTilesTable.cs` at declaration+instrument+catch+latch level with named line ranges
  read in full; the ten remaining `VROptionsTab.*` parts and the topic/group tables by scan plus
  targeted ranges. The full per-file table is in the sub-reader file. **No file escaped all six
  automated scans** (tier census, catch census, latch census, double-`<summary>`, dead-member,
  cadence-gate).
