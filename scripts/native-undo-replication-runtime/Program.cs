using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using FFSNet;
using GloomhavenVR.Board.Patches;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static int Repairs;
        internal static void Note(string scope, string text) => Repairs++;
        internal static void Warn(string scope, string text) => throw new Exception(text);
    }
}

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics;

    private static readonly List<(GameActionType Type, ActionPhaseType Phase, int Actor, int Half, int Card)> Packets = new();
    private static readonly List<string> RuleMessages = new();
    private static GameObject inactiveRoot;
    private static bool online = true;
    private static Choreographer choreographer;
    private static CPlayerActor actor;
    private static UndoButton undo;
    private static UIUseItemsBar items;
    private static InitiativeTrackActorBehaviour selectedEntry;
    private static int cardCallbacks;
    private static int handRestores;

    public static IEnumerator Run()
    {
        Prepare();

        SetActionPhase();
        SetProcessorPhase(ActionPhaseType.FirstAction);
        var item = Item("ITEM_NAME_MinorManaPotion");
        items.SetUseItem(item);
        items.SetActiveItemButtons(true);
        Check(undo.SkipNextNetworkAction && Overrides(undo) == 1, "native item confirmation arms a paired skip flag and cancel override");

        // Enter the real publisher item callback and retain its original rule/packet signatures.
        AccessTools.Method(typeof(UIUseItemsBar), "UseItem").Invoke(items, null);
        Check(Get<CItem>(items, "useItem") == null, "native item confirmation consumes its pending selection");
        Check(Packets.Select(packet => packet.Type).SequenceEqual(new[] { GameActionType.ClickItemBonusSlot, GameActionType.UseItem })
            && RuleMessages.Last() == "ETOGGLEITEM",
            "native item confirmation emits the observed item packets and queues its rule action");
        Packets.Clear();

        // Deliver the observed item-complete widget disable and ensuing ability presentation
        // explicitly. This isolated project has no end-to-end scenario choreography thread.
        AccessTools.Method(typeof(UndoButton), "OnDisable").Invoke(undo, null);
        Check(undo.SkipNextNetworkAction && Overrides(undo) == 0, "native item completion leaves an orphaned local skip flag");
        SelectTop();
        Check(Packets.Last() == (GameActionType.SelectCardAbility, ActionPhaseType.FirstAction, 15, 0, 74),
            "native ability selection emits the observed card, half, actor and phase");
        Packets.Clear();
        SetActionPhase();
        GameState.CurrentActionInitiator = GameState.EActionInitiator.AbilityCard;
        Check(undo.OnClickInternal(), "native undo still handles the targeting click");
        Check(Packets.SequenceEqual(new[] { (GameActionType.UndoAction, ActionPhaseType.TargetSelection, 0, 0, 0) }),
            "genuine targeting undo sends exactly one original UndoAction packet");
        Check(RuleMessages.Last() == "EUNDOMESSAGE" && handRestores == 1 && !undo.SkipNextNetworkAction,
            "native targeting undo still restores the card view and queues its native rule");
        Check(VRLog.Repairs == 1, "repair is reported once");

        // Drive the stock sender card callback after the native Undo boundary completes.
        // The receiver obtains the native undo packet, so its legal first-action path
        // is available before the same half is selected again.
        SelectTop();
        Check(Packets.Count == 2 && Packets[1] == (GameActionType.SelectCardAbility, ActionPhaseType.FirstAction, 15, 0, 74)
            && cardCallbacks == 2, "identical half can be selected with the original sender packet after a genuine undo");

        CheckLocalCancel();
        CheckUnchanged("proxy undo", true, true, true, true, ActionPhaseType.TargetSelection, false);
        CheckUnchanged("offline undo", false, true, true, true, ActionPhaseType.TargetSelection, true);
        CheckUnchanged("VR inactive", true, false, true, true, ActionPhaseType.TargetSelection, true);
        CheckUnchanged("observer", true, true, true, false, ActionPhaseType.TargetSelection, true);
        CheckUnchanged("unrelated button", true, true, false, true, ActionPhaseType.TargetSelection, true);
        CheckUnchanged("other targeting phase", true, true, true, true, ActionPhaseType.FirstAction, true);

        Packets.Clear();
        SetActionPhase();
        undo.SkipNextNetworkAction = true;
        Set(undo, "m_ButtonState", UndoButton.EButtonState.EUNDOBUTTONCLEARTARGETS);
        Check(undo.OnClickInternal(), "native clear-target path is handled");
        Check(Packets.SequenceEqual(new[] { (GameActionType.ClearTargets, ActionPhaseType.TargetSelection, 0, 0, 0) })
            && RuleMessages.Last() == "ECLEARTARGETSMESSAGE",
            "native phase decides between clear targets and UndoAction");
        Check(VRLog.Repairs == 1, "later legitimate repairs do not repeat normal logging");

        Metrics = "publisher item/undo/ability callbacks and rule-message enqueue; Photon transport and rule completion are explicit fixture boundaries";
        yield break;
    }

    private static void Prepare()
    {
        // Installed game IL runs through the native widget callbacks. Isolate unrelated
        // render/audio controls and the transport/rule worker at their explicit boundaries.
        var harmony = new Harmony("ghvr.bugfixCards.nativeBoundary");
        Port(harmony, AccessTools.PropertyGetter(typeof(FFSNetwork), "IsOnline"), nameof(IsOnline));
        Port(harmony, AccessTools.PropertyGetter(typeof(InputManager), "GamePadInUse"), nameof(MouseMode));
        Port(harmony, AccessTools.Method(typeof(GLOOM.LocalizationManager), "GetTranslation"), nameof(Translate));
        Port(harmony, AccessTools.PropertyGetter(typeof(CBaseCard), "Name"), nameof(CardName));
        Port(harmony, AccessTools.Method(typeof(Synchronizer), "SendGameAction"), nameof(RecordPacket));
        Port(harmony, AccessTools.Method(typeof(ScenarioRuleClient), "AddSRLQueueMessage"), nameof(RecordRule));
        Port(harmony, AccessTools.Method(typeof(CardsHandManager), "Undo"), nameof(RecordHandRestore));
        foreach (var (type, names) in new (Type, string[])[]
        {
            (typeof(ReadyButton), new[] { "Toggle", "SetInteractable" }),
            (typeof(SkipButton), new[] { "Toggle", "SetInteractable" }),
            (typeof(UndoButton), new[] { "SetInteractable" }),
            (typeof(UIUseItemsBar), new[] { "Hide", "SetItemsInteractable" }),
            (typeof(UIActiveBonusBar), new[] { "Hide", "SetInteractionAvailableSlots" }),
            (typeof(UIUseAbilitiesBar), new[] { "Hide" }),
            (typeof(UIUseAugmentationsBar), new[] { "Hide", "SetInteractionAvailableSlots" }),
            (typeof(InfusionBoardUI), new[] { "SaveState" }),
        })
            foreach (string name in names)
                Port(harmony, AccessTools.Method(type, name), nameof(Ignore));

        foreach (var method in AccessTools.GetDeclaredMethods(typeof(AudioControllerUtils)).Where(m => m.Name == "PlaySound"))
        {
            if (method.ReturnType == typeof(void))
                Port(harmony, method, nameof(Ignore));
        }
        Port(harmony, AccessTools.Method(typeof(Choreographer), "Pass"), nameof(QueuePass));
        Port(harmony, AccessTools.Method(typeof(InitiativeTrack), "SelectedActor"), nameof(SelectedActor));

        new Harmony("ghvr.bugfixCards.candidate").CreateClassProcessor(typeof(NativeUndoReplication)).Patch();

        inactiveRoot = new GameObject("Original native widgets");
        inactiveRoot.SetActive(false);
        actor = new CPlayerActor { IsUnderMyControl = true, OverrideCharacterModel = "SPELLWEAVER" };
        var character = (CCharacterClass)FormatterServices.GetUninitializedObject(typeof(CCharacterClass));
        Set(character, "<ModelInstanceID>k__BackingField", 15);
        Set(actor, "m_Class", character);
        selectedEntry = Component<InitiativeTrackPlayerBehaviour>();
        Set(selectedEntry, "actor", actor);
        Set(typeof(GameState), "s_CurrentActor", actor);
        Set(typeof(GameState), "s_CurrentActionSelectionFlag", GameState.EActionSelectionFlag.None);
        GameState.ExtraTurnActionSelectionFlagStack = new Stack<GameState.EActionSelectionFlag>();
        GameState.CurrentActionInitiator = GameState.EActionInitiator.None;
        choreographer = Component<Choreographer>();
        choreographer.m_CurrentActor = actor;
        choreographer.readyButton = Component<ReadyButton>();
        choreographer.m_SkipButton = Component<SkipButton>();
        Set(typeof(Choreographer), "s_Choreographer", choreographer);
        undo = UndoWidget();
        choreographer.m_UndoButton = undo;
        WorldspaceStarHexDisplay.Instance = Component<WorldspaceStarHexDisplay>();
        items = Register<UIUseItemsBar>();
        Set(items, "actor", actor);
        Register<UIActiveBonusBar>();
        Register<UIUseAbilitiesBar>();
        Register<UIUseAugmentationsBar>();
        AccessTools.PropertySetter(typeof(InfusionBoardUI), "Instance").Invoke(null, new object[] { Component<InfusionBoardUI>() });
        ElementInfusionBoardManager.SetElementColumn(new ElementInfusionBoardManager.EColumn[6]);
        var hands = Component<CardsHandManager>();
        AccessTools.PropertySetter(typeof(CardsHandManager), "Instance").Invoke(null, new object[] { hands });
        Set(hands, "cardsActionController", Component<CardsActionControlller>());
        Set(typeof(CardsActionControlller), "CurrentPhase", CardsActionControlller.Phase.Select1stCard);
        AccessTools.PropertySetter(typeof(InitiativeTrack), "Instance").Invoke(null, new object[] { Component<InitiativeTrack>() });
    }

    private static void SelectTop()
    {
        var selection = (CPhaseActionSelection)FormatterServices.GetUninitializedObject(typeof(CPhaseActionSelection));
        Set(selection, "m_PhaseType", CPhase.PhaseType.ActionSelection);
        Set(typeof(PhaseManager), "s_CurrentPhase", selection);
        SetProcessorPhase(ActionPhaseType.FirstAction);
        var widget = Component<FullAbilityCard>();
        var card = new CAbilityCard();
        Set(card, "m_TopAction", new CAction());
        Set(card, "m_BottomAction", new CAction());
        Set(card, "<CardInstanceID>k__BackingField", 74);
        Set(card, "m_StringID", "Cold Fire");
        Set(widget, "abilityCard", card);
        Set(widget, "playerActor", actor);
        Set(widget, "cardPile", CBaseCard.ECardPile.Round);
        Set(widget, "onActionCallback", (Action<FullAbilityCard, CBaseCard.ActionType>)((_, _) => cardCallbacks++));
        var title = Component<TextMeshProUGUI>();
        title.text = "Cold Fire";
        Set(widget, "titleText", title);
        Set(widget, "topActionButton", Half());
        Set(widget, "bottomActionButton", Half());
        widget.SetInteractable(true);
        widget.SetValid(true);
        widget.OnAbilityClick(CBaseCard.ActionType.TopAction, isProxyAction: false);
    }

    private static FullAbilityCardAction Half()
    {
        var half = Component<FullAbilityCardAction>();
        Set(half, "actionButton", Component<Button>());
        Set(half, "defaultActionButton", Component<Button>());
        Set(half, "_enhancementElements", new CardEnhancementElements());
        return half;
    }

    private static CItem Item(string key)
    {
        var item = new CItem { NetworkID = 37, SlotState = CItem.EItemSlotState.Useable };
        Set(item, "m_StringID", key);
        Set(item, "m_YMLData", new ItemCardYMLData("FixtureItem") { Name = key, Slot = CItem.EItemSlot.SmallItem, Trigger = CItem.EItemTrigger.DuringOwnTurn });
        items.ItemSlots.Add(item, Component<UIUseItemScenario>());
        return item;
    }

    private static void SetActionPhase()
    {
        var action = (CPhaseAction)FormatterServices.GetUninitializedObject(typeof(CPhaseAction));
        Set(action, "m_PhaseType", CPhase.PhaseType.Action);
        Set(action, "m_CurrentPhaseAbilities", new List<CPhaseAction.CPhaseAbility> { new(new CAbilityAttack(), actor, null, null) });
        Set(typeof(PhaseManager), "s_CurrentPhase", action);
        SetProcessorPhase(ActionPhaseType.TargetSelection);
    }

    private static void CheckLocalCancel()
    {
        Packets.Clear();
        int before = RuleMessages.Count;
        items.SetUseItem(Item("Native picker cancel"));
        items.SetActiveItemButtons(true);
        Check(undo.OnClickInternal(), "stock item-picker cancel callback is handled");
        Check(Packets.Count == 0 && RuleMessages.Count == before && Get<CItem>(items, "useItem") == null
            && Overrides(undo) == 0 && !undo.SkipNextNetworkAction, "active native item override remains local and clears without scenario Undo");
    }

    private static void CheckUnchanged(string role, bool isOnline, bool vr, bool canonical, bool owns, ActionPhaseType phase, bool local)
    {
        Packets.Clear();
        SetActionPhase();
        SetProcessorPhase(phase);
        online = isOnline;
        VRSession.IsRunning = vr;
        actor.IsUnderMyControl = owns;
        GameState.CurrentActionInitiator = GameState.EActionInitiator.None;
        var button = canonical ? undo : UndoWidget();
        button.SkipNextNetworkAction = true;
        int repairs = VRLog.Repairs;
        // The actual receiver uses false and passes the action payload; replay comes
        // through the same native callback without returning a duplicate GameAction.
        var proxy = local ? null : (GameAction)FormatterServices.GetUninitializedObject(typeof(GameAction));
        Check(button.OnClickInternal(local, proxy) && Packets.Count == 0 && VRLog.Repairs == repairs, role + ": preserves original undo behavior");
        actor.IsUnderMyControl = true;
        VRSession.IsRunning = true;
        online = true;
    }

    private static UndoButton UndoWidget()
    {
        var button = Component<UndoButton>();
        Set(button, "m_UndoButton", Component<ExtendedButton>());
        Set(button, "m_ButtonText", Component<TextMeshProUGUI>());
        return button;
    }

    private static T Register<T>() where T : Singleton<T>
    {
        var component = Component<T>();
        Set(typeof(Singleton<T>), "_instance", component);
        return component;
    }

    private static T Component<T>() where T : Component
    {
        var widget = new GameObject(typeof(T).Name, typeof(RectTransform));
        widget.transform.SetParent(inactiveRoot.transform, false);
        return widget.AddComponent<T>();
    }

    private static int Overrides(UndoButton button) => Get<ICollection>(button, "m_OnClickOverrides").Count;
    private static void SetProcessorPhase(ActionPhaseType phase)
        => Set(typeof(ActionProcessor), "currentState", new ActionProcessor.ProcessorState(ActionProcessorStateType.Halted, phase));
    private static void Set(object instance, string field, object value)
        => AccessTools.Field(instance as Type ?? instance.GetType(), field).SetValue(instance is Type ? null : instance, value);
    private static T Get<T>(object instance, string field)
        => (T)AccessTools.Field(instance.GetType(), field).GetValue(instance);
    private static void Port(Harmony harmony, System.Reflection.MethodInfo original, string name)
        => harmony.Patch(original, prefix: new HarmonyMethod(typeof(InteractionProgram), name));
    private static void Check(bool value, string message)
    {
        Checks++;
        if (!value)
            throw new Exception(message);
    }

    private static bool Ignore() => false;
    private static bool IsOnline(ref bool __result) { __result = online; return false; }
    private static bool MouseMode(ref bool __result) { __result = false; return false; }
    private static bool Translate(string __0, ref string __result) { __result = __0; return false; }
    private static bool CardName(CBaseCard __instance, ref string __result) { __result = Get<string>(__instance, "m_StringID"); return false; }
    private static bool RecordPacket(GameActionType __0, ActionPhaseType __1, int __4, int __5, int __7)
    {
        Packets.Add((__0, __1, __4, __5, __7));
        return false;
    }
    private static bool RecordRule(object __0, ref uint __result)
    {
        RuleMessages.Add(Get<object>(__0, "m_MessageType").ToString());
        __result = 1;
        return false;
    }
    private static bool SelectedActor(ref InitiativeTrackActorBehaviour __result)
    {
        __result = selectedEntry;
        return false;
    }
    private static bool RecordHandRestore() { handRestores++; return false; }
    private static bool QueuePass() { ScenarioRuleClient.Pass(); return false; }
}
