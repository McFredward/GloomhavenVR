using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ConfirmationOnOwner
{
    // Build750: the pooled Perks prompt survived the native Class Toggle in749.
    // SourceAlive cannot detect a reused inventory changing its character. Retire
    // the exact opening before an accepted native navigation mutates that context,
    // then run its original decline route. Presentation release/Reset deliberately
    // remain callback-free: modal dimming, fades and VR teardown are not navigation.
    private sealed class NavigationOpening
    {
        internal readonly WeakReference<Component>? Box;
        internal readonly Request Request;
        internal NavigationOpening(Component? box, Request request)
        { Box = ReferenceEquals(box, null) ? null : new WeakReference<Component>(box!); Request = request; }
    }

    internal sealed class NavigationOrigin
    {
        internal readonly Component Source;
        private readonly int _sourceGeneration, _partyGeneration, _menuGeneration;
        internal readonly Component? Party;
        internal readonly object? Character;
        internal readonly object? PartyCharacter;
        internal readonly object? Page;
        internal readonly Component? Menu;
        private readonly Dictionary<string, object?> _sourceFields = new();
        internal NavigationOrigin(Component source)
        {
            Source = source;
            Party = FindParty(source);
            Character = CharacterIdentity(Read(source, IdentityField(source.GetType())));
            PartyCharacter = CharacterIdentity(Read(Party, "selectedCharacter"));
            Page = Read(Party, "displayActive");
            Menu = FindMenu(source);
            foreach (string field in new[] { "characterData", "character", "selectedCharacter", "selectedCard", "mode",
                "slotType", "slotNum", "currentSelection", "player", "characterID", "characterName", "m_CurrentSlotIndex" })
                _sourceFields[field] = CharacterIdentity(Read(source, field));
            _sourceGeneration = Generation(source);
            _partyGeneration = Generation(Party);
            _menuGeneration = Generation(Menu);
        }
        internal object? SourceField(string field) => _sourceFields.TryGetValue(field, out object? value) ? value : null;
        internal bool IsCurrent => Source != null && _sourceGeneration == Generation(Source)
            && _partyGeneration == Generation(Party) && _menuGeneration == Generation(Menu)
            && Equals(Character, CharacterIdentity(Read(Source, IdentityField(Source.GetType()))))
            && (Party == null || Equals(PartyCharacter, CharacterIdentity(Read(Party, "selectedCharacter")))
                && Equals(Page, Read(Party, "displayActive")));
    }

    private static readonly List<NavigationOpening> NavigationOpenings = new(4);
    private static int _navigationFailures;
    private static int _navigationReports;
    private static void ReportNavigation(Request request, string outcome, string reason)
    {
        if (_navigationReports++ < 24)
            VRLog.Note("WorldUI", "CONFIRMATION NAVIGATION: " + request.Source.GetType().Name
                + " " + outcome + " before " + reason + ".");
    }

    private static object? Read(object? source, string? field) => source == null || field == null
        ? null : AccessTools.Field(source.GetType(), field)?.GetValue(source);

    private static string? IdentityField(Type type)
    {
        string name = type.Name;
        if (name == "UIPerksInventory" || name == "UIResetLevelUpWindow") return "characterData";
        if (name == "UILevelUpWindow" || name == "UIPartyItemInventoryDisplay" || name == "UIShopItemInventory"
            || name == "UITempleWindow" || name == "UINewEnhancementWindow") return "character";
        if (name == "NewPartyDisplayUI" || IsNative(type, "UIAdventurePartyAssemblyWindow")) return "selectedCharacter";
        if (name == "UIMultiplayerSelectPlayerScreen") return "currentSelection";
        if (name == "UIMultiplayerMerchantSlot") return "player";
        return null;
    }

    private static bool IsNative(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
            if (current.Name == name) return true;
        return false;
    }

    private static object? CharacterIdentity(object? value)
    {
        if (value == null) return null;
        Type type = value.GetType();
        // Party slots and native character services can be reused independently.
        // IDs include the campaign name: two characters of one class are distinct.
        object? data = AccessTools.Property(type, "Data")?.GetValue(value, null);
        if (data != null) value = data;
        type = value.GetType();
        PropertyInfo? id = AccessTools.Property(type, "CharacterID");
        PropertyInfo? name = AccessTools.Property(type, "CharacterName");
        return id != null && name != null
            ? Tuple.Create(id.GetValue(value, null), name.GetValue(value, null)) : value;
    }

    private static Component? FindParty(Component source)
    {
        NewPartyDisplayUI? party = NewPartyDisplayUI.PartyDisplay;
        if (party == null) return null;
        if (ReferenceEquals(source, party) || source.transform.IsChildOf(party.transform)) return party;
        // Converted native subwindows have left their original hierarchy. Follow
        // exact authored producer references, never focus, names or another window.
        object? perks = Read(party, "perkManager");
        object? equipment = Read(party, "itemInventoryDisplay");
        object? roster = Read(party, "characterSelector");
        if (ReferenceEquals(source, Read(perks, "inventory"))
            || ReferenceEquals(source, Read(equipment, "partyInventory"))
            || ReferenceEquals(source, roster) || ReferenceEquals(source, Read(roster, "roster"))) return party;
        if ((source is UILevelUpWindow || source is UIResetLevelUpWindow)
            && Equals(CharacterIdentity(Read(source, IdentityField(source.GetType()))),
                CharacterIdentity(Read(party, "selectedCharacter")))) return party;
        return null;
    }

    private static Component? FindMenu(Component source)
    {
        for (Transform? node = source.transform; node != null; node = node.parent)
            foreach (Component component in node.GetComponents<Component>())
                if (component != null && (IsNative(component.GetType(), "ESCMenu")
                    || IsNative(component.GetType(), "UIOptionsWindow")
                    || IsNative(component.GetType(), "UIMainOptionsMenu"))) return component;
        return null;
    }

    private static void ForgetNavigationOpening(Component box)
    {
        for (int i = NavigationOpenings.Count - 1; i >= 0; i--)
        {
            NavigationOpening opening = NavigationOpenings[i];
            Component? target = null;
            opening.Box?.TryGetTarget(out target);
            if (ReferenceEquals(target, box) || (target == null && !global::GloomhavenVR.WorldUI.Patches.ConfirmationRescue.IsCurrentNavigationOpening(opening.Request)))
                NavigationOpenings.RemoveAt(i);
        }
    }

    internal static void NativeOutcome(Component box)
    {
        if (Requests.TryGetValue(box, out Request? request)) request.Retired = true;
    }

    internal static void NativeOutcome(Component box, string method)
    {
        if (!Requests.TryGetValue(box, out Request? request)) return;
        if (box.GetType().Name == "UIMultiplayerConfirmationBox"
            && (method == "OnConfirm" && Read(box, "_onConfirmClickedCallback") == null
                || method == "OnSecondConfirm" && Read(box, "_onAlternativeConfirmClickedCallback") == null)) return;
        request.Retired = true;
        if (method == "OnConfirm" || method == "OnConfirmClick" || method == "OnSecondConfirm"
            || method == "OnCancel" || method == "OnBackClick"
            || method == "Hide" && box.GetType().Name == "UIEnhancementConfirmationBox"
                && box.GetComponent<UIWindow>() is UIWindow window && window.IsOpen) request.PendingNativeOutcome = true;
    }

    internal static void NativeTweenFinishing(UIWindow window)
    {
        if (window.IsOpen) return;
        for (int i = 0; i < NavigationOpenings.Count; i++)
        {
            NavigationOpening opening = NavigationOpenings[i];
            if (opening.Box != null && opening.Box.TryGetTarget(out Component? box) && box != null
                && ReferenceEquals(box.GetComponent<UIWindow>(), window)
                && Requests.TryGetValue(box, out Request? current) && ReferenceEquals(current, opening.Request))
                current.PendingNativeOutcome = false; // before native Hidden listeners/reentrant navigation
        }
    }

    // Called immediately before the actual stfld, after native permission/authority
    // checks. A refused native selection and a same-character refresh do nothing.
    internal static void BeforeIdentityWrite(Component owner, object? next, string field)
    {
        object? identity = CharacterIdentity(next);
        bool changed = !Equals(CharacterIdentity(Read(owner, field)), identity);
        if (changed && Enabled && NativeShowDepth == 0) AdvanceGeneration(owner);
        DeclineForNavigation(request => ReferenceEquals(request.Source, owner)
                && (changed || !Equals(request.Origin.SourceField(field), identity))
            || ReferenceEquals(request.Origin.Party, owner)
                && !Equals(request.Origin.PartyCharacter, identity)
            || owner.GetType().Name == "NewPartyCharacterUI" && request.Origin.Party != null
                && ReferenceEquals(Read(request.Origin.Party, "selectedCharacter"), owner)
                && !Equals(request.Origin.PartyCharacter, identity), "character");
    }

    internal static void BeforeInventoryClose(Component equipment)
    {
        if (Read(equipment, "partyInventory") is Component inventory) BeforeProducerDeparture(inventory);
    }

    internal sealed class InventoryRefresh
    {
        internal readonly Component Box;
        internal readonly Request Request;
        internal readonly Component Selection;
        internal readonly object? Item;
        internal InventoryRefresh(Component box, Request request, Component selection)
        { Box = box; Request = request; Selection = selection; Item = SlotItem(selection); }
    }

    private static object? SlotItem(Component slot) => AccessTools.Property(slot.GetType(), "Item")?.GetValue(slot, null) ?? Read(slot, "Item");

    internal static InventoryRefresh? BeforeInventoryRefresh(Component owner, object character, object slotType, int slotNum)
    {
        for (int i = 0; i < NavigationOpenings.Count; i++)
        {
            NavigationOpening opening = NavigationOpenings[i];
            Request request = opening.Request;
            if (!request.Retired && ReferenceEquals(request.Source, owner)
                && Equals(request.Origin.SourceField("character"), CharacterIdentity(character))
                && Equals(request.Origin.SourceField("slotType"), slotType)
                && Equals(request.Origin.SourceField("slotNum"), slotNum)
                && opening.Box != null && opening.Box.TryGetTarget(out Component? box) && box != null
                && Requests.TryGetValue(box, out Request? current) && ReferenceEquals(request, current)
                && Read(owner, "selectedItemToCofirmSwitch") is Component selection && selection != null)
                return new InventoryRefresh(box, request, selection);
        }
        return null;
    }

    internal static void AfterInventoryRefresh(Component owner, InventoryRefresh? refresh)
    {
        if (refresh == null || refresh.Request.Retired || refresh.Selection == null
            || !Requests.TryGetValue(refresh.Box, out Request? current) || !ReferenceEquals(current, refresh.Request)
            || Read(owner, "selectedItemToCofirmSwitch") != null || ForeignOwner(refresh.Box)) return;
        // Native Display clears this UI-only pointer even for resetOpen:false on
        // the same slot. Preserve its original No's focus target for that exact
        // live opening; no actor, equipment, choice or callback is replaced.
        AccessTools.Field(owner.GetType(), "selectedItemToCofirmSwitch")?.SetValue(owner, refresh.Selection);
        if (!ReferenceEquals(refresh.Item, SlotItem(refresh.Selection)))
            DeclineForNavigation(request => ReferenceEquals(request, refresh.Request), "replaced item source");
    }

    internal static void BeforePartyPage(Component owner, string page, bool selected)
    {
        bool same = string.Equals(Read(owner, "displayActive")?.ToString(), page, StringComparison.Ordinal);
        if (selected == same) return;
        if (Enabled && NativeShowDepth == 0) AdvanceGeneration(owner);
        DeclineForNavigation(request => ReferenceEquals(request.Origin.Party, owner), "page");
    }

    internal static void BeforeMenuPage(Component option)
    {
        Component? menu = FindMenu(option);
        if (menu == null) return;
        if (Enabled && NativeShowDepth == 0) AdvanceGeneration(menu);
        DeclineForNavigation(request => ReferenceEquals(request.Origin.Menu, menu), "menu");
    }

    internal static void BeforeProducerDeparture(Component owner)
    {
        if (Enabled && NativeShowDepth == 0) AdvanceGeneration(owner);
        DeclineForNavigation(request => ReferenceEquals(request.Source, owner)
            || (request.Source != null && request.Source.transform.IsChildOf(owner.transform)), "page");
    }

    internal static void BeforeUserWindowClose(UIWindow window)
    {
        DeclineForNavigation(request => ReferenceEquals(request.Window, window)
            || request.Origin.Party != null && ReferenceEquals(request.Origin.Party.GetComponent<UIWindow>(), window)
            || request.Origin.Menu != null && ReferenceEquals(request.Origin.Menu.GetComponent<UIWindow>(), window), "source window");
    }

    internal static void AfterNativeShow(Component? box)
    {
        if (ReferenceEquals(box, null) || NativeShowDepth != 0) return;
        if (Requests.TryGetValue(box, out Request? request) && request.Deferred && !request.Origin.IsCurrent)
            DeclineForNavigation(candidate => ReferenceEquals(candidate, request), "deferred origin");
    }

    private static void DeclineForNavigation(Predicate<Request> departs, string reason)
    {
        if (!Enabled || NativeShowDepth != 0) return;
        // Native decline can synchronously navigate or open a replacement. A
        // snapshot plus request identity prevents cancelling that new opening.
        NavigationOpening[] pending = NavigationOpenings.ToArray();
        for (int i = 0; i < pending.Length; i++)
        {
            NavigationOpening opening = pending[i];
            Request request = opening.Request;
            if (!departs(request) || request.Retired && !request.PendingNativeOutcome) continue;
            Component? box = null;
            opening.Box?.TryGetTarget(out box);
            Request? current = null;
            if (ReferenceEquals(box, null))
            {
                if (!global::GloomhavenVR.WorldUI.Patches.ConfirmationRescue.IsCurrentNavigationOpening(request)) continue;
            }
            else if (!Requests.TryGetValue(box, out current) || !ReferenceEquals(request, current)) continue;
            if (box != null && ForeignOwner(box)) continue;
            if (request.Retired)
            {
                UIWindow? chosen = box != null ? box.GetComponent<UIWindow>() : null;
                if (chosen != null && !chosen.IsOpen)
                {
                    request.PendingNativeOutcome = false;
                    chosen.StartAlphaTween(0f, 0f, ignoreTimeScale: true);
                    ReportNavigation(request, "completed its chosen native outcome", reason);
                }
                continue; // finish the already chosen native result; never turn Yes into No
            }
            request.Retired = true; // before any native event or reentrant navigation
            try
            {
                if (global::GloomhavenVR.WorldUI.Patches.ConfirmationRescue.DeclineForNavigation(box, request))
                { ReportNavigation(request, "declined its original fallback", reason); continue; }
                if (box == null) continue;
                UIWindow? window = box.GetComponent<UIWindow>();
                if (window == null || (!window.IsOpen && !window.IsVisible)) continue;
                Release(box);
                // Generic Hide is the original cancellation path (onHidden),
                // unlike UIItemConfirmationBox.Hide, which skips its No callback.
                if (box is ConfirmationBox generic) generic.Hide();
                else
                {
                    MethodInfo? cancel = AccessTools.DeclaredMethod(box.GetType(),
                        IsNative(box.GetType(), "UIAdventureCharacterConfirmationBox") ? "OnBackClick"
                        : IsNative(box.GetType(), "UIEnhancementConfirmationBox") ? "Hide" : "OnCancel");
                    if (cancel == null) continue; // no decline: mandatory native flow
                    cancel.Invoke(box, null);
                }
                // Item No executes on Hidden. Finish that native outcome before
                // inventory Display/Hide clears its selected-item/character fields.
                // Do not finish or reset a replacement created by the callback.
                if (Requests.TryGetValue(box, out current) && ReferenceEquals(request, current)
                    && !window.IsOpen)
                {
                    request.PendingNativeOutcome = false;
                    window.StartAlphaTween(0f, 0f, ignoreTimeScale: true);
                }
                ReportNavigation(request, "declined its original request", reason);
            }
            catch (Exception ex)
            {
                if (_navigationFailures++ < 4)
                    VRLog.Alert("WorldUI", "CONFIRMATION NAVIGATION: native decline failed before "
                        + reason + " change (" + ex.GetType().Name + "); no confirmation was dispatched.");
            }
        }
    }

    internal static object? CaptureRescueOpening(Component? box)
    {
        if (!ReferenceEquals(box, null) && Requests.TryGetValue(box, out Request? current)) return current;
        if (CurrentScope == null) return null;
        var request = new Request(CurrentScope);
        NavigationOpenings.Add(new NavigationOpening(box, request));
        if (!ReferenceEquals(box, null)) { Requests.Remove(box); Requests.Add(box, request); }
        return request;
    }
}

// This catalog is deliberately explicit. Native decision families without a No
// (character creation, retirement, rewards/level-up Continue and Short Rest) are
// not navigation-cancel targets. Town-service native ownership wins above.
internal static class ConfirmationNavigationCatalog
{
    internal static Type? NativeType(string name)
    {
        string? scope = typeof(UILevelUpWindow).Namespace;
        return typeof(UILevelUpWindow).Assembly.GetType(string.IsNullOrEmpty(scope) ? name : scope + "." + name);
    }

    internal static IEnumerable<MethodBase> Methods(string type, params string[] names)
    {
        Type? native = NativeType(type);
        if (native == null) yield break;
        foreach (MethodInfo method in native.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            for (int i = 0; i < names.Length; i++)
                if (method.Name == names[i]) { yield return method; break; }
    }

    internal static IEnumerable<MethodBase> Producers()
    {
        foreach (MethodBase method in Methods("GLOOM.MainMenu.UILoadGameWindow", "AskDeleteParty", "EnableDLC")) yield return method;
        foreach (MethodBase method in Methods("GLOOM.MainMenu.MainOptionExtras", "OpenModding", "OpenLevelEditor")) yield return method;
        foreach (MethodBase method in Methods("UICampaignAdventurePartyAssemblyWindow", "ConfirmDeleteCharacter")) yield return method;
        foreach (MethodBase method in Methods("UIMultiplayerSelectPlayerScreen", "OnRemoveClicked", "OnSelectedRemovePlayer")) yield return method;
        foreach (MethodBase method in Methods("UIMultiplayerMerchantSlot", "OnReportHotkeyClicked")) yield return method;
        foreach (MethodBase method in Methods("CardsHandManager", "DiscardActiveAbilityCard")) yield return method;
        foreach (MethodBase method in Methods("UIMapMultiplayerController", "OnAttemptRetirement")) yield return method;
        foreach (MethodBase method in Methods("UITempleWindow", "OnSelectedSlot")) yield return method;
        foreach (MethodBase method in Methods("UINewEnhancementWindow", "ConfirmBuy", "ConfirmSell")) yield return method;
    }
}

[HarmonyPatch(typeof(UIWindow), "OnTweenFinished")]
internal static class ConfirmationNativeTweenOutcomePatch
{
    private static void Prefix(UIWindow __instance) => ConfirmationOnOwner.NativeTweenFinishing(__instance);
}

[HarmonyPatch]
internal static class ConfirmationAdditionalOriginPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (MethodBase method in ConfirmationNavigationCatalog.Methods("UIAdventureCharacterConfirmationBox", "ShowConfirmationBox")) yield return method;
        foreach (MethodBase method in ConfirmationNavigationCatalog.Methods("UIMultiplayerConfirmationBox", "ShowConfirmation")) yield return method;
        foreach (MethodBase method in ConfirmationNavigationCatalog.Methods("UIEnhancementConfirmationBox", "ShowConfirmation")) yield return method;
    }
    private static void Prefix(Component __instance)
    {
        ConfirmationOnOwner.NativeShowDepth++;
        ConfirmationOnOwner.Record(__instance);
    }
    private static Exception? Finalizer(Exception? __exception, Component __instance)
    {
        ConfirmationOnOwner.NativeShowDepth--;
        ConfirmationOnOwner.AfterNativeShow(__instance);
        return __exception;
    }
}

[HarmonyPatch]
internal static class ConfirmationNativeOutcomePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string type in new[] { "ConfirmationBox", "UIItemConfirmationBox", "UIAdventureCharacterConfirmationBox", "UIMultiplayerConfirmationBox", "UIEnhancementConfirmationBox" })
            foreach (MethodBase method in ConfirmationNavigationCatalog.Methods(type, "Hide", "HideInternal", "OnConfirm", "OnConfirmClick", "OnSubmit", "OnCancel", "OnBackClick", "OnSecondConfirm")) yield return method;
    }
    private static void Prefix(Component __instance, MethodBase __originalMethod) => ConfirmationOnOwner.NativeOutcome(__instance, __originalMethod.Name);
}

[HarmonyPatch]
internal static class ConfirmationPartyNavigationPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => ConfirmationNavigationCatalog.Methods("NewPartyDisplayUI",
        "OnCardsSelected", "OnItemsSelected", "OnPerksSelected", "OnBattleGoalSelected", "OnCharacterPickerSelected");
    private static void Prefix(Component __instance, MethodBase __originalMethod, bool __1)
    {
        string page = __originalMethod.Name == "OnCardsSelected" ? "CARDS"
            : __originalMethod.Name == "OnItemsSelected" ? "INVENTORY"
            : __originalMethod.Name == "OnPerksSelected" ? "PERKS"
            : __originalMethod.Name == "OnBattleGoalSelected" ? "BATTLE_GOALS" : "CHARACTER_SELECTOR";
        ConfirmationOnOwner.BeforePartyPage(__instance, page, __1);
    }
}

[HarmonyPatch]
internal static class ConfirmationMenuNavigationPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => ConfirmationNavigationCatalog.Methods("UIMenuOption", "Select");
    private static void Prefix(Component __instance)
    {
        PropertyInfo? selected = AccessTools.Property(__instance.GetType(), "IsSelected");
        if (selected?.GetValue(__instance, null) is false) ConfirmationOnOwner.BeforeMenuPage(__instance);
    }
}

[HarmonyPatch]
internal static class ConfirmationProducerDeparturePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (string type in new[] { "UIOptionsWindow", "ESCMenu", "UIListModsView", "UIRulesetsWindow", "UIRulesetView",
            "GLOOM.MainMenu.UILoadGameWindow", "UIAdventurePartyAssemblyWindow", "UIMultiplayerSelectPlayerScreen" })
            foreach (MethodBase method in ConfirmationNavigationCatalog.Methods(type, "Cancel", "Close", "OnCloseButton")) yield return method;
    }
    private static void Prefix(Component __instance) => ConfirmationOnOwner.BeforeProducerDeparture(__instance);
}

[HarmonyPatch]
internal static class ConfirmationInventoryRefreshPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => ConfirmationNavigationCatalog.Methods("UIPartyItemInventoryDisplay", "Display");
    private static void Prefix(Component __instance, object __0, object __2, int __3, out ConfirmationOnOwner.InventoryRefresh? __state) =>
        __state = ConfirmationOnOwner.BeforeInventoryRefresh(__instance, __0, __2, __3);
    private static void Postfix(Component __instance, ConfirmationOnOwner.InventoryRefresh? __state) =>
        ConfirmationOnOwner.AfterInventoryRefresh(__instance, __state);
}
