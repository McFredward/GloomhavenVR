using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit original-game controller/collector/config boundaries. Unity objects, materials,
// renderer flags, hierarchy, bounds, MPBs and camera rendering are real engine calls.
public sealed class ActorBehaviour : MonoBehaviour { }
public sealed class CInteractableActor : MonoBehaviour { }
public sealed class ProceduralMapTile : MonoBehaviour { }
public sealed class FloorMarker : MonoBehaviour { }
public sealed class ProceduralWall : MonoBehaviour { public static readonly List<object> m_WallCache = new(); }
public sealed class TilesOcclusionGenerator : MonoBehaviour
{
    public static TilesOcclusionGenerator s_Instance = null!;
    public readonly List<object> m_RoomRenderers = new();
}
public sealed class SceneController
{
    public static SceneController Instance = new();
    public Scene GetCurrentScene;
    public bool IsLoading, ScenarioIsLoading;
}
namespace ScenarioRuleLibrary { public static class ScenarioManager { public static object? Scenario; } }
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.WorldUI { internal static class VROptionsTab { internal static bool IsOpen; } }
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldProps
    {
        internal static readonly List<GameObject> Visuals = new();
        internal static int Count => Visuals.Count;
        internal static bool TryGetSlot(int i, out object prop, out GameObject visual, out int side, out float size)
        {prop=null!; visual=Visuals[i]; side=0; size=1; return visual!=null;}
        internal static bool Owns(Transform? node)
        {for (;node!=null;node=node.parent) if (Visuals.Contains(node.gameObject)||NetHeldProps.Visuals.Contains(node.gameObject)) return true; return false;}
    }
    internal static class NetHeldProps
    {
        internal static readonly List<GameObject> Visuals = new();
        internal static int Count => Visuals.Count;
        internal static void CopyVisualRoots(List<GameObject> target) => target.AddRange(Visuals);
    }
}
namespace GloomhavenVR.Core
{
    // Named aliases injected into compiled copies only. Native Time cannot be manually
    // advanced by a synchronous editor test; receipts record this exact transformation.
    internal static class WallFixtureClock { internal static int frameCount; internal static float unscaledTime, unscaledDeltaTime; }
    internal static class WallFixtureFocus { internal static bool isFocused=true; }
    internal static class PerfConfig { internal static int WallVisibilityMode, WallAutoHideBelowFps=15; }
    internal static class VRSession { internal static bool IsRunning=true; internal static bool? InputFocus=true; }
    internal static class ScenarioInteractionPreparation { internal static bool IsPreparing; }
    internal static class ScenarioRoomLoading { internal static bool HasPendingReveal; }
    internal enum VRLogLevel { Debug }
    internal static class VRLog
    {
        internal static bool Wants(VRLogLevel level) => true;
        internal static readonly List<string> Messages = new();
        internal static void Info(string scope,string message)=>Messages.Add(message);
        internal static void Debug(string scope,string message)=>Messages.Add(message);
        internal static void Warn(string scope,string message)=>Messages.Add(message);
        internal static void Alert(string scope,string message)=>Messages.Add(message);
    }
    internal static class PerfMonitor
    {
        internal readonly struct Quiet : IDisposable { public void Dispose(){ } }
        internal static Quiet Scope(string name) => new Quiet();
    }
    internal static class WallCommitGeometryReads { internal static Bounds Read(Renderer renderer) => renderer.bounds; }
    internal static class ScenarioEnvironmentBudget
    {
        internal static Action<Renderer>? BeforeWrite;
        internal static int Writes;
        internal static void BeforeNativeRendererWrite(Renderer renderer) { Writes++; BeforeWrite?.Invoke(renderer); }
    }
}
