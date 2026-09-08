# Subsystem review — `src/GloomhavenVR/Cards/`

> **[verified 2026-09-08 against `49ceab21` (ModBuild 483)] "Nothing here has been applied" is
> no longer true.** This is the 2026-07 Phase-1 review; **`PLAN.md` selected from it and `LOG.md`
> records what was executed** (batches A–F), and two further programmes have run since
> (`PLAN-2026-08.md` / `LOG-2026-08.md`, then `.planning/refactor-2026-09/`). Read this file as a
> **reading of the code at that date**, never as a work list — a proposal here may have been done,
> rejected with a recorded reason, or superseded. `LOG.md`'s "Claims that did NOT reproduce" and
> "Open items" sections are where the verdicts are.
>
> The line/file counts in the header below are 2026-07 measurements; the tree is **621 files /
> 551 166 lines** at `49ceab21`. This audit did **not** re-derive the findings themselves — it
> checked the framing. An individual finding in here is unverified against current source.

> Phase 1 output. 22 files, 23 955 lines. Companion to `INVARIANTS-Cards.md` (239 entries),
> which is the constraint set every proposal below was checked against.
>
> **Nothing here has been changed.** No source file was touched, nothing was committed.
> Build verified green at HEAD (`3ee9397`): 0 errors, 4 warnings, 1 of them in `Cards/`.
>
> Findings are ordered by **value ÷ risk**, highest first. Every one states its tier, the
> exact files, what moves, the expected `scripts/refactor-guard.sh` output, and the risk if
> the analysis is wrong.

---

## 0. Verdict up front

This is not badly written code. It is **well-commented code in files that grew past the point
where the comments can be navigated**. 29–49 % of every large file here is comment, and almost
all of it is load-bearing — `INVARIANTS-Cards.md` proves that for 239 separate places. The
maintenance problem is therefore *not* "this logic is tangled". It is "a 5 483-line file is a
5 483-line file", plus a second problem the registry predicted and this review confirms:

**Some of that comment mass is now wrong, and it is wrong in the specific direction that
invites the regression.** Twenty-two verified instances, §5. Three of them tell a reader to
make the exact edit an invariant forbids.

That points the review at two classes of change: **move code without touching it**, and **fix
documentation that lies**. Concretely:

| | |
|---|---|
| Recommended now | 3 provably-zero-diff moves, 3 pure-motion splits (Tier 1), 8 dead-code removals (Tier 0), 22 documentation corrections (Tier 0) |
| Recommended with evidence attached | 3 deduplications (Tier 2) |
| Explicitly recommended **against** | 9 items in §6 — including four that look like obvious wins |
| Deferred to the user | 6 items in §7 |

Two findings are worth more than any single refactor below:

- **§1** shows a claim in `CHARTER.md` about the guard is wrong, and the correction determines
  which splits are achievable at all.
- **§5.0** corrects seven errors in `INVARIANTS-Cards.md` and `CENSUS.md` itself. Four config
  entries those documents recommend a decision about **do not exist**; two invariants point
  their `Where:` at dead code; one names consumers that were reverted away.

---

## 1. PREREQUISITE — the guard does not behave the way the charter says

`CHARTER.md` §3 states:

| Refactor kind | Expected guard output |
|---|---|
| Move a type to another file, split a file, **reorder members** | **empty diff** |

**The "reorder members" half of that row is false**, and it governs every split below.

### Evidence

`ilspycmd -p` writes one file per **top-level type** and emits members in **metadata order**,
which is source order. Comparing the stored baseline against the source:

```
.guard/baseline/GloomhavenVR.Cards/CardsDriver.cs, methods in order:
  OnEnable OnDisable SubscribeBoardTuning OnControlOffsetChanged … ClearBrowseHover
src/GloomhavenVR/Cards/CardsDriver.cs, methods in order:
  OnEnable OnDisable SubscribeBoardTuning OnControlOffsetChanged … ClearBrowseHover
```

Identical sequences, for all 60 members checked; fields likewise. **Reordering members
produces a non-empty guard diff** — a permutation in which every moved body appears once as
`-` and once as `+`.

### The consequence for partial-class splits

`GloomhavenVR.csproj` uses the SDK's default `**/*.cs` glob (no explicit `<Compile Include>`
except the generated `BuildInfo.g.cs`), so compile order follows the glob enumeration — in
practice ordinal filename order per directory. Metadata order is the concatenation of each
file's members in compile order.

A partial-class split is therefore empty-diff **only if** (a) each new file holds a
**contiguous** run of the original members in original order, **and** (b) the files **compile
in the same order as those runs**. Condition (b) is filename-sensitive in a way that is easy to
get wrong: `CardsDriver.Laser.cs` sorts *before* `CardsDriver.cs` (`'L'`=76 < `'c'`=99), so the
obvious naming inverts the order and yields a whole-file permutation.

### Two exceptions that are genuinely free

**(i) A top-level type that shares a source file already has its own decompiled file.**
Moving it to its own source file changes *nothing* — no partial keyword, no ordering argument,
no risk. The baseline proves it: `GloomhavenVR.Cards/CardDustFx.cs` exists as a separate
snapshot file although `CardDustFx` lives inside `VRCard.cs`. See §2.0.

**(ii) Nested types are hoisted.** ILSpy emits every nested type at the top of the parent's
file, ahead of the parent's own members, in their mutual metadata order:

```
.guard/baseline/GloomhavenVR.Cards/PlayTray.cs
   15  internal sealed class PlayTray : IPanelGrabOwner
   17      internal readonly struct LaserTarget          (source 336)
   30      private sealed class SlotPulse               (source 2398)
   58      private sealed class BoardSurfaceTarget      (source 3619)
   73      internal sealed class BoardButton            (source 3631)
  785      private const float SlotCaptureRadius        ← first ordinary member
```

So moving nested types to their own file is empty-diff **provided their mutual order is
preserved** — which is guaranteed if they all move together into one file.

### Recommendation

- Amend `CHARTER.md` §3 with the two conditions and the two exceptions.
- **Do §2.0 first as a calibration commit.** Its predicted output is *exactly empty* on the
  strongest possible grounds (exception (i) — no ordering argument at all). If the guard prints
  anything, the model above is wrong and every later split must be re-planned. One trivial
  commit buys certainty for the other eleven.
- For §2.2–§2.4, pin compile order with explicit `<Compile>` items in the csproj rather than
  trusting the filename sort. That is a build-file edit, not a source edit.

**Tier:** 0 (documentation) plus a build-file change.
**Risk if wrong:** none — this finding only changes what output we *expect*. Getting it wrong
the other way (expecting empty, seeing a 3 000-line permutation) looks like collateral damage
and could scare a correct refactor into being reverted.

---

## 2. Pure motion (Tier 1)

### 2.0 Give the six stowaway top-level types their own files — provably zero-diff

Three source files each hold several top-level types. Each of those types **already has its own
file in the guard snapshot**, so relocating it cannot change the snapshot at all.

| Source file | Stowaway type | Lines | Move to |
|---|---|---|---|
| `VRCard.cs` (2 004) | `CardDustFx` (static) | 1885–2004 (**110**) | `CardDustFx.cs` |
| `PileBrowser.cs` (581) | `PileKind` (enum) | 10–35 (**26**) | `PileKind.cs` |
| `CardsConfig.cs` (1 048) | `CardGrabButton`, `ControlBoard`, `ControlBoards`, `ButtonShape` | 9–77 (**~70**) | `CardsEnums.cs` (or one file each) |

**Guard expectation:** **empty**, on the strongest available grounds. No `partial`, no compile
ordering, no member reordering — the decompiler already treats these as separate units.

**Risk if wrong:** nil. This is the calibration commit.

**Value beyond the line count:** `PileKind` is the subsystem's most dangerous refactor trap.
`CENSUS.md` records that its **member order is a wire constant** — `NetAvatarDriver.TickExtrasSend`
casts it straight into the extras payload — and that the constraint is documented on the *Net*
side while "nothing at the enum itself says so". Alphabetising it or inserting a fourth pile
would corrupt every peer with no compiler error and no single-player symptom. Giving it its own
file is where that comment finally has a home. **Do the move and the comment in one commit.**

---

### 2.1 Move `PlayTray`'s four nested types into one file — biggest safe win in the subsystem

**Files:** `Cards/PlayTray.cs` (4 613) → new `Cards/PlayTrayParts.cs`
**What moves, in this exact order** (preserving their mutual order is what makes it free):

| Type | Current lines | Size |
|---|---|---|
| `LaserTarget` (readonly struct) | 336–346 | 11 |
| `SlotPulse` | 2397–2426 | 30 |
| `BoardSurfaceTarget` | 3611–3624 | 14 |
| `BoardButton` | 3626–4612 | **987** |

≈ **1 042 lines**, leaving `PlayTray.cs` at ≈ 3 570. `PlayTray` becomes `partial`.

`BoardButton` reaches back into `PlayTray`'s private statics (`Tint`, `NewKeycapMaterial`,
`BoxCapShader`, `OverlayMaterial`, `SquareCapBevel`, `CapRestZ`) — all still accessible, since
it stays a nested type of the same partial class.

**Guard expectation:** **empty**, per §1 exception (ii). Moving `BoardButton` *alone* is
filename-order-dependent and would permute if its file compiled first — move all four.

**Risk if wrong:** the guard prints a permutation of four nested type bodies inside
`GloomhavenVR.Cards/PlayTray.cs` and nothing else. Recognisable at a glance; revert is a
`git checkout`.

**Why first among the splits:** `BoardButton` is a genuinely separate concern — a pressable 3D
keycap with its own palette, dwell machinery, depth-fire press, debounce and dissolve/materialize
animation. It shares nothing with the board's placement, slot or watchdog logic beyond four
material helpers. "These are two different things" is uncontroversial here, which is not true of
every split below.

---

### 2.2 Split `CardsDriver.cs` (5 483) along its own existing section dividers

The file already carries **32 hand-written dividers**, and they are not decorative: fields are
declared inside the region that uses them (`_laserHover`/`_laserHoverGraceUntil` under
`fan laser`; `ContactTipReach`/`ContactPalmReach`/`ContactStickyMargin`/`_handContactWinner`/
`_contactSuppressed` under `hand-contact single winner`; `_watchRoot`…`_anchorLossLogged` under
`board pose guard`). The regions are already cohesive; only the file is not split.

**Cut points — every one an existing divider, every run contiguous and in order:**

| Part | Source lines | Regions | Size |
|---|---|---|---|
| 1 (kept) | 1–456 | header, all shared fields, `lifecycle`, `board tuning (Part F)` | 456 |
| 2 | 457–1467 | `board pose guard`, `handlers`, `update`, `tick attribution guard`, `fan diagnostics`, `card audio` | 1 011 |
| 3 | 1469–2484 | `fan laser`, `fan hover split`, `hand-contact arbitration`, `board laser`, `browse laser`, `active laser`, `modal input-block`, `slot snap preview` | 1 016 |
| 4 | 2485–3558 | `hand fan reorder`, `rebuild`, `fly-to-pile`, `MP card-FX anchors`, `BurnSlab`, `HookCard` | 1 074 |
| 5 | 3559–4450 | `interactions`, `pick flows`, `short rest` | 892 |
| 6 | 4451–5483 | `overlay gate`, `wanted-slot hint`, `initiative to-do`, `long-rest tracing`, `take-damage selection`, `long-rest turn pump`, `pile browse`, `active cards`, `dev fake hand` | 1 033 |

Whole regions, byte-identical, in order. No member edited, renamed or reordered within a region.

**Guard expectation:** **empty** under the §1 conditions — the six files must compile 1…6, pinned
in the csproj. If the order is wrong the diff is large but *pure permutation*, confined to
`GloomhavenVR.Cards/CardsDriver.cs`, with no textual change inside any member body.

**Risk if wrong:** the failure mode above is scary-looking but harmless. The genuine risk is
different and worth naming: **a split invites future edits to drift between regions coupled by
call order.** The registry names one such coupling that now spans a cut:

> `TickInteractionsAndStatus` (part 2) fixes the order `laser paths → UpdateHandContactArbitration`
> (part 3). Registry §3: *"Arbitration runs after the laser paths … Breaks if: Reordering the
> tick calls 'since they are all independent'."*

The other two order-critical pairs stay inside one part (`Rebuild` before `TickBurnToPile`, both
part 4; `TickBoardPoseWatch` last in `Update`, both part 2).

**Make this a condition of the split:** each new file gets a header naming the regions it holds
and the cross-file ordering constraints its members participate in. That is a net documentation
gain, not a cost.

---

### 2.3 Split the remainder of `PlayTray.cs` (≈ 3 570 after §2.1)

Same argument, same mechanism, 16 existing dividers. Lower priority — §2.1 already removes 23 %.

| Part | Source lines | Regions | Size |
|---|---|---|---|
| 1 (kept) | 1–1164 | class doc, fields/consts, mount accessors, `laser targets`, `lifecycle` (`EnsureBuilt` + `Build*`), `grab handle`, `dashboard controls`, `ApplyFollowMode`, `Destroy`, `SetVisible`/`TickPlacement`/`PlaceAtHead`/`ReassertPlacement` | 1 164 |
| 2 | 1165–1450 | `LOST-BOARD WATCHDOG` (incl. `SyncPinHolder`, `IsInHeadView`, `IsFinite`) | 286 |
| 3 | 1451–1895 | `debug-menu live apply`, fixed base positions, `ReapplyOrientation`, rebuild helpers, `board-switch pose`, `PersistPoseToConfig` | 445 |
| 4 | 1896–2637 | `slots`, `pick field`, `highlight`, `item-use slot` | 742 |
| 5 | 2638–2807 | `status` (`TickStatus`) | 170 |
| 6 | 2808–3570 | `build` helpers, shaders, materials, `MeasureBoardLocalExtents`, board diagnostics | 763 |

Part 2 is the standout: 286 self-contained lines carrying the subsystem's most expensive lesson
(registry §6 spends 8 entries on it, including *"**the comment block with no code under it IS
the invariant**"*). Its own file makes that block impossible to mistake for cruft.

**Guard expectation / risk:** as §2.2.

---

### 2.4 Verbatim method extractions — four candidates, only two recommended now

Charter §4 admits "extracting a method whose body is moved verbatim" as Tier 1. Four qualify:

| Method | Lines | Extract | Size | Recommend |
|---|---|---|---|---|
| `CardsDriver.Rebuild` (431) | 2683–3113 | zone-flag sweep → `RebuildZoneSweep(CardsHandUI hand, bool grabbable)` | 109 | **yes** |
| | | the two dock-appear loops → `PlayDockAppearAnimations(bool halfVisible)` | 38 | **yes** |
| `VRCard.UpdateBody` (168) | 1689–1856 | a strict early-return chain of 5 disjoint phases: maintenance 1691–1701, held 1703–1714, flying 1718–1750, vanishing 1755–1773, appearing 1778–1795, home-lerp tail 1797–1855. Each block ends in `return;` and touches disjoint fields → `TickFly()/TickVanish()/TickAppear()` returning `bool handled` | ~120 | **yes**, after §2.1 |
| `CardMesh.Build` (122) | 107–228 | four non-interleaved phases: outline 109–132, vertex/normal/UV 134–183, index buffers 185–217, mesh assembly 219–227. No variable crosses a boundary except the arrays and `n`/`*Base` | ~110 | **yes** |
| `CardFan.Relayout` (212) | 1296–1507 | insert-overlay placement 1443–1462 → `PlaceInsertOverlay(...)`; throttled diagnostic 1464–1506 → `LogDepthCurve(...)`. Both are tails that read locals and produce nothing | 63 | later |
| `CardFan.Tick` (171) | 441–611 | live-tuning signature 478–509 and the follow branches 530–574 lift cleanly; the billboard block 576–603 contains two bail-out `return`s and is **not** byte-verbatim | 77 | later |
| `CardsConfig.Bind` (587) | 402–988 | ten contiguous blocks, each already divider-marked. **Two ordering constraints**: the roll-gate v3 migration (442–454) must stay immediately after the 419/434 binds, and the BoardScale migration (746–771) must follow the per-board `foreach` (596–744) because it reads `_boardScale[]` | 587 | later |

`Rebuild` drops 431 → ~290; `VRCard.UpdateBody` 168 → ~50 plus three named phases.

**Guard expectation:** diff **confined to the one type**, showing the new private methods and
their bodies removed from the caller. The compiler may inline them; if not, the diff is exactly
the new methods plus the call sites.

**Risk if wrong:** medium-low and specific. `RebuildZoneSweep` is dense with invariants
(registry §9: the strict `else if` park chain, the `IsFlying || IsVanishing` continue, the
"not parked" pose predicate, the `PokeSelectEnabled = false` re-assert). None is *about* where
the code lives; all are about statement order inside the run, which a verbatim move preserves by
construction. The one real hazard: the sweep reads `grabbable`, assigned by the `switch (mode)`
above. Passing the wrong parameter silently disables card grabbing. **Review that one line.**

---

## 3. Dead code (Tier 0)

Every entry verified by full-repo `grep` over `src/**/*.cs` (excluding `obj/` and the
`.claude/worktrees/` copies) and checked against Charter §5: Harmony surface, Unity messages,
reflection, config keys, log grep tokens, debug menu. `SettingsPanel.cs` — the in-VR debug menu
— was checked directly: it touches `CardsConfig` 110 times, **all by direct static member
access, never by string key**, with no `ConfigDefinition` lookup and no `_file` enumeration. So
§5's debug-menu protection can be resolved by name, and was, for every candidate.

### 3.1 `VRCard`'s render-on-top machinery — reverted, and the registry still documents it as law

```
SetRenderOnTop(bool on)   VRCard.cs:510   → body is `_ = on; RestoreRenderOnTop();`   5 call sites
ApplyRenderOnTop()        VRCard.cs:523   → 0 call sites (70 lines)
_renderOnTop              VRCard.cs:120   → set true ONLY at :583, inside ApplyRenderOnTop
```

Because `_renderOnTop` can never become true: `RestoreRenderOnTop`'s guard (`:596`) always
returns, so its 40-line body is unreachable; `UpdateBody`'s branch `if (_renderOnTop && NeedsFace)`
(`:1696`) can never fire; and all five `SetRenderOnTop` call sites (`:390, :400, :496, :1697`)
are no-ops. Dead by consequence: `_renderOnTopLogged`, `_backingRenderers`, `_backingOrigShared`,
`_faceGraphics`, `_faceOrigMats`, `_canvasOrigOverrideSorting`, `_canvasOrigSortingOrder`,
`_renderOnTopInstances`, `HeldCardSortingOrder`, and **`CardMesh.HeldCardRenderQueue`** (its only
code reference is `VRCard.cs:527`, inside the dead method).

The revert is documented *in place*, at `VRCard.cs:512–518`:

> *"REVERTED (perspective fix): the render-on-top queue/sorting bump … ALSO swallowed all card
> TEXT … Kept as a no-op-forward for call sites."*

**This corrects `INVARIANTS-Cards.md` §14, "Card materials are SHARED; the render queue bump is
PER-INSTANCE".** Its `Where:` names `VRCard.ApplyRenderOnTop` and `CardMesh.HeldCardRenderQueue`
(4200) and its `Breaks if:` warns against "lowering 4200 toward 4003" — constraints on code that
does not run. The *first* half (shared materials, so a late silhouette re-shapes every live card)
is live and correct; the *second* half was reverted.

**Recommendation: KEEP the code, fix the registry.** Three reasons this is not a Tier-0 deletion:

1. `SetRenderOnTop` is **not** dead — 5 live call sites — and its comment is the record of a
   tested-and-rejected approach, the same category as `PlayTray.SyncPinHolder`'s comment-with-no-code
   and `CardGlow`'s `// NOTE (removed)`, both of which the registry protects explicitly.
2. `INVARIANTS-Hands-Board-Core.md` rules the **analogous** `FigureGrabbable` block a **KEEP**,
   retained behind `#pragma warning disable CS0162`. Deciding the two differently would be
   arbitrary. (VRCard's copy is dead by "no caller" rather than by an unreachable `return`, which
   is why the compiler is silent about it.)
3. The `4200 > 4003 > 4100` queue reasoning in that invariant is still the design rationale for
   `PlayTray`'s and `ButtonCluster`'s widget queues.

**Action:** annotate `ApplyRenderOnTop`/`RestoreRenderOnTop`/`HeldCardRenderQueue` as retained-
but-inactive, and split registry §14 into the live half and the reverted half. Guard diff: empty.
**If the user prefers deletion**, it is a clean Tier 0 (~130 lines) — but the revert note and the
queue reasoning must survive in the invariant.

---

### 3.2 `PlayTray.RenderOnTop` — zero call sites (a *different* method from §3.1)

```
grep -rn '\bRenderOnTop\b' src/ → 19 hits: 1 declaration (PlayTray.cs:3578), 18 comments, 0 calls
```

All sixteen `PlayTray` mentions are comments saying the widget in question **no longer** uses it
("drop the `RenderOnTop` shine-through", "NO `RenderOnTop`", "depth-correct now"). The method
outlived every caller. §5 clear: `private static`, no attribute, no reflection by name anywhere,
not a config key, no log line inside it, unreachable from the debug menu.

**This corrects registry §7, "`RenderOnTop` uses per-renderer instances and two ZTest property
names".** The *lesson* is real and is implemented in two live places —
`WorldUI/NativeButtonSkin.cs:217` and `WorldUI/ActorBars.cs:455` both set `_ZTestMode` under a
`HasProperty` guard — but `PlayTray.RenderOnTop` itself is unreachable. **Removing it must move
that invariant's "Why" to the two live implementations**, or the reason for the two-property-name
rule is lost.

**Guard expectation:** the method disappears from `GloomhavenVR.Cards/PlayTray.cs`; nothing else.
**Risk if wrong:** a board HUD widget stops drawing over the board. Bounded — no caller to lose.

---

### 3.3 `PlayTray.ReseatProud` + `SeatOnBoardFace` + `SeatStandoff` + `SeatProud` — zero call sites

```
ReseatProud       decl PlayTray.cs:3216 · comments PlayTray.cs:468, CardsConfig.cs:205 · 0 calls
SeatOnBoardFace   decl PlayTray.cs:3241 · 1 call site: ReseatProud (:3221) · 6 comments
SeatStandoff      decl :3205 · used only inside SeatOnBoardFace (:3253, :3254)
SeatProud         decl :3208 · used only inside SeatOnBoardFace (:3277)
```

`ReseatProud` has **no caller**, so `SeatOnBoardFace`'s only caller is itself dead. ≈ 85 lines.
`_boardColliders` becomes write-only if the cluster goes (filled `EnsureBuilt:464`, cleared
`Destroy:956`, read only at `SeatOnBoardFace:3262`) — but the same colliders are separately
registered as `LaserTargets` at `EnsureBuilt:463`, so the laser surface is unaffected.

**This contradicts registry "Suspected vestigial" → `PlayTray.SeatOnBoardFace` / `ReseatProud`**,
which says *"They still have callers (the bundle Confirm/Undo and rest anchors …), so this is
**not** dead."* Incorrect at HEAD. The bundle Confirm/Undo anchors come from `FindDeep` in
`EnsureBuilt` and are used as-is; the rest anchors are built by `RestControls`; the gear/pin/
readout go through `NewAnchor`, which seats at the fixed `FixedProudZ` and says so ("in place of
the old raycast"). The registry entry was written from the surrounding prose — exactly the trap
the registry itself warns about.

**Guard expectation:** two private methods and two consts disappear from
`GloomhavenVR.Cards/PlayTray.cs`. Nothing else.
**Risk if wrong:** a board widget seats at the wrong depth. Bounded — no caller.
**Handling:** remove the methods, **keep the explanation**. Registry §6 ("Raycast auto-seating is
GONE; fixed proud Z replaces it") is the record of why; the `CardsConfig.cs:205` comment names
both symbols and must be rewritten, not orphaned.

---

### 3.4 `PlayTray`'s pick field — built on every board build, never shown

```
BuildPickField        called once (EnsureBuilt:496) → 5 GameObjects, ends SetActive(false)
SetPickFieldVisible   0 external callers  ⇒ _pickFieldVisible is never true
SetPickFieldHighlight only caller is SetPickFieldVisible (unreachable)
PickFieldAnchor       0 callers
PickFieldNear         0 callers (would return false anyway — guards on _pickFieldVisible)
_pickField / _pickFieldHighlight / _pickFieldVisible / _pickFieldHighlighted   internal only
```

≈ **125 lines**, plus a per-board-build allocation of five `GameObject`s, one glow material and
one TMP caption that can never be seen.

`AddCaption` (`:3347`) has exactly one caller — `BuildPickField:2076` — so it dies with the
cluster, making `PlayTray.cs:3052–3057` stale ("*AddCaption stays live for the pick field's
'SELECT' caption, so nothing goes unused*").

`SetPickActive` / `_pickActive` are **live** (they drive the CONFIRM accent at `TickStatus:2782`)
and must stay. The names are adjacent — do not remove them by association.

The class doc already admits the state at `:31–32`. Registry §8 ("The pick field hides the play
slots") documents `SetPickFieldVisible` as if live and needs a status note.

§5: no Harmony/reflection/config/debug-menu reach; the one log line is unreachable and so cannot
be a live grep token.

**One thing to raise with the removal, not resolve silently:** `Net/RemoteBoardFurniture.cs` has
its **own** `BuildPickField` (`:464`) whose doc says it mirrors `PlayTray.BuildPickField`, and it
*is* shown, via `SetPickField(bool)` (`:557`). If peers render a pick field the local player never
sees, that is an MP inconsistency independent of this refactor. Removing the local copy does not
break the remote one, but the cross-references at `RemoteBoardFurniture.cs:97` and `:460` would
dangle. → §7.

**Guard expectation:** six members and four fields disappear from `GloomhavenVR.Cards/PlayTray.cs`;
`EnsureBuilt` loses one call.
**Risk if wrong:** a pick flow shows no drop target. Bounded — the field is `SetActive(false)` from
birth and nothing turns it on; pick flows home into the slot recesses instead (registry §8).

---

### 3.5 `CardsGameApi` — eight methods with zero call sites

Verified two ways (`CardsGameApi.<name>` across `src/`, and a bare-name grep):

| Member | Lines | Bare-name hits |
|---|---|---|
| `SelectedCount(CardsHandUI)` | 258–259 | 1 = declaration |
| `InitiativeValue(CAbilityCard)` | 475 | 1 = declaration |
| `ReadyWidget()` | 916–924 | 1 = declaration |
| `UndoWidget()` | 931–940 | 1 = declaration |
| `ShortRestWidget()` | 687–696 | 1 = declaration |
| `GetInitiativeOrder(List<CActor>)` | 1269–1281 | 1 = declaration |
| `ActorInitiative(CActor)` | 1319 | 1 = declaration |
| `IsPlayer(CActor)` | 1322 | 1 = declaration |
| `ActiveCount(CardsHandUI)` | 1615–1628 | 4 — the other 3 are `Net.RemoteBoardContent.ActiveCount`, an **unrelated** property counting infused elements. 0 real callers. |

**Root cause for the three `*Widget()` methods, and it matters:**
`WorldUI/Surfaces/TrayControlDockSurface.cs:84` sets `_controls = System.Array.Empty<DockedControl>()`,
and lines 93/96/103/106 hardcode `ContinueDocked => false`, `ContinueVisible => false`,
`UndoDocked => false`, `ShortRestDocked => false`. **Nothing docks any native widget any more**,
so nothing asks for the RectTransforms.

That has a knock-on inside `PlayTray`: `TickStatus:2750` (`_confirm != null && ContinueDocked &&
ContinueVisible`) and `:2790` (`_undo != null && UndoDocked`) are **permanently-false branches**.
**Registry §10, "The mod CONFIRM hides only when the native is docked AND rendering"**, with its
`Breaks if: Dropping the second term as redundant`, describes a condition where both terms are
compile-time-constant `false`. The invariant's *reasoning* is still the record of why the twin
gating exists; its *code* is inert. The constants live in `WorldUI`, so this is **not a Cards-only
removal** — document it here, hand it to the WorldUI review.

Three further members are `internal` but only called from inside `CardsGameApi.cs` and could be
`private`: `ActionPhase()` (819, callers 856/882), `TakeDamageSubject()` (1340, caller 1374),
`TakeDamageIsLocalDecision()` (1360, caller 1379).

§5 clear on all: no Harmony attribute, no Unity message name, no `typeof(CardsGameApi)` anywhere,
not a config key, not a log token, absent from `SettingsPanel.cs`. The `Describe*Gate`
diagnostics that registry §16 protects are all **live** (1 caller each; `DescribeReadyState` 3).

**Guard expectation:** the members disappear from `GloomhavenVR.Cards/CardsGameApi`; nothing else.
**Risk if wrong:** nil for the eight — nothing calls them.

---

### 3.6 `VRCard` / `CardFace` — four unreferenced members

| Symbol | Evidence |
|---|---|
| `VRCard.SetPopped` (658) | **0 references repo-wide.** `_popped` itself is live — written at `OnGrabHighlight:611` and the poke paths `:1635/:1643` — so registry §4 ("Two separate pop sources") keeps its substance; only the named setter is unused. Its doc ("set by layouts each frame is fine") describes a caller that does not exist. |
| `CardFace.ReclaimedFromDialog` (78) | 0 references. The backing `_reclaimedFromDialog` **is** live (103, 489–491, 560, 576). |
| `CardFace.Owner` (80) | 0 references. `VRCard` touches only `_face.IsAdopted/.Adopt/.FaceSize/.Restore/.Maintain`. |
| `VRCard._visualRoot` (101) | a field read only inside `Build` (252, 253, 266, 272); never used after construction — a local would do. Tier 1, not 0. |

Also verified, **against** the registry: `VRCard.IsRooted` has exactly three references, all inside
`VRCard.cs` (165, 1633, 1818). Registry §1 lists it as *"consumed in … `CardsDriver.ScoreContact`,
`CardsDriver.UpdateBoardLaser`"* — those inline `!card.CanGrab` (`CardsDriver.cs:1821`, `:2104`),
which is a **different predicate for a held card** (`IsRooted` is false while held; `!CanGrab` is
true). The registry also says "`VRCard.OnPoke`" where the use is in `OnPokeEnter` (1633). The
invariant's substance is fine; its `Where:` line is not. → §5.0.

**Verified NOT dead, against the registry's suspicion:** `CardFace.LogFaceTextureDiag` has exactly
one live caller — `CardFace.Adopt`, `CardFace.cs:135` — and `CardFaceMipBake.cs:11` cites its log
line as the proof source for the whole mip-bake feature. The registry's own "low-confidence
vestigial; more likely still useful" reading is the accurate one. **Not a Tier-0 candidate.**

---

### 3.7 `ItemsPile` / `BurnCardFx` / `ActivePileViewer` — the consumed-plume pair and four write-only members

Verified independently, not taken from the registry:

| Symbol | Evidence | §5 |
|---|---|---|
| `BurnCardFx.SpawnConsumedPlume` | 2 occurrences total: declaration `BurnCardFx.cs:134`, one comment `ItemsPile.cs:1156`. **0 call sites.** Its log token is unreachable. | clear |
| `BurnCardFx.ItemPlumeCardSpan`, `ItemPlumeMaxLifetime` | used **only** inside `SpawnConsumedPlume`. `StartSizeMultiplier`/`StartSpeedMultiplier` are shared with `Bind` and **stay**. | clear |
| `ItemsPile.ItemChip._plume` | 4 occurrences, **zero non-null assignments** — the `OnDisable` destroy branch (2188–2192) is unreachable | clear |
| `ItemsPile.ItemChip._hasClip` | 1 declaration, **3 writes of the literal `false`, 0 reads**. This is the CS0414 the compiler already reports — the only warning in `Cards/`. | clear |
| `ItemsPile.ItemChip._heldRot` | declared 1042, assigned once (1882), **never read**. `TickHeldPose` derives rotation from the head billboard (2155–2156). Mirrors `VRCard._heldRot`, which **is** used — check that copy before assuming symmetry. | clear |
| `ActivePileViewer.CardScale` | declaration `:32`, **0 code references** — `Relayout:179` reads `CardsConfig.ActiveCardScale(board).Value` directly. Four comments reference it (`:15, :29, :221`, `PlayTray.cs:689`). | clear |

Registry: *"Removable as a pair, but only as a pair — and the 'stays removed' comment must
survive."* Confirmed. The same rule applies to `ActivePileViewer.CardScale`'s four comments: the
*value* (active cards read smaller than hand cards) is real, the *property* is not.

Adjacent asymmetry, not dead but worth a line: `ItemsPile.Destroy` (237–261) does not reset
`_useSlotShownLogged` or `_signature`, while `Close` (214–235) resets both.

**Guard expectation:** members disappear from `GloomhavenVR.Cards/{BurnCardFx, ItemsPile,
ActivePileViewer}`. `_hasClip`'s removal silences the subsystem's only compiler warning.
**Risk if wrong:** nil for the write-only fields. For `SpawnConsumedPlume`, registry §12 calls
re-adding the plume "a documented temptation" — carry the comment forward.

---

### 3.8 `PlayTray.GenericClusterButtonSize` — confirmed unreferenced (registry was right)

```
declaration PlayTray.cs:1547 · comments :3077, :3083 · 0 call sites
```

Its own doc opens `SUPERSEDED` and ends "*nothing in the cluster path uses it*". Registry §7
explains why it must never be re-wired.

**Remove the method, keep both comments.** `GenericColumnHeight` and `GenericButtonGap` are used
only by it and die with it — but `GenericColumnHeight` is also referenced from `GenericClusterY`'s
doc, which stays. Note `PlayTray.cs:3073–3087` **contradicts itself inside one comment block**:
the first paragraph says the area "AUTO-SCALES each cap's size from the live count
(`GenericClusterButtonSize`)"; six lines later the same block says it does not and explains why.
The stale paragraph goes with the method.

---

## 4. Deduplication (Tier 2) — evidence attached

Every candidate below was extracted to files and `diff`ed. Pairs that turned out **not** to be
duplicates are in §6 — that half of the sweep is the more valuable one.

### 4.1 `CardFan` — two byte-identical blocks and a preamble repeated five times

Inside one 1 719-line file:

```
Relayout 1350-1355  vs  TickCollapse 1603-1608   → diff (indent-normalised) exit 0, ZERO differences
Relayout 1379-1388  vs  TickCollapse 1620-1625   → diff (comments+indent normalised) exit 0, ZERO differences
```

The first is the collapsed-pose seed (`mid`, `midAngle`, `midRad`, `collapsedRot`, `collapsedXY`);
the second the per-card pose (`angle`, `rad`, `rot = Euler(0,0,-angle*tiltFactor)`,
`pos = new Vector3(sin*radius, (cos-1)*radius*archFactor, _depths[i])`).

Separately, the **`radius`/`maxArc`/`stepCap`/`step`/`start` derivation appears five times**:
`NearestGap` 306–313, `ArcHalfWidth` 1014–1019, `GazeApexIndex` 1043–1049, `Relayout` 1308–1316,
`TickCollapse` 1589–1601. `Relayout` vs `TickCollapse` differ only in **statement order** (`step`/
`start` before vs after the `FanCurveByFill` block — no data dependency either way) plus one extra
local `w` that only `Relayout` uses.

`ArcHalfWidth`'s own doc (1009–1010) says it *"shares the exact step/sweep/radius derivation used
by `Relayout` so the gaze saturation and the cards can never disagree"* — **the sharing is by copy,
not by call.** That is the sentence that makes this a real defect rather than cosmetic repetition:
the file states an invariant it enforces only by hand.

**Proposal:** one `private FanGeometry Geometry(int n)` returning `(radius, step, start, maxArc,
tiltFactor, archFactor)`, called from all five sites. This is also what makes §2.4's `Relayout`
extraction clean.

**Guard expectation:** diff confined to `GloomhavenVR.Cards/CardFan`.
**Risk if wrong:** the fan lays out at the wrong arc. Medium — but this is the one dedup where the
in-source doc *asks* for it, and registry §4 ("`ComposeDepths` is the single source of depth truth
… so the steady layout, the close animation and the insertion-gap hit test can never drift apart")
applies the identical argument to the sibling concern. Depth truth is already centralised; **arc
truth is not, and the file believes it is.**

---

### 4.2 `CardsDriver.SubscribeBoardTuning` — 34 pairs kept in sync by hand

`CardsDriver.cs:232–311`. The `if (subscribe)` branch (238–271) and the `else` branch (275–308) are
**34 lines each**. Normalising `+=`/`-=` and stripping comments and indentation:

```
diff sub.txt unsub.txt → no output
IDENTICAL after normalising the operator, comments and indentation
```

Every config entry must appear in both lists, and the lists grow with every new per-board entry. A
bind added to only one leaks a subscription to a destroyed `CardsDriver` on every rebuild — no
compiler error, no symptom until something throws inside a stale handler.

**Proposal:** one list plus a local helper. `SettingChanged` is declared on the non-generic
`ConfigEntryBase`, so a single non-generic helper covers all 34.

**Guard expectation:** diff confined to `GloomhavenVR.Cards/CardsDriver`.
**Risk if wrong:** a tuning entry stops live-applying, or a subscription leaks. The diff above
proves the lists are currently in sync, so the merge cannot *introduce* an asymmetry — it removes
the possibility of one.

---

### 4.3 `PileBrowser.TryRaycast` vs `ActivePileViewer.TryRaycast` — 49 lines, one token apart

```
diff PileBrowser.cs:532-580  ActivePileViewer.cs:224-272
-        if (!IsOpen  || _root == null)
+        if (!IsShown || _root == null)
-            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (enlarged cards)
+            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (smaller cards)
```

**One property name and one comment word.** All 47 other lines are character-identical: signature,
out-params, `halfW`/`halfH`, the `denom < 1e-5f` backface reject, `dist <= 0f`, the local-rect
test, the sticky early return, the `dist >= distance` compare, `return card != null`. Confirmed
independently by two separate extractions.

Registry §13 requires both to keep the sticky early-return and the backface reject; a shared helper
preserves both by construction.

**Proposal:** one `internal static bool TryPickLiveRect(IReadOnlyList<VRCard> cards, …)`, with the
`IsOpen`/`IsShown` gate staying at each call site.

**Guard expectation:** diff confined to `GloomhavenVR.Cards/{PileBrowser, ActivePileViewer}` and the
new host type.

**Honest counter-argument, and it is not weak.** The two layouts overlap for different reasons — the
browse arc by `ZStagger`, the active grid because *"the lower rows sit nearer the viewer"* (registry
§13). If one later needs an accept margin like `CardFan`'s 1.10, merging is what makes that change
silently hit the other. **Rank this below §4.1 and §4.2**, and if the plan is short on time drop it
and take the §5.3 documentation fix instead — that captures most of the value at none of the risk.

---

### 4.4 Smaller verified-identical pairs — recorded, not recommended

| Pair | Diff result | Verdict |
|---|---|---|
| `ItemsPile.PlaceAtHead` 509–530 vs `PileBrowser.PlaceAtHead` 293–314 | **byte-for-byte identical**, 22 lines incl. comments | genuine duplication, no documented reason to differ; small |
| `VRCard` affordance-drop block: `FlyToPile` 1229–1236 vs `FlyFromPile` 1281–1288 vs `Vanish` 1520–1527 | **identical**, 8 lines, both pairs | a `DropAffordances()` extraction is clean Tier 1; `OnDisable` 1863–1868 is a deliberate near-copy (omits `Grabbable`/`_instantNext`) |
| `CardsGameApi.ActionSelectionHand` 78–83 vs `LongRestTurnHand` 360–365 | **identical**, 6 lines (`diff` exit 0) | trivial |
| `CardsGameApi.CanShortRest` 666–667 vs `CanLongRest` 778–779 | **identical** guard | trivial; `PhaseManager.PhaseType … SelectAbilityCardsOrLongRest` appears at 9 executable sites |
| `PileBrowser` suppression-clear, 420–425 vs 516–521 | identical 6 lines (comments differ) | a `ClearSuppression()` extraction is the only safe form — `ClearHandSweep` also clears `_handWinner`, which the prologue must **not** (the hysteresis at 458 reads it) |
| `PileViewer` stack positions | computed in `EnsureBuilt` (131/138/146), passed as `localPos`, then **overwritten by `ApplyLayout()` two lines later** (191/196/201) | the `localPos` parameter is dead on arrival |
| `CardsConfig` per-board plumbing | 35 arrays hardcoding `[3]` + 35 one-line resolvers; `ControlBoards.Count` has **0 external uses** although its own doc condemns "a hardcoded `% 3` … precisely the kind of duplication that lets a new board be added everywhere but one" | a table-driven rewrite is Tier 3 → §7 |

---

## 5. Documentation defects — the highest value-per-risk work in this review

The registry's premise is that stale-but-plausible documentation is this codebase's worst hazard.
Twenty-two verified instances follow. **Comments do not survive into IL, so every fix has a
guaranteed empty guard diff and zero behavioural risk.**

### 5.0 Errors in `INVARIANTS-Cards.md` and `CENSUS.md` themselves — fix these first

The registry is the constraint set for the whole refactor. Seven of its statements are wrong at
HEAD, and two of them would misdirect a Phase-2 decision.

| Where | Claim | Verified reality |
|---|---|---|
| `INVARIANTS-Cards.md:2044` + `CENSUS.md:37` | `InspectForward`, `InspectUp`, `RevealPreset`, `RevealDemeo` are bound-but-unread `[Cards]` config entries | **All four have zero occurrences in `src/`.** They exist only in `.planning/research/DEMEO-HANDS-CARDS.md` and in these two documents. `CENSUS.md` recommends a user decision "for all of them at once" over a set that is half fictional. |
| same | the list of unread binds is complete | **Two are missing**: `TrayTilt` (decl 150, bind 497, **0** code readers — superseded by `BoardTilt_{board}`, and `PlayTray.cs:1076` says so) and `RoundButtonThickness` (decl 174, bind 540, 0 readers — the live value is `ButtonTuning.RestCapDepth`). So the real set is 6, not 8: `HeldTiltDegrees`, `RoundButtonDiameter`, `RoundButtonThickness`, `RestButtonInsetX`, `ConfirmUndoInsetX`, `TrayTilt` (+ `FanArcDegrees`). |
| `INVARIANTS-Cards.md:51` | the resting rect is *"consumed by `CardFan.TryRaycast` and `PlayTray.TryRaycastCards`"* | **`PlayTray.cs` has zero `TryGetRestingLaserRect` hits.** `TryRaycastCards` (2604–2635) builds its plane from the **live** transform — `Dot(direction, t.forward)`, `t.position`, `t.InverseTransformPoint` — literally the construction the same entry's `Breaks if:` forbids. `3b86e72` did add the twin there; `4b7ac8a` reverted it. |
| `INVARIANTS-Cards.md:115` | `IsRooted` is *"consumed in … `CardsDriver.ScoreContact`, `CardsDriver.UpdateBoardLaser`"* | 3 references, **all inside `VRCard.cs`**. Those two sites inline `!card.CanGrab`, a different predicate for a held card. The entry also says `VRCard.OnPoke` where the use is `OnPokeEnter`. |
| `INVARIANTS-Cards.md:1667` (§14) | the per-instance render-queue bump (`VRCard.ApplyRenderOnTop`, `CardMesh.HeldCardRenderQueue` 4200) is current law | **reverted** — see §3.1. The shared-materials half is live; the queue-bump half is not. |
| `INVARIANTS-Cards.md:1124` (§10) | the mod CONFIRM hides when the native is `ContinueDocked && ContinueVisible` | both are hardcoded `=> false` (`TrayControlDockSurface.cs:93, 96`), as are `UndoDocked` and `ShortRestDocked`; `_controls` is `Array.Empty` (`:84`). The branch can never be taken — see §3.5. |
| `INVARIANTS-Cards.md:1970, 1978` | `CardsConfig.Bind` owns the `TableScaleDefault25Applied` migration, the `[TransientButtons]` fan-out, and the `[RoundButtons]/[BoardButtons]/[BoardDashboard]/[RestButtons]` sections | `TableScaleDefault25Applied` is in `Rig/ComfortSettings.cs:298`; the fan-out and all four button sections are in `WorldUI/ButtonTuning.cs:186–258, 375–410`. **`CardsConfig.cs` binds exactly one section, `[Cards]`** — 100 `_file.Bind("Cards", …)` calls, 170 keys, zero others. |
| `PlayTray.RenderOnTop` (registry §7) | documented as live | 0 call sites — §3.2 |
| `PlayTray.SeatOnBoardFace`/`ReseatProud` ("Suspected vestigial") | *"still have callers … so this is **not** dead"* | 0 callers — §3.3 |

**Method note for Phase 2.** My first dangling-cref sweep was unsound: it searched all of `src/`,
so a `<see cref="X"/>` matched *itself* and reported zero danglers. Re-running against
comment-stripped sources found four, three of them real:

| Cref | Where | Status |
|---|---|---|
| `SideDepth` | `CardFan.cs:588`, `:1148` | **dangling** — the methods are `BowDepth` (1224) / `RestBowDepth` (1259); the config entry is `FanSideDepthCurve` |
| `BuildSlotLabels` | `PlayTray.cs:72` | **dangling** — removed in test #29 (`PlayTray.cs:3055` says so) |
| `Patches.HandSuppressionPatches` | `CardsModule.cs:10` | names the **file**, not a type (the types are `CardsHandManager_ShowList_Patch` etc.) |
| `PrimitiveType.Cylinder` | `CardMesh.cs:352, 379` | false positive — external Unity enum |

### 5.1 `CardsDriver.BlockCardInteractions` cites the exact predicate the fix removed

`CardsDriver.cs:2321–2328`:
> `/// Menu-open gate (see <see cref="Update"/>): while a modal window floats`
> `/// (<see cref="WorldUI.ModalFallback.WindowModalActive"/>) force EVERY card …`

The caller, 1 280 lines earlier, spends twelve lines explaining that `WindowModalActive` *was the
bug*, "twice over", and gates on `BlockingWindowModalActive` (`:1047`). Registry §11 makes it an
invariant with `Breaks if: Reverting to the broader predicate 'to be safe'.`

A reader who opens `BlockCardInteractions` in isolation — which is exactly what a reader of a
5 483-line file does — finds a doc comment telling them the broader predicate is correct.

### 5.2 `CardsDriver.UpdatePalmGate` documents roll gate **v3**, which v4 replaced

`CardsDriver.cs:1289–1294`:
> `// Live-tunable gate feel (roll gate v3): PalmGate measures Demeo's own roll dot —`
> `// the hand's RIGHT axis tilt toward world up — asin-mapped to DEGREES …`

`Hands/Interact/PalmGate.cs:11–16`, the class it describes:
> `ROLL MEASURE (v4 …): the v3 Demeo measure (dot(side · handRight, worldUp)) is pitch-proof only`
> `while the fingers stay [horizontal] … never open (user requirement …)`

The Cards-side comment describes the measure that **failed on hardware** as if it were what runs.
**Fix:** replace with a pointer to `PalmGate`'s class doc — do not restate the measure, restating
it is how it went stale.

Two lines below, `gate.UseDevicePalmNormal = !_gateHand.IsSimulated;` carries `// sim hands pose
the rig directly`. `PalmGate.cs:96–101` says the flag is *"VESTIGIAL since roll gate v4 (kept so
the Cards driver's per-frame assignment stays [valid])"* — a deliberately-retained no-op whose
Cards-side comment does not say so. **Say so; do not remove the assignment.**

### 5.3 `PileBrowser.TryRaycast`'s doc invites the merge invariant §1 forbids

`PileBrowser.cs:527–530` — *"the browse counterpart of `CardFan.TryRaycast`. **Same** per-card
plane+rect test …"*. Diffed, it is not:

```
CardFan.TryRaycast                          PileBrowser.TryRaycast
  const float acceptMargin = 1.10f;           (none)
  c.TryGetRestingLaserRect(out center, …)     Transform t = c.transform;
  Dot(direction, normal)                      Dot(direction, t.forward)
  Dot(center - origin, normal) / denom        Dot(t.position - origin, t.forward) / denom
  Dot(rel, rectRight) > halfW * acceptMargin  InverseTransformPoint(hit).x > halfW
```

Fan: **resting** rect + 10 % accept margin. Browser: **live** transform, exact half-extents.
Registry §1: *"Breaks if: 'Simplifying' either raycaster to `transform.position` /
`transform.forward` / `InverseTransformPoint`"* — literally what the browser does, correctly,
because it has no laser-driven pop feedback loop. A reader who trusts "Same" unifies them and
reintroduces `306e8ea`.

### 5.4 The `VRCard` render-on-top comment cluster describes disabled behaviour

Seven comments read as live (§3.1). The two worst contradict each other 390 lines apart:
`VRCard.cs:111–119` (*"we push BOTH the opaque backing slab AND the world-space face-art graphics
past those widgets … Instances are destroyed on restore (no leak)"*) vs `:512–518` (*"REVERTED …
never apply the bump"*). Also `:388–389`, `:398`, `:495`, `:501–509`, `:1693–1695`, and
`CardMesh.cs:62–78`.

### 5.5 `ItemsPile`'s consumed-plume prose describes deleted code

| Where | Claim | Reality |
|---|---|---|
| `ItemsPile.cs:44–45` (class doc) | `CONSUMED → ashen + the game's burn plume (<see cref="BurnCardFx"/>)` | all three `BurnCardFx` mentions in the file are comments; there is no code path |
| `:936` (`ItemChip` doc) | `Consumed items carry the burn plume` | same |
| `:966` | `_plume; // consumed-item smoke, destroyed with the chip` | never assigned |
| `:1149–1152` | `the mod-owned pieces (backing above, fallback face + plume below)` | nothing below is a plume |
| `BurnCardFx.cs:124–133` | full XML doc for `SpawnConsumedPlume` in the present tense | no caller |

Registry §12 states the current law and warns re-adding the plume is "a documented temptation".
These five comments are that temptation, in the source, in the imperative.

### 5.6 `RestControls` and `CardsGameApi` document a docking that cannot occur

`RestControls.cs:13–15` (class doc): *"The REAL native 'Kurze Rast' widget docks over the
short-rest anchor when available … the mod short button then HIDES and reappears when the native
widget undocks."*
`RestControls.cs:159–161`, same file: *"the native 'Kurze Rast' widget never docks
(`TrayControlDockSurface.ShortRestDocked` permanently false), so this mod keycap is the sole
short-rest control."*

The second is correct (`TrayControlDockSurface.cs:106`). `TickStatus` never consults
`ShortRestDocked`; visibility is `canShort || shortSelected` (171–172). The same false claim
appears at `CardsGameApi.cs:680–682` (`ShortRestWidget`), `:908–910` (`ReadyWidget`) and
`:927–928` (`UndoWidget`) — all three of which are dead **because** of it (§3.5).

### 5.7 `ItemsPile._anchor` documents the reverted position

`ItemsPile.cs:92`, `:143`, `:425–426` all say `_anchor` is *"the pile mount"*. `PileViewer.cs:154`
passes the **items stack transform**, with `mount` only as a null fallback — and registry §12 makes
that a named invariant (*"The item fan converges on the ITEMS stack … Breaks if: 'Simplifying' to
`mount`, which is only the null fallback"*). `:92` additionally calls it a "placement scale ref",
which it is not.

### 5.8 `PileViewer`'s docs say "two piles"; the code has three

| Where | Says | Code |
|---|---|---|
| `:13–14, 18–19` (class doc) | "two small physical card piles" | `EnsureBuilt` builds `_discard`, `_burnt` **and** `_items` (129/136/146) |
| `:170` | "Re-read **both** pile captions" | refreshes three |
| `:179–181` | "discard upper at +spacing/2, burn lower at −spacing/2" | three stacks; items at `−spacing*1.5` (201) undocumented |
| `:244–246` | "cheap — **two** list Counts" | three counts, **plus** `_itemsBrowse.Tick(hand)` (275, which runs the whole item-fan tick including `UpdateHandSweep`) and `TickItemsUsableHighlight` (276, which scans the inventory) |
| `:103` | "real **game** loc keys" | `PileKind.Items` → `Core.Loc.Mod("items")`, a mod string |

`:244–246` is the one that matters: it advertises a per-frame cost off by an order of magnitude —
exactly the claim a future perf pass would trust.

### 5.9 Config descriptions that invite tuning which cannot respond

Beyond `CENSUS.md`'s Group B, three more verified:

| Entry | Description says | Reality |
|---|---|---|
| `RoundButtonDiameter` (532–539) | *"NO baked notch dimension exists in code, so **this is the fit knob**. PER-BOARD: a future per-board descriptor will override it"* | that descriptor already exists and already wins — `RestControls.cs:47` reads `RestButtonDiameter(active)`. 0 readers. |
| `RestButtonInsetX` (545–554) | *"**LIVE FIT KNOB** (dial in dev.gloomhavenvr.cards.cfg without a rebuild) … tune it live"* | `RestControls.cs:65` reads `RestButtonOffset(active)`. 0 readers. |
| `ConfirmUndoInsetX` (555–558) | ends *"PER-BOARD."* | a single global with 0 readers; `_confirmUndoOffset` (bind 612) is the per-board one that is read |
| `FanRadius` (110–111, 455–456) | *"**Palm fan** arc radius"* | read by `PileBrowser.cs:362` and `ItemsPile.cs:542` — the pile/browse fans. The hand fan reads `FanEffectiveRadius`, whose own description states the split correctly |

### 5.10 Two more verified stale claims

- **`CardFan.cs:13` — "No allocations in `Tick`."** `Tick` reaches at least four interpolated-string
  allocations (`:502–505`, `:745–746` via `UpdateGazeBias`, `:1498–1505` via `Relayout`) and
  `ComposeDepths` reallocates `_depths` at `:1096` when `n` exceeds capacity. All sit behind
  change/throttle gates — which is the documented design (registry §16) — but the blanket claim is
  false, and registry §16 (*"String building sits behind the change gate, not in front of it"*)
  is precisely the invariant a reader would check it against.
- **`CardsConfig.cs:746–767` — the BoardScale migration says "0.4" about a 0.5 default.** The
  comment reads *"the new 0.4 BoardScale default **above**"*; the bind at `:645` is `0.5f` and its
  own description explains why (*"the ~0.4 table-ratio default felt a touch small on first spawn"*).
  The migration body still writes `0.4f` and the marker key is `BoardScaleDefault04Applied`. The
  *comment* is wrong about the line above it; whether fresh-0.5-vs-migrated-0.4 is intended is a
  behaviour question → §7.

### 5.11 Names that assert something the code does not do

| Name | Where | Mismatch |
|---|---|---|
| `VRCard.SetRenderOnTop` | `VRCard.cs:510` | never sets anything on top; it is an unconditional *restore*. `true` and `false` do the same thing, at 5 call sites |
| `ItemChip.TryFingertipDistance` | `ItemsPile.cs:2068` | not a fingertip test (called with the **palm** at 600) and the `Try` prefix implies the reach condition, which lives in the caller. `VRCard.cs:1646–1651`'s equivalent doc is correct and is the model |
| `ItemChip._plume` / `_hasClip` | `:966` / `:1070` | name things that are never created / never read |
| `ClampCardEffectSmoke` / `RestoreCardEffectSmoke` / `SmokeClamp` / `SmokeCardSpan` | `:1441–1603` | every name says "smoke", but the scan is `GetComponentsInChildren<ParticleSystem>(includeInactive: true)` — **all** emitters, deliberately (registry §12 makes plural + inactive-inclusive an invariant). Name and doc together hide the property the invariant protects |
| `ItemChip.OnPoke/OnPokeEnter/OnPokeExit` | `:2163–2181` | a **laser-only** surface — registered via `RegisterLaserTarget` (1162), never via `VRInteractables.RegisterPokeable`, unlike `PileStack` (`PileViewer.cs:665–671`). Registry §13 protects the fact that the two `OnPokeExit`s have **opposite** semantics; the identical names conceal it |
| `CardMesh` | whole file | **227 of 747 lines are *button* geometry** with no card involvement (`BuildBeveledKeycap` 250–348, `RoundCapSegments`/`GetRoundCap`/`BuildRoundCap` 356–483). `WorldUI/ButtonCluster.cs:742` reaches across subsystems into `Cards.CardMesh` to build a keycap |
| `VRCard._popped` vs `_pop` | `:137` / `:138` | one character apart; `_popped` is the proximity-highlight **input flag**, `_pop` the smoothed 0..1 **output** |
| `VRCard.SetColliderRegion` vs `ResetColliderRegion` | `:820` / `:931` | "Set/Reset" implies inverses of one thing; they are the *fan-strip* shape and the *full-card-or-dock-apron* shape |
| `CardFace.ReadTexture` | `CardFace.cs:368` | does not read a texture — it allocates and returns a new one the caller must destroy |
| `CardFaceRaycaster.ModPointerIdCeiling = -100` | `:43` | with the test `pointerId > ModPointerIdCeiling`, the "ceiling" value is itself *accepted*; the real ceiling of rejected ids is −101 |
| `CardsGameApi.PlayableCardCount` | `:515–518` | counts `HandAbilityCards + RoundAbilityCards`, i.e. includes already-played cards. The doc admits it; the name does not |
| `CardsGameApi.ActiveCount` | `:1615` | collides with `Net.RemoteBoardContent.ActiveCount`, which counts *infused elements*. A bare-name grep returns both |
| `CardsConfig.HeldTiltDegrees` | `:126` | collides with the live, stepper-backed `FigureGrabConfig.HeldTiltDegrees`. Any grep returns the dead one first |
| `CardsConfig.RoundButtonDiameter/Thickness` | `:171, :174` | "Round" = circular here; everywhere else `[RoundButtons]` means the **round-phase** button group (`ButtonTuning.cs:13`) |
| `HalfSelection.SetVisible(false)` | `:88–98` | calls `ClearCards()` (97), unregistering every canvas from `UguiPokeSurfaces` — destructive, not a visibility flip |
| `ItemsPile.SetAnchor` / `_anchor` | `:144` / `:92` | dominant use is the emerge/collapse **converge point**, not a parent |
| `ItemsPile.ReleaseHeld(VRHand)` / `TogglePoke(…, VRHand)` | `:178` / `:164` | the signature promises per-hand semantics; the parameter is unused |
| `PileViewer.SetVisible` | `:205` | also force-closes the item browse (214) — invariant-protected behaviour, wrong name |
| `ActivePileViewer.CardScale` | `:32` | presents itself as the scale source of truth; `Relayout` never reads it |
| `EmptyFanHint` "localized" line | `:14–15` vs `:62` | doc says *localized*; the code is `Loc.CurrentLanguage == "German" ? "Keine Handkarten" : "No hand cards"`. Every other Cards string goes through the key table (`Loc.Mod`/`Loc.Game`) |
| `CardFan.SetHovered` / `SetInsertionGap` | `:249` / `:276` | the only two `public` members on an `internal sealed class`; `internal` suffices (same assembly). Reads as an external contract that does not exist. Same pattern in `ControlBoards` and `CardActionQueue.Entry` |
| `CardFan.ArcHalfWidth(int n)` | `:1012–1022` | returns the full `radius` for `n < 2`, where its own doc's formula gives **0**. Harmless where used (a saturation bound), but name, doc and code disagree |
| `CardMesh.Get` doc | `:92–95` | *"The mesh is shared between all cards of the same size"* — the cache is a **single slot** (`_sharedMesh` + `_sharedMeshSize`), so alternating callers (`VRCard.cs:359` card size vs `ItemsPile.cs:1210` near-square chip size) rebuild a `Mesh` **every call**. Contrast `_roundCapCache` (358), a proper `Dictionary`. Behaviour change → §7 |

**All of §5 is Tier 0 with an empty guard diff, and can be one commit per file.**

---

## 6. What should NOT be touched

A finding of "leave it" is a result. These are the cases where the obvious refactor is the
regression.

### 6.1 `CardsDriver.UpdateBoardLaser` (214 lines) — do not split

About 90 of the 214 lines are one comment block (1966–2011) reconstructing three hardware rounds.
The code is ~120 lines with a **three-phase order that is the fix**: pre-empt only on a real hit →
normal element scan → suppress-only fallback at the bail-out. Registry §2 has four entries on this
method, including the two most dangerous edits in the subsystem (`SuppressFarClick` is not
`UiHitOverride`; *"Hoisting the fallback back above the scan **for readability**"*). Splitting it
separates each phase from the paragraph explaining why it sits where it does.

### 6.2 `CardFan.TryRaycast` vs `PileBrowser.TryRaycast` — do not merge

Diffed in §5.3. Registry §1 names the merge as the reintroduction of `306e8ea`. **This is the
registry's "looks redundant, is not" case, confirmed by diff.** Fix the doc instead.

### 6.3 `VRCard.TryGetRestingLaserRect` vs `TryGetLiveLaserRect` — do not unify

Registry §1, two entries, both `high` confidence. Diffed here, and the registry's phrase
"near-identical" **overstates the similarity** — only 2 lines are textually shared. They differ on
four independent axes:

1. **frame** — parent-local home pose (`_homePos`/`_homeRot`) vs the live `transform`;
2. **scale** — `parent.lossyScale.x * _homeScale` (pop grow excluded) vs `t.lossyScale.x` (included);
3. **failure mode** — resting pre-zeroes its `out`s and returns false on a **parentless** card; live
   never inspects the parent and instead rejects a **degenerate half-extent** (`> 1e-5f`);
4. **length** — 22 vs 13 lines.

**Recommendation:** record this in the registry as *same signature, same purpose, different frame
and different guard*. That is a stronger statement of the invariant than "near-identical", and it
makes the merge visibly wrong rather than merely forbidden.

### 6.4 `CardsDriver.UpdateBrowseLaser` vs `UpdateActiveLaser` — do not merge

Diffed; **not** identical:

```
-        if (!_browser.IsOpen || … || _boardHover != null)
+        if (!_active.IsShown  || … || _boardHover != null || _browseHover != null)   ← active yields to browse
…
-            if (dom.TriggerDown) ForeignInteraction("click-away (trigger off the pile)");   ← browse only
```

The extra guard term is the precedence chain (active is deliberately lowest); the click-away dismiss
exists only for the browse. Merging needs a flag parameter that re-implements both differences.

### 6.5 The four `Clear*Hover` methods — do not dedupe

`ClearLaserHover`, `ClearTrayCardHover`, `ClearBrowseHover`, `ClearActiveHover` are four **identical**
6-line bodies over four fields. Deduping needs `ref VRCard?` parameters, changing the compiled form of
all four plus adding a method, to save 18 lines. The four fields participate in a documented
precedence chain that is easier to audit when each has its own named clear.

### 6.6 `PlayTray.BoardButton` dwell machinery — do not remove **and** do not re-enable

`DwellSeconds` is `0f` and no caller sets it, so `DwellHoldRange`/`_dwellHand`/`TickDwell`/
`BeginDwell`/`CancelDwell`/`DwellChargeColor` (~90 lines) look dead. They are not: `239acb5` removed
the dwell *by user directive* while explicitly keeping the `ActivationGuard` accident window.
Registry §7 flags it in both directions. **Leave — and add one line at `DwellSeconds` saying it is
configured-off by user directive, not vestigial.** The current doc says "Machinery kept for a
possible config", which does not convey that re-enabling it reverses a decision.

### 6.7 The three shared-constant families — do not centralise

| Family | Copies | Why they are deliberate |
|---|---|---|
| `ContactTipReach` 0.035 / `ContactPalmReach` 0.13 / `ContactStickyMargin` 0.02 | `ItemsPile`, `PileBrowser`, `CardsDriver` (verified identical) | each copy names its upstream source (`CardFan.FingertipHoverReach`, `ProximityGrabber.ReachMeters`) in a comment; the three arbitrations are separately tuned by registry §3 and §13 |
| `ZStagger = 0.004f` | 7 declarations | the 3 in `Net/` are wire-side mirrors, **deliberately decoupled** so a wire change cannot follow a local tuning change; each says so |
| `BoardFloatHeight` / `BoardFloatProudZ` / `BoardAnchorBase` | `ItemsPile`, `PileBrowser` (verified identical) | registry §12/§13 tune the two fans independently |

### 6.8 The blit→readback trio — document, do not merge

`CardFace.ReadTexture` (368–401), `CardFaceMipBake.ReadbackAtlasPixels` (564–585) and
`CardFaceMipBake.Bake` (670–712) share an 8-line skeleton but differ on
`RenderTextureReadWrite.Linear` vs `.sRGB`, `mipChain`, filter/aniso, and return type. **Registry §14
makes the colour-space split an invariant** (*"The bake path is sRGB; the silhouette path is Linear —
deliberately … Breaks if: Extracting a common readback helper"*), and §14 separately requires
`ReadbackAtlasPixels` to remain the single full-rect-only readback primitive.

### 6.9 `CardsConfig`'s unread binds — already handled, do not re-open

`CENSUS.md` covers them and recommends re-labelling rather than unbinding, because unbinding drops the
key from the user's `.cfg`. **Add only the two corrections from §5.0** (four of the listed entries do
not exist; `TrayTilt` and `RoundButtonThickness` are missing) and leave the recommendation intact.

---

## 7. Deferred — would need real behaviour changes (Tier 3, user decision)

Left in place, written down so they are not lost.

1. **`ItemsPile.PlaceAboveBoard` seats the fan without the item offset.** `:503` writes
   `BoardAnchorBase + BrowseFanOffset`; `Tick:354` writes `BoardAnchorBase + BrowseFanOffset +
   ItemFanOffset` every frame thereafter. The open frame is seated at the wrong spot and jumps on the
   next tick. Registry §12 makes the two offsets deliberately separate, so this is a one-frame visual
   defect — but fixing it changes what the player sees on the open frame.
2. **`CardMesh.Get`'s single-slot cache thrashes.** Two distinct sizes are in play (card vs near-square
   item chip), so alternating callers rebuild a `Mesh` on every call. A `Dictionary` keyed on
   `(w, h)` — like the existing `_roundCapCache` — is an allocation-pattern change, i.e. Tier 3.
3. **`CardsDriver.Rebuild`'s `switch (mode)` (137 lines) cannot be extracted verbatim** — it assigns
   three locals read afterwards; extraction needs `out` params or a return struct. §2.4 already takes
   `Rebuild` from 431 to ~290 without it.
4. **`PlayTray.BoardButton.Create` (264 lines) cannot be extracted verbatim** — its three cap-shape
   branches each produce seven values the code below consumes, and `capFrontZ` is invariant-critical
   (registry §7: the trigger collider is sized from it). §2.1 already moves the whole class out.
5. **`CardsConfig`'s 35 × `[3]` arrays and 35 one-line resolvers.** `ControlBoards.Count` exists as
   "the single definition" and has **0 external uses**, while its own doc condemns exactly this
   duplication. A table-driven rewrite is a real change.
6. **Two MP/consistency questions surfaced by the sweep, for the user rather than the plan:**
   `Net/RemoteBoardFurniture` shows a pick field the local `PlayTray` never shows (§3.4); and the
   BoardScale migration writes 0.4 while fresh installs get 0.5 (§5.10).

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

At 29–49 % comment, these files are long because the *reasons* are written down, and the registry
proves those reasons are load-bearing. Deleting them is the one thing that must not happen — which is
why every proposal above is motion or correction, never compression.

### Longest methods

| Method | Lines | Length | Verdict |
|---|---|---:|---|
| `CardsDriver.Rebuild` | 2683–3113 | 431 | two verbatim extractions → ~290 (§2.4) |
| `CardsDriver.OnCardReleased` | 3603–3891 | 289 | see note below |
| `PlayTray.BoardButton.Create` | 3899–4162 | 264 | do not extract (§7.4) |
| `CardsDriver.UpdateBoardLaser` | 1956–2169 | 214 | **do not touch** (§6.1) |
| `CardFan.Relayout` | 1296–1507 | 212 | two clean tail extractions (§2.4) |
| `CardFan.Tick` | 441–611 | 171 | two of three chunks extractable (§2.4) |
| `VRCard.UpdateBody` | 1689–1856 | 168 | **cleanest extraction in the subsystem** (§2.4) |
| `PlayTray.TickStatus` | 2654–2806 | 153 | extractable; low priority |
| `CardsDriver.UpdateBody` | 878–1025 | 148 | leave — it is the frame-order contract (registry §9) |
| `PlayTray.EnsureBuilt` | 374–505 | 132 | already delegates to 10 `Build*`; leave |
| `PlayTray.BuildMounts` | 570–697 | 128 | ~80 % comment (collision budgets); leave |
| `CardFace.TryCaptureSilhouette` | 238–362 | 125 | partly — the `try/finally` readback lifetime must stay whole (registry §14) |
| `CardsDriver.ApplyBoardTuning` | 332–455 | 124 | 12 near-identical `if (_applyX)` blocks, each with a different setter, entries and message — not deduplicable without a table-driven rewrite. **Leave** |
| `CardMesh.Build` | 107–228 | 122 | four clean phases (§2.4) |
| `CardFaceMipBake.TrimmedReplacementFor` | 386–506 | 121 | validation block lifts verbatim |
| `CardsDriver.UpdateSlotHighlight` | 2358–2458 | 101 | leave — registry §8 ties both branches together |
| `CardsConfig.Bind` | 402–988 | 587 | ten divider-marked blocks, **two ordering constraints** (§2.4) |

`OnCardReleased`'s five drop-outcome branches are each verbatim-movable with an explicit 7-parameter
list — Tier 1 by the charter's definition, but the list is wide enough that a mistyped argument is a
real risk, and registry §8 has six entries on this method. **Do it only after §2.2 lands, and only if
the user asks.**

`ItemsPile.cs` has **no method over 120 lines** (largest `TryHostRealCard` 97, `ItemChip.Create` 87),
and `CardsGameApi.cs` none over 40 — but `ItemsPile` has a **1 003-line stretch with no section
divider at all** (927–1930), covering `Create`, `BuildCardBacking`, `BuildUsableFrame`,
`TryHostRealCard`, the whole smoke-clamp cluster, the fallback face and the motion/use-flow/grab set.
Every other file in the subsystem is uniformly divided. **Adding dividers there is a Tier-0 change
with an empty guard diff and should ride along with §5.**
