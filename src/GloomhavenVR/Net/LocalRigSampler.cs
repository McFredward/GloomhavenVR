using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Samples the LOCAL VR rig (owned head camera + the two <see cref="VRHand"/>s) into an
/// <see cref="AvatarState"/> expressed in the shared frame. Read-only w.r.t. the rig — it
/// only reads transforms the Rig/Hands modules own. Returns false when there is nothing
/// worth sending (no rig / no head), so the caller sends nothing and flat peers stay quiet.
/// </summary>
internal static class LocalRigSampler
{
    private static bool s_loggedHeldItem; // one-time confirm the held-item MP parity path fired (#3)
    private static bool s_loggedDoubleHeld; // one-time note when BOTH hands hold a card (one pose on the wire)

    // COMPILE-TIME WIRE GUARD — do not delete. Sibling of NetAvatarDriver.PileKindWireOrderGuard,
    // for the OTHER enum whose member values ride the wire.
    //
    // Cards.ControlBoard's numeric values are transmitted: LocalBoardStyle() below casts the enum
    // to int and hands it to NetProtocol.EncodeBoardStyle, which packs it into the extras packet's
    // trailing-block byte A (bits 5..6); the receiver casts the decoded code straight back to a
    // ControlBoard. Two things must hold and neither is expressible any other way:
    //
    //   1. Oak == BoardStyleDefaultCode (0). "Style bits absent" and "Oak" MUST render the same —
    //      that equality is the whole reason the board style shipped with no presence bit and no
    //      wire-version bump, and it is what makes a pre-board-style peer read as Oak rather than
    //      as garbage.
    //   2. Every board id fits the 2-bit field (<= BoardStyleMaxCode). A fifth board needs a
    //      trailing wire byte, not a silently clamped id — EncodeBoardStyle would clamp Bronze
    //      onto some other board's material on every peer.
    //
    // Violate either and the divisor is 0, so THIS LINE STOPS COMPILING (CS0020 "Division by
    // constant zero"). Known limit, stated honestly: Net/ names no constant for Steel or Bronze,
    // so swapping just those two is NOT caught here — that case is covered by the explicit
    // numbering and the warning at the enum itself.
    private const int ControlBoardWireOrderGuard = 1 / (
        (int)Cards.ControlBoard.Oak == NetProtocol.BoardStyleDefaultCode &&
        Cards.ControlBoards.Count - 1 <= NetProtocol.BoardStyleMaxCode &&
        (int)Cards.ControlBoard.Bronze <= NetProtocol.BoardStyleMaxCode ? 1 : 0);

    public static bool TrySample(IBoardAnchor anchor, bool includeFingers, out AvatarState state)
    {
        state = default;

        // Sender scale: live rig lossyScale (accounts for pinch-zoom) so the receiver can
        // size the floating hands to the same physical size above the shared board.
        Transform? rigRoot = VRRigDriver.RigRoot;
        state.WorldScale = rigRoot != null ? rigRoot.lossyScale.x : 1f;
        if (!(state.WorldScale > 0f))
            state.WorldScale = 1f;

        // Stamp the locally-chosen head mask (read live so changing it updates remotes at once).
        state.MaskId = (byte)LocalMaskId();

        // Stamp the locally-chosen hand style the same way (additive trailing byte on the
        // wire; old peers ignore it and render default Glove hands).
        state.HandStyle = (byte)HandVisuals.LocalStyle();

        Camera? head = VRRigDriver.HeadCamera;
        if (head != null)
        {
            Transform t = head.transform;
            anchor.ToAnchor(t.position, t.rotation, out Vector3 hp, out Quaternion hr);
            state.Head.Position = hp;
            state.Head.Rotation = hr;
            state.HeadValid = true;
        }

        state.HasFingers = includeFingers;
        SampleHand(anchor, VRHands.Left, includeFingers, ref state.Left);
        SampleHand(anchor, VRHands.Right, includeFingers, ref state.Right);

        // Which hand is dominant (mirror flag so remotes place the card fan on the correct side).
        state.DominantRight = LocalDominantRight();

        // Held figure (cosmetic): the FIRST figure the local player physically holds, in the shared
        // frame. A player can hold one mini per hand; the SECOND one rides the extras packet's
        // additive record (NetProtocol.ExtIdSecondFigure) because the rig flag byte has no bit left
        // to announce a second block here. Slot order is grab order, so this slot keeps naming the
        // same mini for its whole hold even when the other hand grabs or releases one.
        state.HasHeldFigure = NetFigures.TrySampleHeldSlot(
            NetFigures.SlotPrimary, out state.HeldFigureActorId, out Vector3 fp, out Quaternion fr, out _);
        if (state.HasHeldFigure)
        {
            anchor.ToAnchor(fp, fr, out Vector3 ap, out Quaternion ar);
            state.HeldFigurePose.Position = ap;
            state.HeldFigurePose.Rotation = ar;
        }

        // Held card (cosmetic, additive FlagHeldCard field): a single VRCard grip-held in
        // either hand (plucked from the fan or a pile viewer). Pose only — the card's
        // identity NEVER rides the wire (peers render a back slab; anti-cheat stance of
        // the remote fan). The open fan itself is covered by the extras packet's count.
        state.HasHeldCard = TrySampleHeldCard(out Vector3 cp, out Quaternion cr);
        if (state.HasHeldCard)
        {
            anchor.ToAnchor(cp, cr, out Vector3 acp, out Quaternion acr);
            state.HeldCardPose.Position = acp;
            state.HeldCardPose.Rotation = acr;
        }

        // Nothing to say if we have neither a head nor a tracked hand.
        return state.HeadValid || state.Left.Tracked || state.Right.Tracked;
    }

    /// <summary>
    /// Which hand is the local player's DOMINANT hand: the RIGHT hand unless the tracked
    /// non-dominant hand is the Right hand. Defaults true (right-dominant) when the non-dominant
    /// hand is unknown (controller absent / hot reload). Exposed so the driver can stamp the same
    /// value onto the extras packet.
    ///
    /// HANDEDNESS FIX (MP test: "peer fan renders in the wrong hand"). <see cref="NonDominantHold.Hand"/>
    /// IS the non-dominant hand (the opposite of <c>VRHands.Primary</c>, which is where the local
    /// <see cref="Cards.CardFan"/> opens — CardsDriver's gate hand). So "dominant is Right" is
    /// "the non-dominant hand is NOT the Right one". The old predicate compared against
    /// <c>HandSide.Left</c> — inverted for every tracked player (a right-dominant default player
    /// transmitted DominantRight=false), which put the remote hand fan, the ghost-hand fallback and
    /// the card-flight anchor on the DOMINANT hand of every peer's proxy. The receiver mapping
    /// (<see cref="RemoteAvatar.NonDominantHandHolder"/> = DominantRight ? Left : Right) was always
    /// correct — the sender was the inverted side.
    /// </summary>
    public static bool LocalDominantRight() => NonDominantHold.Hand?.Side != HandSide.Right;

    /// <summary>The locally-chosen mask id, clamped to [0, MaskCount-1]. Guarded so an unbound
    /// config (net module never inited) falls back to mask 0 rather than throwing.</summary>
    public static int LocalMaskId() =>
        NetModule.MaskId != null ? Mathf.Clamp(NetModule.MaskId.Value, 0, HeadMaskLibrary.MaskCount - 1) : 0;

    /// <summary>
    /// The locally-chosen head-mask SIZE multiplier, clamped to the transmittable window. Guarded
    /// exactly like <see cref="LocalMaskId"/> so an unbound config (net module never inited, hot
    /// reload) falls back to the authored size rather than throwing or collapsing the head. Read
    /// LIVE by the mirror and by the extras sender, so a stepper edit resizes the mask at once.
    /// </summary>
    public static float LocalMaskSize()
    {
        if (NetModule.MaskSize == null)
            return 1f;
        float v = NetModule.MaskSize.Value;
        if (float.IsNaN(v) || float.IsInfinity(v))
            return 1f;
        return Mathf.Clamp(v, NetProtocol.MaskSizeMin, NetProtocol.MaskSizeMax);
    }

    /// <summary>
    /// The locally-chosen CONTROL-BOARD style as its wire code (0 Oak / 1 Steel / 2 Bronze).
    /// Guarded exactly like <see cref="LocalMaskId"/>: an unbound [Cards] config (Cards module
    /// never inited, hot reload) reads as the default board rather than throwing inside the extras
    /// sender. Read LIVE, so switching the board in the VR settings goes out on the next packet.
    /// </summary>
    public static byte LocalBoardStyle()
    {
        Cards.ControlBoard board = Cards.CardsConfig.Board != null
            ? Cards.ControlBoards.Clamp((int)Cards.CardsConfig.Board.Value)
            : Cards.ControlBoard.Oak;
        return NetProtocol.EncodeBoardStyle((int)board);
    }

    /// <summary>The world pose of the single card the local player grip-holds, if any (left
    /// hand wins when both hold one — matches the mirror's slab order). False when no hand
    /// holds a <see cref="Cards.VRCard"/> ability card or an <see cref="Cards.ItemsPile.ItemChip"/>.
    ///
    /// TWO SIMULTANEOUS HELD CARDS (both-hands ruling 2026-08-04: either hand can now take a
    /// card, so e.g. the gate hand can hold a browse card while the dominant hand plucks
    /// another): the wire's <c>FlagHeldCard</c> field carries exactly ONE pose by design and the
    /// wire format must not change, so the LEFT hand's card is the deterministic pick and the
    /// right hand's card is simply invisible to peers for the overlap (they see that hand's
    /// finger curls close on nothing — cosmetic only, no game state rides either card). Logged
    /// once per session below so a hardware log can attribute the "missing" second slab.</summary>
    private static bool TrySampleHeldCard(out Vector3 pos, out Quaternion rot)
    {
        bool left = TryHeldCard(VRHands.Left, out pos, out rot);
        if (left && !s_loggedDoubleHeld && HoldsCardShape(VRHands.Right))
        {
            s_loggedDoubleHeld = true;
            Core.VRLog.Info("Net", "MP note: BOTH hands hold a card — the wire carries one held-card " +
                                   "pose (FlagHeldCard), so peers see the LEFT hand's card only; the " +
                                   "right hand's card is not represented for the overlap (cosmetic).");
        }
        return left || TryHeldCard(VRHands.Right, out pos, out rot);
    }

    /// <summary>True when <paramref name="hand"/> holds either wire-visible card shape (ability
    /// card or item chip) — the same pattern <see cref="TryHeldCard"/> matches, sans pose.</summary>
    private static bool HoldsCardShape(VRHand? hand)
        => hand != null && hand.Grabber != null
           && (hand.Grabber.Held is Cards.ItemsPile.ItemChip || hand.Grabber.Held is Cards.VRCard);

    private static bool TryHeldCard(VRHand? hand, out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        if (hand == null || hand.Grabber == null)
            return false;
        // Cosmetic MP parity (items rework #3b): a grip-held ABILITY card (VRCard) OR a grip-held
        // ITEM chip both show as a card-BACK slab in the remote avatar's hand — POSE ONLY, no identity
        // (same anti-cheat stance as the ability held card; the item's own use/effect already syncs
        // authoritatively through UseItemService). Before this, a held item was NOT a VRCard, so peers
        // saw the grabbing hand move with an empty hand while a held ability card showed its back.
        // Both grab routes reach this the same way: pinch-grab (ProximityGrabber.BeginGrab) and the
        // board-laser pluck (ItemChip.OnPoke → ProximityGrabber.ForceGrab) BOTH set Grabber.Held to
        // the chip itself, so one pattern match covers every way an item card gets into a hand.
        Cards.ItemsPile.ItemChip? chip = hand.Grabber.Held as Cards.ItemsPile.ItemChip;
        Transform? t = chip != null
            ? chip.transform
            : hand.Grabber.Held is Cards.VRCard card && card != null ? card.transform : null;
        if (t == null)
            return false;
        if (chip != null && !s_loggedHeldItem)
        {
            s_loggedHeldItem = true;
            Core.VRLog.Info("Net", $"MP parity (#3): held ITEM chip sampled onto the wire ({hand.Side} hand, pose " +
                                   "only, back slab on peers) — matches the held ability-card representation.");
        }
        pos = t.position;
        rot = t.rotation;
        return true;
    }

    private static void SampleHand(IBoardAnchor anchor, VRHand? hand, bool includeFingers, ref HandStateSample sample)
    {
        if (hand == null || !hand.IsTracked || hand.Rig == null || hand.Rig.Root == null)
        {
            sample.Tracked = false;
            return;
        }

        Transform t = hand.Rig.Root;
        anchor.ToAnchor(t.position, t.rotation, out Vector3 p, out Quaternion r);
        sample.Tracked = true;
        sample.Pose.Position = p;
        sample.Pose.Rotation = r;

        if (includeFingers)
        {
            sample.Curl0 = hand.GetCurl(Finger.Thumb);
            sample.Curl1 = hand.GetCurl(Finger.Index);
            sample.Curl2 = hand.GetCurl(Finger.Middle);
            sample.Curl3 = hand.GetCurl(Finger.Ring);
            sample.Curl4 = hand.GetCurl(Finger.Pinky);
        }
    }
}
