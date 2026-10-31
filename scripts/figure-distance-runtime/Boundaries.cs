using UnityEngine;
namespace GloomhavenVR.Core
{
    // Resolve the fixture's installed native bank beside its loaded assembly,
    // matching the ordinary desktop loader port without enabling Quest storage.
    internal static class RuntimeDepsLoader
    {
        internal static string PluginDir => System.IO.Path.GetDirectoryName(
            typeof(RuntimeDepsLoader).Assembly.Location) ?? string.Empty;
    }
    internal static class PerfConfig
    {
        internal static bool FigureDistanceLodEnabled = true;
        internal static int MaximumSkinningBones, TownNpcMeshDetailPercent = 100;
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRCameraPolicy { internal static Camera? AllowedHead; }
    internal static class VRLog
    {
        internal static int HandRepairs;
        internal static bool WantsDebug => true;
        internal static void Info(string scope, string message)
        { if (scope == "Hands") HandRepairs++; UnityEngine.Debug.Log(scope + ": " + message); }
        internal static void Debug(string scope, string message) => UnityEngine.Debug.Log(scope + ": " + message);
        internal static void Note(string scope, string message) => UnityEngine.Debug.LogWarning(scope + ": " + message);
    }
}
