// Native object pooling, card artwork and payment rules remain explicit boundaries.
// The checker binds the original game's tab, mode, selection, filtering and highlight
// methods without changing their bodies. Real Unity Toggle supplies the critical
// already-on/no-event behavior that the older unconditional callback stub masked.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ScenarioRuleLibrary;

namespace ScenarioRuleLibrary
{
    public enum EEnhancement { NoEnhancement, PlusOne }
    public enum EEnhancementLine { Attack, Move, SummonAttack }
    public sealed class CAbility { public int ID; }
}
public sealed class EnhancementData { public EEnhancement Enhancement;public CAbility Ability=new(); }
public class EnhancementButtonBase : MonoBehaviour
{
    public RectTransform? ParentContainer;
    public EnhancementData Enhancement=new();
    public CAbility Ability=>Enhancement.Ability;
    public EEnhancementLine EnhancementLine;
    public int EnhancementSlot;
    public EEnhancement EnhancedType=>Enhancement.Enhancement;
}
public sealed class EnhancedAreaHex : EnhancementButtonBase {}
public sealed class CardEnhancementElements { public List<EnhancementButtonBase> All=new(); }
public sealed class EnhancementLine
{
    public EEnhancementLine Line;public CAbility Ability;public List<EnhancementButtonBase> EnhancementSlots;
    public EnhancementLine(EEnhancementLine line,CAbility ability,List<EnhancementButtonBase> slots){Line=line;Ability=ability;EnhancementSlots=slots;}
}
public sealed class EnhancementSlot
{
    public bool BuyMode;public object? priceCalculator;
    public EnhancementSlot(EnhancementButtonBase button,EEnhancement enhancement,bool buyMode,EnhancementLine line,object? calculator=null){BuyMode=buyMode;priceCalculator=calculator;}
}
public sealed class EnhancementBuyPriceCalculator { public EnhancementBuyPriceCalculator(CAbility a,CAbilityCard card,object character){} }
public sealed class EnhancementSellPriceCalculator { public EnhancementSellPriceCalculator(EnhancementLine line,object character){} }
public static class InputManager { public static bool GamePadInUse; }
public static class CoreApplication { public static bool IsQuitting; }
public sealed class NativeText { public string Key="";public void SetTextKey(string key){Key=key;} }
public sealed class NativeEffect { public int Changes;public bool IsBuy;public void ShowModeEffect(bool isBuy){Changes++;IsBuy=isBuy;} }
public sealed class NativeAreaController { public bool IsFocused; }
public sealed class UIEnhancementCardHighlighter
{
    public AbilityCardUI? Card;public bool Invalid;
    public void ShowCard(CAbilityCard card,bool animateShow=false){Card=NativeCards.Faces[card];Invalid=false;}
    public void ShowNoEnhanceCard(CAbilityCard card,bool animateShow=false){Card=NativeCards.Faces[card];Invalid=true;}
    public void Hide(){Card=null;}
}
public class UIEnhancementButtonHighlight : MonoBehaviour
{
    public EnhancementButtonBase? Ability;public bool Selected,Buy;public Action<EnhancementButtonBase>? OnSelected;
    public void HighlightPreview(RectTransform parent,EnhancementButtonBase ability){Ability=ability;Selected=false;OnSelected=null;}
    public void HighlightSelected(RectTransform parent,EnhancementButtonBase ability){Ability=ability;Selected=true;}
    public void HighlightSelectable(RectTransform parent,EnhancementButtonBase ability,Action<EnhancementButtonBase> callback){Ability=ability;Selected=false;OnSelected=callback;}
    public void SetMode(bool buy){Buy=buy;}
}
public static class NativeCards { public static readonly Dictionary<CAbilityCard,AbilityCardUI> Faces=new(); }
public partial class AbilityCardUI { public CardEnhancementElements EnhancementElements=new(); }
public partial class NativeEnhancementShop
{
    public List<EnhancementSlot> Options=new();
    public void Display(object character,List<EnhancementSlot> options){Options=options;}
}
public partial class UIPartyCharacterEnhancementAbilityCardsDisplay
{
    public bool IsBuy;public int ModeRefreshes;
    public void RefreshMode(bool isBuy){IsBuy=isBuy;ModeRefreshes++;}
}
namespace MapRuleLibrary.Adventure
{
    public sealed partial class MapPartyEnhancementShopService
    {
        // Exact game enhancement eligibility/payment services are outside this
        // mode/highlight proof. Supply options only for actual empty native areas.
        public List<EnhancementSlot> GetEnhancementsToBuy(EnhancementLine line)=>line.EnhancementSlots
            .Where(slot=>slot.EnhancedType==EEnhancement.NoEnhancement)
            .Select(slot=>new EnhancementSlot(slot,EEnhancement.PlusOne,true,line)).ToList();
    }
}
public partial class UINewEnhancementWindow
{
    private ShopMode mode;
    private UIEnhancementCardHighlighter cardHolder=new();
    private GameObject cardInformation=null!;
    private NativeText cardInformationText=new();
    private NativeEffect enhanctressEffect=new();
    private UIPartyCharacterEnhancementAbilityCardsDisplay cardsDisplay=>CardsDisplay;
    private EnhancementLineFilter enhancementLineFilter=null!;
    private NativeAreaController controllerArea=new();
    private List<UIEnhancementButtonHighlight> highlightAbilityPool=new(),highlightAbility=new();
    private Transform highlightAbilityParent=null!;
    private bool GamepadMode=>InputManager.GamePadInUse;
    public AbilityCardUI? SowingCard {get;private set;}
    public int NativeMode=>(int)mode;
    public string NativeCardMessage=>cardInformationText.Key;
    public int NativeAreaCount=>highlightAbility.Count;
    public bool NativeInvalid=>cardHolder.Invalid;
    public UIEnhancementButtonHighlight NativeArea(int index)=>highlightAbility[index];
    public bool NativeBuyEffect=>enhanctressEffect.IsBuy;
    public bool NativeInformationVisible=>cardInformation.activeSelf;
    public void Configure(Transform parent,bool alreadyOn,int previousMode)
    {
        buyButton=new GameObject("Native buy toggle",typeof(RectTransform),typeof(NativeBuyTab)).GetComponent<NativeBuyTab>();
        sellButton=new GameObject("Native sell toggle",typeof(RectTransform),typeof(NativeBuyTab)).GetComponent<NativeBuyTab>();
        buyButton.isOn=alreadyOn;mode=(ShopMode)previousMode;
        buyButton.gameObject.transform.SetParent(parent,false);sellButton.gameObject.transform.SetParent(parent,false);
        cardInformation=new GameObject("Original native card information",typeof(RectTransform));cardInformation.transform.SetParent(parent,false);
        highlightAbilityParent=new GameObject("Original enhancement highlight pool",typeof(RectTransform)).transform;highlightAbilityParent.SetParent(parent,false);
        for(int i=0;i<4;i++){var area=new GameObject("Native area "+i,typeof(RectTransform),typeof(UIEnhancementButtonHighlight)).GetComponent<UIEnhancementButtonHighlight>();area.transform.SetParent(highlightAbilityParent,false);highlightAbilityPool.Add(area);}
        enhancementLineFilter=new EnhancementLineFilter(OnSelectedEnhacementButton);
        InstallOriginalTabCallback();
    }
    private void ClearPreviewEnhancement(){}
    private void TrySelectLineOrDefault(EnhancementLine line){}
    private void EnableCardHighlightsNavigation(){}
}
