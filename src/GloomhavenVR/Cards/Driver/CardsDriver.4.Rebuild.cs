using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

// CardsDriver part 4 of 6 (see CardsDriver.1.Core.cs for the split map and its rules).
// Regions: hand fan reorder, rebuild, fly-to-pile (issue 5), MP card-FX anchors, the nested
// BurnSlab, HookCard.
//
// ORDER THAT STAYS IN THIS FILE, deliberately: Rebuild runs before TickBurnToPile. Both are
// here so the pair cannot drift across a file boundary.

internal sealed partial class CardsDriver
{
    // ------------------------------------------------------------- hand fan reorder --

    // Persisted VR fan order (session-only), keyed by AbilityCardUI.CardInstanceID. Applied to
    // _fanBuffer every Rebuild (before _fan.SetCards) so it OVERRIDES the game's own SortCards
    // re-sort — a pure VR-presentation reorder with zero gameplay effect. Pruned to the present
    // hand each Rebuild so stale ids (id reuse across scenarios) never accumulate.
    private readonly List<int> _fanOrder = new(24);
    private readonly List<VRCard> _fanReorderScratch = new(24);

    // Cards plucked OUT of the fan and still held — eligible for a reorder commit on release.
    private readonly HashSet<VRCard> _fanOriginCards = new();

    // Live insertion telegraph while a fan-originating card is held (mirrors _snapHighlightSlot):
    // the open gap (0..n, -1 = none) and the card it belongs to, so "what glows is what drops."
    private int _insertGap = -1;
    private VRCard? _insertHighlightCard;

    // Long-rest empty-board dedupe: one Info line per long-rest turn, not one per rebuild
    // (see CollectRoundCards).
    private bool _loggedLongRestEmptyBoard;

    /// <summary>Stable CardInstanceID key for a fan card (int.MinValue = no game card).</summary>
    private static int FanId(VRCard? card) =>
        card != null && card.GameCard != null ? card.GameCard.CardInstanceID : int.MinValue;

    /// <summary>
    /// Stage A: stable-reorder <see cref="_fanBuffer"/> to match the persisted <see cref="_fanOrder"/>
    /// just before <c>_fan.SetCards</c>. Any card whose id is not yet tracked keeps its game-relative
    /// order and is registered (appended); tracked ids no longer in the hand are pruned. This is the
    /// single seam that overrides the game's re-sort. Allocation-free steady state (reused scratch).
    /// </summary>
    private void ReorderFanBuffer()
    {
        int n = _fanBuffer.Count;
        if (n == 0)
            return;

        // Register newcomers (append, preserving current game-relative order among unknowns).
        for (int i = 0; i < n; i++)
        {
            int id = FanId(_fanBuffer[i]);
            if (id != int.MinValue && !_fanOrder.Contains(id))
                _fanOrder.Add(id);
        }
        // Prune tracked ids absent from the current hand (id reuse / hand change hygiene).
        for (int k = _fanOrder.Count - 1; k >= 0; k--)
        {
            int id = _fanOrder[k];
            bool present = false;
            for (int i = 0; i < n; i++)
            {
                if (FanId(_fanBuffer[i]) == id) { present = true; break; }
            }
            if (!present)
                _fanOrder.RemoveAt(k);
        }

        // Emit in _fanOrder order (stable), then any leftover (null-id) card in original order.
        _fanReorderScratch.Clear();
        _fanReorderScratch.AddRange(_fanBuffer);
        _fanBuffer.Clear();
        for (int k = 0; k < _fanOrder.Count; k++)
        {
            int id = _fanOrder[k];
            for (int i = 0; i < _fanReorderScratch.Count; i++)
            {
                VRCard c = _fanReorderScratch[i];
                if (c != null && FanId(c) == id) { _fanBuffer.Add(c); break; }
            }
        }
        for (int i = 0; i < _fanReorderScratch.Count; i++)
        {
            VRCard c = _fanReorderScratch[i];
            if (c != null && !_fanBuffer.Contains(c))
                _fanBuffer.Add(c);
        }
    }

    /// <summary>
    /// Stage D: while a fan-originating OR tray-originating card is held over the OPEN fan and NOT
    /// over a board slot, resolve the nearest inter-card gap and telegraph it (open the gap + gold
    /// overlay + a debounced haptic on edge), caching it for the release commit. Board-slot
    /// telegraph wins so slot-play and fan-reorder never both glow. Clears whenever nothing
    /// eligible is held. Tray-origin (T1): a card lifted OFF a play slot inserts at the gap too —
    /// the release runs the normal take-back (UnselectCard) and then splices the card's id into
    /// the persisted order at the gap (see the tray take-back branch of OnCardReleased).
    /// </summary>
    private void UpdateFanInsertion()
    {
        // THE ORDER WATCH, and it lives here because this is a per-frame call that runs for BOTH
        // fans and sits AFTER Rebuild in the same Update (CardsDriver.2.Update.cs:546 vs :716).
        // That ordering is what makes it a backstop rather than a competitor: a publish has already
        // logged the change with the reason that caused it, so this finds the same signature and
        // stays silent — and it only speaks for a change NO publish announced. Those are real and
        // are exactly the second face of the 2026-08-22 order report: CardFan.Add (a card coming
        // home from an inspect grab) and CardFan.Remove (a pluck) both move the list without one.
        // Free when nothing moved: LogFanOrder's first act is its own allocation-free signature gate.
        LogFanOrder(OffScenarioFanActive ? "map-room hand" : "scenario hand",
            "a change no publish announced — a card was plucked out of the fan or came home to it");
        LogFanOrderMirror();

        // Only the REAL hand fan reorders (CardsSelection). Pick-mode "fans" (discard/burnt piles)
        // reuse the same _fan but must not telegraph a reorder gap — their releases route through
        // HandlePickRelease, never the commit path.
        CardsHandUI? reorderHand = _fakeActive ? null : CurrentHand();
        if (!_fan.IsOpen || reorderHand == null || CardsGameApi.Mode(reorderHand) != CardHandMode.CardsSelection)
        {
            ClearFanInsertion();
            return;
        }
        VRCard? held = HeldCard(out VRHand? holder);
        // Eligible: plucked out of the fan (reorder) OR lifted off a tray slot (T1 — take-back
        // straight into a chosen fan position). Board slot telegraph (UpdateSlotHighlight ran
        // first) wins outright.
        // INSPECTION grabs never telegraph a gap (2026-08-08). A locked CardsSelection hand keeps
        // mode == CardsSelection, so an inspect-only card WOULD reach the gap logic here — but its
        // release routes home before the reorder-commit branch is even considered, so the gold gap
        // would promise a re-seat that cannot happen. Same "what glows is what drops" contract the
        // slot telegraph honours through IsReadOnlyViewerCard.
        bool eligible = held != null && !held.InspectOnly
            && (_fanOriginCards.Contains(held) || _tray.ContainsCard(held));
        if (held == null || holder == null || !eligible || _snapHighlightSlot >= 0)
        {
            ClearFanInsertion();
            return;
        }

        int gap = _fan.NearestGap(held.transform.position);
        _insertHighlightCard = gap >= 0 ? held : null;
        if (gap != _insertGap)
        {
            _insertGap = gap;
            _fan.SetInsertionGap(gap);
            if (gap >= 0)
                holder.SendHaptic(HapticPreset.HoverTick); // debounced: only on gap change
        }
    }

    /// <summary>Drop any live insertion telegraph (fan closed / nothing eligible held / committed).</summary>
    private void ClearFanInsertion()
    {
        if (_insertGap != -1)
        {
            _insertGap = -1;
            _fan.SetInsertionGap(-1);
        }
        _insertHighlightCard = null;
    }

    /// <summary>
    /// Stage E commit: insert the held card's id into <see cref="_fanOrder"/> at the visual gap
    /// (translated to the persisted order, preserving slotted-card ids), then re-add it to the fan
    /// and rebuild so <see cref="ReorderFanBuffer"/> reproduces the new order everywhere. Session-only.
    /// </summary>
    private void CommitFanInsertion(VRCard card, int gap)
    {
        int id = FanId(card);
        if (id == int.MinValue)
        {
            _fan.Add(card);
            return;
        }
        IReadOnlyList<VRCard> fan = _fan.Cards; // current fan order (the held card is already out)
        _fanOrder.Remove(id);
        int insertAt = _fanOrder.Count;
        if (fan.Count == 0)
        {
            insertAt = _fanOrder.Count;
        }
        else if (gap < fan.Count)
        {
            int idx = _fanOrder.IndexOf(FanId(fan[gap]));      // before the card now to its right
            insertAt = idx >= 0 ? idx : _fanOrder.Count;
        }
        else
        {
            int idx = _fanOrder.IndexOf(FanId(fan[fan.Count - 1])); // after the last fan card
            insertAt = idx >= 0 ? idx + 1 : _fanOrder.Count;
        }
        _fanOrder.Insert(insertAt, id);
        _fan.Add(card);
        _dirty = true;
    }

    // ------------------------------------------------------- fan order diagnostic --
    //
    // USER REPORT 2026-08-22: "Die Kartenreihenfolge soll von links nach rechts nach der INITIATIVE
    // der Karten sortiert sein — und ist es am Anfang auch. Aber wenn man Karten HINZUFÜGT, tauchen
    // sie immer am RECHTEN RAND auf statt sich einzusortieren."
    //
    // THE INSTRUMENT THAT WOULD HAVE CAUGHT IT, and the one this build has to be judged on. It
    // prints the fan's cards IN DRAW ORDER with the number they are supposed to be ordered by, and
    // then answers the question directly — sorted, or not, and where it broke.
    //
    // IT ALSO PRINTS HOW MANY COMPARISONS IT ACTUALLY MADE, and that is the point of the line rather
    // than a decoration. A verdict with no comparisons behind it (one card, or a hand whose cards
    // all failed to resolve a key) is NOT evidence of a sorted fan, and this project has already
    // shipped a "clean" reading that meant "the probe never ran" — see the standing note about a
    // scan that only logs on success. So a hand that could not be checked says NOT CHECKED, with
    // the count, and can never be mistaken for a pass.
    //
    // ONE LINE PER CHANGE: deduped on the rendered content, so the near-per-frame Rebuild is silent
    // while every genuine add / remove / reorder / character swap emits exactly once.

    /// <summary>The last fan-order line rendered — the SECOND belt of the change dedupe, behind the
    /// allocation-free signature below. It only ever catches a line the signature called new and
    /// that reads identically anyway (a label that changed and changed back).</summary>
    private string? _lastOrderLine;

    /// <summary>
    /// Cheap change signature of the fan's ORDER — the gate the string building sits behind.
    /// <see cref="Rebuild"/> runs at very nearly every frame while a character is being watched, so
    /// a diagnostic that composed its line first and compared afterwards would allocate a
    /// StringBuilder and a dozen strings per frame. Same rule as <c>CardFan</c>'s: a diagnostic goes
    /// BEHIND its gate, never in front of it. Allocation-free.
    /// </summary>
    private int _lastOrderSig;

    /// <summary>
    /// The initiative the fan orders <paramref name="card"/> by, or <see cref="NoInitiative"/> when
    /// it cannot be resolved. THE SAME NUMBER IN BOTH WORLDS, from the two places it can live:
    /// <list type="bullet">
    /// <item>A SCENARIO card carries the game's own <c>AbilityCardUI</c>, so the key is
    /// <c>GameCard.AbilityCard.Initiative</c> — literally the field the game's own hand comparison
    /// ends on (<c>AbilityCardUI.CompareTo</c>, AbilityCardUI.cs:1295), which is why the scenario
    /// fan is in initiative order without this mod sorting anything: <c>CardsGameApi.GetCards</c>
    /// reads <c>CardsHandUI.cardsUI</c>, and that list is kept sorted by <c>SortCards</c>.</item>
    /// <item>An OFF-SCENARIO (map-room) card carries NO widget by construction, so its key comes
    /// from <see cref="OffScenarioFanInitiatives"/>, published index-aligned with
    /// <see cref="OffScenarioFanCards"/> by the source that owns those models.</item>
    /// </list>
    /// Matched by REFERENCE against the live source list rather than by index into the fan: the fan
    /// legitimately diverges from the published list between a <c>CardFan.Remove</c> and the next
    /// publish, and an index would then read a neighbour's number.
    /// </summary>
    private static int FanInitiative(VRCard? card)
    {
        if (card == null)
            return NoInitiative;
        AbilityCardUI? widget = card.GameCard;
        if (widget != null && widget.AbilityCard != null)
            return widget.AbilityCard.Initiative;
        IReadOnlyList<VRCard>? source = OffScenarioFanCards;
        IReadOnlyList<int>? keys = OffScenarioFanInitiatives;
        if (source == null || keys == null)
            return NoInitiative;
        int n = source.Count < keys.Count ? source.Count : keys.Count;
        for (int i = 0; i < n; i++)
        {
            if (ReferenceEquals(source[i], card))
                return keys[i];
        }
        return NoInitiative;
    }

    /// <summary>A card's short label for the order line: the game's own card name where the card
    /// carries a widget, otherwise the object name the source gave it (which for a map-room card
    /// already carries its id).</summary>
    private static string FanCardLabel(VRCard? card)
    {
        if (card == null)
            return "<null>";
        AbilityCardUI? widget = card.GameCard;
        if (widget != null && !string.IsNullOrEmpty(widget.CardName))
            return widget.CardName;
        return card.name;
    }

    /// <summary>Change key for <see cref="LogFanOrderMirror"/> - the pair of fingerprints and the
    /// pair of lengths, so the line fires on a real order edge and never on the frame rate.
    /// </summary>
    private long _lastOrderMirrorKey = long.MinValue;

    /// <summary>
    /// An ORDER-SENSITIVE fold over a list of ability-card widgets, keyed on
    /// <c>CardInstanceID</c>. The id is the MODEL's, not a <c>GetInstanceID()</c>, and that is the
    /// whole point: it is host-replicated, so the same hand in the same order folds to the same
    /// word on every machine in the session and the two logs compare without arithmetic.
    /// </summary>
    private static uint OrderFingerprint(System.Collections.Generic.IReadOnlyList<AbilityCardUI?> list)
    {
        uint fp = 2166136261u;
        unchecked
        {
            for (int i = 0; i < list.Count; i++)
            {
                AbilityCardUI? w = list[i];
                fp = (fp ^ (uint)i) * 16777619u;
                fp = (fp ^ (uint)(w != null ? w.CardInstanceID : 0)) * 16777619u;
            }
        }
        return fp;
    }

    /// <summary>Scratch for <see cref="LogFanOrderMirror"/>'s two walks. Reused; the line is
    /// change-gated and this runs on a per-frame call.</summary>
    private readonly List<AbilityCardUI?> _orderMirrorDrawn = new(16);

    /// <inheritdoc cref="_orderMirrorDrawn"/>
    private readonly List<AbilityCardUI?> _orderMirrorDerived = new(16);

    /// <summary>
    /// HARDWARE EVIDENCE for report item 2 of 2026-09-06. Grep token: FAN ORDER MIRROR.
    ///
    /// <para>THE COMPLAINT, verbatim: "Im Test war der remote Faecher anders als der Faecher die er
    /// gesehen hat, heisst: Ich habe ganz rechts eine andere Karte gesehen als der Spieler selber.
    /// Das darf niemals passieren. Auch nach umsortieren etc. muessen die Karten exakt an den
    /// selben Stellen remote zu sehen sein wie lokal beim Spieler."</para>
    ///
    /// <para>WHY IT CAN BE ANSWERED FROM ONE LOG, which is what makes this line worth its bytes:
    /// the co-player's log was not copied this round, and a fingerprint that can only be compared
    /// against a log that does not exist proves nothing. So this line compares the two orders on
    /// THIS machine. <c>drawn</c> is the arc this player is actually looking at - the fan's own
    /// list, which is <see cref="ReorderFanBuffer"/>'s output and therefore carries
    /// <c>_fanOrder</c>, the player's session-only drag-reorder. <c>derived</c> is the SAME cards
    /// walked the way EVERY observer re-derives them: <c>CardsHandUI.cardsUI</c> in its own order,
    /// filtered by <c>CardsGameApi.HandFanMember</c> - which is verbatim what
    /// <c>Net.RemoteHandFan.FillHandBuffer</c> does on the other machine. If those two differ, every
    /// other player is drawing this hand in a different order from its owner, and that is item 2
    /// proven with no peer log at all.</para>
    ///
    /// <para>READ IT LIKE THIS.</para>
    /// <list type="bullet">
    ///   <item><c>MATCH</c> - the arc this player sees is the arc an observer re-derives. Item 2
    ///     is not live for this hand at this moment. Note that this is a claim about ORDER only;
    ///     the length pair beside it is what says whether both lists even hold the same cards.
    ///     </item>
    ///   <item><c>DIVERGED</c> - the orders differ, and the two <c>right='X'</c> names say exactly
    ///     how the user would notice: the right-most card of the fan he is holding against the
    ///     right-most card every other player sees. THIS IS THE WORKING READING OF THE DEFECT and
    ///     it needs no screenshot and no second machine.</item>
    ///   <item><c>n=A/B</c> with A != B - the two walks did not even collect the same number of
    ///     cards, so the order verdict is not the finding; the MEMBERSHIP is. A held card is the
    ///     ordinary cause (the fan drops it for one frame on the pluck and CardsDriver.Rebuild puts
    ///     it back), and a persistent inequality is a filter disagreement, which is report item 5b
    ///     of the previous round happening again.</item>
    /// </list>
    ///
    /// <para>AND THE DIVERGENCE IS MEASURED, NOT PREDICTED. In the 2026-09-06 session both logs
    /// carry it: 38 of the co-player's 54 non-empty <c>Fan order [scenario hand]</c> readings say
    /// NOT SORTED, and 21 of the host's 33 do. NOT SORTED means the arc its owner is looking at is
    /// not in initiative order - while an observer rebuilds that same hand from
    /// <c>CardsHandUI.cardsUI</c>, which the game keeps in <c>AbilityCardUI.CompareTo</c> order.
    /// The two orders therefore differ for most of the session, in BOTH directions, and that is
    /// report item 2 with a number on it: "ganz rechts eine andere Karte".</para>
    ///
    /// <para>THE PARAGRAPH THAT STOOD HERE IS FALSE AND WAS FALSE FOR NINE BUILDS. It read
    /// "NOTHING HERE IS A FIX. The order is NOT on the wire - <c>_fanOrder</c> is session-local and
    /// no record carries a permutation". <c>NetProtocol.ExtIdFanArcOrder</c> (record 44) has
    /// carried exactly that permutation since ModBuild 462, and the shipped log STRING said the
    /// same thing to whoever grepped it. So read this line for what it now is: a statement about
    /// THIS machine's two orders and nothing about the wire. A DIVERGED reading means the owner's
    /// arc is not the game's order — which is the state record 44 exists to carry, not a defect on
    /// its own. Whether it travelled is the <c>FAN ARC ORDER SENT</c> line beside it
    /// (<c>LocalRigSampler</c>), and whether it landed is <c>MIRRORED ARC ORDER</c> on the watcher;
    /// all three print the SAME fingerprint for the same arc.</para>
    /// </summary>
    private void LogFanOrderMirror()
    {
        CardsHandUI? hand = CurrentHand();
        if (hand == null || hand.cardsUI == null)
            return;
        CPlayerActor? actor = hand.PlayerActor;

        _orderMirrorDrawn.Clear();
        IReadOnlyList<VRCard> drawn = _fan.Cards;
        for (int i = 0; i < drawn.Count; i++)
            _orderMirrorDrawn.Add(drawn[i] != null ? drawn[i].GameCard : null);

        _orderMirrorDerived.Clear();
        List<AbilityCardUI> all = hand.cardsUI;
        for (int i = 0; i < all.Count; i++)
        {
            if (CardsGameApi.HandFanMember(all[i], actor))
                _orderMirrorDerived.Add(all[i]);
        }

        uint fpDrawn = OrderFingerprint(_orderMirrorDrawn);
        uint fpDerived = OrderFingerprint(_orderMirrorDerived);
        long key = ((long)fpDrawn << 32) ^ ((long)fpDerived << 8)
                   ^ ((long)_orderMirrorDrawn.Count << 4) ^ _orderMirrorDerived.Count;
        if (key == _lastOrderMirrorKey)
            return;
        _lastOrderMirrorKey = key;

        string rightDrawn = _orderMirrorDrawn.Count > 0
            ? WidgetLabel(_orderMirrorDrawn[_orderMirrorDrawn.Count - 1]) : "<empty>";
        string rightDerived = _orderMirrorDerived.Count > 0
            ? WidgetLabel(_orderMirrorDerived[_orderMirrorDerived.Count - 1]) : "<empty>";

        // HW-VERIFY: report item 2 (2026-09-06). Grep token: FAN ORDER MIRROR.
        VRLog.Note("Cards", $"FAN ORDER MIRROR: {(fpDrawn == fpDerived ? "MATCH" : "DIVERGED")} — "
            + $"n={_orderMirrorDrawn.Count}/{_orderMirrorDerived.Count}, drawn fp={fpDrawn:x8} "
            + $"right='{rightDrawn}', derived fp={fpDerived:x8} right='{rightDerived}'. DRAWN is "
            + "the arc THIS player is looking at (CardFan's own list, i.e. ReorderFanBuffer's "
            + "output, which carries _fanOrder — their session-only drag-reorder). DERIVED is the "
            + "same hand walked the way every OBSERVER re-derives it (CardsHandUI.cardsUI in its "
            + "own order, filtered by CardsGameApi.HandFanMember), which is verbatim what "
            + "Net.RemoteHandFan does on the other machine. DIVERGED therefore means every other "
            + "player is drawing this hand in a different order from its owner — report item 2, "
            + "'ganz rechts eine andere Karte', proven from THIS log with no peer log needed, and "
            + "the two right='' names are the two cards he would compare. The fingerprint is an "
            + "order-sensitive fold over CardInstanceID, which is host-replicated, so it is also "
            + "directly comparable with the peer's 'MIRRORED ARC ORDER' fp for this same hand when "
            + "their log IS present. n=A/B with A!=B is a MEMBERSHIP disagreement and not an order "
            + "one: read it first, because the order verdict beside it is then meaningless. THE "
            + "ARC ORDER IS ON THE WIRE since ModBuild 462 (extension record 44), so a DIVERGED "
            + "reading here is NOT by itself a divergence any watcher sees — it says only that the "
            + "owner's arc is not the game's order, which is exactly what the record exists to "
            + "carry. The reading that decides whether it TRAVELLED is this machine's own 'FAN ARC "
            + "ORDER SENT' line beside it: SENT with the same fp means the arc was stated in full, "
            + "WITHHELD names the exit that refused it. Before the 2026-09-07 fix this string ended 'THIS "
            + "BUILD DOES NOT FIX A DIVERGENCE — the arc order is on no wire', which had been false "
            + "for nine builds and sent the next reader looking for a record that already existed.");
    }

    /// <summary>A widget's card name for a log line, never null.</summary>
    private static string WidgetLabel(AbilityCardUI? widget)
        => widget != null && !string.IsNullOrEmpty(widget.CardName) ? widget.CardName : "<unnamed>";

    /// <summary>
    /// Print the fan's card list IN DRAW ORDER with each card's initiative, plus the sortedness
    /// verdict and the number of comparisons behind it. Called after every seam that changes what
    /// the fan holds; deduped on content, so it is one line per real change.
    /// </summary>
    /// <param name="fan">Which fan this is — "scenario hand" or "map-room hand".</param>
    /// <param name="reason">What just changed, in the user's vocabulary.</param>
    private void LogFanOrder(string fan, string reason)
    {
        IReadOnlyList<VRCard> cards = _fan.Cards;
        int n = cards.Count;

        // THE GATE (allocation-free), see _lastOrderSig: identity AND key of every card in draw
        // order, so a reorder, an add, a removal and a re-keyed card all move it — and a steady
        // per-frame rebuild does not.
        int sig = 17;
        unchecked
        {
            sig = sig * 31 + n;
            sig = sig * 31 + fan.Length;
            for (int i = 0; i < n; i++)
            {
                VRCard c = cards[i];
                sig = sig * 31 + (c != null ? c.GetInstanceID() : 0);
                sig = sig * 31 + FanInitiative(c);
            }
            if (sig == 0)
                sig = 1;   // 0 is the never-logged seed; a real signature must not collide with it
        }
        if (sig == _lastOrderSig)
            return;
        _lastOrderSig = sig;

        var sb = new System.Text.StringBuilder(160);
        int known = 0;       // cards whose initiative resolved
        int compared = 0;    // adjacent pairs actually compared
        int breakAt = -1;    // first draw index that sits left of a smaller initiative
        int prev = NoInitiative;
        for (int i = 0; i < n; i++)
        {
            VRCard card = cards[i];
            int init = FanInitiative(card);
            if (i > 0)
                sb.Append("  ");
            sb.Append(FanCardLabel(card)).Append('(');
            if (init == NoInitiative)
                sb.Append('?');
            else
                sb.Append(init);
            sb.Append(')');
            if (init == NoInitiative)
                continue;
            known++;
            if (prev != NoInitiative)
            {
                compared++;
                if (init < prev && breakAt < 0)
                    breakAt = i;
            }
            prev = init;
        }

        string verdict = n == 0
            ? "EMPTY — no cards, nothing to order."
            : compared == 0
            ? $"NOT CHECKED — 0 comparison(s) were possible ({known} of {n} card(s) resolved an "
              + "initiative, and two are needed for one comparison). This is NOT the same claim as "
              + "SORTED: nothing was verified. For a one-card hand that is the honest answer; for a "
              + "fuller one it means the keys did not resolve, which is itself the finding."
            : breakAt >= 0
                ? $"NOT SORTED — {compared} comparison(s) made, and the one at draw index {breakAt} "
                  + $"failed: '{FanCardLabel(cards[breakAt])}' sits RIGHT of a higher initiative. "
                  + "A card that lands at the right edge instead of its place is exactly this."
                : $"SORTED ascending — {compared} comparison(s) made across {known} of {n} card(s).";

        string line = $"Fan order [{fan}]: n={n}, {verdict}\n  draw order (left to right): "
                      + (n == 0 ? "<empty>" : sb.ToString());
        if (line == _lastOrderLine)
            return;   // nothing about the fan's order moved since the last line
        _lastOrderLine = line;
        VRLog.Info("Cards", line + $"\n  after: {reason}. The number in brackets is the card's "
            + "INITIATIVE — a scenario card's comes off the game's own AbilityCardUI.AbilityCard, a "
            + "map-room card's off the loadout model its source published. '?' means the key could "
            + "not be resolved for that card; it is excluded from the verdict, never guessed at.");
    }

    // ------------------------------------------------------------------ rebuild --

    /// <summary>
    /// Live board switch executor: re-park every card currently seated ON the tray back
    /// to the factory pool, THEN tear the tray down so the next <see cref="Rebuild"/>
    /// loads the newly selected prefab. Slot occupants / pick-field / short-rest cards
    /// are parented under the tray root, so <see cref="PlayTray.Destroy"/>'s
    /// DestroyImmediate would otherwise destroy those factory-owned VRCards and orphan
    /// their adopted game faces — re-parenting them to the pool first keeps them (and
    /// their faces) alive; the forced rebuild re-seats them from authoritative state.
    /// </summary>
    private void RebuildBoard()
    {
        // The poke-toggle pile browse fan is parented under the board root (so it inherits the
        // board's live scale/pose) — a board switch DestroyImmediates that root. Close the browse
        // FIRST (it does not touch the cards' parents), so the park loop below still catches the
        // adopted browse cards as children of trayRoot and re-parks them out before the teardown.
        CloseBrowser("board rebuilt");
        Transform? trayRoot = _tray.Root;
        if (trayRoot != null)
        {
            for (int i = 0; i < _factory.All.Count; i++)
            {
                VRCard card = _factory.All[i];
                if (card != null && !card.IsHeld && card.transform.IsChildOf(trayRoot))
                {
                    // Board rebuilt MID-FLIGHT / mid-burn-hold: Park cancels a running FlyToPile
                    // (VRCard.Park → CancelFly), so its completion callback never fires — drop the
                    // driver-side flight membership and any pending burn-artwork hold here, or the
                    // stale entries would block ("ownedElsewhere") or later re-launch ("hold
                    // release") an animation for a card the pool already owns.
                    _flyingToPile.Remove(card);
                    if (card.GameCard != null)
                        ClearBurnHold(card.GameCard);
                    _factory.Park(card);
                }
            }
        }
        // PART D: capture the outgoing board's world pose BEFORE Destroy so the new board keeps
        // the EXACT same location — and, since 2026-08-25, the EXACT same SIZE. Destroy() is what
        // makes the size hard: it DestroyImmediates the pinned board's "TrayPin" holder, so the
        // three numbers below land in a DIFFERENT parent frame on the far side (measured on his
        // hardware: parent chain ×30.85 → ×9.57, a 3.224× shrink, with localScale bit-identical).
        // TryCapturePose therefore snapshots the frame as well — see the block above
        // PlayTray.CaptureSwitchFrame — and RestorePose re-establishes it below.
        _hasSwitchPose = _tray.TryCapturePose(out _switchPos, out _switchRot, out _switchScale);
        _tray.Destroy();
        _dockAnimSuppressed = true; // issue 2: the rebuilt board re-populates its cards silently (no storm)
        _dirty = true;
    }

    // ============================================================ A DEAD BOARD HAS NO CARDS ======
    //
    // The LOCAL half of the rule; the rule itself, its evidence and the argument for enforcing it
    // at ONE seam are on Board.CharacterFocus.BoardCarriesCards. Everything here is instrumentation:
    // the rebuild's `hand == null` arm below already drains every population, and this states — in
    // numbers, on both sides of that drain — that it did.

    /// <summary>One reading of every CARD population on the local board. Sampled from the surfaces
    /// themselves, never from the model, because the report is about what is DRAWN.</summary>
    private readonly struct ExhaustedCounts
    {
        internal readonly int Fan;
        internal readonly int Slots;
        internal readonly int Halves;
        internal readonly int Active;
        internal readonly int Discard;
        internal readonly int Burnt;
        internal readonly int Items;
        internal readonly bool StacksShown;
        internal readonly int Field;
        internal readonly bool BrowseOpen;
        internal readonly bool ItemFanOpen;
        internal readonly bool TrayShown;

        internal ExhaustedCounts(int fan, int slots, int halves, int active, int discard, int burnt,
            int items, bool stacksShown, int field, bool browseOpen, bool itemFanOpen, bool trayShown)
        {
            Fan = fan; Slots = slots; Halves = halves; Active = active;
            Discard = discard; Burnt = burnt; Items = items; StacksShown = stacksShown;
            Field = field; BrowseOpen = browseOpen; ItemFanOpen = itemFanOpen; TrayShown = trayShown;
        }

        public override string ToString() =>
            $"fan={Fan}, round-card slots={Slots}, docked halves={Halves}, active={Active}, "
            + $"stacks={(StacksShown ? $"shown d{Discard}/b{Burnt}/i{Items}" : "hidden")}, "
            + $"pick/decision field={Field}, browse arc={(BrowseOpen ? "open" : "closed")}, "
            + $"item fan={(ItemFanOpen ? "open" : "closed")}, tray={(TrayShown ? "up" : "down")}";
    }

    /// <summary>Read every card population the local board can carry. THE ENUMERATION — if a new
    /// population is ever added to the board it belongs in this list too, because "empty except for
    /// one of them" is the defect this instrument exists to catch.</summary>
    private ExhaustedCounts SampleBoardCards()
    {
        int slots = 0;
        for (int s = 0; s < 2; s++)
            if (_tray.Occupant(s) != null)
                slots++;
        (int discard, int burnt, int items)? counts = PileViewer.CurrentCounts;
        return new ExhaustedCounts(
            fan: _fan.Count,
            slots: slots,
            halves: _halfBuffer.Count,
            active: _active.Cards.Count,
            discard: counts?.discard ?? 0,
            burnt: counts?.burnt ?? 0,
            items: counts?.items ?? 0,
            // THE RENDERED FACT, not CurrentCounts.HasValue. CurrentCounts is the WIRE seam and is
            // nulled by the per-frame TickStatus, which on the rebuild frame has not run yet; the
            // stacks' own activeSelf is what the player is looking at.
            stacksShown: _piles.StacksShown,
            field: _fieldCards.Count,
            browseOpen: PileBrowser.Current != null && PileBrowser.Current.IsOpen,
            itemFanOpen: _piles.ItemsBrowseOpen,
            trayShown: _tray.IsVisible);
    }

    /// <summary>Per-character gate for the clear line — one line per death, not one per rebuild
    /// (the clear arm runs every rebuild for as long as the character stays dead).</summary>
    private int _loggedExhaustedActorId;

    /// <summary>Session total, for the ARMED line's falsifier reading.</summary>
    private int _exhaustedClears;

    /// <summary>The death whose CLEARED line is captured but not yet emitted, and the BEFORE
    /// reading taken on the frame the clear started. Held across the pile stacks' two-frame hide
    /// grace so the AFTER reading is the settled picture — see the clear arm in
    /// <see cref="Rebuild"/>.</summary>
    private CPlayerActor? _pendingExhaustedLog;

    private ExhaustedCounts _pendingExhaustedBefore;

    private void LogExhaustedBoardClear(CPlayerActor exhausted, in ExhaustedCounts before)
    {
        int id = Net.NetFigures.StableActorId(exhausted);
        if (id == _loggedExhaustedActorId)
            return;
        _loggedExhaustedActorId = id;
        _exhaustedClears++;
        ExhaustedCounts after = SampleBoardCards();
        // HW-VERIFY
        VRLog.Note("Cards", $"EXHAUSTED BOARD: CLEARED (local) — "
            + $"'{Board.CharacterFocus.Describe(exhausted)}' (actor {id}) is exhausted, so the "
            + "control board it was presenting carries no cards at all (user 2026-09-05 #13: "
            + "\"wenn ein Character tot ist ... sollen dort gar keine Karten mehr liegen\"). "
            + $"CARD POPULATIONS BEFORE: {before}. AFTER: {after}. "
            + $"This is clear #{_exhaustedClears} of this session. "
            + "READ IT LIKE THIS: every AFTER number must be 0 and the stacks HIDDEN — a non-zero "
            + "one names the population that survived, which is the whole defect and not a detail. "
            + "A BEFORE that is already all-zero means the board was empty anyway and this line "
            + "proves the RULE ran, not that it removed anything. tray=up in the AFTER reading is "
            + "CORRECT and required: the board is the scenario dashboard (initiative track, "
            + "objectives, element strip) and a just-killed player still needs it. THIS RULE IS "
            + "CARDS ONLY, and it is not the whole story any more: the rest discs are withdrawn "
            + "by RestControls.RestUiOffered, the CARD-SELECTION confirm/revoke by "
            + "PlayTray.SelectionCapRefusedByDeath (user 2026-09-06 #9), and what remains of the "
            + "confirm is the party-wide continue only. Grep 'DEAD OWNER BOARD' for the control "
            + "set this board actually drew.");
    }

    /// <summary>Per-scenario latch for the ARMED line (see <see cref="NoteExhaustedRuleArmed"/>).</summary>
    private bool _exhaustedRuleArmed;

    /// <summary>
    /// THE FALSIFIER. Without this line a session in which NOBODY dies and a session in which the
    /// rule silently failed to run look identical — both are silent. One line per scenario entry
    /// says the rule is live and how many boards it has cleared, so "no CLEARED line" reads as
    /// "nobody died" only when this line is present to say so.
    /// </summary>
    private void NoteExhaustedRuleArmed()
    {
        if (!CardsGameApi.InScenario)
        {
            _exhaustedRuleArmed = false;   // the next scenario entry re-states it
            _loggedExhaustedActorId = 0;   // …and a new scenario's death is a fresh edge
            _pendingExhaustedLog = null;   // a capture that never settled dies with the scenario
            return;
        }
        if (_exhaustedRuleArmed)
            return;
        _exhaustedRuleArmed = true;
        // HW-VERIFY
        VRLog.Note("Cards", "EXHAUSTED BOARD: ARMED (local) — the rule that a dead character's "
            + "board carries no cards is live on this client for this scenario, enforced at "
            + "Board.CharacterFocus (the one seam every card population on the board is fed from) "
            + $"and drained by the rebuild's no-hand arm. {_exhaustedClears} board(s) cleared so "
            + "far this session. FALSIFIER: this line WITHOUT a later 'EXHAUSTED BOARD: CLEARED' "
            + "means nobody was exhausted while this scenario ran — it does NOT mean the clear "
            + "worked. NO line at all means the rule never even reached a rebuild in a scenario, "
            + "which is a broken build, not a quiet success. The peer-side twin is "
            + "'[Net] EXHAUSTED BOARD [id]' and both must agree: the 1:1 rule makes the owner's "
            + "board and its mirror one verdict, not two.");
    }

    private void Rebuild(Transform anchor)
    {
        NoteExhaustedRuleArmed();

        // FREE CHARACTER FOCUS: the game's own presented hand goes in, and the hand the player
        // asked to LOOK at comes out. With no focus this is the identity function, so every path
        // below is byte-for-byte the pre-feature one. With a focus it is an OVERRIDE, and
        // CharacterFocus.ReadOnlyView is latched for the whole rebuild — see the read-only
        // handling below and at the per-card zone stamp, which together make "you cannot act on a
        // character you are only looking at" a property of the objects rather than a convention.
        CardsHandUI? hand = Board.CharacterFocus.ResolveHand(CurrentHand());

        // THE SWAP EDGE (user 2026-08-09, the hand-fan exchange — see the block on
        // _lastPresentedActorId in part 1 for why THIS is the predicate and a list diff is not).
        // ResolveHand has just re-derived PresentedActorId for this rebuild, so comparing it to the
        // previous one asks exactly "is the board now showing a DIFFERENT character's hand than it
        // was". Three further conditions, each of which would otherwise animate something nobody is
        // looking at or something that is not an exchange:
        //   * both ids non-zero — entering and leaving a scenario is not a character swap;
        //   * the fan is OPEN — a hand the player is not holding up has nothing to exchange, and
        //     the whole deferred-face-restore path below stays dormant, i.e. the risky part of this
        //     feature does not exist unless the animation is actually on screen;
        //   * the fan has cards, or the incoming hand does — otherwise there is nothing to move.
        int presentedId = Board.CharacterFocus.PresentedActorId;
        bool handSwap = presentedId != 0 && _lastPresentedActorId != 0
                        && presentedId != _lastPresentedActorId
                        && _fan.IsOpen && (_fan.Count > 0 || Board.CharacterFocus.HandWidgetCount(hand) > 0);
        _lastPresentedActorId = presentedId;
        // ARM THE OUTGOING HALF NOW, not at SetCards. The per-card park sweep further down teleports
        // every VR card that ends this rebuild in no zone straight into the pool — and the hand we
        // are replacing is in no zone by definition. Capturing the wave here is what makes
        // CardFan.IsLeaving answer TRUE while that sweep runs, so the cards it is about to fly away
        // are skipped instead of parked. (CardFan.BeginSwapOut states the same thing from its side.)
        if (handSwap)
            _fan.BeginSwapOut();

        // Give a previously focused character its faces back BEFORE this rebuild adopts anything —
        // and say so in the log. Runs first because it destroys VR cards, which must not happen
        // once this pass has started filling its buffers. DURING A SWAP it only QUEUES the restore:
        // the outgoing cards are still flying and they still need their faces to fly with.
        ReleaseStaleFocusHand(hand, handSwap);

        if (hand == null)
        {
            _hasSwitchPose = false; // no board to re-pose without a hand
            // …and drop the frame the capture took with it. A captured parent frame is only ever
            // valid for the ONE restore it was taken for; leaving it armed here would let a much
            // later, unrelated restore (session resume, carried-pose rebuild) re-establish a holder
            // scale from a board switch that never completed.
            _tray.DiscardCapturedPose();
            // A focus view that ends with no hand at all (scenario teardown, hand mid-rebuild)
            // must not leave the fan latched in a restricted mode: the next interactive fan would
            // be inert.
            _fan.SetMode(CardFan.FanMode.Interactive);
            // A DEAD BOARD HAS NO CARDS (user 2026-09-05 #13). This arm is where the clear happens
            // for EVERY reason a board can lose its hand; the seam tells us when the reason was a
            // DEATH, which is the only one the user asked to be able to read off a log. The counts
            // are taken on both sides of RebuildFakeOrClear because "it cleared" and "there was
            // nothing there anyway" are the two readings a hardware round has to tell apart.
            CPlayerActor? exhausted = Board.CharacterFocus.ExhaustedRefusal;
            if (exhausted != null && _pendingExhaustedLog == null)
            {
                // Captured BEFORE the clear, and held until the clear has fully settled (below).
                // Deliberately NOT gated on the logger's own per-character latch: reading a
                // diagnostic's change gate from the rebuild would make that gate load-bearing, and
                // a logger whose state the mechanism depends on is the exact shape that once
                // nearly latched the wall fade off forever. The latch stays inside
                // LogExhaustedBoardClear, which simply declines a repeat.
                _pendingExhaustedLog = exhausted;
                _pendingExhaustedBefore = SampleBoardCards();
            }
            RebuildFakeOrClear(anchor);
            // THE PILE STACKS HIDE ON A TWO-FRAME GRACE (PileViewer.HidePending): the first call
            // only ARMS it, and this method is the grace's only driver. A board that has gone quiet
            // — which an exhausted character's board does by construction, its own change signals
            // having all stopped — could otherwise take the arming call as the LAST one and keep
            // three stacks of a dead character's numbers up for the rest of the scenario. Re-arming
            // the rebuild until the hide has landed is the general fix: it covers every reason a
            // board loses its hand, not only this one, and costs one extra rebuild.
            //
            // THE EVIDENCE LINE WAITS FOR THE SAME SETTLE, and that is not cosmetic: an AFTER
            // reading taken on the arming frame would report "stacks shown" every single time and
            // the instrument would ship convicting its own fix. Read the whole picture or say
            // nothing yet.
            if (_piles.HidePending)
            {
                _dirty = true;
            }
            else if (_pendingExhaustedLog != null)
            {
                LogExhaustedBoardClear(_pendingExhaustedLog, _pendingExhaustedBefore);
                _pendingExhaustedLog = null; // released whether or not the line printed
            }
            return;
        }
        if (_fakeActive)
            ClearFakeCards();
        _boundHand = hand;

        bool hadTrayRoot = _tray.Root != null;
        _tray.EnsureBuilt(_factory, anchor);
        // PART D: on a board SWITCH, re-apply the captured pose (the new board spawns in the exact
        // same place) instead of PlaceAtHead. A genuine first build has no captured pose and places
        // at the head as usual.
        if (_hasSwitchPose)
        {
            _hasSwitchPose = false;
            _expectedPoseChange = "rebuild-restored (board switch)"; // sanctioned (issue C watchdog)
            _tray.RestorePose(_switchPos, _switchRot, _switchScale);
        }
        else if (!hadTrayRoot && TryRestoreCarriedPose())
        {
            // ISSUE C: the tray root was re-created OUTSIDE the board-switch path (e.g. torn
            // down externally) — carry the previous pose over instead of re-placing at the
            // head. The factory re-creates the object; the pose survives.
            VRLog.Info("Cards", "Tray rebuilt — previous board pose carried over (no re-place at head).");
        }
        _rest.EnsureBuilt(_tray);
        _half.EnsureBuilt(anchor);
        _half.DockTo(_tray); // action selection lives on the control board (test #19)

        // Pile viewer (test #21): stacks exist whenever an active local hand does.
        // Always on — user ruling 2026-08-11: essential (the only way to see the piles in VR).
        _piles.EnsureBuilt(_tray);
        _piles.SetVisible(true);

        CardHandMode mode = CardsGameApi.Mode(hand);
        CardsGameApi.GetCards(hand, _widgetBuffer);

        // Issue 5: snapshot the PREVIOUS rebuild's docked round cards BEFORE clearing, so the park
        // sweep below can recognise a just-cleared played card and fly it into its pile.
        _lastHalfCards.Clear();
        _lastHalfCards.UnionWith(_halfBuffer);

        // Issue 1: snapshot the PREVIOUS rebuild's slot occupants (the vanish set) and every card
        // that was visible in a zone (the appear-suppression set) BEFORE SyncFromGameState re-seats
        // for this (possibly new) character. Read the tray occupants LIVE here — they are still the
        // outgoing character's cards until the sync below evicts them.
        _lastTrayCards.Clear();
        _lastVisibleCards.Clear();
        _lastVisibleCards.UnionWith(_fanBuffer);
        _lastVisibleCards.UnionWith(_halfBuffer);
        // EVENT-DISCARD EXIT (user 2026-08-24): the pick field's counterpart to _lastHalfCards.
        // The FINAL card of an event discard leaves the field only when the game's own confirm
        // dialog commits — by which time the model DOES answer Discarded, so the existing
        // model-driven TryStartFlyToPile can carry it. It just needed a set to be a member of; the
        // pre-batch pages are carried earlier and by a different trigger (FlyLockedPicksToPile).
        _lastFieldCards.Clear();
        _lastFieldCards.UnionWith(_fieldCards);
        for (int s = 0; s < 2; s++)
        {
            VRCard? occ = _tray.Occupant(s);
            if (occ != null)
            {
                _lastTrayCards.Add(occ);
                _lastVisibleCards.Add(occ);
            }
        }

        _fanBuffer.Clear();
        _halfBuffer.Clear();
        // The fan's SOURCE PILE is rewritten by whichever branch below fills the fan, so it starts
        // every rebuild at "nothing is being fanned". Only the modal-pick branch ever sets it to a
        // pile: an ordinary hand fan is the DEFAULT and is deliberately left unsaid, which is what
        // keeps the wire record it feeds absent on every packet of every player who is not mid-pick.
        _fanSourcePile = CardPileType.None;

        // Test #15: the tray is the central DASHBOARD — visible for the whole
        // scenario (initiative track, objectives, confirm/undo, settings), not only
        // during card selection. Cards remain grabbable only in CardsSelection.
        bool trayVisible = true;
        bool halfVisible = false;
        bool grabbable = false;

        // READ-ONLY FOCUS VIEW (feature "free character focus"). The player is looking at a
        // character the game is not presenting to them — a teammate, or one of their own that is
        // not the one acting. What they asked to see is exactly three things, and this branch
        // builds all three and nothing else:
        //   (c) that character's HAND cards in the fan — CardPileType.Hand off the character's own
        //       widget list. NOTE the vanilla branches below would show nothing here: the hand fan
        //       is gated on CardsGameApi.IsSelectionPhase, which is false during a turn. Hand
        //       cards are not secret in Gloomhaven (CCharacterClass.HandAbilityCards is
        //       host-replicated with no visibility gate, CCharacterClass.cs:91) — only the two
        //       CHOSEN round cards are, and only during SelectAbilityCardsOrLongRest, which
        //       CharacterFocus.Open refuses outright and RevealGate independently re-refuses.
        //   (a) the cards that character CHOSE this round, lying in the board's two card SLOTS —
        //       for as long as the game's replicated model still names them, NOT only during that
        //       character's own turn (Board.CharacterFocus.RoundCardDock; user 2026-08-08 "ich will
        //       AUCH, dass dort dann immer die jeweiligen ausgewählten Karten liegen"). The turn key
        //       was the bug: a teammate is focused precisely BECAUSE they are not acting.
        //   (b) their piles — the discard/burnt/items stacks and the browse arc follow `hand`
        //       automatically, further down; nothing extra is needed for them here.
        // grabbable stays FALSE, so the per-card stamp near the end of this method builds every
        // one of these cards non-grabbable and non-pokeable, and CardFan refuses the laser. The
        // slot cards get the SAME treatment through _half.SetReadOnly further down: no poke zones
        // are armed and the card canvas is never registered with UguiPokeSurfaces, so the dock is a
        // picture there too.
        bool readOnly = Board.CharacterFocus.ReadOnlyView;
        // INSPECTION ENTITLEMENT (user ruling 2026-08-08 — taking a card out of the hand to LOOK at
        // it must never be blocked). Latched ONCE for the whole rebuild, exactly like `readOnly`,
        // so the zone stamp and the fan mode below can never disagree about it. The rule and its
        // source evidence live in Board.CharacterFocus.HandInspectable: every character the local
        // client controls (which offline is every merc), never a foreign one.
        bool handInspectable = Board.CharacterFocus.HandInspectable(hand);
        if (readOnly)
        {
            // SLOT CARDS FIRST, so the fan can exclude them (see below).
            bool dockOpen = Board.CharacterFocus.RoundCardDock(hand, out string dockSource);
            if (dockOpen)
                CollectRoundCards(hand, _halfBuffer);
            // An EMPTY dock hides the layout outright rather than showing an armed-but-empty one —
            // "nothing chosen / nothing left to show" must look like an empty board, not a broken
            // one. The reason lands in the log line below.
            halfVisible = _halfBuffer.Count > 0;
            LogFocusSlotCards(hand, dockOpen, dockSource);

            FillHandFan();
            _tray.ClearSlots(); // no slot OCCUPANCY belongs to a character we are only watching —
                                // the dock parents the cards to the slot transforms without ever
                                // claiming a recess, so nothing becomes laser-pluckable
                                // (PlayTray.TryRaycastCards scans _occupants only).
        }
        else
        switch (mode)
        {
            case CardHandMode.CardsSelection:
                // Item B (mode-desync lock): CardsHandUI.currentMode STAYS CardsSelection
                // after the player confirms — it is only re-driven by the next
                // CardsHandManager.Show(...), which never runs during the enemy turn — so
                // the raw mode is a stale trap. The hardware repro sat in this stale
                // CardsSelection fan all through the enemy turn with the played cards still
                // reclaimable and the fan bound. Gate the whole INTERACTIVE selection on the
                // game's own phase (CardsGameApi.IsSelectionPhase == the exact
                // SelectAbilityCardsOrLongRest gate the game uses, CardsHandUI.cs:1516/1564):
                // - selecting → the grabbable hand fan + free slot placement, as before;
                // - locked (confirmed / enemy turn / any non-selection phase) → NO hand fan,
                //   nothing grabbable (grabbable stays false), and the two played cards stay
                //   DOCKED read-only in their slots. The fan unbinds (empty buffer) and no
                //   card is reclaimable until the next real card-selection phase.
                bool selecting = CardsGameApi.IsSelectionPhase(hand);
                grabbable = selecting;
                // THE HAND IS ALWAYS SHOWN — the LOCK only removes the AFFORDANCE (user ruling
                // 2026-08-08, see FillHandFan). The fan used to be filled only while `selecting`,
                // which is precisely the "keine Handkarten" bug: after the player confirms, the
                // hand's currentMode STAYS CardsSelection (the stale-mode trap documented above)
                // while IsSelectionPhase goes false, so the character the GAME presents — the one
                // that was selected when the phase began — got an EMPTY fan and the empty-hand
                // placard, while every FOCUSED character kept its cards (the read-only branch above
                // never had the phase gate). Same fill for both now; `grabbable` alone still carries
                // the lock, so nothing is reclaimable outside the real selection phase.
                FillHandFan();
                _tray.SyncFromGameState(hand, _factory);
                // Tray occupants were created by the sync — hook + re-adopt them too (both
                // while selecting AND locked, so the docked played cards keep their face).
                for (int slot = 0; slot < 2; slot++)
                {
                    VRCard? occupant = _tray.Occupant(slot);
                    if (occupant == null)
                        continue;
                    HookCard(occupant);
                    if (occupant.NeedsFace && occupant.GameCard != null)
                        occupant.AttachGameCard(occupant.GameCard);
                }
                LogSelectionLock(hand, selecting);
                break;

            case CardHandMode.LoseCard:
            case CardHandMode.DiscardCard:
            case CardHandMode.RecoverDiscardedCard:
            case CardHandMode.RecoverLostCard:
            case CardHandMode.IncreaseCardLimit:
                // Modal card picks (long-rest burn, avoid-damage, discards,
                // recovers): the 2D UI is click-to-select. VR (item 10): the
                // candidates are GRABBABLE ONLY — the card must be PLACED into a
                // control-board slot to select it. Poke-to-select is deliberately
                // NOT armed here: merely touching a hand card must never commit it
                // (a fingertip within 8 mm used to fire SelectCard with no board
                // placement). The only commit path is the deliberate slot drop
                // (HandlePickRelease → TryCommitPick → CardsHandUI.SelectCard).
                //
                // ...BUT ONLY WHILE A PICK IS ACTUALLY BEING ASKED FOR (item 6b, 2026-09-06, and
                // the new STANDING RULING it enforces: "Der Hand-Fächer soll immer sichtbar sein.
                // Die einzige Ausnahme ist, wenn einem Spieler kein Charakter zugewiesen wurde").
                //
                // WHAT WAS WRONG. This branch is chosen by the game's LATCHED
                // CardsHandUI.currentMode, which stays LoseCard for minutes after a burn is over —
                // TakeDamagePanel.ResetAndHide touches no CardsHandUI at all. The candidate loop
                // below is gated on `pickOpen`, so for a dead flow it added NOTHING and the fan was
                // published EMPTY. `allowFan` (UpdatePalmGate) then read false, the palm gate
                // refused, EnsureFanCapability declined to re-arm (it is itself gated on allowFan),
                // and the empty-hand placard suppressed ITSELF on the same latched mode — so the
                // player got no cards, no fan and no explanation. That is his report word for word:
                // "Beim zweiten Schaden, bevor er die Entscheidung getroffen hat, konnte er seinen
                // Hand-Fächer nicht mehr öffnen."
                //
                // THE EVIDENCE, from the co-player's drop (remote/Player.log, ModBuild 462). Four
                // PICK GATE lines read `pick=CLOSED … openEdgeOutstanding=False` with the mode
                // still LoseCard — the flow's teardown WORKED. Twelve lines after each of them:
                // `Hand fan WITHHELD by NoCards … boundHand=yes, boundMode=LoseCard`. Fifteen of
                // that log's sixteen WITHHELD lines read boundHand=yes; exactly ONE reads
                // boundHand=NO, which is the ruling's single sanctioned exception.
                //
                // SO A DEAD PICK FALLS BACK TO THE HAND. Not grabbable — the fan mode becomes
                // Inspect, which is what every other non-placing phase already does (the 2026-08-08
                // ruling): every card can be picked up and read, and the release returns it home
                // without touching a game seam. Nothing here widens what may be COMMITTED.
                if (!CardsGameApi.PickIsOpen(hand))
                {
                    LogPickFillGate(mode, pickOpen: false, refusedPile: 0, hand);
                    _fanSourcePile = CardPileType.None;
                    FillHandFan();
                    break;
                }
                grabbable = true;
                // Field occupants stay valid only while the game still reports them
                // selected (an undo / "choose other card" returns them to the fan).
                // Task #11 free swap: while a pick-reopen (cancel → re-select) is in
                // flight the game momentarily reports EVERYTHING deselected — do not
                // prune on that transient or the still-placed card would snap to the
                // fan mid-swap; the reopen's completion re-runs this with final state.
                // ...AND A PARKED CARD IS NOT AN OCCUPANT (item 11d, second half). The
                // selection latch above is the game's, and the game does NOT clear it when the
                // character DIES: the 2026-09-05 host log follows one card the whole way.
                //   251442  Pick commit (LoseCard): 'ABILITY_CARD_PerverseEdge'
                //   252557  BURN ANIM [pile-watch]: ... flies from ... -> Burnt pile
                //   252670  BURN ANIM: 'VRCard_ABILITY_CARD_PerverseEdge' reached the Burnt pile
                //           - PARKED
                //   255544  MindthiefID takes 3 damage and is now at -2 health / ActorDead
                //           (then EndTurnLoot -> EndTurn -> EndRound -> StartRoundEffects ->
                //           PlayerExhausted -> Autosave -> SelectAbilityCardsOrLongRest, with
                //           `Rebuild: mode=LoseCard` unbroken across all seven)
                //   256759  Fly-to-pile REFUSED [pick field]: 'ABILITY_CARD_PerverseEdge' ...
                // Four thousand lines and a whole round after it was parked in the burnt stack,
                // this list still held it and RelayoutField re-seated it into the pick recess -
                // which is verbatim his "eine bereits verbrannte Karte kam wieder zurueck aus dem
                // Stapel auf das Board". IsSelected could not catch it (still true) and neither
                // could the burnt-pile test alone: the three HEALTHY burns in the same session sit
                // in the pile too, for the second or so of the documented handover to
                // TryStartBurnFly, and pruning those would break an accepted animation.
                // PARKED is the term that separates them, and it needs no new state: a card mid-
                // handover is by contract left LYING where it is (that is what "the burn path owns
                // it" means), and only a card whose flight has completed is on the pool root.
                for (int i = _fieldCards.Count - 1; i >= 0; i--)
                {
                    VRCard occupant = _fieldCards[i];
                    if (occupant == null || occupant.GameCard == null
                        || IsParked(occupant)
                        || (!_pickReopenBusy && !occupant.GameCard.IsSelected))
                    {
                        // Event-discard batching: a pruned LOCKED card shrinks the
                        // locked prefix (the step display recomputes from it).
                        if (i < _pickLockedCount)
                            _pickLockedCount--;
                        if (occupant != null)
                            _pickExitFlown.Remove(occupant);
                        _fieldCards.RemoveAt(i);
                    }
                }
                // Item 9: the SELECTABLE widgets become the fan. In CardsSelection the
                // fan is the real hand; in the burn-two-discarded flow the game marks
                // the DISCARD-pile widgets selectable (CardHandMode.LoseCard, pile
                // Discarded, count 2 — AbilityCardUI.SetMode), so the exact same fan
                // becomes the discard pile, picked exactly like hand cards through the
                // one authoritative TryCommitPick → SelectCard seam. Track the source
                // pile for the change-deduped Info line below.
                CardPileType pickSource = CardPileType.None;
                // Task #11 (b): while the game's "Karten verbrennen / Wähle eine andere
                // Karte" confirm popup is open it flips EVERY widget unselectable
                // (OnCardSelected LoseCard branch, CardsHandUI.cs:2046-2052) — which
                // used to empty the fan the moment the required cards were placed. The
                // eligible cards must stay browsable/swappable through the whole flow,
                // so while that popup is open the fan keeps the widgets of the pick
                // source pile (the game's own selectableCardTypes) that are not
                // currently selected. Grabbing one triggers the reopen seam
                // (OnCardGrabbed → game's own "choose another card"), which restores
                // real selectability before any commit can run.
                bool confirmOpen = CardsGameApi.IsPickConfirmDialogOpen(hand);
                // ITEM 11d (2026-09-05): A LATCH THE GAME LEAVES SET IS NOT A LIVE PICK.
                // `widget.IsSelectable` was the ONLY term here, and it survives the flow: when the
                // damage decision closes, TakeDamagePanel re-Shows the hand with
                // `selectableCardType = Any, maxCardsSelected = 0` (TakeDamagePanel.cs:520) and
                // AbilityCardUI.SetMode's `Contains(Any)` arm (AbilityCardUI.cs:905) turns EVERY
                // widget in the hand selectable - the ones in the LOST pile included. The fill then
                // re-adopted burnt cards onto the board and the banner kept asking for a burn while
                // it was no longer the player's turn. The hardware log names both halves:
                //   251607  Pick fan source (LoseCard): burnt pile
                //   256719  Pick banner: "Testo: Waehle 2 Karte(n) zum Verlieren - 1/2 gewaehlt"
                // The remedy is the game's own count - it says 0 exactly when nothing is being
                // asked for - plus a per-mode source-pile test, because a pick that IS open with
                // `Any` still has no business offering a card that is already lost.
                bool pickOpen = CardsGameApi.PickIsOpen(hand);
                int refusedPile = 0;
                for (int i = 0; pickOpen && i < _widgetBuffer.Count; i++)
                {
                    AbilityCardUI widget = _widgetBuffer[i];
                    if (widget.AbilityCard == null || widget.IsLongRest)
                        continue;
                    if (!CardsGameApi.PickPileIsLegalFor(mode, widget.CardType))
                    {
                        refusedPile++;
                        continue;
                    }
                    bool eligible = widget.IsSelectable
                        || (confirmOpen && !widget.IsSelected && CardsGameApi.IsPickEligible(hand, widget));
                    if (!eligible)
                        continue;
                    if (pickSource == CardPileType.None)
                        pickSource = widget.CardType;
                    VRCard card = AdoptedCard(widget);
                    if (!_fieldCards.Contains(card))
                        _fanBuffer.Add(card);
                }
                LogPickFillGate(mode, pickOpen, refusedPile, hand);
                LogPickSource(mode, pickSource);
                // THE SAME VALUE THE LINE ABOVE PRINTS, PUBLISHED RATHER THAN RE-DERIVED. A peer
                // mirroring this fan has no way to compute it (see _fanSourcePile) and every other
                // way of guessing it — matching lengths against the candidate piles — can be
                // confidently wrong, which is the one failure a card face must not have. Read by
                // Net.LocalRigSampler.SampleFanSource for extension record 43.
                _fanSourcePile = pickSource;
                RelayoutField();
                break;

            case CardHandMode.ActionSelection:
                // Task #5 (clear the control board after the own turn): CardsHandUI.currentMode
                // STAYS ActionSelection after the player's own turn ends (it is only re-driven
                // by the next CardsHandManager.Show, which never runs during an enemy turn), so
                // this case is still hit while an ENEMY is up — and the two played cards would
                // otherwise stay docked on the board. Dock them ONLY while it is genuinely THIS
                // character's own action turn (CardsGameApi.IsActionTurn == Choreographer.
                // CurrentActor is this hand's locally-controlled player). The moment an enemy (or
                // any other actor) becomes current, halfVisible stays false and _halfBuffer stays
                // empty, so the zone loop below PARKS the played cards — the board is cleared.
                // When the character's own turn comes round again the cards re-dock. This is
                // pure VR presentation: the game's own 2D round-card state is untouched, and the
                // two-character sequential turns each show their own actor's cards (CurrentActor
                // is that actor during its turn). Long rest is unaffected: the long-rester's own
                // turn keeps CurrentActor == its player (IsActionTurn true) with an empty round
                // pile, exactly as before.
                // ONE SEAM for "may the board's card slots show this hand's chosen cards":
                // CharacterFocus.RoundCardDock. With no focus override it asks whether this hand's
                // locally-controlled character owns the CURRENT TURN — deliberately the turn and
                // NOT Choreographer.CurrentActor, because a card that hands the action to a SUMMON
                // or a commanded ally re-points the acting figure mid-turn while the turn itself
                // never moves (GameState.OverrideCurrentActorForOneAction, GameState.cs:3478-3480,
                // leaves GameState.s_TurnActor alone). Keying on the figure emptied the board for
                // the whole summon action and for every move/attack confirmation driven for another
                // figure — the two reports RoundCardDock's doc quotes. Routing both the vanilla and
                // the focus answer through that one method keeps them from drifting apart.
                bool actionTurn = Board.CharacterFocus.RoundCardDock(hand, out _);
                if (actionTurn)
                {
                    halfVisible = true;
                    CollectRoundCards(hand, _halfBuffer);
                }
                // The remaining HAND is shown here too (user ruling 2026-08-08: the cards must be
                // visible "egal in welcher Phase"). AFTER the round-card collection above, so a card
                // that is lying in a board slot is never also a fan card. `grabbable` stays false:
                // during an action turn the hand is a picture, exactly as in a focus view.
                FillHandFan();
                LogActionTurnLock(hand, actionTurn);
                break;

            default:
                // The remaining modes the game can park a hand in — CardHandMode.DeckSelection and
                // CardHandMode.Preview (CardHandMode.cs) — are NON-PICK modes, so the fan is the
                // hand fan and the hand is shown, read-only (grabbable is false here). This branch
                // is what makes the rule ABSOLUTE rather than "in the phases we happened to list":
                // a hand parked in any mode the mod does not name still shows its cards, so the
                // empty-hand placard can never be the answer to "the mod has no case for this".
                FillHandFan();
                break;
        }

        // THE LAST WORD ON WHAT THE FAN SHOWS IS THE RULES MODEL — for EVERY fill above, not just
        // the one that happens to carry a belt. Runs here, before the park sweep, so a vetoed card
        // is pooled with the rest of the off-board cards instead of being left lying wherever it
        // was. See DropFanCardsTheModelMoved for the report this closes.
        DropFanCardsTheModelMoved(hand, mode, readOnly);

        if (mode != CardHandMode.CardsSelection)
            _tray.ClearSlots(); // stale occupancy must not pin cards outside CardsSelection

        // Drop field (test #21 B): exists ONLY during a pick mode (C); leaving the
        // mode clears its occupants — the zone loop below parks them.
        // READ-ONLY FOCUS: a pick flow belongs to the character the GAME presents, never to one we
        // are merely watching — the watched character's stale CardsHandUI.currentMode could still
        // read LoseCard from its own last decision. Forcing `pick` false keeps the drop field, the
        // pick-confirm mirror and the short-rest overlay out of a focus view entirely.
        // ITEM 6b: THE ARMED DROP TARGET DISARMS WITH THE FLOW — and it is deliberately NOT the
        // same term as the drop field's LIFETIME. Two different questions were sharing one name:
        //
        //   `pick`  — "is a pick being asked for RIGHT NOW". Drives everything the board OFFERS:
        //             the tray's pick state (SetPickActive), the short-rest guard, and through
        //             CardsGameApi.PickFlowLive the wanted-slot glow, the drop telegraph, the
        //             release routing and the mirrored PickFieldSeat record. This one has to key
        //             off the game's own completion, because the LATCHED CardsHandUI.currentMode
        //             it used to read stays LoseCard for minutes after a burn — the 2026-09-05
        //             host log runs `Rebuild: mode=LoseCard` unbroken across a player's DEATH and
        //             into the next round.
        //   `pickModeSeats` — "may the drop field still hold cards". A card sitting in the recess
        //             at the commit is the card being BURNED, and its handover to TryStartBurnFly
        //             is timed by the existing field prune (the IsParked / !IsSelected terms that
        //             ModBuild 4b89d912 added). Clearing the list at the commit edge would launch
        //             that flight ~1 s early, while the game's own AnimateCardsLost is still
        //             playing on the adopted face — a burn FX regression traded for a symptom
        //             nobody reported. So the seats keep the mode term they always had, and the
        //             BURN FLOW ARM line reports `inheritedTargetArmed` so a field that ever
        //             FAILS to drain before the next damage event is one grep away.
        bool pick = !readOnly && PickFlowLive(hand);
        bool pickModeSeats = !readOnly && IsPickMode(mode);

        // Short-rest sacrifice overlay (test #25, item 1d; test #28 seating): while the
        // game presents the randomly lost card's burn/redraw choice (ShortRestedCard !=
        // null; the choice itself is the docked DialogPopup), lay that card physically
        // in the LEFT slot recess (Slot1) — the same left-slot home the avoid-damage
        // burn uses — DISPLAY-ONLY. Guarded off during the pick modes (which own the
        // slots) so the two flows can never collide, and off when the tray is hidden.
        // The short-rest random path stays in CardHandMode.CardsSelection, so this
        // simply overlays the unchanged hand fan. Rebuild is the sole executor;
        // PollShortRest keeps it live on redraw.
        CAbilityCard? shortRested = pick || readOnly ? null : CardsGameApi.ShortRestedCard(hand);
        bool shortRest = shortRested != null && trayVisible;

        // Slots stay physically visible (fixed asset, test #28) — no field to toggle;
        // the driver only marks pick flows so CONFIRM mirrors the mode's confirm.
        _tray.SetPickActive(pick && trayVisible);
        if (shortRest)
            PresentShortRestCard(hand, shortRested!);
        else
            RemoveShortRestCard();

        if (!pickModeSeats)
        {
            _fieldCards.Clear();
            _pickLockedCount = 0;     // event-discard batching never survives the mode
            _pickExitFlown.Clear();   // …nor do the exit-flight claims it made
            _pickReturnFlight.Clear();// …nor a restart return flight the pick no longer owns
            _loggedPickSource = null; // re-entering a pick mode logs its source afresh (item 9)
        }

        // ACTIVE CARDS area (feature 6): refresh the permanently-shown active-card column
        // — BEFORE the zone flags below so its cards are marked in-zone and kept out of the
        // park sweep (like the browse arc), and their active-half highlight is set here.
        //
        // ORDER (fan-close sweep fix): this runs BEFORE UpdateBrowser on purpose. The browse
        // arc must only ever borrow card visuals the board is NOT already showing
        // (BoardOwnsCardVisual), and the ACTIVE column is one of those board zones — so its
        // membership has to be current for THIS rebuild before the browser filters against it.
        // Every other board zone (_halfBuffer, tray slots, _fieldCards, _shortRestCard) is
        // already resolved above.
        UpdateActive(hand);

        // Pile browse (test #21): refresh content or close — BEFORE the zone flags
        // below so freshly closed browse cards park in this same pass.
        UpdateBrowser(hand, mode);

        // Configure cards per zone; everything else parks invisibly.
        for (int i = 0; i < _factory.All.Count; i++)
        {
            VRCard card = _factory.All[i];
            if (card == null || card.IsHeld)
                continue;
            // Issue 5: a card mid-flight into a pile owns its own transform until it arrives —
            // never re-zone, re-home or re-park it (that would teleport it out of the animation).
            // Issue 2: a card mid-disappear likewise owns its transform until it parks itself — the
            // park sweep must not re-park it (double-hide) while it shrinks out.
            if (card.IsFlying || card.IsVanishing)
                continue;
            // Same rule, one more owner (character-swap exchange, 2026-08-09): a card the hand fan
            // is currently flying OUT of itself owns its transform until it lands. Parking it here
            // would teleport it into the pool mid-wipe — the exact pop the exchange exists to
            // remove — and re-stamping its zone verdicts would fight the "makes no promises" flag
            // CardFan.BeginSwapOut wrote. The fan hands each one back the moment its own flight ends
            // (TryTakeLandedOutgoing → DrainSwapExit), and THAT is where it gets parked.
            if (_fan.IsLeaving(card))
                continue;
            // Issue 1: remember every visible card's true world pose so a later damage-burn can fly
            // its slab from where the card ACTUALLY was, never from a teleported pile position.
            // BURN ANIM: the predicate is "NOT parked" rather than "active in hierarchy" on purpose.
            // A card in a CLOSED hand fan is inactive (the fan root is disabled) yet its transform
            // still carries its true world pose at the player's hand — that is exactly where a card
            // burned straight out of the hand must fly FROM. Only a pooled/parked card has a
            // meaningless pose (Park re-homes it to the pool root at the origin), and that is the
            // one case we must not record.
            if (card.GameCard != null && !IsParked(card))
            {
                _lastCardWorldPos[card.GameCard] = card.transform.position;
                _lastCardWorldRot[card.GameCard] = card.transform.rotation;
            }
            bool inFan = _fanBuffer.Contains(card);
            bool inHalf = _halfBuffer.Contains(card);
            bool inTray = _tray.SlotOf(card) >= 0;
            bool inBrowse = _browser.Contains(card);
            bool inField = _fieldCards.Contains(card);
            // EVENT-DISCARD EXIT: a card whose page-turn flight has already run STAYS in
            // _fieldCards — the page maths (PickSeatOfIndex / PickTargetSlot / the prune) is
            // indexed on that list and must not shift under it — but it is bookkeeping-only from
            // here on: it is parked on the (inactive) pool root, and a stale Grabbable there would
            // put an invisible card at the pool origin into the laser/poke sweep, which scores
            // purely on CanGrab (VRCard.cs:393, FanSweep.Score's SweepEligible) and has NO active
            // check of its own. So the ZONE keeps it out of the park sweep and the AFFORDANCE does
            // not follow.
            bool fieldAffordance = inField && !_pickExitFlown.Contains(card);
            bool inActive = _active.Contains(card); // feature 6: shown in the active-cards column
            // Short-rest sacrifice card: its own zone. Never in any of the above, so
            // its Grabbable/PokeSelect resolve to false here (display-only) — we only
            // keep it OUT of the park sweep so PresentShortRestCard's centre home holds.
            bool inShortRest = ReferenceEquals(card, _shortRestCard);

            // PILE-ORIGIN MARKER RETIRE (discard-in-hand-fan bug 2026-08-04): a non-held,
            // non-flying card the browse arc no longer lists has been re-homed by THIS rebuild
            // from game truth (hand fan, pick fan - where the whole fan legitimately IS the
            // discard/burnt set -, tray, half, field, active, short rest) or is about to park.
            // Either way its browse loan is over, so the marker must not outlive it: a stale
            // marker would hijack the card's next release into the pile routing. Held cards were
            // skipped above - their marker survives the hold, which is the entire point (the
            // browser's own list does NOT survive a mid-hold close; see VRCard.PileOrigin).
            if (!inBrowse)
                card.PileOrigin = null;

            // Task #2 follow-up: the slot-dock grab apron (under/around-grab accept,
            // VRCard.SetDockGrabPad) is TRUE exactly for slot-docked cards — tray
            // occupants and pick/field cards in the recesses — and FALSE everywhere
            // else. Central re-assert every Rebuild (idempotent) so no dock/undock
            // path can leave a stale apron on a fan/browse/parked card.
            card.SetDockGrabPad(inTray || fieldAffordance);

            // Item 10: poke-select is never armed on hand cards — touching a card
            // must not auto-select it; a card is committed only by placing it into a
            // board slot. Kept as an explicit reset so a previously pokeable card is
            // disarmed on rebuild.
            card.PokeSelectEnabled = false;
            // Field occupants stay grabbable: plucking one back off the field and
            // releasing it elsewhere unselects through the game's own seam. Browse
            // cards are grabbable too (item 5) — but purely to pull one close and
            // read it; the release routes back to the arc, never to a game seam.
            // Active cards (feature 6) are grabbable for the same read-only reason:
            // pluck one to read it, release returns it to the column, never a game seam.
            // READ-ONLY FOCUS (structural, not a convention): while the mod presents a character
            // the player may not drive, NOTHING it built is grabbable. Together with the
            // unconditional PokeSelectEnabled = false above and CardFan's Picture-mode raycast
            // veto, there is no interactor left that can even FIND one of these cards — so there is
            // no path from a VR input to a game call for a character we are only looking at. This
            // is the single funnel every card in every zone passes through on every rebuild, so it
            // cannot be bypassed by a card that changed zone between frames.
            //
            // COMMIT vs INSPECT (user ruling 2026-08-08: "Ich möchte das man jederzeit auch eine
            // Karte aus der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die Karte
            // nirgendwo ablegen kann. Das soll also niemals blockiert sein"). `commitGrab` is the
            // OLD verdict, byte for byte — "this card may be picked up AND placed", which is why
            // every phase that refused the PLACEMENT also refused the LOOK. `inspectGrab` is the
            // new, additive one: a HAND-FAN card of a hand this client is entitled to handle
            // (Board.CharacterFocus.HandInspectable) may ALWAYS be picked up, in every phase and
            // every mode, with `InspectOnly` stamped so the release routes it straight back home
            // without touching a game seam. Nothing else in the pipeline is widened: the tray, the
            // pick field, the browse arc, the active column and the round-card dock all keep the
            // exact verdicts they had.
            //
            // THE PILE ARC AND THE ACTIVE COLUMN JOIN THE INSPECT SIDE (user ruling 2026-08-08:
            // "Die Verbrannt-Piles und Abgeworfen-Piles sollen jederzeit … öffenbar sein und die
            // Karten sollen auch nehmbar sein um sie anzugucken, das darf nicht blockieren"). The
            // `!readOnly` in `commitGrab` used to make every card of a browse arc / active column
            // inert the moment the player was LOOKING at a character rather than driving it — which
            // is exactly when a pile gets opened, so "pick a discarded card up and read it" was
            // dead in the only view it matters in. These cards were never committable in the first
            // place (OnCardReleased's browse / active branches return them home BEFORE any game
            // hand is resolved, and the PileOrigin fallback catches one still held when the arc
            // closes), so this widens the GRAB and nothing else — the same split the hand fan got.
            bool commitGrab = !readOnly
                              && ((inFan && grabbable) || (inTray && grabbable)
                                  || fieldAffordance || inBrowse || inActive);
            bool inspectGrab = (inFan || inBrowse || inActive) && !commitGrab && handInspectable;
            card.Grabbable = commitGrab || inspectGrab;
            card.InspectOnly = inspectGrab;
            // BOTH HANDS ON EVERY CARD (user ruling 2026-08-04: "Alle Karten sollen allgemein auch
            // mit der nicht-dominanten Hand aufgenommen werden koennen ... Das soll fuer alle
            // Karten gelten - ausser den Faecherkarten selber"). This is the GENERAL rule that
            // replaces the per-zone whittling of the last two builds (browse arc exempted
            // 2026-08-03, pick-field cards 2026-08-04): the fan-owning-hand veto
            // (VRCard.InteractionBlockedHand) exists for ONE geometric reason — the ability fan
            // hangs off the gate hand's own palm, so that hand's hover sits permanently inside the
            // fan — and therefore applies to the fan's OWN cards and nothing else. Every other
            // zone (browse arc, pick field, tray slots, active column, half selection) is
            // hover/highlight/grabbable with BOTH hands. Re-asserted every rebuild, so a recycled
            // widget can never carry a stale verdict into a new role; the two between-rebuild
            // seams (fan pluck -> true, fan re-entry -> false) are stamped at CardsDriver.
            // OnCardGrabbed and CardFan.Add/SetCards respectively (see VRCard.AllowsGateHand).
            card.AllowsGateHand = !inFan;
            // Fan strips only apply while the card is in a fan that TILES its colliders. That is
            // now the browse arc as well as the hand fan (PileBrowser.Relayout strips exactly like
            // CardFan.Relayout — the sweep-skips-cards fix), so a browse card must keep its strip
            // through a rebuild; every other pool still gets the full collider back here.
            if (!inFan && !inBrowse)
                card.ResetColliderRegion();
            if (!inActive)
                ClearActiveHighlight(card); // clear any stale active-region highlight on reused cards

            if (!inFan && !inHalf && !inTray && !inBrowse && !inField && !inShortRest && !inActive)
            {
                // Issue 5: a just-cleared PLAYED round card flies into its destination pile
                // (burned → burnt, discarded → discard) instead of vanishing; everything else
                // parks instantly as before. TryStartFlyToPile returns true only when it launched
                // the animation — the fly's completion callback parks the card on arrival.
                if (TryStartFlyToPile(hand, card))
                {
                    // fly-to-pile owns this card — never also vanish it (no double animation).
                }
                // BURN ANIM (user: "a burned card must SLIDE into the burnt pile instead of just
                // disappearing"): a card that leaves every zone in the SAME frame the game moved it
                // into the character's LOST pile was just BURNED — the long-rest chosen burn and the
                // take-damage "burn a card" pick both land here, because their pick card sits in a
                // board slot / on the field until the commit and is then simply un-zoned. Rebuild
                // runs BEFORE TickBurnToPile in the same Update, so without this the card was
                // already parked (pose gone) by the time the burn watcher noticed it — which is
                // exactly the "card just vanishes" the user reported. Flying it HERE, while the real
                // VR card is still live at its true position, reuses the very same FlyToPile arc as
                // the discard flow (and carries the game's own burn FX, which plays on the adopted
                // face). The claim in TryStartBurnFly keeps TickBurnToPile from animating it twice.
                else if (TryStartBurnFly(hand, card,
                             _lastTrayCards.Contains(card) ? "play slot"
                             : _lastVisibleCards.Contains(card) ? "hand fan"
                             : "board field / pick slot"))
                {
                    // burn fly owns this card — flying, OR lying in place while its burn artwork
                    // completes (the bounded artwork hold). Either way: never park/vanish it here,
                    // that would be exactly the reported "card vanishes after the burn animation".
                }
                // Issue 2/1: a docked card that CLEARS on a character/turn switch — a played ACTION
                // card (in last rebuild's half set) OR a SELECTED card lying in a board SLOT (in last
                // rebuild's tray set) — but is NOT flying to a pile used to POP away. Now it shrinks +
                // fades out IN PLACE, then parks. Everything else (fan close, browse/active, pool
                // return) keeps its own instant park / own animation. Suppressed on the first rebuild
                // after a board build so the scenario-load population never storms.
                else if (!_dockAnimSuppressed && (_lastHalfCards.Contains(card) || _lastTrayCards.Contains(card))
                         && card.gameObject.activeInHierarchy)
                {
                    VRCard vanishing = card;
                    bool wasSlot = _lastTrayCards.Contains(card);
                    card.Vanish(() => _factory.Park(vanishing));
                    VRLog.Info("Cards", $"Card disappear: '{card.name}' ({(wasSlot ? "selected slot" : "docked action")} " +
                                        $"card cleared on a character/turn switch) — scale-down + fade " +
                                        $"({VRCard.DockVanishSeconds:F2}s) in place, then parked (not flying to a pile).");
                }
                else
                {
                    _factory.Park(card);
                }
            }
        }

        // Hand reorder (Stage A): apply the persisted VR order to the real hand fan only —
        // overrides the game's SortCards. Pick-mode "fans" (discard/burnt piles) are transient
        // and keep game order.
        if (mode == CardHandMode.CardsSelection)
            ReorderFanBuffer();
        // FAN MODE: told BEFORE its content, so the very first frame of a restricted view already
        // carries the right affordance (a fan that learned its mode one frame late would offer the
        // wrong one for exactly that frame).
        //
        // THREE STATES, and the middle one is the 2026-08-08 ruling ("Ich möchte das man jederzeit
        // auch eine Karte aus der Hand nehmen kann um sie sich genau anzuschauen, auch wenn man die
        // Karte nirgendwo ablegen kann"):
        //  • INTERACTIVE — `grabbable` and not a focus override: the real card-selection window and
        //    the modal pick flows. Grab, laser-pluck, reorder, DROP into a slot. Unchanged.
        //  • INSPECT — the placement is refused (selection locked / confirmed, an action turn, any
        //    other phase, or a focus view of one of our OWN characters) but the hand is one this
        //    client is entitled to handle. The cards are fully grabbable and laser-pluckable and
        //    the release returns them HOME; no game seam is reachable (VRCard.InspectOnly).
        //    THIS IS WHAT USED TO BE A PICTURE, and that was the reported bug.
        //  • PICTURE — a hand whose FRONTS may not be drawn at all: the secret selection window,
        //    for a character this client does not control. Inert end to end.
        //    IT IS NO LONGER "every foreign hand". User ruling 2026-09-07, given when he was asked
        //    to choose: a foreign character one has focused is shown AND handleable, because a card
        //    that may be read may be held up to the face. The gate moved from OWNER to FRONT and
        //    lives in Board.CharacterFocus.HandInspectable; the comment that used to stand here said
        //    "that one stays refused" and would have been the eighteenth false assertion this
        //    project has found in its own source.
        CardFan.FanMode fanMode =
            !readOnly && grabbable ? CardFan.FanMode.Interactive
            : handInspectable ? CardFan.FanMode.Inspect
            : CardFan.FanMode.Picture;
        LogFanMode(hand, fanMode, readOnly, grabbable, mode);
        _fan.SetMode(fanMode);
        // handSwap: not "the list changed" but "this is the OTHER character's hand" — CardFan turns
        // that into the exchange wipe instead of a plain relayout. See the swap-edge block above.
        _fan.SetCards(_fanBuffer, handSwap);
        // PICK RESTART: the pages that already flew into the discard stack arc back OUT of it.
        // Runs immediately after SetCards for the same reason the docked-card APPEAR pass below
        // does: FlyFromPile flies to the card's HOME, and the fan has just asserted it.
        DrainPickReturnFlight();
        // ORDER PROOF for the scenario hand. Nothing above sorts it and nothing needs to: the
        // widget list this was filled from (CardsGameApi.GetCards -> CardsHandUI.cardsUI) is kept
        // in the game's own hand order, whose final term IS the initiative — and ReorderFanBuffer
        // then applies the PLAYER's own drag-reorder on top, which is a deliberate override and
        // will legitimately read NOT SORTED. That is the one case where a failing verdict here is
        // correct, and it is why this reports rather than corrects.
        LogFanOrder("scenario hand", $"a hand rebuild (mode={mode}"
            + (handSwap ? ", character exchange" : "") + ")");
        _tray.SetVisible(trayVisible);
        // READ-ONLY FOCUS, slot cards: told BEFORE the content for the same reason as the fan. The
        // round-card dock is the ONE zone that arms real input on its cards — fingertip HalfZone
        // volumes plus the card's own uGUI canvas registered with UguiPokeSurfaces (which is what
        // makes the laser able to click a card half at all). A read-only dock arms neither, so a
        // watched character's played cards are a picture: no poke, no laser, no PlayHalf. Showing
        // the cards did not open an input path.
        _half.SetReadOnly(readOnly);
        _half.SetVisible(halfVisible);
        if (halfVisible)
            _half.SetCards(_halfBuffer);

        // Issue 2 APPEAR: a docked action card that just (re-)appeared for the new/active character
        // — now in the half set but NOT in it last rebuild — scales + fades in instead of popping
        // from nothing. Runs AFTER _half.SetCards so each card's home pose is already asserted (the
        // appear grows toward it). Suppressed on the first rebuild after a board build (no storm).
        // A card flying to a pile is never docked here, so the two never collide (no double anim).
        if (halfVisible && !_dockAnimSuppressed)
        {
            for (int i = 0; i < _halfBuffer.Count; i++)
            {
                VRCard card = _halfBuffer[i];
                if (card == null || card.IsHeld || card.IsFlying || _lastHalfCards.Contains(card))
                    continue;
                card.PlayAppear();
                VRLog.Info("Cards", $"Card appear: '{card.name}' (docked action card shown for the new/active " +
                                    $"character) — scale-in + fade ({VRCard.DockAppearSeconds:F2}s) instead of a pop.");
            }
        }

        // Issue 1 APPEAR (selected slot cards): a card that lands in a board SLOT because we switched
        // TO a character who ALREADY had cards lying there — now a slot occupant, but NOT visible in
        // any zone last rebuild (it was parked while the other character was active) — scales + fades
        // IN, in place at its slot home (PlaceCard already asserted it), instead of flying up from
        // below. A card that WAS visible last rebuild (the player just dropped it in from the fan)
        // stays out of this so its release glide is preserved. Suppressed on the first board rebuild.
        if (!_dockAnimSuppressed)
        {
            for (int s = 0; s < 2; s++)
            {
                VRCard? card = _tray.Occupant(s);
                if (card == null || card.IsHeld || card.IsFlying || card.IsVanishing
                    || _lastVisibleCards.Contains(card))
                    continue;
                card.PlayAppear();
                VRLog.Info("Cards", $"Card appear: '{card.name}' (selected card shown in slot {s + 1} for a " +
                                    $"character who already had cards lying there) — scale-in + fade " +
                                    $"({VRCard.DockAppearSeconds:F2}s) in place instead of a fly-from-below.");
            }
        }

        // Issue 2 "no storm" guard: the first Rebuild after a board build / teardown populated the
        // board silently; every later change (the real character/turn switches) now animates.
        _dockAnimSuppressed = false;

        VRLog.Debug("Cards", $"Rebuild: mode={mode} fan={_fanBuffer.Count} tray={trayVisible} half={_halfBuffer.Count}.");
    }

    /// <summary>
    /// FOREIGN-FACE ADOPTION LEDGER for the character-focus feature, and the hand-back that goes
    /// with it.
    ///
    /// <para>WHY THIS EXISTS. A read-only focus view renders ANOTHER character's real
    /// <c>AbilityCardUI</c> widgets: <c>AdoptedCard</c> → <c>VRCard.AttachGameCard</c> →
    /// <c>CardFace.Adopt</c> REPARENTS the live <c>fullAbilityCard</c> into a VR card and records
    /// its original parent/sibling/anchors/pose for a full restore. That is the same path the local
    /// fan has always used across a multi-merc hand switch, and the clone-based readers
    /// (<c>RemoteAbilityCardSource</c> / <c>RemoteCardArt</c>, which <c>Instantiate</c> and then
    /// explicitly reset the clone's rotation and scale) are provably unaffected by it. But it is
    /// still the part of this feature with the least hardware evidence, so:</para>
    /// <list type="bullet">
    /// <item>the faces are handed back the moment the focus LEAVES a character —
    ///   <c>VRCardFactory.ReleaseHand</c> restores every one of that hand's widgets — rather than
    ///   left adopted until the scenario ends;</item>
    /// <item>and each switch logs WHAT was adopted and WHETHER the restore landed: the character,
    ///   how many widgets the mod still held, and how many faces are still parented under a VR
    ///   card afterwards. A healthy switch reads "restored N/N, 0 still held". Anything else names
    ///   the character and the count, which is what a log has to do for a screenshot report.</item>
    /// </list>
    /// </summary>
    private void ReleaseStaleFocusHand(CardsHandUI? nowHand, bool deferForSwap = false)
    {
        CardsHandUI? nowFocusHand = Board.CharacterFocus.ReadOnlyView ? nowHand : null;

        // COMING STRAIGHT BACK (the A → B → A scrub, which the initiative row invites): a hand that
        // is queued for restore and is ALSO the hand we are now adopting must be un-queued, or the
        // drain would hand its faces back and destroy its VR cards moments after we re-adopted
        // them. Checked before the identity early-out because the queue outlives a single rebuild.
        if (nowFocusHand != null)
            _pendingFaceRestore.Remove(nowFocusHand);

        if (ReferenceEquals(_focusAdoptedHand, nowFocusHand))
            return;

        CardsHandUI? was = _focusAdoptedHand;
        _focusAdoptedHand = nowFocusHand;

        // A CHARACTER SWITCH ENDS AN IN-FLIGHT BURN HOLD — DETERMINISTICALLY, HERE (user
        // 2026-09-04). The hold's contract is "the burn path OWNS this card, leave it lying exactly
        // where it is", so anything that changes the board's subject WITHOUT landing the flight
        // strands a burned card. TickBurnToPile has a flush for exactly that, but it is keyed on
        // `hand != _burnWatchHand` and its `hand` is CurrentHand() (CardsDriver.2.Update.cs) — the
        // GAME's hand. A mod-side focus switch does not change the game's hand, so that branch is
        // not merely late for a focus switch, it is UNREACHABLE: the hardware log of the report has
        // no `BURN ANIM: FLUSHING` line anywhere, and the hold ran its full 3 s deadline straight
        // through a second take-damage decision. This is the focus edge, it is an EDGE (the
        // identity early-out above guarantees it) rather than a tick noticing a changed reference,
        // and it runs before the rebuild adopts anything — so the launch sites still see the world
        // the hold was taken in. A no-op when nothing is held.
        FlushBurnHolds(nowFocusHand != null
            ? $"the board switched to presenting '{Board.CharacterFocus.Describe(nowFocusHand.PlayerActor)}'"
            : "the board stopped presenting a focused character and followed the game again");

        if (was != null)
        {
            if (deferForSwap)
            {
                // THE SWAP EXCHANGE OWNS THESE CARDS FOR THE NEXT ~HALF SECOND. Restoring now would
                // strip the very faces the outgoing wave is flying away with (CardFace.Restore
                // reparents the live widget back into the game's UI) and then destroy the VR cards
                // mid-flight. Queued instead; DrainSwapExit hands them back as soon as the fan
                // reports no card is still leaving, and the deadline below is the backstop.
                if (!_pendingFaceRestore.Contains(was))
                    _pendingFaceRestore.Add(was);
                // The deadline only has to bound the OUTGOING wave — that is the half whose cards
                // still need their faces — so both counts are the outgoing hand's. Read off
                // LeavingCount, not Count: BeginSwapOut ran a moment ago and has already moved that
                // hand out of the fan's own list. It is a backstop anyway; the real gate is "no card
                // is still leaving".
                _faceRestoreDeadline = Time.unscaledTime
                    + CardFan.SwapTotalSeconds(_fan.LeavingCount, _fan.LeavingCount)
                    + FaceRestoreDeadlineSlack;
                VRLog.Info("Cards", $"[Focus] RESTORE DEFERRED for '{Board.CharacterFocus.Describe(was.PlayerActor)}': " +
                                    $"{CountFocusAdopted(was)} adopted card face(s) stay borrowed while that hand " +
                                    "flies out of the fan (character-swap exchange). Handing them back now would " +
                                    "make the leaving cards faceless mid-animation and delete them under it. The " +
                                    "matching [Focus] RESTORED line follows when the wave has landed.");
            }
            else
            {
                RestoreFocusHandNow(was);
            }
        }

        if (nowFocusHand != null)
        {
            VRLog.Info("Cards", $"[Focus] ADOPTING '{Board.CharacterFocus.Describe(nowFocusHand.PlayerActor)}': " +
                                $"{CountHandWidgets(nowFocusHand)} live card widget(s) available on this " +
                                "client (the game builds a populated CardsHandUI per player actor on every " +
                                "client — Choreographer.cs:925/1112). Their faces are BORROWED, never " +
                                "copied and never mutated; the matching [Focus] RESTORED line reports the " +
                                "hand-back.");
        }
    }

    /// <summary>
    /// Hand ONE character's borrowed card faces back to the game and say whether the restore landed
    /// — the body <see cref="ReleaseStaleFocusHand"/> used to run inline, now also reachable from
    /// <see cref="DrainSwapExit"/> once a deferred exchange has finished. Unchanged in what it does;
    /// only WHEN it runs moved.
    /// </summary>
    private void RestoreFocusHandNow(CardsHandUI was)
    {
        string wasName = Board.CharacterFocus.Describe(was.PlayerActor);
        int held = CountFocusAdopted(was);
        _factory.ReleaseHand(was);
        int stillHeld = CountFocusAdopted(was);
        int stillParented = CountFacesUnderVrCards(was);
        VRLog.Info("Cards", $"[Focus] RESTORED '{wasName}': handed {held} adopted card face(s) back " +
                            $"to the game (VR cards still held afterwards: {stillHeld}; faces still " +
                            $"parented under a VR card: {stillParented} — both MUST be 0). " +
                            "CardFace.Restore replays the recorded parent, sibling index, anchors, " +
                            "pose and active flag, so the character's 2D hand is the object it was " +
                            "before we borrowed it.");
    }

    /// <summary>
    /// PER-FRAME TAIL OF THE CHARACTER-SWAP EXCHANGE (user 2026-08-09). Two jobs, both of which have
    /// to happen outside <c>Rebuild</c> because the exchange outlives the frame that started it:
    ///
    /// <list type="number">
    /// <item>PARK each outgoing card the moment ITS OWN flight ends. The fan hands them back one by
    ///   one rather than all at the end, so a card that has reached the gather point is disposed of
    ///   while it is invisible instead of sitting there shrunk until the last one arrives. This is
    ///   also the ONLY place a card leaves the fan's outgoing list, which is what makes "no card is
    ///   stuck and none is duplicated" checkable rather than hoped for.</item>
    /// <item>RESTORE the deferred focus hands' borrowed faces, once NO card is still leaving (any
    ///   generation — scrubbing the initiative row can queue several) or the deadline bites. Both
    ///   conditions are needed: the fan's own land-everything paths (close / re-open / destroy)
    ///   satisfy the first immediately, and the deadline covers anything that satisfies neither.</item>
    /// </list>
    ///
    /// Allocation-free: one reused buffer, no closures, and the whole method is a no-op (two field
    /// reads) on every frame in which nothing is being exchanged — which is nearly all of them.
    /// </summary>
    private void DrainSwapExit()
    {
        if (_fan.HasLeavingCards)
        {
            _swapLanded.Clear();
            _fan.TryTakeLandedOutgoing(_swapLanded);
            for (int i = 0; i < _swapLanded.Count; i++)
            {
                VRCard card = _swapLanded[i];
                // A card the player GRABBED out of the leaving wave is theirs now — parking it
                // would yank it out of their hand. The standing rule: while a card IsHeld, write
                // nothing a hold depends on. It re-enters the fan through the normal release path.
                if (card == null || card.IsHeld)
                    continue;
                _factory.Park(card);
            }
        }

        if (_pendingFaceRestore.Count == 0)
            return;
        bool waveBusy = _fan.HasLeavingCards;
        if (waveBusy && Time.unscaledTime < _faceRestoreDeadline)
            return;
        if (waveBusy)
        {
            // ONE line per expiry, not one per queued hand — scrubbing the initiative row queues
            // several, and the same sentence four times reads as four events.
            VRLog.Warn("Cards", "[Focus] the character-swap exchange had not drained when the deferred " +
                                "face restore's deadline expired — the faces go back now. A borrowed hand " +
                                "must never outlive its animation; if this line appears, the fan's outgoing " +
                                "wave is not landing (CardFan.TryTakeLandedOutgoing). Cards still in flight " +
                                "are held back one at a time by FocusHandCardStillInPlay, so nothing " +
                                "visible is destroyed even here.");
        }

        for (int i = _pendingFaceRestore.Count - 1; i >= 0; i--)
        {
            CardsHandUI hand = _pendingFaceRestore[i];
            if (hand == null)
            {
                _pendingFaceRestore.RemoveAt(i);
                continue;
            }
            // THE RESTORE DESTROYS THIS HAND'S VR CARDS (VRCardFactory.ReleaseWidget), so it may
            // only run once none of them is somewhere the player would SEE one disappear. Two such
            // places, and the first is the one the brief calls out by name:
            //   * IN THEIR HAND. Switching character while holding one of the old character's cards
            //     is a real gesture — the card is deliberately left out of the exchange (CardFan's
            //     capture skips IsHeld) precisely so it stays where the player put it. Destroying it
            //     half a second later would be the same vanish by a slower route, so the restore
            //     simply waits: the hold may last as long as it likes, and the deadline above does
            //     NOT override this (it bounds the ANIMATION, not the player).
            //   * BACK IN THE FAN. Releasing that card returns it home (VRCard.InspectOnly's
            //     return-to-fan), which puts a card of the OLD character into the new one's fan for
            //     as long as it takes one rebuild to drop it. Waiting for that rebuild — and asking
            //     for it, below, so it cannot be waited on forever — means the card is already
            //     PARKED and invisible when it is destroyed, instead of blinking out of the arc.
            //   * STILL FLYING OUT. Normally the wave-drained gate above covers this, but it is
            //     overridden by the deadline — and the case the deadline exists for is a STALLED
            //     wave (CardFan.Tick bails before the swap tick when the hand or its palm anchor
            //     goes away, so the clock freezes and no card ever reaches progress 1). Past the
            //     deadline the restore would then delete cards that are still on screen. Checked
            //     per card here so the deadline can still release everything that HAS landed.
            if (FocusHandCardStillInPlay(hand, out bool needsRebuild))
            {
                if (needsRebuild)
                    _dirty = true; // ask for the rebuild that drops it from the fan and parks it
                continue;
            }
            RestoreFocusHandNow(hand);
            _pendingFaceRestore.RemoveAt(i);
        }
    }

    /// <summary>
    /// Is any VR card of <paramref name="hand"/> still somewhere the player can see it — held,
    /// sitting in the hand fan, or still flying out of it? The gate on <see cref="DrainSwapExit"/>'s
    /// face restore, which destroys exactly those cards. <paramref name="needsRebuild"/> separates
    /// the three because they call for different answers: a hold is the player's business and a
    /// flight lands on its own, so neither asks for anything, while a card still sitting in the fan
    /// is stuck until a rebuild re-fills it and the park sweep pools it. Every game deref is guarded —
    /// this is a per-frame gate, and a half-torn hand must read as "nothing of mine is in play"
    /// rather than throw inside Update.
    /// </summary>
    private bool FocusHandCardStillInPlay(CardsHandUI hand, out bool needsRebuild)
    {
        needsRebuild = false;
        bool inPlay = false;
        try
        {
            List<AbilityCardUI> cards = hand.cardsUI;
            if (cards == null)
                return false;
            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCardUI widget = cards[i];
                if (widget == null)
                    continue;
                VRCard? card = _factory.Find(widget);
                if (card == null)
                    continue;
                if (card.IsHeld || _fan.IsLeaving(card))
                {
                    // A hold ends when the player lets go; a flight ends when it lands. Neither
                    // needs anything from us, so neither asks for a rebuild.
                    inPlay = true;
                }
                else if (_fan.Contains(card))
                {
                    // THIS one is stuck until a rebuild re-fills the fan from the new character's
                    // widgets and the park sweep pools it — so ask for that rebuild.
                    inPlay = true;
                    needsRebuild = true;
                }
            }
        }
        catch
        {
            needsRebuild = false;
            return false;
        }
        return inPlay;
    }

    /// <summary>How many of <paramref name="hand"/>'s widgets the mod currently holds a VR card
    /// for. 0 after a clean release.</summary>
    private int CountFocusAdopted(CardsHandUI hand)
    {
        int n = 0;
        try
        {
            List<AbilityCardUI> cards = hand.cardsUI;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] != null && _factory.Find(cards[i]) != null)
                    n++;
            }
        }
        catch { /* half-torn hand — the count is diagnostic, never a gate */ }
        return n;
    }

    /// <summary>How many of <paramref name="hand"/>'s faces are still parented under a mod VR card
    /// — the direct evidence that a restore did NOT land. 0 after a clean release.</summary>
    private static int CountFacesUnderVrCards(CardsHandUI hand)
    {
        int n = 0;
        try
        {
            List<AbilityCardUI> cards = hand.cardsUI;
            for (int i = 0; i < cards.Count; i++)
            {
                AbilityCardUI c = cards[i];
                if (c == null || c.fullAbilityCard == null)
                    continue;
                if (c.fullAbilityCard.GetComponentInParent<VRCard>() != null)
                    n++;
            }
        }
        catch { /* diagnostic only */ }
        return n;
    }

    /// <summary>Live widget count of a hand (diagnostic; 0 while the hand is mid-build).</summary>
    private static int CountHandWidgets(CardsHandUI hand)
    {
        try { return hand.cardsUI != null ? hand.cardsUI.Count : 0; }
        catch { return 0; }
    }

    /// <summary>
    /// THE ONE HAND-FAN FILL — "a character's hand cards are shown, in every phase, for every
    /// character" (user ruling 2026-08-08: "'Keine Handkarten' soll wirklich nur dann kommen, wenn
    /// der Character auch wirklich keine Handkarten hat, egal in welcher Phase — ansonsten sollen
    /// die Handkarten angezeigt werden").
    ///
    /// <para>WHY IT IS ONE METHOD. The mod used to have TWO hand fills with different rules: the
    /// read-only FOCUS branch, which took every <c>CardPileType.Hand</c> widget unconditionally,
    /// and the vanilla <c>CardsSelection</c> branch, which took them only while
    /// <c>CardsGameApi.IsSelectionPhase</c> was true. That asymmetry IS the reported bug: after
    /// "Fortfahren", the character the GAME still presents runs the vanilla branch with
    /// <c>selecting == false</c> and got an empty fan (hardware log: <c>fan state:
    /// mode=CardsSelection, widgets=23, fanBuffer=0</c>), while every character the player FOCUSED
    /// ran the focus branch and kept its cards. One fill, one rule, no way for the two to drift.</para>
    ///
    /// <para>WHAT IS AND IS NOT FILTERED. Long-rest placeholders are not cards. Non-hand piles are
    /// not the hand (a chosen ROUND card has already flipped to <c>CardPileType.Round</c> on every
    /// client — AbilityCardUI.cs:1098/1186 via ProxySelectCard → ToggleSelect, CardsHandUI.cs:2714).
    /// And a card has exactly ONE VR visual, so anything the ROUND-CARD DOCK already took this
    /// rebuild is skipped — otherwise the fan and the dock would fight over its home every rebuild
    /// (both call <c>VRCard.SetHome</c>). Callers therefore fill <c>_halfBuffer</c> FIRST.</para>
    ///
    /// <para>NO INTERACTION IS IMPLIED. This method decides VISIBILITY only. Whether the cards may
    /// be touched is decided elsewhere and unchanged: <c>grabbable</c> (per-card stamp) plus
    /// <c>CardFan.SetReadOnly</c> (the laser/remove veto). Showing a hand never opened an input
    /// path — the same separation the focus view already relies on.</para>
    /// </summary>
    private void FillHandFan()
    {
        for (int i = 0; i < _widgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _widgetBuffer[i];
            // THE HAND FAN'S MEMBERSHIP TEST, AND IT IS A WIRE CONTRACT — the count this loop
            // produces is what a peer is told the fan holds, and it is also the index space the
            // held-card record names a seat in. It is therefore ONE shared expression
            // (CardsGameApi.HandFanMember), not a stack of ifs a peer has to re-derive; the peer
            // re-deriving it and getting the long-rest term wrong is report item 5b. The item-10
            // model belt is folded into it (CardsGameApi.ClassifyHandExit), so "the card left the
            // hand" is still the model's answer and still cannot disagree with the fly-to-pile
            // classifier — CardLeftTheHand below now only DESCRIBES that verdict for the log.
            if (!CardsGameApi.HandFanMember(widget, widget.PlayerActor
                                                    ?? (_boundHand != null ? _boundHand.PlayerActor : null)))
            {
                if (widget != null && widget.CardType == CardPileType.Hand && !widget.IsLongRest)
                    NoteCardLeftTheHand(widget);
                continue;
            }
            VRCard? already = _factory.Find(widget);
            if (already != null && _halfBuffer.Contains(already))
                continue;
            _fanBuffer.Add(AdoptedCard(widget));
        }
    }

    /// <summary>Change-dedup for the model-belt diagnostic: one line per (card, destination), not
    /// one per rebuild while the widget stays behind.</summary>
    private readonly Dictionary<int, CardsGameApi.HandExit> _loggedStaleHandCard = new(8);

    /// <summary>
    /// ITEM 10, THE BELT: does the RULES MODEL already say this card has left the hand, whatever
    /// the widget's <c>CardType</c> still claims?
    ///
    /// <para>WHY A SECOND TEST IS NEEDED AT ALL. <see cref="FillHandFan"/>'s
    /// <c>widget.CardType != CardPileType.Hand</c> is the game's own answer and it is correct — but
    /// it is a UI field, written when the game re-runs <c>CardsHandUI.UpdateCards</c>, and the
    /// authoritative move (<c>CCharacterClass.MoveAbilityCardToPile</c>) happens first. That is a
    /// window in which the model says LOST and the widget still says HAND, and a rebuild landing
    /// inside it puts a burned card on the fan. The user's wording admits no such window.</para>
    ///
    /// <para>THE MODEL IS ASKED THROUGH <see cref="RoundCardExitOf"/> — the SAME classifier the
    /// fly-to-pile trigger uses, so "the card left the hand" and "the card flew to a pile" can
    /// never be two different opinions. Only a POSITIVE destination hides the card:
    /// <c>Discarded</c>, <c>Lost</c>, <c>PermanentlyLost</c> (a burn), <c>Activated</c> (the active
    /// column owns it) and <c>StillRound</c> (a board slot owns it). <c>Hand</c> obviously keeps
    /// it. <c>NoModel</c> and <c>OffModel</c> KEEP it too, deliberately: the first means the model
    /// could not be read at all and the second covers a consumed supply card AND a half-torn
    /// actor/widget, so hiding on either would let a teardown blank a live hand. Failing towards
    /// SHOWING is the direction the whole fan is required to fail in (item 3, same report).</para>
    ///
    /// <para>NOTE THIS IS A BELT, NOT THE FIX. The reason the burned card SAT there is that nothing
    /// made the mod look — see <c>PollHandCards</c>, which is the actual defect. This closes the
    /// remaining sub-frame window, and it costs a handful of list <c>Contains</c> calls per hand
    /// card per rebuild (rebuilds are edge-driven, not per-frame).</para>
    /// </summary>
    private void NoteCardLeftTheHand(AbilityCardUI widget)
    {
        CardsGameApi.HandExit exit = CardsGameApi.ClassifyHandExit(
            widget, widget.PlayerActor ?? (_boundHand != null ? _boundHand.PlayerActor : null));
        if (exit == CardsGameApi.HandExit.InHand)
            return; // the model agrees with the widget — nothing was kept off the fan

        int id = widget.CardID;
        if (!_loggedStaleHandCard.TryGetValue(id, out CardsGameApi.HandExit was) || was != exit)
        {
            _loggedStaleHandCard[id] = exit;
            // HW-VERIFY: item 10. If this line appears, the widget's CardType still said Hand while
            // the model had already moved the card — the exact window a burned card used to be
            // visible in. Note tier because "unter keinen Umständen" needs proof at the shipped tier.
            VRLog.Note("Cards", $"Hand fan: card {id} ('{widget.name}') KEPT OFF the fan by the model " +
                                $"belt — CCharacterClass.{ModelListName(exit)} holds it while the " +
                                "widget's CardType still reads Hand. This is the sub-frame window a " +
                                "just-burned card used to stay visible in (item 10, 2026-09-02). A " +
                                "peer's mirrored fan applies the SAME belt off its own copy of that " +
                                "model (CardsGameApi.HandFanMember), so the two arcs agree on the " +
                                "card's absence rather than disagreeing about the fan's size.");
        }
    }

    private VRCard AdoptedCard(AbilityCardUI widget)
    {
        VRCard card = _factory.GetOrCreate(widget);
        if (card.NeedsFace)
            card.AttachGameCard(widget); // re-adopt after a dialog yielded the face
        HookCard(card);
        return card;
    }

    // ------------------------------------------------- focus slot-card diagnostics --
    //
    // ONE LINE PER FOCUS SWITCH that makes an empty slot self-explaining (user requirement
    // 2026-08-08). Change-deduped on (character, resolved count, reason) so a per-frame rebuild is
    // silent while a genuine switch — or a slot set that changes under a live focus, e.g. the
    // watched character's turn resolving its cards into the piles — is always logged.

    private int _loggedSlotFocusId;
    private int _loggedSlotCount = -1;
    private string? _loggedSlotReason;

    /// <summary>
    /// State the slot-card outcome for the focused character: how many cards were resolved, FROM
    /// WHICH source — and when zero, which of the four possible reasons it was. The four are
    /// deliberately distinguishable, because they call for different answers: a shut RevealGate is
    /// the anti-cheat rule working, a long rest is correct-and-permanent for the round, an empty
    /// model list is "not chosen yet or already played out", and resolved &lt; model is a transient
    /// widget gap that the next rebuild retries.
    /// </summary>
    private void LogFocusSlotCards(CardsHandUI hand, bool dockOpen, string dockSource)
    {
        int resolved = _halfBuffer.Count;
        int inModel = Board.CharacterFocus.ModelRoundCardCount(hand);
        string name = Board.CharacterFocus.Describe(hand.PlayerActor);
        int id = Net.NetFigures.StableActorId(hand.PlayerActor);

        string detail;
        if (!dockOpen)
            detail = $"0 resolved — {dockSource}. Nothing is drawn until the reveal; this is the " +
                     "anti-cheat gate holding, not a failure.";
        else if (resolved > 0)
            detail = $"{resolved} resolved from {dockSource}, docked in the board's card slot(s) " +
                     "READ-ONLY (no poke zones armed, canvas not registered with the laser, " +
                     "Grabbable=false). No card identity was added to our wire — this is the same " +
                     "host-replicated list the remote control board already renders a peer's " +
                     "played cards from.";
        else if (CardsGameApi.IsLongResting(hand) || CardsGameApi.HasLongRested(hand))
            detail = "0 resolved — the character is LONG RESTING this round, so it plays no cards " +
                     "at all. The empty slots are correct for the whole round.";
        else if (inModel == 0)
            detail = "0 resolved — the model exposes no chosen cards for this character right now " +
                     "(CCharacterClass.RoundAbilityCards is empty): either the round's cards are " +
                     "not committed yet, or this character's turn is already over and the game " +
                     "moved them to the discard/burnt pile (GameState.cs:2286).";
        else
            detail = $"0 resolved although the model names {inModel} chosen card(s) — no live " +
                     "AbilityCardUI widget for them exists on this client yet (hand mid-(re)build). " +
                     "Transient: the next rebuild retries, and the focus survives it.";

        if (id == _loggedSlotFocusId && resolved == _loggedSlotCount && detail == _loggedSlotReason)
            return;
        _loggedSlotFocusId = id;
        _loggedSlotCount = resolved;
        _loggedSlotReason = detail;
        VRLog.Info("Cards", $"[Focus] slot cards for '{name}': {detail}");
    }

    private void CollectRoundCards(CardsHandUI hand, List<VRCard> into)
    {
        // LONG REST (user report 2026-08-04: "die zwei Karten von der vorherigen Runde [sind]
        // erschienen und haben dann nochmal die Animation abgespielt ... das Board [soll] an den
        // Kartenstellen leer bleiben"). ROOT CAUSE: on a long-rest confirm the actor's own turn
        // starts (IsActionTurn true, mode still ActionSelection) with ZERO cards played this
        // round — but the phase machine's card pair (CardsActionControlller.topCard/bottomCard)
        // is only ever re-written by Init(...) when cards ARE played
        // (CardsActionControlller.cs:91-111), which never runs for a long rest. The singleton
        // therefore still holds the PREVIOUS round's FullAbilityCards, and the supplement below
        // resurrected exactly that stale pair onto the board (hardware log: turn-clear flights
        // at 13824/13825, then confirm at 14501 → "Card appear" ×2 at 14534/14535 → the pair
        // flies to the piles AGAIN at 14721). Gate on the game's OWN long-rest state: the
        // rules model plays no round cards while CharacterClass.LongRest is set (set on
        // selection, CardsHandUI.cs:1950; cleared when the rest resolves, GameState.cs:2569)
        // and HasLongRested covers the tail of the same turn after that clear (set in
        // GameState.PlayerLongRested, reset at next round start, CCharacterClass.cs:1183) —
        // so the board's round slots stay EMPTY for the whole long-rest turn, keyed on real
        // state, not a heuristic. RoundAbilityCards is empty then anyway (IsInRound already
        // false), so this only suppresses the stale static pair.
        if (CardsGameApi.IsLongResting(hand) || CardsGameApi.HasLongRested(hand))
        {
            if (!_loggedLongRestEmptyBoard)
            {
                _loggedLongRestEmptyBoard = true;
                VRLog.Info("Cards", "Round-card dock EMPTY (long rest): the actor is long-resting, so no round " +
                                    "cards exist this round — the phase machine's stale topCard/bottomCard pair " +
                                    "(previous round) is ignored, nothing docks, nothing replays a pile flight.");
            }
            return;
        }
        _loggedLongRestEmptyBoard = false;
        // The AUTHORITATIVE per-character selector is the acting hand's own round pile
        // (IsInRound → CharacterClass.RoundAbilityCards) — always exactly the two played
        // cards of THIS actor. The phase machine's pair (CardsActionControlller.topCard/
        // bottomCard) is a static singleton that can lag a same-mode turn hand-off, so it is
        // only ADDED (covers the extra-turn pile, where cards are not in RoundAbilityCards) —
        // never used ALONE, so a stale pair can no longer make us dock the previous
        // character's cards (the second-character deadlock).
        //
        // FOCUS ADDENDUM (2026-08-08): the supplement is additionally scoped to the hand whose turn
        // it actually is. The phase machine is a STATIC singleton holding the ACTING character's
        // pair, and the round-card dock now also fills for a focused character that is NOT acting —
        // so an unscoped supplement could dock the acting character's cards onto a watched
        // character's board. The widget loop below only walks THIS hand's own widgets, so a foreign
        // FullAbilityCard could never match anyway; making the scope explicit means that safety is a
        // stated rule instead of a coincidence of which buffer we happen to be iterating.
        FullAbilityCard? first = null;
        FullAbilityCard? second = null;
        if (ReferenceEquals(Board.CharacterFocus.TurnActor, hand.PlayerActor))
            CardsGameApi.GetActionCards(out first, out second);
        for (int i = 0; i < _widgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _widgetBuffer[i];
            if (widget.AbilityCard == null || widget.IsLongRest)
                continue;
            bool staticPair = (first != null && widget.fullAbilityCard == first)
                              || (second != null && widget.fullAbilityCard == second);
            // ─── A CARD THAT IS SITTING IN A PILE IS NOT A ROUND CARD, WHATEVER THE STATIC PAIR
            //     SAYS (2026-09-07, the user's second item 5, verbatim: "Ich habe beim Schaden
            //     erhalten des Mitspielers auf seinem remote-board eine Flug animation einer
            //     verdeckten Karte sehen können! Wenn man den Schaden nimmt passiert gar nichts mit
            //     den Karten - hier einen Flug macht absolut keinen Sinn.")
            //
            // THE CHAIN, MEASURED END TO END ACROSS BOTH ModBuild 476 LOGS. The game's own
            // TakeDamagePanel offers "2 abgeworfene Karten verbrennen", and to offer it, it STAGES
            // the two candidate cards OUT of DiscardedAbilityCards and exposes live AbilityCardUI
            // widgets for them (peer raw 148862 publishes the three-option prompt; 148980 reads
            // `Piles: discard=1` where 149216 reads `discard=3`). The player then clicks 'Receive
            // Damage' (peer raw 148934) — the option under which NO CARD MOVES AT ALL. The panel
            // closes, 'Cards Hands Manager' re-shows (148964), a rebuild runs, and this supplement
            // admitted both staged widgets: peer 148973 `Dock seats [Steel]: 4 card(s) docked in the
            // recesses`, four `Card appear` lines at 148974-148978 (two of them SpareDagger and
            // OverwhelmingAssault, already discarded 172 s earlier at 127912/127916), and
            // `Rebuild: mode=ActionSelection fan=2 tray=True half=4` at 148979. The game then
            // un-stages them, they drop out of the dock, and TryStartFlyToPile sees its exact
            // trigger signature — was in _lastHalfCards, is in no zone now, RoundCardExitOf answers
            // Discarded — and fires TWICE: peer 149156/149160 `PILE FLIGHT [own] ... own-turn-clear
            // ... game phase ActionSelection`, each with `FLIGHT ORIGIN [turn-clear]: this client's
            // own card left the board CENTRE (no recess ...)` because both had been seated on the
            // beside-Slot2 overflow, which RecessSeatOfCard refuses. The host mirrored both as
            // face-down flights (host raw 167487/167494), and that is what he saw.
            //
            // WHY RoundCardExitOf COULD NOT CATCH IT, AND WHY THE FIX BELONGS HERE. That method is a
            // MEMBERSHIP query, not an edge detector: a card that has lain in DiscardedAbilityCards
            // for three minutes answers `Discarded` forever, and the only thing standing between it
            // and a flight is this dock's membership. So the card must never enter the dock. There
            // is already a gate of exactly this shape one branch up — the 2026-08-04 LONG REST
            // hardening, written because the same static topCard/bottomCard singleton resurrected
            // the previous round's pair — and the two failures are the same failure: the singleton
            // is stale and the supplement believed it. That gate keys on a FLOW; this one keys on
            // the CARD, so it covers every future flow that stages a pile card into a live widget
            // without anybody having to enumerate them.
            //
            // IT CANNOT COST THE SUPPLEMENT ITS JOB. The supplement exists for the EXTRA-TURN pile
            // (`CCharacterClass.ExtraTurnCards`, cards deliberately not in RoundAbilityCards), and
            // an extra-turn card is in that list rather than in a pile, so it still passes. What is
            // refused is only a card the model itself says has already left: discarded, burnt,
            // permanently lost, or activated.
            bool isActionCard =
                CardsGameApi.IsInRound(hand, widget.AbilityCard)
                || (staticPair && !HasLeftTheRound(hand, widget));
            if (staticPair && !isActionCard)
                LogStaleStaticPairRefused(widget);
            if (isActionCard)
            {
                VRCard card = AdoptedCard(widget);
                if (!into.Contains(card))
                    into.Add(card);
                // The card is on the board again, so its previous "left the dock without flying"
                // verdict is spent: the NEXT time it leaves, that is a new event and may log again
                // (one refusal line per focus switch — see LogFlightRefused). This is also what
                // bounds the table: only cards that were docked can ever be in it.
                _loggedFlightRefusal.Remove(widget);
            }
        }

        OrderRoundPairByInitiative(hand, into);
    }

    /// <summary>
    /// HAS THIS WIDGET'S CARD ALREADY LEFT THE ROUND, according to the game's own authoritative
    /// lists? The gate the static <c>CardsActionControlller.topCard/bottomCard</c> supplement in
    /// <see cref="CollectRoundCards"/> is asked before it may dock a card.
    ///
    /// <para>ONE EXPRESSION, AND THE SAME ORDER <see cref="RoundCardExitOf"/> USES: the "is it still
    /// a round card" question first, so <c>ExtraTurnCards</c> — the entire reason the supplement
    /// exists — can never be read as "left". Only then the four lists that mean it is gone. It is a
    /// separate predicate rather than a call into <see cref="RoundCardExitOf"/> because that method
    /// takes a <c>VRCard</c>, which this loop does not have yet: reaching one would mean calling
    /// <c>AdoptedCard</c>, i.e. ADOPTING the very card we are about to refuse.</para>
    ///
    /// <para>The card's OWN owner is authoritative, exactly as in <see cref="RoundCardExitOf"/> —
    /// in a sequential two-character turn the staged widget can belong to a different actor than
    /// the presented hand. Falls back to the hand's actor when the widget names none, and answers
    /// FALSE (i.e. "not proven to have left", the answer that changes nothing) whenever the model
    /// cannot be read at all.</para>
    ///
    /// <para><c>ActivatedCards</c> is the RAW <c>List&lt;CBaseCard&gt;</c> field, never the
    /// <c>ActivatedAbilityCards</c> LINQ projection, which allocates a fresh list on every read —
    /// this runs inside a rebuild.</para>
    /// </summary>
    private static bool HasLeftTheRound(CardsHandUI hand, AbilityCardUI widget)
    {
        CAbilityCard? ac = widget.AbilityCard;
        CPlayerActor? owner = widget.PlayerActor != null ? widget.PlayerActor : hand.PlayerActor;
        if (ac == null || owner == null)
            return false;
        CCharacterClass klass = owner.CharacterClass;
        if (klass == null)
            return false;
        if (klass.RoundAbilityCards.Contains(ac) || klass.ExtraTurnCards.Contains(ac))
            return false;
        return klass.DiscardedAbilityCards.Contains(ac)
               || klass.LostAbilityCards.Contains(ac)
               || klass.PermanentlyLostAbilityCards.Contains(ac)
               || klass.ActivatedCards.Contains(ac);
    }

    /// <summary>Widgets whose stale static-pair docking has already been reported, so the line below
    /// is one per card per session rather than one per rebuild (the condition stands for as long as
    /// the singleton is stale, which on the measured take-damage flow was ~3.7 s of rebuilds).
    /// </summary>
    private readonly HashSet<AbilityCardUI> _loggedStaleStaticPair = new();

    private void LogStaleStaticPairRefused(AbilityCardUI widget)
    {
        if (!_loggedStaleStaticPair.Add(widget))
            return;
        // HW-VERIFY: 2026-09-07, the user's SECOND item 5 — "Ich habe beim Schaden erhalten des
        // Mitspielers auf seinem remote-board eine Flug animation einer verdeckten Karte sehen
        // können! Wenn man den Schaden nimmt passiert gar nichts mit den Karten". Grep token:
        // STALE ROUND PAIR REFUSED.
        //
        // WORKING = this line firing on a take-damage decision, with NO 'Card appear' for that card
        //           beside it and NO later '[Cards] PILE FLIGHT [own] ... own-turn-clear' naming it
        //           in phase ActionSelection. On the ModBuild 476 peer log the same moment produced
        //           `Dock seats: 4 card(s)`, four 'Card appear' lines and two spurious flights
        //           (raws 148973-148978, 149156, 149160).
        // INERT   = a repeat of that 476 signature with NO line here: the widget reached the dock
        //           through CardsGameApi.IsInRound instead, i.e. the game really had put the card
        //           back in RoundAbilityCards and this gate is not the one that owes the refusal.
        // BEYOND  = this line firing on an EXTRA-TURN card (its own 'Dock seats' line then reports
        //           fewer cards than the owner can see). That would mean ExtraTurnCards did not
        //           hold a card the supplement exists for, and the term to add is that list's own
        //           reading, never a loosening of the four pile tests.
        VRLog.Note("Cards", "STALE ROUND PAIR REFUSED: the phase machine's static "
            + $"CardsActionControlller pair still names '{CardName(widget.AbilityCard)}', but that "
            + "card is sitting in one of its owner's PILES (discard / burnt / permanently lost / "
            + "active) right now, so it is not a round card and does not dock. This is the "
            + "take-damage staging window: the game's TakeDamagePanel lifts candidate cards out of "
            + "DiscardedAbilityCards to offer 'X abgeworfene Karten verbrennen' and puts them back "
            + "when the player takes the damage instead. Docking them made them appear on the "
            + "board, and un-docking them made TryStartFlyToPile fly them to the discard pile a "
            + "second time — a flight for a card that never moved, mirrored to every peer as an "
            + "anonymous BACK because the overflow seat gives it no recess to inherit a face from.");
    }

    /// <summary>
    /// Put the round pair in the order the OWNER sees, by seating the class's
    /// <c>InitiativeAbilityCard</c> first — user report 8, 2026-08-15 three-player session:
    /// <i>"Die Position der Karten (linke Karte/rechte Karte) war in einem Test verdreht wenn ich
    /// einen Character anklicke die einem anderen Spieler gehört. Die Reihenfolge MUSS zwingend
    /// identisch sein wie es der jenige Spieler auch sieht."</i>
    ///
    /// <para><b>THE ORDER WAS NEVER A FACT — it was three derivations that agreed by accident.</b>
    /// The owner's tray seats the initiative card in recess 0; the mirrored peer board derived
    /// initiative-first as well; and this dock took the iteration order of <em>this client's own</em>
    /// <c>CardsHandUI.cardsUI</c>, a list whose order depends on whenever that client last ran the
    /// unstable <c>SortCards()</c>. The three logs of that session catch it: the owner's own tray had
    /// UnbridledPower on the left while his own docked pair and both watchers had FatalFury there —
    /// and an earlier round in the same session has all three agreeing, which is exactly the
    /// "in EINEM Test verdreht" signature.</para>
    ///
    /// <para><b>Why initiative-first is the right fact rather than a fourth guess:</b>
    /// <c>CCharacterClass.InitiativeAbilityCard</c> is a replicated reference every client resolves
    /// identically, and the game's own <c>SwapInitiative()</c> moves which card holds it. So this is
    /// the one ordering input that cannot differ per machine, and it is the rule the owner's tray
    /// already follows. No card identity goes anywhere near the wire: both cards were already
    /// resolved locally from the replicated model, and only WHICH SLOT each takes changes.</para>
    ///
    /// <para><b>The limit, stated:</b> the MIRRORED board takes the owner's transmitted slot-order
    /// bit (extension record 18), which is authoritative even if the owner's physical recesses ever
    /// disagree with initiative-first. This dock has no actor-to-player map to look that bit up
    /// with, so it uses the replicated derivation. If a hardware log ever shows the mirrored board
    /// and the focus dock disagreeing for the same character, that map is the missing piece — and
    /// the disagreement is then between "the owner's recesses" and "the initiative card", not
    /// between two clients.</para>
    /// </summary>
    private static void OrderRoundPairByInitiative(CardsHandUI hand, List<VRCard> into)
    {
        if (into.Count != 2)
            return;
        ScenarioRuleLibrary.CAbilityCard? initiative = CardsGameApi.InitiativeCard(hand);
        if (initiative == null)
            return;
        // Only a swap, never a sort: with exactly two cards the question is which one leads, and
        // moving the second to the front is the whole operation.
        if (ReferenceEquals(into[1].GameCard?.AbilityCard, initiative)
            && !ReferenceEquals(into[0].GameCard?.AbilityCard, initiative))
        {
            (into[0], into[1]) = (into[1], into[0]);
        }
    }

    // ------------------------------------------------ the model has the last word on the fan --
    //
    // USER REPORT (2026-09-04, verbatim): "Der Mitspieler hat zwei mal hintereinander Schaden
    // bekommen. Das erste mal hat er eine Karte verbrannt, beim zweiten mal hat er zwischendurch
    // kurz den Character gewechselt und nach dem Zurückwechseln hatte er die bereits verbrannte
    // Karte wieder auf der Hand. Das darf unter keinen Umständen sein."
    //
    // WHAT THE HARDWARE LOG ACTUALLY SHOWS (remote/LogOutput.log, one continuous sequence):
    //   47133  Pick commit (LoseCard): 'ABILITY_CARD_WardingStrength' via drop-slot → accepted
    //   47269  BURN ANIM: holding 'ABILITY_CARD_WardingStrength' ON THE BOARD …
    //   47361  … waited 3,00s … flying to the Burnt pile now      ← the burn LANDED correctly
    //   47367  … reached the Burnt pile — parked
    //   47380  fan state: mode=LoseCard, widgets=14, fanBuffer=5   ← still correct: 5 hand cards
    //   48294  [Focus] now looking at 'Testo' … 48310 fanBuffer=6  ← Testo's own hand, correct
    //   48449  [Focus] cleared … 48450 RESTORED 'Testo' … 0 / 0    ← a clean hand-back
    //   48451  Pick fan source (LoseCard): real hand — the selectable cards ARE the hand fan
    //   48453  Fan order [scenario hand]: n=6 … 'ABILITY_CARD_WardingStrength' sits RIGHT of a
    //          higher initiative                                   ← THE BURNED CARD IS BACK
    //   48457  Piles: discard=4, burnt=1 for 'Testi'               ← the MODEL was right all along
    //   49283  the hand is physically in 'VRCard_ABILITY_CARD_WardingStrength' (hand fan)
    //
    // ROOT CAUSE, AND IT IS NOT THE BURN AND NOT THE FACE RESTORE. Both of those did exactly what
    // they promise, and the log says so line by line. The card came back because the take-damage
    // decision runs the board in CardHandMode.LoseCard, and THAT rebuild branch does not fill the
    // fan from the hand at all — it fills it from the GAME WIDGET's own `isSelectable` latch
    // (AbilityCardUI.SetMode → SetSelectable, AbilityCardUI.cs:900/906). Two properties of that
    // latch make the reappearance inevitable:
    //   * it is a LATCH, written only when the game re-runs SetMode. The second take-damage
    //     decision opened while the burned widget's own `cardType` still read Hand, so the widget
    //     was latched selectable and stayed selectable for the whole decision;
    //   * while the burned card was still the PICK-FIELD occupant it was excluded from the fan by
    //     `_fieldCards` (that is the fanBuffer=5), and the moment the field prune dropped it — any
    //     rebuild would do; here it was the focus switch back — the stale latch put it straight
    //     into the fan (fanBuffer=6).
    // FillHandFan's model belt (CardLeftTheHand, item 10, 2026-09-02) would have refused it
    // outright. It never ran: in LoseCard mode FillHandFan is not the fill.
    //
    // WHY THIS IS THE CLASS FIX AND THE BELT WAS NOT. The belt is correct and stays — but it sits
    // in ONE of the fan's fills, so every OTHER fill is free to disagree with the rules model, and
    // that is precisely how the same defect returned a third time. This veto sits at the point
    // where the fan's contents are FINAL, after every branch has had its say and before anything
    // is published or parked, and it asks one question of the game's authoritative lists:
    // "does the model agree this card belongs in the pile the fan is showing?" A future fill —
    // a new mode, a new flow, a new source — inherits it without knowing it exists.
    //
    // IT NEVER WRITES GAME STATE. Every read is a Contains() on a CCharacterClass list; the only
    // thing that changes is which VR cards this mod draws.

    /// <summary>
    /// WHICH PILE THE FAN IS SHOWING THIS REBUILD — the veto's expectation, and the one thing it
    /// must not guess.
    ///
    /// <para>Every fill except the pick branch is the HAND (FillHandFan is the only executor, in
    /// the read-only focus view and in every mode branch alike). The PICK fill is not: the game
    /// marks a whole pile selectable and the fan becomes that pile — the hand for "1 verfügbare
    /// Karte verbrennen" (TakeDamagePanel.cs:538, <c>CardPileType.Hand</c>), the DISCARD pile for
    /// "2 abgeworfene Karten verbrennen" (:575), the burnt pile for a recover
    /// (Choreographer.cs:7775). So the pick expectation is read from the GAME's own declaration of
    /// it — <c>CardsHandUI.selectableCardTypes</c>, written by <c>UpdateView</c>
    /// (CardsHandUI.cs:580) from that same <c>Show(...)</c> argument, and the very list the fill's
    /// own <c>isSelectable</c> / <c>IsPickEligible</c> tests are derived from, so the two cannot
    /// disagree about what is on offer.</para>
    ///
    /// <para>ANYTHING BUT EXACTLY ONE NAMED PILE MEANS NO OPINION, and no opinion vetoes nothing:
    /// <c>Any</c> (TakeDamagePanel.cs:520 opens the take-damage view with every pile selectable),
    /// <c>Unselected</c> (the card-limit pick), several piles at once, or a hand we cannot read.
    /// Failing towards SHOWING is the direction this whole fan is required to fail in.</para>
    /// </summary>
    private static CardPileType FanPileShowing(CardsHandUI? hand, bool readOnly)
    {
        // ITEM 6b: a pick whose flow ENDED is filled by FillHandFan (see the LoseCard branch), so
        // the pile it is showing is the HAND — reading the latched mode here made the model veto
        // compare the hand fan against the burn pick's declared pile.
        if (readOnly || !PickFlowLive(hand))
            return CardPileType.Hand;
        try
        {
            List<CardPileType>? piles = hand != null ? hand.selectableCardTypes : null;
            return piles != null && piles.Count == 1 ? piles[0] : CardPileType.None;
        }
        catch (System.Exception)
        {
            return CardPileType.None; // a half-torn hand has no opinion, and no opinion shows the card
        }
    }

    /// <summary>Change-dedup for the veto line: the last (verdict, expectation) logged per card,
    /// and how many times the veto has fired for it. The COUNT is in the line so a veto that keeps
    /// firing is never silent — a change-gated line with a constant reason reads as a dead
    /// instrument otherwise.</summary>
    private readonly Dictionary<int, (RoundCardExit Exit, CardPileType Expected, int Count)> _loggedFanVeto = new(8);

    /// <summary>
    /// THE CHOKE POINT: drop every card the fan is about to show that the RULES MODEL puts in a
    /// different pile than the one the fan is showing. See the region header for the report and the
    /// root cause.
    ///
    /// <para>ONLY A POSITIVE CONTRADICTION VETOES, and the direction of failure is the one this
    /// whole fan is required to fail in (item 3): <c>NoModel</c> (no CAbilityCard / no owning
    /// actor) and <c>OffModel</c> (a consumed supply card, or a half-torn actor) KEEP the card.
    /// A card with no game widget at all — the dev fake hand, the map room's loadout fan — has no
    /// model to ask and is never touched.</para>
    ///
    /// <para>The model is read through <see cref="RoundCardExitOf"/>, the same classifier the
    /// fly-to-pile trigger and <c>CardLeftTheHand</c> use, so "the card left the hand", "the card
    /// flew to a pile" and "the card may not be shown" can never become three opinions.</para>
    ///
    /// <para>Cost: one <c>RoundCardExitOf</c> per fan card per REBUILD (edge-driven, ~5-14 cards),
    /// i.e. a handful of list <c>Contains</c> calls. No allocation.</para>
    /// </summary>
    private void DropFanCardsTheModelMoved(CardsHandUI? hand, CardHandMode mode, bool readOnly)
    {
        if (_fanBuffer.Count == 0)
            return;
        CardPileType showing = FanPileShowing(hand, readOnly);
        if (showing == CardPileType.None)
            return; // the fan is not showing one nameable pile — nothing to contradict
        for (int i = _fanBuffer.Count - 1; i >= 0; i--)
        {
            VRCard card = _fanBuffer[i];
            if (card == null)
                continue;
            AbilityCardUI? widget = card.GameCard;
            if (widget == null)
                continue; // a mod-built card (dev fake hand / map-room loadout) — no model to ask
            RoundCardExit exit = RoundCardExitOf(hand, card, out CPlayerActor? owner);
            if (!ModelContradictsFanPile(showing, exit))
                continue;
            _fanBuffer.RemoveAt(i);
            LogFanModelVeto(hand, widget, exit, owner, mode, readOnly, showing);
        }
    }

    /// <summary>
    /// Does the model's answer for a card CONTRADICT the pile the fan is showing? One row per pile
    /// the fan can ever show; everything not named is "no opinion", which shows the card.
    ///
    /// <para><c>Lost</c> and <c>Permalost</c> are ONE stack on the board (the 2D hand shows both
    /// under one "burnt" header, CardsHandUI.cs:1333/1340), so either model verdict satisfies
    /// either expectation — otherwise a recover-lost fan would veto its own contents.</para>
    /// </summary>
    private static bool ModelContradictsFanPile(CardPileType showing, RoundCardExit exit) => showing switch
    {
        CardPileType.Hand => exit is RoundCardExit.Discarded or RoundCardExit.Lost
            or RoundCardExit.PermanentlyLost or RoundCardExit.Activated or RoundCardExit.StillRound,
        CardPileType.Discarded => exit is RoundCardExit.Hand or RoundCardExit.Lost
            or RoundCardExit.PermanentlyLost or RoundCardExit.Activated or RoundCardExit.StillRound,
        CardPileType.Lost or CardPileType.Permalost => exit is RoundCardExit.Hand
            or RoundCardExit.Discarded or RoundCardExit.Activated or RoundCardExit.StillRound,
        _ => false, // Round / Active / Any / Unselected / ExtraTurn / None — not a pile this fan shows
    };

    /// <summary>
    /// The veto's HW-VERIFY line: what was offered, by WHICH fill, and what the model said instead.
    /// Change-deduped per (card, verdict, expectation) with a running count.
    /// </summary>
    private void LogFanModelVeto(CardsHandUI? hand, AbilityCardUI widget, RoundCardExit exit,
        CPlayerActor? owner, CardHandMode mode, bool readOnly, CardPileType showing)
    {
        int id = widget.CardID;
        _loggedFanVeto.TryGetValue(id, out (RoundCardExit Exit, CardPileType Expected, int Count) was);
        int count = was.Count + 1;
        bool changed = was.Count == 0 || was.Exit != exit || was.Expected != showing;
        _loggedFanVeto[id] = (exit, showing, count);
        if (!changed)
            return;
        // WHICH PATH OFFERED IT. The pick fills read the game widget's own isSelectable/
        // IsPickEligible latch; every other fill is FillHandFan. Naming the path is the whole
        // point of this line: a future reappearance is then one grep away from its source.
        // ITEM 6b: LIVE, not the latched mode — a pick whose flow ENDED is filled by FillHandFan
        // now, so naming it "the PICK fill" would send the next round to the wrong loop.
        string path = readOnly
            ? "the read-only FOCUS fill (FillHandFan)"
            : PickFlowLive(hand)
                ? $"the PICK fill (mode={mode} — the game's own AbilityCardUI.isSelectable / " +
                  "IsPickEligible latch, which is written once per SetMode and can be stale)"
                : $"the hand fill (FillHandFan, mode={mode})";
        // HW-VERIFY: the burned-card-back-in-the-hand instrument (user 2026-09-04). If a burned card
        // is EVER visible in the fan again and this line is absent, the offer came from a fill that
        // does not pass through the veto — which is the only way left for this defect to exist.
        VRLog.Note("Cards", $"HAND FAN MODEL VETO (#{count} for this card): card {id} " +
                            $"('{CardsGameApi.CardName(widget)}', owner " +
                            $"'{(owner != null ? CardsGameApi.ActorLabel(owner) : "?")}') was offered to the fan " +
                            $"by {path}, but the RULES MODEL puts it in CCharacterClass." +
                            $"{ModelListName(exit)} while the fan is showing the {showing} pile. " +
                            "DROPPED before the fan was published; the park sweep pools its VR card. The game's " +
                            "own model is authoritative and this mod never argues with it — the widget flag that " +
                            "offered the card is a UI latch the game rewrites later. This is the exact window a " +
                            "burned card came back onto the hand fan after a character switch (report 2026-09-04, " +
                            "'die bereits verbrannte Karte wieder auf der Hand').");
    }

    // ---------------------------------------------------------------- fly-to-pile (issue 5) --

    /// <summary>
    /// Every destination a DOCKED ROUND CARD can have, read off the owner's authoritative
    /// <c>CCharacterClass</c> lists. The game's own end-of-turn drain is
    /// <c>CCharacterClass.DiscardRoundAbilityCards</c> (CCharacterClass.cs:505, called from
    /// GameState.cs:2286/2271), which routes every round / extra-turn card through
    /// <c>MoveAbilityCardToPile</c> (CCharacterClass.cs:418) into EXACTLY one of the lists below —
    /// and <c>MoveAbilityCard</c> (CCharacterClass.cs:273) removes it from the source list first,
    /// so the lists are mutually exclusive and this classification is total.
    /// </summary>
    private enum RoundCardExit
    {
        /// <summary>No CAbilityCard / no owning actor — the model cannot be asked. Never fly.</summary>
        NoModel,
        /// <summary>Still in <c>RoundAbilityCards</c> / <c>ExtraTurnCards</c>: the card did NOT move.
        /// The dock's CONTENT changed (a character focus switch) — that is not a card going anywhere.</summary>
        StillRound,
        /// <summary><c>DiscardedAbilityCards</c> → the DISCARD stack.</summary>
        Discarded,
        /// <summary><c>LostAbilityCards</c> (a burn) → the BURNT stack.</summary>
        Lost,
        /// <summary><c>PermanentlyLostAbilityCards</c> → the BURNT stack (same stack as Lost; the 2D
        /// hand shows both under one "burnt" header, CardsHandUI.cs:1333/1340).</summary>
        PermanentlyLost,
        /// <summary><c>ActivatedCards</c> — a persistent/round-long card went to the ACTIVE COLUMN,
        /// not to a pile at all. No flight: the active-cards viewer picks it up.</summary>
        Activated,
        /// <summary><c>HandAbilityCards</c> — the selection was undone
        /// (<c>ClearRoundAbilityCards</c>, CCharacterClass.cs:522): back in the fan, no flight.</summary>
        Hand,
        /// <summary>In none of the lists — a SUPPLY card is consumed and removed from the model
        /// outright (CCharacterClass.cs:420-432), or the actor/widget went away. No flight.</summary>
        OffModel,
    }

    /// <summary>
    /// Issue 5 (user): animate a just-cleared PLAYED round card flying into its destination pile
    /// instead of instantly vanishing.
    ///
    /// <para>THE TRIGGER IS THE MODEL, NOT THE DOCK (user report 2026-08-08, bug 1). This used to
    /// fire on "the card was docked last rebuild and is in no zone now", which is a statement about
    /// the DOCK's contents — and a character focus switch empties the dock without moving a single
    /// card, so merely LOOKING at another character replayed both round cards into the discard pile
    /// (hardware log LogOutput.log:3300/3344/3384/3767/3803/3894/3930/4549, eight identical
    /// turn-clear flights of the same two cards in one session, no turn ever ended between them).
    /// The dock membership is still the pre-filter — only a card that was actually lying in the
    /// board's round slots may fly — but the DECISION is now
    /// <see cref="RoundCardExitOf"/>: the card flies only when it genuinely LEFT
    /// <c>RoundAbilityCards</c> FOR a pile, and it flies to the pile it actually entered.</para>
    ///
    /// <para>Returns true when this path OWNS the card — either the fly launched (the completion
    /// callback parks it on arrival) or, for a BURNT fate, the flight is HELD while the game's burn
    /// artwork plays on the card where it lies (<see cref="TryTakeBurnFlightSlot"/>, the one wait
    /// every burn producer and every peer's mirror share). In both cases the caller must skip the
    /// instant park. Returns false (→ the caller's shrink-and-fade / instant hide fallback) when the
    /// card isn't a cleared round card, is already flying, isn't visible, did not move in the model,
    /// moved somewhere that is not a pile, or the target pile is off / not built.</para>
    /// </summary>
    private bool TryStartFlyToPile(CardsHandUI hand, VRCard card)
    {
        if (card.GameCard == null || card.IsFlying)
            return false;
        // TWO pre-filters, not one. The round-card dock is the original (issue 5). The pick FIELD
        // is the second (user 2026-08-24, the event-discard report): the last card of an event
        // discard leaves the recess only when the game's own confirm dialog commits, and it used to
        // fall through every branch below and POP out of existence via _factory.Park. A field card
        // is admitted for a DISCARD only — a burn keeps its user-ruled "artwork on the lying card
        // FIRST, then the pile flight" order, which lives in TryStartBurnFly/TryTakeBurnFlightSlot
        // and would be clipped if this branch won the race for it.
        bool wasDocked = _lastHalfCards.Contains(card);
        bool wasPickField = !wasDocked && _lastFieldCards.Contains(card);
        if (!wasDocked && !wasPickField)
            return false; // never a fan/browse/active/etc. card
        if (!card.gameObject.activeInHierarchy)
            return false; // already parked/pooled — nothing to animate from

        RoundCardExit exit = RoundCardExitOf(hand, card, out CPlayerActor? owner);
        if (wasPickField && exit != RoundCardExit.Discarded)
        {
            // burn → TryStartBurnFly (artwork first); anything else → no pile at all. Logged on the
            // SAME per-widget-per-verdict dedupe as the docked refusal, so "the card popped and no
            // flight line appeared" is answerable from the log instead of merely observed.
            LogFlightRefused(card, exit, owner, fromPickField: true);
            return false;
        }
        PileKind fate;
        switch (exit)
        {
            case RoundCardExit.Discarded:
                fate = PileKind.Discard;
                break;
            case RoundCardExit.Lost:
            case RoundCardExit.PermanentlyLost:
                fate = PileKind.Burnt;
                break;
            default:
                // The dock changed but the CARD did not go to a pile. Say so once, so a regression
                // (a spurious flight, or a missing one) is visible in the next hardware log.
                LogFlightRefused(card, exit, owner);
                return false;
        }
        // The card really moved — a later dock change for it is a different event and may log again.
        _loggedFlightRefusal.Remove(card.GameCard!);

        if (!_piles.TryGetPileWorld(fate, out Vector3 worldPos, out float slabWidth))
            return false; // pile offscreen / not built → fall back to the instant hide

        // ─── A BURN IS A BURN "EGAL AUS WELCHEM GRUND", INCLUDING THIS ONE ──────────────────────
        // (2026-09-07 round, item 8: "Prüfe das nochmal bei allen Verbrennen-Flows!")
        //
        // THIS WAS THE UNWAITED PRODUCER. A played round card whose own action is a LOST action
        // reaches the park sweep with fate Burnt, and this method sits FIRST in that sweep's
        // if/else chain (Rebuild ~:1425), ahead of TryStartBurnFly — so for that card it won the
        // race, flew immediately, and then dropped the artwork hold outright so the waiting
        // producer could never re-claim it. The user's ruling from 2026-08-03 ("Ich möchte, dass
        // die Karte erst liegen bleibt, man auf der Karte selber die Verbrannt-Animation abwartet
        // und DANN in das jeweilige Pile geht") had one flow that never obeyed it, and it was this
        // one. Three rounds of per-flow patches never reached it because it is not in the burn
        // file's call graph at all — it is the DISCARD path that also handles burns.
        //
        // THE FIX IS THE SAME GATE, NOT A NEW ONE. TryTakeBurnFlightSlot is the single wait
        // (BurnArtwork.Released), which RemoteBurnFx evaluates too, so obeying it here adds no
        // signal and no sequencer — it removes an exception. Returning TRUE while held is the
        // contract TryStartBurnFly already uses at :2811: "the burn path owns this card, leave it
        // lying exactly where it is", so the caller does not park or vanish it.
        //
        // WHO RE-OFFERS IT. Not this method — the next rebuild recomputes _lastHalfCards and the
        // card drops out of the wasDocked pre-filter. That is exactly the frame TryAnimateBurn's
        // ownedElsewhere gate (:3567, `_lastHalfCards.Contains(card)`) stops refusing it, so the
        // pile watcher picks the hold up and finishes it. The two gates are complementary by
        // construction, and because the hold lives in _burnHoldSince keyed on the WIDGET, the wait
        // is continuous across the handover: TryTakeBurnFlightSlot reads the same Since and the
        // total is measured from the pile-count edge, not restarted.
        //
        // NOT DONE THE OTHER TWO WAYS, and here is why each loses. (a) Deleting the ClearBurnHold
        // below: that line is what stops a second slab flying for a card this path already flew, so
        // removing it while the flight still went unwaited would trade a missing wait for a double
        // flight. (b) Making this method DECLINE a Burnt fate and letting the else-if below take
        // it: TryStartBurnFly requires IsFreshBurn, which demands hand == _burnWatchHand, an owner
        // match and absence from _knownBurntWidgets — on the first frame after a hand change those
        // are false, the card would fall through to the Vanish branch, and that is verbatim the
        // "burn artwork plays, then the card just VANISHES" report of 2026-08-04. Narrowing a burn's
        // safety net to widen its wait is the wrong trade.
        if (fate == PileKind.Burnt && card.GameCard != null
            && !TryTakeBurnFlightSlot(card.GameCard, card))
            return true; // HELD: this path owns the card and it stays lying exactly where it is

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        float arcHeight = Mathf.Max(minArc, Vector3.Distance(card.transform.position, worldPos) * VRCard.FlyArcHeightFraction);
        _flyingToPile.Add(card);
        // A burnt-fate round card may have a pending artwork HOLD from the pile watcher (the burn
        // artwork played on the docked card mid-turn). This flight consumes the burn — drop the
        // hold so its later release can never fly a second slab for the same card.
        //
        // IT IS A BELT NOW, NOT THE MECHANISM. Since the gate above, a Burnt fate reaching this
        // line has just been RELEASED by TryTakeBurnFlightSlot, which removes the entry itself — so
        // for the burn path this call is provably a no-op. It stays because it still covers a hold
        // taken against the same widget by another producer, and because deleting a line whose
        // absence would strand state is the edit this file has been burned by before.
        if (card.GameCard != null)
            ClearBurnHold(card.GameCard);
        VRCard flying = card;
        PileKind dest = fate;
        // MP parity (report 6): peers replay this exact flight (slot → discard/burnt stack) against
        // THEIR copy of this player's board pose — 2 bytes, no per-frame transforms. Because the
        // decision above is now the MODEL's, the peer's mirrored flight inherits both halves of the
        // fix: it fires only for a real move, and its destination anchor is the real destination.
        //
        // ─── THE ORIGIN WAS ALWAYS `Board` AND IT WAS THIS EXPRESSION (2026-09-06 item 5) ───────
        // It read SlotAnchor(_tray.SlotOf(card)). SlotOf answers off _occupants — the CardsSelection
        // round-card bookkeeping — and this method is reached from the rebuild's zone loop for a
        // card that is in NO current zone, launched off _lastHalfCards, the PREVIOUS rebuild's dock.
        // By the time it runs, SyncFromGameState has already evicted that card from _occupants, so
        // SlotOf returns -1 BY CONSTRUCTION and SlotAnchor mapped it to CardFxAnchor.Board on every
        // single turn-clear. That is measured, not inferred: the 2026-09-06 session's two logs carry
        // "Board -> Discard" and "Board -> Burnt" and not one "Slot0 ->" or "Slot1 ->" on either
        // machine. The peer's mirrored flight therefore STARTED AT THE BOARD CENTRE while its owner
        // watched the same card leave its recess — the half of the 1:1 breach that survived the
        // face fix of the same round.
        //
        // THE EVICTION IS BOOKKEEPING; THE CARD HAS NOT MOVED. Neither ClearSlots nor RemoveCard nor
        // SyncFromGameState's eviction reparents anything — each of them nulls an occupant entry and
        // stops — so at this instant the card is still a CHILD of its recess transform, exactly
        // where its owner is looking at it. PlayTray.RecessSeatOfCard reads that physical truth (and
        // refuses the beside-slot-2 overflow seat, which is parented to a recess it is not in).
        // Nothing is guessed and no new anchor value is needed: Slot0/Slot1 already exist and
        // RemoteControlBoard.AnchorLocalLive resolves both to the rendered card SEAT of whatever
        // board style the peer runs — they were added for exactly this.
        //
        // _tray.SlotOf STAYS as the fallback rather than being replaced outright: it is the answer
        // for a card the game seated without a VR drop and whose transform a rebuild has already
        // re-homed, and Board remains the honest last resort for a card in neither.
        int flightSeat = _tray.RecessSeatOfCard(card);
        if (flightSeat < 0)
            flightSeat = _tray.SlotOf(card);
        Net.CardFxAnchor flightOrigin = SlotAnchor(flightSeat);
        ReportCardFx(flightOrigin, PileAnchor(fate));
        CardFlightLedger.Note("own", fate.ToString(),
            wasPickField ? "own-pick-field-commit" : "own-turn-clear",
            CardsGameApi.CardName(card.GameCard!));
        card.FlyToPile(worldPos, slabWidth, FlyToPileSeconds, arcUp, () =>
        {
            _flyingToPile.Remove(flying);
            _factory.Park(flying);
            VRLog.Info("Cards", $"Fly-to-pile: '{flying.name}' reached the {dest} pile — parked.");
        }, minArc);
        // A played card whose fate is BURNT (a lost action) is a burn like any other — tag it with
        // the same BURN ANIM token the dedicated burn paths use so ONE grep proves every burn case.
        string origin = wasPickField ? "pick field" : "turn-clear";
        string leftWhat = wasPickField
            ? "the board's PICK RECESS (an event discard the game just committed)"
            : "CCharacterClass.RoundAbilityCards/ExtraTurnCards";
        string tag = fate == PileKind.Burnt ? $"BURN ANIM [{origin}]" : $"Fly-to-pile [{origin}]";
        // HW-VERIFY: report item 5, the ORIGIN half. Grep token: FLIGHT ORIGIN. One line per real
        // flight (a handful per turn) and at Note tier because it is the OWNER'S half of a pair
        // whose other half prints on the OTHER machine — read it beside that peer's
        // '[Net] FLIGHT FACE' line for the same flight, which states the anchor the mirror flew
        // from. WORKING = "round recess 1"/"round recess 2" here and "Slot0 ->"/"Slot1 ->" there.
        // INERT = "the board CENTRE (no recess)" here on a turn-clear, which is the pre-461 picture
        // and means RecessSeatOfCard did not find the card parented at its recess seat. STILL BEYOND
        // THE INSTRUMENT = this line present with NO FLIGHT FACE line on the peer at all: the event
        // was dropped or swallowed by the burn mirror, which is not an origin defect.
        VRLog.Note("Cards", $"FLIGHT ORIGIN [{origin}]: this client's own card left "
            + (flightSeat >= 0
                ? $"round recess {flightSeat + 1}, and that seat is what went on the wire "
                  + $"({flightOrigin}) — every peer's mirrored flight now starts at their copy of "
                  + "that recess, which is the point this player is watching the card leave"
                : $"the board CENTRE (no recess — RecessSeatOfCard and SlotOf both answered -1), so "
                  + $"{flightOrigin} went on the wire and every peer's mirrored flight starts at "
                  + "their copy of the board centre instead. On a TURN-CLEAR that is the 2026-09-06 "
                  + "item 5 origin defect still standing")
            + $", flying to the {fate} stack.");
        VRLog.Info("Cards", $"{tag}: CARD FLIGHT '{CardsGameApi.CardName(card.GameCard!)}' " +
                            $"(owner '{(owner != null ? CardsGameApi.ActorLabel(owner) : "?")}') — WHY: it LEFT " +
                            $"{leftWhat} and ENTERED " +
                            $"CCharacterClass.{ModelListName(exit)}, so it flies to " +
                            $"the {fate} stack ({FlyToPileSeconds:F2}s, arc {arcHeight:F3} m over the board, " +
                            "orientation locked). Trigger and destination are both the authoritative model — a " +
                            "dock/focus change alone can never produce this line. VR presentation only; the game's " +
                            "own pile state is untouched.");
        return true;
    }

    /// <summary>The <c>CCharacterClass</c> list name behind an exit, for the flight/refusal log.</summary>
    private static string ModelListName(RoundCardExit exit) => exit switch
    {
        RoundCardExit.StillRound => "RoundAbilityCards/ExtraTurnCards",
        RoundCardExit.Discarded => "DiscardedAbilityCards",
        RoundCardExit.Lost => "LostAbilityCards",
        RoundCardExit.PermanentlyLost => "PermanentlyLostAbilityCards",
        RoundCardExit.Activated => "ActivatedCards",
        RoundCardExit.Hand => "HandAbilityCards",
        RoundCardExit.OffModel => "no CCharacterClass list (supply card consumed / actor gone)",
        _ => "no readable model",
    };

    /// <summary>The same list name for the SHARED hand-fan classifier
    /// (<see cref="CardsGameApi.HandExit"/>), which is the one the mirrored fan on a peer applies
    /// too. Two enums rather than one because <see cref="RoundCardExit"/> also answers "did this
    /// card FLY anywhere", a question the membership test has no opinion on.</summary>
    private static string ModelListName(CardsGameApi.HandExit exit) => exit switch
    {
        CardsGameApi.HandExit.Round => "RoundAbilityCards/ExtraTurnCards",
        CardsGameApi.HandExit.Discarded => "DiscardedAbilityCards",
        CardsGameApi.HandExit.Lost => "LostAbilityCards",
        CardsGameApi.HandExit.PermanentlyLost => "PermanentlyLostAbilityCards",
        CardsGameApi.HandExit.Activated => "ActivatedCards",
        _ => "HandAbilityCards",
    };

    /// <summary>
    /// REFUSAL LINE (user report 2026-08-08, bug 1): a docked round card left every zone but the
    /// model says it did not go to a pile — so there is NO flight, and that silence must be
    /// provable in the log rather than merely observed. One line per widget per verdict change
    /// (<see cref="_loggedFlightRefusal"/>), i.e. one per focus switch, never per frame: this whole
    /// path only runs from Rebuild, and only for the ≤2 cards the dock held last rebuild.
    /// </summary>
    private void LogFlightRefused(VRCard card, RoundCardExit exit, CPlayerActor? owner,
                                  bool fromPickField = false)
    {
        AbilityCardUI? widget = card.GameCard;
        if (widget == null)
            return;
        if (_loggedFlightRefusal.TryGetValue(widget, out RoundCardExit previous) && previous == exit)
            return;
        _loggedFlightRefusal[widget] = exit;

        string why = exit switch
        {
            RoundCardExit.StillRound =>
                "the card is STILL in the owner's RoundAbilityCards/ExtraTurnCards — nothing moved, the " +
                "round-card DOCK just changed its contents (a character focus switch does exactly that). " +
                "A flight here would be the reported bug.",
            RoundCardExit.Activated =>
                "the card was ACTIVATED (CCharacterClass.ActivatedCards) — a persistent/round-long card goes " +
                "to the ACTIVE COLUMN, not to a pile, so no pile flight exists to play.",
            RoundCardExit.Hand =>
                "the card went back to HandAbilityCards (the selection was undone) — it belongs in the hand " +
                "fan again, not in a pile.",
            RoundCardExit.OffModel =>
                "the card is in NO CCharacterClass list any more (a SUPPLY card is removed from the model when " +
                "used, CCharacterClass.cs:420-432) — there is no pile it entered.",
            _ =>
                "the model could not be read for this card (no CAbilityCard or no owning actor) — refusing " +
                "rather than guessing a pile.",
        };
        // The PICK FIELD refusal is a different sentence: the card left a board RECESS, not the
        // round-card dock, and a Lost/PermanentlyLost verdict there is not a defect at all — it is
        // this method deliberately conceding the card to the burn path so the game's own artwork
        // plays on it before it flies (TryStartBurnFly → TryTakeBurnFlightSlot).
        if (fromPickField)
        {
            string handover = exit is RoundCardExit.Lost or RoundCardExit.PermanentlyLost
                ? "That is a HANDOVER, not a failure: TryStartBurnFly owns a burn, and it holds the " +
                  "card lying in place until the game's burn artwork finishes before flying it to " +
                  "the Burnt stack."
                : why;
            VRLog.Info("Cards", $"Fly-to-pile REFUSED [pick field]: '{CardsGameApi.CardName(widget)}' (owner " +
                                $"'{(owner != null ? CardsGameApi.ActorLabel(owner) : "?")}') left the board's " +
                                "PICK RECESS but the model does NOT say Discarded — it says " +
                                $"{ModelListName(exit)}. {handover}");
            return;
        }
        VRLog.Info("Cards", $"Fly-to-pile REFUSED: '{CardsGameApi.CardName(widget)}' (owner " +
                            $"'{(owner != null ? CardsGameApi.ActorLabel(owner) : "?")}') left the round-card dock " +
                            $"but NOT for a pile — model says {ModelListName(exit)}. {why} The card is parked/faded " +
                            "in place instead, and nothing is announced to peers.");
    }

    /// <summary>
    /// Issue 5 / bug 1+2 (user 2026-08-08): where a docked round card stands in the game's
    /// AUTHORITATIVE model RIGHT NOW — the single source for both "may it fly at all" and "to which
    /// stack". Read-only: no game state is touched.
    ///
    /// <para>The card's OWN owner (<c>AbilityCardUI.PlayerActor</c>) is authoritative — in a
    /// two-character sequential turn a card cleared during the OTHER character's turn belongs to a
    /// different actor than the currently-presented hand, so its fate must be read from its own
    /// character's lists. Falls back to the presented hand if the widget has no owner.</para>
    /// </summary>
    private static RoundCardExit RoundCardExitOf(CardsHandUI? hand, VRCard card, out CPlayerActor? owner)
    {
        AbilityCardUI? widget = card.GameCard;
        CAbilityCard? ac = widget != null ? widget.AbilityCard : null;
        owner = widget != null ? widget.PlayerActor : null;
        if (owner == null && hand != null)
            owner = hand.PlayerActor;
        if (ac == null || owner == null)
            return RoundCardExit.NoModel;

        CCharacterClass klass = owner.CharacterClass;
        // ORDER: the "did it move at all" question first — everything below is a destination, and a
        // card that is still on the board has none.
        if (klass.RoundAbilityCards.Contains(ac) || klass.ExtraTurnCards.Contains(ac))
            return RoundCardExit.StillRound;
        if (klass.DiscardedAbilityCards.Contains(ac))
            return RoundCardExit.Discarded;
        if (klass.LostAbilityCards.Contains(ac))
            return RoundCardExit.Lost;
        if (klass.PermanentlyLostAbilityCards.Contains(ac))
            return RoundCardExit.PermanentlyLost;
        // ActivatedCards is the RAW List<CBaseCard> field (CCharacterClass.cs:97). The public
        // ActivatedAbilityCards property is a LINQ projection that allocates a new list on every
        // read — never call it from a rebuild path.
        if (klass.ActivatedCards.Contains(ac))
            return RoundCardExit.Activated;
        if (klass.HandAbilityCards.Contains(ac))
            return RoundCardExit.Hand;
        return RoundCardExit.OffModel;
    }

    /// <summary>
    /// Issue 5: the destination pile of a card that IS in a pile — a card sitting in the owner's
    /// LOST or PERMANENTLY-LOST list is a burned card (BURNT stack); everything else answers
    /// DISCARD. Kept as the burn watcher's question ("is this widget burnt in its owner's piles?",
    /// <see cref="IsFreshBurn"/>); the fly-to-pile TRIGGER does NOT use it, because "not burnt"
    /// must not be read as "therefore discarded" — see <see cref="RoundCardExitOf"/>.
    /// </summary>
    private static PileKind PileFateOf(CardsHandUI hand, VRCard card) =>
        RoundCardExitOf(hand, card, out _) switch
        {
            RoundCardExit.Lost or RoundCardExit.PermanentlyLost => PileKind.Burnt,
            _ => PileKind.Discard,
        };

    /// <summary>
    /// BURN ANIM: is this VR card PARKED in the factory pool (invisible, pose meaningless)?
    /// <see cref="VRCardFactory.Park"/> re-homes a card onto the pool root at the local origin, so a
    /// parked card's <c>transform.position</c> is the pool root's, NOT where the card last was. Every
    /// other parent — the hand fan (even a CLOSED, i.e. inactive, one), the tray, the half dock, the
    /// browse arc — keeps the card's true world pose, which is what a burn animation must fly from.
    /// </summary>
    private bool IsParked(VRCard card) => card.transform.parent == _factory.PoolRoot;

    /// <summary>
    /// BURN ANIM: has <paramref name="card"/> JUST been burned — i.e. is its ability card in the
    /// owner's LOST / PERMANENTLY-LOST list (<see cref="PileFateOf"/>, the game's own authoritative
    /// piles) while the burn watcher's previous-tick baseline (<see cref="_knownBurntWidgets"/>) did
    /// NOT yet contain its widget? Read-only on game state.
    ///
    /// The baseline is only trustworthy once <see cref="TickBurnToPile"/> has seeded it for THIS
    /// hand, so the check demands <c>hand == _burnWatchHand</c>: on a hand change (character tab
    /// switch, scenario load) the baseline is re-seeded silently there, and until that happened a
    /// hand's long-burned cards must never animate retroactively — the same "no storm" discipline
    /// the dock appear/disappear animations use.
    /// </summary>
    private bool IsFreshBurn(CardsHandUI? hand, VRCard card)
    {
        AbilityCardUI? widget = card.GameCard;
        if (widget == null || hand == null || !ReferenceEquals(hand, _burnWatchHand))
            return false;
        // OWNERSHIP GATE (user report 2026-08-07: "die verbrannte Karte taucht ploetzlich wieder
        // auf dem Board an derselben Stelle auf, obwohl der Charakter gewechselt wurde").
        //
        // ROOT CAUSE this line fixes. The baseline (_knownBurntWidgets) is re-seeded from the
        // PRESENTED hand's burnt pile on every hand change, and PileFateOf deliberately reads the
        // card's OWN owner. Without an ownership test the two disagree the instant characters
        // switch: character A's long-burned card is (a) not in B's freshly seeded baseline and
        // (b) still "lost" in A's own piles — so it re-qualified as a FRESH burn for hand B. The
        // park sweep then re-took ownership of it (TryStartBurnFly → artwork hold → "returns
        // true"), which by contract means "do not park, leave it lying exactly where it is", and
        // TickBurnToPile's hold hygiene dropped the hold again the same frame (the widget is not
        // in B's burnt buffer) — so the next Rebuild started a brand-new hold. That loop is the
        // reported reappearance, and the peer hardware log shows it verbatim: 17 consecutive
        // "BURN ANIM: holding 'ABILITY_CARD_GravelVortex' ON THE BOARD" lines (remote/LogOutput.log
        // 17442-18340) with no release, straddling a Cryonaris→Lastglowworm switch; Simulacrum the
        // same, and it even became grabbable in the hand fan again (16166 ff.).
        //
        // A burn belongs to the character that burned it. If the presented hand is not that
        // character's, this path has nothing to say about the card — it parks like any other
        // off-board card, and the OWNER's own pending flight was already flushed by
        // FlushBurnHolds when the hand changed.
        CPlayerActor? owner = widget.PlayerActor;
        if (owner != null && hand.PlayerActor != null && !ReferenceEquals(owner, hand.PlayerActor))
            return false;
        if (_knownBurntWidgets.Contains(widget))
            return false; // already in the burnt pile before this tick — not a fresh burn
        return PileFateOf(hand, card) == PileKind.Burnt;
    }

    /// <summary>
    /// BURN ANIM (user request): fly a card the game JUST burned into the BURNT stack with the very
    /// same over-the-board arc the discard flow uses (<see cref="VRCard.FlyToPile"/>) — short rest
    /// (random sacrifice), long rest (chosen burn) and damage-induced loss all funnel through here
    /// via their own call sites, so a burned card slides into the pile instead of blinking out.
    /// Purely VR presentation: the game already moved the card into its Lost pile by the time we see
    /// it, nothing here touches game data, and the card's own burn FX (the game's dissolve + the
    /// card-bounded CardSmoke, see <see cref="BurnCardFx"/>) keeps playing on the adopted face while
    /// it flies — no second effect is invented.
    ///
    /// Returns true when the burn path OWNS the card this frame — either the flight launched (the
    /// completion callback parks the card on arrival) or the flight is HELD while the game's burn
    /// artwork still plays ON the lying card (<see cref="TryTakeBurnFlightSlot"/>, the same gate
    /// the pile watcher uses — the user-ruled order: artwork first, THEN the pile flight). In the
    /// held case the caller must leave the card exactly where it lies (no park, no vanish); the
    /// per-tick watcher (<see cref="TickBurnToPile"/>) re-offers the widget every tick — it stays
    /// un-claimed while held — and launches the very same flight the moment the artwork completes
    /// or the deadline passes, from the card's true position, which this ownership preserved.
    /// Returns false — leaving the caller's park and <see cref="TickBurnToPile"/>'s transient-slab
    /// fallback to take over — when the card is held by a hand, already animating, no longer live
    /// (a pooled/parked or inactive card cannot animate: its Update does not run), not a fresh
    /// burn, or the burnt pile is off / not built.
    ///
    /// Claiming: on LAUNCH the widget is added to <see cref="_knownBurntWidgets"/> immediately, so
    /// the burn watcher running LATER in this same frame treats it as already-known and cannot
    /// start a second animation for it. A HELD widget is deliberately NOT claimed.
    /// </summary>
    private bool TryStartBurnFly(CardsHandUI? hand, VRCard card, string origin)
    {
        if (card.IsHeld || card.IsFlying || card.IsVanishing)
            return false;
        if (!card.gameObject.activeInHierarchy)
            return false; // inactive/parked: no Update ticks, so it could never animate
        if (!IsFreshBurn(hand, card))
            return false;
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out Vector3 burntPos, out float slabWidth))
            return false; // burnt pile off / not built — no destination to fly to

        AbilityCardUI widget = card.GameCard!;
        // ORDER OF THE BURN (same ruling as TryAnimateBurn): the park sweep can see the fresh burn
        // BEFORE the pile watcher does (Rebuild runs first in the frame, and the game commits the
        // pile move frames ahead of starting the card's burn timeline). Flying here immediately
        // would clip the artwork the user explicitly asked to watch on the lying card — so this
        // path waits behind the exact same bounded gate. While the gate holds, the card is OWNED:
        // it stays lying at its true pose (the sweep must not park it), and the watcher's per-tick
        // re-offer performs the launch when the artwork ends.
        if (!TryTakeBurnFlightSlot(widget, card))
            return true; // held: owned by the burn path — caller keeps hands off the card
        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        Vector3 from = card.transform.position;
        float arcHeight = Mathf.Max(minArc, Vector3.Distance(from, burntPos) * VRCard.FlyArcHeightFraction);
        _flyingToPile.Add(card);
        _knownBurntWidgets.Add(widget); // claim before the watcher's diff sees it (no double animation)
        _lastCardWorldPos.Remove(widget); // consumed — the real card is flying, no fallback slab wanted
        _lastCardWorldRot.Remove(widget);
        // MP parity (report 6): this launch site was the ONE burn flight that never reported —
        // every other pile flight (turn-clear, pile-watch, fallback slab) announces itself, so a
        // peer watching this player's board saw those but missed a burn that fired from the park
        // sweep. Same 2-byte semantic endpoint pair as the others.
        // …AND IT NAMES THE RECESS when the card is lying in one (item 5's origin half — read the
        // block at TryStartFlyToPile's ReportCardFx for why a hardcoded Board anchor made every
        // mirrored flight start at the board centre). `from` above is this same card's real world
        // position, so both ends of this flight now agree on both machines.
        ReportCardFx(SlotAnchor(_tray.RecessSeatOfCard(card)), Net.CardFxAnchor.Burnt);
        LogBurnAttribution(widget, "park-sweep");
        CardFlightLedger.Note("own", "Burnt", "own-burn/" + origin, CardsGameApi.CardName(widget));
        VRCard flying = card;
        card.FlyToPile(burntPos, slabWidth, FlyToPileSeconds, arcUp, () =>
        {
            _flyingToPile.Remove(flying);
            _factory.Park(flying);
            VRLog.Info("Cards", $"BURN ANIM: '{flying.name}' reached the Burnt pile — parked.");
        }, minArc);
        VRLog.Info("Cards", $"BURN ANIM [{origin}]: '{CardsGameApi.CardName(widget)}' burned — real VR card flies " +
                            $"from {from} → Burnt pile ({FlyToPileSeconds:F2}s, arc {arcHeight:F3} m over the " +
                            "board, orientation locked). VR presentation only; the game's own pile state is " +
                            "untouched.");
        return true;
    }

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-05 item 11a, "der Mitspieler sieht eine andere Karte die verbrannt wurde
    /// als ich"): name the card THIS client attributed the burn to. One line per launched burn (the
    /// launch sites claim the widget into <see cref="_knownBurntWidgets"/> first, so a burn cannot
    /// print twice), never per frame.
    ///
    /// <para>Grep token: <c>BURN CARD</c>. Its RECEIVER twin is <c>RemoteBurnFx</c>'s line with the
    /// same token, so ONE grep across the two hardware logs decides 11a: the owner's line and the
    /// peer's line for the same burn must name the same card. They are deliberately the same token
    /// and the same field order so the comparison needs no arithmetic.</para>
    ///
    /// <para>FALSIFIER: this line appearing on the owner with NO <c>BURN CARD</c> line on the peer
    /// at all means the peer's mirror never armed (board hidden, or the burnt-pile walk found
    /// nothing) — a different defect from the two lines disagreeing, which is the 1:1 breach.</para>
    /// </summary>
    private static void LogBurnAttribution(AbilityCardUI widget, string origin)
    {
        // HW-VERIFY: grep token "BURN CARD". Its twin is RemoteBurnFx's line with the same token on
        // the PEER; for one burn the two must name the same card. See this method's doc for the
        // falsifiers (different cards = 11a still open; no peer line at all = the mirror never armed).
        VRLog.Note("Cards", $"BURN CARD [owner/{origin}]: this client burned " +
                            $"'{CardsGameApi.CardName(widget)}'. This is the card whose face lies on the " +
                            "board through its burn artwork and then flies into the Burnt stack here. " +
                            "Every peer resolves the SAME card locally out of this character's " +
                            "host-replicated LostAbilityCards list (no identity rides the wire) and prints " +
                            "its own BURN CARD line — the two naming different cards IS the 1:1 breach.");
    }

    /// <summary>
    /// Issue A/B: the board's UP axis in world space — the direction a fly-to-pile bows so it arcs
    /// OVER the (possibly tilted) control board. Falls back to world-up before the tray exists.
    /// </summary>
    private Vector3 BoardUp() => _tray.Root != null ? _tray.Root.up : Vector3.up;

    // ------------------------------------------------------- pile ARRIVAL (the count defers) --
    //
    // USER REPORT (verbatim): "Wenn man gerade eine Karte abgeworfen oder verbrannt hat, sie aber
    // noch auf dem Controllboard liegt, wird aber schon der Pile aktualisiert. So kann es sein,
    // dass zwar im Pile '1' steht, wenn man ihn aber öffnen will nichts angezeigt wird. Das soll so
    // nicht sein — der Pile (und die Zahl darauf) soll sich erst unmittelbar aktualisieren, wenn
    // die jeweiligen Karten IN den Pile fliegen. So wird es nie einen '0er-Fächer' geben."
    //
    // THE MODEL AND THE TABLE DISAGREE ABOUT *WHEN*, AND BOTH ARE RIGHT. The rules engine moves a
    // card into CCharacterClass.Discarded/Lost/PermanentlyLostAbilityCards the instant the action
    // resolves (CCharacterClass.MoveAbilityCardToPile, :418 / DiscardRoundAbilityCards, :505) —
    // that is the RULES truth and the mod must never argue with it. On the VR table the same card
    // is still lying in a board slot, or burning where it lies, or arcing over the board. Reading
    // the model list for the stack LABEL therefore stated a fact about a pile the card had not
    // physically reached, and the browse arc — which deliberately refuses to borrow a visual the
    // board is still showing (BoardOwnsCardVisual, CardsDriver.6.Flows.cs) — then opened empty
    // under a label that said "1". The number and the fan were reading two different clocks.
    //
    // ONE PREDICATE FIXES BOTH, because both now ask the SAME question: a card is IN the pile once
    // its VR visual has ARRIVED there. Everything else — docked, held by the burn artwork, in
    // flight, shrinking out in place — is EN ROUTE and is subtracted from the count and skipped by
    // the arc. Count and contents can no longer disagree: they are computed from one classifier.
    //
    // IT IS SELF-HEALING BY CONSTRUCTION, NOT BY BOOKKEEPING. There is NO ledger, no latch, no
    // timer and no "remember that a flight started" table anywhere in this feature — every read is
    // recomputed from scratch, this frame, from live objects (PileViewer.TickStatus calls it per
    // frame). "Still en route" is therefore only ever true while an object is observably in that
    // state, so EVERY way a flight can end converges the count on the model on the very next frame:
    //   * it lands            → the completion callback parks the card → IsParked ⇒ arrived;
    //   * it is cancelled     → VRCard.Park calls VRCard.CancelFly ⇒ _flying false, and
    //                           the card is parked anyway ⇒ arrived;
    //   * the card is destroyed / recycled / the scenario is torn down → _factory.Find returns null
    //                           ⇒ arrived (no visual anywhere means it can only be in the pile);
    //   * a board switch      → RebuildBoard parks every card and clears the burn holds ⇒ arrived;
    //   * a hand/character switch → FlushBurnHolds LAUNCHES every held burn, and the launch lands
    //                           like any other ⇒ arrived;
    //   * the artwork hold    → bounded by BurnEffectMaxHoldSeconds (3 s) inside
    //                           TryTakeBurnFlightSlot, which then releases the flight;
    //   * a phase change / the browser opening mid-flight → neither is consulted at all, so neither
    //                           can strand the count.
    // The pathological case a ledger would have — an entry for a card nobody will ever land — is
    // not representable: there is nothing to leak. The worst reachable state is a card genuinely
    // still lying on the board, which is exactly the state the user asked us to wait for.
    //
    // THAT PARAGRAPH IS TRUE OF THIS FEATURE AND WAS CITED FOR ONE IT DOES NOT COVER (2026-09-07).
    // Every claim above is about PileArrivalsPending and CardEnRouteToPile, which RECOMPUTE from
    // live objects each frame — for them "there is nothing to leak" holds, because they never read
    // a membership without also asking IsFlying/IsParked/_factory.Find. It is NOT true of the
    // CONTAINERS themselves. `_flyingToPile` genuinely leaks: VRCard.CancelFly is documented "Does
    // NOT run the callback", SIX of the fourteen `_factory.Park` call sites do not drop the
    // membership, and CardEnRouteToPile's own doc forty lines below calls the set "a HINT, NEVER
    // THE TRUTH" for exactly that reason.
    //
    // THE COUNT WAS ELEVEN HERE UNTIL IT WAS RECOUNTED. Fourteen call sites, EIGHT of which drop
    // the membership; of the six that do not, `6.Flows.cs:2544` cannot be reached with a live
    // entry (an early return at :2520 stands in front of it), so FIVE are genuine. The leak the
    // reader should go to first is Rebuild's own zone loop at :1458 —
    // `card.Vanish(() => _factory.Park(vanishing))` — because `VRCard.Vanish` short-circuits on
    // `_flying`, so CancelFly runs and that callback never fires at all. And the reads a stale
    // entry actually costs are `TryAnimateBurn`'s `ownedElsewhere` and `LaunchBurnFlight`: both
    // silently downgrade a real burn to the anonymous slab. None of that changes the paragraph's
    // conclusion — the set leaks and a raw `Count` read may not be trusted — which is why the
    // pruning gate below stands as written. CardsDriver.BoardStillOwnsACardsExit (6.Flows) used to
    // read `_flyingToPile.Count` RAW and cited this paragraph as its proof that it could not
    // latch; on 2026-09-07 it latched the wanted-slot overlays off at `held=57.92s` and never let
    // go — two stale memberships from a discard cluster ~57 s earlier. A self-healing ARGUMENT
    // does not transfer to a call site that does not do the recomputation; that gate now prunes.
    // Four of that session's nine locally-animated flight starts never logged a "reached the …
    // pile — parked" line at all, so the leak is a live producer and not a one-off.

    /// <summary>Reused resolve buffer for <see cref="PileArrivalsPending"/> — the per-frame count
    /// query allocates nothing.</summary>
    private readonly List<AbilityCardUI> _arrivalWidgetBuffer = new(16);

    /// <summary>
    /// Is this card physically ON ITS WAY into a pile right now — i.e. does the mod still own its
    /// movement? Three live states, each of which ends on its own (see the region header):
    /// mid-flight (<see cref="TryStartFlyToPile"/> / <see cref="TryStartBurnFly"/> /
    /// <see cref="LaunchBurnFlight"/>), shrinking out in place (the dock vanish, which parks itself
    /// at the end), or lying on the board inside the BURN-ARTWORK HOLD that
    /// <see cref="TryTakeBurnFlightSlot"/> keeps it in while the game's own burn timeline plays.
    ///
    /// <para><c>_flyingToPile</c> membership is asked ALONGSIDE <c>IsFlying</c> rather than instead
    /// of it: the set can hold a stale entry for a card something else parked (Park cancels the fly
    /// without running the completion callback — see RebuildBoard), so it is a hint, never the
    /// truth. <c>IsFlying</c> is the truth, and a stale-set card is only ever ALSO parked, which
    /// the caller checks.</para>
    /// </summary>
    private bool CardEnRouteToPile(VRCard? card)
    {
        if (card == null)
            return false;
        if (card.IsFlying || _flyingToPile.Contains(card))
            return true;
        if (card.IsVanishing)
            return true; // shrinking out where it lay; it parks itself at the end (≤ DockVanishSeconds)
        AbilityCardUI? widget = card.GameCard;
        return widget != null && _burnHoldSince.ContainsKey(widget);
    }

    /// <summary>
    /// How many of this character's cards the MODEL already lists in <paramref name="kind"/> but
    /// whose VR visual has NOT arrived in that stack yet — the number
    /// <c>PileViewer.TickStatus</c> subtracts from the model count so the label becomes true at the
    /// moment the card LANDS rather than at the moment the rules moved it.
    ///
    /// <para>Never negative, never larger than the pile: it is a count of members of the pile's own
    /// widget list (<c>CardsGameApi.GetPileWidgets</c> — the same call, in the same order, that
    /// fills the browse arc and a peer's mirrored arc), so the subtraction cannot underflow. A
    /// half-torn hand answers 0, which defers nothing and shows the plain model number — the safe
    /// direction, because a count that is merely EARLY is the pre-existing behaviour while a count
    /// that never arrives would be a new bug.</para>
    /// </summary>
    private int PileArrivalsPending(CardsHandUI? hand, PileKind kind)
    {
        if (hand == null || hand.PlayerActor == null)
            return 0;
        // CHEAP EXIT FIRST — this runs twice per frame from PileViewer.TickStatus, and
        // GetPileWidgets is an O(pile × cardsUI) scan. Nothing can be en route unless the mod is
        // holding at least one card OUTSIDE the pool: a flight, a burn hold, a docked round card, a
        // play-slot occupant, a pick-field card, the short-rest sacrifice OR THE ACTIVE COLUMN. In
        // the steady state (an enemy turn, a hand being read, the whole card-selection phase) every
        // one of these is empty and the query costs seven compares.
        //
        // THE ACTIVE COLUMN USED TO BE ABSENT HERE, AND THE SENTENCE THAT EXCLUDED IT WAS FALSE.
        // It read: "an activated card is in ActivatedCards, never in a discard/lost list, so it can
        // never be a member of the set this method counts." An active card that BURNS OUT is listed
        // in LostAbilityCards while its visual is still standing in the column — so it is a member
        // of exactly this set, and the early-out returned 0 for it. The loop body was always right:
        // BoardOwnsCardVisual (CardsDriver.6.Flows.cs) has five terms and _active.Contains is the
        // fifth. Only the cheap exit disagreed with it, and a cheap exit that is narrower than the
        // body it guards is the body's blind spot. USER REPORT 2026-09-07 item 4b, verbatim: "Im
        // Stapel steht eine '2' aber der Fächer zeigt nur eine Karte (remote und lokal)" — the
        // badge subtracts this number, so under-counting it by one prints the model count while the
        // card is still visibly in the column, on BOTH boards, which is why neither view agreed
        // with the badge and both agreed with each other.
        if (_flyingToPile.Count == 0 && _burnHoldSince.Count == 0 && _halfBuffer.Count == 0
            && _fieldCards.Count == 0 && _shortRestCard == null && _active.Cards.Count == 0
            && _tray.Occupant(0) == null && _tray.Occupant(1) == null)
            return 0;
        try
        {
            CardsGameApi.GetPileWidgets(hand, kind == PileKind.Burnt, _arrivalWidgetBuffer);
            int pending = 0;
            for (int i = 0; i < _arrivalWidgetBuffer.Count; i++)
            {
                AbilityCardUI widget = _arrivalWidgetBuffer[i];
                if (widget == null || widget.AbilityCard == null || widget.IsLongRest)
                    continue;
                // The hold is keyed on the WIDGET and can outlive the VR card (the fallback-slab
                // path burns a card whose visual was already recycled), so it is asked first.
                if (_burnHoldSince.ContainsKey(widget))
                {
                    pending++;
                    continue;
                }
                VRCard? card = _factory.Find(widget);
                if (card == null)
                    continue; // no visual anywhere ⇒ nothing is on its way ⇒ it IS in the pile
                if (CardEnRouteToPile(card))
                {
                    pending++;
                    continue;
                }
                if (IsParked(card))
                    continue; // pooled: the flight (if any) is over and the card is in the stack
                // Still lying in a board zone the rebuild is showing — the docked round cards, a
                // play slot, the pick field, the short-rest recess, the active column. This is the
                // literal case the report opens with ("sie aber noch auf dem Controllboard liegt").
                // NOTE the browse arc is deliberately NOT one of those zones: a card lying in the
                // fan has arrived — it is being read OUT of the pile, not carried INTO it.
                if (BoardOwnsCardVisual(card))
                    pending++;
            }
            return pending;
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// <see cref="PileArrivalsPending"/> for the pile surfaces, which are private members of this
    /// driver and must not thread an instance through the Cards module. No driver (flat play /
    /// pre-build) ⇒ 0, i.e. the plain model count.
    /// </summary>
    internal static int PendingPileArrivals(CardsHandUI? hand, PileKind kind) =>
        Instance != null ? Instance.PileArrivalsPending(hand, kind) : 0;

    // ------------------------------------------------------------ pick restart return flight --

    /// <summary>
    /// PICK RESTART — the pages that already flew into the discard stack arc back OUT of it.
    ///
    /// <para>USER REPORT 2026-08-24 (verbatim): "Der Knopf am Ende 'Wähle eine andere Karte' Flow
    /// funktioniert nicht. Wird der gedrückt soll die Auswahl auf der ersten Seite nochmal komplett
    /// von anfang an beginnen. Am Besten mit einer kleinen Animation, weil ja bereits zwei Karten in
    /// den jeweiligen pile geflogen sind."</para>
    ///
    /// <para>THE FLIGHT IS THE EXISTING ONE, PLAYED IN REVERSE. <see cref="VRCard.FlyFromPile"/> is
    /// the exact mirror of the <see cref="VRCard.FlyToPile"/> that <c>FlyLockedPicksToPile</c> used
    /// to put these cards in the stack — same duration (<see cref="FlyToPileSeconds"/>), same
    /// world-up arch with the same <see cref="BoardArcMin"/> floor, same locked orientation, same
    /// grow-from-slab-width scale ramp read backwards. It is the very call the short-rest sacrifice
    /// already uses to come out of the discard pile (CardsDriver.5.Interactions.cs:1417), so nothing
    /// new animates and no second animation system exists.</para>
    ///
    /// <para>WHY IT RUNS HERE AND NOT AT THE BUTTON. <c>FlyFromPile</c> flies to the card's HOME
    /// pose, which only exists once a layout has asserted one — and at cancel time these cards are
    /// parked on the (inactive) pool root with no home at all. <see cref="CardFan.SetCards"/>, one
    /// line above the call site, is what gives each returning card its seat; this drains the list
    /// immediately afterwards, exactly as the docked-card APPEAR pass below waits for
    /// <c>_half.SetCards</c>.</para>
    ///
    /// <para>WHAT IT REFUSES, out loud rather than silently. A card that is HELD or already
    /// animating has another owner, and with no discard stack built there is no origin to fly from.
    /// Each of those is logged with its reason and the card simply IS back in the hand, un-animated
    /// — never teleported to a wrong spot, and never left mid-air.</para>
    ///
    /// <para>A CLOSED HAND FAN IS NO LONGER ONE OF THEM (user report 2026-08-24: "Die Animation …
    /// spielt nur ab wenn der Fächer aktuell auch auf ist. Das soll nicht sein … als wäre er auf").
    /// A closed fan parents its cards under a DISABLED root, so <c>VRCard.Update</c> never ticked
    /// and the flight could not run — this method printed "RETURN REFUSED … not live in the
    /// hierarchy" for every card (LogOutput.log:4047-4048, 4351-4352). It now asks
    /// <see cref="CardFan.TrySeatArrival"/> for a live seat instead: the card hangs off a mod-owned,
    /// always-active transform at the hand for the duration of the flight and is re-homed into the
    /// fan when it lands — the same ownership shape the OUTBOUND flight has always had (it flies
    /// while still parented to the live tray recess and only re-homes on arrival). The fan's state
    /// is not faked and nothing of the game is touched; the card is simply not under the fan root
    /// while it is in the air. Opening or closing the fan mid-flight is safe in both directions —
    /// see <c>CardFan.TickArrivals</c>.</para>
    ///
    /// <para>MULTIPLAYER: announced through the same two-byte semantic anchor pair every other
    /// flight uses (<c>Discard → HandFan</c>, the reverse of the exit flight's <c>Slot → Discard</c>).
    /// No card identity, no new channel, no game state written — the deselection that put these
    /// cards back in the hand is the GAME's own cancel callback and the game replicates it.</para>
    /// </summary>
    private void DrainPickReturnFlight()
    {
        if (_pickReturnFlight.Count == 0)
            return;
        bool havePile = _piles.TryGetPileWorld(PileKind.Discard, out Vector3 pilePos, out float slabWidth);
        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        int flew = 0, skipped = 0;
        for (int i = 0; i < _pickReturnFlight.Count; i++)
        {
            VRCard card = _pickReturnFlight[i];
            if (card == null)
            {
                skipped++;
                VRLog.Info("Cards", "Pick restart RETURN REFUSED: the VR card is gone (its widget was " +
                                    "recycled under the restart) — nothing to fly.");
                continue;
            }
            // DEFECT B (user 2026-08-24: "auch wenn der Fächer aktuell nicht auf ist soll die
            // Animation abspielen als wäre er auf"). This used to REFUSE a card that was not live in
            // the hierarchy, and a CLOSED hand fan parents every one of its cards under a disabled
            // root — so the whole animation was silently skipped whenever the fan happened to be
            // down (LogOutput.log:4047-4048, 4351-4352). It is no longer a refusal but a SEAT: the
            // fan hands the card a live, always-active arrival transform at the hand seat an open
            // fan's root would occupy, and re-homes it into the fan when it lands. Same shape as the
            // OUTBOUND flight, which flies while the card still hangs off the live tray recess and
            // only changes owner on arrival (FlyLockedPicksToPile → _factory.Park).
            bool wasDormant = !card.gameObject.activeInHierarchy;
            string? seatRefusal = null;
            // Seated LAST of the three cheap tests and only once every other refusal has passed, so
            // a card that will not fly is never re-parented away from the fan for nothing.
            bool seatable = !card.IsHeld && !card.IsFlying && !card.IsVanishing && havePile
                            && _fan.TrySeatArrival(card, out seatRefusal);
            string? refusal =
                card.IsHeld ? "the player is holding it — the hand owns the pose"
                : card.IsFlying || card.IsVanishing ? "another animation already owns it"
                : !havePile ? "the discard stack is not built / not visible, so there is no origin to " +
                              "fly out of"
                : !seatable ? seatRefusal
                : null;
            if (refusal != null)
            {
                skipped++;
                VRLog.Info("Cards", $"Pick restart RETURN REFUSED: '{card.name}' stays where it is — " +
                                    $"{refusal}. The game's own cancel already put the card back in the " +
                                    "hand; only the animation is skipped.");
                continue;
            }
            card.FlyFromPile(pilePos, slabWidth, FlyToPileSeconds, arcUp, minArc);
            ReportCardFx(Net.CardFxAnchor.Discard, Net.CardFxAnchor.HandFan);
            // ROW 9 OF THE FLIGHT INVENTORY, and it was a hole in the ledger until 2026-09-07. This
            // is the one own-board producer that flies OUT of a pile into the hand, so the ledger's
            // pile-shaped destination vocabulary had no word for it and the flight was invisible to
            // the audit that the user's "Bestandsaufnahme aller Flüge" asked for. It is CORRECT
            // behaviour — the game's own cancel put the card back and this is the reverse of the
            // batch exit flight — but a correct flight still has to be countable.
            CardFlightLedger.Note("own", "HandFan", "own-pick-restart-return", card.name);
            flew++;
            VRLog.Info("Cards", $"Pick restart RETURN: CARD FLIGHT '{card.name}' — WHY: the game's own " +
                                "\"choose another card\" (DialogPopup cancel option) reopened the whole event " +
                                "discard, so the page that had already flown into the Discard stack comes back " +
                                $"OUT of it into the hand ({FlyToPileSeconds:F2}s, arc over the board, " +
                                "orientation locked) — the exact reverse of the Pick batch EXIT flight that " +
                                $"put it there. The selection restarts at page 1. HAND FAN " +
                                $"{(_fan.IsOpen ? "OPEN" : "CLOSED")}, card was " +
                                (wasDormant ? "DORMANT (not live in the hierarchy — the exact state that used " +
                                              "to print 'Pick restart RETURN REFUSED')"
                                            : "already live") + ": " +
                                (_fan.IsArriving(card)
                                    ? "it rides the fan's own live ARRIVAL SEAT at the hand for the flight — " +
                                      "the same ownership shape as the outbound flight, which flies while still " +
                                      "parented to the live tray recess — and is re-homed into the fan when it " +
                                      "lands, open or closed (user ruling 2026-08-24: the animation plays 'als " +
                                      "wäre er auf')."
                                    : "it flies to its own arc seat under the open fan root, unchanged.") +
                                " VR presentation only — the deselection is the game's own cancel callback.");
        }
        _pickReturnFlight.Clear();
        if (flew > 0)
        {
            // Re-arm ONE rebuild for when the flight lands — see _pickReturnSettleAt. The margin is
            // over the flight's REAL duration, which VRCard floors at MinFlySeconds.
            _pickReturnSettleAt = Time.unscaledTime
                                  + Mathf.Max(VRCard.MinFlySeconds, FlyToPileSeconds) + 0.05f;
        }
        if (flew > 0 || skipped > 0)
            VRLog.Info("Cards", $"Pick restart RETURN: {flew} card(s) flew back out of the Discard stack, " +
                                $"{skipped} skipped. The pick is back at page 1 with both recesses empty " +
                                $"(locked batches {_pickLockedCount}, field {_fieldCards.Count}).");
    }

    /// <summary>
    /// The other half of <see cref="DrainPickReturnFlight"/>: give the returned cards their
    /// affordances back the moment the reverse flight lands.
    ///
    /// <para>WHY IT IS NEEDED AT ALL. <c>Rebuild</c>'s per-card zone stamp skips a flying card
    /// outright (a flight owns its transform), and <see cref="VRCard.FlyFromPile"/> clears
    /// <c>Grabbable</c> for the duration — so unless a rebuild happens AFTER the landing, a card
    /// that just flew back out of the discard stack sits in the fan un-grabbable, and the restart
    /// fails at its last step. <c>FlyFromPile</c> takes no completion callback (its fly-IN branch
    /// settles at home and returns), so the trigger is an unscaled deadline over the flight's own
    /// fixed duration rather than a per-card watch.</para>
    ///
    /// <para>One-shot: the deadline is consumed when it fires. A grab, a drop, a mode change or any
    /// other dirty edge in the meantime simply rebuilds earlier and this fires harmlessly on top
    /// (Rebuild is idempotent).</para>
    /// </summary>
    private void TickPickReturnSettle()
    {
        if (_pickReturnSettleAt <= 0f || Time.unscaledTime < _pickReturnSettleAt)
            return;
        _pickReturnSettleAt = 0f;
        _dirty = true;
        VRLog.Info("Cards", "Pick restart RETURN: the reverse flight has landed — rebuilding so the " +
                            "returned card(s) get their grab/poke affordances back (a flying card is " +
                            "skipped by the zone stamp and FlyFromPile drops Grabbable for the " +
                            "flight). The pick is choosable again from page 1.");
    }

    // ---------------------------------------------------------------- MP card-FX anchors --
    //
    // Report 6 ("ALLE Kartenanimationen der Mitspieler sollen im Multiplayer sichtbar sein"): every
    // card animation the local VR launches is announced to peers as a SEMANTIC endpoint pair
    // (Net.NetCardFx), which they replay against their own copy of this player's board/hand pose.
    // These two helpers translate the driver's local notions — a slot index, a PileKind — into the
    // wire anchors. A card whose slot is unknown (-1: never docked, or already evicted) degrades to
    // the generic Board anchor, which flies from the board centre rather than nowhere.

    /// <summary>
    /// Announce a local card animation to peers — EXCEPT while the board is showing a character
    /// under a read-only focus.
    ///
    /// <para>WHY THE EXCEPTION (character focus, slot cards 2026-08-08). Every anchor on this wire
    /// is a POSITION on the SENDER'S OWN board ("slot 0 → discard stack"), which peers replay
    /// against their copy of that board. Under a read-only focus the local board is showing SOMEBODY
    /// ELSE'S cards, so the flights it plays belong to the watched character's turn, not to ours —
    /// forwarding them would make a peer's mirror of OUR board animate cards that were never on it.
    /// The dock now fills for a focused character that is not even at turn, so those clears happen
    /// far more often than before; suppressing here keeps the whole feature a LOCAL view change, and
    /// the owning client still reports its own flights on its own board exactly as before.</para>
    /// </summary>
    private static void ReportCardFx(Net.CardFxAnchor from, Net.CardFxAnchor to)
    {
        if (Board.CharacterFocus.ReadOnlyView)
            return;
        Net.NetCardFx.Report(from, to);
    }

    /// <summary>Wire anchor for a board slot index (-1 → the generic board anchor).</summary>
    private static Net.CardFxAnchor SlotAnchor(int slot) => slot switch
    {
        0 => Net.CardFxAnchor.Slot0,
        1 => Net.CardFxAnchor.Slot1,
        _ => Net.CardFxAnchor.Board,
    };

    /// <summary>Wire anchor for a destination pile stack.</summary>
    private static Net.CardFxAnchor PileAnchor(PileKind kind) =>
        kind == PileKind.Burnt ? Net.CardFxAnchor.Burnt : Net.CardFxAnchor.Discard;

    /// <summary>
    /// Issue 3 (user): the absolute MINIMUM arc peak (world meters) a fly-to/from-pile must reach so
    /// the card visibly clears the control board's top edge even on a SHORT hop — a flat skim was
    /// unreadable. Scaled by the board's live diorama scale (so it tracks board size/config) at
    /// ~1.5 card-heights of lift. The fly takes the max of this and its distance-proportional arc.
    /// </summary>
    private float BoardArcMin()
    {
        float boardScale = _tray.Root != null ? _tray.Root.lossyScale.x : 1f;
        return boardScale * CardsConfig.CardHeight * 1.5f;
    }

    /// <summary>
    /// Issue B (user, "when I burned a card due to damage I didn't perceive the animation"):
    /// watch the acting hand's BURNT pile membership each tick and animate any card that newly
    /// entered it — the take-damage burn ("burn available / discarded card") and any other
    /// lose-to-burnt path — flying into the burnt stack with the same over-the-board arc as the
    /// turn-clear sweep. The turn-clear round-card fly (<see cref="TryStartFlyToPile"/>) already
    /// owns its cards, so this skips anything it is handling (held / flying / in
    /// <see cref="_lastHalfCards"/>) — no double animation. A hand change re-seeds the baseline
    /// silently so a hand's already-burnt cards never animate retroactively.
    /// </summary>
    private void TickBurnToPile(CardsHandUI? hand)
    {
        if (hand == null)
        {
            // Same stranding risk as the hand CHANGE below: the hold owns the card, so losing the
            // presented hand while one is pending would leave it lying (or let the park sweep
            // swallow it silently). Land it instead.
            FlushBurnHolds("the presented hand went away (no local hand to watch)");
            _burnWatchHand = null;
            return;
        }
        CardsGameApi.GetPileWidgets(hand, burnt: true, _burntWidgetBuffer);

        // Re-baseline on a hand change (or first sight): record the current burnt set WITHOUT
        // animating — only cards that cross into it from here on are freshly burned.
        if (!ReferenceEquals(hand, _burnWatchHand))
        {
            // FLUSH, DO NOT DROP (user report 2026-08-07, the "burned card lies on the board
            // forever" half of the reappearance bug). This used to `_burnHoldSince.Clear()`, which
            // silently threw away a burn flight that was mid-artwork-hold. The card was NOT parked
            // (the hold's contract is "the burn path owns it, leave it lying"), and with its hold
            // gone nothing ever launched it — so the previous character's burned card simply stayed
            // on the board. Switching character is exactly when this happens: the take-damage panel
            // closing hands the presented hand straight back to whoever's turn it is, typically a
            // DIFFERENT character, one to two frames after the burn commits.
            //
            // A pending hold is a burn the player already watched; it must land. FlushBurnHolds
            // launches each one immediately (deadline semantics — the artwork has had its moment)
            // so the card flies into the burnt stack and parks, exactly as if the hold had timed
            // out with the same hand still presented. Peers see it too: the flush goes through the
            // same launch sites, so the same Board→Burnt NetCardFx event rides the wire.
            FlushBurnHolds("the presented hand changed to " +
                           $"'{(hand.PlayerActor != null ? CardsGameApi.ActorLabel(hand.PlayerActor) : "?")}'");
            _burnWatchHand = hand;
            _knownBurntWidgets.Clear();
            _burnHoldLogged.Clear();
            for (int i = 0; i < _burntWidgetBuffer.Count; i++)
                if (_burntWidgetBuffer[i] != null)
                    _knownBurntWidgets.Add(_burntWidgetBuffer[i]);
            return;
        }

        for (int i = 0; i < _burntWidgetBuffer.Count; i++)
        {
            AbilityCardUI widget = _burntWidgetBuffer[i];
            if (widget == null || _knownBurntWidgets.Contains(widget))
                continue;
            TryAnimateBurn(widget); // newly entered the burnt pile this tick
        }

        // The rebuilt baseline both records the new arrivals (so they animate exactly once) and
        // drops any that left (a recovered lost card), so a re-burn later animates again.
        //
        // ROOT CAUSE of the "burn artwork plays, then the card just VANISHES" report
        // (user 2026-08-04; hardware log: "BURN ANIM: holding ... ON THE BOARD" at 11379/13331/
        // 17950/19039 with NOT ONE "waited ... flying to the Burnt pile now" release line in the
        // whole session): this very re-baseline used to add EVERY burnt widget — including one
        // whose flight TryTakeBurnFlightSlot had just DECLINED to keep the artwork playing on the
        // lying card. The next tick's loop above then skipped it as already-known, so
        // TryAnimateBurn was never re-offered, the hold never released, and the card was left for
        // a later Rebuild park sweep to swallow (IsFreshBurn false → the Vanish/Park fallback) —
        // the reported disappearance. A widget whose flight is still HELD must therefore stay OUT
        // of the baseline: it remains "fresh", the watch re-offers it every tick, and when the
        // artwork completes (or the deadline passes) the release actually launches the same
        // FlyToPile arc the discard flow uses.
        _knownBurntWidgets.Clear();
        for (int i = 0; i < _burntWidgetBuffer.Count; i++)
        {
            AbilityCardUI w = _burntWidgetBuffer[i];
            if (w != null && !_burnHoldSince.ContainsKey(w))
                _knownBurntWidgets.Add(w);
        }

        // Hold hygiene: a held widget that LEFT the burnt pile again (a recovered lost card —
        // possible before its flight ever released) has nothing left to fly; drop its hold so
        // neither dictionary accumulates dead widgets across a scenario.
        if (_burnHoldSince.Count > 0)
        {
            _burnHoldPruneScratch.Clear();
            foreach (AbilityCardUI held in _burnHoldSince.Keys)
                if (!_burntWidgetBuffer.Contains(held))
                    _burnHoldPruneScratch.Add(held);
            for (int i = 0; i < _burnHoldPruneScratch.Count; i++)
                ClearBurnHold(_burnHoldPruneScratch[i]);
        }
    }

    /// <summary>
    /// Seconds the flight waits for the game's burn artwork to START before giving up on it (the
    /// pile count commits several frames earlier — see <see cref="TryAnimateBurn"/>).
    ///
    /// <para>NOT A VALUE ANY MORE, AN ALIAS. The number lives once, in
    /// <see cref="BurnArtwork.StartGraceSeconds"/>, because the peer's mirror
    /// (<c>RemoteBurnFx</c>) has to hold a burn for exactly as long as this board does — "Das soll
    /// so synchron mit den anderen Spielern sein". Two constants that merely happened to agree is
    /// what the ModBuild 474 logs measured going wrong (+0.33 / −0.48 / −1.45 s over three burns);
    /// an alias cannot drift. The NAME stays because <c>CardsDriver.6.Flows.cs</c> names it in
    /// prose and that prose must not start pointing at a symbol that is gone.</para>
    /// </summary>
    private const float BurnEffectStartGraceSeconds = BurnArtwork.StartGraceSeconds;

    /// <summary>Hard ceiling on the whole wait: however long the artwork runs, a burned card is on
    /// its way to the pile after this. A stranded card on the board is worse than a clipped
    /// animation. <inheritdoc cref="BurnEffectStartGraceSeconds" path="/summary/para"/></summary>
    private const float BurnEffectMaxHoldSeconds = BurnArtwork.MaxHoldSeconds;

    /// <summary>
    /// One burned widget's pending artwork hold: when it started, and whether this client has ever
    /// actually SEEN the game's burn artwork run on it.
    ///
    /// <para>THE SECOND FIELD EXISTS BECAUSE THE LOG LIED (2026-09-07 round, item 8's tail). The
    /// release condition <c>effectActive == false</c> is reached by two completely different
    /// histories — the artwork FINISHED, or it never started at all — and <c>BURN HOLD</c> printed
    /// "artwork finished" for both. ModBuild 474's peer log has the pair one entry apart:
    /// <c>BURN ANIM: holding 'ABILITY_CARD_SpareDagger' … (effect not started yet)</c> at 177184
    /// and <c>BURN HOLD: … waited 0,50s … (artwork finished)</c> at 177185. 0.50 s IS
    /// <see cref="BurnEffectStartGraceSeconds"/>, so the short-rest sacrifice never waited for an
    /// artwork at all and the line said the opposite. A reader had to hold two lines in their head
    /// to see it; now one line names the arm.</para>
    ///
    /// <para>IT RIDES IN THE HOLD ENTRY rather than in a set of its own so that every existing
    /// clear of <see cref="_burnHoldSince"/> clears it too — including the two in
    /// <c>CardsDriver.2.Update.cs</c> (:331, :469), a file this change may not touch. A parallel
    /// <c>HashSet</c> would have accumulated dead widgets for a whole scenario at exactly those two
    /// seams.</para>
    /// </summary>
    private readonly struct BurnHold
    {
        internal BurnHold(float since, bool artworkSeen)
        {
            Since = since;
            ArtworkSeen = artworkSeen;
        }

        /// <summary>Unscaled time the hold began — the pile-count edge, not the artwork's start.</summary>
        internal float Since { get; }

        /// <summary>Has <see cref="BurnArtworkActive"/> ever read TRUE for this widget during this
        /// hold? False at release means the artwork never ran here and the grace arm let it go.</summary>
        internal bool ArtworkSeen { get; }
    }

    /// <summary>Burned widgets whose flight is being held back, and the state of that hold.</summary>
    private readonly Dictionary<AbilityCardUI, BurnHold> _burnHoldSince = new();

    /// <summary>Widgets whose hold has been logged once (one line per burn, not per frame).</summary>
    private readonly HashSet<AbilityCardUI> _burnHoldLogged = new();

    /// <summary>Reused scratch for pruning stale hold entries (allocation-free steady state).</summary>
    private readonly List<AbilityCardUI> _burnHoldPruneScratch = new(4);

    /// <summary>
    /// Drop all artwork-hold state for <paramref name="widget"/>. Called when a flight for the
    /// widget actually launches on ANOTHER path (the turn-clear sweep consumes the burn), when the
    /// widget leaves the burnt pile again (recovered), and when a board switch parks its card —
    /// so a consumed hold can never release a SECOND animation later (the release gate itself
    /// clears these two on a normal in-path release).
    /// </summary>
    private void ClearBurnHold(AbilityCardUI widget)
    {
        _burnHoldSince.Remove(widget);
        _burnHoldLogged.Remove(widget);
    }

    /// <summary>
    /// LAND every burn flight that is still sitting in the artwork hold, right now, and clear the
    /// hold table. The hold's whole contract is "the burn path OWNS this card — the caller must
    /// leave it lying exactly where it is"; so anything that invalidates the hold WITHOUT landing
    /// the flight strands a burned card on the board forever (the reported reappearance). The one
    /// event that used to do that is a presented-hand change: <see cref="TickBurnToPile"/> simply
    /// cleared the table, the widget was no longer offered (it is not in the NEW hand's burnt
    /// pile), and nothing ever launched it. Now the switch flushes instead — each pending burn
    /// flies to the burnt stack immediately, which is what the artwork deadline would have done a
    /// moment later anyway.
    ///
    /// Called with the OLD baseline still in place, so the launch sites see the same world they
    /// were held in. Safe to call with an empty table (no-op, no log).
    /// </summary>
    private void FlushBurnHolds(string reason)
    {
        if (_burnHoldSince.Count == 0)
            return;
        _burnHoldPruneScratch.Clear();
        foreach (AbilityCardUI held in _burnHoldSince.Keys)
            if (held != null)
                _burnHoldPruneScratch.Add(held);
        _burnHoldSince.Clear();
        _burnHoldLogged.Clear();
        for (int i = 0; i < _burnHoldPruneScratch.Count; i++)
        {
            AbilityCardUI widget = _burnHoldPruneScratch[i];
            // ─── AND THE GATE BYPASS BELOW IS DELIBERATE AND CORRECT (2026-09-07 round, item 8) ──
            // The round asked whether EVERY producer waits, and this one does not — LaunchBurnFlight
            // skips TryTakeBurnFlightSlot entirely. That is not the oversight the other producer was
            // (TryStartFlyToPile, which now waits): making a FLUSH wait would strand the card
            // FOREVER, and the mechanism is exact. A flush runs because the presented hand is
            // changing or going away, and the only thing that re-offers a held widget is
            // TickBurnToPile walking the PRESENTED hand's burnt pile. One frame later this widget is
            // not in that pile, so nothing would ever call the gate again, nothing would release the
            // hold, and the card would lie on the board for the rest of the scenario. That exact
            // sequence is the 2026-08-07 report ("die verbrannte Karte … bleibt liegen"), which is
            // why the flush exists at all. Deadline semantics is the honest reading: the artwork has
            // had whatever moment it was going to get.
            //
            // WHAT IT COSTS, STATED RATHER THAN HIDDEN. A flush is the ONE burn whose owner and
            // mirror cannot release on the same term, because the trigger — this client's own 2D
            // card-UI focus changing — is local presentation the rules model does not hold. The
            // mirror closes it from the other end instead, with no new wire field: the owner's
            // presented character already rides extension record 22, so RemoteBurnFx.Watch sees the
            // same edge and flushes its own presentations on it (see its FOCUS FLUSH region). Same
            // event, both sides, no third signal.
            VRLog.Info("Cards", $"BURN ANIM: FLUSHING the held flight of '{CardsGameApi.CardName(widget)}' — " +
                                $"{reason}. A burned card must never be left lying on the board when the " +
                                "character it belongs to is no longer the presented one.");
            _knownBurntWidgets.Add(widget); // claim first: never two flights for one burn
            LaunchBurnFlight(widget, "hand-switch flush");
        }
        _burnHoldPruneScratch.Clear();
    }

    /// <summary>
    /// May the burned <paramref name="widget"/> fly THIS tick? False = keep it lying on the board
    /// and re-offer it next tick (the caller must not claim it). See the order-of-the-burn note in
    /// <see cref="TryAnimateBurn"/>.
    /// </summary>
    private bool TryTakeBurnFlightSlot(AbilityCardUI widget, VRCard? card)
    {
        float now = Time.unscaledTime;
        if (!_burnHoldSince.TryGetValue(widget, out BurnHold hold))
        {
            hold = new BurnHold(now, artworkSeen: false);
            _burnHoldSince[widget] = hold;
        }
        float held = now - hold.Since;

        bool effectActive = BurnArtworkActive(card);
        if (effectActive && !hold.ArtworkSeen)
        {
            hold = new BurnHold(hold.Since, artworkSeen: true);
            _burnHoldSince[widget] = hold; // latched for the whole hold — see BurnHold.ArtworkSeen
        }
        // ONE RELEASE EXPRESSION, SHARED WITH EVERY MIRROR. This used to be three lines of local
        // boolean algebra; it is now BurnArtwork.Released, which RemoteBurnFx.Drive evaluates over
        // the OWNER'S OWN widget on the peer's machine. The arithmetic is unchanged — deadline
        // wins, a running artwork holds, otherwise the start grace — and moving it was the whole
        // point: "Das soll so synchron mit den anderen Spielern sein" cannot be a property of two
        // copies that agree today.
        bool release = BurnArtwork.Released(effectActive, held);

        if (!release)
        {
            if (_burnHoldLogged.Add(widget))
                VRLog.Info("Cards", $"BURN ANIM: holding '{CardsGameApi.CardName(widget)}' ON THE BOARD " +
                                    $"while its burn artwork plays (effect {(effectActive ? "running" : "not started yet")}); " +
                                    $"it flies to the Burnt pile afterwards, at the latest after " +
                                    $"{BurnEffectMaxHoldSeconds:F1}s.");
            return false;
        }

        _burnHoldSince.Remove(widget);
        _burnHoldLogged.Remove(widget);
        if (held > 0.01f)
        {
            string arm = held >= BurnEffectMaxHoldSeconds
                ? $"DEADLINE — {BurnEffectMaxHoldSeconds:F1}s ran out" +
                  (effectActive ? " with the artwork STILL running" : " and the artwork was not running")
                : hold.ArtworkSeen
                    ? "ARTWORK END — the game's own BurnCardTimeline handle went null"
                    : $"START GRACE — the artwork NEVER started on this client, so " +
                      $"{BurnEffectStartGraceSeconds:F2}s was the whole wait";
            // HW-VERIFY (2026-09-05 item 11c "auch fuer mich blieb die Karte laenger liegen", and
            // the 2026-09-07 round's item 8): WHICH ARM released the hold, and after how long.
            // Grep token: "BURN HOLD".
            //
            // THREE ARMS, NAMED — because the old line had TWO and the release condition has three
            // histories. It printed "artwork finished" whenever effectActive was false at release,
            // which is ALSO what "the artwork never started" looks like, and ModBuild 474's peer log
            // caught it: BURN ANIM at 177184 says '(effect not started yet)' and BURN HOLD at 177185
            // says '(artwork finished)' about the same card, 0.50 s apart — 0.50 s being exactly the
            // grace. The short-rest sacrifice never waited for an artwork at all and the instrument
            // asserted the opposite. BurnHold.ArtworkSeen is what separates them.
            //
            // PROOF the wait is doing its job: "ARTWORK END" with held under
            // BurnEffectMaxHoldSeconds (the real BurnCardTimeline is 2.0 s, so ~0.5-2.5 s).
            // FALSIFIER — INERT: "DEADLINE" at 3.00-3.02 s again (the pre-2026-09-05 reading, 9 of
            // 9 burns on ModBuild 447), meaning the running-coroutine term never went false.
            // THE THIRD READING IS NOT A DEFECT BUT IS THE ANSWER TO THE SHORT-REST REPORT:
            // "START GRACE" says the game never played an artwork on this card, so there was
            // nothing to wait for and the 0.50 s is the whole wait by design. If he still reports
            // "nicht ausreichend gewartet" on a line reading START GRACE, the lead is the GAME's
            // burn timeline not starting, not this gate.
            VRLog.Note("Cards", $"BURN HOLD: '{CardsGameApi.CardName(widget)}' waited {held:F2}s on the board " +
                                $"(released by: {arm}) — flying to the Burnt pile now. The release term is " +
                                "BurnArtwork.Released over the game's OWN running BurnCardTimeline handle " +
                                "(CardEffects.coroutine), not its latched toggledEffects membership, which " +
                                "for a LOST card never clears and used to make every burn run the full " +
                                "ceiling. EVERY PEER'S MIRROR EVALUATES THIS SAME EXPRESSION over this same " +
                                "widget (RemoteBurnFx.Drive; the model is local), so this number and the " +
                                "'held=' on their BURN FLIGHT line for this card must agree — that pair IS " +
                                "the 1:1 claim.");
        }
        return true;
    }

    /// <summary>
    /// True while the game is PLAYING its own burn/lost timeline on this card's widget.
    ///
    /// <para>THE OLD BODY MEASURED THE STATE AND CALLED IT THE PICTURE, and the hardware log said so
    /// nine times out of nine (2026-09-05 MP round, item 11c "die Karte blieb länger liegen"). Every
    /// burn on BOTH clients released with "artwork still running — DEADLINE reached" at 3.00-3.02 s,
    /// i.e. the release condition <c>effectActive == false</c> was never once reached and the whole
    /// gate degenerated into a fixed 3 s wait. <c>CardEffects.HasEffect</c> is
    /// <c>toggledEffects.Contains(task)</c> over a <c>HashSet</c> that <c>ToggleEffect(true, …)</c>
    /// ADDS to and only <c>ToggleEffect(false, …)</c> / <c>RestoreCard()</c> ever removes from
    /// (CardEffects.cs:229/354/413/432/468) — for a card that has been LOST that is a latched
    /// display state with no end, not an animation with a duration.</para>
    ///
    /// <para>THE ANIMATION HAS ITS OWN, EXACT INSTRUMENT one field over. <c>BurnCard</c> starts
    /// <c>BurnCardTimeline</c> and stores the handle in <c>CardEffects.coroutine</c>
    /// (CardEffects.cs:448-451); the timeline runs <c>burnTime = 2f</c> seconds and its LAST
    /// statement is <c>coroutine = null</c> (:618). So <c>coroutine != null</c> is "the artwork is
    /// on screen right now" — the picture — and it goes false the instant the fire is over.</para>
    ///
    /// <para>BOTH TERMS ARE KEPT, because either alone is wrong: the coroutine field is also the
    /// handle for the GHOST (discard) timeline, so the state test is what says the running timeline
    /// is a BURN; and the state test alone is what the log has just falsified. The deadline in
    /// <see cref="TryTakeBurnFlightSlot"/> is unchanged and stays the belt for a burn whose timeline
    /// never starts.</para>
    /// </summary>
    /// <remarks>
    /// THE BODY MOVED, THE MEANING DID NOT (2026-09-07 round, item 8). Both terms and both reasons
    /// above are now <see cref="BurnArtwork.Playing"/>, so that <c>RemoteBurnFx</c> can ask the
    /// same question about the same widget on a peer's machine and get the owner's answer instead
    /// of an equivalent-looking one. This wrapper stays rather than being inlined at its single
    /// call site because <c>CardsDriver.6.Flows.cs</c> names it in prose (:128, :259) and a comment
    /// that points at a deleted symbol is the class of false sentence this project keeps finding.
    /// </remarks>
    private static bool BurnArtworkActive(VRCard? card)
        => BurnArtwork.Playing(BurnArtwork.EffectsOf(card != null ? card.FullCard : null));

    /// <summary>
    /// Fly one freshly-burned card into the burnt pile. Prefers the card's LIVE VR representation
    /// (the fan card selected in the LoseCard step) so the very card the player burned flies; if
    /// no usable VR card exists at that instant (already parked/recycled, or a discard-pile source
    /// with no live fan card), a transient card-back slab flies from the discard pile to the burnt
    /// pile so the user still SEES the card go. Skips cards the turn-clear sweep already owns.
    /// </summary>
    private void TryAnimateBurn(AbilityCardUI widget)
    {
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out _, out _))
            return; // burnt pile off / not built — no destination to fly to

        VRCard? card = _factory.Find(widget);

        // ORDER OF THE BURN (user ruling 2026-08-03: "Ich möchte, dass die Karte erst liegen
        // bleibt, man auf der Karte selber die Verbrannt-Animation abwartet und DANN in das
        // jeweilige Pile geht").
        //
        // This watch fires off the PILE COUNT, which the game commits the instant the burn is
        // decided — several frames BEFORE it starts playing the card's own burn artwork. Flying
        // immediately produced exactly the reported sequence: the card left for the pile, the
        // game then ran its burn timeline on the card it still owns (which the dock re-claims, so
        // it "pops back on the board"), and that leftover only went away when the next cards were
        // dealt. The hardware log shows the two in the wrong order plainly — "BURN ANIM
        // [pile-watch] … flies from …" at line 2283, "Burn/ghost effect playing ON the dock card"
        // only at 2427.
        //
        // So the flight WAITS: first for the effect to start (short grace — the game needs a few
        // frames), then for it to finish. Both waits are bounded, and the card is not claimed
        // while waiting, so the watch simply re-offers it next tick. If the effect never appears
        // the deadline lets the flight go anyway — a burned card must never be stranded on the
        // board just because its artwork did not play.
        //
        // OWNERSHIP FIRST, gate second: a card the turn-clear sweep will fly (still docked as a
        // round card, _lastHalfCards), one already mid-flight, or one in the player's hand must
        // not enter the artwork hold at all — its owner animates (or holds) it, and a hold taken
        // here would either release into a duplicate flight or cycle hold/release log lines every
        // grace period until the owner finally moved it. The watch simply keeps re-offering; the
        // ownership resolves itself (turn-clear launches and consumes any stale hold via
        // ClearBurnHold, a grabbed card returns to a zone on release).
        bool ownedElsewhere = card != null
            && (card.IsHeld || card.IsFlying || _flyingToPile.Contains(card) || _lastHalfCards.Contains(card));
        if (ownedElsewhere)
            return;
        if (!TryTakeBurnFlightSlot(widget, card))
            return;
        LaunchBurnFlight(widget, "pile-watch");
    }

    /// <summary>
    /// Launch the actual burn flight for <paramref name="widget"/> — the REAL VR card when one is
    /// still live at its true pose, else a transient card-back slab from the card's last-known
    /// pose, else nothing (never a teleport). Split out of <see cref="TryAnimateBurn"/> so
    /// <see cref="FlushBurnHolds"/> can land a pending flight WITHOUT re-running the artwork hold
    /// gate — a hold that survived until the character switched has had its moment on the board and
    /// must not be dropped (see the flush's own doc). <paramref name="origin"/> only names the
    /// caller in the log. Reports the same Board→Burnt <see cref="Net.NetCardFx"/> event on both
    /// branches, so peers replay every burn regardless of which branch ran.
    /// </summary>
    private void LaunchBurnFlight(AbilityCardUI widget, string origin)
    {
        if (!_piles.TryGetPileWorld(PileKind.Burnt, out Vector3 burntPos, out float slabWidth))
            return; // burnt pile off / not built — no destination to fly to

        Vector3 arcUp = BoardUp();
        float minArc = BoardArcMin();
        VRCard? card = _factory.Find(widget);

        if (card != null && card.GameCard != null && card.gameObject.activeInHierarchy
            && !card.IsHeld && !card.IsFlying && !_flyingToPile.Contains(card))
        {
            // Ideal: the real VR card is still live at its true board position — fly IT (face and
            // all), from where it actually sits, orientation held for the whole flight (FlyToPile).
            float arcHeight = Mathf.Max(minArc, Vector3.Distance(card.transform.position, burntPos) * VRCard.FlyArcHeightFraction);
            _flyingToPile.Add(card);
            VRCard flying = card;
            // MP parity (report 6): a damage-burn is the most dramatic card animation in the game —
            // peers replay it as a card arcing off this player's board into their burnt stack, and
            // since ModBuild 461 out of the RECESS it is lying in rather than off the board centre
            // (item 5's origin half). This branch is reached precisely because the real VR card is
            // still live at its true board position, so the seat is there to be read.
            ReportCardFx(SlotAnchor(_tray.RecessSeatOfCard(card)), Net.CardFxAnchor.Burnt);
            LogBurnAttribution(widget, origin);
            CardFlightLedger.Note("own", "Burnt", "own-burn/" + origin, CardsGameApi.CardName(widget));
            card.FlyToPile(burntPos, slabWidth, FlyToPileSeconds, arcUp, () =>
            {
                _flyingToPile.Remove(flying);
                _factory.Park(flying);
                VRLog.Info("Cards", $"BURN ANIM: '{flying.name}' reached the Burnt pile — parked.");
            }, minArc);
            _knownBurntWidgets.Add(widget); // claim (same contract as TryStartBurnFly) — animate once
            _lastCardWorldPos.Remove(widget); // consumed
            _lastCardWorldRot.Remove(widget);
            VRLog.Info("Cards", $"BURN ANIM [{origin}]: '{CardsGameApi.CardName(widget)}' burned — real VR card " +
                                $"flies from {card.transform.position} → Burnt pile ({FlyToPileSeconds:F2}s, arc " +
                                $"{arcHeight:F3} m over the board). VR presentation only; game pile state untouched.");
            return;
        }

        // Issue 1: the live VR card is already parked (position lost) or recycled, so fly a transient
        // card-back slab from the burned card's LAST-KNOWN world pose — NEVER from the discard pile
        // (that teleport to a different place first was the user's "glitched over the pile then
        // appeared somewhere else" bug). Keep that recorded rotation constant for the whole flight so
        // the card stays equally oriented. With no recorded pose there is genuinely nowhere to fly
        // from, so the animation is SKIPPED (no teleporting slab).
        bool hasFrom = _lastCardWorldPos.TryGetValue(widget, out Vector3 fromPos);
        Quaternion fromRot = _lastCardWorldRot.TryGetValue(widget, out Quaternion r) ? r : Quaternion.identity;
        _lastCardWorldPos.Remove(widget); // consumed either way
        _lastCardWorldRot.Remove(widget);
        if (!hasFrom)
        {
            VRLog.Warn("Cards", $"BURN ANIM [none]: '{CardsGameApi.CardName(widget)}' → Burnt pile: NO last-known VR " +
                                "position for the burned widget — animation skipped (never teleport a slab to a " +
                                "different place; the game pile state is unchanged). If this line appears for a " +
                                "short/long rest or damage burn, the earlier live-card paths all declined — check " +
                                "which zone the card was in when it burned.");
            return;
        }
        Transform? anchor = AnchorParent();
        if (anchor == null)
            return;
        float slabArc = Mathf.Max(minArc, Vector3.Distance(fromPos, burntPos) * VRCard.FlyArcHeightFraction);
        BurnSlab.Launch(anchor, fromPos, fromRot, burntPos, slabWidth, FlyToPileSeconds, arcUp, minArc);
        LogBurnAttribution(widget, origin + "/slab");
        CardFlightLedger.Note("own", "Burnt", "own-burn/" + origin + "/slab", CardsGameApi.CardName(widget));
        // MP parity (report 6): the fallback slab is the same event on the wire.
        // Board STAYS HARDCODED HERE, and that is the honest answer rather than the unfixed one:
        // this branch is reached precisely because there is no live VR card left for the widget
        // (see the line below), so there is no transform to read a recess off. `fromPos` is a
        // REMEMBERED last position, not a seat, and naming a recess from it would be exactly the
        // approximation item 5's fix exists to remove.
        ReportCardFx(Net.CardFxAnchor.Board, Net.CardFxAnchor.Burnt);
        // ─── A STANDING VIOLATION OF THE BURN RULING, AND IT NOW SAYS SO EVERY TIME IT FIRES ────
        // "Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar
        // sein." BurnSlab.Launch builds ONE mesh with an edge material and a BACK material and no
        // front at all (see its own note), so every flight down this branch shows a card back on
        // both faces — the one picture that ruling forbids outright, with no phase, pile or
        // anti-cheat argument that outranks it.
        //
        // WHY IT IS NAMED HERE RATHER THAN FIXED HERE, stated so it cannot be read as an oversight.
        // (1) It is the LOCAL owner's own flight, so it is invisible to the systematic face audit:
        //     Net.PeerCardFaceCensus enumerates the surfaces that draw a PEER's card and this is not
        //     one of them, which is exactly why a wrong face survived here while eight peer surfaces
        //     were being audited. That blind spot is the finding; this line is what closes it.
        // (2) Putting a real front on this slab is not a material swap. A card front in this mod is
        //     a BORROWED uGUI widget on a FaceCanvas (Cards.CardFace), and putting one on a
        //     self-destructing transient and restoring it is the machinery whose failure mode is
        //     already recorded in this project ("Replaying them is how a burned card's face got put
        //     back where it no longer belongs", CardFace.Restore). Doing that from the face lane
        //     while the burn-sequencing lane is rewriting the callers is how two correct changes
        //     make one broken flight.
        // THE FALSIFIER IS THIS LINE'S OWN EXISTENCE: if it never appears in a hardware log, the
        // branch is unreachable and the violation is theoretical; if it appears once, the front rig
        // is owed and the log names the card it was owed for.
        // HW-VERIFY
        VRLog.Note("Cards", $"CARD FACE RULE [{origin}/slab]: '{CardsGameApi.CardName(widget)}' " +
                            "burned and is flying with its BACK — chosen by NO RULE. This is a " +
                            "KNOWN STANDING VIOLATION of the burn ruling ('Beim Verbrennen EGAL " +
                            "AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar " +
                            "sein'): the transient BurnSlab is built with an edge material and a " +
                            "card-BACK material and carries no front at all. It is reached ONLY " +
                            "when no live VR card is left for the burned widget, so this line " +
                            "APPEARING AT ALL is the evidence that the front rig is owed — and its " +
                            "absence across a session is the evidence that the branch is dead. " +
                            "Every other burn surface gets its front from " +
                            "Net.RevealGate.IsPubliclyRevealedCard, which is a property of the CARD " +
                            "and needs no phase; this one has no face host to give it to.");
        VRLog.Info("Cards", $"BURN ANIM [{origin}/slab]: '{CardsGameApi.CardName(widget)}' burned — transient card-back slab " +
                            $"from {fromPos} (the burned card's true last position) → Burnt pile " +
                            $"({FlyToPileSeconds:F2}s, arc {slabArc:F3} m over the board), orientation held — no " +
                            "live VR card left for the burned widget.");
    }

    /// <summary>
    /// Transient card-back slab for the burn fallback (issue B): a pooled-free, self-destructing
    /// mini card that flies from a source into the burnt pile with the SAME over-the-board arc as
    /// <see cref="VRCard.FlyToPile"/>, then removes itself. Used only when the burned card has no
    /// live VR representation to fly. Parented under the cards anchor so it shares the diorama
    /// scale; works in world space on unscaled time (card phases pause timeScale).
    ///
    /// <para>IT DRAWS A CARD BACK ON BOTH FACES, AND THAT IS A KNOWN STANDING VIOLATION rather than
    /// a design choice: the two materials below are the edge and the BACK, and there is no face host
    /// on this object at all. The user's ruling is "Beim Verbrennen EGAL AUS WELCHEM GRUND muss die
    /// Karte immer mit der Vorderseite sichtbar sein", so a burn flight showing a back is wrong on
    /// every path that reaches it. The caller emits a <c>CARD FACE RULE …/slab</c> line saying so
    /// every time this launches — read that line's presence or absence before deciding whether the
    /// front rig is worth building, because the branch is only reached when the burned widget has
    /// no live VR card left and may well be dead.</para>
    /// </summary>
    private sealed class BurnSlab : MonoBehaviour
    {
        private Vector3 _from;
        private Vector3 _to;
        private Vector3 _up;
        private float _height;
        private float _elapsed;
        private float _duration;
        private Vector3 _fromScale;
        private Vector3 _toScale;

        internal static void Launch(Transform anchor, Vector3 fromWorld, Quaternion fixedRot, Vector3 toWorld,
            float targetWorldWidth, float duration, Vector3 worldUp, float minArcHeight = 0f)
        {
            float w = CardsConfig.CardWidth.Value;
            float h = CardsConfig.CardHeight;

            var go = new GameObject("BurnSlab");
            go.transform.SetParent(anchor, worldPositionStays: false);
            var mf = go.AddComponent<MeshFilter>();
            // Round 17: this slab IS an ability card (see the material note below), so it wears
            // the same punched-out contour mesh once the Ability footprint is known.
            CardMesh.AttachBody(mf, CardBodyKind.Ability, w, h);
            var mr = go.AddComponent<MeshRenderer>();
            // ABILITY kind, not the never-clipped Neutral pair (2026-08-11: "Der schwarze Rand soll
            // im gesamten Spiel entfernt werden egal wo die Karte ist"). This slab IS an ability
            // card — it is CardMesh.Get(CardWidth, CardHeight), the very mesh VRCard's backing uses,
            // with the same planar card-space UVs — so the Ability footprint maps onto it exactly.
            // Asking for Neutral here left the one card the player watches most closely (the burn
            // flight, front and centre over the board) as the only black rectangle left in the game.
            mr.sharedMaterials = new[]
            {
                CardMesh.CreateEdgeMaterial(CardBodyKind.Ability),
                CardMesh.CreateBackMaterial(CardBodyKind.Ability),
            };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Core.VRLayers.Apply(go);

            float parentLossy = anchor.lossyScale.x;
            float startLocal = 1f; // full card size at the source
            float endLocal = (parentLossy > 1e-5f && w > 1e-5f)
                ? targetWorldWidth / (parentLossy * w)
                : 0.5f;

            var slab = go.AddComponent<BurnSlab>();
            slab._from = fromWorld;
            slab._to = toWorld;
            slab._up = worldUp.sqrMagnitude > 1e-6f ? worldUp.normalized : Vector3.up;
            slab._height = Mathf.Max(minArcHeight, Vector3.Distance(fromWorld, toWorld) * VRCard.FlyArcHeightFraction);
            slab._duration = Mathf.Max(0.05f, duration);
            slab._fromScale = Vector3.one * startLocal;
            slab._toScale = Vector3.one * Mathf.Max(1e-4f, endLocal);

            go.transform.position = fromWorld;
            go.transform.localScale = slab._fromScale;
            // Issue 1: hold the burned card's LAST-KNOWN orientation for the whole flight — the card
            // must stay equally oriented, never snap to a billboard or the pile's orientation.
            go.transform.rotation = fixedRot;
        }

        private void Update()
        {
            _elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f); // hitch cap, like the fan anim
            float ft = _duration > 0f ? Mathf.Clamp01(_elapsed / _duration) : 1f;
            float e = 1f - (1f - ft) * (1f - ft); // ease-out on the base slide
            transform.position = Vector3.Lerp(_from, _to, e) + VRCard.FlyArcOffset(ft, _up, _height);
            transform.localScale = Vector3.Lerp(_fromScale, _toScale, e);
            if (ft >= 1f)
                Destroy(gameObject);
        }
    }

    private readonly HashSet<VRCard> _hooked = new();

    private void HookCard(VRCard card)
    {
        if (!_hooked.Add(card))
            return;
        card.Released += OnCardReleased;
        card.Grabbed += OnCardGrabbed;
        card.Poked += OnCardPoked;
    }
}
