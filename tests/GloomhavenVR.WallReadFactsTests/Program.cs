using System;
using System.Linq;
using System.Collections.Generic;
using GloomhavenVR.Core;
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
    internal sealed class Transform
    {
        internal Transform? parent;
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
    internal sealed class GameObject { internal bool activeInHierarchy = true; }
    internal class Renderer
    {
        internal bool enabled = true, forceRenderingOff;
        internal readonly GameObject gameObject = new();
        internal HexSelect_Control? Selector;
        internal HexSelectControlParticles? SelectorParticles;
        internal ParticleSystem? System;
        internal T? GetComponentInParent<T>(bool _) where T : class =>
            typeof(T) == typeof(HexSelect_Control) ? Selector as T : SelectorParticles as T;
        internal T? GetComponent<T>() where T : class => System as T;
    }
    internal sealed class ParticleSystem { internal int particleCount; }
    internal sealed class ParticleSystemRenderer : Renderer { }
    internal sealed class MeshRenderer : Renderer
    {
        internal readonly List<Material> Materials = new();
        internal int MaterialListReads, Restores;
        internal bool StandingFloor, StandingFigure;
        internal void GetSharedMaterials(List<Material> list)
        { MaterialListReads++; list.AddRange(Materials); }
        internal void SetPropertyBlock(object? _) { Restores++; }
    }
    internal static class Mathf
    {
        internal static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        internal static float Abs(float value) => Math.Abs(value);
    }
}
internal sealed class HexSelect_Control { internal MeshRenderer? HexProjector; }
internal sealed class HexSelectControlParticles
{
    internal ParticleSystem[]? ParticleBits, ParticleHover;
    internal MeshRenderer? UnseenGroundPlane;
}
internal sealed class TilesOcclusionGenerator { internal readonly List<MeshRenderer> m_RoomRenderers = new(); }
internal sealed class ActorBehaviour { }
internal sealed class CInteractableActor { }

namespace GloomhavenVR.Core
{
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
                driver.NativeSelectionVisuals();
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
            private void NativeSelectionVisuals()
            {
                var selector = new HexSelect_Control();
                var projected = Renderer(Mat("GloomhavenVR/HexDecalStable"));
                projected.Selector = selector; selector.HexProjector = projected;
                Check(IsNativeHexSelectionVisual(projected), "Exact native published selection decal is never wall scenery");
                var masonry = Renderer(Mat("Amp_Basic_WallFade")); masonry.Selector = selector;
                Check(!IsNativeHexSelectionVisual(masonry), "Being near a selector never exempts a real wall mesh");
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
