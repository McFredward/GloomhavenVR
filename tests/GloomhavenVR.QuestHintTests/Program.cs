using System;
using System.Reflection;
using GLOO.Introduction;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string reason)
    { _checks++; if (!value) throw new Exception(reason); }
    private static bool Dispatch(UIIntroduceBase producer, Action? callback = null) =>
        (bool)typeof(QuestPreparationHint).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object?[] { producer, callback })!;

    private static void Main()
    {
        var quest = new UIIntroduceBase();
        var goal = new UIIntroduceBase();
        var manager = new QuestManager { questIntroduction = quest };
        Singleton<QuestManager>.Instance = manager;
        Singleton<QuestManager>.IsInitialized = true;
        VRSession.IsRunning = true;
        int closed = 0;
        Check(!Dispatch(quest), "Exact preparation producer must not enqueue the early hint");
        Check(!Dispatch(quest, () => closed++), "Suppression must retain native continuation");
        Check(closed == 1, "Suppressed producer must deliver its callback exactly once");
        Check(Dispatch(goal, () => closed++), "Battle-goal hint must stay native");
        Check(closed == 1, "Unsuppressed callbacks belong to the native process");
        Check(Dispatch(new UIIntroduceBase()), "Other tutorial producers must stay native");
        VRSession.IsRunning = false;
        Check(Dispatch(quest, () => closed++), "Flat presentation must stay native");
        Check(closed == 1, "Flat callback must not run before native dismissal");
        VRSession.IsRunning = true;
        Singleton<QuestManager>.IsInitialized = false;
        Check(Dispatch(quest), "Missing manager must not suppress unrelated introductions");
        Singleton<QuestManager>.IsInitialized = true;
        Singleton<QuestManager>.Instance = null!;
        Check(Dispatch(quest), "Destroyed manager must preserve native fallback");
        Singleton<QuestManager>.Instance = new QuestManager { questIntroduction = goal };
        Check(Dispatch(quest), "Stale scene producer must not match the next scene manager");
        Check(!Dispatch(goal), "Current scene reference determines the exact exception");
        Console.WriteLine($"Quest preparation production prefix: {_checks} assertions passed.");
    }
}

namespace GLOO.Introduction
{
    public class UIIntroduceBase { }
    public class IntroductionConfigUI { }
}
public class QuestManager { public UIIntroduceBase? questIntroduction; }
public static class Singleton<T>
{
    public static bool IsInitialized;
    public static T Instance = default!;
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning; }
    internal static class VRLog { internal static void Note(string scope, string message) { } }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string name, Type[] arguments) { }
    }
}
