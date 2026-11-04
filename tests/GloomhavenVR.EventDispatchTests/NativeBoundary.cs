// Native type/data and logging ports only. Actual event bus, mode composition,
// observer isolation and diagnostic throttling are compiled from production.
using System;
using System.Collections.Generic;

namespace Code.State { public interface IState { } }
namespace ScenarioRuleLibrary
{
    public class CActor { public int ID; }
    public class CPlayerActor : CActor { }
    public class CMessageData
    {
        public enum MessageType { PlayerToSelectAbilityCardsOrLongRest,
            PlayersHaveSelectedAbilityCardsOrLongRest, StartTurn, ActionSelection,
            ActionSelectionPhaseStart, EndTurn, EndRound, NextRound,
            PlayerLongRested, PlayerShortRested }
        public MessageType m_Type;
    }
    public class CPhase { public enum PhaseType { Action, EndTurn, EndRound } }
    public static class PhaseManager { public static CPhase.PhaseType PhaseType; }
}
namespace UnityEngine
{
    public static class Time { public static float unscaledTime; }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { }
    public enum LoadSceneMode { Single, Additive }
}
namespace UnityEngine.UI
{
    public class UIWindow { public UIWindowID ID; }
}
public enum UIWindowID { None }
public enum CardHandMode { CardsSelection }
public class AbilityCardUI { }
public class ShortRest { }
public class Choreographer
{
    public static Choreographer s_Choreographer;
    public enum ChoreographerStateType { Play, WaitingForPlayerWaypointSelection,
        WaitingForAreaAttackFocusSelection, WaitingForPlayerPushWaypointSelection,
        WaitingForPlayerPullWaypointSelection, WaitingForTileSelected,
        WaitingForEndAbilityAnimSync, WaitingForEndTurnSync, WaitingForEndRoundSync }
    public class CWaitState
    {
        public ChoreographerStateType m_State;
        public ScenarioRuleLibrary.CActor m_StateWaitActor;
    }
    public CWaitState m_WaitState;
    public bool m_EndAbilitySyncComplete, m_EndOfTurnSyncComplete, m_EndOfRoundSyncComplete;
}
public static class FFSNetwork { public static bool IsOnline; }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static readonly List<string> Errors = new();
        internal static readonly List<string> Alerts = new(), Infos = new();
        internal static bool FailLog;
        internal static void Error(string scope, string message)
        {
            if (FailLog) throw new InvalidOperationException("diagnostic unavailable");
            Errors.Add(scope + ": " + message);
        }
        internal static void Alert(string scope, string message) => Alerts.Add(message);
        internal static void Info(string scope, string message) => Infos.Add(message);
        internal static void Debug(string scope, string message) { }
        internal static void Note(string scope, string message) { }
    }
    internal static class PerfMonitor
    {
        internal static long BeginStep() => 0;
        internal static void EndStep(string name, long start) { }
    }
    internal static class VRSession { internal static bool IsRunning; }
    internal static class Loc { internal static string Mod(string id) => id; }
}
namespace GloomhavenVR.Core.Events
{
    internal static class MenuWorldContext { internal static bool IsReady => true; }
}
