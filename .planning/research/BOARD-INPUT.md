# Gloomhaven Digital — Board, Hex Selection, Input Pipeline & Camera (VR-mod research)

All paths relative to `/home/claw/gloomhaven_vr/decompiled/`. Every claim below was verified by reading the decompiled source. Line numbers refer to the decompiled files as-is.

## 1. Overview

The scenario ("board") architecture has three layers:

1. **Rules engine (`ScenarioRuleLibrary`, runs on a worker thread)** — `ScenarioManager`, `CTile[,] ScenarioManager.Tiles`, `CActor`, `CAbility`, `PhaseManager`/`CPhase*`. The UI thread communicates with it exclusively through a message queue: `ScenarioRuleClient.AddSRLQueueMessage` → `BlockingCollection<CSRLMessage>` consumed by `s_WorkThread` (`ScenarioRuleLibrary/ScenarioRuleLibrary/ScenarioRuleClient.cs:343-351,837`). Results flow back as `CMessageData` messages pumped on the main thread in `Choreographer.Update()` (`GH.Runtime/Choreographer.cs:2344`).
2. **Client/presentation (`GH.Runtime`)** — `Choreographer` (16k-line god class orchestrating everything in a scenario), `ClientScenarioManager` (tile array ↔ GameObjects), `WorldspaceStarHexDisplay` (hex highlighting/targeting overlay), `Controller` (mouse pick dispatcher), `CameraController`.
3. **Input (`InControl` + Unity Input System)** — `InputManager` (Singleton, wraps an InControl `PlayerActionSet` called `GHControls`), `InControlInputModuleExtended` for uGUI, `Utilities.InputSystemUtilities` for raw device reads.

Everything world-interactive funnels through **one static raycast helper** (`MF.FindInteractableAtMousePosition`) and **one static click-commit delegate** (`TileBehaviour.s_Callback`). This is extremely convenient for a VR mod: patch a handful of static choke points and the entire game follows a VR pointer.

## 2. Board representation & coordinates

### Runtime hex objects
- Each hex is a **GameObject with a collider** (on a dedicated "hex selection" layer) carrying:
  - `TileBehaviour` (MonoBehaviour) — `GH.Runtime/TileBehaviour.cs:10`. Holds `public CClientTile m_ClientTile` and the static click delegate `s_Callback` (see §4). Registers itself in `ObjectCacheService` on enable (`TileBehaviour.cs:34-47`).
  - `CInteractableTile : CInteractable` — `GH.Runtime/CInteractableTile.cs` — the pick receiver.
- `CClientTile` (`GH.Runtime/CClientTile.cs`) is the glue record: `m_GameObject`/`m_TileBehaviour` (plus `m_GameObject1`/`m_TileBehaviour1` for double-hexes), and `m_Tile` (the logical `ScenarioRuleLibrary.CTile`).
- `ClientScenarioManager` (`GH.Runtime/ClientScenarioManager.cs`) owns the 2-D lookup: `public CClientTile[,] ClientTileArray` (`:60`), built in static `Create(ScenarioState, bool)` (`:100-137`) by walking `ScenarioManager.Tiles[x,y]` and resolving each `CTile.m_Hex` (a `CMapTile`) to its spawned GameObject through `Singleton<ObjectCacheService>.Instance.GetTile(cTile.m_Hex)` (`GH.Runtime/Script.Controller/ObjectCacheService.cs:137`).
- `ClientScenarioManager.m_Board` (`:16`) is the parent GameObject under which stars, spawned characters and props are parented (`Choreographer.cs:904,1087,1191`; `WorldspaceStarHexDisplay.cs:3002`). Tile GameObjects themselves live under `RoomVisibilityManager.s_Instance.Maps` → one child per `ApparanceMap` room → tile children (`GH.Runtime/UnityGameEditorRuntime.cs:675-696`). **The natural "table root" is the common parent of `RoomVisibilityManager.Maps` and `ClientScenarioManager.m_Board` in the `ProcGen` scene — or simply re-root/scale both.**

### Coordinate systems & conversion
- Logical grid: offset coordinates `Point/TileIndex (X, Y)` with odd-row X-shift (pointy-top hex rows along Z).
- **Grid → cartesian**: `ScenarioRuleLibrary.MF.ArrayIndexToCartesianCoord(Point arrayIndex, float xScalar, float yScalar, out float x, out float y)` — `ScenarioRuleLibrary/ScenarioRuleLibrary/MF.cs:99-103`:
  `x = (X + ((Y & 1) == 1 ? 0.5 : 0)) * xScalar; y = Y * yScalar;` (this yields positions in "positive map space", not directly world space — world positions are best read from `ClientTileArray[x,y].m_GameObject.transform.position`).
- **World → grid**: `MF.GetTileIntegerSnapSpace(Vector3 worldPos)` (global-namespace `GH.Runtime/MF.cs:42-46`) snaps a world position to integer hex indices; the array index is `GetTileIntegerSnapSpace(pos) + scenarioState.PositiveSpaceOffset` (see `UnityGameEditorRuntime.InitialiseScenario`, `GH.Runtime/UnityGameEditorRuntime.cs:688-693`). In practice the game never converts world→hex analytically during play — **it raycasts against tile colliders and reads `TileBehaviour.m_ClientTile.m_Tile.m_ArrayIndex`.** A VR pointer should do the same.
- **Hex size**: `UnityGameEditorRuntime.s_TileSize` (`GH.Runtime/UnityGameEditorRuntime.cs:21`), initialised in `Initialise()` (`:31-66`) from the `Resources.Load<GameObject>("Hex")` prefab's BoxCollider: `s_TileSize.x = collider.size.x`, `s_TileSize.z = collider.size.z * 0.75f` (row pitch). The numeric value is data-driven (read it at runtime from `s_TileSize`); it also feeds LOS math via `CActor.SetLOSTileScalar(x, z)` (`:64`, and `ClientScenarioManager.cs:466`).

### Scene loading flow
- Scenes are loaded additively via `SceneManager.LoadSceneAsync` — **no Addressables for scenes** (Addressables are used for assets/sprites: `GH.Runtime/AddressableMisc/*`, `AssetBundleManager.cs`).
- Scene names: `SceneController.GetSceneNameForType` (`GH.Runtime/SceneController.cs:203-214`): `"MainMenu"`, `"NewAdventureMap"`, `"CampaignMap"`, `"Game"` (each with `_gamepad` variant chosen when `InputManager.GamePadInUse`). The scenario geometry itself is generated by the Apparance procedural system in an additional `"ProcGen"` scene: `Choreographer.LoadProcGenScene` (`GH.Runtime/Choreographer.cs:14722,14728`).
- Room reveal ("fog of war") is **not camera-based**: `RoomVisibilityTracker.IsVisible()` checks tile revealed state (`GH.Runtime/RoomVisibilityTracker.cs:66+`), `RoomVisibilityManager` just toggles room assets (`GH.Runtime/RoomVisibilityManager.cs`). It does hold a `_scenarioCamera` reference it blinks off/on to force re-render (`ReloadCameraCoroutine`, `:59-74`) — harmless for VR but note the serialized camera reference.

## 3. Picking / selection pipeline

### The single world-pick choke point
`MF.FindInteractableAtMousePosition(bool ignoreinteractableviaguiflag, LayerMask gameSelectionRaycastLayer)` — `GH.Runtime/MF.cs:387-401`:
```csharp
Ray ray = Camera.main.ScreenPointToRay(InputManager.CursorPosition);
if (Physics.Raycast(ray, out hitInfo, 1000f, gameSelectionRaycastLayer))
    result = hitInfo.transform.gameObject.GetComponentInParent<CInteractable>();
```
Callers (all of them):
- `Controller.LateUpdate` (`GH.Runtime/Controller.cs:171`) — click dispatch.
- `WorldspaceStarHexDisplay.InteractableUnderMouse` (`GH.Runtime/WorldspaceStarHexDisplay.cs:3822-3829`) — hover/targeting refresh (`PointingAtANewTile`, `:3785`).
- `ClientScenarioManager.Update`/`EnableUserLOSDisplay` (`:456,551`) — LOS debug overlay.
- Variant `MF.FindNearestInteractableToPosition(bool, LayerMask, Vector3 screenPos)` (`MF.cs:403-413`), used by gamepad hex-cursor (`WorldspaceStarHexDisplay.cs:3808-3811`).

Layers: `Controller` exposes `m_GameSelectionRaycastLayer`, `m_HexSelectionRaycastLayer`, `m_HeroSelectionRaycastLayer`, with `m_ActiveSelectionRaycastLayer` set to the hex layer at `Start` (`Controller.cs:45-52,162`). Serialized in the scene, so dump at runtime to get layer indices.

### Click dispatch (`Controller`, `GH.Runtime/Controller.cs`)
- `CommonLoop(bool)` (`:243-282`) reads clicks from InControl: `Singleton<InputManager>.Instance.PlayerControl.MouseClickLeft.WasPressed/.WasReleased`, sets `s_SingleClicked`/`s_DoubleClicked`, and suppresses world picks when the press started over uGUI (`EventSystem.current.IsPointerOverGameObject()`, `:251`).
- `LateUpdate` (`:165-221`): on click, raycasts, checks `InteractabilityManager.ShouldAllowSelectionForTileIndex`, requires `Choreographer.s_Choreographer.ThisPlayerHasTurnControl`, then `SelectNewObject` → **`CInteractable.ShowNormalInterface(disabled: false)`** (`:293-305`). Right-click release (without camera rotation) clears the target (`:213-220`).
- `CInteractable` (`GH.Runtime/CInteractable.cs`) is a trivial base: `ShowNormalInterface`, `OnDoubleClicked`, `OnCursorEnter/Exit` (implements `IHoverable`).
- `CInteractableTile.ShowNormalInterface` (`GH.Runtime/CInteractableTile.cs:8-25`) → `ExecuteTileCallback()` (`:45-48`):
  `TileBehaviour.s_Callback(m_TileBehaviour.m_ClientTile, null, networkActionIfOnline: true, isUserClick: true, SaveData.Instance.Global.EnableSecondClickHexToConfirm);`
- `CInteractableActor.ShowNormalInterface` (`GH.Runtime/CInteractableActor.cs:39-47`) — clicking a **monster/character model** resolves the actor's `ArrayIndex` to the `CClientTile` under it and calls the **same** `TileBehaviour.s_Callback`. So actors, doors, chests, and hexes are all "tile clicks"; doors/chests are opened by moving onto/adjacent to their tiles via the normal move flow (door confirm UI: `Choreographer.m_ConfirmDoorTileSelect`).

### Hover / highlight
Two mechanisms:
1. `HoverRegisterer` (`GH.Runtime/HoverRegisterer.cs:34-36`): its own `Camera.main.ScreenPointToRay(InputManager.CursorPosition)` + `Physics.Raycast(..., targetLayer)` each `Update`, driving `IHoverable.OnCursorEnter/OnCursorExit` (skipped when `UIManager.IsPointerOverUI`).
2. `WorldspaceStarHexDisplay` (singleton `Instance`, `GH.Runtime/WorldspaceStarHexDisplay.cs:111`) — the entire hex-overlay system. Its `Update()` (`:408-492`) polls `PointingAtANewTile()` (which uses the central raycast) and refreshes per display state (`WorldSpaceStarDisplayState`: `ShowNone/CharacterPlacement/MovementSelection/TargetSelection/LevelEditorSpawning`, `:60`). Highlights are pooled `HexSelect_Control` "star" objects parented to `m_Board` and positioned on the tile GameObject (`SetStarPos`, `:3000-3005`; `CreateStar` `:2898`). `HexSelect_Control` (`GH.Runtime/HexSelect_Control.cs`) drives a hex-border shader (`HexType` × `HexMode` enums: Move/PositiveEffect/NegativeEffect/… × Reach/PossibleTarget/Selected/Cursor, per-edge shader properties `_W_On…_NW_On`) — camera-independent world-space quads, VR-safe.
3. **Actor outlines**: `WorldspaceUITools` (`GH.Runtime/WorldspaceUITools.cs:11-17`) manages `EPOOutline.Outlinable` components (third-party "Easy Performant Outline", sources under `GH.Runtime.FirstPass/EPOOutline/`) — an image-effect/renderer-feature-style outline; verify it works under stereo rendering.
4. Cursor icon feedback: `ControllerInputPointer.CursorType` (`GH.Runtime/ControllerInputPointer.cs`) — a screen-space virtual pointer image used in gamepad mode; its `_allowedStateTypes` list (`:28-36`) is a good catalog of "pointer-relevant" UI states.

## 4. Targeting flow & commit points (Harmony patch candidates)

### Flow
1. SRL decides an ability needs input; `Choreographer` receives a message, sets `WorldspaceStarHexDisplay` display ability (`SetDisplayAbility`, `WorldspaceStarHexDisplay.cs:335`) and a wait state via `SetChoreographerState(ChoreographerStateType, int, CActor)` (`Choreographer.cs:2277`) — e.g. `WaitingForPlayerWaypointSelection`, `WaitingForAreaAttackFocusSelection` (enum at `Choreographer.cs:51-80`).
2. Valid targets/reachable hexes are computed by SRL (`CAbility.ActorsToTarget`, `CAbility.CanReceiveTileSelection()`, `CAbility.RequiresWaypointSelection()`, pathfinder `ScenarioManager.PathFinder.Nodes[x,y]`) and visualised by `WorldspaceStarHexDisplay.ShowPossibleMoveStars/DisplayStandardAttackStars/DisplayAOEStars/…`.
3. The player's pick arrives through `TileBehaviour.s_Callback`, which the Choreographer re-points depending on mode:
   - `Choreographer.TileHandler` — default (registered at `Choreographer.cs:667,4389,5055,…`; also from `ReadyButton.cs:272`, `SkipButton.cs:100`).
   - `Waypoint.TileHandler` — during move/push/pull waypoint placement (registered at `Choreographer.cs:4324,4752,9898,10034`).
   - `null` while selection must be disabled (`Choreographer.cs:716`).
4. The handler commits to the rules engine via `ScenarioRuleClient` static methods (thread-safe queue), and multiplayer replication via `Choreographer.NetworkTargetSelection` (`Choreographer.cs:3360`) / `Synchronizer.ReplicateControllableStateChange`.

### AoE preview / rotation
- Preview: `WorldspaceStarHexDisplay.DisplayAOEStars(CClientTile originTile = null)` (`:2083`), enemy AoE `DisplayEnemyAOEStars` (`:2056`), object placement `DisplaySelectObjectPositionAOEStars` (`:2238`).
- Rotation state: `m_AreaEffectAngle` (0–300 in 60° steps, `AreaEffectAngle` property `:243`).
  - Melee/adjacent AoE (`Range <= 1`): **angle derives from the hovered tile direction** — `RotateAOEWithMouse` (`:2338-2380`) computes `Quaternion.LookRotation(hoverTilePos - actorTilePos)` snapped to 60°.
  - Ranged AoE: `RotateAOEWithKeyboard` (`:2382-2396`) → `RotateAOEClockwise(bool turnRight)` (`:2398-2434`) on `KeyAction.ROTATE_TARGET` / `ROTATE_TARGET_BUTTON`. **`RotateAOEClockwise` is public — call it directly from a VR thumbstick/gesture.**
- Placement/lock: first tile click calls `cAbility.UpdateAreaEffect(clientTile.m_Tile, WorldspaceStarHexDisplay.Instance.AreaEffectAngle)` (`Choreographer.cs:1997`; method: `ScenarioRuleLibrary/ScenarioRuleLibrary/CAbility.cs:2326 public void UpdateAreaEffect(CTile positionTile, float rotation)`) then `ScenarioRuleClient.TileSelected(...)`; second click on an already-selected tile confirms (`AlreadySelected`, `WorldspaceStarHexDisplay.cs:1648`; lock via `SetAOELocked` `:2045`, `IsAOELocked` `:2051`). Remote-proxy variant: `ProxyUpdateSelectionHexes(CClientTile originTile, int targetingAngle, bool aoeLocked)` (`:4224`).

### Harmony patch candidate table

| Class.Method | Signature | File:Line | Why patch / hook |
|---|---|---|---|
| `MF.FindInteractableAtMousePosition` | `static CInteractable (bool ignoreinteractableviaguiflag, LayerMask gameSelectionRaycastLayer)` | `GH.Runtime/MF.cs:387` | **#1 pick choke point.** Prefix-replace with VR controller-ray `Physics.Raycast` → all hover, targeting and click picking follows the VR pointer. |
| `MF.FindNearestInteractableToPosition` | `static CInteractable (bool, LayerMask, Vector3 position)` | `GH.Runtime/MF.cs:403` | Screen-position variant used by gamepad cursor; patch same way. |
| `InputManager.CursorPosition` (getter) | `static Vector2 get_CursorPosition()` | `GH.Runtime/InputManager.cs:126` | Used by `HoverRegisterer`, `CameraController` ground-drag, `ClickTrackerMap`, tooltips. Return the VR pointer's projection into the game camera's screen space for full compatibility. |
| `HoverRegisterer.Update` | `private void Update()` | `GH.Runtime/HoverRegisterer.cs:22` | Secondary raycaster with its own `Camera.main` ray; replace ray or leave covered by `CursorPosition` patch. |
| `Controller.CommonLoop` | `private bool CommonLoop(bool isPaused)` | `GH.Runtime/Controller.cs:243` | Click detection (`MouseClickLeft.WasPressed/WasReleased`, `IsPointerOverGameObject`). Postfix to OR-in VR trigger press/release, or bypass entirely. |
| `Controller.LateUpdate` | `private void LateUpdate()` | `GH.Runtime/Controller.cs:165` | Full dispatch loop; alternative single override point for "VR pick → ShowNormalInterface". |
| `TileBehaviour.s_Callback` (invoke directly) | `delegate void CallbackType(CClientTile clientTile, List<CTile> optionalTileList, bool networkActionIfOnline = false, bool isUserClick = false, bool actingPlayerHasSecondClickConfirmationEnabled = false)` | `GH.Runtime/TileBehaviour.cs:12,26` | **Cleanest direct commit**: from a VR ray hit on a tile collider, call `s_Callback(tile.m_ClientTile, null, true, true, …)` — identical to a mouse click. Null while selection disabled (acts as gate). |
| `Choreographer.TileHandler` | `public void TileHandler(CClientTile clientTile, List<CTile> optionalTileList = null, bool networkActionIfOnline = false, bool isUserClick = false, bool actingPlayerHasSecondClickConfirmationEnabled = false)` | `GH.Runtime/Choreographer.cs:1811` | Master tile-commit: character placement (`PlaceActorAtRoundStart`, `:2107`), single-target, AoE place/confirm, door select. Patch to observe/veto commits per VR mode. |
| `Waypoint.TileHandler` | `public static void TileHandler(CClientTile clientTile, List<CTile> optionalTileList, bool networkActionIfOnline = true, bool isUserClick = false, bool actingPlayerHasSecondClickConfirmationEnabled = false)` | `GH.Runtime/Waypoint.cs:709` | Move/push/pull waypoint commit path. |
| `CInteractableTile.ShowNormalInterface` | `public override void ShowNormalInterface(bool disabled)` | `GH.Runtime/CInteractableTile.cs:8` | Per-tile click entry (includes gamepad select animation & `UINavigation` state checks). |
| `CInteractableActor.ShowNormalInterface` | `public override void ShowNormalInterface(bool disabled)` | `GH.Runtime/CInteractableActor.cs:39` | Actor-model click → tile commit. |
| `ScenarioRuleClient.TileSelected` | `static uint TileSelected(CTile tile, List<CTile> optionalTileList, bool processImmediately = false)` | `ScenarioRuleLibrary/ScenarioRuleLibrary/ScenarioRuleClient.cs:927` | Lowest-level commit into rules engine (observe-only recommended; bypassing Choreographer breaks MP sync/undo). |
| `ScenarioRuleClient.TileDeselected` | `static uint (CTile, List<CTile>, bool = false)` | same file `:943` | Deselect commit. |
| `ScenarioRuleClient.ApplySingleTarget` | `static uint (CActor actor, bool processImmediately = false)` | same file `:948` | Single-target (item/active-bonus) commit. |
| `ScenarioRuleClient.StepComplete` / `Pass` / `Undo` | `static uint StepComplete(bool=false,bool=false)` / `static uint Pass(bool=false)` / `static uint Undo(List<CActiveBonus>)` | `:917 / :993 / :1018` | Confirm/skip/undo — bind to VR buttons (prefer going through `ReadyButton`/`SkipButton`/`m_UndoButton` UI for state consistency). |
| `WorldspaceStarHexDisplay.RotateAOEClockwise` | `public void RotateAOEClockwise(bool turnRight)` | `GH.Runtime/WorldspaceStarHexDisplay.cs:2398` | Call directly for VR AoE rotation (ranged AoE). |
| `WorldspaceStarHexDisplay.RotateAOEWithMouse` | `private bool RotateAOEWithMouse()` | `:2338` | Melee AoE facing follows hovered tile — already covered by pick patch; patch here if custom VR aiming wanted. |
| `Choreographer.SetChoreographerState` | `public void SetChoreographerState(ChoreographerStateType eState, int waitTickFrame, CActor waitActor)` | `GH.Runtime/Choreographer.cs:2277` | Postfix = reliable "interaction mode changed" event (see §7). |
| `CameraController.LateUpdate` | `private void LateUpdate()` | `GH.Runtime/CameraController.cs:1262` | Disable/replace for VR head-tracked camera (see §6). |
| `InitiativeTrack.Select` | `public bool Select(CPlayerActor playerActor)` | `GH.Runtime/InitiativeTrack.cs:352` | Actor selection during card-selection phase (invoked from `TileHandler`, `Choreographer.cs:1832`). |

## 5. Input layer

- **InControl** is the primary abstraction (compiled InControl DLL isn't in the decompiled set; wrappers are). `InputManager : Singleton<InputManager>` (`GH.Runtime/InputManager.cs:18`) creates `GHControls PlayerControl` (`InitialiseInControlGHControls`, `:1130-1180`): an InControl `PlayerActionSet` with `MouseClickLeft/Right/Middle`, `MouseWheelDelta`, camera pan/rotate/zoom actions, and `KeyAction`-mapped game actions (`GHControls.GetPlayerActionForKeyAction`). `GHControls.GHAction` exposes `OnPressed/OnReleased` events and **`SimulateOnPress()`** (`GH.Runtime/GHControls.cs:61`) — but note `SimulateOnPress` only fires event subscribers, not `WasPressed` polling.
- **Static polling API** used everywhere: `InputManager.GetIsPressed/GetWasPressed/GetWasReleased/GetValue(KeyAction)` (`InputManager.cs:955-989`) and raw `InputManager.GetKey*(Key)` → `Utilities.InputSystemUtilities` (`Utilities/Utilities/InputSystemUtilities.cs`).
- **Cursor position**: `InputManager.CursorPosition` (static, `:126-154`). Mouse mode → `InputSystemUtilities.GetMousePosition()` (`Utilities/Utilities/InputSystemUtilities.cs:174`); gamepad mode → screen-projection of the currently UI-selected element via the camera tagged `"UICamera"`.
- **uGUI**: single `EventSystem` with `InControlInputModuleExtended : InControlInputModule` (`GH.Runtime/InControlInputModuleExtended.cs`), wired in `InputManager.InitialiseInControlGHControls` (`InputManager.cs:1169-1179`; submit/cancel/move actions, `allowMouseInput`).
- **Virtual mice (two!)**:
  1. Unity InputSystem device: `InputManager.CreateVirtualMouse()` (`InputManager.cs:267-284`) adds a real `Mouse` device ("ConsoleVirtualMouse") and drives it with `InputState.Change` / `WarpCursorPosition` — console/gamepad path. A VR mod can reuse exactly this to drive uGUI clicks.
  2. Game-level abstraction: `Utilities.VirtualMouseUtilities` (`Utilities/Utilities/VirtualMouseUtilities.cs`) — object with `_position/_isPressed/...`; once registered (`InputSystemUtilities.InitVirtualMouse` + `TryStart`), **all** `InputSystemUtilities.GetMousePosition/GetMouseButton*` calls route through it (`InputSystemUtilities.cs:91-186`). Caveat: InControl's `MouseClickLeft` binding reads InControl's own mouse provider, not this wrapper — so clicks for `Controller.CommonLoop` still need either the InputSystem virtual mouse or a Harmony patch on `CommonLoop`.
- **Input gating**: `InputManager.RequestDisableInput/RequestEnableInput(object, EKeyActionTag|KeyAction[])` (`:421-509`) — respect these in VR to avoid acting during cutscenes; `InteractabilityManager.ShouldAllowSelectionForTileIndex` gates tile picks (tutorials).
- **Recommended VR injection strategy** (least invasive first):
  1. World picks: prefix `MF.FindInteractableAtMousePosition` (+`FindNearestInteractableToPosition`) with a controller-ray raycast on `Controller.Instance.m_ActiveSelectionRaycastLayer`.
  2. Cursor-dependent code: prefix `InputManager.get_CursorPosition` returning `gameCamera.WorldToScreenPoint(vrRayHitPoint)`.
  3. Clicks: postfix `Controller.CommonLoop` (OR in trigger state, and replace the `IsPointerOverGameObject` suppression with a "VR pointer over UI" check) — or drive the InputSystem virtual mouse for a zero-patch click path that also feeds uGUI.
  4. Confirm/undo/rotate: call `ReadyButton`/`SkipButton`/`m_UndoButton` UI handlers and `WorldspaceStarHexDisplay.RotateAOEClockwise`.

## 6. Camera

- **No Cinemachine** (zero hits in the codebase). One hand-rolled controller: `CameraController` (`GH.Runtime/CameraController.cs:18`), singleton `s_CameraController`, holding `public Camera m_Camera` (the scenario camera; `Camera.main` used interchangeably in helpers).
- All motion happens in `CameraController.LateUpdate` (`:1262-1485`): orbit-style rig defined by `m_FocalPoint`/`m_TargetFocalPoint` on the ground plane (`s_GroundPlane = Plane(Vector3.up, 0)`, `:55`), horizontal angle snapped to 45° steps (`SmoothRotate`, `:1033`), **zoom implemented as FOV** (`m_TargetZoom`, clamped 30–75°, `c_minFOV/c_MaximumFOV` `:65-69`) plus extra height (`CalculateYForZoomFactor`, `:469`). Every frame it writes `m_Camera.transform.position` and `LookAt` (`RefreshFocusPosition`, `:1487-1503`). Mouse drag pans via ground-plane ray from `InputManager.CursorPosition` (`:1371-1391`). `ICameraBehavior` overrides (`SetOverriddenBehavior`, `:588`) and a `CameraTargetFocalFollowController` do scripted focus moves; `SmartFocus`/`MoveToLook`/`ZoomToFOV` are called from gameplay constantly.
- **For VR**: patching `CameraController.LateUpdate` (prefix returning false) or setting `m_IsCameraCodeControlDisabled = true` (`:202`, checked at `:1282`) stops all code-driven movement — but `SetCameraWithMessageProfile`, `ZoomTo`, camera shake (`m_EnableCameraShake`, `:1466-1470`), `PlayableDirector` timeline handoff (`:341-346`) and `SmartFocus` also write to it; safest is prefix-skip `LateUpdate`, `RefreshFocusPosition`, `SetCameraDirection`, and the coroutines, then drive an XR rig parented above the table. FOV writes (`m_Camera.fieldOfView`, `:1464`) must be neutralised — HMDs ignore FOV but Unity logs errors when XR is active.
- **Camera inventory**:
  - Scenario/game camera (`CameraController.m_Camera`, `Camera.main`; also referenced as `RoomVisibilityManager._scenarioCamera`, `GH.Runtime/RoomVisibilityManager.cs:18`).
  - UI camera, tag `"UICamera"` — `UIManager.UICamera` (`GH.Runtime/UIManager.cs:104`); screen-space-camera canvases; `InputManager.CursorPosition` gamepad branch searches `Camera.allCameras` for this tag (`InputManager.cs:136-143`).
  - Worldspace-UI camera: `WorldspaceUITools.worldspaceCamera` + `worldspaceCanvas` (`GH.Runtime/WorldspaceUITools.cs:30-34`) for health bars / tile icons above the board.
  - Occasional: `Character3DDisplayCameraSettings` (portrait render), `VideoCamera`.
- **Post-processing**: Post-Processing Stack **v2** on the game camera — `InitialiseAntialiasTechnique.m_Antialiasing : PostProcessLayer` (`GH.Runtime/InitialiseAntialiasTechnique.cs`), `UnityEngine.Rendering.PostProcessing` namespace; plus `VolumetricFogAndMist` (referenced in `CameraController.cs:16,965-974`), BeautifyEffect, DynamicFogAndMist in ThirdParty. Expect to disable most PPv2 effects and volumetric fog for VR (performance + stereo artifacts).
- **Camera-dependent rendering**: wall fade is a global shader toggle (`Shader.SetGlobalInt("ToggleWallFade", 1)`, `GH.Runtime/ActivateWallFadeInGame.cs:14`) whose shader presumably uses `_WorldSpaceCameraPos` — should track the VR eye automatically; `TilesOcclusionGenerator/Volume` bake occlusion volumes; `OffsetTowardsCamera` (`GH.Runtime/OffsetTowardsCamera.cs`) offsets props toward `Camera.main` once at `Start`; worldspace UI panels billboard toward `worldspaceCamera` (`WorldspaceUITools.cs:106-110`); `EPOOutline` outlines and `LeanTween`-driven `SmartFocus` cinematics assume the mono camera. Room reveal is not frustum-based (§2).

## 7. Turn / phase state machines (hooks for VR mode switching)

Three cooperating layers:

1. **Rules phases (SRL, authoritative)** — `PhaseManager` (`ScenarioRuleLibrary/ScenarioRuleLibrary/PhaseManager.cs:8`): static `CurrentPhase/Phase/PhaseType`. `CPhase.PhaseType` (`ScenarioRuleLibrary/ScenarioRuleLibrary/CPhase.cs:10-28`): `SelectAbilityCardsOrLongRest → (MonsterClassesSelectAbilityCards) → StartRoundEffects → CheckForInitiativeAdjustments → StartTurn → ActionSelection → Action → EndTurn(→EndTurnLoot) → … → EndRound`, plus `Autosave`, `PlayerExhausted`. One `CPhase*` class per phase (`CPhaseAction.cs`, `CPhaseSelectAbilityCardsOrLongRest.cs`, `CPhaseEndRound.cs`, …). `PhaseManager.TileSelected/TileDeselected` (`:37,45`) forward picks into the current phase. No C# event on phase change — poll `PhaseManager.PhaseType` or patch phase transitions.
2. **Choreographer wait-state (client)** — `Choreographer.ChoreographerStateType` (`GH.Runtime/Choreographer.cs:51-80`): `WaitingForCardSelection` (round start / card picking), `WaitingForPlayerWaypointSelection`, `WaitingForAreaAttackFocusSelection`, `WaitingForTileSelected`, `Play`, etc. Current value: `Choreographer.s_Choreographer.m_WaitState.m_State` (`CWaitState`, `:82,218`). Change point: `SetChoreographerState(ChoreographerStateType, int, CActor)` (`:2277`) — **postfix this for a reliable "VR interaction mode" signal.** Also `public event Action<bool> OnAoeTileSelected` (`:648`) and `ThisPlayerHasTurnControl` (`:557`).
3. **UI navigation state machine** — `Singleton<UINavigation>.Instance.StateMachine` (`GH.Runtime/Script.GUI.SMNavigation/UINavigation.cs:13`), a `NavigationStateMachine : Code.State.StateMachine` (`GH.Runtime/Script.GUI.SMNavigation/NavigationStateMachine.cs:12`) with one `IState` per screen/phase; scenario states in `GH.Runtime/Script.GUI.SMNavigation.States.ScenarioStates/` (tags in `ScenarioStateTag.cs`: `RoundStart, CardSelection, SelectAction, UseAction, SelectTarget, HexMovement, Damage, LongRest, EndResults, …`). **`StateMachine` exposes `public event Action<IState> EventStateChanged` (`GH.Runtime/Code.State/StateMachine.cs:52`)** and `IsCurrentState<T>()` — subscribe for phase-driven VR mode switching without any patch. `SelectTargetState.Enter/Exit` (`.../SelectTargetState.cs`) shows the pattern (hooks `OnAoeTileSelected`, registers hotkeys).
4. Round banners: `PhaseBannerHandler` (`GH.Runtime/PhaseBannerHandler.cs:10`, `PhaseBanner` enum `START_ROUND/PLAYER_TURN/ENEMY_TURN/END_ROUND/…`) — patchable for coarse round notifications.

Character placement at scenario start: `Choreographer.TileHandler` `WaitingForCardSelection` branch (`Choreographer.cs:1826-1868`) — click a hero → `InitiativeTrack.Instance.Select`, click a starting hex → `PlaceActorAtRoundStart` (`:2107`) (+`TileToken`/`Synchronizer` in MP).

## 8. Risks & gotchas

- **`Camera.main` coupling in picking**: `MF.FindInteractableAtMousePosition` and `HoverRegisterer` use `Camera.main` + screen-space cursor. If a VR camera becomes `MainCamera`-tagged, screen-space math changes meaning; prefer replacing rays wholesale (world-space origin/direction) rather than spoofing screen coordinates — but keep `InputManager.CursorPosition` patched for CameraController drag/tooltip/`UITooltip` (`GH.Runtime/UnityEngine.UI/UITooltip.cs` uses `Camera.main`) consumers.
- **Two-click confirm semantics**: `s_Callback`'s `actingPlayerHasSecondClickConfirmationEnabled` comes from `SaveData.Instance.Global.EnableSecondClickHexToConfirm`; VR clicks must pass it consistently or confirm flows (AoE lock, waypoint confirm) desync.
- **Multiplayer**: `TileHandler` branches on `FFSNetwork.IsOnline`/`ThisPlayerHasTurnControl` and replicates via `NetworkTargetSelection` (`Choreographer.cs:3360`) and `Synchronizer`. Injecting below `TileHandler` (e.g. calling `ScenarioRuleClient.TileSelected` directly) will break MP and undo/autotest logging — always enter via `s_Callback`/`ShowNormalInterface`.
- **SRL worker thread**: rules run off-main-thread; never call SRL state mutators directly from VR callbacks — only the queued `ScenarioRuleClient.*` API.
- **`Choreographer.m_TileSelectionDisabled` + `InteractabilityManager`** gate clicks during animations/tutorials; replicate the checks (`Controller.LateUpdate:176`, `TileHandler:1869`) or clicks will be buffered/eaten unexpectedly.
- **gamepad-variant scenes**: scene names differ (`"Game"` vs `"Game_gamepad"`, `SceneController.cs:203`) and `InputManager.GamePadInUse` changes `CursorPosition` semantics, UI navigation and `CInteractableTile` select-animation paths. For VR, keep the mouse/keyboard mode (`GamePadInUse == false`) and drive it like a mouse — the gamepad path adds a modal `ControllerInputAreaManager`/`UINavigation` layer that fights a free pointer.
- **FOV-as-zoom**: zoom is camera FOV; with XR the FOV writes are ignored/error — replace zoom with rig-scale or dolly, and neutralise `m_Camera.fieldOfView` writes (`CameraController.cs:1464,1529`).
- **PPv2 + VolumetricFog + EPOOutline + BeautifyEffect** are all image-effect era systems on the built-in pipeline; each needs stereo verification (likely disable fog/beautify, keep FXAA off, test outlines).
- **Worldspace UI canvases** (`WorldspaceUITools`, `WorldspaceTileBehaviourUI`, `ControllerInputWorldArea`) render via a dedicated camera; in VR they should be reparented to render in the main stereo pass, and their billboard math (`worldspaceCamera.transform.position`) pointed at the HMD.
- **Timekeeper/Chronos**: all input smoothing uses `Timekeeper.instance.m_GlobalClock` (pausable); VR locomotion/pointer should use unscaled real time to stay responsive during pauses (`TimeManager.IsPaused` blocks `WorldspaceStarHexDisplay.Update`, `:425`).
- **Decompiled line numbers** may drift slightly vs. other decompiler settings; anchor Harmony targets by signature, not line.
