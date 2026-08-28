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
///   • the OWNER'S OWN per-board offset/scale — read out of <see cref="RemoteBoardTuning"/>, i.e.
///     from extension record 28 when the owner has moved that dial and from the SHIPPED
///     <see cref="Defaults"/> constant (keyed by the PEER's SYNCED board style, extras byte A bits
///     5..6) when they have not.
///
/// ─── WHAT CHANGED, AND WHY THE OLD NOTE HERE WAS WRONG ─────────────────────────────────────────
/// This file used to carry a DELIBERATELY-NOT clause: "the peer's own debug-menu RE-tuning of those
/// offsets … never rides the wire … every client renders a given board style at its SHIPPED layout
/// — which is what the two overwhelmingly common cases (nobody has tuned anything) make identical
/// to what the owner sees". The parenthesis is the whole of the argument, and it only holds while
/// nobody tunes. Under the 2026-08-08 ruling ("alle … Anzeigen des Controllboards … so wie der
/// Spieler sie sieht") a tuned owner's board must read the same everywhere, so the tuning now
/// travels — sparsely, and only when it is off the default, so an untuned player's packet is
/// byte-identical to the previous build's (see <see cref="NetProtocol.ExtIdBoardTuning"/>).
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28, via <see cref="RemoteBoardTuning"/>) +
/// VR-ONLY-derived. Its inputs are the board STYLE that already rides the extras block and the
/// owner's sparse tuning record; the mount BASES are shipped constants both clients compile in.
/// See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
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

    /// <summary>The owner's <c>[Cards] ActiveGridSpacing_{board}</c> — the (column, row) multiples
    /// of their card size that their active column is laid out on (record 28 ids 176/177).</summary>
    public Vector2 ActiveGridSpacing { get; }

    /// <summary>The owner's <c>[Cards] CardWidth</c> (record 28 id 70) — the metric their active
    /// cards are DRAWN at, before <see cref="ActiveCardScale"/>.
    ///
    /// <para>It has to travel because the mirror used to draw that column at a bare 0.075 m
    /// constant while the owner draws it at their own card width (0.0635 m shipped): an
    /// 18 % oversize on every peer's board at the DEFAULTS, before anybody tuned anything.</para></summary>
    public float ActiveCardWidth { get; }

    /// <summary>Round readout ('Runde N') seat — top-right (<c>PlayTray.ReadoutBase +
    /// ReadoutOffset</c>).</summary>
    public Vector3 ReadoutMount { get; }

    /// <summary>Pick-status placard seat — the hovering line above the board's top edge
    /// (<c>PlayTray.PickBannerBase + PickBannerOffset</c>), the mirror of the owner's own
    /// <c>PlayTray.PickBannerLocalPosition</c>.</summary>
    public Vector3 PickBannerMount { get; }

    /// <summary>TOOLTIP AREA origin, AUTHORED — the board's authored top-LEFT corner
    /// (<c>PlayTray.TooltipAreaBase</c>) plus the OWNER'S OWN <c>HoverHintOffset</c>: record 28
    /// id 8 when they have moved that dial, the shipped constant for their synced style when they
    /// have not. This is the PRE-MEASURE seat and the degradation fallback for
    /// <see cref="RemoteBoardTooltip"/> (extension record 9), not the final corner — that class
    /// runs the owner's own <c>PlayTray.MeasureBoardLocalExtents</c> over the peer's board root and
    /// corrects this seat by what it finds, because the authored plate is not the visible board:
    /// the docks hanging off the board root reach past it, on the owner's board and on the mirror
    /// alike.
    ///
    /// <para>TWO SENTENCES THAT USED TO STAND HERE WERE WRONG, recorded so nobody re-derives them.
    /// (1) "the remote mirror uses the authored constant, the same authored-vs-measured split every
    /// dock on this board lives with" — it did, and that WAS the defect: the bare
    /// <c>TooltipAreaBase</c> is the exact expression the OWNER-side fix replaced after the
    /// <c>WorldTooltips</c> "still inside" report, so the mirror was faithfully reproducing a
    /// corner the owner had already stopped using. (2) "the DELIBERATELY-NOT rule applies as
    /// everywhere here: a peer's private debug-menu re-tuning of the offset never rides the wire" —
    /// it rides the wire, and has since record 28; the section above retired that clause for every
    /// dial on this type, and the constructor below has been adding <c>tuning.HoverHintOffset</c>
    /// all along. Prose that contradicts the code is an instruction to rebuild the bug class it
    /// describes.</para></summary>
    public Vector3 TooltipMount { get; }

    /// <summary>Derive the seats from the peer's own tuning (record 28), falling back per field to
    /// the shipped default for their synced style — which is what an untuned peer, and every peer
    /// predating the record, resolves to.</summary>
    public RemoteBoardLayout(in RemoteBoardTuning tuning)
    {
        Style = tuning.Style;

        ObjectivesMount = PlayTray.ObjectivesMountBase + tuning.ObjectivesOffset;
        ObjectivesScale = tuning.ObjectivesScale;
        ObjectivesWidth = PlayTray.ObjectivesMountWidth * tuning.ObjectivesWidth;

        ElementMount = PlayTray.ElementMountBase + tuning.ElementsOffset;
        ElementScale = tuning.ElementsScale;

        InitiativeMount = tuning.InitiativeOffset;

        PileMount = PlayTray.PileMountBase + tuning.PileOffset;
        PileSpacing = tuning.PileSpacing;
        PileScale = tuning.PileScale;

        ActiveMount = PlayTray.ActiveMountBase + tuning.ActiveOffset;
        ActiveCardScale = tuning.ActiveCardScale;
        ActiveGridSpacing = tuning.ActiveGridSpacing;
        ActiveCardWidth = tuning.CardWidth;

        ReadoutMount = PlayTray.ReadoutBase + tuning.ReadoutOffset;

        PickBannerMount = PlayTray.PickBannerBase + tuning.PickBannerOffset;

        TooltipMount = PlayTray.TooltipAreaBase + tuning.HoverHintOffset;
    }

    /// <summary>The shipped layout for a style, with no peer tuning applied — the pre-seat used
    /// before a peer's first extras packet has resolved their style and dials.</summary>
    public RemoteBoardLayout(ControlBoard style)
        : this(new RemoteBoardTuning(style, null, 0))
    {
    }

    /// <summary>One-line dump of the derived seats for the board-built log line: a wrong panel
    /// position is then answerable from the log without a screenshot.</summary>
    public override string ToString() =>
        $"style={Style}, objectives={ObjectivesMount:F3}(×{ObjectivesScale:F2}, w {ObjectivesWidth:F3} m), " +
        $"elements={ElementMount:F3}(×{ElementScale:F2}), initiative={InitiativeMount:F3}, " +
        $"piles={PileMount:F3}(step {PileSpacing:F3}), active={ActiveMount:F3}(×{ActiveCardScale:F2}), " +
        $"readout={ReadoutMount:F3}";
}
