# CONTROLBOARD — confirm-flow inventory for the physical VR control board (research, 2026-07-17)

Purpose: hardware test #10 asked for a redesigned control board ("the tray is at a
weird angle, its elements are confusing"). This note inventories EVERYTHING a player
must confirm/toggle/click during scenario play, with class/method citations from the
decompiled game sources (`decompiled/`, ilspycmd output of the real v1.1.8307.0 DLLs),
and maps each flow onto the physical board. Companion to CARDS.md / PATCH-TARGETS.md.

## 1. ReadyButton — the game's single confirm hub

`public class ReadyButton : ButtonOnBlockingPanel` (GH.Runtime/ReadyButton.cs:15).
Its `EButtonState` enum (ReadyButton.cs:17-35, implicit values 0-15) is a map of every
confirm the game funnels through one button:

| # | State | Meaning (label key) |
|---|---|---|
| 0 | EREADYBUTTONENDSELECTION | commit card selection (`GUI_END_SELECTION`, Choreographer.cs:4167) |
| 1 | EREADYBUTTONENDROUND | (defined, never referenced — legacy) |
| 2 | EREADYBUTTONENDTURN | end own turn (`GUI_END_TURN`/`GUI_END_EXTRA_TURN`, Choreographer.cs:12709) |
| 3 | EREADYBUTTONPASS | pass/confirm setup (`GUI_CONFIRM`, Choreographer.cs:3721) |
| 4 | EREADYBUTTONCONTINUE | acknowledge/continue (`GUI_CONTINUE`, Choreographer.cs:3716/11906) |
| 5 | EREADYBUTTONLONGREST | (defined, never referenced — legacy) |
| 6 | EREADYBUTTONCONFIRMTARGETS | confirm target picks (`GUI_CONFIRM_TARGETS`, Choreographer.cs:4909…) |
| 7 | EREADYBUTTONFINISHTARGETSELECT | (defined, never referenced — legacy) |
| 8 | EREADYBUTTONCONFIRM | confirm action (`GUI_CONFIRM_ACTION`, Choreographer.cs:4939…) |
| 9 | EREADYBUTTONCONFIRMMOVEMENT | confirm movement (`GUI_CONFIRM_MOVEMENT`, Choreographer.cs:4354…) |
| 10 | EREADYBUTTONOPENDOOR | open door (`GUI_OPEN_DOOR`) |
| 11 | EREADYBUTTONCONFIRMDISABLED | confirm shown but disabled |
| 12 | EREADYBUTTONNA | idle default (ReadyButton.cs:69) |
| 13 | EREADYBUTTONRECOVERCARD | recover-card confirm (`GUI_CONFIRM`) |
| 14 | EREADYBUTTONIMPROVEDSHORTREST | improved short rest → long rest (`GUI_PERFORM_LONG_REST`, CardsHandUI.cs:741) |
| 15 | EREADYBUTTONCONFIRMITEM | item consume/infuse confirm (`GUI_CONFIRM`, UIUseItemsBar.cs:128) |

State is set by `Toggle(bool active, EButtonState state, string text, …)`
(ReadyButton.cs:444-478) / `AlternativeAction(...)` (:422) / `ResetAlternativeAction(...)`
(:387); the per-phase chooser is `Choreographer` (public field `readyButton`,
Choreographer.cs:150).

**Click dispatch** — `OnClick(bool)` (ReadyButton.cs:187, guard: `ButtonComponent.enabled
&& !warningMask.activeSelf && readyButton.interactable`, gamepad branch →
LongConfirmHandler) → `public void OnClickInternal(bool)` (ReadyButton.cs:245-363):
1. online (except IMPROVEDSHORTREST / CONFIRMITEM / RECOVERCARD / exhausted):
   `Synchronizer.SendGameAction(GameActionType.ConfirmAction, …)` (:261) + processor halt;
2. if `actionsQueue.Count > 0`: pop + invoke the queued alternative action
   (item use, extra-turn cards, …) (:284-313);
3. else if `state >= EREADYBUTTONCONTINUE` (≥4): `ScenarioRuleClient.StepComplete()` (:317-324);
4. else (ENDSELECTION/ENDTURN/PASS): `Choreographer.s_Choreographer.Pass` →
   `Choreographer.Pass()` (Choreographer.cs:1795) → `ScenarioRuleClient.Pass()`
   (ScenarioRuleClient.cs:993) (:326-332).

**VR board mapping**: the CONFIRM button calls exactly this — guard replicated, then
`OnClickInternal()` (`CardsGameApi.ClickReady`). No spin-wait inside (Pass/StepComplete
only message the SRL); queued via CardActionQueue for ordering. The board label mirrors
`ReadyButton.buttonText.text` (ReadyButton.cs:41) so it is phase-correct and localized
for free.

## 2. CardsActionControlller — half-execution phases

`GH.Runtime/CardsActionControlller.cs:12`, singleton `s_Instance` (:26).
`Phase` enum (:14-21): `None=0, Select1stCard=1, Pick1stTarget=2, Select2ndCard=3,
Pick2ndTarget=4`; `GetPhase()` (:202). Half chosen: `OnCardClicked(FullAbilityCard,
ActionType)` (:453) → Pick*Target; half finished: `OnActionFinished()` (:474) →
Select2nd/Finish. **Undo**: UI-side `OnActionUndo()` (:544-565, unreserves elements,
steps the phase back); SRL-side rollback via `UndoButton` → `ScenarioRuleClient.Undo(...)`
(UndoButton.cs:178). "Any element" infusion during a half:
`PickAnyInfusionElements(...)` (:567-620) → `UIUseAbilitiesBar.ShowInfusionsAction`.

## 3. Card selection commit

- Selecting a card commits IMMEDIATELY into the round pile:
  `ScenarioRuleClient.MoveAbilityCard(...)` from the card click handler
  (CardsHandUI.cs:2015; spin-wait documented in CardsGameApi header). Our fan→slot
  drop already drives this via `CardsHandUI.SelectCard`.
- `CPlayerActorExtensions.IsCardSelectionReady` (CPlayerActorExtensions.cs:5) turns
  true at 2 cards / long rest → the board's CONFIRM button lights green.
- The WHOLE selection is committed by Ready in state ENDSELECTION (§1 dispatch 4).
  Multiplayer ready-up runs parallel via `UIReadyToggle` (GameActionType.ReadyUpPlayer,
  UIReadyToggle.cs:658+) — not board scope (the game shows it as its own overlay).

## 4. Short / long rest

- **Short rest**: `ShortRest.MouseClick()` (ShortRest.cs:202) → `Select(!isSelected)`
  (:208) → `YesNoDialog.Show` (:244). Dialog: `YesNoDialog.Init("GUI_SHORT_REST_CONFIRMATION",
  …)` (ShortRest.cs:100; YesNoDialog.cs:91). On yes → `CardsHandUI.PerformShortRest`
  (CardsHandUI.cs:719-875) with its burn/redraw `DialogPopup` options (`GUI_LOSE_CARD` /
  `GUI_REDRAW_CARD`, :768-769) → `ScenarioRuleClient.ShortRestPlayer(...)` (:957).
  VR board: short-rest token → `ShortRest.MouseClick()` (CardsGameApi.ToggleShortRest);
  the confirm dialog stays the game's 2D dialog (ModalUI mode).
- **Long rest**: pseudo-card CardID −1 toggle (CardsHandUI.cs:1943-1959; VR board token →
  `AbilityCardUI.OnClick`, CardsGameApi.ToggleLongRest). Confirmation:
  `CardsHandManager.ShowLongRestConfirmation(...)` (CardsHandManager.cs:710) →
  `LongRestConfirmationButton.ShowConfirm/Toggle` (LongRestConfirmationButton.cs:104/119,
  online `GameActionType.ToggleLongRestAbility`). The burn-a-discarded-card step arrives
  on the long-rested turn as `CardHandMode.LoseCard` → served by the fan's
  poke-to-select path.

## 5. Item use

- Equipped slots: `UIItemScenario.OnPointerDown()` (UIItemScenario.cs:193) →
  `UseItemService.UseItem(Item)`.
- In-turn bar: `UIUseItemsBar` (singleton, UIUseItemsBar.cs:13) — slot click →
  `new UseItemService(owner).UseItem(item, …)` (:427). When the item consumes/infuses,
  `SetActiveItemButtons` (:119-158) arms Ready as `EREADYBUTTONCONFIRMITEM` +
  `QueueAlternativeAction(UseItem)` (:128-129), Undo override `GUI_UNDO` (:139), Skip →
  use-without-effects `GUI_SKIP` (:149-152).
- Commit: `UseItemService.UseItem(...)` (UseItemService.cs:18-55) →
  `ScenarioRuleClient.ToggleItem(Item, owner)` (:54; def ScenarioRuleClient.cs:978);
  online `GameActionType.UseItem` (:48).
- **VR board mapping**: item confirm rides the SAME Ready path — our CONFIRM button
  already covers `EREADYBUTTONCONFIRMITEM` (label mirrors "Confirm"). Item slot
  clicking itself stays on the game's 2D bar (world-panel scope, Phase-3c).

## 6. Persistent / "logged-in" ability activations

- Activation during a turn: `UIActiveBonusBar` (UIActiveBonusBar.cs:10) slots of type
  `UIUseActiveBonus` (UIUseActiveBonus.cs:8); click →
  `activeBonus.ToggleActiveBonus(element, fromClick: true)` (:96) →
  `ScenarioRuleClient.ToggleActiveBonus(...)` (ScenarioRuleClient.cs:958); locked at
  end-turn by `LockToggledActiveBonuses()` (UIActiveBonusBar.cs:166-176 →
  `ScenarioRuleClient.LockActiveBonus`, :963). Songs are toggle bonuses
  (`it.IsSong && …IsToggleBonus`, UIActiveBonusBar.cs:369-382).
- Cancelling a persistent bonus: `PersistentAbilitiesUI.OnCancelActiveAbility`
  (PersistentAbilitiesUI.cs:366-411) → `UIConfirmationBoxManager.ShowCancelActiveAbility`
  (:392) → `CClass.CancelActiveBonus(bonus)` (:394).
- **VR board mapping**: not on the board (turn-scoped bars, appear mid-action) — they
  ride the ModalUI/world-panel path; the board's CONFIRM still fires their Ready
  confirms because they queue alternative actions on the same ReadyButton.

## 7. Element infusion choices

- Board display: `InfusionBoardUI` (InfusionBoardUI.cs:9) — `ReserveElement(s)`
  (:87/:106), `UnreserveElements()` (:80), `GetAvailableElements()` (:155).
- "Any element" pick: `UIUseAbilitiesBar.ShowInfusionsAction(actor, card, action,
  infusions, onSelect)` (UIUseAbilitiesBar.cs:293-306, networks `NetworkInfuseAbility`);
  generic single pick `ShowGenericInfusion(...)` (:308-326). Triggered from
  `CardsActionControlller.PickAnyInfusionElements` (§2).
- Model: `ElementInfusionBoardManager` (ScenarioRuleLibrary) — `Infuse(list, actor)`,
  `EElement` (incl. `Any`), columns Inert/Strong/Waning.
- **VR board mapping**: element picking appears mid-action as a game overlay — ModalUI
  scope, confirmed by the same Ready states (§1). Not duplicated on the board (P3c
  candidate: physical element board).

## 8. Skip / pass

`SkipButton` (SkipButton.cs:12; default label `GUI_SKIP_MOVEMENT`, :47).
`OnClick(bool)` (:80-135): online `GameActionType.SkipAction` (:95); dispatch →
`ScenarioRuleClient.PassStep()` (:131; def ScenarioRuleClient.cs:998) for push/pull, else
`Choreographer.Pass()` (:126). Whole-turn pass = Ready in PASS/ENDTURN states (§1).
**VR board mapping**: not a separate board button in CardsSelection (skip is never live
there); during action phases the laser/2D path reaches the game's own Skip. Candidate
for the P3c in-turn board revision.

## 9. Localization

Game wrapper: `GLOOM.LocalizationManager` (GH.Runtime/GLOOM/LocalizationManager.cs:8-10)
around I2 Loc. Signatures:
- `public static string GetTranslation(string Term, bool FixForRTL = true, int
  maxLineLengthForRTL = 0, bool ignoreRTLnumbers = true, bool applyParameters = false,
  GameObject localParametersRoot = null, string overrideLanguage = null, bool
  skipWarnings = false, bool useDefaultIfMissing = false, bool returnNullIfNotFound =
  false)` (:12) — missing keys return `"UNDEFINED <color=…>"`, so prefer:
- `public static bool TryGetTranslation(string Term, out string Translation, …)` (:47)
  — used by `CardsGameApi.Localize(key, fallback)`.

Verified keys used by the board: `GUI_UNDO` (UndoButton.cs:67), `GUI_CONFIRM`
(Choreographer.cs:3721), `GUI_END_SELECTION` (Choreographer.cs:4167), `GUI_LONG_REST`
(CardsHandUI.cs:731), `GUI_SKIP` (UIUseItemsBar.cs:150). No plain `GUI_SHORT_REST` /
`GUI_INITIATIVE` key exists — the board falls back to English literals for those two
(TryGetTranslation probes them anyway in case a locale ships them).

## Board element → game entry cheat-sheet (implemented in P7)

| Board element | VR entry | Game path (verified) |
|---|---|---|
| CONFIRM button | `CardsGameApi.ClickReady()` | ReadyButton.OnClickInternal (ReadyButton.cs:245) → Pass()/StepComplete() |
| UNDO button | `CardsGameApi.ClickUndo()` | UndoButton.OnClick (UndoButton.cs:94) → ScenarioRuleClient.Undo/ClearTargets |
| Card slot drop | `CardsGameApi.SelectCard` | CardsHandUI.SelectCard (CardsHandUI.cs:2604), spin-wait → CardActionQueue |
| Card slot take-back | `CardsGameApi.UnselectCard` | CardsHandUI.UnselectCard (CardsHandUI.cs:2619) |
| Slot swap / badge | `CardsGameApi.SwapInitiative` | AbilityCardUI.SwapInitiative (AbilityCardUI.cs:809) |
| Short-rest token | `CardsGameApi.ToggleShortRest` | ShortRest.MouseClick (ShortRest.cs:202) → YesNoDialog |
| Long-rest token | `CardsGameApi.ToggleLongRest` | AbilityCardUI.OnClick on card −1 (CardsHandUI.cs:2644/AbilityCardUI.cs:329) |
| Top/bottom half (HalfSelection layout) | `CardsGameApi.PlayHalf` | FullAbilityCard.OnAbilityClick (FullAbilityCard.cs:606) |
