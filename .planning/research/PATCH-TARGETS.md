# PATCH-TARGETS — Verified Against Real Assemblies

**Date:** 2026-07-15
**Purpose:** Every Harmony patch target / programmatic entry point named in `CARDS.md`, `BOARD-INPUT.md`, and `UI-ARCH.md`, re-verified against the shipping assemblies in `/home/claw/gloomhaven_vr/ressources/Managed/` (NOT the decompiled text dump).

**Method:**
- Each type decompiled straight from the real DLL with `ilspycmd 8.2.0 -t <Full.Type.Name> -r <Managed> <dll>` — signatures below are taken from that output, not from `/home/claw/gloomhaven_vr/decompiled/`.
- Type inventories cross-checked with `ilspycmd -l c/i/s/d/e` per assembly.
- IL-level truth: full `ilspycmd -il` dumps of GH.Runtime.dll and ScenarioRuleLibrary.dll; the **IL size** column below is the method's real IL code size in bytes (used for the inlining-risk assessment; Mono's default inline limit is ~20 bytes of IL).
- The "mangled names" scare (`public n` in ReadyButton.cs) is resolved: see Corrections #1. **No mangled/obfuscated names exist anywhere in the target set.** The decompiled text dump matches the real assemblies for every symbol checked.

Legend: ✅ = exists exactly as claimed in the research reports · ⚠ = exists but differs / needs a caveat · ❌ = not found (none).

---

## 1. GH.Runtime.dll

All types are in the **global namespace** unless a namespace is shown. Members are instance unless marked `static`.

### 1.1 Picking / input

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `MF.FindInteractableAtMousePosition` | static method | `public static CInteractable FindInteractableAtMousePosition(bool ignoreinteractableviaguiflag, LayerMask gameSelectionRaycastLayer)` | 85 B | ✅ (uses `Camera.main.ScreenPointToRay(InputManager.CursorPosition)` + `Physics.Raycast(..., 1000f, layer)` + `GetComponentInParent<CInteractable>()`; early-returns null if `Camera.main == null`) |
| `MF.FindNearestInteractableToPosition` | static method | `public static CInteractable FindNearestInteractableToPosition(bool ignoreinteractableviaguiflag, LayerMask gameSelectionRaycastLayer, Vector3 position)` | — | ✅ (note: resolves via `hitInfo.collider.transform.root.GetComponent<CInteractable>()`, NOT `GetComponentInParent` — subtle difference from its sibling) |
| `InputManager` | class | `public class InputManager : Singleton<InputManager>` | — | ✅ |
| `InputManager.CursorPosition` | static property (get-only) | `public static Vector2 CursorPosition { get; }` → `get_CursorPosition()` | 208 B | ✅ — getter is large (gamepad branch scans `Camera.allCameras` for tag `"UICamera"`); **not** inline-endangered despite being a getter (see §5) |
| `InputManager.CreateVirtualMouse` | **instance** method | `public void CreateVirtualMouse()` | 159 B | ✅ — instance, so call via `Singleton<InputManager>.Instance.CreateVirtualMouse()`. Adds `InputSystem.AddDevice<Mouse>("ConsoleVirtualMouse")`, pairs it, `MakeCurrent()`, warps to screen center |
| `InputManager.GamePadInUse` | static property | `public static bool GamePadInUse { get; }` (get-only) | — | ✅ |
| `HoverRegisterer.Update` | method | `private void Update()` | 529 B | ✅ — caveat: it **caches `Camera.main` in `Awake()`** into `m_Camera` and only re-fetches when the cached camera GO is inactive; a VR mod that swaps the main camera after Awake must account for the stale cache. Raycast gated on `!UIManager.IsPointerOverUI` |
| `Controller.CommonLoop` | method | `private bool CommonLoop(bool isPaused)` | 256 B | ✅ |
| `Controller.LateUpdate` | method | `private void LateUpdate()` | 376 B | ✅ |
| `InControlInputModuleExtended` | class | `public class InControlInputModuleExtended : InControlInputModule, IInputModulePointer` — **lives in GH.Runtime.dll, global namespace**; its base class `InControl.InControlInputModule` (and `InControl.PointerInputModuleExtended`) live in **InControl.dll** | — | ✅ — assembly question answered: patch the extended class in GH.Runtime; patching module internals means targeting InControl.dll |

### 1.2 Tile click / commit pipeline

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `TileBehaviour.s_Callback` | static field | `public static CallbackType s_Callback;` | — | ✅ |
| `TileBehaviour.CallbackType` | nested delegate | `public delegate void CallbackType(CClientTile clientTile, List<CTile> optionalTileList, bool networkActionIfOnline = false, bool isUserClick = false, bool actingPlayerHasSecondClickConfirmationEnabled = false);` | — | ✅ — full real definition; exactly as reported |
| `TileBehaviour.SetCallback` | static method | `public static void SetCallback(CallbackType callback)` | — | ✅ — registration sites confirmed: `Choreographer` (~15 sites incl. `Awake` :667, `null` at :716 to disable selection), `ReadyButton` (:272), `SkipButton` (:100) |
| `s_Callback` invocation sites | — | Exactly 3 in the whole game: `CInteractableTile.ExecuteTileCallback()` (IL 40 B), `CInteractableActor.ShowNormalInterface(bool)`, `AutoLogPlayback` (autotest replay). All pass `(clientTile, null, networkActionIfOnline: true, isUserClick: true, SaveData.Instance.Global.EnableSecondClickHexToConfirm)` (AutoLogPlayback passes false/false) | — | ✅ |
| `CInteractable.ShowNormalInterface` | virtual method | `public virtual void ShowNormalInterface(bool disabled)` — **empty body, IL = 1 byte** | 1 B | ⚠ — exists, but patching the *base* does nothing for tile/actor clicks: `CInteractableTile.ShowNormalInterface` (override, 105 B) and `CInteractableActor.ShowNormalInterface` (override, 86 B) do not call `base.`. **Patch the overrides**, not the base (see Corrections #2) |
| `CInteractableTile.ShowNormalInterface` | override method | `public override void ShowNormalInterface(bool disabled)` | 105 B | ✅ |
| `CInteractableActor.ShowNormalInterface` | override method | `public override void ShowNormalInterface(bool disabled)` | 86 B | ✅ |
| `Choreographer.TileHandler` | method | `public void TileHandler(CClientTile clientTile, List<CTile> optionalTileList = null, bool networkActionIfOnline = false, bool isUserClick = false, bool actingPlayerHasSecondClickConfirmationEnabled = false)` | 2798 B | ✅ (gated by `public bool m_TileSelectionDisabled` when `isUserClick`) |
| `Waypoint.TileHandler` | static method | `public static void TileHandler(CClientTile clientTile, List<CTile> optionalTileList, bool networkActionIfOnline = true, bool isUserClick = false, bool actingPlayerHasSecondClickConfirmationEnabled = false)` | 2722 B | ✅ |
| `Choreographer.ProcessMessage` | method | `private void ProcessMessage(CMessageData message)` | **128,029 B** | ✅ — the giant switch; private (Harmony fine, direct call no) |
| `Choreographer.SetChoreographerState` | method | `public void SetChoreographerState(ChoreographerStateType eState, int waitTickFrame, CActor waitActor)` | 324 B | ✅ |
| `Choreographer.Pass` | method | `public void Pass()` | 119 B | ✅ (wraps `ScenarioRuleClient.Pass()` in try/catch, clears waypoints first) |
| `Choreographer` button fields | fields | `public ReadyButton readyButton;` `public UndoButton m_UndoButton;` `public SkipButton m_SkipButton;` `public SelectButton m_selectButton;` `private FastForwardButton _fastForwardButton;` | — | ✅ |

### 1.3 Camera

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `CameraController.LateUpdate` | method | `private void LateUpdate()` | 2456 B | ✅ |
| `CameraController.RefreshFocusPosition` | method | `private void RefreshFocusPosition(float? y = null)` | 163 B | ✅ — per-frame position/LookAt writer, also writes `m_Camera.fieldOfView = m_TargetZoom` |
| `CameraController.SetCameraDirection` | method | `private void SetCameraDirection(float angle)` | 67 B | ✅ |
| `CameraController.SmoothRotate` | method | `private void SmoothRotate()` | 291 B | ✅ |
| Other writers | methods | `public void SmartFocus(GameObject target, bool pauseDuringTransition = false)`, `public void MoveToLook(Vector3, float, bool = false, Action = null)`, `public void ZoomToFOV(float zoom, float duration, Action onZoomed = null)`, `private IEnumerator ZoomTo(float, float, Action = null)`, `public void SetOverriddenBehavior(ICameraBehavior behavior)`, `private float CalculateYForZoomFactor(float)` | — | ✅ |
| `CameraController.m_IsCameraCodeControlDisabled` | property | `public bool m_IsCameraCodeControlDisabled { get; set; }` — checked at top of `LateUpdate` | — | ✅ — but note it is **set back to false/true by `MoveToLook` and scripted flows**, so it is not a durable "VR owns the camera" switch; prefix-skipping `LateUpdate` + `RefreshFocusPosition` is the durable approach |

### 1.4 Cards — hand management

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `CardsHandManager.Show` (per-player, single pile) | method | `public void Show(CPlayerActor playerActor, CardHandMode mode, CardPileType filterType = CardPileType.Any, CardPileType selectableCardType = CardPileType.Any, int maxCardsSelected = 0, bool fadeUnselectableCards = false, bool highlightSelectableCards = false, bool allowFullCardPreview = true, CardsHandUI.CardActionsCommand resetCardActions = CardsHandUI.CardActionsCommand.RESET, bool forceUseCurrentRoundCards = false, bool allowFullDeckPreview = true, Action<AbilityCardUI> overrideCardCallback = null, Func<CAbilityCard, bool> cardFilter = null)` | — | ⚠ — CARDS.md attributed the *list* signature to line 734; in reality :734 is this **singular `CardPileType selectableCardType`** overload (it wraps the list overload). Harmony `TargetMethod` must disambiguate the 3 overloads by parameter types |
| `CardsHandManager.Show` (per-player, pile list) | method | `public void Show(CPlayerActor playerActor, CardHandMode mode, CardPileType filterType, List<CardPileType> selectableCardTypes, int maxCardsSelected = 0, bool fadeUnselectableCards = false, bool highlightSelectableCards = false, bool allowFullCardPreview = true, CardsHandUI.CardActionsCommand resetCardActionsPhase = CardsHandUI.CardActionsCommand.RESET, bool forceUseCurrentRoundCards = false, bool allowFullDeckPreview = true, Action<AbilityCardUI> overrideCardCallback = null, Func<CAbilityCard, bool> cardFilter = null)` | — | ✅ — this is the one all card flows funnel through; **the best single Show patch target** |
| `CardsHandManager.Show` (active hand) | method | `public void Show(CardHandMode mode, CardPileType filterType, List<CardPileType> selectableCardTypes, int maxCardsSelected = 0, bool fadeUnselectableCards = false, bool allowFullCardPreview = true, bool allowFullDeckPreview = true)` | — | ✅ |
| `CardsHandManager.ShowCoroutine` | method | `public IEnumerator ShowCoroutine(CardHandMode mode, CardPileType filterType, List<CardPileType> selectableCardTypes, int maxCardsSelected = 0, bool fadeUnselectableCards = false, bool allowFullCardPreview = true, bool allowFullDeckPreview = true, Action onShow = null)` | 75 B (stub) | ✅ — it's an iterator: a Harmony postfix fires at *iterator creation*, not when the hand is actually shown. Patch `MoveNext` of `<ShowCoroutine>d__106` or postfix the non-coroutine `Show` instead |
| `CardsHandManager.Hide` | method | `public void Hide(CPlayerActor playerActor = null)` | — | ✅ |
| `CardsHandManager.GetHand` / `GetActiveHand` / `SwitchHand` / `AddPlayer` / `ShowLongRestConfirmation` / `DiscardActiveAbilityCard` | methods | `public CardsHandUI GetHand(int actorID)` / `public CardsHandUI GetHand(CPlayerActor cPlayer)` / `public CardsHandUI GetActiveHand()` / `public void SwitchHand(CPlayerActor cPlayer)` / `public void AddPlayer(CPlayerActor playerData)` / `public void ShowLongRestConfirmation(CPlayerActor playerActor, Action<bool> onToggledConfirmLongRest)` / `public void DiscardActiveAbilityCard(CPlayerActor playerActor, CAbilityCard card, UnityAction onSuccess = null, UnityAction onCancel = null)` | — | ✅ |

### 1.5 Cards — selection / actions (CardsHandUI, FullAbilityCard, AbilityCardUI)

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `CardsHandUI.OnCardSelected` | method | `private void OnCardSelected(AbilityCardUI cardUI, bool networkAction = true)` | 2523 B | ✅ (private, as reported — Harmony OK, direct call needs AccessTools) |
| `CardsHandUI.OnCardDeselected` | method | `private void OnCardDeselected(AbilityCardUI cardUI, bool networkAction = true)` | 1177 B | ✅ (private) |
| `CardsHandUI.SelectCard` | method | `public void SelectCard(CAbilityCard card)` | 74 B | ✅ |
| `CardsHandUI.UnselectCard` | method | `public void UnselectCard(CAbilityCard card)` | 74 B | ✅ |
| `CardsHandUI.ProxySelectCardAction` | method | `public void ProxySelectCardAction(int cardInstanceID, CBaseCard.ActionType cardActionType)` | 112 B | ✅ |
| `CardsHandUI.ProxySelectCard` | method | `public void ProxySelectCard(int cardInstanceID, bool isHandUnderMyControl = true)` | 108 B | ✅ |
| `CardsHandUI.ProxyShortRest` | method | `public void ProxyShortRest(StartRoundCardsToken startRoundCardToken, bool fromStateUpdate = false)` | — | ✅ — beware: `CardsHandManager` has a *different private* `ProxyShortRest(int controllableID, StartRoundCardsToken startRoundCardToken)` (133 B); don't confuse them |
| `CardsHandUI.PerformShortRest` | method | `public void PerformShortRest(CPlayerActor playerActor)` | 1259 B | ✅ |
| `CardsHandUI.DeselectAllCards` | method | `public void DeselectAllCards(bool networkAction = true, bool isHandUnderMyControl = true)` | — | ✅ |
| `CardsHandUI.ProxyLongRest` | method | `public void ProxyLongRest(int burnedCardID)` | — | ✅ |
| `FullAbilityCard.OnAbilityClick` (commit) | method | `public void OnAbilityClick(CBaseCard.ActionType abilityType, bool isProxyAction, bool checkValid = true)` | 882 B | ✅ — the real half-commit; this is the overload to patch/call |
| `FullAbilityCard.OnAbilityClick` (UnityEvent shim) | method | `public void OnAbilityClick(bool isTopAbility)` — body is a single forward to the 3-arg overload | **16 B** | ⚠ — exists, but **inline-endangered** (≤20 B); never patch this shim, patch the 3-arg overload (see §5) |
| `AbilityCardUI.CardSelectionStateChanged` | static event | `public static event Action<AbilityCardUI, bool> CardSelectionStateChanged;` | — | ✅ |
| `AbilityCardUI.CardHoveringStateChanged` | static event | `public static event Action<AbilityCardUI, bool> CardHoveringStateChanged;` | — | ✅ (plus instance `public event Action CardSelected;` / `CardDeselected;`) |
| `AbilityCardUI.OnClick()` | method | `public void OnClick()` — forwards to `OnClick(ignoreHiglight: false)` | **8 B** | ⚠ inline-endangered shim; patch `OnClick(bool)` instead |
| `AbilityCardUI.OnClick(bool)` | method | `public void OnClick(bool ignoreHiglight)` (param name typo `ignoreHiglight` is real) | 162 B | ✅ |
| `AbilityCardUI.ToggleSelect` | method | `public void ToggleSelect(bool active, bool highlight = false, bool networkAction = true, bool isHandUnderMyControl = true)` | — | ✅ |
| `AbilityCardUI.SwapInitiative` | method | `public void SwapInitiative()` | 508 B | ✅ **new finding** — the "initiative badge click handler" CARDS.md referenced only by line range is this named public method (swaps Initiative/SubInitiative cards, reverses `RoundAbilityCards`, updates track, networks) |
| `FullAbilityCard.FullCardHoveringStateChanged` / `OnEnterForView` | static events | `public static event Action<bool> FullCardHoveringStateChanged;` / `public static event Action OnEnterForView;` | — | ✅ |

### 1.6 HUD buttons & confirm flow

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `ReadyButton` | class | `public class ReadyButton : ButtonOnBlockingPanel` | — | ✅ |
| `ReadyButton.EButtonState` | **nested enum — REAL, not mangled** | see full definition below | — | ✅ resolved (Corrections #1) |
| `ReadyButton.OnClick` | method | `public void OnClick(bool networkActionIfOnline = true)` | 117 B | ✅ |
| `ReadyButton.OnClickInternal` | method | `public void OnClickInternal(bool networkActionIfOnline = true)` (returns **void**) | 1099 B | ✅ — the `eButtonState >= EButtonState.EREADYBUTTONCONTINUE → StepComplete` branch confirmed in the real body |
| `ReadyButton.Toggle` | method | `public void Toggle(bool active, EButtonState state = EButtonState.EREADYBUTTONNA, string text = null, bool hideOnClick = true, bool glowingEffect = false, bool interactable = true, bool disregardTurnControlForInteractability = false, bool haltActionProcessorIfDeactivated = true, bool hideSelectionOnEndTurn = true)` | — | ✅ (9 params) |
| `ReadyButton.SetInteractable` / `QueueAlternativeAction` / `AlternativeAction` | methods | `public void SetInteractable(bool interactable, bool disregardTurnControl = false)` / `public void QueueAlternativeAction(Action extraAction)` / `public void AlternativeAction(Action extraAction, EButtonState state = EButtonState.EREADYBUTTONNA, string text = null, bool active = true)` | — | ✅ |
| `UndoButton.OnClickInternal` | method | `public bool OnClickInternal(bool networkActionIfOnline = true, GameAction action = null)` (returns **bool**) | 670 B | ✅ |
| `UndoButton.EButtonState` | nested enum | `public enum EButtonState { EUNDOBUTTONUNDO, EUNDOBUTTONCLEARTARGETS, EUNDOBUTTONUNDOWAYPOINT }` | — | ⚠ — reports listed only the first two; the real enum has a **third member `EUNDOBUTTONUNDOWAYPOINT`** |
| `SkipButton.OnClick` | method | `public void OnClick(bool networkActionIfOnline = true)`; also `public void OnClickFromButton()` and `public void Toggle(bool active, string text = null, bool hideOnClick = true, bool? interactable = null, Action onSkipAction = null)` | — | ✅ |
| `BaseButtons.clickButton` | method | `private void clickButton(GameObject Objbutton)` (lowercase name is real; fires `ExecuteEvents.pointerClickHandler`) | 36 B | ✅ |

### 1.7 Board overlay / worldspace UI / windows / tooltips

| Symbol | Kind | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|---|
| `WorldspaceStarHexDisplay.RotateAOEClockwise` | method | `public void RotateAOEClockwise(bool turnRight)` | 319 B | ✅ (`RotateAOEWithMouse()` / `RotateAOEWithKeyboard()` both `private bool`; `public int AreaEffectAngle => m_AreaEffectAngle;`; `public void ProxyUpdateSelectionHexes(CClientTile originTile, int targetingAngle, bool aoeLocked)`; `public void SetAOELocked(bool locked)`; `public bool IsAOELocked()`) |
| `InitiativeTrackPlayerAvatar.SwapInitiative` | method | `public void SwapInitiative()` (class: `public class InitiativeTrackPlayerAvatar : InitiativeTrackActorAvatar`) | 228 B | ✅ |
| `WorldspaceDisplayPanelBase.TrackCharacter` | method | `public void TrackCharacter()` (called from `private void LateUpdate()`) | 318 B | ✅ |
| `UnityEngine.UI.UIWindowManager` | class (ns `UnityEngine.UI`, in GH.Runtime.dll) | open/close API: `public void HideOrShowWindows(bool onlyHide = false, bool forceHideAll = false, bool popUpsOnly = false)` · `public void ForceHideWindows(bool onlyHide = false)` · `public void Escape()` · `public void Add/RemoveSkipHideWindows(params UIWindowID[])` · `public void Add/RemoveSkipShowWindows(params UIWindowID[])` · `public static void RegisterEscapable(IEscapable element)` / `UnregisterEscapable` | — | ✅ — note per-window open/close lives on `UnityEngine.UI.UIWindow`: `public virtual void Show()` / `Show(bool instant)` / `Hide()` / `Hide(bool instant)`, `public bool IsOpen`, `public static UIWindow GetWindow(UIWindowID id)`, `public static UIWindow FocusedWindow { get; set; }` |
| `UIManager.ToggleLockUI` | method | `public void ToggleLockUI(bool active)` + ref-counted `public void RequestToggleLockUI(bool active, GameObject element)` | 59 B / 52 B | ✅ |
| `UIManager.IsPointerOverUI` | static property | `public static bool IsPointerOverUI { get; }` | 56 B | ✅ |
| `UnityEngine.UI.UITooltip` static API | static methods | `public static void AddTitle(string title, TMP_SpriteAsset spriteAsset = null)` · `AddDescription(string, TMP_SpriteAsset = null)` · `AddLine(...)` (6 overloads) · `SetLines(UITooltipLines lines)` · `public static void Show(float delay = -1f, RectTransform tooltipTransform = null)` · `public static void Hide(float delay = -1f)` · `AnchorToRect(RectTransform targetRect, Corner corner)` · `SetScreenBound(bool enable, float offset)` · `ResetContent()` · `SetWidth(float)` / `SetSize(float, float)` / `ShowBackgroundImage(bool)` / `SetVerticalControls(bool, bool = false)` | — | ✅ — minor: `Hide` takes an optional `float delay = -1f` (reports wrote `Hide` bare) |
| `CanvasManager` rewire | method | `private void OnSceneLoaded(Scene scene, LoadSceneMode mode)` — subscribed in `OnEnable` via `SceneManager.sceneLoaded`; sets `persistentUICanvas.worldCamera` and `tooltipCanvas.worldCamera` to the first camera tagged `"UICamera"` (both fields `[SerializeField] private Canvas`) | 70 B | ✅ |
| `Code.State.StateMachine.EventStateChanged` | event | `public event Action<IState> EventStateChanged;` (+ `public bool IsCurrentState<TState>()`, `public IState CurrentState`, `Enter<TTag>(TTag tag)`) | — | ✅ |
| `Script.GUI.SMNavigation.UINavigation.StateMachine` | property | `public NavigationStateMachine StateMachine { get; private set; }` (class `public class UINavigation : Singleton<UINavigation>`; also `public static event Action<UINavigation> OnInitialize`) | — | ✅ — `NavigationStateMachine : Code.State.StateMachine`, so `EventStateChanged` is available on it |

---

## 2. Utilities.dll (separate assembly!)

| Symbol | Kind | Exact signature (real assembly) | Status |
|---|---|---|---|
| `Utilities.VirtualMouseUtilities` | class | `public class VirtualMouseUtilities` (plain class, namespace `Utilities`, **assembly `Utilities.dll`** — not GH.Runtime). Members: ctor self-registers via `InputSystemUtilities.InitVirtualMouse(this)`; `public bool TryStart()`; `public void Stop()`; `public void Press()` / `Release()`; `public void SetPosition(Vector2 position)` / `public Vector2 GetPosition()`; `SetScrollWheel/GetScrollWheel(Vector2)`; `public bool IsWasPressed()` / `IsPress()` / `IsWasReleased()`; `public void Update()` (hooked to `InputSystem.onAfterUpdate` while started); events `OnTryStart` / `OnStop` | ✅ (assembly clarified) |
| `Utilities.InputSystemUtilities` | static class | `public static class InputSystemUtilities` — `public static Vector3 GetMousePosition()` (**returns Vector3**, not Vector2), `GetMouseButtionDown/Up/GetMouseButtion(MouseButton)` (typo "Buttion" is real), `InitVirtualMouse(VirtualMouseUtilities mouse)`, `GetCurrentVirtualMouse()`, `GetKey(Key)` etc. | ✅ (return type + typo noted) |

---

## 3. ScenarioRuleLibrary.dll (namespace `ScenarioRuleLibrary`)

### 3.1 ScenarioRuleClient (all `public static`, thread-safe enqueue)

| Symbol | Exact signature (real assembly) | IL size | Status |
|---|---|---|---|
| `TileSelected` | `public static uint TileSelected(CTile tile, List<CTile> optionalTileList, bool processImmediately = false)` | 132 B | ✅ |
| `TileDeselected` | `public static uint TileDeselected(CTile tile, List<CTile> optionalTileList, bool processImmediately = false)` | **14 B** | ✅ / inline risk if patched |
| `ApplySingleTarget` | `public static uint ApplySingleTarget(CActor actor, bool processImmediately = false)` | **13 B** | ✅ / inline risk if patched |
| `StepComplete` | `public static uint StepComplete(bool processImmediately = false, bool fromSRL = false)` — note `fromSRL` is **unused in the body** | **13 B** | ✅ / inline risk if patched |
| `Pass` | `public static uint Pass(bool processImmediately = false)` | **13 B** | ✅ / inline risk if patched |
| `PassStep` | `public static uint PassStep(bool processImmediately = false)` | **13 B** | ✅ / inline risk if patched |
| `Undo` | `public static uint Undo(List<CActiveBonus> activeBonusesToUntoggle)` (no `processImmediately` param) | **13 B** | ✅ / inline risk if patched |
| `ClearTargets` | `public static uint ClearTargets(bool processImmediately = false)` | **14 B** | ✅ / inline risk if patched |
| `MoveAbilityCard` | `public static uint MoveAbilityCard(CCharacterClass characterClass, CAbilityCard abilityCard, List<CAbilityCard> fromAbilityCardList, List<CAbilityCard> toAbilityCardList, string fromAbilityCardListName, string toAbilityCardListName, bool networkAction)` | 22 B | ✅ / borderline inline risk |
| `ShortRestPlayer` | `public static uint ShortRestPlayer(CPlayerActor playerActor, CAbilityCard discardedCard, bool loseHealth, bool updateScenarioRNG, bool fromStateUpdate = false, bool processImmediately = false)` | **19 B** | ✅ / inline risk if patched |
| `AddSRLQueueMessage` | `public static uint AddSRLQueueMessage(CSRLMessage srlMessage, bool processImmediately)` | >20 B | ✅ — **the safe single observation point** for everything above (they are all one-line wrappers around it) |
| `SetMessageHandler` / `MessageHandlerCallback` | `public static void SetMessageHandler(MessageHandlerCallback messageHandler)`; `public delegate void MessageHandlerCallback(CMessageData message, bool processImmediately = false);` | — | ✅ |
| `GetAndReplicateStartRoundDeckState` | `public static uint GetAndReplicateStartRoundDeckState(CPlayerActor playerActor, int gameActionID, bool processImmediately = false)` | — | ✅ |

### 3.2 GameState / piles / cards / phases

| Symbol | Kind | Exact signature (real assembly) | Status |
|---|---|---|---|
| `GameState.PlayerSelectedAbilityCardAction` | static method | `public static void PlayerSelectedAbilityCardAction(CAbilityCard roundAbilityCard, CBaseCard.ActionType actionType)` (+ statics `public static CAbilityCard RoundAbilityCardselected { get; private set; }`, `public static CBaseCard.ActionType RoundAbilityCardActionType { get; private set; }`) | ✅ |
| `CCharacterClass` piles | properties | All **get-only** expression properties over private fields: `public List<CAbilityCard> HandAbilityCards`, `RoundAbilityCards`, `DiscardedAbilityCards`, `LostAbilityCards`, `PermanentlyLostAbilityCards`, `SelectedAbilityCards`, `UnselectedAbilityCards` (computed LINQ), `public List<CBaseCard> ActivatedCards`, `public List<CAbilityCard> ActivatedAbilityCards` (computed) | ✅ — mutate only via `MoveAbilityCard`, never by list surgery |
| `CCharacterClass` initiative | members | `public CAbilityCard InitiativeAbilityCard` / `SubInitiativeAbilityCard` / `ExtraTurnInitiativeAbilityCard` (get-only) + `public void SetInitiativeAbilityCard(CAbilityCard abilityCard)` / `SetSubInitiativeAbilityCard(CAbilityCard abilityCard)` | ✅ |
| `CCharacterClass` rest flags | properties | `public bool LongRest { get; set; }` (with side effects in setter), `public bool HasLongRested`, `public bool HasShortRested`, `public bool HasImprovedShortRested`, `public bool ImprovedShortRest`, `public bool ShortRestCardRedrawn { get; set; }`, **`public CAbilityCard ShortRestCardBurned { get; set; }`** | ⚠ — `ShortRestCardBurned` is a `CAbilityCard` reference, **not a bool** (CARDS.md listed it among bool flags) |
| `CCharacterClass.MoveAbilityCard` | method | `public void MoveAbilityCard(CAbilityCard abilityCard, List<CAbilityCard> fromAbilityCardList, List<CAbilityCard> toAbilityCardList, string fromCardPileName, string toCardPileName, bool sendNetworkSelectedCardsWhenDone = false)` | ✅ |
| `CAbilityCard` core | members | `public CAction TopAction` / `BottomAction` / `DefaultAttackAction` / `DefaultMoveAction` / `SelectedAction` / `LastSelectedAction` (all get-only); `public int Level { get; private set; }`; `public bool SupplyCard { get; private set; }`; `public int CardInstanceID { get; private set; }`; `public AbilityCardYMLData GetAbilityCardYML`; `public void SetSelectedAction(CAction action)`; `public CAction GetActionForType(ActionType type)`; `public List<CAbility> GetTopActionAbilities()` / `GetBottomActionAbilities()`; `public CAbilityCard Copy(int? cardInstanceID = null)` | ⚠ minor — `Copy` takes an optional `int? cardInstanceID` (reports wrote `Copy()`) |
| `CBaseCard` enums | nested enums | `public enum ActionType { TopAction, DefaultAttackAction, DefaultMoveAction, BottomAction, NA }`; `ECardType`, `ECardPile` as reported; `public ECardPile CurrentCardPile { get; set; }`; `public int ID { get; private set; }` | ✅ |
| `PhaseManager` | class | `public class PhaseManager` — `public static CPhase.PhaseType PhaseType { get; }` (getter IL 21 B), `public static CPhase CurrentPhase` / `Phase` (6 B getters), `public static void TileSelected(CTile, List<CTile>)`, `TileDeselected`, `ApplySingleTarget(CActor)`, `StepComplete(bool passingStep = false)`, `SetNextPhase(CPhase.PhaseType type)` | ✅ — getters are trivial: **poll them, don't patch them** (see §5) |
| `CPhase.PhaseType` | nested enum | `[Serializable] public enum PhaseType { PlayerExhausted, SelectAbilityCardsOrLongRest, MonsterClassesSelectAbilityCards, StartTurn, ActionSelection, Action, EndTurn, EndRound, Count, None, Autosave, StartRoundEffects, CheckForInitiativeAdjustments, CheckForForgoActionActiveBonuses, StartScenarioEffects, EndTurnLoot }` | ✅ — full real definition (member order matters for numeric comparisons) |

---

## 4. Full real definitions (delegates & enums implementation workers need verbatim)

### `TileBehaviour.CallbackType` (GH.Runtime, nested in `TileBehaviour`)
```csharp
public delegate void CallbackType(CClientTile clientTile, List<CTile> optionalTileList,
    bool networkActionIfOnline = false, bool isUserClick = false,
    bool actingPlayerHasSecondClickConfirmationEnabled = false);
```

### `ReadyButton.EButtonState` (GH.Runtime, nested in `ReadyButton`) — THE "mangled" enum, real & clean
```csharp
public enum EButtonState
{
    EREADYBUTTONENDSELECTION,      // 0
    EREADYBUTTONENDROUND,          // 1
    EREADYBUTTONENDTURN,           // 2
    EREADYBUTTONPASS,              // 3
    EREADYBUTTONCONTINUE,          // 4  <- OnClickInternal commits StepComplete when state >= this
    EREADYBUTTONLONGREST,          // 5
    EREADYBUTTONCONFIRMTARGETS,    // 6
    EREADYBUTTONFINISHTARGETSELECT,// 7
    EREADYBUTTONCONFIRM,           // 8
    EREADYBUTTONCONFIRMMOVEMENT,   // 9
    EREADYBUTTONOPENDOOR,          // 10
    EREADYBUTTONCONFIRMDISABLED,   // 11
    EREADYBUTTONNA,                // 12
    EREADYBUTTONRECOVERCARD,       // 13
    EREADYBUTTONIMPROVEDSHORTREST, // 14
    EREADYBUTTONCONFIRMITEM        // 15
}
```
(16 members, not 17 as UI-ARCH.md said. Private state field: `private EButtonState buttonState = EButtonState.EREADYBUTTONNA;`)

### `UndoButton.EButtonState` (GH.Runtime, nested in `UndoButton`)
```csharp
public enum EButtonState { EUNDOBUTTONUNDO, EUNDOBUTTONCLEARTARGETS, EUNDOBUTTONUNDOWAYPOINT }
```

### `ScenarioRuleClient.MessageHandlerCallback` (ScenarioRuleLibrary)
```csharp
public delegate void MessageHandlerCallback(CMessageData message, bool processImmediately = false);
```

---

## 5. Inlining-risk assessment (Mono JIT, inline limit ≈ 20 bytes IL)

The game ships Mono (managed DLLs, no IL2CPP). Harmony patches rewrite the target's native code; a call site that the JIT already **inlined** bypasses the patch. Methods ≤ ~20 bytes IL are inline candidates; methods with try/catch, virtual dispatch, or >20 B are effectively safe. Patching from a BepInEx plugin at chainload time (before the Game scene JITs) further reduces risk, but do not rely on it for the tiny wrappers below.

**At risk — do NOT use as Harmony observation targets:**

| Method | IL | Safer alternative |
|---|---|---|
| `ScenarioRuleClient.Pass` (13 B), `PassStep` (13 B), `StepComplete` (13 B), `TileDeselected` (14 B), `ApplySingleTarget` (13 B), `Undo` (13 B), `ClearTargets` (14 B), `ShortRestPlayer` (19 B), `MoveAbilityCard` (22 B, borderline) | ≤22 B | **Patch `ScenarioRuleClient.AddSRLQueueMessage(CSRLMessage, bool)` once** and switch on the concrete `CSRLMessage` subtype / `EMessageType` — every wrapper funnels through it. Or observe results via `Choreographer.ProcessMessage` postfix. **Calling** these wrappers from VR code is always safe — inlining only breaks patches, not calls |
| `FullAbilityCard.OnAbilityClick(bool isTopAbility)` | 16 B | Patch/call `OnAbilityClick(CBaseCard.ActionType, bool, bool)` (882 B) |
| `AbilityCardUI.OnClick()` | 8 B | Patch `OnClick(bool ignoreHiglight)` (162 B). (In practice the shim is invoked via UnityEvent delegate, which is not inlined — but the 1-arg overload is the correct target anyway) |
| `CInteractable.ShowNormalInterface` | 1 B (empty) | Not an inlining problem (virtual dispatch is not inlined) but a **dispatch problem**: overrides never execute the base. Patch `CInteractableTile.ShowNormalInterface` (105 B) and `CInteractableActor.ShowNormalInterface` (86 B) individually |
| `PhaseManager.get_PhaseType` (21 B), `get_CurrentPhase` / `get_Phase` (6 B) | ≤21 B | Don't patch; **poll** `PhaseManager.PhaseType`, or observe phase transitions via `Choreographer.ProcessMessage` message types |

**Explicitly checked and NOT at risk (large bodies):**
- `InputManager.get_CursorPosition` — **208 B** static getter. The report's worry ("getters especially") does not apply: it contains the whole gamepad camera-search branch. Safe to prefix-patch. (Its mouse-mode dependency `Utilities.InputSystemUtilities.GetMousePosition()` is also non-trivial because of the virtual-mouse branch.)
- `MF.FindInteractableAtMousePosition` 85 B · `Choreographer.TileHandler` 2798 B · `Waypoint.TileHandler` 2722 B · `Choreographer.ProcessMessage` 128 kB · `SetChoreographerState` 324 B · `CameraController.LateUpdate` 2456 B / `RefreshFocusPosition` 163 B · `CardsHandUI.OnCardSelected` 2523 B / `OnCardDeselected` 1177 B / `SelectCard`/`UnselectCard` 74 B / `ProxySelectCardAction` 112 B / `PerformShortRest` 1259 B · `ReadyButton.OnClickInternal` 1099 B / `OnClick` 117 B · `UndoButton.OnClickInternal` 670 B · `RotateAOEClockwise` 319 B · `SwapInitiative` 228 B · `TrackCharacter` 318 B · `ToggleLockUI` 59 B · `clickButton` 36 B · `CanvasManager.OnSceneLoaded` 70 B · `HoverRegisterer.Update` 529 B · `Controller.CommonLoop` 256 B / `LateUpdate` 376 B. (Unity magic methods `Update`/`LateUpdate` are additionally safe because Unity invokes them directly, never inline.)

---

## 6. Corrections to the research reports

Nothing in the three reports points at a nonexistent symbol — **0 ❌**. The following ⚠ items are what implementation workers must adjust:

1. **"Mangled names" risk is retired.** `ReadyButton.EButtonState` is a real, clean nested enum (16 members, §4). The `public n` sighting in `decompiled/GH.Runtime/ReadyButton.cs` is actually `public new bool IsInteractable => readyButton.interactable;` — the C# `new` modifier, not obfuscation. `FFSNet.GameActionType` and `StartRoundCardState` in the decompiled dump are likewise clean and match the assemblies. The decompiled text dump is trustworthy for names; only line numbers may drift.
2. **`CInteractable.ShowNormalInterface` must not be the patch target** (empty virtual, 1 B; overrides don't call base). Patch `CInteractableTile.ShowNormalInterface` and `CInteractableActor.ShowNormalInterface` (both `public override void ShowNormalInterface(bool disabled)`), or skip this layer entirely and invoke `TileBehaviour.s_Callback` directly.
3. **Don't patch the tiny `ScenarioRuleClient` wrappers as observers** (`Pass`, `StepComplete`, `TileDeselected`, `ApplySingleTarget`, `Undo`, `ClearTargets`, `ShortRestPlayer` are all ≤19 B IL and inline-endangered). Observe `ScenarioRuleClient.AddSRLQueueMessage` (single choke point) or the `Choreographer.ProcessMessage` pump instead. Calling them is fine.
4. **`FullAbilityCard.OnAbilityClick(bool)` and `AbilityCardUI.OnClick()` are 16 B / 8 B forwarding shims** — patch the real overloads `OnAbilityClick(CBaseCard.ActionType, bool, bool)` and `OnClick(bool ignoreHiglight)`.
5. **`Utilities.VirtualMouseUtilities` and `Utilities.InputSystemUtilities` live in `Utilities.dll`**, a separate assembly from GH.Runtime — Harmony patches there need `AccessTools.TypeByName("Utilities.InputSystemUtilities")` against that assembly. `InputSystemUtilities.GetMousePosition()` returns **`Vector3`** (not Vector2), and the button helpers are really spelled `GetMouseButtionDown/Up` (typo in the game).
6. **`InputManager.CreateVirtualMouse()` is an instance method** — call `Singleton<InputManager>.Instance.CreateVirtualMouse()`.
7. **`CardsHandManager.Show` has 3 overloads and the line-734 one takes a single `CardPileType selectableCardType`, not a `List<CardPileType>`** (the List overload is :739; :813 is the active-hand variant). `TargetMethod` must specify parameter types explicitly. Also `ShowCoroutine` is an iterator — a postfix fires at iterator creation, not on completion; prefer patching the `Show(playerActor, mode, filterType, List<...>)` overload.
8. **`ReadyButton.EButtonState` has 16 members** (UI-ARCH.md said "17 states"); `UndoButton.EButtonState` has a **third member `EUNDOBUTTONUNDOWAYPOINT`** the reports omitted.
9. **The initiative-badge click handler has a name:** `AbilityCardUI.SwapInitiative()` (`public void`, 508 B) — CARDS.md referenced it only as "initiative button handler (AbilityCardUI.cs:837-849)". Use it (or `InitiativeTrackPlayerAvatar.SwapInitiative()`) directly from VR.
10. **`CCharacterClass.ShortRestCardBurned` is `public CAbilityCard { get; set; }`**, not a bool flag; the bool flags are `HasShortRested`, `HasImprovedShortRested`, `ShortRestCardRedrawn`. Pile properties are all get-only — never mutate the lists directly.
11. **`CAbilityCard.Copy` is `Copy(int? cardInstanceID = null)`** (optional param); `MF.FindNearestInteractableToPosition` resolves the interactable via `collider.transform.root.GetComponent`, unlike its sibling's `GetComponentInParent` — a VR replacement prefix must mirror each behaviour respectively.
12. **`HoverRegisterer` caches `Camera.main` in `Awake()`** and only refreshes when the cached camera GameObject is inactive — if the VR rig replaces/re-tags the main camera at runtime, this component keeps raycasting from the old camera unless the `InputManager.CursorPosition`/camera swap strategy accounts for it (or its `Update` is prefix-replaced).
13. Minor signature completions: `UITooltip.Hide(float delay = -1f)`; `ScenarioRuleClient.Undo(List<CActiveBonus>)` has **no** `processImmediately` param; `StepComplete`'s `fromSRL` parameter is dead (unused in body); `UndoButton.OnClickInternal` returns `bool` (as UI-ARCH said) while `ReadyButton.OnClickInternal` returns `void`.
14. Per-window open/close is on `UnityEngine.UI.UIWindow` (`Show()/Show(bool)/Hide()/Hide(bool)` virtual, `IsOpen`, static `GetWindow(UIWindowID)`), while `UIWindowManager` only does bulk ops + the escapable list (`RegisterEscapable` is **static**, `Escape()` is instance) — reports implied this split correctly but workers should target `UIWindow.Show/Hide` for individual panels.

---

## 7. Verdict summary

- **✅ 60 symbols verified exactly as claimed** (all classes, methods, fields, events, delegates and enums on the checklist exist in the real assemblies with the reported signatures; line numbers in the reports match the fresh decompile of the shipping DLLs).
- **⚠ 8 caveats** requiring adjusted usage: base-virtual `ShowNormalInterface` (#2), tiny-wrapper inlining set (#3), the two forwarding shims (#4), Utilities.dll residency + `Vector3` return (#5), `Show` overload attribution (#7), enum member-count deltas (#8), `ShortRestCardBurned` type (#10), `Copy` optional param (#11).
- **❌ 0 not found.**
