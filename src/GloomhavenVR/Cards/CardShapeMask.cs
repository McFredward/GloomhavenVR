using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE FACE CLIP (round 5). A uGUI stencil <see cref="Mask"/>, shaped by the captured card
/// footprint, wrapped around a live card face so that EVERYTHING the face draws is bounded by the
/// card's own outline — whatever component draws it, and whether or not this mod can classify it.
///
/// WHY IT EXISTS. The 2026-08-11 report ("die Karten sind Rechtecke und der Rand der Karten ist
/// daher schwarz") survived four attempts that all worked on the card BODY. Round 5 settled the
/// geometry from source: the backing slab is fitted to the RENDERED art rect and the footprint's
/// 0..1 is that same rect (see the refutation note in <c>CardMesh.SetSilhouette</c>), so once the
/// clip is applied the body can paint nothing the art does not also cover. And the captured outline
/// is demonstrably NOT a rectangle — bounding box 95.5 % of the face, filled 0.928 (ModBuild 109).
/// If the body defined the card's visible edge, the card would have stopped looking rectangular the
/// moment the clip landed. It did not. So the rectangle is painted by the FACE, and the only fix
/// that does not first require naming the component is to bound the face itself.
///
/// WHAT IT DOES NOT DO. It never mutes, recolours, disables or reorders anything the game owns
/// except one property (see the canvas note below), and it adds no material of its own to any game
/// object: uGUI's own <c>StencilMaterial</c> cache produces the variants, shared across every card
/// at the same stencil depth.
///
/// THE OBJECTION THIS DESIGN ANSWERS. A Mask can silently do nothing, which is exactly this
/// defect's signature failure (attempt 1 shipped a clip that never executed). Two mechanisms:
/// <list type="number">
/// <item>CANVAS NEUTRALISATION. <c>MaskUtilities.FindRootSortOverrideCanvas</c> stops the stencil
///   walk at the first ancestor Canvas with <c>overrideSorting</c>, and <c>FullAbilityCard</c>
///   carries its own Canvas whose sorting the game toggles
///   (<c>CardsHandUI.ToggleFullCardCanvasSorting</c>). A mask ABOVE such a canvas is invisible to
///   every graphic below it, and — worse — a nested canvas that sorts itself can draw out of order
///   against the stencil write. Every Canvas INSIDE the wrapped face therefore has
///   <c>overrideSorting</c> forced false for as long as we own the face, recorded per canvas and
///   restored exactly on release. It is re-asserted every frame because the game's own toggle can
///   turn it back on. Nothing else about those canvases is touched — not the sorting order, not the
///   layer, not the enabled flag. In our world-space host the card is the only content, so the
///   property has nothing to arbitrate.</item>
/// <item>VERIFICATION, AND FAILING CLOSED. After installing, this resolves the SAME root canvas
///   uGUI will resolve for a representative graphic under the face and asks
///   <c>MaskUtilities.GetStencilDepth</c> whether our mask is on the counted side of it. Depth 0
///   means the mask would render a plain rectangle while claiming success — so the wrapper is
///   removed again, the face is put back exactly where it was, and one log line says why. A face
///   that cannot be evaluated yet (a card parked under the inactive <c>VRCardFactory.PoolRoot</c>
///   has no active canvas in its parent chain at all) is retried, not condemned.</item>
/// </list>
///
/// FAIL-CLOSED IS ALSO STRUCTURAL, not only procedural. If the mask graphic ever stops writing
/// (component disabled, GameObject deactivated, sprite missing) the children's stencil depth
/// collapses to 0 in the same walk and they render unmasked — i.e. exactly today's rounded
/// rectangle. There is no state in which the card can be clipped away by this class failing.
///
/// INPUT IS UNAFFECTED — and this was checked against the sources, not assumed.
/// <c>Mask.IsRaycastLocationValid</c> (Mask.cs:136-142) filters by the mask's RECT only, never by
/// its alpha, and the wrapper's rect is exactly the face's rendered rect, so every point that could
/// hit the card before can still hit it. The wrapper's own Image is <c>raycastTarget = false</c> and
/// so can never become a hit target itself. The game's on-card highlight, the default-action
/// buttons and the half-selection zones all live inside that rect and are reached through the same
/// <c>CardFaceRaycaster</c> path as before.
///
/// NO VISIBLE TRANSITION. From the second launch onward the footprint is already applied when a
/// face is adopted (<c>CardMesh.EnsureSilhouetteCacheLoaded</c> runs inside the card body's own
/// material factory, before any renderer has a material), so the wrapper exists before the face's
/// first drawn frame. On a cold cache there is no wrapper until the capture lands — deliberately
/// today's look, never a guessed shape.
///
/// <para>═══════════════════════════════════════════════════════════════════════════════════════
/// ROUND 6 — EVERYTHING ABOVE IS THE ROUND-5 ARGUMENT AND IT IS STILL ACCURATE ABOUT STENCILS AND
/// ABOUT INPUT. IT IS WRONG ABOUT MATERIALS, THE MECHANISM IS OFF BY DEFAULT, AND
/// <see cref="Enabled"/> IS WHERE THAT IS DECIDED. READ THAT FIELD BEFORE TOUCHING ANYTHING HERE.
/// ═══════════════════════════════════════════════════════════════════════════════════════</para>
/// </summary>
internal sealed class CardShapeMask : MonoBehaviour
{
    /// <summary>
    /// THE FACE CLIP IS OFF. Flip this to <c>true</c> to put ModBuild 110's stencil clip back, and
    /// read the whole block first — it ships off because it is BROKEN, not because it is unfinished.
    ///
    /// <para>THE REGRESSION IT CAUSED. User, after ModBuild 110 (verbatim): "Immer noch exakt das
    /// selbe Fehlerbild - sogar schlimmer geworden: Wenn man zu einem neuen Character wechselt sind
    /// alle Karten kurz ganz schwarz bis sie nachgeladen haben (nur einen ganz kleinen Moment, aber
    /// merkbar)." Before 110 that same window looked right. The border was unchanged, so 110 bought
    /// nothing and cost a visible black flash in a shipped build.</para>
    ///
    /// <para>THE MECHANISM, READ FROM BOTH SOURCES RATHER THAN INFERRED FROM THE SYMPTOM. A uGUI
    /// mask does not clip a child by re-rendering it — it makes the child render through a
    /// DIFFERENT MATERIAL, and that material is a one-time COPY:
    /// <list type="number">
    /// <item><c>MaskableGraphic.GetModifiedMaterial</c> (MaskableGraphic.cs:122-128) hands the
    ///   child's own material to <c>StencilMaterial.Add</c> as soon as its stencil depth is &gt; 0;</item>
    /// <item><c>StencilMaterial.Add</c> (StencilMaterial.cs) answers
    ///   <c>newEnt.customMat = new Material(baseMat)</c> — A SNAPSHOT — and caches it keyed on the
    ///   BASE MATERIAL INSTANCE, returning that same frozen copy to every later caller;</item>
    /// <item>the game drives its card faces by writing shader properties ON THAT BASE MATERIAL, per
    ///   card and per frame. <c>CardEffects.Awake</c> gives every card face image its OWN material
    ///   (<c>image2.material = new Material(image2.material)</c>, CardEffects.cs:336/340) and then
    ///   writes <c>_PosAndBounds</c> into it (:347); <c>CardEffects.RestoreCard</c> — the call that
    ///   makes a card look NORMAL again — writes <c>_GreyOut</c>, <c>_Flow</c>, <c>_Dissolve</c> and
    ///   <c>_Burn</c> back to 0 on it (:478-484); the burn and ghost-out timelines drive the same
    ///   properties every frame (:544-561, :658-675).</item>
    /// </list>
    /// So while the mask is installed the screen shows a material the game can no longer reach. Not
    /// "sometimes" and not "during the load" — ALWAYS, until something drops the StencilMaterial
    /// refcount to zero (an enable/disable cycle on the graphic) and a fresh snapshot is taken.</para>
    ///
    /// <para>WHY THAT IS BLACK, AND WHY IT ENDS WHEN THE ART ARRIVES. Card widgets are POOLED, so a
    /// character switch re-initialises widgets whose materials still carry the previous card's FX
    /// state (a full <c>_Dissolve</c>/<c>_Burn</c>, or <c>_GreyOut</c> from the discard/lost path),
    /// and — before <c>CardEffects.Awake</c> has run for the new binding — a <c>_PosAndBounds</c>
    /// that does not describe this card. The snapshot freezes exactly that. The face therefore
    /// paints nothing usable and what the player sees is the mod's own card BODY, whose front is
    /// <c>CardMesh.EdgeColor</c> — near-black rgb(0.10, 0.09, 0.08) when this was written, warm
    /// umber since round 14 — an art-less card slab. The window closes when the
    /// art finishes loading, because the elements the loader activates go through
    /// <c>MaskableGraphic.OnEnable/OnDisable</c>, which calls <c>StencilMaterial.Remove</c>, drops
    /// the entry and re-snapshots from a material the game has meanwhile restored. That is
    /// "kurz ganz schwarz bis sie nachgeladen haben", end to end.</para>
    ///
    /// <para>THIS PROJECT HAD ALREADY PAID FOR THIS EXACT CLASS ONCE, and round 5 waved the
    /// precedent away with an argument that does not hold: <c>VRCard.SetRenderOnTop</c> is a
    /// permanent no-op-forward because per-instance material copies on the face's TMP text
    /// "swallowed all card TEXT". Round 5 answered that a mask is different because
    /// <c>StencilMaterial</c>'s variants are SHARED. They are shared per BASE MATERIAL — and
    /// <c>CardEffects</c> gives every card image a base material of its own, so the variants are
    /// per-image-per-card after all. The sharing was never the property that mattered anyway: the
    /// COPY is, and a copy is what both mechanisms make. <c>CardFace</c>'s own doc block already
    /// recorded the neighbouring symptom for the same reason — a card whose canvas-sorting prep is
    /// skipped "resolves to DEEP BLACK".</para>
    ///
    /// <para>WHAT IS NOT WRONG WITH IT, so round 6 does not re-litigate settled ground: the stencil
    /// really does resolve (the hardware log says INSTALLED and VERIFIED, probe 'Header', root sort
    /// canvas 'FaceCanvas', depth 1); input really is untouched
    /// (<c>Mask.IsRaycastLocationValid</c> filters by RECT); the canvas neutralisation really is
    /// exact and restored. NONE of that is the problem. The problem is that clipping a uGUI graphic
    /// costs you the graphic's live material, and these graphics' materials are the game's animation
    /// channel.</para>
    ///
    /// <para>WHAT WOULD ACTUALLY HAVE TO CHANGE for a face clip to ship. Not a threshold and not a
    /// placement — the clip has to stop travelling through the material. Copying the base material's
    /// properties into the stencil variant every frame was considered and rejected: ~10 driven
    /// images per card × the whole hand × 90 Hz of <c>CopyPropertiesFromMaterial</c>, in an 11.1 ms
    /// budget, to fight the engine. <c>RectMask2D</c> clips without a material copy but is a
    /// RECTANGLE, which is the one shape that cannot help here. That leaves clipping something that
    /// is NOT the game's own graphic — which is what the card BODY clip already is, and it stays on.
    /// </para>
    ///
    /// <para>WHAT STAYS ON with this false: the body silhouette (<c>CardMesh.SetSilhouette</c>), the
    /// footprint capture and its cache, the dark-border peel, and the shape-less-quad blackout. Only
    /// the wrapper is refused, at <see cref="Wrap"/>, which is the single door every caller —
    /// <c>CardFace</c>, <c>ItemsPile</c> and <c>Net/RemoteCardArt</c> — comes through. It answers
    /// null exactly as it does when no footprint has been captured yet, a case every call site
    /// already handles, so no other file needs to change and <c>Release</c> stays a safe no-op.</para>
    ///
    /// <para><c>static readonly</c> rather than <c>const</c> on purpose: a false <c>const</c> makes
    /// the compiler declare the whole mechanism below it unreachable (CS0162), and the gate is
    /// "exactly the six pre-existing nullability warnings". Suppressing a warning to keep a keyword
    /// is the wrong trade — this way the code still compiles as live code and flipping the flag is a
    /// one-word edit with nothing else to undo.</para>
    /// </summary>
    internal static readonly bool Enabled = false;

    /// <summary>One line per session saying the clip is off and why, so a hardware log can never be
    /// read as "the clip was live and the border survived it".</summary>
    private static bool s_loggedDisabled;

    /// <summary>Name of the wrapper GameObject — one grep away in a hierarchy dump.</summary>
    private const string WrapperName = "GVR_CardShape";

    /// <summary>How many frames an inconclusive verification is retried before it is left alone.
    /// A card built under the inactive pool root can have no active canvas in its parent chain for
    /// as long as it stays there; ~4 s at 90 Hz is far more than any adoption takes.</summary>
    private const int VerifyRetryFrames = 360;

    /// <summary>One shape sprite per <see cref="CardBodyKind"/>, built from the APPLIED footprint
    /// the first time a face of that kind is wrapped.</summary>
    private static readonly Sprite?[] s_shape = new Sprite?[3];

    /// <summary>Footprint identity the cached sprite was built from, so a mask that lands later in
    /// the session (cold cache) rebuilds the sprite instead of reusing a stale one.</summary>
    private static readonly byte[]?[] s_shapeSource = new byte[]?[3];

    private static readonly bool[] s_loggedInstalled = new bool[3];
    private static readonly bool[] s_loggedRefused = new bool[3];
    private static readonly bool[] s_loggedInconclusive = new bool[3];

    /// <summary>A kind whose stencil provably does not resolve on this build. Latched so the owner's
    /// per-frame "wrap me if you can" call cannot turn into a build/verify/destroy churn at 90 Hz,
    /// and so the refusal is stated once rather than once per card.</summary>
    private static readonly bool[] s_refused = new bool[3];

    private CardBodyKind _kind;
    private RectTransform? _face;
    private Image? _image;
    private Mask? _mask;

    /// <summary>Canvases inside the face whose <c>overrideSorting</c> we forced false, with the
    /// value to put back. Restored exactly, on every exit path.</summary>
    private readonly List<(Canvas Canvas, bool Original)> _neutralised = new(2);

    /// <summary>Frames between re-scans of the face's Canvas set (the game grows the hierarchy after
    /// adoption). ~0.7 s at 90 Hz — one <c>GetComponentsInChildren</c> per card per re-scan.</summary>
    private const int CanvasRescanFrames = 60;

    private int _canvasRescanIn = CanvasRescanFrames;

    private int _verifyFramesLeft = VerifyRetryFrames;

    /// <summary>Set the moment this wrapper gives itself up. <c>Object.Destroy</c> is deferred to the
    /// end of the frame, so a caller that got this instance back would otherwise keep talking to a
    /// live-but-doomed component for the rest of the frame.</summary>
    private bool _released;

    /// <summary>True once the stencil was confirmed to resolve for a graphic under the face.</summary>
    internal bool Verified { get; private set; }

    /// <summary>Is this transform one of our wrappers? Used by the owner's per-frame parent
    /// assertion so the extra level does not read as "a game dialog stole the face".</summary>
    internal static bool IsWrapper(Transform? t) =>
        t != null && t.GetComponent<CardShapeMask>() != null;

    // ------------------------------------------------------------------- install --

    /// <summary>
    /// Wrap <paramref name="face"/> in a card-shaped stencil mask, inserting the wrapper into the
    /// face's CURRENT parent at its current sibling index; refreshes an existing wrapper instead of
    /// building a second one. Returns the wrapper, or null when there is nothing to shape with (no
    /// footprint yet) or the wrap was refused — in both cases the face is left exactly as found.
    ///
    /// <para>Peer mirrors call this too: it takes only the face rect, so a hosted clone
    /// (<c>Net/RemoteCardArt</c>) is one line away from the same outline the owner's card has.</para>
    /// </summary>
    internal static CardShapeMask? Wrap(RectTransform? face, CardBodyKind kind)
    {
        // ROUND 6: the single door. Off by default — see the Enabled block for the proof that a uGUI
        // mask makes the card face render a FROZEN COPY of a material the game writes to every
        // frame, and for the black flash that bought. Answering null here is the same answer this
        // method already gives when no footprint has been captured, so every call site's existing
        // handling covers it and nothing else in the mod has to know.
        if (!Enabled)
        {
            if (!s_loggedDisabled)
            {
                s_loggedDisabled = true;
                VRLog.Info("Cards", "CARD FACE CLIP: DISABLED (round 6). ModBuild 110's stencil clip " +
                                    "is not installed on any card face — local, item or peer. It is off " +
                                    "because it made every card paint a FROZEN COPY of its material: uGUI " +
                                    "masks a child by swapping in StencilMaterial.Add's 'new Material(base)' " +
                                    "snapshot, while CardEffects drives the card's shader by writing " +
                                    "_PosAndBounds / _GreyOut / _Flow / _Dissolve / _Burn on the BASE " +
                                    "material (CardEffects.cs:336-347, :478-484). On a character switch a " +
                                    "pooled widget is snapshotted mid-restore, the face paints nothing " +
                                    "usable, and the card body behind it (rgb 0.10/0.09/0.08) is what the " +
                                    "player sees — the 'alle Karten kurz ganz schwarz' of the ModBuild-110 " +
                                    "report. The card BODY silhouette, the footprint cache, the dark-border " +
                                    "peel and the face blackout are all UNAFFECTED and still running. Any " +
                                    "border report on this run is therefore about the pre-110 look.");
            }
            return null;
        }
        if (face == null || kind == CardBodyKind.Neutral || s_refused[(int)kind])
            return null;
        // Footprint first: this is an array read, and it is the branch taken every frame on a cold
        // cache (no shape learned yet), so nothing more expensive may sit in front of it.
        Sprite? shape = ShapeSprite(kind);
        if (shape == null)
            return null; // no footprint for this shape yet — today's look, deliberately

        Transform? parent = face.parent;
        if (parent == null)
            return null;

        // Already wrapped by us: refresh size (the host canvas is resized after adoption) and go.
        CardShapeMask? existing = parent.GetComponent<CardShapeMask>();
        if (existing != null)
        {
            if (existing._kind != kind)
                return existing; // a kind change means a different card entirely — leave it alone
            existing.RefreshRect();
            return existing;
        }

        Vector2 faceSize = face.rect.size;
        float scale = face.localScale.x;
        if (faceSize.x < 1f || faceSize.y < 1f || scale <= 0.0001f)
            return null; // not laid out yet; the caller retries next frame

        var go = new GameObject(WrapperName);
        // THE ONE WAY THIS COULD HAVE CLIPPED A CARD AWAY. The stencil write and the stencil test
        // must happen in the SAME camera pass: if the wrapper sat on a layer the head camera culls
        // while the face's own layer is rendered, the children would carry a "stencil == 1" test
        // against a buffer nothing ever wrote, and the card would render as nothing at all. The
        // wrapper therefore takes the FACE's layer, never the mod shell's — game objects keep their
        // game layer (CAMERA-POLICY §2), so this is the layer that is guaranteed to be drawn with
        // the content it masks.
        go.layer = face.gameObject.layer;
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.SetSiblingIndex(face.GetSiblingIndex());
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        rect.anchoredPosition3D = Vector3.zero;
        rect.sizeDelta = faceSize * scale; // exactly the face's RENDERED rect

        var img = go.AddComponent<Image>();
        img.sprite = shape;
        img.type = Image.Type.Simple;
        img.preserveAspect = false;
        img.color = Color.white;
        img.raycastTarget = false; // can never swallow a pointer

        var mask = go.AddComponent<Mask>();
        mask.showMaskGraphic = false; // stencil only, ColorMask 0 — nothing of ours is ever drawn

        var self = go.AddComponent<CardShapeMask>();
        self._kind = kind;
        self._face = face;
        self._image = img;
        self._mask = mask;

        // Move the face in, keeping its local pose (scale included).
        face.SetParent(rect, worldPositionStays: false);
        face.anchorMin = new Vector2(0.5f, 0.5f);
        face.anchorMax = new Vector2(0.5f, 0.5f);
        face.pivot = new Vector2(0.5f, 0.5f);
        face.anchoredPosition3D = Vector3.zero;
        face.localRotation = Quaternion.identity;

        self.NeutraliseCanvases();
        MaskUtilities.NotifyStencilStateChanged(mask);
        self.TryVerify();
        return self._released ? null : self;
    }

    /// <summary>
    /// Undo everything: restore every neutralised canvas, lift the face back out to where the
    /// wrapper stood, and destroy the wrapper. Idempotent and safe on a face that was never
    /// wrapped or whose wrapper is already gone (teardown races).
    /// </summary>
    internal static void Release(RectTransform? face)
    {
        if (face == null)
            return;
        Transform? parent = face.parent;
        CardShapeMask? mask = parent != null ? parent.GetComponent<CardShapeMask>() : null;
        if (mask == null)
            return;
        mask.ReleaseInternal();
    }

    private void ReleaseInternal()
    {
        if (_released)
            return;
        _released = true;
        RestoreCanvases();
        var rect = transform as RectTransform;
        if (_face != null && rect != null && ReferenceEquals(_face.parent, rect))
        {
            // Back to the wrapper's own slot, local pose preserved — the owner's Restore() then
            // writes the game's captured values over it.
            _face.SetParent(rect.parent, worldPositionStays: false);
            _face.SetSiblingIndex(rect.GetSiblingIndex());
        }
        _face = null;
        if (this != null && gameObject != null)
            Object.Destroy(gameObject);
    }

    // -------------------------------------------------------------------- upkeep --

    /// <summary>Re-fit the wrapper to the face's current rendered rect (the host canvas is resized
    /// right after adoption, so this is not a no-op).</summary>
    internal void RefreshRect()
    {
        if (_face == null)
            return;
        var rect = transform as RectTransform;
        if (rect == null)
            return;
        Vector2 want = _face.rect.size * _face.localScale.x;
        if (want.x < 1f || want.y < 1f)
            return;
        if (rect.sizeDelta != want)
            rect.sizeDelta = want;
    }

    private void LateUpdate()
    {
        if (_face == null)
        {
            // The face left without going through Release (a game dialog took it, or a teardown
            // race). Never leave a wrapper standing with the game's canvases still overridden.
            RestoreCanvases();
            return;
        }
        // The game's own ToggleFullCardCanvasSorting can put overrideSorting back at any time, and
        // one frame of it is one frame of the black rectangle. Two bool compares per card.
        for (int i = 0; i < _neutralised.Count; i++)
        {
            Canvas c = _neutralised[i].Canvas;
            if (c != null && c.overrideSorting)
                c.overrideSorting = false;
        }
        // ...and pick up canvases the game added to the face after we wrapped it.
        if (--_canvasRescanIn <= 0)
        {
            _canvasRescanIn = CanvasRescanFrames;
            NeutraliseCanvases();
        }
        if (!Verified && !_released && _verifyFramesLeft > 0)
        {
            _verifyFramesLeft--;
            TryVerify();
            // NEVER LEAVE IT SILENT. "Inconclusive forever" is a third outcome next to installed and
            // refused, and an unlogged third outcome is how four rounds were spent. The mask stays —
            // an unresolvable stencil fails to the UNMASKED look, which is today's — but the log says
            // that is what happened rather than implying the clip is live.
            if (_verifyFramesLeft == 0 && !Verified && !s_loggedInconclusive[(int)_kind])
            {
                s_loggedInconclusive[(int)_kind] = true;
                VRLog.Warn("Cards", $"CARD FACE CLIP ({_kind}): verification INCONCLUSIVE for " +
                                    $"{VerifyRetryFrames} frames — no drawn graphic under the face with an " +
                                    "active Canvas in its parent chain, so uGUI's own resolver could not be " +
                                    "asked whether the stencil lands. The wrapper is left in place: an " +
                                    "unresolved stencil renders the face UNMASKED, i.e. exactly today's " +
                                    "look, never a clipped-away card. Treat any border report on this run " +
                                    "as 'the clip was never proven live', not as 'the clip failed'.");
            }
        }
    }

    private void OnDestroy() => RestoreCanvases();

    // -------------------------------------------------------- canvases + verify --

    /// <summary>
    /// Record and force <c>overrideSorting = false</c> on EVERY Canvas inside the face — not only
    /// the ones that happen to be true right now.
    ///
    /// <para>That distinction is the whole point: the game's own
    /// <c>CardsHandUI.ToggleFullCardCanvasSorting</c> TOGGLES the flag, so a canvas that reads false
    /// at install time can read true two frames later, and from that moment the stencil walk would
    /// stop there and the black rectangle would come back — quietly, because the failure direction
    /// is "unmasked", i.e. exactly today's look. Recording the original per canvas means the restore
    /// is still exact.</para>
    ///
    /// <para>Re-run on a slow cadence as well, because the game grows the face hierarchy after
    /// adoption (enhancement slots, XP orbs) and a canvas that did not exist at install must not be
    /// a hole in the same guarantee.</para>
    /// </summary>
    private void NeutraliseCanvases()
    {
        if (_face == null)
            return;
        Canvas[] canvases = _face.GetComponentsInChildren<Canvas>(includeInactive: true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas c = canvases[i];
            if (c == null)
                continue;
            bool known = false;
            for (int k = 0; k < _neutralised.Count; k++)
            {
                if (ReferenceEquals(_neutralised[k].Canvas, c))
                {
                    known = true;
                    break;
                }
            }
            if (!known)
                _neutralised.Add((c, c.overrideSorting));
            if (c.overrideSorting)
                c.overrideSorting = false;
        }
    }

    private void RestoreCanvases()
    {
        for (int i = 0; i < _neutralised.Count; i++)
        {
            Canvas c = _neutralised[i].Canvas;
            if (c != null)
                c.overrideSorting = _neutralised[i].Original;
        }
        _neutralised.Clear();
    }

    /// <summary>
    /// Ask uGUI's OWN resolver whether this mask is on the counted side of the root sort-override
    /// canvas for a graphic under the face. This is not a proxy for the real computation — it is
    /// literally the pair of calls <c>MaskableGraphic</c> makes to decide its stencil value, so a
    /// pass here means the children WILL be stencil-tested.
    /// </summary>
    private void TryVerify()
    {
        if (_face == null || _mask == null || _released)
            return;
        Graphic? probe = null;
        Graphic[] graphics = _face.GetComponentsInChildren<Graphic>(includeInactive: false);
        for (int i = 0; i < graphics.Length; i++)
        {
            Graphic g = graphics[i];
            if (g == null || ReferenceEquals(g, _image) || !g.gameObject.activeInHierarchy)
                continue;
            probe = g;
            break;
        }
        if (probe == null)
            return; // nothing drawn yet — inconclusive, retried by LateUpdate

        Transform? root = MaskUtilities.FindRootSortOverrideCanvas(probe.transform);
        if (root == null)
            return; // no ACTIVE canvas in the chain (card parked under the inactive pool root)

        int depth = MaskUtilities.GetStencilDepth(probe.transform, root);
        if (depth >= 1)
        {
            Verified = true;
            if (!s_loggedInstalled[(int)_kind])
            {
                s_loggedInstalled[(int)_kind] = true;
                VRLog.Info("Cards", $"CARD FACE CLIP ({_kind}): INSTALLED and VERIFIED — the live card " +
                                    $"face is stencil-masked to the captured outline (probe '{probe.name}' " +
                                    $"resolves root sort canvas '{root.name}' at stencil depth {depth}, so " +
                                    "uGUI will stencil-test every graphic below the face). " +
                                    $"{_neutralised.Count} nested Canvas(es) inside the face had " +
                                    "overrideSorting forced false so the walk reaches us and the nested " +
                                    "canvas cannot draw out of order against the stencil write; the flag is " +
                                    "re-asserted per frame and restored on release. Whatever paints the " +
                                    "black rectangle — an art-baked frame, a strip, a RawImage, anything — " +
                                    "is now bounded by the card's own shape. Input is untouched: " +
                                    "Mask.IsRaycastLocationValid filters by RECT, and this rect IS the " +
                                    "face's rendered rect.");
            }
            return;
        }

        // Depth 0 — the mask would render a plain rectangle while every line above claims success.
        // That is precisely the failure this defect has already shipped once. Undo it, and latch so
        // no card of this kind tries again this session.
        s_refused[(int)_kind] = true;
        if (!s_loggedRefused[(int)_kind])
        {
            s_loggedRefused[(int)_kind] = true;
            VRLog.Warn("Cards", $"CARD FACE CLIP ({_kind}): REFUSED and REMOVED — stencil depth 0 for probe " +
                                $"'{probe.name}' against root sort canvas '{root.name}' even after forcing " +
                                $"overrideSorting false on {_neutralised.Count} nested Canvas(es). uGUI would " +
                                "not stencil-test the face's graphics, so the mask would have been an " +
                                "invisible no-op claiming success — the exact failure of attempt 1. The face " +
                                "is put back exactly as it was and the card keeps today's look; the card BODY " +
                                "clip is unaffected. If this line appears, the face cannot be clipped from " +
                                "above its own Canvas on this build and the next attempt must place the " +
                                "stencil INSIDE the card widget (or accept that the border is unremovable " +
                                "mod-side).");
        }
        ReleaseInternal();
    }

    // -------------------------------------------------------------------- sprite --

    /// <summary>
    /// The APPLIED footprint as a stencil sprite: white RGB, alpha binarised at the same 128 the
    /// mesh clip uses. No mip chain on purpose — uGUI's mask clips at <c>alpha - 0.001</c>, so a
    /// mip-blurred alpha would keep almost every texel and the mask would degrade back into a
    /// rectangle at distance, which is the one outcome that must not happen quietly.
    /// </summary>
    private static Sprite? ShapeSprite(CardBodyKind kind)
    {
        int i = (int)kind;
        byte[]? foot = CardMesh.Footprint(kind, out int w, out int h);
        if (foot == null || w <= 1 || h <= 1 || foot.Length != w * h)
            return null;
        if (s_shape[i] != null && ReferenceEquals(s_shapeSource[i], foot))
            return s_shape[i];

        var pixels = new Color32[foot.Length];
        for (int p = 0; p < foot.Length; p++)
        {
            byte a = foot[p] >= 128 ? (byte)255 : (byte)0;
            pixels[p] = new Color32(255, 255, 255, a);
        }
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: false)
        {
            name = $"GloomhavenVR.CardShapeMask.{kind}",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = $"GloomhavenVR.CardShapeMask.{kind}";
        s_shape[i] = sprite;
        s_shapeSource[i] = foot;
        VRLog.Info("Cards", $"CARD FACE CLIP ({kind}): shape sprite built {w}x{h} from the applied " +
                            "footprint (alpha binarised at 128, NO mip chain — uGUI masks clip at " +
                            "alpha-0.001, so a mipped mask would widen back into a rectangle at " +
                            "distance).");
        return sprite;
    }
}
