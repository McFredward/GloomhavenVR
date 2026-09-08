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

    /// <summary>
    /// THE CARD BODY'S REAL RECTANGLE — <c>CardFace.VisibleFaceRect(_width, _height)</c>, i.e. the
    /// rectangle the printed face actually paints inside the nominal <see cref="_width"/> ×
    /// <see cref="_height"/> card box, and therefore the rectangle the OWNER's own card body is
    /// scaled to (<c>VRCard.SetCanvasSize</c> fits its backing to exactly this product).
    ///
    /// <para>WHY THIS FIELD EXISTS (2026-09-06 report item 5, verbatim: "Die Größe der visiblen
    /// Karten auf dem remote board ist falsch (das mesh ist größer), dadurch bekommt die Karte auf
    /// dem Board so einen Rand und auch der braune Overlay ist dann als Rechteck visibel").
    /// This slot used to build its quad at the FULL nominal card box and let
    /// <see cref="RemoteCardArt"/> letterbox the cloned face inside it. The game's ability face is
    /// 294 × 450 px — aspect 0.6533 — against the card's 63.5 : 88, i.e. 0.7216, so a Min() fit
    /// leaves the print 85.1 % of the box WIDE and 94.0 % of it TALL: a rim of bare body on every
    /// side, four times wider at the sides than at the ends. On the shipped numbers of this
    /// session's logs (extension record 11, "Slot-card size RECEIVED from player 2: card 156.8 mm")
    /// that is a 156.8 × 217.3 mm slab around a 133.4 × 204.3 mm print — 11.7 mm of rim per side.
    /// The owner's own slot card has no rim at all, because his body IS the 133.4 × 204.3 mm print
    /// rect; both clients' logs state that number in the same words ("the face PAINTS
    /// 54.04x82.72 mm — which is the size every card BODY … is scaled to").</para>
    ///
    /// <para>IT IS THE THIRD INSTANCE OF ONE DEFECT, and that is why the number is read from
    /// <c>CardFace</c> rather than spelled here: <c>VRCard</c> fixed it for the local card,
    /// <c>RemoteHandFan</c> for the peer's hand (user report 12 of 2026-08-15, the same picture on
    /// a different surface), and this slot was simply never converted. Every other mirrored card
    /// surface in the mod already asks <c>CardFace.VisibleFaceRect</c>; this one now does too.</para>
    /// </summary>
    private Vector2 _bodyRect;

    /// <summary>The <c>CardFace.FacePixelsRevision</c> <see cref="_bodyRect"/> was computed at.
    /// Revision-gated rather than value-compared for the same reason <c>RemoteHandFan.SyncFaceRect</c>
    /// is: this client learns the real 294 × 450 face size the first time it hosts an ability card of
    /// its own, which may be AFTER a spectator has already seen a peer's board.</summary>
    private int _bodyRectRevision = int.MinValue;

    /// <summary>One line per process for the recess body rect (see <see cref="SyncBodyRect"/>).
    /// STATIC: one line means "at least one recess reached this size", never "exactly one did".</summary>
    private static bool s_loggedBodyRect;

    /// <summary>Where <see cref="Move"/> was last told to seat this panel, in the parent's local
    /// space, and whether the panel is still on its way there. See <see cref="TickGlide"/> for the
    /// expression and the 1:1 reason it exists at all.</summary>
    private Vector3 _glideTo;
    private bool _gliding;

    /// <summary>The OWNER's own <c>[Cards] CardLerpSpeed</c>, handed in by <see cref="Move"/> — a
    /// mirror may never read the viewer's dial. Seeded to the shipped default so a glide asked for
    /// before that peer's tuning record has arrived still runs at the authored rate rather than
    /// at zero (which would strand the panel between two cells).</summary>
    private float _glideSpeed = Defaults.CardLerpSpeed;

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
    /// <c>CAbilityCard.CardInstanceID</c> of the card whose REAL FACE this recess is drawing right
    /// now, or <see cref="int.MinValue"/> for anything else.
    ///
    /// <para>All three qualifiers are load-bearing and the caller (<c>Net.RemoteBurnFx</c>) needs
    /// every one of them: it hands its own flight over to this recess only while the recess is
    /// really showing THAT card's face. An empty recess, a face-DOWN one and an anonymous back are
    /// each a recess that is not showing the card, and answering with the id anyway would hide the
    /// flight behind a picture that is not there.</para>
    /// </summary>
    public int ShownFaceCardInstanceId =>
        !_shownEmpty && _shownFront && Path != RemoteAbilityCardSource.FacePath.None
            ? _shownId
            : int.MinValue;

    /// <summary>
    /// Is this panel ALREADY holding <paramref name="cardInstanceId"/>? Deliberately blind to the
    /// face, the owner and the reveal gate, because the question it answers is about the OBJECT and
    /// not about the picture: <see cref="Move"/> needs "is the thing that is about to be seated here
    /// the thing that is already here", which is the mirror's reading of the owner's
    /// <c>ActivePileViewer._seated</c> membership test. An empty panel answers false for every id,
    /// including the anonymous-back key, which is what an empty cell should answer.
    /// </summary>
    public bool ShowsCard(int cardInstanceId) =>
        !_shownEmpty && cardInstanceId != int.MinValue && _shownId == cardInstanceId;

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

        // THE BODY IS THE PRINT'S OWN RECTANGLE, NOT THE NOMINAL CARD BOX — see _bodyRect. The quad
        // is built at unit size and SyncBodyRect carries the rect on its localScale, so the same one
        // expression serves the build and the (rare) later revision.
        _bg = BoardVisual.Quad(_root.transform, "Face", Vector2.one, _backMat);
        SyncBodyRect();
        Vector2 body = _bodyRect;

        // The fallback parchment panel rides the BODY, not the nominal box: it is drawn on this same
        // quad, so a label sized to the box would overhang the card it is printed on.
        _initLabel = RemoteBoardContent.Label(_root.transform, "Initiative",
            new Vector3(0f, body.y * 0.34f, -0.001f),
            new Vector2(body.x * 0.9f, body.y * 0.28f), 0.09f,
            new Color(0.12f, 0.10f, 0.08f), TextAlignmentOptions.Center, FontStyles.Bold);
        _nameLabel = RemoteBoardContent.Label(_root.transform, "Name",
            new Vector3(0f, -body.y * 0.12f, -0.001f),
            new Vector2(body.x * 0.86f, body.y * 0.5f), 0.045f,
            new Color(0.14f, 0.11f, 0.09f), TextAlignmentOptions.Center, FontStyles.Normal, wrap: true);

        // The ramp's own clock — only for a recess that has one (see the constructor doc), so the
        // active-card column pays nothing for an animation it must not play.
        if (materialiseOwner != null)
            _root.AddComponent<MaterialisePump>().Slot = this;

        // THE RE-SEAT GLIDE's clock. On EVERY panel because Move() is a property of the PANEL and
        // not of one of its two roles — the active column is its only caller today (R3's F4), and a
        // pump added only for that caller is a rule the next caller has to remember. It costs a
        // no-op Update on a recess: TickGlide early-returns while nothing has asked this panel to
        // move, and only Move() can set that flag. Separate from the pump above because the two are
        // separate animations — the materialise is a recess-only appear/crumble ramp with an owner
        // to ask about dust, the glide is a layout move with neither.
        _root.AddComponent<GlidePump>().Slot = this;

        // Start HIDDEN and in step with the _shownEmpty seed: Set() early-returns while nothing
        // changed, so a panel that never receives a card (an unused active-grid cell, an empty
        // round slot) must not be left standing here showing a card back.
        _root.SetActive(false);
    }

    /// <summary>
    /// Re-seat the panel (the active-card grid relays its cards as the pile changes) — INSTANTLY for
    /// a cell that is taking a different card, and on the OWNER'S OWN HOME GLIDE for one that is
    /// keeping the card it already has.
    ///
    /// <para>WHAT WAS MEASURED (2026-09-07 review R3, F4). This was a bare
    /// <c>localPosition</c> write, and the owner's counterpart is not:
    /// <c>ActivePileViewer.Relayout</c> asks <c>_seated.Add(card)</c> and passes
    /// <c>instant: instant || arriving</c> to <c>VRCard.SetHome</c> — an ARRIVAL is seated, a
    /// RESIDENT glides (<c>ActivePileViewer.cs:293-299</c>). So when a peer activated a second
    /// persistent card, his own first card slid sideways to make room while every mirror of that
    /// board jumped it to the new cell, up to one content-cadence tick late. The arrival half of
    /// that same line was closed in ModBuild 479; this is the other half. 1:1 covers ANIMATION,
    /// not only the end state.</para>
    ///
    /// <para>THE CALLER SAYS WHICH, AND IT SAYS IT FROM THE CARD. <paramref name="instant"/> is the
    /// mirror's reading of the owner's <c>arriving</c> term: a cell that is about to be handed a
    /// card it is not already showing is this column's version of "this column has never asserted a
    /// home for it". It is a parameter rather than a branch in here because only the caller holds
    /// the card that is about to go in, and because a cell-indexed mirror cannot answer the owner's
    /// question in the one case where the two models genuinely differ — see
    /// <c>RemoteActiveCards.Refresh</c>, which carries that limit.</para>
    ///
    /// <para><paramref name="lerpSpeed"/> is the OWNER's <c>[Cards] CardLerpSpeed</c>
    /// (<c>NetProtocol.TuneCardLerpSpeed</c>, off his synced <c>BoardTuning</c>), never this
    /// client's — a mirror may not read the viewer's dial, and this is the very number the owner's
    /// own glide runs at.</para>
    /// </summary>
    public void Move(Vector3 localPos, bool instant, float lerpSpeed)
    {
        _glideTo = localPos;
        _glideSpeed = lerpSpeed > 0f ? lerpSpeed : Defaults.CardLerpSpeed;
        if (instant)
        {
            _gliding = false;
            _root.transform.localPosition = localPos;
            return;
        }
        // Already there (the ordinary case — this runs on every content refresh, and a column that
        // has not re-centred asks for the seat it is already in): no glide to start, and no
        // per-frame lerp toward a point we are standing on.
        _gliding = (localPos - _root.transform.localPosition).sqrMagnitude > GlideEpsilonSq;
        if (!_gliding)
            _root.transform.localPosition = localPos;
    }

    /// <summary>Square of the distance at which a glide is finished — 0.1 mm, well under a pixel at
    /// any board scale this mod draws, and the same order as the residual an exponential lerp leaves
    /// behind for ever if nothing snaps it.</summary>
    private const float GlideEpsilonSq = 1e-8f;

    /// <summary>
    /// Advance an in-progress re-seat by one frame — the OWNER's own home-lerp expression,
    /// <c>1 - exp(-CardLerpSpeed * dt)</c>, called rather than re-derived in the sense that matters:
    /// it is the same closed form <c>VRCard.Update</c> runs on his card (<c>VRCard.cs:2291</c>) with
    /// HIS speed, so the two curves are identical because they are one formula and not because two
    /// numbers were made to agree.
    ///
    /// <para>UNSCALED TIME, WITH <c>VRCard.Update</c>'S OWN 0.05 s HITCH CAP. The owner's home lerp
    /// reads <c>Time.deltaTime</c>, but <c>Time.timeScale</c> is a VIEWER-LOCAL value on this
    /// machine — the local player's own pause menu, his own card phase — and a mirror may never
    /// read the viewer's dial. The sibling ramp in this same class (<see cref="TickMaterialise"/>)
    /// took the identical decision for the identical reason. At <c>timeScale == 1</c>, which is
    /// every frame of ordinary play, the two clocks are the same clock.</para>
    ///
    /// <para>Driven by <see cref="GlidePump"/>: this class is plain C# and its owners refresh
    /// content on a 4 Hz cadence, which cannot carry a glide. A cell whose root is deactivated under
    /// it simply stops — invisible either way, and the next <see cref="Move"/> re-asserts the
    /// seat.</para>
    /// </summary>
    internal void TickGlide()
    {
        if (!_gliding)
            return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap — VRCard.Update's own
        Transform t = _root.transform;
        Vector3 next = Vector3.Lerp(t.localPosition, _glideTo, 1f - Mathf.Exp(-_glideSpeed * dt));
        if ((next - _glideTo).sqrMagnitude <= GlideEpsilonSq)
        {
            next = _glideTo;
            _gliding = false;
        }
        t.localPosition = next;
    }

    /// <summary>
    /// Mip-bake upkeep for a hosted real face (see <see cref="RemoteCardArt.MaintainMipBake"/>).
    /// <see cref="Set"/> is change-gated, so without this the clone would only ever get the single
    /// build-time sprite swap and its ASYNC header art would stay mipless — the "extreme aliasing"
    /// the user reported on remote cards. Called by the owners on their 4 Hz content cadence;
    /// self-early-returns while no front is up, so it is free on backs/empty slots.
    /// </summary>
    public void MaintainMips()
    {
        SyncBodyRect();
        _art?.MaintainMipBake();
    }

    /// <summary>
    /// Scale the body quad to the rectangle the print actually paints — see <see cref="_bodyRect"/>
    /// for the defect and the arithmetic. One int compare while nothing has moved, which is what
    /// makes it safe on the board's 4 Hz cadence and in the constructor alike.
    /// </summary>
    private void SyncBodyRect()
    {
        if (_bodyRectRevision == CardFace.FacePixelsRevision && _bodyRect.x > 0f)
            return;
        _bodyRectRevision = CardFace.FacePixelsRevision;
        Vector2 vis = CardFace.VisibleFaceRect(_width, _height);
        if (vis.x <= 0f || vis.y <= 0f)
            return;
        _bodyRect = vis;
        if (_bg != null)
            _bg.transform.localScale = new Vector3(vis.x, vis.y, 1f);
        if (s_loggedBodyRect)
            return;
        s_loggedBodyRect = true;
        // HW-VERIFY: report item 5. Grep token: REMOTE RECESS CARD RECT.
        // WORKING = "rim 0.0 x 0.0 mm" and the three sizes equal. INERT = the line absent while a
        // peer's board card still shows a rim (this method never ran). STILL BEYOND THE INSTRUMENT =
        // a non-zero rim, which means the print is NOT being fitted by CardFace's own product and
        // the lead is RemoteCardArt.FitClone rather than this quad.
        VRLog.Note("Net", "REMOTE RECESS CARD RECT: this peer's board recess is drawn at "
            + $"nominal card box {_width * 1000f:F1}x{_height * 1000f:F1} mm (slot-root local, "
            + "extension record 11 — the owner's own slot-card size), the game's ability face is "
            + $"{CardFace.ObservedFacePixels.x:F0}x{CardFace.ObservedFacePixels.y:F0} px, so the "
            + $"PRINT is fitted to {vis.x * 1000f:F1}x{vis.y * 1000f:F1} mm and the card BODY is now "
            + $"scaled to exactly that — rim {(_width - vis.x) * 500f:F1} x "
            + $"{(_height - vis.y) * 500f:F1} mm per side. WHY THE THREE NUMBERS: the face's aspect "
            + "(0.653 on the shipped 294x450 widget) is not the card's (63.5:88 = 0.722), so a "
            + "Min() letterbox leaves the print 85 % of the box wide and 94 % of it tall. Until "
            + "ModBuild 462 this quad was built at the NOMINAL box and the difference showed as a "
            + "rim of bare card body four times wider at the sides than at the ends — user item 5, "
            + "'das mesh ist größer … dadurch bekommt die Karte so einen Rand'. The OWNER has no "
            + "rim because VRCard.SetCanvasSize fits his backing to this same product; his own log "
            + "states it as the [Cards] CARD FACE RECT line ('the face PAINTS 54.04x82.72 mm — "
            + "which is the size every card BODY … is scaled to'). A rim above 0.1 mm here means "
            + "the two builders have drifted again, which is what this line exists to catch.");
    }

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
        // …and the used-card look's own copy of that reference, so a board that stopped being drawn
        // holds no game widget alive across a scene change. Its key guard would have re-resolved
        // anyway; this is hygiene, not correctness.
        _fxSource = null;
        _fxSourceMissing = false;
        _fxSourceKey = int.MinValue;
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
    ///
    /// <para>─── THE TWO FLAGS STAY TWO, AND THE GAME IS WHY (2026-09-06) ─────────────────────────
    /// The user reported that "die gesamte Karte" greys, which is true of the END of a turn and
    /// raised the question of whether a HALF-dimmed peer card is a state its owner never has. It is
    /// not — the game reaches it deliberately, mid-turn, and it is the more informative half of what
    /// this record carries:
    /// <list type="bullet">
    /// <item><c>CardsActionControlller</c>, <c>Phase.Pick1stTarget</c> (:289-290): the moment the
    ///   owner picks his first action, the played card's OTHER half is switched off with the
    ///   per-<c>actionType</c> overload while the half he is playing stays bright. Same again on
    ///   <c>Phase.Select2ndCard</c> (:305-308) for the second card.</item>
    /// <item>Only at <c>Finish()</c> (:420-421) and <c>AfterItemUseAtEndOfTurn()</c> (:432-435) does
    ///   the game call the PARAMETERLESS <c>SetInteractable(false)</c> / both
    ///   <c>ToggleSideInteractivity</c> calls, i.e. the whole card. That is the picture the user
    ///   described, and it is the last frame of the sequence, not the only one.</item>
    /// </list>
    /// The ModBuild 457 host log walks exactly that sequence: <c>SPENT HALF SENT</c> masks go
    /// <c>0x00 → 0x09 → 0x0B → 0x0F → 0x00</c> across a turn, and <c>0x09</c> is one half of each
    /// card. Collapsing the flags would erase the mid-turn state the owner really sees. They also
    /// cannot invent one: <c>HalfSelection.SpentHalvesOf</c> MEASURES the owner's own rendered
    /// <c>canvasGroup.alpha</c> per half, so a half-dimmed peer card can only exist while the
    /// owner's own card is half-dimmed. Verified, not assumed — and left exactly as it was.</para>
    ///
    /// <para>WHAT THIS METHOD IS NOT is the whole-card look. These CanvasGroups cover the two action
    /// halves only; the header, title and initiative disc sit outside them. The look that takes the
    /// WHOLE card is <c>CardEffects</c>' own timeline, and it is driven from
    /// <see cref="DriveUsedCardFx"/> below.</para>
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

            // THE WHOLE-CARD LOOK, driven BEFORE the change gate below: it is a RAMP and needs
            // every frame, while the dimming below is a one-shot write that only has to land on an
            // edge. Two different cadences, one call site.
            DriveUsedCardFx(playerId, slot, topSpent, bottomSpent);

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

    // -------------- the PERMANENT burnt wash on a peer's ACTIVATED, LOST-BOUND card -------------

    /// <summary>True while this slot is holding the active-column burnt wash on its face.</summary>
    private bool _activeWashOn;

    /// <summary>Change gate for the line below: (card instance id, look) last reported.</summary>
    private (int Card, RemoteCardArt.CardFxLook Look) _loggedActiveWash =
        (0, RemoteCardArt.CardFxLook.None);

    /// <summary>
    /// RULE 1 ON THE MIRROR - an ACTIVATED card wears the PERMANENT look its destination earns on
    /// every board, and KEEPS it: the burnt wash when it is bound for LOST (rule 1a), the grey-out
    /// when it is bound for DISCARD (rule 1b).
    ///
    /// <para>THE GHOST HALF IS ModBuild 479, and the ruling is the user's own: <i>"Einmal grau
    /// bleibt die Karte (remote UND lokal) grau solange sie im aktiven Stapel liegt. Gleiches gilt
    /// fuer eine verbrannte Karte dort."</i> ModBuild 478 mirrored only the BURN, so a Discard-bound
    /// activated card went blue on the mirror at the same round boundary it went blue on the
    /// owner's board - one defect, two surfaces, and both halves are now one expression
    /// (<c>Cards.BurnLookPolicy.ForActivatedCard</c>) asked over the same local model object.</para>
    ///
    /// <para>USER ITEM 4, his correction of the first attempt, verbatim: <i>"Nein du hast Bahn C
    /// falsch interpretiert. Ich meine nicht die Animation von 2 Sekunden, sondern den dauerhaften
    /// effekt der ueber eine verbrannte Karte liegt. Und dieser Effekt war bei manchen Aktiven
    /// Karten vorhanden und wurde dort auch angezeigt - aber nur eine Runde - die runde darauf war
    /// die Karte wieder blau"</i> - and the 1:1 requirement in his original wording, <i>"Ich will
    /// hier eine Konsitenz auch ueber Runden hinweg, lokal und remote!"</i></para>
    ///
    /// <para>NO RAMP, EVER, AND THAT IS A SEPARATE STANDING RULING. 2026-09-06 item 8a, verbatim:
    /// <i>"Im Test wurde eine Karte aktiviert die nach ihren effekten erst verbrannt wird. D.h. dann
    /// soll auch nicht die verbrennen animation und ton bereits kommen ... da sie ja de facto noch
    /// nicht verbrannt ist, sondern nur aktiviert wurde."</i> The two rulings are not in conflict
    /// and the user drew the line himself: 8a forbids the ANIMATION and the SOUND at the moment of
    /// activation, the 2026-09-07 correction asks for the PERMANENT WASH. So this drives
    /// <c>SetAbilityCardFxProgress(want, 1f)</c> - the settled end state in one write, the same call
    /// <c>RemotePileFronts</c> makes for the pile fans and for the same reason - and never
    /// <see cref="DriveUsedCardFx"/>'s 2 s ramp.</para>
    ///
    /// <para>THE HOLD IS NO LONGER TAKEN HERE, AND THAT IS THE ModBuild 479 FIX FOR ITEM 2b. This
    /// method used to call <c>CardHalfTone.HoldBurntLook</c> itself, and it was the ONLY mirrored
    /// surface that did; the burnt pile fan, the recess ramp and the burn flight all paint through
    /// the same rig and none of them took it, so <c>CardHalfTone.NormalizeCardFx</c> and
    /// <c>RemotePileFronts</c>' 4 Hz re-assert fought over the same four floats - the user's
    /// <i>"mit Feuer Effekt und dann wieder ohne Feuer Effekt alternierend dauerhaft"</i>. The hold
    /// is now taken and released inside <c>RemoteCardArt.SetAbilityCardFxProgress</c> /
    /// <c>ClearAbilityCardFx</c>, the one call every mirrored look goes through, which keeps the
    /// ordering that mattered (hold BEFORE <c>BuildBurnRig</c> mints its per-image materials, or the
    /// rig's next write lands on the SHARED rest copy and chars every other clone using it) and
    /// makes forgetting it unrepresentable rather than a rule each new surface must remember.</para>
    ///
    /// <para>WHY THIS LIVES HERE AND NOT IN <c>RemoteActiveCards</c>. The alternative was to expose
    /// the hosted face and its <c>RemoteCardArt</c> out of this class and drive the hold from the
    /// column. This class already owns every lifetime EDGE the hold has to be released on -
    /// <see cref="ClearFace"/>, <c>ForgetGameHighlight</c>, <see cref="Destroy"/> - and a caller
    /// that missed one would leave <c>CardHalfTone</c> standing down for a face nobody is painting,
    /// which is precisely the failure <c>HoldMirroredDim</c>'s own doc warns about. One owner for
    /// the face, one owner for the hold.</para>
    ///
    /// <para>ACROSS THE FLIGHT BLANK. The column blanks a seat through <c>Set(null, ...)</c> while
    /// its card is still in the air, which reaches <see cref="ClearFace"/> and releases this hold
    /// with the face. That is correct and it is not a loss: the column re-asserts this call on every
    /// refresh, so the wash is re-applied on the first pass after the card lands, in the same pass
    /// that re-seats the face and therefore before its first drawn frame. What is NOT covered is
    /// the wash ON the flying slab itself - that arc is <c>RemoteCardFx</c>'s and is reported as an
    /// open divergence rather than silently claimed.</para>
    /// </summary>
    /// <param name="want">Rule 1's answer for the card in this slot, from the ONE expression both
    /// boards ask - <c>Cards.BurnLookPolicy.ForActivatedCard</c>, mapped into this module's enum by
    /// <c>UsedCardLook.FromPolicy</c>.</param>
    public void SetActiveCardLook(int playerId, int slot, CAbilityCard? card,
                                  RemoteCardArt.CardFxLook want)
    {
        try
        {
            FullAbilityCard? face = Path == RemoteAbilityCardSource.FacePath.None
                ? null
                : ResolveFaceCard();
            if (face == null)
            {
                // No real face in this slot (a back, the mod-drawn fallback, or an empty cell).
                // Nothing to hold and nothing to paint; the hold, if any, went with the face.
                _activeWashOn = false;
                return;
            }

            if (want == RemoteCardArt.CardFxLook.None)
            {
                if (_activeWashOn)
                {
                    _art?.ClearAbilityCardFx();
                    _activeWashOn = false;
                    LogActiveWashIfChanged(playerId, slot, card, want, took: true);
                }
                return;
            }
            if (_art == null)
                return;
            _art.Surface = RemoteCardArt.FxSurface.Active;
            // Idempotent by construction (the same floats and the same colours every time), so the
            // column's cadence costs a handful of SetFloat calls per seat and the rig itself is
            // built once per clone. Re-asserting rather than one-shotting is what makes the look
            // survive a clone rebuild - the defect is that it did NOT survive.
            bool took = _art.SetAbilityCardFxProgress(want, 1f);
            _activeWashOn = took;
            LogActiveWashIfChanged(playerId, slot, card, want, took: took);
        }
        catch (System.Exception ex)
        {
            // A face mid-rebuild draws FRESH - the picture this surface had before this change -
            // and never takes down the per-frame board tick this runs inside.
            _activeWashOn = false;
            VRLog.Warn("Net", "Remote active card: the owner's permanent burnt wash could not be "
                            + $"mirrored ({ex.Message}) - this active cell draws a fresh card.");
        }
    }

    private void LogActiveWashIfChanged(int playerId, int slot, CAbilityCard? card,
                                       RemoteCardArt.CardFxLook look, bool took)
    {
        int id;
        try { id = card != null ? card.CardInstanceID : 0; }
        catch { id = 0; }
        if (_loggedActiveWash.Card == id && _loggedActiveWash.Look == look)
            return;
        _loggedActiveWash = (id, look);
        string on = look.ToString().ToUpperInvariant();
        string name;
        try { name = card != null ? card.Name : "(none)"; }
        catch { name = "(unnamed)"; }
        // HW-VERIFY (2026-09-07 item 4, "lokal und remote"): which permanent look a peer's ACTIVATED
        // card carries on THIS client. Grep token: "REMOTE ACTIVE WASH".
        //
        // THE PAIR THAT IS THE 1:1 CLAIM: this line and the owner's own "[Cards] ACTIVE WASH" line
        // for the same card must agree on the destination, and this one's look must match what the
        // owner's own board is wearing. Both read the same local model object through the same
        // expression (Cards.BurnLookPolicy.ForActivatedCard), so a disagreement is a LOCAL GATE and
        // never a lost packet.
        // FALSIFIER: look=NONE for any card the ACTIVE SET row names as activated at all - since
        // ModBuild 479 there is no such thing as a clean activated card, so NONE here means the
        // destination expression threw or the seat is not really active. look=BURN against an
        // owner's "bound for Discarded", or look=GHOST against "bound for Lost", is the 1:1 breach.
        // took=False separates "the rig refused" (RemoteCardArt.BuildBurnRig found no card-FX
        // material or a degenerate _PosAndBounds) from "nothing was owed".
        VRLog.Note("Net", $"REMOTE ACTIVE WASH [player {playerId}] cell {slot}: '{name}' look={on} "
                        + $"(rig took the write = {took}). RULE 1: an ACTIVATED card bound for LOST "
                        + "wears the PERMANENT burnt wash on every board and keeps it, every round; "
                        + "one bound for DISCARD wears the PERMANENT grey-out and keeps that. There "
                        + "is no third state and no clean activated card. Bound-ness is the game's "
                        + "own expression (CCharacterClass.cs:479), asked once in "
                        + "Cards.BurnLookPolicy and read here off the SAME local model object the "
                        + "owner's board reads. Written as the settled end state at t = 1 and NEVER "
                        + "as a ramp - the 2026-09-06 item 8a ruling forbids the burn animation and "
                        + "sound on an activation, and this is the permanent effect, not the "
                        + "animation.");
    }

    /// <summary>Drop the hold and forget the applied state — a slot that no longer shows a real
    /// face has no halves to dim and must not keep CardHalfTone standing down for it.</summary>
    private void ReleaseSpentHold()
    {
        _appliedSpent = null;
        _fxLook = RemoteCardArt.CardFxLook.None;
        _fxElapsed = 0f;
        if (_faceCard != null)
            CardHalfTone.HoldMirroredDim(_faceCard, false);
    }

    // ───────────────────── the USED-CARD look, in the recess, on the card that was used ──────────

    /// <summary>How long the game's two card-FX timelines run, in seconds. MIRRORED CONSTANT and it
    /// is one number for both: <c>CardEffects.BurnCardTimeline</c> opens with
    /// <c>float burnTime = 2f</c> (CardEffects.cs:516) and <c>GhostOutOnTimeline</c> with
    /// <c>float animTime = 2f</c> (:629). It is the same duration <c>Net.RemoteBurnFx.HoldSeconds</c>
    /// mirrors from the other side.</summary>
    private const float UsedCardFxSeconds = 2f;

    /// <summary>Which look this recess is currently ramping, and how far in. Reset whenever the
    /// shown card changes (through <see cref="ReleaseSpentHold"/> / <see cref="ClearFace"/>), so a
    /// new card in the same recess starts clean.</summary>
    private RemoteCardArt.CardFxLook _fxLook = RemoteCardArt.CardFxLook.None;
    private float _fxElapsed;

    /// <summary>Change gate for the <c>RECESS CARD FX</c> line: the look, the source and whether the
    /// write took.</summary>
    private string _loggedFx = string.Empty;

    /// <summary>Is the card this recess shows a member of its owner's <c>RoundAbilityCards</c> —
    /// i.e. is it SEATED IN THIS ROUND'S RECESS rather than merely lying where it was left? Sampled
    /// once per <see cref="ResolveUsedCardLook"/> pass, reported on the <c>RECESS CARD FX</c> line
    /// beside the state that was chosen, because "seated AND classified discarded" is item 9's
    /// defect stated in one clause.</summary>
    private bool _seatedInRound;

    /// <summary>The card's own <c>CBaseCard.CurrentCardPile</c> stamp at that same instant — the
    /// LATCH, printed beside the LIST so a log says which of the two the classifier obeyed and what
    /// the other one held.</summary>
    private CBaseCard.ECardPile _latchedPile = CBaseCard.ECardPile.None;

    /// <summary>
    /// THE FOLLOW-UP THE USER ASKED FOR IN HIS OWN WORDS (2026-09-06): "Das verkohlen oder ausgrauen
    /// (verbraucht, verbrannt) soll nicht im Fächer (in der Mitte) des jeweiligen Stapels angezeigt
    /// werden, sondern auch im remote board direkt in der Mulde wenn eine Karte genutzt wurde …
    /// nicht nur die Verkohlung bei verbrennen sondern auch das Ausgrauen wenn eine nicht-verbrennen
    /// Aktion benutzt wurde so wie sie der Spieler dessen Board es ist auch sieht."
    ///
    /// <para>WHAT THE OWNER ACTUALLY SEES, read out of the game rather than described. The turn flow
    /// calls <c>FullAbilityCard.TryPlayBurnAnimation(actionType)</c> on the played card at
    /// <c>CardsActionControlller.Finish()</c> (:417), <c>AfterItemUseAtEndOfTurn()</c> (:437-439) and
    /// on entering <c>Phase.Select2ndCard</c> (:298). That method picks between exactly two
    /// <c>CardEffects</c> timelines by the CardPile of the action that was USED: a LOST action that
    /// actually resolved gets <c>FXTask.BurnCard</c> (the char), everything else gets
    /// <c>FXTask.DiscardMode</c> (the grey-out). Both paint the WHOLE card, header included — which
    /// is why the user says "die gesamte Karte" and why this is a different thing from
    /// <see cref="SetSpentHalves"/>, whose alpha-0.5 CanvasGroups only touch the two action halves.
    /// The peer's recess had NEITHER, because <c>RemoteCardArt.StripFragileEffects</c> destroys
    /// <c>CardEffects</c> on every clone.</para>
    ///
    /// <para>WHY THE GAME'S OWN CALL IS NOT MADE HERE, precisely. <c>CardHalfTone</c> can run
    /// <c>SetInteractable</c> on a mod-owned face because that method is a <c>canvasGroup.alpha</c>
    /// write on a <c>FullAbilityCardAction</c> the clone STILL HAS.
    /// <c>TryPlayBurnAnimation</c> is not that kind of call and it fails four separate ways:
    /// (1) it dereferences <c>cardEffects</c> unconditionally and that component is
    /// <c>DestroyImmediate</c>d on the clone before it ever activates — an immediate NRE;
    /// (2) leaving the component alive instead is the hazard the strip exists for —
    /// <c>ToggleEffect</c> opens with <c>Initialize()</c> (CardEffects.cs:359), which swaps every
    /// Image to a screen-space <c>_PosAndBounds</c> material computed against OUR world-space
    /// canvas, the "card renders DEEP BLACK" failure;
    /// (3) <c>ToggleAdditiveEffect</c> drives the timeline through
    /// <c>Choreographer.s_Choreographer.StartCoroutine</c> and <c>Timekeeper.instance.m_GlobalClock</c>
    /// on a detached cosmetic object; and
    /// (4) <c>ToggleEffect</c>'s own <c>RestoreCard()</c> writes <c>imgComp[i].material</c>, which on
    /// a clone whose source never ran <c>Initialize</c> is the SHARED AUTHORED
    /// <c>GUI_CardEffect_Mat</c> — presentation code repainting a game asset for every card in the
    /// process. So the CALL is refused and the same rig
    /// <see cref="RemoteCardArt.SetAbilityCardFxProgress"/> already drives for the burnt-pile fan
    /// and the burn flight is used instead: the game's own numbers, on materials this mod mints.
    /// One implementation of the look, three surfaces.</para>
    ///
    /// <para>THE TRIGGER IS THE OWNER'S OWN EFFECT STATE WHERE IT CAN BE READ. The peer's live
    /// widget is already resolved on this slot for the plume (<see cref="TickPlume"/>, which reads
    /// <c>HasEffect</c> off exactly this object under exactly this identity check), so the first
    /// question asked is the game's own answer. Where that widget cannot be observed the fallback
    /// re-derives <c>TryPlayBurnAnimation</c>'s own expression from state both clients hold: which
    /// half was used (record 41 — measured off the owner's rendered alpha, never from rules), that
    /// action's <c>CardPile</c>, and <c>CBaseCard.ActionHasHappened</c>, which is serialized and is
    /// compared by the game's own MP state check (CBaseCard.cs:407), so it cannot disagree between
    /// the two machines. The log line says WHICH of the two answered.</para>
    ///
    /// <para>ONLY THE USED CARD. Every term is this slot's own — its shown card, its own spent
    /// flags, its own widget. A recess whose card nobody has touched resolves to
    /// <see cref="RemoteCardArt.CardFxLook.None"/> and is never written, which is the user's
    /// "während die andere Karte so bleibt wie sie ist".</para>
    /// </summary>
    private void DriveUsedCardFx(int playerId, int slot, bool topSpent, bool bottomSpent)
    {
        RemoteCardArt.CardFxLook want = ResolveUsedCardLook(topSpent, bottomSpent, out string source);
        if (want != _fxLook)
        {
            RemoteCardArt.CardFxLook was = _fxLook;
            _fxLook = want;
            _fxElapsed = 0f;
            // ─── THE LOOK COMES OFF AGAIN (2026-09-06 item 8a) ───────────────────────────────────
            // The ramp used to be ONE-WAY: every caller could drive it 0 -> 1 and nobody could drive
            // it back, so a card the GAME itself un-burns kept this overlay's char for the rest of
            // its life on that peer's board. FullAbilityCard.SetPile is the game's own restore
            // (FullAbilityCard.cs:325-328) and it fires on exactly the case he reported: a card that
            // burns only AFTER its effects enters ECardPile.Activated first, and Hand/Activated is
            // the branch that calls cardEffects.RestoreCard(). One line, and it is the difference
            // between "activated" and "burnt" on every peer's board.
            if (was != RemoteCardArt.CardFxLook.None && want == RemoteCardArt.CardFxLook.None)
            {
                _art?.ClearAbilityCardFx();
                LogUsedCardFxIfChanged(playerId, slot, applied: true, source);
            }
        }
        if (_fxLook == RemoteCardArt.CardFxLook.None || _art == null)
        {
            // ITEM 9's POSITIVE READING, and it is the reason this branch logs at all. With the
            // fix in place the correct answer for a freshly laid round card is NONE, and a silent
            // instrument cannot be told apart from one that never ran — this project has paid for
            // that ("a held instrument reads as dead"). Gated on SEATED so it is exactly one line
            // per round card per state change and nothing else: an empty recess (card == null)
            // never reaches here, and a card lying anywhere but this round's recess is not what
            // item 9 is about.
            if (_seatedInRound)
                LogUsedCardFxIfChanged(playerId, slot, applied: true, source);
            return;
        }
        // NAMED BY THE DRIVER. This method is no longer the only driver a board card has: since the
        // user's 2026-09-07 correction the ACTIVE-CARDS column drives SetActiveCardLook above, so
        // an activated face does build a rig and does carry a settled look.
        //
        // ─── THE NOTE THAT USED TO STAND HERE IS FALSE NOW, AND SO IS RemoteCardArt's ────────────
        // It read: "the ACTIVE-CARDS column ... never reaches here, so its faces stay
        // FxSurface.Unnamed and print nothing — which is the CORRECT reading for item 8a: an
        // activated card is not burnt". Item 8a (2026-09-06) forbids the burn ANIMATION and SOUND on
        // an activation and says nothing about the permanent wash; the 2026-09-07 correction asks
        // for the wash and the user drew that line himself ("Ich meine nicht die Animation von 2
        // Sekunden, sondern den dauerhaften effekt"). RemoteCardArt.FxSurface.Unnamed's own doc
        // repeats the retired claim and OWES a correction plus an `Active` member; that file was not
        // handed to this lane, so the active wash deliberately leaves Surface at Unnamed and reports
        // through its own REMOTE ACTIVE WASH line instead of mislabelling itself as another
        // surface. FxSurface is instrument-only — nothing behavioural reads it — so the cost is a
        // per-surface latch, not a wrong picture.
        _art.Surface = RemoteCardArt.FxSurface.Recess;
        if (_fxElapsed < UsedCardFxSeconds)
            _fxElapsed = Mathf.Min(UsedCardFxSeconds, _fxElapsed + Mathf.Max(0f, Time.unscaledDeltaTime));
        bool took = _art.SetAbilityCardFxProgress(_fxLook, _fxElapsed / UsedCardFxSeconds);
        LogUsedCardFxIfChanged(playerId, slot, took, source);
    }

    /// <summary>
    /// <c>TryPlayBurnAnimation</c>'s verdict for the card lying in this recess: the game's own
    /// answer when the owner's widget can be observed, and the game's own EXPRESSION over
    /// host-replicated state when it cannot.
    /// </summary>
    private RemoteCardArt.CardFxLook ResolveUsedCardLook(bool topSpent, bool bottomSpent,
                                                         out string source)
    {
        source = "none";
        CAbilityCard? card = _plumeCard;
        if (card == null)
        {
            // An EMPTY recess is not a card seated anywhere. Clearing both here matters because the
            // RECESS CARD FX line reports them: leaving the previous card's values standing would
            // print 'seated=True' for a slot holding nothing, which is a lie in the one clause item
            // 9 is read from.
            _seatedInRound = false;
            _latchedPile = CBaseCard.ECardPile.None;
            return RemoteCardArt.CardFxLook.None;
        }

        // ── 0. THE PILE THE CARD LANDED IN — WHICH OUTRANKS THE ACTION THAT WAS PLAYED, BUT ONLY
        //       ONCE THE CARD HAS LEFT THIS ROUND'S RECESS (item 9, at the end of this block) ──────
        // 2026-09-06 item 8a, his words: "Im Test wurde eine Karte aktiviert die nach ihren effekten
        // erst verbrannt wird. D.h. dann soll auch nicht die verbrennen animation und ton bereits
        // kommen … da sie ja de facto noch nicht verbrannt ist, sondern nur aktiviert wurde."
        //
        // THE GAME HAS TWO RULES FOR THIS LOOK AND THIS SURFACE ONLY EVER COPIED ONE.
        //   (a) FullAbilityCard.TryPlayBurnAnimation (FullAbilityCard.cs:577-604) keys on the PLAYED
        //       ACTION - CardPile plus ActionHasHappened - and is what step 2 below reproduces.
        //   (b) FullAbilityCard.SetPile (:313-333) keys on the PILE THE CARD LANDS IN, and it runs
        //       AFTER (a) on every pile change: Discarded -> DiscardMode, Lost/PermanentlyLost ->
        //       LostMode (which is BurnCardTimeline, CardEffects.cs:422-423), and Hand or ACTIVATED
        //       -> cardEffects.RestoreCard(), i.e. NO LOOK AT ALL.
        // Rule (b) is the later writer and therefore the one the owner ends up looking at, so it is
        // asked FIRST here — but only about a card that has actually LANDED somewhere. See the item
        // 9 block below for the one state where rule (b) has nothing to say and used to answer
        // anyway.
        //
        // AND ACTIVATION IS PRECISELY WHERE THE TWO DISAGREE. A persistent card is one whose action
        // has CardPile == Lost, so rule (a) chars it - but CCharacterClass.MoveAbilityCardToPile
        // (CCharacterClass.cs:434-442) reads:
        //     eCardPile = (!abilityCard.ActionHasHappened) ? Discarded : abilityCard.SelectedAction.CardPile;
        //     if (abilityCard.ActiveBonuses.Count > 0) eCardPile = ECardPile.Activated;
        // The ActiveBonuses clause OVERRIDES the action's own pile, so the card goes to the ACTIVE
        // area, SetPile(Activated) restores it, and the owner's card is clean. It burns for real
        // only on the SECOND pass, when CheckForFinishedActiveBonuses (:1131) re-runs the same
        // method with ActiveBonuses.Count == 0 and the card finally reaches LostAbilityCards.
        //
        // CurrentCardPile IS THE FIELD, and it is not a convenience: CBaseCard.cs:91 declares it,
        // CCharacterClass.cs:452 writes it, it is SERIALIZED (CBaseCard.cs:103/:126) and the
        // game's own multiplayer state comparison checks it (CBaseCard.cs:382-403, mismatch code
        // 2804) - the same argument by which ActionHasHappened is trusted twenty lines below. It
        // cannot disagree between the two machines.
        //
        // ─── THE SENTENCE ABOVE USED TO SAY ":452 MAINTAINS IT" AND THAT WORD WAS FALSE ───────────
        // USER ITEM 9 (2026-09-07, verbatim): "Ein Spieler hatte eine kurze Rast gemacht, danach
        // zwei Karten gelegt und es ging in die Aktionsphase. Dort wurden aber auf dem remote Board
        // und auch wenn ich auf dem Character gegangen bin - die Karten angezeigt als wären sie
        // bereits abgeworfen (so ausgegraut) - sie sind aber ja die zwei Karten in der Mulde für
        // diese Runde, die von dem Character noch gar nicht gespielt wurden."
        //
        // CurrentCardPile IS WRITTEN ON ONE TRANSITION IN FOUR. Every assignment to it in the whole
        // rules library is: MoveAbilityCardToPile (CCharacterClass.cs:452, the END-OF-TURN drain),
        // RestoreCachedAugmentOrSongAbilityCard (:548), scenario setup (:1198/:1207 -> Hand), and
        // CBaseCard's own reset/deserialize. The two moves that matter here go through a DIFFERENT
        // method - CCharacterClass.MoveAbilityCard (:273-303), which edits the two LISTS and never
        // touches the field:
        //   * SELECTING A CARD FOR THE ROUND is MoveAbilityCard(Hand -> Round) (CardsHandUI.cs:2015
        //     via ScenarioRuleClient.MoveAbilityCard);
        //   * A SHORT REST is MoveAbilityCard(Discarded -> Hand), once per discarded card
        //     (GameState.PlayerShortRested :2612-2616), and a LONG REST the same (:2554-2558).
        // So ECardPile.Round is a value the game DECLARES (CBaseCard.cs:41) and NEVER ASSIGNS, the
        // "Round falls through" line below was unreachable, and a card lying in this round's recess
        // carries whatever pile it last LANDED in: Hand for a card never yet played, and Discarded
        // for one a rest handed back. That is the user's report exactly - he rested, so both cards
        // came back to the hand still stamped Discarded, he laid them, and this switch called them
        // discarded before anybody had played anything.
        //
        // MEASURED, ModBuild 474, the host log of the 2026-09-07 session. The peer's short rest is
        // mirrored at line 191297 ('SHORT REST SEAT [player 2] ... ABILITY_CARD_SpareDagger ...
        // resolved from the DISCARD arc at seat 1 of 6'). Lines 208837 and 208838 - the only frame
        // in either log where BOTH recesses change look together - read 'RECESS CARD FX [player 2]
        // recess 1/2: look=GHOST, applied=yes, source=pile:Discarded'. Over the same window the
        // record 41 half-dim channel was SILENT: the host's last inbound 'SPENT HALF [player 2]'
        // before it is line 184650 reading top=False, bottom=False, and the next one is line 237755,
        // 53 000 lines later. Nothing had been used and the mask said so; the LATCH said otherwise
        // and this switch believed the latch. 'CARD HALF TONE CENSUS' corroborates from the pixel
        // side: from #133 (line 211045) it reports CLONE 2 face(s)/4 halves at _GreyOut 1.00
        // unbroken to line 241458 - two faces, the two recesses, for the rest of the session.
        //
        // BOTH SURFACES WERE WRONG AND THE WIRE IS NOT INVOLVED, which is the shape that names a
        // local gate. The rules model of every actor is simulated on every client, so the list this
        // fix reads is the same object on both machines in the same frame.
        //
        // SO THE LIST OUTRANKS THE LATCH, AND ONLY FOR THE CARDS THE LATCH CANNOT DESCRIBE.
        // CCharacterClass.RoundAbilityCards is the raw backing field (:89, not the LINQ projection
        // ActivatedAbilityCards is) and it is maintained on every one of these transitions, because
        // moving the card between lists is the whole of what MoveAbilityCard does. A card IN that
        // list is seated in this round's recess and its pile stamp is a value from a previous round;
        // for it, rule (b) has no opinion and the question goes back to rule (a) - the ACTION THAT
        // WAS PLAYED, steps 1 and 2 below, which are gated on record 41's spent mask and answer
        // None for a card nobody has touched.
        //
        // IT KEEPS ITEM 8a AND IT RESTORES ITEM 8. An ACTIVATED card is not in RoundAbilityCards -
        // MoveAbilityCardToPile removed it on the way to m_ActivatedCards - so 'pile:Activated ->
        // None' still runs and the activated-not-yet-burnt card stays clean. And the mid-turn
        // grey-out the 2026-09-06 item 8 asked for ("das Ausgrauen wenn eine nicht-verbrennen Aktion
        // benutzt wurde") was ALSO being eaten by this switch, in the other direction: a fresh round
        // card is stamped Hand, and 'pile:Hand -> None' returned before either resolver could see
        // that the owner had used a half. One expression, both directions, one fix.
        try
        {
            System.Collections.Generic.List<CAbilityCard>? round =
                _plumeOwner != null && _plumeOwner.CharacterClass != null
                    ? _plumeOwner.CharacterClass.RoundAbilityCards
                    : null;
            _seatedInRound = round != null && round.Contains(card);
            _latchedPile = card.CurrentCardPile;
        }
        catch (System.Exception)
        {
            // An unreadable actor is not an answer either: leave the seat unknown and let the latch
            // decide, which is exactly the picture every build before this one drew.
            _seatedInRound = false;
            _latchedPile = CBaseCard.ECardPile.None;
        }

        if (_seatedInRound)
        {
            // The card is lying in this round's recess. Its pile stamp describes a PREVIOUS round
            // and may not decide anything; fall through to the action-that-was-played below.
            source = $"list:RoundAbilityCards (latch {_latchedPile} overruled)";
        }
        else
        {
            try
            {
                switch (card.CurrentCardPile)
                {
                    case CBaseCard.ECardPile.Activated:
                    case CBaseCard.ECardPile.Hand:
                        // SetPile's RestoreCard branch. An ACTIVATED card is not burnt and not
                        // spent; it is in play. This is the whole of item 8a on this surface.
                        source = "pile:" + card.CurrentCardPile;
                        return RemoteCardArt.CardFxLook.None;
                    case CBaseCard.ECardPile.Lost:
                    case CBaseCard.ECardPile.PermanentlyLost:
                        source = "pile:" + card.CurrentCardPile;
                        return RemoteCardArt.CardFxLook.Burn;
                    case CBaseCard.ECardPile.Discarded:
                        source = "pile:Discarded";
                        return RemoteCardArt.CardFxLook.Ghost;
                }
                // None falls through: the card is still on the board and rule (a) decides. Round
                // NEVER reaches here, and never could — see the block above for why.
            }
            catch (System.Exception)
            {
                // An unreadable pile is not an answer - fall through to the two resolvers below,
                // which is exactly the picture every build before this one drew.
            }
        }

        // ── 1. THE OWNER'S OWN EFFECT STATE ──────────────────────────────────────────────────────
        // Under the SAME identity check TickPlume applies: the effect may only be read off the very
        // widget this slab's face was cloned from, or the look would be a different card's.
        //
        // ITS OWN CACHE, not the plume's, and that is deliberate. TickPlume's resolve only runs
        // while the OWNER'S PARTICLE PERMISSION is on (wire id TuneGameCardParticlesOn), and that
        // bit is about smoke, not about whether a card was used. Sharing the cache would have made
        // this look silently depend on a dial that has nothing to do with it — the "wrong entry"
        // mistake, one surface over.
        try
        {
            FullAbilityCard? full = EnsureFxSource(card);
            CardEffects? fx = full != null && _art != null && _art.ShowsKey(full.GetInstanceID())
                ? full.cardEffects
                : null;
            if (fx != null)
            {
                source = "widget";
                if (fx.HasEffect(CardEffects.FXTask.BurnCard))
                    return RemoteCardArt.CardFxLook.Burn;
                if (fx.HasEffect(CardEffects.FXTask.DiscardMode)
                    || fx.HasEffect(CardEffects.FXTask.LostMode))
                    return RemoteCardArt.CardFxLook.Ghost;
                return RemoteCardArt.CardFxLook.None;
            }
        }
        catch (System.Exception)
        {
            // fall through to the model — an unreadable widget is not an answer
        }

        // ── 2. THE MODEL, term for term out of FullAbilityCard.TryPlayBurnAnimation ───────────────
        if (!topSpent && !bottomSpent)
            return RemoteCardArt.CardFxLook.None;   // nobody has used this card
        source = "model";
        try
        {
            // WHICH action was used. Both halves spent is the Finish() state, where the game has
            // already run TryPlayBurnAnimation for the action that was actually played; a LOST
            // half anywhere in what was used is what decides the char, which is the same answer
            // TryPlayBurnAnimation reaches for the played action and the safe one when both are
            // gone (a card with a lost half that has been used is a card on its way out).
            bool lost = (topSpent && IsLostPile(card.TopAction))
                        || (bottomSpent && IsLostPile(card.BottomAction));
            // …AND THE SECOND TERM OF THAT METHOD, which is easy to drop: a lost action that was
            // SELECTED but never resolved ghosts rather than chars (FullAbilityCard.cs:585,
            // `if (abilityCard.ActionHasHappened)` … `else` → FXTask.DiscardMode).
            // CBaseCard.ActionHasHappened is serialized and is one of the fields the game's own MP
            // comparison checks (CBaseCard.cs:407), so this reads the same bool the owner reads.
            return lost && card.ActionHasHappened
                ? RemoteCardArt.CardFxLook.Burn
                : RemoteCardArt.CardFxLook.Ghost;
        }
        catch (System.Exception)
        {
            // A card whose actions cannot be read gets the GHOST, which is the branch
            // TryPlayBurnAnimation itself falls through to for everything that is not a resolved
            // lost action — the same safe direction, not a guess.
            return RemoteCardArt.CardFxLook.Ghost;
        }
    }

    /// <summary>The peer's LIVE widget for the card in this recess, resolved once per card and then
    /// cached. A miss is cached too (<see cref="_fxSourceMissing"/>) so a peer whose hand this
    /// client does not hold costs one lookup per card and not one per frame. The resolve is
    /// <c>RemoteAbilityCardSource.TryLiveWidget</c> — the same call that drew the face, so the two
    /// answers cannot be two different widgets.</summary>
    private FullAbilityCard? EnsureFxSource(CAbilityCard card)
    {
        int key = PlumeKeyOf(card);
        if (key != _fxSourceKey)
        {
            _fxSourceKey = key;
            _fxSource = null;
            _fxSourceMissing = false;
        }
        if (_fxSource == null && !_fxSourceMissing)
        {
            _fxSource = RemoteAbilityCardSource.TryLiveWidget(_plumeOwner, card);
            _fxSourceMissing = _fxSource == null;
        }
        return _fxSource;
    }

    private FullAbilityCard? _fxSource;
    private bool _fxSourceMissing;
    private int _fxSourceKey = int.MinValue;

    /// <summary>Does this action send its card to the burnt pile? The game's own test, spelled the
    /// game's own way (<c>FullAbilityCard.cs:583</c>).</summary>
    private static bool IsLostPile(CAction? action) =>
        action != null && (action.CardPile == CBaseCard.ECardPile.Lost
                           || action.CardPile == CBaseCard.ECardPile.PermanentlyLost);

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-06 follow-up): does the card in a peer's RECESS wear the look its
    /// owner's does, and which of the two resolvers answered? Grep token <c>RECESS CARD FX</c>.
    ///
    /// <para>WORKING = one line per used card naming <c>look=BURN</c> or <c>look=GHOST</c> with
    /// <c>applied=yes</c>, and NO line for the card beside it while that one is untouched. Read it
    /// beside the owner's <c>SPENT HALF SENT</c> line for the same recess: the two must agree that
    /// this slot's card was used.</para>
    ///
    /// <para>INERT = <c>applied=no</c>, which means the rig refused this face — the reason is then in
    /// <c>Remote BURN look</c> (REFUSED, or NO CARD-FX MATERIAL). No line at all beside a
    /// <c>SPENT HALF</c> line naming that recess means the look resolved to NONE, i.e. neither
    /// resolver saw a used action, and <c>source=</c> on the neighbouring lines says which resolver
    /// is even running.</para>
    ///
    /// <para>THE ITEM 8a READING (2026-09-06): <c>look=NONE, source=pile:Activated</c> on the line
    /// that FOLLOWS a <c>look=BURN</c> line for the same recess is the fix WORKING — the card was
    /// activated rather than burnt and this surface took the char back off. Its absence is the
    /// defect: a <c>look=BURN</c> line with NO <c>look=NONE</c> line after it, on a card the user
    /// can see sitting in the ACTIVE column, means the pile term never fired. Note the SOUND is not
    /// in this instrument and never can be: <c>PlaySound_CardUI_BurnedCard</c> is played by the GAME
    /// at FullAbilityCard.cs:590, on the owner's own machine, from the owner's own
    /// <c>TryPlayBurnAnimation</c> — this mod plays no burn sound anywhere, so an early burn sound
    /// is the game's and is heard by its owner too.</para>
    ///
    /// <para>STILL BEYOND THE INSTRUMENT = <c>source=model</c> on every line. That is not a failure —
    /// the fallback is the game's own expression — but it means the owner's widget could not be
    /// observed on this client, so the premise <see cref="TickPlume"/> is built on
    /// ("a peer's widget carries the effect") has never been confirmed on hardware and the two
    /// resolvers have never been compared against each other. A round with BOTH sources appearing
    /// is what would settle it.</para>
    /// </summary>
    private void LogUsedCardFxIfChanged(int playerId, int slot, bool applied, string source)
    {
        string now = $"{_fxLook}|{applied}|{source}|{Path}";
        if (now == _loggedFx)
            return;
        _loggedFx = now;
        // HW-VERIFY: grep token "RECESS CARD FX" — see this method's doc for the three readings.
        VRLog.Note("Net", $"RECESS CARD FX [player {playerId}] recess {slot + 1}: look="
            + $"{_fxLook.ToString().ToUpperInvariant()}, applied={(applied ? "yes" : "no")}, "
            + $"source={source}, face={Path}. This is the look the GAME puts on the owner's own card "
            + "the moment he uses an action on it — FullAbilityCard.TryPlayBurnAnimation picks "
            + "CardEffects.BurnCardTimeline for a resolved LOST action and GhostOutOnTimeline for "
            + "every other one, and both paint the WHOLE card, not one half. The game's call cannot "
            + "be made on a mod clone (its CardEffects is stripped, and ToggleEffect would run "
            + "Initialize against our world-space canvas and write a SHARED authored material), so "
            + "the same rig the burnt-pile fan and the burn flight already use replays the timeline's "
            + "own numbers on materials this mod minted. source=widget means the OWNER'S OWN effect "
            + "flag answered; source=model means it was re-derived from record 41's spent half plus "
            + "that action's CardPile and CBaseCard.ActionHasHappened, both host-replicated. A recess "
            + "whose card nobody has used prints NO line at all. "
            + "WHICH GAME EVENT FIRED THIS, and it is the whole of the 2026-09-06 item 8a question: "
            + "source=pile:X means the PILE the card LANDED IN decided (FullAbilityCard.SetPile, the "
            + "later writer), and source=widget/model means the ACTION THAT WAS PLAYED decided "
            + "(FullAbilityCard.TryPlayBurnAnimation, the earlier one). look=NONE with "
            + "source=pile:Activated is the ACTIVATION reading and is CORRECT: the card was activated, "
            + "not burnt, CCharacterClass.MoveAbilityCardToPile:440-442 sent it to ECardPile.Activated "
            + "because ActiveBonuses.Count > 0, and SetPile:324-327 calls RestoreCard for that pile ON "
            + "THE CHANGE INTO IT - the three FX arms sit inside 'if (cardPile != newCardPile && "
            + "cardEffects != null)' (FullAbilityCard.cs:314), measured against the value the UI last "
            + "received and not against the model, so RestoreCard is NOT unconditional and an "
            + "unchanged card is a no-op on the FX layer. THIS RECESS DOES NOT DEPEND ON THAT: the "
            + "look above is decided from CBaseCard.CurrentCardPile, which is the pile itself, so the "
            + "reading stays correct whether or not the owner's own widget got its char taken back "
            + "off. look=BURN with source=pile:Lost is the "
            + "RESOLVE reading and is the card really burning. look=BURN with source=widget or "
            + "source=model on a card that is sitting in the ACTIVE column is the DEFECT this term "
            + "was added to end, and seeing it again means CurrentCardPile did not read Activated. "
            + "─── ITEM 9 (2026-09-07), THE LIST AND THE LATCH, PRINTED SIDE BY SIDE: this card is "
            + $"SEATED IN THIS ROUND'S RECESS (CCharacterClass.RoundAbilityCards)={_seatedInRound}, "
            + $"while its own CBaseCard.CurrentCardPile LATCH reads {_latchedPile}. Those two "
            + "disagreeing is not a race and not replication lag - the list and the field are the "
            + "same objects on every client - it is the field being STALE, because the game writes "
            + "it only in MoveAbilityCardToPile (the end-of-turn drain) and never in "
            + "MoveAbilityCard, which is what BOTH a round selection (CardsHandUI.cs:2015) and a "
            + "SHORT REST (GameState.PlayerShortRested:2612-2616) go through. So a rested card comes "
            + "back to the hand still stamped Discarded and gets laid into a recess wearing it. "
            + "seated=True with source=pile:Discarded IS the reported defect and this build cannot "
            + "print it any more: the list is asked first and the latch is overruled, which the "
            + "source clause says in words (list:RoundAbilityCards (latch X overruled)). seated=True "
            + "with look=GHOST and source=widget or model is NOT the defect - that is the owner "
            + "having actually used a half this turn, which is item 8 and must keep working.");
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
    ///
    /// <para>THAT GATE IS NOW SHARED, and the sharing is the whole of user item 7 (2026-09-07).
    /// The expression above lived only here, while the LOCAL twin — the owner's own ACTIVE-cards
    /// column, <c>CardsDriver.SetActiveHighlight</c> — asserted the identical game component with
    /// no gate at all, once per driver rebuild. Same component, same non-idempotent
    /// <c>ShowHover</c>, two opinions about when a write is owed. It moved verbatim into
    /// <see cref="Cards.ActionHighlightDriver"/> so both surfaces ask one expression; the behaviour
    /// on this path is unchanged, term for term.</para>
    /// </summary>
    private bool ApplyHalf(FullAbilityCardAction? action, int half, int hoverHalf, int selectedHalf)
    {
        int want = selectedHalf == half ? Cards.ActionHighlightDriver.Selected
            : hoverHalf == half ? Cards.ActionHighlightDriver.Hover
            : Cards.ActionHighlightDriver.Off;
        // WHICH of this half's two regions the owner named. The selection wins on a doubly-lit
        // half, exactly as the game's own RefreshHighlight parks the selected look over the
        // hover's — so the region question follows the same winner.
        bool wantDefault = want == Cards.ActionHighlightDriver.Selected ? _shownSelectedDefault
            : want == Cards.ActionHighlightDriver.Hover && _shownHoverDefault;
        return Cards.ActionHighlightDriver.Assert(
            action, want, wantDefault,
            ref _appliedHighlight[half], ref _appliedDefault[half],
            Cards.ActionHighlightDriver.Site.MirroredRecess);
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
        {
            CardHalfTone.HoldMirroredDim(_faceCard, false);
            // ITEM 4 (2026-09-07): the ACTIVE-column burnt wash is released with the face for
            // the identical reason — a hold on a face nobody is painting stands CardHalfTone
            // down for a clone that genuinely needs resetting. The column re-asserts it on its
            // next refresh.
            // The card-FX look hold is RemoteCardArt's since ModBuild 479 and is released with
            // the clone it protects (RemoteCardArt.DestroyClone); nothing to release here.
        }
        _activeWashOn = false;
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
        // FRACTIONS OF THE CARD, AND THE CARD IS THE BODY RECT (see _bodyRect). HalfSelection's
        // three fractions are measured against the owner's own card, whose backing is fitted to the
        // printed rect — taking them against the nominal box instead would put a peer's half glow
        // 17.5 % wide of the half it is supposed to be lighting.
        Vector2 body = _bodyRect;
        var size = new Vector3(body.x * Cards.HalfSelection.ZoneWidthFrac,
                               body.y * Cards.HalfSelection.ZoneHeightFrac, 1f);
        float centerY = body.y * Cards.HalfSelection.ZoneCenterYFrac;
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
        // THE DUST IS EMITTED OVER THE CARD, and the card is the BODY rect (see _bodyRect): the
        // owner's own puff spans his backing, which is that rectangle and not the nominal box.
        float halfW = _bodyRect.x * 0.5f * lossy;
        float halfH = _bodyRect.y * 0.5f * lossy;
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

/// <summary>Per-frame clock for <see cref="RemoteBoardCard.TickGlide"/> — the same device, and for
/// the same reason, as <see cref="MaterialisePump"/> beside it: the panel is plain C# and its owners
/// refresh content four times a second, which cannot carry an animation. See
/// <see cref="RemoteBoardCard.Move"/> for what the glide mirrors.</summary>
internal sealed class GlidePump : MonoBehaviour
{
    /// <summary>The panel this pump drives. Assigned once, at build time.</summary>
    internal RemoteBoardCard? Slot;

    private void Update() => Slot?.TickGlide();
}
