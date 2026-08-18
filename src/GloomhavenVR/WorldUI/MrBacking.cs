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
/// One central owner, four one-liner registration surfaces:
/// - <see cref="Label"/> — a fitted dark plate behind a free-floating world TMP label. Sizing
///   rides the <see cref="TmpFit"/> contract (rect.sizeDelta is the label's box in LOCAL
///   METERS), refreshed every tick so text/box changes track live.
/// - <see cref="Opacify(Graphic)"/> / <see cref="Opacify(Material)"/> — a MOD-OWNED translucent
///   backing (wrist-HUD backdrop, pick-banner parchment, remote plates) is driven to alpha 1
///   while MR is on and restored exactly on off. Only mod-owned objects are ever registered.
/// - <see cref="Surface"/> — a world CANVAS that is not a <see cref="ConvertedPanel"/> and
///   therefore invisible to the panel sweep below. The first shipped case is a REMOTE board's
///   <c>Net.RemoteWidgetMirror</c> clones (user report 2026-08-08: "die Mixed-Reality-Hintergründe
///   sollen auch für das Remote-Board genauso angezeigt werden — aktuell sind die Hintergründe
///   nur auf meinem eigenen Board sichtbar"). The asymmetry was structural, not cosmetic: on the
///   OWNER's board the objectives panel and the initiative track are converted panels, so
///   <see cref="TickPanels"/> plates them; on a PEER's mirrored board the very same two widgets
///   are live CLONES hosted on our own world canvas, which <c>ActivePanels</c> never lists — so
///   they floated bare over the passthrough room while the owner's copy sat on a solid plate.
///   The registrant reports its own anchor/size/centre/order each tick (the mirror already
///   measures all four in its fit pass), and gets the SAME plate, the SAME material, the SAME
///   ladder contract and the SAME bit-identical OFF path as a converted panel.
///   <para>The second case is the GAME's shared tooltip widget (<c>WorldTooltips</c>, user report
///   2026-08-09: "Der Hintergrund der Tooltipps erscheint grün im mixed-reality Modus"). That
///   canvas is the game's own <c>CanvasManager.tooltipCanvas</c> — never converted, never adopted,
///   so neither sweep below has ever seen it — and its frame is a 9-sliced sprite under a
///   CanvasGroup the game tweens 0→1 on every show, i.e. genuinely translucent over the chroma
///   key. The PLATE is still mod-owned and still a child of a rect the registrant nominates, so
///   nothing here reaches into game state; see <see cref="Release"/> for the extra teardown that
///   a plate parented under a GAME object needs and the panel plates do not.</para>
/// - Converted panels need no registration: <see cref="Tick"/> sweeps
///   <see cref="CanvasConversion.ActivePanels"/> and keeps a host-rect plate behind EVERY live
///   converted panel. That deliberately includes the ModalFallback float families whose native
///   full-window backing the mod itself strips (WantsTransparentBackground — correct on a flat
///   screen, unreadable in MR) and the adopted HUD panels whose game art may be translucent;
///   behind a natively opaque panel the plate is simply invisible (drawn first, fully covered),
///   so over-coverage is harmless while under-coverage is the reported bug. The ONLY exceptions
///   are panels whose owner set <see cref="ConvertedPanel.MrBackingSuppressed"/> (user ruling
///   2026-08-04: actor health bars and the figure-grab stat cards — see that flag's doc for the
///   full reasoning; joined 2026-08-09 by the hover PROP-INFO cards, "Geschlossene Tür" and
///   friends, whose own card art is already opaque — see <c>Surfaces.PropInfoSurface</c>); the
///   sweep never plates those and destroys any plate they already carry.
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
/// WHAT THE LADDER SLOT DOES *NOT* AUTHORISE — the plate never wins against something genuinely in
/// front of it (user report 2026-08-08, MR: "Die mixed reality hintergründe schieben sich vor den
/// outlines von karten, das darf nicht sein - die Perspektive soll auch hier voll gewährleistet
/// sein"). Against DEPTH-WRITING geometry the plate is already honest and always was: it is
/// ZTest LEqual, so a card's AlphaTest backing slab (queue 2450, ZWrite on) discards the plate over
/// the whole card silhouette, per pixel, whichever sortingOrder either of them holds — which is why
/// the reported defect stopped at the card's OUTLINE. The mod's own cue art AROUND a card (the item
/// fan's usable frame, the hand fan's insertion glow) draws just outside that silhouette, writes no
/// depth of its own and shipped at sortingOrder 0..1, so nothing but order could arbitrate it and
/// the plate's ladder slot won. That is fixed ON THE CUE, not here: those cues now rank themselves
/// against the same distance ladder every frame (Cards/CardGlow.cs, CardCueOrder — which also
/// records why giving a hollow soft-falloff outline a depth write is the wrong answer). Nothing in
/// this file's sorting contract changed for it, deliberately: the plate's slot is what defends the
/// keine_ausblendung.png bleed-through, and a plate that stepped down the ladder to make room for a
/// card cue would let a farther MENU shine through it again.
///
/// THE PLATE MUST DIE WITH ITS CONTENT — AND FADE WITH IT (user report 2026-08-09, MR: "Wenn man
/// jetzt mit dem Laser direkt zu der Option fährt wo vorher das Tooltip war, würde ich erwarten,
/// dass es restlos sofort verschwindet […] Es verschwindet, hinterlässt aber einen Streifen im
/// Mixed-Reality-Modus der ca. 1 Sekunde da ist"). A plate is OPAQUE by construction, so it has no
/// fade of its own: every visibility gate in this file is a HARD on/off, and any content that
/// disappears by fading rather than by being switched off therefore leaves a solid dark rectangle
/// standing where it used to be — invisible on a normal background (the plate matches the panel
/// neutral), unmissable over passthrough. Two shapes of that defect exist and both are answered by
/// <see cref="IFadedBacking"/> / <see cref="Label(TMP_Text?, bool)"/>:
/// <list type="bullet">
/// <item><description>The plate OUTLIVES the content — the reported bug. See
///   <c>WorldTooltips.TooltipBacking</c>: its gate was the placement grace, not the box.</description></item>
/// <item><description>The plate SNAPS while the content fades — <c>Board.PingNameTag</c> runs a
///   0.35 s alpha tail on its caption before destroying itself, so its plate stood at full opacity
///   behind an already-invisible name and then vanished in one frame.</description></item>
/// </list>
/// The fade is OPT-IN, deliberately, and NOT derived from the content's alpha automatically: a
/// label's alpha is not a statement about its presence. <c>ButtonCluster</c> parks a disabled
/// button's caption at a STEADY alpha 0.35 (ButtonCluster.UpdateColor) — following that would thin
/// its plate permanently and hand the passthrough room back through the one backing that exists to
/// keep it out. Only a registrant that knows its alpha means "I am going away" asks for the fade;
/// every other plate keeps the shared opaque material and this whole path costs one bool test.
///
/// KEY-COLOR SAFETY: the plate must NEVER render the chroma key (it would punch a passthrough
/// hole exactly where readability was wanted). Presets are green/magenta/blue/black — the
/// saturated keys are nowhere near the dark panel neutral, but the BLACK preset is close to
/// it, so when the live key color comes within keying distance of the plate color the plate
/// is lifted to a brighter warm gray instead. Re-checked every tick (the key is live-cycled
/// from the VR options tab).
/// </summary>
internal static class MrBacking
{
    /// <summary>
    /// A MOD-OWNED world surface that wants the converted-panel treatment WITHOUT being a
    /// <see cref="ConvertedPanel"/> — see the class doc's <c>Surface</c> bullet. Everything the
    /// plate needs is polled off the implementer each tick, so the registrant stays the single
    /// source of truth for its own geometry and this class never reaches into it.
    ///
    /// CONTRACT: all four geometry members are expressed in <see cref="BackingAnchor"/>-LOCAL
    /// units (whatever the anchor's own scale carries into world — px for a world-space canvas
    /// host, metres for a bare transform), exactly like the panel sweep's host-rect units. The
    /// implementer must report a size of 0 while it has nothing to back rather than guessing.
    /// </summary>
    internal interface IBackedSurface
    {
        /// <summary>False once the registrant is torn down — the ONLY prune signal. (A null
        /// anchor is "not built yet", which is a normal state for a surface registered in its
        /// constructor, so it must never be read as death.)</summary>
        bool BackingAlive { get; }

        /// <summary>Parent for the plate; null until the surface has built its host.</summary>
        Transform? BackingAnchor { get; }

        /// <summary>Whether the backed content is on screen this frame.</summary>
        bool BackingVisible { get; }

        /// <summary>Content extent in anchor-local units.</summary>
        Vector2 BackingSize { get; }

        /// <summary>Content centre in anchor-local units.</summary>
        Vector2 BackingCenter { get; }

        /// <summary>The sortingOrder the plate must SHARE with the content it backs — the plate's
        /// earlier renderQueue (<see cref="PlateQueue"/>) is what draws it first inside that
        /// shared slot, the same tie-break the panel plates ride (see the class doc).</summary>
        int BackingOrder { get; }
    }

    /// <summary>
    /// OPT-IN companion to <see cref="IBackedSurface"/>: "my content DISAPPEARS BY FADING, so the
    /// plate must fade with it". Implement it alongside the main interface and the plate's opacity
    /// tracks <see cref="BackingAlpha"/> every tick (see the class doc, THE PLATE MUST DIE WITH ITS
    /// CONTENT). A surface that does not implement it is bit-identical to before: one type test per
    /// surface per MR-on tick over a list of two.
    /// </summary>
    internal interface IFadedBacking
    {
        /// <summary>0..1 — how present the backed content is THIS tick. 1 is the steady state and
        /// keeps the shared opaque material; anything below it swaps the plate to its own
        /// alpha-blended instance, and at <see cref="PlateFadeCutoff"/> the plate goes off
        /// entirely (there is nothing left to back).</summary>
        float BackingAlpha { get; }
    }

    /// <summary>
    /// Name every plate GameObject carries (<see cref="CreatePlate"/>). Public so a surface that
    /// render-hides a panel itself can REPORT, in its own log line, that the MR plate was among the
    /// renderers it switched off — the one component the ModBuild 84 hardware report proved a
    /// canvas-only hide leaves behind ("Der mixed-reality Hintergrund … ist auch bei den anderen
    /// Characteren noch zu sehen aber leer"). Identification by name is diagnostic ONLY: the hide
    /// itself is name-blind and switches off every Renderer it finds.
    /// </summary>
    internal const string PlateObjectName = "GloomhavenVR.MrBacking";

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

    /// <summary>Below this opacity a fading plate is switched OFF rather than drawn: there is
    /// nothing readable left to back, and an inactive plate is the same zero-cost, zero-risk state
    /// every other gate in this file resolves to. Deliberately well under the 0.05 alpha at which
    /// <c>WorldTooltips</c> already stops calling its box "shown", so the two agree.</summary>
    private const float PlateFadeCutoff = 0.02f;

    /// <summary>Opacity at or above which a plate counts as fully present and rides the SHARED
    /// opaque material — the steady state for every plate in the mod.</summary>
    private const float PlateOpaqueAlpha = 0.999f;

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

        /// <summary>The owner declared that this label DISAPPEARS BY FADING its own colour alpha
        /// (see <see cref="Label(TMP_Text?, bool)"/>) — the only labels whose plate follows that
        /// alpha. False for every other registrant, including the ones that sit at a steady
        /// sub-1 alpha by design.</summary>
        public bool Fades;

        /// <summary>Per-plate alpha-blended material instance, built on the first faded tick and
        /// destroyed with the plate (see <see cref="ApplyPlateAlpha"/>).</summary>
        public Material? FadeMat;

        /// <summary>The plate's renderer currently carries <see cref="FadeMat"/> (so the swap back
        /// to the shared opaque material happens exactly once, not every frame).</summary>
        public bool Faded;
    }

    private sealed class PanelEntry
    {
        public ConvertedPanel Panel = null!;
        public Transform? Plate;

        /// <summary>Change-dedup for the plate-extent line (rounded px) — see <see cref="LogPlateExtent"/>.</summary>
        public string? LoggedExtent;
    }

    private sealed class SurfaceEntry
    {
        public IBackedSurface Surface = null!;
        public Transform? Plate;
        public Renderer? PlateRenderer;

        /// <summary>Cached at registration: the surface also declared <see cref="IFadedBacking"/>,
        /// so the type test happens once per surface instead of once per tick.</summary>
        public IFadedBacking? Fade;

        /// <summary>Per-plate alpha-blended material instance — see <see cref="LabelEntry.FadeMat"/>.</summary>
        public Material? FadeMat;

        /// <summary>The plate's renderer currently carries <see cref="FadeMat"/>.</summary>
        public bool Faded;
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
    private static readonly List<SurfaceEntry> Surfaces = new(8);
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

    /// <summary>
    /// Register a free-floating world TMP label for a fitted backing plate (idempotent).
    ///
    /// <para><paramref name="fades"/> is the opt-in from the class doc's THE PLATE MUST DIE WITH ITS
    /// CONTENT section: pass true ONLY when the owner makes this label disappear by tweening its own
    /// <c>color.a</c> down to 0 (<c>Board.PingNameTag</c>'s 0.35 s expiry tail is the shipped case),
    /// and the plate then fades in lockstep instead of standing solid behind an invisible caption
    /// and snapping off a third of a second later. It is NOT the default because alpha is not a
    /// presence signal in general — <c>ButtonCluster</c> parks a disabled button's caption at a
    /// steady 0.35 and that plate must stay fully opaque.</para>
    /// </summary>
    internal static void Label(TMP_Text? label, bool fades = false)
    {
        if (label == null)
            return;
        for (int i = 0; i < Labels.Count; i++)
        {
            if (!ReferenceEquals(Labels[i].Label, label))
                continue;
            Labels[i].Fades |= fades; // a re-registration may only ever ADD the opt-in
            return;
        }
        Labels.Add(new LabelEntry { Label = label, Fades = fades });
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

    /// <summary>
    /// Register a MOD-OWNED world surface that is not a converted panel for the SAME opaque host
    /// plate the panel sweep builds (idempotent — a surface that re-registers is a no-op). See
    /// <see cref="IBackedSurface"/> and the class doc's <c>Surface</c> bullet: this is what closes
    /// the "my own board has MR backings, the remote board does not" asymmetry for the mirrored
    /// widget canvases. Registration is MR-AGNOSTIC and costs one list entry: no plate exists, and
    /// nothing is polled, until MR is actually on.
    /// </summary>
    internal static void Surface(IBackedSurface? surface)
    {
        if (surface == null)
            return;
        // Prune dead registrants HERE rather than on the (deliberately single-bool-check) MR-off
        // frame: remote boards come and go with peers, so a session that never turns MR on would
        // otherwise grow this list without bound. Registration is once per mirror, so the scan is
        // the same one the idempotence check already walks.
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            if (Surfaces[i].Surface.BackingAlive)
                continue;
            DestroyPlate(Surfaces[i].Plate, Surfaces[i].FadeMat);
            Surfaces.RemoveAt(i);
        }
        for (int i = 0; i < Surfaces.Count; i++)
        {
            if (ReferenceEquals(Surfaces[i].Surface, surface))
                return;
        }
        // The fade opt-in is resolved ONCE here (see IFadedBacking): the per-tick sweep then reads a
        // cached reference instead of type-testing every surface every frame.
        Surfaces.Add(new SurfaceEntry { Surface = surface, Fade = surface as IFadedBacking });
    }

    /// <summary>
    /// Unregister a <see cref="Surface"/> AND destroy its plate NOW, whatever MR is doing.
    ///
    /// <para>WHY THIS EXISTS AND THE OTHER REGISTRATION KINDS DO NOT NEED IT. Every other plate in
    /// this class hangs under a MOD-OWNED object that dies on its own (a converted panel's host, a
    /// mirror's clone canvas, a label the owner destroys), and the MR-OFF contract is therefore
    /// "deactivate, do not destroy" — a deactivated mod object under a mod object is bit-identical
    /// rendering and the next MR-ON reuses it. A plate parented under a GAME rect is a different
    /// promise: <c>WorldTooltips</c> hands its whole presentation back the instant the mod leaves
    /// scenario mode (render mode, worldCamera, scale, sorting, the flatten, the added mask, the
    /// widened fade — all restored verbatim), and leaving an inactive mod quad parented under the
    /// game's shared tooltip widget would be the one piece of that teardown that did not happen.
    /// So the registrant releases explicitly and this is the only path that can run while MR is
    /// OFF (<see cref="TickSurfaces"/>, which normally prunes dead registrants, does not tick
    /// then). Idempotent: releasing an unregistered surface is a no-op, so a restore that runs
    /// twice costs one list walk.</para>
    /// </summary>
    internal static void Release(IBackedSurface? surface)
    {
        if (surface == null)
            return;
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(Surfaces[i].Surface, surface))
                continue;
            DestroyPlate(Surfaces[i].Plate, Surfaces[i].FadeMat);
            Surfaces.RemoveAt(i);
            return;
        }
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
        TickSurfaces();
        TickAlphas();
        _applied = true;
        if (!_loggedOn)
        {
            _loggedOn = true;
            VRLog.Info("WorldUI", $"MR backings ON — {Labels.Count} label plate(s), " +
                                  $"{Panels.Count} panel plate(s), {Surfaces.Count} registered " +
                                  $"non-panel surface(s) (remote-board mirror canvases), " +
                                  $"{Graphics.Count + Materials.Count} " +
                                  $"opacified backing(s); plate color RGBA {_plateColor.r:0.##}," +
                                  $"{_plateColor.g:0.##},{_plateColor.b:0.##},1 (key-color-safe).");
        }
    }

    // ---- the plate must be right at RENDER time, not at UPDATE time ---------------------------
    //
    // USER REPORT (hardware, ModBuild 107, MIXED REALITY, verbatim): "Das Flackern ist weg bei dem
    // Quest Text - aber nun ist mir aufgefallen, dass das selbe Flackern auch bei dem 'ABGEWORFEN',
    // 'VERBRANNT', 'GEGENSTÄNDE' Text auftritt - ich teste gerade im mixed reality modus. Schau dir
    // alle text nochmal an ob sie eventuell auch an dem Selben Problem leiden könnten."
    //
    // ROOT CAUSE — THE SAME DEFECT AS THE BATTLE-GOAL LABEL, BUT IT WAS NEVER THE LABEL'S FAULT.
    // ModBuild 107 fixed the quest caption by moving ITS rank write into the Update pass, because
    // Tick() (and with it TickLabels' order copy) is the last Update step in
    // WorldUIModule.BuildTickSteps. That is a per-registrant contract, and this file has ~15
    // registrants. The board's pile captions are the proof that the contract cannot be kept by
    // hand: NOBODY writes their order in Update at all. They are transparent board furniture, so
    // CanvasConversion.9.Furniture's ApplyFurnitureOrder writes them — from TickPanelOrder, which
    // WorldUIModule pins as the LAST step of the LATE pass ("TRANSPARENCY ROUND, and it must stay
    // LAST"). So the sequence for every furniture-backed caption was, every single frame:
    //
    //     Update      MrBacking.TickLabels  : plate := label.sortingOrder      (last frame's band)
    //     LateUpdate  ApplyFurnitureOrder   : label := new band base + offset
    //     render                            : label = new, plate = OLD
    //
    // and whenever the band moved DOWN — which is half of all band moves — the plate ended the
    // frame ranked ABOVE the glyphs it backs. It is Blend One Zero with _ZWrite 1, seated a couple
    // of millimetres behind, and TMP writes no depth to stop it, so it painted the caption out for
    // exactly that frame and the next Update re-synced both. One frame blank, then back.
    //
    // EVIDENCE, from the ModBuild 107 / b765a5b6e hardware log (MR ON, .planning/debug/LogOutput.log):
    //   * 172 'FURNITURE ORDER' lines — the control board's band re-seats 172 times in one session
    //     and travels the whole ladder (95..99, 111..115, 191..195, 399..403, 511..515). Every one
    //     of those is a frame in which every label plate on the board carried the previous band.
    //   * 'BATTLE-GOAL LADDER' reports 1, 16, 38, 13, 54, 60, 12, 15 draw-order changes between
    //     consecutive lines: the ladder these plates follow moves continuously, it is not a rare
    //     event. (That label itself no longer skews — 'FRAME-ORDER BREACH' fired ZERO times, which
    //     is the ModBuild 107 fix confirming itself.)
    //
    // THE FIX IS CENTRAL AND PHASE-BLIND. A plate's order only has to be correct at ONE moment —
    // the moment the frame is rendered — and the last thing that runs before that is LateUpdate.
    // So the copy is repeated there, after every order writer in the mod has had its say:
    // <see cref="SyncPlateOrders"/> is called from the END of CanvasConversion.TickPanelOrder,
    // which is where ApplyPanelOrder, TickFurnitureOrder, the see-through ladder and
    // Core.UnseenTileOrder.Tick all write. Calling it there rather than appending a step to
    // WorldUIModule's late list is deliberate: the guarantee is then LOCAL ("the orders were just
    // assigned; re-seat the plates that copy them") and cannot be broken by someone reordering a
    // list in another file.
    //
    // WHAT IT COSTS. One walk of Labels + Surfaces per frame while MR is ON — ~15 + ~2 entries in
    // the shipped scene. Per entry: two Unity-null tests and one int compare; a WRITE happens only
    // when the value actually differs, so a steady frame writes nothing and a band move writes at
    // most one int per plate. With MR OFF it is a single bool test (`_applied`), the same
    // no-effect-when-off regression bar the rest of this file keeps. The Update copy in TickLabels
    // and TickSurfaces is KEPT, not replaced: a plate created this frame must be seated before this
    // frame's render even if TickPanelOrder early-outs (no WorldCamera), and the Update copy is
    // what makes the LateUpdate pass a no-op in the overwhelming majority of frames.
    //
    // REJECTED ALTERNATIVES.
    //   * "Fix each registrant, like ModBuild 107 did." That is what produced this report. The
    //     order writer is often not the registrant at all (the furniture ladder writes the pile
    //     captions, the slot labels, the round readout, the ButtonCluster engravings and the item
    //     berth's USE caption), so 'write your rank in Update' is not even an instruction those
    //     owners could follow without leaving the furniture band.
    //   * "Move ApplyFurnitureOrder into the Update pass." It reads panel POSES, and every
    //     board-docked surface re-places its host in LateTick; measuring earlier would rank the
    //     whole ladder from last frame's geometry. WorldUIModule states that constraint explicitly
    //     and it outranks this one.
    //   * "Make the plate an order FOLLOWER of the furniture group." That works only for renderers
    //     the group knows about, and it would put a second writer on a field this class documents
    //     itself as the single writer of (Net/BoardVisual.AdoptBoardOrder skips plates by name for
    //     exactly that reason). Following the label's LIVE value keeps one writer and covers every
    //     registrant, including ones whose order comes from somewhere nobody has thought of yet.
    //   * "Drop the plate's _ZWrite so it cannot paint over the glyphs." The depth write is what
    //     makes the plate an honest opaque surface instead of a sorting trick (see the class doc's
    //     PLATE RENDERING note), and losing it would re-open the transparent-sort gambling the
    //     ladder exists to remove.
    //
    // NOTE FOR THE NEXT ROUND: the per-registrant guard this replaces
    // (Surfaces/TablePanelSurfaces.AssertPlateOrderSynced, 'FRAME-ORDER BREACH') is now structurally
    // unable to fire. It is harmless and was left alone — it is another agent's file — but it is no
    // longer the diagnostic to read. Read 'MR PLATE ORDER RESYNC' below instead.

    /// <summary>
    /// Re-copy every plate's sortingOrder from the content it backs, in the LATE pass — see the
    /// block above. Called as the last act of <c>CanvasConversion.TickPanelOrder</c>, so it runs
    /// after every draw-order writer in the frame and the value it seats is the one that renders.
    ///
    /// <para>PANEL plates deliberately do nothing here and need nothing: they are registered as
    /// offset-0 ORDER FOLLOWERS of their own panel (<see cref="TickPanels"/>), so
    /// <c>ApplyPanelOrder</c> writes plate and panel in the same statement, in this same pass —
    /// they were never able to skew.</para>
    /// </summary>
    internal static void SyncPlateOrders()
    {
        if (!_applied)
            return; // MR off (or never ticked on): no plate exists — one bool per frame

        int fixes = 0;
        for (int i = 0; i < Labels.Count; i++)
        {
            LabelEntry e = Labels[i];
            // Both references are probed/cached by TickLabels and go Unity-null with their owner;
            // a label that has not become visible yet has neither, and has nothing to re-seat.
            Renderer? label = e.LabelRenderer;
            Renderer? plate = e.PlateRenderer;
            if (label == null || plate == null || plate.sortingOrder == label.sortingOrder)
                continue;
            RecordResync(fixes, e.Label, plate.sortingOrder, label.sortingOrder);
            plate.sortingOrder = label.sortingOrder;
            fixes++;
        }
        for (int i = 0; i < Surfaces.Count; i++)
        {
            SurfaceEntry e = Surfaces[i];
            Renderer? plate = e.PlateRenderer;
            // BackingOrder is polled off the registrant, and a registrant whose host died reports
            // a safe 0 — but asking a dead one is pointless work, so the liveness flag gates it.
            if (plate == null || !e.Surface.BackingAlive)
                continue;
            int order = e.Surface.BackingOrder;
            if (plate.sortingOrder == order)
                continue;
            RecordResync(fixes, null, plate.sortingOrder, order);
            plate.sortingOrder = order;
            fixes++;
        }

        Core.PerfMonitor.Count("MrBacking.OrderResync", fixes);
        LogResync(fixes);
    }

    /// <summary>The label whose plate the CURRENT frame's first late re-sync belonged to, plus the
    /// two orders involved — the raw reference, never its name: <c>Object.name</c> allocates on
    /// every read and this runs per frame, so the formatting happens only inside the rate-limited
    /// log line below. Null for a non-label surface (they have no single TMP to name).</summary>
    private static TMP_Text? s_resyncFirst;
    private static int s_resyncFrom;
    private static int s_resyncTo;

    /// <summary>Frames in which at least one plate had to be re-seated late, and the total number
    /// of re-seats — the churn rate the next hardware report is read against.</summary>
    private static int s_resyncFrames;
    private static int s_resyncTotal;
    private static float s_resyncNextLogAt;

    /// <summary>Seconds between two <c>MR PLATE ORDER RESYNC</c> lines. The counters keep running
    /// between them, so the line always states a rate rather than a single frame.</summary>
    private const float ResyncLogIntervalSeconds = 20f;

    private static void RecordResync(int already, TMP_Text? label, int from, int to)
    {
        if (already != 0)
            return; // one example per frame is an identification; a list is a wall
        s_resyncFirst = label;
        s_resyncFrom = from;
        s_resyncTo = to;
    }

    /// <summary>
    /// THE line the next MR flicker report is read against. It answers, arithmetically, the one
    /// question a "the text blinked" report cannot answer on its own: did a plate actually end a
    /// frame ranked apart from its content, and how often. Zero frames here means the draw order is
    /// NOT the cause of whatever is still blinking and the next round must look elsewhere.
    /// </summary>
    private static void LogResync(int fixes)
    {
        if (fixes > 0)
        {
            s_resyncFrames++;
            s_resyncTotal += fixes;
        }
        float now = Time.unscaledTime;
        if (now < s_resyncNextLogAt)
            return;
        s_resyncNextLogAt = now + ResyncLogIntervalSeconds;
        if (s_resyncTotal == 0)
            return; // silence is the steady state; do not log a heartbeat of nothing
        string named = s_resyncFirst != null
            ? $" Last example: '{s_resyncFirst.gameObject.name}' {s_resyncFrom}→{s_resyncTo}."
            : $" Last example: a non-panel backed surface, {s_resyncFrom}→{s_resyncTo}.";
        VRLog.Info("WorldUI", $"MR PLATE ORDER RESYNC: {s_resyncTotal} plate re-seat(s) across " +
                              $"{s_resyncFrames} frame(s) since load. Each one is a frame in which an " +
                              "OPAQUE MR backing plate would have rendered ranked apart from the text " +
                              "it backs — above it, and the glyphs would have been painted out for that " +
                              "one frame. The order is copied in Update AND again here in LateUpdate " +
                              "(after CanvasConversion.TickPanelOrder, i.e. after the panel ladder, the " +
                              "board furniture band and the see-through pass have all written), so the " +
                              "value that renders is always the content's own." + named);
        s_resyncFirst = null;
    }

    /// <summary>Hot-reload / module teardown: destroy every plate, restore every alpha.</summary>
    internal static void Shutdown()
    {
        RestoreAll();
        for (int i = 0; i < Labels.Count; i++)
            DestroyPlate(Labels[i].Plate, Labels[i].FadeMat);
        for (int i = 0; i < Panels.Count; i++)
        {
            if (Panels[i].Plate != null)
                Object.Destroy(Panels[i].Plate!.gameObject); // panels never fade — no instance to drop
        }
        for (int i = 0; i < Surfaces.Count; i++)
            DestroyPlate(Surfaces[i].Plate, Surfaces[i].FadeMat);
        Labels.Clear();
        Panels.Clear();
        Surfaces.Clear();
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
                // The plate went with the label, but a fade material instance is OURS and would
                // leak with the entry — Destroy takes a Unity-null plate in its stride.
                DestroyPlate(e.Plate, e.FadeMat);
                Labels.RemoveAt(i);
                continue;
            }
            RectTransform rect = e.Label.rectTransform;
            Vector2 size = rect.sizeDelta; // TmpFit contract: the label box in local meters
            // OPT-IN FADE (class doc, THE PLATE MUST DIE WITH ITS CONTENT): only a label whose owner
            // declared that it EXPIRES BY FADING lets its alpha speak for the plate. Every other
            // label reports 1 and this is bit-identical to the shipped behaviour — including the
            // ones that sit at a steady sub-1 alpha on purpose (ButtonCluster's disabled captions),
            // whose plates must stay fully opaque or the passthrough room comes back through them.
            float alpha = e.Fades ? Mathf.Clamp01(e.Label.color.a) : 1f;
            bool visible = e.Label.isActiveAndEnabled && size.x > 0.001f && size.y > 0.001f
                           && !string.IsNullOrEmpty(e.Label.text)
                           && alpha > PlateFadeCutoff;
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
            ApplyPlateAlpha(e.PlateRenderer, ref e.FadeMat, ref e.Faded, alpha);
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
            //
            // OwnerRenderHidden — the SAME refusal for the surface-owned hide (hardware report
            // ModBuild 84: "Der mixed-reality Hintergrund für die decision ist auch bei den anderen
            // Characteren noch zu sehen aber leer"). A decision row / use bar that the character
            // focus render-hid is still activeInHierarchy and still !RenderHidden — the surfaces
            // deliberately hide CANVASES, never GameObjects, so the game's live widgets are never
            // poked — and this plate is not a Canvas, so it kept drawing: an empty dark rectangle
            // where the row had been. Reading BOTH flags is the whole fix on this side; the
            // surfaces also switch the plate's Renderer off directly (belt and braces, and it
            // covers a plate that exists before the hide starts). Ordering makes this leak-free
            // rather than one frame late: the two surfaces tick EARLIER in the same Update than
            // this sweep (WorldUIModule.BuildTickSteps: …DecisionDockSurface, UseBarsSurface, …
            // CanvasConversion, MrBacking), so on the first hidden tick no plate is ever created,
            // and an already-built one is deactivated below in that same frame.
            bool visible = panel.HostGo.activeInHierarchy
                           && !panel.RenderHidden && !panel.OwnerRenderHidden
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
            // window never had. …UNLESS THE PANEL RENDERS VISIBLE TEXT OUTSIDE THAT RECT, which is
            // not padding but measurement — see GlyphTrueRect.
            Rect fitted = GlyphTrueRect(panel, host, r, out int overflowing);
            Fit(entry.Plate, host, fitted.size, fitted.center);
            LogPlateExtent(entry, host, r, fitted, overflowing);
        }
    }

    /// <summary>Scratch for the per-panel glyph sweep (no steady-state allocation). Collected as
    /// <see cref="Graphic"/> — the EXACT component type the content fit sweeps
    /// (<c>CanvasConversion.TryMeasureContent</c>) — and filtered to <see cref="TMP_Text"/> inside
    /// the loop, so the two passes provably walk the same set of objects and the shared visibility
    /// verdict below is asked about the same thing the host rect was measured from. The set is
    /// unchanged from the previous <c>List&lt;TMP_Text&gt;</c> sweep: <c>TMP_Text</c> derives from
    /// <c>MaskableGraphic</c>, so BOTH the uGUI <c>TextMeshProUGUI</c> and the world-space
    /// <c>TextMeshPro</c> are Graphics and are found either way.</summary>
    private static readonly List<Graphic> TextScratch = new(32);

    /// <summary>How many overflowing text objects the extent log names (see
    /// <see cref="LogPlateExtent"/>). A handful is an identification, a full list is a wall.</summary>
    private const int OverflowNameCap = 4;

    /// <summary>The first <see cref="OverflowNameCap"/> text objects the last
    /// <see cref="GlyphTrueRect"/> pass had to grow the plate for, and their glyph rects in HOST
    /// units — the raw REFERENCES, never their names: <c>Object.name</c> allocates a managed string
    /// on every read and this sweep runs per panel per frame, so the formatting happens only inside
    /// the change-gated log line.</summary>
    private static readonly TMP_Text?[] OverflowText = new TMP_Text?[OverflowNameCap];
    private static readonly Rect[] OverflowRect = new Rect[OverflowNameCap];

    /// <summary>
    /// THE PLATE MUST COVER WHAT IS DRAWN, NOT WHAT WAS MEASURED (user hardware report, ModBuild 90:
    /// "Der Schadenstext 'Schadensphase: Erleide […]' ist nicht vollständig von dem mixed-reality
    /// Hintergrund abgedeckt, aktuell nur der mittlere Teil des Textes, an den äußeren Rändern fehlt
    /// etwas vom Hintergrund").
    ///
    /// <para>ROOT CAUSE, and why no amount of out-padding was the answer: a converted panel's host
    /// rect is the content FIT's union of the visible graphics' RECTANGLES, clamped to the window's
    /// own frame (<c>CanvasConversion.TryMeasureContent</c>). A <see cref="TMP_Text"/> whose line is
    /// longer than its box does not wrap or clip — uGUI has no implicit clipping — it simply RENDERS
    /// WIDER THAN EVERY RECTANGLE IN THAT UNION. The take-damage HelpBox is exactly that: a
    /// 479-px-wide window holding a full sentence, centred, so the host rect (and therefore an
    /// exactly-host-rect-sized plate) covers the middle of the line and the ends hang off into the
    /// passthrough room. The fix measures the RENDERED glyph bounds — the same <c>textBounds</c>
    /// basis the free-floating label plates have always used (<see cref="TickLabels"/>) — and unions
    /// them into the plate rect, with the same <see cref="LabelPadFraction"/>/
    /// <see cref="LabelPadFloorMeters"/> margin those label plates use, so an overflowing line gets a
    /// backing that looks like every other backing in the mod.</para>
    ///
    /// <para>NON-OVERFLOWING PANELS ARE BIT-IDENTICAL: a label whose glyphs sit inside the host rect
    /// contributes nothing and the plate stays exactly <paramref name="hostRect"/>, so the "never a
    /// frame the window never had" contract still holds for every panel that never had the bug.
    /// CLIPPED text is skipped outright (a <c>RectMask2D</c>/<c>Mask</c> between the label and the
    /// host means the renderer crops it to a viewport that is inside the host anyway) — a scrolled-out
    /// row must never inflate a plate.</para>
    ///
    /// <para>ROUND 2 — "VISIBLE" NOW MEANS WHAT THE FIT MEANS BY IT (user hardware report 2026-08-08,
    /// MR: "Der mixed Reality Hintergrund für die Initiativreihenfolge ist nach deiner letzten
    /// Änderung vertikal zu lang - davor war es besser, ich will nicht, dass große
    /// Hintergrund-Rechtecke existieren von denen der Platz garnicht genutzt wird",
    /// .planning/debug/mixed_reality_background.png: the portrait row sits in the TOP HALF of a plate
    /// twice its height). Round 1's visibility test was active + non-empty text + not masked, which
    /// is a far weaker question than the one the host rect was measured with: the content fit rejects
    /// a graphic as <c>Culled</c> (component-disabled or <c>canvasRenderer.cull</c>), <c>Faint</c>
    /// (effective alpha under 0.05 — own colour × the inherited CanvasGroup alpha), <c>Empty</c>
    /// (collapsed draw rect) or <c>ClippedOut</c>, AND skips this mod's own cue art. So this sweep
    /// was unioning back in EXACTLY the text the fit had already judged invisible, and the hardware
    /// log names the cost on one panel: the fit reads "1201x175 px … rejected 24 culled/disabled, 65
    /// faint", the plate one line later reads "16 text line(s) OUTSIDE its fitted host rect …
    /// 1293x294 px, centred at (46,-59)" — +119 px of height, almost all of it DOWNWARD, under a
    /// portrait row that is 172 px tall. The predicate is now the fit's OWN
    /// (<c>CanvasConversion.CountsAsFitContent</c>), called rather than re-implemented, so the two
    /// can never drift apart again.</para>
    ///
    /// <para>WHAT DELIBERATELY DID NOT CHANGE: the fit's FRAME CLAMP is NOT applied here. The fit
    /// crops its union into the conversion target's own rect; the take-damage HelpBox is a 479 px
    /// window holding a whole centred sentence that renders WIDER than that frame on purpose, and
    /// covering exactly that overflow is what this method exists for. The legitimate overflows in the
    /// same hardware log — <c>Panel_Objectives</c> (+15 px tall) and <c>Panel_EnemyReveal</c>
    /// (+55…115 px tall as its reveal list fills) — are drawn by text the fit COUNTS, so they keep
    /// their enlarged plates unchanged.</para>
    /// </summary>
    private static Rect GlyphTrueRect(ConvertedPanel panel, RectTransform host, Rect hostRect,
                                      out int overflowing)
    {
        overflowing = 0;
        for (int i = 0; i < OverflowNameCap; i++)
            OverflowText[i] = null;
        TextScratch.Clear();
        host.GetComponentsInChildren(includeInactive: false, TextScratch);
        if (TextScratch.Count == 0)
            return hostRect;

        // The fit's per-pass clipper/authored memos are keyed by Transform and only ever cleared at
        // the START of a pass — this sweep is an outside caller of that same code, so it opens its
        // own query the same way (see CanvasConversion.BeginContentQuery: the initiative fit disarms
        // itself after one applied re-fit, so "the next fit pass will clear it" is not true here).
        CanvasConversion.BeginContentQuery();

        // The label margin in HOST units (canvas px): the floor is authored in real metres, and the
        // host's own scale carries px→world — the PlateGapMeters conversion in Fit, same reasoning.
        float unit = LabelPadFloorMeters * PanelLayout.WorldScale
                     / Mathf.Max(Mathf.Abs(host.lossyScale.x), 1e-5f);
        Vector2 min = hostRect.min;
        Vector2 max = hostRect.max;
        for (int i = 0; i < TextScratch.Count; i++)
        {
            Graphic g = TextScratch[i];
            if (g == null || g is not TMP_Text t || string.IsNullOrEmpty(t.text))
                continue;
            if (IsClipped(t.rectTransform, host))
                continue;
            Bounds b = t.textBounds;
            if (b.size.x <= 0.0001f || b.size.y <= 0.0001f)
                continue; // no mesh yet — the rect union already covers the authored box
            Vector3 lo = host.InverseTransformPoint(
                t.transform.TransformPoint(new Vector3(b.min.x, b.min.y, 0f)));
            Vector3 hi = host.InverseTransformPoint(
                t.transform.TransformPoint(new Vector3(b.max.x, b.max.y, 0f)));
            Vector2 gMin = Vector2.Min(lo, hi);
            Vector2 gMax = Vector2.Max(lo, hi);
            if (gMin.x >= hostRect.xMin - 0.5f && gMax.x <= hostRect.xMax + 0.5f
                && gMin.y >= hostRect.yMin - 0.5f && gMax.y <= hostRect.yMax + 0.5f)
                continue; // drawn inside the host rect — the plate already backs it
            // ROUND 2, and the whole "vertikal zu lang" fix: the LAST question, because it is the
            // expensive one (an alpha/cull/clip probe plus, for a survivor, a name read) and because
            // only a geometric overflow candidate can ever grow the plate. A row the FIT did not
            // count is not on screen — backing it is backing nothing.
            if (!CanvasConversion.CountsAsFitContent(panel, g))
                continue;
            if (overflowing < OverflowNameCap)
            {
                OverflowText[overflowing] = t;
                OverflowRect[overflowing] = Rect.MinMaxRect(gMin.x, gMin.y, gMax.x, gMax.y);
            }
            overflowing++;
            // Half the label margin on each side, so the total margin matches TickLabels exactly.
            float padX = ((gMax.x - gMin.x) * LabelPadFraction + unit) * 0.5f;
            float padY = ((gMax.y - gMin.y) * LabelPadFraction + unit) * 0.5f;
            min = Vector2.Min(min, new Vector2(gMin.x - padX, gMin.y - padY));
            max = Vector2.Max(max, new Vector2(gMax.x + padX, gMax.y + padY));
        }
        TextScratch.Clear();
        return overflowing == 0 ? hostRect : Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    /// <summary>Is <paramref name="rect"/> under a clipper (<see cref="RectMask2D"/> / stencil
    /// <see cref="Mask"/>) between it and <paramref name="host"/>? Clipped glyphs are cropped to a
    /// viewport that lives inside the host rect, so they can never be the uncovered content this
    /// sweep is looking for — and treating them as such would inflate a plate to the size of a
    /// scrolled-out list.
    ///
    /// <para>This is the ONE place the plate is deliberately STRICTER than the content fit it now
    /// shares its visibility predicate with (<c>CanvasConversion.CountsAsFitContent</c>): the fit
    /// CLAMPS a partly-clipped graphic to its viewport and keeps the visible remainder, because it
    /// measures RECTANGLES it can clamp. This sweep measures rendered GLYPH BOUNDS, which carry no
    /// clipper clamp — so it declines the text instead of inventing one. Strictness in this
    /// direction is safe by construction (a clipped line is cropped into a viewport that lives
    /// inside the host rect, which the plate already covers); the direction that produced the
    /// "vertikal zu lang" report — accepting what the fit rejected — is the one that is now
    /// impossible.</para></summary>
    private static bool IsClipped(Transform rect, Transform host)
    {
        for (Transform? t = rect; t != null && !ReferenceEquals(t, host); t = t.parent)
        {
            if (t.GetComponent<RectMask2D>() != null)
                return true;
            Mask m = t.GetComponent<Mask>();
            if (m != null && m.enabled)
                return true;
        }
        return false;
    }

    /// <summary>
    /// One line per panel whose plate had to be widened past its host rect, change-gated on the
    /// rounded px — the hardware proof for the ModBuild 90 backdrop report: it states the MEASURED
    /// text extent, the host rect the plate used to be, and the plate extent now.
    ///
    /// <para>ROUND 2 — IT NAMES THE OVERFLOWING OBJECTS. The "vertikal zu lang" report cost a whole
    /// build to attribute because the line only ever said HOW MANY lines overflowed: 16 anonymous
    /// text objects on <c>Panel_InitiativeTrack</c>, with nothing in the log to say whether they
    /// were the portraits' initiative numbers or a row the player cannot see. It now prints the
    /// first <see cref="OverflowNameCap"/> by NAME with their glyph rect in host units, so the next
    /// hardware log identifies the culprits directly instead of only counting them. The names are
    /// read HERE and nowhere else — <c>Object.name</c> allocates on every read, and this line is
    /// change-gated while the sweep that fills <see cref="OverflowText"/> runs every frame.</para>
    /// </summary>
    private static void LogPlateExtent(PanelEntry entry, RectTransform host, Rect hostRect,
                                       Rect fitted, int overflowing)
    {
        if (overflowing == 0)
        {
            entry.LoggedExtent = null; // a panel that stops overflowing re-states it if it comes back
            return;
        }
        string key = $"{hostRect.width:F0}x{hostRect.height:F0}|{fitted.width:F0}x{fitted.height:F0}";
        if (entry.LoggedExtent == key)
            return;
        entry.LoggedExtent = key;
        string named = string.Empty;
        for (int i = 0; i < OverflowNameCap && i < overflowing; i++)
        {
            TMP_Text? t = OverflowText[i];
            if (t == null)
                continue;
            Rect g = OverflowRect[i];
            named += named.Length == 0 ? " Overflowing: " : ", ";
            named += $"'{t.gameObject.name}' {g.width:F0}x{g.height:F0}px " +
                     $"at ({g.center.x:F0},{g.center.y:F0})";
        }
        if (named.Length > 0 && overflowing > OverflowNameCap)
            named += $", +{overflowing - OverflowNameCap} more";
        if (named.Length > 0)
            named += ".";
        VRLog.Info("WorldUI", $"MR PLATE EXTENT: '{host.gameObject.name}' draws {overflowing} VISIBLE " +
                              $"text line(s) OUTSIDE its fitted host rect ({hostRect.width:F0}x" +
                              $"{hostRect.height:F0} px) — a TMP line does not wrap or clip at its box, " +
                              "and the content fit measures rectangles, so an exactly-host-rect plate " +
                              "covered only the middle of it. Visible = the CONTENT FIT's own verdict " +
                              "(CanvasConversion.CountsAsFitContent), so culled/alpha-faint/collapsed " +
                              "rows can no longer inflate this plate. It is now fitted to the RENDERED " +
                              $"glyph bounds plus the standard label margin: {fitted.width:F0}x" +
                              $"{fitted.height:F0} px, centred at ({fitted.center.x:F0},{fitted.center.y:F0}) " +
                              $"— {fitted.width - hostRect.width:F0} px wider and " +
                              $"{fitted.height - hostRect.height:F0} px taller than the host rect, which is " +
                              "exactly the part of the line that used to sit on the passthrough room." +
                              named);
    }

    /// <summary>
    /// The non-panel sweep (see the class doc's <c>Surface</c> bullet): the same build → size →
    /// order → show pass <see cref="TickPanels"/> runs, driven off <see cref="IBackedSurface"/>
    /// instead of off <c>CanvasConversion.ActivePanels</c>. Deliberately a SEPARATE loop rather
    /// than a shim that fakes a ConvertedPanel: a mirror clone has no host rect, no reveal gate
    /// and no ladder slot to follow, and pretending otherwise is how the panel sweep would start
    /// growing special cases.
    ///
    /// ORDER: the surface reports the sortingOrder its own content renders at and the plate is
    /// stamped with exactly that, every tick — and since 2026-08-09 that is a LIVE value: a
    /// mirror's canvas rides its board's draw-order cluster up and down the panel ladder
    /// (<c>Net.BoardVisual.AdoptBoardOrder</c>), so the per-tick re-stamp this loop always did is
    /// what carries the plate with it. (The sweep skips plates by name for the same reason: this
    /// is their one writer.) Inside
    /// that shared slot the plate's earlier renderQueue keeps it UNDER its own content while the
    /// slot itself keeps it OVER everything the board draws further back — the identical contract
    /// the panel plates document.
    /// </summary>
    private static void TickSurfaces()
    {
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            SurfaceEntry e = Surfaces[i];
            IBackedSurface s = e.Surface;
            if (!s.BackingAlive)
            {
                // The registrant is gone. Its plate is a child of the host it destroyed (already
                // Unity-null) OR still ours to drop — going through Destroy covers both.
                DestroyPlate(e.Plate, e.FadeMat);
                Surfaces.RemoveAt(i);
                continue;
            }

            Transform? anchor = s.BackingAnchor;
            Vector2 size = anchor != null ? s.BackingSize : Vector2.zero;
            // OPT-IN FADE (class doc, THE PLATE MUST DIE WITH ITS CONTENT): a surface that declared
            // IFadedBacking hands us the opacity its own content renders at this tick — the tooltip
            // rides the game's CanvasGroup tween that way, so plate and frame appear and disappear
            // as ONE object. A surface that did not is 1 and unchanged.
            float alpha = e.Fade != null ? Mathf.Clamp01(e.Fade.BackingAlpha) : 1f;
            // Degenerate sizes read as "nothing to back": a mirror mid-rebuild reports zero, and an
            // opaque plate at a guessed rect is exactly the pop-in the panel sweep refuses too.
            bool visible = anchor != null && s.BackingVisible
                           && size.x > 0.0001f && size.y > 0.0001f
                           && alpha > PlateFadeCutoff;
            if (e.Plate == null)
            {
                if (!visible)
                    continue; // built lazily on the first tick that has real geometry
                e.Plate = CreatePlate(anchor!);
                e.PlateRenderer = e.Plate.GetComponent<MeshRenderer>();
            }
            else if (anchor != null && e.Plate.parent != anchor)
            {
                // The surface rebuilt its host under us (a mirror rebuilds its clone on any
                // structure change). Re-seat rather than orphan the plate behind the new canvas.
                e.Plate.SetParent(anchor, worldPositionStays: false);
            }

            if (e.Plate.gameObject.activeSelf != visible)
                e.Plate.gameObject.SetActive(visible);
            if (!visible || anchor == null)
                continue;

            int order = s.BackingOrder;
            if (e.PlateRenderer != null && e.PlateRenderer.sortingOrder != order)
                e.PlateRenderer.sortingOrder = order;

            Fit(e.Plate, anchor, size, s.BackingCenter);
            ApplyPlateAlpha(e.PlateRenderer, ref e.FadeMat, ref e.Faded, alpha);
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
                DestroyPlate(Labels[i].Plate, Labels[i].FadeMat);
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
        // Non-panel surfaces: same rule, same reason — the plate is mod-owned, so deactivating it
        // returns the remote board to bit-identical non-MR rendering. Dead registrants are pruned
        // here too, so an MR off/on cycle never resurrects a plate for a torn-down mirror.
        for (int i = Surfaces.Count - 1; i >= 0; i--)
        {
            SurfaceEntry e = Surfaces[i];
            if (!e.Surface.BackingAlive)
            {
                DestroyPlate(e.Plate, e.FadeMat);
                Surfaces.RemoveAt(i);
                continue;
            }
            if (e.Plate != null)
                e.Plate!.gameObject.SetActive(false);
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

    /// <summary><paramref name="anchor"/> is a plain <see cref="Transform"/>, not a
    /// <see cref="RectTransform"/>: the label and panel sweeps pass rects, the non-panel sweep
    /// passes a world-canvas HOST (whose own scale carries px→m the same way a host rect's does).
    /// Nothing here ever needed the rect API — only the layer and the Z lossy scale.</summary>
    private static void Fit(Transform plate, Transform anchor, Vector2 size, Vector2 center)
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

    /// <summary>
    /// Drive one plate's opacity (class doc, THE PLATE MUST DIE WITH ITS CONTENT).
    ///
    /// <para>THE STEADY STATE IS FREE AND BIT-IDENTICAL: at full opacity — which is EVERY plate in
    /// the mod except a tooltip mid-tween and a ping tag in its expiry tail — this is one float
    /// compare against an already-false bool and returns. No material is ever created for a plate
    /// that never fades.</para>
    ///
    /// <para>WHY A SECOND MATERIAL AND NOT A COLOUR WRITE ON THE SHARED ONE. The shared plate
    /// material is <c>Blend One Zero</c> (see <see cref="EnsurePlateMaterial"/>) — genuinely opaque,
    /// so its colour's alpha channel is not read by anything and writing it would do nothing at all.
    /// Fading needs a different BLEND STATE, which is per-material, and the plate colour is shared
    /// by every plate in the scene, so the fading one gets its own instance. It also drops
    /// <c>_ZWrite</c>: a translucent surface that still stamped depth would occlude whatever it is
    /// supposed to be revealing, which is the opposite of a fade.</para>
    ///
    /// <para>The instance is built lazily on the first faded tick, kept for the life of the plate
    /// (a tooltip re-fades on every hover; churning a Material per hover is not free), re-tinted
    /// from the LIVE <see cref="_plateColor"/> so the key-avoidance lift applies to it too, and
    /// destroyed with the plate by <see cref="DestroyPlate"/>.</para>
    /// </summary>
    private static void ApplyPlateAlpha(Renderer? plate, ref Material? fadeMat, ref bool faded,
                                        float alpha)
    {
        if (plate == null)
            return;
        if (alpha >= PlateOpaqueAlpha)
        {
            if (!faded)
                return; // the overwhelmingly common path: nothing to do, nothing allocated
            faded = false;
            plate.sharedMaterial = _plateMat; // back to the shared opaque plate, depth write and all
            return;
        }
        fadeMat ??= CreateFadeMaterial();
        Color wanted = _plateColor;
        wanted.a = alpha;
        if (fadeMat.color != wanted)
            fadeMat.color = wanted;
        if (!faded)
        {
            faded = true;
            plate.sharedMaterial = fadeMat;
        }
    }

    /// <summary>The shared plate recipe with the blend flipped to straight alpha and the depth
    /// write dropped — see <see cref="ApplyPlateAlpha"/> for why both are necessary. At alpha 1 it
    /// composites identically to the opaque material (<c>src·1 + dst·0</c>), so the crossover in
    /// either direction is invisible.</summary>
    private static Material CreateFadeMaterial()
    {
        Material m = WorldUIAssets.CreateFlatMaterial(_plateColor, overlay: true);
        if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite", 0);      // never occlude what it reveals
        if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", 4);        // LEqual — hands/board still win
        if (m.HasProperty("_Cull")) m.SetInt("_Cull", 0);          // readable back side too
        if (m.HasProperty("_SrcBlend")) m.SetInt("_SrcBlend", 5);  // SrcAlpha         ┐ straight
        if (m.HasProperty("_DstBlend")) m.SetInt("_DstBlend", 10); // OneMinusSrcAlpha ┘ alpha blend
        m.renderQueue = PlateQueue;
        return m;
    }

    /// <summary>Drop a plate AND the fade-material instance it may own, in the one place that knows
    /// they belong together. Both arguments tolerate an already-destroyed (Unity-null) object, which
    /// is the normal case for a plate whose parent host died first.</summary>
    private static void DestroyPlate(Transform? plate, Material? fadeMat)
    {
        if (plate != null)
            Object.Destroy(plate.gameObject);
        if (fadeMat != null)
            Object.Destroy(fadeMat);
    }

    private static Transform CreatePlate(Transform parent)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = PlateObjectName;
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
