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
/// 10 bytes at 5 Hz; every dial at once costs 235, and even that leaves the packet 336 bytes inside
/// <see cref="PresenceSerializer.MaxSize"/>.
///
/// COMPARING QUANTIZED CODES, NOT FLOATS, is what makes "differs from the default" stable: a config
/// round-trip through a text file can perturb a float in its last bits without moving a pixel, and
/// a float compare would then start emitting a field forever. Two values that land on the same wire
/// code are the same picture, so they compare equal here.
///
/// ─── SAMPLING CADENCE ──────────────────────────────────────────────────────────────────────────
/// <see cref="Sample"/> rebuilds the payload into a persistent buffer; the caller runs it on a
/// change edge (board style change / config edit / debug-menu drag) rather than per packet, and the
/// serializer's write path is then a bounded copy. Rebuilding is cheap enough that the caller may
/// also run it on a slow poll — it allocates nothing and touches ~50 config entries.
/// </summary>
/// <remarks>CLASSIFICATION: WIRE (extension record 28) — LOCAL CONFIG that is neither GLOBAL (each
/// player has their own) nor derivable from anything already synced. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal static class BoardTuningSampler
{
    /// <summary>Buffer size a caller must hand <see cref="Sample"/>: the record's own worst case
    /// (every field present), which is what <see cref="PresenceSerializer.MaxSize"/> budgets for.
    /// 1 count byte + 15 vec3 × 7 + 13 length × 3 + 22 factor × 3 + 6 angle × 3 + 2 count × 2.</summary>
    internal const int MaxPayloadBytes = 1 + 15 * 7 + 13 * 3 + 22 * 3 + 6 * 3 + 2 * 2;

    /// <summary>
    /// Build the record-28 payload for <paramref name="style"/> into <paramref name="payload"/>
    /// (at least <see cref="MaxPayloadBytes"/> long) and return its length — or 0 when every dial
    /// sits at its shipped default, which is the signal to write no record at all.
    ///
    /// <para>Fields are appended in ASCENDING ID ORDER, which is the record's layout contract: it
    /// makes the bytes deterministic for a given tuning (so the change-gated log and the wire tests
    /// have something stable to compare) and lets a reader stop early.</para>
    ///
    /// <para>Every config entry is null-guarded: this can run before <c>CardsConfig.Bind</c> has
    /// completed (a packet may go out during scene load), and the failure direction there is "no
    /// record" ⇒ the receiver keeps the shipped defaults ⇒ exactly the previous build's picture.</para>
    /// </summary>
    internal static int Sample(ControlBoard style, byte[] payload)
    {
        if (payload == null || payload.Length < MaxPayloadBytes)
            return 0;

        int i = 1;                     // byte 0 is the field count, back-filled below
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

        // ---- ANGLE fields (ids 192..197) ------------------------------------------------------
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

        // ---- COUNT fields (ids 224..225) ------------------------------------------------------
        n += Cnt(payload, ref i, NetProtocol.TuneFanMaxHandForCurve,
                 CardsConfig.FanMaxHandForCurve, Defaults.FanMaxHandForCurve);
        n += Cnt(payload, ref i, NetProtocol.TuneFanCurveMinCards,
                 CardsConfig.FanCurveMinCards, Defaults.FanCurveMinCards);

        if (n == 0)
            return 0;                  // every dial at its shipped default — write NO record
        payload[0] = (byte)n;
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

    // ---- ANGLE dials (degrees) ---------------------------------------------------------------
    public float AssetPitchDegrees { get; }
    public float AssetYawDegrees { get; }
    public float AssetRollDegrees { get; }
    public float FanArcSweepDegrees { get; }
    public float FanPerCardStepDegrees { get; }
    public float ItemFanOpenSpinDegrees { get; }

    // ---- COUNT dials -------------------------------------------------------------------------
    public int FanMaxHandForCurve { get; }
    public int FanCurveMinCards { get; }

    /// <summary>Resolve a peer's tuning. <paramref name="payload"/>/<paramref name="len"/> are the
    /// raw record-28 bytes (null / 0 for a peer with nothing tuned, which is the common case and
    /// yields the shipped layout exactly as before this record existed).</summary>
    public RemoteBoardTuning(ControlBoard style, byte[]? payload, int len)
    {
        Style = style;
        int b = (int)ControlBoards.Clamp((int)style);
        bool has = payload != null && len >= NetProtocol.BoardTuneMinRecordBytes;
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

        AssetPitchDegrees = A(payload, len, NetProtocol.TuneAssetPitch,
                              CardsConfig.BoardDefaults.AssetPitchDegrees[b]);
        AssetYawDegrees = A(payload, len, NetProtocol.TuneAssetYaw, Defaults.AssetYawDegrees_ByBoard[b]);
        AssetRollDegrees = A(payload, len, NetProtocol.TuneAssetRoll, Defaults.AssetRollDegrees_ByBoard[b]);
        FanArcSweepDegrees = A(payload, len, NetProtocol.TuneFanArcSweep, Defaults.FanArcSweepDegrees);
        FanPerCardStepDegrees = A(payload, len, NetProtocol.TuneFanPerCardStep,
                                  Defaults.FanPerCardStepDegrees);
        ItemFanOpenSpinDegrees = A(payload, len, NetProtocol.TuneItemFanOpenSpin,
                                   Defaults.ItemFanOpenSpinDegrees);

        FanMaxHandForCurve = C(payload, len, NetProtocol.TuneFanMaxHandForCurve,
                               Defaults.FanMaxHandForCurve);
        FanCurveMinCards = C(payload, len, NetProtocol.TuneFanCurveMinCards, Defaults.FanCurveMinCards);
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
