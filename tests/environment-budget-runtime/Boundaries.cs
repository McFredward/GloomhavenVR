using System;
using System.Collections.Generic;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    internal sealed class HarmonyPatch : Attribute { internal HarmonyPatch(Type type, string name) { } }
    internal sealed class Harmony
    {
        internal readonly HashSet<Type> Patched = new();
        internal static Action<Type>? PatchObserver = null;
        internal static Action? UnpatchObserver = null;
        internal int PatchCalls;
        internal void PatchAll(Type type) { PatchCalls++; Patched.Add(type); PatchObserver?.Invoke(type); }
        internal void UnpatchSelf() => UnpatchObserver?.Invoke();
    }
}

public class ProceduralBase : MonoBehaviour { public void NotifyContentPlacementComplete() { } }
public sealed class ProceduralMapTile : ProceduralBase { public void ShowContent(GameObject o) { } }
public sealed class ApparanceEntity { public static GameObject CreateInstance(GameObject template, Vector3 position, Vector3 scale, Quaternion rotation, Transform parent) => UnityEngine.Object.Instantiate(template, parent); }
public sealed class ProceduralScenario : ProceduralBase { }
public sealed class ProceduralProp : MonoBehaviour { }
public sealed class ProceduralDoorway : MonoBehaviour { }
public sealed class UnityGameEditorDoorProp : MonoBehaviour { }
public sealed class CInteractable : MonoBehaviour { }
public sealed class ActorBehaviour : MonoBehaviour { }
public sealed class MaterialLoaderData
{
    public Renderer? Renderer;
    // The actual native loader writes this before addressable completion. Asset
    // requests themselves are the explicit boundary; renderer visibility is real.
    public void LoadMaterials() { if (Renderer != null) Renderer.enabled = false; }
}
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
        internal static HarmonyLib.Harmony Harmony = new HarmonyLib.Harmony();
    }
    internal static class BankFixtureAssets { internal static Func<string, TextAsset?> Provider = _ => null; internal static TextAsset? Resolve(string path) => Provider(path); }
    internal static class BankFixturePaths { internal static string streamingAssetsPath => Environment.GetEnvironmentVariable("GHVR_ENVIRONMENT_STREAMING_ASSETS")!; }
    internal static class PerfConfig
    {
        internal static bool StaticScenarioBatchesOn, SimpleEnvironmentShadingOn;
        internal static bool SharedEnvironmentMaterialReadsOn = true, EnvironmentMeshBankOn = false, EnvironmentDrawInstancingOn = false;
        internal static int EnvironmentEffectsDensityPercent = 100;
    }
    internal static class VRLog
    {
        internal static readonly List<string> Faults = new List<string>();
        internal static readonly List<string> DebugLines = new List<string>();
        internal static bool DebugEnabled = true;
        internal static bool Wants(VRLogLevel level) => DebugEnabled;
        internal static void Debug(string scope, string message) { DebugLines.Add(message); }
        internal static void Note(string scope, string message) { Faults.Add(message); }
    }
    internal static class PerfMonitor
    {
        private readonly struct QuietScope : IDisposable { public void Dispose() { } }
        internal static bool ThrowDrawTrace;
        internal static bool StepsActive = true;
        internal static readonly Dictionary<string,long> Counts = new Dictionary<string,long>();
        internal static void Count(string name,long amount=1L)
        {if(!StepsActive||amount==0)return;Counts.TryGetValue(name,out long old);Counts[name]=old+amount;}
        internal static IDisposable Scope(string name)
        {
            if (ThrowDrawTrace && name == "WallFade.DrawTrace") throw new InvalidOperationException("fixture diagnostic fault");
            return new QuietScope();
        }
    }
    internal static class BundleShaders
    {
        internal static bool ThrowResolve;
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
