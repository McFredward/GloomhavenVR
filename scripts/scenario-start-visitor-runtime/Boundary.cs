#pragma warning disable CS0649
// Declared native model, clock and socket ports; original dispatch methods are imported unchanged.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.WorldUI;
using MapRuleLibrary.State;
using ScenarioRuleLibrary;

internal static class FFSNetwork
{
    public static bool IsOnline = true, IsHost = true, HasDesynchronized; public static bool IsClient => IsOnline && !IsHost;
    public static NativeManager Manager = new(); public static Exception? LastDesync;
    public static void HandleDesync(Exception e) { LastDesync = e; HasDesynchronized = true; }
}
internal sealed class NativeManager { internal bool DisplayControllableNameForIncorrectActionErrors; internal int IncorrectActionDetectedTimeOutDuration = 60; }
internal static class Singleton<T> where T : class { internal static T Instance = null!; internal static bool IsInitialized => Instance != null; }
internal sealed partial class StoryController
{
 internal class DialogInfo
 { internal CLevelMessage? LevelMsg; internal MapMessage? MapMsg; internal List<DialogLineDTO>? DialogPages; internal Action? OnClosedAction; internal bool HideOtherGUI=true; }
 internal static bool DisplayDelayInEffect;
 internal readonly UnityEngine.UI.UIWindow window=new(); internal readonly UICharacterStoryBox dialogBox=new();
 internal DialogInfo? m_CurrentMessage; private readonly Queue<DialogInfo> m_PendingMessages=new();
 private bool isBlockedUpdate; private object _stateFilter=new(); internal bool IsVisible=>window.IsVisible;
 internal void ShowOtherGUI(bool show) { }
 internal void Begin(DialogInfo message) { m_PendingMessages.Enqueue(message); ShowImmediately(message); }
 internal void Complete() => OnFinishShow();
 internal void Queue(DialogInfo message) => m_PendingMessages.Enqueue(message);
}
internal sealed class MapChoreographer { internal int Refreshes; internal bool LastSave = true; internal void RefreshAllMapLocations(Action? onFinish = null,bool save = true) { ++Refreshes; LastSave=save; } }
internal sealed class BlessingToken { internal string CharacterID = "Brute"; }
internal sealed partial class Choreographer
{
 internal static Choreographer? s_Choreographer=new(); internal bool isActiveAndEnabled=true;
 internal readonly Queue<object> m_MessageQueue=new(); internal int Blockers, NativeWins; internal bool IsRestarting;
 internal List<ScenarioAchievementProgress> m_InitialProgressAchievements=new();
 internal ReadyButton readyButton=new(); internal ReadyButton m_SkipButton=new();
 internal void AddUpdateBlocker()=>++Blockers; internal void RemoveUpdateBlocker()=>--Blockers;
 internal void SetActiveSelectButton(bool activate) { } internal void LogScenarioResult(SEventActorFinishedScenario.EScenarioResult result) { }
 internal void EndGame(EResult result) { } internal void StartCoroutine(IEnumerator routine) { ++NativeWins; NativeClock.Run(routine); }
}
internal sealed class SceneController
{
 internal static SceneController? Instance=new(); internal bool IsLoading,ScenarioIsLoading,GameLoadedAndClientReady;
 internal int SafeEnds; internal ErrorMessage GlobalErrorMessage=new();
 internal IEnumerator EndScenarioSafely() { if(Choreographer.s_Choreographer!.Blockers!=0)throw new Exception("native story blocker remains"); ++SafeEnds; yield return null; }
}
internal static class DebugMenu
{
    internal static int Reveals, UnlockRefresh;
    internal static void RevealAllRooms() { ++Reveals; ScenarioManager.CurrentScenarioState!.Revealed = true; }
    internal static void ShowAllScenariosNoToggle() => ++UnlockRefresh;
}
internal sealed class SaveData
{
    internal GlobalData Global = new();
    internal static SaveData Instance = new(); internal int Saves; internal bool ThrowSave;
    internal void SaveCurrentAdventureData() { if(ThrowSave)throw new System.IO.IOException("fixture save failure"); ++Saves; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog { internal static void Debug(string c,string m) { } internal static bool Throw; internal static void Note(string c, string m) { if(Throw)throw new Exception("fixture logger failure"); } internal static void Info(string c, string m) { } internal static void Warn(string c, string m) { } internal static void Error(string c, string m) { } }
}
namespace GloomhavenVR.Net
{
    internal static class NetSession { internal static bool FlatNetMode; }
    internal static partial class NetProtocol { internal const ushort ModBuild=763; }
    internal static class NetPlayerActors { internal static int LocalPlayerId()=>FFSNet.PlayerRegistry.MyPlayer?.PlayerID??0; }
    internal static partial class RemoteStorySync { }
    namespace Desync { internal static class DispatchGuard { internal static void Run(string label,Action action)=>action(); } }
    internal static partial class VersionGuard
    {
        internal static int SessionEpoch; internal static readonly Dictionary<int,int> Versions=new();
        internal static int PeerBuild(int id)=>Versions.TryGetValue(id,out int v)?v:0;
        internal static void CollectContinuationPeers(List<int> into,int local) { into.Clear(); foreach(var p in FFSNet.PlayerRegistry.AllPlayers)if(p.PlayerID!=local&&PeerBuild(p.PlayerID)==NetProtocol.ModBuild)into.Add(p.PlayerID); }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class MultiplayerCheats { internal const int NativeAction=2; }
    internal static class VROptionsTab
    { internal static bool CheatsEnabledNow = true; internal static int Backups; internal static bool ThrowBackup; internal static string BackUpAdventureSave() { if(ThrowBackup)throw new System.IO.IOException("fixture backup failure"); ++Backups; return "fixture"; } }
}
namespace MapRuleLibrary.YML.Quest { internal enum EQuestType { City, Job } }
namespace MapRuleLibrary.MapState
{
    internal sealed class CQuestState
    {
        internal enum EQuestState { Locked, Unlocked, Blocked }
        internal string ID = "Quest"; internal EQuestState QuestState; internal QuestData Quest = new(); internal ScenarioStamp? ScenarioState=new();
        internal void LockQuest() => QuestState = EQuestState.Locked;
        internal void UnlockQuest() { if (QuestState == EQuestState.Locked) QuestState = EQuestState.Unlocked; }
    }
    internal sealed class QuestData { internal MapRuleLibrary.YML.Quest.EQuestType Type; }
}
namespace MapRuleLibrary.Party
{
    internal enum EGoldMode { CharacterGold, PartyGold }
    internal sealed class CMapCharacter
    {
        internal string CharacterID = "Brute", CharacterName = "Alice"; internal int Gold;
        internal void ModifyGold(int amount, bool useGoldModifier) { Calls.OnGold?.Invoke(); if (useGoldModifier) throw new Exception("wrong modifier"); Gold += amount; }
    }
    internal sealed class CMapParty
    {
        internal int ProsperityXP; internal static int CalculateProsperityLevel(int xp)=>xp/100;
        internal readonly List<CMapCharacter> CheckCharacters = new() { new(), new() { CharacterName = "Bob" } };
        internal int PartyGold;
        internal void ModifyPartyGold(int amount, bool useGoldModifier) { if (useGoldModifier) throw new Exception("wrong modifier"); PartyGold += amount; }
    }
}
namespace MapRuleLibrary.State
{
    internal sealed class CMapState
    {
        internal int Seed = 77, DLLMode = 1; internal MapRuleLibrary.Party.EGoldMode GoldMode;
        internal MapRuleLibrary.Party.CMapParty MapParty = new();
        internal List<MapRuleLibrary.MapState.CQuestState> AllQuestStates => AllQuests;
        internal List<MapRuleLibrary.MapState.CQuestState> AllQuests = new()
        {
            new() { QuestState = MapRuleLibrary.MapState.CQuestState.EQuestState.Locked },
            new() { QuestState = MapRuleLibrary.MapState.CQuestState.EQuestState.Blocked },
            new() { QuestState = MapRuleLibrary.MapState.CQuestState.EQuestState.Locked,
                    Quest = new() { Type = MapRuleLibrary.YML.Quest.EQuestType.Job } }
        };
    }
}
namespace MapRuleLibrary.Adventure { internal static class AdventureState { internal static CMapState MapState = new(); } }
namespace ScenarioRuleLibrary
{
    internal sealed class ScenarioState { internal string ID = "Scenario4"; internal int Seed = 55; internal bool Revealed; }
    internal static class GameState { internal static bool WaitingForPlayerToSelectDamageResponse, WaitingForPlayerActorToAvoidDamageResponse; }
    internal static class ScenarioRuleClient { internal static bool IsProcessingOrMessagesQueued,ScenarioRuleClientStopped; }
    internal static class ScenarioManager { internal static ScenarioState? CurrentScenarioState; internal static CScenario? Scenario = new(); }
    internal sealed class CScenario { internal SEventActorFinishedScenario.EScenarioResult CurrentScenarioResult; }
    internal static class SEventActorFinishedScenario { internal enum EScenarioResult { None, Win, Lose } }
}
namespace FFSNet
{
    

    internal sealed class NetworkPlayer
    {
        private int _id; public int PlayerID { get { ++Calls.NativePlayerReads;return _id; } set=>_id=value; } public bool IsActive = true; public bool IsClient => PlayerID > 1; public NativeState state = new(); public string Username = "Player";
        public Stats NetStats = new(); internal List<GameAction> Inbox = new();
    }
    internal sealed class Stats { public object Connection = new(); }
    internal sealed class NativeState { public int LatestProcessedActionID; }
    internal static class PlayerRegistry
    {
        public static readonly int HostPlayerID = 1;
        public static List<NetworkPlayer> AllPlayers = new();
        public static NetworkPlayer MyPlayer { get; set; } = null!;
        public static NetworkPlayer HostPlayer { get; set; } = null!;
        public static List<object> JoiningPlayers { get; set; } = new();
        public static List<object> ConnectingUsers { get; set; } = new();
        public static bool OtherClientsAreJoining, LoadingInFromJoiningClient, IsSwitchingCharacter;
        public static int SwitchingPlayerID = 1, SwitchingCharacterSlot;
        public static List<NetworkPlayer> Participants => AllPlayers;
        public static NetworkPlayer? GetPlayer(int id) => AllPlayers.FirstOrDefault(x => x.PlayerID == id);
    }
    internal delegate void GameActionDelegate(GameAction a, ref bool valid, ref bool finished);
    internal sealed partial class GameAction
    {
        private bool isValid = true;
        private static readonly Dictionary<GameActionType, GameActionDelegate> actions = new()
        { [GameActionType.DebugWinScenario]=(GameAction _,ref bool v,ref bool f)=>Choreographer.s_Choreographer!.ClientDebugWinScenario(),
          [GameActionType.PingHex] = (GameAction _, ref bool v, ref bool f) => { },
          [GameActionType.ConfirmAction] = (GameAction _,ref bool v,ref bool f) => { } };
        internal int ActionID, ActionTypeID, PlayerID, ActorID, TargetPhaseID, TargetPlayerID;
        internal int DataInt, SupplementaryDataIDMin, SupplementaryDataIDMed, SupplementaryDataIDMax;
        internal bool BinaryDataIncludesLoggingDetails; internal object? SupplementaryDataToken;
        internal bool IsValid => isValid;
        internal bool ExecuteLocallyFirst, DoNotForwardAction, ValidateAction = true, SyncViaStateUpdate;
        internal byte[] CustomBinaryData = Array.Empty<byte>();
        public bool Execute() => ExecutePatched();
        internal bool ExecutePatched()=>NativeExecute();
    }
    internal static partial class ActionProcessor
    {
        private static ProcessorState currentState = new(ActionProcessorStateType.ProcessFreely,ActionPhaseType.MapHQ),
            previousState = new(ActionProcessorStateType.ProcessFreely,ActionPhaseType.MapHQ), savedState;
        internal static ProcessorState CurrentState => currentState;
        internal static bool IsProcessing => processingAction;
        private static bool processingAction, lockProcessingAction;
        internal static bool Locked { get => lockProcessingAction; set => lockProcessingAction=value; }
        private static bool readyToProcessNextAction = true;
        private static int incorrectActionsDetectedCounter;
        internal static int MaxConsecutiveIncorrectActionsAllowed = 60;
        internal static bool ReadyToProcessNextAction { get => readyToProcessNextAction; set => readyToProcessNextAction = value; }
        internal static ActionPhaseType CurrentPhase { get => currentState.PhaseType; set => currentState=new ProcessorState(currentState.StateType,value); }
        private static Queue<GameAction> actionQueue = new();
        internal static Queue<GameAction> ActionQueue => actionQueue;
        internal static float QueueDelay;
        internal static void LogAction(GameAction a) => Calls.Trace.Add("native-log:" + a.ActionID);

        private static void StartUpActionProcessing(bool immediate) { }
        private static IEnumerator QueueUpActionAfterDelay(GameAction a, bool immediate) { yield break; }
        internal static bool DrainOne()=>TryProcessNextAction();
        internal static void LockProcessingAction()=>lockProcessingAction=true;
        internal static void UnlockProcessingAction()=>lockProcessingAction=false;
    }
    internal static class ActionProcessorCoroutineHelper { internal static void RunCoroutine(IEnumerator e) { } }
    internal static class Console
    { internal static void LogInfo(string m) { } internal static void LogError(string code,string m) { } internal static void LogWarning(string m) { } internal static void LogCoreInfo(string m, bool customFlag = false) { } }
    internal static class ControllableRegistry { internal static List<NetworkControllable> AllControllables=new(){new()}; internal static NetworkControllable GetControllable(int id) => new(); }
    internal sealed class NativeActor { internal string GetName() => "Actor"; }
    internal sealed class NetworkControllable { internal NativeActor ControllableObject = new(); internal void UpdateState(GameAction a) => throw new Exception("unexpected state route"); internal void ApplyState()=>++Calls.ApplyState; }
    internal static partial class Synchronizer
    {
        internal static GameAction? Last;
        internal static int ClientReady;
        internal static void SendSideAction(GameActionType type,object? data,bool canBeUnreliable,bool sendToHostOnly) { if(type==GameActionType.GameLoadedAndClientReady&&!canBeUnreliable&&sendToHostOnly)++ClientReady; }
        private static void ReplicateAction(GameActionType actionType, ActionPhaseType targetPhaseType, bool syncViaStateUpdate,
            bool executeLocallyFirst, bool validateOnServerBeforeExecuting, bool disableAutoReplication,
            int actorID, int min, int med, int max, bool boolean, Guid guid, object? t1, object? t2,
            object? t3, object? t4, byte[]? data, bool details, string? classID)
        {
            Calls.Trace.Add("native-queue");
            Last = new GameAction { ActionTypeID = (int)actionType, PlayerID = PlayerRegistry.MyPlayer.PlayerID,
                ActorID = actorID, TargetPhaseID = (int)targetPhaseType, ExecuteLocallyFirst = executeLocallyFirst,
                ValidateAction = validateOnServerBeforeExecuting, DoNotForwardAction = disableAutoReplication,
                SyncViaStateUpdate = syncViaStateUpdate, CustomBinaryData = data! };
            if (!executeLocallyFirst || !validateOnServerBeforeExecuting || syncViaStateUpdate || targetPhaseType != ActionPhaseType.NONE)
                throw new Exception("wrong original host queue flags");
            ActionProcessor.QueueUpAction(Last);
        }
        private static void SendGameActionToPlayer(GameAction action, NetworkPlayer targetPlayer)
        {
            // Explicit socket/event port. Original meta separately proves7bit type and
            // Bolt's length-prefixed BinaryData attachment; no real network is claimed.
            if (action.ActionTypeID is < 0 or > 127 || action.CustomBinaryData.Length > 600) throw new Exception("native event bound");
            Calls.Trace.Add("native-forward:" + targetPlayer.PlayerID);
            targetPlayer.Inbox.Add(new GameAction { ActionID = action.ActionID, ActionTypeID = action.ActionTypeID,
                PlayerID = action.PlayerID, ActorID = action.ActorID, TargetPhaseID = action.TargetPhaseID,
                ValidateAction = action.ValidateAction, CustomBinaryData = (byte[])action.CustomBinaryData.Clone() });
        }
    }
}
internal static class Calls { internal static Action? OnGold; internal static readonly List<string> Trace = new(); internal static int ApplyState,NativePlayerReads; }

internal static class Debug { internal static void Log(string message) { } internal static void LogError(string message)=>throw new Exception(message); }
internal static class NativeStringExtensions { internal static bool IsNullOrEmpty(this string? value) => string.IsNullOrEmpty(value); }

internal sealed class RequirementCheckResult
{
    internal enum StartingLocatinState { Valid, Missing }
    internal List<string> missingRequiredCharacters = new() { "Brute" }, charactersWithoutLevel = new() { "Brute" },
        missingRequiredItems = new() { "Boots" }, missingRequiredPersonalQuests = new() { "Goal" };
    internal int missingAmountCharacters = 2; internal StartingLocatinState startingLocationState = StartingLocatinState.Missing;
    internal bool IsQuestStateLocked = true;
}

internal sealed class CampaignPersonalQuestData { internal string CharacterID = "Brute", CharacterName = "Alice", PersonalQuestID = "Quest"; }

internal static class NativeEnumExtensions { internal static bool In<T>(this T item,params T[] values) => values.Contains(item); }
