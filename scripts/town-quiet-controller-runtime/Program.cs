using System;
using System.Reflection;
using UnityEngine;
using FFSNet;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;

public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool value,string reason){_checks++;if(!value)throw new Exception(reason);}
    private static RectTransform Rect(string name,Transform? parent=null)
    {
        var go=new GameObject(name,typeof(RectTransform));var rect=(RectTransform)go.transform;
        rect.SetParent(parent,false);rect.anchorMin=new Vector2(.1f,.3f);rect.anchorMax=new Vector2(.9f,.7f);
        rect.pivot=new Vector2(.32f,.71f);rect.sizeDelta=new Vector2(293,417);rect.anchoredPosition3D=new Vector3(11,-23,9);
        rect.localScale=new Vector3(.8f,.6f,.9f);rect.localRotation=Quaternion.Euler(0,0,13);return rect;
    }
    private readonly struct Frame
    {
        private readonly Transform? _parent;private readonly int _sibling;private readonly Vector2 _min,_max,_pivot,_size;
        private readonly Vector3 _position,_scale;private readonly Quaternion _rotation;private readonly bool _active;
        internal Frame(RectTransform rect){_parent=rect.parent;_sibling=rect.GetSiblingIndex();_min=rect.anchorMin;_max=rect.anchorMax;_pivot=rect.pivot;_size=rect.sizeDelta;_position=rect.anchoredPosition3D;_scale=rect.localScale;_rotation=rect.localRotation;_active=rect.gameObject.activeSelf;}
        internal bool Equal(RectTransform rect)=>ReferenceEquals(_parent,rect.parent)&&_sibling==rect.GetSiblingIndex()&&_min==rect.anchorMin&&_max==rect.anchorMax&&_pivot==rect.pivot&&_size==rect.sizeDelta&&_position==rect.anchoredPosition3D&&_scale==rect.localScale&&Quaternion.Angle(_rotation,rect.localRotation)<.001f&&_active==rect.gameObject.activeSelf;
    }
    public static int Run()
    {
        _checks=0;TownServiceQuietController.Reset();
        var character=new CMapCharacter();character.Cards.Add(new CAbilityCard(14));character.Cards.Add(new CAbilityCard(27));
        MapRoomHand.Owned=character;
        var selected=new NewPartyCharacterUI {Service=character};NewPartyDisplayUI.PartyDisplay!.SelectedUISlot=selected;
        var parent=Rect("Inactive original flat map window");parent.gameObject.SetActive(false);
        Merchant(parent,selected,character);
        Mage(parent,selected,character);
        RequestOwnership(character);
        TownServiceQuietController.Reset();TownServicePresentation.Window=null;
        UnityEngine.Object.Destroy(parent.gameObject);
        return _checks;
    }
    private static void Merchant(RectTransform parent,NewPartyCharacterUI selected,CMapCharacter character)
    {
        var window=Rect("OriginalMerchant",parent).gameObject.AddComponent<UIWindow>();
        var shop=window.gameObject.AddComponent<UIShopItemWindow>();
        var before=Rect("EarlierSibling",window.transform);var source=Rect("Original inventory",window.transform);
        var after=Rect("LaterSibling",window.transform);shop.ItemInventory=source.gameObject.AddComponent<UIShopItemInventory>();
        source.gameObject.SetActive(false);
        var group=source.gameObject.AddComponent<CanvasGroup>();group.alpha=.7f;group.ignoreParentGroups=true;
        var record=new Frame(source);var bus=Singleton<MapChoreographer>.Instance.EventBuss;
        TownServicePresentation.Window=window;TownServicePresentation.Service=1;
        Check(TownServiceQuietController.Request(1),"owned merchant can request native original sources");
        Check(TownServiceQuietController.Prepare(window,1),"merchant original source preparation succeeds");
        Check(!window.IsOpen&&!window.IsVisible&&window.Shows==0&&MapRoomDriver.Presses==0&&ReferenceEquals(NewPartyDisplayUI.PartyDisplay!.SelectedUISlot,selected)&&selected.Clicks==0,"quiet merchant preserves flat window and exact party slot");
        Check(shop.ItemInventory.Inits==1&&ReferenceEquals(shop.ItemInventory.Service!.Party,AdventureState.MapState.MapParty)&&ReferenceEquals(shop.ItemInventory.Character,character),"original merchant inventory receives current party and character exactly once");
        Check(shop.Service!.RegisterRemoved==1&&bus.Count==5,"merchant uses original bus and native new-stock callback");
        shop.Service.Notify(new object());shop.Service.Removed!();Check(shop.ItemInventory.NewFlagRefresh==1,"merchant original new-stock event reaches original inventory");
        Check(source.gameObject.activeInHierarchy&&source.parent!=window.transform&&source.parent!.parent!=parent,"source activates independently of its inactive old flat hierarchy");
        Check(!group.ignoreParentGroups&&group.alpha==.7f&&source.parent.GetComponent<CanvasGroup>().alpha==0,"original nested CanvasGroup is masked without rewriting its alpha");
        Check(TownServiceQuietController.OriginalVisible(source,window,1)&&TownServiceQuietController.IsSourceBoundary(source.parent)&&TownServiceQuietController.IsSourceBoundary(window.transform),"quiet source boundary retains original visibility while its flat owner stays closed");
        var nativeChild=Rect("Nested native window",source).gameObject.AddComponent<UIWindow>();
        Check(TownServiceQuietController.OwnsWindow(window)&&TownServiceQuietController.OwnsWindow(nativeChild),"quiet original source and its nested windows remain owned");
        var otherInventory=Rect("OtherInventory",parent).gameObject.AddComponent<UIShopItemInventory>();
        Check(TownServiceQuietController.IsMerchantInventory(shop.ItemInventory)&&!TownServiceQuietController.IsMerchantInventory(otherInventory),"merchant proxy override belongs only to exact original inventory");
        ProxyWindow(typeof(QuietMerchantBuyRefresh),shop.ItemInventory,true);
        ProxyWindow(typeof(QuietMerchantSellRefresh),shop.ItemInventory,true);
        ProxyWindow(typeof(QuietMerchantBuyRefresh),otherInventory,false);
        bus.Emit();Check(shop.Bound==1&&shop.Gold==1&&shop.Equipped==1&&shop.Unbound==1&&shop.Unequipped==1,"each exact original merchant event handler is registered once");
        int clears=shop.Clears,registrations=bus.Registrations;
        for(int i=0;i<40;i++)Check(TownServiceQuietController.Prepare(window,1),"same merchant owner retains native prepared source");
        Check(shop.ItemInventory.Inits==1&&shop.Clears==clears&&bus.Registrations==registrations&&bus.Count==5,"render ticks never repeat native inventory initialization or subscriptions");
        TownServiceQuietController.Release();
        Check(bus.Count==0,"merchant release unregisters every exact original bus callback");
        Check(record.Equal(source),"merchant release restores exact source parent and local rect geometry");
        Check(group.ignoreParentGroups&&group.alpha==.7f&&!source.gameObject.activeSelf,"merchant release restores native active state and independent group semantics");
        Check(!TownServiceQuietController.OwnsWindow(window)&&!TownServiceQuietController.IsMerchantInventory(shop.ItemInventory),"released merchant no longer owns native proxy or window");
        for(int i=0;i<5;i++){Check(TownServiceQuietController.Prepare(window,1),"repeat merchant preparation remains usable");Check(bus.Count==5,"repeat merchant owns one set of callbacks");TownServiceQuietController.Release();Check(bus.Count==0&&record.Equal(source),"repeat merchant release has no geometry or subscription leak");}
        shop.ItemInventory.ThrowInit=true;bool threw=false;
        try { TownServiceQuietController.Prepare(window,1); } catch(Exception){threw=true;}
        Check(threw&&record.Equal(source)&&bus.Count==0&&!TownServiceQuietController.OwnsWindow(window),"failed original preparation restores native source and subscriptions before fallback");
        shop.ItemInventory.ThrowInit=false;Check(TownServiceQuietController.Prepare(window,1),"failed original preparation never poisons later owner initialization");TownServiceQuietController.Release();
        Check(TownServiceQuietController.Prepare(window,1),"merchant source is valid before native teardown fault");shop.ThrowClear=true;
        TownServiceQuietController.Release();shop.ThrowClear=false;
        Check(record.Equal(source)&&bus.Count==0&&!TownServiceQuietController.OwnsWindow(window),"native callback teardown fault still restores exact source frame and releases owner");
        var mismatched=new CMapCharacter{CharacterID="not-selected"};MapRoomHand.Owned=mismatched;
        Check(!TownServiceQuietController.Prepare(window,1)&&bus.Count==0,"mismatched selected character never receives original transaction callbacks");MapRoomHand.Owned=character;
        TownServicePresentation.Window=null;
    }
    private static void ProxyWindow(Type patch,UIShopItemInventory inventory,bool expected)
    {
        object[] args={inventory,false};patch.GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);
        Check((bool)args[1]==expected,"native merchant MP window-open override is exact and owner-scoped");
    }
    private static void Mage(RectTransform parent,NewPartyCharacterUI selected,CMapCharacter character)
    {
        var window=Rect("OriginalMage",parent).gameObject.AddComponent<UIWindow>();
        var shop=window.gameObject.AddComponent<UINewEnhancementWindow>();
        var source=Rect("Original ability slots",window.transform);
        shop.CardsDisplay=source.gameObject.AddComponent<UIPartyCharacterEnhancementAbilityCardsDisplay>();
        shop.CardsDisplay.abilityCardsPanel.content=Rect("OwnedOriginalSlotPool",source);
        shop.CardsDisplay.SetPrefab(Rect("Original native UIEnhanceCardSlot prefab",parent).gameObject.AddComponent<UIEnhanceCardSlot>());
        shop.buyButton.gameObject=Rect("Original native buy tab",window.transform).gameObject;
        shop.sellButton.gameObject=Rect("Original native sell tab",window.transform).gameObject;
        shop.buyButton.Active=shop.ShowBuyOptions;shop.buyButton.interactable=false;shop.sellButton.gameObject.SetActive(false);
        var previousFace=Rect("Previously shown native card",source).gameObject.AddComponent<AbilityCardUI>();previousFace.AbilityCard=new CAbilityCard(999);
        shop.SetOldShown(previousFace);
        var record=new Frame(source);
        TownServicePresentation.Window=window;TownServicePresentation.Service=3;
        Check(TownServiceQuietController.Request(3)&&TownServiceQuietController.Prepare(window,3),"mage uses original native preparation without map destination change");
        Check(!window.IsOpen&&!window.IsVisible&&window.Shows==0&&MapRoomDriver.Presses==0&&selected.Clicks==0,"quiet mage preserves flat window and exact selected party slot");
        Check(shop.BuyMode&&shop.buyButton.Activations==1,"mage buy mode and original tab are activated without a flat open");
        Check(shop.selectedCard==null,"mage entry clears stale native shown card before its original buy tab callback");
        Check(shop.sellButton.gameObject.activeSelf&&shop.buyButton.interactable,"mage entry restores original selling and buy-tab availability from its current native service");
        Check(shop.enhancementShop.Clears==1&&shop.Service!.Enters==1&&ReferenceEquals(shop.Service.Party,AdventureState.MapState.MapParty),"mage original service owns entry and new-stock semantics");
        Check(shop.CardsDisplay.Assigned.Count==character.Cards.Count,"every owned ability keeps its original assigned slot callback");
        Check(shop.CardsDisplay.WarningsUpdates==1&&!shop.CardsDisplay.WarningsVisible,"original new ability pool clears previous enhancement points warning");
        foreach(var card in character.Cards)
        {
            var slot=shop.CardsDisplay.Assigned[card];Check(ReferenceEquals(slot.Card,card)&&ReferenceEquals(slot.Character,character)&&slot.LastCanSelect&&slot.Inits==1,"mage slots initialize exact original card and character");
            var face=Rect("Original native ability widget",source).gameObject.AddComponent<AbilityCardUI>();face.AbilityCard=card;
            slot.Selected!(face);Check(ReferenceEquals(shop.selectedCard,face)&&shop.CardsDisplay.Callbacks>0,"physical card selection routes original slot display callback into original shop");
        }
        Check(ControllableRegistry.Listeners==1,"mage registers one exact ownership event handler");ControllableRegistry.Emit();Check(shop.OwnershipChanges==1,"mage receives original ownership callback once");
        int normalizations=HelperTools.Normalizations,updates=shop.CardsDisplay.PointsUpdates;
        for(int i=0;i<40;i++)Check(TownServiceQuietController.Prepare(window,3),"same mage owner retains original prepared source");
        Check(HelperTools.Normalizations==normalizations&&shop.Service.Enters==1&&shop.buyButton.Activations==1&&ControllableRegistry.Listeners==1,"mage render ticks never rebuild original pool or reset native tabs");
        var action=new GameAction {SupplementaryDataToken=new EnhancementToken{CardID=27}};
        TownServiceQuietController.RefreshProxyEnhancement(shop,action,false,true);
        Check(shop.CardsDisplay.Adds==0&&shop.CardsDisplay.Removes==0&&shop.Payments==0,"invalid native proxy never refreshes or reapplies an enhancement");
        TownServiceQuietController.RefreshProxyEnhancement(shop,action,true,true);
        Check(shop.CardsDisplay.Adds==1&&shop.Refreshes==1&&shop.CardsDisplay.LastSelected&&shop.Payments==0&&AudioControllerUtils.Played.Count==1&&AudioControllerUtils.Played[0]=="native-buy-enhancement","validated exact selected enhancement proxy refreshes original slots without a second payment");
        TownServiceQuietController.RefreshProxyEnhancement(shop,action,true,false);
        Check(shop.CardsDisplay.Removes==1&&shop.Refreshes==2&&shop.Payments==0&&AudioControllerUtils.Played.Count==2&&AudioControllerUtils.Played[1]=="native-sell-enhancement","validated sell enhancement proxy retains original selected-card refresh");
        window.IsVisible=true;TownServiceQuietController.RefreshProxyEnhancement(shop,action,true,true);
        Check(shop.CardsDisplay.Adds==1&&shop.Refreshes==2&&AudioControllerUtils.Played.Count==2,"visible native proxy owns its existing feedback without a duplicate quiet refresh");window.IsVisible=false;
        character.IsUnderMyControl=false;TownServiceQuietController.RefreshProxyEnhancement(shop,action,true,false);
        Check(shop.CardsDisplay.Removes==2&&AudioControllerUtils.Played.Count==2,"successful unowned proxy retains its original presentation without owned feedback audio");character.IsUnderMyControl=true;
        action.SupplementaryDataToken=new EnhancementToken{CardID=999};TownServiceQuietController.RefreshProxyEnhancement(shop,action,true,true);
        Check(shop.CardsDisplay.Adds==1,"unassigned enhancement proxy never refreshes a different native card");
        action.SupplementaryDataToken=new EnhancementToken{CardID=27};shop.CardsDisplay.ThrowRefresh=true;
        int warnings=GloomhavenVR.Core.VRLog.Warnings;
        for(int i=0;i<2;i++)typeof(QuietEnhancementBuyRefresh).GetMethod("Postfix",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,new object[]{shop,action,true});
        Check(GloomhavenVR.Core.VRLog.Warnings==warnings+1&&shop.CardsDisplay.Adds==1&&shop.Payments==0,"cosmetic quiet proxy failures never escape native action or repeat warning per source");shop.CardsDisplay.ThrowRefresh=false;
        updates=shop.CardsDisplay.PointsUpdates;character.FreePoints=2;typeof(TownServiceQuietController).GetField("_nextPointsSample",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,-1f);
        Check(TownServiceQuietController.Prepare(window,3)&&shop.CardsDisplay.PointsUpdates==updates+1,"native point changes refresh the original points presentation");
        TownServiceQuietController.Release();Check(ControllableRegistry.Listeners==0,"mage release unregisters original ownership callback");
        Check(record.Equal(source)&&shop.selectedCard==null&&shop.CardsDisplay.Deselects==1,"mage release restores its exact native source frame and clears original selection");
        for(int i=0;i<5;i++){Check(TownServiceQuietController.Prepare(window,3),"repeat mage preparation remains usable");Check(ControllableRegistry.Listeners==1&&shop.CardsDisplay.Assigned.Count==2,"repeat mage preparation binds exactly one native owned-card pool");TownServiceQuietController.Release();Check(ControllableRegistry.Listeners==0&&record.Equal(source),"repeat mage release has no geometry or callback leak");}
        AdventureState.MapState.MapParty=new MapParty();Check(TownServiceQuietController.Prepare(window,3),"mage prepares after native adventure changes");
        Check(ReferenceEquals(shop.Service!.Party,AdventureState.MapState.MapParty),"mage entry uses the current adventure party after save switch");
        TownServiceQuietController.Release();AdventureState.MapState.MapParty=new MapParty{SellAvailable=false};
        Check(TownServiceQuietController.Prepare(window,3)&&!shop.sellButton.gameObject.activeSelf&&!shop.buyButton.interactable,"mage entry clears old selling availability in native rulesets where selling is unavailable");
        var other=Rect("Unowned mage proxy",parent).gameObject.AddComponent<UINewEnhancementWindow>();other.gameObject.AddComponent<UIWindow>();other.character=character;other.CardsDisplay=shop.CardsDisplay;
        action.SupplementaryDataToken=new EnhancementToken{CardID=14};TownServiceQuietController.RefreshProxyEnhancement(other,action,true,true);
        Check(shop.CardsDisplay.Adds==1,"another player's enhancement proxy cannot refresh this owner's quiet original");
        TownServiceQuietController.Release();TownServicePresentation.Window=null;
    }
    private static void RequestOwnership(CMapCharacter character)
    {
        TownServiceMerchantHandoff.HasParkedOffer=false;TownServiceEnhancementHandoff.HasCurrentOffering=false;TownServiceEnhancementHandoff.KeepsQuietVisit=true;
        TownServicePopulation.Station.Near=false;
        Check(TownServiceQuietController.Request(3)&&TownServiceQuietController.RequestedService==3,"valid physical mage card or fan intent survives a separate gaze-distance sample");
        TownServiceMerchantHandoff.HasParkedOffer=true;Check(!TownServiceQuietController.Request(1),"another service cannot steal a retained native card owner");TownServiceMerchantHandoff.HasParkedOffer=false;
        TownServiceQuietController.EndRequest(3);TownServicePresentation.Ritual=new TownServiceRitual{HasTemplePurseInHand=true};
        Check(!TownServiceQuietController.Request(1)&&!TownServiceQuietController.Request(3),"held purse blocks cross-service native ownership without blocking public stations");
        TownServicePresentation.Ritual.HasTemplePurseInHand=false;TownServicePresentation.Ritual.HasParkedTempleOffer=true;
        Check(!TownServiceQuietController.Request(1),"parked temple purse retains its actual native interaction");
        TownServicePresentation.Ritual=null;MapRoomHand.Owned=null;
        Check(!TownServiceQuietController.Request(1)&&TownServiceQuietController.RequestedService==0,"unassigned peer never initializes a private original transaction controller");
        MapRoomHand.Owned=character;
        var temple=new GameObject("QuietLogicalTemple",typeof(RectTransform),typeof(UIWindow)).GetComponent<UIWindow>();
        TownServicePresentation.Window=temple;TownServicePresentation.Service=2;
        Check(TownServiceQuietController.InteractionMode==EGuildmasterMode.Temple&&!temple.IsOpen,"logical temple interaction never requires a hidden flat destination");TownServicePresentation.Window=null;UnityEngine.Object.Destroy(temple.gameObject);
        Check(MapRoomDriver.Presses==0&&GuildmasterDestinations.Mode==EGuildmasterMode.None,"all valid quiet native preparations avoid hidden flat destination dispatch");
    }
}
