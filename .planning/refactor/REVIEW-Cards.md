# Subsystem review — `src/GloomhavenVR/Cards/`

> Phase 1 output. 22 files, 23 955 lines. Companion to `INVARIANTS-Cards.md` (239 entries),
> which is the constraint set every proposal below was checked against.
>
> **Nothing here has been changed.** No source file was touched, nothing was committed.
>
> Findings are ordered by **value ÷ risk**, highest first. Every one states its tier, the
> exact files, what moves, the expected `scripts/refactor-guard.sh` output, and the risk if
> the analysis is wrong.

---

## 0. Verdict up front

This is not badly written code. It is **well-commented code in files that grew past the point
where the comments can be navigated**. 29–49 % of every large file in this subsystem is
comment, and almost all of it is load-bearing (`INVARIANTS-Cards.md` proves that for 239
separate places). The maintenance problem is therefore *not* "this logic is tangled" — it is
"a 5 483-line file is a 5 483-line file".

That points the whole review at one class of change: **move code without touching it**, and
**fix documentation that lies**. Concretely:

| | |
|---|---|
| Recommended now | 4 pure-motion splits (Tier 1), 4 dead-code removals (Tier 0), 12 documentation corrections (Tier 0) |
| Recommended with evidence attached | 2 deduplications (Tier 2) |
| Explicitly recommended **against** | 7 items, listed in §5 — including three that look like obvious wins |
| Deferred to the user | 3 items in §6 that would need real behaviour changes |

The single highest-value finding is **not a refactor**: it is §1, which shows that a claim in
`CHARTER.md` about the guard is wrong, and that the correction determines which splits are
achievable with an empty diff.

---

## 1. PREREQUISITE — the guard does not behave the way the charter says

`CHARTER.md` §3 states:

| Refactor kind | Expected guard output |
|---|---|
| Move a type to another file, split a file, **reorder members** | **empty diff** |

**The "reorder members" half of that row is false**, and it matters for every split below.

### Evidence

`ilspycmd -p` writes one file per **top-level type** and emits members in **metadata order**,
which is source order. Comparing the stored baseline against the source:

```
decompiled .guard/baseline/GloomhavenVR.Cards/CardsDriver.cs, methods in order:
  OnEnable OnDisable SubscribeBoardTuning OnControlOffsetChanged … ClearBrowseHover
source     src/GloomhavenVR/Cards/CardsDriver.cs, methods in order:
  OnEnable OnDisable SubscribeBoardTuning OnControlOffsetChanged … ClearBrowseHover
```

Identical sequences, for all 60 members checked. Fields likewise. **Reordering members
therefore produces a non-empty guard diff** — a permutation, in which every moved member body
appears once as `-` and once as `+`.

### The consequence for partial-class splits

`GloomhavenVR.csproj` uses the SDK's default `**/*.cs` glob (no explicit `<Compile Include>`
items except the generated `BuildInfo.g.cs`). Compilation order therefore follows the glob
enumeration — in practice ordinal filename order per directory. Metadata order = concatenation
of each source file's members in compile order.

So a partial-class split is an empty-diff operation **only if the concatenation reproduces the
original order**, which requires two things:

1. each new file holds a **contiguous** run of the original file's members, in original order;
2. the files **compile in the same order** as those runs appeared.

Condition 2 is filename-sensitive in a way that is easy to get wrong: `CardsDriver.Laser.cs`
sorts *before* `CardsDriver.cs` (`'L'` = 76 < `'c'` = 99), so the naive naming inverts the
order and produces a whole-file permutation diff.

### The exception that is genuinely free: nested types

ILSpy **hoists every nested type to the top of the parent's decompiled file**, ahead of the
parent's own fields and methods, in their mutual metadata order. From the baseline:

```
.guard/baseline/GloomhavenVR.Cards/PlayTray.cs
   15  internal sealed class PlayTray : IPanelGrabOwner
   17      internal readonly struct LaserTarget          (source line  336)
   30      private sealed class SlotPulse               (source line 2398)
   58      private sealed class BoardSurfaceTarget      (source line 3619)
   73      internal sealed class BoardButton            (source line 3631)
  785      private const float SlotCaptureRadius        ← first ordinary member
```

Therefore: **moving nested types into their own source file changes nothing in the decompiled
output, provided their mutual order is preserved.** That is what makes findings §2.1 and §2.2
the safest large moves available in this subsystem.

### Recommendation

- Amend `CHARTER.md` §3: replace "reorder members → empty diff" with the two conditions above.
- **Do the calibration commit first.** Finding §2.1 (move `CardsDriver.BurnSlab`, 60 lines) is
  the ideal first commit: it is the only nested type in that class, so its metadata position
  cannot move regardless of filename, and the predicted guard output is *exactly empty*. If
  the guard prints anything at all, the model above is wrong and every later split must be
  re-planned. One cheap commit buys certainty for the other ten.
- For the file splits in §2.3–§2.4, pin compile order explicitly in `GloomhavenVR.csproj`
  rather than relying on filename sort. That is a build-file change, not a source change; its
  own guard diff is empty because item order does not survive into IL when the order is
  already correct.

**Tier:** 0 (documentation) + a build-file edit.
**Risk if wrong:** none — this finding only changes what output we *expect*. Getting it wrong
the other way (assuming empty, seeing a 3 000-line permutation) would look like collateral
damage and could scare a correct refactor into being reverted.

---

## 2. Pure motion (Tier 1)

### 2.1 Move `CardsDriver.BurnSlab` to its own file — the calibration commit

**Files:** `Cards/CardsDriver.cs` → new `Cards/CardsDriverBurnSlab.cs`
**What moves:** lines 3481–3546 (the doc comment plus `private sealed class BurnSlab : MonoBehaviour`,
60 lines) verbatim. `CardsDriver` becomes `internal sealed partial class`.
`BurnSlab` uses `CardsConfig`, `CardMesh`, `Core.VRLayers`, `VRCard.FlyArcHeightFraction`,
`VRCard.FlyArcOffset` — all external, so it does not even need to stay nested; but keeping it
nested keeps `BurnSlab.Launch`'s single call site (`CardsDriver.TryAnimateBurn`) unqualified
and keeps the type private, which is the smaller change.

**Guard expectation:** **empty.** `BurnSlab` is the only nested type of `CardsDriver`, so its
hoisted position is index 0 either way, and no ordinary member moves.

**Risk if wrong:** effectively nil. If the guard prints anything, that is the calibration
signal §1 asks for and the commit is reverted at zero cost.

**Value:** small on its own (60 lines). Large as the instrument that validates §1.

---

### 2.2 Move `PlayTray`'s four nested types into one file — biggest safe win in the subsystem

**Files:** `Cards/PlayTray.cs` (4 613 lines) → new `Cards/PlayTrayParts.cs`
**What moves, in this exact order** (their current mutual order — preserving it is what makes
the diff empty):

| Type | Current lines | Size |
|---|---|---|
| `LaserTarget` (readonly struct) | 336–346 | 11 |
| `SlotPulse` | 2397–2426 | 30 |
| `BoardSurfaceTarget` | 3611–3624 | 14 |
| `BoardButton` | 3626–4612 | **987** |

Total **≈ 1 042 lines**, leaving `PlayTray.cs` at ≈ 3 570. `PlayTray` becomes
`internal sealed partial class`.

`BoardButton` reaches back into `PlayTray`'s private statics (`Tint`, `NewKeycapMaterial`,
`BoxCapShader`, `OverlayMaterial`, `SquareCapBevel`, `CapRestZ`) — all still accessible,
because it stays a nested type of the same partial class. Nothing else changes.

**Guard expectation:** **empty**, provided all four move together into one file and keep their
relative order. Moving `BoardButton` *alone* is filename-order-dependent (see §1) and would
produce a permutation diff if the new file compiled first.

**Risk if wrong:** the guard prints a permutation of the four nested type bodies inside
`GloomhavenVR.Cards/PlayTray.cs` and nothing else. Recognisable at a glance, revert is a
`git checkout`.

**Why this one first among the splits:** `BoardButton` is a genuinely separate concern — a
pressable 3D keycap with its own palette, dwell machinery, depth-fire press, debounce and
dissolve/materialize animation. It shares *nothing* with the board's placement, slot or
watchdog logic beyond four material helpers. Splitting it is the one decomposition where
"these are two different things" is uncontroversial.

---

### 2.3 Split `CardsDriver.cs` (5 483 lines) along its own existing section dividers

The file already carries **32 hand-written section dividers**. They are not decorative: fields
are declared inside the region that uses them (`_laserHover`/`_laserHoverGraceUntil` under
`fan laser`; `ContactTipReach`/`ContactPalmReach`/`ContactStickyMargin`/`_handContactWinner`/
`_contactSuppressed` under `hand-contact single winner`; `_watchRoot`…`_anchorLossLogged`
under `board pose guard`). The regions are already cohesive; only the file is not split.

**Proposed cut points — every one is an existing divider, and every run is contiguous:**

| New file | Source lines | Regions | Size |
|---|---|---|---|
| `CardsDriver.cs` (kept) | 1–456 | header, all shared fields, `lifecycle`, `board tuning (Part F)` | 456 |
| *(2)* `CardsDriver` part 2 | 457–1467 | `board pose guard`, `handlers`, `update`, `tick attribution guard`, `fan diagnostics`, `card audio` | 1 011 |
| *(3)* `CardsDriver` part 3 | 1469–2484 | `fan laser`, `fan hover split`, `hand-contact arbitration`, `board laser`, `browse laser`, `active laser`, `modal input-block`, `slot snap preview` | 1 016 |
| *(4)* `CardsDriver` part 4 | 2485–3558 | `hand fan reorder`, `rebuild`, `fly-to-pile`, `MP card-FX anchors`, `BurnSlab`, `HookCard` | 1 074 |
| *(5)* `CardsDriver` part 5 | 3559–4450 | `interactions`, `pick flows`, `short rest` | 892 |
| *(6)* `CardsDriver` part 6 | 4451–5483 | `overlay gate`, `wanted-slot hint`, `initiative to-do`, `long-rest tracing`, `take-damage selection`, `long-rest turn pump`, `pile browse`, `active cards`, `dev fake hand` | 1 033 |

**What moves:** whole regions, byte-identical, in order. No member is edited, renamed or
reordered *within* a region. `CardsDriver` becomes `partial`.

**Guard expectation:** **empty**, but only under the §1 conditions — the six files must
compile in the order 1…6. Pin this in `GloomhavenVR.csproj` with explicit `<Compile>` items
rather than trusting the filename sort. If the order is wrong the diff is a large but *pure*
permutation confined to `GloomhavenVR.Cards/CardsDriver.cs`, with no textual change inside
any member body.

**Risk if wrong:** the failure mode is a scary-looking-but-harmless permutation diff (see
§1). The genuine risk is different and worth naming: **a split invites future edits to drift
between regions that are coupled by call order.** The invariant registry names three such
couplings that now span the proposed cut between parts 3 and 4 and between 2 and 3:

- `TickInteractionsAndStatus` (part 2) fixes the call order `laser paths → UpdateHandContactArbitration`
  (part 3). Registry: *"Arbitration runs after the laser paths; the laser-hovered card is exempt …
  Breaks if: Reordering the tick calls 'since they are all independent'."*
- `Rebuild` (part 4) must run before `TickBurnToPile` (part 4) — both stay in part 4. Good.
- `TickBoardPoseWatch` (part 2) must run **last** in `Update` (part 2). Both stay in part 2. Good.

Mitigation, and it should be a condition of doing the split: **each new file gets a header
comment naming the regions it holds and the cross-file ordering constraints its members
participate in.** That is a net documentation gain, not a cost.

**Value:** the largest file in the mod drops from 5 483 to a 456-line index plus five ~1 000-line
topic files, each of which corresponds to a section a reader already navigates by.

---

### 2.4 Split the remainder of `PlayTray.cs` (≈ 3 570 after §2.2)

Same argument, same mechanism, same 16 existing dividers. Lower priority than §2.3 because
§2.2 already removes 23 % of the file.

| New file | Source lines | Regions | Size |
|---|---|---|---|
| `PlayTray.cs` (kept) | 1–1164 | class doc, all fields/consts, mount accessors, `laser targets`, `lifecycle` (`EnsureBuilt` + `Build*`), `grab handle`, `dashboard controls`, `ApplyFollowMode`, `Destroy`, `SetVisible`/`TickPlacement`/`PlaceAtHead`/`ReassertPlacement` | 1 164 |
| *(2)* | 1165–1450 | `LOST-BOARD WATCHDOG` (incl. `SyncPinHolder`, `IsInHeadView`, `IsFinite`) | 286 |
| *(3)* | 1451–1895 | `debug-menu live apply`, fixed base positions, `ReapplyOrientation`, rebuild helpers, `board-switch pose`, `PersistPoseToConfig` | 445 |
| *(4)* | 1896–2637 | `slots`, `pick field`, `highlight`, `item-use slot` | 742 |
| *(5)* | 2638–2807 | `status` (`TickStatus`) | 170 |
| *(6)* | 2808–3570 | `build` helpers, shaders, materials, `MeasureBoardLocalExtents`, board diagnostics | 763 |

The watchdog file *(2)* is the standout: it is 286 self-contained lines carrying the single
most expensive lesson in the subsystem (registry §6 devotes 8 entries to it, including
"**the comment block with no code under it IS the invariant**"). Giving it its own file makes
that block impossible to mistake for cruft.

**Guard expectation:** empty under the §1 conditions.
**Risk if wrong:** as §2.3.

---

### 2.5 Two verbatim method extractions inside `CardsDriver.Rebuild` (431 lines)

`Rebuild` (lines 2683–3113) is the longest method in the subsystem. Two of its blocks are
contiguous runs that depend only on fields plus locals defined *inside* the run, so their
bodies move byte-for-byte:

| Extract | Lines | Size | Signature |
|---|---|---|---|
| the zone-flag sweep | 2948–3056 | 109 | `private void RebuildZoneSweep(CardsHandUI hand, bool grabbable)` |
| the two dock-appear loops | 3069–3106 | 38 | `private void PlayDockAppearAnimations(bool halfVisible)` |

`Rebuild` drops to ≈ 290 lines. Both extracted bodies are quoted verbatim; only the enclosing
`{ }` and the two parameter reads change.

**Guard expectation:** diff **confined to type `CardsDriver`**, showing two new private
methods and their bodies removed from `Rebuild`. The C# compiler may or may not inline them —
if it does not, the diff is exactly the two new methods plus two call sites.

**Risk if wrong:** medium-low, and specific. The zone sweep is dense with invariants
(registry §9: the strict `else if` park chain, `IsFlying || IsVanishing` continue, the
"not parked" pose-recording predicate, `PokeSelectEnabled = false` re-assert). None of them is
*about* where the code lives, but all of them are about statement order inside the run — which
a verbatim move preserves by construction. The one thing that must be checked in review: the
sweep reads `grabbable`, which the `switch (mode)` above assigns; passing it as a parameter is
the only edit and a wrong parameter would silently disable card grabbing.

**Why not extract the `switch (mode)` too:** it assigns three locals (`trayVisible`,
`halfVisible`, `grabbable`) that the code after it reads. Extracting it needs `out` parameters
or a return struct — a real code change, not motion. → §6.

---

## 3. Dead code (Tier 0)

Each entry below was verified by full-repo `grep` over `src/**/*.cs` (excluding `obj/` and the
`.claude/worktrees/` copies) and checked against Charter §5: Harmony surface, Unity messages,
reflection, config keys, log grep tokens, debug menu.

### 3.1 `PlayTray.RenderOnTop` — zero call sites, and the registry documents it as live

```
grep -rn '\bRenderOnTop\b' src/ → 19 hits
  1 declaration   PlayTray.cs:3578   private static void RenderOnTop(GameObject, int queueBase = 4000)
 18 comments      PlayTray.cs ×16, WorldUI/NativeButtonSkin.cs ×2
  0 call sites
```

Every one of the sixteen `PlayTray` mentions is a comment explaining that the widget in
question **no longer** uses it ("drop the `RenderOnTop` shine-through", "NO `RenderOnTop`",
"depth-correct now"). The method survived the fix that removed all its callers.

§5 clear: `private static`, no attribute, not a Unity message, no reflection by name anywhere
in the repo, not a config key, no log line inside it, not reachable from the debug menu.

**This corrects `INVARIANTS-Cards.md` §7.** The entry *"`RenderOnTop` uses per-renderer
instances and two ZTest property names"* reads as a live constraint. The *lesson* is real and
is implemented in two other places — `WorldUI/NativeButtonSkin.cs:217` and
`WorldUI/ActorBars.cs:455` both set `_ZTestMode` under a `HasProperty` guard — but
`PlayTray.RenderOnTop` itself is unreachable. **Removing the method must move that invariant's
"Why" text to the two live implementations, or the reason for the two-property-name rule is
lost.**

**Guard expectation:** the method disappears from `GloomhavenVR.Cards/PlayTray.cs`; nothing
else changes.
**Risk if wrong:** a board HUD widget stops drawing over the board. Bounded by the grep: there
is no caller to lose.

---

### 3.2 `PlayTray.ReseatProud` + `SeatOnBoardFace` + `SeatStandoff` + `SeatProud` — zero call sites

```
ReseatProud       declaration PlayTray.cs:3216 · comments PlayTray.cs:468, CardsConfig.cs:205 · 0 call sites
SeatOnBoardFace   declaration PlayTray.cs:3241 · 1 call site: ReseatProud (PlayTray.cs:3221) · 6 comments
SeatStandoff      declaration 3205 · used only inside SeatOnBoardFace (3253, 3254)
SeatProud         declaration 3208 · used only inside SeatOnBoardFace (3277)
```

`ReseatProud` has **no caller at all**, so `SeatOnBoardFace`'s only caller is itself dead.
The cluster is ≈ 85 lines. `_boardColliders` becomes write-only if it goes (populated in
`EnsureBuilt:464`, cleared in `Destroy:956`, read only by `SeatOnBoardFace:3262`) — but the
same colliders are separately registered as `LaserTargets` at `EnsureBuilt:463`, so the laser
surface is unaffected.

§5 clear on all four. The two log lines inside `ReseatProud`/`NewAnchor` differ:
`NewAnchor`'s "seated at a fixed proud local-Z" line **is** live and must stay.

**This contradicts `INVARIANTS-Cards.md` "Suspected vestigial" → `PlayTray.SeatOnBoardFace` /
`ReseatProud`**, which says: *"They still have callers (the bundle Confirm/Undo and rest
anchors, which do need the true-surface projection), so this is **not** dead."* That is
incorrect for the current HEAD. The bundle Confirm/Undo anchors come from `FindDeep` in
`EnsureBuilt` and are used as-is; the rest anchors are built by `RestControls`; the gear/pin/
readout go through `NewAnchor`, which seats at the fixed `FixedProudZ` and explicitly says so
("in place of the old raycast"). The registry entry was written from the surrounding prose,
which is exactly the trap the registry itself warns about.

**Guard expectation:** two private methods and two private consts disappear from
`GloomhavenVR.Cards/PlayTray.cs`; `_boardColliders` may remain (still assigned) or be removed
with it. Nothing else changes.
**Risk if wrong:** a board widget seats at the wrong depth. Bounded: there is no caller.
**Recommended handling:** remove the methods, **keep** the explanation. Registry §6
("Raycast auto-seating is GONE; fixed proud Z replaces it") is the record of *why*, and the
`CardsConfig.cs:205` comment references both names — that reference must be rewritten, not
orphaned.

---

### 3.3 `PlayTray`'s pick field — built every board build, never shown

The whole cluster is unreachable:

```
BuildPickField        called once (EnsureBuilt:496) → builds 5 GameObjects, ends SetActive(false)
SetPickFieldVisible   0 external callers  ⇒ _pickFieldVisible is never true
SetPickFieldHighlight only caller is SetPickFieldVisible (unreachable)
PickFieldAnchor       0 callers
PickFieldNear         0 callers   (would return false anyway: guards on _pickFieldVisible)
_pickField / _pickFieldHighlight / _pickFieldVisible / _pickFieldHighlighted   internal only
```

Roughly **125 lines** plus a per-board-build allocation of five `GameObject`s, one glow
material and one TMP caption that can never be seen.

`AddCaption` (`PlayTray.cs:3347`) has exactly one caller — `BuildPickField:2076` — so it dies
with the cluster. That makes `PlayTray.cs:3052–3057` stale:

> `// BuildSlotLabels + its call in EnsureBuilt were removed outright (no no-op stub) —`
> `// AddCaption stays live for the pick field's "SELECT" caption, so nothing goes unused.`

`SetPickActive` / `_pickActive` are **live** (they drive the CONFIRM accent in
`TickStatus:2782`) and must stay — the names are adjacent, do not remove them by association.

The class doc already admits the state at `PlayTray.cs:31–32`: *"the old `BuildPickField`
centre field is retained but unused."* Registry §8 ("The pick field hides the play slots")
documents `SetPickFieldVisible`'s behaviour as if live — that entry needs a status note.

§5: no Harmony/reflection/config/debug-menu reach. The one log line
(`"Board: pick drop field shown/hidden"`) is unreachable, so it cannot be a live grep token.

**One thing to check before removing** — and it is a genuine question, not a formality:
`Net/RemoteBoardFurniture.cs` has its **own** `BuildPickField` (line 464) whose doc says it
mirrors `PlayTray.BuildPickField`, and it *is* shown, via `SetPickField(bool)` (line 557).
If peers render a pick field that the local player never sees, that is an MP inconsistency
independent of this refactor. Removing the local copy does not break the remote one (separate
implementation), but the doc cross-reference at `RemoteBoardFurniture.cs:97` and `:460` would
dangle. **Flag this to the user with the removal, do not silently resolve it.**

**Guard expectation:** the six members and four fields disappear from
`GloomhavenVR.Cards/PlayTray.cs`; `EnsureBuilt` loses one call. Nothing else changes.
**Risk if wrong:** a pick flow shows no drop target. Bounded — the field is `SetActive(false)`
from birth and nothing turns it on; the pick flows home into the slot recesses instead
(registry §8, "test #28").

---

### 3.4 `PlayTray.GenericClusterButtonSize` — confirmed unreferenced (registry was right)

```
declaration PlayTray.cs:1547 · comments PlayTray.cs:3077, 3083 · 0 call sites
```

Its own doc opens with `SUPERSEDED` and ends "*nothing in the cluster path uses it*". Registry
§7 explains why it must never be re-wired ("The item USE button does not resize the cluster").

**Recommendation: remove the method, keep both comments.** They are the record of the fix.
Note that `PlayTray.cs:3073–3077` currently *contradicts itself* inside one comment block —
the first paragraph says the area "AUTO-SCALES each cap's size from the live count
(`GenericClusterButtonSize`)", and six lines later the same block says it does not and explains
why. The stale paragraph should go with the method.

`GenericColumnHeight` and `GenericButtonGap` are used only by `GenericClusterButtonSize` and
die with it; check that before removing (`GenericColumnHeight` is also referenced from the
doc of `GenericClusterY`, which stays).

**Guard expectation:** one static method disappears from `GloomhavenVR.Cards/PlayTray.cs`.
**Risk if wrong:** nil — nothing calls it.

---

### 3.5 `ItemsPile` / `BurnCardFx` — the consumed-plume pair, plus two write-only fields

Independently verified (not taken from the registry):

| Symbol | Evidence | §5 |
|---|---|---|
| `BurnCardFx.SpawnConsumedPlume` | 2 occurrences total: declaration `BurnCardFx.cs:134`, one comment `ItemsPile.cs:1156`. **0 call sites.** Its log token `"Consumed-item plume: bounded to the card"` is unreachable. | clear |
| `BurnCardFx.ItemPlumeCardSpan`, `ItemPlumeMaxLifetime` | used **only** inside `SpawnConsumedPlume` — die with it. `StartSizeMultiplier`/`StartSpeedMultiplier` are shared with `Bind` and **stay**. | clear |
| `ItemsPile.ItemChip._plume` | 4 occurrences, **zero non-null assignments** — the `OnDisable` destroy branch (2188–2192) is unreachable | clear |
| `ItemsPile.ItemChip._hasClip` | 4 occurrences: 1 declaration, **3 writes of the literal `false`, 0 reads**. This is the CS0414 the compiler already reports. | clear |
| `ItemsPile.ItemChip._heldRot` | declared 1042, assigned once (1882), **never read**. `TickHeldPose` derives rotation from the head billboard (2155–2156), not from this field. Mirrors `VRCard._heldRot`, which **is** used — check that copy before assuming symmetry. | clear |
| `ActivePileViewer.CardScale` | declaration `ActivePileViewer.cs:32`; **0 code references** — `Relayout:179` reads `CardsConfig.ActiveCardScale(board).Value` directly. Four comments reference it (`ActivePileViewer.cs:15, 29, 221`, `PlayTray.cs:689`). | clear |

Registry: *"Removable as a pair, but only as a pair — and the 'stays removed' comment must
survive."* Confirmed, and the same rule applies to `ActivePileViewer.CardScale`'s four
comments: the *value* (active cards read smaller than hand cards) is real, the *property* is
not.

**Guard expectation:** the members disappear from `GloomhavenVR.Cards/BurnCardFx`,
`GloomhavenVR.Cards/ItemsPile` and `GloomhavenVR.Cards/ActivePileViewer`. `_hasClip`'s removal
also silences the only Cards compiler warning.
**Risk if wrong:** nil for the write-only fields (no read can change). For
`SpawnConsumedPlume`: registry §12 says re-adding the plume is "a documented temptation" — the
removal must carry the comment forward.

---

## 4. Deduplication (Tier 2) — evidence attached

Both candidates below were extracted to files and `diff`ed. Every other pair I checked is
listed in §5 as **not** mergeable.

### 4.1 `PileBrowser.TryRaycast` vs `ActivePileViewer.TryRaycast` — 49 lines, one token apart

```
diff PileBrowser.cs:532-580  ActivePileViewer.cs:224-272          (comments stripped)
-        if (!IsOpen  || _root == null)
+        if (!IsShown || _root == null)
```
…and one comment word (`(enlarged cards)` vs `(smaller cards)`). **Everything else is
character-identical**: signature, out-params, `halfW`/`halfH` from `CardsConfig`, the
`denom < 1e-5f` backface reject, `dist <= 0f`, the `InverseTransformPoint` local-rect test,
the sticky early return, the `dist >= distance` compare, `return card != null`.

Registry §13 ("Browse and active raycasts use the same sticky-hover rule as the fan") requires
both to keep the sticky early-return and the backface reject — a shared helper preserves both
by construction.

**Proposal:** one `internal static bool TryPickLiveRect(IReadOnlyList<VRCard> cards, Vector3
origin, Vector3 direction, VRCard? sticky, out …)`, with the `IsOpen`/`IsShown` guard staying
at each call site (it is the only difference and it is not part of the pick).

**Guard expectation:** diff confined to `GloomhavenVR.Cards/PileBrowser`,
`GloomhavenVR.Cards/ActivePileViewer` and the new host type.
**Risk if wrong:** the browse or active highlight oscillates or stops responding. Medium-low.

**Honest counter-argument, and it is not weak.** The two layouts overlap differently — the
browse arc overlaps by `ZStagger`, the active grid because *"the lower rows sit nearer the
viewer"* (registry §13). If one of them later needs an accept margin like `CardFan`'s 1.10,
merging is what makes that change silently hit the other. This subsystem's entire lesson is
that near-identical pairs are near-identical for a reason.

**Recommendation:** merge, but only *after* §2 and §3 are done and only with the diff above
pasted into the commit message per Charter §Tier 2. If the plan is short on time, **drop this
one** and take §4.3 instead — the documentation fix captures most of the value at none of the
risk.

---

### 4.2 `CardsDriver.SubscribeBoardTuning` — 34 subscribe/unsubscribe pairs kept in sync by hand

`CardsDriver.cs:232–311`. The `if (subscribe)` branch (238–271) and the `else` branch
(275–308) are **34 lines each**. Normalising `+=`/`-=` to the same token and stripping
comments and indentation:

```
diff sub.txt unsub.txt → no output
IDENTICAL after normalising the operator, comments and indentation
```

Every config entry must appear in both lists, in a file where the lists are 34 lines long and
grow with every new per-board tuning entry. A bind added to only one list leaks a subscription
to a destroyed `CardsDriver` on every rebuild — with no compiler error and no symptom until
something throws inside a stale handler.

**Proposal:** one list, one local helper:

```csharp
void Hook(ConfigEntry<T> e, EventHandler h) { if (subscribe) e.SettingChanged += h; else e.SettingChanged -= h; }
```

(needs a small generic or non-generic overload set — `SettingChanged` is declared on the
non-generic `ConfigEntryBase`, so one non-generic helper covers all 34.)

**Guard expectation:** diff confined to `GloomhavenVR.Cards/CardsDriver`, showing
`SubscribeBoardTuning` restructured plus one new private/local method. No other type changes.
**Risk if wrong:** a tuning entry stops live-applying, or a subscription leaks. The evidence
above proves the two lists are currently in sync, so the merge cannot *introduce* an
asymmetry — it removes the possibility of one.
**Value:** removes 34 duplicated lines and a whole class of silent bug.

---

### 4.3 Three near-duplicate pairs that must **not** be merged, and need a comment saying so

These came out of the same diff sweep and are the more valuable half of it. See §5.

---

## 5. Documentation defects — the highest value-per-risk work in this review

The registry's premise is that stale-but-plausible documentation is this codebase's worst
hazard. The sweep found twelve instances in Cards. Comments do not survive into IL, so **every
fix below has a guaranteed empty guard diff and zero behavioural risk.**

Ordered by how likely the wrong comment is to cause a regression.

### 5.1 `CardsDriver.BlockCardInteractions` cites the exact predicate the fix removed

`CardsDriver.cs:2321–2328`:
> `/// Menu-open gate (see <see cref="Update"/>): while a modal window floats`
> `/// (<see cref="WorldUI.ModalFallback.WindowModalActive"/>) force EVERY card …`

The caller, 1 280 lines earlier, spends twelve lines explaining that `WindowModalActive` *was
the bug*, "twice over", and gates on `BlockingWindowModalActive` (`CardsDriver.cs:1047`).
Registry §11 makes it an invariant: *"Card input blocks on `BlockingWindowModalActive`, not
`WindowModalActive` … Breaks if: Reverting to the broader predicate 'to be safe'."*

A reader who opens `BlockCardInteractions` in isolation — which is exactly what a reader of a
5 483-line file does — finds a doc comment telling them the broader predicate is correct.
**Fix:** change the cref and add one sentence pointing at the caller's rationale.

### 5.2 `CardsDriver.UpdatePalmGate` documents roll gate **v3**, which v4 replaced

`CardsDriver.cs:1289–1294`:
> `// Live-tunable gate feel (roll gate v3): PalmGate measures Demeo's own roll dot —`
> `// the hand's RIGHT axis tilt toward world up — asin-mapped to DEGREES …`

`Hands/Interact/PalmGate.cs:11–16` (the class this comment describes):
> `ROLL MEASURE (v4 …): the v3 Demeo measure (dot(side · handRight, worldUp)) is pitch-proof`
> `only while the fingers stay [horizontal] … never open (user requirement …)`

The Cards-side comment describes the measure that **failed on hardware** as if it were what
runs. Registry §5 rates this v4 entry `high` confidence and lists three superseded generations.
**Fix:** replace with a one-line pointer to `PalmGate`'s class doc — do not restate the
measure here; restating it is how it went stale.

Two lines below, `gate.UseDevicePalmNormal = !_gateHand.IsSimulated;` carries the comment
`// sim hands pose the rig directly`. `PalmGate.cs:96–101` says the flag is *"VESTIGIAL since
roll gate v4 (kept so the Cards driver's per-frame assignment stays [valid])"*. So this is a
deliberately-retained no-op whose Cards-side comment does not say so. **Fix:** say so. Do not
remove the assignment — `PalmGate` explicitly keeps the field for it (cross-subsystem; the
registry hands the field itself to the Hands review).

### 5.3 `PileBrowser.TryRaycast`'s doc invites the merge invariant §1 forbids

`PileBrowser.cs:527–530`:
> `/// … the browse counterpart of <see cref="CardFan.TryRaycast"/>. Same`
> `/// per-card plane+rect test, scale-aware …, same sticky-hover hysteresis`

It is **not** the same test. Diffed:

```
CardFan.TryRaycast                          PileBrowser.TryRaycast
  const float acceptMargin = 1.10f;           (none)
  c.TryGetRestingLaserRect(out center, …)     Transform t = c.transform;
  Dot(direction, normal)                      Dot(direction, t.forward)
  Dot(center - origin, normal) / denom        Dot(t.position - origin, t.forward) / denom
  Dot(rel, rectRight) > halfW * acceptMargin  InverseTransformPoint(hit).x > halfW
```

The fan uses the **resting** rect plus a 10 % accept margin; the browser uses the **live**
transform with exact half-extents. Registry §1 is explicit: *"Breaks if: 'Simplifying' either
raycaster to `transform.position` / `transform.forward` / `InverseTransformPoint`"* — which is
literally what the browser does, correctly, because it has no laser-driven pop feedback loop.

A reader who trusts "Same per-card plane+rect test" concludes the two can be unified. That
merge reintroduces the "invisible collider above the card" bug (`306e8ea`).
**Fix:** state the difference and why the browser is allowed to use the live transform.

### 5.4 `ItemsPile`'s consumed-plume prose describes code that was deleted

Five separate comments assert a plume that does not exist (see §3.5):

| Where | Claim | Reality |
|---|---|---|
| `ItemsPile.cs:44–45` (class doc) | `CONSUMED → ashen + the game's burn plume (<see cref="BurnCardFx"/>)` | all three `BurnCardFx` mentions in the file are comments; there is no code path |
| `ItemsPile.cs:936` (`ItemChip` doc) | `Consumed items carry the burn plume` | same |
| `ItemsPile.cs:966` | `_plume; // consumed-item smoke, destroyed with the chip` | never assigned |
| `ItemsPile.cs:1149–1152` | `the mod-owned pieces (backing above, fallback face + plume below)` | nothing below is a plume |
| `BurnCardFx.cs:124–133` | full XML doc for `SpawnConsumedPlume` in the present tense | no caller |

Registry §12 states the current law: *"Item chips do not spawn the separate consumed plume …
the consumed look comes only from the hosted card's own clamped `ItemCardEffects`"*, and warns
that re-adding it is a documented temptation. These five comments are that temptation, in the
source, in the imperative.

### 5.5 `ItemsPile._anchor` documents the reverted position

Three comments (`ItemsPile.cs:92`, `:143`, `:425–426`) say `_anchor` is *"the pile mount"*.
`PileViewer.cs:154` passes the **items stack transform**, with `mount` only as a null fallback
— and registry §12 makes that a named invariant (*"The item fan converges on the ITEMS stack …
Breaks if: 'Simplifying' to `mount`, which is only the null fallback"*). The comments state
exactly the position the fix reverted. `:92` additionally calls it a "placement scale ref",
which it is not (read at 195 as a fallback parent, at 428/458/913 as the converge point).

### 5.6 `PileViewer`'s docs say "two piles"; the code has three

| Where | Says | Code |
|---|---|---|
| `PileViewer.cs:13–14, 18–19` (class doc) | "two small physical card piles" | `EnsureBuilt` builds `_discard`, `_burnt` **and** `_items` (129/136/146) |
| `:170` | "Re-read **both** pile captions" | body refreshes three |
| `:179–181` | "discard upper at +spacing/2, burn lower at −spacing/2" | three stacks; items at `−spacing*1.5` (201) undocumented |
| `:244–246` | "cheap — **two** list Counts" | three counts, plus `_itemsBrowse.Tick(hand)` (275, which runs the whole item-fan tick including `UpdateHandSweep`) and `TickItemsUsableHighlight` (276, which scans the inventory) |
| `:103` | "real **game** loc keys" | `PileKind.Items` → `Core.Loc.Mod("items")`, a mod string |

`:244–246` is the one that matters: it advertises a per-frame cost that is off by an order of
magnitude, which is exactly the kind of claim a future perf pass would trust.

### 5.7 `ItemChip.TryFingertipDistance`'s summary contradicts its return value

`ItemsPile.cs:2067`:
> `/// <summary>True while the dominant index tip / palm is within this chip's collider reach.</summary>`

It returns `true` for **any live collider at any distance**; the reach test is in the caller
(`UpdateHandSweep:599–603`). `VRCard.cs:1646–1651`'s equivalent doc is correct and is the model
to copy. Also see the naming finding in §5.11.

### 5.8 `PlayTray` — a doc comment attached to the wrong method

`PlayTray.cs:507–519` contains **two consecutive `<summary>` blocks**. The first
("Test #15 dashboard: pose anchors for the game's REAL converted UI …") describes
`BuildMounts`; the second describes `BuildRoundReadout`, which is the method that follows.
`BuildMounts` itself (line 570) has **no** doc comment. The compiler silently keeps only the
last block. **Fix:** move the first block to line 570.

### 5.9 `PlayTray.cs:3052–3057` — "AddCaption stays live … so nothing goes unused"

True only via the dead pick field (§3.3). If the pick field goes, so does `AddCaption` and this
comment.

### 5.10 `PlayTray.cs:3073–3087` — one comment block that contradicts itself

The first paragraph says the generic-button area "AUTO-SCALES each cap's size from the live
count (`GenericClusterButtonSize`)"; the second, six lines later, says it does not and explains
why the auto-scale was removed. The stale paragraph should go with §3.4's removal.

### 5.11 Names that assert something the code does not do

| Name | Where | The mismatch |
|---|---|---|
| `ItemChip.TryFingertipDistance` | `ItemsPile.cs:2068` | not a fingertip test (called with the **palm** at 600) and the `Try` prefix implies the reach condition, which lives in the caller |
| `ItemChip._plume` | `:966` | names a `GameObject` that is never created |
| `ItemChip._hasClip` | `:1070` | reads as "is this chip clipped in"; never read, only ever set `false`. Real state is `PendingUse` + parent |
| `ClampCardEffectSmoke` / `RestoreCardEffectSmoke` / `SmokeClamp` / `SmokeCardSpan` | `:1441–1603` | every name says "smoke", but the scan is `GetComponentsInChildren<ParticleSystem>(includeInactive: true)` — **all** emitters, deliberately so (registry §12 makes the plural + inactive-inclusive scan an invariant). The name and doc together hide the very property the invariant protects |
| `ItemsPile.SetAnchor` / `_anchor` | `:144` / `:92` | dominant use is the emerge/collapse **converge point**, not a parent |
| `ItemChip.OnPoke/OnPokeEnter/OnPokeExit` | `:2163–2181` | a **laser-only** surface — `ItemChip` is registered via `RegisterLaserTarget` (1162) and never via `VRInteractables.RegisterPokeable`, unlike `PileStack` (`PileViewer.cs:665–671`). Registry §13 protects the fact that `ItemChip.OnPokeExit` and `PileStack.OnPokeExit` have **opposite** semantics; the identical names conceal it |
| `ActivePileViewer.CardScale` | `:32` | presents itself as the scale source of truth; `Relayout` never reads it (§3.5) |
| `PileViewer.SetVisible` | `:205` | also force-closes the item browse (214) — a lifecycle action, invariant-protected. The behaviour is right, the name is not |
| `ItemsPile.ReleaseHeld(VRHand vrHand)` / `TogglePoke(…, VRHand vrHand)` | `:178` / `:164` | the signature promises per-hand semantics; the parameter is unused |

**All of §5 is Tier 0, guard diff empty, and can be one commit per file.**

---

## 6. What should NOT be touched

A finding of "leave it" is a result. These are the cases where the obvious refactor is the
regression.

### 6.1 `CardsDriver.UpdateBoardLaser` (214 lines) — do not split

About 90 of those 214 lines are one explanatory comment block (1966–2011) that reconstructs
three hardware rounds of debugging. The code itself is ~120 lines and has a **three-phase
order that is the fix**: pre-empt only on a real hit → normal element scan → suppress-only
fallback at the bail-out. Registry §2 has four entries on this method, including the two most
dangerous edits in the subsystem (`SuppressFarClick` is not `UiHitOverride`; "Hoisting the
fallback back above the scan **for readability**"). Splitting it separates each phase from the
paragraph explaining why it sits where it does. **Leave as one method, in one place.**

### 6.2 The four `Clear*Hover` methods — do not dedupe

`ClearLaserHover`, `ClearTrayCardHover`, `ClearBrowseHover`, `ClearActiveHover` are four
**identical** 6-line bodies over four different fields. Deduping needs `ref VRCard?`
parameters, which changes the compiled form of all four plus adds a method, to save 18 lines.
The four fields participate in a documented precedence chain (fan > tray/board > browse >
active) that is easier to audit when each has its own named clear. **Leave.**

### 6.3 `CardFan.TryRaycast` vs `PileBrowser.TryRaycast` — do not merge (fix the doc instead)

Diffed in §5.3. Resting rect + 1.10 margin vs live transform + exact extents. Registry §1
names the merge as the reintroduction of `306e8ea`. **This is the registry's "looks redundant,
is not" case, confirmed by diff.**

### 6.4 `VRCard.TryGetRestingLaserRect` vs `TryGetLiveLaserRect` — do not unify

Registry §1, two entries, both `high` confidence: *"They are near-identical by design and
their difference is the whole point."* Not merged, not renamed, not given a shared body.

### 6.5 `CardsDriver.UpdateBrowseLaser` vs `UpdateActiveLaser` — do not merge

Diffed. They are **not** identical:

```
-        if (!_browser.IsOpen || … || _laserHover != null || _trayCardHover != null || _boardHover != null)
+        if (!_active.IsShown  || … || _laserHover != null || _trayCardHover != null || _boardHover != null
+                                    || _browseHover != null)                 ← active yields to browse
…
-            if (dom.TriggerDown) ForeignInteraction("click-away (trigger off the pile)");   ← browse only
```

The extra guard term is the precedence chain (active is deliberately lowest) and the
click-away dismiss exists only for the browse. Merging needs a flag parameter that
re-implements both differences. **Leave; the doc comments already state the precedence
correctly.**

### 6.6 `PlayTray.BoardButton` dwell machinery — do not remove **and** do not re-enable

`DwellSeconds` is `0f`, no caller sets it, `DwellHoldRange`/`_dwellHand`/`TickDwell`/
`BeginDwell`/`CancelDwell`/`DwellChargeColor` are all reachable only through it. This looks
like ~90 lines of dead code and is **not**: `239acb5` removed the dwell *by user directive*
while explicitly keeping the `ActivationGuard` accident window. Registry §7 flags it in both
directions. **Leave, and add one line at `DwellSeconds` saying it is configured-off by user
directive, not vestigial** — the doc there says "Machinery kept for a possible config", which
does not convey that re-enabling it reverses a decision.

### 6.7 `CardsConfig`'s eight unread binds — already handled, do not duplicate

`CENSUS.md` covers `HeldTiltDegrees`, `RoundButtonDiameter`, `RestButtonInsetX`,
`ConfirmUndoInsetX`, `InspectForward`, `InspectUp`, `RevealPreset`, `RevealDemeo` (+
`FanArcDegrees`) and recommends re-labelling rather than unbinding, because unbinding drops
the key from the user's `.cfg`. **Nothing to add; do not re-open it in the Cards plan.**
The same applies to `Cards.PileKind`'s member order being a wire constant.

---

## 7. Deferred — would need real behaviour changes (Tier 3, user decision)

Left in place, written down so they are not lost.

### 7.1 `ItemsPile.PlaceAboveBoard` seats the fan without the item offset

`PlaceAboveBoard:503` writes `BoardAnchorBase + BrowseFanOffset`; `Tick:354` writes
`BoardAnchorBase + BrowseFanOffset + ItemFanOffset` every frame thereafter. The open frame is
therefore seated at the wrong spot and jumps on the next tick. Registry §12 makes
`ItemFanOffset` deliberately separate from `BrowseFanOffset`, so this is a one-frame visual
defect, not a design question — but fixing it changes what the player sees on the open frame.
**Not part of this refactor.**

### 7.2 `CardsDriver.Rebuild`'s `switch (mode)` (137 lines) cannot be extracted verbatim

It assigns three locals (`trayVisible`, `halfVisible`, `grabbable`) read by the code after it.
Extraction needs `out` parameters or a return struct — a real code change. Recommended only if
the user wants `Rebuild` under 200 lines; §2.5's two verbatim extractions already take it from
431 to ~290.

### 7.3 `PlayTray.BoardButton.Create` (264 lines) cannot be extracted verbatim

Its three cap-shape branches (round 50, boxy 44, native/procedural 31 lines) each produce seven
values (`capMaterial`, `capBevelMaterial`, `capWallMaterial`, `capFace`, `capMeshRenderer`,
`capFrontZ`, `labelZ`) that the code below consumes. Extraction means seven `out` parameters,
and `capFrontZ` is invariant-critical (registry §7: "Keycap trigger collider spans the whole
visible cap" — the box is sized from it). **Not now.** §2.2 already moves the whole class out
of `PlayTray.cs`, which is the readability win that matters.

### 7.4 MP consistency question raised by §3.3

`Net/RemoteBoardFurniture` builds and shows a pick field that the local `PlayTray` never
shows. Either the remote is right and the local one regressed, or the remote should stop.
**A question for the user, not a refactor.**

---

## 8. Measurements

### File sizes and comment density

| File | Lines | Comment | % | Code |
|---|---:|---:|---:|---:|
| `CardsDriver.cs` | 5 483 | 1 631 | 29 | 3 476 |
| `PlayTray.cs` | 4 613 | 1 727 | 37 | 2 504 |
| `ItemsPile.cs` | 2 252 | 679 | 30 | 1 380 |
| `VRCard.cs` | 2 004 | 718 | 35 | 1 135 |
| `CardFan.cs` | 1 719 | 716 | 41 | 848 |
| `CardsGameApi.cs` | 1 684 | 827 | 49 | 752 |
| `CardsConfig.cs` | 1 048 | | | |
| 15 remaining files | 3 152 | | | |
| **total** | **23 955** | | | |

The comment share is the reason "just split it" is the right prescription: at 29–49 %, the
files are long because the *reasons* are written down, and the registry proves those reasons
are load-bearing. Deleting them is the one thing that must not happen.

### Longest methods

| Method | Lines | Length | Verdict |
|---|---|---:|---|
| `CardsDriver.Rebuild` | 2683–3113 | 431 | two verbatim extractions → ~290 (§2.5) |
| `CardsDriver.OnCardReleased` | 3603–3891 | 289 | see §7 note below |
| `PlayTray.BoardButton.Create` | 3899–4162 | 264 | do not extract (§7.3) |
| `CardsDriver.UpdateBoardLaser` | 1956–2169 | 214 | **do not touch** (§6.1) |
| `PlayTray.TickStatus` | 2654–2806 | 153 | extractable; low priority |
| `CardsDriver.UpdateBody` | 878–1025 | 148 | leave — it is the frame-order contract (registry §9) |
| `PlayTray.EnsureBuilt` | 374–505 | 132 | already delegates to 10 `Build*`; leave |
| `PlayTray.BuildMounts` | 570–697 | 128 | ~80 % comment (collision budgets); leave |
| `CardsDriver.ApplyBoardTuning` | 332–455 | 124 | 12 near-identical `if (_applyX)` blocks; see below |
| `CardsDriver.UpdateSlotHighlight` | 2358–2458 | 101 | leave — registry §8 ties both branches together |

`OnCardReleased`'s five drop-outcome branches (fan→empty slot, fan→occupied swap, tray→tray
reorder, tray→fan take-back, fan-gap commit/void) are each verbatim-movable with an explicit
7-parameter list. That is Tier 1 by the charter's definition, but the parameter list is wide
enough that a mistyped argument is a real risk, and registry §8 has six entries on this method.
**Recommended: do it only after §2.3 lands and only if the user asks for it.**

`ApplyBoardTuning`'s twelve `if (_applyX) { _applyX = false; …; VRLog.Info(…); }` blocks are
structurally identical but each calls a different setter with different config entries and logs
a different message. Not deduplicable without a table-driven rewrite (Tier 3). **Leave.**

### Verified-identical duplication found (for the record)

| Pair | Result |
|---|---|
| `ItemsPile.PlaceAtHead` (509–530) vs `PileBrowser.PlaceAtHead` (293–314) | **byte-for-byte identical**, 22 lines incl. comments |
| `PileBrowser.TryRaycast` vs `ActivePileViewer.TryRaycast` | identical but `IsOpen`/`IsShown` (§4.1) |
| `CardsDriver.SubscribeBoardTuning` sub vs unsub | identical modulo `+=`/`-=` (§4.2) |
| `ContactTipReach` / `ContactPalmReach` / `ContactStickyMargin` | same values in `ItemsPile`, `PileBrowser`, `CardsDriver` (0.035 / 0.13 / 0.02) |
| `ZStagger = 0.004f` | 7 identical declarations; the 3 in `Net/` are deliberately decoupled wire mirrors and each says so |
| `BoardFloatHeight` / `BoardFloatProudZ` / `BoardAnchorBase` | identical in `ItemsPile` and `PileBrowser` |
| billboard math, `ItemsPile.FaceHead` vs `PileBrowser.Tick` | token-identical; comment wording and one indent differ |
| `PileViewer` stack positions | computed in `EnsureBuilt` (131/138/146), passed as `localPos`, then **overwritten by `ApplyLayout()` two lines later** (191/196/201) — the parameter is dead on arrival |
| `PileBrowser` suppression-clear block | byte-identical 6 lines at 420–425 and 516–521 |

The shared-constant cases (`ContactTipReach`, `ZStagger`, `BoardFloat*`) are **deliberate**:
each copy carries a comment naming its source, and the `Net/` copies are decoupled on purpose
so a wire-side change cannot follow a local tuning change. **Do not centralise them.** The
`ItemsPile.PlaceAtHead` / `PileBrowser.PlaceAtHead` pair is genuine duplication with no
documented reason to differ — a real but small Tier-2 candidate, ranked below §4.1 and §4.2.
