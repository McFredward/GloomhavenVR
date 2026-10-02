using FixtureGame;
using HarmonyLib;

namespace FixtureMod;

public static class Entry
{
    private static readonly Harmony Owner = new("fixture.owner");
    public static void Install() { Owner.PatchAll(typeof(CalculatePatch)); Owner.PatchAll(typeof(SimplePatch)); Owner.PatchAll(typeof(MutatingPatch)); Owner.PatchAll(typeof(CleanupPatch)); }
    public static void Remove() => Owner.UnpatchSelf();
    public static int Preparations, Selections;
    public static int FinalizerFailures;
    public static void InstallDynamic() => Owner.PatchAll(typeof(DynamicPatch));
#if QUEST_FIXTURE_NEXT
    public static void InstallNext() => Owner.PatchAll(typeof(NextPatch));
#endif
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
        if (Entry.FinalizerFailures > 0) { Entry.FinalizerFailures--; throw new InvalidOperationException("finalizer-failure"); }
        if (__exception is InvalidOperationException) { __result = 80 + __state.Counter; return null; }
        return __exception;
    }
}

[HarmonyPatch(typeof(Target), nameof(Target.Calculate))]
internal static class SimplePatch
{
    private static void Prefix() => Target.Trace.Add("simple-prefix");
}

[HarmonyPatch(typeof(Target), nameof(Target.Calculate))]
internal static class MutatingPatch
{
    private static bool Prefix(ref int value)
    { Target.Trace.Add("mutating-prefix"); if (value == -1) value = -2; return true; }
}

[HarmonyPatch(typeof(Target), nameof(Target.Calculate))]
internal static class CleanupPatch
{
    private static void Finalizer(Exception? __exception) => Target.Trace.Add("cleanup:" + (__exception?.GetType().Name ?? "ok"));
}

[HarmonyPatch]
internal static class DynamicPatch
{
    private static bool Prepare() { Entry.Preparations++; return true; }
    private static System.Reflection.MethodInfo TargetMethod() { Entry.Selections++; return typeof(Target).GetMethod(nameof(Target.Dynamic))!; }
    private static void Prefix(ref int value) => value +=
#if QUEST_FIXTURE_NEXT
        5;
#else
        3;
#endif
    private static void Postfix(ref int __result) => __result += 4;
}

#if QUEST_FIXTURE_NEXT
[HarmonyPatch(typeof(Target), nameof(Target.Dynamic))]
internal static class NextPatch
{
    private static void Postfix(ref int __result) => __result += 10;
}
#endif

internal static class DirectHooks
{
    internal static bool ArbitraryName(ref int value) { value *= 4; return true; }
    internal static void AdjustResult(ref int __result) => __result += 2;
    internal static Exception? Finish(Exception? __exception) => __exception;
}
