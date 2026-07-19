# Figures-in-Hand (grab a mini, inspect it, info floats next to it) + World-Grab → Thumbstick

Status: research + implementation plan (no `src/` changes made). Ready for `/gsd-plan-phase`.
Author: research+planning agent. Date: 2026-07-19.
Game refs are under `decompiled/`; mod refs under `src/GloomhavenVR/`.

Feature (user, paraphrased from German): grab the board minis and take them **in hand**
(Demeo-style), **no gameplay effect** — pure immersion / close inspection. **Animations
keep playing** while held. While held, show the figure's **info** next to it in hand — the
**same info shown on laser mouse-over today** — always **facing the player**, small offset so
it doesn't clip. Prerequisite: **world-grab moves to the thumbstick click** so pointing at a
figure and grabbing it never also grabs the world (one stick pressed = move the world, both =
zoom/rotate; otherwise identical to today).

---

## 1. Summary — how figures, info, grab, animation and world-grab work today

### 1.1 Figure representation (Q1)

Every board figure (player **and** monster — same rig) is a pair of MonoBehaviours plus a
logical actor, spawned by `Choreographer.CreateCharacterActor` (`decompiled/GH.Runtime/Choreographer.cs:787`):

- **`CharacterManager : MonoBehaviour, IControllable`** (`decompiled/GH.Runtime/CharacterManager.cs:19`)
  — the figure "identity" component on the model prefab. Fields:
  `public CActor CharacterActor` (`:82`), `public GameObject CharacterActorGO` (`:85`),
  `public float Height = 1.8f` (`:63`), `Renderer[] m_Renderers` (`:104`). Static reverse
  lookup `CharacterManager.GetCharacterManager(GameObject, …)` (`:346`) via `GetComponentInParent`.
- **`ActorBehaviour : MonoBehaviour`** (`decompiled/GH.Runtime/ActorBehaviour.cs:8`) — the
  transform/animation/loco driver. Fields: `m_RootGameObject` (`:41`),
  `m_AnimatedGameObject` (`:43`), `m_Animator` (`:45`), `m_WorldspacePanelUI`
  (the HP/conditions bar, `:15`), `m_Hilight`/`m_Base` (`:10`/`:12`), `Actor` (`:81`).
- **`m_Animator`** lives on a **child** of the model (Hero/Monster layer), found once in
  `ActorBehaviour.SetActor` via `MF.GetGameObjectAnimator(root)` (`ActorBehaviour.cs:114`;
  `MF.cs:135`) → `m_AnimatedGameObject = m_Animator.gameObject` (`:115`). Animation params are
  driven by `ActorBehaviour` (`SetFloat("RunBlend",…)` `:547`, `Play("Jump Into")` `:280`) and
  relayed by `ActorEvents` (`decompiled/GH.Runtime/ActorEvents.cs:4`).

**Collider on the figure = `CInteractableActor : CInteractable`** (`decompiled/GH.Runtime/CInteractableActor.cs:5`).
In `Start` it resolves `m_Actor = GetComponentInParent<CharacterManager>()?.CharacterActor` (`:25`).
This is the collider the board pick already hits.

**Mappings (all verified):**
- collider/GameObject → `CActor`: `GetComponentInParent<CharacterManager>().CharacterActor`
  (universal) or `CInteractableActor.m_Actor` — the mod already uses the latter in
  `src/GloomhavenVR/Board/BoardClickDriver.cs:117-120`.
- `CActor` → figure GameObject: `Choreographer.FindClientActorGameObject(CActor)`
  (`decompiled/GH.Runtime/Choreographer.cs:2248`) — dispatches player/enemy/summon/object.
  Mod already uses it in `src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs:253`.
- figure enumeration: `WorldspaceUITools.Instance._panelUIControllers[i]` each has
  `m_ObjectToTrack` (the figure GO) — the mod already iterates this in
  `src/GloomhavenVR/WorldUI/ActorBars.cs:105-112`.

**CRITICAL — the game hard-drives the figure transform every frame.** `ActorBehaviour.Update`
→ `DoTransform` writes `m_AnimatedGameObject/m_RootGameObject.transform.position =
m_LocoIntermediateTarget` **even when idle** (`ActorBehaviour.cs:477-478, :536`), and
`LateUpdate → ApplyMotion` re-asserts `m_RootGameObject.transform.position = position;
m_AnimatedGameObject.transform.localPosition = Vector3.zero;` (`:610-611`). **Any external
`transform.position` write on the real figure is overwritten the same frame.** To move the real
mini you must either drive `ActorBehaviour.ForceSetLocoIntermediateTarget/TeleportToLocation`
(`:245`/`:320`) or prefix-skip / disable `ActorBehaviour`. This is the load-bearing fact for the
animation decision (§1.4 / Q4).

### 1.2 The info tooltip today (Q2)

On hover, the game itself opens the stat window: `HoverRegisterer.Update` raycast
(`decompiled/GH.Runtime/HoverRegisterer.cs:36`) → `WorldspaceStarHexDisplay.DisplayCursorHoverStar`
(`:3200`) → `ShowActorStatPanelForTile(tile)` (`:3343`) → maps tile→actor via
`ScenarioManager.Scenario.FindActorAt(arrayIndex)` (`:3352`) →
**`Singleton<ActorStatPanel>.Instance.Show(actor)`** (`decompiled/GH.Runtime/ActorStatPanel.cs:440`,
takes any `CActor` — populates character AND monster stats, portrait, HP, conditions).
In VR this is fed by the VR board pick (`src/GloomhavenVR/Board/BoardPick.cs`) through the
`MF.FindInteractableAtMousePosition` / `InputManager.CursorPosition` patches, so laser
mouse-over already opens `ActorStatPanel`.

**The mod already physicalizes it.** `src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs`
watches `ActorStatPanel` (and `EnemyCurrentTurnStatPanel`) `UIWindow.onShown/onHidden`, converts
the panel to a world-space host via `CanvasConversion.Convert(…, pokeable:false)`, and in
`PlaceWatch` (`:203-236`) anchors it **beside** the actor's miniature (resolved via
`Choreographer.FindClientActorGameObject`, `:238-255`), billboarded to the head with a
`side*0.30 + up*0.10` offset — exactly the "face the player, small offset" behavior the feature
wants, already implemented for the board case. It is triggered both by the game's own hover and
by `VREvents.MiniaturePoked` (`StatPanelSurface.cs:90-103`, raised from
`BoardClickDriver.cs:122`).

→ For a held figure we **reuse this pipeline**: open `ActorStatPanel.Show(actor)` and give
`StatPanelSurface` a "held-figure anchor override" so it docks the converted panel beside the
**held** figure instead of the board miniature (same billboard math). One info pipeline, no new
canvas plumbing.

### 1.3 Grab infrastructure (Q3)

Frozen Phase-2 grab API, all present and reusable:
- `IGrabbable` (`src/GloomhavenVR/Hands/Interact/IGrabbable.cs`): `CanGrab`, `GrabWithGrip`,
  `OnGrab(hand)`, `OnRelease(hand, velocity)`. Companions `IGrabHighlight`,
  `IGrabbableHandFilter`.
- `GrabbableBehaviour : MonoBehaviour, IGrabbable` (`src/GloomhavenVR/Hands/Interact/VRInteractables.cs:167`)
  — registration + optional **snap-to-hand** (`AttachToHand` parents to
  `hand.Rig.GrabAnchor`, restores on release, `:232-265`) + `GetHeldPose(hand)` override
  (see the `VRCard` pinch pose, `src/GloomhavenVR/Cards/VRCard.cs:428-455` + per-frame
  `TickHeldPose` billboard `:493`).
- `VRInteractables.RegisterGrabbable(target, collider)` (`:68`) — explicit registry, iterated
  each frame.
- `ProximityGrabber` (`src/GloomhavenVR/Hands/Interact/ProximityGrabber.cs`): palm-reach
  (0.13 m) nearest-grabbable highlight + grab; `GrabWithGrip==true` grabbables always take the
  **grip** button (`:97-101`). Also exposes **`ForceGrab(target, releaseOnTriggerUp)`** (`:163`)
  — the **laser-pluck** entry the Cards driver uses for point-and-grab
  (`src/GloomhavenVR/Cards/CardsDriver.cs:885` etc.).
- Laser pick to hit-test a figure: `VRHand.Ray` (`RayInteractor`) exposes
  `TryGetPick(out PickPose)` with `PickPose.HitCollider` (`src/GloomhavenVR/Hands/Interact/IPickProvider.cs`),
  and `BoardPick.HitCollider`/`SourceHand` already give the current board pick collider each
  frame (`src/GloomhavenVR/Board/BoardPick.cs:96`).

### 1.4 Demeo reference (validates the design)

- Demeo's mini grab is `Prototyping.Grabbable` + `Boardgame.BoardPiece.BaseGrabbableMiniature`
  (`decompiled-demeo/Assembly-CSharp/Boardgame.BoardPiece/BaseGrabbableMiniature.cs:16`). It does
  **not** reparent the mini; it parents a throwaway anchor to the hand and hard-copies pose each
  frame (`GrabUpdate`).
- **Real vs. ghost:** if the move is legal it grabs the **real** piece; otherwise it grabs a
  **clone** — `GrabbableGhost.CreateGrabbableGhost` (`.../GrabbableGhost.cs:11`) — a pooled copy
  with its own kinematic body, and **`PieceGhostController.CopyAnimatorParameters`** copies the
  animator params from the source each build so the ghost keeps animating (`:238-247`). Our
  "no gameplay effect + animations keep playing" maps exactly onto Demeo's **ghost** path.
- **Info HUD:** `GrabbedPieceHudInstantiator.CloneCurrentHudState` (`.../Boardgame.Ui/GrabbedPieceHudInstantiator.cs:90`)
  clones health bar + `PieceStatsView`, parents it to the piece, and `GrabbedPieceHudController.Update`
  (`.../GrabbedPieceHudController.cs:46`) billboards it every frame and hides it when it faces
  away (`Vector3.Dot(camDir, forward) < 0.6f`). This is the "face the player, offset above the
  mini" behavior — which our `StatPanelSurface` billboard already reproduces.

### 1.5 World-grab & thumbstick today (Q5)

- `WorldGrab` (`src/GloomhavenVR/Rig/WorldGrab.cs`) currently engages on the **grip**:
  `UpdateGripOwnership` (`:158-175`) reads `hand.GripDown`/`hand.GripPressed` and only starts if
  the proximity grabber holds/highlights nothing (`:173`). One grip = `OneHand` drag
  (`ApplyOneHand` `:218`), both grips = `TwoHand` yaw+pinch-scale (`ApplyTwoHand` `:253`). Applied
  inversely to the rig root; all math is tracking-space anchored.
- `VRHand` (`src/GloomhavenVR/Hands/VRHand.cs`) reads `primary2DAxis` into `Thumbstick` (`:393-394`)
  but **does not read `primary2DAxisClick`** (the stick-push button) — it is listed in the verified
  `CommonUsages` set (`:42`) but never sampled. **This must be added.**
- `SnapTurn` (`src/GloomhavenVR/Rig/SnapTurn.cs`) uses the thumbstick **axis** (`hand.Thumbstick.x`,
  `:68`) for turning and already suppresses itself while
  `WorldGrab.Instance.IsHandGrabbing(hand)` (`:65`). AoE rotation in `BoardTargeting` also uses the
  axis (`AoeControl`). The stick **click** is a distinct button, so rebinding world-grab to the
  click does not collide with axis-based turn/AoE.

---

## 2. Recommended approach (per question)

**Q1 — identify the figure under the hand/laser.** Resolve `CActor` from the pick collider via
`collider.GetComponentInParent<CharacterManager>()?.CharacterActor` (universal; the mod already
uses the `CInteractableActor.m_Actor` variant). Enumerate/register grabbables by reusing the
`WorldspaceUITools.Instance._panelUIControllers` walk that `ActorBars` already does; each
controller's `m_ObjectToTrack` is the figure GO and carries the `CInteractableActor` collider.

**Q2 — info next to the held figure.** Reuse `ActorStatPanel.Show(actor)` + the existing
`StatPanelSurface` conversion. Add a **held-anchor override** to `StatPanelSurface` so, while a
figure is held, `PlaceWatch`/`ResolveActorAnchor` docks the converted panel beside the **held**
figure transform (billboard-to-head, `side*offset + up*offset`, same as the board case). This is
literally "the same info shown on mouse-over," now following the hand.

**Q3 — grab, reusing infra.** New `FigureGrabbable : IGrabbable` (implemented directly, **not**
`GrabbableBehaviour`, because we do not want the base's real-transform reparent — see Q4) with
`GrabWithGrip = true`, registered in `VRInteractables` against the figure's `CInteractableActor`
collider by a `FigureGrabDriver`. Two grab entries, both reusing existing code:
- **proximity** (reach out and close hand): handled automatically by `ProximityGrabber`
  (grip button, since `GrabWithGrip==true`);
- **laser point-and-grab** (the requested "point and grab"): `FigureGrabDriver` watches the
  far pick; on **grip-down** while the ray's `HitCollider` is a figure, call
  `hand.Grabber.ForceGrab(figureGrabbable, releaseOnTriggerUp:false)` — the same laser-pluck the
  Cards fan uses, releasing on **grip-up**.

Using the **grip** for figure-grab is only possible **because the world-grab rebind (Q5) frees
the grip.** Trigger stays the board click/select; thumbstick-click becomes world-grab.

**Q4 — animation while held: HOLD A VISUAL CLONE (recommended).** Because the game hard-writes the
real figure transform every `Update` and `LateUpdate` (`ActorBehaviour.cs:536, :610-611`), and
because the feature must have **zero gameplay effect** and keep animations playing, the safest
design — and the exact Demeo analog (`GrabbableGhost`) — is:
1. On grab, `Instantiate(actorBehaviour.m_AnimatedGameObject)` (the subtree that carries the
   `Animator` + skinned meshes) as a clone; strip/disable any gameplay scripts on the clone
   (`ActorBehaviour`, `CInteractableActor`, colliders) so it is purely visual.
2. Parent the clone to `hand.Rig.GrabAnchor`, apply a `HeldPose` (upright, scaled to a
   comfortable inspection size; watch the 100× armature scale — see memory `vr-render-gotchas`),
   and per-frame billboard is unnecessary (it rides the hand) but keep it upright.
3. Each frame, **mirror the real Animator onto the clone**: copy parameters + per-layer
   `GetCurrentAnimatorStateInfo(layer).fullPathHash` and `normalizedTime` →
   `cloneAnimator.Play(hash, layer, normalizedTime)` (Demeo's `CopyAnimatorParameters` pattern).
   This makes the held clone animate in lockstep with the live mini on the board.
4. On release, `Destroy` the clone and close the info panel. The **real figure never moves**, so
   there is provably no gameplay/MP effect and its own animations keep playing on the board.

**Documented fallback (Approach A — move the real figure).** Prefix-skip `ActorBehaviour.Update`
+ `LateUpdate` for held figures only (the exact precedent is `ActorBars` prefix-skipping
`WorldspaceDisplayPanelBase.TrackCharacter`/`LateUpdate` via an `Owns()` gate,
`src/GloomhavenVR/WorldUI/ActorBars.cs:333-345`), reparent the real `m_RootGameObject` to the
GrabAnchor, and on release simply stop skipping — the game snaps the mini back to its loco target
on the very next frame (free, correct return). Pros: perfect animation fidelity, no clone cost.
Cons: the live gameplay object leaves the board during inspection; concurrent Choreographer
animations addressed at that actor (attacks against it, pushes) are suppressed while held and
only resolve on release; higher MP risk. **Recommend Approach B (clone); expose A as an option
and an open question.**

**Q5 — world-grab → thumbstick click.**
1. `VRHand`: sample `CommonUsages.primary2DAxisClick` and expose
   `ThumbstickClick` / `ThumbstickClickDown` / `ThumbstickClickUp` (mirror the existing
   `Primary/SecondaryButton` down/up edge logic in `ReadDevice` `:379-386`; add to `ClearInput`
   and the sim path `ReadSimulated`).
2. `WorldGrab.UpdateGripOwnership`: replace `hand.GripDown`→`hand.ThumbstickClickDown` and
   `hand.GripPressed`→`hand.ThumbstickClick`. The old grabber-contention guard
   (`Grabber.Held == null && Grabber.Highlighted == null`, `:173`) is no longer needed to avoid
   fighting object grabs (grip now owns those) — keep only a light guard: don't start a world-grab
   on a hand whose `Grabber.Held != null`. Everything else — one-hand drag math, two-hand
   yaw+pinch-scale, deadzones, smoothing, scale persistence, mode gating — is **unchanged**.
   Rename `_leftGrip/_rightGrip` to `_leftStick/_rightStick` for clarity (internal only).
3. Contention: `SnapTurn.IsHandGrabbing` suppression still works (it queries
   `WorldGrab.IsHandGrabbing`, which now reflects stick ownership). Turning/AoE read the stick
   **axis**, independent of the **click**, so both keep working. Update the class-doc "GRIP
   CONTENTION" section to "STICK CONTENTION". No behavioral change to turn/AoE/cards.

---

## 3. Staged implementation plan — parallel workstreams (disjoint file sets)

Worktree-parallel-safe splits. **WS-A must land before WS-B's grip grab is enabled** (until the
stick rebind ships, grip still triggers world-grab); if run truly in parallel, gate WS-B's grip
figure-grab behind a temporary check `!WorldGrab.Instance.IsHandGrabbing(hand)`.

### WS-A — Input rebind (world-grab → thumbstick)
Files (own set): `src/GloomhavenVR/Hands/VRHand.cs`, `src/GloomhavenVR/Rig/WorldGrab.cs`.
Read-only touch/verify: `src/GloomhavenVR/Rig/SnapTurn.cs` (expected: no change).
- Add `ThumbstickClick`/`Down`/`Up` to `VRHand` (device + sim + clear).
- Rebind `WorldGrab.UpdateGripOwnership`; update class docs. No math changes.
- Deliverable check: one stick = drag, both = zoom/rotate; grip no longer moves the world; turn
  and AoE unaffected.

### WS-B — Figure grab driver + grabbable
Files (new, own set): `src/GloomhavenVR/Board/FigureGrab/FigureGrabbable.cs`,
`src/GloomhavenVR/Board/FigureGrab/FigureGrabDriver.cs`, `src/GloomhavenVR/Board/FigureGrab/FigureGrabConfig.cs` (or fold into `BoardConfig`).
- `FigureGrabDriver` (a `MonoBehaviour` on the Board driver GO, or ticked from `BoardDriver`):
  enumerate `WorldspaceUITools.Instance._panelUIControllers` → figure GO + `CInteractableActor`
  collider → register/prune one `FigureGrabbable` each (mirror `ActorBars.Tick`
  adopt/drop pattern). Laser point-and-grab: on `GripDown` with the far pick collider belonging to
  a figure, `hand.Grabber.ForceGrab(figureGrabbable)`.
- `FigureGrabbable : IGrabbable` (+ `IGrabHighlight` for an emissive/outline pulse via
  `WorldspaceUITools.GetActorOutlinable`): `GrabWithGrip = true`; `OnGrab` resolves the `CActor`
  and calls into WS-C (clone) + WS-D (info); `OnRelease` tears both down.
- Registration in `BoardModule.Init` (shared file — see §4).

### WS-C — Held clone + live animation mirror
Files (new, own set): `src/GloomhavenVR/Board/FigureGrab/HeldFigureClone.cs`.
- Build clone from `ActorBehaviour.m_AnimatedGameObject`; disable gameplay components/colliders;
  compute `HeldPose` + inspection scale (handle 100× armature scale, memory
  `vr-render-gotchas`); per-frame `Animator` state+param mirror. Consumed by `FigureGrabbable`.
- Clean seam: `FigureGrabbable` calls `HeldFigureClone.Create(actorBehaviour, hand)` / `.Dispose()`.

### WS-D — Held-figure info panel
Files (own set): `src/GloomhavenVR/WorldUI/Surfaces/StatPanelSurface.cs` (add a static
`HeldFigureAnchor` override + a `ShowForHeld(CActor)` entry that calls `ActorStatPanel.Show`).
- Add `internal static Transform? HeldAnchorOverride` (or a `Vector3?`), preferred in
  `ResolveActorAnchor`/`PlaceWatch`. `FigureGrabbable.OnGrab` sets it to the held clone transform
  and calls `ActorStatPanel.Instance.Show(actor)`; `OnRelease` clears it (and hides the panel if
  the game's hover didn't).
- Alternative to avoid touching the shared surface: a **new** `HeldFigureInfoSurface` registered
  in `WorldUIModule.BuildTickSteps` (shared file — §4). Reusing `StatPanelSurface` is less code
  and guarantees identical content; a new surface keeps file sets fully disjoint. **Pick one at
  plan time.**

### Cross-cutting
- Localization + config toggle live in orchestrator-owned files (§4).
- Optional in-VR toggle in `SettingsPanel` (shared — §4).

---

## 4. Orchestrator-owned shared files (returnable snippets)

These are single-owner/merge-sensitive; hand the exact edits back to the orchestrator rather than
editing them in a parallel worktree.

**`src/GloomhavenVR/Core/Loc.cs`** — add keys to `Build()` (Pair(en, de) form):
```csharp
["grab_figures"]      = Pair("Grab figures", "Figuren greifen"),
["inspect_figure"]    = Pair("Inspect", "Untersuchen"),
```

**Config toggle** — add to `BoardConfig` (module-owned) or a new `FigureGrabConfig.Bind()`:
```csharp
public static ConfigEntry<bool> GrabFigures;      // default true
public static ConfigEntry<float> HeldFigureScale; // inspection scale multiplier
// If Approach A is ever chosen: public static ConfigEntry<bool> GrabRealFigure; // default false
```
World-grab rebind is unconditional per the request; consider a safety escape hatch
`ComfortSettings.WorldGrabOnThumbstick` (default true) bound in `ComfortSettings.Bind`
(`src/GloomhavenVR/Rig/ComfortSettings.cs:241`) so a regression can be toggled without a rebuild.

**`src/GloomhavenVR/Board/BoardModule.cs`** (Board-owned shared) — register the driver:
```csharp
// in Init(), after the existing driver AddComponent:
_driverGo.AddComponent<FigureGrab.FigureGrabDriver>();
// in Shutdown(): FigureGrab.FigureGrabDriver cleans up via OnDestroy; add FigureGrabbable
// unregister if a static registry is used.
```

**`src/GloomhavenVR/WorldUI/WorldUIModule.cs`** (shared) — ONLY if WS-D uses a new
`HeldFigureInfoSurface`: add `("HeldFigureInfoSurface", _heldInfo.Tick)` to `BuildTickSteps`
(`:186-225`) and to `OnDestroy`. If WS-D extends `StatPanelSurface`, no change here.

**`src/GloomhavenVR/WorldUI/SettingsPanel.cs`** (shared, optional) — a "Grab figures" toggle row
(uses the `grab_figures` Loc key).

`src/GloomhavenVR/Cards/PlayTray.cs` — **no change expected** (flagged orchestrator-owned in
memory only in case a tray control is later wanted).

---

## 5. Risks / unknowns needing a hardware run

1. **Clone fidelity.** `Instantiate(m_AnimatedGameObject)` must capture the full skinned-mesh +
   bone hierarchy + attached weapons/VFX. If the SkinnedMeshRenderer's `rootBone`/`bones` live
   outside `m_AnimatedGameObject`, the clone deforms wrong — may need to clone
   `m_RootGameObject` instead and prune. Verify per class (heroes vs. monsters vs. summons).
2. **Animator mirror cost/correctness.** Per-frame `Play(hash, layer, normalizedTime)` across all
   layers — confirm smoothness and that transitions look right; fall back to param-only copy +
   the clone's own controller if state-copy jitters.
3. **Scale.** 100× armature scale + diorama scale in `GrabAnchor`; find an inspection size that
   reads well in hand without clipping the face/hand. Hardware tuning.
4. **Enemy collider coverage.** Confirm every monster figure carries a `CInteractableActor`
   collider reachable by the palm/laser (players confirmed; monsters expected but unverified on
   hardware).
5. **Info panel churn.** `StatPanelSurface` uses show/hide hysteresis for board hover; ensure the
   held-anchor path doesn't fight the game re-showing/hiding `ActorStatPanel` as the laser also
   sweeps the board. May need to suppress board-hover stat opens while a figure is held.
6. **Thumbstick-click ergonomics.** Some runtimes report `primary2DAxisClick` unreliably; verify
   on Quest 3 / Virtual Desktop (VDXR). Keep the config escape hatch.
7. **Mode gating.** Decide whether figure-grab is allowed in `BoardTargeting`/`ModalUI` or only in
   idle scenario view (avoid grabbing while mid-ability-targeting).

---

## 6. Open design questions for the user

1. **Clone vs. real figure.** Recommend the **clone** (zero gameplay effect, animations mirrored,
   Demeo's own "ghost" choice). Accept the clone, or prefer moving the real mini (simpler,
   perfect fidelity, but the live object leaves the board and it interacts with concurrent
   animations)?
2. **Which hand / how many.** Either hand grabs; only one figure at a time, or one per hand?
3. **Characters + monsters both?** Recommend both (same rig, same info). Restrict to enemies, or
   to your own characters?
4. **How is the info dismissed?** Recommend: shown while held, gone on release. Or a sticky toggle
   that keeps the card after you set the figure down?
5. **Held size & placement.** A fixed comfortable inspection scale in the palm, or 1:1 board
   scale? Info card above-and-to-the-side (matching the board hover) OK?
6. **Grab button.** Recommend **grip** (freed by the thumbstick rebind), leaving trigger for
   board select and thumbstick-click for world-grab. Confirm this three-button split.
