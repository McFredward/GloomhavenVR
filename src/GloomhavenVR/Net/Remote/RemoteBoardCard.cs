using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Shared card panel
// =================================================================================================

/// <summary>
/// One card slot on a remote board. FACE-UP shows the REAL, fully detailed game card face;
/// face-down (or empty-but-present) shows the mod card BACK. Rebuilt only on a real change
/// (identity / face-up / owner), so it is cheap to drive on the board's 4 Hz cadence.
///
/// This is the ONE card widget every remote-board surface uses (the two round-card slots and the
/// active-card column), extracted from <see cref="RemoteControlBoard"/> so the size is a parameter:
/// the round cards read large at a distance, the active column deliberately smaller — the same size
/// split the LOCAL board makes between its slot cards and <c>ActivePileViewer</c>.
///
/// CARD FACE ART (the user requirement "vollständig alle Details … damit Mitspieler die Karte
/// vollständig lesen können"): a face-up card here is the game's OWN card widget — full painted art,
/// both action halves with all their icons and numbers, the initiative disc, the level, the
/// enhancement stickers — cloned onto a world-space canvas by <see cref="RemoteCardArt"/> from a
/// source resolved by <see cref="RemoteAbilityCardSource"/> (the peer's own live
/// <c>AbilityCardUI</c>, or a widget borrowed from the game's pool). See that class for the evidence
/// that this is possible at all; the mod-drawn NAME + INITIATIVE panel this slot used to show is now
/// only the LAST-RESORT fallback for when neither source can be resolved.
///
/// ANTI-CHEAT: this panel never decides anything. It renders a face only when its caller passes
/// <c>front: true</c>, and every caller derives that strictly from
/// <see cref="RevealGate.ShowRoundCardFronts"/>. The face host is created INSIDE the
/// <c>if (front)</c> branch and <see cref="RemoteCardArt"/> builds its clone under an INACTIVE host,
/// so no face object can ever render for a frame ahead of the gate; on <c>front: false</c> the face
/// is torn down before anything else happens.
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. Source: the peer's round-card slots off
/// the host-replicated <c>CPlayerActor.CharacterClass</c> (<c>NetPlayerActors.ActorFor</c>), every
/// face gated by <see cref="RevealGate"/>. Card IDENTITY is DELIBERATELY-NOT on the wire — drawing
/// a readable card WITHOUT transmitting one is the requirement this panel exists to satisfy. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteBoardCard
{
    private readonly GameObject _root;
    private readonly MeshRenderer _bg;
    private readonly Material _backMat;
    private readonly Material _faceMat;
    private readonly Material _bodyMat;
    private readonly TextMeshPro _initLabel;
    private readonly TextMeshPro _nameLabel;

    /// <summary>The real-card-face overlay for this slot (created lazily on the first face-up card, so
    /// a slot that never turns face-up never allocates one). Owned by us and destroyed with the
    /// board root; the CLONE inside it is a throwaway we own outright.</summary>
    private RemoteCardArt? _art;
    private readonly float _width;
    private readonly float _height;

    private int _shownId = int.MinValue;
    private bool _shownFront;
    private bool _shownEmpty = true;
    private int _shownOwner = int.MinValue;

    /// <summary>
    /// Change-key identity for <see cref="SetAnonymousBack"/> — "a card is lying here, and this
    /// client does not (yet) know which one". It must be distinct from
    /// <c>int.MinValue</c> (which is this class's "no card at all" key, see <see cref="Set"/> and
    /// <see cref="Blank"/>) or the panel would early-return between the empty state and the
    /// anonymous back and never repaint.
    /// </summary>
    private const int AnonymousCardId = int.MinValue + 1;

    /// <summary>Which path produced the face currently shown — surfaced to the board's diagnostics so
    /// a hardware log can state the FIDELITY per slot, not just that a card is drawn.</summary>
    public RemoteAbilityCardSource.FacePath Path { get; private set; }
        = RemoteAbilityCardSource.FacePath.None;

    /// <summary>
    /// <paramref name="materialiseOwner"/> is the ONE parameter that says WHICH KIND of slot this
    /// is, and it is the call site that says it rather than a branch in here guessing.
    ///
    /// <para>Non-null = a ROUND-CARD RECESS on that peer's board: a card really is played into it
    /// and taken out of it, so it gets the owner's own materialise/crumble ramp (see
    /// <see cref="TickMaterialise"/>). Null = the ACTIVE-CARD COLUMN, which shares this class: an
    /// active card is a standing summary of what is in play, and the owner's driver plays no
    /// appear/vanish on one — <c>CardsDriver</c> calls <c>VRCard.PlayAppear</c>/<c>Vanish</c> for
    /// the TRAY (round slot) and HALF (docked action) sets only (CardsDriver.4.Rebuild.cs:1005,
    /// 1083, 1103), never for <c>ActivePileViewer</c>. Ramping the column would be inventing an
    /// animation its owner does not see, i.e. a NEW 1:1 defect rather than a fix.</para>
    ///
    /// <para>It is also the only handle on the owner's DUST permission (wire id
    /// <see cref="NetProtocol.TuneCardDustOn"/>), which is why one parameter answers both
    /// questions instead of two: a slot that plays the ramp is exactly a slot that has an owner to
    /// ask. NULL BY DEFAULT deliberately — an omitted answer resolves to today's picture (no
    /// ramp), the same "an unstated permission is the stricter one" rule
    /// <c>Cards.CardDustFx.Permission</c> is built on.</para>
    /// </summary>
    public RemoteBoardCard(Transform parent, Vector3 localPos, float width, float height,
                           RemoteAvatar? materialiseOwner = null)
    {
        _materialiseOwner = materialiseOwner;
        _root = new GameObject("Card");
        _root.transform.SetParent(parent, worldPositionStays: false);
        _root.transform.localPosition = localPos;
        _width = width;
        _height = height;

        // Card-back texture, drawn UNLIT (read the mod's shared back texture off CardMesh's back
        // material without mutating it, then wrap it in our own unlit material).
        Texture? backTex = CardMesh.CreateBackMaterial().mainTexture;
        _backMat = BoardVisual.Unlit(Color.white, backTex);
        _faceMat = BoardVisual.Unlit(new Color(0.86f, 0.81f, 0.68f, 1f)); // parchment (fallback panel)
        // Dark card BODY behind a hosted real face: RemoteCardArt insets the art by its BorderFraction,
        // so a rim of this quad shows around it and reads as the card's own dark edge — the same
        // relationship CardMesh's slab has to an adopted face on the local board.
        _bodyMat = BoardVisual.Unlit(new Color(0.09f, 0.08f, 0.07f, 1f));

        // THE PEER'S BOARD CARDS TAKE THE SILHOUETTE TOO (user, 2026-08-11: "auch alle Karten
        // genauso die remote angezeigt werden im Multiplayer bei anderen Spielern"). This site was
        // deliberately left out of the previous round's five opt-ins because it does not USE
        // CardMesh's materials — it reads only `.mainTexture` off one and re-wraps it in its own
        // unlit material, which would have dropped the alpha. Under the 1:1 rule that exclusion is
        // no longer acceptable, so the shape is BOUND to these three materials instead of fetched:
        // this constructor can run before any footprint exists — a spectator may see a peer's board
        // long before building a card of their own — and a getter would leave that board
        // rectangular for the rest of the session.
        //
        // Sprites/Default multiplies texture × colour and alpha-blends, so the Mask layer (white
        // RGB, footprint alpha) leaves the dark body dark and only stops it painting outside the
        // card's own outline. No footprint yet, or none ever: unchanged from today.
        CardMesh.BindSilhouette(_backMat, CardBodyKind.Ability, CardMesh.SilhouetteLayer.Back);
        CardMesh.BindSilhouette(_faceMat, CardBodyKind.Ability, CardMesh.SilhouetteLayer.Mask);
        CardMesh.BindSilhouette(_bodyMat, CardBodyKind.Ability, CardMesh.SilhouetteLayer.Mask);

        _bg = BoardVisual.Quad(_root.transform, "Face", new Vector2(width, height), _backMat);

        _initLabel = RemoteBoardContent.Label(_root.transform, "Initiative",
            new Vector3(0f, height * 0.34f, -0.001f),
            new Vector2(width * 0.9f, height * 0.28f), 0.09f,
            new Color(0.12f, 0.10f, 0.08f), TextAlignmentOptions.Center, FontStyles.Bold);
        _nameLabel = RemoteBoardContent.Label(_root.transform, "Name",
            new Vector3(0f, -height * 0.12f, -0.001f),
            new Vector2(width * 0.86f, height * 0.5f), 0.045f,
            new Color(0.14f, 0.11f, 0.09f), TextAlignmentOptions.Center, FontStyles.Normal, wrap: true);

        // The ramp's own clock — only for a recess that has one (see the constructor doc), so the
        // active-card column adds no component and pays nothing.
        if (materialiseOwner != null)
            _root.AddComponent<MaterialisePump>().Slot = this;

        // Start HIDDEN and in step with the _shownEmpty seed: Set() early-returns while nothing
        // changed, so a panel that never receives a card (an unused active-grid cell, an empty
        // round slot) must not be left standing here showing a card back.
        _root.SetActive(false);
    }

    /// <summary>Re-seat the panel (the active-card grid relays its cards as the pile changes).</summary>
    public void Move(Vector3 localPos) => _root.transform.localPosition = localPos;

    /// <summary>
    /// Mip-bake upkeep for a hosted real face (see <see cref="RemoteCardArt.MaintainMipBake"/>).
    /// <see cref="Set"/> is change-gated, so without this the clone would only ever get the single
    /// build-time sprite swap and its ASYNC header art would stay mipless — the "extreme aliasing"
    /// the user reported on remote cards. Called by the owners on their 4 Hz content cadence;
    /// self-early-returns while no front is up, so it is free on backs/empty slots.
    /// </summary>
    public void MaintainMips() => _art?.MaintainMipBake();

    /// <summary>
    /// Show <paramref name="card"/> face-up when <paramref name="front"/> — as the REAL game card
    /// face when one can be resolved for <paramref name="owner"/>, else as the mod-drawn
    /// name+initiative panel — otherwise the card BACK; hide entirely when there is no card.
    ///
    /// <paramref name="owner"/> is the actor whose board this is; it is used ONLY to find that
    /// player's own already-existing card widget (see <see cref="RemoteAbilityCardSource"/>) and is
    /// never written to. Passing null simply drops the slot to the pooled/fallback paths.
    ///
    /// Change-gated on identity + face-up + owner, so the expensive part (cloning a card widget) runs
    /// once per actual change and NEVER per tick — the board's 4 Hz refresh stays a handful of
    /// early-returns.
    /// </summary>
    public void Set(CAbilityCard? card, bool front, CPlayerActor? owner = null)
    {
        bool empty = card == null;
        int id = card != null ? card.CardInstanceID : int.MinValue;
        int ownerId = OwnerKey(owner);
        if (empty == _shownEmpty && id == _shownId && front == _shownFront && ownerId == _shownOwner)
            return;
        // THE MATERIALISE's own change question, asked BEFORE the shown state is overwritten: this
        // is an ARRIVAL only when the recess was empty or held a DIFFERENT card. A front-only
        // change (the reveal gate opening at the end of the selection phase) or an owner-only
        // change is a repaint of a card that was already lying here, and the owner plays no appear
        // for either — their card never left the slot, so nothing materialises on their screen.
        bool arrived = _shownEmpty || _shownId != id;
        _shownEmpty = empty;
        _shownId = id;
        _shownFront = front;
        _shownOwner = ownerId;

        // WHAT THE PLUME MIRROR READS (see TickPlume). Latched HERE, on the very change gate that
        // decides the face, so the effect and the picture are answers about the same card by
        // construction rather than by a second resolve that could disagree. Both references are
        // read-only: `owner` already carries that contract from this method's own doc.
        _plumeCard = card;
        _plumeOwner = owner;

        if (empty)
        {
            // THE CRUMBLE HALF of the materialise. The owner's card does not pop out of the recess:
            // VRCard.Vanish holds it in place and fades it over DockVanishSeconds (0.30 s at the
            // shipped default) while a dust puff carries it away. Mirrored here, the recess keeps
            // drawing what it was drawing and TickMaterialise fades it out; the hosted face is torn
            // down and the slot deactivated when the ramp lands, not before.
            if (_materialiseOwner != null && _root.activeSelf && !_vanishing)
            {
                // …unless the reveal gate shut in the same step the card left. Then the FACE goes
                // now, at once, and only the back/quad finishes the fade: a crumble is presentation,
                // it may never buy a face-down recess one extra frame of a readable card.
                if (!front)
                    ClearFace();
                BeginVanish();
                return;
            }
            if (_vanishing)
            {
                // Still crumbling and still empty, so only `front` can have changed — same rule.
                if (!front)
                    ClearFace();
                return;
            }
            // ANTI-CHEAT + hygiene: drop any hosted face BEFORE the slot goes away, so a slot that is
            // re-used for a different card (the active grid re-packs its cells) can never flash the
            // previous card's face.
            ClearFace();
            if (_root.activeSelf) _root.SetActive(false);
            return;
        }
        if (!_root.activeSelf) _root.SetActive(true);

        // THE MATERIALISE HALF, seeded HERE — before the face below is built — so the incoming
        // card's very first rendered frame is already transparent and settled-small. Seeding it
        // after the build would show one fully opaque frame, which is the pop the ramp exists to
        // remove (the same line, for the same reason, as RemoteCapFx.PlayAppear's frame-zero paint).
        if (_materialiseOwner != null && arrived)
            ArriveMaterialise();

        if (front)
        {
            // Try the REAL card widget first. The host is created here, inside the front branch —
            // there is no code path in which a face object exists while the gate says "backs".
            _art ??= new RemoteCardArt(_root.transform, _width, _height);
            Path = RemoteAbilityCardSource.ShowFullFace(_art, owner, card!);

            bool real = Path != RemoteAbilityCardSource.FacePath.None;
            if (real)
            {
                // A real face is up: show the dark card BODY behind it (the art is inset, so this is
                // the card's edge) and retire the mod-drawn labels — the face carries all of it, in
                // the game's own typography, and a second name on top would only fight it.
                _bg.sharedMaterial = _bodyMat;
                _initLabel.gameObject.SetActive(false);
                _nameLabel.gameObject.SetActive(false);
                // The board is contractually INERT. The clone is uGUI (no colliders today) and
                // RemoteCardArt already strips its raycasters, but the board's guarantee is a runtime
                // fact, not a review claim — so sweep the hosted subtree too.
                RemoteBoardFurniture.StripColliders(_root, "RemoteBoardCard face");
                return;
            }

            // LAST RESORT (no live widget AND the pool could not manufacture one): the legacy
            // parchment panel with the card's name + initiative. Strictly better than a blank back,
            // strictly worse than the real card — the log line says which one you are looking at.
            _bg.sharedMaterial = _faceMat;
            _initLabel.gameObject.SetActive(true);
            _nameLabel.gameObject.SetActive(true);
            _initLabel.text = card!.Initiative.ToString();
            _nameLabel.text = DisplayName(card);
        }
        else
        {
            ClearFace();
            _bg.sharedMaterial = _backMat;
            _initLabel.gameObject.SetActive(false);
            _nameLabel.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Show a card BACK for a slot that is known to be OCCUPIED without knowing WHICH card occupies
    /// it — the user's requirement "Ich will auch sehen wenn eine Karte abgelegt wurde auf dem
    /// controllboard (mit der Rueckseite)". The occupancy comes off the wire
    /// (<see cref="NetProtocol.BoardUiSlotMask"/>, a POSITION and nothing else); the identity does
    /// not, and never will.
    ///
    /// WHY IT IS NOT JUST <c>Set(card, front: false)</c>. There IS no card to pass. The three cases
    /// this panel now distinguishes are "no card here" (hidden), "a card whose identity we hold"
    /// (<see cref="Set"/> — face or back per <see cref="RevealGate"/>) and "a card we can see but
    /// cannot name" (here). Only the middle one can ever turn face-up, so this method is
    /// structurally incapable of revealing anything: it does not take a card, it tears any hosted
    /// face down, and it hard-sets the back material.
    /// </summary>
    public void SetAnonymousBack()
    {
        if (!_shownEmpty && _shownId == AnonymousCardId && !_shownFront)
            return; // already showing the anonymous back — nothing to repaint
        bool arrived = _shownEmpty || _shownId != AnonymousCardId;
        _shownEmpty = false;
        _shownId = AnonymousCardId;
        _shownFront = false;
        _shownOwner = int.MinValue;

        ClearFace();
        // A recess whose card this client cannot NAME has nothing to look an effect up on, and a
        // plume there would announce that something is happening to a card nobody may identify.
        _plumeCard = null;
        _plumeOwner = null;
        ResetPlume();
        if (!_root.activeSelf) _root.SetActive(true);
        // A back materialising is not a leak: it says a card arrived in this recess, which is
        // exactly what the occupancy mask this method is driven by already says out loud. The
        // owner's own card played the identical ramp at the identical moment.
        if (_materialiseOwner != null && arrived)
            ArriveMaterialise();
        _bg.sharedMaterial = _backMat;
        _initLabel.gameObject.SetActive(false);
        _nameLabel.gameObject.SetActive(false);
    }

    /// <summary>
    /// ANTI-CHEAT TEARDOWN — return the slot to "nothing shown" AND invalidate its change key.
    ///
    /// WHY THE CHANGE KEY MUST GO TOO. <see cref="Set"/> early-returns while identity + face-up +
    /// owner are unchanged, so simply hiding the slot's GameObject would leave the key claiming a
    /// face is up. Two things then go wrong on the way back: (a) if the slot reappears while the
    /// gate is SHUT, the whole board is re-activated one statement before <see cref="Set"/> runs —
    /// a single frame in which a stale face would render behind a closed gate; and (b) if it
    /// reappears while the gate is OPEN with the same card, the early-return would skip the rebuild
    /// and the slot would stay blank. Clearing the key makes the next <see cref="Set"/> a real
    /// decision in both directions, which is the only state this class is allowed to be in.
    ///
    /// Called whenever the board stops being drawn (visibility off, peer without a board, the
    /// ActionPhaseOnly setting hiding the board during the secret selection phase). Cheap enough to
    /// call every frame while hidden: it self-early-returns once blank.
    /// </summary>
    public void Blank()
    {
        if (_shownEmpty && _shownId == int.MinValue && !_shownFront
            && Path == RemoteAbilityCardSource.FacePath.None
            && !_appearing && !_vanishing)
            return; // already blank — nothing to undo

        // A RUNNING RAMP IS A REASON NOT TO EARLY-RETURN, which is why it is a term above. Blank()
        // is the anti-cheat teardown: the board stopped being drawn, so the slot must be gone THIS
        // frame, not 0.30 s from now. EndMaterialise restores the alpha and the scale first, or a
        // re-shown slot would come back mid-fade and settled-small forever (the same class of stale
        // latch the change key below is cleared for).
        EndMaterialise();
        // …and the no-storm seed goes with it: a board that comes back has its recesses re-seeded
        // silently, exactly like CardsDriver's _dockAnimSuppressed. The owner's cards never left
        // their board while ours was hidden, so they materialised nothing.
        _materialiseSeeded = false;
        ClearFace();
        SetHalfStates(-1, -1); // a re-shown slot must never come back with a stale glow lit
        _plumeCard = null;     // …and never with a stale plume latch either: a board that stopped
        _plumeOwner = null;    // being drawn saw none of the frames in between.
        ResetPlume();
        _shownEmpty = true;
        _shownId = int.MinValue;
        _shownFront = false;
        _shownOwner = int.MinValue;
        _bg.sharedMaterial = _backMat;
        _initLabel.gameObject.SetActive(false);
        _nameLabel.gameObject.SetActive(false);
        if (_root.activeSelf)
            _root.SetActive(false);
    }

    // ------------------------------------------------------------- the GAME's card plume --

    /// <summary>
    /// The peer's OWN live <c>fullAbilityCard</c> for the card seated in this recess — the only
    /// object on this client that can be asked whether the game is running a card effect on it.
    /// The CLONE hosted by <see cref="RemoteCardArt"/> cannot answer: it has its
    /// <c>CardEffects</c> <c>DestroyImmediate</c>d on purpose (the screen-space <c>_PosAndBounds</c>
    /// material is the known "card renders DEEP BLACK" hazard on a detached world-space clone), so
    /// the SOURCE is the only thing that can be asked. Cached across frames because the trigger is
    /// sampled per frame while the resolve is a list scan.
    /// </summary>
    private FullAbilityCard? _plumeSource;

    /// <summary>One-shot NEGATIVE latch for <see cref="_plumeSource"/>: this card has no live widget
    /// (its face came from the pooled borrow, or the peer's hand is not built). Without it a slot in
    /// that state would rescan the peer's whole deck every frame forever. Cleared with the card key,
    /// which is the only event that can change the answer — the same widget lookup already succeeded
    /// or failed once for this exact card when <see cref="Set"/> built the face.</summary>
    private bool _plumeSourceMissing;

    /// <summary>
    /// The EDGE latch: whether the source widget was running a card effect on the previous OBSERVED
    /// frame. It is a field, and it is SEEDED TRUE when a card arrives in this recess, because
    /// <c>CardEffects.HasEffect</c> is a LEVEL that long outlives its own animation —
    /// <c>toggledEffects</c> is added to synchronously by <c>ToggleAdditiveEffect</c> and is only
    /// ever emptied by <c>RestoreCard()</c>, i.e. when the card returns to the Hand pile
    /// (CardEffects.cs:404-444, FullAbilityCard.SetPile:313-337). A card that is ALREADY flagged the
    /// first time this slot sees it must therefore NOT plume: its animation belongs to a moment this
    /// recess did not witness, and the owner is not seeing one either. Seeding true makes arrival a
    /// non-edge in both directions — an arriving card that is quiet drops the latch on its first
    /// observed frame and can plume later, which is the case that actually matters.
    /// </summary>
    private bool _plumeRunning = true;

    /// <summary><c>CardInstanceID</c> the plume latches belong to (<c>int.MinValue</c> = nothing
    /// seated). A recess is a POSITION: two different cards passing through it share nothing but the
    /// index, so every latch above is re-seeded when this changes.</summary>
    private int _plumeKey = int.MinValue;

    /// <summary>The card the plume state above belongs to, latched by <see cref="Set"/> on the SAME
    /// change gate as the face so the two can never be about different cards.</summary>
    private CAbilityCard? _plumeCard;

    /// <summary>The actor whose hand holds <see cref="_plumeCard"/>'s widget — latched beside it,
    /// read-only, never written to (the same contract <see cref="Set"/>'s own owner has).</summary>
    private CPlayerActor? _plumeOwner;

    /// <summary>Host transform for a spawned plume — see <see cref="PlumeAnchor"/> for why the slot
    /// root itself is the wrong parent. Built on the first spawn, never before.</summary>
    private Transform? _plumeAnchor;

    /// <summary>One-shot latch (per session, not per slot) for the "source widget resolved" evidence
    /// line — see <see cref="TickPlume"/> for what its presence and its absence each prove.</summary>
    private static bool s_plumeSourceLogged;

    /// <summary>
    /// Mirror the GAME's own card plume (wire id <see cref="NetProtocol.TuneGameCardParticlesOn"/>)
    /// onto this ROUND-SLOT slab while the card lying in it is running a burn / lost / discard
    /// effect on the owner's screen. Called PER FRAME by <see cref="RemoteControlBoard"/>; the
    /// active-card column, which shares this class, never calls it — an active card is a standing
    /// summary, not a card the game plays an effect on.
    ///
    /// <para>THE OWNER REALLY DOES SEE THIS ONE, which is what makes it a 1:1 gap rather than a
    /// feature that only ever existed in a hand. A played card is HELD in its recess while the
    /// game's burn timeline runs on it and only then flies to the pile (user ruling 2026-08-03,
    /// <c>CardsDriver.HoldForBurnArtwork</c>), and the local <c>VRCard</c> ticks
    /// <c>Cards.BurnCardFx</c> on that docked card (<c>VRCard.cs:1996</c>).</para>
    ///
    /// <para>THE TRIGGER IS <c>Cards.BurnCardFx</c>'s PREDICATE, term for term
    /// (<c>HasEffect(BurnCard) || HasEffect(LostMode) || HasEffect(DiscardMode)</c>), read off the
    /// peer's own live widget. The owner's picture and this one agree because they are the same
    /// expression over the same kind of object, not because two formulas were made to match.
    /// EDGE-triggered — see <see cref="_plumeRunning"/>, and note that the level here is far
    /// longer-lived than the hand's (a discarded card stays flagged until the next round returns it
    /// to the Hand pile), which is what makes that latch load-bearing rather than an optimisation.</para>
    ///
    /// <para>NOT <c>CardEffects._smokeEffect</c>, which is what <c>BurnCardFx</c> binds locally:
    /// <c>SpawnParticle</c> opens with <c>base.gameObject.activeInHierarchy</c>
    /// (CardEffects.cs:743) and a peer's widget lives in a hand the game keeps DEACTIVATED, so that
    /// field is null on this client for every remote card. That is precisely why the receiver has to
    /// instantiate its own tamed copy (<see cref="RemoteCardPlume"/>) instead of adopting one.</para>
    ///
    /// <para>ANTI-CHEAT, twice over. <paramref name="allowed"/> already folds in
    /// <c>RevealGate.ShowRoundCardFronts</c>, and the check below additionally requires that the
    /// face currently drawn on this slab was cloned from THIS widget — so a plume can never appear
    /// on a face-down recess, where it would name WHICH card the owner is doing something to.</para>
    ///
    /// <para>The VIEWER's own <c>[Cards] GameCardParticles</c> is not consulted anywhere on this
    /// path; <paramref name="allowed"/> carries the OWNER's bit and nothing else. See
    /// <c>Cards.CardDustFx.Permission</c> for the defect that rule exists to prevent.</para>
    /// </summary>
    public void TickPlume(bool allowed, int playerId, int slot)
    {
        CAbilityCard? card = _plumeCard;
        CPlayerActor? owner = _plumeOwner;
        if (!allowed || card == null || owner == null)
        {
            ResetPlume();
            return;
        }

        int key = PlumeKeyOf(card);
        if (key != _plumeKey)
        {
            _plumeKey = key;
            _plumeSource = null;
            _plumeSourceMissing = false;
            _plumeRunning = true;   // arrival is not an edge — see _plumeRunning
        }

        bool running = false;
        bool observed = false;
        try
        {
            if (_plumeSource == null && !_plumeSourceMissing)
            {
                // THE SAME RESOLVE THAT DREW THE FACE, not a second one. RemoteAbilityCardSource
                // was made internal for exactly this call: two independent lookups that merely
                // AGREE is the failure mode this project names outright, so there is one answer
                // with two consumers.
                _plumeSource = RemoteAbilityCardSource.TryLiveWidget(owner, card);
                _plumeSourceMissing = _plumeSource == null;
            }
            FullAbilityCard? full = _plumeSource;
            // THE IDENTITY CHECK, and it is what makes reading an effect off that widget legitimate:
            // ShowsKey is true only when the face on THIS slab was cloned from THIS widget's
            // instance id, so the plume and the picture are provably about one card. It also
            // disposes of the pooled-borrow face for free — that path's key belongs to a widget the
            // game's pool has already taken back.
            CardEffects? fx = full != null && _art != null && _art.ShowsKey(full.GetInstanceID())
                ? full.cardEffects
                : null;
            if (fx != null)
            {
                observed = true;
                running = fx.HasEffect(CardEffects.FXTask.BurnCard)
                          || fx.HasEffect(CardEffects.FXTask.LostMode)
                          || fx.HasEffect(CardEffects.FXTask.DiscardMode);
            }
        }
        catch (System.Exception)
        {
            observed = false;   // any deref failure means "no plume", never a throw
        }

        // AN UNOBSERVABLE FRAME IS NOT A FALLING EDGE. Returning without touching the latch is the
        // difference between "the widget went away for a frame" and "the burn ended": dropping the
        // latch to false there would arm a spurious spawn on the frame it comes back.
        if (!observed)
            return;

        if (!s_plumeSourceLogged)
        {
            s_plumeSourceLogged = true;
            VRLog.Info("Net", $"Remote card plume SOURCE [player {playerId}] slot {slot}: the peer's " +
                              "own live AbilityCardUI for the card in this ROUND RECESS is resolved " +
                              "AND verified — RemoteCardArt.ShowsKey confirms the face drawn on this " +
                              "slab was cloned from that very widget, so the plume and the picture " +
                              "cannot be about two different cards. Its CardEffects is readable and " +
                              "the burn/lost/discard predicate is now sampled per frame. A log that " +
                              "carries this line and never a 'Remote card plume' spawn line proves " +
                              "the game runs no card effect on a PEER's widget on this client — i.e. " +
                              "the TRIGGER would need a wire field, not the effect.");
        }

        if (running == _plumeRunning)
            return;
        _plumeRunning = running;
        if (!running)
            return;   // the falling edge only re-arms the latch; the plume ends on its own
        RemoteCardPlume.Spawn(PlumeAnchor(), playerId, slot);
    }

    /// <summary>Card identity for the plume latch, guarded because the model can be mid-teardown —
    /// the same shape (and reason) as <see cref="OwnerKey"/>.</summary>
    private static int PlumeKeyOf(CAbilityCard card)
    {
        try { return card.CardInstanceID; }
        catch { return int.MinValue; }
    }

    /// <summary>Drop every plume latch — an empty recess, an anonymous back, a blanked board. Already
    /// SPAWNED hosts are deliberately not touched: each owns its own timed destroy, and a card
    /// leaving the recess mid-burn is exactly the moment the owner's own plume is still finishing.
    /// </summary>
    private void ResetPlume()
    {
        _plumeKey = int.MinValue;
        _plumeSource = null;
        _plumeSourceMissing = false;
        _plumeRunning = true;
    }

    /// <summary>
    /// The transform a spawned plume hangs off: a child of the slot root whose LOCAL SCALE restates
    /// this slot's card in the frame <see cref="RemoteCardPlume"/> is written against.
    ///
    /// <para>WHY NOT THE SLOT ROOT ITSELF — this is the whole reason the method exists.
    /// RemoteCardPlume pins the plume to its parent's world scale (taming item 4), which is correct
    /// on the HAND fan because there the slab root's scale IS the card's size: that fan sets
    /// <c>localScale = cardWidth / DefaultCardWidth</c> and hangs a body mesh authored at the
    /// nominal width off it. A BOARD slot is built the other way round — the quad carries the width
    /// in METRES (<c>BoardVisual.Quad</c> scales a unit primitive) and the slot root hangs off the
    /// board prefab's raw recess anchor, whose scale <see cref="RemoteControlBoard"/>'s own seat
    /// conversion says outright is scene data this mod cannot read. Parenting the plume there would
    /// size the game's SCREEN-authored prefab by an arbitrary number — at anchor scale 1 that is a
    /// ~16x plume, i.e. exactly the field-covering fog <c>Cards.BurnCardFx</c>'s removal note is
    /// about. This child restores the hand's relationship exactly (world scale over card world width
    /// is <c>1 / DefaultCardWidth</c> on both surfaces) and needs no <c>lossyScale</c> read and no
    /// assumption about the anchor, because the ratio is taken in the root's OWN local frame.</para>
    ///
    /// <para><c>RemoteHandFan.DefaultCardWidth</c> is referenced rather than copied deliberately: a
    /// duplicated literal is what makes two surfaces diverge the day one of them is retuned, and the
    /// number's only meaning here is "the card width a plume host scale of 1 means on the hand".</para>
    /// </summary>
    private Transform? PlumeAnchor()
    {
        if (_plumeAnchor != null)
            return _plumeAnchor;
        var go = new GameObject("PlumeAnchor");
        Transform t = go.transform;
        t.SetParent(_root.transform, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one * (_width / RemoteHandFan.DefaultCardWidth);
        _plumeAnchor = t;
        return t;
    }

    // ---------------------------------------------------------- half hover/selection glow --

    /// <summary>The two half glow quads (top / bottom action region), built lazily on the first
    /// synced hover/selection so a board whose owner never touches a half allocates nothing.</summary>
    private GameObject? _halfGlowTop;
    private GameObject? _halfGlowBottom;

    /// <summary>The glow quads' material + renderer refs (index 0 = bottom, 1 = top), captured
    /// at build time so the per-frame pulse never calls GetComponent.</summary>
    private Material?[] _halfGlowMats = System.Array.Empty<Material?>();

    /// <summary>The shared telegraph gold — the additive Overlay shader emits RGB as brightness
    /// (alpha only matters on the Sprites/Default fallback), so the pulse scales both, exactly
    /// like <c>PlayTray.SlotPulse</c>.</summary>
    private static readonly Color GlowGold = new(1f, 0.85f, 0.3f, 0.95f);

    /// <summary>Currently shown hover half (-1 none, 0 bottom, 1 top) — change gate.</summary>
    private int _shownHalfHover = -1;

    /// <summary>Currently shown SELECTED half (-1 none, 0 bottom, 1 top) — change gate.</summary>
    private int _shownHalfSelected = -1;

    /// <summary>The shown HOVER is on that half's standard-action chip rather than the big half
    /// (record 14 byte 2 bit 0) — part of the change gate and of the evidence line.</summary>
    private bool _shownHoverDefault;

    /// <summary>The shown SELECTION is on that half's standard-action chip.</summary>
    private bool _shownSelectedDefault;

    /// <summary>
    /// Drive the two-state half highlight (extras extension record 14 — "worüber hovert mein
    /// Mitspieler" + the follow-up "welche Hälfte hat er GEKLICKT"). Called every frame by the
    /// owning board. <paramref name="hoverHalf"/> / <paramref name="selectedHalf"/>:
    /// -1 none, 0 bottom, 1 top.
    ///
    /// ─── THE GAME'S OWN HIGHLIGHT, NOT A MOD RECTANGLE (user report 2026-08-08) ────────────────
    /// "Das Synchronisieren des Mouseovers über eine Action einer Karte in der Aktionsauswahlphase
    /// highlighted ein ganzes Rechteck der Hälfte der Karte bei dem Remote-Spieler. Es soll so
    /// angezeigt werden, wie der Spieler selbst es auch sieht, also das Highlighting vom Spiel
    /// selbst auf der jeweiligen Karte."
    ///
    /// The first revision drew two additive gold QUADS sized off <c>HalfSelection</c>'s zone
    /// fractions — 96 % × 42 % of the card, i.e. literally "ein ganzes Rechteck der Hälfte der
    /// Karte". Nothing about that is what the owner sees: the game frames the action REGION with
    /// <c>CardActionHighlight</c>, a 9-sliced highlight <c>Image</c> authored into the card prefab
    /// with an angular shine sweep, pulsing (alpha 1↔0.3 over 0.5 s legs) for a hover and steady
    /// at full alpha for a click.
    ///
    /// It does not have to be imitated, because IT IS ALREADY HERE. A face-up slot on a remote
    /// board hosts a real <c>Object.Instantiate</c> clone of the game's own <c>FullAbilityCard</c>
    /// widget (<see cref="RemoteCardArt"/>), and that clone keeps its <c>FullAbilityCardAction</c>
    /// and <c>CardActionHighlight</c> components — only the raycasters and <c>CardEffects</c> are
    /// stripped. So the mirror now calls the GAME's own <c>ShowHover</c> / <c>ShowSelected</c> /
    /// <c>Hide</c> on the clone's own highlight objects: same sprite, same shader, same shine
    /// sweep, same LeanTween cadence, same rect, frame for frame the owner's picture. This is the
    /// remote twin of the LOCAL fix (<c>CardsDriver.SetActiveHighlight</c> →
    /// <c>FullAbilityCard.ToggleHighlightHover</c>), which retired the same overlay quads on the
    /// player's own cards for the same complaint.
    ///
    /// FALLBACK, and only there: a slot that is NOT showing a real card face (a card back, an
    /// anonymous back, or the last-resort parchment name panel) has no <c>CardActionHighlight</c>
    /// to drive — there is no game card in the scene to highlight. Those keep the mod quads, so
    /// "the owner is pointing at the top half of this slot" stays visible rather than silently
    /// disappearing. <see cref="HighlightPath"/> reports which of the two is live, per slot.
    ///
    /// ─── WHICH REGION OF THE HALF (user report 2026-08-15, item 6) ────────────────────────────
    /// "Wenn jemand die standart Aktion ausgewählt hat oder drüber hovered wird trotzdem der große
    /// untere bzw obere Bereich der Karte bei den remote boards angezeigt/gehighlighted, also nicht
    /// richtig synchronisiert. Eventuell wurden die kleinen Standartaktionsfelder hier vergessen zu
    /// implementieren? Auch die sind wichtig."
    ///
    /// A half is TWO clickable regions, not one: the big action half, and the small standard-action
    /// chip ("Attack 2" / "Move 2") inside it. The game frames them with TWO
    /// <c>CardActionHighlight</c>s on the SAME <c>FullAbilityCardAction</c> — <c>highlightAction</c>
    /// and <c>highlightDefaultAction</c> — and <c>RefreshHighlight</c> shows exactly one. This
    /// mirror only ever drove the first, so a peer's standard action lit the whole half. The wire
    /// now names the region (<see cref="NetProtocol.HalfDefaultHoverBit"/>) and
    /// <paramref name="hoverDefault"/> / <paramref name="selectedDefault"/> pick the matching
    /// highlight object — the same widget, the same shader, the correct rectangle.
    ///
    /// POSITIONS only — no card data is read, and the clone is never told which card it is.
    /// </summary>
    public void SetHalfStates(int hoverHalf, int selectedHalf,
                              bool hoverDefault = false, bool selectedDefault = false)
    {
        _shownHalfHover = hoverHalf;
        _shownHalfSelected = selectedHalf;
        _shownHoverDefault = hoverHalf >= 0 && hoverDefault;
        _shownSelectedDefault = selectedHalf >= 0 && selectedDefault;

        if (TryDriveGameHighlight(hoverHalf, selectedHalf))
        {
            // Never both: a slot that upgraded from a back to a real face must not keep the quad
            // it lit while it was a back.
            HideModGlows();
            LogHighlightPathIfChanged();
            return;
        }

        if ((hoverHalf >= 0 || selectedHalf >= 0) && _halfGlowTop == null)
            BuildHalfGlows();
        if (_halfGlowTop == null || _halfGlowBottom == null)
        {
            LogHighlightPathIfChanged();
            return;
        }

        DriveHalf(_halfGlowBottom, 0, hoverHalf, selectedHalf);
        DriveHalf(_halfGlowTop, 1, hoverHalf, selectedHalf);
        LogHighlightPathIfChanged();
    }

    /// <summary>Last (top, bottom) dimming this slot pushed onto its clone — the self-heal key.
    /// Null before the first push, so a fresh clone is always written once.</summary>
    private (bool top, bool bottom)? _appliedSpent;

    /// <summary>Change gate for the receiver's own <c>SPENT HALF</c> line.</summary>
    private string _loggedSpent = string.Empty;

    /// <summary>
    /// ITEM 8 (2026-09-05): DRAW THE HALVES THIS PLAYER HAS ALREADY USED, exactly as their own
    /// screen draws them. User, verbatim: "wenn die Karte grau wird weil sie schon benutzt wurde …
    /// So ist auch für die Mitspieler ersichtlich, welche der beiden Karten bereits
    /// benutzt/verbrannt wurde. Die 1:1 Regel verlangt es."
    ///
    /// <para>MIRROR THE REAL THING, DO NOT REBUILD IT. The dimming is made by the GAME'S OWN call —
    /// <c>FullAbilityCard.ToggleSideInteractivity(active, actionType)</c> →
    /// <c>FullAbilityCardAction.SetInteractable</c> → <c>canvasGroup.alpha = active ? 1f : 0.5f</c>
    /// (FullAbilityCardAction.cs:334) — made on the mirrored clone, which is a real
    /// <c>Object.Instantiate</c> copy of the owner's own widget and still carries both
    /// <c>FullAbilityCardAction</c>s. Same group, same alpha, same rect, and the header, title and
    /// initiative disc stay bright because they sit outside that group, precisely as on the owner's
    /// board. Nothing is imitated and no second look is invented. The clone's raycasters are
    /// already stripped, so the <c>interactable</c> half of that call has no reachable effect.</para>
    ///
    /// <para>THE THIRD MECHANISM THAT WAS ERASING THIS. Two were on the wire (nothing carried the
    /// bit; the peer's identity list is DRAINED as halves are played, so <c>_latchedFaces</c>
    /// re-showed a FRESH face). The third is ours: <c>Cards.Art.CardHalfTone.Normalize</c> runs
    /// <c>SetInteractable(true)</c> on every mod-owned clone to undo a POOLED widget's inherited
    /// dimming — a correct fix for a different problem, and it would wipe this one out on the next
    /// face rebuild. So the mirror takes a HOLD on the face rather than racing it: one writer owns
    /// the final value, which is the rule this project already pays for breaking.</para>
    ///
    /// <para>SELF-HEALING on the same principle as <see cref="ApplyHalf"/>: the write is re-asserted
    /// whenever the clone is rebuilt (a new <c>_faceCardKey</c> clears the applied state), so a
    /// re-pooled or re-instantiated face comes back dimmed rather than silently bright.</para>
    /// </summary>
    public void SetSpentHalves(int playerId, int slot, bool topSpent, bool bottomSpent)
    {
        try
        {
            if (Path == RemoteAbilityCardSource.FacePath.None)
            {
                ReleaseSpentHold();
                return;
            }
            FullAbilityCard? face = ResolveFaceCard();
            if (face == null)
                return;

            // The hold goes down FIRST and every frame the state is asserted: CardHalfTone must
            // never see this face as "needs correction" in the window between the clone being
            // rebuilt and the dim being re-applied.
            CardHalfTone.HoldMirroredDim(face, topSpent || bottomSpent);

            if (_appliedSpent.HasValue && _appliedSpent.Value == (topSpent, bottomSpent))
                return;
            _appliedSpent = (topSpent, bottomSpent);
            face.ToggleSideInteractivity(!topSpent, CBaseCard.ActionType.TopAction);
            face.ToggleSideInteractivity(!bottomSpent, CBaseCard.ActionType.BottomAction);
            LogSpentIfChanged(playerId, slot, topSpent, bottomSpent);
        }
        catch (System.Exception ex)
        {
            // A face mid-rebuild draws UNDIMMED — the picture this surface had before record 41 —
            // and never takes down the per-frame board tick this runs inside.
            VRLog.Warn("Net", $"Remote board card: the owner's spent-half dimming could not be " +
                              $"mirrored ({ex.Message}) — this slot draws both halves bright.");
            ReleaseSpentHold();
        }
    }

    /// <summary>Drop the hold and forget the applied state — a slot that no longer shows a real
    /// face has no halves to dim and must not keep CardHalfTone standing down for it.</summary>
    private void ReleaseSpentHold()
    {
        _appliedSpent = null;
        if (_faceCard != null)
            CardHalfTone.HoldMirroredDim(_faceCard, false);
    }

    /// <summary>
    /// HARDWARE VERIFICATION (item 8), the RECEIVER edge. Grep token: <c>SPENT HALF</c> — the SAME
    /// token the owner's <c>SPENT HALF SENT</c> line carries, so one grep across the two logs
    /// decides the 1:1 question with no arithmetic: for one moment the owner's mask and this line
    /// must name the same recess and the same half.
    ///
    /// <para>FALSIFIER: this line present naming a half while the user still reports both cards
    /// looking identical means the bits and the call both landed and something re-brightened the
    /// clone afterwards — the hold is the term to check, and <c>CardHalfTone</c>'s census prints
    /// the alpha range that settles it.</para>
    /// </summary>
    private void LogSpentIfChanged(int playerId, int slot, bool topSpent, bool bottomSpent)
    {
        string now = $"{topSpent}|{bottomSpent}|{Path}";
        if (now == _loggedSpent)
            return;
        _loggedSpent = now;
        // HW-VERIFY: grep token "SPENT HALF". PROOF = this line and the owner's "SPENT HALF SENT"
        // naming the same recess and half. FALSIFIER = this line present and the halves still
        // looking identical: the hold lost to CardHalfTone. See this method's doc.
        VRLog.Note("Net", $"SPENT HALF [player {playerId}] recess {slot + 1}: top="
            + $"{topSpent}, bottom={bottomSpent} on a slot whose face={Path} (record 41). TRUE draws "
            + "that half at alpha 0.5 through the GAME'S OWN FullAbilityCard.ToggleSideInteractivity "
            + "on the mirrored clone — the identical call and the identical CanvasGroup the owner's "
            + "screen uses, so the header, title and initiative disc stay bright on both boards. "
            + "Compare with that player's own 'SPENT HALF SENT' line: the two naming different "
            + "halves IS the 1:1 breach this record exists to close.");
    }

    /// <summary>Last logged (path, hover, selected) triple — the change gate for the line below.</summary>
    private string _loggedHighlight = string.Empty;

    /// <summary>
    /// State ONCE per real change which mechanism lit this slot. The regression this guards is
    /// exactly the reported one: "the peer sees a big rectangle" is `mod-quad` on a slot whose
    /// `face=` says a real card is up — a contradiction a grep for `Remote board card highlight`
    /// finds in the hardware log without a screenshot.
    /// </summary>
    private void LogHighlightPathIfChanged()
    {
        string now = $"{HighlightPath}|{_shownHalfHover}|{_shownHalfSelected}|" +
                     $"{_shownHoverDefault}|{_shownSelectedDefault}|{Path}";
        if (now == _loggedHighlight)
            return;
        _loggedHighlight = now;
        if (HighlightPath == "none")
            return; // "nothing is lit" is the resting state, not news
        string half(int v, bool def) => v == 1 ? (def ? "TOP standard-action field" : "TOP half")
            : v == 0 ? (def ? "BOTTOM standard-action field" : "BOTTOM half")
            : "none";
        VRLog.Info("Net", $"Remote board card highlight: path={HighlightPath} " +
                          $"(hover {half(_shownHalfHover, _shownHoverDefault)}, " +
                          $"clicked {half(_shownHalfSelected, _shownSelectedDefault)}) " +
                          $"on a slot whose face={Path} — 'game' means the owner's own " +
                          "CardActionHighlight on the mirrored widget (record 14; the chip's " +
                          "highlightDefaultAction when the region is a standard-action field, the " +
                          "half's highlightAction otherwise); 'mod-quad' is the back/fallback " +
                          "stand-in and is only correct while face=None.");
    }

    // ------------------------------------------------ the game's own action highlight --

    /// <summary>Which mechanism lit this slot's last half state — surfaced so a hardware log
    /// PROVES the owner's own highlight is what a peer sees, instead of merely proving something
    /// glowed. "game" = the clone's <c>CardActionHighlight</c>, "mod-quad" = the back/fallback
    /// stand-in, "none" = nothing lit.</summary>
    public string HighlightPath { get; private set; } = "none";

    /// <summary>The clone's own card widget, re-resolved whenever the hosted face is rebuilt.
    /// Unity-null aware: a destroyed clone must re-resolve, never be dereferenced.</summary>
    private FullAbilityCard? _faceCard;

    /// <summary>Instance id of the widget <see cref="_faceCard"/> was resolved from — the
    /// change key that survives a same-card rebuild.</summary>
    private int _faceCardKey;

    /// <summary>Last frame the clone was searched for — one probe per frame, at most.</summary>
    private int _faceProbeFrame = -1;

    /// <summary>Highlight materials we minted for the clone. Index 0/1 = bottom/top BIG-half
    /// highlight, index 2/3 = bottom/top STANDARD-ACTION chip highlight — the chip's own
    /// <c>CardActionHighlight</c> writes the same shared shine-width property, so it needs the same
    /// isolation. See <see cref="IsolateHighlightMaterials"/>.</summary>
    private readonly Material?[] _highlightMats = new Material?[4];

    /// <summary>Last state pushed per half (index 0 = bottom, 1 = top): -1 nothing, 0 hover,
    /// 1 selected. Re-asserted whenever it disagrees with the highlight object's ACTUAL active
    /// flag, which is what makes this self-healing against the clone's own
    /// <c>FullAbilityCardAction.OnEnable → Show() → highlight.Hide()</c>.</summary>
    private readonly int[] _appliedHighlight = { -1, -1 };

    /// <summary>Which REGION of each half the last push lit (index 0 = bottom, 1 = top): true =
    /// the standard-action chip's highlight, false = the big half's. Part of the write gate — the
    /// state can stay "selected" while the region flips, and that flip is the whole of item 6.</summary>
    private readonly bool[] _appliedDefault = { false, false };

    /// <summary>
    /// Drive the clone's own <c>CardActionHighlight</c> pair. Returns false when this slot has no
    /// real card face (back / anonymous back / parchment fallback), which is the ONLY case the mod
    /// quads still serve.
    ///
    /// Wrapped whole: a clone caught mid-rebuild, a card prefab variant without one of the two
    /// highlights, a widget the pool reclaimed — all degrade to "no game highlight", never take
    /// down the per-frame board tick this runs inside.
    /// </summary>
    private bool TryDriveGameHighlight(int hoverHalf, int selectedHalf)
    {
        try
        {
            if (Path == RemoteAbilityCardSource.FacePath.None)
            {
                ForgetGameHighlight();
                return false;
            }
            FullAbilityCard? face = ResolveFaceCard();
            if (face == null)
                return false;

            bool bottom = ApplyHalf(face.bottomActionButton, 0, hoverHalf, selectedHalf);
            bool top = ApplyHalf(face.topActionButton, 1, hoverHalf, selectedHalf);
            if (!bottom && !top)
                return false; // widget without highlights: let the quads speak rather than nothing

            HighlightPath = hoverHalf < 0 && selectedHalf < 0 ? "none" : "game";
            return true;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Net", $"Remote board card: the game's own action highlight could not be " +
                              $"driven ({ex.Message}) — this slot falls back to the mod half quad.");
            ForgetGameHighlight();
            return false;
        }
    }

    /// <summary>
    /// Push one half's wanted state onto the clone's highlight, using the GAME's own methods so
    /// the pulse, the shine width and the alpha curve are the owner's and not an imitation.
    /// Returns false when this half has no highlight object at all.
    ///
    /// SELF-HEALING re-assert: the write is gated on the last pushed state AND on the highlight
    /// object's real <c>activeSelf</c>. That second term matters — the clone's own
    /// <c>FullAbilityCardAction.OnEnable</c> calls <c>Show()</c>, which hides both highlights, so
    /// a purely state-gated driver would go dark for good the first time the face host re-enabled.
    /// A hover that is already running is left alone, so the game's LeanTween loop is never
    /// restarted mid-cycle (which would visibly re-snap the alpha to 1 every frame).
    /// </summary>
    private bool ApplyHalf(FullAbilityCardAction? action, int half, int hoverHalf, int selectedHalf)
    {
        CardActionHighlight? big = action != null ? action.highlightAction : null;
        CardActionHighlight? chip = action != null ? action.highlightDefaultAction : null;
        if (big == null && chip == null)
            return false;

        int want = selectedHalf == half ? 1 : hoverHalf == half ? 0 : -1;
        // WHICH of this half's two regions the owner named. The selection wins on a doubly-lit
        // half, exactly as the game's own RefreshHighlight parks the selected look over the
        // hover's — so the region question follows the same winner.
        bool wantDefault = want == 1 ? _shownSelectedDefault
            : want == 0 && _shownHoverDefault;
        // A card prefab without the chip's highlight cannot show a chip glow. Falling back to the
        // BIG half's highlight there would re-create the exact defect this fixes (a whole half lit
        // for a standard action), so it falls back to NOTHING instead: less than the owner sees,
        // never something different from what the owner sees.
        CardActionHighlight? target = want < 0 ? null : wantDefault ? chip : big;

        // The region can flip while the STATE holds (the beam slides off the half onto its chip:
        // still "hover", different rectangle), so the gate is on BOTH. `isOn` is read from the
        // object we are about to write, which is what keeps the self-heal against the clone's own
        // FullAbilityCardAction.OnEnable → Show() → highlight.Hide().
        bool wantOn = target != null;
        bool isOn = target != null && target.gameObject.activeSelf;
        bool gated = want == _appliedHighlight[half]
                     && wantDefault == _appliedDefault[half]
                     && wantOn == isOn;

        if (!gated)
        {
            _appliedHighlight[half] = want;
            _appliedDefault[half] = wantDefault;
            if (target != null)
            {
                if (want == 1)
                    target.ShowSelected(); // steady, selectedShineWidth — the committed region
                else
                    target.ShowHover();    // the game's own 1↔0.3 LeanTween loop, hoverShineWidth
            }
        }

        // NEVER BOTH AT ONCE, and never a leftover: the same exclusivity
        // FullAbilityCardAction.RefreshHighlight keeps between its two highlights on the owner's
        // own card. Run unconditionally (not only on a change) because the object that has to go
        // dark is the one the gate above is NOT watching — a region flip and a hover ending are
        // both cases where the previously lit highlight would otherwise stay up.
        Park(want < 0 || !ReferenceEquals(big, target) ? big : null);
        Park(want < 0 || !ReferenceEquals(chip, target) ? chip : null);
        return true;

        static void Park(CardActionHighlight? hl)
        {
            if (hl != null && hl.gameObject.activeSelf)
                hl.Hide();
        }
    }

    /// <summary>
    /// Give the clone's two highlight <c>Image</c>s their OWN material instance.
    ///
    /// WHY THIS IS NOT OPTIONAL: <c>CardActionHighlight.ShowHover/ShowSelected</c> write
    /// <c>imageHighlight.material.SetFloat("_AngularHighlightWidth", …)</c>, and
    /// <c>Graphic.material</c> is the SHARED asset (unlike <c>Renderer.material</c>, it does not
    /// instantiate). Driving a peer's mirrored card would therefore reach through the shared
    /// material and re-write the shine width on the LOCAL player's own cards — a peer's hover
    /// silently restyling your hand. One instance per clone closes that: identical pixels,
    /// private state. The instances are ours and are destroyed with the face.
    /// </summary>
    private void IsolateHighlightMaterials(FullAbilityCard card)
    {
        _highlightMats[0] = IsolateOne(card.bottomActionButton, chip: false);
        _highlightMats[1] = IsolateOne(card.topActionButton, chip: false);
        // The STANDARD-ACTION chip's highlight (item 6) writes the same shared shine-width
        // property from the same ShowHover/ShowSelected, so it needs the same isolation — driving
        // it un-isolated would restyle the local player's own chips.
        _highlightMats[2] = IsolateOne(card.bottomActionButton, chip: true);
        _highlightMats[3] = IsolateOne(card.topActionButton, chip: true);

        static Material? IsolateOne(FullAbilityCardAction? action, bool chip)
        {
            CardActionHighlight? hl = action == null ? null
                : chip ? action.highlightDefaultAction : action.highlightAction;
            UnityEngine.UI.Image? img = hl != null ? hl.imageHighlight : null;
            Material? shared = img != null ? img.material : null;
            if (img == null || shared == null)
                return null;
            var owned = new Material(shared) { name = shared.name + " (RemoteBoardCard)" };
            img.material = owned;
            return owned;
        }
    }

    /// <summary>Destroy the material instances minted for the previous clone.</summary>
    private void ReleaseHighlightMaterials()
    {
        for (int i = 0; i < _highlightMats.Length; i++)
        {
            if (_highlightMats[i] != null)
                Object.Destroy(_highlightMats[i]);
            _highlightMats[i] = null;
        }
    }

    /// <summary>
    /// THE ONE PLACE the mirrored clone's own <c>FullAbilityCard</c> is found, and the one place
    /// the per-clone bookkeeping that hangs off it is reset. Both per-frame drivers - the action
    /// highlight (record 14) and the spent-half dimming (record 41) - go through it, because a
    /// second probe that resolved the widget WITHOUT touching <see cref="_faceCardKey"/> would let
    /// a rebuilt clone keep the previous one's isolated highlight materials and its previous
    /// applied state: whichever driver ran first that frame would silently disarm the other.
    ///
    /// <para>Probed at most once a frame: a face path that somehow has no card widget must not turn
    /// a per-frame drive into a per-frame hierarchy search. The clone lives under <c>_root</c>
    /// (slot, then the RemoteCardArt host, then the clone), one per slot.</para>
    /// </summary>
    private FullAbilityCard? ResolveFaceCard()
    {
        if (_faceCard != null)
            return _faceCard;
        if (_faceProbeFrame == Time.frameCount)
            return null;
        _faceProbeFrame = Time.frameCount;
        _faceCard = _root.GetComponentInChildren<FullAbilityCard>(includeInactive: true);
        if (_faceCard == null)
            return null;
        int key = _faceCard.GetInstanceID();
        if (key != _faceCardKey)
        {
            _faceCardKey = key;
            ReleaseHighlightMaterials();
            IsolateHighlightMaterials(_faceCard);
            _appliedHighlight[0] = _appliedHighlight[1] = -1; // a fresh clone starts dark
            _appliedDefault[0] = _appliedDefault[1] = false;
            _appliedSpent = null;  // ...and undimmed, so item 8's write is re-asserted onto it
        }
        return _faceCard;
    }

    /// <summary>Drop every reference into a face that is gone (or never was), so the next call
    /// re-resolves from scratch instead of touching a destroyed clone.</summary>
    private void ForgetGameHighlight()
    {
        if (_faceCard == null && _faceCardKey == 0)
            return;
        ReleaseHighlightMaterials();
        // Item 8: the mirrored dim is released WITH the face, and its applied state is forgotten so
        // the next clone is written once rather than being assumed already dimmed.
        if (_faceCard != null)
            CardHalfTone.HoldMirroredDim(_faceCard, false);
        _appliedSpent = null;
        _faceCard = null;
        _faceCardKey = 0;
        _appliedHighlight[0] = _appliedHighlight[1] = -1;
        _appliedDefault[0] = _appliedDefault[1] = false;
        HighlightPath = "none";
    }

    /// <summary>Park both mod quads (they exist only on slots that once had no real face).</summary>
    private void HideModGlows()
    {
        if (_halfGlowTop != null && _halfGlowTop.activeSelf)
            _halfGlowTop.SetActive(false);
        if (_halfGlowBottom != null && _halfGlowBottom.activeSelf)
            _halfGlowBottom.SetActive(false);
    }

    /// <summary>Per-half state resolve + write for the BACK/FALLBACK stand-in only (a slot showing
    /// a real card face drives the game's own highlight instead — see
    /// <see cref="TryDriveGameHighlight"/>): selected → steady, hovered → pulse, else off. Active
    /// flips are change-gated; the colour write runs only while a pulse is showing (at most one
    /// half per card) or on the steady half's first frame.</summary>
    private void DriveHalf(GameObject glow, int half, int hoverHalf, int selectedHalf)
    {
        bool selected = selectedHalf == half;
        bool hovered = hoverHalf == half;
        bool on = selected || hovered;
        if (glow.activeSelf != on)
            glow.SetActive(on);
        if (on)
            HighlightPath = "mod-quad";
        else if (half == 1 && hoverHalf < 0 && selectedHalf < 0)
            HighlightPath = "none";
        if (!on)
            return;
        Material? mat = half < _halfGlowMats.Length ? _halfGlowMats[half] : null;
        if (mat == null)
            return;
        // Selection wins on a doubly-lit half — the steady latch is the stronger statement,
        // matching the local RefreshHighlight, which parks the selected look over the hover's.
        float k;
        if (selected)
        {
            k = 1f;
        }
        else
        {
            // The game's hover loop: LeanTween alpha 1 → 0.3 → 1 at 0.5 s per leg = a 1 s
            // cycle. A sine at that period reads identically at glow scale.
            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f);
            k = Mathf.Lerp(0.3f, 1f, t);
        }
        Color c = GlowGold;
        c.r *= k;
        c.g *= k;
        c.b *= k;
        c.a *= k; // the alpha-blended Sprites/Default fallback (Overlay absent) pulses too
        if (mat.color != c)
            mat.color = c;
    }

    /// <summary>Build the two glow quads + capture their materials (lazy — first hover/click).</summary>
    private void BuildHalfGlows()
    {
        var size = new Vector3(_width * Cards.HalfSelection.ZoneWidthFrac,
                               _height * Cards.HalfSelection.ZoneHeightFrac, 1f);
        float centerY = _height * Cards.HalfSelection.ZoneCenterYFrac;
        const float glowZ = -0.004f; // in front of the face art's ~1.4 mm standoff
        _halfGlowTop = CardGlow.CreateGlowQuad("HalfHoverTop", _root.transform,
            size, new Vector3(0f, centerY, glowZ), GlowGold);
        _halfGlowBottom = CardGlow.CreateGlowQuad("HalfHoverBottom", _root.transform,
            size, new Vector3(0f, -centerY, glowZ), GlowGold);
        _halfGlowMats = new Material?[2];
        _halfGlowMats[0] = CaptureGlow(_halfGlowBottom);
        _halfGlowMats[1] = CaptureGlow(_halfGlowTop);
    }

    /// <summary>
    /// The material ref the pulse writes. DRAW ORDER is no longer forced here: the glow is 4 mm
    /// PROUD of the face art it belongs to (glowZ above), and the owning board's cluster seats both
    /// from their board-local depth (BoardVisual.AdoptBoardOrder) — a tie inside one tier, which
    /// Unity breaks by distance, i.e. in the glow's favour at every angle. The old constant said
    /// the same thing with a number that also outranked half the board.
    /// </summary>
    private static Material? CaptureGlow(GameObject glow)
    {
        var mr = glow.GetComponent<MeshRenderer>();
        return mr != null ? mr.sharedMaterial : null; // CreateGlowQuad mints one material per quad
    }

    /// <summary>Tear the hosted face down and forget which path drew it (the back/empty states must
    /// never report a fidelity path they are not showing).</summary>
    private void ClearFace()
    {
        // The clone (and its CardActionHighlight objects, and the material instances we minted for
        // them) dies with the face — every reference into it must go FIRST, or the next
        // SetHalfStates would drive a destroyed widget.
        ForgetGameHighlight();
        _art?.HideFront();
        Path = RemoteAbilityCardSource.FacePath.None;
    }

    /// <summary>Destroy the slot's face host. Called when the owning board is torn down: the host is
    /// a child of <c>_root</c> and dies with it either way, but going through
    /// <see cref="RemoteCardArt.Destroy"/> keeps the "we own the clone, we destroy the clone"
    /// contract explicit rather than relying on hierarchy destruction order.</summary>
    public void Destroy()
    {
        ForgetGameHighlight(); // drops the highlight material instances we minted for the clone
        _art?.Destroy();
        _art = null;
        Path = RemoteAbilityCardSource.FacePath.None;
        // The anchor is a child of _root and dies with it, but the FIELD must go too — a rebuilt
        // board would otherwise parent its first plume under a destroyed transform.
        _plumeAnchor = null;
        _plumeCard = null;
        _plumeOwner = null;
        ResetPlume();
        // The group lives on _root and dies with it; the FIELD must go for the same reason the
        // plume anchor's does, and the ramp flags with it so a rebuilt board starts settled.
        _faceGroup = null;
        _appearing = false;
        _vanishing = false;
        _materialiseSeeded = false;
        _shownId = int.MinValue;
        _shownOwner = int.MinValue;
        _shownEmpty = true;
        _shownFront = false;
    }

    /// <summary>Cheap, stable change key for "whose card is this" — the actor id, guarded because the
    /// actor can be mid-teardown. Only used to invalidate the slot when the owner changes.</summary>
    private static int OwnerKey(CPlayerActor? owner)
    {
        if (owner == null)
            return int.MinValue;
        try { return owner.ID; }
        catch { return int.MinValue; }
    }

    /// <summary>Readable card name: <c>CAbilityCard.Name</c> (localized YML name), stripped of the
    /// <c>ABILITY_CARD_</c> loc prefix when present (as the game's own <c>StrictName</c> does).
    /// Guarded — a YML lookup miss degrades to "?".</summary>
    private static string DisplayName(CAbilityCard card)
    {
        string name;
        try { name = card.Name ?? "?"; }
        catch { return "?"; }
        const string prefix = "ABILITY_CARD_";
        return name.StartsWith(prefix, System.StringComparison.Ordinal)
            ? name.Substring(prefix.Length)
            : name;
    }

    // ------------------------------------------------------- appear / crumble materialise --

    /// <summary>
    /// The peer whose board this recess belongs to, or null for the active-card column — see the
    /// constructor for why ONE field answers both "does this slot ramp?" and "may it puff dust?".
    /// Read-only, exactly like <see cref="_plumeOwner"/>: this is presentation, it never writes.
    /// </summary>
    private readonly RemoteAvatar? _materialiseOwner;

    /// <summary>True while the incoming card is fading in (<c>VRCard.DockAppearSeconds</c>, 0.28 s
    /// at the shipped default), false while the outgoing one crumbles
    /// (<c>VRCard.DockVanishSeconds</c>, 0.30 s). Mutually exclusive by construction — every entry
    /// point clears the other — which is why one elapsed counter serves both.</summary>
    private bool _appearing;
    private bool _vanishing;
    private float _rampElapsed;

    /// <summary>
    /// THE NO-STORM SEED, and it is the mirror of <c>CardsDriver</c>'s <c>_dockAnimSuppressed</c>
    /// rather than an invention. A remote board is torn down and rebuilt on any record-28 change,
    /// and <see cref="Blank"/> runs every time the board stops being drawn (visibility off, a peer
    /// without a board, the ActionPhaseOnly setting during the secret selection phase). Without
    /// this latch every one of those would materialise the peer's whole round afresh — an animation
    /// the owner is emphatically NOT seeing, because their board never went away. So the FIRST card
    /// a recess shows after construction or a blank is seeded silently; every later arrival ramps.
    /// </summary>
    private bool _materialiseSeeded;

    /// <summary>
    /// The alpha carrier for the hosted REAL card face. ONE <see cref="CanvasGroup"/> on the slot
    /// root: its alpha multiplies down through the nested world-space canvas
    /// <see cref="RemoteCardArt"/> builds, so the clone's OWN group (minted by
    /// <c>RemoteCardArt.Neutralize</c> to kill input) is never written to and the two cannot fight.
    /// That is the identical device — and the identical reason — as <c>PeerBoardFade</c>'s single
    /// group on the board root, which already fades these very card faces.
    ///
    /// <para>Built lazily on the first ramp, so the active-card column allocates nothing. It is
    /// authored INERT (both interaction flags off) because the board is contractually inert and a
    /// group defaulting to <c>blocksRaycasts = true</c> has no business appearing above a subtree
    /// that was deliberately made non-blocking.</para>
    /// </summary>
    private CanvasGroup? _faceGroup;

    // THE SETTLE SCALE the card grows from / shrinks to while the fade carries the transition is
    // VRCard.DustSettleScale, REFERENCED and not copied. It was the one number in this ramp that
    // had to be a duplicated literal, and only because the owner's was a `private const`; that
    // access modifier was widened to `internal` in the same change. Everything else the ramp is
    // made of already came from the owner directly (VRCard.DockAppearSeconds,
    // VRCard.DockVanishSeconds, VRCard.SmootherStep), for the reason PlumeAnchor already states
    // about RemoteHandFan.DefaultCardWidth: a duplicated literal is what makes two surfaces
    // diverge the day one of them is retuned.

    /// <summary>One-shot evidence line for the whole feature — see <see cref="TickMaterialise"/> for
    /// what its presence and its absence each prove.</summary>
    private static bool s_materialiseLogged;

    /// <summary>
    /// Advance this recess's materialise by one frame. Driven by <see cref="MaterialisePump"/>,
    /// a component on the slot root — this class is plain C# and its two owners refresh content on
    /// a 4 Hz cadence, which is far too coarse for a 0.3 s ramp, so the pump is how a slot ticks
    /// itself without either owner having to learn about it. Same device, same file family, as
    /// <c>RemoteCapFx</c> in <c>RemoteBoardFurniture</c>.
    ///
    /// <para>THE CURVE, THE DURATIONS AND THE HITCH CAP ARE THE OWNER'S, term for term:
    /// <c>VRCard.SmootherStep</c> over <c>DockAppearSeconds</c> / <c>DockVanishSeconds</c>, on
    /// UNSCALED time with the same 0.05 s per-frame cap (card phases pause <c>timeScale</c>, so a
    /// scaled clock would freeze the mirror while the owner's card still animated). Referencing
    /// them rather than matching their numbers is the point — the two ramps are identical because
    /// they are the same expression, not because two formulas were made to agree.</para>
    ///
    /// <para>WHAT IT DOES NOT DO: it never touches game state, it never throws (every write below
    /// is to an object this class minted), and a slot whose root is deactivated under it simply
    /// stops ticking — invisible either way, and the next <see cref="Set"/> or <see cref="Blank"/>
    /// resolves the state. There is deliberately no watchdog: a stalled ramp has no visible
    /// residue to guard against, unlike <c>RemoteCapFx</c>'s, whose cap stays on screen.</para>
    /// </summary>
    internal void TickMaterialise()
    {
        if (!_appearing && !_vanishing)
            return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap — VRCard.Update's own
        _rampElapsed += dt;
        if (_vanishing)
        {
            float vt = VRCard.DockVanishSeconds > 0f
                ? Mathf.Clamp01(_rampElapsed / VRCard.DockVanishSeconds) : 1f;
            ApplyMaterialise(1f - VRCard.SmootherStep(vt));
            if (vt < 1f)
                return;
            _vanishing = false;
            ClearFace();              // the recess is empty NOW; the face has finished saying so
            ApplyMaterialise(1f);     // …and the slot's next life starts opaque and full-size
            if (_root.activeSelf)
                _root.SetActive(false);
            return;
        }
        float at = VRCard.DockAppearSeconds > 0f
            ? Mathf.Clamp01(_rampElapsed / VRCard.DockAppearSeconds) : 1f;
        ApplyMaterialise(VRCard.SmootherStep(at));
        if (at < 1f)
            return;
        _appearing = false;
        ApplyMaterialise(1f);
    }

    /// <summary>
    /// A card (or an anonymous back) just landed in this recess: seed the appear, or — on the very
    /// first content after a build/blank — seed the SILENCE. Both branches are one statement each
    /// and they are kept together so the no-storm rule has exactly one place to be wrong in.
    /// </summary>
    private void ArriveMaterialise()
    {
        if (!_materialiseSeeded)
        {
            _materialiseSeeded = true;
            EndMaterialise(); // a card arriving straight into a running crumble ends it flat
            return;
        }
        BeginAppear();
    }

    /// <summary>
    /// Start the materialise: transparent and settled-small THIS instant (so no fully opaque frame
    /// can render first), then the dust. Order copied from <c>VRCard.PlayAppear</c> — the motes are
    /// emitted at the pose the card has once the settle scale is applied, not before it.
    /// </summary>
    private void BeginAppear()
    {
        EnsureFaceGroup();
        _appearing = true;
        _vanishing = false;
        _rampElapsed = 0f;
        ApplyMaterialise(0f);
        EmitMirroredCardDust(appear: true);
        LogMaterialiseOnce();
    }

    /// <summary>
    /// Start the crumble: the recess keeps drawing exactly what it was drawing (frame zero is the
    /// settled state) and the dust goes NOW, because the owner's <c>VRCard.Vanish</c> emits before
    /// its fade rather than at the end of it.
    /// </summary>
    private void BeginVanish()
    {
        EnsureFaceGroup();
        _vanishing = true;
        _appearing = false;
        _rampElapsed = 0f;
        ApplyMaterialise(1f);
        EmitMirroredCardDust(appear: false);
        LogMaterialiseOnce();
    }

    /// <summary>Abandon a running ramp at its SETTLED state — full alpha, full scale. Every path
    /// that takes the recess away from the ramp (a blank, a teardown, a card arriving into a
    /// crumble) goes through here, or the slot is stranded half-faded and 18 % small forever. Free
    /// when nothing is running, which is every call the active column would ever make.</summary>
    private void EndMaterialise()
    {
        if (!_appearing && !_vanishing)
            return;
        _appearing = false;
        _vanishing = false;
        _rampElapsed = 0f;
        ApplyMaterialise(1f);
    }

    /// <summary>
    /// Write one point of the ramp. <paramref name="visible"/> is the owner's own smootherstepped
    /// <c>s</c> (appear) or <c>1 - s</c> (crumble) — the two are the same number read from opposite
    /// ends, which is why one method serves both directions.
    ///
    /// <para>THREE FAMILIES, because a recess draws three kinds of thing and each takes alpha its
    /// own way — the same split <c>PeerBoardFade</c> documents for the board as a whole:</para>
    /// <list type="bullet">
    /// <item>the hosted REAL card face (uGUI) — the <see cref="CanvasGroup"/> on the slot root;</item>
    /// <item>the slab quad — its material's own colour alpha. ALL THREE materials are written even
    ///   though only one is on the renderer, and that is load-bearing: <see cref="Set"/> swaps
    ///   between them while a ramp can be running, and a material left at alpha 1 would snap the
    ///   card back to opaque the moment the swap happened. They are per-slot instances minted in
    ///   the constructor, so nothing outside this recess can see the write — unlike the owner, who
    ///   cannot fade its slab at all because <c>CardMesh</c>'s materials are shared with every card
    ///   in the scene (which is exactly why <c>VRCard</c> hides its body instead);</item>
    /// <item>the two mod-drawn 3D labels — <c>TextMeshPro.alpha</c>. They are MeshRenderers, not
    ///   canvas graphics, so the group above does not reach them and they cannot be double-faded
    ///   by it either.</item>
    /// </list>
    ///
    /// <para>The scale settle rides the slot ROOT. Residue, stated: <see cref="PlumeAnchor"/> hangs
    /// off that root, so a game plume spawned during the 0.3 s ramp would be up to 18 % small.
    /// Transient, and the alternative — a second transform between the root and everything else —
    /// costs more than it buys.</para>
    /// </summary>
    private void ApplyMaterialise(float visible)
    {
        float a = Mathf.Clamp01(visible);
        if (_faceGroup != null)
            _faceGroup.alpha = a;
        SetQuadAlpha(_backMat, a);
        SetQuadAlpha(_faceMat, a);
        SetQuadAlpha(_bodyMat, a);
        _initLabel.alpha = a;
        _nameLabel.alpha = a;
        _root.transform.localScale = Vector3.one * Mathf.Lerp(VRCard.DustSettleScale, 1f, a);
    }

    /// <summary>Scale one slot material's OWN colour alpha. Read-modify-write rather than a stored
    /// base, because the three colours are authored once in the constructor and never repainted —
    /// so the alpha channel is the only thing here that ever moves.</summary>
    private static void SetQuadAlpha(Material m, float alpha)
    {
        Color c = m.color;
        if (Mathf.Approximately(c.a, alpha))
            return;
        c.a = alpha;
        m.color = c;
    }

    /// <summary>Mint the slot root's fade carrier — see <see cref="_faceGroup"/> for why it goes
    /// on the ROOT and not on the face host.</summary>
    private void EnsureFaceGroup()
    {
        if (_faceGroup != null)
            return;
        _faceGroup = _root.GetComponent<CanvasGroup>();
        if (_faceGroup == null)
            _faceGroup = _root.AddComponent<CanvasGroup>();
        _faceGroup.interactable = false;
        _faceGroup.blocksRaycasts = false;
    }

    /// <summary>
    /// The mod's own crumble / materialise puff for this recess, at the slab's current world pose.
    ///
    /// <para>THE FRAME IS <c>VRCard.EmitCardDust</c>'s, term for term — right/up off the slab, the
    /// out-normal <c>-forward</c> because a card's +Z points AWAY from the viewer, and half extents
    /// converted to world by <c>lossyScale</c>. AND THIS IS THE ONE PLACE WHERE A BOARD SLOT AND A
    /// HAND SLAB LEGITIMATELY DIFFER, which <see cref="PlumeAnchor"/> exists because of: the plume
    /// needed a scale RATIO, and a board recess hangs off a prefab anchor whose scale is scene data
    /// this mod cannot read, so it needed a corrective host. The dust takes world LENGTHS instead,
    /// and <c>_width</c>/<c>_height</c> are metres in the slot root's own local frame — so
    /// <c>lossyScale</c> IS the exact and complete conversion, whatever the anchor carries, and no
    /// anchor is needed. (<c>RemoteHandFan.EmitMirroredCardDust</c> reads the same way for the same
    /// reason, one frame over.)</para>
    ///
    /// <para>THE GATE IS THE OWNER'S <c>CardDustOn</c> BIT AND NOTHING ELSE — wire id
    /// <see cref="NetProtocol.TuneCardDustOn"/>, already decoded as
    /// <c>RemoteBoardTuning.CardDustOn</c>, no new field owed. The viewer's own <c>[Cards]
    /// CardDust</c> dial is not consulted, which is what <c>Cards.CardDustFx.Permission</c> makes
    /// the call site say out loud: that dial answers "do MY OWN cards puff?", and ANDing the two is
    /// precisely the defect that left the mirrored HAND dust inert for its whole shipped life.
    /// Read straight off the avatar rather than cached: <c>BoardTuning</c> is a wide struct, but
    /// this is asked twice in a card's entire life in the recess, not per card per frame the way
    /// the fan asks it.</para>
    ///
    /// <para>The TONE is <c>CardDustFx.DefaultTone</c> — the wire carries no per-card tone, and the
    /// emitter owns that number precisely so a mirror cannot hold a diverging copy of it.</para>
    /// </summary>
    private void EmitMirroredCardDust(bool appear)
    {
        RemoteAvatar? owner = _materialiseOwner;
        if (owner == null)
            return;
        bool allowed;
        try { allowed = owner.BoardTuning.CardDustOn; }
        catch { return; } // an avatar mid-teardown means "no dust", never a throw on a ramp frame
        if (!allowed)
            return;
        Transform t = _root.transform;
        float lossy = t.lossyScale.x;
        float halfW = _width * 0.5f * lossy;
        float halfH = _height * 0.5f * lossy;
        if (halfW < 1e-4f || halfH < 1e-4f)
            return;
        if (appear)
            CardDustFx.EmitAppear(t.position, t.right, t.up, -t.forward, halfW, halfH,
                                  CardDustFx.DefaultTone, CardDustFx.Permission.OwnerAlreadySaidYes);
        else
            CardDustFx.EmitVanish(t.position, t.right, t.up, -t.forward, halfW, halfH,
                                  CardDustFx.DefaultTone, CardDustFx.Permission.OwnerAlreadySaidYes);
    }

    /// <summary>One line, once per session, on the first ramp any recess plays. Its ABSENCE from a
    /// hardware log is the diagnosis: no line at all means no round-slot recess was ever
    /// constructed with an owner (the call site has not opted in), which is a different failure
    /// from "it ran and looked wrong".</summary>
    private void LogMaterialiseOnce()
    {
        if (s_materialiseLogged)
            return;
        s_materialiseLogged = true;
        VRLog.Info("Net", "Remote round-card materialise ARMED: a peer's played card now fades in " +
                          $"over {VRCard.DockAppearSeconds:F2}s and crumbles out over " +
                          $"{VRCard.DockVanishSeconds:F2}s in its recess, on the owner's own curve " +
                          "(VRCard.SmootherStep, unscaled), instead of popping. The dust puff rides " +
                          "the OWNER's [Cards] CardDust bit (wire id 233) and is silent when they " +
                          "have it off — the viewer's own dial is deliberately not a term. The " +
                          "active-card column shares this class and is NOT ramped: its owner plays " +
                          "no appear on an active card.");
    }
}

/// <summary>
/// The per-frame pump for one recess's materialise ramp. It exists because
/// <see cref="RemoteBoardCard"/> is plain C# with no <c>Update</c> of its own and both of its
/// owners repaint content on a 4 Hz cadence — one or two samples across a 0.3 s fade, i.e. the pop
/// the ramp exists to remove. Rather than make either owner learn about the ramp, a slot that has
/// one carries its own tick, exactly as <c>RemoteCapFx</c> does for the mirrored keycaps in
/// <c>RemoteBoardFurniture</c>.
///
/// <para>It rides the slot ROOT, so it stops ticking whenever that root is deactivated — which is
/// correct in both directions: an inactive recess draws nothing to animate, and the crumble is the
/// one case that needs the root to stay up, which <see cref="RemoteBoardCard.Set"/> guarantees by
/// deferring the <c>SetActive(false)</c> to the end of the ramp.</para>
/// </summary>
internal sealed class MaterialisePump : MonoBehaviour
{
    /// <summary>The recess this pump drives. Assigned once, at build time.</summary>
    internal RemoteBoardCard? Slot;

    private void Update() => Slot?.TickMaterialise();
}
