using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Compat;
using ScenarioRuleLibrary.CustomLevels;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new Exception(reason); }

    private static void Main()
    {
        // Deliberately nonnumeric: the native menu order, not a guessed filename, is authoritative.
        var first = new Tutorial("intro", "Tutorial_2");
        var later = new Tutorial("advanced", "Tutorial_1");
        var global = new GlobalData { GameMode = EGameMode.FrontEndTutorial,
            CurrentFrontEndTutorialID = first.TutorialID, CurrentFrontEndTutorialFilename = first.TutorialFileName };
        SaveData.Instance = new SaveData { Global = global };
        LevelEventsController.s_EventsControllerActive = true;
        TutorialVR.Enabled = true;
        TutorialLessonScope.CaptureFirstTutorial(null);
        Check(!TutorialLessonScope.IsActive, "Unknown menu identity must not inject a lesson");
        var service = new TutorialService { Tutorials = new List<ITutorial> { first, later } };
        Capture(service);
        Check(TutorialLessonScope.IsActive, "First native tutorial must retain the VR lesson");
        global.CurrentFrontEndTutorialID = later.TutorialID;
        global.CurrentFrontEndTutorialFilename = later.TutorialFileName;
        Check(!TutorialLessonScope.IsActive, "Later tutorial must not receive extra VR steps");
        global.CurrentFrontEndTutorialFilename = first.TutorialFileName;
        Check(!TutorialLessonScope.IsActive, "Different tutorial ID must not reuse the first file's admission");
        global.CurrentFrontEndTutorialFilename = later.TutorialFileName;
        global.CurrentFrontEndTutorialID = first.TutorialID;
        Check(!TutorialLessonScope.IsActive, "Stale first ID with another file must not admit a lesson");
        global.CurrentFrontEndTutorialFilename = first.TutorialFileName;
        Check(TutorialLessonScope.IsActive, "Returning to the first tutorial must retain its lesson");
        service.Tutorials.Clear();
        Check(TutorialLessonScope.IsActive, "Menu unload must not erase the copied first identity");
        foreach (EGameMode mode in Enum.GetValues<EGameMode>())
        {
            global.GameMode = mode;
            Check(TutorialLessonScope.IsActive == (mode == EGameMode.FrontEndTutorial),
                "Other game modes must not inherit stale tutorial identity: " + mode);
        }
        global.GameMode = EGameMode.FrontEndTutorial;
        FFSNetwork.IsOnline = true;
        Check(!TutorialLessonScope.IsActive, "Online sessions must not inject local tutorial steps");
        FFSNetwork.IsOnline = false;
        TutorialVR.Enabled = false;
        Check(!TutorialLessonScope.IsActive, "Disabled tutorial bridge must not admit extra steps");
        TutorialVR.Enabled = true;
        LevelEventsController.s_EventsControllerActive = false;
        Check(!TutorialLessonScope.IsActive, "Inactive scene must not keep a lesson eligible");
        LevelEventsController.s_EventsControllerActive = true;
        SaveData.Instance = null;
        Check(!TutorialLessonScope.IsActive, "Save teardown must safely reject lesson admission");
        SaveData.Instance = new SaveData { Global = null };
        Check(!TutorialLessonScope.IsActive, "Missing global data must safely reject lesson admission");
        SaveData.Instance.Global = global;
        foreach (string? missing in new string?[] { null, "", " " })
        {
            Check(!TutorialLessonScope.MatchesFirstTutorial(false, EGameMode.FrontEndTutorial,
                missing, first.TutorialFileName, missing, first.TutorialFileName),
                "Empty IDs must not authorize an extra lesson");
            Check(!TutorialLessonScope.MatchesFirstTutorial(false, EGameMode.FrontEndTutorial,
                first.TutorialID, missing, first.TutorialID, missing),
                "Empty filenames must not authorize an extra lesson");
        }
        TutorialLessonScope.CaptureFirstTutorial(Array.Empty<ITutorial>());
        Check(!TutorialLessonScope.IsActive, "Empty catalogue must clear prior admission");
        TutorialLessonScope.CaptureFirstTutorial(new ITutorial[] { null!, first });
        Check(!TutorialLessonScope.IsActive, "Invalid first entry must not promote a later entry");
        service.Tutorials = new List<ITutorial> { first, later };
        Capture(service);
        Check(TutorialLessonScope.IsActive, "A valid catalogue must restore first-tutorial eligibility");
        service.Throw = true;
        Capture(service);
        Check(!TutorialLessonScope.IsActive, "Failed catalogue lookup must clear stale admission");
        Check(typeof(TutorialService_StartTutorial_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.ReturnType == typeof(void),
            "Admission hook must not suppress native tutorial startup");
        service.Throw = false;
        Capture(service);
        CheckHoldLifetime(global, first, later);
        Console.WriteLine($"Tutorial scope production tests: {_checks} assertions passed.");
    }

    private static void CheckHoldLifetime(GlobalData global, Tutorial first, Tutorial later)
    {
        typeof(TutorialChainHold).GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
        var owner = new LevelEventsController();
        LevelEventsController.s_Instance = owner;
        global.CurrentFrontEndTutorialID = first.TutorialID;
        global.CurrentFrontEndTutorialFilename = first.TutorialFileName;
        Check(TutorialChainHold.Engage("test", "first tutorial"), "First tutorial must allow its lesson hold");
        var a = new CLevelMessage { MessageName = "a" };
        var b = new CLevelMessage { MessageName = "b" };
        owner.ShowLevelMessage(a);
        owner.ShowLevelMessage(b);
        Check(owner.Shown.Count == 0, "First tutorial must hold messages until its lesson completes");
        foreach (int terminal in new[] { 30, 31 })
        {
            var end = new CLevelMessage { DisplayTrigger = new CLevelTrigger { IsUIEventTypeTrigger = true, EventTriggerTypeInt = terminal } };
            owner.ShowLevelMessage(end);
            Check(owner.Shown.Contains(end), "Scenario outcome must never be held by a VR lesson");
        }
        owner.Shown.Clear();
        TutorialChainHold.Release("other", "unrelated owner");
        Check(TutorialChainHold.Engaged, "Other lesson must not release an existing hold");
        global.CurrentFrontEndTutorialID = later.TutorialID;
        global.CurrentFrontEndTutorialFilename = later.TutorialFileName;
        owner.ShowLevelMessage(new CLevelMessage());
        Check(owner.Shown.Count == 1, "Later tutorial messages must bypass any pending old hold");
        owner.Shown.Clear();
        TutorialChainHold.Release("test", "scope ended before scene teardown");
        Check(owner.Shown.Count == 2 && ReferenceEquals(owner.Shown[0], a) && ReferenceEquals(owner.Shown[1], b),
            "Scope loss must replay pending messages in order while their controller is alive");
        Check(!TutorialChainHold.Engaged, "Scope-loss release must clear the hold");
        Check(!TutorialChainHold.Engage("test", "later tutorial"), "Later tutorial must never engage a lesson hold");
        global.CurrentFrontEndTutorialID = first.TutorialID;
        global.CurrentFrontEndTutorialFilename = first.TutorialFileName;
        Check(TutorialChainHold.Engage("test", "replay first"), "Replayed first tutorial must still be eligible");
        owner.ShowLevelMessage(a);
        var next = new LevelEventsController();
        LevelEventsController.s_Instance = next;
        next.ShowLevelMessage(b);
        Check(next.Shown.Count == 1, "New controller messages must bypass an old controller hold");
        owner.Shown.Clear();
        TutorialChainHold.Release("test", "new controller replaced old");
        Check(owner.Shown.Count == 0 && next.Shown.Count == 1,
            "Stale held messages must not replay into either controller after scenario replacement");
        Check(TutorialChainHold.Engage("test", "new first attempt"), "New first attempt can engage its own hold");
        next.ShowLevelMessage(a);
        TutorialChainHold.Discard("scenario boundary");
        Check(!TutorialChainHold.Engaged && next.Shown.Count == 1, "Scenario reset must discard the old hold without replay");
    }

    private static void Capture(TutorialService service) => typeof(TutorialService_StartTutorial_Patch)
        .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { service });
}

internal sealed class Tutorial : ITutorial
{
    public Tutorial(string id, string filename) { TutorialID = id; TutorialFileName = filename; }
    public string TutorialID { get; }
    public string TutorialFileName { get; }
}
public interface ITutorial { string TutorialID { get; } string TutorialFileName { get; } }
public class TutorialService
{
    public List<ITutorial> Tutorials = new();
    public bool Throw;
    public List<ITutorial> GetTutorials() => Throw ? throw new InvalidOperationException("catalogue unavailable") : Tutorials;
}
public enum EGameMode { FrontEndTutorial, Guildmaster, Campaign, SingleScenario, Autotest }
public class GlobalData
{
    public EGameMode GameMode;
    public string? CurrentFrontEndTutorialID;
    public string? CurrentFrontEndTutorialFilename;
}
public class SaveData { public static SaveData? Instance; public GlobalData? Global; }
public static class FFSNetwork { public static bool IsOnline; }
public class LevelEventsController
{
    public static bool s_EventsControllerActive;
    public static LevelEventsController? s_Instance;
    public readonly List<CLevelMessage> Shown = new();
    public void ShowLevelMessage(CLevelMessage message)
    {
        bool run = (bool)typeof(TutorialChainHold).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { this, message })!;
        if (run) Shown.Add(message);
    }
}
namespace GloomhavenVR.Compat { internal static class TutorialVR { internal static bool Enabled; } }
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Warn(string scope, string message) { }
        internal static void Info(string scope, string message) { }
        internal static void Note(string scope, string message) { }
    }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type, string method) { }
    }
    public static class AccessTools
    {
        public static Type? TypeByName(string name) => typeof(LevelEventsController);
        public static MethodInfo? Method(Type type, string name, Type[] parameters) => type.GetMethod(name, parameters);
    }
}
namespace ScenarioRuleLibrary { }

namespace ScenarioRuleLibrary.CustomLevels
{
    public class CLevelMessage
    {
        public string MessageName = "message";
        public string LayoutType = "box";
        public CLevelTrigger? DisplayTrigger;
    }
    public class CLevelTrigger { public bool IsUIEventTypeTrigger; public int EventTriggerTypeInt; }
}
namespace GloomhavenVR.Compat
{
    internal static class TutorialGrabStep { }
    internal static class ControlsTutorial { }
    internal static class WallFadeDisable { }
}
