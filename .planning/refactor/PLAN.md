# Refactoring Plan — Phase 3

> The ordered work list. Nothing here has been executed. Phase 4 does not begin until the user
> has seen this. Detail per item lives in the four `REVIEW-*.md` files; this document is the
> curated decision, not a paste of them.
>
> Inputs: `CHARTER.md` (rules, tiers, where the guard is blind), four `INVARIANTS-*.md`
> (~800 load-bearing behaviours), four `REVIEW-*.md`, `CENSUS.md`.

## 0. What the reviews actually concluded

The expected finding was "tangled code, needs untangling". That is **not** what came back.

- The code is not tangled. The **files** are too big, and 29–49 % of each large file is
  load-bearing comment. The prescription is therefore **motion and documentation repair — never
  compression.**
- Duplication is essentially absent. Across WorldUI a subsystem-wide scan found exactly **one**
  real verbatim pair, and the reviewer recommends **not** merging it (it would make three
  hardware-tuned constants shared). In Net/Rig, **11** near-identical pairs were examined and
  **none** merged — one of them turned out to hide an undocumented behavioural divergence.
- The real exposure is not code shape at all. It is **five blind spots** neither the compiler nor
  the guard can see: frame ordering, Harmony patch wiring, patch-doc drift, mirrored-constant
  drift, and interface-doc drift. All five close with text that never reaches the compiler.

So the plan front-loads **checkers and documentation**, and treats file splitting as the last and
least important part.

### The verification chain caught real errors at every layer

Worth stating, because it is the argument for the sequencing:

| Layer | What it corrected |
|---|---|
| History mining vs my briefing | 3 of my stated "facts" were superseded `STATE.md` prose |
| Source review vs history mining | 7 registry statements wrong at HEAD, incl. **4 config entries that do not exist** |
| Measuring vs reasoning about the guard | 2 wrong claims about my own tool, one of which would have made every split look like a failure |

The rule this produces, now written into `CHARTER.md`: **a history-derived fact needs a HEAD
check before it becomes a plan.**

---

## Batch A — Checkers first (Tier 0, no source behaviour touched)

Nothing else may run before these. They are what makes the rest safe, and they close the blind
spots the guard cannot.

| # | Item | Guard expectation |
|---|---|---|
| A1 | **Regenerate `docs/PATCH-INVENTORY.md` from source.** Doc lists 17 patched methods; the repo declares **60 `[HarmonyPatch]` attributes across 36 classes** (verified independently). Generate it, and add the check to `refactor-guard.sh` so it cannot drift again. | none — doc + script only |
| A2 | **Make an unregistered patch class loud at COMMIT time.** Twice a written patch shipped inert because no module carried its `PatchAll`. A static source parse sees every call site including the lazy one; a runtime audit provably cannot (it can't run before the lazy `PatchAll`, and both historical misses declare `TargetMethod`, so it could only warn). | none — no IL added |
| A3 | **Frame-order lock**: marker comments + `FRAME-ORDER.lock` + a text checker. Two levers must disagree for it to be defeated. 13 constraints ruled: 7 need markers, 6 already state their reason in-source. | none |
| A4 | **Golden-vector wire tests** — 10 byte-exact cases in a source-linked test project. Deliberately *not* round-trip tests: a round trip cannot catch this codebase's real hazard, which is a refactor that moves a block in `Write` **and** `TryRead` together. | none — separate project |
| A5 | **Mirrored-constant lint** (3 lines) instead of merging two constant pairs that straddle Hands↔Board. Merging would worsen layering; the actual failure mode is "someone tunes one copy". | none |

**Explicitly rejected in this batch:** adding `[DefaultExecutionOrder]` to the two
cross-`MonoBehaviour` orderings. The code deliberately does *not* rely on them — the two-frame
`HasFreshUiHit` window exists precisely so producer/consumer phase does not matter. Freezing an
order the code does not depend on is a Tier 3 behaviour change wearing a tidy-up costume.

## Batch B — Wire safety (Tier 0/1, provably free)

| # | Item | Guard expectation |
|---|---|---|
| B1 | `Cards.PileKind` and `Cards.ControlBoard`: explicit `= 0, 1, 2` + the wire-constant warning at the enum + a compile-time assertion at the cast site. The reviewer **built and ran** the assertion to confirm it errors on renumbering. | **empty** — implicit sequential enum values are not rendered in the snapshot (measured) |
| B2 | Name the last free protocol bit (`PileBrowseReservedBit = 1 << 7`) and terminate both flag-constant runs with "FLAG BYTE IS FULL → go here". Someone reaching for a flag bit looks at the flag constants, not 80 lines further down. | empty (const naming) |
| B3 | Make the GLOBAL / PER-ACTOR MODEL / VR-ONLY / DELIBERATELY-NOT classification greppable in code. It exists **only in prose** today, and `RemoteBoardContent`'s own header says "TWO DATA CLASSES" where there are four. Recommend a `CLASSIFICATION:` tag over marker interfaces — interfaces cannot express the three MIXED types. | empty (comments) |

B1 is the highest value-to-risk item in the whole plan: it neutralises a trap that produces a
silent multiplayer desync, with a diff that is provably empty.

## Batch C — Documentation repair (Tier 0, empty guard diff)

The reviews found **~40 stale comment clusters**. These are not cosmetic. Several tell a reader
to make the exact edit an invariant forbids:

- `CardsDriver.BlockCardInteractions` cites `WindowModalActive` — **the predicate that was the
  bug, twice over.** Its caller spends twelve lines explaining that, 1 280 lines earlier. A
  reader opening the method in isolation, which is what a reader of a 5 483-line file does,
  finds a doc comment endorsing the broken predicate.
- `CardsDriver.UpdatePalmGate` documents roll gate **v3** — the measure that failed on hardware —
  as if it were what runs. Fix: point at `PalmGate`'s class doc, do **not** restate the measure.
  Restating it is how it went stale.
- `PileBrowser.TryRaycast` claims "*same* per-card plane+rect test as `CardFan`". It is not.
- `EnemyRevealSurface`'s phantom Y-lock: seven contradictory strata, two of them three lines
  apart.
- The `0.5×–2×` resize floor documented in six files is really `0.15×`.
- `_tailSteps`' comment lists **five** steps for a **six**-step array — the only guard on the
  mod's most order-sensitive array has been wrong since a commit inserted a step and left the
  comment alone.
- `docs/INTERFACES-P2.md` is stale in the section `CHARTER.md` §5 cites as the "frozen API, do
  not delete" authority: 8 identifiers gone, and one switch described as doing **the opposite**
  of what the code does.
- Four dangling `<see cref="…"/>` (three real). Note the method caveat: the first sweep was
  unsound because crefs matched themselves — re-run comment-stripped for every subsystem.
- The registry's own 7 errors (`REVIEW-Cards.md` §5.0) and the `STATE.md` paragraphs that
  produced my 3 wrong briefings.

## Batch D — Dead code (Tier 0, each grep-verified against Charter §5)

| Where | What | Size |
|---|---|---|
| WorldUI | the 9-method mesh-UV island, `ReconcileMapDeferred` + `MapEffect`, `BoostAmbientForMapRender`/`EnsureMapLight`, orphans `Fmt` and `_backdropMenuQueue` | **~500 lines** |
| Cards | `PlayTray.RenderOnTop`, `ReseatProud` + `SeatOnBoardFace` (~85), the whole pick-field cluster (~125, built on every board build and never shown), 8 zero-caller `CardsGameApi` methods, the consumed-plume pair, `_hasClip` | **~300 lines** |
| Hands/Board/Core | 3 genuinely unreferenced members; `PalmGate.UseDevicePalmNormal` (confirmed vestigial — one dead write, its own doc says it is kept "so a dead write keeps compiling") | small |

**The map code is safe to delete** — I had warned the reviewer it might be a known-broken feature
awaiting repair. It checked: the feature is **solved**, and the document itself enumerates each
of these paths as "proven dead, do not re-attempt". My memory note was the stale thing.

**Not deleted, kept and annotated:** `VRCard.ApplyRenderOnTop`'s reverted block and
`ApplyRenderOnTop`'s analogue in Hands/Board — both are documented reversals, and deleting them
discards the record of *why not to re-add them*. Same ruling for the deliberately-disabled dwell
machinery and the `CardGlow` / `SyncPinHolder` comment-blocks-without-code, which **are** the
invariant.

## Batch E — Config honesty (**USER DECISION**, see §Decisions)

Seven `[Cards]` entries are bound and read by nothing. Not a cleanup — a compatibility promise
("kept bound so existing cfg files load"). Plus 19 further keys in `FlatScreenStereo` whose
descriptions now lie, and `ScreenLeftMirrorFallback`, which has no reader at all.

## Batch F — Motion (Tier 1), in confidence order

Every cut lands on an **existing hand-written divider**; fields already sit beside their region.

| # | Item | Guard expectation |
|---|---|---|
| F1 | Move top-level types that merely share a file — 4 out of `CanvasConversion`, `CardDustFx` + `PileKind` + 4 `CardsConfig` enums out of theirs | **nothing at all** — the snapshot already files them separately |
| F2 | `PlayTray`'s four nested types (incl. the 987-line `BoardButton`) into one file — **1 042 lines out** | empty (nested types hoist as a block) |
| F3 | `RemoteBoardContent.cs` → 7 top-level types, already banner-sectioned; 100 % `git mv`, no accessibility change | nothing |
| F4 | Partial splits: `CardsDriver` (5 483 → 6), `PlayTray` (→ 6), `FlatScreen` (→ 5), `SettingsPanel` (→ 5), `CanvasConversion` (→ 3), `ModalFallback` (→ 8) | **`MOVED`** per type |
| F5 | `VRRigDriver` — **partial class only**. `_camera` is touched by 7 groups, `_rigRoot` by 6; separate types would not be pure motion. | `MOVED` |

**Never split a method.** `ModalFallback.Tick`, `FlatScreenStereo`'s camera hooks and `BindConfig`
stay whole.

**`FlatScreenStereo`: "do the map/compositor separation and stop" is the recommended answer.** It
is ~1 950 lines of campaign-map renderer and ~1 400 of stereo compositor sharing almost no state,
so the one split is worth it; beyond that it is hardware-won plumbing and the charter's default
answer applies.

**Trap for execution:** moving a file is invisible to the guard — changing its **namespace** is
not. And a split is empty-diff only if each new file holds a *contiguous* run and compile order
matches, which is **filename-sensitive**. F1–F3 avoid this entirely; F4–F5 must be verified one
at a time.

## Batch G — Explicitly NOT doing

- Merging the one real verbatim duplicate pair (`StatPanelSurface`/`PropInfoSurface`) — it would
  make three hardware-tuned constants shared. Take the comment instead.
- Merging any of the 11 near-identical `Remote*` pairs.
- Deleting the CS0162 unreachable blocks. They are switched-off bisection experiments from the
  ~30-commit map hunt, and two sibling flags escape the warning only by sitting in ternaries.
  Deleting half of a mechanism is worse than leaving it. Treatment: one header comment naming all
  five flags + three *site-scoped* pragmas.
- Moving the 3 patch classes out of their driver files (that is precisely the documented failure
  mode; A1's inventory buys the discoverability instead).
- Touching serializer statement order, flag bit values or byte layout. Not one line.
- Reordering `RegisterModules` — and note the registry's claim that `CompatModule` is registered
  last is **false**, `DevModule` follows it. The code is right; the comment is imprecise.
- Anything Tier 3.

---

## Decisions I need from the user

1. **Legacy config entries** (7 in Cards + ~20 in WorldUI). Recommendation: **keep them bound,
   prefix each description with `LEGACY — no effect, superseded by <X>`**. Costs one line each,
   every existing `.cfg` keeps loading, and a misleading knob becomes an honest one. Deletion
   buys a shorter file and breaks a deliberate back-compat promise. *(Note: this shows a guard
   diff — descriptions are string literals, not comments. My earlier claim that it was free was
   wrong.)*
2. **`PlacementDiagnostics` trio** — author-marked TEMPORARY, observationally pure (traced
   through the decompiled source), cost is one raycast during placement, and they carry
   `[Placement]` grep tokens. Removing them needs a headset re-verification, not a code argument.
   Remove, or keep until the next placement question?
3. **`CombatLogSurface` minimum scale** — clamps to `0.5` in three places while every sibling
   panel uses the shared `0.15`, and one site *persists* a sub-0.5 pinch as 0.5. Its config text
   and class doc agree with the code, so it may well be deliberate — but it is the exact failure
   the shared constant's comment warns about. Deliberate, or a bug to file separately? **No change
   proposed either way**; this is Tier 3.
4. **`CacheTickDelegates` A/B harness** — 5 sites currently carry both paths so the hypothesis can
   be tested against the `[Perf]` counters on hardware. Confirm when that test is done, so the
   dual paths can collapse to the cached one. Until then nobody should "simplify" them.

## Execution rules for Phase 4

One subsystem per commit, one tier per commit, build green at every commit, guard output recorded
in `LOG.md` at every commit. Batches run **A → B → C → D → F**; E waits on decision 1. Any item
whose guard output does not match its expectation above is reverted, not argued with.
