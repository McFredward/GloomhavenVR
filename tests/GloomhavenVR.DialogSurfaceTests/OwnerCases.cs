using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;

internal static class OwnerCases
{
    private static void Check(bool value, string why) => Program.Check(value, why);
    private static void Record(UIConfirmationBoxManager manager, Component? source)
    {
        var previous = ConfirmationOnOwner.CurrentScope;
        ConfirmationOnOwner.CurrentScope = source;
        object?[] args = { manager, null };
        typeof(ConfirmationRequestOriginPatch).GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        typeof(ConfirmationRequestOriginPatch).GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { null, args[1] });
        ConfirmationOnOwner.CurrentScope = previous;
    }
    private static (DialogSurface, UIConfirmationBoxManager, UILevelUpWindow, ConvertedPanel, Transform) Setup()
    {
        CanvasConversion.Reset();
        WorldUIConfig.ConversionActive = WorldUIConfig.Dialogs.Value = true;
        VRModeStateMachine.TableInFrontOfPlayer = true;
        FlatScreen.ManualScreenActive = false;
        var main = new GameObject("party");
        var content = new GameObject("level-up");
        content.transform.SetParent(main.transform, false);
        content.AddComponent<UIWindow>();
        var source = content.AddComponent<UILevelUpWindow>();
        var panel = CanvasConversion.Convert((RectTransform)main.transform, "main", 1)!;
        var home = new GameObject("persistent canvas").transform;
        var manager = new UIConfirmationBoxManager();
        manager.CurrentBox.transform.SetParent(home, false);
        Singleton<UIConfirmationBoxManager>.Instance = manager;
        var surface = new DialogSurface(); surface.Tick();
        return (surface, manager, source, panel, home);
    }

    internal static void Run()
    {
        var (surface, manager, source, panel, home) = Setup();
        var rect = (RectTransform)manager.CurrentBox.transform;
        rect.anchorMin = new Vector2(.1f,.2f); rect.anchorMax = new Vector2(.6f,.7f);
        rect.pivot = new Vector2(.3f,.4f); rect.sizeDelta = new Vector2(1200,900);
        rect.anchoredPosition = new Vector2(81,23); rect.localPosition = new Vector3(81,23,9);
        rect.localScale = new Vector3(.8f,.9f,1);
        rect.localRotation = new Quaternion { w = .7f };
        rect.gameObject.layer = 3; source.gameObject.layer = 24;
        var oldLayout = rect.gameObject.AddComponent<LayoutElement>(); oldLayout.ignoreLayout = false;
        Record(manager, source); manager.Show(); surface.Tick();
        Check(CanvasConversion.ActivePanels.Count == 2 && CanvasConversion.Converts == 2
            && ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "causal confirmation has one owner-bound member and no independent world placement");
        Check(ReferenceEquals(rect.parent,CanvasConversion.Last!.HostRect) && ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "confirmation must be mounted to its exact level-up subview inside the main panel");
        Check(rect.sizeDelta.x == 1200 && rect.sizeDelta.y == 900 && rect.anchoredPosition.sqrMagnitude == 0,
            "centering must preserve measured native frame, not collapse stretched anchors");
        Check(CanvasConversion.Last!.ModLayerEnabled,"persistent confirmation admits isolated filtered capture and scoped blur treatment");
        Check(CanvasConversion.Last.HideBackground,"persistent confirmation removes only full-frame backdrops before reveal and allocation");
        Check(CanvasConversion.Last.BaseSortingOrder==panel.BaseSortingOrder+1,"persistent confirmation starts above its owner inside the native depth tie");
        rect.sizeDelta = Vector2.zero;
        surface.Tick();
        Check(rect.sizeDelta.x == 1200 && rect.sizeDelta.y == 900,
            "centering must preserve measured native frame, not collapse stretched anchors");
        CanvasConversion.Last!.HostRaycaster.enabled = false; surface.Tick();
        Check(CanvasConversion.Last.HostRaycaster.enabled,"embedded confirmation must retain modal laser and poke routing");
        int transfers = CanvasConversion.Transfers;
        for (int i=0;i<30;i++) surface.Tick();
        Check(CanvasConversion.Transfers == transfers && CanvasConversion.Converts == 2,
            "settled containment must not repeatedly convert or transfer the native dialog");
        source.GetComponent<UIWindow>()!.IsOpen = source.GetComponent<UIWindow>()!.IsVisible = false;
        surface.Tick();
        Check(manager.CurrentBox.IsOpen && ReferenceEquals(rect.parent,CanvasConversion.Last!.HostRect),
            "native parent dimming must never synthesize cancellation or lose a live confirmation");
        rect.RejectReparent = true;
        ConfirmationOnOwner.Release(manager.CurrentBox);
        Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "rejected native reparenting must retain the restoration record instead of losing the pooled original");
        rect.RejectReparent = false;
        manager.Hide(); surface.Tick();
        Check(ReferenceEquals(rect.parent,home) && !ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "native hide must restore the original confirmation parent without a lingering claim");
        Check(rect.anchorMin.x == .1f && rect.anchorMax.y == .7f && rect.pivot.x == .3f
            && rect.sizeDelta.x == 1200 && rect.anchoredPosition.x == 81 && rect.anchoredPosition.y == 23
            && rect.localPosition.z == 9 && rect.localScale.x == .8f && rect.localRotation.w == .7f
            && rect.gameObject.layer == 3 && !oldLayout.ignoreLayout,
            "native hide must restore authored rect, rotation, scale, layer and layout exactly");
        // Same pooled box now carries a different unowned prompt; yesterday's screen is not its owner.
        Record(manager,null); manager.Show(); surface.Tick();
        Check(CanvasConversion.Converts == 3 && !ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "unowned reuse of a pooled dialog must retain standalone fallback instead of stale level-up ownership");
        // A source can become available after dedicated conversion. Retire it before recording home.
        Record(manager,source); surface.Tick();
        Check(CanvasConversion.ActivePanels.Count == 2 && CanvasConversion.Releases == 3
            && ReferenceEquals(rect.parent,CanvasConversion.Last!.HostRect),
            "late semantic adoption must release the old standalone capture before recording native home");
        manager.Hide(); surface.Tick();
        Check(ReferenceEquals(rect.parent,home),"late adoption must restore native home rather than a dead standalone host");
        surface.Shutdown();

        (surface,manager,source,panel,home)=Setup();
        Record(manager,source); manager.Show(); surface.Tick();
        WorldUIConfig.ConversionActive=false; surface.Tick();
        Check(manager.CurrentBox.IsOpen && ReferenceEquals(manager.CurrentBox.transform.parent,home),
            "VR-off teardown must restore presentation without invoking native cancel or confirm");
        WorldUIConfig.ConversionActive=true; surface.Tick();
        Check(ReferenceEquals(manager.CurrentBox.transform.parent,CanvasConversion.Last!.HostRect),
            "re-enabling conversion must contain an already-open request without another native Show edge");
        source.gameObject.activeSelf=false; surface.Tick();
        Check(manager.CurrentBox.IsOpen && !ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "lost producer must release presentation safely and preserve native fallback authority");
        surface.Shutdown();

        (surface,manager,source,panel,home)=Setup();
        var menu = new UIConfirmationBoxManager(); UIConfirmationBoxManager.MainMenuInstance=menu;
        menu.CurrentBox.transform.SetParent(home,false);
        Record(menu,source); menu.Show(); surface.Tick();
        Check(ReferenceEquals(menu.CurrentBox.transform.parent,CanvasConversion.Last!.HostRect)
            && CanvasConversion.ActivePanels.Count==2,
            "MainMenuInstance confirmations must also reuse a proved containing panel");
        // Two managers have independent native boxes; one cannot evict another's request.
        Record(manager,source); manager.Show(); surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(menu.CurrentBox.Window)
            && ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "independent native managers must preserve both exact confirmation owners");
        surface.Shutdown();
        Check(ReferenceEquals(menu.CurrentBox.transform.parent,home)
            && ReferenceEquals(manager.CurrentBox.transform.parent,home)
            && menu.CurrentBox.IsOpen && manager.CurrentBox.IsOpen,
            "shutdown must restore both originals without losing native callbacks");

        (surface,manager,source,panel,home)=Setup();
        manager.CurrentBox.gameObject.scene=2;
        Record(manager,source); manager.Show(); surface.Tick();
        // Build728 user request: level-up confirmation belongs in the same physical
        // window. Keep its original persistent scene via an owner-aligned capture.
        Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window) && CanvasConversion.Converts==2,
            "persistent level-up dialog uses a safe owner-aligned capture instead of standalone placement");
        Check(CanvasConversion.Last!.ModLayerEnabled,"persistent confirmation admits isolated filtered capture and scoped blur treatment");
        Check(CanvasConversion.Last!.HideBackground,"persistent confirmation removes only full-frame backdrops before reveal and allocation");
        Check(CanvasConversion.Last!.BaseSortingOrder==panel.BaseSortingOrder+1,"persistent confirmation starts above its owner inside the native depth tie");
        Check(manager.CurrentBox.gameObject.scene==2
            && !manager.CurrentBox.transform.IsChildOf(source.transform),
            "persistent level-up dialog never inherits the scene-owned producer lifetime");
        Check(CanvasConversion.Last!.HostRect.position.z==source.transform.position.z-32
            && CanvasConversion.Last.HostRect.localScale.x==source.transform.lossyScale.x,
            "persistent member copies only accepted owner world geometry");
        source.gameObject.activeSelf=false;surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)&&manager.CurrentBox.IsOpen
            &&manager.CurrentBox.gameObject.scene==2,
            "lost level-up owner restores persistent presentation without native cancellation");
        surface.Shutdown();

        (surface,manager,source,panel,home)=Setup();
        manager.CurrentBox.gameObject.scene=2;
        Record(manager,source);manager.Show();surface.Tick();
        var persistent=CanvasConversion.Last!;
        var persistentRect=(RectTransform)manager.CurrentBox.transform;
        manager.CurrentBox.IsOpen=false;persistentRect.RejectReparent=true;
        ConfirmationOnOwner.Release(manager.CurrentBox);
        Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)&&persistentRect.IsChildOf(persistent.HostRect),
            "refused persistent detach retains its original subtree and restoration record after registry removal");
        persistentRect.RejectReparent=false;ConfirmationOnOwner.Release(manager.CurrentBox);
        Check(persistent.IsAlive&&!ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)&&ReferenceEquals(persistentRect.parent,home),
            "successful persistent release keeps the native target alive while retiring its conversion seat");
        int persistentReleases=CanvasConversion.Releases;
        for(int i=0;i<50;i++)surface.Tick();
        Check(CanvasConversion.Releases==persistentReleases,
            "a closed persistent confirmation is not released again on every frame");
        int persistentConverts=CanvasConversion.Converts;Record(manager,source);manager.Show();surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)&&CanvasConversion.Converts==persistentConverts+1,
            "a normally retired persistent confirmation can acquire a fresh presentation on native reopen");
        surface.Shutdown();

        (surface,manager,source,panel,home)=Setup();
        var other = new GameObject("other owner"); other.AddComponent<UIWindow>();
        var perk = other.AddComponent<UIPerksInventory>();
        CanvasConversion.Convert((RectTransform)other.transform,"perks",1);
        Record(manager,source); manager.Show(); surface.Tick();
        Record(manager,perk); surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)
            && CanvasConversion.Last!.HostRect.position.z==perk.transform.position.z-32,
            "new producer request must replace old pooled ownership immediately");
        Record(manager,null); manager.Show(); surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "unowned reuse of a pooled dialog must retain standalone fallback instead of stale level-up ownership");
        // Every reviewed native producer is a real containing source; neither its
        // CLR type nor the pooled box's persistent scene changes ownership.
        foreach (int dialogScene in new[] { 1, 2 })
        foreach (Func<GameObject, Component> create in new Func<GameObject, Component>[] {
            go => go.AddComponent<UIPerksInventory>(), go => go.AddComponent<UIPartyItemInventoryDisplay>(),
            go => go.AddComponent<UIAdventurePartyAssemblyRoster>(), go => go.AddComponent<UIOptionsWindow>(),
            go => go.AddComponent<ESCMenu>(), go => go.AddComponent<UIMapEscMenu>(),
            go => go.AddComponent<UIMultiplayerEscSubmenu>(), go => go.AddComponent<UIScenarioEscMenu>(),
            go => go.AddComponent<UIResetLevelUpWindow>(), go => go.AddComponent<UIShopItemInventory>(),
            go => go.AddComponent<LevelEditorMainMenu>(), go => go.AddComponent<UICreateModView>(),
            go => go.AddComponent<UIExportModdingManager>(), go => go.AddComponent<UIRulesetsWindow>(),
            go => go.AddComponent<PersistentAbilitiesUI>() })
        {
            surface.Shutdown();
            (surface, manager, source, panel, home) = Setup();
            var producerRoot = new GameObject("reviewed producer");
            producerRoot.transform.SetParent(panel.Target, false);
            var producerWindow = producerRoot.AddComponent<UIWindow>();
            var producer = create(producerRoot);
            manager.CurrentBox.gameObject.scene = dialogScene;
            Record(manager, producer); manager.Show(); surface.Tick();
            var member = CanvasConversion.Last!;
            Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)
                && !ReferenceEquals(member,panel),
                "every exact native producer owns its foreground member in either scene");
            Check(member.HostGo.scene == dialogScene && member.HideBackground && member.ModLayerEnabled
                && member.BaseSortingOrder == panel.BaseSortingOrder+1,
                "same and persistent native confirmations share safe foreground presentation");
            producer.transform.position = new Vector3(43,18,-7);
            producer.transform.localScale = new Vector3(.6f,.6f,.6f);
            producerWindow.IsOpen = false; surface.Tick();
            Check(member.HostRect.position.x == 43 && member.HostRect.position.z == -39
                && member.HostRect.localScale.x == .6f && manager.CurrentBox.IsOpen,
                "producer dimming never detaches the accepted source-relative pose");
            manager.Hide(); surface.Tick();
            Check(ReferenceEquals(manager.CurrentBox.transform.parent,home)
                && manager.CurrentBox.gameObject.scene == dialogScene
                && !ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
                "every producer restores its original pooled home without inheriting scene lifetime");
        }
        surface.Shutdown();
        (surface, manager, source, panel, home) = Setup();
        var unavailable = new GameObject("native producer without a converted owner").AddComponent<UIPerksInventory>();
        Record(manager, unavailable); manager.Show(); surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "a source without a real containing converted window retains native standalone continuation");
        var cycle = new GameObject("invalid containing self"); cycle.transform.SetParent(manager.CurrentBox.transform,false);
        var invalid = cycle.AddComponent<UIPerksInventory>();
        Record(manager,invalid); surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "a confirmation cannot become its own containing owner through a producer descendant");
        // Nested native call scopes unwind even when the original action throws.
        var prefix=typeof(ConfirmationProducerScopePatch).GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!;
        var finalizer=typeof(ConfirmationProducerScopePatch).GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static)!;
        ConfirmationOnOwner.CurrentScope=source;
        object?[] args={perk,null}; prefix.Invoke(null,args);
        var error=new InvalidOperationException("native");
        Check(ReferenceEquals(ConfirmationOnOwner.CurrentScope,perk),"producer prefix must record exact native instance");
        Check(ReferenceEquals(finalizer.Invoke(null,new object?[]{error,args[1]}),error)
            && ReferenceEquals(ConfirmationOnOwner.CurrentScope,source),
            "producer finalizer must restore previous scope and retain native exceptions");
        var targets=(IEnumerable<MethodBase>)typeof(ConfirmationProducerScopePatch)
            .GetMethod("TargetMethods",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
        int count=0; foreach(var target in targets) { Check(target!=null,"every reviewed producer patch target must exist");count++; }
        Check(count>=28,"all baseline in-game producer methods remain covered alongside the audited native navigation producers");
        var requests=(IEnumerable<MethodBase>)typeof(ConfirmationRequestOriginPatch)
            .GetMethod("TargetMethods",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,null)!;
        count=0; foreach(var target in requests) { Check(target!=null,"every native generic request patch target must exist");count++; }
        Check(count==5,"all five native generic manager request paths must retain provenance");
        surface.Shutdown();
        ProducerCases742.Run();
    }
}
