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

    /// <summary>The card-shaped stencil wrapper this face is currently inside, if any. Null while
    /// no footprint exists (first run before the capture lands) or while the wrap was refused —
    /// both of which are exactly today's look. See <see cref="EnsureShapeMask"/>.</summary>
    private CardShapeMask? _shape;

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
    /// THE uGUI <c>Mask</c> ON THE FACE — built, in <see cref="CardShapeMask"/>. It bounds
    /// EVERYTHING the face draws by the card outline regardless of which component paints it, which
    /// is the only answer that does not first require naming the culprit. It was declined earlier in
    /// round 5 and then built, because the objections turned out to be about PLACEMENT and
    /// VERIFIABILITY rather than about the idea. Each one, and what answers it:
    /// <list type="number">
    /// <item>PRECEDENT — <c>VRCard.SetRenderOnTop</c> is a permanent no-op-forward because
    ///   per-instance material copies on the face's TMP text "swallowed all card TEXT". Round 5
    ///   answered: different mechanism, because <c>StencilMaterial</c>'s variants are keyed on
    ///   (base material, stencil state) and therefore SHARED by every card at the same depth.
    ///   <b>THAT ANSWER WAS WRONG AND IT IS WHAT SANK THE ROUND</b> — the objection was never about
    ///   sharing, it was about the COPY, and a mask makes one too
    ///   (<c>StencilMaterial.Add</c> → <c>new Material(baseMat)</c>). <c>CardEffects</c> gives every
    ///   card image a base material of its own and then drives the card through it every frame, so
    ///   the variants are per-image-per-card AND frozen. The precedent held exactly as stated. See
    ///   <c>CardShapeMask.Enabled</c> for the full derivation and the user report it cost.</item>
    /// <item>IT MUST SIT ON AN ANCESTOR — so it does: a mod-owned wrapper inserted between the host
    ///   and the face, never a Graphic added to the game's own <c>FullAbilityCard</c> GameObject
    ///   (<c>Graphic</c> is <c>[DisallowMultipleComponent]</c> and that GO may already own one).
    ///   <see cref="Maintain"/>'s per-frame parent assertion learns the extra level through
    ///   <see cref="IsOurParent"/> rather than fighting it.</item>
    /// <item>IT REACHES ONLY OUR HOSTS — <c>CardShapeMask.Wrap</c> takes a face rect and inserts the
    ///   wrapper into whatever parent that face already has, so the peer mirror
    ///   (<c>Net/RemoteCardArt</c>) is one call away from the identical outline. That call is not
    ///   made from here; the file is not this work's to edit.</item>
    /// <item>IT CAN SILENTLY DO NOTHING, WHICH IS THIS DEFECT'S SIGNATURE FAILURE (attempt 1 shipped
    ///   a clip that never executed). <c>MaskUtilities.FindRootSortOverrideCanvas</c> stops at the
    ///   first ancestor Canvas with <c>overrideSorting</c>, and <c>FullAbilityCard</c> carries its
    ///   own Canvas whose sorting the game toggles
    ///   (<c>CardsHandUI.ToggleFullCardCanvasSorting</c>). So the wrapper forces
    ///   <c>overrideSorting</c> false on every Canvas inside the face while we own it (recorded,
    ///   re-asserted per frame, restored exactly on release) AND then asks uGUI's own resolver
    ///   whether the stencil actually resolves for a graphic under the face. Depth 0 → the wrapper
    ///   removes itself, the face goes back exactly as found, and one log line says so. It cannot
    ///   claim a success it did not have.</item>
    /// </list>
    /// The shape-less-quad MUTING below stays alongside it: it is cheaper, it names what it found,
    /// and it still works on the one build where the stencil is refused.
    ///
    /// WHAT ROUND 5 ESTABLISHED, so round 6 starts from facts instead of re-deriving them:
    /// <list type="bullet">
    /// <item>The mask's 0..1 and the card body's 0..1 are the SAME rectangle — the "0.94 mismatch"
    ///   is refuted by arithmetic, see the note in <c>CardMesh.SetSilhouette</c>.</item>
    /// <item>Therefore, once the clip is applied, the card's visible extent equals the ART's opaque
    ///   extent: the slab is fitted to the rendered art rect and clipped by the art's own alpha, so
    ///   it can paint nothing the art does not also cover. Any black the player still sees is drawn
    ///   BY THE FACE — either inside the art's own opaque pixels or by a face graphic the blackout
    ///   was structurally unable to see.</item>
    /// <item>And the captured outline is NOT a rectangle: bounding box 95.5 % of the face, filled
    ///   0.928. If the body defined the card's visible edge, the card would have stopped looking
    ///   rectangular the moment attempt 2 landed. It did not — which is the positive evidence that
    ///   the FACE paints over the body's whole footprint, and the reason round 5 clips the face
    ///   itself (<see cref="CardShapeMask"/>) rather than scheduling it as a next round.</item>
    /// </list>
    /// Three mechanisms shipped together in ModBuild 110, and they are not alternatives: the FACE
    /// CLIP bounds whatever paints; the DARK BORDER PEEL takes a printed black frame out of the
    /// shape both the clip and the mesh use; the widened BLACKOUT still mutes provably shape-less
    /// dark quads and prints the full face inventory so the next reader sees what was actually
    /// there.
    ///
    /// <para>ROUND 6 TURNED THE FIRST OF THE THREE OFF (<c>CardShapeMask.Enabled = false</c>) and
    /// left the other two running. The clip made a shipped build flash every card fully black on a
    /// character switch, for a reason that is structural rather than tunable: masking a uGUI graphic
    /// makes it render a frozen COPY of its material, and these graphics' materials are the channel
    /// the game animates them through. The border is therefore back to its pre-110 state and is NOT
    /// closed. What round 6 does hand forward is a strictly narrower search space — the log line
    /// above is still printed, so the FULL FACE INVENTORY is still evidence, and it says that on a
    /// Berserker ability face nothing dark paints outside the outline at all: the darkest element
    /// listed is luma 0.54 and everything with any coverage outside the card ('Header' 11 %,
    /// 'UIFX_Overlay' 18 %) is WHITE. Whatever the black rim is, the inventory has now twice failed
    /// to find a face graphic that could be painting it.</para>
    ///
    /// <para>ROUND 7 CLOSED IT, AND THE THING THAT BROKE THE DEADLOCK WAS A SCREENSHOT
    /// (<c>.planning/debug/karten.png</c>). Six rounds argued about WHICH LAYER paints the black
    /// while nobody had measured WHERE it is. It is not a ring: on the board card the band is 18 px
    /// at the top and 25 px at the bottom of a 378 px card, against 2 px and 6 px at the sides of a
    /// 254 px card. A band on the short axis only is a LETTERBOX, and the letterbox is arithmetic
    /// anyone could have done from the log's own "294x450 px face": the card body is fitted to that
    /// rect (aspect 0.6533) and the ability card's art inside it is poker-shaped (0.7216), so
    /// <c>Image.preserveAspect</c> draws the art at 90.54 % of the rect's height and leaves 4.73 %
    /// dead top and bottom. Measured: 4.76 %. The capture normalised its candidates by their LAYOUT
    /// rect, so the mask was stretched +10.4 % vertically and declared the body "card" in exactly
    /// those two bands — every clip since ModBuild 108 was correct and aimed 10 % away from the edge
    /// it was meant to find. The whole cure is <see cref="DrawnLocalRect"/>: stamp into the rect the
    /// art is actually drawn in. Read that method before touching anything here. Nothing about the
    /// card's size, proportions or behaviour changes — the user ruled those fixed ("Ich möchte gerne
    /// an den aktuellen Proportionen festhalten … nur eben ohne die schwarzen Ränder") and this is a
    /// coordinate correction on the MASK alone. The other candidate (a printed black frame inside
    /// the art) is neutralised in the same build rather than left for an eighth run: see
    /// <see cref="DarkBorderMaxDepthFraction"/>, whose cap the ModBuild-110 log reported as
    /// REACHED.</para>
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

    // ------------------------------------------------- dark border peel (round 5) --
    //
    // "STOP TREATING OPAQUE AS CARD."  The drop-shadow trim above only reaches SEMI-transparent
    // dark pixels.  A PRINTED black frame — a dark band that is fully opaque and sits on the
    // outside of the art — is therefore card by the capture's own definition, is unioned into the
    // footprint, and no alpha clip built from that footprint can ever remove it.  If the black
    // rectangle the user still reports is that band, every previous round was arithmetically
    // correct and visually a no-op, which is exactly what he described ("Exakt gleiches Fehlerbild
    // wie zuvor.  Hat sich an den Kartenrändern nichts geändert.", ModBuild 109).
    //
    // The peel below erodes inward from the silhouette's OUTER boundary for as long as the texels
    // it meets are opaque AND near-black, up to a hard depth cap.  It is a boundary band by
    // construction (the flood is seeded only from texels that are already outside, plus the
    // footprint's own image border), so a dark ornament in the middle of the card is unreachable.
    //
    // THE PEEL AND THE FACE CLIP ARE ONE MECHANISM — READ THEM TOGETHER.  On its own the peel would
    // be cosmetically inert: the footprint drives the MESH, the mesh sits BEHIND the art, and
    // shrinking it under an OPAQUE art pixel changes nothing the player can see.  That was the
    // licence the drop-shadow trim above rests on, and it is exactly why a mesh-side answer alone
    // could never close this defect.  <see cref="CardShapeMask"/> now clips the FACE to this same
    // footprint, so what the peel removes is genuinely removed from the picture: peel the printed
    // black frame out of the mask, and the face clip stops the art drawing it.
    //
    // THAT INVERTS THE SAFETY ARGUMENT, so the guards are the safety now, not the invisibility:
    //   • DARK.  Only near-black texels qualify (<see cref="DarkBorderLumaMax"/>) — "der schwarze
    //     Rand", not a merely dark ornament.
    //   • THIN.  <see cref="DarkBorderMaxDepthFraction"/> caps how far in it can reach; a frame is a
    //     few millimetres, and nothing deeper is a frame.
    //   • BOUNDED.  Past <see cref="DarkBorderMaxAreaFraction"/> of the opaque area the whole peel is
    //     DISCARDED rather than partially applied — a dark CARD is not a dark FRAME, and the
    //     standing rule is to degrade to the shape we already had, never to a guessed one.
    //   • BOUNDARY-ONLY BY CONSTRUCTION.  The flood is seeded solely from texels that are already
    //     outside (plus the footprint's own image border), so a black ornament in the middle of the
    //     card is unreachable at any threshold.
    //
    // AND IT IS THE ROUND'S FALSIFIER.  The log line always prints, peel or no peel.  Read it
    // together with the CARD FACE CLIP line: peel > 0 and clip VERIFIED means the printed frame was
    // there and is now gone; peel ~0 and clip VERIFIED means the black was never in the art and the
    // clip has bounded whatever else was painting it; clip REFUSED means the face cannot be
    // stencilled from above its own Canvas on this build and says so in one line rather than
    // pretending.
    //
    // ROUND 8: THE FRAME IS NOW REMOVED UPSTREAM. The peel twice measured the ring AT ITS CAP
    // (ModBuild 110: depth 11 of 11; ModBuild 111: depth 18 of 18, mean luma 22) — proof the frame
    // is real and thicker than every guessed cap. The cure moved to the SOURCE: CardFaceMipBake's
    // FRAME PUNCH erases those pixels from the mod-owned sprite copies the face renders, with a
    // LEARNED depth, and the capture samples the punched sprites — so this peel now runs on art
    // that should already be frameless. It stays as the backstop and the falsifier: "0 texel(s)"
    // here together with a successful CARD FRAME PUNCH line means the punch removed the frame and
    // the footprint agrees with it; a large peel here means a face-spanning sprite ESCAPED the
    // punch (its gate/refusal line says why).

    /// <summary>
    /// Maximum Rec.601 luminance (0..255) an OPAQUE boundary texel may have and still be peeled as
    /// "printed black frame" rather than card. 48 is the deliberate compromise: the mod's own card
    /// edge is 0.10/0.09/0.08 → luma 24, a printed ink frame the player calls "schwarz" sits under
    /// ~50, and card ART — parchment, illustration, the coloured action halves — is far brighter.
    ///
    /// <para>It is NOT set generously, because the face clip makes an over-eager peel VISIBLE: it
    /// would cut real art off the card rather than merely shrinking a hidden mesh. If a future
    /// report says a dark-but-not-black frame survived, this number is the turn to make — a
    /// threshold change, not a redesign.</para>
    /// </summary>
    private const byte DarkBorderLumaMax = 48;

    /// <summary>
    /// How deep the peel may reach, as a fraction of the footprint's SHORT side.
    ///
    /// <para>ROUND 7 RAISED THIS FROM 0.05 TO 0.08, and the reason is a measurement, not a hunch.
    /// The ModBuild-110 log reports the peel <b>at its cap</b>: "945 texel(s) = 1.23 % of the face
    /// … max depth 11 of 11 texels". 11 texels is 0.05 × min(224, 343) ≈ 3.1 mm on a 63.5 mm card.
    /// The band the user's screenshot actually shows is 4.76 % of the card's height — 21.4 face px,
    /// i.e. ≈ 16 texels ≈ 4.6 mm — so the old cap could not have reached it at any luma threshold.
    /// 0.08 gives 18 texels ≈ 5.1 mm: enough for that band with a texel to spare, still nowhere near
    /// hollowing a card out.</para>
    ///
    /// <para>AND THE SAFETY ARGUMENT HAS INVERTED BACK. Round 5 tightened this deliberately because
    /// <c>CardShapeMask</c> clipped the FACE to the same footprint, which made an over-eager peel
    /// VISIBLE (it would have cut real art away). <c>dfe54e0</c> turned that clip off and it stays
    /// off, so the footprint drives the MESH only — and the mesh sits BEHIND the art. A peel that
    /// takes one texel too many now hides mesh under opaque art and changes nothing the player can
    /// see; a peel that takes one texel too few leaves the reported black band. The asymmetry runs
    /// the other way than it did in round 5, so the number does too.</para>
    /// </summary>
    private const float DarkBorderMaxDepthFraction = 0.08f;

    /// <summary>
    /// Hard ceiling on what the peel may take, as a fraction of the OPAQUE area it started
    /// from. Past this the footprint is a dark card, not a dark frame, and the whole peel is
    /// discarded (logged) rather than partially applied — degrade to the shape we already had,
    /// never to a guessed one.
    ///
    /// <para>ROUND 7 RAISED THIS FROM 0.12 TO 0.20 so the deeper cap above is reachable instead of
    /// self-cancelling. The band this has to be able to take is the two LONG edges only:
    /// 2 × 224 × 16 = 7168 texels = 10.7 % of the 0.874 opaque area the ModBuild-110 log measured —
    /// which sat directly on the old 12 % ceiling, so a slightly thicker frame would have tripped
    /// the all-or-nothing discard and shipped round six's look with a log line claiming a peel was
    /// considered. 0.20 clears it with margin. The guard keeps doing its actual job: a full ring at
    /// the new depth cap would be ≈ 30 % and is still discarded, i.e. a dark CARD is still not a
    /// dark FRAME.</para>
    /// </summary>
    private const float DarkBorderMaxAreaFraction = 0.20f;

    /// <summary>
    /// Erode the opaque, near-black band on the OUTSIDE of a captured footprint (see the block
    /// above). Mutates <paramref name="alpha"/> in place and returns how many texels went; returns
    /// 0 and leaves the mask untouched when nothing qualifies or the caps are exceeded.
    /// </summary>
    private static int PeelDarkBorder(byte[] alpha, byte[] luma, int fw, int fh,
                                      out int maxDepthReached, out int meanLuma, out bool capped)
    {
        maxDepthReached = 0;
        meanLuma = 0;
        capped = false;
        int maxDepth = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(fw, fh) * DarkBorderMaxDepthFraction));

        long opaqueBefore = 0;
        for (int i = 0; i < alpha.Length; i++)
            if (alpha[i] >= 128)
                opaqueBefore++;
        if (opaqueBefore <= 0)
            return 0;

        // depth[i] == 0 -> unvisited; >0 -> peel distance from the outside.
        var depth = new ushort[alpha.Length];
        var queue = new Queue<int>(1024);
        var peeled = new List<int>(1024);
        long lumaSum = 0;

        void Seed(int idx)
        {
            if (depth[idx] != 0 || alpha[idx] < 128 || luma[idx] > DarkBorderLumaMax)
                return;
            depth[idx] = 1;
            queue.Enqueue(idx);
            peeled.Add(idx);
            lumaSum += luma[idx];
        }

        // Seeds: every opaque near-black texel that touches a TRANSPARENT texel, plus the
        // footprint's own image border (a card whose art runs to the very edge of the face rect
        // has no transparent neighbour there, and its frame must still be reachable).
        for (int y = 0; y < fh; y++)
        {
            int row = y * fw;
            for (int x = 0; x < fw; x++)
            {
                int i = row + x;
                if (alpha[i] >= 128)
                {
                    if (x == 0 || y == 0 || x == fw - 1 || y == fh - 1)
                        Seed(i);
                    continue;
                }
                // transparent: its opaque 4-neighbours are on the outer boundary
                if (x > 0) Seed(i - 1);
                if (x < fw - 1) Seed(i + 1);
                if (y > 0) Seed(i - fw);
                if (y < fh - 1) Seed(i + fw);
            }
        }

        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int d = depth[i];
            if (d > maxDepthReached)
                maxDepthReached = d;
            if (d >= maxDepth)
                continue;
            int x = i % fw, y = i / fw;
            void Step(int n)
            {
                if (depth[n] != 0 || alpha[n] < 128 || luma[n] > DarkBorderLumaMax)
                    return;
                depth[n] = (ushort)(d + 1);
                queue.Enqueue(n);
                peeled.Add(n);
                lumaSum += luma[n];
            }
            if (x > 0) Step(i - 1);
            if (x < fw - 1) Step(i + 1);
            if (y > 0) Step(i - fw);
            if (y < fh - 1) Step(i + fw);
        }

        if (peeled.Count == 0)
            return 0;
        meanLuma = (int)(lumaSum / peeled.Count);
        if (peeled.Count > opaqueBefore * DarkBorderMaxAreaFraction)
        {
            capped = true;
            return 0; // nothing applied — this is a dark CARD, not a dark FRAME
        }
        for (int p = 0; p < peeled.Count; p++)
            alpha[peeled[p]] = 0;
        return peeled.Count;
    }

    // ------------------------------------------------- frame punch sweep (round 8) --

    /// <summary>
    /// The face-side driver of the FRAME PUNCH. ROUND 9: the punch region is GEOMETRIC — the
    /// card's true outline, derived once per background art by <see cref="CardOutline"/> from the
    /// bright trim contour — and it is applied to EVERY full-span face layer, not only to the one
    /// image the old area gate admitted. The 112 log forced both changes: the BFS-punched
    /// background still painted 102 of 264 near-black band probes (dark pixels the luma
    /// connectivity could not reach), and the two action-half plates painted 55 of their 72 band
    /// probes while the "&gt;= 70 % of the face" AREA gate excluded them — yet they are genuine
    /// face layers spanning the full card width at top and bottom, and the bottom band (the
    /// thickest, 6.6 % of the card in the screenshot) is largely theirs.
    ///
    /// <para>THE GATES, restated for round 9 — two of them, with different jobs:
    /// (1) the OLD coverage gate (<see cref="MinOutlineCoverage"/> of the face) now has exactly
    /// ONE remaining job: choosing which sprite the OUTLINE is derived from (the full-bleed
    /// background). It no longer decides what gets punched.
    /// (2) eligibility for PUNCHING is the SPAN gate (<see cref="SpanAxisFraction"/> on either
    /// axis of the drawn rect): a face LAYER spans the card on at least one axis (background:
    /// both; action halves: full width), while a decoration does not. This is what makes the
    /// punch safe for icons, buttons and portraits BY CONSTRUCTION — a small centred icon never
    /// spans an axis, so it can never be eroded — and it also protects designed protrusions (the
    /// class banner at the top, the initiative chip at the bottom) that legitimately poke past
    /// the background's trim contour: they are narrow, they fail the span gate, and the
    /// screenshot-derived outline follows their bright edging anyway where they are part of the
    /// background art. The 112 BAND INVENTORY confirms the closure: exactly three graphics
    /// contributed dark band pixels, and all three span the full card width.</para>
    ///
    /// <para>THE SIMPLE-ONLY FILTER WAS THE ACTION HALVES' ESCAPE HATCH, and the 112 log proves
    /// it by arithmetic: the sweep reported "1 image(s) passed … 11 below the gate (largest
    /// 1 %)", yet the halves draw at ~18 % coverage — they are in NEITHER count, so they fell to
    /// the <c>img.type != Simple</c> skip. They are the game's prefab-serialized 9-SLICED button
    /// plates (<c>FullAbilityCardAction.actionButton</c> is a <c>Button</c> with a SpriteSwap
    /// transition). Round 9 therefore maps Sliced (and full Filled) images through uGUI's own
    /// 9-slice geometry (<c>CardFaceMipBake.PunchMapping</c>) instead of skipping them; only
    /// Tiled/partially-filled images remain unmappable, and the sweep line counts them. Known
    /// residual, pre-existing (STATE §5e): a hovered half renders the game's
    /// <c>Image.overrideSprite</c> state sprite, which no bake or punch has ever covered — the
    /// frame can reappear inside the plate WHILE hovered, hover-transient only.</para>
    ///
    /// <para>WHEN IT RUNS: unchanged — from <see cref="Offer"/>, i.e. on adoption, on the 1 s
    /// Rescan cadence, and on the ART-ARRIVAL seam in the same frame the game assigns the sprite,
    /// before its first rendered frame. A punched copy is what the face shows from its first
    /// pixel.</para>
    ///
    /// <para>DEGRADES TO TODAY, in two stages: no validated outline (see CardOutline's gates and
    /// its refusal line) ⇒ this sweep falls back to the EXACT ModBuild-112 behaviour (luma-BFS
    /// punch of the coverage-gate layer only); a refused individual punch (unextractable region,
    /// budget, implausible mapping) ⇒ that sprite keeps its unpunched copy, logged with numbers
    /// by the bake. Never a guessed shape.</para>
    /// </summary>
    private static class FramePunch
    {
        /// <summary>A drawn rect spanning at least this fraction of the face on EITHER axis marks
        /// a face LAYER (eligible for the geometric punch). Backgrounds span both axes, the
        /// action halves span the full width (their drawn width ≈ 100 % of the face); the largest
        /// decoration (an action half is a layer, a banner/chip/icon is not) stays well under.</summary>
        private const float SpanAxisFraction = 0.9f;

        private static readonly bool[] s_sweepLogged = new bool[3];
        private static bool s_gateOffLogged;
        private static bool s_errorLogged;

        internal static void Sweep(RectTransform faceRoot, CardBodyKind kind)
        {
            try
            {
                if (CardsConfig.FaceMipBake == null || !CardsConfig.FaceMipBake.Value)
                {
                    // The punch lives on the mip-baked copies; with the bake dial off there are no
                    // copies to punch and the face renders the game's originals — frame included.
                    if (!s_gateOffLogged)
                    {
                        s_gateOffLogged = true;
                        VRLog.Info("Cards", "CARD FRAME PUNCH inactive: [Cards] FaceMipBake is OFF, so no " +
                                            "mod-owned sprite copies exist to erase the printed frame from. " +
                                            "Cards keep the game's own art, black frame included.");
                    }
                    return;
                }
                Rect faceRect = faceRoot.rect;
                if (faceRect.width < 1f || faceRect.height < 1f)
                    return;

                // Pass 1: every drawn sprite Image whose pixel-to-face mapping is derivable —
                // Simple and full Filled map uniformly, SLICED plates map through the 9-slice
                // function (the ModBuild-112 action halves: 55 of 72 band probes, and absent from
                // both sweep counts — they never passed the old Simple-only filter). Tiled and
                // partially filled images have no derivable static mapping and are counted.
                Image[] images = faceRoot.GetComponentsInChildren<Image>(includeInactive: true);
                var entries = new List<(Image Img, Sprite Worn, CardFaceMipBake.PunchMapping Map)>(8);
                var corners = new Vector3[4];
                Image? outlineImg = null;
                Rect outlineRect = default;
                float outlineCoverage = 0f;
                int unmappable = 0;
                foreach (Image img in images)
                {
                    if (img == null || !img.enabled || !img.gameObject.activeSelf)
                        continue;
                    Sprite? worn = img.sprite;
                    if (worn == null)
                        continue;
                    Image.Type type = img.type;
                    CardFaceMipBake.PunchMapping map;
                    if (type == Image.Type.Simple)
                    {
                        // Drawn rect (preserveAspect letterbox corrected — round 7).
                        Rect norm = FaceNormRectOf(img.rectTransform, DrawnLocalRect(img, worn, out _),
                            faceRoot, faceRect, corners);
                        if (norm.width <= 0f || norm.height <= 0f)
                            continue;
                        map = new CardFaceMipBake.PunchMapping(norm);
                        // Gate (1): the outline SOURCE must span the card — same full-bleed
                        // reading the capture uses, and only a uniformly mapped (Simple) image
                        // qualifies. Largest qualifying candidate wins (the background).
                        float coverage = norm.width * norm.height;
                        if (coverage >= MinOutlineCoverage && coverage > outlineCoverage)
                        {
                            outlineImg = img;
                            outlineRect = norm;
                            outlineCoverage = coverage;
                        }
                    }
                    else if (type == Image.Type.Sliced
                             || (type == Image.Type.Filled && img.fillAmount >= 0.999f))
                    {
                        // Sliced/full-filled draw across their LAYOUT rect (preserveAspect is a
                        // Simple-only feature in uGUI's OnPopulateMesh).
                        Rect norm = FaceNormRectOf(img.rectTransform, img.rectTransform.rect,
                            faceRoot, faceRect, corners);
                        if (norm.width <= 0f || norm.height <= 0f)
                            continue;
                        Vector4 borderPx = worn.border;
                        if (type != Image.Type.Sliced || borderPx == Vector4.zero)
                        {
                            // A sliced image without a border renders exactly like Simple.
                            map = new CardFaceMipBake.PunchMapping(norm);
                        }
                        else
                        {
                            // uGUI GenerateSlicedSprite: borders in local units are
                            // sprite.border / multipliedPixelsPerUnit, clamp-scaled per axis when
                            // the rect is smaller than the combined borders — replicated from
                            // live values, then converted into face-normalized units.
                            float ppm = Mathf.Max(0.01f, img.pixelsPerUnit * img.pixelsPerUnitMultiplier);
                            Vector4 local = borderPx / ppm;
                            Rect layout = img.rectTransform.rect;
                            for (int axis = 0; axis <= 1; axis++)
                            {
                                float combined = local[axis] + local[axis + 2];
                                float size = axis == 0 ? layout.width : layout.height;
                                if (combined > size && combined > 0f)
                                {
                                    float ratio = size / combined;
                                    local[axis] *= ratio;
                                    local[axis + 2] *= ratio;
                                }
                            }
                            float sx = norm.width / Mathf.Max(0.01f, layout.width);
                            float sy = norm.height / Mathf.Max(0.01f, layout.height);
                            var dest = new Vector4(local.x * sx, local.y * sy, local.z * sx, local.w * sy);
                            map = new CardFaceMipBake.PunchMapping(norm, borderPx, dest);
                        }
                    }
                    else
                    {
                        unmappable++;
                        continue;
                    }
                    entries.Add((img, worn, map));
                }
                if (entries.Count == 0)
                    return;

                CardOutline? outline = null;
                if (outlineImg != null && outlineImg.sprite != null)
                {
                    Sprite outlineSource = CardFaceMipBake.OriginalOf(outlineImg.sprite) ?? outlineImg.sprite;
                    outline = CardOutline.ForSource(kind, outlineSource, outlineRect);
                }

                int layers = 0, repointed = 0, wearing = 0, kept = 0, decorations = 0;
                if (outline != null)
                {
                    // GEOMETRIC MODE (round 9): every full-span layer is punched against the one
                    // derived outline — background AND action halves AND any further layer.
                    foreach ((Image img, Sprite worn, CardFaceMipBake.PunchMapping map) in entries)
                    {
                        Rect norm = map.FaceRect;
                        bool spans = norm.width >= SpanAxisFraction || norm.height >= SpanAxisFraction;
                        if (!spans)
                        {
                            decorations++;
                            continue;
                        }
                        layers++;
                        Sprite source = CardFaceMipBake.OriginalOf(worn) ?? worn;
                        Sprite? punched = CardFaceMipBake.OutlinePunchedReplacementFor(source, map, outline);
                        if (punched == null)
                        {
                            kept++; // clean (nothing outside) or refused — the punch log names which
                            continue;
                        }
                        if (!ReferenceEquals(worn, punched))
                        {
                            img.sprite = punched;
                            repointed++;
                        }
                        else
                        {
                            wearing++;
                        }
                    }
                    if (layers > 0 && !s_sweepLogged[(int)kind])
                    {
                        s_sweepLogged[(int)kind] = true;
                        VRLog.Info("Cards", $"CARD FRAME PUNCH sweep ({kind}, GEOMETRIC): outline from " +
                                            $"'{outline.SourceName}' (bands {outline.BandSummary}); eligibility " +
                                            $"= drawn rect spanning >= {SpanAxisFraction:P0} of the " +
                                            $"{faceRect.width:F0}x{faceRect.height:F0} px face on either axis. " +
                                            $"{layers} layer(s) eligible ({repointed} re-pointed to punched " +
                                            $"copies, {wearing} already wearing one, {kept} kept unpunched — " +
                                            $"clean or refused, the punch lines name which); {decorations} " +
                                            $"sprite(s) are decorations/icons and were never touched; " +
                                            $"{unmappable} tiled/partial-filled image(s) have no derivable " +
                                            "mapping and were never touched.");
                    }
                }
                else if (outlineImg != null)
                {
                    // FALLBACK = the EXACT ModBuild-112 behaviour: luma-BFS punch of the
                    // coverage-gate layer(s) only. The CardOutline refusal line above this one
                    // names why the geometric mode is unavailable for this art.
                    int spanning = 0;
                    foreach ((Image img, Sprite worn, CardFaceMipBake.PunchMapping map) in entries)
                    {
                        Rect norm = map.FaceRect;
                        if (map.Sliced || norm.width * norm.height < MinOutlineCoverage)
                            continue; // 112 punched Simple coverage-gate layers only
                        spanning++;
                        Sprite source = CardFaceMipBake.OriginalOf(worn) ?? worn;
                        Sprite? punched = CardFaceMipBake.PunchedReplacementFor(source);
                        if (punched == null)
                        {
                            kept++;
                            continue;
                        }
                        if (!ReferenceEquals(worn, punched))
                        {
                            img.sprite = punched;
                            repointed++;
                        }
                        else
                        {
                            wearing++;
                        }
                    }
                    if (spanning > 0 && !s_sweepLogged[(int)kind])
                    {
                        s_sweepLogged[(int)kind] = true;
                        VRLog.Info("Cards", $"CARD FRAME PUNCH sweep ({kind}, FALLBACK — no validated " +
                                            $"outline, see the CARD OUTLINE line): ModBuild-112 luma-BFS punch " +
                                            $"of {spanning} coverage-gate layer(s) ({repointed} re-pointed, " +
                                            $"{wearing} wearing, {kept} kept unpunched). The action halves are " +
                                            "NOT punched in this mode; if the band survives here, the outline " +
                                            "refusal is the lead.");
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (!s_errorLogged)
                {
                    s_errorLogged = true;
                    VRLog.Warn("Cards", $"CARD FRAME PUNCH sweep failed ({ex.GetType().Name}: {ex.Message}) — " +
                                        "faces keep their unpunched copies (today's look).");
                }
            }
        }

        /// <summary>A local rect on <paramref name="irt"/> (drawn or layout, the caller decides —
        /// the round-7 letterbox correction applies to Simple images only) in face-normalized
        /// 0..1 coordinates. May extend past 0..1 when the graphic overhangs the face.</summary>
        private static Rect FaceNormRectOf(RectTransform irt, Rect drawnLocal, RectTransform faceRoot,
                                           Rect faceRect, Vector3[] corners)
        {
            corners[0] = irt.TransformPoint(new Vector3(drawnLocal.xMin, drawnLocal.yMin, 0f));
            corners[1] = irt.TransformPoint(new Vector3(drawnLocal.xMin, drawnLocal.yMax, 0f));
            corners[2] = irt.TransformPoint(new Vector3(drawnLocal.xMax, drawnLocal.yMax, 0f));
            corners[3] = irt.TransformPoint(new Vector3(drawnLocal.xMax, drawnLocal.yMin, 0f));
            float minNx = float.MaxValue, minNy = float.MaxValue;
            float maxNx = float.MinValue, maxNy = float.MinValue;
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
            return Rect.MinMaxRect(minNx, minNy, maxNx, maxNy);
        }
    }

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

            // (0) THE FRAME PUNCH, BEFORE EVERYTHING (round 8; round 9 made the region GEOMETRIC).
            // The black band is pixels in the face layers' OWN art outside the card's true
            // outline — the bright-trim contour CardOutline derives from the background sprite
            // (the 112 log killed the luma-connectivity definition: BFS stopped at 24 px with
            // 102 of 264 band probes still dark, and the never-punched action halves painted 55
            // of 72). The sweep erases every full-span layer outside that outline; see the
            // FramePunch doc. It runs before the capture ON PURPOSE: the capture samples
            // img.sprite, so once the sweep has re-pointed the layers at their punched copies,
            // the footprint is stamped FROM the punched pixels — and TryCapture additionally
            // intersects the footprint with the same outline, so the mesh clip and the face are
            // one geometry by construction. Runs on every offer (cheap dictionary hits once
            // warm) so every class and both card kinds are covered, not just the one class the
            // once-per-session capture happens to see.
            FramePunch.Sweep(root, kind);

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
        // Luminance of whichever sample WON each texel's alpha max — the only extra state the
        // drop-shadow rule needs (see ShadowLumaMax). One byte per texel, thrown away below.
        var luma = new byte[fw * fh];
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

        // DARK BORDER PEEL (round 5) — see the block above PeelDarkBorder for the derivation and
        // for why this line is the round's falsifier. Runs on every capture, logs either way.
        int peeled = PeelDarkBorder(alpha, luma, fw, fh, out int peelDepth, out int peelLuma,
                                    out bool peelCapped);
        VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}) DARK BORDER PEEL: {peeled} texel(s) = " +
                            $"{(float)peeled / alpha.Length:P2} of the face removed as a printed black " +
                            $"frame (opaque, luma <= {DarkBorderLumaMax}, reachable from outside, max depth " +
                            $"{peelDepth} of {Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(fw, fh) * DarkBorderMaxDepthFraction))} " +
                            $"texels; mean luma of what went = {peelLuma})" +
                            (peelCapped
                                ? " — DISCARDED: the band exceeded " +
                                  $"{DarkBorderMaxAreaFraction:P0} of the opaque area, so this is a dark CARD, " +
                                  "not a dark FRAME. Footprint kept exactly as captured."
                                : peeled == 0
                                    ? " — the card art carries NO opaque black band on its outside, so the " +
                                      "black border the user reports is not printed into the art: it is drawn " +
                                      "by something on the FACE (see the blackout line) or by a body this " +
                                      "clip does not reach."
                                    : " — the mask now describes the card WITHOUT its printed frame. The mesh " +
                                      "sits BEHIND the art, so this alone cannot uncover a band the ART paints; " +
                                      "what it does buy is a tighter definition of 'outside the card' for the " +
                                      "face blackout below."));

        // OUTLINE INTERSECTION (round 9) — the third consumer of the ONE geometry. The punched
        // sprites the capture just sampled are already erased outside the outline, so in the
        // normal case this removes little; its job is the guarantee: whatever any candidate
        // stamped (a sprite whose individual punch was refused, a peel that fell short), the
        // footprint the MESH clips to can never extend past the same outline the FACE is punched
        // to. Mesh, sprite punch and the (disabled) CardShapeMask share one InsideFace answer.
        CardOutline? kindOutline = CardOutline.ForKind(kind);
        if (kindOutline != null)
        {
            int outlineTrimmed = 0;
            for (int fy = 0; fy < fh; fy++)
            {
                float v = (fy + 0.5f) / fh;
                int rowBase2 = fy * fw;
                for (int fx = 0; fx < fw; fx++)
                {
                    int i = rowBase2 + fx;
                    if (alpha[i] == 0)
                        continue;
                    if (!kindOutline.InsideFace((fx + 0.5f) / fw, v))
                    {
                        alpha[i] = 0;
                        outlineTrimmed++;
                    }
                }
            }
            VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}) OUTLINE CLIP: footprint intersected with the " +
                                $"derived card outline (from '{kindOutline.SourceName}', bands " +
                                $"{kindOutline.BandSummary}) — {outlineTrimmed} texel(s) = " +
                                $"{(float)outlineTrimmed / alpha.Length:P2} of the face removed. Mesh clip, " +
                                "sprite punch and the (disabled) shape mask now share ONE geometry; a band " +
                                "that survives this build is NOT painted inside these bounds.");
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

        // THE ROUND-7 FALSIFIER. This line always prints and it says, in one number, whether the
        // defect this round diagnosed exists on the user's art: how much of the face rect the card
        // art actually DRAWS on. 100 % on both axes means uGUI is stretching the sprite to the whole
        // rect after all, the letterbox theory is wrong for this card, and the remaining black has
        // to be inside the art itself (read the DARK BORDER PEEL line above — that is the other
        // candidate and it is neutralised in the same build). Anything below 100 % on an axis is a
        // band the BODY used to paint and no longer does.
        Rect artRect = (artMaxX > artMinX && artMaxY > artMinY)
            ? Rect.MinMaxRect(Mathf.Clamp01(artMinX), Mathf.Clamp01(artMinY),
                              Mathf.Clamp01(artMaxX), Mathf.Clamp01(artMaxY))
            : new Rect(0f, 0f, 1f, 1f);
        VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}) ART RECT: the card art DRAWS on " +
                            $"{artRect.width:P1} x {artRect.height:P1} of the {faceRect.width:F0}x" +
                            $"{faceRect.height:F0} px face rect (x {artRect.xMin:F3}..{artRect.xMax:F3}, " +
                            $"y {artRect.yMin:F3}..{artRect.yMax:F3}); {aspectCorrected} of {picks.Count} " +
                            "candidate(s) are preserveAspect and were letterboxed inside their own layout " +
                            "rect. The card BODY is fitted to the FULL face rect (VRCard.SetCanvasSize), so " +
                            "any axis below 100 % here is exactly the black band of the 2026-08-11 report: " +
                            "the mask is now transparent there and the slab stops painting it. 100 % x 100 % " +
                            "means this round's cause is absent on this art and the DARK BORDER PEEL line " +
                            "above carries the other candidate.");

        // ATTEMPT-EIGHT FALLBACK DIAGNOSTIC: if the band survives THIS build too, the next log
        // must name the culprit graphic outright instead of costing a ninth round of inference.
        LogFrameBandInventory(faceRoot, kind, faceRect);

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
    /// <c>CardsConfig.CardHeight</c>, the mesh, the collider, <c>VRCard.WorldWidth</c>, the fan, the
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

    // ------------------------------------------- frame band inventory (round 8 diag) --

    /// <summary>Latch: one FRAME BAND INVENTORY line per card kind per session.</summary>
    private static readonly bool[] s_bandInventoryLogged = new bool[3];

    /// <summary>The outer band of the face the inventory samples, as a fraction of each axis.
    /// 8 % comfortably contains the measured band (4.76 % of the card height).</summary>
    private const float BandFraction = 0.08f;

    /// <summary>
    /// ATTEMPT-EIGHT DIAGNOSTIC (runs once per kind, on capture): name EVERY drawn graphic that
    /// contributes opaque near-black pixels to the outer <see cref="BandFraction"/> band of the
    /// face, with counts — so if the black band survives this build too, the next hardware log
    /// names the culprit graphic outright and ends the guessing. Sprite-bearing Images are sampled
    /// at their real pixels (GPU readback, trim-correct, drawn-rect mapping); sprite-less quads
    /// and other Graphic types are judged by their colour (stated in the line, so the method is on
    /// the record with the number). Failure here only costs the line, never the capture.
    /// </summary>
    private static void LogFrameBandInventory(RectTransform faceRoot, CardBodyKind kind, Rect faceRect)
    {
        if (s_bandInventoryLogged[(int)kind])
            return;
        s_bandInventoryLogged[(int)kind] = true;
        var readbacks = new List<Texture2D>(4);
        try
        {
            const int probeX = 24, probeY = 36;
            const float lumaMax = 48f / 255f;
            Graphic[] graphics = faceRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
            var sb = new System.Text.StringBuilder(192);
            int checkedCount = 0, silent = 0;
            var corners = new Vector3[4];
            var regionCache = new Dictionary<int, Texture2D?>();
            foreach (Graphic g in graphics)
            {
                if (g == null || !g.enabled || !g.gameObject.activeSelf)
                    continue;
                checkedCount++;

                Image? gImg = g as Image;
                Sprite? sprite = gImg != null ? gImg.sprite : null;
                // Where does this graphic DRAW? Sprite Images: the letterbox-corrected drawn rect;
                // everything else: its layout rect (a quad/text fills it).
                Rect local = gImg != null && sprite != null
                    ? DrawnLocalRect(gImg, sprite, out _)
                    : ((RectTransform)g.transform).rect;
                RectTransform grt = (RectTransform)g.transform;
                corners[0] = grt.TransformPoint(new Vector3(local.xMin, local.yMin, 0f));
                corners[1] = grt.TransformPoint(new Vector3(local.xMin, local.yMax, 0f));
                corners[2] = grt.TransformPoint(new Vector3(local.xMax, local.yMax, 0f));
                corners[3] = grt.TransformPoint(new Vector3(local.xMax, local.yMin, 0f));
                float minNx = 1f, minNy = 1f, maxNx = 0f, maxNy = 0f;
                for (int c = 0; c < 4; c++)
                {
                    Vector3 p = faceRoot.InverseTransformPoint(corners[c]);
                    float nx = (p.x - faceRect.xMin) / faceRect.width;
                    float ny = (p.y - faceRect.yMin) / faceRect.height;
                    if (nx < minNx) minNx = nx;
                    if (nx > maxNx) maxNx = nx;
                    if (ny < minNy) minNy = ny;
                    if (ny > maxNy) maxNy = ny;
                }
                float aw = maxNx - minNx, ah = maxNy - minNy;
                if (aw <= 0f || ah <= 0f)
                    continue;

                // Sprite pixel source, once per sprite (null = unreadable → colour-only verdict).
                Texture2D? region = null;
                float srcW = 0f, srcH = 0f, trW = 0f, trH = 0f;
                Vector2 trimOff = Vector2.zero;
                bool sampled = false;
                if (sprite != null && sprite.texture != null)
                {
                    int sid = sprite.GetInstanceID();
                    if (!regionCache.TryGetValue(sid, out region))
                    {
                        try
                        {
                            Rect tr = sprite.textureRect;
                            region = tr.width >= 2f && tr.height >= 2f
                                ? ReadSpriteRegion(sprite.texture, tr)
                                : null;
                            if (region != null)
                                readbacks.Add(region);
                        }
                        catch (System.Exception)
                        {
                            region = null; // tight-packed: textureRect throws — colour-only below
                        }
                        regionCache[sid] = region;
                    }
                    if (region != null)
                    {
                        Rect tr = sprite.textureRect;
                        srcW = Mathf.Max(1f, sprite.rect.width);
                        srcH = Mathf.Max(1f, sprite.rect.height);
                        trW = tr.width;
                        trH = tr.height;
                        trimOff = sprite.textureRectOffset;
                        sampled = true;
                    }
                }

                Color col = g.color;
                float colLuma = col.r * 0.299f + col.g * 0.587f + col.b * 0.114f;
                int dark = 0, band = 0;
                for (int py = 0; py < probeY; py++)
                {
                    float lv = (py + 0.5f) / probeY;
                    float v = minNy + ah * lv;
                    if (v < 0f || v > 1f)
                        continue;
                    for (int px = 0; px < probeX; px++)
                    {
                        float lu = (px + 0.5f) / probeX;
                        float u = minNx + aw * lu;
                        if (u < 0f || u > 1f)
                            continue;
                        if (Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) > BandFraction)
                            continue; // not in the outer band
                        band++;
                        float a, luma;
                        if (sampled)
                        {
                            float pxf = lu * srcW - trimOff.x;
                            float pyf = lv * srcH - trimOff.y;
                            if (pxf < 0f || pxf > trW || pyf < 0f || pyf > trH)
                                continue; // trimmed-away margin: transparent
                            Color s = region!.GetPixelBilinear(pxf / trW, pyf / trH);
                            a = s.a * col.a;
                            luma = s.r * col.r * 0.299f + s.g * col.g * 0.587f + s.b * col.b * 0.114f;
                        }
                        else
                        {
                            a = col.a;
                            luma = colLuma;
                        }
                        if (a >= 0.5f && luma <= lumaMax)
                            dark++;
                    }
                }
                if (dark == 0)
                {
                    silent++;
                    continue;
                }
                if (sb.Length > 0)
                    sb.Append("; ");
                sb.Append('\'').Append(g.name).Append('\'');
                if (sprite != null)
                    sb.Append("/'").Append(sprite.name).Append('\'');
                sb.Append(' ').Append(dark).Append(" of ").Append(band).Append(" band probe(s)")
                  .Append(sampled ? string.Empty
                      : sprite != null ? " (pixels unreadable — judged by colour)" : " (colour-only quad/text)");
            }
            // ROUND 9: print the derived outline's per-edge band widths alongside the inventory,
            // so ONE log line validates the geometry against what the player actually sees — a
            // visible band that disagrees with these numbers means the DERIVATION is wrong; a
            // band that agrees with them but still shows means a consumer is not applying it.
            CardOutline? outline = CardOutline.ForKind(kind);
            string outlineNote = outline != null
                ? $" Derived outline (from '{outline.SourceName}', threshold {outline.Threshold}): bands " +
                  $"{outline.BandSummary}."
                : " No validated outline for this kind (see the CARD OUTLINE line) — geometric punch " +
                  "inactive, ModBuild-112 fallback in effect.";
            VRLog.Info("Cards", $"CARD FRAME BAND INVENTORY ({kind}): opaque near-black (alpha >= 0.5, " +
                                $"luma <= 48/255) contributions to the outer {BandFraction:P0} band of the " +
                                $"{faceRect.width:F0}x{faceRect.height:F0} px face — " +
                                $"{(sb.Length > 0 ? sb.ToString() : "NONE")}. {checkedCount} drawn graphic(s) " +
                                $"checked, {silent} contribute nothing.{outlineNote} If the black band survives " +
                                "this build, the graphic that paints it is named RIGHT HERE.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CARD FRAME BAND INVENTORY ({kind}) skipped ({ex.GetType().Name}: " +
                                $"{ex.Message}) — the capture itself is unaffected.");
        }
        finally
        {
            foreach (Texture2D t in readbacks)
                if (t != null)
                    Object.Destroy(t);
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
        // Drop any stale wrapper first: this method exists to re-assert the pose after somebody
        // moved the face, and a wrapper left behind would be an empty shell under the host.
        Cards.CardShapeMask.Release(_face);
        _shape = null;
        _face.SetParent(_host, worldPositionStays: false);
        _face.anchorMin = CenterAnchor;
        _face.anchorMax = CenterAnchor;
        _face.pivot = CenterAnchor;
        _face.anchoredPosition3D = Vector3.zero;
        _face.localRotation = Quaternion.identity;
        _face.localScale = new Vector3(_fitScale, _fitScale, _fitScale);
        if (!_face.gameObject.activeSelf)
            _face.gameObject.SetActive(true);
        EnsureShapeMask();
    }

    /// <summary>
    /// THE FACE CLIP. Wrap the adopted face in the card-shaped stencil mask
    /// (<see cref="CardShapeMask"/>) as soon as a footprint exists. Two entry points on purpose:
    /// here, so a warm cache has the mask in place BEFORE the face's first drawn frame (the
    /// footprint is loaded inside the card body's material factory, i.e. before this card's shell
    /// finished building), and from <see cref="Maintain"/>, so the FIRST run — where the shape is
    /// only learned once the art loads — picks it up the moment it lands instead of at the next
    /// re-adoption. Cheap: one field test in the steady state.
    /// </summary>
    private void EnsureShapeMask()
    {
        if (_face == null || _shape != null)
            return;
        _shape = Cards.CardShapeMask.Wrap(_face, CardBodyKind.Ability);
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

        // OURS = directly under the host, or one level down inside our own card-shape stencil
        // wrapper (CardShapeMask). Without the second reading every wrapped card would look to
        // this test like a face a game dialog had stolen, and Maintain would fight its own wrapper
        // every frame.
        if (!IsOurParent(_face.parent))
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
        // FIRST RUN ONLY in practice: with a warm cache the wrapper was built during Adopt. On a
        // cold cache the shape is learned mid-session, and this is what puts the clip on every face
        // already on screen in the frame the footprint lands. In the steady state it is one field
        // test; once wrapped it only re-fits the wrapper to the face's rendered rect.
        EnsureShapeMask();
        if (_shape != null)
            _shape.RefreshRect();
    }

    /// <summary>
    /// True when <paramref name="parent"/> lives inside a <c>DialogPopup</c> — i.e. the
    /// game handed our face to a burn/lose/discard confirm popup (the only flow that
    /// re-parents a live <c>fullAbilityCard.gameObject</c> into a DialogPopup, verified
    /// CardsHandUI.cs:826/850/909/2069). Full-card PREVIEW re-parents into
    /// <c>FullCardHandViewer.CardContainer</c> instead — and is short-circuited here
    /// anyway by <c>LockFullCard</c> — so this never mis-fires on a preview.
    /// </summary>
    /// <summary>Is <paramref name="parent"/> the host, or our own stencil wrapper sitting directly
    /// under it? See the call site in <see cref="Maintain"/>.</summary>
    private bool IsOurParent(Transform? parent)
    {
        if (parent == null || _host == null)
            return false;
        if (ReferenceEquals(parent, _host))
            return true;
        return CardShapeMask.IsWrapper(parent) && ReferenceEquals(parent.parent, _host);
    }

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
        // contract; a dialog that shows this face must see the game's own colours)...
        FaceBlackout.Restore(_face);
        // ...and drop the stencil wrapper, which also puts every neutralised Canvas' overrideSorting
        // back. A dialog that shows this face must get the game's own hierarchy, not ours.
        CardShapeMask.Release(_face);
        _shape = null;
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
        _shape = null;
        _reclaimedFromDialog = false;
        _artWatch.Clear();

        if (face == null)
            return;

        // FULL-RESTORE CONTRACT, and it must run BEFORE the transform is put back: the wrapper lifts
        // the face out of itself and restores every Canvas.overrideSorting it neutralised. A widget
        // must never return to the game's pool inside a mod GameObject or with a game canvas still
        // holding a value we wrote.
        CardShapeMask.Release(face);

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
