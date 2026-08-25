# The wall fade — closeout

> **Status: CLOSED**, on the user's own report against ModBuild 283 (2026-08-25):
> *"Ich habe den ersten Test gemacht mit den Wänden und ich nehme es jetzt schon als viel besser
> wahr! Ich bemerke keine Ruckler mehr bei der noch genausogut vorhandenen Logik mit den Mauern,
> top!"* — and, on the 281 run before it, *"Ich nehme es jetzt schon als viel besser wahr!"*
> beside a measured **worst commit 94.8 ms → 72.84 ms**.
>
> He has called the fade *behaviour* perfect twice. Everything in this file is written on the
> assumption that **the behaviour is the thing you must not break**, and that any future work
> here has to earn its way past that.

This is the account for someone who has never seen the subsystem. It is the closing document for
roughly twenty-five builds (ModBuild 251 → 283). The detailed per-build record lives in
`src/GloomhavenVR/Net/NetProtocol.cs` (newest first, at the top); this file is the map.

Companion documents, all still current:

| file | what it holds |
|---|---|
| `.planning/perf/FINDINGS.md` | the judder investigation and the ModBuild 278 measurement round |
| `.planning/perf/WALL-COMMIT-ARCHITECTURE.md` | the per-phase cost table, options A–E, the decodable-delta derivation |
| `.planning/perf/WALL-COMMIT-B-BUILD2.md` | the sliced/double-buffered commit — **designed, handed over, NOT built** |
| `.planning/perf/decode-signature-delta.py` | reads the signature delta out of any past log |
| `.planning/perf/verify-committedtable-noop.py` | mechanically undoes the ModBuild 282 rename and diffs |
| `.planning/wall-fade-stereo-rivalry.md` | the one-eyed fading item, parked |

---

## 1. What the subsystem does

A wall that stands between the player's eye and the floor he is looking at goes transparent, and
comes back when it does not. The decision is **per wall**, on that wall's own coverage of its
room's *frustum-visible playable floor tiles*, smoothed by an EMA, latched by a Schmitt trigger,
and delayed by a dwell. The user's own framing of the rule, which is the one the code implements:

> *"Es sollte anhand der verdeckten Boden-tiles des jeweiligen Raumes berechnet werden, oder?"*

The hard part is never the decision. It is **which renderers belong to which wall** — the game's
tilesets hang torches, banners, shelves, statues, hedges, water features and whole bookcases on,
in front of, over and around their masonry, through eight different delivery paths. That table is
what costs ~73 ms to rebuild, and it is what twenty-five builds were about.

### 1.1 The pipeline: Classify → Survey → Prepare → Commit

One *cycle* runs every `RescanIntervalSeconds` (2.0 s). It is a state machine
(`FadeDriver.RescanStage`, `WallSegmentFade.cs`), and only the last stage is allowed to write:

| stage | what it does | writes? | cost shape |
|---|---|---|---|
| **Classify** | walk every `Renderer` in the scene (or a reused snapshot) and derive eight verdict bits per renderer — mesh / particles / mountable / mod / wall-fade shader / foliage shader / water / active — from **one** `GetSharedMaterials` and **one** `r.name` (`ClassifyMaterialsAndName`) | no | sliced, `SliceBudgetMillis` (1.5 ms) per frame, ~46 frames |
| **Survey** | re-derive a 64-bit signature of everything the commit's output is a function of, so a cycle whose inputs have not moved can be skipped outright | no | sliced, same budget |
| **Prepare** | pay the commit's pure *derivation* ahead of the commit frame — `MeasureStandingUnit`'s subtree+material walks, the `PropUnitRootOf` climb, the per-`Shader` name memo | **memos only** — this is the PREPARE INVARIANT, stated on `StepPrepare` and verified mechanically | sliced, same budget |
| **Commit** | 24 mutation phases, in **one atomic frame** | yes, everything | ~73 ms, once per cycle *that is not skipped* |

**Why the commit is atomic and stays atomic.** The per-frame appliers read the segment table
every frame. A commit spread across frames is a table they can see mid-rebuild — and two of the
expensive phases (`EnforcePropUnitCohesion`, `CollectWallMountedProps`) *mutate segments in
place*, moving renderers between owners and hiding and restoring pieces. That is exactly the
"wall gone, thing still on it" class the subsystem spent sixteen builds on. ModBuild 273 briefed
"slice the commit" and the brief was **wrong**: holding the appliers off for 107 ms at 1.5 ms/frame
buys a 0.8 s window in which no wall may change its fade, against a fade time constant of 0.12 s
and an enter dwell of 0.20 s. A worse artefact than the stall.

### 1.2 The skip (PERF S5, ModBuild 275) — the change that actually worked

The subsystem was spending ~95 ms every two seconds rebuilding a table that came out **the same
sixty-six times running** (the PREPARE clause reported identical integers — 12819 renderers,
3105 unit roots — in 22 consecutive windows). So the fix is not "cheaper" or "spread", it is
**do not run**.

Survey hashes identity + verdict bits over every scene renderer and over every wall; if nothing
moved, the commit is skipped, `_segments` is untouched, and the appliers, the ramp, the Schmitt
trigger, the 0.12 s tau and the 0.20 s dwell all run exactly as before.

**It is fail-safe by construction**: both halves are sampled *earlier* in the cycle than the
commit that banks them, so a stale hash can only cause an **unnecessary commit**, never a wrong
skip. On his hardware it takes **~71 % of cycles** (80 of 113 in the ModBuild 277 log).

Two bugs the lane wrote and then caught, both of which would have made the change **silently
inert** — record them, because both are re-inventable:

- Folding `renderer.enabled` into the signature. *This subsystem hides things by writing that
  field*, so the signature would have moved on every fade and the skip would have fired almost
  never — precisely while the player is moving. Both halves key on **identity plus
  `activeInHierarchy`**, which is what the *game* flips and the fade system never touches.
- A drift probe baselined in `FinishRefresh` — taken **before** `StripGroundRenderers` and
  `EnforcePropUnitCohesion` remove members from the list it would be compared against, so every
  ground-stripped wall would have read as permanent drift.

Three fail-safes sit behind the skip so a signature blind spot cannot latch: a **staleness
ceiling** (30 cycles), a **segment-AABB drift probe** (`BoundsDriftEpsilonWU = 0.05 wu`, a ring
walked `BoundsProbeSegmentsPerCycle` at a time), and a materials check. In the whole ModBuild 277
log the ceiling fired 0 times and the drift probe fired 0 times. That is not proof the signature
is complete — only that nothing has caught it being incomplete.

### 1.3 The walk-in stand-down (ModBuild 271/272/278)

When the player zooms himself *into* the play field, every wall is held solid and nothing fades.
This is the **third** attempt at a scene-wide rule and the first two were rejected on hardware —
the full retirement record is the class header of `WallSegmentFade.Inside.cs` and it must be read
before anything here is touched. What is new in the third attempt is not the mechanism (which is
byte-for-byte the rejected ModBuild 251 one) but the **trigger**: it additionally requires the
board's wall crest to be at least `WalkInMinCrestMetres` (1.20 m) **in real metres**. Two
populations, no overlap: 0.61 m when he leans over his tabletop diorama, 1.62–1.88 m when he has
genuinely walked in.

ModBuild 278 added `WalkInSuspendSampling`: while the latch holds, the decision, the coverage
sweep, the rescan cadence and the path audit are all computing a verdict that is overruled by
decree one branch later, so they stop. Four things about the suspension are load-bearing:

- **An in-flight cycle runs to completion.** Abandoning it drops `_committedSigValid`, so the
  first cycle after release would refuse on "no table yet" and pay a guaranteed ~90 ms commit **on
  the frame he zooms out** — the hitch relocated to the worst possible moment.
- **The release edge is immediate because the clocks are left alone.** `_nextRescan`,
  `_nextPathAudit` and `_nextEvalTime` are never pushed forward while suspended, so all three are
  already in the past when the latch drops. No forced commit, no special release path.
- **A latch that could never let go**, found before it shipped: `_samplingSuspended` is read
  *early* in the tick and written *late*, after the `_segments.Count == 0` early return. Suspend,
  load a new scenario, and the flag would still gate the only thing that could refill the table.
  Release also runs from `ReleaseWalkInside` and the teardown branch.
- **The per-frame fade ramp and its material write do NOT stop**, so the walls come back through
  the ordinary animated un-fade. See §4.

### 1.4 The per-frame appliers

Independent of the cycle, every frame:

- the fade ramp per segment and its material / MPB write (`Apply(seg)`);
- `ApplyCornerPieces` — a shared corner takes the **min fade** of its adjacent walls (round 7);
- `FastReclaimRegeneratedShell` — Apparance regenerates wall renderers mid-fade, and a
  regenerated shell piece must be re-hidden faster than the 2 s cycle. Measured 7.5 ms/s
  (`WallFade.FastReclaim`), of which 4.9 ms/s is `WallFade.FastSweep`. **This is real work, not a
  diagnostic.**

---

## 2. The dials that shipped

23 live `[WallFade]` keys plus 2 one-shot migration markers. The ones that matter, and what each
actually buys.

### 2.1 The two cadences — and they are deliberately not both called "Abtastrate"

The user asked (2026-08-25) to be able to lower "die Abtastrate, also Frequenz in dem gecheckt
wird ob eine Wand etwas verdeckt". **His sentence names one cadence and his symptom is caused by
the other.** Both ship as dials.

| key | what it is | what raising it buys | what it costs |
|---|---|---|---|
| `RescanIntervalSeconds` (2.0, 0.5–15) | the **table rebuild**, ending in one atomic ~73 ms frame | divides the **NUMBER** of those frames — and by *less* than the ratio, because 71 % of cycles already find nothing, so doubling the period halves *opportunities*, not commits | decision latency, printed on the SKIP clause as *"the table in force stood at most X s without a rebuild"* |
| `EvalIntervalSeconds` (0, 0–0.25) | the per-frame **occlusion decision** — literally "wird gecheckt ob eine Wand etwas verdeckt" | a **~0.39 ms/frame** ceiling; see below | up to `2T` of added latency on *when* a decision lands |

**It shortens not one commit.** Do not tune `RescanIntervalSeconds` past 4.0 without reading a
`SIGNATURE CULPRITS:` line first — the useful number is the *churn timescale*, not the period.

**`EvalIntervalSeconds` and the number that was wrong.** My own brief read
`WallFade.Late 1.121 ms avg / 94.3 ms/s` as 9.4 % of wall-clock and asked for a dial that would
recover it. That figure is **INCLUSIVE**: `PerfMonitor.EndStep` folds a nested scope's full
duration into every enclosing name, and `WallFade.Late` wraps the whole tick. Level-1 children
come to ~51 ms/s, leaving **~43 ms/s (~0.43 ms/frame) exclusive** — and only the *decision* half
of that is gateable, the rest being the fade ramp, the material writes, `ApplyCornerPieces`, the
fade-write census and the inside/walk-in tests. So the real ceiling on this dial is **~0.39
ms/frame**, i.e. 1.7–3.5 % of an 11.11 ms budget — worth having, and **not** what he was
reporting. The inclusive figure reads **~2.4× larger than the exclusive cost**. Anyone quoting
`WallFade.Late` as a cost is quoting the commit's own price under a per-frame dial's name.

The residue was confirmed real rather than assumed by a clean natural experiment: a 30 s window
reading `WallFade.Late 1.054 ms avg, worst 8.15 ms, 71.2 ms/s` — `Late` is inclusive, so a worst
frame of 8.15 ms means **no commit landed in those 30 seconds at all**, and the subsystem still
cost 71.2 ms/s.

**Recommended value: 0.05** (20 Hz), and it is a recommendation, not a shipped default. It clears
three separate bars, derived in `FINDINGS.md` §4: four confirming samples against the 0.20 s
enter dwell (above `dwell/2 = 0.10` the dwell degenerates to a single sample and stops
debouncing); 0.10 s worst-case added latency, inside the 0.12 s fade ramp constant; and
`T/τ_EMA = 0.33`. The shipped default stays **0** in both `[WallFade] EvalIntervalSeconds` and
the older `[Optimize] WallFadeEvalInterval`, because he had just called the fade behaviour in
that level perfect and ~0.39 ms/frame does not buy the right to change when walls fade behind his
back.

The older key still applies while the new one is 0, **by precedence and not `max()`**, so
"I set 0.05 and nothing happened" stays diagnosable.

**The cadence trap, and why it is not one.** Six sites zero `_nextRescan` to request a prompt
cycle, and `_cycleOpenedEarly` detects that by comparing the gap against the cadence. With a live
dial that comparison would read a value the cycle was never scheduled with — and had it read too
small, **every** cycle would look asked-for and the 71 % skip would be silently defeated.
`_scheduledRescanInterval` latches the value each cycle was scheduled with. The falsifier is
shipped: the SKIP clause prints scheduled vs live side by side and totals an `asked for` counter
that read **0** across the whole 277 log.

### 2.2 The rest, in one line each

| key | default | what it buys |
|---|---|---|
| `OnFraction` / `OffFraction` | 0.25 / — | the Schmitt pair on the room's frustum-visible playable-floor coverage |
| `ExitDwellMovedSeconds` / `ExitDwellStationarySeconds` | 2.50 / 7.00 | un-fade dwell; rotation alone should almost never bring a wall back |
| `StackedShellFade` | on | fort/keep superstructures without a fade shader join their wall's occlusion box |
| `SyncPeerFades` | on | MP: fade what a teammate's fade hides. Receiver-side; own fades are always broadcast |
| `SplitRunUnified` | on | a wall run carved into pieces decides **once**, on the union |
| `SplitRunAdoptGroundScenery` | on | a split-run piece the standing FLOOR arm refuses rides its run as a passenger |
| `WalkInStandDown` + 6 trigger keys | on / 1.20 m etc. | §1.3. Every default is bit-equal to the constant it replaced |
| `WalkInSuspendSampling` | on | §1.3 |
| `SliceBudgetMillis` | 1.5 | one dial replacing three private consts (`Classify`/`Prepare`/`Survey`) whose own doc comments each said they were deliberately the same number. Also the budget the sliced commit will spend if B ever lands |
| `SignatureCulpritCensus` | **off** (was on for 278–283) | names WHICH renderers moved the scene signature. Its question is answered — §7.2 |
| `CommitTableGate` | **off** (was on for 282–283) | measures how much one commit churns the table — the population B's carry-forward must survive. Answered; B is not being built |
| `FigureExemptSkip` | off, **and the off is the finding** | §5.4 |

One migration wart worth knowing: the walk-in release dwell used to *follow*
`ExitDwellMovedSeconds` and got its own key at the same 2.50 in ModBuild 272. An install with a
hand-tuned `ExitDwellMovedSeconds` (his live cfg holds 0.5) will see the walk-in release stop
tracking it until he sets the new key.

---

## 3. Where the milliseconds are, today

Measured on his hardware, ModBuild 281 log:

```
WORST COMMIT   94.8 ms  ->  72.84 ms
leading phase  WallCache (61.89 ms in 271)  ->  PropUnits at 25.5 ms; WallCache now 10.4 ms
skip           ~71 % of cycles (80 of 113)
```

The history, for the record, because it used to be printed in a log line every 5 seconds and does
not belong there:

| build | what the instruments read |
|---|---|
| ModBuild 226 | the whole thing was ONE 118 ms frame every 2 s — `WallFade.Rescan 118.174 ms avg, worst 141.18 ms`, 15 stalls per 30 s window |
| ModBuild 228 | after PERF S2 (sweep + census sliced off): sweep 4.19 ms and rare, census 1.5 ms/frame over 12–18 frames — both fine. **WORST COMMIT 96.81 ms**, and one number for all 23 phases, so it could not say which |
| ModBuild 267 | worst commit 83.6–96.4 ms |
| ModBuild 271 | **WORST COMMIT 123.78 ms** — WallCache 61.89, PropUnits 29.94, Mounted 25.14. This is the build PERF S4 is measured against |
| ModBuild 273 (PERF S4) | 49.8 ms of pure derivation hoisted off the commit frame, 12819 renderers warmed, 3105 unit roots prewarmed, 0 refused, 0 dropped — **and the stall stayed.** Fifty milliseconds moved and it did not help. *That result is the finding*: hoisting more was the wrong direction |
| ModBuild 274 | 92.4–120.4 ms — ~17 ms **worse** than four builds earlier, on matched scene population (5803–5806 renderers, 1888 fade-capable in both logs) |
| ModBuild 277 | 33 commits, mean worst 85.6 ms, max 134.0 ms; p50 frame 11.10/11.72/11.98 ms across three windows, p99 26.5–29.9 ms |
| ModBuild 281 | **72.84 ms**, PropUnits leading at 25.5 ms — ModBuild 280's option E landing |

**Read the distribution, not the worst field.** The 92/104/120 scatter in the 274 window is
variance around a constant, not an occasional spike: the commit cost ~95 ms on *every* cycle
(WallCache 31.5, PropUnits 30.0, Mounted 22–26, the other 21 phases ~10 total, all spread under
1 ms).

---

## 4. The rulings that are not negotiable

Each of these is a user ruling with a date, or a hardware rejection. None of them is a threshold
that can be re-tuned; every one of them has been re-discovered at least once by a lane that did
not know it existed.

| ruling | source | where it is enforced |
|---|---|---|
| **Doorway segments never fade.** A recognised doorway is held permanently solid | user ruling 2026-08-02 | `seg.DoorRoot != null`; the `DOORWAY` flag on the diag line; its own bucket in the path audit |
| **Lights are never written to.** The subsystem writes renderers, materials and property blocks — never a `Light` | standing | no `Light` write exists anywhere in `WallSegmentFade*` |
| **Figures are never touched.** `ActorBehaviour` / `CInteractableActor` is an ABSOLUTE veto | round 7 | `PurgeFigureRenderers` is commit **phase 1**; `IsFigureOrActorRenderer` refuses in every adoption lane. See §7.2 for what this guarantee does *not* mean |
| **The water feature stays whole.** The Brunnen, its basin, bank and rim stay solid | user ruling 2026-08-09 | a spatial protection rect that **pulls** renderers off their wall (`IsWaterProtected`); its own path-audit bucket; `_propUnitWaterSkipped` |
| **The crystal formation stays**, and the low stone formation, and the well | user ruling, ALLOWED class 2026-08-24 | structurally unreachable from the ModBuild 275 figure-arm narrowing — the four-level bounded walk finds no prop-unit root for it; `ALLOWED` in the leftover classifier |
| **Walls return through the ANIMATED un-fade and never snap** | **ModBuild 271**, and it is why the walk-in suspension does not stop the ramp | the per-frame fade ramp and its material write are outside every suspension gate |
| **Each wall decides for itself.** No scene-wide fade switch except the one walk-in mode he asked for by name | user rejection of ModBuild 251, and his explicit lifting of the rule 2026-08-25 for that one mode | `WallSegmentFade.Inside.cs` class header |
| **Whole unit or nothing.** *"Entweder verschwindet die ganze Wand mit ALLEM was dazu gehört (Bäume, Gestrüp, etc.) oder sie ist vollständig da"* | user 2026-08-24, `neues_wandproblem.jpg` | `EnforcePropUnitCohesion`; a member this mod may not write refuses its **whole unit** |
| **Dressing stays hidden while its wall is faded.** *"das Gestrüp soll gar nicht mehr auftauchen, solange die Wand gefaded ist"* | user 2026-08-24 | ownership sticky while faded; a conceded piece is never switched back on while the segment still hides |

---

## 5. What was tried and rejected — do not rediscover these

### 5.1 The two failed stand-downs

- **ModBuild 241–250 — a raised bar.** While the head read INSIDE, the coverage pair 0.98/0.90
  was substituted for the live bars. **Inert**: `BlockedFraction`'s head-inside-AABB shortcut
  returned a hard `1f` from *open floor*, because the AABB it tested was the segment's union box,
  and 1.00 clears 0.98 exactly as easily as it clears 0.10. 75 diag samples in the 250 log carry
  that signature.
- **ModBuild 251 — a hard stand-down.** Every wall's decision forced off at once, with a
  mesh-keyed carve-out. It worked exactly as designed and the user rejected it in one build:
  *"Entweder alle grünen Wände verschwinden auf einmal, oder alle sind da. Ich will aber das jede
  Wand einzeln verschwinden kann und andere bleiben."*

The verdict written after 251 was "a scene-wide switch cannot express a per-wall fact". That is
**half** of it, and it is the half that was wrong about the mechanism. The record's own numbers
say the rest, and they say it about the **trigger**: in the 251 log the verdict flips four times
in one session and the deciding term is `FOOTPRINT` as often as `HEIGHT` (27 against 26), on a
board with **0.61 m walls** and his eyes 0.34 m above its floor plane. "INSIDE THE MAP" was firing
when he leaned over his own table. The discriminator both attempts lacked — the board's scale in
real metres — **was printed on every one of those lines while nothing read it.**

### 5.2 The stereo rivalry, parked

One-eyed wall fading. Unfixable on the game's own masonry shader without losing the dissolve.
Parked with its evidence in `.planning/wall-fade-stereo-rivalry.md`; do not reopen without a new
shader.

### 5.3 The shadow table — refused, and the refusal was right

The integrator's plan (ModBuild 282) was a **sliced shadow table** built beside the live one and
discarded, with an equality gate against the still-authoritative atomic commit — "changes nothing
on screen". The lane refused to build it. The reason is structural and is the single most
important thing in this section:

> **The commit phases are not pure functions of the scene.** They mutate ≥15 driver-level ledgers
> *outside* the table, **six of them do Unity writes**, and `CollectWallMountedProps`
> **drains and clears** `_mountedTouched`, `_mountedAnchorLedger` and `_mountedMobile` at the top
> of its pass. Run the commit twice per cycle and **the undo log is drained twice** — the second
> run restores props the first is still holding, or fails to restore props nothing else will.

A safe shadow needs write suppression through every phase plus save/restore of ~15 collections,
**and** it needs the slicing to already exist, or running the commit twice **doubles** the hitch.
Fifteen chances to corrupt the live ledger, in a build advertised as safe, on behaviour he had
just called perfect.

What shipped instead was a **churn gate**: the table *before* the commit against the table
*after* it, read-only, incapable of changing a pixel — exactly the population the carry-forward
cases are defined over. Its log line says **in its own words** that it is not the same question as
fresh-vs-incremental, rather than letting a number stand in for an answer nobody measured.

### 5.4 The figure exemption — shipped as a measurement, and it bought nothing

The design said: `PurgeFigureRenderers` is commit phase 1, therefore a figure provably cannot
change the commit's output, therefore figures can be dropped from the skip signature. **The
central claim is false, and it was falsified in the source, not on hardware:**

1. **The purge is RESTITUTION, not prevention.** It walks `_mountedTouched` only, and it
   **exempts `IsWallGeneratedDressing`** (`WallSegmentFade.cs:3950`). `CollectWallMountedProps`
   adopts a figure-classified renderer when that predicate holds — ModBuild 266, the cloth post
   under `Wall 4/Generated Content` — so such a renderer legitimately lives in the table and its
   `activeInHierarchy` really does decide the commit. *Closed* by defining the exempt class as
   `IsFigureOrActorRenderer && !IsWallGeneratedDressing`, the identical predicate the purge uses.
2. **NOT closed:** `WallSegmentFade.PropUnit.cs:1212` asks `IsActuallyDrawing` (enabled **and**
   `activeInHierarchy`) of every prop-unit MEMBER, five lines before the figure refusal at
   `:1267`. A figure inside a prop unit therefore decides by liveness alone whether the whole unit
   is refused. That arm fired **zero times** in the ModBuild 261 session — a reason to *expect*
   safety, not a proof of it.

So the dial shipped **off**, and the default is itself the finding. The asymmetry decides it: a
wrong skip costs thirty rescan cadences of a wall that should have opened and did not —
indistinguishable from the mod being off — while enabling it one build early buys a hitch *rate*
he had already said is not what he asked for.

**And on his hardware it bought nothing anyway.** `FigureExemptSkip`'s own shadow accumulator read
**0** in the ModBuild 281 window. The narrowing that *is* worth having is §7.2.

### 5.5 Smaller refusals, kept because each one is re-inventable

- **Hoisting the 40-entry name cap above `IsActuallyDrawing`/`ClassifyLeftover` is not free.**
  `_censusMountedLeftover`, its particle counter and the leftover class histogram all count *past*
  that cap deliberately. Hoisting truncates a **population count** at a **presentation** cap.
- **The squared-gap rewrite is a decision change.** Its premise — "every consumer is a
  comparison" — is false: the gap escapes into three census lines as a printed `F2`, and the
  adoption loops pick an owner with `gap >= bestGap`, where `sqrt` is monotonic but **not
  injective in float**, so two distinct squared distances can round to one root and hand a
  near-tie to the later candidate. That is a different wall owning a prop. Shipped instead: a
  degenerate-axis early return (`dz == 0 -> dx`), exact under IEEE-754.
- **Dropping the `Active` bit from the signature.** Refused: `activeInHierarchy` is a live
  candidate filter in the Mounted phase (`Mounted.cs:1218`) and is read by `IsActuallyDrawing`,
  `IsFigureOrActorRenderer` and `HasWallGeneratorAncestry`. Dropping it lets a real membership
  change through as a skip.
- **One deliberate waste kept:** Prepare does a `GetComponentsInChildren` the commit repeats,
  rather than handing the array over. Reusing it would miss a renderer created between the two
  stages for one rescan, for an estimated 3–6 ms.

---

## 6. The instruments that lied

This subsystem produced most of this project's examples. They are in
`~/.claude/.../memory/` individually; this is the local roster, because the same shapes will
recur here.

1. **`TORN N/M` — a ratio with two populations.** "TORN 1/2 written" counted *writes on one node*
   against *renderers in the whole subtree*. Under that denominator the top five defects were all
   fine. A ratio is only a finding when both terms are the same population.
2. **A truncated list read as absence.** ModBuild 260's UNCLAIMED name list was capped at **160
   characters** — six names out of 111 — and "X never appears" was quoted for two rounds off a
   list that had run out of budget. Every capped list in this subsystem now states
   *"named K of N, dropped M"* **in the line itself**. ModBuild 274's own log is the argument: its
   roll-call ended in a bare `…`.
3. **`grep -c` over a change-triggered census.** "It never fired" was a count over a line that
   only prints when its signature moves. It *had* fired — once, on a wall, and frozen it.
4. **The figure exemption's justification, falsified in source.** §5.4. The instrument was a
   *design document*, and the thing it asserted was checkable by reading two files.
5. **A sentinel-overflow cadence that never fired.** `now - int.MinValue` overflows, so the
   cadence never came due and the probe read as clean. Log the *failure*, not only the success.
6. **A held instrument reads as dead.** A change-gated line whose reason is a constant prints once
   and then looks like a stopped tick. Every change-triggered census here folds its own roll-call
   into its signature for exactly this reason (`LogStandingPropCensus`, ModBuild 266/275).
7. **A vacuous all-clear.** The path audit's guard returned when there were no cache **walls**,
   never when the walls held no **renderers** — so a wall cache registering before the tileset's
   meshes exist reached *"0 UNCLAIMED — every wall renderer is owned by a path"* with every bucket
   at zero. Both hardware sessions printed exactly that. The line now says **NOTHING MEASURED** and
   refuses to be read as an all-clear.
8. **A false positive built into a classifier.** `StripGroundRenderers` removes a water-protected
   renderer from both lists, and the water surface stands *above* the ground band by construction
   — so the Brunnen the user expressly allows landed in `UNCLAIMED [ALARM]` with nothing wrong
   with it, and its name was quoted as a defect. Water and the doorway arch now have their own
   buckets, **asked before the alarm**.
9. **A denominator that was never evaluable.** 127 of 171 cache walls read fail-safe solid on the
   same heartbeat as "111 UNCLAIMED", because a wall with no room decision got
   `ceiling = -inf` and *every* ground renderer fell through to the alarm. The band is
   **unevaluable** for those, not failed, and has its own bucket.
10. **The instrument's own first output is a hypothesis.** `SignatureCulpritCensus` shipped with a
    NULL control, a known positive, order-freedom and the no-baseline third state validated in CI
    (+41 assertions) *before* anybody was allowed to believe it. Its null-diff output is the
    self-accusing string *"NOTHING MOVED, AND THAT IS A FINDING ABOUT THIS INSTRUMENT, NOT ABOUT
    THE SCENE"*. The churn gate got the same treatment: two null controls, one known positive per
    snap case, one per ownership class, a one-ULP bounds difference compared **bitwise** (a
    tolerance here would be a hidden dial deciding how wrong the slice may be), and it **disarms
    for the session on a throw**, warning that a session ending without a gate line reported
    *nothing*, not agreement.
11. **Timing the instrument inside the thing it judges.** The churn gate sits **deliberately
    outside** `WallFade.Rescan` and `_cycleWorstCommitMillis`. Inside, it would fold itself into
    the very number the round is judged against.
12. **`WallFade.Late` read as an exclusive cost.** §2.1. `PerfMonitor.EndStep` is inclusive; the
    figure reads ~2.4× larger than the exclusive cost, and a whole dial was briefed against it.
13. **A gate blind to three of seven classes agrees with a torn table.** The design's ownership
    gate walked four of a segment's seven ownership classes. See §7 of
    `WALL-COMMIT-B-BUILD2.md`.
14. **A first draft that timed a population that does not exist.** The churn gate's first version
    measured 13,335 renderers — 15 in *every* class — and reported headroom from that. The real
    figure is 127 segments / 2540 owned renderers, 0.136 ms.

---

## 7. Two findings that outlive this subsystem

### 7.1 The signature delta is decodable — every past log already carries the answer

The fold is

```
h = ( a ^ bits ) * P        a = (FnvOffset ^ instanceID) * P,   P = 1099511628211
```

with `bits` in the low 8 bits only, and `FoldSceneFact` **sums**. So a change confined to `bits`
gives

```
sumDelta = δ · P   (mod 2^64),   |δ| < 256
```

while a change to the renderer *set* gives a full-width pseudo-random delta. `P` is odd, hence
invertible mod 2⁶⁴, so **δ is recoverable exactly**. The `LAST REFUSAL` clause already prints
`banked <sum>/<xor>, live <sum>/<xor>` — **every refusal in every log already carries a decodable
statement of what moved, and for several builds nobody read it.**

`.planning/perf/decode-signature-delta.py` does the read-out. Over the 17 distinct
scene-signature refusals in the ModBuild 277 log:

| decoded | count | reading |
|---|---|---|
| `δ = +128` | 1 | exactly one renderer's `activeInHierarchy` went false → true, nothing else |
| `δ = −128` | 2 | exactly one renderer went true → false |
| `δ = −384` | 2 | exactly three renderers deactivated, nothing else |
| `δ = 0`, xor moved | 1 | anomaly, open — see below |
| full-width | 11 | the renderer **set** changed |

**It self-validates.** The decoder is not fitted: it recovers exact small integers (128, 384) that
are precisely the `BitActive` weight and 3×. Random 64-bit data through the same inverse gives
uniformly-distributed garbage, which is what the other 11 lines give. That is a null control and a
known-positive control in one table. A second, independent confirmation: the *xor* deltas of the
Active-flip lines cluster at bit 47 and the low 16 bits — exactly where `128·P = 2⁴⁷ + 0xD980`
puts them — which is the carry pattern of `x XOR (x + δ·P)`.

**Caveats, stated:** these are `_lastNoSkipDetail` strings, one sample per BUDGET window, so 17
samples against 28+ refusals; and the delta is **identity-independent** — it names which *bit*
moved, never which *renderer*. The `δ = 0` line (sum identical, xor moved by `0x20700`) is an
anomaly and remains open: a single add or remove cannot preserve the sum while moving the xor.
`AdoptCommittedSignature` copies both accumulators from the same instant, so the pair cannot be
split across cycles. One line of 17 — do not build on it.

**This technique generalises to any FNV-style additive signature in this repo.**

### 7.2 We were the churn — and the fix is sound, provable, and NOT implemented

**This is the first thing to do if the wall topic reopens.**

The ModBuild 281 log's culprit census: of **38** refusals carrying named groups, **12 were
triggered by nothing but the mod's OWN objects** —

```
GloomhavenVR.Reticle_Right   24
GloomhavenVR.Laser_Right     11
the loading indicator          2
```

**The mod's own hand laser appearing costs a ~73–90 ms wall-table rebuild.** That is **32 % of
refusals**, caused entirely by us.

Those provably cannot change a fade verdict, and the proof is already in the shipped code:

- **`IsModObject` is ALREADY one of the eight classified verdict bits** (`f.Mod`, set from
  `layer == VRLayers.ModLayer || name.StartsWith("GloomhavenVR.")`).
- **`CountsTowardFadeUnit` excludes mod objects by construction.**

So the signature is listening to a population **its own consumer discards**. That is a sound
narrowing where the figure one (§5.4) was not: there is no ancestry predicate to go stale, no memo
window to keep warm, and no "provably cannot" resting on a purge that turns out to be restitution.

**The design.** For a renderer with `f.Mod` set, fold **identity plus the Mod bit only**, exactly
as `NarrowedBits` already does for `f.Figure` — one more arm in
`WallSegmentFadeCulprits.NarrowedBits`, which is in the Unity-free file and is already driven
exhaustively over all 256 bit patterns by the wire suite.

**The failure mode, named.** `IsModObject` is a *name-prefix or layer* test, not an ancestry
climb, so it cannot go stale the way the figure predicate can. The residual hole is: a mod object
**re-parented under, or re-layered into, a game object** without its name changing would flip the
Mod bit — and the flip itself is folded, so the crossing is caught. What is *not* caught is a
renderer that is wrongly classified Mod for a whole cycle and whose real change is therefore
dropped. The backstops are the same two that already stand behind the skip: the 30-cycle staleness
ceiling and the segment-AABB drift probe. **Ship it with both signatures computed every cycle and
the culprit census on**, exactly as the figure arm shipped, so the first hardware run names what
the narrowing dropped.

**Expected yield: ~32 % of refusals**, measured (not a hypothesis — these are named groups, not
decoded deltas). And unlike A, it needs no dial to be safe.

**Say plainly what it is:** this is statistical. It makes hitches **rarer**. Each remaining commit
still costs ~73 ms, and he has said rarer is not what he asked for. It is worth doing because it
is nearly free and because it shrinks how often B's slicing would have to run — not because it
solves his complaint.

---

## 8. What is still open

- **The ~73 ms commit itself.** Untouched. `PropUnits` is now the leading phase at **25.5 ms**
  (it was 29.94; `WallCache` fell from 61.89 to 10.4). `Mounted` is next. Both live in
  `WallSegmentFade.PropUnit.cs` and `.Mounted.cs`, both mutate segments in place, and the fade
  behaviour there is now reported **correct** — so any change needs a gate proving the resulting
  table *identical*, not "looks right".
- **PERF B build 2** — the sliced, double-buffered commit. Fully designed in
  `WALL-COMMIT-B-BUILD2.md`, including the five things that will bite and one **open question the
  lane refused to resolve by assertion**: `_sampleVisible` is tick-owned but index-aligned to
  `AllSamples` with no invalidation path, so it is stale by construction on the swap frame, and
  whether that matters is recorded as **unsettled**. It stays a plan.
- **The mod-object narrowing** (§7.2). Sound, measured, not implemented.
- **The `δ = 0` signature anomaly** (§7.1). One line of 17.
- **Stereo rivalry / rim items**, parked in `.planning/wall-fade-stereo-rivalry.md`.
- **`Mounted`'s in-place mutation of *other* segments' lists mid-loop.** ModBuild 273 left it
  alone deliberately: every input it needs is produced by the phases immediately before it, so
  suspending it across a frame boundary is the prop-with-two-owners class.

---

## 9. The ModBuild 284 cleanup — what was retired, and what was kept

The user, 2026-08-25: *"Dokumentier alles und räum dann den Code auf — auch entsprechende
Log-Einträge die wir jetzt nicht mehr brauchen."*

**The rule applied.** A **spent probe** is one whose question has been answered and cannot come
back; it costs interop and string building every cycle and tells nobody anything now — retire it.
A **record** is a block comment saying "this was tried and here is why it must not be re-added" —
keep every one, and move it to the nearest surviving place rather than losing it.

**The constraint:** *no decision predicate changed.* Every removal below was checked for a write
before it was made.

### 9.0 The first finding is that there was almost nothing to delete

A whole-subsystem sweep of 506 private fields and ~400 private methods across all 22 files found
**zero** unused fields, **zero** uncalled private methods, **zero** unreachable branches, **zero**
dead locals, and **zero** `_scratch` collections that are filled and never consumed (all 17 are
rented and read). What it did find is a very large **diagnostic surface**: ~58 fields whose only
read site is inside a log string. That is not dead code — it is the price of the instrumentation,
and the instrumentation is why the subsystem converged. The cleanup below is therefore about
*what still runs*, not about *what is unreachable*.

Exactly one field was a genuine defect, and it was an honesty defect rather than a dead one.

### 9.1 Retired

| what | why it is spent | measured saving |
|---|---|---|
| the `BUDGET:` line's history prose | it narrated the ModBuild 226 / 228 / 271 numbers in a line printed every 5 s so a reader could compare. The comparison baseline belongs in §3 of this file, which is where it was being read from anyway | **343 characters** and **5 constant `Append` calls** per BUDGET line |
| `_cycleCommitFrames` and the `COMMIT SPREAD: N frame(s)` clause | a field **initialised to 1 and never assigned anywhere**, printed as if it were a measurement. A literal wearing a counter's clothes. The clause beside it already states the same fact honestly ("24 of 24 phase(s) still run ATOMICALLY"), and the parenthetical explaining *why* was 175 characters of standing prose | **175 characters**, **3 `Append` calls**, **1 interpolation hole**, and one field. Total with the row above: **518 chars / 8 appends per line, every 5 s** |
| `BankFactCensus` running while its consumer is off | it built a ~5800-entry `Dictionary<int, Banked>` on **every commit**, **ungated**, to feed `LogSignatureCulprits` — which *is* config-gated, and has been since ModBuild 278. An ungated cost behind a gated instrument | **~5800 `GetInstanceID()` interop calls + ~5800 dictionary inserts + ~5800 struct constructions, per commit** (~30 commits per session), whenever the census is off. It also now invalidates the bank, so a mid-session flip-on reports the designed no-baseline third state instead of diffing against a ten-minute-old scene |
| `SignatureCulpritCensus` default → **off** | its own default comment shipped it on "because it is the whole point of the build". That build is ModBuild 278; the question was answered in the 281 log (§7.2) and the answer is written down. **Dial, instrument and all four CI-validated controls kept** | with it off: the row above, plus a ~5800-entry `List<Entry>` build, a 5800-lookup diff and a grouped format, **once per BUDGET window on a refusing cycle** (~0.2 log lines/s) |
| `CommitTableGate` default → **off** | same argument in its own words: shipped on because "shipping it OFF would hand the tester a build that measures nothing". It measured — the churn population and its cost are in `WALL-COMMIT-B-BUILD2.md` — and PERF B build 2 is not being built | **0.136 ms per commit** (measured; 0.346 ms at 4× population) plus two ~2540-entry table snapshots per commit, and **0.05 log lines/s** |
| the `WALL-PATH AUDIT` **cadence**, not the audit | 2.29 ms avg / **8.4 ms per second**, the second-largest per-frame cost in the subsystem, re-deriving an unchanged verdict every 2 s on a closed topic. It is **not** retired, because it is the only instrument that can raise `UNCLAIMED > 0`. It now backs off 2 → 4 → 8 → 16 → 32 → 60 s while its own bucket tally does not move, and snaps back to 2 s the instant it does | at convergence **8.4 → ~0.26 ms/s (~32×)** and **0.5 → 0.017 log lines/s**. First-alarm latency on a moving scene is **unchanged at 2 s**, because a moving tally never converges |

**Per-second totals, steady state, converged, defaults as shipped:** roughly **0.73 log lines per
second removed** out of ~4–4.5, and the largest single per-frame cost in the subsystem that was
not doing work is down by 32×. The two dial flips additionally remove ~5800 interop calls and
~5800 dictionary inserts from every commit frame.

**Nothing above changed a decision predicate.** Every removal was checked for a write first, and
two of them turned out to have one — see §9.3.

### 9.2 Kept, and the regression each would catch

| kept | what it is the only thing that would catch |
|---|---|
| `WALL-PATH AUDIT` | a **new tileset whose asset family falls through every delivery path**. `UNCLAIMED > 0` is the alarm and there is no other instrument that can raise it. It is the line a reader greps to decide a new tileset is covered |
| `diag:` (top-3 segments, every 2 s) | a wall stuck faded, a wall that never fades, a coverage number that has gone constant, a room with no grid (`!NOGRID`), a sample plane above the wall (`!ABOVE-WALL`), an unanchored room |
| `PER-WALL` | *"a session that never goes mixed is the defect reported on 2026-08-24"* — the per-wall-independence claim, which is the user's central ruling, made into a number |
| `SPLIT RUN` + leftover breakdown | the whole-unit ruling: a run that fades while its members stay, and the orphan population |
| `SIGNATURE CULPRITS` (behind its now-off dial) | the reopen path for §7.2, with its four CI-validated controls intact |
| `WallFade.FastReclaim` / `WallFade.FastSweep` | **not diagnostics** — real work, re-hiding Apparance-regenerated shell pieces faster than the 2 s cycle |
| the drift probe and the staleness ceiling | **decision predicates**, not diagnostics: they are the two fail-safes behind the skip |
| `STANDING PROP` / `PROP UNIT` census (commit phases 22 and 23) | 2 of the 24 commit phases are log lines — but both are already change-triggered on a composite signature that includes their own roll-call, and ModBuild 280 threaded a `describe` flag through `ChooseOwner` so the sentences are built only where consumed. In steady state each is a handful of `Count` reads and a return. Nothing to win here |
| `GATE-LIFT fade ON/OFF` and `PEER-SYNC fade ON/OFF` | a gate-lifted wall and a peer-driven wall never flip `seg.State`, so `LogStateFlip` is silent for both. These are the **only** evidence either mechanism ever fired — a peer sync that quietly stopped delivering is otherwise indistinguishable from a teammate whose walls happen not to be faded |
| the drift probe and the staleness ceiling | **decision predicates**, not diagnostics: they are the two fail-safes behind the skip. The probe builds a string only on its *failure* path, which has fired 0 times — there is no chatter to retire |
| the `FigureExemptSkip` shadow signature | the narrowing itself is off and bought nothing (§5.4), but `WallSegmentFadeCulprits.NarrowedBits` is the exact place the §7.2 mod-object arm goes, and it is already driven exhaustively over all 256 bit patterns by the wire suite. Its per-cycle cost is two extra hash folds per renderer — ~0.02 ms, arithmetic only, no allocation and no interop — so gating it would cost a branch in the hot classify loop to save nothing |
| every capped list's *"named K of N, dropped M"* | instrument lie #2 |
| every change-triggered census's roll-call-in-signature | instrument lie #6 |

### 9.3 Two writes that live inside instruments — found, and made un-deletable

The brief for this cleanup warned that removing a diagnostic must not remove a side effect. Two
sites in this subsystem were exactly that, and both were one grep away from being retired by a
future "delete the `Log*` methods" pass:

1. **`LogSamplingResumed` was the walk-in suspension's release.** `_samplingSuspended = false`
   happened inside it; the log line was the passenger. Sitting in a file full of `Log*` methods
   that really are log-only, it read as retirable — and retiring it would have latched the wall
   fade **off for the rest of the session with nothing in the log**, which is precisely the
   deadlock its own doc comment describes two paragraphs higher. **Renamed to
   `ReleaseSamplingSuspension`**, with the reason recorded at the site.
2. **`PurgeFigureRenderers` ran the round-7 restitution inside its name-building loop.**
   `RestoreProp(p)` was interleaved with `names.Append(p.Renderer.name)`, so trimming the WARN's
   string building would have deleted the restitution. **Split into two loops** — order-preserving
   and read-identical, because `_figurePurgeScratch` comes from `_mountedTouched`, which is keyed
   *by renderer*, so no two entries share one and restoring one cannot change another's name — with
   the reason recorded at the site in capitals.

Audited and **cleared** as genuinely log-only despite writing a `Segment` field:
`LogGateLiftEdge` (`seg.GateLiftActive`) and `LogRemoteFadeEdge` (`seg.RemoteFadeActive`). Grep
each: declaration, one compare, one assignment, nothing else. Both are their own line's edge latch,
not a decision input. Both are kept for the reason in §9.2.

### 9.4 Two premises in the cleanup brief that did not hold

- **"The drift probe's chatter."** There is none. `SegmentBoundsStillWhereTheCommitLeftThem`
  builds a string only when it *refuses*, and it refused 0 times across the whole ModBuild 277
  log. It is also a decision predicate, so it was never a candidate.
- **"`docs/PATCH-NOTES.md` (the wall-fade section)."** That file has no wall-fade section and
  should not get one: it is the hand-written Phase-5 *Harmony patch* companion to the generated
  `docs/PATCH-INVENTORY.md`, and its own header says newer subsystems are documented in a block
  comment where they live. The wall fade is a driver, not a patch target. This file is its home.
