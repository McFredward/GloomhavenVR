using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

// This gate is deliberately independent of Unity: its loading/cooldown transitions can be
// exercised in a small focused harness, while the scene identity and readiness come from the
// game's SceneController at each summary boundary.
internal enum CensusDecision { Deferred, Skipped, Sample, NewScene }

internal struct CensusRationer
{
    private bool _sampled;
    private int _sceneHandle;
    private int _procGenHandle;
    private int _skipWindows;

    internal int SkipWindows => _skipWindows;

    internal CensusDecision Decide(bool ready, int sceneHandle, int procGenHandle)
    {
        if (!ready)
            return CensusDecision.Deferred;
        bool newScene = _sampled && (sceneHandle != _sceneHandle
                                     || procGenHandle != _procGenHandle);
        if (_skipWindows > 0 && !newScene)
        {
            _skipWindows--;
            return CensusDecision.Skipped;
        }
        return newScene ? CensusDecision.NewScene : CensusDecision.Sample;
    }

    internal void RecordSample(int sceneHandle, int procGenHandle, double costMs,
        double targetMs, int maxSkippedWindows)
    {
        _sampled = true;
        _sceneHandle = sceneHandle;
        _procGenHandle = procGenHandle;
        _skipWindows = costMs <= targetMs ? 0
            : Math.Min((int)Math.Ceiling(costMs / targetMs) - 1, maxSkippedWindows);
    }
}

internal static class CensusVisibility
{
    internal static bool CountVisible(bool unityVisible, bool forceRenderingOff)
        => unityVisible && !forceRenderingOff;

    internal static bool EstimateSubmitted(bool enabled, bool visible, bool inHeadMask)
        => enabled && visible && inHeadMask;
}

/// <summary>
/// Read-only debug population census. Build612 measured 91–120ms synchronous walks.
/// Build615 traverses loaded scene roots, one object/component at a time, and tallies
/// the original SIM/SCENE/GFX/TEX populations across bounded Update slices. No full
/// Resources/FindObjects census runs here. Counts are observations across the stated
/// sampling span, not a simultaneous snapshot or measured eye draw calls.
/// </summary>
internal static partial class PerfSceneProfile
{
    /// <summary>How many groups the SCENE line names before collapsing the tail into "+N more".</summary>
    private const int TopRoots = 16;

    /// <summary>How many ticking behaviour TYPES the SIM line names before collapsing the tail.</summary>
    private const int TopBehaviourTypes = 14;

    /// <summary>How many shaders the SCENE line names before collapsing the tail.</summary>
    private const int TopShaders = 12;

    /// <summary>
    /// The amortised per-window budget, in milliseconds, this whole walk is allowed to cost.
    ///
    /// <para>The total CPU price is a property of the scene,
    /// not of this code: 8,600 renderers and every MonoBehaviour in the process. Rather than guess
    /// whether that is affordable, it is TIMED and then RATIONED — a window that measured 40 ms
    /// skips the next nine, so the instrument's own share of the session stays at this number no
    /// matter how big the scene gets. The skip is printed, so a sparse SCENE/SIM series in the log
    /// is self-explaining rather than looking like a fault.</para>
    /// </summary>
    private const double AmortiseTargetMs = 4d;

    /// <summary>Hard ceiling on the rationing above, so a pathological sample cannot silence the
    /// instrument for the rest of the session. 8 skipped 30 s windows is ~4.5 minutes.</summary>
    private const int MaxSkippedWindows = 8;

    /// <summary>
    /// Hard ceiling on the parent walk in <see cref="TallyRoot"/>. Game hierarchies here are a
    /// handful of levels deep and the mod's own are under ten; 256 is unreachable in practice and
    /// exists purely so that no hierarchy shape can turn this walk into a hang.
    /// </summary>
    private const int MaxHierarchyDepth = 256;

    /// <summary>Reused across windows so the walk allocates lists once, not once per window.</summary>
    private static readonly List<Material> MaterialScratch = new(8);

    /// <summary>
    /// Distinct shared-material INSTANCE ids among the renderers that actually get submitted.
    /// This is the number that decides whether batching is even possible: the built-in pipeline
    /// can only merge renderers that share a material instance, so N submitted renderers over M
    /// distinct materials have a batch floor of M. M ≈ N means every renderer is its own draw call
    /// no matter what anybody does, and no mod-side change can alter that.
    /// </summary>
    private static readonly HashSet<int> SubmittedMaterials = new(256);

    private static readonly Dictionary<int, RootRec> Roots = new(64);
    private static readonly List<RootRec> RootOrder = new(64);
    private static readonly Dictionary<string, KindRec> Kinds = new(8);
    private static readonly List<KindRec> KindOrder = new(8);
    private static readonly Dictionary<int, ShaderRec> Shaders = new(128);
    private static readonly List<ShaderRec> ShaderOrder = new(128);
    private static readonly int[] LayerCounts = new int[32];
    private static readonly int[] LayerVisible = new int[32];

    /// <summary>The SIM line, built by the same walk and logged just before the SCENE line.</summary>
    private static readonly StringBuilder SimSb = new(4096);

    /// <summary>Cooldown for the sampled scene population.</summary>
    private static CensusRationer _rationer;

    /// <summary>Last measured total walk cost, milliseconds — printed on both lines.</summary>
    private static double _lastWalkMs;


    /// <summary>One scene-root's share of the renderer population.</summary>
    private sealed class RootRec
    {
        public RootRec(string name) => Name = name;

        public readonly string Name;
        public int Count;
        public int Enabled;
        public int Visible;
        public int Materials;
        public int InHeadMask;
    }

    /// <summary>
    /// One RENDERER KIND (MeshRenderer / SkinnedMeshRenderer / ParticleSystemRenderer /
    /// SpriteRenderer / …). The kind is what decides which lever applies at all — a skinned mesh is
    /// an animator's output, a particle renderer is a simulation's output, a static mesh is neither
    /// — so the split has to carry VISIBLE counts and not just totals.
    /// </summary>
    private sealed class KindRec
    {
        public KindRec(string name) => Name = name;

        public readonly string Name;
        public int Count;
        public int Enabled;
        public int Visible;
        public int Submitted;
    }

    /// <summary>One shader's share of the submitted material slots. Keyed by instance id so the
    /// name (an allocating <c>UnityEngine.Object.name</c> read) is taken once per shader.</summary>
    private sealed class ShaderRec
    {
        public ShaderRec(string name) => Name = name;

        public readonly string Name;
        public int Slots;
        public int SubmittedSlots;
    }

    /// <summary>
    /// The scenes the game shows before the main menu: scene 0 (<c>Bootstrap</c>) and
    /// <c>Intro</c> — the same pre-menu gate <c>FlatScreen</c> uses, from the same source
    /// (decompiled <c>GH.Runtime Bootstrap.ShowSplash</c>: Bootstrap → "Intro" →
    /// "Gloomhaven_unified"). Nothing here is worth profiling — hardware 2026-07 counted FIVE
    /// renderers in the intro — and a fault in a walk that runs there cannot be switched off,
    /// because the settings pane the switch lives in does not exist yet.
    /// </summary>
    internal static bool IsPreMenuScene()
    {
        try
        {
            Scene active = SceneManager.GetActiveScene();
            return active.buildIndex <= 0
                   || string.Equals(active.name, "Intro", StringComparison.Ordinal);
        }
        catch (Exception)
        {
            return true;    // cannot tell where we are → do not walk
        }
    }

    // ==========================================================================================
    //  [Perf] SCENE — what is there
    // ==========================================================================================

    /// <summary>
    /// Compose the renderer breakdown. Never throws out to the caller: an instrumentation walk
    /// that takes the summary line down with it is strictly worse than a missing line.
    /// </summary>
    private static IEnumerator SampleSceneLine(StringBuilder sb, int sceneHandle,
        int procGenHandle, string provenance, bool newScene)
    {
        foreach (object? step in InventoryScene()) yield return step;
        _inSim = true;
        SimSb.Length = 0;
        SimSb.Append("SIM — active component population eligible for Unity callbacks "
            + "(sampled ONCE this window, same walk as the SCENE line that follows). "
            + "Counts do not time these callbacks, Unity internal animation or waits; "
            + "compare the aligned NATIVE and SPLIT lines before assigning cost");
        foreach (object? step in AppendBehaviours(SimSb)) yield return step;
        foreach (object? step in AppendAnimators(SimSb)) yield return step;
        foreach (object? step in AppendParticles(SimSb)) yield return step;
        SimSb.Append(provenance);
        _inSim = false;
        yield return null;
        sb.Append("SCENE — renderer/material submission estimate (sampled ONCE this window; "
            + "loaded scene-root traversal sees ACTIVE GameObjects only in these counts; "
            + "SetActive(false) is absent, renderer.enabled/alpha/scale remains present; "
            + "actual draw calls are unmeasured)");
        sb.Append(provenance);
        if (newScene) sb.Append(" | new loaded scene bypassed the prior scene's census cooldown once");
        List<Renderer> all = _inventoryRenderers;
        Camera? head = Rig.VRRigDriver.HeadCamera;
        int headMask = head != null ? head.cullingMask : ~0;
        int modLayer = VRLayers.ModLayer;

        Reset();
        // The texture census rides THIS walk rather than paying for one of its own — the renderers
        // and material slots it needs are already in hand below, and a second FindObjectsOfType over
        // 8,600 renderers would double the most expensive thing in this file. Armed here, fed inside
        // the loop, printed by AppendGfxLine. See PerfTextureCensus for what it answers and why.
        PerfTextureCensus.Begin(head);

        int enabled = 0, visible = 0, inMask = 0, submitted = 0, forcedOff = 0;
        int materialsTotal = 0, materialsSubmitted = 0, instanced = 0;
        int staticBatched = 0, modOwned = 0, modOwnedEnabled = 0;
        int propertyBlocks = 0, propertyBlocksSubmitted = 0;
        int castOff = 0, castOn = 0, castTwoSided = 0, castShadowsOnly = 0;

        for (int i = 0; i < all.Count; i++)
        {
            yield return null;
            Renderer r = all[i];
            if (r == null || !r.gameObject.activeInHierarchy)
                continue;

            int layer = r.gameObject.layer;
            bool on = r.enabled;
            bool forced = r.forceRenderingOff;
            bool vis = CensusVisibility.CountVisible(r.isVisible, forced);
            bool masked = (headMask & (1 << layer)) != 0;
            bool subm = CensusVisibility.EstimateSubmitted(on, vis, masked);
            RecordRoster(r, on, on && vis);

            if (forced)
                forcedOff++;

            LayerCounts[layer]++;
            if (vis)
                LayerVisible[layer]++;
            if (on)
                enabled++;
            if (vis)
                visible++;
            if (masked)
                inMask++;
            if (subm)
                submitted++;
            if (layer == modLayer)
            {
                modOwned++;
                if (on)
                    modOwnedEnabled++;
            }
            if (r.isPartOfStaticBatch)
                staticBatched++;
            // A per-renderer property block is the classic batch-breaker: the renderer can no
            // longer share a draw call with anything else using the same material. Counting them
            // prices the hypothesis directly instead of arguing it — the mod sets them (wall
            // see-through) and so does the game (decals, VFX).
            if (r.HasPropertyBlock())
            {
                propertyBlocks++;
                if (subm)
                    propertyBlocksSubmitted++;
            }

            switch (r.shadowCastingMode)
            {
                case UnityEngine.Rendering.ShadowCastingMode.Off: castOff++; break;
                case UnityEngine.Rendering.ShadowCastingMode.TwoSided: castTwoSided++; break;
                case UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly: castShadowsOnly++; break;
                default: castOn++; break;
            }

            // How big this renderer is in the eye, in the eye's own pixels — 0 for anything that is
            // not submitted or is too small to be part of the "matschige Texturen" complaint. Taken
            // ONCE here so the Renderer.bounds read is not repeated per material slot below.
            float texSpanPx = subm ? PerfTextureCensus.PixelSpan(r) : 0f;

            // GetSharedMaterials fills OUR list; the sharedMaterials PROPERTY would allocate a
            // fresh array per renderer, which at ~1700 renderers is the difference between a
            // sampling hitch and a garbage-collection one.
            int mats;
            try
            {
                r.GetSharedMaterials(MaterialScratch);
                mats = MaterialScratch.Count;
                for (int m = 0; m < MaterialScratch.Count; m++)
                {
                    Material? mat = MaterialScratch[m];
                    if (mat == null)
                        continue;
                    if (mat.enableInstancing)
                        instanced++;
                    if (subm)
                        SubmittedMaterials.Add(mat.GetInstanceID());
                    TallyShader(mat, subm);
                    if (texSpanPx > 0f)
                        PerfTextureCensus.Offer(r, mat, texSpanPx);
                }
            }
            catch (Exception)
            {
                mats = 0; // a renderer with no material array still counts as an object
            }
            materialsTotal += mats;
            if (subm)
                materialsSubmitted += mats;

            TallyKind(r, on, vis, subm);
            TallyRoot(r, on, vis, subm, mats);
        }

        int passes = XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass ? 2 : 1;

        sb.Append(" | totals: ").Append(all.Count).Append(" active renderer(s), ")
          .Append(enabled).Append(" enabled, ").Append(forcedOff)
          .Append(" forceRenderingOff, ").Append(visible)
          .Append(" visible (excluding forceRenderingOff), ")
          .Append(inMask).Append(" inside the head camera's culling mask, ")
          .Append(submitted).Append(" enabled+visible+in-mask candidates")
          .Append(" | material-pass candidates: ").Append(materialsSubmitted)
          .Append(" material slot(s) on those, x").Append(passes)
          .Append(" pass(es) = ~").Append(materialsSubmitted * passes)
          .Append(" potential submissions before batching, shadow passes or depth prepasses "
                  + "(isVisible can mean ANY camera; this is neither an actual draw-call count "
                  + "nor a lower bound)")
          .Append(" | ").Append(materialsTotal).Append(" material slot(s) over all renderers");

        // ---- the batching verdict -------------------------------------------------------------
        // This is the number the 2026-07 investigation turns on. ~5 µs of main-thread time per
        // visible renderer per pass is far too high for pure draw-call submission, which invites
        // the conclusion "something is breaking the batches". Whether ANY batch was ever possible
        // is decided here and not by argument: the built-in pipeline merges only renderers that
        // share a material INSTANCE, so the batch floor is the distinct-material count.
        int distinct = SubmittedMaterials.Count;
        sb.Append(" | batching: ").Append(staticBatched)
          .Append(" renderer(s) report isPartOfStaticBatch, ").Append(instanced)
          .Append(" material slot(s) have GPU instancing enabled, ").Append(propertyBlocks)
          .Append(" renderer(s) carry a MaterialPropertyBlock (").Append(propertyBlocksSubmitted)
          .Append(" of them submitted — a property block stops that renderer from sharing a draw "
                  + "call, and both the mod's wall see-through and the game's own decals/VFX set "
                  + "them), ").Append(distinct)
          .Append(" DISTINCT material instance(s) across the ").Append(submitted)
          .Append(" submitted renderer(s)");
        if (submitted > 0)
        {
            float perMaterial = distinct > 0 ? submitted / (float)distinct : 0f;
            sb.Append(" = ").Append(perMaterial.ToString("F1"))
              .Append(" renderer(s) per material. ")
              .Append(perMaterial < 1.5f
                  ? "AT OR NEAR 1: every submitted renderer has its own material instance, so "
                    + "there is NO batch for anything to break — not the mod's property blocks, not "
                    + "its render-queue bumps, not its shader swaps. The submission count is the "
                    + "scene's own and no mod-side material change can reduce it; the only levers "
                    + "left are FEWER OBJECTS or FEWER PASSES."
                  : "ABOVE 1: renderers do share materials, so batching is at least possible and "
                    + "the property-block/render-queue count above is worth an A/B — if it were "
                    + "not for the property blocks and queue bumps, these could merge.");
        }
        // These are actual Renderer batch flags. Historical experiments and the native
        // hotkey are not proof of which current optional pass authored them.
        if (staticBatched == 0)
            sb.Append(" NOTHING is statically batched (the game only ever calls "
                      + "StaticBatchingUtility.Combine from a debug hotkey; compare optional scenario "
                      + "batching separately rather than infer an enabled optimisation from this count).");
        else
            sb.Append(" — renderer-reported static-batch membership; compare configured "
                      + "scenario batching and the native debug-hotkey StaticBatchingUtility.Combine.");

        sb.Append(" | mod-owned (layer ").Append(modLayer).Append("): ").Append(modOwned)
          .Append(" renderer(s), ").Append(modOwnedEnabled).Append(" enabled (")
          .Append(all.Count > 0 ? (100f * modOwned / all.Count).ToString("F1") : "0")
          .Append("% of the scene)");

        sb.Append(" | shadow casting: ").Append(castOn).Append(" On, ").Append(castOff)
          .Append(" Off, ").Append(castTwoSided).Append(" TwoSided, ").Append(castShadowsOnly)
          .Append(" ShadowsOnly (only meaningful while QualitySettings.shadows is enabled — see "
                  + "the GFX line)");

        AppendKinds(sb);
        AppendShaders(sb, passes);
        AppendLayers(sb, headMask, all.Count);
        AppendRoots(sb, passes);
        yield return null;
        foreach (object? step in PrepareGraphics()) yield return step;
        foreach (object? step in AppendLights(_gfxLights)) yield return step;
        foreach (object? step in PerfTextureCensus.PrepareLine()) yield return step;
        FinishWalk(sb, _sampleCpu, _simMs, sceneHandle, procGenHandle);
    }

    /// <summary>
    /// Stop the clock, print the instrument's OWN price on both lines, ration the next few windows
    /// if it was expensive, and emit the SIM line. The pump passes a stopwatch that
    /// runs only around work units; time between frames is deliberately excluded.
    /// Failed/cancelled samples never publish an apparently complete population.
    /// </summary>
    private static void FinishWalk(StringBuilder sb, Stopwatch clock, double simMs,
        int sceneHandle, int procGenHandle)
    {
        clock.Stop();
        _lastWalkMs = clock.Elapsed.TotalMilliseconds;
        double sceneMs = Math.Max(0d, _lastWalkMs - simMs);

        // Rationing. Ceil(cost / target) - 1 is "how many windows this one sample has to be spread
        // over"; at or under the target it is 0 and every window is sampled.
        _rationer.RecordSample(sceneHandle, procGenHandle, _lastWalkMs,
            AmortiseTargetMs, MaxSkippedWindows);

        StringBuilder cost = new(320);
        cost.Append(" | INSTRUMENT COST, measured not asserted: this whole sample took ")
            .Append(_lastWalkMs.ToString("F1")).Append("ms (SIM half ").Append(simMs.ToString("F1"))
            .Append("ms, SCENE half ").Append(sceneMs.ToString("F1"))
            .Append("ms) across ").Append(_sampleSlices).Append(" bounded slice(s), ")
            .Append((Time.unscaledTime - _sampleStart).ToString("F2"))
            .Append("s observation span; max slice ").Append(_maxSliceMs.ToString("F3"))
            .Append("ms, max atomic work unit ").Append(_maxUnitMs.ToString("F3"))
            .Append("ms, budget ").Append(_budgetMilliseconds.ToString("F3"))
            .Append("ms or ").Append(_objectsPerFrame)
            .Append(" units/frame (a Unity API call cannot be preempted)");
        if (_rationer.SkipWindows > 0)
        {
            cost.Append(" — above the ").Append(AmortiseTargetMs.ToString("F0"))
                .Append("ms/window budget, so the next ").Append(_rationer.SkipWindows)
                .Append(" window(s) for this same scene are skipped (unless a new scene loads)");
        }
        else
        {
            cost.Append(" — at or under the ").Append(AmortiseTargetMs.ToString("F0"))
                .Append("ms/window budget, so every window is sampled");
        }
        string costText = cost.ToString();

        sb.Append(costText);
        if (SimSb.Length > 0)
        {
            SimSb.Append(costText);
            VRLog.Info("Perf", SimSb.ToString());
            SimSb.Length = 0;
        }
    }

    private static void Reset()
    {
        Roots.Clear();
        RootOrder.Clear();
        SubmittedMaterials.Clear();
        Kinds.Clear();
        KindOrder.Clear();
        Shaders.Clear();
        ShaderOrder.Clear();
        Array.Clear(LayerCounts, 0, LayerCounts.Length);
        Array.Clear(LayerVisible, 0, LayerVisible.Length);
    }

    /// <summary>
    /// Tally by renderer KIND — mesh / skinned / particle / sprite / line / trail — carrying the
    /// visible and submitted counts, not just the total. Which lever applies depends entirely on
    /// this split: SkinnedMeshRenderers are what an <see cref="Animator"/> writes into and are the
    /// only renderers animator culling can help; ParticleSystemRenderers are what particle culling
    /// can help; a plain MeshRenderer is neither, and if the population is overwhelmingly plain
    /// meshes then both Part-2 levers are the wrong shape no matter what the totals say.
    /// </summary>
    private static void TallyKind(Renderer r, bool on, bool vis, bool submitted)
    {
        // Type.Name is cached on the runtime type object — no allocation per read.
        string name = r.GetType().Name;
        if (!Kinds.TryGetValue(name, out KindRec rec))
        {
            rec = new KindRec(name);
            Kinds[name] = rec;
            KindOrder.Add(rec);
        }
        rec.Count++;
        if (on)
            rec.Enabled++;
        if (vis)
            rec.Visible++;
        if (submitted)
            rec.Submitted++;
    }

    /// <summary>
    /// Tally material slots by SHADER. Keyed on the shader's instance id so <c>Shader.name</c> —
    /// which allocates a fresh string on every read, like every other
    /// <see cref="UnityEngine.Object"/> name — is read once per distinct shader instead of once per
    /// material slot, i.e. a few dozen times rather than ~13,000.
    ///
    /// <para>WHY SHADERS AND NOT JUST MATERIALS: the material count already on this line prices
    /// BATCHING. The shader histogram prices something else — which shaders the frame is actually
    /// made of, so "the board is 6,000 slots of one masonry shader" and "the board is 6,000 slots
    /// of forty different ones" stop looking identical. It is also how the mod's own bundled
    /// shaders become visible as a share of the frame rather than an assumption about one.</para>
    /// </summary>
    /// <returns>The material's shader — the resolved reference, so a caller that needs it does not
    /// pay for a second <c>Material.shader</c> marshal on every one of the scene's ~2,500 material
    /// slots. Null when it could not be read.</returns>
    private static Shader? TallyShader(Material mat, bool submitted)
    {
        Shader? sh;
        try
        {
            sh = mat.shader;
        }
        catch (Exception)
        {
            return null;    // a material whose shader failed to load still counted as a slot above
        }
        if (sh == null)
            return null;

        int id = sh.GetInstanceID();
        if (!Shaders.TryGetValue(id, out ShaderRec rec))
        {
            rec = new ShaderRec(sh.name);
            Shaders[id] = rec;
            ShaderOrder.Add(rec);
        }
        rec.Slots++;
        if (submitted)
            rec.SubmittedSlots++;
        return sh;
    }

    /// <summary>
    /// Group by <c>&lt;scene root&gt;/&lt;its direct child&gt;</c>, not by scene root alone. The game parents
    /// EVERY generated room under one root object and every prop under another (verified in the
    /// decompiled <c>RoomVisibilityManager</c>/<c>Choreographer</c>: the roots are literally "Maps"
    /// and "Props"), so grouping by root would collapse the entire board into a single bucket and
    /// answer nothing. One level down is where the rooms, the walls container and the mod's own
    /// subsystems become separable — and it is still only a few dozen buckets, so the name reads
    /// stay negligible.
    ///
    /// <para>THE ASCENT MUST ADVANCE. The first version of this walk read the parent ONCE before
    /// the loop and then re-tested that same cached reference forever, so every renderer nested
    /// three levels or deeper — which includes every hand mesh under
    /// <c>VRRig/VRHand/HandRoot/…</c> — spun the main thread until the process was killed. It froze
    /// the game at the first window close with no exception and no log line, i.e. exactly the
    /// symptom a hang has. The loop below therefore advances <c>node</c> and re-reads its parent
    /// each turn, and is additionally bounded: an instrumentation walk is never allowed to be the
    /// thing that stops the frame.</para>
    /// </summary>
    private static void TallyRoot(Renderer r, bool on, bool vis, bool submitted, int mats)
    {
        Transform node = r.transform;
        Transform root = node;
        // Depth guard, not a correctness device: Unity forbids transform cycles, so this cannot
        // trigger on a well-formed hierarchy. It is here so that a malformed one costs a wrong
        // group name instead of a frozen main thread.
        for (int depth = 0; depth < MaxHierarchyDepth; depth++)
        {
            Transform? parent = node.parent;
            if (parent == null)
                break;              // node has no parent → node IS the scene root
            root = parent;
            if (parent.parent == null)
                break;              // parent is the scene root → node is its direct child
            node = parent;
        }

        int id = node.GetInstanceID();
        if (!Roots.TryGetValue(id, out RootRec rec))
        {
            // The ONLY place names are read: UnityEngine.Object.name allocates a fresh string on
            // every access, so this runs once per GROUP instead of once per renderer.
            rec = new RootRec(ReferenceEquals(node, root) ? node.name : root.name + "/" + node.name);
            Roots[id] = rec;
            RootOrder.Add(rec);
        }
        rec.Count++;
        if (on)
            rec.Enabled++;
        if (vis)
            rec.Visible++;
        if (submitted)
        {
            rec.InHeadMask++;
            rec.Materials += mats;
        }
    }

    private static void AppendKinds(StringBuilder sb)
    {
        KindOrder.Sort(static (a, b) => b.Count.CompareTo(a.Count));
        sb.Append(" | by renderer KIND (total/enabled/visible/submitted — a skinned mesh is an "
                  + "Animator's output and a particle renderer is a simulation's output, so this "
                  + "split is what says whether a simulation-side lever can apply at all):");
        for (int i = 0; i < KindOrder.Count; i++)
        {
            KindRec k = KindOrder[i];
            sb.Append(i == 0 ? " " : ", ").Append(k.Name).Append(' ').Append(k.Count).Append('/')
              .Append(k.Enabled).Append('/').Append(k.Visible).Append('/').Append(k.Submitted);
        }
        if (KindOrder.Count == 0)
            sb.Append(" none");
    }

    /// <summary>
    /// Material slots grouped by SHADER, ranked by the slots that are actually submitted. The
    /// distinct-material count above prices batching; this prices COMPOSITION — whether the frame is
    /// six thousand slots of one masonry shader or six thousand slots of forty different ones, which
    /// are the same number and completely different problems.
    /// </summary>
    private static void AppendShaders(StringBuilder sb, int passes)
    {
        ShaderOrder.Sort(static (a, b) =>
        {
            int bySubmitted = b.SubmittedSlots.CompareTo(a.SubmittedSlots);
            return bySubmitted != 0 ? bySubmitted : b.Slots.CompareTo(a.Slots);
        });
        sb.Append(" | by SHADER, ranked by submitted material slots (name: submitted/total slots; "
                  + "each submitted slot is paid ").Append(passes).Append("x per frame):");
        int shown = Mathf.Min(TopShaders, ShaderOrder.Count);
        for (int i = 0; i < shown; i++)
        {
            ShaderRec s = ShaderOrder[i];
            sb.Append(i == 0 ? " " : ", ").Append(s.Name).Append(": ").Append(s.SubmittedSlots)
              .Append('/').Append(s.Slots);
        }
        if (ShaderOrder.Count > shown)
        {
            int restSubmitted = 0, restSlots = 0;
            for (int i = shown; i < ShaderOrder.Count; i++)
            {
                restSubmitted += ShaderOrder[i].SubmittedSlots;
                restSlots += ShaderOrder[i].Slots;
            }
            sb.Append(", + ").Append(ShaderOrder.Count - shown).Append(" more shader(s) totalling ")
              .Append(restSubmitted).Append('/').Append(restSlots).Append(" slot(s)");
        }
        if (ShaderOrder.Count == 0)
            sb.Append(" none");
        else
            sb.Append(" (").Append(ShaderOrder.Count).Append(" distinct shader(s))");
    }

    /// <summary>
    /// Per-layer counts with the layer's NAME and whether the head camera renders it. This is the
    /// whole basis for culling-mask hygiene: a layer the head camera does not need is a slice of
    /// the scene that stops being culled and submitted, twice under MultiPass — but only a layer
    /// named here with a real renderer count is worth touching, and the decision needs the name.
    /// </summary>
    private static void AppendLayers(StringBuilder sb, int headMask, int total)
    {
        sb.Append(" | by layer (name, renderers, visible, and whether the head camera renders it):");
        bool any = false;
        for (int layer = 0; layer < 32; layer++)
        {
            if (LayerCounts[layer] == 0)
                continue;
            string name = LayerMask.LayerToName(layer);
            sb.Append(any ? ", " : " ").Append(layer).Append('=')
              .Append(string.IsNullOrEmpty(name) ? "<unnamed>" : name)
              .Append(' ').Append(LayerCounts[layer])
              .Append('/').Append(LayerVisible[layer]).Append("vis ")
              .Append((headMask & (1 << layer)) != 0 ? "RENDERED" : "culled-by-mask");
            any = true;
        }
        if (!any)
            sb.Append(" none");
        sb.Append(" (of ").Append(total).Append(" active renderers)");
    }

    /// <summary>
    /// The answer to "what ARE those renderers": the scene roots that own them, ranked by the
    /// count that actually costs (enabled + visible + inside the head mask), with their material
    /// slots so a root's share of the draw-call floor is readable directly.
    /// </summary>
    private static void AppendRoots(StringBuilder sb, int passes)
    {
        RootOrder.Sort(CompareRootDesc);
        sb.Append(" | by scene-root/child group, ranked by submitted renderers "
                  + "(name: submitted/enabled/total, material slots):");
        int shown = Mathf.Min(TopRoots, RootOrder.Count);
        for (int i = 0; i < shown; i++)
        {
            RootRec r = RootOrder[i];
            sb.Append(i == 0 ? " " : ", ").Append(r.Name).Append(": ")
              .Append(r.InHeadMask).Append('/').Append(r.Enabled).Append('/').Append(r.Count)
              .Append(", ").Append(r.Materials).Append("mat");
        }
        if (RootOrder.Count > shown)
        {
            int restRenderers = 0, restMaterials = 0;
            for (int i = shown; i < RootOrder.Count; i++)
            {
                restRenderers += RootOrder[i].InHeadMask;
                restMaterials += RootOrder[i].Materials;
            }
            sb.Append(", + ").Append(RootOrder.Count - shown).Append(" more group(s) totalling ")
              .Append(restRenderers).Append(" submitted renderer(s), ").Append(restMaterials)
              .Append("mat");
        }
        sb.Append(" (").Append(RootOrder.Count).Append(" group(s); every material slot here is "
                  + "paid ").Append(passes).Append("x per frame)");
    }

    private static int CompareRootDesc(RootRec a, RootRec b)
    {
        int byMask = b.InHeadMask.CompareTo(a.InHeadMask);
        return byMask != 0 ? byMask : b.Count.CompareTo(a.Count);
    }

    // ==========================================================================================
    //  [Perf] SIM — what the main-thread loop iterates over
    // ==========================================================================================

    /// <summary>
    /// Below this many <see cref="AnimatorCullingMode.AlwaysAnimate"/> animators, an animator-culling
    /// lever cannot pay for itself and must not be shipped.
    ///
    /// <para>WHERE THE NUMBER COMES FROM, so it is a threshold and not a vibe: Unity evaluates a
    /// generic animator in single-digit microseconds and a humanoid one (retarget + IK) in low tens.
    /// At 200 animators, culling EVERY one of them recovers roughly 1–4 ms — and the ZOOM clause on
    /// the <c>[Perf] SPLIT</c> line sets its own noise floor at 1.0 ms on the grounds that a
    /// compositor-quantised frame steps in whole budgets. A lever whose best case sits at the
    /// instrument's noise floor is a lever that cannot be shown to have worked, which is the one
    /// thing this project has repeatedly paid for.</para>
    /// </summary>
    private const int AnimatorLeverFloor = 200;

    /// <summary>The same threshold for particle systems, on the same reasoning.</summary>
    private const int ParticleLeverFloor = 100;

    /// <summary>Which per-frame Unity messages a behaviour TYPE declares.</summary>
    [Flags]
    private enum TickKind
    {
        None = 0,
        Update = 1,
        LateUpdate = 2,
        FixedUpdate = 4,
    }

    /// <summary>
    /// Per-type reflection result, cached for the life of the process. A <see cref="Type"/>'s method
    /// table cannot change at runtime, so this is computed once per distinct behaviour type ever
    /// seen and never recomputed — which is what makes the SIM walk affordable from the second
    /// window onwards, where the first one pays a few thousand <see cref="Type.GetMethod(string, BindingFlags)"/>
    /// calls and every one after it pays a dictionary lookup.
    /// </summary>
    private static readonly Dictionary<Type, TickKind> TickKinds = new(1024);

    private static readonly Dictionary<Type, BehaviourRec> Behaviours = new(512);
    private static readonly List<BehaviourRec> BehaviourOrder = new(512);

    /// <summary>One behaviour TYPE's instance count. "12,000 instances of X" names a lever in one line.</summary>
    private sealed class BehaviourRec
    {
        public BehaviourRec(string name, TickKind ticks, bool modOwned)
        {
            Name = name;
            Ticks = ticks;
            ModOwned = modOwned;
        }

        public readonly string Name;
        public readonly TickKind Ticks;
        public readonly bool ModOwned;
        public int Count;
        public int Enabled;
    }

    /// <summary>
    /// Build the <c>[Perf] SIM</c> line into <see cref="SimSb"/> and return the milliseconds it
    /// cost. Never throws out to the caller and latches itself off after one fault, because the
    /// SCENE line it shares a walk with must survive a defect in this half.
    ///
    /// <para>WHY THIS LINE EXISTS AT ALL. <c>[Perf] SPLIT</c> puts ~50 % of a 43 ms frame in
    /// main-thread logic and the mod at 14.6 % of the total, i.e. the GAME'S OWN
    /// <c>Update</c>/<c>LateUpdate</c> is the single biggest thing in the frame — and until this
    /// line, every instrument in the mod measured RENDERERS, which are the other half of the frame.
    /// Eight windows of renderer counts cannot name one behaviour.</para>
    ///
    /// <para>WHAT MAKES THE UPDATE COUNT EXACT RATHER THAN A PROXY. Unity does not call
    /// <c>Update</c> on every Behaviour; it maintains a list of exactly those whose script type
    /// DECLARES the method, and walks that list once per frame. So "N enabled behaviours whose type
    /// declares Update" is not an estimate of the loop — it IS the loop's length. The reflection
    /// walks base types explicitly (with <see cref="BindingFlags.DeclaredOnly"/> at each level)
    /// because a private <c>Update</c> inherited from a base class is invisible to a single
    /// <see cref="Type.GetMethod(string, BindingFlags)"/> call, and the game's actor and procedural
    /// hierarchies are exactly that shape (<c>ProceduralMapTile</c> overrides
    /// <c>ProceduralTileObserver.Update</c>).</para>
    /// </summary>
    /// <summary>
    /// The behaviour census: how long Unity's per-frame script lists actually are, and which TYPES
    /// fill them.
    /// </summary>
    private static IEnumerable AppendBehaviours(StringBuilder sb)
    {
        List<MonoBehaviour> all = _inventoryBehaviours;

        Behaviours.Clear();
        BehaviourOrder.Clear();

        int total = 0, enabled = 0;
        int upd = 0, late = 0, fixedUpd = 0;
        int modUpd = 0, modLate = 0;
        int tickingTypes = 0;

        for (int i = 0; i < all.Count; i++)
        {
            yield return null;
            MonoBehaviour mb = all[i];
            if (mb == null || !mb.gameObject.activeInHierarchy)
                continue;
            total++;
            bool on = mb.isActiveAndEnabled;
            if (on)
                enabled++;

            Type t = mb.GetType();
            TickKind ticks = TicksOf(t);
            if (ticks == TickKind.None)
                continue;

            if (!Behaviours.TryGetValue(t, out BehaviourRec rec))
            {
                // Type.Namespace/Name are read ONCE per distinct type, not once per instance.
                string? ns = t.Namespace;
                bool mine = ns != null && ns.StartsWith("GloomhavenVR", StringComparison.Ordinal);
                rec = new BehaviourRec(t.Name, ticks, mine);
                Behaviours[t] = rec;
                BehaviourOrder.Add(rec);
                tickingTypes++;
            }
            rec.Count++;
            if (!on)
                continue;

            rec.Enabled++;
            // Only ENABLED behaviours are on Unity's lists, so only they are counted into the
            // loop lengths. A disabled one costs nothing per frame, however many there are.
            if ((ticks & TickKind.Update) != 0)
            {
                upd++;
                if (rec.ModOwned)
                    modUpd++;
            }
            if ((ticks & TickKind.LateUpdate) != 0)
            {
                late++;
                if (rec.ModOwned)
                    modLate++;
            }
            if ((ticks & TickKind.FixedUpdate) != 0)
                fixedUpd++;
        }

        sb.Append(" | behaviours: ").Append(total)
          .Append(" MonoBehaviour(s) on ACTIVE GameObjects, ").Append(enabled)
          .Append(" of them enabled")
          .Append(" | PER-FRAME LISTS (callback-eligible enabled behaviours whose type "
                  + "declares the method; counts do not measure time): Update ").Append(upd)
          .Append(", LateUpdate ").Append(late).Append(", FixedUpdate ").Append(fixedUpd)
          .Append(" — of which the mod's own are ").Append(modUpd).Append(" Update and ")
          .Append(modLate).Append(" LateUpdate (").Append(upd > 0 ? (100f * modUpd / upd).ToString("F1") : "0")
          .Append("% of the Update population); other components may be game or third-party code")
          .Append(" | ").Append(tickingTypes).Append(" distinct TICKING type(s)");

        BehaviourOrder.Sort(static (a, b) =>
        {
            int byEnabled = b.Enabled.CompareTo(a.Enabled);
            return byEnabled != 0 ? byEnabled : b.Count.CompareTo(a.Count);
        });
        sb.Append(" | heaviest ticking TYPES by instance count (name: enabled/total [which "
                  + "messages]; * = the mod's own):");
        int shown = Mathf.Min(TopBehaviourTypes, BehaviourOrder.Count);
        for (int i = 0; i < shown; i++)
        {
            BehaviourRec r = BehaviourOrder[i];
            sb.Append(i == 0 ? " " : ", ");
            if (r.ModOwned)
                sb.Append('*');
            sb.Append(r.Name).Append(": ").Append(r.Enabled).Append('/').Append(r.Count).Append(" [");
            bool first = true;
            if ((r.Ticks & TickKind.Update) != 0) { sb.Append("U"); first = false; }
            if ((r.Ticks & TickKind.LateUpdate) != 0) { sb.Append(first ? "" : "+").Append("L"); first = false; }
            if ((r.Ticks & TickKind.FixedUpdate) != 0) { sb.Append(first ? "" : "+").Append("F"); }
            sb.Append(']');
        }
        if (BehaviourOrder.Count > shown)
        {
            int rest = 0;
            for (int i = shown; i < BehaviourOrder.Count; i++)
                rest += BehaviourOrder[i].Enabled;
            sb.Append(", + ").Append(BehaviourOrder.Count - shown)
              .Append(" more ticking type(s) totalling ").Append(rest).Append(" enabled instance(s)");
        }
        if (BehaviourOrder.Count == 0)
            sb.Append(" none");
    }

    /// <summary>
    /// Whether a behaviour type declares any of Unity's per-frame messages. Cached forever; walks
    /// the base chain with <see cref="BindingFlags.DeclaredOnly"/> at each level because a
    /// <c>private void Update()</c> on a base class is not returned by a single
    /// <see cref="Type.GetMethod(string, BindingFlags)"/> on the derived type, and missing those
    /// would under-count exactly the deepest hierarchies (the game's actors and procedural tiles).
    /// </summary>
    private static TickKind TicksOf(Type type)
    {
        if (TickKinds.TryGetValue(type, out TickKind cached))
            return cached;

        TickKind kind = TickKind.None;
        try
        {
            for (Type? cur = type;
                 cur != null && cur != typeof(MonoBehaviour) && cur != typeof(Behaviour)
                 && cur != typeof(Component) && cur != typeof(UnityEngine.Object);
                 cur = cur.BaseType)
            {
                if (Declares(cur, "Update"))
                    kind |= TickKind.Update;
                if (Declares(cur, "LateUpdate"))
                    kind |= TickKind.LateUpdate;
                if (Declares(cur, "FixedUpdate"))
                    kind |= TickKind.FixedUpdate;
            }
        }
        catch (Exception)
        {
            // A type whose base chain cannot be walked (a broken assembly reference, a generic
            // definition) is recorded as non-ticking rather than retried every window: the answer
            // will not get better, and the cache is what keeps this walk affordable.
            kind = TickKind.None;
        }

        TickKinds[type] = kind;
        return kind;
    }

    private static bool Declares(Type type, string method)
    {
        try
        {
            return type.GetMethod(method,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.DeclaredOnly) != null;
        }
        catch (AmbiguousMatchException)
        {
            return true;    // more than one overload IS a declaration
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// THE ANIMATOR CENSUS — the number that decides whether animator culling is a lever or a
    /// coincidence.
    ///
    /// <para>THE CLAIM UNDER TEST. The <c>[Perf] SPLIT</c> line's ZOOM verdict concludes, whenever
    /// most of a near/far delta lands in the logic span, that the cost is <i>"view-scaled
    /// SIMULATION — animators, particle systems and isVisible-gated scripts that stop being culled
    /// as the head rises"</i>. Animator evaluation genuinely does land inside that span (Unity's
    /// animation update runs between <c>Update</c> and <c>LateUpdate</c>, which is exactly where the
    /// mod's logic span is measured), so the sentence is at least well-typed. Whether it is TRUE
    /// depends entirely on <see cref="Animator.cullingMode"/>, and nothing had ever read it.</para>
    ///
    /// <para>TWO WAYS THE LEVER DIES, both printed here rather than argued:</para>
    /// <list type="number">
    /// <item>If the population is dominated by <see cref="AnimatorCullingMode.AlwaysAnimate"/>, then
    /// NO animator in this build stops ticking when it leaves the screen — so the named mechanism
    /// does not exist, and any correlation between frame time and the visible-renderer count is a
    /// coincidence of POPULATION (opening a room activates its renderers and its animators at the
    /// same moment) rather than of visibility.</item>
    /// <item>If the population is merely SMALL (see <see cref="AnimatorLeverFloor"/>), the lever
    /// cannot pay for itself even in its best case, and shipping it would produce a change nobody
    /// can measure — which is indistinguishable from shipping nothing, except that it also carries
    /// the risk.</item>
    /// </list>
    ///
    /// <para>AND ONE STRUCTURAL LIMIT WORTH PRINTING EVERY TIME: Unity decides animator culling from
    /// the visibility of the RENDERERS bound to that animator. An animator with no renderer under it
    /// is animated unconditionally in every mode — which is every uGUI animator in this game, because
    /// a <c>CanvasRenderer</c> is not a <see cref="Renderer"/>. The count of animators that own no
    /// renderer is therefore the count that no culling mode can ever touch, and it is reported.</para>
    /// </summary>
    private static IEnumerable AppendAnimators(StringBuilder sb)
    {
        List<Animator> all = _inventoryAnimators;

        int enabled = 0, controller = 0, human = 0, rootMotion = 0, layers = 0, noRenderer = 0;
        int always = 0, cullTransforms = 0, cullCompletely = 0, otherMode = 0;

        for (int i = 0; i < all.Count; i++)
        {
            yield return null;
            Animator a = all[i];
            if (a == null || !a.gameObject.activeInHierarchy)
                continue;
            bool on = a.isActiveAndEnabled;
            if (on)
                enabled++;
            try
            {
                if (a.runtimeAnimatorController != null)
                    controller++;
                if (a.isHuman)
                    human++;
                if (a.applyRootMotion)
                    rootMotion++;
                layers += a.layerCount;
            }
            catch (Exception)
            {
                // An animator without an avatar/controller answers some of these with a throw;
                // it still counts as an animator, which is the number this line is about.
            }

            switch (a.cullingMode)
            {
                case AnimatorCullingMode.AlwaysAnimate: always++; break;
                case AnimatorCullingMode.CullUpdateTransforms: cullTransforms++; break;
                case AnimatorCullingMode.CullCompletely: cullCompletely++; break;
                default: otherMode++; break;
            }

            // GetComponentInChildren is a subtree walk, so it is only done for the animators that
            // are enabled and could therefore cost anything — and it answers the one question no
            // culling mode can override.
            if (on && !_rendererAncestors.Contains(a.transform.GetInstanceID()))
                noRenderer++;
        }

        sb.Append(" | ANIMATORS: ").Append(all.Count).Append(" on active GameObject(s), ")
          .Append(enabled).Append(" enabled, ").Append(controller)
          .Append(" with a runtime controller, ").Append(human).Append(" humanoid (retarget+IK), ")
          .Append(rootMotion).Append(" with root motion, ").Append(layers).Append(" layer(s) total")
          .Append(" | cullingMode: AlwaysAnimate ").Append(always)
          .Append(", CullUpdateTransforms ").Append(cullTransforms)
          .Append(", CullCompletely ").Append(cullCompletely);
        if (otherMode > 0)
            sb.Append(", other ").Append(otherMode);
        sb.Append(" | ").Append(noRenderer)
          .Append(" enabled animator(s) own NO Renderer at all and are therefore animated "
                  + "unconditionally in EVERY mode (Unity culls an animator against the visibility "
                  + "of the renderers bound to it, and a CanvasRenderer is not a Renderer) — no "
                  + "culling lever can ever touch those");

        // ---- the verdict, which is the whole point of the line ---------------------------------
        sb.Append(" | VERDICT: ");
        if (enabled == 0)
        {
            sb.Append("no enabled animators in this scene at all — the ZOOM clause's "
                      + "'view-scaled animators' cannot be what this frame is made of.");
            yield break;
        }

        bool uncullable = always >= enabled - noRenderer;
        if (uncullable)
        {
            sb.Append("NOT ONE animator in this scene is view-culled — all ").Append(always)
              .Append(" are at Unity's default AlwaysAnimate, and the game's own code never writes "
                      + "Animator.cullingMode anywhere. The [Perf] SPLIT line's ZOOM verdict names "
                      + "'animators … that stop being culled as the head rises' as its mechanism: "
                      + "THAT MECHANISM DOES NOT EXIST IN THIS BUILD. Any correlation between frame "
                      + "time and the visible-renderer count is therefore a coincidence of "
                      + "POPULATION — revealing a room activates its renderers and its animators in "
                      + "the same SetActive — not of visibility. ");
        }
        else
        {
            sb.Append(cullTransforms + cullCompletely)
              .Append(" animator(s) ARE view-culled already, so part of the logic span genuinely "
                      + "does scale with what the head sees. ");
        }

        int addressable = Mathf.Max(0, always - noRenderer);
        if (addressable < AnimatorLeverFloor)
        {
            sb.Append("AND THE LEVER IS STILL NOT WORTH SHIPPING: only ").Append(addressable)
              .Append(" animator(s) could be culled at all, which is under the ")
              .Append(AnimatorLeverFloor).Append(" this instrument sets as the floor (at single- to "
                      + "low-tens-of-microseconds per animator, culling every one of them recovers "
                      + "about a millisecond — the ZOOM clause's own noise floor). Whatever owns "
                      + "the ~50% of this frame that is main-thread logic, it is on the behaviour "
                      + "list above, not in the animators.");
        }
        else
        {
            sb.Append("The addressable population is ").Append(addressable)
              .Append(" animator(s), which is above the ").Append(AnimatorLeverFloor)
              .Append(" floor — an animator-culling lever is worth pricing. It must be "
                      + "CullUpdateTransforms and never CullCompletely: this game stores door "
                      + "open/closed state AS the animator's current state, its turn sequencer polls "
                      + "animator states from Choreographer.Update, and 32 StateMachineBehaviour "
                      + "classes run authoritative logic (one of them SENDS A NETWORK MESSAGE) from "
                      + "animator callbacks, so an animator that stops advancing stalls the turn.");
        }
    }

    /// <summary>
    /// The particle census. Same two questions as the animators — is it culled, and is there enough
    /// of it to matter — with one difference that decides the lever before it is built: Unity's
    /// DEFAULT for <see cref="ParticleSystemCullingMode"/> is already
    /// <see cref="ParticleSystemCullingMode.Automatic"/>, i.e. pause-and-catch-up wherever the
    /// system can be resimulated retroactively. So unlike the animators, the "leave it alone" state
    /// here is already the optimised one, and a lever would only be adding risk unless this census
    /// shows a large AlwaysSimulate population.
    /// </summary>
    private static IEnumerable AppendParticles(StringBuilder sb)
    {
        List<ParticleSystem> all = _inventoryParticles;

        int playing = 0, emitting = 0, live = 0, looping = 0;
        int automatic = 0, pauseCatchup = 0, pause = 0, alwaysSimulate = 0;

        for (int i = 0; i < all.Count; i++)
        {
            yield return null;
            ParticleSystem p = all[i];
            if (p == null || !p.gameObject.activeInHierarchy)
                continue;
            try
            {
                if (p.isPlaying)
                    playing++;
                if (p.isEmitting)
                    emitting++;
                live += p.particleCount;
                ParticleSystem.MainModule main = p.main;
                if (main.loop)
                    looping++;
                switch (main.cullingMode)
                {
                    case ParticleSystemCullingMode.Automatic: automatic++; break;
                    case ParticleSystemCullingMode.PauseAndCatchup: pauseCatchup++; break;
                    case ParticleSystemCullingMode.Pause: pause++; break;
                    default: alwaysSimulate++; break;
                }
            }
            catch (Exception)
            {
                // one unreadable system must not cost the census
            }
        }

        sb.Append(" | PARTICLES: ").Append(all.Count).Append(" system(s) on active GameObject(s), ")
          .Append(playing).Append(" playing, ").Append(emitting).Append(" emitting, ").Append(looping)
          .Append(" looping, ").Append(live).Append(" live particle(s)")
          .Append(" | cullingMode: Automatic ").Append(automatic).Append(", PauseAndCatchup ")
          .Append(pauseCatchup).Append(", Pause ").Append(pause).Append(", AlwaysSimulate ")
          .Append(alwaysSimulate)
          .Append(" | VERDICT: ");

        if (alwaysSimulate < ParticleLeverFloor)
        {
            sb.Append("no lever here. Unity's DEFAULT is Automatic — which already pauses and "
                      + "catches up every system that can be resimulated retroactively — and only ")
              .Append(alwaysSimulate).Append(" system(s) are at AlwaysSimulate, under the ")
              .Append(ParticleLeverFloor).Append(" floor. Forcing Pause on top of that would buy "
                      + "nothing and would risk a real softlock: the game gates END OF TURN on "
                      + "ParticleSystem.IsAlive() for its active buff effects, and a paused system "
                      + "stays alive forever.");
        }
        else
        {
            sb.Append(alwaysSimulate).Append(" system(s) simulate unconditionally, which is above "
                      + "the ").Append(ParticleLeverFloor).Append(" floor and worth pricing — but "
                      + "NEVER for the buff effects the Choreographer gates end-of-turn on via "
                      + "IsAlive(), and PauseAndCatchup rather than Pause, because catch-up is what "
                      + "keeps a re-revealed effect from being frozen mid-burst.");
        }
    }

    // ==========================================================================================
    //  [Perf] GFX — the render state that multiplies all of it
    // ==========================================================================================

    /// <summary>
    /// The render-state line. Everything here is a read; nothing is asserted or changed.
    ///
    /// <para>THE SHADOW STATE IS RECORDED, NOT PROSECUTED. Shadows were the strongest remaining
    /// mechanism and they were tested on hardware in 2026-07 with the game's own Options › Graphics
    /// shadow control off and its quality preset at minimum: the head camera did not move. The
    /// fields below therefore exist to PIN THE STATE a future measurement was taken in — a
    /// refutation only covers the state it was measured in — not to argue a lever. Shadows are the
    /// base game's own setting either way, so a mod-side override would have been the wrong shape
    /// even if it had worked.</para>
    /// </summary>
    internal static void AppendGfxLine(StringBuilder sb)
    {
        // THE TEXTURE CENSUS IS EMITTED HERE, BEFORE THIS LINE, AND ON A LINE OF ITS OWN.
        //
        // Its data was collected during the SCENE walk above (PerfTextureCensus.Begin/PixelSpan/
        // Offer), and the caller — PerfMonitor.LogSceneProfile — logs SCENE, then calls this, then
        // logs GFX. Emitting from here therefore puts [Perf] TEX between them, which is the order
        // it wants to be read in: SCENE says what is submitted, TEX says what those surfaces are
        // TEXTURED with, GFX says what state multiplies all of it.
        //
        // Why not a fourth clause on the GFX line: GFX is already the longest line in the log and
        // the texture question ("warum sind die Texturen matschig") is a different question from
        // the submission-volume question this line exists for. Why not a fourth call site in
        // PerfMonitor: that file is not this lane's to edit, and one call from the class that owns
        // the walk is a smaller seam than a new entry point in the monitor.
        PerfTextureCensus.Log();

        sb.Append("GFX — the render state that multiplies submission volume");

        try
        {
            int level = QualitySettings.GetQualityLevel();
            string[] names = QualitySettings.names;
            sb.Append(" | quality level ").Append(level).Append(" '")
              .Append(level >= 0 && level < names.Length ? names[level] : "?")
              .Append("' of ").Append(names.Length);
        }
        catch (Exception e)
        {
            sb.Append(" | quality level n/a (").Append(e.GetType().Name).Append(')');
        }

        sb.Append(" | shadows=").Append(QualitySettings.shadows)
          .Append(" cascades=").Append(QualitySettings.shadowCascades)
          .Append(" distance=").Append(QualitySettings.shadowDistance.ToString("F1"))
          .Append(" resolution=").Append(QualitySettings.shadowResolution)
          .Append(" projection=").Append(QualitySettings.shadowProjection)
          .Append(" mask=").Append(QualitySettings.shadowmaskMode);

        sb.Append(" | pixelLights=").Append(QualitySettings.pixelLightCount)
          .Append(" lodBias=").Append(QualitySettings.lodBias.ToString("F2"))
          .Append(" maxLOD=").Append(QualitySettings.maximumLODLevel)
          .Append(" softParticles=").Append(QualitySettings.softParticles)
          .Append(" realtimeReflectionProbes=").Append(QualitySettings.realtimeReflectionProbes)
          .Append(" skinWeights=").Append(QualitySettings.skinWeights)
          .Append(" antiAliasing=").Append(QualitySettings.antiAliasing)
          .Append(" vSync=").Append(QualitySettings.vSyncCount);

        // A/B values must be visible beside actual native quality, not inferred from a
        // platform marker. The same live settings can be used on PC and standalone Frame.
        sb.Append(" | scenarioGrass=").Append(PerfConfig.ScenarioSceneryDensityPercentValue).Append('%')
          .Append(" scenarioDecoration=").Append(PerfConfig.ScenarioDecorationDensityPercentValue).Append('%')
          .Append(" scenarioVegetation=").Append(PerfConfig.ScenarioVegetationDensityPercentValue).Append('%')
          .Append(" playerFigureDetail=").Append(PerfConfig.PlayerFigureDetailPercent).Append('%')
          .Append(" enemyFigureDetail=").Append(PerfConfig.EnemyFigureDetailPercent).Append('%')
          .Append(" figureCloth=").Append(PerfConfig.FigureClothSimulationEnabled)
          .Append(" floorChunks=").Append(PerfConfig.StaticScenarioBatchesOn)
          .Append(" simpleFloorShading=").Append(PerfConfig.SimpleEnvironmentShadingOn)
          .Append(" environmentEffects=").Append(PerfConfig.EnvironmentEffectsDensityPercent).Append('%')
          .Append(" reducedGenerationNextLoad=").Append(PerfConfig.ReducedScenarioGenerationOn)
          .Append(" sharedWallReadCache=").Append(PerfConfig.SharedWallReadCacheOn)
          .Append(" lightWorkCache=").Append(PerfConfig.LightStabiliserWorkCacheOn)
          .Append(" desktopMirror=").Append(WorldUI.WorldUIConfig.DesktopMirrorLeftEye?.Value ?? Defaults.DesktopMirrorLeftEye);

        // The two TEXTURE-side quality dials, added 2026-08-23 with the [Perf] TEX line. They are
        // duplicated onto this line deliberately: TEX can be rationed away with the SCENE walk it
        // rides, and these two fields are three field reads that must be in EVERY window's record.
        // masterTextureLimit especially — the game writes it from a persisted profile whose
        // deserialisation fallback is EIGHTHEN (one-eighth resolution) and re-loads it from the
        // level asset on every QualitySettings.SetQualityLevel, which is the same mechanism this
        // mod already re-asserts MSAA and the pixel-light cap against. Until now nothing in the mod
        // read it at all, so no log in this project's history can say what it was.
        sb.Append(" | masterTextureLimit=").Append(QualitySettings.masterTextureLimit)
          .Append(" anisotropicFiltering=").Append(QualitySettings.anisotropicFiltering)
          .Append(" streamingMipmaps=").Append(QualitySettings.streamingMipmapsActive)
          .Append(" (masterTextureLimit N ⇒ every MIPPED texture renders at 1/2^N per side; the "
                  + "[Perf] TEX line above is what these two mean for the surfaces actually in "
                  + "view)");

        AppendLodGroups(sb);
        sb.Append(_gfxLights);
        AppendHeadCamera(sb);
    }

    /// <summary>
    /// LOD census. Named here because "does the game have LOD groups" is a question the Part-3 lever
    /// menu has to answer, and answering it from source alone is unsafe: content generated at
    /// runtime can carry components the generator's C# never mentions. If the count is zero then
    /// <see cref="QualitySettings.lodBias"/> and <c>maximumLODLevel</c> above are inert dials, and
    /// no LOD-side lever exists to pull no matter how attractive it sounds.
    /// </summary>
    private static void AppendLodGroups(StringBuilder sb)
    {
        try
        {
            // THE SWEEP IS LodGroupCensus'S SINCE 2026-09-05 (redundancy survey R43). AutoLod asks
            // the same question with the same API on a different cadence and counted only the
            // ACTIVE half; the two lines then sat in one log with nothing to say which number was
            // fresher or whether they were even the same measurement — and they were not. The
            // sentence below is this line's own and is unchanged; only the counting moved, and the
            // population rule is now stated in the line the way the SCENE line above already does.
            LodGroupCensus.Result lod = new(_inventoryLodGroups.ToArray(), _lodEnabled);
            sb.Append(" | LOD groups: ").Append(lod.Active).Append(" active, ").Append(lod.Enabled)
              .Append(" enabled");
            if (lod.Active == 0)
            {
                sb.Append(" — NONE, so lodBias and maximumLODLevel above are inert here and there "
                          + "is no LOD lever to pull. Distance-based reduction would have to come "
                          + "from Camera.layerCullDistances instead");
            }
            sb.Append(" [population: loaded scene-root traversal, inactive objects excluded; "
                + "disabled components counted separately; same incremental SCENE sampling span]");
        }
        catch (Exception e)
        {
            sb.Append(" | LOD groups n/a (").Append(e.GetType().Name).Append(')');
        }
    }

    /// <summary>
    /// Light census. The number that matters is not "how many lights" but "how many REAL-TIME
    /// lights cast shadows", because only those add shadow-map passes — a baked or shadow-less
    /// light adds no submission at all.
    /// </summary>
    private static IEnumerable AppendLights(StringBuilder sb)
    {
        List<Light> lights = _inventoryLights;

        int enabled = 0, realtimeShadow = 0, dir = 0, point = 0, spot = 0, area = 0, baked = 0;
        for (int i = 0; i < lights.Count; i++)
        {
            yield return null;
            Light l = lights[i];
            if (l == null || !l.isActiveAndEnabled)
                continue;
            enabled++;
            switch (l.type)
            {
                case LightType.Directional: dir++; break;
                case LightType.Point: point++; break;
                case LightType.Spot: spot++; break;
                default: area++; break;
            }
            bool isBaked = l.bakingOutput.isBaked
                           && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked;
            if (isBaked)
                baked++;
            else if (l.shadows != LightShadows.None)
                realtimeShadow++;
        }

        sb.Append(" | lights: ").Append(lights.Count).Append(" active object(s), ").Append(enabled)
          .Append(" enabled (").Append(dir).Append(" dir, ").Append(point).Append(" point, ")
          .Append(spot).Append(" spot, ").Append(area).Append(" other; ").Append(baked)
          .Append(" fully baked) | REAL-TIME SHADOW-CASTING LIGHTS: ").Append(realtimeShadow);

        if (QualitySettings.shadows == ShadowQuality.Disable || realtimeShadow == 0)
        {
            sb.Append(" — no shadow map is rendered in this state, so nothing here is on the "
                      + "submission bill.");
        }
        else
        {
            sb.Append(" — each one re-submits every shadow caster within ")
              .Append(QualitySettings.shadowDistance.ToString("F1")).Append("m, once per cascade (")
              .Append(QualitySettings.shadowCascades).Append("). NOTE (2026-07 hardware): removing "
                      + "all of it moved the head camera by nothing — this state is recorded, not "
                      + "accused. Shadows are the base game's own control (Options › Graphics).");
        }
    }

    /// <summary>
    /// The head camera's own configuration — every field on it changes how much gets submitted,
    /// and two of them are the mod's own doing (the forward path and the depth-texture mode), so
    /// they are reported plainly rather than left for someone to rediscover.
    /// </summary>
    private static void AppendHeadCamera(StringBuilder sb)
    {
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            sb.Append(" | head camera: none (no VR rig up)");
            return;
        }

        sb.Append(" | head camera: path=").Append(head.renderingPath).Append('/')
          .Append(head.actualRenderingPath)
          .Append(" depthTextureMode=").Append(head.depthTextureMode)
          .Append(" occlusionCulling=").Append(head.useOcclusionCulling)
          .Append(" clear=").Append(head.clearFlags)
          .Append(" near=").Append(head.nearClipPlane.ToString("F3"))
          .Append(" far=").Append(head.farClipPlane.ToString("F0"))
          .Append(" stereo=").Append(XRSettings.stereoRenderingMode);

        if ((head.depthTextureMode & DepthTextureMode.Depth) != 0
            && head.actualRenderingPath == RenderingPath.Forward)
        {
            sb.Append(" | NOTE: a FORWARD camera with DepthTextureMode.Depth builds "
                      + "_CameraDepthTexture by rendering the whole opaque scene a SECOND time "
                      + "through each shader's shadow-caster pass. That is a full extra scene "
                      + "submission per eye pass, and it is the mod's own (it is what makes the "
                      + "game's soft-particle VFX fade correctly against walls). "
                      + "[Optimize] HeadDepthPrepass switches it off for an A/B.");
        }

        sb.Append(" | head culling mask 0x").Append(head.cullingMask.ToString("X8")).Append(" renders:");
        bool any = false;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((head.cullingMask & (1 << layer)) == 0)
                continue;
            string name = LayerMask.LayerToName(layer);
            sb.Append(any ? ", " : " ").Append(layer).Append('=')
              .Append(string.IsNullOrEmpty(name) ? "<unnamed>" : name);
            any = true;
        }
        if (!any)
            sb.Append(" nothing");
        if (PerfConfig.HeadMaskDropMask != 0)
        {
            sb.Append(" | [Optimize] HeadCullingMaskDrop is ACTIVE (0x")
              .Append(PerfConfig.HeadMaskDropMask.ToString("X8"))
              .Append(") — the mask above already has those layers removed");
        }

        AppendLayerCullDistances(sb, head);
        AppendCommandBuffers(sb, head);
    }

    /// <summary>
    /// Per-layer cull distances on the head camera.
    ///
    /// <para>WHY THIS IS PRINTED RATHER THAN ASSUMED. <see cref="Camera.layerCullDistances"/> is the
    /// one distance-based reduction available in a scene with no LOD groups, and it is also a
    /// perfect trap for this project: the array is in WORLD UNITS, and this diorama is scaled by
    /// ~198x, so a "10 metre" cut-off written as 10 removes essentially everything. The mod has
    /// already shipped one bug from confusing world units with metres (a laser drawn 0.15 mm wide at
    /// rig scale). Printing the live array next to the head camera's far plane is what makes the
    /// scale visible before anyone types a number into it. An all-zero array means "no per-layer
    /// override", which is Unity's default and the state today.</para>
    /// </summary>
    private static void AppendLayerCullDistances(StringBuilder sb, Camera head)
    {
        try
        {
            float[] dists = head.layerCullDistances;
            int overridden = 0;
            for (int i = 0; i < dists.Length; i++)
            {
                if (dists[i] > 0f)
                    overridden++;
            }
            sb.Append(" | layerCullDistances: ").Append(overridden)
              .Append(" of 32 layer(s) overridden, spherical=").Append(head.layerCullSpherical);
            if (overridden == 0)
            {
                sb.Append(" (all zero = Unity's default: every layer is culled at the camera's far "
                          + "plane of ").Append(head.farClipPlane.ToString("F0"))
                  .Append(" WORLD UNITS. Any value written here is in the same world units, NOT "
                          + "metres — the diorama is scaled ~198x)");
            }
            else
            {
                for (int i = 0; i < dists.Length; i++)
                {
                    if (dists[i] <= 0f)
                        continue;
                    string name = LayerMask.LayerToName(i);
                    sb.Append(", ").Append(i).Append('=')
                      .Append(string.IsNullOrEmpty(name) ? "<unnamed>" : name).Append(':')
                      .Append(dists[i].ToString("F1")).Append("wu");
                }
            }
        }
        catch (Exception e)
        {
            sb.Append(" | layerCullDistances n/a (").Append(e.GetType().Name).Append(')');
        }
    }

    /// <summary>
    /// COMMAND BUFFERS attached to the head camera, by event, with their names.
    ///
    /// <para>WHY THIS BELONGS ON A PERFORMANCE LINE. A command buffer is a block of rendering work
    /// that does not appear in ANY of the mod's other numbers: it is not a renderer, it is not a
    /// material slot, it is not a camera pass, and it is not a light — but it runs inside the head
    /// camera's render loop, once per eye pass, and it can be arbitrarily large. This game ships one
    /// that matters: <c>TilesOcclusionGenerator</c> builds a buffer at
    /// <c>CameraEvent.BeforeGBuffer</c> that draws EVERY revealed room renderer and object renderer
    /// into an occlusion map, with no frustum test — work whose size is a direct function of how
    /// many rooms the player has opened, which is the exact axis of the report this line was
    /// switched on for. Whether that buffer is attached to the MOD's head camera or only to the
    /// game's own ScenarioCamera decides whether VR pays for it twice per frame, and there is no
    /// other way to find out than to ask the camera.</para>
    /// </summary>
    private static void AppendCommandBuffers(StringBuilder sb, Camera head)
    {
        try
        {
            sb.Append(" | command buffers on the head camera: ").Append(head.commandBufferCount)
              .Append(" total");
            if (head.commandBufferCount == 0)
            {
                sb.Append(" — none, so nothing outside the ordinary render loop is attached here");
                return;
            }

            // Only the events anything in this game actually uses are probed: the enum has ~20
            // members and a GetCommandBuffers call per member allocates an array per call.
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.BeforeGBuffer);
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.BeforeForwardOpaque);
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.AfterForwardOpaque);
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.BeforeForwardAlpha);
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.AfterForwardAlpha);
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.BeforeImageEffects);
            AppendBuffersAt(sb, head, UnityEngine.Rendering.CameraEvent.AfterEverything);

            // THE PATH DECIDES WHETHER A DEFERRED-ONLY EVENT COSTS ANYTHING AT ALL, and without
            // this clause the line above is a trap. TilesOcclusionGenerator attaches its
            // "Tile Occlusion Map Generation" buffer at BeforeGBuffer (decompiled
            // TilesOcclusionGenerator.cs:192) — a buffer that draws EVERY revealed room renderer
            // and EVERY object renderer with no frustum test, plus six blits, so it is exactly the
            // shape that scales with "the player opened all the rooms". But BeforeGBuffer is a
            // DEFERRED-path event: on a forward camera it is never reached, and the mod forces the
            // forward path ([Rig] ForwardRendering, default on). So an attached buffer at that
            // event on a forward camera costs NOTHING and must not be read as a finding.
            // It also attaches to whatever camera the generator itself sits on
            // (`m_Camera = GetComponent<Camera>()`, :48) — the game's ScenarioCamera, not ours —
            // so the count above being 0 is the expected reading and is not evidence of absence
            // anywhere else. Print the path so the next reader can tell the three cases apart.
            sb.Append(" | head camera renderingPath=").Append(head.renderingPath)
              .Append("/actual=").Append(head.actualRenderingPath)
              .Append(head.actualRenderingPath == RenderingPath.Forward
                          ? " — FORWARD, so any BeforeGBuffer buffer listed above is INERT here "
                            + "(deferred-only event) and is not a cost"
                          : " — DEFERRED, so a BeforeGBuffer buffer above really does run");
        }
        catch (Exception e)
        {
            sb.Append(" | command buffers n/a (").Append(e.GetType().Name).Append(')');
        }
    }

    private static void AppendBuffersAt(StringBuilder sb, Camera cam,
                                        UnityEngine.Rendering.CameraEvent evt)
    {
        UnityEngine.Rendering.CommandBuffer[] buffers = cam.GetCommandBuffers(evt);
        if (buffers.Length == 0)
            return;
        sb.Append(" | at ").Append(evt).Append(':');
        for (int i = 0; i < buffers.Length; i++)
        {
            sb.Append(i == 0 ? " " : ", ").Append(buffers[i].name).Append(" (")
              .Append(buffers[i].sizeInBytes).Append("B)");
        }
    }
}
