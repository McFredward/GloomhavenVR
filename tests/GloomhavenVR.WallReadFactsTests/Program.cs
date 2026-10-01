using System;
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
        internal int Queries;
        internal T? GetComponent<T>() where T : class, new()
        { Queries++; return Components.Contains(typeof(T)) ? new T() : null; }
    }
    internal sealed class Animator { }
    internal sealed class MeshRenderer
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
internal sealed class ActorBehaviour { }
internal sealed class CInteractableActor { }

namespace GloomhavenVR.Core
{
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
                return driver._assertions;
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
