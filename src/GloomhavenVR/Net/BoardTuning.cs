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
/// Rebuilding is cheap enough to run per send — it allocates nothing and touches ~76 config entries.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28) — LOCAL CONFIG that is neither GLOBAL (each
/// player has their own) nor derivable from anything already synced. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal static class BoardTuningSampler
{
    /// <summary>
    /// Buffer size a caller must hand <see cref="Sample"/>: the FIELD-ID SPACE's own worst case,
    /// <see cref="NetProtocol.BoardTuneMaxFieldBytes"/> = 969 — every one of the 247 usable ids
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

        // ---- LENGTH fields (ids 64..75) — scalars measured in metres ------------------------
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

        // ---- FACTOR fields (ids 128..142) — dimensionless multipliers ------------------------
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

        if (n == 0)
            return 0;                  // every dial at its shipped default — write NO record

        // THE ONE BOUND LEFT, CHECKED ON THE BYTES WE ACTUALLY PRODUCED. Paging removed the 255-byte
        // ceiling; what remains is the FIELD-ID SPACE (247 usable ids, 969 bytes at their widths),
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

    private static int Len(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<float>? live, float shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneLengthField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    private static int Fac(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<float>? live, float shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneFactorField(p, ref i, id, live.Value, shipped) ? 1 : 0;

    private static int Ang(byte[] p, ref int i, byte id,
                           BepInEx.Configuration.ConfigEntry<float>? live, float shipped) =>
        live == null ? 0
            : NetProtocol.WriteTuneAngleField(p, ref i, id, live.Value, shipped) ? 1 : 0;

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

    // ---- FACTOR dials ------------------------------------------------------------------------
    public float ObjectivesScale { get; }
    public float ObjectivesWidth { get; }
    public float ElementsScale { get; }
    public float PileScale { get; }
    public float ActiveCardScale { get; }
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

        ObjectivesScale = F(payload, len, NetProtocol.TuneObjectivesScale,
                            CardsConfig.BoardDefaults.ObjectivesScale[b]);
        ObjectivesWidth = F(payload, len, NetProtocol.TuneObjectivesWidth,
                            CardsConfig.BoardDefaults.ObjectivesWidth[b]);
        ElementsScale = F(payload, len, NetProtocol.TuneElementsScale, Defaults.ElementsScale_ByBoard[b]);
        PileScale = F(payload, len, NetProtocol.TunePileScale, Defaults.PileScale_ByBoard[b]);
        ActiveCardScale = F(payload, len, NetProtocol.TuneActiveCardScale,
                            CardsConfig.BoardDefaults.ActiveCardScale[b]);
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

    /// <summary>One-line dump for the change-gated board-built log: a wrong seat is then answerable
    /// from the log without a screenshot, and the FIELD COUNT says at a glance whether the peer's
    /// board is at the shipped layout or at their own.</summary>
    public override string ToString() =>
        Tuned
            ? $"style={Style}, {FieldCount} tuned dial(s): objectives={ObjectivesOffset:F3}" +
              $"(×{ObjectivesScale:F2}), piles={PileOffset:F3}(step {PileSpacing:F3}, ×{PileScale:F2}), " +
              $"initiative={InitiativeOffset:F3}, mesh={AssetOffset:F3}" +
              $"(pitch {AssetPitchDegrees:F1}°, yaw {AssetYawDegrees:F1}°, roll {AssetRollDegrees:F1}°), " +
              $"fan r={FanEffectiveRadius:F3} sweep={FanArcSweepDegrees:F1}° step={FanPerCardStepDegrees:F1}°"
            : $"style={Style}, no tuning record — every dial at the shipped default";
}
