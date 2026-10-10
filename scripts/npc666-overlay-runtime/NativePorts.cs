// External asset/shop/navigation ports. Native model, construction, availability,
// highlight and click bodies are bound from the read-only game DLL by run.py.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI
{
    internal static class Native666
    {
        internal static string Data = "";
        internal static readonly Dictionary<string, GameObject> Prefabs = new();
        internal static CAbilityCard Card(int id) => CharacterClassManager.AllAbilityCards.Single(c => c.ID == id);
        internal static CardEnhancementElements NewElements()
        {
            var elements=new CardEnhancementElements();
            typeof(CardEnhancementElements).GetField("_all",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(elements,new List<EnhancementButtonBase>());
            return elements;
        }
        internal static void Load(string directory, string rules)
        {
            Data = directory;
            SharedLibrary.Client.SharedClient.InitValidationRecord();
            var y = new CSRLYML { YMLMode = CSRLYML.EYMLMode.Global };
            typeof(ScenarioRuleClient).GetField("s_SRLYML", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, y);
            using var z = ZipFile.OpenRead(rules);
            foreach(var e in z.Entries.Where(e=>e.FullName.StartsWith("AttackModifier/")))
                using(var r=Reader(e,"AttackModifier"))if(!y.GlobalData.AttackModifiers.ProcessFile(r,e.FullName))throw new Exception("native attack modifier parse failed "+e.FullName);
            foreach(var e in z.Entries.Where(e=>e.FullName=="AttackModifierDeck/Standard.yml"))
                using(var r=Reader(e,"AttackModifierDeck"))if(!y.GlobalData.AttackModifierDecks.ProcessFile(r,e.FullName))throw new Exception("native deck parse failed "+e.FullName);
            foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("HeroSummon/")))
                using (var r = Reader(e,"HeroSummon")) if (!y.GlobalData.HeroSummons.ProcessFile(r,e.FullName)) throw new Exception("native summon parse failed " + e.FullName);
            foreach (var e in z.Entries.Where(e => e.FullName=="Character/Summoner.yml" || e.FullName=="Character/Spellweaver.yml"))
                using (var r = Reader(e,"Character")) if (!y.GlobalData.Characters.ProcessFile(r,e.FullName)) throw new Exception("native character parse failed " + e.FullName);
            foreach (var e in z.Entries.Where(e => e.FullName.StartsWith("AbilityCard/") && (e.FullName.Contains("_Summoner_") || e.FullName.Contains("_Spellweaver_"))))
                using (var r = Reader(e,"AbilityCard")) if (!y.GlobalData.AbilityCards.ProcessFile(r,e.FullName)) throw new Exception("native card parse failed " + e.FullName);
            CharacterClassManager.Load();
            var bank=new GameObject("Serialized native prefab bank");bank.SetActive(false);
            foreach(string key in new[]{"SummonContainer","PreviewText","EnhancementContainer","Enhancement"})
                Prefabs[key]=MirrorProgram.Import666(key,bank.transform);
        }
        private static StreamReader Reader(ZipArchiveEntry entry,string parser)
        {
            var reader=new StreamReader(entry.Open());
            // Native ruleset dispatch consumes the Parser header before handing
            // the remaining unchanged stream to its table's ProcessFile method.
            if(reader.ReadLine()!="Parser: "+parser)throw new InvalidDataException("Unexpected original parser "+entry.FullName);
            return reader;
        }
    }
    internal sealed class AssetBundleManager
    {
        internal static readonly AssetBundleManager Instance = new();
        internal T LoadAssetFromBundle<T>(string bundle,string key,string group) where T:UnityEngine.Object
            => (T)(UnityEngine.Object)Native666.Prefabs[key];
    }
    internal sealed class AbilityCardSettings666
    {
        internal TMP_FontAsset CardFont => TMP_Settings.defaultFontAsset;
        // Font atlas and these settings are declared layout-size boundaries;
        // both printed original and selectable source consume the same settings.
        internal float SummonStatFontSize=16,EnhancementIconSize=12,EnhancementIconSpacing=2,EnhancementIconInitialSpace=3;
    }
    internal sealed class GlobalSettings
    {
        internal static readonly GlobalSettings Instance=new();
        internal readonly AbilityCardSettings666 m_AbilityCardSettings=new();
    }
    internal sealed class UITextTooltipTarget:MonoBehaviour { internal void SetText(string text) {} }
    internal sealed class InfuseElement:MonoBehaviour { internal void Init(ElementInfusionBoardManager.EElement value){} }
    public sealed class EnhancedAreaHex:EnhancementButtonBase {}
    public sealed class EnhancementButton:EnhancementButtonBase
    {
        internal InfuseElement InfuseElement=null!;
        private bool isInitialized;
        private Image enhancementImage=null!;
        private Sprite[] enhancementSprites=new Sprite[18];
        private UITextTooltipTarget tooltipTarget=null!;
        // The prefab's visual icon assets/tooltips are not a gameplay authority.
        // Initialise the actual members before the bound Init/Update bodies run.
        internal void FixtureAssets()
        {
            enhancementImage=GetComponentInChildren<Image>(true);
            tooltipTarget=GetComponent<UITextTooltipTarget>()??gameObject.AddComponent<UITextTooltipTarget>();
            var go=new GameObject("Native infusion port");go.transform.SetParent(transform,false);InfuseElement=go.AddComponent<InfuseElement>();
        }
        private void Awake()=>FixtureAssets();
        NATIVE_ENHANCEMENT_METHODS
    }
    internal static class InputManager { internal static bool GamePadInUse=>false; }
    internal sealed class ExtendedButton:Button
    {
        public UnityEvent onSelected=new(),onDeselected=new();
        internal bool IsNavigationEnabled;
        // Actual ExtendedButton ultimately invokes Button.OnPointerClick; audio,
        // automated-input recording and the game's UI lock are external ports.
    }
    public sealed class UINavigationSelectable:MonoBehaviour {}
    internal sealed class UiNavigationRoot:MonoBehaviour {}
    internal sealed class NavigationState666
    { internal int Enters; internal void Enter(CampaignMapStateTag tag,object payload){Enters++;} }
    internal sealed class UINavigation
    { internal readonly NavigationState666 StateMachine=new();internal readonly Input666 Input=new(); }
    internal sealed class Input666 { internal bool GamePadInUse=>false; }
    internal static class Singleton<T> where T:new() { internal static T Instance=new(); }
    internal enum CampaignMapStateTag { EnhancmentSelectOptionUpgrade }
    internal static class EnhancmentSelectOptionUpgradeState
    { internal sealed class Data { internal bool SelectedFirst;internal UiNavigationRoot? PreviousRoot; } }
    internal static class CoreApplication { internal static bool IsQuitting=>false; }
    internal sealed class UIInfoTools
    { internal static readonly UIInfoTools Instance=new();internal Color buyEnhancementColor=new(.807843f,.678431f,.352941f,1),sellEnhancementColor=Color.red; }
    internal sealed class ControllerArea666 { internal bool IsFocused; }
    internal enum ShopMode { BUY,SELL }
    internal sealed class UINewEnhancementWindow
    {
        internal UIEnhancementCardHighlighter cardHolder=null!;
        internal readonly List<UIEnhancementButtonHighlight> highlightAbilityPool=new(),highlightAbility=new();
        internal Transform highlightAbilityParent=null!;
        internal int FilterCalls;
        internal readonly EnhancementLineFilter enhancementLineFilter;
        internal UINewEnhancementWindow(){enhancementLineFilter=new EnhancementLineFilter(_=>FilterCalls++);}
        internal readonly ControllerArea666 controllerArea=new();
        internal ShopMode mode;
        internal void Initialise()=>SetEnhanceFilters(cardHolder.Card!);
        internal void Refresh()=>HighlightButtons();
        private void EnableCardHighlightsNavigation(){foreach(var a in highlightAbility)a.EnableNavigation();}
        NATIVE_WINDOW_METHODS
    }
    internal sealed class PokeOnlyTarget:MonoBehaviour {}
    internal sealed class Pointer666
    {
        private readonly List<RaycastResult> _hits=new();private bool _farRay=true;
        private int _lastPokeHitCount,_lastPokePadRank;private GameObject? _lastPokeRunnerUp;
        internal bool Pick(GraphicRaycaster caster,PointerEventData data,out RaycastResult top)=>TryRaycastTop(caster,data,out top);
        NATIVE_RAYCAST_METHOD
    }
    internal partial class TownServiceEnhancementHandoff
    {
        // The owning offered-card/canvas/session gate is an explicit input port.
        // The actual production hit validation below must still find a real Ability.
        internal static bool TryNativeAreaCanvas(Canvas canvas,out GloomhavenVR.Cards.VRCard? offered)
        { offered=TownServicePresentation.Ritual?.Handoff?.Card;return offered!=null&&canvas!=null; }
        NATIVE_AREA_METHOD
    }
}
