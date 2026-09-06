using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S BURN, ON THE RIGHT CARD, AT THE RIGHT MOMENT — the receiving half of the burn flow
/// (2026-09-05 multiplayer report, items 11a and 11b).
///
/// <para>WHAT HE REPORTED, VERBATIM: "Immer noch sieht der Mitspieler eine andere Karte die
/// verbrannt wurde als ich. Und auch sieht er nicht die Feuer Animation die ich sehe. Verletzung
/// der 1:1 Regel."</para>
///
/// <para>THE CAUSE IS STRUCTURAL AND THE WIRE SAYS IT IN ITS OWN WORDS. Everything a peer was
/// given about a burn is <see cref="NetCardFx"/>'s two-byte event: a low nibble FROM anchor, a
/// high nibble TO anchor, and a wrapping sequence. <see cref="RemoteCardFx"/>'s own doc states the
/// consequence — "the slab is a BACK on both faces… No card identity is ever transmitted or
/// rendered here." So the peer flew a blank card back. He could not see WHICH card burned because
/// nothing named one, and he could not see the fire because a back slab has no face to burn. Both
/// halves of the report are the same missing fact.</para>
///
/// <para>AND THE STANDING ARGUMENT FOR WHY IT COULD NOT BE FIXED IS FALSE. <c>RemoteAvatar</c>'s
/// held-card note says of this very surface: "a flight is an EVENT, not a state, and the card it
/// carries is in transit BETWEEN two lists — the model has usually already moved it by the time
/// the receiver replays the arc, so there is no list position that names it for the duration of
/// the flight. It needs a different record." That is true of a DISCARD and false of a BURN, and
/// the difference is the whole of this class. A burned card is not in transit: the game commits it
/// into <c>CCharacterClass.LostAbilityCards</c> BEFORE it starts the card's burn artwork (that
/// ordering is the reason the owner's own <c>CardsDriver.TickBurnToPile</c> has to hold the card
/// on the board at all), and it stays in that list for the rest of the scenario. That list is
/// HOST-REPLICATED and this client already walks it — <see cref="RemotePileFronts.Resolve"/> reads
/// exactly it, through exactly the same <c>CardsGameApi.GetPileWidgets</c> call, to draw the
/// peer's burnt-pile fan. A card ARRIVING in it is a burn, and naming it costs nothing.</para>
///
/// <para>SO NO WIRE FIELD IS OWED, and adding one would have been the wrong fix twice over: it
/// would have put a card identity on the wire for the first time (the anti-cheat line every remote
/// card surface is built to hold) to transmit a fact the receiver already has. The identity is
/// resolved LOCALLY here, gated by the identical <see cref="RevealGate.ShowRoundCardFronts"/> call
/// the board slots, the hand fan, the pile fans and the held-card face all make.</para>
///
/// <para>THE CHOREOGRAPHY MIRRORS THE OWNER'S, TERM FOR TERM. The owner's card lies on his board
/// while the game's <c>BurnCardTimeline</c> plays on it (<c>burnTime = 2 s</c>) and only then
/// flies into his Burnt stack on a <see cref="VRCard.FlyToPile"/> arc. This plays the same two
/// phases against the SENDER'S OWN synced board pose: hold at their board anchor with the burn
/// ramping on the real face, then the same arc to their Burnt stack over
/// <see cref="NetProtocol.CardFxSeconds"/>. Both clocks start from the same host-replicated pile
/// change, so they are in step by construction rather than by a timestamp we would have had to
/// send.</para>
///
/// <para>THE WIRE EVENT IS NOT REMOVED — it is CONSUMED. While this mirror is presenting a burn for
/// this owner, <see cref="ConsumesWireEvent"/> swallows the matching <c>Board -&gt; Burnt</c>
/// event so there is never a second flight; when this mirror cannot present one (their board is
/// hidden, the reveal gate is shut, no hand on this client, the face would not clone) the event
/// passes through and the peer gets exactly the back slab he got before. Degrading to the old
/// picture is the failure mode, never nothing.</para>
///
/// <para>NOTHING HERE WRITES GAME STATE. Every list is walked read-only, the face is a throwaway
/// CLONE built by <see cref="RemoteCardArt"/>, the burn look is rebuilt on materials that overlay
/// mints and destroys (never the game's own <c>CardEffects</c>, which cannot run on a detached
/// clone and would write a pooled widget — see <c>RemoteCardArt.BuildBurnRig</c>), and the body
/// mesh comes out of <see cref="CardMesh"/>'s shared cache.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. The burned card's identity is resolved
/// from the host-replicated <c>CCharacterClass.LostAbilityCards</c> list this client already reads
/// for the peer's burnt-pile fan, never from a packet, and every face is gated by
/// <see cref="RevealGate"/>. No new wire field, no new record, no byte added to any packet. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteBurnFx
{
    /// <summary>
    /// CEILING on how long a burned card may be held before it flies, in seconds — NOT a duration
    /// any more.
    ///
    /// <para>IT STOPPED BEING A DURATION IN THIS BUILD, and that is report item 9's third clause.
    /// The hold now lasts exactly as long as the OWNER's own card is still seated in the recess this
    /// client mirrors: <see cref="Drive"/> hands over to the arc on the frame that seat empties, and
    /// a burn this client could never seat flies at once. Both are strictly closer to the owner's
    /// timing than a fixed two seconds was, because the seat emptying IS his flight starting. What
    /// this number still does is stop a card being held forever by a recess that never lets go.</para>
    ///
    /// <para>MIRRORED CONSTANT, and the thing it mirrors is the GAME's, not ours:
    /// <c>CardEffects.BurnCardTimeline</c> opens with <c>float burnTime = 2f</c>
    /// (CardEffects.cs:515) and that loop is the whole visible burn. The owner's own hold ends when
    /// that timeline's coroutine handle goes null (<c>CardsDriver.BurnArtworkActive</c>), so this
    /// is the same duration reached from the other side rather than a second dial. It is
    /// deliberately NOT <c>CardsDriver.BurnEffectMaxHoldSeconds</c> (3 s): that is the owner's
    /// belt for a burn whose artwork never starts, and copying a CEILING as if it were a duration
    /// is what made every burn on ModBuild 447 run the full 3 s.</para>
    /// </summary>
    private const float HoldSeconds = 2f;

    /// <summary>
    /// Seconds between two walks of the peer's host-replicated Lost pile — THE UPPER BOUND ON HOW
    /// LATE THIS MIRROR CAN LEARN OF A BURN, and therefore on report item 9's first clause ("Die
    /// Animation beim remote Board … ist immer noch etwas verzögert").
    ///
    /// <para>IT IS NOT <c>RemoteBoardContent.RefreshSeconds</c> ANY MORE, and the two really are
    /// different questions. That cadence paces a STATE mirror — occupancy, counts, faces — where a
    /// quarter-second of staleness is invisible because the picture it produces is the same picture
    /// a moment later. This is an EVENT edge, and the whole of it is WHEN: the ModBuild 462 host log
    /// carries five mirrored burns and every one of them reports 255-259 ms between the walk that
    /// found it and the walk before, i.e. the cadence itself was the entire measured delay on every
    /// burn in the session. Sharing a dial with a state mirror was the reason.</para>
    ///
    /// <para>WHAT IT COSTS. The walk is two list scans over lists this client already holds
    /// (<c>CardsGameApi.GetPileWidgets</c> → <c>AppendPileWidgets</c>, the character's Lost pile
    /// against <c>hand.cardsUI</c>) plus a hash-set diff — no <c>FindObjectsOfType</c>, no
    /// allocation, no component search. Three times the rate of a 4 Hz pass on a handful of peers is
    /// not measurable beside the board pass it used to ride on.</para>
    ///
    /// <para>IT DOES NOT SET THE VISIBLE START ANY MORE, WHICH IS WHY IT ONLY HAD TO BE SMALLER AND
    /// NOT ZERO. Since <see cref="Drive"/> hands over on the recess-emptying edge, a burn discovered
    /// well before the owner's card leaves its seat costs nothing at all; this number is the delay
    /// only for a burn that is discovered AFTER the seat has already gone — the short-rest case.</para>
    /// </summary>
    private const float WatchSeconds = 0.08f;

    /// <summary>Concurrent burn presentations. A two-card damage burn commits both cards in the
    /// SAME frame — the 2026-09-05 host log shows exactly that pair — so one is not enough; beyond
    /// this the oldest is recycled, because a burn animation is cosmetic and must never be a queue
    /// that backs up.</summary>
    private const int MaxBurns = 3;

    /// <summary>Slack added to (hold + flight) before this mirror stops claiming the owner's
    /// <c>Board -&gt; Burnt</c> wire event. The owner reports the flight when HIS hold releases,
    /// which is up to his own 3 s ceiling after the pile change this mirror triggered on — so the
    /// window has to outlast the difference or a claimed burn would still get a second back slab.
    /// </summary>
    private const float ClaimSlackSeconds = 3f;

    private readonly RemoteAvatar _owner;

    private sealed class Burn
    {
        public GameObject? Go;
        public RemoteCardArt? Art;
        public float Elapsed;
        public bool Active;
        public bool HasFace;
        public Vector3 From;
        public Vector3 To;
        public float Arc;

        /// <summary><c>CAbilityCard.CardInstanceID</c> of the card being burned, so the recess that
        /// is drawing it can be re-identified every frame rather than once.</summary>
        public int CardId;

        /// <summary>The recess this card was lying in when the burn was learned about (0/1), or -1.
        /// It decides both the ORIGIN of the flight and whether this presentation draws a slab at
        /// all during the hold — see <see cref="Drive"/>.</summary>
        public int Recess;

        /// <summary>Card name, kept only so the hand-over line can name the same card the
        /// <c>BURN CARD</c> line above it named.</summary>
        public string Name = "?";

        /// <summary>Frames the owner's recess was still DRAWING this card, i.e. the length of the
        /// hold this presentation actually observed rather than the one it used to run on a clock of
        /// its own. Zero means the card was never seated where this client could see it.</summary>
        public int SeatedFrames;

        /// <summary>Has the arc been reported? One line per burn, at the hand-over instant.</summary>
        public bool HandoverLogged;

        /// <summary>What this slab's card BODY is wearing on its FRONT fan (true = the card back) —
        /// the edge gate for <see cref="SetFrontFace"/>. Seeded true because that is what
        /// <see cref="Acquire"/> builds it with.</summary>
        public bool WearsBack = true;
    }

    private readonly List<Burn> _burns = new(MaxBurns);
    private GameObject? _root;

    // ---- the model watch (the whole identity story) ------------------------------------------
    private readonly List<AbilityCardUI> _burntBuf = new(16);
    private readonly HashSet<AbilityCardUI> _known = new();
    private readonly List<AbilityCardUI> _pruneScratch = new(4);
    private int _watchActor;                 // stable id of the actor the baseline belongs to
    private bool _seeded;
    private float _nextWalkAt;

    /// <summary>Unscaled time and frame of the PREVIOUS burnt-pile walk. THE DELAY LIVES HERE and
    /// report item 7 names it ("diese war verzögert"): this class learns of a burn by POLLING the
    /// host-replicated Lost pile at <see cref="WatchSeconds"/>, so the burn happened
    /// somewhere inside the window these two numbers bound. Printed on the BURN CARD line rather
    /// than reasoned about, because "the poll cadence" and "the packet was late" look identical in
    /// a log without them.</summary>
    private float _lastWalkAt = float.NegativeInfinity;
    private int _lastWalkFrame = -1;

    /// <summary>Seconds between the walk that found the current burn and the one before it (-1 on
    /// the very first walk) — the upper bound on how long this mirror sat on a burn it had not
    /// looked for yet.</summary>
    private float _sinceLastWalk = -1f;

    /// <summary>Unscaled time of the most recent presentation start — the claim window for
    /// <see cref="ConsumesWireEvent"/>.</summary>
    private float _lastPresentedAt = float.NegativeInfinity;

    /// <summary>How many of the owner's <c>Board -&gt; Burnt</c> events this mirror still owes a
    /// swallow, i.e. how many burns it has presented whose wire event has not arrived yet.
    ///
    /// <para>A COUNT AND NOT JUST A TIME WINDOW, because the two failure modes are different burns.
    /// The window has to outlast the owner's own hold (he reports the flight when HIS hold
    /// releases, up to his 3 s ceiling after the pile change this mirror triggered on), and a bare
    /// window that wide would also swallow the event for a SECOND burn that this mirror could not
    /// present at all — costing the player the anonymous back slab he would otherwise still get.
    /// One token per presentation means each presented burn eats exactly its own event, and an
    /// unpresented one always falls through.</para></summary>
    private int _claims;

    private int _played;

    internal RemoteBurnFx(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ the wire hand-off --

    /// <summary>
    /// Should the owner's <c>Board -&gt; Burnt</c> card-FX event be SWALLOWED because this mirror is
    /// already showing that burn on the real card? Any other endpoint pair is never claimed.
    ///
    /// <para>Time-boxed on purpose: a claim that outlived its presentation would silently delete a
    /// later burn's animation, and a burn this mirror never saw must still reach the player as the
    /// old back slab.</para>
    ///
    /// <para>A LOST <c>CARD FX</c> EVENT CANNOT DELAY OR MISPLACE A BURN, and that is worth stating
    /// here because the ModBuild 462 host log carries <c>CARD FX LOST … 3 of that peer's
    /// card-animation event(s) never reached this client (8 seen)</c> beside the burn delay of
    /// report item 9, and the two look related. They are not. This class is the ONLY producer of a
    /// mirrored burn and it is driven entirely by <see cref="Watch"/>'s local walk of the peer's
    /// host-replicated Lost pile; the wire event's sole role is the one this method plays —
    /// SUPPRESSING a duplicate. A dropped event therefore costs at most a DISCARD flight (which
    /// <see cref="RemoteCardFx"/> owns), never a burn's timing and never its origin. The extras
    /// stream's first-of-a-pair gap is real and is somebody else's item; it is not this one.</para>
    /// </summary>
    internal bool ConsumesWireEvent(byte endpoints)
    {
        if (NetCardFx.To(endpoints) != CardFxAnchor.Burnt)
            return false;
        if (_claims <= 0)
            return false;
        // The window is the STALE-TOKEN sweep, not the claim itself: an event that never arrived
        // (a dropped packet on an unreliable stream, an owner whose report was suppressed by a
        // read-only character focus) must not leave a token behind to eat the NEXT burn's event.
        if (Time.unscaledTime - _lastPresentedAt > HoldSeconds + NetProtocol.CardFxSeconds + ClaimSlackSeconds)
        {
            _claims = 0;
            return false;
        }
        _claims--;
        return true;
    }

    // ------------------------------------------------------------------ per frame --

    internal void Tick(float dt)
    {
        try
        {
            Watch();
        }
        catch (System.Exception ex)
        {
            // Never throw out of the avatar tick: an unguarded throw there starves VR input.
            _seeded = false;
            VRLog.Warn("Net", $"Remote burn watch [player {_owner.PlayerId}] threw ({ex.Message}) — " +
                              "the baseline is re-seeded silently, so no burn storms when it recovers.");
        }
        Drive(dt);
    }

    /// <summary>
    /// Diff the displayed character's BURNT pile against the previous walk. A new entry is a burn
    /// this client has just learned about, at the same instant the owner's own watcher learns it —
    /// both read the same host-replicated list.
    ///
    /// <para>THE BASELINE IS SEEDED SILENTLY on the first walk and on every actor change, exactly
    /// as <c>CardsDriver.TickBurnToPile</c> re-seeds <c>_knownBurntWidgets</c> on a hand change and
    /// for the same reason: a character's long-burned cards must never animate retroactively when
    /// the board starts presenting them.</para>
    ///
    /// <para>It keeps walking while the peer's board is HIDDEN and simply presents nothing, so
    /// switching <c>[Net] RemoteBoards</c> back on cannot replay a scenario's worth of burns.</para>
    /// </summary>
    private void Watch()
    {
        if (Time.unscaledTime < _nextWalkAt)
            return;
        _sinceLastWalk = _lastWalkFrame < 0 ? -1f : Time.unscaledTime - _lastWalkAt;
        _lastWalkAt = Time.unscaledTime;
        _lastWalkFrame = Time.frameCount;
        _nextWalkAt = Time.unscaledTime + WatchSeconds;

        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
        int actorId = NetFigures.StableActorId(actor);
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? hand = actor != null && manager != null ? manager.GetHand(actor) : null;
        if (hand == null)
        {
            // No hand on this client for that character — forget the baseline rather than diffing
            // against a stale one when it comes back.
            _seeded = false;
            _known.Clear();
            _watchActor = 0;
            return;
        }
        if (actorId != _watchActor)
        {
            _watchActor = actorId;
            _seeded = false;
            _known.Clear();
        }

        _burntBuf.Clear();
        CardsGameApi.GetPileWidgets(hand, burnt: true, _burntBuf);

        if (!_seeded)
        {
            _seeded = true;
            for (int i = 0; i < _burntBuf.Count; i++)
            {
                if (_burntBuf[i] != null)
                    _known.Add(_burntBuf[i]);
            }
            return;
        }

        for (int i = 0; i < _burntBuf.Count; i++)
        {
            AbilityCardUI widget = _burntBuf[i];
            if (widget == null || !_known.Add(widget))
                continue;
            // The SAME membership expression the owner's own browse arc and the mirrored pile fan
            // apply — a long-rest placeholder or a widget with no model card is not a burn.
            if (!CardsGameApi.PileWidgetIsArcMember(widget))
                continue;
            Present(widget, actor);
        }

        // Drop anything that LEFT the pile (a recovered lost card) so a re-burn animates again.
        // Through a reused scratch list rather than RemoveWhere: this runs per peer at the board
        // cadence and a lambda that captures `this` allocates a delegate on every call.
        _pruneScratch.Clear();
        // Membership in the game's own list is the whole test: a widget the game has destroyed is
        // no longer in it either, so a separate Unity-null test would only add a branch (and a
        // nullable-flow suppression) for a case this already covers.
        foreach (AbilityCardUI k in _known)
        {
            if (!_burntBuf.Contains(k))
                _pruneScratch.Add(k);
        }
        for (int i = 0; i < _pruneScratch.Count; i++)
            _known.Remove(_pruneScratch[i]);
        _pruneScratch.Clear();
    }

    /// <summary>Start one burn presentation for <paramref name="widget"/>.</summary>
    private void Present(AbilityCardUI widget, CPlayerActor? actor)
    {
        string name = CardsGameApi.CardName(widget);

        // BOARD FURNITURE. Both endpoints of this presentation are on the owner's board, so a
        // viewer who has switched that board off must not get a card burning in mid-air — the same
        // rule and the same predicate RemoteCardFx applies to every flight that touches the board.
        if (!RemoteBoardGate.ShowBoardSurface(_owner) || !_owner.HasBoard)
        {
            LogSkipped(name, "this viewer is not showing that peer's board right now");
            return;
        }

        // ─── WHERE THE OWNER'S CARD ACTUALLY IS (2026-09-06 follow-up, corrected for item 7) ─────
        // It is in a RECESS, not at the board centre: CardsDriver holds the played card on its slot
        // anchor while the game's burn artwork runs on it and only then flies it to the stack. This
        // mirror used to present at CardFxAnchor.Board because nothing named the recess — but this
        // client SEATED that card there itself, so it can simply ask. No wire field is owed: the
        // anchor vocabulary already has Slot0/Slot1 and RemoteControlBoard.AnchorLocalLive resolves
        // both to the rendered card SEAT of whatever board style the peer runs.
        //
        // IT ASKS RecessOfBurningCard, NOT RecessShowingCard, AND THAT WAS THE WHOLE OF ITEM 7.
        // RecessShowingCard answers "which recess is drawing this card's FACE", which is an
        // IDENTITY question standing in for a POSITION one — so a recess that is occupied but
        // drawing an anonymous back places the card nowhere. The ModBuild 461 log has exactly two
        // mirrored burns and that happened on ONE of them: 'BURN CARD ... at their board CENTRE'
        // beside 'round-card faces=anon-back/empty, slot-occupancy=0x1'. A fallback documented as
        // being for "a burn nobody could place" fired on half the burns in the round.
        int cardId = CardInstanceIdOf(widget);
        int recess = _owner.RecessOfBurningCard(cardId, out string recessHow);
        CardFxAnchor origin = recess == 0 ? CardFxAnchor.Slot0
            : recess == 1 ? CardFxAnchor.Slot1
            : CardFxAnchor.Board;

        if (!TryAnchor(origin, out Vector3 from)
            || !TryAnchor(CardFxAnchor.Burnt, out Vector3 to))
        {
            LogSkipped(name, "their board pose has not arrived yet, so there is nowhere to burn it");
            return;
        }

        Burn b = Acquire();
        if (b.Go == null)
            return;
        b.CardId = cardId;
        b.Recess = recess;
        b.Name = name;
        b.SeatedFrames = 0;
        b.HandoverLogged = false;

        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        float cardWidth = Mathf.Max(0.01f, _owner.BoardTuning.CardWidth);
        float widthRatio = cardWidth / RemoteHandFan.DefaultCardWidth;
        float cardHeight = cardWidth * (88f / 63.5f);

        b.Elapsed = 0f;
        b.Active = true;
        b.From = from;
        b.To = to;
        // CardsDriver.BoardArcMin and VRCard.FlyArcHeightFraction, the same two terms RemoteCardFx
        // resolves for every other mirrored flight — read its ArcFraction note before touching
        // either number, they are code literals that simply have to be the same on both sides.
        b.Arc = Mathf.Max(cardHeight * 1.5f * scale, Vector3.Distance(from, to) * VRCard.FlyArcHeightFraction);
        b.Go.transform.localScale = Vector3.one * (scale * widthRatio);
        // LYING ON THEIR BOARD, not billboarded at us: the owner's card rests in a recess of a
        // board this client already knows the rotation of, and FlyToPile holds that orientation for
        // the whole flight ("orientation locked"). Facing it at the local head instead would be a
        // pose the owner never sees.
        b.Go.transform.SetPositionAndRotation(from, _owner.BoardRotation);
        // …AND IT STARTS HIDDEN, ALWAYS. The slab's only appearance is the ARC (report item 9's
        // third clause — the viewer may never be shown a mini card lying at the flight's origin),
        // and Drive() is the one place that reveals it, on the frame the recess stops drawing this
        // card. Two copies of one card is also a worse divergence than the one this class was built
        // to fix, and the recess copy is the better of the two by construction: it is the card the
        // owner is looking at, in the recess he is looking at, wearing the char
        // RemoteBoardCard.DriveUsedCardFx is ramping on it.
        if (b.Go.activeSelf)
            b.Go.SetActive(false);

        // THE FACE — resolved locally, gated exactly as every other remote card surface is.
        b.HasFace = false;
        bool fronts = false;
        try
        {
            // THE BURN CARVE-OUT (2026-09-06 report item 6). The user's ruling is absolute — "Beim
            // Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar
            // sein" — and it is implemented ONCE, in RevealGate.IsPubliclyRevealedCard, which now
            // answers TRUE for a card in either of that character's burnt lists. This surface gets
            // it by asking the card-aware overload instead of the bare phase predicate; so does the
            // Slot -> Burnt flight in RemoteCardFx and any surface added later. The old expression
            // (ShowRoundCardFronts alone) drew a BACK for the whole of a burn that happened inside
            // the peer's own selection window, which is exactly what he reported.
            fronts = actor != null
                     && RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor, cardId)
                        != RevealGate.CardFaceSource.None;
            FullAbilityCard? full = fronts && widget != null ? widget.fullAbilityCard : null;
            if (full != null && b.Art != null)
                b.HasFace = b.Art.ShowFront(full);
        }
        catch (System.Exception ex)
        {
            b.HasFace = false;
            VRLog.Warn("Net", $"Remote burn face [player {_owner.PlayerId}]: '{name}' front resolve " +
                              $"failed ({ex.Message}) — burning a card BACK, which is what every build " +
                              "before this one showed.");
        }
        if (!b.HasFace)
            b.Art?.HideFront();
        // …AND WHAT THE BODY UNDER THE PRINT WEARS (user item 10, 2026-09-06 late). Read off the
        // SAME expression that decides whether a front is drawn at all — the HideFront above — and
        // not off b.HasFace later in the flight, because Drive() re-uses that field to mean "the
        // burn rig is still driving this face" and clears it while the print is still up. One
        // decision, expressed once. CardMesh.SetBodyFrontFace owns the rule and the fade-aware
        // write; a burn showing a BACK keeps the back on both submeshes.
        SetFrontFace(b, showsBack: !b.HasFace);

        _played++;
        _lastPresentedAt = Time.unscaledTime;
        _claims++;
        // THE SAME CENSUS POPULATION RemoteCardFx REPORTS (2026-09-06 report item 5). A burn IS a
        // card flying into a stack, and it reaches the viewer through this class instead of that one
        // only because ConsumesWireEvent swallowed the event — an implementation split, not a
        // different picture. Reporting from both is what makes "flight slab" a statement about every
        // mirrored flight rather than about the ones one of the two classes happened to draw.
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.FlightSlab, _owner.PlayerId,
            b.HasFace ? 1 : 0, b.HasFace ? 0 : 1,
            b.HasFace
                ? "BURN: FRONT off this client's own copy of that character's LostAbilityCards"
                : fronts
                    ? "BURN: RevealGate was OPEN but the front did not resolve — read the "
                      + "'BURN CARD [peer n]' line beside this for which half failed"
                    : "BURN: RevealGate.ShowRoundCardFronts(actor)=false — the game's own secret "
                      + "SelectAbilityCardsOrLongRest window for a remote character");
        LogAttribution(name, fronts, b.HasFace, actor, recess, recessHow);
    }

    /// <summary>Advance every live presentation: hold with the burn ramping on, then the arc.</summary>
    private void Drive(float dt)
    {
        float step = Mathf.Max(dt, 0f);
        for (int i = 0; i < _burns.Count; i++)
        {
            Burn b = _burns[i];
            if (!b.Active || b.Go == null)
                continue;
            b.Elapsed += step;

            // THE OWNER'S BOARD IS A MOVING FRAME, and this presentation is 2.4 s long — long
            // enough that a board the owner pulls toward himself mid-burn would leave the card
            // hanging in the air where the board used to be. Both endpoints are therefore
            // re-resolved every frame against their LIVE synced pose (RemoteCardFx resolves once
            // because its flights last 0.4 s; that shortcut does not survive a 2 s hold). A frame
            // in which the pose cannot be resolved keeps the last one rather than snapping.
            // …and the ORIGIN is the owner's RECESS whenever this client can still see the card
            // seated there. Re-asked every frame rather than latched: the recess empties the moment
            // the owner's occupancy nibble clears, which is the same instant HIS card leaves it, and
            // that is the hand-over this presentation has to survive. It falls back to the board
            // centre only for a burn nobody could place — the picture every build before this one
            // drew.
            CardFxAnchor origin = b.Recess == 0 ? CardFxAnchor.Slot0
                : b.Recess == 1 ? CardFxAnchor.Slot1
                : CardFxAnchor.Board;
            if (TryAnchor(origin, out Vector3 liveFrom))
                b.From = liveFrom;
            if (TryAnchor(CardFxAnchor.Burnt, out Vector3 liveTo))
                b.To = liveTo;

            if (b.Elapsed < HoldSeconds)
            {
                // PHASE 1 — it lies on their board and chars, exactly as it does on theirs, AND THE
                // ONLY THING THAT MAY DRAW IT IS THEIR OWN RECESS. RemoteBoardCard.DriveUsedCardFx
                // is ramping the very same RemoteCardArt rig on that seated face, off the owner's
                // own effect state, so a slab here would be a SECOND copy of one card.
                //
                // ─── AND WHEN THE RECESS STOPS DRAWING IT, THE CARD FLIES. IT NEVER LIES. ────────
                // 2026-09-06 late report, item 9, third clause, verbatim: "Ich will aber gar nicht
                // sehen, wie die Karte in mini auf dem Board liegt (Startposition der Animation),
                // das soll unmittelbar nach dem Verschwinden geschehen, so wie es lokal auch der
                // Fall ist, damit der Eindruck entsteht, die Karte würde direkt in den jeweiligen
                // Stapel gehen." That is a REQUIREMENT and not only a bug: a stationary mini slab at
                // the flight's origin may never be a picture this class draws.
                //
                // WHAT USED TO HAPPEN, and why it read as a delay. The slab was HIDDEN while the
                // recess drew the card and REVEALED the instant it stopped — at the recess anchor,
                // stationary, for whatever was left of the two seconds. The recess stops drawing the
                // card exactly when the owner's occupancy nibble clears, which is exactly when HIS
                // card leaves the recess, which is exactly when HIS flight starts. So the mirror
                // was showing a still copy of a card the owner already had in the air, and only then
                // launching it.
                //
                // SO THE HAND-OVER IS THE EDGE, AND THAT IS STRICTLY MORE 1:1, NOT LESS. The hold no
                // longer runs a clock of its own: it lasts precisely as long as the owner's card is
                // still seated in the recess this client is mirroring, and the arc begins on the
                // frame that seat empties. HoldSeconds survives as the CEILING it always was for a
                // burn whose recess this client could never place (a short rest whose sacrifice had
                // already left, a hidden board) — and there the flight starts at once, because a
                // card nobody can seat has nowhere to lie either.
                bool recessDraws = b.CardId != int.MinValue
                                   && _owner.RecessShowingCard(b.CardId) == b.Recess
                                   && b.Recess >= 0;
                if (recessDraws)
                {
                    b.SeatedFrames++;
                    if (b.Go.activeSelf)
                        b.Go.SetActive(false);
                    continue;
                }
                // The recess is not drawing it, so nothing may hold it any longer: skip whatever is
                // left of the hold and fall through into the arc on this very frame.
                LogHandover(b, b.Elapsed, "their recess stopped drawing that card");
                b.Elapsed = HoldSeconds;
            }
            else
            {
                LogHandover(b, HoldSeconds, "the " + HoldSeconds.ToString("F1")
                    + "s ceiling ran out while their recess was STILL drawing the card");
            }

            // THE HAND-OVER. The flight always draws the slab, and it draws it ALREADY CHARRED: a
            // fresh card lifting out of a recess the viewer just watched blacken is the one way this
            // split could read worse than the single slab it replaced. Writing the settled state
            // every frame of the flight is the same idempotent call the burnt-pile fan makes.
            if (!b.Go.activeSelf)
                b.Go.SetActive(true);
            if (b.HasFace && b.Art != null && !b.Art.SetAbilityBurnProgress(1f))
                b.HasFace = false;

            // PHASE 2 — the same over-the-board arc every other pile flight uses.
            float t = NetProtocol.CardFxSeconds > 0f
                ? Mathf.Clamp01((b.Elapsed - HoldSeconds) / NetProtocol.CardFxSeconds)
                : 1f;
            float e = t * t * (3f - 2f * t);
            // WORLD up, never the owner's board up — VRCard.FlyToPile deliberately throws the
            // caller's board-up away so a tilted board can never lean the arch sideways, and
            // RemoteCardFx carries that sentence verbatim. Same rule here.
            Vector3 p = Vector3.Lerp(b.From, b.To, e) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * b.Arc);
            b.Go.transform.position = p;
            if (t >= 1f)
            {
                b.Active = false;
                b.Art?.HideFront();
                b.HasFace = false;
                // A parked slab with no print on it wears the card BACK again — the state every
                // path that tears a front down has to leave behind, so a pooled slab can never be
                // re-shown with an edge ring around a bare back.
                SetFrontFace(b, showsBack: true);
                b.Go.SetActive(false);
            }
        }
    }

    // ------------------------------------------------------------------ anchors + pool --

    /// <summary><c>CAbilityCard.CardInstanceID</c> of a pile widget's model card, or
    /// <see cref="int.MinValue"/> when it has none. It is the key <c>RemoteBoardCard.Set</c> stores
    /// for the card it seated, so the two surfaces are comparing the same identifier rather than two
    /// that happen to agree.</summary>
    private static int CardInstanceIdOf(AbilityCardUI? widget)
    {
        try
        {
            CAbilityCard? card = widget != null ? widget.AbilityCard : null;
            return card != null ? card.CardInstanceID : int.MinValue;
        }
        catch (System.Exception)
        {
            return int.MinValue;
        }
    }

    private bool TryAnchor(CardFxAnchor anchor, out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (_owner.BoardAnchorLocal(anchor) * scale);
        return true;
    }

    /// <summary>Tell this burn slab's card BODY what its FRONT fan wears — see
    /// <c>CardMesh.SetBodyFrontFace</c>, which owns the rule and the (fade-aware) write.
    ///
    /// <para>EDGE-GATED, like every other caller: a steady burn never walks CardMesh's body
    /// registry. The gate lives on the Burn because up to <see cref="MaxBurns"/> presentations run
    /// at once and a damage burn commits two cards in the same frame, one of which may resolve a
    /// front while the other does not.</para>
    ///
    /// <para>MID-FADE: this class is a <c>PeerBoardFade</c> follower (<c>Always</c>) and its slab
    /// hangs over the owner's board for the whole burn, so a ramp really can be running underneath
    /// it. The write therefore goes through <c>PeerBoardFade.SetSubmeshMaterial</c>, which edits the
    /// remembered authored array and the installed clone for slot 0 only. That path is argued from
    /// <c>Swap</c>/<c>Restore</c>/<c>SettleDepthState</c> and is NOT hardware-verified.</para></summary>
    private static void SetFrontFace(Burn b, bool showsBack)
    {
        if (b.Go == null || b.WearsBack == showsBack)
            return;
        CardMesh.SetBodyFrontFace(b.Go.transform, showsBack);
        b.WearsBack = showsBack;
    }

    private Burn Acquire()
    {
        for (int i = 0; i < _burns.Count; i++)
        {
            if (!_burns[i].Active)
                return _burns[i];
        }
        if (_burns.Count >= MaxBurns)
        {
            Burn oldest = _burns[0];
            for (int i = 1; i < _burns.Count; i++)
            {
                if (_burns[i].Elapsed > oldest.Elapsed)
                    oldest = _burns[i];
            }
            oldest.Art?.HideFront();
            oldest.HasFace = false;
            SetFrontFace(oldest, showsBack: true);   // …the same teardown, one recycle earlier
            return oldest;
        }

        EnsureRoot();
        var b = new Burn();
        if (_root != null)
        {
            var go = new GameObject($"Burn{_burns.Count}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            float w = RemoteHandFan.DefaultCardWidth;
            float h = RemoteHandFan.DefaultCardHeight;
            // THE BODY IS SIZED TO THE FACE IT WILL WEAR, one level down, exactly as
            // RemoteHandFan builds its slabs (2026-09-06 report item 5, the third instance of one
            // defect). The flight slab used to carry the mesh on the ROOT at the full nominal
            // 63.5 x 88 mm while RemoteCardArt letterboxed the 294 x 450 px print inside it, so a
            // burning card flew across the table with 17.5 % more body than print at the sides.
            // The root STAYS uniform — Fly() writes its localScale and the print's world-space
            // canvas hangs off it, so a non-uniform scale here would stretch the art.
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, worldPositionStays: false);
            Vector2 vis = CardFace.VisibleFaceRect(w, h);
            body.transform.localScale = new Vector3(vis.x / w, vis.y / h, 1f);
            var filter = body.AddComponent<MeshFilter>();
            // The owner's punched-out ABILITY body, out of CardMesh's shared cache (never ours to
            // destroy) — the same body every other mirrored card slab wears.
            CardMesh.AttachBody(filter, CardBodyKind.Ability, w, h);
            var renderer = body.AddComponent<MeshRenderer>();
            Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability);
            renderer.sharedMaterials = new[] { back, back };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            VRLayers.Apply(go);
            b.Go = go;
            b.Art = new RemoteCardArt(go.transform, w, h)
            {
                // Names this face for the card-FX instrument, which latches PER SURFACE so a
                // term that arms on the recess and refuses on the flight is readable rather
                // than silent. See RemoteCardArt.FxSurface.
                Surface = RemoteCardArt.FxSurface.Flight,
            };
        }
        _burns.Add(b);
        return b;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteBurnFx[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.localScale = Vector3.one;
        // Same board fade every other mirrored surface follows (user item 7, 2026-09-02): a card
        // burning at full opacity over a board that is faded out is the wrongness he described.
        PeerBoardFade.Follow(_owner.PlayerId, _root.transform);
        VRLayers.Apply(_root);
    }

    internal void Destroy()
    {
        for (int i = 0; i < _burns.Count; i++)
            _burns[i].Art?.Destroy();
        _burns.Clear();
        _known.Clear();
        _burntBuf.Clear();
        _pruneScratch.Clear();
        _seeded = false;
        _claims = 0;
        if (_root != null)
        {
            // The body meshes are CardMesh's SHARED cache — never ours to destroy.
            Object.Destroy(_root);
            _root = null;
        }
    }

    // ------------------------------------------------------------------ instruments --

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-05 item 11a): the RECEIVER's answer to "which card was burned". Grep
    /// token <c>BURN CARD</c> — deliberately the SAME token the owner's
    /// <c>CardsDriver.LogBurnAttribution</c> prints, so one grep across the two hardware logs
    /// decides the 1:1 question with no arithmetic: for each burn the owner's line and this line
    /// must name the same card.
    ///
    /// <para>Event-driven (a handful per scenario), never per frame.</para>
    ///
    /// <para>PROOF the fix landed: a <c>[peer]</c> line naming the SAME card as the owner's
    /// <c>[owner/…]</c> line for that burn, with <c>face=REAL</c>.</para>
    ///
    /// <para>FALSIFIERS, and they say different things. (1) The two lines naming DIFFERENT cards:
    /// the identity resolve is wrong and 11a is NOT fixed — the next lead is
    /// <c>RemotePileFronts.Resolve</c>, which walks the same list. (2) No <c>[peer]</c> line at all
    /// beside an owner line: this mirror never armed, so the peer still gets the old back slab
    /// through <see cref="RemoteCardFx"/> — read the <c>BURN MIRROR SKIPPED</c> line for the
    /// reason. (3) The right card with <c>face=BACK</c>: the reveal gate or the clone refused, and
    /// only the FACE half of the fix is inert; the flight and the timing are still right.</para>
    /// </summary>
    private void LogAttribution(string name, bool fronts, bool face, CPlayerActor? actor, int recess,
                                string recessHow)
    {
        string where = recess >= 0
            ? $"in their round recess {recess + 1} ({recessHow}) — so this presentation holds and "
              + "flies from the seat their own card is lying in, not from the board centre"
            : $"at their board CENTRE, because {recessHow}; that is the pre-2026-09-06 picture and "
              + "the fallback, not the intent — it is report item 7's symptom and any occurrence "
              + "of it is a finding";
        // HW-VERIFY: grep token "BURN CARD" — the same token the OWNER's
        // CardsDriver.LogBurnAttribution prints, so one grep across the two hardware logs decides
        // the 1:1 question. See this method's doc for the three falsifiers.
        VRLog.Note("Net", $"BURN CARD [peer {_owner.PlayerId}]: that player burned '{name}' — " +
                          $"showing it {where}, charring in that seat for as long as their own card " +
                          $"is still in it (ceiling {HoldSeconds:F1}s), then flying " +
                          $"it into their Burnt stack ({NetProtocol.CardFxSeconds:F2}s). " +
                          $"face={(face ? "REAL" : "BACK")}, revealGate={(fronts ? "open" : "shut")}, " +
                          $"char='{Board.CharacterFocus.Describe(actor)}', burn #{_played}. TIMING: " +
                          $"found on the burnt-pile walk at frame {_lastWalkFrame}, " +
                          (_sinceLastWalk < 0f
                              ? "the FIRST walk for this character (no bound on the wait)"
                              : $"{_sinceLastWalk * 1000f:F0} ms after the previous walk — this " +
                                "mirror POLLS the host-replicated Lost pile at " +
                                $"{WatchSeconds * 1000f:F0} ms, so that number " +
                                "IS the upper bound on the delay report item 7 names; anything much " +
                                "larger than the cadence is a stalled board pass and not the poll") +
                          ", then the hold, which ENDS WHEN THEIR RECESS DOES — read the 'BURN " +
                          "FLIGHT' line beside this one for when the arc actually began. The identity " +
                          "was read from THIS client's own copy of that character's host-replicated " +
                          "LostAbilityCards list (the same CardsGameApi.GetPileWidgets call the mirrored " +
                          "burnt-pile fan uses) — NO card identity crossed the wire and no wire field " +
                          "was added. Compare with the owner's 'BURN CARD [owner/...]' line for the " +
                          "same burn: they must name the same card.");
    }

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-06 late report, item 9): the instant the mirrored burn stops
    /// being the owner's seated card and becomes a flight, and how long — if at all — a STATIONARY
    /// slab was visible before it. Grep token <c>BURN FLIGHT</c>. One line per burn, at the
    /// hand-over, never per frame.
    ///
    /// <para>WHY IT IS A SECOND LINE AND NOT A CLAUSE ON <see cref="LogAttribution"/>. That line is
    /// printed when the burn is DISCOVERED, and everything item 9 asks about happens afterwards: the
    /// hold's real length is not known until it ends, and the whole question is whether a slab was
    /// ever shown standing still. A report about a discovery cannot answer a question about a
    /// hand-over.</para>
    ///
    /// <para><b>WORKING</b> = one line per burn reading <c>stationary slab shown for 0.00s</c> — the
    /// number is a CONSTANT ZERO by construction now (the slab's only appearance is the arc), so any
    /// other value is a code defect and not a tuning question — with <c>seated for N frame(s)</c>
    /// non-zero and <c>arc began 0.5s..2.0s after discovery</c> for a card the owner played onto his
    /// board, beside a <c>BURN CARD</c> line naming a recess.</para>
    ///
    /// <para><b>INERT</b> = <c>arc began 0.00s after discovery</c> with <c>seated for 0 frame(s)</c>
    /// on a burn whose <c>BURN CARD</c> line DID name a recess: the recess never drew the card this
    /// client placed there, so the viewer got a flight with no char in front of it. The lead is then
    /// <c>RemoteControlBoard.RecessShowingCard</c> and the <c>ANONYMOUS RECESS</c> line, not this
    /// class. The same reading on a burn whose <c>BURN CARD</c> line named the board CENTRE is the
    /// CORRECT outcome, not a defect — a short-rest sacrifice that had already left its seat has
    /// nowhere to lie, and flying it at once is exactly what item 9 asks for.</para>
    ///
    /// <para><b>STILL BEYOND THE INSTRUMENT</b> = every line reading 0.00s stationary and the user
    /// still reporting a mini card lying on the board. This class would then not be the thing
    /// drawing it, and the next surface to look at is the RECESS mirror itself
    /// (<c>RemoteBoardCard</c>), which keeps drawing the burnt card face-up for as long as the
    /// owner's occupancy nibble says his own card is still seated — a picture the owner has too.</para>
    /// </summary>
    private void LogHandover(Burn b, float after, string why)
    {
        if (b.HandoverLogged)
            return;
        b.HandoverLogged = true;
        // HW-VERIFY: grep token "BURN FLIGHT" — see this method's doc for the three readings.
        VRLog.Note("Net", $"BURN FLIGHT [peer {_owner.PlayerId}]: '{b.Name}' leaves for their Burnt " +
                          $"stack now, {after:F2}s after this mirror discovered the burn, because " +
                          $"{why}. It was seated in their recess " +
                          $"{(b.Recess >= 0 ? (b.Recess + 1).ToString() : "(none — board centre)")} " +
                          $"for {b.SeatedFrames} frame(s) of that, and the STATIONARY SLAB WAS SHOWN " +
                          "FOR 0.00s — the slab this class owns is only ever revealed by the arc " +
                          "itself. THAT ZERO IS THE WHOLE OF REPORT ITEM 9's third clause ('Ich " +
                          "will aber gar nicht sehen, wie die Karte in mini auf dem Board liegt … " +
                          "das soll unmittelbar nach dem Verschwinden geschehen'): the hold is no " +
                          "longer a clock of its own, it lasts exactly as long as the OWNER's card " +
                          "is still seated in the recess this client mirrors, and the arc starts on " +
                          $"the frame that seat empties. {HoldSeconds:F1}s remains only as the " +
                          "ceiling for a card that never appears in a recess at all. A non-zero " +
                          "stationary figure would be a code defect, not a dial.");
    }

    /// <summary>
    /// HARDWARE VERIFICATION: why a burn this client KNOWS about was not presented. Without it, "the peer saw
    /// nothing" and "the peer saw the old back slab" are indistinguishable in a log. One line per
    /// skipped burn, event-driven.
    /// </summary>
    private void LogSkipped(string name, string reason)
    {
        // HW-VERIFY: grep token "BURN MIRROR SKIPPED" — it is what separates "the peer saw nothing"
        // from "the peer saw the old anonymous back slab", which a log otherwise cannot tell apart.
        VRLog.Note("Net", $"BURN MIRROR SKIPPED [player {_owner.PlayerId}]: '{name}' burned, but " +
                          $"{reason}. The owner's own Board→Burnt card-FX event is therefore NOT " +
                          "claimed and still plays as the anonymous card-back slab (RemoteCardFx), " +
                          "which is exactly the picture every build before this one showed.");
    }
}
