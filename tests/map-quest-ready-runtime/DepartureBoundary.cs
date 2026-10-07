// Native OnPlayerLeft, ACK coroutine, Proceed/Reset and ReadyUp/Unready bodies are unchanged.
// Only platform/render/input/network delivery and the MEC clock/scheduler are boundaries.
using System;
using System.Collections.Generic;
using System.Linq;
using FFSNet;
using MEC;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GloomhavenVR.WorldUI.MapRoom;

namespace UnityEngine
{
    internal readonly struct Color { }
    internal sealed class GameObject { public bool activeSelf; public bool activeInHierarchy => activeSelf; public void SetActive(bool value) => activeSelf=value; }
}
namespace UnityEngine.Events { internal delegate void UnityAction(); internal delegate void UnityAction<T>(T arg); internal delegate void UnityAction<T,U>(T a,U b); }
namespace UnityEngine.UI
{
    internal sealed class UIWindow { public bool IsOpen; public void Show() => IsOpen=true; public void Hide() => IsOpen=false; public readonly GameObject gameObject = new(); }
    internal sealed class Toggle { public bool interactable; }
}
#if !REAL_HARMONY
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method,AllowMultiple=true)] internal sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type,string method) { } public HarmonyPatch(Type type,string method,Type[] arguments) { } }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyPostfix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] internal sealed class HarmonyFinalizer : Attribute { }
    internal sealed class Harmony { public Harmony(string name) { } public readonly List<Type> Installed=new(); public void PatchAll(Type type) => Installed.Add(type); }
    internal static class AccessTools
    {
        public static Type? TypeByName(string name) => typeof(AccessTools).Assembly.GetType(name);
        public static System.Reflection.PropertyInfo? Property(Type type,string name) => type.GetProperty(name,System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static);
    }
}
#endif
namespace GloomhavenVR.Core
{
    internal static class VRSession { public static bool IsRunning=true; public static HarmonyLib.Harmony? Harmony=new("gloomhavenvr.quest.departure.fixture"); }
    internal static class VRLog { public static List<string> Notes=new(); public static void Note(string scope,string text) => Notes.Add(text); }
}
namespace Photon.Bolt { internal interface IProtocolToken { } }
namespace FFSNet
{
    internal enum ActionPhaseType { MapHQ,MapAtLinkedScenario,MapLoadoutScreen,ScenarioEnded }
    internal enum GameActionType { ReadyUpPlayer,UnreadyPlayer,AllPlayersReady,ReadyProceed }
    internal sealed class NetworkPlayer
    {
        public int PlayerID { get; set; } public string Username => "player"+PlayerID;
        public bool IsParticipant { get; set; }=true; public bool IsActive=true,HasDesynched;
        public readonly List<NetworkPlayer> PlayersACKedMyLatestControllableState=new();
    }
    internal delegate void PlayersChangedEvent(NetworkPlayer player);
    internal static class PlayerRegistry
    {
        public static NetworkPlayer MyPlayer { get; set; }=new();
        public static List<NetworkPlayer> AllPlayers=new();
        public static List<NetworkPlayer> Participants => AllPlayers.Where(p=>p.IsParticipant).ToList();
        public static List<object> ConnectingUsers=new(); public static PlayersChangedEvent? OnPlayerLeft;
        public static NetworkPlayer? GetPlayer(int id) => AllPlayers.FirstOrDefault(p=>p.PlayerID==id);
    }
    internal static class FFSNetwork
    {
        public static bool IsOnline=true,IsClient; public static bool IsHost => IsOnline&&!IsClient;
        public static int Desyncs; public static void HandleDesync(Exception error) => Desyncs++;
    }
    internal sealed class ControllableStateRevisionToken : Photon.Bolt.IProtocolToken { public ControllableStateRevisionToken(NetworkPlayer player) { } }
    internal sealed class ReadyUpToken : Photon.Bolt.IProtocolToken { public string ToggleState; public ReadyUpToken(string state) => ToggleState=state; }
    internal sealed class GameAction { public int TargetPhaseID,PlayerID,ActionTypeID,SupplementaryDataIDMax; public bool SupplementaryDataBoolean; public Photon.Bolt.IProtocolToken? SupplementaryDataToken,SupplementaryDataToken2; }
    internal static class ActionProcessor { public static ActionPhaseType CurrentPhase=ActionPhaseType.MapHQ; public static List<GameAction> ActionQueue=new(); }
    internal static class Console { public static void LogInfo(string text) { } public static void LogWarning(string text) { } }
    internal static class Synchronizer
    {
        public readonly record struct Request(GameActionType Type,bool AutoValidate,int PlayerId,Photon.Bolt.IProtocolToken? Token2);
        public static readonly List<Request> Actions=new();
        public static void SendGameAction(GameActionType actionType,ActionPhaseType targetPhaseType,
            bool validateOnServerBeforeExecuting=false,bool disableAutoReplication=false,int actorID=0,
            int supplementaryDataIDMin=0,int supplementaryDataIDMed=0,int supplementaryDataIDMax=0,
            bool supplementaryDataBoolean=false,Guid supplementaryDataGuid=default,
            Photon.Bolt.IProtocolToken? supplementaryDataToken=null,Photon.Bolt.IProtocolToken? supplementaryDataToken2=null)
            => Actions.Add(new(actionType,supplementaryDataBoolean,PlayerRegistry.MyPlayer.PlayerID,supplementaryDataToken2));
    }
}
namespace MEC
{
    internal readonly record struct CoroutineHandle(int Value);
    internal static class Timing
    {
        private static int _next; private static readonly Dictionary<int,IEnumerator<float>> Running=new();
        public static int Starts;
        public static CoroutineHandle RunCoroutine(IEnumerator<float> routine)
        {
            var handle=new CoroutineHandle(++_next); Starts++;
            // MEC begins the iterator synchronously; the original native reset must kill the
            // predecessor handle before the new handle assignment lands on its toggle.
            if (routine.MoveNext()) Running[handle.Value]=routine;
            return handle;
        }
        public static void KillCoroutines(CoroutineHandle handle) => Running.Remove(handle.Value);
        public static void KillCoroutines(string tag) { }
        public static float WaitForSeconds(float seconds) => seconds;
        public static void Advance()
        {
            foreach (var pair in Running.ToArray()) if (!pair.Value.MoveNext()) Running.Remove(pair.Key);
        }
        public static void Reset() { Running.Clear(); Starts=0; }
    }
}
internal static class NativeCollectionExtensions { public static bool IsNullOrEmpty<T>(this IEnumerable<T>? items) => items==null||!items.Any(); }
internal sealed class Timekeeper { public static readonly Timekeeper instance=new(); public readonly Clock m_GlobalClock=new(); internal sealed class Clock { public float time; } }
internal static class LogUtils { public static void Log(string text) { } public static void LogError(string text) { } }
internal static class InputManager { public static bool GamePadInUse => false; }
internal sealed class TextGraphic { public void CrossFadeColor(Color color,float time,bool ignoreTimeScale,bool useAlpha) { } }
internal sealed class UIInfoTools { public static readonly UIInfoTools Instance=new(); public Color White,greyedOutTextColor; }
internal static class UIUtility { public static void BringToFront(GameObject go) { } }
internal static class Singleton<T> where T:class { public static T Instance=null!; public static bool IsInitialized => Instance!=null; }
internal enum EReadyUpToggleStates { NotSet,Quests,CityEvents,Reward }
internal sealed partial class UIReadyToggle
{
    public enum EReadyUpType { Participant,Player }
    public EReadyUpToggleStates readyUpToggleState; private EReadyUpType readyUpType;
    private bool _requestVisible,_interactable,_allPlayersReady,_isOn;
    private readonly HashSet<object> _visibilityRequests=new();
    public readonly UIWindow window=new(); public readonly Toggle toggle=new();
    private readonly TextGraphic _textGraphic=new();
    private readonly Progress _progressBar=new();
    private UnityAction? onReady,onUnready,onAllPlayersReady;
    private UnityAction<NetworkPlayer,bool>? onReadiedPlayersChanged;
    private Action? onShortPressed; private Func<bool>? canReadyUp;
    private string? readyTextLoc,unreadyTextLoc,readyAudioItem,controllerAreaAllowed;
    private UnityAction<bool>? onStartProgress,onEndProgress; private bool validateReadyUpOnPlayerLeft;
    private readonly List<NetworkPlayer> playersAwaited=new();
    private CoroutineHandle stateSyncCheckRoutine;
    private static readonly float globalStateSyncTimeoutDuration=30f,stateSyncCheckInterval=.2f;
    public List<NetworkPlayer> PlayersReady { get; }=new();
    public Dictionary<NetworkPlayer,EReadyUpToggleStates> PlayerReadiedState { get; }=new();
    public bool IsVisible => window.IsOpen; public bool ToggledOn => _isOn; public EReadyUpType ReadyUpType => readyUpType;
    public bool IsInteractable => _interactable; public bool CanBeToggled { get; set; }=true;
    public bool IsProgressingBar => _progressBar.gameObject.activeInHierarchy;
    private bool _useProgressConfirm=true,_useProgressCancel;
    private sealed class Progress
    {
        public readonly GameObject gameObject=new(); public Action? Complete;
        public void SetAmount(float a,float b) { }
        public void PlayProgressTo(float value,string text,Action done) => Complete=done;
    }
    private void SetIsOnWithoutNotify(bool isOn) => _isOn=isOn;
    // Only Harmony delivery is adapted: every native InputToggle/animation callback executes
    // unchanged and reaches the original two-argument ReadyUp with the production ref argument.
    public void ReadyUp(bool value)
    {
        object[] args={this,value,false}; DepartureHooks.Call(typeof(MapQuestDepartureValidation.ReadyUpSeam),"BeforeReadyUp",args);
        ReadyUp(value,(bool)args[2]);
    }
    public void SetLocalReadySnapshot(bool ready) => SetIsOnWithoutNotify(ready);
    public void EnableCancelProgress(bool value) => _useProgressCancel=value;
    public void ExplicitInput(bool value)
    {
        object[] args={this,value,false}; DepartureHooks.Call(typeof(MapQuestDepartureValidation.ExplicitInputSeam),"BeforeExplicitInput",args);
        Exception? error=null;
        try { typeof(UIReadyToggle).GetMethod("InputToggle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(this,new object[] { value }); }
        catch (System.Reflection.TargetInvocationException e) { error=e.InnerException; throw; }
        finally { DepartureHooks.Call(typeof(MapQuestDepartureValidation.ExplicitInputSeam),"AfterExplicitInput",new object?[] { this,args[2],error }); }
    }
    public void CompleteProgress(bool value=false)
    {
        object[] args={this,value,false}; DepartureHooks.Call(typeof(MapQuestDepartureValidation.ProgressEndSeam),"BeforeProgressEnd",args);
        try { _progressBar.Complete?.Invoke(); }
        finally { DepartureHooks.Call(typeof(MapQuestDepartureValidation.ProgressEndSeam),"AfterProgressEnd",new object?[] { args[2],null }); }
    }
    public void AbortProgress()
    {
        DepartureHooks.Call(typeof(MapQuestDepartureValidation.CancelProgressSeam),"BeforeCancelProgress",new object[] { this }); CancelProgress();
    }
    public void NativeReset()
    {
        Reset(); DepartureHooks.Call(typeof(MapQuestDepartureValidation.ResetSeam),"AfterReset",new object[] { this });
    }
}
internal static class LocalizationManager { public static string GetTranslation(string key) => key; }
internal static class DepartureHooks
{
    public static bool RealInstalled;
    public static object? Call(Type seam,string method,object?[] args) => RealInstalled ? null : seam.GetMethod(method,System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,args);
}
internal sealed class NativeQuest { public string ID="Quest_A"; }
internal sealed class UIMapMultiplayerController { public NativeQuest? HostSelectedQuest=new(); }
namespace GloomhavenVR.WorldUI.MapRoom { internal static class StoryComposite { public static bool PointOfNoReturn; } }
#if REAL_HARMONY
namespace System.Runtime.CompilerServices { internal sealed class IsExternalInit { } }
#endif
