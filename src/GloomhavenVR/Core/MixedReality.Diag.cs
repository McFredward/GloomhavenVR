using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// ROUND 16 — THE DIAGNOSTIC ROUND. Not a fix: fifteen rounds of "put opaque dark geometry behind
/// the translucent surface" produced zero change at the tile rim, and round 15's own instruments
/// proved the geometry EXISTS (ModBuild-74 log: 226 prisms built, 226 renderers carrying underlay +
/// wafer, "UNBACKED PREVIEW RENDERERS — none"). Round 14 (opaque + ZWrite) and its revert (round 15)
/// are OPPOSITE changes with the SAME visual result — the strongest possible hint that the pixels
/// the user sees are not produced by anything this code has ever touched.
///
/// WHAT THIS FILE ADDS (three instruments, all MR-only, all off the hot path)
/// -------------------------------------------------------------------------
/// 1. RIM POPULATION (<see cref="LogRimPopulation"/>) — every round so far dumped a SAMPLE piece or
///    a predicate-filtered list (translucent-by-probe, family-named, Preview-descendant). All three
///    filters are exactly what a renderer we never see would fail. This dump takes the OUTER
///    silhouette pieces of the unseen region — derived geometrically, from neighbour counts, with NO
///    material predicate whatsoever — and prints every renderer standing there with its full render
///    state, whether the mod backed it, and how deep its nearest 'Preview' ancestor is (the
///    depth-12 cap in <see cref="UnderPreviewNode"/> is a live suspect: a renderer deeper than that
///    is invisible to the sweep AND to the unbacked instrument, which only records
///    Preview-descendants — a silent false all-clear).
///
///    ARITHMETIC THAT MOTIVATED IT (read off the ModBuild-74 log, WallSegmentFade MAPTILE dumps):
///    a preview subtree with backings has 603 / 225 / 171 renderers — all exactly divisible by 9 —
///    while the one with content generated but backings not yet built has 66 = 22x3. So the kit is
///    3 authored renderers per hex ('Simple Tile', 'EN_Unseen_FloorHex_Edge_Damage_03_PR',
///    'EN_CR_FloorTiles_Damaged_03') and the mod adds 6 = 2x3 backings per hex. TWO of the three
///    authored pieces are backed; 'Simple Tile' — the one whose bounds span the FULL block height
///    y-0.4..-0.1, i.e. the vertical cliff the user photographs — carries NO backing child, and the
///    unbacked instrument still says "none". This dump is what turns that inference into a fact
///    (or refutes it) in one hardware round.
///
/// 2. CAMERA SETUP (<see cref="LogCameraSetup"/>) — verify, do not assume: if the tiles were drawn
///    by a camera our backings' layer is not in, our dark geometry would be culled while the tiles
///    render, and every round would have looked exactly like the fifteen we have had. Prints the
///    head camera's clear flags / background colour / culling mask, every other live camera's, which
///    of them include the tile layer, and an audit that every mod backing really did inherit its
///    source's layer.
///
/// 3. DEBUG TINT (in <see cref="MixedReality"/> proper, config key
///    '[MixedReality] UnseenBackingDebugColors') — same geometry, same queue, same blend/depth
///    state, only the COLOUR changes: coplanar underlay = blue, top wafer = magenta, rim curtain =
///    red. One screenshot then says which of our surfaces reach the screen and where they land. If
///    none of the three colours appears anywhere, the backing strategy is provably dead — which is
///    the single most valuable thing this round can learn.
///
/// Everything here is ONE-SHOT per MR session, gated on a SETTLED backing count (Apparance
/// regenerates tile content constantly; a dump taken mid-regen would describe a half-built region),
/// and reset with the underlays.
/// </summary>
internal static partial class MixedReality
{
    /// <summary>One-shot latches (reset in <see cref="RestoreUnseenUnderlays"/>).</summary>
    private static bool _rimPopLogged;
    private static bool _camDumpLogged;

    /// <summary>Backing count seen by the previous sweep — the dumps fire only when it is UNCHANGED,
    /// i.e. the region has finished regenerating. -1 = no sweep with backings yet.</summary>
    private static int _diagPrevBackingCount = -1;

    /// <summary>Neighbour radius as a multiple of a piece's own larger XZ bound. A hex's six
    /// neighbours sit one footprint away (~1.75-2.0 wu for the pieces in the log); 1.15x catches
    /// all of them and stops well short of the second ring (~3 wu).</summary>
    private const float RimNeighborRadiusFactor = 1.15f;

    /// <summary>How many distinct OUTER-silhouette hex positions the population dump samples.</summary>
    private const int RimHexesSampled = 4;

    /// <summary>Two sampled positions closer than this (XZ) count as the same hex.</summary>
    private const float RimHexSeparationWu = 0.75f;

    /// <summary>Cap on renderer lines in the population dump (the total is always reported).</summary>
    private const int RimPopMaxListed = 12;

    /// <summary>Per-hex line cap, so the total cap cannot spend itself on the first position and
    /// leave the other sampled rim hexes unreported.</summary>
    private const int RimPopPerHexMax = 4;

    /// <summary>Slack added to a rim piece's bounds when collecting the renderers standing at it —
    /// the co-located pieces of one hex have slightly different footprints.</summary>
    private const float RimPopGatherSlackWu = 0.25f;

    // One-shot scratch (this whole file runs at most twice per MR session).
    private static readonly List<Renderer> DiagSources = new(256);
    private static readonly List<Renderer> DiagPopulation = new(32);

    /// <summary>
    /// Called at the end of every unseen sweep. Fires the two one-shot dumps once the backing
    /// population has stopped changing, so both describe a settled region rather than a
    /// mid-regeneration snapshot.
    /// </summary>
    private static void TickUnseenDiagnostics(Renderer[] all)
    {
        int count = UnseenUnderlays.Count;
        if (count == 0)
        {
            _diagPrevBackingCount = -1;
            return;
        }
        bool settled = count == _diagPrevBackingCount;
        _diagPrevBackingCount = count;
        if (!settled)
            return;

        if (!_camDumpLogged)
        {
            _camDumpLogged = true;
            LogCameraSetup();
        }
        if (!_rimPopLogged)
        {
            _rimPopLogged = true;
            LogRimPopulation(all);
        }
    }

    /// <summary>
    /// THE dump this round exists for: the population standing on the unseen region's OUTER
    /// silhouette, chosen geometrically and printed WITHOUT any material filter.
    ///
    /// <para>Silhouette derivation: for every backed source, count the other backed sources whose
    /// centre lies within <see cref="RimNeighborRadiusFactor"/> footprints of it. An interior hex is
    /// surrounded on all six sides; a rim hex is not. Sorting by that count ascending (ties broken by
    /// distance from the region centre, farthest first) puts the true outer pieces at the top with no
    /// assumption about the hex grid, the board's rotation or the region's shape. Up to
    /// <see cref="RimHexesSampled"/> distinct positions are sampled, and at each one EVERY non-mod
    /// renderer overlapping the piece is listed — including the ones no sweep predicate ever
    /// matched, which is the entire point.</para>
    /// </summary>
    private static void LogRimPopulation(Renderer[] all)
    {
        DiagSources.Clear();
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            Renderer s = UnseenUnderlays[i].Source;
            if (s != null)
                DiagSources.Add(s);
        }
        int n = DiagSources.Count;
        if (n == 0)
            return;

        var centers = new Vector3[n];
        var radii = new float[n];
        Vector3 mean = Vector3.zero;
        for (int i = 0; i < n; i++)
        {
            Bounds b = DiagSources[i].bounds;
            centers[i] = b.center;
            radii[i] = RimNeighborRadiusFactor * Mathf.Max(b.size.x, b.size.z);
            mean += b.center;
        }
        mean /= n;

        var neighbours = new int[n];
        for (int i = 0; i < n; i++)
        {
            float r2 = radii[i] * radii[i];
            int c = 0;
            for (int j = 0; j < n; j++)
            {
                if (j == i)
                    continue;
                float dx = centers[j].x - centers[i].x;
                float dz = centers[j].z - centers[i].z;
                if (dx * dx + dz * dz <= r2)
                    c++;
            }
            neighbours[i] = c;
        }

        var order = new int[n];
        for (int i = 0; i < n; i++)
            order[i] = i;
        Array.Sort(order, (a, b) =>
        {
            int cmp = neighbours[a].CompareTo(neighbours[b]);
            if (cmp != 0)
                return cmp; // fewest neighbours first — the outer silhouette
            float da = (centers[a] - mean).sqrMagnitude;
            float db = (centers[b] - mean).sqrMagnitude;
            return db.CompareTo(da); // then farthest from the region centre
        });

        VRLog.Info("Core", $"MR: RIM POPULATION — the unseen region has {n} backed source(s), " +
                           $"centre {Fmt(mean)}. The OUTER silhouette is derived from neighbour " +
                           "counts (no material/name/Preview predicate — this dump must be able to " +
                           "see a renderer every sweep filter misses). Sampling the " +
                           $"{RimHexesSampled} least-surrounded position(s); every non-mod renderer " +
                           "standing at each one is listed with its full render state:");

        DiagPopulation.Clear();
        var picked = new List<Vector3>(RimHexesSampled);
        var pickedNeighbours = new List<int>(RimHexesSampled);
        int listed = 0;
        int backedAtRim = 0;
        int unbackedAtRim = 0;
        int totalAtRim = 0;

        for (int oi = 0; oi < n && picked.Count < RimHexesSampled; oi++)
        {
            int si = order[oi];
            Vector3 c = centers[si];
            bool dup = false;
            for (int p = 0; p < picked.Count; p++)
            {
                float dx = picked[p].x - c.x;
                float dz = picked[p].z - c.z;
                if (dx * dx + dz * dz < RimHexSeparationWu * RimHexSeparationWu)
                {
                    dup = true;
                    break;
                }
            }
            if (dup)
                continue;
            picked.Add(c);
            pickedNeighbours.Add(neighbours[si]);

            Bounds gather = DiagSources[si].bounds;
            gather.Expand(RimPopGatherSlackWu * 2f);
            DiagPopulation.Clear();
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null)
                    continue;
                if (r.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                    continue; // our own backings are counted per source, below
                if (!gather.Intersects(r.bounds))
                    continue;
                DiagPopulation.Add(r);
            }

            totalAtRim += DiagPopulation.Count;
            // Two passes: UNBACKED renderers first. They are the diagnostic payload (a piece the
            // sweep never matched is exactly what fifteen rounds could not see), so no cap may
            // ever hide one behind a backed neighbour.
            int listedThisHex = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < DiagPopulation.Count; i++)
                {
                    Renderer r = DiagPopulation[i];
                    bool modBacked = UnseenSources.Contains(r.GetInstanceID());
                    if (modBacked != (pass == 1))
                        continue;
                    if (modBacked)
                        backedAtRim++;
                    else
                        unbackedAtRim++;
                    if (listed >= RimPopMaxListed || listedThisHex >= RimPopPerHexMax)
                        continue;
                    listed++;
                    listedThisHex++;
                    LogRimRenderer(r, picked.Count, pickedNeighbours[picked.Count - 1], c, modBacked);
                }
            }
        }

        VRLog.Info("Core", $"MR: RIM POPULATION — {totalAtRim} renderer(s) stand at the " +
                           $"{picked.Count} sampled rim position(s): {backedAtRim} carry a mod " +
                           $"backing, {unbackedAtRim} do NOT" +
                           (listed < totalAtRim ? $" (first {listed} listed above)" : string.Empty) +
                           ". A piece listed with modBacked=NO whose bounds cover the region's " +
                           "vertical cliff (y-span ~0.3 wu) is the geometry every previous round " +
                           "was blind to — its shader/queue/tag/previewDepth on the same line say " +
                           "exactly which signal the sweep needs to match it.");
        DiagPopulation.Clear();
        DiagSources.Clear();
    }

    /// <summary>One renderer's full identity + render state + backing status. The one line this
    /// round is for; deliberately verbose (it runs at most 12 times per MR session).</summary>
    private static void LogRimRenderer(Renderer r, int hexIndex, int hexNeighbours, Vector3 hexCenter,
                                       bool modBacked)
    {
        GameObject go = r.gameObject;
        Material? m = r.sharedMaterial;
        Material[] mats = r.sharedMaterials;
        Bounds b = r.bounds;

        int backingChildren = 0;
        Transform t = r.transform;
        for (int i = 0; i < t.childCount; i++)
        {
            if (t.GetChild(i).name.StartsWith("GloomhavenVR.Mr", StringComparison.Ordinal))
                backingChildren++;
        }

        bool family = HasUnseenName(go.name);
        bool translucent = false;
        if (mats != null)
        {
            for (int i = 0; i < mats.Length; i++)
            {
                if (IsUnseenFamilyMaterial(mats[i]))
                    family = true;
                if (IsTranslucent(mats[i]))
                    translucent = true;
            }
        }
        int previewDepth = PreviewAncestorDepth(t);
        string previewNote = previewDepth < 0
            ? "none in 64 levels"
            : previewDepth <= 12
                ? $"{previewDepth} (within the sweep's cap)"
                : $"{previewDepth} — DEEPER THAN THE SWEEP'S CAP OF 12, invisible to UnderPreviewNode";

        VRLog.Info("Core", $"MR:   rim[hex {hexIndex}/{RimHexesSampled} @ {Fmt(hexCenter)}, " +
                           $"{hexNeighbours} neighbour(s)] '{go.name}' " +
                           $"[{r.GetType().Name}] parent '{(t.parent != null ? t.parent.name : "<root>")}' " +
                           $"layer {LayerName(go.layer)} shader '{ShaderName(r)}' mat " +
                           $"'{(m != null ? m.name : "<none>")}' slots {(mats != null ? mats.Length : 0)} " +
                           $"queue {(m != null ? m.renderQueue : -1)} renderTypeTag " +
                           $"'{(m != null ? m.GetTag("RenderType", false, "<none>") : "<none>")}' " +
                           $"{StateProps(m)} bounds s{Fmt(b.size)} @ {Fmt(b.center)} | " +
                           $"enabled={r.enabled} activeInHierarchy={go.activeInHierarchy} " +
                           $"visibleLastFrame={r.isVisible} | modBacked={(modBacked ? "YES" : "NO")} " +
                           $"backingChildren={backingChildren} | familyNamed={family} " +
                           $"translucentByProbe={translucent} previewAncestorDepth={previewNote}.");
    }

    /// <summary>Blend/depth state of a material, value or 'hardcoded' (a pass that fixes its own
    /// state exposes no property — the documented blind spot of every probe in this file).</summary>
    private static string StateProps(Material? m)
    {
        if (m == null)
            return "_SrcBlend=<no mat> _DstBlend=<no mat> _ZWrite=<no mat>";
        return $"_SrcBlend={StateProp(m, "_SrcBlend")} _DstBlend={StateProp(m, "_DstBlend")} " +
               $"_ZWrite={StateProp(m, "_ZWrite")}";
    }

    private static string StateProp(Material m, string name) =>
        m.HasProperty(name) ? m.GetFloat(name).ToString("0.#") : "hardcoded";

    /// <summary>Depth (in transform levels) to the nearest ancestor named 'Preview', or -1. UNCAPPED
    /// at the sweep's 12 — measuring the real depth is the whole point: <see cref="UnderPreviewNode"/>
    /// stops at 12, and a renderer below that line is matched by nothing and RECORDED by nothing,
    /// which is how "UNBACKED PREVIEW RENDERERS — none" can be a false all-clear.</summary>
    private static int PreviewAncestorDepth(Transform t)
    {
        Transform? p = t;
        for (int depth = 0; p != null && depth < 64; depth++)
        {
            if (p.name == "Preview")
                return depth;
            p = p.parent;
        }
        return -1;
    }

    /// <summary>
    /// The camera half of the round: VERIFY the assumption that our backings land on a camera that
    /// draws them. Prints the head camera's clear/background/culling mask (the key is the background
    /// colour), every other live camera's, whether each includes the tile layer, and an audit that
    /// every mod backing inherited its source's layer. If the tile layer is missing from the head
    /// camera's mask, or backings sit on a layer the tiles do not, no amount of dark geometry could
    /// ever have appeared — and the whole strategy was aimed at a camera nobody sees.
    /// </summary>
    private static void LogCameraSetup()
    {
        Camera? head = VRCameraPolicy.AllowedHead;
        int tileLayer = -1;
        Renderer? sample = null;
        for (int i = 0; i < UnseenUnderlays.Count && sample == null; i++)
            sample = UnseenUnderlays[i].Source;
        if (sample != null)
            tileLayer = sample.gameObject.layer;

        int backings = 0;
        int layerMismatch = 0;
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            UnseenUnderlay e = UnseenUnderlays[i];
            if (e.Source == null)
                continue;
            int want = e.Source.gameObject.layer;
            if (e.Plate != null)
            {
                backings++;
                if (e.Plate.gameObject.layer != want)
                    layerMismatch++;
            }
            if (e.Fill != null)
            {
                backings++;
                if (e.Fill.gameObject.layer != want)
                    layerMismatch++;
            }
            if (e.Rim != null)
            {
                backings++;
                if (e.Rim.gameObject.layer != want)
                    layerMismatch++;
            }
        }

        VRLog.Info("Core", $"MR: CAMERA SETUP — head camera " +
                           $"'{(head != null ? head.name : "<none>")}' clear=" +
                           $"{(head != null ? head.clearFlags.ToString() : "<none>")} background=" +
                           $"{(head != null ? FmtColor(head.backgroundColor) : "<none>")} " +
                           $"cullingMask=0x{(head != null ? head.cullingMask : 0):X8} depth=" +
                           $"{(head != null ? head.depth : 0f):0.#} target=" +
                           $"{(head != null && head.targetTexture != null ? "RT" : "<screen>")} " +
                           $"stereo={(head != null ? head.stereoTargetEye.ToString() : "<none>")} " +
                           $"far={(head != null ? head.farClipPlane : 0f):0.#}. Tile layer = " +
                           $"{(tileLayer >= 0 ? LayerName(tileLayer) : "<no backed source>")}, mod " +
                           $"layer = {LayerName(VRLayers.ModLayer)}; head renders the tile layer: " +
                           $"{(head != null && tileLayer >= 0 && (head.cullingMask & (1 << tileLayer)) != 0 ? "YES" : "NO")}. " +
                           $"Backing layer audit: {backings} backing object(s), {layerMismatch} on a " +
                           "layer OTHER than their source's (0 = every backing is on the tiles' own " +
                           "layer, so no camera can draw one without the other).");

        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        int shown = 0;
        for (int i = 0; i < count && shown < 8; i++)
        {
            Camera cam = cams[i];
            if (cam == null)
                continue;
            shown++;
            bool rendersTiles = tileLayer >= 0 && (cam.cullingMask & (1 << tileLayer)) != 0;
            VRLog.Info("Core", $"MR:   camera '{cam.name}' enabled={cam.enabled} " +
                               $"clear={cam.clearFlags} background={FmtColor(cam.backgroundColor)} " +
                               $"cullingMask=0x{cam.cullingMask:X8} depth={cam.depth:0.#} target=" +
                               $"{(cam.targetTexture != null ? cam.targetTexture.name : "<screen>")} " +
                               $"stereo={cam.stereoTargetEye} rendersTileLayer=" +
                               $"{(rendersTiles ? "YES" : "NO")}" +
                               (head != null && cam == head ? " (THE HEAD CAMERA)" : string.Empty) + ".");
        }
        if (count > shown)
            VRLog.Info("Core", $"MR:   camera … +{count - shown} more live camera(s).");
    }

    private static string Fmt(Vector3 v) => $"({v.x:0.##},{v.y:0.##},{v.z:0.##})";

    private static string FmtColor(Color c) => $"rgba({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";
}
