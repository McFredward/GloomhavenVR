# Phase 2 — Frozen interface surface for Phase-3 workers

> **This API is FROZEN after `feat/hands` merges.** Changes only via the orchestrator
> (ROADMAP: "shared surface is frozen after P2"). Phase-3 workers (`feat/board`,
> `feat/cards`, `feat/world-ui`) build against exactly what is documented here.
> Everything is `internal` — all workers compile into the same `GloomhavenVR.dll`.

## Threading rules (apply to every event below)

- **All bus events, mode changes and interactor callbacks fire on the Unity main
  thread** (sources: Choreographer pump postfixes, uGUI callbacks, hand Update loop).
- **Never block in a handler.** The ScenarioRuleLibrary runs its own worker thread;
  `CardsHandUI.OnCardSelected` already spin-waits the main thread up to 1 s for SRL
  acks (CARDS.md §8) — route card selection through a coroutine, and never add more
  blocking on top.
- Handlers should not throw. Throwing is caught and logged (the game survives), but
  the remaining subscribers of that single raise are skipped.
- Events are edge-triggered notifications — do per-frame work in your own Update,
  not in handlers.
- `ScenarioRuleClient.*` calls only *enqueue*; results come back later via
  `VREvents.ChoreographerMessage`. Never call them off the main thread with
  `processImmediately: true`.

## 1. Hands — `GloomhavenVR.Hands`

### Static access

```csharp
VRHands.Left / VRHands.Right          // VRHand? — null while hands are torn down
VRHands.Ready                          // both exist
VRHands.Get(HandSide side)
VRHands.Primary                        // [Hands] PrimaryHand config (default Right)
VRHands.PrimaryPick                    // IPickProvider? — the primary hand's ray
VRHands.HandsChanged                   // event Action — fired on build AND teardown
```

Hands exist while the Phase-1 rig exists (scenario + VR running) or the dev
simulation is on. **Always null-check or subscribe to `HandsChanged`.**

### `VRHand` (MonoBehaviour, one per side)

| Member | Meaning |
|---|---|
| `Side`, `IsTracked`, `IsSimulated`, `HasPose` | identity/tracking state |
| `Rig` | `HandRig` transform contract (below), never null |
| `TriggerValue`, `GripValue` | analog 0..1 |
| `TriggerPressed/Down/Up`, `GripPressed/Down/Up` | digital with 0.75/0.55 hysteresis |
| `PrimaryButton/PrimaryDown`, `SecondaryButton/SecondaryDown` | A/X, B/Y |
| `ThumbTouch` | capacitive thumb rest (primary/secondary/stick touch) |
| `Thumbstick` | `Vector2` (Phase-3a: AoE rotation on left/right) |
| `Pose` | `HandPose` — `Idle / OpenPalm / Point / Fist` |
| `PalmVelocity` | world units/s (diorama-scaled), for throws/releases |
| `WorldScale` | diorama scale at the hand — multiply "real meters" by this |
| `GetCurl(Finger)` | smoothed 0..1 finger curl |
| `SendHaptic(HapticPreset)` | `HoverTick / ClickPulse / GrabPulse` (rate-limited hover) |
| `TrackedChanged` | `event Action<VRHand,bool>` |
| `Poke`, `Ray`, `Grabber`, `PalmGate` | the four interaction primitives |

Pose source is `UnityEngine.XR.InputDevices` (`XRNode.LeftHand/RightHand`,
`CommonUsages.devicePosition/deviceRotation` etc.) — game-shipped XRModule, no
InputSystem coupling.

### `HandRig` — the transform contract

Same for the procedural fallback hand and bundle gloves; **swapping in real glove
art is a data change, not a code change**.

```csharp
rig.Root        // wrist origin: +Z along fingers, +Y out of the BACK of the hand
rig.Wrist
rig.PalmCenter  // +Y = palm normal (OUT of the palm), +Z along fingers
rig.PalmNormal  // world-space shortcut
rig.IndexTip    // poke origin
rig.GrabAnchor  // where grabbed objects snap
rig.GetFinger(Finger.Thumb..Pinky)  // FingerJoints { Root, Mid, Tip }
```

All anchors are children of the tracked hand: world positions/scale follow the
diorama rig automatically.

### Hand assets in `gloomhavenvr.bundle` (arrive via later human step)

Probed asset paths (first hit wins):

1. `Assets/Bundle/Hands/VRHand_L.prefab` / `Assets/Bundle/Hands/VRHand_R.prefab`
2. `Assets/Bundle/Hands/HandLeft.prefab` / `Assets/Bundle/Hands/HandRight.prefab` (legacy)

Rig mapping inside the prefab, by child transform name (first match wins):

- Anchors: `Anchor_Wrist`, `Anchor_Palm`, `Anchor_IndexTip`, `Anchor_Grab`
- Fingers: `Anchor_{Thumb|Index|Middle|Ring|Pinky}_{Root|Mid|Tip}`
- Fallback: SteamVR bone names (`finger_index_0_r`, `finger_index_1_r`, …)

Missing transforms are synthesized at procedural default positions — the `HandRig`
contract always holds. Prefab conventions: root at wrist, +Z along fingers, +Y out of
the back of the hand (see `unity/GloomhavenVR.Assets/Assets/Bundle/Hands/README.md`).
Finger joints must curl toward the palm under **positive local-X rotation**.

## 2. Interaction primitives — `GloomhavenVR.Hands.Interact`

### Poke (`IPokeable` + `PokeInteractor`)

```csharp
interface IPokeable
{
    void OnPokeEnter(VRHand hand);   // fingertip within ~3.5 cm
    void OnPokeExit(VRHand hand);
    void OnPoke(VRHand hand);        // fingertip contact (~8 mm), once per press
}
```

Registration: `VRInteractables.RegisterPokeable(target, collider)` /
`UnregisterPokeable(target)` — or derive from `PokeableBehaviour` (auto-registers the
GameObject's collider on enable/disable). **Use primitive/convex colliders**
(the test uses `Collider.ClosestPoint`).

Per hand: `hand.Poke.Hovered`, `hand.Poke.HoveredUi`, `hand.Poke.Enabled`.

### uGUI poke (world-space canvases)

`UguiPokeSurfaces.Register(Canvas)` / `Unregister(Canvas)` — Phase-3c registers its
converted world-space canvases. When a fingertip crosses the canvas plane the
interactor synthesizes real pointer events (`pointerEnter/Exit/Down/Up/Click`) via
`ExecuteEvents` at the projected screen point.

**Modality is respected by construction**: hits come exclusively from the canvas's
own `GraphicRaycaster.Raycast`, and only while that raycaster is **enabled** — the
game's `UIManager.ToggleLockUI` disables raycasters to lock the UI, so locked UI
gets no synthetic events. Requirements for registered canvases: WorldSpace render
mode, `worldCamera` set (use the VR head camera), a `GraphicRaycaster` present.

### Ray (`RayInteractor : IPickProvider`)

```csharp
interface IPickProvider { bool TryGetPick(out PickPose pick); }
struct PickPose { Vector3 Origin, Direction; bool HasHit; Vector3 HitPoint; float HitDistance; Collider? HitCollider; }
```

- `hand.Ray.Mask` — set the game's selection LayerMask here (Phase-3a:
  `Controller.m_ActiveSelectionRaycastLayer`).
- `hand.Ray.Current` — latest pick, updated once per frame while enabled.
- Visuals (subtle laser + reticle) show only while enabled (mode policy /
  `[Hands] RayAlwaysOn`).
- **Phase-3a contract**: your `MF.FindInteractableAtMousePosition` /
  `InputManager.CursorPosition` patches consume `VRHands.PrimaryPick` and may
  substitute their own `IPickProvider` (e.g. fingertip touch near the board).

### Grab (`IGrabbable` + `ProximityGrabber`)

```csharp
interface IGrabbable
{
    bool CanGrab { get; }
    void OnGrab(VRHand hand);
    void OnRelease(VRHand hand, Vector3 velocity);  // palm velocity, world units/s
}
interface IGrabHighlight { void OnGrabHighlight(VRHand hand, bool highlighted); }  // optional, emissive pulse hook
```

Registration: `VRInteractables.RegisterGrabbable(target, collider)` — or derive from
`GrabbableBehaviour` (auto-registration + optional snap-to-`GrabAnchor` parenting with
restore-on-release; `Holder` property tells you which hand holds it).
Flow: nearest `CanGrab` within palm reach (~13 cm) highlights → grip press grabs →
grip release calls `OnRelease` with measured velocity. The grabber never reparents —
the grabbable decides what "held" means. Per hand: `hand.Grabber.Highlighted`,
`hand.Grabber.Held`, `hand.Grabber.HighlightChanged`.

### Palm gate (`PalmGate`) — Phase-3b's card-fan trigger

`hand.PalmGate.IsOpen`, `hand.PalmGate.CurrentDot`,
`event Action<VRHand,bool> Changed`. Opens at dot(palmNormal, toHMD) > 0.6, closes
below 0.35 (hysteresis).

## 3. Event bus — `GloomhavenVR.Core.Events.VREvents`

| Event | Payload | Source |
|---|---|---|
| `ChoreographerMessage` | `ChoreoMessageEvent { MessageType Type; CMessageData Message }` | postfix `Choreographer.ProcessMessage` |
| `ChoreographerStateChanged` | `ChoreoStateEvent { Choreographer.ChoreographerStateType State }` | postfix `Choreographer.SetChoreographerState` |
| `NavigationStateChanged` | `NavStateEvent { IState State }` | `UINavigation.StateMachine.EventStateChanged` |
| `CardSelectionChanged` | `CardSelectionEvent { AbilityCardUI Card; bool Selected }` | `AbilityCardUI.CardSelectionStateChanged` |
| `CardHoverChanged` | `CardHoverEvent { AbilityCardUI Card; bool Hovering }` | `AbilityCardUI.CardHoveringStateChanged` |
| `FullCardHoverChanged` | `FullCardHoverEvent { bool Hovering }` | `FullAbilityCard.FullCardHoveringStateChanged` |
| `ShortRestSelected` | `ShortRestEvent { ShortRest Widget; bool Selected }` | `ShortRest.OnSelectShortRest` |
| `UiLockChanged` | `UiLockEvent { bool Locked }` | postfix `UIManager.ToggleLockUI` |

Struct payloads, no per-event allocation. Payload fields reference live game objects —
consume synchronously. The bus (and everything below) also runs in `[Dev] Enabled`
mode without an HMD.

## 4. Mode state machine — `GloomhavenVR.Core.Events.VRModeStateMachine`

```csharp
enum VRMode { Menu2D, TableIdle, CardSelection, HalfSelection, BoardTargeting, ModalUI }
[Flags] enum Interactors { None, Poke, Ray, Grab, PalmGate, All }

VRModeStateMachine.CurrentMode
VRModeStateMachine.ModeChanged        // event Action<VRModeChange { From, To }>
VRModeStateMachine.InteractorsFor(VRMode)  // effective policy incl. RayAlwaysOn override
```

Composition (priority): no scenario → `Menu2D`; UI locked → `ModalUI`; Choreographer
in a targeting wait-state → `BoardTargeting`; else the flow mode from the message map
(`TableIdle` / `CardSelection` / `HalfSelection`).

**Extension points (data, not patches)** — call from your module `Init()`:

```csharp
VRModeStateMachine.MapMessage(CMessageData.MessageType.X, VRMode.Y);         // add/override message → flow mode
VRModeStateMachine.SetTargetingState(Choreographer.ChoreographerStateType.X, true); // mark a wait-state as targeting
VRModeStateMachine.SetInteractorPolicy(VRMode.X, Interactors.Poke | ...);     // change which primitives a mode enables
```

Default policy: Menu2D=Ray · TableIdle/CardSelection/HalfSelection=Poke+Grab+PalmGate ·
BoardTargeting=Ray+Poke · ModalUI=Poke+Ray. The `HandsDriver` applies the policy to
both hands on every mode change; you normally only *read* `CurrentMode` and react to
`ModeChanged`.

## 5. Virtual mouse — `GloomhavenVR.WorldUI.VirtualMouse`

Drives the game's own console virtual mouse (a real InputSystem `Mouse` device via
`InputManager.CreateVirtualMouse()` — instance method, PATCH-TARGETS correction #6),
so ALL remaining 2D uGUI works without InputModule patches:

```csharp
VirtualMouse.EnsureCreated()     // bool — needs the InputManager singleton
VirtualMouse.IsAvailable
VirtualMouse.WarpTo(Vector2 screenPos)
VirtualMouse.Press() / Release()
VirtualMouse.Click(Vector2? screenPos = null)  // press + auto-release 2 frames later
VirtualMouse.BeginDrag(screenPos) / EndDrag(screenPos)
```

Phase-3c: ray hits the floating 2D screen → hit UV × screen resolution → `WarpTo` +
`Click`. Known caveats: a moving hardware mouse fights for `Mouse.current` (idle in
VR — acceptable; `InputManager.DisableAllMouses` exists if not); the deferred click
release needs the WorldUI driver alive (it is, whenever VR or dev mode runs).
`Utilities.VirtualMouseUtilities` (Utilities.dll) is referenced too, but it only
reroutes `InputManager.CursorPosition`, not the InControl uGUI module — the
InputSystem device is the right default.

## 6. Rig access (from Phase 1, unchanged plus two statics)

```csharp
GloomhavenVR.Rig.VRRigDriver.RigRoot      // Transform? — tracking-space root, lossyScale = diorama scale
GloomhavenVR.Rig.VRRigDriver.HeadCamera   // Camera? — the head-tracked scenario camera
```

Null while no scenario/VR. Parent world-anchored VR objects to `RigRoot` only if they
should move with the table; UI/hand things belong under the hands or your own roots.

## 7. Dev harness (for your own testing)

`[Dev] Enabled = true` in `BepInEx/config/dev.gloomhavenvr.cfg` runs the bus, mode
machine, hands (simulated: `[Dev] SimulateHands` or F8; hold T = trigger, G = grip)
and virtual mouse on a flat desktop. F9 clicks the Ready button through the same
GraphicRaycaster+ExecuteEvents path the fingertip poke uses; F10 toggles the overlay
(mode, hand state, recent events). `[Dev] InputDeviceDumpInterval = 5` logs XR devices.
