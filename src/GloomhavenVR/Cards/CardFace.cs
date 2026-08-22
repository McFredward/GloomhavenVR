using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// Adoption of a live game card face onto a VR card, with full restore.
///
/// Mechanism (ARCHITECTURE §5 "golden seam" #4, CARDS.md §3/§5): every
/// <c>AbilityCardUI</c> owns a self-contained <c>FullAbilityCard</c> child with its
/// own Canvas (<c>public Canvas Canvas</c>, FullAbilityCard.cs:96). The game itself
/// re-parents these at runtime — <c>CardsHandUI.ToggleFullCardsPreview</c> moves whole
/// cards into <c>FullCardHandViewer.CardContainer</c> (CardsHandUI.cs:345) and the
/// dialog flows re-parent <c>fullAbilityCard.gameObject</c> into popups
/// (CardsHandUI.cs:826/2069) — so re-hosting the face is a game-sanctioned operation.
/// We move ONLY the FullAbilityCard rect under a world-space canvas on the VR card;
/// the AbilityCardUI root (mini card, buttons, layout) stays in the suppressed 2D
/// hand so all game bookkeeping keeps working.
///
/// While adopted:
/// - <c>AbilityCardUI.LockFullCard = true</c> (verified property, AbilityCardUI.cs:163)
///   short-circuits <c>ToggleFullCardPreview</c> (early-out at AbilityCardUI.cs:1033),
///   preventing the game's hover preview from adding Canvases / moving the face.
/// - <see cref="Maintain"/> re-asserts parent/active/rect once per frame because
///   <c>UpdateView/ToggleFullCard</c> (CardsHandUI.cs:993) rewrite anchors and
///   deactivate the face whenever the game refreshes the (invisible) 2D hand.
///
/// <see cref="Restore"/> puts every captured value back — REQUIRED before the widget
/// returns to the game's object pool (see Patches/CardLifecyclePatches.cs).
/// </summary>
internal sealed class CardFace
{
    /// <summary>Centered anchor frame the host pose uses (anchors AND pivot).</summary>
    private static readonly Vector2 CenterAnchor = new(0.5f, 0.5f);

    /// <summary>
    /// Fraction the fitted face is shrunk by so the card mesh's rounded dark front
    /// shows as a thin outline around the art (test #24: "the black border is WAY too
    /// big"). 0.06 leaves ~3% margin on every edge — ≈1.9 mm on a 63.5 mm card — a
    /// tasteful rounded border, NOT the old fat black frame. See <see cref="RefreshFitScale"/>.
    /// </summary>
    private const float BorderFraction = 0.06f;

    // ------------------------------------------------- the VISIBLE FACE RECT (shared) --
    //
    // WHAT THIS IS AND WHY IT IS PUBLISHED. A card SLAB is nominally 63.5 × 88 mm (poker,
    // CardsConfig.CardWidth/CardHeight). The card FACE is a uGUI rect of a completely different
    // aspect — the game's FullAbilityCard measures 294 × 450 px, aspect 0.6533 against the slab's
    // 0.7216 — and it is fitted into the slab with Mathf.Min (letterbox, never crop) and then inset
    // by BorderFraction. So the rectangle the face actually PAINTS is strictly smaller than the
    // nominal slab, and by a different amount on each axis.
    //
    // The LOCAL card has always closed that gap by shrinking the BODY to the painted rect rather
    // than by growing the face: VRCard.SetCanvasSize scales the backing mesh to
    // facePixels × fit × VisibleFaceFraction, so slab and face are the same rectangle and only the
    // physical rim shows. That arithmetic lived inline in VRCard with a hand-copied 0.94 constant
    // and a comment asking the next reader to keep the two files in sync.
    //
    // It was not kept in sync — because the second consumer was in another module and nobody knew
    // it existed. Net/RemoteHandFan built its ghost slabs at the FULL nominal 63.5 × 88 and let
    // RemoteCardArt letterbox the cloned face inside them, so a peer's card showed its own card
    // BACK as a rim all the way around the printed face (user report 12, 2026-08-15: "Die remote
    // Handkarten Vorderseiten werden etwas zu klein angezeigt, so dass sie nicht perfekt auf dem
    // mesh liegen und der Hintergrund am rand durchscheint", .planning/debug/remote_faecher.jpg).
    // The same numbers also made a peer's card 17.5 % WIDER than the owner's own — a 1:1 defect
    // that no screenshot of a single machine could show.
    //
    // So the rect is computed HERE, once, from the same BorderFraction the face is drawn with, and
    // both consumers ask for it. A future third surface asks the same question and cannot drift.

    /// <summary>The face pixel size assumed before any real face has been adopted — VRCard's
    /// build-time placeholder, kept identical so a card never resizes for the wrong reason.</summary>
    internal static readonly Vector2 DefaultFacePixels = new(270f, 400f);

    /// <summary>Fraction of the fitted face rect the visible art fills (1 − <see cref="BorderFraction"/>).
    /// Bodies and grab colliders are fit to THIS, not to the full nominal card rect.</summary>
    internal static float VisibleFaceFraction => 1f - BorderFraction;

    /// <summary>
    /// The pixel size of the LAST ability face this client actually hosted (294 × 450 on the
    /// shipped widget), seeded to <see cref="DefaultFacePixels"/> until one has been. It is a
    /// LOCAL observation of this client's own card widget — the same prefab every player's cards
    /// are built from — so a remote surface can size its slab to the face it is about to clone
    /// WITHOUT asking anything about the peer, and in particular without a wire field.
    /// </summary>
    internal static Vector2 ObservedFacePixels { get; private set; } = DefaultFacePixels;

    /// <summary>Bumped whenever <see cref="ObservedFacePixels"/> actually changes, so a consumer
    /// that BAKED the rect into a mesh scale can notice and rebuild without comparing floats.</summary>
    internal static int FacePixelsRevision { get; private set; }

    /// <summary>
    /// The world-space rectangle a fitted card face actually paints inside a nominal
    /// <paramref name="slabWidth"/> × <paramref name="slabHeight"/> card — i.e. the rectangle a
    /// card BODY has to be, if no background is to show around the printed face.
    /// <c>facePixels × Min(w/px.x, h/px.y) × VisibleFaceFraction</c>, term for term what
    /// <c>VRCard.SetCanvasSize</c> fits the local backing to.
    /// </summary>
    internal static Vector2 VisibleFaceRect(float slabWidth, float slabHeight, Vector2 facePixels)
    {
        if (facePixels.x <= 1f || facePixels.y <= 1f)
            facePixels = DefaultFacePixels;
        if (slabWidth <= 0f || slabHeight <= 0f)
            return facePixels;
        float fit = Mathf.Min(slabWidth / facePixels.x, slabHeight / facePixels.y);
        float k = VisibleFaceFraction;
        return new Vector2(facePixels.x * fit * k, facePixels.y * fit * k);
    }

    /// <summary>The same rect against <see cref="ObservedFacePixels"/> — the overload a surface
    /// that does not hold a face of its own (a remote ghost slab) asks.</summary>
    internal static Vector2 VisibleFaceRect(float slabWidth, float slabHeight) =>
        VisibleFaceRect(slabWidth, slabHeight, ObservedFacePixels);

    /// <summary>Record the real face pixel size the first time (and any time) it changes. Called
    /// from <see cref="Adopt"/>, the one place this client learns it from the game.</summary>
    private static void NoteFacePixels(Vector2 size)
    {
        if (size.x <= 1f || size.y <= 1f)
            return;
        if (Mathf.Approximately(size.x, ObservedFacePixels.x) && Mathf.Approximately(size.y, ObservedFacePixels.y))
            return;
        Vector2 was = ObservedFacePixels;
        ObservedFacePixels = size;
        FacePixelsRevision++;
        Vector2 vis = VisibleFaceRect(CardsConfig.CardWidth.Value, CardsConfig.CardHeight, size);
        VRLog.Info("Cards", $"CARD FACE RECT: the hosted ability face measures {size.x:F0}x{size.y:F0} px "
            + $"(was {was.x:F0}x{was.y:F0}). Letterboxed into a "
            + $"{CardsConfig.CardWidth.Value * 1000f:F1}x{CardsConfig.CardHeight * 1000f:F1} mm card and inset by "
            + $"{BorderFraction * 100f:F0} %, the face PAINTS {vis.x * 1000f:F2}x{vis.y * 1000f:F2} mm — which is "
            + "the size every card BODY (local backing and remote ghost slab alike) is scaled to, so no "
            + "background can show around the print.");
    }

    private AbilityCardUI? _owner;
    private RectTransform? _face;
    private RectTransform? _host;

    // Captured original state.
    private Transform? _origParent;
    private int _origSibling;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private Vector2 _origAnchorMin;
    private Vector2 _origAnchorMax;
    private Vector2 _origPivot;
    private Vector2 _origAnchoredPos;
    private bool _origActive;
    private bool _origLock;

    private float _fitScale = 1f;
    // Host size the fit scale was last computed against — the host canvas is resized
    // to the real face pixels AFTER Adopt (VRCard.SetCanvasSize), so the fit must be
    // recomputed once the true host size is known (test #24 fat-border bug).
    private Vector2 _fitHostSize;

    // Test #22: change-dedup for the burn/lose-confirm re-claim log (Maintain).
    private bool _reclaimedFromDialog;

    internal bool IsAdopted => _face != null;

    internal AbilityCardUI? Owner => _owner;

    /// <summary>Pixel size of the face rect (for host canvas sizing).</summary>
    internal Vector2 FaceSize { get; private set; } = new(270f, 400f);

    /// <summary>
    /// Re-parent the live face under <paramref name="host"/> (a world-space canvas
    /// rect). Returns false if the widget has no usable face.
    /// </summary>
    internal bool Adopt(AbilityCardUI owner, RectTransform host)
    {
        if (owner == null || owner.fullAbilityCard == null)
            return false;

        RectTransform? face = owner.fullAbilityCard.RectTransform;
        if (face == null)
            face = owner.fullAbilityCard.transform as RectTransform;
        if (face == null)
            return false;

        _owner = owner;
        _face = face;
        _host = host;
        _reclaimedFromDialog = false;

        _origParent = face.parent;
        _origSibling = face.GetSiblingIndex();
        _origLocalPos = face.localPosition;
        _origLocalRot = face.localRotation;
        _origLocalScale = face.localScale;
        _origAnchorMin = face.anchorMin;
        _origAnchorMax = face.anchorMax;
        _origPivot = face.pivot;
        _origAnchoredPos = face.anchoredPosition;
        _origActive = face.gameObject.activeSelf;
        _origLock = owner.LockFullCard;

        owner.LockFullCard = true;

        // REGISTER BEFORE ANY PUMP RUNS, and the order is load-bearing rather than tidy. This
        // registry is the ONE answer to "is this face a live game widget or a copy the mod built
        // for itself" (CardArtGuard.IsAdopted), and CardHalfTone — which rides the very same
        // per-face pump a few lines down — WRITES to a face it believes is a mod-owned copy. The
        // face has already been re-parented out from under its AbilityCardUI by then, so an
        // unregistered adopted face is indistinguishable from a clone: registering after the first
        // Rescan would hand the game's own widget to a writer that must never touch it.
        CardArtGuard.NoteAdopted(owner.fullAbilityCard);
        _nextArtTick = Time.unscaledTime + CardArtGuard.TickIntervalSeconds;

        Vector2 size = face.rect.size;
        if (size.x > 1f && size.y > 1f)
            FaceSize = size;
        // …and publish it: this is the ONE place the mod learns the real face pixel size from the
        // game, and the remote ghost slabs need it to size their bodies to the face they clone.
        NoteFacePixels(size);
        RefreshFitScale();

        ApplyHostPose();

        // Test #25 / 2026-08-11: the card art has an artistic, non-rectangular outline.
        // The capture is OFFERED from CardFaceMipBake.Rescan — the one per-face pump that
        // both card kinds already run — so there is no separate call here; the Rescan a
        // few lines down is what drives it. See the silhouette section below.

        // Aliasing round 3: log (once) what the world-space card canvas ACTUALLY samples.
        LogFaceTextureDiag(owner);

        // T3 mip bake: swap the face's mipless-atlas sprites for mip-baked equivalents
        // (guarded inside; art loads async so the arrival watch below catches the rest).
        CardFaceMipBake.Rescan(owner.fullAbilityCard);
        _nextMipRescan = Time.unscaledTime + MipRescanInterval;
        // ...and arm the per-frame ARRIVAL WATCH that makes the swap land BEFORE the art's
        // first rendered frame (see MaintainArtArrival — this is the fix for "man sieht für
        // ca. 1 Sekunde die Variante mit Aliasing" on a character switch).
        _artWatch.Capture(owner.fullAbilityCard);

        // (WHITE DECISION-PHASE FACES: from here on this face is re-activated by Maintain
        // whenever the game's pick-mode UpdateView deactivates it, and every such cycle
        // re-enters the game's addressable card-art loader. CardArtGuard — registered above,
        // before the first pump — stops an in-flight load from being restarted (which nulls the
        // action-half sprites) and heals/replays afterwards; see CardArtGuard's class doc.)

        return true;
    }

    /// <summary>T3: BACKSTOP cadence for re-running the sprite swap while adopted. It used to
    /// be the ONLY re-scan, and that is exactly what the player saw: "man sieht wenn man neue
    /// Karten auflegt (zB beim Wechsel des Characters) immer für ca. 1 Sekunde die Variante mit
    /// Aliasing" — see <see cref="CardArtWatch"/> for the measured chain and the fix.
    /// It stays because it is the one pass that also re-captures the watch array when the game
    /// grows the face hierarchy; it is a no-op whenever the arrival watch already swapped.</summary>
    private const float MipRescanInterval = 1f;
    private float _nextMipRescan;

    // ----------------------------------------------------- card-art arrival watch --

    /// <summary>
    /// The per-frame art-arrival watch that makes the mip-baked sprite the card's FIRST rendered
    /// pixels rather than its second — see <see cref="CardArtWatch"/> for the measured chain that
    /// produced the reported "ca. 1 Sekunde die Variante mit Aliasing". Re-captured on adoption
    /// and on the <see cref="MipRescanInterval"/> backstop, dropped whenever the face stops being
    /// ours.
    /// </summary>
    private readonly CardArtWatch _artWatch = new();

    /// <summary>
    /// ZERO-ALIASED-FRAME SWAP, run from <c>VRCard.LateUpdate</c> once per frame per adopted card.
    /// LateUpdate on purpose: the loader's continuations run inside the Update phase and uGUI
    /// builds the canvas after LateUpdate, so a swap issued here is always in place before the
    /// art's first rendered frame. See <see cref="CardArtWatch"/> for the whole rationale.
    /// </summary>
    internal void MaintainArtArrival()
    {
        if (_face == null || _owner == null || _owner.fullAbilityCard == null)
            return;
        if (_artWatch.Poll("card face") > 0)
        {
            _nextMipRescan = Time.unscaledTime + MipRescanInterval; // it just did the backstop's job
            // ART JUST ARRIVED — the SAME frame the game assigned it, while the addressable loader
            // still has the Image disabled, so nothing here has been drawn yet. Two things ride
            // this seam (2026-08-11: "der Prozess der 'Ausblendung' soll auch nicht sichtbar sein"):
            // the face BLACKOUT, which mutes this face's black backdrop quad before its first
            // rendered frame — including after a CHARACTER SWITCH, where the same pooled card gets
            // new art — and, on a cold cache only, the one live capture that learns the shape.
            Offer(_owner.fullAbilityCard, artJustArrived: true);
        }
    }

    /// <summary>Next unscaled time <see cref="CardArtGuard.Tick"/> runs for this face (replay of a
    /// suppressed ShowCard + heal of an action half left on a null sprite).</summary>
    private float _nextArtTick;

    // ------------------------------------------------- rendered-texture diagnostics --

    /// <summary>
    /// Latched once an adoption had sprite textures to report (card art loads async, so
    /// the first adoptions may see none — keep retrying until one does).
    /// </summary>
    private static bool s_texDiagLogged;

    /// <summary>
    /// One-shot diag for the card-shimmer investigation (aliasing round 3): the RENDERED
    /// card face is the adopted live uGUI, so the textures that matter are the game's own
    /// sprite atlases those Images sample — not anything the mod creates. Log their
    /// mip/aniso/filter state so the hardware log PROVES whether texture-space AA is even
    /// possible: mipmapCount == 1 means the atlas ships mipless, minification shimmer is
    /// baked into the data, and the only honest fix is eye-texture supersampling
    /// ([RenderQuality] EyeResolutionScale).
    /// </summary>
    private static void LogFaceTextureDiag(AbilityCardUI owner)
    {
        if (s_texDiagLogged)
            return;
        try
        {
            FullAbilityCard? faceCard = owner.fullAbilityCard;
            if (faceCard == null)
                return;
            var seen = new HashSet<int>();
            var sb = new System.Text.StringBuilder(256);
            int count = 0;
            Image[] images = faceCard.GetComponentsInChildren<Image>(includeInactive: false);
            foreach (Image img in images)
            {
                Sprite? sprite = img != null ? img.sprite : null;
                Texture2D? tex = sprite != null ? sprite.texture : null;
                if (tex == null || !seen.Add(tex.GetInstanceID()))
                    continue;
                if (count > 0)
                    sb.Append(", ");
                sb.Append('\'').Append(tex.name).Append("' ").Append(tex.width).Append('x')
                  .Append(tex.height).Append(" mips=").Append(tex.mipmapCount)
                  .Append(" aniso=").Append(tex.anisoLevel).Append(' ').Append(tex.filterMode);
                if (++count >= 8)
                    break;
            }
            if (count == 0)
                return; // art still loading async — retry on a later adoption
            s_texDiagLogged = true;
            VRLog.Info("Cards", "FACE TEXTURE DIAG (game atlases the adopted card canvas samples): " +
                                $"{sb} — mips=1 ⇒ the source atlas is MIPLESS: texture-space shimmer " +
                                "cannot be fixed camera-side (MSAA/supersampling can't help). " +
                                "[Cards] FaceMipBake (T3) now swaps these sprites onto mip-baked " +
                                "trilinear/aniso copies — see the MIP BAKE log lines for what was baked.");
        }
        catch (System.Exception ex)
        {
            s_texDiagLogged = true; // never spam a throwing path
            VRLog.Warn("Cards", $"Face texture diag skipped ({ex.Message}).");
        }
    }

    // ------------------------------------------------------- silhouette capture --

    /// <summary>
    /// SILHOUETTE CAPTURE — the one measurement the whole card-shape pipeline runs on.
    ///
    /// The card ART carries the card's real, non-rectangular outline in its ALPHA channel
    /// (the class background sprite, e.g. 'AC_Berserker_Background', is authored with the
    /// ornate silhouette; everything outside it is transparent). The capture stamps that
    /// alpha — the STOCK art's designed alpha, nothing else — into a card-space footprint,
    /// <c>CardMesh.SetSilhouette</c> persists it (see <c>CardMesh.CacheVersion</c>) and
    /// derives from it the contour the card BODY mesh is punched out to
    /// (<c>CardContour</c>/<c>CardMesh.AttachBody</c>). Body and face therefore end on the
    /// same line BY CONSTRUCTION: the face renders the stock art, the body backs exactly the
    /// pixels the art draws at alpha >= 0.5 — including the printed frame and the
    /// initiative-chip protrusion at the card's bottom edge.
    ///
    /// HISTORY, in one line: 17 rounds of material clips, sprite punches, face crops and
    /// shader probes tried to shape the card before the geometric answer landed — see the
    /// NetProtocol build notes for ModBuild 105-121. Two rules survived them all:
    ///  • DEGRADE TO THE RECTANGLE, never to a wrong shape — every gate that refuses keeps
    ///    the rounded-rect slab and names itself in the log;
    ///  • NO VISIBLE TRANSITION — the footprint is persisted and re-applied inside the card
    ///    body's material factory, before the first card of a launch draws
    ///    (<c>CardMesh.EnsureSilhouetteCacheLoaded</c>); only the very first launch learns
    ///    the shape live.
    ///
    /// MECHANICS that are load-bearing today:
    ///  • ACTIVE-STATE INDEPENDENCE: candidates are tested with
    ///    <c>img.enabled &amp;&amp; img.gameObject.activeSelf</c>, never
    ///    <c>isActiveAndEnabled</c> — the first adoption happens while the card is parked
    ///    under the INACTIVE <c>VRCardFactory.PoolRoot</c>, whose images are perfectly
    ///    readable.
    ///  • RETRY DRIVEN BY ART, NOT ADOPTION COUNT: card art loads async; each offer hashes
    ///    the qualifying (image, sprite) set and only a CHANGED set costs a GPU readback.
    ///  • THE DRAWN RECT, NOT THE LAYOUT RECT (<see cref="DrawnLocalRect"/>):
    ///    <c>Image.preserveAspect</c> letterboxes the sprite inside its rect; stamping
    ///    across the layout rect once mis-aimed the mask by ~10 % on the short axis.
    ///  • THE FULL-BLEED GATE (<see cref="MinOutlineCoverage"/>): what defines the outline
    ///    must demonstrably SPAN the card — one candidate or the candidates' union — so the
    ///    capture can never latch an inner panel, whatever the loader's timing.
    ///  • TWO SHAPES, ONE MECHANISM: keyed by <see cref="CardBodyKind"/> (poker-aspect
    ///    ability cards, near-square item cards); the entry point <see cref="Offer"/> rides
    ///    <c>CardFaceMipBake.Rescan</c>, the one per-face pump both kinds already run.
    ///  • Interior designed holes cannot pierce the body: <c>CardContour.Extract</c> keeps
    ///    the LARGEST closed loop of the 0.5 iso, and semi-transparent fringes fall on the
    ///    right side of the same 0.5 threshold.
    /// </summary>
    private sealed class SilhouetteState
    {
        /// <summary>The one live capture this session owed for this kind is done — either it landed
        /// the shape, or it confirmed/refreshed the persisted cache. Nothing further is measured.
        /// (The APPLIED mask itself lives in <see cref="CardMesh"/>, which is what serves it to
        /// every card body and to the blackout, cache-loaded or captured.)</summary>
        internal bool CaptureSettled;

        internal int Attempts;
        internal int Hash;
        internal float NextAttemptTime;
        internal string? LastReason;
        internal bool GaveUpLogged;

        /// <summary>How many offers the full-bleed gate has turned away (see
        /// <see cref="MinOutlineCoverage"/>). Diagnostic only — it does NOT decide anything. It
        /// used to drive a "after N waits, take the largest candidate" fallback, and that was
        /// removed: a 24-card fan produces 24 offers in ONE frame, so the counter could run out
        /// before any art had loaded and latch an inner panel as the card's outline.</summary>
        internal int CoverageWaits;

        /// <summary>Instance ids of the Images the footprint was UNIONED from. Those carry the
        /// card's shape by definition and are never muted.</summary>
        internal readonly HashSet<int> OutlineImageIds = new();
    }

    private static readonly SilhouetteState[] s_silhouette =
    {
        new(), new(), new(), // indexed by CardBodyKind (Neutral slot unused)
    };

    /// <summary>One "this kind's face reached the capture at all" line per kind. Without it, a log
    /// with no lines for a kind is ambiguous between "never hosted" and "silently broken".</summary>
    private static readonly bool[] s_offerLogged = new bool[3];

    /// <summary>Hard ceiling on capture attempts per card kind. An attempt only happens when the
    /// qualifying sprite set actually changed, and every card carries different image instances,
    /// so without a ceiling a 24-card fan could pay 24 readback rounds for the same answer. Every
    /// card of a class shares the frame art the outline comes from, so if the first few cannot
    /// yield an outline neither can the rest.</summary>
    private const int MaxSilhouetteAttempts = 8;

    /// <summary>Minimum unscaled seconds between two REAL attempts (ones that reach the GPU
    /// readback). A hand fan builds all its cards in one frame; this spreads any repeat cost
    /// across frames instead of stacking it into a single 11.1 ms budget.</summary>
    private const float SilhouetteAttemptInterval = 0.5f;

    /// <summary>Footprint resolution (card-space). ~224 px wide keeps the ornate curve
    /// crisp at fan distance while the one-shot CPU cost stays trivial.</summary>
    private const int FootprintWidth = 224;

    /// <summary>Smallest share of the face rect an <c>Image</c> must cover to count toward the
    /// OUTER outline. Icons, XP orbs and enhancement slots sit far inside it and can only add
    /// noise.</summary>
    private const float MinOutlineAreaFraction = 0.03f;

    /// <summary>
    /// An image at least this large that is ALSO opaque at every probe point is a plain
    /// rectangular backdrop, not the card's shape. Unioned in, it would erase the silhouette and
    /// the result would be rejected as "solid" — so it is skipped. Safe by construction: if the
    /// card genuinely has no outline beyond such a backdrop, what remains is sparse and the
    /// sanity guard keeps the rounded rect, which is exactly the old look.
    /// </summary>
    private const float BackdropAreaFraction = 0.85f;

    /// <summary>
    /// Minimum share of the face ONE candidate must cover before the union is allowed to define
    /// the card's OUTER outline at all — "the card's own background art is here". Deliberately
    /// below <see cref="BackdropAreaFraction"/>: a background whose art sits inside a small shadow
    /// margin still qualifies, while the biggest INNER element on an ability face (an action half,
    /// ~50 % of the card) cannot. The gate is timing-independent, which is the point — it does not
    /// care WHEN the background arrives, only that it has.
    ///
    /// <para>ROUND 7 NOTE — the coverage a full-bleed background reports is now its DRAWN area, not
    /// its layout area (<see cref="DrawnLocalRect"/>), so the reported ability-card background drops
    /// from 100 % to ~90.5 %: the same image, measured where uGUI actually puts it. 0.7 keeps ample
    /// headroom, and the gate's real job — "an action half at ~50 % of the card can never define the
    /// outline" — is unchanged. A card whose art were letterboxed below 70 % would be refused and
    /// keep the rounded rect, which is the standing degrade-to-today rule, and the ART RECT log line
    /// would name the number.</para>
    /// </summary>
    private const float MinOutlineCoverage = 0.7f;

    /// <summary>
    /// Offer a live card face to the silhouette capture AND to the face blackout. Cheap and safe
    /// to call every frame: the capture skips its readback entirely while the face's qualifying
    /// sprite set is unchanged, and the blackout is rate-limited per face. Classifies by the game
    /// component itself — on the root, else on the first one found among its CHILDREN, which is
    /// what makes a peer's cloned card front (a card widget hosted under a bare world-space canvas,
    /// <c>Net/RemoteCardArt</c>) reachable. A root with no card component anywhere below it is
    /// ignored.
    ///
    /// <para><paramref name="artJustArrived"/> is the ZERO-VISIBLE-FRAME path and the reason this
    /// method takes an argument at all (2026-08-11: "Der Prozess der 'Ausblendung' soll auch nicht
    /// sichtbar sein"). <see cref="CardArtWatch"/> fires in the SAME frame the game assigns a
    /// card's art — while the addressable loader still has the Image disabled, i.e. strictly before
    /// that art's first rendered frame — and <c>CardFaceMipBake</c> has been riding that seam since
    /// T3 to make the mip-baked sprite the card's first pixels rather than its second. The blackout
    /// rides the same seam: on arrival it bypasses its own rate limit, so a face that gains a black
    /// backdrop quad (a fresh card, or the same pooled card after a CHARACTER SWITCH) is muted
    /// before it is ever drawn with one. The periodic pass stays only as a self-heal for the game
    /// recolouring a quad later — a backstop, never the mechanism.</para>
    /// </summary>
    internal static void Offer(Component? faceRoot, bool artJustArrived = false)
    {
        if (faceRoot == null)
            return;
        try
        {
            CardBodyKind kind;
            RectTransform? root;
            // Unity-null aware (a destroyed component is != null to C# but == null to Unity).
            FullAbilityCard? ability = faceRoot.GetComponent<FullAbilityCard>();
            ItemCardUI? item = ability != null ? null : faceRoot.GetComponent<ItemCardUI>();
            if (ability == null && item == null)
            {
                // A HOST, NOT A CARD — and until 2026-08-11 round 2 that was a silent return, which
                // is precisely how the MULTIPLAYER half of the requirement fell out ("UND auch alle
                // Karten genauso die remote angezeigt werden im Multiplayer bei anderen Spielern").
                // A peer's card FRONT is a throwaway Instantiate of the game's own card widget
                // hosted on a world-space canvas (Net/RemoteCardArt), and the only pump that reaches
                // it — Net/RemoteCardArt.RescanMips → CardFaceMipBake.Rescan — passes the CANVAS,
                // whose GameObject carries no card component; the clone is its CHILD. Rescan's own
                // GetComponentsInChildren walk therefore covered the peer's face while this
                // classification did not, so the peer kept the full black rectangle that the local
                // cards had already lost. Resolving through the children closes that with no change
                // in Net/** at all, and it can never mis-fire: the components looked for ARE the
                // game's two card widgets, so anything that has one IS a card face.
                //
                // includeInactive on purpose — RemoteCardArt configures and fits the clone BEFORE it
                // activates the host, which is the one moment a mute lands before a first drawn frame.
                ability = faceRoot.GetComponentInChildren<FullAbilityCard>(includeInactive: true);
                if (ability == null)
                    item = faceRoot.GetComponentInChildren<ItemCardUI>(includeInactive: true);
            }
            if (ability != null)
            {
                kind = CardBodyKind.Ability;
                root = ability.RectTransform != null
                    ? ability.RectTransform
                    : ability.transform as RectTransform;

                // THE HALF TONE, on the same pump and for the same reason as the blackout below:
                // this is the ONE place both ability-face paths already meet (the adopted local
                // widget and a mod-built clone), so the "greyer in the selectable areas" report is
                // answered once here instead of in each surface. Writes only to faces the mod owns
                // outright and measures the rest — see CardHalfTone's class doc.
                CardHalfTone.Observe(ability);
            }
            else if (item != null)
            {
                kind = CardBodyKind.Item;
                // The CARD's rect, never the host canvas's — the blackout probes a quad's coverage
                // against this rect, and measuring against a host of a different size would place
                // every probe on the wrong footprint texel.
                root = item.transform as RectTransform;
            }
            else
            {
                return;
            }
            if (root == null)
            {
                // Was a silent return. The ModBuild-108 log contains NOT ONE "(Item)" line of any
                // kind, and the reason turned out to be that no item card was ever hosted that
                // session (zero "ITEM CARD host" / "ITEM #1" lines either) — but a silent branch
                // meant we could not tell that from "the item path is broken" without a second run.
                if (!s_offerLogged[(int)kind])
                {
                    s_offerLogged[(int)kind] = true;
                    VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): a face was offered but its root is not " +
                                        $"a RectTransform ('{faceRoot.name}') — this kind cannot be captured " +
                                        "from it.");
                }
                return;
            }
            if (!s_offerLogged[(int)kind])
            {
                s_offerLogged[(int)kind] = true;
                VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): first face offered ('{faceRoot.name}', rect " +
                                    $"{root.rect.width:F0}x{root.rect.height:F0} px). If a later hardware log " +
                                    "has no line for a kind AT ALL, that kind's face was never hosted in that " +
                                    "session — not that its capture failed.");
            }

            SilhouetteState state = s_silhouette[(int)kind];

            // (1) THE BLACKOUT, AND ALWAYS. Its input is the APPLIED mask, which — from the
            // second launch onward — CardMesh loaded from the persisted cache before the first card
            // body's material existed. So on the arrival seam this runs with a mask already in hand
            // and the face is muted before its first rendered frame. Deliberately not gated on
            // state.Applied: this session may never capture anything and still have the mask.
            byte[]? mask = CardMesh.Footprint(kind, out int mw, out int mh);
            FaceBlackout.Maintain(root, kind, mask, mw, mh, state.OutlineImageIds, artJustArrived);

            // (2) THE CAPTURE. Runs at most ONCE per session per kind even when a mask is already
            // applied: with nothing applied it lands the shape, and with the cache applied it only
            // verifies/refreshes the FILE (see CardMesh.RefreshSilhouetteCache) so a character class
            // whose card frame really differs corrects itself on the next launch instead of snapping
            // mid-session.
            if (state.CaptureSettled)
                return;
            if (state.Attempts >= MaxSilhouetteAttempts)
            {
                if (!state.GaveUpLogged)
                {
                    state.GaveUpLogged = true;
                    VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): giving up after {state.Attempts} " +
                                        $"attempt(s), last reason '{state.LastReason ?? "n/a"}' — that shape " +
                                        $"keeps {(CardMesh.SilhouetteApplied(kind) ? "the mask it already has" : "the opaque rounded-rect body")} " +
                                        "(no wrong shape is ever shown; the log line above names the gate).");
                }
                return;
            }
            TryCapture(root, kind, state);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CARD SILHOUETTE offer skipped ({ex.GetType().Name}: {ex.Message}) — " +
                                "kept the rounded-rect card slab.");
        }
    }

    /// <summary>
    /// Hand a face back to the game with every quad the round-2 blackout muted restored — the
    /// full-restore contract for a card kind whose face is NOT owned by this class. <c>ItemsPile</c>
    /// recycles its hosted <c>ItemCardUI</c> into the game's own <c>ObjectPool</c> and must call
    /// this first. Idempotent and safe on a face that was never touched.
    /// </summary>
    internal static void ReleaseFaceBlackout(Component? faceRoot) => FaceBlackout.Restore(faceRoot);

    /// <summary>
    /// Log a rejection once per distinct KIND OF reason per card kind. Deduping on a short
    /// <paramref name="code"/> rather than on the full text is deliberate: the detail carries
    /// per-card counts, so text dedup would still print once per card and a fan adopts up to 24
    /// at a time. A NEW code is information; the same code again is not.
    /// </summary>
    private static void Reject(SilhouetteState state, CardBodyKind kind, string code, string detail)
    {
        if (state.LastReason == code)
            return;
        state.LastReason = code;
        VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): no footprint yet [{code}] — {detail}. " +
                            "The body keeps its opaque rounded-rect shape until a usable outline appears.");
    }

    /// <summary>
    /// Capture the live card art's opacity footprint (card-space alpha) for one card kind and
    /// drive <see cref="CardMesh.SetSilhouette"/>. We union the alpha of the card's larger
    /// <c>Image</c> sprites (the class-skin backgrounds / frame that define the outer outline),
    /// each sampled by GPU blit → sub-rect readback so it works even for non-CPU-readable atlas
    /// textures. Robust: any failure just leaves the opaque rounded-rect slab in place.
    /// </summary>
    private static void TryCapture(RectTransform faceRoot, CardBodyKind kind, SilhouetteState state)
    {
        Rect faceRect = faceRoot.rect;
        if (faceRect.width < 1f || faceRect.height < 1f)
        {
            Reject(state, kind, "degenerate-rect",
                   $"the face rect measures {faceRect.width:F1}x{faceRect.height:F1} px");
            return;
        }

        Image[] images = faceRoot.GetComponentsInChildren<Image>(includeInactive: true);
        if (images.Length == 0)
        {
            Reject(state, kind, "no-images", "the face carries no Image at all");
            return;
        }

        // --- pass 1: which images can carry the OUTER outline, and has that set changed? ------
        var picks = new List<(Image Img, Rect Norm)>(4);
        int hash = 17;
        int rejectedInactive = 0, rejectedTiny = 0, rejectedFaint = 0, rejectedType = 0, rejectedNoSprite = 0;
        int aspectCorrected = 0;
        bool hasFullBleed = false;
        float largestPick = 0f;
        float unionMinX = 1f, unionMinY = 1f, unionMaxX = 0f, unionMaxY = 0f;
        var corners = new Vector3[4];
        foreach (Image img in images)
        {
            if (img == null)
                continue;
            // ACTIVE-STATE INDEPENDENCE (see the class note): activeSelf, never
            // isActiveAndEnabled — a card parked under the inactive VRCardFactory.PoolRoot is
            // exactly where the first adoption happens, and its images are perfectly readable.
            if (!img.enabled || !img.gameObject.activeSelf)
            {
                rejectedInactive++;
                continue;
            }
            Sprite sprite = img.sprite;
            if (sprite == null || sprite.texture == null)
            {
                rejectedNoSprite++;
                continue;
            }
            // Only Simple images map their sprite 1:1 onto their rect; a 9-sliced or tiled
            // image would put the alpha somewhere else entirely, and a wrong outline is worse
            // than none.
            if (img.type != Image.Type.Simple)
            {
                rejectedType++;
                continue;
            }
            if (img.color.a < 0.2f)
            {
                rejectedFaint++;
                continue;
            }

            // THE DRAWN RECT, NOT THE LAYOUT RECT (round 7 — see DrawnLocalRect). GetWorldCorners
            // reports the RectTransform's rect; uGUI draws a preserveAspect Image LETTERBOXED
            // inside that rect. Stamping the sprite across the layout rect would place the card's
            // own outline where the art is not, which is the whole defect.
            Rect drawnLocal = DrawnLocalRect(img, sprite, out float aspectShrink);
            RectTransform irt = img.rectTransform;
            corners[0] = irt.TransformPoint(new Vector3(drawnLocal.xMin, drawnLocal.yMin, 0f));
            corners[1] = irt.TransformPoint(new Vector3(drawnLocal.xMin, drawnLocal.yMax, 0f));
            corners[2] = irt.TransformPoint(new Vector3(drawnLocal.xMax, drawnLocal.yMax, 0f));
            corners[3] = irt.TransformPoint(new Vector3(drawnLocal.xMax, drawnLocal.yMin, 0f));
            float minNx = 1f, minNy = 1f, maxNx = 0f, maxNy = 0f;
            for (int c = 0; c < 4; c++)
            {
                Vector3 local = faceRoot.InverseTransformPoint(corners[c]);
                float nx = (local.x - faceRect.xMin) / faceRect.width;
                float ny = (local.y - faceRect.yMin) / faceRect.height;
                if (nx < minNx) minNx = nx;
                if (nx > maxNx) maxNx = nx;
                if (ny < minNy) minNy = ny;
                if (ny > maxNy) maxNy = ny;
            }
            float aw = maxNx - minNx, ah = maxNy - minNy;
            if (aw <= 0.001f || ah <= 0.001f || aw * ah < MinOutlineAreaFraction)
            {
                rejectedTiny++;
                continue;
            }

            picks.Add((img, new Rect(minNx, minNy, aw, ah)));
            // Counted HERE, not where the shrink is computed: the ART RECT line reports
            // "{aspectCorrected} of {picks.Count} candidate(s)", so only images that actually
            // BECOME candidates may count. The ModBuild-111 log's impossible "2 of 1" was a
            // letterboxed image that the size gate then rejected — measured, counted, never a
            // candidate. Diagnostics are load-bearing in this project; the numbers must be true.
            if (aspectShrink < 0.999f)
                aspectCorrected++;
            float area = aw * ah;
            if (area > largestPick)
                largestPick = area;
            if (area >= MinOutlineCoverage)
                hasFullBleed = true;
            // Union bounding box of every candidate — the OTHER way a face can legitimately reach
            // the card's outer outline (see the gate below).
            if (minNx < unionMinX) unionMinX = minNx;
            if (maxNx > unionMaxX) unionMaxX = maxNx;
            if (minNy < unionMinY) unionMinY = minNy;
            if (maxNy > unionMaxY) unionMaxY = maxNy;
            hash = hash * 31 + img.GetInstanceID();
            hash = hash * 31 + sprite.GetInstanceID();
        }
        float unionCoverage = (unionMaxX > unionMinX && unionMaxY > unionMinY)
            ? (unionMaxX - unionMinX) * (unionMaxY - unionMinY)
            : 0f;

        if (picks.Count == 0)
        {
            Reject(state, kind, "no-candidate",
                   $"none of {images.Length} image(s) can carry the outer outline (rejected: " +
                   $"{rejectedInactive} not drawn, {rejectedNoSprite} without a sprite, {rejectedType} " +
                   $"non-Simple, {rejectedFaint} near-transparent, {rejectedTiny} smaller than " +
                   $"{MinOutlineAreaFraction:P0} of the face) — the card's background art has most " +
                   "likely not finished loading");
            return;
        }
        // FULL-BLEED GATE — the one-shot may only be spent on a footprint whose union contains the
        // card's OWN near-full-bleed background art. See the <see cref="SilhouetteState"/> note:
        // the ModBuild-108 capture fired in the frame the background arrived and reported "1 of 1
        // candidate", which LOOKED like a fragile single-source guess and was in fact the correct
        // and complete source ('AC_Berserker_Background' 1254x1916 = the face's 294x450 aspect).
        // What would have been a real defect is the same code firing one frame EARLIER, when only
        // an inner panel qualified — an inner panel's rect is not the card's outer outline, and the
        // result would have latched for the session. This gate makes that impossible without
        // depending on timing.
        //
        // ITEM CARDS MUST WORK BY CONSTRUCTION, not merely be wired (2026-08-11: "Weiterhin sollen
        // Item-Karten genauso betroffen sein"). The ability face's background is a single full-bleed
        // Image; an item face's may not be — nothing here can measure that offline. So the gate has
        // TWO ways to pass, and both are timing-independent:
        //   (a) ONE candidate covers MinOutlineCoverage of the face — the ability card's case;
        //   (b) the candidates' UNION bounding box does, no matter how many pieces it took — the
        //       case of a face whose background is split into several Images.
        // Either way the thing that defines the outline demonstrably spans the card.
        //
        // WHAT THIS DELIBERATELY DOES *NOT* DO, AND WHY (this replaces a count-based fallback that
        // read "after N fruitless offers, accept the largest candidate"). That fallback could not
        // hold: a hand fan adopts up to 24 cards IN ONE FRAME and every one of them is an offer, so
        // the whole grace was spendable before the addressable loader had returned anything at all —
        // and the largest candidate at that moment is an INNER PANEL (an action half is ~50 % of the
        // card). The result would have been a wrong, permanently latched outline, which is the one
        // outcome the standing rule forbids: degrade to today's rectangle, never to a wrong shape.
        // A card kind that can satisfy neither (a) nor (b) simply keeps the rounded rect and says so
        // through the normal give-up line — visibly identical to ModBuild 108, and honest.
        if (!hasFullBleed && unionCoverage < MinOutlineCoverage)
        {
            state.CoverageWaits++;
            Reject(state, kind, "no-full-bleed",
                   $"{picks.Count} candidate image(s) qualified but neither a single one " +
                   $"({largestPick:P0} largest) nor their union bounding box ({unionCoverage:P0}) " +
                   $"reaches {MinOutlineCoverage:P0} of the face, so nothing here can carry the OUTER " +
                   "outline (the card's own background art has most likely not arrived yet). Waiting: " +
                   "an inner panel's rect is not the card's outline and accepting one would latch a " +
                   "WRONG shape for the session");
            return;
        }
        if (!hasFullBleed)
        {
            VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): no single candidate reaches " +
                                $"{MinOutlineCoverage:P0} of the face (largest {largestPick:P0}), but the " +
                                $"UNION of {picks.Count} candidate(s) spans {unionCoverage:P0} of it — " +
                                "proceeding on the union. This is the branch that exists for a card kind " +
                                "whose background is not one full-bleed Image; CardMesh's shape guard " +
                                "still has the last word.");
        }
        if (hash == state.Hash)
            return; // same sprites as the last attempt — nothing new to look at, cost nothing
        if (Time.unscaledTime < state.NextAttemptTime)
            return; // spread repeat readbacks across frames (see SilhouetteAttemptInterval)

        state.Hash = hash;
        state.NextAttemptTime = Time.unscaledTime + SilhouetteAttemptInterval;
        state.Attempts++;
        state.OutlineImageIds.Clear(); // this attempt's union defines the shape, not a stale one

        // --- pass 2: stamp the union of their alpha into a card-space footprint --------------
        int fw = FootprintWidth;
        int fh = Mathf.Clamp(Mathf.RoundToInt(fw * faceRect.height / faceRect.width), 64, 512);
        var alpha = new byte[fw * fh];
        var readbacks = new List<Texture2D>(picks.Count);
        int stamped = 0, backdrops = 0;
        // THE ART RECT: the union of the DRAWN rects that actually contributed, in face-normalized
        // coordinates. The footprint itself stays authored over the FULL face rect — that is the
        // space the card BODY samples it in (CardMesh.SetSilhouette's doc, and it is exact) — but a
        // consumer that has no face rect at all, i.e. every peer mirror, needs to know which part of
        // it is card. See CardMesh.ExportTexture.
        float artMinX = 1f, artMinY = 1f, artMaxX = 0f, artMaxY = 0f;
        var sources = new System.Text.StringBuilder(128);
        try
        {
            var cache = new Dictionary<int, Texture2D>();
            foreach ((Image img, Rect norm) in picks)
            {
                Sprite sprite = img.sprite;
                if (sprite == null || sprite.texture == null)
                    continue;
                Rect tr = sprite.textureRect;
                if (tr.width < 2f || tr.height < 2f)
                    continue;

                // Keyed on the SPRITE, not its texture: two sprites routinely share one atlas
                // and each needs its own sub-rect readback.
                int key = sprite.GetInstanceID();
                if (!cache.TryGetValue(key, out Texture2D region))
                {
                    region = ReadSpriteRegion(sprite.texture, tr);
                    cache[key] = region;
                    readbacks.Add(region);
                }

                float colorA = img.color.a;
                // TRIM-CORRECT MAPPING. A tightly packed atlas sprite stores only its opaque
                // island (textureRect) and remembers where that island sat inside the sprite's
                // ORIGINAL bounds (rect + textureRectOffset). A Simple Image stretches the
                // ORIGINAL bounds across its RectTransform, so image-rect space must be measured
                // in sprite.rect pixels and the trimmed-away margin must read as alpha 0 — which
                // is itself part of the silhouette. Sampling textureRect as if it were the whole
                // image (what the predecessor did) stretched the art and lost that margin.
                float srcW = Mathf.Max(1f, sprite.rect.width);
                float srcH = Mathf.Max(1f, sprite.rect.height);
                Vector2 trimOff = sprite.textureRectOffset;
                bool trimmed = tr.width < srcW - 0.5f || tr.height < srcH - 0.5f;

                // A full-bleed image that is opaque everywhere we probe is a backdrop, not a
                // shape — unioning it in would flatten the silhouette to a rectangle. A TRIMMED
                // sprite is never that: its margin is transparent by definition.
                if (!trimmed && norm.width * norm.height >= BackdropAreaFraction
                    && IsFullyOpaque(region, colorA))
                {
                    backdrops++;
                    continue;
                }

                // NAME THE SOURCES. The ModBuild-108 log said "1 of 1 candidate image(s)" and left
                // us guessing which one; the frame's MIP BAKE line was the only reason we could
                // name it ('AC_Berserker_Background'). Never again.
                if (sources.Length > 0)
                    sources.Append(", ");
                sources.Append('\'').Append(img.name).Append("'/'").Append(sprite.name).Append("' ")
                       .Append((norm.width * norm.height).ToString("P0"))
                       .Append(trimmed ? " trimmed" : " untrimmed");
                state.OutlineImageIds.Add(img.GetInstanceID());
                if (norm.xMin < artMinX) artMinX = norm.xMin;
                if (norm.yMin < artMinY) artMinY = norm.yMin;
                if (norm.xMax > artMaxX) artMaxX = norm.xMax;
                if (norm.yMax > artMaxY) artMaxY = norm.yMax;

                int fx0 = Mathf.Clamp(Mathf.FloorToInt(norm.xMin * fw), 0, fw - 1);
                int fx1 = Mathf.Clamp(Mathf.CeilToInt(norm.xMax * fw), 0, fw - 1);
                int fy0 = Mathf.Clamp(Mathf.FloorToInt(norm.yMin * fh), 0, fh - 1);
                int fy1 = Mathf.Clamp(Mathf.CeilToInt(norm.yMax * fh), 0, fh - 1);
                for (int fy = fy0; fy <= fy1; fy++)
                {
                    float lv = ((fy + 0.5f) / fh - norm.yMin) / norm.height;
                    if (lv < 0f || lv > 1f)
                        continue;
                    float py = lv * srcH - trimOff.y;
                    if (py < 0f || py > tr.height)
                        continue; // trimmed-away margin: transparent, contributes nothing
                    float ty = py / tr.height;
                    int rowBase = fy * fw;
                    for (int fx = fx0; fx <= fx1; fx++)
                    {
                        float lu = ((fx + 0.5f) / fw - norm.xMin) / norm.width;
                        if (lu < 0f || lu > 1f)
                            continue;
                        float px = lu * srcW - trimOff.x;
                        if (px < 0f || px > tr.width)
                            continue;
                        Color src = region.GetPixelBilinear(px / tr.width, ty);
                        float sa = src.a * colorA;
                        var b = (byte)Mathf.Clamp(Mathf.RoundToInt(sa * 255f), 0, 255);
                        int idx = rowBase + fx;
                        if (b > alpha[idx])
                        {
                            alpha[idx] = b;
                            stamped++;
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (Texture2D t in readbacks)
                if (t != null)
                    Object.Destroy(t);
        }

        if (stamped == 0)
        {
            Reject(state, kind, "sampled-empty",
                   $"{picks.Count} candidate image(s) sampled to nothing ({backdrops} were opaque " +
                   "full-bleed backdrops and were skipped by design)");
            return;
        }

        VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}) attempt {state.Attempts}: footprint {fw}x{fh} " +
                            $"stamped from {picks.Count - backdrops} of {picks.Count} candidate image(s) " +
                            $"({backdrops} opaque full-bleed backdrop(s) skipped) on a " +
                            $"{faceRect.width:F0}x{faceRect.height:F0} px face — sources: " +
                            $"{(sources.Length > 0 ? sources.ToString() : "none")}. STOCK-ALPHA footprint: " +
                            "the art's own designed alpha at the 0.5 iso — no shadow trim, no frame punch, " +
                            "no outline clip — so the body backs exactly what the face draws, bottom edge " +
                            "included. Handing it to CardMesh.");
        string sourceName = sources.Length > 0 ? sources.ToString() : "unnamed";

        // THE ART RECT LINE: how much of the face rect the card art actually DRAWS on
        // (preserveAspect letterboxes the poker-shaped art inside the taller face rect). The
        // footprint spans the FULL face rect — that is the space the card body samples it in —
        // and this sub-rect is what ExportTexture crops to for consumers that have no face rect
        // (the peer board-recess quads, Net/RemoteBoardCard).
        Rect artRect = (artMaxX > artMinX && artMaxY > artMinY)
            ? Rect.MinMaxRect(Mathf.Clamp01(artMinX), Mathf.Clamp01(artMinY),
                              Mathf.Clamp01(artMaxX), Mathf.Clamp01(artMaxY))
            : new Rect(0f, 0f, 1f, 1f);
        VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}) ART RECT: the card art DRAWS on " +
                            $"{artRect.width:P1} x {artRect.height:P1} of the {faceRect.width:F0}x" +
                            $"{faceRect.height:F0} px face rect (x {artRect.xMin:F3}..{artRect.xMax:F3}, " +
                            $"y {artRect.yMin:F3}..{artRect.yMax:F3}); {aspectCorrected} of {picks.Count} " +
                            "candidate(s) are preserveAspect and were letterboxed inside their own layout " +
                            "rect. The footprint spans the full face rect; this sub-rect only drives " +
                            "CardMesh.ExportTexture's crop for the peer-quad consumers.");

        // ALREADY APPLIED (normally: from the persisted cache, before this session drew a card).
        // Do NOT re-apply — a mid-session re-shape is exactly the visible transition the user
        // rejected — only confirm or refresh the FILE for the next launch.
        if (CardMesh.SilhouetteApplied(kind))
        {
            state.CaptureSettled = true;
            CardMesh.RefreshSilhouetteCache(kind, alpha, fw, fh, artRect, sourceName);
            return;
        }

        bool applied = CardMesh.SetSilhouette(kind, alpha, fw, fh, artRect, sourceName, fromCache: false);
        if (applied)
        {
            state.CaptureSettled = true;
            // FIRST RUN ONLY, and the one thing that shortens its window. Until this moment the
            // blackout had no mask and did nothing; every face already on screen now has one, but
            // each is behind its own 1 s rate limit. Dropping those limits means the very next
            // Offer of each face — the 1 s Rescan cadence at worst, its next art arrival at best —
            // walks it, instead of both delays adding up. From the second launch onward this line
            // never runs: the mask arrives from the cache before the first card body exists.
            FaceBlackout.InvalidateRateLimits();
            VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): APPLIED — every body of this shape is now " +
                                "punched out to the card art's own outline (CardContour mesh), so the " +
                                "umber front only exists where the card itself is solid ('die meshes genau " +
                                "die Ränder der Karten'). This session is the FIRST for this shape, so the " +
                                "change was visible once; it is now cached and every later launch has it " +
                                "before the first card is drawn.");
        }
        else
        {
            Reject(state, kind, "guard-refused",
                   "the footprint failed CardMesh's sanity guard (see the SetSilhouette line above)");
        }
    }

    /// <summary>
    /// ROUND 7 — THE COORDINATE CORRECTION. Where an <c>Image</c> actually DRAWS its sprite inside
    /// its own RectTransform, in that RectTransform's local space.
    ///
    /// <para>THE DEFECT THIS CLOSES, measured rather than argued. The user's screenshot
    /// (<c>.planning/debug/karten.png</c>, same session as the ModBuild-110 log) shows the black
    /// band is NOT a ring: on the board card it is ~18 px at the top and ~25 px at the bottom of a
    /// 378 px card, and 2 px / 6 px at the left and right of a 254 px card. So the slab is too TALL
    /// for the art, not too wide — 4.76 % of the card height dead at the top, against a slab whose
    /// own footprint margin (log: bounding box 95.5 % of the face) accounts for barely 1.6 %.</para>
    ///
    /// <para>THE ARITHMETIC THAT NAMES IT. <c>VRCard.SetCanvasSize</c> fits the slab to
    /// <c>facePixels × fit × VisibleFaceFraction</c> and <see cref="RefreshFitScale"/> draws the
    /// face at the same <c>1 − BorderFraction</c>, so slab and face RECT are the same rectangle —
    /// the 294×450 face rect, aspect 0.6533. The card ART inside that rect is poker-shaped
    /// (63.5:88 = 0.7216). Fitted to the rect's width it is <c>0.6533 / 0.7216 = 90.54 %</c> of the
    /// rect's height, centred → <b>4.73 % dead margin at the top and at the bottom</b>. Measured:
    /// 4.76 %. That is the black band, to two decimal places, and it is on exactly the axis the
    /// screenshot shows it on.</para>
    ///
    /// <para>WHY SIX ROUNDS OF CORRECT CLIPPING CHANGED NOTHING. The capture normalised each
    /// candidate by <c>GetWorldCorners</c>, i.e. by its LAYOUT rect, and stamped the sprite's alpha
    /// across all of it. uGUI does not draw it there: <c>Image.preserveAspect</c> letterboxes the
    /// sprite inside the rect (<c>Image.PreserveSpriteAspectRatio</c>, replicated exactly below).
    /// So the mask was stretched 1/0.9054 = +10.4 % vertically, its own transparent margins were
    /// pushed off the top and bottom of the card, and the mesh was declared "card" in precisely the
    /// two bands where the art draws nothing. Every clip since ModBuild 108 has been correct and
    /// aimed 10 % away from the edge it was meant to find. The ModBuild-110 log states the
    /// consequence without naming it: the footprint's bounding box reaches 95.5 % of the face and
    /// is filled 0.915 — a near-rectangle — while the art visibly occupies 88.6 %.</para>
    ///
    /// <para>WHAT THIS DOES AND DOES NOT CHANGE. It changes ONE thing: the rectangle the sprite's
    /// alpha is stamped into. Nothing here touches <c>CardsConfig.CardWidth</c>,
    /// <c>CardsConfig.CardHeight</c>, the mesh, the collider, <c>VRCard.SweepFaceWidthWorld</c>, the fan, the
    /// recesses or <c>[Cards] SlotOverlayScale</c> — the card keeps exactly the size, proportions
    /// and behaviour it has today (user, verbatim: "Ich möchte gerne an den aktuellen Proportionen
    /// festhalten. Ich will es also so wie es jetzt ist und sich verhält - nur eben ohne die
    /// schwarzen Ränder."). The band disappears because the BODY stops painting where the art does
    /// not, which is the only thing that was ever wrong.</para>
    ///
    /// <para>EVERY TERM IS LIVE. The shrink is derived from <c>img.preserveAspect</c>,
    /// <c>sprite.rect</c>, the RectTransform's own rect and its pivot — nothing is hard-coded, so a
    /// tuned <c>CardWidth</c>, a different face resolution or an item card's own near-square art all
    /// fall out of the same three lines. If the image does NOT preserve aspect the drawn rect IS the
    /// layout rect and this returns it unchanged, so the correction is a strict no-op wherever the
    /// old assumption happened to hold.</para>
    ///
    /// <paramref name="shrink"/> receives the fraction of the layout rect's area the drawn rect
    /// keeps (1 = no letterbox), for the log.
    /// </summary>
    private static Rect DrawnLocalRect(Image img, Sprite sprite, out float shrink)
    {
        shrink = 1f;
        Rect r = img.rectTransform.rect;
        if (!img.preserveAspect || r.width <= 0f || r.height <= 0f)
            return r;
        float sw = sprite.rect.width, sh = sprite.rect.height;
        if (sw <= 0f || sh <= 0f)
            return r;

        // Byte-for-byte uGUI's own PreserveSpriteAspectRatio (UnityEngine.UI.Image): the rect is
        // shrunk on ONE axis to the sprite's aspect and re-anchored by the PIVOT, not centred.
        // Copying the pivot term matters — a card whose background is pivoted at the top would
        // otherwise be corrected in the wrong direction, and this must be right for art nobody here
        // can open.
        float spriteRatio = sw / sh;
        float rectRatio = r.width / r.height;
        Vector2 pivot = img.rectTransform.pivot;
        if (spriteRatio > rectRatio)
        {
            float oldH = r.height;
            r.height = r.width / spriteRatio;
            r.y += (oldH - r.height) * pivot.y;
        }
        else
        {
            float oldW = r.width;
            r.width = r.height * spriteRatio;
            r.x += (oldW - r.width) * pivot.x;
        }
        Rect layout = img.rectTransform.rect;
        float layoutArea = layout.width * layout.height;
        if (layoutArea > 0f)
            shrink = Mathf.Clamp01(r.width * r.height / layoutArea);
        return r;
    }

    /// <summary>Is this sprite region opaque at every probe point? A 16x16 grid including the
    /// extreme corners — an ornate outline is transparent there, a rectangular backdrop is
    /// not.</summary>
    private static bool IsFullyOpaque(Texture2D region, float colorA)
    {
        if (colorA < 0.99f)
            return false;
        const int probes = 16;
        for (int y = 0; y < probes; y++)
        {
            float v = (y + 0.5f) / probes;
            for (int x = 0; x < probes; x++)
            {
                if (region.GetPixelBilinear((x + 0.5f) / probes, v).a < 0.99f)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// CPU-read ONE sprite's region out of a (usually non-CPU-readable, usually 4096²) atlas via
    /// a GPU blit plus a sub-rect <c>ReadPixels</c>. Sub-rect on purpose: the predecessor read
    /// the WHOLE atlas into a mipped RGBA32 Texture2D — ~85 MB and a full-frame stall per unique
    /// texture per attempt. Only the sprite's own texels are ever sampled, so only they are
    /// fetched, and no mip chain is built (<c>GetPixelBilinear</c> reads level 0). The result is
    /// CPU-side only and destroyed by the caller; it is never rendered.
    /// </summary>
    private static Texture2D ReadSpriteRegion(Texture src, Rect texRect)
    {
        int rx = Mathf.Clamp(Mathf.FloorToInt(texRect.x), 0, Mathf.Max(0, src.width - 1));
        int ry = Mathf.Clamp(Mathf.FloorToInt(texRect.y), 0, Mathf.Max(0, src.height - 1));
        int rw = Mathf.Clamp(Mathf.RoundToInt(texRect.width), 1, src.width - rx);
        int rh = Mathf.Clamp(Mathf.RoundToInt(texRect.height), 1, src.height - ry);

        RenderTexture rt = RenderTexture.GetTemporary(
            src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(rw, rh, TextureFormat.RGBA32, mipChain: false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            tex.ReadPixels(new Rect(rx, ry, rw, rh), 0, 0);
            tex.Apply(updateMipmaps: false);
            return tex;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
    // ------------------------------------------------------ face blackout (round 2) --

    /// <summary>
    /// THE ROUND-2 FIX. Mute the adopted card face's own SHAPE-LESS black — the thing that is
    /// actually painting "der schwarze Rand … immer noch vollständig da" now that the card BODY
    /// behind it is alpha-clipped.
    ///
    /// WHY THIS IS THE LAYER. See <see cref="SilhouetteState"/> for the full derivation; the short
    /// form is his own second sentence: during a character switch the border goes TRANSPARENT for
    /// a moment and then turns black again. A clipped mesh cannot un-clip itself — the only thing
    /// that can stop painting and start again is the FACE, whose art the addressable loader takes
    /// down and puts back across a switch. So the black is drawn by a face graphic sitting where
    /// the card is not.
    ///
    /// WHAT IT MUTES, AND WHY THAT CAN NEVER BE CARD ART. Only an <c>Image</c> that is:
    /// <list type="number">
    /// <item>DRAWN (<c>enabled &amp;&amp; activeSelf</c>) — the same test the capture uses;</item>
    /// <item>WITHOUT A SPRITE. uGUI renders those as a plain quad in <c>Image.color</c>. A quad is
    ///   a RECTANGLE: it cannot carry the card's outline, so wherever it extends past the
    ///   silhouette it is by definition not the card. The capture log counted <b>22</b> such
    ///   images enabled on a single face, which is where this hypothesis came from;</item>
    /// <item>OPAQUE (alpha ≥ <c>BlackoutMinAlpha</c>, near 1) and DARK (<c>BlackoutMaxLuma</c>) —
    ///   a bright quad is not "der schwarze Rand", and a TRANSLUCENT dark one is a dimmer (an
    ///   unplayable card's veil), which is a game state cue and must survive;</item>
    /// <item>CARD-SIZED (≥ <see cref="BlackoutMinAreaFraction"/> of the face) — a small dark chip
    ///   behind an initiative number is a legitimate widget and is left alone;</item>
    /// <item>and actually PAINTING OUTSIDE the captured silhouette (≥
    ///   <see cref="BlackoutMinOutsideFraction"/> of its own area) — if it lives entirely inside
    ///   the card it is invisible to this report and is left alone.</item>
    /// </list>
    /// All five must hold. Muting is <c>color.a = 0</c>, not <c>enabled = false</c>: the game's own
    /// layout, raycast targets and tween targets keep working, and <see cref="Restore"/> only puts
    /// the colour back if it is still the one we wrote (so a game recolour in between wins).
    ///
    /// WHEN IT RUNS — AND WHY NOT ON A TIMER (2026-08-11: "Der Prozess der 'Ausblendung' soll auch
    /// nicht sichtbar sein, sondern direkt die richtigen meshes sichtbar sein"). A once-a-second
    /// sweep would leave a card on screen with its black rim for up to a second: that is the defect
    /// itself, merely intermittent. The mechanism is therefore the ARRIVAL SEAM
    /// (<see cref="CardArtWatch"/> → <see cref="MaintainArtArrival"/> → <see cref="Offer"/> with
    /// <c>artJustArrived</c>): the same frame the game assigns a face its art, while the
    /// addressable loader still has the Image disabled and before that art's first rendered frame —
    /// the very seam <c>CardFaceMipBake</c> has ridden since T3 for exactly this reason. A face
    /// never walked before is likewise walked immediately. The 1 s cadence remains ONLY as a
    /// self-heal for the game recolouring a quad later; it is a backstop, not the mechanism.
    ///
    /// AND ITS INPUT IS READY BEFORE THE FIRST CARD EXISTS. The mask it judges "outside" against is
    /// whatever <see cref="CardMesh"/> has applied — which, from the second launch onward, is the
    /// PERSISTED footprint loaded inside the card body's own material factory (see
    /// <c>CardMesh.EnsureSilhouetteCacheLoaded</c>). So on every launch but the first, arrival →
    /// mute happens with the mask already in hand.
    ///
    /// DEGRADES TO TODAY. With no footprint (first run, capture not done, or refused by CardMesh's guard)
    /// this does nothing at all — the card keeps exactly the ModBuild-108 look. And every face it
    /// LOOKS at is logged once with its numbers, muted or not, so a face that paints black through
    /// some other component names itself in the next hardware log instead of costing a round.
    /// </summary>
    private static class FaceBlackout
    {
        /// <summary>
        /// Minimum colour alpha for a quad to count as "painting black". Deliberately near-opaque
        /// rather than 0.5: the one thing on a card face that legitimately IS a large dark quad is
        /// a DIMMER (an unplayable/unfocused card veiled by a translucent overlay), and a dimmer is
        /// translucent by construction — muting it would delete a game state cue. A quad that is
        /// opaque AND dark AND card-sized hides whatever is behind it, so it can only ever be a
        /// backdrop.
        /// </summary>
        private const float BlackoutMinAlpha = 0.85f;

        /// <summary>Maximum Rec.601 luminance (0..1) for a quad to count as "black".</summary>
        private const float BlackoutMaxLuma = 0.30f;

        /// <summary>Minimum share of the face a sprite-less quad must cover to be a card BACKDROP
        /// rather than a widget chip. Tier A of two — see <see cref="BlackoutStripOutsideFraction"/>
        /// for why one tier is not enough.</summary>
        private const float BlackoutMinAreaFraction = 0.5f;

        /// <summary>Minimum share of its OWN area a quad must paint outside the silhouette before
        /// it is muted. Guards against muting something that merely grazes the outline.</summary>
        private const float BlackoutMinOutsideFraction = 0.02f;

        /// <summary>
        /// TIER B — "MOSTLY OUTSIDE THE CARD", at any size.
        ///
        /// Tier A assumes the black rim is ONE card-sized backdrop quad behind the art. That is the
        /// likeliest construction and it is what the 22 sprite-less Images on the captured face
        /// suggest — but it is an assumption, and the alternative construction (a rim assembled from
        /// four thin EDGE STRIPS, or one strip per side of a frame) would pass none of Tier A's
        /// size test while producing exactly the same black border. The standing rule for this
        /// report is to neutralise both candidates rather than ship a diagnostic that distinguishes
        /// them on the next hardware run — he has spent four sessions on this border.
        ///
        /// So a sprite-less, opaque, DARK quad of ANY size that paints at least this much of its own
        /// area where the footprint says "not card" is muted too. The threshold is deliberately high:
        /// a quad that is two thirds outside the card outline is not a card element under any
        /// reading — a real widget (an initiative chip, an action-half backdrop) lies INSIDE the
        /// card and scores ~0 here. Both tiers are named in the log line, so the next log states
        /// which construction this game actually uses instead of leaving it inferred.
        /// </summary>
        private const float BlackoutStripOutsideFraction = 0.66f;

        /// <summary>
        /// ROUND-5 CORRECTION TO TIER B. Tier B exists for a rim assembled from thin EDGE STRIPS —
        /// and until this build it could never fire on one, because the size test that precedes the
        /// tiers rejected anything under <see cref="MinOutlineAreaFraction"/> (3 % of the face)
        /// BEFORE either tier was evaluated and before the quad was even counted as "considered".
        /// A 2 %-wide full-height strip is exactly the construction Tier B was written for and
        /// exactly the one that gate hid — including from the log, which is why the ModBuild-109
        /// line could truthfully say "1 sprite-less quad" while a rim of four strips stood on the
        /// same face. The size test now only gates TIER A (which genuinely means "card-sized
        /// backdrop"); Tier B sees every quad down to this noise floor, and every one of them is
        /// reported.
        /// </summary>
        private const float BlackoutNoiseFloorFraction = 0.002f;

        /// <summary>Probe grid used to measure "how much of this quad falls outside the card".
        /// 24x36 ≈ the footprint's own aspect; 864 lookups per candidate per second.</summary>
        private const int ProbeX = 24;
        private const int ProbeY = 36;

        /// <summary>Muted graphics and the colour we wrote them from, keyed by instance id so a
        /// pooled/recycled widget can never collide with a live one.</summary>
        private static readonly Dictionary<int, (Graphic Img, Color Orig, Color Muted)> s_muted = new();

        /// <summary>
        /// Is this graphic a provably SHAPE-LESS quad — i.e. does uGUI draw it as a plain
        /// rectangle in <c>Graphic.color</c>?
        ///
        /// <para>Round 5 widens the answer from "an <c>Image</c> without a sprite" to "an
        /// <c>Image</c> without a sprite OR a <c>RawImage</c> without a texture". The rectangle
        /// argument that makes muting safe (see the class doc) holds identically for both, and the
        /// narrower test was an assumption about which component the game reached for, never a
        /// measurement: a <c>RawImage</c> backdrop would have been invisible to every pass of the
        /// previous four rounds — it is not an <c>Image</c>, so neither the capture's rejection
        /// counts nor the blackout's candidate list would have mentioned it once.</para>
        ///
        /// <para>Everything that CARRIES art is still refused here on purpose: its shape is the
        /// artist's business, and muting a whole sprite because part of it falls outside the
        /// outline would delete card art.</para>
        /// </summary>
        private static bool IsShapelessQuad(Graphic g) => g switch
        {
            Image img => img.sprite == null,
            RawImage raw => raw.texture == null,
            _ => false,
        };

        private static readonly bool[] s_logged = new bool[3];
        private static readonly bool[] s_loggedMuted = new bool[3];

        /// <summary>Next unscaled time a given face root may be walked again.</summary>
        private static readonly Dictionary<int, float> s_nextPass = new();

        private const float PassInterval = 1f;

        internal static void Maintain(RectTransform? faceRoot, CardBodyKind kind, byte[]? footprint,
                                      int fw, int fh, HashSet<int> outlineIds, bool artJustArrived)
        {
            if (faceRoot == null || footprint == null || fw <= 1 || fh <= 1)
                return; // no trustworthy shape to judge "outside" against — keep today's look
            int rootId = faceRoot.GetInstanceID();
            float now = Time.unscaledTime;
            // ARRIVAL BYPASSES THE RATE LIMIT — this is the whole point (see CardFace.Offer's
            // artJustArrived note). The game has just assigned this face its art while the loader
            // still holds the Image disabled, so a pass now lands before the first rendered frame.
            // A face never walked before is treated the same way: its first walk is immediate.
            if (!artJustArrived && s_nextPass.TryGetValue(rootId, out float next) && now < next)
                return;
            s_nextPass[rootId] = now + PassInterval;
            Prune();

            Rect faceRect = faceRoot.rect;
            if (faceRect.width < 1f || faceRect.height < 1f)
                return;

            // EVERY Graphic, not every Image (see IsShapelessQuad) — a RawImage backdrop was
            // structurally invisible to rounds 1-4.
            Graphic[] images = faceRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
            // The per-candidate detail is only ever printed by the two one-shot latches below, and
            // this pass runs once a second PER CARD — a full fan plus a browser is twenty-odd
            // instances. Build the string only while a latch can still fire; afterwards this is a
            // measurement loop with no allocation at all.
            bool wantReport = !s_logged[(int)kind] || !s_loggedMuted[(int)kind];
            System.Text.StringBuilder? report = wantReport ? new System.Text.StringBuilder(160) : null;
            int muted = 0, considered = 0;
            var corners = new Vector3[4];

            foreach (Graphic img in images)
            {
                if (img == null || !img.enabled || !img.gameObject.activeSelf)
                    continue;
                // Art-bearing graphics are NEVER muted (their shape is the artist's business) —
                // but they ARE measured once, for the inventory in the log line below. Four rounds
                // have now been spent inferring what paints the border from what the code happens
                // to look at; the next log states what is actually on the face and how much of each
                // element lies outside the captured outline.
                bool shapeless = IsShapelessQuad(img);
                int imgId = img.GetInstanceID();
                if (outlineIds.Contains(imgId))
                    continue; // (cannot happen — the union needs a sprite — but state it anyway)

                // ALREADY OURS. Muting sets alpha 0, so a muted quad can never re-qualify below;
                // this is the branch that keeps it muted after the game recolours it (the game
                // does exactly that on focus/unfocus). If the game's new colour is no longer a
                // dark opaque quad, it is a legitimate visible element again — stop tracking it
                // and leave the game's value alone.
                if (s_muted.TryGetValue(imgId, out (Graphic Img, Color Orig, Color Muted) tracked))
                {
                    // IT GAINED ART WHILE MUTED. The addressable loader nulls a card element's
                    // sprite for the duration of a load (see CardArtGuard), so a quad can be
                    // shape-less — and therefore mutable — at one pass and carrying the card's own
                    // art at the next. Hand it straight back: a muted sprite is invisible ART, which
                    // is the one outcome this mechanism must never produce. Round 5 widened what may
                    // be muted, so this stops being a theoretical ordering note and starts being a
                    // guard.
                    if (!shapeless)
                    {
                        s_muted.Remove(imgId);
                        if (img.color == tracked.Muted)
                            img.color = tracked.Orig;
                        continue;
                    }
                    Color live = img.color;
                    if (live == tracked.Muted)
                        continue; // still muted, nothing to do
                    float liveLuma = live.r * 0.299f + live.g * 0.587f + live.b * 0.114f;
                    if (live.a < BlackoutMinAlpha || liveLuma > BlackoutMaxLuma)
                    {
                        s_muted.Remove(imgId); // the game made it something else — hands off
                        continue;
                    }
                    var remute = new Color(live.r, live.g, live.b, 0f);
                    img.color = remute;
                    s_muted[imgId] = (img, tracked.Orig, remute);
                    continue;
                }

                // Past this point everything is MEASUREMENT. An art-bearing graphic can never be
                // muted, so it is only walked while a report latch is still open (the inventory).
                if (!shapeless && report == null)
                    continue;

                Color c = img.color;
                img.rectTransform.GetWorldCorners(corners);
                float minNx = 1f, minNy = 1f, maxNx = 0f, maxNy = 0f;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 local = faceRoot.InverseTransformPoint(corners[i]);
                    float nx = (local.x - faceRect.xMin) / faceRect.width;
                    float ny = (local.y - faceRect.yMin) / faceRect.height;
                    if (nx < minNx) minNx = nx;
                    if (nx > maxNx) maxNx = nx;
                    if (ny < minNy) minNy = ny;
                    if (ny > maxNy) maxNy = ny;
                }
                float aw = maxNx - minNx, ah = maxNy - minNy;
                float area = Mathf.Max(0f, aw) * Mathf.Max(0f, ah);
                // NOISE FLOOR ONLY — the 3 % test that used to stand here gated BOTH tiers and so
                // made Tier B unreachable for the exact construction it was written for. See
                // BlackoutNoiseFloorFraction. Tier A still requires BlackoutMinAreaFraction below.
                if (area < BlackoutNoiseFloorFraction)
                    continue;

                float luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

                // How much of this quad falls where the footprint says "not card"?
                int outside = 0, probes = 0;
                for (int py = 0; py < ProbeY; py++)
                {
                    float v = minNy + ah * ((py + 0.5f) / ProbeY);
                    if (v < 0f || v > 1f)
                        continue;
                    int ty = Mathf.Clamp((int)(v * fh), 0, fh - 1);
                    for (int px = 0; px < ProbeX; px++)
                    {
                        float u = minNx + aw * ((px + 0.5f) / ProbeX);
                        if (u < 0f || u > 1f)
                            continue;
                        int tx = Mathf.Clamp((int)(u * fw), 0, fw - 1);
                        probes++;
                        if (footprint[ty * fw + tx] < 128)
                            outside++;
                    }
                }
                float outsideFrac = probes > 0 ? (float)outside / probes : 0f;

                if (shapeless)
                    considered++;
                // Both gates start from "opaque AND dark AND shape-less", which is the part that
                // makes muting safe (see the class note). They differ only in what makes the quad
                // ILLEGITIMATE: Tier A "it is a card-sized backdrop that reaches past the outline",
                // Tier B "whatever size it is, it lives mostly where the card is not".
                bool opaqueDark = c.a >= BlackoutMinAlpha && luma <= BlackoutMaxLuma;
                bool backdropTier = shapeless && opaqueDark
                                    && area >= BlackoutMinAreaFraction
                                    && outsideFrac >= BlackoutMinOutsideFraction;
                bool stripTier = shapeless && opaqueDark && outsideFrac >= BlackoutStripOutsideFraction;
                bool qualifies = backdropTier || stripTier;

                if (report != null)
                {
                    if (report.Length > 0)
                        report.Append("; ");
                    string art = img is Image im && im.sprite != null ? $"sprite '{im.sprite.name}'"
                               : img is RawImage rw && rw.texture != null ? $"texture '{rw.texture.name}'"
                               : img is Image ? "no sprite"
                               : img is RawImage ? "no texture"
                               : img.GetType().Name;
                    report.Append('\'').Append(img.name).Append("' [").Append(art).Append("] ")
                          .Append(area.ToString("P0"))
                          .Append(" of the face, rgba(").Append(c.r.ToString("F2")).Append(',')
                          .Append(c.g.ToString("F2")).Append(',').Append(c.b.ToString("F2")).Append(',')
                          .Append(c.a.ToString("F2")).Append("), luma ").Append(luma.ToString("F2"))
                          .Append(", ").Append(outsideFrac.ToString("P0")).Append(" of it outside the card → ")
                          .Append(qualifies ? (backdropTier ? "MUTED (tier A: card-sized backdrop)"
                                                            : "MUTED (tier B: mostly outside the card)")
                                            : !shapeless ? "kept (carries art — inventory only, never muted)"
                                            : c.a < BlackoutMinAlpha ? "kept (translucent — a dimmer, not a backdrop)"
                                            : luma > BlackoutMaxLuma ? "kept (not dark)"
                                            : area < BlackoutMinAreaFraction ? "kept (too small for tier A, and " +
                                                  $"under {BlackoutStripOutsideFraction:P0} outside for tier B)"
                                            : "kept (paints nothing outside the card)");
                }

                if (!qualifies)
                    continue;

                var mutedColor = new Color(c.r, c.g, c.b, 0f);
                img.color = mutedColor;
                s_muted[imgId] = (img, c, mutedColor);
                muted++;
            }

            // Two latches on purpose: the first face walked may be one where nothing qualifies yet
            // (art still arriving), and a log that only ever prints that pass would hide the real
            // answer. So: print the first pass that MUTES something, and — separately, once — the
            // first pass that found candidates and muted none.
            // The inventory is the reason the "nothing muted" latch no longer needs a shape-less
            // candidate to fire: a face with ZERO shape-less quads is itself the answer to "what
            // paints the border", and a silent pass would have hidden it (again).
            if (muted > 0 ? !s_loggedMuted[(int)kind] : (!s_logged[(int)kind] && report != null && report.Length > 0))
            {
                if (muted > 0)
                    s_loggedMuted[(int)kind] = true;
                s_logged[(int)kind] = true;
                VRLog.Info("Cards", $"CARD FACE BLACKOUT ({kind}): {muted} of {considered} shape-less " +
                                    "quad(s) on the first measured face muted. FULL FACE INVENTORY (every " +
                                    "drawn Graphic, art-bearing ones listed but never muted) — " +
                                    $"{report}. A shape-less " +
                                    "uGUI Image/RawImage is a plain RECTANGLE, so anything it paints outside the " +
                                    "captured card outline is the black rectangular border of the " +
                                    "2026-08-11 report ('der schwarze Rand … immer noch vollständig da'); " +
                                    "the card BODY behind it is alpha-clipped already, which is why he saw " +
                                    "it go transparent for a moment on a character switch. Muting is " +
                                    "alpha→0 on the game's own Image and is restored on detach.");
            }
        }

        /// <summary>Forget every per-face rate limit, so the next <see cref="Offer"/> of any face
        /// walks it immediately. Called once, on the first run, the moment a footprint first
        /// exists — see the call site for why that is the only time it can matter.</summary>
        internal static void InvalidateRateLimits() => s_nextPass.Clear();

        /// <summary>Drop bookkeeping for faces/images Unity has destroyed. A card that dies with
        /// the scene never reaches <see cref="Restore"/>, and neither dictionary may grow for the
        /// life of the session. Cheap: only walks when the tables are already large, and only from
        /// the once-a-second pass.</summary>
        private static void Prune()
        {
            if (s_muted.Count < 128 && s_nextPass.Count < 256)
                return;
            var deadImages = new List<int>();
            foreach (KeyValuePair<int, (Graphic Img, Color Orig, Color Muted)> kv in s_muted)
                if (kv.Value.Img == null)
                    deadImages.Add(kv.Key);
            foreach (int id in deadImages)
                s_muted.Remove(id);
            if (s_nextPass.Count >= 256)
                s_nextPass.Clear(); // pure rate-limit state; rebuilding it costs one extra pass
        }

        /// <summary>Put every muted Image on this face back the way we found it. Only writes back
        /// when the colour is still OURS — if the game recoloured the quad meanwhile, the game's
        /// value is the newer truth and we must not stomp it.</summary>
        internal static void Restore(Component? faceRoot)
        {
            if (faceRoot == null)
                return;
            s_nextPass.Remove(faceRoot.transform.GetInstanceID());
            Graphic[] images = faceRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
            foreach (Graphic img in images)
            {
                if (img == null)
                    continue;
                int id = img.GetInstanceID();
                if (!s_muted.TryGetValue(id, out (Graphic Img, Color Orig, Color Muted) rec))
                    continue;
                s_muted.Remove(id);
                Color now = img.color;
                if (Mathf.Approximately(now.r, rec.Muted.r) && Mathf.Approximately(now.g, rec.Muted.g)
                    && Mathf.Approximately(now.b, rec.Muted.b) && now.a <= 0.001f)
                {
                    img.color = rec.Orig;
                }
            }
        }
    }

    /// <summary>
    /// (Re)compute the face-to-host fit scale against the host's CURRENT size. VRCard
    /// resizes the host canvas to the real face pixels right after <see cref="Adopt"/>,
    /// so the scale captured during Adopt (against the placeholder host) would leave the
    /// art shrunk in a large canvas — the "black border WAY too big" of test #24. We
    /// re-fit whenever the host size changes and inset by <see cref="BorderFraction"/>
    /// so the mesh's rounded dark front reads as a thin outline. Scale-independent, so
    /// it holds at every card scale (fan, held, tray) and on both hands.
    /// </summary>
    private void RefreshFitScale()
    {
        if (_host == null)
            return;
        Vector2 hostSize = _host.rect.size;
        _fitHostSize = hostSize;
        _fitScale = ComputeFitScale(hostSize, FaceSize) * (1f - BorderFraction);
    }

    private static float ComputeFitScale(Vector2 hostSize, Vector2 faceSize)
    {
        if (hostSize.x <= 0f || hostSize.y <= 0f || faceSize.x <= 0f || faceSize.y <= 0f)
            return 1f;
        return Mathf.Min(hostSize.x / faceSize.x, hostSize.y / faceSize.y);
    }

    private void ApplyHostPose()
    {
        if (_face == null || _host == null)
            return;
        RefreshFitScale();
        _face.SetParent(_host, worldPositionStays: false);
        _face.anchorMin = CenterAnchor;
        _face.anchorMax = CenterAnchor;
        _face.pivot = CenterAnchor;
        _face.anchoredPosition3D = Vector3.zero;
        _face.localRotation = Quaternion.identity;
        _face.localScale = new Vector3(_fitScale, _fitScale, _fitScale);
        if (!_face.gameObject.activeSelf)
            _face.gameObject.SetActive(true);
    }

    /// <summary>
    /// Per-frame guard (no allocations): the game rewrites face anchors/active state
    /// whenever it refreshes the suppressed 2D hand — put our pose back.
    /// </summary>
    internal void Maintain()
    {
        if (_face == null || _host == null)
            return;
        if (_owner == null || _owner.fullAbilityCard == null)
        {
            // Widget died under us (scene teardown) — drop references.
            _face = null;
            _owner = null;
            _artWatch.Clear();
            return;
        }

        if (!ReferenceEquals(_face.parent, _host))
        {
            // Who moved it? A hand-layout refresh re-parents the face back under its
            // own AbilityCardUI (reclaim it). A game DIALOG (burn/redraw popups take
            // fullAbilityCard.gameObject, CardsHandUI.cs:826/2069) re-parents it
            // somewhere foreign.
            if (_face.parent == _origParent || (_owner != null && _face.IsChildOf(_owner.transform)))
            {
                ApplyHostPose();
            }
            // BURN/LOSE/DISCARD CONFIRM (test #22, symptom 4b): the game passes the
            // LIVE face straight to <c>DialogPopup.Show(fullAbilityCard.gameObject)</c>
            // (CardsHandUI.cs:826/850/909/2069), which re-parents it under the popup's
            // <c>contentHolder</c> WITHOUT the healthy full-card preview prep
            // (ToggleFullCard / ToggleFullCardCanvasSorting / a CardEffects _PosAndBounds
            // refresh — CardsHandUI.cs:340-345). On our world-space modal float the
            // card's custom screen-space card shader then resolves to DEEP BLACK, its
            // hover FX Image quads blow up to the popup canvas scale, and the burn flame
            // overlay reads as a fullscreen sheet (symptoms 4b/4c). We do NOT float that
            // popup content: RE-CLAIM the face onto our own known-good FaceCanvas (the
            // identical pipeline that renders every other hand/dock card correctly) so
            // the card stays readable ON the action-selection dock and the burn effect
            // plays on the card mesh. The popup's own yes/no buttons still float and
            // commit the burn; its now-empty contentHolder is harmless, and DialogPopup
            // restores the face to this same parent on Hide (PreviousState, verified
            // DialogPopup.cs:38-46). Keyed purely off widget state (a DialogPopup in the
            // parent chain), never off the modal-dock code.
            else if (IsDialogContent(_face.parent))
            {
                if (!_reclaimedFromDialog)
                {
                    _reclaimedFromDialog = true;
                    VRLog.Info("Cards", "CardFace re-claimed the burn/lose confirm card onto the " +
                                        "action-selection dock (kept off the modal float where the " +
                                        "screen-space card shader renders black).");
                }
                ApplyHostPose();
            }
            else
            {
                // Any other foreign parent — YIELD, never fight it; the next HandShown
                // rebuild re-adopts the face after the flow resolves.
                VRLog.Debug("Cards", $"CardFace yielded to game dialog ({_face.parent?.name ?? "null"}).");
                Yield();
            }
            return;
        }
        if (!_face.gameObject.activeSelf)
            _face.gameObject.SetActive(true);
        // T3 mip bake BACKSTOP: the per-frame arrival watch (MaintainArtArrival) is what makes
        // the swap land before the art's first rendered frame. This slow pass exists for the one
        // thing the watch cannot see — Images that did not exist when the watch array was
        // captured (the game activates enhancement slots / XP orbs after adoption). Re-capture,
        // then sweep. A no-op in the steady state: every sprite is already a baked copy.
        if (Time.unscaledTime >= _nextMipRescan)
        {
            _nextMipRescan = Time.unscaledTime + MipRescanInterval;
            CardFaceMipBake.Rescan(_owner!.fullAbilityCard);
            _artWatch.Capture(_owner!.fullAbilityCard);
        }
        // WHITE DECISION-PHASE FACES: replay any ShowCard the guard had to skip while the
        // card art was mid-load, and heal an action half the game left on a null sprite
        // (uGUI draws its built-in WHITE texture there). Both only fire when the addressable
        // loader is quiet — see CardArtGuard.
        if (Time.unscaledTime >= _nextArtTick)
        {
            _nextArtTick = Time.unscaledTime + CardArtGuard.TickIntervalSeconds;
            CardArtGuard.Tick(_owner!.fullAbilityCard);
        }
        // Re-assert the FULL anchor frame, not just the anchored position (test #19
        // x-offset): on ActionSelection entry the game re-anchors the face rect —
        // <c>AbilityCardUI.ToggleFullCard(active: true)</c> sets
        // <c>anchorMin = anchorMax = (0, 0.5)</c>, LEFT-middle (AbilityCardUI.cs:
        // 1000-1002, verified ilspycmd). With only anchoredPosition3D restored, the
        // face's pivot then sat on the host's LEFT EDGE — the card art (and the
        // backing fitted to it) rendered half a card left of the slot frame.
        if (_face.anchorMin != CenterAnchor)
            _face.anchorMin = CenterAnchor;
        if (_face.anchorMax != CenterAnchor)
            _face.anchorMax = CenterAnchor;
        if (_face.pivot != CenterAnchor)
            _face.pivot = CenterAnchor;
        if (_face.anchoredPosition3D != Vector3.zero)
            _face.anchoredPosition3D = Vector3.zero;
        if (_face.localRotation != Quaternion.identity)
            _face.localRotation = Quaternion.identity;
        // Re-fit if the host canvas was resized (VRCard.SetCanvasSize runs right after
        // Adopt to swap the placeholder host size for the real face pixels). Without
        // this the face stays fitted to the placeholder → the fat black border.
        if (_host.rect.size != _fitHostSize)
            RefreshFitScale();
        float scale = _face.localScale.x;
        if (!Mathf.Approximately(scale, _fitScale))
            _face.localScale = new Vector3(_fitScale, _fitScale, _fitScale);
    }

    /// <summary>
    /// True when <paramref name="parent"/> lives inside a <c>DialogPopup</c> — i.e. the
    /// game handed our face to a burn/lose/discard confirm popup (the only flow that
    /// re-parents a live <c>fullAbilityCard.gameObject</c> into a DialogPopup, verified
    /// CardsHandUI.cs:826/850/909/2069). Full-card PREVIEW re-parents into
    /// <c>FullCardHandViewer.CardContainer</c> instead — and is short-circuited here
    /// anyway by <c>LockFullCard</c> — so this never mis-fires on a preview.
    /// </summary>
    private static bool IsDialogContent(Transform? parent) =>
        parent != null && parent.GetComponentInParent<DialogPopup>() != null;

    /// <summary>
    /// Let go of the face WITHOUT touching its transform (a game dialog owns it now).
    /// Only the LockFullCard flag is returned.
    /// </summary>
    internal void Yield()
    {
        _reclaimedFromDialog = false;
        _artWatch.Clear();
        // The face stops being ours — hand back every quad the blackout muted (full-restore
        // contract; a dialog that shows this face must see the game's own colours).
        FaceBlackout.Restore(_face);
        if (_owner != null)
        {
            CardArtGuard.NoteReleased(_owner.fullAbilityCard);
            _owner.LockFullCard = _origLock;
        }
        _face = null;
        _host = null;
        _owner = null;
    }

    /// <summary>Give the face back to the game exactly as captured.</summary>
    internal void Restore()
    {
        RectTransform? face = _face;
        AbilityCardUI? owner = _owner;
        _face = null;
        _host = null;
        _owner = null;
        _reclaimedFromDialog = false;
        _artWatch.Clear();

        if (face == null)
            return;

        // The face stops being ours here — the guard must not keep suppressing/healing a
        // widget the game owns again (owner may already be gone during scene teardown).
        CardArtGuard.NoteReleased(owner != null ? owner.fullAbilityCard : face.GetComponent<FullAbilityCard>());

        // T3 mip bake: hand the ORIGINAL sprites back before the widget returns to the
        // game's pool (full-restore contract; guarded inside).
        CardFaceMipBake.RestoreSprites(face);

        // ...and the same for every sprite-less quad the round-2 blackout muted (see FaceBlackout).
        FaceBlackout.Restore(face);

        // Owner/parent may already be destroyed during scene teardown.
        if (owner != null)
            owner.LockFullCard = _origLock;

        if (_origParent == null)
            return;

        try
        {
            face.SetParent(_origParent, worldPositionStays: false);
            face.SetSiblingIndex(_origSibling);
            face.anchorMin = _origAnchorMin;
            face.anchorMax = _origAnchorMax;
            face.pivot = _origPivot;
            face.anchoredPosition = _origAnchoredPos;
            face.localPosition = _origLocalPos;
            face.localRotation = _origLocalRot;
            face.localScale = _origLocalScale;
            face.gameObject.SetActive(_origActive);
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CardFace.Restore partial failure (teardown race is benign): {ex.Message}");
        }
    }
}
