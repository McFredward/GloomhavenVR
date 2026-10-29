using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;
using UnityEngine.UI;

internal static class ProducerCases742
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
    private static MethodInfo Method(Type owner, string name) => owner.GetMethod(name,Hidden)!;
    private static void Check(bool value,string why) => Program.Check(value,why);
    private static void Record(UIConfirmationBoxManager manager, Component source)
    { var previous=ConfirmationOnOwner.Begin(source);ConfirmationOnOwner.Record(manager);ConfirmationOnOwner.CurrentScope=previous; }

    internal static void Run()
    {
        CanvasConversion.Reset();WorldUIConfig.ConversionActive=true;
        VRModeStateMachine.TableInFrontOfPlayer=true;FlatScreen.ManualScreenActive=false;
        var root=new GameObject("native owner");var panel=CanvasConversion.Convert((RectTransform)root.transform,"owner",1)!;
        var source=root.AddComponent<UIPerksInventory>();root.AddComponent<UIWindow>();
        var home=new GameObject("pooled native home").transform;
        var manager=new UIConfirmationBoxManager();manager.CurrentBox.transform.SetParent(home,false);
        Singleton<UIConfirmationBoxManager>.Instance=manager;
        var surface=new DialogSurface();surface.Tick();
        var itemRoot=new GameObject("original native item confirmation");itemRoot.transform.SetParent(home,false);
        itemRoot.scene=2;var item=itemRoot.AddComponent<UIItemConfirmationBox>();var itemWindow=itemRoot.AddComponent<UIWindow>();
        Singleton<UIItemConfirmationBox>.Instance=item;
        var previous=ConfirmationOnOwner.Begin(source);
        Method(typeof(ItemConfirmationRequestOriginPatch),"Prefix").Invoke(null,new object[]{item});
        ConfirmationOnOwner.CurrentScope=previous;item.IsActive=itemWindow.IsVisible=true;
        TownServicePresentation.Owned=true;surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(itemWindow)&&ReferenceEquals(itemRoot.transform.parent,home),
            "immersive item requests never acquire a generic member or change native home");
        TownServicePresentation.Owned=false;
        previous=ConfirmationOnOwner.Begin(source);Method(typeof(ItemConfirmationRequestOriginPatch),"Prefix").Invoke(null,new object[]{item});ConfirmationOnOwner.CurrentScope=previous;
        MapDialogSeat.AllowTransfer=false;surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(itemWindow),"refused legacy item restoration prevents a new producer snapshot");
        MapDialogSeat.AllowTransfer=true;
        surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(itemWindow)&&itemRoot.scene==2&&CanvasConversion.Last!.HideBackground,
            "native item confirmation joins its exact producer and retains original persistence and small foreground");
        var itemRect=(RectTransform)itemRoot.transform;itemRect.RejectReparent=true;
        Check(!ModalFallback.ReleaseForComposite(itemWindow)&&ConfirmationOnOwner.IsOwned(itemWindow),
            "external composite cannot snapshot a producer whose native home restoration was rejected");
        itemRect.RejectReparent=false;
        Check(ModalFallback.ReleaseForComposite(itemWindow)&&!ConfirmationOnOwner.IsOwned(itemWindow)
            &&ReferenceEquals(itemRect.parent,home),"external composite receives the verified native home after complete producer retirement");
        surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(itemWindow),"external transfer clears old exact producer provenance until another native request");
        previous=ConfirmationOnOwner.Begin(source);Method(typeof(ItemConfirmationRequestOriginPatch),"Prefix").Invoke(null,new object[]{item});ConfirmationOnOwner.CurrentScope=previous;
        surface.Tick();
        Record(manager,source);manager.Show();surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(itemWindow)&&ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "item and generic pools retain independent exact owners");
        manager.Hide();surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(itemWindow),"closing a generic pool cannot release an independent native item prompt");
        item.IsActive=itemWindow.IsVisible=false;surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(itemWindow)&&ReferenceEquals(itemRoot.transform.parent,home)&&itemRoot.scene==2,
            "closing an item prompt restores native home without cancelling its action");
        var targets=(IEnumerable<MethodBase>)Method(typeof(DeferredConfirmationProducerScopePatch),"TargetMethods").Invoke(null,null)!;
        int callbacks=0;foreach(var target in targets){Check(target!=null,"every reviewed native deferred callback has a captured original owner");callbacks++;}
        Check(callbacks==4,"all four deferred validation entry points retain exact captured sources");
        var deferred=new GameObject("original async producer");deferred.transform.SetParent(root.transform,false);
        deferred.AddComponent<UIWindow>();var mods=deferred.AddComponent<UIListModsView>();mods.ValidateMod();
        var decoy=new GameObject("later unrelated focus").AddComponent<UIOptionsWindow>();
        ConfirmationOnOwner.CurrentScope=decoy;
        object?[] prefixArgs={mods.Completion!.Target,null};
        Method(typeof(DeferredConfirmationProducerScopePatch),"Prefix").Invoke(null,prefixArgs);
        mods.Completion();ConfirmationOnOwner.Record(manager);manager.Show();
        Method(typeof(DeferredConfirmationProducerScopePatch),"Finalizer").Invoke(null,new object?[]{null,prefixArgs[1]});
        surface.Tick();
        Check(mods.Completed==11&&ReferenceEquals(ConfirmationOnOwner.CurrentScope,decoy)
            &&ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)
            &&CanvasConversion.Last!.HostRect.position.z==deferred.transform.position.z-32,
            "deferred native validation uses its captured owner instead of later focus and restores ambient scope");
        manager.Hide();surface.Tick();

        var effects=root.AddComponent<PersistentAbilitiesUI>();int clicks=0;
        var sprite=new SpriteMemoryManagement.ReferenceToSprite();
        var nativeAction=new Action(()=>{clicks++;ConfirmationOnOwner.Record(manager);manager.Show();});
        var passive=new Tuple<SpriteMemoryManagement.ReferenceToSprite,string,string,Action,bool>(sprite,"passive","details",null!,false);
        var actions=new List<Tuple<SpriteMemoryManagement.ReferenceToSprite,string,string,Action,bool>> {
            new(sprite,"native title","native details",nativeAction,true),passive };
        Method(typeof(PersistentConfirmationActionScopePatch),"Postfix").Invoke(null,new object[]{effects,actions});
        Check(clicks==0&&ReferenceEquals(actions[0].Item1,sprite)&&actions[0].Item2=="native title"
            &&actions[0].Item3=="native details"&&actions[0].Item5&&ReferenceEquals(actions[1],passive),
            "native action decoration preserves all original effect content and never executes at construction");
        actions[0].Item4();surface.Tick();
        Check(clicks==1&&ReferenceEquals(ConfirmationOnOwner.CurrentScope,decoy)
            &&ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "actor-only deferred tooltip actions retain the original UI source without bypassing the click");
        manager.Hide();surface.Tick();
        var thrown=new InvalidOperationException("native callback");
        actions=new(){new(sprite,"title","detail",()=>throw thrown,true)};
        Method(typeof(PersistentConfirmationActionScopePatch),"Postfix").Invoke(null,new object[]{effects,actions});
        Exception? caught=null;try{actions[0].Item4();}catch(Exception error){caught=error;}
        Check(ReferenceEquals(caught,thrown)&&ReferenceEquals(ConfirmationOnOwner.CurrentScope,decoy),
            "deferred original exceptions propagate while restoring the previous producer scope");
        surface.Shutdown();

        // The menu's captured-camera relation is an explicit surrounding port here;
        // the root integration exercises its actual native camera and final pixels.
        CanvasConversion.Reset();VRModeStateMachine.TableInFrontOfPlayer=false;MenuWorldContext.IsReady=true;
        var menuRoot=new GameObject("mod world menu wrapper");var menuPanel=new ConvertedPanel {Target=(RectTransform)menuRoot.transform,HostGo=menuRoot};
        FlatScreen.MenuOwner=menuPanel;var nativeCanvas=new GameObject("actual captured native menu");FlatScreen.MenuSource=nativeCanvas.transform;
        var options=new GameObject("actual native menu options");options.transform.SetParent(nativeCanvas.transform,false);
        var menuSource=options.AddComponent<UIOptionsWindow>();options.AddComponent<UIWindow>();
        manager=new UIConfirmationBoxManager();manager.CurrentBox.transform.SetParent(home,false);
        Singleton<UIConfirmationBoxManager>.Instance=manager;surface=new DialogSurface();
        Record(manager,menuSource);manager.Show();surface.Tick();
        Check(ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window),
            "ready menu confirmation uses its proved capture wrapper without a fake table or converted ancestor");
        menuPanel.HostRect.position=new Vector3(13,7,41);menuPanel.HostRect.localScale=new Vector3(.2f,.2f,.2f);surface.Tick();
        Check(CanvasConversion.Last!.HostRect.position.x==13&&CanvasConversion.Last.HostRect.position.z==9
            &&CanvasConversion.Last.HostRect.localScale.x==.2f,
            "captured menu confirmation follows only accepted manual window geometry");
        int settledConverts=CanvasConversion.Converts;
        for(int frame=0;frame<20;frame++)surface.Tick();
        Check(CanvasConversion.Converts==settledConverts,
            "settled menu ownership retains its live wrapper without per-frame release and recapture");
        MenuWorldContext.IsReady=false;surface.Tick();
        Check(!ConfirmationOnOwner.IsOwned(manager.CurrentBox.Window)&&manager.CurrentBox.IsOpen
            &&ReferenceEquals(manager.CurrentBox.transform.parent,home),
            "menu wrapper retirement returns the native prompt without accepting or cancelling it");
        surface.Shutdown();CanvasConversion.Reset();VRModeStateMachine.TableInFrontOfPlayer=true;
    }
}
