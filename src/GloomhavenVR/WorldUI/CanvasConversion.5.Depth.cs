using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 5 (per-host depth compose). NEW members only — appended after parts
// 1-4 in the filename sort, so the existing member/static-initializer order (which the
// refactor guard tracks and part 1's header explains) is untouched.

internal static partial class CanvasConversion
{
    // ---- per-host depth-compose mask (user: initiative portraits BLEND with a floated menu) --
    //
    // ROOT CAUSE. Converted hosts are world-space canvases that write NO depth (uGUI shaders,
    // deliberate — hands/board must keep occluding them), so between two converted panels the
    // draw order alone decides who paints over whom. The floated modal host is deliberately
    // lifted to ModalFallback.ModalHostSortingOrder (1000) to kill the equal-order distance
    // jitter — but Unity sorts by sortingOrder BEFORE distance, so the menu draws AFTER every
    // order-0 host (initiative track, decision dock, stat panels, use bars, damage tooltip...)
    // even when it is spatially BEHIND them: the menu alpha-blends over the already-drawn
    // portraits and "shines through" them. Equal-order pairs are no better — their tie falls
    // to camera-distance measured per CANVAS (not per pixel), which jitters with head motion.
    //
    // FIX (the proven pattern in this repo — GrabbableModal.BuildDepthMask and the
    // ModalCloseButton depth stamp): every converted host gets a color-invisible depth-WRITING
    // per-graphic quad mesh a hair behind its content plane. All masks render at queue 2999
    // (after every opaque draw, before all ~3000 canvas content, on both transparent-sort
    // axes: renderer sortingOrder stays 0), so by the time ANY panel's content draws, EVERY
    // panel plane's depth is already stamped — and each content pass ZTest-LEquals against it:
    // the nearer panel wins PER PIXEL, independent of sortingOrder, draw order, or camera
    // jitter. Farther content fails under nearer stamps; nearer content passes over farther
    // stamps; hands/board (opaque, queue ≤2500) still beat everything; gaps between graphics
    // stay depth-open (per-graphic quads + the MaskMinAlpha floor + non-rendering-emitter
    // exclusions — all shared with the modal mask via CollectVisibleMaskRects). Fan/tray cards
    // need nothing: their backing slabs are depth-writing AlphaTest (queue 2450) geometry, so
    // they already stamp their own footprint before any mask or canvas draws.
    //
    // WHY NOT per-frame distance-derived sorting orders instead: order is per CANVAS — it can
    // never resolve interpenetrating/oblique panels per pixel, and rewriting orders each frame
    // re-introduces exactly the frame-to-frame swap flicker the dominant modal order was added
    // to kill. The depth stamp is order-independent and already proven twice in this repo.

    /// <summary>Mask render queue — after all opaques (>2500), before all canvas content
    /// (~3000); identical to <c>GrabbableModal.DepthMaskQueue</c> for the same ordering proof.</summary>
    private static readonly int HostMaskQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 1; // 2999

    /// <summary>
    /// Fixed pad (host px ≈ mm at the default 1 mm/px) the mask sits BEHIND the deepest emitted
    /// graphic (<see cref="LastMaskMaxZ"/> — the initiative row's normalized recession is real
    /// +Z the mask must clear, or the receded portraits would fail their own panel's ZTest).
    /// Big enough never to z-fight coplanar content, tiny against any real panel-to-panel gap.
    /// </summary>
    private const float HostMaskBehindPx = 3f;

    /// <summary>Quad cap per host — matches the modal mask's cap; overflow merges into the last
    /// slot inside <see cref="CollectVisibleMaskRects"/> (coverage kept, gap fidelity degrades).</summary>
    private const int HostMaskMaxQuads = 256;

    /// <summary>Padding (host px) around each quad — bridges antialiased glyph/sprite edges
    /// without re-closing the honest gaps between rows (same value class as the modal mask).</summary>
    private const float HostMaskQuadPaddingPx = 3f;

    // Scratch (single-threaded ticks; shared across all hosts, cleared per use).
    private static readonly List<Vector4> HostMaskRectScratch = new(HostMaskMaxQuads);
    private static readonly List<Vector3> HostMaskVertScratch = new(HostMaskMaxQuads * 4);
    private static readonly List<int> HostMaskTriScratch = new(HostMaskMaxQuads * 6);

    /// <summary>One-shot per-session log that the depth-capable shader is missing (log hygiene).</summary>
    private static bool s_hostMaskShaderWarned;

    /// <summary>
    /// Per-frame service (called from <see cref="Tick"/> for every live panel): keep the host's
    /// depth-compose mask matching its visible content. Skips hosts whose
    /// <see cref="GrabbableModal"/> already owns a coplanar mask
    /// (<see cref="ConvertedPanel.HostDepthMaskSuppressed"/>) and hides the mask whenever the
    /// host itself is render-hidden (reveal-pending modals, tray-hidden docks) — a stamp for an
    /// invisible panel would punch an invisible hole into everything behind it. Mesh rebuilds
    /// are hash-gated exactly like the modal mask; a static panel costs one Graphic walk.
    /// </summary>
    private static void TickHostDepthMask(ConvertedPanel panel)
    {
        if (panel.HostGo == null || panel.HostRect == null)
            return;
        bool wanted = !panel.HostDepthMaskSuppressed
                      && panel.HostCanvas != null && panel.HostCanvas.enabled
                      && panel.HostGo.activeInHierarchy;
        if (!wanted)
        {
            if (panel.HostDepthMask != null && panel.HostDepthMask.gameObject.activeSelf)
                panel.HostDepthMask.gameObject.SetActive(false);
            return;
        }

        int count = CollectVisibleMaskRects(panel, HostMaskRectScratch, HostMaskMaxQuads);
        if (count == 0)
        {
            // Nothing visible (fade-in, emptied panel) — no stamp at all.
            if (panel.HostDepthMask != null && panel.HostDepthMask.gameObject.activeSelf)
                panel.HostDepthMask.gameObject.SetActive(false);
            return;
        }
        float maxZ = LastMaskMaxZ; // capture before anything else runs a collection pass

        if (panel.HostDepthMask == null)
            BuildHostDepthMask(panel);
        if (panel.HostDepthMask == null || panel.HostDepthMaskMesh == null)
            return; // build failed (shader missing warning already logged)

        // Rebuild gate: whole-pixel quantized hash — sub-pixel layout jitter never rebuilds,
        // any real change (turn advance, reorder slide, scroll, toggle) does.
        int hash = 17;
        for (int i = 0; i < count; i++)
        {
            Vector4 r = HostMaskRectScratch[i];
            hash = hash * 31 + Mathf.RoundToInt(r.x);
            hash = hash * 31 + Mathf.RoundToInt(r.y);
            hash = hash * 31 + Mathf.RoundToInt(r.z);
            hash = hash * 31 + Mathf.RoundToInt(r.w);
        }
        if (hash != panel.HostDepthMaskHash)
        {
            panel.HostDepthMaskHash = hash;
            RebuildHostDepthMaskMesh(panel.HostDepthMaskMesh, count);
        }

        if (!panel.HostDepthMask.gameObject.activeSelf)
            panel.HostDepthMask.gameObject.SetActive(true);
        // Parented under HostRect: local units ARE host px (the host's own localScale carries
        // px→metres and every tray/diorama/grab scale on top), so the mask tracks re-fits,
        // re-docks and drags with zero per-frame math. +Z is away from the viewer (converted
        // panels face −Z); seat behind the deepest content pixel plus the fixed pad.
        Vector3 pos = panel.HostDepthMask.localPosition;
        float z = Mathf.Max(0f, maxZ) + HostMaskBehindPx;
        if (!Mathf.Approximately(pos.z, z))
            panel.HostDepthMask.localPosition = new Vector3(0f, 0f, z);
    }

    /// <summary>
    /// Lazily build the host's mask renderer: mod-owned, collider-less (never a poke/laser
    /// target), on the MOD layer regardless of the host's own layer — non-modal hosts stay on
    /// the game UI layer, which the game's mono UI Camera also renders, and a depth stamp
    /// double-drawn into a screen-space UI pass would depth-cut the 2D composite; the mod layer
    /// is head-camera-only by policy (docs/CAMERA-POLICY.md §2). Material = the exact
    /// GrabbableModal.BuildDepthMask state: ZWrite 1 / ZTest LEqual / Cull Off / Blend Zero One
    /// (framebuffer colour untouched — depth only), queue <see cref="HostMaskQueue"/>,
    /// renderer sortingOrder left at 0.
    /// </summary>
    private static void BuildHostDepthMask(ConvertedPanel panel)
    {
        var maskGo = new GameObject("GloomhavenVR.HostDepthMask");
        maskGo.layer = VRLayers.ModLayer;
        maskGo.transform.SetParent(panel.HostRect, worldPositionStays: false);
        maskGo.transform.localRotation = Quaternion.identity;
        maskGo.transform.localPosition = new Vector3(0f, 0f, HostMaskBehindPx);
        maskGo.transform.localScale = Vector3.one; // vertices in host px; the host scale converts

        panel.HostDepthMaskMesh = new Mesh { name = "GloomhavenVR.HostDepthMask" };
        panel.HostDepthMaskMesh.MarkDynamic();
        panel.HostDepthMaskHash = int.MinValue; // sentinel: first sync always builds
        maskGo.AddComponent<MeshFilter>().sharedMesh = panel.HostDepthMaskMesh;

        var mr = maskGo.AddComponent<MeshRenderer>();
        Material mat = WorldUIAssets.CreateFlatMaterial(Color.clear, overlay: true);
        bool depthCapable = mat.HasProperty("_ZWrite") && mat.HasProperty("_ZTest");
        if (mat.HasProperty("_ZWrite")) mat.SetInt("_ZWrite", 1);     // WRITE depth (stamp the panel plane)
        if (mat.HasProperty("_ZTest")) mat.SetInt("_ZTest", 4);       // LEqual — nearer hands/board/panels win
        if (mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);         // two-sided (hosts get viewed obliquely)
        if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", 0); // Zero ┐ colour = 0*src + 1*dst
        if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", 1); // One  ┘   = dst (UNCHANGED)
        mat.renderQueue = HostMaskQueue;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        // sortingOrder deliberately left 0: below every host canvas order on the order axis and
        // below content on the queue axis — the stamp is always down before any panel paints.

        panel.HostDepthMask = maskGo.transform;

        if (depthCapable)
        {
            VRLog.Info("WorldUI", $"HOST DEPTH-MASK: '{panel.HostGo.name}' created — converted panels now " +
                                  "depth-compose per pixel (nearer panel wins; a floated menu behind the " +
                                  "initiative track can no longer blend through its portraits).");
        }
        else if (!s_hostMaskShaderWarned)
        {
            s_hostMaskShaderWarned = true;
            VRLog.Warn("WorldUI", "HOST DEPTH-MASK: the 'GloomhavenVR/Overlay' shader (gloomhavenvr.bundle) " +
                                  "is unavailable — host masks cannot write depth, so converted panels keep " +
                                  "composing by sorting order alone (menus may blend through HUD panels).");
        }
    }

    /// <summary>Emit one padded quad per collected rect (host-local px, z = 0 — the transform
    /// carries the behind-plane offset). Same mesh economy as the modal mask: ≤256 quads,
    /// winding irrelevant (two-sided material).</summary>
    private static void RebuildHostDepthMaskMesh(Mesh mesh, int count)
    {
        HostMaskVertScratch.Clear();
        HostMaskTriScratch.Clear();
        for (int i = 0; i < count; i++)
        {
            Vector4 r = HostMaskRectScratch[i];
            float x0 = r.x - HostMaskQuadPaddingPx;
            float y0 = r.y - HostMaskQuadPaddingPx;
            float x1 = r.z + HostMaskQuadPaddingPx;
            float y1 = r.w + HostMaskQuadPaddingPx;
            int b = HostMaskVertScratch.Count;
            HostMaskVertScratch.Add(new Vector3(x0, y0, 0f));
            HostMaskVertScratch.Add(new Vector3(x1, y0, 0f));
            HostMaskVertScratch.Add(new Vector3(x1, y1, 0f));
            HostMaskVertScratch.Add(new Vector3(x0, y1, 0f));
            HostMaskTriScratch.Add(b);
            HostMaskTriScratch.Add(b + 1);
            HostMaskTriScratch.Add(b + 2);
            HostMaskTriScratch.Add(b);
            HostMaskTriScratch.Add(b + 2);
            HostMaskTriScratch.Add(b + 3);
        }
        mesh.Clear();
        mesh.SetVertices(HostMaskVertScratch);
        mesh.SetTriangles(HostMaskTriScratch, 0);
    }

    /// <summary>Free the mask's MESH asset when a host dies (the GameObject cascades with
    /// HostGo, the Mesh does not). Called by <see cref="Release"/> and the dead-panel prune.</summary>
    private static void DestroyHostDepthMask(ConvertedPanel panel)
    {
        if (panel.HostDepthMaskMesh != null)
        {
            Object.Destroy(panel.HostDepthMaskMesh);
            panel.HostDepthMaskMesh = null;
        }
        panel.HostDepthMask = null;
    }
}
