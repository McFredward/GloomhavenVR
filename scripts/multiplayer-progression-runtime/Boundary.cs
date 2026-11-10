// Explicit ports: native window/model existence, registry readiness and transport claims.
// Real Unity transforms, cameras, meshes, shader depth and final pixels execute below.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
public static class FFSNetwork { public static bool IsOnline = true; }
public static class PhaseManager { public static int PhaseType; }
namespace ScenarioRuleLibrary { internal class FixtureNamespace { } }
public class CameraController { public static CameraController s_CameraController = new(); public Vector3 FocusPoint=Vector3.zero; }
public class Singleton<T> where T:class { public static T? Instance; public static bool IsInitialized => Instance != null; }
public class UIReadyToggle : MonoBehaviour {
 public enum EReadyUpType { Participant, Player }
 public IList PlayersReady { get; } = new List<object>(); public bool IsVisible, ShouldBeVisible, IsProgressingBar;
 public EReadyUpType ReadyUpType;
}
namespace UnityEngine.UI { internal class UIWindow { internal bool IsOpen=true; } }
namespace FFSNet { public static class PlayerRegistry { public static IList Participants { get; }=new List<object>(); public static IList AllPlayers = Participants; } }
namespace HarmonyLib { internal static class AccessTools {
 internal static PropertyInfo? Property(Type t,string n)=>t.GetProperty(n,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
 internal static Type? TypeByName(string n)=>typeof(AccessTools).Assembly.GetType(n);
} }
namespace GloomhavenVR.Core { internal static class VRSession { internal static bool IsRunning=true; } internal static class VRLog { internal static void Note(string tag,string text) { } internal static void Info(string tag,string text) { } internal static void Warn(string tag,string text) { } } }
namespace GloomhavenVR.Rig {
 internal static class VRRigDriver { internal static Camera? HeadCamera; }
 internal static class SpawnRing {
 internal static int Reads; internal struct Footprint { public Vector3 Center; public Vector2 OuterHalfWorld; }
 internal static bool TryFootprint(float y,out Footprint b) { Reads++; b=new Footprint { Center=Vector3.zero, OuterHalfWorld=new Vector2(5,5) }; return true; }
 } }
namespace GloomhavenVR.Board { internal static class SelectionReadyHighlighter { internal static float PulseAlpha=>.7f; } }
namespace GloomhavenVR.Net {
 internal static class BoardVisual {
 internal static Material Unlit(Color c) { var m=new Material(Shader.Find("GloomhavenVR/Overlay"));m.color=c;return m; }
 internal static MeshRenderer Quad(Transform p,string n,Vector2 s,Material m) {
 var go=GameObject.CreatePrimitive(PrimitiveType.Quad);go.name=n;go.transform.SetParent(p,false);go.transform.localScale=new Vector3(s.x,s.y,1);
 UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());var r=go.GetComponent<MeshRenderer>();r.sharedMaterial=m;return r;
 }
 internal static void OrderWithPanels(Renderer[] r,float d) { }
 }
 internal static class NetProtocol { internal const byte DecisionKindNone=0;internal const float SendRateHz=15f; } internal static class NetSession { internal static bool FlatNetMode=false; }
 internal static class NetPlayerActors { internal static int PlayerIdOf(object o)=>(int)o; }
 internal class RemoteAvatar { internal bool HasDecisionAttribution,DecisionPending;internal int DecisionActorId;internal byte DecisionPromptKind; }
 internal static class SharedWindowMoves { internal static bool MayBegin=true,Active=true;internal static int Claims;
 internal static SharedWindowMoveRegister Owner=null!,Observer=null!;
 internal static void Setup(SharedWindowKind kind){Claims=0;MayBegin=Active=true;Owner=new(1,(byte)kind);Observer=new(2,(byte)kind);Owner.SetAutomaticAuthor(1,0);Observer.SetAutomaticAuthor(1,0);Owner.Open(77,1);Observer.Open(77,1);Owner.Sample(out var opening);Observer.Observe(1,in opening,1,0);}
 internal static bool BeginAutomatic(SharedWindowKind kind) { if(!MayBegin)return false;if(Active&&!Owner.Begin(0,true))return false;Claims++;return true; }
 internal static bool MayWrite(SharedWindowKind kind)=>Owner.LocalWins;
 internal static bool HasLocalClaim(SharedWindowKind kind)=>Owner.HasLocalClaim;
 internal static void Deliver(float at){Owner.Sample(out var row);Observer.Observe(1,in row,1,at);if(!Observer.Accepts(1,in row))throw new Exception("observer rejects actual automatic flight claim");}
 internal static void RemoteGrab(float at){if(!Observer.Begin(at))throw new Exception("remote grip cannot begin");Observer.Sample(out var row);Owner.Observe(2,in row,1,at);}
 }
 internal sealed partial class NetAvatarDriver : MonoBehaviour {
 private static NetAvatarDriver? _instance; private readonly Dictionary<int,RemoteAvatar> _avatars=new();
 private readonly Transport _transport=new(); private sealed class Transport { internal bool IsOnline=true;internal int LocalPlayerId=1; }
 internal static void Setup(NetAvatarDriver d){_instance=d;d._nextDecisionWait=0;}
 internal static void Peer(int id,bool pending,int actor,byte kind=1){_instance!._avatars[id]=new RemoteAvatar{HasDecisionAttribution=true,DecisionPending=pending,DecisionActorId=actor,DecisionPromptKind=kind};_instance._nextDecisionWait=0;}
 internal static int PollAt(float now){_instance!._nextDecisionWait=0;return DelayedDecisionPlayer(now);}
 }
}
namespace GloomhavenVR.WorldUI.Surfaces { internal static class DecisionDockSurface { internal static byte WirePromptKind=0; } internal static class UseBarsSurface {
 internal static int Actor=0;internal static bool Pending;
 internal static void SampleDecisionAttribution(out int a,out bool p,out bool v){a=Actor;p=Pending;v=true;}
} }
namespace GloomhavenVR.WorldUI.MapRoom { internal static class MapRoomDriver {
 internal static bool TryGetParchmentFrame(out Vector3 c,out float s){c=default;s=0;return false;}
} }
namespace GloomhavenVR.WorldUI {
 internal enum SharedWindowKind { None,ScenarioStory,MapStory,Other }
 internal class ConvertedPanel { internal GameObject HostGo=null!;internal bool RenderHidden=false; }
 internal interface IPanelGrabOwner { }
 internal partial class GrabbableModal { internal bool IsGrabbed;internal Transform Root=null!;internal int Moves;
 private Transform _frame=>Root;private bool _visualValid=false,_easing=false;
 private Vector3 _visualPos=Vector3.zero;private Quaternion _visualRot=Quaternion.identity;
 private readonly Reface _reFaceTween=new();private sealed class Reface{internal void Cancel(){}}
 private void EnsureFrame(){}private void Tick(){Moves++;if(_easing)throw new Exception("unexpected extra frame easing");}
 }
 internal static class SharedWindows { internal static bool SessionIsOnline()=>FFSNetwork.IsOnline;internal static SharedWindowKind KindOf(UIWindow? w)=>ModalFallback.Kind; }
 internal static class CanvasConversion { internal static Camera? WorldCamera; }
 internal static class SharedWindowSize { internal static float ReferenceWorldScale=10; }
 internal static class SharedWindowSpawnBridge { internal static bool IsInitialAuthor=true; }
 internal static class SharedWindowInteractionBridge { internal static Action<SharedWindowKind>? Released=null; }
 internal static class PanelPoseWatch { internal enum Writer{StoryRecovery} internal static void Announce(ConvertedPanel p,Writer w,string why){} }
 internal static partial class ModalFallback {
 internal class WindowPanel {
 internal UIWindow Window=new();internal ConvertedPanel Panel=new();internal GrabbableModal? Grab;
 internal StoryRecoveryDwell StoryRecovery=new();internal Vector3 StoryRecoverySeat;
 internal StoryRecoveryFlight? StoryRecoveryFlight;
 internal StoryRecoveryFlight? StoryRecoveryPublication;
 internal object? SpawnAnchor;internal bool PoseRePlaceDone,ReflowCancelled;
 }
 private static readonly List<WindowPanel> Converted=new();private const float WindowDistanceMeters=.95f;internal static SharedWindowKind Kind=SharedWindowKind.ScenarioStory;
 internal static bool Reflow;
 private static bool SharedReflowActive(SharedWindowKind kind)=>Reflow;
 private static bool ReflowVisible(WindowPanel wp)=>wp.Panel.HostGo.activeSelf&&!wp.Panel.RenderHidden;
 internal static WindowPanel Add(Transform root){var w=new WindowPanel{Panel=new ConvertedPanel{HostGo=root.gameObject},Grab=new GrabbableModal{Root=root}};Converted.Clear();Converted.Add(w);return w;}
 internal static void PollAt(float now){_nextStoryRecoveryPoll=0;TickStoryRecovery(now);}
 internal static void FrameAt(float now)=>TickStoryRecovery(now);
 internal static bool FastAt(float now)=>StoryRecoverySendDueAt(now);
 }
}

public class Choreographer {
 public static Choreographer s_Choreographer=new(); public bool WaitingForConfirm=false; public CWaitState? m_WaitState=null;public object? LastMessage=null;
 public class CWaitState { public ChoreographerStateType m_State; }
 public enum ChoreographerStateType { NA,WaitingForCardSelection,WaitingForItemRefresh,WaitingForLoseGoalChestRewardSelection,WaitingForElementPicked,WaitingForTileSelected,WaitingForPlayerWaypointSelection,WaitingForPlayerPushWaypointSelection,WaitingForPlayerPullWaypointSelection,WaitingForAreaAttackFocusSelection }
}
namespace GloomhavenVR.WorldUI { internal static class HealthbarForegroundDepth { internal static void Register(Transform t) { } } }
namespace GloomhavenVR.Rig {
 internal static class ComfortSettings { internal static int Writes; internal static void PersistScaleMultiplier(float m) { Writes++; } }
 internal static class RigTarget { internal static float BaseScale=11; }
 internal sealed partial class WorldGrab {
 private enum GrabState {None,OneHand,TwoHand} private GrabState _state;private Transform? _gestureRig; private bool _leftStick,_rightStick,_dragLive,_rotateLive,_scaleLive;private object? _dragHand;
 internal void Begin(Transform t){_gestureRig=t;_state=GrabState.TwoHand;_leftStick=_rightStick=_dragLive=_rotateLive=_scaleLive=true;_dragHand=new object();}
 internal void Handoff(Transform? t)=>TrackRig(t);
 internal bool Idle=>_state==GrabState.None&&!_leftStick&&!_rightStick&&!_dragLive&&!_rotateLive&&!_scaleLive&&_dragHand==null;
 } }
