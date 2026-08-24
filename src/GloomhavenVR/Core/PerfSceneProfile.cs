using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace GloomhavenVR.Core;

/// <summary>
/// WHAT THE RENDER LOOP IS ACTUALLY SUBMITTING — the per-object breakdown of the frame.
///
/// <para>THE INVESTIGATION THIS WAS BUILT FOR IS CLOSED (2026-07-28,
/// <c>.planning/perf/FINDINGS.md</c>): the wall was that Unity submitted every draw call on ONE
/// thread, and threaded submission (<c>[Core] EnableGraphicsJobs</c>) took the main-thread render
/// loop from 14.9 ms to 1.8 ms and the headset from 45 Hz to 90 Hz. This line stays because it is
/// how a regression would be seen at all — and because its own numbers are still exactly what they
/// always were. Note what it can and cannot show now: it counts renderers and MATERIAL SLOTS, which
/// is submission VOLUME, and volume is no longer the same thing as main-thread cost. Read it
/// together with the <c>[Perf] SPLIT</c> line, which is the one that settled the question.</para>
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
/// <para>2026-08-22, THE REPORT THIS LINE WAS FINALLY SWITCHED ON FOR (user, verbatim): <i>"Ich hab
/// nun mal eine Map aufgemacht mit vielen Details und hab dort zum Testen alle Räume aufgemacht. Ich
/// merke deutliche Laggs wenn ich alle Räume von oben anschaue. In VR ist dieses überblickende 'von
/// oben schauen' sehr wichtig, dass es möglich ist."</i> The ModBuild 226 log measures that session
/// exactly: the scene census climbs from ~2,120 renderers with a few rooms open to <b>8,569–8,631
/// renderers, 8,176–8,288 enabled, up to 5,660 visible</b>, and the frame goes with it — 12–14 ms
/// with a few rooms, <b>43–78 ms with all of them</b>, of which <c>[Perf] SPLIT</c> puts ~50 % in
/// main-thread LOGIC, ~13 % in the render loop and ~36 % blocked, with the mod itself at 6.3 ms
/// (14.6 %). Half the frame is the GAME'S OWN <c>Update</c>/<c>LateUpdate</c>, and this file could
/// not say one word about what runs in it: it counted renderers, and renderers are the render loop.</para>
///
/// <para>THE SENTENCE THAT HAD NEVER BEEN CHECKED. The <c>ZOOM</c> clause on the SPLIT line
/// concludes, whenever most of a near/far delta lands in logic, that the cost is <i>"view-scaled
/// SIMULATION — animators, particle systems and isVisible-gated scripts that stop being culled as
/// the head rises"</i>. That is a HYPOTHESIS the zoom axis cannot test, and the same log contains
/// windows that contradict it: one window's MIDDLE third is the most expensive of the three
/// (29.56 / 48.86 / 20.29 ms), and the last window of the session reads 71.58 / 70.99 / 71.12 ms
/// across a 18.5→31.3 wu distance sweep — a flat 71 ms that does not care where the head is. If the
/// game ships its animators at Unity's default <see cref="AnimatorCullingMode.AlwaysAnimate"/>, then
/// every animator ticks whether or not it is on screen, the correlation with the visible-renderer
/// count is a COINCIDENCE OF POPULATION (more rooms open ⇒ both more renderers and more animators),
/// and no culling lever can touch it. That is one number, and the <c>[Perf] SIM</c> line below is
/// here to print it.</para>
///
/// <para>THREE LINES, THREE QUESTIONS:</para>
/// <list type="bullet">
/// <item><b><c>[Perf] SIM</c></b> — WHAT THE MAIN-THREAD LOOP ITERATES OVER, which is a different
/// population from the renderers and the one that owns ~50 % of this frame. Unity's per-frame
/// script loop is exactly "every enabled Behaviour whose type DECLARES <c>Update</c>", so that
/// count is not a proxy for the loop, it IS the loop's length; the same for <c>LateUpdate</c> and
/// <c>FixedUpdate</c>. Beside it: the heaviest ticking TYPES by instance count (mod-owned ones
/// marked, so the mod's 14.6 % is separable from the game's 85 %), the <see cref="Animator"/>
/// census broken down BY <see cref="AnimatorCullingMode"/>, and the <see cref="ParticleSystem"/>
/// census broken down by <see cref="ParticleSystemCullingMode"/>. Emitted BEFORE the SCENE line it
/// shares a walk with, deliberately: it is the line that decides what to do, and SCENE is its
/// context.</item>
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
/// <para>COST, MEASURED RATHER THAN ASSERTED, AND SELF-LIMITING. The walk is five
/// <c>FindObjectsOfType</c> calls plus a per-renderer material-list fill, over a population that the
/// ModBuild 226 log puts at 8,600 renderers and an unknown but larger number of behaviours — far too
/// expensive per frame, which is exactly why it runs ONCE PER SUMMARY WINDOW (30 s by default).
/// Every name lookup is done once per BUCKET, not once per object (<see cref="UnityEngine.Object.name"/>
/// and <c>Shader.name</c> allocate a fresh string on every read), and the <c>Update</c>/<c>LateUpdate</c>
/// reflection is cached per <see cref="Type"/> for the life of the process, so the second window pays
/// none of it. It <b>times itself with a <see cref="Stopwatch"/> and prints the figure on both lines</b>
/// — the way <c>Perf.ZoomSample</c> prints its 0.006 ms — and if that figure comes in above
/// <see cref="AmortiseTargetMs"/> it SKIPS the next few windows so the amortised cost stays under
/// that target. An instrument is not allowed to become the thing it measures, and "I promise it is
/// cheap" is not a measurement.</para>
///
/// <para>REJECTED: sampling any of this per frame (it is a full-scene walk — the per-frame estimate
/// on the ZOOM clause already exists for that and works by round-robin slices); gating the walk on
/// a scenario being loaded (the pre-menu gate below is the only place it is genuinely worthless, and
/// the campaign map is a legitimate subject); and counting behaviours by <c>GetComponents</c> per
/// GameObject (one <c>FindObjectsOfType&lt;MonoBehaviour&gt;</c> is a single native walk, the other
/// is a managed loop with an allocation per object).</para>
///
/// <para>MULTIPLAYER / REVERSIBILITY. Reads state, writes log lines. It never touches a game
/// object, game state or wire traffic, and it holds no reference past the end of the call.</para>
/// </summary>
internal static class PerfSceneProfile
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
    /// <para>The walk is a one-frame hitch by construction and its size is a property of the scene,
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

    /// <summary>Windows still to be skipped by the rationing described on <see cref="AmortiseTargetMs"/>.</summary>
    private static int _skipWindows;

    /// <summary>Last measured total walk cost, milliseconds — printed on both lines.</summary>
    private static double _lastWalkMs;

    /// <summary>Latched after the SIM half throws once, so a fault there cannot cost the SCENE line.</summary>
    private static bool _simFaulted;

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
    internal static void AppendSceneLine(StringBuilder sb)
    {
        // ---- rationing (see AmortiseTargetMs) --------------------------------------------------
        // Checked BEFORE anything is walked, and it still emits a line: a window that silently
        // produced no output would be indistinguishable from the instrument having faulted, and
        // this project has already lost rounds to a remedy that never ran while looking like it had.
        if (_skipWindows > 0)
        {
            _skipWindows--;
            sb.Append("SCENE — skipped this window. The last walk measured ")
              .Append(_lastWalkMs.ToString("F1"))
              .Append("ms, so it is being rationed down to an amortised ")
              .Append(AmortiseTargetMs.ToString("F0")).Append("ms/window; ")
              .Append(_skipWindows).Append(" more window(s) will be skipped before the next sample. "
                      + "This is the instrument refusing to become the thing it measures, not a "
                      + "fault. Set [Perf] SceneProfile = false to stop it entirely.");
            // The GLOW CARDS census does NOT have to be rationed with this walk, and must not be:
            // ModBuild 251's whole A/B session produced exactly one armed window because the census
            // could only sample when this walk chose to, and that window landed on a five-renderer
            // frame. It runs a renderer-only sweep of its own here — self-timed, self-rationed to
            // 2ms/window, and printed — on windows this walk has already declined to pay for.
            GlowCardCensus.RunStandalone(Rig.VRRigDriver.HeadCamera);
            return;
        }

        Stopwatch clock = Stopwatch.StartNew();

        // The SIM half runs FIRST and is logged first: it is the line that decides what to do about
        // the ~50 % of the frame that main-thread logic owns, and SCENE is its context. It is also
        // the half that walks the largest population, so if the whole sample has to be cut short by
        // an exception, this is the half worth having.
        double simMs = BuildSimLine();

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
            FinishWalk(sb, clock, simMs);
            return;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        int headMask = head != null ? head.cullingMask : ~0;
        int modLayer = VRLayers.ModLayer;

        Reset();
        // The texture census rides THIS walk rather than paying for one of its own — the renderers
        // and material slots it needs are already in hand below, and a second FindObjectsOfType over
        // 8,600 renderers would double the most expensive thing in this file. Armed here, fed inside
        // the loop, printed by AppendGfxLine. See PerfTextureCensus for what it answers and why.
        PerfTextureCensus.Begin(head);
        // The glow-card census rides the SAME walk, for the same reason and at a smaller price: one
        // dictionary lookup per material slot, keyed by the SHADER's instance id, so a shader name
        // is marshalled once per distinct shader and never once per renderer. It answers "what ARE
        // those pale rectangles on the gate, and can their opacity work on this camera at all" —
        // see GlowCardCensus for the report and the two competing explanations it separates.
        GlowCardCensus.Begin(head);

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

            // How big this renderer is in the eye, in the eye's own pixels — 0 for anything that is
            // not submitted or is too small to be part of the "matschige Texturen" complaint. Taken
            // ONCE here so the Renderer.bounds read is not repeated per material slot below.
            float texSpanPx = subm ? PerfTextureCensus.PixelSpan(r) : 0f;

            // ONE Renderer.bounds read per SUBMITTED renderer, for the glow census's PLATE test and
            // its own unfloored pixel span. It cannot reuse PerfTextureCensus.PixelSpan: that one
            // applies a 120px floor and returns 0 below it, which is exactly why ModBuild 251 printed
            // "~0px" for every candle and every torch and then ranked its cap-of-16 by nothing at all.
            GlowCardCensus.OfferRenderer(r, subm, layer);

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
                    Shader? slotShader = TallyShader(mat, subm);
                    // NOT gated on texSpanPx: a glow card that is currently DISABLED, or too small
                    // for the texture census's 120px floor, is exactly as interesting as a big one
                    // — "is anything driving it" is the question, and a hidden card still answers
                    // it. The shader reference is the one TallyShader just resolved, so this call
                    // adds one dictionary lookup per slot and no second Material.shader marshal.
                    if (slotShader != null)
                        GlowCardCensus.Offer(r, mat, slotShader, subm, texSpanPx);
                    if (texSpanPx > 0f)
                        PerfTextureCensus.Offer(r, mat, texSpanPx);
                }
            }
            catch (Exception)
            {
                mats = 0; // a renderer with no material array still counts as an object
            }
            // Close the glow census's per-renderer accumulation: it scores and pools the renderer HERE,
            // with its render queue and its shader's lighting passes already known, because the band
            // test depends on both and neither is available while the geometry pass is running.
            GlowCardCensus.EndRenderer(r);
            materialsTotal += mats;
            if (subm)
                materialsSubmitted += mats;

            TallyKind(r, on, vis, subm);
            TallyRoot(r, on, vis, subm, mats);
        }

        // Hand the per-layer population to the glow census rather than making it count again: its
        // path-contrast block needs to say how many renderers sit on the layers only the head camera
        // draws, and a second pass over 3,000 renderers to re-count what this loop already counted is
        // the exact shape of defect this file's own doc calls the default suspect.
        GlowCardCensus.NoteLayerPopulation(LayerCounts, LayerVisible);

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
        // The mod's own experimental batching pass was REMOVED (2026-08 user ruling — see
        // .planning/static-batching-removed.md), so a non-zero count can only come from the
        // game's own debug hotkey.
        if (staticBatched == 0)
            sb.Append(" NOTHING is statically batched (the game only ever calls "
                      + "StaticBatchingUtility.Combine from a debug hotkey, and its board geometry "
                      + "is generated at runtime, so this is expected rather than a regression).");
        else
            sb.Append(" — the mod never batches, so that count is the game's own debug-hotkey "
                      + "StaticBatchingUtility.Combine.");

        sb.Append(" | mod-owned (layer ").Append(modLayer).Append("): ").Append(modOwned)
          .Append(" renderer(s), ").Append(modOwnedEnabled).Append(" enabled (")
          .Append(all.Length > 0 ? (100f * modOwned / all.Length).ToString("F1") : "0")
          .Append("% of the scene)");

        sb.Append(" | shadow casting: ").Append(castOn).Append(" On, ").Append(castOff)
          .Append(" Off, ").Append(castTwoSided).Append(" TwoSided, ").Append(castShadowsOnly)
          .Append(" ShadowsOnly (only meaningful while QualitySettings.shadows is enabled — see "
                  + "the GFX line)");

        AppendKinds(sb);
        AppendShaders(sb, passes);
        AppendLayers(sb, headMask, all.Length);
        AppendRoots(sb, passes);
        FinishWalk(sb, clock, simMs);
    }

    /// <summary>
    /// Stop the clock, print the instrument's OWN price on both lines, ration the next few windows
    /// if it was expensive, and emit the SIM line. Called on every exit path from
    /// <see cref="AppendSceneLine"/> — including the failure ones, because a walk that threw
    /// halfway still spent the time it spent.
    /// </summary>
    private static void FinishWalk(StringBuilder sb, Stopwatch clock, double simMs)
    {
        clock.Stop();
        _lastWalkMs = clock.Elapsed.TotalMilliseconds;
        double sceneMs = Math.Max(0d, _lastWalkMs - simMs);

        // Rationing. Ceil(cost / target) - 1 is "how many windows this one sample has to be spread
        // over"; at or under the target it is 0 and every window is sampled.
        _skipWindows = _lastWalkMs <= AmortiseTargetMs
            ? 0
            : Mathf.Clamp((int)Math.Ceiling(_lastWalkMs / AmortiseTargetMs) - 1, 0, MaxSkippedWindows);

        StringBuilder cost = new(320);
        cost.Append(" | INSTRUMENT COST, measured not asserted: this whole sample took ")
            .Append(_lastWalkMs.ToString("F1")).Append("ms (SIM half ").Append(simMs.ToString("F1"))
            .Append("ms, SCENE half ").Append(sceneMs.ToString("F1"))
            .Append("ms) in ONE frame of this window");
        if (_skipWindows > 0)
        {
            cost.Append(" — above the ").Append(AmortiseTargetMs.ToString("F0"))
                .Append("ms/window budget, so the next ").Append(_skipWindows)
                .Append(" window(s) are skipped and the amortised cost stays under it");
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
    /// <returns>The material's shader, so the caller can hand the SAME resolved reference to
    /// <see cref="GlowCardCensus"/> instead of paying for a second <c>Material.shader</c> marshal on
    /// every one of the scene's ~2,500 material slots. Null when it could not be read.</returns>
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
    private static double BuildSimLine()
    {
        SimSb.Length = 0;
        if (_simFaulted)
            return 0d;

        Stopwatch clock = Stopwatch.StartNew();
        try
        {
            SimSb.Append("SIM — what the game's MAIN-THREAD LOOP iterates over (sampled ONCE this "
                         + "window, same walk as the SCENE line that follows). The SPLIT line puts "
                         + "~half the frame in Update→LateUpdate and the mod at ~15% of the total, "
                         + "so most of what is measured here is the GAME'S OWN per-frame work — "
                         + "which no renderer count can see");
            AppendBehaviours(SimSb);
            AppendAnimators(SimSb);
            AppendParticles(SimSb);
        }
        catch (Exception e)
        {
            _simFaulted = true;
            SimSb.Append(" | SIM walk threw ").Append(e.GetType().Name)
                 .Append(" and has DISABLED ITSELF for this session; the SCENE and GFX lines are "
                         + "unaffected: ").Append(e.Message);
        }
        clock.Stop();
        return clock.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The behaviour census: how long Unity's per-frame script lists actually are, and which TYPES
    /// fill them.
    /// </summary>
    private static void AppendBehaviours(StringBuilder sb)
    {
        MonoBehaviour[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
        }
        catch (Exception e)
        {
            sb.Append(" | behaviours n/a (walk threw ").Append(e.GetType().Name).Append(')');
            return;
        }

        Behaviours.Clear();
        BehaviourOrder.Clear();

        int total = 0, enabled = 0;
        int upd = 0, late = 0, fixedUpd = 0;
        int modUpd = 0, modLate = 0;
        int tickingTypes = 0;

        for (int i = 0; i < all.Length; i++)
        {
            MonoBehaviour mb = all[i];
            if (mb == null)
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
          .Append(" | UNITY'S PER-FRAME LISTS (not a proxy — Unity walks exactly the enabled "
                  + "behaviours whose TYPE declares the method): Update ").Append(upd)
          .Append(", LateUpdate ").Append(late).Append(", FixedUpdate ").Append(fixedUpd)
          .Append(" — of which the mod's own are ").Append(modUpd).Append(" Update and ")
          .Append(modLate).Append(" LateUpdate (").Append(upd > 0 ? (100f * modUpd / upd).ToString("F1") : "0")
          .Append("% of the Update list), so everything else on those lists is the game's and "
                  + "cannot be removed, only stopped from existing")
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
    private static void AppendAnimators(StringBuilder sb)
    {
        Animator[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<Animator>();
        }
        catch (Exception e)
        {
            sb.Append(" | animators n/a (walk threw ").Append(e.GetType().Name).Append(')');
            return;
        }

        int enabled = 0, controller = 0, human = 0, rootMotion = 0, layers = 0, noRenderer = 0;
        int always = 0, cullTransforms = 0, cullCompletely = 0, otherMode = 0;

        for (int i = 0; i < all.Length; i++)
        {
            Animator a = all[i];
            if (a == null)
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
            if (on && a.GetComponentInChildren<Renderer>(true) == null)
                noRenderer++;
        }

        sb.Append(" | ANIMATORS: ").Append(all.Length).Append(" on active GameObject(s), ")
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
            return;
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
    private static void AppendParticles(StringBuilder sb)
    {
        ParticleSystem[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
        }
        catch (Exception e)
        {
            sb.Append(" | particles n/a (walk threw ").Append(e.GetType().Name).Append(')');
            return;
        }

        int playing = 0, emitting = 0, live = 0, looping = 0;
        int automatic = 0, pauseCatchup = 0, pause = 0, alwaysSimulate = 0;

        for (int i = 0; i < all.Length; i++)
        {
            ParticleSystem p = all[i];
            if (p == null)
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

        sb.Append(" | PARTICLES: ").Append(all.Length).Append(" system(s) on active GameObject(s), ")
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
        // …and the glow-card census after it, still before GFX: GFX is the line that prints
        // depthTextureMode as one field among thirty, and GLOW CARDS is the line that says what
        // that one field COSTS in this room. Reading them adjacent is the point.
        GlowCardCensus.Log();

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
        AppendLights(sb);
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
            LODGroup[] groups = UnityEngine.Object.FindObjectsOfType<LODGroup>();
            int enabled = 0;
            for (int i = 0; i < groups.Length; i++)
            {
                if (groups[i] != null && groups[i].enabled)
                    enabled++;
            }
            sb.Append(" | LOD groups: ").Append(groups.Length).Append(" active, ").Append(enabled)
              .Append(" enabled");
            if (groups.Length == 0)
            {
                sb.Append(" — NONE, so lodBias and maximumLODLevel above are inert here and there "
                          + "is no LOD lever to pull. Distance-based reduction would have to come "
                          + "from Camera.layerCullDistances instead");
            }
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
