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
/// ModBuild 486: native widgets are the sole presentation. The historical procedural composition
/// below is retained only as uncalled diagnostic/source history; it is not an availability fallback.
/// The remote-only initiative badge is likewise permanently hidden.
///
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
/// SPECIAL RULES ("Spezialregeln") — a SECOND mirror in this same dock, stacked under the
/// objectives, over <c>UIManager.Instance.ScenarioModifierContainer</c>. See
/// <see cref="RefreshRules"/> for the mechanism and <c>WorldUI.Surfaces.ScenarioRulesSurface</c>
/// for the local half. Vanilla stacks the two the same way (<c>UIQuestDescription</c> draws
/// <c>specialRulesText</c> directly under <c>goalText</c>), which is what the user asked for:
/// "links bei den Questzielen auch solche 'Spezialregeln'".
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
    private readonly Transform _rulesRoot;
    private readonly RemoteWidgetMirror _rulesMirror;
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

    /// <summary>How many SPECIAL RULE rows the mirrored rules section is showing (diagnostics).
    /// 0 is a scenario with no special rules, which is a normal and common scenario.</summary>
    public int RuleRowCount { get; private set; }

    /// <summary>Which mechanism is drawing the SPECIAL RULES section (diagnostics). Only ever
    /// <c>MirroredWidget</c> or <c>None</c> — this section has no mod-drawn rung, see
    /// <see cref="RefreshRules"/>.</summary>
    public RemoteWidgetMirror.Fidelity RulesSource { get; private set; } =
        RemoteWidgetMirror.Fidelity.None;

    /// <summary>Why the real widget is not being mirrored, for the diagnostic line (empty when it is).</summary>
    // A source which never existed remains on normal recovery. A previously visible clone
    // destroyed by a native sync failure must rebuild on the next tick, regardless of traffic.
    internal bool HasMissingClone =>
        Source == RemoteWidgetMirror.Fidelity.MirroredWidget && !_mirror.HasLiveClone
        || RulesSource == RemoteWidgetMirror.Fidelity.MirroredWidget && !_rulesMirror.HasLiveClone;

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

    /// <summary>
    /// Gap between the objectives panel's bottom edge and the special-rules section's top edge,
    /// board-local metres.
    ///
    /// <para>MIRROR of <c>WorldUI.Surfaces.ScenarioRulesSurface.StackGapMeters</c>, linted as one
    /// value by <c>scripts/check-mirrors.sh</c>. A local copy for the same reason
    /// <see cref="ObjectivesDensityScale"/> above is one — reading a WorldUI surface's protected
    /// geometry from <c>Net/</c> is not possible — and linted because this number is the ONLY thing
    /// that decides how far the rules sit under the goals: an owner and a peer that disagreed about
    /// it would show two different pictures of one panel, which is exactly the 1:1 ruling broken by
    /// a number nobody thinks to check.</para>
    /// </summary>
    private const float ScenarioRulesStackGap = 0.012f;

    /// <summary>
    /// Height budget of the special-rules section, board-local metres.
    ///
    /// <para>MIRROR of <c>WorldUI.Surfaces.ScenarioRulesSurface.RulesBudgetMeters</c>, linted by
    /// <c>scripts/check-mirrors.sh</c> — that constant carries the full derivation (it is what is
    /// left of the objectives' own budget above the element board's top edge at -0.172 m, sized so
    /// the section still clears the elements at the tallest objectives panel the objectives surface
    /// derives). It must match the owner's: the dock fit divides this budget by the measured
    /// content, so two different budgets would render the same paragraph at two different sizes on
    /// the two boards.</para>
    /// </summary>
    private const float ScenarioRulesBudget = 0.09f;

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

        // ---- SPECIAL RULES ("Spezialregeln"), stacked under the objectives -------------------
        //
        // The user's item 3 of 2026-09-07: "In dem Szenario das wir gespielt haben, haben wir jede
        // Runde einen Schaden bekommen - diese Info ist aber in VR nirgendwo ersichtlich. Ich
        // möchte das du (lokal & remote) links bei den Questzielen auch solche 'Spezialregeln'
        // eines Szenarios hinzufügst!"
        //
        // SAME SHAPE AS THE OBJECTIVES ABOVE, and that is the whole design: the game holds the
        // rules in a REAL WIDGET, UIManager.ScenarioModifierContainer — the exact sibling of the
        // MissionObjectiveContainer this panel already mirrors, filled in the same call
        // (UIManager.InitScenario). So the peers' copy is a live CLONE of the owner's own rules
        // panel, at the owner's column, in the game's own localised prose, for the same reasons and
        // through the same machinery. Nothing is redrawn, nothing is translated by the mod, and no
        // wire field exists or is needed: ScenarioModifiers are scenario-global model state that
        // every client builds this container from for itself (CLASSIFICATION: GLOBAL).
        //
        // GROW (-1,-1) rather than the objectives' (-1,0): the mount point is this section's
        // TOP-RIGHT CORNER, so its seat needs only the panel ABOVE it and never its own height —
        // which is not known until the first fit has run. ScenarioRulesSurface uses the identical
        // vector on the local board for the identical reason.
        //
        // The MOUNT is re-seated every Refresh from _mirror.FittedSize (see SeatRules) — the
        // objectives panel changes height whenever a row is ticked off or the owner's width dial
        // re-wraps the column, and a fixed drop would leave a widening hole or an overlap.
        _rulesRoot = new GameObject("ScenarioRules").transform;
        _rulesRoot.SetParent(_root, worldPositionStays: false);
        _rulesMirror = new RemoteWidgetMirror("ScenarioRules", _rulesRoot,
            layout.ObjectivesWidth, ScenarioRulesBudget, new Vector2(-1f, -1f),
            fitWidth: false,
            densityScale: ObjectivesDensityScale,
            layoutOwner: RemoteWidgetMirror.LayoutOwner.CloneAtBoardOwnersWidth);

        _fallbackRoot = new GameObject("Fallback").transform;
        _fallbackRoot.gameObject.SetActive(false); // retired surrogate: never visible, including construction failure
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
    public void TickLive()
    {
        _mirror.TickLive();
        _rulesMirror.TickLive();
    }

    public void Destroy()
    {
        _mirror.Destroy();
        _rulesMirror.Destroy();
    }

    /// <summary>Refresh the original objective and rule widgets; retry native recovery while absent.</summary>
    public void Refresh()
    {
        RefreshObjectives();
        RefreshRules();
    }

    private void RefreshObjectives()
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
            StyleMirroredText();
            return;
        }

        // ObjectivesSurface converts this same original container and has no procedural rows.
        // Keep native recovery live without substituting a different widget during an outage.
        Source = RemoteWidgetMirror.Fidelity.None;
        _mirror.SetShown(false);
        _fallbackRoot.gameObject.SetActive(false);
        RowCount = 0;
    }

    // ---------------------------------------------------------------- SPECIAL RULES ------------

    /// <summary>Last (rows, source) pair reported, so the rules line is one per real change.</summary>
    private int _loggedRuleRows = -1;
    private RemoteWidgetMirror.Fidelity _loggedRuleSource = RemoteWidgetMirror.Fidelity.None;

    /// <summary>
    /// Mirror the peer-visible SPECIAL RULES — the game's own
    /// <c>UIManager.ScenarioModifierContainer</c> — under the objectives.
    ///
    /// <para><b>MIRROR OR NOTHING, deliberately, and it is the 1:1 answer rather than a gap in the
    /// work.</b> The objectives above keep a mod-drawn fallback because their previous mod-drawn
    /// version was already shipped and rejected in that shape, so it survives as the ladder's last
    /// rung. The rules have no such history and must not acquire one: the LOCAL board
    /// (<c>ScenarioRulesSurface</c>) converts the real widget or draws nothing at all, so a peer
    /// board that fell back to mod-drawn rows would be showing team-mates a picture the owner
    /// himself never sees. Absent on both sides beats different on each.</para>
    /// </summary>
    private void RefreshRules()
    {
        ScenarioModifierContainer? container = null;
        try
        {
            UIManager? manager = UIManager.Instance;
            container = manager != null ? manager.ScenarioModifierContainer : null;
        }
        catch { container = null; }

        int rows = CountRuleRows(container);

        // Seat BEFORE the fit: the mirror measures itself against its mount, so the mount has to be
        // where this section belongs by the time Refresh runs, or the first fitted frame lands on
        // last frame's drop.
        SeatRules();

        bool live = rows > 0
                    && container != null
                    && _rulesMirror.Refresh(container.transform);
        if (!live)
        {
            // No rules in this scenario (the common case), or no container yet. Tear the clone down
            // rather than leave a stale one parked under the objectives.
            if (rows <= 0 || container == null)
                _rulesMirror.Refresh(null);
            _rulesMirror.SetShown(false);
            RulesSource = RemoteWidgetMirror.Fidelity.None;
            RuleRowCount = 0;
            LogRules(0, RemoteWidgetMirror.Fidelity.None);
            return;
        }

        _rulesMirror.SetShown(true);
        RulesSource = RemoteWidgetMirror.Fidelity.MirroredWidget;
        RuleRowCount = rows;
        // The clone hangs under _root, so the objectives' own relief sweep covers it — but that
        // sweep only runs on the objectives' MIRRORED branch, and these two sections resolve
        // independently. Running it here as well is what keeps a mirrored rules paragraph relieved
        // while the objectives are on their fallback.
        StyleMirroredText();
        LogRules(rows, RemoteWidgetMirror.Fidelity.MirroredWidget);
    }

    /// <summary>
    /// Drop this section to just under the objectives, board-local.
    ///
    /// <para>The objectives sit CENTRED on the panel root (their grow.y is 0), so their bottom edge
    /// is half their height below it; this section's mount is its own TOP-RIGHT corner (grow
    /// (-1,-1)), so the drop is that half-height plus the column's clearance and nothing else. Both
    /// terms are LIVE: <c>FittedSize</c> is republished by every fit, and the objectives panel
    /// changes height whenever a row is ticked off or the owner re-wraps the column.</para>
    ///
    /// <para>On the objectives' mod-drawn fallback there is no fit to read, so the drawn plate's
    /// own height is the anchor — the same measurement, taken from the thing that is actually on
    /// screen. Measuring the mirror's stale <c>FittedSize</c> in that state is how a section ends
    /// up floating over a panel that is no longer the size it was.</para>
    /// </summary>
    private void SeatRules()
    {
        float half = Source == RemoteWidgetMirror.Fidelity.MirroredWidget
            ? _mirror.FittedSize.y * 0.5f
            : Mathf.Max(RowHeight + Mathf.Max(_shownRows, 0) * RowHeight + 0.010f, RowHeight) * 0.5f;
        float drop = half + ScenarioRulesStackGap;

        Vector3 p = _rulesRoot.localPosition;
        if (Mathf.Abs(p.y + drop) < 1e-5f)
            return;
        _rulesRoot.localPosition = new Vector3(p.x, -drop, p.z);
    }

    /// <summary>
    /// The rules the peer's owner can actually READ, counted as the container's live
    /// <c>ScenarioModifierUI</c> children.
    ///
    /// <para>THE WIDGETS ARE THE POPULATION, NOT THE MODEL LIST, and that is the whole reason this
    /// count can be trusted. <c>ScenarioModifierContainer.InitialiseScenarioModifier</c> spawns a
    /// row only for a modifier that is neither <c>IsHidden</c> nor <c>Deactivated</c> AND whose
    /// <c>LocalizeText(scenarioID)</c> came back non-empty — and the second filter is not
    /// reconstructible from the model without re-implementing
    /// <c>LocalizationScenarioModifierConveter</c>, whose <c>default:</c> branch returns
    /// <c>string.Empty</c> for 15 of the 18 modifier types. Counting the model would therefore
    /// report rules the game deliberately shows nobody.</para>
    /// </summary>
    private static int CountRuleRows(ScenarioModifierContainer? container)
    {
        try
        {
            if (container == null)
                return 0;
            int n = 0;
            foreach (ScenarioModifierUI row in
                     container.GetComponentsInChildren<ScenarioModifierUI>(includeInactive: false))
                if (row != null && row.gameObject.activeInHierarchy)
                    n++;
            return n;
        }
        catch { return 0; }
    }

    /// <summary>Change-gated rules report — see the string for how to read the numbers.</summary>
    private void LogRules(int rows, RemoteWidgetMirror.Fidelity source)
    {
        if (rows == _loggedRuleRows && source == _loggedRuleSource)
            return;
        _loggedRuleRows = rows;
        _loggedRuleSource = source;

        // HW-VERIFY
        VRLog.Note("Net", $"PEER SCENARIO RULES: {rows} special-rule row(s) mirrored under a peer's " +
            $"objectives, source={source} (mirror reason '{_rulesMirror.Reason}'), seated " +
            $"{-_rulesRoot.localPosition.y:F3} m under the objectives mount against an objectives " +
            $"panel {(Source == RemoteWidgetMirror.Fidelity.MirroredWidget ? _mirror.FittedSize.y : 0f):F3} m " +
            $"tall (objectives source={Source}). SOURCE ANSWERED: the game's own " +
            "UIManager.ScenarioModifierContainer, cloned — the same widget the owner's own board " +
            "docks, so the sentences are the game's localised prose and the mod supplies no text " +
            "in any language. ZERO WIRE: ScenarioModifiers are scenario-global model state that " +
            "every client already holds, so there is nothing here for a peer to send. READ IT LIKE " +
            "THIS, and the two zeroes are DIFFERENT. WORKING: rows>0 with source=MirroredWidget on " +
            "a scenario that has special rules, and the drop above equals half the objectives " +
            "height plus 12 mm to the millimetre — that is this section sitting ON the objectives' " +
            "bottom edge rather than at a guessed offset. A LEGITIMATE ZERO: rows=0 with " +
            "source=None means this scenario HAS no readable special rules, the section collapses, " +
            "and the local board shows nothing either — the flat game's DecorateSpecialRules does " +
            "exactly this. INERT: rows=0 with source=None on a scenario where the party is taking " +
            "damage every round; then the container exists but spawned no widget, and the mirror " +
            "reason above says whether it was even asked. STILL BEYOND THE INSTRUMENT: rows>0 with " +
            "source=None — the container has rows and the clone refused them, which is a " +
            "RemoteWidgetMirror question and the reason field is the only thing that names it. A " +
            "count that RISES without the scenario changing would mean the game is adding " +
            "modifiers mid-scenario (ModifyUpdatedHiddenOrDeactivatedState), which is legal and is " +
            "why this line is gated on the count rather than printed once.");
    }

    /// <summary>How many mirrored labels the last sweep had to relieve (change-gated log).</summary>
    private int _styledRows = -1;

    /// <summary>
    /// THE SAME LEGIBILITY RELIEF THE OWNER'S OWN OBJECTIVES PANEL WEARS, applied to the CLONE
    /// (user request 5, 2026-09-03 — <c>ObjectivesSurface.StyleObjectivesText</c> is the local half
    /// and carries the measurement).
    ///
    /// <para><b>WHY BOTH SIDES AND NOT JUST THE OWNER'S.</b> The mirror is an
    /// <c>Object.Instantiate</c> of the owner's live subtree, and Instantiate copies the serialized
    /// font-material reference — so a clone taken AFTER the owner's relief landed already carries
    /// it for free, and one taken BEFORE carries the bare material until the next clone rebuild.
    /// That is a window in which the owner reads a relieved panel and his team-mates read a bare
    /// one, which is the 1:1 ruling broken by TIMING rather than by design. Applying the identical
    /// constant on the clone closes it: there is no dial on either side to disagree about, so the
    /// two sides cannot land on different pictures no matter when the clone was taken.</para>
    ///
    /// <para>Swept over the whole panel root, so the mod-drawn FALLBACK rows are covered by the same
    /// pass — they come from <c>RemoteBoardContent.Label</c>, which already relieves them, and the
    /// material read-back makes re-visiting them free.</para>
    /// </summary>
    private void StyleMirroredText()
    {
        int styled = 0;
        foreach (TMP_Text t in _root.GetComponentsInChildren<TMP_Text>(includeInactive: true))
        {
            if (WorldUI.NativeButtonSkin.HasWorldReadableRelief(t))
                continue;
            WorldUI.NativeButtonSkin.StyleWorldReadableLabel(t);
            if (WorldUI.NativeButtonSkin.HasWorldReadableRelief(t))
                styled++;
        }
        if (styled <= 0 || styled == _styledRows)
            return;
        _styledRows = styled;
        // HW-VERIFY
        VRLog.Note("Net", $"PEER OBJECTIVES RELIEF: {styled} label(s) on the MIRRORED objectives " +
            "panel were re-lettered with the world-readable keyline this sweep — the same constant " +
            "the owner's own panel uses (WORLD-LABEL LEGIBILITY names the recipe). A non-zero " +
            "count here is the clone having been taken before the owner's relief landed, which is " +
            "expected and is exactly what this pass exists to close; the count going quiet is the " +
            "two boards agreeing. A count that RISES every sweep would mean the clone is being " +
            "rebuilt every 250 ms, which is a different defect entirely.");
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
