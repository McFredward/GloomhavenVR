using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool pass,string message){_assertions++;if(!pass)throw new Exception(message);}
    private static void Near(float value,float expected,string message)=>Check(Math.Abs(value-expected)<.0001f,message);
    private static int OwnedCount=>((ICollection<Light>)typeof(TownServiceLighting).GetField("Owned",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!).Count;
    private static Light[] Living()=>Light.All.Where(l=>l!=null).ToArray();
    private static void Main()
    {
        UnityEngine.Object.BeforeDestroy=obj=>{foreach(Light light in obj.Lights)Check(!TownServiceLighting.Owns(light),"ownership removed before deferred Destroy");};
        var native=new GameObject("Native torch").AddComponent<Light>();
        var sameLayerNative=new GameObject("Native mod-layer lamp"){layer=27}.AddComponent<Light>();
        var sameNameNative=new GameObject("TownService.PracticalLight").AddComponent<Light>();
        for(int round=0;round<20;round++)
        {
            var root=new GameObject("Station").transform;root.localScale=Vector3.one*2;
            var merchant=new TownServiceLighting(root,1);
            var temple=new TownServiceLighting(root,2);
            var enchantress=new TownServiceLighting(root,3);
            var owned=Living().Where(TownServiceLighting.Owns).ToArray();
            Check(owned.Length==7&&OwnedCount==7,"one moon and six practicals, bounded across repeated visits");
            Check(owned.Count(l=>l.type==LightType.Directional)==1,"shared moon light");
            Check(!TownServiceLighting.Owns(native)&&!TownServiceLighting.Owns(sameLayerNative)&&!TownServiceLighting.Owns(sameNameNative),"no name/layer ownership heuristic");
            LightStabiliser.Reset();
            Check(LightStabiliser.Adopt(Living())==3,"only unrelated native lights adopted");
            Check(LightStabiliser.Count==3,"town lights never enter stabilizer records");
            Check(LightStabiliser.Adopt(Living())==0,"native lights not duplicated on repeated census");
            foreach(var lamp in owned.Where(l=>l.type==LightType.Point))
            {Near(lamp.intensity,0,"practical dark before asset source");Near(lamp.range,5.3f,"scaled practical range");Check(lamp.renderMode==LightRenderMode.ForceVertex,"zero-pixel-cap compatibility");}
            merchant.SetVisibility(.5f);merchant.SetFlame(new Vector3(2,3,4),0);
            var merchantLamp=owned.First(l=>l.type==LightType.Point);
            Near(merchantLamp.intensity,1.3f,"late source uses current fade without damping");Near(merchantLamp.transform.position.y,3,"late source keeps original flame position");
            merchant.SetFlame(new Vector3(-2,3,4),1);
            Near(owned.Where(l=>l.type==LightType.Point).Skip(1).First().intensity,1.3f,"second visible practical illuminates other side of face");
            merchant.SetVisibility(1);Near(merchantLamp.intensity,2.6f,"owner reaches full light immediately");
            SkyAlternative.HasMoon=false;enchantress.Refresh(root);Near(owned.Single(l=>l.type==LightType.Directional).intensity,0,"MR/default has no invented studio key");SkyAlternative.HasMoon=true;
            merchant.Dispose();Check(OwnedCount==5,"single station leaves shared moon and other stations");
            int destroys=UnityEngine.Object.DestroyRequests;merchant.Dispose();Check(UnityEngine.Object.DestroyRequests==destroys&&OwnedCount==5,"double dispose is inert");
            temple.Dispose();Check(OwnedCount==3,"temple releases both candles");enchantress.Dispose();
            Check(OwnedCount==0,"last resident releases registry and shared moon before end-of-frame");
            UnityEngine.Object.FinishFrame();
        }
        var survivorRoot=new GameObject("External teardown test").transform;
        var first=new TownServiceLighting(survivorRoot,1);
        var oldMoon=Living().Single(l=>TownServiceLighting.Owns(l)&&l.type==LightType.Directional);
        UnityEngine.Object.ExternalDestroy(oldMoon.gameObject);
        first.Refresh(survivorRoot);
        Check(OwnedCount==3,"externally destroyed moon reference removed before replacement");
        var frame=new GameObject("Author map frame").transform;
        frame.rotation=Quaternion.LookRotation(new Vector3(1,0,1),Vector3.up);
        var snapshot=new GloomhavenVR.Net.TownActivityState{HasSharedPerformance=true};
        TownServiceLighting.SampleEnvironment(frame,ref snapshot);
        var fill=Living().Single(l=>TownServiceLighting.Owns(l)&&l.type==LightType.Directional);
        Vector3 authorDirection=fill.transform.forward;
        Check(snapshot.HasEnvironmentLight,"owned fill has an explicit shared sample");
        Near(snapshot.EnvironmentLightIntensity,.4f,"actual author fill intensity sampled");
        Near(snapshot.EnvironmentLightColour.x,.7f,"actual author fill colour sampled");
        SkyAlternative.Direction=new(-1,0,0);SkyAlternative.Colour=new(1,0,0);
        first.Refresh(survivorRoot,false);
        Near(fill.color.g,.79f,"observer cannot resample its local moon colour");
        Near(fill.transform.forward.y,authorDirection.y,"observer cannot resample its local moon direction");
        var observerFrame=new GameObject("Observer map frame").transform;
        observerFrame.rotation=Quaternion.LookRotation(new Vector3(-1,0,1),Vector3.up);
        TownServiceLighting.ApplyEnvironment(observerFrame,in snapshot);
        Vector3 expectedDirection=observerFrame.TransformDirection(snapshot.EnvironmentLightDirection);
        Near(fill.transform.forward.x,expectedDirection.x,"author fill direction follows observer's shared frame rotation");
        Near(fill.transform.forward.y,expectedDirection.y,"author fill direction preserves elevation");
        Near(fill.color.r,.7f,"author colour survives different observer room");
        Near(fill.color.g,.79f,"author colour survives different observer moon");
        Check(fill.enabled,"published lit environment enables the owned fill");
        snapshot.EnvironmentLightIntensity=0f;
        TownServiceLighting.ApplyEnvironment(observerFrame,in snapshot);
        Check(!fill.enabled&&fill.intensity==0f,"author's MR/no-fill is explicitly disabled on observer");
        var firstLamp=Living().First(l=>TownServiceLighting.Owns(l)&&l.type==LightType.Point);
        UnityEngine.Object.ExternalDestroy(firstLamp.gameObject);
        first.Dispose();Check(OwnedCount==0,"Unity fake-null practical still removed from registry");UnityEngine.Object.FinishFrame();
        Check(!TownServiceLighting.Owns(native),"native light ownership remains untouched");
        Console.WriteLine($"Town lighting: {_assertions} production ownership/lifecycle assertions passed");
    }
}
