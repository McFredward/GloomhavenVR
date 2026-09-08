# Invariant registry — Hands / Board / Core / Compat / plugin root

> Companion to `CHARTER.md`. Phase 1 deliverable: **which logic is load-bearing and why**.
>
> Every entry below is code that looks arbitrary and is not. It is the residue of a bug
> that cost hardware test rounds. Before touching any of it, read the entry.
>
> **Where** uses SYMBOL NAMES ONLY (no line numbers — files are being edited concurrently).
> **Established by** is the commit that introduced or fixed the behaviour; where the reason
> is documented in-source but the exact commit is inferred from the log subject, confidence
> is marked accordingly.
>
> **Last verified 2026-09-08 against `49ceab21` (ModBuild 483).** Corrections from that pass carry
> **[verified 2026-09-08]**. Read this before trusting an unmarked entry:
>
> - The audit checked **every symbol named in a `Where:` line of sections 1, 5, 15 and 16**, plus
>   spot checks elsewhere. **Five entries named members that no longer exist**, and three of them
>   had gone further than a rename — the MECHANISM had inverted, so the entry forbade what ships.
>   Those are the dangerous ones and they are rewritten in place with the old approach kept as a
>   rejected approach.
> - Entries **outside** sections 1, 5, 15 and 16 were **not** re-derived symbol by symbol. Treat
>   an unmarked entry as "believed true, last read 2026-09-08", not as re-measured.
> - The 2026-08 ruling that "the beam always picks; only COMMITS are gated" invalidated more of
>   section 1 than the two entries the brief for this audit named. Where a section-1 entry talks
>   about *gating the pick*, check it against `RayInteractor`'s current class doc first.
>
> Scope: `src/GloomhavenVR/Hands/`, `Board/`, `Core/`, `Compat/`, `Plugin.cs`.
> **[verified 2026-09-08]** Measured at `49ceab21`: `Hands/` 25 files / 11 834 lines, `Board/`
> 62 / 38 082, `Core/` 122 / 101 683 — 209 files, 151 599 lines across the three. The registry
> was written against a far smaller tree; **its silence about a file is not evidence that
> nothing load-bearing lives there.**
> Cards / WorldUI / Rig / Net invariants are out of scope here except where a
> Hands/Board/Core symbol depends on them (noted inline).

## How to use this during the refactor

- An entry is a **veto**, not a note. If a proposed change makes the "Breaks if" line true,
  the change is Tier 3 (behavioural) and out of scope per CHARTER §4.
- Ordering constraints (§ "Frame ordering") are the most fragile class here: they survive
  a compiled-form diff untouched *and* are exactly what a "tidy up the Update method"
  refactor destroys. Guard output cannot catch them.
- Log strings are **grep tokens** the user's hardware reports rely on (CHARTER §5).
  Rewording a log line that names a root cause is a documentation loss, not a cleanup.

---

## 1. Ray / laser (Hands.Interact.RayInteractor)

### UiHitOverride ends the beam at a PROJECTION, not at the point
- **Where:** `RayInteractor.UiHitOverride`, `RayInteractor.UpdateVisuals`
- **Rule:** the published point only ever contributes `Vector3.Dot(point - origin, direction)`
  — its along-ray distance. The beam's two endpoints must both lie on `(origin, direction)`.
- **Why:** the visible beam is a straight segment of the aim ray; a hit may clamp its LENGTH
  and nothing may re-aim it. Publishing a point that is off the aim line (a latched press
  point, an object's centre) therefore creates a *phantom plane through that centre,
  perpendicular to the beam, at every aim direction* — the tester's "invisible wall drawn
  orthogonally through the middle of the card". The earlier geometry (start at knuckle, end
  at the hit POINT) made the beam visibly "zap" onto elements.
- **Established by:** `4330504` fix(cards): the phantom "wall through the middle of the card" was a beam clamp, not a collider; original straight-beam rule `fe95b06` fix(hands): dead-straight laser (length-clamp only) + beam/dot on top of UI
- **Breaks if:** you "simplify" `UpdateVisuals` to `end = _uiHitOverride.Value`, or reintroduce
  a reticle/end-point override that moves the dot off the aim line (the removed
  `ReticleOverride`).
- **Confidence:** high

### SuppressFarClick decouples "claim the trigger" from "move the beam"
- **Where:** `RayInteractor.SuppressFarClick`, `RayInteractor._farClickFrame`, `RayInteractor.HasFreshUiHit`
- **Rule:** a driver that owns the trigger but has **no real beam hit point** must call
  `SuppressFarClick()`, never publish a synthetic `UiHitOverride`. `HasFreshUiHit` honours
  EITHER latch (fresh override OR fresh far-click claim).
- **Why:** the two duties used to be welded together; the only way to say "don't let this
  trigger also fire a board click" was to publish a world point, which then clamped the beam
  — see the phantom-plane entry above.
- **Established by:** `4330504`
- **Breaks if:** the two latches are merged back into one field, or `HasFreshUiHit` is
  narrowed to `_uiHitOverride.HasValue`.
- **Confidence:** high

### The freshness window is TWO frames (`<= 1`), not one
- **Where:** `RayInteractor.HasFreshUiHit`, `RayInteractor.UpdateVisuals` (`uiHit` local)
- **Rule:** `Time.frameCount - <latchFrame> <= 1`.
- **Why:** producers and consumers run in different phases of the frame (RayUgui/CardsDriver
  publish during `VRHand.Update`; `BoardClickDriver.Tick` and the game's `Controller.LateUpdate`
  consume later; `FigureGrabDriver.TryLaserGrab` explicitly compares `Time.frameCount - myClampFrame > 1`).
  A strict same-frame test drops the claim for one of the consumers and the trigger
  double-fires as a board click.
- **Established by:** inherent to `4330504` / `c2234ad`; the `> 1` counterpart is in
  `FigureGrabDriver.TryLaserGrab` (`8d28b55` feat(figures): grab real board minis into hand)
- **Breaks if:** tightened to `== Time.frameCount`.
- **Confidence:** medium

### `Active` is LEVEL-derived every frame — never edge-latched
- **Where:** `RayInteractor.Active`, `RayInteractor.IsHolding`, `RayInteractor.SyncActiveState`,
  `VRModeStateMachine.InteractorsFor(VRMode, HandRole)`
- **Rule:** every input that can turn the ray off is re-read from live state each `Tick`
  (mode mask, `_hand.HasPose`, `Grabber.Held`). The dominant hand's `Ray` bit is OR'd in
  centrally for EVERY mode. The truth table in the `Active` doc comment is the specification.
- **Why:** hardware test #19 — the dominant laser silently vanished mid-scenario and never
  came back. Root cause: a single-target attack waits in Choreographer state
  `WaitingForCardSelection`, which is **not** a `TargetingStates` member, so the mode stayed
  `HalfSelection`, whose policy row carried no `Ray`. A latched-off laser is unrecoverable
  without a mode event that may never arrive.
- **Established by:** `d73d155` fix(hands): level-derive the ray's effective state — no edge-latched off; `e06bae1` fix(mode): dominant hand's ray is on in EVERY mode — the #19 latch
- **Breaks if:** the dominant-hand `set |= Interactors.Ray` is moved back into the
  `InteractorPolicy` / `HandPolicy` tables (rows there only distribute Poke/Grab/PalmGate and
  the NON-dominant ray), or `Active` is cached across frames.
- **Confidence:** high

### `SyncActiveState` emits one change-deduped line naming the CAUSE
- **Where:** `RayInteractor.SyncActiveState` (`_wasActive`, `_lastReason`)
- **Rule:** every on/off flip and every reason change logs exactly once, naming the gate.
- **Why:** #19 was undiagnosable because the disappearance was silent. This is the designed
  evidence trail for the next silent disappearance; it is a feature, not debug residue.
- **Established by:** `d73d155`
- **Breaks if:** removed as "log spam" — it is already deduped to zero per-frame cost.
- **Confidence:** high

### Fan occlusion is a GEOMETRIC test, computed once per Tick, shared by three consumers
- **Where:** `RayInteractor.FanOccluderDistance`, `RayInteractor.ComputeFanOccluder`,
  `RayInteractor.FanOcclusionEpsilonMeters`, consumed by `RayInteractor.Tick`,
  `RayUguiDriver.Tick`, `RayGrabDriver.Tick`
- **Rule:** the raised card fan occludes any board/hex/menu/grab-bar target more than
  `FanOcclusionEpsilonMeters * scale` behind it. It is NOT a physics query (fan cards are
  trigger colliders on the mod layer, invisible to the pick `Mask`) and it must be computed
  before the physics raycast so all three consumers see the same value.
- **Why:** the laser selected things visible THROUGH the hand of cards — hex tiles, a floated
  window's grab bar.
- **Established by:** `c2234ad` fix(hands): raised card fan occludes the laser pick
- **Breaks if:** each driver recomputes its own (drift between consumers), the epsilon is
  dropped (grazing self-occlusion), or the fan's own interactions are routed through this
  pick instead of `CardsDriver`'s independent fan raycast.
- **Confidence:** high

### The beam ALWAYS picks; only COMMITS are modal-gated, and the two targets gate differently
**[verified 2026-09-08 — this entry replaces "Modal pick-block keys on BlockingWindowModalActive".
Every symbol the old entry named is gone from `src/`, and the mechanism inverted: it is no longer
the pick that is gated.]**

- **Where:** `RayInteractor.UpdateCommitSuppression` (static), `RayInteractor._cardTrayCommitsSuppressed`, `RayInteractor._boardClickCommitsSuppressed`, `RayInteractor._commitPolicyFrame`. The predicates it reads are `WorldUI.ModalFallback.BlockingWindowModalActive` (card/tray) and `WorldUI.ModalFallback.HardCommitLockActive` (board clicks).
- **Rule:** the physics pick **always runs**. No modal state suppresses it. What is suppressed is the *commit*: card and tray commits while a BLOCKING floated window is up; board clicks only under the **hard** lock (results screens / error box). A reachable non-blocking menu (pause/ESC, Options, Multiplayer, Compendium) imposes **zero** restrictions on either.
- **Why:** the two reasons, in the order they were learned. (1) The original one, still true: with a broader predicate, opening the pause menu froze all board and card picking while the player was still expected to interact. (2) The 2026-08 user ruling that inverted the mechanism: suppressing the *pick* also killed the beam's collision and every hover behind the menu — the reported *"laser appears but collides only with the grab bar"*. The anti-click-through duty moved entirely to the commit layer, where the modal's own uGUI wins the trigger by nearest-hit arbitration plus `HasFreshUiHit`. The full per-target decision table is at `ModalFallback.HardCommitLockActive`.
- **Established by:** `bd790d5` (the Blocking-vs-plain distinction, item 4 wiring), then the 2026-08 commit-layer ruling that made the pick unconditional.
- **Breaks if:** the pick is gated on any modal predicate again — that is the regression this entry exists to prevent, and it is *not* what the old sentence said. Also: collapsing the two flags into one. They are deliberately different predicates; `BlockingWindowModalActive` is wider than `HardCommitLockActive`, and a board click under a merely blocking window must still land.
- **Confidence:** high (re-read at `49ceab21`)

**REJECTED APPROACH, kept so it is not retried: `VRMode.ModalUI` as a pick gate.** The old rule named it. It no longer gates anything here, and `Board/BoardPick.cs` records the ruling at the site: *"ModalUI deliberately does NOT bail any more (user ruling 2026-08…)"*. The enum member still exists and is still used elsewhere; only its role as a pick gate is gone.

### The commit-suppression policy is computed once per frame and SHARED by both hands
**[verified 2026-09-08 — the invariant holds; the symbols are renamed.]**
- **Where:** `RayInteractor._commitPolicyFrame` (static), `RayInteractor.UpdateCommitSuppression` (was `_modalPickFrame` / `UpdateModalPickBlock`)
- **Rule:** static, frame-memoized; the transition log fires once, not per hand.
- **Why:** it is a global mode fact; per-hand evaluation double-logs and can disagree
  mid-frame if a window closes between the two hands' `Tick`s.
- **Confidence:** medium

### Ray visuals draw at sortingOrder 5000 AND renderQueue 4600 — both are needed
- **Where:** `RayInteractor.RayVisualSortingOrder`, `RayInteractor.CreateBeamMaterial`
- **Rule:** the LineRenderer and the reticle MeshRenderer get `sortingOrder = 5000` **and**
  their shared material gets `renderQueue = 4600`. ZTest stays LEqual.
- **Why:** two independent losses. (a) Unity sorts renderers by sortingLayer → **sortingOrder
  first**, only then by renderQueue — a floated modal host raised to `sortingOrder = 1000`
  painted straight over a queue-4600 dot at default order 0. (b) Within the transparent
  queue, world-space canvas graphics (UI/Default, TMP) sort by depth, so coplanar canvas
  geometry could draw over the dot. Keeping ZTest LEqual is what preserves real occlusion
  behind solid furniture (UI shaders write no depth, so the depthless modal never occludes).
- **Established by:** `2e301f6` fix(ray): draw laser hit dot above floated modals (sortingOrder);
  queue-4600 half from `a3e752f` fix(hands): laser dot visible across the entire clickable dialog surface
- **Breaks if:** either is removed as redundant, or ZTest is forced to Always ("simpler").
- **Confidence:** high

### Reticle and beam width are ANGULAR, and the reticle divides out parent lossyScale
- **Where:** `RayInteractor.ReticleAngularFactor`, `BeamWidthAngularFactor`, the four
  Min/Max clamps, `UpdateVisuals` (`parentScale` division)
- **Rule:** size is derived from head→endpoint distance, and the reticle's `localScale`
  is divided by `_hand.transform.lossyScale.x`.
- **Why:** hardware test #8 — the reticle scaled with the rig's `WorldScale`; zooming the
  diorama out grew the dot enormously, and doubly so because `localScale` sat under an
  already rig-scaled parent.
- **Established by:** `d3ef4cc` feat(hands): laser uGUI driver, comfy palm gate, angular ray visuals, trigger-pluck grab
- **Breaks if:** the double-compensation is "simplified" to one factor.
- **Confidence:** high

### ~~`VisualsAllowed` short-circuits on `HasFreshUiHit` BEFORE the cone test~~ — RETIRED, the whole gate is gone
**[verified 2026-09-08 — struck out, not deleted: the measurement below is why the gate was removed, and it is the argument against building another one.]**
- **Where:** `RayInteractor.VisualsAllowed` still exists as a **seam**, with the body `=> true;`. There is no cone, no short-circuit and no `HasFreshUiHit` test in it. The signature is deliberately kept so a future policy slots back in at that one place; the class doc says so.
- **Rule at HEAD:** the beam's visuals are shown wherever the hand points, in every phase.
- **Why the old rule is kept as a record:** hardware test #13 — the cone measured the angle to the canvas CENTRE only. On a floated story window (1920 px x 0.7 ≈ 1.3 m wide at 1.2 m) the outer half sat outside the 25° cone, so the dot vanished while clicks kept landing. The short-circuit was the patch; removing the cone altogether was the fix. **`[Hands] RayAlwaysOn` and `[Hands] ModalRayConeDegrees` are DELETED config keys** (2026-08 dead-settings sweep; `ConfigSteps.cs` records `Hands/ModalRayConeDegrees is GONE`). `ModalRayConeDegrees` survives in comments only — a grep for it finds prose, not a binding.
- **Established by:** `a3e752f` (the short-circuit), retired by the 2026-08 sweep.
- **Breaks if:** someone re-introduces an angular gate on beam visuals. If that is ever wanted, it goes in `VisualsAllowed` — that is what the seam is for — and it must handle the wide-canvas case above, which a centre-angle cone cannot.
- **Confidence:** high (re-read at `49ceab21`)

### Beam visual origin is the knuckle PROJECTED onto the aim line, and clamped to 90 % of length
- **Where:** `RayInteractor.UpdateVisuals` (`Plugin.LaserFingerOrigin` branch),
  `HandRig.IndexKnuckle`
- **Rule:** the start point is `origin + direction * min(alongRayDistanceOfKnuckle + offset, length * 0.9)`.
  The anchor is the **knuckle** (curl-independent), not the fingertip.
- **Why:** the fingertip curls with the trigger pull (FingerCurler), which swung the beam on
  every press (test #7); an un-projected off-axis start point re-aims the beam (test #14).
  The 0.9 clamp stops the start overtaking the end on very short clamped beams.
- **Established by:** `fe95b06`; `IndexKnuckle` contract added in the same round
- **Breaks if:** the anchor reverts to `IndexTip`, or the projection/clamp is dropped.
- **Confidence:** high

### Visuals are created lazily and re-layered locally
- **Where:** `RayInteractor.CreateVisuals` (`VRLayers.Apply` on both new GOs)
- **Rule:** the laser/reticle GameObjects are created inside `UpdateVisuals` (i.e. after
  `HandsDriver`'s tree-wide `VRLayers.Apply`), so they must apply the mod layer themselves.
- **Why:** anything created after the tree sweep stays on layer 0 and is culled by the head
  camera, which renders the mod layer only. Same class of bug as the glove prefab
  (`HandVisuals.Build` re-applies the layer for exactly this reason).
- **Established by:** `7d9a338` fix(hands+board): glove on mod layer; deeper card seating; mod board elements face player
- **Breaks if:** creation is hoisted into `Initialize` "for tidiness" without keeping the
  layer call, or the layer call is deleted as duplicated.
- **Confidence:** high

---

## 2. uGUI pointer (Hands.Interact.UguiPointer)

### Hover dispatch walks leaf → common root, mirroring HandlePointerExitAndEnter
- **Where:** `UguiPointer.SetHovered`, `UguiPointer.FindCommonRoot`, `UguiPointer._hoverChain`
- **Rule:** exit walks the RECORDED chain leaf-first up to (excluding) the common root; enter
  walks the new target up to (excluding) the same root. Never `ExecuteEvents.Execute` on the
  single raycast hit.
- **Why:** the game's hover feedback does not live on the object a `GraphicRaycaster` returns.
  `ExtendedButton.OnPointerEnter → OnHighlight()` sits on the BUTTON while the hit is whichever
  child Graphic is topmost (the TMP label, the plate Image, a Toggle's Background); `HoverEffect`,
  `MouseOverUIElement`, `UIHighlightTransition`, `UITooltipTarget` sit on WRAPPERS. Leaf-only
  dispatch landed on an object with no handler and the animation never ran. Poking *appeared*
  to work only because `Press` uses `ExecuteHierarchy`, and `Selectable.OnPointerDown` →
  `SetSelectedGameObject` → `OnSelect` triggers the same `Animate(effects)` as a side effect.
- **Established by:** `5ad4939` fix(interact): the laser now triggers the game's own hover animation
- **Breaks if:** reverted to `ExecuteEvents.Execute(_hovered, …)`; or the common-root stop is
  removed (beam jitter INSIDE one widget would fire handler events again — the current
  behaviour is strictly *less* thrash than leaf dispatch, not more).
- **Confidence:** high

### The exit walk uses the recorded chain, not live transforms
- **Where:** `UguiPointer._hoverChain`, `UguiPointer.SetHovered` (exit loop), `UguiPointer.Cancel`
- **Rule:** entered objects are recorded in `_hoverChain` and released through the same method.
  `Cancel` → `SetHovered(null)` is on every teardown path.
- **Why:** walking `_hovered.transform` throws when a released/closed panel was Unity-destroyed
  while hovered; the refcounts would then leak and widgets stay stuck highlighted.
- **Established by:** `5ad4939`
- **Breaks if:** the chain is dropped in favour of re-walking `target.transform.parent`.
- **Confidence:** high

### `UguiHoverTracker` keys by INSTANCE ID, never by the GameObject
- **Where:** `UguiHoverTracker.Acquire`, `UguiHoverTracker.Release`, `UguiHoverTracker.Counts`
- **Rule:** `Dictionary<int,int>` keyed on `GetInstanceID()`. `Release` is deliberately
  Unity-null tolerant (`is null`, not `== null`) so a destroyed object still balances its count.
- **Why:** `UnityEngine.Object` overrides equality with fake-null semantics — **two DESTROYED
  objects compare EQUAL**. An object-keyed dictionary corrupts silently. Instance IDs stay
  stable and unique across destruction.
- **Established by:** `5ad4939`
- **Breaks if:** "simplified" to `Dictionary<GameObject,int>` or `HashSet<GameObject>`; or the
  `is null` checks are normalised to `== null` (a destroyed object would then skip its release
  and the refcount never drains).
- **Confidence:** high

### Cross-pointer arbitration: first-in dispatches enter, last-out dispatches exit
- **Where:** `UguiHoverTracker`, called from `UguiPointer.SetHovered`
- **Rule:** up to four pointers (poke + laser × two hands) share the refcount table.
- **Why:** without it, poking a widget the laser already hovers sends a SECOND `pointerEnter`
  (double highlight tween + double hover sound), and pulling one away sends a `pointerExit`
  that un-highlights a widget the other pointer is still on.
- **Established by:** `5ad4939`
- **Breaks if:** each pointer manages its own enter/exit independently.
- **Confidence:** high

### `RemoveByRef` exists because `List.Remove` uses Unity equality
- **Where:** `UguiPointer.RemoveByRef` (used on `PointerEventData.hovered`)
- **Rule:** identity comparison, not `List<T>.Remove`.
- **Why:** same fake-null trap — `Remove` could drop the WRONG entry when a panel is torn down
  while hovered.
- **Established by:** `5ad4939`
- **Breaks if:** replaced with `data.hovered.Remove(go)`.
- **Confidence:** high

### Distinct pointer IDs per (hand × near/far)
- **Where:** `UguiPointer.LeftHandPointerId` (-101) / `RightHandPointerId` (-102) /
  `LeftHandRayPointerId` (-111) / `RightHandRayPointerId` (-112)
- **Rule:** four distinct IDs, all clear of mouse (-1..-3) and touch (0+) ranges.
- **Why:** the far ray and the fingertip coexist on the SAME hand; sharing an ID makes uGUI
  see one pointer teleporting between them.
- **Breaks if:** collapsed to one ID per hand, or into the mouse range.
- **Confidence:** high

### Nested canvases are merged into the HOST's hit-test but never registered as surfaces
- **Where:** `UguiPointer.TryRaycast` (nested loop), `UguiPointer.Beats`,
  `UguiPokeSurfaces.RegisterNested` / `NestedOf`
- **Rule:** only HOSTS live in `UguiPokeSurfaces.Surfaces`; nested canvases extend hit-testing
  only. The host raycaster's enabled state remains the modality gate (a locked UI yields no
  hits and therefore no events); a disabled nested canvas is skipped.
- **Why:** Graphics under an ENABLED nested canvas register with THAT canvas in
  `GraphicRegistry`, so the host's `GraphicRaycaster` raycasts a hollow panel (test #20 —
  the invisible/unclickable initiative track).
- **Established by:** `9ea6d0c` feat(interact): merge nested-canvas raycasters into host uGUI hit-testing
- **Breaks if:** nested canvases are added to `Surfaces` — the beam-clamp and poke-plane math
  (`RayUguiDriver.TryIntersect`, `PokeInteractor.TickCanvases`) intersect the HOST rect and
  would start fighting each other.
- **Confidence:** high

### `Beats` ordering: sorting layer, then sortingOrder, ties to the challenger
- **Where:** `UguiPointer.Beats`
- **Rule:** `challenger.sortingOrder >= incumbent.sortingOrder` — ties go to the NESTED hit.
- **Why:** a nested canvas reports its own serialized order even with `overrideSorting` off
  (verified in test #19 logs: 35/-1 while the host is 0), so the initiative track's content (40)
  must beat the host background, and the element board's `BackgroundMask` underlay (-1) must
  stay behind host content. Nested content draws inside/above the host content it overlaps.
- **Established by:** `9ea6d0c`
- **Breaks if:** the tie is flipped to `>` ("strictly better wins").
- **Confidence:** medium

### Depth-portrait picking is FAR-RAY ONLY and registry-scoped
- **Where:** `UguiPointer.TryRaycast` (`_farRay && DepthPortraitPicks.TryGet(...)`),
  `IDepthPortraitPicker`, `DepthPortraitPicks`
- **Rule:** the analytic per-portrait ray∩rect pick runs only for the laser and only for hosts
  that registered a picker. The poke path always uses the flat screen point.
- **Why:** the initiative track authors each portrait at a stepped local z; on a world-space
  host that is literal geometry, and a flat host-plane screen point projects to a different
  screen position under perspective — the `GraphicRaycaster` resolved a NEIGHBOUR. A fingertip
  is a world point, not this aim ray, so the depth path is meaningless for poke. Depth
  *normalization* was tried and only reduced the error.
- **Established by:** `e4e4237` fix(worldui): per-portrait depth-aware laser pick for the initiative track
- **Breaks if:** the `_farRay` guard is dropped, or the picker is made global instead of
  per-host.
- **Confidence:** high

### The depth pick publishes `UiHitOverride` AFTER RayUguiDriver's flat point — same-frame override wins
- **Where:** `UguiPointer.TryRaycast` (depth branch sets `ray.UiHitOverride = portraitHit`)
- **Rule:** `RayUguiDriver.Tick` sets the flat plane point, then calls `TryRaycast`, which may
  overwrite it. Order is load-bearing.
- **Why:** the beam must land on the portrait's real depth, not the flat host plane. Only the
  along-ray distance is consumed (see §1), so this is a length correction, not a re-aim.
- **Established by:** `e4e4237`
- **Breaks if:** `TryRaycast` is hoisted above the `UiHitOverride` assignment.
- **Confidence:** high

### `useDragThreshold = false`, and drags are driven every held frame
- **Where:** `UguiPointer.Press` (`data.useDragThreshold = false`), `UguiPointer.Drag`,
  `RayUguiDriver.TickPressed` (calls `Drag` BEFORE the release check)
- **Rule:** begin the drag on the first movement, no pixel threshold; `Drag` is fed the SAME
  clamped-into-rect screen point used for the raycast.
- **Why:** sliders, scrollbars and scroll-rects only move via `IDragHandler` — Down/Up/Click
  alone never budge a Slider handle. A pixel threshold on a VR laser reads as a dead control.
- **Established by:** `909c2bb` fix: laser now sends begin/drag/end so options sliders & scrollbars respond (#9)
- **Breaks if:** the threshold is restored, `Drag` is moved after the release check, or a
  different (unclamped) screen point is used for the delta.
- **Confidence:** high

### `Scroll` clears `scrollDelta` after dispatch
- **Where:** `UguiPointer.Scroll`
- **Rule:** the shared `PointerEventData`'s `scrollDelta` is zeroed after the dispatch.
- **Why:** the event data is reused; a stale scroll would leak into the next press/drag.
- **Confidence:** high

### `Release` fires click only when press and release resolve to the SAME click handler
- **Where:** `UguiPointer.Release` (`_pressedClickHandler` vs `hoveredClickHandler`)
- **Rule:** compare the resolved `IPointerClickHandler` on both ends, not the raw hovered object.
- **Why:** the raycast leaf jitters within one widget; comparing leaves would swallow legitimate
  clicks. This is the uGUI release-outside idiom.
- **Confidence:** high

### The uGUI click log line carries provenance
- **Where:** `UguiPointer._sourceTag`, `UguiPointer.Release` log, `UguiPointer.LogHover`
- **Rule:** every synthesized click/hover names `laser-L`/`poke-R`.
- **Why:** test #18 — "did the initiative portrait click reach the game's handler" must be
  answerable from the log alone. The hover log is double-throttled (per-widget dedupe +
  a hard rate cap) and **reports the swallowed count**, so the log never understates the rate.
- **Established by:** `19099db` fix(interact): initiative portrait clicks switch characters; `5ad4939` (hover log)
- **Breaks if:** the suppressed counter is dropped — a throttled log that hides its own losses
  is what made the earlier flip-war unreadable.
- **Confidence:** high

---

## 3. Poke (Hands.Interact.PokeInteractor)

### Three press modes coexist; the deliberate (v1) mode is per-canvas, not global
- **Where:** `PokeInteractor.TickCanvases`, `PokeInteractor._pressPending` / `_pressDeliberate`,
  `PokeInteractor.IsDeliberate`, `DeliberatePokeSurfaces`
- **Rule:**
  - `PokePressDepthMm == 0` → instant click on plane contact;
  - `> 0` → contact ARMS (pointerDown + light haptic), click fires at push-through depth;
  - canvas registered in `DeliberatePokeSurfaces` → contact ARMS **even at depth 0**, and the
    click fires only on the conscious WITHDRAWAL past `ReleaseDepth` in FRONT of the plane;
    sweeping through past `PressThrough` or leaving sideways cancels SILENTLY (no click, no
    cooldown charge).
- **Why:** instant-on-contact fired on accidental brushes (user #12); for irreversible decisions
  (take-damage burn choice, burn-confirm, short-rest Ja/Nein) a sweep of the hand across the
  dock must never trigger a decision (user #13b).
- **Established by:** `7bcf8b3` fix(worldui): instant poke clicks…; `dab77fb` feat(interact): push-in confirmation for flat-button pokes (PokePressDepthMm); `36f1766` fix(decision-dock): … deliberate v1 poke press for decision buttons
- **Breaks if:** the three branches are unified; or `DeliberatePokeSurfaces` is replaced by a
  global config flag (the registry says WHICH canvases; `DecisionPokeDeliberate` is only the
  live escape hatch and is read at press time).
- **Confidence:** high

### A pending DELIBERATE press keeps the PLAIN PressThrough guard; a pending depth press gets the 1.5× grace
- **Where:** `PokeInteractor.PendingPressThroughGrace`, `TickCanvases`
  (`if (_pressPending && !_pressDeliberate && ReferenceEquals(canvas, _activeCanvas))`)
- **Rule:** the grace is applied **only** to the non-deliberate pending press.
- **Why:** for depth-fire, a fast stab that overshoots `PressThrough` between two frames must
  still fire (the fire depth is evaluated on the same tick) instead of silently dropping the
  canvas. For deliberate, sweeping through past `PressThrough` is exactly the cancel gesture —
  granting it grace would let a sweep click.
- **Established by:** `dab77fb` (grace) + `36f1766` (deliberate exemption)
- **Breaks if:** the `!_pressDeliberate` term is dropped as "an odd special case".
- **Confidence:** high

### Fire depth is clamped to 0.8 × the per-canvas PressThrough
- **Where:** `PokeInteractor.DepthPressThroughHeadroom`, `TickCanvases`
- **Rule:** `fireDepth = min(configured, tuning.PressThrough * 0.8f)`.
- **Why:** the click must always land BEFORE the canvas-drop guard, or a press can be eaten in
  the window between fire depth and `PressThrough`.
- **Established by:** `dab77fb`
- **Breaks if:** the clamp is removed because "the config default is smaller anyway" — the
  config is user-editable and `SmallDialog.PressThrough` is only 30 mm.
- **Confidence:** high

### Canvas switch/loss cancels in-flight pointer state — this IS the v1 cancel path
- **Where:** `PokeInteractor.TickCanvases` (`if (!ReferenceEquals(best, _activeCanvas))`)
- **Rule:** losing the active canvas calls `_pointer.Cancel()` and clears `_canvasPressed`,
  `_pressPending`, `_pressDeliberate`.
- **Why:** this branch is how "swept through" and "left the rect sideways" resolve to a silent
  pointerUp without a click. It is not defensive cleanup.
- **Established by:** `36f1766`
- **Breaks if:** the cancel is narrowed to "canvas destroyed".
- **Confidence:** high

### Re-arm hysteresis + a 0.25 s cooldown, and a cancel costs NO cooldown charge
- **Where:** `PokeInteractor.ClickCooldownSeconds`, `_lastCanvasClick`, `_canvasPressed`,
  the `else if (bestSigned < -tuning.ReleaseDepth * scale)` re-arm branch
- **Rule:** `_lastCanvasClick` is stamped only when a click actually fires. A retract-before-depth
  cancel clears state without stamping.
- **Why:** the cooldown exists only to swallow re-entry jitter at the release boundary — far
  below any deliberate second press, so it never reads as a dwell. Charging it on cancels would
  turn a corrected mis-poke into a dead 250 ms.
- **Established by:** `7bcf8b3`
- **Breaks if:** the cooldown is stamped unconditionally on `_canvasPressed = true`.
- **Confidence:** high

### The deliberate fire path resets `_canvasPressed` at the withdrawal edge
- **Where:** `PokeInteractor.TickCanvases` (deliberate branch sets `_canvasPressed = false`)
- **Rule:** firing at the withdrawal edge means the fingertip is already past the re-arm
  hysteresis, so the press fully re-arms there.
- **Why:** otherwise the next contact cannot press again until an extra retract.
- **Confidence:** medium

### Front-side only, and press-through tolerance is per-canvas
- **Where:** `PokeInteractor.TickCanvases` (`signed = Dot(tip - t.position, t.forward)`),
  `PokeSurfaceTuning`
- **Rule:** uGUI renders facing `-forward`; the viewer side is `signed < 0`. Fingers physically
  sink through on a press, so `PressThrough` tolerance is honoured before dropping the canvas.
  `PokeSurfaceTuning.Default` (0.06/0.012/0.05) reproduces the Phase-2 constants exactly.
- **Breaks if:** the sign convention is "cleaned up", or `Default` is retuned (untuned canvases
  would all change behaviour at once).
- **Confidence:** high

### Collider pokeables use a nearest-within-range + contact-radius model with its own re-arm
- **Where:** `PokeInteractor.TickPokeables`, `FingertipRadius` (8 mm), `HoverRange` (35 mm),
  `ReleaseRange` (20 mm), `_armed`
- **Rule:** hover at ≤ `HoverRange`, fire once at ≤ `FingertipRadius`, re-arm above
  `ReleaseRange`. Changing hover target resets `_armed`.
- **Why:** hysteresis prevents a single press firing repeatedly at the boundary. `BoardClickDriver`
  mirrors `ContactDepth`/`ReleaseDepth` (8 mm / 20 mm) deliberately — see §5.
- **Confidence:** high

### Dead colliders are pruned lazily, never mid-iteration
- **Where:** `PokeInteractor.TickPokeables` / `TickCanvases` (`sawDeadCollider` / `sawDead`),
  `VRInteractables.Prune`, `UguiPokeSurfaces.Prune`
- **Rule:** the scan records that something was dead and prunes AFTER the loop.
- **Why:** the registries are indexed directly by the hot path in for-loops; mutating during
  iteration corrupts the scan.
- **Confidence:** high

### `SafeExit` try/catches `OnPokeExit`
- **Where:** `PokeInteractor.SafeExit`
- **Rule:** an exiting pokeable's callback must not abort the poke tick.
- **Why:** exit runs during teardown paths where the target is often half-destroyed; the same
  starve-the-input-pipeline lesson as `TickGuard`.
- **Confidence:** medium

---

## 4. Grab (Hands.Interact.ProximityGrabber, RayGrabDriver, VRInteractables)

### The trigger grab DEFERS to the laser — it never fights it
- **Where:** `ProximityGrabber.Tick` (`if (_hand.TriggerDown && !_hand.Ray.HasFreshUiHit)`)
- **Rule:** a proximity trigger-grab is claimed only when the ray is NOT clamped to a
  UI/interactive surface this frame. `HasFreshUiHit` is the single unified signal every
  trigger-click path already raises (RayUguiDriver, CardsDriver fan/board laser, FlatScreen,
  FigureGrabDriver).
- **Why:** the trigger is also the uGUI/laser/board "click". A laser-pluck or UI click must
  always win; a trigger pull with an empty hand falls straight through to the UI/board click
  exactly as before. When a laser pluck and a proximity highlight coincide, whichever grabs
  first sets `Held` and the other early-outs on `Held != null` — no double grab.
- **Established by:** `22f56f1` feat(cards): G3 — proximity grab uses [Cards] GrabButton (trigger default)
- **Breaks if:** proximity is made to *win* the trigger, or the check is moved after `BeginGrab`.
- **Confidence:** high

### `GrabWithGrip` is a PER-OBJECT property, not a config branch
- **Where:** `IGrabbable.GrabWithGrip`, `ProximityGrabber.Tick` (the `Highlighted.GrabWithGrip`
  branch runs BEFORE the `[Cards] GrabButton` branch), `GrabbableBehaviour.GrabWithGrip`,
  `FigureGrabbable.GrabWithGrip`
- **Rule:** grip-only grabbables (world panels/boards via `PanelGrabHandle`) always take the
  GRIP regardless of `[Cards] GrabButton`; cards and figures take the configured button.
- **Why:** the single config entry governed `ProximityGrabber` for ALL grabbables; the tester
  found grip more intuitive for moving boards while cards had to keep the Demeo trigger grab.
  Note: net472 has no default-interface-method support, so the property is implemented on each
  base — that duplication is forced, not accidental.
- **Established by:** `12f7e83` fix(interact): grip-grab boards/panels while cards keep the trigger grab
- **Breaks if:** folded back into the config; or the grip branch is reordered below the trigger
  branch.
- **Confidence:** high

### `releaseOnTriggerUp` must be symmetric between proximity grab and laser pluck
- **Where:** `ProximityGrabber._releaseOnTriggerUp`, `BeginGrab`, `ForceGrab`, `Tick`
  (`bool stillHeld = _releaseOnTriggerUp ? _hand.TriggerPressed : _hand.GripPressed`)
- **Rule:** an object grabbed with the trigger releases on trigger STATE going false — the same
  edge the laser pluck uses.
- **Why:** asymmetry leaves a card/figure stuck held forever. Reading the *state* (not the up
  edge) means a mode or hands hiccup still releases.
- **Established by:** `22f56f1`
- **Breaks if:** release is switched to `TriggerUp` (an edge that can be missed).
- **Confidence:** high

### `HealDeadHeld` re-derives hold validity every Tick instead of latching
- **Where:** `ProximityGrabber.HealDeadHeld`, called at the top of `Tick`
- **Rule:** a held object that was destroyed, disabled/re-parked (pooled), or whose
  `IGrabbableHandFilter` stopped allowing this hand is force-released with a `Warn`.
- **Why:** a stale `Held` refuses EVERY subsequent grab **and** keeps `RayInteractor.Active`
  false (laser gone) while hover haptics elsewhere keep promising a grab. This is structural:
  objects can die underneath a hold through paths that never call `OnRelease`.
- **Established by:** `bce9a63` fix(interact/cards): BoardTargeting grab policy + grab-state self-heal (bug A)
- **Breaks if:** removed as "defensive"; or the `GrabbableBehaviour.OnDisable` `Holder = null`
  companion is dropped.
- **Confidence:** high

### No grab available ⇒ no grab affordance (honest affordance rule)
- **Where:** `RayGrabDriver.Tick` (`|| !_hand.Grabber.Enabled` in the early-out),
  `ProximityGrabber.LogRefusal` / `LogNoCandidateRefusal` / the disabled-interactor branch
- **Rule:** hover tint + haptics are suppressed whenever the grabber is policy-disabled; every
  refused grab NAMES its gate at Info level, throttled 1/s per hand.
- **Why:** bug A — `BoardTargeting`'s policy carried no `Grab`, but `RayGrabDriver`'s hover tint
  and haptic never consulted `Grabber.Enabled`. The felt result was "bars flicker and vibrate
  but won't grab", and the log was silent.
- **Established by:** `bce9a63`
- **Breaks if:** the `Grabber.Enabled` term is dropped from `RayGrabDriver.Tick`, or the refusal
  diagnostics are removed as noise (they are edge-triggered and throttled).
- **Confidence:** high

### Laser grab tests the BAR collider, not the registered grab zone
- **Where:** `RayGrabDriver.Tick` (`Collider col = handle.BarCollider != null ? handle.BarCollider : entries[i].Collider`)
- **Rule:** when a `PanelGrabHandle` exposes a dedicated narrow bar collider, the laser tests
  ONLY that; the wider registered zone stays palm-only.
- **Why:** the "lost menu" incident — a floated menu sits between the user and the board, and
  ray-testing its generous grab zone made EVERY trigger aimed at the cards/board start a
  laser-carry of the menu instead. The hover beam-clamp follows the same collider so the beam
  only latches onto the visible bar.
- **Established by:** `a312cb4` fix(worldui): lost-menu incident — bar-only laser grab, off-view menu recall, DIAG throttle
- **Breaks if:** unified back to `entries[i].Collider`.
- **Confidence:** high

### Sticky highlight: 2.5 cm switch margin
- **Where:** `ProximityGrabber.SwitchMarginMeters`, `UpdateHighlight` (the early-return)
- **Rule:** a rival must be closer than the current highlight by 2.5 cm (scale 1) to steal it.
- **Why:** overlapping fan cards flapped the highlight every frame and buzzed the controller.
  2.5 cm is chosen against the ~3.5 cm pop animation plus card strip spacing, so animation
  alone cannot flip the winner.
- **Established by:** `d3ef4cc` (P6/test #8), widened P7/test #10
- **Breaks if:** retuned without re-deriving it against the pop distance.
- **Confidence:** high

### `Collider.Raycast` is used for grab bars because they are TRIGGERS on the mod layer
- **Where:** `RayGrabDriver.Tick` (`col.Raycast(ray, …)`)
- **Rule:** geometric per-collider raycast, not `Physics.Raycast`.
- **Why:** `RayInteractor` excludes the mod layer and ignores triggers BY DESIGN — grabs route
  through the `VRInteractables` registry, not physics layers. `Collider.Raycast` works on
  triggers with no layer coupling.
- **Confidence:** high

### Registration is explicit; the registries are indexed directly by the hot path
- **Where:** `VRInteractables.Pokeables` / `Grabbables` (public `List<T>` fields),
  `RegisterPokeable` / `RegisterGrabbable` (each unregisters first)
- **Rule:** register in `OnEnable`, unregister in `OnDisable`; registration is idempotent by
  target reference; interactors iterate with plain for-loops (no allocations, no physics query).
- **Why:** VR interactables must never fight the game's own physics setup / layer masks.
- **Breaks if:** converted to layer/collider auto-discovery, or to `foreach` over a
  collection that interactors mutate.
- **Confidence:** high

### `GrabbableBehaviour.OnDisable` clears `Holder`
- **Where:** `GrabbableBehaviour.OnDisable`
- **Rule:** a grabbable disabled/re-parked while held clears `Holder`; the holding hand's
  `HealDeadHeld` then calls `OnRelease`, which tolerates an already-cleared `Holder`.
- **Why:** `IsHeld` read true on a pooled card forever.
- **Established by:** `bce9a63`
- **Confidence:** high

---

## 5. Hand model, rig, curl, ghost

### Global `QualitySettings.skinWeights` is a CAP — re-assert a FourBones floor every frame
- **Where:** `HandsDriver.EnforceGlobalSkinWeights` (called from `TickRig`),
  `HandVisuals.Build` (`smr.quality = SkinQuality.Bone4`)
- **Rule:** both halves are required. Per-renderer `SkinnedMeshRenderer.quality` can only
  LOWER the global, never raise it, so the Bone4 pin alone is silently clamped.
- **Why:** at boot the game enables its lowest quality level ("Fastest", `skinWeights = 1`);
  the user's saved graphics settings only apply once the menu is up. Curling a finger then
  snapped every vertex rigidly to its single heaviest bone → extreme tearing during the intro.
  The game re-lowers it on quality-level swaps and via its own selector, hence per-frame
  re-assert (a cheap enum compare), not set-once.
- **Established by:** `5b119e0` fix(hands): pin SkinQuality.Bone4 …; `559cd99` fix(hands): … + global 4-bone skin floor (intro distortion)
- **Breaks if:** either half is deleted as redundant, or the floor is set once at init.
- **Confidence:** high

### `HandGhost.IsAttachment` checks the hand ROOT first; the wrist is a socket only when distinct
- **Where:** `HandGhost.IsAttachment`, `HandGhost.UiLayer`
- **Rule:** order is: UI-layer identity → walk up, `rig.Root` returns **false** first →
  `PalmCenter`/`GrabAnchor` return true → `Wrist` returns true only when
  `!ReferenceEquals(rig.Wrist, rig.Root)`.
- **Why:** `HandVisuals` assigns `rig.Wrist = handRoot` for the procedural hand and falls back
  to the whole prefab instance for a glove — the wrist is NOT a socket in the shipped rigs.
  Testing "is any ancestor the wrist" first matched EVERY renderer, so the ghost excluded the
  entire hand (`1 renderer(s) cloned` in the hardware log) and nothing looked different. The
  wrist HUD, which really does hang off the wrist, is excluded by its own IDENTITY (UI layer 5,
  set in `WristHud.Build`) — a property of the thing, not of where it is parented.
- **Established by:** `d72cb29` fix(hands): the ghost hand actually fades the hand (it was excluding all of it)
- **Breaks if:** the ancestor tests are reordered, or the wrist HUD is re-excluded by parentage.
- **Confidence:** high

### `GloomhavenVR/BoardLit` exposes no blend state — HasProperty-guarded writes are "true and useless"
- **Where:** `HandGhost.CanBlend`, `HandGhost.SwapToBlendableShader`, `HandGhost.MakeTransparent`,
  `HandGhost.s_swappedShader` (folded into the engage log)
- **Rule:** if a material's shader exposes neither `_Mode`/`_Surface` nor the raw
  `_SrcBlend`+`_DstBlend` pair, its blending is hard-coded and the CLONE's shader must be
  REPLACED (`Sprites/Default` → `UI/Default` → `Unlit/Transparent`), carrying base map + tint.
- **Why:** every knob in `MakeTransparent` is `HasProperty`-guarded, and the bundled opaque
  `GloomhavenVR/BoardLit` has none of them — the whole recipe silently no-opped, `SetAlpha`
  wrote an alpha the shader never blends with, and the log dutifully reported
  "1 material(s) tinted" for a write that could not possibly show. That cost a round.
  The replacement must be UNLIT: the VR void and menu scenes have no lights, so a lit shader
  renders the hand pitch black (same reason `HandVisuals.CreateHandMaterial` picks Sprites/Default).
- **Established by:** `9ba065f` fix(hands): the ghost hand's shader had no blend state, so the fade could not show
- **Breaks if:** more `HasProperty`-guarded property writes are added instead of a swap; or a
  lit shader is chosen as the swap target; or the swap-report is dropped from the log (it is
  the evidence that the fade can show at all).
- **Confidence:** high

### The ghost clones materials and assigns via `sharedMaterials`, and destroys the clones
- **Where:** `HandGhost.Engage` (`r.sharedMaterials = clones`), `HandGhost.Release`,
  `HandsDriver.TearDown` (calls `HandGhosts.Shutdown()` BEFORE destroying the hand tree),
  `HandsModule.Shutdown`
- **Rule:** never mutate a shared material; clone per RENDERER, assign through `sharedMaterials`
  (which — unlike `.materials` — does not trigger Unity's implicit instantiation), mark clones
  `HideFlags.HideAndDontSave`, and destroy them on release.
- **Why:** hand materials are shared — the procedural hand builds ONE material for ~40
  primitives, and the glove prefab's materials are the bundle ASSETS, also used by the
  mirrored self-preview and by every remote player's hands. Writing alpha into those fades
  every hand in the room, permanently (bundle assets survive the object). Cloned materials are
  assets too: Unity does not free them with the GameObject, so a style switch or rig rebuild
  under an open fan leaks one per renderer.
- **Established by:** `314ba8f` feat(hands): optional ghost hand while the card fan is open
- **Breaks if:** `.materials` is used, the teardown order in `HandsDriver.TearDown` is changed,
  or `Release` is skipped on any teardown path.
- **Confidence:** high

### Roll gate v4: parallel-transported reference up, re-anchored only away from vertical
- **Where:** `PalmGate.Tick`, `PalmGate._uref`, `PalmGate.PitchNeutralUp`,
  `VerticalAnchorDot` (0.7), `ReanchorSharpness` (8)
- **Rule:** each frame `Uref` is projected onto the plane ⊥ the current finger axis; while
  `|dot(F, up)| < 0.7` it is eased back onto the pitch-neutral world anchor; near vertical the
  transported value alone carries the reference. `_uref` is nulled on disable and on pose loss.
- **Why:** three generations of this measure failed at a reachable pose. v2's projected angle
  degenerated near world-up ("roll 97" on a pure PITCH). v3's Demeo `dot(handRight, up)` is
  pitch-proof only while the fingers stay off vertical — hand pointing UP, the right axis lies
  horizontal and a wrist twist merely SPINS it, so the dot pins near 0 and the fan can never
  open. Transport alone drifts (holonomy: closed wrist paths accumulate residual twist), hence
  the re-anchor; the re-anchor degenerates at vertical, hence the 0.7 gate.
- **Established by:** `d7ec01c` fix(cards): Demeo pitch-proof roll gate (v3); `6b5c38c` fix(cards,rig): roll gate v4 parallel-transport (pitch-invariant reveal)
- **Breaks if:** `Uref` is eased to world up unconditionally, or the transport is replaced by
  any construction with a degenerate pose.
- **Confidence:** high

### The gate measures the DEVICE frame times the SHIPPED seat — never the tuned hand
**[verified 2026-09-08 — INVERTED. The old entry said the gate measures `HandRig.Root` and that
switching to `_hand.transform.rotation` would break it. The shipped code does exactly what the
old "Breaks if" forbids, and `HandRig.Root` is the approach that was tried and rejected. An
entry whose "Breaks if" describes the current code is the worst failure mode in this registry:
it recruits a successor to revert a fix.]**

- **Where:** `PalmGate.Tick` — `Quaternion frame = _hand.transform.rotation * HandsConfig.ShippedSeatRotation(style);` where `style` is `_hand.Rig.VisualStyle` (falling back to `HandVisuals.LocalStyle()`). `HandsConfig.ShippedSeatRotation(int style)` returns `Quaternion.Euler(-Seat(DefaultSeatPitch, style), 0f, 0f)` — the **shipped** seat pitch for that hand style, never the player's tuned one.
- **Rule:** the frame keeps +Z along the fingers and +Y out of the back of the hand (v4), and is built from the **device rotation plus the shipped seat**. The per-side sign (`left +, right −`) stays load-bearing: the device grip frame is NOT mirrored between hands.
- **Why:** the gesture is a property of the CONTROLLER, not of how the hands are seated. Reading `HandRig.Root` made re-seating the hands silently re-tune the gesture — turning them to sit right on the controller moved the wrist angle at which the fan opens, **which is not something a cosmetic setting may do.** Anchoring on the shipped seat keeps "how far do I turn my wrist" identical for every player at every hand tuning, while leaving the frame exactly what v4 chose for the default seat.
- **Established by:** `6b5c38c` (v4 frame), re-anchored to the shipped seat afterwards; the source carries the whole argument at `PalmGate.Tick` and in the class doc.
- **Breaks if:** switched (back) to `HandRig.Root` or to any live, user-tuned transform — **that is the rejected approach, and it is the one the old version of this entry demanded.** Also breaks if `ShippedSeatRotation` is made to read the tuned seat "for consistency": the whole point is that it does not.
- **Confidence:** high (re-read at `49ceab21`)

**REJECTED APPROACH — `HandRig.Root`.** Kept because the old entry's REASON was not stupid, and a successor will think of it again: the user tunes the seat trims so the visual hand sits right, and it is tempting to argue the gate should agree with what they SEE. It was tried and rejected on the ground above — a cosmetic dial must not move a gesture threshold. `HandRig.Root` itself is very much alive (`HandRig.cs`, read by `VRHand`, `RayInteractor`, `AvatarMirror`, `RemoteAvatar`, `FlatScreen`, `HeldPoseReport`); it is only `PalmGate` that no longer reads it.

**Also corrected:** the old entry pointed at "§11" for the vestigial `UseDevicePalmNormal`. §11 is *Wall fade*; the vestigial section is **§15**. And `UseDevicePalmNormal` no longer exists anywhere in `src/` — see §15 and §16.

### Hysteresis cannot be inverted by a hand-edited config
- **Where:** `PalmGate.MinHysteresisDegrees` (3), `PalmGate.Tick`
  (`exit = Mathf.Min(ExitDegrees, enter - MinHysteresisDegrees)`)
- **Rule:** exit is clamped strictly below enter.
- **Why:** an inverted config would flicker the fan every frame.
- **Confidence:** high

### The busy-hand gate keeps the measure warm while forcing the gate closed
- **Where:** `PalmGate.Tick` (`if (busy) return;` placed AFTER `_uref` is updated),
  `PalmGate.IgnoreWhenHandBusy`, `_busySuppressed`
- **Rule:** while the gate hand is mid-grab, the threshold logic is skipped but the roll measure
  and its transported reference keep ticking.
- **Why:** the gate must resume with a live, un-drifted reference the moment the grab ends.
  The gate reads `_hand.Grabber.Held` on its OWN hand — the driver only forwards the toggle.
- **Established by:** `8b4abd0` feat(cards): Demeo reveal preset + busy-hand gate in PalmGate
- **Breaks if:** the early return is hoisted above the transport block, or the busy state is
  passed in by the driver.
- **Confidence:** high

### Index curl is gated by CAPACITIVE TOUCH, not by the analog trigger
- **Where:** `VRHand.TriggerTouchUsage` / `IndexTouchUsage`, `VRHand.IndexTouch`,
  `VRHand._indexTouchSupported`, `VRHand.UpdateCurlTargets`, `VRHand.UpdatePoseClassification`
- **Rule:** when a capacitive usage is delivered (latched on first delivery): trigger NOT
  touched ⇒ index straight (pointing) even at full grip; touched ⇒ index joins the fist. When
  no touch source exists: the index follows the TRIGGER fallback chain only and the grip NEVER
  closes it — pointing beats fist-completeness.
- **Why:** the round-2 grip-fist override closed ALL fingers at `gripCurl >= 0.9`, which made
  index pointing IMPOSSIBLE on runtimes whose analog trigger is dead (VDXR: `raw trig=0.00`
  through a full squeeze) — gripping the controller always curled the index.
- **Established by:** `9461a69` fix(hands+mirror): grip-fist on dead VDXR trigger…; `b5055f9` fix(hands): geometric digit skinning + DIP joints, capacitive index point
- **Breaks if:** the two branches are unified, or the `_indexTouchSupported` latch is replaced
  by a per-frame probe (runtimes withhold the usage until the controller wakes).
- **Confidence:** high

### Trigger fallback chain, and the log line naming the winning source
- **Where:** `VRHand.ReadTriggerFallbacks`, `VRHand.TriggerValueAltUsage`, `SelectUsage`,
  `VRHand._triggerSource`
- **Rule:** when `CommonUsages.trigger <= 0.001f`, probe in order: `TriggerValue` alt float →
  `triggerButton` → OpenXR `Select`; first non-zero wins (booleans map to 1.0). Log once on
  source change.
- **Why:** Quest 3 via VDXR delivers a permanently-zero analog trigger while digital states
  still fire. The log is how a hardware report shows WHICH source feeds the index finger.
- **Established by:** `9461a69`
- **Confidence:** high

### `CurlInputFullAt` remaps CURL ONLY — press hysteresis and pose stay raw
- **Where:** `VRHand.RemapCurlInput`, `VRHand.UpdateCurlTargets`, `VRHand.ApplyAnalog`
  (`PressThreshold` 0.75 / `ReleaseThreshold` 0.55 use raw values)
- **Rule:** `TriggerValue`/`GripValue` stay raw for the 0.75/0.55 hysteresis and pose
  classification; only the curl targets are remapped.
- **Why:** VDXR plateaus the analog grip below 1.0 at a comfortable full squeeze, so the
  unremapped value never reached full curl ("mostly no fist" on every style). Remapping the
  press thresholds too would change every grab/click threshold as a side effect.
- **Established by:** `726bdac` fix(hands): full-fist runtime chain — grip remap, thumb close, tunable curl angles, TestFist + fist diagnostics
- **Breaks if:** the remap is applied at `ApplyAnalog` instead of at the curl targets.
- **Confidence:** high

### `ConsumeExternalDrift` is the "another writer moves our bones" detector
- **Where:** `FingerCurler._lastWritten` / `_hasWritten` / `_maxExternalDriftDeg`,
  `FingerCurler.ConsumeExternalDrift`, `VRHand.TickFistDiagnostics`
- **Rule:** each Tick compares the live joint rotations against what THIS curler wrote last
  frame, before overwriting them.
- **Why:** the smoking gun for "curl applied but the rendered hand does not close" (an Animator
  pass or other script running after our Update). The FIST log samples it after
  `FistLogSettleFrames` (24 ≈ 0.3 s at 72 Hz) so the exponential smoothing has converged.
- **Established by:** `726bdac`
- **Breaks if:** removed as debug-only — it is the designed evidence for the next fist regression.
- **Confidence:** high

### Glove pinky counter-abduction is a RUNTIME fix with a documented impossibility proof
- **Where:** `FingerCurler.DefaultGlovePinkyCounterAbductionDeg` (14°), `FingerCurler._pinkySplaySign`,
  `FingerCurler.Tick` (`rootRz` applied on the ROOT joint's local Z, coupled to curl)
- **Rule:** glove style only; sign flips per hand; applied as the Z component of
  `Quaternion.Euler(x, 0, z)` — Unity's ZXY order applies Z BEFORE the X flexion.
- **Why:** the glove pinky MESH tube leans ~18° outward in XZ while its bone chain is straight,
  so a full local-X curl leaves the curled pinky visibly splayed. A Blender bone-ROLL fix
  **cannot** correct this: roll only spins the flexion axis within the plane perpendicular to
  the bone, and the desired mesh-plane axis's perpendicular component IS the existing +X.
- **Established by:** `8734eea` fix(hands): revert glove to accepted pre-refit rig + runtime pinky counter-abduction; repack bundle with REGENERATED prefabs (stale-prefab root cause)
- **Breaks if:** "moved into the asset" as a bone roll; or the euler order assumption is
  changed by composing quaternions differently.
- **Confidence:** high

### `StyleCurlScale` is all-1.0 and is a documented tuning point, not dead code
- **Where:** `FingerCurler.StyleCurlScale`
- **Rule:** the array stays, with its history comment.
- **Why:** the 0.72/0.85 clamps were added for the first styled builds, then removed once the
  round-3 adaptive digit caps fixed the underlying weights. Deleting the array deletes the
  record of why full range is now correct, and the hook for a future style that over-closes.
- **Established by:** `f31264f` fix(hands): full fist on Plate/Arcane; `dc14bda` fix(hands): per-style scale/seat config
- **Confidence:** medium

### Per-style scale is applied to the hand subtree and counter-scaled at the SOCKETS
- **Where:** `HandVisuals.ApplyStyleScale`, `HandVisuals.CreateSocket`, `HandVisuals.NormalizeSocket`,
  `VRHand.SyncVisualOffset` (`_appliedStyleScale`)
- **Rule:** `handRoot.localScale = scale`, then each of Wrist/PalmCenter/GrabAnchor sockets is
  numerically normalized by `referenceScale / parent.lossyScale`. Sockets are created BEFORE
  the scale is applied.
- **Why:** everything the rest of the mod parents INTO the hand (card fan under PalmCenter,
  grabbed objects under GrabAnchor, wrist HUD under Wrist) must keep its own world size. The
  compensation is numeric so it is exact for any hierarchy the anchors ended up in (FBX bone,
  procedural child, or handRoot itself).
- **Established by:** `dc14bda`
- **Breaks if:** the socket layer is removed as an extra indirection, or the order is swapped.
- **Confidence:** high

### `VRHand.SyncVisualOffset` re-checks by float compare every frame — live tuning is the point
- **Where:** `VRHand.SyncVisualOffset`, `_appliedGripPitch` / `_appliedLateralOffset` /
  `_appliedVerticalOffset` / `_appliedForwardOffset` / `_appliedStyleScale`
- **Rule:** all five are re-read per frame and applied only on change; the style key is
  `Rig.VisualStyle` (what was ACTUALLY built), not the configured style.
- **Why:** an old bundle degrades Plate/Arcane to Glove; keying on the configured style would
  shrink a degraded glove by the Plate scale. Pitch sign is inverted on assignment
  (`Euler(-pitch, 0, 0)`) because Unity pitches down with POSITIVE X.
- **Established by:** `8a10b22` fix(hands): make the full visual-hand seat offset live-tunable; `dc14bda`
- **Breaks if:** cached at init, or keyed on `Plugin.HandStyle`.
- **Confidence:** high

### The procedural hand's material is UNLIT, and its primitives have no colliders
- **Where:** `HandVisuals.CreateHandMaterial`, `HandVisuals.CreatePrimitivePart`
  (`Object.Destroy(go.GetComponent<Collider>())`)
- **Rule:** `Sprites/Default` → `UI/Default` → `Hidden/InternalErrorShader`; every primitive's
  collider is destroyed at creation.
- **Why:** the VR void and menu scenes have NO lights, so a lit shader renders the hand pitch
  black (test #7). Interactors are registry-driven — hand visuals must not collide with anything.
- **Established by:** `7d9a338` / `54a4bcf`
- **Confidence:** high

### The rig contract is always complete, even with an unmapped art asset
- **Where:** `HandVisuals.FillMissingAnchors`, `HandRig.IsComplete`
- **Rule:** any transform the prefab did not provide is synthesized at the procedural default
  position (including an invisible joint chain per finger).
- **Why:** the curler and every interactor dereference these unconditionally; a partially
  mapped bundle must degrade, not NRE.
- **Confidence:** high

### The palm anchor is rotated 180° about Z
- **Where:** `HandVisuals.FillMissingAnchors` (`palm.localRotation = Quaternion.Euler(0,0,180)`),
  `HandRig.PalmNormal`
- **Rule:** +Y of the hand frame is the BACK of the hand, so the palm anchor is flipped to make
  its +Y the palm normal.
- **Why:** `PalmNormal`, the fan mount and `GrabAnchor`'s +0.02 Y offset all assume it.
- **Confidence:** high

---

## 6. Frame ordering constraints (invisible to the compiled-form guard)

> These survive a byte-identical decompile and are exactly what a "tidy the Update method"
> refactor destroys. Treat any reordering as Tier 3.

### `VRHand.Update`: Poke → Ray → RayUgui → RayGrab → Grabber → PalmGate
- **Where:** `VRHand.Update`
- **Rule:** RayUgui ticks immediately after Ray (it consumes THIS frame's pick). RayGrab ticks
  AFTER RayUgui (a UI click on a window must win over dragging its bar) and BEFORE Grabber (a
  laser-carry it starts via `ForceGrab` sets `Held`, so the proximity trigger-grab early-outs
  that frame). PalmGate last (it reads `Grabber.Held`).
- **Why:** each adjacency encodes an arbitration decision; the doc comment in `Update` states
  each one.
- **Established by:** `fe8e2d2` feat: drag world windows at a distance with the hand laser (#8); `22f56f1`
- **Breaks if:** reordered, or any step is moved into `LateUpdate`.
- **Confidence:** high

### Device read → velocity → pose classification → curl targets → curler.Tick → interactors
- **Where:** `VRHand.Update`
- **Rule:** interactors must see the fresh pose and the already-applied curl.
- **Why:** `PokeInteractor` reads `Rig.IndexTip.position`; `PalmGate` reads `Rig.Root.rotation`;
  both are written by the curler / the transform update earlier in the same method.
- **Confidence:** high

### `BoardDriver.Update`: SyncRayMask → CameraArrival → Click → Aoe → Targeting
- **Where:** `BoardDriver.Update`
- **Rule:** exactly this order, each wrapped in `TickGuard.Run`. The whole driver runs in
  `Update` — i.e. BEFORE the game's `Controller.LateUpdate` consumes the pending click.
- **Why:** the ray mask must be current before any pick; the camera-arrival unpause must land
  before `WorldspaceStarHexDisplay.Update` gates on `!TimeManager.IsPaused`; the click must be
  pending before the game's LateUpdate.
- **Established by:** `16f32ee` fix: promote shared Core.TickGuard, isolate+attribute driver tick sequences
- **Breaks if:** moved to LateUpdate, or the steps are reordered "alphabetically".
- **Confidence:** high

### `FigureGrabDriver.LateUpdate` runs AFTER the Animator
- **Where:** `FigureGrabDriver.LateUpdate` → `HeldFigures.PinAnimatedRoots`,
  `NetHeldFigures.PinAnimatedRoots`, `FigureRingSuppressor.Tick`
- **Rule:** these three MUST be in `LateUpdate`, never `Update`.
- **Why:** the suppressed `ActorBehaviour.ApplyMotion` is what normally re-zeros the animated
  mesh each LateUpdate; a figure grabbed mid-walk would otherwise animate straight out of the
  hand. The ring suppression must land after the game's Update writes and before render.
- **Established by:** `436e6d7` fix(figuregrab): hand-relative fixed hold + held mini renders in front + no anim drift (#3/#2/#6)
- **Breaks if:** merged into `Update` for symmetry.
- **Confidence:** high

### `WallSegmentFade.FadeDriver.LateUpdate`
- **Where:** `WallSegmentFade.FadeDriver.LateUpdate` (guarded, logs once)
- **Rule:** LateUpdate on purpose — the occlusion generator's renderer lists and the head pose
  are final only then.
- **Confidence:** high

### `MixedReality.Tick` runs AFTER `VRRigDriver.TickHeadClearColor` and the camera-policy sweep
- **Where:** `MixedReality.Tick` (called from `VRRigDriver.Update`), `SkyBackdrop.Tick`
- **Rule:** MR owns the head clear WHILE ON, so it must write last in the frame; `SkyBackdrop`
  stands down first when MR takes the sphere (`SkyBackdrop.Tick(mrHidingSky: true)` restores
  the renderer/material before MR's `HideSkyGeometry` disables a clean renderer).
- **Why:** otherwise `TickHeadClearColor` re-pins `VoidColor` over the chroma key, or MR and
  SkyBackdrop fight over the same sphere material.
- **Established by:** `5c881e7` feat(core): mixed reality chroma-key mode; `033d5b5` fix(mr): mixed reality hides the sky/background GEOMETRY, not just the clear
- **Breaks if:** the two Tick calls are reordered or merged.
- **Confidence:** high

---

## 7. Board picking and click commit

### All board picking funnels through the game's single choke point
- **Where:** `Board.Patches.MF_FindInteractableAtMousePosition_Patch.Prefix`
- **Rule:** the prefix substitutes the VR ray and mirrors the original's
  `GetComponentInParent<CInteractable>()` resolution, using the CALLER's mask
  (`Controller.m_ActiveSelectionRaycastLayer` vs `WorldspaceStarHexDisplay.m_HexSelectionRaycastLayer`).
  It returns **true** (vanilla) whenever `BoardPick` is inactive.
- **Why:** every hover, targeting and click pick in a scenario goes through this one method, so
  patching it makes hexes, actors, doors, chests and loot resolve identically to a mouse pick
  with no per-feature work.
- **Breaks if:** the mask parameter is replaced by a stored mask (the two callers pass
  different masks), or the fall-through is removed (mouse + virtual mouse must stay vanilla).
- **Confidence:** high

### `BoardPick` is frame-memoized and computed lazily
- **Where:** `BoardPick.EnsureFresh` / `_frame` / `Compute`, every public accessor
- **Rule:** one compute per frame, on first access; all queries go through `EnsureFresh`.
- **Why:** the same pick is read by the MF prefix, the CursorPosition prefix, the
  IsPointerOverUI prefix, `BoardClickDriver`, `TargetingUx`, `HexHoverClear`, `BoardPing` and
  the placement diagnostics — several of them inside game code, at different points in the
  frame. Divergent picks between hover-gating and click-routing is the class of bug §7's
  IsPointerOverUI entry fixes.
- **Breaks if:** made eager in a driver Update (the game's LateUpdate consumers would then read
  a stale pick), or an accessor bypasses `EnsureFresh`.
- **Confidence:** high

### `InScenario` and `Active` are DIFFERENT predicates and both are needed
- **Where:** `BoardPick.InScenario`, `BoardPick.Active`, `BoardPick.Compute`
- **Rule:** `InScenario` = past the mode gate + a live `Controller`, regardless of whether any
  hand produced a pick. `Active` = a pick source exists this frame.
- **Why:** `HexHoverClear` must clear a stale star even when `source == None` (ray untracked /
  mode policy off) — gating it on `Active` silently skipped exactly that case, and the game's
  fallback mouse position could keep a star lit.
- **Established by:** `294cbf8` fix(board): grab-info panel, stale hex star, robust figure highlight
- **Breaks if:** the two are merged.
- **Confidence:** high

### Near pick beats far pick, and the CLOSER hand wins the near pick
- **Where:** `BoardPick.Compute` (near before far), `BoardPick.TryNearPick`
  (`if (_source == PickSource.Near && surface >= _nearSurfaceDistance) return;`)
- **Rule:** both hands are tried for near; the smaller fingertip-to-surface distance wins;
  far is only attempted when no near pick exists. Near-touch follows the Poke interactor's
  mode policy (`hand.Poke.Enabled`), not the ray's.
- **Confidence:** high

### The near pick lifts its ray origin 3 cm above the fingertip
- **Where:** `BoardPick.NearOriginLift`, `TryNearPick` (`surface = hit.distance - lift`)
- **Rule:** origin is lifted, and the lift is subtracted back out to yield a SIGNED
  fingertip-to-surface distance (negative = pressed in).
- **Why:** a finger already touching (or inside) the surface would otherwise miss it entirely.
- **Confidence:** high

### The far pick re-raycasts authoritatively; it does not trust `RayInteractor`'s hit
- **Where:** `BoardPick.TryFarPick`, `BoardPick.FarMaxDistance` (1000f)
- **Rule:** re-raycast on the game's selection mask at the game's own 1000f range.
- **Why:** `RayInteractor` uses its own visual range (20 m × scale) and its own `Mask`, and
  applies fan occlusion. The game's pick semantics must match the mouse's exactly.
- **Breaks if:** "optimised" to reuse `pick.HitCollider`.
- **Confidence:** high

### A missed pick projects the cursor far OFF-SCREEN, not to zero
- **Where:** `BoardPick.OffscreenCursor` (-4096,-4096), `TryGetCursorScreenPoint`
- **Rule:** when VR owns the cursor but points at nothing, return true with the off-screen point.
- **Why:** downstream screen-ray consumers (`HoverRegisterer`) must MISS. Returning (0,0) is a
  real screen position at the corner; returning false hands control back to the mouse.
- **Confidence:** high

### Hex-centre snapping affects the GAME cursor only, never the visible reticle
- **Where:** `BoardPick.ResolveCursorWorld`, `BoardPick.TryGetCursorWorld`; the removed
  `BoardDriver.SyncReticleSnap` / `RayInteractor.ReticleOverride`
- **Rule:** `[Board] SnapToHexCenter` snaps the projected cursor; the beam and dot stay on the
  straight aim ray. The snapped hex is communicated by the game's own hex hover highlight.
- **Why:** moving the dot off the aim line visibly re-aims the beam (test #14 item 2) — the
  same defect class as the phantom plane.
- **Established by:** `fe95b06`
- **Breaks if:** a reticle override is reintroduced in any form.
- **Confidence:** high

### Hex centre comes from the tile GameObject, not from the analytic converter
- **Where:** `BoardPick.ResolveCursorWorld` (`tile.m_ClientTile.m_GameObject.transform.position`)
- **Rule:** read the world position off the tile object.
- **Why:** `MF.ArrayIndexToCartesianCoord` yields positive-map-space coords, NOT world space
  (BOARD-INPUT §2). The game itself reads world positions from the tile GameObject.
- **Confidence:** high

### `UIManager.IsPointerOverUI` must report VR truth while the VR pick owns the pointer
- **Where:** `Board.Patches.UIManager_IsPointerOverUI_Patch.Prefix`
- **Rule:** while `BoardPick.Active`, over-UI = `hand.Ray.HasFreshUiHit || hand.Poke.HoveredUi != null`
  — the SAME predicate `BoardClickDriver.TickFar` uses to route trigger clicks. Vanilla answer
  otherwise (so flat-screen/2D flows are untouched).
- **Why:** hero-placement's second click was dead. `WorldspaceStarHexDisplay.Interactable()`
  bails FIRST on `UIManager.IsPointerOverUI`, which on PC is `EventSystem.IsPointerOverGameObject()`
  for the mouse pointer — whose position is the PARKED virtual/hardware mouse, not our patched
  `CursorPosition`. Its `RaycastAll` hits our world-space host canvases through the head camera,
  and with the pre-fit giant planes (Panel_InitiativeTrack ~45×25 m) virtually ANY parked pixel
  was "over UI", so `Interactable()` returned null every frame and `s_PlacementTile` stayed null.
  The character click has no such gate — exactly the observed asymmetry.
- **Established by:** `96d8351` fix(board): hero-placement second click — VR truth for UIManager.IsPointerOverUI
- **Breaks if:** relied on the canvas content-fit alone (that removes the *giant plane* symptom,
  not the parked-mouse divergence); or the predicate is allowed to drift from
  `BoardClickDriver`'s — hover gating and click routing must never disagree.
- **Confidence:** high

### The click is injected by OR-ing into `Controller.CommonLoop`'s outputs, with verbatim double-click bookkeeping
- **Where:** `Controller_CommonLoop_Patch.Postfix`, `BoardClickDriver.ConsumePendingClick`
- **Rule:** the postfix sets `s_StartedButtonDownInGUI = false`, `s_SingleClicked = true`,
  replicates CommonLoop's own 0.3 s `m_DoubleClickStart` branch verbatim, and reproduces its
  tail (`__result = !isPaused || !TimeManager.IsPaused`). It always CONSUMES the pending click,
  and yields to a real mouse click that already fired (`if (__result) return;`).
- **Why:** InControl's polled `WasPressed` cannot observe a synthetic press
  (`GHControls.SimulateOnPress` only fires event subscribers), and calling
  `ShowNormalInterface` / `TileBehaviour.s_Callback` directly would SKIP LateUpdate's gating
  (InteractabilityManager tile gating, `ThisPlayerHasTurnControl`, ping handling) and the
  double-click bookkeeping. This way the vanilla LateUpdate does the entire dispatch through
  our patched `MF.FindInteractableAtMousePosition`. One patch, zero game logic bypassed.
- **Breaks if:** the double-click block is "simplified" (rapid presses lose double-click
  semantics), the always-consume is made conditional (a click leaks into a later frame), or
  `s_Callback` is invoked directly.
- **Confidence:** high

### The pending click self-expires at the START of `BoardClickDriver.Tick`
- **Where:** `BoardClickDriver.Tick` (`_pending = false;` first line)
- **Rule:** anything the game did not consume last frame is dropped before recomputing.
- **Why:** a click can never fire against a stale pick.
- **Confidence:** high

### Near click is suppressed while the fingertip is on a pokeable or a canvas
- **Where:** `BoardClickDriver.TickNear` (`hand.Poke.Hovered == null && hand.Poke.HoveredUi == null`)
- **Rule:** the Poke interactor owns those targets.
- **Why:** otherwise one fingertip press fires both a uGUI click and a board click.
- **Confidence:** high

### Far click requires `Held == null`, `Poke.HoveredUi == null` AND `!Ray.HasFreshUiHit`
- **Where:** `BoardClickDriver.TickFar`
- **Rule:** all three.
- **Why:** each covers a different consumer of the same trigger (a held grabbable, a fingertip
  on UI, a beam clamped to a UI/card/figure surface).
- **Confidence:** high

### Placement is armed at CLICK time, under the game's own predicates
- **Where:** `BoardClickDriver.ArmPlacementTile`
- **Rule:** only inside `WaitingForCardSelection` + `CharacterPlacement`; only when the clicked
  tile passes all three of the game's own predicates (`display.s_PlacementStars.ContainsKey`,
  `Scenario.FindActorAt(...) == null`, `InitiativeTrack.SelectedActor() != null`).
- **Why:** `HighlightSelectedPlacementHex` arms `Waypoint.s_PlacementTile` only on a pointed-at
  tile CHANGE, clears it first, and early-outs on any hover hiccup — with hand jitter between
  adjacent hexes the armed tile flip-flopped between null and a tile at frame rate, and
  `Choreographer.TileHandler`'s placement branch rejected most clicks. The one successful click
  was a lucky frame. `TileHandler` still performs every placement validation itself, so no rule
  is bypassed.
- **Established by:** `2253b0c` fix(board): arm the placement tile at click time under the game's own predicates
- **Breaks if:** any of the three predicates is dropped ("we already know it's a star"), or the
  fix is replaced by hover smoothing (that only widens the lucky-frame window).
- **Confidence:** high

### `CameraArrivalGuard` completes camera-follow transitions through the game's OWN arrival path
- **Where:** `CameraArrivalGuard.Tick` (calls `follow.OnArrivedToPoint()`), ticked from `BoardDriver`
- **Rule:** poll (not a patch on `SetPoint`), gated on `VRSession.IsRunning`, deliberately NOT
  phase-gated, and it must call `OnArrivedToPoint()` rather than unpausing by hand.
- **Why:** `SetPoint(pauseDuringTransition: true)` pauses the clock and unpauses ONLY in
  `OnArrivedToPoint`, whose single call site is the focal lerp inside `CameraController.LateUpdate`
  — which the Rig module prefix-skips entirely while VR runs. So the first pausing `SmartFocus`
  froze `TimeManager` for the whole session, and `WorldspaceStarHexDisplay.Update` requires
  `!TimeManager.IsPaused` before it may hover-arm the placement tile. Clicks kept working
  (`Controller.LateUpdate` uses `CommonLoop(isPaused: false)`), which is why the symptom read as
  "second click dead" rather than "game frozen". Polling covers every caller, has no Mono inline
  risk, and recovers a pause left stuck from before a hot reload.
- **Established by:** `a6e2739` fix(board): unpause camera-follow transitions the VR-parked camera never finishes
- **Breaks if:** replaced by a direct `TimeManager.UnpauseTime()` (unbalanced pause refcount,
  follow flag left set), phase-gated to placement (a leaked pause freezes the 3D clock in every
  phase), or a patch on `SetPoint` (misses callers, inline risk).
- **Confidence:** high

### AoE reads ONLY the primary hand's stick X, only in BoardTargeting, and mirrors the game's gates
- **Where:** `AoeControl.Tick`, `AoeControl.CanRotate`, `AoeControl.RefreshStars`,
  `AoeControl.RearmThreshold` (0.3), repeat clamped to ≥ 0.3 s
- **Rule:** the repeat interval is clamped to ≥ 0.3 s; melee AoE (`AbilityRange <= 1`) is
  deliberately NOT handled; after rotating, the same redraw the game's Update would perform is
  called, matched on `CurrentAbilityDisplayType`.
- **Why:** `RotateAOEClockwise` latches the turn direction for 0.3 s
  (`m_LastRecievedRotateDirection`) — a shorter repeat makes direction reversals ineffective.
  The keyboard path only redraws because its return value triggers `DisplayAOEStars()` in Update.
  Melee facing follows the hovered tile (`RotateAOEWithMouse`), which the picking patches
  already drive. The stick contention rule (AoE = X in BoardTargeting; SnapTurn = X elsewhere;
  `RayUguiDriver.TickStickScroll` = Y only while hovering a ScrollRect; world grab =
  `primary2DAxisClick`) means no frame has two consumers of the same axis.
- **Breaks if:** the 0.3 s clamp is removed, melee is "unified", or the redraw is dropped.
- **Confidence:** high

### `TargetingUx` suppresses `ActorStatPanel.DoShow` in EVERY phase except while a figure is held
- **Where:** `TargetingUx.SuppressHoverActorInfo`, `TargetingUx.RestoreActorStatPanel`,
  `TargetingUx._statPanelSuppressed`, `TargetingUx.Reset`
- **Rule:** `suppress = display != null && !levelEditor && !figureHeld`. There is NO
  `TargetSelection` exemption. `DoShow` is only ever RAISED back if we were the one who lowered it.
- **Why:** in VR merely POINTING the laser at a miniature popped its stat panel
  (`DisplayCursorHoverStar → ShowActorStatPanelForTile → ActorStatPanel.Show`, the only
  scenario-play caller). The earlier `TargetSelection` exemption let it pop during ability/attack
  target selection and decision prompts (user #10). `figureHeld` is load-bearing in the other
  direction: the grab flow docks the SAME panel, and `Show()` early-outs while `DoShow == false`,
  so suppressing during a hold would kill the grab-info panel.
- **Established by:** `488f43e` / `447e00f` fix(board): stop laser hover from popping the actor stat panel (#2); `c10a67f` fix(board): suppress laser-hover figure info panel in ALL phases (user #10); `294cbf8` (the `figureHeld` term)
- **Breaks if:** the `TargetSelection` exemption is re-added "so target stats stay visible", the
  `figureHeld` term is dropped, or `DoShow` is raised unconditionally (clobbers other owners
  such as `FullCardHandViewer`).
- **Confidence:** high

### `HexHoverClear` is a POSTFIX on `WorldspaceStarHexDisplay.Update`
- **Where:** `Board.Patches.HexHoverClear.Postfix`, `HexHoverClear.PickIsOnHex`,
  `HexHoverClear.HideStaleTooltips`
- **Rule:** postfix (so it runs after the tail re-activation and gets the last word); gated on
  `BoardPick.InScenario`; "on a hex" is judged the same way the game does
  (`TileBehaviour` with a live `m_ClientTile`).
- **Why:** `DisplayCursorHoverStar`'s null branch clears the tile ref, stat panel and outlines
  but NEVER deactivates `s_CursorHighlightedStar`, and the very tail of `Update` unconditionally
  re-activates it every frame. With a mouse this is masked (the pointer is almost always over
  some tile); in VR the laser routinely leaves the board. Same family: the hover info hint
  (`UITextInfoPanel` / `UIPropInfoPanel`) is hidden only inside `ShowTooltipForTile`, which
  needs a NEW tile hover — and grabbing a figure turns the ray off entirely, so no tile is ever
  hovered again.
- **Established by:** `0075f80` fix(board): clear stale cursor-hover hex highlight when VR laser hits no hex; `4ceaba0` fix(board): stale hover hint cleared + figure glide-back on release
- **Breaks if:** converted to a prefix or to a patch on `DisplayCursorHoverStar` (the tail undoes
  it); or the scope widens beyond `s_CursorHighlightedStar` (placement/attack/ability/move star
  dictionaries are separate and must not be touched).
- **Confidence:** high

### The tooltip hide is doubly self-no-op'd
- **Where:** `HexHoverClear.HideStaleTooltips`
- **Rule:** `UITextInfoPanel.Hide()` only while its `UIWindow.IsVisible`; `UIPropInfoPanel` uses
  the game's typed `Hide(EPropType.QuestItem)`.
- **Why:** skips panels the game temp-hid (card viewer open, gamepad tooltip toggle), avoids
  per-frame Hide spam, and leaves trap/hazard/difficult-terrain tooltips to their own
  `IHoverable` exit lifecycle (`HoverRegisterer` fires `OnCursorExit` on a missed raycast, so
  those cannot go stale this way).
- **Confidence:** high

### The action-phase select guard patches the HUMAN CLICK seam only
- **Where:** `Board.Patches.InitiativeTrackPlayerAvatar_OnClick_Guard.Prefix`
- **Rule:** patch `InitiativeTrackPlayerAvatar.OnClick` (the player override), not
  `InitiativeTrack.Select`. On a non-current locally-controlled player during the action phase,
  play the game's own invalid-click SFX and return false.
- **Why:** during ActionSelection the docked cards belong to `Choreographer.CurrentActor`, and
  the game refuses every half whose owner is not that actor. Laser-clicking another of your own
  characters re-pointed the SELECTED actor and re-docked unclickable cards — a deadlock.
  Patching `Select` would also catch the game's PROGRAMMATIC selects (round-start turn select,
  `CardsHandTabs` character-tab switch, initiative-adjustment select) and the mod's own
  take-damage drive (`CardsGameApi.SelectActor`), breaking last round's attacked-actor selection.
  Enemy avatars run the un-overridden base, so this patch never sees an enemy click.
- **Established by:** `04ce1dd` fix(cards): reject non-current player select during action phase (deadlock guard)
- **Breaks if:** moved to `InitiativeTrack.Select`, or the "locally controlled" filter is dropped.
- **Confidence:** high

### Board ping uses the DOMINANT hand's `PrimaryDown`, and reflection-guards the game call
- **Where:** `BoardPing.Tick`, `BoardPing.EnsureResolved`, `BoardPing.Cooldown` (0.2 s)
- **Rule:** dominant `primaryButton` down-edge; single-player `PingManager.Ping3DElementSinglePlayer`
  resolved by reflection once, degrading to a warned no-op.
- **Why:** the pause-menu "X" is bound to the NON-dominant hand, so the dominant A is free — no
  reassignment needed. The single-player entry is used deliberately (the game/PingManager path
  already carries a shown ping to teammates) so the mod takes no Bolt/NetworkPlayer dependency.
- **Established by:** `850bc1e` / `65f150a` feat(board): ping hex under laser on dominant-hand A button
- **Breaks if:** the multiplayer overload is called directly, or the button is moved to the
  non-dominant hand.
- **Confidence:** high

### `SelectionReadyHighlighter` uses the game's own readiness predicate and the MP ownership guard
- **Where:** `SelectionReadyHighlighter.Tick` (`IsUnderControlOrSingle()` +
  `!IsCardSelectionReady()`), driven from `LateUpdate` under `TickGuard`, pulsing on
  `Time.unscaledTime`
- **Rule:** authoritative "done" is `CPlayerActorExtensions.IsCardSelectionReady` — the same
  per-actor test the game uses for its round-ready button. Unscaled time for the pulse.
- **Why:** the predicate already folds in the two-cards / long-rest / short-rest rules and is
  itself local-only, so a remote actor always reports ready and is never revealed. The pulse
  must keep breathing while the game is time-paused during a selection camera move.
- **Established by:** `1375bf3` feat(selection): move pending-selection cue from board figures to initiative bar
- **Breaks if:** the predicate is reimplemented, or `Time.time` is used.
- **Confidence:** high

---

## 8. Figure grab

### The held rotation is a FIXED CONSTANT anchor-LOCAL rotation, applied at grab
- **Where:** `FigureGrabbable.ApplyHeldPose`, `FigureGrabConfig.HeldUprightRotation(side)`
- **Rule:** not derived from world up, the head, the figure's board rotation, or the
  grab-moment anchor orientation; and NOT re-derived per frame.
- **Why:** two rounds pulled in opposite directions and the invariant that survives both is:
  *the grab approach angle must not set the resting hold, but the wrist must still carry it.*
  v1 baked a rotation under the wrist anchor → the mini rode the wrist and its pose equalled
  the approach angle. v2 re-derived every frame as a WORLD rotation → hand-independent, so
  turning the hand no longer turned the mini (wrong). v3 bakes the grab-angle-independent base
  once into `localRotation` under the hand anchor.
- **Established by:** `477ea17` → `2277b54` → `a5c7f92` (read all three together)
- **Breaks if:** either earlier variant is reinstated alone.
- **Confidence:** high

### Left hand gets the MIRROR of the tuned right-hand pose
- **Where:** `FigureGrabConfig.HeldOffsetFor(side)`, `FigureGrabConfig.HeldUprightRotation(side)`
- **Rule:** lateral offset and yaw/roll flip sign; forward/up/tilt unchanged.
- **Why:** the user tunes once. Legacy (non-upright) mode is tilt-only and mirror-invariant.
- **Established by:** `f338d5c` feat(figuregrab): mirror held pose for left hand; offset-anchor grab selection
- **Confidence:** high

### `_heldBaseScale` is captured at grab so live-tuning never compounds
- **Where:** `FigureGrabbable.OnGrab` (`t.SetParent(anchor, worldPositionStays: true)` then
  `_heldBaseScale = t.localScale`), `ApplyHeldPose` (`t.localScale = _heldBaseScale * ActiveHeldScale`),
  `FigureGrabbable.Live` + `ReapplyAll`
- **Rule:** re-parent with `worldPositionStays: true` (no scale pop entering the hand), snapshot
  the resulting anchor-local scale as the base, and always re-derive from that base.
- **Why:** `ApplyHeldPose` doubles as the live-tune path (a debug-menu stepper writes a config
  entry → `ReapplyAll`); multiplying the CURRENT scale would compound on every stepper tick.
- **Confidence:** high

### The release GLIDE keeps the actor in `HeldFigures` until arrival
- **Where:** `FigureGrabbable.TryBeginGlide`, `TickGlide`, `FinishGlide`,
  `FigureGrabbable.Gliding`, `FigureGrabDriver.Update` (`TickGuard.Run("FigureGrab.Glide", …)`
  placed BEFORE the `GrabFigures` config gate)
- **Rule:** the hand is freed immediately (stat panel undocked, `_holder` cleared → re-grab
  works) but the actor stays in `HeldFigures` until the glide lands. The glide's END STATE is
  the captured original local TRS — bit-identical to the old instant restore.
- **Why:** staying in the set is what (a) keeps the game's transform writers suppressed,
  (b) keeps the home ghost alive (`FigureGhosts` reconciles off the held-sets), and (c) keeps
  the net send streaming the gliding pose so peers watch the same glide. Ticking before the
  config gate means a glide started just before `GrabFigures` was toggled off still lands.
- **Established by:** `4ceaba0` / `bb3502c` (glide-back on release)
- **Breaks if:** `HeldFigures.Remove` is moved to `TryBeginGlide`, or the glide tick is moved
  below the config gate.
- **Confidence:** high

### Re-grab during a glide finishes the glide FIRST
- **Where:** `FigureGrabbable.OnGrab` (`if (_glideActive) FinishGlide(root.transform);`)
- **Rule:** snap to the home local pose, then capture `_origLocalPos/Rot/Scale`.
  `HeldFigures.Add` re-enters the set in the same call.
- **Why:** otherwise the "original" pose captured is a mid-glide sample and the mini drifts a
  little further from home on every re-grab. Re-entering in the same call means `FigureGhosts`
  never sees a released frame, so the ghost and its captured home pose survive the re-grab.
- **Confidence:** high

### The glide uses UNSCALED time and a cubic ease-out
- **Where:** `FigureGrabbable.GlideDurationSeconds` (0.28), `TickGlide`
- **Rule:** `Time.unscaledTime`.
- **Why:** the game pauses `timeScale` (camera transitions, dialogs); a scaled glide would
  freeze mid-air.
- **Confidence:** high

### `AuthoritativeCellChanged` auto-releases a held figure the game moved
- **Where:** `FigureGrabbable._grabCell`, `AuthoritativeCellChanged`,
  `FigureGrabDriver.AutoReleaseMovedFigures`
- **Rule:** the actor's `ArrayIndex` is snapshotted at grab and compared each frame; a change
  (or a destroyed actor/root) forces `Restore()`.
- **Why:** a networked move on a remote/enemy turn would leave the mini riding the hand at a
  now-stale board position and jumping on release. `Restore` is idempotent, so the grabber's
  logical hold still ends normally on trigger-up.
- **Confidence:** high

### `Restore` is defensive about a destroyed original parent
- **Where:** `FigureGrabbable.Restore` (`Transform? parent = _origParent != null ? _origParent : null;`)
- **Rule:** the Unity `!= null` check converts a destroyed parent to a real null so `SetParent`
  unparents to the scene root instead of throwing.
- **Why:** the parent can die while the figure is held (actor removed / scene teardown). This
  looks like a no-op ternary and is not — it is a fake-null unwrap.
- **Breaks if:** "simplified" to `t.SetParent(_origParent, …)`.
- **Confidence:** high

### Offset-anchor selection works by SUPPRESSING losers, not by changing the grabber's metric
- **Where:** `FigureGrabDriver.SelectByOffsetAnchor`, `FigureGrabbable.SetProximitySuppressed`,
  `FigureGrabbable.AllowsHand`, `FigureGrabDriver.ReachMeters` (0.13, mirrors
  `ProximityGrabber.ReachMeters`)
- **Rule:** among figures within PALM reach, the one nearest the OFFSET ANCHOR wins; every
  other palm-reach candidate is marked suppressed FOR THAT HAND via `IGrabbableHandFilter`.
  Uncontested figures and far laser targets are never suppressed.
- **Why:** `ProximityGrabber`'s metric is nearest-to-palm and is shared with cards/panels; it
  cannot be changed for figures alone. Suppression is the composable way in. The two reach
  constants must stay equal or the candidate sets diverge.
- **Established by:** `f338d5c`
- **Breaks if:** the reach constant drifts, or suppression is not cleared when the hand becomes
  untracked / starts holding (see `ClearSuppression`).
- **Confidence:** high

### The far figure pluck ignores its OWN beam clamp when arbitrating
- **Where:** `FigureGrabDriver.TryLaserGrab` (`_leftClampFrame` / `_rightClampFrame`,
  `bool foreignUi = hand.Ray.HasFreshUiHit && Time.frameCount - myClampFrame > 1`)
- **Rule:** record the frame we clamped the beam to a figure; treat `HasFreshUiHit` as foreign
  only when it is not ours.
- **Why:** the driver itself sets `Ray.UiHitOverride` on hover (so the trigger over a figure
  grabs instead of doubling as a board far-click). Without the self-check it would defer to
  itself and never grab.
- **Established by:** `8d28b55` feat(figures): grab real board minis into hand
- **Breaks if:** the per-hand clamp frames are removed.
- **Confidence:** high

### The far pluck only runs when the proximity grabber is idle
- **Where:** `FigureGrabDriver.TryLaserGrab` (`if (hand.Grabber.Held != null || hand.Grabber.Highlighted != null) return;`)
- **Rule:** near reach-grab belongs to `ProximityGrabber`.
- **Why:** both use the trigger; without this the two paths race on the same press.
- **Confidence:** high

### Held figures: transform writes are suppressed, STATE is never touched
- **Where:** `ActorBehaviour_HeldTransform_Patch.Update_Prefix` / `LateUpdate_Prefix`,
  `HeldFigures.Owns`, `NetHeldFigures.Owns`
- **Rule:** whole-method skip of `Update` and `LateUpdate` for held actors only.
  `FixedUpdate` (invisibility dissolve) is deliberately NOT patched.
- **Why:** skeletal animation is driven by the separate `m_Animator` component, which Unity
  ticks on its own while `m_Animator.enabled` (we never call `PauseLoco`, the only thing that
  disables it). `DoTransform` only WRITES the transform position and sets the `RunBlend`
  locomotion float — it does not play the animation. So the skip freezes position and freezes
  RunBlend while the clip keeps playing. On release the actor leaves the set and the game's own
  Update snaps it back to its cell — no manual return math.
- **Established by:** `8d28b55`
- **Breaks if:** narrowed to `DoTransform`/`ApplyMotion` (they are private call targets, and the
  RunBlend nudge would resume), or `FixedUpdate` is added.
- **Confidence:** high

### `PinAnimatedRoots` reinstates exactly the ONE suppressed write
- **Where:** `HeldFigures.PinAnimatedRoots`, `NetHeldFigures.PinAnimatedRoots`
- **Rule:** zero `m_AnimatedGameObject.localPosition` each LateUpdate, after the Animator.
- **Why:** that is precisely what `ApplyMotion` does to absorb the walk/loco clip's root
  translation. Without it a figure grabbed mid-animation animates straight out of the hand.
- **Established by:** `436e6d7`
- **Confidence:** high

### `SetHilighted` is routed to `FigureRingSuppressor.RecordGameIntent` for held actors
- **Where:** `ActorBehaviour_HeldTransform_Patch.SetHilighted_Prefix`, `FigureRingSuppressor`
- **Rule:** the game's mid-hold selection-ring toggles are recorded, not applied; the recorded
  intent (select OR deselect) is re-applied exactly on release. `FigureRingSuppressor.Tick`
  additionally hides a ring the game re-activated this frame.
- **Why:** `m_Hilight` is a child of the actor subtree and therefore rides the hand. Only the
  ghost's own copy at the home cell should show it. Both mechanisms are needed: the prefix
  catches explicit toggles, the LateUpdate reconcile catches re-activations from other paths.
  A deselect-while-held must be honoured on release, not wrongly resurrected.
- **Established by:** `50537a1` fix: singleton-safe dual stat panels, ring-only-at-ghost, animated fog-free ghost, calmer pulse
- **Breaks if:** either half is dropped, or the ring is simply force-disabled without recording
  intent.
- **Confidence:** high

### `FigureRingSuppressor.Restore` aliases the key before the Unity null check
- **Where:** `FigureRingSuppressor.Restore` (`ActorBehaviour key = actor;`)
- **Rule:** the CLR-non-null alias is used for `_tracked.TryGetValue`/`Remove`; the Unity
  `!= null` check only guards the ring access.
- **Why:** a DESTROYED actor is still a valid CLR dictionary key; using the fake-null-compared
  reference for the dictionary would leak the entry.
- **Confidence:** high

### The ghost is spawned EXPLICITLY at grab and despawned by RECONCILIATION
- **Where:** `FigureGhosts.NotifyHeld` (called from `FigureGrabbable.OnGrab` BEFORE the reparent),
  `FigureGhosts.Tick` (reconciles against `HeldFigures` ∪ `NetHeldFigures`),
  `FigureGrabDriver.Update` (ghost tick runs even when local figure-grab is disabled)
- **Rule:** spawn is explicit at the exact moment a hold begins so the frozen snapshot and home
  pose are captured at the board; despawn is derived purely from the two held-sets, so no
  release path can leak a ghost.
- **Why:** local `Restore` and remote `NetFigures` release both simply drop the actor from a set.
  The ghost tick must run unconditionally so REMOTE-held ghosts still appear/clear when local
  grabbing is off.
- **Established by:** `bcc9260` feat(figuregrab): animated highlight, home-spot ghost, dual info panels (MP-synced)
- **Breaks if:** spawn is moved into `Tick` (captures a mid-hand pose), or the ghost tick is
  moved inside the `GrabFigures` gate.
- **Confidence:** high

### Ghost/highlight overlays use `_ZTest LEqual` + `_ZWrite 0`, and are SKIPPED without the bundle shader
- **Where:** `FigureOverlay.MakeOverlayMaterial`, `FigureHighlight.Apply`, `FigureGhosts.NotifyHeld`
- **Rule:** both routes are occlusion-correct; when `PlayTray.OverlayShader()` is null the
  effect is skipped entirely rather than falling back.
- **Why:** the figure already wrote depth in its opaque pass, so an LEqual/ZWrite-off overlay is
  hidden by walls exactly like the mini. The game's Amp character shaders do NOT expose
  `_EmissionColor`, so the original emissive-glow approach was a silent no-op — the bundled
  Overlay shader is the tool that works. A fallback would produce a wall-piercing highlight,
  which is worse than none.
- **Established by:** `294cbf8` (emission is a no-op) / `b03fbc6` (pre-grab highlight) / `da66275` (depth-correct)
- **Breaks if:** a "graceful" fallback shader is added, or ZTest is set to Always.
- **Confidence:** high

### The highlight container is a SIBLING of the Animator object
- **Where:** `FigureHighlight.Apply` (`root.transform.SetParent(figureRoot.transform, …)`),
  `FigureOverlay.CloneRenderersSharingBones`
- **Rule:** clones are parented under a container on the figure ROOT, not under the animated
  object; skinned clones share the ORIGINAL `bones`/`rootBone`.
- **Why:** the game's own `GetComponentsInChildren<Renderer>()` passes (jump-exit opacity,
  invisibility dissolve) must never enumerate our extra renderers. Sharing bones is what makes
  the overlay track the live idle animation with no per-frame work.
- **Confidence:** high

### The frozen ghost strips scripts/VFX but KEEPS the Animator
- **Where:** `FigureOverlay.BuildFrozenGhost`
- **Rule:** `applyRootMotion = false`, `fireEvents = false`, `cullingMode = AlwaysAnimate`;
  Cloth/Collider/Rigidbody/MonoBehaviour destroyed; ParticleSystems stopped+cleared and
  destroyed BEFORE their renderers (RequireComponent dependency); renderers on distort/particle/
  fog/FX shaders destroyed rather than re-tinted; `m_Hilight`'s twin keeps its ORIGINAL materials.
- **Why:** particles and distort-shader renderers kept simulating and were re-tinted into a
  fog/mist blob at the home cell. `fireEvents = false` because the event receivers are gone.
  `AlwaysAnimate` because an offscreen home cell would otherwise freeze the ghost mid-pose.
  The ring twin must look exactly vanilla.
- **Established by:** `50537a1`; ring-twin preservation in the same commit
- **Breaks if:** the ParticleSystem/renderer destruction order is swapped, or MonoBehaviours are
  merely disabled without `Destroy` (deferred destruction still lets one Update run — hence
  `mb.enabled = false` FIRST, then `Destroy`).
- **Confidence:** high

### `OverlayMaterialOwner` / `OverlayPulse` exist because Unity does not free materials with GameObjects
- **Where:** `OverlayMaterialOwner.OnDestroy`, `OverlayPulse.OnDestroy`
- **Rule:** every overlay material has exactly one owner component that destroys it.
- **Why:** otherwise one material leaks per hover and per ghost.
- **Confidence:** high

### `ApplyRenderOnTop` is a deliberate NO-ACTION with the code retained
- **Where:** `FigureGrabbable.ApplyRenderOnTop` (`return;` before an unreachable block guarded
  by `#pragma warning disable CS0162`)
- **Rule:** held minis keep their native depth-correct render. The call sites stay so the
  grab/release symmetry (with `RestoreRenderers`) is unchanged.
- **Why:** bumping the held mini to `HeldRenderQueue` (4100) did make it draw over the control
  board's widgets, but it also punched the mini THROUGH walls/floors/health-bars and the effect
  persisted after release. The user wants perspective respected for every element except the
  sky. The board-widget occlusion is handled proud-seat + LEqual on the widget side instead.
- **Established by:** `4ef1d76` fix: revert card + held-figure render-on-top — restore card text & depth-correct figures (perspective)
- **Breaks if:** the unreachable block is deleted as dead code (Tier-0 candidates must be
  checked against §11 — this one is a documented, reversible decision, not dead code) or the
  early `return` is removed.
- **Confidence:** high

---

## 9. Core — session, mode machine, tick isolation

### `TickGuard.Run(name, fn)` is the per-subsystem isolation seam
- **Where:** `Core.TickGuard.Run`, `TickGuard.State`, `TickGuard.DeriveScope`; call sites in
  `BoardDriver.Update`, `HandsDriver.Update`, `FigureGrabDriver.Update` / `LateUpdate`,
  `BoardPing.Update`, `SelectionReadyHighlighter.LateUpdate`
- **Rule:** every per-frame sub-tick runs inside `TickGuard.Run`. The first throw per step name
  is logged at Error WITH its stack, repeats are throttled to ~1 per 10 s on `Time.unscaledTime`,
  and it NEVER rethrows. Callers pass CACHED delegates.
- **Why:** a driver whose `Update` runs a sequence of `.Tick()` calls with no try/catch will, on
  a single throwing sub-tick, abort the REST of that frame's ticks AND log an anonymous stackless
  NullReferenceException every frame — a flood that names no subsystem and starved the input
  pipeline (the pause-menu reopen bug). This is a robustness + attribution layer, not a fix for
  the underlying null: the point is that the next log NAMES the subsystem.
- **Established by:** `16f32ee` fix: promote shared Core.TickGuard, isolate+attribute driver tick sequences
- **Breaks if:** the calls are inlined back into `Update` "for readability"; per-frame method
  groups from INSTANCE methods are passed (allocates a delegate every frame — hence
  `HandsDriver._tickRig` / `_tickSim` / `_tickGhost` cached in `Awake`, and the deliberate
  bool-through-a-field pattern in the rig driver); or it starts rethrowing.
- **Confidence:** high

### `ScenarioBoardExists` is the canonical "a board exists" signal — deliberately NOT the camera or the game state
- **Where:** `VRModeStateMachine.ScenarioBoardExists` (`Choreographer.s_Choreographer != null`)
- **Rule:** Rig, WorldUI and the mode machine all read THIS.
- **Why:** `CameraController.s_CameraController` ALSO exists on the campaign/world map
  (`ClickTrackerMap` raycasts map locations through it) — that was the test-#8 giant-map bug.
  `SaveData…CurrentGameState == EGameState.Scenario` derives from the adventure MapState phase
  and flips during loading/travel before any board exists. The Choreographer scene object is
  set in its Awake and nulled in its OnDestroy, and exists only in the scenario scenes.
- **Established by:** `9826e6e` feat(events): UIWindow visibility events + aux-modal input + canonical scenario signal; `66c0839` fix(rig): campaign/world map stays flat — scenario rig requires an actual scenario board
- **Breaks if:** any consumer switches to the camera or the game-state enum.
- **Confidence:** high

### Mode composition priority: Menu2D > ModalUI > BoardTargeting > flow
- **Where:** `VRModeStateMachine.Recompute`, `_inScenario` / `_modal` / `_auxModal` /
  `_targeting` / `_flowMode`
- **Rule:** exactly this order; leaving the scenario clears every scenario-scoped input.
- **Why:** three independent inputs compose into one effective mode; any reordering changes
  which interactor mask applies in overlapping states.
- **Confidence:** high

### `WaitingForCardSelection` must NOT be added to `TargetingStates`
- **Where:** `VRModeStateMachine.TargetingStates`
- **Rule:** the five listed wait-states only.
- **Why:** this is the "obvious fix" for the #19 laser latch and it is wrong — that state is
  also live during round-start card picking and hero placement, and forcing `BoardTargeting`
  there would break the CardSelection fan. The correct fix is the central dominant-Ray OR in
  `InteractorsFor`.
- **Established by:** `e06bae1` (states the rejected alternative explicitly)
- **Breaks if:** a future "targeting laser missing" report is fixed by editing this set.
- **Confidence:** high

### Mode policy tables are DATA with a public extension API — never patch the matrix
- **Where:** `VRModeStateMachine.MapMessage`, `SetTargetingState`, `SetInteractorPolicy`,
  `SetHandInteractorPolicy`, `ClearHandInteractorPolicy`, `SetAuxModal`;
  used by `HandsModule.Init`/`Shutdown` (Menu2D per-hand policy) and `CardsDriver`
- **Rule:** modules register overrides from their `Init` and clear them in `Shutdown`
  (statics survive within one assembly load, so the symmetry is required for hot reload).
- **Why:** the state machine is the single source of truth for the per-mode/per-hand interactor
  matrix; modules must not toggle interactors directly.
- **Confidence:** high

### `ModalUI` carries `Grab` — cards refuse it per-object
- **Where:** `VRModeStateMachine.InteractorPolicy[VRMode.ModalUI]`; `VRCard.CanGrab`
- **Rule:** the interactor provides the primitive; per-object policy decides who takes it.
  The tray's `PanelGrabHandle` accepts in ModalUI, `VRCard` refuses.
- **Why:** the tray dashboard's grab handle must keep working while a dialog floats (test #15),
  but card grabs must not. Gating per-object also means a HELD card is not force-dropped by
  interactor teardown. The named pattern is **primitive-vs-policy**.
- **Established by:** `101afb6` feat(events): Grab interactor active in ModalUI; cards refuse modal grabs
- **Breaks if:** `Grab` is removed from the ModalUI row again to "fix" a modal card grab.
- **Confidence:** high

### `VREvents.Invoke` swallows subscriber exceptions
- **Where:** `VREvents.Invoke<T>`
- **Rule:** a throwing subscriber is logged and does not propagate.
- **Why:** the raise sites are Harmony postfixes inside `Choreographer.Update`'s message pump;
  an escaping exception takes the game's pump down. Handlers must also never block — the
  ScenarioRuleLibrary worker thread is fed from here.
- **Confidence:** high

### Event payloads are readonly STRUCTS passed by `in`
- **Where:** `VREvents.Raise(in …)`, all `…Event` structs
- **Rule:** no per-event heap allocation; fields are live references, consumed synchronously.
- **Confidence:** medium

### `VRPresenceWatch.UserPresent` is POSITIVE evidence only, and cleared in `OnDisable`
- **Where:** `VRPresenceWatch.UserPresent`, `VRPresenceWatch.Update`,
  `FrozenPoseSeconds` (5), `MinGapSeconds` (3)
- **Rule:** worn = the runtime's `userPresence` feature reports true, OR the head pose changed
  within 5 s. False before the first sample, while frozen, and with no head camera.
- **Why:** VDXR may not report the proximity sensor at all, so presence cannot rely on it alone;
  a worn HMD never freezes below the sensor-jitter epsilon, which is the fallback. This flag is
  what breaks the test-#18 pointer flip-war: Virtual Desktop keeps injecting host mouse events
  while the user plays in VR, so the physical mouse stays permanently "fresh" and any
  recency/active-thief heuristic defers to it forever. Only PRESENCE can break that tie.
  Gaps under `MinGapSeconds` are sensor blips and raise no recovery sweep.
- **Established by:** `f90659b` feat(core): presence watch + session-resume recovery sweep on HMD re-don; `6d4b575` fix(worldui): presence-gated pointer currency (test #18 flip-war)
- **Breaks if:** replaced by a recency heuristic, or `OnDisable` stops clearing it (a torn-down
  watch would report "worn" forever).
- **Confidence:** high

### `SessionResumedEvent.Recovered` is a shared list handlers append to
- **Where:** `VRPresenceWatch.Recovered` (static, reused), `SessionResumedEvent`
- **Rule:** handlers recover synchronously and append a short description; the raiser logs the
  union afterwards and clears the list.
- **Why:** one "[Core] Session resumed" line stating what was actually recovered, with no
  allocation in steady state.
- **Confidence:** medium

### OpenXR init success = a display subsystem EXISTS, not that it is `running`
- **Where:** `OpenXRBootstrap.TryStartWith` (`if (displays.Count > 0) return true;`),
  `OpenXRBootstrap.WatchDisplayRunning`, `DisplayRunningWatchdogFrames` (900)
- **Rule:** existence is the success criterion; a watchdog coroutine reports when rendering
  actually starts (or errors when it never does).
- **Why:** `OpenXRLoaderBase.StartInternal()` returns while the session is still negotiating —
  the XrReady native event that actually starts the display subsystem arrives via the
  `Application.onBeforeRender` pump a few rendered frames later. Requiring `running == true`
  synchronously tore down healthy sessions (booting SteamVR, then declaring failure).
- **Established by:** `969cecb` fix(core): XR init success = display subsystem EXISTS, not already running
- **Breaks if:** re-tightened to `.running`.
- **Confidence:** high

### Interaction profiles must be enabled BEFORE the session starts
- **Where:** `OpenXRBootstrap.CreateSettings` (Oculus Touch / Valve Index / KHR Simple,
  `OpenXRSettings.Instance.features`), `renderMode = MultiPass`, `depthSubmissionMode = None`
- **Rule:** profiles are created and enabled before `InitXRSDK()`.
- **Why:** otherwise there is no controller input at all. MultiPass is the safe default for the
  built-in RP; SPI is a later opt-in.
- **Confidence:** high

### Runtime candidate order: system default first, SteamVR LAST
- **Where:** `OpenXRRuntimeRegistry.GetCandidates`, `Plugin.RuntimePriority` ("auto"),
  `Plugin.SkipRuntimeCandidates`
- **Rule:** system default → VDXR when Virtual Desktop is streaming → the rest, SteamVR last.
- **Why:** *attempting* a runtime boots it — putting SteamVR first side-launched its compositor
  on a VDXR machine.
- **Established by:** `1fd0c75` fix(core): try the system-default OpenXR runtime first, SteamVR last
- **Confidence:** high

### XR-typed methods must not be JIT-compiled before `RuntimeDepsLoader.LoadAll()`
- **Where:** `CoreModule.Init` ordering, `CoreModule.StartVR` / `StopVR`
  (`[MethodImpl(MethodImplOptions.NoInlining)]`)
- **Rule:** `Init()` only CALLS the non-inlined methods that reference XR types.
- **Why:** Mono binds assembly references against already-loaded assemblies when a method is
  JITted; inlining `StartVR` into `Init` would pull XR types into `Init`'s JIT before the
  `Assembly.LoadFile` pass ran.
- **Breaks if:** the attributes are removed, or the bodies are inlined by hand.
- **Confidence:** high

### `RuntimeDepsLoader` replays `[RuntimeInitializeOnLoadMethod]` in Unity's real order, once per AppDomain
- **Where:** `RuntimeDepsLoader.InvokeRuntimeInitializers`, `InitializersInvokedKey`,
  the explicit `executionOrder` array
- **Rule:** the order array is explicit (the enum values are NOT in execution order:
  SubsystemRegistration=4 … AfterSceneLoad=1). The idempotency guard lives on the
  **AppDomain**, not on a plugin static.
- **Why:** Unity only invokes those hooks for assemblies in `ScriptingAssemblies.json`;
  `Assembly.LoadFile`'d assemblies are invisible to that scan. Plugin statics reset on
  ScriptEngine hot reload but the RuntimeDeps assemblies (and any side effects) persist, so a
  static-only guard would re-run the hooks.
- **Breaks if:** `executionOrder` is replaced by `Enum.GetValues`, or the AppDomain key is
  dropped. Today the loop is a safety net (the shipped deps' hooks are no-ops without an
  `XRGeneralSettings.Instance`); it becomes load-bearing the moment a dep with real hooks is added.
- **Confidence:** high

### `Plugin.OnDestroy` shuts modules down in REVERSE order, then unpatches
- **Where:** `Plugin.OnDestroy`, `Plugin.RegisterModules` (Core first, Compat last)
- **Rule:** reverse iteration so Core stops XR last; `_harmony.UnpatchSelf()` after all
  shutdowns; `StopAllCoroutines()` first; `VRSession.CoroutineHost = null`.
- **Why:** the ScriptEngine (F6) hot-reload contract — every patch, every driver GO, every
  cloned material and every registry must be undone. Modules gate their `Init` on
  `VRSession.IsRunning`, which Core owns.
- **Breaks if:** the order is normalised, or a module's `Shutdown` stops being symmetric with
  its `Init` (see `HandsModule` clearing its Menu2D per-hand overrides).
- **Confidence:** high

### A patch class that is written but not REGISTERED is inert
- **Where:** `BoardModule.Init` / `VREventsModule.Init` / `CompatModule.Init` `PatchAll` lists
- **Rule:** every patch class must appear in exactly one module's `PatchAll` call.
- **Why:** this has been a real failure mode twice — `8567af4` wire(compat): register
  WallFadeDisable in CompatModule.Init and `57309c5` wire(compat): register InitialInputSkip
  patch in CompatModule.Init both exist solely because the patch was written and never wired.
- **Breaks if:** a patch class is moved to another file/namespace during a Tier-1 motion and its
  `PatchAll` reference is not moved with it.
- **Confidence:** high

### `VRSession.IsRunning` false ⇒ every patch behaves 100 % vanilla
- **Where:** `VRSession.IsRunning`; consulted by `VRLayers.Apply`, `VRCameraPolicy.Sweep`,
  `CameraArrivalGuard.Tick`, `WallSegmentFade.Install`/`Tick`, `MixedReality.Tick`,
  `SkyBackdrop.Tick`, and the module `Init` gates
- **Rule:** the flag is the single global kill switch.
- **Confidence:** high

---

## 10. Core — rendering, layers, camera ownership

### The rig owns its OWN head camera; game cameras never render stereo, period
- **Where:** `VRCameraPolicy.AllowedHead`, `VRCameraPolicy.Sweep`, `VRCameraPolicy.RestoreAll`
- **Rule:** every camera except `AllowedHead` is forced to `StereoTargetEyeMask.None` with
  `XRDevice.DisableAutoXRCameraTracking(cam, true)`. With no rig head, EVERY camera is forced
  to None. Originals are recorded on first force and restored on VR-off / hot reload.
- **Why:** test #3 — ANY enabled camera with stereo=Both hijacks the HMD. The first fix promoted
  ONE game camera to rig head; test #4 showed that head-tracking a GAME-owned camera lets game
  code (menu camera writers, component toggles, VideoPlayer interactions) break pose application
  invisibly. Owning the camera removes the interference CLASS instead of chasing triggers.
- **Established by:** `55569fe` fix(rig): rig owns its own head camera — game cameras never render stereo, period
- **Breaks if:** a "stand down when no rig head exists" special case is re-added, or the anchor
  camera is re-parented / pose-driven again.
- **Confidence:** high

### `Sweep` is a pump, not a one-shot; `PruneDead` keeps fake-null keys usable
- **Where:** `VRCameraPolicy.Sweep(reason)` (called on scene load, after every rig rebuild, and
  periodically), `VRCameraPolicy.PruneDead`, `GetAllCamerasNonAlloc`
- **Rule:** `reason` must be a constant/interned string (no per-frame formatting); the scan
  buffer is reused; `PruneDead` keeps the destroyed camera REFERENCE (needed for
  `Dictionary.Remove`) even though it compares null.
- **Why:** cameras are created late (`MainMenuVideo`, RT cameras); a one-shot sweep misses them.
- **Confidence:** high

### ONE mod layer, resolved at runtime; game-owned objects are NEVER re-layered
- **Where:** `VRLayers.ModLayer` / `Resolve` (first unnamed layer scanning 31→8, fallback UI 5),
  `VRLayers.Apply` (no-op unless `VRSession.IsRunning`)
- **Rule:** all mod-owned visuals go on the mod layer; the head camera ORs `ModLayerMask` into
  its culling mask and no other camera gets the bit. Converted uGUI panels, tooltips and card
  faces keep their authored layers.
- **Why:** mod visuals used to inherit layer 0 or hard-coded 5 while menu cameras cull 0x20 (or
  0x0), so hands/lasers/quad were invisible in the HMD (test #3). Ad-hoc per-object fixes fought
  the game's masks instead of owning one layer. Layers are RENDER-only here — picking runs
  through the registries and the game's own selection masks — so re-layering a game object is
  the regression. The dev-mode no-op preserves the pre-fix flat behaviour.
- **Established by:** `e86d1d0` / `1a00709` (VRLayers + VRCameraPolicy)
- **Breaks if:** a game object is passed to `Apply`, or the sim-mode gate is removed.
- **Confidence:** high

### Anything created AFTER the tree-wide `VRLayers.Apply` must layer itself
- **Where:** `HandVisuals.Build` (glove instance), `RayInteractor.CreateVisuals`,
  `SkyBackdrop.EnsureResetObject` (`_resetGo.layer = VRLayers.ModLayer`)
- **Rule:** every lazily-created mod visual applies the layer at creation.
- **Why:** the bundle glove is instantiated after `HandsDriver`'s sweep and stayed on layer 0 —
  the menu head camera (mod-layer-only mask) culled it, so the hands vanished in front of the
  menu.
- **Established by:** `7d9a338`
- **Breaks if:** these calls are deduplicated as "already handled by the driver".
- **Confidence:** high

### The head camera renders FORWARD — this is the root cause of eight rounds of see-through
- **Where:** `Plugin.ForwardRendering`, consumed by `VRRigDriver` (`renderingPath = RenderingPath.Forward`)
- **Rule:** forward, not the game's DeferredShading.
- **Why:** the `SkyBackdrop` depth-reset renderer (Overlay shader, ZTest Always, queue 1999) has
  NO deferred pass, so on a DEFERRED camera it runs in the forward-opaque FALLBACK — AFTER the
  deferred G-buffer walls — and its ZTest-Always wipes the wall depth for the whole transparent
  pass, so ALL transparent effects (fire/torch glow, hex selection ring, health bars) bled
  through walls. Opaque figures were unaffected (occluded in the G-buffer BEFORE the wipe) —
  exactly the observed split. Forward rendering restores strict queue order: reset (1999) runs
  BEFORE walls (2000) and the walls overwrite it.
- **Established by:** `1aa339a` fix: head camera renders FORWARD — deferred made the sky depth-reset wipe wall depth (root cause, 8 rounds)
- **Breaks if:** the head camera goes back to deferred, or the depth-reset queue/ZTest is
  "simplified". Eight prior rounds (`da66275`, `278f11e`, `08198ab`, `f97920e`, `7246907`,
  `ed942c7`, `8019975`, `af95ad0`) chased the symptom — do not restart that.
- **Confidence:** high

### The sky sphere is NEVER suppressed; depth is reset by a separate ORDINARY draw
- **Where:** `SkyBackdrop.Decide`, `SkyBackdrop.ApplyEffects`, `SkyBackdrop.EnsureResetAssets`,
  `DepthResetQueue` (1999), `BackgroundQueue` (1000)
- **Rule:** the sphere material drops to `RenderQueue.Background` (1000) and its own automatic
  draw renders the colour; a mod-layer quad at queue 1999 with
  `ZTest Always, ZWrite On, Blend Zero One, Cull Off` overwrites depth to ~far.
- **Why:** two earlier attempts went BLACK for the same reason — **suppressing a renderer
  (`enabled = false` OR `forceRenderingOff = true`) also suppresses
  `CommandBuffer.DrawRenderer` for it**: it is dropped from the camera's prepared/culled render
  data, so the sphere was drawn by neither path. A mid-pass depth `ClearRenderTarget` greyed
  colour to black on the tiled Quest GPU. Ordering must be by RENDER QUEUE, not camera events —
  a `CameraEvent` can only inject before ALL opaque or after ALL opaque; there is no
  "between the sky and the rest" event.
- **Established by:** `200d9cd` → `b21fd7b` → `ba1a5dc` fix(core): sky visible via DepthResetRenderer — never suppress the sphere
- **Breaks if:** any "simplify by disabling the sky renderer" or "just clear depth" change.
- **Confidence:** high

### The depth-reset surface is a FLAT HEAD-FACING QUAD, not a head-centred sphere
- **Where:** `SkyBackdrop.UpdateResetObject`, `DepthResetFarFraction` (0.98),
  `DepthResetCoverFactor` (8)
- **Rule:** a plane perpendicular to the view axis, re-posed every frame at 0.98 × the LIVE far
  clip plane, oversized by 8×.
- **Why:** a sphere centred on the head writes depth `R·cos(theta)` — a FIXED screen-space
  RADIAL gradient (~30 % nearer at the edge of a 90° FOV). Where a highlighted hex overhangs the
  VOID around the floating diorama, that reset depth IS the scene depth the game's
  depth-fading hex-highlight glow reads back, so a world-fixed hex sliding across a screen-fixed
  gradient made the fade shimmer — the "see-through that moves with the head". A flat
  perpendicular plane has constant view-space z ⇒ uniform depth. 0.98 keeps it inside the far
  plane (a clipped quad leaves the view un-reset); the far plane CHANGES under world-grab zoom,
  hence per-frame. The 8× cover absorbs the two per-eye frustums and their asymmetry.
- **Established by:** `c36ffe4` fix(sky): flat head-facing depth-reset quad kills head-tracking see-through inside hex highlights (#7)
- **Breaks if:** reverted to a sphere, the cover factor is shrunk, or the pose is computed once.
- **Confidence:** high

### `SkyBackdrop.Decide` picks its mechanism from the shader's ACTUAL property inventory
- **Where:** `SkyBackdrop.Decide`, `DepthWriteHints`, `Mechanism.ZWriteOff` /
  `Mechanism.DepthResetRenderer`
- **Rule:** if the shader exposes ANY numeric depth-write property (under any of the hinted
  names) use `ZWriteOff`; otherwise use the depth-reset renderer. The full property dump is
  logged on first sight.
- **Why:** `AMP_SkyShader` hard-codes `ZWrite On` and exposes no `_ZWrite`, which is why the
  cheap path is unavailable — and the log is the evidence for that claim on the next game update.
- **Breaks if:** the mechanism is hard-coded, or the property dump is removed.
- **Confidence:** high

### MR keys the HEAD camera and hides sky GEOMETRY — a clear-flag change alone is not enough
- **Where:** `MixedReality.Tick`, `MixedReality.HideSkyGeometry`, `IsSkyRenderer`,
  `SkyMinEnclosingSize` (10), `SkyEnclosingFarFraction` (0.1), `SkyNameHints`
- **Rule:** null `RenderSettings.skybox` (re-asserted every tick — `StaticAmbience` can re-set
  it), force the head camera to SolidColor(key), sweep every OTHER camera still on
  `CameraClearFlags.Skybox`, and disable renderers that are sky-ish by name/material/shader OR
  whose world bounds ENCLOSE the head with a large extent on ALL THREE axes.
- **Why:** the scenario background is drawn as OPAQUE MESH geometry, not a skybox — the
  environment is procedurally generated (Apparance), the head camera renders the scenario
  camera's full culling mask, and `StaticAmbience.Apply` is the ONLY writer of
  `RenderSettings.skybox`. No clear-flag or skybox patch can remove a backdrop mesh. The
  three-axis enclosure test is what distinguishes a dome/backdrop box from a flat floor tile
  (one thin axis) or a table prop (does not contain the head).
- **Established by:** `5c881e7` feat(core): mixed reality chroma-key mode; `033d5b5` fix(mr): mixed reality hides the sky/background GEOMETRY, not just the clear
- **Breaks if:** the geometry sweep is dropped, the enclosure test is relaxed to one or two
  axes, or the mod layer / UI layer exclusions are removed from the sweep.
- **Confidence:** high

### MR only ever touches cameras whose clear is STILL Skybox
- **Where:** `MixedReality.Tick` (`cam.clearFlags != CameraClearFlags.Skybox → continue`)
- **Rule:** FlatScreen's RT-captured cameras are already non-Skybox, so MR never writes the same
  camera in the same frame; FlatScreen keeps authority over them.
- **Why:** documented precedence — two owners writing one camera's clear per frame is a flicker.
- **Confidence:** high

### `MixedReality.KeepMenusUnclipped` is a deliberate NO-OP kept for its call sites
- **Where:** `MixedReality.KeepMenusUnclipped`
- **Rule:** empty body, with the reason in the comment above it.
- **Why:** the previous lever disabled the backdrop renderer while a menu floated; the user
  found the vanishing sky very distracting. The occlusion is now fixed on the MODAL side
  (`CanvasConversion` renderOnTop). Deleting the method silently changes the WorldUI call sites'
  shape and loses the record of the rejected approach.
- **Established by:** `b84817d` fix(worldui): render floated modals ON TOP instead of disabling the sky
- **Breaks if:** deleted as dead code without also updating `ModalFallback` — and note that
  re-implementing it is the regression.
- **Confidence:** medium

### Hex highlight: the stereo-stable shader keeps the ORIGINAL property names
- **Where:** `HexHighlightFix.TrySwapStable`, `StableShaderName`, `OriginalShaderName`
- **Rule:** swap only a material whose current shader is exactly `OmniDecal_Shd`; property names
  in `HexDecalStable` are identical to the original; the pre-swap `renderQueue` (4000) is
  re-asserted after the swap; swapped materials are tracked and restored in `Reset`.
- **Why:** the game's per-state writes in `ProjectorMaterialAdjustment` must keep landing, and
  Unity carries all matching property values (textures included) across a `Material.shader`
  assignment. `OmniDecal_Shd` is a screen-space depth-reconstruction projector: it derives the
  shaded point from `_CameraDepthTexture` + `unity_CameraInvProjection`/`unity_CameraToWorld`
  per pixel. In multipass XR the raster and depth texture are per-eye but the UnityPerCameraRare
  matrices are mono — hence the right-eye-only "reflection" that swims with head pose.
- **Established by:** `63fd581` (layer-kill mitigation) → `d7201b6` fix(board): stereo-stable hex-highlight shader
- **Breaks if:** properties are renamed, the queue re-assert is dropped, or the swap is widened
  beyond the one shader we ported.
- **Confidence:** high

### The swap runs in a POSTFIX on the game's own single material writer
- **Where:** `HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch.Postfix`
- **Rule:** postfix `HexSelect_Control.ProjectorMaterialAdjustment` — the one method that writes
  every hex-highlight material property, including right after each
  `m_Material = new Material(_exampleMaterial)` re-creation. Body fully try/caught, error log
  capped at 3.
- **Why:** setting the properties once (or polling) is undone by the next game write. Work
  happens only when the game itself just rewrote the material, not per frame.
- **Breaks if:** converted to a polling driver or a one-shot init.
- **Confidence:** high

### The stable decal exports SV_Depth, so ZTest LEqual is possible at all
- **Where:** `HexHighlightFix.ApplyOcclusionKnobs`, `VRZTest` (default 4 = LEqual),
  `VRDepthBias` (2e-4); the shader's per-pixel depth export
- **Rule:** LEqual **only** together with the depth export; `ZWrite` stays off; the bias is
  re-applied on every swap/postfix so live config edits take effect.
- **Why:** vanilla ran ZTest Always (highlight drew through walls and figures). A naive LEqual
  would z-KILL the whole decal, because the Cull-Front box mesh's visible fragments sit BELOW
  the floor plane — only exporting the true floor-point depth makes LEqual correct. The bias
  prevents z-fight speckle against the co-planar tile floor. `8` restores vanilla draw-through
  as an on-device fallback.
- **Established by:** `b6cb01c` fix(board): hex highlight respects occlusion — per-pixel SV_Depth export + ZTest LEqual
- **Breaks if:** `_VRZTest` is flipped to LEqual without the export, `ZWrite` is enabled, or the
  bias is dropped.
- **Confidence:** high

### Fallback layer-kill knobs are BYPASSED while the stable swap is active
- **Where:** `HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch.Postfix`
  (`if (SwapStableShader && TrySwapStable(mat)) { … return; }`), `_knobsBypassLogged`
- **Rule:** the `Kill*` knobs apply only when the swap is off or the shader is missing from an
  older bundle.
- **Why:** with the stable shader the layers no longer swim, so the full vanilla look returns —
  keeping the knobs applied would permanently remove the border flame and crosshair for no reason.
- **Confidence:** high

---

## 11. Wall fade (Core.WallSegmentFade + Compat.WallFadeDisable)

### The GLOBAL `ToggleWallFade` is pinned to 0 UNCONDITIONALLY; MPBs re-open the gate per renderer
- **Where:** `Compat.WallFadeDisable.Postfix` (postfix on `Main.Update`),
  `WallSegmentFade.FadeDriver.Apply` (`_mpb.SetInteger(ToggleWallFadeId, 1)`)
- **Rule:** the global stays 0 regardless of `[Compat] WallFade`; only walls the mod decided to
  fade get an MPB that re-opens the gate, together with a SUBSTITUTED constant occlusion map.
  Unity property precedence is MPB > material > global.
- **Why:** two hardware rounds. (1) The fade condition samples the screen-space
  `_TilesOcclusionMap`, rendered per frame by `TilesOcclusionGenerator`'s CommandBuffer on the
  PARKED game camera — valid only for that camera's viewpoint, so an open global gate makes
  every wall sample a wrong-viewpoint map on the head camera. (2) Even with a correct
  head-camera map, the per-pixel screen-space discard is unusable in VR: fast head movement pops
  PARTS of walls in and out. With the global at 0, every untouched wall renders bit-for-bit solid.
- **Established by:** `c22c51c` / `8567af4` (pin) → `b627324` feat(wallfade): whole-wall VR see-through with hysteresis, replacing per-pixel occlusion feed
- **Breaks if:** the global pin is made conditional on the toggle, or the per-renderer MPB path
  is replaced by re-opening the global.
- **Confidence:** high

### `Main.Update` postfix, not a chase of every setter
- **Where:** `WallFadeDisable.TargetMethod`, `WallFadeDisable.Postfix`, `WallFadeDisable.Degrade`
- **Rule:** re-assert 0 after the game's own logic runs, every frame; the whole patch degrades
  to a strict no-op (TargetMethod returns null) if `Main`/`Main.Update` cannot be resolved.
- **Why:** the global is natively asserted to 1 from `Main.Start`,
  `ActivateWallFadeInGame.Start/Update` and `ToggleWallTransparencyGlobal.Update` — the last of
  which re-asserts every frame and would race a one-shot.
- **Confidence:** high

### Held-faded delivers map `r=1, a=0` and the material's AUTHORED cutoff — never `_Cutoff = 1.1`
- **Where:** `WallSegmentFade.FadeDriver._occludedTex`, `Segment.HeldCutoff`,
  `CollectWallFadeInfo` (clamps to 0.05–0.95), `Apply` (the `seg.Fade >= 1f` branch)
- **Rule:** constant `r=1, a=0` makes the map term `m = 1 - r = 0` view-independently (`a = 0`
  fails the reversed/conventional depth compare for every visible fragment), and `_Cutoff` is
  the material's own "Mask Clip Value".
- **Why:** the earlier held state forced `m = 1` with `_Cutoff = 1.1`, which on the HIGH shader
  makes `A = B = 1` and `clip = -0.1` everywhere — a TOTAL discard that erased the base course
  (the user's bug). The flat game never drives `_Cutoff` at all; it only sets the global to 1
  and rasterizes revealed-room footprints into the map, which over a room interior reads
  `occ.r ≈ 1`. Driving exactly that value lets each variant's own foundation-band terms survive:
  LOW discards only above its hard object-Y 0.4 gate; HIGH's world-Y ramp keeps the foundation
  gradient solid (S=1 ⇒ noise × 0) and discards the upper wall. The clamp matters: `c = 0`
  disables the LOW discard, `c ≥ 1` kills the HIGH foundation band.
- **Established by:** `d38e551` (the R2 held state) → `8f2ad63` fix(wallfade): keep the foundation band when a wall is held faded (flat-game look)
- **Breaks if:** "simplified" back to one `a=1, _Cutoff=1.1` MPB, or the cutoff clamp is removed.
- **Confidence:** high

### The residual HIGH-variant view coupling is ACCEPTED and provably irremovable
- **Where:** `WallSegmentFade` class doc ("IMPOSSIBILITY NOTE"), `LogStateFlip`
- **Rule:** do not attempt to strip the screen-radial vignette from the held state.
- **Why:** the world-Y band term and the vignette are SUMMED inside one scalar `S` before the
  single cutoff compare, and every vignette coefficient is a DXBC immediate literal. The only
  strictly view-independent HIGH deliveries are `m = 1` constants — i.e. whole wall visible, or
  the total discard that erased the foundation. The per-wall fade DECISION stays CPU-side and
  view-independent; only the shading has this game-native residue.
- **Established by:** `d38e551`
- **Confidence:** high

### The floor plane is TILE-ANCHORED; `Renderer.bounds` is trusted for XZ only
- **Where:** `FadeDriver.Rescan` (`_floorYByRenderer` from `TilesOcclusionVolume.CentralTile`),
  `_roomFloorY`, `_roomFloorAnchored`, `FloorSampleEpsilon` (0.05), the `!ABOVE-WALL` /
  `!UNANCHORED` diagnostic tripwires
- **Rule:** sample height = `CentralTile.transform.position.y + 0.05`; rooms without a volume
  match fall back to the median anchored height, then to the bounds top — with tripwires.
- **Why:** the round-6 hardware log carried `!ABOVE-WALL` on EVERY line. `m_RoomRenderers` are
  the game's top-down occlusion-map PROXY meshes: only their XZ footprint matches the tiles,
  and their AABB tops sat ~9 wu above the actual tile plane (sampY 9.05 vs wall tops ≤ 3.67).
  Every head→sample ray ran entirely ABOVE every wall box, so blocked counts were permanently 0.
  The one frame where the head dipped below instantly read 0.69 — proving the ray math fine and
  the FRAME wrong.
- **Established by:** `614f4d8` fix(wallfade): tile-anchored floor plane + per-wall room-coverage fraction
- **Breaks if:** `Renderer.bounds.max.y` is used for anything but the fallback, or the tripwires
  are removed.
- **Confidence:** high

### The blocked test uses `max(thicknessEps, 5 % of distance)` — a pure percentage was the bug
- **Where:** `FadeDriver.BlockedFraction`, `BlockEpsDistFraction` (0.05),
  `BlockEpsMinWorld` (0.10) / `BlockEpsMaxWorld` (0.90), `Segment.BlockEps` (half the wall's
  smaller horizontal AABB extent, clamped)
- **Rule:** a sample counts as blocked when the wall AABB entry precedes it by
  `max(halfThickness, 0.05 × dist)`, OR the sample lies inside the AABB. Head inside the AABB
  counts as 1.0.
- **Why:** the round-4 "88 % of distance" rule discarded exactly the near-edge first-row samples
  that carry the whole signal (at diorama viewing angles their AABB entry lands at 90–99 % of the
  sample distance). A pure thickness epsilon was still too strict for long grazing rays. The
  upper clamp exists because an L-shaped corner run has two large horizontal extents.
- **Established by:** `cb5f40d` fix(wallfade): scale-correct retune — in-band sample heights, epsilon blocked test, per-room coverage
- **Breaks if:** either term is dropped, or the clamps are removed.
- **Confidence:** high

### The frustum cull applies to the NUMERATOR only; the denominator is the room's WHOLE grid
- **Where:** `FadeDriver.BlockedFraction` (`blocked / (float)total`), `UpdateSampleVisibility`,
  `_roomSampleStart` / `_roomSampleCount`, `FrustumMargin` (0.20)
- **Rule:** a floor point outside the view cannot be "hidden by the wall", so it is excluded from
  the numerator; the denominator stays the room's full grid.
- **Why:** the number must read literally as "this wall hides X % of the room's floor" so the
  VR-menu stepper values keep their plain meaning. Per-wall (not global) denominators exist
  because a single global fraction diluted the signal across rooms. The 0.20 viewport margin
  also covers the per-eye-vs-mono frustum skew (the visibility test uses the MONO matrices).
- **Established by:** `cb5f40d`, `614f4d8`
- **Breaks if:** the denominator is frustum-culled too, or the margin is tightened.
- **Confidence:** high

### Asymmetric hysteresis: 0.2 s in, 2.5 s out after a real perspective change, 7 s when only rotating
- **Where:** `FadeDriver.Tick` (dwell selection), `EnterDwellSeconds` (0.20),
  `WallFadeTuning.DwellMoved` / `DwellStationary`, `ReevalArmSeconds` (3),
  `HeadMoveReevalMeters` (0.18), `UpdatePerspectiveState`
- **Rule:** fade-IN is prompt; fade-OUT is deliberately delayed, and much longer when the head
  only ROTATED. "Perspective changed" = `RigPoseVersion` bump (recenter/rig rebuild), rig-root
  motion beyond epsilon (world-grab/snap-turn), a room-bounds shift, or real head TRANSLATION
  > 0.18 m measured in **tracking space**.
- **Why:** walls re-solidified as soon as the user looked slightly away. Head translation must be
  measured in tracking space (metres, scale-independent) — a world-space threshold changes
  meaning with `WorldScale` (11–20 wu per metre). `DwellStationary` is clamped never below
  `DwellMoved`, and `Off` never above `On`, so a hand-edited config cannot invert the trigger.
- **Established by:** `42315d0` fix(wallfade): view-coverage metric + perspective-anchored hysteresis — rotation no longer un-fades
- **Breaks if:** made symmetric, or head movement is measured in world space.
- **Confidence:** high

### Fade 0 removes the MPB entirely
- **Where:** `FadeDriver.Apply` (`if (seg.Fade <= 0f) … SetPropertyBlock(null)`),
  `ClearAllBlocks`
- **Rule:** an unfaded wall carries NO property block.
- **Why:** with the global pinned 0, an untouched renderer is bit-for-bit today's solid wall —
  that is the guarantee the whole design rests on.
- **Confidence:** high

### Faded segments re-apply their MPB EVERY frame
- **Where:** `FadeDriver.Apply` (loop over `seg.Renderers`, `lostRenderer` → `_nextRescan = 0f`)
- **Rule:** re-applied while faded; a null renderer triggers a prompt rescan.
- **Why:** Apparance may regenerate wall renderers mid-fade.
- **Confidence:** high

### The noise texture is RANK-FLATTENED to a uniform histogram
- **Where:** `FadeDriver.EnsureTextures` (`Array.Sort(order, …)` then `Lerp(0.06f, 1f, rank/n)`)
- **Rule:** value noise, then rank-flattened over [0.06, 1]; low frequency (~5 cells);
  `a = 0`; `makeNoLongerReadable: true`.
- **Why:** the `_Cutoff` sweep must dissolve at a constant AREA rate — raw Perlin is
  bell-distributed, so the dissolve would stall then rush. Low frequency keeps the left/right-eye
  screen-space patterns correlated (they differ only by disparity).
- **Breaks if:** the flattening is dropped as "unnecessary maths".
- **Confidence:** high

### `MaxTotalSamples` (96) is a budget with a graceful per-room degradation
- **Where:** `FadeDriver.RebuildSamples` (grid 4→3→2→1 by room count; over-budget rooms get 0)
- **Rule:** samples are room-CONTIGUOUS; each room's `[start, count)` range doubles as its
  fraction denominator.
- **Why:** the contiguity is what makes the per-room denominator a slice rather than a lookup.
- **Confidence:** high

### Scene load resets the rescan, not the blocks
- **Where:** `FadeDriver.OnSceneLoaded` (`_nextRescan = 0f; _builtRoomCount = -1; …`)
- **Rule:** old renderers die with their scene, so blocks need no explicit clearing.
- **Why:** scenario scenes are additive and walls/volumes stream in. Note the broader lesson
  from `7246907`: **scene-load is not a valid trigger for anything that inspects scenario
  content** — Gloomhaven builds the map procedurally AFTER scene load, which is why the rescan
  is also interval-driven and count-triggered
  (`gen.m_RoomRenderers.Count != _builtRoomCount`).
- **Confidence:** high

---

## 12. Compat

### `InitialInputSkip` invokes the game's OWN dismiss, gated on the window being open
- **Where:** `Compat.InitialInputSkip.TargetMethod`, `Postfix`, `IsWindowOpen`, `Degrade`
- **Rule:** postfix `InitialInputScreen.Update`; fire `SelectInputDevice(false)` only while
  `Window.IsOpen`; `isGamepad = false` matches the mod's virtual-mouse scheme. Everything is
  reflection-resolved so a renamed/removed type makes the patch a strict no-op.
- **Why:** it does precisely what a real keypress would do at the same call site, so
  `UINavigation.StateMachine` advances to the main menu exactly as vanilla. Firing without the
  open gate dismisses a screen that is not accepting input yet.
- **Established by:** `a538ad3` + `57309c5` (the wiring commit)
- **Breaks if:** the open gate is dropped, or the private method is replaced by a hand-rolled
  state-machine advance.
- **Confidence:** high

### Compat kill-switches are data-driven by TYPE NAME and re-applied per scene load
- **Where:** `CompatModule._typesToDisable`, `ApplyKillSwitches`, `OnSceneLoaded`,
  `ResolveType`, `_disabled` (restore list)
- **Rule:** resolve by full name across loaded assemblies — no compile-time reference to
  `Unity.Postprocessing.Runtime` / `ThirdParty`; re-disable on every scene load; re-enable
  everything on shutdown.
- **Why:** the mod must build and run without those assemblies present. Scenario scenes are
  additive, so components reappear.
- **Breaks if:** converted to typed references, or the restore list is dropped.
- **Confidence:** high

### `CompatModule` is registered LAST among the FEATURE modules
- **Where:** `Plugin.RegisterModules`
- **Rule:** Core first (XR bootstrap), Compat last among the feature modules (fixups on top of
  everything else). `Core.DevModule` is appended *after* `CompatModule` and does not violate
  this: `DevModule.Init` returns immediately unless `[Dev] Enabled`, and even then it only adds
  a `DevConsole` overlay GameObject — it applies no fixups, patches nothing and touches no game
  state, so it cannot get between Compat and anything.
- **Why:** the kill-switches and the wall-fade pin must apply over whatever the other modules
  installed.
- **Breaks if:** a module that mutates game state is inserted after `CompatModule`, or
  `RegisterModules` is reordered to make the older, imprecise wording literally true — the
  CODE is right and the wording was wrong (`REVIEW-Hands-Board-Core.md` §P4.3). Reordering
  module init for a cosmetic match is a Tier-3 change.
- **Confidence:** high
- **Corrected:** Batch C. The earlier text said "`CompatModule` is registered LAST" full stop,
  which is false at HEAD (`DevModule` follows it) and would have led a reader to "fix" the code.

---

## 13. Harmony patch surface (a frozen contract with a game we cannot change)

> Every patch target below is a method in `GH.Runtime.dll` we do not control. Patch TARGETS,
> patch TYPES (prefix/postfix/skip) and the gate each patch consults are **out of scope for this
> refactor** (CHARTER §1). `docs/PATCH-INVENTORY.md` predates several of these — treat this
> table as the current inventory for the subsystems in scope.
>
> All patches go through the single shared `Harmony("dev.gloomhavenvr")`
> (`VRSession.Harmony`, created in `Plugin.Awake`) and are removed collectively by
> `Plugin.OnDestroy → UnpatchSelf()`. Patch classes are applied by an explicit
> `Harmony.PatchAll(typeof(X))` in a module `Init` — a class that is not listed there is inert
> (see §9, "A patch class that is written but not REGISTERED is inert").

### Owned by Board (`BoardModule.Init`)

| Patched method | Class / member | Type | What must stay true |
|---|---|---|---|
| `MF.FindInteractableAtMousePosition(bool, LayerMask)` | `MF_FindInteractableAtMousePosition_Patch.Prefix` | prefix, conditional replace | Uses the CALLER's mask argument; mirrors `GetComponentInParent<CInteractable>()`; returns **true** (vanilla) whenever `BoardPick` is inactive. The single world-pick choke point. |
| `InputManager.get_CursorPosition` | `InputManager_CursorPosition_Patch.Prefix` | prefix, conditional replace | Projects the VR pick through the HEAD camera; a miss returns the off-screen point, not false. Vanilla when `BoardPick` is inactive. Target is 208 B IL — explicitly not inline-endangered. |
| `UIManager.get_IsPointerOverUI` | `UIManager_IsPointerOverUI_Patch.Prefix` | prefix, conditional replace | Only while `BoardPick.Active`; the predicate must remain IDENTICAL to `BoardClickDriver.TickFar`'s (`Ray.HasFreshUiHit \|\| Poke.HoveredUi`). |
| `Controller.CommonLoop(bool)` (private) | `Controller_CommonLoop_Patch.Postfix` | postfix | Verbatim double-click bookkeeping and verbatim tail; always consumes the pending click; yields to a real mouse click (`if (__result) return;`). Never call `s_Callback`. |
| `WorldspaceStarHexDisplay.Update` | `HexHoverClear.Postfix` | postfix | MUST be a postfix (the method's tail re-activates the star). Scope is `s_CursorHighlightedStar` + the two hover info panels only. Gated on `BoardPick.InScenario`. |
| `WorldspaceStarHexDisplay.Update` | `Placement_UpdateGate_Diagnostics.Prefix` | prefix (observe) | Change-deduped snapshot; silent outside `WaitingForCardSelection`; the `Interactable()` probe runs only while display == CharacterPlacement. Marked TEMPORARY — see §14. |
| `WorldspaceStarHexDisplay.HighlightSelectedPlacementHex` | `Placement_Hover_Diagnostics.Postfix` | postfix (observe) | Logs only on `s_PlacementTile` RESULT change. Marked TEMPORARY. |
| `Choreographer.TileHandler` | `Placement_Click_Diagnostics.Prefix` | prefix (observe) | Never alters state; logs the three operands of the placement branch. Marked TEMPORARY. |
| `HexSelect_Control.ProjectorMaterialAdjustment` (private) | `HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch.Postfix` | postfix | The ONE method that writes every highlight material property, including after each material re-creation. Body fully try/caught; error log capped. |
| `ActorBehaviour.Update` (private) | `ActorBehaviour_HeldTransform_Patch.Update_Prefix` | prefix-skip | Skips only for actors in `HeldFigures` ∪ `NetHeldFigures`. Whole-method skip is required (see §8). |
| `ActorBehaviour.LateUpdate` (private) | `ActorBehaviour_HeldTransform_Patch.LateUpdate_Prefix` | prefix-skip | Same gate. `FixedUpdate` deliberately NOT patched. |
| `ActorBehaviour.SetHilighted(GameObject, bool)` | `ActorBehaviour_HeldTransform_Patch.SetHilighted_Prefix` | prefix, conditional replace | The only place the game SetActives `m_Hilight`. For held actors the intent is RECORDED and re-applied on release, not dropped. |
| `InitiativeTrackPlayerAvatar.OnClick(InitiativeTrackActorBehaviour)` | `InitiativeTrackPlayerAvatar_OnClick_Guard.Prefix` | prefix, conditional block | Must stay on the PLAYER override (the human-click seam). Enemy avatars run the un-overridden base, and every programmatic select calls `InitiativeTrack.Select` directly. |

### Owned by Core/Events (`VREventsModule.Init`)

| Patched method | Class / member | Type | What must stay true |
|---|---|---|---|
| `Choreographer.ProcessMessage(CMessageData)` (private) | `Choreographer_ProcessMessage_Patch.Postfix` | postfix (observe) | Runs inside Choreographer.Update's 8 ms/frame pump — the postfix must stay CHEAP and must never throw (`VREvents.Invoke` guards subscribers). Never alters game state. |
| `Choreographer.SetChoreographerState(...)` | `Choreographer_SetChoreographerState_Patch.Postfix` | postfix (observe) | Observe only. |
| `UIManager.ToggleLockUI(bool)` | `UIManager_ToggleLockUI_Patch.Postfix` | postfix (observe) | The ref-counted `RequestToggleLockUI` funnels through this method, so one postfix sees every lock/unlock; the bool is the EFFECTIVE state. |
| `UIWindow.EvaluateAndTransitionToVisualState(VisualState, bool)` (protected virtual) | `UIWindow_Transition_Patch.Postfix` | postfix (observe) | The single visibility choke point every Show/Hide/starting-state path funnels through; no subclass overrides it. Edge-triggered (Show/Hide early-out on the current state). |

### Owned by Compat (`CompatModule.Init`)

| Patched method | Class / member | Type | What must stay true |
|---|---|---|---|
| `Script.GUI.SMNavigation.InitialInputScreen.Update` (reflection-resolved) | `InitialInputSkip.TargetMethod` / `Postfix` | postfix | `TargetMethod` returning null must degrade the whole patch to a no-op. Fire only while `Window.IsOpen`. |
| `Main.Update` (reflection-resolved) | `WallFadeDisable.TargetMethod` / `Postfix` | postfix | Re-asserts the global `ToggleWallFade = 0` EVERY frame, unconditionally (not gated on `[Compat] WallFade`). Degrades to a no-op on resolution failure. |

### Cross-module contention rules that must survive the refactor

- **`InputManager` is touched by two modules on DIFFERENT methods** — Board patches
  `get_CursorPosition`; WorldUI patches the gamepad-mode setters. No interaction.
- **`WorldspaceStarHexDisplay.Update` carries two Board patches** (`HexHoverClear` postfix,
  `Placement_UpdateGate_Diagnostics` prefix). They are different patch types on the same target
  and must stay in different classes — merging them would tie the diagnostics' lifetime to the
  fix's.
- **`TakeDamagePanel` is patched by two modules** (WorldUI `TakeDamagePanelSafety`, Cards
  `DamageFlowPatches`) on overlapping mouse-enter/exit members. Out of scope here, but any
  Board/Core change that alters hover routing can surface there.
- **Trigger** is disjoint by mode/state: `BoardClickDriver` far click vs FlatScreen pointer vs
  card/figure grabs — arbitrated by `Ray.HasFreshUiHit`, `Grabber.Held` and `Poke.HoveredUi`.
- **Grip**: `ProximityGrabber` (per hand) > `WorldGrab` (Rig) for OBJECT grabs; world-grab
  locomotion itself is on `primary2DAxisClick` and is explicitly allowed while a hand holds a
  figure/card (`925002e` / `95ae86f`) — the two inputs never contend.
- **Thumbstick**: `AoeControl` reads X only in `BoardTargeting`; SnapTurn reads X elsewhere;
  `RayUguiDriver.TickStickScroll` reads Y only while hovering a `ScrollRect`.
- **Face buttons**: recenter chord = B+Y on BOTH hands (Rig); settings chord = A/X on the
  NON-dominant hand (WorldUI); `BoardPing` = A on the DOMINANT hand. No overlap.

---

## 14. Cross-cutting patterns (recognise these before "simplifying")

1. **Level, never latch.** `RayInteractor.Active`, `VRModeStateMachine.InteractorsFor`,
   `VRPresenceWatch.UserPresent`, `BoardClickDriver.ArmPlacementTile`,
   `ProximityGrabber.HealDeadHeld`, `FigureGrabbable.AuthoritativeCellChanged` all replace an
   edge/latch with a per-frame re-derivation from live facts. Any change that caches one of
   these across frames is a regression.
2. **Postfix the game's own single writer.** `HexHoverClear` (Update's tail re-activates the
   star), `HexHighlightFix` (the game re-creates the material), `WallFadeDisable`
   (`ToggleWallTransparencyGlobal.Update` re-asserts the global), `Controller_CommonLoop_Patch`.
   Patching anything upstream of the re-assertion silently no-ops.
3. **Fake-null discipline.** `UnityEngine.Object` equality makes two DESTROYED objects compare
   EQUAL and a destroyed object compare equal to null. Hence: instance-ID refcounts
   (`UguiHoverTracker`), `ReferenceEquals` removal (`UguiPointer.RemoveByRef`), `is null` vs
   `== null` (`UguiHoverTracker.Release`, `DeliberatePokeSurfaces.Unregister`,
   `DepthPortraitPicks.Unregister`, `FigureRingSuppressor.Restore`), keeping the destroyed
   reference for `Dictionary.Remove` (`VRCameraPolicy.PruneDead`, `UguiPokeSurfaces.PruneDeadKeys`,
   `WallSegmentFade.Rescan`), and the `_origParent != null ? _origParent : null` unwrap in
   `FigureGrabbable.Restore`. None of these are noise.
4. **The diagnostic was true and useless.** `HandGhost` (a tint written into a shader that
   cannot blend), `FigureHighlight` (an `_EmissionColor` the Amp shaders do not expose),
   `WallSegmentFade` (blocked counts of 0 from a sample plane above every wall). Later code logs
   the APPLIED EFFECT (shader swapped y/n, ZTest applied, cutoff + variant, `!ABOVE-WALL`), not
   the attempt. Keep it that way.
5. **An API's nominal meaning ≠ its shipped-data meaning.** `QualitySettings.skinWeights` is a
   CAP, not a default; `HandRig.Wrist` is not necessarily a socket; `Renderer.bounds` on the
   game's occlusion proxies is not geometry; `CameraController.s_CameraController` is not
   "a scenario exists".
6. **Suppressing a renderer also suppresses `CommandBuffer.DrawRenderer` for it.** Two black-sky
   rounds. Reorder by render queue instead.
7. **A separate wiring commit is a real failure mode.** Twice (`8567af4`, `57309c5`) a written
   patch was inert because no module registered it. Tier-1 file moves must carry the
   `PatchAll(typeof(X))` reference with the class.
8. **Scene load is not a valid trigger for anything that inspects scenario content** — the map
   is built procedurally AFTER scene load and keeps spawning during play. Use interval + count
   triggers (`WallSegmentFade.Rescan`, `MixedReality.HideSkyGeometry`, `SkyBackdrop.EnsureAcquired`).

---

## 15. Suspected vestigial

> **Nothing here is deleted.** Each item is flagged with the reasoning and the §5 check it still
> needs (Harmony surface / Unity message / reflection / config key / log grep token / debug menu).
> Several are *deliberate* non-actions and must NOT be removed at all — those are marked KEEP.

### KEEP — deliberate, documented non-actions

- **`FigureGrabbable.ApplyRenderOnTop` unreachable block** (+ `HeldRenderQueue`,
  `_heldRenderers`, `_origSharedMats`, `RestoreRenderers`). Reverted by `4ef1d76` because the
  render-queue bump punched held minis through walls and persisted after release. The block is
  retained behind `#pragma warning disable CS0162` for quick re-enable, and the call sites keep
  the grab/release symmetry. Deleting it removes the record of a tested-and-rejected approach.
- **`MixedReality.KeepMenusUnclipped`** — empty body kept for its `ModalFallback` call sites;
  the comment above it records why the sky is no longer disabled for a floated menu (`b84817d`).
- **`FingerCurler.StyleCurlScale`** — all three entries are 1.0. The array + comment record why
  the earlier 0.72/0.85 clamps were removed once the round-3 adaptive digit caps landed
  (`f31264f`), and it is the tuning hook for a future style whose rest pose over-closes.
- ~~**`PalmGate.UseDevicePalmNormal`** — genuinely vestigial *as a behaviour* since roll gate v4
  (the gate always reads `HandRig.Root`), but it is still written every frame by
  `CardsDriver` (`gate.UseDevicePalmNormal = !_gateHand.IsSimulated;`). The field is documented
  as kept so that assignment stays source-stable. Removing it is a two-file change that touches
  Cards (out of scope) — propose in `PLAN.md`, do not do it inline.~~
  **[verified 2026-09-08] DONE and doubly stale.** `grep -rn "UseDevicePalmNormal" src/` returns
  **nothing**: both the field and the `CardsDriver` write are gone, and `PalmGate`'s class doc
  records the retirement (*"v3 selected a raw device pose through a flag on this class; the flag
  was retired once nothing read it"*). The parenthesis was also wrong on its own terms — the gate
  does **not** "always read `HandRig.Root`"; see the corrected §5 entry.
- **`Plugin.Experimental3DMap`** — bound but explicitly UNIMPLEMENTED. It is a **config key**
  (§5): removing it silently drops a user's persisted setting, and the long description is the
  only record of the test-#8 decision to keep the campaign map flat. `VRRigDriver` carries the
  matching comment.
- **`RayInteractor.Mask` default `Physics.DefaultRaycastLayers`** — overwritten every frame by
  `BoardDriver.SyncRayMask` while a `Controller` exists, but it is the value used in menu scenes
  and in `[Dev]` mode. Not dead.

### Marked TEMPORARY by their own authors — needs a user decision, not a unilateral delete

> **DECIDED (Batch D): the `PlacementDiagnostics` trio is KEPT** until the next placement
> question. Removing them needs a clean HMD placement pass — evidence, not a code argument —
> and a refactor may not spend the user's headset time (CHARTER §1). The decision and the
> measured cost (one raycast per frame, only while `WaitingForCardSelection` **and**
> display == `CharacterPlacement`; all three verified observationally pure) are now recorded
> at the top of the file itself. **Do not re-raise this as a fresh finding.**

- **`Board/Patches/PlacementDiagnostics.cs`** — all three classes
  (`Placement_Hover_Diagnostics`, `Placement_UpdateGate_Diagnostics`, `Placement_Click_Diagnostics`)
  carry "TEMPORARY … remove after the placement flow is confirmed on HMD". The root causes they
  were written for are fixed (`96d8351`, `a6e2739`, `2253b0c`). **However:** they are Harmony
  patches on `WorldspaceStarHexDisplay.Update` / `HighlightSelectedPlacementHex` /
  `Choreographer.TileHandler`, and their `[Placement]` log lines are grep tokens the hardware
  reports use. The `Update` prefix also calls `__instance.Interactable()` (one raycast) while
  display == CharacterPlacement — a real, if small, side effect on the game's own pick path.
  → Propose removal in `PLAN.md` for the user to confirm the flow has been re-verified on HMD.
  Confidence that they are removable: **medium**.
- **`TargetingUx._suppressionLogged`** — one-shot log only; harmless either way.

### Genuinely unreferenced (verified by full-repo grep) — Tier 0 candidates after the §5 check

- **`HandGhost.Engaged`** and **`HandGhost.RendererCount`** — defined, never read anywhere in
  `src/`. Not a Unity message, not reflected, not a config key. Low risk. Confidence **high**
  that they are unreferenced; note only that `HandGhost` is instantiated by `HandGhosts`,
  `AvatarMirror` and `RemoteAvatar`, so re-check those before deleting.
- **`HandRig.PalmNormal`** — defined, never read. Part of the **FROZEN Phase-2 API**
  (`docs/INTERFACES-P2.md`) documented as the hand transform contract, so removing it is an
  interface change even though nothing consumes it today. Confidence **medium**.
- **`HandPose.OpenPalm`** — assigned in `VRHand.UpdatePoseClassification`, never compared
  against. `HandPose.Point` and `.Fist` are read (`UpdateCurlTargets`, `DevConsole`). The enum
  is frozen Phase-2 API; the *assignment* is what makes the classification total. Do not remove
  the enum member. Confidence **medium**.
- **`FigureGhosts` `Ghost.Pos`/`Ghost.Rot`** are read every Tick — NOT vestigial despite looking
  like inert snapshot state.

#### Added in Batch D — found by an independent sweep, NOT on the review's list

A comment-stripped sweep for members whose identifier occurs exactly once in the whole
compiled-source corpus turned up four more. **None were removed**: all four are the same shape
as `HandRig.PalmNormal` — documented contract or API statements that cost one line — and
CHARTER §2 puts the burden of proof on the change. Recorded so the next pass does not redo the
search, and so a future pass does not delete them without an argument:

- **`Hands.Interact.UguiPointer.IsPressed`** (`_pressed != null`) — the natural inspection hook
  for a pointer-state debug line; the class is otherwise entirely private state.
- **`Hands.VRHand.IndexTouchSupported`** — the public statement of a capability the trigger
  fallback chain (§5, "index touch source:") logs about. Removing it hides *why* the fallback
  chain exists.
- **`Board.BoardPick.TryGetCursorWorld`** — documented P5/MISSION A.1 API. The live path is
  `ResolveCursorWorld → TryGetCursorScreenPoint`; this world-space accessor has no caller. Note
  `RayInteractor.cs`'s do-not-resurrect comment used to point at it (corrected in Batch C).
- **`Board.FigureGrab.FigureGrabConfig.HeldOffset`** — the canonical RIGHT-hand held pose; its
  doc is where the left-hand mirroring rule is stated. `HeldOffsetFor(side)` is what runs.

Caveat on the sweep: it cannot see members whose name collides with an unrelated symbol
elsewhere (that is why `HandGhost.Engaged` did not appear — `WorldUI/HexHintFacing` has an
unrelated `Engaged` field). It is a lower bound, not a complete list.

### Looks redundant, is not — do not "dedupe"

- **`VRLayers.Apply` calls in `HandVisuals.Build`, `RayInteractor.CreateVisuals`,
  `SkyBackdrop.EnsureResetObject`** vs the tree-wide call in `HandsDriver.Build`. See §10 —
  these objects are created after the sweep.
- **`ProximityGrabber.ReachMeters` (0.13) and `FigureGrabDriver.ReachMeters` (0.13)** — a
  deliberate mirror across an assembly-internal boundary; the comment says so. Merging them into
  one shared constant is Tier 2 and *safe*, but only if the comment travels with it.
- **`PokeInteractor.ContactDepth`/`ReleaseDepth`-equivalents in `BoardClickDriver`** (0.008 /
  0.02) — same values, different subsystems, deliberately mirrored so the near board click and
  the poke press arm/re-arm together. Same Tier-2 caveat.
- ~~**`SkyBackdrop.RemoveEffects` vs `FullReset`**~~ — **not a near-duplicate at all; struck so
  a future reviewer does not go looking.** `FullReset` **calls** `RemoveEffects` and then
  additionally forgets the sphere, the mechanism decision and the reset material. They are
  already correctly factored: there is no duplication to resist, only a split to preserve
  (`RemoveEffects` alone is the MR handover path and must NOT forget the mechanism decision —
  merging the two would re-run the shader property dump on every MR toggle). Verified at HEAD,
  Batch D; the difference is now stated at both methods in `SkyBackdrop.cs`.
- **`HeldFigures` and `NetHeldFigures`** — near-identical shapes, deliberately separate: one is
  owned by the local grab flow, the other is REPLACED wholesale by `Net/NetFigures`
  (`ReplaceWith`). The patch gate ORs both. Merging couples local grab lifetime to the wire.
- **`VRCameraPolicy.PruneDead` and `MixedReality.PruneDead`** — both called from
  `VRRigDriver`'s scene-load path; they prune different maps.

### Config entries that are bound but effectively dead (Batch E — kept, relabelled)

`PLAN.md` §Batch E listed legacy config entries in **Cards and WorldUI only**. There are 22 more
in this scope, none of them on any list. All are **kept bound** (unbinding drops the key from
every existing `.cfg`, CHARTER §5) and their descriptions now open with
`LEGACY — no effect, superseded by <X>.`:

- `Plugin` `[Hands]`: `GripPitchOffsetDegrees`, `HandLateralOffset`, `HandVerticalOffset`,
  `HandForwardOffset` + the 12 `{Glove,Plate,Arcane}{PitchTrimDegrees,LateralTrim,VerticalTrim,
  ForwardTrim}` entries. Superseded by the ABSOLUTE per-style seat keys
  (`[Hands] {Style}{GripPitchDegrees,LateralOffset,VerticalOffset,ForwardOffset}` in
  `dev.gloomhavenvr.hands.cfg`). Read exactly once, via `HandsConfig.LegacySeat`, as the bind
  default that seeds those keys; `StyleValue` prefers the per-style array whenever it exists,
  which is always after `HandsConfig.Bind`.
- `FigureGrabConfig` `[FigureGrab]`: `HeldScale`, `HeldOffsetForward`, `HeldOffsetUp`,
  `HeldOffsetSide`, `HeldTiltDegrees`, `HeldFaceYawDegrees`. Same pattern via `StyleOr`,
  superseded by `{Style}Held*` in the SAME file. `HeldUpright` is **not** legacy — it is a mode,
  not geometry, and stayed global deliberately.
- `Plugin` `[Hands] {Style}Scale` is **not** legacy either — `HandVisuals` and the settings
  panel read it live. The scale/trim split inside one loop is the trap here.

Note for the next sweep: a `.Value` grep does **not** find these, because the seed read is a
`.Value` inside the entry's own config file. Look for entries whose only reads are in their
declaring file.

### Log lines that are grep tokens, not debug residue (CHARTER §5)

**[verified 2026-09-08] This list is right about WHICH lines matter and was silent about the one
thing a successor needs to know before using it: SIXTEEN of the twenty-one print NOTHING at the
shipped log level, and one has no call site at all.** The list is unchanged in substance — every
token below is still a line the debug workflow depends on and none of them may be deleted or
reworded. What is added is the tier, because a token that cannot be read is not a token.

**Why.** ModBuild 331 made the log quiet, correctly and on the user's own request, by re-deciding
what each severity MEANS rather than by deleting anything (`Core/VRLog.cs`):

| method | tier it emits at | printed at the shipped default (`[General] LogLevel = Info`)? |
|---|---|---|
| `VRLog.Error` | Error | **yes** |
| `VRLog.Alert` | Warning | **yes** |
| `VRLog.Note` | Info | **yes** — the usual choice for a line a hardware round waits on |
| `VRLog.Warn` | **Debug** | no |
| `VRLog.Info` | **Debug** | no |
| `VRLog.Debug` | Debug | no |

`Defaults.Plugin.cs` ships `LogLevel = VRLogLevel.Info`. So **`VRLog.Info` does not print by
default**, and most of this list was written with it. The first hardware round after 331 came back
with fifteen mod lines and answered nothing; `scripts/check-hw-verify.py` was built for exactly
that failure — but it only enforces sites carrying a `// HW-VERIFY` comment, and **only three of
the twenty-one tokens below are marked**. The rest are outside every gate, which is precisely why
this section could go stale with the whole suite green.

**To capture any token marked "Debug" below, the tester must set `[General] LogLevel = Debug`.**

| # | grep token | emitter | tier | default? |
|---|---|---|---|---|
| 1 | ~~`Modal input-block ENGAGED/RELEASED`~~ | **none — 0 hits in `src/`** | — | **the line is gone.** It was deleted with the modal pick-block (§1); `RayInteractor` now calls it *"the former 'modal input-block'"* and logs `laser gating: …` in its place. |
| 2 | `ray ON/OFF — <reason>` | `Hands/Interact/RayInteractor.cs` `VRLog.Debug` | Debug | no |
| 3 | `uGUI hover ENTER/EXIT` | `Hands/Interact/UguiPointer.cs` `VRLog.Info` | Debug | no |
| 4 | `uGUI click:` | `Hands/Interact/UguiPointer.cs` `VRLog.Info` | Debug | no |
| 5 | `GRAB STATE heal:` | `Hands/Interact/ProximityGrabber.cs` `VRLog.Note`, **`// HW-VERIFY`** | Info | **yes** |
| 5b | `GRAB STATE heal:` *(same token, other subsystem)* | `WorldUI/Grab/PanelGrab.cs` `VRLog.Warn` | Debug | no — **one grep token, two tiers.** A capture at the default level shows the Hands half and silently omits the WorldUI half. |
| 6 | `grab refused —` | `Hands/Interact/ProximityGrabber.cs` `VRLog.Note`, **`// HW-VERIFY`** | Info | **yes** |
| 7 | `Ghost hand ON/OFF` | `Hands/HandGhost.cs` `VRLog.Info` | Debug | no |
| 8 | `FIST <side>` | `Hands/VRHand.cs` `VRLog.Info` | Debug | no |
| 9 | `squeeze released: peak raw grip=` | `Hands/VRHand.cs` `VRLog.Info` | Debug | no |
| 10 | `index touch source:` | `Hands/VRHand.cs` `VRLog.Info` | Debug | no |
| 11 | `Global skinWeights raised` | `Hands/HandsDriver.cs` `VRLog.Info` | Debug | no |
| 12 | `[Placement] …` | `Board/Patches/PlacementDiagnostics.cs`, `Board/BoardClickDriver.cs`, `Board/CameraArrivalGuard.cs` — all `VRLog.Info` | Debug | no |
| 13 | `LASER INFO SUPPRESSION` | `Board/TargetingUx.cs` `VRLog.Info` | Debug | no |
| 14 | `fade ON/OFF '<wall>' … [HIGH\|LOW]` | `Core/WallFade/WallSegmentFade.cs` `VRLog.Info` | Debug | no |
| 15 | `diag: vis … !ABOVE-WALL … !UNANCHORED` | `Core/WallFade/WallSegmentFade.cs` `VRLog.Info` | Debug | no |
| 16 | `SkyBackdrop mechanism =` | `Core/Environment/SkyBackdrop.cs` `VRLog.Info` | Debug | no |
| 17 | `MR: disabled sky/background renderer` | `Core/MixedReality/MixedReality.cs` `VRLog.Info` | Debug | no |
| 18 | `stable hex decal ZTest=` | `Board/HexHighlightFix.cs` `VRLog.Note`, **`// HW-VERIFY`** | Info | **yes** |
| 19 | `Session resumed after …` | `Core/VRPresenceWatch.cs` `VRLog.Info` | Debug | no |
| 20 | `Tick '<name>' threw and was ISOLATED` | `Core/Perf/TickGuard.cs` and `Net/Desync/DispatchGuard.cs` `VRLog.Error` | Error | **yes** |
| 21 | `IF THIS IS NOT THE COMMIT YOU EXPECTED` | `Plugin.cs` `VRLog.Note` | Info | **yes** |

**What NOT to conclude from this table.** Do not promote these lines to `VRLog.Note` in bulk. The
331 quiet was a user request and several of these fire per frame or per hover — promoting them
re-creates the flood 331 removed, which is why `check-hw-verify.py` refuses a marked site inside
`Update`/`LateUpdate`. The correct move is per round: when a hardware question depends on a token,
mark that ONE site `// HW-VERIFY` and move it to `VRLog.Note` in the same commit, and let the gate
hold it there. `ProximityGrabber.cs` shows the intended form — its marker comment cites this very
section by name.

---

## 16. Open questions for `PLAN.md`

Answered during Batches C/D/E; kept with their answers so the questions are not re-asked.

- ~~Retire the three `PlacementDiagnostics` patch classes?~~ **KEEP** until the next placement
  question — the blocker is a clean HMD pass, not a code argument. Decision and measured cost
  recorded at the top of `Board/Patches/PlacementDiagnostics.cs` and in §15.
- ~~**STILL OPEN — retire `PalmGate.UseDevicePalmNormal` together with the `CardsDriver`
  assignment.**~~ **[verified 2026-09-08] CLOSED.** It was done exactly as prescribed — both
  halves in one commit. `grep -rn "UseDevicePalmNormal" src/` returns nothing. The original text
  is kept below because the *method* is the reusable part: a field in one subsystem written from
  another cannot be retired by either owner alone, and the entry that says so is what made the
  single-commit removal happen. Original: *"Confirmed vestigial at HEAD (the gate never reads it;
  one write, in Cards). **Not done**: the two halves are in different subsystems worked by
  different workers, and removing the Hands half alone does not compile. Whoever removes the
  `CardsDriver` write must delete the field in the SAME commit."*
- ~~Extract the mirrored reach/depth constants into a shared location?~~ **NO** — replaced by
  `scripts/check-mirrors.sh` (Batch A). Merging would worsen the Hands↔Board layering. Every
  mirrored site now says so at the constant. Note the exposure is wider than the review stated:
  the fingertip radius has FOUR copies across four subsystems, not two, and the lint covers all
  four (re-verified in Batch C by sweeping every `const float ... = 0.008f`).
- ~~`docs/PATCH-INVENTORY.md` is stale (Phase 5, 17 patches).~~ **DONE** — generated from source
  by `scripts/patch-inventory.sh` (Batch A) and checked by `refactor-guard.sh`. It now reports
  34 classes / 56 methods. **Never hand-edit it**; regenerate. It records declaration LINE
  NUMBERS, so any comment edit inside a patch file requires a regenerate + commit.





