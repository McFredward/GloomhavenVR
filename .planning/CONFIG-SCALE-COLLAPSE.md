# The config scale collapse — a zeroed dial lost its resolution

2026-08-26. Two commits, on top of ModBuild 293 (`e82ee8f2`).

1. `chore(defaults)` — adopt the user's tuned cfg drop as the shipped defaults (54 values).
2. `fix(worldui)` — the defect that rebase exposed, which would otherwise have shipped a
   1000x-too-fine step into the headset on the exact dials his original complaint was about.

They are separate on purpose: commit 1 fails the wire suite (150792 passed / 22 FAILED) and
commit 2 is the remedy, so the defect and its fix stay legible in history.

---

## 1. The rebase

`python3 scripts/rebase-defaults.py apply` against `.planning/debug/default/*.cfg`, taken
verbatim per the standing ruling that a dropped cfg is always measured against the newest build.
**54 defaults rebased**, 526 entries checked, 34 left alone (seeded / legacy / pinned), 192 cfg
keys with no Defaults line (retired keys BepInEx still round-trips — not guessed at).

By area:

| area | n | notes |
|---|---:|---|
| `[Cards]` card-tray pose (`Tray{Right,Down,Forward,Pitch,Yaw,Scale}`) | 6 | a full re-seat: the tray moved from 0.555 m to 0.113 m off centre and grew 2.2x |
| `[Cards]` per-board offset families zeroed | 15 | `SlotOverlayOffset_*`, `SlotOverlaySpacing_*`, `ActiveOffset_*`, `PinOffset_*`, `ElementsOffset_*` — see §2 |
| `[Cards]` per-board offsets retuned | 17 | `Readout`, `Initiative`, `Objectives`, `ItemUseSlot`, `Decision`, `PickBanner`, `HoverHint`, `Pile`, `Asset`, `ConfirmUndo` |
| `[Cards]` board geometry | 5 | `Board Steel -> Bronze`, `BoardScale_Steel`, `BoardPitch{Min,Max}_Steel`, `DecisionGap_{Steel,Bronze}` |
| `[Cards]` scales / keycaps | 5 | `ActiveCardScale_{Steel,Bronze}`, `SlotOverlayScale_Bronze`, `RestButtonDiameter`, `RestStackSpacing` |
| `[Comfort]` `SavedScaleMultiplier` | 1 | 2.7331 -> 0.834434 |
| `[Net]` `MaskId` | 1 | 2 -> 0 |
| `[WallFade]` | 3 | `CommitTableGate` and `SignatureCulpritCensus` false -> **true**, `ExitDwellMovedSeconds` 2.5 -> 0.5 |

Two that should not be a surprise:

* **`[Cards] Board  Steel -> Bronze`.** A fresh install now starts on the bronze board. That is
  his current choice and it is intended.
* **Five offset families are now zero** — spelled as the `1e-10` / `2e-09` epsilon the in-VR
  sliders and BepInEx's float32 round-trip write for an explicit zero, not as `0`. That spelling
  is the whole of §2.

One thing to flag rather than fix, because the brief said take the drop verbatim and I did:
**`[WallFade] SignatureCulpritCensus` and `CommitTableGate` now ship `true`.** Those read as
diagnostic instruments rather than settings, and a fresh install will now run the culprit census
by default. If that is not wanted, it is a one-line pin (`// => [WallFade] SignatureCulpritCensus
(pinned: …)`) and the rebase script will leave it alone from then on — the mechanism the eight
other pinned entries already use. I did not make that call.

---

## 2. The mechanism

### What broke

`ConfigCatalog.OwnScale` decides how far one press of the in-VR menu's ◀ / ▶ moves a dial:

```csharp
item.HasRange && item.Max > item.Min ? item.Max - item.Min
                                     : Magnitude(item.Entry.DefaultValue);
```

With no declared range, a dial's *scale* is the magnitude of its own shipped default — and the
family takes the largest scale among its members (`ConfigSteps.FamilyOf`), which is the fix for
three earlier user reports about one axis of a vector feeling dead. `ConfigSteps.Resolve` then
derives the step from the key's unit word and **caps it at `scale / 4`**.

For `[Cards] SlotOverlayOffset_*` the unit rule gives the right answer on its own — `Offset` is a
length, lengths step in millimetres, so 0.001 m. The cap is what broke. After the rebase the whole
family's largest magnitude is `3e-10`, so:

    cap = 3e-10 / 4 = 7.5e-11  ->  snapped up to NiceStep's floor  ->  1e-6 m

**One press went from one millimetre to one micron.** To move a card overlay the 18 mm the family
used to span: 18,000 presses. That is the complaint that started this whole file
(*"die Schrittweite zu hoch ist, und ich den optimalen Punkt so immer überspringe"*) arriving
again on the same family, from the opposite direction.

### Why the existing zero-guard did not fire

`Resolve` has always said a scale of zero bounds nothing — its own parameter doc reads *"Zero
where the whole family ships at 0, which is not a statement about resolution and therefore bounds
nothing"* — and 72 dials ship at exactly `0` and take that branch today, correctly.

The premise was right and the test for it was wrong. **This config does not spell an explicit zero
as `0`.** It spells it `1.1175871e-10`, `1.974e-09`, `4.74975e-11` — what the sliders and the
float32 round-trip produce — and an epsilon is greater than zero, so `scale > 0d` passed and the
entry was taken at its word. `rebase-defaults.py`'s own tolerance already treats those as zero
(`near(0, 1.1e-10)` is true, which is why `check` is green); the resolver did not.

The wire guard `ConfigStepVectors` already knew this, in a comment, two lines from the code that
got it wrong: *"Epsilon seeds are excluded: this config writes 1e-10 / 2e-09 to mark an explicit
zero, and a marker is not tuning."* The judgement existed. It had just never been applied on the
resolver side.

### The fix

A dial's scale is a property of what it *can be set to*, never of what it happens to be set to
right now. So an epsilon-zero takes the same branch its exact-zero siblings already take, and
nothing else changes:

```csharp
internal const double ZeroMagnitude = 1e-6d;

internal static double OwnScale(bool hasRange, double min, double max, double magnitude)
{
    if (hasRange && max > min)
        return max - min;
    return magnitude > ZeroMagnitude ? magnitude : 0d;
}
```

This is deliberately **not** a new rule. It is the rule the code already documents, with a test
that can recognise the zeros this config actually writes.

**Why 1e-6, measured.** Across the 403 stepped defaults the two populations do not overlap by
anything like a factor:

| | value |
|---|---|
| largest epsilon-zero marker in the config | `2e-09` (`[Cards] HoverHintOffset_*`, `ElementsOffset_Bronze`) |
| **smallest genuinely tuned magnitude** | **`0.004`** (`[Cards] SlotCardInset`, `[Cards] FanFollowDeadzone`, `[BoardButtons] Travel`) |
| finest real dial in the mod | `0.0002` (`[HexHighlight] StableDepthBias`) — has a declared range, so it never reaches this branch |

Six orders of magnitude of empty space. `1e-6` sits 500x above the largest marker and 200x below
the finest real value, and it is the number this code already uses for the same judgement in two
other places: `NiceStep`'s floor (*"nothing a player meets is anywhere near this small"*) and the
reachability guard's own epsilon filter.

### The two candidate directions I rejected, and why

* **Give the families a declared range via `Clamped(...)`.** Rejected. It invents limits the user
  never asked for on dials he is actively tuning, and a declared range also *clamps the live
  value* — his current tuning would be silently re-bounded by a number I picked. (The recorded
  trap applies too: the number inside `Clamped(...)` is the pre-bind fallback, not the shipped
  default. Both are real reasons; the clamping one is the fatal one.)
* **Derive the family scale from the unit/role instead of the literal.** Rejected as
  *unnecessary*, and that is the interesting part: the unit rule **already produced the right
  answer** (0.001 m from the `Offset` / `Spacing` suffix). The cap derived from the literal was
  the only thing overriding it. Replacing the magnitude term wholesale would have rewritten the
  scale for all 403 dials to fix 15; removing a bound that a zero was never entitled to impose
  touches exactly the 15.

### Mirrors, by construction

`ConfigCatalog.OwnScale` and `ConfigStepVectors.FamilyScale` were two hand-kept copies of one
expression, with a comment on the test asking that they stay mirrors. They *were* faithful — that
is why the guard caught this the day the cfg drop landed. But a hand-kept mirror is one that
eventually is not, and the failure mode is silent: the guard goes green while the menu misbehaves.
Both now call the single `ConfigSteps.OwnScale`. Each still owns its own loop, because only the
callers know what a "member" is (a `ConfigItem` in the catalog, a parsed Defaults line in the
guard).

---

## 3. `rule-vector-defaults-are-reachable` — a canary counting the wrong thing

    FAIL: only 44 tuned vector components found (63 on 2026-08-25); either the Vector defaults
          moved or this guard has stopped parsing them

The guard conflated two questions in one number:

* **the assertion** — every tuned component must be reachable by its own dial's arrows. Per
  component, correct, unchanged.
* **the canary** — "is this guard still reading the Defaults at all?" — floored at 55.

The canary was the *tuned* count, i.e. the count of **non-zero** components. That makes a guard
about the parser a function of **how much the user has tuned**: he legitimately zeroed five offset
families, the count fell 63 -> 44, and the canary reported a parser failure that had not happened.

A count of non-zero values cannot answer "did the parse work". The number of components the parser
**produced** can, whatever their values, so that is what is floored now: `parsed >= 150` against
**180 today** (61 Vector2/Vector3 dials). Zeroing every vector in the mod would leave `parsed` at
180 and simply give the assertion nothing to say, which is the correct outcome. The tuned count is
still reported in the failure text, where it is useful and not load-bearing.

This is a corrected instrument, not a relaxed one: the old floor could not have failed for the
reason it named, and the new one can.

**No assertion was weakened to make the build green.** The 21 `rule-bounds` /
`rule-the-reported-dials` failures were fixed in the shipped resolver; `rule-the-reported-dials`
still pins `[Cards] SlotOverlay{Offset,Spacing}_{Oak,Steel,Bronze}` to exactly 0.001 by name, and
it passes because the step really is 0.001 again.

---

## 4. What one press is worth, in millimetres

`[Cards] SlotOverlayOffset_*` (and the four sibling families, all identical):

| | family scale | one press |
|---|---|---|
| ModBuild 293, before the rebase | 0.018 m | **1 mm** |
| **rebase alone (commit 1, no fix)** | 3e-10 m | **0.001 mm** — one micron, 1000x too fine |
| rebase + fix (commit 2) | 0 (a zero bounds nothing) | **1 mm** |

Measured, not derived: a step dump over all 403 dials with the old and the new default table.

All five collapsed families restore to exactly 0.001:

| family | pre-rebase scale / step | post-fix scale / step |
|---|---|---|
| `Cards/SlotOverlayOffset` | 0.018 / 0.001 | 0 / 0.001 |
| `Cards/SlotOverlaySpacing` | 0.008 / 0.001 | 0 / 0.001 |
| `Cards/ActiveOffset` | 0.09 / 0.001 | 0 / 0.001 |
| `Cards/PinOffset` | 0.022 / 0.001 | 0 / 0.001 |
| `Cards/ElementsOffset` | 0.04 / 0.001 | 0 / 0.001 |

### Blast radius, measured two ways

**The code change is inert on the old table.** Running the new resolver against the pre-rebase
Defaults gives **150861 assertions passed — bit-identical to the ModBuild 293 baseline.** The
`ZeroMagnitude` branch never fires on a table with no epsilon-zero family, so nothing that was
working could have moved.

**After both commits, exactly ONE dial in the whole mod steps differently from ModBuild 293:**

    [Cards] TrayRight   0.005  ->  0.001   (5 mm -> 1 mm)

and that is the *rebase*, not the fix, and it is the rule working: he moved the card tray from
0.555 m off centre to 0.113 m, so the family is genuinely five times smaller and the `scale/250`
floor stops coarsening the millimetre away. It gets finer, on the grid he tunes on. Every other
one of the 403 dials resolves to the same step it did before.

### The assertion count moved 150861 -> 150784 (-77), fully attributed

Per-case, by instrumenting the harness and diffing the two default tables:

| case | before | after | delta |
|---|---:|---:|---|
| `configsteps/rule-bounds` | 679 | 621 | **-58** |
| `configsteps/rule-vector-defaults-are-reachable` | 64 | 45 | **-19** |
| everything else | — | — | 0 |

* **-58** = 30 dials that now ship an all-zero family and are skipped (`s <= 0`, two assertions
  each = -60), **plus** `[Cards] ConfirmUndoOffset`, which he tuned *away* from zero to
  `(0, 0, 0.012)` and which is therefore checked for the first time (+2).
  Of those 30 newly-skipped dials only **15** are the epsilon families this fix recognises; the
  other 15 (`AssetOffset_*`, `Asset{Pitch,Yaw,Roll}Degrees_*`, `PileOffset_*`) he zeroed to
  literal `0`, and the old code skipped those too. None of the 30 changed step.
* **-19** = the tuned-component count falling 63 -> 44 because five vector families are now zero.
  Zeros are not reachability assertions; there is nothing left to assert about them.

Every one of the 77 is a dial that no longer has anything to say, or a count that was measuring
the user's tuning instead of the code.

---

## 5. Gates

| gate | required | got |
|---|---|---|
| build | 0 errors / exactly 4 warnings | **0 / 4** |
| wire tests | 150861 | **150784** — clean pass, -77 attributed above |
| mirrors | 18 | **18** |
| frame order | 7 | **7** |
| patch inventory | 80 / 132 | **80 / 132** |
| remote defaults | 76 | **76** |
| refasm | 16 | **16** |
| `rebase-defaults.py check` | — | **clean, 526 entries, 0 differ** |

`ModBuild` not bumped. Bundle not rebuilt. Nothing touched in `WorldUI/ActorBars.cs` or
`Board/FigureGrab/**`.

`patch-inventory.sh` prints a pre-existing `warn: docs/PATCH-INVENTORY.md line references have
shifted (no patch added, removed or unregistered)`. Not mine: neither `ConfigCatalog.cs` nor
`ConfigSteps.cs` appears anywhere in that document.

---

## WHAT I COULD NOT VERIFY WITHOUT HARDWARE

1. **That one press actually moves a card overlay one millimetre in the headset.** Everything in
   §4 is `ConfigSteps.Resolve` — the number the menu *asks for*. Whether the row renders it,
   whether the arrow's hold-repeat rate makes 1 mm usable, and whether the overlay actually moves
   by the amount the config says are three further links in the chain that only a Quest 3 closes.
   What I *have* verified is that the resolver returns the same number it returned on ModBuild
   293, which he has already tested; the claim is "unchanged", not "good".

2. **That the 54 rebased values look right in VR.** I took them verbatim, as instructed, and made
   no judgement about any of them. In particular: a fresh install now starts on the **bronze**
   board, and `[Cards] BoardScale_Steel` halves (1.127 -> 0.543) while `TrayScale` more than
   doubles (0.783 -> 1.694). These were tuned on his rig and are unseen by me.

3. **Whether zeroing those five offset families is what he meant.** `SlotOverlayOffset_*` at zero
   means the slot overlays now sit exactly on their anchor with no nudge on any board. That is a
   legitimate tuned value and the fix exists precisely so it costs him no resolution — but I
   cannot see whether the overlays look right there, only that he can still move them 1 mm at a
   time when they do not.

4. **`[WallFade] SignatureCulpritCensus = true` and `CommitTableGate = true` as shipped
   defaults.** Taken verbatim. I could not measure what the census costs per frame on a Quest 3,
   and a diagnostic left on by default is the shape of a known past defect ("a probe that answered
   is spent"). Flagged in §1, not changed.

5. **Multiplayer.** No wire format, packet, or `NetProtocol` constant was touched, and `remote
   defaults` still resolves 76/76, so peers on this build agree on every rebased default. But a
   *mixed* session — this build against 293 — now disagrees on 54 default values for any config
   entry a peer has never written to its own cfg. That is the ordinary consequence of any defaults
   rebase and the `ModBuild` version-mismatch dialog is the mechanism that covers it; it has not
   been exercised on hardware here.
