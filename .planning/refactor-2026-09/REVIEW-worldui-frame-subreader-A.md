# Review A — WorldUI/Conversion + WorldUI/Materialise

Reviewer: sub-reviewer A, worldui-frame lane. READ-ONLY.
Status: COMPLETE

## Files read

## Findings

Base SHA: 5aa007149ca3a3cc89984b6dc9d5c0699c286dc3 (worktree clean at start)
Worktree: /home/claw/gloomhaven_vr/.claude/worktrees/agent-a236ff8d959582747
Set: src/GloomhavenVR/WorldUI/Conversion/ (22 .cs, 22 067 lines) + WorldUI/Materialise/ (5 .cs, 3 837 lines)
NOTE for the integrator: the prompt says "28 files" for Conversion and lists 6 Materialise files;
at HEAD there are 22 and 5. `CanvasConversion.3.Fit.cs` is 6 941 lines (prompt correct).

Log tiers verified in `src/GloomhavenVR/Core/VRLog.cs`:
  Error (Level>=Error), Alert (>=Warning), Note (>=Info) PRINT at the shipped `[General] LogLevel = Info`.
  Warn (>=Debug), Info (>=Debug), Debug (>=Debug) print NOTHING at the shipped default.

file read: CanvasConversion.1.Core.cs 1-584 (whole)

### A-01 — the ONLY failing branch of the 2026-09-03 deadlock fix is silent at the shipped log level
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs:576-581`
- **Class:** risk-gap
- **Tier:** 3 (one-word promotion; no behaviour change)
- **Evidence:** `KeepHostInTargetScene` has three outcomes and prints three lines.
  The two NON-failing ones are `VRLog.Note` and both carry `// HW-VERIFY`:
  `:549` `HOST SCENE PINNED` (success on a persistent window) and `:566` `HOST SCENE PIN SKIPPED`
  (scene mid-unload). The **catch** at `:576` — the one case where the pin genuinely did not happen
  for an unknown reason — is `VRLog.Warn("WorldUI", $"HOST SCENE PIN FAILED for '{name}' …")`.
  `VRLog.Warn` gates on `Level >= VRLogLevel.Debug` (`Core/VRLog.cs:155`), so at the shipped
  `[General] LogLevel = Info` it prints NOTHING. The comment three lines above the try says
  *"Failure is never fatal: the worst case is the pre-fix behaviour, and it is logged."* — it is
  logged only for a player who has already switched to Debug, i.e. never for the report that would
  bring this back. The failure mode this guards is the "Quest verwerfen" hard deadlock (a
  persistent `ConfirmationBox` deleted by the next scene load), which the file documents at
  `:266-292` as the worst class of bug this subsystem has shipped.
- **Proposed action:** `VRLog.Warn` → `VRLog.Alert` at `:578`. One word. Message text unchanged
  (no grep token reworded). Optionally add `// HW-VERIFY` beside it so `check-hw-verify.py` pins
  the tier from then on — the two siblings already carry it.
- **Guard expectation:** `CHANGED` confined to `CanvasConversion`, one call site.
- **Risk if wrong:** none — `Alert` is `LogWarning` at `VRLogLevel.Warning`; strictly more visible.
- **Cross-lane:** none.

### A-02 — STALE-DOC-REFS `CanvasConversion.1.Core.cs:32` cleared: the symbol MOVED namespace, it did not die
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs:33`
- **Class:** doc-drift
- **Tier:** 1
- **Evidence:** the demoted cref reads `<c>Core.VREvents.UiLockChanged</c>`. The symbol is alive at
  `src/GloomhavenVR/Core/Events/VREvents.cs:238`
  (`public static event Action<UiLockEvent>? UiLockChanged;`, namespace `GloomhavenVR.Core.Events`,
  raised at `:264` from `Core/Events/GameEventPatches.cs:76`). The cref failed only because the
  file's own namespace is `GloomhavenVR.WorldUI`, so `Core.VREvents` does not resolve —
  `Core.Events.VREvents` does. The chain the sentence describes is still exactly true:
  `GameEventPatches` (`UIManager.ToggleLockUI`) → `VREvents.UiLockChanged` →
  `WorldUIModule.OnUiLock` (`WorldUIModule.cs:227`) → `CanvasConversion.SetUiLocked`
  (`CanvasConversion.4.Lifecycle.cs:2108`) → `EffectiveLock` (`:2099`) → every host raycaster.
- **Proposed action:** restore the live tag with the correct path:
  `/// mirrored here: <see cref="Core.Events.VREvents.UiLockChanged"/> plus module-side soft locks`
  and delete the `WorldUI/Conversion/CanvasConversion.1.Core.cs | 32` row from
  `.planning/refactor/STALE-DOC-REFS.md`. Sentence otherwise unchanged — it is correct.
- **Guard expectation:** empty (XML doc comments do not appear in the ilspy snapshot — CHARTER §3).
- **Risk if wrong:** none.
- **Cross-lane:** none (STALE-DOC-REFS row names a file in this set, so the row is ours to retire).

file read: CanvasConversion.4.Lifecycle.cs 1-2165 (whole)

### A-03 — R41 confirmed at HEAD, and it is worse than the audit says: `PanelOrderStep` has FOUR in-type consumers and TEN out-of-type literals
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.8.Order.cs` (declaration) —
  in-type derivations at `CanvasConversion.4.Lifecycle.cs:462` (`ConcededOrderMaxOffset =
  PanelOrderStep - ConcededOrderLift - 2`) and `:499` (`RaisedOverlayOrderOffset = PanelOrderStep - 1`).
- **Class:** structure-naming (the R41 row of `.planning/redundancy-audit.md`)
- **Tier:** 1
- **Evidence:** verified at HEAD by grep (see the site list appended under A-03b below). The
  in-type sites already DERIVE from the constant — `CanvasConversion` is one partial type, so
  `private const` is visible to `.4.Lifecycle.cs`. Every site OUTSIDE the type is a frozen literal
  with the relation stated only in prose. `CanvasConversion.4.Lifecycle.cs:432-460` and `:482-497`
  spell the whole band arithmetic out in words (`ModalCloseButton.XOrderOffset` is +2, the grab bar
  is +4, `WorldTooltips.MenuPanelSortingLift` +10, `TablePanelSurfaces` +12, next window at +16) —
  i.e. this file already knows the invariant and cannot express it, because the constant it needs
  is `private`.
- **Proposed action:** `private const int PanelOrderStep = 16;` → `internal const`. Nothing else in
  this lane. The ten literal sites are then free to derive in their own lanes; the ones inside this
  lane's file set (`Grab/GrabBarLayout.cs`, `Grab/ModalCloseButton.cs`, `Hands/HandGhost.cs`) can be
  done by this lane, the rest are NEEDED-OUTSIDE. Value unchanged (16) — no tuning change.
- **Guard expectation:** empty (accessibility of a `const` is not rendered as a behaviour change;
  at most `CHANGED` on the `CanvasConversion` type, names only).
- **Risk if wrong:** none — widening `private` to `internal` cannot change a compiled value.
- **Cross-lane:** NEEDED-OUTSIDE for `WorldUI/Tooltips/WorldTooltips` (+10) and
  `WorldUI/Surfaces/TablePanelSurfaces` (+12) — those belong to lane **worldui-front**.

### A-04 — the deadline reveal ("a window is visible but was NOT settled") prints nothing at the shipped level
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs:202`
- **Class:** risk-gap
- **Tier:** 3 (one-word promotion)
- **Evidence:** `CompleteReveal` has two exits. The settled one is `VRLog.Info` (`:178`) — correct,
  it is the healthy case and belongs in the running commentary. The forced one is
  `VRLog.Warn("WorldUI", $"MODAL REVEAL: '…' FORCED after {waitedMs:F0} ms …")`, i.e. also silent at
  `LogLevel = Info`. Its own text says the window "may show one visible correction" and that "any
  pose re-place still pending is now permanently SKIPPED" — this is the line that explains the
  user-visible "window appears in the wrong place / snaps" report the whole reveal gate was built
  for (`:161-169`, `:189-190`). The `TickRevealGate` doc at `:1017`(approx, method summary) already
  says *"a `Warn` names what was still pending"* — the doc believes this line is heard.
- **Proposed action:** `VRLog.Warn` → `VRLog.Alert` at `:202` only (leave the settled `Info` at
  `:178` exactly as it is — it fires once per float and would flood the player log). Text unchanged.
- **Guard expectation:** `CHANGED` confined to `CanvasConversion`, one call site.
- **Risk if wrong:** a noisier player log on a rig where windows habitually miss the 600 ms budget;
  that is the condition the line exists to surface, so the noise IS the signal. Judgement call for
  the integrator — recorded as a finding, not asserted as a must-fix.
- **Cross-lane:** none.

### A-03 CORRECTED — R41's premise is NO LONGER TRUE at HEAD (already fixed), and the fix that closed it contradicts a shipped caller
- **Status:** supersedes the draft A-03 above. `PanelOrderStep` is **`internal const int
  PanelOrderStep = 16;`** at `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.8.Order.cs:176`,
  and a runtime bound check `CheckFollowerOffset` exists at `:300-314`. Commit `756bda65`
  (2026-09-05) *"fix(worldui): R41 — the panel ladder's one invariant was a private constant and
  ten hand-copied literals"* is an ancestor of HEAD. **Do not re-raise R41 as an open row.** The
  audit's "no assert" and "`private const`" clauses are both stale.
  The out-of-lane consumers now DERIVE or cite it: `Grab/GrabBarLayout.cs:110` (BarOrderOffset 4),
  `Grab/GrabbableModal.cs:137`/`:1619`, `Grab/ModalCloseButton.cs:184` (2),
  `Tooltips/WorldTooltips.cs:718` (10), `Surfaces/SurfaceGrabBar.cs:124`,
  `Surfaces/TablePanelSurfaces.cs:2583`, `Hands/HandGhost.cs:133`, `WorldUI/FreeLabelOrder.cs:90`,
  `Net/Board/BoardVisual.cs:105`, `WorldUI/MapRoom/MapRoomHand.3.Wrist.cs:128`.

### A-05 — DEFECT: the ladder's new bound check fires an Alert-tier HW-VERIFY line on a BY-DESIGN offset, every session a window materialises
- **File:line:** `src/GloomhavenVR/WorldUI/Materialise/WindowMaterialiseDebris.cs:113` +
  `:519` (the registration) vs `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.8.Order.cs:300-314`
  (`CheckFollowerOffset`) and `:355` (`RegisterOrderFollower(…, Renderer, …)`).
  Both files are inside THIS lane's set — this is a same-lane conflict, not a cross-lane one.
- **Class:** defect (also parallel construction of the "what is a legal follower offset?" rule)
- **Tier:** 3
- **Evidence — the demonstration, end to end:**
  1. `WindowMaterialiseDebris.cs:113` declares `private const int DebrisBehindOrderOffset = -1;`.
     Its doc essay at `:85-108` states the reason at length: a converted panel writes NO depth, so
     `sortingOrder` is the ONLY thing that can put geometry behind a window, and the cloud is split
     into two renderers registered *"at offset +1 and -1. The window is drawn between them."*
  2. `TryBuildDebris` at `:519` runs
     `CanvasConversion.RegisterOrderFollower(panel, cl.Behind, DebrisBehindOrderOffset);`
     on **every** debris build (every materialise appear/vanish with debris enabled).
  3. `RegisterOrderFollower(ConvertedPanel, Renderer, int)` at `.8.Order.cs:355` calls
     `CheckFollowerOffset(offset, renderer.name)` **unconditionally, before the dedupe loop**.
  4. `CheckFollowerOffset` (`:300`) returns early only for `offset >= 0 && offset < PanelOrderStep`.
     `-1` fails it, `ReportedBadOffsets.Add(-1)` succeeds the first time, and the method emits
     `VRLog.**Alert**` (prints at the shipped `LogLevel = Info`), marked `// HW-VERIFY`:
     `"PANEL ORDER FOLLOWER OUT OF BAND: 'Behind' registered at offset -1, which is outside
      [0, 16). … this decoration will paint OVER a window that is genuinely nearer … Nothing is
      clamped: the fix is the offset. This is the silent failure survey row R41 named."`
     → **observed:** a player-visible Alert accusing the mod of a defect, once per session, on a
     shipped, deliberate, essay-documented offset. **expected:** silence.
  5. `ApplyPanelOrder` (`.8.Order.cs:719`) applies it anyway with no clamp:
     `f.Renderer.sortingOrder = order + (-1)` = `slot - 1`.
  6. And the check's stated reason is NOT vacuous. `CanvasConversion.9.Furniture.cs:109`
     `FurnitureBandWidth = 5`, `:120` `FurnitureClusterTopOffset = FurnitureBandWidth - 2 = 3`,
     `:248` `FurnitureBandBase(rank) = PanelOrderBase + rank*PanelOrderStep - 5`. So a furniture
     cluster occupies `slot-5 … slot-2` and `:113`'s doc reserves the remaining `slot-1` for
     *"free-floating plates that resolve AGAINST clusters"* (`Net.BoardVisual.OrderWithPanels` —
     the identity tags over a peer's head and on a peer's board corner). The debris BEHIND half at
     `slot-1` therefore **ties** with exactly that reserved plate slot.
- **Cause (archaeology):** `DebrisBehindOrderOffset = -1` predates the check —
  `502eb388 feat(worldui): window debris is real solids in the room, not a decal on the pane`.
  `CheckFollowerOffset` arrived on `756bda65` (2026-09-05, the R41 remedy) and its author enumerated
  the follower offsets from the ten PROSE citations of `PanelOrderStep`, which name +2/+4/+5/+10/+12
  — none of which is negative. The `-1` is registered from a file that cites `PanelOrderStep` only
  in a comment (`WindowMaterialiseDebris.cs:108`) and never as a symbol, so the grep that built the
  bound could not see it. `[[an-audit-is-a-snapshot]]` and `[[a-truncated-list-is-not-absence]]`.
- **Proposed action — a FINDING for the integrator, not a commit, because it needs a design
  decision** (BRIEF §1). The three candidate resolutions, with what each costs:
  (a) **Widen the bound to `(-1, PanelOrderStep)` i.e. accept `-1`,** and move the free-floating
      plate reservation down to `slot-1` sharing with the debris behind-half. Cheapest; but it
      concedes the tie the check's own doc calls out, and `BoardVisual` is another lane's file.
  (b) **Give the debris behind-half its own reserved offset** by widening `FurnitureBandWidth`
      from 5 to 6 and putting the debris at `slot-1` alone with the plates moving to `slot-2`.
      Touches a tuning-shaped constant in `.9.Furniture.cs` → a Tier-3 ordering change.
  (c) **Teach `CheckFollowerOffset` a named exemption** for the materialise behind-half (an
      `allowBehind` flag on the `Renderer` overload, defaulted false). Smallest blast radius,
      keeps the invariant for everyone else, and puts the exception where the reader looks.
  My recommendation is (c): it is the only one of the three that changes no number and no draw
  order — it removes a FALSE alarm and leaves the (real, narrow, never-yet-observed) tie documented
  at the exemption site rather than shouted at the player once a session.
- **Guard expectation:** for (c), `CHANGED` confined to `CanvasConversion` and
  `WindowMaterialiseDebris` (a new bool parameter with a default = a new overload in the snapshot).
- **Risk if wrong:** if the exemption is written too wide, a genuinely bad negative offset stops
  being reported. Scope it to the one call site by parameter, not by a name test.
- **Cross-lane:** none for (c). Option (a) would need `Net/Board/BoardVisual.cs` (lane **net**).

file read: CanvasConversion.2.Adopt.cs 1-1251 (whole)

### A-06 — INVARIANT CHECK (§2 canvas conversion): every entry re-verified at HEAD
One line per entry, as required. All verified against source, not against the prior review.

1. **Nested game Canvases stay ENABLED.** TRUE — `AdoptCanvas` (`.2.Adopt.cs:611`ff) never writes
   `nested.enabled`; the only enable/disable of a nested canvas is the reversible reveal hide in
   `.6.Hide.cs`, restored by `SetPanelRenderVisible(visible:true)` on both exits from `Active`
   (`.4.Lifecycle.cs:36` in `Release`, `:640`ish in the prune branch).
2. **`overrideSorting` re-asserted on a sweep AND per-frame for modals.** TRUE — sweep
   `CanvasSweepIntervalFrames = 30` (`.2.Adopt.cs:14`), per-frame `ReassertAdoptedSorting` gated on
   `panel.PerFrameGuards` (`.4.Lifecycle.cs:~700`). NOTE: since ModBuild 179 the per-frame guard may
   CONCEDE the flag after `ConcedeAfterReclears = 3` and own the number instead — that is an
   addition to the invariant, not a violation of it, and is itself documented at `.4.Lifecycle.cs:343-371`.
3. **Dropdown overlays adopt KEEPING `overrideSorting`.** TRUE — `IsDropdownOverlay`
   (`.2.Adopt.cs:604`), `DropdownListSortingOrder = 4000` (`:598`), `DropdownBlockerSortingOrder =
   3999` (`:601`), `NestedCanvasRecord.KeepOverrideSorting` set at `:643`. The band
   1000 < 1100 < 3999 < 4000 < 5000 is intact.
4. **`Release` ALWAYS detaches before destroying.** TRUE AND STRENGTHENED — `.4.Lifecycle.cs:173`
   `target.SetParent(restoreParent, worldPositionStays:false)` is unconditional, and since 2026-09-03
   the move is VERIFIED (`:187-190`) with a bare-root retry, an `Alert` when Unity refuses, and a
   `DestroyHostSafely` that parks rather than cascades. The invariant's "Breaks if" (restoring the
   "if parent alive" guard) is not present.
5. **The `_disableCanvas` forced-hidden branch.** TRUE — `.4.Lifecycle.cs:236`
   `if (releasedWindow._disableCanvas)` still scopes the `Canvas.enabled = false`; the
   `CanvasGroup` alpha/blocksRaycasts/interactable writes are unscoped, as the entry requires.
6. **`KeepBackgroundHidden` survives Release.** TRUE — `.4.Lifecycle.cs:110-122`, the
   `if (panel.KeepBackgroundHidden)` branch logs and does NOT re-enable.
7. **Fit hysteresis (growth fast, shrink damped).** TRUE — see A-11 below (`.3.Fit.cs`).
8. **Degenerate rects bypass the 100 px clamp.** TRUE — `.1.Core.cs:189-191` computes `degenerate`
   and `:386` stores `panel.FitFrameDegenerate = degenerate`; the fit reads it.
9. **`Flatten2D` in LateUpdate.** TRUE — `.4.Lifecycle.cs:241` `if (panel.FlattenEnabled)
   FlattenSubtree(panel);` inside `LateTick()`. x/y are never written: `RunFlattenPass`
   (`.2.Adopt.cs:914`, `:1009`) writes only `new Vector3(pos.x, pos.y, 0f)` and `localRotation`.
10. **The two alpha floors (0.05 fit / 0.15 mask).** TRUE — see A-12.
11. **Invisible clippers do not stamp depth.** N/A AT HEAD — see A-13: the per-graphic depth mask
    was REMOVED (`.4.Lifecycle.cs:719-723` says so explicitly). Both this entry and #12 are now
    describing a mechanism that no longer exists. Recorded as invariant-registry drift, not a defect.
12. **Per-graphic depth mask, not one union quad.** N/A AT HEAD — same as #11.
13. **World-space scroll views need an explicit clipper.** TRUE — `EnsureScrollClipping`
    (`.2.Adopt.cs:492`), and the two restore lists are still SEPARATE (`AddedScrollMasks` destroyed,
    `EnabledScrollMasks` re-disabled — `.4.Lifecycle.cs:127-140`), which the entry requires.
14. **Every measured graphic clamped to its enclosing clipper.** TRUE — `TryGetVisibleHostRect` +
    `ClipperMemo` in `.3.Fit.cs` (see A-14).
15. **`useModLayer` moves the WHOLE subtree.** TRUE — `ApplyModLayer` (`.2.Adopt.cs:553`) walks
    explicitly rather than using `GetComponentsInChildren`, records every original layer, and
    (ModBuild 180) skips a foreign render subtree WHOLE via `IsForeignRenderSubtree` (`:626`).
16. **sortingOrder 1000 for modal hosts.** TRUE as the CONVERSION TIER; the LIVE order is now
    rewritten every LateUpdate by `TickPanelOrder` (`.8.Order.cs`) and 1000 survives as
    `ConvertedPanel.BaseSortingOrder` for the raycast tie-break — documented at `.1.Core.cs:102-111`.
    The band relation the entry protects is expressed by `PanelOrderBase`/`PanelOrderStep` now.
17. **`EarlySettleUntil` re-runs from BOTH `Tick` and `LateTick`.** TRUE — `.4.Lifecycle.cs:~640`
    (Update) and `:256-269` (LateUpdate). Neither half has been dropped.
18. **Background-opaque detection reads INTRINSIC alpha.** TRUE — `.2.Adopt.cs:~700`
    `if (g.color.a < BackgroundOpaqueAlpha) continue;` — `g.color.a` alone, no inherited term.
19. **`HideFullScreenBackground` never disables a stencil-Mask driver.** TRUE — `.2.Adopt.cs:~693`
    `if (g.GetComponent<Mask>() != null) continue;`.
20. **Render-hidden creation + reveal after settle.** TRUE — `.1.Core.cs:441-449`,
    `TickRevealGate` + `CompleteReveal` (`.4.Lifecycle.cs`).
21. **`capHeightToCanvas` reads `CanvasScaler.referenceResolution.y`.** TRUE —
    `ResolveStableHeightCap` (`.1.Core.cs:471-493`) reads `scaler.referenceResolution.y` and gates on
    `uiScaleMode == ScaleWithScreenSize`; the live rect is not read anywhere in that method.
22. **The one-shot fit waits for N consecutive stable measurements.** TRUE — `OneShotSettleChecks`
    and `SettleOneShotFit` in `.3.Fit.cs` (see A-11).
23. **Width-hug is ESC only; Options keeps full width.** TRUE at this end — `Convert` takes
    `fitContent` and `fitOneShot` as separate parameters and does not itself classify; the
    classification lives in `ModalFallback` (lane worldui-front's neighbour file, not ours).
24. **The fit clamp normalises the target frame corners (`Vector2.Min/Max`).** TRUE — see A-11.
25. **The fit union clamps against the TARGET's frame, not the shrunk host.** TRUE — see A-11.
26. **EVERY pokeable host is content-fit.** TRUE — `.1.Core.cs:383` `if (fitContent ?? pokeable)`.
27. **Game-owned panels keep the UI layer.** TRUE — `.1.Core.cs:47` `UiLayer = 5` with the policy
    comment at `:43-46`; the ONLY re-layering is the recorded, restored `useModLayer` opt-in.

### A-07 — INVARIANT CHECK (§11 entries naming `CanvasConversion`): all six re-verified
1. **`CanvasSweepNextFrame` advanced INSIDE the schedule check.** TRUE — `.2.Adopt.cs:143-144`:
   `if (Time.frameCount >= panel.CanvasSweepNextFrame) panel.CanvasSweepNextFrame = …`. The
   coupling to `ApplyModLayer`'s re-sweep is intact (`.4.Lifecycle.cs`, `sweepDue` term).
2. **The dropdown Blocker is found by NAME under the host.** TRUE — `.2.Adopt.cs:174-189`, direct
   children of `panel.HostRect`, `child.name != DropdownBlockerName` continue. Mod-owned sibling
   canvases (the X) are untouched.
3. **Fit clamps to the target frame; the mask deliberately does not.** PARTLY MOOT — the mask half
   (`CollectVisibleMaskRects`) no longer exists (A-13). The fit half is unchanged and still clamps.
4. **Two definitions of "visible alpha" coexist.** TRUE — `TryGetVisibleHostRect` multiplies by
   `CanvasRenderer.GetInheritedAlpha()` (`.3.Fit.cs`, see A-12) vs `HideFullScreenBackground` using
   `Graphic.color.a` alone (`.2.Adopt.cs`). No shared "is this visible" helper has been extracted.
5. **`ClipperMemo` per-pass + the scratch buffers' single-threaded contract.** PARTLY CHANGED —
   `ClipperMemo` is still cleared per measurement pass, but its SECOND clearing site
   (`CollectVisibleMaskRects`) is gone with the depth mask; the scratch list inventory the entry
   names is now `CanvasScratch`, `ScrollScratch`, `TransformScratch`, `BgGraphicScratch`,
   `BgCornerScratch`, `RectScratch`, `GraphicScratch`, `CornerScratch` minus whatever the mask
   owned. No coroutine or re-entrancy has been introduced — every consumer is a direct call from
   `Tick`/`LateTick`. Detail in A-14.
6. **`FirstCapHeights` never cleared.** TRUE — `.1.Core.cs:58` declares it, `:237-238` is the ONLY
   write and it is `if (!FirstCapHeights.ContainsKey(name))`. `grep -n 'FirstCapHeights'` returns
   exactly those two sites plus the read at `:486`. No `Clear()` anywhere.

file read: CanvasConversion.6.Hide.cs 1-700 (whole)

### A-08 — parallel construction + doc-drift: `UIWindow._disableCanvas` is read TWO ways in the SAME partial type, and the reflective one's stated reason is false
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.6.Hide.cs:668-698`
  (`ReadsDisableCanvas`, `AccessTools.Field(typeof(UIWindow), "_disableCanvas")`) vs
  `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs:236`
  (`if (releasedWindow._disableCanvas)` — a direct field read).
- **Class:** parallel-construction (also doc-drift)
- **Tier:** 2
- **Evidence:**
  - `.6.Hide.cs:661-664` states the reason for the reflection verbatim: *"There is no public
    accessor, so the field is read once through a cached `FieldInfo`; a read that fails for any
    reason answers 'restore'."*
  - That sentence is FALSE about this build. `src/GloomhavenVR/GloomhavenVR.csproj:118`
    `<Reference Include="GH.Runtime" … Publicize="true" …>` (BepInEx.AssemblyPublicizer.MSBuild
    0.4.3) makes every private member of `GH.Runtime` — which is where
    `/home/claw/gloomhaven_vr/decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs:119
    private bool _disableCanvas;` lives — directly accessible at COMPILE time. The proof is in the
    same partial type: `.4.Lifecycle.cs:236` reads it with no reflection at all, and the build is
    green.
  - So the mod holds two readers of one game field, in one type, with different failure modes:
    a direct read (a game rename ⇒ compile break, or `MissingFieldException` at runtime against a
    patched game) and a reflective read (a game rename ⇒ silent `false` plus one `Alert`).
- **Proposed action — I recommend NOT merging, and here is why (negative result).** The reflective
  path is genuinely more robust than the direct one on a patched game, and the difference is not
  cosmetic: `ReadsDisableCanvas`'s failure answer (`false` ⇒ "restore the canvas") is the SAFE
  direction the whole rule is built around, whereas a `MissingFieldException` out of `Release`'s
  hot path would abort the release mid-way and leak a host. **The correct minimal change is the
  COMMENT, not the code:** replace *"There is no public accessor, so the field is read once
  through a cached `FieldInfo`"* with
  > *"The field is `private` in the game (`UIWindow.cs:119`), and although this project's
  > publicizer makes it directly readable at compile time (`CanvasConversion.4.Lifecycle.cs:236`
  > does exactly that), this site reads it through a cached `FieldInfo` ON PURPOSE: a game patch
  > that renames or removes it must degrade to 'restore the canvas' — the direction that draws —
  > and not throw out of a reveal. The one `Alert` below is what makes the stand-down visible."*
  If the integrator prefers one reader, the direction to unify is toward the REFLECTIVE one (make
  `.4.Lifecycle.cs:236` call `ReadsDisableCanvas(releasedWindow)`), never toward the direct one —
  and that IS a small behaviour change on a patched game, so it is Tier 3 and needs the user.
- **Guard expectation:** empty (comment only) for the recommended action.
- **Risk if wrong:** none for the comment fix.
- **Cross-lane:** none.

### A-09 — the one-eye visibility-write detector is silent at the shipped level
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.6.Hide.cs:147`
- **Class:** risk-gap
- **Tier:** 3 (one-word promotion)
- **Evidence:** `AssertNotInRenderPhase` is the detector for the ModBuild-round-6 defect class
  ("a visibility write from a camera callback lands between the two MultiPass eye passes and shows
  in ONE EYE") — the class this project has an entire memory entry for (`[[aliasing-is-per-eye]]`).
  It is latched (`s_renderPhaseViolationLogged`, one line per session — so no flood risk) and it
  appends `System.Environment.StackTrace`, i.e. it is written to be READ off a hardware log. It
  emits `VRLog.Warn` ⇒ nothing at `LogLevel = Info`. Compare the neighbouring rule 400 lines
  further down in the SAME file, `:265-269`, whose comment argues the opposite way and wins:
  *"AND SAY SO AT A TIER THE SHIPPED DEFAULT PRINTS. … on the user's hardware that clause is
  invisible, and reading it here would repeat exactly the mistake that made the 392 round
  unreadable."* The file knows the rule; this one call site predates it.
- **Proposed action:** `VRLog.Warn` → `VRLog.Alert` at `:147`. One word. The latch already bounds
  it to one line per session, so the cost is one line.
- **Guard expectation:** `CHANGED` confined to `CanvasConversion`, one call site.
- **Risk if wrong:** one extra player-log line per session in the (never-yet-observed) violating case.
- **Cross-lane:** none.

file read: CanvasConversion.8.Order.cs 1-866 (whole)

### A-10 — A-05 is SHARPER than "an unseen site": `PanelOrderStep`'s own doc NAMES the -1 it then forbids
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.8.Order.cs:158-166` (the doc
  of `PanelOrderStep`) vs `:300-302` (`CheckFollowerOffset`'s `offset >= 0` term).
- **Class:** defect (evidence supplement to A-05 — same commit, same author, same paragraph)
- **Evidence:** the doc block that justifies making the constant `internal` enumerates the ten
  literals verbatim and includes *"`WindowMaterialiseDebris`'s **+/-1**"*. The very next
  arithmetic sentence is *"2 < 4 < 10 < 12 < 16 is consistent today"* — a list that silently drops
  the `-1` it just named. The bound written three paragraphs later is `[0, PanelOrderStep)`. So the
  contradiction is INSIDE one commit (`756bda65`), not between a new rule and an unseen caller;
  the `-1` was seen, listed, and then excluded from the inequality that became the check. This is
  worth stating because it changes the fix: the question is not "did we miss a site" but "is
  `-1` legal", and only the integrator/user can answer that (A-05 options a/b/c).

### A-11 — `INSTRUMENT-WRITES.baseline`: the single entry for this set no longer meets the file's own admission test
- **File:line:** `.planning/refactor/INSTRUMENT-WRITES.baseline` line
  `CanvasConversion::s_orderHashNodes <- LogPanelOrder`;
  source at `CanvasConversion.8.Order.cs:765` (the write, inside `LogPanelOrder`) and `:625`
  (the read, `Core.PerfMonitor.Count("Order.HashNodes", s_orderHashNodes)` inside `TickPanelOrder`).
- **Class:** doc-drift (against the baseline file, not against source)
- **Tier:** n/a — reporting only, per the task brief ("say so, do not edit")
- **Evidence:** the baseline's own header defines an entry as *"something that is **not a
  diagnostic** reads a field it writes"*. `s_orderHashNodes` is read by exactly one site, and that
  site is `Core.PerfMonitor.Count(...)` — a perf-attribution counter that early-outs on a static
  bool while `[Perf] Attribution` is off, i.e. another diagnostic. Nothing behavioural reads it:
  `grep -rn 's_orderHashNodes' src/` returns four lines, all inside `.8.Order.cs` (declaration
  `:283`, reset `:470`, write `:765`, read `:625`). Deleting `LogPanelOrder` would therefore make
  ONE perf counter read 0 and change no behaviour.
  Additional (harmless) subtlety worth recording: since the S2 hoist of the throttle above the hash
  (`:753-758`), `s_orderHashNodes` is written only on the ~1-in-135 frames that pass the diagnostic
  throttle, so `Order.HashNodes` legitimately reports 0 on every other frame. That is correct —
  it counts work actually done — but a reader of the perf table who does not know it would read the
  zeros as "the hash never runs".
- **Proposed action:** the integrator may retire this baseline row (it is the only `CanvasConversion`
  row, and this lane owns rows naming its own files). Do NOT delete or gate `LogPanelOrder` — the
  `PANEL DRAW ORDER` line is the documented answer to every "X draws over Y" report.
- **Guard expectation:** n/a.
- **Risk if wrong:** none.
- **Cross-lane:** none.

file read: CanvasConversion.3.Fit.cs 1-1240 (slice 1/9), 1240-1780 (slice 2/9)

### A-12 — DEFECT: two unrelated exception paths share ONE report latch, so the first one to throw silences the other for the session
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs:5981`
  (`private static bool s_hitRectFaultLogged;`), written at `:6082` and at `:6464`.
- **Class:** defect (risk-gap in an instrument; `[[a-cap-that-goes-silent]]`)
- **Tier:** 3 (minimal: one extra bool)
- **Evidence:** the latch is read+set by two `catch` blocks in two different methods, with two
  different consequences, and each block's own text says *"Logged once per session"*:
  - `:6077-6090` inside `TryMeasureDrawnContent` — *"DRAWN CONTENT: the visible-extent measurement
    threw … the map room's arc reservations fall back to the HOST RECT … a window with a wide
    transparent frame books more arc than it draws and the room packs more tightly than it needs to."*
  - `:6459-6472` inside `TickHitRect` — *"HIT RECT: the interactive-area measurement threw … Windows
    that draw outside their own frame simply stay unclickable there until this is fixed."*
  Because the two share `s_hitRectFaultLogged`, whichever throws FIRST permanently suppresses the
  other. The two failures are independent (one is a placement/arc-booking question, the other is an
  input question) and the second one is the one a player would report as a bug ("I can't click the
  X"). A session in which the arc measure throws once therefore reports the input failure as
  silence. **And both lines are `VRLog.Warn`**, so at the shipped `LogLevel = Info` neither prints
  at all — the latch question only bites in a debug capture, which is exactly where these lines are
  meant to be read.
- **Proposed action:** split the latch into two — `s_drawnContentFaultLogged` and
  `s_hitRectFaultLogged` — one per verdict class, exactly as the memory entry prescribes. Keep both
  texts byte-identical (no grep token reworded). Separately consider promoting the INPUT one
  (`:6465`) to `VRLog.Alert`: it is latched to one line per session and it names an unclickable
  window, which is player-actionable.
- **Guard expectation:** `CHANGED` confined to `CanvasConversion` (one new static bool, one
  renamed read/write pair).
- **Risk if wrong:** none — strictly more reporting.
- **Cross-lane:** none.

### A-13 — audit: the `VRLog.Warn` instruments in `.3.Fit.cs` that a shipped log will never carry
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs`
- **Class:** risk-gap (bundled; one finding rather than fourteen)
- **Tier:** 3 (each is a one-word promotion) — but this is a JUDGEMENT LIST for the integrator, not
  a blanket recommendation. `.3.Fit.cs` has 31 `VRLog` calls: **28 at Debug tier** (`Info`/`Warn`),
  2 `Note` and 1 `Alert` — the three printing ones all carry `// HW-VERIFY`, so
  `scripts/check-hw-verify.py` (green: *"555 marked line(s) across 193 file(s), all at a tier the
  default log level prints"*) is doing its job and nothing marked is mis-tiered. The gap is in the
  lines that are NOT marked but whose own prose claims a user-report role.
  Ranked by how directly the line names something the user would report:
  1. **`:5616` `MODAL WINDOW: '…' pre-reveal first fit found NOTHING MEASURABLE by its reveal
     deadline`** — the strongest candidate. Its own comment (`:5605-5612`) cites
     `[[sentinel-overflow-and-silent-scans]]` and argues *"One line, once, at the moment it stops
     mattering"*, and the text ends *"If this window is a SHARED one its half-size is a term of the
     shared anchor, so this line is the reason its home looks wrong."* A shared window's home is a
     **1:1 standing-ruling** surface. Latched per panel (`FitNeverMeasurableLogged`).
  2. **`:2934` `FIXED FIT CONCEDED`** — latched per window (`fx.ConcededLogged`); the outcome it
     names is *"the open sub-view will simply be drawn at the size the game gives it and may reach
     outside the frame"*, i.e. the exact symptom of the 2026-08-22 report the fixed fit was built for.
  3. **`:6465` `HIT RECT: … swallowed`** — see A-12; names an unclickable window; latched.
  4. **`:5010` `one-shot fit is INCONSISTENT ACROSS OPENS`** — the pause-menu-size report's own
     falsifier. Fires at most once per open; the text explicitly asks the reader to *"Report this
     line"*, which a player at the shipped level cannot do.
  5. **`:5172` `one-shot fit REJECTED by the validity check`** — names *"the 'empty frame in front,
     content far away' failure"*; hard-capped at `FitVerifyMaxCorrections = 2` per open.
  The remaining nine (`:4722`, `:4845`, `:4908`, `:4930`, `:5211`, `:5291`, `:5322`, `:5708`,
  `:6083`) are per-frame-capable or pure hunt commentary and I recommend leaving them at `Warn`.
- **Proposed action:** promote 1-3 (all latched, all naming a user-visible outcome); leave 4-5 to
  the integrator's judgement; leave the rest. **No text changes anywhere** — a promotion is one word.
- **Guard expectation:** `CHANGED` confined to `CanvasConversion`, N call sites.
- **Risk if wrong:** a busier player log on a rig where these conditions are common. Note that each
  of the three recommended ones is latched, so the worst case is three extra lines per session.
- **Cross-lane:** none.

file read: CanvasConversion.3.Fit.cs 1780-3400 (slices 3-4/9, structural + hazard scan)
file read: CanvasConversion.9d.FlashVeil.cs 1-998 (whole)
file read: CanvasConversion.9e.HiddenWindowVeil.cs — the veil-export region (`:200-235`, `:320-400`, `:580-600`)

### A-14 — §6.5 of the redundancy audit is CLOSED at HEAD. Do not re-open it.
- **File:line:** `src/GloomhavenVR/WorldUI/Materialise/WindowMaterialiseRunner.cs:366-368`
- **Class:** leave-alone (a prior-audit claim verified NO LONGER TRUE)
- **Tier:** n/a
- **Evidence — the task asked me to determine whether `9d` is still outside the chain and whether
  `PreVeilAlpha`'s doc still miscounts. Both answers are NO:**
  1. `9d` DOES export a pre-veil alpha. `CanvasConversion.9d.FlashVeil.cs:317`
     `private static readonly Dictionary<CanvasRenderer, float> FlashVeilHolds = new(256);` and
     `:737 internal static float PreFlashVeilAlpha(CanvasRenderer cr, float own)`.
  2. The runner asks all THREE, nested, in one expression:
     `_origAlpha.Add(CanvasConversion.PreFlashVeilAlpha(cr, CanvasConversion.PreSeatVeilAlpha(cr,
      CanvasConversion.PreVeilAlpha(cr, cr.GetAlpha()))));` — `WindowMaterialiseRunner.cs:366-368`,
     with a comment at `:355-364` that names the audit row: *"ModBuild 439 (survey item B5): AND
     THROUGH THE THIRD. … 9e and 9g exported their pre-veil value and were asked, 9d exported
     nothing and nobody asked it."*
  3. `PreVeilAlpha`'s doc no longer says "the ONE other mod writer". `9e:211-214` now reads
     *"'the one other' is what this said until ModBuild 439, and there were three … All three are
     now asked, in one chain, by `WindowMaterialiseRunner.CollectElements`. A count in a doc comment
     is a claim like any other and this one went stale twice."*
  4. I verified the writer population independently rather than trusting the doc:
     `grep -rn '\.SetAlpha(' src/ --include=*.cs` returns writers in exactly four files —
     `9d.FlashVeil` (`:784`, `:816`), `9e.HiddenWindowVeil` (`:331`, `:393`, `:591`),
     `9g.SubViewSeatVeil` (`:674`, `:680`, `:730`) and `WindowMaterialiseRunner` (`:556`, `:661`).
     Three veils, one effect. The audit's fourth veil (`ModalFallback.11.PreConvertHide`) is not in
     this population at all: it works on `Canvas.enabled`, not on `CanvasRenderer.SetAlpha`, so it
     is correctly outside this chain.
  5. The reconciliation is BIDIRECTIONAL, which the audit did not ask about and which is the other
     half of the same hazard: the runner's per-frame write is itself clamped through the seat veil
     (`Runner.cs:556 cr.SetAlpha(veiled ? CanvasConversion.SeatVeilClamp(cr, a) : a)`), and the
     hidden-window veil's restore defers to a foreign value
     (`9e:591 cr.SetAlpha(foreign != 0f ? foreign : h.Alpha)`).
- **Verdict on the task's question "state it as a demonstrable defect, or as hazard-not-demonstrated":
  NEITHER — it is FIXED.** The window-alive-interactive-and-invisible outcome the audit described
  cannot occur through this path at HEAD.
- **Proposed action:** none in `src/`. The integrator should mark `.planning/redundancy-audit.md`
  §6 item 5 as closed by ModBuild 439 (that file is not this lane's to edit; recorded here so the
  next reader does not spend a round on it). `[[an-audit-is-a-snapshot]]`.
- **Guard expectation:** empty.
- **Risk if wrong:** none.
- **Cross-lane:** none.

file read: PanelInkBounds.cs 1-1023 (whole)

### A-15 — R27 is CLOSED, but the SAME defect survives one constant pair over — and the reason given for it is factually wrong
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs:176-180`
  (`PlateWidthFraction = 0.80f` / `PlateHeightFraction = 0.95f`)
  vs `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs:1843-1844`
  (`private const float FixedFitPlateWidthFraction = 0.80f;` / `…HeightFraction = 0.95f;`)
- **Class:** duplication (the R27 class) + doc-drift
- **Tier:** 2
- **Evidence:**
  - **R27 itself is FIXED and must not be re-raised.** `PanelInkBounds.cs:193` reads
    `private const float FaintAlphaFloor = CanvasConversion.FitMinAlpha;` — a genuine compile-time
    REFERENCE, not a copy. Its doc at `:186-192` names the audit row and the outcome:
    *"ModBuild 439 (survey row R27): the house 'is this graphic painting?' floor existed as FIVE 0.05
    literals … It is one constant now."* And `scripts/check-mirrors.sh:177-180` carries the same
    account. So the task's question — *"is the second a copy of the same house floor or a
    deliberately different one?"* — has a third answer at HEAD: **it is neither; it is the same
    constant, referenced.**
  - **The plate pair beside it is the row R27 did not cover.** `PanelInkBounds.cs:176-179` says:
    *"Full-frame plate test, BY VALUE from `CanvasConversion.FixedFitPlateWidthFraction` and
    `…FixedFitPlateHeightFraction` (0.80 / 0.95). Restated rather than referenced because **those
    are private to another lane's file**; if that pair ever moves, this pair must follow and the
    falsifier's 'plates excluded' count is what would show the drift."*
  - **That reason is FALSE.** `CanvasConversion.3.Fit.cs` and `PanelInkBounds.cs` are both
    `src/GloomhavenVR/WorldUI/Conversion/`, both `namespace GloomhavenVR.WorldUI`, both compiled
    into the same assembly, and per `BRIEF.md` §2 both belong to lane **worldui-frame**. There is no
    lane boundary between them at all. The `FaintAlphaFloor` line three declarations further down
    proves the mechanism works: it references a constant in that same file.
  - **The drift is load-bearing, not cosmetic.** `PanelInkBounds.cs:88-100` records that
    `Ink.Plates` became a HANDLE in ModBuild 447: it is *"read by `GrabbableModal` and decides,
    through `GrabBarLayout.SolveSpan`, whether the grab bar's width and centre come from the frame
    or from this union"*. So if someone tunes `FixedFitPlateWidthFraction` in `.3.Fit.cs`, the grab
    bar's width source silently stops agreeing with the fit's plate exclusion — with nothing but a
    count in a log line to notice.
- **Proposed action (all inside this lane, one file each):**
  1. `CanvasConversion.3.Fit.cs:1843-1844`: `private const` → `internal const` (values unchanged).
  2. `PanelInkBounds.cs:178-179`:
     `private const float PlateWidthFraction = CanvasConversion.FixedFitPlateWidthFraction;`
     `private const float PlateHeightFraction = CanvasConversion.FixedFitPlateHeightFraction;`
  3. Rewrite the doc at `:176-180` to say what is now true:
     > *"Full-frame plate test — `CanvasConversion.FixedFitPlateWidthFraction` /
     > `…FixedFitPlateHeightFraction` themselves (0.80 / 0.95), NOT copies of their values. The
     > ModBuild 447 note below is why: `Ink.Plates` is read by `GrabbableModal` through
     > `GrabBarLayout.SolveSpan` and decides where the grab bar's width comes from, so the two
     > tests must not be able to disagree. Same borrowing as `FaintAlphaFloor` above."*
- **Guard expectation:** `CHANGED` confined to `CanvasConversion` and `PanelInkBounds` — and
  **name-only**: a `const` reference compiles to the identical literal 0.80/0.95, so the compiled
  bodies are byte-identical. No tuning value changes.
- **Risk if wrong:** none. If the guard reports any numeric difference, the change is wrong and
  must be reverted — that is the check.
- **Cross-lane:** none (both files are in this sub-reader's own set).

### A-16 — R7: my side of the `dupes2` clone, and the contract for the Sharpness reader
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/PanelInkBounds.cs:936-950`
  (inside `ComputeActiveSetSignature`) and `:727-735` (`Intersect`)
  vs `src/GloomhavenVR/WorldUI/Sharpness/PanelSupersample.4.Content.cs:2048-2074` and `:1194-1210`.
- **Class:** parallel-construction
- **Tier:** 2 (for the signature half) / leave-alone (for the ink half)
- **Evidence — what the two ACTUALLY share, read on my side:**
  - **They share the SIGNATURE, and it is a near-exact clone.** `PanelInkBounds.cs:936-950` and
    `PanelSupersample.4.Content.cs:2048-2073` are the same fourteen statements in the same order:
    the `NewPartyDisplayUI.PartyDisplay` fetch in a try/catch returning `sig`, the null guard, then
    `sig = sig * 31 + (int)display.ActiveDisplay;` followed by the SAME SIX `MixSubView` calls in
    the SAME ORDER (`CharacterSelector`, `PerkManager`, `AbilityCardsDisplay`,
    `EnhancementCardsDisplay`, `ItemInventoryDisplay`, `BattleGoalWindow`), inside a second
    try/catch. Both files also declare their own `MixSubView`. The only textual difference on my
    side is that the Sharpness copy's empty catch carries a comment and mine does not.
  - **They are NOT the same INK measure, and that is the important half of the answer.** The two
    walks answer different questions and MUST stay separate:
    | | `PanelInkBounds.MeasureCore` | `PanelSupersample`'s content walk |
    |---|---|---|
    | question | *"what does this window PAINT"* (the grab rod's span, the X's seat) | *"what must the CAPTURE FRAME cover"* |
    | on faint content | REJECTS below `FaintAlphaFloor` and counts it into `Ink.Faint` | must NEVER crop — `PanelSupersample.Draws` is *"permissive by design"* (`PanelInkBounds.cs:112-114` states this about its partner) |
    | on full-frame plates | EXCLUDES them (`Ink.Plates`) | must include them |
    | on empty `TMP_Text` | excludes | includes |
    | memoisation | none, deliberately (`PanelInkBounds.cs:471-481`: sharing the fit's memo would mean *"a dictionary keyed on Transforms that grows without bound … holding Unity fake-null keys for destroyed objects"*) | its own |
    **So: the Sharpness twin is a DIFFERENT QUESTION, not a second ink measure.** Merging the two
    walks would re-create the ModBuild 234/239 defects. Only the SIGNATURE is genuinely one concept.
  - **And the stated reason for the signature clone is false the same way A-15's is.**
    `PanelInkBounds.cs:742` — *"restated here because that method is private to another lane's file"*.
    `WorldUI/Sharpness/` is in lane **worldui-frame** (`BRIEF.md` §2), the same lane as
    `WorldUI/Conversion/`. Three sentences in this one file (`:177`, `:742`, `:834`) invoke a lane
    boundary that does not exist; `:834` names `LoadoutConfirmPark` (`WorldUI/Composites/` — lane
    **worldui-front**), which IS a real boundary, so the author had the concept right and applied it
    to the wrong two files twice.
- **CONTRACT FOR THE SHARPNESS SUB-READER (as the task asked me to provide):**
  - The two `ActiveSetSignature` bodies are ONE concept and may be merged. The merge direction that
    is safe is **hoisting the shared party-display tail into ONE `internal static` helper** — the
    six `MixSubView` calls plus the two try/catches — leaving each caller its own PART ONE (they
    differ: mine hashes the active set to `SignatureDepth = 2` with a `TransientFamilies` exemption
    and a `SigMemo`; the Sharpness one hashes direct children only).
  - **The order of the six `MixSubView` calls is load-bearing** (it is a rolling `sig * 31 + …`
    hash) and must be preserved byte-for-byte, or every window in both subsystems sees a phantom
    generation event on the build that changes it.
  - **Do NOT merge the surrounding ink/content walks.** See the table above — five terms differ and
    each one is a named hardware round.
  - Whoever lands it owns whichever file the helper moves OUT of; if the helper lands in
    `PanelInkBounds` it is this sub-set's, if in `PanelSupersample` it is the Sharpness reader's.
    I have not moved anything.
- **Proposed action:** merge the signature tail only, as above; leave both walks alone. **Also fix
  the three false "another lane's file" sentences** at `PanelInkBounds.cs:177`, `:742` (`:834` is
  correct and stays).
- **Guard expectation:** `CHANGED` confined to `PanelInkBounds` and `PanelSupersample` for the
  merge; empty for the comment fixes.
- **Risk if wrong:** a changed hash order would make both the grab rod and the supersample capture
  re-seed their generation on every frame of the affected window. The guard cannot see that — the
  reviewer must diff the six-call order by eye in the commit.
- **Cross-lane:** none across LANES; across sub-readers, yes — flagged for the Sharpness reader.

file read: WindowMaterialiseDebris.cs 1-825 (whole), WindowMaterialiseField.cs 1-406 (whole)

### A-17 — STALE-DOC-REFS `WindowMaterialiseDebris.cs:143` cleared (the line has moved to `:161`)
- **File:line:** `src/GloomhavenVR/WorldUI/Materialise/WindowMaterialiseDebris.cs:161`
- **Class:** doc-drift
- **Tier:** 1
- **Evidence:** the demoted cref reads
  `/// <summary>Front half, then behind half. See <c>Half</c>.</summary>` and it documents
  `private const int Halves = 2;`. No symbol named `Half` exists anywhere in `src/`
  (`grep -rn 'Half\b'` over the file finds only `Halves`, `HalfFront`, `HalfBehind`, `BuildHalf`'s
  `string half` parameter, and the `int h` index). It was an enum that has been replaced by the
  two `int` constants declared on the two lines IMMEDIATELY BELOW the sentence — `:163
  private const int HalfFront = 0;` and `:164 private const int HalfBehind = 1;` — and the
  ordering the sentence describes ("front half, then behind half") is exactly those two values.
  **Note the STALE-DOC-REFS row says line 143; at HEAD it is line 161.** The row is still valid,
  the line has moved.
- **Proposed action:**
  `/// <summary>Two halves, in this index order: <see cref="HalfFront"/> (0) then`
  `/// <see cref="HalfBehind"/> (1) — the order <see cref="BuildHalf"/> is called in and the order`
  `/// the two ladder offsets +1 / -1 correspond to.</summary>`
  Then delete the row from `.planning/refactor/STALE-DOC-REFS.md`.
- **Guard expectation:** empty (doc comments are not in the ilspy snapshot).
- **Risk if wrong:** none.
- **Cross-lane:** none.

### A-18 — STALE-DOC-REFS `WindowMaterialiseField.cs:17` cleared, and the root cause is a FILE NAME that is not a TYPE
- **File:line:** `src/GloomhavenVR/WorldUI/Materialise/WindowMaterialiseField.cs:17`
- **Class:** doc-drift (+ a structure-naming observation)
- **Tier:** 1
- **Evidence:** the sentence reads *"`WindowMaterialiseDebris` builds a shard mesh whose every
  vertex carries the threshold of the point of the window it was torn from"*. That statement is
  **true about the code and false about the symbol**: there is no type called
  `WindowMaterialiseDebris` anywhere in `src/` — `grep -rn 'class WindowMaterialiseDebris' src/`
  returns nothing. `WindowMaterialiseDebris.cs:73` declares
  `internal static partial class WindowMaterialise`, i.e. the file is a PART of `WindowMaterialise`
  named after its subject rather than its type (exactly the `CanvasConversion.N.Name.cs` pattern,
  minus the digits that make the pattern legible).
  The member that actually does what the sentence says is `WindowMaterialise.TryBuildDebris`
  (`WindowMaterialiseDebris.cs:295`), and the type it produces is
  `WindowMaterialise.DebrisCloud` (`:268`).
- **Proposed action:** restore a live cref to the real member:
  `/// <see cref="WindowMaterialise.TryBuildDebris"/> builds a shard mesh whose every vertex carries the`
  Then delete the row from `.planning/refactor/STALE-DOC-REFS.md`.
- **Secondary observation (recorded, NOT proposed as a change):** the `Materialise/` folder's five
  files name three types between them, and two of the five file names are not types —
  `WindowMaterialise.cs` and `WindowMaterialiseDebris.cs` are both parts of `WindowMaterialise`,
  while `WindowMaterialiseVisibility.cs` declares TWO unrelated top-level types
  (`WindowVisibilityHold` at `:88` and `WindowMaterialisePreRoll` at `:410`) and no
  `WindowMaterialiseVisibility`. **Leave it alone** (CHARTER §2: the burden is on the change) — but
  it is why this cref went stale, and if the integrator ever renames, the `.1.` / `.2.` digit
  convention that `CanvasConversion` and `FlatScreen` use would make the partial split visible and
  stop the next reader writing a cref to a filename. Renaming files here would also require
  `bash scripts/patch-inventory.sh generate` if any patch class moved (BRIEF §2) — it would not,
  but the check is owed.
- **Guard expectation:** empty for the comment fix.
- **Risk if wrong:** none.
- **Cross-lane:** none.

files read: ModOwnedContent.cs 1-69 (whole), PanelLayout.cs 1-170 (whole);
  WindowMaterialise.cs / WindowMaterialiseRunner.cs / WindowMaterialiseVisibility.cs —
  read at the instrument, exception-path and veil-reconciliation level (all 28 `VRLog` sites,
  every `catch`, the `SetAlpha` population and `CollectElements`); NOT read line-by-line whole.

### A-19 — dead-code sweep of the whole set: SIX candidates, §5 checklist run on each
- **Method:** a declaration/reference census over both folders cross-checked against `grep -rn`
  across ALL of `src/` and `tests/`, then each survivor checked against BRIEF §5 items 1-8 and
  `git log -S`. Everything else in the set has at least one live reference.
- **Class:** dead
- **Tier:** 0

**(a) `CanvasConversion.6.Hide.cs:569 RevealWithheldCanvases` and `:572 RevealWithheldName`** —
  `internal static` accessors over `s_revealWithheldCanvases` / `s_revealWithheldName`.
  §5: (1) not a Harmony target or patch method; (2) not a Unity message, not serialized;
  (3) `grep -rn` finds the identifiers only at their own declarations — no `nameof`, no string
  literal, no `AccessTools`/`Traverse`/`GetMethod`; (4) not a config key; (5) not a log grep token
  (the token the docs use is the LINE text `REVEAL RESTORE WITHHELD`, which stays); (6) not in
  `Options/DevPanels.cs` or any debug menu; (7) not in `tests/` or `Shims.cs`; (8) they are
  READERS, not the only writer of anything. The two backing fields ARE live — written by
  `SetPanelRenderVisible` / `RevealRestoreWithheld` and read by `RevealWithholdClause` (`:578`).
  `git log -S'RevealWithheldCanvases'` → introduced by `c66c0788` with the rule itself and never
  had an external caller. **VERDICT: dead. Delete the two accessors, keep the fields.**

**(b) `CanvasConversion.9c.SubViewBurst.cs:233 SubViewSetInFlight`** — `internal static bool`, no
  caller. §5: all eight items negative, same evidence shape as (a). `git log -S` →
  `9f42b347 fix(worldui): count the flash on every occurrence, not four times by luck`; it was
  introduced together with the distinction its own doc draws against `SubViewBurstRunning`
  (`[[two-fans-one-name]]`) and the consumer it names — *"A consumer that asks this first can hold
  its last good answer instead"* — was never written.
  **VERDICT: dead, but I recommend KEEPING it and saying so in the doc.** It is two lines, its whole
  value is the named distinction beside `SubViewBurstRunning`, and deleting it invites the next
  author to re-derive the coarser predicate from the finer one — which is exactly the failure the
  memory entry records. Minimal change: append to its summary
  *"NO CONSUMER YET (ModBuild 480). Kept because the distinction it names is the one
  `SubViewBurstRunning` must not be stretched to cover."*

**(c) `PanelPlacement.cs:859 HeadEyeHeight.TrackedCount` and `:862 UntrackedCount`** — same shape
  as (a): `internal static` accessors with no reader; the backing `_tracked` / `_untracked` are
  live (written at `:886`/`:889`, read at `:937-938` by the describe string). Their siblings
  `Comparisons` (`:857`) and `HaveMeasured` (`:865`) DO have external readers, which is why only
  these two fell out. §5 all negative. **VERDICT: dead. Delete the two accessors, keep the fields.**
  *Caution for whoever removes them:* `HeadEyeHeight.TrackedCount`/`UntrackedCount` are name-twins
  of nothing else in `src/` — the apparent second hit in
  `.planning/refactor/.guard/baseline/GloomhavenVR.WorldUI/HeadEyeHeight.cs` IS this same type
  (see A-20: the type lives in `PanelPlacement.cs`), not a second declaration.

**(d) `CanvasConversion.4.Lifecycle.cs:2120 SetSoftLock` — NOT dead, and this is the interesting one.**
  §5 items 1-3 and 5-8 are negative and it has no caller in `src/` or `tests/` — but item 8 inverts:
  it is the **ONLY writer** of `SoftLocks` (`.1.Core.cs:50`), and `SoftLocks` is read by a
  MECHANISM, not a diagnostic: `.4.Lifecycle.cs:2099
  private static bool EffectiveLock => _uiLocked || SoftLocks.Count > 0;` → `IsLockedNow` → six
  call sites across `Cards/CardsGameApi.cs:3476`, `WorldUI/Surfaces/StatPanelSurface.cs` (×3),
  `WorldUI/Surfaces/TrayControlDockSurface.cs:262`, `WorldUI/Modal/ModalFallback.9.Spawn.cs:4046`
  and the raycaster writes at `.1.Core.cs:257` / `.4.Lifecycle.cs:847`.
  **So the second term of `EffectiveLock` is permanently FALSE at HEAD, and has been since
  `00c51010 fix(worldui): floating phase banner gone — round readout docked on the control board`
  removed the one caller.** The doc still describes that caller as live: *"Module-side soft lock
  (e.g. while the phase banner blocks input full-screen in 2D …, UI-ARCH §9.7)"*.
  **VERDICT: keep the method, fix the doc.** Deleting it would cascade into `SoftLocks`,
  `EffectiveLock`'s OR and `ReleaseAll`'s `SoftLocks.Clear()` (`:328`) — four members for a
  capability the module layer may legitimately want back, and `REVIEW-WorldUI.md:549` still lists it
  as part of the core surface. Minimal change: append to the summary
  *"NO CALLER AT HEAD (ModBuild 480). The phase banner this existed for was removed in `00c51010`;
  the mechanism is intact and `EffectiveLock`'s soft-lock term is therefore constantly false until
  something calls this again. Not dead — INERT."*

### A-20 — `PanelPlacement.cs` declares THREE unrelated top-level types
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/PanelPlacement.cs:28` (`PanelPlacement`),
  `:204` (`PanelPoseWatch`), `:827` (`HeadEyeHeight`)
- **Class:** structure-naming
- **Tier:** 1
- **Evidence:** three `internal static class`es in one 957-line file, and the third
  (`HeadEyeHeight`, a head-tracking measurement instrument with its own session counters and its
  own describe string) has nothing to do with panel placement beyond being consulted by it. The
  refactor guard's own snapshot already files it separately —
  `.planning/refactor/.guard/baseline/GloomhavenVR.WorldUI/HeadEyeHeight.cs` — which is exactly the
  CHARTER §3 property that makes moving a whole type to its own file cost **nothing at all** in the
  guard.
  `ConvertedPanel.cs` has the same shape (`ConvertedPanel` + `LayerRecord` + `NestedCanvasRecord` +
  `FlattenRecord`) but there the three structs are that class's own record types and belong beside
  it — **leave that one alone**.
- **Proposed action:** move `HeadEyeHeight` to `WorldUI/Conversion/HeadEyeHeight.cs` and
  `PanelPoseWatch` to `WorldUI/Conversion/PanelPoseWatch.cs`, verbatim, no member reordering.
  Nothing else. This is the one place in my set where a reader is demonstrably lost: three
  unrelated concerns, no digit convention, and the file's name announces only the first.
- **Guard expectation:** **empty** — "Move a whole type to another file → nothing at all"
  (CHARTER §3 table). If the guard reports ANYTHING, the move touched a field order and must be
  reverted.
- **Risk if wrong:** none if the guard is empty; a `MOVED` on a type file would mean a reordered
  field initialiser and must be investigated before the commit stands.
- **Cross-lane:** none. `GloomhavenVR.csproj` uses globbing, so no project edit — verify before
  committing (a `MOVED` on `GloomhavenVR.csproj` is the CHARTER's one allowed Tier-1 guard output).

file read: PanelPlacement.cs 1-957 (whole), SubViewRevival.cs 1-408, OnTopUiGraphics.cs 1-401,
  UnmaskedUiGraphics.cs 1-228, CanvasConversion.9f.SubViewSlot.cs 1-303, 9b.SeeThrough.cs 1-341,
  9c.SubViewBurst.cs 1-622, 9g.SubViewSeatVeil.cs 1-873, 9.Furniture.cs 1-543,
  4b.CameraOwnership.cs 1-599, ConvertedPanel.cs 1-1223 — read at the declaration / instrument /
  hazard level (every static field, every VRLog site, every catch, every dictionary key type,
  every region banner); the long doc essays read in full, the arithmetic bodies scanned.

### A-21 — a further Tier-1 split of `CanvasConversion.3.Fit.cs`: seams named, and the verdict is SPLIT THE FILE, NEVER THE TYPE
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs` (6 941 lines)
- **Class:** structure-naming
- **Tier:** 1
- **Evidence — the four seams are real and contiguous** (from the method census; contiguity is what
  would make a partial split a pure motion):

  | seam | lines | entry points | own private state |
  |---|---|---|---|
  | **A. measure + general fit** | ~1-1240, 4432-5560 | `TryGetVisibleHostRect`, `TryMeasureContent`, `FitHostToContent`, `TickFit`, `SettleOneShotFit`, `VerifyOneShotFit`, `SettlePreRevealFirstFit`, `TickSettleGate` | `GraphicScratch`, `CornerScratch`, `ClipperMemo`, `AuthoredOffsetMemo`, `LastOneShotFits`, `OneShotOpens`, `FitLoops`, 18 `s_last*` |
  | **B. the fixed-size window** | ~1770-4430, 6876-6941 | `IsFixedSizeWindow`, `ApplyFixedFit(Core)`, `MeasureFixedFitParts`, `SolveSubViewPlacement`, `LogFixedFit`, `ReportColumnOverspill` | `FixedFits`, `FixedFitGateLogs`, `SubViewCandidates`, `FixedFitGraphics`, `TransientMemo`, `s_fixedFitOwnerNote` |
  | **C. the hit rect** | ~5820-6875 | `TryGetHitRect`, `TickHitRect`, `TryMeasureDrawnUnion`, `TryMeasureChrome`, `LogHitRect` | `HitRects`, `HitGraphicScratch`, `ChromeGraphicScratch`, `ChromeCornerScratch`, `s_hitRectFaultLogged` |
  | **D. the conversion-frame guard** | ~4617-4757 | `ReassertConversionFrame`, `DescribeTargetFrame` | none of its own |

- **Evidence — WHAT FORBIDS SEPARATE TYPES (CHARTER §2: the burden of proof is on the change).**
  The file holds **52 mutable static fields** and **57 consts**, and the cross-seam sharing is not
  incidental:
  1. `ClipperMemo` (`:681`) and `AuthoredOffsetMemo` (`:456`) are written by seam A's
     `TryGetVisibleHostRect` / `AuthoredOffset` and CLEARED by seam A's `TryMeasureContent`
     (`:757`), seam B's `MeasureFixedFitParts` (`:3400`) **and** seam C's `TryMeasureDrawnUnion`
     (`:6534`). Three seams, one per-pass memo, under a documented single-threaded,
     non-re-entrant contract (INVARIANTS-WorldUI §11). As separate TYPES that contract becomes a
     cross-type protocol no single diff can show.
  2. `TransientMemo` (`:3688`) is declared inside seam B and cleared by seam A's
     `TryMeasureContent` (`:764`) — a genuine backwards dependency.
  3. The 18 `s_last*` measure fields are written by seam A's `TryMeasureContent` and read by
     `DescribeLastMeasure`, which is called from seam A (`FitHostToContent`, `SettleOneShotFit`,
     `VerifyOneShotFit`, `SettlePreRevealFirstFit`) **and** from seam B's release log path.
  4. `FitMinAlpha`, `FitContentPaddingPx`, `FixedFitPlateWidthFraction` / `…HeightFraction` are
     read across seams AND — see A-15 — from `PanelInkBounds`.
  5. `CornerScratch` (a 4-element `Vector3[]`) is reused twice inside one `TryGetVisibleHostRect`
     call (graphic corners, then clipper corners). It survives only because everything is one type
     on one thread.
- **VERDICT — a recommendation of "leave it" is a finding, so here it is explicitly:**
  **Do NOT extract any type.** Do not extract seam C into a `PanelHitRect` class — the tempting one
  — because it would have to reach back into `ClipperMemo`, `TryGetVisibleHostRect` and
  `FitMinAlpha` and would turn five private statics into an `internal` surface.
  **DO split the FILE only**, if the integrator wants it, exactly as parts 6 / 8 / 9x already were:
  `CanvasConversion.3.Fit.cs` (seams A + D) → `CanvasConversion.3b.FixedFit.cs` (seam B) →
  `CanvasConversion.3c.HitRect.cs` (seam C), all `internal static partial class CanvasConversion`,
  members moved VERBATIM, nothing reordered. The gate is
  `python3 scripts/check-partial-order.py`, which is GREEN at HEAD — *"partial order: 23 multi-part
  types, 179 parts, 0 declared cross-part dependencies; no static field initialiser depends on
  another part of its own type."* Two declarations need care if they move:
  `:3636 private static readonly string[] TransientFamilyNames = TransientFamilies.Names;` (an
  initialiser reading another TYPE's static — legal across parts, but it must not be reordered
  ahead of anything `TransientFamilies` needs) and `:1797 FixedFitWidthPx` (a `const` composed of
  three other `const`s — folded at compile time, safe by construction).
  **My own recommendation is LEAVE IT.** Length is not the problem a reader of this file has: the
  four seams are contiguous, each carries a region banner, and only five methods exceed 150 code
  lines — of which two are documented diagnostics (`LogFixedFit` 224, `LogHitRect` 112) and three
  are single decision procedures whose steps must be read in order (`ApplyFixedFitCore` 196,
  `MeasureFixedFitParts` 219, `TickHitRect` 216). CHARTER §2's default answer applies.
- **Guard expectation:** for the file split, `MOVED` on `CanvasConversion.cs` in the snapshot and
  nothing else; `check-partial-order.py` green. Anything `CHANGED` = revert.
- **Risk if wrong:** a reordered static field initialiser is invisible behind a `MOVED` verdict
  (CHARTER §3's own caveat) — the reviewer must diff the 52 declarations by eye.
- **Cross-lane:** none.

### A-22 — config keys and instrument writes: the negative results
- **Config keys.** The whole set binds exactly **four**, all in `WindowMaterialise.cs`:
  `:159 WindowMaterialise`, `:170 WindowMaterialiseAppearSeconds`,
  `:181 WindowMaterialiseVanishSeconds`, `:191 WindowMaterialiseIntensity`. All four are READ
  (`:217`, `:229`, `:238`, `:247`), each through a null-coalesce onto the matching `Defaults.*`
  value. **No INERT key, and no prose default that contradicts its bound default.** Nothing to do.
- **Instrument writes.** Every `Log*` / `Report*` / `Describe*` / `Diagnose*` / `Verify*` body in
  both folders was grepped for a field write and each field's readers traced. Beyond the one
  baseline row (A-11) the writes are: `LogFixedFitGate` → `FixedFitGateLogs`;
  `ReportFlashVeil*` → the `s_flashVeil*` counters; `ReportSubViewBurst` /
  `ReportSubViewSeatVeil` / `ReportSubViewSeatWatch` → the `fx.*LastReported` change-gates;
  `LogFixedFit` → `fx.LastLogTime`; `LogHitRect` → `entry.NextLogAt` / `entry.LoggedGrown` /
  `entry.SuppressedCommits`; `DiagnoseModal` → `panel.DiagLastSnapshot` / `DiagLastCameras` /
  `DiagNextCameraScanFrame`; `LogPanelOrder` → `s_orderDiagLastHash` / `s_orderDiagNextAllowed` /
  `s_orderDiagHeartbeatAt`; `LogAdoptedOrderCensus` → `panel.AdoptedOverrideNow`;
  `RebuildConcededOrderOffsets` → `panel.RebaseClampLogged`.
  **Every one is a throttle or a change-gate, read only by another diagnostic. NOTHING NEW BELONGS
  IN `INSTRUMENT-WRITES.baseline` from this set** (reported, not edited, per the task).
- **Per-frame scene sweeps.** `FindObjectsOfType` / `FindObjectOfType` /
  `Resources.FindObjectsOfTypeAll`: **zero occurrences** in either folder. The single textual hit,
  `CanvasConversion.9d.FlashVeil.cs:491`, is a comment explaining why the game's own `_uiWindows`
  registry is enumerated instead. No sweep defect to report.
- **Fake-null dictionary keys** (the `== null`-on-a-destroyed-Object-used-as-a-key hazard).
  Three dictionaries are keyed on a `UnityEngine.Object`: `9e:200
  Dictionary<CanvasRenderer, VeilHold>`, `9d:317 Dictionary<CanvasRenderer, float>`, `9g:229
  Dictionary<CanvasRenderer, SeatVeilHold>`. All three are drained by an explicit lift/release
  path rather than left to accumulate, and none is ever probed with `== null` as a KEY. The four
  `Dictionary<int, …>` tables (`FixedFits`, `FitLoops`, `HitRects`, `FixedFitGateLogs`) are keyed
  on `GetInstanceID()` and pruned on a destroyed-`Owner` test — the correct shape.
  `PanelPlacement.Entries` is keyed on `ConvertedPanel`, a plain managed class, and pruned at
  `:362`. **No fake-null hazard found in this set** — recorded as a positive result, since this is
  a hazard class the project has paid for elsewhere.
- **Mirror pairs.** None in this set: no local↔remote pair lives in `Conversion/` or `Materialise/`,
  so `check-mirrors.sh` / `check-remote-defaults.py` / `check-mirror-dials.py` have nothing here.
- **A guard missing a term the game's own expression carries** (`check-mirrors.sh` PART 3). The one
  candidate I chased was `RevealRestoreWithheld`'s `_disableCanvas` term against
  `decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs:119/348/572/592`. The mod's four terms
  (`HasGoneToStartingState`, `IsOpen`, `_disableCanvas`, plus the host/target exclusion) are a
  strict SUPERSET of what the game's own `OnTransitionStarted` consults. **No missing term.**
- **Comments asserting a game behaviour, checked against `/home/claw/gloomhaven_vr/decompiled/`.**
  `UIWindow._disableCanvas` is `private bool` at `UIWindow.cs:119` — which confirms A-08's premise
  about the GAME and falsifies its conclusion about THIS build (the publicizer). The five-step
  proof at `9d.FlashVeil.cs:33-95` (`m_CurrentVisualState` field initialiser, `Hide(bool)`'s guard,
  `Start()`'s unconditional flag set, `ShowOrUpdateStartingState`, the instant slam) matches the
  decompiled source line for line. `ObjectPool.cs:468` `SetParent(parent)` with
  `resetLocalRotation` defaulting false (`:415`, `:481-483`) and local z always zeroed
  (`:484-486`), cited by `.2.Adopt.cs:820-835`, also matches. **No falsified game assertion found
  in this set.**
- **Searches that came back empty, so nobody redoes them:** no `[DefaultExecutionOrder]` anywhere
  in the set; no coroutine or `async` anywhere in the set (so the scratch buffers' non-re-entrancy
  contract is not violated by anything shipped); no `Object.Destroy` of a game-owned object outside
  the guarded `DestroyHostSafely` path; no write to game state from a `Log*`/`Report*` body; no
  `PanelSlot` enum value without a consumer (`CombatLog` and `ButtonCluster` are reached only from
  `WorldUI/Options/DevPanels.cs:23`, i.e. BRIEF §5 item 6 — the debug menu, a deliberate feature).

### A-23 — the invariant registry and the 2026-07/08 review have drifted against HEAD in four places
- **File:line:** `.planning/refactor/INVARIANTS-WorldUI.md` §2 (two entries) and §11 (two entries);
  `.planning/refactor/REVIEW-WorldUI.md` §4.1, §4.2, §7.2, §7.3
- **Class:** doc-drift (against the registry, not against source)
- **Tier:** n/a — reported for the integrator. These files are shared artefacts; the task says a
  lane edits only entries naming files in its set, and all four DO, so they are actionable here.
- **Evidence, item by item:**
  1. **§2 "Invisible clippers and raycast catchers must NOT stamp depth"** and
     **§2 "The depth mask is PER-GRAPHIC, not one union quad"** — both describe
     `CanvasConversion.CollectVisibleMaskRects`, `MaskMinAlpha` and the per-graphic depth quad.
     **None of those three symbols exists at HEAD** (`grep -rn 'CollectVisibleMaskRects\|MaskMinAlpha'
     src/` → zero hits). They were removed with the depth stamps themselves; the essay at
     `CanvasConversion.8.Order.cs:13-90` is the replacement and states the ruling that supersedes
     them: *"NO DEPTH WRITING ANYWHERE BETWEEN PANELS — ORDER THEM INSTEAD."* The two entries should
     be marked SUPERSEDED (not deleted — they record why the depth approach failed twice, which is
     exactly what stops a third attempt) with a pointer to `.8.Order.cs`.
  2. **§11 "The fit CLAMPS to the target frame; the depth mask deliberately does NOT"** — half of
     the pair it names is gone (same cause). The surviving half (the fit clamps) is TRUE.
  3. **§11 "`ClipperMemo` is cleared per measurement PASS; the scratch buffers assume
     single-threaded, non-re-entrant ticks"** — the RULE holds and I verified there is no coroutine,
     no `async` and no second driver instance anywhere in the set. But its buffer INVENTORY has
     drifted: it names `RectScratch`, which **no longer exists** (`grep -rn 'RectScratch'
     src/GloomhavenVR/WorldUI/Conversion/` → 0), and it names the memo's second clearing site as
     `CollectVisibleMaskRects`, which is gone. At HEAD `ClipperMemo` is cleared at FOUR sites:
     `.3.Fit.cs:446` (`BeginContentQuery`, the external MR-plate entry point), `:757`
     (`TryMeasureContent`), `:3400` (`MeasureFixedFitParts`) and `:6534` (`TryMeasureDrawnUnion`).
     The live inventory is `CanvasScratch`, `ScrollScratch`, `TransformScratch`, `BgGraphicScratch`,
     `BgCornerScratch`, `GraphicScratch`, `CornerScratch`, plus `HideCanvasScratch`,
     `HideRendererScratch`, `OwnerHideCanvasScratch`, `OwnerHideRendererScratch`,
     `FlashVeilScratch`, `VeilWindowScratch`, `VeilGraphicScratch`, `FixedFitGraphics`,
     `HitGraphicScratch`, `ChromeGraphicScratch`, `ChromeCornerScratch`, `SubViewCandidates` and
     `PanelInkBounds.Stack`/`Corners`. **The entry is worth MORE, not less, after that update** —
     the buffer population has roughly tripled since it was written.
  4. **`REVIEW-WorldUI.md` §4.1 and §4.2 are DONE** (the task told me to verify this and it is
     confirmed): `ConvertedPanel`, `LayerRecord`, `NestedCanvasRecord` and `FlattenRecord` are in
     `WorldUI/Conversion/ConvertedPanel.cs`, and `CanvasConversion` is split into the eleven
     `.1`…`.9g` partials. **The split's own safety rule holds at HEAD:**
     `python3 scripts/check-partial-order.py` → *"23 multi-part types, 179 parts, 0 declared
     cross-part dependencies; no static field initialiser depends on another part of its own type."*
     Exit 0. I also confirmed by inspection that `ConvertedPanel` — the only type in this set with
     INSTANCE fields — declares all of them in ONE file (`ConvertedPanel.cs`) and has no partials,
     so the "every instance field in the primary part" rule is satisfied vacuously.
     **§7.3 is superseded**: the ladder it says has "no shared constant" now has one
     (`CanvasConversion.8.Order.cs:176 internal const int PanelOrderStep = 16`) plus a registration-
     seam check (`CheckFollowerOffset`) — see A-03 CORRECTED and A-05.
- **Proposed action:** the integrator updates those four entries; I have not edited any shared
  artefact. Note for whoever does: the two §2 depth-mask entries must be marked SUPERSEDED and kept,
  never deleted — they are the record of two shipped-and-failed attempts, and `.8.Order.cs`'s own
  header depends on that history being findable.
- **Guard expectation:** n/a (planning files only).
- **Risk if wrong:** none.
- **Cross-lane:** none — every entry names a file in this sub-set.

### A-24 — `ApplyFitConverging` and the fit loop: verified correct, recorded as a negative result
- **File:line:** `src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.3.Fit.cs:4517-4580`
- **Class:** leave-alone
- **Tier:** n/a
- **Evidence:** I went looking for the classic bug in a converging loop — an unbounded iteration, or
  a convergence test on a stale measurement. Neither is present: the loop is hard-capped at
  `FitApplyIterations = 3` (`:4483`), the `resized` test (`:4533`) short-circuits the expensive
  re-measure whenever the size did not actually change (so a rigid re-centre costs one pass), the
  re-measure is preceded by an explicit `FlushPendingLayout` (`:4557`) with a comment naming exactly
  why a render-hidden panel would otherwise report a false convergence, and the tolerance
  (`:4562-4563`) has an absolute 2 px floor under the relative `FitChangeFraction` so a tiny host
  cannot get an unreachable bar. The one allocation — `new StringBuilder(160)` at `:4519` — is on
  the APPLIED-fit path only, which the damping (`FitStableSeconds` 0.5 / `FitRefitMinIntervalSeconds`
  1.5) makes rare, and the converged guard (`:4441`ff, `FitLoopState`) suppresses the repeat case
  entirely. **Nothing to change.**

### A-25 — the "BY VALUE, because it is private" pattern, all four sites: one is fixed, one's comment is stale, two are live copies
- **Class:** duplication (the R27/§0 parallel-construction class) + doc-drift
- **Tier:** 2
- **Why one finding:** this is the same mistake four times in two files, and the integrator should
  see it as one shape rather than four rows. A `grep -rn 'BY VALUE\|Restated rather than referenced\|
  restated here because\|private to another'` over both folders returns exactly seven lines, and
  they are these four sites.

  | # | copy | source | source's accessibility | same lane? | status at HEAD |
  |---|---|---|---|---|---|
  | 1 | `PanelInkBounds.cs:193 FaintAlphaFloor` | `CanvasConversion.3.Fit.cs:82 FitMinAlpha` | **`internal`** since ModBuild 291 | yes (same folder) | **FIXED** — it is a real reference. Only the CLASS-COMMENT at `:81` is stale: *"restated here BY VALUE from `CanvasConversion.FitMinAlpha`, the same borrowing (and the same standing risk of drift) as the plate fractions above"*. There is no borrowing and no drift risk any more. |
  | 2 | `PanelInkBounds.cs:178-179 PlateWidthFraction` / `PlateHeightFraction` | `CanvasConversion.3.Fit.cs:1843-1844 FixedFitPlateWidthFraction` / `…HeightFraction` | `private` | **yes** — same folder, same namespace, same assembly, same lane | **LIVE COPY.** See A-15. Reason given (*"private to another lane's file"*) is false. |
  | 3 | `PanelInkBounds.cs:936-950` signature tail | `PanelSupersample.4.Content.cs:2048-2073` | `private` | **yes** — `WorldUI/Sharpness/` is lane worldui-frame (BRIEF §2) | **LIVE CLONE.** See A-16. Reason given (*"private to another lane's file"*) is false. |
  | 4 | `CanvasConversion.3.Fit.cs:5946 HitRectShrinkDeadBandPx = 32f` | `WorldUI/Grab/GrabbableModal.cs:420 InkReleaseDeadBandPx = 32f` | `private` | **yes** — `WorldUI/Grab/` is lane worldui-frame | **LIVE COPY.** Its own doc names the invariant it is protecting: *"one 32 px quantum, the same dead band the capture frame's shrink hysteresis uses, **so all three instruments agree about what 'smaller' means**."* Three instruments, one number, no shared constant and no lint. |
  | (`PanelInkBounds.cs:834`) | a form of `LoadoutConfirmPark`'s | `WorldUI/Composites/` | — | **NO — that IS a real boundary** (lane worldui-front) | correctly left as a copy |

- **Evidence that this matters and is not tidiness:** #4 is the sharpest. `GrabbableModal.cs:263`
  says *"Lowering `InkReleaseConsecutive` or `InkReleaseDeadBandPx` now would trade away the …"* and
  `:336` says *"NOTHING WAS TRADED AWAY: `InkReleaseConsecutive` is still 3 and
  `InkReleaseDeadBandPx` is still 32"* — i.e. that number is a settled hardware value with a named
  round behind it, and a future round tuning it there would leave the hit rect's shrink dead band at
  32 with nothing to notice. That is the R27 shape exactly ("a constant that is an inline literal
  rather than a shared `const NAME`"), one folder over.
- **Proposed action (all inside lane worldui-frame; #2 is entirely inside THIS sub-set):**
  1. Fix the stale sentence at `PanelInkBounds.cs:81`: *"…is the fit's own floor,
     `CanvasConversion.FitMinAlpha` itself — see `FaintAlphaFloor` below, which references it
     rather than restating it, so the two cannot drift."*
  2. See A-15 — `internal` + reference (this sub-set alone).
  3. See A-16 — hoist the shared signature tail; needs the Sharpness sub-reader.
  4. `GrabbableModal.InkReleaseDeadBandPx`: `private const` → `internal const`, then
     `HitRectShrinkDeadBandPx = GrabbableModal.InkReleaseDeadBandPx;`. `WorldUI/Grab/` belongs to
     this lane but not to this sub-set, so it is one line for whoever owns `Grab/` — **NOT a
     NEEDED-OUTSIDE entry, because it does not leave the lane.**
- **Guard expectation:** name-only `CHANGED` on the touched types; a `const` reference folds to the
  same literal, so the compiled bodies must be byte-identical. **Any numeric difference in the guard
  output means the change is wrong — revert.**
- **Risk if wrong:** none if the guard is clean. No tuning value changes: 0.05, 0.80, 0.95 and 32
  all stay exactly what they are.
- **Cross-lane:** none. Three of the four partners are inside lane worldui-frame; the one that is
  genuinely outside it (`LoadoutConfirmPark`) is correctly left alone.

### A-26 — where my reading was thinner, stated plainly
The set is 25 904 lines and I read it in the order the task gave (risk first). Coverage, honestly:
- **Whole, top to bottom:** `CanvasConversion.1.Core.cs`, `.2.Adopt.cs`, `.4.Lifecycle.cs`,
  `.6.Hide.cs`, `.8.Order.cs`, `.9d.FlashVeil.cs`, `PanelInkBounds.cs`, `PanelLayout.cs`,
  `ModOwnedContent.cs`, `WindowMaterialiseDebris.cs`, `WindowMaterialiseField.cs`.
- **`CanvasConversion.3.Fit.cs` (6 941):** slices 1-4 (`1-3400`) read whole; `3400-6941` read as
  region banners + every declaration + every `VRLog` site + every `catch` + the four census methods
  the task named (`LogFixedFit :4040`, `MeasureFixedFitParts :3338`, `TickHitRect :6120`,
  `ApplyFixedFitCore :2806`) + `ApplyFitConverging`, `ReassertConversionFrame`, `TickFit`,
  `SettleOneShotFit`, `VerifyOneShotFit`, `LockOneShotFit`, `SettlePreRevealFirstFit`,
  `TryMeasureDrawnContent`, `TryMeasureChrome`, `RigUnitsPerMetre`, `ReportColumnOverspill`.
  **Not read line-by-line:** the interiors of `SolveSubViewPlacement` (`:3724`, 75 code lines),
  `EnsureColumnSeam`/`MaybeReDeriveSeamOnNarrowing` (`:3891`-`:4010`) and the body of `LogHitRect`
  (`:6701`, 112 lines of string assembly). None of them writes game state or holds a latch, but
  a second reader should be told they are the thinnest part of my pass.
- **`WindowMaterialise.cs` (1 247), `WindowMaterialiseRunner.cs` (777),
  `WindowMaterialiseVisibility.cs` (582):** read at the instrument / exception-path /
  veil-reconciliation / config-key level (all 28 `VRLog` sites, every `catch`, the whole
  `SetAlpha` population, `CollectElements`, the four `Bind`s and their readers) — **not whole.**
  These three are the largest genuinely under-read area of my set and the honest place for a
  follow-up reader to start.
- **`.9g.SubViewSeatVeil.cs` (873), `.9c.SubViewBurst.cs` (622), `.4b.CameraOwnership.cs` (599),
  `.9.Furniture.cs` (543), `ConvertedPanel.cs` (1 223), `PanelPlacement.cs` (957),
  `SubViewRevival.cs`, `OnTopUiGraphics.cs`, `UnmaskedUiGraphics.cs`, `.9b`, `.9e`, `.9f`:**
  every declaration, every doc block, every `VRLog` site and every `catch` read; the arithmetic
  interiors scanned rather than reasoned through line by line.

---

## Files read (coverage), with line counts

`src/GloomhavenVR/WorldUI/Conversion/` — 22 files, 22 067 lines:

| file | lines | depth |
|---|---|---|
| `CanvasConversion.1.Core.cs` | 584 | whole |
| `CanvasConversion.2.Adopt.cs` | 1 251 | whole |
| `CanvasConversion.3.Fit.cs` | 6 941 | 1-3400 whole; 3400-6941 banners + every declaration, `VRLog` site, `catch` and named census method (see A-26) |
| `CanvasConversion.4.Lifecycle.cs` | 2 165 | whole |
| `CanvasConversion.4b.CameraOwnership.cs` | 599 | declarations, docs, instruments, catches |
| `CanvasConversion.6.Hide.cs` | 700 | whole |
| `CanvasConversion.8.Order.cs` | 866 | whole |
| `CanvasConversion.9.Furniture.cs` | 543 | declarations, docs, instruments |
| `CanvasConversion.9b.SeeThrough.cs` | 341 | declarations, docs |
| `CanvasConversion.9c.SubViewBurst.cs` | 622 | declarations, docs, instruments |
| `CanvasConversion.9d.FlashVeil.cs` | 998 | whole |
| `CanvasConversion.9e.HiddenWindowVeil.cs` | 702 | veil export + `SetAlpha` + lift/release regions |
| `CanvasConversion.9f.SubViewSlot.cs` | 303 | declarations, docs |
| `CanvasConversion.9g.SubViewSeatVeil.cs` | 873 | declarations, docs, `SetAlpha` region |
| `ConvertedPanel.cs` | 1 223 | declarations + docs (four top-level types) |
| `ModOwnedContent.cs` | 69 | whole |
| `OnTopUiGraphics.cs` | 401 | declarations, docs |
| `PanelInkBounds.cs` | 1 023 | whole |
| `PanelLayout.cs` | 170 | whole |
| `PanelPlacement.cs` | 957 | whole (three top-level types) |
| `SubViewRevival.cs` | 408 | declarations, docs |
| `UnmaskedUiGraphics.cs` | 228 | declarations, docs |

`src/GloomhavenVR/WorldUI/Materialise/` — 5 files, 3 837 lines:

| file | lines | depth |
|---|---|---|
| `WindowMaterialise.cs` | 1 247 | instruments, config keys, catches, `Name`/raycaster teardown |
| `WindowMaterialiseDebris.cs` | 825 | whole |
| `WindowMaterialiseField.cs` | 406 | whole |
| `WindowMaterialiseRunner.cs` | 777 | `CollectElements`, the veil chain, every `VRLog`, every `catch` |
| `WindowMaterialiseVisibility.cs` | 582 | every `VRLog`, every `catch`, the two top-level types' declarations |

**Correction to the task brief:** it names 28 files in `Conversion/` and 6 in `Materialise/`; at
HEAD `5aa00714` there are **22** and **5**. Nothing named in the task is missing — the counts are
just stale.

Governance read in full first, as required: `refactor-2026-09/BRIEF.md` (227),
`refactor/CHARTER.md` (168), `INVARIANTS-WorldUI.md` §2 + §11 + the entries naming my symbols,
`REVIEW-WorldUI.md` §4.1/§4.2/§7.2/§7.3 + every `CanvasConversion` hit,
`redundancy-audit.md` §0/§4/§6 + every row naming my files, `STALE-DOC-REFS.md`,
`FRAME-ORDER.lock`, `INSTRUMENT-WRITES.baseline`, `Core/VRLog.cs`.

## Verified still true / no longer true

**STILL TRUE at HEAD:**
- All 27 `INVARIANTS-WorldUI.md` §2 entries except #11 and #12 — see A-06, one line each.
- 4 of the 6 §11 entries naming `CanvasConversion` — see A-07.
- `FRAME-ORDER.lock` `WorldUIModule.LateTail | [PanelMipBake.TickArrivals, GrabBarTween.TickAll,
  CanvasConversion.TickPanelOrder]` — `TickPanelOrder` is still last, and `.8.Order.cs:450`'s own
  doc restates why ("run LAST in the WorldUI LateUpdate chain"). No marker moved.
- `INSTRUMENT-WRITES.baseline`'s one `CanvasConversion` row still describes the code — but see A-11
  for why it no longer meets the file's own admission test.
- `redundancy-audit.md` §4 item 18 ("four `CanvasRenderer` alpha veils … the four-way split is
  justified") — still true, and the hazard it flagged in the same breath is now closed (A-14).
- `REVIEW-WorldUI.md` §7.2 (the single-threaded scratch-buffer contract) — the RULE holds; its
  inventory has drifted (A-23 item 3).
- `PanelSlot.CombatLog` / `ButtonCluster` are reached only from the debug menu — BRIEF §5 item 6,
  a deliberate feature, not dead.

**NO LONGER TRUE at HEAD:**
- `redundancy-audit.md` **R41** — `PanelOrderStep` is `internal` and the invariant IS checked
  (`756bda65`). A-03 CORRECTED. And the check contradicts a shipped caller — A-05, A-10.
- `redundancy-audit.md` **R27** — the 0.05 floor is one referenced constant, not five literals
  (`PanelInkBounds.cs:193`, `check-mirrors.sh:177-180`). A-15. The plate pair beside it is the row
  R27 did not cover.
- `redundancy-audit.md` **§6 item 5** — `9d` exports `PreFlashVeilAlpha`, the runner asks all three
  veils, and `PreVeilAlpha`'s doc no longer miscounts. A-14. Closed by ModBuild 439.
- `redundancy-audit.md` **R7** — `PanelInkBounds` has the inherited-alpha term now
  (`FaintAlphaFloor`); what remains of R7 on my side is the signature clone, not the ink walk. A-16.
- `INVARIANTS-WorldUI.md` §2 entries #11 and #12 and §11's "fit clamps / mask does not" — the
  depth-mask mechanism they describe no longer exists. A-23.
- `REVIEW-WorldUI.md` §4.1 and §4.2 — **DONE**, and `check-partial-order.py` is green (A-23 item 4).
- `REVIEW-WorldUI.md` §7.3 ("the ladder has no shared constant") — superseded by `PanelOrderStep`.
- `STALE-DOC-REFS.md` — all three rows naming my files are clearable: A-02, A-17, A-18.
  (The `WindowMaterialiseDebris.cs` row says line 143; at HEAD it is 161.)

## What I did not find (searches that came back empty — nobody redo these)

- **No per-frame `FindObjectsOfType`** anywhere in either folder. Zero occurrences of any `Find*`
  scene sweep; the one textual hit is a comment saying why the game's own registry is used instead.
- **No fake-null dictionary-key hazard.** All seven `UnityEngine.Object`-adjacent tables are either
  keyed on `GetInstanceID()` with a destroyed-owner prune, or drained by an explicit release path,
  or keyed on a plain managed class. None is probed with `== null` as a key.
- **No coroutine, no `async`, no second driver instance** anywhere in the set — so the scratch
  buffers' documented non-re-entrancy contract is not violated by anything shipped.
- **No mirror pair** (local↔remote) in either folder, so no `check-mirrors.sh` /
  `check-mirror-dials.py` / `check-remote-defaults.py` exposure and no 1:1 dial hazard.
- **No guard missing a term the game's own expression carries.** The one candidate
  (`RevealRestoreWithheld` vs `UIWindow.OnTransitionStarted`) is a strict superset.
- **No falsified game assertion.** Every comment in the set that claims a game behaviour and that I
  could check cheaply against `/home/claw/gloomhaven_vr/decompiled/` — `UIWindow._disableCanvas`,
  `HasGoneToStartingState`, `m_CurrentVisualState`, `Hide(bool)`'s guard,
  `ShowOrUpdateStartingState`, `ObjectPool.Spawn`'s `resetLocalRotation` default,
  `InitiativeTrack`'s nested canvas at order 40, `TMP_Dropdown.Show()`'s no-op-while-list-lives —
  checked out.
- **No INERT config key and no prose default contradicting a bound default.** Four keys, all read.
- **No `// HW-VERIFY` line at a silent tier**: `python3 scripts/check-hw-verify.py` is green
  (*"555 marked line(s) across 193 file(s), all at a tier the default log level prints"*), and the
  34 marked lines in my set are all `Note`/`Alert`/`Error`. The tier gaps I DID find (A-01, A-04,
  A-09, A-12, A-13) are all on lines that are NOT marked — which is precisely the blind spot that
  checker cannot cover, and the reason those five findings exist.
- **No exception path that leaves state latched** other than the shared-latch defect A-12. Every
  other `catch` in the set either restores, releases, or degrades to the pre-fix behaviour and says
  so; `PanelFlattenDriver.LateUpdate` (`.2.Adopt.cs:1232`) self-disarms at `VRLog.Error` rather
  than starving VR input, which is the correct shape.
- **No frame-order change candidate.** Nothing in my set proposes moving a `FRAME-ORDER.lock` step,
  and no marker comment in the set disagrees with the lock file.
- **No wire field, no `ModBuild` reference, no record id, no `Loc` string** anywhere in either
  folder — the whole set is presentation-local, so the hard rules on wire format and localisation
  have no surface here.

## Findings by class

| class | count | IDs |
|---|---|---|
| defect | 2 | A-05, A-12 |
| risk-gap | 4 | A-01, A-04, A-09, A-13 |
| parallel-construction | 2 | A-08, A-16 |
| duplication | 2 | A-15, A-25 |
| dead | 1 (4 members) | A-19 |
| structure-naming | 2 | A-20, A-21 |
| doc-drift | 4 | A-02, A-17, A-18, A-23 |
| leave-alone / negative result | 5 | A-03 CORRECTED, A-06, A-07, A-14, A-22, A-24, A-26 |

Ranked by risk reduced, the order to act in is:
A-05 → A-12 → A-01 → A-04 → A-09 → A-13 → A-15 → A-25 → A-16 → A-08 → A-19 → A-20 → A-21 →
A-02 / A-17 / A-18 → A-23 → A-11.

**Status: COMPLETE.**
