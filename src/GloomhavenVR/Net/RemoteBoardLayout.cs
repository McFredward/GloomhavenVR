using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// WHERE EVERY DOCK ON A PEER'S CONTROL BOARD SITS — derived, never hand-tuned.
///
/// ─── THE DEFECT THIS TYPE EXISTS TO CLOSE ──────────────────────────────────────────────────────
/// Defect (c) of the 1:1-parity round: "die Kartenoverlays und die Buttons sind nicht dort, wo der
/// Besitzer sie hat". The cause was structural, not cosmetic. The LOCAL board places every docked
/// panel at <c>&lt;MountBase&gt; + &lt;per-board offset&gt;</c> — <c>PlayTray.BuildMounts</c> writes
/// <c>ObjectivesMountBase + CardsConfig.ObjectivesOffset(board)</c>, <c>PileMountBase +
/// PileOffset(board)</c>, and so on, with the initiative mount being the per-board offset outright.
/// The REMOTE board reproduced only the BASE and dropped the per-board term entirely, then papered
/// over the resulting depth error with a hand-estimated per-style "content proud lift" whose own
/// doc admitted it was "an approximation of the meshes, not a measurement".
///
/// For an Oak peer that was invisible (every Oak offset is ~0). For the Steel board the hardware
/// session was run on, it is the whole defect: the shipped Steel offsets move the objectives dock
/// 32 mm up and 42 mm toward the viewer, the piles and the element column 40 mm toward the viewer,
/// the round readout 40 mm left / 22 mm up / 44 mm toward the viewer, and the initiative track to
/// (0, 0.200, −0.070) — where the remote board had it hardcoded at (0, 0.191, −0.054). Every one of
/// those panels was therefore drawn somewhere the owner does not have it.
///
/// ─── THE RULE ──────────────────────────────────────────────────────────────────────────────────
/// This type computes each mount from the SAME two ingredients the local board uses:
///   • the mount BASE — read straight out of <see cref="PlayTray"/> (those five members were
///     widened from private to internal for exactly this; see the note there). No copy, so no
///     mirrored constant to drift and nothing new for scripts/check-mirrors.sh to police.
///   • the AUTHORED per-board offset/scale — read out of <see cref="Defaults"/>, keyed by the PEER's
///     SYNCED board style (extras byte A bits 5..6), which is how <see cref="RemoteBoardFurniture"/>
///     has been seating the keycaps since the 3D-parity pass.
///
/// WHAT IS DELIBERATELY NOT APPLIED: the peer's own debug-menu RE-tuning of those offsets. It is
/// their local config, it never rides the wire, and the standing DELIBERATELY-NOT rule keeps it off.
/// Every client therefore renders a given board style at its SHIPPED layout — which is what the two
/// overwhelmingly common cases (nobody has tuned anything) make identical to what the owner sees.
/// </summary>
/// <remarks>CLASSIFICATION: DELIBERATELY-NOT + VR-ONLY-derived — ZERO wire of its own. Its only
/// input is the board STYLE that already rides the extras block; everything else is a shipped
/// constant both clients compile in. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal readonly struct RemoteBoardLayout
{
    /// <summary>The peer's synced board style this layout was derived for.</summary>
    public ControlBoard Style { get; }

    /// <summary>Objectives ('Aufgaben') dock — RIGHT-centre origin growing LEFT off the board's left
    /// edge (<c>PlayTray.ObjectivesMountBase + ObjectivesOffset</c>).</summary>
    public Vector3 ObjectivesMount { get; }

    /// <summary>Objectives dock scale — the local mount's <c>localScale</c> ('Größe').</summary>
    public float ObjectivesScale { get; }

    /// <summary>Objectives width budget, tray-local metres (<c>ObjectivesMountWidth × the authored
    /// per-board width multiplier</c>) — the wrap column the local panel forces onto its rows.</summary>
    public float ObjectivesWidth { get; }

    /// <summary>Element infusion dock — left column below the objectives, same origin convention
    /// (<c>PlayTray.ElementMountBase + ElementsOffset</c>).</summary>
    public Vector3 ElementMount { get; }

    /// <summary>Element dock scale.</summary>
    public float ElementScale { get; }

    /// <summary>Initiative-track dock — BOTTOM-centre origin growing UP, above the board's top edge.
    /// The local mount is the per-board offset OUTRIGHT (<c>PlayTray.BuildMounts</c> assigns
    /// <c>CardsConfig.InitiativeOffset(board).Value</c> with no base), so this one is too.</summary>
    public Vector3 InitiativeMount { get; }

    /// <summary>Pile-stack dock (discard / burnt / items) — LEFT-centre origin growing RIGHT off the
    /// board's right edge (<c>PlayTray.PileMountBase + PileOffset</c>).</summary>
    public Vector3 PileMount { get; }

    /// <summary>Vertical distance between two pile-stack centres, mount-local metres.</summary>
    public float PileSpacing { get; }

    /// <summary>Pile-stack scale.</summary>
    public float PileScale { get; }

    /// <summary>Active/persistent card column — further off the right edge, past the piles
    /// (<c>PlayTray.ActiveMountBase + ActiveOffset</c>).</summary>
    public Vector3 ActiveMount { get; }

    /// <summary>Active-card scale.</summary>
    public float ActiveCardScale { get; }

    /// <summary>Round readout ('Runde N') seat — top-right (<c>PlayTray.ReadoutBase +
    /// ReadoutOffset</c>).</summary>
    public Vector3 ReadoutMount { get; }

    /// <summary>Pick-status placard seat — the hovering line above the board's top edge
    /// (<c>PlayTray.PickBannerBase + PickBannerOffset</c>), the mirror of the owner's own
    /// <c>PlayTray.PickBannerLocalPosition</c>.</summary>
    public Vector3 PickBannerMount { get; }

    /// <summary>TOOLTIP AREA origin — the board's authored top-LEFT corner
    /// (<c>PlayTray.TooltipAreaBase</c> + the SHIPPED per-board <c>HoverHintOffset</c>), where
    /// <see cref="RemoteBoardTooltip"/> seats a peer's synced tooltip (extension record 9). The
    /// owner's LOCAL area refines the corner from measured renderer bounds; the remote mirror
    /// uses the authored constant, the same authored-vs-measured split every dock on this board
    /// lives with. The DELIBERATELY-NOT rule applies as everywhere here: a peer's private
    /// debug-menu re-tuning of the offset never rides the wire.</summary>
    public Vector3 TooltipMount { get; }

    public RemoteBoardLayout(ControlBoard style)
    {
        Style = style;
        int i = (int)ControlBoards.Clamp((int)style);

        ObjectivesMount = PlayTray.ObjectivesMountBase + CardsConfig.BoardDefaults.ObjectivesOffset[i];
        ObjectivesScale = CardsConfig.BoardDefaults.ObjectivesScale[i];
        ObjectivesWidth = PlayTray.ObjectivesMountWidth * CardsConfig.BoardDefaults.ObjectivesWidth[i];

        ElementMount = PlayTray.ElementMountBase + CardsConfig.BoardDefaults.ElementsOffset[i];
        ElementScale = Defaults.ElementsScale_ByBoard[i];

        InitiativeMount = CardsConfig.BoardDefaults.InitiativeOffset[i];

        PileMount = PlayTray.PileMountBase + CardsConfig.BoardDefaults.PileOffset[i];
        PileSpacing = Defaults.PileSpacing_ByBoard[i];
        PileScale = Defaults.PileScale_ByBoard[i];

        ActiveMount = PlayTray.ActiveMountBase + CardsConfig.BoardDefaults.ActiveOffset[i];
        ActiveCardScale = CardsConfig.BoardDefaults.ActiveCardScale[i];

        ReadoutMount = PlayTray.ReadoutBase + CardsConfig.BoardDefaults.ReadoutOffset[i];

        PickBannerMount = PlayTray.PickBannerBase + CardsConfig.BoardDefaults.PickBannerOffset[i];

        TooltipMount = PlayTray.TooltipAreaBase + CardsConfig.BoardDefaults.HoverHintOffset[i];
    }

    /// <summary>One-line dump of the derived seats for the board-built log line: a wrong panel
    /// position is then answerable from the log without a screenshot.</summary>
    public override string ToString() =>
        $"style={Style}, objectives={ObjectivesMount:F3}(×{ObjectivesScale:F2}, w {ObjectivesWidth:F3} m), " +
        $"elements={ElementMount:F3}(×{ElementScale:F2}), initiative={InitiativeMount:F3}, " +
        $"piles={PileMount:F3}(step {PileSpacing:F3}), active={ActiveMount:F3}(×{ActiveCardScale:F2}), " +
        $"readout={ReadoutMount:F3}";
}
