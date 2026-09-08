// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not restated
// here. Part 5 answers exactly one question, and it is the question ModBuild 193 got wrong:
// WHAT GUARANTEES THAT A CAPTURE CAMERA SEES ITS OWN PANEL AND NOTHING ELSE?

using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- the capture-layer POOL -----------------------------------------------------------------

    /// <summary>
    /// <b>THE MODBUILD 193 DEFECT, READ FROM THE CODE AND CONFIRMED BY A PHOTOGRAPH.</b>
    ///
    /// <para>193 resolved ONE capture layer for the whole mod — the log says so in as many words:
    /// <i>"PANEL SUPERSAMPLE capture layer resolved: 26 (first unnamed layer scanning 31-&gt;8 that is
    /// not the mod layer 27; mask 0x04000000)"</i> — and every per-panel capture camera was built
    /// with <c>cullingMask = 1 &lt;&lt; thatOneLayer</c>. Every supersampled window's subtree was moved
    /// onto that same layer. A camera's culling mask is the ONLY thing that decided what it drew, so
    /// each panel's camera rendered EVERY supersampled panel that fell inside its orthographic
    /// frustum, straight into that panel's own render target.</para>
    ///
    /// <para>THE FRUSTUM IS NOT NARROW, WHICH IS WHY THIS FIRES IN NORMAL USE.
    /// <see cref="SyncProjection"/> puts the near/far planes half a FRAME HEIGHT either side of the
    /// panel plane (<c>slab = frameHeightWorld * 0.5</c>, <c>near = standoff - slab</c>,
    /// <c>far = standoff + slab</c>), i.e. the capture slab is as deep as the window is tall. The
    /// ModBuild 193 log's 'New Party display' framed 1611x1453 uGUI px, so its capture volume was
    /// 1611 x 1453 x 1453 uGUI px — around 165 x 149 x 149 world units at that window's own scale.
    /// The floated windows in this mod stand side by side on an arc at roughly one arm's length.
    /// Two neighbours are therefore routinely inside each other's capture volumes, and
    /// <c>.planning/debug/window_merge.jpg</c> is what that looks like: the floated quest card
    /// "Unheilvoller Riss" shows, inside its OWN rectangle and clipped to its own right edge, the
    /// merchant window's item rows (Beckenhaube, Ermächtigungstalisman, Falkenhelm, Hornhelm, Kette
    /// des Dunkelpaktes, Kettenhaube, Zahnkette). Those rows are drawn well to the right of the
    /// merchant window's own display quad, so they cannot be the merchant quad seen past an edge;
    /// they are inside the quest window's TEXTURE.</para>
    ///
    /// <para><b>THE FIX: ONE LAYER PER PANEL, NEVER SHARED.</b> Unity gives a project 32 layers and
    /// a camera culls by layer, so a private layer is the only per-camera visibility switch the
    /// engine offers that costs nothing per frame and cannot be raced by another writer. Each engaged
    /// panel takes a layer out of this pool for as long as it lives and hands it back on stand-down;
    /// its camera's mask is <c>1 &lt;&lt; itsOwnLayer</c> and nothing else in the scene is ever on that
    /// layer. That is EXACT, not probabilistic: it does not depend on how far apart two windows
    /// happen to stand, on their depth order, or on the size of anybody's frustum.</para>
    ///
    /// <para><b>WHAT WAS REJECTED, AND WHY.</b>
    /// <list type="number">
    /// <item>TIGHTENING THE ORTHO FRUSTUM until only the own panel is inside it. Rejected as
    /// unsound in principle. The near/far planes could be squeezed to a few world units around the
    /// panel plane, but the mod's windows sit on an ARC AT THE SAME RADIUS from the seat, so a
    /// neighbour is separated from the own panel mostly in X, not in depth — and the frustum's X
    /// extent is the frame width, which is exactly the quantity that must not shrink. Worse, uGUI
    /// content is coplanar by construction (<c>CanvasConversion</c>'s flatten pass drives local z to
    /// 0 across the subtree), so a coplanar neighbour that overlaps in X cannot be separated by ANY
    /// clip plane. A fix that works "when the windows are far enough apart" is exactly what the user
    /// ruled out: <i>"Verhindere solche Wechselwirkungen zwischen den verschiedenen Fenster
    /// komplett."</i></item>
    /// <item>PER-CAMERA EXCLUSION — in each capture camera's own <c>onPreCull</c>, disabling the
    /// OTHER panels' root <see cref="Canvas"/> components and restoring them in <c>onPostRender</c>.
    /// Rejected on TWO independent grounds. (a) It is forbidden by this lane's standing constraint:
    /// the host canvas must keep its pose, its rect, its <c>Canvas.enabled</c>, its
    /// <c>GraphicRaycaster</c> and its <c>UguiPokeSurfaces</c> registration — only the LAYER may
    /// move. <c>GraphicRaycaster.Raycast</c> returns immediately when its own canvas is disabled, and
    /// a poke or a laser hit that lands in the wrong slot of the camera loop would silently miss.
    /// (b) The cost is wrong even if it were allowed: toggling <c>Canvas.enabled</c> off and on marks
    /// the canvas dirty, and Unity rebuilds that canvas's batches on the next
    /// <c>willRenderCanvases</c> — with N panels each toggling the other N-1 every frame that is
    /// N x (N-1) canvas batch rebuilds per frame, on subtrees this log measures at 2391, 3363 and
    /// 4624 transforms.</item>
    /// <item>THE SAME EXCLUSION DONE BY LAYER instead of by <c>Canvas.enabled</c> (move the other
    /// panels' roots off the capture layer for the duration of one camera's render). Legal under the
    /// constraint, but rejected as UNPROVEN: whether moving only a canvas ROOT hides its whole
    /// subtree depends on whether Unity culls uGUI per-Canvas or per-CanvasRenderer, and this lane
    /// has no hardware to settle that with. Moving the whole subtree instead is O(transforms) per
    /// camera per frame — 4624 transforms x 4 cameras x 90 Hz. The pool is correct under EITHER
    /// culling model, because the entire subtree already lives on the panel's own layer for the whole
    /// of its life, and it costs nothing per frame.</item>
    /// </list></para>
    ///
    /// <para><b>THE PRICE, STATED HONESTLY: THE POOL IS SMALLER THAN <see cref="MaxPanels"/>, AND
    /// THAT CAPS CONCURRENCY.</b> The census below is printed once so the number is never guessed
    /// again. From the ModBuild 193 log, layers 0-2, 4-5, 9-19 and 28-31 carry names
    /// (the head camera's mask decode lists Default, TransparentFX, Ignore Raycast, Water, UI,
    /// Monster, Ground, Static, Character Controller, Ragdoll, Hex, Hovering, BuzzingInsects, Weapon,
    /// Particle, Wall, WaypointRenderTexture, RenderTarget, Outline, LevelEditorPlane) and 27 is the
    /// mod layer, so the expected pool is 20-26 = SEVEN layers. That is one fewer than the cap of
    /// eight raised in ModBuild 193, and the 193 session never held more than FOUR panels at once
    /// ("across 4 panel(s), cap 8" is the high-water mark of that log). A panel that cannot get a
    /// layer is REFUSED and keeps today's direct rendering — which is the dial's OFF behaviour and
    /// is already this class's documented degradation everywhere else. It is emphatically NOT
    /// allowed to share a layer with another panel, because that is the bug being fixed.</para>
    ///
    /// <para>The names are read with <see cref="LayerMask.LayerToName"/>, which answers from the
    /// project's TagManager and is therefore constant for the whole process — the census cannot go
    /// stale between scenes.</para>
    /// </summary>
    private static readonly List<int> LayerPool = new(8);

    /// <summary>Layers of <see cref="LayerPool"/> that are not currently assigned to an entry.</summary>
    private static readonly List<int> FreeLayers = new(8);

    /// <summary>OR of <c>1 &lt;&lt; layer</c> for every layer CURRENTLY held by a live entry. This is
    /// the mask <see cref="OnPreCull"/> clears from every camera that is not one of ours — it must be
    /// the union, not one bit, or a panel holding a second pool layer would be drawn into the eye as
    /// well as into its capture.</summary>
    private static int _poolMask;

    private static bool _poolResolved;

    /// <summary>How many panels can be isolated at once. Zero stands the whole path down.</summary>
    private static int PoolSize
    {
        get
        {
            ResolvePool();
            return LayerPool.Count;
        }
    }

    /// <summary>The real concurrency cap: the smaller of the authored cap and the layer pool. Read
    /// everywhere <see cref="MaxPanels"/> used to be read, so a pool smaller than the cap can never
    /// be silently exceeded.</summary>
    private static int EffectiveMaxPanels
    {
        get
        {
            int pool = PoolSize;
            return pool < MaxPanels ? pool : MaxPanels;
        }
    }

    /// <summary>
    /// Resolve the pool once and PRINT THE CENSUS. The census is the point: ModBuild 193's log said
    /// only which single layer it took, so nothing in it could tell a reader how many were available
    /// — which is precisely the number this design lives or dies on.
    /// </summary>
    private static void ResolvePool()
    {
        if (_poolResolved)
            return;
        _poolResolved = true;
        int mod = VRLayers.ModLayer;
        var named = new StringBuilder(256);
        int namedCount = 0;
        for (int i = 31; i >= 8; i--)
        {
            string name = LayerMask.LayerToName(i);
            if (!string.IsNullOrEmpty(name))
            {
                namedCount++;
                if (namedCount > 1)
                    named.Append(", ");
                named.Append(i).Append(':').Append(name);
                continue;
            }
            if (i == mod)
                continue;
            LayerPool.Add(i);
        }
        FreeLayers.Clear();
        FreeLayers.AddRange(LayerPool);
        // AcquireLayer pops the LAST element, and the pool was built scanning 31->8, so reversing
        // here makes the FIRST panel take the HIGHEST free layer — 26 on this game, i.e. exactly the
        // layer ModBuild 193's single shared capture layer used. That keeps the first line of a
        // hardware log directly comparable with the previous build's; it has no other effect.
        FreeLayers.Reverse();

        var pool = new StringBuilder(64);
        for (int i = 0; i < LayerPool.Count; i++)
        {
            if (i > 0)
                pool.Append(", ");
            pool.Append(LayerPool[i]);
        }

        if (LayerPool.Count == 0)
        {
            // HW-VERIFY (2026-09 refactor, F-64) — the once-per-session census the stand-down
            // line in .1.Core.cs refers the reader to. A referring line at a printing tier pointing
            // at a referent at a dropped one is not a reference.
            VRLog.Note(Scope, "PANEL SUPERSAMPLE capture-layer POOL is EMPTY: every layer 8-31 is "
                              + $"either named ({namedCount} named: {named}) or is the mod layer "
                              + $"{mod}. THE CONSEQUENCE: no window can be supersampled at all, "
                              + "because a capture camera with no private layer would have to share "
                              + "one — and sharing is exactly the ModBuild 193 defect in which one "
                              + "window's capture contained its neighbour (window_merge.jpg). Every "
                              + "floated window therefore keeps today's direct rendering, i.e. the "
                              + "dial's OFF behaviour including the reported text and edge shimmer. "
                              + "Nothing else changes.");
            return;
        }

        // HW-VERIFY (2026-09 refactor, F-64) — the other branch of the same once-per-session
        // census; the two must be readable in the same log or neither is.
        VRLog.Note(Scope, $"PANEL SUPERSAMPLE capture-layer POOL resolved: {LayerPool.Count} private "
                          + $"layer(s) [{pool}], scanning 31->8 for unnamed layers and excluding the "
                          + $"mod layer {mod}. LAYER CENSUS 8-31 — {namedCount} named: {named}; "
                          + $"1 mod layer: {mod}; {LayerPool.Count} free and now reserved by this "
                          + "path. HOW TO READ THIS. EVERY supersampled panel gets its OWN layer for "
                          + "as long as it is engaged, and its capture camera's culling mask is that "
                          + "one bit — so a capture camera CANNOT see another panel, at any distance, "
                          + "at any overlap, in any depth order. That is the ModBuild 194 fix for the "
                          + "user's report that the quest window was drawing the merchant window's "
                          + "item rows inside itself: ModBuild 193 gave every camera the SAME layer, "
                          + $"so each one captured every panel inside its frustum. The cap on "
                          + $"simultaneous supersampled windows is therefore min(MaxPanels "
                          + $"{MaxPanels}, pool {LayerPool.Count}) = {EffectiveMaxPanels}; a window "
                          + "beyond that is refused and keeps today's direct rendering (the OFF "
                          + "behaviour) rather than sharing a layer. IF THAT CAP IS EVER REACHED IN "
                          + "PRACTICE the 'cap reached' Warn says so by name, and the only honest "
                          + "levers are to free a named layer or to accept the refusal — never to "
                          + "share.");
    }

    /// <summary>Take a private layer, or -1 when the pool is exhausted. Never throws.</summary>
    private static int AcquireLayer()
    {
        ResolvePool();
        if (FreeLayers.Count == 0)
            return -1;
        int last = FreeLayers.Count - 1;
        int layer = FreeLayers[last];
        FreeLayers.RemoveAt(last);
        _poolMask |= 1 << layer;
        return layer;
    }

    /// <summary>Hand a layer back. Idempotent: releasing a layer twice cannot corrupt the pool.</summary>
    private static void ReleaseLayer(int layer)
    {
        if (layer < 0)
            return;
        _poolMask &= ~(1 << layer);
        for (int i = 0; i < FreeLayers.Count; i++)
        {
            if (FreeLayers[i] == layer)
                return; // already free
        }
        for (int i = 0; i < LayerPool.Count; i++)
        {
            if (LayerPool[i] == layer)
            {
                FreeLayers.Add(layer);
                return;
            }
        }
    }

    /// <summary>
    /// Is <paramref name="layer"/> a pool layer held by an entry OTHER than <paramref name="e"/>?
    /// The layer sweep asks this before it moves a transform, and skips that transform AND its
    /// children when the answer is yes.
    /// <para>WHY IT IS HERE EVEN THOUGH THE SUBTREES SHOULD BE DISJOINT: each converted panel gets
    /// its own scene-root host GameObject (<c>CanvasConversion.1.Core.cs</c>,
    /// <c>new GameObject($"GloomhavenVR.Panel_{name}")</c>), so two panels' subtrees do not overlap
    /// today. If that ever stopped being true, two entries would drag the same transform between
    /// their layers every frame and the value would ALTERNATE — this project's "don't win a write
    /// war" failure exactly, and under MultiPass the two eyes would sample different sides of it.
    /// One mask test per transform buys the guarantee outright, so the guarantee does not rest on an
    /// invariant in a file this lane does not own.</para>
    /// </summary>
    private static bool IsForeignPoolLayer(Entry e, int layer)
    {
        if (layer < 0 || layer == e.Layer)
            return false;
        return (_poolMask & (1 << layer)) != 0;
    }

    // ---- the isolation falsifier ----------------------------------------------------------------

    /// <summary>Frustum planes scratch for <see cref="ForeignPanelsInFrustum"/>. Six planes, reused;
    /// the call runs once per panel per report window, never per frame.</summary>
    private static readonly Plane[] IsoPlanes = new Plane[6];

    /// <summary>Corner scratch for the isolation falsifier — deliberately its own array rather than
    /// the report's <c>Corners</c> or the content walk's <c>ContentCorners</c>, for the reason stated
    /// on the latter: sharing one buffer between unrelated measurements produces a wrong number once
    /// and then never again reproducibly.</summary>
    private static readonly Vector3[] IsoCorners = new Vector3[4];

    /// <summary>
    /// HOW MANY OTHER SUPERSAMPLED PANELS WOULD THIS CAMERA HAVE CAPTURED under ModBuild 193's single
    /// shared layer? This is the falsifier for the whole of part 5, and it is reported every 10 s.
    ///
    /// <para>It measures each other panel's HOST RECT world-space bounding box against this capture
    /// camera's own frustum planes with <see cref="GeometryUtility.TestPlanesAABB"/>. Two known
    /// inexactnesses, both stated rather than hidden: the AABB test counts a box that merely
    /// straddles a frustum corner as inside (over-reports), and a neighbour whose CONTENT spills
    /// outside its own host rect — the ModBuild 193 log's 'New Party display' spilled 545x373 uGUI
    /// px — is measured only by that rect (can under-report). It is a magnitude, not a proof; the
    /// proof is the private layer.</para>
    ///
    /// <para>READING IT. A NON-ZERO count is not a defect in ModBuild 194: the private layer means
    /// that neighbour is not on this camera's mask and cannot be drawn. It is the measurement of how
    /// often the 193 bug WAS firing, and therefore of how load-bearing this fix is. A count that is
    /// permanently zero would instead mean the shared-layer explanation cannot account for
    /// window_merge.jpg and the next round must look elsewhere.</para>
    /// </summary>
    private static int ForeignPanelsInFrustum(Entry e)
    {
        if (e.Cam == null || Entries.Count < 2)
            return 0;
        int hits = 0;
        try
        {
            GeometryUtility.CalculateFrustumPlanes(e.Cam, IsoPlanes);
            for (int i = 0; i < Entries.Count; i++)
            {
                Entry other = Entries[i];
                if (ReferenceEquals(other, e) || other.Panel == null || other.Panel.HostRect == null)
                    continue;
                other.Panel.HostRect.GetWorldCorners(IsoCorners);
                Vector3 min = IsoCorners[0];
                Vector3 max = IsoCorners[0];
                for (int c = 1; c < 4; c++)
                {
                    min = Vector3.Min(min, IsoCorners[c]);
                    max = Vector3.Max(max, IsoCorners[c]);
                }
                var bounds = new Bounds((min + max) * 0.5f, max - min);
                if (GeometryUtility.TestPlanesAABB(IsoPlanes, bounds))
                    hits++;
            }
        }
        catch (System.Exception)
        {
            return 0; // a diagnostic must never be the thing that breaks a frame
        }
        return hits;
    }
}
