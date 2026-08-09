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

        // Test #25: the card art has an artistic, non-rectangular outline. Once (session
        // wide — the outer silhouette is shared by every ability card), capture the live
        // art's opacity footprint and hand it to CardMesh, which re-shapes ALL card
        // slabs to that outline via alpha-clip. Fully guarded/fallback-safe; never
        // blocks adoption.
        if (!s_silhouetteTried && !CardMesh.SilhouetteApplied)
            TryCaptureSilhouette(owner);

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
            _nextMipRescan = Time.unscaledTime + MipRescanInterval; // it just did the backstop's job
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

    /// <summary>Session-wide guard for <see cref="TryCaptureSilhouette"/>: latched only once
    /// the silhouette is successfully applied (or the retry budget is spent). Card art loads
    /// ASYNC (ImageAddressableLoader.LoadAsync), so the first adopted card usually has no
    /// sprites yet — a plain one-shot burned on that first attempt would permanently block
    /// every later card whose art HAS loaded. We instead retry across the next few adoptions
    /// until one yields a valid footprint.</summary>
    private static bool s_silhouetteTried;

    /// <summary>Bounded retry budget so a genuinely rectangular / never-capturable card set
    /// stops re-blitting after a handful of adoptions.</summary>
    private static int s_silhouetteAttempts;
    private const int MaxSilhouetteAttempts = 16;

    /// <summary>Footprint resolution (card-space). ~224 px wide keeps the ornate curve
    /// crisp at fan distance while the one-shot CPU cost stays trivial.</summary>
    private const int FootprintWidth = 224;

    /// <summary>
    /// Capture the live card art's opacity footprint (card-space alpha) and drive
    /// <see cref="CardMesh.SetSilhouette"/>. We union the alpha of the card's larger
    /// <c>Image</c> sprites (the class-skin backgrounds / frame that define the outer
    /// outline; tiny icons are skipped and never extend the silhouette anyway), each
    /// sampled by GPU blit → readback so it works even for non-CPU-readable atlas
    /// textures. Robust: any failure just leaves the opaque rounded-rect slab in place.
    /// </summary>
    private static void TryCaptureSilhouette(AbilityCardUI owner)
    {
        // Retry across adoptions until a footprint applies; latch off only when the budget
        // is exhausted (see s_silhouetteTried doc) so async-loaded art still gets captured.
        if (++s_silhouetteAttempts >= MaxSilhouetteAttempts)
            s_silhouetteTried = true;
        var readbacks = new List<Texture2D>();
        try
        {
            FullAbilityCard? faceCard = owner.fullAbilityCard;
            if (faceCard == null)
                return;
            RectTransform faceRoot = faceCard.RectTransform;
            if (faceRoot == null)
                return;
            Rect faceRect = faceRoot.rect;
            if (faceRect.width < 1f || faceRect.height < 1f)
                return;

            int fw = FootprintWidth;
            int fh = Mathf.Clamp(
                Mathf.RoundToInt(fw * faceRect.height / faceRect.width), 64, 512);
            var alpha = new byte[fw * fh];

            var cache = new Dictionary<int, Texture2D>();
            var corners = new Vector3[4];
            bool stamped = false;

            Image[] images = faceCard.GetComponentsInChildren<Image>(includeInactive: false);
            foreach (Image img in images)
            {
                if (img == null || !img.isActiveAndEnabled)
                    continue;
                Sprite sprite = img.sprite;
                if (sprite == null || sprite.texture == null)
                    continue;
                float colorA = img.color.a;
                if (colorA < 0.2f)
                    continue;

                // Image rect → normalized [0,1] within the face root (scale-independent:
                // world corners transformed back into the face root's own local rect).
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
                if (aw <= 0.001f || ah <= 0.001f)
                    continue;
                if (aw * ah < 0.03f)
                    continue; // tiny icon — never part of the outer outline

                int texId = sprite.texture.GetInstanceID();
                if (!cache.TryGetValue(texId, out Texture2D readback))
                {
                    readback = ReadTexture(sprite.texture);
                    cache[texId] = readback;
                    readbacks.Add(readback);
                }
                Rect tr = sprite.textureRect;
                float tW = readback.width, tH = readback.height;

                int fx0 = Mathf.Clamp(Mathf.FloorToInt(minNx * fw), 0, fw - 1);
                int fx1 = Mathf.Clamp(Mathf.CeilToInt(maxNx * fw), 0, fw - 1);
                int fy0 = Mathf.Clamp(Mathf.FloorToInt(minNy * fh), 0, fh - 1);
                int fy1 = Mathf.Clamp(Mathf.CeilToInt(maxNy * fh), 0, fh - 1);
                for (int fy = fy0; fy <= fy1; fy++)
                {
                    float v = (fy + 0.5f) / fh;
                    float lv = (v - minNy) / ah;
                    if (lv < 0f || lv > 1f)
                        continue;
                    float uvy = (tr.y + lv * tr.height) / tH;
                    int rowBase = fy * fw;
                    for (int fx = fx0; fx <= fx1; fx++)
                    {
                        float u = (fx + 0.5f) / fw;
                        float lu = (u - minNx) / aw;
                        if (lu < 0f || lu > 1f)
                            continue;
                        float uvx = (tr.x + lu * tr.width) / tW;
                        float sa = readback.GetPixelBilinear(uvx, uvy).a * colorA;
                        var b = (byte)Mathf.Clamp(Mathf.RoundToInt(sa * 255f), 0, 255);
                        int idx = rowBase + fx;
                        if (b > alpha[idx])
                        {
                            alpha[idx] = b;
                            stamped = true;
                        }
                    }
                }
            }

            if (!stamped)
                return;

            bool applied = CardMesh.SetSilhouette(alpha, fw, fh);
            if (applied)
                s_silhouetteTried = true; // success — stop retrying regardless of budget
            VRLog.Info("Cards", applied
                ? $"CardFace captured the card-art silhouette ({fw}x{fh}) — 3D card body " +
                  "now clipped to the artistic outline (test #25)."
                : "CardFace captured a card-art footprint but it failed the silhouette " +
                  "sanity guard (empty/solid/hollow) — kept the rounded-rect slab.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Cards", $"CardFace silhouette capture skipped ({ex.Message}) — " +
                                "kept the rounded-rect card slab.");
        }
        finally
        {
            foreach (Texture2D t in readbacks)
                if (t != null)
                    Object.Destroy(t);
        }
    }

    /// <summary>
    /// CPU-read a texture's pixels via a GPU blit — works even when the source atlas
    /// texture is not marked CPU-readable (the usual case for bundled sprites).
    /// </summary>
    private static Texture2D ReadTexture(Texture src)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            // HONESTY NOTE (aliasing round 3): this readback texture is CPU-SIDE ONLY — it
            // feeds GetPixelBilinear for the silhouette footprint and is destroyed in the
            // caller's finally block; it is NEVER rendered. The mip/trilinear/aniso settings
            // here (added by 9ca829b against the card shimmer) therefore CANNOT affect what
            // the player sees: the visible card face is the ADOPTED LIVE game uGUI, whose
            // Images sample the game's own atlas textures directly (see LogFaceTextureDiag —
            // if those atlases ship without mips, no setting on our side can add them and
            // the honest lever is [RenderQuality] EyeResolutionScale supersampling).
            // Kept mipped + max aniso anyway: harmless one-shot cost, and future-proof
            // should a readback ever be rendered.
            var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: true)
            {
                filterMode = FilterMode.Trilinear,
                anisoLevel = 16,
            };
            tex.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            tex.Apply(updateMipmaps: true);
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
