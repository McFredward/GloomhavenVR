using System;
using UnityEngine;

// Only the native/runtime boundary is substituted. Profiles, GameObjects, scenes,
// ScriptableObject instantiation and the complete production override run in actual Unity.
public class ProceduralBase : MonoBehaviour { }
public class ProceduralScenario : MonoBehaviour { }
public class ProceduralMapTile : ProceduralBase { public void WriteExtraParameters() { } }
public class ProceduralWall : ProceduralBase { public void WriteExtraParameters() { } }
public class ApparancePlatformSettingData : ScriptableObject
{
    public int _qualityLevel = 1;
    public bool _disableWallClutterGeneration, _disableWallTorchesGeneration;
    public bool _showUnderground = true, _disableSurfaceFeaturesGeneration;
    public int _detailsDisablingLevel;
}
public class PlatformSetting
{ public ApparancePlatformSettingData GetApparenceSettingByCurrentLevel() => throw new NotSupportedException(); }
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type, string method) { } }
    public class Harmony { public void PatchAll(Type type) { } }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession
    { internal static bool IsRunning = true; internal static HarmonyLib.Harmony? Harmony = new(); }
    internal static class PerfConfig { internal static bool ReducedScenarioGenerationOn = true; }
    internal static class VRLog
    { internal static void Info(string scope, string text) { } internal static void Note(string scope, string text) { } }
}
