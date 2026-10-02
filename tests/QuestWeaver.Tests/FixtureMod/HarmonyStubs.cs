namespace HarmonyLib;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HarmonyPatch : Attribute
{
    public HarmonyPatch(Type type, string method) { }
    public HarmonyPatch() { }
}
public sealed class Harmony
{
    public string Id { get; }
    public Harmony(string id) { Id = id; }
    public void PatchAll(Type type) => throw new InvalidOperationException("Runtime patch engine must never be called.");
    public void UnpatchSelf() => throw new InvalidOperationException("Runtime unpatch engine must never be called.");
    public System.Reflection.MethodInfo? Patch(System.Reflection.MethodBase original, HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null, HarmonyMethod? transpiler = null, HarmonyMethod? finalizer = null,
        HarmonyMethod? ilManipulator = null) => throw new InvalidOperationException("Runtime direct patch engine must never be called.");
}
public sealed class HarmonyMethod
{
    public System.Reflection.MethodInfo method;
    public HarmonyMethod(Type type, string name) { method = type.GetMethod(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!; }
}
public static class AccessTools
{
    public delegate ref F FieldRef<in T, F>(T instance);
    public static FieldRef<T, F> FieldRefAccess<T, F>(string name) => throw new InvalidOperationException("Runtime emit helper must never be called.");
}
