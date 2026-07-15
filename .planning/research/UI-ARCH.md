# Gloomhaven UI Architecture — VR Mod Research

Sources: decompiled C# at `/home/claw/gloomhaven_vr/decompiled/` (all paths below relative to that root unless absolute). Verified by reading code — every claim has a `path:line` reference.

---

## 1. Overview

- **Framework: 100% uGUI (Canvas + UnityEngine.UI + TextMeshPro).** No UIElements/UIDocument anywhere (grep for `UnityEngine.UIElements|UIDocument|VisualElement` returns zero hits). IMGUI (`OnGUI`) appears only in debug/demo helpers (`GH.Runtime/MapChoreographer.cs` debug overlay, `VoiceChat/BoltVoiceMenu.cs`, RFX demo scripts) — nothing player-facing.
- **Canvases are Screen-Space Camera**, driven by a dedicated camera tagged `"UICamera"`. `CanvasManager` re-wires the persistent + tooltip canvases to whatever camera carries that tag after every scene load — `GH.Runtime/CanvasManager.cs:23-35`.
- **Window system:** a custom `UIWindow`/`UIWindowManager` component system (in the `UnityEngine.UI` namespace, game code) identified by the `UIWindowID` enum. No screen *stack*; windows are flat CanvasGroup-based show/hide with an "escapable" priority list for ESC handling.
- **Input:** InControl-based `InControlInputModuleExtended` (subclass of the InControl asset's `InControlInputModule`) is the live uGUI input module; the game additionally uses Unity's **new Input System** for raw mouse reads and even creates a **virtual `Mouse` device** for gamepad/console play — a ready-made injection point for a VR pointer.
- **"Worldspace" UI is fake:** health bars, condition icons, damage numbers over actors are on ONE shared screen-space canvas, positioned each `LateUpdate` via `WorldToScreenPoint` (`GH.Runtime/WorldspaceDisplayPanelBase.cs:181-197`). In VR these must be re-projected (they will otherwise be head-locked billboards on the HUD plane).
- **Localization:** I2 Localization behind the static facade `GLOOM.LocalizationManager` — trivially reusable for VR labels; mod strings can be injected via `AddCSVSource`.

---

## 2. Framework & canvases

### 2.1 Canvas / camera setup

| Thing | Evidence |
|---|---|
| Persistent UI canvas + tooltip canvas, Screen-Space **Camera**, camera found by tag `"UICamera"` on every `sceneLoaded` | `GH.Runtime/CanvasManager.cs:7-11` (serialized `persistentUICanvas`, `tooltipCanvas`), `:23-35` (`OnSceneLoaded` sets `canvas.worldCamera = allCameras[i]` where `tag == "UICamera"`) |
| Main in-scenario overlay canvas + UI camera held by `UIManager` | `GH.Runtime/UIManager.cs:78-81` (`[SerializeField] Camera uiCamera; Canvas uiOverlayCanvas`), `:104` (`public Camera UICamera`), `:142` (`public Canvas UICanvas => uiOverlayCanvas`) |
| Global cross-scene canvas on the persistent `SceneController` object | `GH.Runtime/SceneController.cs` — `public GameObject GlobalCanvas;` enabled in `Awake`; global error popups instantiated under it (`Addressables.InstantiateAsync(key, GlobalCanvas.transform, …)` ~line 626) |
| Map scenes activate their own overlay canvases | `GH.Runtime/MapChoreographer.cs` — fields `_mapCanvas`, `_campaignCanvas`, `_mapEscMenuCanvas` |
| UI scale option manipulates a `CanvasScaler.scaleFactor` directly (so the scaler is in **Constant Pixel Size** mode, scaled by settings) | `GH.Runtime/Gloomhaven/InterfaceSettings.cs:8-9,87-90` (`uiCanvasScaler.scaleFactor = Utility.Round(scaleFactor, 2)`) |
| Initiative track owns its own `Canvas` with `sortingOrder = 40` | `GH.Runtime/InitiativeTrack.cs` (per surface inventory) |
| `UITooltip` is canvas-render-mode aware — already handles `ScreenSpaceOverlay`, `ScreenSpaceCamera`, and world camera cases | `GH.Runtime/UnityEngine.UI/UITooltip.cs:307-325` (`uiCamera` property) |

**Consequence for VR:** because everything is Screen-Space *Camera* (not Overlay), the cheapest first-pass VR approach is to (a) keep the `"UICamera"` rendering to a `RenderTexture` shown on a floating quad, or (b) flip the canvases to `RenderMode.WorldSpace` and re-drive `worldCamera`. `CanvasManager.OnSceneLoaded` and `UIManager.uiCamera` are the two places that re-bind the camera and must be patched/observed.

### 2.2 Scene structure (from SceneController)

`GH.Runtime/SceneController.cs`: enum `ESceneType { None, Empty, MainMenu, CampaignMap, NewAdventureMap, Scenario }`. Scene names resolve to a keyboard and a **`_gamepad` variant** chosen via `InputManager.GamePadInUse` (`GetSceneNameForType`): `MainMenu[_gamepad]`, `CampaignMap[_gamepad]`, `NewAdventureMap[_gamepad]`, `Game[_gamepad]` (the scenario/combat scene), plus `EmptyScene` and `InitialLoadingScreen`. All loaded **additively** (`SceneManager.LoadSceneAsync(..., LoadSceneMode.Additive)` ~line 1050), previous scene unloaded. Persistent objects live on the never-unloaded bootstrap scene: `SceneController` (with `GlobalCanvas`, `MainSceneCamera`, `LoadingScreenGO`), plus `Singleton<T>`-pattern managers (`UINavigation` calls `DontDestroyOnLoad` in `Setup()` — `GH.Runtime/Script.GUI.SMNavigation/UINavigation.cs:19`).

> VR note: the `_gamepad` scene duplication means UI prefab hierarchies differ between mouse and gamepad modes; target the keyboard/mouse variants.

---

## 3. Screen/window management

### 3.1 UIWindow system (the closest thing to a "window manager")

- **`UnityEngine.UI.UIWindow`** — `GH.Runtime/UnityEngine.UI/UIWindow.cs:15` — `[RequireComponent(typeof(CanvasGroup))] public class UIWindow : MonoBehaviour, ISelectHandler, IPointerDownHandler, IEscapable, IShowActivity`. Identified by `UIWindowID m_WindowId` (`:74`) + optional `m_CustomWindowId`. Show/hide = CanvasGroup alpha tween (`Transition.Instant|Fade`), events `onShown/onHidden/onTransitionBegin/onTransitionComplete` (`:131-139`). Static registry: `UIWindow.GetWindows()` (a `HashSet<UIWindow>`, `:69`), `UIWindow.FocusedWindow` (`:148`). Per-window `EscapeKeyAction { None, Hide, HideIfFocused, Toggle, HideOnlyThis, Skip }` (`:29-37`).
- **`UnityEngine.UI.UIWindowManager`** — `GH.Runtime/UnityEngine.UI/UIWindowManager.cs:9` — `Singleton<UIWindowManager>`. Not a stack; it owns the **escapable list**: `RegisterEscapable(IEscapable)` / `UnregisterEscapable` (`:167-187`), and `Escape()` (`:84-116`) walks listeners sorted by `IEscapable.Order()` until one consumes the ESC. Bound to `KeyAction.UI_CANCEL` (`:22`). Bulk ops: `HideOrShowWindows`, `ForceHideWindows`, skip-lists by `UIWindowID` (`:36-165`).
- **`UIWindowID`** — `GH.Runtime/UIWindowID.cs:1-46` — 45 IDs: `Options, Village, Shop, ConfirmationBox, PartyPanel, CardHolder, MapNodeInfoPanel, EventsPanel, RewardsPanel, ResultsPanel, HeroLevelUpPanel, TakeDamagePanel, ActorStatPanel, AdventureCompletionPanel, TrapInfoPanel, DurabilityPanel, DifficultyPanel, Message, ESCMenu, CompendiumPanel, MapObjectiveManager, HelpBox, TextInfoPanel, DoorInfoPanel, EnhancementShop, … EnemyCurrentTurnStatPanel` etc.
- **Higher-level navigation** (gamepad focus, not windowing): `Script.GUI.SMNavigation/UINavigation.cs:7` — `Singleton<UINavigation>` owning `SmInput`, `UiNavigationManager`, `NavigationStateMachine` (states in `Script.GUI.SMNavigation.States.*`, e.g. `ScenarioStates`, `CampaignMapStates`, `MainMenuStates`). `ControllerInputAreaManager` focuses "areas" (`EControllerInputAreaType`). Relevant to VR only if we reuse gamepad-style focus; a pointer-based VR UI can ignore it.
- **Modal dialogs:** `UIConfirmationBoxManager` — `GH.Runtime/UIConfirmationBoxManager.cs:9` — `Singleton<UIConfirmationBoxManager>` with `ShowGenericConfirmation(title, explanation, onActionConfirmed, onActionCancelled, …)` (`:80`), `ShowGenericWarningConfirmation` (`:75`), `ShowGenericSpendConfirmation` (`:85`), `Hide()` (`:95`), `IsRequested` (`:22`). A separate `MainMenuInstance` exists (`UIManager.cs:386`).

### 3.2 Inventory of in-scenario UI surfaces (all uGUI)

| Surface | Class(es) | Path | Notes |
|---|---|---|---|
| Initiative tracker | `InitiativeTrack` (+ `InitiativeTrackPlayerBehaviour` / `InitiativeTrackEnemyBehaviour`, avatars) | `GH.Runtime/InitiativeTrack.cs`, `InitiativeTrack*.cs` | Plain MonoBehaviour, own `Canvas` (sortingOrder 40), `HorizontalLayoutGroup`; fed by `UpdateInitiativeTrack(List<CActor>, …)` |
| Character HUD / party bar | `APartyDisplayUI` (abstract `Singleton<APartyDisplayUI>`) → `NewPartyDisplayUI`; per-char `NewPartyCharacterUI` | `GH.Runtime/APartyDisplayUI.cs`, `NewPartyDisplayUI.cs`, `NewPartyCharacterUI.cs` | Hosts sub-windows: perks, equipment, cards, battle goals via `DisplayType` enum |
| Actor stat sheet (yours + enemies) | `ActorStatPanel` | `GH.Runtime/ActorStatPanel.cs` | `[RequireComponent(typeof(UIWindow))] Singleton<ActorStatPanel>`; HP, stats, AM-deck odds (`UIAttackModifierCalculator`) |
| Enemy current-turn card | `EnemyCurrentTurnStatPanel` | `GH.Runtime/EnemyCurrentTurnStatPanel.cs` | `Singleton`, UIWindow; `Show(CEnemyActor)` renders `MonsterClass.RoundAbilityCard` into `MonsterBaseUI` |
| Monster round card | `MonsterRoundCardUI`, `MonsterBaseUI` | `GH.Runtime/MonsterRoundCardUI.cs` | Pooled (`IPooleable`) |
| Element infusion board | `InfusionBoardUI` (+6× `InfusionElementUI`) | `GH.Runtime/InfusionBoardUI.cs`, `InfusionElementUI.cs` | Ad-hoc `static Instance`; 6 elements from `ElementInfusionBoardManager.EElement`; `UpdateBoard`, `ReserveElement`, events `OnReservedElement` |
| Ability card hand | `CardsHandManager` (`.Instance`), `CardsHandUI`, `AbilityCardUI` | `GH.Runtime/CardsHandManager.cs`, `CardsHandUI.cs`, `AbilityCardUI.cs` | Owns `UIWindow window`; long-rest button `LongRestConfirmationButton` |
| Attack modifier display | `UIAttackModifier<T>`, `UIScenarioAttackModifier`, `UIAttackModifierCalculator`; in-world reveal `AttackModBar` | `GH.Runtime/UIScenarioAttackModifier.cs`, `WorldspaceUI/AttackModBar.cs` | Counters sourced from `ActorStatPanel.Instance` |
| Loot/gold/XP notifications | `InfoBar.ShowGold/ShowXP` (per actor), `UINotificationManager` (toasts), `GoldCounter` | `GH.Runtime/WorldspaceUI/InfoBar.cs`, `UINotificationManager.cs`, `GoldCounter.cs` | `Singleton<UINotificationManager>.ShowNotification(NotificationData, …)` |
| Round/phase banners | `PhaseBannerHandler` | `GH.Runtime/PhaseBannerHandler.cs` | `Singleton`, UIWindow; enum `PhaseBanner { ENEMY_TURN, PLAYER_TURN, START_ROUND, END_ROUND, EXHAUSTED, DEATH, PRE_DEATH }`; `interactionBlock` Image blocks input while showing |
| Context menus | **none** (no radial/context menu class) | — | Role filled by option panels: `AbilityOption`, `ChooseAbilityOptionController`, `InitiativeOptionController`, `UseAugmentationOptionController` + worldspace tile UI (`WorldspaceTileBehaviourUI`, `WorldspaceStarHexDisplay`) |
| Combat log | `CombatLogHandler` (+ pooled `CombatLogText`) | `GH.Runtime/CombatLogHandler.cs` | `Singleton`, UIWindow, `ScrollRect`, filterable |
| Objectives / scenario header | `MissionObjectiveContainer`, `MissionObjectiveUI`, `ScenarioModifierContainer`, `BattleGoalContainer` | `GH.Runtime/MissionObjectiveContainer.cs` etc. | Initialized by `UIManager.InitScenario(...)` — `GH.Runtime/UIManager.cs:203` |
| In-scenario item/ability/bonus bars | `UIUseItemsBar`, `UIUseAbilitiesBar`, `UIUseAugmentationsBar`, `UIActiveBonusBar` | `GH.Runtime/UIUse*.cs`, `UIActiveBonusBar.cs` | All `Singleton<T>`; shown/hidden by the button flow (see §5) |
| Per-actor overhead UI | `WorldspacePanelUIController` → `HealthBar`, `EffectsBar`, `ShieldBar`, `AttackModBar`, `InfoBar`, `WorldspaceCharacterInfoPanel` | `GH.Runtime/WorldspacePanelUIController.cs`, `WorldspaceUI/*.cs` | See §3.3 |
| Popups anchored to slots | `SlotPopup` base → `StatusEffectPopup`, `ImmunityPopup`, `PassivePerkPopup`, `PersistentAbilityPopup` | `GH.Runtime/SlotPopup.cs` etc. | `[RequireComponent(UIWindow)]` |
| ESC menu / options | `ESCMenu` (`Singleton<ESCMenu>`), `UIOptionsWindow` | `GH.Runtime/ESCMenu.cs`, `UIOptionsWindow.cs` | Referenced from `UIManager.LoadMainMenu` — `UIManager.cs:392-393` |
| Results / rewards | `UIResultsManager` (`Singleton`) | `GH.Runtime/UIResultsManager.cs` | `IsShown` gates many button paths |

### 3.3 The "Worldspace" UI is screen-space (critical for VR)

- `WorldspaceUITools` — `GH.Runtime/WorldspaceUITools.cs:29-36` — holds **one shared** `Canvas worldspaceCanvas` + `Camera worldspaceCamera` (set via `Init(Camera)` `:62`), registry of `WorldspacePanelUIController`s, and re-sorts panel sibling order by camera distance every `Update` (`:104-133`).
- `WorldspaceDisplayPanelBase.TrackCharacter()` — `GH.Runtime/WorldspaceDisplayPanelBase.cs:181-197` — per `LateUpdate`: track actor head bone `"C_headSkel01_JNT"` or `"Base"` transform → `WorldspaceCamera.WorldToScreenPoint` → `RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPoint, canvas.worldCamera, out localPoint)` → set `RectTransform.localPosition`. Distance-based scaling via `m_ScalingCurve` (`:146-179`).
- **VR approach:** these panels are the *best candidates for true 3D physicalization* — the data flow (`WorldspacePanelUIController.UpdateHealth/UpdateEffects/ShowDamage/OnWonGold/OnEarnedXP`) is already per-actor; a Harmony patch can re-parent each panel to a real world-space canvas above the actor and no-op `TrackCharacter`, since positioning logic is isolated in `WorldspaceDisplayPanelBase.LateUpdate/TrackCharacter`.

### 3.4 Out-of-scenario UI (keep on virtual 2D screen)

All the same uGUI + `UIWindow` + `UINavigation` stack; no separate framework anywhere:

- **Main menu:** `MainMenuUIManager` — `GH.Runtime/GLOOM.MainMenu/MainMenuUIManager.cs` (static `Instance`; owns `MainMenuCamera`, disables `SceneController.MainSceneCamera`). Panels: `UIMainOptionsMenu`, `UIMainMenuModeSelection`, `CustomPartySetup`, `EULAScreen`, `UICreditsWindow`.
- **Guildmaster / campaign map:** 3D scene + 2D overlays. `MapChoreographer` (`GH.Runtime/MapChoreographer.cs`, `Singleton`) drives 3D camera via `CameraController` (`GH.Runtime/CameraController.cs`, `public Camera m_Camera`) and activates `_mapCanvas`/`_campaignCanvas`/`_mapEscMenuCanvas`. UI manager: `AdventureMapUIManager` (`GH.Runtime/AdventureMapUIManager.cs`, `Singleton, IEscapable`); Guildmaster HUD: `UIGuildmasterHUD` (`GH.Runtime/UIGuildmasterHUD.cs`).
- **Merchant/shop:** `UIShopItemWindow` (`GH.Runtime/UIShopItemWindow.cs`, `[RequireComponent(typeof(UIWindow))] Singleton`), plus `UIShopItemInventory/Slot/Filter`, temple + enhancement variants; backing non-UI services `ShopService`, `TempleShopService`.
- **Level-up:** `UILevelUpWindow` (`GH.Runtime/UILevelUpWindow.cs`, `Singleton, IEscapable`) + `UILevelUpCard*`; Guildmaster variant `GuildmasterLevelUpTab` (`GH.Runtime/GuildmasterLevelUpTab.cs`, `INavigationTab`). (No `HeroLevelUpPanel` class exists despite the `UIWindowID.HeroLevelUpPanel` id.)
- **Character creator:** `UICharacterCreatorWindow` (`GH.Runtime/UICharacterCreatorWindow.cs`, `[RequireComponent(typeof(UIWindow))]`) + step panels.

---

## 4. EventSystem / raycast plumbing — how to inject a VR pointer

### 4.1 Input modules in play

| Class | Path | Role |
|---|---|---|
| `InControlInputModuleExtended : InControlInputModule, IInputModulePointer` | `GH.Runtime/InControlInputModuleExtended.cs:5` | **The live input module.** Static `Instance` (`:13`), enables `allowMouseInput = true` and touch per platform in `Start()` (`:34-44`). Base class `InControl.InputModule` comes from the InControl plugin assembly (not in the decompiled set — it's a `StandaloneInputModule`-style module fed by InControl devices). |
| `StandaloneInputModuleExtended : StandaloneInputModule, IInputModulePointer` | `GH.Runtime/StandaloneInputModuleExtended.cs:4` | Alternate/legacy module, same pointer-query interface. |
| `IInputModulePointer` | `GH.Runtime/IInputModulePointer.cs` | One method: `GameObject GameObjectUnderPointer(int pointerId = -1)` — implemented by both modules via `GetLastPointerEventData(pointerId)?.pointerCurrentRaycast.gameObject`. |
| `EventSystemChecker` | `GH.Runtime.FirstPass/EventSystemChecker.cs:6-14` | Fallback: creates `EventSystem` + `StandaloneInputModule` (`forceModuleActive = true`) if none exists. |
| `InputManager` (game's own, `Singleton<InputManager>`) | `GH.Runtime/InputManager.cs:18` | Holds `private InControlInputModule m_CurrentInputModule` (`:24`) and subscribes to its `InputVectorEvent`/`PreUnityInputVectorEvent` (`:209-210`). Wraps InControl action sets (`GHControls`, `PlayerActionControlsProvider`) *and* Unity Input System keys. |

### 4.2 Where the pointer position comes from

- `InputManager.CursorPosition` — `GH.Runtime/InputManager.cs:126-154`: mouse mode returns `InputSystemUtilities.GetMousePosition()`; gamepad mode returns the screen position of the currently navigation-selected element (projected via the `"UICamera"`-tagged camera, `:136-147`).
- `InputSystemUtilities.GetMousePosition()` — `Utilities/Utilities/InputSystemUtilities.cs:174-187`: reads **`UnityEngine.InputSystem.Mouse.current.position`**, or a registered `VirtualMouseUtilities` if active.
- **Virtual mouse (the golden injection point):** `InputManager.CreateVirtualMouse()` — `GH.Runtime/InputManager.cs:267-284` — `InputSystem.AddDevice<Mouse>("ConsoleVirtualMouse")`, pairs it, `MakeCurrent()`, `WarpCursorPosition(...)` + `InputState.Change(...)`. The game itself already drives all UI from a synthetic mouse on console.
- World-object clicks (hexes, doors, props) do **not** go through uGUI: `ClickTracker` (`GH.Runtime/ClickTracker.cs:9`) implements only `IPointerEnter/ExitHandler` for hover (via PhysicsRaycaster on the scene camera) and polls `PlayerControl.MouseClickLeft/Right/Middle.WasPressed` from InControl in `Update` (`:45-78`). Variants: `ClickTrackerExtended`, `ClickTrackerMap`, `MouseOverUIElement`. Tile logic: `TileBehaviour` (`GH.Runtime/TileBehaviour.cs`) + `Choreographer.TileHandler` callback.

### 4.3 Raycasters

- Standard `GraphicRaycaster` per canvas. `UIManager` serializes a main `graphicRaycaster` + `List<GraphicRaycaster> graphicRaycasters` and locks UI by disabling them: `ToggleLockUI` — `GH.Runtime/UIManager.cs:60-63,306-313`; ref-counted `RequestToggleLockUI` (`:315-326`). **A VR mod must respect/reuse this lock** (patching `ToggleLockUI` or reading `elementsLockUI`).
- Custom `ICanvasRaycastFilter`s (shape-limited hit areas — they keep working under any raycaster): `UICircularRaycastFilter` (`GH.Runtime/UnityEngine.UI/UICircularRaycastFilter.cs:5`), `UIRectangularRaycastFilter`, `UIMaskRaycastFilter`, `UIIgnoreRaycast` (same folder).
- Hover state queries used by game logic: `UIManager.IsPointerOverUI` — `GH.Runtime/UIManager.cs:108-122` (uses `EventSystem.current.IsPointerOverGameObject()` + `EventSystem.current.RaycastAll` at `InputManager.CursorPosition` for gamepad, `:124-140`); `UIManager.GameObjectUnderPointer()` — `:272-275` (casts `EventSystem.current.currentInputModule` to `IInputModulePointer`).

### 4.4 Recommended VR injection strategies (in order of least invasion)

1. **Virtual-mouse warping:** raycast VR controller → UI plane (the quad rendering the UI camera's RenderTexture) → convert hit UV to screen pixels → `InputState.Change(mouse.position, px)` on the game's (or our own) `InputSystem` Mouse device, and inject clicks the same way. Everything downstream (InControl module reads mouse via Unity, `ClickTracker` reads `Mouse.current` through InControl bindings, `IsPointerOverUI`, tooltips) keeps working untouched. The game's own `CreateVirtualMouse` (`InputManager.cs:267`) proves the pattern is supported end-to-end.
2. **Custom `BaseInput` override** on `InControlInputModuleExtended` (`BaseInputModule.inputOverride`) returning VR-pointer screen coordinates — cleaner but must coexist with InControl's own mouse handling in the plugin assembly.
3. **World-space canvases + custom raycaster:** flip canvases to WorldSpace and add a `OVRRaycaster`-style `GraphicRaycaster` subclass driven by a controller ray; requires re-implementing pointer events (a full custom InputModule) and patching `UIManager.IsPointerOverUI` / `InputManager.CursorPosition` — heavier, needed only for fully physicalized panels.
4. Programmatic clicking for physical 3D buttons: the game itself does `ExecuteEvents.Execute(buttonGO, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler)` — `GH.Runtime/BaseButtons.cs:115-121` — or call the public `OnClick()`/manager methods in §5 directly.

---

## 5. Key buttons & commit methods (candidates for physical 3D buttons)

The scenario HUD's bottom-center button cluster is owned by `Choreographer` (the scenario orchestrator, `GH.Runtime/Choreographer.cs:48`): `public ReadyButton readyButton` (`:150`), `public UndoButton m_UndoButton` (`:187`), `public SkipButton m_SkipButton` (`:189`), `public SelectButton m_selectButton` (`:191`), `private FastForwardButton _fastForwardButton` (`:224`). `BaseButtons` (`GH.Runtime/BaseButtons.cs:8`) maps hotkeys to these via `GameObject[] baseButtonList` — index 0=undo/clear, 1=confirm, 2=skip, 3/4/5=damage-avoidance choices (`:84-113`), clicking through `ExecuteEvents` (`:115-121`).

| Purpose | Class | Method (signature) | Path:line |
|---|---|---|---|
| **End turn / confirm** (multi-role: end selection, end round, end turn, pass, long rest, confirm targets/movement/item, open door — see `EButtonState`, 17 states) | `ReadyButton : ButtonOnBlockingPanel` | `public void OnClick(bool networkActionIfOnline = true)`; real commit: `public void OnClickInternal(bool networkActionIfOnline = true)`; `public void Toggle(bool active, EButtonState state = EREADYBUTTONNA, string text = null, bool hideOnClick = true, …)`; `public void SetInteractable(bool interactable, bool disregardTurnControl = false)`; `public void QueueAlternativeAction(Action extraAction)` | `GH.Runtime/ReadyButton.cs:187,245,444,480,381`; states enum `:17-35` |
| **Undo / clear targets** | `UndoButton : ButtonOnBlockingPanel, IEscapable` | `public void OnClick(bool networkActionIfOnline = true)`; `public bool OnClickInternal(bool networkActionIfOnline = true, GameAction action = null)` → sends `GameActionType.UndoAction`/`ClearTargets`, calls `ScenarioRuleClient.Undo(...)` / `ScenarioRuleClient.ClearTargets()`; `public void Toggle(bool active, EButtonState buttonState = EUNDOBUTTONUNDO, string text = null)`; override stack `SetOnClickOverrider(Action, EButtonState, string)` | `GH.Runtime/UndoButton.cs:94,108,252,209` |
| **Skip (skip movement/attack step)** | `SkipButton : ButtonOnBlockingPanel` | `public void OnClickFromButton()`; `public void OnClick(bool networkActionIfOnline = true)` → `Synchronizer.SendGameAction(GameActionType.SkipAction, …)`, `Choreographer.Pass()` / `ScenarioRuleClient.PassStep()`; `public void Toggle(bool active, string text = null, bool hideOnClick = true, bool? interactable = null, Action onSkipAction = null)` | `GH.Runtime/SkipButton.cs:68,80,137` |
| **Select** (contextual "select" prompt) | `SelectButton : MonoBehaviour` | `public void SetActive(bool activate, string text = null)`; `public void UpdateText(string text)`; `public void PlayAnimation(Action callback)`; toggled by `Choreographer.SetActiveSelectButton(bool)` | `GH.Runtime/SelectButton.cs:71,77,62` |
| **Long rest confirm** | `LongRestConfirmationButton : MonoBehaviour` | `public void ShowConfirm(CPlayerActor playerActor, Action<bool> onToggled)`; `public void ShowUsed(CPlayerActor)`; `public void Hide()` | `GH.Runtime/LongRestConfirmationButton.cs:104,109,114` |
| **Fast-forward / speed-up** | `FastForwardButton : Singleton<FastForwardButton>` | `public void Toggle(bool active)` | `GH.Runtime/Script.GUI.GameScreen/FastForwardButton.cs:8,52` |
| Hotkey → click bridge (damage-avoidance dialog etc.) | `BaseButtons` | `private void clickButton(GameObject)` via `ExecuteEvents.Execute(..., pointerClickHandler)`; `public void Toggle(object requester, bool isEnabled)` | `GH.Runtime/BaseButtons.cs:115,128` |
| Generic modal confirm | `UIConfirmationBoxManager : Singleton<…>` | `public void ShowGenericConfirmation(string title, string explanation, UnityAction onActionConfirmed, UnityAction onActionCancelled = null, string confirmButtonKey = null, string cancelButtonKey = null, …)`; `public void Hide()` | `GH.Runtime/UIConfirmationBoxManager.cs:80,95` |

Shared plumbing for these buttons:
- Base: `ButtonOnBlockingPanel` — `GH.Runtime/Script.GUI.GameScreen/ButtonOnBlockingPanel.cs:6` — `ToggleVisibility(bool)`, `IsInteractable()`, CanvasGroup alpha handling.
- Button widget: `ExtendedButton : Button, IInteractable` — `GH.Runtime/ExtendedButton.cs:14` — adds TMP label + `textLanguageKey`, hover scale/movement, `TooltipUI tooltip`, `onMouseEnter/onMouseExit/onSelected/onDeselected` UnityEvents, `alternativeInputKey`. VR hover feedback can subscribe to these events directly.
- Click gate: `InteractabilityManager.ShouldAllowClickForExtendedButton(ExtendedButton)` — `GH.Runtime/InteractabilityManager.cs:99` — FTUE/tutorial gating; physical VR buttons should route through `OnClick()` (which already checks it) rather than the internals.
- Interactability of ready/skip/undo is recomputed every frame from scenario state (`CheckButtonInteractability` — `SkipButton.cs:159`, `UndoButton.cs:292`); mirror `interactable` onto physical buttons rather than re-deriving.

---

## 6. Text, tooltips

- **Text:** TextMeshPro (`TextMeshProUGUI`) everywhere (e.g. `ReadyButton.cs:40`, `SkipButton.cs:18`); legacy `UnityEngine.UI.Text` only in old bits (`UIManager.debugText:69`, `InitiativeDisplayGUI`). TMP renders fine on world-space canvases — no obstacle for VR.
- **Central tooltip:** `UnityEngine.UI.UITooltip` — `GH.Runtime/UnityEngine.UI/UITooltip.cs:14` — singleton (`mInstance`) with an entirely **static** API: `AddTitle/AddDescription/AddLine/SetLines` (`:914-1010`), `Show(float delay = -1, RectTransform tooltipTransform = null)` (`:1018`), `Hide` (`:1026`), `AnchorToRect(RectTransform, Corner)` (`:1034`), `SetScreenBound` (`:1106`). It lives on the dedicated `tooltipCanvas` wired by `CanvasManager` (`CanvasManager.cs:11,31`) and already resolves its camera per canvas render mode (`:307-325`) → **reusable in world space** by moving the tooltip canvas or re-anchoring via `AnchorToRect` with a VR-cursor-attached rect.
- **Trigger component:** `UITooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler` — `GH.Runtime/UITooltipTarget.cs:5` — with `OnPointerEnter/Exit` (`:71,80`) and gamepad `OnNavigationSelect/Deselect` (`:106,115`); abstract, subclasses `UITextTooltipTarget`, `UIPrefabTooltipTarget`, `UIItemCardTooltipTarget`. A VR ray-pointer that produces real `pointerEnter/Exit` events gets all tooltips for free.
- Secondary tooltip widgets: `TooltipUI` (`GH.Runtime/TooltipUI.cs:5`, `Init(string descriptionKey, string warningKey = null)` `:40` — attached to `ExtendedButton`), `UILocalTooltip`/`UIItemTooltip`/`UIAbilityTooltip` etc., `CompendiumTooltip`, `UIPingTooltip` (multiplayer ping, screen-space offset at `UIPingTooltip.cs:120`).

## 7. Localization (brief)

- Engine: **I2 Localization** (`ThirdParty/I2.Loc/LocalizationManager.cs:12`, `public static class LocalizationManager`; `CurrentLanguage` property `:53`).
- Game facade — use this from the mod: `GLOOM.LocalizationManager` — `GH.Runtime/GLOOM/LocalizationManager.cs:10`:
  - `public static string GetTranslation(string Term, bool FixForRTL = true, …, bool returnNullIfNotFound = false)` (`:12`) — lowercases terms, handles `$…$` card keys, English fallback, `"UNDEFINED"` marker on miss.
  - `public static bool TryGetTranslation(string Term, out string Translation, …)` (`:47`).
  - **Mod strings:** `public static bool AddCSVSource(string csvPath)` (`:61`) / `InsertCSVSource(csv, index)` (`:72`) merge a CSV into `I2.Loc.LocalizationManager.Sources` — exactly how DLC languages load.
- UI binding: `I2.Loc.Localize` MonoBehaviour (`ThirdParty/I2.Loc/Localize.cs`) with TMP target `LocalizeTarget_TextMeshPro_UGUI`; game-side `TextLocalizedListener : LocalizedListener` (`GH.Runtime/TextLocalizedListener.cs`) re-translates on language change. VR labels: call `GLOOM.LocalizationManager.GetTranslation("GUI_UNDO")` etc. (keys visible throughout, e.g. `UndoButton.cs:67`, `SkipButton.cs:47`).

## 8. Cursor / hover intent

- **No OS hardware-cursor manager**: zero `Cursor.SetCursor` calls in the entire codebase (grep verified). Mouse mode uses the default OS cursor; `Cursor.lockState` is only touched for console virtual mouse (`InputManager.cs:279-283`).
- **Gamepad virtual cursor:** `ControllerInputPointer : ControllerInputElement` — `GH.Runtime/ControllerInputPointer.cs:12` — a screen-center `Image` cursor with `static ECursorType CursorType` (`:44-65`) and sprite swap `SetCursorType(ECursorType)` (`:116`). `ECursorType { Invalid, Default, Targeted }` — `GH.Runtime/ECursorType.cs`. Set from `Controller.cs:175,202` based on whether a valid tile is under the pointer. Shown only during specific `UINavigation` scenario states (`RoundStartScenarioState`, `HexMovementOnSelectActionState`, `UseActionScenarioState`, `SelectTargetState`, `DamageScenarioState`, `AbilityActionsScenarioState` — `:28-36`) — this state list is itself a useful "current interaction intent" signal.
- **Richer intent sources for VR hand feedback** (since ECursorType is coarse):
  - `Singleton<UINavigation>.Instance.StateMachine` current state + `EventStateChanged` event (`ControllerInputPointer.cs:85`) — tells move/target/damage/ability phase.
  - `ReadyButton`'s current `EButtonState` (end turn vs confirm move vs open door …) and `UndoButton.EButtonState` (undo vs clear targets).
  - Hover outlining: `WorldspaceUITools.EnableHoveredOutline/DisableHoveredOutline` (`GH.Runtime/WorldspaceUITools.cs:156-175`) — patch these to drive controller haptics when pointing at a hoverable actor.
  - `UIManager.IsPointerOverUI` (`UIManager.cs:108`) — distinguishes UI-pointing vs world-pointing.

## 9. Risks & gotchas for the VR mod

1. **InControl base classes are in a separate plugin assembly** (`InControl.dll` — `InControlInputModule` isn't in the decompiled set). Harmony patches on module internals must target that assembly; prefer the virtual-mouse injection (§4.4) which avoids touching it.
2. **Dual scene variants** (`Game` vs `Game_gamepad`, etc.) mean two different UI prefab sets; the mod must handle whichever loads (`SceneController.GetSceneNameForType` decides via `InputManager.GamePadInUse`). Forcing non-gamepad mode keeps the mouse-oriented UI (recommended for pointer-based VR).
3. **UI lock via raycaster disabling** (`UIManager.ToggleLockUI`, `UIManager.cs:306`): if the VR pointer bypasses `GraphicRaycaster` (e.g. direct `ExecuteEvents`), it also bypasses the game's modal locking — always check `graphicRaycaster.enabled` / use the normal event path.
4. **Per-frame interactability recomputation** on scenario buttons (`CheckButtonInteractability` in Ready/Skip/Undo) — physical buttons must mirror `Button.interactable` every frame, not cache it.
5. **"Worldspace" bars are screen-space + distance-scaled** (`WorldspaceDisplayPanelBase`); naive world-space canvas conversion leaves `LateUpdate` fighting the mod every frame — disable `TrackCharacter` (Harmony prefix returning false) when re-parenting, and re-implement occlusion ordering (`WorldspaceUITools.OrderWorldspaceUIPanels`, `WorldspaceUITools.cs:104`).
6. **`IsPointerOverUI` and `CursorPosition` assume one screen** (`Screen.width/height`, `Camera.allCameras` tag search — `InputManager.cs:136-150`); with a VR headset camera present, tag lookups (`"UICamera"`) can mis-resolve if the VR rig's cameras are tagged carelessly — never tag VR cameras `UICamera`, and keep `Camera.main` semantics intact for `UITooltip.uiCamera` fallback (`UITooltip.cs:323`).
7. **Banners block input with a fullscreen Image** (`PhaseBannerHandler.interactionBlock`) and `BaseButtons` fires clicks through `ExecuteEvents` from `Update` — synthetic VR clicks arriving mid-banner can desync the scenario state machine; gate VR clicks on `UIManager.IsPointerOverUI` + `ScenarioRuleClient.IsProcessingOrMessagesQueued` like the game does (`UndoButton.cs:294`).
8. **Multiplayer echoes**: every commit method has a `networkActionIfOnline` flag and `Synchronizer.SendGameAction(...)` side effects — physical VR buttons must call the public `OnClick()` (not internals) to keep online play consistent.
9. **LeanTween GUI animations** (`LeanTweenGUIAnimator` on gamepad long-press confirm, `ReadyButton.cs:205-226`) delay the actual commit until animation end; on VR synthetic input the `InputManager.GamePadInUse` branch decides whether `OnClick` commits immediately or arms the long-press flow — keep the game in mouse mode.
