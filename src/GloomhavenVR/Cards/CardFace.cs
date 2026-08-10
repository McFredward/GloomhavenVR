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
            // ART JUST ARRIVED — the one moment a silhouette capture can newly succeed. Offering
            // it here instead of waiting for the 1 s backstop keeps the window in which cards are
            // still drawn as rectangles down to a frame or two: the clip is a one-shot that
            // re-shapes every card at once, so the later it lands the more it reads as a POP.
            Offer(_owner.fullAbilityCard);
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
    private sealed class SilhouetteState
    {
        internal bool Applied;
        internal int Attempts;
        internal int Hash;
        internal float NextAttemptTime;
        internal string? LastReason;
        internal bool GaveUpLogged;
    }

    private static readonly SilhouetteState[] s_silhouette =
    {
        new(), new(), new(), // indexed by CardBodyKind (Neutral slot unused)
    };

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
    /// Offer a live card face to the silhouette capture. Cheap and safe to call every frame:
    /// returns immediately once that kind is done, and skips the readback entirely while the
    /// face's qualifying sprite set is unchanged. Classifies by the game component itself, so a
    /// root that is neither card kind (e.g. the peer's card canvas, which reaches
    /// <c>CardFaceMipBake.Rescan</c> from <c>Net/RemoteCardArt</c>) is ignored.
    /// </summary>
    internal static void Offer(Component? faceRoot)
    {
        if (faceRoot == null)
            return;
        try
        {
            CardBodyKind kind;
            RectTransform? root;
            // Unity-null aware (a destroyed component is != null to C# but == null to Unity).
            FullAbilityCard? ability = faceRoot.GetComponent<FullAbilityCard>();
            if (ability != null)
            {
                kind = CardBodyKind.Ability;
                root = ability.RectTransform != null
                    ? ability.RectTransform
                    : ability.transform as RectTransform;
            }
            else if (faceRoot.GetComponent<ItemCardUI>() != null)
            {
                kind = CardBodyKind.Item;
                root = faceRoot.transform as RectTransform;
            }
            else
            {
                return;
            }
            if (root == null)
                return;

            SilhouetteState state = s_silhouette[(int)kind];
            if (state.Applied || CardMesh.SilhouetteApplied(kind))
                return;
            if (state.Attempts >= MaxSilhouetteAttempts)
            {
                if (!state.GaveUpLogged)
                {
                    state.GaveUpLogged = true;
                    VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): giving up after {state.Attempts} " +
                                        $"attempt(s), last reason '{state.LastReason ?? "n/a"}' — that shape " +
                                        "keeps the opaque rounded-rect body (unchanged look, no black border " +
                                        "beyond what the art itself leaves transparent).");
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
            hash = hash * 31 + img.GetInstanceID();
            hash = hash * 31 + sprite.GetInstanceID();
        }

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
        if (hash == state.Hash)
            return; // same sprites as the last attempt — nothing new to look at, cost nothing
        if (Time.unscaledTime < state.NextAttemptTime)
            return; // spread repeat readbacks across frames (see SilhouetteAttemptInterval)

        state.Hash = hash;
        state.NextAttemptTime = Time.unscaledTime + SilhouetteAttemptInterval;
        state.Attempts++;

        // --- pass 2: stamp the union of their alpha into a card-space footprint --------------
        int fw = FootprintWidth;
        int fh = Mathf.Clamp(Mathf.RoundToInt(fw * faceRect.height / faceRect.width), 64, 512);
        var alpha = new byte[fw * fh];
        var readbacks = new List<Texture2D>(picks.Count);
        int stamped = 0, backdrops = 0;
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
                        float sa = region.GetPixelBilinear(px / tr.width, ty).a * colorA;
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
                            $"{faceRect.width:F0}x{faceRect.height:F0} px face — handing it to CardMesh.");
        bool applied = CardMesh.SetSilhouette(kind, alpha, fw, fh);
        if (applied)
        {
            state.Applied = true;
            VRLog.Info("Cards", $"CARD SILHOUETTE ({kind}): APPLIED — every body of this shape is now " +
                                "alpha-clipped to the card art's own outline, so the dark front only shows " +
                                "where the card itself is solid. This is the 2026-08-11 report " +
                                "('keinen schwarzen Rand … die meshes genau die Ränder der Karten').");
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
