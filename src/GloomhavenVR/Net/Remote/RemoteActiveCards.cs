using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Active / persistent cards — PER-ACTOR MODEL
// =================================================================================================

/// <summary>
/// A peer's ACTIVE (round-long / persistent) ability cards, drawn as a small column off the far
/// RIGHT edge — the mirror of the local board's <c>ActivePileViewer</c> and at the same base offset
/// (<c>PlayTray.ActiveMountBase</c> = board half-width + 0.012 + <c>ActiveMountOffsetX</c>), just past
/// the pile stacks.
///
/// SOURCE (per-actor model, zero wire): <c>CCharacterClass.ActivatedAbilityCards</c> — the very list
/// the local <c>ActivePileViewer</c> is fed from, read off the host-replicated actor.
///
/// MB487: active cards follow the same phase visibility as held, fan and placed cards. The
/// historical AlreadyPublic population name is retained but grants no selection-phase exemption.
///
/// <para>CLOSED — THE 0.4 s OVERLAP, WHICH WAS ALSO THE WRONG-TIMING HALF OF USER ITEM 2. Since
/// ModBuild 462 a card going active also flies a mirrored slab into this column
/// (<c>CardFxAnchor.Active</c>), and this column is rebuilt from the host-replicated
/// <c>ActivatedCards</c>, which holds the card from the moment the model moved it. So for the
/// flight's 0.4 s an observer saw the card SEATED IN ITS CELL and a second copy of it in the AIR,
/// while the owner sees exactly one — their flying card IS the cell's card
/// (<c>ActivePileViewer.Relayout</c> hands it the cell and <c>VRCard</c> ignores its home while
/// <c>IsFlying</c>), so the owner's cell is EMPTY for the whole arc and the card APPEARS when the
/// arc lands.</para>
///
/// <para>THE USER READ THAT AS THE FLIGHT HAVING ALREADY HAPPENED, and said so (2026-09-07, item 2,
/// verbatim): "Der Mitspieler hat eine Karte aktiviert und ich habe auf dem remote baord direkt
/// schon den Flug zur Aktiven karten gesehen. … Am Ende des Zuges am remote board gab es dann gar
/// kein FLug mehr wo der Flug hätte sein sollen zu den aktiven Karten (weil es ihn schon bereits
/// davor gab). Am lokalen Board passt alles." A cell that fills BEFORE its arc is a card that has
/// arrived, and an arc that then lands on an already-occupied cell delivers nothing — so the
/// mirror's active card gained no moment of arrival at all. 1:1 covers TIMING, and the owner's
/// timing is: empty cell, arc, card.</para>
///
/// <para>THE FIX IS A CONSUMER, NOT A MECHANISM. <c>RemoteCardFx</c> has stamped the flown card's
/// <c>CardInstanceID</c> on its <c>Flight</c> and exposed <c>bool IsFlyingToActive(int)</c> since
/// that half shipped — and until this build NOTHING CALLED IT. A producer with no consumer reads
/// exactly like a fix that is in place, which is why the overlap survived the build that "closed"
/// it. <see cref="Refresh"/> now asks (through <c>RemoteAvatar.IsCardFlyingToActive</c>) and blanks
/// that card's cell through the SAME <c>Set(null, showFronts, actor)</c> the held-seat suppression
/// below uses — one hiding mechanism, no second one to keep in step. A DERIVED CLOCK WAS
/// DELIBERATELY REJECTED: this surface refreshes at 4 Hz (<c>RemoteBoardContent.RefreshSeconds</c>)
/// and the arc lasts 0.4 s, so a timer started from the arrival this class can see would blank the
/// cell for up to a full arc AFTER the slab had already landed — a card popping out of the matrix
/// and back. The flight's own clock cannot drift from the flight.</para>
///
/// <para>AND A LOST <c>-&gt; Active</c> EVENT COSTS NOTHING. The extras stream is unreliable by
/// contract; a dropped event means no flight starts, <c>IsFlyingToActive</c> answers false, and the
/// cell draws immediately — the pre-ModBuild-462 picture, i.e. a missing animation and never a
/// hidden card. <see cref="ReportArrivals"/> is keyed on the DRAWN set for the same reason, so its
/// line still fires exactly once per activation, at the moment the card actually becomes visible
/// here.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. Source:
/// <c>CCharacterClass.ActivatedAbilityCards</c> off the host-replicated actor
/// (<c>NetPlayerActors.ActorFor</c>), fronts gated by <see cref="RevealGate"/>. Card IDENTITY is
/// DELIBERATELY-NOT on the wire, ever — it is resolved locally through the gate instead. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteActiveCards
{
    /// <summary>
    /// LEGACY active-card width — what this column drew before ModBuild 305, kept ONLY as the
    /// fallback for a sender whose card width is not on the wire.
    ///
    /// <para>IT WAS WRONG AND IT WAS WRONG AT THE DEFAULTS. The owner draws their active cards at
    /// <c>[Cards] CardWidth</c> (0.0635 m shipped) times their <c>ActiveCardScale</c>; this column
    /// drew them at a bare 0.075 m — an <b>18 % oversize on every peer's board with nobody having
    /// tuned anything</b>, plus the same 18 % on the grid step derived from it. It is the exact
    /// defect the 1:1 rule names ("gleiche Position, gleiche Größe") and it survived every checker
    /// because the coverage guard watches DIALS: it saw that [Cards] ActiveGridSpacing was unwired
    /// and had no way to see that the metric underneath it was a constant.</para>
    /// </summary>
    private const float LegacyCardW = 0.075f;

    /// <summary>The owner's own column count, CALLED not copied — <c>ActivePileViewer.Columns</c>.
    /// It was a hand-typed <c>2</c> against the owner's <c>3</c> from the day this file was written
    /// (the local grid went 3-wide eight days earlier, 85bbb8ca), so with three active cards the
    /// owner read one row of three and every teammate read a 2+1 block one row taller and one
    /// column narrower, re-centred to a different height against the board's other docks. The 1:1
    /// ruling names exactly that, and the layout note below already claimed the construction was
    /// copied "term for term": this was the one term that was not.</summary>
    private static int Columns => ActivePileViewer.Columns;

    /// <summary>The owner's own card width (record 28 id 70) — the metric their active cards are
    /// drawn at, before the column's <c>ActiveCardScale</c>. Falls back to
    /// <see cref="LegacyCardW"/>.</summary>
    private readonly float _cardW;

    /// <summary>…and its height, in the game's own 88:63.5 card ratio, exactly as the owner derives
    /// theirs (<c>CardsConfig.CardHeight</c>).</summary>
    private readonly float _cardH;

    /// <summary>The owner's <c>[Cards] ActiveGridSpacing_{board}</c> — (column, row) multiples of
    /// the card size. The row factor used to be a hardcoded 0.72 against the owner's shipped 0.70:
    /// wrong even before anybody tuned it.</summary>
    private readonly Vector2 _grid;

    /// <summary>
    /// How many card slots this column BUILDS UP FRONT. It is a seed, not a cap — <see cref="Refresh"/>
    /// grows the list to whatever the actor's active pile holds, exactly as the owner's
    /// <c>ActivePileViewer</c> does (its list is uncapped).
    ///
    /// <para>IT USED TO BE A CAP, <c>MaxCards = 6</c>, AND THE SEVENTH ACTIVE CARD WAS INVISIBLE TO
    /// EVERY TEAMMATE — the owner drew it and nobody else did. It is safe to lift because this
    /// column is a PER-ACTOR MODEL read with ZERO wire (see the class remarks): the list comes off
    /// the host-replicated <c>CCharacterClass.ActivatedAbilityCards</c>, so no packet field bounds
    /// it and no receiver buffer can overrun. Seven-plus active cards is rare, so the seed stays at
    /// the old number and the growth path allocates only when it is actually exceeded.</para>
    /// </summary>
    private const int InitialSlots = 6;

    /// <summary>Whose board this column belongs to — diagnostics only.</summary>
    private readonly int _playerId;

    private readonly Transform _root;
    private readonly TextMeshPro _title;
    private readonly List<RemoteBoardCard> _cards = new(InitialSlots);
    private readonly List<CAbilityCard> _buffer = new(InitialSlots);
    private readonly Dictionary<int, RemoteBoardCard> _panelsByCard = new(InitialSlots);
    private readonly List<RemoteBoardCard> _orderedPanels = new(InitialSlots);
    private readonly HashSet<RemoteBoardCard> _assignedPanels = new();
    private readonly Dictionary<int, int> _heldPoseSlots = new(2);
    private readonly List<int> _pruneIds = new(InitialSlots);

    /// <summary>How many active cards the column currently draws (diagnostics).</summary>
    public int Count { get; private set; }

    /// <summary>The peer whose matrix this is — read for record 36's held seats (see
    /// <see cref="RemoteAvatar.HeldActiveSeats"/>), which is the only reason this class needs more
    /// than a player id.</summary>
    private readonly RemoteAvatar _owner;

    /// <summary>The pulsing ACTIVE REGION on this board's mirrored active cards, and on that
    /// peer's fist while they are holding one — user item 3 of 2026-09-07. Zero wire; it resolves
    /// the half off the same local model this column reads. See
    /// <see cref="RemoteActiveCardPulse"/>.</summary>
    private readonly RemoteActiveCardPulse _pulse;

    /// <summary>Cells held back this pass because that card is in the AIR on its way here (user
    /// item 2) — indices into <see cref="_buffer"/>. Reused, never re-allocated.</summary>
    private readonly HashSet<int> _flyingSeats = new(2);

    // Reordering model/FX delivery may precede the semantic flight. Bound the initial hold by
    // that flight's own duration, independent of how frequently board content is refreshed.
    private readonly Dictionary<int, float> _arrivalDeadlines = new(InitialSlots);

    /// <summary>The cell-local position of every cell this pass actually SEATED a card in, and the
    /// card that went into it — parallel lists, handed to <see cref="RemoteActiveCardPulse"/> so a
    /// hosted clone can be traced back to its card. A blanked cell is deliberately absent: its
    /// object is switched off and hosts nothing. See the pulse class for why the clone's own
    /// <c>AbilityCard</c> can never answer this.</summary>
    private readonly List<Vector3> _cellPos = new(InitialSlots);
    private readonly List<CAbilityCard> _cellCard = new(InitialSlots);

    public RemoteActiveCards(RemoteAvatar owner, int playerId, Transform boardRoot,
                             in RemoteBoardLayout layout)
    {
        _owner = owner;
        _playerId = playerId;
        _root = new GameObject("ActiveCards").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        // The OWNER's own seat: PlayTray.ActiveMountBase plus the AUTHORED per-board ActiveOffset
        // and card scale, keyed by the peer's synced style (RemoteBoardLayout). The old hardcoded
        // base dropped both, so on Steel/Bronze this column sat 20–40 mm behind the owner's and at
        // the wrong card size — part of defect (c) of the 1:1-parity round.
        _pulse = new RemoteActiveCardPulse(playerId);
        _root.localPosition = layout.ActiveMount;
        _root.localScale = Vector3.one * layout.ActiveCardScale;

        // THE OWNER'S OWN METRIC AND GRID, not this renderer's constants — see LegacyCardW for the
        // 18 % the constants were off by. The card SCALE is already carried by the root above, so
        // these are the unscaled numbers, which is exactly how the owner's ActivePileViewer holds
        // them (it multiplies width x cardScale x grid at the same point).
        _cardW = layout.ActiveCardWidth > 0f ? layout.ActiveCardWidth : LegacyCardW;
        _cardH = _cardW * (88f / 63.5f);
        _grid = layout.ActiveGridSpacing;

        // Use the owner's caption construction: its 0.22 font ceiling, normal weight, native
        // font, -2.5 mm depth and free-label rank. The old replica used 0.045 and bold at z=0.
        _title = ActivePileViewer.CreateTitle(_root);

        for (int i = 0; i < InitialSlots; i++)
            _cards.Add(new RemoteBoardCard(_root, Vector3.zero, _cardW, _cardH));

        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// ActivePileViewer.Layout, term for term — the ROOT-LOCAL seat of cell <paramref name="seat"/>
    /// of <paramref name="count"/>. Copying the CONSTRUCTION rather than the numbers is what makes
    /// this column and the owner's identical by being the same expression instead of two formulas
    /// that have to agree; the column COUNT and the row Z-stagger are read off ActivePileViewer for
    /// the same reason.
    ///
    /// <para>IT IS A METHOD RATHER THAN AN INLINE BLOCK BECAUSE A SECOND SURFACE NEEDS IT.
    /// <see cref="Refresh"/> moves its cells with this and <c>RemoteCardFx</c> lands its
    /// '-&gt; Active' arcs on it, so a flight cannot arrive somewhere the cell is not — by
    /// construction, not by two formulas agreeing. Root-local: the caller adds the root's own
    /// <c>ActiveMount</c> and <c>ActiveCardScale</c>, which is what
    /// <see cref="TryCellBoardLocal"/> does.</para>
    /// </summary>
    internal static Vector3 CellLocal(int seat, int count, float cardW, in Vector2 grid)
    {
        int cols = Columns;
        int rows = (count + cols - 1) / cols;
        float rowStep = cardW * (88f / 63.5f) * grid.y;
        float colStep = cardW * grid.x;
        float yTop = rowStep * (rows - 1) * 0.5f;
        int row = seat / cols;
        int col = seat % cols;
        int colsInRow = Mathf.Min(cols, count - row * cols);
        return new Vector3((col - (colsInRow - 1) * 0.5f) * colStep,
                           yTop - row * rowStep,
                           -ActivePileViewer.ZStagger * row);
    }

    /// <summary>
    /// BOARD-LOCAL seat of the cell that will draw <paramref name="cardInstanceId"/> — for
    /// <c>RemoteCardFx</c>'s '-&gt; Active' arc, which used to end on the block's MIDPOINT.
    ///
    /// <para><c>RemoteControlBoard.AnchorLocal</c> resolves <c>CardFxAnchor.Active</c> to
    /// <c>layout.ActiveMount</c>, and its own comment says the mount "IS the block's midpoint and a
    /// one-card column lands dead on its own cell". Both halves are true and the second one is the
    /// whole defect: this column CENTRES N cells on that midpoint, so with two active cards each
    /// cell sits half a column step off it — 0.0337 m board-local at the shipped CardWidth 0.0635,
    /// ActiveGridSpacing.x 1.06 and ActiveCardScale 1.00 — and every arc landed between them. The
    /// ModBuild 476 peer log has the case in one line: <c>ACTIVE ARRIVAL … (cell 2 of 2)</c>.</para>
    ///
    /// <para>It reads <see cref="_root"/>'s OWN <c>localPosition</c> and <c>localScale</c> rather
    /// than re-deriving the mount and the card scale from the layout: those two are the terms the
    /// root actually applies, so the answer is the seat this column is drawing at and not a second
    /// evaluation that could drift from it. False when this column is not drawing that card — the
    /// flight then keeps the mount, which is the old behaviour and is right for a one-card
    /// column.</para>
    /// </summary>
    internal bool TryCellBoardLocal(int cardInstanceId, out Vector3 boardLocal)
    {
        boardLocal = default;
        if (cardInstanceId == int.MinValue || Count <= 0)
            return false;
        for (int i = 0; i < _buffer.Count && i < Count; i++)
        {
            int id;
            try { id = _buffer[i].CardInstanceID; }
            catch { continue; }
            if (id != cardInstanceId)
                continue;
            boardLocal = _root.localPosition + _root.localScale.x * CellLocal(i, Count, _cardW, _grid);
            return true;
        }
        return false;
    }

    /// <summary>Change key for <see cref="ReportActiveHeldIfChanged"/>.</summary>
    private int _loggedActiveHeld = int.MinValue;

    /// <summary>
    /// HARDWARE EVIDENCE for report item 1 of 2026-09-06, on the surface the user did not happen to
    /// photograph. Grep token: ACTIVE MATRIX HELD SEAT.
    ///
    /// <para>READ IT LIKE THIS. <c>held=H matrix=N recordLen=L suppressed=S</c>:</para>
    /// <list type="bullet">
    ///   <item><c>held=0</c> — this peer is not holding an active card. The round says NOTHING
    ///     about this fix, and the silence of everything else proves nothing.</item>
    ///   <item><c>held&gt;0 and suppressed==held</c> — WORKING. The card is drawn in their fist and
    ///     its matrix cell is blank, which is the picture the owner is looking at.</item>
    ///   <item><c>held&gt;0 and suppressed==0</c> — INERT, and <c>recordLen</c> against
    ///     <c>matrix</c> says why: they must be equal for a positional seat to be a name, and a
    ///     PERSISTENT inequality means the sender's ActivatedCards walk and this one have drifted
    ///     apart, which is a defect in one of the two walks and not in this belt.</item>
    /// </list>
    /// <para>Change-gated on all four numbers, so a settled board costs no lines and a pick-up
    /// costs one.</para>
    /// </summary>
    private void ReportActiveHeldIfChanged(int held, int listLength, int seatA, int seatB)
    {
        int suppressed = (seatA >= 0 ? 1 : 0) + (seatB >= 0 ? 1 : 0);
        int key = ((held * 97 + listLength) * 97 + Count) * 97 + suppressed;
        if (key == _loggedActiveHeld)
            return;
        _loggedActiveHeld = key;
        if (held == 0 && suppressed == 0)
            return;   // the resting state is not a reading worth a line
        // HW-VERIFY: report item 1 (2026-09-06). Grep token: ACTIVE MATRIX HELD SEAT.
        VRLog.Note("Net", $"ACTIVE MATRIX HELD SEAT [player {_playerId}]: held={held} "
            + $"matrix={Count} recordLen={listLength} suppressed={suppressed}. FIFTH COPY OF ONE "
            + "MEMBERSHIP DEFECT, and the one the user could not have photographed: picking an "
            + "active card up removes it from nothing a peer can see (the grab choke point touches "
            + "the hand fan only, ActivePileViewer.Relayout merely declines the held card a grid "
            + "POSE, and this matrix is rebuilt from the replicated ActivatedCards, which still "
            + "holds it), so the same card was drawn in their fist AND in their matrix — in EVERY "
            + "phase, because this population is exempt from the selection-phase gate and has no "
            + "run of identical backs to hide a duplicate behind. held>0 with suppressed=held is "
            + "the WORKING reading. held>0 with suppressed=0 is INERT, and the two lengths beside "
            + "it are the reason: recordLen is the sender's own ActivatedCards walk and matrix is "
            + "this client's, they must be equal for seat k to name cell k, and a positional guess "
            + "across a frame where they differ would blank the WRONG card — worse than the "
            + "duplicate. A PERSISTENT inequality is a drift between the two walks and is the "
            + "finding. NOTHING NEW IS ON THE WIRE: record 36 has named this card in this list "
            + "since HeldFaceListActive shipped, so its own front could be drawn on the fist slab; "
            + "until now RemoteHeldCardFace was its only consumer.");
    }

    /// <summary>Has <c>IsFlyingToActive</c> EVER answered true on this board? The one term that
    /// separates "the hold is working" from "the hold never had a flight to hold for" — see
    /// <see cref="ReportFlightHoldIfChanged"/>.</summary>
    private bool _seenInAir;

    /// <summary>Change key for <see cref="ReportFlightHoldIfChanged"/>.</summary>
    private int _loggedHold = int.MinValue;

    /// <summary>
    /// HARDWARE EVIDENCE for user item 2 (2026-09-07). Grep token: ACTIVE FLIGHT HOLD.
    ///
    /// <para>READ IT LIKE THIS. <c>held=H ofMatrix=N sawFlight=B</c>:</para>
    /// <list type="bullet">
    ///   <item><c>held&gt;0 with sawFlight=True</c> — WORKING. A card is in the air into this
    ///     matrix and its cell is empty for the arc, which is the picture the owner is looking at.
    ///     Its counterpart is <c>[Net] Remote card FX … -&gt; Active</c> in the same interval, and
    ///     the <c>[Net] ACTIVE ARRIVAL</c> line for that card must come AFTER both.</item>
    ///   <item><c>held&gt;0 with sawFlight=False</c> — this is the ONE PASS OF GRACE and nothing
    ///     more. If it is followed by an <c>ACTIVE ARRIVAL</c> and never by a
    ///     <c>Remote card FX … -&gt; Active</c>, that peer's event was LOST (cross-check
    ///     <c>[Net] CARD FX LOST</c>) and the card correctly appeared one tick late without an
    ///     animation.</item>
    ///   <item>this line ABSENT for a session in which a peer activated a card — INERT, and the
    ///     fault is upstream of the hold: either this column never saw the card enter
    ///     <c>ActivatedCards</c> (read <c>[Cards] ACTIVE SET</c>'s MODEL row for that character) or
    ///     <c>Refresh</c> is not running for that board at all.</item>
    /// </list>
    /// </summary>
    private void ReportFlightHoldIfChanged()
    {
        int key = (_flyingSeats.Count * 401 + Count) * 2 + (_seenInAir ? 1 : 0);
        if (key == _loggedHold)
            return;
        _loggedHold = key;
        if (_flyingSeats.Count == 0)
            return;   // a matrix with nothing held back is the resting state, not a reading
        // HW-VERIFY: user item 2 (2026-09-07). Grep token: ACTIVE FLIGHT HOLD.
        VRLog.Note("Net", $"ACTIVE FLIGHT HOLD [player {_playerId}]: held={_flyingSeats.Count} "
            + $"ofMatrix={Count} sawFlight={_seenInAir}. THE CELL IS KEPT EMPTY WHILE THE CARD IS IN "
            + "THE AIR, because the owner's own cell is: ActivePileViewer.Relayout hands the flying "
            + "card its home and VRCard ignores that home while IsFlying, so on their board the card "
            + "APPEARS when the arc lands. This column used to fill the cell the moment the model "
            + "moved the card — up to a refresh BEFORE the arc started — and then play the arc into "
            + "an occupied cell, which is what the user saw: 'ich habe auf dem remote baord direkt "
            + "schon den Flug zur Aktiven karten gesehen … Am Ende des Zuges am remote board gab es "
            + "dann gar kein FLug mehr'. 1:1 covers TIMING. THE SIGNAL IS RemoteCardFx's own "
            + "IsFlyingToActive, which has carried the flown card's CardInstanceID since that half "
            + "shipped and had NO CALLER AT ALL until this build — a producer with no consumer reads "
            + "exactly like a fix that is in place. sawFlight=False on every line of a session in "
            + "which a peer DID activate a card means only the one pass of grace ever fired and the "
            + "wire event never arrived; read '[Net] CARD FX LOST' for that peer before blaming this "
            + "hold. A card this column has already DRAWN is never taken back, whatever the flight "
            + "pool says, because a card popping out of the matrix and back is worse than the "
            + "overlap this replaces.");
    }

    public void Refresh(CPlayerActor actor)
    {
        // THE CARVE-OUT FROM THE CARVE-OUT, asked of the one rule every peer-card surface asks. It
        // still needs a character to resolve against; the ONLY term it drops is the phase.
        //
        // THE CAPABILITY HALF IS THIS BOARD'S, NOT RevealGate.InScenario's (2026-09-07 review, item
        // B3). The two-argument overload folds in RevealGate.InScenario, which is false for a null
        // SaveData.Instance, an unassigned SaveData.Global, or a campaign with no MapState — and
        // this column's neighbour one method away, RemoteControlBoard's round-card recesses, has no
        // such term at all. So in the window where the board is up and CurrentGameState has not said
        // Scenario yet, the recesses drew FRONTS and this matrix drew BACKS: the one population in
        // the mod whose own census string says it "must NEVER read a BACK, in any phase". This class
        // is only ever ticked from RemoteControlBoard.RefreshContent, whose whole lifetime is gated
        // on RemoteBoardScenarioGate, so the capability question is already answered above it and is
        // passed in rather than asked a second way. Nothing else about the call changes, and the
        // rule it names is reported beside the count below.
        bool showFronts = RevealGate.CardFaces(RevealGate.PeerCardPopulation.AlreadyPublic, actor,
                                               int.MinValue, scenarioEstablished: true,
                                               out RevealGate.FaceRule faceRule)
                          != RevealGate.CardFaceSource.None;
        _buffer.Clear();
        try
        {
            // THE RAW FIELD, VIA THE ONE ACCESSOR EVERY SEAT NOW ASKS (ActiveCardSet). It used to be
            // `cc?.ActivatedAbilityCards`, which is correct and allocating: that property is a LINQ
            // projection that materialises a NEW list on every read (CCharacterClass.cs:99) and this
            // is a per-rebuild path. ActiveCardSet.ActivatedCards hands back m_ActivatedCards itself.
            // The behaviour is unchanged — the projection's only work was the CAbilityCard filter,
            // which is the `is CAbilityCard` test below.
            List<CBaseCard>? active = Cards.ActiveCardSet.ActivatedCards(actor);
            if (active != null)
            {
                // UNCAPPED, like the owner's own list: a 7th active card used to be dropped here
                // and drawn there — see InitialSlots.
                for (int i = 0; i < active.Count; i++)
                    if (active[i] is CAbilityCard card)
                        _buffer.Add(card);
            }
        }
        catch { _buffer.Clear(); }

        Count = _buffer.Count;

        // ─── THE CARD IN THE OWNER'S FIST IS NOT DRAWN IN THE MATRIX (report item 1, 2026-09-06) ─
        // "Durchsuche nochmal alles nach solchen ungewollten Kopien in der Hand die Karten kopieren
        // statt sie aus einem Faecher zu nehmen." Fifth copy of one membership defect and the one
        // with the widest blast radius, because this surface is exempt from the selection-phase
        // gate (RevealGate.PeerCardPopulation.AlreadyPublic) — so unlike the hand fan there is not
        // even a run of identical backs to hide the duplicate behind. It was visible in EVERY
        // phase. Picking an active card up removes it from nothing a peer can see: the grab choke
        // point touches _fan only, ActivePileViewer.Relayout merely declines the held card a grid
        // POSE, and this class rebuilds from the replicated ActivatedCards, which still holds it.
        //
        // THE GAP IS KEPT, exactly as it is for the hand, browse and item arcs and for the same
        // reason: the owner's own ActivePileViewer.Layout keeps n = _cards.Count and gives every
        // other card the pose for its own unchanged index, so the owner is looking at a grid with
        // one cell empty. Blanking the cell in place IS that picture; re-flowing the grid would
        // trade one 1:1 breach for a worse one.
        //
        // THE LENGTH BELT IS WHAT MAKES THE INDEX SAFE. Record 36's seat indexes the sender's walk
        // of ActivatedCards; this buffer is the identical walk, so seat k is cell k — but only
        // while both walks are the same length. They can differ for a frame around a card going
        // active, and a positional guess across that frame would blank the WRONG card, which is
        // strictly worse than a duplicate. So a disagreement hides nothing.
        int heldActive = _owner.HeldActiveSeats(out int activeSeatA, out int activeSeatB,
                                                out int activeListLen, out int activePoseA);
        if (heldActive == 2 && activeSeatA == activeSeatB)
        {
            activeSeatB = -1;
            heldActive = 1;
        }
        if (heldActive == 0 || activeListLen != Count)
        {
            activeSeatA = -1;
            activeSeatB = -1;
        }
        ReportActiveHeldIfChanged(heldActive, activeListLen, activeSeatA, activeSeatB);

        // ─── THE CELL STAYS EMPTY WHILE THE CARD IS IN THE AIR (user item 2, 2026-09-07) ─────────
        // "Am Ende des Zuges am remote board gab es dann gar kein FLug mehr wo der Flug hätte sein
        // sollen zu den aktiven Karten (weil es ihn schon bereits davor gab)."
        //
        // The owner's cell is empty for the whole 0.4 s arc — ActivePileViewer.Relayout hands the
        // flying card its home and VRCard ignores that home while IsFlying — so the card APPEARS
        // when the arc lands. This column was filling the cell the moment the model moved the card,
        // which is up to a refresh BEFORE the arc even starts, and then playing an arc into an
        // occupied cell. Same two events, opposite order: that is a TIMING breach of the 1:1 rule,
        // not a missing animation, and it is why the flight looked like it had "already happened".
        //
        // THE SIGNAL IS THE FLIGHT'S OWN and it is exact — RemoteCardFx stamps the flown card's
        // CardInstanceID on the Flight and answers IsFlyingToActive for as long as that arc is
        // running on THIS client's clock. It had no caller until this line; see the class note.
        //
        // Model and cosmetic messages can arrive in either order. A never-seated card waits
        // at most one existing flight lifetime for the event; a known running flight owns its
        // complete lifetime. This used to count refresh calls and silently assumed 250 ms/call.
        // Lost events still settle after a bounded interval, and seated cards never retract.
        _flyingSeats.Clear();
        for (int i = 0; i < _buffer.Count; i++)
        {
            int flying;
            try { flying = _buffer[i].CardInstanceID; }
            catch { continue; }
            if (_seatedIds.Contains(flying))
                continue;                       // already drawn here: never take it back
            if (!_arrivalDeadlines.TryGetValue(flying, out float deadline))
            {
                deadline = Time.unscaledTime + NetProtocol.CardFxSeconds;
                _arrivalDeadlines.Add(flying, deadline);
            }
            bool inTheAir = _owner.IsCardFlyingToActive(flying);
            if (Time.unscaledTime < deadline || inTheAir)
                _flyingSeats.Add(i);
            if (inTheAir)
                _seenInAir = true;
        }
        ReportFlightHoldIfChanged();
        // The comparable half of item 8b's answer: what THIS client believes is active for THAT
        // player, in the same format and the same sort order the owner's own board reports, so two
        // logs' lines for one character diff literally. Reported BEFORE the empty early-out below,
        // because an empty active pile is a reading and not an absence.
        Cards.ActiveCardSet.Report(actor, Cards.ActiveCardSet.Belief.RemoteBoard, _buffer);
        // The observer half of user item 7's animation pair. Reported here for the same reason the
        // census above is: an EMPTY active pile has to prune the arrival marks, so a card that went
        // active, expired and went active again flies and reports twice rather than once.
        ReportArrivals();
        bool any = Count > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);
        if (!any)
        {
            _panelsByCard.Clear();
            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Set(null, showFronts, actor);
                // ITEM 4 (2026-09-07): release the permanent look with the cell. Set(null)
                // already drops it through ClearFace; this makes the release EXPLICIT at every
                // blanking site so a future path that blanks some other way cannot leave a look
                // standing on a face nobody is painting.
                _cards[i].SetActiveCardLook(_playerId, i, null, RemoteCardArt.CardFxLook.None);
            }
            // ZERO IS A READING (see PeerCardFaceCensus): an empty active pile must overwrite this
            // population's census row rather than leave the last non-empty one standing.
            PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.ActiveMatrix, _playerId, 0, 0,
                "this character has no active cards");
            // AN EMPTY PILE STILL HAS TO RUN THE PULSE PASS. It is what takes the highlight back
            // OFF a face this driver was lighting a moment ago — a card whose bonus just expired,
            // or one still hosted on that peer's held slab — and what prunes the isolated
            // materials. A driver that only runs while there is something to light cannot turn
            // anything off.
            _cellPos.Clear();
            _cellCard.Clear();
            DrivePulse(actor);
            return;
        }

        // Follow the local title's language.
        RemoteBoardContent.SetText(_title, ActivePileViewer.Caption().ToUpperInvariant());

        // Grow the slot pool to whatever the owner is actually showing (see InitialSlots). Slots
        // are never returned: an active pile that reached N once will reach it again, and a
        // RemoteBoardCard owns hosted card faces whose teardown is Destroy()'s job.
        while (_cards.Count < Count)
            _cards.Add(new RemoteBoardCard(_root, Vector3.zero, _cardW, _cardH));

        // Reserve every resident panel before assigning any newcomer: otherwise inserting a
        // card at index zero can overwrite the panel needed by a later resident. Held cells keep
        // their identity even while their face is blank, just like the owner's VRCard object.
        _orderedPanels.Clear();
        _assignedPanels.Clear();
        for (int i = 0; i < Count; i++)
        {
            RemoteBoardCard? panel = _panelsByCard.TryGetValue(_buffer[i].CardInstanceID,
                out RemoteBoardCard existing) ? existing : null;
            _orderedPanels.Add(panel!);
            if (panel != null) _assignedPanels.Add(panel);
        }
        for (int i = 0; i < Count; i++)
        {
            if (_orderedPanels[i] != null) continue;
            for (int j = 0; j < _cards.Count; j++)
            {
                if (!_assignedPanels.Add(_cards[j])) continue;
                _orderedPanels[i] = _cards[j];
                break;
            }
        }
        for (int i = 0; i < _cards.Count; i++)
            if (!_assignedPanels.Contains(_cards[i])) _orderedPanels.Add(_cards[i]);
        _cards.Clear();
        _cards.AddRange(_orderedPanels);

        // ActivePileViewer.Layout, term for term — now in ONE place, <see cref="CellLocal"/>, which
        // this loop moves its cells with AND RemoteCardFx lands its '-> Active' arcs on. It was
        // written out here inline, and the flight had no way to reach it: RemoteControlBoard
        // resolves CardFxAnchor.Active to layout.ActiveMount, i.e. the block's MIDPOINT, so every
        // arc into a two-card column landed exactly between the two cells. See CellLocal.
        _cellPos.Clear();
        _cellCard.Clear();
        for (int i = 0; i < _cards.Count; i++)
        {
            if (i >= Count)
            {
                _cards[i].Set(null, showFronts, actor);
                _cards[i].SetActiveCardLook(_playerId, i, null, RemoteCardArt.CardFxLook.None);
                continue;
            }
            Vector3 cellAt = CellLocal(i, Count, _cardW, _grid);
            int cardId = _buffer[i].CardInstanceID;
            bool residentKeepsItsCard = _panelsByCard.ContainsKey(cardId);
            bool held = i == activeSeatA || i == activeSeatB;
            if (!held && _heldPoseSlots.TryGetValue(cardId, out int oldPoseSlot))
            {
                Transform? releasedSlab = _owner.HeldSlab(oldPoseSlot);
                if (releasedSlab != null)
                    _cards[i].SeedReturn(releasedSlab.position, releasedSlab.rotation,
                        Mathf.Abs(releasedSlab.lossyScale.x) * RemoteHandFan.DefaultCardWidth);
                _heldPoseSlots.Remove(cardId);
            }
            if (held)
                _heldPoseSlots[cardId] = i == activeSeatA ? activePoseA : 3 - activePoseA;
            // THE OWNER'S OWN RATE, off his synced BoardTuning ([Cards] CardLerpSpeed, wire id
            // NetProtocol.TuneCardLerpSpeed) — never this client's config. A mirror may not read the
            // viewer's dial, and this is the very number VRCard.Update runs his glide at.
            _cards[i].Move(cellAt, instant: !residentKeepsItsCard, _owner.BoardTuning.CardLerpSpeed);
            if (held || _flyingSeats.Contains(i))
            {
                // Either in their fist and not in their matrix, or still in the AIR on its way
                // here (user item 2). Moved to its own cell first so the gap sits where the owner's
                // does, then blanked through the same Set(null) every unused cell takes — one
                // hiding mechanism with three callers, no second one to keep in step.
                _cards[i].Set(null, showFronts, actor);
                // ITEM 4: a seat blanked because the card is in their FIST or still in the AIR has
                // no face to wash, and the hold must come off with it. Nothing is lost across the
                // blank: this method is re-asserted on every refresh, so the wash goes back on in
                // the same pass that re-seats the face when the card lands — before its first drawn
                // frame, because Set() and this call are one pass. The wash on the FLYING slab
                // itself belongs to RemoteCardFx's arc and is NOT claimed here.
                _cards[i].SetActiveCardLook(_playerId, i, _buffer[i], RemoteCardArt.CardFxLook.None);
                continue;
            }
            // The ACTIVE column gets the same real-card treatment as the round slots: the actor is
            // handed through so its cards can be resolved to that player's own widgets. An active
            // card is public by definition (it was played face-up in front of everybody), and it is
            // still gated by the very same showFronts answer — the gate can only ever be stricter
            // than vanilla here, never looser.
            _cards[i].Set(_buffer[i], showFronts, actor);
            // Set() is change-gated, so the cadenced mip-bake rescan for a hosted face's async
            // header art rides this refresh instead (see RemoteBoardCard.MaintainMips).
            _cards[i].MaintainMips();
            // ── RULE 1, THE MIRROR HALF (user item 4, 2026-09-07 afternoon AND evening) ─────────
            // "Ich meine nicht die Animation von 2 Sekunden, sondern den dauerhaften effekt der
            // über eine verbrannte Karte liegt. Und dieser Effekt war bei manchen Aktiven Karten
            // vorhanden und wurde dort auch angezeigt - aber nur eine Runde - die runde darauf war
            // die Karte wieder blau" — and, from the same report, "lokal und remote"; then, that
            // evening: "Einmal grau bleibt die Karte (remote UND lokal) grau solange sie im aktiven
            // Stapel liegt. Gleiches gilt für eine verbrannte Karte dort."
            //
            // BOTH HALVES OF THE RULE, NOT JUST THE BURN. This call used to pass a BOOLEAN — wears
            // the burnt wash, or nothing — so a Discard-bound activated card was mirrored CLEAN and
            // went blue at the same round boundary the owner's own did. Cards.BurnLookPolicy.
            // ForActivatedCard answers the whole rule in one term: Burn for Lost-bound, Ghost for
            // Discard-bound, and there is no third answer because a card is only in this pile
            // because its action was played.
            //
            // ONE EXPRESSION, BOTH BOARDS. That method is the game's own CCharacterClass.cs:479 test
            // and it is the SAME one the owner's own board asks through BurnLookPolicy.Enforce —
            // not an equivalent-looking copy. THE MODEL IS LOCAL: _buffer is this client's own walk
            // of that peer's ActivatedCards and the SelectedAction it reads is on every client, so
            // no wire field is owed and none is added. RIGHT AFTER Set(), which is where the face
            // exists; the one-writer hold is taken inside RemoteCardArt, next to the art it
            // protects.
            _cards[i].SetActiveCardLook(_playerId, i, _buffer[i],
                UsedCardLook.FromPolicy(Cards.BurnLookPolicy.ForActivatedCard(_buffer[i])));
            // …and the pulse pass's only way back from a hosted clone to the card it is showing.
            // Recorded HERE rather than derived again later, so the position the driver matches on
            // is the very Vector3 the cell was moved to and not a second evaluation of the same
            // formula that could drift from it.
            _cellPos.Add(cellAt);
            _cellCard.Add(_buffer[i]);
        }
        _panelsByCard.Clear();
        for (int i = 0; i < Count; i++)
            _panelsByCard[_buffer[i].CardInstanceID] = _cards[i];

        // The standing picture for the census — this population must NEVER read a BACK, in any
        // phase, and the line PeerCardFaceCensus prints says so in as many words. RealFaceCount is
        // "how many of the drawn cells carry a real game-card face"; the rest are the mod-drawn
        // name+initiative fallback, which is a front too (it is not a card back), so the BACK count
        // is the cells the gate refused outright and nothing else.
        int fronts = showFronts ? Count : 0;
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.ActiveMatrix, _playerId,
            fronts, Count - fronts,
            showFronts
                ? $"RevealGate.CardFaces(AlreadyPublic) — {RealFaceCount} real game-card face(s), "
                  + "the rest the mod-drawn fallback panel; rule: " + RevealGate.RuleText(faceRule)
                : "RevealGate.CardFaces(AlreadyPublic) named NO source — selection is covered "
                  + "or the actor/scenario source is unavailable");

        // Change-gated on the shape itself, so the line below fires on a human-paced event (a card
        // going active) and never per refresh. The row count is re-derived here rather than carried
        // down from the layout block: CellLocal owns that arithmetic now, and a diagnostic must not
        // be the reason a layout local outlives the loop that needs it.
        int rows = (Count + Columns - 1) / Columns;
        int shape = Count * 100 + rows;
        if (_loggedShape != shape)
        {
            _loggedShape = shape;
            // HW-VERIFY: this line decides R3 — that a peer's active block is the OWNER's grid, not
            // a narrower one, and that no active card is silently dropped.
            VRLog.Note("Net", $"Peer [{_playerId}] active grid: {Count} card(s) in {rows} row(s) x up to "
                            + $"{Columns} col(s) — the owner's own ActivePileViewer.Columns, and the "
                            + "list is uncapped, so a 7th active card is drawn here too.");
        }

        // LAST, because it asserts onto the faces the loop above has just seated: a pulse pushed at
        // a cell whose Set() had not run yet would be writing to the previous card's clone.
        DrivePulse(actor);
    }

    /// <summary>
    /// The pulsing active REGION, on this column's cells AND on that peer's fist — user item 3
    /// (2026-09-07). Split out so the empty-pile early-out above can run it too; see
    /// <see cref="RemoteActiveCardPulse"/> for why the held slabs are a second root and why no wire
    /// field is involved.
    /// </summary>
    private void DrivePulse(CPlayerActor actor)
    {
        _pulse.Begin();
        _pulse.DriveMatrix(_root, actor, _cellPos, _cellCard);

        // AND THE FIST. The seats come from the SAME record-36 read the blanking above uses, under
        // the SAME length belt: a seat only names a card while the sender's ActivatedCards walk and
        // this client's are the same length, because a positional guess across a frame where they
        // differ would pulse the wrong card's half. A slab whose seat resolves to nothing is driven
        // with a null card, which takes any highlight this driver left on it back OFF rather than
        // stranding one.
        int held = _owner.HeldActiveSeats(out int seatA, out int seatB, out int listLen,
                                          out int poseSlotA);
        CAbilityCard? slab1 = null;
        CAbilityCard? slab2 = null;
        if (held > 0 && listLen == Count)
        {
            if (poseSlotA == 1)
            {
                slab1 = CardAtSeat(seatA);
                slab2 = CardAtSeat(seatB);
            }
            else if (poseSlotA == 2)
            {
                slab2 = CardAtSeat(seatA);   // seatB cannot exist on this branch, by construction
            }
        }
        // BOTH SLABS, ALWAYS, even when neither names an active card: a null card is what takes a
        // highlight this driver left on a slab back OFF. Driving only the resolved one would strand
        // a pulse on a card that was active, was picked up, and then stopped being active while it
        // was still in the fist.
        _pulse.DriveSingle(_owner.HeldSlab(1), actor, slab1);
        _pulse.DriveSingle(_owner.HeldSlab(2), actor, slab2);
        _pulse.End();
    }

    /// <summary>The card at one record-36 seat of this client's own <c>ActivatedCards</c> walk, or
    /// null when the seat names nothing. Bounds-checked here rather than at the two call sites: an
    /// out-of-range seat is a wire reading, not a programming error.</summary>
    private CAbilityCard? CardAtSeat(int seat) =>
        seat >= 0 && seat < _buffer.Count ? _buffer[seat] : null;

    /// <summary>Last logged (count, rows) shape (−1 = never), so the grid diagnostic fires on a real
    /// change and never per refresh.</summary>
    private int _loggedShape = -1;

    // ─── THE OBSERVER'S HALF OF THE END-OF-TURN ACTIVE FLIGHT (user item 7, 2026-09-06 late) ─────

    /// <summary>The cards this column is already drawing, keyed on <c>CardInstanceID</c> — the
    /// game's own per-instance identity, the one <c>RevealGate</c> and the departed-face memory use.
    /// (The card DATA id beside it is what gets LOGGED, because that is the field <c>ACTIVE SET</c>
    /// prints and therefore the one two clients' lines diff on.) An instance not in here and in the
    /// actor's <c>ActivatedCards</c> arrived since the last pass — the observer's view of the very
    /// end-of-turn transition the owner flies on.</summary>
    private readonly HashSet<int> _seatedIds = new(InitialSlots);

    /// <summary>Scratch for this pass's ids, so the prune allocates nothing.</summary>
    private readonly HashSet<int> _passIds = new(InitialSlots);

    /// <summary>
    /// THE OBSERVER'S LINE, WRITTEN TO DIFF AGAINST THE OWNER'S. Grep token: ACTIVE ARRIVAL.
    ///
    /// <para>User item 7 is a 1:1 claim about an ANIMATION — "remote und lokal" — and an animation
    /// is the one thing a state census cannot photograph. So the pair is: the owner's
    /// <c>[Cards] ACTIVE FLIGHT</c> line (which names the card, the end-of-turn transition that
    /// triggered it, the recess it left, the Active destination and the frame it launched) and this
    /// one, on every observing machine, naming the SAME card in the SAME <c>id:name</c> form
    /// <c>ACTIVE SET</c> uses, the frame it seated here, and the cell it took. Two logs, one card
    /// id, one subtraction — which is what a video would otherwise be needed for.</para>
    ///
    /// <para>READ IT WITH the <c>[Net] FLIGHT FACE ... -&gt; Active</c> line this client prints for
    /// the same peer in the same interval: that one names the ANCHORS this mirror actually flew and
    /// the face it drew, and it is the line that says whether the flight arrived at all.</para>
    ///
    /// <list type="bullet">
    ///   <item>WORKING = one line here per activation, with a <c>FLIGHT FACE ... their Slot0 -&gt;
    ///     Active</c> or <c>Slot1 -&gt; Active</c> beside it reading FRONT, and the owner's
    ///     <c>ACTIVE FLIGHT</c> naming the same card id. ModBuild 462 read 3 flights for 1
    ///     activation, all <c>Board -&gt; Active</c>, 2 of 3 drawn as a BACK.</item>
    ///   <item>INERT = this line present with NO <c>FLIGHT FACE ... -&gt; Active</c> in the same
    ///     interval: the card reached this matrix through the host-replicated model and the wire
    ///     event never arrived. That is a MISSING animation and nothing more — the card is seated
    ///     correctly in the cell below, never stranded mid-flight. Read <c>[Net] CARD FX LOST</c>
    ///     for this peer against their <c>[Net] CARD FX OUTBOX</c>; it stood at 1 of 7 this
    ///     session.</item>
    ///   <item>NO LONGER BEYOND THE INSTRUMENT — THE OVERLAP. This line used to say "the cell is
    ///     drawn" without being able to say whether a slab was in the air at the same instant. It
    ///     now cannot fire while one is: the cell is held empty for the length of the arc
    ///     (<c>RemoteAvatar.IsCardFlyingToActive</c>), so this line marks the moment the card
    ///     becomes VISIBLE here, which is the moment the owner's own arc lands. Its FRAME minus the
    ///     owner's <c>ACTIVE FLIGHT</c> frame is therefore the end-to-end delay PLUS one arc, not
    ///     the delay alone.</item>
    /// </list>
    /// </summary>
    private void ReportArrivals()
    {
        _passIds.Clear();
        for (int i = 0; i < _buffer.Count; i++)
        {
            int id;
            int instanceId;
            string name;
            try
            {
                id = _buffer[i].ID;
                instanceId = _buffer[i].CardInstanceID;
                name = _buffer[i].Name ?? "?";
            }
            catch { continue; }
            _passIds.Add(instanceId);
            // IN THE AIR IS NOT ARRIVED (user item 2). The card is in the model but this column is
            // deliberately holding its cell empty until the arc lands, exactly as the owner's does,
            // so calling it "now drawn" here would be this line asserting a picture that is not on
            // the screen. It stays in _passIds so the prune below does not forget it and make the
            // landing read as a second arrival.
            if (_flyingSeats.Contains(i))
                continue;
            if (!_seatedIds.Add(instanceId))
                continue;   // already drawn on an earlier pass — not an arrival
            // HW-VERIFY: user item 7 (2026-09-06 late), the OBSERVER's half. Grep token:
            // ACTIVE ARRIVAL. One line per card per activation on a 4 Hz surface, so a settled
            // board costs nothing. Its counterpart is '[Cards] ACTIVE FLIGHT' on the owner's
            // machine; the two name the same card id and the same transition.
            VRLog.Note("Net", $"ACTIVE ARRIVAL [player {_playerId}]: card {id}:{name} is now drawn "
                + $"in this peer's ACTIVE matrix (cell {i + 1} of {_buffer.Count}) at frame "
                + $"{Time.frameCount}. THE EVENT is the same one the owner flies on — the game's own "
                + "CCharacterClass.DiscardRoundAbilityCards end-of-turn drain moved this card out of "
                + "RoundAbilityCards into ActivatedCards — seen here through the host-replicated "
                + "model rather than off the wire, which is why this line fires even when the card-FX "
                + "event is lost. ORIGIN ANCHOR expected on the wire: that peer's Slot0/Slot1 (their "
                + "round recess); DESTINATION ANCHOR: CardFxAnchor.Active, which this client resolves "
                + "to RemoteControlBoard.AnchorLocal => layout.ActiveMount — this very column. "
                + "COMPARE the owner's '[Cards] ACTIVE FLIGHT' line for card id " + id + ": same card, "
                + "same transition, and its FRAME plus this one's is the end-to-end delay. A "
                + "'[Net] FLIGHT FACE ... -> Active' line for this peer in the same interval says "
                + "which anchors the mirror actually flew and whether it drew the FRONT; its ABSENCE "
                + "means the event was lost and the card simply appeared here, which is a missing "
                + "animation and never a wrong picture. SINCE USER ITEM 2 (2026-09-07) THIS LINE IS "
                + "GATED ON THE CELL BEING DRAWN, NOT ON THE MODEL: while that card is in the air "
                + "into this matrix the cell is held EMPTY, exactly as the owner's is, so this frame "
                + "is the frame the card became VISIBLE here and the arc that preceded it delivered "
                + "something. Before that gate the cell filled first and the arc landed on an "
                + "occupied cell, which the user read as the flight having already happened.");
        }
        _pruneIds.Clear();
        foreach (int id in _arrivalDeadlines.Keys)
            if (!_passIds.Contains(id)) _pruneIds.Add(id);
        for (int i = 0; i < _pruneIds.Count; i++) _arrivalDeadlines.Remove(_pruneIds[i]);
        _pruneIds.Clear();
        foreach (int id in _heldPoseSlots.Keys)
            if (!_passIds.Contains(id)) _pruneIds.Add(id);
        for (int i = 0; i < _pruneIds.Count; i++) _heldPoseSlots.Remove(_pruneIds[i]);
        _seatedIds.IntersectWith(_passIds);

    }

    public void SetActive(bool active)
    {
        if (_root != null && _root.gameObject.activeSelf != active && (Count > 0 || !active))
            _root.gameObject.SetActive(active);
    }

    /// <summary>Blank every slot (see <see cref="RemoteBoardCard.Blank"/>) — called while the board
    /// is not being drawn, so no hosted face survives a hide/show cycle.</summary>
    public void Blank()
    {
        for (int i = 0; i < _cards.Count; i++)
            _cards[i]?.Blank();
        Count = 0;
    }

    /// <summary>Drop every slot's hosted card face (board teardown). The panels themselves die with
    /// the board root; this makes the clone ownership explicit — see <see cref="RemoteBoardCard.Destroy"/>.</summary>
    public void Destroy()
    {
        for (int i = 0; i < _cards.Count; i++)
            _cards[i]?.Destroy();
        // The pulse driver mints one Material per mirrored face it lights (see its class note on
        // why the shared one may never be written); they are ours and they die here.
        _pulse.Destroy();
    }

    /// <summary>How many of the drawn active cards currently show a REAL game card face (as opposed
    /// to the mod-drawn fallback panel) — diagnostics only.</summary>
    public int RealFaceCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < _cards.Count && i < Count; i++)
                if (_cards[i] != null && _cards[i].Path != RemoteAbilityCardSource.FacePath.None)
                    n++;
            return n;
        }
    }
}
