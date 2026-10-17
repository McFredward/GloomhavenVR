using System;
using System.Collections.Generic;

// This fixture tests the production UI and localization. Its runtime/Unity doubles
// deliberately make no claim about decoding, scene admission, sharing or headset output.
namespace UnityEngine
{
    internal static class Time { internal static float unscaledTime => 0f; }
    internal sealed class Transform { }
    internal sealed class GameObject
    {
        internal readonly TMPro.TMP_Text Label = new();
        internal bool activeSelf = true;
        internal void SetActive(bool active) => activeSelf = active;
        internal T? GetComponentInChildren<T>(bool includeInactive) where T : class => Label as T;
    }
}
namespace BepInEx.Configuration
{
    internal sealed class ConfigEntry<T>
    {
        internal T Value;
        internal ConfigEntry(T value) => Value = value;
    }
    internal sealed class ConfigFile
    {
        internal ConfigEntry<T> Bind<T>(string section, string key, T value, string description) => new(value);
    }
}
internal static class FFSNetwork { internal static bool IsOnline; }
namespace TMPro
{
    internal sealed class TMP_Text
    {
        private string _text = "";
        internal int Writes;
        internal bool Destroyed;
        internal string text
        {
            get => _text;
            set
            {
                if (Destroyed) throw new Exception("Destroyed status label was repainted");
                Writes++;
                _text = value;
            }
        }
    }
}
namespace GloomhavenVR.Core
{
    internal static partial class Loc
    {
        internal static string CurrentLanguage = "English";
        internal static string Mod(string key) => key;
        internal static string Game(string key, string fallback) =>
            key == "NATIVE_CLASS_BEAST_TYRANT" ? "Native " + CurrentLanguage : fallback;
    }
    internal static class ModuleConfig
    {
        internal static string LastModule = "";
        internal static BepInEx.Configuration.ConfigFile Create(string module)
        { LastModule = module; return new(); }
    }
    internal static class VRLog
    {
        internal static void Info(string area, string text) { }
        internal static void Note(string area, string text) { }
        internal static void Warn(string area, string text) { }
    }
    internal static class TickGuard
    {
        internal static void Run(string key, Action action, string area) => action();
    }
    internal static class ElementMood
    {
        internal static bool Forcing;
        internal static void ClearForce(string reason) => Forcing = false;
    }
    internal static class Haunt
    {
        internal static bool Forcing;
        internal static void ClearForce(string reason) => Forcing = false;
    }
}
namespace ScenarioRuleLibrary
{
    internal enum ECharacter { BeastTyrant, Brute }
    internal sealed class CCharacterClass
    {
        internal ECharacter CharacterModel;
        internal string LocKey = "";
    }
    internal static class CharacterClassManager
    {
        internal static readonly List<CCharacterClass> Classes = new();
    }
}
namespace GloomhavenVR.Net
{
    internal static class RemoteTestTriggers
    {
        internal static int Presses;
        internal static void NoteLocalPress(string reason) => Presses++;
    }
}
namespace GloomhavenVR.WorldUI
{
    internal enum NativeVideoReviewStatus { Idle, Preparing, Playing, Closed, Failed, Busy }
    internal enum NativeVideoReviewFailure
    {
        None, InvalidContext, NativeMovieActive, RemoteMovieActive, AlreadyActive,
        MediaUnavailable, InvalidMovie, DecoderError,
    }
    internal static class NativeVideoReview
    {
        internal static string[] Keys = Array.Empty<string>();
        internal static int Count => Keys.Length;
        internal static string GetKey(int index) => Keys[index];
        internal static NativeVideoReviewStatus Status;
        internal static NativeVideoReviewFailure LastFailure;
        internal static string LastKey = "";
        internal static bool Active;
        internal static bool ObservingReview, ObservingNativeMovie;
        internal static bool Context = true;
        internal static int Starts, Stops, CloseRequests, ObserverCloseRequests;
        internal static readonly List<string> Requests = new();
        internal static bool TryStart(string key)
        {
            Requests.Add(key);
            if (!Context || Active || ObservingNativeMovie)
            {
                LastFailure = Active ? NativeVideoReviewFailure.AlreadyActive
                    : ObservingNativeMovie ? NativeVideoReviewFailure.RemoteMovieActive : NativeVideoReviewFailure.InvalidContext;
                if (!Active) Status = NativeVideoReviewStatus.Busy;
                return false;
            }
            Starts++;
            LastKey = key;
            Active = true;
            Status = NativeVideoReviewStatus.Preparing;
            LastFailure = NativeVideoReviewFailure.None;
            return true;
        }
        internal static void Stop()
        {
            if (!Active) return;
            Stops++;
            Active = false;
            Status = NativeVideoReviewStatus.Closed;
            LastFailure = NativeVideoReviewFailure.None;
        }
        internal static void RequestClose()
        {
            CloseRequests++;
            if (Active) Stop();
            else if (ObservingReview)
            {
                // Observe only the GUI intent at the API boundary. Real network
                // closure/election is covered by the separate runtime fixture.
                ObserverCloseRequests++;
                ObservingReview = false;
                Status = NativeVideoReviewStatus.Closed;
                LastFailure = NativeVideoReviewFailure.None;
            }
        }
        internal static void Reset()
        {
            Status = NativeVideoReviewStatus.Idle;
            LastFailure = NativeVideoReviewFailure.None;
            LastKey = "";
            Active = ObservingReview = ObservingNativeMovie = false;
            Context = true;
            Starts = Stops = CloseRequests = ObserverCloseRequests = 0;
            Requests.Clear();
        }
    }
    internal static partial class VROptionsTab
    {
        internal sealed record ActionRow(string Caption, Action Click, bool IsAction, UnityEngine.GameObject Row);
        internal static readonly List<ActionRow> Actions = new();
        internal static readonly List<string> Notes = new();
        internal static readonly List<TMPro.TMP_Text> Labels = new();
        internal static readonly List<string> Headers = new();
        internal static bool IsOpen = true;
        internal static int Rebuilds, ReservedLines;
        private static UnityEngine.Transform? ContentRoot = new();
        private static readonly List<object> LatchLabels = new();
        private enum View { AdvancedIndex, AdvancedCheats }
        private static View _view;
        private static void Rebuild() { _ = _view; Rebuilds++; }
        private static int BuildElementTriggers(bool waning, string headerKey) => 0;
        private static int BuildHauntTriggers() => 0;
        private static void RefreshLatchRows() { }
        // Save/gameplay row builders are explicit ports, never counterfeit save operations.
        // Their unchanged native refusals run in the separate scenario-win-cheat fixture.
        internal static readonly List<bool> SaveRowOnline = new();
        private static object? _winArmedScenario, _goldLastResult;
        private static int SaveRow(string name, bool online)
        {
            SaveRowOnline.Add(online);
            BuildLinkRow(ContentRoot!, name, () => { }, asAction: true);
            return 1;
        }
        private static int BuildUnlockAllLevelsRow(bool online) => SaveRow("unlock boundary", online);
        private static int BuildScenarioGateRow(bool online) => SaveRow("scenario gate boundary", online);
        private static int BuildOpenAllDoorsRow(bool online) => SaveRow("doors boundary", online);
        private static int BuildWinScenarioRow() => SaveRow("win boundary", SessionOnline());
        private static int BuildGoldRow() => SaveRow("gold boundary", SessionOnline());
        private static int BuildAdvancedIndex() { Headers.Add("advanced index boundary"); return 1; }
        private static void BuildHeader(UnityEngine.Transform parent, string caption, string? hintKey = null,
                                        bool sub = false) => Headers.Add(caption);
        private static UnityEngine.GameObject BuildNote(UnityEngine.Transform parent, string text,
                                                        bool multiline = false, int minLines = 1)
        {
            Notes.Add(text);
            var row = new UnityEngine.GameObject();
            row.Label.text = text;
            Labels.Add(row.Label);
            ReservedLines = Math.Max(ReservedLines, minLines);
            return row;
        }
        private static UnityEngine.GameObject BuildLinkRow(UnityEngine.Transform parent, string caption,
                                                          Action onClick, bool asAction = false)
        {
            var row = new UnityEngine.GameObject();
            Actions.Add(new ActionRow(caption, onClick, asAction, row));
            return row;
        }
        private static void BeginBuild(bool noRoot)
        {
            ClearVideoReviewRows();
            Actions.Clear(); Notes.Clear(); Labels.Clear(); Headers.Clear();
            ReservedLines = Rebuilds = 0;
            SaveRowOnline.Clear();
            ContentRoot = noRoot ? null : new UnityEngine.Transform();
            _ = _winArmedScenario; _ = _goldLastResult;
        }
        internal static void Build(bool noRoot = false)
        {
            BeginBuild(noRoot);
            BuildCheatsPage();
        }
        internal static void BuildPublic()
        {
            BeginBuild(false);
            BuildTestTriggers();
        }
        internal static int BuildDirectReview() { BeginBuild(false); return BuildVideoReviewTriggers(); }
        internal static int BuildCheatsDoor() { BeginBuild(false); return BuildCheatsIndexLink(); }
        internal static bool Cheats
        {
            get => CheatsAvailable;
            set { BindCheatsConfig(); _cheatsEnabled!.Value = value; }
        }
        internal static void Refresh() => RefreshVideoReviewRows();
        internal static void Clear() => ClearVideoReviewRows();
        internal static string Caption => _videoReviewStatus?.text ?? "";
        internal static TMPro.TMP_Text? StatusLabel => _videoReviewStatus;
    }
}
