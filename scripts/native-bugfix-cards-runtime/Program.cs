using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FFSNet;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GloomhavenVR.Compat;

public static class InteractionProgram
{
    public static int Checks;
    public static string Metrics;
    private static bool online;
    private static int gameActions;
    private static readonly string Owner = "ghvr.bugfixCards.nativeBoundary";
    private static readonly string PatchOwner = "ghvr.bugfixCards.candidate";
    private static BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static void Check(bool value, string message) { Checks++; if (!value) throw new Exception(message); }
    private static void Set(object instance, string field, object value)
        => AccessTools.Field(instance is Type ? (Type)instance : instance.GetType(), field).SetValue(instance is Type ? null : instance, value);
    private static T Get<T>(object instance, string field)
        => (T)AccessTools.Field(instance is Type ? (Type)instance : instance.GetType(), field).GetValue(instance is Type ? null : instance);
    private static T Component<T>(string name) where T : Component
    {
        var go = new GameObject(name, typeof(RectTransform)); go.SetActive(false);
        return go.AddComponent<T>();
    }
    private static void SetStaticProperty(Type type, string name, object value)
        => type.GetProperty(name, Any).SetValue(null, value);
    private static bool Online(ref bool __result) { __result = online; return false; }
    private static bool RecordAction() { gameActions++; return false; }
    private static void Prepare()
    {
        var boundary = new Harmony(Owner);
        boundary.Patch(AccessTools.PropertyGetter(typeof(FFSNetwork), "IsOnline"),
            prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(Online)));
        // Transport is an external boundary. Any unexpected native game action is
        // counted and fails; no model or UI method is substituted.
        foreach (var method in typeof(Synchronizer).GetMethods(Any).Where(m => m.Name == "SendGameAction"))
            boundary.Patch(method, prefix: new HarmonyMethod(typeof(InteractionProgram), nameof(RecordAction)));
        new GameObject("NativeEventSystem").AddComponent<EventSystem>();
        var elements = Component<InfusionBoardUI>("NativeInfusionBoard");
        SetStaticProperty(typeof(InfusionBoardUI), "Instance", elements);
        ElementInfusionBoardManager.SetElementColumn(new ElementInfusionBoardManager.EColumn[6]);
        var augmentation = Component<UIUseAugmentationsBar>("NativeAugmentationBar");
        Set(typeof(Singleton<UIUseAugmentationsBar>), "_instance", augmentation);
        var manager = Component<CardsHandManager>("NativeHandManager");
        SetStaticProperty(typeof(CardsHandManager), "Instance", manager);
        var hand = Component<CardsHandUI>("NativeCurrentHand");
        Set(manager, "currentHand", hand);
        GameState.ExtraTurnActionSelectionFlagStack = new Stack<GameState.EActionSelectionFlag>();
        Set(typeof(GameState), "s_CurrentActionSelectionFlag", GameState.EActionSelectionFlag.None);
        var controller = Component<CardsActionControlller>("NativeCardsController");
        Set(typeof(CardsActionControlller), "s_Instance", controller);
        Set(manager, "cardsActionController", controller);
        Set(typeof(Choreographer), "s_Choreographer", Component<Choreographer>("NativeChoreographer"));
    }
    private static FullAbilityCardAction Half(string name)
    {
        var half = Component<FullAbilityCardAction>(name);
        Set(half, "actionButton", Component<Button>(name + "-Action"));
        Set(half, "defaultActionButton", Component<Button>(name + "-Default"));
        Set(half, "_enhancementElements", new CardEnhancementElements());
        Set(half, "canvasGroup", half.gameObject.AddComponent<CanvasGroup>());
        return half;
    }
    private static FullAbilityCard Card(string name, CPlayerActor actor)
    {
        var card = Component<FullAbilityCard>(name);
        Set(card, "titleText", Component<TextMeshProUGUI>(name + "-Title"));
        Get<TextMeshProUGUI>(card, "titleText").text = name;
        Set(card, "playerActor", actor);
        var ability = new CAbilityCard();
        Set(ability, "m_TopAction", new CAction()); Set(ability, "m_BottomAction", new CAction());
        Set(card, "abilityCard", ability);
        Set(card, "topActionButton", Half(name + "-Top"));
        Set(card, "bottomActionButton", Half(name + "-Bottom"));
        card.SetInteractable(true);
        card.SetValid(actor.IsUnderMyControl);
        var effects = Component<CardEffects>(name + "-OriginalEffects");
        Get<HashSet<CardEffects.FXTask>>(effects, "toggledEffects").Add(CardEffects.FXTask.DiscardMode);
        Set(card, "cardEffects", effects);
        return card;
    }
    private static CPlayerActor Actor()
    {
        var actor = new CPlayerActor();
        actor.TakingExtraTurnOfTypeStack = new Stack<CAbilityExtraTurn.EExtraTurnType>();
        actor.PendingExtraTurnOfTypeStack = new Stack<CAbilityExtraTurn.EExtraTurnType>();
        actor.CachedDisableCardActionActiveBonuses = new List<CDisableCardActionActiveBonus>();
        actor.IsUnderMyControl = true;
        return actor;
    }
    private static void Bind(CPlayerActor actor)
    {
        GameState.OverrideCurrentActor(actor);
        Set(CardsHandManager.Instance.CurrentHand, "playerActor", actor);
    }
    private static void Cache(CardsActionControlller controller, FullAbilityCard top, FullAbilityCard bottom,
        CardsActionControlller.Phase phase, CBaseCard.ActionType firstAction,
        CAbilityExtraTurn.EExtraTurnType staleType)
    {
        Set(controller, "topCard", top); Set(controller, "bottomCard", bottom);
        Set(controller, "firstPlayedCard", top); Set(controller, "firstPlayedActionType", firstAction);
        Set(controller, "secondPlayedCard", bottom);
        Set(controller, "secondPlayedActionType", CBaseCard.ActionType.BottomAction);
        Set(typeof(CardsActionControlller), "CurrentPhase", phase);
        controller.CachePhase(); // Actual publisher caches all original phase fields.
        Set(controller, "extraTurnType", staleType);
    }
    private static string Model(CPlayerActor actor)
        => string.Join(",", actor.TakingExtraTurnOfTypeStack.ToArray()) + "|"
         + string.Join(",", actor.PendingExtraTurnOfTypeStack.ToArray()) + "|"
         + actor.SelectingCardsForExtraTurnOfType + "|" + actor.SkipTopCardAction + "|" + actor.SkipBottomCardAction
         + "|" + GameState.HasPlayedTopAction + "|" + GameState.HasPlayedBottomAction + "|"
         + Get<GameState.EActionSelectionFlag>(typeof(GameState), "s_CurrentActionSelectionFlag")
         + "|" + string.Join(",", GameState.ExtraTurnActionSelectionFlagStack.ToArray())
         + "|" + ReferenceEquals(GameState.InternalCurrentActor, actor) + "|" + gameActions;
    private static void RestoreCase(bool repaired, string role, CBaseCard.ActionType firstAction,
        CAbilityExtraTurn.EExtraTurnType currentType, bool spentOther = false, bool disabledBonus = false,
        bool owns = true)
    {
        online = role != "singleplayer";
        var actor = Actor(); actor.IsUnderMyControl = owns; Bind(actor);
        if (currentType != CAbilityExtraTurn.EExtraTurnType.None) actor.TakingExtraTurnOfTypeStack.Push(currentType);
        bool topWasPlayed = firstAction == CBaseCard.ActionType.TopAction;
        var staleType = topWasPlayed ? CAbilityExtraTurn.EExtraTurnType.TopAction : CAbilityExtraTurn.EExtraTurnType.BottomAction;
        Set(typeof(GameState), "s_CurrentActionSelectionFlag", spentOther
            ? (topWasPlayed ? GameState.EActionSelectionFlag.BottomActionPlayed : GameState.EActionSelectionFlag.TopActionPlayed)
            : GameState.EActionSelectionFlag.None);
        var top = Card("Original-A", actor); var bottom = Card("Original-B", actor);
        // Native extra-turn preparation disables the opposing half on all cards.
        top.ToggleSideInteractivity(false, topWasPlayed ? CBaseCard.ActionType.BottomAction : CBaseCard.ActionType.TopAction);
        bottom.ToggleSideInteractivity(false, topWasPlayed ? CBaseCard.ActionType.BottomAction : CBaseCard.ActionType.TopAction);
        if (disabledBonus)
        {
            var bonus = new CDisableCardActionActiveBonus();
            var ability = new CAbilityDisableCardAction(new CAbilityDisableCardAction.DisableCardActionData
            { CardName = "Original-B", DisableActionType = topWasPlayed ? CBaseCard.ActionType.BottomAction : CBaseCard.ActionType.TopAction });
            Set(bonus, "DisableCardActionAbility", ability); actor.CachedDisableCardActionActiveBonuses.Add(bonus);
        }
        var controller = CardsActionControlller.s_Instance;
        Cache(controller, top, bottom, CardsActionControlller.Phase.Select2ndCard, firstAction, currentType);
        // Actual native extra-turn Init overwrites the UI field after the
        // original cards/phase have been cached. Model extra-turn entry/exit is
        // supplied explicitly at the actor boundary; no native rules are patched.
        actor.TakingExtraTurnOfTypeStack.Push(staleType);
        var extra = Card("Native-Extra", actor);
        controller.Init(extra, null, resetPhase: true, extraTurnType: actor.TakingExtraTurnOfType);
        Check(Get<CAbilityExtraTurn.EExtraTurnType>(controller, "extraTurnType") == staleType,
            "actual native Init overwrites the interrupted UI restriction");
        actor.TakingExtraTurnOfTypeStack.Pop(); controller.Reset();
        // Original CachePhase has set CurrentPhase=None. Original restored
        // SetPhase therefore calls original TryPlayBurnAnimation; the card's
        // already-present native DiscardMode prevents a duplicate effect request.
        string before = Model(actor);
        controller.RestorePhase();
        Check(before == Model(actor), role + ": restore does not change any sampled native turn model or emit GameAction");
        Check(controller.GetPhase() == CardsActionControlller.Phase.Select2ndCard, "original restore consumes cached phase");
        Check(Get<CardsActionControlller.Phase>(typeof(CardsActionControlller), "CachedPhase") == CardsActionControlller.Phase.None, "cache is retired by original method");
        Check(!top.topActionButton.IsInteractable() && !top.bottomActionButton.IsInteractable(), "first played card remains disabled");
        Check(top.cardEffects.HasEffect(CardEffects.FXTask.DiscardMode), "actual original pending discard effect survives restore");
        var available = topWasPlayed ? bottom.bottomActionButton : bottom.topActionButton;
        bool allowed = !spentOther && currentType != staleType && !disabledBonus;
        if (repaired)
        {
            Check(available.IsInteractable(topWasPlayed ? CBaseCard.ActionType.BottomAction : CBaseCard.ActionType.TopAction) == allowed,
                "legal restored card half exactly follows original native restrictions");
            Check(Get<CAbilityExtraTurn.EExtraTurnType>(controller, "extraTurnType") == currentType,
                "stale local type follows restored native actor, including nested turn");
        }
        else
            Check(!available.IsInteractable(), "original stale half remains blocked: reproduced native UI bug");
        Check(Get<bool>(bottom, "isValid") == owns, "card ownership validity is never promoted by repair");
        Set(typeof(GameState), "s_CurrentActionSelectionFlag", GameState.EActionSelectionFlag.None);
    }
    public static IEnumerator Run()
    {
        Prepare();
        if (typeof(InteractionProgram).Assembly.GetName().Name.Contains("owner_member_fault"))
        {
            new Harmony(PatchOwner).PatchAll(typeof(NativeCardsRestorePhaseFix));
            GloomhavenVR.Core.VRLog.Throw = true;
            for (int attempt = 0; attempt < 3; attempt++)
                RestoreCase(false, "singleplayer", CBaseCard.ActionType.TopAction, CAbilityExtraTurn.EExtraTurnType.None);
            Check(GloomhavenVR.Core.VRLog.Failures == 1,
                "reflective member mismatch is reported once and logger failure cannot suppress original restore");
            Metrics = "Actual publisher restore continues three times after an incompatible owner member and a throwing diagnostic sink.";
            new Harmony(PatchOwner).UnpatchSelf(); new Harmony(Owner).UnpatchSelf();
            yield break;
        }
        if (typeof(InteractionProgram).Assembly.GetName().Name.Contains("owner_member_missing"))
        {
            new Harmony(PatchOwner).PatchAll(typeof(NativeCardsRestorePhaseFix));
            for (int attempt = 0; attempt < 3; attempt++)
                RestoreCase(false, "singleplayer", CBaseCard.ActionType.TopAction, CAbilityExtraTurn.EExtraTurnType.None);
            Check(GloomhavenVR.Core.VRLog.Failures == 0,
                "missing optional owner member is quietly declined and cannot suppress original restore");
            Metrics = "Actual publisher restore continues three times when its optional cached-owner member is absent.";
            new Harmony(PatchOwner).UnpatchSelf(); new Harmony(Owner).UnpatchSelf();
            yield break;
        }
        RestoreCase(false, "singleplayer", CBaseCard.ActionType.TopAction, CAbilityExtraTurn.EExtraTurnType.None);
        RestoreCase(false, "singleplayer", CBaseCard.ActionType.BottomAction, CAbilityExtraTurn.EExtraTurnType.None);
        var harmony = new Harmony(PatchOwner); harmony.PatchAll(typeof(NativeCardsRestorePhaseFix));
        foreach (string role in new[] { "singleplayer", "VR-host-own-action-with-vanilla-client", "VR-client-own-action-with-vanilla-host", "VR-replaying-vanilla-action" })
        foreach (var first in new[] { CBaseCard.ActionType.TopAction, CBaseCard.ActionType.BottomAction })
        {
            RestoreCase(true, role, first, CAbilityExtraTurn.EExtraTurnType.None);
            RestoreCase(true, role, first, CAbilityExtraTurn.EExtraTurnType.BothActions);
            RestoreCase(true, role, first, first == CBaseCard.ActionType.TopAction
                ? CAbilityExtraTurn.EExtraTurnType.TopAction : CAbilityExtraTurn.EExtraTurnType.BottomAction);
            RestoreCase(true, role, first, CAbilityExtraTurn.EExtraTurnType.None, spentOther: true);
            RestoreCase(true, role, first, CAbilityExtraTurn.EExtraTurnType.None, disabledBonus: true);
            RestoreCase(true, role, first, CAbilityExtraTurn.EExtraTurnType.None, owns: false);
        }
        GuardCases();
        TargetPhaseComparison();
        SelectionPhaseCases();
        OriginalRulesCounterexamples();
        Check(gameActions == 0, "all real native restore paths emitted no game action");
        var patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(PatchOwner)).ToArray();
        Check(patched.Length == 1 && patched[0].DeclaringType == typeof(CardsActionControlller), "candidate patches only one GH.Runtime UI method");
        Check(!Harmony.GetAllPatchedMethods().Any(m => m.DeclaringType.Assembly == typeof(GameState).Assembly
            && Harmony.GetPatchInfo(m).Owners.Contains(PatchOwner)), "no ScenarioRuleLibrary method is patched");
        harmony.UnpatchSelf(); new Harmony(Owner).UnpatchSelf();
        Metrics = "Actual original GH.Runtime RestorePhase/CachePhase/SetPhase, FullAbilityCard and half Buttons; both baseline failures; 48 scoped role/half/restriction repairs; native-model fields unchanged and zero native GameActions. Online/role values are explicit transport boundaries, not a connected multiplayer session.";
        yield break;
    }
    private static void GuardCases()
    {
        var actor = Actor(); Bind(actor); var other = Actor();
        var controller = CardsActionControlller.s_Instance;
        var top = Card("Guard-A", actor); var bottom = Card("Guard-B", actor);
        MethodInfo prefix = AccessTools.Method(typeof(NativeCardsRestorePhaseFix), "Prefix");
        foreach (string kind in new[] { "vr-off", "none-phase", "pick-first", "pick-second", "other-controller", "actor-mismatch", "split-pair", "no-actor", "empty-pair", "missing-hand", "missing-manager", "hand-owner-mismatch" })
        {
            GloomhavenVR.Core.VRSession.IsRunning = kind != "vr-off";
            GameState.OverrideCurrentActor(kind == "no-actor" ? null : kind == "actor-mismatch" ? other : actor);
            var current = kind == "other-controller" ? Component<CardsActionControlller>("ObserverController") : controller;
            if (kind == "split-pair") Set(bottom, "playerActor", other); else Set(bottom, "playerActor", actor);
            var manager = CardsHandManager.Instance; var hand = manager.CurrentHand;
            if (kind == "missing-hand") Set(manager, "currentHand", null);
            if (kind == "missing-manager") SetStaticProperty(typeof(CardsHandManager), "Instance", null);
            if (kind == "hand-owner-mismatch") Set(hand, "playerActor", other);
            top.SetInteractable(false); bottom.SetInteractable(false);
            var phase = kind == "none-phase" ? CardsActionControlller.Phase.None : kind == "pick-first" ? CardsActionControlller.Phase.Pick1stTarget
                : kind == "pick-second" ? CardsActionControlller.Phase.Pick2ndTarget : CardsActionControlller.Phase.Select2ndCard;
            object[] args = { current, phase,
                kind == "empty-pair" ? null : top, kind == "empty-pair" ? null : bottom, CAbilityExtraTurn.EExtraTurnType.TopAction };
            prefix.Invoke(null, args);
            Check((CAbilityExtraTurn.EExtraTurnType)args[4] == CAbilityExtraTurn.EExtraTurnType.TopAction, kind + ": stale/missing/observer identity does not rewrite UI type");
            Check(!top.topActionButton.IsInteractable() && !bottom.bottomActionButton.IsInteractable(), kind + ": no stale widget interactivity promoted");
            SetStaticProperty(typeof(CardsHandManager), "Instance", manager); Set(manager, "currentHand", hand); Set(hand, "playerActor", actor);
        }
        GloomhavenVR.Core.VRSession.IsRunning = true; Bind(actor);
    }
    private static void TargetPhaseComparison()
    {
        var actor = Actor(); Bind(actor); var controller = CardsActionControlller.s_Instance;
        var first = Card("Target-Used", actor); var second = Card("Target-Current", actor);
        first.SetInteractable(false);
        Cache(controller, first, second, CardsActionControlller.Phase.Pick2ndTarget,
            CBaseCard.ActionType.TopAction, CAbilityExtraTurn.EExtraTurnType.TopAction);
        controller.RestorePhase();
        Check(!first.topActionButton.IsInteractable() && !first.bottomActionButton.IsInteractable(),
            "candidate leaves already-used first card disabled in native target phase");
        Check(Get<CAbilityExtraTurn.EExtraTurnType>(controller, "extraTurnType") == CAbilityExtraTurn.EExtraTurnType.TopAction,
            "candidate leaves target-phase UI state untouched");
        first.SetInteractable(false);
        Cache(controller, first, second, CardsActionControlller.Phase.Pick2ndTarget,
            CBaseCard.ActionType.TopAction, CAbilityExtraTurn.EExtraTurnType.TopAction);
        // Exact upstream prefix operations applied to the real original widgets,
        // followed by the real original RestorePhase (not a lookalike controller).
        first.SetInteractable(true); second.SetInteractable(true);
        Set(controller, "extraTurnType", actor.TakingExtraTurnOfType);
        controller.RestorePhase();
        Check(first.topActionButton.IsInteractable() && first.bottomActionButton.IsInteractable(),
            "upstream broad reset promotes already-used first card in Pick2ndTarget: reproduced counterexample");
    }
    private static void SelectionPhaseCases()
    {
        foreach (var type in Enum.GetValues(typeof(CAbilityExtraTurn.EExtraTurnType)).Cast<CAbilityExtraTurn.EExtraTurnType>())
        foreach (bool owns in new[] { true, false })
        {
            online = true; var actor = Actor(); actor.IsUnderMyControl = owns; Bind(actor);
            if (type != CAbilityExtraTurn.EExtraTurnType.None) actor.TakingExtraTurnOfTypeStack.Push(type);
            var top = Card("Selection-One", actor);
            var bottom = owns ? Card("Selection-Two", actor) : null;
            var controller = CardsActionControlller.s_Instance;
            Cache(controller, top, bottom, CardsActionControlller.Phase.Select1stCard,
                CBaseCard.ActionType.TopAction, CAbilityExtraTurn.EExtraTurnType.BottomAction);
            string before = Model(actor); controller.RestorePhase();
            Check(Model(actor) == before, "Select1st original native model unchanged");
            Check(top.topActionButton.IsInteractable() == (type != CAbilityExtraTurn.EExtraTurnType.BottomAction), "native top-side restriction retained");
            Check(top.bottomActionButton.IsInteractable() == (type != CAbilityExtraTurn.EExtraTurnType.TopAction), "native bottom-side restriction retained");
            Check(Get<bool>(top, "isValid") == owns, "native SetValid retains actual ownership for a single or paired card");
        }
    }
    private static void OriginalRulesCounterexamples()
    {
        // Audit-only original rules, never replacements or Harmony patches.
        // This source evidence is why these upstream patches cannot be imported
        // into a mixed vanilla/VR session as a presentation repair.
        var caster = Actor(); var damaged = Actor();
        var ability = new CAbilityPreventDamage(false) { Strength = 1 };
        typeof(CAbility).GetProperty("ActiveBonusData", Any).SetValue(ability,
            new AbilityData.ActiveBonusData { IsToggleBonus = true, ToggleIsOptional = true });
        var active = new CPreventDamageActiveBonus(); Set(active, "m_Ability", ability);
        typeof(CActiveBonus).GetProperty("Caster", Any).SetValue(active, caster);
        active.ToggledBonus = false;
        var behaviour = new CPreventDamageActiveBonus_PreventAndApplyToActiveBonusCaster();
        Set(behaviour, "m_Ability", ability); Set(behaviour, "m_ActiveBonus", active);
        GameState.RedirectedDamageToActor = null;
        Check(!active.IsActiveBonusToggledAndNotRestricted(damaged), "actual inactive Divine Intervention native guard is false");
        behaviour.OnPreventDamageTriggered(3, null, damaged, null);
        Check(GameState.RedirectedDamageToActor != null
            && ReferenceEquals(GameState.RedirectedDamageToActor.Item1, caster)
            && GameState.RedirectedDamageToActor.Item2 == 2,
            "actual original inactive Divine Intervention redirects unrelated prevented damage: native bug reproduced");
        GameState.RedirectedDamageToActor = null;
        behaviour.OnPreventDamageTriggered(1, null, damaged, null);
        Check(GameState.RedirectedDamageToActor == null, "original zero net redirected damage stays zero");
        var scenario = new CScenario("audit", "audit", 0, default(CVectorInt3), null);
        Set(typeof(ScenarioManager), "s_Scenario", scenario);
        typeof(CActor).GetProperty("ActorGuid", Any).SetValue(caster, "audit-player");
        scenario.PlayerActors.Add(caster);
        var parent = new CHeroSummonActor(); var child = new CHeroSummonActor();
        typeof(CActor).GetProperty("ActorGuid", Any).SetValue(parent, "audit-parent-summon");
        typeof(CActor).GetProperty("ActorGuid", Any).SetValue(child, "audit-child-summon");
        Set(parent, "m_SummonerGuid", "audit-player"); Set(child, "m_SummonerGuid", "audit-parent-summon");
        scenario.HeroSummons.Add(parent); scenario.HeroSummons.Add(child);
        Check(child.Summoner == null, "actual original nested summon fails to resolve its player owner");
        bool nativeNullFault = false;
        try { _ = child.IsCompanionSummon; }
        catch (NullReferenceException) { nativeNullFault = true; }
        Check(nativeNullFault, "actual native IsCompanionSummon throws for summon-created summon");
        Check(Get<CPlayerActor>(parent, "m_SummonerCached") == null,
            "upstream fallback cannot recover from an uninitialized parent's native cache");
        Check(ReferenceEquals(parent.Summoner, caster), "actual parent getter resolves original player and warms native cache");
        Check(child.Summoner == null, "actual original nested getter still fails even after parent cache is warm");
        Set(typeof(ScenarioManager), "s_Scenario", null);
    }
}
