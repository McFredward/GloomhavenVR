using FixtureGame;
using HarmonyLib;

namespace FixtureMod;

public static class Entry
{
    private static readonly Harmony Owner = new("fixture.owner");
    public static void Install() { Owner.PatchAll(typeof(CalculatePatch)); Owner.PatchAll(typeof(SimplePatch)); }
    public static void Remove() => Owner.UnpatchSelf();
    public static int Preparations, Selections;
    public static void InstallDynamic() => Owner.PatchAll(typeof(DynamicPatch));
    public static void InstallDirect() => Owner.Patch(typeof(Target).GetMethod(nameof(Target.Direct))!,
        prefix: new HarmonyMethod(typeof(DirectHooks), nameof(DirectHooks.ArbitraryName)),
        postfix: new HarmonyMethod(typeof(DirectHooks), nameof(DirectHooks.AdjustResult)),
        finalizer: new HarmonyMethod(typeof(DirectHooks), nameof(DirectHooks.Finish)));
    private static readonly AccessTools.FieldRef<Target, int> Counter = AccessTools.FieldRefAccess<Target, int>("counter");
    public static void SetCounter(Target instance, int value) => Counter(instance) = value;
}

[HarmonyPatch(typeof(Target), nameof(Target.Calculate))]
internal static class CalculatePatch
{
    private struct State { public int Counter; }
    private static bool Prefix(ref int value, ref int __result, out State __state, ref int ___counter)
    {
        Target.Trace.Add("prefix"); __state = new State { Counter = ___counter };
        if (value == -1) { __result = 70; return false; }
        value += 2; return true;
    }
    private static void Postfix(ref int __result, State __state, System.Reflection.MethodBase __originalMethod)
    { Target.Trace.Add("postfix:" + __originalMethod.Name); __result += __state.Counter; }
    private static Exception? Finalizer(Exception? __exception, ref int __result, State __state)
    {
        Target.Trace.Add("finalizer");
        if (__exception is InvalidOperationException) { __result = 80 + __state.Counter; return null; }
        return __exception;
    }
}

[HarmonyPatch(typeof(Target), nameof(Target.Calculate))]
internal static class SimplePatch
{
    private static void Prefix() => Target.Trace.Add("simple-prefix");
}

[HarmonyPatch]
internal static class DynamicPatch
{
    private static bool Prepare() { Entry.Preparations++; return true; }
    private static System.Reflection.MethodInfo TargetMethod() { Entry.Selections++; return typeof(Target).GetMethod(nameof(Target.Dynamic))!; }
    private static void Prefix(ref int value) => value += 3;
    private static void Postfix(ref int __result) => __result += 4;
}

internal static class DirectHooks
{
    internal static bool ArbitraryName(ref int value) { value *= 4; return true; }
    internal static void AdjustResult(ref int __result) => __result += 2;
    internal static Exception? Finish(Exception? __exception) => __exception;
}
