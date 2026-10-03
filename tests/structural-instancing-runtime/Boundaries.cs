using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute { internal HarmonyPatch(Type type, string name) { } }
    internal sealed class Harmony { internal void PatchAll(Type type) { } }
}

public class ProceduralBase : MonoBehaviour { public void NotifyContentPlacementComplete() { } }
public sealed class ProceduralMapTile : ProceduralBase { public void ShowContent(GameObject o) { } }
public sealed class ProceduralScenario : ProceduralBase { }
public sealed class ProceduralProp : MonoBehaviour { }
public sealed class ProceduralDoorway : MonoBehaviour { }
public sealed class UnityGameEditorDoorProp : MonoBehaviour { }
public sealed class CInteractable : MonoBehaviour { }
public sealed class ActorBehaviour : MonoBehaviour { }
public sealed class MaterialLoaderData { public Renderer? Renderer; }
public sealed class SceneController
{
    public static SceneController Instance = new SceneController();
    public bool IsLoading, ScenarioIsLoading;
    public void DisableLoadingScreen() { }
}

namespace GloomhavenVR.Core
{
    // Documentation references only; runtime floor arithmetic comes from the full
    // production WallFloorTile copied by the runner, never a semantic substitute.
    internal static class WallStandingProp
    {
        internal const float MaxSpanWU = 6f;
        internal static void Judge() { }
    }
    internal enum VRLogLevel { Debug }
    internal static class VRSession
    {
        internal static bool IsRunning;
        internal static readonly HarmonyLib.Harmony Harmony = new HarmonyLib.Harmony();
    }
    internal static class PerfConfig
    {
        internal static bool StaticScenarioBatchesOn = false, SimpleEnvironmentShadingOn = false;
        internal static int EnvironmentEffectsDensityPercent = 100;
    }
    internal static class VRLog
    {
        internal static readonly List<string> Faults = new List<string>();
        internal static bool Wants(VRLogLevel level) => true;
        internal static bool WantsDebug => true;
        internal static void Debug(string scope, string message) { }
        internal static void Note(string scope, string message) { Faults.Add(message); }
    }
    internal static class PerfMonitor
    {
        private readonly struct QuietScope : IDisposable { public void Dispose() { } }
        internal static IDisposable Scope(string name) => new QuietScope();
    }
    internal static class BundleShaders
    {
        internal static bool ThrowResolve = false;
        internal static Shader? Resolve(string name, string scope, string whenFound, string whenMissing)
        {
            if (ThrowResolve) throw new InvalidOperationException("generated shader resolver fault");
            return Shader.Find(name);
        }
    }
    internal static class SceneRegistry
    {
        internal static class MapTiles
        {
            internal static void Collect(List<ProceduralMapTile> list)
            { list.Clear(); list.AddRange(UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>()); }
        }
    }
}
