using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Which CARD SHAPE a procedural body belongs to — the unit the silhouette is captured and
/// applied per (2026-08-11 report: "alle Karten, auch die Itemkarten").
///
/// The mod draws two physically different cards: the tall poker-aspect ABILITY card and the
/// near-square ITEM card. They do NOT share an outline, so they cannot share one shape
/// footprint: the ability outline stretched onto a square item body would nibble the item
/// card's own corners away. One mechanism, two footprints, keyed by this.
///
/// <see cref="Neutral"/> is the LEGACY shape every pre-existing caller still gets from the
/// parameterless <see cref="CardMesh.CreateEdgeMaterial()"/> /
/// <see cref="CardMesh.CreateBackMaterial()"/> and from <see cref="CardMesh.Get"/>. It is
/// NEVER re-shaped: opting a call site into a card shape is a one-word edit (pass the kind);
/// opting it in by accident is a defect, so the default stays the plain rounded rect.
/// </summary>
internal enum CardBodyKind
{
    /// <summary>Legacy shared shape — the plain rounded rect, never re-shaped.</summary>
    Neutral = 0,

    /// <summary>The tall poker-aspect ability card (hand fan, tray slots, piles).</summary>
    Ability = 1,

    /// <summary>The near-square item card (<c>ItemsPile</c> chips).</summary>
    Item = 2,
}

/// <summary>
/// Procedural 3D card body (P7, hardware test #10: "cards look flat, I want real 3D
/// cards"). A rounded-rectangle slab with genuine thickness: front face (viewer side,
/// -Z), back face (+Z, decorative card-back pattern), and a rim wall — so a card read
/// edge-on or from behind looks like a physical card, not a textured quad.
///
/// Used by <see cref="VRCard"/> as the fallback when the bundle has no
/// <c>CardBacking.prefab</c> (asset contract: unity/.../Table/README.md). Geometry is
/// authored at real card size (meters, 1 unit = 1 m); layouts scale the transform.
/// Everything here runs once per prefab-less card build — no per-frame cost.
///
/// Convention (module-wide): +Z points AWAY from the viewer. The live face canvas
/// floats at z = -0.0012 in front of the front face (z = 0); the slab extends from
/// z = 0 to z = +thickness, i.e. entirely behind the face — matching the README
/// contract "front face area flush around z ≈ 0..+0.001".
///
/// Silhouette (user, verbatim: "Die Karten im Spiel haben eine eigene Form die nicht
/// Rechteckig ist … die meshes genau die Ränder der Karten selber haben"): the game's
/// cards are NOT plain rectangles — the card art carries an artistic, non-rectangular
/// outline in its alpha. <see cref="SetSilhouette"/> takes a card-space alpha footprint
/// captured from the LIVE stock card art (see <see cref="CardFace"/>'s silhouette
/// section), persists it, and drives the GEOMETRIC answer: the card BODY of that
/// <see cref="CardBodyKind"/> is punched out to the footprint's contour
/// (<c>CardContour</c>, served through <see cref="AttachBody"/>), so the body's
/// BOUNDARY is the card outline — no shader variant can paint outside it. The
/// full-envelope rounded slab (<see cref="Get"/>) remains the cold-start/refusal
/// fallback and the Neutral legacy shape; nothing that measures a card (grab collider,
/// fan layout, dock apron, <c>VRCard.WorldWidth</c>) changes either way, because the
/// shaped mesh's bounds stay pinned to the full card box. If no footprint is ever
/// supplied (or it fails the sanity guard) cards keep the opaque rounded rect — degrade
/// to the rectangle, never to a wrong shape. 17 rounds of material/texture clips
/// preceded this; geometry won — see the NetProtocol build notes for ModBuild 105-121.
/// </summary>
internal static class CardMesh
{
    /// <summary>Card body thickness in meters (~1.5 mm — a stiff physical card).</summary>
    internal const float Thickness = 0.0015f;

    /// <summary>
    /// Corner radius in meters. Real Gloomhaven / poker cards round at ~3 mm on a
    /// 63.5 mm width (≈4.7% of width) — the gentle curve the card art outline follows.
    /// </summary>
    internal const float CornerRadius = 0.003f;

    // Arc subdivision per 90° corner. Test #24: the thin dark front now reads AS the
    // card's rounded border (see CardFace inset), so the corners must look smoothly
    // curved rather than faceted. 6 segments -> 4 arcs x 7 points = 28-point outline,
    // still a trivial one-time build.
    private const int CornerSegments = 6;

    // How far (fraction of card size) the rim's UV sample is pulled INWARD from its
    // outline point, so the thin edge samples fully-opaque interior of the silhouette
    // footprint (alpha ≈ 1) and survives the alpha clip along the solid outline instead
    // of straddling the ~0.5 alpha boundary. ~2 % ≈ 1.3 mm on a 63.5 mm card.
    // Internal since round 17: the PUNCHED-OUT body (CardContour.BuildBody) uses the same
    // trick for its rim, so the two bodies sample any shared texture identically.
    internal const float RimUvInset = 0.02f;

    /// <summary>
    /// Render queue for a VR card that is CURRENTLY VISIBLE to the player (fanned in hand /
    /// held / in the browse view). Bug #2: the control board's action-button TMP label is
    /// force-drawn "on top" at queue 4003 with ZTest Always + ZWrite Off (ButtonCluster) so
    /// it clears the opaque board rim — which ALSO made it unconditionally overpaint any
    /// ability card in front of the board (button text bleeding through the card). A visible
    /// card pushes BOTH its opaque backing slab AND its world-space face-art graphics to THIS
    /// queue — above the button's 4003, and at/above the held-mini's 4100
    /// (<see cref="Board.FigureGrab.FigureGrabbable"/>) — via PER-INSTANCE materials, while
    /// KEEPING the shader's ZTest LEqual + ZWrite On. The card therefore wins DRAW ORDER over
    /// the ZWrite-off button widget (which never owns depth), yet still self-occludes and
    /// stays correctly hidden behind real walls / board geometry (LEqual). See
    /// <see cref="VRCard.SetRenderOnTop"/>; this mirrors the held-mini render-on-top fix.
    /// NOTE: applied per-instance on the card renderers, NEVER on the shared card materials
    /// (which <see cref="Net.RemoteHandFan"/> reuses for the opponent's hand backs).
    ///
    /// RETAINED BUT INACTIVE: the bump was reverted (it swallowed all card TEXT), so this constant
    /// has no live reader — its one code reference is inside the retained-but-uncalled
    /// <c>VRCard.ApplyRenderOnTop</c>. It stays because the 4200 &gt; 4100 &gt; 4003 ordering it
    /// records is still the design rationale for the widget queues in <c>PlayTray</c> and
    /// <c>ButtonCluster</c>. Do not "free up" the number.
    /// </summary>
    internal const int HeldCardRenderQueue = 4200;

    private static Texture2D? _backTexture;

    // Shared front/rim + back materials, ONE PAIR PER CardBodyKind. Cached so that a
    // silhouette supplied AFTER cards are built (SetSilhouette, driven from the first
    // face that yields a footprint) mutates the very instances every backing renderer
    // already references — so every live card of that kind adopts the ornate outline at
    // once, with no rebuild and no pop. See VRCard.BuildProceduralBacking.
    private static readonly Material?[] _edgeMaterials = new Material?[3];
    private static readonly Material?[] _backMaterials = new Material?[3];
    private static readonly bool[] _silhouetteApplied = new bool[3];

    // The APPLIED footprint per kind, kept live so CardFace's face blackout can ask "is this pixel
    // card?" without re-capturing, and so a cache-loaded mask serves the very first face of a
    // session. Row-major, [y*W + X], y up, 0..255 alpha.
    private static readonly byte[]?[] _footprints = new byte[]?[3];
    private static readonly int[] _footW = new int[3];
    private static readonly int[] _footH = new int[3];
    private static readonly string?[] _footSource = new string?[3];

    /// <summary>
    /// Which sub-rectangle of the footprint the card ART actually draws on, in the footprint's own
    /// 0..1 (= the face rect). Round 7: the mod's card BODY is fitted to the whole face rect while
    /// the art inside it is poker-shaped and letterboxed, so the two are NOT the same rectangle and
    /// the difference is the black band the user reported (derivation in
    /// <c>CardFace.DrawnLocalRect</c>).
    ///
    /// <para>The footprint itself stays authored over the FULL face rect — that is the space the
    /// body samples it in, exactly and unchanged. This rect exists for consumers that have no face
    /// rect: see <see cref="ExportTexture"/>.</para>
    /// </summary>
    private static readonly Rect[] _footArt =
    {
        new(0f, 0f, 1f, 1f), new(0f, 0f, 1f, 1f), new(0f, 0f, 1f, 1f),
    };

    /// <summary>Whether the persisted cache has been probed for this kind yet (once per session,
    /// re-entrancy guard for the <see cref="SetSilhouette"/> call it makes).</summary>
    private static readonly bool[] _cacheProbed = new bool[3];

    /// <summary>
    /// The applied footprint for <paramref name="kind"/>, or null while that shape is still the
    /// plain rounded rect. Callers must treat it as READ-ONLY — it is the live mask.
    /// </summary>
    internal static byte[]? Footprint(CardBodyKind kind, out int w, out int h)
    {
        int i = (int)kind;
        w = _footW[i];
        h = _footH[i];
        return _footprints[i];
    }

    /// <summary>Name of the sprite the applied footprint was captured from (cache provenance).</summary>
    internal static string? FootprintSource(CardBodyKind kind) => _footSource[(int)kind];

    // ------------------------------------------------ silhouette for FOREIGN materials --

    /// <summary>
    /// Which look a bound material wants behind the card outline — see
    /// <see cref="BindSilhouette"/>.
    /// </summary>
    internal enum SilhouetteLayer
    {
        /// <summary>White RGB, footprint alpha. Multiplied by the material's own colour, so a dark
        /// material stays dark and simply stops painting outside the card.</summary>
        Mask = 0,

        /// <summary>The decorative card-back lattice, footprint alpha. For a quad that shows a
        /// FACE-DOWN card.</summary>
        Back = 1,
    }

    private static readonly Texture2D?[,] _exportTextures = new Texture2D?[3, 2];

    /// <summary>Materials that asked to wear the card outline, with the layer each one wants.
    /// Re-applied whenever a footprint lands, so a material minted before the capture still gets
    /// the shape (see <see cref="BindSilhouette"/>). Bounded by construction — one entry per peer
    /// board card slot — and destroyed entries are dropped on the next apply.</summary>
    private static readonly List<(Material Mat, CardBodyKind Kind, SilhouetteLayer Layer)> _bound = new();

    /// <summary>
    /// USER REQUIREMENT (2026-08-11, verbatim): "UND auch alle Karten genauso die remote angezeigt
    /// werden im Multiplayer bei anderen Spielern."
    ///
    /// THE PROBLEM THIS EXISTS FOR. Five of the six peer mirrors draw a card as a MESH, and since
    /// they adopted <see cref="AttachBody"/> the punched-out card shape reaches them for free.
    /// <c>Net/RemoteBoardCard</c> is the one that does not: a peer's board recess is a flat
    /// <c>BoardVisual.Quad</c> whose material is minted locally on <c>Sprites/Default</c> — it
    /// only borrows this class's back TEXTURE off <c>CreateBackMaterial().mainTexture</c> and
    /// re-wraps it, which carries no card shape at all. Under the requirement above that is a
    /// black rectangle on every peer's control board, i.e. exactly the reported defect seen from
    /// the other side.
    ///
    /// WHY A BINDING AND NOT A GETTER. A getter would be read once, in that quad's constructor,
    /// which on a cold cache runs before any footprint exists — and the peer's board would then
    /// keep the rectangle for the whole session while the local cards did not. Registering the
    /// material means the ONE moment a footprint lands (cache load or live capture) re-paints every
    /// consumer at once, which is the same "no rebuild, no pop, the materials are shared" property
    /// the mesh path already has.
    ///
    /// Degrades to today: with no footprint the material is left exactly as the caller built it.
    /// </summary>
    internal static void BindSilhouette(Material? m, CardBodyKind kind, SilhouetteLayer layer)
    {
        if (m == null || kind == CardBodyKind.Neutral)
            return;
        // A consumer may be the FIRST thing in the process to want this kind's shape (a spectator
        // who sees a peer's board before building a card of their own), so probe the cache here too.
        EnsureSilhouetteCacheLoaded(kind);
        for (int i = 0; i < _bound.Count; i++)
        {
            if (_bound[i].Mat == m)
            {
                _bound[i] = (m, kind, layer);
                ApplyBinding(m, kind, layer);
                return;
            }
        }
        _bound.Add((m, kind, layer));
        ApplyBinding(m, kind, layer);
    }

    private static void ApplyBinding(Material m, CardBodyKind kind, SilhouetteLayer layer)
    {
        Texture2D? tex = ExportTexture(kind, layer);
        if (tex != null)
            m.mainTexture = tex;
    }

    /// <summary>Re-paint every bound material after a footprint landed; drops destroyed ones.</summary>
    private static void ApplyBindings(CardBodyKind kind)
    {
        int repainted = 0;
        for (int i = _bound.Count - 1; i >= 0; i--)
        {
            (Material Mat, CardBodyKind Kind, SilhouetteLayer Layer) b = _bound[i];
            if (b.Mat == null)
            {
                _bound.RemoveAt(i);
                continue;
            }
            if (b.Kind != kind)
                continue;
            ApplyBinding(b.Mat, b.Kind, b.Layer);
            repainted++;
        }
        if (repainted > 0)
        {
            VRLog.Info("Cards", $"CardMesh.BindSilhouette({kind}): re-painted {repainted} foreign " +
                                "material(s) with the card outline — the flat quads that draw a PEER's " +
                                "board recess cannot wear the shaped body mesh, so they carry the " +
                                "same footprint as an alpha channel instead (2026-08-11: 'UND auch alle " +
                                "Karten genauso die remote angezeigt werden im Multiplayer').");
        }
    }

    /// <summary>
    /// The footprint as a blendable RGBA texture for a caller that cannot use the shaped body mesh
    /// (built once per kind+layer, cached). Null while that kind is still the plain rounded rect.
    ///
    /// <para>ORIENTATION: FRONT, i.e. un-mirrored — a plain <c>BoardVisual.Quad</c> has ordinary
    /// 0..1 UVs (unlike the body mesh's mirrored-UV back submesh, see <see cref="Build"/>).
    /// Invisible on a left-right symmetric outline — which is what ships — and wrong the moment
    /// one is not, so it is stated rather than left to luck.</para>
    ///
    /// <para>EXTENT: the export is CROPPED to <see cref="_footArt"/>, the sub-rect the card art
    /// actually draws on, and it is the one place that crop happens. The two families live in
    /// different spaces: the MESH bodies are fitted to the whole FACE RECT
    /// (<c>VRCard.SetCanvasSize</c>), so the raw footprint spans that rect; a peer's flat quad
    /// (<c>Net/RemoteBoardCard</c>'s board-recess card front) has no face rect at all — its 0..1
    /// IS the card — so it gets the art-rect crop. With no letterbox the crop is the whole
    /// footprint and this is a byte-for-byte no-op.</para>
    /// </summary>
    private static Texture2D? ExportTexture(CardBodyKind kind, SilhouetteLayer layer)
    {
        int i = (int)kind;
        int l = (int)layer;
        Texture2D? cached = _exportTextures[i, l];
        if (cached != null)
            return cached;
        byte[]? alpha = _footprints[i];
        int w = _footW[i], h = _footH[i];
        if (alpha == null || w <= 1 || h <= 1 || alpha.Length != w * h)
            return null;

        // Crop window in footprint texels. Clamped and floored to at least 2x2 so a degenerate art
        // rect can only ever fall back to the full footprint, never produce an empty texture.
        Rect art = _footArt[i];
        int cx0 = Mathf.Clamp(Mathf.FloorToInt(art.xMin * w), 0, w - 1);
        int cx1 = Mathf.Clamp(Mathf.CeilToInt(art.xMax * w) - 1, cx0, w - 1);
        int cy0 = Mathf.Clamp(Mathf.FloorToInt(art.yMin * h), 0, h - 1);
        int cy1 = Mathf.Clamp(Mathf.CeilToInt(art.yMax * h) - 1, cy0, h - 1);
        int cw = cx1 - cx0 + 1, ch = cy1 - cy0 + 1;
        if (cw < 2 || ch < 2)
        {
            cx0 = 0; cy0 = 0; cw = w; ch = h;
        }

        var pixels = new Color32[cw * ch];
        Texture2D? backPattern = layer == SilhouetteLayer.Mask ? null : GetBackTexture();
        for (int y = 0; y < ch; y++)
        {
            int srcRow = (cy0 + y) * w;
            int dstRow = y * cw;
            for (int x = 0; x < cw; x++)
            {
                byte a = alpha[srcRow + cx0 + x];
                if (backPattern == null)
                {
                    pixels[dstRow + x] = new Color32(255, 255, 255, a);
                    continue;
                }
                // The lattice is sampled across the CROPPED extent, so a peer's card back shows the
                // same pattern edge-to-edge on its own card that the owner sees on hers.
                Color rgb = backPattern.GetPixelBilinear((x + 0.5f) / cw, (y + 0.5f) / ch);
                pixels[dstRow + x] = new Color32(
                    (byte)(rgb.r * 255f), (byte)(rgb.g * 255f), (byte)(rgb.b * 255f), a);
            }
        }
        if (cw != w || ch != h)
        {
            VRLog.Info("Cards", $"CardMesh.ExportTexture({kind}/{layer}): cropped the {w}x{h} face-rect " +
                                $"footprint to its ART RECT {cw}x{ch} (x {art.xMin:F3}..{art.xMax:F3}, " +
                                $"y {art.yMin:F3}..{art.yMax:F3}). A peer's slab has no face rect — its 0..1 " +
                                "is the CARD — so it gets the card, not the face. The mesh pair is untouched " +
                                "and keeps the full face-rect mask it is fitted to.");
        }
        // Name distinct from the MESH pair's ".Edge"/".Back" bakes on purpose — a hardware log must
        // be able to say which of the two families a CARD TEX line belongs to.
        Texture2D tex = MakeCutoutTexture($"GloomhavenVR.CardSilhouette.{kind}.Quad{layer}", pixels, cw, ch);
        _exportTextures[i, l] = tex;
        return tex;
    }

    // One mesh per DISTINCT card size, quantised to 0.1 mm. This used to be a single
    // slot keyed on the last size asked for, which was fine while only the ability card
    // existed — but ItemsPile asks for the near-square ITEM size from the same cache
    // (ItemsPile.BuildCardBacking), so the two sizes evicted each other and every
    // alternating call rebuilt (and leaked) a Mesh. Same shape as _roundCapCache.
    private static readonly Dictionary<(int, int), Mesh> _bodyCache = new();

    /// <summary>
    /// Build (or reuse) the rounded slab mesh for the given card size. Submesh 0 =
    /// front + rim (dark edge material), submesh 1 = back (card-back material).
    /// The mesh is shared between all cards of the same size.
    /// </summary>
    internal static Mesh Get(float width, float height)
    {
        var key = (Mathf.RoundToInt(width * 10000f), Mathf.RoundToInt(height * 10000f));
        if (_bodyCache.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;
        Mesh built = Build(width, height);
        _bodyCache[key] = built;
        return built;
    }

    // ------------------------------------------------ round 17: the PUNCHED-OUT body --
    //
    // USER RULING (2026-08-11, verbatim, binding): "Die Aufgabe ist doch eher das mesh der Karte
    // auf das outline der Kartenoberfläche 'auszustanzen'." The slab's GEOMETRY is now cut to the
    // card-surface outline — see CardContour for the whole pipeline and the reasoning. Everything
    // here is once per kind (contour) / once per kind+size (mesh) per session, at footprint-apply
    // or card-build time — never per frame.

    /// <summary>Outline polygon per kind, derived deterministically from the applied footprint
    /// (no cache file of its own — it re-derives identically from the persisted mask at load).
    /// Null while unknown or refused; <see cref="_contourTried"/> latches refusals.</summary>
    private static readonly Vector2[]?[] _contours = new Vector2[]?[3];
    private static readonly bool[] _contourTried = new bool[3];

    /// <summary>Shaped body mesh per (kind, size) — same 0.1 mm quantisation as
    /// <see cref="_bodyCache"/>.</summary>
    private static readonly Dictionary<(int, int, int), Mesh> _shapedCache = new();

    /// <summary>Live card-body MeshFilters wearing a shaped-capable kind, so a footprint that
    /// lands AFTER cards were built (cold cache, live capture) swaps every live body to the
    /// punched-out mesh in one step — the same "swap when learned" progressive behaviour the
    /// silhouette materials have always had. Pruned of destroyed filters on every pass.</summary>
    private static readonly List<(MeshFilter Filter, CardBodyKind Kind, float W, float H)> _bodies = new();

    /// <summary>
    /// Attach a card BODY mesh to <paramref name="mf"/>: the punched-out contour mesh when this
    /// kind's outline is already known (warm cache — known before the first card exists), else
    /// the rounded-rect slab, upgraded in place the moment a footprint lands. This is the one
    /// entry every shaped-capable body (VRCard backing, ItemsPile chip backing, the burn-flight
    /// slab) goes through; the Neutral legacy kind and every non-card consumer keep calling
    /// <see cref="Get"/> and are bit-for-bit unchanged.
    /// </summary>
    internal static void AttachBody(MeshFilter mf, CardBodyKind kind, float width, float height)
    {
        if (mf == null)
            return;
        // Same choke-point rule as the material factories: the persisted mask must be loaded
        // before the first body exists, so a warm launch is born punched-out with no transition.
        EnsureSilhouetteCacheLoaded(kind);
        mf.sharedMesh = BodyMesh(kind, width, height);
        if (kind == CardBodyKind.Neutral)
            return;
        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            if (_bodies[i].Filter == null)
                _bodies.RemoveAt(i);
        }
        _bodies.Add((mf, kind, width, height));
    }

    /// <summary>The body mesh for one kind+size: punched-out when the contour is known, else the
    /// rounded-rect slab (cold start / refused derivation — the standing degrade rule).</summary>
    private static Mesh BodyMesh(CardBodyKind kind, float width, float height)
    {
        Vector2[]? contour = kind == CardBodyKind.Neutral ? null : ContourFor(kind);
        if (contour == null)
            return Get(width, height);
        var key = ((int)kind, Mathf.RoundToInt(width * 10000f), Mathf.RoundToInt(height * 10000f));
        if (_shapedCache.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;
        Mesh? shaped = CardContour.BuildBody(contour, width, height, Thickness, RimUvInset,
            out string? refusal);
        if (shaped == null)
        {
            VRLog.Warn("Cards", $"CARD BODY ({kind}): punched-out mesh build refused for " +
                                $"{width * 1000f:F1}x{height * 1000f:F1} mm — {refusal}. This size keeps " +
                                "the rounded-rect slab (degrade to the rectangle, never a wrong shape).");
            shaped = Get(width, height);
        }
        else
        {
            VRLog.Info("Cards", $"CARD BODY ({kind}): punched-out mesh built for " +
                                $"{width * 1000f:F1}x{height * 1000f:F1} mm — {contour.Length} contour " +
                                $"vertices, bounds pinned to the full card box (box-metrics invariant). " +
                                "The body's BOUNDARY is now the card outline; no shader variant can " +
                                "paint outside it (2026-08-11: 'das mesh der Karte auf das outline der " +
                                "Kartenoberfläche auszustanzen').");
        }
        _shapedCache[key] = shaped;
        return shaped;
    }

    /// <summary>Derive (once, latched) the outline contour for a kind from its applied footprint.
    /// Logged with timing — the derivation runs at footprint-apply time, once per kind per
    /// session, so a few ms here never touch a rendered frame twice.</summary>
    private static Vector2[]? ContourFor(CardBodyKind kind)
    {
        int i = (int)kind;
        if (_contourTried[i])
            return _contours[i];
        byte[]? alpha = _footprints[i];
        if (alpha == null)
            return null; // not tried yet — no footprint to derive from
        _contourTried[i] = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Vector2[]? contour = CardContour.Extract(alpha, _footW[i], _footH[i], out string? refusal);
        sw.Stop();
        _contours[i] = contour;
        if (contour == null)
        {
            VRLog.Warn("Cards", $"CARD CONTOUR ({kind}): outline extraction refused in " +
                                $"{sw.Elapsed.TotalMilliseconds:F1} ms — {refusal}. Bodies of this kind " +
                                "keep the rounded-rect slab this session (degrade to the rectangle, " +
                                "never a wrong shape).");
        }
        else
        {
            VRLog.Info("Cards", $"CARD CONTOUR ({kind}): outline extracted from the {_footW[i]}x{_footH[i]} " +
                                $"footprint in {sw.Elapsed.TotalMilliseconds:F1} ms — {contour.Length} vertices " +
                                $"(budget {CardContour.MaxVertices}), marching squares at the 0.5 iso, " +
                                "largest closed loop, Douglas-Peucker simplified. Derived once per kind per " +
                                "session; deterministic from the cached footprint, so no new cache file.");
        }
        return contour;
    }

    /// <summary>Swap every live body of <paramref name="kind"/> to its punched-out mesh — called
    /// when a footprint lands after cards were already built (cold cache). One assignment per
    /// body, no rebuild, no material churn; mirrors the shared-material "no pop" property.</summary>
    private static void ReshapeBodies(CardBodyKind kind)
    {
        if (ContourFor(kind) == null)
            return;
        int reshaped = 0;
        for (int i = _bodies.Count - 1; i >= 0; i--)
        {
            (MeshFilter filter, CardBodyKind k, float w, float h) = _bodies[i];
            if (filter == null)
            {
                _bodies.RemoveAt(i);
                continue;
            }
            if (k != kind)
                continue;
            filter.sharedMesh = BodyMesh(kind, w, h);
            reshaped++;
        }
        if (reshaped > 0)
        {
            VRLog.Info("Cards", $"CARD BODY ({kind}): {reshaped} live card bod(y/ies) swapped to the " +
                                "punched-out mesh in one step (footprint learned this session — from " +
                                "the second launch on the cache applies it before the first card exists " +
                                "and this line never prints).");
        }
    }

    private static Mesh Build(float width, float height)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        float r = Mathf.Min(CornerRadius, Mathf.Min(hw, hh) * 0.45f);

        // Rounded-rect outline, counter-clockwise seen from the FRONT (-Z side).
        int loopCount = 4 * (CornerSegments + 1);
        var outline = new Vector2[loopCount];
        int k = 0;
        // Corner centers: TR, TL, BL, BR — sweeping 90° each keeps the loop CCW when
        // viewed from -Z (x right, y up, viewer looking along +Z).
        Vector2[] centers =
        {
            new(hw - r, hh - r), new(-hw + r, hh - r),
            new(-hw + r, -hh + r), new(hw - r, -hh + r),
        };
        float[] startAngles = { 0f, 90f, 180f, 270f };
        for (int c = 0; c < 4; c++)
        {
            for (int s = 0; s <= CornerSegments; s++)
            {
                float a = (startAngles[c] + 90f * s / CornerSegments) * Mathf.Deg2Rad;
                outline[k++] = centers[c] + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
        }

        // Vertex layout: front loop + front center | back loop + back center |
        // rim (duplicated loop verts front+back for hard outward normals).
        int n = loopCount;
        var vertices = new Vector3[(n + 1) * 2 + n * 2];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];

        int frontBase = 0;              // n outline + 1 center
        int backBase = n + 1;           // n outline + 1 center
        int rimBase = (n + 1) * 2;      // n front + n back

        for (int i = 0; i < n; i++)
        {
            Vector2 p = outline[i];
            var uvP = new Vector2(p.x / width + 0.5f, p.y / height + 0.5f);

            vertices[frontBase + i] = new Vector3(p.x, p.y, 0f);
            normals[frontBase + i] = Vector3.back; // viewer side
            uv[frontBase + i] = uvP;

            vertices[backBase + i] = new Vector3(p.x, p.y, Thickness);
            normals[backBase + i] = Vector3.forward;
            // Mirror X so the back pattern is not a mirror image when seen from +Z.
            uv[backBase + i] = new Vector2(1f - uvP.x, uvP.y);

            // Rim duplicates (outward normal from the outline point).
            Vector3 outward = OutwardNormal(p, hw, hh, r);
            vertices[rimBase + i * 2] = new Vector3(p.x, p.y, 0f);
            vertices[rimBase + i * 2 + 1] = new Vector3(p.x, p.y, Thickness);
            normals[rimBase + i * 2] = outward;
            normals[rimBase + i * 2 + 1] = outward;
            // Card-space planar UV pulled slightly inward (so the silhouette footprint
            // is sampled just INSIDE the outline, alpha ≈ 1 on the solid edge) — the rim
            // then follows the ornate outline under the alpha clip and keeps its 1.5 mm
            // thickness along the visible silhouette. Same value front + back copy so
            // the whole edge wall clips consistently.
            Vector2 pin = new(p.x - outward.x * (width * RimUvInset),
                              p.y - outward.y * (height * RimUvInset));
            var uvRim = new Vector2(
                Mathf.Clamp01(pin.x / width + 0.5f),
                Mathf.Clamp01(pin.y / height + 0.5f));
            uv[rimBase + i * 2] = uvRim;
            uv[rimBase + i * 2 + 1] = uvRim;
        }
        vertices[frontBase + n] = new Vector3(0f, 0f, 0f);
        normals[frontBase + n] = Vector3.back;
        uv[frontBase + n] = new Vector2(0.5f, 0.5f);
        vertices[backBase + n] = new Vector3(0f, 0f, Thickness);
        normals[backBase + n] = Vector3.forward;
        uv[backBase + n] = new Vector2(0.5f, 0.5f);

        // Submesh 0: front fan (facing -Z) + rim quads. Submesh 1: back fan (+Z).
        var frontRim = new int[n * 3 + n * 6];
        var back = new int[n * 3];
        int t = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            // Front face: the outline is CCW in the XY plane; a viewer on the -Z
            // side (Unity left-handed: sees +X right, +Y up) needs CLOCKWISE
            // winding — center → next → i.
            frontRim[t++] = frontBase + n;
            frontRim[t++] = frontBase + next;
            frontRim[t++] = frontBase + i;
        }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int a = rimBase + i * 2;      // front, i
            int b = rimBase + i * 2 + 1;  // back, i
            int c = rimBase + next * 2;   // front, next
            int d = rimBase + next * 2 + 1; // back, next
            frontRim[t++] = a; frontRim[t++] = c; frontRim[t++] = b;
            frontRim[t++] = c; frontRim[t++] = d; frontRim[t++] = b;
        }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            // Back face: viewed from +Z the x-axis appears mirrored, so the
            // CCW-in-XY order center → i → next reads clockwise there.
            back[i * 3] = backBase + n;
            back[i * 3 + 1] = backBase + i;
            back[i * 3 + 2] = backBase + next;
        }

        var mesh = new Mesh { name = "GloomhavenVR.CardBody" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(frontRim, 0);
        mesh.SetTriangles(back, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Item 4 (make the button side walls actually VISIBLE): build a real 3D keycap with a
    /// CHAMFERED front edge, authored at its REAL size in meters so the owning transform can
    /// stay unit-scaled (uniform scale keeps the 45° bevel normal a true 45° in world space,
    /// which is what lets it catch light). Three submeshes, coloured by the caller as a bright
    /// top / a BRIGHT parchment-lit bevel ring / a dark warm wall band, so a huge top→bevel→wall
    /// value gradient reads the cap as unmistakably RAISED even viewed near top-down against a
    /// dark board (the old flat-dark walls, ×0.45 of an already-dark top, vanished):
    ///   • submesh 0 — the TOP plateau (flat, faces the viewer at local −Z), inset by
    ///     <paramref name="bevel"/> from the outer edge;
    ///   • submesh 1 — the BEVEL RING: four ~45° chamfer quads bridging the inset plateau edge
    ///     (at z = −thickness) out to the full-size top edge (at z = −thickness + bevel). Angled
    ///     halfway between top and wall, so it is always partly visible AND shades distinctly
    ///     under BoardLit — the primary "this is 3D" cue;
    ///   • submesh 2 — the four vertical side WALLS + the hidden back.
    /// The cap spans local z = −<paramref name="thickness"/> (front/top, viewer side) to 0
    /// (back), matching the old cube placement, so the label offset and press travel are
    /// unchanged. Each face carries its own flat-shaded vertices/normal; triangle winding is
    /// derived from the outward normal so every face is front-facing regardless of corner order.
    /// </summary>
    internal static Mesh BuildBeveledKeycap(float width, float height, float thickness, float bevel)
    {
        float hw = width * 0.5f, hh = height * 0.5f;
        bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(Mathf.Min(hw, hh) * 0.9f, thickness * 0.9f));
        float iw = hw - bevel, ih = hh - bevel;   // inset plateau half-extents
        float zTop = -thickness;                  // frontmost plane (the plateau)
        float zBev = -thickness + bevel;          // where the bevel meets the vertical wall
        float zBack = 0f;                          // hidden back

        var verts = new System.Collections.Generic.List<Vector3>(24);
        var norms = new System.Collections.Generic.List<Vector3>(24);
        var uvs = new System.Collections.Generic.List<Vector2>(24);
        var top = new System.Collections.Generic.List<int>(6);
        var ring = new System.Collections.Generic.List<int>(24);
        var walls = new System.Collections.Generic.List<int>(30);

        // Task #5a: planar UV from the cap's local XY, normalized 0..1 across the footprint —
        // the SAME convention CardMesh.Build uses for the card front (u = x/width + 0.5,
        // v = y/height + 0.5). Applied to EVERY vertex (top, bevel ring AND walls) so the grain
        // texture maps sensibly instead of the old single (0.5, 0.5) texel. Because the mapping
        // is continuous in XY it is watertight at the top→bevel fold (shared XY → shared UV → no
        // seam). The vertical walls share their edge's XY, so they sample a THIN grain strip along
        // that edge (a subtle stretched grain — acceptable per the task, texture set to Repeat).
        Vector2 Uv(Vector3 p) => new(p.x / width + 0.5f, p.y / height + 0.5f);

        // Add a quad (a,b,c,d looping the rim) to submesh <sm> with flat normal <n>. Winding is
        // chosen from the outward normal so the face is always visible from its +n (OUTSIDE) side.
        //
        // FIX (Item 7 — "you can see the button's OWN UNDERSIDE / interior through it"): the whole
        // keycap was wound INSIDE-OUT and every face was back-face-CULLED by BoardLit (its cap
        // materials are `new Material(BoardLit)`, so they keep the shader's default _Cull = 2 =
        // Back — never the board's _Cull = 0). With every outward face culled, only the FAR interior
        // faces survived toward the viewer, so the button read as see-through onto its own back cap /
        // inner walls. Ground truth (the shipping card front face, CardMesh.Build): a triangle is
        // front-facing/visible from the side its RIGHT-HAND normal (Cross(v1−v0, v2−v0)) points
        // TOWARD. So to be visible from +n the EMITTED winding's RH normal must point +n — the
        // opposite of what this method did before (it forced the RH normal to −n in BOTH branches).
        void AddQuad(System.Collections.Generic.List<int> sm,
                     Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int b0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(Uv(a)); uvs.Add(Uv(b)); uvs.Add(Uv(c)); uvs.Add(Uv(d));
            Vector3 rh = Vector3.Cross(b - a, c - a); // RH normal of triangle (a,b,c)
            if (Vector3.Dot(rh, n) > 0f)
            {
                // (a,b,c)/(a,c,d) already wind so the RH normal points +n → visible from outside.
                sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2);
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 3);
            }
            else
            {
                // Reverse so the RH normal flips to +n (outward).
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1);
                sm.Add(b0); sm.Add(b0 + 3); sm.Add(b0 + 2);
            }
        }

        // Top plateau (faces the viewer, −Z).
        AddQuad(top, new(-iw, ih, zTop), new(iw, ih, zTop), new(iw, -ih, zTop), new(-iw, -ih, zTop),
                Vector3.back);

        // Bevel ring — four 45° chamfers (normal = outward + toward viewer). Corner folds are
        // shared edges (plateau corner → outer corner), so the ring is watertight.
        const float s = 0.70710678f;
        AddQuad(ring, new(-iw, ih, zTop), new(iw, ih, zTop), new(hw, hh, zBev), new(-hw, hh, zBev),
                new Vector3(0f, s, -s));   // +Y edge
        AddQuad(ring, new(iw, ih, zTop), new(iw, -ih, zTop), new(hw, -hh, zBev), new(hw, hh, zBev),
                new Vector3(s, 0f, -s));   // +X edge
        AddQuad(ring, new(iw, -ih, zTop), new(-iw, -ih, zTop), new(-hw, -hh, zBev), new(hw, -hh, zBev),
                new Vector3(0f, -s, -s));  // −Y edge
        AddQuad(ring, new(-iw, -ih, zTop), new(-iw, ih, zTop), new(-hw, hh, zBev), new(-hw, -hh, zBev),
                new Vector3(-s, 0f, -s));  // −X edge

        // Vertical side walls (outward normals) from the bevel base back to z = 0.
        AddQuad(walls, new(-hw, hh, zBev), new(hw, hh, zBev), new(hw, hh, zBack), new(-hw, hh, zBack),
                Vector3.up);
        AddQuad(walls, new(hw, hh, zBev), new(hw, -hh, zBev), new(hw, -hh, zBack), new(hw, hh, zBack),
                Vector3.right);
        AddQuad(walls, new(hw, -hh, zBev), new(-hw, -hh, zBev), new(-hw, -hh, zBack), new(hw, -hh, zBack),
                Vector3.down);
        AddQuad(walls, new(-hw, -hh, zBev), new(-hw, hh, zBev), new(-hw, hh, zBack), new(-hw, -hh, zBack),
                Vector3.left);
        // Hidden back (kept so the solid never shows a hole if seen edge-on).
        AddQuad(walls, new(-hw, hh, zBack), new(hw, hh, zBack), new(hw, -hh, zBack), new(-hw, -hh, zBack),
                Vector3.forward);

        var mesh = new Mesh { name = "GloomhavenVR.BeveledKeycap" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(ring, 1);
        mesh.SetTriangles(walls, 2);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Segment count for the generated round board caps (user: "you can see the CORNERS in
    /// the 'round' buttons"). Unity's <see cref="PrimitiveType.Cylinder"/> has only ~20 radial
    /// sides, so a large round keycap reads as a faceted polygon; 64 sides reads perfectly
    /// smooth at the caps' on-board size while staying a trivial one-time build.
    /// </summary>
    internal const int RoundCapSegments = 64;

    private static readonly System.Collections.Generic.Dictionary<(int, int, int), Mesh> _roundCapCache = new();

    /// <summary>
    /// Cached, SHARED high-segment round-cap disc (see <see cref="BuildRoundCap"/>). Keyed on
    /// (diameter, thickness, segments) quantised to 0.1 mm so identical caps — every rest disc,
    /// and Confirm/Undo when their per-board shape is Round — reuse ONE mesh instead of
    /// rebuilding per button. Never rebuilt per frame (called once at each button's build).
    /// </summary>
    internal static Mesh GetRoundCap(float diameter, float thickness, int segments = RoundCapSegments)
    {
        segments = Mathf.Clamp(segments, 12, 128);
        var key = (Mathf.RoundToInt(diameter * 10000f), Mathf.RoundToInt(thickness * 10000f), segments);
        if (_roundCapCache.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;
        Mesh built = BuildRoundCap(diameter, thickness, segments);
        _roundCapCache[key] = built;
        return built;
    }

    /// <summary>
    /// Smooth round keycap/disc mesh (user: the 'round' board buttons showed visible CORNERS
    /// because they were Unity's ~20-sided <see cref="PrimitiveType.Cylinder"/>). A genuine
    /// <paramref name="segments"/>-sided disc — a front cap fan (viewer side, −Z), a back cap
    /// fan (+Z) and a radial side wall — authored at REAL size (<paramref name="diameter"/> ×
    /// <paramref name="thickness"/>, centred on the local origin) so the owning holder stays
    /// identity-rotated / unit-scaled, reproducing the exact placement the flattened cylinder
    /// had (the front face protrudes <paramref name="thickness"/>/2 toward the viewer).
    ///
    /// Planar XY UVs (u = x/diameter + 0.5, v = y/diameter + 0.5) — the SAME convention as the
    /// card front (<see cref="Build"/>) and the beveled keycap (<see cref="BuildBeveledKeycap"/>)
    /// — so the shared carved-grain keycap <c>_MainTex</c> (grayscale grain × the state colour)
    /// maps across the round face exactly as it does on the square caps; the side wall samples
    /// its rim XY (a thin grain strip, like the keycap walls, texture set to Repeat). One
    /// submesh (the round caps carry a single keycap material — no bevel/wall split), so the
    /// caller's <c>sharedMaterial</c> and <c>SetCapColor</c> drive it unchanged.
    /// </summary>
    internal static Mesh BuildRoundCap(float diameter, float thickness, int segments)
    {
        segments = Mathf.Clamp(segments, 12, 128);
        int seg = segments;
        float r = diameter * 0.5f;
        float h = Mathf.Max(0.0005f, thickness * 0.5f);
        float zFront = -h;  // viewer side (−Z), matching the flattened-cylinder placement
        float zBack = h;

        var verts = new System.Collections.Generic.List<Vector3>(seg * 4 + 2);
        var norms = new System.Collections.Generic.List<Vector3>(seg * 4 + 2);
        var uvs = new System.Collections.Generic.List<Vector2>(seg * 4 + 2);
        var tris = new System.Collections.Generic.List<int>(seg * 12);

        Vector2 Uv(float x, float y) => new(x / diameter + 0.5f, y / diameter + 0.5f);

        var ring = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            ring[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        // Front ring + centre (normal −Z, viewer side).
        int frontBase = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            verts.Add(new Vector3(ring[i].x, ring[i].y, zFront));
            norms.Add(Vector3.back);
            uvs.Add(Uv(ring[i].x, ring[i].y));
        }
        int frontCenter = verts.Count;
        verts.Add(new Vector3(0f, 0f, zFront)); norms.Add(Vector3.back); uvs.Add(new Vector2(0.5f, 0.5f));

        // Back ring + centre (normal +Z).
        int backBase = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            verts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
            norms.Add(Vector3.forward);
            uvs.Add(Uv(ring[i].x, ring[i].y));
        }
        int backCenter = verts.Count;
        verts.Add(new Vector3(0f, 0f, zBack)); norms.Add(Vector3.forward); uvs.Add(new Vector2(0.5f, 0.5f));

        // Side wall: duplicated ring verts (front+back) with hard radial-outward normals.
        int wallBase = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            Vector3 outward = new Vector3(ring[i].x, ring[i].y, 0f).normalized;
            verts.Add(new Vector3(ring[i].x, ring[i].y, zFront));
            verts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
            norms.Add(outward); norms.Add(outward);
            uvs.Add(Uv(ring[i].x, ring[i].y)); uvs.Add(Uv(ring[i].x, ring[i].y));
        }

        // Front cap fan (visible from −Z): centre → next → i (clockwise from −Z, per Build's
        // front face — the winding that makes the RH normal point toward the viewer).
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            tris.Add(frontCenter); tris.Add(frontBase + next); tris.Add(frontBase + i);
        }
        // Back cap fan (visible from +Z): centre → i → next.
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            tris.Add(backCenter); tris.Add(backBase + i); tris.Add(backBase + next);
        }
        // Side wall quads (outward-facing — same winding as Build's rim: a,c,b / c,d,b).
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            int a = wallBase + i * 2;        // front, i
            int b = wallBase + i * 2 + 1;    // back, i
            int c = wallBase + next * 2;     // front, next
            int d = wallBase + next * 2 + 1; // back, next
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(c); tris.Add(d); tris.Add(b);
        }

        var mesh = new Mesh { name = "GloomhavenVR.RoundCap" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 1;
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 OutwardNormal(Vector2 p, float hw, float hh, float r)
    {
        // Direction from the nearest corner-arc center (also correct on the straight
        // edges, where the point lies on the inflated rect around the core rect).
        float cx = Mathf.Clamp(p.x, -hw + r, hw - r);
        float cy = Mathf.Clamp(p.y, -hh + r, hh - r);
        var v = new Vector2(p.x - cx, p.y - cy);
        if (v.sqrMagnitude < 1e-12f)
            return Vector3.right;
        v.Normalize();
        return new Vector3(v.x, v.y, 0f);
    }

    // ------------------------------------------------------------------ materials --

    /// <summary>
    /// Front/rim colour of the slab — the card's visible physical EDGE (mostly hidden behind the
    /// live face; the thin rounded front and the 1.5 mm rim read at the card boundary).
    ///
    /// <para>WARM UMBER, not near-black (border round 14; user ruling "Ich möchte gerne an den
    /// aktuellen Proportionen festhalten... nur eben ohne die schwarzen Ränder"): the rim is a
    /// design element whose COLOUR was the defect. (0.42, 0.33, 0.23), luma ≈ 0.35, lands the
    /// visible edge in the same warm-wood family as the tray liner/keycaps
    /// (<c>PlayTray.SlotLinerColor</c> (0.46,0.37,0.26), luma 0.38) — on the tray the ring
    /// blends into the liner beneath it, in the hand it reads as a warm card edge. Reaches all
    /// wearers by construction: the Ability/Item pairs and the Neutral legacy pair all take
    /// their colour from here.</para>
    /// </summary>
    private static readonly Color EdgeColor = new(0.42f, 0.33f, 0.23f);

    /// <summary>
    /// THE LIGHTING FIX (border round 15). The slab pair renders through the stock
    /// <c>Standard</c> shader, whose output is albedo × incoming light — and the game's VR
    /// scenes are dark / stripped of lighting, so a Standard surface renders near-black
    /// REGARDLESS of its albedo (karten4.png measured the card edge at (4,4,3), ~16 % of what
    /// the authored EdgeColor would read under ordinary light). The mod solved this exact hole
    /// for the board furniture with the bundled <c>GloomhavenVR/BoardLit</c> shader, but moving
    /// the slab there would need a bundle rebuild — so the slab keeps Standard and gets a
    /// SELF-ILLUMINATION FLOOR instead: <c>_EmissionMap</c> = the material's own albedo texture
    /// and <c>_EmissionColor</c> = the albedo tint × this factor, so the fragment output is
    /// albedo × light + albedo × factor — in a black scene the surface reads exactly its
    /// authored albedo × factor.
    ///
    /// <para>FACTOR CHOICE = 1.0, calibrated against the BoardLit tray the cards sit on. BoardLit
    /// shades a viewer-facing surface at <c>_Ambient</c> (0.5) + key/fill ≈ 1.0..1.4 × albedo, so
    /// the tray liner (0.46,0.37,0.26, luma 0.38) reads at roughly its authored luma — the
    /// "keycap grain ≈ luma 0.38 visible" reference brightness. Factor 1.0 puts the card edge
    /// (EdgeColor luma 0.35) in exactly that family. The only over-brightness risk is a scene
    /// with REAL light adding on top; karten4 measures that residual at ≈ 0.16 × albedo, so the
    /// worst case is ≈ 1.16 × authored albedo — still wood, nowhere near blown out.</para>
    ///
    /// <para>REACH. Applied inside the shared-material factories, so it covers by construction:
    /// the Ability/Item pairs (fan, tray, held, piles) and the Neutral legacy pair (the peer
    /// mirrors and the avatar mirror borrow THESE material instances via
    /// <c>CreateEdgeMaterial()</c>/<c>CreateBackMaterial()</c>). <c>Net/RemoteBoardCard</c> needs
    /// nothing: its quad is <c>Sprites/Default</c> (unlit by construction, no hole). KNOWN
    /// RESIDUAL RISK, stated rather than hidden: if the game build stripped the Standard shader's
    /// <c>_EMISSION</c> variant, EnableKeyword silently falls back to the emission-less variant
    /// and the edge stays lighting-dependent.</para>
    /// </summary>
    private const float EmissionFloorFactor = 1.0f;

    /// <summary>
    /// Give a Standard-shaded material the self-illumination floor described at
    /// <see cref="EmissionFloorFactor"/>: emission = current albedo (texture × tint) × factor,
    /// derived from the material's CURRENT <c>mainTexture</c>/<c>color</c> — so call it again
    /// after changing either (idempotent; re-syncs). No-ops on materials without
    /// <c>_EmissionColor</c> (BoardLit, Sprites/Default, Overlay — the unlit/self-lit families
    /// have no hole to floor). Logs once per material, on first application. Writes exactly four
    /// things — the <c>_EMISSION</c> keyword, the GI flags, <c>_EmissionMap</c> and
    /// <c>_EmissionColor</c> — and nothing else about the material's state.
    /// </summary>
    internal static void ApplyEmissionFloor(Material? m)
    {
        if (m == null || m.shader == null || !m.HasProperty("_EmissionColor"))
            return;
        bool first = !m.IsKeywordEnabled("_EMISSION");
        m.EnableKeyword("_EMISSION");
        // Pure realtime shading term: no GI participation (and never EmissiveIsBlack, which is
        // the flag Unity's material editor uses to mean "treat emission as absent").
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        if (m.HasProperty("_EmissionMap"))
            m.SetTexture("_EmissionMap", m.mainTexture); // null → white → emission = tint alone
        Color tint = m.HasProperty("_Color") ? m.color : Color.white;
        m.SetColor("_EmissionColor", tint * EmissionFloorFactor);
        if (first)
        {
            VRLog.Info("Cards", $"CARD EMISSION FLOOR: '{m.name}' (shader '{m.shader.name}') now " +
                                $"self-illuminates at albedo × {EmissionFloorFactor:F2} (_EmissionMap = its own " +
                                $"albedo texture{(m.mainTexture != null ? $" '{m.mainTexture.name}'" : " (none → tint only)")}, " +
                                $"_EmissionColor {tint * EmissionFloorFactor}) — Standard renders albedo × light and " +
                                "the VR scenes are dark, which is why 14 rounds of albedo changes on this surface " +
                                "were invisible (karten4: band (4,4,3) ≈ unlit). If this build's Standard lacks the " +
                                "_EMISSION variant this is a silent no-op — the CARD BAND PIXELS scan decides.");
        }
    }

    /// <summary>
    /// Dark neutral for the front (hidden behind the live face) and the rim edge, for the
    /// LEGACY <see cref="CardBodyKind.Neutral"/> pair — never alpha-clipped. Every call
    /// site that existed before the two-shape silhouette work still lands here and is
    /// therefore bit-for-bit unchanged; see <see cref="CardBodyKind"/> for why that is
    /// the safe default.
    /// </summary>
    internal static Material CreateEdgeMaterial() => CreateEdgeMaterial(CardBodyKind.Neutral);

    /// <summary>
    /// Dark front/rim material for one card SHAPE. Shared by every body of that kind, so a
    /// later <see cref="SetSilhouette"/> re-shapes them all at once — including cards that
    /// were built long before the footprint was captured.
    /// </summary>
    internal static Material CreateEdgeMaterial(CardBodyKind kind)
    {
        int i = (int)kind;
        Material? m = _edgeMaterials[i];
        if (m == null)
        {
            m = NewMaterial();
            m.color = EdgeColor;
            m.name = $"GloomhavenVR.CardEdge.{kind}";
            ApplyEmissionFloor(m); // round 15: dark scenes — see EmissionFloorFactor
            _edgeMaterials[i] = m;
        }
        // THE FIRST DRAWN PIXEL IS ALREADY RIGHT (2026-08-11: "Der Prozess der 'Ausblendung' soll
        // auch nicht sichtbar sein, sondern direkt die richtigen meshes sichtbar sein"). This is
        // the ONE choke point every card body of a kind passes through on its way to existing, so
        // loading the persisted mask here — not from a module init hook — means it is applied
        // before the renderer that will draw it has a material at all, on every construction path
        // there is or ever will be.
        EnsureSilhouetteCacheLoaded(kind);
        return _edgeMaterials[i]!;
    }

    /// <summary>Opaque decorative card back: procedural lattice pattern texture. LEGACY
    /// <see cref="CardBodyKind.Neutral"/> pair (see <see cref="CreateEdgeMaterial()"/>).</summary>
    internal static Material CreateBackMaterial() => CreateBackMaterial(CardBodyKind.Neutral);

    /// <summary>Decorative card back for one card SHAPE (see
    /// <see cref="CreateEdgeMaterial(CardBodyKind)"/>). All kinds start from the same
    /// procedural lattice; only the alpha clip differs once a footprint lands.</summary>
    internal static Material CreateBackMaterial(CardBodyKind kind)
    {
        int i = (int)kind;
        Material? m = _backMaterials[i];
        if (m == null)
        {
            m = NewMaterial();
            m.color = Color.white;
            m.mainTexture = GetBackTexture();
            m.name = $"GloomhavenVR.CardBack.{kind}";
            ApplyEmissionFloor(m); // round 15: dark scenes — see EmissionFloorFactor
            _backMaterials[i] = m;
        }
        EnsureSilhouetteCacheLoaded(kind); // see CreateEdgeMaterial — same choke point, same reason
        return _backMaterials[i]!;
    }

    /// <summary>True once <see cref="SetSilhouette"/> has re-shaped this card SHAPE's body to
    /// the real card-art outline (one-shot per kind).</summary>
    internal static bool SilhouetteApplied(CardBodyKind kind) => _silhouetteApplied[(int)kind];

    /// <summary>
    /// Accept the card-shape footprint for ONE <paramref name="kind"/> ("auch die Itemkarten").
    /// Once per kind per session: <paramref name="alpha"/> is a card-space opacity footprint of
    /// the LIVE stock card art (row-major, <c>alpha[y*w + x]</c>, x → right, y → up, normalized
    /// over the card face rect) captured by <see cref="CardFace"/>. On acceptance the footprint
    /// drives the PUNCHED-OUT body geometry: <see cref="ReshapeBodies"/> derives the contour
    /// (<c>CardContour</c>, 0.5 iso, largest closed loop) and swaps every live body of the kind
    /// onto the shaped mesh; later <see cref="AttachBody"/> calls are born shaped. The footprint's
    /// 0..1 square is the FACE RECT — the body is fitted to exactly that rect on both card kinds
    /// (<c>VRCard.SetCanvasSize</c> — facePixels × fit × VisibleFaceFraction, which equals
    /// CardFace's own 1−BorderFraction inset; <c>ItemsPile</c> — native × fit), so footprint
    /// texel (u,v) sits on the card pixel it was sampled from, at every card scale.
    ///
    /// <para><paramref name="artRect"/> is the sub-rect of the footprint the card ART actually
    /// draws on, in the footprint's own 0..1 — see <see cref="_footArt"/> and
    /// <see cref="ExportTexture"/>. Pass the full 0..1 when it is not known; the footprint itself
    /// is unaffected either way. <paramref name="source"/> names the sprite the mask came from
    /// (cache provenance); <paramref name="fromCache"/> suppresses the write-back so loading never
    /// rewrites what it just read.</para>
    ///
    /// Robust by design: returns without applying (cards stay the opaque rounded-rect)
    /// if the footprint is malformed, degenerate (mostly empty or a solid rectangle —
    /// the latter would be pointless AND is the signature of a bad capture) or hollow in
    /// the centre. It therefore can never make a card invisible. Returns whether the
    /// silhouette was applied.
    /// </summary>
    internal static bool SetSilhouette(CardBodyKind kind, byte[]? alpha, int w, int h,
                                       Rect artRect, string? source, bool fromCache)
    {
        if (kind == CardBodyKind.Neutral)
        {
            // The legacy shape is shared with call sites this change never audited at runtime
            // (see CardBodyKind). Refusing here rather than at the caller keeps that promise
            // in ONE place.
            VRLog.Warn("Cards", "CardMesh.SetSilhouette: refused for the Neutral (legacy shared) " +
                                "shape — a re-shape there would reach consumers this was never " +
                                "verified against.");
            return false;
        }
        if (_silhouetteApplied[(int)kind])
            return true;
        if (alpha == null || w <= 1 || h <= 1 || alpha.Length != w * h)
        {
            VRLog.Warn("Cards", $"CardMesh.SetSilhouette({kind}): malformed footprint " +
                                $"(alpha={(alpha == null ? "null" : alpha.Length.ToString())}, {w}x{h}) — kept rounded-rect.");
            return false;
        }

        // --- sanity guard: reject empty / solid / hollow-centre footprints ----------
        long opaque = 0;
        int bx0 = w, bx1 = -1, by0 = h, by1 = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (alpha[row + x] < 128)
                    continue;
                opaque++;
                if (x < bx0) bx0 = x;
                if (x > bx1) bx1 = x;
                if (y < by0) by0 = y;
                if (y > by1) by1 = y;
            }
        }
        float frac = (float)opaque / alpha.Length;
        bool centerOpaque = CenterOpaque(alpha, w, h);

        // IS THIS AN OUTLINE OR JUST A SMALLER RECTANGLE? (2026-08-11 round 2.) The 0.12..0.985
        // window says nothing about SHAPE: an inset axis-aligned box of 89 % area sails through it
        // exactly as an ornate curve of 89 % area does, and the two are worth completely different
        // things to the player. bboxFill = opaque / bounding-box area is the discriminator — 1.000
        // means the footprint IS its own bounding rectangle, and the only thing the clip then buys
        // is a slightly narrower slab. Reported, never guessed at again.
        long bboxArea = (bx1 >= bx0 && by1 >= by0) ? (long)(bx1 - bx0 + 1) * (by1 - by0 + 1) : 0L;
        float bboxFill = bboxArea > 0 ? (float)opaque / bboxArea : 0f;
        float bboxCoverage = (float)bboxArea / ((long)w * h);
        VRLog.Info("Cards", $"CardMesh.SetSilhouette({kind}): footprint {w}x{h}, opaque frac={frac:F3}, " +
                            $"centerOpaque={centerOpaque} (accept if 0.12<frac<0.985 & centre solid) — " +
                            $"SHAPE: bounding box {(bx1 - bx0 + 1)}x{(by1 - by0 + 1)} = {bboxCoverage:P1} of " +
                            $"the face, filled {bboxFill:F3} (1.000 = a plain rectangle, so the clip only " +
                            "narrows the slab; below ~0.97 = a genuinely non-rectangular outline).");

        // A footprint that is the FULL face AND solid to its own bounding box is a no-op shape.
        // Refuse it rather than burn the one shot, so a later, better footprint can still land.
        if (bboxFill >= 0.995f && bboxCoverage >= 0.99f)
        {
            VRLog.Info("Cards", $"CardMesh.SetSilhouette({kind}): the footprint is the full face and solid " +
                                "to its own bounding box — a re-shape here changes nothing. Kept rounded-rect; " +
                                "the one shot stays available for a later footprint.");
            return false;
        }
        if (frac < 0.12f || frac > 0.985f)
        {
            // near-empty (bad/early capture, art not loaded) or near-solid (a plain
            // rectangle — clipping would be a visual no-op). The backing is already fit to
            // the visible art (VRCard.VisibleFaceFraction), so the card shows no black
            // border either way; this only decides whether the RIM traces an ornate outline.
            VRLog.Info("Cards", $"CardMesh.SetSilhouette({kind}): frac {frac:F3} out of range — kept rounded-rect " +
                                "(border already removed by the art-fitted backing).");
            return false;
        }
        // Centre must be solid card (a valid card is opaque at its middle).
        if (!centerOpaque)
        {
            VRLog.Info("Cards", $"CardMesh.SetSilhouette({kind}): centre not solid — kept rounded-rect.");
            return false;
        }

        _silhouetteApplied[(int)kind] = true;
        _footprints[(int)kind] = alpha;
        _footW[(int)kind] = w;
        _footH[(int)kind] = h;
        _footSource[(int)kind] = source;
        // Degenerate or absent art rect ⇒ the whole footprint, i.e. exactly today's export.
        _footArt[(int)kind] = (artRect.width > 0.01f && artRect.height > 0.01f)
            ? Rect.MinMaxRect(Mathf.Clamp01(artRect.xMin), Mathf.Clamp01(artRect.yMin),
                              Mathf.Clamp01(artRect.xMax), Mathf.Clamp01(artRect.yMax))
            : new Rect(0f, 0f, 1f, 1f);
        VRLog.Info("Cards", $"CardMesh.SetSilhouette({kind}): APPLIED from {(fromCache ? "the PERSISTED CACHE " +
                            "(before this session drew a single card — no visible transition)" : "a live capture")} " +
                            $"[source '{source ?? "n/a"}'] — the footprint drives the PUNCHED-OUT body GEOMETRY " +
                            "(CardContour); the shared front/rim + back materials keep their plain umber/lattice " +
                            "look and every body of this kind gets a mesh whose boundary IS the card outline.");
        // Derive the outline contour and swap every live body to the punched-out mesh. On the
        // cache path this runs before any body exists (nothing to swap — later AttachBody calls
        // are born shaped); on a cold-start live capture it is the one visible step.
        ReshapeBodies(kind);
        // FOREIGN CONSUMERS LAST, and only now that _footprints holds the mask: the flat quads
        // that draw a PEER's board recess cannot wear the shaped mesh, so they carry the same
        // footprint as an alpha channel. See BindSilhouette for why this is a binding, not a getter.
        ApplyBindings(kind);
        if (!fromCache)
            WriteSilhouetteCache(kind, alpha, w, h, _footArt[(int)kind], source);
        return true;
    }

    // ------------------------------------------------- persisted silhouette cache --

    /// <summary>
    /// USER REQUIREMENT (2026-08-11, verbatim): "Der Prozess der 'Ausblendung' soll auch nicht
    /// sichtbar sein, sondern direkt die richtigen meshes sichtbar sein."
    ///
    /// A footprint captured from LIVE card art cannot exist before that art has loaded, so within
    /// one session there is an unavoidable window between "the first card is drawn" and "the shape
    /// is known". The only way to close it is to not learn it in that session at all: the mask is
    /// written to the mod's own per-user data directory the first time it is captured, and read
    /// back — and APPLIED — inside <see cref="CreateEdgeMaterial(CardBodyKind)"/>, i.e. at the
    /// moment a card body's material is created and therefore before any renderer using it can
    /// draw. From the second launch onward the card is born with the correct silhouette and there
    /// is no transition to see.
    ///
    /// THE FIRST LAUNCH AFTER INSTALLING IS THE ONE EXCEPTION, and it is stated rather than hidden:
    /// on a cold cache the cards keep exactly today's opaque rounded rect until the first card art
    /// loads (in the ModBuild-108 log: the same frame the hand fan opened), then take the outline
    /// in one step. That is deliberately the OLD look during the window, never a guessed shape —
    /// the standing rule is to degrade to today's rectangle, never to a wrong one.
    ///
    /// WHERE IT IS WRITTEN. <c>Paths.ConfigPath</c> as
    /// <c>BepInEx/config/dev.gloomhavenvr.cardsilhouette.{kind}.bin</c> — the mod's own per-user
    /// data directory, the same place and the same <c>PLUGIN_GUID</c> naming
    /// <c>Core/ModuleConfig</c> puts every module cfg and <c>WorldUI/LoadingIndicator</c> puts its
    /// baked boot icon. NEVER the game's own tree and never <c>ressources/</c>.
    ///
    /// <para>NOT <c>Paths.CachePath</c>, which is the first instinct and is wrong here: BepInEx
    /// owns <c>BepInEx/cache</c> and clears it on its own schedule (assembly patch caches live
    /// there). Losing this file is not a normal cache miss — it silently reintroduces the ONE
    /// visible transition this whole mechanism exists to remove, and it would do so without any
    /// user-visible cause. A file that must survive to keep a look promise belongs where the
    /// user's other mod data lives.</para>
    ///
    /// A missing, unreadable, truncated or version-mismatched file is simply ignored (and the log
    /// says which), so the feature can only ever fall back to the cold-start behaviour above.
    ///
    /// CLASS DRIFT. The mask is keyed by <see cref="CardBodyKind"/> alone, but it is captured from
    /// ONE character's background art ('AC_Berserker_Background' in the reported session). The
    /// Gloomhaven ability-card frame is a shared template, so this is expected to be a constant —
    /// but it is not GUARANTEED, so the cache records the source sprite name and
    /// <see cref="RefreshSilhouetteCache"/> silently re-learns it once per session from whatever
    /// class is actually being played. A drift is corrected for the NEXT launch rather than
    /// re-applied mid-session, because re-applying is precisely the visible transition the user
    /// rejected.
    /// </summary>
    private const uint CacheMagic = 0x53525647; // 'GVRS' little-endian

    /// <summary>
    /// Bump this whenever the MEANING of a stored mask changes, not just its layout — a file whose
    /// bytes still parse but no longer mean what this build thinks they mean is worse than no file.
    /// <see cref="EnsureSilhouetteCacheLoaded"/> applies the file before the first card body exists
    /// and <see cref="RefreshSilhouetteCache"/> deliberately refuses to re-apply mid-session (that
    /// re-shape IS the visible transition the user rejected), so a stale file would silently ship
    /// the PREVIOUS build's shape — the bump is what forces the relearn. Cost: exactly one cold
    /// start per shape, the documented first-run behaviour, never a wrong shape.
    ///
    /// <para>CURRENT MEANING (v6): the footprint is the STOCK card art's own designed alpha at
    /// the 0.5 iso, stamped into the art's drawn rect — no drop-shadow trim, no dark-border peel,
    /// no frame punch, no outline clip. The file also carries the art rect (since v3). Body and
    /// face coincide by construction: the contour derived from this mask reaches everything the
    /// art draws, printed frame and bottom protrusion included.</para>
    ///
    /// <para>v5 → v6 IS LOAD-BEARING (the ModBuild-121 bottom-edge fix): every v5 file on a
    /// player's rig was learned from PUNCHED pixels additionally clipped by the CardOutline band
    /// geometry — its bottom band (~5.3 % of the face) was a frame-era estimate that provably
    /// disagreed with the art's true bottom edge, so the v5 body stopped HIGHER than the stock
    /// art draws and the unbacked strip read as a transparent card bottom ("Der untere Rand von
    /// allen Karten ist teilweise transparent", karten6.png). A v6 build must not apply that
    /// mask. Earlier bumps (v1→v2 peel, v2→v3 drawn rect + art rect, v3→v4 punched source,
    /// v4→v5 outline clip) were each load-bearing the same way; their machinery is deleted —
    /// 17 rounds, geometry won; see the NetProtocol build notes for ModBuild 105-121.</para>
    /// </summary>
    private const byte CacheVersion = 6;

    private static string CacheFilePath(CardBodyKind kind) => System.IO.Path.Combine(
        BepInEx.Paths.ConfigPath, $"{MyPluginInfo.PLUGIN_GUID}.cardsilhouette.{kind}.bin");

    /// <summary>Load and apply the persisted mask for one kind. Runs at most once per kind per
    /// session and is re-entrancy safe (it calls <see cref="SetSilhouette"/>, which calls back into
    /// the material factories).</summary>
    private static void EnsureSilhouetteCacheLoaded(CardBodyKind kind)
    {
        int i = (int)kind;
        if (_cacheProbed[i])
            return;
        _cacheProbed[i] = true; // BEFORE anything that can re-enter
        if (kind == CardBodyKind.Neutral || _silhouetteApplied[i])
            return;

        string path = CacheFilePath(kind);
        try
        {
            if (!System.IO.File.Exists(path))
            {
                VRLog.Info("Cards", $"CARD SILHOUETTE CACHE ({kind}): none at '{path}' — FIRST RUN for this " +
                                    "shape. Cards keep today's opaque rounded rect until the first card art " +
                                    "loads, then take the outline in one step; the mask is written here so " +
                                    "every later launch has it before the first card exists.");
                return;
            }
            byte[] raw = System.IO.File.ReadAllBytes(path);
            if (raw.Length < 18)
            {
                VRLog.Warn("Cards", $"CARD SILHOUETTE CACHE ({kind}): '{path}' is {raw.Length} bytes — too " +
                                    "short to be a header. Ignored; this session re-learns it.");
                return;
            }
            using var ms = new System.IO.MemoryStream(raw, writable: false);
            using var br = new System.IO.BinaryReader(ms);
            uint magic = br.ReadUInt32();
            byte version = br.ReadByte();
            byte storedKind = br.ReadByte();
            if (magic != CacheMagic || version != CacheVersion || storedKind != (byte)kind)
            {
                VRLog.Warn("Cards", $"CARD SILHOUETTE CACHE ({kind}): header mismatch in '{path}' " +
                                    $"(magic 0x{magic:X8} want 0x{CacheMagic:X8}, version {version} want " +
                                    $"{CacheVersion}, kind {storedKind} want {(byte)kind}). Ignored; this " +
                                    "session re-learns it.");
                return;
            }
            int w = br.ReadInt32();
            int h = br.ReadInt32();
            string source = br.ReadString();
            if (w <= 1 || h <= 1 || (long)w * h > 4_000_000L)
            {
                VRLog.Warn("Cards", $"CARD SILHOUETTE CACHE ({kind}): implausible footprint {w}x{h} — ignored.");
                return;
            }
            // ART RECT (v3): where the card art draws inside the footprint. Read before the mask so
            // a truncated file still fails on the mask length check below, as it always has.
            float ax = br.ReadSingle(), ay = br.ReadSingle();
            float aw = br.ReadSingle(), ah = br.ReadSingle();
            var artRect = new Rect(ax, ay, aw, ah);
            var alpha = br.ReadBytes(w * h);
            if (alpha.Length != w * h)
            {
                VRLog.Warn("Cards", $"CARD SILHOUETTE CACHE ({kind}): truncated ({alpha.Length} of {w * h} " +
                                    "mask bytes) — ignored; this session re-learns it.");
                return;
            }
            VRLog.Info("Cards", $"CARD SILHOUETTE CACHE ({kind}): loaded {w}x{h} from '{source}', art rect " +
                                $"{artRect.width:P1} x {artRect.height:P1} of the face — applying BEFORE the " +
                                "first card body draws.");
            SetSilhouette(kind, alpha, w, h, artRect, source, fromCache: true);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CARD SILHOUETTE CACHE ({kind}): load skipped ({ex.GetType().Name}: " +
                                $"{ex.Message}) — cards keep the rounded rect until a live capture.");
        }
    }

    private static void WriteSilhouetteCache(CardBodyKind kind, byte[] alpha, int w, int h,
                                             Rect artRect, string? source)
    {
        string path = CacheFilePath(kind);
        try
        {
            string? dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                System.IO.Directory.CreateDirectory(dir);
            using var ms = new System.IO.MemoryStream(w * h + 64);
            using (var bw = new System.IO.BinaryWriter(ms))
            {
                bw.Write(CacheMagic);
                bw.Write(CacheVersion);
                bw.Write((byte)kind);
                bw.Write(w);
                bw.Write(h);
                bw.Write(source ?? string.Empty);
                bw.Write(artRect.xMin);
                bw.Write(artRect.yMin);
                bw.Write(artRect.width);
                bw.Write(artRect.height);
                bw.Write(alpha, 0, w * h);
            }
            System.IO.File.WriteAllBytes(path, ms.ToArray());
            VRLog.Info("Cards", $"CARD SILHOUETTE CACHE ({kind}): wrote {w}x{h} from '{source ?? "n/a"}' to " +
                                $"'{path}' — the NEXT launch applies this before the first card is drawn, so " +
                                "the shape change is never seen again on this machine.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CARD SILHOUETTE CACHE ({kind}): write skipped ({ex.GetType().Name}: " +
                                $"{ex.Message}) — this session is unaffected; the next launch simply " +
                                "re-learns the shape the same way this one did.");
        }
    }

    /// <summary>
    /// A live capture ran while a mask was ALREADY applied (normally: applied from the cache).
    /// Never re-applies — re-applying mid-session IS the visible transition the user rejected —
    /// but rewrites the cache when the freshly measured mask disagrees, so a class whose card frame
    /// really does differ corrects itself on the next launch. Returns whether the file was rewritten.
    /// </summary>
    internal static bool RefreshSilhouetteCache(CardBodyKind kind, byte[] alpha, int w, int h,
                                                Rect artRect, string? source)
    {
        int i = (int)kind;
        byte[]? live = _footprints[i];
        // The ART RECT is part of what the file means, so a session that measures a different one
        // must rewrite even when the mask texels agree — otherwise a v3 file could keep describing a
        // letterbox that no longer exists (round 7; the mask/art-rect pair is the unit here).
        Rect liveArt = _footArt[i];
        bool artMoved = Mathf.Abs(liveArt.xMin - artRect.xMin) > 0.005f
                        || Mathf.Abs(liveArt.yMin - artRect.yMin) > 0.005f
                        || Mathf.Abs(liveArt.width - artRect.width) > 0.005f
                        || Mathf.Abs(liveArt.height - artRect.height) > 0.005f;
        if (!artMoved && live != null && _footW[i] == w && _footH[i] == h)
        {
            long differing = 0;
            for (int p = 0; p < alpha.Length; p++)
                if ((alpha[p] >= 128) != (live[p] >= 128))
                    differing++;
            float frac = (float)differing / alpha.Length;
            if (frac <= 0.005f)
            {
                VRLog.Info("Cards", $"CARD SILHOUETTE CACHE ({kind}): this session's live capture from " +
                                    $"'{source ?? "n/a"}' agrees with the applied mask to {1f - frac:P2} — " +
                                    "cached shape confirmed, nothing rewritten.");
                return false;
            }
            VRLog.Info("Cards", $"CARD SILHOUETTE CACHE ({kind}): live capture from '{source ?? "n/a"}' " +
                                $"differs from the applied mask (from '{_footSource[i] ?? "n/a"}') in " +
                                $"{frac:P1} of texels — the card frame is NOT the constant it was assumed " +
                                "to be. Rewriting the cache; the shape is corrected on the NEXT launch, " +
                                "deliberately not mid-session (that would be the visible transition).");
        }
        WriteSilhouetteCache(kind, alpha, w, h, artRect, source);
        return true;
    }

    /// <summary>Is the middle 20 % box of the footprint solidly opaque (a real card)?</summary>
    private static bool CenterOpaque(byte[] alpha, int w, int h)
    {
        int x0 = (int)(w * 0.4f), x1 = (int)(w * 0.6f);
        int y0 = (int)(h * 0.4f), y1 = (int)(h * 0.6f);
        int total = 0, opaque = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                total++;
                if (alpha[y * w + x] >= 128) opaque++;
            }
        return total > 0 && opaque >= total * 0.85f;
    }

    private static Texture2D MakeCutoutTexture(string name, Color32[] pixels, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            // Trilinear + aniso, not Bilinear/aniso-1 (aliasing report 2026-08, "die Linien und
            // Rahmen auf allen Karten"): having a mip chain is only half the sampling fix. With
            // FilterMode.Bilinear the GPU picks ONE mip level and snaps between levels, so a card
            // drifting in the fan pops across the mip boundary — that pop reads as crawling edges
            // in stereo; Trilinear blends the two levels instead. anisoLevel 8 is the other half:
            // cards lie nearly flat on the table and fan out at steep angles, and at grazing
            // angles an isotropic sampler picks a mip for the SHORT axis, which over-blurs along
            // one direction and still aliases along the other. Same values the mip bake gives the
            // game's own card art (CardFaceMipBake.BakedAnisoLevel) — one consistent card look.
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        VRLog.Info("Cards", $"CARD TEX: '{name}' {w}x{h} RGBA32 mips {tex.mipmapCount} " +
                            $"{tex.filterMode} aniso {tex.anisoLevel} — card cutout footprint.");
        return tex;
    }

    private static Material NewMaterial()
    {
        Shader? shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
        var m = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        if (m.HasProperty("_Glossiness"))
            m.SetFloat("_Glossiness", 0.25f);
        return m;
    }

    /// <summary>
    /// 128x128 card-back pattern (built once, cached): deep burgundy field, gold
    /// diamond lattice, double border — reads as "card back" at fan distance without
    /// any bundled art. Replaced wholesale when CardBacking.prefab ships in the bundle.
    /// </summary>
    private static Texture2D GetBackTexture()
    {
        if (_backTexture != null)
            return _backTexture;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
        {
            name = "GloomhavenVR.CardBack",
            wrapMode = TextureWrapMode.Clamp,
            // See MakeCutoutTexture for the full WHY. It matters most HERE: this pattern is
            // nothing BUT lines — a 1-px gold diamond lattice and a 2-px gold inner frame at
            // 128² — which is precisely the "Linien und Rahmen" content that shimmers under a
            // mip-snapping bilinear sampler on a card lying at a grazing angle on the table.
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
        };

        var field = new Color(0.28f, 0.08f, 0.10f);
        var fieldDark = new Color(0.22f, 0.06f, 0.08f);
        var gold = new Color(0.78f, 0.62f, 0.28f);
        var border = new Color(0.12f, 0.10f, 0.08f);

        var pixels = new Color[size * size];
        const int cell = 16; // lattice cell in pixels
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                Color c;
                if (edge < 4)
                {
                    c = border; // outer dark border
                }
                else if (edge < 6)
                {
                    c = gold; // thin gold inner frame
                }
                else
                {
                    // Diamond lattice: distance to the nearest diagonal grid line.
                    int lx = x % cell;
                    int ly = y % cell;
                    int d1 = Mathf.Abs(lx - ly);
                    int d2 = Mathf.Abs(lx + ly - cell);
                    bool onLine = d1 <= 1 || d2 <= 1;
                    // Subtle two-tone checker inside the lattice cells.
                    bool alt = ((x / cell) + (y / cell)) % 2 == 0;
                    c = onLine ? gold * 0.85f : (alt ? field : fieldDark);
                    c.a = 1f;
                }
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        // Keep CPU-readable: SetSilhouette samples this pattern (GetPixelBilinear) to
        // composite the card-back with the captured outline alpha.
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        VRLog.Info("Cards", $"CARD TEX: '{tex.name}' {size}x{size} RGBA32 mips {tex.mipmapCount} " +
                            $"{tex.filterMode} aniso {tex.anisoLevel} — procedural card-back " +
                            "lattice/frame (built once, shared by every card back).");
        _backTexture = tex;
        return tex;
    }
}
