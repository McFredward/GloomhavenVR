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
/// the local <c>ActivePileViewer</c> is fed from, read off the host-replicated actor. Card FRONTS
/// (name) are shown only through <see cref="RevealGate.ShowRoundCardFronts"/>, so during the secret
/// selection phase a peer's active column shows BACKS — the same stance the round-card slots take.
/// In practice an active card is public by definition (it was played face-up in front of everybody),
/// so the gate can only ever be stricter than vanilla, never looser.
///
/// <para>OPEN, MEASURED, AND NOT FIXED HERE — THE 0.4 s OVERLAP. Since ModBuild 462 a card going
/// active also flies a mirrored slab into this column (<c>CardFxAnchor.Active</c>), and this column
/// is rebuilt from the host-replicated <c>ActivatedCards</c>, which holds the card from the moment
/// the model moved it. So for the flight's 0.4 s an observer sees the card in its CELL and a second
/// copy of it in the AIR, while the owner sees exactly one — their flying card IS the cell's card
/// (<c>ActivePileViewer.Relayout</c> hands it the cell and <c>VRCard</c> ignores its home while
/// <c>IsFlying</c>), so the owner's cell is empty for the whole arc. Two copies of one card is the
/// failure this project ranks above a missing animation, and ModBuild 461 hid the flight slab for
/// the round recesses for exactly this reason.</para>
///
/// <para>IT IS NOT FIXED IN THIS FILE BECAUSE THE ONLY EXACT SIGNAL IS IN ANOTHER ONE. Suppressing
/// the cell needs the IDENTITY of the card currently in the air, and that is resolved inside
/// <c>RemoteCardFx.ResolveFace</c> (via <c>RemoteAvatar.TryTakeDepartedRecessFace</c>) and kept in
/// its private <c>Flight</c> pool; nothing on <see cref="RemoteAvatar"/> exposes it. THE EXACT
/// CHANGE OWED: <c>RemoteCardFx</c> records the claimed <c>CAbilityCard</c>'s
/// <c>CardInstanceID</c> on the <c>Flight</c> it starts for a <c>-&gt; Active</c> destination and
/// exposes a <c>bool IsFlyingToActive(int cardInstanceId)</c> that scans its live flights; this
/// class then blanks that card's cell through the SAME <c>Set(null, showFronts, actor)</c> the
/// held-seat suppression below already uses — one hiding mechanism, no second one to keep in step.
/// A DERIVED CLOCK WAS DELIBERATELY REJECTED: this surface refreshes at 4 Hz
/// (<c>RemoteBoardContent.RefreshSeconds</c>) and the arc lasts 0.4 s, so a timer started from the
/// arrival this class can see would blank the cell for up to a full arc AFTER the slab had already
/// landed — a card popping out of the matrix and back, which is worse than the overlap and would be
/// a second, drifting copy of a fact the flight already knows.</para>
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

    /// <summary>How many active cards the column currently draws (diagnostics).</summary>
    public int Count { get; private set; }

    /// <summary>The peer whose matrix this is — read for record 36's held seats (see
    /// <see cref="RemoteAvatar.HeldActiveSeats"/>), which is the only reason this class needs more
    /// than a player id.</summary>
    private readonly RemoteAvatar _owner;

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
        _root.localPosition = layout.ActiveMount;
        _root.localScale = Vector3.one * layout.ActiveCardScale;

        // THE OWNER'S OWN METRIC AND GRID, not this renderer's constants — see LegacyCardW for the
        // 18 % the constants were off by. The card SCALE is already carried by the root above, so
        // these are the unscaled numbers, which is exactly how the owner's ActivePileViewer holds
        // them (it multiplies width x cardScale x grid at the same point).
        _cardW = layout.ActiveCardWidth > 0f ? layout.ActiveCardWidth : LegacyCardW;
        _cardH = _cardW * (88f / 63.5f);
        _grid = layout.ActiveGridSpacing;

        _title = RemoteBoardContent.Label(_root, "Title", new Vector3(0f, 0.075f, 0f),
            new Vector2(0.09f, 0.024f), 0.045f,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Center, FontStyles.Bold);
        RemoteBoardContent.SetText(_title, ActivePileViewer.Caption().ToUpperInvariant());
        WorldUI.MrBacking.Label(_title); // off-board title → sky/room behind it in MR

        for (int i = 0; i < InitialSlots; i++)
            _cards.Add(new RemoteBoardCard(_root, Vector3.zero, _cardW, _cardH));

        _root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Re-read the actor's active pile and repaint.
    ///
    /// <para>IT ASKS THE FACE RULE ITSELF, and that is the fix for 2026-09-05 report item 2b: "Die
    /// Vorderseite der aktiven Karten in der kleinen Matrix neben dem Controllboard soll IMMER
    /// angezeigt werden - das ist kein Geheimnis. Auch in der Auswahlphase." This method used to
    /// TAKE a <c>showFronts</c> from its caller, and the caller had exactly one — the board's own
    /// <c>RevealGate.ShowRoundCardFronts(actor)</c>, computed for the round-card recesses and
    /// handed to every per-actor surface below it. So the selection phase, which is the right
    /// answer for a card being CHOSEN, was also being applied to a card that was PLAYED FACE-UP in
    /// front of everybody two rounds ago. A parameter is a carve-out the caller can forget; the
    /// call below is one this surface declares about itself and no caller can take away —
    /// <c>RevealGate.PeerCardPopulation.AlreadyPublic</c> carries the whole argument.</para>
    /// </summary>
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

    public void Refresh(CPlayerActor actor)
    {
        // THE CARVE-OUT FROM THE CARVE-OUT, asked of the one rule every peer-card surface asks. It
        // still needs a running scenario and a character to resolve against (both are inside the
        // call); the ONLY term it drops is the phase.
        bool showFronts = RevealGate.CardFaces(RevealGate.PeerCardPopulation.AlreadyPublic, actor)
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
                                                out int activeListLen);
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
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].Set(null, showFronts, actor);
            // ZERO IS A READING (see PeerCardFaceCensus): an empty active pile must overwrite this
            // population's census row rather than leave the last non-empty one standing.
            PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.ActiveMatrix, _playerId, 0, 0,
                "this character has no active cards");
            return;
        }

        // Follow the local title's language.
        RemoteBoardContent.SetText(_title, ActivePileViewer.Caption().ToUpperInvariant());

        // Grow the slot pool to whatever the owner is actually showing (see InitialSlots). Slots
        // are never returned: an active pile that reached N once will reach it again, and a
        // RemoteBoardCard owns hosted card faces whose teardown is Destroy()'s job.
        while (_cards.Count < Count)
            _cards.Add(new RemoteBoardCard(_root, Vector3.zero, _cardW, _cardH));

        int rows = (Count + Columns - 1) / Columns;
        // ActivePileViewer.Layout, term for term: card metric x the owner's grid factor. Copying
        // the CONSTRUCTION rather than the numbers is what makes the two columns identical by being
        // the same expression instead of two formulas that have to agree. The COLUMN COUNT and the
        // row Z-stagger are now read off ActivePileViewer too — they were the two terms this note
        // claimed and did not have.
        float rowStep = _cardH * _grid.y;
        float colStep = _cardW * _grid.x;
        float yTop = rowStep * (rows - 1) * 0.5f;
        for (int i = 0; i < _cards.Count; i++)
        {
            if (i >= Count)
            {
                _cards[i].Set(null, showFronts, actor);
                continue;
            }
            int row = i / Columns;
            int col = i % Columns;
            int colsInRow = Mathf.Min(Columns, Count - row * Columns);
            float x = (col - (colsInRow - 1) * 0.5f) * colStep;
            _cards[i].Move(new Vector3(x, yTop - row * rowStep, -ActivePileViewer.ZStagger * row));
            if (i == activeSeatA || i == activeSeatB)
            {
                // In their fist, not in their matrix. Moved to its own cell first so the gap sits
                // where the owner's does, then blanked through the same Set(null) every unused cell
                // takes — no second hiding mechanism to keep in step with this one.
                _cards[i].Set(null, showFronts, actor);
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
        }

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
                  + "the rest the mod-drawn fallback panel"
                : "RevealGate.CardFaces(AlreadyPublic) named NO source — off-scenario, or no "
                  + "character resolved. NOT the selection phase: this population is exempt from it");

        // Change-gated on the shape itself, so the line below fires on a human-paced event (a card
        // going active) and never per refresh.
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
    }

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
    ///   <item>BEYOND THE INSTRUMENT = the OVERLAP. This line says the cell is drawn; it cannot say
    ///     whether a flight slab was in the air at the same instant, because the in-flight card's
    ///     identity lives in <c>RemoteCardFx</c> and this surface has no read of it. See the class
    ///     note above <see cref="Refresh"/> for the exact two-line change that would close it and
    ///     why it was not made here.</item>
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
                + "animation and never a wrong picture.");
        }
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
