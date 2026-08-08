using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cosmetic ghost of a remote player's ITEM fan (<see cref="ItemsPile"/>) — the counterpart of
/// <see cref="RemoteHandFan"/> for equipped item cards.
///
/// ROOT CAUSE this exists (user report 5, multiplayer half): the extras packet only ever carried
/// the ABILITY hand-card count, so when a peer raised their item fan — a very visible, near-square
/// arc of cards floating over their palm or their board — every other player saw absolutely
/// nothing. The item fan is now broadcast as an additive count + a held/board-anchored flag
/// (<see cref="NetProtocol.FlagItemFan"/>), and this renders it.
///
/// ANTI-CHEAT / bandwidth: no item identity, art or state ever rides the WIRE — a peer transmits how
/// many item cards are up and where the fan is, nothing more. Item cards are near-square rather than
/// 63.5×88, so the slab uses its own dimensions; the real PER-CARD face size is deliberately NOT
/// transmitted.
///
/// <para>THAT DECISION WAS RE-EXAMINED against the 2026-08-08 1:1 ruling and DELIBERATELY KEPT, so
/// here is the reasoning rather than an assertion. The owner's item chip sizes itself from the REAL
/// hosted <c>ItemCardUI</c> rect (<c>ItemsPile.ItemChip.FaceWidth/FaceHeight</c>) — a
/// PER-CARD measurement of a game widget, in units of their <c>[Cards] CardWidth</c>. Two of those
/// three inputs already reach this client without a byte: the widget is the game's own prefab
/// (identical on every machine, and this fan already hosts it through <see cref="RemotePileFronts"/>
/// off the peer's host-replicated <c>CInventory.AllItems</c>), and CardWidth now rides extension
/// record 28. What is left is the residual per-card aspect, and buying it would cost a length-
/// prefixed list of up to 12 sizes — ~25 bytes on EVERY packet while a fan is open, more than the
/// whole board-tuning record's typical cost — to correct a difference the receiver can measure for
/// itself from the same widget it is already drawing. A record that pays 25 B/packet for a number
/// the receiver holds locally is the "second source of truth" the tooltip's own frame-metrics note
/// rejects. If the near-square constants below are ever seen to disagree with a real item on
/// hardware, the fix is to measure the hosted face here — not to transmit it.</para>
///
/// CARD FRONTS (user ruling 2026-08-08, "Die Oberseiten der Karten des remote Spielers soll auch
/// überall sichtbar sein … NUR in der Auswahlphase sieht man überall nur die Rückseiten"): this arc
/// used to be BACKS UNCONDITIONALLY. That made the secrecy rule a PLACE rule, in direct conflict with
/// the PHASE rule the round-card slots and the hand fan already obeyed. Each slab now carries the REAL
/// item card face whenever <see cref="RevealGate.ShowRoundCardFronts"/> is open for the displayed
/// actor, drawn by <see cref="RemotePileFronts"/> from that peer's own host-replicated
/// <c>CInventory.AllItems</c> — the identical list the LOCAL <see cref="ItemsPile"/> reads. Zero new
/// wire bytes: the item identities were already on this client, they were simply never drawn.
///
/// THE CARD IN THE USE RECESS (extension record 26, 2026-08-09): one of these slabs may be lying in
/// the owner's item-USE recess rather than standing in the arc. WHICH one arrives as a bare arc
/// INDEX and the slab is re-parented onto <see cref="RemoteBoardFurniture.ItemUseRecess"/>, so it
/// lies flat IN the recess and rides that board — never billboarded at a head. The 0.28 s arrival
/// settle and the glide back out are replayed from the index's own EDGES on the local clock, like
/// the fan's deal-out and fold-in; see the field block below for the whole argument.
///
/// PLACEMENT mirrors the local fan's two modes: HAND-HELD → floating a palm standoff above the
/// sender's DOMINANT hand (the hand that pinch-grabbed the item stack); BOARD-ANCHORED → floating
/// above the sender's synced control board at the same shared board-top spot the local fan uses.
/// Faces away from the owner's head so the owner's side reads as the "front" and everyone else sees
/// backs — the same convention as every other remote card visual.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY (the fan's existence, size and placement) + PER-ACTOR MODEL (its
/// card faces) + WIRE (extension record 28, via <see cref="RemoteBoardTuning"/> — the owner's own
/// open/close ANIMATION dials, and only the ones they have actually moved) — costs wire bytes: extras
/// <c>FlagItemFan</c> + one count byte, plus the two pure flags
/// <c>FlagItemFanHeld</c> / <c>FlagItemFanLeft</c> (0 B each, and both INERT — the whole-fan grab is
/// gone), plus extension record 26 (ONE index byte, and only while a card really lies in the owner's
/// item-use recess). Item IDENTITY and per-item face size
/// are DELIBERATELY-NOT transmitted; the FACES are resolved locally off the host-replicated
/// <c>Inventory.AllItems</c> behind <see cref="RevealGate"/> (<see cref="RemotePileFronts"/>). The
/// item's real effect syncs authoritatively through <c>UseItemService</c>, not through here. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteItemFan
{
    // ---- geometry (mirror of ItemsPile's arc constants) --------------------------------------
    private const int MaxCards = 12;
    private const float CardW = 0.075f;                  // item cards read near-square…
    private const float CardH = CardW * 1.15f;           // …so this is NOT the 88/63.5 ability ratio
    private const float Radius = 0.1792f * 1.7f;         // CardsConfig.FanEffectiveRadius × RadiusFactor
    private const float MaxArcDegrees = 110f;            // ItemsPile.MaxArcDegrees
    private const float MaxStepDegrees = 10f;            // ItemsPile.MaxStepDegrees
    private const float ZStagger = 0.004f;               // ItemsPile.ZStagger (draw order)
    private const float HandPalmOffset = 0.16f;          // ItemsPile.HandPalmOffset
    private const float BoardFloatHeight = 0.26f;        // ItemsPile.BoardFloatHeight
    private const float BoardFloatProudZ = -0.05f;       // ItemsPile.BoardFloatProudZ
    private const float Smoothing = 14f;

    private readonly RemoteAvatar _owner;
    private GameObject? _root;
    private readonly List<GameObject> _cards = new(MaxCards);

    /// <summary>The FRONT layer over those slabs (user ruling 2026-08-08) — one overlay per slab,
    /// gated on <see cref="RevealGate.ShowRoundCardFronts"/> and fed from the peer's own replicated
    /// inventory. Owned here, destroyed with the fan.</summary>
    private readonly RemotePileFronts _fronts;

    private Mesh? _mesh;
    private int _builtCount = -1;
    private bool _poseInit;
    private int _loggedCount = -1;
    private bool _loggedHeld;
    /// <summary>One-shot latch for the "hidden by the remote-board setting" line (see Tick).</summary>
    private bool _gateHiddenLogged;

    // ---- emerge / collapse (the peer-visible "Auf- und Zuklappen" of the item Fach) ------------
    // The local fan does BOTH: ItemsPile.EmergeAll deals every chip OUT of the items stack into the
    // arc, and ItemsPile.CollapseChips folds every chip back INTO that stack before it dies. This
    // ghost used to do neither properly — it spread from its own centre and then vanished instantly
    // on close — so a peer never saw the fan close AT ALL, it just blinked out.
    //
    // Driven, like RemoteBrowserFan's, off the RECEIVER'S state transition (count 0↔N) rather than
    // off an event: the receiver already knows the sender's board pose and therefore where their
    // ITEMS stack is (RemoteControlBoard.AnchorLocal), so both arcs replay locally for zero extra
    // wire bytes. Timings match the local ones so both players see the same motion.
    //
    // ─── THE PRESENCE PASS (user report 2026-08-08: "Ich mag die Animation im Item-Pile sehr aber
    // sie ist (insbesondere in mixed Reality) etwas zu dezent.") ────────────────────────────────
    // The local fly-out stopped being an exponential home-lerp and became a parametric, per-chip
    // animation: a centre-out deal-out ripple, a mid-flight bow toward the viewer, growth from a
    // much smaller seed, a signed unfold roll, and an ease-out-BACK overshoot at the settle. The
    // fold-in is the same curve reversed (outermost chip first, ease-in-BACK wind-up).
    //
    // ALL OF IT IS REPRODUCED HERE, and that is not optional polish: the standing 1:1 ruling names
    // "alle Interaktionen, **Animationen** und Anzeigen des Controllboards". An owner whose item
    // fan deals out with an audible sense of weight while every peer sees the old smooth blob is
    // precisely the divergence the ruling forbids — and it is the kind nobody can spot from inside
    // their own headset. The maths below is ItemsPile.ItemChip's, term for term; the only
    // difference is that it runs in fan-LOCAL space on a slab instead of on a hosted card widget.
    private float _emergeElapsed = -1f;
    private Vector3 _emergeSeedLocal;                 // the sender's items stack, in fan-local space

    // ---- the owner's own ANIMATION dials (extension record 28) --------------------------------
    // Wire-overridable fallbacks, exactly like RemoteHandFan's geometry fields: the value the owner
    // set where they moved the dial, this client's shipped constant where they did not — which is
    // the same number, so an untuned peer is drawn with the shipped animation. Refreshed by
    // SyncTuning() on the owner's tuning revision, read as plain floats in between (the layout loop
    // touches them per slab per frame and RemoteBoardTuning is a wide struct).
    // scripts/check-remote-defaults.py holds all eight to the Defaults entries the local binds read.
    private float _openSeconds = Defaults.ItemFanOpenDuration;
    private float _openStagger = Defaults.ItemFanOpenStagger;
    private float _openArc = Defaults.ItemFanOpenArc;
    private float _openSpinDegrees = Defaults.ItemFanOpenSpinDegrees;
    private float _seedScale = Defaults.ItemFanSeedScale;
    private float _settleOvershoot = Defaults.ItemFanSettleOvershoot;
    private float _closeSeconds = Defaults.ItemFanCloseDuration;
    private float _closeStagger = Defaults.ItemFanCloseStagger;

    /// <summary>The <see cref="RemoteAvatar.BoardTuningRevision"/> the eight dials above were last
    /// refreshed at (−1 = never). Latched, not value-compared — the resolve already happens once
    /// per real change in <see cref="RemoteAvatar"/>.</summary>
    private int _tuningRevision = -1;

    // ---- the card LYING IN the owner's item-use recess (extension record 26) --------------------
    // THE GAP THIS CLOSES (2026-08-09). The owner lays a usable item card into their board's
    // item-USE recess; it re-parents onto the recess and settles into it over ClipSettleSeconds.
    // This ghost replayed a bare COUNT into the arc and RemoteBoardFurniture carried recess
    // VISIBILITY only, so on every other machine that card was still drawn out here in the arc while
    // the mirrored recess stood empty. The standing multiplayer ruling of 2026-08-08 covers "alle
    // Interaktionen, Animationen und Anzeigen des Controllboards", and a card lying in a recess is
    // such an Anzeige.
    //
    // WHAT ARRIVES IS ONE INDEX (NetProtocol.ExtIdItemUseClip) — a position in the very arc this
    // renderer already builds. So the fix is not a new object at all: the slab that would have been
    // drawn at that arc position is RE-PARENTED onto the mirrored recess, exactly as the owner's own
    // chip is re-parented onto theirs (ItemsPile.ItemChip.ClipIntoSlot). The hierarchy then holds it
    // there rigidly — right rotation, right board scale, right visibility, zero per-frame work — and
    // it LIES IN the recess rather than billboarding to anyone's head. Routing it through the synced
    // held-card pose slot was considered and rejected for precisely that: that receiver aims its
    // slab at the owner's head, so the card would have floated upright over the recess.
    //
    // THE ARRIVAL IS ANIMATED FROM THE EDGE, not streamed. The receiver knows the frame the index
    // appears, so it runs the owner's own settle on its own clock — same re-parent-keeping-world-
    // pose, same exponential, same duration — the identical trick that already replays the fan's
    // whole deal-out and fold-in for zero wire bytes. Streaming a pose would cost ~20 B × 15 Hz for
    // a 0.28 s movement both machines can derive from one byte.
    //
    // TAKING IT BACK is the same edge in reverse: the index disappears, the slab returns to the fan
    // root keeping its world pose and GLIDES to its arc slot over ReleaseGlideSeconds — the mirror
    // of ItemChip.ReturnToFan, which is the animation the owner sees when they pull the card out.
    private int _clipIndex = -1;          // slab currently parented to the mirrored recess, -1 = none
    private float _clipSettle;            // seconds left of the arrival ease (0 = landed / none)
    private float _clipFitScale = 1f;     // recess-local scale it settles to (fitted to the plate)
    private int _returnIndex = -1;        // slab gliding back to the arc after a take-back, -1 = none
    private float _returnGlide;           // seconds left of that glide
    private int _loggedClip = -2;         // change gate for the clip log line

    /// <summary>Unscaled seconds the arrival settle runs — MIRROR of
    /// <c>Cards.ItemsPile.ItemChip.ClipSettleSeconds</c>, the window the owner's own card eases from
    /// the release pose into the recess frame in. Held equal by scripts/check-mirrors.sh: a peer
    /// whose settle is a different LENGTH is watching a different animation, which is exactly what
    /// the 1:1 ruling forbids.</summary>
    private const float ClipSettleSeconds = 0.28f;

    /// <summary>Unscaled seconds the take-back glide runs — MIRROR of
    /// <c>Cards.ItemsPile.ItemChip.ReleaseGlideSeconds</c>, the window the owner's card flies home to
    /// its arc slot in.</summary>
    private const float ReleaseGlideSeconds = 0.35f;

    /// <summary>How much of the recess's clear inner plate a card lying in it fills, so the gold rim
    /// stays visible all the way round — MIRROR of <c>Cards.ItemsPile.UseSlotFillFraction</c>. The
    /// plate itself is <see cref="RemoteBoardFurniture.ItemUseInnerWidth"/>/<c>…Height</c>, i.e. THIS
    /// board's own geometry, so the fit is derived exactly the way the owner derives theirs: from
    /// the recess in front of the card, never from the fan's chip scale.</summary>
    private const float UseSlotFillFraction = 0.94f;

    private float _collapseElapsed = -1f;
    private Vector3 _collapseTo;
    private readonly List<Vector3> _collapseFrom = new(MaxCards);
    private readonly List<Quaternion> _collapseFromRot = new(MaxCards);
    private readonly List<float> _collapseFromScale = new(MaxCards);

    /// <summary>Drop the three index-parallel capture lists together. They are only ever filled and
    /// cleared as a set, and a partial clear would leave the collapse indexing a dead slab.</summary>
    private void ClearCollapseCapture()
    {
        _collapseFrom.Clear();
        _collapseFromRot.Clear();
        _collapseFromScale.Clear();
    }

    public RemoteItemFan(RemoteAvatar owner)
    {
        _owner = owner;
        _fronts = new RemotePileFronts(owner, "item fan");
    }

    public void Tick(float dt)
    {
        dt = Mathf.Max(dt, 0f);

        // The owner's own animation dials (record 28) BEFORE anything reads them — including the
        // collapse below, which must run on the owner's close timing even though the fan is already
        // logically gone.
        SyncTuning();

        // A collapse (the fan closing) runs to completion on its own — the count already went to 0,
        // so this is the only thing keeping the chips on screen.
        if (_collapseElapsed >= 0f)
        {
            TickCollapse(dt);
            return;
        }

        int count = Mathf.Clamp(_owner.ItemCardCount, 0, MaxCards);
        if (count == 0)
        {
            // Close edge: prefer the collapse-into-the-stack glide; BeginCollapse returns false when
            // there is nothing up or no stack to aim at, and only then do we blink out as before.
            if (!BeginCollapse())
                Hide();
            return;
        }

        // VISIBILITY ([Net] RemoteBoards — audit 2026-07). A BOARD-ANCHORED item fan is part of the
        // peer's board: it floats at a fixed board-local spot above their control board and emerges
        // out of that board's items stack. Before this check it ignored the setting entirely, so a
        // player who had chosen "Aus" — or "Aktionsphase" during the secret selection phase — saw a
        // near-square arc of card backs blooming in empty air exactly where the board they had asked
        // NOT to see would have been. It obeys the shared gate now. A HAND-HELD fan is avatar
        // content (it rides the sender's palm) and is deliberately left alone: the setting governs
        // boards, not hands.
        // Hidden INSTANTLY rather than collapsed: the collapse animation flies the chips into the
        // board's items stack, and that stack is exactly what the gate just hid — an arc gliding
        // into nothing is worse than the fan simply not being there. The next time the gate opens
        // with the fan still up, _root is inactive, so the normal emerge-out-of-the-stack runs.
        if (!_owner.ItemFanHeld && !RemoteBoardGate.ShowBoardSurface(_owner))
        {
            if (!_gateHiddenLogged)
            {
                _gateHiddenLogged = true;
                VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: board-anchored fan " +
                                  $"HIDDEN by [Net] RemoteBoards = {RemoteBoardGate.Mode} " +
                                  "(it belongs to that peer's board, which this client is not drawing).");
            }
            Hide();
            return;
        }
        _gateHiddenLogged = false;

        if (!TryResolvePose(out Vector3 target, out Quaternion rot))
        {
            Hide(); // holding hand not tracked / no board pose — no anchor, so nothing to show
            return;
        }

        EnsureRoot();
        if (_root == null)
            return;
        if (count != _builtCount)
            Rebuild(count);
        // Scale lives on the root (the fan is NOT parented under a scaled holder) and re-applies
        // every frame (one compare). It reproduces WHICHEVER transform the LOCAL fan hangs under
        // — the same rule RemoteBrowserFan documents: a HELD fan rides the palm (rig scale), a
        // BOARD-ANCHORED one is a child of the board root (ItemsPile.Open) and wears the BOARD
        // scale. The board branch used to apply the rig scale — the receiver-side fidelity bug
        // the old TryResolvePose note recorded as "needs a hardware round"; fixed in the 1:1
        // parity round (the board scales independently of world zoom, so the two routinely
        // differed and the peer's fan rendered the wrong size).
        float rootScale = _owner.ItemFanHeld
            ? _owner.AppliedScale
            : _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        if (!Mathf.Approximately(_root.transform.localScale.x, rootScale))
            _root.transform.localScale = Vector3.one * rootScale;
        if (!_root.activeSelf)
        {
            _root.SetActive(true);
            _poseInit = true;   // snap on the frame we appear, never ease in from a stale pose
            _root.transform.SetPositionAndRotation(target, rot);
            SeedEmerge();       // …and the chips fly OUT of the sender's ITEMS stack, like the local fan
        }

        Transform t = _root.transform;
        if (_poseInit)
        {
            _poseInit = false;
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * Mathf.Max(dt, 0f));
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rot, k));
        }

        // THE CARD IN THE RECESS (extension record 26) — resolved BEFORE the layout, because the
        // layout must know which slab it may not write this frame: the clipped one belongs to the
        // recess's hierarchy, not to the arc.
        ResolveClip();
        Layout(count, dt);
        TickClipSettle(dt);

        // THE FRONT LAYER (user ruling 2026-08-08). After the layout, so a face is only ever asked for
        // on a slab that already sits where it belongs. The gate inside is evaluated every frame; the
        // model resolve behind it rides the board-content cadence — see RemotePileFronts.
        _fronts.Tick(RemotePileFronts.Content.Items);

        if (count != _loggedCount || _owner.ItemFanHeld != _loggedHeld)
        {
            _loggedCount = count;
            _loggedHeld = _owner.ItemFanHeld;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: {count} item card(s), " +
                              $"{(_owner.ItemFanHeld ? $"hand-held above their {(_owner.ItemFanLeftHand ? "LEFT" : "RIGHT")} palm" : "anchored above their board")} " +
                              "— no item identity on the wire; whether the slabs show FRONTS or BACKS " +
                              "is stated separately by the \"Remote item fan faces\" line (RevealGate " +
                              "decides, per phase).");
        }
    }

    /// <summary>Where the fan sits this frame: above the sender's dominant palm when they hold it,
    /// otherwise at their synced board-local fan anchor (record 5; authored default for
    /// pre-record senders). False when neither reference exists yet.
    ///
    /// NEAR-TWIN OF <c>RemoteBrowserFan.TryResolveAnchor</c> — kept separate because the browse
    /// fan carries per-card enlargement and a root-scale out-param. Since the 1:1 parity round
    /// the SCALE rule finally agrees between the two: both reproduce whichever transform the
    /// LOCAL fan hangs under (held → rig scale, board-anchored → board scale; see the scale
    /// note in <see cref="Tick"/> — the old rig-scale-on-the-board-branch divergence was a
    /// receiver-side fidelity bug, now fixed).</summary>
    private bool TryResolvePose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = Quaternion.identity;
        float scale = _owner.AppliedScale;

        if (_owner.ItemFanHeld)
        {
            // The grabbing hand rides the wire as a flag (FlagItemFanLeft) — the owner may raise the
            // item fan with either hand, and guessing the dominant one put it on the wrong arm.
            Transform? holder = _owner.ItemFanLeftHand ? _owner.LeftHandHolder : _owner.RightHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            pos = holder.position + holder.up * (HandPalmOffset * scale);
        }
        else
        {
            if (!_owner.HasBoard)
                return false;
            float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
            // Defect 6 ("die Fächer sitzen nicht da, wo der Besitzer sie hat"): prefer the
            // SYNCED board-local anchor (extension record 5) — the owner's real fan spot
            // including their per-board [Cards] offsets. The authored default survives only
            // for pre-record senders / while the record is absent.
            Vector3 local = _owner.HasFanAnchor
                ? _owner.FanAnchorLocal
                : new Vector3(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);
            pos = _owner.BoardPosition + _owner.BoardRotation * (local * bs);
        }

        // Fronts (−Z) toward the owner, backs toward everyone else — the shared card convention.
        Transform? head = _owner.HeadHolder;
        if (head != null)
        {
            Vector3 away = pos - head.position;
            if (away.sqrMagnitude > 1e-6f)
                rot = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        return true;
    }

    /// <summary>Arc the slabs in fan-local space — the same reading arc <see cref="ItemsPile"/>
    /// lays its chips out on (capped sweep, capped per-card step, z-staggered for draw order),
    /// dealing them out of the <see cref="SeedEmerge"/> seed on the owner's own fly-out curve.</summary>
    private void Layout(int n, float dt)
    {
        float step = n > 1 ? Mathf.Min(MaxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;
        float mid = (n - 1) * 0.5f;

        // The fly-out clock. It runs until the LAST slab (the outermost pair) has finished its own
        // duration — not for a fixed settle window — so lengthening the stagger cannot silently
        // truncate the deal-out into a snap on the trailing cards.
        bool easing = _emergeElapsed >= 0f;
        if (easing)
        {
            _emergeElapsed += dt;
            if (_emergeElapsed >= mid * _openStagger + _openSeconds)
            {
                _emergeElapsed = -1f; // settled: assert the slots exactly from here on
                easing = false;
            }
        }

        // WHICH ITEM CHIP THE OWNER IS SINGLING OUT (extension record 6, byte 1 — hardware MP
        // test 2026-08-04: "Das Highlighting der Karten in den Fächern ist nicht synchronisiert").
        // ROOT CAUSE of that report: the SENDER has fed the item fan's HighlightedIndex into the
        // card-highlight record since build 36 (NetAvatarDriver reads ItemsPile.HighlightedIndex
        // when no pile browser is open — the two board fans share wire byte 1 because at most one
        // is ever open), and RemoteBrowserFan renders it for the browse arc — but THIS renderer
        // never consumed the index, so a peer sweeping their equipped items showed a flat arc on
        // every other screen. Same fix shape as RemoteBrowserFan.Layout: clamp the synced index
        // against OUR live slab count (a packet can arrive a frame off the count it was measured
        // against) and lift that one slab.
        int hovered = _owner.FanHighlightIndex;
        if (hovered < 0 || hovered >= _cards.Count)
            hovered = -1;

        for (int i = 0; i < _cards.Count; i++)
        {
            // The slab lying in the owner's use recess is NOT in this arc: it is a child of the
            // mirrored recess and its pose is the recess's own frame (see ResolveClip /
            // TickClipSettle). Writing an arc slot over it here would be the second, disagreeing
            // answer — the exact defect ItemsPile fixed locally by having Relayout skip its own
            // PendingUse chip.
            if (i == _clipIndex)
                continue;
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * Radius, (Mathf.Cos(rad) - 1f) * Radius, -ZStagger * i);
            Quaternion rot = Quaternion.Euler(0f, 0f, -angle);
            // The POP, exactly as the owner's own chip applies it (ItemsPile.ItemChip: PopLift
            // toward the viewer along the chip's local −Z, ×PopScale enlargement, eased at
            // PopLerpSpeed): reproduced from the synced INDEX alone — the ramp runs on the LOCAL
            // clock, so the wire carries a position and never an animation. −Z is toward the
            // owner's head here (the fan billboards its back at everyone else), which matches the
            // local chip popping toward ITS viewer.
            float popT = PopAmount(i, hovered, dt);
            if (popT > 0f)
                pos += rot * new Vector3(0f, 0f, -PopLift * popT);
            float scale = 1f + (PopScale - 1f) * popT;
            Transform t = _cards[i].transform;
            if (easing)
            {
                // The owner's own fly-out, term for term (ItemsPile.ItemChip.TickEmerge): a per-slab
                // clock offset by the centre-out ripple, an ease-out-BACK onto the arc slot, a
                // mid-flight bow along local −Z (toward the fan's own viewer, exactly as the local
                // chip bows toward its owner), growth out of the seed size, and an unfold roll that
                // eases on the CLAMPED progress — a card that overshoots its roll reads as a wobble.
                float u = Mathf.Clamp01((_emergeElapsed - Mathf.Abs(i - mid) * _openStagger) / _openSeconds);
                float e = EaseOutBack(u, _settleOvershoot);
                Vector3 seed = _emergeSeedLocal + new Vector3(0f, 0f, -ZStagger * i);
                Vector3 p = Vector3.LerpUnclamped(seed, pos, e);
                p.z -= _openArc * Mathf.Sin(u * Mathf.PI);
                t.localPosition = p;
                Quaternion spin = Quaternion.Euler(0f, 0f, SpinSign(i, mid) * _openSpinDegrees);
                t.localRotation = rot * Quaternion.Slerp(spin, Quaternion.identity, e);
                t.localScale = Vector3.one * Mathf.LerpUnclamped(_seedScale, scale, e);
            }
            else if (i == _returnIndex && _returnGlide > 0f)
            {
                // TAKE-BACK GLIDE — the mirror of ItemChip.ReturnToFan: the slab was just handed
                // back from the recess frame to the fan root keeping its world pose, so it starts
                // wherever it was lying and eases to its arc slot on the same exponential the
                // owner's card flies home on. Only the CLOCK is local; the motion is theirs.
                _returnGlide -= dt;
                float k = 1f - Mathf.Exp(-Defaults.CardLerpSpeed * Mathf.Max(dt, 0f));
                t.localPosition = Vector3.Lerp(t.localPosition, pos, k);
                t.localRotation = Quaternion.Slerp(t.localRotation, rot, k);
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one * scale, k);
                if (_returnGlide <= 0f)
                {
                    _returnGlide = 0f;
                    _returnIndex = -1; // landed: the plain assignment below owns it again
                }
            }
            else
            {
                t.localPosition = pos;
                t.localRotation = rot;
                t.localScale = Vector3.one * scale;
            }
        }
        LogHighlightIfChanged(hovered);
    }

    // ---------------------------------------------------------------- highlight (record 6) --

    /// <summary>ItemsPile.ItemChip.PopScale — the enlargement a lifted item chip takes locally
    /// (×1.18). Local copy of the authored value, like every geometry constant in this file.</summary>
    private const float PopScale = 1.18f;

    /// <summary>ItemsPile.ItemChip.PopLift — how far the lifted chip comes toward its viewer
    /// (local −Z, metres at chip scale 1).</summary>
    private const float PopLift = 0.02f;

    /// <summary>ItemsPile.ItemChip.PopLerpSpeed — the exponential ease rate of the local pop.</summary>
    private const float PopLerpSpeed = 16f;

    /// <summary>Per-slab pop ramp (0..1), index-aligned with <c>_cards</c> — per slab so a lift
    /// MOVING along the arc has the old chip relaxing while the new one rises, exactly like the
    /// owner's own sweep.</summary>
    private readonly List<float> _pop = new(MaxCards);

    /// <summary>Last highlighted index stated in the log (−2 = never).</summary>
    private int _loggedHighlight = -2;

    /// <summary>Advance and return slab <paramref name="i"/>'s pop ramp toward 1 while it is the
    /// highlighted chip and toward 0 otherwise, on the local chip's own exponential.</summary>
    private float PopAmount(int i, int hovered, float dt)
    {
        while (_pop.Count <= i)
            _pop.Add(0f);
        float target = i == hovered ? 1f : 0f;
        float t = Mathf.Lerp(_pop[i], target, 1f - Mathf.Exp(-PopLerpSpeed * Mathf.Max(dt, 0f)));
        // Snap the tail so a settled ramp stops writing transforms (the exponential never quite
        // arrives on its own).
        if (Mathf.Abs(t - target) < 0.005f)
            t = target;
        _pop[i] = t;
        return t;
    }

    /// <summary>Drop every pop ramp (fan closed / rebuilt) so a re-opened fan never starts with a
    /// stale chip already lifted.</summary>
    private void ClearPops()
    {
        for (int i = 0; i < _pop.Count; i++)
            _pop[i] = 0f;
        _loggedHighlight = -2;
    }

    /// <summary>Change-gated evidence that the synced item-fan highlight reached the render path
    /// (grep: "Remote item fan highlight").</summary>
    private void LogHighlightIfChanged(int hovered)
    {
        if (hovered == _loggedHighlight)
            return;
        _loggedHighlight = hovered;
        VRLog.Info("Net", $"Remote item fan highlight [{_owner.PlayerId}]: " +
                          $"index {(hovered >= 0 ? hovered.ToString() : "none")} of " +
                          $"{_cards.Count} slab(s) (wire index {_owner.FanHighlightIndex}) — " +
                          "the chip lifts on ItemsPile's own pop, from the INDEX alone " +
                          "(extension record 6: no item identity).");
    }

    // ------------------------------------------------------ the card in the use recess (26) --

    /// <summary>
    /// Reconcile the synced clip index (<see cref="RemoteAvatar.ItemUseClipIndex"/>) with what this
    /// fan is actually rendering, and act only on the EDGES: a card entering the recess is
    /// re-parented onto it and starts its settle, a card leaving is handed back to the fan root and
    /// starts its glide home. Runs before <see cref="Layout"/>, which must not write the clipped
    /// slab.
    ///
    /// <para>THREE THINGS ARE VALIDATED, and each is a way a peer's card could otherwise end up
    /// somewhere the owner's is not:</para>
    /// <list type="bullet">
    /// <item>the index is clamped against OUR live slab count — a packet can legitimately arrive a
    /// frame either side of a fan resize, which is the same reason the highlight index is clamped
    /// here and not at the reader;</item>
    /// <item>the recess must exist AND be active in the hierarchy. It is shown from the board-UI
    /// record's own bit and hidden with the whole board by the <c>[Net] RemoteBoards</c> gate, so a
    /// card must never be left lying on a recess this client is not drawing — it falls back to the
    /// arc, which is exactly what pre-record builds showed;</item>
    /// <item>the parent is RE-ASSERTED while the state holds, so a board rebuild (which destroys and
    /// re-creates the furniture, and with it the recess transform) re-adopts the card instead of
    /// leaving it orphaned in mid-air — the receiver-side twin of <c>TickPendingUse</c>'s own
    /// "re-assert after a board rebuild" line.</item>
    /// </list>
    /// </summary>
    private void ResolveClip()
    {
        Transform? recess = _owner.ItemUseRecess;
        bool recessUsable = recess != null && recess.gameObject.activeInHierarchy;

        int want = _owner.ItemUseClipIndex;
        if (want < 0 || want >= _cards.Count || !recessUsable)
            want = -1;

        if (want == _clipIndex)
        {
            // Steady state. The settle is over as far as the hierarchy is concerned, so the only
            // per-frame work is proving we still own the parent we think we own.
            if (_clipIndex >= 0 && recess != null)
            {
                Transform t = _cards[_clipIndex].transform;
                if (t.parent != recess)
                {
                    t.SetParent(recess, worldPositionStays: true);
                    _clipSettle = ClipSettleSeconds; // re-seat visibly, never a teleport
                }
            }
            return;
        }

        // LEAVING first, so a clip that MOVES from one fan position to another (the owner swaps the
        // placed card — the demand pick's own "a fresh drop replaces an older clip" rule) releases
        // the old slab before the new one is taken.
        if (_clipIndex >= 0)
            ReleaseClip(returnToArc: true);

        if (want >= 0 && recess != null)
        {
            Transform t = _cards[want].transform;
            // Keep the WORLD pose across the re-parent and ease into the recess frame from there —
            // the card is seen travelling out of the arc and into the recess, which is the same
            // statement the owner's own ClipIntoSlot makes ("everything that moves is seen moving").
            t.SetParent(recess, worldPositionStays: true);
            _clipIndex = want;
            _clipSettle = ClipSettleSeconds;
            _clipFitScale = RecessFitScale();
            _returnIndex = -1; // a slab cannot be gliding home and lying in the recess at once
            _returnGlide = 0f;
        }
        LogClipIfChanged(recessUsable);
    }

    /// <summary>
    /// Advance the arrival settle: ease the clipped slab's RECESS-LOCAL pose toward the recess's own
    /// frame (zero position, identity rotation, <see cref="_clipFitScale"/>), then land on it
    /// exactly and stop writing. Term for term <c>ItemsPile.ItemChip.TickClipSettle</c>, including
    /// the property that matters most: once the window closes the transform hierarchy holds the card
    /// rigidly with no per-frame work at all — no chase, no residual error, nothing to swim behind a
    /// moving board or a moving head.
    /// </summary>
    private void TickClipSettle(float dt)
    {
        if (_clipIndex < 0 || _clipSettle <= 0f || _clipIndex >= _cards.Count)
            return;
        Transform t = _cards[_clipIndex].transform;
        _clipSettle -= dt;
        if (_clipSettle <= 0f)
        {
            _clipSettle = 0f;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one * _clipFitScale;
            return;
        }
        float k = 1f - Mathf.Exp(-Defaults.CardLerpSpeed * Mathf.Max(dt, 0f));
        t.localPosition = Vector3.Lerp(t.localPosition, Vector3.zero, k);
        t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.identity, k);
        t.localScale = Vector3.Lerp(t.localScale, Vector3.one * _clipFitScale, k);
    }

    /// <summary>
    /// Hand the clipped slab back to the fan root KEEPING its world pose, so nothing jumps, and
    /// (when <paramref name="returnToArc"/>) start the glide home <see cref="Layout"/> runs for it.
    /// Every exit from the clipped state goes through here — the owner taking the card back, the
    /// recess disappearing, the fan closing, rebuilding or being destroyed — because the slab is a
    /// child of a transform this fan does NOT own, and a fan that hides its root while one of its
    /// slabs lives elsewhere leaves that slab hanging in the air.
    /// </summary>
    private void ReleaseClip(bool returnToArc)
    {
        int i = _clipIndex;
        _clipIndex = -1;
        _clipSettle = 0f;
        if (i < 0 || i >= _cards.Count || _root == null)
            return;
        GameObject card = _cards[i];
        if (card == null)
            return;
        card.transform.SetParent(_root.transform, worldPositionStays: true);
        if (!returnToArc)
            return;
        _returnIndex = i;
        _returnGlide = ReleaseGlideSeconds;
    }

    /// <summary>
    /// The recess-local scale a card lying in the recess comes to rest at: its own slab face fitted
    /// into THIS board's inner plate (<see cref="RemoteBoardFurniture.ItemUseInnerWidth"/>/Height),
    /// keeping its aspect, with the gold rim left showing. The receiver-side twin of
    /// <c>ItemsPile.UseSlotFitScale</c> — and derived the same way, from the RECESS in front of the
    /// card rather than from the fan's chip scale, which is what stops a near-square item face
    /// hanging over the tall card-shaped frame on either side.
    /// </summary>
    private static float RecessFitScale() =>
        Mathf.Min(RemoteBoardFurniture.ItemUseInnerWidth / CardW,
                  RemoteBoardFurniture.ItemUseInnerHeight / CardH) * UseSlotFillFraction;

    /// <summary>Change-gated evidence that the synced clip reached the render path (grep:
    /// "Remote item recess").</summary>
    private void LogClipIfChanged(bool recessUsable)
    {
        if (_clipIndex == _loggedClip)
            return;
        _loggedClip = _clipIndex;
        VRLog.Info("Net", $"Remote item recess [{_owner.PlayerId}]: " +
                          (_clipIndex >= 0
                              ? $"fan position {_clipIndex} of {_cards.Count} slab(s) now LIES IN the " +
                                $"mirrored item-use recess, settling over {ClipSettleSeconds:F2}s "
                              : "the recess is EMPTY again; the card glides back into the arc ") +
                          $"(wire index {_owner.ItemUseClipIndex}, extension record 26 — an arc " +
                          "POSITION, no item identity" +
                          (recessUsable ? ")." : "; this client is not drawing that recess right now)."));
    }

    // ------------------------------------------------------------------ emerge / collapse --

    /// <summary>
    /// Seed every chip ON the sender's ITEMS stack, shrunk to <see cref="_seedScale"/> and rolled by
    /// the owner's unfold angle, so the parametric ease in <see cref="Layout"/> deals them OUT of the
    /// pile — the wire-free replay of <c>ItemsPile.EmergeAll</c> + <c>ItemChip.BeginEmerge</c>. Falls
    /// back to the fan centre when the sender's board pose is unknown, so the worst case is a
    /// spread-open, never a pop-in.
    ///
    /// <para>The seed pose is written on the SAME frame the root is activated (see the caller), so a
    /// slab whose stagger delay has not elapsed yet is sitting on the stack from its first rendered
    /// frame — it never shows up in the arc and then jumps back. That is the receiver-side half of
    /// the owner's "nothing may pop" ruling.</para>
    /// </summary>
    private void SeedEmerge()
    {
        if (_root == null)
            return;
        _emergeSeedLocal = Vector3.zero;
        if (TryItemStackWorld(out Vector3 stackWorld))
            _emergeSeedLocal = _root.transform.InverseTransformPoint(stackWorld);
        for (int i = 0; i < _cards.Count; i++)
        {
            Transform t = _cards[i].transform;
            t.localPosition = _emergeSeedLocal + new Vector3(0f, 0f, -ZStagger * i); // keep the draw order stable
            t.localScale = Vector3.one * _seedScale;
            // ROTATION is deliberately not seeded here: the unfold roll is expressed relative to the
            // slab's ARC rotation, which only Layout knows, and Layout runs unconditionally later in
            // the SAME Tick call at progress 0 — i.e. it writes the exact seed pose before anything
            // is rendered. Seeding a rotation here would be a second, disagreeing answer.
        }
        _emergeElapsed = 0f;
    }

    /// <summary>Which way slab <paramref name="i"/> rolls out of the stack: outward from the fan
    /// centre, so the arc UNFOLDS rather than sliding open (<c>ItemsPile.EmergeAll</c>'s rule).</summary>
    private static float SpinSign(int i, float mid) => i - mid >= 0f ? 1f : -1f;

    /// <summary>Ease-out BACK — <c>ItemsPile.ItemChip.EaseOutBack</c> verbatim. <paramref name="s"/>
    /// = 0 degenerates to the plain ease-out cubic, which is what an owner who has turned the
    /// overshoot off is looking at.</summary>
    private static float EaseOutBack(float t, float s)
    {
        float u = t - 1f;
        return 1f + u * u * ((s + 1f) * u + s);
    }

    /// <summary>Ease-in BACK — the collapse's wind-up, <c>ItemsPile.ItemChip.EaseInBack</c> verbatim.</summary>
    private static float EaseInBack(float t, float s) => t * t * ((s + 1f) * t - s);

    /// <summary>
    /// Pull the owner's own item-fan ANIMATION dials out of their resolved tuning (extension record
    /// 28) when it has actually changed. Every dial they have not touched resolves to this client's
    /// shipped constant — the same number — so an untuned peer's fan opens exactly as this build
    /// ships it. No rebuild is ever needed: none of these changes the slab GEOMETRY, only the curve
    /// the slabs travel on, and that is recomputed every frame anyway.
    /// </summary>
    private void SyncTuning()
    {
        if (_tuningRevision == _owner.BoardTuningRevision)
            return;
        _tuningRevision = _owner.BoardTuningRevision;
        RemoteBoardTuning t = _owner.BoardTuning;
        _openSeconds = Mathf.Max(0.01f, t.ItemFanOpenDuration);
        _openStagger = Mathf.Max(0f, t.ItemFanOpenStagger);
        _openArc = Mathf.Max(0f, t.ItemFanOpenArc);
        _openSpinDegrees = t.ItemFanOpenSpinDegrees;
        _seedScale = Mathf.Clamp(t.ItemFanSeedScale, 0.02f, 1f);
        _settleOvershoot = Mathf.Clamp(t.ItemFanSettleOvershoot, 0f, 3f);
        _closeSeconds = Mathf.Max(0.01f, t.ItemFanCloseDuration);
        _closeStagger = Mathf.Max(0f, t.ItemFanCloseStagger);
    }

    /// <summary>
    /// Close edge: glide every chip back INTO the sender's items stack over
    /// <see cref="CollapseSeconds"/> instead of blinking the fan out — the replay of
    /// <c>ItemsPile.CollapseChips</c>. The root pose is frozen for the duration and the chips are
    /// driven in WORLD space, mirroring how the local chips are re-parented out of the fan root
    /// before they glide. Returns false (caller hides instantly) when there is nothing to collapse
    /// or no stack to collapse into.
    /// </summary>
    private bool BeginCollapse()
    {
        if (_root == null || !_root.activeSelf || _cards.Count == 0)
            return false;
        if (!TryItemStackWorld(out Vector3 stackWorld))
            return false;

        // A slab lying in the recess belongs to the RECESS's hierarchy, and the collapse drives
        // every slab in world space off a pose captured right here — so it comes home to the fan
        // root first (keeping its world pose, so the capture is unchanged) and folds into the stack
        // with the rest. Without this the fan would "close" while one card stayed in the recess.
        ReleaseClip(returnToArc: false);

        _emergeElapsed = -1f;
        _collapseTo = stackWorld;
        ClearCollapseCapture();
        for (int i = 0; i < _cards.Count; i++)
        {
            Transform t = _cards[i].transform;
            _collapseFrom.Add(t.position);
            _collapseFromRot.Add(t.rotation);
            _collapseFromScale.Add(t.localScale.x);
        }
        _collapseElapsed = 0f;

        float total = (_cards.Count - 1) * 0.5f * _closeStagger + _closeSeconds;
        VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closing — {_cards.Count} item card(s) " +
                          $"fold back into their items stack outermost-first ({total:F2}s total: " +
                          $"{_closeSeconds:F2}s each, {_closeStagger:F3}s per place), matching the local fan.");
        _loggedCount = 0; // Hide's own "closed" line is redundant with this one
        return true;
    }

    /// <summary>
    /// The fold-in, driven in WORLD space off the poses captured at the close — the replay of
    /// <c>ItemsPile.CollapseChips</c> + <c>ItemChip</c>'s collapse tick, including the presence pass:
    /// the REVERSE ripple (outermost slab first, so the close is the open played backwards), the
    /// ease-in-BACK wind-up (the slab lifts AWAY from the stack for a moment before it is pulled in
    /// — the anticipation that says where the card is about to go before it goes there), and the
    /// unfold roll wound back on.
    ///
    /// <para>A slab still waiting out its delay holds its captured pose exactly, because the curve is
    /// the identity at t = 0. Nothing is hidden and nothing is moved early.</para>
    /// </summary>
    private void TickCollapse(float dt)
    {
        _collapseElapsed += dt;
        int n = _cards.Count;
        float mid = (n - 1) * 0.5f;
        bool allDone = true;
        for (int i = 0; i < n && i < _collapseFrom.Count; i++)
        {
            // (mid − |i − mid|): the outermost pair starts at 0, the centre slab last — the exact
            // reverse of the fly-out's centre-out ripple.
            float delay = (mid - Mathf.Abs(i - mid)) * _closeStagger;
            float u = Mathf.Clamp01((_collapseElapsed - delay) / _closeSeconds);
            float e = EaseInBack(u, _settleOvershoot);
            Transform t = _cards[i].transform;
            t.position = Vector3.LerpUnclamped(_collapseFrom[i], _collapseTo, e);
            t.rotation = _collapseFromRot[i]
                       * Quaternion.Slerp(Quaternion.identity,
                                          Quaternion.Euler(0f, 0f, SpinSign(i, mid) * _openSpinDegrees), u);
            t.localScale = Vector3.one
                         * Mathf.LerpUnclamped(_collapseFromScale[i], _collapseFromScale[i] * _seedScale, e);
            if (u < 1f)
                allDone = false;
        }
        if (!allDone)
            return;
        _collapseElapsed = -1f;
        Hide();
    }

    /// <summary>World position of the sender's ITEMS stack, resolved through the SHARED board-local
    /// stack layout against their own synced board pose — the point the chips emerge from and
    /// collapse into, wherever that player parked their board.</summary>
    private bool TryItemStackWorld(out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition
                + _owner.BoardRotation * (_owner.BoardAnchorLocal(CardFxAnchor.Items) * bs);
        return true;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteItemFan[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        // Sized by the sender's rig scale so the fan reads the same physical size as their hands.
        _root.transform.localScale = Vector3.one * _owner.AppliedScale;
        _root.SetActive(false);
        _mesh = RemoteHandFan.BuildBackSlab(CardW, CardH);
        VRLayers.Apply(_root);
    }

    private void Rebuild(int count)
    {
        // The clipped slab is a child of the mirrored RECESS, not of this fan's root — bring it
        // home before the slabs are destroyed, and drop the clip state with them: the indices about
        // to be handed out address a different set of objects.
        ReleaseClip(returnToArc: false);
        _returnIndex = -1;
        _returnGlide = 0f;
        _loggedClip = -2;
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        ClearCollapseCapture(); // parallel to _cards — never let it outlive the slabs it indexed
        ClearPops();            // index-aligned with _cards too — a rebuilt arc starts flat

        Material back = CardMesh.CreateBackMaterial(); // SHARED cache — never ours to destroy
        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Item{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            var mf = card.AddComponent<MeshFilter>();
            mf.sharedMesh = _mesh;
            var mr = card.AddComponent<MeshRenderer>();
            mr.sharedMaterial = back;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
        }
        _builtCount = count;
        VRLayers.Apply(_root!);
        // Re-bind the front overlays onto the NEW slabs (the old ones died with their hosts above).
        // The card size handed over is the UNSCALED slab size: the overlay is a child of the slab, so
        // it inherits the emerge seed scale and the pop enlargement for free, like the slab's own mesh.
        _fronts.Rebuild(_cards, CardW, CardH);
    }

    private void Hide()
    {
        // FIRST, always: a slab parented to the mirrored recess is NOT under _root, so deactivating
        // the root would leave it lying on that board with no fan behind it. No glide home — the
        // fan is going away, and a card gliding into an arc nobody can see is worse than the card
        // simply not being there (the same argument the gate-hidden branch in Tick makes).
        ReleaseClip(returnToArc: false);
        _returnIndex = -1;
        _returnGlide = 0f;
        _loggedClip = -2;
        _fronts.HideAll(); // a hidden fan keeps no game-widget clones alive
        if (_loggedCount > 0)
        {
            _loggedCount = 0;
            VRLog.Info("Net", $"Remote ITEM fan [player {_owner.PlayerId}]: closed.");
        }
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
        _emergeElapsed = -1f;   // next appearance emerges out of the stack again
        _collapseElapsed = -1f;
        ClearCollapseCapture();
        ClearPops();            // a re-opened fan never starts with a stale chip lifted
    }

    public void Destroy()
    {
        _fronts.Destroy();
        // The clipped slab hangs off the mirrored recess, so destroying _root would not take it
        // with it — it has to be destroyed in its own right or it outlives the whole fan on that
        // peer's board.
        if (_clipIndex >= 0 && _clipIndex < _cards.Count && _cards[_clipIndex] != null)
            Object.Destroy(_cards[_clipIndex]);
        _clipIndex = -1;
        _clipSettle = 0f;
        _returnIndex = -1;
        _returnGlide = 0f;
        _cards.Clear();
        ClearCollapseCapture();
        _builtCount = -1;
        if (_mesh != null)
            Object.Destroy(_mesh); // asset — not freed with the GameObject tree
        _mesh = null;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }
}
