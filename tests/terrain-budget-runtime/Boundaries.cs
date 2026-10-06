using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ProceduralMapTile : MonoBehaviour { }
public sealed class ProceduralScenario : MonoBehaviour { }
public sealed class ProceduralWall : MonoBehaviour { public static readonly List<ProceduralWall> m_WallCache = new(); }
public sealed class ProceduralProp : MonoBehaviour { }
public sealed class ProceduralDoorway : MonoBehaviour { }
public sealed class UnityGameEditorDoorProp : MonoBehaviour { }
public sealed class CInteractable : MonoBehaviour { }
public sealed class ActorBehaviour : MonoBehaviour { }
namespace GloomhavenVR.Core
{
    internal enum VRLogLevel { Info, Debug }
    // The compiled fixture binds the one private production pose-write call here.
    // It records invocation and still executes the unchanged actual Unity operation;
    // Unity's hasChanged alone cannot detect repeated identical setter invocations.
    internal static class TerrainWriteObserver
    {
        internal static int PoseWrites;
        internal static void SetPositionAndRotation(Transform target, Vector3 position, Quaternion rotation)
        { PoseWrites++; target.SetPositionAndRotation(position, rotation); }
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class PerfConfig
    {
        internal static bool CheapWallShadingOn;
        internal static int TerrainDetailPercent = 100, DistantTerrainDetailPercent = 100;
        internal static float TerrainDistanceMeters = .75f;
    }
    internal static class VRLog
    {
        internal static VRLogLevel Level = VRLogLevel.Debug;
        internal static readonly List<string> Faults = new();
        internal static void Note(string scope, string message) => Faults.Add(message);
        internal static void Debug(string scope, string message) => Faults.Add(message);
    }
    internal static class BundleShaders
    {
        internal static bool Throw;
        internal static Shader? Resolve(string name, string scope, string yes, string no)
        { if (Throw) throw new InvalidOperationException("fixture shader resolver fault"); return Shader.Find(name); }
    }
    internal static class PerfMonitor
    {
        private readonly struct Quiet : IDisposable { public void Dispose() { } }
        internal static readonly Dictionary<string,int> Counts = new();
        internal static void Register(string name) { }
        internal static IDisposable Scope(string name) => new Quiet();
        internal static void Count(string name, int count) => Counts[name] = count;
    }
    internal static class SceneRegistry
    {
        internal static class MapTiles
        {
            internal static void Collect(List<ProceduralMapTile> output)
            { output.Clear(); output.AddRange(UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>()); }
        }
    }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldProps { internal static bool Held; internal static bool OwnsRendererOf(Transform t) => Held; }
    internal static class PropGrab { internal static bool Held = false; internal static bool OwnsRendererOf(Transform t) => Held; }
}
namespace GloomhavenVR.Hands
{
    internal sealed class VRHand : MonoBehaviour { internal bool HasPose; internal float WorldScale = 1f; }
    internal static class VRHands { internal static VRHand? Left, Right = null; }
}
