using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

public static partial class LevelUpNativeProgram
{
    private static object ItemSeat742()=>typeof(MapDialogSeat).GetField("ItemSeat",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    private static FieldInfo Field742(object owner,string name)=>owner.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance)!;
    private static IEnumerator ConfirmationOwnershipInterop742(RectTransform ownerRoot,string output)
    {
        MapDialogSeat.Reset();TownServicePresentation.Owned=null;
        var home=Rect("actual original item home",ownerRoot,1920,1080);
        var merchant=Rect("actual merchant window",null,1920,1080);var merchantWindow=merchant.gameObject.AddComponent<UIWindow>();
        var merchantPanel=CanvasConversion.Convert(merchant,"merchant ownership witness")!;
        GuildmasterDestinations.Windows[EGuildmasterMode.Merchant]=merchantWindow;GuildmasterDestinations.Mode=EGuildmasterMode.Merchant;
        var itemRoot=Rect("actual pooled item original",home,1920,1080);itemRoot.anchoredPosition=new Vector2(19,27);
        var item=itemRoot.gameObject.AddComponent<UIItemConfirmationBox>();var window=itemRoot.gameObject.AddComponent<UIWindow>();
        Singleton<UIItemConfirmationBox>.Instance=item;
        var producer=merchant.gameObject.AddComponent<UIShopItemInventory>();producer.Item=item;
        ConfirmationOnOwner.CurrentScope=null;item.ShowConfirmation(BoxConfirmationType.Buy);MapDialogSeat.Tick();
        Check(itemRoot.parent==merchant,"actual legacy map coordinator first seats an unscoped merchant prompt");
        var legacy=ItemSeat742();var originalHome=Field742(legacy,"Home").GetValue(legacy);
        var invalidHome=Rect("deliberately rejected cyclic native home",itemRoot,20,20);
        Field742(legacy,"Home").SetValue(legacy,invalidHome);
        var previous=ConfirmationOnOwner.Begin(producer);item.ShowConfirmation(BoxConfirmationType.Buy);ConfirmationOnOwner.CurrentScope=previous;
        int conversions=CanvasConversion.Converts;ConfirmationOnOwner.Tick();
        Check(!ConfirmationOnOwner.IsOwned(window)&&itemRoot.parent==merchant&&CanvasConversion.Converts==conversions
            &&ReferenceEquals(Field742(legacy,"Parked").GetValue(legacy),itemRoot),
            "rejected legacy native home retains the old lease and prevents a competing producer snapshot");
        Field742(legacy,"Home").SetValue(legacy,originalHome);ConfirmationOnOwner.Tick();
        Check(ConfirmationOnOwner.IsOwned(window)&&Field742(legacy,"Parked").GetValue(legacy)==null,
            "verified legacy home restoration retires its writer before exact producer member adoption");
        var member=CanvasConversion.ActivePanels.Find(p=>ReferenceEquals(p.Target,itemRoot))!;
        Transform accepted=itemRoot.parent;Vector3 acceptedPosition=itemRoot.localPosition;
        conversions=CanvasConversion.Converts;
        for(int frame=0;frame<25;frame++){MapDialogSeat.Tick();ConfirmationOnOwner.Tick();}
        Check(itemRoot.parent==accepted&&itemRoot.localPosition==acceptedPosition&&CanvasConversion.Converts==conversions,
            "both actual coordinators retain one settled item owner without transform or capture churn");

        // The separate enhancement observer keeps its original native host Hide
        // edge while an item is owned by the new coordinator.
        var temple=Rect("native temple host",null,1920,1080);var templeWindow=temple.gameObject.AddComponent<UIWindow>();
        var templePanel=CanvasConversion.Convert(temple,"temple observer witness")!;
        GuildmasterDestinations.Windows[EGuildmasterMode.Temple]=templeWindow;GuildmasterDestinations.Mode=EGuildmasterMode.Temple;
        var enhancementRoot=Rect("native enhancement confirmation",home,1920,1080);
        var enhancement=enhancementRoot.gameObject.AddComponent<UIEnhancementConfirmationBox>();
        var enhancementWindow=enhancementRoot.gameObject.AddComponent<UIWindow>();int hidden=0;enhancementWindow.OnHide=()=>hidden++;
        Singleton<UIEnhancementConfirmationBox>.Instance=enhancement;MapDialogSeat.Tick();templeWindow.Hide();
        Check(hidden==1&&!enhancementWindow.IsOpen&&ConfirmationOnOwner.IsOwned(window),
            "item handoff preserves the independent enhancement host-close continuation exactly once");
        MapDialogSeat.Tick();Singleton<UIEnhancementConfirmationBox>.Instance=null;

        // Invoke the exact production common transfer body used by the palm.
        Check(ModalFallback.ReleaseForComposite(window)&&!ConfirmationOnOwner.IsOwned(window)&&itemRoot.parent==home,
            "actual common transfer retires the complete member lease before the palm records native home");
        ConfirmationOnOwner.Tick();
        Check(!ConfirmationOnOwner.IsOwned(window),"external transfer clears old exact producer provenance until another native request");
        var palm=Rect("external palm mask",null,1920,1080);Transform palmHome=itemRoot.parent;
        TownServicePresentation.Owned=window;itemRoot.SetParent(palm,false);
        previous=ConfirmationOnOwner.Begin(producer);item.ShowConfirmation(BoxConfirmationType.Buy);ConfirmationOnOwner.CurrentScope=previous;
        Check(!MapDialogSeat.ReleaseForConfirmationOwner(window),"legacy handoff refuses an original currently owned by the immersive native presentation");
        int externalTransfers=ModalFallback.CompositeTransfers;
        for(int frame=0;frame<25;frame++){ConfirmationOnOwner.Tick();MapDialogSeat.Tick();}
        Check(ModalFallback.CompositeTransfers==externalTransfers,
            "immersive native requests bypass generic transfer machinery before either coordinator can write");
        Check(itemRoot.parent==palm&&palmHome==home&&!ConfirmationOnOwner.IsOwned(window),
            "actual old and new ticks preserve an immersive palm snapshot without reclaiming its original");
        itemRoot.SetParent(palmHome,false);TownServicePresentation.Owned=null;
        // Stop the legacy merchant discriminator author from being the new author
        // here; the stale exact-origin request itself must have been retired too.
        item.IsActive=false;window.IsVisible=false;MapDialogSeat.Tick();
        item.IsActive=window.IsVisible=true;ConfirmationOnOwner.Tick();
        Check(!ConfirmationOnOwner.IsOwned(window),"external transfer retires stale producer provenance before a later unscoped native opening");
        item.IsActive=window.IsVisible=false;ConfirmationOnOwner.Tick();

        // The producer member's own restoration can also be rejected. Its claim
        // must stop the common transfer before any palm/native-home snapshot.
        previous=ConfirmationOnOwner.Begin(producer);item.ShowConfirmation(BoxConfirmationType.General);ConfirmationOnOwner.CurrentScope=previous;
        ConfirmationOnOwner.Tick();
        var seats=(System.Collections.IList)typeof(ConfirmationOnOwner).GetField("Seats",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        object current=seats[seats.Count-1]!;object? producerHome=Field742(current,"Home").GetValue(current);
        invalidHome.SetParent(itemRoot,false);Field742(current,"Home").SetValue(current,invalidHome);
        Check(!ModalFallback.ReleaseForComposite(window)&&ConfirmationOnOwner.IsOwned(window),
            "rejected producer native home blocks common transfer and retains the exact restoration lease");
        Field742(current,"Home").SetValue(current,producerHome);
        Check(ModalFallback.ReleaseForComposite(window)&&itemRoot.parent==home&&!ConfirmationOnOwner.IsOwned(window),
            "a later verified native-home return completes the same external transfer safely");
        item.IsActive=window.IsVisible=false;ConfirmationOnOwner.Tick();
        int beforeDestroyed=seats.Count;
        var destroyedRoot=Rect("scene-owned original destroyed before its next tick",home,1920,1080);
        var destroyedItem=destroyedRoot.gameObject.AddComponent<UIItemConfirmationBox>();var destroyedWindow=destroyedRoot.gameObject.AddComponent<UIWindow>();
        Singleton<UIItemConfirmationBox>.Instance=destroyedItem;
        previous=ConfirmationOnOwner.Begin(producer);destroyedItem.ShowConfirmation();ConfirmationOnOwner.CurrentScope=previous;ConfirmationOnOwner.Tick();
        Check(seats.Count==beforeDestroyed+1,"scene-owned original acquires a producer lease before destruction");
        Object.DestroyImmediate(destroyedRoot.gameObject);bool safelyRetired=true;
        try { ConfirmationOnOwner.Tick(); } catch(MissingReferenceException) { safelyRetired=false; }
        Check(safelyRetired&&seats.Count==beforeDestroyed,"destroyed native originals retire without consulting a foreign owner through a missing Unity object");
        Check(!ConfirmationOnOwner.ReleaseForExternalOwner(destroyedWindow),"a destroyed native original cannot be borrowed by an external presentation owner");
        MapDialogSeat.Reset();
        Singleton<UIItemConfirmationBox>.Instance=null;TownServicePresentation.Owned=null;GuildmasterDestinations.Windows.Clear();
        CanvasConversion.Release(merchantPanel);CanvasConversion.Release(templePanel);
        Object.DestroyImmediate(merchant.gameObject);Object.DestroyImmediate(temple.gameObject);
        Object.DestroyImmediate(palm.gameObject);Object.DestroyImmediate(home.gameObject);
        File.WriteAllText(Path.Combine(output,"confirmation-two-owner742-limits.txt"),
            "Whole production ConfirmationOnOwner, MapDialogSeat and common ReleaseForComposite execute with real Unity transforms, failed cyclic reparenting and Harmony Show provenance. Native game controller fields/events and palm container are explicit surrounding inputs; the containing flat-confirmation-lifecycle suite separately executes the unchanged native Show/Hide/outcome methods and real zero-alpha window mask.");
        yield return null;
    }
}
