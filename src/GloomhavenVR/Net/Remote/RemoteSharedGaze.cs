using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// WHERE THE PARTY IS LOOKING — ONE DECISION, MADE BY THE HOST, PUBLISHED AS ONE BYTE.
///
/// <para><b>THE REQUEST.</b> USER, 2026-09-03, verbatim: <i>"Das Multiplayer-Dialogfenster ist nicht
/// ideal gespawnt, es wäre trotzdem gut wenn es im Sichtbereich der Spieler spawnt (eventuell
/// Mittelwert der Blickfelder oder sowas?)."</i></para>
///
/// <para><b>WHY THIS COULD NOT BE A LOCAL RULE, which is the whole reason a byte is being spent.</b>
/// A shared window's spawn pose is a pure function of the map table's own geometry
/// (<c>ModalFallback.TrySharedAnchorOnTable</c>, WorldUI/Modal/ArcSeats.cs) — every client computes the same place with nothing
/// sent, which is exactly what makes it 1:1. But the table does not know where the humans are
/// facing, and no amount of geometry can tell it. The heads ARE on this wire already
/// (<c>AvatarState.Head</c>), so each client could average them — and that is the trap: two clients
/// average their own INTERPOLATED copies of those heads, sampled at different ages, and land on two
/// slightly different means. A shared window placed from a per-client mean is not a shared window.
/// So exactly one machine decides and everybody else obeys, which is 1:1 by construction rather
/// than by two machines happening to agree.</para>
///
/// <para><b>WHY THE DECISION IS FROZEN FOR THE WHOLE ROOM VISIT.</b> A published mean that TRACKED
/// the heads would be a different number 200 ms later, and two clients spawning the same window
/// 200 ms apart would place it in two places — the same divergence one step further along. The host
/// therefore decides ONCE per map-room activation, after <see cref="SettleSeconds"/> of the room
/// standing (long enough for the peers who came with it to have published a head), and holds that
/// decision until the room comes down. Every value this class can be in is stable for the length of
/// a room visit: NOT YET DECIDED, DECIDED-yaw, or DECIDED-REFUSED.</para>
///
/// <para><b>THE ONE INTERVAL IN WHICH TWO CLIENTS CAN STILL DISAGREE, stated rather than hidden:</b>
/// the ≤200 ms between the host latching and its next extras packet arriving, and the ≤200 ms after
/// a client walks into the room. A shared window spawning inside one of those two windows anchors
/// from the fallback while a client that already has the byte anchors from the byte. Both are legal
/// placements and the window is fully draggable and fully synced from the first drag; the anchor
/// line prints the value and its age, so a disagreement is a diff of two log lines and not a
/// mystery. Anything narrower than that would require the SPAWN to block on the wire, and a window
/// that waits for a packet before it can appear is a worse defect than one that stands 30° off.</para>
///
/// <para><b>THE DEPENDENCY DIRECTION IS THE ONE THIS PROJECT ALREADY HAS.</b> <c>Net</c> reads
/// <c>WorldUI</c> and pushes into its mailboxes; <c>WorldUI</c> never reaches back into <c>Net</c>
/// (the rule is written at the top of <c>WorldUI/Modal/SharedWindows.cs</c>). So the decided yaw is
/// handed to <see cref="WorldUI.ModalFallback.NoteSharedGazeYaw"/>, the same shape as
/// <see cref="WorldUI.ModalFallback.NoteSharedAnchorSpent"/>.</para>
/// </summary>
internal static class RemoteSharedGaze
{
    private const string Scope = "Net";

    /// <summary>How long the map room must have been STANDING on the host before it decides. Two
    /// seconds is not a guess about the network: the extras cadence is 5 Hz
    /// (<see cref="NetProtocol.ExtrasSendRateHz"/>), so two seconds is ten packets from every peer
    /// who is already in the room — comfortably more than the fifteen-missed-packet staleness window
    /// this family uses everywhere else, and short enough that it is over before a story window can
    /// be clicked open. Deciding on frame one would decide from the host's head alone, because
    /// nobody else's has arrived yet.</summary>
    private const float SettleSeconds = 2f;

    /// <summary>How long a peer's map-room record stays believed here. The same three seconds
    /// <c>RemoteMapRoom</c> uses, for the same reason and from the same packets — a peer whose
    /// packets stopped is not standing at the table any more.</summary>
    private const float PeerStaleSeconds = 3f;

    /// <summary>How coherent the party's gaze must be before a mean of it means anything: the
    /// LENGTH of the mean unit gaze vector, 0..1. Three people facing the same way give ~1.0; two
    /// people facing opposite ways give ~0.0, and the "mean direction" of that is an artefact of
    /// which way round they happen to be standing. Below this the host refuses to decide and
    /// everybody keeps the fixed table axis — a refusal is a real answer here, and it is the one
    /// that cannot put a window somewhere nobody is looking.</summary>
    private const float MinGazeCoherence = 0.5f;

    /// <summary>What one peer last said about their map room, reduced to the two facts this class
    /// needs. Fed from the same packet <c>RemoteMapRoom.Observe</c> reads, so there is no second
    /// source of truth about who is in the room.</summary>
    private readonly struct PeerGaze
    {
        public PeerGaze(bool inRoom, bool gazeValid, byte gazeYaw, float at)
        {
            InRoom = inRoom;
            GazeValid = gazeValid;
            GazeYaw = gazeYaw;
            At = at;
        }

        public readonly bool InRoom;
        public readonly bool GazeValid;
        public readonly byte GazeYaw;
        public readonly float At;
    }

    private static readonly Dictionary<int, PeerGaze> Peers = new();

    // ---- the host's own decision ---------------------------------------------------------------

    /// <summary>When the map room came up here, or −1 while it is down. The settle delay is measured
    /// from this, not from process start.</summary>
    private static float _roomUpAt = -1f;

    /// <summary>Whether the decision for THIS room visit has been made. It is set for a refusal too:
    /// a refusal that could be re-tried would be a value that changes during a room visit, which is
    /// the one thing this class exists to prevent.</summary>
    private static bool _decided;

    /// <summary>The decided yaw's byte, meaningful only while <see cref="_decidedValid"/>.</summary>
    private static byte _decidedYaw;

    /// <summary>Whether the decision was a YAW (true) or a REFUSAL (false). A refusal publishes no
    /// bit, so every client keeps the fixed table axis.</summary>
    private static bool _decidedValid;

    /// <summary>Drop everything on session end / shutdown. Nothing here owns a GameObject, so this
    /// IS the teardown.</summary>
    internal static void Reset()
    {
        Peers.Clear();
        _decisionFrom = 0;
        _roomUpAt = -1f;
        _decided = false;
        _decidedYaw = 0;
        _decidedValid = false;
        WorldUI.ModalFallback.NoteSharedGazeYaw(false, 0f, 0, "the net session ended");
    }

    /// <summary>
    /// THE HOST'S SAMPLE, called once per extras send from <c>RemoteMapRoom.Sample</c> while the map
    /// room is standing. Returns the byte to publish and, through <paramref name="valid"/>, whether
    /// <see cref="NetProtocol.MapRoomGazeValidBit"/> may be set beside it.
    ///
    /// <para>A NON-HOST ALWAYS ANSWERS "no decision". It is not that a client's mean would be
    /// wrong — it is that two of them would be two means. One machine decides.</para>
    ///
    /// <para>The host also feeds its OWN decision straight into the anchor here, because a sender
    /// never receives its own record and would otherwise be the one client in the room using the
    /// fallback.</para>
    /// </summary>
    internal static byte SampleHostYaw(out bool valid)
    {
        valid = false;
        if (!MapRoomDriver.Active)
        {
            // The room is down: forget the decision so the NEXT visit makes its own. A latch that
            // outlived the room would seat tomorrow's windows from where somebody looked today.
            NoteRoomDown("the map room came down");
            return 0;
        }
        if (_roomUpAt < 0f)
            _roomUpAt = Time.unscaledTime;

        if (!FFSNetwork.IsHost)
            return 0;   // exactly one machine decides, and this is not it

        if (!_decided)
        {
            if (Time.unscaledTime - _roomUpAt < SettleSeconds)
                return 0;   // still waiting for the party's heads to arrive
            Decide();
        }

        valid = _decidedValid;
        return _decidedValid ? _decidedYaw : (byte)0;
    }

    /// <summary>
    /// Take one peer's freshly parsed extras packet — called from <c>RemoteMapRoom.Observe</c>, off
    /// the same packet, so there is never a second opinion about who is in the room.
    ///
    /// <para>Pure bookkeeping plus ONE mailbox write: a HOST's decision is pushed straight into the
    /// anchor. Only a peer that says it is the host may do that, so a client cannot place another
    /// client's windows even by lying — the worst a liar achieves is the placement the real host
    /// would have been allowed to choose anyway.</para>
    /// </summary>
    internal static void Observe(int senderId, in PresenceState p)
    {
        if (senderId <= 0)
            return;
        if (!p.HasMapRoom)
        {
            DropPeer(senderId, "that peer's map-room record is gone, so they have left the room");
            return;
        }
        bool inRoom = (p.MapRoomFlags & NetProtocol.MapRoomInRoomBit) != 0;
        bool isHost = (p.MapRoomFlags & NetProtocol.MapRoomHostBit) != 0;
        bool gazeValid = (p.MapRoomFlags & NetProtocol.MapRoomGazeValidBit) != 0;
        Peers[senderId] = new PeerGaze(inRoom, gazeValid, p.MapRoomGazeYaw, Time.unscaledTime);

        if (!isHost || !inRoom)
            return;
        _decisionFrom = senderId;
        if (gazeValid)
        {
            WorldUI.ModalFallback.NoteSharedGazeYaw(
                true, NetProtocol.DecodeMapRoomGazeYaw(p.MapRoomGazeYaw), senderId,
                $"player {senderId} is the host and published gaze step {p.MapRoomGazeYaw} of "
                + $"{NetProtocol.MapRoomGazeYawSteps}");
        }
        else
        {
            WorldUI.ModalFallback.NoteSharedGazeYaw(
                false, 0f, senderId,
                $"player {senderId} is the host and has NOT decided (its record carries no gaze "
                + "bit — the room has just come up, or the host refused because the party's gaze "
                + "was incoherent or pointed away from the table)");
        }
    }

    /// <summary>Forget the peer bookkeeping for one sender. Called from <c>RemoteMapRoom.Forget</c>'s
    /// own edge so the two dictionaries can never disagree about who is present.</summary>
    internal static void Forget(int senderId)
        => DropPeer(senderId, "that peer went quiet and was forgotten");

    /// <summary>Which peer's decision this client last adopted, 0 for none. Only used to notice that
    /// the decider has GONE; the host's own copy of its own decision never goes through here.</summary>
    private static int _decisionFrom;

    /// <summary>
    /// Drop one sender AND, if that sender was the host whose decision this client is using, stand
    /// the decision down.
    ///
    /// <para>THE SECOND HALF IS THE POINT. A value latched from a host who has since left the map
    /// room is a direction nobody is looking in any more, and it would go on seating windows for the
    /// rest of the session with nothing anywhere saying where it came from. A departed decider means
    /// NO decision, which is the fixed table axis — the same answer a session with an older host
    /// gets, and the same answer this client gave before the decision arrived.</para>
    /// </summary>
    private static void DropPeer(int senderId, string why)
    {
        if (!Peers.Remove(senderId))
            return;
        if (senderId != _decisionFrom)
            return;
        _decisionFrom = 0;
        WorldUI.ModalFallback.NoteSharedGazeYaw(false, 0f, 0,
            $"player {senderId} was the host whose gaze decision this client was using, and {why}");
    }

    private static void NoteRoomDown(string why)
    {
        if (_roomUpAt < 0f && !_decided)
            return;     // already down; nothing to say and nothing to clear
        _roomUpAt = -1f;
        _decided = false;
        _decidedYaw = 0;
        _decidedValid = false;
        WorldUI.ModalFallback.NoteSharedGazeYaw(false, 0f, 0, why);
    }

    // ---- the decision itself -------------------------------------------------------------------

    /// <summary>
    /// DECIDE, ONCE, FOR THIS ROOM VISIT.
    ///
    /// <para>The input is the party's gaze: each participating player's head FORWARD, flattened to
    /// the horizon and summed as UNIT VECTORS. A vector mean and never an arithmetic mean of angles
    /// — averaging 359° and 1° as numbers gives 180°, which is the exact opposite of the answer, and
    /// that is not a rounding error but a wrong direction.</para>
    ///
    /// <para>TWO REFUSALS, and each of them keeps a real invariant rather than tidying an edge:</para>
    /// <list type="number">
    /// <item>INCOHERENT GAZE. The mean unit vector's LENGTH is how much the party agrees; below
    /// <see cref="MinGazeCoherence"/> the "mean direction" is an artefact of which way round people
    /// happen to be standing, and seating a window on it would be worse than the fixed axis it
    /// replaces.</item>
    /// <item>THE MEAN POINTS BACK OVER THE PLAYERS. The half-ring's own standing invariant is that
    /// no shared window is ever seated between a reader and the map ("the depth is the POSITIVE
    /// root"). If the mean gaze points from the table back toward where the party is standing — they
    /// are facing AWAY from the table — honouring it would seat the window behind them and in front
    /// of the map at once. So the host refuses and the fixed axis stands.</item>
    /// </list>
    ///
    /// <para>Both refusals are LATCHED. A refusal that re-tried would be a value that changes during
    /// a room visit, which is the divergence this whole class is built to avoid.</para>
    /// </summary>
    private static void Decide()
    {
        _decided = true;
        _decidedValid = false;
        _decidedYaw = 0;

        if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 centre, out float frameScale)
            || frameScale <= 0f)
        {
            // HW-VERIFY: the host could not decide at all because the shared frame is not there.
            // Once per room visit; its presence means every client in this session is on the fixed
            // table axis and the "im Sichtbereich" feature did nothing.
            VRLog.Note(Scope, "SHARED GAZE — the host cannot decide where the party is looking "
                              + "because MapRoomDriver.TryGetParchmentFrame has no frame yet, so no "
                              + "gaze bit is published for this room visit and EVERY client anchors "
                              + "a shared window on the fixed table axis, exactly as every build "
                              + "before this one did. This is the correct fallback and not a "
                              + "degradation of anything that used to work.");
            return;
        }

        Vector3 gazeSum = Vector3.zero;
        Vector3 headSum = Vector3.zero;
        int heads = 0;

        Camera? cam = CanvasConversion.WorldCamera;
        if (cam != null)
        {
            Transform t = cam.transform;
            if (Accumulate(t.forward, t.position, ref gazeSum, ref headSum))
                heads++;
        }

        float now = Time.unscaledTime;
        foreach (KeyValuePair<int, PeerGaze> kv in Peers)
        {
            if (!kv.Value.InRoom || now - kv.Value.At > PeerStaleSeconds)
                continue;
            if (!NetAvatarDriver.TryGetPeerHeadHolder(kv.Key, out Transform holder) || holder == null)
                continue;
            if (Accumulate(holder.forward, holder.position, ref gazeSum, ref headSum))
                heads++;
        }

        if (heads == 0)
        {
            // HW-VERIFY: once per room visit. Not an error — a host alone in a room whose head
            // camera has no tracked pose reaches it — but it is the line that says why the feature
            // stood down, and its absence is what says the feature ran.
            VRLog.Note(Scope, "SHARED GAZE — the host found NO usable head to average (no local "
                              + "head camera and no peer head holder with a horizontal facing), so "
                              + "it publishes no gaze bit for this room visit and every client "
                              + "anchors a shared window on the fixed table axis.");
            return;
        }

        float coherence = gazeSum.magnitude / heads;
        Vector3 mean = gazeSum.normalized;
        Vector3 party = headSum / heads - centre;
        party.y = 0f;
        float outward = Vector3.Dot(mean, party.sqrMagnitude > 1e-6f ? party.normalized : -mean);

        if (coherence < MinGazeCoherence)
        {
            // HW-VERIFY: once per room visit. Names the refusal AND its number, so the threshold is
            // tunable from a log rather than from a guess.
            VRLog.Note(Scope, $"SHARED GAZE — {heads} head(s) averaged to a coherence of "
                              + $"{coherence:F2}, under the {MinGazeCoherence:F2} this decision "
                              + "requires, so the host REFUSES to name a direction for this room "
                              + "visit and every client keeps the fixed table axis. Coherence is the "
                              + "LENGTH of the mean unit gaze vector: 1.00 is a party all facing one "
                              + "way and 0.00 is a party facing opposite ways, whose 'mean "
                              + "direction' is an artefact of how they happen to be standing rather "
                              + "than a place anybody is looking.");
            return;
        }
        if (outward > 0f)
        {
            // HW-VERIFY: once per room visit.
            VRLog.Note(Scope, $"SHARED GAZE — {heads} head(s) agree (coherence {coherence:F2}) but "
                              + "their mean gaze points BACK OVER THE PARTY rather than across the "
                              + $"table (outward dot {outward:+0.00;-0.00}), i.e. they are facing "
                              + "away from the map. Seating a window there would put it behind them "
                              + "AND between the readers and the map, which the half-ring's own "
                              + "'the depth is the POSITIVE root' invariant forbids. The host "
                              + "REFUSES for this room visit and every client keeps the fixed table "
                              + "axis.");
            return;
        }

        float yawDeg = Mathf.Atan2(mean.x, mean.z) * Mathf.Rad2Deg;
        _decidedYaw = NetProtocol.EncodeMapRoomGazeYaw(yawDeg);
        _decidedValid = true;
        float quantised = NetProtocol.DecodeMapRoomGazeYaw(_decidedYaw);

        // HW-VERIFY: THE line this whole feature is verified by, once per room visit on the host.
        // Its presence with a step number means a shared window in this session is seated where the
        // party was looking; its absence, or a REFUSED line above it, means the fixed axis. The
        // matching line on every OTHER client is the anchor's own "GAZE step N" clause.
        VRLog.Note(Scope, $"SHARED GAZE DECIDED — {heads} player head(s) in the map room average to "
                          + $"yaw {yawDeg:F2}°, coherence {coherence:F2} (1.00 = all facing one "
                          + $"way), and the host publishes that as step {_decidedYaw} of "
                          + $"{NetProtocol.MapRoomGazeYawSteps} = {quantised:F2}° — a quantisation "
                          + $"error of {Mathf.Abs(Mathf.DeltaAngle(yawDeg, quantised)):F2}°, which "
                          + "is under a centimetre of arc at the 0.80 m ring radius. THE HOST "
                          + "DECIDES AND EVERY CLIENT OBEYS: this is what makes a shared window 1:1 "
                          + "here, not two machines averaging the same peer heads at two different "
                          + "ages and hoping. THE DECISION IS NOW FROZEN for this room visit — it "
                          + "does not follow anybody's head — because a value that moved would seat "
                          + "the same window in two places for two clients spawning it 200 ms "
                          + "apart. It is released when the map room comes down.");

        // The host never receives its own record, so it feeds its own decision in directly. Without
        // this the one client that MADE the decision would be the only one not using it.
        WorldUI.ModalFallback.NoteSharedGazeYaw(
            true, quantised, 0,
            $"this client is the host and decided step {_decidedYaw} from {heads} head(s) at "
            + $"coherence {coherence:F2}");
    }

    /// <summary>Add one head to the running sums, or answer false if its facing has no horizontal
    /// component at all (a head looking straight down has no gaze yaw, and normalising that vector
    /// would amplify tracking noise into a direction).</summary>
    private static bool Accumulate(Vector3 forward, Vector3 pos, ref Vector3 gazeSum,
                                   ref Vector3 headSum)
    {
        Vector3 flat = new(forward.x, 0f, forward.z);
        if (flat.sqrMagnitude < 1e-6f)
            return false;
        gazeSum += flat.normalized;
        headSum += pos;
        return true;
    }

}
