// GH.Runtime defines this global compiler-visible name. Retain its conditional
// logging contract so a Unity-only fixture cannot conceal lost video evidence.
internal static class Debug
{
    [System.Diagnostics.Conditional("ENABLE_LOGS")]
    public static void Log(object value) => UnityEngine.Debug.Log(value);
    [System.Diagnostics.Conditional("ENABLE_LOGS")]
    public static void LogWarning(object value) => UnityEngine.Debug.LogWarning(value);
    public static void LogError(object value) => UnityEngine.Debug.LogError(value);
}
