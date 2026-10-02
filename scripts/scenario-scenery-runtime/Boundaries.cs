using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Native lifecycle/config transport boundaries are inert. The tests use real Unity transforms,
// component lookup, Mesh.bounds, materials, colliders and forceRenderingOff. The complete actual
// production classifier and Driver run unchanged, apart from the deterministic unscaled clock.
public class MaterialLoader : MonoBehaviour
{
    public static int Instances;
    private void Awake() { Instances++; }
}
public class DetailsDisabler : MonoBehaviour { }
public class DetailLevelDisableProvider : MonoBehaviour { }
public class ImportantObjectsShadowsDisabler : MonoBehaviour { }
public class PropObjectsShadowsDisabler : MonoBehaviour { }
public class UnknownNativeCallback : MonoBehaviour { }
namespace ForeignCallbacks { public class MaterialLoader : MonoBehaviour { } }
public class MaterialLoaderData { public Renderer Renderer = null!; }
public class ProceduralBase : MonoBehaviour { public virtual void NotifyContentPlacementComplete() { } }
public class ProceduralMapTile : ProceduralBase { public static void ShowContent(GameObject o) { } }
public class ProceduralWall : ProceduralBase { }
public class ProceduralProp : ProceduralBase { }
public class ProceduralScenario : MonoBehaviour { }
public class ProceduralDoorway : MonoBehaviour { }
public class UnityGameEditorDoorProp : MonoBehaviour { }
public interface IFixtureObjectPlacement { void CreateObject(int child_count); }
public class ApparanceEntity : MonoBehaviour, IFixtureObjectPlacement
{
    public GameObject CreateInstance() => null!;
    void IFixtureObjectPlacement.CreateObject(int child_count) { }
}
public class ActorBehaviour : MonoBehaviour { }
public class CInteractable : MonoBehaviour { }
public class CInteractableTile : CInteractable { }
public class CInteractableActor : CInteractable { }
public class UnityGameEditorObject : MonoBehaviour { public object? PropObject; }
public class SceneController
{
    public static SceneController Instance = new();
    public bool IsLoading, ScenarioIsLoading;
    public void DisableLoadingScreen() { }
}
public class Choreographer
{
    public static Choreographer s_Choreographer = new();
    public Scene m_ProcGenScene;
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch() { } public HarmonyPatch(Type type, string name) { } }
    internal sealed class TestHarmony { internal void PatchAll(Type type) { } }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldProps
    {
        internal static Transform? Held;
        internal static int Count => Held == null ? 0 : 1;
        internal static bool OwnsRendererOf(Transform leaf) => Held != null && leaf.IsChildOf(Held);
    }
    internal static class NetHeldProps { internal static bool Any; }
}
namespace GloomhavenVR.Core
{
    internal static class SceneryClock { internal static float Now; }
    internal static class VRSession { internal static bool IsRunning = true; internal static HarmonyLib.TestHarmony Harmony = new(); }
    internal static class VRLog
    {
        internal static bool WantsDebug = true;
        internal static readonly List<string> Messages = new();
        internal static void Note(string scope, string text) => Messages.Add(text);
        internal static void Debug(string scope, string text) => Messages.Add(text);
    }
    internal static class PerfConfig
    {
        internal static int ScenarioSceneryDensityPercentValue = 100;
        internal static int ScenarioVegetationDensityPercentValue = 100;
        internal static int ScenarioDecorationDensityPercentValue = 100;
    }
    internal static class PerfMonitor
    {
        internal static readonly List<string> Marks = new();
        internal static void MarkChange(string text) => Marks.Add(text);
        internal static TestScope Scope(string text) => new();
        internal readonly struct TestScope : IDisposable { public void Dispose() { } }
    }
    internal static class SceneRegistry
    {
        internal static readonly Registry MapTiles = new();
        internal sealed class Registry
        {
            internal readonly List<ProceduralMapTile> Tiles = new();
            internal void Collect(List<ProceduralMapTile> output)
            {
                output.Clear();
                foreach (var tile in Tiles)
                    if (tile != null && tile.gameObject.activeInHierarchy) output.Add(tile);
            }
        }
    }
}
