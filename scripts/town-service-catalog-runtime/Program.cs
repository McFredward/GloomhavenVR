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
    private static void CardReach(TownServiceCatalog catalog)
    {
        var entries=new List<TownServiceCatalog.Entry>();
        var colliders=new List<Collider>();
        foreach(var entry in catalog.Entries)
        {
            Set(entry,"_presentedAt",-1000f);entry.Tick(1f);if(!entry.Exposed)continue;entries.Add(entry);
            colliders.Add(entry.Sample.PickCollider);
        }
        Physics.SyncTransforms();
        for(int n=0;n<entries.Count;n++)
        {
            var card=(RectTransform)entries[n].CardRoot;
            Vector3 target=card.position;
            // The actual token owns these actual Unity colliders; the old test fitted a
            // separate fake collider and stubbed the very grip which failed on hardware.
            var ray=new Ray(target-card.forward*.4f,card.forward);
            float closest=float.PositiveInfinity;int picked=-1;
            for(int candidate=0;candidate<colliders.Count;candidate++)
                if(colliders[candidate].Raycast(ray,out RaycastHit hit,.8f)&&hit.distance<closest)
                {closest=hit.distance;picked=candidate;}
            Check(picked==n,"every open card is the nearest actual collider along its face approach");
            Check(Vector3.Distance(colliders[n].ClosestPoint(target-card.forward*.01f),target)<.006f,
                "physical fingertip can reach each exposed card face");
        }
    }
    private static void Turn(TownServiceCatalog catalog,TownServiceMerchantDrawer crank)
    {
        int previous=crank.Page;
        Check(crank.RequestTurn(),"unheld stocked rack accepts one mechanical turn");
        Check(!crank.RequestTurn(),"a running mechanical turn cannot restart");
        Set(crank,"_clock",.20f);crank.Tick(1f);
        Check(crank.Page==previous,"card identity is retained while outgoing front is visible");
        Check(crank.HousingRoot.Find("Cassette").localPosition.z > .1f,"cassette retracts actual card parent before replacing the page");
        Set(crank,"_clock",.45f);crank.Tick(1f);
        Check(crank.Page==previous/256*256+(previous%256+1)%crank.PageCount,"replacement happens behind the opaque folding shutter");
        foreach(var entry in catalog.Entries)entry.Tick(1f);
        foreach(var entry in catalog.Entries)if(entry.Selling==crank.Selling)
            Check(!entry.Sample.CanGrab,"moving rack prevents a grab during mechanical turnover");
        Set(crank,"_clock",.85f);crank.Tick(1f);
        foreach(var entry in catalog.Entries)entry.Tick(1f);
        Check(!crank.Moving&&Quaternion.Angle(crank.HousingRoot.localRotation,Quaternion.identity)<.001f,"rack settles at original front pose");
    }
    private static void HeldScale(TownServiceCatalog catalog,Transform anchor)
    {
        var entry=catalog.Entries[0];var source=entry.CardRoot;
        Transform physical=source.parent.parent;
        foreach(float scale in new[]{.05f,1f,2f,198.12f})foreach(HandSide side in new[]{HandSide.Left,HandSide.Right})
        {
            anchor.localScale=Vector3.one*scale;
            var hand=new VRHand{Side=side,WorldScale=scale};
            hand.Rig.GrabAnchor.localScale=Vector3.one*scale;
            hand.Rig.GrabAnchor.SetPositionAndRotation(new Vector3(1f,1.4f,-2f)*scale,Quaternion.Euler(-15f,37f,22f));
            entry.Tick(scale);Vector3 home=physical.localPosition;Quaternion rotation=physical.localRotation;
            Check(entry.Sample.CanGrab,"real physical token accepts an unowned inspection hand");
            entry.Sample.OnGrab(hand);for(int settle=0;settle<120;settle++)entry.Tick(scale);catalog.LateTick();
            Check(entry.Sample.IsHeld&&entry.Sample.HeldRoot==physical,"pickup retains the same physical native card and body");
            Check(source.gameObject.activeInHierarchy&&source.GetComponentInParent<Canvas>().enabled,
                "held native face remains active and rendered through the original world canvas");
            Vector3 local=hand.Rig.GrabAnchor.InverseTransformPoint(physical.position);
            Check(local.magnitude<.15f,"held card stays inside one hand span at every rig scale");
            Check(Vector3.Distance(source.position,physical.position)<.002f*scale,"native face retains the held body pose used by multiplayer publishing");
            Check(!catalog.CanRelocate,"held or returning sample prevents station relocation");
            entry.Sample.OnGrabCancelled(hand);catalog.LateTick();
            Check(Vector3.Distance(physical.localPosition,home)<.0001f&&Quaternion.Angle(physical.localRotation,rotation)<.001f,
                "cancel restores the original counter pose");
            UnityEngine.Object.DestroyImmediate(hand.Rig.GrabAnchor.gameObject);
        }
        anchor.localScale=Vector3.one;
    }
    public static int Run()
    {
        assertions=0;var root=new GameObject("MerchantFixture");var events=new GameObject("Events",typeof(EventSystem));
        var prefab=new GameObject("MerchantPrefab");var counter=new GameObject("Counter");counter.transform.SetParent(prefab.transform,false);
        var plank=GameObject.CreatePrimitive(PrimitiveType.Cube);plank.name="Furniture_DarkWood";plank.transform.SetParent(counter.transform,false);
        plank.GetComponent<MeshRenderer>().sharedMaterial=new Material(Shader.Find("Standard")){name="DarkWood"};
        var returnTemplate=new GameObject("CounterReturn");returnTemplate.transform.SetParent(counter.transform,false);
        var returnTop=GameObject.CreatePrimitive(PrimitiveType.Cube);returnTop.transform.SetParent(returnTemplate.transform,false);
        returnTop.transform.localPosition=new Vector3(0f,.96f,0f);returnTop.transform.localScale=new Vector3(1.30f,.02f,1.15f);returnTemplate.SetActive(false);
        foreach(string template in new[]{"MerchantCassetteTemplate","MerchantCrankTemplate","MerchantButtonTemplate","MerchantShutterTemplate"})
        {
            var t=GameObject.CreatePrimitive(PrimitiveType.Cube);t.name=template;t.transform.SetParent(counter.transform,false);
            if(template=="MerchantCrankTemplate") {var h=GameObject.CreatePrimitive(PrimitiveType.Cube);h.name="Handle";h.transform.SetParent(t.transform,false);h.transform.localPosition=new Vector3(.08f,-.09f,0f);}
            if(template=="MerchantShutterTemplate") {var u=new GameObject("Upper");u.transform.SetParent(t.transform,false);var l=new GameObject("Lower");l.transform.SetParent(u.transform,false);}
            t.SetActive(false);
        }
        TownServiceAssets.Merchant=prefab;
        var inventory=Rect("Inventory",root.transform).gameObject.AddComponent<UIShopItemInventory>();
        inventory.itemsCanvasGroup=inventory.gameObject.AddComponent<CanvasGroup>();
        inventory.itemTooltip=Rect("OriginalDetail",inventory.transform).gameObject.AddComponent<UIPartyItemInventoryTooltip>();
        inventory.buyTab=Rect("Buy",inventory.transform).gameObject.AddComponent<Tab>();inventory.buyTab.Changed=()=>inventory.Selling=false;
        inventory.sellTab=Rect("Sell",inventory.transform).gameObject.AddComponent<Tab>();inventory.sellTab.Changed=()=>inventory.Selling=true;
        var slot=Rect("NativeSlot",root.transform).gameObject.AddComponent<UIShopItemSlot>();slot.Selectable=slot.gameObject.AddComponent<Button>();inventory.slotPrefab=slot;
        var confirmation=Rect("Confirmation",root.transform).gameObject.AddComponent<UIItemConfirmationBox>();confirmation.confirmButton=confirmation.gameObject.AddComponent<Button>();Singleton<UIItemConfirmationBox>.Instance=confirmation;
        for(int failAt=1;failAt<=2;failAt++)
        {
            int sibling=inventory.transform.GetSiblingIndex();bool threw=false;
            GloomhavenVR.Net.RemoteWidgetMirror.ThrowConstruction=failAt;
            try{new TownServiceCatalog(inventory,root.transform,()=>inventory.character,()=>true,root.transform);}
            catch(InvalidOperationException e){threw=e.Message=="injected mirror allocation failure";}
            Check(threw,"injected preview constructor failure is exercised");
            Check(inventory.transform.parent==root.transform&&inventory.transform.GetSiblingIndex()==sibling,"constructor failure restores native inventory ownership");
            Check(GloomhavenVR.Core.Loc.Subscribers==0,"constructor failure removes acquired zone subscriptions");
            foreach(Transform child in root.transform)
                if(child.name=="GloomhavenVR.Merchant.HiddenBackend"||child.name=="GloomhavenVR.TownService.Catalog")
                    Check(!child.gameObject.activeSelf,"failed presentation roots are immediately hidden");
        }
        var counts=new[]{20,24,37,17,19,44,3};int id=1;
        for(int category=0;category<counts.Length;category++)for(int n=0;n<counts[category];n++,id++)
        {
            var item=new CItem(id);item.YMLData.Slot=(CItem.EItemSlot)category;inventory.service.Buy.Add(item);
            var own=new CItem(id){NetworkID=(uint)(1000+id)};own.YMLData.Slot=item.YMLData.Slot;inventory.service.Sell.Add(own);
        }
        var anchor=new GameObject("CounterAnchor");anchor.transform.SetParent(root.transform,false);
        object context=new object();bool alive=true;
        ShopService.Source=inventory.service;
        var catalog=new TownServiceCatalog(inventory,anchor.transform,()=>context,()=>alive,anchor.transform,persistent:true);
        catalog.SetVisibility(1f);Census(catalog);catalog.Tick(1f);
        Check(catalog.Entries.Count==164,"all 164 stock identities remain available in the persistent cabinet");
        Check(catalog.Controls.Count==0,"no flat filter or page buttons");
        Check(ObjectPool.Alive==164,"one physical original card per persistent entry");
        Check(catalog.Drawers.Count==1&&catalog.Categories.Count==6,"one crank and six physical category buttons share one cabinet");
        Check(catalog.Extensions.Count==0,"stock growth never adds giant side returns");
        foreach(var e in catalog.Entries)
        {
            bool visible=e.Category==0&&e.Ordinal<12;
            Check(e.Exposed==visible&&e.CardUI.GetComponentInParent<Canvas>().enabled&&e.CardUI.GetComponentInParent<CanvasGroup>().alpha==(visible?1f:0f)&&e.CardUI.gameObject.activeInHierarchy,
                "only active category and page are exposed; source identity remains alive");
            Check(e.Sample.CanGrab==visible,"only exposed tray cards have a physical pickup target");
            Check(e.CardUI.GetComponent<Image>().raycastTarget==false,"own card GUI cannot veto direct pickup");
            Check(e.BodyRoot!=null,"each item has physical body");
            if(!e.Selling)
            {
                var corners=new Vector3[4];((RectTransform)e.CardRoot).GetWorldCorners(corners);
                foreach(Vector3 corner in corners)
                {
                    Vector3 point=anchor.transform.InverseTransformPoint(corner);
                    Check(point.x<-.4f,"all stock cards clear the NPC ledger and transaction workspace");
                    Check(point.x > -1.38f && point.x < -.52f,"every stock card fits the portable cabinet width");
                }
                if(e.Ordinal%TownServiceMerchantLayout.StockColumns>0)
                {
                    var previous=catalog.Entries.FindEntry(e.Ordinal-1,false,e.Category);
                    float separation=Mathf.Abs(e.CardRoot.position.x-previous.CardRoot.position.x);
                    Check(separation>Vector3.Distance(corners[0],corners[3]),"physical card faces never overlap their adjacent column");
                }
            }
        }
        var stable=catalog.Entries[0];var stableRoot=stable.CardRoot;
        Census(catalog);Check(catalog.Entries[0].CardRoot==stableRoot,"unchanged census retains physical identity");
        var added=new CItem(999);added.YMLData.Slot=CItem.EItemSlot.Head;inventory.service.Buy.Add(added);Census(catalog);
        Check(catalog.Entries.Count==165,"late unlock adds card without dropping old stock");
        Check(catalog.Entries[0].CardRoot==stableRoot,"late unlock preserves existing card transforms");
        HeldScale(catalog,anchor.transform);
        var crank=catalog.Drawers[0];
        stable.Tick(1f);var holder=new VRHand();stable.Sample.OnGrab(holder);stable.Tick(1f);
        Check(!crank.RequestTurn(),"held merchandise prevents rack motion");
        Check(stable.Exposed&&crank.Page==0,"held card cannot be swapped into a hidden tray");
        stable.Sample.OnGrabCancelled(holder);UnityEngine.Object.DestroyImmediate(holder.Rig.GrabAnchor.gameObject);
        Turn(catalog,crank);Check(crank.Page==1,"one completed physical turn advances exactly one tray");
        for(int n=1;n<crank.PageCount;n++)Turn(catalog,crank);
        Check(crank.Page==0,"stock rack completes its accessible cycle");
        int fullPages=crank.PageCount;
        Check(crank.RequestTurn(),"native stock can shrink during a valid pending turn");
        crank.SetPageCount(1);
        Check(crank.ToPage==1,"native stock shrink retains the authored in-flight target");
        Set(crank,"_clock",.45f);crank.Tick(1f);
        Check(crank.Page==crank.ToPage,"owner page and explicit remote target agree after a mid-turn shrink");
        Set(crank,"_clock",.85f);crank.Tick(1f);crank.SetPageCount(fullPages);
        foreach(var entry in catalog.Entries)entry.Tick(1f);
        Check(inventory.service.Commits==0,"inspection census and opening never spend gold");
        // Public stock is read-only and does not enter/reparent the native inventory.
        Check(inventory.transform.parent==root.transform,"persistent browsing leaves native inventory ownership untouched");
        Check(inventory.Refreshes==0,"persistent browsing never enters native transaction flow");
        CItem buy=inventory.service.Buy[0];
        Check(TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"explicit drop dispatches native buy confirmation");
        Check(inventory.service.Commits==0&&confirmation.IsActive,"offering opens confirmation without spending");
        ExecuteEvents.Execute(confirmation.confirmButton.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
        Check(inventory.service.Commits==1&&!confirmation.IsActive,"explicit confirmation commits exactly once");
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
        Check(inventory.service.Commits==1&&confirmation.IsActive,"sale offering waits for explicit confirmation");
        ExecuteEvents.Execute(confirmation.confirmButton.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerClickHandler);
        Check(inventory.service.Commits==2,"sale confirmation invokes native callback once");
        inventory.service.Sell.Remove(owned);
        Check(!TownServiceMerchantTransaction.Eligible(inventory,owned,true),"stale owned item is ineligible");
        Check(!TownServiceMerchantTransaction.Commit(inventory,owned,true,()=>true),"stale owned item cannot sell a different copy");
        inventory.service.Buy.Remove(buy);
        Check(!TownServiceMerchantTransaction.Commit(inventory,buy,false,()=>true),"last stock race refuses purchase");
        Transform originalParent=root.transform,cachedMount=stable.MountRoot;
        Check(TownServiceCatalog.PresentationOwner(stable.CardRoot)==cachedMount,"pooled card ownership resolves its actual physical mount");
        catalog.Dispose();
        Check(TownServiceCatalog.PresentationOwner(cachedMount)==null,"disposing physical catalog removes retained presentation ownership");
        Check(inventory.transform.parent==originalParent,"opt out restores hidden native inventory hierarchy");
        Check(ObjectPool.Alive==0,"all physical card loans returned on teardown");
        Check(VRInteractables.Registered.Count==0,"all physical pickup colliders unregistered on teardown");
        Check(inventory.itemsCanvasGroup.interactable,"presentation never changes native permissions");
        // Original HQ reward/stock caps are Common6/Rare2/Relic1. Across all supplied
        // nonquest IDs that is512 sellable copies at21 roster characters, including multiple distinct CItems.
        // The native GetItemsToSell excludes QuestItem in both character and party mode.
        inventory.service.Sell.Clear();inventory.service.Buy.Clear();
        int nextId=1;uint network=10000;
        int[] originals={20,24,54,19,44},copies={40,44,102,37,289};
        CItem.EItemSlot[] slots={CItem.EItemSlot.Head,CItem.EItemSlot.Body,CItem.EItemSlot.OneHand,CItem.EItemSlot.Legs,CItem.EItemSlot.SmallItem};
        for(int category=0;category<slots.Length;category++)
        {
            int first=nextId;
            for(int n=0;n<originals[category];n++)
            {var item=new CItem(nextId++);item.YMLData.Slot=slots[category];inventory.service.Buy.Add(item);}
            for(int n=0;n<copies[category];n++)
            {var item=new CItem(first+n%originals[category]){NetworkID=network++};item.YMLData.Slot=slots[category];inventory.service.Sell.Add(item);}
        }
        using(var fullCatalog=new TownServiceCatalog(inventory,anchor.transform,()=>context,()=>true,anchor.transform,persistent:true))
        {
            fullCatalog.SetVisibility(1f);Census(fullCatalog);fullCatalog.Tick(1f);
            Check(fullCatalog.Entries.Count==161,"native rarity copies produce one stock surface per item identity");
            Check(fullCatalog.Extensions.Count==0&&fullCatalog.Drawers.Count==1,"maximum roster inventory retains one compact cabinet");
            var seen=new HashSet<TownServiceCatalog.Entry>();var rack=fullCatalog.Drawers[0];
            for(int category=0;category<6;category++)
            {
                if(category!=rack.Category){Check(rack.Select(category,false),"physical category can change after a completed turn");Set(rack,"_clock",.85f);rack.Tick(1f);}
                fullCatalog.Tick(1f);
                for(int page=0;page<rack.PageCount;page++)
                {
                    foreach(var entry in fullCatalog.Entries)if(entry.Exposed)seen.Add(entry);
                    CardReach(fullCatalog);
                    if(rack.PageCount>1)Turn(fullCatalog,rack);
                }
                Check(rack.Page==category*256,"complete category cycle returns to first page");
            }
            Check(seen.Count==161,"all late-game stock is physically reachable across categories and crank pages");
            CardReach(fullCatalog);
            foreach(var entry in fullCatalog.Entries)
            {
                var corners=new Vector3[4];((RectTransform)entry.CardRoot).GetWorldCorners(corners);
                foreach(var point in corners)
                    Check(point.y+.970f>=0,"every native-capacity card stays above the floor");
            }
        }
        Check(ObjectPool.Alive==0&&VRInteractables.Registered.Count==0,"maximum native inventory releases every card and return");
        UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(prefab);UnityEngine.Object.DestroyImmediate(events);
        return assertions;
    }
}
internal static class CatalogFixtureLookup
{
    internal static TownServiceCatalog.Entry FindEntry(this IReadOnlyList<TownServiceCatalog.Entry> entries,int ordinal,bool selling,int category)
    {foreach(var entry in entries)if(entry.Ordinal==ordinal&&entry.Selling==selling&&entry.Category==category)return entry;throw new Exception("Missing counter slot");}
}
