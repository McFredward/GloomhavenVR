using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public class GameObject : Object { public readonly Transform transform = new(); public GameObject(string name) { } }
    public class Transform
    {
        public Vector3 position, localScale = Vector3.one;
        public Quaternion rotation = Quaternion.identity;
        public Vector3 lossyScale => localScale;
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
        public Vector3 TransformPoint(Vector3 p) => position + p * localScale.x;
        public Vector3 InverseTransformPoint(Vector3 p) => (p - position) * (1f / localScale.x);
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 Lerp(Vector3 a,Vector3 b,float t)=>a+(b-a)*Mathf.Clamp01(t);
        public static Vector3 zero => new(0,0,0);
        public static Vector3 one => new(1,1,1);
        public float sqrMagnitude => x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public static Quaternion identity => new(){w=1};
        public static Quaternion Inverse(Quaternion q) => q;
        public static Quaternion operator *(Quaternion a,Quaternion b)=>identity;
    }
    public static class Time { public static float unscaledTime, unscaledDeltaTime; }
    public static class Mathf
    {
        public const float PI=(float)Math.PI;
        public static float Sin(float v)=>(float)Math.Sin(v);
        public static float Cos(float v)=>(float)Math.Cos(v);
        public static float Clamp01(float v)=>Math.Max(0,Math.Min(1,v));
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
        public static float Abs(float v)=>Math.Abs(v);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int RoundToInt(float v)=>(int)Math.Round(v);
        public static float MoveTowards(float v,float target,float delta)=>Math.Abs(v-target)<=delta?target:v+Math.Sign(target-v)*delta;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    using UnityEngine;
    internal static class MapRoomDriver
    {
        internal static bool Active, FrameReady=true;
        internal static Vector3 Center=new(10,20,30);
        internal static float Scale=2;
        internal static bool TryGetParchmentFrame(out Vector3 p,out float scale) { p=Center;scale=Scale;return FrameReady; }
    }
}
namespace GloomhavenVR.WorldUI
{
    using UnityEngine;
    internal static class WorldUIConfig
    { internal sealed class Entry { internal bool Value; } internal static readonly Entry ImmersiveTownServices=new(); }
    internal static class TownServicePresentation { internal static bool Active; internal static byte Service; internal static float SessionAge; }
    internal static class TownServiceSync { internal static int Shutdowns; internal static void Shutdown()=>Shutdowns++; }
    internal sealed class TownServiceStation
    {
        internal static readonly Dictionary<byte,TownServiceStation> Live=new();
        internal static readonly HashSet<byte> Missing=new();
        internal static bool Ready=true;
        internal static int Creates, Disposals;
        internal static float Floor;
        internal readonly byte Service;
        internal readonly Transform Root=new();
        internal bool IsReady=>Ready;
        internal float GreetingDuration=>2f;
        internal float Visibility, Age, ActorFloorOffset, FurnitureBottom;
        internal void SetGrounding(float actor,float furniture) { ActorFloorOffset=actor;FurnitureBottom=furniture; }
        internal bool? LastAuthor;
        internal string Clip="";
        private TownServiceStation(byte service) { Service=service; }
        internal static TownServiceStation? Create(byte service,Vector3 center,float scale)
        {
            Creates++;
            if(Missing.Contains(service))return null;
            var station=new TownServiceStation(service); station.Root.position=center;station.Root.localScale=Vector3.one*scale;Live[service]=station;return station;
        }
        internal void RefreshEnvironment(bool author) { LastAuthor=author; if(author)Root.position=new Vector3(Root.position.x,Floor,Root.position.z); }
        internal void SetVisibility(float value)=>Visibility=value;
        internal bool PrepareActivityAttention(bool previous)=>false;
        internal void SampleActivity(in TownActivityVisual pose) { }
        internal int FaceSeeds;internal bool FaceAuthor,FaceReceived;internal GloomhavenVR.Net.TownFacePose FacePose;
        internal void SeedFace(GloomhavenVR.Net.TownFacePose pose,int author,float elapsed){FaceSeeds++;FacePose=pose;}
        internal GloomhavenVR.Net.TownFacePose SampleFace(bool author,bool received,int authorId,in GloomhavenVR.Net.TownFacePose remote,float elapsed,float clock){FaceAuthor=author;FaceReceived=received;if(received)FacePose=remote;return FacePose;}
        internal void Sample(string clip,float age) { Clip=clip;Age=age; }
        internal void Dispose() { Live.Remove(Service);Disposals++; }
    }
    internal sealed class TownServiceVisitTarget
    {
        internal static readonly Dictionary<byte,TownServiceVisitTarget> Live=new();
        internal readonly byte Service;
        internal bool Visible;
        internal bool Enabled=>Visible&&WorldUIConfig.ImmersiveTownServices.Value;
        internal TownServiceVisitTarget(byte service,Transform root) { Service=service;Live[service]=this; }
        internal void Tick(bool visible)=>Visible=visible;
        internal static void TickLaser() { }
        internal void Dispose()=>Live.Remove(Service);
    }
}
namespace GloomhavenVR.Net.TownServices
{
    using UnityEngine;
    internal sealed class TownServiceSessionInfo
    { internal bool Active;internal byte Service;internal float ReceivedTime,SessionAge;internal int Peer; }
    internal static class TownServiceMirror
    { internal static readonly Dictionary<int,TownServiceSessionInfo> RemoteSessions=new();internal static Func<int,Transform?>? SharedFrameForRemote; }
}
namespace GloomhavenVR.Net
{
    using UnityEngine;
    internal static class NetPlayerActors { internal static int Local=10; internal static int LocalPlayerId()=>Local; }
    internal static class NetProtocol { internal const float StaleTimeoutSeconds=3; internal const byte ExtIdTownResidents=79; }
    internal struct RigPose { internal Vector3 Position; internal Quaternion Rotation; }
    internal struct PresenceState { internal bool TownActivityRecordSeen; internal bool HasTownActivity;internal TownActivityState TownActivity;internal bool HasTownFace;internal TownFaceState TownFace; internal bool HasTownResidents; internal TownResidentsState TownResidents; }
    // Serialization is independently covered by golden wire tests. Any accidental use here fails.
    internal static class AvatarSerializer
    {
        internal static void WritePoseShared(byte[] b,ref int o,in RigPose p)=>throw new Exception("Unexpected codec boundary");
        internal static void WriteF32(byte[] b,ref int o,float p)=>throw new Exception("Unexpected codec boundary");
        internal static short ReadI16(byte[] b,ref int o)=>throw new Exception("Unexpected codec boundary");
        internal static float ReadF32(byte[] b,ref int o)=>throw new Exception("Unexpected codec boundary");
        internal static void ReadPoseShared(byte[] b,ref int o,out RigPose p)=>throw new Exception("Unexpected codec boundary");
    }
}

namespace GloomhavenVR.WorldUI { internal static class TownServiceFaceSpeech { internal static System.Action? ResetObserver {get;set;} } }
namespace GloomhavenVR.Net
{
    internal struct TownFacePose { internal float HeadYaw; }
    internal struct TownFaceState { internal bool Active;internal uint Epoch,Sequence;internal float Clock;private TownFacePose _m,_t,_e;internal TownFacePose At(int index)=>index==0?_m:index==1?_t:_e;internal void Set(int index,TownFacePose pose){if(index==0)_m=pose;else if(index==1)_t=pose;else _e=pose;} }
    internal static class RemoteTownFaces
    {
        private static readonly Dictionary<int,TownFaceState> States=new();
        internal static bool Sample(int player,out TownFaceState state,out float elapsed){elapsed=0;return States.TryGetValue(player,out state);}
        internal static void ObservePresence(int player,in TownFaceState state){States[player]=state;}
        internal static bool TrySeed(out TownFaceState state,out int author,out float elapsed){foreach(var pair in States){author=pair.Key;elapsed=0;state=pair.Value;return true;}state=default;author=0;elapsed=0;return false;}
        internal static void Forget(int player)=>States.Remove(player);
        internal static void Reset()=>States.Clear();
    }
}

// The analytic phase and real IK have their own Unity production fixture. This boundary
// isolates resident lifetime/authority routing from cosmetic pose details.
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceFaceMotion
    {
        internal static GloomhavenVR.Net.TownFacePose Interpolate(in GloomhavenVR.Net.TownFacePose a,in GloomhavenVR.Net.TownFacePose b,float t)
            => new GloomhavenVR.Net.TownFacePose{HeadYaw=UnityEngine.Mathf.Lerp(a.HeadYaw,b.HeadYaw,t)};
    }
}
namespace GloomhavenVR.Net
{
    internal static class RemoteTownActivities
    {
        internal static bool KnownPair(int peer)=>false;
        internal static bool Sample(int peer,out TownActivityState state,out float elapsed){state=default;elapsed=0;return false;}
        internal static bool TrySeed(out TownActivityState state,out int author,out float elapsed){state=default;author=0;elapsed=0;return false;}
        internal static void ObservePresence(int peer,in TownActivityState state){}
        internal static void Forget(int peer){} internal static void Reset(){}
    }
}

namespace GloomhavenVR.Net { internal static class RemoteTownPerformance { internal static bool Observe(int peer,in TownActivityState a,in TownFaceState f,bool presence){RemoteTownFaces.ObservePresence(peer,in f);return true;} } }

// Rendering/template lifetime is covered by the real Unity mirror suite. Population
// owns only these lifecycle calls; record them here without emulating render state.
namespace GloomhavenVR.Net.TownServices
{
    internal static class NativeTemplates
    {
        internal static readonly System.Collections.Generic.HashSet<byte> Invalidated = new();
        internal static void InvalidateResident(byte service) => Invalidated.Add(service);
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceConfirmationMask
    {
        internal static int Clears;
        internal static void Clear() => Clears++;
    }
}

// Geometry mutation and actual return lifetime have their own production-bound Unity suites.
namespace GloomhavenVR.WorldUI {
 internal static class TownServiceRoomClearance { internal static void Tick(bool enabled) { } internal static void Reset() { } }
 internal static class TownServiceEnhancementHandoff { internal static readonly System.Collections.Generic.List<object> Returning = new(); }
}
