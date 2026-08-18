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
    private const int Columns = 3;               // active cards per row
    private const float ZStagger = 0.004f;        // render-order stagger, same as CardFan/PileBrowser

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
    /// "das soll für alle Fächer gelten").</summary>
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

        _root = new GameObject("GloomhavenVR.ActivePile").transform;
        _root.SetParent(mount, worldPositionStays: false);
        Core.VRLayers.Apply(_root.gameObject); // cards apply themselves in VRCard.Build

        var titleGo = new GameObject("Title");
        titleGo.transform.SetParent(_root, worldPositionStays: false);
        titleGo.transform.localPosition = new Vector3(0f, 0.075f, -0.0025f); // above the column, viewer side (-Z)
        _title = titleGo.AddComponent<TextMeshPro>();
        _title.text = Caption().ToUpperInvariant();
        _title.alignment = TextAlignmentOptions.Center;
        _title.color = new Color(1f, 0.9f, 0.6f);
        WorldUI.NativeButtonSkin.ApplyFont(_title); // native HUD font, like the pile captions
        Core.TmpFit.Fit(_title, 0.09f, 0.024f, maxFontSize: 0.22f, wrap: false);
        WorldUI.MrBacking.Label(_title); // off-board title → sky/room behind it in MR

        // Live language following: the title is built once — re-read it on a language change.
        if (!_locHooked)
        {
            _locHooked = true;
            Core.Loc.OnChanged += RefreshLabels;
        }
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
        if (_root != null)
        {
            Object.DestroyImmediate(_root.gameObject);
            _root = null;
            _title = null; // child of _root, destroyed with it
        }
    }

    // ------------------------------------------------------------------ content --

    /// <summary>
    /// Replace the shown card set (driver rebuild path; cards fly to their column slots).
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

    private void Relayout(bool instant)
    {
        if (_root == null)
            return;
        int n = _cards.Count;
        if (n == 0)
            return;

        ControlBoard board = CardsConfig.CurrentBoard;
        float cardScale = CardsConfig.ActiveCardScale(board).Value;
        Vector2 grid = CardsConfig.ActiveGridSpacing(board).Value; // (col factor, row factor)
        float colStep = CardsConfig.CardWidth.Value * cardScale * grid.x;
        float rowStep = CardsConfig.CardHeight * cardScale * grid.y;
        int rows = (n + Columns - 1) / Columns; // ceil(n / Columns)
        float yTop = rowStep * (rows - 1) * 0.5f; // vertically centered block (midpoint at y = 0)

        for (int i = 0; i < n; i++)
        {
            VRCard card = _cards[i];
            if (card == null || card.IsHeld)
                continue;
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
            card.SetHome(_root, pos, Quaternion.identity, cardScale, instant);
            card.ResetColliderRegion(); // active cards are not fan-stripped
        }

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
