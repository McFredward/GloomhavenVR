# REVIEW — lane worldui-frame, sub-reader B
# Set: src/GloomhavenVR/WorldUI/{Options,Grab,Patches}/  (~33 372 lines, 50 files)

**Base SHA:** `e26371e401a7d41f4c08324ebed5b133aaa6c51b`
(`docs(refactor-2026-09): review §6 — WorldUI/Conversion + WorldUI/Materialise (F-46…F-63)`;
`60beaa1f` IS an ancestor, so the base is current for the lane.)
**Worktree:** `/home/claw/gloomhaven_vr/.claude/worktrees/agent-a236ff8d959582747`
**Read-only pass.** Finding ids B-01…; F-01…F-63 are taken by the lane review.

## Log tiers, verified at HEAD (`src/GloomhavenVR/Core/VRLog.cs`)

| method | line | gate | prints at shipped default `LogLevel = Info`? |
|---|---|---|---|
| `Error`  | 106/111 | `Level >= VRLogLevel.Error`   | YES |
| `Alert`  | 125/130 | `Level >= VRLogLevel.Warning` | YES |
| `Note`   | 138/143 | `Level >= VRLogLevel.Info`    | YES |
| `Warn`   | 153/158 | `Level >= VRLogLevel.Debug`   | **NO** |
| `Info`   | 163/168 | `Level >= VRLogLevel.Debug`   | **NO** |
| `Debug`  | 173/178 | `Level >= VRLogLevel.Debug`   | **NO** |

`VRLog.Level` initialises to `VRLogLevel.Info` (line 91) and is overwritten from `[General] LogLevel`
at bind. Confirmed shipped default below.

---

## Findings

### B-01 — F-53 #4 confirmed at HEAD: `InkReleaseDeadBandPx` is still `private`, still copied BY VALUE, and both files are in THIS lane

- **File:line:** `src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs:420`
  (`private const float InkReleaseDeadBandPx = 32f;`) ·
  `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs:5946`
  (`private const float HitRectShrinkDeadBandPx = 32f;`), doc at `:5942-5945`.
- **Class:** duplication. **Tier:** 2.
- **Evidence.** The Fit copy's own `<summary>` names its source in so many words:
  > `/// considered. BY VALUE from <c>GrabbableModal.InkReleaseDeadBandPx</c> (32) — one 32 px`
  > `/// quantum, the same dead band the capture frame's shrink hysteresis uses, so all three`
  > `/// instruments agree about what "smaller" means.`
  and the `GrabbableModal` side is a hardware-settled number with a named round behind it —
  `GrabbableModal.cs:336`: *"NOTHING WAS TRADED AWAY: `InkReleaseConsecutive` is still 3 and
  `InkReleaseDeadBandPx` is still 32 px, so quest_überlap.jpg's protection is intact to the
  pixel."* — and `:263` *"Lowering `InkReleaseDeadBandPx` or `InkReleaseConsecutive` now would
  trade away the …"*. Nothing enforces the agreement the Fit doc asserts.
  Both types are `GloomhavenVR.WorldUI.*` in ONE assembly (confirmed from the generated XML:
  `F:GloomhavenVR.WorldUI.GrabbableModal.InkReleaseDeadBandPx` and
  `F:GloomhavenVR.WorldUI.CanvasConversion.HitRectShrinkDeadBandPx`), and both files are lane
  **worldui-frame**, so `internal` reaches across with no cross-lane API.
  Readers of the Grab constant: `GrabbableModal.cs:613-616` (`Receded`), `:3637`.
  Readers of the Fit copy: `.3.Fit.cs:6213-6216`, `:6243-6246`.
- **Proposed action.** `private const float InkReleaseDeadBandPx` → `internal const float`
  (GrabbableModal.cs:420); replace `.3.Fit.cs:5946`'s literal with
  `= GrabbableModal.InkReleaseDeadBandPx;` and rewrite the summary's "BY VALUE from" sentence to
  say it now REFERENCES it. **No numeric change** — 32f either way. `Net/NetProtocol.cs:10642`
  mentions the name in prose only; leave it.
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.GrabbableModal` (accessibility
  only) and `GloomhavenVR.WorldUI.CanvasConversion` (the const's *value* is inlined by the
  compiler, so the decompiled field may read `32f` in both before and after — an EMPTY diff on
  `CanvasConversion` is the expected outcome, not a red flag). Any change to the NUMBER = revert.
- **Risk if wrong:** none behavioural; a const is compile-time.
- **Cross-lane:** no. `WorldUI/Grab/` and `WorldUI/Conversion/` are both worldui-frame.

### B-02 — `GrabbableModal.OnGrabFinished`'s "NEVER SILENTLY" fallback warning is silent at the shipped level

- **File:line:** `src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs:940-956` (the `if (!inkPivot)`
  branch), and its comment at `:941-942`; the epilogue line at `:970-977`.
- **Class:** risk-gap. **Tier:** 3 (a one-word promotion).
- **Evidence.** The comment above the emitter states the requirement in capitals:
  > `// NEVER SILENTLY. A fallback here reproduces the exact defect this change fixes, and it`
  > `// would look identical to the fix not working at all.`
  and the emitter one line below is `VRLog.Warn("WorldUI", $"MODAL WINDOW: '{_logName}' released
  after a move — RE-FACING ABOUT THE FRAME ORIGIN, which is the pre-ModBuild-240 behaviour and the
  thing the user reported: …")`. `VRLog.Warn` gates on `Level >= VRLogLevel.Debug`
  (`Core/VRLog.cs:153`), and the shipped default is `VRLogLevel.Info`
  (`Defaults/Defaults.Plugin.cs:23`), so at the level every hardware log we have was taken this
  branch prints **nothing** — i.e. it IS silent, which is precisely what the comment forbids, and
  the reported symptom (a window swinging two thirds of a metre through an arc on release) is
  indistinguishable from "the fix did not work".
- **Not per-frame.** `OnGrabFinished` is `IPanelGrabOwner.OnGrabFinished` — one call per release
  gesture. It sits *below* the epsilon early-out at `:931`, so it fires only on releases that
  actually turn the window. Safe to promote.
- **The sibling epilogue at `:970`** (`VRLog.Info`, *"THE DRAWN WINDOW DID NOT MOVE … the mod-owned
  frame origin was carried {frameMovedMm:F0} mm around it"*) is the ModBuild 240 acceptance
  measurement and is likewise silent. It is per-release too, but the *verdict* half of it already
  prints: `WindowReFacePolicy.cs:132-140` emits an HW-VERIFY `VRLog.Note` `WINDOW RE-FACE: …` on
  every release. Recommendation: promote the **fallback warning only** (it names a defect), and
  leave the epilogue at `Info` — one line per release from an instrument that already has a
  printing verdict line is not worth the second line.
- **Proposed action.** `VRLog.Warn` → `VRLog.Alert` at `:944` (the failure mode is player-visible
  and is a standing user report — the documented definition of `Alert`), and add `// HW-VERIFY`
  above it so `scripts/check-hw-verify.py` pins the tier. **String unchanged** — `check-surface.py`
  owns every token in it.
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.GrabbableModal`, one call
  target (`VRLog.Warn` → `VRLog.Alert`); no string literal differs.
- **Risk if wrong:** one extra warning per release *only* when the ink union is missing — which
  the file argues should never happen for an accepted window.
- **Cross-lane:** no.

### B-03 — `WINDOW RE-FACE` prints `RE-FACING` for releases that then write nothing (sub-epsilon)

- **File:line:** `WorldUI/Grab/WindowReFacePolicy.cs:131-141` (the HW-VERIFY `Note`) vs
  `WorldUI/Grab/GrabbableModal.cs:929-932` and `WorldUI/Surfaces/SurfaceGrabBar.cs:522`.
- **Class:** risk-gap (an instrument that slightly overstates). **Tier:** n/a — **leave it**.
- **Evidence.** The policy is asked *first* (`GrabbableModal.cs:899`), the epsilon test runs
  *after* (`:931`: `if (Quaternion.Angle(_frame.rotation, facing) < ReFaceEpsilonDeg) return;`).
  So a release where the drag already left the window facing the player logs
  `WINDOW RE-FACE: MODAL WINDOW '…' released — RE-FACING` and then writes no rotation.
- **Why leave it.** The line's own stated job is to report **which rule decided**, and the sentence
  in it is explicit about that (*"a dial set to Never must produce 'keeping its angle' for modal
  windows AND for floating decision panels alike"*). Moving the Note below the epsilon test would
  be a frame-order/structure change in three owners for a wording nuance, and the token
  `WINDOW RE-FACE` is surface-checked. Recorded so the next reader does not mistake a
  `RE-FACING` line with no visible turn for a fault.
- **Cross-lane:** would be (SurfaceGrabBar is worldui-front) — another reason not to.

### B-04 — `OptionsToggle.LogOptionsKey`'s `beforeNames` loop dereferences `_censusBefore[i].name` with no null test, where both sibling loops have one

- **File:line:** `src/GloomhavenVR/WorldUI/Options/OptionsToggle.cs:389-391`
  (`for (int i = 0; i < before && i < 4; i++) sb.Append(…).Append(_censusBefore[i].name)…`),
  against `:368-369` (`if (w == null || w.IsOpen) continue;`) and `:380-381`
  (`if (w == null || _censusBefore.Contains(w)) continue;`) in the SAME method.
- **Class:** defect (fake-null hazard). **Tier:** 3.
- **Evidence, end to end.**
  1. `CensusOthers(_censusBefore)` (`:277`, body `:341-353`) captures live `UIWindow`
     REFERENCES — not names — for every registered window that `IsOpen` and is outside the key's
     domain.
  2. Between that capture and `LogOptionsKey` (`:334`, declared `:362`), the tap runs `CloseAll(menu, st)` (`:287`)
     or `OpenMenu` (`:302`), i.e. the game's whole `Hide()`/`Show()` cascade **on this call
     stack** — the method's own comment at `:330-333` says exactly that: *"anything the mod's
     CloseAll or the game's own Show/Hide cascade did to another window on this call stack is
     visible right here"*.
  3. The two loops that read the captured references AFTER that cascade both guard against a
     destroyed object (`w == null`). The third one does not. On a destroyed `UIWindow`,
     `.name` raises `MissingReferenceException` — a destroyed `UnityEngine.Object` is fake-null
     for `==` but throwing for member access.
  4. Observed vs expected: expected — a `OPTIONS KEY press #n` line naming the before-set;
     observed — a throw out of `LogOptionsKey`, which aborts the rest of the tap frame
     (`LogTapCost` at `:336` never runs) and, because the driver step is wrapped, is swallowed
     into a guard line rather than into the `OPTIONS KEY` instrument. The instrument the
     2026-09-03 ruling is checked with would go silent on exactly the press that broke it.
- **Honest reachability caveat.** I am *inferring* the destruction, not demonstrating it: I did not
  find a game path in `/home/claw/gloomhaven_vr/decompiled/` that destroys a registered `UIWindow`
  inside `Hide()`. The finding is that the method's own two other loops treat this as possible and
  this one does not — an internally inconsistent guard, which is the demonstrable part.
- **Proposed action.** Mirror the sibling guard, minimally:
  `UIWindow bw = _censusBefore[i]; if (bw == null) continue;` before the append (and keep the
  separator logic keyed off whether anything has been appended). No string literal changes.
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.OptionsToggle`.
- **Risk if wrong:** the `beforeNames` list could print one fewer name in a case it previously
  threw in. There is no case where it prints something different and does not throw today.
- **Cross-lane:** no.

### B-05 — the ESC-menu resolution chain's three anomaly lines are all at a tier the shipped log drops

- **File:line:** `WorldUI/Options/OptionsToggle.cs:573-577` (`ranked.Count == 0` — *"no ESCMenu
  object exists"*), `:681-687` (`SECOND CHANCE TOOK`), `:674-676` (`second-chance … ALSO refused`),
  `:705-711` (`ESC MENU RESOLVED`, the only line that names `_menuSource`), `:550-557`
  (`cache DROPPED`), `:279-281` (`[OptionsToggle] X tap on …`).
- **Class:** risk-gap. **Tier:** 3 (word-level promotions), **but only two of the six.**
- **Evidence.** All six are `VRLog.Info` or `VRLog.Warn`, i.e. `Level >= Debug`, i.e. nothing at
  the shipped `LogLevel = Info`. What each is FOR:
  - `.planning/OPTIONS-MENU-NEVER-BLOCKED.md:147` names `[OptionsToggle] X tap on
    UIScenarioEscMenu (source: Singleton<ESCMenu> …)` as the line that identifies WHICH menu was
    driven *"without a crash"* — the whole point of the ModBuild 289/290 work.
  - `.planning/STATE.md:1215` reasons from `[OptionsToggle] X tap … -> OPEN`.
  - `WorldUI/Modal/ModalFallback.7.Close.cs:253` reconstructs the ModBuild 407 story-window
    deadlock from `[OptionsToggle] X tap on UIMapEscMenu … actuallyOpen=True -> CLOSE` followed by
    `OPTIONS TAP: close routed through CloseFloatedWindow`.
  So three separate documents triage from tokens that a default-level log does not contain.
- **What already prints, and why I am NOT proposing all six.** `LogOptionsKey`'s HW-VERIFY
  `VRLog.Note` (`:427-445`) prints `OPTIONS KEY press #n: {menuName} OPENED/CLOSED …` on every
  press up to the caps, so *which menu* and *what the press did* ARE answered at the shipped level;
  `OPTIONS TAP: … DID NOT OPEN` is already `VRLog.Error` (`:327`). The gap is narrower than "six
  silent lines": it is the **anomaly** branches.
- **Ranked judgement (per-frame safety stated for each):**
  | line | fires | per-frame? | verdict |
  |---|---|---|---|
  | `:573` *no ESCMenu object exists* | once per TAP when the pause menu cannot be opened at all | no — gated on `NonDominantHold.ShortTapThisFrame` (`:568`) | **PROMOTE to `Alert` + `// HW-VERIFY`.** This IS "the X button did nothing", a standing user ruling, and it is the one branch that has no printing counterpart (it `return null`s before `LogOptionsKey` is ever reached). Bounded by the player's thumb, ~1/tap. |
  | `:681` `SECOND CHANCE TOOK` | when the first-choice menu refused to open | no — inside the failed-open branch | **PROMOTE to `Alert`.** A rescue that fired is an anomaly with a crash history (ModBuild 289/290) and `LogOptionsKey` reports only the eventual success. |
  | `:674` second-chance also refused | same branch | no | leave at `Warn` — the `:327` `VRLog.Error` covers the outcome. |
  | `:705` `ESC MENU RESOLVED` | on a menu CHANGE only (`!ReferenceEquals(live, _menu)`, `:536`) | change-gated, effectively once per scene | leave — the source string is diagnostic detail, not a verdict. Consider adding `_menuSource` to the `OPTIONS KEY` Note instead, which already prints; that is a **string change to a surface-checked token** and therefore not a refactor-phase move. |
  | `:550` `cache DROPPED` | on a scene-change with an empty singleton | change-gated | leave at `Info`. |
  | `:279` `[OptionsToggle] X tap` | every acted-on tap | no, but ~1/tap and verbose | leave at `Info`; the Note line answers the same question inside the cap. |
- **Proposed action.** Two one-word promotions (`:573` `Info`→`Alert`, `:681` `Warn`→`Alert`), plus
  `// HW-VERIFY` above `:573`. Strings untouched.
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.OptionsToggle`, two call
  targets.
- **Risk if wrong:** at most one warning per tap in a session where the pause menu is broken —
  which is the session you want the lines from.
- **Cross-lane:** no.

### B-06 — `LogOptionsKey` does not count a window that was DESTROYED (rather than hidden) by the tap

- **File:line:** `WorldUI/Options/OptionsToggle.cs:368-369`.
- **Class:** risk-gap (instrument blind spot). **Tier:** n/a — **leave it, recorded.**
- **Evidence.** `if (w == null || w.IsOpen) continue;` — the `w == null` arm skips a destroyed
  window, so it is counted neither as still-open nor as `HID`. A tap that DESTROYED a foreign
  window would read `TOUCHED NOTHING ELSE`, which is the ruling's own falsifier reading clean on
  a worse violation than the one it was built for.
- **Why leave it.** Fixing it means a new verdict word in a `check-surface.py`-owned string, i.e.
  a token change, which §4 forbids in this phase. Recorded so a later round can add a
  `DESTROYED:` clause deliberately rather than discover the blind spot from a log.
- **Cross-lane:** no.

### B-07 — the two ModBuild 336 HW-VERIFY verdicts are unreachable on their own failure path, so their ABSENCE cannot be read

- **File:line:** `WorldUI/Options/VROptionsTab.1.Inject.cs:600-616` (`_loggedEscapableLeave` gate,
  then the `VRLog.Note` HW-VERIFY) and its `catch` at `:618-623` (`VRLog.Warn`); the twin at
  `:662-681` (`_loggedAreaLeave` + `VRLog.Note`) and its `catch` at `:683-687` (`VRLog.Warn`).
  Latches declared at `:146-147`.
- **Class:** risk-gap (an instrument that cannot answer the question it was built for). **Tier:** 3.
- **Evidence, end to end.**
  1. Both Notes are marked `// HW-VERIFY` and both say so explicitly — `:603-605`: *"this is the
     verdict the ModBuild 336 round is waiting on — it must stay at a tier the default log level
     prints, **or the round comes back unable to say whether the two windows were separated at
     all**."*
  2. Each Note is the LAST statement of its `try`. The mutating calls precede it
     (`win.escapeKeyAction = None; UIWindowManager.UnregisterEscapable(win);` at `:594-598`;
     `_inputArea.Destroy();` at `:660`).
  3. If any of those throws, control leaves for the `catch`, the `VRLog.Warn` there prints
     **nothing** at the shipped level (`Level >= Debug`), and — the load-bearing part —
     `_loggedEscapableLeave` / `_loggedAreaLeave` are **never set**, so the Note is not printed
     either, on this open or any later one that also throws.
  4. Observed vs expected: expected — the round reads either the verdict Note or a line saying it
     failed. Observed — an empty log for both, which is byte-for-byte the same picture as
     "the patch never ran" and as "`IsStandalone` is false so `LeaveInputAreaStack` returned at
     `:656`". Three distinct states, one reading. This is exactly the ambiguity
     `MenuExitLatchGuard.ArmOnce` (`Patches/MenuExitLatchGuard.cs:87-113`) was added to remove for
     its own guard, and the fix is not applied here.
- **Reachability of each failure branch.** `LeaveSharedStacks` runs exactly once, from `:354`
  (injection), so its `catch` fires at most once per session. `LeaveInputAreaStack` runs on **every
  open** (`:863`, `:932`), so its `catch` can repeat once per open — human-rate, not per-frame,
  but un-deduped.
- **Proposed action (minimal, two parts).**
  1. `:620` `VRLog.Warn` → `VRLog.Alert` — it fires at most once per session and it is the negative
     half of an HW-VERIFY verdict.
  2. `:685` — give it its own one-shot latch (`_loggedAreaLeaveFailed`) and then `VRLog.Alert`, so
     the repeat-per-open is bounded exactly the way the success line is. Do **not** promote it
     without the latch.
  Strings untouched in both cases.
- **Guard expectation:** `CHANGED` confined to the `VROptionsTab` type — two call targets and one
  new `private static bool`.
- **Risk if wrong:** one extra warning per session (part 1) / per session (part 2 with the latch).
- **Cross-lane:** no.

### B-08 — `ConfigCatalog.Tooltip`'s `<summary>` is orphaned onto `Hint`, which now carries TWO summaries; `Tooltip` has none

- **File:line:** `src/GloomhavenVR/WorldUI/Options/ConfigCatalog.cs:1695-1707` (the orphan),
  `:1708-1718` (`Hint`'s real doc), `:1719` (`Hint`), `:1726` (`Tooltip`, undocumented).
- **Class:** structure-naming / doc-drift. **Tier:** 0 (comment-only motion).
- **Evidence — demonstrated, not inferred.** The generated documentation file at HEAD proves it:
  `src/GloomhavenVR/bin/Release/net472/GloomhavenVR.xml:171914` opens
  `<member name="M:GloomhavenVR.WorldUI.ConfigCatalog.Hint(…)">` and contains **two consecutive
  `<summary>` elements** (`:171915` and `:171928`), and there is **no
  `<member name="M:GloomhavenVR.WorldUI.ConfigCatalog.Tooltip(…)">` element in the file at all** —
  the only occurrence of that name is a `<see cref>` from inside `Hint`'s second summary
  (`:171931`).
  The first block describes `Tooltip`'s job word for word — *"where the entry lives, what the
  config file says about it, its default and range, and — honestly — whether the change is live"* —
  which is exactly what `Tooltip` (`:1726-1753`) builds and exactly what `Hint` (`:1719-1724`)
  does NOT do; `Hint` returns the description paragraph alone. The second block says so itself:
  *"Deliberately not `Tooltip`, which is the power-user readout … Same source text, none of the
  scaffolding."*
  This is the class `an-assertion-in-the-source-is-a-hypothesis` / F-28 names: a `<summary>` whose
  member moved silently becomes the doc of the next member.
- **Proposed action.** Move the `:1695-1707` block down to sit immediately above
  `internal static string Tooltip(ConfigItem item)` at `:1726`. Nothing else; no wording change.
- **Guard expectation:** **empty** — XML doc comments do not appear in the `ilspycmd -p` snapshot
  (CHARTER §3, measured: 0 `<summary>` tags in the whole snapshot).
- **Risk if wrong:** none. `scripts/check-docs-i18n.py` should stay green (no user-facing string).
- **Cross-lane:** no.

### B-09 — STALE-DOC-REFS row `WorldUI/Options/ConfigCatalog.cs:166 ResolveStep` — the corrected cref is `ResolveSteps`

- **File:line:** `WorldUI/Options/ConfigCatalog.cs:166` (the demoted `<c>ResolveStep</c>`), and two
  more prose restatements of the same stale singular at `:690` (*"never reached ResolveStep at
  all"*) and `WorldUI/Options/ConfigSteps.cs:640` (which already spells it `ConfigCatalog.ResolveSteps`).
- **Class:** doc-drift. **Tier:** 0.
- **Evidence.** The live member is `private static void ResolveSteps(List<ConfigItem> items)` at
  `ConfigCatalog.cs:815`, called once from `:379` (`ResolveSteps(items);`). No member named
  `ResolveStep` exists anywhere in `src/` (`grep -rn 'ResolveStep\b'` returns only the three prose
  sites above plus `ResolveSteps` itself). The sentence around the demoted cref is still TRUE —
  *"the step depends on the largest magnitude in the entry's FAMILY, which is not knowable until
  every entry has been read"* is precisely what `ResolveSteps` (`:815-843`) does in its second pass
  over the whole catalog, reading `OwnScale(item)` per item.
- **Proposed action.** `:166` `<c>ResolveStep</c>` → `<see cref="ResolveSteps"/>`; `:690`
  `ResolveStep` → `ResolveSteps`; then **delete the row** from
  `.planning/refactor/STALE-DOC-REFS.md` (it is the only row in that file naming a file in this
  sub-set — see the verification table at the end).
- **Guard expectation:** empty (comment-only).
- **Risk if wrong:** none; `ResolveSteps` is `private` in the same type, so the cref resolves.
- **Cross-lane:** the STALE-DOC-REFS row edit is explicitly permitted (BRIEF §2: *"edit only the
  entries that name files in your set"*).

### B-10 — `VariantTiles.ResetVariantTiles` has no caller and its doc claims a contract it does not participate in — INERT, not dead

- **File:line:** `src/GloomhavenVR/WorldUI/Options/VariantTiles.cs:675-678`
  (`internal static void ResetVariantTiles() => TileSprites.Clear();`), doc at `:675-677`.
- **Class:** dead → reclassified **INERT** + doc-drift. **Tier:** 0 (comment only).
- **BRIEF §5 checklist, run item by item.**
  1. *Harmony target / annotated patch method?* No — no attribute, and it is absent from
     `docs/PATCH-INVENTORY.md`.
  2. *Unity message / serialized field?* No — `VariantTiles` is a `static class`.
  3. *Reflection / `nameof` / string literal?* No — a whole-repo grep for `ResetVariantTiles`
     (excluding `bin/`, `obj/` and the guard baseline) returns **one** hit: the declaration itself.
  4. *Config key?* No.
  5. *Log grep token a doc relies on?* No — it emits nothing.
  6. *Debug-menu entry?* No.
  7. *`tests/` shim pin?* No. (Contrast `ConfigSteps.ExplicitKeys` at `ConfigSteps.cs:563`, which
     LOOKS equally callerless in `src/` and is pinned twice by
     `tests/GloomhavenVR.WireTests/ConfigStepVectors.cs:367` and `:470` — that one must NOT be
     touched, and is the reason this checklist is worth running.)
  8. *Only writer of a field an instrument reads?* No — `TileSprites` (`:134`) is written at `:671`
     by `TileSprite`.
  So: no caller, and nothing outside the compiler pins it.
- **Why INERT and not dead — and the false claim.** The doc says it drops the sprites *"(module
  shutdown / hot reload), **matching `WorldUIAssets.Reset`'s contract**"*. `WorldUIAssets.Reset`
  (`WorldUI/WorldUIAssets.cs:104-111`) is called from `WorldUI/WorldUIModule.cs:220` on shutdown and
  clears `_bundle`, `_probed`, `_gameFont` — it does **not** call `ResetVariantTiles`, and nothing
  else does. The sentence asserts a participation that does not exist.
- **And there is no defect behind it,** which is why this is a comment finding rather than a wiring
  one: the `Sprite`s in `TileSprites` are built over `Texture2D`s from
  `Core/EmbeddedTexture.cs`, whose own `_cache` (`:34`) is **never** cleared on a module reset
  (that file has no `Reset` at all; the only `Destroy` is the decode-failure path at `:74`). So the
  cached sprites stay valid across a hot reload and a stale cache is a small leak, not a blank tile.
- **Proposed action.** Do **not** delete (deleting would remove the only mechanism, and the F-56/
  `a-write-inside-a-logger` precedent is to keep a mechanism and document it). Correct the two
  sentences: say the method is currently **UNCALLED**, that `WorldUIAssets.Reset` does not invoke
  it, and that the cache survives a reset harmlessly because `EmbeddedTexture` keeps its own
  textures alive. If someone later wants it wired, the call belongs in `WorldUIAssets.Reset` or
  `WorldUIModule.Shutdown` — **both are lane worldui-front**, so that is a `NEEDED-OUTSIDE` entry,
  not an in-lane edit.
- **Guard expectation:** empty (comment-only).
- **Risk if wrong:** none.
- **Cross-lane:** the doc fix is in-lane; any wiring would not be.

### B-11 — SIX one-shot self-disarms in this set are `VRLog.Warn`; the ONE that was fixed sits three files away and documents the rule it broke

- **File:line (the six):**
  | # | site | emitter | latch | what is lost |
  |---|---|---|---|---|
  | 1 | `WorldUI/Options/VROptionsTab.1.Inject.cs:1436-1440` | `VRLog.Warn` | `_degraded` `:1434` | **the entire VR settings menu, for the session** — *"no VR settings menu this session … The pause-menu VR row is not injected either"* |
  | 2 | `WorldUI/Patches/InputFieldFocusWatch.cs:165-167` | `VRLog.Warn` | `_degraded` `:164` | the push seam; falls back to *"its FindObjectsOfType sweep … it costs frame time again inside a scenario"* — the same sweep this file's `:141-146` records the hardware log measuring at **90-99 ms/s, worst 23 ms in one frame, the mod's most expensive step by a wide margin** |
  | 3 | `WorldUI/Patches/EscMenuInputBlock.cs:145-146` | `VRLog.Warn` | `_degraded` `:144` | *"Game's controller ESC-menu paths left vanilla (X may double-act)"* — the options key toggling twice per press |
  | 4 | `WorldUI/Patches/SettingsClickExemption.cs:242-245` | `VRLog.Warn` | `_degraded` `:241` | *"Pause/options-window clicks stay vanilla-gated (may be vetoed during scripted tutorials, or killed by a gate `NullReferenceException` during confirm waits)"* |
  | 5 | `WorldUI/Patches/Character3DDisplayRefcount.cs:252-258` | `VRLog.Warn` | `_resolved`+`_standDown` `:245`/`:251` | the refcount gate; *"the first one to close runs the game's ungated `character3D.Hide()` … which blanks the model out of the render texture the second window is still showing"* |
  | 6 | `WorldUI/Patches/PartyPreviewStorm.cs:227-233` | `VRLog.Warn` | `_resolved`+`_standDown` `:220`/`:226` | the redundant-rebuild suppression |
- **The reference implementation, in the same folder:** `WorldUI/Patches/EscMenuShowSafety.cs:114-124`
  — the identical *"Log the first failure and thereafter stay silent"* shape, at **`VRLog.Alert`**
  with a `// HW-VERIFY` marker at `:119`. Its sibling `Report` (`:145-171`) carries the paragraph
  that states the general rule (`:136-142`):
  > *"**AT THE ALERT TIER, NOT Warn (ModBuild 439, survey item B3).** … `VRLog.Warn` and
  > `VRLog.Info` both gate on `Level >= VRLogLevel.Debug`, so at the shipped default these lines
  > printed NOTHING and the doc's own requirement was false for eight builds. … The wordings are
  > untouched; only the tier moved."*
- **Class:** risk-gap + parallel-construction (one concept, seven implementations, one of them
  corrected). **Tier:** 3 — six one-word promotions.
- **Evidence.** Verified at HEAD: `VRLog.Warn` gates on `Level >= VRLogLevel.Debug`
  (`Core/VRLog.cs:153`); shipped default is `VRLogLevel.Info` (`Defaults/Defaults.Plugin.cs:23`).
  Each of the six is behind a one-shot latch set BEFORE the emitter, so **not one of them can
  print more than once per session** — there is no flood argument against any of them, which is
  what separates this cluster from the ordinary "a row could not be painted" catches in the same
  files (see B-13). Each names a player-visible or performance-visible consequence in its own
  string; three of them (1, 3, 4) name consequences that fall under the standing ruling *"it must
  ALWAYS be possible to open the options menu"*, which is the exact ruling
  `EscMenuShowSafety.Report`'s paragraph cites as the reason for `Alert`.
- **Proposed action.** `VRLog.Warn` → `VRLog.Alert` at all six emitters. **No string changes** —
  `check-surface.py` owns every token in them. Add `// HW-VERIFY` to sites 1, 2 and 5 only (the
  three whose absence is currently unreadable against a real symptom); leave the other three
  unmarked so `check-hw-verify.py`'s marked-site count stays meaningful.
- **Guard expectation:** `CHANGED` confined to six types — `VROptionsTab`, `InputFieldFocusWatch`,
  `EscMenuInputBlock`, `SettingsClickExemption`, `Character3DDisplayRefcount`,
  `PartyPreviewStorm` — one call target each, no literal differs. Anything else = collateral.
- **Risk if wrong:** at most six additional warning lines per session, each of which reports a
  feature that is off.
- **Cross-lane:** no — all six are in `WorldUI/{Options,Patches}`.

### B-12 — `PARTY PREVIEW STORM` and `CHARACTER 3D CADENCE` are a two-line attribution pair built to decide the next hardware round, and neither prints

- **File:line:** `WorldUI/Patches/PartyPreviewStorm.cs:257-283` (`VRLog.Info`) and
  `WorldUI/Patches/Character3DDisplayRefcount.cs:370` + `:391` (`VRLog.Info`).
- **Class:** risk-gap. **Tier:** 3 (one-word), **with a throttle caveat spelled out below.**
- **Evidence.** `PartyPreviewStorm`'s own line says what it is for, in the string:
  > *"HOW TO READ IT. This line is the ATTRIBUTION for the CHARACTER 3D CADENCE line: cadence
  > measures the OUTCOME …, this measures the two causes we can act on. … **(3) Both counters at 0
  > while the user reports flicker means neither cause is live and the next round must not be
  > spent here.**"*
  A line that directs where the next hardware round is spent, at a tier no hardware log carries.
  `CHARACTER 3D CADENCE` is the other half of the same reading and is also `VRLog.Info`.
- **Throttling, stated because it is the whole question.** `MaybeReport` (`:238-284`) is a real
  window: `ReportSeconds = 5f` (`:135`), the window start is advanced **before** the
  nothing-happened early-out (`:245-249`, so the cadence cannot be defeated the way
  `ActorPropBody.cs:933` was — I checked that specific inversion here and it is **correct**), and
  the line is suppressed entirely when both counters are zero. So the ceiling is **one line per
  5 s, and only while the player is hovering the roster**. `Character3DDisplayRefcount`'s window is
  `WindowSeconds = 1f` (`:120`) — one line per second under activity, which is four times noisier.
- **Proposed action — ONE of the two, not both.** Promote `PARTY PREVIEW STORM` (`:257`)
  `Info` → `Note` and mark it `// HW-VERIFY`; **leave `CHARACTER 3D CADENCE` at `Info`** — its
  1 s window makes it the noisy half, and the storm line already names the cadence line's verdict
  in prose, so one printing half is enough to decide whether the round is worth spending. The
  `CHARACTER 3D {kind}` line at `:391` is per-event and must stay at `Info`.
- **Guard expectation:** `CHANGED` confined to `PartyPreviewStorm`, one call target.
- **Risk if wrong:** up to 12 lines/minute during sustained roster hovering. If that is judged too
  noisy, the alternative is to widen `ReportSeconds` — but that is a **tuning value** and therefore
  out of scope (§4), so the honest choice is promote-as-is or leave it.
- **Cross-lane:** no.

### B-13 — `RE-ASSERT WATCH ARMED` promises "A CLOSING LINE IS PRINTED EITHER WAY"; on the two branches where no closing line follows, the reason is silent

- **File:line:** `WorldUI/Patches/MainMenuLogoSwap.cs:1898-1927` (`ArmWatch`) — the not-armed
  branch `:1902-1907` (`VRLog.Warn`), the armed line `:1913-1918` (`VRLog.Info`), the
  start-failure catch `:1924-1925` (`VRLog.Warn`) — against the closing line at `:2006`
  (`VRLog.Note`, prints).
- **Class:** risk-gap (the instrument's own stated guarantee is false in the shipped log).
  **Tier:** 3, two one-word promotions.
- **Evidence.** The armed line states the guarantee in the string:
  > *"A CLOSING LINE IS PRINTED EITHER WAY — if nothing ever reverted it will say so, **because a
  > guard that only speaks when it fires is indistinguishable from a guard that never ran.**"*
  Three exits from `ArmWatch`:
  1. `manager == null || !manager.isActiveAndEnabled` → `VRLog.Warn` at `:1902`, `return` at
     `:1911`. **No coroutine, therefore no closing line, and the Warn prints nothing.**
  2. `manager.StartCoroutine` throws → `VRLog.Warn` at `:1924`. **Same: no closing line, silent.**
  3. Normal → the coroutine runs and `:2006`'s `VRLog.Note RE-ASSERT WATCH CLOSED` prints.
  So in exactly the two cases the guarantee exists to cover, a default-level log contains **no
  MainMenuLogoSwap watch line at all** — which is byte-identical to "the patch never ran", the
  reading the sentence says it prevents.
- **Both are one-shot** (`ArmWatch` is called once, from the `Awake` postfix chain), so neither can
  flood.
- **Proposed action.** `:1902` and `:1924` `VRLog.Warn` → `VRLog.Alert`. Strings untouched. (The
  `:1913` ARMED line may stay at `Info`: when it is missing AND a closing line is missing, the two
  promoted lines now say which of the two happened.)
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.MainMenuLogoPlacement`
  (`ArmWatch` lives in the second type in this file, declared at `:770`), two call targets.
- **Risk if wrong:** two possible lines per session, on the failure path only.
- **Cross-lane:** no.

### B-14 — `MainMenuLogoSwap.cs` holds two top-level types (2 039 lines) — considered and DECLINED

- **File:line:** `WorldUI/Patches/MainMenuLogoSwap.cs:126` (`MainMenuLogoSwap`) and `:770`
  (`MainMenuLogoPlacement`).
- **Class:** structure-naming. **Tier:** 1 if taken. **Verdict: leave it.**
- **Evidence and reason.** BRIEF §6 makes "several **unrelated** top-level types" the finding, and
  CHARTER §2 makes length by itself explicitly not one. These two are one concept split by role:
  `MainMenuLogoSwap` owns the swap and the restore bookkeeping (`s_records`, `s_originalSprites`,
  `s_originalTextures`, `SwapUnder`, `SweepScene`), and `MainMenuLogoPlacement` owns the
  measurement/instrument half (`MeasureInk`, `PlaceOnBand`, `Census`, `MeasureDrawnInk`,
  `ArmWatch`, `WatchRoutine`) plus the `SwapRecord` type they share. The second type reaches into
  the first on nearly every method (`MainMenuLogoSwap.Records`, `.SweepScene`, `.RunWatchTick`),
  which is the opposite of an unrelated neighbour.
  A split would be free by the guard (CHARTER §3: moving a whole type to another file produces
  **nothing at all**, because the snapshot files types separately), so the cost is not the risk —
  it is that it buys nothing and adds a file to `GloomhavenVR.csproj`'s `MOVED` noise. Recorded so
  the next reader does not re-derive it.
- **Cross-lane:** no.

### B-15 — four more double-`<summary>` sites; two are orphans that stole a documented member's doc, and one carries a FALSE cross-lane claim

A scan of the whole sub-set for `</summary>` followed immediately by a second `<summary>` on the
same member returns exactly five sites. B-08 is the first; these are the other four. Every claim
below is verified against the generated `src/GloomhavenVR/bin/Release/net472/GloomhavenVR.xml`.

**(a) `ConfigCatalog.cs:1375-1379` — `LeadingWord`'s doc is stranded on `VariantWords`.**
- **Class:** structure-naming. **Tier:** 0.
- XML `:171827` `<member name="F:GloomhavenVR.WorldUI.ConfigCatalog.VariantWords">` opens with
  *"The key's leading word — the automatic cluster name inside an oversized section
  (\"FanArcSweepDegrees\" → \"Fan\", …). Bounded by the key length; never returns empty for a
  non-empty key."* — a description of a METHOD over a `HashSet<string>` field — then a second
  `<summary>` at `:171833` which is the field's real one. `grep -c
  'M:GloomhavenVR.WorldUI.ConfigCatalog.LeadingWord'` over the XML returns **0**: the method it
  describes (`ConfigCatalog.cs:1461`, `internal static string LeadingWord(string key)`) has no
  documentation at all.
- **Action:** move `:1375-1379` down to sit above `:1461`. Nothing else.

**(b) `GrabbableModal.cs:3233-3257` — `ReportBarPlacement`'s doc is stranded on `MouseoverLedger`.**
- **Class:** structure-naming. **Tier:** 0. **This is the largest of the four.**
- XML `:146654` `<member name="M:GloomhavenVR.WorldUI.GrabbableModal.MouseoverLedger">` opens with
  *"THE FALSIFIER, read back off the transform that was just written … One greppable line per
  window per (re-)capture, rate-limited"* — 25 lines describing the `GRAB BAR CLEARS THE INK`
  reporter, its two judged terms (`TO THE INK`, `BELOW THE FRAME`) and the ModBuild 238/239
  history, ending *"Silence on this cost another build."* `MouseoverLedger` (`:3275`) is a
  four-line expression-bodied string builder; it does not read a transform, is not rate-limited and
  prints nothing. The method actually described is `ReportBarPlacement` (`:3523`), the emitter of
  `GRAB BAR CLEARS THE INK` (`:3594`, `:3685`, `:3748`), and
  `grep -c 'M:GloomhavenVR.WorldUI.GrabbableModal.ReportBarPlacement'` over the XML returns **0**.
- **Action:** move `:3233-3257` down to `:3523`. Nothing else.

**(c) `VROptionsTab.10.Skin.cs:211-221` — a stale block ABOVE the alias, and it names this lane's own file as another lane's.**
- **Class:** doc-drift. **Tier:** 0. **This is the "private to another lane's file" pattern the
  brief flags, and here the claim is false in the same way.**
- Both blocks are the member's own (`internal const float HoverTintFadeSeconds =
  UguiTintFeel.HoverTintFadeSeconds;` at `:222`), so nothing was stolen — but the first is the
  PRE-move text and it now says something untrue:
  > *"Three sites: the action-row plate (VROptionsTab.2.Rows.cs), the variant picture tile
  > (VariantTiles.cs) and the modal close X (**WorldUI/Grab/ModalCloseButton.cs, not converted this
  > round — it is another lane's file**)."*
  `WorldUI/Grab/` is **lane worldui-frame** — this lane. And it WAS converted: at HEAD
  `WorldUI/Grab/ModalCloseButton.cs:433` reads `colors.fadeDuration =
  UguiTintFeel.HoverTintFadeSeconds;`. A whole-repo grep for `fadeDuration` finds exactly three
  assignment sites (`VROptionsTab.2.Rows.cs:826`, `VariantTiles.cs:637`, `ModalCloseButton.cs:433`)
  and **none of them is a bare literal any more** — R20 is CLOSED.
- **Action:** delete the first block's stale sentence pair and keep the second (the `MOVED to
  UguiTintFeel` note, which is accurate), or fold the surviving prose into the second block. Do
  NOT delete the reasoning about why the three PALETTES stay separate — that is a live decision.

**(d) `PanelGrab.cs:408` — a `<summary>` that should be two `<param>` tags.**
- **Class:** structure-naming. **Tier:** 0. **Smallest of the four.**
- `/// <summary><paramref name="logName"/>/<paramref name="logChannel"/> keep the owner's log
  identity ("Tray grab: …" etc.).</summary>` sits above `Init`'s real summary (`:409-413`). It uses
  `<paramref>` for `Init`'s own parameters, so it was written as parameter documentation and landed
  in a `<summary>` element.
- **Action:** turn it into `<param name="logChannel">` / `<param name="logName">` under the real
  summary, or fold the sentence into that summary. Either is comment-only.

- **Guard expectation for all four:** **empty** — XML doc comments do not reach the `ilspycmd -p`
  snapshot.
- **Risk if wrong:** none. `check-docs-i18n.py` must stay green.
- **Cross-lane:** none. (c) explicitly does NOT need `WorldUI/Buttons/UguiTintFeel.cs`, which is
  worldui-front and is already correct.

### B-16 — `ConfirmationBoxRescue`'s "that arm of the deadlock guard is INERT" is the ONE silent line in a file that is otherwise entirely at printing tiers

- **File:line:** `WorldUI/Patches/ConfirmationBoxRescue.cs:456-458` (`VRLog.Warn`), resolver
  declared `:430`, called from `:364` and `:388`.
- **Class:** risk-gap. **Tier:** 3 (one word).
- **Evidence.** Every other `VRLog` call in this file is at a tier the shipped log prints —
  `:209` `Error`, `:224` `Alert`, `:236` `Error`, `:246` `Note`, `:253` `Note`, `:258` `Error`,
  `:343` `Note` (HW-VERIFY `CONFIRMATION RESCUE ARMED`). The single exception is the line that
  reports **the guard being switched off**:
  > `"CONFIRMATION RESCUE: no ConfirmationBox.ShowGenericConfirmation overload with[out] a cancel
  > action was found — that arm of the deadlock guard is INERT for this build of the game."`
  The file's own HW-VERIFY comments say what is at stake: `:222` *"THE LINE THAT DECIDES THE NEXT
  HARDWARE ROUND"*, `:341` *"proof the rescue is LIVE. Its absence means the patch never ran at
  all"*. A missing arm is exactly the state `:341`'s reasoning cannot distinguish from a live one:
  `CONFIRMATION RESCUE ARMED` only prints on the first confirmation that actually reaches a
  patched overload, so with one arm inert the log carries neither the ARMED line for that arm nor
  any explanation.
- **Bounded by construction.** `ShowGenericConfirmation(bool)` is a Harmony `TargetMethod`
  resolver: two call sites, both at patch registration, **at most two lines per session**.
- **Proposed action.** `VRLog.Warn` → `VRLog.Alert` at `:456`. String untouched. (Not `Error`:
  the mod still works, the arm is simply absent — `Alert`'s documented meaning is exactly *"a
  feature could not install"*.)
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.Patches.ConfirmationBoxRescueTargets`,
  one call target.
- **Risk if wrong:** two possible lines per session, only on a game build whose signature changed.
- **Cross-lane:** no.

### B-17 — `RE-ASSERT FIRED` calls itself "the proof" of hypothesis (B) and is silent; the closing line carries only its count

- **File:line:** `WorldUI/Patches/MainMenuLogoSwap.cs:270-275` (`VRLog.Warn`) vs the closing
  `VRLog.Note` at `:2006-2018`.
- **Class:** risk-gap. **Tier:** 3 (one word) — **smaller than it looks, and the reason is here.**
- **Evidence.** The string says *"Something outside this patch is rewriting the logo after our
  Awake postfix ran — that is hypothesis (B), **and this line is the proof.** … further repairs
  on this graphic are silent."* So it is a per-graphic one-shot (bounded by
  `MainMenuLogoSwap.Records.Count`), and it is the only line that NAMES the graphic and the path
  (`rec.Origin`, `rec.Path`) that reverted.
- **What already prints:** `RE-ASSERT WATCH CLOSED` (`:2006`, `VRLog.Note`) carries
  `{repairs} repair(s), {lateSwaps} late graphic(s) swapped`. So *whether* hypothesis (B) is live
  IS answerable from a default-level log; *which graphic* is not.
- **Proposed action.** `VRLog.Warn` → `VRLog.Alert` at `:270`. It is a one-shot per swapped
  graphic (typically 1-3 records), and it converts a bare count into a named object.
- **Guard expectation:** `CHANGED` confined to `GloomhavenVR.WorldUI.MainMenuLogoSwap`, one call
  target.
- **Risk if wrong:** one line per swapped graphic, once, and only when something is genuinely
  rewriting the logo.
- **Cross-lane:** no.

### B-18 — `OPTION NAME TOO LONG` is the only reporter of a standing user ruling and is silent — considered, LEAVE IT, with the reason

- **File:line:** `WorldUI/Options/VROptionsTab.2.Rows.cs:393-401` (`VRLog.Warn`), dedup set at
  `:368-369`.
- **Class:** risk-gap. **Tier:** n/a — **leave it.**
- **Evidence for the gap.** The method doc (`:371-376`) says the line *"is the DATA behind the
  'shorten the name' half of the ruling: instead of guessing which of the 362 localized names are
  too long, the log names them"*, and the string cites the ruling verbatim (*"ellipsis is banned,
  user ruling 2026-08-03"*). At `VRLog.Warn` no hardware log has ever carried one. The redundancy
  audit's row R18 already noted that this is the only one of three caption paths that reports at
  all; R18's other two halves are CLOSED at HEAD (`VariantTiles` no longer sets its own
  `fontSizeMin`; the probe is now measured against the label's OWN floor, `:387-389`, which the
  comment there explains).
- **Why leave it, stated plainly.** It is deduped per KEY, not per session
  (`!ReportedLongCaptions.Add(key)`), and the population is 362 localized names across two
  languages plus tab captions and tile labels. A German menu browse could legitimately emit
  **dozens** of lines at once — this is precisely the flood ModBuild 331 removed, and the
  `check-hw-verify.py` header refuses a marked site *"inside an obvious per-frame method"* for the
  same reason. The right fix is a per-session SUMMARY line (`n name(s) did not fit; the worst is
  …`), which is a **new instrument**, not a tier change, and therefore a proposal rather than a
  refactor commit. Recorded here so the next round starts from that design rather than from a
  one-word promotion that would flood.
- **Cross-lane:** the actual remedy (shortening a name) lives in `Core/Loc/Loc.ConfigNames.cs`,
  lane **core**.

### B-19 — `INVARIANTS-WorldUI.md` has SIX entries naming this sub-set that are superseded at HEAD; four of them describe a mechanism that no longer exists at all

- **Class:** doc-drift. **Tier:** 0 (documentation only). This is the Grab/Options equivalent of
  F-60's registry drift, and it matters for the same reason: an invariant file is what the next
  round is told not to break, so an entry describing a removed mechanism is a trap.
- **Verified against HEAD source, entry by entry:**

  | INVARIANTS | claim | verdict at HEAD |
  |---|---|---|
  | `:308-311` *"The depth mask is PER-GRAPHIC, not one union quad"* — `CanvasConversion.CollectVisibleMaskRects` (cap 256), `GrabbableModal` dynamic mesh + rect-set hash gate | **GONE.** `grep -rn 'CollectVisibleMaskRects' src/` returns **nothing**. `GrabbableModal.cs:711` records the removal: *"TRANSPARENCY ROUND: the `depthMask` parameter is gone **with the mask itself**. Its job … is now done by the draw ladder (CanvasConversion.8.Order.cs)."* `ConvertedPanel.cs:1013` lists the five removed members by name. |
  | `:568-573` *"`HostLateSync` exists because Update order between two writers is undefined"* — `GrabbableModal.Tick` + `HostLateSync` on the holder | **GONE from `GrabbableModal`.** `HostLateSync` survives only in `WorldUI/Surfaces/SurfaceGrabBar.cs:552/:648` (lane worldui-front). The "Why" reasons entirely from the depth mask (*"the depth mask (a rigid frame child) rendered at the NEW pose"*), which no longer exists. **Also a live stale cref in THIS lane:** `WorldUI/Conversion/CanvasConversion.2.Adopt.cs:1186` still writes `<c>GrabbableModal.HostLateSync</c>`. |
  | `:584-589` *"The depth-mask quad's render state is exact"* — `GrabbableModal` depth-mask material, queue 2999, +2 mm behind | **GONE.** No depth-mask material in `GrabbableModal` at HEAD. `WorldUI/MrBacking.cs:60` already speaks of it in the past tense: *"It used to share this recipe with the panel depth masks at 2999."* |
  | `:1726-1731` *"`DepthMaskQuadPaddingPx` is 3, deliberately down from 12"* — `GrabbableModal.DepthMaskQuadPaddingPx` (3), `DepthMaskMaxQuads` (256) | **GONE.** Neither identifier exists anywhere in `src/`. |
  | `:592-597` *"The grab bar is opaque and sorts at 1100"* — `MeshRenderer.sortingOrder` 1100 | **SUPERSEDED.** `GrabbableModal.cs:129-137`: *"TRANSPARENCY ROUND: this is no longer an absolute value (it was 1100 …). A fixed 1100 would have made the bar pierce every nearer panel"*; the bar now rides the ladder at `BarOrderOffset = GrabBarLayout.BarOrderOffset` (`GrabBarLayout.cs:110` = 4). |
  | `:1697-1702` *"Laser carry translates only, then RETURNS"* — Why: *"`GrabbableModal.GrabCarriesYaw` … `IPanelGrabOwner.GrabCarriesYaw`"* | **RULE TRUE, CREFS STALE.** The mechanism is intact (`PanelGrab.cs:874-883`: `_laserCarry && _handB == null` → position lerp → `return`). But `GrabCarriesYaw` no longer exists: it is `IPanelGrabOwner.CarryMode` / `enum PanelCarryMode` (`PanelGrab.cs:28`, `:75-81`), whose own doc translates the old names (*"`PanelCarryMode.Slide` the old `false`"*). |

- **Two more, PARTLY superseded (recorded, lower value):**
  - `:539-544` *"The X button sits on its OWN nested canvas at 1100"* — still true of the
    **invisible HitPlane** (`ModalCloseButton.cs:497`, const at `:212`), but at HEAD there are
    **two** canvases: the visible X rides the ladder at `XOrderOffset` +2 (`:452`), which the
    source explains at `:203-210`. The entry names one.
  - `:1719-1724` / `:1396-1401` — `BarColliderPad` (1.5) is no longer in `GrabbableModal`; the
    constant moved to `Core/GrabBarVisual.cs:78`, where it is described in the **past tense**
    (*"The box it replaces was `BarColliderPad = 1.5`"*), and `BarWidthFraction 0.55` /
    `ZoneWidthFraction 0.62` now live in `WorldUI/Grab/GrabBarLayout.cs:70/:76`, not on
    `PanelGrabHandle`. `SetBarCollider` itself is still live (`GrabbableModal.cs:2008`), so the
    RULE holds and only the "Where" is stale.
- **And one in §1923 (the numbered standing items):** item 8 states
  *"`InputModeGuard.Active` (`ForceMouseMode && ConversionActive`)"*. At HEAD
  `WorldUI/Grab/InputModeGuard.cs:38` is `internal static bool Active => WorldUIConfig.ConversionActive;`
  and the doc two lines above says why: *"`[WorldUI] ForceMouseMode` is GONE, user ruling
  2026-08-13"*. The other two gates in that item are correct
  (`EscMenuInputBlock.ShouldSuppress => VRSession.IsRunning`, `EscMenuInputBlock.cs:58`;
  `TakeDamagePanelSafety.GuardsActive => WorldUIConfig.ConversionActive`,
  `TakeDamagePanelSafety.cs:63`). Item 6's ladder also still lists *"depth mask 2999"*.
- **Proposed action.** BRIEF §2 permits editing the entries that name files in this set. **Retire**
  the four depth-mask entries (they document a removed mechanism — retire, do not delete the
  history: mark them REMOVED with the ModBuild/round that removed them and a pointer to
  `CanvasConversion.8.Order.cs`), **correct** the `1100` bar entry and the two crefs in the laser
  entry, **widen** the X-button entry to name both canvases, and **correct** §1923 item 8's
  `InputModeGuard.Active` expression and item 6's ladder. Separately, fix the one live source cref:
  `CanvasConversion.2.Adopt.cs:1186` `GrabbableModal.HostLateSync` → `SurfaceGrabBar.HostLateSync`
  (same lane, comment-only).
- **Guard expectation:** empty for the `.planning` edits; empty for the `.2.Adopt.cs` comment fix.
- **Risk if wrong:** none behavioural. The risk of NOT doing it is a future round "restoring" a
  depth mask that was deliberately removed — the same shape as the `renderOnTop` entry at `:576`,
  which exists precisely to stop that.
- **Cross-lane:** no. `CanvasConversion.2.Adopt.cs` is `WorldUI/Conversion/` = this lane.

### B-20 — `VROptionsTab._degraded` is a process-lifetime latch on a class whose stated job is to RE-inject per options window; a single bad reading kills the VR settings menu for the session

- **File:line:** `WorldUI/Options/VROptionsTab.1.Inject.cs:135` (declaration), `:255-257`
  (`Tick`'s first statement: `if (_degraded) return;`), `:168`
  (`CanOpen => !_degraded && …`), `:1430-1441` (`Degrade`), `:1452-1470` (`Forget`),
  `:1497-1525` (`Shutdown`), and the five `Degrade(...)` call sites at `:308`, `:315`, `:327`,
  `:363`, `:393`.
- **Class:** defect (a latch that never clears). **Tier:** 3. **Recommendation: FINDING, NOT A
  COMMIT — see below.**
- **Evidence, end to end.**
  1. The class doc states the design (`:250-252`): *"**Re-injects when the options window is
     replaced** — it is a `Singleton` that does not survive every scene, and a stale reference
     would leave the tab silently missing for the rest of the session."* `Tick` at `:263-272`
     implements exactly that: host gone ⇒ drop references ⇒ next host gets a fresh tab.
  2. `Tick`'s **first** statement is `if (_degraded) return;` (`:256`). So the re-injection
     machinery is downstream of a latch that, once set, is never cleared.
  3. `Degrade` sets `_degraded = true` (`:1434`) and then calls `Forget()`. `Forget`
     (`:1452-1470`) clears `_host`, `_toggle`, `_window`, `ContentRoot`, `TabBarRoot`,
     `IsStandalone`, `_onHidden`, `_hiddenHooked`, `_selectOnShow`, `_showHooked`,
     `_loggedShowRemedy`, `_inputArea` — **and not `_degraded`.** `Shutdown`'s
     `finally { Forget(); }` (`:1522-1524`) therefore does not clear it either, and no other
     writer exists (`grep -n '_degraded' ` over the file returns six lines: `:135`, `:168`,
     `:256`, `:758`, `:1432`, `:1434` — one declaration, three reads, the guard and the single
     write).
  4. Four of the five `Degrade` reasons are measurements of ONE options-window instance —
     `:308` *"the options window has no tabs to clone from"*, `:315` *"no usable donor tab (need
     one with both a toggle and a tab window)"*, `:327` *"the tab window could not be cloned"*,
     `:363` *"the pane could not be detached AND the tab toggle could not be cloned"* — and the
     fifth, `:393` `Degrade($"injection threw: {e}")`, is a catch-all around the entire injection.
  5. Observed vs expected. Expected (from `:250-252`): a bad options-window instance is dropped
     and the next one is injected. Observed: the first bad instance latches, and every later
     options window — including one in a different scene, and one after a full `Shutdown`/re-init
     — is skipped at `:256`. `CanOpen` (`:168`) then returns false for the rest of the process, so
     the pause-menu VR row is not injected either (the `Degrade` string says so:
     *"The pause-menu VR row is not injected either, because a row that opens nothing is worse
     than no row."*).
  6. **The sibling class does it the other way.** `WorldUI/Options/VRMenuEntry.cs:950` clears its
     own `_degraded = false;` in its reset, alongside `_loggedInject`, `_loggedIcon`,
     `_loggedMain`, `_loggedDetach`, `_loggedLatch`. Two classes in the same folder, one concept
     ("this feature could not install"), opposite lifetimes, and neither says why.
  7. And it is **silent** — the `Degrade` emitter is `VRLog.Warn` (B-11 site 1), so the session in
     which the whole VR settings menu vanished carries no line at the shipped level.
- **What I am NOT claiming.** I did not find a concrete input that makes one of the five reasons
  fire transiently; I am *inferring* the transient case from the catch-all at `:393` and from the
  class's own statement that the host does not survive every scene. The demonstrable half is the
  contradiction between `:250-252` and `:256`, and the asymmetry against `VRMenuEntry.cs:950`.
- **Proposed action.** **Do not change the latch in this phase.** Clearing `_degraded` in `Forget`
  is a behaviour change on a feature whose failure mode is "the options menu opens onto nothing",
  and BRIEF §1 says a fix that needs a design decision is a finding. What SHOULD land now:
  1. B-11 site 1's `Warn` → `Alert`, so the state is at least readable; and
  2. a comment above `:135` recording, in one sentence, that the latch is deliberately
     process-lifetime **and that `Forget` does not clear it**, next to the pointer to
     `VRMenuEntry.cs:950`'s opposite choice — so the next reader sees a decision instead of an
     omission.
  The open question for the user: *should a single failed injection disable the VR settings menu
  for the rest of the process, or only for that options-window instance?*
- **Guard expectation:** empty (comment only) for the part that lands; B-11 covers the tier change.
- **Risk if wrong:** none for the comment. Changing the latch would risk an injection retry loop on
  a genuinely broken game build — which is the reason the latch exists and the reason this is a
  question, not a commit.
- **Cross-lane:** no.

### B-11b — two more of the same class, found in `WorldUI/Grab/UiSoundEar.cs` (add to B-11's list as sites 7 and 8)

| # | site | emitter | latch | what is lost |
|---|---|---|---|---|
| 7 | `WorldUI/Grab/UiSoundEar.cs:325-329` | `VRLog.Warn` | `_fieldResolved` `:315` (resolve-once) | *"UI SOUND EAR cannot be repaired: `AudioController` has no instance field `'_currentAudioListener'` … **Every button hover/click sound will stay INAUDIBLE** while the mod owns the AudioListener"* |
| 8 | `WorldUI/Grab/UiSoundEar.cs:673-678` (`LogFailureOnce`) | `VRLog.Warn` | `_failureLogged` `:675` | *"UI SOUND EAR {what} — button hover/click sounds may stay inaudible. **This is logged once per session**"* |

- **Why these two matter as much as B-11's six.** The consequence is a **verbatim standing user
  report**, quoted in this same file (`:684-687`, ModBuild 195): *"Immer noch keine Geräusche wenn
  ich die physischen buttons drücke wie zB 'Händler', ich will das die selben Geräusche kommen die
  auch im normalen Spiel hörbar sind."* If site 7 fires, the answer to the next round's "still no
  sounds" is already in the code and is invisible in the log — the reader would repeat ModBuild
  195's investigation from scratch.
- Site 8's own string says *"logged once per session"*, which is the strongest possible statement
  that no flood argument applies to it.
- **Proposed action.** Both `VRLog.Warn` → `VRLog.Alert`, strings untouched; `// HW-VERIFY` on
  site 7. **Guard:** `CHANGED` confined to `GloomhavenVR.WorldUI.UiSoundEar`, two call targets.
- **Cross-lane:** no. This brings B-11's total to **eight one-shot self-disarms at a silent tier**
  in this sub-set, against one (`EscMenuShowSafety`) that was corrected.


---

## Coverage — every file, how it was actually read

Legend: **W** = read whole, top to bottom. **D** = declarations + every `VRLog` call + every
`catch` + every `Reset`/latch + the doc blocks around them (the automated scans below plus targeted
reads of every hit). **S** = skimmed: automated scans only (tier census, catch census, latch census,
double-`<summary>` scan, dead-member scan, cadence-gate scan), plus whatever line ranges those
scans pointed at. No file in the set escaped all six scans.

### `WorldUI/Options/` — 17 054 lines

| file | lines | how read |
|---|---|---|
| `ConfigCatalog.cs` | 1 802 | **D** (`:155-220`, `:315-400`, `:610-700`, `:810-870`, `:1370-1470`, `:1600-1760` read; the ~900 lines of topic/group tables skimmed) |
| `ConfigSteps.cs` | 876 | **S** + `:540-660` read |
| `DevPanels.cs` | 140 | **D** |
| `MenuRowSeat.cs` | 1 235 | **D** (`:250-360`, `:430-500`, `:1000-1045` read; the plan/seat arithmetic `:600-1000` skimmed) |
| `OptionsToggle.cs` | 936 | **W** |
| `VariantTiles.cs` | 679 | **D** (`:250-300`, `:590-679` read) |
| `VariantTilesTable.cs` | 247 | **D** (`:55-140` read) |
| `VRMenuEntry.cs` | 955 | **D** (`:130-145`, `:355-400`, `:460-600`, `:700-960`; injection body `:180-350` skimmed) |
| `VROptionsTab.1.Inject.cs` | 1 524 | **D** (`:130-170`, `:250-400`, `:550-730`, `:840-1000`, `:1270-1330`, `:1420-1530` read) |
| `VROptionsTab.2.Rows.cs` | 2 306 | **S** + `:290-400`, `:560-600`, `:750-840`, `:1000-1030`, `:1420-1440`, `:1720-1900`, `:2000-2040`, `:2080-2270` |
| `VROptionsTab.3.Content.cs` | 663 | **S** |
| `VROptionsTab.4.Curated.cs` | 1 785 | **S** + the Sky / MixedReality / WindowFacing rows and `:1520-1720` |
| `VROptionsTab.5.Variants.cs` | 271 | **S** |
| `VROptionsTab.6.BoardTopic.cs` | 390 | **S** |
| `VROptionsTab.7.TopicTrees.cs` | 659 | **S** |
| `VROptionsTab.8.Dependencies.cs` | 425 | **S** |
| `VROptionsTab.9.TestTriggers.cs` | 474 | **S** |
| `VROptionsTab.10.Skin.cs` | 863 | **S** + `:205-240`, `:490-530`, `:700-730` |
| `VROptionsTab.Cheats.cs` | 824 | **S** + `:140-200`, `:760-820` |

### `WorldUI/Grab/` — 8 902 lines

| file | lines | how read |
|---|---|---|
| `BarSizeSettle.cs` | 309 | **D** (`:220-309` read whole) |
| `GrabbableModal.cs` | 3 834 | **D** (`:75-150`, `:315-440`, `:600-640`, `:700-740`, `:855-1000`, `:1600-1620`, `:1960-2040`, `:3220-3320`, `:3510-3620`, `:3740-3760` read; the ink/fit/report arithmetic between them skimmed — **the thinnest part of my pass**) |
| `GrabBarLayout.cs` | 303 | **S** + `:50-120`, `:290-305` |
| `GrabBarTween.cs` | 403 | **D** (`:77-100`, `:240-262`, `:385-403`) |
| `InputModeGuard.cs` | 97 | **W** |
| `ModalCloseButton.cs` | 540 | **D** (`:120-215`, `:250-300`, `:405-510` read) |
| `NonDominantHold.cs` | 189 | **W** |
| `PanelGrab.cs` | 1 397 | **D** (`:20-110`, `:145-230`, `:310-345`, `:400-425`, `:430-500`, `:575-720`, `:810-960`, `:1010-1110`, `:1170-1210` read) |
| `UiSoundEar.cs` | 1 028 | **S** + `:160-185`, `:210-350`, `:385-410`, `:485-515`, `:655-690`, `:810-830`, `:940-1025` |
| `VRKeyboard.cs` | 659 | **S** + `:80-95`, `:245-275`, `:390-425`, `:430-445`, `:560-660` |
| `WindowReFacePolicy.cs` | 143 | **W** |

### `WorldUI/Patches/` — 7 416 lines

| file | lines | how read |
|---|---|---|
| `Character3DDisplayRefcount.cs` | 424 | **D** (`:115-135`, `:225-265`, `:360-400`) |
| `CharacterClickSelectsOnly.cs` | 291 | **S** |
| `ConfirmationBoxRescue.cs` | 461 | **D** (`:130-150`, `:200-260`, `:335-400`, `:420-461`) |
| `EscMenuInputBlock.cs` | 231 | **D** (`:15-60`, `:65-150`, `:160-231`) |
| `EscMenuShowSafety.cs` | 332 | **D** (`:30-45`, `:100-200`, `:260-332`) |
| `InitiativeHoverCardBlock.cs` | 62 | **S** |
| `InputFieldFocusWatch.cs` | 240 | **D** (`:10-30`, `:100-200`) |
| `KeyboardAutoHideBlock.cs` | 125 | **S** |
| `MainMenuLogoSwap.cs` | 2 039 | **D** (`:110-180`, `:230-310`, `:480-540`, `:1890-2039` read; the ink-measurement / placement / census arithmetic `:770-1890` **skimmed — second-thinnest part of my pass**) |
| `MapLocationHoverAnimationGate.cs` | 61 | **S** |
| `MapLocationSelectorGate.cs` | 66 | **S** |
| `MenuExitLatchGuard.cs` | 123 | **W** |
| `MouseWorldSurfaceCut.cs` | 130 | **S** |
| `PartyPanelStackingHide.cs` | 309 | **S** + `:185-210` |
| `PartyPreviewStorm.cs` | 283 | **W** (`:100-283`; the class doc `:1-99` skimmed) |
| `ScenarioGateCheat.cs` | 640 | **S** + `:340-360`, `:395-415`, `:470-485`, `:600-615` |
| `SettingsClickExemption.cs` | 247 | **D** (`:110-150`, `:185-247`) |
| `TakeDamagePanelSafety.cs` | 310 | **D** (`:55-110`, `:150-165`, `:250-310`) |
| `TooltipRaiseGuard.cs` | 167 | **S** + `:95-140` |
| `TooltipWindowPatches.cs` | 875 | **S** + `:80-125`, `:270-330`, `:490-510`, `:560-700`, `:760-880` |

### The thinnest parts of my pass, in plain words

1. **`GrabbableModal.cs`'s ink/bar arithmetic (roughly `:1000-3200`, ~2 200 lines).** I read its
   constants, its reset-shaped bodies, every `VRLog` call and the two report methods, but I did
   **not** follow the ink-union to span to rod-placement chain statement by statement. A
   wrong-result defect living purely inside that arithmetic would not have been found by anything
   I did.
2. **`MainMenuLogoSwap.cs`'s `MainMenuLogoPlacement` measurement half (`:770-1890`).** Same shape:
   I read its instruments and its watch, not its rect maths (`MeasureInk`, `SubBox`, `DrawnBox`,
   `PlaceOnBand`, `Region`, `Stats`).
3. **The five curated / topic-tree files** (`4.Curated`, `5.Variants`, `6.BoardTopic`,
   `7.TopicTrees`, `8.Dependencies` — ~3 530 lines). I leaned on
   `scripts/check-options-coverage.py`, which is green and checks seven things a machine can decide
   about exactly these files, rather than reading the row tables. A row that is *present, resolving
   and consistent* but in the WRONG PLACE is invisible to both the script and to me.
4. **`VROptionsTab.2.Rows.cs` (2 306) and `MenuRowSeat.cs` (1 235).** Read at every instrument and
   every catch; the uGUI row-construction and seat-planning bodies were skimmed.
5. **`ConfigSteps.cs` and `ConfigCatalog.cs`'s classification tables.** I verified the
   STALE-DOC-REF, the double-`<summary>` sites and the `Tooltip`/`Hint` pair; I did not audit the
   step rule against `tests/GloomhavenVR.WireTests/ConfigStepVectors.cs`, which pins it anyway.

## Verified still true / no longer true

### `.planning/refactor/INVARIANTS-WorldUI.md` — every entry whose subject is in THIS sub-set

| entry | verdict at HEAD |
|---|---|
| `:308` The depth mask is PER-GRAPHIC | **NO LONGER TRUE** — mechanism removed entirely (B-19) |
| `:539` The X button sits on its OWN nested canvas at 1100 | **PARTLY TRUE** — 1100 now carries only the invisible HitPlane; the visible X rides the ladder at `XOrderOffset` +2 (B-19) |
| `:546` The X glyph's `localPosition.z` only is normalized | **STILL TRUE** — `ModalCloseButton.cs:414`, `:491`, `:534` all write `new Vector3(x, y, 0f)` |
| `:553` A grabbable modal's holder must stay at IDENTITY scale | **STILL TRUE** — `GrabbableModal.cs:1081` `_holder.localScale = Vector3.one;` |
| `:561` Floated menus capture `worldScale` ONCE at spawn | **STILL TRUE** — `_spawnWorldScale` set `:723`, read `:1080`, `:1216`, `:1236` |
| `:568` `HostLateSync` exists because Update order is undefined | **NO LONGER TRUE for `GrabbableModal`** — survives only in `SurfaceGrabBar` (B-19) |
| `:584` The depth-mask quad's render state is exact | **NO LONGER TRUE** — no such material (B-19) |
| `:592` The grab bar is opaque and sorts at 1100 | **NO LONGER TRUE** — ladder-relative `BarOrderOffset` 4 (B-19) |
| `:599` The modal escape chord runs on raw XR state | **STILL TRUE** — `NonDominantHold` reads `hand.PrimaryButton` directly on `Time.unscaledDeltaTime` |
| `:1326` `NonDominantHold` FREEZES the press on pose loss, needs `_hadHand` both frames | **STILL TRUE** — `NonDominantHold.cs:112-121`, `:133-138` |
| `:1334` Face buttons read on the CONTROLLER INSTANCE, not `HasPose` | **STILL TRUE** — `:96-108`, `:110` |
| `:1342` One physical press serves exactly one intent | **STILL TRUE** — `PressId` `:127`; `_spentPressId` `OptionsToggle.cs:54` |
| `:1349` Controller-X is the SOLE ESC-menu owner | **STILL TRUE** — both suppressors live; `EscMenuEscapeSuppressor.Prefix` scoped by `__instance.ID != UIWindowID.ESCMenu` (`:222-223`); self-registered from `InputModeGuard.Tick` `:44` |
| `:1357` The X toggle decision is computed from LIVE game windows each tap | **RULE TRUE, "Where" STALE** — the compendium probe is no longer a *side-effect-free scene scan*: `FindOpenCompendiumWindow` (`OptionsToggle.cs:928-937`) walks `UIWindow.GetWindows()`, and `:915-923` says why. Correct the wording. |
| `:1365` `ESCMenu` cached, recovered by an inactive-inclusive scene scan | **STILL TRUE, now RANKED** — `RankedCandidates` (`:613`) uses `FindObjectsOfType<ESCMenu>(includeInactive: true)`; the entry predates the ModBuild 290 ranking and should name it |
| `:1372` `TakeDamagePanelSafety` is three independent nets, all VR-gated | **STILL TRUE** — `GuardsActive` `:63`, hover stand-down `:260-265`, `IsLethalDamage` prefix `:299+` |
| `:1396` The laser grabs ONLY the visible drag bar | **RULE TRUE, "Where" STALE** — `BarWidthFraction` 0.55 / `ZoneWidthFraction` 0.62 now in `GrabBarLayout.cs:70/:76`, not on `PanelGrabHandle`; the 1.5x pad is gone (B-19) |
| `:1403` One-hand carry yaw from swing-twist | **STILL TRUE** — `LevelPose.TwistDegrees(handDelta, up)`, `PanelGrab.cs:921` |
| `:1410` / `:1712` `PanelGrabHandle` drops stale grip slots | **STILL TRUE** — `HasPose` drops `:818`/`:832` run FIRST, then `!ReferenceEquals(_handX.Grabber.Held, this)` `:843`/`:855`; `:853` fires `OnGrabFinished()` |
| `:1417` `InputModeGuard` keeps `GamePadInUse` false | **STILL TRUE** — both prefixes present, `InputModeGuard.cs:69-97` |
| `:1690` `PanelGrabHandle.MinScale`/`MaxScale` are `internal` | **STILL TRUE** — `PanelGrab.cs:173-174`, `internal const` 0.15 / 2 |
| `:1697` Laser carry translates only, then RETURNS | **RULE TRUE, CREFS STALE** — `GrabCarriesYaw` became `IPanelGrabOwner.CarryMode` / `enum PanelCarryMode` (B-19); the early return is intact at `PanelGrab.cs:874-883` |
| `:1704` One-hand swing-twist vs two-hand heading | **STILL TRUE** |
| `:1719` `GrabbableModal` keeps the bar collider as a LASER-ONLY target | **PARTLY TRUE** — `SetBarCollider` live (`:2008`); `BarColliderPad` gone (B-19) |
| `:1726` `DepthMaskQuadPaddingPx` is 3 | **NO LONGER TRUE** — neither symbol exists (B-19) |
| `:1733` `EscMenuInputBlock` sets `_registered = true` BEFORE the try | **STILL TRUE** — `EscMenuInputBlock.cs:76`, comment intact |
| `:1740` `EscMenuEscapeSuppressor` sets `__result = false` before returning false | **STILL TRUE** — `:227-228` |
| `:1747` `TakeDamagePanelSafety.AllowHover` returns false WITHOUT logging | **STILL TRUE** — `:260-265`, no `_lastSwallow` touch on the docked path |
| `:1882` `DevPanels` = `[WorldUI] DevShowAllPanels` under `[Dev] SimulateHands` | **STILL TRUE** — `DevPanels.cs:31` |
| §1921 item 6 (the sorting/queue ladder) | **PARTLY STALE** — "depth mask 2999" is gone (B-19) |
| §1922 item 7 (static scratch buffers) | **PARTLY STALE** — the `GrabbableModal` half named the depth mask, which is gone |
| §1923 item 8 (three "am I active?" gates) | **PARTLY STALE** — `InputModeGuard.Active` is `ConversionActive` alone, not `ForceMouseMode && ConversionActive` (B-19). The other two gates are correct: `EscMenuInputBlock.ShouldSuppress` `:58`, `TakeDamagePanelSafety.GuardsActive` `:63` |
| §1924 `EscMenuInputBlock` self-registers from `InputModeGuard.Tick` | **STILL TRUE** — `InputModeGuard.cs:44` |

### `.planning/refactor/REVIEW-WorldUI.md`

No item names a file in this sub-set that is not already covered by an INVARIANTS row above.
Nothing to re-raise or retire beyond B-19.

### `.planning/redundancy-audit.md` — rows naming my files

| row | verdict at HEAD |
|---|---|
| §2 "the close X built twice" (`ModalCloseButton` vs a 3D `BoardButton` "X") | **still a real pair, still DELIBERATE** — different input surfaces; not proposed for merge |
| **R1** release re-face | **CLOSED** — `WorldUI/Grab/WindowReFacePolicy.cs` is the shared policy; `SurfaceGrabBar.cs:511` and `CombatLogSurface.cs:572` both call it; `ReFaceEpsilonDeg` referenced by both (`GrabbableModal.cs:978`, `SurfaceGrabBar.cs:129`) |
| **R2** two doors, two names (20 of 82 disagreeing) | **CLOSED** — `check-options-coverage.py` check 5 compares `CaptionKey` with `ConfigNames`; green, reports "82 row(s) named on both doors agree, 3 deliberate second door(s)" |
| **R8** seven settings state a default the mod does not ship | **NOT THIS FILE'S BUG** — `ConfigCatalog.Tooltip` (`:1726-1753`) prints `item.Entry.DefaultValue`, the real value. The drift is in the descriptions (Bind sites, `Core/Loc`) — lanes worldui-front / core |
| **R13** "Ein" vs "An" at `ConfigCatalog.cs:1512` | **the ConfigCatalog half is CLOSED** — no such literal remains in that file; `VROptionsTab.2.Rows.cs:1433-1435` keeps the ruling comment. The rest is `Core/Loc` (lane core) |
| **R18** three caption-shrink floors | **CLOSED for the floors** — `VariantTiles.cs` sets no `fontSizeMin`; `ProbeCaptionFit` measures the label's OWN floor (`VROptionsTab.2.Rows.cs:387-389`). The silent-failure half is B-18 |
| **R20** three `Button.colors` palettes, three bare `fadeDuration = 0.08f` | **CLOSED** — all three read `UguiTintFeel.HoverTintFadeSeconds` (`VROptionsTab.2.Rows.cs:826`, `VariantTiles.cs:637`, `ModalCloseButton.cs:433`); the stale prose is B-15(c) |
| **R28** laser beam geometry | **not this sub-set** — no beam constant in `Options/Grab/Patches` |
| **R30** per-tick exception isolation, incl. `EscMenuShowSafety.cs:132`, and §6.3 | **the `EscMenuShowSafety` half is CLOSED** — `Report`/`Degrade` are `VRLog.Alert` with the ModBuild 439 paragraph at `:136-142`. §6.3's claim is **no longer true of that file** — but the same defect survives at eight other sites: B-11 + B-11b |
| **R41** draw order inside one window | **MOSTLY CLOSED** — `PanelOrderStep` is `internal const` (`CanvasConversion.8.Order.cs:176`) and `RegisterOrderFollower` (`:302-312`) refuses an offset outside `[0, PanelOrderStep)` with a log line: that is the missing assert. `BarOrderOffset` 4 and `XOrderOffset` 2 remain literals but are now validated at the choke point. **Not re-raised.** |
| **R45** two doors on "pick your environment" | **CLOSED** — `VariantTilesTable.ChooseEnvironment` (`:130`) is the shared writer; the dropdown at `VROptionsTab.4.Curated.cs:1692-1715` routes through it |
| §4.9 `Cheats.Text` duplicating `Curated.Say` | **STILL TRUE, STILL DELIBERATE** — `Curated.cs:110`'s reason (the cheats file is built to be deleted in one step) holds. Not proposed for merge |
| §6.6 `VariantTiles.cs:356 colors.disabledColor = TileBorderOff` | **STILL PRESENT, still unreachable** — nothing sets `interactable = false`. Left alone |

### `.planning/refactor/STALE-DOC-REFS.md`

Exactly **one** row names this sub-set: `WorldUI/Options/ConfigCatalog.cs | 166 | ResolveStep`.
**Clearable** — B-09. Corrected cref: `<see cref="ResolveSteps"/>` (`ConfigCatalog.cs:815`).

### `.planning/refactor/INSTRUMENT-WRITES.baseline`

Two rows name this sub-set (checked with `scripts/check-instrument-writes.py --report`; the plain
check is green at baseline 65):

- `PanelGrabHandle::_reelSelfTicks <- ReportReelReleased` — the only reader is `PanelGrab.cs:1102`
  in `TickCarryReel()`, a `+=` that exists solely to feed the report; `ArmReel` (`:587-588`)
  already zeroes both counters at every carry start. **Meets the file's letter, not its spirit**
  (same verdict shape as F-61). Recommend annotating it "instrument-only, safe to retire, `ArmReel`
  owns the reset".
- `UiSoundEar::_stateLogged <- ReportState` — read at `UiSoundEar.cs:223` in
  `NoticeHoveredWidget()`, a non-diagnostic method on the hover path. **Genuinely load-bearing**:
  delete `ReportState` and the latch never sets, so every hover repays the try/catch and the
  ancestor walk. Cost, not behaviour. **Keep the row.**

## Searches that came back EMPTY (so nobody redoes them)

1. **Empty catches.** `catch {}` / `catch { }` / `catch (Exception) { }` across all 50 files:
   **zero**. All 158 `catch` sites log something.
2. **`DepthMaskQuadPaddingPx`, `DepthMaskMaxQuads`, `CollectVisibleMaskRects`** anywhere in `src/`:
   **zero**. (Feeds B-19.)
3. **`GrabCarriesYaw` as a live member:** **zero** — three prose mentions in `PanelGrab.cs` only.
4. **`ForceMouseMode` as a live config key:** **zero** — six tombstone comments, no `Bind`.
5. **Cadence-gate inversion** (the `ActorPropBody.cs:933` shape — deadline advanced only past a
   change gate). Checked **every** `_next*` / `_last*` pair in the sub-set:
   `VRMenuEntry.cs:377-381`, `VRKeyboard.cs:259-261`, `BarSizeSettle.cs:285-287`,
   `SettingsClickExemption.cs:215-217`, `TooltipRaiseGuard.cs:103-105` and `:130-132`,
   `InitiativeHoverCardBlock.cs:51-53`, `PartyPreviewStorm.cs:245-249`,
   `Character3DDisplayRefcount.cs` (`WindowSeconds` 1 s), `VROptionsTab.2.Rows.cs:2016`/`:2029`.
   **None is inverted** — every one advances before the expensive work and before every early
   return.
6. **Per-frame `FindObjectsOfType`.** Nine call sites in the sub-set; **none is per-frame**:
   `OptionsToggle.cs:613` (tap only), `VRMenuEntry.cs:386` (behind `_nextMainScan` +
   `_mainScansLeft`, both spent BEFORE the call at `:379-380`), `VRKeyboard.cs:263` (behind
   `_lastSweep`, set before the sweep), `:438`/`:571`/`:580`/`:591` (probe / teardown),
   `VROptionsTab.10.Skin.cs:498` (one harvest), `MainMenuLogoSwap.cs` (`FindObjectsOfTypeAll` at
   2 Hz inside the bounded watch — and the file says so at `:1892`).
7. **A bound config key nothing reads.** Only **one** `.Bind(` exists in the whole sub-set:
   `VROptionsTab.Cheats.cs:179` `[Cheats] Enabled`, read at that file's own gate. Every other
   setting these files touch is bound elsewhere and reached through `ConfigCatalog`. The
   "unread key" class does not live here.
8. **A curated / topic-tree row naming a key that does not exist.** `check-options-coverage.py`
   check 1 covers exactly this and is green; I found no route around it.
9. **`List<UnityObject>.Remove/Contains` fake-null collisions.** Three candidates —
   `OptionsToggle._censusBefore.Contains` (`:381`, guarded by a `w == null` test one clause
   earlier), `GrabbableModal.LiveHolders.Contains/Remove` (`:2034`, `:3757`), and
   `GrabBarTween.Live.Remove` (`:166` — `GrabBarTween` is `internal sealed class`, NOT a
   `UnityEngine.Object`, so reference equality applies, and `TickAll` `:253` prunes on Unity-null).
   None is exposed. The one real gap I did find is B-04, a `.name` dereference, not a container.
10. **Unity objects as dictionary KEYS** (`MenuRowSeat.Originals` `:345`, `Displaced` `:355`,
    `Seat.Seated` `:304`). Not a demonstrable defect: `UnityEngine.Object.GetHashCode()` returns
    the stable instance id, so two *different* destroyed objects land in different buckets and the
    fake-null `Equals` is never reached; `PruneDisplaced` (`:1026-1039`) drops dead keys past 32,
    and `Release` (`:446-467`) null-checks every key before use and then clears both tables.
    `Originals` has no prune analogue — a bounded growth, not a wrong result. Recorded, not raised.
11. **`BY VALUE` / `restated` / `private to another lane's file`** across the sub-set: the only live
    copy-by-value is **B-01**. No site in `Options/Grab/Patches` makes the false "another lane's
    file" claim in code — the one that does (`VROptionsTab.10.Skin.cs:211-221`) is a **stale**
    claim about a conversion that has since happened (B-15c).
12. **A `Reset()` that leaves a one-shot latch set.** All eight resets checked
    (`MenuRowSeat.Reset` / `ResetLogLatches`, `VariantTiles.ResetVariantTiles`,
    `BarSizeSettle.Reset`, `InputFieldFocusWatch.Clear`, `NonDominantHold.Reset`,
    `InputModeGuard.Reset`, `ConfirmationBoxRescue.Reset`, `VROptionsTab.Forget`, plus
    `VRMenuEntry`'s reset at `:941-952`). Seven are complete. The single exception is
    `VROptionsTab.Forget` vs `_degraded` — **B-20**, and it may be deliberate.
13. **`NonDominantHold.Reset()` zeroing `PressId` while `OptionsToggle._spentPressId` survives.**
    Chased as a cross-reset latch; it is **not** one. `_spentPressId` is an INSTANCE field
    (`OptionsToggle.cs:54`) on `_optionsToggle`, a `readonly` field of the driver MonoBehaviour
    (`WorldUIModule.cs:292`) that `Shutdown` destroys (`:205-208`), so a re-init gets `-1`; and the
    test is `==`, not `<=`, so even a hypothetical stale value would cost one press, not all.
14. **Gates that print nothing at all.** No `Log*`/`Report*`/`Census*` method in the sub-set has an
    empty body or a body whose only statement is a `return`.

## Findings by class

| class | ids | count |
|---|---|---|
| **defect** | B-04, B-20 | 2 |
| **risk-gap — act (tier 3)** | B-02, B-05, B-07, B-11, B-11b, B-12, B-13, B-16, B-17 | 9 |
| **risk-gap — leave alone, recorded** | B-03, B-06, B-18 | 3 |
| **duplication** | B-01 | 1 |
| **parallel-construction** | B-11 (also counted under risk-gap) | 1 |
| **dead → reclassified INERT** | B-10 | 1 |
| **structure-naming** | B-08, B-15(a)(b)(d), B-14 (declined) | 3 entries / 5 sites |
| **doc-drift** | B-09, B-15(c), B-19 | 3 |
| **leave-alone (explicit negative results)** | B-03, B-06, B-14, B-18 | 4 |
| **TOTAL** | B-01 … B-20 plus B-11b | **21** |

By tier: **Tier 0** — B-08, B-09, B-10, B-15, B-19 (5; all comment/doc-only, guard expected
**empty**). **Tier 2** — B-01 (1). **Tier 3** — B-02, B-04, B-05, B-07, B-11, B-11b, B-12, B-13,
B-16, B-17 (10 — of which **nine are one-word tier promotions on latched or event-gated lines**
and one, B-04, is a two-line null guard). **n/a / leave-alone** — B-03, B-06, B-14, B-18, and
B-20's code half (a question for the user, not a commit).

Log-tier promotions proposed in total: **eighteen call sites**, every one of them behind a
one-shot latch, a per-release/per-tap edge, or a Harmony `TargetMethod` resolver. **No
per-frame-capable line is proposed for promotion**; the ones I deliberately left noisy and silent
are named in B-05's table (`[OptionsToggle] X tap`, `cache DROPPED`, `ESC MENU RESOLVED`,
second-chance-also-refused), B-12 (`CHARACTER 3D CADENCE`, `CHARACTER 3D {kind}`), B-18
(`OPTION NAME TOO LONG`) and B-02's epilogue half.

### If only three things land from this report

1. **B-11 + B-11b** — eight one-shot self-disarms at a tier no hardware log carries; one is the
   entire VR settings menu, one answers a verbatim standing user report about button sounds. Eight
   one-word changes, none able to flood, against one sibling (`EscMenuShowSafety`) that was already
   corrected and carries the paragraph explaining why.
2. **B-04** — the only demonstrable throw hazard I found, inside the instrument that adjudicates
   the 2026-09-03 options-key ruling, two lines away from two sibling loops that already guard it.
3. **B-19** — four `INVARIANTS-WorldUI.md` entries documenting a depth-mask mechanism that no
   longer exists. Left alone, the next round is told to protect it.

---

## Note on the worktree state during this pass (not a finding — for the integrator)

At the moment I finished, `git status` in the worktree showed three files modified that are **not
mine** and that I never touched (my only write this session was this report plus three helper
scripts in the scratchpad):

```
 M src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs      (+9/-2)
 M src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs              (+39/-15)
 M src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.4.Content.cs   (+11/-3)
```

That is the §6/§8 half of this lane (F-52 / F-54 territory) being edited concurrently. **It does
NOT collide with B-01:** `git diff -- CanvasConversion.3.Fit.cs | grep HitRectShrinkDeadBandPx`
returns nothing, so `:5946` is untouched and F-53 #4 is still open exactly as recorded.
Every line number quoted anywhere in this report was taken from the **committed** state at
`e26371e401a7d41f4c08324ebed5b133aaa6c51b`, not from the dirty working tree; nothing I cite is in
one of those three files except that one negative check.
