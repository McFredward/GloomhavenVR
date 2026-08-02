# Harmony patch inventory

> **GENERATED FILE — do not edit by hand.**
> Regenerate with `scripts/patch-inventory.sh generate`;
> `scripts/patch-inventory.sh check` (run by `scripts/refactor-guard.sh check`)
> fails if this file drifts from the source.
>
> The previous, hand-maintained version of this document listed 17 patched
> methods while the repository declared 60 `[HarmonyPatch]` attributes. Since
> `CHARTER.md` §5 cites this file as the authority for "is this a patch target?"
> — i.e. it is consulted before deleting anything — a stale version is actively
> dangerous, not merely untidy. Hence: generated.
>
> All patches go through the single shared `Harmony("dev.gloomhavenvr")` instance
> and are removed collectively by `Plugin.OnDestroy → UnpatchSelf()`.
>
> **Prose a parser cannot derive** — the per-patch effect/gate notes and the
> cross-module contention rules — lives in `docs/PATCH-NOTES.md`, which was split
> out of this file when it became generated. Per-patch *contracts* live in
> `.planning/refactor/INVARIANTS-Hands-Board-Core.md` §13. The three documents
> answer different questions and all three should survive.

## How to read the Registered-by column

A patch class only takes effect if some module calls `PatchAll(typeof(X))` on it.
A class with **NONE** is compiled, shipped, and completely inert — that has
happened twice (`8567af4`, `57309c5`). `check` fails on it.

Classes marked **degrades by design** resolve their own target through a
`TargetMethod` that may return `null`; those are *allowed* to patch nothing at
runtime, which is why a runtime audit could never do this job (see
`.planning/refactor/REVIEW-Hands-Board-Core.md` §P1).


**45 patch classes, 69 patched methods.**

## Board

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `Controller_CommonLoop_Patch`<br/><sub>src/GloomhavenVR/Board/BoardClickDriver.cs:261</sub> | `Controller.CommonLoop()` *(private)* | postfix | `BoardModule`:99 |
| `ActorBehaviour_HeldTransform_Patch`<br/><sub>src/GloomhavenVR/Board/FigureGrab/ActorBehaviour_HeldTransform_Patch.cs:39</sub> | `ActorBehaviour.Update()` *(private)* | prefix | `BoardModule`:102 |
| &nbsp; | `ActorBehaviour.LateUpdate()` *(private)* | prefix | &nbsp; |
| &nbsp; | `ActorBehaviour.SetHilighted()` | prefix | &nbsp; |
| `HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch`<br/><sub>src/GloomhavenVR/Board/HexHighlightFix.cs:258</sub> | `HexSelect_Control.ProjectorMaterialAdjustment()` *(private)* | postfix | `BoardModule`:98 |
| `HexHoverClear`<br/><sub>src/GloomhavenVR/Board/Patches/HexHoverClear.cs:91</sub> | `WorldspaceStarHexDisplay.Update()` | postfix | `BoardModule`:92 |
| `MF_FindInteractableAtMousePosition_Patch`<br/><sub>src/GloomhavenVR/Board/Patches/PickingPatches.cs:42</sub> | `MF.FindInteractableAtMousePosition()` | prefix | `BoardModule`:86 |
| `InputManager_CursorPosition_Patch`<br/><sub>src/GloomhavenVR/Board/Patches/PickingPatches.cs:75</sub> | `InputManager.get_CursorPosition()` | prefix | `BoardModule`:87 |
| `UIManager_IsPointerOverUI_Patch`<br/><sub>src/GloomhavenVR/Board/Patches/PickingPatches.cs:129</sub> | `UIManager.get_IsPointerOverUI()` | prefix | `BoardModule`:88 |
| `PingNameTag_Patch` *(degrades by design)*<br/><sub>src/GloomhavenVR/Board/Patches/PingNameTag.cs:43</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `BoardModule`:118 |
| `Placement_Hover_Diagnostics`<br/><sub>src/GloomhavenVR/Board/Patches/PlacementDiagnostics.cs:47</sub> | `WorldspaceStarHexDisplay.HighlightSelectedPlacementHex()` | postfix | `BoardModule`:120 |
| `Placement_UpdateGate_Diagnostics`<br/><sub>src/GloomhavenVR/Board/Patches/PlacementDiagnostics.cs:98</sub> | `WorldspaceStarHexDisplay.Update()` | prefix | `BoardModule`:121 |
| `Placement_Click_Diagnostics`<br/><sub>src/GloomhavenVR/Board/Patches/PlacementDiagnostics.cs:155</sub> | `Choreographer.TileHandler()` | prefix | `BoardModule`:122 |
| `InitiativeTrackPlayerAvatar_OnClick_Guard`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:56</sub> | `InitiativeTrackPlayerAvatar.OnClick()` | prefix | `BoardModule`:106 |
| `Choreographer_TileHandler_OwnershipGuard`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:113</sub> | `Choreographer.TileHandler()` | prefix | `BoardModule`:111 |
| `CharacterManager_OnControlReleased_Fallback`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:168</sub> | `CharacterManager.OnControlReleased()` | prefix | `BoardModule`:115 |
| &nbsp; | `CharacterManager.OnControlReleased()` | postfix | &nbsp; |

## Cards

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `CardsHandUI_OnDestroy_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/CardLifecyclePatches.cs:22</sub> | `CardsHandUI.OnDestroy()` *(private)* | prefix | `CardsModule`:44 |
| `CardsHandUI_DestroyCardUI_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/CardLifecyclePatches.cs:35</sub> | `CardsHandUI.DestroyCardUI()` | prefix | `CardsModule`:45 |
| `CardsHandUI_OnLoseCardClick_Gate`<br/><sub>src/GloomhavenVR/Cards/Patches/DamageFlowPatches.cs:42</sub> | `CardsHandUI.OnLoseCardClick()` *(private)* | prefix | `CardsModule`:47 |
| `TakeDamagePanel_BurnHover_Skip`<br/><sub>src/GloomhavenVR/Cards/Patches/DamageFlowPatches.cs:100</sub> | `TakeDamagePanel.OnMouseEnterBurnOne()` | prefix | `CardsModule`:48 |
| &nbsp; | `TakeDamagePanel.OnMouseEnterBurnTwo()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitBurnOne()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitBurnTwo()` | prefix | &nbsp; |
| `DialogPopup_Show_HoverStrip`<br/><sub>src/GloomhavenVR/Cards/Patches/DamageFlowPatches.cs:134</sub> | `DialogPopup.Show(List<GameObject>, DialogOption[], bool, int, UnityAction)` | postfix | `CardsModule`:49 |
| `CardsHandManager_ShowList_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/HandSuppressionPatches.cs:60</sub> | `CardsHandManager.Show(CPlayerActor, CardHandMode, CardPileType, List<CardPileType>, int, bool, bool, bool, CardsHandUI.CardActionsCommand, bool, bool, Action<AbilityCardUI>, Func<CAbilityCard, bool>)` | postfix | `CardsModule`:41 |
| `CardsHandManager_ShowAll_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/HandSuppressionPatches.cs:79</sub> | `CardsHandManager.Show(CardHandMode, CardPileType, List<CardPileType>, int, bool, bool, bool)` | postfix | `CardsModule`:42 |
| `CardsHandManager_ShowHands_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/HandSuppressionPatches.cs:94</sub> | `CardsHandManager.ShowHands()` *(private)* | postfix | `CardsModule`:43 |

## Compat

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `InitialInputSkip` *(degrades by design)*<br/><sub>src/GloomhavenVR/Compat/InitialInputSkip.cs:28</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `CompatModule`:59 |
| `LevelEventsController_StartListeningForEvents_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs:34</sub> | `LevelEventsController.StartListeningForEvents()` *(private)* | postfix | `CompatModule`:118 |
| `LevelEventsController_MessageWasDisplayed_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs:87</sub> | `LevelEventsController.MessageWasDisplayed()` *(private)* | postfix | `CompatModule`:119 |
| `LevelEventsController_MessageWasDismissed_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs:110</sub> | `LevelEventsController.MessageWasDismissed()` *(private)* | postfix | `CompatModule`:120 |
| `LevelMessagePageUI_OnLanguageChanged_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialHintPatches.cs:224</sub> | `LevelMessagePageUI.OnLanguageChanged()` *(private)* | postfix | `CompatModule`:121 |
| `LevelMessageUILayout_Title_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialHintPatches.cs:250</sub> | `LevelMessageUILayout.Init()` *(private)* | postfix | `CompatModule`:122 |
| &nbsp; | `LevelMessageUILayout.OnLanguageChanged()` *(private)* | postfix | &nbsp; |
| `WallFadeDisable` *(degrades by design)*<br/><sub>src/GloomhavenVR/Compat/WallFadeDisable.cs:53</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `CompatModule`:74 |

## Core

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `Choreographer_ProcessMessage_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:26</sub> | `Choreographer.ProcessMessage()` *(private)* | postfix | `VREventsModule`:34 |
| `Choreographer_SetChoreographerState_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:48</sub> | `Choreographer.SetChoreographerState()` | postfix | `VREventsModule`:35 |
| `UIManager_ToggleLockUI_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:73</sub> | `UIManager.ToggleLockUI()` | postfix | `VREventsModule`:36 |
| `UIWindow_Transition_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:101</sub> | `UIWindow.EvaluateAndTransitionToVisualState()` *(private)* | postfix | `VREventsModule`:37 |
| `MaterialLoader_LoadMaterials_RegisterPatch`<br/><sub>src/GloomhavenVR/Core/MaterialLoaderHeal.cs:755</sub> | `MaterialLoader.LoadMaterials()` | postfix | `CompatModule`:97 |

## Rig

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `CameraController_LateUpdate_Patch`<br/><sub>src/GloomhavenVR/Rig/CameraControllerPatches.cs:32</sub> | `CameraController.LateUpdate()` *(private)* | prefix | `RigModule`:44 |
| `CameraController_RefreshFocusPosition_Patch`<br/><sub>src/GloomhavenVR/Rig/CameraControllerPatches.cs:50</sub> | `CameraController.RefreshFocusPosition()` *(private)* | prefix | `RigModule`:45 |

## WorldUI

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `WorldspaceDisplayPanelBase_Patches`<br/><sub>src/GloomhavenVR/WorldUI/ActorBars.cs:572</sub> | `WorldspaceDisplayPanelBase.TrackCharacter()` | prefix | `WorldUIModule`:44 |
| &nbsp; | `WorldspaceDisplayPanelBase.LateUpdate()` *(private)* | prefix | &nbsp; |
| `InputManager_SetGamepadInputDevice_Patch`<br/><sub>src/GloomhavenVR/WorldUI/InputModeGuard.cs:73</sub> | `InputManager.SetGamepadInputDevice()` | prefix | `WorldUIModule`:45 |
| `InputManager_AssignGamepadBindings_Patch`<br/><sub>src/GloomhavenVR/WorldUI/InputModeGuard.cs:93</sub> | `InputManager.AssignGamepadBindingsToPlayerActions()` *(private)* | prefix | `WorldUIModule`:46 |
| `ShowUIWindowSuppressor` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs:111</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `EscMenuInputBlock`:79 |
| `EscMenuEscapeSuppressor` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs:152</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `EscMenuInputBlock`:80 |
| `InitiativeHoverCardBlock`<br/><sub>src/GloomhavenVR/WorldUI/Patches/InitiativeHoverCardBlock.cs:39</sub> | `CardsHandManager.Preview(CPlayerActor, Transform)` | prefix | `WorldUIModule`:49 |
| `KeyboardHideSuppressor` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/KeyboardAutoHideBlock.cs:91</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `KeyboardAutoHideBlock`:62 |
| `TakeDamagePanelSafety`<br/><sub>src/GloomhavenVR/WorldUI/Patches/TakeDamagePanelSafety.cs:58</sub> | `TakeDamagePanel.TakeDamage()` | prefix | `WorldUIModule`:48 |
| &nbsp; | `TakeDamagePanel.BurnAvailableCard(bool)` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.BurnDiscardedCards(bool)` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.PreviewDamage()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.PreviewAvailableCards()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.PreviewDiscardedCards()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.ResetPreviewing()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.ClearSelectedToggle()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.ShowDamageTooltip()` *(private)* | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.UpdateTakeDamageOptionVisuals()` *(private)* | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseEnterTakeDamage()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitTakeDamage()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseEnterBurnOne()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitBurnOne()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseEnterBurnTwo()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitBurnTwo()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.get_IsLethalDamage()` *(private)* | prefix | &nbsp; |
| `UITextInfoPanel_Show_Patch`<br/><sub>src/GloomhavenVR/WorldUI/Surfaces/PropInfoSurface.cs:254</sub> | `UITextInfoPanel.Show((string, string)[])` | postfix | `WorldUIModule`:47 |

## Registration sites

| Module file | Patch classes registered |
|---|---|
| `src/GloomhavenVR/Board/BoardModule.cs` | `ActorBehaviour_HeldTransform_Patch`, `CharacterManager_OnControlReleased_Fallback`, `Choreographer_TileHandler_OwnershipGuard`, `Controller_CommonLoop_Patch`, `HexHoverClear`, `HexSelect_ProjectorMaterialAdjustment_Patch`, `InitiativeTrackPlayerAvatar_OnClick_Guard`, `InputManager_CursorPosition_Patch`, `MF_FindInteractableAtMousePosition_Patch`, `PingNameTag_Patch`, `Placement_Click_Diagnostics`, `Placement_Hover_Diagnostics`, `Placement_UpdateGate_Diagnostics`, `UIManager_IsPointerOverUI_Patch` |
| `src/GloomhavenVR/Cards/CardsModule.cs` | `CardsHandManager_ShowAll_Patch`, `CardsHandManager_ShowHands_Patch`, `CardsHandManager_ShowList_Patch`, `CardsHandUI_DestroyCardUI_Patch`, `CardsHandUI_OnDestroy_Patch`, `CardsHandUI_OnLoseCardClick_Gate`, `DialogPopup_Show_HoverStrip`, `TakeDamagePanel_BurnHover_Skip` |
| `src/GloomhavenVR/Compat/CompatModule.cs` | `InitialInputSkip`, `LevelEventsController_MessageWasDismissed_Patch`, `LevelEventsController_MessageWasDisplayed_Patch`, `LevelEventsController_StartListeningForEvents_Patch`, `LevelMessagePageUI_OnLanguageChanged_Patch`, `LevelMessageUILayout_Title_Patch`, `MaterialLoader_LoadMaterials_RegisterPatch`, `WallFadeDisable` |
| `src/GloomhavenVR/Core/Events/VREventsModule.cs` | `Choreographer_ProcessMessage_Patch`, `Choreographer_SetChoreographerState_Patch`, `UIManager_ToggleLockUI_Patch`, `UIWindow_Transition_Patch` |
| `src/GloomhavenVR/Rig/RigModule.cs` | `CameraController_LateUpdate_Patch`, `CameraController_RefreshFocusPosition_Patch` |
| `src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs` | `EscMenuEscapeSuppressor`, `ShowUIWindowSuppressor` |
| `src/GloomhavenVR/WorldUI/Patches/KeyboardAutoHideBlock.cs` | `KeyboardHideSuppressor` |
| `src/GloomhavenVR/WorldUI/WorldUIModule.cs` | `InitiativeHoverCardBlock`, `InputManager_AssignGamepadBindings_Patch`, `InputManager_SetGamepadInputDevice_Patch`, `TakeDamagePanelSafety`, `UITextInfoPanel_Show_Patch`, `WorldspaceDisplayPanelBase_Patches` |

The preloader (`GloomhavenVR.Preload.dll`) patches **no** assemblies (`TargetDLLs` is empty); it only installs the OpenXR natives + UnitySubsystems manifest.
