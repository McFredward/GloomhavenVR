# Lane cards — changes needed OUTSIDE this lane's file set

> **[verified 2026-09-08 against `49ceab21` (ModBuild 483)] SOME OF THESE HAVE BEEN APPLIED, AND
> THIS FILE DOES NOT SAY WHICH.** A NEEDED-OUTSIDE list is written at the moment the lane closes
> and is never revisited, so its standing claim that "nothing here has been applied" decays into a
> false statement the first time the integrator lands one of them. Per-item status was **spot
> checked, not exhaustively re-derived** — check the item against source before acting on it, and
> read the line numbers as advisory (they are from the lane's base commit, not from `49ceab21`).
>
> Verified in this pass: **§1's pairs are STILL OPEN** — no shared helper was created for any of
> the three, so the side-by-sides below are still the current state of the code.

> Each entry: the files, the reason, and the exact change (or the side-by-side when the change is
> a merge the integrator has to decide). Nothing here was committed by lane cards.

## 1. The three local↔remote pairs the census names — side by side (brief §2, "a merge across lanes is a finding")

All three were read at b40f8564. Verdict in one line: **the bodies are byte-identical after
normalisation; the only differences are (a) WHERE the dial comes from — the local half reads
`CardsConfig.<Dial>.Value` (its own board, so the owner's value), the remote half reads a field the
owner's wire record filled — and (b) log-line prefixes/counters.** (a) is the mirror-dial rule
applied correctly (`check-mirror-dials.py`), not drift. A merge is therefore a SHARED HELPER that
takes the dial VALUES as parameters, with the remote caller passing the owner's synced copy — never
`CardsConfig` — and it touches a `Net/Remote/` file in every case, which is lane net's.

### 1.1 `Cards/Piles/PileViewer.cs:1140-1185` (`PileStack.SetUsableCue` tail + `BuildUsableRings`) ↔ `Net/Remote/RemoteControlBoard.cs:3603-3648`

| local | remote |
|---|---|
| `if (_usableEmbers == null && on) _usableEmbers = BuildUsableEmbers();` … `Play()/Stop(withChildren:false, StopEmitting)` | identical |
| `if (_usableRings == null && on) _usableRings = BuildUsableRings(); … SetActive(true); Show(); / Hide();` | identical |
| `BuildUsableRings`: `alpha = Clamp01(CardsConfig.ItemCueRingAlpha.Value); reach = Max(1f, CardsConfig.ItemCueRingReach.Value); if (alpha <= 0.002f \|\| reach <= 1.001f) return null;` | `alpha = Clamp01(_itemCueRingAlpha); reach = Max(1f, _itemCueRingReach);` — the OWNER's synced values (correct; `MIRROR-DIALS.allow` records the pair) |

**Proposed (integrator / lane net):** a `WorldUI.SoftCueRings.Build(Transform parent, float alpha,
float reach, …)` (or a static on `PileFanShape`, which `RemotePileFronts` already reads from
`Cards/` — "pure math is shared, calls go down", `check-mirrors.sh:393`) called by both; the remote
keeps passing `_itemCueRingAlpha/_itemCueRingReach`. Until then the pair stays as designed. **Not
a defect.**

### 1.2 `Cards/CardFan.cs:1297-1345` (`UpdateGazeBias`) ↔ `Net/Remote/RemoteHandFan.cs:5352-5395`

| local `UpdateGazeBias(away, headForward)` | remote `UpdateGazeBias(away, headForward, dt)` |
|---|---|
| project both on world-up; `SignedAngle(awayH, gazeH, up)`; `mag`, `side` | identical |
| the `_gazeSide` latch: commit past `GazeBiasDeadzoneDeg`, release inside `GazeBiasReleaseDeg`, opposite side only on a firm crossing | identical, shorter comment |
| `t = Clamp01((mag − Deadzone)/Max(0.01, Full − Deadzone)); t = t·t·(3−2t); target = Clamp(side·mag·Gain·t, ±MaxYaw)` | identical |
| the six constants are FILE-LOCAL `const`s, pinned pairwise by `check-mirrors.sh:329-334` | same |
| easing tail uses the fan's own `Time.unscaledDeltaTime` | takes `dt` from the caller (the mirror runs on the receiver's clock) |

The remote's doc says *"CardFan.UpdateGazeBias, term for term"* and it is. Both halves keep the
same two instance latches (`_gazeSide`, `_gazeBiasYaw`). **Proposed:** a static
`CardFan.GazeBiasStep(Vector3 away, Vector3 headForward, float dt, ref int side, ref float yaw)`
in `Cards/` that both call (Net may call Cards; the reverse is forbidden by the same ruling), the
six constants staying in `CardFan.cs` so the mirror pins keep their subject. **Not a defect** — the
constants are already gated against drift; the LOGIC is not, which is the argument for the fold.

### 1.3 `Cards/HandFanEnhancementRefresh.cs:150-205` ↔ `Net/Remote/RemoteFanEnhancementRefresh.cs:165-220`

The sticker-rewrite loop — read `sticker.Enhancement` under try (the getter walks
`CharacterClassManager` and can throw), compare to `sticker.EnhancementType`, then the game's own
two writer arms verbatim (`EnhancedAreaHex.RemoveEnhancement/ApplyEnhancement`,
`EnhancementButton.UpdateEnhancement`) — is identical line for line. Differences: the log prefix
(`Enhancement refresh:` vs `Peer enhancement refresh:` — both grep tokens in their own right; a
helper must keep emitting both spellings), the counter name (`writtenOnThisCard` /
`writtenOnThisSlab`), and the ORDER of the two statements in the refused-write `catch` (the local
increments `failures` before the Warn, `:203-204`; the remote after it, `:219-222` — same count, so
NOT an asymmetry; recorded because a truncated read first said otherwise). **Proposed merge:** `Cards/EnhancementStickers.Rewrite(
IEnumerable<…> stickers, string scope, string prefix, out int written, out int failures)` called
by both, per the "calls go down" ruling.

## 2. `.planning/deadlock-class-audit.md`

R5 §5 lists seven places where that audit is wrong at 479/480 (17 park sites not 12; A.5's negative
measured the wrong population; D.3.1/D.3.2/C.5.1 fixed; A.6 answered; C.2 still open). It is a
planning doc outside every lane's set. Nothing in lane cards depends on it; recorded so the
integrator can hand it to whoever owns `.planning/`.

## 3. `Net/Remote/RemoteCardFx.cs` doc (from R3 N8 family, not re-verified here)

`RemoteBrowserFan.cs:52` still describes its collapse as *"smoothstep along the chord + a sine
bow"*; the code calls `RemoteFlightCurve`. Documentation only; lane net.

## 4. `.planning/refactor/INSTRUMENT-WRITES.baseline` — a rebaseline only the integrator can take

`scripts/check-instrument-writes.py` counts `=`, a compound operator and `++`/`--` as writes,
and NOT a mutating call. So a diagnostic that does `_seen.Add(id)` or `_pending.Clear()` on a
field the mechanism then reads is invisible to the gate whose whole reason for existing is
`a-write-inside-a-logger`. Implemented and measured on a scratch copy (not committed):

    before: 558 fields written by diagnostics, 65 load-bearing
    after:  672 fields written by diagnostics, 102 load-bearing   → 39 new pairs, gate exits 1

The gate is baseline-gated, so landing it means running `check-instrument-writes.py --baseline`,
which rewrites entries naming **every lane's** files — `FadeDriver` ×11, `ModalFallback` ×3,
`MixedReality`, `MapIconHoverAnimation`, `MapLocationInteractor`, `PerfMonitor`,
`GrabbableProp`, `ActorPropBody`, `NetCardFx`, `CameraOrderProbe`,
`InitiativeSelectionGlow`, … Lane cards may not touch those entries (brief §2), so the change
is handed over rather than half-landed.

**Read `REVIEW-cards.md` §5.7a before taking it.** A large share of the 39 are scratch buffers,
not state (`_matScratch`, `_subtreeScratch`, `RendererScratch`, `UnbackedScratch`, and this
lane's `ActiveCardSet::s_sb`/`::s_sort`), which read as load-bearing only because the helper
that consumes them is named `Format` and so does not match `DIAG_NAME`. Landing the scan
without also seeding `Format`-shaped names — or exempting a field cleared on both sides of its
use — fills the registry with entries that are not constraints, and the registry's value is
that every line in it is one. `NetCardFx::s_queue <- Report` is the one hit that looks like the
real class and is worth reading first.

The patch is four lines plus a comment; it is in the lane's scratch as
`collwrite_fix.py` and is trivial to re-derive from §5.7a.
