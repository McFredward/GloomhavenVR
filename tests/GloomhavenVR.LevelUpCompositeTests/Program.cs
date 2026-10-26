using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string reason)
    { ++_checks;if(!value)throw new InvalidOperationException("Level-up composite assertion: "+reason); }
    private static bool Near(float a,float b)=>Math.Abs(a-b)<.01f;
    private static bool Same(Vector2 a,Vector2 b)=>Near(a.x,b.x)&&Near(a.y,b.y);
    private static bool Same(Vector3 a,Vector3 b)=>Near(a.x,b.x)&&Near(a.y,b.y)&&Near(a.z,b.z);
    private static RectTransform Rect(string name,Transform? parent,float width,float height)
    {
        var value=(RectTransform)new GameObject(name).transform;
        value.SetParent(parent,false);value.sizeDelta=new(width,height);return value;
    }
    private sealed class Fixture
    {
        internal readonly RectTransform Campaign,Host,Party,Level,Preview,Inventory,Before,After,Owned,OwnedScroll;
        internal readonly UIWindow PartyWindow,LevelWindow,InventoryWindow;
        internal readonly UILevelUpWindow Native;
        internal readonly UIResetLevelUpWindow Reset;
        internal readonly UILevelUpCardInventory Cards;
        internal readonly ConvertedPanel Panel;
        internal readonly NativeComparisonCards Comparison;
        internal readonly Vector3 OwnedPosition,OwnedScale;
        internal readonly Quaternion OwnedRotation;
        internal readonly Vector2 OwnedAnchorMin,OwnedAnchorMax,OwnedPivot,OwnedSize;
        internal readonly int OwnedSibling;
        internal int Compared;
        internal readonly Vector2 AnchorMin,AnchorMax,Pivot,Size;
        internal readonly Vector3 Position,Scale;
        internal readonly Quaternion Rotation;
        internal readonly int Sibling;
        internal int Selected;
        internal Fixture(float worldScale=1,float yaw=0)
        {
            LevelUpComposite.Reset();CanvasConversion.ActivePanels.Clear();ModalFallback.ReleaseRequests=0;ModalFallback.RefuseRelease=false;ModalFallback.BeforeRelease=null;
            WorldUIConfig.ConversionActive=true;FlatScreen.ManualScreenActive=false;VRModeStateMachine.TableInFrontOfPlayer=true;
            Campaign=Rect("Campaign Canvas",null,1920,1080);
            Before=Rect("Earlier sibling",Campaign,10,10);
            Inventory=Rect("LevelUpInventory (1) Variant",Campaign,368,1080);
            After=Rect("Later sibling",Campaign,10,10);
            Inventory.gameObject.layer=5;
            Inventory.anchorMin=new(1,.5f);Inventory.anchorMax=new(1,.5f);Inventory.pivot=new(.2f,.8f);
            Inventory.anchoredPosition3D=new(368,29,7);Inventory.localScale=new(.9f,.8f,1);Inventory.localRotation=Quaternion.Euler(0,0,2);
            var nested=Rect("Card list native content",Inventory,200,400);nested.gameObject.layer=6;
            Cards=Inventory.gameObject.AddComponent<UILevelUpCardInventory>();
            InventoryWindow=Inventory.gameObject.AddComponent<UIWindow>();Cards.window=InventoryWindow;
            Cards.Selected+=()=>++Selected;
            Host=Rect("GloomhavenVR.Panel_Modal_New Party display",null,1988,1080);
            Host.localScale=new(worldScale,worldScale,worldScale);Host.localRotation=Quaternion.Euler(0,yaw,0);Host.localPosition=new(17,21,-13);
            Party=Rect("New Party display",Host,1920,1080);Party.gameObject.layer=27;
            PartyWindow=Party.gameObject.AddComponent<UIWindow>();PartyWindow.ID=UIWindowID.PartyPanel;
            var party=Party.gameObject.AddComponent<NewPartyDisplayUI>();party.window=PartyWindow;NewPartyDisplayUI.PartyDisplay=party;
            Owned=Rect("Character Ability Cards Display",Party,749,1080);Owned.anchoredPosition3D=new(290,15,2);Owned.gameObject.layer=27;
            Owned.anchorMin=Owned.anchorMax=new(.4f,.5f);Owned.pivot=new(.3f,.7f);Owned.localScale=new(1.1f,.9f,1);Owned.localRotation=Quaternion.Euler(0,0,3);
            Comparison=Owned.gameObject.AddComponent<NativeComparisonCards>();Comparison.Selected+=()=>++Compared;party.AbilityCardsDisplay=Comparison;
            OwnedScroll=Rect("Original owned card scroll content",Owned,640,900);OwnedScroll.anchoredPosition=new(12,47);
            OwnedPosition=Owned.anchoredPosition3D;OwnedScale=Owned.localScale;OwnedRotation=Owned.localRotation;
            OwnedAnchorMin=Owned.anchorMin;OwnedAnchorMax=Owned.anchorMax;OwnedPivot=Owned.pivot;OwnedSize=Owned.sizeDelta;OwnedSibling=Owned.GetSiblingIndex();
            Level=Rect("Hero Level Up Panel",Party,1920,1080);Level.gameObject.layer=27;
            LevelWindow=Level.gameObject.AddComponent<UIWindow>();LevelWindow.ID=UIWindowID.HeroLevelUpPanel;
            Native=Level.gameObject.AddComponent<UILevelUpWindow>();Native.myWindow=LevelWindow;Native.inventory=Cards;Native.IsShowing=false;
            var highlighter=Rect("Highlighter",Level,350,500).gameObject.AddComponent<UILevelUpCardHighlighter>();
            var holder=Rect("Preview holder",highlighter.transform,350,500).gameObject.AddComponent<UILevelUpCardHolder>();
            var card=Rect("Preview card",holder.transform,350,500).gameObject.AddComponent<UILevelUpCard>();
            Preview=Rect("Full ability card",card.transform,300,450);Preview.anchoredPosition=new(185,0);Preview.localScale=new(.8f,.8f,1);
            card.fullAbilityCard=Preview.gameObject.AddComponent<FullAbilityCard>();holder.Card=card;highlighter.cardHolder=holder;Native.cardHolder=highlighter;
            Native.header=Rect("Header",Level,500,80);
            Reset=Inventory.gameObject.AddComponent<UIResetLevelUpWindow>();Reset.inventory=Cards;
            Reset.displayToggle=Rect("Reset toggle",Party,40,40).gameObject.AddComponent<Toggle>();
            Reset.resetPanel=new GameObject("Reset controls");
            Singleton<UILevelUpWindow>.Instance=Native;Singleton<UIResetLevelUpWindow>.Instance=Reset;
            Panel=new ConvertedPanel{Target=Party,HostRect=Host,HostCanvas=Host.gameObject.AddComponent<Canvas>()};CanvasConversion.ActivePanels.Add(Panel);
            AnchorMin=Inventory.anchorMin;AnchorMax=Inventory.anchorMax;Pivot=Inventory.pivot;Size=Inventory.sizeDelta;
            Position=Inventory.anchoredPosition3D;Scale=Inventory.localScale;Rotation=Inventory.localRotation;Sibling=Inventory.GetSiblingIndex();
        }
        internal void Tick()=>LevelUpComposite.Tick();
        internal void SettleSlide(){Inventory.anchoredPosition3D=Vector3.zero;Tick();}
        internal void CheckRestored(string reason)
        {
            Check(ReferenceEquals(Inventory.parent,Campaign),reason+": original parent restored");
            Check(Inventory.GetSiblingIndex()==Sibling,reason+": original sibling restored");
            Check(Same(Inventory.anchorMin,AnchorMin)&&Same(Inventory.anchorMax,AnchorMax)&&Same(Inventory.pivot,Pivot),reason+": anchors and pivot restored");
            Check(Same(Inventory.sizeDelta,Size)&&Same(Inventory.anchoredPosition3D,Position)&&Same(Inventory.localScale,Scale),reason+": native rect, position and scale restored");
            Check(Inventory.localRotation.Value.Equals(Rotation.Value),reason+": rotation restored");
            Check(Inventory.gameObject.layer==5&&Inventory.GetChild(0).gameObject.layer==6,reason+": original per-child layers restored");
            Check(InventoryWindow.NativeShowCalls==0&&InventoryWindow.NativeHideCalls==0,reason+": native show/hide callbacks untouched");
            Check(ReferenceEquals(Owned.parent,Party)&&Owned.GetSiblingIndex()==OwnedSibling,reason+": comparison parent and sibling restored");
            Check(Same(Owned.anchorMin,OwnedAnchorMin)&&Same(Owned.anchorMax,OwnedAnchorMax)&&Same(Owned.pivot,OwnedPivot),reason+": comparison anchors and pivot restored");
            Check(Same(Owned.sizeDelta,OwnedSize)&&Same(Owned.anchoredPosition3D,OwnedPosition)&&Same(Owned.localScale,OwnedScale)&&Owned.localRotation.Value.Equals(OwnedRotation.Value),reason+": comparison native geometry restored");
        }
    }
    private static Rect Bounds(RectTransform rect,RectTransform space)
    {
        var corners=new Vector3[4];rect.GetWorldCorners(corners);Vector2 min=new(float.PositiveInfinity,float.PositiveInfinity),max=new(float.NegativeInfinity,float.NegativeInfinity);
        foreach(Vector3 corner in corners){Vector2 point=space.InverseTransformPoint(corner);min=Vector2.Min(min,point);max=Vector2.Max(max,point);}
        return UnityEngine.Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    private static void Main()
    {
        var metrics=new List<object>();
        foreach((float scale,float yaw) in new[]{(1f,0f),(.04f,53f),(198.12f,-79f)})
        {
            var f=new Fixture(scale,yaw);f.Tick();
            Check(f.Inventory.IsChildOf(f.Level),"sibling inventory becomes part of actual native level-up owner");
            Check(!ReferenceEquals(f.Inventory.parent,f.Level),"separate mount preserves native inventory slide");
            Check(ModalFallback.ReleaseRequests==1,"standalone float retires before native home capture");
            Check(ReferenceEquals(LevelUpComposite.FittedView,f.LevelWindow)&&!f.Native.IsShowing,"native open level-up stays fitted during choice after IsShowing clears");
            Check(ReferenceEquals(LevelUpComposite.FindContainingPanel(f.LevelWindow),f.Panel),"nested level-up resolves actual party conversion rather than an unrelated float");
            Check(Near(f.Inventory.anchoredPosition3D.x,f.Position.x),"native inventory horizontal slide displacement retained");
            f.SettleSlide();Rect preview=Bounds(f.Preview,f.Level),list=Bounds(f.Inventory,f.Level);
            Check(list.xMin>=preview.xMax+ChromeParkTuning.ChromeGapPx-.01f,"full preview right edge remains readable beside inventory");
            Check(!list.Overlaps(preview),"native card list cannot cover twenty percent of preview");
            Rect owned=Bounds(f.Owned,f.Level);
            Check(f.Owned.IsChildOf(f.Level)&&!ReferenceEquals(f.Owned.parent,f.Level),"existing owned deck shares level root through independent mount");
            Check(owned.xMax<=preview.xMin-ChromeParkTuning.ChromeGapPx+.01f&&!owned.Overlaps(preview),"existing owned deck cannot obscure any preview edge");
            Check(!owned.Overlaps(list),"existing and unowned card lists remain separate");
            f.Comparison.Select();Check(f.Compared==1,"original owned-card callback remains wired exactly once");
            Vector2 scrollBefore=f.OwnedScroll.anchoredPosition;f.OwnedScroll.anchoredPosition+=new Vector2(0,71);f.Tick();
            Check(Same(f.OwnedScroll.anchoredPosition,scrollBefore+new Vector2(0,71)),"composite leaves original comparison scroll animation intact");
            Check(f.Inventory.gameObject.layer==27&&f.Inventory.GetChild(0).gameObject.layer==27,"entire native inventory joins owning capture layer");
            f.Cards.Select();Check(f.Selected==1,"original native card-selection callback remains wired exactly once");
            float seat=f.Inventory.parent!.localPosition.x;
            f.Preview.sizeDelta=new(430,450);f.Tick();f.SettleSlide();preview=Bounds(f.Preview,f.Level);list=Bounds(f.Inventory,f.Level);
            Check(list.xMin>=preview.xMax+ChromeParkTuning.ChromeGapPx-.01f,"larger later preview cannot be hidden behind inventory");
            Check(Bounds(f.Owned,f.Level).xMax<=preview.xMin-ChromeParkTuning.ChromeGapPx+.01f,"larger later preview cannot be hidden behind original owned deck");
            float enlarged=f.Inventory.parent!.localPosition.x;
            f.Preview.sizeDelta=new(100,450);f.Tick();Check(Near(f.Inventory.parent!.localPosition.x,enlarged),"hover shrink cannot jitter list back across preview envelope");
            Check(enlarged>seat,"preview growth expands actual seat");
            metrics.Add(new{worldScale=scale,yaw,previewRight=preview.xMax,listLeft=list.xMin,gap=list.xMin-preview.xMax});
            for(int i=0;i<120;i++)f.Tick();Check(ModalFallback.ReleaseRequests==1,"steady state does not retransfer or replay native show");
            f.InventoryWindow.IsOpen=false;f.LevelWindow.IsOpen=false;f.Tick();f.CheckRestored("native hide after selected card");
            Check(!f.InventoryWindow.IsOpen&&!f.LevelWindow.IsOpen,"presentation restore never reopens closed native windows");
        }
        foreach(string mode in new[]{"conversion-off","manual-screen","leave-table","panel-dead","inventory-hidden","owner-hidden","singleton-replaced","shutdown"})
        {
            var f=new Fixture();f.Tick();
            switch(mode)
            {
                case "conversion-off":WorldUIConfig.ConversionActive=false;break;
                case "manual-screen":FlatScreen.ManualScreenActive=true;break;
                case "leave-table":VRModeStateMachine.TableInFrontOfPlayer=false;break;
                case "panel-dead":f.Panel.IsAlive=false;break;
                case "inventory-hidden":f.InventoryWindow.IsOpen=false;break;
                case "owner-hidden":f.LevelWindow.IsOpen=false;break;
                case "singleton-replaced":Singleton<UILevelUpWindow>.Instance=null;break;
                case "shutdown":LevelUpComposite.Reset();break;
            }
            if(mode!="shutdown")f.Tick();f.CheckRestored(mode);
            Check(!f.Inventory.Destroyed,mode+": inventory is returned before mount destruction");
        }
        {
            var f=new Fixture();f.Tick();Transform oldMount=f.Inventory.parent!;
            f.LevelWindow.IsOpen=false;f.Reset.displayToggle.isOn=true;f.Tick();
            Check(f.Inventory.IsChildOf(f.Party)&&!f.Inventory.IsChildOf(f.Level),"reset browse rebinds same inventory to current party owner");
            Check(oldMount.Destroyed,"owner change removes previous empty mount");
            f.SettleSlide();Check(Bounds(f.Inventory,f.Party).xMin>=Bounds((RectTransform)NewPartyDisplayUI.PartyDisplay!.AbilityCardsDisplay!.transform,f.Party).xMax+ChromeParkTuning.ChromeGapPx-.01f,"reset list sits beside original character cards");
            f.Reset.displayToggle.isOn=false;f.Tick();f.CheckRestored("reset browse closed");
            f.LevelWindow.IsOpen=true;f.InventoryWindow.IsOpen=true;f.Tick();Check(f.Inventory.IsChildOf(f.Level),"next level-up uses current owner after reset browse");
            LevelUpComposite.Reset();f.CheckRestored("next character shutdown");
        }
        {
            var f=new Fixture();f.LevelWindow.IsOpen=false;f.Reset.displayToggle.isOn=true;
            Singleton<UILevelUpWindow>.Instance=null;f.Tick();
            Check(f.Inventory.IsChildOf(f.Party),"reset browse resolves serialized inventory without a level-up singleton");
            Check(ReferenceEquals(LevelUpComposite.InventoryView,f.Cards),"fixed-fit inventory reference survives missing level-up singleton");
            f.Reset.displayToggle.isOn=false;f.Tick();f.CheckRestored("reset-only native scene");
        }
        {
            var f=new Fixture();f.Tick();var replaced=f.Owned;Transform priorMount=replaced.parent!;
            var next=Rect("Replacement original comparison",f.Party,560,900);var nextController=next.gameObject.AddComponent<NativeComparisonCards>();
            NewPartyDisplayUI.PartyDisplay!.AbilityCardsDisplay=nextController;f.Tick();
            Check(ReferenceEquals(replaced.parent,f.Party)&&Same(replaced.anchoredPosition3D,f.OwnedPosition),"replaced comparison returns original pose before next source is adopted");
            Check(priorMount.Destroyed&&next.IsChildOf(f.Level),"comparison replacement retires former mount and adopts current source");
            LevelUpComposite.Reset();Check(ReferenceEquals(next.parent,f.Party),"replacement comparison returns to its own parent on shutdown");f.CheckRestored("comparison replaced");
        }
        {
            var f=new Fixture();ModalFallback.RefuseRelease=true;f.Tick();f.CheckRestored("float refuses transfer");
            Check(ReferenceEquals(f.Inventory.parent,f.Campaign),"active native close cannot be stolen by composite");
            ModalFallback.RefuseRelease=false;f.Tick();Check(f.Inventory.IsChildOf(f.Level),"transfer retries when prior standalone close finishes");
            LevelUpComposite.Reset();
        }
        {
            var f=new Fixture();
            var previousFloat=Rect("Previous standalone float",null,368,1080);
            f.Inventory.SetParent(previousFloat,false);
            ModalFallback.BeforeRelease=window=>
            {
                Check(ReferenceEquals(window,f.InventoryWindow),"handover identifies exact original inventory window");
                f.Inventory.SetParent(f.Campaign,false);f.Inventory.SetSiblingIndex(f.Sibling);
            };
            f.Tick();Check(f.Inventory.IsChildOf(f.Level),"already floated inventory transfers into its native owner");
            LevelUpComposite.Reset();f.CheckRestored("standalone handover records restored home");
        }
        {
            var f=new Fixture();CanvasConversion.ActivePanels.Clear();
            var unrelated=Rect("Other open window",null,500,500);CanvasConversion.ActivePanels.Add(new ConvertedPanel{Target=unrelated,HostRect=unrelated});
            f.Tick();Check(ReferenceEquals(f.Inventory.parent,f.Campaign),"unrelated open window never becomes level-up owner");
            LevelUpComposite.Reset();
        }
        string? report=Environment.GetEnvironmentVariable("GHVR_LEVELUP_REPORT");
        if(!string.IsNullOrEmpty(report))File.WriteAllText(report,JsonSerializer.Serialize(new{checks=_checks,geometry=metrics,scope="Production ownership/rect/layer state machine over bounded native API model; no renderer/headset claim."},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Level-up composite: {_checks} assertions passed.");
    }
}
