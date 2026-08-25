# PERF B, build 2 — what is landed, what is falsified, and what is left

Written by the PERF-B1 lane against ModBuild 280 (`cf24af90`). Everything below is checked
against the source as it stands after this lane's three commits, not against the design note.

**Read `.planning/perf/WALL-COMMIT-ARCHITECTURE.md` §3 first, then this. §3 is a strong design
and most of it holds. Three of its claims do not, and they are the three that would have cost
build 2 the most time.**

---

## 1. What landed

| commit | what | verified how |
|---|---|---|
| step 1 | `CommittedTable` — 27 fields of committed state in one object behind one field `_live`, never reassigned | **proven no-op**: every renamed identifier normalised back and diffed against `cf24af90`; 0 unexpected differences over 14 files and 436 references; 5 of 14 files normalise back byte-identical |
| step 2 | `Core/WallCommitDiff.cs` — the equality gate and the renderer-level carry-forward PLAN, Unity-free | 118 new wire-test assertions: null control ×2, one known positive per snap case, one per ownership class, class crossing, structural/ULP bounds, gate-lift, order-freedom, elision both sides of the cap, cost |
| step 3 | the CHURN gate wired into the driver (`[WallFade] CommitTableGate`, ON) and `[WallFade] SliceBudgetMillis` (1.5) | build + all gates; behaviour unchanged by construction (read-only gate; dial at its shipped value) |

Gate numbers: build 0/4 · wire-tests 147644 → **147763** · mirrors 19 · frame-order 7 · bundle
68,577,168 · patch-inventory 78/130 · wire-coverage 172/85/257 · rebase-defaults 491 · refasm 16
· remote-defaults 76.

---

## 2. Three things §3 gets wrong

### 2.1 §3.1's committed-state list is missing eleven fields, and two of them decide pixels

§3.1 lists sixteen. Twenty-seven are on the decision path. The eleven it misses, all found by
tracing the per-frame READERS rather than by reading the list:

| field | now | read every frame by | why it matters |
|---|---|---|---|
| `_roomSampleStart` | `RoomSampleStart` | `RoomBlockedFraction`, `PieceBlockedSamples`, `InsideRoomFraction` | index INTO `AllSamples`, which §3.1 does buffer |
| `_roomSampleCount` | `RoomSampleCount` | `BlockedFraction`, `RoomDecisionValid`, `EvaluateSplitRuns` | **the coverage DENOMINATOR** |
| `_boardVolumeValid` | `BoardVolumeValid` | `UpdateInsideBoard` | without it `BoardVolume` is a stale AABB reading as authoritative |
| `_boardCrestWU` | `BoardCrestWU` | `UpdateInsideBoard`, `UpdateWalkInside` | both Schmitt bars are FRACTIONS of it; the walk-in real-metre term is `/rigScale` of it |
| `_boardFloorY` | `BoardFloorY` | same pair | |
| `_boardVolumeRooms` | `BoardVolumeRooms` | `UpdateWalkInside` refusal line | a refusal quoting a retired table's count is a lying diagnostic |
| `_cornerPieces` | `CornerPieces` | `ApplyCornerPieces` | **also APPENDED by an applier** (`FastReclaimSweep`) |
| `_propUnitAnchors` | `PropUnitAnchors` | `HighestCandidateRootAbove`, `BeginPrepareStage` | **also rebuilt from the TICK prepare stage** |
| `_propUnitRootMemo` | `PropUnitRootMemo` | `StaggerRootOf`, every prop-frame | |
| `_prepBoardProbePos` | `PrepBoardProbePos` | `BoardStillWhereTheFloorPlanesSayItIs` | gate 3 of the commit-SKIP decision |
| `_prepBoardProbeValid` | `PrepBoardProbeValid` | same | |

**The sample indices are the sharpest one and it was not hypothetical.** After the first pass of
this refactor the source literally read

```csharp
int start = _roomSampleStart[room];                                   // driver field
int end = Mathf.Min(start + totalOut,
                    Mathf.Min(_live.AllSamples.Count, _sampleVisible.Length));   // table
```

— an old start index bounded by a new sample list, in the function that decides which walls
fade. That is ModBuild 258's "the denominator was a rectangle drawn around a hexagonal room",
re-armed by the refactor meant to make the table safe. All eleven are now in `CommittedTable`.

### 2.2 §3.8's gate walks four ownership classes; a segment has seven

§3.8 proposes comparing `Renderers` / `Foliage` / `Siblings` / `Mounted`. A `Segment` also owns
`Body`, `Stacked` and `UnitDressing` — each with its own `Prev*` list and its own `*State` undo
flag, i.e. three more independent undo logs with the same three failure modes. A gate blind to
three of seven agrees with a torn table, and agreement from a blind instrument is precisely what
would have shipped build 2. `WallCommitDiff` walks all seven and the wire suite plants a
difference in each of them in turn.

### 2.3 There is a FOURTH snap case, and it is the one with no symptom until a prop changes wall

§3.4 names three. **Case 4: a renderer owned in BOTH tables by DIFFERENT segments.** It is
invisible to a union-of-owned-renderers diff (the renderer is owned throughout, so it is neither
a leaver nor a joiner) and invisible to a per-segment carry (the block state that says whether
it carries an MPB lives on the SEGMENT, and it just changed segment). A renderer that moves from
a segment with `HasBlock = true` to one carrying a carried `HasBlock = false` is a renderer
nobody will ever restore. `EnforcePropUnitCohesion` re-adjudicates a statue between two walls
every rescan — that is the `skelet.jpg` defect class, photographed and reported four times.
`Diff.Moved` is the only thing that sees it.

§3.5 is **right** and the brief's correction of itself is right: the anchor `Component` is the
carry-forward key, `WireKey` is for the wire. `Segment.GateLift` is compared by the *partner's
anchor id*, never by reference — two tables never share `Segment` instances.

---

## 3. Why build 1 is a churn gate and not a shadow build

The design note's build-1 plan (§3.8) is to run the sliced builder producing a shadow table and
compare fresh-against-incremental. **That premise does not survive contact with the source.**

- The 24 phases are **not pure functions of the scene**. They mutate at least fifteen
  driver-level ledgers outside the table: `_claimedRenderers`, `_siblingOwned`,
  `_mountedTouched`, `_mountedAnchorLedger`, `_mountedMobile`, `_attachmentOwned`,
  `_mountedOwned`, `_mountedUnitHome`, `_propUnitOwnerLast`, the standing and node memos, the
  census accumulators. A second run in the same cycle sees all of them already rewritten.
- `CollectWallMountedProps` **drains and clears** `_mountedTouched`, `_mountedAnchorLedger` and
  `_mountedMobile` at the top of its pass. Run twice per cycle, the undo log is drained twice.
- Six phases perform Unity **writes**. A shadow run repeats every one.
- `IsMobileProp` is a cross-commit differential whose baseline a shadow run consumes.

A safe shadow therefore needs a write-suppression flag threaded through every phase plus a
save/restore of ~15 collections — **and it needs the slicing to exist already**, or running the
commit twice doubles the 95 ms hitch instead of removing it. A build advertised as "changes
nothing on screen" that carries fifteen chances to corrupt the live ledger is not the safe half
of a two-build plan.

So build 1 measures **churn**: the table before the commit against the table after it. That is
read-only, cannot change a pixel, and is exactly the population the four cases are defined over.
**It is not the same question** as fresh-vs-incremental and the log line says so in its own
words. What it buys build 2: the four case counts from his hardware and his scenarios, and
`WallCommitDiff` exercised on real tables at real sizes on the target CPU.

---

## 4. What build 2 has to do, in order

1. **Read the ModBuild 281 log's `WALL TABLE GATE` line first.** The number that decides the
   shape of the work is *worst-case mid-fade drops per commit*. Zero across a long session ⇒
   case 1 is theoretical and the carry-forward is cheap insurance. Non-zero ⇒ that is the count
   of permanently half-transparent walls B would ship without one, and it is the blocker.
2. **Defer the Unity writes.** §3.2 is right that the six restore sites are all the same shape;
   replace each with an append to `_pendingRestores`, drained immediately after the swap. The
   greppable invariant afterwards is that the builder contains no Unity writes at all.
3. **Execute the carry-forward plan `WallCommitDiff.Compare` already produces**: restore
   `Diff.Leaving`, mark `Diff.Joining` dirty, re-home `Diff.Moved`, carry animation state for
   `Diff.Carried`. The decision is already written and already pinned in CI; only the execution
   is left, and it belongs in the driver.
4. **Slice the builder with a cursor**, budget `WallFadeTuning.SliceBudget` (already a dial,
   already 1.5). §3.6's interleaving advice stands: Prepare, then Build, never both.
5. **Then** flip the swap on.

### The five things that will bite

- **`_mountedTouched` must be SHARED, never snapshotted.** It is the undo log at prop
  granularity, it is written by `ApplyBody` / `ApplyStacked` / `ApplyCornerPieces` /
  `ApplyUnitDressing` / `FastReclaimSweep` as well as by the commit, and eight commit phases
  read it.
- **Three ledgers hold `Segment` REFERENCES and are not in the table**: `_mountedAnchorLedger`
  (`PropAnchor.Owner`), `_attachmentOwned` (`OwnerRef.Seg`), `_mountedUnitHome`. After a swap
  they name segments in the discarded table.
- **Two hold `Segment` references ACROSS FRAMES, with their own cursors**: `_driftRing`
  (`Prepare.cs`) and `_pathAuditWalls`. Swap under them and the drift probe measures a retired
  buffer while the wall-path audit reports on segments nobody owns. Both must be dropped or
  re-seated AT the swap.
- **`_sampleVisible` is TICK-owned but index-aligned to `AllSamples`, and no invalidation path
  for it exists.** On the swap frame it is stale by construction. Either force an
  `UpdateSampleVisibility` pass before the next `BlockedFraction`, or prove it cannot matter.
  This one is an open question, not a finding.
- **`CornerPieces` and `PropUnitAnchors` are committed state that a TICK path also writes.** The
  swap has to decide whether an applier's mid-cycle addition follows the table it was made
  against (it should) or is dropped (it must not be).

Plus §3.3's three, which still stand: `ClassifyProp` must read the authored material exactly
once, `IsMobileProp`'s baseline becomes ~0.7 s instead of one frame, and `c.enabled` /
`ParticleSystem.particleCount` must be read live at the swap.

### Committed state NOT moved into `CommittedTable`, on purpose

About thirty census/diagnostic fields are commit-written and Tick-read and therefore technically
cross the boundary — the mounted census family (`Mounted.cs:393-572`), `_censusFadeRenderers` /
`_censusClaimed` / `_censusAdopted` / `_censusWallsWithoutFade` / `_unfadeableWallShaders`,
`_roomRendererCounts`, `_roomMapKeys`, `_tilesByMap`, `_tilesResolved`, `_heartbeatLogged`,
`_propUnitTouched`, `_attachmentOwned`, the step-edge and split-run-leftover families. **None
can change a pixel**, so they were left out to keep the no-op diff reviewable. They are listed
here so build 2 does not have to re-derive them; a swap that leaves them behind produces a
census that describes the previous table, which is a lying diagnostic and not a torn table.

Explicitly SCRATCH, checked and ruled out: `_doorRoots` (not read per frame — only
`FindDoorwayRoot` from `AdoptShaderMatchedWalls`), `_claimedRenderers`, `_siblingOwned`,
`_mountedOwned`, `_roomMapByRenderer`, `_roomMapLabelByRenderer`, `_floorYByRenderer`,
`_roomTileCount`, `_roomTileGrid`, `_sampleGridCells`, `_boardVolumeWalls`, `_deadKeys`,
and the standing/node memo families (cleared from BOTH the commit and the tick prepare stage).

---

## 5. Numbers, and where they came from

- Gate cost: **0.136 ms** over 127 segments / 2,540 owned renderers (top of the logged range),
  **0.346 ms** at 4× that. §3.8 estimated 0.05–0.2 ms. **Desktop CI box, not a Quest 3.** No
  headset has seen any number in this round. The shipped gate reports its own cost on hardware
  as the `WallFade.TableGate` step, which is the figure to actually quote.
- The first draft of that cost case timed 13,335 owned renderers — 15 in *every* one of the
  seven classes — and reported comfortable headroom from a population that does not exist. The
  corrected shape and the mistake are both recorded in `WallCommitDiffVectors.cs`.
