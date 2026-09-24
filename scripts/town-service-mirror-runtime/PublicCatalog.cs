using System;
using System.Collections;
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
        GloomhavenVR.WorldUI.TownServiceSync.Calls.Clear();
        GloomhavenVR.WorldUI.TownServiceSync.Tick(owner,null);
        var calls=GloomhavenVR.WorldUI.TownServiceSync.Calls;
        Check(calls.Count==1024,"closed native shop publishes all 512 owned faces and original backings exactly once");
        foreach(var chip in GloomhavenVR.WorldUI.TownServiceMerchantHandoff.OwnedChips)
        {
            Check(calls.Count(c=>c.Source==chip.NativeItemCard!.transform)==1&&calls.Count(c=>c.Source==chip.InspectionBody)==1,
                "owned item publisher uses each actual original face and backing");
            Check(!calls.Any(c=>c.Source==chip.transform),"owned item mount is not duplicated as a second physical card");
        }
        object instance=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("Private",PrivateStatic)!.GetValue(null)!;
        var generation=typeof(GloomhavenVR.WorldUI.TownServiceSync).GetField("_generation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
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
