using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
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
        public static float Distance(Vector3 a,Vector3 b)=>Mathf.Sqrt((a-b).sqrMagnitude);
    }
    public struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}
        public static Quaternion Slerp(Quaternion a,Quaternion b,float t)
        { var q=System.Numerics.Quaternion.Slerp(new(a.x,a.y,a.z,a.w),new(b.x,b.y,b.z,b.w),Math.Clamp(t,0,1));return new(q.X,q.Y,q.Z,q.W); }
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
        public static float Floor(float v)=>(float)Math.Floor(v);
        public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v));
        public static int Clamp(int v,int a,int b)=>Math.Max(a,Math.Min(b,v));
        public static float Clamp01(float v)=>Math.Max(0,Math.Min(1,v));
        public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
        public static float SmoothStep(float a,float b,float t){t=Clamp01(t);return a+(b-a)*t*t*(3-2*t);}
        public static float Abs(float v)=>Math.Abs(v);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Sqrt(float v)=>(float)Math.Sqrt(v);
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
    internal static class TownServiceAvailability
    {
        internal static readonly HashSet<byte> Locked = new();
        internal static bool NativeUnlocked(byte service) => service is >= 1 and <= 3 && !Locked.Contains(service);
        internal static bool ShouldPublish(bool nativeUnlocked, bool immersive, bool remoteVisitor)
            => nativeUnlocked && (immersive || remoteVisitor);
    }
    internal static class TownServiceTutorialPatches { internal static void Tick() { } }
    internal static class StoryComposite { internal static bool PointOfNoReturn; }
    internal enum TownVoiceReaction : byte
    { MerchantOffer, MerchantBuy, MerchantSell, PriestessDonate, EnchantressEnhance, PriestessUnavailable }
    internal static class TownServiceVoice
    {
        internal static int Requests;
        internal static void RequestReaction(byte service,TownVoiceReaction reaction) { Requests++; }
    }
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
        internal static bool NearVisitor; internal int AttentionQueries;
        internal bool PrepareActivityAttention(bool previous){AttentionQueries++;return NearVisitor;}
        internal TownActivityVisual LastActivity;
        internal void SampleActivity(in TownActivityVisual pose) { LastActivity=pose; }
        internal bool LastActivityAudioVisible;
        internal void SampleActivityAudio(int author,uint epoch,float clock,bool visible,in TownActivityVisual shown,bool lookingAtVisitor,
            bool authorPerformance,float performanceClock,GloomhavenVR.Net.TownActivitySoundState remote)
        { LastActivityAudioVisible=visible; }
        internal GloomhavenVR.Net.TownActivitySoundState PublishedFoley=>default;
        internal void BindEnvironment(Transform frame,in GloomhavenVR.Net.TownActivityState state) { }
        internal int FaceSeeds;internal bool FaceAuthor,FaceReceived;internal GloomhavenVR.Net.TownFacePose FacePose;
        internal void SeedFace(GloomhavenVR.Net.TownFacePose pose,int author,float elapsed){FaceSeeds++;FacePose=pose;}
        internal GloomhavenVR.Net.TownFacePose SampleFace(bool author,bool received,int authorId,in GloomhavenVR.Net.TownFacePose remote,float elapsed,float clock){FaceAuthor=author;FaceReceived=received;if(received)FacePose=remote;return FacePose;}
        internal void Sample(string clip,float age) { Clip=clip;Age=age; }
        internal int Blessings; internal float LastBlessingAge;
        internal void PlayTempleBlessing(float elapsed) { Blessings++;LastBlessingAge=elapsed; }
        private uint _blessingGeneration;
        internal void SampleTempleBlessing(uint epoch,uint generation,float age,bool interactive)
        {
            if(!interactive||generation==0||age<0f||age>=TownServiceActivityMotion.TempleBlessingVisualSeconds)return;
            if(generation!=_blessingGeneration){_blessingGeneration=generation;Blessings++;}
            LastBlessingAge=age;
        }
        internal void Dispose() { Live.Remove(Service);Disposals++; }
    }
    // Engine/environment boundary; the producer and codec under test are real.
    internal static class TownServiceLighting
    {
        internal static int Samples, Replays;
        internal static GloomhavenVR.Net.TownActivityState LastReplay;
        internal static void SampleEnvironment(Transform frame,ref GloomhavenVR.Net.TownActivityState state)
        {
            Samples++;state.HasEnvironmentLight=true;state.EnvironmentLightDirection=new(0,0,1);
            state.EnvironmentLightColour=new(.7f,.8f,.9f);state.EnvironmentLightIntensity=.4f;
            state.HasAmbientProbe=true;state.AmbientProbe.C0=new(.2f,.3f,.4f);
            state.HasAuthoredKey=true;state.KeyDirection=new(0,1,0);state.KeyColour=new(.5f,.6f,.7f);state.KeyIntensity=.75f;
        }
        internal static void ApplyEnvironment(Transform frame,in GloomhavenVR.Net.TownActivityState state)
        { if(state.HasEnvironmentLight){Replays++;LastReplay=state;} }
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
    using System.Collections.Generic;
    using UnityEngine;
    internal readonly struct TownTempleDonationState
    {
        internal readonly int Peer;
        internal readonly uint Session;
        internal readonly bool Known, Available;
        internal readonly uint Revision;
        internal readonly float TransitionAge;
        internal readonly bool HasCommitAge;
        internal TownTempleDonationState(int peer,uint session,bool known,bool available,uint revision,float transitionAge,bool hasCommitAge=false)
        { Peer=peer;Session=session;Known=known;Available=available;Revision=revision;TransitionAge=transitionAge;HasCommitAge=hasCommitAge; }
    }
    internal sealed class TownServiceSessionInfo
    { internal bool Active;internal byte Service;internal float ReceivedTime,LastSeenTime,SessionAge;internal int Peer; }
    internal static class TownServiceMirror
    {
        internal static bool RemoteMerchantOffering;
        internal static bool RemoteEnhancementCue;
        internal static bool HasVisibleRemoteEnhancementCue() => RemoteEnhancementCue;
        internal static bool TempleReceived,TempleKnown,TempleAvailable=true;
        internal static int TempleOwner;
        internal static uint TempleSession,TempleRevision;
        internal static float TempleTransitionAge;
        internal static readonly List<TownTempleDonationState> TempleStates=new();
        internal static readonly Dictionary<int,TownServiceSessionInfo> RemoteSessions=new();
        internal static Func<int,Transform?>? SharedFrameForRemote;
        internal static bool TryInteractionOwner(byte service,out int player,out uint session,out float age)
        {
            player=0;session=0;age=0;
            foreach(var pair in RemoteSessions)
            {
                TownServiceSessionInfo value=pair.Value;
                if(!value.Active||value.Service!=service
                    ||UnityEngine.Time.unscaledTime-value.LastSeenTime>GloomhavenVR.Net.NetProtocol.StaleTimeoutSeconds)continue;
                if(player!=0&&pair.Key>=player)continue;
                player=pair.Key;session=0;
                age=value.SessionAge+UnityEngine.Mathf.Max(0,UnityEngine.Time.unscaledTime-value.ReceivedTime);
            }
            return player!=0;
        }
        internal static bool TryTempleDonationState(out int owner,out uint session,out bool known,
            out bool available,out uint revision,out float transitionAge)
        {
            owner=TempleOwner;session=TempleSession;known=TempleKnown;available=TempleAvailable;
            revision=TempleRevision;transitionAge=TempleTransitionAge;return TempleReceived;
        }
        internal static bool TryTemplePresentationState(out bool hasVisitor,out bool anyCanDonate)
        {
            hasVisitor=TempleStates.Count>0||TempleReceived;
            anyCanDonate=!hasVisitor;
            if(TempleStates.Count>0)
            {foreach(var state in TempleStates)if(!state.Known||state.Available)anyCanDonate=true;}
            else if(TempleReceived)anyCanDonate=!TempleKnown||TempleAvailable;
            return hasVisitor;
        }
        internal static void CollectTempleDonationStates(List<TownTempleDonationState> destination)
        {
            destination.Clear();
            if(TempleStates.Count>0)destination.AddRange(TempleStates);
            else if(TempleReceived)destination.Add(new TownTempleDonationState(TempleOwner,TempleSession,
                TempleKnown,TempleAvailable,TempleRevision,TempleTransitionAge));
        }
    }
}
namespace GloomhavenVR.Net
{
    using UnityEngine;
    internal static class NetPlayerActors { internal static int Local=10; internal static int LocalPlayerId()=>Local; }
    internal static class NetProtocol { internal const float StaleTimeoutSeconds=3; internal const byte ExtIdTownResidents=79,ExtIdTownActivity=81,ExtIdTownFace=80,MsgTownActivity=22,MsgTownFace=21,Version=3; internal const uint Magic=0x47565231; }
    internal struct RigPose { internal Vector3 Position; internal Quaternion Rotation; }
    internal struct PresenceState { internal bool TownActivityRecordSeen; internal bool HasTownActivity;internal TownActivityState TownActivity;internal bool HasTownFace;internal TownFaceState TownFace; internal bool HasTownResidents; internal TownResidentsState TownResidents; }
    // Codec mechanics are elementary byte operations. Production validators,
    // writers/readers and packet framing below execute unchanged, not a Valid=true stub.
    internal static class AvatarSerializer
    {
        internal static void WritePoseShared(byte[] b,ref int o,in RigPose p)=>throw new Exception("Unused resident codec boundary");
        internal static void WriteF32(byte[] b,ref int o,float v){BitConverter.GetBytes(v).CopyTo(b,o);o+=4;}
        internal static void WriteU32(byte[] b,ref int o,uint v){BitConverter.GetBytes(v).CopyTo(b,o);o+=4;}
        internal static void WriteI16(byte[] b,ref int o,short v){BitConverter.GetBytes(v).CopyTo(b,o);o+=2;}
        internal static short ReadI16(byte[] b,ref int o){short v=BitConverter.ToInt16(b,o);o+=2;return v;}
        internal static uint ReadU32(byte[] b,ref int o){uint v=BitConverter.ToUInt32(b,o);o+=4;return v;}
        internal static float ReadF32(byte[] b,ref int o){float v=BitConverter.ToSingle(b,o);o+=4;return v;}
        internal static void ReadPoseShared(byte[] b,ref int o,out RigPose p)=>throw new Exception("Unused resident codec boundary");
    }
    internal static class NetPacket
    { internal static byte PeekType(byte[] b,int count)=>count>=6&&BitConverter.ToUInt32(b,0)==NetProtocol.Magic&&b[4]==NetProtocol.Version?b[5]:(byte)0; }

}

namespace GloomhavenVR.WorldUI { internal static class TownServiceFaceSpeech { internal static System.Action? ResetObserver {get;set;} } }
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

// Actual return lifetime has its own production-bound Unity suite.
namespace GloomhavenVR.WorldUI {
 internal static class TownServiceEnhancementHandoff { internal static readonly System.Collections.Generic.List<object> Returning = new(); internal static bool HasVisibleCue; }
}

namespace GloomhavenVR.WorldUI { internal static class TownServiceMerchantHandoff { internal static bool WantsOffering; } }
