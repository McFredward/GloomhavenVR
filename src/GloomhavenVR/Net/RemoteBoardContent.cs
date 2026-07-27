using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The MOD-DRAWN reproductions of everything a peer's control board shows beyond their two round
/// cards — the "alles was am Controllboard angezeigt ist soll auch beim fremden Controllboard
/// sichtbar sein" requirement. All of it is rendered at the peer's board pose by
/// <see cref="RemoteControlBoard"/>; NOTHING here rides the wire.
///
/// WHY A REPRODUCTION AND NOT THE GAME'S OWN CANVAS: the game instantiates exactly ONE objectives
/// container (<c>UIManager.MissionObjectiveContainer</c>), ONE infusion board
/// (<c>InfusionBoardUI.Instance</c>) and ONE initiative track (<c>InitiativeTrack.Instance</c>) per
/// client, and the local board already docks those single instances onto ITS mounts
/// (WorldUI TrayMountedPanelSurface). A canvas cannot be in two places at once, and re-parenting or
/// duplicating a live game canvas would violate the module's reversibility rule. So a peer's board
/// draws its own picture from the SAME model data the local panels are fed from — a read-only
/// mirror, exactly like <see cref="RemoteControlBoard"/>'s round-card panels.
///
/// FOUR DATA CLASSES. Two of them are zero-wire and are what THIS file draws; the other two exist
/// on a remote board too, and are named here because the first question about any new remote-board
/// content is which of the four it is. Every remote-content type carries the answer as a greppable
/// <c>CLASSIFICATION:</c> tag on its own doc comment — <c>grep -rn "CLASSIFICATION:" Net/</c>.
///   • GLOBAL (zero wire) — objectives, element infusions, round number. Scenario-wide state that is
///     bit-identical on every client (<c>ScenarioManager.CurrentScenarioState</c>,
///     <c>ElementInfusionBoardManager</c>, <c>Choreographer</c>), so a peer's board just has to
///     RENDER it at their pose.
///   • PER-ACTOR MODEL (zero wire) — initiative, pile counts, rest state, active cards. Read locally
///     off the already-host-replicated <c>CPlayerActor.CharacterClass</c>. Everything that the
///     vanilla client itself hides during the secret selection phase goes through
///     <see cref="RevealGate"/>; the rest is information vanilla already gives away for free (see
///     the per-section notes).
///   • VR-ONLY (costs wire bytes) — facts that exist NOWHERE in the game model: the board's world
///     pose and scale, the chosen board style, hand/head poses, the reading fans, card-FX events.
///     Nothing in THIS file is VR-only; the pose everything here is drawn at comes from
///     <see cref="RemoteControlBoard"/>, which is.
///   • DELIBERATELY-NOT (costs 0 B by decision) — knowable, but not worth a field or not safe to
///     leak. <see cref="RemoteBoardFurniture"/>'s "NEUTRAL LOOKS" and "LOCAL-ONLY STATE" blocks are
///     this class, not a fifth thing: button enabled-states, the local player's own tuning offsets,
///     and card IDENTITY, which never crosses the wire in any form.
///
/// WHY THE HEADER NAMES ALL FOUR AND NOT JUST THIS FILE'S TWO: the decision rule for new remote
/// content is "GLOBAL or PER-ACTOR MODEL by default; VR-ONLY must be justified", and there is
/// exactly ONE free bit left in the whole protocol
/// (<see cref="NetProtocol.PileBrowseReservedBit"/>). A reader who learns only that remote content
/// is "zero-wire" has no framework for the one question they must answer first. See
/// <c>.planning/refactor/INVARIANTS-Net-Rig.md</c> "Net — content classification".
///
/// Style: unlit (<see cref="BoardVisual"/>) like every other remote-board visual, change-gated TMP
/// writes (a per-frame <c>TMP.text</c> assignment re-triggers auto-size layout — the badge-flicker
/// lesson), and content re-read on a 4 Hz cadence rather than per frame so a table of four peers
/// costs nothing measurable.
/// </summary>
/// <remarks>CLASSIFICATION: n/a — this type is the shared TMP/label plumbing for the widgets below,
/// not content of its own. Each widget carries its own CLASSIFICATION tag.</remarks>
internal static class RemoteBoardContent
{
    /// <summary>Content re-read cadence (seconds). The board POSE follows every frame; only the
    /// model reads + TMP rebuilds are throttled.</summary>
    internal const float DefaultRefreshSeconds = 0.25f;

    /// <summary>
    /// EFFECTIVE content re-read cadence. [Optimize] RemoteContentInterval can widen it: this walk
    /// scales with the number of PEERS (every remote board's model reads, furniture, card faces,
    /// fans and FX ride this one cadence), so on a four-player table it is the mod cost that grows
    /// while a single-player capture shows nothing at all. 0 in config = keep the 0.25 s default,
    /// which is what ships — the trade it buys is purely how fast a PEER's board contents catch up,
    /// never anything about the local player's own board, and it is worth nothing in single player.
    /// </summary>
    internal static float RefreshSeconds
    {
        get
        {
            float over = Core.PerfConfig.RemoteContentSeconds;
            return over > 0f ? over : DefaultRefreshSeconds;
        }
    }

    /// <summary>A fitted, unlit world-space label under <paramref name="parent"/>. Shared by every
    /// section below so the remote board's typography is consistent with the local one.</summary>
    internal static TextMeshPro Label(Transform parent, string name, Vector3 localPos, Vector2 box,
        float maxFont, Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal,
        bool wrap = false)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = localPos;
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = align;
        tmp.color = color;
        tmp.fontStyle = style;
        TmpFit.Fit(tmp, box.x, box.y, maxFont, wrap);
        return tmp;
    }

    /// <summary>Change-gated TMP write (see the class note on auto-size churn).</summary>
    internal static void SetText(TextMeshPro? tmp, string text)
    {
        if (tmp != null && tmp.text != text)
            tmp.text = text;
    }
}

// =================================================================================================
//  Objectives ("Aufgaben") — GLOBAL
// =================================================================================================

/// <summary>
/// The scenario objectives with their progress, drawn off the LEFT edge of a peer's board — the
/// mirror of the local board's docked <c>ObjectivesSurface</c> (same right-centre origin growing
/// left, same base offset <c>PlayTray.ObjectivesMountBase</c>, so a peer's task list hangs exactly
/// where that player sees their own).
///
/// SOURCE (global, zero wire): <c>ScenarioManager.CurrentScenarioState.WinObjectives/LoseObjectives</c>
/// — the same lists <c>MissionObjectiveContainer.Init</c> feeds its rows from, filtered by the same
/// rule it uses (<c>LocKey != "" &amp;&amp; IsActive &amp;&amp; !IsHidden</c>,
/// MissionObjectiveContainer.InitialiseObjective). Text comes from the game's own extension
/// <c>LocalizationObjectiveConveter.LocalizeText(objective, initialState: false)</c> — the identical
/// call <c>MissionObjectiveUI.UpdateMissionText</c> makes, so the wording (incl. the "X remaining"
/// live counts) matches the local panel word for word in every shipped language. Progress comes from
/// <c>CObjective.GetObjectiveProgress(partySize, out total, out current)</c>, the identical call
/// <c>MissionObjectiveUI.UpdateMissionProgress</c> makes.
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

    /// <summary>How many objective rows are currently drawn (diagnostics).</summary>
    public int RowCount { get; private set; }

    public RemoteObjectivesPanel(Transform boardRoot)
    {
        _root = new GameObject("Objectives").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(MountX - Width * 0.5f, 0f, RemoteControlBoard.ProudZLocal);

        _plate = BoardVisual.Quad(_root, "Plate", new Vector2(Width, RowHeight),
            BoardVisual.Unlit(new Color(0.07f, 0.07f, 0.06f, 0.92f)));

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

// =================================================================================================
//  Element infusions ("Elemente") — GLOBAL
// =================================================================================================

/// <summary>
/// The element infusion board, drawn in the LEFT column below the objectives — the mirror of the
/// local board's docked <c>ElementBoardSurface</c> (same <c>PlayTray.ElementMountBase</c> offset).
///
/// SOURCE (global, zero wire): <c>ElementInfusionBoardManager.ElementColumn(EElement)</c>, the exact
/// static the game's own <c>InfusionBoardUI.UpdateBoard</c> reads to decide each chip's state. The
/// infusion table is scenario-wide and identical on every client, so this needs no traffic and
/// reveals nothing.
///
/// PRESENTATION mirrors vanilla: an INERT element is not drawn at all (InfusionBoardUI
/// <c>SetActive(false)</c>s it), STRONG draws at full colour, WANING dimmed and smaller — which is
/// what the game's strong/waning sprite pair conveys. Chip colours come from the game's own
/// <c>UIInfoTools.GetElementHighlightColor</c> when that singleton is up, with a hardcoded fallback
/// so the strip still reads in the menu/loading window where UIInfoTools is absent.
/// </summary>
/// <remarks>CLASSIFICATION: GLOBAL — scenario-wide state, bit-identical on every client, ZERO wire.
/// Source: <c>ElementInfusionBoardManager.ElementColumn</c>. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteElementStrip
{
    private const float MountX = -RemoteControlBoard.BoardHalfW - 0.012f;
    private const float Width = 0.26f;
    private const float ChipSize = 0.030f;
    private const float ChipStep = 0.038f;

    /// <summary>Column Y below the objectives dock — mirrors PlayTray.ElementMountBase
    /// (ObjectivesMountMaxHeight/2 + 0.012 + ElementMountMaxHeight/2 = 0.16 + 0.012 + 0.06).</summary>
    private const float MountY = -0.232f;

    private static readonly Color[] Fallback =
    {
        new(0.95f, 0.40f, 0.15f), // Fire
        new(0.45f, 0.80f, 1.00f), // Ice
        new(0.72f, 0.80f, 0.86f), // Air
        new(0.45f, 0.72f, 0.30f), // Earth
        new(1.00f, 0.95f, 0.58f), // Light
        new(0.56f, 0.40f, 0.82f), // Dark
    };

    private readonly Transform _root;
    private readonly MeshRenderer[] _chips = new MeshRenderer[6];
    private readonly Material[] _mats = new Material[6];
    private int _signature = -1;

    /// <summary>How many non-inert elements the strip currently draws (diagnostics).</summary>
    public int ActiveCount { get; private set; }

    public RemoteElementStrip(Transform boardRoot)
    {
        _root = new GameObject("Elements").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(MountX - Width * 0.5f, MountY, RemoteControlBoard.ProudZLocal);

        for (int i = 0; i < 6; i++)
        {
            _mats[i] = BoardVisual.Unlit(Fallback[i]);
            _chips[i] = BoardVisual.Quad(_root, $"Element_{(ElementInfusionBoardManager.EElement)i}",
                new Vector2(ChipSize, ChipSize), _mats[i]);
            _chips[i].gameObject.SetActive(false);
        }
    }

    /// <summary>Re-read the infusion table and repaint on an actual change.</summary>
    public void Refresh()
    {
        int sig = 0;
        int visible = 0;
        var state = new ElementInfusionBoardManager.EColumn[6];
        for (int i = 0; i < 6; i++)
        {
            ElementInfusionBoardManager.EColumn col;
            try { col = ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i); }
            catch { col = ElementInfusionBoardManager.EColumn.Inert; }
            state[i] = col;
            sig = sig * 3 + (int)col;
            if (col != ElementInfusionBoardManager.EColumn.Inert)
                visible++;
        }
        if (sig == _signature)
            return;
        _signature = sig;
        ActiveCount = visible;

        // Pack the visible chips left-to-right and centre the run, exactly like the game's own
        // horizontal element holder does with its layout group.
        float left = -(visible - 1) * 0.5f * ChipStep;
        int slot = 0;
        for (int i = 0; i < 6; i++)
        {
            bool on = state[i] != ElementInfusionBoardManager.EColumn.Inert;
            if (_chips[i].gameObject.activeSelf != on)
                _chips[i].gameObject.SetActive(on);
            if (!on)
                continue;
            bool strong = state[i] == ElementInfusionBoardManager.EColumn.Strong;
            Color c = ColorFor((ElementInfusionBoardManager.EElement)i, i);
            _mats[i].color = strong ? c : new Color(c.r * 0.55f, c.g * 0.55f, c.b * 0.55f, 0.80f);
            float s = strong ? ChipSize : ChipSize * 0.74f;
            _chips[i].transform.localScale = new Vector3(s, s, 1f);
            _chips[i].transform.localPosition = new Vector3(left + slot * ChipStep, 0f, 0f);
            slot++;
        }
    }

    private static Color ColorFor(ElementInfusionBoardManager.EElement e, int index)
    {
        try
        {
            if (UIInfoTools.Instance != null)
                return UIInfoTools.Instance.GetElementHighlightColor(e, 1f);
        }
        catch { /* menu / loading window — fall through */ }
        return Fallback[index];
    }

}

// =================================================================================================
//  Round number / initiative / rest — GLOBAL + PER-ACTOR MODEL
// =================================================================================================

/// <summary>
/// The three small readouts on a peer's board frame:
///
/// ROUND (GLOBAL) — "Runde N" on the top-right, the mirror of the local board's own round readout
/// (<c>PlayTray.BuildRoundReadout</c>) reading the SAME state through
/// <c>CardsGameApi.RoundNumber()</c> (<c>Choreographer.m_CurrentState.RoundNumber</c>) and the SAME
/// <c>GUI_START_ROUND_BANNER</c> localization. Scenario-wide: no wire, no secret.
///
/// INITIATIVE (PER-ACTOR MODEL, gated) — the peer's initiative number. This is what their docked
/// initiative track shows for them, and the anti-cheat rule is copied verbatim from the vanilla
/// widget: <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" while
/// <c>FFSNetwork.IsOnline &amp;&amp; phase == SelectAbilityCardsOrLongRest &amp;&amp;
/// !actor.IsUnderMyControl</c> — which is precisely <see cref="RevealGate.ShowRoundCardFronts"/>.
/// So this shows "?" in exactly the frames vanilla shows "?", and the real number in exactly the
/// frames vanilla already shows it on the shared initiative track. No new information exists.
///
/// REST (PER-ACTOR MODEL, split gate) — <c>CCharacterClass.HasShortRested</c> /
/// <c>HasLongRested</c> are PAST-TENSE facts about a rest that already resolved in front of
/// everybody, so they are shown unconditionally. <c>CCharacterClass.LongRest</c> is the PENDING
/// long-rest SELECTION and is therefore secret during the selection phase — vanilla gates its own
/// display of it on the same condition (<c>InitiativeTrackActorAvatar</c>: the long-rest branch is
/// inside the <c>flag</c> = not-hidden test), so this one goes through
/// <see cref="RevealGate.ShowRoundCardFronts"/> as well.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (GLOBAL + PER-ACTOR MODEL) — ZERO wire either way. The ROUND
/// number is GLOBAL (<c>CardsGameApi.RoundNumber()</c>); the INITIATIVE number and the REST state
/// are PER-ACTOR MODEL, read off the host-replicated <c>CPlayerActor.CharacterClass</c> via
/// <c>NetPlayerActors.ActorFor</c> and gated by <see cref="RevealGate"/> (rest uses the SPLIT gate
/// described above). One of the three genuinely MIXED types — which is why the classification is a
/// doc tag and not a marker interface: an interface would have to lie about this one. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteStatusReadouts
{
    private readonly TextMeshPro _round;
    private readonly TextMeshPro _initiative;
    private readonly TextMeshPro _rest;
    private readonly Transform _restRoot;

    private int _roundShown = int.MinValue;
    private string _langShown = string.Empty;

    /// <summary>Last rendered values, for the change-gated diagnostic line.</summary>
    public string InitiativeText { get; private set; } = "?";
    public string RestText { get; private set; } = string.Empty;
    public string RoundText { get; private set; } = "-";

    public RemoteStatusReadouts(Transform boardRoot)
    {
        // --- round: top-right, mirroring PlayTray.ReadoutBase (ButtonZoneX 0.235, y 0.125).
        var roundRoot = new GameObject("RoundReadout").transform;
        roundRoot.SetParent(boardRoot, worldPositionStays: false);
        roundRoot.localPosition = new Vector3(0.235f, 0.125f, RemoteControlBoard.ProudZLocal);
        BoardVisual.Quad(roundRoot, "Plate", new Vector2(0.13f, 0.036f),
            BoardVisual.Unlit(new Color(0.12f, 0.11f, 0.10f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        _round = RemoteBoardContent.Label(roundRoot, "Text", Vector3.zero,
            new Vector2(0.12f, 0.028f), 0.06f,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Center);
        RemoteBoardContent.SetText(_round, "-");

        // --- initiative: top-centre, in the strip between the round-card tops (y 0.104) and the
        //     board's top edge (y 0.16) — where the local board's docked initiative track sits.
        var iniRoot = new GameObject("InitiativeReadout").transform;
        iniRoot.SetParent(boardRoot, worldPositionStays: false);
        iniRoot.localPosition = new Vector3(0f, 0.132f, RemoteControlBoard.ProudZLocal);
        BoardVisual.Quad(iniRoot, "Plate", new Vector2(0.11f, 0.042f),
            BoardVisual.Unlit(new Color(0.12f, 0.11f, 0.10f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        RemoteBoardContent.Label(iniRoot, "Caption", new Vector3(-0.035f, 0f, 0f),
            new Vector2(0.034f, 0.026f), 0.035f,
            new Color(0.75f, 0.70f, 0.60f), TextAlignmentOptions.Center).text = "INI";
        _initiative = RemoteBoardContent.Label(iniRoot, "Value", new Vector3(0.016f, 0f, 0f),
            new Vector2(0.060f, 0.034f), 0.075f,
            new Color(1f, 0.93f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold);
        RemoteBoardContent.SetText(_initiative, "?");

        // --- rest: bottom-left, the local board's rest zone. Collision budget (board-local, the
        //     same arithmetic PlayTray.BuildMounts documents for its own furniture): the plate spans
        //     x −0.295..−0.105 and y −0.146..−0.110, so it clears the round-card slot above it
        //     (slot 0 bottom edge y −0.104) by 6 mm and stays inside the board (bottom edge −0.16).
        _restRoot = new GameObject("RestReadout").transform;
        _restRoot.SetParent(boardRoot, worldPositionStays: false);
        _restRoot.localPosition = new Vector3(-0.20f, -0.128f, RemoteControlBoard.ProudZLocal);
        BoardVisual.Quad(_restRoot, "Plate", new Vector2(0.19f, 0.036f),
            BoardVisual.Unlit(new Color(0.14f, 0.12f, 0.10f, 1f)))
            .transform.localPosition = new Vector3(0f, 0f, 0.001f);
        _rest = RemoteBoardContent.Label(_restRoot, "Text", Vector3.zero,
            new Vector2(0.18f, 0.028f), 0.05f,
            new Color(0.95f, 0.86f, 0.62f), TextAlignmentOptions.Center, FontStyles.Bold);
        _restRoot.gameObject.SetActive(false);
    }

    /// <summary>Re-read round / initiative / rest. <paramref name="showFronts"/> is the shared
    /// <see cref="RevealGate"/> answer for this actor.</summary>
    public void Refresh(CPlayerActor actor, bool showFronts)
    {
        // Language change invalidates the cached round string (the local board does the same).
        string lang = Loc.CurrentLanguage;
        if (lang != _langShown)
        {
            _langShown = lang;
            _roundShown = int.MinValue;
        }

        int round = CardsGameApi.RoundNumber();
        if (round != _roundShown)
        {
            _roundShown = round;
            string text;
            if (round <= 0)
            {
                text = "-";
            }
            else
            {
                try { text = string.Format(Loc.Game("GUI_START_ROUND_BANNER", "Round {0}"), round); }
                catch (System.FormatException) { text = $"Round {round}"; }
            }
            RoundText = text;
            RemoteBoardContent.SetText(_round, text);
        }

        // Initiative — vanilla's own rule, verbatim (see the class note).
        string ini = "?";
        if (showFronts)
        {
            int value = 0;
            try { value = actor.Initiative(); }
            catch { value = 0; }
            if (value != 0)
                ini = value.ToString();
        }
        InitiativeText = ini;
        RemoteBoardContent.SetText(_initiative, ini);

        // Rest — past-tense facts always, the pending long-rest SELECTION only once revealed.
        CCharacterClass cc = actor.CharacterClass;
        string rest = string.Empty;
        if (cc != null)
        {
            if (cc.HasLongRested)
                rest = Loc.Game("GUI_LONG_REST", "Long rest");
            else if (cc.HasShortRested)
                rest = Loc.Mod("short_rest");
            else if (showFronts && cc.LongRest)
                rest = Loc.Game("GUI_LONG_REST", "Long rest");
        }
        RestText = rest;
        bool show = rest.Length > 0;
        if (_restRoot.gameObject.activeSelf != show)
            _restRoot.gameObject.SetActive(show);
        if (show)
            RemoteBoardContent.SetText(_rest, rest.ToUpperInvariant());
    }
}

// =================================================================================================
//  Initiative TRACK — GLOBAL actor list + PER-ACTOR initiative (vanilla's own gate)
// =================================================================================================

/// <summary>
/// The scenario's INITIATIVE TRACK, drawn above a peer's board — the mirror of the local board's
/// docked <c>InitiativeTrack</c> canvas (<c>PlayTray.InitiativeMount</c>).
///
/// WHY IT IS HERE NOW. The previous parity pass rendered only the peer's own initiative NUMBER and
/// left the track out, arguing that its portraits are CLICKABLE (they switch the local player's
/// selected character / open the card overview). As a purely NON-INTERACTIVE picture that objection
/// disappears — nothing drawn here has a collider, so there is nothing to click — and the user's
/// standing requirement is that every element of the control board is represented on a peer's board.
///
/// SOURCE (global list + per-actor numbers, zero wire): <c>ScenarioManager.Scenario.AllAliveActors</c>
/// filtered and DEDUPED BY <c>CActor.Class</c>, which is exactly what
/// <c>InitiativeTrack.UpdateInitiativeTrack</c> does when it builds its entries (it skips hero
/// summons and prop/object actors and keeps one entry per <c>CClass</c>, so a monster GROUP is one
/// row). Order mirrors <c>InitiativeTrackActorBehaviour.CompareTo</c>'s effect: ascending
/// <c>CActor.Initiative()</c>, with every entry whose initiative is not (yet) knowable pushed to the
/// end — the same place vanilla's negative order-priorities put them.
///
/// ANTI-CHEAT — vanilla's rule, verbatim. A foreign player's initiative reads "?" while
/// <c>FFSNetwork.IsOnline &amp;&amp; phase == SelectAbilityCardsOrLongRest &amp;&amp;
/// !actor.IsUnderMyControl</c> (<c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> /
/// <c>InitiativeTrackActorAvatar</c>) — which is precisely <see cref="RevealGate.ShowRoundCardFronts"/>.
/// Monsters follow vanilla's own numeric rule (&lt;0 blank, 0 → "?", else the number), i.e. their
/// initiative appears only once their ability card has been revealed to everyone. So this shows a
/// number in exactly the frames the shared 2D track already shows it, and "?" in exactly the frames
/// it shows "?". No new information exists anywhere in this panel.
///
/// NOT REPRODUCED — the PORTRAIT. Vanilla's entry paints a character portrait fetched through
/// <c>CharacterPortraitsProvider</c> from the <c>misc_characterportraits</c> asset bundle; reaching
/// that per-actor texture from the mod is neither cheap nor cheat-relevant, and vanilla itself only
/// reveals the actor's NAME on hover. Each chip therefore carries the localized actor name
/// (<c>CActor.ActorLocKey()</c>, the identical string vanilla's own <c>nameText</c> uses) over a
/// class-tinted plate, which is the readable equivalent at VR distance.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (GLOBAL + PER-ACTOR MODEL) — ZERO wire either way. The actor LIST
/// and its order are GLOBAL (<c>ScenarioManager.Scenario.AllAliveActors</c>, deduped by
/// <c>CActor.Class</c>); the per-actor initiative NUMBERS are PER-ACTOR MODEL, gated by
/// <see cref="RevealGate"/> with monsters following vanilla's own numeric rule. Portraits are
/// DELIBERATELY-NOT reproduced (see NOT REPRODUCED above). One of the three genuinely MIXED types.
/// See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteInitiativeTrack
{
    /// <summary>Bottom-centre origin above the board's top edge. The local dock sits at
    /// <c>PlayTray.InitiativeMountY</c> (0.10) and grows up; this one is pushed to 0.165 so it
    /// clears the mod's own INI badge (<see cref="RemoteStatusReadouts"/>, y 0.132 ± 0.021) which
    /// already occupies the lower half of that mount on a remote board.</summary>
    private const float MountY = 0.165f;

    /// <summary>Track width — <c>PlayTray.InitiativeMountWidth</c> (= the board width).</summary>
    private const float Width = 0.64f;

    private const float ChipH = 0.052f;
    private const float MaxChipW = 0.082f;
    private const int MaxChips = 8;

    private readonly Transform _root;
    private readonly Chip[] _chips = new Chip[MaxChips];
    private readonly List<CActor> _entries = new(MaxChips);
    private readonly List<CClass> _seen = new(MaxChips);

    private string _signature = string.Empty;

    /// <summary>How many chips the track currently draws (diagnostics).</summary>
    public int Count { get; private set; }

    public RemoteInitiativeTrack(Transform boardRoot)
    {
        _root = new GameObject("InitiativeTrack").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(0f, MountY + ChipH * 0.5f, RemoteControlBoard.ProudZLocal);

        for (int i = 0; i < MaxChips; i++)
            _chips[i] = new Chip(_root);

        _root.gameObject.SetActive(false);
    }

    /// <summary>Re-read the scenario's actor list + initiatives and repaint on an actual change.
    /// Wrapped whole: a half-initialised scenario must degrade to an empty track, never throw.</summary>
    public void Refresh()
    {
        _entries.Clear();
        _seen.Clear();
        try { Collect(); }
        catch { _entries.Clear(); }

        // Ascending initiative; everything not (yet) knowable to the end — the effect of vanilla's
        // own negative order priorities for actors whose initiative is unknown.
        _entries.Sort(static (a, b) => SortKey(a).CompareTo(SortKey(b)));

        var sb = new StringBuilder(96);
        for (int i = 0; i < _entries.Count; i++)
            sb.Append(Text(_entries[i])).Append(':').Append(InitiativeLabel(_entries[i])).Append(';');
        string sig = sb.ToString();
        if (sig == _signature)
            return;
        _signature = sig;

        Count = _entries.Count;
        bool any = Count > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);

        float chipW = Mathf.Min(MaxChipW, Count > 0 ? Width / Count : MaxChipW);
        float left = -(Count - 1) * 0.5f * chipW;
        for (int i = 0; i < MaxChips; i++)
        {
            if (i >= Count)
            {
                _chips[i].SetShown(false);
                continue;
            }
            CActor a = _entries[i];
            _chips[i].Set(new Vector3(left + i * chipW, 0f, 0f), chipW, Text(a), InitiativeLabel(a),
                a is CPlayerActor);
        }
    }

    /// <summary>Filter + dedupe exactly like <c>InitiativeTrack.UpdateInitiativeTrack</c>: alive
    /// actors, one entry per <c>CClass</c>, no hero summons and no prop/object actors.</summary>
    private void Collect()
    {
        CScenario? scenario = ScenarioManager.Scenario;
        List<CActor>? actors = scenario?.AllAliveActors;
        if (actors == null)
            return;
        for (int i = 0; i < actors.Count && _entries.Count < MaxChips; i++)
        {
            CActor a = actors[i];
            if (a == null || a is CHeroSummonActor || a is CObjectActor)
                continue;
            CClass cls = a.Class;
            if (cls != null)
            {
                if (_seen.Contains(cls))
                    continue;
                _seen.Add(cls);
            }
            _entries.Add(a);
        }
    }

    /// <summary>Sort key: the actor's initiative, or a value past every real initiative when it is
    /// not knowable (gated player / monster card not yet revealed).</summary>
    private static int SortKey(CActor a)
    {
        string label = InitiativeLabel(a);
        return int.TryParse(label, out int v) ? v : int.MaxValue;
    }

    /// <summary>Vanilla's own display rule for the number (see the class note).</summary>
    private static string InitiativeLabel(CActor a)
    {
        try
        {
            if (a is CPlayerActor pa && !RevealGate.ShowRoundCardFronts(pa))
                return "?";
            int v = a.Initiative();
            if (v < 0)
                return string.Empty;
            return v == 0 ? "?" : v.ToString();
        }
        catch { return "?"; }
    }

    /// <summary>Localized actor name — the identical <c>ActorLocKey()</c> string vanilla's own entry
    /// puts in its (hover-only) name label.</summary>
    private static string Text(CActor a)
    {
        try
        {
            string key = a.ActorLocKey() ?? string.Empty;
            return key.Length == 0 ? "?" : Loc.Game(key, key);
        }
        catch { return "?"; }
    }

    /// <summary>One track entry: a tinted plate, the actor name and the initiative number. Inert —
    /// vanilla's entry is a button (character switch / card overview); this one is three quads.</summary>
    private sealed class Chip
    {
        private readonly Transform _root;
        private readonly MeshRenderer _plate;
        private readonly Material _plateMat;
        private readonly TextMeshPro _name;
        private readonly TextMeshPro _initiative;

        private static readonly Color PlayerTint = new(0.20f, 0.26f, 0.20f, 0.95f);
        private static readonly Color EnemyTint = new(0.28f, 0.16f, 0.15f, 0.95f);

        public Chip(Transform parent)
        {
            _root = new GameObject("Chip").transform;
            _root.SetParent(parent, worldPositionStays: false);

            _plateMat = BoardVisual.Unlit(PlayerTint);
            _plate = BoardVisual.Quad(_root, "Plate", new Vector2(MaxChipW * 0.94f, ChipH), _plateMat);
            _plate.transform.localPosition = new Vector3(0f, 0f, 0.001f);

            _initiative = RemoteBoardContent.Label(_root, "Initiative", new Vector3(0f, 0.010f, 0f),
                new Vector2(MaxChipW * 0.8f, 0.024f), 0.06f,
                new Color(1f, 0.93f, 0.72f), TextAlignmentOptions.Center, FontStyles.Bold);
            _name = RemoteBoardContent.Label(_root, "Name", new Vector3(0f, -0.014f, 0f),
                new Vector2(MaxChipW * 0.92f, 0.018f), 0.030f,
                new Color(0.86f, 0.83f, 0.75f), TextAlignmentOptions.Center);

            _root.gameObject.SetActive(false);
        }

        public void SetShown(bool shown)
        {
            if (_root.gameObject.activeSelf != shown)
                _root.gameObject.SetActive(shown);
        }

        public void Set(Vector3 localPos, float chipW, string name, string initiative, bool player)
        {
            SetShown(true);
            _root.localPosition = localPos;
            _plate.transform.localScale = new Vector3(chipW * 0.94f, ChipH, 1f);
            _plateMat.color = player ? PlayerTint : EnemyTint;
            RemoteBoardContent.SetText(_name, name);
            RemoteBoardContent.SetText(_initiative, initiative);
        }
    }
}

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
    private const float MountX = RemoteControlBoard.BoardHalfW + 0.012f + 0.17f;
    private const float CardW = 0.075f;
    private const float CardH = CardW * (88f / 63.5f);
    private const int Columns = 2;
    private const int MaxCards = 6;

    private readonly Transform _root;
    private readonly TextMeshPro _title;
    private readonly List<RemoteBoardCard> _cards = new(MaxCards);
    private readonly List<CAbilityCard> _buffer = new(MaxCards);

    /// <summary>How many active cards the column currently draws (diagnostics).</summary>
    public int Count { get; private set; }

    public RemoteActiveCards(Transform boardRoot)
    {
        _root = new GameObject("ActiveCards").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);
        _root.localPosition = new Vector3(MountX, 0f, RemoteControlBoard.ProudZLocal);

        _title = RemoteBoardContent.Label(_root, "Title", new Vector3(0f, 0.075f, 0f),
            new Vector2(0.09f, 0.024f), 0.045f,
            new Color(1f, 0.9f, 0.6f), TextAlignmentOptions.Center, FontStyles.Bold);
        RemoteBoardContent.SetText(_title, ActivePileViewer.Caption().ToUpperInvariant());

        for (int i = 0; i < MaxCards; i++)
            _cards.Add(new RemoteBoardCard(_root, Vector3.zero, CardW, CardH));

        _root.gameObject.SetActive(false);
    }

    /// <summary>Re-read the actor's active pile and repaint.</summary>
    public void Refresh(CPlayerActor actor, bool showFronts)
    {
        _buffer.Clear();
        try
        {
            CCharacterClass cc = actor.CharacterClass;
            List<CAbilityCard>? active = cc?.ActivatedAbilityCards;
            if (active != null)
            {
                for (int i = 0; i < active.Count && _buffer.Count < MaxCards; i++)
                    if (active[i] != null)
                        _buffer.Add(active[i]);
            }
        }
        catch { _buffer.Clear(); }

        Count = _buffer.Count;
        bool any = Count > 0;
        if (_root.gameObject.activeSelf != any)
            _root.gameObject.SetActive(any);
        if (!any)
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].Set(null, showFronts, actor);
            return;
        }

        // Follow the local title's language.
        RemoteBoardContent.SetText(_title, ActivePileViewer.Caption().ToUpperInvariant());

        int rows = (Count + Columns - 1) / Columns;
        float rowStep = CardH * 0.72f;   // rows overlap slightly, like the local active grid
        float colStep = CardW * 1.06f;
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
            _cards[i].Move(new Vector3(x, yTop - row * rowStep, -0.004f * row));
            // The ACTIVE column gets the same real-card treatment as the round slots: the actor is
            // handed through so its cards can be resolved to that player's own widgets. An active
            // card is public by definition (it was played face-up in front of everybody), and it is
            // still gated by the very same showFronts answer — the gate can only ever be stricter
            // than vanilla here, never looser.
            _cards[i].Set(_buffer[i], showFronts, actor);
        }
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

    private int _shownId = int.MinValue;
    private bool _shownFront;
    private bool _shownEmpty = true;
    private int _shownOwner = int.MinValue;

    /// <summary>Which path produced the face currently shown — surfaced to the board's diagnostics so
    /// a hardware log can state the FIDELITY per slot, not just that a card is drawn.</summary>
    public RemoteAbilityCardSource.FacePath Path { get; private set; }
        = RemoteAbilityCardSource.FacePath.None;

    public RemoteBoardCard(Transform parent, Vector3 localPos, float width, float height)
    {
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

        _bg = BoardVisual.Quad(_root.transform, "Face", new Vector2(width, height), _backMat);

        _initLabel = RemoteBoardContent.Label(_root.transform, "Initiative",
            new Vector3(0f, height * 0.34f, -0.001f),
            new Vector2(width * 0.9f, height * 0.28f), 0.09f,
            new Color(0.12f, 0.10f, 0.08f), TextAlignmentOptions.Center, FontStyles.Bold);
        _nameLabel = RemoteBoardContent.Label(_root.transform, "Name",
            new Vector3(0f, -height * 0.12f, -0.001f),
            new Vector2(width * 0.86f, height * 0.5f), 0.045f,
            new Color(0.14f, 0.11f, 0.09f), TextAlignmentOptions.Center, FontStyles.Normal, wrap: true);

        // Start HIDDEN and in step with the _shownEmpty seed: Set() early-returns while nothing
        // changed, so a panel that never receives a card (an unused active-grid cell, an empty
        // round slot) must not be left standing here showing a card back.
        _root.SetActive(false);
    }

    /// <summary>Re-seat the panel (the active-card grid relays its cards as the pile changes).</summary>
    public void Move(Vector3 localPos) => _root.transform.localPosition = localPos;

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
        _shownEmpty = empty;
        _shownId = id;
        _shownFront = front;
        _shownOwner = ownerId;

        if (empty)
        {
            // ANTI-CHEAT + hygiene: drop any hosted face BEFORE the slot goes away, so a slot that is
            // re-used for a different card (the active grid re-packs its cells) can never flash the
            // previous card's face.
            ClearFace();
            if (_root.activeSelf) _root.SetActive(false);
            return;
        }
        if (!_root.activeSelf) _root.SetActive(true);

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
            && Path == RemoteAbilityCardSource.FacePath.None)
            return; // already blank — nothing to undo

        ClearFace();
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

    /// <summary>Tear the hosted face down and forget which path drew it (the back/empty states must
    /// never report a fidelity path they are not showing).</summary>
    private void ClearFace()
    {
        _art?.HideFront();
        Path = RemoteAbilityCardSource.FacePath.None;
    }

    /// <summary>Destroy the slot's face host. Called when the owning board is torn down: the host is
    /// a child of <c>_root</c> and dies with it either way, but going through
    /// <see cref="RemoteCardArt.Destroy"/> keeps the "we own the clone, we destroy the clone"
    /// contract explicit rather than relying on hierarchy destruction order.</summary>
    public void Destroy()
    {
        _art?.Destroy();
        _art = null;
        Path = RemoteAbilityCardSource.FacePath.None;
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
}
