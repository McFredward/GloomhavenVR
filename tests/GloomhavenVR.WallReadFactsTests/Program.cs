using System;
using System.Linq;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;

namespace UnityEngine
{
    internal sealed class Shader { internal string name; internal Shader(string name) { this.name = name; } }
    internal sealed class Material
    {
        internal Shader? Shader;
        internal int ShaderReads, PropertyReads;
        internal readonly Dictionary<int, float> Floats = new();
        internal bool Keyword;
        internal Shader shader { get { ShaderReads++; return Shader!; } }
        internal bool HasProperty(int id) { PropertyReads++; return Floats.ContainsKey(id); }
        internal float GetFloat(int id) { PropertyReads++; return Floats[id]; }
        internal bool IsKeywordEnabled(string _) { PropertyReads++; return Keyword; }
    }
    internal readonly struct Vector3
    {
        internal readonly float x, y, z;
        internal Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    internal struct Bounds { internal Vector3 center, min; }
    internal sealed class Transform
    {
        internal Transform? parent;
        internal readonly GameObject gameObject;
        internal Transform() { gameObject = new GameObject(this); }
        internal Transform(GameObject owner) { gameObject = owner; }
        internal bool IsChildOf(Transform root)
        { for (Transform? node=this;node!=null;node=node.parent) if(ReferenceEquals(root,node))return true; return false; }
        internal Vector3 position = default;
        internal readonly HashSet<Type> Components = new();
        internal string name = "Native unit";
        internal int Queries;
        internal T? GetComponent<T>() where T : class, new()
        { Queries++; return Components.Contains(typeof(T)) ? new T() : null; }
    }
    internal static class Time { internal static int frameCount; }
    internal sealed class Component
    {
        internal readonly List<MeshRenderer> Children = new();
        internal int HierarchyReads;
        internal void GetComponentsInChildren(bool includeInactive, List<MeshRenderer> destination)
        { if (includeInactive) throw new Exception("Wall collection must retain active children only"); HierarchyReads++; destination.AddRange(Children); }
    }
    internal sealed class Animator { }
    internal sealed class GameObject
    {
        internal bool activeInHierarchy = true; internal int layer = 0;
        internal readonly Transform transform;
        internal GameObject() { transform = new Transform(this); }
        internal GameObject(Transform owner) { transform=owner; }
    }
    internal class Renderer
    {
        internal bool enabled = true, forceRenderingOff;
        internal readonly GameObject gameObject = new();
        internal string name = "Native renderer";
        internal Transform transform => gameObject.transform;
        internal readonly Bounds bounds = new();
        internal readonly List<Material> Materials = new();
        internal int MaterialListReads;
        private static int _nextId;
        private readonly int _id = ++_nextId;
        internal int GetInstanceID() => _id;
        internal void GetSharedMaterials(List<Material> list)
        { MaterialListReads++; list.AddRange(Materials); }
        internal FigureVisualMirror? Mirror;
        internal int MirrorQueries;
        internal bool ActorParent = false;
        internal ActorBehaviour ActorOwner = new();
        internal WaypointHolder? WaypointOwner;
        internal HexSelect_Control? Selector, OwnSelector;
        internal HexSelectControlParticles? SelectorParticles;
        internal ParticleSystem? System;
        internal T? GetComponentInParent<T>(bool includeInactive) where T : class
        {
            if (!includeInactive) throw new InvalidOperationException("Exact visual ownership must include inactive children");
            if (typeof(T) == typeof(FigureVisualMirror)) { MirrorQueries++; return Mirror as T; }
            if (typeof(T) == typeof(ActorBehaviour)) return ActorParent ? ActorOwner as T : null;
            if (typeof(T) == typeof(WaypointHolder)) return WaypointOwner as T;
            return typeof(T) == typeof(HexSelect_Control) ? Selector as T : SelectorParticles as T;
        }
        internal T? GetComponent<T>() where T : class => typeof(T) == typeof(HexSelect_Control) ? OwnSelector as T : System as T;
    }
    internal sealed class ParticleSystem { internal int particleCount; }
    internal sealed class ParticleSystemRenderer : Renderer { }
    internal sealed class MeshRenderer : Renderer
    {
        internal int Restores;
        internal bool StandingFloor, StandingFigure;
        internal void SetPropertyBlock(object? _) { Restores++; }
    }
    internal static class Mathf
    {
        internal static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        internal static float Abs(float value) => Math.Abs(value);
        internal static int NextPowerOfTwo(int value) { int n = 1; while (n < value) n *= 2; return n; }
    }
}
internal sealed class HexSelect_Control { internal MeshRenderer? HexProjector; }
internal sealed class HexSelectControlParticles
{
    internal ParticleSystem[]? ParticleBits, ParticleHover;
    internal MeshRenderer? UnseenGroundPlane;
}
internal sealed class WaypointHolder
{
    internal sealed class WaypointPrefab { internal GameObject? Prefab; }
    internal List<WaypointPrefab>? m_Prefabs;
}
internal sealed class ObjectPool
{
    internal static ObjectPool? instance;
    // Actual native field names and exact collection shapes, borrowed by production reflection.
    private readonly Dictionary<GameObject,GameObject> spawnedObjects = new();
    private readonly Dictionary<GameObject,List<GameObject>> pooledObjects = new();
    internal void Spawn(GameObject instance,GameObject prefab) { spawnedObjects[instance]=prefab; }
    internal void Park(GameObject instance,GameObject prefab)
    { spawnedObjects.Remove(instance); pooledObjects[prefab]=new List<GameObject>{instance}; }
    internal void Clear() { spawnedObjects.Clear(); pooledObjects.Clear(); }
}
internal sealed class GlobalSettings
{
    internal static GlobalSettings? Instance;
    internal sealed class GlobalParticleEffects
    { internal GameObject? DefaultHealEffect = null, DefaultPositiveCondition = null, DefaultNegativeCondition = null, DefaultCharacterReveal = null, DefaultCharacterSwap = null; }
    internal sealed class MagicEffects { internal GameObject? RetaliateHit = null, RetaliateTarget = null, WoundDamage = null; }
    internal sealed class ActiveBonusBuffTargetEffects
    {
        internal GameObject? AttackBuffTargetEffect = null, ShieldActiveBonusTargetEffect = null, RetaliateActiveBonusTargetEffect = null, GainShield = null, GainRetaliate = null, GainDisarm = null, GainImmobilize = null, GainPoison = null, GainStun = null, GainWound = null, GainBless = null, GainCurse = null, GainSleep = null, GainStrengthen = null, GainMuddle = null, GainInvisibility = null, GainAddTarget = null, GainAddHeal = null, GainAddRange = null, GainAttackersGainDisadvantage = null, GainAttackActiveBonus = null, GainDefault = null;
    }
    internal GlobalParticleEffects? m_GlobalParticles = null;
    internal MagicEffects? m_MagicEffects = null;
    internal ActiveBonusBuffTargetEffects? m_ActiveBonusBuffTargetEffects = null;
}
internal sealed class TilesOcclusionGenerator { internal readonly List<MeshRenderer> m_RoomRenderers = new(); }
internal sealed class ActorBehaviour { internal readonly GameObject gameObject = new(); }
internal sealed class CInteractableActor { }

namespace GloomhavenVR.Board.FigureGrab { internal sealed class FigureVisualMirror { } }
namespace GloomhavenVR.Core
{
    internal static class VRLayers
    { internal const int ModLayer = 31; internal const string ModOwnedNamePrefix = "VR", ModOwnedQualifiedPrefix = "GloomhavenVR."; }

    internal static class ScenarioEnvironmentBudget { internal static void BeforeNativeRendererWrite(Renderer renderer) { } }
    internal static class ScenarioSceneryBudget
    {
        internal static readonly HashSet<Renderer> Hidden = new();
        internal static bool IsOwnedHidden(Renderer renderer) => renderer.forceRenderingOff && Hidden.Contains(renderer);
    }
    internal static class PerfConfig
    {
        internal static bool SharedWallReadCache = true;
        internal static int Reads;
        internal static bool SharedWallReadCacheOn { get { Reads++; return SharedWallReadCache; } }
    }
    internal static partial class WallSegmentFade
    {
        internal static int Run() => FadeDriver.Run();
        private sealed partial class FadeDriver
        {
            private const int CutoffId = 1, ToggleWallfadeMatId = 2, WallFadeOnMatId = 3,
                ToggleWallFadeLocalMatId = 4;
            private const string WallFadeOnKeyword = "_WALLFADE_ON_ON";
            private static bool _figureMemoActive;
            private static readonly Dictionary<Transform, bool> FigureAncestryMemo = new(),
                GameLogicAncestryMemo = new(), WallGeneratorAncestryMemo = new();
            private readonly Dictionary<Shader, ShaderFadeName> _shaderFadeName = new();
            private readonly List<Material> _matScratch = new();
            private readonly Dictionary<Shader, bool> _shaderVerdict = new(), _shaderFoliageVerdict = new(), _shaderWaterVerdict = new();
            private Renderer?[] _snapshot = new Renderer?[1];
            private readonly RendererFact[] _facts = new RendererFact[1];
            private readonly List<int> _factWallFade = new(), _factWater = new();
            private bool _classifyCold = true;
            private ulong[] _sigRow = Array.Empty<ulong>();
            private int[] _sigRowId = Array.Empty<int>();
            private byte[] _sigRowFlags = Array.Empty<byte>();
            private int _sceneExemptRows, _sceneFoldedRows;
            private ulong _sceneFactSigSum, _sceneFactSigXor, _narrowSceneSigSum, _narrowSceneSigXor, _figureSetSigSum, _figureSetSigXor;
            private static bool IsMountableRendererType(Renderer r) => r is MeshRenderer;
            private static bool IsFigureOrActorRenderer(Renderer r) => r.ActorParent;
            private static bool IsWallGeneratedDressing(Renderer _) => false;
            private readonly List<Material> _donors = new();
            private int _assertions;
            private bool _prepWarmArmed, _boardUnmoved;
            private int _cyclePrepRefusedReveal, _cyclePrepRefusedBoard, _memoDrops;
            private readonly Table _live = new();
            private sealed class Table { internal int BuiltRoomCount; }
            private bool BoardStillWhereTheFloorPlanesSayItIs(TilesOcclusionGenerator _) => _boardUnmoved;
            private void BeginStandingMemoScope() { _memoDrops++; ClearPreparedStandingLabels(); }
            private sealed class MountedProp
            {
                internal Renderer Renderer = null!;
                internal bool Driven;
            }
            private sealed class Segment
            {
                internal readonly List<MeshRenderer> PrevRenderers = new();
                internal readonly HashSet<MeshRenderer> PrevRendererSet = new();
                internal int ToggleNative;
                internal float HeldCutoff = 0.5f;
                internal bool CutoffAuthored, VariantLow, VariantHigh;
                internal string ShaderNames = "?";
            }
            private static bool IsStandingFigureProp(MeshRenderer r) => r.StandingFloor || r.StandingFigure;
            private static bool IsStandingFigureOnlyProp(MeshRenderer r) => r.StandingFigure;
            private static void NoteStandingPropBlocked(MeshRenderer _, Segment __) { }
            private static void LogToggleNativeMaterialOnce(Material _) { }
            private void CaptureMasonryTemplate(Material material) => _donors.Add(material);
            private void Check(bool condition, string reason)
            { _assertions++; if (!condition) throw new InvalidOperationException(reason); }
            private static Material Mat(string shader, float? cutoff = null, int? gate = null, float value = 1)
            {
                var material = new Material { Shader = new Shader(shader) };
                if (cutoff.HasValue) material.Floats[CutoffId] = cutoff.Value;
                if (gate.HasValue) material.Floats[gate.Value] = value;
                return material;
            }
            private static MeshRenderer Renderer(params Material[] materials)
            { var renderer = new MeshRenderer(); renderer.Materials.AddRange(materials); return renderer; }
            internal static int Run()
            {
                var driver = new FadeDriver();
                driver.MaterialAdmission();
                driver.FigureRoots();
                driver.DisabledReads();
                driver.VisualCloneOwnership();
                driver.VisualCloneOwnershipQueries();
                driver.NativeSelectionVisuals();
                driver.NativeTransientOwnership();
                driver.ActorParticleSignatures();
                driver.BudgetMasks();
                driver.PreparedReadLifetime();
                driver.PreparedPublicationGate();
                return driver._assertions;
            }
            private void PreparedPublicationGate()
            {
                var gen = new TilesOcclusionGenerator();
                gen.m_RoomRenderers.Add(Renderer());
                _live.BuiltRoomCount = 1;
                _prepWarmArmed = true; _boardUnmoved = true;
                VerifyPrepareStillValid(gen);
                Check(_memoDrops == 0, "An unchanged prepared floor registry keeps its measured derivations");
                gen.m_RoomRenderers.Add(Renderer());
                VerifyPrepareStillValid(gen);
                Check(_memoDrops == 1 && _cyclePrepRefusedReveal == 1,
                    "A room reveal during preparation drops all old measured floor derivations before publication");
                _live.BuiltRoomCount = 2; _boardUnmoved = false;
                VerifyPrepareStillValid(gen);
                Check(_memoDrops == 2 && _cyclePrepRefusedBoard == 1, "A board move during preparation retains the original live-read fallback");
                _prepWarmArmed = false;
                VerifyPrepareStillValid(gen);
                Check(_memoDrops == 2, "A refused warm has no measured state to validate or discard");
            }
            private void PreparedReadLifetime()
            {
                var anchor = new Component();
                var first = Renderer(Mat("Amp_Basic_WallFade"));
                var second = Renderer(Mat("Amp_Basic_WallFade_Low"));
                anchor.Children.Add(first); anchor.Children.Add(second);
                var read = new List<MeshRenderer>();
                Time.frameCount = 10;
                ReadWallCacheChildren(anchor, read);
                Check(anchor.HierarchyReads == 1 && read.SequenceEqual(anchor.Children), "An unwarmed wall retains its live native ordered hierarchy query");
                NotePreparedWallChildren(anchor, read);
                ReadWallCacheChildren(anchor, read);
                Check(anchor.HierarchyReads == 1 && read.SequenceEqual(anchor.Children), "The same-frame prepared wall read avoids a second native hierarchy walk");
                var other = new Component(); other.Children.Add(second);
                ReadWallCacheChildren(other, read);
                Check(other.HierarchyReads == 1 && read.SequenceEqual(other.Children), "A different anchor never borrows another wall's child list");
                Time.frameCount++;
                anchor.Children.RemoveAt(0);
                ReadWallCacheChildren(anchor, read);
                Check(anchor.HierarchyReads == 2 && read.SequenceEqual(anchor.Children), "A cross-frame native regeneration discards prepared hierarchy facts");
                NotePreparedWallChildren(anchor, read);
                Check(_preparedWallChildren.Count == 1 && _preparedWallChildPool[0].SequenceEqual(read), "Only the current frame's wall membership retains references");
                ClearPreparedWallChildren();
                Check(_preparedWallChildren.Count == 0 && _preparedWallChildPool.All(list => list.Count == 0), "Abandon and publication release all prepared child references");
                ReadWallCacheChildren(anchor, read);
                Check(anchor.HierarchyReads == 3, "An abandoned prepare never leaves a reusable wall snapshot");
                ClearPreparedStandingLabels();
                const int units = 100, childrenPerUnit = 50;
                for (int i = 0; i < units; i++)
                {
                    var root = new Transform { name = "Unit " + i };
                    string text = StandingNamedWhy(root, false, "measured floor geometry");
                    for (int child = 1; child < childrenPerUnit; child++)
                        Check(ReferenceEquals(text, StandingNamedWhy(root, false, "measured floor geometry")), "Repeated unit child keeps one exact immutable diagnostic label");
                }
                Check(_standingLabelsBuilt == units, "5000 child verdicts build 100 unit labels instead of 5000 labels");
                var changed = new Transform { name = "Before" };
                Check(StandingNamedWhy(changed, false, "floor") == "'Before' floor", "Label preserves original root and refusal sentence");
                changed.name = "After";
                Check(StandingNamedWhy(changed, false, "floor") == "'After' floor", "A renamed root immediately updates its diagnostic label");
                Check(StandingNamedWhy(changed, true, "actor") == "'After' actor", "A changed arm and measured sentence never reuse another verdict");
                ClearPreparedStandingLabels();
                Check(_standingLabels.Count == 0 && _standingLabelsBuilt == 0, "Prepared labels and counters are released with the measured memo scope");
            }
            private void BudgetMasks()
            {
                var renderer = Renderer(Mat("Amp_Basic_Foliage"));
                var piece = new MountedProp { Renderer = renderer, Driven = true };
                int highest = 0;
                Check(!SkipBudgetMaskedAttachment(piece, 2, ref highest), "An unmasked piece keeps its original visual lane");
                renderer.forceRenderingOff = true;
                Check(!SkipBudgetMaskedAttachment(piece, 2, ref highest), "A foreign visibility mask never grants our visual skip");
                ScenarioSceneryBudget.Hidden.Add(renderer);
                Check(SkipBudgetMaskedAttachment(piece, 2, ref highest) && highest == 2,
                    "A masked driven piece retains the outstanding wall restitution latch");
                highest = 2;
                Check(SkipBudgetMaskedAttachment(piece, 1, ref highest) && highest == 2, "Mask skip never reduces another piece's lane state");
                highest = 0; piece.Driven = false;
                Check(SkipBudgetMaskedAttachment(piece, 2, ref highest) && highest == 0, "An undriven masked piece creates no new fade ownership");
                renderer.forceRenderingOff = false;
                Check(!SkipBudgetMaskedAttachment(piece, 2, ref highest), "Budget restoration immediately resumes ordinary fade delivery");
                Check(IsActuallyDrawing(renderer), "An active enabled mesh contributes effective drawing");
                renderer.forceRenderingOff = true;
                Check(!IsActuallyDrawing(renderer), "Force-hidden scenery never contributes false drawing diagnostics");
                renderer.forceRenderingOff = false; renderer.enabled = false;
                Check(!IsActuallyDrawing(renderer), "Disabled native visibility remains part of the drawing predicate");
                renderer.enabled = true; renderer.gameObject.activeInHierarchy = false;
                Check(!IsActuallyDrawing(renderer), "Inactive native visibility remains part of the drawing predicate");
                var emitter = new ParticleSystem();
                var particleRenderer = new ParticleSystemRenderer { System = emitter };
                Check(!IsActuallyDrawing(particleRenderer), "Empty emitters do not count as drawing");
                emitter.particleCount = 1;
                Check(IsActuallyDrawing(particleRenderer), "A live emitter retains its original drawing classification");
                ScenarioSceneryBudget.Hidden.Clear();
            }
            private void VisualCloneOwnershipQueries()
            {
                var children = Enumerable.Range(0, 100).Select(_ => Renderer(Mat("Amp_Basic"))).ToArray();
                for (int i = 0; i < children.Length; i++)
                    if ((i & 1) == 0) children[i].Mirror = new FigureVisualMirror();
                PerfConfig.SharedWallReadCache = true;
                BeginFigureMemo();
                for (int pass = 0; pass < 100; pass++)
                    for (int i = 0; i < children.Length; i++)
                        Check(IsModObject(children[i]) == ((i & 1) == 0), "Exact mirror ownership is consistent across adoption lanes");
                Check(children.Sum(r => r.MirrorQueries) == 100,
                    "Repeated live adoption lanes query each exact mirror ancestor once per synchronous scope");
                EndFigureMemo();
                Check(VisualMirrorOwnershipMemo.Count == 0, "Mirror ownership memo releases every renderer at the scope boundary");
                children[0].Mirror = null;
                BeginFigureMemo();
                Check(!IsModObject(children[0]) && children[0].MirrorQueries == 2,
                    "A new frame or scope observes native reparenting without stale mirror ownership");
                EndFigureMemo();
                PerfConfig.SharedWallReadCache = false;
                BeginFigureMemo();
                for (int pass = 0; pass < 100; pass++) IsModObject(children[1]);
                Check(children[1].MirrorQueries == 101 && VisualMirrorOwnershipMemo.Count == 0,
                    "Disabled shared-read cache preserves direct exact ownership probes");
                EndFigureMemo();
                PerfConfig.SharedWallReadCache = true;
            }
            private void SurveyOwned(Renderer? renderer)
            {
                _snapshot[0] = renderer;
                _factWallFade.Clear(); _factWater.Clear();
                _sceneExemptRows = _sceneFoldedRows = 0;
                _sceneFactSigSum = _sceneFactSigXor = _narrowSceneSigSum = _narrowSceneSigXor = _figureSetSigSum = _figureSetSigXor = 0;
                ClassifySlice(0, 1);
                _classifyCold = false;
            }
            private void ActorParticleSignatures()
            {
                var particles = new ParticleSystemRenderer { ActorParent = true, System = new ParticleSystem() };
                SurveyOwned(particles);
                Check(!_facts[0].Mod && _sceneExemptRows == 1 && _sceneFoldedRows == 0
                    && _sceneFactSigSum == 0 && _narrowSceneSigSum == 0 && _figureSetSigSum == 0,
                    "An exact native actor particle leaves all three signature halves unchanged");
                particles.gameObject.activeInHierarchy = false;
                SurveyOwned(particles);
                Check(_sceneExemptRows == 1 && _sceneFactSigSum == 0,
                    "A pooled child deactivation under an active native actor remains non-wall");
                SurveyOwned(null);
                Check(_sceneExemptRows == 1 && _sceneFactSigSum == 0,
                    "A destroyed exact actor particle retains its last live exemption");
                particles.ActorParent = false;
                SurveyOwned(particles);
                Check(_sceneFoldedRows == 1 && _sceneFactSigSum != 0,
                    "A pooled particle reparented outside its actor returns to conservative folding");
                particles.ActorParent = true; particles.ActorOwner.gameObject.activeInHierarchy = false;
                SurveyOwned(particles);
                Check(_sceneFoldedRows == 1, "An inactive native actor retains conservative particle membership");
                particles.ActorOwner.gameObject.activeInHierarchy = true;
                particles.Materials.Add(Mat("Fixture/Water_Shd")); _classifyCold = true;
                SurveyOwned(particles);
                Check(_sceneFoldedRows == 1 && _factWater.Count == 1,
                    "An actor water particle retains native protection and conservative signatures");
                var actorMesh = Renderer(Mat("Amp_Basic_WallFade")); actorMesh.ActorParent = true;
                SurveyOwned(actorMesh);
                Check(_sceneFoldedRows == 1 && _factWallFade.Count == 1,
                    "Actor ancestry never exempts a structural or wall-shader mesh");
            }

            private void VisualCloneOwnership()
            {
                var native = Renderer(Mat("Amp_Basic")); native.name = "MO_Spitting_Drake_Mesh_LOD2";
                native.ActorParent = true;
                Check(!IsModObject(native), "Native actor ancestry alone never grants visual ownership");
                SurveyOwned(native);
                Check(!_facts[0].Mod && _sceneFoldedRows == 1 && _sceneExemptRows == 0,
                    "An original native-named actor keeps its cold classifier and signature contribution");
                var clone = Renderer(Mat("Amp_Basic")); clone.name = native.name;
                clone.Mirror = new FigureVisualMirror(); clone.gameObject.activeInHierarchy = false;
                Check(IsModObject(clone), "An inactive native-named mirror child is excluded from live wall adoption");
                SurveyOwned(clone);
                Check(_facts[0].Mod && _sceneExemptRows == 1 && _sceneFoldedRows == 0
                    && _sceneFactSigSum == 0 && _narrowSceneSigSum == 0 && _figureSetSigSum == 0,
                    "Exact mirror-owned children contribute nothing to all three wall signature halves");
                int queries = clone.MirrorQueries;
                SurveyOwned(clone);
                Check(clone.MirrorQueries == queries, "Warm census retains clone ownership without new native ancestry queries");
                int cloneId = clone.GetInstanceID();
                SurveyOwned(null);
                Check(SceneRowWasExemptWhenAlive(0) && _sigRowId[0] == cloneId
                    && _sceneExemptRows == 1 && _sceneFoldedRows == 0
                    && _sceneFactSigSum == 0 && _narrowSceneSigSum == 0 && _figureSetSigSum == 0,
                    "A dead clone row retains its exact exemption without signature churn");
                var wall = Renderer(Mat("Amp_Basic_WallFade")); wall.Mirror = clone.Mirror; wall.name = clone.name;
                SurveyOwned(wall);
                Check(_facts[0].Mod && _facts[0].WallFadeShader && _factWallFade.Count == 1
                    && _sceneFoldedRows == 1 && _sceneExemptRows == 0 && _sceneFactSigSum != 0,
                    "A mirror mesh with a real wall-fade shader retains its conservative signature");
                SurveyOwned(null);
                Check(!SceneRowWasExemptWhenAlive(0) && _sceneFoldedRows == 1 && _sceneFactSigSum != 0,
                    "Death of a real wall-shader row remains a structural signature change");
                var water = Renderer(Mat("Water_Shd")); water.name = "Fountain";
                SurveyOwned(water);
                Check(!_facts[0].Mod && _facts[0].WaterSurface && _factWater.Count == 1 && _sceneFoldedRows == 1,
                    "Original native water keeps protection membership and signature contribution");
                var sameName = Renderer(Mat("Amp_Basic")); sameName.name = clone.name;
                Check(!IsModObject(sameName), "Matching a clone child name alone never exempts native scenery");
            }
            private void NativeSelectionVisuals()
            {
                var selector = new HexSelect_Control();
                var projected = Renderer(Mat("GloomhavenVR/HexDecalStable"));
                projected.Selector = selector; selector.HexProjector = projected;
                Check(IsNativeHexSelectionVisual(projected), "Exact native published selection decal is never wall scenery");
                var masonry = Renderer(Mat("Amp_Basic_WallFade")); masonry.Selector = selector;
                Check(!IsNativeHexSelectionVisual(masonry), "Being near a selector never exempts a real wall mesh");
                var rootEmitter = new ParticleSystemRenderer { Selector = selector, OwnSelector = selector, System = new ParticleSystem() };
                Check(IsNativeHexSelectionVisual(rootEmitter), "Exact native root selection emitter is not wall scenery");
                SurveyOwned(rootEmitter);
                Check(_facts[0].Mod && _sceneExemptRows == 1 && _sceneFactSigSum == 0,
                    "Native root selection emitter never moves the production wall signature");
                SurveyOwned(null);
                Check(SceneRowWasExemptWhenAlive(0) && _sceneFactSigSum == 0,
                    "Destroyed native root selection emitter retains its exact exemption");
                var foreignEmitter = new ParticleSystemRenderer { Selector = selector, System = new ParticleSystem() };
                Check(!IsNativeHexSelectionVisual(foreignEmitter), "Parent proximity never exempts a foreign selection-root emitter");
                var particles = new HexSelectControlParticles();
                var emitter = new ParticleSystem();
                var highlight = new ParticleSystemRenderer { System = emitter, SelectorParticles = particles };
                particles.ParticleBits = new[] { emitter };
                Check(IsNativeHexSelectionVisual(highlight), "Native selection bits use exact emitter identity");
                particles.ParticleBits = null; particles.ParticleHover = new[] { emitter };
                Check(IsNativeHexSelectionVisual(highlight), "Native hover bits use exact emitter identity");
                highlight.System = new ParticleSystem();
                Check(!IsNativeHexSelectionVisual(highlight), "Environmental particles beneath a selection root keep their native facts");
                particles.UnseenGroundPlane = masonry; masonry.SelectorParticles = particles;
                Check(!IsNativeHexSelectionVisual(masonry), "Native unseen ground protection stays outside selection exemption");
                projected.Selector = null;
                Check(!IsNativeHexSelectionVisual(projected), "A same-shader object without native ownership is not exempt");
                particles.ParticleHover = null;
                Check(!IsNativeHexSelectionVisual(highlight), "Unpublished missing selection arrays remain conservative");
            }
            private void NativeTransientOwnership()
            {
                var member = new GameObject();
                var holder = new WaypointHolder { m_Prefabs = new List<WaypointHolder.WaypointPrefab>
                    { new WaypointHolder.WaypointPrefab { Prefab=member } } };
                var particle = new ParticleSystemRenderer { name="Sparks", System=new ParticleSystem(), WaypointOwner=holder };
                particle.transform.parent=member.transform;
                Check(IsNativeNonWallPresentation(particle) && IsModObject(particle),
                    "Published waypoint particles share exact signature and live collector rejection");
                var sameName=new ParticleSystemRenderer { name="Sparks", System=new ParticleSystem(), WaypointOwner=holder };
                Check(!IsNativeNonWallPresentation(sameName), "Unpublished nearby same-name scenery cannot become a waypoint");
                SurveyOwned(particle);
                Check(_sceneExemptRows==1 && _sceneFoldedRows==0,
                    "Actual classifier exempts native waypoint identity, active state and death");
                particle.gameObject.activeInHierarchy=false;
                ulong before=_sceneFactSigSum; SurveyOwned(particle);
                Check(_sceneFactSigSum==before, "Inactive pooled waypoint cannot move the wall scene signature");
                particle.gameObject.activeInHierarchy=true; particle.transform.parent=null;
                SurveyOwned(particle);
                Check(_sceneExemptRows==0 && _sceneFoldedRows==1, "Reparented unpublished waypoint fails conservative on warm ownership");
                particle.transform.parent=member.transform;
                SurveyOwned(particle); SurveyOwned(null);
                Check(_sceneFactSigSum==before, "Dead published waypoint keeps its remembered exact exemption");
                particle.Materials.Add(Mat("Amp_Basic_WallFade"));
                Check(!IsNativeNonWallPresentation(particle), "Published waypoint with a real wall shader remains a table input");
                particle.Materials.Clear(); particle.Materials.Add(Mat("Water_Shd"));
                Check(!IsNativeNonWallPresentation(particle), "Published waypoint water remains a protection input");
                particle.Materials.Clear(); holder.m_Prefabs=null;
                Check(!IsNativeNonWallPresentation(particle), "Missing published waypoint references remain conservative");

                var root = new GameObject(); var original=new GameObject();
                var fx=new ParticleSystemRenderer { name="Sparks", System=new ParticleSystem() };
                fx.transform.parent=root.transform;
                GlobalSettings.Instance=new GlobalSettings { m_MagicEffects=new GlobalSettings.MagicEffects { RetaliateHit=original } };
                ObjectPool.instance=new ObjectPool(); ObjectPool.instance.Spawn(root,original);
                BeginFigureMemo();
                Check(IsNativeNonWallPresentation(fx) && IsModObject(fx), "Exact original combat-prefab pool membership rejects live collector adoption");
                Check(NativeActionRoots.Count==1, "Native action roots are borrowed once inside one synchronous owner window");
                EndFigureMemo();
                Check(NativeActionRoots.Count==0 && NativeVisualOwnershipMemo.Count==0,
                    "Native ownership window releases all roots and renderer references");
                ObjectPool.instance.Park(root,original);
                Check(IsNativeNonWallPresentation(fx), "Native recycle and inactive action pool preserve exact prefab ownership");
                ObjectPool.instance.Clear(); ObjectPool.instance.Spawn(root,new GameObject());
                Check(!IsNativeNonWallPresentation(fx), "Pooled root reassigned to scenery immediately loses action ownership");
                ObjectPool.instance.Spawn(root,original); fx.Materials.Add(Mat("Water_Shd"));
                Check(!IsNativeNonWallPresentation(fx), "Exact combat pool never exempts authored water surfaces");
                fx.Materials.Clear(); fx.Materials.Add(Mat("Amp_Basic_WallFade"));
                Check(!IsNativeNonWallPresentation(fx), "Exact combat pool never exempts real wall-shader members");
                fx.Materials.Clear(); fx.name="Fountain";
                Check(!IsNativeNonWallPresentation(fx), "Existing native water-name protection is conservative for combat pools");
                ObjectPool.instance=null; GlobalSettings.Instance=null;
                Check(!IsNativeNonWallPresentation(fx), "Unavailable native owners keep original signature and collectors");
                NativeActionRoots.Clear(); _nativeActionRootsReady=false;
            }
            private void MaterialAdmission()
            {
                // These are actual production admission calls, including variant, cutoff,
                // toggle count, donor and standing-prop restitution writes. No parallel mock
                // of the consumer's output policy is used.
                foreach (bool cache in new[] { false, true })
                {
                    if (cache) BeginWallCacheMaterialFacts();
                    var low = Mat("Amp_WallFade_Low", -1);
                    var high = Mat("Amp_WallFade_High", 2);
                    var runtime = Mat("Amp_Basic", 0.4f, ToggleWallfadeMatId, 0);
                    var on = Mat("Amp_Basic", 0.3f, WallFadeOnMatId);
                    var off = Mat("Amp_Basic", 0.3f, WallFadeOnMatId, 0);
                    var keyword = Mat("Amp_Basic", 0.3f, WallFadeOnMatId, 0); keyword.Keyword = true;
                    var localOn = Mat("Amp_Basic", 0.3f, ToggleWallFadeLocalMatId, -0.6f);
                    var localOff = Mat("Amp_Basic", 0.3f, ToggleWallFadeLocalMatId, 0.5f);
                    var noCutoff = Mat("Amp_Basic", null, ToggleWallfadeMatId);
                    var precedence = Mat("Amp_Basic", 0.3f, WallFadeOnMatId, 0);
                    precedence.Floats[ToggleWallFadeLocalMatId] = 1;
                    var namedOff = Mat("WallFade_Low", 0.2f, WallFadeOnMatId, 0);
                    var nullShader = new Material();
                    foreach (var entry in new[] {
                        (low, true, true, false, 0.05f), (high, true, false, false, 0.95f),
                        (runtime, true, false, true, 0.4f), (on, true, false, true, 0.3f),
                        (keyword, true, false, true, 0.3f), (localOn, true, false, true, 0.3f),
                        (off, false, false, false, 0.5f), (localOff, false, false, false, 0.5f),
                        (noCutoff, false, false, false, 0.5f), (nullShader, false, false, false, 0.5f),
                        (precedence, false, false, false, 0.5f), (namedOff, true, true, false, 0.2f)
                    })
                    {
                        var segment = new Segment();
                        bool admitted = CollectWallFadeInfo(Renderer(entry.Item1), segment);
                        Check(admitted == entry.Item2, "Native gate and shader admission must match authored state");
                        Check(segment.VariantLow == entry.Item3 && segment.VariantHigh == (admitted && !entry.Item3),
                            "Native high and low variants must retain the same admission");
                        Check(segment.ToggleNative == (entry.Item4 ? 1 : 0), "Toggle census must remain per material occurrence");
                        Check(segment.HeldCutoff == entry.Item5, "Authored cutoff must retain clamp and default");
                    }
                    var multi = new Segment();
                    int previousDonors = _donors.Count;
                    Check(CollectWallFadeInfo(Renderer(runtime, runtime, low, high), multi), "Multiple slots must be admitted");
                    Check(multi.ToggleNative == 2 && multi.VariantLow && multi.VariantHigh,
                        "Repeated shared material slots still count and donate per occurrence");
                    Check(_donors.Count == previousDonors + 2 && _donors[^1] == runtime,
                        "Template donation must still run for every eligible material occurrence");
                    Check(multi.HeldCutoff == 0.4f && multi.ShaderNames == "Amp_Basic(toggle-native)+Amp_WallFade_Low+Amp_WallFade_High",
                        "Material ordering, first cutoff and shader-name order must stay identical");
                    var noProperty = new Segment();
                    Check(CollectWallFadeInfo(Renderer(Mat("WallFade_High"), runtime), noProperty)
                        && noProperty.HeldCutoff == 0.4f, "A missing cutoff must not prevent a later authored one");
                    var standing = Renderer(runtime); standing.StandingFloor = true;
                    var previous = new Segment(); previous.PrevRenderers.Add(standing);
                    Check(!CollectWallFadeInfo(standing, previous) && standing.Restores == 1
                        && standing.MaterialListReads == 0, "Standing restitution must run before all material reads");
                    Check(CollectWallFadeInfo(standing, new Segment(), true), "Figure-only foliage rule must preserve floor-arm exception");
                    standing.StandingFigure = true;
                    Check(!CollectWallFadeInfo(standing, new Segment(), true), "Figure-arm exception must never admit a figure");
                    if (cache) EndWallCacheMaterialFacts();
                }
                var common = Mat("Amp_Basic", 0.25f, WallFadeOnMatId);
                BeginWallCacheMaterialFacts();
                try
                {
                    Check(CollectWallFadeInfo(Renderer(common), new Segment()), "First shared material must be sampled");
                    int reads = common.PropertyReads, shaderReads = common.ShaderReads;
                    for (int i = 0; i < 4000; i++)
                        Check(CollectWallFadeInfo(Renderer(common), new Segment()), "Every shared renderer must remain admitted");
                    Check(common.PropertyReads == reads && common.ShaderReads == shaderReads,
                        "Repeated shared material inspection must not repeat native property probes");
                    var sameShaderOtherMaterial = Mat("unused", 0.9f, WallFadeOnMatId, 0);
                    sameShaderOtherMaterial.Shader = common.Shader;
                    Check(!CollectWallFadeInfo(Renderer(sameShaderOtherMaterial), new Segment()),
                        "Materials sharing one shader must retain independent authored gates");
                    var replaced = Renderer(common);
                    replaced.Materials.Clear(); replaced.Materials.Add(Mat("Unfadeable"));
                    Check(!CollectWallFadeInfo(replaced, new Segment()) && replaced.MaterialListReads == 1,
                        "Live renderer material-list replacement must remain visible during the commit");
                }
                finally { EndWallCacheMaterialFacts(); }
                Check(!_wallCacheMaterialFactsActive && _wallCacheMaterialFacts.Count == 0,
                    "The material memo must close and release references");
                common.Floats[WallFadeOnMatId] = 0;
                BeginWallCacheMaterialFacts();
                Check(!CollectWallFadeInfo(Renderer(common), new Segment()), "Native gate changes must be re-read next commit");
                EndWallCacheMaterialFacts();
                common.Floats[WallFadeOnMatId] = 1;
                Check(CollectWallFadeInfo(Renderer(common), new Segment()), "Outside the scope properties must be read live");
                common.Floats[WallFadeOnMatId] = 0;
                Check(!CollectWallFadeInfo(Renderer(common), new Segment()), "Outside the scope changes cannot hit stale facts");
            }
            private void FigureRoots()
            {
                foreach (Type marker in new[] { typeof(ActorBehaviour), typeof(CInteractableActor), typeof(Animator) })
                {
                    var root = new Transform(); root.Components.Add(marker);
                    var nodes = new List<Transform> { root };
                    var parent = root;
                    for (int i = 0; i < 12; i++) { parent = new Transform { parent = parent }; nodes.Add(parent); }
                    BeginFigureMemo();
                    for (int i = 0; i < 120; i++)
                    {
                        var leaf = new Transform { parent = parent };
                        Check(FigurePropRootOf(leaf) == root, "Nearest actor, interactable or animator root must match");
                    }
                    int queries = 0; foreach (Transform node in nodes) queries += node.Queries;
                    Check(queries <= 39, "Shared ancestor component lookups must run once per node per synchronous window");
                    var nearer = new Transform { parent = parent }; nearer.Components.Add(typeof(Animator));
                    Check(FigurePropRootOf(new Transform { parent = nearer }) == nearer,
                        "Nested figure roots must choose the nearest component, including inactive-node semantics");
                    EndFigureMemo();
                    Check(FigureRootMemo.Count == 0 && !_figureMemoActive, "Figure root memo must release all transform references");
                    var other = new Transform(); other.Components.Add(typeof(Animator));
                    parent.parent = other;
                    BeginFigureMemo();
                    Check(FigurePropRootOf(parent) == other, "Reparenting between synchronous passes must invalidate the root");
                    EndFigureMemo();
                    parent.parent = null;
                    Check(FigurePropRootOf(parent) == null, "Unscoped figure-root query must remain live");
                }
                Check(FigurePropRootOf(null) == null, "Missing figure root must remain null");
                BeginFigureMemo();
                Check(FigurePropRootOf(new Transform()) == null, "Absent root must retain negative result");
                EndFigureMemo();
            }
            private void DisabledReads()
            {
                PerfConfig.SharedWallReadCache = false;
                int settingsReads = PerfConfig.Reads;
                BeginWallCacheMaterialFacts();
                Check(PerfConfig.Reads == settingsReads + 1, "Material cache setting must be sampled once per phase");
                var material = Mat("Amp_Basic", 0.25f, WallFadeOnMatId);
                Check(CollectWallFadeInfo(Renderer(material), new Segment()), "Disabled cache must retain native admission");
                int properties = material.PropertyReads;
                for (int i = 0; i < 8; i++)
                    Check(CollectWallFadeInfo(Renderer(material), new Segment()), "Disabled material cache must remain usable");
                Check(material.PropertyReads > properties && _wallCacheMaterialFacts.Count == 0,
                    "Disabled material cache must use live probes without memo entries");
                material.Floats[WallFadeOnMatId] = 0;
                Check(!CollectWallFadeInfo(Renderer(material), new Segment()), "Disabled material cache cannot reuse stale authored gates");
                Check(PerfConfig.Reads == settingsReads + 1, "Material consumers must not read the setting per renderer");
                EndWallCacheMaterialFacts();
                BeginFigureMemo();
                Check(_figureMemoActive && !_figureRootMemoActive,
                    "Disabled figure root cache must retain the existing figure ancestry window");
                var root = new Transform(); root.Components.Add(typeof(Animator));
                var leaf = new Transform { parent = root };
                Check(FigurePropRootOf(leaf) == root, "Disabled figure cache must retain exact nearest root");
                int queries = leaf.Queries + root.Queries;
                for (int i = 0; i < 8; i++)
                    Check(FigurePropRootOf(leaf) == root, "Disabled figure cache must remain usable");
                Check(leaf.Queries + root.Queries > queries && FigureRootMemo.Count == 0,
                    "Disabled figure root cache must use live ancestry queries without memo entries");
                Check(PerfConfig.Reads == settingsReads + 2, "Figure root consumers must not read the setting per renderer");
                PerfConfig.SharedWallReadCache = true;
                Check(FigurePropRootOf(leaf) == root && FigureRootMemo.Count == 0,
                    "A changed setting must take effect only at the next synchronous window");
                EndFigureMemo();
                BeginFigureMemo();
                Check(_figureRootMemoActive && FigurePropRootOf(leaf) == root && FigureRootMemo.Count > 0,
                    "Re-enabling the read cache must affect the next synchronous window");
                EndFigureMemo();
                Check(!_figureRootMemoActive && FigureRootMemo.Count == 0,
                    "The figure root cache must close independently of its selected setting");
            }
        }
    }
}
internal static class Program
{
    private static void Main() => Console.WriteLine($"Wall read facts: {WallSegmentFade.Run()} assertions passed.");
}
