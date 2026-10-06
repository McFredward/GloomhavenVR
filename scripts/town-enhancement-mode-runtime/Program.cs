using System;
using UnityEngine;
using FFSNet;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;

public static class InteractionProgram
{
    private static int checks;
    private static void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
    private static RectTransform Rect(string name,Transform? parent=null)
    {var value=(RectTransform)new GameObject(name,typeof(RectTransform)).transform;value.SetParent(parent,false);return value;}
    private static AbilityCardUI Face(CMapCharacter owner,int id,int areas,int enhanced,Transform parent)
    {
        var card=new CAbilityCard(id);owner.Cards.Add(card);
        var face=Rect("Native owned ability "+id,parent).gameObject.AddComponent<AbilityCardUI>();face.AbilityCard=card;
        for(int i=0;i<areas;i++)
        {
            var row=Rect("Row Container "+i,face.transform);var item=Rect("Native upgrade button "+i,row).gameObject.AddComponent<EnhancementButtonBase>();
            item.EnhancementLine=i%2==0?EEnhancementLine.Attack:EEnhancementLine.Move;item.Enhancement.Ability.ID=i;
            item.Enhancement.Enhancement=i<enhanced?EEnhancement.PlusOne:EEnhancement.NoEnhancement;
            face.EnhancementElements.All.Add(item);
        }
        NativeCards.Faces.Add(card,face);return face;
    }
    public static int Run()
    {
        checks=0;
        Visit(true,0);Visit(true,2);Visit(false,0);Visit(false,2);Visit(true,1);
        Transitions();
        return checks;
    }
    private static void Transitions()
    {
        TownServiceQuietController.Reset();NativeCards.Faces.Clear();
        TownServiceEnhancementHandoff.KeepsQuietVisit=false;TownServicePopulation.Station.Near=true;
        var owner=new CMapCharacter();MapRoomHand.Owned=owner;
        var selected=new NewPartyCharacterUI{Service=owner};NewPartyDisplayUI.PartyDisplay!.SelectedUISlot=selected;
        var root=Rect("Inactive native map during visits");root.gameObject.SetActive(false);
        var mageWindow=Rect("Native mage source owner",root).gameObject.AddComponent<UIWindow>();
        var mage=mageWindow.gameObject.AddComponent<UINewEnhancementWindow>();mage.Configure(root,true,0);
        var mageSource=Rect("Native mage slots",mageWindow.transform);mage.CardsDisplay=mageSource.gameObject.AddComponent<UIPartyCharacterEnhancementAbilityCardsDisplay>();
        mage.CardsDisplay.abilityCardsPanel.content=Rect("Native mage pool",mageSource);
        mage.CardsDisplay.SetPrefab(Rect("Original native slot prefab",root).gameObject.AddComponent<UIEnhanceCardSlot>());
        var eligible=Face(owner,42,3,0,mageSource);
        var merchantWindow=Rect("Native merchant source owner",root).gameObject.AddComponent<UIWindow>();
        var merchant=merchantWindow.gameObject.AddComponent<UIShopItemWindow>();
        var merchantSource=Rect("Native merchant inventory",merchantWindow.transform);merchant.ItemInventory=merchantSource.gameObject.AddComponent<UIShopItemInventory>();
        var templeWindow=Rect("Logical temple source owner",root).gameObject.AddComponent<UIWindow>();
        TownServicePresentation.Window=mageWindow;TownServicePresentation.Service=3;
        Check(TownServiceQuietController.Request(3)&&TownServiceQuietController.Prepare(mageWindow,3),"mage starts native quiet lease before service transitions");
        mage.CardsDisplay.Assigned[eligible.AbilityCard].Selected!(eligible);
        TownServiceEnhancementHandoff.HasCurrentOffering=true;
        Check(!TownServiceQuietController.Request(1)&&TownServiceQuietController.OwnsWindow(mageWindow),"parked mage card preserves its rightful local transaction source");
        TownServiceEnhancementHandoff.HasCurrentOffering=false;mage.OnSelectedCardToEnhance(null);
        Check(TownServiceQuietController.Request(1),"mage cancellation immediately permits merchant visit");
        TownServicePresentation.Window=merchantWindow;TownServicePresentation.Service=1;
        Check(TownServiceQuietController.Prepare(merchantWindow,1)&&mageSource.parent==mageWindow.transform&&ControllableRegistry.Listeners==0,
            "mage to merchant switch restores old source and ownership callback before preparing new owner");
        Check(TownServiceQuietController.OwnsWindow(merchantWindow)&&Singleton<MapChoreographer>.Instance.EventBuss.Count==5,"merchant owns one exact native event lease after mage switch");
        TownServiceMerchantHandoff.HasParkedOffer=true;
        Check(!TownServiceQuietController.Request(3),"parked merchant card preserves its rightful local transaction source");
        TownServiceMerchantHandoff.HasParkedOffer=false;
        TownServiceQuietController.EndRequest(1);TownServiceQuietController.Release();
        TownServicePresentation.Window=templeWindow;TownServicePresentation.Service=2;
        Check(TownServiceQuietController.InteractionMode==EGuildmasterMode.Temple&&merchantSource.parent==merchantWindow.transform&&Singleton<MapChoreographer>.Instance.EventBuss.Count==0,
            "merchant to logical temple transition releases exact merchant source without a flat destination");
        // The original temple controller is a separate boundary here; exercise
        // only the quiet controller's actual local held/parked transaction guards.
        TownServicePresentation.Ritual=new TownServiceRitual{HasTemplePurseInHand=true};
        Check(!TownServiceQuietController.Request(3),"held temple transaction preserves its active local native source");
        TownServicePresentation.Ritual.HasTemplePurseInHand=false;TownServicePresentation.Ritual.HasParkedTempleOffer=true;
        Check(!TownServiceQuietController.Request(3),"parked temple transaction preserves its active local native source");
        TownServicePresentation.Ritual=null;
        Check(TownServiceQuietController.Request(3),"completed temple transaction immediately permits mage visit");
        TownServicePresentation.Window=mageWindow;TownServicePresentation.Service=3;
        Check(TownServiceQuietController.Prepare(mageWindow,3),"mage source prepares after a complete multi-service visit cycle");
        mage.CardsDisplay.Assigned[eligible.AbilityCard].Selected!(eligible);
        Check(mage.NativeMode==1&&mage.NativeAreaCount==3,"reentered mage retains all native selectable areas with its already-on tab");
        TownServicePopulation.Station.Near=false;
        Check(TownServiceQuietController.RequestedService==0,"unheld mage visit ends immediately after leaving its resident range");
        TownServiceQuietController.Release();TownServicePopulation.Station.Near=true;
        Check(TownServiceQuietController.Request(3)&&TownServiceQuietController.Prepare(mageWindow,3),"departed mage owner can reenter without a stale native lease");
        mage.CardsDisplay.Assigned[eligible.AbilityCard].Selected!(eligible);
        Check(mage.NativeAreaCount==3&&!mageWindow.IsOpen&&!merchantWindow.IsOpen&&!templeWindow.IsOpen&&MapRoomDriver.Presses==0&&selected.Clicks==0,
            "all multi-service transitions keep native enhancements usable without flat windows or party-selection writes");
        TownServiceQuietController.Reset();TownServicePresentation.Window=null;
        UnityEngine.Object.Destroy(root.gameObject);
    }
    private static void Visit(bool alreadyOn,int previousMode)
    {
        TownServiceQuietController.Reset();NativeCards.Faces.Clear();AudioControllerUtils.Played.Clear();
        var owner=new CMapCharacter();MapRoomHand.Owned=owner;
        var selected=new NewPartyCharacterUI{Service=owner};NewPartyDisplayUI.PartyDisplay!.SelectedUISlot=selected;
        var root=Rect("Inactive original flat hierarchy");root.gameObject.SetActive(false);
        var window=Rect("Original native mage window",root).gameObject.AddComponent<UIWindow>();
        var shop=window.gameObject.AddComponent<UINewEnhancementWindow>();
        shop.Configure(root,alreadyOn,previousMode);
        var source=Rect("Native card display",window.transform);shop.CardsDisplay=source.gameObject.AddComponent<UIPartyCharacterEnhancementAbilityCardsDisplay>();
        shop.CardsDisplay.abilityCardsPanel.content=Rect("Native slot content",source);
        shop.CardsDisplay.SetPrefab(Rect("Original UIEnhanceCardSlot prefab",root).gameObject.AddComponent<UIEnhanceCardSlot>());
        var eligible=Face(owner,11,3,0,source);var partial=Face(owner,12,3,1,source);
        var full=Face(owner,13,2,2,source);var unsupported=Face(owner,14,0,0,source);
        TownServicePresentation.Window=window;TownServicePresentation.Service=3;
        Check(TownServiceQuietController.Request(3)&&TownServiceQuietController.Prepare(window,3),"quiet owned native mage preparation succeeds");
        Check(!window.IsOpen&&!window.IsVisible&&window.Shows==0&&MapRoomDriver.Presses==0&&selected.Clicks==0&&ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot,selected),"native mode recovery never opens flat UI or changes selected character");
        Check(shop.CardsDisplay.Assigned.Count==4&&ControllableRegistry.Listeners==1,"exact owned native pool and ownership callback retained");
        shop.CardsDisplay.Assigned[eligible.AbilityCard].Selected!(eligible);
        Check(shop.NativeAreaCount==3&&!shop.NativeInvalid&&!shop.NativeInformationVisible&&ReferenceEquals(shop.selectedCard,eligible),"enchantable owned native card exposes every original selectable area immediately");
        Check(shop.NativeMode==1&&shop.buyButton.isOn,"native already-on and off tabs establish BUY from NONE or retained SELL");
        Check(shop.CardsDisplay.IsBuy&&shop.NativeBuyEffect||previousMode==1,"original buy callback updates native display and effect");
        Check(shop.buyButton.Activations==(alreadyOn?0:1),"already-on native tab runs direct mode callback without fabricated activation");
        for(int i=0;i<shop.NativeAreaCount;i++)Check(shop.NativeArea(i).gameObject.activeInHierarchy&&shop.NativeArea(i).transform.IsChildOf(eligible.transform),"original native selectable areas activate on the actual offered card hierarchy");
        Check(shop.enhancementShop.Options.Count>0&&shop.enhancementShop.Options.TrueForAll(slot=>slot.BuyMode),"original BUY line callback exposes enhancement options immediately");
        var area=shop.NativeArea(1);Check(area.Buy&&area.OnSelected!=null,"original selectable highlight retains native buy mode and callback");
        area.OnSelected!(area.Ability!);
        Check(shop.NativeAreaCount==3&&shop.enhancementShop.Options.Count>0,"physical native area callback selects another valid enhancement line");
        shop.CardsDisplay.Assigned[partial.AbilityCard].Selected!(partial);
        Check(shop.NativeAreaCount==2&&!shop.NativeInvalid,"partly enhanced card excludes occupied area and keeps remaining original areas");
        shop.CardsDisplay.Assigned[full.AbilityCard].Selected!(full);
        Check(shop.NativeAreaCount==0&&shop.NativeInvalid&&shop.NativeInformationVisible&&shop.NativeCardMessage=="GUI_CARD_FULLY_ENHANCED","fully enhanced native card remains a valid no-upgrade exception");
        shop.CardsDisplay.Assigned[unsupported.AbilityCard].Selected!(unsupported);
        Check(shop.NativeAreaCount==0&&shop.NativeInvalid&&shop.NativeInformationVisible&&shop.NativeCardMessage=="GUI_CARD_CANNOT_BE_ENHANCED","unsupported native card remains a valid no-enhancement exception");
        shop.OnSelectedCardToEnhance(null);Check(shop.selectedCard==null&&shop.NativeAreaCount==0,"native cancel clears original card and areas");
        shop.CardsDisplay.Assigned[eligible.AbilityCard].Selected!(eligible);
        Check(shop.NativeAreaCount==3&&!shop.NativeInvalid,"cancelled card can be replaced with an eligible original card");
        var action=new GameAction{SupplementaryDataToken=new EnhancementToken{CardID=11}};
        TownServiceQuietController.RefreshProxyEnhancement(shop,action,true,true);
        Check(shop.CardsDisplay.Adds==1&&shop.Refreshes==1&&shop.Payments==0,"successful native multiplayer enhancement callback preserves original pool refresh without a second payment");
        TownServiceQuietController.RefreshProxyEnhancement(shop,action,false,true);
        Check(shop.CardsDisplay.Adds==1&&shop.Payments==0,"invalid native enhancement callback never bypasses payment validation");
        int activations=shop.buyButton.Activations,modeChanges=shop.CardsDisplay.ModeRefreshes;
        for(int i=0;i<20;i++)Check(TownServiceQuietController.Prepare(window,3),"same owner keeps prepared native mode");
        Check(shop.buyButton.Activations==activations&&shop.CardsDisplay.ModeRefreshes==modeChanges&&shop.NativeAreaCount==3,"ordinary ticks never reset current native areas or tab mode");
        TownServiceQuietController.Release();Check(ControllableRegistry.Listeners==0&&shop.selectedCard==null&&source.parent==window.transform,"native release restores source and ownership lifecycle");
        TownServicePresentation.Window=null;UnityEngine.Object.Destroy(root.gameObject);
    }
}
