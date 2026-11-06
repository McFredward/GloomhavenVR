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
        public static bool operator ==(Vector3 a,Vector3 b)=>a.Equals(b);
        public static bool operator !=(Vector3 a,Vector3 b)=>!a.Equals(b);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float f) => new(a.x*f,a.y*f,a.z*f);
        public static Vector3 operator /(Vector3 a, float f) => a*(1/f);
        internal static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float f) => a+(b-a)*f;
        internal static Vector3 Lerp(Vector3 a, Vector3 b, float f) => LerpUnclamped(a,b,Math.Clamp(f,0,1));
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
        public static bool operator ==(Quaternion a,Quaternion b)=>a.Equals(b);
        public static bool operator !=(Quaternion a,Quaternion b)=>!a.Equals(b);
        public static Quaternion operator *(Quaternion a, Quaternion b) => new() { Value = a.Value*b.Value };
        public static Vector3 operator *(Quaternion q, Vector3 v) => Vector3.From(N.Vector3.Transform(v.Native,q.Value));
        public bool Equals(Quaternion q) => Value.Equals(q.Value);
        public override bool Equals(object? value) => value is Quaternion q && Equals(q);
        public override int GetHashCode() => Value.GetHashCode();
        internal static float Angle(Quaternion a, Quaternion b) => 2*MathF.Acos(MathF.Min(1,MathF.Abs(N.Quaternion.Dot(a.Value,b.Value))))*180/MathF.PI;
        internal static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float f) => new() { Value = N.Quaternion.Slerp(a.Value,b.Value,f) };
        internal static Quaternion Slerp(Quaternion a,Quaternion b,float f)=>SlerpUnclamped(a,b,Math.Clamp(f,0,1));
    }
    internal static class Mathf
    {
        internal static float Sqrt(float v)=>MathF.Sqrt(v);
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
    public class Object
    {
        public bool Destroyed;
        public static void DontDestroyOnLoad(Object value) { }
        public static void Destroy(Object value)
        {
            value.Destroyed = true;
            if (value is GameObject root) foreach (Component component in root.Components) component.Destroyed = true;
        }
        private static bool Null(Object? value) => ReferenceEquals(value, null) || value.Destroyed;
        public static bool operator ==(Object? a, Object? b) => Null(a) && Null(b) || ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }
    public sealed class GameObject : Object
    {
        public readonly List<Component> Components = new();
        internal bool activeInHierarchy = true;
        public GameObject(string name = "fixture") { }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this }; Components.Add(component); return component;
        }
    }
    public class Component : Object { public GameObject gameObject = null!; }
    public sealed class AudioSource : Object { public bool mute; }

    internal sealed class Transform
    {
        internal Vector3 position, localScale=Vector3.one;
        internal Quaternion rotation=Quaternion.identity;
    }
}
namespace UnityEngine.Video
{
    public enum VideoRenderMode { APIOnly }
    public enum VideoAudioOutputMode { None, Direct, AudioSource }
    public sealed class VideoPlayer : Component
    {
        public bool playOnAwake, isLooping, isPrepared, isPlaying, isPaused;
        public bool canSetTime = true;
        public string url = string.Empty;
        public double time, length = 100;
        public ushort audioTrackCount;
        public VideoRenderMode renderMode;
        public VideoAudioOutputMode audioOutputMode;
        public int PlayCalls, StopCalls;
        private readonly Dictionary<ushort, bool> _mute = new();
        private readonly Dictionary<ushort, AudioSource> _targets = new();
        public event Action<VideoPlayer>? prepareCompleted, loopPointReached;
        public event Action<VideoPlayer, string>? errorReceived;
        public void Prepare() { }
        public void Prepared() { isPrepared = true; prepareCompleted?.Invoke(this); }
        public Action CapturePrepared() { var callback = prepareCompleted; return () => { isPrepared = true; callback?.Invoke(this); }; }
        public void End() { isPlaying = false; loopPointReached?.Invoke(this); }
        public void Fail() => errorReceived?.Invoke(this, "injected decoder failure");
        public void Play() { PlayCalls++; isPlaying = true; isPaused = false; }
        public void Pause() { isPlaying = false; isPaused = true; }
        public void Stop() { StopCalls++; isPlaying = isPaused = false; }
        public bool GetDirectAudioMute(ushort track) => _mute.TryGetValue(track, out bool mute) && mute;
        public void SetDirectAudioMute(ushort track, bool mute) => _mute[track] = mute;
        public void SetDirectAudioVolume(ushort track, float volume) { }
        public AudioSource GetTargetAudioSource(ushort track) => _targets.TryGetValue(track, out AudioSource? source) ? source : null!;
        public void Bind(ushort track, AudioSource source) => _targets[track] = source;
    }
}

public static class AudioController { public static float GetGlobalVolume() => 0.8f; }

internal static class FFSNetwork { internal static bool IsOnline=true,IsHost=false; }
namespace FFSNet { internal static class FixtureNamespace { } }
namespace UnityEngine.UI { internal class UIWindow { internal string name="native window"; internal bool IsOpen=true,IsVisible=true; internal UnityEngine.GameObject gameObject=new(); } }
internal sealed class DialogLineDTO { internal string text="page-key", character="hero-guid"; }
internal sealed class UICharacterStoryBox { internal List<DialogLineDTO>? dialogs=new() { new() }; internal int currentDialogIndex=0; }
internal sealed class UIQuestPopup { internal uint Key = 303; }
internal sealed class UIEventPanel : UnityEngine.UI.UIWindow { internal uint Key = 404; }
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Info(string area,string message) { } internal static void Note(string area,string message) { } }
}
namespace GloomhavenVR.WorldUI
{
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.Video;
    internal enum SharedWindowKind { None,ScenarioStory,MapStory,QuestConfirm,Encounter,Video,RewardShowcase,Results }
    internal interface IPanelGrabOwner { Transform? GrabRoot { get; } bool GrabVisible { get; } }
    internal sealed class GrabbableModal : IPanelGrabOwner
    {
        internal readonly Transform Root=new();
        internal bool Visible = true, IsGrabbed = false;
        internal void YieldSharedControl() { IsGrabbed = false; }
        internal int RoomMakingWrites;
        internal void PlaceRoomMakingSample(Vector3 position,Quaternion rotation){RoomMakingWrites++;PlaceFrameAt(position,rotation);}
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
        internal static bool SessionIsOnline()=>FFSNetwork.IsOnline;
        internal static SharedWindowKind KindOf(UIWindow? window)
        {foreach(var row in Windows)if(ReferenceEquals(row.Value,window))return row.Key;return SharedWindowKind.None;}
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
    internal static partial class ModalFallback
    {
        // Already-admitted converted windows and a completed overlap proposal are explicit
        // inputs. The actual production tween, authority/revision gates and final write run.
        private sealed class WindowPanel
        {internal UIWindow? Window;internal GrabbableModal? Grab;internal bool ReflowCancelled;}
        private static readonly List<WindowPanel> Converted=new();
        private static readonly WindowPanel?[] ReflowWindows=new WindowPanel?[ReflowCapacity];
        private static readonly bool[] ReflowIncluded=new bool[ReflowCapacity];
        private static readonly Vector3[] ReflowFrom=new Vector3[ReflowCapacity],ReflowTo=new Vector3[ReflowCapacity];
        private static readonly Quaternion[] ReflowFromRotation=new Quaternion[ReflowCapacity],ReflowToRotation=new Quaternion[ReflowCapacity];
        private static readonly int[] ReflowRevision=new int[ReflowCapacity];
        private static readonly SharedWindowKind[] ReflowKinds=new SharedWindowKind[ReflowCapacity];
        private static int _reflowCount,_reflowIncomingRevision;
        private static float _reflowStarted;
        private static bool _reflowRunning;
        private static WindowPanel? _reflowIncoming;
        private static SharedWindowKind _reflowIncomingKind;
        private static bool ReflowVisible(WindowPanel panel)=>panel.Grab?.Visible==true;
        private static void RefreshStandingArcClaims(int except) { }
        internal static void ResetRoomProof(){Converted.Clear();_reflowRunning=false;_reflowIncoming=null;_reflowCount=0;Array.Clear(ReflowWindows);Array.Clear(ReflowIncluded);}
        internal static void StartRoomProof(SharedWindowKind kind,Vector3 target)
        {
            ResetRoomProof();var older=new WindowPanel{Window=SharedWindows.Windows[kind],Grab=SharedWindows.Grabs[kind]};
            _reflowIncomingKind=SharedWindowKind.MapStory;
            _reflowIncoming=new WindowPanel{Window=SharedWindows.Windows[_reflowIncomingKind],Grab=SharedWindows.Grabs[_reflowIncomingKind]};
            Converted.Add(older);Converted.Add(_reflowIncoming);_reflowCount=1;_reflowRunning=true;_reflowStarted=Time.unscaledTime;
            ReflowWindows[0]=older;ReflowIncluded[0]=true;ReflowKinds[0]=kind;
            ReflowFrom[0]=older.Grab!.Root.position;ReflowTo[0]=target;ReflowFromRotation[0]=ReflowToRotation[0]=older.Grab.Root.rotation;
            ReflowRevision[0]=SharedWindowReflowBridge.Revision!(kind);_reflowIncomingRevision=SharedWindowReflowBridge.Revision!(_reflowIncomingKind);
        }
        internal static void TickRoomProof()=>TickRoomMakingAnimation();
        internal static bool RoomProofRunning=>_reflowRunning;
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
    internal static class ScenarioResultsNative { internal static UnityEngine.UI.UIWindow? Window = new(); }
    internal static class NativeVideoReview
    {
        internal static bool ContextAvailable = true;
        internal static bool TryCloseOwnedKey(string key)
        {
            if (!ContextAvailable || NativeVideoWindow.ReviewPlayer == null || NativeVideoWindow.ReviewKey != key) return false;
            CancelForNativePlayback(); return true;
        }
        internal static void CancelForNativePlayback()
        {
            if (NativeVideoWindow.ReviewPlayer != null) NativeVideoWindow.ReviewPlayer.Stop();
            NativeVideoWindow.ReviewPlayer = null;
        }
    }
    internal static class NativeVideoWindow
    {
        internal static VideoPlayer? NativePlayer, Remote;
        internal static uint NativePlaybackGeneration;
        internal static VideoPlayer? ReviewPlayer;
        internal static uint ReviewGeneration;
        internal static string ReviewKey = string.Empty;
        internal static uint OriginatingPlaybackGeneration => NativePlayer != null ? NativePlaybackGeneration : ReviewGeneration;
        internal static VideoPlayer? OriginatingPlayer => NativePlayer ?? ReviewPlayer;
        internal static bool SharedInitialPoseReady => ModalFallback.Ready;
        internal static bool Suppressed, AssetAvailable = true;
        internal static string NativeKey = "Heroes/A.mov", ResolvedKey = string.Empty;
        internal static string PlaybackKey => NativePlayer != null ? NativeKey : ReviewPlayer != null ? ReviewKey : string.Empty;
        internal static string RemoteKey = string.Empty;
        internal static void SetRemoteSource(VideoPlayer? player, string selectedKey = "")
        { if (player != null) NativeVideoReview.CancelForNativePlayback(); Remote = player; RemoteKey = selectedKey; }
        internal static void SetNativePresentationSuppressed(bool suppressed) => Suppressed = suppressed;
        internal static void SetRemotePose(Vector3 pos, Quaternion rot, float size) { }
        internal static bool TryResolvePlaybackKey(string key, out string url) { url = ResolvedKey.Length == 0 ? key : ResolvedKey; return AssetAvailable; }
    }
    internal static class NativeVideoMovies
    {
        internal const string StereoIntroKey = "CP_Intro/GH_CP_Intro.sbs-v2.mov";
        internal const string NativeIntroKey = "CP_Intro/GH_CP_Intro.native.mov";
        internal static bool IsStereoKey(string? key) => key != null && (key.EndsWith(".sbs-v1.mov", StringComparison.Ordinal) || key.EndsWith(".sbs-v2.mov", StringComparison.Ordinal));
        internal static bool IsStereoUrl(string url) => IsStereoKey(url);
        private static string? Canonical(string? key)
        {
            if (GloomhavenVR.Net.VideoMovieKeys.TryUnwrapReview(key, out string selected)) key = selected;
            return key == StereoIntroKey || key == NativeIntroKey ? "CP_Intro/GH_CP_Intro.mov" : key;
        }
        internal static bool SamePresentation(string? first, string? second) =>
            GloomhavenVR.Net.VideoMovieKeys.IsReview(first) == GloomhavenVR.Net.VideoMovieKeys.IsReview(second)
            && Canonical(first) == Canonical(second);
    }
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
        internal VideoWindowState VideoWindow;
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
