using System;
using System.Collections.Generic;
using N = System.Numerics;
namespace UnityEngine
{
    internal struct Vector2 { internal float x,y; internal Vector2(float a,float b){x=a;y=b;} }
    internal struct Vector3
    {
        internal float x,y,z;
        internal Vector3(float a,float b,float c){x=a;y=b;z=c;}
        internal static Vector3 zero=>default; internal static Vector3 one=>new(1,1,1);
        internal static Vector3 forward=>new(0,0,1); internal static Vector3 up=>new(0,1,0);
        internal float sqrMagnitude=>x*x+y*y+z*z;
        internal Vector3 normalized=>this/MathF.Sqrt(sqrMagnitude);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
    }
    internal struct Quaternion
    {
        internal N.Quaternion Value;
        internal static Quaternion identity=>new(){Value=N.Quaternion.Identity};
        internal static Quaternion LookRotation(Vector3 forward,Vector3 up)=>new(){Value=N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY,MathF.Atan2(forward.x,forward.z))};
        internal Vector3 eulerAngles=>new(0,MathF.Atan2(2*Value.W*Value.Y,1-2*Value.Y*Value.Y)*180/MathF.PI,0);
    }
    internal sealed class Transform { internal Vector3 position,localScale=Vector3.one,forward=Vector3.forward; internal Quaternion rotation=Quaternion.identity; }
    internal sealed class GameObject { internal readonly Transform transform=new(); internal bool activeInHierarchy=true; }
    internal sealed class Camera { internal readonly Transform transform=new(); }
    internal static class Time { internal static float unscaledTime=10; internal static int frameCount=100; }
    internal static class Mathf { internal static int Clamp(int x,int a,int b)=>Math.Clamp(x,a,b); internal static int RoundToInt(float x)=>(int)MathF.Round(x); }
}
namespace UnityEngine.UI
{
    internal class Selectable { internal bool Interactable=true; internal bool IsInteractable()=>Interactable; }
    internal sealed class UIWindow { internal uint Key=42; internal int NativeContinues; internal string name="original reward"; }
}
internal sealed class ExtendedButton : UnityEngine.UI.Selectable { internal readonly UnityEngine.GameObject gameObject=new(); internal bool enabled=true; }
internal sealed class UICampaignRewardWindow
{
    internal UnityEngine.UI.UIWindow window=null!; internal bool isActiveAndEnabled=true,isRevealing;
    internal Action? continueAction; internal ExtendedButton? continueButton;
    internal void OnContinueButtonClick()=>continueAction?.Invoke();
}
internal sealed class UIRewardsManager { internal bool ProcessingRewards=true,isConfirmPressed,networkProcessIfServer; internal Func<bool>? interactionChecker; }
internal static class FFSNetwork { internal static bool IsClient; }
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Info(string a,string m){} internal static void Note(string a,string m){} } }
namespace GloomhavenVR.WorldUI
{
    using UnityEngine; using UnityEngine.UI;
    internal enum SharedWindowKind { None,ScenarioStory,MapStory,QuestConfirm,Encounter,Video,RewardShowcase }
    internal sealed class ConvertedPanel { internal readonly GameObject HostGo=new(); internal bool SharedSpawnPoseReady,RevealPending=true; internal bool IsAlive=true; internal SharedWindowKind SharedDesignKind=SharedWindowKind.RewardShowcase; }
    internal interface IPanelGrabOwner { Transform? GrabRoot { get; } }
    internal sealed class GrabbableModal : IPanelGrabOwner
    {
        internal readonly Transform Root=new(); internal ConvertedPanel? Panel; internal bool IsGrabbed; internal int Snaps,Placements;
        Transform? IPanelGrabOwner.GrabRoot=>Root;
        internal void SnapFrameTo(Vector3 p,Quaternion q){Root.position=p;Root.rotation=q;if(Panel!=null){Panel.HostGo.transform.position=p;Panel.HostGo.transform.rotation=q;}Snaps++;}
        internal void PlaceFrameAt(Vector3 p,Quaternion q){Root.position=p;Root.rotation=q;if(Panel!=null){Panel.HostGo.transform.position=p;Panel.HostGo.transform.rotation=q;}Placements++;}
    }
    internal static class SharedWindows { internal static bool Online=true; internal static bool ParticipatesHere(SharedWindowKind kind)=>Online&&kind!=SharedWindowKind.None; }
    internal static class SharedWindowSize { internal static float ReferenceWorldScale=12; internal static bool UsesSharedWorldFrame(ConvertedPanel? p)=>p!=null&&SharedWindows.ParticipatesHere(p.SharedDesignKind); }
    internal static partial class SharedWindowSizeLaw { }
    internal static class SharedWindowSpawnBridge { internal static bool IsInitialAuthor; internal static bool Handles(ConvertedPanel? p)=>p!=null&&p.SharedDesignKind!=SharedWindowKind.RewardShowcase; }
    internal static class CanvasConversion { internal static Camera? WorldCamera=new(); }
    internal static class PanelLayout { internal static float WorldScale=12; }
    internal static class PanelPoseWatch { internal enum Writer { Placement } internal static void Announce(ConvertedPanel p,Writer w,string why){} }
    internal static class RewardShowcaseIdentity { internal static uint ContentKey(UIWindow? w)=>w?.Key??0; }
    internal static class WorldUIConfig { internal static bool ConversionActive=true; }
    internal static class FlatScreen { internal static bool ManualScreenActive; }
    internal static partial class RewardShowcase
    {
        internal static UIWindow? Window;
        internal static uint ContentKey=>Window?.Key??0;
        private static bool _enabled=true;
        private static int _lastConfirmFrame=-1;
        private static UIRewardsManager? _pendingNativeInput;
        private static UICampaignRewardWindow? CampaignWindow=>null;
        private static UIRewardsManager? Guildmaster=>null;
        internal static bool CanConfirm=>ConfirmBlock()==ContinueBlock.None;
        internal static void ResetCallback(){_lastConfirmFrame=-1;_pendingNativeInput=null;}
        internal static UIRewardsManager? Pending=>_pendingNativeInput;
    }
    internal static class PostQuestRewardSync
    {
        internal static bool Permission=true;
        internal static bool Owns(UIWindow? w)=>w!=null;
        internal static bool CanConfirm=>Permission;
        internal static bool TryConfirm(){RewardShowcase.Window!.NativeContinues++;return true;}
    }
    internal static partial class ModalFallback
    {
        private const float WindowDistanceMeters=1.3f,MaxSpawnPitchDeg=15;
        private struct HeightDecision{}
        private struct SpawnAnchor { internal bool Valid; internal Vector3 HeadPos,HeadForward,RawPos; internal float Scale; internal int ArcSlot; }
        private sealed class WindowPanel { internal UIWindow Window=null!; internal ConvertedPanel Panel=null!; internal GrabbableModal? Grab; internal SpawnAnchor SpawnAnchor; internal bool PoseRePlaceDone; internal float ExtraScale=1; }
        private static readonly List<WindowPanel> Converted=new();
        private static SpawnAnchor s_lastSpawnAnchor;
        internal static bool Failed;
        internal static int Spent,ClampCalls;
        internal static ConvertedPanel? PanelFor(UIWindow? w)=>Converted.Find(p=>ReferenceEquals(p.Window,w))?.Panel;
        internal static bool RewardPlacementFailed(UIWindow? w)=>Failed;
        internal static bool TryGetGrabFor(UIWindow? w,out GrabbableModal? g){g=Converted.Find(p=>ReferenceEquals(p.Window,w))?.Grab;return g!=null;}
        internal static void PreserveRewardInitialPose(UIWindow w){var p=Converted.Find(p=>ReferenceEquals(p.Window,w));if(p!=null){p.SpawnAnchor=default;p.PoseRePlaceDone=true;}}
        internal static void NoteSharedAnchorSpent(SharedWindowKind kind,string why)=>Spent++;
        private static Vector2 PanelWorldHalfSize(ConvertedPanel p,float scale)=>new(scale*.5f,scale*.3f);
        private static void ClampSpawnPose(Vector3 h,Vector3 f,ref Vector3 p,float scale,Vector2 half,float max,bool centreAtEyeLevel,out HeightDecision result){ClampCalls++;p.y=h.y;result=default;}
        internal static void Reset(){Converted.Clear();Failed=false;Spent=ClampCalls=0;s_lastSpawnAnchor=default;}
        internal static void Register(UIWindow w,ConvertedPanel p,GrabbableModal g){g.Panel=p;Converted.Add(new(){Window=w,Panel=p,Grab=g});}
        internal static bool Preview(ConvertedPanel p,out Vector3 position)=>ComputeSharedHostSpawn(p,null,new(1,1),out position,out _,out _);
        internal static void Capture(ConvertedPanel p){var wp=Converted.Find(w=>ReferenceEquals(w.Panel,p))!;wp.SpawnAnchor=s_lastSpawnAnchor;}
        internal static bool Preserved(ConvertedPanel p){var wp=Converted.Find(w=>ReferenceEquals(w.Panel,p))!;return wp.PoseRePlaceDone&&!wp.SpawnAnchor.Valid;}
    }
}
namespace GloomhavenVR.Net
{
    internal static partial class NetProtocol { }
    internal static class NetPlayerActors { internal static int Id=2; internal static int LocalPlayerId()=>Id; }
    internal static partial class NetAvatarDriver { }
    internal static partial class VersionGuard
    {
        internal readonly record struct Member(int Id);
        private static readonly List<Member> CensusRoster=new();
        internal static readonly Dictionary<int,int> Builds=new();
        internal static int PeerBuild(int id)=>Builds.TryGetValue(id,out int b)?b:0;
        internal static void Roster(params int[] peers){CensusRoster.Clear();foreach(int id in peers)CensusRoster.Add(new(id));}
    }
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] b,ref int at,uint n){for(int i=0;i<4;i++)b[at++]=(byte)(n>>(8*i));}
        internal static uint ReadU32(byte[] b,ref int at){uint n=0;for(int i=0;i<4;i++)n|=(uint)b[at++]<<(8*i);return n;}
    }
    internal static partial class RemoteMapStory
    {
        private readonly struct PeerEntry { internal readonly uint ContentKey; internal readonly bool HasPose; internal PeerEntry(uint key,bool pose){ContentKey=key;HasPose=pose;} }
        private static readonly RewardPoseHandshake RewardHandshake=new();
        private static readonly Dictionary<int,PeerEntry> RewardPeers=new();
        private static readonly Dictionary<int,bool> RewardReady=new(),RewardUnavailable=new();
        private static readonly List<int> RewardParticipants=new();
        private static readonly Dictionary<int,byte> RewardStamp=new();
        private static readonly Dictionary<int,float> RewardStampAt=new();
        private static readonly Dictionary<int,bool> RewardMoved=new();
        private static readonly List<int> Scratch=new();
        private sealed class Local { internal int FollowingPeer; }
        private static readonly Local RewardLocal=new();
        private static int _rewardPlayerId;
        private static bool _rewardMoved;
        private const float PeerStaleSeconds=10;
        private static bool _rewardSentReady,_rewardSentPose;
        private static uint RewardKey=>WorldUI.RewardShowcase.ContentKey;
        internal static int Owner(uint key,int id)=>RewardInitialOwner(key,id);
        internal static void Reset(){RewardHandshake.Reset();RewardPeers.Clear();RewardReady.Clear();RewardUnavailable.Clear();_rewardSentReady=_rewardSentPose=false;}
        internal static void Refresh(int id){RewardHandshake.SetLocalKey(RewardKey);RewardHandshake.Refresh(id,RewardParticipants,0,10);}
        internal static RewardPoseHandshakeState Request()=>RewardHandshake.Sample();
        internal static void Observe(int id,RewardPoseHandshakeState h)=>RewardHandshake.Observe(id,h,0);
        internal static void Visible(int id,uint key){RewardPeers[id]=new(key,true);RewardReady[id]=true;}
        private static void SetRewardIdentity(uint key)=>RewardHandshake.SetLocalKey(key);
        private static void RefreshRewardHandshake(int id){NetAvatarDriver.CollectRewardPosePeers(RewardParticipants);RewardHandshake.Refresh(id,RewardParticipants,0,10);}
        private static void PruneStale(Dictionary<int,PeerEntry> peers,Dictionary<int,byte> stamps,Dictionary<int,float> times){}
        private static void PublishRewardInitialPose(uint key)=>WorldUI.RewardShowcasePlacement.ClearInitialPose();
        private static void SelectRewardCandidates(uint key,int id){}
        private static void ResolvePose(WorldUI.SharedWindowKind kind,Local local,uint key,Dictionary<int,PeerEntry> peers,Dictionary<int,float> times){}
        private static readonly Dictionary<int,PeerEntry> RewardCandidates=new();
        internal static int PlayerId=>_rewardPlayerId;
        internal static bool Moved=>_rewardMoved;
    }
}
