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

    /// <summary>
    /// True once the face was re-claimed from a burn/lose/discard confirm popup back
    /// onto our dock (see <see cref="Maintain"/>). Purely informational.
    /// </summary>
    internal bool ReclaimedFromDialog => _reclaimedFromDialog;

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

        Vector2 size = face.rect.size;
        if (size.x > 1f && size.y > 1f)
            FaceSize = size;
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

        // WHITE DECISION-PHASE FACES: from here on this face is re-activated by Maintain
        // whenever the game's pick-mode UpdateView deactivates it, and every such cycle
        // re-enters the game's addressable card-art loader. Register it so CardArtGuard can
        // stop an in-flight load from being restarted (which nulls the action-half sprites)
        // and can heal/replay afterwards — see CardArtGuard's class doc.
        CardArtGuard.NoteAdopted(owner.fullAbilityCard);
        _nextArtTick = Time.unscaledTime + CardArtGuard.TickIntervalSeconds;

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
    /// USER REPORT, verbatim (2026-08-11, hardware, ModBuild 107, b765a5b):
    /// "Die Karten im Spiel haben eine eigene Form die nicht Rechteckig ist - aktuell sind die
    /// Karten Rechtecke und der Rand der Karten ist daher schwarz. Ich möchte dass die Karten
    /// (alle Karten, auch die Itemkarten), keinen schwarzen Rand mehr haben sondern die meshes
    /// genau die Ränder der Karten selber haben."
    ///
    /// WHAT THE BLACK BORDER IS — established, not assumed. The card ART carries its own
    /// non-rectangular silhouette in its ALPHA channel; the mod's body is a rounded RECTANGLE
    /// whose near-black front (<c>CardMesh.EdgeColor</c> 0.10/0.09/0.08) sits directly behind
    /// it. Where the art is transparent, that front is what the player sees. It is NOT an inset
    /// or a letterbox: <c>VRCard.SetCanvasSize</c> scales the backing to facePixels × fit ×
    /// VisibleFaceFraction (0.94) and <see cref="BorderFraction"/> insets the art by exactly the
    /// same 6 %, so slab edge and art edge already coincide — the only dark pixels left are the
    /// ones INSIDE the art rect where the art itself is see-through. That is why shrinking the
    /// margin further (test #24) never removed it and never could.
    ///
    /// WHY IT WAS STILL THERE THOUGH THE CURE ALREADY SHIPPED. The alpha-clip cure (test #25)
    /// has been in the build since 6d7f1bb and HAS NEVER ONCE RUN. Evidence, ModBuild 107 log
    /// (3.9 MB, b765a5b — the exact commit of the report): zero <c>CardMesh.SetSilhouette</c>
    /// lines, zero "captured the card-art silhouette", zero "failed the silhouette sanity
    /// guard", zero "capture skipped" — while <c>FACE TEXTURE DIAG</c> (line 748) DOES list five
    /// live sprite textures for the very first adopted card. Both walk the same face with the
    /// same <c>GetComponentsInChildren&lt;Image&gt;</c> call in the same method invocation; the
    /// ONLY difference between them is that the capture additionally demanded
    /// <c>img.isActiveAndEnabled</c>. And that is always false at that moment:
    /// <c>VRCardFactory.CreateBlank</c> parents each new card under <c>PoolRoot</c>, which is
    /// created <c>SetActive(false)</c> ("parked cards are invisible/inactive",
    /// VRCardFactory.cs:54), and <c>AttachGameCard</c> → <see cref="Adopt"/> runs while the card
    /// is still parked there. <c>isActiveAndEnabled</c> consults <c>activeInHierarchy</c>, so
    /// every candidate image was rejected, nothing was stamped, and the pass returned through a
    /// branch that logs NOTHING. The retry budget (16 adoptions) was then burnt by the first 16
    /// pooled cards inside the first few frames — a fan adopts up to 24 at once — and latched
    /// the feature off for the session before any art had loaded.
    ///
    /// THE FIX, in three parts.
    /// 1. ACTIVE-STATE INDEPENDENCE. A footprint needs sprite pixels and rect geometry; neither
    ///    requires the object to be live. The walk is <c>includeInactive: true</c> and the test
    ///    is <c>img.enabled &amp;&amp; img.gameObject.activeSelf</c> — "the game intends this
    ///    image to be drawn" — which is true in the pool and true in the fan.
    /// 2. RETRY DRIVEN BY ART, NOT BY ADOPTION COUNT. Card art loads async
    ///    (<c>ImageAddressableLoader.LoadAsync</c>), so the useful moment is when the sprite set
    ///    CHANGES, not when the n-th card is adopted. Each attempt hashes the qualifying images'
    ///    (instance id, sprite id) pairs; an unchanged hash is skipped for free, so the expensive
    ///    readback only runs when there is genuinely something new to look at.
    /// 3. NOTHING FAILS SILENTLY. Every rejection path names itself and its numbers, deduped by
    ///    reason so a fan of 24 cards cannot spam the log.
    ///
    /// TWO SHAPES, ONE MECHANISM (his "auch die Itemkarten"). Ability cards are poker-aspect,
    /// item cards near-square; a single footprint cannot serve both, so the capture is keyed by
    /// <see cref="CardBodyKind"/> and each kind clips its own material pair. The entry point is
    /// <see cref="Offer"/>, called from <c>CardFaceMipBake.Rescan</c> — the one per-face pump
    /// BOTH kinds already run (this file on adoption + a 1 s cadence, <c>ItemsPile</c> on host +
    /// a per-frame art poll for ~2 s + a 1 s cadence). No new update loop, and the item path
    /// needs no change in a file this change does not own.
    ///
    /// REJECTED ALTERNATIVES.
    /// • Remeshing the body to a traced contour. It would have to trace the SAME runtime alpha
    ///   (the art is not readable offline — <c>ressources/</c> is Managed DLLs only), then
    ///   triangulate it, for two shapes, and every consumer of the card's bounds — the dock grab
    ///   apron, the neighbour-separation clamp, the laser hit test, <c>VRCard.WorldWidth</c> and
    ///   the record-11 mirror width — measures the rectangle. Alpha-clip changes zero vertices
    ///   and zero bounds and is exact at any distance; contour tracing would be an approximation
    ///   that also moves the collider.
    /// • Hand-authored profile constants. Cannot be measured from the assets, would have to be
    ///   guessed per card kind, and would drift the moment the game re-skins a class.
    /// • Clipping the LEGACY shared material pair (what the old code did). It is shared with the
    ///   peer mirrors and the avatar mirror; see <see cref="CardBodyKind"/>.
    /// </summary>
    /// <summary>
    /// ROUND 2 — USER REPORT, verbatim (2026-08-11, hardware, ModBuild 108, 9cbc918):
    /// "Der schwarze Rand in den Handkarten ist immer noch vollständig da (Handfächer) - Beim
    /// Charactertausch sehe ich kurz das der Schwarze Rand transparent ist und sehr schnell
    /// dannach ist das schwarz wieder da, auch auf dem Controllboard selber."
    ///
    /// THE SECOND SENTENCE IS THE PROOF, AND IT ACQUITS THE MESH. Read against the log of that
    /// exact build the capture ran, succeeded once and was applied:
    /// <code>
    /// CARD SILHOUETTE (Ability) attempt 1: footprint 224x343 stamped from 1 of 1 candidate
    ///   image(s) (0 opaque full-bleed backdrop(s) skipped) on a 294x450 px face
    /// CardMesh.SetSilhouette(Ability): footprint 224x343, opaque frac=0.888, centerOpaque=True
    /// CardMesh.SetSilhouette(Ability): APPLIED — shared front/rim + back materials → Cutout
    /// </code>
    /// 224x343 is NOT a sub-rect of the face — it is the footprint's RESOLUTION, and it spans the
    /// WHOLE face: <c>fh = round(224 * 450 / 294) = 343</c>, i.e. exactly the 294x450 face aspect.
    /// Its 0..1 square maps onto the body's planar card-space UVs, and the body is scaled to
    /// facePixels x fit x 0.94 while the face art is scaled by CardFace's own 1-0.06 — the same
    /// number — so footprint texel (u,v) sits on the card pixel it was sampled from. The mask is
    /// neither offset nor stretched.
    ///
    /// The single candidate is identified in the same log: one line earlier, in the SAME frame,
    /// <c>MIP BAKE atlas readback cached: 'AC_Berserker_Background' 1254x1916 … textureRect
    /// 1245x1863 at (2,35)</c>. 1254:1916 = 0.6545 against the face's 294:450 = 0.6533 — that IS
    /// the card's own full-bleed background art, i.e. the RIGHT source for an outer outline, and
    /// it escaped the full-bleed-backdrop skip precisely because it is trimmed. So the mesh was
    /// clipped to the real card art's alpha.
    ///
    /// And that is exactly what he saw: DURING the character switch the game's addressable loader
    /// takes the face's art down (the log's own words for the arrival path: "while the loader
    /// still had the Image disabled"), nothing paints there for a moment — and the black rim is
    /// GONE, because the mesh behind it is already clipped away. The instant the face paints
    /// again, the black is back. A clipped mesh cannot come back. **Therefore the remaining black
    /// rim is painted by the adopted uGUI FACE, not by the mod's card body.** The alpha clip
    /// shipped, works, and clips the wrong layer.
    ///
    /// WHAT THE FACE CAN PAINT THERE — and the reason we do not have to guess between them,
    /// because both are neutralised below:
    /// • A DRAWN <c>Image</c> WITH NO SPRITE. uGUI renders those as a solid quad in
    ///   <c>Image.color</c>; the very capture log counts <b>22 of them enabled on one card face</b>
    ///   ("22 without a sprite" are images that passed <c>enabled &amp;&amp; activeSelf</c>). A
    ///   card-sized dark one is a black RECTANGLE behind ornate art — which is precisely the
    ///   original report, "aktuell sind die Karten Rechtecke und der Rand der Karten ist daher
    ///   schwarz". It has no sprite, therefore it cannot carry a card shape, therefore it is never
    ///   legitimate outside the silhouette. <see cref="FaceBlackout"/> mutes exactly that.
    /// • A DARK SOFT FRINGE inside the background art's own alpha (a baked drop shadow reads as
    ///   alpha ≈ 0.5-0.9 dark pixels). Those count as "card" under a plain ≥ 0.5 test, so the mesh
    ///   is NOT clipped under them and the art paints them. <see cref="ShadowLumaMax"/> /
    ///   <see cref="ShadowAlphaCeil"/> take them out of the footprint, and the log prints how many
    ///   pixels that removed — zero says the case does not exist on this art.
    ///
    /// TWO MORE THINGS THE SAME LOG SHOWS, both addressed here.
    /// • "1 OF 1 CANDIDATE" LOOKS FRAGILE AND WAS NOT — but only by luck of timing. The one
    ///   candidate WAS the right source; the capture just happened to run in the frame the
    ///   background arrived. One frame earlier only an inner panel would have qualified, an inner
    ///   panel's rect is not the card's outer outline, and the one-shot would have latched onto it
    ///   for the session. NOTE also that unioning MORE art is not the cure some would reach for:
    ///   the action halves are large OPAQUE rectangles inside the card, so unioning them can only
    ///   push the footprint TOWARD a rectangle. The gate is therefore not "wait longer" or "union
    ///   more" but "what defines the outline must demonstrably SPAN the card" (code
    ///   <c>no-full-bleed</c>) — satisfied either by one candidate covering
    ///   <see cref="MinOutlineCoverage"/> of the face (the ability card) or by the candidates'
    ///   union bounding box doing so (a face whose background is several Images). Both readings are
    ///   timing-independent, which is the whole point: nothing here may depend on WHEN the loader
    ///   returns. A face that satisfies neither keeps the rounded rect — the shipped look — rather
    ///   than latching a guess. The sources are named in the log so this is never guesswork.
    /// • NOTHING COULD TELL A RECTANGLE FROM AN OUTLINE. frac 0.888 passes the 0.12..0.985 gate
    ///   whether it is an ornate curve or a plain inset box. <c>CardMesh.SetSilhouette</c> now
    ///   measures and logs the footprint's bounding box FILL, so the log states which one it got.
    ///
    /// THE TRANSITION ITSELF (2026-08-11, narrowed: "Der Prozess der 'Ausblendung' soll auch nicht
    /// sichtbar sein, sondern direkt die richtigen meshes sichtbar sein"). A mask captured from LIVE
    /// art cannot exist before that art loads, so inside ONE session the window is unavoidable. It
    /// is closed by not learning it in that session: <c>CardMesh</c> persists the footprint to
    /// BepInEx's cache directory and re-applies it from inside the card body's own material factory,
    /// i.e. before any renderer that will draw it has a material. First launch after installing:
    /// today's opaque rounded rect until the first card art loads, then one step. Every launch
    /// after that: correct from the first drawn pixel, on every construction path, because they all
    /// pass through that one factory.
    ///
    /// WHY NOT A uGUI <c>Mask</c> ON THE FACE (the obvious "clip the art" answer). A Mask
    /// stencil-clips every child graphic through per-graphic material variants
    /// (<c>StencilMaterial.Add</c>) — and this project has already paid for that class of change
    /// once: <c>VRCard.SetRenderOnTop</c> is a permanent no-op-forward because per-instance
    /// material copies on the face's TMP text "swallowed all card TEXT". Muting one provably
    /// shape-less Image cannot swallow anything.
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
    /// </summary>
    private const float MinOutlineCoverage = 0.7f;

    /// <summary>
    /// Pixels at or above this alpha are card no matter how dark — a genuinely dark PRINTED card
    /// border is opaque, and must survive. Only the band BELOW it can be a soft shadow.
    /// </summary>
    private const byte ShadowAlphaCeil = 230;

    /// <summary>
    /// Below this luminance (0..255) a semi-transparent pixel is treated as a baked DROP SHADOW
    /// rather than card, and taken out of the footprint. A shadow is dark by definition; card art
    /// that happens to be semi-transparent (the anti-aliased edge of a bright ornament) is not.
    /// Only ever makes the mesh clip TIGHTER, and the mesh sits BEHIND the art — so an over-eager
    /// trim removes nothing the player can see, while an under-eager one leaves today's look.
    /// </summary>
    private const byte ShadowLumaMax = 56;

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

            // (1) THE BLACKOUT FIRST, AND ALWAYS. Its input is the APPLIED mask, which — from the
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

            img.rectTransform.GetWorldCorners(corners);
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
        // Luminance of whichever sample WON each texel's alpha max — the only extra state the
        // drop-shadow rule needs (see ShadowLumaMax). One byte per texel, thrown away below.
        var luma = new byte[fw * fh];
        var readbacks = new List<Texture2D>(picks.Count);
        int stamped = 0, backdrops = 0;
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
                            // Rec.601 on the image's own tint-modulated colour.
                            float y601 = (src.r * img.color.r * 0.299f
                                        + src.g * img.color.g * 0.587f
                                        + src.b * img.color.b * 0.114f) * 255f;
                            luma[idx] = (byte)Mathf.Clamp(Mathf.RoundToInt(y601), 0, 255);
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

        // DROP-SHADOW TRIM (see ShadowLumaMax). A baked shadow around the card frame lands in the
        // 0.5..0.9 alpha band and is dark; under a plain ">= 0.5 is card" test it would hold the
        // mesh out to a soft rectangle. Opaque dark pixels (a PRINTED dark border) are untouched.
        int shadowTrimmed = 0;
        for (int i = 0; i < alpha.Length; i++)
        {
            if (alpha[i] >= 128 && alpha[i] < ShadowAlphaCeil && luma[i] <= ShadowLumaMax)
            {
                alpha[i] = 0;
                shadowTrimmed++;
            }
        }

        VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}) attempt {state.Attempts}: footprint {fw}x{fh} " +
                            $"stamped from {picks.Count - backdrops} of {picks.Count} candidate image(s) " +
                            $"({backdrops} opaque full-bleed backdrop(s) skipped) on a " +
                            $"{faceRect.width:F0}x{faceRect.height:F0} px face — sources: " +
                            $"{(sources.Length > 0 ? sources.ToString() : "none")}. Drop-shadow trim " +
                            $"(dark pixels under alpha {ShadowAlphaCeil}/255, luma <= {ShadowLumaMax}) " +
                            $"removed {shadowTrimmed} texel(s) = {(float)shadowTrimmed / alpha.Length:P1} " +
                            "of the face; 0 means this art carries no soft dark fringe. Handing it to " +
                            "CardMesh.");
        string sourceName = sources.Length > 0 ? sources.ToString() : "unnamed";

        // ALREADY APPLIED (normally: from the persisted cache, before this session drew a card).
        // Do NOT re-apply — a mid-session re-shape is exactly the visible transition the user
        // rejected — only confirm or refresh the FILE for the next launch.
        if (CardMesh.SilhouetteApplied(kind))
        {
            state.CaptureSettled = true;
            CardMesh.RefreshSilhouetteCache(kind, alpha, fw, fh, sourceName);
            return;
        }

        bool applied = CardMesh.SetSilhouette(kind, alpha, fw, fh, sourceName, fromCache: false);
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
                                "alpha-clipped to the card art's own outline, so the dark front only shows " +
                                "where the card itself is solid. This is the 2026-08-11 report " +
                                "('keinen schwarzen Rand … die meshes genau die Ränder der Karten'). This " +
                                "session is the FIRST for this shape, so the change was visible once; it is " +
                                "now cached and every later launch has it before the first card is drawn.");
        }
        else
        {
            Reject(state, kind, "guard-refused",
                   "the footprint failed CardMesh's sanity guard (see the SetSilhouette line above)");
        }
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

        /// <summary>Probe grid used to measure "how much of this quad falls outside the card".
        /// 24x36 ≈ the footprint's own aspect; 864 lookups per candidate per second.</summary>
        private const int ProbeX = 24;
        private const int ProbeY = 36;

        /// <summary>Muted images and the colour we wrote them from, keyed by instance id so a
        /// pooled/recycled widget can never collide with a live one.</summary>
        private static readonly Dictionary<int, (Image Img, Color Orig, Color Muted)> s_muted = new();

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

            Image[] images = faceRoot.GetComponentsInChildren<Image>(includeInactive: true);
            // The per-candidate detail is only ever printed by the two one-shot latches below, and
            // this pass runs once a second PER CARD — a full fan plus a browser is twenty-odd
            // instances. Build the string only while a latch can still fire; afterwards this is a
            // measurement loop with no allocation at all.
            bool wantReport = !s_logged[(int)kind] || !s_loggedMuted[(int)kind];
            System.Text.StringBuilder? report = wantReport ? new System.Text.StringBuilder(160) : null;
            int muted = 0, considered = 0;
            var corners = new Vector3[4];

            foreach (Image img in images)
            {
                if (img == null || !img.enabled || !img.gameObject.activeSelf)
                    continue;
                if (img.sprite != null)
                    continue; // carries art; its shape is the artist's business, not ours
                int imgId = img.GetInstanceID();
                if (outlineIds.Contains(imgId))
                    continue; // (cannot happen — the union needs a sprite — but state it anyway)

                // ALREADY OURS. Muting sets alpha 0, so a muted quad can never re-qualify below;
                // this is the branch that keeps it muted after the game recolours it (the game
                // does exactly that on focus/unfocus). If the game's new colour is no longer a
                // dark opaque quad, it is a legitimate visible element again — stop tracking it
                // and leave the game's value alone.
                if (s_muted.TryGetValue(imgId, out (Image Img, Color Orig, Color Muted) tracked))
                {
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
                if (area < MinOutlineAreaFraction)
                    continue; // too small to be anyone's border — not even worth reporting

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

                considered++;
                // Both gates start from "opaque AND dark AND sprite-less", which is the part that
                // makes muting safe (see the class note). They differ only in what makes the quad
                // ILLEGITIMATE: Tier A "it is a card-sized backdrop that reaches past the outline",
                // Tier B "whatever size it is, it lives mostly where the card is not".
                bool opaqueDark = c.a >= BlackoutMinAlpha && luma <= BlackoutMaxLuma;
                bool backdropTier = opaqueDark
                                    && area >= BlackoutMinAreaFraction
                                    && outsideFrac >= BlackoutMinOutsideFraction;
                bool stripTier = opaqueDark && outsideFrac >= BlackoutStripOutsideFraction;
                bool qualifies = backdropTier || stripTier;

                if (report != null)
                {
                    if (report.Length > 0)
                        report.Append("; ");
                    report.Append('\'').Append(img.name).Append("' ").Append(area.ToString("P0"))
                          .Append(" of the face, rgba(").Append(c.r.ToString("F2")).Append(',')
                          .Append(c.g.ToString("F2")).Append(',').Append(c.b.ToString("F2")).Append(',')
                          .Append(c.a.ToString("F2")).Append("), luma ").Append(luma.ToString("F2"))
                          .Append(", ").Append(outsideFrac.ToString("P0")).Append(" of it outside the card → ")
                          .Append(qualifies ? (backdropTier ? "MUTED (tier A: card-sized backdrop)"
                                                            : "MUTED (tier B: mostly outside the card)")
                                            : (c.a < BlackoutMinAlpha ? "kept (translucent — a dimmer, not a backdrop)"
                                            : luma > BlackoutMaxLuma ? "kept (not dark)"
                                            : area < BlackoutMinAreaFraction ? "kept (too small for tier A, and " +
                                                  $"under {BlackoutStripOutsideFraction:P0} outside for tier B)"
                                            : "kept (paints nothing outside the card)"));
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
            if (muted > 0 ? !s_loggedMuted[(int)kind] : (!s_logged[(int)kind] && considered > 0))
            {
                if (muted > 0)
                    s_loggedMuted[(int)kind] = true;
                s_logged[(int)kind] = true;
                VRLog.Info("Cards", $"CARD FACE BLACKOUT ({kind}): {muted} of {considered} sprite-less " +
                                    $"quad(s) on the first measured face muted — {report}. A sprite-less " +
                                    "uGUI Image is a plain RECTANGLE, so anything it paints outside the " +
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
            foreach (KeyValuePair<int, (Image Img, Color Orig, Color Muted)> kv in s_muted)
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
            Image[] images = faceRoot.GetComponentsInChildren<Image>(includeInactive: true);
            foreach (Image img in images)
            {
                if (img == null)
                    continue;
                int id = img.GetInstanceID();
                if (!s_muted.TryGetValue(id, out (Image Img, Color Orig, Color Muted) rec))
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
