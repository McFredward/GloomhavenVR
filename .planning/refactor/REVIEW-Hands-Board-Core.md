# Review — Hands / Board / Core / Compat / `Plugin.cs`

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

> Phase-1 deliverable per `CHARTER.md` §6. Companion to
> `INVARIANTS-Hands-Board-Core.md` (the veto list) — this document is the *proposal*
> list. Nothing here has been executed; no source file was modified.
>
> Scope audited: `src/GloomhavenVR/Hands/` (23 files, 6 850 lines),
> `Board/` (25 files, 4 626), `Core/` (27 files, 7 024 incl. the new
> `PerfMonitor.cs`/`PerfConfig.cs`), `Compat/` (3 files, 398), `Plugin.cs` (556).
> Total 21 454 lines. Full Release build run: **4 warnings repo-wide**
> (1 × CS0414, 3 × CS0162, all outside my subsystems except by reference).

---

## 0. Headline

These four subsystems are in good shape. The compiler finds almost nothing dead, the
duplication is deliberate and commented, and nearly every non-obvious decision already
carries its reason in-source. **The expected yield from moving code is therefore close
to zero, and I am recommending very little of it.**

The real exposure is different, and it is the one the charter's safety net cannot see:

| Failure mode | Caught by `refactor-guard.sh`? | Caught by the compiler? | Caught today by anything? |
|---|---|---|---|
| Reordering steps inside a driver `Update` | **no** (ordinary in-type diff) | no | **nothing** |
| A patch class losing its `PatchAll` reference | **no** (the class still compiles) | no | **nothing** (has happened twice) |
| `docs/PATCH-INVENTORY.md` drifting from source | n/a | no | **nothing** (has drifted: 17 vs ~58) |
| Two mirrored constants drifting apart | no | no | **nothing** |
| `docs/INTERFACES-P2.md` drifting from the "frozen" API | n/a | no | **nothing** (8 dead identifiers) |

Every one of those five holes can be closed by **text that never reaches the compiler**
— comments, a lock file, and three small scripts. That is the cheapest currency this
refactor has: the guard output for all of it is *literally empty*, because comments and
`scripts/` files do not exist in the compiled DLL.

So the ordering of this review is: **checkers first, deletions second, motion last.**

---

## 1. Findings ordered by value ÷ risk

Risk key: **nil** = cannot change behaviour by construction; **low** = compiler or
full-repo grep proves the blast radius; **medium** = needs a human to confirm a
non-mechanical claim.

| # | Proposal | Tier | Guard output | Risk |
|---|---|---|---|---|
| P1 | Generate `docs/PATCH-INVENTORY.md` from source + static wiring check | 0 | **empty** | nil |
| P2 | Frame-order marker comments + order lock + checker | 0 | **empty** | nil |
| P3 | Mirrored-constant lint (instead of the Tier-2 merge) | 0 | **empty** | nil |
| P4 | Four stale comments that name symbols/orderings that do not exist | 0 | **empty** | nil |
| P5 | Repair `docs/INTERFACES-P2.md` §PalmGate (5 dead identifiers) + P4 (3) | 0 | **empty** | nil |
| P6 | Delete `ItemsPile.ItemChip._hasClip` (CS0414) | 0 | 1 type, 1 field + 3 stores | nil |
| P7 | Delete `HandGhost.Engaged` / `HandGhost.RendererCount` | 0 | 1 type, 2 properties | nil |
| P8 | Retire `PalmGate.UseDevicePalmNormal` (+ Cards write, + P2 doc line) | 0 | 2 types, 1 field + 1 store | low |
| P9 | Extract `WallFadeTuning` out of `WallSegmentFade.cs` | 1 | **empty** | nil |
| P10 | Extract `DevModule` out of `Core/DevConsole.cs` | 1 | **empty** | nil |
| P11 | Split `PerfMonitor.XrProbe` / `PerfHost` into partial files | 1 | **empty** | nil |
| P12 | Split `Plugin.cs` config binding into a partial `PluginConfig.cs` | 1 | **empty** | nil |
| P13 | Split `UguiHoverTracker` / `DepthPortraitPicks` out of `UguiPointer.cs` | 1 | **empty** | nil |
| P14 | Unify the four `Degrade(reason)` helpers | 2 | 5 types | low — **recommend deferring** |
| — | §2: eleven things to leave alone, with the reason | — | — | — |
| — | §3: three decisions for the user (`PLAN.md`) | — | — | — |

---

## P1 — The Harmony patch surface (highest-value item in this scope)

### The problem, measured

- `docs/PATCH-INVENTORY.md` header says "Phase 5, MISSION A.11 … complete list" and
  lists **17** patched methods. The repository today declares **~58** patched-method
  declarations across **34** patch classes. Everything Board-side since Phase 5
  (`HexHoverClear`, `UIManager_IsPointerOverUI_Patch`, the three placement
  diagnostics, `InitiativeTrackPlayerAvatar_OnClick_Guard`, `HexHighlightFix`,
  `ActorBehaviour_HeldTransform_Patch`), all of `Compat`, `UIWindow_Transition_Patch`,
  `TakeDamagePanelSafety` (17 methods on its own) and `EscMenuInputBlock` are absent
  from it. The doc is not merely stale, it is *actively misleading* — it is cited by
  `CHARTER.md` §5 as the authority for "is this a patch target?".
- Two commits exist solely because a written patch was never registered:
  `8567af4 wire(compat): register WallFadeDisable in CompatModule.Init` and
  `57309c5 wire(compat): register InitialInputSkip patch in CompatModule.Init`.
  Nothing in the build, the compiler, or the guard would report it a third time.

I ran the audit by hand: **every patch class in the repository is currently registered
exactly once.** So this proposal is preventive, not corrective — which is exactly what
you want before a refactor starts moving files around.

### The mechanism: one script, derived from source, zero runtime code

`scripts/patch-inventory.py` (invoked from a thin `scripts/patch-inventory.sh`), with
two modes:

```
scripts/patch-inventory.sh generate   # rewrites docs/PATCH-INVENTORY.md
scripts/patch-inventory.sh check      # exit 1 on drift OR on an unregistered class
```

What it parses (all of it is regular, and I prototyped the parse during this review —
it produced the correct 34-class list on the first pass):

1. **Declared patch classes** — any type carrying a type-level `[HarmonyPatch…]`, or
   containing a member with `[HarmonyPatch…]`. Records file + line.
2. **Targets** — from the attribute arguments (`typeof(X)`, `nameof(X.Y)`, a string
   literal for privates, `MethodType.Getter`, the explicit argument-type array), or the
   literal token `TargetMethod` when the class resolves its own target reflectively.
3. **Patch kind** — `[HarmonyPrefix]`/`[HarmonyPostfix]`/`[HarmonyTranspiler]`/
   `[HarmonyFinalizer]`, falling back to the method-name convention Harmony itself uses.
4. **Owner** — the file containing `PatchAll(typeof(<class>))`, mapped to its module
   (`BoardModule` → Board, `CompatModule` → Compat, `EscMenuInputBlock` → WorldUI/lazy).
5. **Degrades-by-design flag** — the class declares a `TargetMethod` returning
   `MethodBase?` (today: `WallFadeDisable`, `InitialInputSkip`, `ShowUIWindowSuppressor`,
   `EscMenuEscapeSuppressor`). These are the classes that are *allowed* to end up
   patching nothing at runtime.

`check` fails when:

- a declared patch class has **no** `PatchAll(typeof(…))` reference anywhere
  → *"`X` declares Harmony patches but no module registers it — it is INERT
  (see INVARIANTS §9). Add `PatchAll(typeof(X))` to the owning module's `Init`."*
- a class has **more than one** `PatchAll` reference (double-apply);
- the generated inventory differs from the checked-in `docs/PATCH-INVENTORY.md`.

### Why static, not a runtime audit

I designed and then rejected a runtime audit
(`Harmony.GetAllPatchedMethods()` → `GetPatchInfo(m).Prefixes ∪ …` →
`patch.PatchMethod.DeclaringType`, differenced against the assembly's declared patch
types). It works, but it is strictly worse here:

- it would need to run *after* `EscMenuInputBlock.EnsureRegistered()`, which fires
  lazily from `InputModeGuard.Tick` on the first VR frame — so it cannot live in
  `Plugin.Awake` and would need its own scheduling;
- both historical misses (`WallFadeDisable`, `InitialInputSkip`) *do* declare
  `TargetMethod`, so a runtime audit could only Warn on them, not Error;
- it adds a new type to the DLL and thus a non-empty guard diff, for a class of bug the
  static check catches **at commit time instead of at HMD time**.

Static analysis sees every `PatchAll` call site in the repository, including the lazy
one. There is no case the runtime check catches that the static one misses.

If you still want a runtime confirmation, the right home is a `DevConsole` command
(dev-mode only, no per-frame cost) — listed in §3 as a user decision, not proposed here.

### Wiring

Add to the top of `refactor-guard.sh check` (before the build):

```bash
"$ROOT/scripts/patch-inventory.sh" check || { echo "error: Harmony patch surface drifted" >&2; exit 1; }
"$ROOT/scripts/check-frame-order.sh"      || { echo "error: frame ordering drifted" >&2; exit 1; }
```

so a Tier-1 file move that drops a `PatchAll` reference fails the same command every
refactor commit already runs.

- **Tier:** 0.
- **Files:** new `scripts/patch-inventory.py`, `scripts/patch-inventory.sh`;
  regenerated `docs/PATCH-INVENTORY.md`; 2 lines in `scripts/refactor-guard.sh`.
  **No `src/` file changes.**
- **Guard output:** **empty** — nothing reaches the DLL.
- **Risk if I am wrong:** the generated doc is inaccurate in a corner (e.g. an
  attribute form the parser does not know). Mitigation: the first `generate` run is
  reviewed against `INVARIANTS §13`, which is hand-verified and covers 20 of the ~58;
  a parse failure on an unrecognised attribute form should be a hard error, not a
  silent omission.
- **Note for §13 of the registry:** once generated, `INVARIANTS §13` should say
  "the generated inventory is the list; this table is the *contract* per patch" —
  the two documents answer different questions and both should survive.

---

## P2 — Frame ordering: make an accidental reorder loud

### Why the compiled-form guard cannot help

A reorder of five statements inside `BoardDriver.Update` is an ordinary diff inside a
type the refactor legitimately touched. `refactor-guard.sh` reports it as "confined to
`BoardDriver`" — which is the *pass* condition for a Tier-1 motion. The guard is
structurally blind here, and this is the only class of regression in my scope that can
ship silently.

### Mechanism: a marker comment + a lock file + a text checker

Add above each ordered block one marker line the checker can parse, and check the whole
set into `.planning/refactor/FRAME-ORDER.lock`:

```csharp
// FRAME-ORDER VRHand.Update [Poke, Ray, RayUgui, RayGrab, Grabber, PalmGate]
//   Each adjacency is an arbitration decision — INVARIANTS §6. Reordering is Tier 3.
```

`scripts/check-frame-order.sh` then enforces **two independent things**:

1. the tokens in the bracket list occur, in that order, in the source lines that
   follow the marker (catches "I tidied the Update method");
2. the bracket list is byte-identical to the entry in `FRAME-ORDER.lock`
   (catches "I tidied the Update method *and* the comment").

Both are pure text; neither reaches the compiler. Two levers must be pulled in
agreement to defeat it, which is exactly the "loud" property asked for.

### Per-site recommendation

Thirteen ordering constraints exist in my scope. Not all want the same mechanism.

| # | Site | Constraint | Cheapest mechanism |
|---|---|---|---|
| 1 | `VRHand.UpdateBody` (interactor block) | Poke → Ray → RayUgui → RayGrab → Grabber → PalmGate | **marker + lock** (comment already excellent; the marker makes it machine-checked) |
| 2 | `VRHand.UpdateBody` (pre-block) | device read → velocity → pose class. → curl targets → `_curler.Tick` → interactors | **marker + lock** |
| 3 | `BoardDriver.Update` | SyncRayMask → CameraArrival → Click → Aoe → Targeting, all in `Update` (before the game's `Controller.LateUpdate`) | **marker + lock**; the existing comment already states the order verbatim — the marker is a 1-line edit of it |
| 4 | `FigureGrabDriver.Update` | Ghosts → Glide → *[config gate]* → Registry → AutoRelease → OffsetAnchorSelect → LaserGrab; Ghosts+Glide **before** the gate | **marker + lock**, with the gate as an explicit token: `[Ghosts, Glide, GATE:GrabFigures, Registry, AutoRelease, OffsetAnchorSelect, LaserGrab]` |
| 5 | `FigureGrabDriver.LateUpdate` | must be `LateUpdate`, after the Animator | **method-name assertion**: `// FRAME-ORDER LateUpdate-required [HeldFigures.PinAnimatedRoots, NetHeldFigures.PinAnimatedRoots, FigureRingSuppressor.Tick]` — the checker asserts all three symbols appear inside a `LateUpdate` body in that file. This is the one that a "merge Update and LateUpdate for symmetry" change would break. |
| 6 | `WallSegmentFade.FadeDriver.LateUpdate` | `LateUpdate` on purpose (renderer lists + head pose final) | **method-name assertion**, same form |
| 7 | `VRRigDriver._tailSteps` | MixedReality **last** | **marker + lock** on the array literal. *Cross-boundary:* the array lives in `Rig/` (Net/Rig reviewer's file) but the invariant belongs to `Core.MixedReality`. Recommend the Rig review own the marker and this review own the lock entry; flag it in `PLAN.md` so it is not dropped between the two. |
| 8 | `MixedReality.Tick` | `SkyBackdrop.Tick(mrHidingSky: true)` **before** `HideSkyGeometry`; `false` branch after the early-out | already stated in-source at both branches — **no change**, add the lock entry only |
| 9 | `RayInteractor.Tick` | `ComputeFanOccluder` **before** the physics raycast, so all three consumers see one value; `UpdateVisuals` last | already stated in-source — **no change** |
| 10 | `UguiPointer.TryRaycast` | the depth-portrait `UiHitOverride` write must come **after** `RayUguiDriver`'s flat point | already stated in-source — **no change** |
| 11 | `HandsDriver.TearDown` | `HandGhosts.Shutdown()` **before** `Destroy(_handsRoot)` | already stated in-source — **no change** |
| 12 | `HandVisuals` | sockets created **before** `ApplyStyleScale` | already stated in-source — **no change** |
| 13 | `FigureOverlay.BuildFrozenGhost` | ParticleSystems destroyed **before** their renderers; `mb.enabled = false` **before** `Destroy(mb)` | already stated in-source — **no change** |

Sites 8–13 are single-method-local orderings with the reason written at the code. A
lock entry adds nothing a reader would miss; sites 1–7 are *lists of calls*, which is
the shape a tidy-up refactor rewrites.

### `[DefaultExecutionOrder]` — my recommendation is **do not add any**

There are two places where a cross-`MonoBehaviour` `Update` order *looks* load-bearing:

- `BoardDriver.SyncRayMask` writes `hand.Ray.Mask`; `VRHand.Update` reads it. Different
  GameObjects, so Unity's order is undefined.
- `FigureGrabDriver.TickLaserGrab` writes `hand.Ray.UiHitOverride`; consumers read it
  through `HasFreshUiHit`.

In both cases the code is deliberately **order-independent**: the ray mask is sticky
across frames (a one-frame-stale mask is the same mask), and `HasFreshUiHit`'s window is
`<= 1` frame *precisely so producers and consumers may sit in either phase*
(INVARIANTS §1, "The freshness window is TWO frames"). Adding
`[DefaultExecutionOrder]` would **freeze an order the code does not currently rely on**
— which is a behaviour change (Tier 3) and, worse, would let a future edit start
depending on it and thereby narrow the 2-frame window's job without anyone noticing.

Precedent exists and is correctly scoped: `PerfMonitor.PerfHost` carries
`[DefaultExecutionOrder(-30000)]` because it genuinely must sample before every mod
driver. That is the only site in the mod where the attribute is the right tool.

**Action instead:** one comment at each of the two sites saying the order is
deliberately not relied on, and *why* (stickiness / the 2-frame window). Guard: empty.

- **Tier:** 0 (comments + scripts + a lock file only).
- **Files:** ~7 comment edits across `VRHand.cs`, `BoardDriver.cs`,
  `FigureGrabDriver.cs`, `WallSegmentFade.cs`, `VRRigDriver.cs` (cross-boundary);
  new `scripts/check-frame-order.sh`, new `.planning/refactor/FRAME-ORDER.lock`.
- **Guard output:** **empty**.
- **Risk if I am wrong:** a checker false positive blocks a legitimate commit. Cheap
  to fix (edit the lock); no way for it to hide a real reorder.

---

## P3 — Mirrored constants: lint them, do not merge them

`INVARIANTS §15` lists two mirrored-constant pairs as "Tier 2 and safe". I verified both
and **recommend against merging**, with a cheaper alternative.

Verified identical today:

| Pair | Values | Comment present? |
|---|---|---|
| `ProximityGrabber.ReachMeters` ↔ `FigureGrabDriver.ReachMeters` | `0.13f` ↔ `0.13f` | yes, on the Board side, naming the Hands side |
| `PokeInteractor.FingertipRadius`/`ReleaseRange` ↔ `BoardClickDriver.ContactDepth`/`ReleaseDepth` | `0.008f`/`0.02f` ↔ `0.008f`/`0.02f` | yes, in `INVARIANTS §3` and in the Poke doc |

Merging costs more than it buys:

- both pairs straddle **Hands ↔ Board**. A shared constant means either Board depends on
  a Hands `private const` being promoted to `internal` (leaking an interactor detail into
  the frozen P2 surface), or a new `Core` constants file that neither owner reads
  naturally. Both make the layering *worse*, and the second guarantees nobody finds the
  value when tuning.
- the failure mode is not "there are two constants", it is "someone tunes one". A shared
  constant fixes that; so does a three-line lint, at nil risk and with the layering intact.

**Proposal:** extend `scripts/check-frame-order.sh` (or a sibling
`scripts/check-mirrors.sh`) with a mirror table:

```
ProximityGrabber.ReachMeters == FigureGrabDriver.ReachMeters
PokeInteractor.FingertipRadius == BoardClickDriver.ContactDepth
PokeInteractor.ReleaseRange   == BoardClickDriver.ReleaseDepth
```

grep both literals, fail on inequality with a message naming the invariant. Guard:
**empty**. Risk: nil.

---

## P4 — Stale comments that name things which do not exist

All four are documentation drift with zero behavioural content. Guard output for the
whole group: **empty** (comments are not compiled).

1. **`Compat/CompatModule.cs:111`** — *"WallFadeDisable is a Harmony patch reverted by
   VRSession's UnpatchAll on hot-reload"*. There is no `VRSession.UnpatchAll`; the
   actual mechanism is `Plugin.OnDestroy → _harmony.UnpatchSelf()` (INVARIANTS §9).
2. **`Compat/WallFadeDisable.cs:45`** — *"reversible on hot-reload via Harmony
   `UnpatchAll`"*. Same correction. `UnpatchAll` and `UnpatchSelf` are different Harmony
   APIs with different blast radii; naming the wrong one in the doc of the patch whose
   *whole design* is "degrade cleanly" is worth fixing.
3. **`Plugin.cs:174-176`** — *"Order matters: Core first (XR bootstrap), Compat last
   (fixups on top of everything else)"* — but `RegisterModules` appends
   `Core.DevModule` **after** `Compat.CompatModule`. So does the `CompatModule` class
   doc (*"registered last on purpose"*), and so does `INVARIANTS §12`
   (*"`CompatModule` is registered LAST"*).
   **Ruling: the code is correct and the comment is imprecise, not the reverse.**
   `DevModule.Init` returns immediately unless `[Dev] Enabled`, and installs only a
   `DevConsole` overlay GameObject — it applies no fixups, patches nothing, and touches
   no game state, so it cannot get between Compat and anything. Reword to *"Compat last
   among the feature modules; the Dev harness is appended after it and is inert unless
   `[Dev] Enabled`."* and correct the same claim in `INVARIANTS §12`.
   **Do not reorder `RegisterModules` to match the comment** — that would be a Tier-3
   change to module init order for a cosmetic reason.
4. Two comments correctly document *removed* symbols and should be **kept** —
   `RayInteractor.cs:41/411` and `BoardDriver.cs:41` name `ReticleOverride` /
   `SyncReticleSnap`, which no longer exist. That is the point (INVARIANTS §7,
   "a reticle override is reintroduced in any form" is the break condition). A future
   "clean up references to non-existent symbols" pass must not eat these; the checker in
   P2 could optionally whitelist them, but a comment marking them `// (removed on
   purpose — do not resurrect)` is enough.

---

## P5 — `docs/INTERFACES-P2.md` is stale in the section the registry leans on

`CHARTER.md` §5 and `INVARIANTS §15` both use "part of the frozen Phase-2 API
(`docs/INTERFACES-P2.md`)" as a *reason not to delete* (`HandRig.PalmNormal`,
`HandPose.OpenPalm`). I checked the doc against the code mechanically.

**8 identifiers named in the interface docs no longer exist anywhere in `src/`:**

- `INTERFACES-P2.md`: `EnterThreshold`, `ExitThreshold`, `RollAxisOnly`,
  `DisableAllMouses`, `TrayGrabHandle`
- `INTERFACES-P4.md`: `SeatedMode`, `VignetteEnabled`, `VignetteStrength`

The concentration is the **PalmGate section** (`INTERFACES-P2.md:229-243`), which is
wholesale a description of the superseded P6/P7 gate:

- it documents `EnterThreshold`/`ExitThreshold` — the fields are now
  `EnterDegrees`/`ExitDegrees`;
- it documents thresholds *"defaults 0.6/0.35 = the P2 constants"* as **dots** — the
  gate has read **degrees** since roll gate v3 (`60`/`45`);
- it documents `RollAxisOnly` — removed;
- it documents `[Cards] SupinationThreshold` — the key no longer exists;
- it documents `[Hands] GripPitchOffsetDegrees (default -60°)` — the default is `-30f`
  since hardware test #27;
- it describes `UseDevicePalmNormal (default true)` as *"evaluates the RAW grip-pose
  palm instead of the visual rig"* — which is **the exact opposite of what the code
  does today** (roll gate v4 always reads `HandRig.Root`; see P8).

Last touched at `101afb6`, i.e. before `d7ec01c` (v3) and `6b5c38c` (v4).

**Proposal:** rewrite the PalmGate section from `PalmGate.cs`'s own class doc (which is
accurate and thorough), and delete/fix the 3 other dead identifiers. Add the P2/P4 doc
files to the identifier-existence check in the P2 script so it cannot drift again — the
same 20-line parse already used for the patch inventory.

- **Tier:** 0 — docs only. **Guard: empty.** **Risk: nil.**
- **Consequence for §15:** with the PalmGate section corrected, "it is documented frozen
  API" survives as an argument for `PalmNormal`/`OpenPalm` (both still appear in the
  accurate parts of the doc) but stops being an argument for `UseDevicePalmNormal` — the
  doc's description of it is simply wrong. That is what makes P8 clean.

---

## P6 — `Cards/ItemsPile.ItemChip._hasClip` (the CS0414)

**Ruling: delete. It is a leftover of the clip/unclip rework, and the comment next to
one of its assignments says so in as many words.**

Evidence:

- Declared `ItemsPile.cs:1070`. Assigned in exactly three places — `ClipIntoSlot`
  (:1725), `ReturnToFan` (:1758), `PlayUseThenCollapse` (:1771). **All three assign
  `false`.** It is never assigned `true`, never read, and never compared.
- The `ClipIntoSlot` assignment carries the epitaph:
  `_hasClip = false;   // no chase target any more — the hierarchy owns the pose`.
  That is Requirement 6's rework: the chip used to be *chased* toward a per-tick target
  (which "made it swim behind head movement") and is now **re-parented** onto the slot.
  `_hasClip` was the "a chase target exists" flag; the chase went, the flag stayed.
- §5 sweep: not a Unity message, not serialized (private field on a nested non-serialized
  class), not reflected, not a config key, not a log token, not reachable from the debug
  menu.

- **Tier:** 0. **Files:** `src/GloomhavenVR/Cards/ItemsPile.cs` — remove the declaration
  and the three `_hasClip = false;` statements (keeping the surrounding comments, which
  document the re-parent decision and are worth more than the field ever was).
- **Guard output:** confined to `ItemsPile` (nested `ItemChip`) — one field gone, three
  stores gone. Nothing else.
- **Risk:** nil — the compiler proves there is no read.
- **Ownership:** this is a Cards file. Execute it in the **Cards** commit, not a
  Hands/Board/Core one (CHARTER §7, one subsystem per commit).

---

## P7 — `HandGhost.Engaged` and `HandGhost.RendererCount`

**Ruling: delete both.** I re-verified independently rather than trusting §15.

- Full-repo grep for `Engaged` returns `HandGhost.cs:87` (the declaration) and eleven
  hits in `WorldUI/HexHintFacing.cs` — an **unrelated** `public bool Engaged` field on a
  different type. `RendererCount` returns exactly one hit: the declaration.
- Both are `internal` members on an `internal` class; there is no external assembly.
- `HandGhost` is constructed from `HandGhosts`, `AvatarMirror` and
  `Net/RemoteAvatar.cs:257` — I checked all three; none reads either member, and neither
  appears in any log format string.
- §5 sweep: not Unity messages, not serialized, not reflected, not config, not log
  tokens, not debug-menu-reachable (`DevConsole` prints `PalmGate.CurrentDot` and pose,
  not ghost state).

- **Tier:** 0. **Files:** `Hands/HandGhost.cs`, two expression-bodied properties.
- **Guard output:** confined to `HandGhost` — two properties removed.
- **Risk:** nil.
- **Small caveat worth honouring:** `Engaged` is a one-line, self-documenting mirror of
  `_renderers != null` and the natural thing a future debug line would print. If you want
  to keep an inspection hook, keep `Engaged` and drop only `RendererCount` (the fade
  *does* already report its renderer count in the engage log, which is the invariant
  §5 requires). Either choice is Tier 0; I lean to deleting both, because "we might log
  it later" is how the other 200 unread members in a codebase are justified.

---

## P8 — `PalmGate.UseDevicePalmNormal` (handed over from the Cards review)

**Ruling: confirmed vestigial. Retire it — but as a deliberate cross-module item, not
an inline Tier-0 delete.**

Verification (independent of `6b5c38c`'s claim and of §15):

- `PalmGate.cs:101` declares `public bool UseDevicePalmNormal = true;`.
- **`PalmGate.cs` never reads it.** `Tick` unconditionally does
  `Transform? root = _hand.Rig?.Root; Quaternion frame = root != null ? root.rotation :
  _hand.transform.rotation;` — the device transform is a *null-rig fallback*, not a
  flag-selected mode. The flag's entire historical job (choose the raw grip-pose palm
  over the visual rig) was deleted when roll gate v4 landed.
- The only other reference in the repository is `Cards/CardsDriver.cs:1298`:
  `gate.UseDevicePalmNormal = !_gateHand.IsSimulated;` — a write to a field nobody reads.
- The field's own XML doc already says "VESTIGIAL since roll gate v4 … kept so the Cards
  driver's per-frame assignment stays source-stable", and the class doc repeats it. The
  stated reason to keep it is *literally* "so that a dead write keeps compiling".
- §5 sweep: `public` on an `internal` class ⇒ no external surface; not serialized (plain
  class, not a `MonoBehaviour`); not reflected; not a config key (`[Cards]
  SupinationThreshold`, the config that once fed the neighbouring thresholds, is itself
  gone); not a log token; not debug-menu-reachable.
- One live reference outside code: `docs/INTERFACES-P2.md:236` describes it — and
  describes it **wrongly** (see P5). Removing the field and repairing that paragraph are
  the same edit.

- **Tier:** 0 by evidence, but it is a **three-file, two-subsystem** change
  (`Hands/Interact/PalmGate.cs`, `Cards/CardsDriver.cs`, `docs/INTERFACES-P2.md`), which
  CHARTER §7 says must not be smeared across a Hands commit.
- **Files / exact change:** delete the field + its 7-line doc comment; delete the one
  assignment line in `CardsDriver.Tick` (and the trailing comment
  `// sim hands pose the rig directly`, which describes the removed selection); rewrite
  the `UseDevicePalmNormal` sentence in `INTERFACES-P2.md` §PalmGate as part of P5.
  Amend the `PalmGate` class-doc sentence that currently ends *"(which v3 read via
  `UseDevicePalmNormal` — that flag is now vestigial…)"* to name v3 without the symbol,
  so the historical record survives the deletion.
- **Guard output:** two types — `PalmGate` loses one public field;
  `CardsDriver` loses one store from `Tick`. Nothing else. *Note:* because `PalmGate`'s
  field is `public`, the field genuinely disappears from the type's public surface in the
  decompile; that is expected, not collateral.
- **Risk: low.** The one way to be wrong is a reflective consumer. I searched for
  `"UseDevicePalmNormal"` as a string literal repo-wide: no hits. The mod does not
  reflect over its own types anywhere except `RuntimeDepsLoader` (assembly hooks) and
  `PerfMonitor.XrProbe` (XR display stats) — neither touches `Hands`.
- **Sequencing:** land it *with* the Cards subsystem commit, since that is where the
  second file lives and the Cards review already owns the finding.

---

## P9–P13 — Motion (all Tier 1, all expected to produce an **empty** guard diff)

> **A trap that must be written into `PLAN.md`:** `ilspycmd -p` groups output by
> **namespace and type**. Moving a type to a different *directory* is invisible to the
> guard; moving it to a different **namespace** is a delete-plus-add in the snapshot and
> a real change to the type's full metadata name. **Every file move below keeps the
> namespace exactly as it is**, including where that means a file under `Board/Patches/`
> declaring `namespace GloomhavenVR.Board`.

I found only five motions worth doing. Ordered by value.

### P9 — `WallFadeTuning` out of `WallSegmentFade.cs`

`Core/WallSegmentFade.cs` is 1 311 lines and holds four types:
`WallFadeTuning` (19–206), `WallSegmentFade` facade (207–246), `Segment` (247–291),
`FadeDriver` (292–1310).

`WallFadeTuning` is ~190 lines of **config binding and tuning constants** — the same
role `BoardConfig.cs`, `FigureGrabConfig.cs`, `HandsConfig.cs` and `PerfConfig.cs`
already occupy in their own files. It is the one type here whose cohesion argument is
independent of length.

- **Change:** move `WallFadeTuning` verbatim to `Core/WallFadeTuning.cs`, namespace
  unchanged. `WallSegmentFade.cs` drops to ~1 120 lines and contains only the fade
  algorithm.
- **Guard:** empty. **Risk:** nil.
- **Explicitly not proposed:** splitting `FadeDriver` (1 020 lines) along its four
  existing `// ----` section banners into partials. It would also be an empty-guard
  motion, but the class is one algorithm with heavily shared private state
  (`_roomFloorY`, `_roomSampleStart/_roomSampleCount`, `_occludedTex`, the sample
  arrays), and the banners already give a reader the map. Per CHARTER §2 the burden of
  proof is on the change and I cannot meet it.

### P10 — `DevModule` out of `Core/DevConsole.cs`

`Core/DevConsole.cs` declares `DevModule` (an `IVRModule`, lines 16–46) and then
`DevConsole` (the overlay `MonoBehaviour`). Every other module in the mod lives in a
file named after it (`CoreModule.cs`, `HandsModule.cs`, `BoardModule.cs`,
`CompatModule.cs`, `VREventsModule.cs`). `DevModule` is the only one you cannot find by
filename — and it is the module referenced in `Plugin.RegisterModules`, i.e. exactly the
one a reader chasing module order (see P4.3) goes looking for.

- **Change:** move `DevModule` verbatim to `Core/DevModule.cs`, namespace unchanged.
- **Guard:** empty. **Risk:** nil.

### P11 — `PerfMonitor.XrProbe` / `PerfHost` into partial files

`Core/PerfMonitor.cs` (905 lines, new this pass) is well-structured, but the last ~230
lines are two nested types with a different concern from frame pacing:
`PerfHost` (the `MonoBehaviour` pump) and `XrProbe` (a reflection shim binding
`XRDisplaySubsystem` stat delegates — dropped/presented frames, GPU time,
motion-to-photon, refresh).

- **Change:** `internal static partial class PerfMonitor` in three files —
  `PerfMonitor.cs`, `PerfMonitor.Host.cs`, `PerfMonitor.XrProbe.cs`. The nested types
  stay nested, so metadata is byte-identical.
- **Guard:** empty. **Risk:** nil.
- **Confidence this is worth it: medium.** It is new, cohesive code that nobody is
  currently lost in. I would schedule it last, or not at all, and would not object to a
  "leave it" call.

### P12 — `Plugin.cs` config binding into a partial

`Plugin.cs` is 556 lines, of which ~370 are `Config.Bind` calls and their (long,
valuable, load-bearing) description strings, plus `BindHandStyleEntries`. The lifecycle
— `Awake`, `OnDestroy`'s reverse-order shutdown, `RegisterModules`, `InitModules`,
`DelayedInit`, `LogStartupSummary` — is ~130 lines buried underneath.

Those lifecycle methods carry three separate invariants (§9 reverse shutdown order,
§12 module registration order, the `IF THIS IS NOT THE COMMIT YOU EXPECTED` grep token)
and are the part of this file anyone ever needs to read.

- **Change:** `public partial class Plugin` split into `Plugin.cs` (lifecycle,
  ~150 lines) and `PluginConfig.cs` (the `ConfigEntry` fields + `Awake`'s binding block
  moved verbatim into a `BindConfig()` called as the first statement of `Awake`).
- **Guard:** empty **only if** the binding block is moved *whole and in order* into one
  method called first — config binding order determines the order sections/keys are
  written to a fresh `.cfg`, and `Awake`'s early-out on `!Enabled.Value` must still sit
  after every `Bind` call. If you extract more than one method, the guard shows
  `Plugin` changed and you have to justify it.
- **Risk: low, and higher than the others in this group.** `Config.Bind` order is
  observable in the user's config file. Recommend: extract exactly one method, and
  record the guard as "empty" — if it is not empty, revert rather than argue.

### P13 — `UguiHoverTracker` / `IDepthPortraitPicker` / `DepthPortraitPicks` out of `UguiPointer.cs`

`Hands/Interact/UguiPointer.cs` (702 lines) ends with three types that are *registries
consumed by* `UguiPointer` rather than parts of it: `UguiHoverTracker` (611+),
`IDepthPortraitPicker` (661), `DepthPortraitPicks` (678). `UguiPokeSurfaces`,
`DeliberatePokeSurfaces` and `VRInteractables` — the mod's other three interaction
registries — already live in their own files.

- **Change:** move the three to `Hands/Interact/UguiHoverTracker.cs` and
  `Hands/Interact/DepthPortraitPicks.cs`, namespace unchanged.
- **Guard:** empty. **Risk:** nil.
- **Caution to carry in the commit message:** `UguiHoverTracker`'s instance-ID keying and
  its `is null` (not `== null`) checks are load-bearing (INVARIANTS §2, fake-null
  discipline). A verbatim move preserves them; a "while I'm here" tidy does not. The
  file's own doc comment should travel with it.

---

## P14 — The four `Degrade(reason)` helpers (Tier 2 — **recommend deferring**)

Four patch classes implement the same "log the first failure, then stay silent, return
null" helper:

| Class | Body |
|---|---|
| `Compat/WallFadeDisable.Degrade` | `if (!_degraded) { _degraded = true; VRLog.Warn("WallFadeDisable", $"disabled — {reason}. Wall see-through fade left vanilla."); } return null;` |
| `Compat/InitialInputSkip.Degrade` | identical, tag `"InitialInputSkip"`, tail `"'Press any key to continue' screen left vanilla."` |
| `WorldUI/Patches/EscMenuInputBlock.Degrade` | identical, tag `"EscMenuInputBlock"`, tail `"Game's controller ESC-menu paths left vanilla (X may double-act)."`, `void` instead of `MethodBase?` |
| (`ShowUIWindowSuppressor` / `EscMenuEscapeSuppressor` both delegate to the third) | — |

The diff between copies is **two string literals and a return type**. By CHARTER §4 that
is a legitimate Tier-2 merge with a provable nil difference.

**I recommend not doing it, at least not now**, for three reasons:

1. The value is ~30 lines across two subsystems (Compat + WorldUI), and it would put a
   new shared type in `Core` that both must reference — a coupling for a logging idiom.
2. The one thing the shared helper would genuinely buy — a machine-readable "this patch
   may legitimately be inert" marker for the P1 audit — is already available for free:
   **the presence of a `TargetMethod` returning `MethodBase?` is exactly that marker**,
   and P1 uses it.
3. Guard output would be five types (three losing a method, one gaining, plus the two
   suppressors' call sites), which is a lot of blast radius for a cosmetic win.

If you do want it later: the right shape is `Core.PatchDegrade.Once(ref bool flag,
string tag, string reason, string tail)`, and the commit message must carry the
four-way side-by-side diff CHARTER §4 requires.

---

## 2. Leave these alone — with the reason

Eleven things I looked at hard and am recommending **no action** on. "Leave this alone
because X" is a finding; each of these would otherwise be picked up by a later pass.

1. **`FigureGrabbable.ApplyRenderOnTop`'s unreachable block** (+ `HeldRenderQueue`,
   `_heldRenderers`, `_origSharedMats`, `RestoreRenderers`). Re-verified: the `return;`
   guards a `#pragma warning disable CS0162` block, `RestoreRenderers` is still called
   from two release paths (`:432`, `:528`) and is idempotent, so grab/release symmetry is
   real code, not a corpse. Reverted deliberately by `4ef1d76` because the queue bump
   punched minis through walls *and persisted after release*. **Keep — it is a
   documented, reversible, tested-and-rejected decision.** Note that the CS0162
   suppression is why this does not appear in the four-warning build; a future
   "eliminate all `#pragma warning disable`" pass must skip it.

2. **`MixedReality.KeepMenusUnclipped`** (empty body, two `ModalFallback` call sites).
   Verified: both call sites exist (`ModalFallback.cs:800`, `:1135`) and pass real
   arguments. The comment block above it (`MixedReality.cs:120-137`) is the only record
   of why the sky is no longer disabled for a floated menu, and states that
   re-implementing it *is* the regression. **Keep.**

3. **`FingerCurler.StyleCurlScale`** (`{1f, 1f, 1f}`). Read every frame at
   `FingerCurler.cs:94` — it is **not** unreferenced code, it is a live lookup whose
   current values are all-1. **Keep.**

4. **`Plugin.Experimental3DMap`**. A bound `ConfigEntry` ⇒ a persisted user setting
   (CHARTER §5). Its 9-line description is the only surviving record of the test-#8
   decision to keep the campaign map flat, and `VRRigDriver.cs:394` carries the matching
   comment. **Keep.**

5. **`RayInteractor.Mask = Physics.DefaultRaycastLayers`**. Overwritten by
   `BoardDriver.SyncRayMask` only while a `Controller` exists — i.e. never in menu
   scenes or `[Dev]` mode. **Keep.**

6. **`HandRig.PalmNormal`** (unreferenced, one-line `=> PalmCenter.up`). Genuinely
   unread — but it is the *named* expression of the palm-frame contract that
   `HandVisuals.FillMissingAnchors`' 180° Z flip exists to satisfy (INVARIANTS §5), it is
   in the accurate part of `INTERFACES-P2.md`, and it costs nothing. Deleting it removes
   a contract statement to save a line. **Keep.**

7. **`HandPose.OpenPalm`** (assigned, never compared). The assignment is what makes
   `UpdatePoseClassification` **total** — remove it and the classifier silently reports
   `Idle` for a flat open hand, which is a behaviour change, not a cleanup. The enum
   member is frozen P2 API. **Keep both.**

8. **`SkyBackdrop.RemoveEffects` vs `FullReset`.** §15 lists this under "looks
   redundant, is not", which understates it: `FullReset` **calls** `RemoveEffects` and
   then additionally forgets the sphere, the mechanism decision and the reset material.
   They are already correctly factored — there is no duplication to resist. **No action;
   suggest softening the §15 wording so a future reviewer does not go looking.**

9. **`VRCameraPolicy.PruneDead` vs `MixedReality.PruneDead`.** Diffed: different
   dictionaries (`Dictionary<Camera, StereoTargetEyeMask>` vs
   `Dictionary<Camera,(CameraClearFlags,Color)>`), and MR's additionally prunes
   `HiddenSky` and resets three scan/log latches. The only genuinely identical part is
   the six-line "collect Unity-null keys into a scratch list, then Remove" idiom — which
   would have to become a generic helper to be shared, adding generic instantiation to a
   scene-load path to save twelve lines. **No action.**

10. **The `VRLayers.Apply` calls in `HandVisuals.Build`, `RayInteractor.CreateVisuals`,
    `SkyBackdrop.EnsureResetObject`.** Each object is created *after* the tree-wide sweep
    (INVARIANTS §10). Deduping them is precisely the bug `7d9a338` fixed. **No action.**

11. **The patch classes that do not live in a `Patches/` folder** —
    `Controller_CommonLoop_Patch` (inside `Board/BoardClickDriver.cs`),
    `HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch` (nested),
    `ActorBehaviour_HeldTransform_Patch` (in `Board/FigureGrab/`). Tidying these into
    `Board/Patches/` is superficially a Tier-1 motion, but INVARIANTS §14.7 records that
    a separate wiring commit is a *real* failure mode and that "Tier-1 file moves must
    carry the `PatchAll(typeof(X))` reference with the class". Moving three patch classes
    to buy folder tidiness, in the exact way the registry warns about, is a bad trade.
    **Leave them where they are — P1's generated inventory gives you the discoverability
    the folder was going to buy, at nil risk.**

Also worth recording, though it is not mine to decide: the
**`PerfConfig.CacheDelegates ? _cached ??= F : F`** idiom appears at 5 sites
(`FigureGrabDriver` ×4 via a local, `SelectionReadyHighlighter`, `BoardPing`,
`VRRigDriver`). It is an A/B harness for the 2026-07 perf pass, not a permanent design.
A `TickGuard.Run(name, ref Action? cache, Action fn)` overload would collapse it, but
that changes IL at every site to remove a toggle that is probably about to be deleted
outright. **No action now** — see §3.

---

## 3. For `PLAN.md` — decisions that are the user's, not mine

1. **The three `PlacementDiagnostics` patch classes.** I verified the mechanical claims
   independently:
   - all three are marked `TEMPORARY … remove after the placement flow is confirmed on
     HMD`, and the three root causes they were written for are fixed (`96d8351`,
     `a6e2739`, `2253b0c`);
   - they are **observationally pure**. `Placement_Hover_Diagnostics` and
     `Placement_Click_Diagnostics` only read statics. `Placement_UpdateGate_Diagnostics`
     calls `__instance.Interactable()`, which I traced through the decompiled source:
     `Interactable()` → `InteractableUnderMouse()` → `MF.FindInteractableAtMousePosition`,
     which is a pure `Camera.main.ScreenPointToRay` + `Physics.Raycast` +
     `GetComponentInParent` — **no state mutation anywhere on that path**. So the cost is
     exactly one extra raycast per frame, only while `WaitingForCardSelection` **and**
     display == `CharacterPlacement`;
   - the blocker is not code, it is evidence: `[Placement]` is a grep token the hardware
     reports use (INVARIANTS §15), and hero placement is the flow that cost three
     separate root causes.
   **Recommendation:** remove all three, in one commit, *after* the user confirms one
   clean HMD placement pass. Guard: three types disappear plus three lines from
   `BoardModule.Init`. Do **not** remove them piecemeal — `Placement_Hover_Diagnostics`
   owns the `TileName` helper the other two call.

2. **`PalmGate.UseDevicePalmNormal` (P8)** — cross-module, so it needs a scheduling
   decision: Cards commit or a dedicated two-file commit. My recommendation: Cards.

3. **`PerfConfig.CacheTickDelegates`** — is the 2026-07 perf pass finished? If the
   toggle is going to be pinned true and deleted, the five `? _cached ??= F : F`
   expressions collapse to plain cached delegates as part of *that* change (Tier 3,
   config removal), and no refactor should touch them first. If it is staying as a live
   knob, leave it alone too. Either way: **not a refactor item**, but it should be
   recorded so a later reviewer does not "simplify" a measurement harness.

4. **`INVARIANTS §12` correction** — "`CompatModule` is registered LAST" is not literally
   true (`DevModule` follows it). See P4.3 for the precise wording; the *intent* of the
   invariant is sound and should survive.

---

## 4. What I did not find

Recorded so the next pass does not redo the search:

- **No dead code beyond the four items above.** A full Release build produces
  4 warnings; 3 of them (`CS0162` in `WorldUI/FlatScreenStereo.cs`) are outside my
  scope. Every "suspected vestigial" entry in §15 either turned out to be live
  (`StyleCurlScale`, `RayInteractor.Mask`, `FigureGhosts.Ghost.Pos/Rot`), deliberate
  (`ApplyRenderOnTop`, `KeepMenusUnclipped`, `Experimental3DMap`), or is P6–P8.
- **No verbatim duplication worth merging.** I diffed all six §15 "looks redundant"
  pairs plus the four `Degrade` helpers. The only byte-identical bodies in the whole
  scope are the `Degrade` trio (P14, deferred) and the six-line fake-null prune idiom
  (item 9 above, rejected). The mirrored *constants* are real and are better handled by
  a lint than a merge (P3).
- **No misleading names.** The one candidate — `PalmGate.CurrentDot`, which returns
  degrees, not a dot product — already carries an explicit doc note saying the name is
  kept for the frozen P2 surface and the unit changed. Renaming it would be a two-file
  change (`DevConsole` prints it) for no reader benefit. Leave it.
- **No layering violations.** Board reads Hands (one direction), Core is read by all,
  Compat reads Core, nothing reads Compat. The two Hands↔Board couplings are the
  mirrored constants (P3) and `FigureGrabDriver`'s use of `Ray.UiHitOverride` /
  `Grabber.ForceGrab` — both through the frozen P2 surface, which is what it is for.
