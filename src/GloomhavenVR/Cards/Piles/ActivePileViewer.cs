using System.Collections.Generic;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// The ACTIVE CARDS display area (feature 6): a permanently-visible
/// compact column of the character's currently-active ability cards (round-long or
/// persistent — <c>CardPileType.Active</c>), docked off the board's RIGHT edge just past
/// the discard/burnt pile stacks (<see cref="PlayTray.ActiveMount"/>). Unlike the pile
/// <see cref="PileBrowser"/> it is NOT opened/closed on demand — it simply mirrors the
/// active pile whenever there is one, and shows nothing when the pile is empty. Each card
/// is rendered slightly SMALLER than the hand/browse cards (<see cref="CardScale"/>) and
/// stays grabbable so the player can pluck one out to read it, then it returns to the
/// column on release (the driver routes both, exactly like the browse arc). Purely
/// informational: adopting/plucking an active card never commits or selects it. The
/// active HALF/halves of each card carry the game's OWN action-region highlight (the
/// native FullAbilityCard.ToggleHighlightHover, driven by CardsDriver from
/// CardsGameApi.GetActiveHalves) so what is active is visible at a glance. Cards are laid
/// out in a matrix (up to 3 per row) recentered on the mount; laser hit-testing (browse-
/// style TryRaycast) makes them hover/pluck-to-read. Layout + ray only; content,
/// highlight and lifecycle are driven by <see cref="CardsDriver"/>.
/// </summary>
internal sealed class ActivePileViewer
{
    /// <summary>
    /// Active cards read slightly smaller than the hand/browse fan (PileBrowser.CardScale = 1.3).
    /// Round-2: the scale is now PER-BOARD (debug-menu tunable), seeded 0.82.
    /// </summary>
    internal static float CardScale => CardsConfig.ActiveCardScale(CardsConfig.CurrentBoard).Value;

    // Matrix geometry (feature 6 grid): up to Columns cards side-by-side per row; a full
    // row starts the next one. Rows overlap vertically slightly (the lower a row, the
    // nearer the viewer, so its tops cover the row above cleanly); columns clear one full
    // card width so neighbours never overlap horizontally. The whole grid is symmetric
    // about the mount x and vertically centered, so its midpoint holds at a consistent
    // height and it stays balanced as rows are added. Round-2: the col/row step FACTORS are
    // per-board (debug-menu tunable, CardsConfig.ActiveGridSpacing — seeded (1.06, 0.70)).
    /// <summary>Active cards per row. INTERNAL because the peer's mirror
    /// (<c>Net.RemoteActiveCards</c>) draws the same block and must draw it in the same number of
    /// columns: it carried its own <c>Columns = 2</c> until 2026-09-05, so with three active cards
    /// the owner saw one row of three and every teammate saw a 2+1 block, re-centred to a
    /// different height against the board's other docks. That is the standing 1:1 ruling's own
    /// object ("gleiche Position, gleiche Größe"), and the mirror's own note already claimed to
    /// copy this layout "term for term" — the column count was the one term it re-typed.</summary>
    internal const int Columns = 3;               // active cards per row

    /// <summary>Render-order stagger per row (same as CardFan/PileBrowser). INTERNAL for the same
    /// reason as <see cref="Columns"/> — the mirror stepped its rows by an inline copy of it.</summary>
    internal const float ZStagger = 0.004f;

    private readonly List<VRCard> _cards = new(8);
    private Transform? _root;
    private TextMeshPro? _title;
    private bool _locHooked;

    internal bool IsShown => _root != null && _root.gameObject.activeSelf;

    internal bool Contains(VRCard card) => _cards.Contains(card);

    /// <summary>The cards the column is currently showing (read-only view, never mutated). Read by
    /// the laser stand-down's contact scan (<c>CardsDriver.ContactedCard</c>): the column is the one
    /// card pool with NO hand sweep of its own, so its only candidate used to be
    /// <c>Grabber.Highlighted</c> — the single nearest grabbable — and a hand buried in a column
    /// card while the grabber preferred something else stood no beam down (user report 2026-08-09,
    /// "das soll für alle Fächer gelten" — "that should apply to every fan").</summary>
    internal IReadOnlyList<VRCard> Cards => _cards;

    // ------------------------------------------------------------------ lifecycle --

    /// <summary>
    /// Localized caption for the area. No game key exists (GUI_ACTIVE is absent), so this is
    /// a mod string (English/German table, English fallback).
    /// </summary>
    internal static string Caption() => Core.Loc.Mod("active");

    internal void EnsureBuilt(PlayTray tray)
    {
        Transform? mount = tray.ActiveMount;
        if (mount == null)
            return;
        // A tray teardown destroys the column with the mount — the Unity fake-null makes
        // this == check true and the column rebuilds from scratch under the fresh mount.
        if (_root != null)
            return;

        // Rebuilding under a fresh mount: any prior card refs are stale (parked by the
        // board-switch / released with the old tray). The driver repopulates via SetCards.
        _cards.Clear();
        _seated.Clear(); // stale refs must never make a fresh arrival look like a resident

        _root = new GameObject("GloomhavenVR.ActivePile").transform;
        _root.SetParent(mount, worldPositionStays: false);
        Core.VRLayers.Apply(_root.gameObject); // cards apply themselves in VRCard.Build

        _title = CreateTitle(_root);

        // Live language following: the title is built once — re-read it on a language change.
        if (!_locHooked)
        {
            _locHooked = true;
            Core.Loc.OnChanged += RefreshLabels;
        }
    }

    /// <summary>Build the same active-area caption on the owner's board and its remote mirror.</summary>
    internal static TextMeshPro CreateTitle(Transform root)
    {
        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(root, worldPositionStays: false);
        titleGo.transform.localPosition = new Vector3(0f, 0.075f, -0.0025f); // above the column, viewer side (-Z)
        TextMeshPro title = titleGo.AddComponent<TextMeshPro>();
        title.text = Caption().ToUpperInvariant();
        title.alignment = TextAlignmentOptions.Center;
        title.color = new Color(1f, 0.9f, 0.6f);
        WorldUI.NativeButtonSkin.ApplyFont(title); // native HUD font, like the pile captions
        Core.TmpFit.Fit(title, 0.09f, 0.024f, maxFontSize: 0.22f, wrap: false);
        WorldUI.MrBacking.Label(title); // off-board title → sky/room behind it in MR
        // PERSPECTIVE: the active column mounts at BoardW/2 + 0.182 m — OUTSIDE the control board's
        // furnished apron (PlayTray.FurnitureApronMeters = 0.18), so there is no depth-writing slab
        // behind this caption and it was never adopted into the board's furniture band either. It
        // was the third unranked pile title (sortingOrder 0) and lost to every converted panel for
        // the same reason the items fan's title did. See WorldUI.FreeLabelOrder.
        WorldUI.FreeLabelOrder.Rank(title);

        return title;
    }

    /// <summary>Re-read the area caption in the current language (live-follow, Loc.OnChanged).</summary>
    internal void RefreshLabels()
    {
        if (_title != null)
            _title.text = Caption().ToUpperInvariant();
    }

    internal void SetVisible(bool visible)
    {
        if (_root != null && _root.gameObject.activeSelf != visible)
            _root.gameObject.SetActive(visible);
    }

    internal void Destroy()
    {
        if (_locHooked)
        {
            Core.Loc.OnChanged -= RefreshLabels;
            _locHooked = false;
        }
        _cards.Clear();
        _seated.Clear();
        _seatPrune.Clear();
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _title = null; // child of _root, destroyed with it
        }
    }

    // ------------------------------------------------------------------ content --

    /// <summary>
    /// Replace the shown card set (driver rebuild path). Cards ALREADY in the column glide to
    /// their new slots; a card ARRIVING in the column is seated instantly — see
    /// <see cref="_seated"/> for why the arrival must not animate here.
    /// The driver has already adopted the cards and set their per-card active highlight.
    /// </summary>
    internal void SetCards(List<VRCard> cards)
    {
        _cards.Clear();
        for (int i = 0; i < cards.Count; i++)
            _cards.Add(cards[i]);
        Relayout(instant: false);
    }

    /// <summary>Drop one card (widget recycled under us); the column closes the gap.</summary>
    internal void Remove(VRCard card)
    {
        if (_cards.Remove(card))
            Relayout(instant: false);
    }

    /// <summary>
    /// Return a card plucked out for a close read back into the column — the active-area
    /// counterpart of <see cref="PileBrowser.Add"/>. Purely informational: the release
    /// routes here, never to a select/slot seam.
    /// </summary>
    internal void Add(VRCard card)
    {
        if (!_cards.Contains(card))
            _cards.Add(card);
        Relayout(instant: false);
    }

    // ------------------------------------------------------------------ layout --

    // Change-gate for the grid-shape Info line ((rows, cols) key); int.MinValue = unlogged.
    private int _loggedLayout = int.MinValue;

    /// <summary>
    /// Round-2 live-apply: re-lay the grid from the active board's per-board card scale + col/row
    /// step factors when the debug menu / cfg edits either. No-op when the column is empty.
    /// </summary>
    internal void ApplyLayout() => Relayout(instant: false);

    /// <summary>
    /// The cards this column has ALREADY asserted a home for. Membership, not placement: a card
    /// held out for a close read is in here too, so its release glides it back into its cell.
    ///
    /// <para>WHY IT EXISTS — user report items 1 and 8, 2026-09-07. Item 1, verbatim: "Eine
    /// Fluganimation zu viel: Die aktive Karte fliegt nach dem aktivieren sofort in den aktiven
    /// bereich obwohl die Karte noch liegen bleibt. Nach beenden des Zuges fliegt die Karte korrekt
    /// an die richtige Stelle." An activated Gloomhaven card STAYS PHYSICALLY ON THE BOARD for the
    /// rest of the turn and only leaves at end of turn, so the activation edge must not move it at
    /// all — and <see cref="Relayout"/> moved it, because it called
    /// <c>SetHome(..., instant: false)</c> on EVERY column card unconditionally. A card entering the
    /// column therefore glided out of the recess the player was still looking at, every time, on
    /// whichever rebuild first saw it in <c>CCharacterClass.ActivatedCards</c>.</para>
    ///
    /// <para>THE CORRECT FLIGHT IS SOMEBODY ELSE'S AND IS UNAFFECTED. The end-of-turn arc into this
    /// column is <c>CardsDriver.LaunchActiveFlights</c>, which runs AFTER
    /// <see cref="SetCards"/> and replays the card from a pose CAPTURED BEFORE the layout ran
    /// (<c>ActiveFlight.FromWorld</c>) via <c>VRCard.FlyFromPile</c>. Seating the arrival instantly
    /// makes that ordering MORE correct, not less: the home cell is asserted crisply and the arc
    /// then flies into it from the recess, which is the ordering that method's own comment ("THE
    /// HOME CELL FIRST, THEN THE ARC INTO IT") already asks for.</para>
    ///
    /// <para>MEASURED, ModBuild 472, both logs of the 2026-09-07 session. The owner's column rose
    /// from 0 to a non-empty count THREE times on the host ('[Cards] Active cards: N shown' at
    /// lines 55899 / 222988 / 240847) and THREE times on the peer (209327 / 209808 / 232300) — six
    /// arrivals — against exactly ONE legitimate end-of-turn arc in the whole session
    /// ('GloomhavenVR] [Cards] ACTIVE FLIGHT', host line 55898, and ZERO on the peer). Five of the
    /// six arrivals therefore drew a glide that no flight producer had authorised and no instrument
    /// named. Item 8's restore edge is the same producer read a second way: the host burned
    /// ParasiticInfluence at line 239903, the column emptied (240566) and refilled (240847) as the
    /// damage burn resolved and the cards came back, and the refill glided again.</para>
    ///
    /// <para>IT IS DIRECTIONAL BY CONSTRUCTION, which is what item 8 asks for. This set is pruned
    /// to the column's live contents on every pass and cleared outright when the column empties, so
    /// "already seated" can only ever mean "was in this column on the previous layout". A card that
    /// leaves and returns is a NEW arrival and is seated instantly — a returning card is not a
    /// card that flew anywhere.</para>
    /// </summary>
    private readonly HashSet<VRCard> _seated = new(8);

    /// <summary>Scratch for pruning <see cref="_seated"/> without allocating a delegate per pass.</summary>
    private readonly List<VRCard> _seatPrune = new(8);

    private void Relayout(bool instant)
    {
        if (CardsDriver.BurnLayoutPending) return;

        if (_root == null)
            return;
        int n = _cards.Count;
        if (n == 0)
        {
            // The column is empty: nothing is seated, so a card that comes back later is a genuine
            // arrival and not a resident that merely moved. This is item 8's restore edge.
            _seated.Clear();
            return;
        }

        ControlBoard board = CardsConfig.CurrentBoard;
        float cardScale = CardsConfig.ActiveCardScale(board).Value;
        Vector2 grid = CardsConfig.ActiveGridSpacing(board).Value; // (col factor, row factor)
        float colStep = CardsConfig.CardWidth.Value * cardScale * grid.x;
        float rowStep = CardsConfig.CardHeight * cardScale * grid.y;
        int rows = (n + Columns - 1) / Columns; // ceil(n / Columns)
        float yTop = rowStep * (rows - 1) * 0.5f; // vertically centered block (midpoint at y = 0)

        int arrived = 0;
        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null)
                continue;
            if (card.IsHeld)
            {
                // A held card is declined a cell but is still a MEMBER of the column, so its
                // release glides it back from the hand instead of snapping. Recording membership
                // here is what keeps the pluck-to-read return animated.
                _seated.Add(card);
                continue;
            }
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);

            int row = i / Columns;
            int col = i % Columns;
            int colsInRow = Mathf.Min(Columns, n - row * Columns);
            // Symmetric about the mount x; a partial last row centers on its own width.
            float x = (col - (colsInRow - 1) * 0.5f) * colStep;
            float y = yTop - row * rowStep;
            // Lower rows sit nearer the viewer (-Z) so their tops overlap the row above.
            var pos = new Vector3(x, y, -ZStagger * row);
            // AN ARRIVAL IS SEATED, A RESIDENT GLIDES. See _seated: an activated card is still
            // lying on the board and must not be moved by the mere fact that the model now lists
            // it as active; a card already in the column that shifts because the grid re-centred
            // (a second card arrived, one was removed, the debug menu re-tuned the spacing) is a
            // real layout move and keeps its glide.
            // HashSet.Add answers TRUE when the card was not already a member — which is exactly
            // "this column has never asserted a home for it", i.e. an arrival.
            bool arriving = _seated.Add(card);
            if (arriving)
                arrived++;
            card.SetHome(_root, pos, Quaternion.identity, cardScale, instant || arriving);
            card.ResetColliderRegion(); // active cards are not fan-stripped
        }

        // Prune to the live contents so "already seated" can never outlive the card's membership.
        _seatPrune.Clear();
        foreach (VRCard seated in _seated)
        {
            if (!_cards.Contains(seated))
                _seatPrune.Add(seated);
        }
        for (int i = 0; i < _seatPrune.Count; i++)
            _seated.Remove(_seatPrune[i]);
        _seatPrune.Clear();

        if (arrived > 0)
            ReportArrivals(arrived, n);

        LogGridShape(rows, n);
    }

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-07 report items 1 and 8): how a card ENTERED the active
    /// column — seated in place, or moved. Grep token <c>ACTIVE SEAT</c>. One line per arrival
    /// batch (a handful per scenario), never per frame: <see cref="Relayout"/> itself runs only on
    /// a content or layout change, and this is gated on <c>arrived &gt; 0</c> inside it.
    ///
    /// <para>WHY IT IS NOT A <c>CardFlightLedger</c> LINE. The ledger records FLIGHTS, keyed on
    /// (board, destination stack), and the whole point of this change is that a column arrival is
    /// NOT a flight — it is the absence of one. Recording a non-event in the flight ledger would
    /// put an eighteenth and nineteenth "PILE FLIGHT" line into a log whose flight count is the
    /// number under audit. See <c>CardFlightLedger</c>'s inventory table, row 12.</para>
    ///
    /// <para><b>WORKING</b> = one <c>ACTIVE SEAT</c> line per activation reading
    /// <c>seated 1 card in place</c>, and NO <c>[Cards] ACTIVE FLIGHT</c> line at that moment. At
    /// end of the same turn the card leaves its recess and <c>ACTIVE FLIGHT</c> prints exactly once
    /// for it. Over a session the two counts must satisfy:
    /// <c>ACTIVE SEAT arrivals &gt;= [Cards] ACTIVE FLIGHT lines</c>, with every ACTIVE FLIGHT
    /// preceded by its own card's seat. On ModBuild 472 the same session read 6 arrivals against 1
    /// ACTIVE FLIGHT and had no way to say so — every one of the other 5 drew an unlogged glide.</para>
    ///
    /// <para><b>INERT</b> = this line absent while <c>[Cards] Active cards: N shown</c> rises from
    /// 0 to N. The arrival went through a path that does not reach <see cref="Relayout"/> at all,
    /// so the fix is not on the edge the user is watching and the producer is elsewhere in the
    /// inventory table.</para>
    ///
    /// <para><b>STILL BEYOND THE INSTRUMENT</b> = this line reading <c>seated N card(s) in
    /// place</c> and the user STILL reporting a flight at activation. The glide is then not this
    /// column's: the remaining candidates that move a card toward the board's right edge are the
    /// hand fan's own relayout (<c>CardFan.Relayout</c>, when the card leaves the fan) and the
    /// tray recess dock (<c>PlayTray.PlaceCard</c>). Neither is under this class and neither is
    /// ledgered — see the inventory's rows 13-17. It is NOT the mirror: <c>RemoteActiveCards</c>
    /// seats its cells with <c>RemoteBoardCard.Move</c>, a direct <c>localPosition</c> write with no
    /// interpolation at all, so a peer's column has never animated an arrival either way.</para>
    /// </summary>
    private void ReportArrivals(int arrived, int n)
    {
        // NOTHING LOAD-BEARING IS WRITTEN HERE. This method may be gated off, demoted or retired
        // and the column's behaviour is identical — the arrival decision is made by the _seated
        // membership test in Relayout, never by anything this line touches.
        // HW-VERIFY: report items 1 and 8 — the activation edge must not move the card. Grep
        // token: ACTIVE SEAT. See this method's doc for WORKING / INERT / STILL BEYOND.
        VRLog.Note("Cards", $"ACTIVE SEAT: seated {arrived} card(s) in place in the ACTIVE column "
            + $"(column now holds {n}). AN ARRIVAL IS NOT A FLIGHT — an activated Gloomhaven card "
            + "stays lying on the board until the end of the turn, so this column asserts the "
            + "newcomer's home cell WITHOUT animating it, and the card is only moved later by "
            + "CardsDriver.LaunchActiveFlights, which replays it from the recess pose it captured "
            + "before this layout ran and prints '[Cards] ACTIVE FLIGHT'. Cards ALREADY in the "
            + "column still glide when the grid re-centres around a newcomer, which is a real "
            + "layout move and not an arrival. Read this line against '[Cards] ACTIVE FLIGHT' and "
            + "'[Cards] Active cards: N shown': arrivals must be >= flights, and a flight with no "
            + "preceding seat for the same card means the card reached the column by some path "
            + "other than this one.");
    }

    private void LogGridShape(int rows, int n)
    {
        int layoutKey = rows * 100 + Mathf.Min(n, Columns);
        if (_loggedLayout != layoutKey)
        {
            _loggedLayout = layoutKey;
            VRLog.Info("Cards", $"Active grid: {n} card(s) in {rows} row(s) × up to {Columns} col(s), " +
                                "recentered on the mount.");
        }
    }

    // ------------------------------------------------------------------ laser pick --

    /// <summary>
    /// Geometric ray hit-test over the active grid (feature 6, laser-hover to read /
    /// pluck close) — the active-area counterpart of <see cref="PileBrowser.TryRaycast"/>.
    /// Same per-card plane+local-rect test, scale-aware (the grid's cards read smaller via
    /// <see cref="CardScale"/>), same sticky-hover hysteresis so overlap between rows does
    /// not flip the highlight. No allocations.
    /// </summary>
    internal bool TryRaycast(Vector3 origin, Vector3 direction, VRCard? sticky,
        out VRCard? card, out Vector3 point, out float distance)
    {
        card = null;
        point = default;
        distance = float.PositiveInfinity;

        if (!IsShown || _root == null)
            return false;

        float halfW = CardsConfig.CardWidth.Value * 0.5f;
        float halfH = CardsConfig.CardHeight * 0.5f;

        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard c = _cards[i];
            if (c == null || c.IsHeld || !c.gameObject.activeInHierarchy)
                continue;

            Transform t = c.transform;
            float denom = Vector3.Dot(direction, t.forward);
            if (denom < 1e-5f)
                continue;
            float dist = Vector3.Dot(t.position - origin, t.forward) / denom;
            if (dist <= 0f)
                continue;

            Vector3 hit = origin + direction * dist;
            Vector3 local = t.InverseTransformPoint(hit); // scale-aware (smaller cards)
            if (Mathf.Abs(local.x) > halfW || Mathf.Abs(local.y) > halfH)
                continue;

            if (ReferenceEquals(c, sticky))
            {
                card = c;
                point = hit;
                distance = dist;
                return true;
            }

            if (dist >= distance)
                continue;
            card = c;
            point = hit;
            distance = dist;
        }

        return card != null;
    }
}
