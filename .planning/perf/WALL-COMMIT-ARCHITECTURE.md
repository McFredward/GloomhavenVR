# The wall-fade commit: what it costs, and the design for A + B

> Measurement lane, 2026-08-25, against `5046ba16` (ModBuild 278). Log evidence is the
> ModBuild 277 hardware session (`.planning/debug/Player.log`).
>
> **No headset was available to this lane.** Every number below is either (a) read off the
> shipped log, (b) derived arithmetically from the log plus the source, or (c) explicitly
> labelled as needing a hardware run. Nothing here is a desk estimate wearing a measurement's
> clothes.

---

## 0. Lead: three findings that change the shape of the round

**1. The commit is not 85.6 ms with a spread. In the big scenario it is 94.8 ms with a
standard deviation under 1 ms, and a separate 134 ms tail with a known cause.**
The brief's "mean worst 85.6 ms" averages small early-scene commits (18–48 ms) together with
the steady state. Split properly (§1), the steady state is extremely reproducible, which
itself says the work is proportional to a fixed scene population and not to churn — and that
is what makes it schedulable.

**2. The narrowing the coordinator proposed for Option A is already implemented.**
`ClassifySlice` (`WallSegmentFade.cs:4386`) already folds *verdict bits*, not a raw renderer
set: `FoldSceneFact(FoldSig(FoldSig(FnvOffset, r.GetInstanceID()), bits))` with eight verdict
bits. It does not hash bounds, names, materials or positions. The premise "if a renderer that
can never enter a segment can move the signature, the commit is being triggered by things that
provably cannot change its output" is *true*, but not for the reason given — and the fix is a
different one. §2 gives the term-by-term reading and the narrowing that is actually available.

**3. There is a third option nobody has costed, it needs no hardware, and all three phase
inventories found it independently: a large share of the commit is diagnostic work that is
built and then thrown away by a cap.** Eager `why`-strings at reject sites, `.name` interop
reads feeding a 64-entry roll, `StandsOnFloor` formatting 2–5 strings per renderer per commit,
uncapped census entry back into Unity. This is neither Unity-API-bound nor decision-necessary.
It is filed here as **Option E** (§5) and it should be done first, because it shrinks whatever
Option B then has to slice.

---

## 1. The number, and its distribution

### 1.1 The steady state

Eight BUDGET windows in the ModBuild 277 log show the big scenario at rest
(12,819 warmed wall-cache renderers, 3,105 unit roots, 5,797 classified renderers,
1,888 fade-capable + 2 water indexed, 99–127 segments):

| phase | worst single cycle | window total ÷ committed cycles |
|---|---|---|
| WallCache | 30.70 – 31.14 ms | 30.6 – 30.9 ms |
| PropUnits | 28.89 – 29.36 ms | 28.9 – 29.2 ms |
| Mounted | 24.08 – 26.44 ms | 24.2 – 26.4 ms |
| other 21 phases | — | ~10.4 ms |
| **commit** | **93.4 – 96.8 ms** | **~94.8 ms** |

Coefficient of variation across those windows is **under 1 %** for all three heavy phases.
That is not a noisy measurement; it is a fixed amount of work over a fixed population.

### 1.2 The tail, and its cause

Three windows show WallCache at **48.50, 50.79 and 57.92 ms** instead of 30.8, with commits of
**114.3, 116.3 and 134.0 ms**. In those same windows the PREPARE clause reports fewer renderers
warmed (**8,546** in one, against 12,819 elsewhere). The tail is therefore **prepare-incomplete
cycles**, not variance: a cycle that commits before the read-only warm stage has finished pays
the cold subtree walks inside the commit frame.

Derived rate: warming 4,273 extra renderers removes ~18–27 ms from WallCache
(≈ 5 µs/renderer removed), while Prepare pays ~46 ms / 12,819 ≈ 3.6 µs/renderer to do it.
Those are consistent, and they are the best available empirical handle on the cost of one
renderer's worth of this work.

### 1.3 The Unity-API-vs-computation split — what I can and cannot say

**I cannot give you this number.** It cannot be derived statically and it cannot be measured
without a Unity runtime, which this lane does not have. What the three phase inventories
(§6) establish is that the honest decomposition has **three** buckets, not two:

| bucket | what it is | can it leave the main thread? |
|---|---|---|
| **U — Unity-bound** | `Renderer.bounds`, `GetComponentsInChildren`, `Object.name`, `GetSharedMaterials`, `Material.HasProperty/GetFloat`, `Transform.parent`, `Renderer.enabled`, `SetPropertyBlock`, `Object.Destroy` | No. Main thread, always. |
| **C — decision computation** | AABB unions, the three O(candidates × segments) geometric matches, claim scoring, `ChooseOwner`, dictionary/list building | Yes, over a snapshot. |
| **D — discarded diagnostics** | `why`-strings built past a cap, `StandsOnFloor`'s 2–5 strings/renderer, `.name` reads for capped rolls, uncapped `IsActuallyDrawing`/`ClassifyLeftover` re-entry | **It should not run at all.** |

The brief framed the question as U-vs-C because that decides worker-thread viability. With C and
D off the table this round, the number's job changes: it is now a **sizing input for B's slicing
budget** and a way to know how much of the 94.8 ms Option E can delete. Bucket D is the one
worth measuring first, and it is the cheapest to measure.

**Handed over: the instrument spec is §7.** It is one build, off by default, and the next
hardware run answers it.

---

## 2. Option A — narrowing the commit trigger

### 2.1 What the signature hashes today, term by term

Two independent gates, both in `CommitWouldChangeNothing`
(`WallSegmentFade.Prepare.cs:1080`), evaluated in this order after the reveal / asked-for /
materials-dirty / board-moved gates:

**The SCENE half** — `_sceneFactSigSum` / `_sceneFactSigXor`, folded in `ClassifySlice`
(`WallSegmentFade.cs:4307-4388`), once per snapshot entry per cycle:

```
per live renderer:   FoldSceneFact( FoldSig( FoldSig(FnvOffset, r.GetInstanceID()), bits ) )
per dead entry:      FoldSceneFact( DeadRendererSigTerm )      // fixed constant 0xD1CE…CF
```

with `bits` (`WallSegmentFade.cs:4378-4385`) =

| bit | term | source |
|---|---|---|
| 1 | `f.Mesh != null` | `r as MeshRenderer` |
| 2 | `f.Particles` | `r is ParticleSystemRenderer` |
| 4 | `f.Mountable` | `IsMountableRendererType(r)` |
| 8 | `f.Mod` | layer or `"GloomhavenVR."` name prefix |
| 16 | `f.WallFadeShader` | material shader family |
| 32 | `f.FoliageShader` | material shader family |
| 64 | `f.WaterSurface` | shader + name family |
| 128 | `r.gameObject.activeInHierarchy` | live |

Accumulated **commutatively**, by SUM *and* XOR (`Prepare.cs:619-626`), because
`FindObjectsOfType` order is unspecified.

**Deliberately NOT folded**, each with a stated reason in the source: bounds (accepted as up to
one interval stale, with the drift probe underneath), `renderer.enabled` (this subsystem writes
it, so folding it would gate the remedy behind its own side effect — `cs:4356-4372`), materials
(carried explicitly via `_censusMaterialsDirty`).

**The WALL half** — `_surveySig`, an order-*sensitive* FNV chain over the wall cache
(`Prepare.cs:690-781`): per wall its instance id + split-anchor flag, then the wall count, then
per subtree renderer its instance id, then `_factCount`, `_factWallFade.Count`,
`_factWater.Count`.

### 2.2 So the coordinator's proposed narrowing is already the shipped design

> *"The narrowing therefore has to hash the verdict bits, not the raw renderer set."*

That is what `ClassifySlice` does and has done since PERF S5. There is no raw-renderer-set hash
to remove. **Do not spend a round re-deriving this.**

The 5,797-vs-1,888 gap is also not the lever it looks like. `1,888` is
`f.Mesh && f.WallFadeShader` — the *adoption sweep's* input only. It is not the set of renderers
that can enter a segment. Wall dressing, foliage, siblings, stacked shell pieces, mounted props
and prop-unit members all enter through subtree walks from a wall root
(`MeasureStandingUnit`'s `GetComponentsInChildren(includeInactive: true)`,
`Standing.cs:820`), and those renderers carry no wall-fade shader at all. **Nearly every
MeshRenderer in a dressed room is a potential segment member.** A narrowing keyed on
"can this renderer enter the table" would exclude almost nothing.

### 2.3 What I *can* tell you with no hardware at all: the deltas are decodable

This is the useful result. Because the fold is

```
h = ( a ^ bits ) * P        where a = (FnvOffset ^ instanceID) * P,  P = 1099511628211
```

and `bits` occupies only the low 8 bits, a change confined to **bits** produces

```
sumDelta = δ * P        with |δ| < 256
```

while a change to the renderer *set* produces a full-width pseudo-random delta. `P` is odd, so
it is invertible mod 2⁶⁴ and `δ` is recoverable exactly. The `LAST REFUSAL` clause already
prints `banked <sum>/<xor>, live <sum>/<xor>` — **so every refusal in every log already carries
a decodable statement of what moved, and nobody has been reading it.**

Decoding the 17 distinct scene-signature refusals in the ModBuild 277 log
(script: `.planning/perf/decode-signature-delta.py`):

| decoded | count | reading |
|---|---|---|
| `δ = +128` | 1 | exactly one renderer's `activeInHierarchy` went **false → true**, nothing else changed |
| `δ = −128` | 2 | exactly one renderer went **true → false** |
| `δ = −384` | 2 | exactly **three** renderers deactivated, nothing else |
| `δ = 0`, xor moved | 1 | sum-preserving multiset change — see the caveat below |
| full-width delta | 11 | the renderer **set** changed (something appeared or died) |

**Self-validation.** The decoder is not fitted to the data: it recovers exact small integers
(128, 384) that are precisely the `BitActive` weight and 3× it. Random 64-bit values run
through the same inverse produce uniformly-distributed 64-bit garbage, which is what the other
11 lines give. That is a null control and a known-positive control in the same table.

**Caveats, stated rather than smoothed over:**
- These are the **`_lastNoSkipDetail` strings**, i.e. *one sample per BUDGET window*, overwritten
  by the most recent refusal. 17 samples against 28+ refusals. It is a sample of the population,
  not the population. Treat "5 of 17 were pure Active flips" as ≈ 29 % ± a lot.
- The delta is **identity-independent**: `δ` does not depend on `a`, so it tells you *which bit*
  moved but never *which renderer*. That is precisely the gap `SignatureCulpritCensus` fills.
- **A note in favour of the model, before the one against it.** The *xor* deltas of the
  Active-flip lines are structured in the same bit positions as `δ·P`
  (`000F80000005A380`, `000C80000005E780`, `0001800000096F80`, `0000800000012980` — all cluster
  at bit 47 and the low 16, exactly where `128·P = 2⁴⁷ + 0xD980` puts them). That is the carry
  pattern you get from XOR-ing `x` with `x + δ·P`, and it is a second, independent confirmation
  that these lines really are single-bit changes on one or three renderers.
- The `δ = 0` line (sum identical, xor moved by `0x20700`) **is an anomaly and remains open.**
  A single add or remove cannot preserve the sum while moving the xor; it needs a compensating
  multi-element change, which real hashes make astronomically unlikely. I ruled out the obvious
  causes: `AdoptCommittedSignature` (`Prepare.cs:924-928`) copies both accumulators together
  from the same instant, so the pair cannot be split across cycles, and both values in the
  string are formatted at the moment of refusal. A replayed `ClassifySlice` would give the
  opposite fingerprint (sum moves, xor does not). **One line of 17 — do not build on it, and
  have the implementer of §2.5's shadow signature print `_factCount` alongside the pair so the
  next occurrence is attributable.**

### 2.4 The narrowing that is actually sound — and its named failure mode

**Not available: dropping the `Active` bit.** I checked whether `activeInHierarchy` can change
the commit's output. It can:

```
Mounted.cs:1218:   if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
```

is a live candidate filter in the Mounted phase, and `IsActuallyDrawing`
(`Mounted.cs:1216-1227`), `IsFigureOrActorRenderer` (`cs:6320`) and `HasWallGeneratorAncestry`
(`cs:6453`) all read it. Dropping the bit would let a real membership change through as a skip.
**Refused.**

**Available: exempt the renderers this subsystem is constitutionally forbidden to touch.**

The round-7 ruling is absolute and is enforced as *phase 1 of the commit itself*:

```
cs:4593:   // Figures first (round 7): nothing below may keep or re-take an actor renderer.
           using (Phase(CommitPhase.Figures)) PurgeFigureRenderers();
```

A figure/actor renderer **cannot** be in the table when the commit ends — that is what phase 1
guarantees, by construction, not by threshold. So a figure moving, being highlighted, being
activated or deactivated, or changing shader **provably cannot change the commit's output**, and
folding those events into the skip signature triggers a 95 ms rebuild for nothing.

The design:

1. Add a **`Figure` bit** to `RendererFact`, classified in `ClassifySlice` alongside the
   existing eight. `IsFigureOrActorRenderer` (`cs:6314`) is the existing round-7 predicate;
   `f.Mod` is already classified the same way.
2. For a renderer with `Figure` set, fold **identity plus the Figure bit only** —
   `FoldSig(FoldSig(FnvOffset, instanceID), BitFigure)`. Identity is still folded, so a figure
   **appearing or dying still moves the hash**. Its movement, activation, highlight and shader
   changes do not.

**What it can miss — the failure mode, named.** A renderer that is a figure *now* and was not a
figure *then* (or the reverse) flips the `Figure` bit, which is folded, so the crossing itself is
caught. The genuine hole is different: **`IsFigureOrActorRenderer` is an ancestry predicate**
(`GetComponent<ActorBehaviour>` / `<CInteractableActor>` / `<Animator>` up the chain). If the
game **reparents** a renderer under an actor without touching the renderer itself, and the
ancestry memo is cold at the right moment, the Figure verdict can be wrong for one cycle — and a
wrongly-Figure renderer's real changes would be silently dropped from the signature. The
backstops are the staleness ceiling (30 cycles) and the drift probe. **State this to the user as
the risk, and do not ship it without the falsifier below.**

**Cost.** `IsFigureOrActorRenderer` per renderer in the classify pass. That pass is already off
the commit frame (1.5 ms/frame budget, spread over ~46 frames), so the cost lands where it is
invisible — *but* the predicate leans on `FigureAncestryMemo`, whose window is one synchronous
pass (`BeginFigureMemo`/`EndFigureMemo`, `cs:4564-4566`). Classify spans frames. **The
implementer must open a memo window per classify slice, exactly as `StepPrepare` already does
(`Prepare.cs:319`/`:366`), or the climb is cold per slice and the classify budget blows.**

### 2.5 Can `SignatureCulpritCensus` falsify it? Yes — and this is the shipping condition

Ship the narrowing with **both** signatures computed every cycle. The fold is two multiplies; a
second accumulator pair over 5,797 renderers costs on the order of 0.02 ms, inside a stage that
is already budgeted at 1.5 ms/frame. Then:

- Every cycle where **full moved but narrowed did not** is a cycle the narrowing skipped and the
  old code would have committed. Count them, and have `LogSignatureCulprits` name the renderers
  responsible with their bits — that is exactly what `WallSegmentFadeCulprits.Banked(name, bits)`
  (`Prepare.cs:868`) already banks.
- Every cycle where **narrowed moved but full did not** is impossible and is an instrument bug;
  count it separately and make it loud.

**Ship the narrowing behind a dial, with the shadow full signature always on.** The first
hardware run then proves, with names, exactly what the narrowing dropped. That is a permanent,
self-accusing falsifier rather than a reassurance.

### 2.6 Expected reduction — derived, with the honest bound

From §2.3: 5 of 17 sampled refusals were pure `activeInHierarchy` flips of 1 or 3 renderers.
**If** those renderers are figures — which the decode cannot tell you, because the delta is
identity-independent — the narrowing removes ≈ **29 %** of scene-signature refusals. Since scene
refusals were 28 of 33 commits, that is ≈ 24 % of all commits.

The remaining 11 were renderer-set changes. In a steady combat scene the things that appear and
die are attack VFX, damage numbers, highlight decals and effect emitters — all figure-adjacent,
so the narrowing *may* cover most of those too. **That is a hypothesis, not a measurement, and
it should be labelled as one to the user.** The 278 census answers it on the first run.

**And say this plainly to the user: A is statistical. It makes hitches rarer. Even at a 90 %
reduction the remaining commits still cost 95 ms each, and he has said rarer is not what he
asked for. A is worth doing because it is nearly free and because it shrinks the number of
times B's slicing has to run — not because it solves his complaint.**

---

## 3. Option B — double-buffer the table and slice the build

### 3.1 The correction that matters most: `_segments` is not the only thing that swaps

`Tick()` (`WallSegmentFade.cs:1805`) is the per-frame applier and the only per-frame reader of
committed state. It reads `_segments` — and also `_roomBounds`, `_builtRoomCount`,
`_splitAnchors`, `_allSamples`, `_sampleYMin`/`_sampleYMax`, `_roomsAnchored`. `ApplyMounted`
reads `_mountedUnion` and `_unionOwners`, which `BuildMountedUnionOverlaps`
(`Mounted.cs:2299`) rebuilds inside the commit and which the phase inventory confirms are
*"read every frame by the appliers"*.

So the committed state set is at least:

```
_segments, _roomBounds, _roomLabels, _roomFloorY, _roomFloorAnchored, _roomsAnchored,
_builtRoomCount, _allSamples, _sampleYMin, _sampleYMax, _splitAnchors,
_waterRects, _archRects, _mountedUnion, _unionOwners, _boardVolume
```

**Double-buffering `_segments` alone is not sufficient and would ship a torn table.** This is
the single most important thing for the implementer to know before starting, and it is what
turns B from a two-day job into a two-week one if it is discovered halfway.

**The enabling refactor: extract the committed state into one `CommittedTable` class with a
single `_live` field, swapped by one reference assignment.** Mechanical — move ~16 fields into a
class, rename the applier reads to `_live.X` and the builder writes to `_next.X`. The existing
gates (`check-mirrors.sh`, the wire tests) protect the move. **Do this as its own commit, with
`_next` never assigned, before any behaviour changes.** It is a pure refactor that can be
verified to be a no-op.

### 3.2 The per-phase table

19 of the 24 phases already write into a table they are handed and would work unchanged against
a fresh one. **Five phases perform Unity writes**, and — this is the good news — every one of
them is the *same shape*: a **restore**, undoing a write this subsystem previously made to a
renderer that is leaving a segment.

| # | phase | fresh-table-safe? | note |
|---|---|---|---|
| 1 | Figures | **NO — restores** | `PurgeFigureRenderers` → `RestoreProp` |
| 2 | TileAnchors | yes | rebuilds `_roomFloorY` |
| 3 | RoomRegistry | yes | rebuilds `_roomBounds`, `_roomLabels` |
| 4 | DeadSegments | yes | trivially — a fresh table has no dead keys |
| 5 | WallCache | **NO — restores** | `FinishRefresh` → `SetPropertyBlock(null)` on leavers when `seg.HasBlock` (`cs:7947`) |
| 6 | Doors | yes | |
| 7 | GateSeed | yes | |
| 8 | Adopt | yes | |
| 9 | Water | yes | rebuilds `_waterRects` |
| 10 | Samples | yes | rebuilds `_allSamples` |
| 11 | Rooms | yes | writes `seg.RoomIndex`, `seg.BorderRooms` |
| 12 | Ground | **NO — restores** | `SetPropertyBlock(null)` at `cs:5288` |
| 13 | Engulf | yes | adds/removes segments, writes `_splitAnchors` |
| 14 | Ground2 | **NO — restores** | same site as 12 |
| 15 | Stacked | **NO — restores** | `CollectStackedShellPieces` → `RestoreProp` on leavers (`Stacked.cs:783`). *(The `r.enabled` writes at `Stacked.cs:385`/`618`/`644`/`1212-1216` are in `ApplyStacked`, `FastReclaimSweep` and `ApplyCornerPieces` — appliers, not this phase. Checked, because a per-phase table that miscites is a lying instrument.)* |
| 16 | PropUnits | **NO — restores** | `SetPropertyBlock(null)` (`PropUnit.cs:1320`, `1447`), `Object.Destroy` via `RestorePropSwap` |
| 17 | Siblings | yes | |
| 18 | **Mounted** | **NO — the hard one** | see §3.3 |
| 19 | WireKeys | yes | reads `anchor.transform.position`, `anchor.name` |
| 20 | GateLift | yes | |
| 21 | GateBounds | yes | |
| 22 | StandCensus | yes | diagnostic |
| 23 | UnitCensus | yes | diagnostic |
| 24 | BoardVolume | yes | |

**The uniform rework.** Because every one of these writes is a restore, and because *until the
swap the OLD segment still owns that renderer and is still correctly animating it*, all of them
can be **deferred**: replace the direct write with an append to a `_pendingRestores` list,
drained immediately after the reference swap. One shape, six sites, and it is verifiable —
after the change, the builder contains no Unity writes at all, which is a greppable invariant
in the spirit of `PREPARE MAY WRITE NOTHING BUT MEMOS`.

### 3.3 Mounted is the one that will fight

Three things in phase 18 are not deferrable by that rule:

1. **`ClassifyProp` must read `r.sharedMaterial` exactly once, at first adoption, and never
   again** (`Mounted.cs:1982-1986`). Re-reading a material we are *currently ramping* snapshots
   our own ramp as the "authored" value and permanently corrupts the restore. Whether a prop is
   already owned is state only the *live* table knows — so the builder must consult the live
   table for ownership while writing to the shadow one. That is legal (read-only on live) but it
   must be written down, because it is the one place where the two tables are coupled.
2. **`IsMobileProp` is a cross-commit differential** with a side-effecting ledger
   (`_mountedAnchorLedger`, `_mountedMobile`, `Mounted.cs:1187`, `:1192`). Its baseline is "where
   this prop was last rescan, in the owner's frame". Under a sliced build the two reads are no
   longer one frame apart — they are up to 0.7 s apart. **Either the ledger write must move to
   the swap, or the drift threshold must be re-derived against the new interval.** This is a
   real behaviour risk and it should be called out to the user.
3. **`c.enabled` and `ParticleSystem.particleCount` must be read live** (`RendererFact`'s own
   doc forbids caching `enabled`, `cs:1471-1475`). Under slicing, a candidate examined on frame
   N is judged on `enabled` as of frame N and swapped in at frame N+60. **Take these reads in a
   small live pass at the swap, for the props the decision actually touched, not during the
   build.**

### 3.4 The wall that visibly snaps — three cases, in order of severity

**Case 1 (the bad one): a segment in the OLD table and not the NEW one, with `Fade > 0`.**
Today the removal sites call the restore path, so the wall returns to solid on the same frame.
With a fresh table the segment simply is not there: nothing animates it, nothing restores it,
and **the renderer keeps the MaterialPropertyBlock it last had, frozen mid-fade, for the rest of
the session.** A permanently half-transparent wall.

The cause is structural and worth stating in one sentence: **the segment table is not only a
derived view of the scene, it is the undo log for every write this subsystem has made to it.**
`HasBlock`, `FoliageState`, `SiblingState`, `MountedState`, `PrevRenderers`, `PrevFoliage`,
`PrevSiblings`, `_mountedTouched` are all records of mutations already applied. Throwing the
table away throws the undo log away.

**Remedy:** the swap must compute the set difference `old \ new` and run the full restore path
for every dropped segment, before or at the swap. That set is exactly what the equality gate
(§3.7) walks anyway.

**Case 2: a segment in BOTH tables whose animation state is not carried.** `Fade` resets to 0
while the renderer still carries an MPB with a cutoff — the wall snaps back to solid, then fades
again. Fixed by carrying forward `Fade`, `State`, `PendingRaw`, `PendingSince`, `Smooth`,
`SmoothInit`, `HasBlock`, `FoliageState`, `SiblingState`, `MountedState`, `RunDriven`.

**Case 3 (the subtle one): a segment in both tables whose RENDERER LIST changed.** Old owns
`{A,B}`, new owns `{A,C}`. Carrying `Fade` fixes A. But B is now unowned and still carries the
MPB → B freezes mid-fade (case 1 at renderer granularity). And C has no MPB while the carried
`HasBlock = true` tells the applier one is already there → C never fades.

**So the carry-forward is not a segment-level operation, it is a renderer-level one.** The swap
must diff the *union of all owned renderers* across the two tables, restore the leavers and mark
the joiners dirty. That is precisely what `PrevRenderers` + `FinishRefresh` do today *within* a
segment; under B it is promoted to a table-level operation. **This is the core of the
implementation and it should be written first.**

### 3.5 `ComputeWireKeys` is the wrong key — use the dictionary key

The brief asked whether `ComputeWireKeys` supplies the carry-forward identity. **No, and it
should not be used for this.** `WireKey` is

```
Net.cs:107:   seg.WireKey = Fnv1a32Key($"{anchor.name}|{roomLabel}|{qx}|{qz}");
```

— a 32-bit FNV over an anchor name, a room label and a position quantised to 0.5 world units. It
is lossy and collision-prone by construction, it is `0` for a dead anchor, and it is designed to
survive *across machines*, which is a much weaker requirement than uniqueness within one process.

The right key is already there: **`_segments` is keyed by `Component` (the anchor), and object
identity is exact, unique, free and already the thing both tables agree on.** Carry-forward is
`if (_live.Segments.TryGetValue(anchor, out var old)) CarryAnimationState(old, fresh);`. Use
`WireKey` for the wire, and nothing else.

### 3.6 Slicing budget, and what happens mid-build

**Granularity.** Because nothing reads the shadow table, a phase can be suspended *mid-item* —
the builder does not need phase granularity, it needs a cursor, exactly as `ClassifySlice` and
`StepPrepare` already have. The one care point is `NeutralizeEngulfingSegments`, which adds
segments while iterating; that needs a stable cursor over a snapshotted key list, the same
problem Classify already solved.

**Budget.** At 1.5 ms/frame (Prepare's budget), 94.8 ms of commit is **63 frames ≈ 0.70 s** at
90 Hz. But Prepare *also* runs at 1.5 ms/frame for 22–33 frames, so naive stacking gives
3 ms/frame of mod work. Two options: interleave the stages (Prepare, then Build, never both),
or raise the shared budget to 2.0 ms/frame → **48 frames ≈ 0.53 s**. Interleaving is preferable
because it keeps the existing per-frame number unchanged.

**Per-frame cost B leaves: 1.5 ms, by choice.** Against an 11.11 ms budget and a main thread the
`FINDINGS.md` measurements show is **74 % blocked waiting on the GPU**, that is not a dropped
frame and not a perceptible hitch. This is the whole point of B and the reason it beats a worker
thread on the metric the user actually named.

**Scene changes mid-build.** Today this cannot happen; the commit is atomic. Under B there are
two policies:

- *(a) throw the half-built table away and restart.* Cost: the frames already spent (up to
  ~95 ms of work) are wasted — but **zero stall**, because they were spread. The user sees
  nothing; the table is just older.
- *(b) finish and swap anyway.* The result is at most one rescan interval stale.

**(b) is strictly better and the log proves the tolerance:** the SKIP path already lets the table
stand up to **7.8 s** without a rebuild (`DECISION LATENCY` clause), against a 2.0 s cadence. A
0.5–0.7 s build window is well inside a staleness budget that already ships.

**Restart only on a room REVEAL**, which invalidates the floor planes the build measured against
— the same gate `BeginPrepareStage` already uses (`m_RoomRenderers.Count != _builtRoomCount`,
`Prepare.cs:266`). Reuse it verbatim; do not invent a second one.

### 3.7 Memory: a real number

Per `Segment`, derived from the field list (`cs:860-1100`, plus `Mounted.cs:122`) on 64-bit Mono:

| part | bytes |
|---|---|
| object header + ~5 reference fields + 2 `Bounds` (24 B each) + ~20 scalars | ≈ 240 |
| 8–12 `List<T>` objects @ 32 B (empty lists share `s_emptyArray`, so cost nothing more) | ≈ 320 |
| `Renderers` + `PrevRenderers` backing arrays @ ~24 refs (734 renderers ÷ 31 walls) | ≈ 430 |
| `Foliage`/`Siblings`/`Mounted`/`BorderRooms`/`LastBlockedCells` backing, typically small | ≈ 250 |
| **per segment** | **≈ 1.25 KB** |

At the observed 99–127 segments: **≈ 125–160 KB per table.** A second table is therefore
**≈ 0.16 MB**.

Add the shadow-side auxiliaries the builder needs: `_claimedRenderers` (`HashSet<MeshRenderer>`
over ~1,888 entries ≈ 45 KB), `_propUnitAnchors`, `_mountedUnion` + `_unionOwners`, the room
tables, `_allSamples` (96 entries). **Total additional resident memory: ≈ 0.3 – 0.5 MB.**

For scale: this game's streaming mipmap budget is 900 MB (see the "higher preset can be worse"
finding). **0.5 MB is not a consideration.** Say so to the user rather than hedging.

### 3.8 The gate — and yes, B makes it dramatically cheaper

The brief is right that this is the safety net the whole change needs, and right that B makes it
cheap. With both tables in memory at the swap, the equality check is a walk over two structures
rather than a serialisation captured across time:

For each anchor in `old ∪ new`, compare `Renderers` / `Foliage` / `Siblings` / `Mounted` as sets,
plus `Bounds` (bitwise), `HasBounds`, `RoomIndex`, `Engulfing`, `DoorRoot`, `RunOwner`,
`FromSplitRun`, `HeldCutoff`, `WireKey`. That is **O(total owned renderers) ≈ 2,000–3,000
elements** at a few ns each: **≈ 0.05 – 0.2 ms**, small enough to run permanently behind a dial.

**And there is a stronger form, which is the shipping plan I recommend:**

> **Build 1 — shadow only.** Ship the sliced builder producing a shadow table which is
> **discarded**, with the existing atomic commit still authoritative, and the equality gate
> comparing them. Costs 1.5 ms/frame extra. **Changes nothing on screen.** The log says whether
> the two tables ever differ, on his hardware, in his scenario.
>
> **Build 2 — flip the swap on**, once build 1 has reported agreement across a session.

That is a null-input validation in the exact sense the discipline demands: a new instrument
whose first output is a hypothesis, checked against a known-good control (the atomic commit)
before anything depends on it. It also directly answers "which option can break the behaviour he
just praised" — B can, and this is how we find out before he does.

---

## 4. C and D — for the record, one paragraph each

**C — worker thread / job.** Not taken. Recording the reason because it is not "too hard": it is
that **C cannot beat B on the metric the user named, for any value of the split.** Whatever
fraction of the commit is Unity-bound (`Renderer.bounds`, `GetComponentsInChildren`,
`Object.name`, `GetSharedMaterials`, and the `SetPropertyBlock`/`Destroy` writes) is a **floor**
that stays on the main thread — the snapshot must still be taken there. B has no such floor: it
spreads *everything*, Unity calls included, at a per-frame budget you choose. C wins only on
*latency* (it would finish in one Unity-bound pass rather than 0.5 s), and the log shows the
system already tolerates 7.8 s of staleness. Add that Mono's GC stops all threads, that the
commit performs real Unity writes, and that ~6,000 lines across four files would need a
no-Unity-dereference rule enforced by hand, and C is a large rewrite that lands *behind* B.
(Confirmed for the record: the Job System types are present in
`ressources/Managed/UnityEngine.CoreModule.dll` — `IJobParallelFor`, `IJobFor`, `NativeArray`,
`JobHandle`, and notably `IJobParallelForTransform`/`TransformAccessArray`, the one sanctioned
off-thread transform path. But `Unity.Collections.dll` is **not** shipped, and Burst AOT-compiles
at player-build time, so a BepInEx-loaded assembly gets no Burst code and no SIMD. The
integrator's assumption holds.)

**D — incremental rebuild.** Not taken. The phases have hard ordering dependencies that are each
a user ruling or a defect fix ("LAST on purpose", "BEFORE the mounted pass", "Second ground pass
ON PURPOSE"), and a second code path that can diverge from the full one is a cache-invalidation
bug farm sitting exactly in the fade correctness the user has just called perfect. It would also
need the churn data that only the 278 census can supply. If it is ever revisited, note that B is
its prerequisite anyway: incremental rebuild into a live table has the same torn-table problem
that double-buffering solves.

---

## 5. Option E — the work that should not run at all

All three phase inventories, produced independently, converged on this. It is not in the
brief's four options and it is the cheapest thing on the list.

| site | what runs per commit | why it is waste |
|---|---|---|
| `WallStandingProp.cs:399-496` (`StandsOnFloor`) | builds a `shape` string + one of five `why` sentences on **every call**, once per child renderer of every cache wall, twice on the foliage branch. On net472 each `$"…"` is `string.Format(string, object[])` — an array plus a box per float. | The memo above it caches the *measurement*, not the verdict, so the sentence is rebuilt every time. ~6–12 heap objects per renderer per commit, nearly all discarded. |
| `Standing.cs:926-927` (`NoteStandingSubject`) | `r.name` read **twice unconditionally** (two `ContainsKey` operands, no local), a third time on a new name | feeds a roll capped at 64+16 entries. On the 734-renderer scenario that is ~750–1,500 native string allocations per commit. |
| `Mounted.cs:1703` | `c.name` per surviving sweep candidate, **built as an argument** so it allocates even when `_archRects` is empty | `RendererFact.Name` (`cs:1488`) already holds this exact string and is in hand at `:1584`. `IsArchProtected` only needs "does the name contain 'Door'" — one precomputed bool. |
| `Mounted.cs:1862–1979` | eager `why`-string construction at 7 of 8 `NoteMountedReject` call sites | `NoteMountedReject` discards them at the 24-entry cap or the 2.5 wu gate. The `StructuralSkipArmed` hoist (`:1118`) was applied to only two of the sites that need it. |
| `Mounted.cs:2772-2833` | `IsActuallyDrawing` (5 native calls incl. `GetComponent<ParticleSystem>`) and `ClassifyLeftover` (3× `r.bounds`, 2× `r.name`, an unmemoised `GetComponentInParent<ProceduralWall>`, a 16-sample ray loop) for **every** reject near a faded wall | the 40-entry cap is checked at `:2816`, *after* both have already run. |
| `Mounted.cs:2611-2620` | `seg.Anchor.name` + a linear string scan, per (candidate × saturated segment) | before its own `< 8` cap check. |
| `PropUnit.cs:826`, `853-854` | `_propUnitByRoot`/`_propUnitByStem` record **successes only**; a span-refused group re-walks the parent subtree and re-reads `K_p` names for **every** renderer under it | O(K_p²) name allocations per wall in the flat-parented masonry case. The "rare by construction" comment covers the root case, not the refusal-by-span case. |
| `PropUnit.cs:1513`, `Standing.cs:901` | `m.shader.name.Contains("WallFade")` — raw, uncached | The identical expression on the identical `Shader` object is already cached by `FadeNameOf`/`_shaderFadeName` (`cs:1548`). PERF S4 fixed this at `CollectWallFadeInfo` and missed these two sites. |
| `WallPropUnit.cs:147,188,212,242` | `out string rule` **always** allocated | discarded unless the census is under cap. |

Two further pure-compute wins in the same sweep: `HorizontalGap`'s `Mathf.Sqrt`
(`Mounted.cs:1257`) is paid on every (candidate × segment) pair and **every consumer is a
comparison** — a squared form removes it twice over; and there are **three** separate
O(candidates × segments) walks per commit (`Mounted.cs:1781`, `:1140`, `:2383`) with **no
broad-phase index**, against reach constants of 0.9–2.5 wu on a board of many metres. An XZ grid
over segment AABBs is the standard remedy.

**Why E first:** it needs no hardware to design, it does not touch a single decision predicate
(so it cannot change the look the user just praised), it is measurable by the per-phase
instrument that already ships, and every millisecond it removes is a millisecond B does not have
to slice. **What it is not: a fix.** Even a 40 % cut leaves ~57 ms atomic, which is five dropped
frames. E shrinks the problem; B removes it.

---

## 6. The instrument for the U/C/D split — handed over, one build

**This needs one instrumented build on his hardware.** Saying so is the honest answer, and the
brief allows for it.

**Design — counters always, timers behind the existing `[Perf]` dial.**

1. **Funnel, don't wrap.** Route the Unity calls in the three heavy phases through a small set of
   static accessors (`U.Bounds(r)`, `U.Name(o)`, `U.Children<T>(t, list)`, `U.Parent(t)`,
   `U.Enabled(r)`, `U.SharedMaterials(r, list)`). Each increments an `int` counter, and *also
   counts result elements* for the `GetComponentsInChildren` family — a subtree walk's cost is
   proportional to what it returns, so a call count alone would be a per-call-cost assumption of
   exactly the kind this project has been burned by.
2. **Counters cost ~1 ns.** Even at 200 k Unity calls per commit that is 0.2 ms on 94.8 ms
   (0.2 %). Always on; state the overhead in the log line.
3. **Timers only at the choke points** — `CollectWallFadeInfo`, `MeasureStandingUnit`,
   `PropUnitRootOf`'s three node facts, the Mounted sweep body — gated on `PerfMonitor.StepsActive`
   (an existing dial; **no new config key**, which keeps `rebase-defaults.py`'s 491 and
   `check-remote-defaults.py`'s 76 untouched and stays out of the other lanes' files).
4. **A `D` counter**: bytes/objects allocated by diagnostic string building per phase, via a
   `GC.GetAllocatedBytesForCurrentThread()` delta around the census calls. That sizes Option E
   directly.
5. **Self-calibration in-build**: once per BUDGET window, micro-benchmark `.bounds`, `.name`,
   `GetComponentsInChildren`, `.parent`, `.enabled` over ~200 renderers already in hand. Counts ×
   measured per-call cost gives a predicted Unity-bound total. **The model is self-falsifying:
   if the predicted Unity millis exceed the measured phase millis, the model is wrong and the log
   says so.** That is route 2 validated against route 1, as the brief asked.
6. **Null control, mandatory**: an empty scope and a counter-only loop, both measured in-build and
   printed. Do not set an acceptance bar before that floor is on the page.

**I did not build this.** With C and D off the table it is a sizing input for B, not a decision
input, and the redirect said not to hold the design for it. It should be built alongside B's
build 1, where its output goes straight into choosing the slicing budget.

---

## 7. Recommendation

**Do E, then A, then B — in that order, and ship B in two builds.**

1. **E first (no hardware, no risk to the look).** It touches no decision predicate. It shrinks
   the 94.8 ms directly, and the per-phase instrument already in the build measures whether it
   worked. Best guess at the size: substantial but unquantified — that is exactly what the §6
   instrument's `D` counter is for, and it can ride the same build.
2. **A second (nearly free, and it is not the answer).** The `Figure` bit narrowing, shipped
   behind a dial with the full signature computed in shadow and `SignatureCulpritCensus` as the
   named falsifier. Expected ≈ 24 % fewer commits, on a sample of 17. **Tell the user plainly
   that this makes hitches rarer, which is the thing he said was not enough.**
3. **B is the one that answers his actual complaint.** It takes the per-frame cost to a number
   *you choose* — 1.5 ms — against an 11.11 ms budget on a main thread that is 74 % idle. That is
   below the perception floor, which is what "gar keine spürbaren Ruckler" means.
   - **Build 1:** the `CommittedTable` extraction (pure refactor, verifiably a no-op), then the
     sliced shadow builder + equality gate, shadow table **discarded**. Nothing changes on
     screen. The log reports whether the two tables ever disagree.
   - **Build 2:** flip the swap on, with the renderer-level carry-forward diff (§3.4) as the
     first thing written and the deferred-restore list (§3.2) as the second.

**What to tell the user about risk.** B is the only option here that can break the fade
behaviour he has just called perfect, and §3.4 names the three ways: a dropped segment freezing
a wall mid-fade, a carried segment snapping back to solid, and a renderer changing owners
between tables. He is entitled to know that, and to know that build 1 exists specifically so
those are found by the log and not by him.

**Where the ~2 ms question lands.** The brief asked whether the honest answer might be "B alone
gets it under 2 ms and C would take it to 0.2 ms". It is better than that: **B's per-frame cost
is not an outcome, it is a dial.** 1.5 ms is the value Prepare already uses and already proves is
invisible. C's floor, by contrast, is the Unity-bound fraction and cannot be chosen at all. There
is no version of this where a 6,000-line rewrite is the right trade.
