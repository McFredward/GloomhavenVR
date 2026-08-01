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
/// The scenario objectives with their progress, drawn off the LEFT edge of a peer's board — the
/// mirror of the local board's docked <c>ObjectivesSurface</c> (same right-centre origin growing
/// left, same base offset <c>PlayTray.ObjectivesMountBase</c>, so a peer's task list hangs exactly
/// where that player sees their own).
///
/// SOURCE (global, zero wire) — THE LIVE GAME PANEL, VERBATIM (defect 2 of the 1:1 parity round,
/// "Aufgaben falsch angezeigt"): the primary read is
/// <c>UIManager.Instance.MissionObjectiveContainer.ObjectiveInstances</c> — the very row widgets
/// the owner's board docks (<c>ObjectivesSurface</c> converts that same container) — mirrored in
/// their LIVE on-screen order (ascending sibling index; the container inserts rows with
/// <c>SetAsFirstSibling</c>, so lose objectives sit above win objectives, reversed within each
/// list) and only while each row is actually active. That inherits, for free, everything the old
/// derivation got wrong: rows the game REMOVED on completion (<c>CheckToRemoveObjectives</c> —
/// the owner's panel no longer shows them, this one kept drawing them ticked), runtime-added
/// rows (<c>AddObjective</c>), the display order, and the QUEST HEADER (the scenario/quest name
/// row the hardware session showed missing — mirrored from the container's own
/// <c>questHeader.questText</c> into this panel's title). Text still comes from the game's own
/// <c>LocalizationObjectiveConveter.LocalizeText(objective, initialState: false)</c> and progress
/// from <c>CObjective.GetObjectiveProgress</c> — the identical calls
/// <c>MissionObjectiveUI.UpdateMissionText/UpdateMissionProgress</c> make. The old
/// WinObjectives/LoseObjectives scan survives only as the FALLBACK for when the container does
/// not exist (mid-load).
///
/// ANTI-CHEAT: nothing here is per-player. The objectives are printed on the local player's own board
/// already; rendering the same list a second time at a peer's pose reveals literally nothing new.
///
/// Read-only throughout: <c>GetObjectiveProgress</c> is a pure out-parameter query and
/// <c>LocalizeText</c> only formats strings. Both are wrapped — a YML miss or a half-initialised
/// scenario state must degrade to a blank panel, never kill the remote-avatar tick.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide state, bit-identical on every client, ZERO wire.
/// Source: <c>ScenarioManager.CurrentScenarioState.WinObjectives/LoseObjectives</c> +
/// <c>CObjective.GetObjectiveProgress</c>. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemoteObjectivesPanel
{
    // Mirrors PlayTray.ObjectivesMountBase (-BoardW/2 - 0.012, 0, -0.004) + ObjectivesMountWidth.
    private const float MountX = -RemoteControlBoard.BoardHalfW - 0.012f;
    private const float Width = 0.26f;
    private const float RowHeight = 0.030f;
    private const float BarHeight = 0.005f;
    private const int MaxRows = 6;

    private readonly Transform _root;
    private readonly MeshRenderer _plate;
    private readonly TextMeshPro _title;
    private readonly List<Row> _rows = new(MaxRows);

    private string _signature = string.Empty;
    private int _shownRows = -1;
    private string _shownTitle = string.Empty;
    private readonly List<MissionObjectiveUI> _mirrorRows = new(MaxRows * 2);

    /// <summary>How many objective rows are currently drawn (diagnostics).</summary>
    public int RowCount { get; private set; }

    public RemoteObjectivesPanel(Transform boardRoot)
    {
        _root = new GameObject("Objectives").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(MountX - Width * 0.5f, 0f, RemoteControlBoard.ProudZLocal);

        _plate = BoardVisual.Quad(_root, "Plate", new Vector2(Width, RowHeight),
            BoardVisual.Unlit(new Color(0.07f, 0.07f, 0.06f, 0.92f)));
        WorldUI.MrBacking.Opacify(_plate.sharedMaterial); // 0.92 → 1 while MR is on (rows ride this plate)

        _title = RemoteBoardContent.Label(_root, "Title", Vector3.zero,
            new Vector2(Width * 0.9f, 0.020f), 0.05f,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Center, FontStyles.Bold);
        // The game has no single "objectives" header key; the panel's own quest header is a
        // campaign-only extra. Reuse the mod's task caption so it reads in the active language.
        RemoteBoardContent.SetText(_title, Loc.Mod("objectives").ToUpperInvariant());

        for (int i = 0; i < MaxRows; i++)
            _rows.Add(new Row(_root, Width));

        SetRowCount(0);
    }

    /// <summary>Re-read the scenario objectives and repaint on an actual change.</summary>
    public void Refresh()
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

        // PRIMARY: mirror the LIVE game container — the widget the owner's board actually shows
        // (see the class note). FALLBACK (container absent, mid-load): the old model scan.
        try
        {
            if (MirrorContainer(party))
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
    /// Mirror <c>UIManager.MissionObjectiveContainer</c>'s live rows (active instances only, in
    /// sibling = display order) plus its quest header. Returns false when the container is not
    /// available so the caller can fall back to the model scan.
    /// </summary>
    private bool MirrorContainer(int party)
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

        bool any = n > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);

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
