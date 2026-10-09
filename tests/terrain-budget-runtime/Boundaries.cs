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
        internal static int MaterialReads;
        internal static void SetPositionAndRotation(Transform target, Vector3 position, Quaternion rotation)
        { PoseWrites++; target.SetPositionAndRotation(position, rotation); }
    }
    // The harness wraps only production primitive accesses, then executes the
    // exact Unity operation. Counts are not inferred from mirrored logic.
    internal static class TerrainReadObserver
    {
        internal static int HeadPositions, HeadScales, HandPositions, PropertyGuards, PropertyReads, PropertyWrites, EffectReads;
        internal static int EnabledReads, ActiveReads, MaskReads;
        internal static bool Enabled(Renderer renderer) { EnabledReads++; TerrainWorkObserver.Native(renderer); return renderer.enabled; }
        internal static bool Active(Renderer renderer) { ActiveReads++; TerrainWorkObserver.Native(renderer); return renderer.gameObject.activeInHierarchy; }
        internal static bool Mask(Renderer renderer) { MaskReads++; TerrainWorkObserver.Native(renderer); return renderer.forceRenderingOff; }
        internal static Vector3 HeadPosition(Transform head) { HeadPositions++; return head.position; }
        internal static float HeadScale(Transform head) { HeadScales++; return head.lossyScale.x; }
        internal static Vector3 HandPosition(GloomhavenVR.Hands.VRHand hand) { HandPositions++; return hand.transform.position; }
        internal static bool HasPropertyBlock(Renderer renderer) { PropertyGuards++; return renderer.HasPropertyBlock(); }
        internal static void GetPropertyBlock(Renderer renderer, MaterialPropertyBlock block)
        { PropertyReads++; renderer.GetPropertyBlock(block); }
        internal static void GetPropertyBlock(Renderer renderer, MaterialPropertyBlock block, int slot)
        { PropertyReads++; renderer.GetPropertyBlock(block, slot); }
        internal static void SetPropertyBlock(Renderer renderer, MaterialPropertyBlock block)
        { PropertyWrites++; renderer.SetPropertyBlock(block); }
        internal static void SetPropertyBlock(Renderer renderer, MaterialPropertyBlock? block, int slot)
        { PropertyWrites++; renderer.SetPropertyBlock(block, slot); }
        internal static float Effect(MaterialPropertyBlock block, string key) { EffectReads++; return block.GetFloat(key); }
        internal static void Reset()
        { HeadPositions=HeadScales=HandPositions=PropertyGuards=PropertyReads=PropertyWrites=EffectReads=0;
            EnabledReads=ActiveReads=MaskReads=0; }
    }
    internal static class TerrainOwnershipObserver
    {
        internal static int VisualReads, RootCopies;
        internal static Transform VisualTransform(GameObject visual) { VisualReads++; return visual.transform; }
        internal static void Reset() { VisualReads = RootCopies = 0; }
    }
    // Observers wrap the extracted driver; every observed native operation still
    // executes unchanged. Exact wall membership and final release are explicit
    // external-owner boundaries rather than a copied wall algorithm.
    internal static class TerrainWorkObserver
    {
        internal static Renderer? Target = null;
        internal static int Validations, Details, GeometrySteps, Preparations, NativeReads, FilterReads;
        private static bool IsTarget(Renderer? renderer) => Target is not null && ReferenceEquals(Target, renderer);
        internal static void Validate(Renderer renderer) { if (IsTarget(renderer)) Validations++; }
        internal static void Detail(Renderer renderer) { if (IsTarget(renderer)) Details++; }
        internal static void Geometry(Renderer renderer) { if (IsTarget(renderer)) GeometrySteps++; }
        internal static void Prepare(Renderer renderer) { if (IsTarget(renderer)) Preparations++; }
        internal static void Native(Renderer renderer) { if (IsTarget(renderer)) NativeReads++; }
        internal static MeshFilter Filter(Transform node, MeshRenderer? renderer)
        { if (IsTarget(renderer)) FilterReads++; return node.GetComponent<MeshFilter>(); }
        internal static void Reset(Renderer? renderer)
        { Target = renderer; Validations = Details = GeometrySteps = Preparations = NativeReads = FilterReads = 0; }
    }
    // The narrow wall-hide fixture executes actual Terrain ownership through the
    // production write contract. The independent environment chunks are covered
    // by the wall worker's actual-owner fixture, rather than modeled here.
    internal static class ScenarioEnvironmentBudget
    {
        internal static int Writes;
        internal static bool OwnsRenderSubstitute(Renderer renderer) => false;
        internal static void BeforeNativeRendererWrite(Renderer renderer)
        { Writes++; ScenarioTerrainBudget.BeforeNativeRendererWrite(renderer); }
    }
    internal static class ScenarioSceneryBudget
    {
        internal static void AfterPerformanceWallRestore(Renderer renderer) { }
    }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class PerfConfig
    {
        internal static bool CheapWallShadingOn;
        internal static bool TerrainSubstitutionOn = true;
        internal static bool SharedEnvironmentMaterialReadsOn;
        internal static int TerrainCameraSourceLimit;
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
        internal static void RegisterDebug(string name) { }
        internal static bool StepsActive => true;
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
    // Only registration is a fixture boundary. Every read/ancestry/copy API is
    // source-extracted from the real local and remote prop owners.
    internal sealed class GrabbableProp { internal GameObject Visual = null!; }
    internal static partial class PropGrab
    {
        private static readonly Dictionary<int, GrabbableProp> Registry = new();
        internal static void SetRoots(params GameObject[] roots)
        { Registry.Clear(); for (int i = 0; i < roots.Length; i++) Registry.Add(i, new GrabbableProp { Visual = roots[i] }); }
    }
    internal static partial class HeldProps
    {
        private static readonly List<ScenarioRuleLibrary.CObjectProp> Held = new();
        private static readonly List<GloomhavenVR.Hands.HandSide> Sides = new();
        private static readonly List<GameObject> Visuals = new();
        private static readonly List<float> HomeWorldScales = new();
        internal static void SetRoots(params GameObject[] roots)
        {
            Held.Clear(); Sides.Clear(); Visuals.Clear(); HomeWorldScales.Clear();
            foreach (GameObject visual in roots)
            { Held.Add(new ScenarioRuleLibrary.CObjectProp()); Sides.Add(GloomhavenVR.Hands.HandSide.Right); Visuals.Add(visual); HomeWorldScales.Add(1f); }
        }
    }
    internal static partial class NetHeldProps
    {
        private static readonly Dictionary<int, GameObject> Visuals = new();
        internal static void SetRoots(params GameObject[] roots)
        { Visuals.Clear(); for (int i = 0; i < roots.Length; i++) Visuals.Add(i, roots[i]); }
    }
}
namespace ScenarioRuleLibrary { internal sealed class CObjectProp { } }
namespace GloomhavenVR.Hands
{
    internal enum HandSide { Left, Right }
    internal sealed class VRHand : MonoBehaviour { internal bool HasPose; internal float WorldScale = 1f; }
    internal static class VRHands { internal static VRHand? Left, Right = null; }
}
