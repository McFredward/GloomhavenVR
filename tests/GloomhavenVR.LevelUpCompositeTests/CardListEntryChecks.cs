using System;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;

internal static partial class Program
{
    private static void CardListEntryChecks()
    {
        var f=new Fixture(.04f,53f);f.LevelWindow.IsOpen=false;f.InventoryWindow.IsOpen=false;
        var party=NewPartyDisplayUI.PartyDisplay!;party.ActiveDisplay=NewPartyDisplayUI.DisplayType.CARDS;
        var toggle=f.Reset.displayToggle;var rect=(RectTransform)toggle.transform;
        rect.SetParent(f.Inventory,false);rect.anchorMin=rect.anchorMax=new Vector2(0,1);
        rect.pivot=Vector2.one;rect.sizeDelta=new Vector2(43,65);rect.anchoredPosition3D=new Vector3(0,0,3);
        rect.localRotation=Quaternion.Euler(0,0,2);rect.localScale=new Vector3(.9f,1.1f,1);
        rect.gameObject.layer=5;toggle.gameObject.SetActive(false);
        Vector3 home=rect.anchoredPosition3D,scale=rect.localScale;Quaternion rotation=rect.localRotation;
        var hint=Rect("inactive native controller hint",f.Owned,400,50).gameObject.AddComponent<TextMeshProUGUI>();
        hint.font=new Object();hint.fontSize=100;hint.gameObject.SetActive(true); // Even an active gamepad hint is never a caption template.
        var header=Rect("Header",Rect("Container",f.Owned,388,1080),368,50);
        var font=Rect("Title",header,340,40).gameObject.AddComponent<TextMeshProUGUI>();
        font.font=new Object();font.fontSharedMaterial=new Object();font.fontSize=26;
        int opened=0,closed=0;
        // The native game callback/service is a declared port in this portable
        // geometry suite; the real Unity companion exercises Toggle event dispatch.
        toggle.ValueChanged+=on=>{f.InventoryWindow.IsOpen=on;if(on)opened++;else closed++;};
        f.Tick();Check(ReferenceEquals(rect.parent,f.Inventory),"native inactive opener is never made available by the mod");
        toggle.gameObject.SetActive(true); // Original UIResetLevelUpWindow.Show.
        f.Tick();
        Check(rect.IsChildOf(f.Owned)&&!rect.IsChildOf(f.Inventory),"closed inventory opener joins original character Cards view before the inventory opens");
        Check(!f.InventoryWindow.IsOpen&&opened==0,"parking native opener does not invoke native show or change toggle value");
        Check(Same(rect.sizeDelta,new Vector2(43,65)),"native arrow frame stays authored43x65");
        var label=rect.GetComponentsInChildren<TextMeshProUGUI>(true).Single();
        Check(label.text=="More Cards"&&ReferenceEquals(label.font,font.font)&&ReferenceEquals(label.fontSharedMaterial,font.fontSharedMaterial),"entry caption follows native typography");
        Check(label.raycastTarget&&ReferenceEquals(label.GetComponentInParent<Toggle>(),toggle),"caption graphic dispatches to the original native toggle");
        Transform mount=rect.parent!;f.Tick();Check(ReferenceEquals(rect.parent,mount),"closed opener does not cycle parent or allocate another mount every frame");
        label.GetComponentInParent<Toggle>()!.Click();f.Tick();
        Check(opened==1&&toggle.isOn&&f.Inventory.IsChildOf(f.Owned),"clicking native opener opens its original card list inside the same character window");
        toggle.interactable=false;label.GetComponentInParent<Toggle>()!.Click();
        Check(toggle.isOn&&opened==1&&closed==0,"native disabled toggle cannot be bypassed through its caption");
        toggle.interactable=true;Loc.Language(true);Check(label.text=="Weitere Karten","open caption follows native language changes");
        toggle.Click();f.Tick();
        Check(closed==1&&!f.InventoryWindow.IsOpen&&ReferenceEquals(f.Inventory.parent,f.Campaign),"native toggle closes inventory and preserves original close callback");
        Check(ReferenceEquals(rect.parent,mount),"closed native list retains its reachable opener");
        foreach(string reason in new[]{"tab","manual","permission","show-lock","VR","owner","conversion","table","hidden-window","hidden-view"})
        {
            switch(reason)
            {
                case "tab":party.ActiveDisplay=NewPartyDisplayUI.DisplayType.PERKS;break;
                case "manual":FlatScreen.ManualScreenActive=true;break;
                case "permission":party.IsDisableFurtherAbilityPanel=true;break;
                case "show-lock":f.Reset.requestDisableShow.Add(f.Native);break;
                case "VR":VRSession.IsRunning=false;break;
                case "owner":f.Panel.IsAlive=false;break;
                case "conversion":WorldUIConfig.ConversionActive=false;break;
                case "table":VRModeStateMachine.TableInFrontOfPlayer=false;break;
                case "hidden-window":f.PartyWindow.IsOpen=false;break;
                case "hidden-view":f.Owned.gameObject.SetActive(false);break;
            }
            f.Tick();
            Check(ReferenceEquals(rect.parent,f.Inventory)&&Same(rect.anchoredPosition3D,home)&&Same(rect.localScale,scale)
                &&rect.localRotation.Value.Equals(rotation.Value),reason+": opener returns its native geometry and parent");
            Check(toggle.gameObject.activeSelf&&toggle.interactable&&!toggle.isOn,reason+": native active/permission/value remains untouched");
            party.ActiveDisplay=NewPartyDisplayUI.DisplayType.CARDS;FlatScreen.ManualScreenActive=false;
            party.IsDisableFurtherAbilityPanel=false;f.Reset.requestDisableShow.Clear();VRSession.IsRunning=true;f.Panel.IsAlive=true;
            WorldUIConfig.ConversionActive=true;VRModeStateMachine.TableInFrontOfPlayer=true;f.PartyWindow.IsOpen=true;f.Owned.gameObject.SetActive(true);
            f.Tick();Check(rect.IsChildOf(f.Owned),reason+": same native opener can rejoin its current Cards owner");
        }
        mount=rect.parent!;toggle.gameObject.SetActive(false);
        Transform.RefuseParent=(node,parent)=>ReferenceEquals(node,rect)&&ReferenceEquals(parent,f.Inventory);
        f.Tick();Check(ReferenceEquals(rect.parent,mount)&&!rect.Destroyed&&!mount.Destroyed,"refused native opener restoration retains original and its mount");
        Transform.RefuseParent=(node,parent)=>{if(ReferenceEquals(node,rect)&&ReferenceEquals(parent,f.Inventory))throw new InvalidOperationException("native reparent callback");return false;};
        f.Tick();Check(ReferenceEquals(rect.parent,mount)&&!rect.Destroyed&&!mount.Destroyed,"throwing native opener restore retains original and receipt for retry");
        Transform.RefuseParent=null;f.Tick();
        Check(ReferenceEquals(rect.parent,f.Inventory)&&mount.Destroyed&&!rect.Destroyed,"retry restores native opener before retiring only its empty mount");
        Check(opened==1&&closed==1,"all presentation transitions preserve the original callback count");
        Check(rect.gameObject.layer==5,"original native opener layer restored");
        Check(Loc.Subscribers==0,"returned native entry removes its language-change subscription");
        // Unity-null homes can die during scene retirement while the borrowed
        // original survives under its converted owner. Restore safely to world.
        toggle.gameObject.SetActive(true);f.Tick();Object.Destroy(f.Inventory.gameObject);
        LevelUpComposite.Reset();Check(ReferenceEquals(rect.parent,null)&&!rect.Destroyed,"destroyed native home detaches surviving original without destroying it");
        Check(Loc.Subscribers==0,"destroyed home retires caption language subscription");
        // The carried original can also be removed independently. Keep a live
        // recorded child outside it to exercise complete layer receipt cleanup.
        var next=new Fixture();next.LevelWindow.IsOpen=false;next.InventoryWindow.IsOpen=false;
        NewPartyDisplayUI.PartyDisplay!.ActiveDisplay=NewPartyDisplayUI.DisplayType.CARDS;
        var other=next.Reset.displayToggle;other.transform.SetParent(next.Inventory,false);other.gameObject.layer=5;
        var nextHeader=Rect("Header",Rect("Container",next.Owned,388,1080),368,50);
        var otherFont=Rect("Title",nextHeader,340,40).gameObject.AddComponent<TextMeshProUGUI>();otherFont.font=new Object();
        var child=Rect("surviving original descendant",other.transform,10,10);child.gameObject.layer=6;
        next.Tick();child.SetParent(next.Campaign,false);Object.Destroy(other.gameObject);LevelUpComposite.Reset();
        Check(child.gameObject.layer==6&&Loc.Subscribers==0,"destroyed original still restores live recorded descendants and unsubscribes caption");
        Loc.Language(false);LevelUpComposite.Reset();
    }
}
