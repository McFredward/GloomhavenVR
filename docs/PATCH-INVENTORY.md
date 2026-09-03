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


**85 patch classes, 141 patched methods.**

## Board

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `Controller_CommonLoop_Patch`<br/><sub>src/GloomhavenVR/Board/BoardClickDriver.cs:781</sub> | `Controller.CommonLoop()` *(private)* | postfix | `BoardModule`:115 |
| `ActorBehaviour_HeldTransform_Patch`<br/><sub>src/GloomhavenVR/Board/FigureGrab/ActorBehaviour_HeldTransform_Patch.cs:39</sub> | `ActorBehaviour.Update()` *(private)* | prefix | `BoardModule`:118 |
| &nbsp; | `ActorBehaviour.LateUpdate()` *(private)* | prefix | &nbsp; |
| &nbsp; | `ActorBehaviour.SetHilighted()` | prefix | &nbsp; |
| `HexHighlightFix.ProjectorModifier_Awake_Patch`<br/><sub>src/GloomhavenVR/Board/HexHighlightFix.cs:431</sub> | `ProjectorModifier.Awake()` *(private)* | postfix | `BoardModule`:114 |
| `HexHighlightFix.HexSelect_ProjectorMaterialAdjustment_Patch`<br/><sub>src/GloomhavenVR/Board/HexHighlightFix.cs:545</sub> | `HexSelect_Control.ProjectorMaterialAdjustment()` *(private)* | postfix | `BoardModule`:106 |
| `AllCardsViewerBlock`<br/><sub>src/GloomhavenVR/Board/Patches/AllCardsViewerBlock.cs:115</sub> | `CardsHandManager.ToggleViewAllCards(CPlayerActor, bool)` | prefix | `BoardModule`:141 |
| &nbsp; | `CardsHandUI.ToggleFullCardsPreview(bool, bool)` | prefix | &nbsp; |
| `InitiativeTrack_ShowMonsterClasses_ArmSkip`<br/><sub>src/GloomhavenVR/Board/Patches/EnemyInfoPhaseSkip.cs:397</sub> | `InitiativeTrack.ShowMonsterClassesForSelectingRoundAbilityCards()` | postfix | `BoardModule`:155 |
| `InitiativeTrack_Update_TickSkip`<br/><sub>src/GloomhavenVR/Board/Patches/EnemyInfoPhaseSkip.cs:417</sub> | `InitiativeTrack.Update()` *(private)* | postfix | `BoardModule`:156 |
| `HexHoverClear`<br/><sub>src/GloomhavenVR/Board/Patches/HexHoverClear.cs:135</sub> | `WorldspaceStarHexDisplay.Update()` | postfix | `BoardModule`:100 |
| `HoverPickPatch`<br/><sub>src/GloomhavenVR/Board/Patches/HoverPickPatch.cs:159</sub> | `HoverRegisterer.Update()` *(private)* | prefix | `BoardModule`:96 |
| `MF_FindInteractableAtMousePosition_Patch`<br/><sub>src/GloomhavenVR/Board/Patches/PickingPatches.cs:42</sub> | `MF.FindInteractableAtMousePosition()` | prefix | `BoardModule`:86 |
| `InputManager_CursorPosition_Patch`<br/><sub>src/GloomhavenVR/Board/Patches/PickingPatches.cs:87</sub> | `InputManager.get_CursorPosition()` | prefix | `BoardModule`:87 |
| `UIManager_IsPointerOverUI_Patch`<br/><sub>src/GloomhavenVR/Board/Patches/PickingPatches.cs:141</sub> | `UIManager.get_IsPointerOverUI()` | prefix | `BoardModule`:88 |
| `PingNameTag_Patch` *(degrades by design)*<br/><sub>src/GloomhavenVR/Board/Patches/PingNameTag.cs:45</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `BoardModule`:148 |
| `Placement_Hover_Diagnostics`<br/><sub>src/GloomhavenVR/Board/Patches/PlacementDiagnostics.cs:47</sub> | `WorldspaceStarHexDisplay.HighlightSelectedPlacementHex()` | postfix | `BoardModule`:158 |
| `Placement_UpdateGate_Diagnostics`<br/><sub>src/GloomhavenVR/Board/Patches/PlacementDiagnostics.cs:98</sub> | `WorldspaceStarHexDisplay.Update()` | prefix | `BoardModule`:159 |
| `Placement_Click_Diagnostics`<br/><sub>src/GloomhavenVR/Board/Patches/PlacementDiagnostics.cs:155</sub> | `Choreographer.TileHandler()` | prefix | `BoardModule`:160 |
| `InitiativeTrackPlayerAvatar_OnClick_Guard`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:60</sub> | `InitiativeTrackPlayerAvatar.OnClick()` | prefix | `BoardModule`:122 |
| &nbsp; | `InitiativeTrackPlayerAvatar.OnClick()` | postfix | &nbsp; |
| `InteractabilityManager_PortraitFocusBypass`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:290</sub> | `InteractabilityManager.ShouldAllowClickForExtendedButton()` | prefix | `BoardModule`:130 |
| `Choreographer_TileHandler_OwnershipGuard`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:368</sub> | `Choreographer.TileHandler()` | prefix | `BoardModule`:135 |
| `CharacterManager_OnControlReleased_Fallback`<br/><sub>src/GloomhavenVR/Board/Patches/SelectionGuardPatches.cs:423</sub> | `CharacterManager.OnControlReleased()` | prefix | `BoardModule`:145 |
| &nbsp; | `CharacterManager.OnControlReleased()` | postfix | &nbsp; |

## Cards

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `FullAbilityCard_ShowCard_ArtGuard`<br/><sub>src/GloomhavenVR/Cards/Patches/CardArtPatches.cs:23</sub> | `FullAbilityCard.ShowCard()` | prefix | `CardsModule`:69 |
| `CardsHandUI_OnDestroy_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/CardLifecyclePatches.cs:22</sub> | `CardsHandUI.OnDestroy()` *(private)* | prefix | `CardsModule`:44 |
| `CardsHandUI_DestroyCardUI_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/CardLifecyclePatches.cs:35</sub> | `CardsHandUI.DestroyCardUI()` | prefix | `CardsModule`:45 |
| `CardsHandUI_OnLoseCardClick_Gate`<br/><sub>src/GloomhavenVR/Cards/Patches/DamageFlowPatches.cs:42</sub> | `CardsHandUI.OnLoseCardClick()` *(private)* | prefix | `CardsModule`:47 |
| `TakeDamagePanel_BurnHover_Skip`<br/><sub>src/GloomhavenVR/Cards/Patches/DamageFlowPatches.cs:313</sub> | `TakeDamagePanel.OnMouseEnterBurnOne()` | prefix | `CardsModule`:48 |
| &nbsp; | `TakeDamagePanel.OnMouseEnterBurnTwo()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitBurnOne()` | prefix | &nbsp; |
| &nbsp; | `TakeDamagePanel.OnMouseExitBurnTwo()` | prefix | &nbsp; |
| `DialogPopup_Show_HoverStrip`<br/><sub>src/GloomhavenVR/Cards/Patches/DamageFlowPatches.cs:347</sub> | `DialogPopup.Show(List<GameObject>, DialogOption[], bool, int, UnityAction)` | postfix | `CardsModule`:49 |
| `MapPartyEnhancementShopService_AddEnhancement_FanRefresh`<br/><sub>src/GloomhavenVR/Cards/Patches/EnhancementCommitPatch.cs:36</sub> | `MapPartyEnhancementShopService.AddEnhancement()` | postfix | `CardsModule`:76 |
| `FullCardEventPusher_Enter_LaserGeometric`<br/><sub>src/GloomhavenVR/Cards/Patches/HalfHoverPatches.cs:37</sub> | `FullCardEventPusher.OnPointerEnter()` | prefix | `CardsModule`:54 |
| `FullCardEventPusher_Exit_LaserGeometric`<br/><sub>src/GloomhavenVR/Cards/Patches/HalfHoverPatches.cs:48</sub> | `FullCardEventPusher.OnPointerExit()` | prefix | `CardsModule`:55 |
| `FullAbilityCard_Enter_HalfHoverSync`<br/><sub>src/GloomhavenVR/Cards/Patches/HalfHoverPatches.cs:68</sub> | `FullAbilityCard.OnPointerEnter()` | postfix | `CardsModule`:61 |
| `FullAbilityCard_Exit_HalfHoverSync`<br/><sub>src/GloomhavenVR/Cards/Patches/HalfHoverPatches.cs:77</sub> | `FullAbilityCard.OnPointerExit()` | postfix | `CardsModule`:62 |
| `CardsHandManager_ShowList_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/HandSuppressionPatches.cs:60</sub> | `CardsHandManager.Show(CPlayerActor, CardHandMode, CardPileType, List<CardPileType>, int, bool, bool, bool, CardsHandUI.CardActionsCommand, bool, bool, Action<AbilityCardUI>, Func<CAbilityCard, bool>)` | postfix | `CardsModule`:41 |
| `CardsHandManager_ShowAll_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/HandSuppressionPatches.cs:79</sub> | `CardsHandManager.Show(CardHandMode, CardPileType, List<CardPileType>, int, bool, bool, bool)` | postfix | `CardsModule`:42 |
| `CardsHandManager_ShowHands_Patch`<br/><sub>src/GloomhavenVR/Cards/Patches/HandSuppressionPatches.cs:94</sub> | `CardsHandManager.ShowHands()` *(private)* | postfix | `CardsModule`:43 |

## Compat

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `InitialInputSkip` *(degrades by design)*<br/><sub>src/GloomhavenVR/Compat/InitialInputSkip.cs:28</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `CompatModule`:59 |
| `LoadoutHostingGuard`<br/><sub>src/GloomhavenVR/Compat/LoadoutHostingGuard.cs:116</sub> | `UILoadoutManager.OnSwitchedToMultiplayer()` *(private)* | prefix | `CompatModule`:83 |
| `TutorialChainHold` *(degrades by design)*<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialChainHold.cs:74</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `CompatModule`:171 |
| `LevelEventsController_StartListeningForEvents_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs:35</sub> | `LevelEventsController.StartListeningForEvents()` *(private)* | postfix | `CompatModule`:160 |
| `LevelEventsController_MessageWasDisplayed_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs:123</sub> | `LevelEventsController.MessageWasDisplayed()` *(private)* | postfix | `CompatModule`:161 |
| `LevelEventsController_MessageWasDismissed_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialFlowPatches.cs:146</sub> | `LevelEventsController.MessageWasDismissed()` *(private)* | postfix | `CompatModule`:162 |
| `LevelMessagePageUI_OnLanguageChanged_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialHintPatches.cs:413</sub> | `LevelMessagePageUI.OnLanguageChanged()` *(private)* | postfix | `CompatModule`:163 |
| `LevelMessageUILayout_Title_Patch`<br/><sub>src/GloomhavenVR/Compat/Tutorial/TutorialHintPatches.cs:449</sub> | `LevelMessageUILayout.Init()` *(private)* | postfix | `CompatModule`:164 |
| &nbsp; | `LevelMessageUILayout.OnLanguageChanged()` *(private)* | postfix | &nbsp; |
| `WallFadeDisable` *(degrades by design)*<br/><sub>src/GloomhavenVR/Compat/WallFadeDisable.cs:53</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `CompatModule`:71 |

## Core

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `Choreographer_ProcessMessage_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:26</sub> | `Choreographer.ProcessMessage()` *(private)* | postfix | `VREventsModule`:34 |
| `Choreographer_SetChoreographerState_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:48</sub> | `Choreographer.SetChoreographerState()` | postfix | `VREventsModule`:35 |
| `UIManager_ToggleLockUI_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:73</sub> | `UIManager.ToggleLockUI()` | postfix | `VREventsModule`:36 |
| `UIWindow_Transition_Patch`<br/><sub>src/GloomhavenVR/Core/Events/GameEventPatches.cs:101</sub> | `UIWindow.EvaluateAndTransitionToVisualState()` *(private)* | postfix | `VREventsModule`:37 |
| `MaterialLoader_LoadMaterials_RegisterPatch`<br/><sub>src/GloomhavenVR/Core/MaterialLoaderHeal.cs:1104</sub> | `MaterialLoader.LoadMaterials()` | postfix | `CompatModule`:137 |
| `TilesOcclusionVolume_Start_RegisterPatch`<br/><sub>src/GloomhavenVR/Core/SceneRegistry.cs:260</sub> | `TilesOcclusionVolume.Start()` *(private)* | postfix | `SceneRegistry`:108 |
| `UnityGameEditorDoorProp_Start_RegisterPatch`<br/><sub>src/GloomhavenVR/Core/SceneRegistry.cs:272</sub> | `UnityGameEditorDoorProp.Start()` *(private)* | postfix | `SceneRegistry`:123 |
| `ProceduralTileObserver_OnEnable_RegisterPatch`<br/><sub>src/GloomhavenVR/Core/SceneRegistry.cs:288</sub> | `ProceduralTileObserver.OnEnable()` *(private)* | postfix | `SceneRegistry`:138 |
| `ProceduralBase_NotifyContentPlacementComplete_WaterPatch`<br/><sub>src/GloomhavenVR/Core/Water/WaterTerrainVR.cs:3362</sub> | `ProceduralBase.NotifyContentPlacementComplete()` | postfix | `WaterTerrainVR`:284 |

## Rig

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `CameraController_LateUpdate_Patch`<br/><sub>src/GloomhavenVR/Rig/CameraControllerPatches.cs:32</sub> | `CameraController.LateUpdate()` *(private)* | prefix | `RigModule`:44 |
| `CameraController_RefreshFocusPosition_Patch`<br/><sub>src/GloomhavenVR/Rig/CameraControllerPatches.cs:50</sub> | `CameraController.RefreshFocusPosition()` *(private)* | prefix | `RigModule`:45 |

## WorldUI

| Patch class | Target | Kind | Registered by |
|---|---|---|---|
| `WorldspaceDisplayPanelBase_Patches`<br/><sub>src/GloomhavenVR/WorldUI/ActorBars.cs:1623</sub> | `WorldspaceDisplayPanelBase.TrackCharacter()` | prefix | `WorldUIModule`:52 |
| &nbsp; | `WorldspaceDisplayPanelBase.LateUpdate()` *(private)* | prefix | &nbsp; |
| `InputManager_SetGamepadInputDevice_Patch`<br/><sub>src/GloomhavenVR/WorldUI/Grab/InputModeGuard.cs:75</sub> | `InputManager.SetGamepadInputDevice()` | prefix | `WorldUIModule`:53 |
| `InputManager_AssignGamepadBindings_Patch`<br/><sub>src/GloomhavenVR/WorldUI/Grab/InputModeGuard.cs:95</sub> | `InputManager.AssignGamepadBindingsToPlayerActions()` *(private)* | prefix | `WorldUIModule`:54 |
| `MapQuestReadyUp.ClientQuestPromptSeam`<br/><sub>src/GloomhavenVR/WorldUI/MapRoom/MapQuestReadyUp.cs:753</sub> | `UIGuildmasterConfirmActionButtonPresenter.ShowQuestSelectedAction()` | postfix | `MapQuestReadyUp`:328 |
| &nbsp; | `UIGuildmasterConfirmActionPopupPresenter.ShowQuestSelectedAction()` | postfix | &nbsp; |
| `MapTravelConfirm.TravelShortcutGate`<br/><sub>src/GloomhavenVR/WorldUI/MapRoom/MapTravelConfirm.cs:2397</sub> | `AdventureMapUIManager.OnSelectedMapLocation()` *(private)* | prefix | `MapTravelConfirm`:828 |
| `MapPartyTravel.TravelDrivePatches`<br/><sub>src/GloomhavenVR/WorldUI/MapRoom/MapTravelConfirm.cs:2967</sub> | `global::PartyToken.PartyMoveTo(Vector3[], float, System.Action<List<Vector3>>, System.Action<float>)` | prefix | `MapTravelConfirm`:2608 |
| &nbsp; | `global::PartyToken.PartyMoveTo(Vector3[], System.Action, System.Action<float>)` | prefix | &nbsp; |
| &nbsp; | `global::MapTimedMovementFlow.TeleportPartyToWayPoint()` *(private)* | prefix | &nbsp; |
| &nbsp; | `global::PartyToken.PartyInstantMove()` | prefix | &nbsp; |
| &nbsp; | `global::MapTimedMovementFlow.MovePartyToEncounter()` | prefix | &nbsp; |
| `Character3DDisplayRefcount`<br/><sub>src/GloomhavenVR/WorldUI/Patches/Character3DDisplayRefcount.cs:115</sub> | `Character3DDisplayManager.Display(Component, ECharacter, string, string)` *(private)* | prefix | `WorldUIModule`:87 |
| &nbsp; | `Character3DDisplayManager.Hide()` | prefix | &nbsp; |
| &nbsp; | `Character3DDisplayManager.HideAll()` | prefix | &nbsp; |
| `CharacterClickSelectsOnly` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/CharacterClickSelectsOnly.cs:158</sub> | `NewPartyCharacterUI.OnClick()` | prefix | `WorldUIModule`:112 |
| &nbsp; | `NewPartyCharacterUI.OnClick()` | postfix | &nbsp; |
| `ConfirmationBox_ShowGenericConfirmation_Pair_Rescue_Patch` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/ConfirmationBoxRescue.cs:362</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `WorldUIModule`:150 |
| &nbsp; | *(resolved at runtime by `TargetMethod`)* | finalizer | &nbsp; |
| `ConfirmationBox_ShowGenericConfirmation_Single_Rescue_Patch` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/ConfirmationBoxRescue.cs:386</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `WorldUIModule`:151 |
| &nbsp; | *(resolved at runtime by `TargetMethod`)* | finalizer | &nbsp; |
| `ConfirmationBox_ShowGenericSpendConfirmation_Rescue_Patch` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/ConfirmationBoxRescue.cs:407</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `WorldUIModule`:152 |
| &nbsp; | *(resolved at runtime by `TargetMethod`)* | finalizer | &nbsp; |
| `ShowUIWindowSuppressor` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs:158</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `EscMenuInputBlock`:79 |
| `EscMenuEscapeSuppressor` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs:199</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `EscMenuInputBlock`:80 |
| `EscMenuTransitionFinalizer` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/EscMenuShowSafety.cs:176</sub> | *(resolved at runtime by `TargetMethod`)* | finalizer | `EscMenuInputBlock`:114 |
| `EscMenuMultiplayerCheckFinalizer` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/EscMenuShowSafety.cs:254</sub> | *(resolved at runtime by `TargetMethod`)* | finalizer | `EscMenuInputBlock`:116 |
| `InitiativeHoverCardBlock`<br/><sub>src/GloomhavenVR/WorldUI/Patches/InitiativeHoverCardBlock.cs:38</sub> | `CardsHandManager.Preview(CPlayerActor, Transform)` | prefix | `WorldUIModule`:57 |
| `InputFieldActivateWatch`<br/><sub>src/GloomhavenVR/WorldUI/Patches/InputFieldFocusWatch.cs:180</sub> | `TMP_InputField.ActivateInputField()` | postfix | `InputFieldFocusWatch`:119 |
| `InputFieldDeactivateWatch` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/InputFieldFocusWatch.cs:216</sub> | *(resolved at runtime by `TargetMethod`)* | postfix | `InputFieldFocusWatch`:131 |
| `KeyboardHideSuppressor` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/KeyboardAutoHideBlock.cs:91</sub> | *(resolved at runtime by `TargetMethod`)* | prefix | `KeyboardAutoHideBlock`:62 |
| `MainMenuLogoSwap`<br/><sub>src/GloomhavenVR/WorldUI/Patches/MainMenuLogoSwap.cs:127</sub> | `MainMenuUIManager.Awake()` *(private)* | postfix | `WorldUIModule`:119 |
| `MapLocationHoverAnimationGate`<br/><sub>src/GloomhavenVR/WorldUI/Patches/MapLocationHoverAnimationGate.cs:42</sub> | `MapLocation.Highlight()` *(private)* | postfix | `WorldUIModule`:79 |
| `MapLocationSelectorGate`<br/><sub>src/GloomhavenVR/WorldUI/Patches/MapLocationSelectorGate.cs:42</sub> | `MapLocationSelector.Update()` *(private)* | prefix | `WorldUIModule`:70 |
| `ESCMenu_OnShow_LatchGuard_Patch`<br/><sub>src/GloomhavenVR/WorldUI/Patches/MenuExitLatchGuard.cs:54</sub> | `ESCMenu.OnShow()` *(private)* | postfix | `WorldUIModule`:130 |
| `MouseWorldSurfaceCut`<br/><sub>src/GloomhavenVR/WorldUI/Patches/MouseWorldSurfaceCut.cs:75</sub> | `EventSystem.RaycastAll()` | postfix | `WorldUIModule`:65 |
| `PartyPanelStackingHide` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/PartyPanelStackingHide.cs:170</sub> | `NewPartyDisplayUI.Hide(object, bool, Action, bool)` | prefix | `WorldUIModule`:171 |
| `PartyPreviewStorm`<br/><sub>src/GloomhavenVR/WorldUI/Patches/PartyPreviewStorm.cs:113</sub> | `UIAdventurePartyAssemblyWindow.PreviewCharacterInfo(CMapCharacter)` *(private)* | prefix | `WorldUIModule`:103 |
| `SettingsClickExemption` *(degrades by design)*<br/><sub>src/GloomhavenVR/WorldUI/Patches/SettingsClickExemption.cs:102</sub> | *(resolved at runtime by `TargetMethod`)* | finalizer | `SettingsClickExemption`:146 |
| `TakeDamagePanelSafety`<br/><sub>src/GloomhavenVR/WorldUI/Patches/TakeDamagePanelSafety.cs:58</sub> | `TakeDamagePanel.TakeDamage()` | prefix | `WorldUIModule`:56 |
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
| `TooltipRaiseGuard`<br/><sub>src/GloomhavenVR/WorldUI/Patches/TooltipRaiseGuard.cs:78</sub> | `UITooltipTarget.OnPointerEnter()` | prefix | `WorldUIModule`:58 |
| &nbsp; | `UITooltip.Show()` | prefix | &nbsp; |
| `TooltipWindowPatches`<br/><sub>src/GloomhavenVR/WorldUI/Patches/TooltipWindowPatches.cs:67</sub> | `RectTransformExtensions.DeltaWorldPositionToFitTheScreen(RectTransform, Camera, float)` | prefix | `WorldUIModule`:94 |
| &nbsp; | `RectTransformExtensions.DeltaWorldPositionToFitTheScreen(RectTransform, Camera, float, float)` | prefix | &nbsp; |
| &nbsp; | `RectTransformExtensions.DeltaPositionToFitTheScreen(RectTransform, Camera, float)` | prefix | &nbsp; |
| &nbsp; | `RectTransformExtensions.DeltaPositionToFitTheScreen(RectTransform, float)` | prefix | &nbsp; |
| &nbsp; | `RectTransformExtensions.DeltaWorldPositionToFitRectTransform(RectTransform, Camera, RectTransform, bool)` | prefix | &nbsp; |
| &nbsp; | `UILocalTooltip.RefreshPosition()` | postfix | &nbsp; |
| &nbsp; | `UIPartyItemInventoryTooltip.RefreshPosition()` | postfix | &nbsp; |
| &nbsp; | `UITempleSlotTooltip.Show()` | postfix | &nbsp; |
| &nbsp; | `TooltipUI.ToggleEnable()` | postfix | &nbsp; |
| &nbsp; | `UIShopItemSlot.OnHovered()` | postfix | &nbsp; |
| &nbsp; | `UIPartyItemSlot.OnHovered()` | postfix | &nbsp; |
| &nbsp; | `UIPartyCharacterEquippementSlot.OnHovered()` *(private)* | postfix | &nbsp; |
| &nbsp; | `UIPartyCharacterEquippementSlot.OnUnHovered()` *(private)* | postfix | &nbsp; |
| &nbsp; | `UIPartyItemInventoryTooltip.Build()` *(private)* | postfix | &nbsp; |
| &nbsp; | `UITempleShopSlot.Select()` | postfix | &nbsp; |
| &nbsp; | `UITempleShopSlot.Deselect()` | postfix | &nbsp; |
| &nbsp; | `AbilityCardUI.ChangeFullCardPosition()` | prefix | &nbsp; |
| &nbsp; | `AbilityCardUI.ToggleFullCardPreview()` | postfix | &nbsp; |
| &nbsp; | `AbilityCardUI.ToggleFullCardPreview()` | prefix | &nbsp; |
| `UITextInfoPanel_Show_Patch`<br/><sub>src/GloomhavenVR/WorldUI/Surfaces/PropInfoSurface.cs:571</sub> | `UITextInfoPanel.Show((string, string)[])` | postfix | `WorldUIModule`:55 |

## Registration sites

| Module file | Patch classes registered |
|---|---|
| `src/GloomhavenVR/Board/BoardModule.cs` | `ActorBehaviour_HeldTransform_Patch`, `AllCardsViewerBlock`, `CharacterManager_OnControlReleased_Fallback`, `Choreographer_TileHandler_OwnershipGuard`, `Controller_CommonLoop_Patch`, `HexHoverClear`, `HexSelect_ProjectorMaterialAdjustment_Patch`, `HoverPickPatch`, `InitiativeTrackPlayerAvatar_OnClick_Guard`, `InitiativeTrack_ShowMonsterClasses_ArmSkip`, `InitiativeTrack_Update_TickSkip`, `InputManager_CursorPosition_Patch`, `InteractabilityManager_PortraitFocusBypass`, `MF_FindInteractableAtMousePosition_Patch`, `PingNameTag_Patch`, `Placement_Click_Diagnostics`, `Placement_Hover_Diagnostics`, `Placement_UpdateGate_Diagnostics`, `ProjectorModifier_Awake_Patch`, `UIManager_IsPointerOverUI_Patch` |
| `src/GloomhavenVR/Cards/CardsModule.cs` | `CardsHandManager_ShowAll_Patch`, `CardsHandManager_ShowHands_Patch`, `CardsHandManager_ShowList_Patch`, `CardsHandUI_DestroyCardUI_Patch`, `CardsHandUI_OnDestroy_Patch`, `CardsHandUI_OnLoseCardClick_Gate`, `DialogPopup_Show_HoverStrip`, `FullAbilityCard_Enter_HalfHoverSync`, `FullAbilityCard_Exit_HalfHoverSync`, `FullAbilityCard_ShowCard_ArtGuard`, `FullCardEventPusher_Enter_LaserGeometric`, `FullCardEventPusher_Exit_LaserGeometric`, `MapPartyEnhancementShopService_AddEnhancement_FanRefresh`, `TakeDamagePanel_BurnHover_Skip` |
| `src/GloomhavenVR/Compat/CompatModule.cs` | `InitialInputSkip`, `LevelEventsController_MessageWasDismissed_Patch`, `LevelEventsController_MessageWasDisplayed_Patch`, `LevelEventsController_StartListeningForEvents_Patch`, `LevelMessagePageUI_OnLanguageChanged_Patch`, `LevelMessageUILayout_Title_Patch`, `LoadoutHostingGuard`, `MaterialLoader_LoadMaterials_RegisterPatch`, `TutorialChainHold`, `WallFadeDisable` |
| `src/GloomhavenVR/Core/Events/VREventsModule.cs` | `Choreographer_ProcessMessage_Patch`, `Choreographer_SetChoreographerState_Patch`, `UIManager_ToggleLockUI_Patch`, `UIWindow_Transition_Patch` |
| `src/GloomhavenVR/Core/SceneRegistry.cs` | `ProceduralTileObserver_OnEnable_RegisterPatch`, `TilesOcclusionVolume_Start_RegisterPatch`, `UnityGameEditorDoorProp_Start_RegisterPatch` |
| `src/GloomhavenVR/Core/Water/WaterTerrainVR.cs` | `ProceduralBase_NotifyContentPlacementComplete_WaterPatch` |
| `src/GloomhavenVR/Rig/RigModule.cs` | `CameraController_LateUpdate_Patch`, `CameraController_RefreshFocusPosition_Patch` |
| `src/GloomhavenVR/WorldUI/MapRoom/MapQuestReadyUp.cs` | `ClientQuestPromptSeam` |
| `src/GloomhavenVR/WorldUI/MapRoom/MapTravelConfirm.cs` | `TravelDrivePatches`, `TravelShortcutGate` |
| `src/GloomhavenVR/WorldUI/Patches/EscMenuInputBlock.cs` | `EscMenuEscapeSuppressor`, `EscMenuMultiplayerCheckFinalizer`, `EscMenuTransitionFinalizer`, `ShowUIWindowSuppressor` |
| `src/GloomhavenVR/WorldUI/Patches/InputFieldFocusWatch.cs` | `InputFieldActivateWatch`, `InputFieldDeactivateWatch` |
| `src/GloomhavenVR/WorldUI/Patches/KeyboardAutoHideBlock.cs` | `KeyboardHideSuppressor` |
| `src/GloomhavenVR/WorldUI/Patches/SettingsClickExemption.cs` | `SettingsClickExemption` |
| `src/GloomhavenVR/WorldUI/WorldUIModule.cs` | `Character3DDisplayRefcount`, `CharacterClickSelectsOnly`, `ConfirmationBox_ShowGenericConfirmation_Pair_Rescue_Patch`, `ConfirmationBox_ShowGenericConfirmation_Single_Rescue_Patch`, `ConfirmationBox_ShowGenericSpendConfirmation_Rescue_Patch`, `ESCMenu_OnShow_LatchGuard_Patch`, `InitiativeHoverCardBlock`, `InputManager_AssignGamepadBindings_Patch`, `InputManager_SetGamepadInputDevice_Patch`, `MainMenuLogoSwap`, `MapLocationHoverAnimationGate`, `MapLocationSelectorGate`, `MouseWorldSurfaceCut`, `PartyPanelStackingHide`, `PartyPreviewStorm`, `TakeDamagePanelSafety`, `TooltipRaiseGuard`, `TooltipWindowPatches`, `UITextInfoPanel_Show_Patch`, `WorldspaceDisplayPanelBase_Patches` |

The preloader (`GloomhavenVR.Preload.dll`) patches **no** assemblies (`TargetDLLs` is empty); it only installs the OpenXR natives + UnitySubsystems manifest.
