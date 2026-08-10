using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// THE OWNER'S OWN DIAL POSITIONS, ON BOTH ENDS OF THE WIRE — the sender-side sampler that turns
/// the live <c>CardsConfig</c> into extension record <see cref="NetProtocol.ExtIdBoardTuning"/>
/// (28), and the receiver-side resolver every remote board visual reads instead of reaching for a
/// shipped constant.
///
/// ─── THE DEFECT THIS TYPE EXISTS TO CLOSE ──────────────────────────────────────────────────────
/// Every mirrored dock, cap, overlay, mesh pose and hand fan on a peer's board was seated from the
/// AUTHORED per-board constants keyed by the synced board style, with a DELIBERATELY-NOT note in
/// each source file saying that the owner's private re-tuning stays local. Under the standing
/// ruling — "alle Interaktionen, Animationen und Anzeigen des Controllboards … so wie der Spieler
/// sie sieht" (2026-08-08) — that is a divergence, not a policy: a player who drags their
/// objectives dock, shrinks their pile stacks, pitches their board mesh or widens their hand fan is
/// looking at a board no other client draws. It was invisible only for as long as nobody re-tuned,
/// and the shipped defaults are themselves a REBASE of one player's tuning
/// (<c>scripts/rebase-defaults.py</c>) — which is proof that the dials get moved.
///
/// ─── WHY IT IS AFFORDABLE ──────────────────────────────────────────────────────────────────────
/// These are config entries a player edits once and then never touches, so the record follows the
/// <see cref="NetProtocol.MaskSizeDefaultCode"/> precedent to the letter: a field is written ONLY
/// when its live value differs from the compiled default for the sender's style, and when NO field
/// differs the record is not written at all — the extension tail does not even open for it. An
/// untuned player therefore emits the exact bytes the previous build emitted. One moved dock costs
/// 16 bytes at 5 Hz (2 TLV + 7 page header + 1 id + 6 value).
///
/// ─── AND WHY IT NO LONGER HAS A CEILING ────────────────────────────────────────────────────────
/// It used to. The extension tail writes each record's length in ONE BYTE, this record's worst case
/// had reached EXACTLY 255, and <see cref="Sample"/>'s only honest response to the next dial was to
/// refuse the whole record. Two dials had already been squeezed into a narrower container purely to
/// fit. Under the 1:1 ruling ("Ändert ein Spieler also die Positionen für sich selber, so sollen
/// alle anderen diese Position bei seinem board auch sehen", 2026-08-09) a capacity ceiling is not
/// a budget, it is a broken guarantee — so the record is PAGED (<see cref="BoardTunePages"/>). This
/// method now builds the COMPLETE field list, of any length the id space allows, and the pager
/// splits it; the largest a PACKET can carry stayed exactly where it was, at one 255-byte page.
///
/// COMPARING QUANTIZED CODES, NOT FLOATS, is what makes "differs from the default" stable: a config
/// round-trip through a text file can perturb a float in its last bits without moving a pixel, and
/// a float compare would then start emitting a field forever. Two values that land on the same wire
/// code are the same picture, so they compare equal here.
///
/// ─── SAMPLING CADENCE ──────────────────────────────────────────────────────────────────────────
/// <see cref="Sample"/> rebuilds the field list into a persistent buffer on every send;
/// <see cref="BoardTunePageSender.Update"/> byte-compares it against the list it holds, so an
/// unchanged config costs one memcmp and the serializer's write path is a bounded copy of one page.
/// Rebuilding is cheap enough to run per send — it allocates nothing and touches ~118 config entries.
///
/// ─── AND THAT NUMBER IS NOW ALLOWED TO GROW ────────────────────────────────────────────────────
/// It went 76 → 86 when the item-cue / item-berth re-art's ten dials were wired (ids 80 / 161..169),
/// which is worth stating because under the OLD scheme it could not have: those ten are the exact
/// dials that had to ship as frozen constants while the record stood at 255 bytes. It went 86 → 105
/// when the KEYCAP GEOMETRY family joined (ids 81..98 + 228) — the [RoundButtons] / [BoardButtons] /
/// [BoardDashboard] / [RestButtons] cap sizes, seats and press travels, which were the single
/// largest block of PENDING debt <c>scripts/check-wire-coverage.py</c> was carrying and were frozen
/// constants in <c>RemoteBoardFurniture</c> for exactly as long as the ceiling stood. It went
/// 105 → 118 when the COLOURS AND SHAPES joined (ids 48..53 on the new COLOUR range, 99..100,
/// 170, 229..232) under the user's ruling that "das remote Board 1:1 das anzeigt was der Spieler
/// sieht" — colours and shapes included. The complete field list is 411 bytes at its worst case now
/// (was 370) and STILL splits into TWO pages, so the convergence bound written down in
/// <see cref="BoardTunePages"/> — complete state by T + pageCount × 200 ms — is unchanged at
/// ≤400 ms.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28) — LOCAL CONFIG that is neither GLOBAL (each
/// player has their own) nor derivable from anything already synced. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal static class BoardTuningSampler
{
    /// <summary>
    /// Buffer size a caller must hand <see cref="Sample"/>: the FIELD-ID SPACE's own worst case,
    /// <see cref="NetProtocol.BoardTuneMaxFieldBytes"/> = 921 — every one of the 247 usable ids
    /// present at its own width.
    ///
    /// <para>IT USED TO READ 255, AND THAT WAS THE BUG THIS PASS EXISTS TO KILL. 255 is the
    /// extension tail's ONE-BYTE per-record length ceiling, the record had grown to exactly it, and
    /// the sampler's only honest response to the next dial was to refuse the WHOLE record — which
    /// meant a player who had tuned enough was drawn at the shipped defaults on every other screen,
    /// with nothing on their own screen to tell them. Under the 1:1 ruling ("Ändert ein Spieler also
    /// die Positionen für sich selber, so sollen alle anderen diese Position bei seinem board auch
    /// sehen", 2026-08-09) a capacity ceiling is a broken guarantee, not a budget, so the record is
    /// now PAGED (<see cref="BoardTunePages"/>) and this buffer holds the COMPLETE field list, which
    /// the pager then splits. There is no length here that a dial can cross.</para>
    ///
    /// <para>The remaining bound is the ID SPACE, and it is a BUILD-TIME one: a new dial needs a new
    /// <c>NetProtocol.Tune*</c> constant, so exhausting it is a thing a human reads at a compiler.
    /// The one-shot check at the end of <see cref="Sample"/> is what makes even that impossible to
    /// meet silently.</para>
    /// </summary>
    internal const int MaxPayloadBytes = NetProtocol.BoardTuneMaxFieldBytes;

    /// <summary>One-shot log guard for the overrun check at the end of <see cref="Sample"/> — the
    /// sampler runs on every send, and a crossed bound would otherwise repeat forever.</summary>
    private static bool s_ceilingLogged;

    /// <summary>
    /// Build the COMPLETE sparse field list for <paramref name="style"/> into
    /// <paramref name="payload"/> (at least <see cref="MaxPayloadBytes"/> long) and return its byte
    /// length — or 0 when every dial sits at its shipped default, which is the signal to write no
    /// record at all.
    ///
    /// <para>NO COUNT BYTE HERE ANY MORE: this produces the raw <c>[id][value]…</c> run.
    /// <see cref="BoardTunePageSender"/> splits it into pages (each of which states its own field
    /// count in its header) and <see cref="BoardTunePageAssembler"/> re-assembles it on the far side
    /// into the <c>[n][fields]</c> shape <see cref="RemoteBoardTuning"/> reads. The split is the
    /// pager's business precisely so that adding a dial here is a one-line change with no arithmetic
    /// attached to it.</para>
    ///
    /// <para>Fields are appended in ASCENDING ID ORDER, which is the record's layout contract: it
    /// makes the bytes deterministic for a given tuning (so the change-gated log and the wire tests
    /// have something stable to compare), lets a reader stop early, and — since paging cuts the run
    /// into contiguous id ranges — is what lets a page state a RANGE it is complete for.</para>
    ///
    /// <para>Every config entry is null-guarded: this can run before <c>CardsConfig.Bind</c> has
    /// completed (a packet may go out during scene load), and the failure direction there is "no
    /// record" ⇒ the receiver keeps the shipped defaults ⇒ exactly the previous build's picture.</para>
    /// </summary>
    internal static int Sample(ControlBoard style, byte[] payload)
    {
        if (payload == null || payload.Length < MaxPayloadBytes)
            return 0;

        int i = 0;
        int n = 0;
        int b = (int)ControlBoards.Clamp((int)style);

        // ---- VECTOR3 fields (ids 1..15) — board-local / mount-local offsets ------------------
        n += Vec(payload, ref i, NetProtocol.TuneObjectivesOffset,
                 CardsConfig.ObjectivesOffset(style), CardsConfig.BoardDefaults.ObjectivesOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneElementsOffset,
                 CardsConfig.ElementsOffset(style), CardsConfig.BoardDefaults.ElementsOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneInitiativeOffset,
                 CardsConfig.InitiativeOffset(style), CardsConfig.BoardDefaults.InitiativeOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TunePileOffset,
                 CardsConfig.PileOffset(style), CardsConfig.BoardDefaults.PileOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneActiveOffset,
                 CardsConfig.ActiveOffset(style), CardsConfig.BoardDefaults.ActiveOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneReadoutOffset,
                 CardsConfig.ReadoutOffset(style), CardsConfig.BoardDefaults.ReadoutOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TunePickBannerOffset,
                 CardsConfig.PickBannerOffset(style), CardsConfig.BoardDefaults.PickBannerOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneHoverHintOffset,
                 CardsConfig.HoverHintOffset(style), CardsConfig.BoardDefaults.HoverHintOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneConfirmUndoOffset,
                 CardsConfig.ConfirmUndoOffset(style), CardsConfig.BoardDefaults.ConfirmUndoOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneRestButtonOffset,
                 CardsConfig.RestButtonOffset(style), CardsConfig.BoardDefaults.RestButtonOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TunePinOffset,
                 CardsConfig.PinOffset(style), CardsConfig.BoardDefaults.PinOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneItemUseSlotOffset,
                 CardsConfig.ItemUseSlotOffset(style), Defaults.ItemUseSlotOffset_ByBoard[b]);
        n += Vec(payload, ref i, NetProtocol.TuneDecisionOffset,
                 CardsConfig.DecisionOffset(style), CardsConfig.BoardDefaults.DecisionOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneSlotOverlayOffset,
                 CardsConfig.SlotOverlayOffset(style), CardsConfig.BoardDefaults.SlotOverlayOffset[b]);
        n += Vec(payload, ref i, NetProtocol.TuneAssetOffset,
                 CardsConfig.AssetOffset(style), CardsConfig.BoardDefaults.AssetOffset[b]);
        // The turn-flow cluster's per-board seat — the SKIP cap's spot on the board. See
        // NetProtocol.TuneClusterOffset for why this arrived a round after its ClusterScale twin.
        n += Vec(payload, ref i, NetProtocol.TuneClusterOffset,
                 CardsConfig.ClusterOffset(style), Defaults.ClusterOffset_ByBoard[b]);

        // ---- COLOUR fields (ids 48..53) — the [ButtonColors] family --------------------------
        // SIX FIELDS, EIGHTEEN CHANNELS, TWENTY-FOUR BYTES. Sampled from the same ButtonTuning
        // accessors the LOCAL caps read (LabelColor / LabelOutlineColor / CapTint), never from the
        // raw config entries, so the sender's own clamps are already applied and the wire carries
        // the colour the owner is actually looking at.
        //
        // THE DEFAULT SIDE IS Defaults.*, NOT ButtonTuning.Default*: the Bind()s take their default
        // from Defaults, so that is what "the value the receiver already has" means. (Those two used
        // to disagree for the outline triple — see the note in ButtonTuning.)
        n += Col(payload, ref i, NetProtocol.TuneLabelColor,
                 WorldUI.ButtonTuning.LabelColor,
                 new Color(Defaults.LabelR, Defaults.LabelG, Defaults.LabelB, 1f));
        n += Col(payload, ref i, NetProtocol.TuneLabelOutlineColor,
                 WorldUI.ButtonTuning.LabelOutlineColor,
                 new Color(Defaults.LabelOutlineR, Defaults.LabelOutlineG, Defaults.LabelOutlineB, 1f));
        n += Col(payload, ref i, NetProtocol.TuneBoardCapTint,
                 WorldUI.ButtonTuning.BoardCapTint,
                 new Color(Defaults.BoardCapTintR, Defaults.BoardCapTintG, Defaults.BoardCapTintB, 1f));
        n += Col(payload, ref i, NetProtocol.TuneDashCapTint,
                 WorldUI.ButtonTuning.DashCapTint,
                 new Color(Defaults.DashCapTintR, Defaults.DashCapTintG, Defaults.DashCapTintB, 1f));
        n += Col(payload, ref i, NetProtocol.TuneClusterCapTint,
                 WorldUI.ButtonTuning.ClusterCapTint,
                 new Color(Defaults.ClusterCapTintR, Defaults.ClusterCapTintG, Defaults.ClusterCapTintB, 1f));
        n += Col(payload, ref i, NetProtocol.TuneRestCapTint,
                 WorldUI.ButtonTuning.RestCapTint,
                 new Color(Defaults.RestCapTintR, Defaults.RestCapTintG, Defaults.RestCapTintB, 1f));

        // ---- LENGTH fields (ids 64..80) — scalars measured in metres ------------------------
        n += Len(payload, ref i, NetProtocol.TunePileSpacing,
                 CardsConfig.PileSpacing(style), Defaults.PileSpacing_ByBoard[b]);
        n += Len(payload, ref i, NetProtocol.TuneRestButtonDiameter,
                 CardsConfig.RestButtonDiameter(style), CardsConfig.BoardDefaults.RestButtonDiameter[b]);
        n += Len(payload, ref i, NetProtocol.TuneRestButtonSpacing,
                 CardsConfig.RestButtonSpacing(style), CardsConfig.BoardDefaults.RestButtonSpacing[b]);
        n += Len(payload, ref i, NetProtocol.TuneGenericButtonSpacing,
                 CardsConfig.GenericButtonSpacing(style), CardsConfig.BoardDefaults.GenericButtonSpacing[b]);
        n += Len(payload, ref i, NetProtocol.TuneSlotOverlaySpacing,
                 CardsConfig.SlotOverlaySpacing(style), CardsConfig.BoardDefaults.SlotOverlaySpacing[b]);
        n += Len(payload, ref i, NetProtocol.TuneDecisionGap,
                 CardsConfig.DecisionGap(style), CardsConfig.BoardDefaults.DecisionGap[b]);
        n += Len(payload, ref i, NetProtocol.TuneCardWidth,
                 CardsConfig.CardWidth, Defaults.CardWidth);
        n += Len(payload, ref i, NetProtocol.TuneFanPalmOffset,
                 CardsConfig.FanPalmOffset, Defaults.FanPalmOffset);
        n += Len(payload, ref i, NetProtocol.TuneFanEffectiveRadius,
                 CardsConfig.FanEffectiveRadius, Defaults.FanEffectiveRadius);
        n += Len(payload, ref i, NetProtocol.TuneFanSideDepthCurve,
                 CardsConfig.FanSideDepthCurve, Defaults.FanSideDepthCurve);
        n += Len(payload, ref i, NetProtocol.TuneFanSplitMultiplier,
                 CardsConfig.FanSplitMultiplier, Defaults.FanSplitMultiplier);
        n += Len(payload, ref i, NetProtocol.TuneFanSelectedPopForward,
                 CardsConfig.FanSelectedPopForward, Defaults.FanSelectedPopForward);
        n += Len(payload, ref i, NetProtocol.TuneItemFanOpenArc,
                 CardsConfig.ItemFanOpenArc, Defaults.ItemFanOpenArc);
        // The HAND FAN's character-SWAP exchange (2026-08-09). Same argument as the item fan's
        // animation set: the 1:1 ruling names ANIMATIONS, so an owner who re-tunes how their hand
        // is exchanged must be seen re-tuning it. Ids 77..78 / 150..153 / 226..227.
        n += Len(payload, ref i, NetProtocol.TuneFanSwapTravel,
                 CardsConfig.FanSwapTravel, Defaults.FanSwapTravel);
        n += Len(payload, ref i, NetProtocol.TuneFanSwapArc,
                 CardsConfig.FanSwapArc, Defaults.FanSwapArc);
        // The BOARD PILE fans' base radius (id 79) — the first dial that could NOT have been added
        // before paging: the record stood at exactly 255 and this would have been byte 258. Its
        // remote consumers held it as a bare literal (`Radius = 0.16f * 1.7f` in RemoteBrowserFan /
        // RemoteItemFan), so an owner who widened their pile fans was the only person who saw it.
        n += Len(payload, ref i, NetProtocol.TuneFanRadius,
                 CardsConfig.FanRadius, Defaults.FanRadius);
        // The ITEM-USE BERTH's outline thickness (id 80) — the first of the ten dials the item-cue /
        // item-berth re-art shipped as FROZEN constants because the record stood at exactly 255 and
        // could not carry them. The paging round reserved the ids for them; this claims the first.
        n += Len(payload, ref i, NetProtocol.TuneItemBerthRingThickness,
                 CardsConfig.ItemBerthRingThickness, Defaults.ItemBerthRingThickness);
        // ---- THE KEYCAP GEOMETRY FAMILY (ids 81..98, plus the shape at 228 below) --------------
        // Every 3D keycap standing on a control board — the turn-flow SKIP cap, the Confirm/Undo
        // pads, the gear/Fixiert plates and the rest discs — was mirrored on a peer's board from a
        // FROZEN constant in RemoteBoardFurniture, and scripts/check-wire-coverage.py carried the
        // whole set as one PENDING debt whose stated blocker ("the renderer is owned by a parallel
        // round") has expired. The user report that cashed it in is about the SKIP cap's dials
        // specifically (ModBuild 96: "vermisse ich die Einstellungen im Debug Menu für genau diese
        // 'Überspringen'-Tasten (offsets, Form, Größe, etc..)") — and under the 1:1 ruling, a dial
        // the player can now find is a dial every peer must see them turn.
        //
        // The three [RoundButtons] offsets are sampled as three SEPARATE lengths rather than one
        // vec3 because they ARE three separate config entries; see NetProtocol's block comment on
        // TuneRoundOffsetX for why that matters to the coverage guard.
        n += Len(payload, ref i, NetProtocol.TuneRoundOffsetX,
                 WorldUI.ButtonTuning.RoundOffsetX, Defaults.RoundButtons_OffsetX);
        n += Len(payload, ref i, NetProtocol.TuneRoundOffsetY,
                 WorldUI.ButtonTuning.RoundOffsetY, Defaults.RoundButtons_OffsetY);
        n += Len(payload, ref i, NetProtocol.TuneRoundOffsetZ,
                 WorldUI.ButtonTuning.RoundOffsetZ, Defaults.OffsetZ);
        n += Len(payload, ref i, NetProtocol.TuneRoundCapSize,
                 WorldUI.ButtonTuning.RoundCapSize, Defaults.RoundButtons_CapSize);
        n += Len(payload, ref i, NetProtocol.TuneRoundCapWidth,
                 WorldUI.ButtonTuning.RoundWidth, Defaults.RoundButtons_Width);
        n += Len(payload, ref i, NetProtocol.TuneRoundCapHeight,
                 WorldUI.ButtonTuning.RoundHeight, Defaults.RoundButtons_Height);
        n += Len(payload, ref i, NetProtocol.TuneRoundCapDepth,
                 WorldUI.ButtonTuning.RoundDepth, Defaults.RoundButtons_Depth);
        n += Len(payload, ref i, NetProtocol.TuneRoundCapTravel,
                 WorldUI.ButtonTuning.RoundTravel, Defaults.RoundButtons_Travel);
        n += Len(payload, ref i, NetProtocol.TuneBoardCapWidth,
                 WorldUI.ButtonTuning.BoardWidth, Defaults.BoardButtons_Width);
        n += Len(payload, ref i, NetProtocol.TuneBoardCapHeight,
                 WorldUI.ButtonTuning.BoardHeight, Defaults.BoardButtons_Height);
        n += Len(payload, ref i, NetProtocol.TuneBoardCapDepth,
                 WorldUI.ButtonTuning.BoardDepth, Defaults.BoardButtons_Depth);
        n += Len(payload, ref i, NetProtocol.TuneBoardCapTravel,
                 WorldUI.ButtonTuning.BoardTravel, Defaults.BoardButtons_Travel);
        n += Len(payload, ref i, NetProtocol.TuneDashPinWidth,
                 WorldUI.ButtonTuning.DashPinWidth, Defaults.PinWidth);
        n += Len(payload, ref i, NetProtocol.TuneDashCapHeight,
                 WorldUI.ButtonTuning.DashHeight, Defaults.BoardDashboard_Height);
        n += Len(payload, ref i, NetProtocol.TuneDashCapDepth,
                 WorldUI.ButtonTuning.DashDepth, Defaults.BoardDashboard_Depth);
        n += Len(payload, ref i, NetProtocol.TuneDashCapTravel,
                 WorldUI.ButtonTuning.DashTravel, Defaults.BoardDashboard_Travel);
        // [RestButtons] — the WHOLE set now. Depth and Travel rode from the start; Width and Height
        // were held back with a precise reason ("a peer's rest discs are drawn ROUND unconditionally,
        // so these would be bytes no receiver reads" — the FanCloseDuration rule) and that reason is
        // spent: RemoteBoardFurniture branches on the owner's [Cards] RestButtonShape_{board} (id
        // 231) and its Square branch needs exactly these two. Wiring them WITH the branch, never
        // before it, is the standard the un-wiring of FanCloseDuration set.
        n += Len(payload, ref i, NetProtocol.TuneRestCapDepth,
                 WorldUI.ButtonTuning.RestDepth, Defaults.RestButtons_Depth);
        n += Len(payload, ref i, NetProtocol.TuneRestCapTravel,
                 WorldUI.ButtonTuning.RestTravel, Defaults.RestButtons_Travel);
        n += Len(payload, ref i, NetProtocol.TuneRestCapWidth,
                 WorldUI.ButtonTuning.RestWidth, Defaults.RestButtons_Width);
        n += Len(payload, ref i, NetProtocol.TuneRestCapHeight,
                 WorldUI.ButtonTuning.RestHeight, Defaults.RestButtons_Height);

        // ---- FACTOR fields (ids 128..169) — dimensionless multipliers, plus the seconds- and
        // per-second-valued dials that ride this WIDTH (the id range fixes the width, not the unit)
        n += Fac(payload, ref i, NetProtocol.TuneObjectivesScale,
                 CardsConfig.ObjectivesScale(style), CardsConfig.BoardDefaults.ObjectivesScale[b]);
        n += Fac(payload, ref i, NetProtocol.TuneObjectivesWidth,
                 CardsConfig.ObjectivesWidth(style), CardsConfig.BoardDefaults.ObjectivesWidth[b]);
        n += Fac(payload, ref i, NetProtocol.TuneElementsScale,
                 CardsConfig.ElementsScale(style), Defaults.ElementsScale_ByBoard[b]);
        n += Fac(payload, ref i, NetProtocol.TunePileScale,
                 CardsConfig.PileScale(style), Defaults.PileScale_ByBoard[b]);
        n += Fac(payload, ref i, NetProtocol.TuneActiveCardScale,
                 CardsConfig.ActiveCardScale(style), CardsConfig.BoardDefaults.ActiveCardScale[b]);
        // The blinking slot overlays AND the card resting in them are ONE size (2026-08-11) — the
        // peer draws both from this factor, which is why it travels even though the card metric
        // itself already rides record 11. See NetProtocol.TuneSlotOverlayScale.
        n += Fac(payload, ref i, NetProtocol.TuneSlotOverlayScale,
                 CardsConfig.SlotOverlayScale(style), CardsConfig.BoardDefaults.SlotOverlayScale[b]);
        n += Fac(payload, ref i, NetProtocol.TuneClusterScale,
                 CardsConfig.ClusterScale(style), Defaults.ClusterScale_ByBoard[b]);
        n += Fac(payload, ref i, NetProtocol.TuneDecisionScale,
                 CardsConfig.DecisionScale(style), Defaults.DecisionScale_ByBoard[b]);
        n += Fac(payload, ref i, NetProtocol.TuneFanFlatCurvatureFactor,
                 CardsConfig.FanFlatCurvatureFactor, Defaults.FanFlatCurvatureFactor);
        n += Fac(payload, ref i, NetProtocol.TuneFanTiltFactor,
                 CardsConfig.FanTiltFactor, Defaults.FanTiltFactor);
        n += Fac(payload, ref i, NetProtocol.TuneFanFaceViewer,
                 CardsConfig.FanFaceViewer, Defaults.FanFaceViewer);
        n += Fac(payload, ref i, NetProtocol.TuneFanCurvePower,
                 CardsConfig.FanCurvePower, Defaults.FanCurvePower);
        n += Fac(payload, ref i, NetProtocol.TuneFanGazeApexFollow,
                 CardsConfig.FanGazeApexFollow, Defaults.FanGazeApexFollow);
        n += Fac(payload, ref i, NetProtocol.TuneFanSplitFalloff,
                 CardsConfig.FanSplitFalloff, Defaults.FanSplitFalloff);
        n += Fac(payload, ref i, NetProtocol.TuneFanHoverSplitScale,
                 CardsConfig.FanHoverSplitScale, Defaults.FanHoverSplitScale);
        n += Fac(payload, ref i, NetProtocol.TuneHoverInfoScale,
                 WorldUI.WorldUIConfig.HoverInfoScale, Defaults.HoverInfoScale);
        n += Fac(payload, ref i, NetProtocol.TuneCanvasScaleMm,
                 WorldUI.WorldUIConfig.CanvasScaleMm, Defaults.CanvasScaleMm);
        // The ITEM FAN's ANIMATION (the presence pass of 2026-08-08). Four seconds-valued dials ride
        // the FACTOR width — millisecond resolution, see the id table's note on why that is not a
        // category error. These are here rather than frozen into RemoteItemFan because the standing
        // 1:1 ruling names ANIMATIONS explicitly: an owner who makes their item fan deal out slower,
        // wider or with a bigger settle must be seen doing it on every other screen.
        n += Fac(payload, ref i, NetProtocol.TuneItemFanOpenDuration,
                 CardsConfig.ItemFanOpenDuration, Defaults.ItemFanOpenDuration);
        n += Fac(payload, ref i, NetProtocol.TuneItemFanOpenStagger,
                 CardsConfig.ItemFanOpenStagger, Defaults.ItemFanOpenStagger);
        n += Fac(payload, ref i, NetProtocol.TuneItemFanSeedScale,
                 CardsConfig.ItemFanSeedScale, Defaults.ItemFanSeedScale);
        n += Fac(payload, ref i, NetProtocol.TuneItemFanSettleOvershoot,
                 CardsConfig.ItemFanSettleOvershoot, Defaults.ItemFanSettleOvershoot);
        n += Fac(payload, ref i, NetProtocol.TuneItemFanCloseDuration,
                 CardsConfig.ItemFanCloseDuration, Defaults.ItemFanCloseDuration);
        n += Fac(payload, ref i, NetProtocol.TuneItemFanCloseStagger,
                 CardsConfig.ItemFanCloseStagger, Defaults.ItemFanCloseStagger);
        n += Fac(payload, ref i, NetProtocol.TuneFanSwapDuration,
                 CardsConfig.FanSwapDuration, Defaults.FanSwapDuration);
        n += Fac(payload, ref i, NetProtocol.TuneFanSwapStagger,
                 CardsConfig.FanSwapStagger, Defaults.FanSwapStagger);
        n += Fac(payload, ref i, NetProtocol.TuneFanSwapSeedScale,
                 CardsConfig.FanSwapSeedScale, Defaults.FanSwapSeedScale);
        n += Fac(payload, ref i, NetProtocol.TuneFanSwapSettleOvershoot,
                 CardsConfig.FanSwapSettleOvershoot, Defaults.FanSwapSettleOvershoot);
        // The HAND FAN's REVEAL and the pile fans' shape — the rest of the set the old ceiling had
        // locked out. RemoteHandFan held the reveal as `const OpenSeconds/OpenStagger`, so every
        // peer's hand appeared at THIS client's timing; the 1:1 ruling names animations outright.
        n += Fac(payload, ref i, NetProtocol.TuneFanOpenDuration,
                 CardsConfig.FanOpenDuration, Defaults.FanOpenDuration);
        n += Fac(payload, ref i, NetProtocol.TuneFanOpenStagger,
                 CardsConfig.FanOpenStagger, Defaults.FanOpenStagger);
        // [Cards] FanCloseDuration is DELIBERATELY NOT SAMPLED, though id 156 is declared for it.
        // RemoteHandFan has no collapse animation at all — it hides the fan outright — so sending
        // the dial would put three bytes on the wire that no receiver reads, AND would let
        // scripts/check-wire-coverage.py report it as covered while a peer still sees no difference.
        // A field id costs nothing to reserve; a false "covered" costs the guard its meaning. The
        // guard carries it as a PENDING debt instead, which is what it is.
        n += Fac(payload, ref i, NetProtocol.TuneCardLerpSpeed,
                 CardsConfig.CardLerpSpeed, Defaults.CardLerpSpeed);
        n += Fac(payload, ref i, NetProtocol.TuneFanRadiusFactorItems,
                 CardsConfig.FanRadiusFactor(PileKind.Items), Defaults.FanRadiusFactor_Items);
        n += Fac(payload, ref i, NetProtocol.TuneFanRadiusFactorDiscard,
                 CardsConfig.FanRadiusFactor(PileKind.Discard), Defaults.FanRadiusFactor_Discard);
        n += Fac(payload, ref i, NetProtocol.TuneFanRadiusFactorBurnt,
                 CardsConfig.FanRadiusFactor(PileKind.Burnt), Defaults.FanRadiusFactor_Burnt);
        // The USABLE-ITEM CUE and the ITEM-USE BERTH (ids 161..169, plus 80 above). These nine and
        // the berth's outline were FROZEN constants in RemoteControlBoard's mirrored pile cue and
        // RemoteBoardFurniture's mirrored berth, for one reason and one only: record 28 was at its
        // exact 255-byte ceiling when that re-art landed, so its ten dials could not ride. Paging
        // removed the ceiling and the reason expired — an owner who slows their item heartbeat,
        // dims the rings or opens the berth's glow must be SEEN doing it, which is the 1:1 ruling
        // read literally ("Ändert ein Spieler also die Positionen für sich selber, so sollen alle
        // anderen diese Position bei seinem board auch sehen", 2026-08-09) and, for the animated
        // half of them, the older ruling that names ANIMATIONS outright.
        n += Fac(payload, ref i, NetProtocol.TuneItemCueBeatSeconds,
                 CardsConfig.ItemCueBeatSeconds, Defaults.ItemCueBeatSeconds);
        n += Fac(payload, ref i, NetProtocol.TuneItemCueRingReach,
                 CardsConfig.ItemCueRingReach, Defaults.ItemCueRingReach);
        n += Fac(payload, ref i, NetProtocol.TuneItemCueRingAlpha,
                 CardsConfig.ItemCueRingAlpha, Defaults.ItemCueRingAlpha);
        // …and the ONE dial whose config range (0..60 embers/s) overruns the factor width's own
        // ±32.767, carried in TENTHS so a player at 40 embers/s is not silently clamped to 32.767 on
        // every peer's screen. The scale is NetProtocol's, named once and un-applied in
        // RemoteBoardTuning; see TuneItemCueEmberRate for the whole argument.
        n += FacScaled(payload, ref i, NetProtocol.TuneItemCueEmberRate,
                       CardsConfig.ItemCueEmberRate, Defaults.ItemCueEmberRate,
                       NetProtocol.TuneItemCueEmberRateScale);
        n += Fac(payload, ref i, NetProtocol.TuneItemCueEmberSize,
                 CardsConfig.ItemCueEmberSize, Defaults.ItemCueEmberSize);
        n += Fac(payload, ref i, NetProtocol.TuneItemBerthGlow,
                 CardsConfig.ItemBerthGlow, Defaults.ItemBerthGlow);
        n += Fac(payload, ref i, NetProtocol.TuneItemBerthPingSeconds,
                 CardsConfig.ItemBerthPingSeconds, Defaults.ItemBerthPingSeconds);
        n += Fac(payload, ref i, NetProtocol.TuneItemBerthPingReach,
                 CardsConfig.ItemBerthPingReach, Defaults.ItemBerthPingReach);
        n += Fac(payload, ref i, NetProtocol.TuneItemBerthRevealSeconds,
                 CardsConfig.ItemBerthRevealSeconds, Defaults.ItemBerthRevealSeconds);
        // The keycap label's keyline WIDTH — a 0..1 fraction of the SDF spread, so it rides the
        // factor width with the rest of the dimensionless dials. Its COLOUR is id 49 and its
        // on/off id 229: three containers for one look, because the record's ranges are widths.
        n += Fac(payload, ref i, NetProtocol.TuneLabelOutlineWidth,
                 WorldUI.ButtonTuning.LabelOutlineW, Defaults.LabelOutlineWidth);

        // ---- ANGLE fields (ids 192..200) ------------------------------------------------------
        n += Ang(payload, ref i, NetProtocol.TuneAssetPitch,
                 CardsConfig.AssetPitch(style), CardsConfig.BoardDefaults.AssetPitchDegrees[b]);
        n += Ang(payload, ref i, NetProtocol.TuneAssetYaw,
                 CardsConfig.AssetYaw(style), Defaults.AssetYawDegrees_ByBoard[b]);
        n += Ang(payload, ref i, NetProtocol.TuneAssetRoll,
                 CardsConfig.AssetRoll(style), Defaults.AssetRollDegrees_ByBoard[b]);
        n += Ang(payload, ref i, NetProtocol.TuneFanArcSweep,
                 CardsConfig.FanArcSweepDegrees, Defaults.FanArcSweepDegrees);
        n += Ang(payload, ref i, NetProtocol.TuneFanPerCardStep,
                 CardsConfig.FanPerCardStepDegrees, Defaults.FanPerCardStepDegrees);
        n += Ang(payload, ref i, NetProtocol.TuneItemFanOpenSpin,
                 CardsConfig.ItemFanOpenSpinDegrees, Defaults.ItemFanOpenSpinDegrees);
        n += Ang(payload, ref i, NetProtocol.TuneFanStepDegreesItems,
                 CardsConfig.FanStepDegrees(PileKind.Items), Defaults.FanStepDegrees_Items);
        n += Ang(payload, ref i, NetProtocol.TuneFanStepDegreesDiscard,
                 CardsConfig.FanStepDegrees(PileKind.Discard), Defaults.FanStepDegrees_Discard);
        n += Ang(payload, ref i, NetProtocol.TuneFanStepDegreesBurnt,
                 CardsConfig.FanStepDegrees(PileKind.Burnt), Defaults.FanStepDegrees_Burnt);

        // ---- COUNT fields (ids 224..225) ------------------------------------------------------
        n += Cnt(payload, ref i, NetProtocol.TuneFanMaxHandForCurve,
                 CardsConfig.FanMaxHandForCurve, Defaults.FanMaxHandForCurve);
        n += Cnt(payload, ref i, NetProtocol.TuneFanCurveMinCards,
                 CardsConfig.FanCurveMinCards, Defaults.FanCurveMinCards);
        // The swap's last two dials ride the COUNT width — a byte each, which is what keeps the
        // record's worst case at 255 instead of 257 (see MaxPayloadBytes). Rounded, never
        // truncated, so a dial sitting at its shipped default still compares equal to it.
        n += Quantized(payload, ref i, NetProtocol.TuneFanSwapSpin,
                       CardsConfig.FanSwapSpinDegrees, Defaults.FanSwapSpinDegrees, 1f);
        n += Quantized(payload, ref i, NetProtocol.TuneFanSwapOverlapPercent,
                       CardsConfig.FanSwapOverlap, Defaults.FanSwapOverlap, 100f);
        // THE THREE CAP SHAPES — enums in the one-byte count width. The turn-flow cap's shape rode
        // alone one build ago because it was the only one whose MIRROR could draw both members; the
        // other two were a stated PENDING debt, not a decision. RemoteBoardFurniture branches
        // Round/Square for the rest pair and the Confirm/Undo/USE column now, so all three ride and
        // none of them is a field that reports "covered" while a peer still sees the wrong shape.
        n += Enum8(payload, ref i, NetProtocol.TuneRoundCapShape,
                   WorldUI.ButtonTuning.RoundShape, Defaults.RoundButtons_Shape);
        // …and the two BOOLS of the [ButtonColors] family, which is what a two-state dial is in a
        // one-byte container. A bool has no quantization to speak of, so "differs from the shipped
        // default" is the code comparison every other kind here makes, trivially.
        n += Bool8(payload, ref i, NetProtocol.TuneLabelOutlineOn,
                   WorldUI.ButtonTuning.LabelOutline, Defaults.LabelOutline);
        n += Bool8(payload, ref i, NetProtocol.TuneLabelUnderlayOn,
                   WorldUI.ButtonTuning.LabelUnderlay, Defaults.LabelUnderlay);
        // The PER-BOARD shapes, sampled for the sender's OWN style exactly like every other
        // per-board dial (the style itself rides the extras block, so only one is ever sent).
        n += Enum8(payload, ref i, NetProtocol.TuneRestCapShape,
                   CardsConfig.RestButtonShape(style), Defaults.RestButtonShape_ByBoard[b]);
        n += Enum8(payload, ref i, NetProtocol.TuneGenericCapShape,
                   CardsConfig.GenericButtonShape(style), Defaults.GenericButtonShape_ByBoard[b]);

        if (n == 0)
            return 0;                  // every dial at its shipped default — write NO record

        // THE ONE BOUND LEFT, CHECKED ON THE BYTES WE ACTUALLY PRODUCED. Paging removed the 255-byte
        // ceiling; what remains is the FIELD-ID SPACE (247 usable ids, 921 bytes at their widths),
        // and reaching it needs a dial appended above with an id nobody could have declared. The
        // check is here rather than as a `MaxPayloadBytes > …` assertion because that form would be
        // constant-folded away — both sides are compile-time consts, so it could never fire — while
        // THIS one is reachable and catches the real failure. Refusing the whole tuning is the safe
        // direction (every dial falls back to the shipped default, i.e. the pre-record picture) and
        // the line says exactly what caused it. NOTHING HERE MAY EVER DROP A DIAL QUIETLY: that is
        // the property the 1:1 ruling actually demands, and it is why this stays even though the
        // ceiling it once guarded is gone.
        if (i > NetProtocol.BoardTuneMaxFieldBytes)
        {
            if (!s_ceilingLogged)
            {
                s_ceilingLogged = true;
                Core.VRLog.Error("Net", $"BOARD TUNING built {i} field bytes, past the id space's own worst " +
                                        $"case of {NetProtocol.BoardTuneMaxFieldBytes} — which is only " +
                                        "reachable if a dial was appended with a field id outside the declared " +
                                        "ranges, or the same id twice. The whole tuning is dropped rather than " +
                                        "sent short: peers see this player at the shipped defaults, LOUDLY " +
                                        "rather than invisibly. See NetProtocol.BoardTuneMaxFields.");
            }
            return 0;
        }
        return i;
    }

    // ---- per-kind appenders. Each returns 1 when it wrote a field, 0 otherwise, so the caller
    // sums them into the count byte. A null config entry (pre-Bind) counts as "at default": the
    // designed failure direction is always "no record" ⇒ the receiver's shipped constant.

    private static int Vec(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<Vector3>? live, Vector3 shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneVectorField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    /// <summary>
    /// A COLOUR dial. Unlike every other appender here it takes a resolved <see cref="Color"/>
    /// rather than a <c>ConfigEntry</c>, because a colour is THREE entries on the local side and the
    /// live accessor that combines them (<c>WorldUI.ButtonTuning.LabelColor</c> and friends) is the
    /// exact value the owner's own caps are painted with — including its clamps. Reaching past it to
    /// the three raw entries would re-implement those clamps here, and a sender whose clamps differ
    /// from its own renderer's is the divergence this whole record exists to end.
    ///
    /// <para>Those accessors are null-safe by construction (they fall back to the authored colour
    /// before <c>Bind</c>), so there is no null guard to write: the pre-Bind value already equals
    /// the shipped default and therefore writes no field, which is the same "no record ⇒ the
    /// receiver's shipped constant" failure direction the other appenders get from their null
    /// check.</para>
    /// </summary>
    private static int Col(byte[] p, ref int i, byte id, Color live, Color shipped) =>
        NetProtocol.WriteTuneColorField(p, ref i, id, live, shipped) ? 1 : 0;

    private static int Len(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<float>? live, float shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneLengthField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    private static int Fac(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<float>? live, float shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneFactorField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    /// <summary>
    /// A FACTOR-width dial carried in a SCALED unit: the live and shipped values are both multiplied
    /// by <paramref name="scale"/> before they are quantized, so the comparison that decides whether
    /// to emit the field still happens on the wire CODE — the property that keeps "differs from the
    /// default" stable across a config file's float round-trip, exactly as in <see cref="Quantized"/>.
    ///
    /// <para>It exists for one dial (<see cref="NetProtocol.TuneItemCueEmberRate"/>) and it exists
    /// because that dial's config range overruns the factor width's ±32.767, not because scaling is
    /// tidy. Reaching for it anywhere else is a sign the dial wants a different ID RANGE.</para>
    /// </summary>
    private static int FacScaled(byte[] p, ref int i, byte id,
                                 BepInEx.Configuration.ConfigEntry<float>? live, float shipped,
                                 float scale) =>
        live == null ? 0
            : NetProtocol.WriteTuneFactorField(p, ref i, id, live.Value * scale, shipped * scale)
                ? 1 : 0;

    private static int Ang(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<float>? live, float shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneAngleField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    /// <summary>
    /// A two-member ENUM dial carried in the one-byte COUNT width: sampled as its integer value and
    /// compared against the shipped member's, so "differs from the default" is decided on the WIRE
    /// CODE exactly like every other kind here. The receiver never indexes an enum with a wire
    /// number — see <see cref="RemoteBoardTuning"/>'s shape resolution, which maps anything it does
    /// not recognise back to the shipped member.
    /// </summary>
    private static int Enum8<T>(byte[] p, ref int i, byte id,
                                BepInEx.Configuration.ConfigEntry<T>? live, T shipped)
        where T : struct, System.Enum =>
        live == null ? 0
            : NetProtocol.WriteTuneCountField(p, ref i, id,
                System.Convert.ToInt32(live.Value), System.Convert.ToInt32(shipped)) ? 1 : 0;

    /// <summary>A BOOL dial in the one-byte COUNT width — 0 or 1, compared as the wire code like
    /// every other kind here. The receiver reads it back as <c>code != 0</c>, so a corrupt sender's
    /// third value can only ever mean "on", never crash a branch.</summary>
    private static int Bool8(byte[] p, ref int i, byte id,
                             BepInEx.Configuration.ConfigEntry<bool>? live, bool shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneCountField(p, ref i, id, live.Value ? 1 : 0, shipped ? 1 : 0)
                ? 1 : 0;

    private static int Cnt(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<int>? live, int shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneCountField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    /// <summary>
    /// A FLOAT dial carried in the one-byte COUNT width: multiplied by <paramref name="unit"/> and
    /// ROUNDED (degrees at unit 1, a 0..1 fraction as whole percent at unit 100), then compared as
    /// the quantized code exactly like every other kind here — which is what keeps "differs from the
    /// default" stable across a config file's float round-trip, and what stops a dial that was never
    /// moved from being emitted forever. Clamped to the byte's range so a nonsense config value can
    /// only ever saturate, never wrap into a different picture.
    /// </summary>
    private static int Quantized(byte[] p, ref int i, byte id,
                                 BepInEx.Configuration.ConfigEntry<float>? live, float shipped,
                                 float unit)
    {
        if (live == null)
            return 0;
        int code = Mathf.Clamp(Mathf.RoundToInt(live.Value * unit), 0, 255);
        int def = Mathf.Clamp(Mathf.RoundToInt(shipped * unit), 0, 255);
        return NetProtocol.WriteTuneCountField(p, ref i, id, code, def) ? 1 : 0;
    }
}

/// <summary>
/// THE RECEIVER SIDE of extension record 28 — a peer's dial positions, resolved once against this
/// client's own shipped defaults and then read like any other constant.
///
/// <para>Every member falls back to the SHIPPED default for the peer's synced board style when the
/// corresponding field is absent, which is what makes the sparse record correct: peers must run the
/// same <see cref="NetProtocol.ModBuild"/> to play together (the handshake blocks a mismatch before
/// a packet is interpreted), so both ends compile the same <c>Defaults</c> tables and "field
/// absent" means exactly "the value you already have". A pre-record sender, an untuned sender and a
/// sender who has moved only ONE dial therefore all render correctly through the same code path.</para>
///
/// <para>It is a readonly struct built at board-(re)build time, not a per-frame lookup: the wire
/// payload is scanned once per field here, and every consumer then reads a plain float.</para>
/// </summary>
internal readonly struct RemoteBoardTuning
{
    /// <summary>The peer's synced board style these values were resolved for.</summary>
    public ControlBoard Style { get; }

    /// <summary>True when the peer actually sent a tuning record — i.e. at least one dial of theirs
    /// is off the shipped default. Drives the change-gated receive log; every value below is
    /// correct either way.</summary>
    public bool Tuned { get; }

    /// <summary>How many fields the record carried (0 when <see cref="Tuned"/> is false). Log only.</summary>
    public int FieldCount { get; }

    // ---- VECTOR3 dials --------------------------------------------------------------------
    public Vector3 ObjectivesOffset { get; }
    public Vector3 ElementsOffset { get; }
    public Vector3 InitiativeOffset { get; }
    public Vector3 PileOffset { get; }
    public Vector3 ActiveOffset { get; }
    public Vector3 ReadoutOffset { get; }
    public Vector3 PickBannerOffset { get; }
    public Vector3 HoverHintOffset { get; }
    public Vector3 ConfirmUndoOffset { get; }
    public Vector3 RestButtonOffset { get; }
    public Vector3 PinOffset { get; }
    public Vector3 ItemUseSlotOffset { get; }
    public Vector3 DecisionOffset { get; }
    public Vector3 SlotOverlayOffset { get; }

    /// <summary>[Cards] ClusterOffset_{board} — where the owner's docked turn-flow SKIP cap
    /// stands. Its ClusterScale twin has ridden the wire since record 28 shipped; the position
    /// could not, because until ModBuild 97 neither end applied it (NetProtocol.TuneClusterOffset).</summary>
    public Vector3 ClusterOffset { get; }

    /// <summary>The BOARD MESH's own pose offset inside the board root
    /// (<c>PlayTray.SetAssetPose</c>). The bronze board ships a non-zero default, so a peer on
    /// bronze exercises this even untuned.</summary>
    public Vector3 AssetOffset { get; }

    // ---- LENGTH dials (metres) --------------------------------------------------------------
    public float PileSpacing { get; }
    public float RestButtonDiameter { get; }
    public float RestButtonSpacing { get; }
    public float GenericButtonSpacing { get; }
    public float SlotOverlaySpacing { get; }
    public float DecisionGap { get; }
    public float CardWidth { get; }
    public float FanPalmOffset { get; }
    public float FanEffectiveRadius { get; }
    public float FanSideDepthCurve { get; }
    public float FanSplitMultiplier { get; }
    public float FanSelectedPopForward { get; }

    /// <summary>[Cards] ItemFanOpenArc — the item chip's mid-flight bow toward its viewer.</summary>
    public float ItemFanOpenArc { get; }

    /// <summary>[Cards] FanSwapTravel — how far past the arc's end the exchange's gather/deal point sits.</summary>
    public float FanSwapTravel { get; }

    /// <summary>[Cards] FanSwapArc — the exchange's mid-flight depth amplitude.</summary>
    public float FanSwapArc { get; }

    /// <summary>[Cards] FanRadius — the base arc radius the BOARD PILE fans multiply.</summary>
    public float FanRadius { get; }

    /// <summary>[Cards] ItemBerthRingThickness — the outline thickness of the mirrored item-use
    /// berth, in metres.</summary>
    public float ItemBerthRingThickness { get; }

    // ---- the KEYCAP GEOMETRY family (ids 81..98 + the shape at 228). Every one of these was a
    // frozen constant in RemoteBoardFurniture until this build; see NetProtocol's block comment on
    // TuneRoundOffsetX for the debt they pay off and the report that cashed it in.

    /// <summary>[RoundButtons] OffsetX — the turn-flow SKIP cap group's sideways seat.</summary>
    public float RoundOffsetX { get; }
    /// <summary>[RoundButtons] OffsetY — that group's up-board seat.</summary>
    public float RoundOffsetY { get; }
    /// <summary>[RoundButtons] OffsetZ — how far out of the board face it is seated.</summary>
    public float RoundOffsetZ { get; }
    /// <summary>[RoundButtons] CapSize — the turn-flow cap radius.</summary>
    public float RoundCapSize { get; }
    /// <summary>[RoundButtons] Width — its width while the shape is Square.</summary>
    public float RoundCapWidth { get; }
    /// <summary>[RoundButtons] Height — its height while the shape is Square.</summary>
    public float RoundCapHeight { get; }
    /// <summary>[RoundButtons] Depth — its extrusion toward the player.</summary>
    public float RoundCapDepth { get; }
    /// <summary>[RoundButtons] Travel — how far it sinks under a press.</summary>
    public float RoundCapTravel { get; }

    /// <summary>[RoundButtons] Shape — Round puck or Square keycap. Resolved through a KNOWN-MEMBER
    /// test, never by casting the wire byte: an unrecognised code falls back to the shipped shape,
    /// so a corrupt or future sender can only ever make the cap look like this build's default.</summary>
    public ButtonShape RoundCapShape { get; }

    /// <summary>[BoardButtons] Width — the Confirm/Undo keycap width.</summary>
    public float BoardCapWidth { get; }
    /// <summary>[BoardButtons] Height.</summary>
    public float BoardCapHeight { get; }
    /// <summary>[BoardButtons] Depth.</summary>
    public float BoardCapDepth { get; }
    /// <summary>[BoardButtons] Travel.</summary>
    public float BoardCapTravel { get; }

    /// <summary>[BoardDashboard] PinWidth — the follow/pin plate width.</summary>
    public float DashPinWidth { get; }
    /// <summary>[BoardDashboard] Height.</summary>
    public float DashCapHeight { get; }
    /// <summary>[BoardDashboard] Depth.</summary>
    public float DashCapDepth { get; }
    /// <summary>[BoardDashboard] Travel.</summary>
    public float DashCapTravel { get; }

    /// <summary>[RestButtons] Depth — the rest disc thickness.</summary>
    public float RestCapDepth { get; }
    /// <summary>[RestButtons] Travel — the rest press travel.</summary>
    public float RestCapTravel { get; }
    /// <summary>[RestButtons] Width — the rest keycap width while <see cref="RestCapShape"/> is
    /// Square (a round disc takes <see cref="RestButtonDiameter"/> instead, exactly as locally).</summary>
    public float RestCapWidth { get; }
    /// <summary>[RestButtons] Height — its height while the shape is Square.</summary>
    public float RestCapHeight { get; }

    /// <summary>[Cards] RestButtonShape_{board} — ROUND disc or SQUARE keycap for the peer's rest
    /// pair. Resolved through the same KNOWN-MEMBER test as <see cref="RoundCapShape"/>.</summary>
    public ButtonShape RestCapShape { get; }

    /// <summary>[Cards] GenericButtonShape_{board} — ROUND or SQUARE for the peer's Confirm / Undo /
    /// item-USE column.</summary>
    public ButtonShape GenericCapShape { get; }

    // ---- the [ButtonColors] family (ids 48..53 + 170 + 229..230) -------------------------------
    // A peer's caps are LETTERED and TINTED in their owner's colours now. Before this build
    // RemoteBoardFurniture read none of these — not even at their defaults — so the mirrored caps
    // were drawn at the raw state palette while every local cap is drawn at palette × a 0.5 TINT:
    // two completely untuned players saw each other's boards at twice their own brightness. See the
    // cap-palette block in RemoteBoardFurniture for the fix and NetProtocol.TuneLabelColor for the
    // ruling that made the colours wire content rather than "a look decision".

    /// <summary>[ButtonColors] LabelR/G/B — the fill colour of every engraved keycap letter.</summary>
    public Color LabelColor { get; }
    /// <summary>[ButtonColors] LabelOutlineR/G/B — the dark keyline ringing those letters.</summary>
    public Color LabelOutlineColor { get; }
    /// <summary>[ButtonColors] LabelOutlineWidth — that keyline's width, fraction of the SDF spread.</summary>
    public float LabelOutlineWidth { get; }
    /// <summary>[ButtonColors] LabelOutline — whether the keyline is drawn at all.</summary>
    public bool LabelOutlineOn { get; }
    /// <summary>[ButtonColors] LabelUnderlay — whether the drop-shadow underlay is drawn.</summary>
    public bool LabelUnderlayOn { get; }
    /// <summary>[ButtonColors] BoardCapTint — the Confirm / Undo / item-USE cap FACE multiplier.</summary>
    public Color BoardCapTint { get; }
    /// <summary>[ButtonColors] DashCapTint — the follow/pin plate face multiplier.</summary>
    public Color DashCapTint { get; }
    /// <summary>[ButtonColors] ClusterCapTint — the turn-flow SKIP cap face multiplier.</summary>
    public Color ClusterCapTint { get; }
    /// <summary>[ButtonColors] RestCapTint — the short/long rest cap face multiplier.</summary>
    public Color RestCapTint { get; }

    // ---- FACTOR dials ------------------------------------------------------------------------
    public float ObjectivesScale { get; }
    public float ObjectivesWidth { get; }
    public float ElementsScale { get; }
    public float PileScale { get; }
    public float ActiveCardScale { get; }
    /// <summary>The owner's slot-overlay size — the blinking wanted-glow AND the card that rests in
    /// it, one number (<c>[Cards] SlotOverlayScale_{board}</c>). See NetProtocol's field doc.</summary>
    public float SlotOverlayScale { get; }
    public float ClusterScale { get; }
    public float DecisionScale { get; }
    public float FanFlatCurvatureFactor { get; }
    public float FanTiltFactor { get; }
    public float FanFaceViewer { get; }
    public float FanCurvePower { get; }
    public float FanGazeApexFollow { get; }
    public float FanSplitFalloff { get; }
    public float FanHoverSplitScale { get; }
    public float HoverInfoScale { get; }
    public float CanvasScaleMm { get; }

    // The item fan's ANIMATION (presence pass). The four SECONDS values ride the factor width —
    // see the id table in NetProtocol for why that is the right container and not a category slip.
    public float ItemFanOpenDuration { get; }
    public float ItemFanOpenStagger { get; }
    public float ItemFanSeedScale { get; }
    public float ItemFanSettleOvershoot { get; }
    public float ItemFanCloseDuration { get; }
    public float ItemFanCloseStagger { get; }

    // The hand fan's character-SWAP exchange. Two seconds values on the factor width (as above) and
    // — see the id table — two more that arrive as one-byte COUNTS and are un-quantized here, so
    // every consumer reads a plain float in the dial's own unit and no reader has to know.
    public float FanSwapDuration { get; }
    public float FanSwapStagger { get; }
    public float FanSwapSeedScale { get; }
    public float FanSwapSettleOvershoot { get; }

    // The HAND FAN's REVEAL and the BOARD PILE fans' shape — the dials the record's 255-byte
    // ceiling had locked out until it was paged away (see NetProtocol.TuneFanRadius).

    /// <summary>[Cards] FanOpenDuration — seconds one hand-fan card takes to appear.</summary>
    public float FanOpenDuration { get; }
    /// <summary>[Cards] FanOpenStagger — the reveal ripple's per-card delay.</summary>
    public float FanOpenStagger { get; }
    /// <summary>[Cards] CardLerpSpeed — the exponential rate a card flies to its slot at.</summary>
    public float CardLerpSpeed { get; }
    /// <summary>[Cards] FanRadiusFactor_Items — the items pile fan's radius multiplier.</summary>
    public float FanRadiusFactorItems { get; }
    /// <summary>[Cards] FanRadiusFactor_Discard.</summary>
    public float FanRadiusFactorDiscard { get; }
    /// <summary>[Cards] FanRadiusFactor_Burnt.</summary>
    public float FanRadiusFactorBurnt { get; }

    // The USABLE-ITEM CUE on the closed items pile and the ITEM-USE BERTH (ids 161..169, plus 80
    // above). Frozen constants in their consumers until the record was paged and had room again; the
    // seconds- and per-second-valued members ride the FACTOR width for the reason stated everywhere
    // else here — an id range fixes the value WIDTH, not the unit. ItemCueEmberRate is un-scaled
    // back into embers per second in the constructor, so every consumer reads a plain dial.

    /// <summary>[Cards] ItemCueBeatSeconds — the cue's heartbeat period, seconds.</summary>
    public float ItemCueBeatSeconds { get; }
    /// <summary>[Cards] ItemCueRingReach — how far a ring travels off the pile.</summary>
    public float ItemCueRingReach { get; }
    /// <summary>[Cards] ItemCueRingAlpha — peak opacity of those rings.</summary>
    public float ItemCueRingAlpha { get; }
    /// <summary>[Cards] ItemCueEmberRate — embers per second, in the dial's OWN unit (the wire
    /// carries tenths — see <see cref="NetProtocol.TuneItemCueEmberRateScale"/>).</summary>
    public float ItemCueEmberRate { get; }
    /// <summary>[Cards] ItemCueEmberSize — ember size multiplier.</summary>
    public float ItemCueEmberSize { get; }
    /// <summary>[Cards] ItemBerthGlow — brightness of the warm field inside the berth.</summary>
    public float ItemBerthGlow { get; }
    /// <summary>[Cards] ItemBerthPingSeconds — the inward ping's period, seconds.</summary>
    public float ItemBerthPingSeconds { get; }
    /// <summary>[Cards] ItemBerthPingReach — where outside the card rect that ping starts.</summary>
    public float ItemBerthPingReach { get; }
    /// <summary>[Cards] ItemBerthRevealSeconds — the berth's grow-in / collapse-out time.</summary>
    public float ItemBerthRevealSeconds { get; }

    /// <summary>[Cards] FanSwapOverlap as a 0..1 fraction (the wire carries whole percent).</summary>
    public float FanSwapOverlap { get; }

    /// <summary>[Cards] FanSwapSpinDegrees (the wire carries whole degrees).</summary>
    public float FanSwapSpinDegrees { get; }

    // ---- ANGLE dials (degrees) ---------------------------------------------------------------
    public float AssetPitchDegrees { get; }
    public float AssetYawDegrees { get; }
    public float AssetRollDegrees { get; }
    public float FanArcSweepDegrees { get; }
    public float FanPerCardStepDegrees { get; }
    public float ItemFanOpenSpinDegrees { get; }

    /// <summary>[Cards] FanStepDegrees_Items — the items pile fan's per-card angular step.</summary>
    public float FanStepDegreesItems { get; }
    /// <summary>[Cards] FanStepDegrees_Discard.</summary>
    public float FanStepDegreesDiscard { get; }
    /// <summary>[Cards] FanStepDegrees_Burnt.</summary>
    public float FanStepDegreesBurnt { get; }

    // ---- COUNT dials -------------------------------------------------------------------------
    public int FanMaxHandForCurve { get; }
    public int FanCurveMinCards { get; }

    /// <summary>Resolve a peer's tuning. <paramref name="payload"/>/<paramref name="len"/> are the
    /// ASSEMBLED record-28 bytes — <c>[n][n × [id][value]]</c>, the output of that peer's
    /// <see cref="BoardTunePageAssembler"/>, never a raw wire page — and are null / 0 for a peer with
    /// nothing tuned or one whose first generation has not converged yet. Both of those yield the
    /// shipped layout exactly as before this record existed.</summary>
    public RemoteBoardTuning(ControlBoard style, byte[]? payload, int len)
    {
        Style = style;
        int b = (int)ControlBoards.Clamp((int)style);
        bool has = payload != null && len >= NetProtocol.BoardTuneAssembledMinBytes;
        Tuned = has;
        FieldCount = has ? payload![0] : 0;

        ObjectivesOffset = V(payload, len, NetProtocol.TuneObjectivesOffset,
                             CardsConfig.BoardDefaults.ObjectivesOffset[b]);
        ElementsOffset = V(payload, len, NetProtocol.TuneElementsOffset,
                           CardsConfig.BoardDefaults.ElementsOffset[b]);
        InitiativeOffset = V(payload, len, NetProtocol.TuneInitiativeOffset,
                             CardsConfig.BoardDefaults.InitiativeOffset[b]);
        PileOffset = V(payload, len, NetProtocol.TunePileOffset,
                       CardsConfig.BoardDefaults.PileOffset[b]);
        ActiveOffset = V(payload, len, NetProtocol.TuneActiveOffset,
                         CardsConfig.BoardDefaults.ActiveOffset[b]);
        ReadoutOffset = V(payload, len, NetProtocol.TuneReadoutOffset,
                          CardsConfig.BoardDefaults.ReadoutOffset[b]);
        PickBannerOffset = V(payload, len, NetProtocol.TunePickBannerOffset,
                             CardsConfig.BoardDefaults.PickBannerOffset[b]);
        HoverHintOffset = V(payload, len, NetProtocol.TuneHoverHintOffset,
                            CardsConfig.BoardDefaults.HoverHintOffset[b]);
        ConfirmUndoOffset = V(payload, len, NetProtocol.TuneConfirmUndoOffset,
                              CardsConfig.BoardDefaults.ConfirmUndoOffset[b]);
        RestButtonOffset = V(payload, len, NetProtocol.TuneRestButtonOffset,
                             CardsConfig.BoardDefaults.RestButtonOffset[b]);
        PinOffset = V(payload, len, NetProtocol.TunePinOffset,
                      CardsConfig.BoardDefaults.PinOffset[b]);
        ItemUseSlotOffset = V(payload, len, NetProtocol.TuneItemUseSlotOffset,
                              Defaults.ItemUseSlotOffset_ByBoard[b]);
        DecisionOffset = V(payload, len, NetProtocol.TuneDecisionOffset,
                           CardsConfig.BoardDefaults.DecisionOffset[b]);
        SlotOverlayOffset = V(payload, len, NetProtocol.TuneSlotOverlayOffset,
                              CardsConfig.BoardDefaults.SlotOverlayOffset[b]);
        AssetOffset = V(payload, len, NetProtocol.TuneAssetOffset,
                        CardsConfig.BoardDefaults.AssetOffset[b]);
        ClusterOffset = V(payload, len, NetProtocol.TuneClusterOffset,
                          Defaults.ClusterOffset_ByBoard[b]);

        PileSpacing = L(payload, len, NetProtocol.TunePileSpacing, Defaults.PileSpacing_ByBoard[b]);
        RestButtonDiameter = L(payload, len, NetProtocol.TuneRestButtonDiameter,
                               CardsConfig.BoardDefaults.RestButtonDiameter[b]);
        RestButtonSpacing = L(payload, len, NetProtocol.TuneRestButtonSpacing,
                              CardsConfig.BoardDefaults.RestButtonSpacing[b]);
        GenericButtonSpacing = L(payload, len, NetProtocol.TuneGenericButtonSpacing,
                                 CardsConfig.BoardDefaults.GenericButtonSpacing[b]);
        SlotOverlaySpacing = L(payload, len, NetProtocol.TuneSlotOverlaySpacing,
                               CardsConfig.BoardDefaults.SlotOverlaySpacing[b]);
        DecisionGap = L(payload, len, NetProtocol.TuneDecisionGap,
                        CardsConfig.BoardDefaults.DecisionGap[b]);
        CardWidth = L(payload, len, NetProtocol.TuneCardWidth, Defaults.CardWidth);
        FanPalmOffset = L(payload, len, NetProtocol.TuneFanPalmOffset, Defaults.FanPalmOffset);
        FanEffectiveRadius = L(payload, len, NetProtocol.TuneFanEffectiveRadius,
                               Defaults.FanEffectiveRadius);
        FanSideDepthCurve = L(payload, len, NetProtocol.TuneFanSideDepthCurve,
                              Defaults.FanSideDepthCurve);
        FanSplitMultiplier = L(payload, len, NetProtocol.TuneFanSplitMultiplier,
                               Defaults.FanSplitMultiplier);
        FanSelectedPopForward = L(payload, len, NetProtocol.TuneFanSelectedPopForward,
                                  Defaults.FanSelectedPopForward);
        ItemFanOpenArc = L(payload, len, NetProtocol.TuneItemFanOpenArc, Defaults.ItemFanOpenArc);
        FanSwapTravel = L(payload, len, NetProtocol.TuneFanSwapTravel, Defaults.FanSwapTravel);
        FanSwapArc = L(payload, len, NetProtocol.TuneFanSwapArc, Defaults.FanSwapArc);
        FanRadius = L(payload, len, NetProtocol.TuneFanRadius, Defaults.FanRadius);
        ItemBerthRingThickness = L(payload, len, NetProtocol.TuneItemBerthRingThickness,
                                   Defaults.ItemBerthRingThickness);

        RoundOffsetX = L(payload, len, NetProtocol.TuneRoundOffsetX, Defaults.RoundButtons_OffsetX);
        RoundOffsetY = L(payload, len, NetProtocol.TuneRoundOffsetY, Defaults.RoundButtons_OffsetY);
        RoundOffsetZ = L(payload, len, NetProtocol.TuneRoundOffsetZ, Defaults.OffsetZ);
        RoundCapSize = L(payload, len, NetProtocol.TuneRoundCapSize, Defaults.RoundButtons_CapSize);
        RoundCapWidth = L(payload, len, NetProtocol.TuneRoundCapWidth, Defaults.RoundButtons_Width);
        RoundCapHeight = L(payload, len, NetProtocol.TuneRoundCapHeight, Defaults.RoundButtons_Height);
        RoundCapDepth = L(payload, len, NetProtocol.TuneRoundCapDepth, Defaults.RoundButtons_Depth);
        RoundCapTravel = L(payload, len, NetProtocol.TuneRoundCapTravel, Defaults.RoundButtons_Travel);
        // KNOWN-MEMBER test, not a cast: the wire byte selects a shape only when it names one this
        // build has. Anything else — a corrupt packet, a future sender's third shape — resolves to
        // the SHIPPED member, which is the same picture every pre-record build drew.
        RoundCapShape = Shape(C(payload, len, NetProtocol.TuneRoundCapShape,
                                (int)Defaults.RoundButtons_Shape),
                              Defaults.RoundButtons_Shape);

        BoardCapWidth = L(payload, len, NetProtocol.TuneBoardCapWidth, Defaults.BoardButtons_Width);
        BoardCapHeight = L(payload, len, NetProtocol.TuneBoardCapHeight, Defaults.BoardButtons_Height);
        BoardCapDepth = L(payload, len, NetProtocol.TuneBoardCapDepth, Defaults.BoardButtons_Depth);
        BoardCapTravel = L(payload, len, NetProtocol.TuneBoardCapTravel, Defaults.BoardButtons_Travel);
        DashPinWidth = L(payload, len, NetProtocol.TuneDashPinWidth, Defaults.PinWidth);
        DashCapHeight = L(payload, len, NetProtocol.TuneDashCapHeight, Defaults.BoardDashboard_Height);
        DashCapDepth = L(payload, len, NetProtocol.TuneDashCapDepth, Defaults.BoardDashboard_Depth);
        DashCapTravel = L(payload, len, NetProtocol.TuneDashCapTravel, Defaults.BoardDashboard_Travel);
        RestCapDepth = L(payload, len, NetProtocol.TuneRestCapDepth, Defaults.RestButtons_Depth);
        RestCapTravel = L(payload, len, NetProtocol.TuneRestCapTravel, Defaults.RestButtons_Travel);
        RestCapWidth = L(payload, len, NetProtocol.TuneRestCapWidth, Defaults.RestButtons_Width);
        RestCapHeight = L(payload, len, NetProtocol.TuneRestCapHeight, Defaults.RestButtons_Height);

        // The two PER-BOARD shapes, through the same KNOWN-MEMBER test as the turn-flow cap above:
        // a wire byte selects a shape only when it names one this build has, and anything else is
        // the shipped member for the PEER'S OWN style — which is the shape every build before this
        // one drew unconditionally.
        RestCapShape = Shape(C(payload, len, NetProtocol.TuneRestCapShape,
                               (int)Defaults.RestButtonShape_ByBoard[b]),
                             Defaults.RestButtonShape_ByBoard[b]);
        GenericCapShape = Shape(C(payload, len, NetProtocol.TuneGenericCapShape,
                                  (int)Defaults.GenericButtonShape_ByBoard[b]),
                                Defaults.GenericButtonShape_ByBoard[b]);

        // The [ButtonColors] family. The fallbacks are the SHIPPED colours rather than white or the
        // authored palette: "field absent" has to mean "the value you already have", and for the
        // four cap tints the value everyone already has is 0.5 grey, not identity.
        LabelColor = Cl(payload, len, NetProtocol.TuneLabelColor,
                        new Color(Defaults.LabelR, Defaults.LabelG, Defaults.LabelB, 1f));
        LabelOutlineColor = Cl(payload, len, NetProtocol.TuneLabelOutlineColor,
                               new Color(Defaults.LabelOutlineR, Defaults.LabelOutlineG,
                                         Defaults.LabelOutlineB, 1f));
        LabelOutlineWidth = F(payload, len, NetProtocol.TuneLabelOutlineWidth,
                              Defaults.LabelOutlineWidth);
        LabelOutlineOn = C(payload, len, NetProtocol.TuneLabelOutlineOn,
                           Defaults.LabelOutline ? 1 : 0) != 0;
        LabelUnderlayOn = C(payload, len, NetProtocol.TuneLabelUnderlayOn,
                            Defaults.LabelUnderlay ? 1 : 0) != 0;
        BoardCapTint = Cl(payload, len, NetProtocol.TuneBoardCapTint,
                          new Color(Defaults.BoardCapTintR, Defaults.BoardCapTintG,
                                    Defaults.BoardCapTintB, 1f));
        DashCapTint = Cl(payload, len, NetProtocol.TuneDashCapTint,
                         new Color(Defaults.DashCapTintR, Defaults.DashCapTintG,
                                   Defaults.DashCapTintB, 1f));
        ClusterCapTint = Cl(payload, len, NetProtocol.TuneClusterCapTint,
                            new Color(Defaults.ClusterCapTintR, Defaults.ClusterCapTintG,
                                      Defaults.ClusterCapTintB, 1f));
        RestCapTint = Cl(payload, len, NetProtocol.TuneRestCapTint,
                         new Color(Defaults.RestCapTintR, Defaults.RestCapTintG,
                                   Defaults.RestCapTintB, 1f));

        ObjectivesScale = F(payload, len, NetProtocol.TuneObjectivesScale,
                            CardsConfig.BoardDefaults.ObjectivesScale[b]);
        ObjectivesWidth = F(payload, len, NetProtocol.TuneObjectivesWidth,
                            CardsConfig.BoardDefaults.ObjectivesWidth[b]);
        ElementsScale = F(payload, len, NetProtocol.TuneElementsScale, Defaults.ElementsScale_ByBoard[b]);
        PileScale = F(payload, len, NetProtocol.TunePileScale, Defaults.PileScale_ByBoard[b]);
        ActiveCardScale = F(payload, len, NetProtocol.TuneActiveCardScale,
                            CardsConfig.BoardDefaults.ActiveCardScale[b]);
        SlotOverlayScale = F(payload, len, NetProtocol.TuneSlotOverlayScale,
                             CardsConfig.BoardDefaults.SlotOverlayScale[b]);
        ClusterScale = F(payload, len, NetProtocol.TuneClusterScale, Defaults.ClusterScale_ByBoard[b]);
        DecisionScale = F(payload, len, NetProtocol.TuneDecisionScale, Defaults.DecisionScale_ByBoard[b]);
        FanFlatCurvatureFactor = F(payload, len, NetProtocol.TuneFanFlatCurvatureFactor,
                                   Defaults.FanFlatCurvatureFactor);
        FanTiltFactor = F(payload, len, NetProtocol.TuneFanTiltFactor, Defaults.FanTiltFactor);
        FanFaceViewer = F(payload, len, NetProtocol.TuneFanFaceViewer, Defaults.FanFaceViewer);
        FanCurvePower = F(payload, len, NetProtocol.TuneFanCurvePower, Defaults.FanCurvePower);
        FanGazeApexFollow = F(payload, len, NetProtocol.TuneFanGazeApexFollow,
                              Defaults.FanGazeApexFollow);
        FanSplitFalloff = F(payload, len, NetProtocol.TuneFanSplitFalloff, Defaults.FanSplitFalloff);
        FanHoverSplitScale = F(payload, len, NetProtocol.TuneFanHoverSplitScale,
                               Defaults.FanHoverSplitScale);
        HoverInfoScale = F(payload, len, NetProtocol.TuneHoverInfoScale, Defaults.HoverInfoScale);
        CanvasScaleMm = F(payload, len, NetProtocol.TuneCanvasScaleMm, Defaults.CanvasScaleMm);
        ItemFanOpenDuration = F(payload, len, NetProtocol.TuneItemFanOpenDuration,
                                Defaults.ItemFanOpenDuration);
        ItemFanOpenStagger = F(payload, len, NetProtocol.TuneItemFanOpenStagger,
                               Defaults.ItemFanOpenStagger);
        ItemFanSeedScale = F(payload, len, NetProtocol.TuneItemFanSeedScale, Defaults.ItemFanSeedScale);
        ItemFanSettleOvershoot = F(payload, len, NetProtocol.TuneItemFanSettleOvershoot,
                                   Defaults.ItemFanSettleOvershoot);
        ItemFanCloseDuration = F(payload, len, NetProtocol.TuneItemFanCloseDuration,
                                 Defaults.ItemFanCloseDuration);
        ItemFanCloseStagger = F(payload, len, NetProtocol.TuneItemFanCloseStagger,
                                Defaults.ItemFanCloseStagger);
        FanSwapDuration = F(payload, len, NetProtocol.TuneFanSwapDuration, Defaults.FanSwapDuration);
        FanSwapStagger = F(payload, len, NetProtocol.TuneFanSwapStagger, Defaults.FanSwapStagger);
        FanSwapSeedScale = F(payload, len, NetProtocol.TuneFanSwapSeedScale, Defaults.FanSwapSeedScale);
        FanSwapSettleOvershoot = F(payload, len, NetProtocol.TuneFanSwapSettleOvershoot,
                                   Defaults.FanSwapSettleOvershoot);
        FanOpenDuration = F(payload, len, NetProtocol.TuneFanOpenDuration, Defaults.FanOpenDuration);
        FanOpenStagger = F(payload, len, NetProtocol.TuneFanOpenStagger, Defaults.FanOpenStagger);
        CardLerpSpeed = F(payload, len, NetProtocol.TuneCardLerpSpeed, Defaults.CardLerpSpeed);
        FanRadiusFactorItems = F(payload, len, NetProtocol.TuneFanRadiusFactorItems,
                                 Defaults.FanRadiusFactor_Items);
        FanRadiusFactorDiscard = F(payload, len, NetProtocol.TuneFanRadiusFactorDiscard,
                                   Defaults.FanRadiusFactor_Discard);
        FanRadiusFactorBurnt = F(payload, len, NetProtocol.TuneFanRadiusFactorBurnt,
                                 Defaults.FanRadiusFactor_Burnt);
        ItemCueBeatSeconds = F(payload, len, NetProtocol.TuneItemCueBeatSeconds,
                               Defaults.ItemCueBeatSeconds);
        ItemCueRingReach = F(payload, len, NetProtocol.TuneItemCueRingReach,
                             Defaults.ItemCueRingReach);
        ItemCueRingAlpha = F(payload, len, NetProtocol.TuneItemCueRingAlpha,
                             Defaults.ItemCueRingAlpha);
        // Un-scaled back into embers per second here, the ONE place that mirrors the sampler's
        // FacScaled — the fallback is scaled the same way so an ABSENT field still resolves to the
        // shipped default exactly, never to a tenth of it.
        ItemCueEmberRate = F(payload, len, NetProtocol.TuneItemCueEmberRate,
                             Defaults.ItemCueEmberRate * NetProtocol.TuneItemCueEmberRateScale)
                           / NetProtocol.TuneItemCueEmberRateScale;
        ItemCueEmberSize = F(payload, len, NetProtocol.TuneItemCueEmberSize,
                             Defaults.ItemCueEmberSize);
        ItemBerthGlow = F(payload, len, NetProtocol.TuneItemBerthGlow, Defaults.ItemBerthGlow);
        ItemBerthPingSeconds = F(payload, len, NetProtocol.TuneItemBerthPingSeconds,
                                 Defaults.ItemBerthPingSeconds);
        ItemBerthPingReach = F(payload, len, NetProtocol.TuneItemBerthPingReach,
                               Defaults.ItemBerthPingReach);
        ItemBerthRevealSeconds = F(payload, len, NetProtocol.TuneItemBerthRevealSeconds,
                                   Defaults.ItemBerthRevealSeconds);

        AssetPitchDegrees = A(payload, len, NetProtocol.TuneAssetPitch,
                              CardsConfig.BoardDefaults.AssetPitchDegrees[b]);
        AssetYawDegrees = A(payload, len, NetProtocol.TuneAssetYaw, Defaults.AssetYawDegrees_ByBoard[b]);
        AssetRollDegrees = A(payload, len, NetProtocol.TuneAssetRoll, Defaults.AssetRollDegrees_ByBoard[b]);
        FanArcSweepDegrees = A(payload, len, NetProtocol.TuneFanArcSweep, Defaults.FanArcSweepDegrees);
        FanPerCardStepDegrees = A(payload, len, NetProtocol.TuneFanPerCardStep,
                                  Defaults.FanPerCardStepDegrees);
        ItemFanOpenSpinDegrees = A(payload, len, NetProtocol.TuneItemFanOpenSpin,
                                   Defaults.ItemFanOpenSpinDegrees);
        FanStepDegreesItems = A(payload, len, NetProtocol.TuneFanStepDegreesItems,
                                Defaults.FanStepDegrees_Items);
        FanStepDegreesDiscard = A(payload, len, NetProtocol.TuneFanStepDegreesDiscard,
                                  Defaults.FanStepDegrees_Discard);
        FanStepDegreesBurnt = A(payload, len, NetProtocol.TuneFanStepDegreesBurnt,
                                Defaults.FanStepDegrees_Burnt);

        FanMaxHandForCurve = C(payload, len, NetProtocol.TuneFanMaxHandForCurve,
                               Defaults.FanMaxHandForCurve);
        FanCurveMinCards = C(payload, len, NetProtocol.TuneFanCurveMinCards, Defaults.FanCurveMinCards);
        // The two COUNT-carried swap dials, un-quantized back into their own units here so every
        // consumer reads a plain float and the wire's container never leaks into a renderer.
        FanSwapSpinDegrees = C(payload, len, NetProtocol.TuneFanSwapSpin,
                               Mathf.RoundToInt(Defaults.FanSwapSpinDegrees));
        FanSwapOverlap = C(payload, len, NetProtocol.TuneFanSwapOverlapPercent,
                           Mathf.RoundToInt(Defaults.FanSwapOverlap * 100f)) / 100f;
    }

    private static Vector3 V(byte[]? p, int len, byte id, Vector3 fallback) =>
        NetProtocol.BoardTuneVector(p, 0, len, id, fallback);

    private static float L(byte[]? p, int len, byte id, float fallback) =>
        NetProtocol.BoardTuneLength(p, 0, len, id, fallback);

    private static float F(byte[]? p, int len, byte id, float fallback) =>
        NetProtocol.BoardTuneFactor(p, 0, len, id, fallback);

    private static float A(byte[]? p, int len, byte id, float fallback) =>
        NetProtocol.BoardTuneAngle(p, 0, len, id, fallback);

    private static int C(byte[]? p, int len, byte id, int fallback) =>
        NetProtocol.BoardTuneCount(p, 0, len, id, fallback);

    private static Color Cl(byte[]? p, int len, byte id, Color fallback) =>
        NetProtocol.BoardTuneColor(p, 0, len, id, fallback);

    /// <summary>
    /// THE KNOWN-MEMBER TEST every shape on this record goes through, written once because there are
    /// three of them now. A wire code selects a shape only when it NAMES a member this build has;
    /// anything else — a corrupt packet, a future sender's third shape — resolves to
    /// <paramref name="shipped"/>, which is the shape every build before the field existed drew.
    ///
    /// <para>Casting the byte to the enum instead would be the bug: C# would happily produce
    /// <c>(ButtonShape)7</c>, every <c>== Round</c> test downstream would answer false, and a peer
    /// would silently get the SQUARE branch of a renderer for a shape nobody authored.</para>
    /// </summary>
    private static ButtonShape Shape(int code, ButtonShape shipped) =>
        code == (int)ButtonShape.Round ? ButtonShape.Round
        : code == (int)ButtonShape.Square ? ButtonShape.Square
        : shipped;

    /// <summary>One-line dump for the change-gated board-built log: a wrong seat is then answerable
    /// from the log without a screenshot, and the FIELD COUNT says at a glance whether the peer's
    /// board is at the shipped layout or at their own.</summary>
    public override string ToString() =>
        Tuned
            ? $"style={Style}, {FieldCount} tuned dial(s): objectives={ObjectivesOffset:F3}" +
              $"(×{ObjectivesScale:F2}), piles={PileOffset:F3}(step {PileSpacing:F3}, ×{PileScale:F2}), " +
              $"initiative={InitiativeOffset:F3}, cluster={ClusterOffset:F3}(×{ClusterScale:F2}), " +
              $"mesh={AssetOffset:F3}" +
              $"(pitch {AssetPitchDegrees:F1}°, yaw {AssetYawDegrees:F1}°, roll {AssetRollDegrees:F1}°), " +
              $"fan r={FanEffectiveRadius:F3} sweep={FanArcSweepDegrees:F1}° step={FanPerCardStepDegrees:F1}°"
            : $"style={Style}, no tuning record — every dial at the shipped default";
}
