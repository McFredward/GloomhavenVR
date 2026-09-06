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

    /// <summary>Small inset so the art sits just inside the box it is handed (mirrors
    /// <c>CardFace.BorderFraction</c>): a cloned face is fitted to <c>(1 - BorderFraction)</c> of
    /// that box.
    ///
    /// <para>INTERNAL, because two surfaces have to undo it and a private copy is how they drift.
    /// A caller whose BODY is the card's real outline (an item chip, whose box comes from the Item
    /// footprint) grows the box it hands us by <c>1/(1 - BorderFraction)</c> so the print lands
    /// flush on that outline; a caller whose body it OWNS (an ability slab) shrinks the body to
    /// <c>CardFace.VisibleFaceRect</c> instead, which is the same product seen from the other end.
    /// Either way the number belongs to this class, which is the one that applies it.</para>
    /// </summary>
    internal const float BorderFraction = 0.06f;

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

    /// <summary>The card body's front box in SLAB-LOCAL metres, from the last measurement. Held
    /// beside <see cref="_printedLocalMeters"/> only so the per-frame verdict and its log can quote
    /// both numbers without re-measuring a mesh that has not changed.</summary>
    private Vector2 _bodyFrontBox;

    /// <summary>Does the print cover the body's front face? The SLOW term of the face-hosting
    /// verdict: it can only change when a new print is fitted, so it is measured on the cadence and
    /// cached for the per-frame re-ask.</summary>
    private bool _bodyCovers;

    /// <summary>The <c>PeerBoardFade</c> driver that composites this slab, or null for a local
    /// surface (and for a peer surface whose board has not been built yet). Resolving it is the
    /// expensive half of the verdict — a parent walk plus a scan of the follower registry — and it
    /// can only change on events the 1 s cadence already exists for, so it is cached here and only
    /// its live <c>CompositingBelowSolid</c> state is read per frame.</summary>
    private PeerBoardFade? _fadeDriver;

    /// <summary>The REGISTERED follower root this slab hangs under, or null when it is parented
    /// beneath the peer board itself. Held beside <see cref="_fadeDriver"/> because a follower's
    /// membership is conditional — a <c>WhileOverBoard</c> hand fan is in the fade set only while
    /// its owner holds it over the board — and the per-frame verdict has to ask
    /// <c>PeerBoardFade.IsFollowing</c> about THIS root rather than assume the driver composites
    /// everything it has ever been handed.</summary>
    private Transform? _fadeFollowerRoot;

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
    /// Last SEE-THROUGH/SOLID state the MIRRORED SURFACE DEPTH line reported, PER PEER BOARD (keyed
    /// by the driver's instance id).
    ///
    /// <para>The line writes on the EDGE rather than once per session, because the whole point of it
    /// is that a peer's card must END every fade episode back in the SOLID, depth-stamping state — a
    /// one-shot could only ever prove it entered one of the two. Keyed per BOARD and not held in a
    /// single static bool because two peers whose boards are in OPPOSITE states would make one
    /// shared latch flip on every card, every frame: a two-value cap over a two-population reading
    /// is exactly the flood the "a cap that goes silent" note is about. Per board it is two lines
    /// per fade episode, no matter how many cards ride it.</para>
    /// </summary>
    private static readonly Dictionary<int, bool> MirrorDepthReported = new(4);

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
    /// Is the SLAB this overlay hangs on being drawn at all? A front printed onto a deactivated
    /// host is on nobody's screen, and the point of asking is the CENSUS: a caller that counts it
    /// as a front makes the one instrument that measures the 1:1 face rule report a card the player
    /// cannot see. <c>RemoteItemFan</c> deactivates the slab of a chip the owner has in their fist
    /// (2026-09-06 report item 5), which is the first surface for which this is not always true.
    /// </summary>
    public bool HostDrawn => _slab != null && _slab.gameObject.activeInHierarchy;

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
            ItemFxRig itemFx = StripFragileEffects(clone, out _burnHeaderText, out _burnInitiativeText,
                                                  out _flameBurnTexture, out _flameGhostTexture);
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

    /// <summary>
    /// THE RENDERER STACK AT THIS ONE CARD SEAT, in words — the measurement the 2026-09-06 report's
    /// items 6 and 7a turn on ("Abgeworfene Karten sieht man teilweise noch die Rückseite … durch-
    /// scheinen", "mit diesem Rückseiten Raster darauf drübergelegt").
    ///
    /// <para>WHY IT LIVES HERE AND NOWHERE ELSE. This class IS the thing that knows a peer's card is
    /// two coincident surfaces: an opaque <c>MeshRenderer</c> whose two submeshes BOTH wear
    /// <c>CardMesh</c>'s procedural card BACK — a gold diamond lattice, 8 cells across a card, on a
    /// deep burgundy field — and a world-space uGUI print standing
    /// <see cref="FrontStandoff"/> in front of it. Three surfaces host that pair (the hand fan, the
    /// two pile arcs, the held card) and a copy of this walk in each of them is exactly how the
    /// three would come to answer the same question differently. One implementation, three callers.
    /// </para>
    ///
    /// <para>WHAT IT DECIDES. The lattice can only reach the player's eye if the print in front of
    /// it is not opaque, so the line separates the three ways that can happen, by number:
    /// a CanvasGroup product below 1.000 (something is compositing the whole print — the peer board
    /// fade, or a group the game left at 0 while an addressable load was in flight); a Graphic count
    /// whose DRAWING half is far short of its total (the print's Images are not painting at all, and
    /// only its TMP text is); or neither, in which case the print is opaque and whatever is painting
    /// the lattice is NOT this seat. It states the mesh's material names and render queues beside
    /// them so a draw-ORDER cause is visible in the same line rather than inferred from its absence.
    /// </para>
    ///
    /// <para>Never throws and never allocates on a steady frame: every caller is change-gated, and a
    /// diagnostic may never be the thing that breaks the render path it is describing.</para>
    /// </summary>
    internal string DescribeStack()
    {
        try
        {
            if (_slab == null)
                return "SEAT STACK: no slab.";
            var sb = new System.Text.StringBuilder(320);
            var meshes = _slab.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            sb.Append("SEAT STACK ('").Append(_slab.name).Append("'): ").Append(meshes.Length)
              .Append(" MeshRenderer(s) wearing [");
            int slot = 0;
            for (int i = 0; i < meshes.Length; i++)
            {
                if (meshes[i] == null)
                    continue;
                Material[] mats = meshes[i].sharedMaterials;
                for (int j = 0; j < mats.Length; j++)
                {
                    if (slot++ > 0)
                        sb.Append(", ");
                    sb.Append(mats[j] != null ? mats[j].name : "(null)")
                      .Append(" q").Append(mats[j] != null ? mats[j].renderQueue : -1);
                }
            }
            sb.Append(']');

            int graphics = 0;
            int drawing = 0;
            int zeroAlpha = 0;
            float groupAlpha = 1f;
            if (_host != null)
            {
                var gs = _host.GetComponentsInChildren<UnityEngine.UI.Graphic>(includeInactive: true);
                graphics = gs.Length;
                for (int i = 0; i < gs.Length; i++)
                {
                    UnityEngine.UI.Graphic g = gs[i];
                    if (g == null || !g.isActiveAndEnabled)
                        continue;
                    if (g.color.a <= 0.004f)
                        zeroAlpha++;
                    else
                        drawing++;
                }
                var groups = _host.GetComponentsInChildren<CanvasGroup>(includeInactive: true);
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i] != null && groups[i].isActiveAndEnabled)
                        groupAlpha *= Mathf.Clamp01(groups[i].alpha);
                }
            }
            sb.Append(" + a uGUI print of ").Append(graphics).Append(" Graphic(s) — ")
              .Append(drawing).Append(" drawing, ").Append(zeroAlpha)
              .Append(" at colour alpha 0, the rest inactive — under a CanvasGroup product of ")
              .Append(groupAlpha.ToString("F3"))
              .Append(", face-hosted=").Append(_bodyFaceHosted ? "YES" : "no")
              .Append(". TWO SURFACES AT ONE SEAT IS THE OVERLAY: the mesh above wears the card BACK "
                    + "on BOTH submeshes and the print stands 0.6 mm in front of it, so anything "
                    + "that stops the print being opaque puts the card back's gold lattice on a "
                    + "peer's card FRONT. A product below 1.000, or 'drawing' far short of the "
                    + "Graphic total, IS the reported defect and names which of the two it is; "
                    + "1.000 with nearly every Graphic drawing means the print is opaque and the "
                    + "lattice is coming from somewhere other than this seat.");
            return sb.ToString();
        }
        catch (System.Exception ex)
        {
            return $"SEAT STACK unreadable ({ex.GetType().Name}).";
        }
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
    private static ItemFxRig StripFragileEffects(GameObject clone,
        out TMPro.TextMeshProUGUI? header, out TMPro.TextMeshProUGUI? initiative,
        out Texture? flameBurn, out Texture? flameGhost)
    {
        UnityEngine.UI.Image[]? images = null;
        TMPro.TextMeshProUGUI[]? texts = null;
        header = null;
        initiative = null;
        flameBurn = null;
        flameGhost = null;
        try
        {
            var effects = clone.GetComponentsInChildren<CardEffects>(includeInactive: true);
            for (int i = 0; i < effects.Length; i++)
            {
                if (effects[i] == null)
                    continue;
                // THE ABILITY CARD'S TWO NAMED TEXTS, lifted in the same instant and for the same
                // reason the item rig above lifts imgComp/txtComp: after DestroyImmediate there is
                // nothing left to ask, and asking the SOURCE would name the game's objects instead
                // of ours. First component wins — a card carries exactly one CardEffects.
                if (header == null)
                    LiftBurnTexts(effects[i], ref header, ref initiative);
                // …AND THE FLAME SHEET'S TWO TEXTURES, in the same instant and for the same reason.
                // `overlayFrameBurn` and `overlayFrameGhost` are PUBLIC serialized fields
                // (CardEffects.cs:221-223), so Object.Instantiate carries them onto the clone and this
                // is a read of the clone's own state — no reflection is owed for these two. They are
                // what CardEffects pushes into the fgFx overlay's `_ParticleTexture`
                // (CardEffects.cs:554 / :668) and they are the picture the user calls the fire.
                if (flameBurn == null)
                {
                    flameBurn = effects[i].overlayFrameBurn;
                    flameGhost = effects[i].overlayFrameGhost;
                }
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

    /// <summary>
    /// Read <c>CardEffects._header</c> and <c>CardEffects._initiativeText</c> off the CLONE's own
    /// component. Both are <c>[SerializeField] private</c> (CardEffects.cs:57-75), so
    /// <c>Object.Instantiate</c> copies them and this is a read of the clone's own state — unlike
    /// <c>imgComp</c>/<c>txtComp</c>, which <c>Initialize()</c> builds at runtime and a clone
    /// therefore never has.
    ///
    /// <para>Reflection, and only here: the fields are private, the handles are resolved ONCE per
    /// process, and a rename in a game patch lands on null rather than on a throw — the burnt
    /// header then falls back to the same mid grey every other text takes, which is the picture
    /// every build before this one drew.</para>
    /// </summary>
    private static void LiftBurnTexts(CardEffects effects,
        ref TMPro.TextMeshProUGUI? header, ref TMPro.TextMeshProUGUI? initiative)
    {
        if (!s_effectFieldsResolved)
        {
            s_effectFieldsResolved = true;
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            s_headerField = typeof(CardEffects).GetField("_header", flags);
            s_initiativeField = typeof(CardEffects).GetField("_initiativeText", flags);
        }
        header = s_headerField?.GetValue(effects) as TMPro.TextMeshProUGUI;
        initiative = s_initiativeField?.GetValue(effects) as TMPro.TextMeshProUGUI;
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

    // ─── THE FLAME SHEET'S NINE PROPERTIES (2026-09-06 report item 4) ────────────────────────────
    // The burn has TWO face-wide halves and only one of them was ever mirrored. The FACE half is the
    // material sweep above. The FIRE half is a SECOND Image — CardEffects' serialized `_uiFxOverlay`,
    // aliased `fgFx`, live GameObject 'UIFX_Overlay', a sprite-less quad drawn over ~120 % of the
    // card — carrying its own shader with its own nine properties, of which `_FXAnim` is the only
    // ANIMATED one. That is what the user means by "die Feuer animation": an orange sheet
    // (_TintColor 1, 0.3, 0, 0.8) textured with `overlayFrameBurn` at _Glow 3, ramped in over the
    // first HALF SECOND and then held for the remaining 1.5 s of the 2 s timeline.
    //
    // ITS RATE IS NOT THE FACE HALF'S, and that is the one term a "same timeline" assumption would
    // get wrong: CardEffects.cs:581 writes `Mathf.Clamp(dTime * 4f, 0f, 1f) / 2f`, i.e. FOUR TIMES
    // the face rate, capped, halved — 0 → 0.5 reached at t = 0.25 (0.5 s) and constant after. The
    // fire flashes up and sits; the char keeps darkening underneath it.
    private static readonly int FxAnimId = Shader.PropertyToID("_FXAnim");
    private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    private static readonly int ParticleTextureId = Shader.PropertyToID("_ParticleTexture");
    private static readonly int OverlayFlowSpeedId = Shader.PropertyToID("_FlowSpeed");
    private static readonly int OffsetStrengthId = Shader.PropertyToID("_OffsetStrength");
    private static readonly int ThinHighlightsId = Shader.PropertyToID("_ThinHighlights");
    private static readonly int ThinHighlightMinId = Shader.PropertyToID("_ThinHighlight_Min");
    private static readonly int ThinHighlightMaxId = Shader.PropertyToID("_ThinHighlight_Max");
    private static readonly int FlowNoiseTilingId = Shader.PropertyToID("_Flow_NoiseTiling");
    private static readonly int OverlayGlowId = Shader.PropertyToID("_Glow");

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
    /// dropped only while that IS happening, asked as
    /// <c>PeerBoardFade.DriverFor(_slab)?.CompositingBelowSolid</c> — the driver's own registry and
    /// the driver's own live state, not a list of class names. A local card has no driver, keeps its
    /// fan, and stamps the depth it stamped before any of this existed — AND SO DOES A PEER'S CARD
    /// ON A SOLID BOARD, which is what item 7 of the 2026-09-05 report asked for.</para>
    ///
    /// <para>WHAT CHANGED IN ModBuild 449, AND WHY 446'S ANSWER WAS ONE STEP SHORT. 446 fed the
    /// switch a CAPABILITY — <c>PeerBoardFade.BelongsToAFadeSet</c>, "can this surface ever fade?" —
    /// and stated its price openly: "a peer's printed card does not stamp depth while their board is
    /// still solid, which is a surface nowhere near the viewer's own hands". The second half of that
    /// sentence is the assumption that failed. In room-scale VR the viewer walks up to a peer's
    /// board, reaches across it and holds their own card over it; the hardware screenshots
    /// remote-fächer-tiefenproblem1..3 show the viewer's ghost hand and forearm drawn straight
    /// THROUGH a peer's item fan, a peer's hand fan and a card held over a peer's board — the exact
    /// picture 446 removed from the map-room hand, still standing on every mirrored surface. A
    /// capability test is not a policy. The switch now reads the live composite state, so a peer's
    /// card is face-hosted for the ~0.6 s its board is actually see-through and carries the same
    /// depth-stamping front fan a local card carries for all the rest of the time.</para>
    /// </summary>
    private void ApplyBodyFaceHosting(Vector2 printedLocalMeters)
    {
        // Remembered so the cadenced re-evaluation below has the number that was measured at the
        // one moment it was true (the final pose write in FitClone) instead of re-deriving it from
        // a rect that may be mid-relayout.
        _printedLocalMeters = printedLocalMeters;
        _bodyCovers = false;
        _fadeDriver = null;
        _fadeFollowerRoot = null;

        // A slab with no REGISTERED card body under it — a board slot, a control-board panel, whose
        // backing is a single quad with no front fan to drop — answers false here and drops out of
        // everything below, including the log: there is no verdict to report about a surface this
        // correction does not apply to.
        if (printedLocalMeters.x <= 0f || printedLocalMeters.y <= 0f
            || !CardMesh.TryMeasureBodyFrontBox(_slab, out Vector2 measured))
            return;

        _bodyCovers = printedLocalMeters.x >= measured.x * RequiredBodyCoverage
                      && printedLocalMeters.y >= measured.y * RequiredBodyCoverage;
        _bodyFrontBox = measured;

        // SECOND TERM, AND IT IS THE ONE THE FIRST VERSION OF THIS CORRECTION WAS MISSING (hardware
        // report, 2026-09: "Die Geisterhand und die Characterinfo am Handgelenk ist durch die Karten
        // hindurch sichtbar"). Dropping the front fan does not only remove a surface that BLEEDS
        // when composited — it removes the card's only DEPTH-WRITING front surface. The print that
        // replaces it is a world-space uGUI canvas, and uGUI draws with ZWrite Off, so a face-hosted
        // body stamps nothing into the depth buffer over its own face. Everything that was being
        // rejected BY that stamp then comes through the card: the ghost hand (renderQueue 3100,
        // ZWrite off, ZTest LEqual — Hands/HandGhost) and the wrist HUD (a world-space canvas, whose
        // unity_GUIZTestMode is LEqual).
        //
        // THE DRIVER IS RESOLVED HERE, ON THE SLOW CADENCE, AND ITS STATE IS READ PER FRAME BELOW.
        // Resolution is the expensive half (a parent walk plus a scan of the follower registry) and
        // it can only change when a fan registers with PeerBoardFade.Follow or a peer's board is
        // (re)built — the same rare events the 1 s re-ask already exists for. The live alpha is the
        // cheap half and has to be read at the frame rate, because a 0.6 s ramp read at 1 Hz would
        // hand the bleed back for most of it.
        _fadeDriver = PeerBoardFade.DriverFor(_slab, out _fadeFollowerRoot);
        EvaluateBodyFaceHosting();
    }

    /// <summary>
    /// The cheap half of the face-hosting verdict: one reference compare and one bool read against
    /// terms <see cref="ApplyBodyFaceHosting"/> has already measured. Safe to call every frame, and
    /// it must be — the term that moves is a peer board's LIVE composite state.
    /// </summary>
    private void EvaluateBodyFaceHosting()
    {
        Vector2 printedLocalMeters = _printedLocalMeters;
        Vector2 measured = _bodyFrontBox;
        bool covers = _bodyCovers;
        // A driver that was destroyed under us compares equal to null through Unity's operator, and
        // the next cadenced re-resolve will pick up its replacement. Until then: no driver means
        // nothing composites this slab, which is the SAFE answer — keep the fan, stamp the depth.
        bool canFade = _fadeDriver != null;
        // BOTH TERMS, AND THE SECOND ONE IS NOT REDUNDANT. A WhileOverBoard follower — the peer's
        // hand fan and its placard — is REGISTERED for the whole session but composited only while
        // its owner holds it over the board. Asking only "is the driver below solid" would drop the
        // front fan of a fully opaque hand fan held away from a fading board: a fresh copy of the
        // defect this change removes, on the one surface the report named twice. The membership term
        // is only reached while the board is actually fading, so it costs nothing in the steady
        // state; a slab parented under the board root itself has no follower root and is
        // unconditionally in the census.
        bool seeThrough = canFade && _fadeDriver!.CompositingBelowSolid
                          && (_fadeFollowerRoot == null || _fadeDriver.IsFollowing(_fadeFollowerRoot));
        bool hosted = covers && seeThrough;

        // The MESH switch is change-gated; the VERDICT below is not gated on it. A refusal never
        // moves the switch (the body already has its front fan), so gating the log on the switch
        // would have made the refusal unreportable — the one reading that says a surface still
        // carries the bleed would never have been printed.
        if (hosted != _bodyFaceHosted && CardMesh.SetBodyFaceHosted(_slab, hosted) > 0)
            _bodyFaceHosted = hosted;

        // THE MIRRORED-SURFACE READING (item 7 of the 2026-09-05 report). One token for every
        // mirrored surface whose depth the report is about, printed once per VERDICT CLASS so a fan
        // of twelve chips writes one line and a class that never occurs writes none. It states the
        // three terms that decide whether this card occludes the viewer's own hand, in the same
        // words the LOCAL card's CARD BODY DEPTH STAMP states them, so the two can be compared in
        // one grep next round.
        int driverKey = canFade ? _fadeDriver!.GetInstanceID() : 0;
        if (covers && canFade
            && (!MirrorDepthReported.TryGetValue(driverKey, out bool wasSeeThrough)
                || wasSeeThrough != seeThrough))
        {
            MirrorDepthReported[driverKey] = seeThrough;
            // HW-VERIFY
            VRLog.Note("Net", "MIRRORED SURFACE DEPTH: peer fan card on slab "
                + $"'{_slab.name}' — its peer board is "
                + (seeThrough ? "SEE-THROUGH" : "SOLID")
                + $", so the printed face ({printedLocalMeters.x * 1000f:F1}x"
                + $"{printedLocalMeters.y * 1000f:F1} mm over a body of {measured.x * 1000f:F1}x"
                + $"{measured.y * 1000f:F1} mm) is "
                + (seeThrough
                    ? "FACE-HOSTED: the front fan is dropped, this card STAMPS NO DEPTH over its own "
                      + "face, and the viewer's ghost hand (renderQueue 3100, ZWrite off, ZTest "
                      + "LEqual) and wrist HUD (a world-space canvas at LEqual) are free to draw "
                      + "through it. Correct while the board is see-through — a transparent card "
                      + "must not occlude — and it is the state the card must LEAVE the moment the "
                      + "ramp finishes."
                    : "carrying its FRONT FAN: this card STAMPS DEPTH over its own face exactly like "
                      + "the viewer's own hand cards do, and rejects the ghost hand and the wrist "
                      + "HUD standing behind it. THIS IS THE FIX for item 7 (remote-fächer-"
                      + "tiefenproblem1..3): until ModBuild 448 a peer's card was face-hosted for as "
                      + "long as it BELONGED to a fade set, i.e. for ever, so it never stamped depth "
                      + "at all.")
                + " FALSIFIER: with no peer board on screen this line does not appear at all — a "
                + "local card resolves NO driver and takes the CARD FACE HOSTING NOT NEEDED HERE "
                + "branch instead, so silence here means 'no peer fan was printed', never 'the fix "
                + "worked'. Read it against ] [Cards] CARD BODY DEPTH STAMP, which must show 85 "
                + "FRONT FAN triangles (259-triangle body) or 8 (28-triangle chip) whenever the "
                + "SOLID half of this line is the last one printed.");
        }

        if (covers && !canFade && !_loggedFaceHostNeverFades)
        {
            _loggedFaceHostNeverFades = true;
            // HW-VERIFY
            VRLog.Note("Net", "CARD FACE HOSTING NOT NEEDED HERE: the printed face on slab "
                + $"'{_slab.name}' does cover its card body "
                + $"({printedLocalMeters.x * 1000f:F1}x{printedLocalMeters.y * 1000f:F1} mm over "
                + $"{measured.x * 1000f:F1}x{measured.y * 1000f:F1} mm), but no PeerBoardFade driver "
                + "composites this slab, so nothing will ever composite it at an alpha below 1 and "
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
                + "it. THAT PRICE IS NOW PAID ONLY WHILE IT BUYS SOMETHING (ModBuild 449): hosting is "
                + "granted while the owning board is ACTUALLY compositing below solid, not for as long as "
                + "the slab BELONGS to a fade set. 446 made it a membership test on the premise that a "
                + "peer's board 'is nowhere near the viewer's own hands' — and the 2026-09-05 screenshots "
                + "remote-fächer-tiefenproblem1..3 show the viewer's ghost hand and forearm straight "
                + "through a peer's item fan, hand fan and held card. A see-through card must not occlude; "
                + "a solid one must, and now does.");
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
    ///
    /// <para>THE CADENCE IS NOW SPLIT, AND THE FAST HALF IS THE WHOLE OF ITEM 7 (ModBuild 449). The
    /// verdict's second term stopped being a membership — "can this ever fade?" — and became the
    /// owning board's LIVE composite state, which moves over a ~0.6 s ramp. Re-asked once a second
    /// it would hand a peer's card its depth stamp back somewhere in the middle of the next episode
    /// instead of at its end, i.e. it would ship the fix with a lag long enough to be the defect
    /// again. So <see cref="EvaluateBodyFaceHosting"/> — one reference compare, one bool read and a
    /// change-gated mesh assignment against terms already measured — runs on EVERY call, and only
    /// the two expensive terms (measuring the print against the body, resolving the driver) stay on
    /// the 1 s cadence.</para>
    /// </summary>
    private void MaintainBodyFaceHosting()
    {
        if (_printedLocalMeters.x <= 0f || _printedLocalMeters.y <= 0f)
            return;
        float now = Time.unscaledTime;
        if (now < _nextFaceHostRecheck)
        {
            EvaluateBodyFaceHosting();
            return;
        }
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
        _bodyCovers = false;
        _fadeDriver = null;
        _fadeFollowerRoot = null;
        if (!_bodyFaceHosted)
            return;
        CardMesh.SetBodyFaceHosted(_slab, false);
        _bodyFaceHosted = false;
    }

    // ───────────────────────────── the BURN, played on a peer's card in step with its owner ─────
    //
    // WHY THIS IS A RAMP AND NOT A REPLAY, and why re-attempting the replay is forbidden: see
    // ApplySpentLook's block above. Calling the game's own CardEffects.ToggleEffect on a clone is
    // dead three times over (the strip destroys the component; a borrowed widget never ran
    // Initialize so imgCount is 0 and the timeline would write the game's SHARED authored material;
    // and the clone's Images point at the SOURCE widget's materials by reference). Every write
    // below lands on a `new Material` this overlay minted and destroys with the clone, exactly as
    // ApplySpentLook does.
    //
    // WHAT IS DIFFERENT FROM ApplySpentLook, and it is the one thing that makes an ANIMATION
    // legitimate here. That method deliberately writes only the SETTLED end-state, on the argument
    // that "a peer's card can appear on our board long after the owner's timeline ran and a fade
    // starting THEN would be a picture the owner never had". That argument does not reach a BURN:
    // the trigger is the card ENTERING the owner's LostAbilityCards list, which is host-replicated
    // and lands on every client at the same instant it lands on the owner's - the same instant the
    // owner's own CardsDriver.TickBurnToPile starts its hold. So the two clocks are the game's, not
    // ours, and the ramp is in step by construction.
    //
    // AND THERE ARE TWO OF THEM, not one (2026-09-06 follow-up, "nicht nur die Verkohlung bei
    // verbrennen sondern auch das Ausgrauen wenn eine nicht-verbrennen Aktion benutzt wurde").
    // FullAbilityCard.TryPlayBurnAnimation (FullAbilityCard.cs:577) picks between exactly two
    // CardEffects timelines by the CardPile of the action that was used: BurnCardTimeline for a
    // LOST action that actually resolved, GhostOutOnTimeline for everything else. The two are the
    // SAME machine — same three animated terms, same 2 s, same _Dissolve ceiling of 0.646 — and
    // differ only in six constants, of which the one that matters is _Burn_ColourTint: a WARM
    // brown (0.369, 0.145, 0.075) for the char and a COLD blue-grey (0.243, 0.282, 0.341) for the
    // ghost. So this rig carries a LOOK rather than a single constant set, and "verkohlt" and
    // "ausgegraut" are one implementation with two tables — which is also why neither is an
    // approximation of the other.
    //
    // THE NUMBERS ARE THE GAME'S OWN, term for term out of CardEffects.BurnCardTimeline
    // (CardEffects.cs:508-618) and CardEffects.GhostOutOnTimeline (:621-730): the constants are set
    // once at t=0 and the three animated terms are
    // _GreyOut = t, _Flow = t, _Dissolve = lerp(0, 0.646, t) over burnTime = 2 s. The fgFx overlay
    // quad (the orange flame sheet) is deliberately NOT reproduced, for the reason ApplySpentLook
    // states: it is a second unmeasured material driven by nine more properties drawn over the
    // WHOLE card, and a wrong write there is a full-card artefact. Cost, stated: the peer sees the
    // card char, grey out and dissolve on the owner's clock, without the flame sheen on top.

    /// <summary>
    /// WHICH of the game's two card-FX timelines a face is wearing — the same choice
    /// <c>FullAbilityCard.TryPlayBurnAnimation</c> makes, by the same rule.
    /// </summary>
    internal enum CardFxLook
    {
        /// <summary>Nothing written. A card nobody has used yet.</summary>
        None,

        /// <summary><c>CardEffects.BurnCardTimeline</c> — the WARM char. The owner gets it when the
        /// action he used has <c>CardPile == Lost / PermanentlyLost</c> AND the action actually
        /// resolved (<c>CBaseCard.ActionHasHappened</c>).</summary>
        Burn,

        /// <summary><c>CardEffects.GhostOutOnTimeline</c> — the COLD grey-out, the game's
        /// <c>FXTask.DiscardMode</c>. Every other used action takes this one, which is the whole of
        /// the user's "das Ausgrauen wenn eine nicht-verbrennen Aktion benutzt wurde".</summary>
        Ghost,
    }

    /// <summary>
    /// WHICH OF THE MOD'S CARD-FX SURFACES this face belongs to — named by whoever DRIVES the look,
    /// because driving it is what makes a surface exist for the instrument below.
    ///
    /// <para>IT EXISTS BECAUSE A ONCE-PER-PROCESS LATCH CANNOT ANSWER THE QUESTION. The rig is one
    /// implementation with several callers, and the defect shape this project has already shipped
    /// once (2026-09-05 item 9b, the burnt pile fan that nobody had asked for the look) is a term
    /// that arms on ONE surface and refuses on ANOTHER. The armed/refused line was latched on a
    /// single static bool, so only the FIRST surface to build a rig ever printed and every other
    /// one was invisible. Latching per surface is what makes "fire armed on the recess, refused on
    /// the pile" readable at a glance instead of being a silence.</para>
    ///
    /// <para>Bounded by construction: four surfaces times three outcomes is at most twelve lines for
    /// the life of the process, never per card and never per frame.</para>
    /// </summary>
    internal enum FxSurface
    {
        /// <summary>Nobody has driven a card-FX look on this face. The ACTIVE-CARDS column sits here
        /// permanently and that is the correct reading for it — an activated card is not burnt, so
        /// no look is driven, so no rig is built and no line is printed.</summary>
        Unnamed,

        /// <summary>A peer's round-card RECESS — <c>RemoteBoardCard.DriveUsedCardFx</c>, a live 2 s
        /// ramp off the owner's own effect state.</summary>
        Recess,

        /// <summary>The burn FLIGHT slab — <c>RemoteBurnFx</c>, the same 2 s ramp during the hold and
        /// the settled look for the arc.</summary>
        Flight,

        /// <summary>The opened BURNT PILE fan — <c>RemotePileFronts</c>, the settled end-state at
        /// t = 1 and deliberately never a ramp.</summary>
        Pile,
    }

    /// <summary>Which surface is driving this face. Assigned by the driver, idempotently; a face
    /// nobody drives keeps <see cref="FxSurface.Unnamed"/> and never reaches the instrument.</summary>
    internal FxSurface Surface { get; set; } = FxSurface.Unnamed;

    /// <summary>How far the card-FX rig has got. Built ONCE per clone: the walk over the clone's
    /// Images and the material minting must not repeat per frame.</summary>
    private enum BurnRig { Unbuilt, Ready, Refused }

    private BurnRig _burnRigState = BurnRig.Unbuilt;
    private UnityEngine.UI.Image[]? _burnImages;
    private TMPro.TextMeshProUGUI[]? _burnTexts;

    /// <summary>The clone's own header title, lifted off its <c>CardEffects</c> before the strip.
    /// Null when the game's field name has moved, which costs the header its burnt tint and
    /// nothing else.</summary>
    private TMPro.TextMeshProUGUI? _burnHeaderText;

    /// <summary>The clone's own initiative disc, same seam and same fallback as
    /// <see cref="_burnHeaderText"/>.</summary>
    private TMPro.TextMeshProUGUI? _burnInitiativeText;

    /// <summary>The colour every watched text had when the rig was built — the clone's copy of the
    /// game's own <c>txtColourStore</c>, which <c>GhostOutOnTimeline</c> lerps FROM
    /// (CardEffects.cs:703). Index-aligned with <see cref="_burnTexts"/>. Captured once, because a
    /// per-frame ramp that read the live colour would lerp from its own previous output and settle
    /// early.</summary>
    private Color[]? _burnTextColours;

    /// <summary>Which look the minted materials currently carry. A change rewrites the constant
    /// half on the copies this overlay already owns — the walk and the minting never repeat.</summary>
    private CardFxLook _burnRigLook = CardFxLook.None;

    /// <summary>The game's own <c>CardEffects.burntTextColor</c> (CardEffects.cs:225), a
    /// <c>Color32(143, 58, 44, 255)</c> field initialiser and therefore a BUILD FACT of the game,
    /// not a dial anybody tunes. It is the colour the owner's burnt card title is drawn in and the
    /// one this overlay owed the peer's.</summary>
    private static readonly Color BurntTextColor = new Color32(143, 58, 44, byte.MaxValue);

    /// <summary>Cached reflection handles for the two SERIALIZED privates of <c>CardEffects</c> the
    /// text half needs to name. Looked up once per process, never per clone; a miss leaves both
    /// null and the look degrades to uniform grey.</summary>
    private static System.Reflection.FieldInfo? s_headerField;
    private static System.Reflection.FieldInfo? s_initiativeField;
    private static bool s_effectFieldsResolved;

    /// <summary>The clone's own flame sheet — <c>CardEffects._uiFxOverlay</c> ('UIFX_Overlay'),
    /// found by MATERIAL SIGNATURE rather than by name, wearing a material this overlay minted.
    /// Null when the face carries no such graphic or when the sheet was REFUSED, in which case
    /// <see cref="_flameRefusal"/> names the term that refused it.</summary>
    private UnityEngine.UI.Image? _flameQuad;

    /// <summary>Why the flame sheet is not being drawn, in one phrase, for the instrument. Empty
    /// means it IS being drawn.</summary>
    private string _flameRefusal = "no rig built yet";

    /// <summary>The two authored frame textures lifted off the clone's own <c>CardEffects</c> in the
    /// instant before it is destroyed — <c>overlayFrameBurn</c> and <c>overlayFrameGhost</c>, the
    /// pictures the flame sheet is textured with. A face whose look has no texture here is refused
    /// the sheet whole rather than shown the shared material's leftover one.</summary>
    private Texture? _flameBurnTexture;
    private Texture? _flameGhostTexture;

    /// <summary>One-shot latches for the three burn-rig outcomes, PER SURFACE — see
    /// <see cref="FxSurface"/> for why a single latch could not answer the question it was written
    /// to answer. Never per card, never per frame; at most one line per (surface, outcome) pair for
    /// the life of the process.</summary>
    private static readonly bool[] s_burnRigLogged = new bool[4];
    private static readonly bool[] s_burnRigRefused = new bool[4];
    private static readonly bool[] s_burnRigNoFxImages = new bool[4];

    /// <summary>
    /// PUT THE CARD BACK — the mirror of <c>CardEffects.RestoreCard()</c> (CardEffects.cs:466-506)
    /// on a rig this overlay owns, for a card the GAME has un-burned.
    ///
    /// <para>THE DEFECT THIS EXISTS FOR (2026-09-06 item 8a, his words: "auch soll im Bereich
    /// 'aktive Karten' die Karte nicht verbrannt dargestellt sein, da sie ja de facto noch nicht
    /// verbrannt ist, sondern nur aktiviert wurde"). The look was a one-way ramp: every caller could
    /// drive it 0 -> 1, and NOBODY could drive it back. So a card the game itself restores kept the
    /// char this overlay had painted, for the rest of its life on that peer's board.</para>
    ///
    /// <para>AND THE GAME RESTORES EXACTLY THIS CASE. <c>FullAbilityCard.SetPile</c> reads
    /// (FullAbilityCard.cs:325-328): <c>if (newCardPile == ECardPile.Hand || newCardPile ==
    /// ECardPile.Activated) cardEffects.RestoreCard();</c>. A persistent card - one that burns only
    /// AFTER its effects have run - enters <c>ECardPile.Activated</c>
    /// (<c>CCharacterClass.MoveAbilityCardToPile</c>, CCharacterClass.cs:440-442: <c>if
    /// (abilityCard.ActiveBonuses.Count > 0) eCardPile = ECardPile.Activated;</c>, which OVERRIDES
    /// the action's own Lost pile), so the owner's own card is wiped clean the instant it reaches
    /// the active area. Without this method the peer's copy was not.</para>
    ///
    /// <para>TERM FOR TERM, and the set is the game's: the three animated face terms back to zero,
    /// the flame sheet back to <c>_FXAnim = 0</c>, and every watched text back to the colour it was
    /// authored in (<c>txtColourStore</c>, CardEffects.cs:499-505) with its vertex gradient back on.
    /// The CONSTANT half is deliberately left where it is - the game leaves <c>_Burn</c> alone in
    /// the arm it takes here too, and all four terms it multiplies are now zero, so it is inert.</para>
    ///
    /// <para>Never throws and never builds a rig: a face that was never painted has nothing to put
    /// back, and calling this on one costs a single field compare.</para>
    /// </summary>
    public void ClearAbilityCardFx()
    {
        if (_burnRigState != BurnRig.Ready || _burnImages == null)
            return;
        try
        {
            for (int i = 0; i < _burnImages.Length; i++)
            {
                Material? mat = MaterialOf(_burnImages[i]);
                if (mat == null)
                    continue;
                SetFloatIfPresent(mat, GreyOutId, 0f);
                SetFloatIfPresent(mat, FlowId, 0f);
                SetFloatIfPresent(mat, DissolveId, 0f);
            }
            Material? flame = MaterialOf(_flameQuad);
            if (flame != null)
                flame.SetFloat(FxAnimId, 0f);
            if (_burnTexts != null && _burnTextColours != null)
            {
                for (int i = 0; i < _burnTexts.Length && i < _burnTextColours.Length; i++)
                {
                    TMPro.TextMeshProUGUI text = _burnTexts[i];
                    if (text == null)
                        continue;
                    text.color = _burnTextColours[i];
                    // The ghost arm switches this OFF past halfway (CardEffects.cs:703-706); the
                    // game's own restore does not put it back explicitly, but it restores the
                    // colour that gradient was authored against, so leaving it off would keep half
                    // of the ghost on a card that is meant to be clean.
                    text.enableVertexGradient = true;
                }
            }
            _burnRigLook = CardFxLook.None;
        }
        catch (System.Exception ex)
        {
            _burnRigState = BurnRig.Refused;
            VRLog.Debug("Net", $"Remote card-FX restore stopped ({ex.Message}) - the peer's card "
                               + "keeps its current look.");
        }
    }

    /// <summary>The BURN look at progress <paramref name="t"/> — the shape every existing caller
    /// uses. See <see cref="SetAbilityCardFxProgress"/>, which it forwards to.</summary>
    public bool SetAbilityBurnProgress(float t) => SetAbilityCardFxProgress(CardFxLook.Burn, t);

    /// <summary>
    /// Drive one of the game's two card-FX timelines on the shown ABILITY face at progress
    /// <paramref name="t"/> (0 = untouched, 1 = the timeline's settled end-state).
    ///
    /// <para><paramref name="look"/> picks the timeline exactly as
    /// <c>FullAbilityCard.TryPlayBurnAnimation</c> does — <see cref="CardFxLook.Burn"/> is
    /// <c>BurnCardTimeline</c>, <see cref="CardFxLook.Ghost"/> is <c>GhostOutOnTimeline</c>. Both
    /// drive the SAME three animated terms over the same 2 s; only the constant half and the text
    /// rule differ, so switching look on a face already wearing one costs a rewrite of the
    /// constants on materials this overlay already minted and nothing else.</para>
    ///
    /// <para>Returns true while the look is really being drawn — false means this face carries no
    /// card-FX material at all, or one whose footprint could not be measured, and the caller then
    /// shows the card WITHOUT it rather than with a guess. Missing char is a small divergence; a
    /// black card on a peer's board is not.</para>
    ///
    /// <para>Never throws: it runs inside the avatar tick.</para>
    /// </summary>
    public bool SetAbilityCardFxProgress(CardFxLook look, float t)
    {
        if (_clone == null || look == CardFxLook.None)
            return false;
        if (_burnRigState == BurnRig.Unbuilt)
            BuildBurnRig();
        if (_burnRigState != BurnRig.Ready || _burnImages == null)
            return false;
        if (look != _burnRigLook)
            RewriteFxConstants(look);

        float k = Mathf.Clamp01(t);
        try
        {
            for (int i = 0; i < _burnImages.Length; i++)
            {
                Material? mat = MaterialOf(_burnImages[i]);
                if (mat == null)
                    continue;
                SetFloatIfPresent(mat, GreyOutId, k);
                SetFloatIfPresent(mat, FlowId, k);
                SetFloatIfPresent(mat, DissolveId, Mathf.Lerp(0f, 0.646f, k));
            }
            // THE FIRE, ON ITS OWN CLOCK. CardEffects.cs:581 and :696 are the same line in both
            // timelines and it is NOT the face rate: Clamp(t * 4, 0, 1) / 2 reaches its settled 0.5
            // at t = 0.25 - half a second into a two-second burn - and holds there. Writing k here
            // instead would have the flame still climbing when the card has finished charring, which
            // is a picture the owner never has.
            Material? flame = MaterialOf(_flameQuad);
            if (flame != null)
                flame.SetFloat(FxAnimId, Mathf.Clamp(k * 4f, 0f, 1f) / 2f);
            if (look == CardFxLook.Ghost && _burnTexts != null && _burnTextColours != null)
            {
                // GhostOutOnTimeline's text rule (CardEffects.cs:700-708) and it is a DIFFERENT rule,
                // not a different colour: every affected text lerps from the colour it was AUTHORED
                // in toward pure WHITE, and its vertex gradient is switched off past the halfway
                // mark. (The burn arm below drives toward two DARK colours instead. That is why the
                // ghosted card reads as washed out and the burnt one as charred.)
                for (int i = 0; i < _burnTexts.Length && i < _burnTextColours.Length; i++)
                {
                    TMPro.TextMeshProUGUI text = _burnTexts[i];
                    if (text == null)
                        continue;
                    text.color = Color.Lerp(_burnTextColours[i], Color.white, k);
                    if (k > 0.5f)
                        text.enableVertexGradient = false;
                }
                return true;
            }
            if (_burnTexts != null)
            {
                // BurnCardTimeline recolours the affected texts while it runs (CardEffects.cs:583-590):
                // the HEADER and the INITIATIVE disc take `burntTextColor`, every other affected text
                // takes mid grey. Both colours are the game's own literals and BOTH are reproduced —
                // the earlier note here ("a per-widget field this overlay has no honest way to read")
                // was a hypothesis, and it is false: `_header` and `_initiativeText` are SERIALIZED
                // privates of CardEffects, so Object.Instantiate copies them onto the clone and
                // StripFragileEffects lifts them off in the instant before the component dies (the
                // same seam the item rig already uses). The runtime-built `imgComp`/`txtComp` arrays
                // really are unreachable — those are built in Initialize() and are not serialized —
                // which is why the SET of texts is still every TMP on the clone rather than the
                // game's `txtAffected` subset.
                //
                // IT IS THE HALF THE USER ASKED FOR BY NAME (item 9a, "Der Text ist bei mir da, aber
                // nicht beim remote board sichtbar"): his own burnt card's title is drawn in
                // burntTextColor over a CHARRED header plate and reads; the peer's clone had a fresh
                // BLUE plate and no recolour at all, so the two halves of the look disagreed.
                Color grey = new(0.5f, 0.5f, 0.5f, 1f);
                for (int i = 0; i < _burnTexts.Length; i++)
                {
                    TMPro.TextMeshProUGUI text = _burnTexts[i];
                    if (text == null)
                        continue;
                    bool headline = ReferenceEquals(text, _burnHeaderText)
                                    || ReferenceEquals(text, _burnInitiativeText);
                    // FROM the colour the face was AUTHORED in, not from white: the game jumps
                    // straight to the target every frame of its loop, so any start colour lands on
                    // the same place at t = 1, and easing out of the real colour is the only one of
                    // the two that does not flash a card whose title is not white to begin with.
                    Color from = _burnTextColours != null && i < _burnTextColours.Length
                        ? _burnTextColours[i]
                        : Color.white;
                    text.color = Color.Lerp(from, headline ? BurntTextColor : grey, k);
                }
            }
            return true;
        }
        catch (System.Exception ex)
        {
            _burnRigState = BurnRig.Refused;
            VRLog.Debug("Net", $"Remote burn ramp stopped ({ex.Message}) - the peer's card keeps its " +
                               "current look.");
            return false;
        }
    }

    /// <summary>
    /// Collect the shown clone's card-FX Images and mint the materials this overlay will write.
    /// Same discipline as <see cref="ApplySpentLook"/>: the signature test names an FX material
    /// without naming a shader, and every material written is one we own and destroy.
    ///
    /// <para>THE IMAGES ARE FOUND BY WALKING THE CLONE, not by reading <c>CardEffects.imgComp</c>.
    /// That array is built in <c>Initialize()</c> at runtime and is NOT a serialized field, so
    /// <c>Object.Instantiate</c> does not carry it - the clone's copy is null. (The ITEM path can
    /// read its rig only because <see cref="StripFragileEffects"/> lifts it off the clone's own
    /// component in the instant before destroying it, and <c>ItemCardEffects</c> has the same
    /// problem; that path exists and this one does not.) The signature walk finds the same set for
    /// the same reason the item bounds gate works: an image the game's FX never paints does not
    /// carry the FX material and is skipped, exactly as the game's own write is inert on it.</para>
    ///
    /// <para>─── THE FOOTPRINT IS MEASURED NOW, NOT REFUSED (2026-09-06 report, items 6 and 9) ───
    /// This method used to REFUSE the whole face when the inherited <c>_PosAndBounds</c> still read
    /// 0x0, and the hardware logs of ModBuild 457 say that is what happened: <c>Remote BURN look
    /// REFUSED</c> printed on BOTH clients and <c>Remote BURN look armed</c> printed on NEITHER, so
    /// not one peer's burning card ever carried a single FX term. That is the whole of "man hört nur
    /// den Sound - sieht aber die Animation nicht": <see cref="RemoteBurnFx"/> held the card on the
    /// owner's board for its 2 s char and there was no char, so the card simply LAY there — which is
    /// also, verbatim, "bleiben die Karten trotzdem auf dem remote board erstmal liegen".</para>
    ///
    /// <para>WHY IT WAS ZERO, and why refusing was the wrong answer. <c>CardEffects.Initialize</c>
    /// runs from <c>Awake</c> and writes the vector; a peer's <c>AbilityCardUI</c> has never been
    /// active on THIS client, so it never ran and the widget's Images still point at the SHARED
    /// authored material whose <c>_PosAndBounds</c> is the asset default. The same log proves the
    /// LOCAL cards are fine — <c>CardHalfTone</c>'s census reads "_PosAndBounds unset on 0/54 (a set
    /// one reads -611,-316,294x450)" on both machines — so this is a property of the CLONE SOURCE,
    /// not of the build or of the player's quality settings.</para>
    ///
    /// <para>AND THE INHERITED VALUE IS WRONG EVEN WHEN IT IS NON-ZERO. Those census numbers are the
    /// card's position on the owner's SCREEN-SPACE hand canvas (-611, -316). This clone is centred on
    /// its own world-space canvas, so an inherited origin two card-widths away saturates the shader's
    /// card-local coordinate to a constant and the face half of the burn stops varying across the
    /// card. That is not a new theory: it is the ModBuild 348 item-card defect ("nur ganz leicht am
    /// Rand sichtbar") word for word, and <see cref="Cards.CardFxBounds"/> is the fix that shipped
    /// for it. So the footprint is RE-DERIVED here in the space the shader actually reads and written
    /// into the copy — the same expression, in the same space, on a material this overlay minted and
    /// destroys. Nothing the game owns is touched.</para>
    ///
    /// <para>The DEEP-BLACK guard is not dropped, it is MOVED: a face whose own rect measures under a
    /// canvas unit is still refused whole (<see cref="ReportBurnRigRefused"/>), because writing a
    /// degenerate footprint is the failure mode itself. The difference is that the refusal is now
    /// about a rect we measured rather than about a number the clone inherited.</para>
    /// </summary>
    private void BuildBurnRig()
    {
        _burnRigState = BurnRig.Refused;
        _burnImages = null;
        _burnTexts = null;
        if (_clone == null)
            return;
        try
        {
            var all = _clone.GetComponentsInChildren<UnityEngine.UI.Image>(includeInactive: true);
            var kept = new List<UnityEngine.UI.Image>(12);
            for (int i = 0; i < all.Length; i++)
            {
                Material? mat = MaterialOf(all[i]);
                if (mat == null || !IsCardFxMaterial(mat))
                    continue;
                kept.Add(all[i]);
            }
            if (kept.Count == 0)
            {
                ReportBurnRigNoFxImages(Surface);
                return;
            }

            Vector4 inherited = MaterialOf(kept[0])?.GetVector(PosAndBoundsId) ?? Vector4.zero;
            if (!TryMeasureFxFootprint(kept, out Vector4 footprint))
            {
                // ALL OR NOTHING, same as the item look: switching the FX terms on against a
                // degenerate footprint is the "card renders DEEP BLACK" failure.
                ReportBurnRigRefused(Surface);
                return;
            }

            for (int i = 0; i < kept.Count; i++)
            {
                Material? mat = MaterialOf(kept[i]);
                if (mat == null)
                    continue;
                Material copy;
                try
                {
                    copy = new Material(mat) { name = mat.name + " (VR-burn)" };
                }
                catch (System.Exception)
                {
                    continue;
                }
                // The constant half of the chosen timeline, written once here so the per-frame call
                // only has to move the three animated terms.
                WriteFxConstants(copy, CardFxLook.Burn);
                // …and the footprint the FX terms are multiplied against, in the space this face is
                // really drawn in. The signature test above already proved the property exists.
                copy.SetVector(PosAndBoundsId, footprint);
                kept[i].material = copy;
                _ownedMaterials.Add(copy);
            }

            _burnImages = kept.ToArray();
            _burnTexts = _clone.GetComponentsInChildren<TMPro.TextMeshProUGUI>(includeInactive: true);
            _burnTextColours = new Color[_burnTexts.Length];
            for (int i = 0; i < _burnTexts.Length; i++)
            {
                if (_burnTexts[i] != null)
                    _burnTextColours[i] = _burnTexts[i].color;
            }
            _burnRigState = BurnRig.Ready;
            _burnRigLook = CardFxLook.Burn;
            BuildFlameQuad(all, CardFxLook.Burn);
            ReportBurnRigOnce(Surface, _burnImages.Length, inherited, footprint,
                              _flameQuad != null, _flameRefusal);
        }
        catch (System.Exception ex)
        {
            _burnImages = null;
            _burnTexts = null;
            VRLog.Debug("Net", $"Remote burn rig skipped ({ex.Message}) - the peer's card burns " +
                               "without the char.");
        }
    }

    /// <summary>
    /// THE FIRE HALF - find the clone's own flame sheet, mint its material and write the nine
    /// constants of the chosen timeline into the copy.
    ///
    /// <para>THIS IS THE TERM THE USER REPORTED MISSING (2026-09-06 item 4: "die Feuer animation,
    /// die kommen sollte wird nicht korrekt dargestellt beim remote board"). Everything the char rig
    /// above drives is the FACE half of the burn - <c>_GreyOut</c>, <c>_Flow</c>, <c>_Dissolve</c>
    /// and the text recolour - and the hardware logs of ModBuild 459 confirm that half landed on
    /// BOTH machines ("Remote BURN look armed on 9 card image(s)" prints on the host and on the
    /// co-player, and <c>RECESS CARD FX</c> reads <c>look=BURN, applied=yes</c>). The user agrees:
    /// "die Karte wird zwar grau und der Text rot wie es sein soll". So the report is NOT a wrong
    /// place, a wrong size, a wrong orientation or a wrong clock - it is one named term that was
    /// never written, and the instrument that shipped in 459 says so in its own words: "the smoke
    /// emitter and the fgFx flame quad are still deliberately not reproduced".</para>
    ///
    /// <para>WHAT THE TERM IS. The burn has TWO face-wide halves and no rim object anywhere in the
    /// system. The FACE half is the material sweep above. The FIRE half is a SECOND Image -
    /// <c>CardEffects</c>' serialized <c>_uiFxOverlay</c>, aliased <c>fgFx</c>, live GameObject
    /// 'UIFX_Overlay', a sprite-less quad drawn over ~120 % of the card - carrying its own shader
    /// with its own nine properties, of which <c>_FXAnim</c> is the only ANIMATED one.</para>
    ///
    /// <para>WHY IT WAS DEFERRED, AND WHAT ANSWERS THAT NOW. <see cref="ApplySpentLook"/>'s note
    /// declined it as "a second, unmeasured material driven by nine more properties and drawn over
    /// the WHOLE card; if any of that lands wrong the failure is a full-card artefact". Two of those
    /// three clauses no longer hold. It is MEASURED - <c>Cards.CardFxBounds</c>' census read this
    /// very graphic on a real hosted card ("'UIFX_Overlay' [no sprite] 132 % of the face"), and the
    /// nine values are read term for term out of <c>CardEffects.BurnCardTimeline</c> (CardEffects.cs
    /// :528-537, written :553-561) and <c>GhostOutOnTimeline</c> (:642-651, written :666-675). And
    /// the material is not the game's: the copy is minted here and destroyed with the clone, exactly
    /// as the face half's is. What REMAINS true is the full-card failure mode, so the sheet is
    /// ALL-OR-NOTHING and refuses itself BY NAME rather than writing a partial set - see
    /// <see cref="_flameRefusal"/>, which the instrument prints.</para>
    ///
    /// <para>FOUND BY SIGNATURE, NOT BY NAME. The graphic's GameObject is called 'UIFX_Overlay' and
    /// <c>Cards.CardFxBounds</c> matches that string for a DIAGNOSTIC; a FIX never may. The test
    /// here is the material's own property set - <c>_FXAnim</c> AND <c>_ParticleTexture</c> AND
    /// <c>_Glow</c>, none of which the face's <c>AbilityCard_Shd</c> carries - which is the same
    /// discipline <see cref="IsCardFxMaterial"/> applies one graphic over.</para>
    ///
    /// <para>THE SHEET IS INERT AT REST. <c>RestoreCard</c> leaves the overlay ACTIVE and simply
    /// zeroes <c>_FXAnim</c> (CardEffects.cs:488-491), and <c>BurnCardTimeline</c> - unlike
    /// <c>GhostOutOnTimeline</c> (:624-626) - never switches the GameObject on at all, which is the
    /// proof that it is authored ON. So this method writes <c>_FXAnim = 0</c> into the fresh copy
    /// and the card looks exactly as it did before until a progress call moves it.</para>
    /// </summary>
    private void BuildFlameQuad(UnityEngine.UI.Image[] all, CardFxLook look)
    {
        _flameQuad = null;
        _flameRefusal = "the face carries no 'UIFX_Overlay' graphic (no material with "
                        + "_FXAnim + _ParticleTexture + _Glow)";
        // NO TEXTURE, NO SHEET. The authored frame picture IS the whole of what this quad draws;
        // without it the shader would fall back to whatever the SHARED authored material happens to
        // hold, which is a guess painted over the entire card - the one failure this is written to
        // avoid.
        Texture? tex = look == CardFxLook.Burn ? _flameBurnTexture : _flameGhostTexture;
        if (tex == null)
        {
            _flameRefusal = look == CardFxLook.Burn
                ? "CardEffects.overlayFrameBurn was null on the clone, so there is no fire picture to draw"
                : "CardEffects.overlayFrameGhost was null on the clone, so there is no ghost frame to draw";
            return;
        }
        for (int i = 0; i < all.Length; i++)
        {
            Material? mat = MaterialOf(all[i]);
            // THE TWO SIGNATURES ARE DISJOINT BY CONSTRUCTION - the face's AbilityCard_Shd carries
            // neither _ParticleTexture nor _Glow - but this walk runs AFTER the face loop has already
            // replaced those Images with copies WE minted, so an explicit skip is what keeps a future
            // shader change from quietly minting a second copy over our own.
            if (mat == null || IsCardFxMaterial(mat) || !IsFlameMaterial(mat))
                continue;
            Material copy;
            try
            {
                copy = new Material(mat) { name = mat.name + " (VR-flame)" };
            }
            catch (System.Exception)
            {
                _flameRefusal = "the overlay material could not be copied, and the game's own shared "
                                + "asset is never written";
                return;
            }
            all[i].material = copy;
            _ownedMaterials.Add(copy);
            _flameQuad = all[i];
            // ACTIVE-STATE IS INHERITED, NEVER FORCED. BurnCardTimeline - unlike GhostOutOnTimeline
            // (CardEffects.cs:624-626) - never switches this GameObject on, so on the owner's card
            // it is authored ON and a clone copies that. If the clone's copy ever came back INACTIVE
            // the owner would have no fire either, so forcing it here would be inventing a picture
            // he does not have; it is REPORTED instead, and the instrument says which it was.
            // Its OWN authored state, not isActiveAndEnabled: the clone can be built under an
            // inactive host, and reporting the HOST as the overlay would be a false reading.
            _flameRefusal = all[i].enabled && all[i].gameObject.activeSelf
                ? string.Empty
                : "INACTIVE-ON-CLONE (armed anyway; the source's own overlay is off, so the owner "
                  + "sees no fire either and forcing it on here would invent a picture he does not have)";
            WriteFlameConstants(copy, look);
            // At rest until a progress call moves it - the same value RestoreCard leaves behind.
            copy.SetFloat(FxAnimId, 0f);
            return;
        }
    }

    /// <summary>Does this material belong to the flame sheet? Three properties the card face's own
    /// <c>AbilityCard_Shd</c> does not carry, so the two graphics can never be confused, and no
    /// shader name and no GameObject name is consulted.</summary>
    private static bool IsFlameMaterial(Material mat) =>
        mat.HasProperty(FxAnimId) && mat.HasProperty(ParticleTextureId) && mat.HasProperty(OverlayGlowId);

    /// <summary>
    /// The nine constants of the flame sheet, term for term out of the game's two timelines -
    /// <c>BurnCardTimeline</c> (CardEffects.cs:528-537) and <c>GhostOutOnTimeline</c> (:642-651).
    /// Every value is the game's own literal.
    ///
    /// <para>The pair that carries the LOOK is <c>_TintColor</c> and <c>_Glow</c>: the burn is
    /// orange (1, 0.3, 0, 0.8) at glow 3 and the ghost is a pale blue-white (0.8, 0.8, 1, 0.6) at
    /// glow ZERO, which is why the ghosted card gets a faint frame and only the burnt one reads as
    /// fire.</para>
    /// </summary>
    private void WriteFlameConstants(Material mat, CardFxLook look)
    {
        bool burn = look == CardFxLook.Burn;
        mat.SetColor(TintColorId, burn
            ? new Color(1f, 0.3f, 0f, 0.8f)
            : new Color(0.8f, 0.8f, 1f, 0.6f));
        Texture? tex = burn ? _flameBurnTexture : _flameGhostTexture;
        if (tex != null)
            mat.SetTexture(ParticleTextureId, tex);
        SetFloatIfPresent(mat, OverlayFlowSpeedId, burn ? 0.5f : 0.2f);
        SetFloatIfPresent(mat, OffsetStrengthId, burn ? 0.2f : 0.8f);
        SetFloatIfPresent(mat, ThinHighlightsId, burn ? 0.463f : 0.125f);
        SetFloatIfPresent(mat, ThinHighlightMinId, burn ? 0f : 0.77f);
        SetFloatIfPresent(mat, ThinHighlightMaxId, burn ? 0.195f : 0.9f);
        SetFloatIfPresent(mat, FlowNoiseTilingId, burn ? 6f : 2f);
        SetFloatIfPresent(mat, OverlayGlowId, burn ? 3f : 0f);
    }

    /// <summary>
    /// The CONSTANT half of one of the game's two card-FX timelines, term for term. Six values, and
    /// the only reason there are two tables instead of one is that the game has two:
    /// <c>BurnCardTimeline</c> (CardEffects.cs:518-531) and <c>GhostOutOnTimeline</c> (:632-645).
    /// The tint is the term the eye reads — warm brown for the char, cold blue-grey for the ghost.
    /// </summary>
    private static void WriteFxConstants(Material mat, CardFxLook look)
    {
        bool burn = look == CardFxLook.Burn;
        SetFloatIfPresent(mat, BurnId, burn ? 0.691f : 0.7f);
        SetFloatIfPresent(mat, FlowOffsetId, 0.03f);
        SetFloatIfPresent(mat, FlowSpeedId, burn ? 0.4f : 0.2f);
        SetFloatIfPresent(mat, DissolveVerticalGradientId, 0.2f);
        if (mat.HasProperty(BurnColourTintId))
        {
            mat.SetColor(BurnColourTintId, burn
                ? new Color(0.36862746f, 0.14509805f, 0.07450981f, 0.601f)
                : new Color(0.24313726f, 24f / 85f, 29f / 85f, 0.5f));
        }
        if (mat.HasProperty(AnimNoiseMaskId))
        {
            float tile = burn ? 40f : 10f;
            mat.SetTextureScale(AnimNoiseMaskId, new Vector2(tile, tile));
        }
    }

    /// <summary>Switch a face that is already rigged from one look to the other. Only the constant
    /// half moves: the walk, the minting and the footprint measurement all stand, because the two
    /// timelines drive the same properties on the same materials.</summary>
    private void RewriteFxConstants(CardFxLook look)
    {
        if (_burnImages == null)
            return;
        // THROUGH _burnImages AND NOT _ownedMaterials: that list is the overlay's whole material
        // ledger and the ITEM path adds its own copies to it (ApplySpentLook). Those are never on
        // an ability face today, but a rewrite keyed on "everything this overlay owns" would start
        // repainting them the day one is, which is a defect nobody would look for here.
        for (int i = 0; i < _burnImages.Length; i++)
        {
            Material? mat = MaterialOf(_burnImages[i]);
            if (mat != null)
                WriteFxConstants(mat, look);
        }
        // …AND THE FIRE HALF, which has its own nine constants and its own texture. A look change
        // that moved only the face would leave an ORANGE sheet over a card that is ghosting, which
        // is the one thing the two timelines differ in by eye.
        Material? flame = MaterialOf(_flameQuad);
        if (flame != null)
        {
            Texture? tex = look == CardFxLook.Burn ? _flameBurnTexture : _flameGhostTexture;
            if (tex == null)
            {
                // The other look has no picture. Hide the sheet rather than paint the wrong one.
                flame.SetFloat(FxAnimId, 0f);
                _flameQuad = null;
                _flameRefusal = "the other look's overlay frame texture was null on the clone";
            }
            else
            {
                WriteFlameConstants(flame, look);
            }
        }
        _burnRigLook = look;
    }

    /// <summary>
    /// THE CARD-FX FOOTPRINT THIS CLONE IS ACTUALLY DRAWN AT, in the ROOT CANVAS's local space —
    /// which is the space uGUI batches a Graphic's vertices in, and therefore the space the shader
    /// reconstructs its card-local coordinate from.
    ///
    /// <para>It is <c>CardEffects.Initialize</c>'s own expression (CardEffects.cs:311-313 and
    /// :341-348) evaluated on OUR hierarchy instead of on the hand canvas: the ORIGIN is the card
    /// root's position (the game reads <c>((RectTransform)transform.parent).anchoredPosition</c>,
    /// i.e. where the card sits on its canvas — <see cref="FitClone"/> centres this clone, so the
    /// measurement lands on 0,0 and is computed rather than assumed), and the BOUNDS are the card
    /// PLATE's rect (the game reads <c>header.rectTransform.rect</c>; <c>header</c> is the
    /// full-card <c>_headerImage</c>, which is why the census reads 294x450 — a card-sized rect —
    /// and why the largest FX image is the same object).</para>
    ///
    /// <para>Returns false for a rect under a canvas unit, which is the degenerate footprint the
    /// DEEP-BLACK guard exists for. Same refusal, measured instead of inherited.</para>
    /// </summary>
    private bool TryMeasureFxFootprint(List<UnityEngine.UI.Image> fxImages, out Vector4 footprint)
    {
        footprint = default;
        if (_clone == null || _canvas == null)
            return false;
        var cardRect = _clone.transform as RectTransform;
        if (cardRect == null)
            return false;

        RectTransform? plate = null;
        float widest = 0f;
        for (int i = 0; i < fxImages.Count; i++)
        {
            RectTransform? r = fxImages[i] != null ? fxImages[i].rectTransform : null;
            if (r == null)
                continue;
            float area = r.rect.width * r.rect.height;
            if (area > widest)
            {
                widest = area;
                plate = r;
            }
        }
        if (plate == null)
            return false;

        Vector2 size = plate.rect.size;
        if (size.x < 1f || size.y < 1f)
            return false;
        Vector3 local = _canvas.transform.InverseTransformPoint(cardRect.position);
        footprint = new Vector4(local.x, local.y, size.x, size.y);
        return true;
    }

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-06 report, items 6 and 9): the one line that says whether a peer's
    /// burnt card can carry the char at all. Grep token <c>Remote BURN look</c>, which every one of
    /// the three outcomes shares, so a single grep says which of them happened.
    ///
    /// <para>WORKING = exactly one <c>armed</c> line, with <c>images</c> ≥ 1 and a <c>now</c>
    /// footprint whose extents are the card's own (roughly 294x450 canvas units, the number
    /// <c>CardHalfTone</c>'s local census already prints), beside <c>BURN CARD [peer n]</c> lines
    /// with <c>face=REAL</c>.</para>
    ///
    /// <para>INERT = a <c>REFUSED</c> line, or no <c>Remote BURN look</c> line of any kind beside
    /// <c>BURN CARD [peer n]</c> lines. Both mean zero FX terms were written and the peer's card
    /// lies on their board for 2 s without charring — which is the 457 picture exactly.</para>
    ///
    /// <para>STILL BEYOND THE INSTRUMENT = an <c>armed</c> line reading <c>FIRE=…ARMED</c>, a
    /// non-degenerate <c>now</c> footprint, and the user STILL reports no fire. Every term this rig
    /// can write is then proven written, and the next lead is the one graphic no term here names —
    /// the <c>CardSmoke</c> plume, which is <see cref="RemoteCardPlume"/>'s and is gated behind the
    /// owner's own <c>[Cards] GameCardParticles</c> permission bit, so check that bit before
    /// suspecting this rig again.</para>
    ///
    /// <para>HALF-FIXED = <c>FIRE=REFUSED (…)</c> beside an otherwise armed line. The char landed
    /// and the flame sheet did not, and the parenthesis names WHICH of the three terms refused it:
    /// no overlay graphic on the face, a null <c>overlayFrameBurn</c>, or a material that would not
    /// copy. That is the 459 picture plus the char — grey card, red title, no fire.</para>
    ///
    /// <para>─── READ IT PER SURFACE, WHICH IS THE WHOLE POINT OF THE <c>[…]</c> TAG ───────────────
    /// The tag is a <see cref="FxSurface"/>, and the line latches per surface rather than once for
    /// the process. THE COMPLETE READING for the 1:1 ruling of 2026-09-06 ("Für 1:1 Regel soll es
    /// voll gleich da sein … beim remote-board oder remote-karten") is <c>FIRE=…ARMED</c> on
    /// <b>three</b> tags across one scenario:
    /// <list type="bullet">
    /// <item><c>[Recess]</c> — the card lying in a peer's round slot, a live 2 s ramp.</item>
    /// <item><c>[Flight]</c> — the burn slab in transit to the burnt stack.</item>
    /// <item><c>[Pile]</c> — the opened burnt-pile fan, which is where user item 9b of the previous
    ///   round was reported and is the surface this project has already forgotten once.</item>
    /// </list>
    /// A tag that never appears is a surface whose fire was never even asked for; a tag appearing
    /// with <c>FIRE=REFUSED</c> is a surface where it was asked for and declined. Before this line
    /// carried a tag, only the FIRST surface to build a rig ever printed and the other two were
    /// indistinguishable from silence — which is exactly how 9b got through.</para>
    ///
    /// <para><c>[Unnamed]</c> NEVER APPEARS, and its absence is a reading too: the ACTIVE-CARDS
    /// column builds these faces and drives no look on them, because an activated card is not burnt
    /// (item 8a). An <c>[Unnamed]</c> line would mean some surface started driving the look without
    /// naming itself.</para>
    ///
    /// <para>THE TIMING IS DIFFERENT ON EACH AND THAT IS DELIBERATE. The fire runs at four times the
    /// face rate, capped and halved (CardEffects.cs:581), so it settles at <c>_FXAnim = 0.5</c> a
    /// half second in and holds while the char darkens for the remaining 1.5 s. <c>[Recess]</c> and
    /// the hold phase of <c>[Flight]</c> drive the real ramp; the arc phase of <c>[Flight]</c> and
    /// the whole of <c>[Pile]</c> write progress 1, which lands on that same settled 0.5. A card the
    /// viewer finds ALREADY BURNT — every card in an opened pile fan — therefore shows the settled
    /// fire and never restarts the animation, which is the rule <c>RemotePileFronts</c> states in
    /// its own words and the one <c>ApplySpentLook</c> was written around.</para>
    /// </summary>
    private static void ReportBurnRigOnce(FxSurface surface, int images, Vector4 was, Vector4 now,
                                          bool flame, string flameRefusal)
    {
        if (s_burnRigLogged[(int)surface])
            return;
        s_burnRigLogged[(int)surface] = true;
        // HW-VERIFY: grep token "Remote BURN look" — see this method's doc for the four readings.
        VRLog.Note("Net", $"Remote BURN look [{surface}] armed on {images} card image(s): _PosAndBounds was "
                          + $"({was.x:0.#}, {was.y:0.#}, {was.z:0.#}x{was.w:0.#}) -> now "
                          + $"({now.x:0.#}, {now.y:0.#}, {now.z:0.#}x{now.w:0.#}). TERMS ARMED, by "
                          + "name: FACE=_GreyOut+_Flow+_Dissolve (0 -> 1, 0 -> 1, 0 -> 0.646 over 2 s) "
                          + "on the images above; TEXT=header+initiative -> RGB(143,58,44), the rest -> "
                          + "mid grey; FIRE=the fgFx 'UIFX_Overlay' flame sheet, _FXAnim 0 -> 0.5 over "
                          + $"the first 0.5 s at _TintColor(1, 0.3, 0, 0.8) _Glow 3 — {(flame ? (flameRefusal.Length == 0 ? "ARMED" : "ARMED but " + flameRefusal) : "REFUSED (" + flameRefusal + ")")}; "
                          + "SMOKE=the CardSmoke plume, which is NOT part of this rig at all and is "
                          + "mirrored separately by RemoteCardPlume behind the owner's own "
                          + "[Cards] GameCardParticles permission bit (wire id 236). The FOOTPRINT the "
                          + $"shader will read is the 'now' half above: ({now.z:0.#} x {now.w:0.#}) "
                          + "canvas units centred on this clone. The 'was' half is what the CLONE "
                          + "INHERITED: 0x0 extents mean the peer's widget never ran "
                          + "CardEffects.Initialize on this client (it has never been active here), and "
                          + "a large negative origin means it ran on a SCREEN-SPACE hand canvas and the "
                          + "number is two card-widths away from this world-space clone. Either way the "
                          + "FX terms would have been multiplied by a card-local coordinate that does "
                          + "not vary across the card, which is the ModBuild 348 item-card defect one "
                          + "card type over. The game's CardEffects stays stripped throughout.");
    }

    /// <summary>
    /// HARDWARE VERIFICATION: the DEEP-BLACK refusal, now measured rather than inherited. One line per
    /// process. Reading it means the char is inert and the reason is this clone's own geometry.
    /// </summary>
    private static void ReportBurnRigRefused(FxSurface surface)
    {
        if (s_burnRigRefused[(int)surface])
            return;
        s_burnRigRefused[(int)surface] = true;
        // HW-VERIFY: grep token "Remote BURN look" — the REFUSED arm of the three outcomes.
        VRLog.Alert("Net", $"Remote BURN look [{surface}] REFUSED: this clone's own card plate measures under a "
                           + "canvas unit, so the footprint written into _PosAndBounds would be "
                           + "degenerate - the 'card renders DEEP BLACK' failure mode. The peer's "
                           + "burning card keeps its FRESH face and still flies into the burnt stack; "
                           + "the missing char is the cheaper divergence. NOTE this is no longer the "
                           + "457 refusal, which fired on the INHERITED value: a clone whose source "
                           + "never ran CardEffects.Initialize now measures its own plate instead.");
    }

    /// <summary>
    /// HARDWARE VERIFICATION: the third outcome, which used to be a silent <c>return</c> and is the reason
    /// a 457 log could show neither an armed line nor a refusal for a given face. One line per
    /// process.
    /// </summary>
    private static void ReportBurnRigNoFxImages(FxSurface surface)
    {
        if (s_burnRigNoFxImages[(int)surface])
            return;
        s_burnRigNoFxImages[(int)surface] = true;
        // HW-VERIFY: grep token "Remote BURN look" — the NO-FX-MATERIAL arm of the three outcomes.
        VRLog.Alert("Net", $"Remote BURN look [{surface}] has NO CARD-FX MATERIAL to write: not one Image on this "
                           + "clone carries the _GreyOut + _PosAndBounds signature, so there is nothing "
                           + "on this face the game's own burn terms would paint either. The peer's "
                           + "card burns without the char. If this line appears instead of the 'armed' "
                           + "one, the signature test has stopped matching the game's material and "
                           + "CardHalfTone/CardFxBounds use the same pair and are equally blind.");
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
        _burnImages = null;  // the burn rig named the clone's own Images
        _burnTexts = null;
        _burnTextColours = null;
        _burnHeaderText = null;
        _burnInitiativeText = null;
        _flameQuad = null;               // the flame sheet was the clone's own Image
        _flameRefusal = "no rig built yet";
        _flameBurnTexture = null;        // …and the textures were lifted off the clone's CardEffects
        _flameGhostTexture = null;
        _burnRigState = BurnRig.Unbuilt;
        _burnRigLook = CardFxLook.None;
        _shownSourceId = int.MinValue;
    }
}
