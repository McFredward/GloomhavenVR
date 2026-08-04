using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Mixed-reality readability backings (user ruling): while MR/see-through is ON, every
/// mod-drawn text or converted panel that floats WITHOUT an opaque backing gets one, because
/// the chroma-keyed pixels AROUND thin content are replaced by the live passthrough room —
/// glyphs survive the keying (their pixels no longer match the key color) but end up floating
/// over the real room's clutter, unreadable. With MR OFF nothing here renders at all: the
/// regression bar is bit-identical rendering, so plates are DEACTIVATED (mod-owned objects,
/// never a game material touched) and every opacified alpha is restored to its recorded value.
///
/// One central owner, three one-liner registration surfaces:
/// - <see cref="Label"/> — a fitted dark plate behind a free-floating world TMP label. Sizing
///   rides the <see cref="TmpFit"/> contract (rect.sizeDelta is the label's box in LOCAL
///   METERS), refreshed every tick so text/box changes track live.
/// - <see cref="Opacify(Graphic)"/> / <see cref="Opacify(Material)"/> — a MOD-OWNED translucent
///   backing (wrist-HUD backdrop, pick-banner parchment, remote plates) is driven to alpha 1
///   while MR is on and restored exactly on off. Only mod-owned objects are ever registered.
/// - Converted panels need no registration: <see cref="Tick"/> sweeps
///   <see cref="CanvasConversion.ActivePanels"/> and keeps a host-rect plate behind EVERY live
///   converted panel. That deliberately includes the ModalFallback float families whose native
///   full-window backing the mod itself strips (WantsTransparentBackground — correct on a flat
///   screen, unreadable in MR) and the adopted HUD panels whose game art may be translucent;
///   behind a natively opaque panel the plate is simply invisible (drawn first, fully covered),
///   so over-coverage is harmless while under-coverage is the reported bug. The ONLY exceptions
///   are panels whose owner set <see cref="ConvertedPanel.MrBackingSuppressed"/> (user ruling
///   2026-08-04: actor health bars and the figure-grab stat cards — see that flag's doc for the
///   full reasoning); the sweep never plates those and destroys any plate they already carry.
///
/// PLATE RENDERING: the bundled Overlay shader forced to _ZWrite=1 / _ZTest=4 (LEqual) /
/// _Cull=0 / Blend One Zero at renderQueue 2998 — after all opaque geometry and before the
/// text/uGUI it backs (~3000). (It used to share this recipe with the panel depth masks at 2999;
/// those are gone — see CanvasConversion.8.Order.cs — but the plate is a genuinely OPAQUE backing
/// the user asked for, not an invisible stamp, so its depth write is the honest kind: it hides
/// what is behind it because it is a solid surface, not because a rectangle said so.) Writing depth means anything BEHIND the plate is occluded the
/// normal way (no transparent-sort gambling), while the content 2 mm in front still passes
/// LEqual. Without the bundle the material falls back to Sprites/Default at the same queue:
/// no depth write, but the plate still draws before its content — readable either way.
///
/// PLATE SORTING ORDER — the plate RIDES ITS PANEL'S LADDER SLOT (user report 2026-08-05, MR:
/// "menus that are further back shine THROUGH nearer ones"). Unity resolves transparent
/// renderers by sortingLayer → sortingOrder FIRST and only then by material renderQueue, and
/// every converted panel composites on the per-frame distance ladder
/// (CanvasConversion.8.Order.cs: base 100, step 16, nearer = higher order = painted later; NO
/// panel writes depth). A plate left at sortingOrder 0 therefore painted before EVERY panel
/// canvas — so a FARTHER panel's rows painted over a NEARER panel's dark backing, exactly the
/// bleed-through in .planning/debug/keine_ausblendung.png (top right). Each panel plate now
/// registers as an ORDER FOLLOWER of its own panel at OFFSET 0: same sortingOrder as the
/// panel's content, where the plate's EARLIER renderQueue (2998 vs the content's ~3000) is the
/// tie-break that keeps it just under its own content — the intra-panel contract the queue
/// split has always expressed — while the slot itself puts it ABOVE every farther panel's
/// content AND above the board-furniture band (whose top is slot−1; offset −1 would tie with
/// it, offset 0 cannot). Label plates get the same treatment by copying their label renderer's
/// LIVE sortingOrder each tick (identity tags ride the ladder via
/// CanvasConversion.OrderAboveDistance — see Net/BoardVisual.OrderWithPanels — so their plates
/// must follow; a plain order-0 caption keeps a plain order-0 plate, bit-identical to before).
///
/// KEY-COLOR SAFETY: the plate must NEVER render the chroma key (it would punch a passthrough
/// hole exactly where readability was wanted). Presets are green/magenta/blue/black — the
/// saturated keys are nowhere near the dark panel neutral, but the BLACK preset is close to
/// it, so when the live key color comes within keying distance of the plate color the plate
/// is lifted to a brighter warm gray instead. Re-checked every tick (the key is live-cycled
/// from the settings panel).
/// </summary>
internal static class MrBacking
{
    /// <summary>World gap between a plate and the content it backs (meters). Big enough to
    /// clear z-fighting at HMD depth precision, small enough to read as one surface.</summary>
    private const float PlateGapMeters = 0.002f;

    /// <summary>Label plates out-pad the TmpFit box so descenders/edges sit on plate, not sky:
    /// fraction of the box per axis plus an absolute floor for the tiny caption boxes.</summary>
    private const float LabelPadFraction = 0.12f;
    private const float LabelPadFloorMeters = 0.004f;

    /// <summary>After opaque geometry, before the text/uGUI it backs (3000). Since the plate now
    /// SHARES its content's sortingOrder (panel plates ride their panel's ladder slot at follower
    /// offset 0, label plates copy their label renderer's live order — see the class doc), this
    /// queue is the tie-break that draws the plate first WITHIN that shared slot: "the plate
    /// composites before the content it backs", the same contract sortingOrder 0 used to express
    /// before the ladder existed.</summary>
    private const int PlateQueue = 2998;

    /// <summary>The repo's dark panel neutral (RoundReadout/slot plates use the same family).</summary>
    private static readonly Color PlateDark = new(0.12f, 0.11f, 0.10f, 1f);

    /// <summary>Key-avoidance lift: still a readable dark-warm backing, but every channel is
    /// ≥0.25 away from a black key so no compositor threshold can key the plate away.</summary>
    private static readonly Color PlateLift = new(0.34f, 0.30f, 0.25f, 1f);

    /// <summary>Channel distance below which the plate counts as key-colored (matches the
    /// generous end of Virtual Desktop's similarity slider, deliberately conservative).</summary>
    private const float KeyDistance = 0.25f;

    private sealed class LabelEntry
    {
        public TMP_Text Label = null!;
        public Transform? Plate;

        /// <summary>The plate's own MeshRenderer (cached at creation — sortingOrder sync target).</summary>
        public Renderer? PlateRenderer;

        /// <summary>The LABEL's renderer, probed once: a world TMP label draws through a
        /// MeshRenderer on its own GameObject, and call sites that rank against the panel ladder
        /// (Net/BoardVisual.OrderWithPanels) write their live order onto exactly that renderer —
        /// the plate copies it each tick. Null for a label without one (then the plate keeps
        /// order 0, the pre-ladder behaviour).</summary>
        public Renderer? LabelRenderer;
        public bool LabelRendererProbed;
    }

    private sealed class PanelEntry
    {
        public ConvertedPanel Panel = null!;
        public Transform? Plate;
    }

    private sealed class GraphicEntry
    {
        public Graphic Graphic = null!;
        public float OriginalAlpha;
        public bool Applied;
    }

    private sealed class MaterialEntry
    {
        public Material Material = null!;
        public float OriginalAlpha;
        public bool Applied;
    }

    private static readonly List<LabelEntry> Labels = new(24);
    private static readonly List<PanelEntry> Panels = new(8);
    private static readonly List<GraphicEntry> Graphics = new(8);
    private static readonly List<MaterialEntry> Materials = new(8);

    private static Material? _plateMat;
    private static Color _plateColor;
    private static bool _applied;      // MR backings currently on (transition edge for restore)
    private static bool _loggedOn;     // change-dedup for the on/off log

    /// <summary>
    /// Poll for call sites with their own per-frame alpha math (EmptyFanHint): true while the
    /// MR treatment wants backings opaque. Reading this instead of registering keeps such a
    /// site's existing fade authority intact — it restores itself the frame MR turns off.
    /// </summary>
    internal static bool WantOpaque => MixedReality.BackingsWanted;

    /// <summary>Register a free-floating world TMP label for a fitted backing plate (idempotent).</summary>
    internal static void Label(TMP_Text? label)
    {
        if (label == null)
            return;
        for (int i = 0; i < Labels.Count; i++)
        {
            if (ReferenceEquals(Labels[i].Label, label))
                return;
        }
        Labels.Add(new LabelEntry { Label = label });
    }

    /// <summary>Register a MOD-OWNED translucent backing Graphic: alpha 1 while MR, restored off.</summary>
    internal static void Opacify(Graphic? backing)
    {
        if (backing == null)
            return;
        for (int i = 0; i < Graphics.Count; i++)
        {
            if (ReferenceEquals(Graphics[i].Graphic, backing))
                return;
        }
        Graphics.Add(new GraphicEntry { Graphic = backing, OriginalAlpha = backing.color.a });
    }

    /// <summary>Register a MOD-OWNED translucent plate material: alpha 1 while MR, restored off.</summary>
    internal static void Opacify(Material? backing)
    {
        if (backing == null)
            return;
        for (int i = 0; i < Materials.Count; i++)
        {
            if (ReferenceEquals(Materials[i].Material, backing))
                return;
        }
        Materials.Add(new MaterialEntry { Material = backing, OriginalAlpha = backing.color.a });
    }

    /// <summary>
    /// Per-frame driver (WorldUIModule, after CanvasConversion.Tick so host rects are read
    /// post-fit). ON: ensure/refit every plate and drive registered alphas to 1. OFF: one
    /// transition pass deactivates all plates and writes the recorded alphas back — after
    /// that this is a single bool check per frame (the no-effect-when-off regression bar).
    /// </summary>
    internal static void Tick()
    {
        bool want = MixedReality.BackingsWanted;
        if (!want)
        {
            if (_applied)
                RestoreAll();
            return;
        }

        EnsurePlateMaterial();
        TickLabels();
        TickPanels();
        TickAlphas();
        _applied = true;
        if (!_loggedOn)
        {
            _loggedOn = true;
            VRLog.Info("WorldUI", $"MR backings ON — {Labels.Count} label plate(s), " +
                                  $"{Panels.Count} panel plate(s), {Graphics.Count + Materials.Count} " +
                                  $"opacified backing(s); plate color RGBA {_plateColor.r:0.##}," +
                                  $"{_plateColor.g:0.##},{_plateColor.b:0.##},1 (key-color-safe).");
        }
    }

    /// <summary>Hot-reload / module teardown: destroy every plate, restore every alpha.</summary>
    internal static void Shutdown()
    {
        RestoreAll();
        for (int i = 0; i < Labels.Count; i++)
        {
            if (Labels[i].Plate != null)
                Object.Destroy(Labels[i].Plate!.gameObject);
        }
        for (int i = 0; i < Panels.Count; i++)
        {
            if (Panels[i].Plate != null)
                Object.Destroy(Panels[i].Plate!.gameObject);
        }
        Labels.Clear();
        Panels.Clear();
        Graphics.Clear();
        Materials.Clear();
        if (_plateMat != null)
        {
            Object.Destroy(_plateMat);
            _plateMat = null;
        }
    }

    // ---- per-tick application -----------------------------------------------------------------

    private static void TickLabels()
    {
        for (int i = Labels.Count - 1; i >= 0; i--)
        {
            LabelEntry e = Labels[i];
            if (e.Label == null) // Unity fake-null: label destroyed (its plate died with it)
            {
                Labels.RemoveAt(i);
                continue;
            }
            RectTransform rect = e.Label.rectTransform;
            Vector2 size = rect.sizeDelta; // TmpFit contract: the label box in local meters
            bool visible = e.Label.isActiveAndEnabled && size.x > 0.001f && size.y > 0.001f
                           && !string.IsNullOrEmpty(e.Label.text);
            if (e.Plate == null)
            {
                if (!visible)
                    continue; // nothing to back yet — create lazily on first visible tick
                e.Plate = CreatePlate(rect);
                e.PlateRenderer = e.Plate.GetComponent<MeshRenderer>();
            }
            if (e.Plate.gameObject.activeSelf != visible)
                e.Plate.gameObject.SetActive(visible);
            if (!visible)
                continue;

            // Perspective (class doc, PLATE SORTING ORDER): the plate copies its label renderer's
            // LIVE sortingOrder every tick. Labels ranked against the converted-panel ladder
            // (identity tags via CanvasConversion.OrderAboveDistance) drag their plate with them
            // — same slot, earlier queue draws the plate just under the glyphs and above every
            // panel genuinely farther. An unranked order-0 label keeps an order-0 plate.
            if (!e.LabelRendererProbed)
            {
                e.LabelRendererProbed = true;
                e.LabelRenderer = e.Label.GetComponent<Renderer>();
            }
            if (e.LabelRenderer != null && e.PlateRenderer != null
                && e.PlateRenderer.sortingOrder != e.LabelRenderer.sortingOrder)
            {
                e.PlateRenderer.sortingOrder = e.LabelRenderer.sortingOrder;
            }

            // Hug the RENDERED glyph bounds, clamped to the layout box: the TmpFit box is a
            // tight fit already, but some labels sit in a much larger layout rect (the starting
            // indicator's 3×1 m box) where a rect-sized plate would be an absurd slab. Fall back
            // to the box only before the first mesh update (textBounds still zero). The bounds
            // center also places the plate correctly for non-centered alignments (name tags are
            // left-aligned beside their avatar).
            Bounds tb = e.Label.textBounds;
            bool glyphs = tb.size.x > 0.0005f && tb.size.y > 0.0005f;
            Vector2 fit = glyphs ? Vector2.Min(size, (Vector2)tb.size) : size;
            float padX = fit.x * LabelPadFraction + LabelPadFloorMeters;
            float padY = fit.y * LabelPadFraction + LabelPadFloorMeters;
            Vector2 center = glyphs ? (Vector2)tb.center : rect.rect.center;
            Fit(e.Plate, rect, new Vector2(fit.x + padX, fit.y + padY), center);
        }
    }

    private static void TickPanels()
    {
        // Prune entries whose panel released (the host — and the plate under it — is destroyed
        // by the release; ActivePanels no longer lists it) OR opted out of the plate
        // (ConvertedPanel.MrBackingSuppressed, user ruling 2026-08-04: actor bars and the
        // figure-grab stat cards). The flag is normally set at Convert time — before this sweep
        // ever sees the panel — but a live panel that acquires it later still has its existing
        // plate destroyed here, so the opt-out can never race the sweep.
        IReadOnlyList<ConvertedPanel> active = CanvasConversion.ActivePanels;
        for (int i = Panels.Count - 1; i >= 0; i--)
        {
            ConvertedPanel p = Panels[i].Panel;
            bool alive = false;
            for (int j = 0; j < active.Count; j++)
            {
                if (ReferenceEquals(active[j], p))
                {
                    alive = true;
                    break;
                }
            }
            if (!alive || p.MrBackingSuppressed)
            {
                // A released panel's plate died with its host (Unity-null here); a suppressed
                // live panel's plate is mod-owned and must go explicitly.
                if (Panels[i].Plate != null)
                    Object.Destroy(Panels[i].Plate!.gameObject);
                Panels.RemoveAt(i);
            }
        }

        for (int i = 0; i < active.Count; i++)
        {
            ConvertedPanel panel = active[i];
            RectTransform? host = panel.HostRect;
            if (host == null || panel.HostGo == null || panel.MrBackingSuppressed)
                continue;

            PanelEntry? entry = null;
            for (int j = 0; j < Panels.Count; j++)
            {
                if (ReferenceEquals(Panels[j].Panel, panel))
                {
                    entry = Panels[j];
                    break;
                }
            }
            if (entry == null)
            {
                entry = new PanelEntry { Panel = panel };
                Panels.Add(entry);
            }

            Rect r = host.rect;
            // User ruling 2026-08-02 round 2: never plate a panel that is still render-hidden
            // behind the reveal gate. This plate is OPAQUE and exactly host-rect sized, so on a
            // freshly floated window it would pop in as a solid dark rectangle at the PRE-FIT rect
            // — a window-shaped block in the wrong place for the whole settle window. The panel's
            // render hide would switch the plate's renderer off in LateUpdate anyway; refusing it
            // here means the plate is not even built at the wrong pose. (This Tick runs AFTER
            // CanvasConversion.Tick, so RenderHidden is this frame's settled value.)
            bool visible = panel.HostGo.activeInHierarchy && !panel.RenderHidden
                           && r.width > 2f && r.height > 2f;
            if (entry.Plate == null)
            {
                if (!visible)
                    continue;
                entry.Plate = CreatePlate(host);
                // Perspective (class doc, PLATE SORTING ORDER): ride the panel's own ladder slot.
                // Offset 0 = the content's sortingOrder, where the plate's earlier renderQueue
                // (2998 vs ~3000) draws it just UNDER its own content — and the slot itself puts
                // it ABOVE every farther panel's content, so a menu behind this one can no longer
                // paint through this plate. Re-stamped by ApplyPanelOrder on every ladder resort;
                // the follower entry self-prunes when the plate is destroyed.
                CanvasConversion.RegisterOrderFollower(
                    panel, entry.Plate.GetComponent<MeshRenderer>(), 0);
            }
            if (entry.Plate.gameObject.activeSelf != visible)
                entry.Plate.gameObject.SetActive(visible);
            if (!visible)
                continue;

            // Exactly the host rect (host units are canvas px; the host scale carries px→m):
            // no out-padding — a plate proud of the window edge would read as a frame the
            // window never had.
            Fit(entry.Plate, host, r.size, r.center);
        }
    }

    private static void TickAlphas()
    {
        for (int i = Graphics.Count - 1; i >= 0; i--)
        {
            GraphicEntry e = Graphics[i];
            if (e.Graphic == null)
            {
                Graphics.RemoveAt(i);
                continue;
            }
            if (!e.Applied)
            {
                e.OriginalAlpha = e.Graphic.color.a; // re-record at the flip (owner may have refaded)
                e.Applied = true;
            }
            // ALPHA ONLY — the RGB stays whatever the owner last wrote (owners retint live,
            // e.g. player/enemy chip tints); MR claims only the translucency.
            Color c = e.Graphic.color;
            if (c.a < 1f)
            {
                c.a = 1f;
                e.Graphic.color = c;
            }
        }
        for (int i = Materials.Count - 1; i >= 0; i--)
        {
            MaterialEntry e = Materials[i];
            if (e.Material == null)
            {
                Materials.RemoveAt(i);
                continue;
            }
            if (!e.Applied)
            {
                e.OriginalAlpha = e.Material.color.a;
                e.Applied = true;
            }
            Color c = e.Material.color;
            if (c.a < 1f)
            {
                c.a = 1f;
                e.Material.color = c;
            }
        }
    }

    /// <summary>OFF transition: plates deactivate (mod-owned; zero rendering), alphas restore.</summary>
    private static void RestoreAll()
    {
        for (int i = Labels.Count - 1; i >= 0; i--)
        {
            if (Labels[i].Label == null)
            {
                Labels.RemoveAt(i);
                continue;
            }
            if (Labels[i].Plate != null)
                Labels[i].Plate!.gameObject.SetActive(false);
        }
        for (int i = Panels.Count - 1; i >= 0; i--)
        {
            if (Panels[i].Plate != null)
                Panels[i].Plate!.gameObject.SetActive(false);
        }
        for (int i = Graphics.Count - 1; i >= 0; i--)
        {
            GraphicEntry e = Graphics[i];
            if (e.Graphic == null)
            {
                Graphics.RemoveAt(i);
                continue;
            }
            if (e.Applied)
            {
                Color c = e.Graphic.color; // alpha-only restore: keep the owner's live RGB
                c.a = e.OriginalAlpha;
                e.Graphic.color = c;
                e.Applied = false;
            }
        }
        for (int i = Materials.Count - 1; i >= 0; i--)
        {
            MaterialEntry e = Materials[i];
            if (e.Material == null)
            {
                Materials.RemoveAt(i);
                continue;
            }
            if (e.Applied)
            {
                Color c = e.Material.color;
                c.a = e.OriginalAlpha;
                e.Material.color = c;
                e.Applied = false;
            }
        }
        bool wasApplied = _applied;
        _applied = false;
        if (wasApplied && _loggedOn)
        {
            _loggedOn = false;
            VRLog.Info("WorldUI", "MR backings OFF — every plate deactivated, every opacified " +
                                  "backing alpha restored to its recorded value (bit-identical).");
        }
    }

    // ---- plate plumbing -----------------------------------------------------------------------

    private static void Fit(Transform plate, RectTransform anchor, Vector2 size, Vector2 center)
    {
        // The parent may be re-layered AFTER registration (VRLayers.Apply runs post-build,
        // CanvasConversion re-layers hosts) — sync every tick so the plate always renders
        // exactly where its content renders.
        int layer = anchor.gameObject.layer;
        if (plate.gameObject.layer != layer)
            plate.gameObject.layer = layer;

        // REAL gap → anchor-local units (the anchor's Z scale carries local→world).
        //
        // ROUND 6 (one-eye flicker hunt): the gap is specified in REAL metres, so it must be
        // converted to WORLD units before the local conversion. Inside a scenario the diorama runs
        // at ~48 game units per real metre, so the old code — which treated the constant as world
        // units — seated the plate 2 mm / 48 ≈ 0.04 mm behind the content it backs. At HMD depth
        // precision that is co-planar, and co-planar surfaces in this project are a KNOWN per-eye
        // artifact (INVARIANTS-WorldUI: the round-token label z-fought "per-eye under stereo" at
        // sub-millimetre separation) — an opaque plate doing that behind a menu is a flicker at the
        // panel edges, where the grazing angle makes the depth difference smallest. Outside a
        // scenario WorldScale is 1 and this is bit-identical to the shipped behaviour.
        float lossyZ = Mathf.Abs(anchor.lossyScale.z);
        float zLocal = PlateGapMeters * PanelLayout.WorldScale / Mathf.Max(lossyZ, 1e-5f);
        plate.localPosition = new Vector3(center.x, center.y, zLocal);
        plate.localRotation = Quaternion.identity;
        var scale = new Vector3(size.x, size.y, 1f);
        if (plate.localScale != scale)
            plate.localScale = scale;
    }

    private static Transform CreatePlate(Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "GloomhavenVR.MrBacking";
        Object.Destroy(go.GetComponent<Collider>()); // never a poke/laser target
        go.transform.SetParent(parent, worldPositionStays: false);
        go.layer = parent.gameObject.layer;
        var mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = _plateMat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        // sortingOrder starts at 0 but does NOT stay there: the callers slave it to the content
        // the plate backs (panel plates ride their panel's ladder slot as offset-0 order
        // followers, label plates copy their label renderer's live order each tick) — see the
        // class doc, PLATE SORTING ORDER. Within the shared slot the plate's earlier renderQueue
        // (2998 vs the content's ~3000) still draws it first.
        return go.transform;
    }

    /// <summary>
    /// Shared plate material: the GrabbableModal depth-mask render state with a real color
    /// (Blend One Zero instead of Zero One). Re-tinted live when the key color moves into
    /// keying distance of the plate — the plate must never be keyed out (see class doc).
    /// </summary>
    private static void EnsurePlateMaterial()
    {
        Color key = MixedReality.KeyColor.Value;
        bool nearKey = Mathf.Abs(key.r - PlateDark.r) < KeyDistance
                       && Mathf.Abs(key.g - PlateDark.g) < KeyDistance
                       && Mathf.Abs(key.b - PlateDark.b) < KeyDistance;
        Color wanted = nearKey ? PlateLift : PlateDark;

        if (_plateMat == null)
        {
            _plateMat = WorldUIAssets.CreateFlatMaterial(wanted, overlay: true);
            if (_plateMat.HasProperty("_ZWrite")) _plateMat.SetInt("_ZWrite", 1);   // occlude what's behind
            if (_plateMat.HasProperty("_ZTest")) _plateMat.SetInt("_ZTest", 4);     // LEqual — hands/board still win
            if (_plateMat.HasProperty("_Cull")) _plateMat.SetInt("_Cull", 0);       // readable back side too
            if (_plateMat.HasProperty("_SrcBlend")) _plateMat.SetInt("_SrcBlend", 1); // One  ┐ colour = src
            if (_plateMat.HasProperty("_DstBlend")) _plateMat.SetInt("_DstBlend", 0); // Zero ┘ (opaque)
            _plateMat.renderQueue = PlateQueue;
            _plateColor = wanted;
            return;
        }
        if (_plateColor != wanted)
        {
            _plateColor = wanted;
            _plateMat.color = wanted;
            VRLog.Info("WorldUI", $"MR backings: key color moved near the plate neutral — plate " +
                                  $"re-tinted to RGBA {wanted.r:0.##},{wanted.g:0.##},{wanted.b:0.##},1 " +
                                  "so it can never be chroma-keyed away.");
        }
    }
}
