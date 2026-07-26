# Review — `src/GloomhavenVR/WorldUI/`

> Phase 1 output. Companion to `CHARTER.md` (rules, risk tiers) and
> `INVARIANTS-WorldUI.md` (244 load-bearing behaviours — the constraint set).
> **Nothing in this document has been applied.** No source file was modified.
>
> Every proposal below carries: tier, files, exactly what moves, the expected
> `scripts/refactor-guard.sh` output, and the risk if the analysis is wrong.
> Findings are ordered by **value ÷ risk**, highest first.
>
> Build state at review time: `Build succeeded, 4 Warning(s)` — CS0414 ×1
> (`Cards/ItemsPile.cs`, out of scope) and CS0162 ×3, all in
> `WorldUI/FlatScreenStereo.cs` (lines 2931, 3094, 3200).

---

## 0. Headline

The subsystem is 24 285 lines over 41 files. Three findings dominate:

1. **`FlatScreenStereo.cs` is two subsystems in one file.** ~1 950 of its 3 787
   lines are the campaign-map renderer; the rest is the stereo compositor. They
   share almost no state.
2. **~530 lines of that map code is provably unreachable**, and **19 config keys
   are bound with descriptions that describe behaviour which never executes.**
   This is *not* a broken feature awaiting repair — see §1, which settles that
   question with evidence.
3. **There is almost no duplication.** A whole-subsystem 8-line-window scan found
   exactly **one** genuinely verbatim, non-trivial duplicate pair
   (`StatPanelSurface` / `PropInfoSurface`). Everything else the scan flagged is
   either 5–8 lines of field-nulling or is *deliberately* parallel-but-different.
   The registry's warning #3 is vindicated: this codebase does not have a
   copy-paste problem.

The single best value-per-risk item is not a split at all — it is **correcting
comments that currently lie** (§2). Comments do not survive into IL, so the guard
diff is empty *by construction*, and the registry itself flags stale comments as
the thing most likely to make a future refactorer act wrongly. A full audit of
every class-level and field-level doc comment in the subsystem found **seven
distinct clusters** of stale documentation, four of which actively describe a
design the project *tried and rejected*. Three of them would lead a reader to
re-introduce a known regression:

- `EnemyRevealSurface`'s "restore the Y lock" (§2.1),
- the `0.5×–2×` resize floor that is really `0.15×` (§2.4),
- `ModalFallback`'s blanket-`ModalUI` claim (§2.5).

The same audit confirmed the good news: `WorldUIConfig` has **zero** dead entries
(§8.3), no stale `renderOnTop` claim survives inside `WorldUI/` (§2.3), and the
class docs of `FlatScreen`, `CanvasConversion`, `ConvertedPanel`, `WindowPanel`,
`GrabbableModal`'s depth-mask block, `WristHud`, `AvatarMirror` and eleven of the
thirteen surfaces all match their code exactly.

### A method note the plan needs before any Tier 1 split

`ilspycmd -p` emits one file per **type**, with members in **metadata order**,
which is **source declaration order**. I verified this against the stored
baseline (`.guard/baseline/GloomhavenVR.WorldUI/CanvasConversion.cs` lists
`Convert` → `ResolveStableHeightCap` → `PlaceHost` → `AdoptNestedCanvases` …,
exactly the source order), and confirmed nested types are emitted **inside** the
outer type's guard file (`MirrorEntry`, `MapEffect` appear inside
`FlatScreenStereo.cs`).

Consequence — the charter's table is slightly optimistic:

| Refactor kind | Actual guard output |
|---|---|
| Move a **top-level type** to its own file | **empty** (its guard file is already separate) |
| Split a class into **partials** | the type's guard file shows the **same members in a different order**; every body byte-identical |

So a partial-class split does **not** produce an empty diff. It produces a *pure
permutation*. That is still a complete proof of no behaviour change, but it needs
a different check:

```sh
diff <(sort .planning/refactor/.guard/baseline/<ns>/<Type>.cs) \
     <(sort .planning/refactor/.guard/current/<ns>/<Type>.cs)      # must be EMPTY
```

Empty ⇒ the multiset of emitted lines is identical ⇒ no body changed, only
member order. **Recommendation: add a `check --permutation` mode to
`scripts/refactor-guard.sh` before Phase 4 starts.** Without it, every Tier 1
split in this document looks like a failure. This is a prerequisite, not a
proposal — it costs ~6 lines of shell and unblocks §4 entirely.

Where a proposal below says *"guard: permutation"*, it means exactly this.

---

## 1. Is the dead map code a broken feature awaiting repair? — **No.**

The task brief flagged a memory saying *"Weltkarte renders black on flat screen;
all RT-capture paths dead; fix = forward albedo re-render"*. That is the **index
one-liner**, written at the *start* of the hunt. The memory body records nine
further stages, ending:

> **MAP FEATURE COMPLETE: render(both maps) + framing + markers + clicks + icons
> + city + zoom(flat-matched) + PAN(trigger-drag) + wind-under-icons +
> physical-mouse-suppression all shipped.** Remaining polish only: icon hover
> grow/shrink anim; general wind thickness over non-icon areas.

And, in the same memory, verbatim:

> **Dead code found:** `MapCaptureMode` config never read (albedo-cam path always
> runs); `EnsureCorrectedMesh`/corrected-mesh path has no call site (original
> mesh's real TexCoord0 is what draws); `BoostAmbientForMapRender` never called
> (MapUnlit is unlit anyway).

`INVARIANTS-WorldUI.md` independently records the *shipped* mechanism as
high-confidence invariants — private RT (`0094147`), `MapUnlit` sampling
TexCoord0 (`53af144`), matrices in `OnPreCull` (`89fed52`), icons on cleared
depth (`287cfdb`/`e02be4f`), wind dimmed not disabled (`6e0824a`), FOV zoom
(`b9d170c`), the driven game camera (`199df5a`), pan (`88b35b3`).

**Verdict:** the code named in §3 is *disproven scaffolding from a hunt that
succeeded*, not a half-finished repair. Both the class doc and the memory
enumerate these paths under "EVERY RT-CAPTURE PATH IS PROVEN DEAD (do not
re-attempt)". Deleting them cannot cost a future fix, because no future fix will
re-arm them.

**One caveat, stated plainly:** the remaining map polish (icon hover animation,
wind thinness over non-icon areas) will be done in `DrawMapIcons` and
`TuneMapWindParticles` — both of which are **live** and are **not** deletion
targets anywhere in this document.

---

## 2. Comment corrections — Tier 0/1, guard diff **empty**, highest value ÷ risk

Comments do not survive into IL. Every item here has a **provably empty** guard
diff. The registry's own recommendation for the first item is *"do it early,
because the stale comments describe superseded invariants and a refactorer
reading them will draw the wrong conclusion."*

### 2.1 `EnemyRevealSurface.cs` — **seven** contradictory comment strata on one field

**File:** `src/GloomhavenVR/WorldUI/Surfaces/EnemyRevealSurface.cs`

The registry flagged this file; a full read found the drift is worse than
recorded. Seven separate places describe a design that was superseded, and two of
them sit **inside the same comment block**, contradicting each other three lines
apart.

| Lines | Stale text | What is true |
|---|---|---|
| 97–104 | `// LAZY FOLLOW — HORIZONTAL ONLY … its WORLD HEIGHT is LOCKED at spawn (see _worldYLocked) … Place() overrides worldPos.y with the locked value` | **There is no `_worldYLocked` field** anywhere in the repo. `Place()` (line 767) applies the full re-projected `worldPos` including Y, with no override. The Y-lock was removed in `e5e7027` once the real cause was found to be a tray-coupled `ScrollRect`. |
| 170–176 | `// Stored pose is now ABSOLUTE WORLD space, planted ONCE … and there is no follow to chase the head.` | Contradicted by lines 177–179 of the *same block*. |
| 177–179 | `// Stored pose is RIG-LOCAL … re-projected through the LIVE rig each frame in Place().` | **This one is correct** (registry: *"anchored in the RIG-LOCAL frame with a HEAD-frame scale"*). Keep it; delete 170–176. |
| 180 | `private Vector3 _position; // … — HORIZONTAL follow` | All-axis. The follow arms on the **full** `Vector3.Angle(gazeL, toPanel)` (line 498) and the spawn log itself prints *"Lazy follow ON in ALL axes (X/Z + Y)"* (line 487). |
| 185 | `// gliding back to the in-view target (horizontal)` | Same word, same error. |
| 712–718 | `Place()` doc: *"PLANTED ONCE by `PlantPose` and then held as an absolute world pose … the board / tray / world-grab move the rig, never this world pose."* | Rig-local and re-projected every frame; it eases to a new target after 22° off-gaze for 0.5 s. Board-independence is real but comes from **rig-locality**, not from an absolute world pose. |
| 759–762 | `// Plant once (rig-local) … no follow, so it never chases a head movement either.` | Contradicted **four lines later** by line 766 (`// Full rig-local follow pose … (all axes, incl. Y)`). |
| 781–785 | *"The key column is Δ = rawReprojY − lockedY … If panel Y ever != lockedY the lock failed."* | The log at 816–827 prints `myHostY`, `CARD widget worldY`, `TRAY Y`, `trackRootY`, `headWorldY`, `rigScale`, `easing`, `CLEARANCE` — **no `rawReprojY`, no `lockedY`, no Δ**. The comment documents columns that do not exist, for a lock that does not exist. |

Only the surviving grain of truth in 97–104 is that the *facing* is yaw-only and
upright (lines 769–775) — that part should be kept, moved to the facing code.

**Why this ranks first:** a refactorer reading lines 97–104 would conclude the
Y-lock is broken and "restore" it — re-breaking the deliberate all-axis follow
that took **eleven** user reports to arrive at (registry: *"The enemy reveal is
anchored in the RIG-LOCAL frame with a HEAD-frame scale"*).

**Proposal:** delete strata 97–104 (minus the facing sentence), 170–176, 759–762
and 781–785; fix "HORIZONTAL" → "all axes" at 180 and 185; rewrite the `Place()`
summary at 712–718. Add one line pointing at the registry entry *"The reveal's
real height bug was a tray-coupled `ScrollRect`"*.

**Tier:** 0 — **comments only**. The `lockedY` field name in the diagnostic and
the log format string at 816–827 are IL and a possible log grep token (charter
§5): **do not touch them in this pass.** Correct only the prose around them.
**Guard:** **empty**.
**Risk if wrong:** zero. This is the highest value-per-risk item in the review.

### 2.2 `FlatScreenStereo.cs` class doc — the MAP section describes the *disproven* design

**File:** `src/GloomhavenVR/WorldUI/FlatScreenStereo.cs`, lines 181–220.

Four claims are wrong about the code directly below them:

| Line | Says | Actually |
|---|---|---|
| 192–193 | *"renders the worldMap mesh straight into the flat-screen base RT (`_leftRt`, = FlatScreen's `_rt`)"* | Renders into the **private** `_mapRt` (`EnsureMapRt`, line 3166). Rendering into `_leftRt` is the exact bug that `0094147` fixed, and line 361–368 of the same file documents that correctly. The class doc contradicts its own field doc. |
| 197–199 | *"drawn UNLIT via a temporary MATERIAL OVERRIDE: one `Sprites/Default` material per submesh"* | `BuildOverrideMaterials` (line 3014) uses `MapUnlitShader()` → `GloomhavenVR/MapUnlit`. Registry: *"`MapUnlit` samples the mesh's own `TexCoord0` on the GPU"*. |
| 194–196 | *"cloned from the game MapCamera's transform + projection + mask"* | `MapDiagTopDown = true` + `MapMatchGameFraming = true`: the mod frames the map **itself** and *drives* the game camera to that pose (`MapDriveGameCamera`). Cloning the game camera is the path that was proven impossible (its pose grazes the parchment plane; `WorldToViewportPoint(meshCenter).z < 0`). |
| 218–220 | *"`ScreenLeftMirrorFallback` off = legacy (black map…)"* | That key is bound and **never read** — see §3.5. |

Same file, three field docs with the same defect:
- line 404 `/// <summary>Unlit Sprites/Default override materials …` → they are `MapUnlit`.
- line 416 `… Sprites/Default samples uv0 and multiplies texture × vertex colour …` (on the dead corrected-mesh field).
- line 264/683 the `MapAlbedoOriginalMaterial` docs, which describe a live A/B choice that no longer exists.

**Tier:** 0. **Guard:** empty. **Risk if wrong:** zero — but note this doc is the
main reference for the hardest area in the mod, so getting the *replacement* text
right matters more than getting it done fast. Draft it against
`INVARIANTS-WorldUI.md` §1 and the map memory, not from the code alone.

### 2.3 `MixedReality.KeepMenusUnclipped` doc cites a **reverted** fix

**File:** `src/GloomhavenVR/Core/MixedReality.cs`, lines 127 and 135.

> `// … to ZTest Always (WorldUI.CanvasConversion 'renderOnTop') …`
> `/// … now fixed by rendering the MODAL on top (WorldUI.CanvasConversion 'renderOnTop') …`

Registry: *"`renderOnTop` (ZTest Always) was tried and **DELIBERATELY REVERTED**"*
— added `b84817d`, removed `8ad0250`. The actual mechanism is the depth-mask quad
at renderQueue 2999. This comment is the most dangerous kind: it names a
technique the project rejected, as if it were the current design, in a file a
refactorer would read while looking at menu occlusion.

**Tier:** 0. **Guard:** empty. **Risk:** zero. *(This is in `Core/`, not
`WorldUI/` — pair it with §8.1, which deletes the same method.)*

**Scope note, verified:** this is the **only** surviving stale `renderOnTop`
claim. A directory-wide check found no on-top claim left inside `WorldUI/`;
`ModalFallback.cs:1131` documents the revert correctly (*"Item 1a (revert): menus
render with NORMAL ZTest again"*), and the `ZTest Always` mentions in
`NativeButtonSkin.cs:124/184` describe *forcing LEqual to undo* the game HUD
asset's on-top material, which is what that code does.

### 2.4 The `0.5×–2×` resize range — stale in six places, and it is the dangerous kind

**Authoritative constant:** `PanelGrabHandle.MinScale = 0.15f` /
`MaxScale = 2f` (`WorldUI/PanelGrab.cs:65-66`). Its own field comment says
*"Item 4: two-hand resize floor. **Lowered from 0.5** … `internal` so the panel
owners reuse the SAME range … single source of truth."* The registry entry
confirms: *"clamping to a higher per-panel minimum would **silently re-cap what
the two-hand pinch just shrank**, so the user's pinch would appear to do nothing
at the bottom of its range."*

Six doc comments still state the old floor:

| File:line | Text |
|---|---|
| `PanelGrab.cs:42` | *"two hands gripping resize it … clamped **0.5×–2×**"* — contradicted 23 lines later by its own `MinScale` comment |
| `PanelGrab.cs:309` | *"midpoint carry + pinch scale (**0.5×–2×**)"* |
| `GrabbableModal.cs:12` | *"two hands RESIZE it (**0.5x-2x**)"* |
| `SettingsPanel.cs:46` | *"two hands resize (**0.5×–2×**)"* |
| `SettingsPanel.cs:66` | *"SettingsScale (**0.5×–2×**) is then the user's ONLY size control"* — but line 590 clamps with `PanelGrabHandle.MinScale` |
| `ModalFallback.cs:114` | *"The user's two-hand resize (**0.5×–2×**) still rides on top of this smaller default."* |

**Do NOT change** `CombatLogSurface.cs:28` / `:50` or the `CombatLogScale` config
description — see §9.1, where the combat log turns out to be a genuine
behavioural outlier rather than a stale comment. **Do NOT change**
`GrabbableModal.cs:583` — that is a `VRLog` string, i.e. IL and a possible grep
token.

**Tier:** 0. **Guard:** **empty** (all six are comments).
**Risk if wrong:** zero to behaviour. The value is that someone re-deriving a
per-panel clamp from these comments would write `Mathf.Clamp(x, 0.5f, 2f)` and
re-introduce exactly the bug `MinScale`'s comment warns about.

### 2.5 `ModalFallback.cs:44-46` class doc claims a blanket `ModalUI` assert

> *"While any of these hold in a scenario, this class asserts
> `VRModeStateMachine.SetAuxModal` (mode → ModalUI) and makes the window
> operable"*

**True:** line 1161 is `VRModeStateMachine.SetAuxModal(wantLock); // ModalUI only
for genuine blockers (item 3b)`. The registry entry *"Floating a menu and
asserting `ModalUI` are two separate wants"* records that coupling them made the
pause/Options menu **invisible** — a regression caught the same round it was
introduced. The `BlockingModalActive` doc 700 lines lower states the rule
correctly.

A refactorer trusting the class doc would re-add a blanket assert and re-break
card grabbing behind the pause menu. **Tier:** 0. **Guard:** empty. **Risk:** nil.

### 2.6 Three numeric cross-references that drifted

| File:line | Says | Actually |
|---|---|---|
| `SettingsPanel.cs:59` | `CanvasMetersPerPixel = 0.0007f; // 380 px ≈ 27 cm wide` | `PanelWidthPx = 520f` (line 54, "2026-07 redesign") → 520 × 0.0007 = **36.4 cm**. Both constants feed the same expression at line 478. |
| `SettingsPanel.cs:62-63` | *"FIXED real-world reference width … roughly the control board width (`PlayTray.BoardW` ≈ 0.64 m)"* | `SettingsPanelWidthMeters = 0.82f` (line 69, trailing `// widened with PanelWidthPx`) — 28 % wider than `BoardW = 0.64f`. |
| `ModalFallback.cs:108` | *"roughly the control-board width (**SettingsPanel targets 0.6 m** ≈ `PlayTray.BoardW`)"* | `SettingsPanelWidthMeters = 0.82f`, `ModalTargetWidthMeters = 0.80f`, `BoardW = 0.64f`. The stated 0.6 m matches **none** of the three. |

The third is the one that matters: anyone "re-syncing the modal width to the
settings panel" from that sentence would shrink it by a quarter.
**Tier:** 0. **Guard:** empty. **Risk:** nil.

### 2.7 `TrayControlDockSurface.cs` class doc describes a feature its own constructor disabled

**File:** `src/GloomhavenVR/WorldUI/Surfaces/TrayControlDockSurface.cs`, lines 3–6
and 21–33.

> *"docks the game's REAL persistent turn-flow widgets onto the control board …
> **replacing the mod-drawn CONFIRM/UNDO board buttons** and the short-rest
> token"* — then documents three live widgets: *"CONTINUE / CONFIRM … Docks at
> the tray's old CONFIRM column position"*, *"UNDO … Docks at the old UNDO column
> position"*, *"SHORT REST … Docks at the rest zone's short-rest anchor"*.

**True:** `_controls = System.Array.Empty<DockedControl>()` (line 82) — the
constructor's own comment already says *"With every control mod-drawn there is
nothing left to dock, so the control set is empty"* — and `ContinueDocked` /
`ContinueVisible` / `UndoDocked` / `ShortRestDocked` are hard-coded `false`
(lines 91, 94, 101, 104). `TrayNativeControls` defaults to `false`.

This is the exact inverse of §3: **the code must stay** (registry: *"do not
delete… a deliberate config-reachable alternative… Tier 3"*), but the class doc is
the last thing in the file still asserting the feature is on. A refactorer reading
top-down would try to "fix" the mod-drawn buttons believing the native dock is
their live fallback.

**Proposal:** prefix the class doc with a status paragraph — *"CURRENTLY INERT BY
DESIGN: `_controls` is empty and the four `*Docked` constants are `false` (see the
constructor for the per-control reason each was pulled). The mechanism below is
retained as a config-reachable alternative behind `[WorldUI] TrayNativeControls`,
which defaults to false. Removing it is a user decision."* Leave every line of
code untouched.

**Tier:** 0. **Guard:** empty. **Risk:** nil.

---

## 3. Dead code + lying config in the map path — Tier 0 code, deprecate-in-place config

All reachability below was verified three ways: full-repo grep, the compiler, and
charter §5 (Harmony inventory, Unity messages, reflection strings, config keys,
log tokens, debug menu). `grep -rn` over `docs/` and `.planning/` returns **no**
mention of any of these symbols outside `INVARIANTS-WorldUI.md` itself. None is a
Harmony target, Unity message, or reflection name.

The 19 config keys below **must not be deleted** (charter §5: an unread config key
is still a user's persisted setting, and unbinding drops it from the user's
`.cfg` on the next write). The precedent is `[Comfort] SeatedMode` — *stay bound,
marked deprecated*. Description edits do not survive into IL, so **the config half
of every item below has an empty guard diff**; only the code half moves.

### 3.1 The mesh-UV-rebuild island — the largest single Tier 0 block

**File:** `FlatScreenStereo.cs`. **Uncalled, and self-contained:**

| Member | Lines | Reachable from |
|---|---|---|
| `EnsureCorrectedMesh` | 2218–2288 | *nothing* |
| `LogWorldMapUvs` | 2289–2319 | `EnsureCorrectedMesh` only |
| `BuildUv0` | 2328–2354 | `EnsureCorrectedMesh` only |
| `ReadRealUvAuto` | 2360–2394 | `BuildUv0` only |
| `ReadRealUv` | 2397–2418 | `BuildUv0` only |
| `BuildPositionalUv` | 2426–2468 | `BuildUv0` only |
| `AxisVal` / `AxisName` | 2470 / 2472 | `BuildPositionalUv` only |
| `ApplyOrientation` | 2475–2488 | `BuildUv0` only |
| `ReleaseCorrectedMesh` | 2491–2524 | *nothing* |

Fields used **only** inside that range (verified by per-field line census):
`_worldMapCorrectedMesh`, `_worldMapOrigMesh`, `_worldMapMeshFilter`,
`_meshSwapOrig`, `_meshSwapped`, `_uvConfigRevisionApplied`,
`_worldMapColorsLogged`, plus `s_uvConfigRevision` and its six
`SettingChanged += bump` hooks (lines 664–671).

≈ **285 lines of method bodies + 8 fields + 8 lines of event wiring.**

Six config keys become description-only work: `MapUvSource`, `MapUvSwapUV`,
`MapUvFlipU`, `MapUvFlipV`, `MapUvChannel`, `MapUvComponent`. Note the live code
**explicitly distrusts** one of them — line 1746: *"HARD-CODED (not the persisted
`MapUvChannel` config, which may hold a stale value)"*. So `MapUvChannel` is not
merely unread; reading it was rejected.

**Proposed descriptions** (prefix, keep the rest so an existing `.cfg` diff stays
readable):

```
MapUvSource      → "DEPRECATED — no effect. The map's UV is now sampled on the GPU
                    from the mesh's own TexCoord0 by GloomhavenVR/MapUnlit; the CPU
                    uv0-rebuild path it configured was removed (53af144). Kept bound
                    so existing .cfg files load unchanged."
MapUvSwapUV / MapUvFlipU / MapUvFlipV / MapUvComponent  → same prefix.
MapUvChannel     → "DEPRECATED — no effect, and deliberately not read: the shader's
                    UV channel is hard-coded to 0 so a stale persisted value cannot
                    break the map. Kept bound so existing .cfg files load unchanged."
```

**Tier:** 0 (code) + description-only (config).
**Guard:** the nine methods vanish from `FlatScreenStereo.cs`; the eight fields
vanish; **nothing else in any type changes**. Anything else appearing = revert.
**Risk if wrong:** the map's UV would have a runtime escape hatch removed. Two
independent proofs say it does not: (a) no caller, verified by grep and by the
compiler's own reachability (removing them compiles); (b) the offline ground
truth in the map memory established the mesh's UV0 is correct and the shader
reads it directly. Residual risk: **very low**.

### 3.2 `ReconcileMapDeferred` + the `MapCaptureMode` strategy

**File:** `FlatScreenStereo.cs`.

- `ReconcileMapDeferred` (1941–1982) — **no caller**. `EndStackSync`
  unconditionally calls `RestoreStrippedEffects()` then
  `ReconcileAlbedoCamera(mapSource)` (lines 1184, 1186); mode 1 is never entered.
- `BuildMapEffects` (1990–2023) — reachable only from `ReconcileMapDeferred`.
- `DeclaresOnRenderImage` (2026–2038) — reachable only from `BuildMapEffects`.
- `ApplyMapEffectStrip` (2039–2055) — reachable only from `ReconcileMapDeferred`.
- `MapEffect` class (447–457) + `_mapEffects`, `_mapEffectCam`,
  `_mapEffectsLogged` — written only by the above.
- `RestoreStrippedEffects` (2058–2080) — **is** called (twice, live), but iterates
  `_mapEffects`, which nothing can ever populate once the producer is gone. It
  becomes a vacuous loop.

**This is the registry's "paired lists / asymmetric undo" hazard in its benign
form:** the producer is dead and the consumer is live. Delete them **as one
commit**, never one side only. (Same shape as §3.3.)

≈ **145 lines + 1 nested class + 3 fields.**

Six config keys → deprecate in place: `MapCaptureMode`, `MapStripBeautify`,
`MapStripVolumetricFog`, `MapStripSSAO`, `MapStripPostProcess`,
`MapStripAllImageEffects`.

Two internal inconsistencies worth recording in the new descriptions:
- the accessor's fallback is `?? 2` (line 691) while the bound default is `1`
  (line 615) — they have disagreed since the entry was written;
- **mode 2 has no implementation at all**, and its three orientation knobs
  (`MapTexFlipX`, `MapTexFlipY`, `MapTexSwapDiag`, lines 630–636) have no reader
  anywhere — not even a dead one. *These three are a finding the registry did not
  name.* Same treatment.

**Proposed description:**

```
MapCaptureMode → "DEPRECATED — no effect. The campaign map is always rendered by
                  the mod's forward albedo camera into a private RenderTexture
                  (0094147). The 'passive-deferred' (1) and 'texture blit' (2)
                  strategies this selected were disproven and removed. Kept bound
                  so existing .cfg files load unchanged."
MapStrip*      → "DEPRECATED — no effect (belonged to MapCaptureMode 1). …"
MapTexFlip*/SwapDiag → "DEPRECATED — no effect (belonged to MapCaptureMode 2, which
                  was never implemented). …"
```

**Tier:** 0. **Guard:** those five methods + `MapEffect` disappear from
`FlatScreenStereo.cs`; `EndStackSync` and `ReleaseAlbedo` each lose one call.
Diff confined to `FlatScreenStereo`.
**Risk if wrong:** if the map ever regresses to black, mode 1 was the "keep the
game's deferred render, strip its effects" rescue. It is proven dead — the memory
records the deferred→RT path as a hard wall with ~9 hardware attempts — and it
would not have worked anyway. **Low.**

### 3.3 `BoostAmbientForMapRender` / `EnsureMapLight` — producer dead, restorer live

**File:** `FlatScreenStereo.cs`.

- `BoostAmbientForMapRender` (3697–3717) — **no caller**.
- `EnsureMapLight` (3733–3746) — reachable only from it.
- `RestoreAmbientAfterMapRender` (3719–3730) — **is** called from `ReleaseAlbedo`
  (line 3243); guarded on `_ambBoosted`, which nothing can set.
- `ReleaseAlbedo` lines 3245–3248 destroy `_mapAlbedoLight`, which nothing creates.

Again: **delete the four together, in one commit.** ≈ **55 lines + 5 fields.**

Two config keys → deprecate: `MapAlbedoAmbient` (default 4), `MapAlbedoLight`.
Both describe brightness tuning for a *lit* render; `MapUnlit` made the concept
moot. Their descriptions currently end *"Tune live"* — precisely the "knob that
lies" pattern that triggered the settings audit in `cc99144`.

**Tier:** 0. **Guard:** confined to `FlatScreenStereo`. **Risk:** very low.

### 3.4 `MapAlbedoOriginalMaterial` — a config key with an accessor and no consumer

**Not named in the registry.** `s_mapAlbedoOriginalMat` (line 265) is bound (line
588) and has an accessor `MapAlbedoUseOriginalMat` (line 684) — which is called
**nowhere**. Its description tells the user that ON renders with the game's
Amplify material and OFF selects "the old (dead) Sprites/Default override path".
Neither is true: `BuildOverrideMaterials` unconditionally uses `MapUnlit`.

**Tier:** 0 for the accessor property; description-only for the key.
**Guard:** one property getter disappears from `FlatScreenStereo`. **Risk:** nil.

### 3.5 `ScreenLeftMirrorFallback` — bound, described, never read at all

**Not named in the registry.** `s_leftMirrorFallback` (line 261) is bound at line
571 with a 6-line description promising *"Off = legacy (black map if the game
camera renders black)"*. There is **no accessor and no reader** — the only other
mention in the whole repo is the class-doc sentence at line 219.

This one is worth calling out separately because it is the *most* misleading of
the 19: it presents itself as the master switch for the map rescue.

**Tier:** description-only (the field must stay to keep the key bound).
**Guard:** empty. **Risk:** nil.

### 3.6 Two one-line orphans

| Where | What | Evidence |
|---|---|---|
| `FlatScreenStereo.cs:1929` | `private static string Fmt(Vector3 vp) => …` | zero references in the file or the repo. **Not in the registry.** |
| `FlatScreen.cs:241` | `private bool _backdropMenuQueue;` | assigned at line 460 in `TickBackdropDepth`, never read. `_screenMaterialQueueDefault` is the load-bearing one. Registry confirms. |

**Tier:** 0. **Guard:** confined to `FlatScreenStereo` / `FlatScreen`. **Risk:** nil.

### 3.7 Summary of §3

| | Code removed | Config keys re-described |
|---|---|---|
| 3.1 mesh-UV island | ~293 lines, 9 methods, 8 fields | 6 |
| 3.2 `MapCaptureMode` | ~145 lines, 5 methods, 1 class, 3 fields | 9 |
| 3.3 ambient boost | ~55 lines, 3 methods, 5 fields | 2 |
| 3.4 / 3.5 / 3.6 | ~5 lines | 2 |
| **Total** | **≈ 500 lines (13 % of `FlatScreenStereo.cs`)** | **19** |

Suggested commit split: **four** commits (3.1 / 3.2 / 3.3 / 3.4+3.5+3.6), one
guard run each, plus **one** description-only commit for all 19 keys (guard:
empty). Never mix the code and description commits — the whole point of the
description commit is that its guard output is provably empty.

---

## 4. God files — decomposition

### 4.0 What decomposition is *for* here

Not aesthetics. Concretely: `FlatScreenStereo.cs` currently makes you scroll past
1 950 lines of parchment-mesh, icon-decal and wind-particle code to find the
30-line eye-separation math, and a `Ctrl-F` for `_leftRt` returns hits from both
subsystems. That is the maintainability cost. Nothing else about these files is
wrong.

### 4.1 `CanvasConversion.cs` — move four top-level types out (**pure motion, guaranteed empty diff**)

**This is the safest split in the subsystem, and it should go first.**

`CanvasConversion.cs` (2 347 lines) contains **five** top-level types:

| Type | Lines |
|---|---|
| `ConvertedPanel` | 9–245 |
| `LayerRecord` | 247–256 |
| `NestedCanvasRecord` | 258–290 |
| `FlattenRecord` | 292–300 |
| `CanvasConversion` | 302–2347 |

The first four are already **separate files in the guard baseline**
(`.guard/baseline/GloomhavenVR.WorldUI/ConvertedPanel.cs`, `LayerRecord.cs`,
`NestedCanvasRecord.cs`, `FlattenRecord.cs`). Moving them to
`WorldUI/ConvertedPanel.cs` therefore cannot change one byte of guard output —
not even member order, because each is its own guard file and its internal order
is untouched.

**What moves:** lines 1–300 verbatim (four types + their doc comments), plus the
`using` lines they need. `CanvasConversion.cs` drops to ~2 050 lines.
**Tier:** 1. **Guard:** **empty**, unconditionally.
**Risk if wrong:** none identifiable. The csproj uses default globs (no explicit
`<Compile Include>` items), so no project edit is needed.

### 4.2 `CanvasConversion` itself — partial split into three

After 4.1, the remaining class has three cohesive responsibility groups. They are
**already contiguous** in the source, which is why the split is clean:

| Proposed partial | Members | ~Lines |
|---|---|---|
| `CanvasConversion.cs` (core) | `Convert`, `ResolveStableHeightCap`, `PlaceHost`, `Release`, `ReleaseAll`, `Tick`, `LateTick`, `SetUiLocked`, `SetSoftLock`, `Add/RemoveMaskRequest`, `EnsureCameraMask`, `RestoreCameraMask`, `DiagnoseModal`, the static state block | ~1 000 |
| `CanvasConversion.Adopt.cs` | `AdoptNestedCanvases`, `IsDropdownOverlay`, `AdoptCanvas`, `ReassertAdoptedSorting`, `EnsureScrollClipping`, `ApplyModLayer`, `IsRelayered`, `HideFullScreenBackground`, `FlattenSubtree`, `IsFlattenRecorded` | ~600 |
| `CanvasConversion.Fit.cs` | `TryGetVisibleHostRect`, `FindEnclosingClipper`, `TryMeasureContent`, `CollectVisibleMaskRects`, `IsNonRenderingMaskEmitter`, `FitHostToContent`, `TickFit`, `SettleOneShotFit` | ~450 |

**Critical constraint this split must preserve, and does:** the seven static
scratch buffers (`CanvasScratch` 678, `ScrollScratch` 883, `TransformScratch` 966,
`BgGraphicScratch` 1026, `RectScratch` 1109, `GraphicScratch` 1225) and
`ClipperMemo` (1329) are shared *across* the Adopt and Fit groups. A **partial
class keeps them in the same type** — they stay `private static` on
`CanvasConversion` and nothing about their sharing changes. Splitting into two
*separate classes* would force them public or duplicate them, which is exactly the
re-entrancy hazard the registry names (standing warning #7). **Partial only.**

Similarly `FirstCapHeights` (line 341) is a process-lifetime dictionary that is
never cleared, by design (registry) — it stays where it is, in the core partial.

**Tier:** 1. **Guard:** permutation on `CanvasConversion.cs` (see §0 method note);
`ConvertedPanel`/`LayerRecord`/`NestedCanvasRecord`/`FlattenRecord` untouched.
**Risk if wrong:** if a `private static` field or a nested helper is accidentally
duplicated rather than moved, the guard's permutation check catches it
immediately (line multiset would differ). **Low**, and *detectable*.

### 4.3 `FlatScreenStereo.cs` — split the campaign map off the stereo compositor

**Do §3 first.** Removing ~500 dead lines shrinks what has to move by a quarter
and removes the temptation to move dead code into a new file.

I measured this by classifying every member declaration: **~1 950 of the 2 901
member-body lines belong to the campaign map**, and the map members are already
roughly zoned (1250–1550, 1552–2779, 2782–3364, 3697–3754). After §3 the map
block is ~1 450 lines.

The charter's history explicitly flags this file as hardware-won plumbing. **So
be explicit about what this split is and is not:** it moves member declarations
between files in the same `partial class`. It changes no statement, no field
accessibility, no call order, no `[Conditional]`, no hook registration. The
`OnPreCullCamera` / `OnPreRenderCamera` / `OnPostRenderCamera` hooks contain
*both* stereo and map logic in one method body — **they stay in the core file
whole; a method is never split.** That is a deliberate limitation of this
proposal and it is the right one.

| Proposed partial | Contents | ~Lines after §3 |
|---|---|---|
| `FlatScreenStereo.cs` (core) | class doc, `BindConfig`, `MirrorEntry`, `CreateColorRt`, `DescribeRt`, `WantActive`, `Tick`, `ComputeVideoShiftUv`, `Deactivate`, `ReleaseRightRt`, `ReleaseShiftRt`, `BeginStackSync`, `SweepForCameraPlaneVideos`, `LogLateVideo`, `SyncCamera`, `EndStackSync`, `EnsureShiftRt`, `ReleaseMirrors`, `DestroyMirrorAt`, `CreateMirror`, `IsNearPlaneVideoActive`, the three camera hooks, `SampleIpd` | ~1 400 |
| `FlatScreenStereo.MapRender.cs` | `EngageMapAlbedo`, `ReconcileAlbedoCamera`, `EnsureAlbedoCamera`, `EnsureAlbedoReady`, `EnsureMapRt`, `ReleaseMapRt`, `ReleaseAlbedo`, `DetectActiveMap`, `FindWorldMapRenderer`, `GatherMapTextures`, `QuadrantIndexFromName`, `BuildOverrideMaterials`, `ApplyWorldMapOverride`, `RestoreWorldMapOverride`, `DestroyOverrideMaterials`, `MapUnlitShader`, `UvDebugTexture`, `LogMapSceneRenderers` + their fields/consts | ~800 |
| `FlatScreenStereo.MapInput.cs` | `TickMapInput`, `IsScenarioOverlayActive`, `TryComputeGameMapPose`, `TryMapPlaneHit`, `TryMapPixelToPlane`, `BeginMapPan`, `UpdateMapPan`, `EndMapPan`, `MapActive`, `MapPanning`, zoom/FOV consts | ~300 |
| `FlatScreenStereo.MapDecor.cs` | `DrawMapIcons`, `CollectIconDecals`, `RentIconMpb`, `TuneMapWindParticles`, `RestoreWindParticles`, `ScaleGradientAlpha`, `ScaleGradient` + icon/wind fields | ~350 |
| `FlatScreenStereo.MapProbe.cs` | `TickBlackProbe`, `OnBlackProbe`, `TickAlbedoProbe`, `OnAlbedoProbe`, `ReleaseProbeRt`, `ReleaseAlbedoProbeRt` + probe consts/fields | ~150 |

**`BindConfig` stays whole in the core file.** It binds stereo *and* map keys in
one method; splitting it would be a real code change, and the config-binding seam
is exactly where a mistake costs a user their settings.

**Tier:** 1. **Guard:** permutation on `FlatScreenStereo.cs`; **no other type may
appear**.
**Risk if wrong:** the highest-consequence file in the subsystem. Mitigations:
(a) do it *after* §3 so the diff is smaller; (b) do it in **five** commits, one
partial at a time, guard-checked each time — a permutation failure on commit *n*
is diagnosable in isolation; (c) never split a method body; (d) if the permutation
check is not yet available (§0), **do not start this item**.

**Alternative if the user prefers maximum conservatism:** do §3 only and stop.
`FlatScreenStereo.cs` at ~3 280 lines with the dead map scaffolding gone and the
class doc corrected is already a materially better file, and it costs one
provably-empty-diff commit plus four confined ones. The split is a genuine
improvement but it is the one item in this document where "leave it" is a
defensible answer.

### 4.4 `SettingsPanel.cs` (3 014 lines) — partial split, five clean seams

This file has the cleanest internal structure of the five giants; its own section
comments already name the seams.

| Proposed partial | Contents | ~Lines |
|---|---|---|
| `SettingsPanel.cs` (core) | enums, state, ctor, `RefreshLanguage`, `RequestToggle`, `Tick`, `Shutdown`, `Toggle`, `SetOpen`, `RefreshAll`, `Safe` | ~450 |
| `SettingsPanel.Placement.cs` | `Placement`, `PlaceInView`, `IPanelGrabOwner` members, `TogglePin`, `ApplyPinVisual`, `TickPin`, `PersistLayout`, `TickChord`, `BuildFrame` | ~350 |
| `SettingsPanel.Widgets.cs` | `Row`, `Section`, `Label`, `Button`, `Stepper`, `MiniStepper`, `Toggle`, `ToggleButton`, `CycleButton`, `BuildColumn`, `BuildSwatch` | ~300 |
| `SettingsPanel.Content.cs` | `Build`, `BuildBody`, `BuildNavButton`, `GateCat`, `NavCatLabel`, `BuildHandsCategory`, `BuildFiguresCategory`, `BuildWristCategory`, `BuildPerformanceCategory` | ~750 |
| `SettingsPanel.DebugTuning.cs` | `BuildElementTuning` and the whole `Format*` / `Step*` / `*RowsVisible` / `ElementHas*` / `Add*Stepper` / `Cycle*` / `ResetDebugElement` family | ~1 150 |

**Constraint to carry across, verbatim, as a comment in the core partial:**
`Build()` must clear `_refreshers`, `_debugRows`, `_debugRowVisible` **first** —
the registry entry (`c1e60fc`) explains this is an NRE-flood fix, not tidiness.
After the split, `Build()` lives in `Content.cs` and the lists live in the core
partial; the physical distance makes it easier to "clean up". Add the pointer.

**Tier:** 1. **Guard:** permutation on `SettingsPanel.cs`. **Risk:** low — this
class is self-contained, has no Harmony surface, and its per-frame path
(`Tick` → `Placement`) is short.

### 4.5 `ModalFallback.cs` (2 866 lines) — nested types out, then partials

Two nested types come out first (they get their own guard files only if promoted
to top level — **do not promote them**; keep them nested and move them into a
partial, so the guard sees a permutation of `ModalFallback.cs`, not a new type):

| Proposed partial | Contents | ~Lines |
|---|---|---|
| `ModalFallback.cs` (core) | `FallbackIds`, `NonBlockingMenus`, static state, `Attach`, `Detach`, `OnWindow`, `IsFallbackWindow`, `Tick`, `BlockingWindowModalActive`/`WindowModalActive`, `FindPanel`, `ReassertStickyVisible`, `IsConverted`, window-gathering helpers, `LogPollTransition` | ~800 |
| `ModalFallback.DecisionDock.cs` | the nested `DecisionDock` static class + `Prompt` (309–610) | ~300 |
| `ModalFallback.WindowPanel.cs` | the nested `WindowPanel` class (613–784) | ~170 |
| `ModalFallback.Convert.cs` | `IsFullScreenMenu`, `WantsTransparentBackground`, `TryConvertWindow`, `RefloatOpenWindows`, `IsResultsPanel`, `ReleaseAllWindows` | ~450 |
| `ModalFallback.Spawn.cs` | `TryGetBoardPlaneY`, `ClampSpawnPose`, `CollectSpawnObstacles`, `CandidateBounds`, `OverlapAmount`, `ResolveSpawnOverlap`, `PanelWorldHalfSize`, `ComputeHmdPose`, `DeriveWindowScale`, `PlaceAtHmd` | ~490 |
| `ModalFallback.Close.cs` | `TickEscapeChord`, `CloseTopModal`, `CloseFloatedWindow`, `ResetEscMenuToggleGroup`, `CloseStickyFloatsExceptEscMenu` | ~250 |
| `ModalFallback.MenuGuard.cs` | `TickMenuRecall`, `IsInHeadView`, `ApplyMenuSelectionGuard`, `RestoreMenuSelectionGuard`, `SyncEscMenuTabHighlights`, `SyncTab`, `IsIdFloated` | ~280 |
| `ModalFallback.ResultsScroll.cs` | `TickResultsStickScroll`, `TryFindResultsScrollTarget`, `DriveResultsScroll` | ~175 |

**`Tick()` stays whole in the core file.** It is a numbered step sequence (the
registry cites "step 5b" by name) and its statement order is load-bearing in at
least four registry entries. It is the single method in this subsystem I would
most strongly refuse to touch.

**Tier:** 1. **Guard:** permutation on `ModalFallback.cs`. `DecisionDock`,
`Prompt` and `WindowPanel` are emitted *inside* that file, so they participate in
the same permutation — this is expected, not collateral.
**Risk if wrong:** medium-low. `ModalFallback` is the busiest file in the
registry (≈ 40 entries). Do it last among the splits, in the order listed
(nested types first — they are self-contained and prove the mechanism).

### 4.6 `FlatScreen.cs` (2 456 lines) — split along its own existing section markers

This file already carries `// ---- <section> ----` banners that map 1:1 onto a
partial split. Nothing needs to be invented:

| Proposed partial | Existing banner | Members |
|---|---|---|
| `FlatScreen.cs` (core) | *policy*, *lifecycle*, *pre-menu indicator* | `Tick`, `WantVisible`, `IsPreMenuScene`, `TickManualChord`, `IsConfirmationBoxOpen`, `Show`, `Hide`, `Shutdown`, `OnEndOfFrame`, `TickStartingIndicator`, `DestroyIndicator`, `CapturedCamera` |
| `FlatScreen.CameraStack.cs` | *screen layer split* | `CaptureStack`, `IsUiCamera`, `IsCaptured`, `TargetFor`, `SelectBases`, `TickSplitLifecycle`, `EnsureBackQuad`, `TeardownSplit`, `SetSplitRouting`, `ClearUiRt`, `TickNoUiWatchdog`, `TickStackClears`, `ReleaseStack` |
| `FlatScreen.Desktop.cs` | *ITEM 9* | `TickDesktopMirrorMode`, `RestoreDesktopMirrorMode`, `TickDesktopCameraScrub`, `ReleaseDesktopScrub` |
| `FlatScreen.Placement.cs` | *placement* + *ITEM 1* | `PlaceScreen`, `PlaceBackQuad`, `LogPlacement`, `WantedQuadWidth`, `FollowHead`, `TickBackdropDepth`, `LogHandsVisibilityDiagnostic`, `LogHandRenderers` |
| `FlatScreen.Pointer.cs` | *pointer*, *poke click*, *direct click*, *drag* | `TickPointer`, `TickHandednessSwitch`, `TickPoke`, `TryBeginPoke`, `EndPoke`, `HideReticle`, `DirectClick`, `BeginScreenDrag`, `UpdateScreenDrag`, `EndScreenDrag`, `LogUnderPointer` |

**Constraint to carry as a comment:** `WantedQuadWidth` moves to `Placement.cs`
while its *second* consumer, `FollowHead`'s re-place trigger, moves with it — good.
But the registry entry (*"one function, both callers … a divergence would re-place
the screen every frame"*) should be restated at the function after the move.

**Tier:** 1. **Guard:** permutation on `FlatScreen.cs`. **Risk:** low.

### 4.7 Not proposed — files that are long but coherent

`ButtonCluster.cs` (1 200), `AvatarMirror.cs` (1 087), `ButtonTuning.cs` (768),
`WristHud.cs` (719), `GrabbableModal.cs` (703), `VirtualMouseBridge.cs` (647).
Each is one concept. Splitting them buys nothing and costs a guard run.
`ButtonTuning.cs` does host a second top-level type (`ButtonDissolveFx`, line
648) — moving it out is a free empty-diff win if a commit is going that way
anyway, but it is not worth its own commit.

---

## 5. The three CS0162 warnings — recommendation

### 5.1 The facts

Three `if (<const false>)` **statement** sites, all in `FlatScreenStereo.cs`:

| Warning line | Method | Flag | Body |
|---|---|---|---|
| 2931 | `DrawMapIcons` | `MapIconsSolidTest` (1806) | draw solid magenta quads to prove icon geometry renders |
| 3094 | `BuildOverrideMaterials` | `MapUvDebug` (1722) | swap the albedo for a generated UV read-out texture (`UvDebugTexture`, 46 lines) |
| 3200 | `ApplyWorldMapOverride` | `MapDiagClearOnly` (1734) | early `return` — leave the game's deferred material on, so only the camera clear renders |

Two siblings from the **same family, with the same intent**, produce **no**
warning purely because they are consumed inside *expressions* rather than
statements:

| Line | Flag | Form |
|---|---|---|
| 1572 | `MapDiagClearColor` (1724) | `cam.backgroundColor = MapDiagClearColor ? magenta : Color.black;` |
| 3104 | `MapDiagPerSubmeshChannel` (1732) | `float chan = MapDiagPerSubmeshChannel ? … : MapUnlitUvChannel;` |

`MapDiagClearOnly` also appears in a log-string interpolation at line 1660, which
likewise does not warn.

A **different** family, which must not be confused with these, is
`MapDiagTopDown` (1739) = `true`, `MapMatchGameFraming` (1742) = `true`,
`MapDriveGameCamera` (1769) = `true`, `MapDrawIcons` (1804) = `true`. These
**select shipping behaviour**; their disabled arms are the *previous disproven
implementations*. Note that `if (!MapDiagTopDown && …)` at line 3496 escapes
CS0162 only because `false && <non-constant>` is not a constant expression —
another accident of form, not design. **Flipping any of these four is Tier 3.**

### 5.2 Recommendation: make the mechanism uniform with scoped `#pragma`, do not delete

The three branches contribute **zero bytes to the shipped DLL** — the compiler
eliminates a `const false` branch entirely, which is *why* they warn. So:

- deleting them → **empty guard diff**, and loses a documented diagnostic surface;
- suppressing them → **empty guard diff**, and keeps it.

Given identical guard outcomes, the deciding question is only "is the mechanism
worth keeping?". It is, for two reasons: the map hunt took ~30 commits and each
of these flags is what disambiguated one hypothesis; and the map still has open
polish work (icon hover animation, wind thickness) in exactly `DrawMapIcons` and
`BuildOverrideMaterials`, the two methods two of the three flags instrument.

**Proposal:**

1. Collect all five diagnostic flags into **one commented block** at their
   declarations (they are already adjacent, lines 1712–1748), under a header that
   states the contract explicitly:

   > *Compile-time bisection switches from the campaign-map hunt. Deliberately
   > `const` so a disabled branch contributes nothing to the shipped DLL. Flip one
   > to `true`, rebuild the DLL (no AssetBundle rebuild needed), take one hardware
   > screenshot. `MapDiagClearColor` and `MapDiagPerSubmeshChannel` sit in
   > ternaries and therefore do not raise CS0162 — that is an accident of
   > expression form, not a difference in kind. The `true`-valued
   > `MapDiagTopDown` / `MapMatchGameFraming` / `MapDriveGameCamera` /
   > `MapDrawIcons` below are NOT diagnostics: they select shipping behaviour and
   > flipping one is a behaviour change.*

2. Wrap **each of the three warning sites individually** in
   `#pragma warning disable CS0162` / `restore CS0162`, each carrying a one-line
   comment naming the flag.

   *Site-scoped, not file-scoped.* A file-wide disable would mask a genuine
   unreachable-code bug introduced later in a 3 800-line file — which is exactly
   the class of defect CS0162 exists to catch.

3. Do **not** convert them to `static bool` properties or to live config entries.
   A property is not a compile-time constant, so the branch would start shipping
   in the DLL and the guard diff would stop being empty; a config entry makes them
   runtime-flippable, which is a behaviour change (Tier 3) and re-creates the
   "knob that lies" problem §3 is trying to fix.

**Tier:** 0 (pragmas and comments are not IL).
**Guard:** **empty**.
**Risk if wrong:** none to behaviour. The only cost is that CS0162 stays
suppressed at three known sites — mitigated by the site scoping.

**Alternative, if the user wants zero suppressions:** delete all three branches
*and* the two ternary arms *and* `UvDebugTexture` (46 lines) together, so the
mechanism disappears consistently rather than half of it. Guard is also empty.
I do not recommend it — but it is the only *consistent* deletion, and deleting
just the three warning sites (leaving the two ternaries) would be the worst of
both, which is what a naive "fix the warnings" pass would do.

---

## 6. Duplication — one real candidate, and it is optional

A normalised 8-line sliding-window scan across all 41 files found **no**
duplicate longer than ~17 lines. Every candidate was diffed. Results:

### 6.1 `StatPanelSurface` / `PropInfoSurface` — genuinely byte-identical (Tier 2 candidate)

Byte-identical, verified by mechanical diff:

| Member | Result |
|---|---|
| nested `class Watch` (17 fields/props) | identical except one comment word (`test #16` vs `test #16 pattern`) |
| `ReleaseDelaySeconds` = 0.3f, `ChurnWindowSeconds` = 2f, `ChurnWarnCount` = 5 | identical |
| `DetachWatch` (11 lines) | **diff = 0** |
| `CountConversion` (17 lines) | **diff = 0**, including the warning string |
| `ScheduleRelease` (6 lines) | **diff = 0** |

**Not identical, and must stay per-surface:** `TickWatch` — `StatPanelSurface`
passes `sortingOrder: StatPanelSortingOrder` (10), which is a named registry
invariant (*"a deliberate middle tier"*); `PropInfoSurface` passes none. They also
gate on different config keys (`StatPanels` vs `PropInfoCards`) and
`StatPanelSurface` additionally requires `Choreographer.s_Choreographer != null`.

**Proposal (optional, low priority):** extract `Watch`, the three constants,
`DetachWatch`, `CountConversion` and `ScheduleRelease` into one
`Surfaces/HoverPanelWatch.cs`; leave `TickWatch` in each surface.
≈ 60 duplicated lines removed, and the shared "hover panel anti-churn" concept
finally gets a name.

**Tier:** 2. **Guard:** `StatPanelSurface.Watch` and `PropInfoSurface.Watch`
disappear, a new `HoverPanelWatch` type appears, and both surfaces' bodies change.
**Blast radius is three types.**
**Risk if wrong:** the constants become *shared*. If a future round needs
`PropInfoSurface`'s release hysteresis at 0.5 s, an edit there would silently
retune `StatPanelSurface` — and the registry names `StatPanelSurface`'s three
breakers as *"independent breakers by design"* whose removal re-opens a 45 Hz
self-occlusion loop.

**My recommendation: do not merge in this refactor.** Instead take the
zero-risk half: add a cross-reference comment at each of the five duplicated
members (*"deliberately duplicated with `PropInfoSurface`; the constants are
per-surface tunables — see `INVARIANTS-WorldUI.md`, StatPanelSurface entry"*).
That captures the knowledge, costs an empty guard diff, and leaves the merge
available as a later user decision. Listed here because the charter asks for the
census, not because I think it should be executed.

### 6.2 `SettingsPanel.RefreshLanguage` vs `SettingsPanel.Shutdown` — an **asymmetry**, not a duplicate

Both contain the same 14-line teardown (`Destroy(_holder)` + 10 field nulls +
`_placedFromConfig` / `_facedPoseVersion` / `_healLogged`) and the same 5-line
list-clear tail. `Shutdown` inserts **two extra lines** between them:

```csharp
_respawnRequested = false;
_sizeScale = 0f; // re-snapshot the diorama scale on the next open
```

`RefreshLanguage` deliberately does **not** clear `_sizeScale` — and it must not:
the registry entry *"The settings panel size is decoupled from diorama zoom,
snapshotted at open"* means clearing it would re-snapshot from the live diorama
scale, so a **language switch would resize the panel**.

**This is a textbook instance of registry hazard #3.** The right action is a
one-line comment on those two lines in `Shutdown` explaining why
`RefreshLanguage` omits them. **Tier 0, guard empty, risk nil.** A helper
extraction is possible (Tier 2, guard confined to `SettingsPanel`) but buys ~18
lines and re-couples two paths whose difference is load-bearing. Not recommended.

### 6.3 Examined and rejected

| Pair | Why not |
|---|---|
| `SettingsPanel.Placement` vs `CombatLogSurface` (PINNED heal block, ~15 lines) | Structurally parallel, but different config keys, an extra `!grabbed &&` guard, and **two different `VRLog.Info` strings**. Log lines are a protected surface (charter §5, and the registry's "change-deduped logging is a diagnostic contract"). Extracting would parameterise a log token. **Leave; add a cross-reference comment.** |
| `DecisionDockSurface` vs `DamageTooltipSurface` (8-line mount-fit block) | Both are `TrayMountedPanelSurface` subclasses; hoisting to the base changes virtual dispatch and `DensityScale` resolution. Tier 2/3 for 8 lines. **Leave.** |
| `CombatLogSurface` internal 8-line field-null runs (lines 204/408) | Trivial. **Leave.** |
| `FlatScreenStereo` 2304 vs 2368 | Inside the dead mesh-UV path — resolved by §3.1. |

---

## 7. Constraints to document rather than unify (charter §7: "write the reason down")

These are the items the brief asked to *document*, not fix. All are Tier 0
comment additions with an empty guard diff. All are also **standing invitations
to a wrong "cleanup"**, which is why writing them at the code matters.

### 7.1 Three sibling files, four different "am I active?" gates — **do not unify**

| Gate | File | Definition |
|---|---|---|
| `InputModeGuard.Active` | `InputModeGuard.cs:35` | `ForceMouseMode.Value && ConversionActive` |
| `EscMenuInputBlock.ShouldSuppress` | `Patches/EscMenuInputBlock.cs:58` | `VRSession.IsRunning` |
| `TakeDamagePanelSafety.GuardsActive` | `Patches/TakeDamagePanelSafety.cs:61` | `ConversionActive` |
| `VirtualMouse.TickSuppressPhysicalMice` | `VirtualMouseBridge.cs` | `SuppressPhysicalMouse && VRSession.IsRunning` |

They differ for reasons the registry states individually: the ESC suppressor must
be live whenever the VR session is (it owns a *physical button*, not a converted
canvas); the take-damage guards are scoped so vanilla is byte-identical when
conversion is off; the input-mode guard additionally honours its own config key
because it changes which **scene** the game loads. **Unifying them is Tier 3.**

**Proposal:** a four-line comment at each gate naming the other three and stating
"deliberately different — see `INVARIANTS-WorldUI.md`, standing warning #8".

### 7.2 Static scratch buffers assume a single-threaded, non-re-entrant tick

`CanvasConversion`: `CanvasScratch` (678), `ScrollScratch` (883),
`TransformScratch` (966), `BgGraphicScratch` (1026), `RectScratch` (1109),
`GraphicScratch` (1225), `ClipperMemo` (1329, cleared at the top of *both*
`TryMeasureContent` and `CollectVisibleMaskRects`).
`GrabbableModal`: `MaskRectScratch`, `MaskVertScratch`, `MaskTriScratch`,
`MaskSourceScratch` (138–145).

`CollectVisibleMaskRects` **deliberately reuses the fit's buffers**. Introducing a
coroutine, an `async` step, or a second driver instance silently corrupts both the
content fit and the depth mask, with no exception.

**Proposal:** one shared header comment above the buffer block in each file,
stating the contract as a *requirement on future changes*, not as a description.
**Unification is not possible and not desirable** — the buffers are already as
shared as they can safely be. §4.2's partial split preserves this exactly (same
type, same `private static` fields).

### 7.3 The sorting / render-queue ladder has no shared constant, spanning four files

`cap face 1 → dust 2 → keycap label 3 → panel canvases & StatPanelSurface 10 →
modal host 1000 → grab bar & ModalCloseButton 1100 → dropdown Blocker 3999 →
dropdown List 4000 → ray visuals 5000`; and on the queue axis `BoardLit (Geometry)
→ depth mask 2999 → UI/labels 3000`, never the game HUD font's `4003 + Always`.

Introducing a shared constants class would be a *real* improvement — and it is
**Tier 2 at best and arguably Tier 3**, because the literals are spread across
`CanvasConversion`, `ModalCloseButton`, `GrabbableModal`, `StatPanelSurface`,
`NativeButtonSkin`, `ButtonCluster` and `PanelGrabHandle`, and several are passed
as default parameter values. **Not proposed for this refactor.** Instead: one
comment block, in `CanvasConversion` (the file that owns the 1000 tier), listing
the full ladder and the files that own each rung, referenced from each site by a
one-liner. Guard: empty.

### 7.4 `ButtonCluster.SetVisible` asymmetry — leave, per the registry

`if (_visible == visible) { if (!visible) return; }` — early-returns on a repeat
*hide*, falls through on a repeat *show*. The registry flags this as the one
asymmetry in the button files with no stated reason, and recommends leaving it and
adding a comment only after the user confirms. **I concur; no action.**

---

## 8. Small Tier 0 items

### 8.1 `MixedReality.KeepMenusUnclipped` — an empty method with two call sites

`src/GloomhavenVR/Core/MixedReality.cs:138`: `internal static void
KeepMenusUnclipped(bool wanted) { }`, documented as *"now a no-op kept only for
its call sites"*. Call sites: `ModalFallback.cs:800` (in `Detach`) and
`ModalFallback.cs:1135` (in `Tick`).

**Proposal:** delete the method and both calls **in one commit**. Removing one
side only leaves either a dangling call (build break) or a permanently-dead
method. Fix the stale doc (§2.3) in the same or a prior commit.

**Tier:** 0. **Guard:** `MixedReality.KeepMenusUnclipped` disappears;
`ModalFallback` loses one call in `Detach` and one in `Tick`. **Diff confined to
exactly two types.** **Risk:** nil — the body is empty; an empty method call is
still emitted in Release, so this is a genuine (tiny) IL change with no
behavioural content.
**Note:** this crosses `Core/` ↔ `WorldUI/`. Charter §7 says one subsystem per
commit; this item cannot honour that. Flag it for the user as a deliberate,
two-file exception.

### 8.2 Already covered above

- `FlatScreenStereo.Fmt` (line 1929) — §3.6.
- `FlatScreen._backdropMenuQueue` (line 241) — §3.6.

### 8.3 `WorldUIConfig.cs` is clean — a positive finding

I ran the census the `CENSUS.md` method note prescribes (is `<name>` referenced
anywhere outside its own config file?) over all **65** `ConfigEntry` fields in
`WorldUIConfig.cs`. Exactly one came back with no external reference —
`DevForceConvert` — and it is read *inside* `WorldUIConfig.cs` itself, by
`ConversionActive` (line 584). **`WorldUIConfig` has zero dead entries.**

The config-hygiene problem in this subsystem is confined entirely to
`FlatScreenStereo`'s own 19 private `s_map*` binds (§3). That is worth stating in
`PLAN.md`: the fix is local, not systemic.

---

## 9. Findings that are NOT refactor items

### 9.1 Tier 3 observation for the user — the combat log does not use the shared scale floor

Found while verifying §2.4, and deliberately **not** proposed as a change.

`GrabbableModal` (lines 259, 315) and `SettingsPanel` (lines 524, 590, 673) clamp
with `PanelGrabHandle.MinScale` (**0.15**). `CombatLogSurface` clamps with a
hard-coded **0.5** in three places:

| Line | Statement |
|---|---|
| 273 | `* Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f)` |
| 378 | `_frame.localScale = Vector3.one * Mathf.Clamp(WorldUIConfig.CombatLogScale.Value, 0.5f, 2f)` |
| 552 | `WorldUIConfig.CombatLogScale.Value = Mathf.Clamp(_frame.localScale.x, 0.5f, 2f)` |

Line 552 is the consequential one: a two-hand pinch below 0.5× is **persisted as
0.5**, and line 378 re-applies it on the next show. That is precisely the failure
the `MinScale` field comment warns about — *"would silently re-cap what the
two-hand pinch just shrank, so the user's pinch would appear to do nothing at the
bottom of its range"*.

**But it may be deliberate:** the combat log's config description
(`WorldUIConfig.cs:495`) says *"clamped 0.5-2"*, and its class doc says the same —
so the code, the config text and the doc all agree with each other. The registry
entry names `GrabbableModal.Tick` and `SettingsPanel` as the reusers of the shared
constant and does **not** name `CombatLogSurface`.

So this is one of three things, and I cannot tell which from the code:
a floor the combat log deliberately keeps; an owner that was missed when
`MinScale` was lowered from 0.5; or a genuine "pinch does nothing" bug the user
has not reported yet.

**This is Tier 3 — a behaviour question, not a cleanup.** No change proposed.
Recorded here, and it belongs in `PLAN.md` as a question for the user, not as a
work item. Whatever the answer, the three comments in `PanelGrab`/`GrabbableModal`
that state 0.5 for the **generic** handle are stale regardless (§2.4).

### 9.2 Explicit "leave it as it is" findings

Legitimate outputs of this review. Each was checked and is **not** a defect.

| Item | Why it stays |
|---|---|
| `TrayControlDockSurface` (whole surface, `_controls = Array.Empty<>()`, four `false` constants) | The four `false` constants are what other modules read to decide whether to draw their mod-button twin — removing them silently hides board buttons. The surface is the only implementation of the native-widget dock and a documented config escape hatch (`TrayNativeControls`). Deleting it is a **user decision (Tier 3)**, not a cleanup. |
| `[WorldUI] ModalStyle = "screen"` | Retained deliberately in `1c4fcd8` as the fallback path, and a window that *fails* to convert raises the full screen automatically — so the machinery is live regardless. |
| `[WorldUI] ScreenParallaxScale` | Looks orphaned only because `ScreenWindowRecess` and the frame quads around it were reverted (`82affec`). Kept **on purpose**; it amplifies real scene depth. Live and read (line 709). |
| `DevPanels` | Charter §5 protects debug-menu-only features. Gated behind two config flags. |
| `SettingsPanel.BoardButtonRowsVisible` gating on `DebugElement.Generic` | Reads like a bug; is not. The `BoardButtons` enum member was correctly deleted and issue 6 merged the two because they are the same physical Confirm/Undo caps. The registry entry exists specifically to stop a reviewer "fixing" this. **Confirmed still true in the current source.** |
| `EscMenuInputBlock` self-registering from `InputModeGuard.Tick` instead of `WorldUIModule.Init` | A legitimate consolidation candidate, but moving it changes *when* the patch lands and its gate would have to stay `IsRunning` (not `ConversionActive`) — see §7.1. **Tier 3; not proposed.** |
| `FlatScreen.SelectBases`, `TickStackClears`, `TickNoUiWatchdog` (two latches), `HostLateSync`, the `_ZTestMode`/`unity_GUIZTestMode` pair, the immediate+queued input writes, the re-warp of the latched pixel | Every one is a "redundant second write that IS the fix" (registry standing warning #4). Not touched by any proposal here, and §4.6's split keeps each method whole. |

---

## 10. Suggested execution order for `PLAN.md`

Ordered by value ÷ risk. Each line is one commit unless noted.

| # | Item | Tier | Guard expectation |
|---|---|---|---|
| 0 | Add `check --permutation` to `scripts/refactor-guard.sh` | — | n/a (tooling) |
| 1 | §2.1 `EnemyRevealSurface` — 7 comment strata | 0 | **empty** |
| 2 | §2.4 the `0.5×–2×` floor in 6 doc comments | 0 | **empty** |
| 3 | §2.5 + §2.6 + §2.7 `ModalFallback` ModalUI claim, 3 numeric cross-refs, `TrayControlDockSurface` status paragraph | 0 | **empty** |
| 4 | §2.2 `FlatScreenStereo` class doc + 3 field docs | 0 | **empty** |
| 5 | §2.3 + §8.1 `KeepMenusUnclipped` doc, method, 2 call sites | 0 | confined to `MixedReality` + `ModalFallback` |
| 5a | §3 config: all 19 map descriptions marked DEPRECATED | 0 | **empty** |
| 5b | §5 three `#pragma` sites + the five-flag header comment | 0 | **empty** |
| 6 | §3.1 mesh-UV island (9 methods, 8 fields, 6 event hooks) | 0 | confined to `FlatScreenStereo` |
| 7 | §3.2 `ReconcileMapDeferred` + `MapEffect` + `RestoreStrippedEffects` | 0 | confined to `FlatScreenStereo` |
| 8 | §3.3 ambient boost + map light (producer **and** restorer) | 0 | confined to `FlatScreenStereo` |
| 9 | §3.4/3.5/3.6 orphans (`MapAlbedoUseOriginalMat`, `Fmt`, `_backdropMenuQueue`) | 0 | confined to `FlatScreenStereo` + `FlatScreen` |
| 10 | §4.1 move 4 types out of `CanvasConversion.cs` | 1 | **empty** |
| 11 | §7 constraint comments (4 gates, scratch buffers, sorting ladder, 6.1/6.2 cross-refs) | 0 | **empty** |
| 12 | §4.6 `FlatScreen` → 5 partials (one commit per partial) | 1 | permutation on `FlatScreen` |
| 13 | §4.4 `SettingsPanel` → 5 partials | 1 | permutation on `SettingsPanel` |
| 14 | §4.2 `CanvasConversion` → 3 partials | 1 | permutation on `CanvasConversion` |
| 15 | §4.3 `FlatScreenStereo` → 5 partials (one commit each) | 1 | permutation on `FlatScreenStereo` |
| 16 | §4.5 `ModalFallback` → 8 partials (nested types first) | 1 | permutation on `ModalFallback` |
| — | §6.1 `HoverPanelWatch` extraction | 2 | **not recommended** — user decision |
| — | §7.3 shared sorting-constant class | 2/3 | **not recommended** |
| — | §9.1 combat-log 0.5 scale floor | 3 | **question for the user, not a work item** |

Items 1–11 are all Tier 0 and **seven of them have a provably empty guard diff**.
They deliver the correctness value (stale comments, lying config, ~500 dead
lines) before any file is split. If the user wants to stop after item 11, the
subsystem is meaningfully better and not one statement has moved.

## 11. What I did not do

- No source file was modified; nothing was committed.
- I did **not** run `refactor-guard.sh check` — there is nothing to check yet.
  I did run a full `dotnet build -c Release` to confirm the warning set
  (succeeded, 4 warnings) and to verify that the §3 reachability claims are
  consistent with what the compiler sees.
- Reachability for §3 was established by full-repo `grep`, a per-field line
  census, and charter §5 (Harmony inventory, Unity messages, reflection strings,
  config keys, `docs/` grep). It was **not** established by deleting the code and
  seeing if it compiles — that is Phase 4's job, and the compiler is the final
  arbiter there.
- I did not audit `Cards/`, `Hands/`, `Net/`, `Rig/`, `Board/` or `Core/`, except
  for the two `Core/MixedReality.cs` items that a WorldUI change must be paired
  with (§2.3, §8.1).
