// HELD-PROP SYNC — the map items in a player's hands, mirrored on every peer (cosmetic,
// desync-safe).
//
// User, 2026-09-05, verbatim: "Das Aufnehmen der Props wird im Multiplayer nicht synchronisiert,
// sie sollen wie die Figuren vollständig synchronisiert werden mit allem drum und dran (mach da
// keinen Unterschied zwischen Figuren und Props!)."
//
// This file is the prop-side twin of NetFigures: send, receive and interpolation for extension
// record 37 (NetProtocol.ExtIdHeldProp). It is PURELY COSMETIC in the same strict sense that file
// is — a prop's board position is authoritative game state that the rule library owns, and nothing
// here ever writes it. What is mirrored is a presentation fact ("this player is carrying that chest
// in their left hand, that big, right there"), and it is torn down the instant the record stops
// arriving.
//
// THE ONE STRUCTURAL DIFFERENCE FROM THE FIGURE PATH, and it is the whole reason this is not a copy
// of NetFigures with a different noun: A FIGURE HEALS ITSELF AND A PROP DOES NOT. Every client
// re-derives a figure's transform from board state every frame (ActorBehaviour.Update → DoTransform,
// LateUpdate → ApplyMotion), which is why the figure receiver only has to STOP driving a released
// mini and why NetFigures.RestoreHomeScale restores the one channel the game will not re-author.
// Nothing re-places a prop at all: Choreographer places it once at spawn (SpawnProp:13159,
// PlaceRandomProps:15459) and every other prop write in it sits inside a message handler. So this
// receiver owns the whole restore — position, rotation AND scale — and a release path that forgot
// it would leave a peer's chest hanging in mid-air for the rest of the scenario.

using System.Collections.Generic;
using Apparance.Unity;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Held-prop sync. The send side samples the props the local player physically holds
/// (<see cref="HeldProps"/>) as STABLE cross-client prop ids, world poses and measured held sizes.
/// The receive side maps each id back to the local prop visual, drives it toward the pose and size
/// the grabber sends, leaves a home ghost on its board hex, and hands it back — pose, rotation and
/// scale — the moment the record stops arriving.
///
/// <para><b>TWO PROPS PER PLAYER, one per hand</b>, in the grab order <see cref="HeldProps"/>
/// defines, both slots inside record 37. That is the same two-handed capability the figure path has
/// and the user's ruling is explicit that there is to be no difference between the two families.
/// A slot is pinned to its prop for the whole hold, so grabbing a second chest cannot move the
/// first one into the other slot mid-stream.</para>
///
/// <para><b>IDENTITY is <see cref="StablePropId"/></b> — FNV-1a-32 of <c>CObjectProp.PropGuid</c>,
/// the exact analogue of <c>NetFigures.StableActorId</c> over <c>ActorGuid</c>. Send and receive
/// funnel through the one function so the derivation cannot diverge between the sampler and the
/// lookup.</para>
///
/// <para><b>THE FREEZE IS NOT OPTIONAL ON THE RECEIVE SIDE</b> (the ModBuild 349 correction, and
/// the reason a naive mirror would have shipped a flickering chest to every peer). A prop's root
/// carries an <c>ApparanceEntity</c> whose <c>MonitorMovement</c> makes it destroy and rebuild the
/// prop's whole generated content whenever the transform changes — the local hold learned that over
/// three rounds of "das Item flackert in der Hand". This driver writes a remotely-held prop's
/// transform every frame, which is the identical stimulus, so it applies the identical suppression
/// (<see cref="Freeze"/>) and the same animation belt the local hold engages.</para>
///
/// <para>Everything is a strict no-op offline and in single player: nothing is sampled unless a
/// prop is locally held, and nothing is driven unless a modded VR peer reports one.</para>
/// </summary>
internal static class NetProps
{
    /// <summary>Wire slot of the FIRST held prop — the oldest still-held one, in grab order.</summary>
    public const int SlotPrimary = 0;

    /// <summary>Wire slot of the SECOND held prop — the one in the player's other hand.</summary>
    public const int SlotSecondary = 1;

    /// <summary>How many wire slots record 37 can carry. Two, because a player has two hands and a
    /// <c>ProximityGrabber</c> holds one object per hand.</summary>
    public const int SlotCount = 2;

    private sealed class RemoteHeld
    {
        public int PropId;
        public CObjectProp? Prop;
        public GameObject Visual = null!;

        public Vector3 TargetPos;
        public Quaternion TargetRot = Quaternion.identity;

        /// <summary>Which of the grabber's hands carries this item. Every slot of record 37 carries
        /// its own hand bit (unlike the figure family, where the first figure's hand had to be
        /// smuggled into the second figure's record), so this is always known.</summary>
        public bool LeftHand;

        /// <summary>The prop's LOCAL pose on its board hex, captured on the frame this hold became
        /// fresh — before anything here has moved it. THE WHOLE RESTORE, and it must be local
        /// rather than world: the board root carries the diorama zoom, so a peer who zooms during
        /// the hold would otherwise get their chest back at the wrong place and size.</summary>
        public Vector3 HomeLocalPos;
        public Quaternion HomeLocalRot = Quaternion.identity;
        public Vector3 HomeLocalScale = Vector3.one;

        /// <summary>The holder's HELD SIZE for this slot as it last arrived — the prop's rendered
        /// size as a multiple of its own board-home size, measured on the holder's machine. 1 until
        /// a record says otherwise, which is also what a prop held at board size means.</summary>
        public float StretchTarget = 1f;

        /// <summary>The size this client is currently RENDERING, eased toward
        /// <see cref="StretchTarget"/> with the same sharpness as the pose — the record arrives at
        /// packet cadence and a raw write would step visibly.</summary>
        public float StretchApplied = 1f;

        /// <summary>True once this hold has written the prop's transform at least once, i.e. once
        /// the restore below has something to undo.</summary>
        public bool PoseTouched;

        /// <summary>The <c>ApparanceEntity</c> components suppressed for this hold and the
        /// <c>MonitorMovement</c> each of them had — see <see cref="Freeze"/>.</summary>
        public ApparanceEntity[]? Frozen;
        public bool[]? FrozenMonitor;

        /// <summary>Throttle for this record's MIRROR line — the last size logged and when.</summary>
        public float LoggedFactor = float.NaN;
        public float LoggedAt = -999f;
    }

    /// <summary>Both wire slots of ONE remote player. Independent: a peer carrying something in each
    /// hand fills both, putting one down clears only that one.</summary>
    private sealed class PlayerHeld
    {
        public RemoteHeld? Primary;
        public RemoteHeld? Secondary;
    }

    private static readonly Dictionary<int, PlayerHeld> _byPlayer = new();

    // propId → the local prop and its visual. Rebuilt lazily on a miss or a dead entry, from
    // ScenarioState.Props (the population) resolved through PropVisualLookup (the visual) — the
    // same two sources PropGrab's own discovery uses, so a remotely-held prop is always the very
    // object a local grab would have lifted.
    private static readonly Dictionary<int, CObjectProp> _propById = new();
    private static readonly Dictionary<int, GameObject> _visualById = new();

    private static readonly List<int> _scratchPlayers = new();
    private static readonly List<int> _scratchIds = new(4);
    private static readonly List<GameObject> _scratchVisuals = new(4);

    private static bool _loggedMirror;

    // ---- send ---------------------------------------------------------------------------

    /// <summary>
    /// Send side: the prop in wire slot <paramref name="slot"/> — its stable id, world pose, and
    /// whether the LEFT hand carries it. False when the local player holds fewer props than that,
    /// when the prop has no stable id, or when its visual is gone.
    ///
    /// <para>Slot order is grab order and therefore fixed for the lifetime of a hold
    /// (<see cref="HeldProps.TryGetSlot"/>): picking up a second item must not move the first one
    /// into the other wire slot, and putting the second down must not move the first at all.</para>
    /// </summary>
    public static bool TrySampleHeldSlot(int slot, out int propId, out Vector3 pos,
                                         out Quaternion rot, out bool leftHand)
    {
        propId = 0;
        pos = default;
        rot = Quaternion.identity;
        leftHand = false;

        if (!HeldProps.TryGetSlot(slot, out CObjectProp prop, out GameObject visual,
                                  out HandSide side, out _))
            return false;
        propId = StablePropId(prop);
        if (propId == 0)
            return false;

        Transform t = visual.transform;
        pos = t.position;
        rot = t.rotation;
        leftHand = side == HandSide.Left;
        return true;
    }

    /// <summary>
    /// Send side: THE WHOLE HELD SIZE of the prop in wire slot <paramref name="slot"/>, as a
    /// multiple of that prop's own board-home size — measured straight off the transform the holder
    /// is looking at, exactly as <c>FigureGrabbable.HeldSizeFactorOf</c> measures a mini's. 1.0 when
    /// the slot holds nothing, when the home size was not capturable, or when the item is simply at
    /// board size.
    ///
    /// <para><b>MEASURED, NEVER RE-DERIVED, and that is the one lesson the figure path paid for.</b>
    /// The obvious alternative here is to send <c>GrabbableProp.TotalHeldSizeRatio</c> — the latch
    /// times the gesture — and have the peer rebuild the rest. That is precisely the mechanism the
    /// 3-player hardware session of 2026-08-15 removed from the figure path ("Die Größe einer Figur
    /// MUSS zwingend immer 1:1 genau die sein die der Spieler auch in der Hand hat"), because a
    /// reconstruction cannot see the grab-time size clamp and takes its zoom base from the wrong
    /// machine at the wrong instant. <c>lossyScale ÷ homeWorldScale</c> has all three folded in by
    /// construction, so the peer's size is a pure function of one transmitted number.</para>
    /// </summary>
    public static float SampleHeldStretch(int slot)
    {
        if (!HeldProps.TryGetSlot(slot, out _, out GameObject visual, out _, out float home))
            return 1f;
        if (home <= 1e-6f)
            return 1f; // degenerate capture: report "board size" rather than divide by it
        float factor = visual.transform.lossyScale.x / home;
        return float.IsNaN(factor) || float.IsInfinity(factor) || factor <= 0f ? 1f : factor;
    }

    // ---- receive ------------------------------------------------------------------------

    /// <summary>
    /// Receive side: remote <paramref name="playerId"/> holds prop <paramref name="propId"/> in wire
    /// slot <paramref name="slot"/>, at the given world pose and at <paramref name="stretch"/> times
    /// its own board-home size. Resolves the id to the local prop, captures its home pose, spawns
    /// the home ghost, suppresses its Apparance rebuild and starts driving it.
    ///
    /// <para>An id this client cannot resolve (a room it has not revealed, a prop destroyed here)
    /// clears only THAT slot, so the peer's other hand keeps what it is carrying.</para>
    ///
    /// <para><b>A LOCALLY-HELD PROP IS NEVER ADOPTED.</b> Two hands on two machines cannot hold one
    /// object, and on this machine the local player's hand is the fact — the remote grab-lock in
    /// <c>GrabbableProp.CanGrab</c> is what normally prevents the collision, and this is the guard
    /// for the packet that crosses it in flight. Adopting it anyway would put two drivers on one
    /// transform and, worse, double-book the Apparance freeze: the second capture would record
    /// <c>MonitorMovement</c> as already-false and the restore would strand the prop unable to
    /// rebuild for the rest of the session.</para>
    /// </summary>
    public static void ApplyRemoteHeld(int playerId, int slot, int propId, Vector3 pos,
                                       Quaternion rot, bool leftHand, float stretch)
    {
        if (!TryResolve(propId, out CObjectProp prop, out GameObject visual)
            || HeldLocally(propId))
        {
            ReleaseRemoteSlot(playerId, slot);
            return;
        }

        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
        {
            player = new PlayerHeld();
            _byPlayer[playerId] = player;
        }

        // THE DUPLICATE IS REFUSED BEFORE A RECORD IS BUILT, not deduplicated afterwards: creating
        // one would capture a "home" pose off a prop the OTHER slot has already carried into the
        // air, and would double-book the Apparance freeze on the same entities.
        if (slot == SlotSecondary && player.Primary != null && player.Primary.PropId == propId)
        {
            ReleaseRemoteSlot(playerId, SlotSecondary);
            return;
        }

        RemoteHeld? rec = slot == SlotSecondary ? player.Secondary : player.Primary;

        // A slot whose PROP CHANGED is a new hold, not a continuing one: the peer swapped items, or
        // a state sync re-keyed this one and the lookup handed back a different visual. The outgoing
        // prop must be put back on its hex — nothing else will ever do it — and the incoming one must
        // have its OWN home pose captured, or it inherits the previous item's and is restored onto
        // the wrong hex when the hold ends.
        if (rec != null && (rec.PropId != propId || !ReferenceEquals(rec.Visual, visual)))
        {
            RestoreHome(rec);
            rec = null;
            if (slot == SlotSecondary) player.Secondary = null;
            else player.Primary = null;
        }

        if (rec == null)
        {
            rec = new RemoteHeld { PropId = propId, Visual = visual };
            if (slot == SlotSecondary) player.Secondary = rec;
            else player.Primary = rec;

            Transform home = visual.transform;

            // THE HOME GHOST, from the board pose, BEFORE anything moves the prop — the same order
            // GrabbableProp.OnGrab uses locally, and the same idempotent builder. The user asked for
            // the ghost as part of the prop hold ("wenn man es in der Hand hat einen Geist
            // hinterlassen"), and "wie die Figuren" makes it part of the mirror too.
            PropGhosts.NotifyHeld(prop, visual, home.position, home.rotation, home.lossyScale);

            rec.HomeLocalPos = home.localPosition;
            rec.HomeLocalRot = home.localRotation;
            rec.HomeLocalScale = home.localScale;

            // Seed the rendered size to what arrived rather than easing up from board size: the
            // hold and its size are ONE record here, so the factor is never late and a ramp would
            // simply be a wrong size the peer can see.
            rec.StretchTarget = stretch;
            rec.StretchApplied = stretch;

            Freeze(rec);
            PropAnimBelt.Engage(visual, Label(prop));
        }

        rec.Prop = prop;
        rec.TargetPos = pos;
        rec.TargetRot = rot;
        rec.LeftHand = leftHand;
        rec.StretchTarget = stretch;

        RebuildSet();
    }

    /// <summary>Is the LOCAL player holding this prop? Asked by stable id rather than by reference,
    /// because a state sync replaces every <c>CObjectProp</c> instance on the board (see
    /// <c>PropVisualLookup</c>) and a reference compare would answer "no" for the very prop in the
    /// player's own hand. At most two slots.</summary>
    private static bool HeldLocally(int propId)
    {
        for (int slot = 0; slot < SlotCount; slot++)
        {
            if (HeldProps.TryGetSlot(slot, out CObjectProp p, out _) && StablePropId(p) == propId)
                return true;
        }
        return false;
    }

    /// <summary>Receive side: <paramref name="playerId"/> put down whatever was in one wire slot.
    /// That prop goes back on its hex; anything in the other slot keeps riding their other
    /// hand.</summary>
    public static void ReleaseRemoteSlot(int playerId, int slot)
    {
        if (!_byPlayer.TryGetValue(playerId, out PlayerHeld player))
            return;

        if (slot == SlotSecondary)
        {
            if (player.Secondary == null)
                return;
            RestoreHome(player.Secondary);
            player.Secondary = null;
        }
        else
        {
            if (player.Primary == null)
                return;
            RestoreHome(player.Primary);
            player.Primary = null;
        }

        if (player.Primary == null && player.Secondary == null)
            _byPlayer.Remove(playerId);
        RebuildSet();
    }

    /// <summary>Receive side: <paramref name="playerId"/> released EVERYTHING (peer left, went
    /// stale, switched to flat, session torn down). Both slots go, so a departing player can never
    /// leave a chest floating where their hand was.</summary>
    public static void ReleaseRemote(int playerId)
    {
        if (_byPlayer.TryGetValue(playerId, out PlayerHeld leaving))
        {
            RestoreHome(leaving.Primary);
            RestoreHome(leaving.Secondary);
        }
        if (_byPlayer.Remove(playerId))
            RebuildSet();
    }

    /// <summary>Scenario teardown / module shutdown: every remotely-held prop goes home NOW, and
    /// every pending thaw is completed rather than dropped — a suppressed <c>MonitorMovement</c>
    /// that outlives the driver would leave that prop unable to rebuild for the rest of the
    /// session.</summary>
    public static void Clear()
    {
        foreach (PlayerHeld player in _byPlayer.Values)
        {
            RestoreHome(player.Primary);
            RestoreHome(player.Secondary);
        }
        _byPlayer.Clear();
        _propById.Clear();
        _visualById.Clear();
        _loggedMirror = false; // a new session earns its own HW-VERIFY line
        NetHeldProps.Clear();
    }

    // ---- interpolation ------------------------------------------------------------------

    /// <summary>Per-frame ease of every remotely-held prop toward its target pose and size
    /// (exponential Lerp/Slerp, the same <c>InterpolationSharpness</c> a held figure and a remote
    /// hand get — same cadence in, same easing, therefore the same motion). Prunes a slot whose
    /// visual was destroyed, and completes any pending Apparance thaw. No-op when no remote prop is
    /// held and no thaw is pending.</summary>
    public static void Tick()
    {
        if (_byPlayer.Count == 0)
            return;

        float dt = Mathf.Max(Time.unscaledDeltaTime, 0f);
        float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);

        _scratchPlayers.Clear();
        bool pruned = false;
        foreach (KeyValuePair<int, PlayerHeld> kv in _byPlayer)
        {
            PlayerHeld player = kv.Value;
            if (!EaseSlot(player.Primary, kv.Key, k)) { RestoreHome(player.Primary); player.Primary = null; pruned = true; }
            if (!EaseSlot(player.Secondary, kv.Key, k)) { RestoreHome(player.Secondary); player.Secondary = null; pruned = true; }
            if (player.Primary == null && player.Secondary == null)
                _scratchPlayers.Add(kv.Key);
        }

        for (int i = 0; i < _scratchPlayers.Count; i++)
            _byPlayer.Remove(_scratchPlayers[i]);
        if (pruned)
            RebuildSet();
    }

    /// <summary>Ease one slot toward its target. False when the slot is gone (empty, or its visual
    /// was destroyed) and must be dropped.</summary>
    private static bool EaseSlot(RemoteHeld? rec, int playerId, float k)
    {
        if (rec == null)
            return false;
        GameObject visual = rec.Visual;
        if (visual == null)
            return false; // the prop was destroyed under us → drop this hold
        Transform t = visual.transform;

        t.position = Vector3.Lerp(t.position, rec.TargetPos, k);
        t.rotation = Quaternion.Slerp(t.rotation, rec.TargetRot, k);

        // THE SIZE: the holder's own measured factor times THIS client's copy of the prop's
        // board-home local scale, and nothing else. Same rule as the figure mirror, and for the same
        // reason — the home scale is authoritative game state, identical on every machine, so a size
        // built from it plus one transmitted number cannot drift. Eased with the pose's own k
        // because the factor arrives at packet cadence.
        rec.StretchApplied = Mathf.Lerp(rec.StretchApplied, rec.StretchTarget, k);
        Vector3 want = rec.HomeLocalScale * rec.StretchApplied;
        if (t.localScale != want)
            t.localScale = want;
        rec.PoseTouched = true;

        // RE-ASSERT THE HOME GHOST, idempotently (a dictionary lookup when it already stands). It is
        // built once at the grab, but the store it lives in can be cleared out from under a remote
        // hold — PropGrab.ReleaseAll calls PropGhosts.Clear whenever the local player toggles
        // [FigureGrab] GrabFigures off, and that hold is not theirs to end. Rebuilding it from the
        // home LOCAL pose kept on this record makes the ghost survive that, and survive a diorama
        // zoom, without asking PropGrab for anything.
        Transform? parent = t.parent;
        Vector3 ghostPos = parent != null ? parent.TransformPoint(rec.HomeLocalPos) : rec.HomeLocalPos;
        Quaternion ghostRot = parent != null ? parent.rotation * rec.HomeLocalRot : rec.HomeLocalRot;
        Vector3 ghostScale = parent != null
            ? Vector3.Scale(parent.lossyScale, rec.HomeLocalScale)
            : rec.HomeLocalScale;
        PropGhosts.NotifyHeld(rec.Prop, visual, ghostPos, ghostRot, ghostScale);

        LogMirror(rec, playerId);
        return true;
    }

    // ---- the mirror diagnostic ----------------------------------------------------------

    private const float MirrorQuietPeriodSeconds = 5f;
    private const float MirrorReportableChange = 0.02f;

    /// <summary>
    /// ONE LINE PER REMOTE HOLD, naming the prop by its cross-client STABLE ID, so the holder's log
    /// and every mirror's log can be lined up on one grep at one timestamp — the same discipline the
    /// figure family's SIZE SYNC pair established after a hardware round could not tell "wrong on
    /// the wire" from "wrong after it arrived".
    /// </summary>
    private static void LogMirror(RemoteHeld rec, int playerId)
    {
        float now = Time.unscaledTime;
        bool changed = float.IsNaN(rec.LoggedFactor)
                       || Mathf.Abs(rec.StretchApplied - rec.LoggedFactor)
                          > MirrorReportableChange * Mathf.Max(rec.LoggedFactor, 1e-3f);
        if (!changed && now - rec.LoggedAt < MirrorQuietPeriodSeconds)
            return;
        bool first = !_loggedMirror;
        rec.LoggedFactor = rec.StretchApplied;
        rec.LoggedAt = now;

        string text = $"[Props] HELD-PROP MIRROR player {playerId} prop {rec.PropId} "
            + $"'{(rec.Prop != null ? rec.Prop.PrefabName : "?")}' in their "
            + $"{(rec.LeftHand ? "LEFT" : "RIGHT")} hand: wire size {rec.StretchTarget:0.####}× → "
            + $"rendering {rec.StretchApplied:0.####}× × board-home {rec.HomeLocalScale.x:0.######} "
            + $"= localScale {(rec.HomeLocalScale.x * rec.StretchApplied):0.######}; "
            + $"{NetHeldProps.Count} prop(s) held remotely. "
            + "The factor is the HOLDER's own measured size (record 37, field 4) and is the only "
            + "size input used here, so if their machine's [Size] line for this prop shows the same "
            + "number at this moment the sync is 1:1 and any remaining difference is their board "
            + "scale, not the wire. NO line at all while a peer is visibly carrying something means "
            + "the record never arrived — read the extras cadence, not this file.";

        if (first)
        {
            _loggedMirror = true;
            // HW-VERIFY: the first mirror of a session is the proof that the whole held-prop sync
            // arrived at all, and it is the line the 2026-09-05 report is answered by. It must stay
            // at a tier the DEFAULT log level prints (Note/Alert/Error) — scripts/check-hw-verify.py
            // enforces it. Once per session only; the throttled repeats below are Info.
            VRLog.Note("Net", text + " (First remote prop hold this session; the rest of this "
                + "hold's curve is logged at debug level.)");
            return;
        }
        VRLog.Info("Net", text);
    }

    // ---- helpers ------------------------------------------------------------------------

    /// <summary>Rebuild <see cref="NetHeldProps"/> as the union of every player's held props — keeps
    /// the grab lock, the home ghosts and the wall-fade exemption correct when peers hold different
    /// props, when one peer holds two, or when two peers somehow name the same one.</summary>
    private static void RebuildSet()
    {
        _scratchIds.Clear();
        _scratchVisuals.Clear();
        foreach (PlayerHeld player in _byPlayer.Values)
        {
            Collect(player.Primary);
            Collect(player.Secondary);
        }
        NetHeldProps.ReplaceWith(_scratchIds, _scratchVisuals);
    }

    private static void Collect(RemoteHeld? rec)
    {
        if (rec == null || rec.Visual == null)
            return;
        _scratchIds.Add(rec.PropId);
        _scratchVisuals.Add(rec.Visual);
    }

    /// <summary>
    /// PUT A RELEASED PROP BACK ON ITS HEX — position, rotation and scale, from the LOCAL pose
    /// captured when the hold began.
    ///
    /// <para><b>THIS IS THE PATH THAT DOES NOT HEAL ITSELF, and it is the structural difference from
    /// the figure mirror.</b> A released figure is re-authored by the game's own Update on the very
    /// next frame, so <c>NetFigures</c> only has to restore the one channel the game never writes
    /// (scale). NOTHING re-places a prop — the game puts it down once at spawn and never looks at
    /// its transform again — so every channel this driver wrote is a channel only this driver can
    /// undo. A release path that skipped it would leave a peer's chest hanging in the air where
    /// their hand was, for the rest of the scenario.</para>
    ///
    /// <para><b>THE THAW IS IMMEDIATE HERE, unlike the local hold's</b>, and the difference is the
    /// glide. <c>GrabbableProp.ScheduleThaw</c> waits three frames because a locally released prop
    /// keeps MOVING — it eases home — so handing <c>MonitorMovement</c> back at once would re-arm
    /// the rebuild trigger in the middle of that motion. A remote release is ONE write of the home
    /// pose and then nothing, so there is no settling to wait for, and waiting would cost more than
    /// it saves: the local player can grab the very prop a peer just put down within those three
    /// frames (the remote grab-lock lifts on this same call), and <c>FreezeApparance</c> would then
    /// capture the SUPPRESSED value as if it were the authored one and hand back <c>false</c> for
    /// ever — a prop that can never rebuild again for the rest of the session. One rebuild on the
    /// cell is the price of not having that race, and it is the right one.</para>
    /// </summary>
    private static void RestoreHome(RemoteHeld? rec)
    {
        if (rec == null)
            return;
        PropAnimBelt.Release(rec.Visual);
        // The ghost comes down HERE rather than through PropGhosts.Tick's reconcile, because that
        // reconcile is called below the local [FigureGrab] GrabFigures gate and a peer's hold is not
        // something a local dial gets a vote on. See PropGhosts.NotifyRemoteReleased.
        PropGhosts.NotifyRemoteReleased(rec.Prop);
        GameObject visual = rec.Visual;
        if (visual != null && rec.PoseTouched)
        {
            Transform t = visual.transform;
            t.localPosition = rec.HomeLocalPos;
            t.localRotation = rec.HomeLocalRot;
            t.localScale = rec.HomeLocalScale;
        }
        Thaw(rec);
    }

    /// <summary>
    /// Turn Apparance's rebuild-on-move off for the length of a remote hold — the receive-side twin
    /// of <c>GrabbableProp.FreezeApparance</c>, and not a call into it: that method is per-INSTANCE
    /// hold state on a grabbable this client is not holding, so there is no instance to call it on.
    /// The rule it implements is the same one, quoted from the plugin's own tooltip on that field:
    /// "By default, transforming an Entity causes a re-build if it's procedural content".
    /// </summary>
    private static void Freeze(RemoteHeld rec)
    {
        if (rec.Frozen != null || rec.Visual == null)
            return;
        ApparanceEntity[] entities = rec.Visual.GetComponentsInChildren<ApparanceEntity>(includeInactive: true);
        rec.Frozen = entities;
        rec.FrozenMonitor = new bool[entities.Length];
        for (int i = 0; i < entities.Length; i++)
        {
            ApparanceEntity e = entities[i];
            if (e == null)
                continue;
            rec.FrozenMonitor[i] = e.MonitorMovement;
            e.MonitorMovement = false;
        }
    }

    /// <summary>Hand every captured <c>MonitorMovement</c> back. Idempotent.</summary>
    private static void Thaw(RemoteHeld rec)
    {
        ApparanceEntity[]? entities = rec.Frozen;
        bool[]? monitor = rec.FrozenMonitor;
        rec.Frozen = null;
        rec.FrozenMonitor = null;
        if (entities == null || monitor == null)
            return;
        for (int i = 0; i < entities.Length && i < monitor.Length; i++)
        {
            ApparanceEntity e = entities[i];
            if (e != null)
                e.MonitorMovement = monitor[i];
        }
    }

    private static string Label(CObjectProp? prop)
        => prop != null ? $"'{prop.PrefabName}' {prop.ObjectType}" : "'?'";

    /// <summary>
    /// The cross-client-stable prop id: FNV-1a(32) of the game's replicated
    /// <c>CObjectProp.PropGuid</c>. 0 = unknowable (no prop, no guid), which every wire field
    /// already treats as "nothing held". Send and receive funnel through this one function so the
    /// derivation cannot diverge between the sampler and the lookup.
    /// </summary>
    internal static int StablePropId(CObjectProp? prop)
    {
        if (prop == null)
            return 0;
        string? guid = null;
        try { guid = prop.PropGuid; } catch { /* mid-teardown prop — fall through */ }
        return string.IsNullOrEmpty(guid) ? 0 : Fnv1a32(guid!);
    }

    /// <summary>FNV-1a, 32-bit, over the guid's UTF-16 code units — deterministic across machines
    /// and runs, unlike <c>string.GetHashCode</c>, whose value is process-randomizable. Byte-for-byte
    /// the function <c>NetFigures</c> hashes actor guids with, so the two id spaces are built the
    /// same way even though they never mix. Zero is remapped so an id of 0 can keep meaning "nothing
    /// held" everywhere the wire defaults it.</summary>
    private static int Fnv1a32(string s)
    {
        unchecked
        {
            uint hash = 2166136261u;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 16777619u;
            }
            int id = (int)hash;
            return id != 0 ? id : 1;
        }
    }

    /// <summary>Map a stable prop id to the local prop and its visual, rebuilding the cache on a
    /// miss or a dead entry. False when no live prop matches — a room this client has not revealed,
    /// a prop destroyed here, or one whose visual has not spawned yet.</summary>
    private static bool TryResolve(int propId, out CObjectProp prop, out GameObject visual)
    {
        prop = null!;
        visual = null!;
        if (propId == 0)
            return false;

        if (_propById.TryGetValue(propId, out CObjectProp cachedProp)
            && _visualById.TryGetValue(propId, out GameObject cachedVisual)
            && cachedVisual != null)
        {
            prop = cachedProp;
            visual = cachedVisual;
            return true;
        }

        RebuildLookup();
        if (!_propById.TryGetValue(propId, out prop!)
            || !_visualById.TryGetValue(propId, out visual!) || visual == null)
        {
            prop = null!;
            visual = null!;
            return false;
        }
        return true;
    }

    /// <summary>
    /// Re-index every prop the scenario currently states, by stable id.
    ///
    /// <para>The visual comes from <see cref="PropVisualLookup"/> and not from
    /// <c>ObjectCacheService.GetPropObject</c>, for the reason that class was written: the game's
    /// cache is keyed by object REFERENCE and a state sync hands back fresh <c>CObjectProp</c>
    /// instances for the same board, after which <c>GetPropObject</c> misses for every prop for the
    /// rest of the scenario — the 2026-09-03 "no prop can be grabbed" report. A lookup that stops
    /// answering here would drop a peer's carried prop back onto the board mid-hold, which is the
    /// same defect wearing the multiplayer costume. It is also silent on a miss, so a remote id this
    /// client cannot place does not fill the log with the game's own warnings.</para>
    ///
    /// <para>No <c>FindObjectsOfType</c> anywhere: the population is a list the scenario already
    /// holds, and this runs only on a cache miss.</para>
    /// </summary>
    private static void RebuildLookup()
    {
        _propById.Clear();
        _visualById.Clear();

        ScenarioState? state = ScenarioManager.CurrentScenarioState;
        List<CObjectProp>? props = state != null ? state.Props : null;
        if (props == null)
            return;

        for (int i = 0; i < props.Count; i++)
        {
            CObjectProp prop = props[i];
            int id = StablePropId(prop);
            if (id == 0)
                continue;
            // Last write wins, exactly as in the figure lookup: ids are guid hashes, so a collision
            // is a genuine 2^-32 accident rather than a systematic overlap, and the consequence is
            // cosmetic.
            _propById[id] = prop;
            GameObject? visual = PropVisualLookup.Resolve(prop, out _);
            if (visual != null)
                _visualById[id] = visual;
        }
    }
}
