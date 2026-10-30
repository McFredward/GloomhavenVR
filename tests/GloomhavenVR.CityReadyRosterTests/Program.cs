using System;
using GloomhavenVR.WorldUI.MapRoom;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition,string message){++_assertions;if(!condition)throw new InvalidOperationException("City ready roster: "+message);}
    private static RectTransform Rect(string name,Transform? parent=null,float width=307,float height=65)
    {var go=new GameObject(name);go.transform.SetParent(parent,false);var r=(RectTransform)go.transform;r.sizeDelta=new(width,height);return r;}
    private static UIWindow Window(string name,float height=65)=>Rect(name,height:height).gameObject.AddComponent<UIWindow>();
    private static Graphic Ink(string name,Transform parent,float x,float y,float width,float height,float alpha=1)
    {var r=Rect(name,parent,width,height);r.anchoredPosition=new(x,y);var g=r.gameObject.AddComponent<Graphic>();g.color=new(1,1,1,alpha);return g;}
    private sealed class Fixture
    {
        internal readonly RectTransform Hud,Bar;
        internal readonly UIWindow Ready,Quest,Loadout;
        internal readonly UIReadyToggle Toggle;
        internal readonly Graphic[] Cells=new Graphic[4];
        internal readonly CanvasGroup[] CellStates=new CanvasGroup[4];
        internal Fixture()
        {
            MapQuestReadyRoster.Reset();VRLog.Warnings.Clear();MapRoomDriver.Active=true;FFSNetwork.IsOnline=true;Time.frameCount+=10;Time.unscaledTime+=1;
            Hud=Rect("Native HUD",width:1920,height:1080);Quest=Window("Quest card",1000);Loadout=Window("Character window",1080);Ready=Window("Native pooled ready",65);
            Toggle=Ready.gameObject.AddComponent<UIReadyToggle>();Singleton<UIReadyToggle>.Instance=Toggle;Ready.gameObject.layer=23;
            Ink("Original native button",Ready.transform,0,0,307,65);
            Bar=Rect("Original native tracker bar",Hud,500,180);Bar.anchorMin=Bar.anchorMax=new(.25f,.75f);Bar.anchoredPosition=new(35,18);Bar.localScale=new(.9f,.9f,.9f);
            var tracker=Bar.gameObject.AddComponent<UIReadyTrackerBar>();Singleton<UIReadyTrackerBar>.Instance=tracker;
            for(int i=0;i<4;++i){Cells[i]=Ink("Original player cell "+i,Bar,(i-1.5f)*58,-25,50,63);CellStates[i]=Cells[i].gameObject.AddComponent<CanvasGroup>();CellStates[i].alpha=i%2==0?1:.5f;}
        }
        internal void Tick(ReadyRosterSite site,UIWindow host,GameObject? confirm,bool posed=true)
        {MapQuestReadyRoster.Tick(site,host,confirm,posed);Time.frameCount++;Time.unscaledTime+=.21f;}
        internal void City()
        {Toggle.readyUpToggleState=EReadyUpToggleStates.CityEvents;Tick(ReadyRosterSite.CityEvent,Ready,Ready.gameObject);}
        internal void NativeCellsUnchanged()
        {
            Check(ReferenceEquals(MapQuestReadyRoster.Bar,Bar.gameObject),"borrow the exact original tracker bar");
            for(int i=0;i<4;++i)Check(ReferenceEquals(Bar.GetChild(i),Cells[i].transform)&&CellStates[i].alpha==(i%2==0?1:.5f),"native cell identity, order and readiness remain original");
            Check(Toggle.ReadyCalls==0,"presentation must not consent for a participant");
        }
    }
    private static void Main()
    {
        var f=new Fixture();Vector3 readyPosition=f.Ready.transform.localPosition;f.City();
        Check(ReferenceEquals(f.Bar.parent,f.Ready.transform)&&MapQuestReadyRoster.Site==ReadyRosterSite.CityEvent,"city ready-up adopts native readiness row into same shared window");
        f.NativeCellsUnchanged();Check(MapQuestReadyRoster.ReservedHeight()==0,"city row never offsets quest confirmation");
        Check(f.Ready.transform.localPosition.x==readyPosition.x&&f.Ready.transform.localPosition.y==readyPosition.y,"city presentation never moves native button");
        Check(f.Bar.gameObject.layer==23&&f.Cells[0].gameObject.layer==23,"adopted original row uses shared capture layer");
        MapTravelConfirm.TryInkBounds((RectTransform)f.Ready.transform,f.Bar,out Rect row,out _);
        MapTravelConfirm.TryInkBounds((RectTransform)f.Ready.transform,f.Ready.transform,f.Bar,out Rect button,out int buttonCount);
        Check(buttonCount==1&&row.yMin>button.yMax,"original row ink sits directly above original button ink");
        Check(row.yMax>((RectTransform)f.Ready.transform).rect.yMax,"actual drawn row extends beyond native65px control rect");
        MapTravelConfirm.TryInkBounds((RectTransform)f.Ready.transform,f.Ready.transform,out Rect whole,out int wholeCount);
        Check(wholeCount==5&&whole.yMax>=row.yMax&&whole.yMin<=button.yMin,"unclipped host ink union includes complete native row and button");
        Vector2 settled=f.Bar.anchoredPosition;
        for(int frame=0;frame<100;++frame)f.Tick(ReadyRosterSite.CityEvent,f.Ready,f.Ready.gameObject);
        Check((f.Bar.anchoredPosition-settled).sqrMagnitude<.01f,"city same-root measurement cannot chase its own row upward");

        // Retained offers are deliberately two frames old. Their exact native phase
        // must still match now, independent of which parker calls first this frame.
        foreach(ReadyRosterSite prior in new[]{ReadyRosterSite.Quest,ReadyRosterSite.EnterDungeon})
        {
            f=new Fixture();f.Toggle.readyUpToggleState=prior==ReadyRosterSite.Quest?EReadyUpToggleStates.Quests:EReadyUpToggleStates.NotSet;
            UIWindow previous=prior==ReadyRosterSite.Quest?f.Quest:f.Loadout;
            f.Ready.transform.SetParent(previous.transform,false);f.Tick(prior,previous,f.Ready.gameObject);
            Check(ReferenceEquals(f.Bar.parent,previous.transform),"existing quest/loadout row still uses same coordinator");
            f.Toggle.readyUpToggleState=EReadyUpToggleStates.CityEvents;f.Ready.transform.SetParent(null,false);
            f.Tick(ReadyRosterSite.CityEvent,f.Ready,f.Ready.gameObject);
            Check(ReferenceEquals(f.Bar.parent,f.Ready.transform)&&MapQuestReadyRoster.Site==ReadyRosterSite.CityEvent,"stale previous-phase offer cannot steal city row");
            Check(MapQuestReadyRoster.NoticeOwner==null,"stale quest cannot display assignment notice during city event");
            f.Tick(prior,previous,f.Ready.gameObject);
            Check(ReferenceEquals(f.Bar.parent,f.Ready.transform),"late previous-phase parker cannot override current city ownership");
            f.NativeCellsUnchanged();
        }
        f=new Fixture();f.City();
        f.Toggle.readyUpToggleState=EReadyUpToggleStates.Quests;f.Ready.transform.SetParent(f.Quest.transform,false);f.Tick(ReadyRosterSite.Quest,f.Quest,f.Ready.gameObject);
        Check(ReferenceEquals(f.Bar.parent,f.Quest.transform)&&MapQuestReadyRoster.ReservedHeight()>0,"quest successor regains native row and original reservation");
        f.Tick(ReadyRosterSite.CityEvent,f.Ready,f.Ready.gameObject);
        Check(ReferenceEquals(f.Bar.parent,f.Quest.transform),"stale city offer cannot override quest successor");
        f.Toggle.readyUpToggleState=EReadyUpToggleStates.Retirement;f.Tick(ReadyRosterSite.CityEvent,f.Ready,f.Ready.gameObject);
        Check(ReferenceEquals(f.Bar.parent,f.Hud)&&MapQuestReadyRoster.ReservedHeight()==0,"unrelated native phase immediately releases shared row");
        Check(f.Bar.localScale.x==.9f&&f.Bar.anchoredPosition.x==35&&f.Bar.anchoredPosition.y==18,"row release restores exact native HUD home");
        Check(f.Bar.gameObject.layer==0&&f.Cells[0].gameObject.layer==0,"row release restores original layers");

        f=new Fixture();f.Toggle.readyUpToggleState=EReadyUpToggleStates.CityEvents;
        var clone=Window("Unrelated same-type ready");clone.gameObject.AddComponent<UIReadyToggle>();
        f.Tick(ReadyRosterSite.CityEvent,f.Ready,clone.gameObject);Check(ReferenceEquals(f.Bar.parent,f.Hud),"same component type cannot counterfeit native ready identity");
        f.City();f.Bar.gameObject.SetActive(false);f.Tick(ReadyRosterSite.CityEvent,f.Ready,f.Ready.gameObject);
        Check(!f.Bar.gameObject.activeSelf,"native hidden tracker bar is never made active by presentation");
        f.Bar.gameObject.SetActive(true);f.Tick(ReadyRosterSite.CityEvent,f.Ready,null);
        Check(ReferenceEquals(f.Bar.parent,f.Hud),"ended native city offer immediately restores original row");
        f.City();MapRoomDriver.Active=false;f.Tick(ReadyRosterSite.CityEvent,f.Ready,f.Ready.gameObject);
        Check(ReferenceEquals(f.Bar.parent,f.Hud),"map-room disable releases native row");
        MapRoomDriver.Active=true;f.City();MapQuestReadyRoster.Reset();
        Check(ReferenceEquals(f.Bar.parent,f.Hud)&&MapQuestReadyRoster.ReservedHeight()==0,"teardown clears third-site offer and restores native row");
        Console.WriteLine($"City ready roster: {_assertions} assertions; production ready-row coordinator, ink/clip/layer kernels passed. No capture/headset/Photon claim.");
    }
}
