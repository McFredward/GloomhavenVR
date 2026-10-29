using System;
using System.Collections.Generic;
using N = System.Numerics;

// Managed arithmetic is an explicit boundary for this portable transport proof. Physical Unity
// hierarchy and picture writers have their independent shared-window-geometry-runtime proof.
namespace UnityEngine
{
    internal struct Vector2
    {
        internal float x,y;
        internal Vector2(float a,float b) {x=a;y=b;}
    }
    internal struct Vector3 : IEquatable<Vector3>
    {
        internal float x, y, z;
        internal Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        internal static Vector3 zero => default;
        internal static Vector3 one => new(1,1,1);
        internal float sqrMagnitude => x*x+y*y+z*z;
        internal N.Vector3 Native => new(x,y,z);
        internal static Vector3 From(N.Vector3 v) => new(v.X,v.Y,v.Z);
        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? value) => value is Vector3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(x,y,z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float f) => new(a.x*f,a.y*f,a.z*f);
        public static Vector3 operator /(Vector3 a, float f) => a*(1/f);
        internal static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float f) => a+(b-a)*f;
    }
    internal struct Quaternion : IEquatable<Quaternion>
    {
        internal N.Quaternion Value;
        internal Quaternion(float x,float y,float z,float w) { Value=new N.Quaternion(x,y,z,w); }
        internal float x=>Value.X; internal float y=>Value.Y; internal float z=>Value.Z; internal float w=>Value.W;
        internal static Quaternion identity => new() { Value = N.Quaternion.Identity };
        internal static Quaternion Euler(float x, float y, float z) => new() { Value = N.Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180) };
        internal Vector3 eulerAngles => new(0,(MathF.Atan2(2*(Value.W*Value.Y+Value.X*Value.Z),1-2*(Value.Y*Value.Y+Value.X*Value.X))*180/MathF.PI+360)%360,0);
        internal static Quaternion Inverse(Quaternion q) => new() { Value = N.Quaternion.Inverse(q.Value) };
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { Value = a.Value*b.Value };
        public static Vector3 operator *(Quaternion q, Vector3 v) => Vector3.From(N.Vector3.Transform(v.Native,q.Value));
        public bool Equals(Quaternion q) => Value.Equals(q.Value);
        public override bool Equals(object? value) => value is Quaternion q && Equals(q);
        public override int GetHashCode() => Value.GetHashCode();
        internal static float Angle(Quaternion a, Quaternion b) => 2*MathF.Acos(MathF.Min(1,MathF.Abs(N.Quaternion.Dot(a.Value,b.Value))))*180/MathF.PI;
        internal static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float f) => new() { Value = N.Quaternion.Slerp(a.Value,b.Value,f) };
    }
    internal static class Mathf
    {
        internal static float Max(float a,float b)=>MathF.Max(a,b);
        internal static float Min(float a,float b)=>MathF.Min(a,b);
        internal static float Abs(float v)=>MathF.Abs(v);
        internal static float Clamp(float v,float min,float max)=>Math.Clamp(v,min,max);
        internal static int Clamp(int v,int min,int max)=>Math.Clamp(v,min,max);
        internal static float Clamp01(float v)=>Clamp(v,0,1);
        internal static float LerpUnclamped(float a,float b,float f)=>a+(b-a)*f;
        internal static int RoundToInt(float v)=>(int)MathF.Round(v);
    }
    internal static class Time { internal static float unscaledTime = 10; internal static int frameCount = 100; }
    internal sealed class Transform
    {
        internal Vector3 position, localScale=Vector3.one;
        internal Quaternion rotation=Quaternion.identity;
    }
}
internal static class FFSNetwork { internal static bool IsOnline=true,IsHost=false; }
namespace FFSNet { internal static class FixtureNamespace { } }
namespace UnityEngine.UI { internal class UIWindow { internal string name="native window"; } }
internal sealed class DialogLineDTO { internal string text="page-key", character="hero-guid"; }
internal sealed class UICharacterStoryBox { internal List<DialogLineDTO>? dialogs=new() { new() }; internal int currentDialogIndex=0; }
internal sealed class UIQuestPopup { internal uint Key = 303; }
internal sealed class UIEventPanel : UnityEngine.UI.UIWindow { internal uint Key = 404; }
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Info(string area,string message) { } }
}
namespace GloomhavenVR.WorldUI
{
    using UnityEngine;
    using UnityEngine.UI;
    internal enum SharedWindowKind { None,ScenarioStory,MapStory,QuestConfirm,Encounter,Video,RewardShowcase }
    internal interface IPanelGrabOwner { Transform? GrabRoot { get; } bool GrabVisible { get; } }
    internal sealed class GrabbableModal : IPanelGrabOwner
    {
        internal readonly Transform Root=new();
        internal bool Visible = true, IsGrabbed = false;
        internal void YieldSharedControl() { IsGrabbed = false; }
        internal float SharedReferenceWorldScale { get; private set; } = 11.47f;
        internal float ReferenceAtLastPlacement;
        internal int ReferenceApplications;
        internal void ApplySharedReferenceWorldScale(float reference)
        { SharedReferenceWorldScale=reference; ReferenceApplications++; }
        Transform? IPanelGrabOwner.GrabRoot => Root;
        bool IPanelGrabOwner.GrabVisible => Visible;
        internal void PlaceFrameAt(Vector3 pos,Quaternion rot) {ReferenceAtLastPlacement=SharedReferenceWorldScale;Root.position=pos;Root.rotation=rot;}
    }
    internal static class SharedWindows
    {
        internal static bool ParticipatesHere(SharedWindowKind kind) => true;
        internal static readonly Dictionary<SharedWindowKind,GrabbableModal> Grabs=new();
        internal static readonly Dictionary<SharedWindowKind,UIWindow> Windows=new();
        internal static bool TryGetGrab(SharedWindowKind kind,out GrabbableModal? grab)=>Grabs.TryGetValue(kind,out grab);
        internal static UIWindow? WindowOf(SharedWindowKind kind)=>Windows.TryGetValue(kind,out UIWindow? w)?w:null;
    }
    internal static class SharedWindowReflowBridge
    {
        internal static Func<bool>? CanArrange;
        internal static Func<SharedWindowKind,bool>? Begin;
        internal static Func<SharedWindowKind,int>? Revision;
        internal static Action<SharedWindowKind>? End;
    }
    internal static class SharedWindowSpawnBridge
    {
        internal delegate bool InitialPoseReader(SharedWindowKind kind,out Vector3 pos,out Quaternion rot,out float factor);
        internal static Func<bool>? InitialAuthor { get; set; }
        internal static InitialPoseReader? TryInitialPose { get; set; }
    }
    internal static class ModalFallback
    {
        internal static bool Ready=true;
        internal const string StoryWindowLogName="native story";
        internal static bool SharedInitialPoseReady(SharedWindowKind kind)=>Ready;
        internal static bool TryGetStoryGrab(out GrabbableModal? grab)=>SharedWindows.TryGetGrab(SharedWindowKind.ScenarioStory,out grab);
        internal static void NoteSharedAnchorSpent(SharedWindowKind kind,string why) { }
    }
    internal static class SharedWindowSize
    {
        internal static float ReferenceWorldScale=12;
        internal static void NoteRemotePuller(SharedWindowKind kind,int peer) { }
    }
    internal static class SharedWindowIdentity { internal static void NotePoseApplied(SharedWindowKind kind) { } }
    internal static class PanelLayout
    {
        internal static Vector3 Origin;
        internal static Quaternion Yaw=Quaternion.identity;
        internal static float WorldScale=12;
        internal static bool Available=true;
        internal static bool TryGetAnchor(out Vector3 origin,out Quaternion yaw) {origin=Origin;yaw=Yaw;return Available;}
    }
    internal static class RewardShowcasePlacement
    {
        internal static bool LocalRevealPending=false, LocalPlacementFailed=false;
        internal static float CachedReference,CachedFactor; internal static uint CachedKey;
        internal static Vector3 CachedPosition; internal static Quaternion CachedRotation;
        internal static void ClearInitialPose() { CachedReference=0; CachedKey=0; }
        internal static void ClearInitialAuthority() { }
        internal static void SetInitialAuthority(uint key,bool mayReveal) { }
        internal static void SetInitialPose(uint key,Vector3 position,Quaternion rotation,float size,bool ready=true,float referenceScale=0)
        { CachedKey=key; CachedReference=referenceScale; CachedPosition=position; CachedRotation=rotation; CachedFactor=size; }
    }
    internal static class RewardShowcase { internal static object? Window=new(); internal static uint ContentKey=606; }
    // Native post-quest completion is a boundary; this transport proof exercises chest/map reward
    // placement, while the native reward suites retain opening/confirmation ownership.
    internal static class PostQuestRewardSync
    {
        internal static object? Opening=>null;
        internal static bool SendDue=>false;
        internal static void Reset() { }
        internal static GloomhavenVR.Net.MapStoryOpening[] SampleCompletions()=>Array.Empty<GloomhavenVR.Net.MapStoryOpening>();
        internal static void ObserveCompletions(int sender,GloomhavenVR.Net.MapStoryOpening[] entries) { }
        internal static bool MatchesPose(int sender,int local,uint key,GloomhavenVR.Net.MapStoryOpening[]? entries)=>true;
    }
    internal static class NativeVideoWindow { internal static bool SharedInitialPoseReady=>ModalFallback.Ready; }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    using UnityEngine;
    internal static class MapRoomDriver
    {
        internal static bool Active=false;
        internal static Vector3 Center;
        internal static float Scale=198.12f;
        internal static bool TryGetParchmentFrame(out Vector3 center,out float scale) {center=Center;scale=Scale;return Active;}
    }
}
namespace GloomhavenVR.Net
{
    using UnityEngine;
    using GloomhavenVR.WorldUI;

    internal static class PlayerRegistry { internal const int HostPlayerID=1; }
    internal static class NetPlayerActors {internal static int Id=2;internal static int LocalPlayerId()=>Id;}
    internal static class NetSession {internal static bool FlatNetMode=false;}
    internal static partial class VersionGuard
    {
        internal readonly record struct PeerInfo(int Build);
        internal readonly record struct RosterMember(int Id);
        private static readonly Dictionary<int,PeerInfo> Peers=new();
        private static readonly List<RosterMember> CensusRoster=new();
        internal static void Fresh(params int[] ids)
        {
            Peers.Clear();CensusRoster.Clear();
            foreach(int id in ids) {Peers[id]=new PeerInfo(NetProtocol.ModBuild);CensusRoster.Add(new RosterMember(id));}
        }
    }
    internal struct SharedWindowEntry
    {
        internal byte Flags,Kind,Page,PageCount,PoseStamp,SizeCode,Frame;
        internal uint ContentKey;
        internal RigPose Pose;
        internal bool InitialPose;
    }
    internal struct FixtureVideo { internal SharedWindowEntry Window; }
    internal struct PresenceState
    {
        internal bool HasStorySync,StoryInitialPose,HasSharedWindow,HasVideoWindow,HasRewardWindow,HasMapStoryLifecycle,HasRewardPoseHandshake,HasRewardContinuation;
        internal bool HasMapRoom,HasSharedWindowMotion;
        internal byte MapRoomFlags,SharedWindowHeldMask,SharedWindowReflowMask;
        internal byte StoryFlags,StoryPage,StoryPageCount,StoryPoseStamp,StorySizeCode;
        internal uint StoryKey;
        internal RigPose StoryPose;
        internal SharedWindowEntry[]? SharedWindowEntries;
        internal int SharedWindowCount;
        internal FixtureVideo VideoWindow;
        internal RewardWindowState RewardWindow;
        internal RewardPoseHandshakeState RewardPoseHandshake;
        internal MapStoryOpening[]? RewardContinuationEntries;
        internal SharedWindowPoseContext? WindowPoseContext;
        internal SharedWindowScaleContext? WindowScales;
        internal SharedWindowMoveOrder? WindowMoves;
        internal MapStoryOpening[]? MapStoryLifecycleEntries;
    }
    internal static partial class MapStoryLifecycle
    {
        internal static MapStoryOpeningLedger Ledger=new(112);
        internal static object Current=new();
        internal static bool MatchesPose(bool map,int sender,uint epoch,uint token)=>Ledger.MatchesPose(Current,sender,NetPlayerActors.Id,epoch,token);
    }
    internal static partial class RemoteStorySync
    {
        private const float PeerStaleSeconds=5,MoveEpsilonMeters=.005f,MoveEpsilonDegrees=.5f,MoveEpsilonSize=.005f,MoveSettleSeconds=.25f,FinishedLingerSeconds=60;
        private static readonly Dictionary<int,PeerStory> Peers=new();
        private static readonly Dictionary<int,byte> PeerPoseStamp=new();
        private static readonly Dictionary<int,float> PeerPoseChangedAt=new();
        private static readonly SharedWindowPoseTrack PoseTrack=new();
        private static int _poseTrackPeer,_followingPeer;
        private static uint _localKey;
        private static byte _localPageCount,_localPoseStamp,_followedStamp;
        private static float _finishedUntil,_lastFrameSize=1,_localMoveSettleAt;
        private static Vector3 _lastFramePos;
        private static Quaternion _lastFrameRot=Quaternion.identity;
        private static bool _haveFrameBaseline,_localPoseOwned,_localMoving,_followedStampValid;
        internal static UICharacterStoryBox? CurrentBox=new();
        private static UICharacterStoryBox? Box()=>CurrentBox;
        private static void NoteIgnored(string reason) { }
        private static string DescribeAnchorLocal(Vector3 pos,Quaternion rot)=>"fixture";
    }
    internal static partial class RemoteMapStory
    {
        private const float PeerStaleSeconds=5,MoveEpsilonMeters=.005f,MoveEpsilonDegrees=.5f,MoveEpsilonSize=.005f,MoveSettleSeconds=.25f;
        private const int IdentitySettleFrames=2;
        private const string Scope="fixture";
        private static readonly Local StoryLocal=new(),QuestLocal=new(),EncounterLocal=new();
        private static readonly Dictionary<int,PeerEntry> StoryPeers=new(),QuestPeers=new(),EncounterPeers=new();
        private static readonly Dictionary<int,float> StoryStampAt=new(),QuestStampAt=new(),EncounterStampAt=new();
        private static readonly Dictionary<int,byte> StoryStamp=new(),QuestStamp=new(),EncounterStamp=new();
        private static readonly List<int> Scratch=new();
        private static int _storyPublishedFrame,_storyAppliedFrame,_storyAppliedPeer;
        private static byte _storyPublishedStamp,_storyAppliedStamp;
        private static bool _storyPublishedMoving;
        internal static UICharacterStoryBox? CurrentBox=new();
        internal static UIQuestPopup? CurrentQuest=new();
        internal static UIEventPanel? CurrentEvent=new();
        private static UICharacterStoryBox? MapBox()=>CurrentBox;
        private static UIQuestPopup? QuestPopup()=>CurrentQuest;
        private static UnityEngine.UI.UIWindow? EventPanel()=>CurrentEvent;
        private static uint QuestKey(UIQuestPopup popup)=>popup.Key;
        private static uint EncounterKey(UnityEngine.UI.UIWindow panel)=>((UIEventPanel)panel).Key;
        private static void Note(string reason) { }
        private static void WarnIfOutOfCone(SharedWindowKind kind,int peer,Vector3 pos) { }
    }
}
