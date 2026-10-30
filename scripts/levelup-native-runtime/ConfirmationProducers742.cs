extern alias native;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    private static IEnumerator ConfirmationProducers742(Camera head,RectTransform ownerRoot,UILevelUpWindow owner,string output,Harmony harmony)
    {
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
        Assembly game=typeof(native::ExtendedButton).Assembly;var metadata=new List<string>();
        var direct=(IEnumerable<MethodBase>)typeof(ConfirmationProducerScopePatch).GetMethod("TargetMethods",flags)!.Invoke(null,null)!;
        foreach(MethodBase fixture in direct)
        {
            Type original=game.GetType(fixture.DeclaringType!.Name,true)!;int found=0;
            foreach(var method in original.GetMethods(flags|BindingFlags.DeclaredOnly))if(method.Name==fixture.Name)found++;
            Check(found>0,"each scoped producer exists in the original read-only game assembly");
            metadata.Add(original.FullName+"."+fixture.Name+" native overloads="+found);
        }
        foreach(var row in new[]{("UIRulesetsWindow","CompileRuleset"),("UIListModsView","ValidateMod"),
            ("UIListModsView","OpenLevelEditor"),("UIRulesetView","ValidateMod")})
        {
            Type original=game.GetType(row.Item1,true)!;
            var callbacks=(IEnumerable<MethodBase>)typeof(DeferredConfirmationProducerScopePatch).GetMethod("Callbacks",flags)!
                .Invoke(null,new object[]{original,new[]{row.Item2}})!;
            int found=0;foreach(MethodBase callback in callbacks){metadata.Add(callback.ToString()+" owner="+original.FullName);found++;}
            Check(found>0,"every deferred producer discovers its original native captured-owner callback");
        }
        Check(game.GetType("UIItemConfirmationBox",true)!.GetMethod("ShowConfirmation",flags)!=null,
            "original item confirmation has its separately scoped native Show entry");
        File.WriteAllLines(Path.Combine(output,"native-confirmation-producer-metadata742.txt"),metadata);
        foreach(Type patch in new[]{typeof(ConfirmationProducerScopePatch),typeof(ConfirmationRequestOriginPatch),
            typeof(DeferredConfirmationProducerScopePatch),typeof(ItemConfirmationRequestOriginPatch),typeof(PersistentConfirmationActionScopePatch),typeof(MapItemConfirmationRequestObserver)})
            harmony.CreateClassProcessor(patch).Patch();

        UIConfirmationBoxManager? previousManager=Singleton<UIConfirmationBoxManager>.Instance;
        var home=Rect("producer proof pooled home",null,1920,1080);Object.DontDestroyOnLoad(home.gameObject);
        var prompt=Rect("producer proof native prompt",home,1920,1080);var box=prompt.gameObject.AddComponent<ConfirmationBox>();
        var window=prompt.gameObject.AddComponent<UIWindow>();box.IsOpen=window.IsVisible=false;
        var manager=new UIConfirmationBoxManager{CurrentBox=box};Singleton<UIConfirmationBoxManager>.Instance=manager;
        var origin=Rect("original asynchronous modding window",ownerRoot,1620,1080);origin.anchoredPosition=new Vector2(41,-19);
        origin.gameObject.AddComponent<UIWindow>();var mods=origin.gameObject.AddComponent<UIListModsView>();mods.Manager=manager;
        var decoy=Rect("later unrelated focus",null,1620,1080).gameObject.AddComponent<UIOptionsWindow>();
        ConfirmationOnOwner.CurrentScope=decoy;
        mods.ValidateMod();Check(!box.IsOpen&&mods.Completed==0,"native async request creation never opens a dialog or executes its completion");
        mods.Completion!();ConfirmationOnOwner.Tick();
        Check(mods.Completed==11&&ReferenceEquals(ConfirmationOnOwner.CurrentScope,decoy)&&ConfirmationOnOwner.IsOwned(window),
            "native deferred callback uses its captured original producer and restores ambient scope");
        var member=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,prompt))!;
        Check(Vector3.Distance(member.HostRect.position,origin.TransformPoint(new Vector3(0,0,-32)))<.00001f,
            "deferred native confirmation belongs to the actual producer subwindow");
        box.IsOpen=window.IsVisible=false;ConfirmationOnOwner.Tick();

        var effects=origin.gameObject.AddComponent<PersistentAbilitiesUI>();int actions=0;
        effects.Effects.Add(new Tuple<SpriteMemoryManagement.ReferenceToSprite,string,string,Action,bool>(
            new SpriteMemoryManagement.ReferenceToSprite(),"original title","original body",()=>{actions++;manager.ShowGenericConfirmation();},true));
        var decorated=effects.GetCustomEffects();Check(actions==0&&!box.IsOpen,"native actor-only action remains deferred until the original click");
        decorated[0].Item4();ConfirmationOnOwner.Tick();
        Check(actions==1&&ReferenceEquals(ConfirmationOnOwner.CurrentScope,decoy)&&ConfirmationOnOwner.IsOwned(window),
            "native actor-only tooltip action retains its producing UI and original action");
        box.IsOpen=window.IsVisible=false;ConfirmationOnOwner.Tick();

        var itemRoot=Rect("original item pool",home,1920,1080);var item=itemRoot.gameObject.AddComponent<UIItemConfirmationBox>();
        var itemWindow=itemRoot.gameObject.AddComponent<UIWindow>();item.IsActive=itemWindow.IsVisible=false;
        Singleton<UIItemConfirmationBox>.Instance=item;
        var shop=origin.gameObject.AddComponent<UIShopItemInventory>();shop.Item=item;
        AccessTools.Method(typeof(UIShopItemInventory),"OnSelectedItemBuy").Invoke(shop,null);ConfirmationOnOwner.Tick();
        Check(ConfirmationOnOwner.IsOwned(itemWindow)&&ReferenceEquals(ConfirmationOnOwner.CurrentScope,decoy)
            &&item.gameObject.scene.name=="DontDestroyOnLoad",
            "native item Show records its exact shop producer and keeps the pooled original persistent");
        item.IsActive=itemWindow.IsVisible=false;ConfirmationOnOwner.Tick();
        Check(itemRoot.parent==home&&!ConfirmationOnOwner.IsOwned(itemWindow),"native item close restores its original home without dispatching an action");
        Singleton<UIItemConfirmationBox>.Instance=null;

        var menuPanel=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,ownerRoot))!;
        var nativeCanvas=Rect("actual captured menu canvas",null,1920,1080);var menuSource=nativeCanvas.gameObject.AddComponent<UIOptionsWindow>();
        FlatScreen.MenuOwner=menuPanel;FlatScreen.MenuSource=nativeCanvas;MenuWorldContext.IsReady=true;
        VRModeStateMachine.TableInFrontOfPlayer=false;CanvasConversion.ActivePanels.Remove(menuPanel);
        // Scope the exact native source without a table or an ancestor conversion.
        var previous=ConfirmationOnOwner.Begin(menuSource);manager.ShowGenericConfirmation();ConfirmationOnOwner.CurrentScope=previous;
        ConfirmationOnOwner.Tick();
        Check(ConfirmationOnOwner.IsOwned(window),"ready menu confirmation uses its proved capture wrapper without a fake table or converted ancestor");
        int captures=CanvasConversion.Converts;for(int frame=0;frame<20;frame++)ConfirmationOnOwner.Tick();
        Check(CanvasConversion.Converts==captures,"settled menu ownership retains its live wrapper without per-frame release and recapture");
        MenuWorldContext.IsReady=false;ConfirmationOnOwner.Tick();
        Check(box.IsOpen&&prompt.parent==home&&!ConfirmationOnOwner.IsOwned(window),
            "retiring the menu wrapper restores native confirmation authority without cancelling it");
        FlatScreen.MenuOwner=null;FlatScreen.MenuSource=null;VRModeStateMachine.TableInFrontOfPlayer=true;
        CanvasConversion.ActivePanels.Add(menuPanel);box.IsOpen=window.IsVisible=false;
        ConfirmationOnOwner.CurrentScope=null;ConfirmationOnOwner.Tick();
        Singleton<UIConfirmationBoxManager>.Instance=previousManager;
        Object.DestroyImmediate(nativeCanvas.gameObject);Object.DestroyImmediate(decoy.gameObject);
        Object.DestroyImmediate(origin.gameObject);Object.DestroyImmediate(home.gameObject);
        yield return null;
    }
}
