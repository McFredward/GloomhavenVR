using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
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

    /// <summary>The world pose of the FIRST card the local player grip-holds, if any (left
    /// hand wins when both hold one — matches the mirror's slab order). False when no hand
    /// holds a <see cref="Cards.VRCard"/> ability card or an <see cref="Cards.ItemsPile.ItemChip"/>.
    ///
    /// TWO SIMULTANEOUS HELD CARDS (both-hands ruling: either hand can take a card, so e.g. the
    /// gate hand can hold a browse card while the dominant hand plucks another): the rig packet's
    /// <c>FlagHeldCard</c> field carries exactly ONE pose and its layout must not change, so this
    /// sampler keeps its deterministic left-first preference unchanged — and the OTHER hand's
    /// card rides the extras packet as extension record
    /// <see cref="NetProtocol.ExtIdSecondHeldCard"/> (<see cref="TrySampleSecondHeldCard"/>), so
    /// peers now see BOTH cards (user ruling 2026-08-04: "Wenn Karten in beiden Haenden sind,
    /// soll das auch synchronisiert werden"). The split is deterministic by construction: this
    /// slot names the LEFT hand's card whenever two are held, record 10 names the RIGHT's.</summary>
    private static bool TrySampleHeldCard(out Vector3 pos, out Quaternion rot)
    {
        return TryHeldCard(VRHands.Left, out pos, out rot)
               || TryHeldCard(VRHands.Right, out pos, out rot);
    }

    /// <summary>
    /// The world pose of the SECOND held card — the one the rig packet's <c>FlagHeldCard</c> slot
    /// does NOT carry. True ONLY while BOTH hands physically hold a wire-visible card shape: the
    /// rig slot then carries the LEFT hand's card (<see cref="TrySampleHeldCard"/>'s unchanged
    /// preference), so the second card is by definition the RIGHT hand's. While only one hand —
    /// either one — holds a card, the rig slot already carries it and this returns false, which
    /// keeps the extras record absent and the idle/one-card packet byte-identical to build 49.
    /// Read by <see cref="NetAvatarDriver"/>'s extras sender (extension record
    /// <see cref="NetProtocol.ExtIdSecondHeldCard"/>). Pose only, no identity, ever.
    /// </summary>
    public static bool TrySampleSecondHeldCard(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        return HoldsCardShape(VRHands.Left) && TryHeldCard(VRHands.Right, out pos, out rot);
    }

    /// <summary>True when <paramref name="hand"/> holds either wire-visible card shape (ability
    /// card or item chip) — the same pattern <see cref="TryHeldCard"/> matches, sans pose.
    ///
    /// <para>THE LIVE-TRANSFORM TEST IS PART OF THE PATTERN, not extra caution. Without it this
    /// predicate is very slightly WIDER than <see cref="TryHeldCard"/>, which also requires a
    /// transform — and the two are used to answer the same question about the same hand in two
    /// places. A card destroyed under the hand mid-hold (pooled away, scene torn down) would pass
    /// here and fail there, and every caller that pairs them would then disagree about which slot
    /// holds which card: <see cref="TrySampleSecondHeldCard"/> would claim a second card while the
    /// first slot had already fallen through to the right hand's, sending ONE card twice. It has
    /// always been a narrow window and it has never been reported; it is closed here because
    /// <see cref="SampleHeldCardGripMask"/> makes the pairing load-bearing rather than incidental
    /// — its bits name POSE SLOTS, and a slot rule that disagrees with the slots is a bit
    /// describing the wrong card.</para></summary>
    private static bool HoldsCardShape(VRHand? hand)
        => hand != null && hand.Grabber != null
           && (hand.Grabber.Held is Cards.ItemsPile.ItemChip chip && chip != null
               || hand.Grabber.Held is Cards.VRCard card && card != null);

    /// <summary>
    /// WHICH HELD-CARD POSE SLOTS ARE RIGID — extension record
    /// <see cref="NetProtocol.ExtIdHeldCardGrip"/>'s whole payload, as
    /// <see cref="NetProtocol.HeldCardGripFirstBit"/> |
    /// <see cref="NetProtocol.HeldCardGripSecondBit"/>. 0 = both held cards billboard at this
    /// player's head, which is every packet before ModBuild 317 and every packet from a player who
    /// does not use the gesture; the writer then omits the record entirely.
    ///
    /// <para>THE BITS NAME SLOTS, NOT HANDS, and that is the only thing this method has to get
    /// right. The two poses are assigned by a left-first rule that lives in two other methods
    /// (<see cref="TrySampleHeldCard"/> takes the LEFT hand's card when the left hand holds one,
    /// otherwise the right's; <see cref="TrySampleSecondHeldCard"/> takes the right's, and only
    /// while BOTH hold). Re-deriving that rule here from the same predicate is what keeps a bit
    /// from describing the other hand's card — the classic "a ratio with two populations" defect,
    /// where the numerator and the denominator were counted over different sets.</para>
    /// </summary>
    public static byte SampleHeldCardGripMask()
    {
        bool left = HoldsCardShape(VRHands.Left);
        bool right = HoldsCardShape(VRHands.Right);
        byte mask = 0;
        // Slot 1 = the LEFT hand's card when the left hand holds one, otherwise the right's.
        // SET FOR THE WHOLE JOURNEY, not just at the ends: a bit means "do not billboard this one",
        // and for every frame of the grasp the card is somewhere between the two poses, which is
        // somewhere the billboard rule cannot predict. Reading the BLEND rather than the mode is
        // what makes a peer's copy travel with the owner's instead of jumping at each end of it.
        bool leftGrasp = Cards.HeldCardGrip.Blend(HandSide.Left) > 0f;
        bool rightGrasp = Cards.HeldCardGrip.Blend(HandSide.Right) > 0f;
        bool firstIsLeft = left;
        if (firstIsLeft ? leftGrasp : right && rightGrasp)
            mask |= NetProtocol.HeldCardGripFirstBit;
        // Slot 2 exists only while BOTH hands hold one, and is then the RIGHT hand's by definition.
        if (left && right && rightGrasp)
            mask |= NetProtocol.HeldCardGripSecondBit;
        return mask;
    }

    /// <summary>
    /// WHICH PILE THE LOCAL FAN IS DRAWN FROM — extension record
    /// <see cref="NetProtocol.ExtIdFanSource"/>'s whole payload, as one list id. Answers
    /// <see cref="NetProtocol.HeldFaceListNone"/> for an ordinary hand fan and for no fan at all,
    /// and the writer then omits the record entirely.
    ///
    /// <para>THE HAND IS DELIBERATELY UNSAYABLE. It is what every receiver already resolves and
    /// what every sender predating this record means, so "my fan is my hand" and "no record" have
    /// to be ONE state — two spellings of one picture is how a mirror latches, which is the lesson
    /// record 41's own vectors are written around.</para>
    ///
    /// <para>IT IS READ, NOT RE-DERIVED. <c>Cards.CardsDriver.FanSourcePile</c> is assigned from the
    /// very widgets that became the fan, inside the rebuild that built it, so this cannot describe a
    /// fan that is no longer up or a pile the fan was never filled from. Re-deriving it here out of
    /// the game's <c>selectableCardType</c> would be a second expression for one fact — precisely
    /// the defect <see cref="NameHeldCard"/>'s own block below is written about.</para>
    ///
    /// <para>ROUND, ACTIVE and the rest of <c>CardPileType</c> are not expressible and fall through
    /// to "the hand": a fan is only ever the hand, the discard pile or the lost pile, and a value
    /// this record cannot say is better absent than guessed at.</para>
    /// </summary>
    public static byte SampleFanSource() => Cards.CardsDriver.FanSourcePile switch
    {
        CardPileType.Discarded => NetProtocol.HeldFaceListDiscard,
        CardPileType.Lost or CardPileType.Permalost => NetProtocol.HeldFaceListBurnt,
        _ => NetProtocol.HeldFaceListNone,
    };

    /// <summary>
    /// WHICH CARD EACH HELD-CARD POSE SLOT IS SHOWING — extension record
    /// <see cref="NetProtocol.ExtIdHeldCardFace"/>'s whole payload, as a <c>[code][list length]</c>
    /// pair per slot. A code of 0 means "this slot names nothing", and when BOTH are 0 the writer
    /// omits the record entirely, so every packet of every player who is not holding a card is
    /// byte-identical to ModBuild 351's.
    ///
    /// <para>THE SLOTS ARE FILLED BY THE SAME LEFT-FIRST RULE THE POSES ARE, re-derived from the
    /// same <see cref="HoldsCardShape"/> predicate for the same reason
    /// <see cref="SampleHeldCardGripMask"/> re-derives it: an entry that describes the other hand's
    /// card is a FRONT drawn on the wrong card, which is the one failure this record must not have.
    /// Slot 1 is the LEFT hand's card whenever the left hand holds one, otherwise the right's;
    /// slot 2 exists only while BOTH hold, and is then the right's by definition.</para>
    ///
    /// <para>WHAT IS NAMED IS A POSITION, NEVER A CARD. Each code carries a SOURCE-LIST id and an
    /// index into that list, and every one of the four lists is host-replicated onto the receiver
    /// already — it is the same list the peer's mirrored fan resolves its own fronts from. The
    /// LENGTH byte beside it is what makes the position safe: the receiver refuses the front unless
    /// its own copy of the list is exactly this long, so a client whose model lags behind the owner
    /// (the ModBuild 351 burnt-card defect) draws a back rather than a shifted face.</para>
    ///
    /// <para>THE ACTOR IS THE ONE THE BOARD IS PRESENTING (<c>ItemsPile.Current.OwnerActor</c>) and
    /// that is not a convenience: it is the SAME character the receiver resolves through
    /// <c>RemoteBoardFocus.DisplayedActor</c>, and the card in this player's hand came out of that
    /// character's own fan. With no item pile there is no control board and nothing to pluck from,
    /// so the honest answer is "names nothing" rather than a guess at whose list to index.</para>
    /// </summary>
    public static void SampleHeldCardFaces(out byte code0, out byte count0,
                                           out byte code1, out byte count1)
    {
        code0 = 0;
        count0 = 0;
        code1 = 0;
        count1 = 0;
        bool left = HoldsCardShape(VRHands.Left);
        bool right = HoldsCardShape(VRHands.Right);
        if (!left && !right)
            return;
        CPlayerActor? actor = Cards.ItemsPile.Current?.OwnerActor;
        // ─── THE ACTOR CAME OUT OF THE ITEM PILE, AND THE ITEM PILE IS USUALLY NOT THERE ─────────
        // 2026-09-05 report item 2c, "Innerhalb des Szenarios hat das wieder so gut wie garnicht
        // funktioniert", and this line is the whole of it. Cards.ItemsPile.Current is PUBLISHED BY
        // THE ITEM ARC AND ONLY WHILE IT IS OPEN — ItemsPile.Open assigns it, ItemsPile.Close nulls
        // it, and its own doc comment says so ("Current is null the moment the arc goes"). A player
        // in a scenario raises their item fan for a few seconds a session; the rest of the time this
        // read answers null, the map-room branch below was taken, MapRoomHand had nothing to say
        // because no map room is standing, and the record named NOTHING. So a card plucked out of a
        // peer's HAND inside a scenario could never get a front — on any client, in any phase, for
        // the whole life of the feature.
        //
        // THE EVIDENCE IS THE SILENCE, and it is only readable once the line's cadence is known:
        // "Held-card face SENT" is change-gated on the CODE, so a code that is always 0 never changes and
        // never prints. Both 100 MB logs of the 2026-09-05 session contain four such lines each and
        // not one of them names the hand: 'map-room loadout' and 'items', never 'hand fan'. Two
        // players played a whole scenario picking cards up and putting them down.
        //
        // THE CORRECT SOURCE IS THE CHARACTER WHOSE HAND THIS CLIENT IS PRESENTING, which is what
        // the RECEIVER already resolves the seat against: RemoteBoardFocus.DisplayedActor follows
        // extension record 22, and record 22 carries Board.CharacterFocus.PresentedActorId. Asking
        // for the same fact on this side makes the two ends of the index name the same list by
        // construction instead of by coincidence. Guarded on RevealGate.InScenario so a
        // PresentedActor left standing from a finished scenario can never steal the map room's
        // branch below — off-scenario the map loadout is the only list there is.
        if (actor == null && RevealGate.InScenario)
            actor = Board.CharacterFocus.PresentedActor;
        // NO ACTOR IS NOT "NAMES NOTHING" ANY MORE — IT IS THE MAP ROOM (report item 5a). This used
        // to return here, and the consequence was that in the map room the record was omitted
        // outright and a card in a peer's hand could only ever be a back, while the fan beside it
        // showed its fronts. The premise was sound and the conclusion did not follow: there really
        // is no CPlayerActor and no ItemsPile in the map room (CMapCharacter.GetActor() reads
        // ScenarioManager.Scenario, which is null there, and THROWS), but the map room has its own
        // unit of identity and its own list — the loadout, resolved from the replicated
        // CMapCharacter and already mirrored on every peer. So the absence of an actor selects a
        // DIFFERENT SOURCE LIST rather than ending the sample.
        if (actor == null)
        {
            NameHeldMapCard(left ? VRHands.Left : VRHands.Right, out code0, out count0);
            if (left && right)
                NameHeldMapCard(VRHands.Right, out code1, out count1);
            return;
        }
        // Slot 1 = the LEFT hand's card when the left hand holds one, otherwise the right's —
        // TrySampleHeldCard's unchanged preference, restated here rather than shared, because the
        // two answers must agree about the SLOT and nothing else about them is common.
        NameHeldCard(actor, left ? VRHands.Left : VRHands.Right, out code0, out count0);
        // Slot 2 exists only while BOTH hands hold one, and is then the RIGHT hand's.
        if (left && right)
            NameHeldCard(actor, VRHands.Right, out code1, out count1);
    }

    /// <summary>
    /// THE MAP-ROOM HALF of <see cref="SampleHeldCardFaces"/>: seat the card <paramref name="hand"/>
    /// is holding in the local map LOADOUT (<c>NetProtocol.HeldFaceListMapLoadout</c>), or leave the
    /// code 0.
    ///
    /// <para>The seat is resolved by the map room itself
    /// (<c>WorldUI.MapRoom.MapRoomHand.TryNameLocalLoadoutSeat</c>) rather than re-derived here, for
    /// the reason every other list in this record is: the index is only a name for a card while both
    /// machines build the list with the same expression, and the receiver builds it with
    /// <c>MapRoomHand.ResolveLoadout</c>. WHICH character it is a loadout OF does not have to be
    /// guessed — record 20 already carries the sender's map character key and the peer resolves the
    /// list through it (<c>MapRoomHand.TryResolvePeerLoadout</c>).</para>
    ///
    /// <para>Everything that cannot be answered exactly — the room is stood down, the card is not one
    /// of ours, its model is not in the loadout, an index past what five bits can carry — leaves the
    /// code 0 and draws a BACK, which is the picture this surface had before the record existed.</para>
    /// </summary>
    private static void NameHeldMapCard(VRHand? hand, out byte code, out byte count)
    {
        code = 0;
        count = 0;
        object? held = hand != null && hand.Grabber != null ? hand.Grabber.Held : null;
        if (held is not Cards.VRCard card || card == null)
            return;
        if (!WorldUI.MapRoom.MapRoomHand.TryNameLocalLoadoutSeat(card, out int seat, out int length))
            return;
        count = (byte)Mathf.Clamp(length, 0, 255);
        code = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListMapLoadout, seat);
        if (code == 0)
            count = 0; // could not be seated in five bits — say nothing rather than half a thing
    }

    /// <summary>
    /// Seat the card <paramref name="hand"/> is holding in one of the four host-replicated lists
    /// the receiver can index, or leave the code 0 ("names nothing") when it cannot be seated.
    ///
    /// <para>EVERY LIST IS BUILT BY THE EXACT EXPRESSION THE RECEIVER USES, and that is the whole
    /// correctness argument: the ability HAND list is <c>cardsUI</c> filtered to
    /// <c>CardPileType.Hand</c> with a non-null <c>fullAbilityCard</c> —
    /// <c>RemoteHandFan.ResolveHandFronts</c>'s filter, character for character; the two pile lists
    /// are <c>Cards.CardsGameApi.GetPileWidgets</c>, the call <c>RemotePileFronts.Resolve</c> makes; the
    /// item list is <c>Inventory.AllItems</c> walked RAW. A filter that differs by one term names a
    /// different seat on each machine, which is why they are not re-expressed anywhere.</para>
    ///
    /// <para>Read-only throughout — every game object here is inspected, never written — and the
    /// whole body is inside the caller's per-send path rather than a per-frame one.</para>
    /// </summary>
    private static void NameHeldCard(CPlayerActor actor, VRHand? hand,
                                     out byte code, out byte count)
    {
        code = 0;
        count = 0;
        object? held = hand != null && hand.Grabber != null ? hand.Grabber.Held : null;
        if (held == null)
            return;

        if (held is Cards.ItemsPile.ItemChip chip && chip != null)
        {
            CItem? item = chip.Item;
            CInventory? inv = actor.Inventory;
            System.Collections.Generic.List<CItem>? all = inv != null ? inv.AllItems : null;
            if (item == null || all == null)
                return;
            // RAW index, nulls included — the same index space the usable mask (record 35) uses,
            // and the one the receiver re-walks. The fan builders skip nulls; this must not, or the
            // two disagree about which chip position bit i / index i means.
            int at = all.IndexOf(item);
            if (at < 0)
                return;
            count = (byte)Mathf.Clamp(all.Count, 0, 255);
            code = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListItems, at);
            return;
        }

        if (held is not Cards.VRCard card || card == null)
            return;
        NameCard(actor, card, out code, out count);
    }

    /// <summary>Shared positional address for a real VR card, whether held or emitting native FX.</summary>
    internal static void NameCard(CPlayerActor actor, Cards.VRCard card, out byte code, out byte count)
    {
        code = count = 0;
        AbilityCardUI? widget = card.GameCard;
        if (widget == null)
            return;
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? gameHand = manager != null ? manager.GetHand(actor) : null;
        if (gameHand == null)
            return;

        // A card on loan from a pile browse names THAT pile; anything else is a card out of the
        // owner's own hand fan. PileOrigin is the loan marker CardsDriver.OnCardReleased already
        // routes the card home by, so it is the same fact, read rather than re-derived.
        Cards.PileKind? origin = card.PileOrigin;
        if (origin == Cards.PileKind.Discard || origin == Cards.PileKind.Burnt)
        {
            // THE PILE ARC'S INDEX SPACE, and the SAME CALL the receiver resolves with
            // (Net.RemoteHeldCardFace.Resolve). This used to index and count the RAW getter while
            // the receiver narrowed it by CardsGameApi.PileWidgetIsArcMember first — two
            // expressions for one wire index space, which is precisely what the paragraph below
            // about the hand fan says must never happen, on the pile arm instead of the hand arm.
            // It was inert (AppendPileWidgets cannot currently append a non-member) and it failed
            // SAFE when it was not (a dropped entry moves the count too, so the length belt refuses
            // and draws a back) — neither is a reason to keep two copies of an index space.
            Cards.CardsGameApi.GetPileArcWidgets(gameHand, origin == Cards.PileKind.Burnt,
                                                 s_heldFaceBuf);
            int at = s_heldFaceBuf.IndexOf(widget);
            count = (byte)Mathf.Clamp(s_heldFaceBuf.Count, 0, 255);
            s_heldFaceBuf.Clear();
            if (at < 0)
            {
                count = 0;
                return;
            }
            code = NetProtocol.EncodeHeldFace(
                origin == Cards.PileKind.Burnt
                    ? NetProtocol.HeldFaceListBurnt
                    : NetProtocol.HeldFaceListDiscard, at);
            return;
        }
        // PileKind.Items on a VRCard is not expressible — an item card is an ItemChip, handled
        // above — so it deliberately falls through to "names nothing" rather than being seated in
        // a list it is not in.
        if (origin != null)
            return;

        // Address active cards by authoritative membership, not the widget's delayed CardType.
        // MB486 sometimes sent code 0 immediately after activation and only recovered after a
        // later widget refresh. Both endpoints walk this same model list; this is an address,
        // while RevealGate separately decides whether the current phase permits its face.
        CCharacterClass? cc = actor.CharacterClass;
        System.Collections.Generic.List<CBaseCard>? activeCards = cc != null ? cc.ActivatedCards : null;
        int seatActive = -1;
        int nActive = 0;
        if (activeCards != null)
        {
            for (int i = 0; i < activeCards.Count; i++)
            {
                if (activeCards[i] is not CAbilityCard ability || ability == null)
                    continue;
                if (widget.AbilityCard != null && ability.CardInstanceID == widget.CardInstanceID)
                    seatActive = nActive;
                nActive++;
            }
        }
        if (seatActive >= 0)
        {
            count = (byte)Mathf.Clamp(nActive, 0, 255);
            code = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListActive, seatActive);
            if (code == 0) count = 0;
            return;
        }

        // ─── A CARD OUT OF A PICK FAN IS NOT A HAND CARD, AND HAS NO PileOrigin EITHER ──────────
        // 2026-09-06 report item 7, the HELD half of it: "Aktuell ist der Faecher als auch die
        // Karte in der Hand des Spielers wieder nur die Rueckseite." While the game has the owner
        // stepping through a modal pick it re-Shows their hand over another PILE, so the card they
        // pluck out of the arc is a DISCARD or LOST widget that was never loaned from a pile browse
        // — PileOrigin is null (nothing lent it) and CardsGameApi.HandFanMember is false (its
        // CardType is not Hand). The loop below therefore found no seat, this method returned code
        // 0, and the receiver printed the only thing it could: "the sender named no seat for this
        // card (record 36 code 0) — nothing this receiver can do". That line stands 5 times in the
        // host's census during the co-player's long rest and is the whole evidence for this branch.
        //
        // THE PILE ARM ALREADY EXISTS AND IS ALREADY CORRECT — the receiver's Discard/Burnt branch
        // (Net.RemoteHeldCardFace.Resolve) walks GetPileArcWidgets against the presented character
        // and length-checks it, exactly as it does for a browse loan. All that was missing is that
        // this sampler only reached it through PileOrigin. Reading the WIDGET'S OWN CardType covers
        // both ways a card can be in a pile and needs no new wire field: record 36 has carried a
        // list id since it shipped.
        //
        // It falls through to the hand arm for every other CardType, which is the picture this
        // sampler drew before — a card whose pile this record cannot name stays a BACK rather than
        // being seated in a list it is not in.
        CardPileType heldPile = widget.CardType;
        if (!Cards.CardsGameApi.HandFanMember(widget, actor)
            && (heldPile == CardPileType.Discarded || heldPile == CardPileType.Lost
                || heldPile == CardPileType.Permalost))
        {
            bool burnt = heldPile != CardPileType.Discarded;
            Cards.CardsGameApi.GetPileArcWidgets(gameHand, burnt, s_heldFaceBuf);
            int atPile = s_heldFaceBuf.IndexOf(widget);
            count = (byte)Mathf.Clamp(s_heldFaceBuf.Count, 0, 255);
            s_heldFaceBuf.Clear();
            if (atPile < 0)
            {
                count = 0;
                return;
            }
            code = NetProtocol.EncodeHeldFace(
                burnt ? NetProtocol.HeldFaceListBurnt : NetProtocol.HeldFaceListDiscard, atPile);
            return;
        }

        System.Collections.Generic.List<AbilityCardUI>? all2 = gameHand.cardsUI;
        if (all2 == null)
            return;
        // THE HAND FAN'S MEMBERSHIP TEST — the SAME EXPRESSION the owner's own arc and a peer's
        // mirrored arc are built from (Cards.CardsGameApi.HandFanMember), not a third copy of its
        // terms. This used to spell out `CardType == Hand && fullAbilityCard != null`, which agreed
        // with the receiver of the day and with NEITHER the owner's fan nor the receiver after it
        // was corrected — so the seat this record named and the seat the peer counted to were two
        // different index spaces the moment a long-rest placeholder sat in the hand.
        int seat = -1;
        int n = 0;
        for (int i = 0; i < all2.Count; i++)
        {
            AbilityCardUI c = all2[i];
            if (!Cards.CardsGameApi.HandFanMember(c, actor))
                continue;
            if (ReferenceEquals(c, widget))
                seat = n;
            n++;
        }
        if (seat < 0)
            return;
        count = (byte)Mathf.Clamp(n, 0, 255);
        code = NetProtocol.EncodeHeldFace(NetProtocol.HeldFaceListHand, seat);
    }

    /// <summary>Scratch for <see cref="SampleFanArcOrder"/>'s derived-list walk. Static and
    /// reused: this runs at the extras rate.</summary>
    private static readonly System.Collections.Generic.List<AbilityCardUI> s_fanOrderDerived = new(16);

    /// <summary>Change key for <see cref="ReportFanArcOrder"/> — the verdict, the two lengths and
    /// the arc's own fingerprint, so a standing arc costs ONE line and a reorder costs one more.
    /// </summary>
    private static long s_fanOrderSentKey = long.MinValue;

    /// <summary>
    /// The hand whose <c>cardsUI</c> LISTS <paramref name="widget"/> — the index space the arc was
    /// actually built out of, asked of the arc itself rather than of a parallel lookup.
    ///
    /// <para>THIS EXISTS BECAUSE THE PARALLEL LOOKUP WAS WRONG, and it is the sender half of the
    /// 2026-09-07 report item 2. <see cref="SampleFanArcOrder"/> used to derive its index space
    /// from <c>CardsGameApi.ActiveHand()</c> alone, which is <c>CardsHandManager.CurrentHand</c> —
    /// "whatever character tab the game last happened to switch to", in that method's own words,
    /// "which is exactly why it can be STALE". The arc, however, is built by
    /// <c>CardsDriver.Rebuild</c> out of <c>Board.CharacterFocus.ResolveHand(CurrentHand())</c>,
    /// and <c>CardsDriver.CurrentHand</c> is <c>DecidingHand() ?? ActiveHand()</c> filtered by
    /// <c>IsLocalHand</c>. The two answers differ for a whole named list of flows — an
    /// action-selection hand-off inside one phase, the boots' ± step (where the game deliberately
    /// never switches hands), a take-damage panel, an item surrender — and in every one of them the
    /// sampler's derived walk held a DIFFERENT character's card widgets. <c>IndexOf</c> then
    /// returned -1 for the first arc card, the sampler refused, no record went out, and the watcher
    /// drew the fan in the game's order while its owner looked at their own. Silently, because the
    /// sampler had no log line at all.</para>
    ///
    /// <para>Bounded by the party (four hands), allocation-free, and it cannot drift from the arc by
    /// construction: it is the arc that names the hand.</para>
    /// </summary>
    private static CardsHandUI? HandListingArc(AbilityCardUI? widget)
    {
        if (widget == null)
            return null;
        CardsHandManager manager = CardsHandManager.Instance;
        System.Collections.Generic.List<CardsHandUI>? hands =
            manager != null ? manager.CardHandsUI : null;
        if (hands == null)
            return null;
        for (int i = 0; i < hands.Count; i++)
        {
            // ONE EXPRESSION FOR "IS THIS CARD YOURS", shared with CardsDriver.OnCardReleased's
            // wrong-hand belt — the same question must never be spelled twice.
            if (Cards.CardsGameApi.HandOwnsWidget(hands[i], widget))
                return hands[i];
        }
        return null;
    }

    /// <summary>
    /// HARDWARE EVIDENCE, SENDER EDGE, for report item 2 of 2026-09-07. Grep token:
    /// FAN ARC ORDER SENT.
    ///
    /// <para>WHY IT HAD TO EXIST, and it is the whole reason that round could not be closed from
    /// two logs. <see cref="SampleFanArcOrder"/> had a dozen ways to return false and not one line
    /// of output, so every one of them arrived on the far end as the single word
    /// <c>none stated</c> — which the receiver's own line then reads out as the BENIGN case ("they
    /// can send one and chose not to, because their arc already matches"). "The sender could not
    /// describe its arc" and "the sender had nothing to say" were the same reading, and they have
    /// opposite meanings.</para>
    ///
    /// <para>READ IT LIKE THIS, and it is designed to be compared with ONE grep and no second
    /// machine. <c>fp</c> is the same order-sensitive FNV fold over <c>CardInstanceID</c> that
    /// <c>CardsDriver</c>'s <c>FAN ORDER MIRROR</c> and <c>Net.RemoteHandFan</c>'s
    /// <c>MIRRORED ARC ORDER</c> print — the id is host-replicated, so THE SAME ARC IN THE SAME
    /// ORDER PRINTS THE SAME WORD ON EVERY MACHINE IN THE SESSION.</para>
    /// <list type="bullet">
    ///   <item><c>SENT</c> with <c>fp=X</c> — record 44 is on this packet and states this arc. The
    ///     watcher's <c>MIRRORED ARC ORDER</c> for this player must print <c>fp=X</c> too; if it
    ///     prints a different word, the record arrived and the divergence is on the RECEIVER.</item>
    ///   <item><c>WITHHELD</c> with <c>why=</c> — no record on this packet, and the reason names
    ///     which exit fired. <c>arc is not our own character's hand</c> is the SENDER defect, and
    ///     since 2026-09-07 it is one of THREE strings rather than one: the other two, <c>the hand
    ///     listing this arc has no PlayerActor</c> and <c>NetPlayerActors.ActorFor could not name
    ///     OUR OWN actor</c>, were folded into it and are a different failure with a different fix
    ///     — see the comment at the test itself. <c>no hand lists this arc</c> reads like a defect
    ///     and USUALLY IS NOT: all 21 of its readings in the 2026-09-07 evening logs are the
    ///     map-room loadout arc, which is not a hand and has no <c>CardsHandUI</c>. <c>derived list
    ///     shorter than the arc</c> is this client's own model lagging its own fan and is
    ///     transient; <c>no open fan</c> is the resting state and is owed nothing.</item>
    /// </list>
    ///
    /// <para>Change-gated on the verdict, the two lengths and the fingerprint, so a fan standing
    /// still costs one line and a drag costs one more — never the 5 Hz extras rate.</para>
    /// </summary>
    private static bool ReportFanArcOrder(bool sent, int arc, int seats, uint fp, string why)
    {
        long key = ((long)fp << 24) ^ ((long)arc << 12) ^ ((long)seats << 4) ^ (sent ? 1L : 0L)
                   ^ ((long)why.Length << 40);
        if (key != s_fanOrderSentKey)
        {
            s_fanOrderSentKey = key;
            // HW-VERIFY: report item 2 (2026-09-07). Grep token: FAN ARC ORDER SENT.
            Core.VRLog.Note("Net", $"FAN ARC ORDER SENT: {(sent ? "SENT" : "WITHHELD")} — arc={arc} "
                + $"seats={seats} fp={(sent ? fp.ToString("x8") : "--------")} "
                // THE LIST THE INDICES ARE IN, on the SENT path — record 44 is indices into the
                // list record 43 NAMES, not into the hand by definition (see SampleFanArcOrder's
                // index-space block). A watcher's own 'FAN SOURCE [player N]' line names the same
                // list from the other end, so the two settle in one grep whether both ends chose
                // the same population; they cannot differ within a packet, because one read of
                // SampleFanSource() feeds both records.
                + $"why={(sent ? $"our own arc, stated in full, as indices into the {why}" : why)}"
                + ". This is the OWNER edge of "
                + "extension record 44: 'the order I sent'. fp is an order-sensitive fold over "
                + "CardInstanceID, which is host-replicated, so the same arc in the same order "
                + "prints the SAME WORD on every machine — compare it with this player's "
                + "'MIRRORED ARC ORDER ... fp=' on any watcher and with this machine's own 'FAN "
                + "ORDER MIRROR ... drawn fp=', and the three settle \"exakt an den selben "
                + "Stellen\" with no screenshot and no second log. SENT means record 44 rides this "
                + "packet and states the arc in full — INCLUDING when it happens to equal the "
                + "order a watcher would derive, which every build from ModBuild 462 to 471 "
                + "deliberately omitted to save bytes. That omission was the defect: a receiver "
                + "rebuilds its slab list from the game's own order every frame and has nothing to "
                + "'keep', so silence never meant 'keep the order you had', it meant 'go back to "
                + "the game's order' — and the fan visibly snapped between the two as the sender's "
                + "arc drifted in and out of that identity. WITHHELD therefore now means exactly "
                + "one thing: this client could not honestly describe its own arc, and 'why' names "
                + "which exit fired.");
        }
        return sent;
    }

    /// <summary>
    /// THE LEFT-TO-RIGHT ORDER OF OUR OWN HAND ARC — extension record
    /// <see cref="NetProtocol.ExtIdFanArcOrder"/>'s whole payload, as indices into the hand list
    /// every receiver already builds. Report item 2 of 2026-09-06, and again of 2026-09-07.
    ///
    /// <para>WHAT IT WRITES: entry k is the position, in the list produced by walking
    /// <c>CardsHandUI.cardsUI</c> under <c>CardsGameApi.HandFanMember</c>, of the card the owner
    /// has at ARC SEAT k. That walk is verbatim the one <c>Net.RemoteHandFan</c> performs on the
    /// other machine and verbatim the one <see cref="NameHeldCard"/>'s hand arm seats record 36 in,
    /// so the three cannot drift into three index spaces — the mistake that cost report item 5b a
    /// whole round. NO CARD IDENTITY LEAVES THIS METHOD.</para>
    ///
    /// <para>IT IS SAMPLED FROM THE ARC ITSELF, on the same call as the arc COUNT, and that is a
    /// requirement rather than a convenience: a permutation that arrived one packet apart from the
    /// count it permutes would describe a different moment, and the receiver would apply yesterday's
    /// order to today's fan. Both come off <c>CardFan.Current</c> in the same frame.</para>
    ///
    /// <para><b>AND THE HAND IT DERIVES AGAINST IS NOW THE ARC'S OWN — 2026-09-07 REPORT ITEM 2.</b>
    /// This method used to be handed <c>CardsGameApi.ActiveHand()</c>, i.e.
    /// <c>CardsHandManager.CurrentHand</c>. The arc is not built from that: <c>CardsDriver.Rebuild</c>
    /// builds it from <c>Board.CharacterFocus.ResolveHand(CurrentHand())</c>, and
    /// <c>CardsDriver.CurrentHand</c> is <c>DecidingHand() ?? ActiveHand()</c> behind
    /// <c>IsLocalHand</c>. Whenever the deciding chain claimed a hand the tab had not switched to —
    /// an action-selection hand-off inside one phase, the boots' ± step, a take-damage panel, an
    /// item surrender — the derived walk here held ANOTHER CHARACTER'S widgets, every
    /// <c>IndexOf</c> returned -1, and the record was silently withheld for as long as that lasted.
    /// <see cref="HandListingArc"/> asks the ARC which hand lists it instead, so the two cannot
    /// drift; and the answer is refused outright unless that hand belongs to the very actor a
    /// watcher resolves for us (<c>NetPlayerActors.ActorFor</c>), because an order sampled off a
    /// character the watcher will not derive is not a saving — it is a well-formed permutation of
    /// the WRONG list, the one failure <c>NetProtocol.ValidateFanArcOrder</c> exists to
    /// prevent.</para>
    ///
    /// <para><b>IT NO LONGER STAYS SILENT ON AN IDENTITY, AND THAT IS THE OTHER HALF OF THE
    /// 2026-09-07 FIX.</b> Every build from ModBuild 462 omitted the record whenever the arc
    /// already equalled the derived order, on the stated grounds that "silence means keep the order
    /// you already had". The receiver keeps nothing: <c>RemoteHandFan</c> rebuilds its slab list
    /// from <c>CardsGameApi.HandFanMember</c> every frame and applies whatever the CURRENT packet
    /// states, so absence has always meant "fall back to the game's order". The two readings agree
    /// only in the case the sender was reasoning about and disagree in every other — which is why
    /// the 2026-09-07 session shows the peer's mirror of the host's two-card fan APPLYING
    /// <c>fp=27e7dccb</c> and then, 79 lines later, printing <c>fp=5469e8a7 … none stated</c> while
    /// the host's own <c>FAN ORDER MIRROR</c> had not changed at all: the fan snapped from the
    /// owner's order to the game's because one packet arrived without the record. So the record is
    /// now written whenever this method can answer, identity or not; absence means ONLY "the sender
    /// could not describe its arc", which is what <see cref="ReportFanArcOrder"/> prints. The cost
    /// is 3-9 bytes on a packet whose fan is open, and it is inside the documented worst case
    /// (1747 against <c>PresenceSerializer.MaxSize</c> 2100) because that case already counted this
    /// record present.</para>
    ///
    /// <para>IT RETURNS FALSE — and the caller then writes no record at all — in every case where
    /// it cannot answer honestly, and every one of them now NAMES ITSELF in the log:</para>
    /// <list type="bullet">
    ///   <item>no open fan, or no hand that lists the arc;</item>
    ///   <item>an arc belonging to a character a watcher will not resolve for us;</item>
    ///   <item>a hand or an arc past <see cref="NetProtocol.FanArcOrderMaxSeats"/>, which a 4-bit
    ///     index cannot name;</item>
    ///   <item>the derived list being SHORTER than the arc, which means this client's own model is
    ///     a beat behind its own fan and no index into it names the arc;</item>
    ///   <item>an arc card the derived walk does not contain — a browse loan, or a modal PICK fan
    ///     drawn over a pile (record 43), where the arc is not the hand at all and an index into
    ///     the hand would be a confident lie.</item>
    /// </list>
    ///
    /// <para>A SHORTER ARC IS NOT A REFUSAL, AND THAT IS 2026-09-06 REPORT ITEM 4. The derived list
    /// being LONGER than the arc is the normal, expected state whenever this player is holding a
    /// card up or has laid a hand card in one of their round recesses: their own <c>CardFan</c> has
    /// dropped it and <c>CardsGameApi.HandFanMember</c> has not, because the card has not been
    /// played. What travels is an INJECTION: <c>count</c> entries naming distinct derived indices,
    /// so the receiver learns both the order AND which derived cards the arc does not hold. Still
    /// no card identity — every entry is a position in a list the receiver builds itself, from the
    /// same expression, off host-replicated state.</para>
    /// </summary>
    internal static bool SampleFanArcOrder(int localPlayerId, int[] order, out int count)
    {
        count = 0;
        try
        {
            Cards.CardFan? fan = Cards.CardFan.Current;
            if (fan == null || order == null)
                return ReportFanArcOrder(false, 0, 0, 0u, "no open fan");
            System.Collections.Generic.IReadOnlyList<Cards.VRCard> arc = fan.Cards;
            int n = arc.Count;
            if (n <= 0)
                return ReportFanArcOrder(false, 0, 0, 0u, "no open fan");
            if (n > NetProtocol.FanArcOrderMaxSeats || n > order.Length)
                return ReportFanArcOrder(false, n, 0, 0u,
                    $"arc past the {NetProtocol.FanArcOrderMaxSeats}-seat 4-bit cap");

            // THE ARC NAMES ITS OWN HAND. See HandListingArc for the whole of why this is no
            // longer CardsGameApi.ActiveHand().
            Cards.VRCard first = arc[0];
            CardsHandUI? hand = HandListingArc(first != null ? first.GameCard : null);
            if (hand == null || hand.cardsUI == null)
                return ReportFanArcOrder(false, n, 0, 0u, "no hand lists this arc");
            CPlayerActor? actor = hand.PlayerActor;
            // ONE INDEX SPACE, ENFORCED RATHER THAN ASSUMED. A watcher derives this fan against
            // NetPlayerActors.ActorFor(ourPlayerId); an order sampled off any other character is a
            // valid-looking permutation of the WRONG list.
            //
            // THREE CAUSES, THREE STRINGS — and the 2026-09-07 evening round's ONLY measured loss of
            // a record 44 that a watcher could actually SEE went out under this one word. The user's
            // log prints `why=arc is not our own character's hand` three times, for arc=8/7/8
            // (lines 19951, 20009, 20023), on a frame whose own `fan state:` line reads
            // boundHand=True — and the co-player's MIRRORED ARC ORDER census answers
            // `refused/none stated` with model=8 at 11497/11567/11575, the same 8/7/8, the only three
            // census rows in either log where a mirrored fan WITH FRONTS got no order. One string
            // cannot say which of three things happened, and they have three different fixes: a hand
            // with no PlayerActor, and an ActorFor that cannot name OUR OWN actor, are both this
            // client failing to answer a question about ITSELF (NetPlayerActors.ActorFor walks
            // MyControllables and returns the FIRST CharacterManager it finds, which a summon or a
            // targeting controllable can displace), while the third is the genuine foreign-hand
            // refusal this test was written for. ReportFanArcOrder's change key folds why.Length and
            // the three lengths are distinct, so the three cannot collapse into one line.
            CPlayerActor? mine = NetPlayerActors.ActorFor(localPlayerId);
            if (actor == null)
                return ReportFanArcOrder(false, n, 0, 0u,
                    "the hand listing this arc has no PlayerActor");
            if (mine == null)
                return ReportFanArcOrder(false, n, 0, 0u,
                    "NetPlayerActors.ActorFor could not name OUR OWN actor");
            if (!ReferenceEquals(actor, mine))
                return ReportFanArcOrder(false, n, 0, 0u, "arc is not our own character's hand");

            // ─── THE INDEX SPACE IS RECORD 43'S TO CHOOSE, NOT THIS METHOD'S TO ASSUME ──────────
            // This walk was the HAND unconditionally, and that is the whole of 2026-09-07 report
            // item 9's second symptom ("sehen die anderen Spieler auch die Faecher nicht mehr").
            // While the game has its owner stepping through a modal card pick it re-Shows the hand
            // OVER a pile, so CardFan.Current is the DISCARD or LOST arc — every card of which
            // correctly fails a HandFanMember walk, and the IndexOf below then refused the whole
            // record. Measured on the ModBuild 472 host: 42 change-gated
            // `FAN ARC ORDER SENT: WITHHELD ... why=an arc card is not in this hand's fan walk (a
            // loan or a pick fan)` lines, against ZERO on the peer — the only asymmetric refusal
            // between the two logs, and the peer is the client whose fan went to backs.
            //
            // THE RECEIVER WAS ALREADY WRITTEN TO THIS CONTRACT AND ONLY THIS END BROKE IT, which
            // is why no receiver change goes with this one. Net.Remote.RemoteHandFan.UpdateFaces
            // calls ResolveHandFronts(actor, fanList) — which branches on
            // NetProtocol.IsFanSourcePile and fills _handBuffer from GetPileArcWidgets for a pick
            // fan — and only THEN calls ApplyFanArcOrder, which indexes into whatever _handBuffer
            // holds. Record 44 has therefore always meant "indices into the list record 43 names";
            // this end was the one that hardcoded the hand.
            //
            // ONE DECISION, BOTH RECORDS. The term is SampleFanSource(), the very expression record
            // 43 carries, read here rather than re-derived — so the two records cannot name
            // different populations in one packet. Re-deriving the pile from the game's
            // selectableCardType would be a second expression for one fact, which is the defect
            // NameHeldCard's own block is written about.
            //
            // AND IT IS BELTED BY THE IndexOf BELOW. If the two reads ever DID disagree, every arc
            // card would fail to be found in the derived list and the record is withheld exactly as
            // it is today — the failure direction is silence, never a valid-looking permutation of
            // the wrong list.
            byte fanList = SampleFanSource();
            bool pickFan = NetProtocol.IsFanSourcePile(fanList);
            s_fanOrderDerived.Clear();
            if (pickFan)
            {
                Cards.CardsGameApi.GetPileArcWidgets(
                    hand, fanList == NetProtocol.HeldFaceListBurnt, s_fanOrderDerived);
            }
            else
            {
                System.Collections.Generic.List<AbilityCardUI> all = hand.cardsUI;
                for (int i = 0; i < all.Count; i++)
                {
                    if (Cards.CardsGameApi.HandFanMember(all[i], actor))
                        s_fanOrderDerived.Add(all[i]);
                }
            }
            // THE DERIVED LIST MAY BE LONGER THAN THE ARC AND MUST NEVER BE SHORTER. Longer is the
            // ordinary state of a card in this player's fist or lying in one of their recesses, and
            // it is exactly the state the injection below exists to describe. SHORTER means this
            // client's own model has not caught up with its own fan, and then no index into it names
            // the arc at all — say nothing. A hand the 4-bit index cannot span is refused for the
            // same reason the arc is refused above.
            if (s_fanOrderDerived.Count < n)
            {
                int had = s_fanOrderDerived.Count;
                s_fanOrderDerived.Clear();
                return ReportFanArcOrder(false, n, had, 0u, "derived list shorter than the arc");
            }
            if (s_fanOrderDerived.Count > NetProtocol.FanArcOrderMaxSeats)
            {
                int had = s_fanOrderDerived.Count;
                s_fanOrderDerived.Clear();
                return ReportFanArcOrder(false, n, had, 0u,
                    $"hand past the {NetProtocol.FanArcOrderMaxSeats}-seat 4-bit cap");
            }

            // The SAME order-sensitive FNV fold over CardInstanceID that CardsDriver's FAN ORDER
            // MIRROR and RemoteHandFan's MIRRORED ARC ORDER print, spelled here over the ARC so the
            // three words are directly comparable across machines. Instrument only — no byte of it
            // reaches the wire.
            uint fp = 2166136261u;
            unchecked
            {
                for (int k = 0; k < n; k++)
                {
                    Cards.VRCard c = arc[k];
                    AbilityCardUI? w = c != null ? c.GameCard : null;
                    fp = (fp ^ (uint)k) * 16777619u;
                    fp = (fp ^ (uint)(w != null ? w.CardInstanceID : 0)) * 16777619u;
                }
            }

            for (int k = 0; k < n; k++)
            {
                Cards.VRCard card = arc[k];
                AbilityCardUI? widget = card != null ? card.GameCard : null;
                int at = widget != null ? s_fanOrderDerived.IndexOf(widget) : -1;
                if (at < 0)
                {
                    s_fanOrderDerived.Clear();
                    // An arc card the walk of the list record 43 NAMES does not hold. A pick fan is
                    // no longer one of these — that was this refusal's commonest cause and it is
                    // fixed above — so the reason string no longer says it is, and a reading of it
                    // now means a genuine foreign card in the arc (a browse loan) or the two reads
                    // of FanSourcePile disagreeing inside one packet.
                    return ReportFanArcOrder(false, n, 0, 0u,
                        "an arc card is not in the " + (pickFan ? "pick fan's pile" : "hand")
                        + " walk (a browse loan, or record 43 naming a list this arc is not)");
                }
                order[k] = at;
            }
            s_fanOrderDerived.Clear();
            // NO IDENTITY SHORTCUT. See the header: the receiver has nothing to "keep", so an
            // omitted record is read as the GAME's order and the fan snaps back to it. State it.
            count = n;
            return ReportFanArcOrder(true, n, n, fp,
                pickFan ? "pick fan pile arc" : "hand");
        }
        catch (System.Exception ex)
        {
            s_fanOrderDerived.Clear();
            count = 0;
            // A card mid-teardown must never take down the extras sender — but it must not be
            // indistinguishable from a resting fan either.
            return ReportFanArcOrder(false, 0, 0, 0u, "sampler threw: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// WHICH ROUND RECESS HOLDS THE SHORT-REST SACRIFICE, AND WHICH CARD IT IS — extension record
    /// <see cref="NetProtocol.ExtIdSacrificeSeat"/>'s whole payload, as a <c>[code][list length]</c>
    /// pair per RECESS. A code of 0 means "no sacrifice in this recess", and when both are 0 the
    /// writer omits the record entirely, so every packet outside the few seconds of an actual short
    /// rest is byte-identical to ModBuild 447's.
    ///
    /// <para>REPORT ITEM 15, and the reason it needed a wire field at all. A watcher could name a
    /// recess card only out of <c>CCharacterClass.RoundAbilityCards</c>
    /// (<c>RemoteControlBoard.OrderRoundCards</c>), and the sacrifice was never in it — so the
    /// recess drew an anonymous back and the user saw "nur die Rückseite". The card is NOT, however,
    /// outside every replicated list, which is what two earlier lanes concluded and what
    /// <c>NetProtocol.ExtIdSacrificeSeat</c>'s doc block sets out in full: it sits in
    /// <c>DiscardedAbilityCards</c> for the entire time it lies in the recess, because
    /// <c>CardsHandUI.PerformShortRest</c> indexes that list and removes nothing.</para>
    ///
    /// <para>THE SEAT IS RESOLVED WITH THE RECEIVER'S OWN CALL —
    /// <c>Cards.CardsGameApi.GetPileArcWidgets(hand, burnt: false)</c>, the same method
    /// <c>RemoteHeldCardFace.Resolve</c> and <see cref="NameHeldCard"/>'s pile arm now use. An index
    /// is only a name for a card while both machines build the list with the SAME expression, so
    /// there is one method and no second copy of its terms to keep in step.</para>
    ///
    /// <para>THE ACTOR MUST BE THE ONE THE BOARD PRESENTS, exactly as in
    /// <see cref="SampleHeldCardFaces"/>: the receiver resolves this index through
    /// <c>RemoteBoardFocus.DisplayedActor</c>'s discard pile, so naming a seat in a DIFFERENT
    /// character's list would put a confidently wrong face in a peer's recess. When the sacrificing
    /// hand is not that character's, this says nothing and the recess keeps the anonymous back —
    /// the honest answer, and the picture this surface had before the record existed.</para>
    ///
    /// <para>Read-only throughout, and gated on a short rest actually being mid-choice, so on every
    /// other packet the whole body is one null check.</para>
    /// </summary>
    public static void SampleSacrificeSeats(out byte code0, out byte count0,
                                            out byte code1, out byte count1, out string reason)
    {
        code0 = 0;
        count0 = 0;
        code1 = 0;
        count1 = 0;
        reason = "nothing was lying in either recess that this record can name";
        // ─── CASE 1: THE SHORT-REST SACRIFICE (report item 15) ──────────────────────────────────
        (CardsHandUI? hand, int recess) = Cards.CardsDriver.SacrificeSeat;
        if (hand != null && recess >= 0 && recess < NetProtocol.BoardUiSlotCount)
        {
            // EVERY REFUSAL FROM HERE ON NAMES ITSELF. A short rest IS mid-choice and the card IS
            // lying in a recess — so from this point a zero code is a defect and not a quiet
            // no-op, and the whole reason this record read INERT for a 2 h two-player session is
            // that nothing said which of the three terms below refused it.
            if (!OwnedByPresentedCharacter(hand))
            {
                reason = "a short rest IS mid-choice, but the sacrificing hand is not the character "
                       + "this client's board presents — the receiver resolves every seat in the "
                       + "presented character's list, so naming one here would be a wrong face";
            }
            else
            {
                AbilityCardUI? widget = Cards.CardsGameApi.ShortRestedCardWidget(hand);
                if (widget == null || widget.AbilityCard == null)
                {
                    reason = "a short rest IS mid-choice and the board recess holds its card, but "
                           + "CardsGameApi.ShortRestedCardWidget resolved no widget this frame";
                }
                else if (!TrySeatInPileArc(hand, widget, burnt: false,
                                           out byte code, out byte count))
                {
                    reason = "a short rest IS mid-choice and its widget resolved, but the card is "
                           + "not in this character's DISCARD arc this frame "
                           + "(CardsGameApi.GetPileArcWidgets) or its seat does not fit in 5 bits";
                }
                else
                {
                    Write(recess, code, count, ref code0, ref count0, ref code1, ref count1);
                    reason = $"SHORT-REST SACRIFICE seated in recess {recess + 1}";
                }
            }
        }

        // ─── CASE 2: A MODAL PICK'S CARD LYING IN A RECESS (2026-09-06 report item 7) ───────────
        // The long rest's burn card, and every other pick that lays a card down out of a PILE. Asked
        // per recess, after the sacrifice, and it may not overwrite one: the two never coexist (a
        // short rest presents into recess 0 and no pick field is up), and where they somehow would,
        // the sacrifice is the more specific fact.
        for (int r = 0; r < NetProtocol.BoardUiSlotCount; r++)
        {
            if (r == 0 ? code0 != 0 : code1 != 0)
                continue;
            (CardsHandUI? pickHand, AbilityCardUI? pickWidget) = Cards.CardsDriver.PickFieldSeat(r);
            if (pickHand == null || pickWidget == null || pickWidget.AbilityCard == null)
                continue;
            if (!OwnedByPresentedCharacter(pickHand))
                continue;
            // WHICH PILE THE CARD CAME OUT OF, read off the WIDGET rather than off the pick mode:
            // the mode says what the game is asking for, the CardType says where this particular
            // card actually is, and the receiver resolves the arc the card is IN. A card that is in
            // neither pile arc — a HAND card, which is what an avoid-damage burn or a card-limit
            // discard lays down — falls through and stays an anonymous back. That is the third of
            // the three refusals NetProtocol.ExtIdSacrificeSeat's boundary paragraph names, and it
            // is the one that makes the format itself unable to express the two-card commit.
            CardPileType pile = pickWidget.CardType;
            if (pile != CardPileType.Discarded && pile != CardPileType.Lost
                && pile != CardPileType.Permalost)
            {
                reason = $"a PICK card lies in recess {r + 1} and its pile is {pile}, which this "
                       + "record may not name — a HAND card in a recess is the two-card commit's "
                       + "own population and stays an anonymous back by construction";
                continue;
            }
            if (TrySeatInPileArc(pickHand, pickWidget, pile != CardPileType.Discarded,
                                 out byte pickCode, out byte pickCount))
            {
                Write(r, pickCode, pickCount, ref code0, ref count0, ref code1, ref count1);
                reason = $"MODAL PICK card seated in recess {r + 1} out of the {pile} arc";
            }
            else
            {
                reason = $"a PICK card lies in recess {r + 1} out of the {pile} arc and could not "
                       + "be seated in it this frame (CardsGameApi.GetPileArcWidgets), so it stays "
                       + "an anonymous back rather than a guessed seat";
            }
        }
    }

    /// <summary>
    /// Is <paramref name="hand"/> the character the BOARD presents? The receiver resolves every seat
    /// this record carries in the presented character's list, so naming a seat in a different
    /// character's would put a confidently wrong face in a peer's recess. Silence beats a guess at
    /// whose list to index.
    ///
    /// <para>THE ITEM PILE WAS THE WRONG SOURCE AND THIS RECORD HAS NEVER ONCE WORKED BECAUSE OF IT.
    /// <c>Cards.ItemsPile.Current</c> is published by the ITEM ARC and only while it is open
    /// (<c>ItemsPile.Open</c> assigns it, <c>Close</c> nulls it) — a player raises that arc for a
    /// few seconds a session, and every other frame this read answered null and the whole record was
    /// omitted. That is the identical defect <see cref="SampleHeldCardFaces"/> was fixed for one
    /// method over, and the sentence that used to stand here — "the same stance
    /// <c>NameHeldCard</c> takes" — became FALSE the moment that fix landed and was still being
    /// cited as the justification.</para>
    ///
    /// <para>THE EVIDENCE IS TOTAL, and it is a silence that had to be counted rather than read. In
    /// the 2026-09-06 two-player session — which contains a complete short rest, its sacrifice
    /// presented and REDRAWN, on the co-player's own log — the <c>SHORT REST SEAT</c> sender line
    /// appears ZERO times in either 50-85 MB log, and every single <c>board pick seat</c> row of the
    /// receiver's census on both machines reads "extension record 39 named NO seat for this recess"
    /// (134 host rows, 155 peer rows, no other text). The owner's own line said so from the other
    /// end: "peers draw: ANONYMOUS BACK ... identity replicated=False".</para>
    ///
    /// <para>THE FALLBACK IS THE RECEIVER'S OWN QUESTION ASKED ON THIS SIDE.
    /// <c>Board.CharacterFocus.PresentedActor</c> is what extension record 22 carries and what
    /// <c>Net.RemoteBoardFocus.DisplayedActor</c> resolves the seat against, so the two ends name the
    /// same list by construction rather than by coincidence. Guarded on
    /// <c>RevealGate.InScenario</c> for the same reason the held-card sampler guards it: a presented
    /// actor left standing after a finished scenario must not name a list nobody is holding.</para>
    /// </summary>
    private static bool OwnedByPresentedCharacter(CardsHandUI hand)
    {
        CPlayerActor? shown = Cards.ItemsPile.Current?.OwnerActor;
        if (shown == null && RevealGate.InScenario)
            shown = Board.CharacterFocus.PresentedActor;
        return shown != null && ReferenceEquals(hand.PlayerActor, shown);
    }

    /// <summary>Seat <paramref name="widget"/> in its owner's DISCARD or BURNT arc —
    /// <c>CardsGameApi.GetPileArcWidgets</c>, the identical call the receiver re-walks — and encode
    /// it. False (and nothing written) when the card is not in that arc this frame or the seat does
    /// not fit in five bits: half a thing is worse than an honest back.</summary>
    private static bool TrySeatInPileArc(CardsHandUI hand, AbilityCardUI widget, bool burnt,
                                         out byte code, out byte count)
    {
        code = 0;
        count = 0;
        Cards.CardsGameApi.GetPileArcWidgets(hand, burnt, s_heldFaceBuf);
        int at = s_heldFaceBuf.IndexOf(widget);
        int length = s_heldFaceBuf.Count;
        s_heldFaceBuf.Clear();
        if (at < 0)
            return false;
        byte encoded = NetProtocol.EncodeHeldFace(
            burnt ? NetProtocol.HeldFaceListBurnt : NetProtocol.HeldFaceListDiscard, at);
        if (encoded == 0)
            return false;
        code = encoded;
        count = (byte)Mathf.Clamp(length, 0, 255);
        return true;
    }

    private static void Write(int recess, byte code, byte count,
                              ref byte code0, ref byte count0, ref byte code1, ref byte count1)
    {
        if (recess == 0)
        {
            code0 = code;
            count0 = count;
        }
        else
        {
            code1 = code;
            count1 = count;
        }
    }

    /// <summary>Reused widget buffer for <see cref="NameHeldCard"/>'s pile lookups — the send path
    /// runs at the extras rate and must stay allocation-free.</summary>
    private static readonly System.Collections.Generic.List<AbilityCardUI> s_heldFaceBuf = new();

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
