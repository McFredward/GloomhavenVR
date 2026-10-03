using System;
using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.SceneManagement;

// Game lifecycle, saved settings and held registries are boundary inputs only. The complete
// actual production driver runs against real Unity LODGroup, Renderer, Cloth and transforms.
namespace ScenarioRuleLibrary
{
    public class CActor
    {
        public enum EType { Player, Enemy, HeroSummon, Ally, Enemy2, Neutral, Unknown }
        public EType Type;
        public bool IsMonsterType => Type is EType.Enemy or EType.Enemy2 or EType.Ally or EType.Neutral;
    }
}
public class ActorBehaviour : MonoBehaviour
{
    private GameObject? m_RootGameObject;
    private int m_ForcingPositionChangeCounter;
    public CActor Actor = null!;
    public void Bind(GameObject root,CActor actor) { m_RootGameObject=root;Actor=actor; }
    public void ForceNativePosition(int count) { m_ForcingPositionChangeCounter=count; }
    public bool Bound => m_RootGameObject != null && m_ForcingPositionChangeCounter >= 0;
    public static void SetActor(GameObject gameObject,CActor actor) => GetActorBehaviour(gameObject).Bind(gameObject,actor);
    public static ActorBehaviour GetActorBehaviour(GameObject root) => root.GetComponentInChildren<ActorBehaviour>(true);
}
public class ProceduralScenario : MonoBehaviour { }
public class MaterialLoaderData
{
    public Renderer Renderer=null!;
    public void Complete(Material[] materials) { Renderer.sharedMaterials=materials; CheckAllMaterialLoaded(); }
    private void CheckAllMaterialLoaded() { Renderer.enabled=true; }
}
public class ClientScenarioManager : MonoBehaviour
{
    public static ClientScenarioManager s_ClientScenarioManager = null!;
    public GameObject m_Board = null!;
}
public class Choreographer
{
    public static Choreographer s_Choreographer = new();
    public Scene m_ProcGenScene;
}
public class SceneController
{
    public static SceneController Instance = new();
    public bool IsLoading,ScenarioIsLoading;
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute
    { public HarmonyPatch(Type type,string name) { } }
    internal sealed class TestHarmony { internal void PatchAll(Type type) { } }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class FigureCloth
    {
        internal static readonly HashSet<Cloth> DisabledClaims=new();
        internal static bool TakeDisabledSimulationOwnership(Cloth cloth) => DisabledClaims.Remove(cloth);
    }
    internal static class HeldFigures
    {
        internal static ActorBehaviour? Held;
        internal static bool Owns(ActorBehaviour actor) => Held == actor;
    }
    internal static class NetHeldFigures
    {
        internal static ActorBehaviour? Held;
        internal static bool Owns(ActorBehaviour actor) => Held == actor;
    }
}
namespace GloomhavenVR.Core
{
    internal static class FigureClock { internal static float Now; }
    internal static class VRSession
    {
        internal static bool IsRunning=true;
        internal static HarmonyLib.TestHarmony Harmony=new();
    }
    internal static class VRCameraPolicy { internal static Camera? AllowedHead => null; }
    internal static class PerfConfig
    {
        internal static bool FigureDistanceLodEnabled => false;
        internal static int MaximumSkinningBones => 0;
        internal static int PlayerFigureDetailPercent=100,EnemyFigureDetailPercent=100,FigureEffectsDensityPercent=100;
        internal static bool ThrowOnRead;
        private static bool ClothEnabled=true;
        internal static bool FigureClothSimulationEnabled
        {
            get { if(ThrowOnRead)throw new InvalidOperationException("Injected boundary failure"); return ClothEnabled; }
            set { ClothEnabled=value; }
        }
    }
    internal static class VRLog
    {
        internal static bool WantsDebug=true;
        internal static readonly List<string> Messages=new();
        internal static void Debug(string scope,string text) => Messages.Add(text);
        internal static void Note(string scope,string text) => Messages.Add(text);
    }
    internal static class PerfMonitor
    {
        internal static TestScope Scope(string text) => new();
        internal readonly struct TestScope:IDisposable { public void Dispose() { } }
    }
}
