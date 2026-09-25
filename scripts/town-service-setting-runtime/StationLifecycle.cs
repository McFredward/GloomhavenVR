using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

internal static class StationLifecycle
{
    private static int _assertions;
    private static void Check(bool value,string message) { _assertions++;if(!value)throw new Exception(message); }
    private static void Near(float actual,float expected,string message)=>Check(Math.Abs(actual-expected)<.0001f,message+$" {actual} != {expected}");
    internal static void Run()
    {
        MapRoomDriver.Active=true;MapRoomDriver.FrameReady=true;MapRoomDriver.Center=Vector3.zero;MapRoomDriver.Scale=1;
        SkyAlternative.PlacedRoomRoot=new Transform{position=new Vector3(0,4,0)};
        for(byte service=1;service<=3;service++)
        {
            SkyAlternative.PlacedRoomRoot=new Transform{position=new Vector3(0,4,0)};
            var station=TownServiceStation.Create(service,Vector3.zero,1)!;
            Check(station!=null,"station created");
            station!.RefreshEnvironment(false);
            station.Root.position=new Vector3(9,100,7); // Incoming author's deliberately different floor.
            station.SetGrounding(-.04f,-.06f);
            station.RefreshEnvironment(false);
            Near(station.Root.position.y,100,"follower keeps incoming pose");
            Near(station.ActorFloorOffset,-.04f,"follower retains author sole correction");
            Near(station.FurnitureBottom,-.06f,"follower retains author support correction");
            int before=station.Root.PoseWrites;
            station.RefreshEnvironment(true);
            Near(station.Root.position.y,4,"follower to author resolves actual floor with unchanged room/frame");
            Near(station.ActorFloorOffset,0,"new author replaces old sole correction");
            Near(station.FurnitureBottom,0,"new author replaces old support correction");
            Check(station.Root.PoseWrites==before+1,"handover performs one placement");
            station.RefreshEnvironment(true);Check(station.Root.PoseWrites==before+1,"steady author does not re-place");
            station.RefreshEnvironment(false);station.Root.position=new Vector3(9,123,7);
            SkyAlternative.PlacedRoomRoot=new Transform{position=new Vector3(0,6,0)};
            station.RefreshEnvironment(false);Near(station.Root.position.y,123,"viewer room change retains peer pose");
            station.RefreshEnvironment(true);Near(station.Root.position.y,6,"next authority transition applies changed floor");
            int refreshes=TownServiceLighting.Last!.Refreshes;
            station.RefreshEnvironment(false);station.Root.localScale=Vector3.one*3;
            station.RefreshEnvironment(false);
            Check(TownServiceLighting.Last.Refreshes==refreshes+1,"remote scale change refreshes light range without local pose");
            Near(TownServiceLighting.Last.Scale,3,"light range receives actual remote scale");
            station.RefreshEnvironment(false);Check(TownServiceLighting.Last.Refreshes==refreshes+1,"steady follower does not rescan lighting");
            Check(TownServiceDecor.Last!.Ticks>=8,"followers still advance asynchronous decoration");
            var originalRenderer=station.Root.gameObject.Renderers[0];
            var laterCard=new Renderer();station.Root.gameObject.Renderers=new[]{originalRenderer,laterCard};
            station.SetVisibility(.5f);
            Check(originalRenderer.PropertyWrites==1,"station-owned renderer fades");
            Check(laterCard.PropertyWrites==0,"late workspace card receives no station property block");
            var activity=default(GloomhavenVR.WorldUI.TownActivityVisual);
            TownServiceActivityRig.Available=false;station.SampleActivity(in activity);
            Check(TownServiceDecor.Last.ActivitySamples==0&&TownServiceDecor.Last.Suspends==1,"missing arm rig cannot create ungripped tools");
            TownServiceActivityRig.Available=true;station.SampleActivity(in activity);
            Check(TownServiceDecor.Last.ActivitySamples==1,"ready activity applies tools");
            TownServiceActivityRig.Throw=true;station.SampleActivity(in activity);int activitySamples=TownServiceDecor.Last.ActivitySamples;
            Check(TownServiceDecor.Last.Suspends==2,"failed activity withdraws held tools");
            station.SampleActivity(in activity);Check(TownServiceDecor.Last.ActivitySamples==activitySamples,"failed activity never retries or gates station");
            TownServiceActivityRig.Throw=false;
            TownServiceFace.Throw=true;var face=default(GloomhavenVR.Net.TownFacePose);
            station.SampleFace(true,false,1,in face,0,0);int faceTicks=TownServiceFace.Ticks;
            station.SampleFace(true,false,1,in face,0,0);
            Check(TownServiceFace.Ticks==faceTicks,"failed facial presentation stops retrying without blocking native station");
            TownServiceFace.Throw=false;
            station.Dispose();Check(TownServiceLighting.Last.Disposed&&TownServiceDecor.Last.Disposed,"owned decoration and lighting released");
        }
        // Legacy bundles must never switch geometry while approaching an NPC.
        var high=new Renderer();var low=new Renderer();var eyes=new Renderer();
        var group=new LODGroup{Levels=new[]{new LOD{renderers=new[]{high,eyes}},new LOD{renderers=new[]{low,eyes}}}};
        var actor=new Transform{Lod=group};
        var method=typeof(TownServiceStation).GetMethod("PreserveActorDetail",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(null,new object?[]{actor});
        Check(!group.enabled && group.Forced==0,"legacy NPC never performs distance LOD switching");
        Check(high.enabled && eyes.enabled && !low.enabled,"legacy NPC retains full skin and shared eyes only");
        method.Invoke(null,new object?[]{actor});
        Check(high.enabled && eyes.enabled && !low.enabled,"fixed detail is idempotent");
        method.Invoke(null,new object?[]{null});
        method.Invoke(null,new object?[]{new Transform()});
        int created=TownServiceLighting.Creates;
        TownServiceAssets.HasAnchor=false;
        bool rejected=false;
        try {TownServiceStation.Create(1,Vector3.zero,1);}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"invalid prefab anchor rejected");
        Check(TownServiceLighting.Creates==created,"invalid anchor does not acquire lighting/decor resources");
        TownServiceAssets.HasAnchor=true;
        Console.WriteLine($"Town station lifecycle: {_assertions} production assertions passed");
    }
}

namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceAssets
    {
        internal static bool HasAnchor=true;
        internal static GameObject? Prefab(string name) {var obj=new GameObject();obj.transform.HasAnchor=HasAnchor;return obj;}
    }
    internal sealed class TownServiceLighting : IDisposable
    {
        internal static TownServiceLighting? Last;
        internal static int Creates;
        internal int Refreshes;
        internal float Scale;
        internal bool Disposed;
        internal TownServiceLighting(Transform root,byte service){Last=this;Creates++;}
        internal void Refresh(Transform root){Refreshes++;Scale=root.lossyScale.x;}
        internal void SetVisibility(float value){}
        public void Dispose()=>Disposed=true;
    }
    internal sealed class TownServiceDecor : IDisposable
    {
        internal static TownServiceDecor? Last;
        internal bool Ready=>true;
        internal int Ticks;
        internal bool Disposed;
        internal TownServiceDecor(Transform root,byte service,TownServiceLighting lighting){Last=this;}
        internal void Tick()=>Ticks++;
        internal void SetVisibility(float value){}
        internal void SetClock(float seconds){}
        internal int ActivitySamples,Suspends;
        internal void SampleActivity(in GloomhavenVR.WorldUI.TownActivityVisual pose){ActivitySamples++;}
        internal void SuspendActivity(){Suspends++;}
        public void Dispose()=>Disposed=true;
    }
}
namespace UnityEngine
{
    public class Object
    {
        public static GameObject Instantiate(GameObject prefab) {var clone=new GameObject();clone.transform.HasAnchor=prefab.transform.HasAnchor;return clone;}
        public static void Destroy(Object obj){}
    }
    public class GameObject : Object
    {
        public bool activeSelf=true;
        public string name="";
        public int layer;
        public readonly Transform transform;
        public Renderer[] Renderers={new Renderer()};
        public GameObject(){transform=new Transform{Owner=this};}
        internal GameObject(Transform t){transform=t;}
        public T? GetComponentInChildren<T>(bool inactive) where T:class=>null;
        public T[] GetComponentsInChildren<T>(bool inactive) where T:class=>typeof(T)==typeof(Renderer)?(T[])(object)Renderers:typeof(T)==typeof(Transform)?(T[])(object)new[]{transform}:Array.Empty<T>();
    }
    public class Animation
    {
        public AnimationState this[string name]=>null!;
        public void Stop(){}
        public void Sample(){}
    }
    public class AnimationState {public bool enabled;public float weight,time,length;}
    public class Collider {public bool enabled;}
    public class MaterialPropertyBlock {public void SetFloat(int id,float value){}}
    public struct LOD { public Renderer[] renderers; }
    public class LODGroup
    {
        public bool enabled=true;
        public int Forced=-1;
        public LOD[] Levels=Array.Empty<LOD>();
        public LOD[] GetLODs()=>Levels;
        public void ForceLOD(int level)=>Forced=level;
    }
    public class Renderer
    {
        public bool enabled = true;
        public int PropertyWrites;
        public void GetPropertyBlock(MaterialPropertyBlock block){}
        public void SetPropertyBlock(MaterialPropertyBlock block)=>PropertyWrites++;
    }
    public class Shader {public static int PropertyToID(string name)=>name.GetHashCode();}
}

namespace GloomhavenVR.Net
{ internal struct TownFacePose { } internal struct TownActivityPose { } internal struct TownClothRunnerState { } }
namespace GloomhavenVR.WorldUI
{
    internal sealed class TownServiceFace
    {
        internal static bool Throw;internal static int Ticks;
        internal TownServiceFace(UnityEngine.Transform root,byte service) { }
        // Attention geometry is exercised by the merchant handoff Unity fixture.
        internal bool IsLocalVisitorNear(bool wasNear) => false;
        internal bool PrepareActivityAttention(bool previous)=>false;
        internal void BeforeBodySample() { }
        internal void Seed(in GloomhavenVR.Net.TownFacePose pose,int author,float elapsed){}
        internal GloomhavenVR.Net.TownFacePose Tick(bool author,bool received,int authorId,in GloomhavenVR.Net.TownFacePose remote,float elapsed,float clock){Ticks++;if(Throw)throw new InvalidOperationException("fixture facial failure");return remote;}
    }
}

namespace GloomhavenVR.Core {internal static class VRLog {internal static void Warn(string scope,string message){} }}

namespace GloomhavenVR.WorldUI
{
    internal struct TownActivityVisual { }
    internal sealed class TownServiceCloth : IDisposable
    {
        internal TownServiceCloth(UnityEngine.Transform root, byte service) { }
        internal void TickAuthor(float age, float dt, bool visible) { }
        internal void TickObserver(float age, float elapsed, in GloomhavenVR.Net.TownClothRunnerState first,
            in GloomhavenVR.Net.TownClothRunnerState second, bool visible) { }
        internal GloomhavenVR.Net.TownClothRunnerState First => default;
        internal GloomhavenVR.Net.TownClothRunnerState Second => default;
        internal void SetVisible(bool visible) { }
        public void Dispose() { }
    }
    internal sealed class TownServiceActivityAudio
    {
        internal TownServiceActivityAudio(UnityEngine.Transform root,byte service){}
        internal void Tick(int author,uint epoch,float clock,float elapsed,bool visible,in TownActivityVisual shown){}
        internal void Dispose(){}
    }
    internal sealed class TownServiceActivityRig
    {
        internal static bool Available=true,Throw;
        internal bool Ready=>Available;
        internal TownServiceActivityRig(UnityEngine.Transform root,byte service){}
        internal void Suspend(){} internal void BeforeBodySample(){} internal void Apply(in GloomhavenVR.WorldUI.TownActivityVisual pose){if(Throw)throw new InvalidOperationException("fixture arm failure");}
    }
}

namespace UnityEngine { internal static class Time { internal static float unscaledDeltaTime=>1f/90f; } }
