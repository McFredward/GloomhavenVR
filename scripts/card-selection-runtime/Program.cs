using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using GloomhavenVR.Cards;
using HarmonyLib;
using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using Script.GUI.SMNavigation;
using UnityEngine;

public static class InteractionProgram
{
    private static int checks;
    private static int moves;
    private static int errors;
    private static readonly string Owner = "ghvr.cardSelection680.boundaries";
    private static readonly List<UnityEngine.Object> objects = new();
    private static CardsHandManager manager = null!;
    private static PartyAdventureData adventure = null!;
    private static Thread? rules;
    private static void Check(bool value, string text) { checks++; if (!value) throw new Exception(text); }
    private static void Set(object target, string field, object? value)
        => AccessTools.Field(target is Type ? (Type)target : target.GetType(), field).SetValue(target is Type ? null : target, value);
    private static void Property(object target, string name, object? value)
    {
        for (Type? type = target is Type ? (Type)target : target.GetType(); type != null; type = type.BaseType)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property != null) { property.SetValue(target is Type ? null : target, value); return; }
        }
        throw new Exception("missing fixture property " + name);
    }
    private static T Data<T>()
    {
        T value = (T)FormatterServices.GetUninitializedObject(typeof(T));
        for (Type? type = typeof(T); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (field.FieldType.IsGenericType && new[] { typeof(List<>), typeof(Stack<>) }.Contains(field.FieldType.GetGenericTypeDefinition()))
                    field.SetValue(value, Activator.CreateInstance(field.FieldType));
        return value;
    }
    private static T Component<T>(string name) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform)); go.SetActive(false); objects.Add(go); return go.AddComponent<T>();
    }
    private static bool Ignore() => false;
    private static bool Offline(ref bool __result) { __result = false; return false; }
    private static bool Scenario(ref EGameState __result) { __result = EGameState.Scenario; return false; }
    private static bool Adventure(ref PartyAdventureData __result) { __result = adventure; return false; }
    private static void Patch(Harmony harmony, Type type, string method, string prefix = nameof(Ignore))
        => harmony.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(InteractionProgram), prefix));
    private static void Prepare()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        SharedLibrary.Client.SharedClient.SetDLLDebugHandler(message => {
            if (message.m_LogType == SharedLibrary.Logger.EDLLDebug.Error) Interlocked.Increment(ref errors);
        });
        var harmony = new Harmony(Owner);
        // A scenario-only fixture supplies the already loaded save and local session.
        // Card mode, native widget guards, callbacks, SRL queue and rule moves are real.
        harmony.Patch(AccessTools.PropertyGetter(typeof(FFSNetwork), "IsOnline"), prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(Offline)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(GlobalData), "CurrentGameState"), prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(Scenario)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(GlobalData), "CurrentAdventureData"), prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(Adventure)));
        // These overloads only change original pictures; selection state is read
        // from the real actor's native RoundAbilityCards, never a fixture boolean.
        foreach (var method in typeof(AbilityCardUI).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic).Where(m => m.Name == "SetDisplayMode"))
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(Ignore)));
        Patch(harmony, typeof(FullAbilityCard), "DisplaySelected");
        Patch(harmony, typeof(MiniAbilityCard), "ShowIndicatorNumber");
        Patch(harmony, typeof(CardsHandManager), "OnSelectedCardsNumberChanged");
        Patch(harmony, typeof(InitiativeTrack), "CheckRoundAbilityCardsOrLongRestSelected");
        Patch(harmony, typeof(InitiativeTrack), "UpdateActors");
        Patch(harmony, typeof(UIEventManager), "LogUIEvent");
        // This native UI method only publishes a backend start-round snapshot;
        // the actual native initiative swap/reverse/selected list still execute.
        Patch(harmony, typeof(CardsHandUI), "NetworkSelectedRoundCards");
        var nav = Component<UINavigation>("navigation");
        Set(typeof(Singleton<UINavigation>), "_instance", nav);
        Property(nav, "StateMachine", new NavigationStateMachine(null!));
        var save = Component<SaveData>("save-container"); SaveData.Instance = save; save.Global = new GlobalData();
        adventure = Data<PartyAdventureData>(); adventure.AdventureMapState = Data<CMapState>();
        Property(adventure.AdventureMapState, "MapParty", Data<CMapParty>());
        Property(adventure.AdventureMapState.MapParty, "SelectedCharactersArray", Array.Empty<CMapCharacter>());
        manager = Component<CardsHandManager>("native-manager"); Property(typeof(CardsHandManager), "Instance", manager); Set(manager, "isShown", true);
        Property(typeof(InitiativeTrack), "Instance", Component<InitiativeTrack>("native-initiative"));
        Set(typeof(ScenarioManager), "s_Scenario", Data<CScenario>());
        Set(typeof(PhaseManager), "s_CurrentPhase", new CPhaseSelectAbilityCardsOrLongRest());
        Set(typeof(CharacterClassManager), "s_Classes", new List<CCharacterClass>());
        Set(typeof(ScenarioRuleClient), "s_SRLYML", new CSRLYML { YMLMode = CSRLYML.EYMLMode.Global });
        SEventLogMessageHandler.Initialise();
        ScenarioRuleClient.SetMessageHandler((message, immediate) => {
            if (message is CPlayerFinishedMovingAbilityCard) Interlocked.Increment(ref moves);
            if (message is CSRLException_MessageData) Interlocked.Increment(ref errors);
        });
        AccessTools.Method(typeof(ScenarioRuleClient), "ResetQueue").Invoke(null, null);
        Set(typeof(ScenarioRuleClient), "s_CancellationTokenSource", new CancellationTokenSource());
        Set(typeof(ScenarioRuleClient), "s_IsAlive", true);
        // Start the original native consumer without starting an unrelated whole
        // scenario. No ScenarioRuleLibrary method is patched or reimplemented.
        rules = new Thread(() => AccessTools.Method(typeof(ScenarioRuleClient), "Work").Invoke(null, null));
        rules.Start();
        Application.logMessageReceived += Log;
    }
    private static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error) errors++;
    }
    private static CPlayerActor Actor(string? display, string? guid, string identity, string? classId = null)
    {
        var character = Data<CCharacterClass>(); Property(character, "ID", identity);
        var yml = Data<CharacterYMLData>(); Property(yml, "ID", classId ?? identity); Set(character, "m_CharacterYML", yml);
        // A native actor generated by the game has a GUID; missing GUID is an
        // explicit hostile lifecycle input, not a fabricated fallback identifier.
        var actor = new CPlayerActor(); Set(actor, "m_Class", character); actor.CharacterName = display;
        Property(actor, "ActorGuid", guid); actor.IsUnderMyControl = true;
        actor.TakingExtraTurnOfTypeStack = new(); actor.PendingExtraTurnOfTypeStack = new();
        character.ExtraTurnCardsSelectedInCardSelectionStack = new();
        ScenarioManager.Scenario.PlayerActors.Add(actor);
        CharacterClassManager.Classes.Add(character);
        Property(adventure.AdventureMapState.MapParty, "SelectedCharactersArray", adventure.AdventureMapState.MapParty.SelectedCharacters
            .Concat(new[] { MapCharacter(display ?? "native-unlabelled", character.CharacterID) }).ToArray());
        return actor;
    }
    private static CMapCharacter MapCharacter(string name, string classId)
    {
        var map = Data<CMapCharacter>(); Property(map, "CharacterID", classId); Property(map, "CharacterName", name); return map;
    }
    private static CardsHandUI Hand(CPlayerActor actor)
    {
        var hand = Component<CardsHandUI>("hand-" + actor.ActorGuid); Set(hand, "playerActor", actor);
        Property(hand, "currentMode", CardHandMode.CardsSelection); Set(hand, "maxCardsSelected", 2);
        Set(manager, "currentHand", hand); Set(hand, "cardsUI", new List<AbilityCardUI>());
        manager.cardHandsUI.Add(hand);
        return hand;
    }
    private static VRCard Card(CardsHandUI hand, int id)
    {
        var ability = new CAbilityCard(); Property(ability, "ID", id); Property(ability, "CardInstanceID", id + 1000);
        Property(ability, "ClassID", hand.PlayerActor.CharacterClass.CharacterID); Property(ability, "CardType", CBaseCard.ECardType.CharacterAbility);
        var yml = Data<AbilityCardYMLData>(); Property(yml, "ID", id); Property(yml, "Name", "fixture-card-" + id);
        if (!ScenarioRuleClient.SRLYML.AbilityCards.Any(c => c.ID == id)) ScenarioRuleClient.SRLYML.AbilityCards.Add(yml);
        var widget = Component<AbilityCardUI>("original-widget-" + id);
        widget.CardID = id; widget.CardInstanceID = id + 1000;
        Set(widget, "abilityCard", ability); Set(widget, "playerActor", hand.PlayerActor); Property(widget, "CharacterID", hand.PlayerActor.CharacterClass.CharacterID);
        Set(widget, "isValid", true); Set(widget, "isSelectable", true); Set(widget, "unselectedCardType", CardPileType.Hand);
        Set(widget, "fullAbilityCard", Component<FullAbilityCard>("native-full-" + id));
        Set(widget, "miniAbilityCard", Component<MiniAbilityCard>("native-mini-" + id));
        var audio = ScriptableObject.CreateInstance<AudioButtonProfile>(); objects.Add(audio); Set(widget, "interactionAudioProfile", audio);
        Set(widget, "onSelectedCallback", AccessTools.Method(typeof(CardsHandUI), "OnCardSelected").CreateDelegate(typeof(Action<AbilityCardUI, bool>), hand));
        Set(widget, "onDeselectedCallback", AccessTools.Method(typeof(CardsHandUI), "OnCardDeselected").CreateDelegate(typeof(Action<AbilityCardUI, bool>), hand));
        hand.cardsUI.Add(widget); hand.PlayerActor.CharacterClass.HandAbilityCards.Add(ability);
        return new VRCard { GameCard = widget };
    }
    private static int Id(VRCard card) => card.GameCard!.AbilityCard.ID;
    private static void Selected(CardsHandUI hand, int expected, string label)
    {
        Check(errors == 0, label + ": original native callbacks have no exceptions");
        Check(hand.PlayerActor.CharacterClass.RoundAbilityCards.Count == expected, label + ": original round pile follows intended selection");
        Check(hand.SelectedCards.Count == expected, label + ": native widget list matches original round pile");
        Check(hand.cardsUI.All(card => card.IsSelected == hand.PlayerActor.CharacterClass.RoundAbilityCards.Contains(card.AbilityCard)), label + ": native IsSelected reads real model");
        Check(hand.PlayerActor.CharacterClass.HandAbilityCards.Concat(hand.PlayerActor.CharacterClass.RoundAbilityCards).Distinct().Count()
            == hand.PlayerActor.CharacterClass.HandAbilityCards.Count + expected, label + ": no duplicate card exists between round and hand");
    }
    private static void NativeCycles(EGameMode mode, string? name, string? guid, bool gap, int index)
    {
        SaveData.Instance.Global.GameMode = mode;
        // The native Campaign-only sound-owner lookup uses the selected character
        // container, which may contain one instance of each class in this case.
        Property(adventure.AdventureMapState.MapParty, "SelectedCharactersArray", Array.Empty<CMapCharacter>());
        var actor = Actor(name, guid, "actor-" + index); var hand = Hand(actor);
        var driver = new CardsDriver(); var cards = Enumerable.Range(1, 4).Select(id => Card(hand, id)).ToArray();
        driver._fan.SetCards(cards.ToList()); driver.ApplyOrder();
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Set(manager, "currentHand", hand);
            for (int slot = 0; slot < 2; slot++) { driver._tray.Target = slot; driver.Release(cards[slot], hand); CardActionQueue.Drain(); }
            Selected(hand, 2, "both slots selected");
            // The production rebuild reapplies this exact ordering before it
            // presents the fan whose insertion gap the user can target.
            driver.ApplyOrder();
            int beforeMoves = moves;
            driver._tray.Target = -1;
            if (gap) driver.Gap(cards[0], 0);
            // This is the exact production normal-release block containing the
            // hardware exception, not a new substitute withdrawal implementation.
            try { driver.Release(cards[0], hand); }
            catch (ArgumentNullException error) { throw new Exception("nullable presentation identity must never abort native deselection", error); }
            Check(CardActionQueue.Count == 1, "take-back queues native deselection despite nullable presentation identity");
            Check(!driver._tray.ContainsCard(cards[0]), "withdrawal immediately empties its physical recess");
            Check(driver._tray.Occupant(1) == cards[1], "withdrawing left card leaves right physical card untouched");
            // Change the presented actor before the queued native call. The closure
            // must still refer to the hand and local order captured at release.
            var other = Actor("focus-" + index + "-" + cycle, "focus-guid-" + index + "-" + cycle, "other-" + index + "-" + cycle);
            var otherHand = Hand(other);
            var otherDriver = new CardsDriver();
            otherDriver._fan.SetCards(new[] { Card(otherHand, 7), Card(otherHand, 8) }.ToList()); otherDriver.ApplyOrder();
            var otherOrder = otherDriver._fanOrder.ToArray(); CardActionQueue.Drain();
            Selected(hand, 1, "focus change before withdrawal pump");
            Check(other.CharacterClass.RoundAbilityCards.Count == 0, "queued withdrawal never edits newly presented actor");
            Check(moves == beforeMoves + 1, "withdrawal executes one original SRL queued round-to-hand move");
            Check(otherDriver._fanOrder.SequenceEqual(otherOrder), "queued fan-gap completion cannot edit newly presented actor's local order");
            Set(manager, "currentHand", hand);
            Check(!cards[0].GameCard!.IsSelected && cards[1].GameCard!.IsSelected, "focus back cannot resurrect withdrawn native selection");
            Check(actor.CharacterClass.HandAbilityCards.Contains(cards[0].GameCard!.AbilityCard), "withdrawn card remains in actual native hand after focus return");
            if (gap && CardsDriver.Key(cards[0], out _, out _))
            {
                driver.ApplyOrder(); Check(driver._fan.Cards[0] == cards[0], "withdrawal restores the captured actor's requested first fan gap after focus return"
                    + " mode=" + mode + " index=" + index + " cycle=" + cycle + " order=" + string.Join(",", driver._fanOrder)
                    + " fan=" + string.Join(",", driver._fan.Cards.Select(Id)));
            }
            // Replacement must serialize the original unselect before select,
            // even while native CanSelectInScenario would reject a third card.
            driver._tray.Target = 1; driver.Release(cards[2], hand);
            Check(CardActionQueue.Count == 2, "occupied-slot replacement queues native unselect then select");
            CardActionQueue.Drain(); Selected(hand, 1, "occupied-slot replacement");
            Check(!cards[1].GameCard!.IsSelected && cards[2].GameCard!.IsSelected, "replacement changes native card identity without restoring displaced card");
            driver._tray.Target = -1; driver.Release(cards[2], hand); CardActionQueue.Drain(); Selected(hand, 0, "all cards withdrawn");
            Check(actor.CharacterClass.HandAbilityCards.Count == 4, "arbitrary repeated take-backs restore complete native hand");
        }
        // Native widget restrictions are kept: no fixture-side selection boolean
        // overrides either the game's card-click gate or its toggle predicate.
        Set(manager, "currentHand", hand); Set(cards[0].GameCard!, "_canToggleCard", (Func<bool>)(() => false));
        hand.SelectCard(cards[0].GameCard!.AbilityCard); Selected(hand, 0, "native locked toggle");
        Set(cards[0].GameCard!, "_canToggleCard", null);
        hand.SelectCard(cards[0].GameCard!.AbilityCard); hand.SelectCard(cards[1].GameCard!.AbilityCard);
        var committed = actor.CharacterClass.RoundAbilityCards.ToArray(); var initiative = actor.CharacterClass.InitiativeAbilityCard;
        // Phase and mode are supplied native lifecycle inputs, not a simulated
        // Ready-button click. Verify the real callback's post-selection fences.
        var actionPhase = Data<CPhaseActionSelection>(); Set(actionPhase, "m_PhaseType", CPhase.PhaseType.ActionSelection);
        Set(typeof(PhaseManager), "s_CurrentPhase", actionPhase);
        foreach (var frozenMode in new[] { CardHandMode.ActionSelection, CardHandMode.Preview })
        {
            Property(hand, "currentMode", frozenMode); hand.UnselectCard(cards[0].GameCard!.AbilityCard);
            Check(actor.CharacterClass.RoundAbilityCards.SequenceEqual(committed), "native action/preview callback cannot reopen a committed round");
            CardsGameApi.SwapInitiative(hand);
            Check(ReferenceEquals(actor.CharacterClass.InitiativeAbilityCard, initiative), "native action phase cannot swap already committed initiative");
        }
        Property(hand, "currentMode", CardHandMode.CardsSelection);
        Set(typeof(PhaseManager), "s_CurrentPhase", new CPhaseSelectAbilityCardsOrLongRest());
        hand.UnselectCard(cards[0].GameCard!.AbilityCard); hand.UnselectCard(cards[1].GameCard!.AbilityCard);
        Selected(hand, 0, "later native editable selection");
    }
    private static void OrderIsolation()
    {
        SaveData.Instance.Global.GameMode = EGameMode.Guildmaster;
        var one = Actor(null, "same-key", "order-one", "BruteID"); var two = Actor("", "other-key", "order-two", "BruteID");
        var named = Actor("same-key", "named-guid", "order-three", "BruteID");
        var d1 = new CardsDriver(); var d2 = new CardsDriver(); var dn = new CardsDriver();
        var h1 = Hand(one); var h2 = Hand(two); var hn = Hand(named);
        var c1 = Enumerable.Range(11, 3).Select(id => Card(h1, id)).ToList();
        var c2 = Enumerable.Range(11, 3).Select(id => Card(h2, id)).ToList();
        var cn = Enumerable.Range(11, 3).Select(id => Card(hn, id)).ToList();
        d1._fan.SetCards(c1); d2._fan.SetCards(c2); dn._fan.SetCards(cn);
        d1.ApplyOrder(); d2.ApplyOrder(); dn.ApplyOrder(); d1.Insert(c1[2], 0); d1.ApplyOrder();
        Check(d1._fan.Cards.Select(Id).SequenceEqual(new[] { 13, 11, 12 }), "unnamed native actor can reorder its normal fan");
        d2.ApplyOrder(); Check(d2._fan.Cards.Select(Id).SequenceEqual(new[] { 11, 12, 13 }), "two unnamed same-class actors keep independent order");
        dn.ApplyOrder(); Check(dn._fan.Cards.Select(Id).SequenceEqual(new[] { 11, 12, 13 }), "named display equal to unnamed GUID cannot collide with its order");
        Check(CardsDriver.Key(c1[0], out string key, out int id) && key == "same-key" && id == 11, "unnamed scenario card key uses actual native actor GUID");
        Check(CardsDriver.Key(cn[0], out string nameKey, out _) && nameKey == "same-key", "named campaign/map presentation identity remains unchanged");
        var refreshed = Actor(null, "same-key", "order-refreshed"); var refreshedHand = Hand(refreshed);
        var refreshCards = Enumerable.Range(11, 3).Select(cardId => Card(refreshedHand, cardId)).ToList();
        var refreshedDriver = new CardsDriver(); refreshedDriver._fan.SetCards(refreshCards); refreshedDriver.ApplyOrder();
        Check(refreshedDriver._fan.Cards.Select(Id).SequenceEqual(new[] { 13, 11, 12 }), "refreshed native actor and widgets preserve GUID-scoped order");
    }
    private static void FocusCases()
    {
        SaveData.Instance.Global.GameMode = EGameMode.Guildmaster;
        var actor = Actor(null, "focus-native-a", "focus-native-a"); var hand = Hand(actor);
        var a = Card(hand, 21); var b = Card(hand, 22);
        hand.SelectCard(a.GameCard!.AbilityCard); hand.SelectCard(b.GameCard!.AbilityCard);
        Selected(hand, 2, "focus native pair");
        var driver = new CardsDriver { _boundHand = hand }; driver._tray.PlaceCard(b, 0); driver._tray.PlaceCard(a, 1);
        driver.Reconcile(hand);
        Check(ReferenceEquals(actor.CharacterClass.InitiativeAbilityCard, b.GameCard.AbilityCard), "own physical left recess still drives original native initiative swap");
        var other = Actor(null, "focus-native-b", "focus-native-b"); var otherHand = Hand(other); var foreign = Card(otherHand, 23);
        driver._boundHand = otherHand; driver._tray.PlaceCard(foreign, 0); var before = actor.CharacterClass.InitiativeAbilityCard;
        driver.Reconcile(hand);
        Check(ReferenceEquals(actor.CharacterClass.InitiativeAbilityCard, before), "old selection completion cannot reconcile against another actor's current tray");
        var pending = Card(hand, 24); driver._tray.PlaceCard(pending, 0); driver.Reconcile(hand);
        Check(ReferenceEquals(actor.CharacterClass.InitiativeAbilityCard, before), "pending same-actor presentation cannot rewrite the existing native initiative");
        driver._tray.PlaceCard(foreign, 0);
        Set(manager, "currentHand", hand); driver.Swap(); Set(manager, "currentHand", otherHand);
        CardActionQueue.Drain();
        Check(!ReferenceEquals(actor.CharacterClass.InitiativeAbilityCard, before), "queued explicit initiative swap still affects the hand captured at request");
        Check(driver._tray.LastSyncedHand == null, "queued initiative completion cannot repopulate a different actor's current tray");
        Check(driver._tray.Occupant(0) == foreign, "foreign actor's physical recess remains untouched by prior-hand completion");
    }
    private static void RapidReleaseQueue()
    {
        var hand = Hand(Actor(null, "rapid-native", "rapid-native"));
        var cards = Enumerable.Range(31, 3).Select(id => Card(hand, id)).ToArray(); var driver = new CardsDriver { _boundHand = hand };
        driver._fan.SetCards(cards.ToList());
        for (int slot = 0; slot < 2; slot++) { driver._tray.Target = slot; driver.Release(cards[slot], hand); CardActionQueue.Drain(); }
        driver._tray.Target = 0; driver.Release(cards[2], hand);
        driver._tray.Target = -1; driver.Release(cards[2], hand);
        driver._tray.Target = 0; driver.Release(cards[2], hand);
        Check(CardActionQueue.Count == 4, "normal release code admits replacement/withdrawal/replacement while earlier callbacks remain queued");
        int before = moves;
        CardActionQueue.Pump(); Check(!cards[0].GameCard!.IsSelected && !cards[2].GameCard!.IsSelected, "rapid native queue first withdraws displaced original");
        CardActionQueue.Pump(); Check(!cards[2].GameCard!.IsSelected, "rapid native queue skips an explicitly withdrawn older incoming select");
        Check(driver._tray.IsSelectionPending(cards[2], hand), "older native select acknowledgement cannot consume a newer physical drop token");
        CardActionQueue.Pump(); Check(!cards[2].GameCard!.IsSelected, "rapid native queue next acknowledges intervening take-back");
        Check(driver._tray.IsSelectionPending(cards[2], hand), "intervening native take-back leaves most recent drop awaiting its own acknowledgement");
        CardActionQueue.Pump(); Check(cards[2].GameCard!.IsSelected, "rapid native queue finally acknowledges most recent replacement");
        Check(!driver._tray.IsSelectionPending(cards[2], hand), "most recent native select acknowledgement consumes exactly its own drop token");
        Check(moves == before + 2, "rapid release queue preserves native FIFO while explicitly superseded select emits no rule move");
        Selected(hand, 2, "rapid native release final selection");
    }
    private static void FocusQueuedLeftIntent()
    {
        var hand = Hand(Actor(null, "queued-left", "queued-left"));
        var right = Card(hand, 41); var incoming = Card(hand, 42);
        hand.SelectCard(right.GameCard!.AbilityCard);
        var driver = new CardsDriver { _boundHand = hand };
        driver._tray.PlaceCard(right, 1); driver._fan.Add(incoming);
        driver._tray.Target = 0; driver.Release(incoming, hand);
        Check(CardActionQueue.Count == 1, "left placement captures one native select before view change");
        var other = Hand(Actor(null, "queued-left-view", "queued-left-view")); var foreign = Card(other, 43);
        driver._boundHand = other; driver._tray.RemoveCard(incoming); driver._tray.RemoveCard(right); driver._tray.PlaceCard(foreign, 0);
        CardActionQueue.Drain();
        Check(ReferenceEquals(hand.PlayerActor.CharacterClass.InitiativeAbilityCard, incoming.GameCard!.AbilityCard),
            "queued left-slot drop retains original actor initiative across view change");
        Check(driver._tray.Occupant(0) == foreign, "queued left intent never replaces the newly displayed actor's physical card");
    }
    public static int Run()
    {
        Prepare(); int index = 0;
        foreach (var mode in new[] { EGameMode.Guildmaster, EGameMode.Campaign, EGameMode.SingleScenario, EGameMode.FrontEndTutorial })
        foreach (bool gap in new[] { false, true })
        {
            NativeCycles(mode, "named-" + index, "guid-" + index, gap, index++);
            NativeCycles(mode, null, "guid-" + index, gap, index++);
            NativeCycles(mode, "", "guid-" + index, gap, index++);
            NativeCycles(mode, null, null, gap, index++);
        }
        OrderIsolation();
        FocusCases();
        FocusQueuedLeftIntent();
        RapidReleaseQueue();
        Check(errors == 0, "all actual native card callbacks and SRL moves complete without errors");
        Check(!Harmony.GetAllPatchedMethods().Any(method => method.DeclaringType.Assembly == typeof(ScenarioRuleClient).Assembly && Harmony.GetPatchInfo(method).Owners.Contains(Owner)), "no ScenarioRuleLibrary method is patched");
        return checks;
    }
    public static void Finish()
    {
        Application.logMessageReceived -= Log;
        if (rules != null && rules.IsAlive)
        {
            ((CancellationTokenSource)AccessTools.Field(typeof(ScenarioRuleClient), "s_CancellationTokenSource").GetValue(null)).Cancel();
            rules.Join();
        }
        new Harmony(Owner).UnpatchSelf();
        SharedLibrary.Client.SharedClient.SetDLLDebugHandler(null!);
        foreach (var value in objects) if (value != null) UnityEngine.Object.DestroyImmediate(value);
        objects.Clear();
    }
}
