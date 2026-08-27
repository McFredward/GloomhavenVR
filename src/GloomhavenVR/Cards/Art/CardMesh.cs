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
/// fan layout, dock apron, <c>VRCard.SweepFaceWidthWorld</c>) changes either way, because the
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
    /// THE BOARD KEYCAP — a SIGNET PLATE, authored at its REAL size in meters so the owning
    /// transform can stay unit-scaled (uniform scale keeps every chamfer a true 45° in world space,
    /// which is what lets it catch the shader's baked key).
    ///
    /// <para><b>ROUND 2 RE-CUT THE SHAPE. The user, verbatim:</b> <i>"Auch die Form der Buttons
    /// gefällt mir noch nicht. Rund und Viereckig sind vorgabe, aber ansonsten darfst du gerne
    /// kreativ werden."</i> What it was: one flat plateau behind a single 7 mm 45° chamfer, 40 verts
    /// and 20 triangles. What it is now, outward-to-inward:</para>
    /// <list type="bullet">
    /// <item>the vertical side WALL (submesh 2, dark warm band) and the hidden back;</item>
    /// <item>a 45° OUTER CHAMFER (submesh 1);</item>
    /// <item>a flat RIM LAND at the cap's frontmost plane, facing the viewer squarely (submesh 1);</item>
    /// <item>a 45° INNER CHAMFER stepping BACK (submesh 1) — the recess;</item>
    /// <item>the RECESSED FIELD (submesh 0, the state colour) that carries the carved symbol and,
    /// floating just proud of it, the caption.</item>
    /// </list>
    ///
    /// <para><b>WHY A RIM LAND, measured rather than chosen.</b> <c>BoardLit</c> shades by world
    /// normal against two baked studio directions and never reads a scene light, so on this surface
    /// a 45° chamfer is a FIXED value, not a highlight that travels as the player moves. The
    /// strongest "this is raised" cue it can give is therefore the largest area that both faces the
    /// viewer and carries the bright bevel tint — which is exactly a flat land at the frontmost
    /// plane. The two chamfers either side of it supply the silhouette's value steps.</para>
    ///
    /// <para><b>AND THE SHAPE PAID FOR THE CAPTION.</b> The old 7 mm chamfer is 0.159 of the Bronze
    /// cap's short side by itself; the whole new bezel is <see cref="CapFaceLayout.BezelTotal"/> =
    /// 0.135. That is where the millimetres came from that let "Auswahl beenden" wrap to two lines
    /// instead of being cut to "AUSWAHL BEEN" — see <c>CapFaceLayout</c> for the report and the
    /// arithmetic.</para>
    ///
    /// <para>The cap spans local z = −<c>thickness</c> (front, viewer side) to 0 (back),
    /// matching the old placement, so the press travel and the collider are unchanged. Each face
    /// carries its own flat-shaded vertices and normal; triangle winding is derived from the outward
    /// normal so every face is front-facing regardless of corner order. Watertight counts:
    /// <b>72 verts, 36 tris, submesh index counts [6, 72, 30]</b> (field 6 / bezel 72 / walls+back
    /// 30) — asserted by <c>BoardButton.LogCapDiagnostics</c> and by the companion render station.</para>
    /// </summary>

    /// <summary>
    /// THE TANGENT EVERY KEYCAP VERTEX CARRIES, and why it is one constant.
    ///
    /// <para><b>THE DEFECT THIS CLOSES.</b> Neither keycap mesh wrote tangents at all — measured,
    /// <c>mesh.tangents.Length == 0</c> — and <c>BoardLit</c> builds its whole tangent basis from
    /// them: <c>o.wt = UnityObjectToWorldDir(v.tangent.xyz)</c> and
    /// <c>o.wb = cross(o.wn, o.wt) * v.tangent.w</c>. With no TANGENT stream bound, that is
    /// whatever the graphics API supplies for a missing vertex attribute, which is undefined and
    /// not guaranteed to agree between the editor's GL and the rig's D3D11. The normal map was
    /// reaching the pixel through a basis nobody had specified. It went unnoticed because the only
    /// map bound was a low-contrast shared grain; this round binds a per-board map whose carved
    /// symbol is the whole point, so an undefined basis stops being survivable.</para>
    ///
    /// <para><b>WHY A CONSTANT IS THE RIGHT ANSWER HERE, not a cop-out.</b> Both keycap meshes map
    /// UV as a pure function of object XY — <c>u = x/width + 0.5</c>, <c>v = y/height + 0.5</c> —
    /// on EVERY face. So the direction of increasing u is object <c>+X</c> everywhere and the
    /// direction of increasing v is <c>+Y</c> everywhere; there is nothing per-vertex to derive.
    /// <c>w = -1</c> is what makes <c>cross(n, t) * w</c> come out as <c>+Y</c> on the front-facing
    /// plateau (n = <c>-Z</c>: <c>cross((0,0,-1),(1,0,0)) = (0,-1,0)</c>), which is the face the
    /// symbol is carved into and the only one a player looks at.</para>
    ///
    /// <para><b>AND WHERE IT IS A CONVENTION RATHER THAN A DERIVATION, stated rather than hidden:</b>
    /// on the vertical side WALLS the UV is degenerate — a wall spans a constant y (or x) and varies
    /// only in z, so v (or u) does not change across it at all and no correct tangent frame exists
    /// there. That is the same fact the UV comment above already records as "the walls sample a THIN
    /// grain strip". Keeping the plateau's frame there is the choice that makes the grain run the
    /// same way over the fold instead of turning at it. The hidden BACK cap gets its green channel
    /// mirrored by the same constant; it is inside the well and is never seen.</para>
    ///
    /// <para><c>Mesh.RecalculateTangents</c> was the obvious alternative and is worse here: it
    /// solves per-triangle from the UV gradient, which is exactly the quantity that is degenerate on
    /// eight of this mesh's twenty triangles.</para>
    /// </summary>
    private static readonly Vector4 KeycapTangent = new(1f, 0f, 0f, -1f);

    /// <summary>
    /// THE SIGNET BEZEL, SOLVED ONCE for both cap shapes and for the callers that need to know where
    /// the recessed field ended up.
    ///
    /// <para>The three fractions are <see cref="CapFaceLayout"/>'s — the same numbers the atlas
    /// generator carves against and the caption box is measured in — taken against
    /// <paramref name="refSide"/>, the cap's SHORT side (its diameter, on a disc). The two clamps
    /// only ever SHRINK the bezel, so a cap that is tiny or thin gets a smaller one rather than an
    /// inverted one; on all three shipped board sizes neither clamp fires, which is the case that
    /// matters and the reason the field band is a single pair of numbers.</para>
    ///
    /// <para>It is INTERNAL and not private because <c>BoardButton.Create</c> seats the caption
    /// exactly <see cref="LabelProudOfField"/> in front of the field plane, and the field plane is
    /// <c>−thickness + step</c>. Deriving that in the caller from the same fractions would be a
    /// second copy of this arithmetic, and a caption floating in front of where the field USED to be
    /// is not something any gate in this repository can see.</para>
    /// </summary>
    internal static void CapProfile(float refSide, float halfLimit, float thickness,
                                    out float chamfer, out float rim, out float step)
    {
        float span = CapFaceLayout.BezelTotal * refSide;
        float scale = 1f;
        if (span > 0f)
            scale = Mathf.Min(scale, halfLimit * 0.9f / span);
        float chamferSpan = CapFaceLayout.BezelChamfer * refSide;
        if (chamferSpan > 0f)
            scale = Mathf.Min(scale, thickness * 0.45f / chamferSpan);
        scale = Mathf.Clamp(scale, 0f, 1f);
        chamfer = CapFaceLayout.BezelChamfer * refSide * scale;
        rim = CapFaceLayout.BezelRim * refSide * scale;
        step = CapFaceLayout.BezelStep * refSide * scale;
    }

    /// <summary>How far in front of the recessed FIELD the caption quad floats (meters). Small: the
    /// caption is engraved parchment on the plate, not a sign hovering over it, and a large gap
    /// shows as parallax between the two the moment the player looks along the board.</summary>
    internal const float LabelProudOfField = 0.0015f;

    /// <summary>
    /// THE CAP'S OUTLINE, INSET BY <paramref name="t"/> — one function for all three corner
    /// treatments, and it is an EXACT inward offset rather than a scale.
    ///
    /// <para>A convex outline is the intersection of half-planes <c>n·p ≤ d</c>, and offsetting it
    /// inward by t is subtracting t from every d. That is the whole derivation, and it is why each
    /// case below is a closed form rather than a fit:</para>
    /// <list type="bullet">
    /// <item><b>Sharp</b> — half-extents shrink by t. Four points, in the same order and at the same
    /// places the pre-round-4 <c>AddBand</c> put them.</item>
    /// <item><b>Clip</b> — an octagon. The 45° face's own offset moves it inward by t along a normal
    /// of (1,1)/√2, so the clip's length along each EDGE shrinks by t(√2−1), not by t. Getting that
    /// factor wrong is invisible on the outer band and opens a visible wedge at the field.</item>
    /// <item><b>Round</b> — the four arc CENTRES are invariant under inward offset (c = hw − r, and
    /// (hw − t) − (r − t) = hw − r), so only the radius moves. The radius CLAMPS at zero rather than
    /// going negative: offsetting a fillet inward past its own radius genuinely does produce a sharp
    /// corner, and steel's fillet (0.100 of the short side) is smaller than the bezel (0.135), so
    /// its field corner is square on purpose and not by accident.</item>
    /// </list>
    ///
    /// <para><b>The point COUNT and ORDER do not depend on t</b>, which is the property the bands
    /// rely on: two rings at different insets are bridged vertex to vertex, so every fold is a
    /// shared edge and the solid is watertight exactly as the rectangle version was.</para>
    /// </summary>
    private static Vector2[] CapRing(float hw, float hh, float t,
                                     CapFaceLayout.CapCorner corner, float cornerSize, int arcSegs)
    {
        float X = Mathf.Max(1e-5f, hw - t), Y = Mathf.Max(1e-5f, hh - t);
        switch (corner)
        {
            case CapFaceLayout.CapCorner.Clip:
            {
                // k is what is left of the clip at this inset. Clamped to the half-extent so a
                // deep inset degenerates to a diamond rather than folding through itself.
                float k = Mathf.Clamp(cornerSize - t * (Mathf.Sqrt(2f) - 1f), 0f, Mathf.Min(X, Y));
                return new[]
                {
                    new Vector2(-(X - k), Y), new Vector2(X - k, Y),
                    new Vector2(X, Y - k),    new Vector2(X, -(Y - k)),
                    new Vector2(X - k, -Y),   new Vector2(-(X - k), -Y),
                    new Vector2(-X, -(Y - k)), new Vector2(-X, Y - k),
                };
            }
            case CapFaceLayout.CapCorner.Round:
            {
                float r0 = Mathf.Min(cornerSize, Mathf.Min(hw, hh));
                float cx = Mathf.Max(0f, hw - r0), cy = Mathf.Max(0f, hh - r0);
                float R = Mathf.Max(0f, r0 - t);
                cx = Mathf.Min(cx, X); cy = Mathf.Min(cy, Y);
                int n = arcSegs;
                var pts = new System.Collections.Generic.List<Vector2>(4 + 4 * n);
                // Clockwise from the top-left, matching the Sharp order below.
                void Arc(float sx, float sy, float a0, float a1)
                {
                    for (int i = 1; i <= n; i++)
                    {
                        float a = Mathf.Lerp(a0, a1, i / (float)n);
                        pts.Add(new Vector2(sx + R * Mathf.Cos(a), sy + R * Mathf.Sin(a)));
                    }
                }
                pts.Add(new Vector2(-cx, Y));
                pts.Add(new Vector2(cx, Y));
                Arc(cx, cy, Mathf.PI * 0.5f, 0f);                    // top-right
                pts.Add(new Vector2(X, -cy));
                Arc(cx, -cy, 0f, -Mathf.PI * 0.5f);                  // bottom-right
                pts.Add(new Vector2(-cx, -Y));
                Arc(-cx, -cy, -Mathf.PI * 0.5f, -Mathf.PI);          // bottom-left
                pts.Add(new Vector2(-X, cy));
                Arc(-cx, cy, Mathf.PI, Mathf.PI * 0.5f);             // top-left
                // THE LAST ARC ENDS WHERE THE FIRST POINT STARTS. Arc() includes its end angle, and
                // the top-left arc's end (90°) is (−cx, cy + R) = (−cx, Y), which is pts[0]. Leaving
                // the duplicate in gives the ring one zero-length segment, which AddRingBand's
                // degeneracy guard then skips in EVERY band — a one-quad gap at the top-left corner
                // of the outer chamfer, the rim land and the inner chamfer alike. It is small and it
                // is a genuine hole: the mesh audit caught it as the only two non-flat open edges on
                // the bronze cap, on the inner chamfer, exactly at (−cx, Y).
                if ((pts[pts.Count - 1] - pts[0]).sqrMagnitude < 1e-12f)
                    pts.RemoveAt(pts.Count - 1);
                return pts.ToArray();
            }
            default:
                return new[]
                {
                    new Vector2(-X, Y), new Vector2(X, Y), new Vector2(X, -Y), new Vector2(-X, -Y),
                };
        }
    }

    /// <summary>Arc segments per rounded corner. Eight puts a facet every 11°, which is smooth at a
    /// 40-63 mm cap; the round CAP itself uses 64 around a full circle, i.e. 16 per quadrant, and is
    /// a much larger arc.</summary>
    private const int CapCornerArcSegments = 8;

    /// <summary>
    /// A DOME HEAD — the rivet on the steel board's frame, the boss on the bronze one. An ellipsoid
    /// cap of radius <paramref name="rs"/> standing <paramref name="hs"/> proud of
    /// <paramref name="zPlane"/> toward the viewer (−Z).
    ///
    /// <para>SHARED BY BOTH CAP SHAPES on purpose. It was written twice first, once in each builder,
    /// and the two copies are exactly the kind of pair this repository has already been bitten by —
    /// the square cap and the round cap must read as one set, and two implementations of the same
    /// rivet is the cheapest possible way to make them not.</para>
    ///
    /// <para>The normal is the ELLIPSOID's own gradient, not a sphere's. At hs = 0.55·rs a sphere
    /// normal is up to 12° wrong on the flank, and on <c>BoardLit</c> — which shades against baked
    /// key directions and never reads a scene light — that is a fixed shading error, not one that
    /// moves and forgives itself.</para>
    /// </summary>
    private static void AddDomeHead(System.Collections.Generic.List<Vector3> verts,
                                    System.Collections.Generic.List<Vector3> norms,
                                    System.Collections.Generic.List<Vector2> uvs,
                                    System.Collections.Generic.List<int> sm,
                                    System.Func<Vector3, Vector2> uv,
                                    float px, float py, float zPlane, float rs, float hs)
    {
        const int Lat = 4, Seg = 12;
        Vector3 P(int k, int s)
        {
            float phi = (k / (float)Lat) * Mathf.PI * 0.5f;
            float th = (s % Seg) * 2f * Mathf.PI / Seg;
            float rr = rs * Mathf.Cos(phi);
            return new Vector3(px + rr * Mathf.Cos(th), py + rr * Mathf.Sin(th),
                               zPlane - hs * Mathf.Sin(phi));
        }
        Vector3 N(Vector3 p)
        {
            float X = p.x - px, Y = p.y - py, Z = zPlane - p.z;
            var g = new Vector3(X / (rs * rs), Y / (rs * rs), -Z / Mathf.Max(1e-6f, hs * hs));
            return g.sqrMagnitude < 1e-12f ? Vector3.back : g.normalized;
        }
        // THE POLE IS A TRIANGLE FAN, NOT A ROW OF COLLAPSED QUADS. Emitting the top row as quads
        // whose far edge has zero length costs Seg fully degenerate triangles per dome and — the
        // reason it was found rather than tolerated — leaves each apex edge referenced three times,
        // which reads to any watertightness check as a hole that is not there. Twelve phantom open
        // edges per rivet, times four rivets, on every metal cap.
        var apex = new Vector3(px, py, zPlane - hs);
        for (int k = 0; k < Lat; k++)
            for (int s = 0; s < Seg; s++)
            {
                Vector3 a = P(k, s), b = P(k, s + 1);
                bool pole = k == Lat - 1;
                Vector3 c2 = pole ? apex : P(k + 1, s + 1);
                Vector3 d2 = pole ? apex : P(k + 1, s);
                int b0 = verts.Count;
                if (pole)
                {
                    foreach (Vector3 p in new[] { a, b, apex })
                    {
                        verts.Add(p); norms.Add(p == apex ? Vector3.back : N(p)); uvs.Add(uv(p));
                    }
                    if (Vector3.Dot(Vector3.Cross(b - a, apex - a), N(a)) > 0f)
                    { sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2); }
                    else
                    { sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1); }
                    continue;
                }
                foreach (Vector3 p in new[] { a, b, c2, d2 })
                {
                    verts.Add(p); norms.Add(N(p)); uvs.Add(uv(p));
                }
                if (Vector3.Dot(Vector3.Cross(b - a, c2 - a), N(a)) > 0f)
                {
                    sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2);
                    sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 3);
                }
                else
                {
                    sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1);
                    sm.Add(b0); sm.Add(b0 + 3); sm.Add(b0 + 2);
                }
            }
    }

    /// <summary>The ModBuild 289 cap: a plain right-angled signet plate, for a cap with no board.</summary>
    internal static Mesh BuildBeveledKeycap(float width, float height, float thickness)
        => BuildBeveledKeycap(width, height, thickness, CapFaceLayout.PlainConstruction);

    internal static Mesh BuildBeveledKeycap(float width, float height, float thickness,
                                            CapFaceLayout.CapConstruction con)
    {
        float hw = width * 0.5f, hh = height * 0.5f;
        CapProfile(Mathf.Min(width, height), Mathf.Min(hw, hh), thickness,
                   out float c, out float rim, out float step);

        float zTop = -thickness;                 // frontmost plane — the RIM LAND lives here
        float zWallTop = -thickness + c;         // where the outer chamfer meets the vertical wall
        float zField = -thickness + step;        // the recessed field, one step BACK from the rim
        float zBack = 0f;                        // hidden back

        var verts = new System.Collections.Generic.List<Vector3>(72);
        var norms = new System.Collections.Generic.List<Vector3>(72);
        var uvs = new System.Collections.Generic.List<Vector2>(72);
        var top = new System.Collections.Generic.List<int>(6);
        var ring = new System.Collections.Generic.List<int>(72);
        var walls = new System.Collections.Generic.List<int>(30);

        // Task #5a: planar UV from the cap's local XY, normalized 0..1 across the footprint —
        // the SAME convention CardMesh.Build uses for the card front (u = x/width + 0.5,
        // v = y/height + 0.5). Applied to EVERY vertex (field, rim, both chamfers AND walls) so the
        // grain texture maps sensibly instead of the old single (0.5, 0.5) texel. Because the
        // mapping is continuous in XY it is watertight at every fold (shared XY → shared UV → no
        // seam). The vertical walls share their edge's XY, so they sample a THIN grain strip along
        // that edge (a subtle stretched grain — acceptable per the task, texture set to Repeat).
        //
        // AND THIS IS THE PROPERTY CardMesh.KeycapTangent DEPENDS ON. That constant (1, 0, 0, -1)
        // is EXACT only while u is a pure function of x and v a pure function of y on every vertex
        // of the mesh. The signet profile keeps it exactly: every ring below is a rectangle in XY
        // and every vertex is UV'd through this one function. Nothing here needs a per-vertex basis
        // and RecalculateTangents stays the wrong answer for the same reason it was before.
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
        // TOWARD. So to be visible from +n the EMITTED winding's RH normal must point +n.
        void AddQuad(System.Collections.Generic.List<int> sm,
                     Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Vector3 n)
        {
            int b0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(cc); verts.Add(d);
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(Uv(a)); uvs.Add(Uv(b)); uvs.Add(Uv(cc)); uvs.Add(Uv(d));
            Vector3 rh = Vector3.Cross(b - a, cc - a); // RH normal of triangle (a,b,cc)
            if (Vector3.Dot(rh, n) > 0f)
            {
                sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2);
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 3);
            }
            else
            {
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1);
                sm.Add(b0); sm.Add(b0 + 3); sm.Add(b0 + 2);
            }
        }

        // One BAND of the bezel: four mitred quads bridging rectangle A (half-extents axh, ayh at
        // depth az) out to rectangle B (bxh, byh at bz). `nOut` is the normal's component along the
        // side's own outward direction and `nZ` its component toward the viewer (−Z); they are
        // passed rather than derived because the four bands do not share one rule — the walls run
        // front-to-back at a CONSTANT radius, where any "rotate the edge direction" formula gives
        // the inward normal. Corner folds are shared edges, so every band is watertight.
        void AddBand(System.Collections.Generic.List<int> sm,
                     float axh, float ayh, float az, float bxh, float byh, float bz,
                     float nOut, float nZ)
        {
            float len = Mathf.Sqrt(nOut * nOut + nZ * nZ);
            if (len <= 1e-6f)
                return;
            float o = nOut / len, z = nZ / len;
            AddQuad(sm, new(-axh, ayh, az), new(axh, ayh, az), new(bxh, byh, bz), new(-bxh, byh, bz),
                    new Vector3(0f, o, z));    // +Y
            AddQuad(sm, new(axh, ayh, az), new(axh, -ayh, az), new(bxh, -byh, bz), new(bxh, byh, bz),
                    new Vector3(o, 0f, z));    // +X
            AddQuad(sm, new(axh, -ayh, az), new(-axh, -ayh, az), new(-bxh, -byh, bz), new(bxh, -byh, bz),
                    new Vector3(0f, -o, z));   // −Y
            AddQuad(sm, new(-axh, -ayh, az), new(-axh, ayh, az), new(-bxh, byh, bz), new(-bxh, -byh, bz),
                    new Vector3(-o, 0f, z));   // −X
        }

        // A BAND between two RINGS, for the per-board constructions. Same contract as AddBand
        // above and the same reason the sign is passed rather than derived: the four bands do not
        // share one rule — the inner chamfer faces INWARD and the wall faces along no z at all —
        // so `outSign` (+1 outward, −1 inward) is stated and only the MAGNITUDES are derived from
        // the band's own geometry. Checked against all four of the rectangle version's bands
        // before it was used: outer chamfer (1,−1), rim land (0,−1), inner chamfer (−1,−1) and
        // wall (1,0) all come out exactly as AddBand is called with them today.
        // `zSign` is −1 for a surface facing the VIEWER and +1 for one facing away — the undercut's
        // shoulder is the underside of an overhang and is the only band in either cap that faces
        // backwards. It is passed for exactly the reason `outSign` is: the bands do not share one
        // rule, and deriving this one from the geometry is impossible because a flat annulus has
        // the same geometry whichever way it faces.
        void AddRingBand(System.Collections.Generic.List<int> sm,
                         Vector2[] A, float zA, Vector2[] B, float zB, float outSign,
                         float zSign = -1f)
        {
            int n = A.Length;
            float dz = Mathf.Abs(zB - zA);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector2 a = A[i], b = A[j], bi = B[j], ai = B[i];
                Vector2 e = b - a;
                if (e.sqrMagnitude < 1e-12f)
                    continue;                       // a collapsed arc segment (fillet clamped to 0)
                var outw = new Vector2(e.y, -e.x).normalized;
                if (Vector2.Dot(outw, (a + b) * 0.5f) < 0f)
                    outw = -outw;
                float dt = ((a - ai).magnitude + (b - bi).magnitude) * 0.5f;
                var nrm = new Vector3(outw.x * dz * outSign, outw.y * dz * outSign, dt * zSign);
                if (nrm.sqrMagnitude < 1e-12f)
                    nrm = new Vector3(outw.x * outSign, outw.y * outSign, 0f);
                AddQuad(sm, new Vector3(a.x, a.y, zA), new Vector3(b.x, b.y, zA),
                            new Vector3(bi.x, bi.y, zB), new Vector3(ai.x, ai.y, zB), nrm.normalized);
            }
        }

        // A filled RING as a triangle fan about the cap's centre.
        void AddRingFan(System.Collections.Generic.List<int> sm, Vector2[] R, float z, bool front)
        {
            int b0 = verts.Count;
            Vector3 n = front ? Vector3.back : Vector3.forward;
            for (int i = 0; i < R.Length; i++)
            {
                verts.Add(new Vector3(R[i].x, R[i].y, z));
                norms.Add(n);
                uvs.Add(Uv(new Vector3(R[i].x, R[i].y, z)));
            }
            int centre = verts.Count;
            verts.Add(new Vector3(0f, 0f, z));
            norms.Add(n);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < R.Length; i++)
            {
                int nx = (i + 1) % R.Length;
                // Rings run clockwise seen from the viewer (−Z). A front face must have its
                // right-hand normal pointing −Z, which is the (centre, i, next) order; the back
                // face is the reverse. Taken from BuildRoundKeycap.AddFan, which is the winding
                // this project has already proved outward-facing on hardware.
                if (front) { sm.Add(centre); sm.Add(b0 + i); sm.Add(b0 + nx); }
                else { sm.Add(centre); sm.Add(b0 + nx); sm.Add(b0 + i); }
            }
        }

        void AddDome(System.Collections.Generic.List<int> sm, float px, float py, float zPlane,
                     float rs, float hs)
            => AddDomeHead(verts, norms, uvs, sm, Uv, px, py, zPlane, rs, hs);

        // A DENTIL BLOCK — one of the small raised rectangles of the oak board's own border, sitting
        // on the rim land and standing `rise` proud of it. Five visible faces; the sixth is the
        // rim land it stands on.
        void AddBlock(System.Collections.Generic.List<int> sm,
                      float x0, float x1, float y0, float y1, float zBase, float rise)
        {
            float zf = zBase - rise;
            AddQuad(sm, new(x0, y1, zf), new(x1, y1, zf), new(x1, y0, zf), new(x0, y0, zf), Vector3.back);
            AddQuad(sm, new(x0, y1, zBase), new(x1, y1, zBase), new(x1, y1, zf), new(x0, y1, zf), Vector3.up);
            AddQuad(sm, new(x0, y0, zBase), new(x1, y0, zBase), new(x1, y0, zf), new(x0, y0, zf), Vector3.down);
            AddQuad(sm, new(x1, y1, zBase), new(x1, y0, zBase), new(x1, y0, zf), new(x1, y1, zf), Vector3.right);
            AddQuad(sm, new(x0, y1, zBase), new(x0, y0, zBase), new(x0, y0, zf), new(x0, y1, zf), Vector3.left);
        }

        float r1x = hw - c, r1y = hh - c;                    // inner edge of the outer chamfer
        float r2x = r1x - rim, r2y = r1y - rim;              // inner edge of the rim land
        float r3x = r2x - step, r3y = r2y - step;            // the recessed FIELD

        if (!con.IsPlain)
        {
            // ---------------------------------------------------------------------------------
            // THE PER-BOARD CONSTRUCTION. Every band below lives in d ∈ [0, BezelTotal]; the FIELD
            // ring is taken at exactly BezelTotal, so CapFaceLayout.FieldLo, the caption solve and
            // the atlas's cell arithmetic are untouched by any of it. See CapFaceLayout.
            // ---------------------------------------------------------------------------------
            float shortSide = Mathf.Min(width, height);
            float cs = con.CornerFrac * shortSide;
            Vector2[] Ring(float t) => CapRing(hw, hh, t, con.Corner, cs, CapCornerArcSegments);

            // [1] THE OUTER ZONE — one 45° chamfer, or the stepped double TERRACE that every
            // board's chosen round option (OA-R2 / ST-R2 / BR-R2) shows. The fractions are of the
            // chamfer's own width and depth, so a terrace never eats into the rim land.
            float outerTop = con.FlatLand ? zTop : zWallTop;
            if (con.FlatLand)
            {
                // A SQUARE-EDGED PLATE: the outer zone is one flat land at the frontmost plane,
                // and the wall runs straight up to it. That is the joiner's edge the oak board's
                // own recesses have, and it is what gives the dentil ring a band 0.105 of the
                // short side wide to stand on instead of 0.045.
                AddRingBand(ring, Ring(0f), zTop, Ring(c + rim), zTop, 1f);
            }
            else if (con.Terrace)
            {
                float zMid = Mathf.Lerp(zWallTop, zTop, 0.55f);
                AddRingBand(ring, Ring(0f), zWallTop, Ring(0.18f * c), zMid, 1f);   // riser
                AddRingBand(ring, Ring(0.18f * c), zMid, Ring(0.58f * c), zMid, 1f); // tread
                AddRingBand(ring, Ring(0.58f * c), zMid, Ring(0.76f * c), zTop, 1f); // riser
                AddRingBand(ring, Ring(0.76f * c), zTop, Ring(c), zTop, 1f);         // tread
                AddRingBand(ring, Ring(c), zTop, Ring(c + rim), zTop, 1f);           // rim land
            }
            else
            {
                AddRingBand(ring, Ring(0f), zWallTop, Ring(c), zTop, 1f);
                AddRingBand(ring, Ring(c), zTop, Ring(c + rim), zTop, 1f);           // rim land
            }

            AddRingBand(ring, Ring(c + rim), zTop, Ring(c + rim + step), zField, -1f); // inner chamfer

            // [0] THE RECESSED FIELD — unmoved, unchanged, and flat.
            AddRingFan(top, Ring(c + rim + step), zField, front: true);

            // [2] THE WALLS, with the UNDERCUT SKIRT. The crown keeps the full footprint; only the
            // base steps IN, so nothing here can touch the bezel, the field or the caption box —
            // and the cap can only get narrower below the shoulder, so it cannot foul its seat.
            float uc = con.UndercutFrac * shortSide;
            if (uc > 0f)
            {
                float zShoulder = Mathf.Lerp(outerTop, zBack, 0.38f);
                AddRingBand(walls, Ring(0f), outerTop, Ring(0f), zShoulder, 1f);   // crown wall
                AddRingBand(walls, Ring(0f), zShoulder, Ring(uc), zShoulder, 1f, zSign: 1f); // shoulder
                AddRingBand(walls, Ring(uc), zShoulder, Ring(uc), zBack, 1f);     // skirt
                AddRingFan(walls, Ring(uc), zBack, front: false);
            }
            else
            {
                AddRingBand(walls, Ring(0f), outerTop, Ring(0f), zBack, 1f);
                AddRingFan(walls, Ring(0f), zBack, front: false);
            }

            // THE HARDWARE, on the rim land, in the BEZEL submesh so it takes the bright bevel
            // tint and reads as fitted metal rather than as part of the face.
            if (con.Stud == CapFaceLayout.CapStud.Dome && con.StudFrac > 0f)
            {
                float rs = Mathf.Min(con.StudFrac * shortSide, rim * 0.95f);
                Vector2[] seat = Ring(c + rim * 0.5f);
                // The four CORNER-most points of the rim-land ring, found by projection rather
                // than by index: the ring's point count and ordering differ between Clip (8) and
                // Round (4 + 4·segments), and an index that is right for one is silently wrong
                // for the other.
                foreach (Vector2 dir in new[] { new Vector2(1, 1), new Vector2(1, -1),
                                                new Vector2(-1, -1), new Vector2(-1, 1) })
                {
                    Vector2 best = seat[0];
                    float bestDot = float.NegativeInfinity;
                    foreach (Vector2 p in seat)
                    {
                        float dp = Vector2.Dot(p, dir.normalized);
                        if (dp > bestDot) { bestDot = dp; best = p; }
                    }
                    AddDome(ring, best.x, best.y, zTop, rs, rs * 0.55f);
                }
            }

            // THE DENTIL RING — the oak board's own border, at cap scale. Blocks sit on the rim
            // land along the four STRAIGHT runs; the corners are left clear, which is what the
            // board does at its own corners too.
            if (con.Dentils > 0 && con.DentilRise > 0f)
            {
                float rise = con.DentilRise * shortSide;
                float yOut = hh - c, yIn = hh - c - rim;
                float xOut = hw - c, xIn = hw - c - rim;
                // The straight run of the rim land, taken from the ring's own inner edge so a clip
                // or a fillet shortens it correctly instead of running blocks off the corner.
                Vector2[] inner = Ring(c + rim);
                float runX = 0f, runY = 0f;
                foreach (Vector2 p in inner)
                {
                    if (Mathf.Abs(Mathf.Abs(p.y) - yIn) < rim * 0.25f) runX = Mathf.Max(runX, Mathf.Abs(p.x));
                    if (Mathf.Abs(Mathf.Abs(p.x) - xIn) < rim * 0.25f) runY = Mathf.Max(runY, Mathf.Abs(p.y));
                }
                void Row(bool horizontal, float sign)
                {
                    float run = horizontal ? runX : runY;
                    if (run <= 0f)
                        return;
                    int n = con.Dentils;
                    float pitch = 2f * run / n;
                    float half = pitch * 0.32f;          // 64 % block, 36 % gap — the board's ratio
                    for (int i = 0; i < n; i++)
                    {
                        float ctr = -run + pitch * (i + 0.5f);
                        if (horizontal)
                            AddBlock(ring, ctr - half, ctr + half,
                                     sign > 0 ? yIn : -yOut, sign > 0 ? yOut : -yIn, zTop, rise);
                        else
                            AddBlock(ring, sign > 0 ? xIn : -xOut, sign > 0 ? xOut : -xIn,
                                     ctr - half, ctr + half, zTop, rise);
                    }
                }
                Row(horizontal: true, sign: 1f);
                Row(horizontal: true, sign: -1f);
                Row(horizontal: false, sign: 1f);
                Row(horizontal: false, sign: -1f);
            }
        }
        else
        {

        // [0] THE RECESSED FIELD — the state colour, the carved symbol, and the caption above it.
        AddQuad(top, new(-r3x, r3y, zField), new(r3x, r3y, zField),
                     new(r3x, -r3y, zField), new(-r3x, -r3y, zField), Vector3.back);

        // [1] THE BEZEL — outer chamfer, rim land, inner chamfer. All three take the BRIGHT bevel
        // material, so the field sits in a lit frame rather than behind a single 45° edge. The RIM
        // LAND is the piece that did not exist before: it is the largest area of the cap that faces
        // the viewer squarely while carrying the bevel tint, and on a shader that bakes its key
        // directions (BoardLit reads no scene light) a squarely-facing lit face is the strongest
        // "raised" cue available — a chamfer is a fixed value, not a highlight that moves.
        AddBand(ring, hw, hh, zWallTop, r1x, r1y, zTop, 1f, -1f);      // outer chamfer, 45°
        AddBand(ring, r1x, r1y, zTop, r2x, r2y, zTop, 0f, -1f);        // rim land, flat to the viewer
        AddBand(ring, r2x, r2y, zTop, r3x, r3y, zField, -1f, -1f);     // inner chamfer, stepping down

        // [2] THE WALLS + the hidden back (kept so the solid never shows a hole if seen edge-on).
        AddBand(walls, hw, hh, zWallTop, hw, hh, zBack, 1f, 0f);
        AddQuad(walls, new(-hw, hh, zBack), new(hw, hh, zBack), new(hw, -hh, zBack), new(-hw, -hh, zBack),
                Vector3.forward);

        }

        var mesh = new Mesh { name = "GloomhavenVR.SignetKeycap" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        // See KeycapTangent: BoardLit builds its whole TBN from this stream, and until this line
        // existed the stream did not.
        mesh.SetTangents(new System.Collections.Generic.List<Vector4>(
            System.Linq.Enumerable.Repeat(KeycapTangent, verts.Count)));
        mesh.subMeshCount = 3;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(ring, 1);
        mesh.SetTriangles(walls, 2);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// The SIGNET PROFILE on a disc — the round board caps (the two rest pads, and Confirm/Undo on
    /// a board whose shape is Round). Same five zones as <see cref="BuildBeveledKeycap"/> and the
    /// same three submeshes, so the two shapes read as one family and the caller's
    /// field / bezel / wall materials drive either one unchanged.
    ///
    /// <para><b>THIS IS A NEW MESH, NOT A CHANGE TO <see cref="BuildRoundCap"/>, and deliberately.</b>
    /// That plain disc has three other callers — the cap's own backing ring
    /// (<c>PlayTray.7.Nested</c> and its mirror) and the map room's button rail — and every one of
    /// them assigns a single <c>sharedMaterial</c>. Giving the shared disc three submeshes would
    /// have left submeshes 1 and 2 with no material on furniture nobody is looking at in this round.</para>
    ///
    /// <para>UVs are planar XY over the disc's own footprint exactly as before
    /// (u = x/diameter + 0.5, v = y/diameter + 0.5), so <see cref="KeycapTangent"/> is exact here
    /// for the same reason it is on the square cap, and the atlas cell lands on the field the same
    /// way. The rim is the one place u and v are constant along z; that degenerate case is the disc's
    /// own and is unchanged.</para>
    /// </summary>
    internal static Mesh BuildRoundKeycap(float diameter, float thickness, int segments)
        => BuildRoundKeycap(diameter, thickness, segments, CapFaceLayout.PlainConstruction);

    internal static Mesh BuildRoundKeycap(float diameter, float thickness, int segments,
                                          CapFaceLayout.CapConstruction con)
    {
        segments = Mathf.Clamp(segments, 12, 128);
        int seg = segments;
        float rOut = diameter * 0.5f;
        float h = Mathf.Max(0.0005f, thickness * 0.5f);
        CapProfile(diameter, rOut, thickness, out float c, out float rim, out float step);

        float zFront = -h;                 // viewer side (−Z) — the RIM LAND plane
        float zWallTop = -h + c;
        float zField = -h + step;
        float zBack = h;
        float r1 = rOut - c, r2 = r1 - rim, r3 = r2 - step;

        var verts = new System.Collections.Generic.List<Vector3>(seg * 10 + 2);
        var norms = new System.Collections.Generic.List<Vector3>(seg * 10 + 2);
        var uvs = new System.Collections.Generic.List<Vector2>(seg * 10 + 2);
        var top = new System.Collections.Generic.List<int>(seg * 3);
        var ring = new System.Collections.Generic.List<int>(seg * 18);
        var walls = new System.Collections.Generic.List<int>(seg * 9);

        Vector2 Uv(float x, float y) => new(x / diameter + 0.5f, y / diameter + 0.5f);

        var cs = new float[seg];
        var sn = new float[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            cs[i] = Mathf.Cos(a);
            sn[i] = Mathf.Sin(a);
        }

        // A radial BAND: `seg` quads from radius ra at depth za out to radius rb at zb, with a hard
        // normal whose radial component is nR and whose viewer-ward component is nZ. Same argument
        // as the square cap's AddBand for passing the normal instead of deriving it.
        void AddBand(System.Collections.Generic.List<int> sm,
                     float ra, float za, float rb, float zb, float nR, float nZ)
        {
            float len = Mathf.Sqrt(nR * nR + nZ * nZ);
            if (len <= 1e-6f)
                return;
            float nr = nR / len, nz = nZ / len;
            int b0 = verts.Count;
            for (int i = 0; i < seg; i++)
            {
                verts.Add(new Vector3(cs[i] * ra, sn[i] * ra, za));
                norms.Add(new Vector3(cs[i] * nr, sn[i] * nr, nz));
                uvs.Add(Uv(cs[i] * ra, sn[i] * ra));
                verts.Add(new Vector3(cs[i] * rb, sn[i] * rb, zb));
                norms.Add(new Vector3(cs[i] * nr, sn[i] * nr, nz));
                uvs.Add(Uv(cs[i] * rb, sn[i] * rb));
            }
            for (int i = 0; i < seg; i++)
            {
                int next = (i + 1) % seg;
                int a0 = b0 + i * 2, b1 = b0 + i * 2 + 1;
                int c0 = b0 + next * 2, d1 = b0 + next * 2 + 1;
                // WINDING IS CHOSEN FROM THE BAND'S OWN NORMAL, exactly as the square cap's AddQuad
                // chooses it — and this line is a FIX, not a tidy-up.
                //
                // It used to be the fixed order (a, c, b / c, d, b), with the comment "taken from
                // BuildRoundCap's side wall, which is the one this project has already proved
                // outward-facing on hardware". That is true of the WALL and false of everything
                // else. The wall is the one band with no radial step (ra == rb), and for it the
                // right-hand normal of (a, c, b) does come out radially outward. On a band that
                // steps inward — the outer chamfer, the RIM LAND and the inner chamfer, i.e. the
                // entire bezel — the same order gives a right-hand normal of +Z, pointing AWAY from
                // the viewer, against a shading normal that points toward them.
                //
                // Every cap material is `new Material(BoardLit)` and keeps the shader's default
                // _Cull = Back, so all three bezel bands were being back-face culled. Measured:
                // 384 of the round cap's 640 triangles wound against their own normals (exactly
                // 64 segments x 6), and the station's own picture of an Oak rest cap
                // (.planning/debug/round4/station/Oak_ShortRest_after_rake.png) shows the recessed
                // field, a crescent of the far WALL and NO BEZEL RING AT ALL — while the square
                // cap beside it shows its full bright frame. The round rest pads on all three
                // boards have had no bezel since round 2 gave the disc the signet profile.
                //
                // This is the winding bug class this repository has already shipped seven times.
                // Deriving the order instead of asserting it is what the square cap does, and it is
                // why the square cap does not have this bug.
                Vector3 pa = verts[a0], pc = verts[c0], pb = verts[b1];
                var want = new Vector3(cs[i] * nr, sn[i] * nr, nz);
                if (Vector3.Dot(Vector3.Cross(pc - pa, pb - pa), want) > 0f)
                {
                    sm.Add(a0); sm.Add(c0); sm.Add(b1);
                    sm.Add(c0); sm.Add(d1); sm.Add(b1);
                }
                else
                {
                    sm.Add(a0); sm.Add(b1); sm.Add(c0);
                    sm.Add(c0); sm.Add(b1); sm.Add(d1);
                }
            }
        }

        // An arbitrary quad with a stated normal, wound so it is visible from its +n side. Same
        // right-hand rule and the same argument as the square cap's AddQuad; the round builder had
        // no need of one until the dentil blocks, which are neither bands nor fans.
        void AddQuadN(System.Collections.Generic.List<int> sm,
                      Vector3 a, Vector3 b, Vector3 c2, Vector3 d2, Vector3 n)
        {
            n = n.normalized;
            int b0 = verts.Count;
            foreach (Vector3 p in new[] { a, b, c2, d2 })
            {
                verts.Add(p); norms.Add(n); uvs.Add(Uv(p.x, p.y));
            }
            if (Vector3.Dot(Vector3.Cross(b - a, c2 - a), n) > 0f)
            {
                sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2);
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 3);
            }
            else
            {
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1);
                sm.Add(b0); sm.Add(b0 + 3); sm.Add(b0 + 2);
            }
        }

        // A flat disc FAN at radius r, depth z, facing `front` (−Z) or the back (+Z).
        void AddFan(System.Collections.Generic.List<int> sm, float r, float z, bool front)
        {
            int b0 = verts.Count;
            for (int i = 0; i < seg; i++)
            {
                verts.Add(new Vector3(cs[i] * r, sn[i] * r, z));
                norms.Add(front ? Vector3.back : Vector3.forward);
                uvs.Add(Uv(cs[i] * r, sn[i] * r));
            }
            int centre = verts.Count;
            verts.Add(new Vector3(0f, 0f, z));
            norms.Add(front ? Vector3.back : Vector3.forward);
            uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < seg; i++)
            {
                int next = (i + 1) % seg;
                if (front) { sm.Add(centre); sm.Add(b0 + next); sm.Add(b0 + i); }
                else { sm.Add(centre); sm.Add(b0 + i); sm.Add(b0 + next); }
            }
        }

        AddFan(top, r3, zField, front: true);                      // [0] the recessed field

        // [1] THE OUTER ZONE. A disc has no corner to cut, so the round cap carries its board's
        // construction in the two cues that DO survive being circular: the stepped TERRACE (every
        // board's chosen round option is a terrace — OA-R2, ST-R2, BR-R2) and the HARDWARE. That
        // is what makes the two mandatory shapes read as one set rather than as two assets.
        if (con.Terrace)
        {
            float zMid = Mathf.Lerp(zWallTop, zFront, 0.55f);
            AddBand(ring, rOut, zWallTop, rOut - 0.18f * c, zMid, 1f, -1f);
            AddBand(ring, rOut - 0.18f * c, zMid, rOut - 0.58f * c, zMid, 0f, -1f);
            AddBand(ring, rOut - 0.58f * c, zMid, rOut - 0.76f * c, zFront, 1f, -1f);
            AddBand(ring, rOut - 0.76f * c, zFront, r1, zFront, 0f, -1f);
        }
        else
        {
            AddBand(ring, rOut, zWallTop, r1, zFront, 1f, -1f);    //     outer chamfer
        }

        AddBand(ring, r1, zFront, r2, zFront, 0f, -1f);            //     rim land
        AddBand(ring, r2, zFront, r3, zField, -1f, -1f);           //     inner chamfer
        // [2] THE SIDE WALL, with the same UNDERCUT SKIRT the square cap gets — see
        // CapFaceLayout.CapConstruction.UndercutFrac for why the wall is where the area is. The
        // crown keeps the full diameter; only the base steps in.
        float ucR = con.UndercutFrac * diameter;
        if (ucR > 0f)
        {
            float zSh = Mathf.Lerp(zWallTop, zBack, 0.38f);
            AddBand(walls, rOut, zWallTop, rOut, zSh, 1f, 0f);         // crown wall
            AddBand(walls, rOut, zSh, rOut - ucR, zSh, 0f, 1f);        // the shoulder, facing BACK
            AddBand(walls, rOut - ucR, zSh, rOut - ucR, zBack, 1f, 0f); // skirt
            AddFan(walls, rOut - ucR, zBack, front: false);            //     hidden back
        }
        else
        {
            AddBand(walls, rOut, zWallTop, rOut, zBack, 1f, 0f);
            AddFan(walls, rOut, zBack, front: false);
        }

        // THE HARDWARE, on the rim land, at the same four DIAGONAL positions the square cap puts
        // its studs — so a player looking at a board's round cap and its square cap sees the rivets
        // in the same places on both.
        if (con.Stud == CapFaceLayout.CapStud.Dome && con.StudFrac > 0f)
        {
            float rs = Mathf.Min(con.StudFrac * diameter, rim * 0.95f);
            float seatR = (r1 + r2) * 0.5f;
            for (int q = 0; q < 4; q++)
            {
                float a = Mathf.PI * 0.25f + q * Mathf.PI * 0.5f;
                AddDomeHead(verts, norms, uvs, ring, p => Uv(p.x, p.y),
                            seatR * Mathf.Cos(a), seatR * Mathf.Sin(a), zFront, rs, rs * 0.55f);
            }
        }

        // THE DENTIL RING on a disc: the rim-land annulus CRENELLATED, alternate sectors standing
        // proud. The square cap's blocks are boxes on four straight runs; a circle has no straight
        // run, so the same border becomes raised sectors of the annulus — the same count of blocks
        // around the same band, which is what the oak board's own border does where it turns a
        // corner.
        if (con.Dentils > 0 && con.DentilRise > 0f)
        {
            float rise = con.DentilRise * diameter;
            int m = con.Dentils * 4;
            float zBlk = zFront - rise;
            for (int i = 0; i < m; i++)
            {
                float a0 = (i + 0.18f) * 2f * Mathf.PI / m;
                float a1 = (i + 0.82f) * 2f * Mathf.PI / m;   // 64 % block, 36 % gap
                const int SubSeg = 3;
                Vector3 In(float a, float z) => new(r2 * Mathf.Cos(a), r2 * Mathf.Sin(a), z);
                Vector3 Ou(float a, float z) => new(r1 * Mathf.Cos(a), r1 * Mathf.Sin(a), z);
                for (int s = 0; s < SubSeg; s++)
                {
                    float b0 = Mathf.Lerp(a0, a1, s / (float)SubSeg);
                    float b1 = Mathf.Lerp(a0, a1, (s + 1) / (float)SubSeg);
                    AddQuadN(ring, Ou(b0, zBlk), Ou(b1, zBlk), In(b1, zBlk), In(b0, zBlk),
                             Vector3.back);                                        // block face
                    AddQuadN(ring, Ou(b0, zFront), Ou(b1, zFront), Ou(b1, zBlk), Ou(b0, zBlk),
                             new Vector3(Mathf.Cos((b0 + b1) * 0.5f), Mathf.Sin((b0 + b1) * 0.5f), 0f));
                    AddQuadN(ring, In(b0, zFront), In(b1, zFront), In(b1, zBlk), In(b0, zBlk),
                             new Vector3(-Mathf.Cos((b0 + b1) * 0.5f), -Mathf.Sin((b0 + b1) * 0.5f), 0f));
                }
                // The two radial ENDS of the block.
                foreach ((float a, float sgn) in new[] { (a0, -1f), (a1, 1f) })
                {
                    var tang = new Vector3(-Mathf.Sin(a) * sgn, Mathf.Cos(a) * sgn, 0f);
                    AddQuadN(ring, Ou(a, zFront), In(a, zFront), In(a, zBlk), Ou(a, zBlk), tang);
                }
            }
        }

        var mesh = new Mesh { name = "GloomhavenVR.SignetRoundKeycap" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTangents(new System.Collections.Generic.List<Vector4>(
            System.Linq.Enumerable.Repeat(KeycapTangent, verts.Count)));
        mesh.subMeshCount = 3;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(ring, 1);
        mesh.SetTriangles(walls, 2);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static readonly System.Collections.Generic.Dictionary<(int, int, int, int), Mesh> _roundKeycapCache = new();

    /// <summary>Cached <see cref="BuildRoundKeycap"/>, keyed exactly as <see cref="GetRoundCap"/> is
    /// and for the same reason: every rest disc on a board is the same size.
    ///
    /// <para><b>THE BOARD IS PART OF THE KEY, and leaving it out would have been a silent
    /// cross-board bug rather than a slow cache.</b> Since ModBuild 290 the mesh depends on the
    /// board (<see cref="CapFaceLayout.ConstructionFor"/>), and all three boards fit their rest
    /// discs to very nearly the same diameter — so a size-only key would have handed the second
    /// board whichever board happened to build first, on a mesh that is shared and never rebuilt.
    /// In multiplayer that is a peer's mirror wearing the wrong board's rivets with nothing in the
    /// build, the mirrors gate or the wire suite able to see it.</para></summary>
    internal static Mesh GetRoundKeycap(float diameter, float thickness, ControlBoard? style = null,
                                        int segments = RoundCapSegments)
    {
        segments = Mathf.Clamp(segments, 12, 128);
        var key = (Mathf.RoundToInt(diameter * 10000f), Mathf.RoundToInt(thickness * 10000f),
                   segments, style is ControlBoard b ? (int)b : -1);
        if (_roundKeycapCache.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;
        Mesh built = BuildRoundKeycap(diameter, thickness, segments,
                                      CapFaceLayout.ConstructionFor(style is ControlBoard s ? (int)s : null));
        _roundKeycapCache[key] = built;
        return built;
    }

    /// <summary>The square cap in a board's own construction. The board is the only thing that
    /// selects it — see <see cref="CapFaceLayout.CapConstruction"/> for why this is derived from
    /// <see cref="ControlBoard"/> rather than synced as a tuning field.</summary>
    internal static Mesh BuildBeveledKeycap(float width, float height, float thickness,
                                            ControlBoard? style)
        => BuildBeveledKeycap(width, height, thickness,
                              CapFaceLayout.ConstructionFor(style is ControlBoard s ? (int)s : null));

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
        // The round disc shares the square cap's UV convention exactly (u = x/diameter + 0.5,
        // v = y/diameter + 0.5), so it shares its tangent — see KeycapTangent for what was
        // undefined before this line and why one constant is the right answer for both meshes.
        // The disc's own degenerate case is its RIM, where u and v are constant along z.
        mesh.SetTangents(new System.Collections.Generic.List<Vector4>(
            System.Linq.Enumerable.Repeat(KeycapTangent, verts.Count)));
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
    /// visible edge in the same warm-wood family as the board's keycaps — it was picked against
    /// the recess seat liner's (0.46,0.37,0.26), luma 0.38, which has since been deleted with the
    /// liner itself (see PlayTray.4.Slots' record of that deletion), so the number below is now
    /// the only survivor of that pair — in the hand it reads as a warm card edge. Reaches all
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
