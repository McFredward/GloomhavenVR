using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

// =================================================================================================
//  Objectives ("Aufgaben") — GLOBAL
// =================================================================================================

/// <summary>
/// The scenario objectives ("Aufgaben") with their quest header and progress, drawn off the LEFT
/// edge of a peer's board — the mirror of the local board's docked <c>ObjectivesSurface</c>.
///
/// ─── DEFECT (b) OF THE 1:1-PARITY ROUND, AND WHAT IT ACTUALLY WAS ──────────────────────────────
/// The hardware screenshot showed a dark box carrying the scenario name ("Felssturzkamm") over two
/// empty bars ("0/3", "0/18") — nothing like the owner's panel. The log said
/// <c>objectives=2 row(s)</c>, so again the DATA was resolved: the previous pass already read the
/// live container's rows, their localized text and their progress. What it could not reproduce was
/// the panel — because it was re-drawing the rows as mod geometry: two TMP labels, a quad progress
/// bar and a caption plate, at mod-chosen sizes and a mod-chosen wrap column. The game's row is a
/// three-deep nest of layout groups with a marker icon, a check glyph, a stretch-anchored progress
/// image and a TMP text whose column is derived top-down (see <c>ObjectivesSurface</c>'s prefab
/// dump), so a hand-drawn approximation of it can only ever look like an approximation.
///
/// ─── WHAT IT DRAWS NOW ─────────────────────────────────────────────────────────────────────────
/// PRIMARY — <see cref="RemoteWidgetMirror"/> over
/// <c>UIManager.Instance.MissionObjectiveContainer.transform</c>: the REAL container, cloned once
/// and puppeteered per frame. That is the very widget the owner's board docks (there is one per
/// client and it is scenario-global), so the quest header, every row's real text at the BOARD
/// OWNER's own wrap column, the marker icons, the check marks, the gold counter and the live
/// progress fill all come across exactly as authored — and rows the game REMOVES on completion
/// (<c>CheckToRemoveObjectives</c>) or ADDS at runtime (<c>AddObjective</c>) simply appear and
/// disappear, because the clone is rebuilt whenever the source's shape changes.
///
/// FALLBACK — the previous mod-drawn row list, kept verbatim for the frames where the container
/// does not exist (between scenarios, mid-load). It reads
/// <c>MissionObjectiveContainer.ObjectiveInstances</c> in sibling = display order when it can, and
/// <c>ScenarioState.WinObjectives/LoseObjectives</c> when it cannot; text from the game's own
/// <c>LocalizationObjectiveConveter.LocalizeText</c> and progress from
/// <c>CObjective.GetObjectiveProgress</c> — the identical calls
/// <c>MissionObjectiveUI.UpdateMissionText/UpdateMissionProgress</c> make. Which of the two is live
/// is stated in the <c>Remote board content</c> log line.
///
/// SEAT — <see cref="RemoteBoardLayout.ObjectivesMount"/> and <c>ObjectivesScale</c>, i.e.
/// <c>PlayTray.ObjectivesMountBase</c> plus the authored per-board <c>ObjectivesOffset</c> the
/// owner's own mount carries, keyed by that peer's synced style. The old remote-only constant
/// dropped the per-board term entirely, which on the Steel board of the hardware session put this
/// panel 32 mm low and 42 mm behind where the owner has it — part of defect (c).
///
/// ANTI-CHEAT: nothing here is per-player. The objectives are printed on the local player's own
/// board already; rendering the same list a second time at a peer's pose reveals literally nothing
/// new. Read-only throughout: <c>GetObjectiveProgress</c> is a pure out-parameter query and
/// <c>LocalizeText</c> only formats strings. Both are wrapped — a YML miss or a half-initialised
/// scenario state must degrade to a blank panel, never kill the remote-avatar tick.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide state, bit-identical on every client, ZERO wire.
/// Source: <c>ScenarioManager.CurrentScenarioState.WinObjectives/LoseObjectives</c> +
/// <c>CObjective.GetObjectiveProgress</c>. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemoteObjectivesPanel
{
    private const float Width = PlayTray.ObjectivesMountWidth;
    private const float RowHeight = 0.030f;
    private const float BarHeight = 0.005f;
    private const int MaxRows = 6;

    private readonly Transform _root;
    private readonly Transform _fallbackRoot;
    private readonly RemoteWidgetMirror _mirror;
    private readonly MeshRenderer _plate;
    private readonly TextMeshPro _title;
    private readonly List<Row> _rows = new(MaxRows);

    private string _signature = string.Empty;
    private int _shownRows = -1;
    private string _shownTitle = string.Empty;
    private readonly List<MissionObjectiveUI> _mirrorRows = new(MaxRows * 2);

    /// <summary>How many objective rows are currently represented (diagnostics). With the mirror
    /// live this is the live container's active row count; on the fallback it is the drawn rows.</summary>
    public int RowCount { get; private set; }

    /// <summary>Which mechanism is drawing the panel right now (diagnostics).</summary>
    public RemoteWidgetMirror.Fidelity Source { get; private set; } = RemoteWidgetMirror.Fidelity.None;

    /// <summary>Why the real widget is not being mirrored, for the diagnostic line (empty when it is).</summary>
    public string Reason => _mirror.Reason;

    /// <summary>The RESOLVED seat of this dock for the per-peer board log: the board-local mount
    /// position (including the mount SCALE, which is part of this dock's authored layout) and which
    /// measure sized the mirrored panel growing from it. See <c>RemoteControlBoard.Seats</c>.</summary>
    public string SeatLine =>
        $"mount={_root.localPosition:F3}(x{_root.localScale.x:F2}) via {_mirror.MeasurePath}";

    /// <summary>Verbatim <c>ObjectivesSurface.DensityScale</c> — the per-panel multiplier on the
    /// shared tray density that this dock (and only this dock) applies. A local copy for the same
    /// reason as every other constant on the remote board: reading WorldUI's protected override is
    /// not possible, and a mirrored panel drawn at a different density is not a mirror.</summary>
    private const float ObjectivesDensityScale = 0.6f;

    public RemoteObjectivesPanel(Transform boardRoot, in RemoteBoardLayout layout)
    {
        _root = new GameObject("Objectives").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        // The mount convention (PlayTray: RIGHT-centre of the panel, grows LEFT off the board's
        // left edge) plus the authored per-board scale — the owner's mount carries both.
        _root.localPosition = layout.ObjectivesMount;
        _root.localScale = Vector3.one * layout.ObjectivesScale;

        // fitWidth:false mirrors ObjectivesSurface.FitWidthToMount, and STAYS false: that override
        // exists because the width budget is FORCED onto the content rather than measured from it,
        // and feeding a forced width back into the uniform dock fit is what once made the 'Breite'
        // dial behave like 'Größe' (TablePanelSurfaces derives it in full). Passing true here to get
        // the owner's width in would re-import that defect, not fix anything.
        //
        // WHICH IS EXACTLY WHY THE WIDTH ARRIVES THE OTHER WAY, through layout:. The gap this line
        // used to record — and the reason the comment before it claimed a parity the chain did not
        // have — is that the wrap column is written by ObjectivesSurface.ApplyContentWidth as
        // wantPx = MountWidth · density onto the objectives container ROOT of this client's OWN live
        // panel, with
        //     MountWidth = PlayTray.ObjectivesMountWidth × CardsConfig.ObjectivesWidth(CurrentBoard)
        // — the VIEWER's dial and the VIEWER's board style — and the mirror clones that
        // already-wrapped subtree. So a peer's task panel used to re-wrap whenever the VIEWER
        // touched their own 'Breite' and never when its owner did.
        //
        // LayoutOwner.CloneAtBoardOwnersWidth closes it, and it is deliberately NOT the one-line fix
        // this file originally asked for. Writing the owner's wantPx onto the cloned root and
        // stopping there is inert twice over: the mirror's per-frame drive copies the source's
        // sizeDelta back onto the clone root every frame, and even surviving it would propagate to
        // nothing, because the column is derived through three nested layout groups that the
        // clone's neutralisation destroys and every node below the root is anchor-pinned rather than
        // stretch-anchored. Under the flag the game's own layout components survive on the clone,
        // the mirror forces layout.ObjectivesWidth × the shared density onto the clone root, and the
        // rect half of the drive stands down — the clone owns its geometry, the source still owns
        // every glyph, colour, sprite and progress fill. RemoteWidgetMirror.ApplyOwnersColumn holds
        // the full derivation, the contested-property audit and the rejected alternatives; the
        // number it writes is greppable as 'OBJECTIVES COLUMN (mirrored)' and rides SeatLine below.
        //
        // layout.ObjectivesWidth IS the owner's width (extension record 28, field id 129 =
        // NetProtocol.TuneObjectivesWidth): PlayTray.ObjectivesMountWidth × their synced multiplier.
        // It needs no live re-read here — the owner moving that dial bumps BoardTuningRevision and
        // RemoteControlBoard rebuilds every dock from a fresh RemoteBoardLayout, so this constructor
        // argument cannot go stale.
        //
        // densityScale mirrors ObjectivesSurface.DensityScale (0.6): this ONE panel renders the
        // same content pixels onto ~1.67x more tray metres than the shared tray density, because
        // the objectives text was illegibly small at 1.0. Omitting it here rendered a peer's
        // objectives at 60 % of the size their owner reads them at — a silent parity gap, since
        // both panels then look "right" in isolation and only differ side by side. It is also the
        // second factor of the wrap column above, which is why both sides land on the identical
        // 0.26 m × 0.8 × (2400 × 0.6) = 299.5 px at the shipped defaults.
        _mirror = new RemoteWidgetMirror("Objectives", _root,
            layout.ObjectivesWidth, PlayTray.ObjectivesMountMaxHeight, Vector2.left, fitWidth: false,
            densityScale: ObjectivesDensityScale,
            layoutOwner: RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth);

        _fallbackRoot = new GameObject("Fallback").transform;
        _fallbackRoot.SetParent(_root, worldPositionStays: false);
        _fallbackRoot.localPosition = new Vector3(-Width * 0.5f, 0f, 0f);

        _plate = BoardVisual.Quad(_fallbackRoot, "Plate", new Vector2(Width, RowHeight),
            BoardVisual.Unlit(new Color(0.07f, 0.07f, 0.06f, 0.92f)));
        WorldUI.MrBacking.Opacify(_plate.sharedMaterial); // 0.92 → 1 while MR is on (rows ride this plate)

        _title = RemoteBoardContent.Label(_fallbackRoot, "Title", Vector3.zero,
            new Vector2(Width * 0.9f, 0.020f), 0.05f,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Center, FontStyles.Bold);
        // The game has no single "objectives" header key; the panel's own quest header is a
        // campaign-only extra. Reuse the mod's task caption so it reads in the active language.
        RemoteBoardContent.SetText(_title, Loc.Mod("objectives").ToUpperInvariant());

        for (int i = 0; i < MaxRows; i++)
            _rows.Add(new Row(_fallbackRoot, Width));

        SetRowCount(0);
    }

    /// <summary>Per-FRAME: keep the mirrored container in step with the original, so a progress bar
    /// filling or a row ticking off plays out at the source's own rate. No-op on the fallback.</summary>
    public void TickLive() => _mirror.TickLive();

    public void Destroy() => _mirror.Destroy();

    /// <summary>Mirror the REAL container when it exists; else repaint the mod-drawn fallback.</summary>
    public void Refresh()
    {
        MissionObjectiveContainer? container = null;
        try
        {
            UIManager? manager = UIManager.Instance;
            container = manager != null ? manager.MissionObjectiveContainer : null;
        }
        catch { container = null; }

        if (_mirror.Refresh(container != null ? container.transform : null))
        {
            Source = RemoteWidgetMirror.Fidelity.MirroredWidget;
            _mirror.SetShown(true);
            if (_fallbackRoot.gameObject.activeSelf)
                _fallbackRoot.gameObject.SetActive(false);
            if (!_root.gameObject.activeSelf)
                _root.gameObject.SetActive(true);
            RowCount = CountLiveRows(container);
            _signature = string.Empty;  // a later fallback must repaint from scratch
            _shownRows = -1;
            return;
        }

        Source = RemoteWidgetMirror.Fidelity.ModDrawn;
        _mirror.SetShown(false);
        RefreshFallback();
    }

    /// <summary>Active row count of the LIVE container (what the mirrored picture is showing) —
    /// diagnostics only.</summary>
    private static int CountLiveRows(MissionObjectiveContainer? container)
    {
        try
        {
            List<MissionObjectiveUI>? rows = container != null ? container.ObjectiveInstances : null;
            if (rows == null)
                return 0;
            int n = 0;
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] != null && rows[i].gameObject.activeSelf)
                    n++;
            return n;
        }
        catch { return 0; }
    }

    /// <summary>FALLBACK: re-read the scenario objectives and repaint the mod-drawn rows on an
    /// actual change.</summary>
    private void RefreshFallback()
    {
        ScenarioState? state = ScenarioManager.CurrentScenarioState;
        if (state == null)
        {
            // Between scenarios / mid-load. Clear AND drop the repaint signature: re-entering a
            // scenario with a bit-identical objective list must repaint, not stay blank.
            _signature = string.Empty;
            SetRowCount(0);
            return;
        }

        int party;
        try { party = Mathf.Max(1, state.Players != null ? state.Players.Count : 1); }
        catch { party = 1; }

        // Even on the fallback path, prefer the LIVE container's rows (it may exist while the
        // widget mirror could not be built — a degenerate rect, a hostile prefab shape). The model
        // scan below is the last rung of the ladder.
        try
        {
            if (RowsFromLiveContainer(party))
                return;
        }
        catch { /* fall through to the model scan */ }

        RefreshTitle(null); // fallback path: the mod caption, no quest header to mirror

        var sb = new StringBuilder(96);
        int row = 0;
        row = Append(state.WinObjectives, party, sb, row);
        row = Append(state.LoseObjectives, party, sb, row);

        string sig = sb.ToString();
        if (sig == _signature)
            return;
        _signature = sig;
        SetRowCount(row);
        RowCount = row;
    }

    /// <summary>
    /// Draw the mod-drawn rows from <c>UIManager.MissionObjectiveContainer</c>'s live row widgets
    /// (active instances only, in sibling = display order) plus its quest header. Returns false when
    /// the container is not available so the caller can fall back to the model scan.
    /// </summary>
    private bool RowsFromLiveContainer(int party)
    {
        UIManager? manager = UIManager.Instance;
        MissionObjectiveContainer? container = manager != null ? manager.MissionObjectiveContainer : null;
        if (container == null)
            return false;
        List<MissionObjectiveUI> instances = container.ObjectiveInstances;
        if (instances == null)
            return false;

        // Quest header (campaign/guildmaster): the scenario/quest name row the owner's panel
        // shows above the rows — mirrored into this panel's title when active.
        string? quest = null;
        try
        {
            UIScenarioQuest header = container.questHeader;
            if (header != null && header.gameObject.activeSelf && header.questText != null)
                quest = header.questText.text;
        }
        catch { quest = null; }

        _mirrorRows.Clear();
        for (int i = 0; i < instances.Count; i++)
        {
            MissionObjectiveUI ui = instances[i];
            if (ui == null || ui.m_Objective == null || !ui.gameObject.activeSelf)
                continue;
            _mirrorRows.Add(ui);
        }
        // Display order = sibling order (the container inserts with SetAsFirstSibling).
        _mirrorRows.Sort(static (a, b) =>
            a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));

        RefreshTitle(quest);

        var sb = new StringBuilder(96);
        if (quest != null)
            sb.Append('#').Append(quest).Append(';');
        int row = 0;
        for (int i = 0; i < _mirrorRows.Count && row < MaxRows; i++)
        {
            CObjective o = _mirrorRows[i].m_Objective;

            string text;
            try { text = LocalizationObjectiveConveter.LocalizeText(o, initialState: false) ?? (o.LocKey ?? "?"); }
            catch { text = "?"; }

            int total = 1, current = o.IsComplete ? 1 : 0;
            try { o.GetObjectiveProgress(party, out total, out current); }
            catch { /* degrade to the complete/incomplete pip */ }

            _rows[row].Set(text, current, total, o.IsComplete);
            sb.Append(text).Append('|').Append(current).Append('/').Append(total)
              .Append(o.IsComplete ? '+' : '-').Append(';');
            row++;
        }

        string sig = sb.ToString();
        if (sig == _signature)
            return true;
        _signature = sig;
        SetRowCount(row);
        RowCount = row;
        return true;
    }

    /// <summary>Change-gated title write: the mirrored quest header when the owner's panel shows
    /// one, else the mod's own localized caption.</summary>
    private void RefreshTitle(string? quest)
    {
        string want = string.IsNullOrEmpty(quest)
            ? Loc.Mod("objectives").ToUpperInvariant()
            : quest!;
        if (want == _shownTitle)
            return;
        _shownTitle = want;
        RemoteBoardContent.SetText(_title, want);
    }

    /// <summary>Fill rows from one objective list; returns the next free row index. The signature
    /// string accumulates exactly what is DRAWN, so the repaint gate fires on a real change only.</summary>
    private int Append(List<CObjective>? list, int party, StringBuilder sig, int row)
    {
        if (list == null)
            return row;
        for (int i = 0; i < list.Count && row < MaxRows; i++)
        {
            CObjective o = list[i];
            if (o == null)
                continue;
            // The container's own filter (MissionObjectiveContainer.InitialiseObjective).
            string key;
            try { key = o.LocKey ?? string.Empty; }
            catch { continue; }
            if (key.Length == 0 || !o.IsActive || o.IsHidden)
                continue;

            string text;
            try { text = LocalizationObjectiveConveter.LocalizeText(o, initialState: false) ?? key; }
            catch { text = key; }

            int total = 1, current = o.IsComplete ? 1 : 0;
            try { o.GetObjectiveProgress(party, out total, out current); }
            catch { /* degrade to the complete/incomplete pip */ }

            _rows[row].Set(text, current, total, o.IsComplete);
            sig.Append(text).Append('|').Append(current).Append('/').Append(total)
               .Append(o.IsComplete ? '+' : '-').Append(';');
            row++;
        }
        return row;
    }

    /// <summary>Show <paramref name="n"/> rows, resize the backing plate around them and re-centre
    /// the block (right-centre mount origin, like the local dock).</summary>
    private void SetRowCount(int n)
    {
        if (n == _shownRows)
            return;
        _shownRows = n;
        RowCount = n;

        // The FALLBACK subtree is what appears/disappears with the row count; the panel ROOT also
        // hosts the widget mirror, so hiding the root here would hide the real widget too.
        bool any = n > 0;
        if (!_root.gameObject.activeSelf && any)
            _root.gameObject.SetActive(true);
        if (_fallbackRoot.gameObject.activeSelf != any)
            _fallbackRoot.gameObject.SetActive(any);

        float titleH = RowHeight;
        float total = titleH + n * RowHeight;
        _plate.transform.localScale = new Vector3(Width, Mathf.Max(total + 0.010f, RowHeight), 1f);
        _plate.transform.localPosition = new Vector3(0f, 0f, 0.001f); // behind the text

        float top = total * 0.5f;
        _title.transform.localPosition = new Vector3(0f, top - titleH * 0.5f, 0f);
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Place(i < n, new Vector3(0f, top - titleH - (i + 0.5f) * RowHeight, 0f));
    }

    /// <summary>One objective line: the localized text, a "current/total" counter and a progress
    /// bar. The bar is hidden for one-shot objectives (total ≤ 1), exactly like
    /// <c>MissionObjectiveUI.UpdateMissionProgress</c> hides its own.</summary>
    private sealed class Row
    {
        private readonly Transform _root;
        private readonly TextMeshPro _text;
        private readonly TextMeshPro _count;
        private readonly MeshRenderer _barBg;
        private readonly MeshRenderer _barFill;
        private readonly float _width;

        public Row(Transform parent, float width)
        {
            _width = width;
            _root = new GameObject("Row").transform;
            _root.SetParent(parent, worldPositionStays: false);

            _text = RemoteBoardContent.Label(_root, "Text",
                new Vector3(-width * 0.5f + width * 0.36f, 0.004f, 0f),
                new Vector2(width * 0.70f, 0.018f), 0.038f,
                new Color(0.90f, 0.88f, 0.80f), TextAlignmentOptions.Left, wrap: false);
            _count = RemoteBoardContent.Label(_root, "Count",
                new Vector3(width * 0.5f - width * 0.11f, 0.004f, 0f),
                new Vector2(width * 0.20f, 0.018f), 0.038f,
                new Color(1f, 0.92f, 0.70f), TextAlignmentOptions.Right, FontStyles.Bold);

            _barBg = BoardVisual.Quad(_root, "BarBg", new Vector2(width * 0.90f, BarHeight),
                BoardVisual.Unlit(new Color(0.18f, 0.17f, 0.15f, 1f)));
            _barBg.transform.localPosition = new Vector3(0f, -0.009f, 0.0005f);
            _barFill = BoardVisual.Quad(_root, "BarFill", new Vector2(width * 0.90f, BarHeight),
                BoardVisual.Unlit(new Color(0.72f, 0.60f, 0.25f, 1f)));
            _barFill.transform.localPosition = new Vector3(0f, -0.009f, 0f);
        }

        public void Place(bool visible, Vector3 localPos)
        {
            if (_root.gameObject.activeSelf != visible)
                _root.gameObject.SetActive(visible);
            _root.localPosition = localPos;
        }

        public void Set(string text, int current, int total, bool complete)
        {
            RemoteBoardContent.SetText(_text, text);
            // '✓' is already proven in this font (the local board's confirmed CONFIRM label uses it).
        // A one-shot objective that is NOT complete shows no counter at all — its text colour
        // already carries the state, and inventing a bullet glyph risks a tofu box.
        RemoteBoardContent.SetText(_count, complete ? "✓" : total > 1 ? $"{current}/{total}" : string.Empty);
            _text.color = complete ? new Color(0.65f, 0.85f, 0.55f) : new Color(0.90f, 0.88f, 0.80f);

            bool bar = total > 1 && !complete;
            if (_barBg.gameObject.activeSelf != bar) _barBg.gameObject.SetActive(bar);
            if (_barFill.gameObject.activeSelf != bar) _barFill.gameObject.SetActive(bar);
            if (!bar)
                return;
            float f = Mathf.Clamp01(total > 0 ? (float)current / total : 0f);
            float full = _width * 0.90f;
            _barFill.transform.localScale = new Vector3(Mathf.Max(full * f, 1e-4f), BarHeight, 1f);
            // Left-aligned growth: the quad is centred, so shift it by half the missing length.
            _barFill.transform.localPosition = new Vector3(-full * 0.5f + full * f * 0.5f, -0.009f, 0f);
        }
    }
}
