using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using FFSNet;
using GloomhavenVR.Compat;
using GloomhavenVR.Core;
using HarmonyLib;
using ScenarioRuleLibrary;
using ScenarioRuleLibrary.YML;
using UnityEngine;

public static class InteractionProgram
{
    private static int count, showCalls, replayCalls, slotClicks;
    private static CActor? replayOwner, showOwner;
    private static CItem? replayItem;
    private static bool replayNetwork, replaySave;
    private static CActor? infusionOwner;
    private static List<ElementInfusionBoardManager.EElement>? infused, reserved;
    private static int boardUpdates;
    private static readonly Type[] ShowTypes = { typeof(CActor), typeof(List<CItem>), typeof(Func<CItem, bool>), typeof(Action<CItem, bool>), typeof(bool), typeof(string), typeof(string) };
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    private static void Patch(Harmony h, MethodBase target, string prefix)
        => h.Patch(target, prefix: new HarmonyMethod(typeof(InteractionProgram).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic)));
    private static bool Online(ref bool __result) { __result = true; return false; }
    private static bool Show(CActor actor) { showCalls++; showOwner = actor; return false; }
    private static bool Use(UseItemService __instance, CItem Item, bool networkActionIfOnline, bool saveState)
    { replayCalls++; replayOwner = Get<CActor>(__instance, "inventoryOwner"); replayItem = Item; replayNetwork = networkActionIfOnline; replaySave = saveState; return false; }
    private static bool Click() { slotClicks++; return false; }
    private static bool Infuse(List<ElementInfusionBoardManager.EElement> elements, CActor actorInfusing)
    { infused = elements; infusionOwner = actorInfusing; return false; }
    private static bool Reserve(List<ElementInfusionBoardManager.EElement> elements, bool active)
    { reserved = elements; return false; }
    private static bool Update() { boardUpdates++; return false; }

    public static int Run()
    {
        count = showCalls = replayCalls = slotClicks = 0;
        var h = new Harmony("ghvr.native-item-repair." + typeof(InteractionProgram).Assembly.GetName().Name);
        var root = new GameObject("NativeItemsFixture"); root.SetActive(false);
        var oldSave = SaveData.Instance;
        var oldChoreo = Choreographer.s_Choreographer;
        var oldYml = ScenarioRuleClient.SRLYML;
        var oldInfusionBoard = InfusionBoardUI.Instance;
        string networkVersion = NetworkVersion.Current;
        try
        {
            var bar = Inactive<UIUseItemsBar>(root.transform);
            Check(typeof(UIUseItemsBar).Assembly.GetName().Name == "GH.Runtime", "uses the shipped native item bar");
            var save = Inactive<SaveData>(root.transform);
            SaveData.Instance = save;
            save.Global = (GlobalData)FormatterServices.GetUninitializedObject(typeof(GlobalData));
            save.Global.GameMode = EGameMode.Campaign;
            var choreo = Inactive<Choreographer>(root.transform); Choreographer.s_Choreographer = choreo;
            var nativeYml = (CSRLYML)FormatterServices.GetUninitializedObject(typeof(CSRLYML));
            var mode = (CSRLYMLModeData)FormatterServices.GetUninitializedObject(typeof(CSRLYMLModeData));
            Set(mode, "<ItemCards>k__BackingField", new ItemCardYML());
            Set(nativeYml, "<GlobalData>k__BackingField", mode); nativeYml.YMLMode = CSRLYML.EYMLMode.Global;
            typeof(ScenarioRuleClient).GetField("s_SRLYML", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, nativeYml);
            var a = Actor(71, true); var b = Actor(72, false);
            var itemA = Item(171); var itemB = Item(172);
            a.Inventory.QuestItemSlots[0] = itemA; b.Inventory.QuestItemSlots[0] = itemB;
            var objectA = Inactive<ActorBehaviour>(root.transform); objectA.Actor = a;
            var objectB = Inactive<ActorBehaviour>(root.transform); objectB.Actor = b;
            choreo.m_ClientPlayers.Add(objectA.gameObject); choreo.m_ClientPlayers.Add(objectB.gameObject);
            Check(ReferenceEquals(choreo.FindPlayerActor(a.ID), a), "native actor-ID resolution returns the exact inventory owner");

            Patch(h, typeof(FFSNetwork).GetProperty(nameof(FFSNetwork.IsOnline))!.GetGetMethod()!, nameof(Online));
            // Full ShowUsableItems and IsItemInteractable execute. Only its downstream
            // rendering entry is bounded: it would require the entire live scenario UI.
            Patch(h, typeof(UIUseItemsBar).GetMethod(nameof(UIUseItemsBar.ShowItems), ShowTypes)!, nameof(Show));
            Set(bar, "actor", null);
            Check(ThrowsNull(() => bar.ShowUsableItems(a)), "unmodified first quest-item presentation reproduces native null actor");
            Set(bar, "actor", b); showCalls = 0;
            bar.ShowUsableItems(a);
            Check(showCalls == 0, "unmodified stale owner rejects an otherwise locally controlled quest item");
            Set(bar, "actor", a); showCalls = 0;
            bar.ShowUsableItems(b);
            Check(showCalls == 1 && ReferenceEquals(showOwner, b), "unmodified stale owner accepts another player's quest item");

            h.PatchAll(typeof(NativeBugFixes_ShowUsableItemsActor));
            Set(bar, "actor", null); showCalls = 0;
            Check(Works(() => bar.ShowUsableItems(a)) && showCalls == 1 && ReferenceEquals(showOwner, a), "fixed first presentation uses the supplied actor before its ownership predicate");
            Set(bar, "actor", b); showCalls = 0; bar.ShowUsableItems(a);
            Check(showCalls == 1 && ReferenceEquals(showOwner, a), "fixed stale non-null presentation uses the incoming owner");
            Set(bar, "actor", a); showCalls = 0; bar.ShowUsableItems(b);
            Check(showCalls == 0 && ReferenceEquals(Get<CActor>(bar, "actor"), b), "fixed remote quest item retains native ownership denial");
            VRSession.IsRunning = false; Set(bar, "actor", null);
            Check(ThrowsNull(() => bar.ShowUsableItems(a)), "VR-off first presentation retains the original failure");
            VRSession.IsRunning = true; Set(bar, "actor", a);
            Check(ThrowsNull(() => bar.ShowUsableItems(null!)) && ReferenceEquals(Get<CActor>(bar, "actor"), a), "invalid inventory owner is neither fabricated nor swallowed");

            // Full action and numeric replay bodies execute, including native item
            // lookup/element translation. Only the subsequent action service is captured:
            // native phase processing and buttons need an actual loaded scenario.
            Patch(h, typeof(UseItemService).GetMethod(nameof(UseItemService.UseItem))!, nameof(Use));
            var tokenA = new ItemToken(itemA.NetworkID);
            var actionA = Action(a.ID, tokenA, GameActionType.UseItem);
            Set(bar, "isShown", false); Set(bar, "actor", null);
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(actionA)), "unmodified hidden replay reproduces native null actor");
            Set(bar, "actor", b); replayCalls = 0;
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(actionA)) && replayCalls == 0, "unmodified stale replay dereferences the missing item from the wrong actor");
            Set(bar, "actor", a); replayCalls = 0; bar.ProxyUseItemBonus(actionA);
            Check(replayCalls == 1 && ReferenceEquals(replayItem, itemA) && ReferenceEquals(replayOwner, a) && !replayNetwork && !replaySave,
                "working unmodified native replay supplies the same exact item, actor and non-replicating flags as the repair");
            h.PatchAll(typeof(NativeBugFixes_ItemReplayActor));
            foreach (GameActionType kind in new[] { GameActionType.UseItem, GameActionType.ClickItemBonusSlot })
            foreach (CActor? stale in new CActor?[] { null, b, a })
            {
                var action = Action(a.ID, tokenA, kind);
                Set(bar, "actor", stale); replayCalls = 0;
                Check(Works(() => bar.ProxyUseItemBonus(action)) && replayCalls == 1 && ReferenceEquals(replayItem, itemA) && ReferenceEquals(replayOwner, a), "fixed hidden replay resolves the action's exact native item and owner");
                Check(!replayNetwork && !replaySave, "native replay retains networkActionIfOnline=false and saveState=false");
                Check(ReferenceEquals(action.SupplementaryDataToken, tokenA) && action.ActorID == a.ID && action.ActionTypeID == (int)kind,
                    "native action and token are unchanged for mixed clients");
                Check(itemA.SlotState == CItem.EItemSlotState.Useable && a.Inventory.AllItems[0] == itemA && b.Inventory.AllItems[0] == itemB,
                    "repair does not change native inventory membership or item state");
            }
            // Verify nonempty element payloads through the original numeric body.
            // UI reservation and authoritative infusion are downstream boundaries,
            // captured independently of this repair rather than simulated here.
            var board = Inactive<InfusionBoardUI>(root.transform);
            typeof(InfusionBoardUI).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, board);
            Patch(h, typeof(ElementInfusionBoardManager).GetMethod(nameof(ElementInfusionBoardManager.Infuse), new[] { typeof(List<ElementInfusionBoardManager.EElement>), typeof(CActor) })!, nameof(Infuse));
            Patch(h, typeof(InfusionBoardUI).GetMethod(nameof(InfusionBoardUI.ReserveElements))!, nameof(Reserve));
            Patch(h, typeof(InfusionBoardUI).GetMethod(nameof(InfusionBoardUI.UpdateBoard), new[] { typeof(List<ElementInfusionBoardManager.EElement>) })!, nameof(Update));
            var richToken = new ItemToken(itemA.NetworkID, chosenElement: new List<ElementInfusionBoardManager.EElement> { ElementInfusionBoardManager.EElement.Fire },
                infusions: new List<ElementInfusionBoardManager.EElement> { ElementInfusionBoardManager.EElement.Ice });
            var richAction = Action(a.ID, richToken, GameActionType.UseItem);
            int[] originalChosen = (int[])richToken.ChosenElement.Clone(), originalInfusions = (int[])richToken.InfusionElements.Clone();
            Set(bar, "actor", b); boardUpdates = 0; replayCalls = 0; bar.ProxyUseItemBonus(richAction);
            Check(replayCalls == 1 && itemA.ChosenElement.Count == 1 && itemA.ChosenElement[0] == ElementInfusionBoardManager.EElement.Fire,
                "native original replay preserves chosen-element translation");
            Check(infused?.Count == 1 && infused[0] == ElementInfusionBoardManager.EElement.Ice && ReferenceEquals(infusionOwner, a),
                "native original replay preserves infusion elements and exact inventory owner");
            Check(reserved?.Count == 1 && reserved[0] == ElementInfusionBoardManager.EElement.Fire && boardUpdates == 1,
                "native original replay retains its reservation and board update calls");
            Check(richToken.ChosenElement[0] == originalChosen[0] && richToken.InfusionElements[0] == originalInfusions[0]
                && ReferenceEquals(richAction.SupplementaryDataToken, richToken), "native replay token remains byte-field equivalent with nonempty elements");
            var foreignToken = new ItemToken(itemB.NetworkID);
            Set(bar, "actor", b);
            Check(Works(() => bar.ProxyUseItemBonus(Action(a.ID, foreignToken, GameActionType.UseItem))) && ReferenceEquals(Get<CActor>(bar, "actor"), b), "token absent from action actor inventory leaves native handling untouched");
            Set(bar, "actor", b); bar.ProxyUseItemBonus(Action(999, foreignToken, GameActionType.UseItem));
            Check(ReferenceEquals(Get<CActor>(bar, "actor"), b), "unknown actor leaves native handling untouched");
            Set(bar, "actor", b); bar.ProxyUseItemBonus(Action(a.ID, foreignToken, GameActionType.ShortRest));
            Check(ReferenceEquals(Get<CActor>(bar, "actor"), b), "unrelated native action retains native handling");
            var invalid = Action(a.ID, null, GameActionType.UseItem); Set(bar, "actor", b);
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(invalid)) && ReferenceEquals(Get<CActor>(bar, "actor"), b), "invalid token preserves original failure and bar context");
            tokenA.ChosenElement = null!; Set(bar, "actor", b);
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(actionA)) && ReferenceEquals(Get<CActor>(bar, "actor"), b), "invalid element data preserves original failure and bar context");
            tokenA.ChosenElement = Array.Empty<int>();
            VRSession.IsRunning = false; Set(bar, "actor", b); bar.ProxyUseItemBonus(Action(a.ID, foreignToken, GameActionType.UseItem));
            Check(ReferenceEquals(replayOwner, b), "VR-off replay retains the original actor"); VRSession.IsRunning = true;
            // A broken native FindPlayerActor entry can throw. Prefix must not leak
            // that new exception into the original network action handler.
            choreo.m_ClientPlayers.Insert(0, null!); Set(bar, "actor", b); bar.ProxyUseItemBonus(Action(a.ID, foreignToken, GameActionType.UseItem));
            Check(ReferenceEquals(replayOwner, b) && VRLog.Warnings == 1, "failed resolution falls back to original replay without prefix exception");
            bar.ProxyUseItemBonus(Action(a.ID, foreignToken, GameActionType.UseItem)); Check(VRLog.Warnings == 1, "resolution anomaly logging is bounded");
            choreo.m_ClientPlayers.RemoveAt(0);

            var summon = new CHeroSummonActor { StandeeID = 73 };
            Set(summon, "m_Inventory", new CInventory(1, summon)); Set(summon, "m_SummonerCached", a);
            Check(ReferenceEquals(summon.Summoner, a) && a.Inventory.AllItems.Contains(itemA) && !summon.Inventory.AllItems.Contains(itemA),
                "native summon and summoner are distinct inventories in the borrowing defect");
            Check(choreo.FindPlayerActor(summon.ID) == null, "native player actor lookup cannot fabricate an unregistered summon");
            Set(bar, "actor", summon); Set(bar, "isShown", false);
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(itemA.NetworkID, tokenA)) && ReferenceEquals(Get<CActor>(bar, "actor"), summon),
                "numeric replay without action actor identity retains original failure instead of changing inventory owner");
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(Action(summon.ID, tokenA, GameActionType.UseItem))) && ReferenceEquals(Get<CActor>(bar, "actor"), summon),
                "unresolved summon replay remains native rather than borrowing a summoner's model authority");
            // The ordinary emitted action with the actual inventory owner's ID is
            // repairable even when the previous hidden UI context was a summon.
            bar.ProxyUseItemBonus(actionA);
            Check(ReferenceEquals(replayOwner, a) && ReferenceEquals(replayItem, itemA),
                "native owner-ID replay repairs stale summon context without modifying payload or inventory ownership");

            var slot = Inactive<UIUseItemScenario>(root.transform); Set(slot, "selected", true);
            bar.ItemSlots.Add(itemA, slot); Set(bar, "isShown", true); Set(bar, "actor", b);
            Patch(h, typeof(UIUseItemScenario).GetMethod(nameof(UIUseItemScenario.OnPointerDown))!, nameof(Click));
            bar.ProxyUseItemBonus(actionA);
            Check(slotClicks == 1 && ReferenceEquals(Get<CActor>(bar, "actor"), b), "visible original slots retain their native click replay and context");
            Check(NetworkVersion.Current == networkVersion, "native network version stays exactly unchanged");
            h.UnpatchSelf(); Set(bar, "actor", null); Set(bar, "isShown", false);
            Check(ThrowsNull(() => bar.ProxyUseItemBonus(actionA)), "unpatch restores the exact original replay");
            return count;
        }
        finally
        {
            h.UnpatchSelf(); SaveData.Instance = oldSave; Choreographer.s_Choreographer = oldChoreo;
            typeof(ScenarioRuleClient).GetField("s_SRLYML", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, oldYml);
            typeof(InfusionBoardUI).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, oldInfusionBoard);
            VRSession.IsRunning = true; UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static CPlayerActor Actor(int id, bool controlled)
    {
        var actor = new CPlayerActor { IsUnderMyControl = controlled };
        var klass = (CCharacterClass)FormatterServices.GetUninitializedObject(typeof(CCharacterClass));
        Set(klass, "<ModelInstanceID>k__BackingField", id); Set(actor, "m_Class", klass);
        Set(actor, "m_Inventory", new CInventory(1, actor)); return actor;
    }
    private static CItem Item(uint id)
    {
        var item = new CItem { NetworkID = id, SlotState = CItem.EItemSlotState.Useable };
        var data = (ItemCardYMLData)FormatterServices.GetUninitializedObject(typeof(ItemCardYMLData));
        data.Slot = CItem.EItemSlot.QuestItem; data.Name = "NativeQuestItem"; data.ID = (int)id;
        Set(item, "<ID>k__BackingField", (int)id); Set(item, "<CardType>k__BackingField", CBaseCard.ECardType.Item);
        ScenarioRuleClient.SRLYML.GlobalData.ItemCards.LoadedYML.Add(data);
        Set(item, "m_YMLData", data); return item;
    }
    private static GameAction Action(int actor, ItemToken? token, GameActionType kind)
    {
        var action = (GameAction)FormatterServices.GetUninitializedObject(typeof(GameAction));
        Set(action, "<ActorID>k__BackingField", actor); Set(action, "<ActionTypeID>k__BackingField", (int)kind);
        action.SupplementaryDataToken = token; return action;
    }
    private static T Inactive<T>(Transform parent) where T : Component
    { var go = new GameObject(typeof(T).Name, typeof(RectTransform)); go.SetActive(false); go.transform.SetParent(parent, false); return go.AddComponent<T>(); }
    private static bool ThrowsNull(Action body)
    { try { body(); return false; } catch (NullReferenceException) { return true; } }
    private static bool Works(Action body)
    { try { body(); return true; } catch (NullReferenceException) { return false; } }
    private static FieldInfo Field(Type type, string name)
    { for (Type? current = type; current != null; current = current.BaseType) { var field = current.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly); if (field != null) return field; } throw new Exception("Missing field: " + name); }
    private static void Set(object value, string field, object? data) => Field(value.GetType(), field).SetValue(value, data);
    private static T Get<T>(object value, string field) => (T)Field(value.GetType(), field).GetValue(value)!;
}
