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

    public RemoteActiveCards(int playerId, Transform boardRoot, in RemoteBoardLayout layout)
    {
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
        // The comparable half of item 8b's answer: what THIS client believes is active for THAT
        // player, in the same format and the same sort order the owner's own board reports, so two
        // logs' lines for one character diff literally. Reported BEFORE the empty early-out below,
        // because an empty active pile is a reading and not an absence.
        Cards.ActiveCardSet.Report(actor, Cards.ActiveCardSet.Belief.RemoteBoard, _buffer);
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
