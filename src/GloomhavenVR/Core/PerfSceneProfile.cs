using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// WHAT THE RENDER LOOP IS ACTUALLY SUBMITTING — the breakdown the 2026-07 judder investigation
/// needs next, and the one number the previous instrumentation could not produce.
///
/// <para>WHERE THIS COMES FROM. <see cref="PerfFrameSplit"/> settled WHICH LAYER owns the frame:
/// the main thread spends 78–84 % of it inside Unity's render loop, ~11–16 ms of that in the head
/// camera alone over two MultiPass passes, against ~1 ms of logic and ~2.5 ms blocked. That
/// verdict kills every pixel-cost lever (an 11× cut in pixel samples moved nothing) and points at
/// the NUMBER of things submitted. But its scene census was a bare count — "1683 renderers, 1536
/// enabled, 811–1511 visible" — and a count cannot choose a lever. Board tiles, wall segments,
/// props, figures, particle systems and the mod's own visuals all cost the same in a total and
/// nothing at all alike in what can be done about them.</para>
///
/// <para>TWO LINES, TWO QUESTIONS:</para>
/// <list type="bullet">
/// <item><b><c>[Perf] SCENE</c></b> — WHAT is there, grouped the way a lever would have to group
/// it: by scene-root object (so "the board" and "the mod's hands" are separable), by layer (so
/// culling-mask hygiene becomes a decision instead of a guess), by renderer type, by shadow-casting
/// mode, and by MATERIAL COUNT. Materials matter more than renderers: the built-in pipeline emits
/// at least one draw call per renderer PER MATERIAL, so the submitted draw-call estimate is the sum
/// of material counts over the renderers that are enabled, visible and inside the head camera's
/// culling mask — multiplied by the pass count, because MultiPass pays all of it twice.</item>
/// <item><b><c>[Perf] GFX</c></b> — the RENDER STATE that multiplies all of it: the live quality
/// level and every <see cref="QualitySettings"/> field that changes submission volume (shadows,
/// cascades, shadow distance, pixel light count, LOD bias), a census of real-time shadow-casting
/// lights, and the head camera's own configuration (culling mask decoded to layer NAMES, rendering
/// path, depth-texture mode, occlusion culling).</item>
/// </list>
///
/// <para>SHADOWS: TESTED AND REFUTED (2026-07, hardware). The shadow-cascade hypothesis was the
/// strongest remaining mechanism — in the built-in pipeline a real-time shadow-casting light
/// re-submits every shadow caster once per cascade, which is invisible to a pixel-cost experiment
/// and scales with renderer count exactly as the judder does. It is wrong here. With shadows
/// switched off in the game's own Options › Graphics AND the quality preset at its lowest, the head
/// camera measured 15.0–17.4 ms of cull+submit against 10.8–17.8 ms with shadows on at normal
/// quality: no improvement at all. The state fields are still printed below, because a hypothesis
/// is only refuted for the state it was refuted in and the next log has to be able to show that
/// state — but no shadow-side lever is worth building.</para>
///
/// <para>WHAT THE COUNTS DO AND DO NOT INCLUDE. <see cref="UnityEngine.Object.FindObjectsOfType{T}"/>
/// returns components on ACTIVE GameObjects only. So an object hidden with
/// <c>SetActive(false)</c> never appears here at all, while one hidden by <c>renderer.enabled =
/// false</c>, zero alpha or zero scale appears and is counted — deliberately, because the second
/// kind still costs culling and, at zero alpha, usually still costs submission. "Total" is
/// therefore "active in the hierarchy", not "exists".</para>
///
/// <para>COST, AND WHY IT IS STILL WORTH IT. The walk is two <c>FindObjectsOfType</c> calls plus a
/// per-renderer material-list fill, i.e. a few milliseconds and a few hundred KB of garbage — far
/// too expensive per frame, which is exactly why it runs ONCE PER SUMMARY WINDOW (30 s by default)
/// behind its own config switch, like the census it extends. Root-object NAMES are read once per
/// distinct root rather than once per renderer (<see cref="UnityEngine.Object.name"/> allocates a
/// fresh string on every read), which turns ~1700 string allocations into ~50.</para>
///
/// <para>MULTIPLAYER / REVERSIBILITY. Reads state, writes log lines. It never touches a game
/// object, game state or wire traffic, and it holds no reference past the end of the call.</para>
/// </summary>
internal static class PerfSceneProfile
{
    /// <summary>How many groups the SCENE line names before collapsing the tail into "+N more".</summary>
    private const int TopRoots = 16;

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
    private static readonly Dictionary<string, int> Types = new(8);
    private static readonly List<KeyValuePair<string, int>> TypeOrder = new(8);
    private static readonly int[] LayerCounts = new int[32];
    private static readonly int[] LayerVisible = new int[32];

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
    internal static void AppendSceneLine(StringBuilder sb)
    {
        sb.Append("SCENE — what the render loop is asked to submit (sampled ONCE this window; "
                  + "FindObjectsOfType sees ACTIVE GameObjects only, so anything hidden with "
                  + "SetActive(false) is absent from every number here, while anything hidden by "
                  + "renderer.enabled/alpha/scale is present and still costs culling)");

        Renderer[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<Renderer>();
        }
        catch (Exception e)
        {
            sb.Append(" | n/a (renderer walk threw ").Append(e.GetType().Name).Append(')');
            return;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        int headMask = head != null ? head.cullingMask : ~0;
        int modLayer = VRLayers.ModLayer;

        Reset();

        int enabled = 0, visible = 0, inMask = 0, submitted = 0;
        int materialsTotal = 0, materialsSubmitted = 0, instanced = 0;
        int staticBatched = 0, modOwned = 0, modOwnedEnabled = 0;
        int propertyBlocks = 0, propertyBlocksSubmitted = 0;
        int castOff = 0, castOn = 0, castTwoSided = 0, castShadowsOnly = 0;

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null)
                continue;

            int layer = r.gameObject.layer;
            bool on = r.enabled;
            bool vis = r.isVisible;
            bool masked = (headMask & (1 << layer)) != 0;
            bool subm = on && vis && masked;

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
                }
            }
            catch (Exception)
            {
                mats = 0; // a renderer with no material array still counts as an object
            }
            materialsTotal += mats;
            if (subm)
                materialsSubmitted += mats;

            TallyType(r);
            TallyRoot(r, on, vis, subm, mats);
        }

        int passes = XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass ? 2 : 1;

        sb.Append(" | totals: ").Append(all.Length).Append(" active renderer(s), ")
          .Append(enabled).Append(" enabled, ").Append(visible).Append(" visible, ")
          .Append(inMask).Append(" inside the head camera's culling mask, ")
          .Append(submitted).Append(" enabled+visible+in-mask")
          .Append(" | draw-call floor: ").Append(materialsSubmitted)
          .Append(" material slot(s) on those, x").Append(passes)
          .Append(" pass(es) = ~").Append(materialsSubmitted * passes)
          .Append(" draw calls/frame BEFORE batching, shadow passes and any depth prepass "
                  + "(one submesh/material = at least one call; this is a floor, never a ceiling)")
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
        // The batching state is now something the MOD can change, so this sentence has to name who
        // owns the number rather than asserting the old "nobody batches here" as a standing fact.
        if (staticBatched == 0)
            sb.Append(" NOTHING is statically batched (the game only ever calls "
                      + "StaticBatchingUtility.Combine from a debug hotkey, and its board geometry "
                      + "is generated at runtime, so this is expected rather than a regression). "
                      + "The mod's own experimental pass ([Batching] Mode, Core.StaticBatcher) is "
                      + "therefore either off or has not run yet — its [Batch] PROBE/APPLY lines "
                      + "say which.");
        else
            sb.Append(" — and since the game itself never batches outside a debug hotkey, that "
                      + "count is the mod's own experimental pass ([Batching] Mode); compare it "
                      + "against the renderer total on the [Batch] APPLY line.");

        sb.Append(" | mod-owned (layer ").Append(modLayer).Append("): ").Append(modOwned)
          .Append(" renderer(s), ").Append(modOwnedEnabled).Append(" enabled (")
          .Append(all.Length > 0 ? (100f * modOwned / all.Length).ToString("F1") : "0")
          .Append("% of the scene)");

        sb.Append(" | shadow casting: ").Append(castOn).Append(" On, ").Append(castOff)
          .Append(" Off, ").Append(castTwoSided).Append(" TwoSided, ").Append(castShadowsOnly)
          .Append(" ShadowsOnly (only meaningful while QualitySettings.shadows is enabled — see "
                  + "the GFX line)");

        AppendTypes(sb);
        AppendLayers(sb, headMask, all.Length);
        AppendRoots(sb, passes);
    }

    private static void Reset()
    {
        Roots.Clear();
        RootOrder.Clear();
        SubmittedMaterials.Clear();
        Types.Clear();
        TypeOrder.Clear();
        Array.Clear(LayerCounts, 0, LayerCounts.Length);
        Array.Clear(LayerVisible, 0, LayerVisible.Length);
    }

    private static void TallyType(Renderer r)
    {
        // Type.Name is a cached string on the runtime type object — no allocation per read.
        string name = r.GetType().Name;
        Types.TryGetValue(name, out int n);
        Types[name] = n + 1;
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

    private static void AppendTypes(StringBuilder sb)
    {
        sb.Append(" | by renderer type:");
        bool first = true;
        foreach (KeyValuePair<string, int> kv in Types)
        {
            sb.Append(first ? " " : ", ").Append(kv.Key).Append(' ').Append(kv.Value);
            first = false;
        }
        if (first)
            sb.Append(" none");
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

        AppendLights(sb);
        AppendHeadCamera(sb);
    }

    /// <summary>
    /// Light census. The number that matters is not "how many lights" but "how many REAL-TIME
    /// lights cast shadows", because only those add shadow-map passes — a baked or shadow-less
    /// light adds no submission at all.
    /// </summary>
    private static void AppendLights(StringBuilder sb)
    {
        Light[] lights;
        try
        {
            lights = UnityEngine.Object.FindObjectsOfType<Light>();
        }
        catch (Exception e)
        {
            sb.Append(" | lights n/a (").Append(e.GetType().Name).Append(')');
            return;
        }

        int enabled = 0, realtimeShadow = 0, dir = 0, point = 0, spot = 0, area = 0, baked = 0;
        for (int i = 0; i < lights.Length; i++)
        {
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

        sb.Append(" | lights: ").Append(lights.Length).Append(" active object(s), ").Append(enabled)
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
    }
}
