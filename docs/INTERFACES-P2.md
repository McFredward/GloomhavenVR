# Phase 2 — Shared interface surface (updated for Phase 5)

> This API was FROZEN during the parallel P3/P4 phases and re-opened by the Phase-5
> integration pass. Everything marked **P5** below is an additive integration change;
> the pre-P5 surface is behavior-preserving except where explicitly noted (per-hand
> interactor matrix §4). Everything is `internal` — all modules compile into the same
> `GloomhavenVR.dll`.

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

**P5:** `Register(Canvas, PokeSurfaceTuning?)` adds per-canvas press feel —
`PokeSurfaceTuning { HoverRange, ReleaseDepth, PressThrough }` (meters @ scale 1),
with presets `Default` (= the P2 constants 0.06/0.012/0.05, used when no tuning is
passed), `SmallDialog` (0.04/0.008/0.03) and `LargePanel` (0.08/0.015/0.07).
`CanvasConversion.Convert(..., pokeTuning:)` forwards it.

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
- **P5** `hand.Ray.ReticleOverride` (`Vector3?`) — world point that replaces the
  VISIBLE reticle/laser end while the ray has a hit (Board snaps it to hex centers
  under `[Board] SnapToHexCenter`). One-frame latch: set it every frame you want it;
  it self-clears otherwise. Pick data is never affected.
- **P5** ModalUI visual constraint: in `VRMode.ModalUI` the laser only shows while
  pointing within `[Hands] ModalRayConeDegrees` (default 25°, 0 = always) of a UI
  surface — any registered `UguiPokeSurfaces` canvas or an extra target registered
  via static `RayInteractor.RegisterUiTarget(Transform)` / `UnregisterUiTarget`
  (the WorldUI flat screen registers its quad). The pick itself stays active.

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
**P5:** override `protected virtual HeldPose GetHeldPose(VRHand hand)` to declare the
pose (local position/rotation relative to the GrabAnchor, optional uniform scale) the
object takes while held — instead of re-writing the transform after `base.OnGrab`
(which fought the base snap). `HeldPose.Default` reproduces the P2 snap; a scale set
by the pose is restored on release (VRCard uses this for its inspect pose).
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
| `SceneLoaded` **(P5)** | `SceneLoadedEvent { Scene Scene; LoadSceneMode Mode }` | `SceneManager.sceneLoaded` relay (no patch) |
| `HandShown` **(P5)** | `HandShownEvent { CPlayerActor? Player; CardHandMode Mode }` | Cards module (CardsHandManager.Show/ShowHands postfixes) — replaces the module-local `CardsSignals.HandShown` |
| `MiniaturePoked` **(P5)** | `MiniaturePokedEvent { CActor Actor }` | Board module (near-touch poke on an actor miniature) — WorldUI opens `ActorStatPanel` on it |

Struct payloads, no per-event allocation. Payload fields reference live game objects —
consume synchronously. The bus (and everything below) also runs in `[Dev] Enabled`
mode without an HMD.

## 4. Mode state machine — `GloomhavenVR.Core.Events.VRModeStateMachine`

```csharp
enum VRMode { Menu2D, TableIdle, CardSelection, HalfSelection, BoardTargeting, ModalUI }
[Flags] enum Interactors { None, Poke, Ray, Grab, PalmGate, All }
enum HandRole { Dominant, NonDominant }    // P5 — dominance = [Hands] PrimaryHand

VRModeStateMachine.CurrentMode
VRModeStateMachine.ModeChanged        // event Action<VRModeChange { From, To }>
VRModeStateMachine.InteractorsFor(VRMode)            // both-hands union incl. RayAlwaysOn
VRModeStateMachine.InteractorsFor(VRMode, HandRole)  // P5: effective per-hand policy
```

Composition (priority): no scenario → `Menu2D`; UI locked → `ModalUI`; Choreographer
in a targeting wait-state → `BoardTargeting`; else the flow mode from the message map
(`TableIdle` / `CardSelection` / `HalfSelection`).

**Extension points (data, not patches)** — call from your module `Init()`:

```csharp
VRModeStateMachine.MapMessage(CMessageData.MessageType.X, VRMode.Y);         // add/override message → flow mode
VRModeStateMachine.SetTargetingState(Choreographer.ChoreographerStateType.X, true); // mark a wait-state as targeting
VRModeStateMachine.SetInteractorPolicy(VRMode.X, Interactors.Poke | ...);     // both-hands policy for a mode
VRModeStateMachine.SetHandInteractorPolicy(VRMode.X, HandRole.Dominant, ...);  // P5: per-hand override
VRModeStateMachine.ClearHandInteractorPolicy(VRMode.X, HandRole.Dominant);
```

### Final per-mode / per-hand interactor matrix (P5, MISSION A.4/A.11)

| Mode | Dominant hand | Non-dominant hand | Rationale |
|---|---|---|---|
| `Menu2D` | Ray + Poke | Poke | **Only the dominant hand has a laser** (test #6): it is the flat-screen pointer/click hand. The NON-dominant **trigger switches dominance** to that hand (persisted to `[Hands] PrimaryHand`, haptic confirm — `FlatScreen.TickHandednessSwitch`). Poke on both hands serves the settings panel and the flat-screen poke-click. No Grab/PalmGate — nothing physical exists in the menu. |
| `TableIdle` | Poke + Grab + PalmGate | Poke + Grab + PalmGate | Spectate/manipulate; palm-up shows the fan on the non-dominant hand (Cards only reads that hand's gate). |
| `CardSelection` | Poke + Grab + **Ray** | Poke + Grab + PalmGate | Dominant ray = hero placement / board picks during selection (P3a wish); non-dominant owns the fan, and having **no laser on the fan hand** keeps the beam out of the cards (P3b wish). |
| `HalfSelection` | Poke + Grab + PalmGate | Poke + Grab + PalmGate | Played-card halves are poked; targeting has its own mode. |
| `BoardTargeting` | Ray + Poke | Poke | The far pick consumes `VRHands.PrimaryPick` exclusively — a second laser was noise. Near-touch works with either hand. |
| `ModalUI` | Poke + Ray† | Poke + Ray† | † Ray stays ACTIVE but its laser only shows within `[Hands] ModalRayConeDegrees` (default 25°) of a UI surface — see §2 Ray. |

`[Hands] RayAlwaysOn = true` ORs Ray into every cell. The `HandsDriver` applies the
matrix to both hands on every mode change; you normally only *read* `CurrentMode`
and react to `ModeChanged` — never toggle interactors yourself.

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
`Click`. Since test #6 every write is BOTH applied immediately (`InputState.Change`)
and queued as a real state event (`InputSystem.QueueStateEvent`) — the game's uGUI
module polls single-frame `wasPressedThisFrame` edges, which a mid-frame direct write
only makes visible to code running after it in the same frame; the queued event lands
at the next frame's input update, before every `Update`, like hardware input.
Known caveats: a moving hardware mouse fights for `Mouse.current` (idle in
VR — acceptable; `InputManager.DisableAllMouses` exists if not; the bridge re-claims
currency while it drives the pointer, and processed state events re-claim it for
free); the deferred click
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

## 7. Module configuration — the canonical pattern (P5, MISSION A.9)

One pattern, everywhere: **each module binds its own `ConfigFile`** created via

```csharp
GloomhavenVR.Core.ModuleConfig.Create("board")   // → BepInEx/config/dev.gloomhavenvr.board.cfg
```

with a `_file != null` bind-once guard (see `BoardConfig`/`CardsConfig`/
`WorldUIConfig` for the template). The main plugin config
(`dev.gloomhavenvr.cfg`, `Plugin.Config`) is reserved for the cross-cutting
`[General]/[Rig]/[Hands]/[Compat]/[Dev]` sections owned by `Plugin.cs`.
`FindObjectOfType<Plugin>().Config` is retired — do not resurrect it.

Current files:

| File | Owner | Sections |
|---|---|---|
| `dev.gloomhavenvr.cfg` | Plugin.cs | General, Rig, Hands, Compat, Dev |
| `dev.gloomhavenvr.board.cfg` | Board | Board |
| `dev.gloomhavenvr.cards.cfg` | Cards | Cards |
| `dev.gloomhavenvr.comfort.cfg` | Rig (P4) | Comfort |
| `dev.gloomhavenvr.worldui.cfg` | WorldUI | WorldUI, SettingsPanel |

BepInEx persists every `ConfigEntry` write automatically; entries read live apply
immediately, others document their apply point in the entry description.

## 8. Dev harness (for your own testing)

`[Dev] Enabled = true` in `BepInEx/config/dev.gloomhavenvr.cfg` runs the bus, mode
machine, hands (simulated: `[Dev] SimulateHands` or F8; hold T = trigger, G = grip)
and virtual mouse on a flat desktop. F9 clicks the Ready button through the same
GraphicRaycaster+ExecuteEvents path the fingertip poke uses; F10 toggles the overlay
(mode, hand state, recent events). `[Dev] InputDeviceDumpInterval = 5` logs XR devices.
