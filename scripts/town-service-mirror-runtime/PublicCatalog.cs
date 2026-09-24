using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using GloomhavenVR.Net;
using GloomhavenVR.Net.TownServices;
using UnityEngine;

public static partial class MirrorProgram
{
    private static IEnumerator PublicCatalogLanes()
    {
        TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer = 1;
        Transform owner = Go("dual lane owner").transform, observer = Go("dual lane observer").transform;
        Transform service = Go("private native service", owner).transform;
        Transform catalog = Go("public cabinet", owner).transform;
        Transform cassette = Go("Cassette", catalog).transform;
        Transform shutter = Go("Shutter", catalog).transform;
        Transform upper = Go("Upper", shutter).transform, lower = Go("Lower", upper).transform;
        upper.localPosition = new Vector3(0,.265f,0); lower.localPosition = new Vector3(0,-.265f,-.02f);
        TownServiceMirror.RegisterTemplate(2,1,service,address:"temple|");
        TownServiceMirror.RegisterTemplate(1,1,catalog,address:"merchant.rack|");
        TownServiceMirror.BeginSession(2,900,owner,service);
        TownServiceMirror.RegisterModule(10,1,service,address:"temple|");
        using (TownServiceMirror.UsePublicLane())
        {
            TownServiceMirror.BeginSession(1,901,owner,catalog);
            TownServiceMirror.RegisterModule(10,1,catalog,address:"merchant.rack|");
            TownServiceMirror.SetRack(10,new TownRackState {Cassette=true,Turn=1,From=0,To=1,Page=1,Elapsed=.425f});
            TownCassetteMotion.Apply(catalog,.5f);
        }
        var snapshots=Capture();
        Check(snapshots.Count(bytes=>TownServiceCodec.TryRead(bytes,bytes.Length,out var frame)&&frame!.Module==10)==2,
            "private service and public stock both publish identical module IDs without overwrite");
        Check(TownServiceMirror.RemoteSessions.Count==0,"local public stock creates no visitor workspace");
        NetPlayerActors.Peer=3; Receive(2,snapshots); TownServiceMirror.TickRemote(_=>observer);
        Check(TownServiceMirror.PublicAuthor==2,"one lowest live stock author is elected");
        Check(TownServiceMirror.RemoteSessions.Count==1,"remote public stock is excluded from visitor census");
        Check(Remote(2,10)!=null&&Remote(-2,10)!=null,"private native service coexists with shared public cabinet");
        Transform remote=Remote(-2,10)!.Root;
        Check(Math.Abs(remote.Find("Cassette").localPosition.z-.32f)<.001f,
            "late public observer reconstructs cassette withdrawal from explicit clock");
        Check(Quaternion.Angle(remote.Find("Shutter/Upper").localRotation,Quaternion.identity)<.1f,
            "late public observer sees fully closed shutter at identity exchange");
        float start=Time.unscaledTime;
        while(Time.unscaledTime-start<.5f) {TownServiceMirror.TickRemote(_=>observer);yield return null;}
        Check(remote.Find("Cassette").localPosition==Vector3.zero,
            "public observer completes cassette extension without another owner packet");
        Check(TownServiceMirror.PublicRack?.Elapsed==TownRackState.TurnDuration&&TownServiceMirror.PublicRack?.Page==1,
            "authority handoff retains completed observer clock instead of rewinding stale owner sample");
        TownServiceMirror.EndSession(); Receive(2,Capture()); TownServiceMirror.TickRemote(_=>observer);
        Check(Remote(2,10)==null&&Remote(-2,10)!=null,
            "closing private native window leaves public cabinet continuously visible");
        TownServiceMirror.ClaimPublicCatalog();
        Check(TownServiceMirror.IsPublicAuthor,"physical cabinet interaction promotes local presentation authority");
        TownServiceMirror.TickRemote(_=>observer);
        Check(Remote(-2,10)==null,"authority promotion removes obsolete observer cabinet rather than duplicating it");
        using(TownServiceMirror.UsePublicLane()) TownServiceMirror.EndSession();
        Check(TownServiceMirror.PublicAuthor==2,"inactive local stock never blocks another player's active cabinet");
        TownServiceMirror.RemovePeer(2);
        Check(TownServiceMirror.PublicAuthor==int.MaxValue,"departed public owner leaves no stale invisible authority");
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;
        RollerLateJoin();
    }
    private static void RollerLateJoin()
    {
        foreach (int direction in new[] {-1, 1}) foreach (float phase in new[] {.12f, .5f, .82f})
        {
            TownServiceMirror.Shutdown(); Baselines.Clear(); NetPlayerActors.Peer=1;
            Transform owner=Go("roller owner").transform, observer=Go("roller observer").transform;
            owner.localScale=Vector3.one*1.4f; observer.localScale=owner.localScale;
            observer.rotation=Quaternion.Euler(0,71,0);
            Transform cabinet=Go("roller cabinet",owner).transform, cassette=Go("Cassette",cabinet).transform;
            for(int row=0;row<3;row++)
            {
                Transform holder=Go("Row"+row,cassette).transform;
                for(int card=0;card<4;card++)
                {
                    Transform face=Go("OriginalCard"+card,holder).transform;
                    face.localPosition=new Vector3((card-1.5f)*.18f,0,-.025f);
                }
            }
            Transform indicator=Go("PageIndicator",cabinet).transform;
            var caption=Rect("Caption",indicator,Vector2.zero,new Vector2(145,110)).gameObject.AddComponent<TMPro.TextMeshProUGUI>();
            caption.text="wrong later page";
            TownServiceMirror.RegisterTemplate(1,1,cabinet,address:"merchant.rack|");
            using(TownServiceMirror.UsePublicLane())
            {
                TownServiceMirror.BeginSession(1,950,owner,cabinet);
                TownServiceMirror.RegisterModule(10,1,cabinet,address:"merchant.rack|");
                TownServiceMirror.SetRack(10,new TownRackState{Cassette=true,ScrollDirection=(sbyte)direction,
                    PageCount=2,Turn=1,From=0,To=1,Page=(ushort)(phase<.5f?0:1),Elapsed=phase*TownRackState.TurnDuration});
                TownCassetteMotion.Apply(cabinet,phase,direction);
            }
            var captured=Capture();NetPlayerActors.Peer=3;Receive(2,captured);TownServiceMirror.TickRemote(_=>observer);
            Transform mirrored=Remote(-2,10)!.Root;
            for(int row=0;row<3;row++)
            {
                Transform original=cabinet.Find("Cassette/Row"+row), copy=mirrored.Find("Cassette/Row"+row);
                Check(Vector3.Distance(original.localPosition,copy.localPosition)<.0001f
                    &&Quaternion.Angle(original.localRotation,copy.localRotation)<.01f,
                    "late observer reconstructs exact owner holder translation and hinge angle in either scroll direction");
                for(int card=0;card<4;card++) foreach(Vector3 corner in new[]{new Vector3(-.07f,-.056f,0),new Vector3(.07f,.056f,0)})
                {
                    Vector3 expected=owner.InverseTransformPoint(original.Find("OriginalCard"+card).TransformPoint(corner));
                    Vector3 actual=observer.InverseTransformPoint(copy.Find("OriginalCard"+card).TransformPoint(corner));
                    Check(Vector3.Distance(expected,actual)<.0002f,
                        "remote original card corners follow owner roller at intermediate poses without viewer-facing changes");
                }
            }
            Check(mirrored.Find("PageIndicator/Caption").GetComponent<TMPro.TMP_Text>().text=="↑\n"+(phase<.5f?1:2)+" / 2\n↓",
                "page counter follows displayed clock instead of an unrelated late native text sample");
            Check(TownServiceMirror.PublicRack?.ScrollDirection==direction&&TownServiceMirror.PublicRack?.PageCount==2,
                "public authority handoff retains scroll direction and complete page count");
        }
        TownServiceMirror.Shutdown();NetPlayerActors.Peer=1;
    }

}

public static partial class MirrorProgram
{
    private static void InspectionPublisher()
    {
        var owner = Go("inspection owner").transform;
        GloomhavenVR.WorldUI.TownServiceSync.ResetNetwork();
        GloomhavenVR.WorldUI.TownServicePresentation.Active = false;
        GloomhavenVR.WorldUI.TownServiceEnhancementHandoff.Returning.Clear();
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active = true;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Session = 500;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.StationRoot = owner;
        for(int i=0;i<512;i++)
        {
            var chip=Go("owned item",owner).AddComponent<GloomhavenVR.Cards.ItemsPile.ItemChip>();
            chip.Item=new GloomhavenVR.Cards.ItemsPile.Item {ID=1000+i};
            chip.NativeItemCard=Go("original item face",chip.transform).AddComponent<GloomhavenVR.WorldUI.ItemCardUI>();
            chip.NativeItemCard.CardID=1000+i;
            chip.InspectionBody=Go("original item backing",chip.transform).transform;
            GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Add(chip);
        }
        Transform offeringSeat = Go("offering outside fan", owner).transform;
        offeringSeat.localPosition = new Vector3(.7f, .17f, -.5f);
        var offered = GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips[0];
        offered.transform.SetParent(offeringSeat, false); offered.TownOffering = true;
        GloomhavenVR.WorldUI.TownServiceSync.Calls.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        var calls=GloomhavenVR.WorldUI.TownServiceSync.Calls;
        Check(calls.Count==1024,"closed native shop publishes all 512 owned faces and original backings exactly once");
        Check(calls.Count(c=>c.Source==offered.NativeItemCard!.transform)==1
            && calls.Count(c=>c.Source==offered.InspectionBody)==1,
            "original offered face and body publish after leaving the wrist fan hierarchy");
        foreach(var chip in GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips)
        {
            Check(calls.Count(c=>c.Source==chip.NativeItemCard!.transform)==1&&calls.Count(c=>c.Source==chip.InspectionBody)==1,
                "owned item publisher uses each actual original face and backing");
            Check(!calls.Any(c=>c.Source==chip.transform),"owned item mount is not duplicated as a second physical card");
        }
        object instance=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("Private",PrivateStatic)!.GetValue(null)!;
        var generation=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("_generation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        var priority = (List<Transform>)typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("PriorityRoots",BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
        Check(priority.Contains(offered.NativeItemCard!.transform), "floating owned offering has animation publication priority");
        var prompt = new GloomhavenVR.WorldUI.TownServicePalmConfirmation.Entry { Service = 1, Seat = offeringSeat };
        for (int part = 0; part < 4; part++)
        {
            var surface = new GloomhavenVR.WorldUI.TownServiceSurface { Id = (ushort)(60 + part) };
            surface.Panel.Target = Go("original merchant decision " + part, offeringSeat).transform;
            prompt.Surfaces.Add(surface);
        }
        GloomhavenVR.WorldUI.TownServicePalmConfirmation.Active.Add(prompt);
        calls.Clear();GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        foreach (var surface in prompt.Surfaces)
        {
            Check(calls.Count(c => c.Key == "item.confirm.part." + (surface.Id - 60) && c.Source == surface.Panel.Target) == 1,
                "inspection-only merchant retains each original palm confirmation part exactly once");
            Check(priority.Contains(surface.Panel.Target), "inspection-only palm confirmation receives motion priority");
        }
        GloomhavenVR.WorldUI.TownServicePalmConfirmation.Active.Clear();
        object before=generation.GetValue(instance)!;
        GloomhavenVR.WorldUI.TownServicePresentation.Active=true;
        GloomhavenVR.WorldUI.TownServicePresentation.Service=1;
        GloomhavenVR.WorldUI.TownServicePresentation.Session=5000;
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,owner);
        Check(before.Equals(generation.GetValue(instance)),"opening native purchase confirmation retains the owned fan publication lifetime");
        GloomhavenVR.WorldUI.TownServicePresentation.Active=false;
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        Check(before.Equals(generation.GetValue(instance)),"closing native shop does not restart owned item fan baselines");
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.Active=false;
        GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        Check(GloomhavenVR.WorldUI.TownServiceSync.ModuleCount==0,"leaving merchant retires owned inspection publication");
    }
}
