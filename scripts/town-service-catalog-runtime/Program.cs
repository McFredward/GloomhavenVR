using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
public static class InteractionProgram
{
    private static int assertions;
    private static void Check(bool value,string message){assertions++;if(!value)throw new Exception(message);}
    private static void Set(object o,string field,object value)=>o.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    private static RectTransform Rect(string name,Transform parent)
    {var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.sizeDelta=new Vector2(500,50);return r;}
    private static void Census(TownServiceCatalog c)=>typeof(TownServiceCatalog).GetMethod("RefreshRows",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(c,null);
    private static void Advance(TownServiceMerchantDrawer drawer,float amount){Set(drawer,"_amount",amount);Set(drawer,"_target",amount);drawer.Tick(1f);}
    public static int Run()
    {
        assertions=0;var root=new GameObject("MerchantFixture");var events=new GameObject("Events",typeof(EventSystem));
        var prefab=new GameObject("MerchantPrefab");var counter=new GameObject("Counter");counter.transform.SetParent(prefab.transform,false);
        var plank=GameObject.CreatePrimitive(PrimitiveType.Cube);plank.name="SurfacePlank0";plank.transform.SetParent(counter.transform,false);
        TownServiceAssets.Merchant=prefab;
        var inventory=Rect("Inventory",root.transform).gameObject.AddComponent<UIShopItemInventory>();
        inventory.itemsCanvasGroup=inventory.gameObject.AddComponent<CanvasGroup>();
        inventory.itemTooltip=Rect("OriginalDetail",inventory.transform).gameObject.AddComponent<UIPartyItemInventoryTooltip>();
        inventory.buyTab=Rect("Buy",inventory.transform).gameObject.AddComponent<Tab>();inventory.buyTab.Changed=()=>inventory.Selling=false;
        inventory.sellTab=Rect("Sell",inventory.transform).gameObject.AddComponent<Tab>();inventory.sellTab.Changed=()=>inventory.Selling=true;
        var slot=Rect("NativeSlot",root.transform).gameObject.AddComponent<UIShopItemSlot>();slot.Selectable=slot.gameObject.AddComponent<Button>();inventory.slotPrefab=slot;
        var confirmation=Rect("Confirmation",root.transform).gameObject.AddComponent<UIItemConfirmationBox>();confirmation.confirmButton=confirmation.gameObject.AddComponent<Button>();Singleton<UIItemConfirmationBox>.Instance=confirmation;
        var counts=new[]{20,24,37,17,19,44,3};int id=1;
        for(int category=0;category<counts.Length;category++)for(int n=0;n<counts[category];n++,id++)
        {
            var item=new CItem(id);item.YMLData.Slot=(CItem.EItemSlot)category;inventory.service.Buy.Add(item);
            var own=new CItem(id){NetworkID=(uint)(1000+id)};own.YMLData.Slot=item.YMLData.Slot;inventory.service.Sell.Add(own);
        }
        var anchor=new GameObject("CounterAnchor");anchor.transform.SetParent(root.transform,false);
        object context=new object();bool alive=true;
        var catalog=new TownServiceCatalog(inventory,anchor.transform,()=>context,()=>alive,anchor.transform);
        catalog.SetVisibility(1f);Census(catalog);catalog.Tick(1f);
        Check(catalog.Entries.Count==328,"all 164 stock and 164 owned entries persist without pagination");
        Check(catalog.Controls.Count==0,"no flat filter or page buttons");
        Check(catalog.Drawers.Count==12,"native five slot categories use six drawers per bank");
        Check(ObjectPool.Alive==328,"one physical original card per persistent entry");
        foreach(var e in catalog.Entries)
        {
            Check(!e.Sample.CanGrab,"closed opaque drawers prevent picking through cabinet");
            Check(!e.Exposed,"closed drawer contents excluded only when physically hidden");
            Check(e.CardUI.GetComponent<Image>().raycastTarget==false,"own card GUI cannot veto direct pickup");
            Check(e.BodyRoot!=null,"each item has physical body");
        }
        var drawer=catalog.Drawers[0];Advance(drawer,1f);
        Check(drawer.Root.localPosition.z<-.58f,"full pull clears native countertop back rows");
        foreach(var e in catalog.Entries)if(e.Drawer==drawer)
        {
            Check(e.Sample.CanGrab,"every exposed back card is independently inspectable");
            Check(e.Exposed,"all open drawer items publish to observers");
            Vector3 centre=anchor.transform.InverseTransformPoint(e.CardRoot.position);
            Check(centre.z<-.35f,"all open back card centres clear counter front");
        }
        var stable=catalog.Entries[0];var stableRoot=stable.CardRoot;
        Census(catalog);Check(catalog.Entries[0].CardRoot==stableRoot,"unchanged census retains physical identity");
        var added=new CItem(999);added.YMLData.Slot=CItem.EItemSlot.Head;inventory.service.Buy.Add(added);Census(catalog);
        Check(catalog.Entries.Count==329,"late unlock adds card without dropping old stock");
        Check(catalog.Entries[0].CardRoot==stableRoot,"late unlock preserves existing card transforms");
        Check(drawer.Accessible,"late unlock does not shut an open drawer");
        stable.Sample.IsMoving=true;Check(!catalog.CanRelocate,"held or returning sample prevents station relocation");
        drawer.Close();Check((float)typeof(TownServiceMerchantDrawer).GetField("_target",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(drawer)! == 1f,"drawer cannot close on held card");
        stable.Sample.IsMoving=false;
        Set(drawer,"_target",0f);Check(!catalog.CanRelocate,"moving drawer prevents workspace relocation");Advance(drawer,1f);
        Check(inventory.service.Commits==0,"inspection census and opening never spend gold");
        stable.Sample.IsHeld=true;stable.Sample.PickupSequence=1;catalog.LateTick();
        Check(inventory.itemTooltip.Shows==1&&inventory.itemTooltip.Service==inventory.service,"held inspection invokes native complete item detail with original discount service");
        Check(inventory.service.Commits==0,"native details never select or transact");
        Check(catalog.PreviewContent!=null,"native detail content available for observer mirror");
        stable.Sample.IsHeld=false;catalog.LateTick();Check(!inventory.itemTooltip.IsShown,"release retires attached inspection details");
        CItem buy=inventory.service.Buy[0];
        Check(TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"explicit drop dispatches native buy confirmation");
        Check(inventory.service.Commits==1,"explicit drop commits exactly once");
        inventory.service.Affordable=false;
        Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"unaffordable drop refused");
        inventory.service.Affordable=true;inventory.itemsCanvasGroup.interactable=false;
        Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"native multiplayer lock refused");inventory.itemsCanvasGroup.interactable=true;
        FFSNet.FFSNetwork.IsOnline=true;FFSNet.PlayerRegistry.MyPlayer=new FFSNet.NetworkPlayer{IsParticipant=false};
        Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"spectator cannot trade");FFSNet.FFSNetwork.IsOnline=false;
        confirmation.IsActive=true;Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"unrelated pending confirmation retained");confirmation.IsActive=false;
        bool current=true;confirmation.BeforeShow=()=>current=false;
        Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>current),"context race never confirms native callback");
        Check(!confirmation.IsActive && confirmation.Cancels==1,"own stale item prompt cancelled through native lifecycle");
        Check(inventory.service.Commits==1,"refusals never spend");confirmation.BeforeShow=null;confirmation.IsActive=false;
        CItem owned=inventory.service.Sell[0];
        Check(TownServiceMerchantTransaction.Commit(inventory,owned,true,()=>true),"owned item exact identity sells through native confirmation");
        Check(inventory.service.Commits==2,"sell invoked once");
        inventory.service.Sell.Remove(owned);
        Check(!TownServiceMerchantTransaction.Eligible(inventory,owned,true),"stale owned item is ineligible");
        Check(!TownServiceMerchantTransaction.Commit(inventory,owned,true,()=>true),"stale owned item cannot sell a different copy");
        inventory.service.Buy.Remove(buy);
        Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"last stock race refuses purchase");
        Transform originalParent=root.transform;catalog.Dispose();
        Check(inventory.transform.parent==originalParent,"opt out restores hidden native inventory hierarchy");
        Check(ObjectPool.Alive==0,"all physical card loans returned on teardown");
        Check(VRInteractables.Registered.Count==0,"all drawer grips unregistered on teardown");
        Check(inventory.itemsCanvasGroup.interactable,"presentation never changes native permissions");
        UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(prefab);UnityEngine.Object.DestroyImmediate(events);
        return assertions;
    }
}
