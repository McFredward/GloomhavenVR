using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cloned FRONT-art overlay for ONE card slot of a remote surface — a <see cref="RemoteHandFan"/>
/// slab, a <see cref="RemoteBoardCard"/> recess, or (since the user ruling of 2026-08-08) a slab of
/// the pile-browse and item fans via <see cref="RemotePileFronts"/>.
///
/// When (and ONLY when) <see cref="RevealGate.ShowRoundCardFronts"/> permits, the owning surface asks
/// each slot to show the REAL card face — art + enhancement stickers — of the corresponding card. We
/// render it by CLONING the game's own widget (<c>Object.Instantiate</c>) onto a world-space canvas
/// that floats a hair in front of the slab's owner-facing (−Z) side, matching the fan convention
/// "fronts look toward the owner". We NEVER adopt/reparent the LIVE widget (that would rip the face
/// out of the remote actor's own hidden hand UI and corrupt the game's bookkeeping) — always a
/// throwaway clone we fully own and destroy.
///
/// TWO KINDS OF SOURCE, ONE MECHANISM. An ABILITY card arrives as a <c>FullAbilityCard</c> (the
/// dedicated overload below, which also re-hands the runtime skin). Anything else arrives through the
/// generic <c>ShowFront(GameObject, key, …)</c> overload with a caller-supplied dedup key and an
/// optional "configure the clone before it activates" hook — which is what an ITEM card needs, because
/// its widget has to be manufactured from the pool per call and its model reference re-planted before
/// <c>OnEnable</c> dereferences it (see <see cref="RemoteItemCardSource"/>).
///
/// ANTI-CHEAT: this class holds NO gate logic of its own — it renders a face only when its owner
/// hands it a source widget, and every owner gates that strictly on
/// <see cref="RevealGate.ShowRoundCardFronts"/>. Every game deref here is null-guarded and the whole
/// clone is wrapped so that ANY failure leaves the slot showing NO front (the slab's card BACK
/// remains) — fail-safe = no leak. The clone is made fully NON-INTERACTIVE (raycasters removed +
/// a blocking <see cref="CanvasGroup"/>) so a local player can never poke a remote player's card
/// action buttons through it.
///
/// ENHANCEMENTS: the enhancement stickers ("Verbesserungen") live as child objects of the source
/// <c>fullAbilityCard</c> (populated in <c>MakeFullCard</c> onto the top/bottom action buttons), so
/// the deep <c>Instantiate</c> copy carries them along with whatever sprites the owner's widget had
/// loaded — the clone therefore shows the same enhancements the owner sees.
///
/// ART LOAD: the header illustration is loaded via an <c>ImageAddressableLoader</c> keyed by the
/// widget's runtime <c>_skin</c> (a NON-serialized field, so <c>Instantiate</c> does NOT copy it).
/// We best-effort re-hand the source skin to the clone so its own <c>OnEnable → ShowCard</c> reloads
/// the real header art even if the (hidden) source hand never had it loaded; if the source skin is
/// itself null the clone still shows the copied frame/actions/enhancements minus the header art.
///
/// STATE DECORATIONS ARE PART OF THE PICTURE, NOT AN EXTRA. The FX driver components are stripped off
/// every clone (see <see cref="StripFragileEffects"/>) because they cannot be run on a detached,
/// inactive, world-space copy — but for an ITEM card that used to mean a peer's SPENT item looked
/// FRESH while its owner's own board showed it ghosted grey, and the 1:1 ruling covers displays.
/// <see cref="ApplySpentLook"/> rebuilds that decoration from the game's own settled end-state onto
/// materials this overlay mints and destroys. Read its doc before touching either method: it also
/// records the three independent reasons the obvious fix (playing the game's own state FX on the
/// borrowed widget) cannot work, so nobody re-derives them.
///
/// Cheap to drive per frame: the clone is (re)built only when the shown card identity changes (or a
/// front first appears), and torn down on hide/rebuild/teardown — no per-frame allocations.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. It renders a face from a game-owned widget
/// resolved by <see cref="RemoteAbilityCardSource"/> off the host-replicated model. No card
/// identity, art reference or enhancement state ever crosses the wire; all of it is
/// DELIBERATELY-NOT. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteCardArt
{
    // Physical card size the clone is fit to (matches the slab it overlays).
    private readonly float _cardWidth;
    private readonly float _cardHeight;

    // Standoff toward −Z (owner side), a hair in front of the slab's front face so the art wins the
    // depth test over the card-back slab. The slab front face sits at z = −Thickness/2.
    private static readonly float FrontStandoff = CardMesh.Thickness * 0.5f + 0.0006f;

    // Fallback face pixel size when the cloned rect is degenerate.
    private static readonly Vector2 DefaultFaceSize = new(270f, 400f);

    // Small inset so the art sits just inside the slab silhouette (mirrors CardFace.BorderFraction).
    private const float BorderFraction = 0.06f;

    /// <summary>Re-run the shared mip-bake sprite swap over the shown clone this often (seconds) —
    /// the exact cadence the LOCAL card faces use (<c>CardFace.MipRescanInterval</c>). Needed
    /// because the clone's art arrives ASYNC (OnEnable → ShowCard → addressable header load) and
    /// the game can hand sub-widgets fresh mipless sprites after the build-time pass ran.</summary>
    private const float MipRescanInterval = 1f;

    private readonly Transform _slab;
    private GameObject? _host;      // world-space canvas host (child of the slab), inactive when hidden
    private Canvas? _canvas;
    private GameObject? _clone;     // the instantiated fullAbilityCard clone (child of _host)
    private int _shownSourceId = int.MinValue; // GetInstanceID of the source fullAbilityCard shown
    private float _nextMipRescan;   // unscaled time of the next cadenced mip-bake rescan

    /// <summary>Is the slab's card body currently serving its FACE-HOSTED mesh — i.e. has this
    /// overlay told <see cref="CardMesh.SetBodyFaceHosted"/> that its print covers the body's front
    /// face? Held here so the cadenced re-ask in <see cref="MaintainBodyFaceHosting"/> costs nothing
    /// while the verdict has not moved, and so <c>CardMesh.AttachBody</c> can carry the switch across
    /// an in-place re-cut.</summary>
    private bool _bodyFaceHosted;

    /// <summary>
    /// How much of the body's front box the print has to cover before the front fan behind it may be
    /// dropped (see <see cref="ApplyBodyFaceHosting"/>). Not 1.0 exactly, because the surfaces that
    /// size a print to a slab reach the same rectangle through two independently rounded routes —
    /// the item fan takes its slab aspect from the quantised Item FOOTPRINT grid while the print
    /// takes it from the live widget's pixel rect — and a strict equality test would refuse a print
    /// that is flush to within a twentieth of a millimetre. 0.1 % of a 63.5 mm card is 0.06 mm, well
    /// under one texel of anything drawn on it.
    /// </summary>
    private const float RequiredBodyCoverage = 0.999f;

    /// <summary>The printed rectangle in SLAB-LOCAL metres, as measured by the last
    /// <see cref="FitClone"/> — the one moment that number is true. Kept so
    /// <see cref="MaintainBodyFaceHosting"/> can re-run the verdict without re-deriving it from a
    /// rect that may be mid-relayout. Zero until a print has been fitted.</summary>
    private Vector2 _printedLocalMeters;

    /// <summary>Unscaled time of the next cadenced re-run of the face-hosting verdict. See
    /// <see cref="MaintainBodyFaceHosting"/> for why the verdict cannot be a one-shot.</summary>
    private float _nextFaceHostRecheck;

    /// <summary>How often the face-hosting verdict is re-asked (seconds). It only ever changes when
    /// a peer fan registers itself with <c>PeerBoardFade.Follow</c>, which happens once per fan per
    /// session and normally BEFORE the first print — this is the backstop for the reverse order,
    /// not a poll of anything that moves.</summary>
    private const float FaceHostRecheckInterval = 1f;

    /// <summary>One-shot per VERDICT CLASS, not per card: the hosted reading, the coverage refusal
    /// and the "this surface never fades" refusal each print once per session with the numbers that
    /// produced them. A fan of twelve chips must not write twelve identical lines, and an acceptance
    /// must not summarise a refusal away. NOTE THAT THESE ARE STATIC: one line means "at least one
    /// surface reached this verdict", never "exactly one did" — reading the single hosting line in
    /// the ModBuild 445 log as a count of one card is what hid this defect for a round.</summary>
    private static bool _loggedFaceHosted;
    private static bool _loggedFaceHostRefused;
    private static bool _loggedFaceHostNeverFades;

    /// <summary>
    /// The peer half of the local cards' zero-aliased-frame fix (see <see cref="Cards.CardArtWatch"/>).
    /// The clone runs the game's OWN widget, so its header art arrives through the same async
    /// addressable loader with the same two-continuation delay — and until this existed, a remote
    /// card showed the mipless original for up to <see cref="MipRescanInterval"/> exactly like a
    /// local one did. The 1:1 rule cuts both ways: a peer's card must not look worse on our screen
    /// than our own does.
    /// </summary>
    private readonly Cards.CardArtWatch _artWatch = new();

    /// <summary>
    /// WHICH "already used" look the game plays over an ITEM card face, if any. The three values are
    /// exactly the three arms of <c>ItemCardUI.UpdateState</c> (ItemCardUI.cs:376-395) —
    /// <c>Spent</c> → <c>ItemCardEffects.GhostOutOnTimeline</c>, <c>Consumed</c> →
    /// <c>ItemCardEffects.BurnCardTimeline</c>, everything else → the card's REST look — so the
    /// mirror can never invent a fourth state the owner's board does not have.
    /// </summary>
    internal enum SpentLook
    {
        None = 0,
        Spent = 1,
        Consumed = 2,
    }

    /// <summary>
    /// The <c>ItemCardEffects</c> draw rig, lifted off the CLONE in the instant before that component
    /// is destroyed (see <see cref="StripFragileEffects"/>).
    ///
    /// <para>THE CAPTURE IS ON THE CLONE AND NEVER ON THE BORROWED SOURCE, and that is the whole
    /// design. <c>Object.Instantiate</c> re-points a serialized reference that names an object INSIDE
    /// the copied hierarchy at the copy, so <c>imgComp</c>/<c>txtComp</c> read off the clone's own
    /// component are the clone's own Images and texts. Reading them off the pooled widget instead
    /// would hand us the GAME's objects, and everything downstream of that is a write into game
    /// state.</para>
    /// </summary>
    private readonly struct ItemFxRig
    {
        public readonly UnityEngine.UI.Image[]? Images;
        public readonly TMPro.TextMeshProUGUI[]? Texts;

        public ItemFxRig(UnityEngine.UI.Image[]? images, TMPro.TextMeshProUGUI[]? texts)
        {
            Images = images;
            Texts = texts;
        }
    }

    /// <summary>Materials this overlay MINTED for the shown clone and therefore owns outright. They
    /// die with the clone in <see cref="DestroyClone"/>: a <c>Destroy</c> of a GameObject does not
    /// take the materials its Images point at, so without this list a fan that re-ghosts a card on
    /// every state flip would leak one material per card image per flip.</summary>
    private readonly List<Material> _ownedMaterials = new(8);

    public RemoteCardArt(Transform slab, float cardWidth, float cardHeight)
    {
        _slab = slab;
        _cardWidth = cardWidth;
        _cardHeight = cardHeight;
    }

    /// <summary>
    /// Is a front currently up that was built for <paramref name="key"/>? The dedup question asked
    /// from OUTSIDE, so a caller whose source widget does not exist yet (an ITEM card, which the game
    /// only ever manufactures from the pool — see <see cref="RemoteItemCardSource"/>) can skip the
    /// whole borrow when the right face is already showing. Without it every such caller would have
    /// to spawn a widget just to learn its instance id, which is precisely the per-frame cost the
    /// dedup exists to avoid.
    /// </summary>
    public bool ShowsKey(int key) => _clone != null && _host != null && _shownSourceId == key;

    /// <summary>
    /// Show the cloned face of <paramref name="source"/> (a remote hand card's live
    /// <c>fullAbilityCard</c>). Dedups by instance id — a no-op if the same source is already shown.
    /// Any failure clears the front (fail-safe to the slab back). Returns true iff a front is shown.
    /// </summary>
    public bool ShowFront(FullAbilityCard source)
    {
        if (source == null)
        {
            HideFront();
            return false;
        }
        // …plus the SKIN FIXUP (2026-08-13, report "weiße vierecke mit manchen symbolen drin"): the
        // two FullAbilityCardAction halves keep their skin + action-sprite references in PLAIN
        // private runtime fields, which Object.Instantiate cannot copy, so a clone streamed no
        // action backgrounds at all and drew uGUI's built-in white texture. RemoteAbilityCardSource
        // replays the game's own SetSkin on the clone through the beforeActivate seam — the whole
        // derivation, and why it is not a call to FullAbilityCard.SetSkin itself, is documented
        // there. Null (= no skin to hand over) leaves this call byte-for-byte as it was.
        return ShowFront(source.gameObject, source.GetInstanceID(), source,
                         RemoteAbilityCardSource.SkinFixup(source));
    }

    /// <summary>
    /// The GENERIC clone path: show a throwaway copy of <paramref name="sourceGo"/>, dedup'd on a
    /// CALLER-supplied <paramref name="key"/>.
    ///
    /// <para>WHY THE KEY IS A PARAMETER RATHER THAN <c>sourceGo.GetInstanceID()</c>: an ITEM card has
    /// no long-lived widget anywhere on this client (the game manufactures one from the pool on
    /// demand), so its source object is a different instance on every call and would defeat instance-id
    /// dedup entirely — a clone rebuilt every frame, for every chip, for every peer. The item source
    /// keys on the ITEM instead, which is stable for as long as the peer keeps that item equipped, and
    /// so never rebuilds a settled fan. Keys from different KINDS of source must never meet on the same
    /// overlay: the pile-browse fan, which is the one surface that can switch between ability and item
    /// content on the same slabs, calls <see cref="HideFront"/> on every overlay when its content kind
    /// changes (see <see cref="RemotePileFronts.Tick"/>), so the key space is cleared with it.</para>
    ///
    /// <paramref name="skinSource"/> is the ability-card widget whose runtime <c>_skin</c> the clone
    /// needs re-handed (see <see cref="TryReapplySkin"/>); null for non-ability sources.
    /// <paramref name="beforeActivate"/> runs on the clone while it is still INACTIVE — the seam where
    /// a caller re-plants the non-serialized model reference its widget dereferences in
    /// <c>OnEnable</c> (an <c>ItemCardUI</c> reads <c>item.ID</c> the instant it activates).
    ///
    /// <paramref name="spentLook"/> is the "already used" decoration the OWNER's board is showing over
    /// this card right now (item cards only; see <see cref="ApplySpentLook"/>). It is part of the DEDUP
    /// KEY at the caller, not a property of the clone — a card whose state flips must re-key, or the
    /// face that is already up would keep its old look forever.
    /// </summary>
    public bool ShowFront(GameObject sourceGo, int key, FullAbilityCard? skinSource = null,
        System.Action<GameObject>? beforeActivate = null, SpentLook spentLook = SpentLook.None)
    {
        if (sourceGo == null)
        {
            HideFront();
            return false;
        }

        if (_clone != null && key == _shownSourceId && _host != null)
        {
            if (!_host.activeSelf)
                _host.SetActive(true);
            // The hand fan calls ShowFront every frame while a front is up, so the steady-state
            // cadence rescan lives right here on the dedup path — the remote twin of
            // CardFace.Maintain's 1 s loop (the board slots, whose Set() is change-gated instead,
            // reach the same loop through MaintainMipBake below).
            MaintainMipBake();
            return true;
        }

        // Identity changed (or first show): rebuild the clone.
        DestroyClone();
        try
        {
            EnsureHost();
            if (_host == null)
                return false;

            // Build the clone UNDER the inactive host so the cloned widget's Awake/OnEnable does not
            // run until we have neutralized interaction and re-applied the skin.
            var clone = Object.Instantiate(sourceGo, _host.transform, worldPositionStays: false);
            clone.name = "FrontArtClone";
            _clone = clone;

            Neutralize(clone);
            ItemFxRig itemFx = StripFragileEffects(clone);
            if (skinSource != null)
                TryReapplySkin(skinSource, clone);
            beforeActivate?.Invoke(clone);

            // A clone copies the source's own activeSelf, and a widget BORROWED from the pool is
            // handed out deactivated (activate:false) so it can never render before the gate. Flip the
            // clone on here — it is still under the INACTIVE host, so nothing runs yet — rather than
            // leaving it to FitClone below: that way the host activation is what runs the widget's
            // OnEnable, and FitClone's pose write stays the FINAL one on every path, not just on the
            // paths whose source happened to be active.
            if (!clone.activeSelf)
                clone.SetActive(true);

            // Owned head camera renders the mod layer only; put the whole clone subtree on it (the
            // game face was on a game UI layer). It's a throwaway clone we own, so re-layering is safe.
            VRLayers.Apply(_host);

            _host.SetActive(true);   // clone activates → OnEnable → ShowCard reloads the real art
            FitClone(clone);         // final pose write, so OnEnable's own reposition can't offset it

            // THE OWNER'S "already used" LOOK, rebuilt on materials this overlay owns. AFTER the
            // activation on purpose: the widget's own OnEnable is the last thing that could touch its
            // Images, so writing here means nothing the game runs can land on top of the result.
            ApplySpentLook(itemFx, spentLook);

            // MIP BAKE (user report: "the aliasing on the remote cards is extreme — the fix for my
            // own local cards should apply here too"). The clone's Image sprites are verbatim
            // copies of the source's, i.e. they sample the game's MIPLESS UI atlases — the exact
            // data defect CardFaceMipBake exists for, and the remote faces bypassed it entirely.
            // One immediate pass swaps everything already copied; the cadenced rescan (see
            // MaintainMipBake) catches the async header art and any sprite the widget re-assigns.
            // Shared cache: an atlas the local faces already baked costs nothing here, and vice
            // versa. The clone is a throwaway we own, so no restore pass is ever needed.
            RescanMips();

            // SHAPE THE PEER'S CARD FROM ITS FIRST DRAWN FRAME (2026-08-11). The local
            // face is stencil-clipped to the captured card outline; a peer's clone is the
            // same widget on a world-space canvas, so under the 1:1 rule it takes the same
            // clip. Here rather than only on art arrival, because FitClone has just written
            // the final pose and scale — the wrapper fits the face's RENDERED rect, so this
            // is the first moment that rect is correct. A no-op with no footprint yet.

            _shownSourceId = key;
            return true;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"RemoteCardArt clone failed ({ex.Message}) — slot stays a card back.");
            DestroyClone();
            HideFront();
            return false;
        }
    }

    /// <summary>
    /// Cadenced mip-bake upkeep while a front is shown — the remote equivalent of the 1 s rescan
    /// loop in <c>CardFace.Maintain</c> (and <c>ItemsPile</c>'s hosted card): the clone's header
    /// illustration arrives ASYNC after activation, so a single build-time pass would leave
    /// exactly the biggest image on the card mipless. Idempotent-cheap once warm (dictionary hits
    /// inside <see cref="CardFaceMipBake.Rescan"/>); a hidden/absent front early-returns. Called
    /// per frame by the hand fan (via ShowFront's dedup path) and on the 4 Hz content cadence by
    /// the board-card owners, whose Set() is change-gated and would otherwise never rescan.
    /// </summary>
    public void MaintainMipBake()
    {
        if (_clone == null || _host == null || !_host.activeSelf)
            return;
        MaintainBodyFaceHosting();
        // ARRIVAL FIRST (zero-alloc, one reference compare per Image): the clone's header art
        // lands in a loader continuation, and this catches it in that very frame instead of
        // whenever the cadence below next happens to fire. The cadenced pass stays as the
        // backstop for Images the clone's widget created after the watch was captured.
        if (_artWatch.Poll("remote card") > 0)
        {
            // …and mute the peer clone's sprite-less black quads in the SAME frame the art lands,
            // for the same reason the mip swap happens here rather than on the cadence: a peer's
            // card must not show the black rectangle for even one frame that the owner's card does
            // not (user, 2026-08-11: "auch alle Karten genauso die remote angezeigt werden").
            // Belt to the build-time RescanMips walk, which already covers a clone's first draw.
            Cards.CardFace.Offer(_clone.transform, artJustArrived: true);
            // …and install the shape clip if the footprint only became available after this
            // clone was built (cold cache). Wrap is idempotent, so the warm-cache case that
            // already clipped at the build seam costs one early-out.
            _nextMipRescan = Time.unscaledTime + MipRescanInterval;
        }
        if (Time.unscaledTime < _nextMipRescan)
            return;
        RescanMips();
    }

    /// <summary>One shared-cache sprite-swap pass over the clone, cadence re-armed. Guarded inside
    /// <see cref="CardFaceMipBake.Rescan"/> itself (a bake surprise never breaks the face) and
    /// config-gated there on [Cards] FaceMipBake — the same switch the local cards obey.</summary>
    private void RescanMips()
    {
        if (_canvas != null)
        {
            CardFaceMipBake.Rescan(_canvas);
            _artWatch.Capture(_canvas); // (re)arm the per-frame arrival watch on the current clone
        }
        _nextMipRescan = Time.unscaledTime + MipRescanInterval;
    }

    /// <summary>Hide any front (the slab's card BACK shows through). Keeps the host for reuse.</summary>
    public void HideFront()
    {
        if (_clone != null || _shownSourceId != int.MinValue)
            DestroyClone();
        if (_host != null && _host.activeSelf)
            _host.SetActive(false);
        // No print in front of it any more, so the body owns its front face again — and it MUST get
        // it back on this same path, or a peer's face-DOWN card would show a see-through hole where
        // its card back belongs, which is the hidden-information leak this whole class exists to
        // avoid. The release is on HideFront rather than on DestroyClone on purpose: ShowFront
        // rebuilds a clone in place when the shown card changes, and the body should not flicker its
        // front fan back for a frame in between.
        ReleaseBodyFaceHosting();
    }

    public void Destroy()
    {
        DestroyClone();
        ReleaseBodyFaceHosting();
        if (_host != null)
        {
            Object.Destroy(_host);
            _host = null;
            _canvas = null;
        }
    }

    // ------------------------------------------------------------------ internals --

    private void EnsureHost()
    {
        if (_host != null)
            return;

        _host = new GameObject("FrontArt");
        _host.transform.SetParent(_slab, worldPositionStays: false);
        // Float a hair in front of the slab's owner-facing (−Z) face, identity rotation so the uGUI
        // content reads from the −Z side — the exact convention VRCard's FaceCanvas uses.
        _host.transform.localPosition = new Vector3(0f, 0f, -FrontStandoff);
        _host.transform.localRotation = Quaternion.identity;
        _host.transform.localScale = Vector3.one;

        _canvas = _host.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head != null)
            _canvas.worldCamera = head;

        _host.SetActive(false); // stays inactive until a clone is built + neutralized
    }

    /// <summary>Make the clone purely cosmetic: remove uGUI raycasters and add a blocking
    /// <see cref="CanvasGroup"/> so a local poke/laser can never drive a remote card's buttons.
    /// The clone is still inactive here (no Awake has run), so component removal is side-effect free.
    ///
    /// <para>THE <c>interactable = false</c> BELOW HAS A VISIBLE SIDE EFFECT, and it was the reported
    /// colour defect for three hardware rounds. It makes <c>Selectable.IsInteractable()</c> false for
    /// every button under the face (that method is <c>m_GroupsAllowInteraction &amp;&amp;
    /// m_Interactable</c>, and this falsifies the FIRST term — the one no census had read), which puts
    /// the two action plates in <c>SelectionState.Disabled</c>; the plates are SPRITE-SWAP Selectables,
    /// so the engine then draws the skin's DISABLED plate artwork, a ≈0.85-desaturated copy of the
    /// regular one. The line STAYS — it is anti-cheat and it is cheap — and
    /// <see cref="RemoteAbilityCardSource.NeutralizePlateLook"/> removes the TRANSITION that turns it
    /// into pixels, using the game's own <c>DisableHoverHighlight</c> route. The whole derivation, the
    /// photographed numbers and why every earlier remedy measured clean and changed nothing are in the
    /// block comment above that method.</para></summary>
    private static void Neutralize(GameObject clone)
    {
        var raycasters = clone.GetComponentsInChildren<UnityEngine.UI.GraphicRaycaster>(includeInactive: true);
        for (int i = 0; i < raycasters.Length; i++)
        {
            if (raycasters[i] != null)
                Object.Destroy(raycasters[i]);
        }

        CanvasGroup cg = clone.GetComponent<CanvasGroup>();
        if (cg == null)
            cg = clone.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false; // immediate + robust: blocks all raycasts to the subtree this frame

        // …and immediately undo the LOOK that line implies, never the safety it buys. Unconditional:
        // no defect count gates it, so it cannot silently skip the faces it was shipped to correct.
        RemoteAbilityCardSource.NeutralizePlateLook(clone);
    }

    /// <summary>
    /// Strip the <c>CardEffects</c> / <c>ItemCardEffects</c> FX drivers from the clone BEFORE it
    /// activates. CardEffects'
    /// <c>Initialize</c> (Awake) swaps the card's image materials to a CUSTOM screen-space shader keyed
    /// by a <c>_PosAndBounds</c> that is only valid for the card's ORIGINAL hand-canvas position — on a
    /// detached world-space clone that shader is the known "card renders DEEP BLACK" hazard
    /// (CardFace.Maintain documents the identical burn/lose-popup failure). Removing the component while
    /// the clone is still INACTIVE means Initialize never runs, so every card Image keeps its plain
    /// copied material and renders the REAL art/frame/text/enhancements via the standard UI shader. We
    /// lose only the cosmetic holo/foil sheen — an acceptable, deterministic trade for a correct render.
    /// Uses <c>DestroyImmediate</c> because a deferred Destroy would still let Awake run this frame; safe
    /// here since the clone has never been active (no Awake yet) and all game refs to cardEffects are
    /// null-guarded in the widget code we let run.
    ///
    /// <para><c>ItemCardEffects</c> is the ITEM card's twin and is stripped too, but NOT for the same
    /// reason and NOT at the same cost — see <see cref="ApplySpentLook"/>, which puts the look the
    /// strip used to throw away back on the clone. It is stripped because it cannot be run here at all:
    /// it drives a particle emitter whose size is only valid at the card's original canvas scale
    /// (<c>Cards.ItemsPile</c> keeps it alive only because it hosts the widget at a measured scale and
    /// clamps that emitter — <c>ClampCardEffectSmoke</c>; a detached clone has no such clamp), its
    /// <c>GhostOutOn</c>/<c>BurnCard</c> entry points route through
    /// <c>Choreographer.s_Choreographer.StartCoroutine</c> and fall back to <c>StartCoroutine</c> on a
    /// widget we require to be INACTIVE (which throws), and both timelines dereference
    /// <c>Timekeeper.instance.m_GlobalClock</c>. The strip also removes the one component
    /// <c>ItemCardUI.OnReturnedToPool</c> dereferences without a null check — harmless here because a
    /// clone is never recycled, and the BORROWED source is never touched by this method.</para>
    ///
    /// <para>WHAT IT RETURNS, AND WHY IT HAS TO. <c>ItemCardEffects</c> is also the only object that
    /// knows WHICH of the card's Images and texts the state FX paint (<c>imgComp</c>/<c>txtComp</c>,
    /// ItemCardEffects.cs:54/56) — a hand-authored subset, not "every Graphic". Those two arrays are
    /// lifted off the CLONE's own component here, in the last instant they exist, and handed back so
    /// <see cref="ApplySpentLook"/> can reproduce the look on them. Capturing them AFTER the destroy is
    /// impossible and capturing them from the SOURCE would name the game's objects instead of ours.</para>
    /// </summary>
    private static ItemFxRig StripFragileEffects(GameObject clone)
    {
        UnityEngine.UI.Image[]? images = null;
        TMPro.TextMeshProUGUI[]? texts = null;
        try
        {
            var effects = clone.GetComponentsInChildren<CardEffects>(includeInactive: true);
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] != null)
                    Object.DestroyImmediate(effects[i]);
            }
            var itemEffects = clone.GetComponentsInChildren<ItemCardEffects>(includeInactive: true);
            for (int i = 0; i < itemEffects.Length; i++)
            {
                if (itemEffects[i] == null)
                    continue;
                // First rig wins: an item card carries exactly one of these, and a face that somehow
                // carried two would be a card inside a card — take the outermost and say nothing.
                if (images == null)
                {
                    images = itemEffects[i].imgComp;
                    texts = itemEffects[i].txtComp;
                }
                Object.DestroyImmediate(itemEffects[i]);
            }
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Net", $"RemoteCardArt CardEffects strip skipped: {ex.Message}");
        }
        return new ItemFxRig(images, texts);
    }

    // ─────────────────────────────────── the "already used" look on a peer's item card ──────────

    /// <summary>The four state terms <c>ItemCardEffects</c> drives on a card image, plus the four that
    /// SHAPE them. Ids, never names: the same call the game itself makes on the same property.</summary>
    private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut");
    private static readonly int FlowId = Shader.PropertyToID("_Flow");
    private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
    private static readonly int BurnId = Shader.PropertyToID("_Burn");
    private static readonly int BurnColourTintId = Shader.PropertyToID("_Burn_ColourTint");
    private static readonly int FlowOffsetId = Shader.PropertyToID("_Flow_Offset");
    private static readonly int FlowSpeedId = Shader.PropertyToID("_Flow_Speed");
    private static readonly int AnimNoiseMaskId = Shader.PropertyToID("_AnimNoise_Mask");
    private static readonly int DissolveVerticalGradientId = Shader.PropertyToID("_Dissolve_VerticalGradient");

    /// <summary>The SIGNATURE that identifies a card-FX material without naming a shader or an asset:
    /// <c>_GreyOut</c> AND <c>_PosAndBounds</c> together, the same pair — for the same reason — that
    /// <see cref="Cards.CardHalfTone"/> uses. A shader name is a string a game patch can change; a
    /// property pair is what the code actually reads. <c>_PosAndBounds</c> is READ here (as the refusal
    /// gate) and NEVER written.</summary>
    private static readonly int PosAndBoundsId = Shader.PropertyToID("_PosAndBounds");

    /// <summary>One-shot latches: the first face that takes the look, and the first face refused by the
    /// bounds gate. Never per card, never per frame.</summary>
    private static bool s_spentLookLogged;
    private static bool s_spentBoundsRefused;

    /// <summary>
    /// REBUILD THE OWNER'S "already used" DECORATION ON A CLONE WE OWN OUTRIGHT.
    ///
    /// <para>THE DEFECT. A spent item is ghosted grey on its owner's board — <c>Cards.ItemsPile</c>
    /// hosts a LIVE <c>ItemCardUI</c> and calls <c>UpdateState(item.SlotState, force: true)</c>
    /// (ItemsPile.cs:5086) which reaches <c>ItemCardEffects.GhostOutOnTimeline</c> — and it was fresh on
    /// every peer's, because this class strips the component that plays it. Under the 1:1 ruling a peer
    /// must see what the owner sees, DISPLAYS included, so the look is owed. There is no wire debt: the
    /// item model that manufactures the face carries <c>SlotState</c> on every client already
    /// (<see cref="RemoteItemCardSource"/> reads it off the same host-replicated list
    /// <c>RemotePileFronts.TryResolveItemSpentFlags</c> walks).</para>
    ///
    /// <para>WHY THE OBVIOUS FIX — call the game's own <c>UpdateState</c> on the borrowed widget — IS
    /// DEAD, three times over. DO NOT RE-ATTEMPT IT. (1) <c>RemoteItemCardSource.TryPooledClone</c>
    /// hands the widget back through <c>ObjectPool.RecycleCard</c> in a <c>finally</c>;
    /// <c>ObjectPool.cs:560</c> calls <c>OnReturnedToPool()</c>, which is
    /// <c>cardEffects.RestoreCard()</c> (ItemCardUI.cs:363), which re-zeroes
    /// <c>_GreyOut/_Flow/_Dissolve/_Burn</c> on <c>imgComp[i].material</c> — the very material objects
    /// <c>Instantiate</c> handed the clone BY REFERENCE. The look would be undone synchronously before
    /// the clone drew a single frame. (2) The borrow is <c>activate: false</c> under an inactive
    /// holder, so <c>ItemCardEffects.Initialize</c> — the method that mints the per-Image
    /// <c>new Material(...)</c> — has never run on it: <c>imgCount</c> is 0 and the image loop is a
    /// no-op, but <c>GhostOutOnTimeline</c> still does <c>fgFx.gameObject.SetActive(true)</c> and eight
    /// <c>fgFx.material.Set*</c> calls on the SHARED AUTHORED MATERIAL of a widget going straight back
    /// into the game's own pool. That is presentation code writing game state and this project forbids
    /// it outright. (3) Even if it stuck, the clone's Images point at the POOLED widget's materials, so
    /// the next borrow of that pool entry would repaint a peer's card underneath it.</para>
    ///
    /// <para>SO THE LOOK IS REBUILT, NOT REPLAYED. Every write here lands on a <c>new Material</c> this
    /// overlay minted and this overlay destroys (<see cref="_ownedMaterials"/>), assigned to the CLONE's
    /// own Images. Nothing the game owns is read-modify-written, no restore contract is created, and
    /// there is no shared asset anywhere in the chain — which answers all three blockers at once. The
    /// numbers are the game's own settled end-states, copied verbatim from the non-animated arms of
    /// <c>GhostOutOnTimeline</c> (ItemCardEffects.cs:464-486) and <c>BurnCardTimeline</c>
    /// (:356-372); the mirror plays no animation, because a peer's card can appear on our board long
    /// after the owner's timeline ran and a fade starting THEN would be a picture the owner never had.</para>
    ///
    /// <para>THE <c>_PosAndBounds</c> HAZARD, AND WHY IT DOES NOT REACH THIS WRITE. The strip above
    /// exists for the "card renders DEEP BLACK" incident, so a write that turns the FX terms ON has to
    /// answer it. It is an ABILITY-card mechanism: <c>CardEffects.Initialize</c> SWAPS every image to
    /// <c>new Material(_lowMaterial)</c> under <c>_useLowEffect</c> (CardEffects.cs:318-322) and then
    /// writes a canvas-space <c>_PosAndBounds</c> into it. <c>ItemCardEffects.Initialize</c> swaps
    /// NOTHING (ItemCardEffects.cs:159-174): it instances the Image's ALREADY-AUTHORED material and
    /// writes the same property into the copy — so an item card draws through its FX material with or
    /// without that component, which is why the peer's item face renders correctly TODAY at
    /// <c>_GreyOut = _Flow = _Dissolve = _Burn = 0</c>. And the terms have been proven nonzero on a
    /// world-space canvas already: <c>ItemsPile.TryHostRealCard</c> runs the full ghost on a
    /// <c>RenderMode.WorldSpace</c> canvas where <c>Initialize</c> wrote <c>_PosAndBounds</c> from a VR
    /// WORLD position in METRES against a PIXEL rect — the most mis-scaled value this material will ever
    /// be handed — and the result is the ghost the user is looking at on his own board and asked to have
    /// back (INVARIANTS-Cards.md, "the whole 'verbraucht' look … must stay live"). Nonzero FX plus a
    /// meaningless <c>_PosAndBounds</c> is a SHIPPED, ACCEPTED configuration, not a guess.</para>
    ///
    /// <para>WHAT IS STILL GUARDED ANYWAY. We do not WRITE <c>_PosAndBounds</c> — reconstructing it is
    /// exactly the guess the strip exists to avoid, and <see cref="Cards.CardHalfTone"/> already carries
    /// that standing ruling. We READ it instead: a card-FX material whose extents are still (0, 0) would
    /// hand the shader a degenerate footprint the instant the terms stop being 0, so such a face is
    /// REFUSED whole and stays fresh, once-logged. Missing grey is a small divergence; a black card on a
    /// peer's board is not.</para>
    ///
    /// <para>TWO PARTS OF THE GAME'S LOOK ARE DELIBERATELY NOT REPRODUCED. The <c>fx_Smoke</c> emitter
    /// both timelines switch on is the diorama-fogging particle system <c>ItemsPile</c> only dares keep
    /// because it CLAMPS it (<c>ClampCardEffectSmoke</c>) — a fan slab has no such clamp, and fog over a
    /// peer's board is a worse divergence than the one being fixed. The <c>fgFx</c> overlay quad is a
    /// second, unmeasured material driven by nine more properties and drawn over the WHOLE card; if any
    /// of that lands wrong the failure is a full-card artefact, which is the one class of failure this
    /// method is written to avoid. Cost: the ghost's faint blue-white frame sheen. The dominant term,
    /// the <c>_GreyOut</c> desaturation, and the greyed card text are both here.</para>
    /// </summary>
    private void ApplySpentLook(in ItemFxRig rig, SpentLook look)
    {
        if (look == SpentLook.None || rig.Images == null)
            return;
        try
        {
            // MEASURE FIRST, ALL OR NOTHING. A face with three of its eight images ghosted reads as a
            // rendering fault, not as a used card, so the bounds gate decides for the whole face.
            int qualifying = 0;
            for (int i = 0; i < rig.Images.Length; i++)
            {
                Material? mat = MaterialOf(rig.Images[i]);
                if (mat == null || !IsCardFxMaterial(mat))
                    continue;   // not an FX image — the game's own write is inert on it too
                Vector4 bounds = mat.GetVector(PosAndBoundsId);
                if (bounds.z == 0f && bounds.w == 0f)
                {
                    ReportBoundsRefusal(look);
                    return;
                }
                qualifying++;
            }
            if (qualifying == 0)
                return;

            for (int i = 0; i < rig.Images.Length; i++)
            {
                UnityEngine.UI.Image image = rig.Images[i];
                Material? mat = MaterialOf(image);
                if (mat == null || !IsCardFxMaterial(mat))
                    continue;
                Material copy;
                try
                {
                    copy = new Material(mat) { name = mat.name + " (VR-used)" };
                }
                catch (System.Exception)
                {
                    continue;   // one image short of the look is still a readable card
                }
                WriteUsedTerms(copy, look);
                image.material = copy;
                _ownedMaterials.Add(copy);
            }

            // The card TEXT is not a material write at all — the game just recolours it, and the two
            // timelines disagree on the colour: the ghost lerps all the way to WHITE and kills the
            // vertex gradient (ItemCardEffects.cs:483-484), the burn goes to mid grey and leaves the
            // gradient alone (:370).
            if (rig.Texts != null)
            {
                Color used = look == SpentLook.Spent ? Color.white : new Color(0.5f, 0.5f, 0.5f, 1f);
                for (int i = 0; i < rig.Texts.Length; i++)
                {
                    TMPro.TextMeshProUGUI text = rig.Texts[i];
                    if (text == null)
                        continue;
                    text.color = used;
                    if (look == SpentLook.Spent)
                        text.enableVertexGradient = false;
                }
            }

            ReportLookOnce(look, qualifying);
        }
        catch (System.Exception ex)
        {
            // NEVER throw from this path: the front is already up and correct without the decoration.
            VRLog.Debug("Net", $"Remote ITEM used-look skipped ({ex.Message}) — the peer's card shows " +
                               "the FRESH face.");
        }
    }

    /// <summary>The settled end-state of one timeline on one card image. Every value is read off the
    /// game's own non-animated arm; a property the material does not expose is skipped rather than
    /// logged, because the low-effect material variant genuinely does not carry all of them.</summary>
    private static void WriteUsedTerms(Material material, SpentLook look)
    {
        bool ghost = look == SpentLook.Spent;
        SetFloatIfPresent(material, GreyOutId, 1f);
        SetFloatIfPresent(material, FlowId, 1f);
        SetFloatIfPresent(material, DissolveId, 0.646f);          // mc_Dissolve, identical in both
        SetFloatIfPresent(material, BurnId, ghost ? 0.7f : 0.691f);
        SetFloatIfPresent(material, FlowOffsetId, 0.03f);         // mc_Flow_Offset, identical in both
        SetFloatIfPresent(material, FlowSpeedId, ghost ? 0.2f : 0.4f);
        SetFloatIfPresent(material, DissolveVerticalGradientId, 0.2f);
        if (material.HasProperty(BurnColourTintId))
        {
            material.SetColor(BurnColourTintId, ghost
                ? new Color(0.24313726f, 24f / 85f, 29f / 85f, 0.5f)
                : new Color(0.36862746f, 0.14509805f, 0.07450981f, 0.601f));
        }
        if (material.HasProperty(AnimNoiseMaskId))
        {
            float tile = ghost ? 10f : 40f;   // mc_AnimNoise_Mask_tile
            material.SetTextureScale(AnimNoiseMaskId, new Vector2(tile, tile));
        }
    }

    private static void SetFloatIfPresent(Material material, int id, float value)
    {
        if (material.HasProperty(id))
            material.SetFloat(id, value);
    }

    /// <summary>Unity-null-aware read of an Image's effective material (a Graphic with none set
    /// answers with the shared built-in UI material, which the signature test then rejects).</summary>
    private static Material? MaterialOf(UnityEngine.UI.Image? image)
    {
        try
        {
            return image != null ? image.material : null;
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private static bool IsCardFxMaterial(Material material)
    {
        try
        {
            return material.HasProperty(GreyOutId) && material.HasProperty(PosAndBoundsId);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    private static void ReportLookOnce(SpentLook look, int images)
    {
        if (s_spentLookLogged)
            return;
        s_spentLookLogged = true;
        VRLog.Info("Net", $"Remote ITEM used-look = {look} on {images} card image(s) — a peer's " +
                          "already-used item now carries the same grey-out/dissolve/burn terms and the " +
                          "same greyed card text the OWNER's board shows, rebuilt from the game's own " +
                          "settled end-state onto materials this overlay minted and destroys with the " +
                          "clone. The game's ItemCardEffects is still stripped (it cannot run on an " +
                          "inactive detached clone, and running it would write the game's pooled " +
                          "widget); the smoke emitter and the fgFx overlay quad are deliberately not " +
                          "reproduced. Zero wire traffic: the state comes from CItem.SlotState on the " +
                          "host-replicated inventory every client already holds.");
    }

    private static void ReportBoundsRefusal(SpentLook look)
    {
        if (s_spentBoundsRefused)
            return;
        s_spentBoundsRefused = true;
        VRLog.Warn("Net", $"Remote ITEM used-look ({look}) REFUSED: a card-FX material on this face " +
                          "still carries _PosAndBounds extents of 0x0, so switching its FX terms on " +
                          "would hand the shader a degenerate card footprint — the 'card renders DEEP " +
                          "BLACK' failure mode. The peer's card stays FRESH, which is a missing grey " +
                          "tint and nothing worse. If this line appears, the item card's authored " +
                          "material has changed: read the extents on ItemsPile's own hosted card (the " +
                          "same material, where ItemCardEffects.Initialize writes them) before " +
                          "loosening the gate.");
    }

    /// <summary>Best-effort: re-hand the source widget's runtime <c>_skin</c> to the clone so its
    /// activation reloads the real header art (the skin is non-serialized → not copied by
    /// Instantiate). Non-fatal: on any failure the clone keeps its copied visuals minus the header.</summary>
    private static void TryReapplySkin(FullAbilityCard source, GameObject clone)
    {
        try
        {
            var cloneFull = clone.GetComponent<FullAbilityCard>();
            AbilityCardUISkin? skin = source._skin;   // publicized runtime field
            if (cloneFull != null && skin != null)
                cloneFull._skin = skin;               // OnEnable → ShowCard() will LoadAsync the art
        }
        catch (System.Exception ex)
        {
            VRLog.Debug("Net", $"RemoteCardArt skin reapply skipped: {ex.Message}");
        }
    }

    /// <summary>Centre + fit the clone rect to the physical card size, mirroring CardFace's host pose.
    /// Runs AFTER activation so it is the final write and the widget's own OnEnable reposition can't
    /// offset the art.</summary>
    private void FitClone(GameObject clone)
    {
        if (_canvas == null)
            return;

        var canvasRect = (RectTransform)_canvas.transform;
        RectTransform? cloneRect = clone.transform as RectTransform;
        if (cloneRect == null)
            return;

        Vector2 faceSize = cloneRect.rect.size;
        if (faceSize.x <= 1f || faceSize.y <= 1f)
            faceSize = DefaultFaceSize;

        // Host canvas sized to face pixels, scaled down to the physical card size (inset a touch).
        canvasRect.sizeDelta = faceSize;
        float fit = Mathf.Min(_cardWidth / faceSize.x, _cardHeight / faceSize.y) * (1f - BorderFraction);
        canvasRect.localScale = new Vector3(fit, fit, fit);

        // The host is a direct child of the slab at unit scale, so the canvas's face pixels times the
        // fit above ARE the printed rectangle in slab-local metres — the one frame in which it is
        // comparable with the card body hanging off that same slab. This is the final pose write, so
        // it is also the first moment that number is true.
        ApplyBodyFaceHosting(faceSize * fit);

        // Centre the clone in the host canvas.
        var center = new Vector2(0.5f, 0.5f);
        cloneRect.anchorMin = center;
        cloneRect.anchorMax = center;
        cloneRect.pivot = center;
        cloneRect.anchoredPosition3D = Vector3.zero;
        cloneRect.localRotation = Quaternion.identity;
        cloneRect.localScale = Vector3.one;
        if (!cloneRect.gameObject.activeSelf)
            cloneRect.gameObject.SetActive(true);
    }

    /// <summary>
    /// THE FRONT/BACK BLEED CORRECTION (hardware report, 2026-09: a peer's ITEM card fronts read
    /// correctly in the action phase, and carried the card BACK's orange/gold diamond lattice over
    /// them as soon as that peer's board went see-through).
    ///
    /// <para>A remote card is TWO coincident surfaces. The slab's body is a MeshRenderer whose
    /// submesh 0 is the front fan plus the rim wall and whose submesh 1 is the back fan, and every
    /// remote surface deliberately gives BOTH slots the card-BACK material — no card identity is ever
    /// on the wire, so a peer's slab shows the back on both faces. This class then paints the REAL
    /// card face on a world-space canvas 0.6 mm in front of it. Opaque, that is free: the canvas is
    /// opaque, it is in front, and it wins the depth test, so the front fan behind it is never seen.
    /// <c>Net/Board/PeerBoardFade</c> then writes ONE uniform alpha to both surfaces — and alpha
    /// blending is not occlusion. At alpha a the rear surface still contributes a(1−a) of the
    /// composite, which peaks at 25 % halfway down the ramp, and what that rear surface is showing is
    /// the card back's lattice.</para>
    ///
    /// <para>It is not the fade driver's bug to fix. That driver is a correct generic per-renderer
    /// uniform-alpha writer, and it has no way to know that two of its surfaces are a front/back pair
    /// sharing 0.6 mm. This class is the one thing in the process that knows a printed face is
    /// standing in front of a card body, so the correction is made from here — by telling
    /// <c>CardMesh</c> to serve that body a mesh with the front fan dropped for as long as the print
    /// is up. <c>CardMesh.SetBodyFaceHosted</c> documents why that is a MESH swap and not a material
    /// swap (a material write mid-fade would destroy the fade's own installed clones).</para>
    ///
    /// <para>WHY THE COVERAGE IS MEASURED AND NOT ASSUMED. Dropping the front fan is invisible while
    /// opaque ONLY where the print really covers the body's front face; over an uncovered border it
    /// would read as a see-through hole, because the back fan behind it is wound away from the viewer
    /// and is back-face culled. Three of the four surfaces that host prints have already been tuned
    /// until the print and the body ARE the same rectangle — the hand fan and the pile-browse fan
    /// both squash their body to <c>CardFace.VisibleFaceRect</c>, which is the same
    /// <c>facePixels × fit × (1 − BorderFraction)</c> product computed a few lines above, and the item
    /// fan grows the box it hands us by 1/(1 − BorderFraction) so the print lands flush on the
    /// punched-out outline. The fourth, <c>RemoteHeldCardFace</c>, cuts its body and sizes its art to
    /// the SAME box, so its print sits a 6 % border inside its own slab; that surface is not a
    /// <c>PeerBoardFade</c> follower and never fades today, so it has no bleed to correct yet, and it
    /// is left with its front fan rather than with a hole. Measuring rather than listing is what makes
    /// that a property of the geometry instead of a list somebody has to remember to update.</para>
    ///
    /// <para>AND THE SECOND TERM, WHICH THE FIRST VERSION OF THIS CORRECTION DID NOT HAVE. The
    /// paragraph above says "the canvas is opaque, sits in front and wins the depth test" — true for
    /// COLOUR and false for DEPTH, and that gap is the whole of the 2026-09 hardware report "Die
    /// Geisterhand und die Characterinfo am Handgelenk ist durch die Karten hindurch sichtbar". The
    /// print is a world-space uGUI canvas; uGUI draws with ZWrite Off. So the front fan this drops
    /// was not merely a redundant surface behind an opaque one — it was the card's ONLY depth-writing
    /// front surface, and a face-hosted body stamps nothing over its own face. Every transparent
    /// surface that was being z-rejected by that stamp then comes straight through the card: the
    /// ghost hand (<c>Hands/HandGhost</c>, renderQueue 3100, ZWrite off, ZTest LEqual) and the wrist
    /// HUD (a world-space canvas, <c>unity_GUIZTestMode</c> = LEqual) both did, on all ten cards of
    /// the map-room hand — a LOCAL fan that reuses this class's printing machinery and that nothing
    /// ever fades.</para>
    ///
    /// <para>The bleed exists only while something composites the surface at an alpha below 1, and
    /// the only thing that does that to a card is <c>Net/Board/PeerBoardFade</c>. So the fan is
    /// dropped only where that can happen, asked as
    /// <c>PeerBoardFade.BelongsToAFadeSet(_slab)</c> — the driver's own registry, not a list of class
    /// names. A local card belongs to no fade set, keeps its fan, and stamps the depth it stamped
    /// before any of this existed.</para>
    /// </summary>
    private void ApplyBodyFaceHosting(Vector2 printedLocalMeters)
    {
        // Remembered so the cadenced re-evaluation below has the number that was measured at the
        // one moment it was true (the final pose write in FitClone) instead of re-deriving it from
        // a rect that may be mid-relayout.
        _printedLocalMeters = printedLocalMeters;

        // A slab with no REGISTERED card body under it — a board slot, a control-board panel, whose
        // backing is a single quad with no front fan to drop — answers false here and drops out of
        // everything below, including the log: there is no verdict to report about a surface this
        // correction does not apply to.
        if (printedLocalMeters.x <= 0f || printedLocalMeters.y <= 0f
            || !CardMesh.TryMeasureBodyFrontBox(_slab, out Vector2 measured))
            return;

        bool covers = printedLocalMeters.x >= measured.x * RequiredBodyCoverage
                      && printedLocalMeters.y >= measured.y * RequiredBodyCoverage;

        // SECOND TERM, AND IT IS THE ONE THE FIRST VERSION OF THIS CORRECTION WAS MISSING (hardware
        // report, 2026-09: "Die Geisterhand und die Characterinfo am Handgelenk ist durch die Karten
        // hindurch sichtbar"). Dropping the front fan does not only remove a surface that BLEEDS
        // when composited — it removes the card's only DEPTH-WRITING front surface. The print that
        // replaces it is a world-space uGUI canvas, and uGUI draws with ZWrite Off, so a face-hosted
        // body stamps nothing into the depth buffer over its own face. Everything that was being
        // rejected BY that stamp then comes through the card: the ghost hand (renderQueue 3100,
        // ZWrite off, ZTest LEqual — Hands/HandGhost) and the wrist HUD (a world-space canvas, whose
        // unity_GUIZTestMode is LEqual) both did, on every card of the map-room hand.
        //
        // The bleed only exists while something composites this surface at an alpha below 1, and the
        // only thing in the mod that does that to a card is PeerBoardFade. So the fan is dropped
        // only where that can happen. A LOCAL card — the scenario hand, the map-room hand, the
        // player's own item chips — belongs to no fade set, keeps its fan, and goes on stamping the
        // depth it stamped before this correction existed.
        bool canFade = PeerBoardFade.BelongsToAFadeSet(_slab);
        bool hosted = covers && canFade;

        // The MESH switch is change-gated; the VERDICT below is not gated on it. A refusal never
        // moves the switch (the body already has its front fan), so gating the log on the switch
        // would have made the refusal unreportable — the one reading that says a surface still
        // carries the bleed would never have been printed.
        if (hosted != _bodyFaceHosted && CardMesh.SetBodyFaceHosted(_slab, hosted) > 0)
            _bodyFaceHosted = hosted;
        if (covers && !canFade && !_loggedFaceHostNeverFades)
        {
            _loggedFaceHostNeverFades = true;
            // HW-VERIFY
            VRLog.Note("Net", "CARD FACE HOSTING NOT NEEDED HERE: the printed face on slab "
                + $"'{_slab.name}' does cover its card body "
                + $"({printedLocalMeters.x * 1000f:F1}x{printedLocalMeters.y * 1000f:F1} mm over "
                + $"{measured.x * 1000f:F1}x{measured.y * 1000f:F1} mm), but this slab belongs to NO "
                + "peer board's fade set, so nothing will ever composite it at an alpha below 1 and "
                + "the front/back bleed this correction exists for cannot happen on it. The front "
                + "fan is therefore KEPT — which matters, because that fan is the card's only "
                + "depth-writing front surface: the print in front of it is a uGUI canvas and uGUI "
                + "draws ZWrite Off. Keeping it is what makes this card reject the ghost hand "
                + "(renderQueue 3100, ZWrite off, ZTest LEqual) and the wrist HUD (a world-space "
                + "canvas at LEqual) standing behind it. If you can see either of those THROUGH a "
                + "card while this line is in the log, the depth stamp is being lost somewhere other "
                + "than here — read the CARD BODY DEPTH STAMP line for what the body actually is.");
        }
        else if (hosted && !_loggedFaceHosted)
        {
            _loggedFaceHosted = true;
            // HW-VERIFY
            VRLog.Note("Net", "REMOTE CARD FACE HOSTING: the printed face on slab "
                + $"'{_slab.name}' measures {printedLocalMeters.x * 1000f:F1}x{printedLocalMeters.y * 1000f:F1} mm "
                + $"against a card body of {measured.x * 1000f:F1}x{measured.y * 1000f:F1} mm, so the print "
                + "covers the body's front face and that body is now served the FACE-HOSTED mesh: same two "
                + "submeshes, same material array, same bounds, but without the front fan that was sitting "
                + "0.6 mm behind the print. That fan is why a peer's card front carried the card BACK's gold "
                + "lattice the moment their board went see-through — two coincident surfaces at one uniform "
                + "alpha COMPOSE, they do not occlude, and the rear one peaks at 25 % of the picture halfway "
                + "down the ramp. On an opaque board this changes nothing you can see, because the print was "
                + "already painting over that fan. If a peer's card front now shows a see-through RING around "
                + "its edge, the print is less flush than this measurement says and the coverage bar is what "
                + "to move. THE PRICE, STATED: the dropped fan was also this body's only depth-writing front "
                + "surface (the print is a uGUI canvas and uGUI draws ZWrite Off), so while it is hosted this "
                + "card stamps no depth over its own face and does not reject a transparent surface behind "
                + "it. That is why hosting is granted ONLY to a slab in a peer board's fade set: a peer's "
                + "board is not somewhere the viewer's own ghost hand or wrist HUD ever stands.");
        }
        else if (!covers && !_loggedFaceHostRefused)
        {
            _loggedFaceHostRefused = true;
            // HW-VERIFY
            VRLog.Note("Net", "REMOTE CARD FACE HOSTING REFUSED: the printed face on slab "
                + $"'{_slab.name}' measures {printedLocalMeters.x * 1000f:F1}x{printedLocalMeters.y * 1000f:F1} mm "
                + $"but its card body is {measured.x * 1000f:F1}x{measured.y * 1000f:F1} mm, so the print does "
                + "NOT cover the body's front face and the front fan behind it is being KEPT. That is the safe "
                + "answer, not a failure: dropping the fan would leave the uncovered border see-through, "
                + "because the back fan behind it is wound away from you and is culled. The cost is that IF "
                + "this surface ever sits on a see-through peer board, its card front will still carry the "
                + "card back's lattice. The known surface that lands here is the peer's HELD card, which cuts "
                + "its body and sizes its art to the same box and so prints 6 % inside its own slab — and "
                + "which does not follow the board fade today, so nothing is wrong on screen. The remedy, if "
                + "it ever does follow it, is the one the item fan already ships: hand the art a box grown by "
                + "1/(1 - 6 %).");
        }
    }

    /// <summary>
    /// Re-ask the face-hosting verdict on a slow cadence while a print is up.
    ///
    /// <para>WHY THE VERDICT CANNOT BE A ONE-SHOT. It used to be decided exactly once, in
    /// <see cref="FitClone"/>, because its only term was the printed COVERAGE and that cannot change
    /// while the same print is up. Its second term can: a peer fan registers itself with
    /// <c>PeerBoardFade.Follow</c> when it builds its root, and while every fan in the codebase does
    /// that before it prints its first face, nothing in the type system says it must. A one-shot
    /// asked in the other order would refuse hosting for the life of that print and silently hand
    /// back the front/back bleed on a peer's faded board — a defect that only shows on somebody
    /// else's machine, mid-ramp. One re-ask a second closes that seam for three field compares.</para>
    ///
    /// <para>It costs nothing to be wrong about the cadence: the mesh switch inside
    /// <see cref="ApplyBodyFaceHosting"/> is change-gated, so a verdict that has not moved writes
    /// nothing at all.</para>
    /// </summary>
    private void MaintainBodyFaceHosting()
    {
        if (_printedLocalMeters.x <= 0f || _printedLocalMeters.y <= 0f)
            return;
        float now = Time.unscaledTime;
        if (now < _nextFaceHostRecheck)
            return;
        _nextFaceHostRecheck = now + FaceHostRecheckInterval;
        ApplyBodyFaceHosting(_printedLocalMeters);
    }

    /// <summary>Give the body its front fan back. Idempotent and free when nothing was ever taken:
    /// the switch can only be true if <see cref="ApplyBodyFaceHosting"/> found a real card body under
    /// this slab and measured a print that covered it.</summary>
    private void ReleaseBodyFaceHosting()
    {
        // No print in front of the body any more, so there is no printed rectangle to re-judge and
        // MaintainBodyFaceHosting must stand down with it — otherwise a hidden front would go on
        // asking a verdict about a measurement that stopped being true.
        _printedLocalMeters = Vector2.zero;
        if (!_bodyFaceHosted)
            return;
        CardMesh.SetBodyFaceHosted(_slab, false);
        _bodyFaceHosted = false;
    }

    private void DestroyClone()
    {
        if (_clone != null)
        {
            Object.Destroy(_clone);
            _clone = null;
        }
        // …and the materials minted for it (see ApplySpentLook). Destroying a GameObject does NOT
        // destroy the materials its Images point at, and these have no other owner — the clone they
        // were assigned to is on its way out in the same frame.
        for (int i = 0; i < _ownedMaterials.Count; i++)
        {
            if (_ownedMaterials[i] != null)
                Object.Destroy(_ownedMaterials[i]);
        }
        _ownedMaterials.Clear();
        _artWatch.Clear(); // the watched Images belong to the clone that just died
        _shownSourceId = int.MinValue;
    }
}
