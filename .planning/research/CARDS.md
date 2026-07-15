# Gloomhaven Digital — Ability Card System & Card UI/UX Flow (VR Mod Research)

All paths relative to `/home/claw/gloomhaven_vr/decompiled/`. Line numbers are from the decompiled sources (ILSpy-style, stable for this dump). Verified by reading the code — every claim below has a `path:line` reference.

---

## 1. Overview / Architecture

The game is split into a **pure-C# rules engine** and a **Unity presentation layer**, connected by two thread-safe message queues:

- **ScenarioRuleLibrary (SRL)** — the rules engine. Runs on its **own worker thread** (`ScenarioRuleClient.Start()` spawns `s_WorkThread = new Thread(Work, 1048576)`, `ScenarioRuleLibrary/ScenarioRuleLibrary/ScenarioRuleClient.cs:857-874`). Card data model, piles, phases, and action resolution all live here. It knows nothing about Unity.
- **GH.Runtime** — Unity assembly. `Choreographer` (`GH.Runtime/Choreographer.cs`, ~13k lines) is the main-thread orchestrator; `CardsHandManager`/`CardsHandUI`/`AbilityCardUI`/`FullAbilityCard` are the card UI.

**UI → engine**: static methods on `ScenarioRuleClient` enqueue `CSRLMessage` objects into a `ConcurrentQueue`/`BlockingCollection` consumed by the SRL worker thread (`ScenarioRuleClient.cs:896` `AddSRLQueueMessage`). Examples: `MoveAbilityCard`, `StepComplete`, `Pass`, `ShortRestPlayer`.

**Engine → UI**: SRL calls `ScenarioRuleClient.MessageHandler(CMessageData)` — a delegate registered by the UI. `Choreographer.Awake()` registers `ScenarioRuleClient.SetMessageHandler(MessageHandler)` (`GH.Runtime/Choreographer.cs:666`). `Choreographer.MessageHandler` (`Choreographer.cs:1645`) appends to `m_MessageQueue` (locked `List<CMessageData>`), which `Choreographer.Update()` drains on the main thread (`Choreographer.cs:2344-2392`) into a giant `ProcessMessage(CMessageData)` switch (`Choreographer.cs:3395`).

This is exactly the seam a VR mod wants: **the engine-facing API (`ScenarioRuleClient.*`, `GameState.*`) is UI-agnostic and callable from anywhere on the main thread**, and the message pump tells you every state change.

---

## 2. Data Model (ScenarioRuleLibrary)

### CBaseCard — `ScenarioRuleLibrary/ScenarioRuleLibrary/CBaseCard.cs`
- `enum ActionType { TopAction, DefaultAttackAction, DefaultMoveAction, BottomAction, NA }` (`CBaseCard.cs:15-22`)
- `enum ECardType { None, CharacterAbility, HeroSummonAbility, MonsterAbility, Item, AttackModifier, ScenarioModifier }` (`CBaseCard.cs:25-34`)
- `enum ECardPile { None, Discarded, Round, Hand, Activated, Lost, PermanentlyLost }` (`CBaseCard.cs:37-46`) — **card state enum**
- Properties: `int ID`, `ECardType CardType`, `ECardPile CurrentCardPile { get; set; }`, `bool ActionHasHappened`, `List<CActiveBonus> ActiveBonuses`, `string Name` (resolved from YML) (`CBaseCard.cs:85-95`)
- `AddActiveBonus(...)` (`CBaseCard.cs:156`) — where persistent/active bonuses attach to a card.

### CBaseAbilityCard — `ScenarioRuleLibrary/ScenarioRuleLibrary/CBaseAbilityCard.cs`
- `int Initiative { get; private set; }` (`CBaseAbilityCard.cs:9`) — **the initiative value**
- `string ClassID`, `ClassModel` (character model lookup).

### CAbilityCard — `ScenarioRuleLibrary/ScenarioRuleLibrary/CAbilityCard.cs`
The player ability card. Key members:
- `CAction TopAction`, `CAction BottomAction` — **the two card halves** (`CAbilityCard.cs:26,32`)
- `CAction DefaultAttackAction`, `CAction DefaultMoveAction` — the "Attack 2"/"Move 2" fallbacks printed on every card (`CAbilityCard.cs:28,30`)
- `CAction SelectedAction`, `LastSelectedAction`; `SetSelectedAction(CAction)` (`CAbilityCard.cs:89`)
- `int Level`, `bool SupplyCard`, `int CardInstanceID` (unique per copy; `ID + n*10000`, `CAbilityCard.cs:69-81`) — **CardInstanceID is the canonical handle used by all proxy/network APIs**
- `AbilityCardYMLData GetAbilityCardYML` — back-pointer to authoring data
- `GetActionForType(ActionType)` (`CAbilityCard.cs:111`), `GetTopActionAbilities()/GetBottomActionAbilities()` (`CAbilityCard.cs:161,166`), `Copy()` (`CAbilityCard.cs:84`).

`CAction` holds `Abilities` (list of `CAbility` subclasses: attack, move, heal, summon…), `Augmentations`, `Infusions`, `CardPile` (where the card goes after this half is played: Discarded vs Lost).

### Piles / hand storage — `ScenarioRuleLibrary/ScenarioRuleLibrary/CCharacterClass.cs`
Per-character-class card containers (fields at `CCharacterClass.cs:17-27`, properties `:85-104`):
- `List<CAbilityCard> HandAbilityCards` — current hand in scenario
- `List<CAbilityCard> RoundAbilityCards` — the (up to) 2 cards picked this round
- `List<CAbilityCard> DiscardedAbilityCards`, `LostAbilityCards` (burned), `PermanentlyLostAbilityCards`
- `List<CBaseCard> ActivatedCards` / `ActivatedAbilityCards` — **active/persistent cards in play**
- `List<CAbilityCard> SelectedAbilityCards` (`:77`) — the deck chosen for the scenario (out-of-scenario "hand"); `UnselectedAbilityCards`, `m_AbilityCardsPool` = full class pool
- `CAbilityCard InitiativeAbilityCard` / `SubInitiativeAbilityCard` / `ExtraTurnInitiativeAbilityCard` (`:220-224`) with setters `SetInitiativeAbilityCard(CAbilityCard)` (`:374`), `SetSubInitiativeAbilityCard` (`:379`)
- `bool LongRest { get; set; }` (`:156`), `HasLongRested`, `ImprovedShortRest`, `HasShortRested`, `ShortRestCardBurned`
- `void MoveAbilityCard(CAbilityCard abilityCard, List<CAbilityCard> from, List<CAbilityCard> to, string fromName, string toName, bool sendNetworkSelectedCardsWhenDone = false)` (`:273`) — the low-level pile mover (updates `CurrentCardPile`)
- `GetStartRoundCardState()` (`:1711`) → `StartRoundCardState` snapshot (round/hand/discard/lost/perma/activated instance IDs + initiative IDs + rest flags), used for save/undo/MP replication.

### Registries
- `CharacterClassManager.AllAbilityCards` / `AllAbilityCardInstances` (`ScenarioRuleLibrary/ScenarioRuleLibrary/CharacterClassManager.cs:23-25`) — global lookup, `CardInstanceID → CAbilityCard` resolution used everywhere.
- Live actors: `ScenarioManager.Scenario.PlayerActors` → `CPlayerActor.CharacterClass` → piles above.

### Authoring data (YML)
- `AbilityCardYMLData` (`ScenarioRuleLibrary/ScenarioRuleLibrary.YML/AbilityCardYMLData.cs`): `Name, CharacterID, ID, Level, Initiative`, `TopActionCardData/BottomActionCardData (CAction)`, `TopDiscardType/BottomDiscardType` (Discarded/Lost/PermanentlyLost), **`TopActionFullLayout` / `BottomActionFullLayout` (`CardLayout`)** — the declarative visual layout of each half (`AbilityCardYMLData.cs:39-61`).
- `CardLayout`/`CardLayoutGroup`/`CardLayoutRow` (`ScenarioRuleLibrary/ScenarioRuleLibrary/CardLayout.cs`) — row/column tree with text + icon tokens parsed from YML, consumed by the UI's `CreateLayout`.

---

## 3. Hand Management & Card Rendering (GH.Runtime)

### Managers
- **`CardsHandManager`** (`GH.Runtime/CardsHandManager.cs`, singleton `CardsHandManager.Instance`): owns one `CardsHandUI` per player (`public List<CardsHandUI> cardHandsUI`, `:67`). Key API:
  - `AddPlayer(CPlayerActor)` (`:466`) / `AddPlayerCoroutine` (`:508`) — builds a hand
  - `Show(CPlayerActor, CardHandMode mode, CardPileType filterType, List<CardPileType> selectableCardTypes, int maxCardsSelected, …)` (`:734`, overloads `:739,813`), `ShowCoroutine(...)` (`:838`), `Hide(CPlayerActor)` (`:899`)
  - `GetHand(int actorID)` (`:570`) / `GetHand(CPlayerActor)` (`:583`) / `GetActiveHand()` (`:595`), `SwitchHand` (`:613`)
  - `ShowLongRestConfirmation(CPlayerActor, Action<bool>)` (`:710`), `DiscardActiveAbilityCard(...)` (`:1007`)
  - `cardsActionController` field → `CardsActionControlller`.
- **`CardsHandUI`** (`GH.Runtime/CardsHandUI.cs`, one per player): `List<AbilityCardUI> cardsUI`, `selectedCardsUI` (`:128-130`); `Init(CPlayerActor, Transform, CardsActionControlller)` (`:435`); spawns cards in `SpawnCards(...)` coroutine (`:1716`) via `ObjectPool.SpawnCard(cardID, ECardType.Ability, holder)`; a **long-rest pseudo-card with CardID -1** is spawned into every hand (`:1798`), plus a `ShortRest` button widget (`:1809`).
- `UpdateView(CardHandMode mode, CardPileType filterType, List<CardPileType> selectableCardTypes, int maxCardsSelected, …)` (`:567,572`) drives which pile is shown/selectable. Modes: `enum CardHandMode { CardsSelection, ActionSelection, LoseCard, DeckSelection, Preview, RecoverLostCard, RecoverDiscardedCard, IncreaseCardLimit, DiscardCard }` (`GH.Runtime/CardHandMode.cs`); pile filter `enum CardPileType { Discarded, Round, Hand, Active, Lost, Permalost, Any, Unselected, ExtraTurn, None }` (`GH.Runtime/CardPileType.cs`).

### The card widget stack
- **`AbilityCardUI`** (`GH.Runtime/AbilityCardUI.cs`) — one instance per card GameObject; composed of:
  - **`MiniAbilityCard`** (`GH.Runtime/MiniAbilityCard.cs`) — compact hand row: `TextMeshProUGUI initiativeText/titleText`, `Image iconImage/backgroundImage/glow` (`MiniAbilityCard.cs:24-45`).
  - **`FullAbilityCard`** (`GH.Runtime/FullAbilityCard.cs`) — the full card face: `initiativeText/titleText/levelText (TMP)`, `headerImage (Image)`, `topActionButton`/`bottomActionButton` (**`FullAbilityCardAction`**), `DefaultMoveButton`/`DefaultAttackButton`, a per-card `public Canvas Canvas` (`FullAbilityCard.cs:21-96`).
  - `AbilityCardUI.Init(CAbilityCard, CPlayerActor, CardPileType, onSelected, onDeselected, onUnableToSelect, onHover, Action<FullAbilityCard, CBaseCard.ActionType> onActionCallback, Action onActionComplete, …)` (`AbilityCardUI.cs:474`).
- **`FullAbilityCardAction`** (`GH.Runtime/FullAbilityCardAction.cs`) — one card half: `Button actionButton`, `Button defaultActionButton`, consume/infuse buttons, enhancement slots; `enum CardHalf { NA, Top, Bottom }`.

### How the face is built (important for VR)
Cards are **100% uGUI (Canvas + Image + TextMeshProUGUI), generated procedurally — there is no single pre-baked card sprite**:
1. `PersistentData.CreateAbilityCard1(AbilityCardYMLData, out GameObject, out AbilityCardUI)` (`GH.Runtime/PersistentData.cs:307`): instantiates the prefab `AssetBundleManager.Instance.LoadAssetFromBundle<GameObject>("misc_gui", "AbilityCard" | "AbilityCard_gamepad" | "LongRest card", "gui")` (`PersistentData.cs:321`), then `card.MakeAbilityCard(cardData, …)`.
2. `AbilityCardUI.MakeAbilityCard(AbilityCardYMLData, …)` (`AbilityCardUI.cs:1298`) → `miniAbilityCard.MakeMiniCard(...)` + `fullAbilityCard.MakeFullCard(...)`.
3. `FullAbilityCard.MakeFullCard` (`FullAbilityCard.cs:456`) sets initiative/level text and calls `topActionButton.MakeAbilityAction(cardData.TopActionFullLayout.ParentGroup, …)` / bottom equivalent — which drives **`CreateLayout`** (`GH.Runtime/CreateLayout.cs`, 2025 lines): walks the YML `CardLayoutGroup` tree and instantiates rows/columns of TMP text and `Image` icons loaded from `Resources` under `"AbilityCard/"` (consume buttons, infuse elements, XP markers, summon boxes, AoE hex grids, enhancement slots) (`CreateLayout.cs:60-120`).
4. Class-specific frame art: `AbilityCardUISkin` (`GH.Runtime/AbilityCardUISkin.cs`) — sprites for title plate, top/bottom action backgrounds in regular/highlight/selected/disabled states, obtained via `UIInfoTools.Instance.GetCardSkin(playerName)` (`AbilityCardUI.cs:728-737`).
5. Instances are pooled: `ObjectPool.SpawnCard/RecycleCard/CreatePooledAbilityCard` (`GH.Runtime/ObjectPool.cs:378-470`).

**VR texturing strategy**: since each card is a self-contained GameObject with its own `Canvas` (`FullAbilityCard.Canvas`, sorting toggled via `AbilityCardUI.ToggleFullCardCanvasSorting`), the cheapest robust approach is to spawn a card via `ObjectPool.SpawnCard(cardID, ECardType.Ability, parent)`, set the Canvas to world-space (or point a dedicated camera at it) and **blit to a RenderTexture per card** for the 3D card mesh — or simply re-parent the live canvas onto a world-space VR panel. There is no atlas of finished card faces to steal; the face only exists as a live uGUI hierarchy.

---

## 4. Card Selection Flow (round start: pick 2 cards or rest)

### Sequence of calls (single player)
1. **Engine → UI**: `CPhaseSelectAbilityCardsOrLongRest.OnNextStep()` sends `CPlayerToSelectAbilityCardsOrLongRest_MessageData` (`ScenarioRuleLibrary/ScenarioRuleLibrary/CPhaseSelectAbilityCardsOrLongRest.cs:10-14`).
2. `Choreographer.ProcessMessage` `case CMessageData.MessageType.PlayerToSelectAbilityCardsOrLongRest:` (`GH.Runtime/Choreographer.cs:3567`) → `CardsHandManager.Instance.ShowCoroutine(CardHandMode.CardsSelection, CardPileType.Any, {Hand, Active, Round}, maxCardsSelected: 2, …)` (`Choreographer.cs:3602-3607`).
3. **Card click**: uGUI Button → `AbilityCardUI.OnClick()` (`AbilityCardUI.cs:324`, guard logic `:329-349`) → `ToggleSelect(!IsSelected, highlight: true)` (`AbilityCardUI.cs:1110`) → selected-callback → **`CardsHandUI.OnCardSelected(AbilityCardUI cardUI, bool networkAction = true)`** (`CardsHandUI.cs:1927`).
4. Inside `OnCardSelected` (CardsSelection mode, `CardsHandUI.cs:2005-2025`):
   - `ScenarioRuleClient.MoveAbilityCard(playerActor.CharacterClass, cardUI.AbilityCard, HandAbilityCards, RoundAbilityCards, "HandAbilityCards", "RoundAbilityCards", networkAction)` (`:2015`) — enqueues to the SRL thread, then **spin-waits up to 1000 ms** for `ScenarioRuleClient.s_SRLLastProcessedMessageID` to catch up (`:2016-2020`). (Deselect does the reverse move, `CardsHandUI.OnCardDeselected`, `:2250`.)
   - `InitiativeTrack.Instance.CheckRoundAbilityCardsOrLongRestSelected()` (`:2022`) — enables the ready button when every `CPlayerActor.IsCardSelectionReady()` (extension, `GH.Runtime/CPlayerActorExtensions.cs:5-14`: needs `RoundAbilityCards.Count == 2` or `LongRest`).
   - `UpdateInitiative()` (`CardsHandUI.cs:635-646`): `SetInitiativeAbilityCard(selectedCardsUI[count-1].AbilityCard)` and `SetSubInitiativeAbilityCard(selectedCardsUI[0].AbilityCard)`. Note `selectedCardsUI.Insert(0, cardUI)` on each select (`:1965`) ⇒ **the FIRST card picked is the initiative (leading) card** by default.
5. **Choosing which card gives initiative**: click the initiative badge — `AbilityCardUI` initiative button handler (`AbilityCardUI.cs:837-849`) or `InitiativeTrackPlayerAvatar.SwapInitiative()` (`GH.Runtime/InitiativeTrackPlayerAvatar.cs:73-96`). Both swap `InitiativeAbilityCard`/`SubInitiativeAbilityCard`, call `RoundAbilityCards.Reverse()`, `InitiativeTrack.Instance.UpdateActors()` and `CardsHandUI.NetworkSelectedRoundCards()`.
6. **Long rest selection**: the long-rest pseudo-card (CardID −1, `IsLongRest`) click path in `OnCardSelected` (`CardsHandUI.cs:1943-1959`): deselects everything else, sets `playerActor.CharacterClass.LongRest = true`, `UpdateInitiative()` (long rest = initiative 99), `OnSelectedCardsNumberChanged(2)`.
7. **Confirm**: the round-ready button — `ReadyButton.OnClickInternal(bool networkActionIfOnline = true)` (`GH.Runtime/ReadyButton.cs:245`); with empty action queue and `EButtonState >= EREADYBUTTONCONTINUE` it invokes **`ScenarioRuleClient.StepComplete()`** (`ReadyButton.cs:317-323`; engine side `ScenarioRuleClient.cs:917`). The SRL phase then ends: `CPhaseSelectAbilityCardsOrLongRest.OnEndPhase()` emits `CPlayersHaveSelectedAbilityCardsOrLongRest_MessageData` (`CPhaseSelectAbilityCardsOrLongRest.cs:21-25`), handled at `Choreographer.cs:3618`.
8. **MP replication** (also fires in SP): `CardsHandUI.NetworkSelectedRoundCards()` (`CardsHandUI.cs:2685`) — normalizes order so `RoundAbilityCards[0]` = initiative card, then `ScenarioRuleClient.GetAndReplicateStartRoundDeckState(playerActor, 32)` (`ScenarioRuleClient.cs:1053`), which snapshots `StartRoundCardState` (`ScenarioRuleLibrary/ScenarioRuleLibrary/StartRoundCardState.cs`).

### Programmatic selection helpers (already exist — perfect for VR)
- `CardsHandUI.SelectCard(CAbilityCard card)` / `UnselectCard(CAbilityCard card)` (`CardsHandUI.cs:2604/2619`) — finds the `AbilityCardUI` and calls `OnClick(ignoreHiglight: true)`; full pipeline incl. engine + network.
- `CardsHandUI.ProxySelectCard(int cardInstanceID, bool isHandUnderMyControl = true)` (`:2714`) — used by multiplayer proxies.
- `CardsHandUI.DeselectAllCards(bool networkAction = true, ...)` (`:2146`), `IsLongRestSelected()` (`:2662`), `IsCardSelected(int cardID)` (`:2676`).

---

## 5. During a Turn: choosing the card half; rests

### Top/bottom action selection
1. Engine: `StartTurn` → `ActionSelection` phase → `CMessageData.MessageType.ActionSelection` handled at `Choreographer.cs:3915`; hand is shown in action mode at `Choreographer.cs:4062`: `CardsHandManager.Instance.Show(playerActor, CardHandMode.ActionSelection, CardPileType.Round, …)` — the two round cards render as full cards.
2. State machine **`CardsActionControlller`** (`GH.Runtime/CardsActionControlller.cs`, note triple-l typo; singleton `s_Instance:26`): `enum Phase { …Select1stCard, Pick1stTarget, Select2ndCard, Pick2ndTarget… }`; `Init(FullAbilityCard topCard, FullAbilityCard bottomCard, …)` (`:91`, wired from `CardsHandUI.SetMode` at `CardsHandUI.cs:1551-1560`); `OnCardClicked(FullAbilityCard, CBaseCard.ActionType)` (`:453`); `OnActionFinished()` (`:474`); `GetAction(CAbilityCard)` (`:207`).
3. **The click**: top/bottom half button (prefab-wired UnityEvent) → `FullAbilityCard.OnAbilityClick(bool isTopAbility)` (`FullAbilityCard.cs:511`) → **`FullAbilityCard.OnAbilityClick(CBaseCard.ActionType abilityType, bool isProxyAction, bool checkValid = true)`** (`FullAbilityCard.cs:606`). This method is the heart of half-selection; after guards it:
   - disables the *other* mode of the same half (top ⇄ default-attack, bottom ⇄ default-move mapping, `:649-672`),
   - `onActionCallback(this, abilityType)` → `CardsHandUI.OnCardAction` (`CardsHandUI.cs:1852-1862`) → `CardsActionControlller.OnCardClicked`,
   - **`GameState.PlayerSelectedAbilityCardAction(abilityCard, abilityType)`** (`FullAbilityCard.cs:687`) — engine commit (see below),
   - **`Choreographer.s_Choreographer.Pass()`** (`FullAbilityCard.cs:688`) → `ScenarioRuleClient.Pass()` (`Choreographer.cs:1795-1809`, engine `ScenarioRuleClient.cs:993`) — kicks the SRL to start executing the chosen action,
   - if online, replicates via `Synchronizer.SendGameAction(GameActionType.SelectCardAbility, …, cardInstanceID, actionType, …)` (`FullAbilityCard.cs:689-698`).
4. Engine commit: `GameState.PlayerSelectedAbilityCardAction(CAbilityCard roundAbilityCard, CBaseCard.ActionType actionType)` (`ScenarioRuleLibrary/ScenarioRuleLibrary/GameState.cs:794-815`): copies the `CAction`, sets `CurrentAction = new CurrentActorAction(action, card)`, `card.SetSelectedAction(action)`, `RoundAbilityCardselected`/`RoundAbilityCardActionType` statics.
5. When the action resolves, `Choreographer` calls back `onCharacterAbilityComplete` → `FullAbilityCard.OnActionMade` → `onActionCompleteCallback` → `CardsHandUI.OnActionCompleted` (`CardsHandUI.cs:1835-1850`) → `CardsActionControlller.OnActionFinished()` → phase advances to `Select2ndCard` or `Finish()`.
6. **Proxy entry point** (used by MP, ideal for VR): `CardsHandUI.ProxySelectCardAction(int cardInstanceID, CBaseCard.ActionType cardActionType)` (`CardsHandUI.cs:2725`) → `fullAbilityCard.OnAbilityClick(cardActionType, isProxyAction: true, checkValid: false)`.

### Short rest
- UI widget `ShortRest` (`GH.Runtime/ShortRest.cs`), spawned into the hand (`CardsHandUI.cs:1809`). Click → `MouseClick()` (`ShortRest.cs:202`) → `Select(true)` → yes/no dialog (`ShortRest.cs:100-119`) → confirm invokes the injected `shortRestAction` = **`CardsHandUI.PerformShortRest(CPlayerActor)`** (`CardsHandUI.cs:719`): picks a random discarded card via `ScenarioManager.CurrentScenarioState.ScenarioRNG` (`:752`), offers redraw for −1 HP, then `PerformFinalShortRest` (`:877`) → **`FinalizeShortRest`** (`:941`) → **`ScenarioRuleClient.ShortRestPlayer(playerActor, cardUI.AbilityCard, loseHp, !networkActionIfOnline, fromStateUpdate)`** (`:957`; engine API `ScenarioRuleClient.cs:1043`).
- MP proxy: `CardsHandUI.ProxyShortRest(StartRoundCardsToken, bool)` (`:2798`).

### Long rest (execution during the player's turn)
- Selected at card-selection time (long-rest card sets `CharacterClass.LongRest = true`). When the turn comes, the UI asks which card to burn: `CardsHandManager.Show(playerActor, CardHandMode.LoseCard, …, CardPileType.Discarded, 1, …)`; chosen via `CardsHandUI.OnLoseCardClick` (`:2295`) → `HandleLongRest(CAbilityCard cardToBurn)` (`:2423`). Engine effect (`GameState.cs` "LongRestPlayer" region around `:2540-2586`): burn card, return discards to hand, `Healed(2)`, refresh spent items, then emits `CPlayerLongRested_MessageData` (`GameState.cs:2581`).
- Improved short rest variant handled by `HandleImprovedShortRest` (`CardsHandUI.cs:2437`) and `ReadyButton.EButtonState.EREADYBUTTONIMPROVEDSHORTREST` queueing (`CardsHandUI.cs:728-748`).
- MP proxy: `CardsHandUI.ProxyLongRest(int burnedCardID)` (`:2819`).

### View-all / full-hand preview
- `CardsHandUI.ToggleFullCardsPreview(bool active, bool openByKey)` (`CardsHandUI.cs:312`) re-parents each `FullAbilityCard` into `Singleton<FullCardHandViewer>.Instance.CardContainer` (`GH.Runtime/FullCardHandViewer.cs`) — proof the full-card canvases can be re-parented at runtime without breaking (good news for VR world-space re-hosting).

---

## 6. Harmony Patch Candidates (the UI ⇄ engine seam)

The clean strategy: **postfix/observe the Choreographer message pump to know when input is required, then drive the existing public commit methods from VR** instead of clicking uGUI. Almost everything is `public`.

| # | Class | Method (signature) | File | Why patch / call |
|---|-------|--------------------|------|------------------|
| 1 | `Choreographer` | `private void ProcessMessage(CMessageData message)` | `GH.Runtime/Choreographer.cs:3395` | **Postfix** = the single place every engine event reaches the main thread (card selection requested, action selection, rests, turn start/end). Drive VR state machine from `message.m_Type`. |
| 2 | `CardsHandManager` | `public void Show(CPlayerActor, CardHandMode, CardPileType, List<CardPileType>, int maxCardsSelected, bool, bool, bool, CardsHandUI.CardActionsCommand, bool, bool, Action<AbilityCardUI>, Func<CAbilityCard,bool>)` | `GH.Runtime/CardsHandManager.cs:739` | Postfix to know exactly which hand/mode/max-count the 2D UI would show; prefix+skip to suppress 2D panel in VR. |
| 3 | `CardsHandUI` | `public void SelectCard(CAbilityCard card)` / `public void UnselectCard(CAbilityCard card)` | `GH.Runtime/CardsHandUI.cs:2604/2619` | **Call directly from VR grab/click** — full pipeline (engine move Hand→Round, initiative update, ready-gating, network). |
| 4 | `CardsHandUI` | `public void ProxySelectCardAction(int cardInstanceID, CBaseCard.ActionType cardActionType)` | `GH.Runtime/CardsHandUI.cs:2725` | **Call directly**: choose top/bottom (or default move/attack) half from VR; bypasses raycast/hover guards (`isProxyAction: true`). |
| 5 | `FullAbilityCard` | `public void OnAbilityClick(CBaseCard.ActionType abilityType, bool isProxyAction, bool checkValid = true)` | `GH.Runtime/FullAbilityCard.cs:606` | The real half-commit (engine + pass + network). Patch to intercept/mirror; call for fine control. |
| 6 | `GameState` | `public static void PlayerSelectedAbilityCardAction(CAbilityCard roundAbilityCard, CBaseCard.ActionType actionType)` | `ScenarioRuleLibrary/ScenarioRuleLibrary/GameState.cs:794` | Lowest-level engine commit of a card half. Postfix to observe; avoid calling raw (skips UI bookkeeping in #5). |
| 7 | `ScenarioRuleClient` | `public static uint MoveAbilityCard(CCharacterClass, CAbilityCard, List<CAbilityCard> from, List<CAbilityCard> to, string fromName, string toName, bool networkAction)` | `ScenarioRuleLibrary/ScenarioRuleLibrary/ScenarioRuleClient.cs:988` | Engine-side card pile move (selection/deselection). Observe for VR hand mirroring. |
| 8 | `ScenarioRuleClient` | `public static uint StepComplete(bool processImmediately = false, bool fromSRL = false)` | `ScenarioRuleClient.cs:917` | The "confirm/ready" commit. Call from a VR ready gesture (must satisfy `IsCardSelectionReady()` first). |
| 9 | `ReadyButton` | `public void OnClickInternal(bool networkActionIfOnline = true)` | `GH.Runtime/ReadyButton.cs:245` | Safer confirm entry: handles MP `ConfirmAction`, queued alternative actions (extra-turn/recover confirms), button state. Prefer calling this over #8. |
| 10 | `CardsHandUI` | `private void OnCardSelected(AbilityCardUI cardUI, bool networkAction = true)` / `OnCardDeselected(...)` | `GH.Runtime/CardsHandUI.cs:1927/2190` | Postfix to sync VR card highlighting/initiative marker with 2D logic. |
| 11 | `InitiativeTrackPlayerAvatar` | `public void SwapInitiative()` | `GH.Runtime/InitiativeTrackPlayerAvatar.cs:73` | Call from VR to toggle which selected card is the initiative card. |
| 12 | `CardsHandUI` | `public void PerformShortRest(CPlayerActor playerActor)` | `GH.Runtime/CardsHandUI.cs:719` | Call from VR "short rest" interaction (handles RNG card pick + redraw dialog flow). |
| 13 | `ScenarioRuleClient` | `public static uint ShortRestPlayer(CPlayerActor, CAbilityCard discardedCard, bool loseHealth, bool updateScenarioRNG, bool fromStateUpdate = false, bool processImmediately = false)` | `ScenarioRuleClient.cs:1043` | Raw engine short-rest if bypassing the 2D dialog entirely. |
| 14 | `CardsActionControlller` | `public void OnCardClicked(FullAbilityCard card, CBaseCard.ActionType actionType)` / `OnActionFinished()` | `GH.Runtime/CardsActionControlller.cs:453/474` | Observe the 1st/2nd-action phase machine to sequence VR prompts ("now pick the second half"). |
| 15 | `ScenarioRuleClient` | `public static uint Pass(bool processImmediately = false)` | `ScenarioRuleClient.cs:993` | Skip/advance current step from VR (also used internally after half-selection). |
| 16 | `AbilityCardUI` | `public void ToggleSelect(bool active, bool highlight = false, bool networkAction = true, bool isHandUnderMyControl = true)` | `GH.Runtime/AbilityCardUI.cs:1110` | Alternative per-card toggle (validity checks inside: `CanSelectInScenario`). |
| 17 | `PersistentData` | `public static bool CreateAbilityCard1(AbilityCardYMLData, out GameObject, out AbilityCardUI)` | `GH.Runtime/PersistentData.cs:307` | Reuse to fabricate standalone card GameObjects for VR RenderTexture capture. |

**Warnings for callers**
- `CardsHandUI.OnCardSelected/OnCardDeselected` **block the main thread up to 1 s** (`Thread.Sleep(10)` loop, `CardsHandUI.cs:2016-2020,2251-2255`) waiting for the SRL thread; expect frame hitches at selection commit (worse in VR — consider prefixing to make it async).
- All `ScenarioRuleClient.*` calls just enqueue; results arrive later via the Choreographer pump. Never call them from a non-main thread with `processImmediately: true`.
- Anything with `networkAction`/`Synchronizer.SendGameAction` matters only when `FFSNetwork.IsOnline`; keep default values for SP.
- Many small helpers (e.g. `PhaseManager` in GH.Runtime, `StartRoundCardState`, `GameActionType`) decompiled with mangled names in a few files (`public n`) — re-verify names against the actual assembly with dnSpy before writing patches for those specific types (the SRL `ScenarioRuleLibrary/ScenarioRuleLibrary/PhaseManager.cs` is intact: `PhaseManager.PhaseType`, `CurrentPhase`).

---

## 7. Event / Messaging System

There is **no UniRx** (zero hits in the codebase). Three mechanisms:

1. **SRL → UI message pump (primary)**: `ScenarioRuleClient.MessageHandlerCallback` delegate (`ScenarioRuleClient.cs:335`, registered `Choreographer.cs:666`) carrying `CMessageData` with `MessageType` enum of ~200 values (`ScenarioRuleLibrary/ScenarioRuleLibrary/CMessageData.cs:5`). Card-relevant types: `PlayerToSelectAbilityCardsOrLongRest`, `PlayersHaveSelectedAbilityCardsOrLongRest`, `ActionSelection`, `ActionSelectionPhaseStart`, `StartTurn`, `PlayerLongRested`, `PlayerShortRested`, `RecoverLostCards`, `RecoverDiscardedCards`, `SelectRecoverCards`, `MoveSelectedCards`, `EndTurn`, `EndRound`, `NextRound`. Payload messages live in SRL, e.g. `CPlayerToSelectAbilityCardsOrLongRest_MessageData`, `CPlayerLongRested_MessageData` (ctor `.../CPlayerLongRested_MessageData.cs:9`), `CSupplyCardsGiven_MessageData`. Processed single-threaded in `Choreographer.Update` (8 ms budget/frame, `Choreographer.cs:2358`).
2. **UI → SRL queue**: `ScenarioRuleClient.AddSRLQueueMessage(CSRLMessage, bool processImmediately)` (`ScenarioRuleClient.cs:896`) with typed wrappers (`CSRLMoveAbilityCardMessage` `:89`, etc.). Returns a message ID; `s_SRLLastProcessedMessageID` lets callers await processing.
3. **C# static events / UnityEvents on UI classes** (useful VR hooks without Harmony):
   - `AbilityCardUI.CardHoveringStateChanged` / `CardSelectionStateChanged` (`static event Action<AbilityCardUI,bool>`, `AbilityCardUI.cs:262-264`)
   - `FullAbilityCard.FullCardHoveringStateChanged` (`static event Action<bool>`, `FullAbilityCard.cs:115`), `OnEnterForView` (`:117`), `onFullCardSelected` (UnityEvent, `:83`)
   - `ShortRest.OnSelectShortRest` / `OnUnselectShortRest` (`static event Action<ShortRest,bool>`, `ShortRest.cs:86-88`)
   - `CardsHandUI.OnShow` / `OnHide` (UnityEvents, `CardsHandUI.cs:190-192`)
   - `UIEventManager.LogUIEvent(new UIEvent(EUIEventType.AbilityCardSelected / CardTopHalfSelected / CardBottomHalfSelected / ShortRestPressed / ConfirmButtonPressed, …))` (`GH.Runtime/UIEventManager.cs:23`) — telemetry stream that doubles as a convenient, already-instrumented postfix target enumerating every card interaction.
4. **Multiplayer game-actions** (relevant if VR must stay MP-compatible): `Synchronizer.SendGameAction(GameActionType, …)` with `GameActionType.SelectRoundCards, SelectCardAbility, SelectExtraTurnCards, ConfirmAction, ShortRest, LongRest, BurnAvailableCard` (`GH.Runtime/FFSNet/GameActionType.cs`); incoming proxies routed through `CardsHandManager` (e.g. `hand.ProxySelectCardAction(...)` at `CardsHandManager.cs:1368`).

---

## 8. Open Questions / Risks

1. **Main-thread spin-wait on selection** (`CardsHandUI.cs:2016`): a 10–1000 ms stall per card select/deselect is tolerable on flat-screen, disastrous in VR (dropped frames). Options: prefix-patch `OnCardSelected` to enqueue + poll in a coroutine, or call `ScenarioRuleClient.MoveAbilityCard` ourselves and do the initiative/ready bookkeeping after observing the SRL ack.
2. **Decompiler-mangled names**: a handful of GH.Runtime types decompiled as `public n` (e.g. `GH.Runtime/FFSNet/GameActionType.cs` header, `ReadyButton.cs` class line, `StartRoundCardState.cs` in SRL dump). The member lists look correct, but confirm exact type/method names in the shipping assemblies before finalizing Harmony `TargetMethod`s.
3. **Prefab dependence**: card faces need the `misc_gui` bundle prefabs (`AbilityCard`, `AbilityCard_gamepad`, `LongRest card`, `ItemCard`, `MonsterRoundCard`) and `Resources/AbilityCard/*` icons. If the VR mod renders cards before the game loads these (main menu), it must trigger `ObjectPool.CreatePooledAbilityCard` lazily like the game does.
4. **Two UI skins (mouse vs gamepad)**: `InputManager.GamePadInUse` selects different prefabs and navigation paths everywhere (`PersistentData.cs:319`, `CardsHandUI` navigation groups). VR should probably impersonate the *mouse* path (simpler raycast-driven flow) and keep `GamePadInUse == false`.
5. **State-machine coupling**: `Singleton<UINavigation>.Instance.StateMachine.Enter(ScenarioStateTag.CardSelection / Burn / RoundStart)` calls are sprinkled through the flow (`CardsHandUI.cs:2054,2073`, `Choreographer.cs:3582`). If we suppress 2D panels wholesale, verify nothing else depends on those state transitions (input routing, hotkeys, popups).
6. **Extra-turn cards** (`SelectingCardsForExtraTurnOfType`, `ExtraTurnCardsSelectedInCardSelection`, `CExtraTurnCardsSelected`): a parallel selection flow with its own initiative rules (`CardsHandUI.cs:1972-2003`) — punt to 2D UI initially, but the same proxy APIs cover it (`GameActionType.SelectExtraTurnCards`).
7. **Burn-to-prevent-damage & recover/lose card dialogs** (`CardHandMode.LoseCard/DiscardCard/Recover*`, `TakeDamagePanel`, dialog popups in `OnCardSelected` `:2027-2122`): each is another modal card-pick the VR UI must eventually re-implement; all funnel back through `OnLoseCardClick`/`OnRecoverCardClick` → `Choreographer.s_Choreographer.readyButton.AlternativeAction(...)`.
8. **Enhancements/consumes on card faces**: elements like consume buttons and infusion pickers on the card halves are interactive during action selection (`FullAbilityCardAction.consumeButtons`, `CardsActionControlller.RefreshConsumeBar` `:360-414`). A static RenderTexture snapshot loses that interactivity — VR needs either live world-space canvases or separate VR widgets for consumes.
9. **Save/undo snapshots** (`StartRoundCardState`, `ScenarioRuleClient.ProxySetStartRoundDeckState` `:1048`): if VR mutates piles directly instead of via `ScenarioRuleClient.MoveAbilityCard`, undo/restart-round and MP state compare (`CBaseCard.Compare`, mismatch codes 2801-2899) will desync. Always go through the client API.
