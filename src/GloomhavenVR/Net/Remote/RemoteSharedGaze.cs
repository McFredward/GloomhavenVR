using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// WHERE THE PARTY IS STANDING — ONE DECISION, MADE BY THE HOST, PUBLISHED AS ONE BYTE.
///
/// <para><b>THE REQUEST THIS CLASS WAS BUILT FOR (2026-09-03, verbatim):</b> <i>"Das
/// Multiplayer-Dialogfenster ist nicht ideal gespawnt, es wäre trotzdem gut wenn es im Sichtbereich
/// der Spieler spawnt (eventuell Mittelwert der Blickfelder oder sowas?)."</i> It shipped in
/// ModBuild 458 as literally that — a mean of the party's head FORWARD vectors.</para>
///
/// <para><b>THE REPORT THAT KILLED THAT RULE (2026-09-06, ModBuild 459, verbatim):</b> <i>"1) Die
/// Spawnpunkte der Multiplayerfenster sind irgendwie anders. Im Map Raum hatten wir jetzt den Fall
/// das sie ziemlich von uns beiden weggedreht sind im Mapraum. 2) Im Test ist das Dialogfenster
/// gespawnt bevor alle Spieler am Tisch fertig gesetzt wurden. D.h. es hat sich völlig an einem
/// Spieler orientiert und war dann beim 2. Spieler gegenüber nur von hinten lesbar. … a) der
/// spawnpunkt soll sich an den finalen Spawnpunkt orientieren der Spieler b) Sind zwei Spieler
/// gegenüber ist es besser wenn es links oder rechts von uns beiden spawnt zu uns gedreht."</i></para>
///
/// <para><b>BOTH HALVES OF THAT REPORT ARE THIS ONE METHOD, AND THE 459 LOG NAMES THEM.</b>
/// <c>[Net] SHARED GAZE DECIDED — 1 player head(s) … coherence 1.00</c> stands at Player.log:12510.
/// <c>[Net] MIXED SESSION CENSUS: 1 player(s)</c> stands at :12424 and
/// <c>MIXED SESSION CENSUS: 2 player(s)</c> at :13650, with <c>Remote avatar created for player
/// 2</c> at :13671. So the host decided the party's spawn direction 1 140 lines BEFORE the second
/// player existed on the wire at all, latched it for the whole room visit, and the first window
/// anchored at :15686 — two thousand lines after the peer arrived — on that one player's momentary
/// glance. That is item 2 word for word ("es hat sich völlig an einem Spieler orientiert"), and
/// item 1 is the same reading one step on: a direction taken from ONE head is turned however that
/// head happened to be turned, which is "ziemlich von uns beiden weggedreht" for the two people who
/// afterwards stood somewhere else.</para>
///
/// <para><b>WHY A MEAN OF FACINGS COULD NEVER HAVE ANSWERED IT, stated as arithmetic rather than as
/// a preference.</b> Two people reading one table stand OPPOSITE each other and therefore face
/// opposite ways. The sum of two opposite unit vectors is the zero vector: its LENGTH (the
/// coherence this class used to test) is 0 and its DIRECTION is whatever the tracking noise on two
/// heads happens to leave behind. The old rule had exactly two outcomes for the case the user
/// reports, and both are in his two sentences — refuse (and fall back to a fixed table axis that
/// has no term for either human, item 1) or believe a single head (item 2). No threshold on that
/// mean fixes it, because the input carries no information about where anybody is STANDING.</para>
///
/// <para><b>WHAT REPLACES IT: SEATS, AND A WORST-SEAT OBJECTIVE.</b> The direction is now chosen by
/// SEARCH over the 256 yaws the wire byte can represent, scored against where the party's heads
/// ARE. For each candidate the ring point the anchor would use is built, and for every seat the
/// READING ANGLE is measured — the angle between the window's own front normal and the direction
/// from the window to that head. 0° is dead square-on, 90° is edge-on, and past 90° that player is
/// looking at the BACK of the window, which is the user's "nur von hinten lesbar" measured as a
/// number. The score of a candidate is the WORST seat's angle, and the winner is the candidate
/// whose worst seat is best off.</para>
///
/// <para><b>WHY THE WORST SEAT AND NOT THE MEAN, which is the whole of item 2.</b> A mean lets one
/// player be handed a 170° back so long as the other is handed a 5° front: that pair averages to a
/// respectable 87° and is precisely the picture he photographed. The worst-seat form cannot buy one
/// player's comfort with another's, so it has no way to express the outcome he reported. It is the
/// same rule the 1:1 ruling already applies to size ("what the owner of a thing sees, every other
/// player sees"), applied to legibility.</para>
///
/// <para><b>AND IT PRODUCES HIS (b) WITHOUT BEING TOLD TO.</b> For two seats opposite each other at
/// distance D from the table centre, with the ring radius R, a window seated ON the axis between
/// them faces one of them at ~0° and the other at ~180°: worst 180°. Seated on the PERPENDICULAR
/// BISECTOR — "links oder rechts von uns beiden" — both read it at exactly
/// <c>acos(R / sqrt(D² + R²))</c>, which at the shipped R = 0.80 m and a seat 0.75 m out is 43°:
/// square enough for a flat panel, and equal for the two of them. The search finds that because it
/// is the arithmetic optimum of the worst-seat score, not because a rule for "two opposite players"
/// was written down. One player collapses to the old answer (the far side of the table, dead
/// square-on); three and four spread the cost evenly; a player who has not arrived yet is not a
/// seat and cannot skew anything.</para>
///
/// <para><b>THE ONE TERM THAT IS NOT AN ANGLE.</b> A candidate that seats the window INSIDE
/// somebody's reading distance scores 0° by the angle alone — he is square-on to it because it is
/// against his face — while standing between him and the map. So a seat nearer than
/// <see cref="MinReadingMeters"/> is charged a penalty that reaches 180° at zero distance. That
/// replaces ModBuild 458's "the mean points BACK OVER THE PARTY" refusal, which was a property of
/// the mean and has no meaning here; it keeps the same invariant the half-ring states in its own
/// words ("no shared window is ever seated between the reader and the map") and keeps it as a
/// measured distance rather than as a sign test.</para>
///
/// <para><b>WHY THE DECISION IS STILL ONE MACHINE'S, unchanged and non-negotiable.</b> The heads
/// are on this wire already (<c>AvatarState.Head</c>), so every client COULD run this search — and
/// would run it over its own interpolated copies of those heads, sampled at different ages, and
/// land on different bytes. A shared window placed from a per-client search is not a shared window.
/// Exactly one machine decides and everybody else obeys, which is 1:1 by construction rather than
/// by two machines happening to agree. Nothing in the receiving path changed: it is the same byte,
/// the same record 20, the same <see cref="WorldUI.ModalFallback.NoteSharedGazeYaw"/> mailbox, and
/// the same fixed-axis fallback for a session whose host is on an older build.</para>
///
/// <para><b>THE FREEZE IS NOW EARNED RATHER THAN TIMED, WHICH IS ITEM 2(a).</b> ModBuild 458 froze
/// after a flat two seconds of the room standing — a number chosen to be "ten packets from every
/// peer who is already in the room", which quietly assumes the party is already assembled. It was
/// not. The gate now demands three things together: the room has stood for
/// <see cref="MinSettleSeconds"/>, every player the session roster knows has published a fresh
/// map-room record (so nobody is still on their way in), and every participating head has held
/// still within <see cref="SeatStillMeters"/> for <see cref="SeatStillSeconds"/> (so nobody is
/// still walking to their place). <see cref="MaxSettleSeconds"/> is the cap that keeps a fidgety
/// party from disabling the feature forever, and a decision taken on the cap says MOVING in its own
/// log line instead of pretending to be settled.</para>
///
/// <para><b>AND THE FREEZE REOPENS ON A MEMBERSHIP CHANGE, ONCE PER CHANGE.</b> "Der spawnpunkt
/// soll sich an den finalen Spawnpunkt orientieren der Spieler" cannot be honoured by a value
/// latched before the last player walked in. So a seat set that GAINS or LOSES a player re-opens
/// the decision; a seat set that merely MOVES does not, because that would be the per-frame value
/// the freeze exists to prevent — unless it has moved somewhere the standing answer is BROKEN, which
/// is the second trigger below. The change must HOLD for <see cref="MembershipHoldSeconds"/> first
/// — <c>NetAvatarDriver.TryGetPeerHeadHolder</c> answers false for a head holder that is momentarily
/// inactive, and a blinking avatar would otherwise re-place every standing window twice a second —
/// and the settle gate then re-runs from a clock that starts AT the re-open, so a re-decision is
/// judged on how settled the party is now rather than on how old the room is. Windows already
/// standing are re-placed on the new decision through
/// <c>ModalFallback.ReseatSharedWindowsOnce</c> — only those nobody has dragged, because the drag
/// still wins and spends the anchor.</para>
///
/// <para><b>AND IT REOPENS A SECOND WAY, WHICH IS THE 2026-09-06 ITEM 1 FIX.</b> Membership was the
/// only trigger through ModBuild 462, and a player ALONE at the table has no membership change
/// available to him: his round-1 direction, chosen at the 20 s cap while he was still walking in
/// from 2.47 m out, stood for the entire room visit and seated every quest popup back-first once he
/// took his real place — <c>SHARED WINDOW SEAT ANGLES</c> 121.5°, 145.5° and 119.1° over ONE head in
/// that build's host log, against 25-26° for the same session's two-player windows. His own words
/// are "spawnt das Multiplayer-Fenster falsch herum, also weg von einem gewandt … wenn ein weiterer
/// Spieler dazu kommt, spawnt es so wie gewollt": the joining player was never the cure, the
/// re-decision the join forced was. So a STANDING direction whose worst seat has gone past
/// <see cref="StaleReadingDegrees"/> for <see cref="MembershipHoldSeconds"/>, AND which a
/// re-decision would improve by at least <see cref="StaleImprovementDegrees"/>, re-opens too. See
/// <see cref="StaleDirectionReopen"/> for why those two guards mean this is not the per-frame value
/// the freeze forbids. A re-open of EITHER kind keeps the published byte until the new decision
/// lands, so no client is dropped onto the humanless fixed axis while the settle gate runs.</para>
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
/// for a PLACEMENT INPUT (the rule is written at the top of <c>WorldUI/Modal/SharedWindows.cs</c>).
/// So the decided yaw is handed to <see cref="WorldUI.ModalFallback.NoteSharedGazeYaw"/>, the same
/// shape as <see cref="WorldUI.ModalFallback.NoteSharedAnchorSpent"/>.</para>
/// </summary>
internal static class RemoteSharedGaze
{
    private const string Scope = "Net";

    /// <summary>The FLOOR on the settle gate: how long the map room must have stood here before the
    /// host will even look at the party. Two seconds is ten packets at the 5 Hz extras cadence
    /// (<see cref="NetProtocol.ExtrasSendRateHz"/>) from every peer who is already in the room.
    /// Deciding on frame one would decide from the host's head alone, because nobody else's has
    /// arrived yet — which is what ModBuild 459's log caught it doing anyway, two seconds being no
    /// help at all against a player who joins the SESSION a minute later.</summary>
    private const float MinSettleSeconds = 2f;

    /// <summary>The CAP on the settle gate. Past this the host decides with whatever the party looks
    /// like, and says MOVING in its own line rather than claiming a settle it did not get. It exists
    /// because every other term in the gate can be held open forever by one player who never stands
    /// still, and a feature that silently never runs is worse than one that runs on an imperfect
    /// input ([[gated-remedy-never-ran]]).</summary>
    private const float MaxSettleSeconds = 20f;

    /// <summary>How long every participating head must hold still before the party counts as seated.
    /// Sampled at the extras cadence, so this is five samples.</summary>
    private const float SeatStillSeconds = 1f;

    /// <summary>How far a head may drift inside <see cref="SeatStillSeconds"/> and still count as
    /// standing still — horizontal metres. Wide enough to ignore the sway of a standing human and
    /// narrow enough to catch somebody walking round the table to the other end, which is the motion
    /// the user's "bevor alle Spieler am Tisch fertig gesetzt wurden" is about.</summary>
    private const float SeatStillMeters = 0.12f;

    /// <summary>How long a peer's map-room record stays believed here. The same window
    /// <c>RemoteMapRoom</c> uses, for the same reason and from the same packets — a peer whose
    /// packets stopped is not standing at the table any more. Aliased to
    /// <see cref="NetProtocol.StaleTimeoutSeconds"/>, the window the peer's own avatar is dropped
    /// on, so "still at the table" cannot outlive "still here".</summary>
    private const float PeerStaleSeconds = NetProtocol.StaleTimeoutSeconds;

    /// <summary>The nearest a shared window may be seated to somebody's head before the candidate is
    /// charged as unreadable, in real metres of the shared frame. A window closer than this is
    /// against that player's face and between him and the map, and the ANGLE alone would score that
    /// candidate 0° — it is square-on precisely because it is in the way. See the class comment.
    /// </summary>
    private const float MinReadingMeters = 0.35f;

    /// <summary>The reading angle at which a STANDING decision counts as stale rather than merely
    /// imperfect: past this the window's face is turned so far from a seat that the player is behind
    /// it, which is the user's "spawnt weg von einem gewandt" in one number. Below it the decision is
    /// left alone however old it is, because a freeze that re-opens on small change is the per-frame
    /// value the freeze exists to prevent. Deliberately the BROKEN bar and not the WORKING bar: the
    /// instrument's own working bar is 70°, and re-opening at 70° would churn on a table that is
    /// merely awkward.</summary>
    private const float StaleReadingDegrees = 90f;

    /// <summary>How much better the best available direction must be than the standing one before a
    /// stale decision re-opens. THE ANTI-OSCILLATION GUARD: a party can stand in a way whose OPTIMUM
    /// worst seat is itself past <see cref="StaleReadingDegrees"/> (four players round a small
    /// table), and re-deciding there would re-place every window once a second forever while landing
    /// on the same byte. A re-open must buy a real improvement or it does not happen. After a
    /// re-decision the standing worst IS the best, so the improvement is 0 and the trigger cannot
    /// immediately re-arm.</summary>
    private const float StaleImprovementDegrees = 25f;

    /// <summary>What one peer last said about their map room, reduced to the two facts this class
    /// needs. Fed from the same packet <c>RemoteMapRoom.Observe</c> reads, so there is never a second
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

    // ---- the party's seats -----------------------------------------------------------------------

    /// <summary>One player's place at the table: who, and WHERE — never which way they are looking.
    /// A gaze is a glance and moves twice a second; a seat is where the human is standing, and it is
    /// the thing the user's report is about.</summary>
    private readonly struct Seat
    {
        public Seat(int playerId, Vector3 headWorld)
        {
            PlayerId = playerId;
            HeadWorld = headWorld;
        }

        /// <summary>0 for this client, otherwise the peer's player id.</summary>
        public readonly int PlayerId;

        /// <summary>The head's world position. Flattened where it is used; kept whole so the log can
        /// print what was measured.</summary>
        public readonly Vector3 HeadWorld;
    }

    /// <summary>The seats sampled this tick. A field so the 5 Hz sample allocates nothing.</summary>
    private static readonly List<Seat> Seats = new(4);

    /// <summary>Stillness bookkeeping, keyed the way <see cref="Seat.PlayerId"/> is: the last
    /// position this seat was seen to MOVE to, and when it moved there. A seat is "still" when that
    /// stamp is older than <see cref="SeatStillSeconds"/>.</summary>
    private static readonly Dictionary<int, (Vector3 Pos, float MovedAt)> SeatMotion = new();

    /// <summary>Scratch for the session roster read, so the completeness test allocates nothing.
    /// </summary>
    private static readonly List<(int Id, string? Account, string? Name)> RosterScratch = new(8);

    // ---- the host's own decision -----------------------------------------------------------------

    /// <summary>When the map room came up here, or −1 while it is down. The settle gate is measured
    /// from this, not from process start.</summary>
    private static float _roomUpAt = -1f;

    /// <summary>Whether a decision for THIS room visit stands. Set for a refusal too: a refusal that
    /// re-tried on its own would be a value that changes during a room visit, which is the divergence
    /// this class exists to prevent. It is re-opened by a MEMBERSHIP change or by the standing
    /// direction going STALE against where the party now is — see <see cref="ReopenDecision"/>.
    /// </summary>
    private static bool _decided;

    /// <summary>The decided yaw's byte, meaningful only while <see cref="_decidedValid"/>.</summary>
    private static byte _decidedYaw;

    /// <summary>Whether the decision was a YAW (true) or a REFUSAL (false). A refusal publishes no
    /// bit, so every client keeps the fixed table axis.</summary>
    private static bool _decidedValid;

    /// <summary>The membership the standing decision was taken from — the seat ids folded, and
    /// NOTHING about where they were. A change here re-opens the decision; a seat that merely moved
    /// does not, because re-deciding on movement is the per-frame value the freeze forbids. A seat
    /// that moved somewhere the standing direction is BROKEN for is a different question and is
    /// answered by <see cref="StaleDirectionReopen"/>, not here.</summary>
    private static int _decidedMembership;

    /// <summary>How many times this room visit has decided, for the log only.</summary>
    private static int _decisionRound;

    /// <summary>When the settle gate's clock started: the room coming up, or the last time a
    /// membership change re-opened the decision. SEPARATE from <see cref="_roomUpAt"/>, which also
    /// means "the room is standing" and must not be rewound. Without this the gate would measure a
    /// re-open against the room's own age, find itself long past <see cref="MaxSettleSeconds"/>, and
    /// stamp a perfectly settled party MOVING.</summary>
    private static float _gateClockAt = -1f;

    /// <summary>The membership last OBSERVED to differ from the decided one, and when it started
    /// differing. A change must hold for <see cref="MembershipHoldSeconds"/> before it re-opens
    /// anything: <c>TryGetPeerHeadHolder</c> answers false for a head holder that is momentarily
    /// inactive, so a flapping avatar would otherwise re-open, re-place and re-log twice a second and
    /// the windows would visibly jump.</summary>
    private static int _pendingMembership;

    private static float _pendingMembershipSince = -1f;

    /// <summary>How long a changed membership must hold before the frozen decision re-opens. Five
    /// samples at the 5 Hz extras cadence.</summary>
    private const float MembershipHoldSeconds = 1f;

    /// <summary>When the standing decision was first observed to be STALE — worst seat past
    /// <see cref="StaleReadingDegrees"/> with a materially better direction available — or −1 while
    /// it is not. Its own clock, separate from <see cref="_pendingMembershipSince"/>, so a party that
    /// is both changing and badly seated cannot have one trigger reset the other's hold.</summary>
    private static float _staleSince = -1f;

    /// <summary>Which term last blocked the settle gate, so the refusal can be logged on its EDGE
    /// instead of at the 5 Hz sample rate: 0 nothing, 1 the floor, 2 a roster player with no record,
    /// 3 a seat still moving. Reset per decision round.</summary>
    private static int _gateBlockClass;

    /// <summary>Drop everything on session end / shutdown. Nothing here owns a GameObject, so this
    /// IS the teardown.</summary>
    internal static void Reset()
    {
        Peers.Clear();
        Seats.Clear();
        SeatMotion.Clear();
        _decisionFrom = 0;
        _roomUpAt = -1f;
        _gateClockAt = -1f;
        _decided = false;
        _decidedYaw = 0;
        _decidedValid = false;
        _decidedMembership = 0;
        _pendingMembership = 0;
        _pendingMembershipSince = -1f;
        _staleSince = -1f;
        _gateBlockClass = 0;
        _decisionRound = 0;
        WorldUI.ModalFallback.NoteSharedGazeYaw(false, 0f, 0, "the net session ended");
    }

    /// <summary>
    /// THE HOST'S SAMPLE, called once per extras send from <c>RemoteMapRoom.Sample</c> while the map
    /// room is standing. Returns the byte to publish and, through <paramref name="valid"/>, whether
    /// <see cref="NetProtocol.MapRoomGazeValidBit"/> may be set beside it.
    ///
    /// <para>A NON-HOST ALWAYS ANSWERS "no decision". It is not that a client's search would be
    /// wrong — it is that two of them would be two searches over two sets of interpolated heads. One
    /// machine decides.</para>
    ///
    /// <para>The host also feeds its OWN decision straight into the anchor, because a sender never
    /// receives its own record and would otherwise be the one client in the room using the
    /// fallback.</para>
    /// </summary>
    internal static byte SampleHostYaw(out bool valid)
    {
        valid = false;
        if (!MapRoomDriver.Active)
        {
            // The room is down: forget the decision so the NEXT visit makes its own. A latch that
            // outlived the room would seat tomorrow's windows from where somebody stood today.
            NoteRoomDown("the map room came down");
            return 0;
        }
        if (_roomUpAt < 0f)
        {
            _roomUpAt = Time.unscaledTime;
            _gateClockAt = _roomUpAt;
        }

        if (!FFSNetwork.IsHost)
            return 0;   // exactly one machine decides, and this is not it

        // The party is surveyed on EVERY sample, decided or not: the membership fold below is what
        // re-opens a frozen decision when the last player finally walks in, which is the user's
        // "der spawnpunkt soll sich an den finalen Spawnpunkt orientieren der Spieler".
        CollectSeats();
        UpdateSeatMotion();
        int membership = MembershipFold();

        // A CHANGED MEMBERSHIP MUST HOLD BEFORE IT RE-OPENS ANYTHING. See _pendingMembership: a head
        // holder that blinks would otherwise re-place every standing window twice a second.
        if (!_decided || membership == _decidedMembership)
        {
            _pendingMembership = membership;
            _pendingMembershipSince = -1f;
        }
        else
        {
            if (membership != _pendingMembership || _pendingMembershipSince < 0f)
            {
                _pendingMembership = membership;
                _pendingMembershipSince = Time.unscaledTime;
            }
        }

        if (_decided && membership != _decidedMembership && _pendingMembershipSince >= 0f
            && Time.unscaledTime - _pendingMembershipSince >= MembershipHoldSeconds)
        {
            // HW-VERIFY: the party changed under a frozen decision. Once per membership change, so at
            // most a handful per room visit; its ABSENCE while a player joins is the ModBuild 459
            // defect returning.
            VRLog.Note(Scope, "SHARED SEATS — the party changed while a spawn direction stood "
                              + $"(membership fold {_decidedMembership} → {membership}, "
                              + $"{Seats.Count} seat(s) now, held for "
                              + $"{MembershipHoldSeconds:F1} s). The decision is RE-OPENED: a direction "
                              + "chosen before the last player walked in is exactly ModBuild 459's "
                              + "report (\"bevor alle Spieler am Tisch fertig gesetzt wurden\"), and "
                              + "the user's own remedy is that the spawn point follow the FINAL "
                              + "seats. Windows already standing that nobody has dragged are "
                              + "re-placed on the new answer; a dragged window keeps its pose, "
                              + "because the drag still spends the anchor.");
            ReopenDecision();
        }

        // THE SECOND RE-OPEN, AND THE ONE A SOLO PARTY DEPENDS ON. A membership fold cannot fire for
        // a player who is alone at the table, so a direction chosen while he was still walking in
        // stood for the whole room visit — see StaleDirectionReopen for the reading that convicts it.
        StaleDirectionReopen();

        if (!_decided)
        {
            if (!SettleGateOpen(out bool settled, out string gateWhy))
            {
                // THE GATE IS SHUT, WHICH IS NOT A REASON TO PUBLISH NOTHING. A re-open keeps the
                // byte that already stands (see ReopenDecision), so this interval publishes the
                // direction the party has been using rather than dropping every client onto the
                // fixed table axis for as long as 20 s. On the FIRST decision of a room visit
                // nothing stands, _decidedValid is false, and this is bit for bit the old answer.
                valid = _decidedValid;
                return _decidedValid ? _decidedYaw : (byte)0;
            }
            _decisionRound++;
            _gateBlockClass = 0;
            Decide(membership, settled, gateWhy);
        }

        valid = _decidedValid;
        return _decidedValid ? _decidedYaw : (byte)0;
    }

    /// <summary>
    /// RE-OPEN THE FROZEN DECISION, from either trigger, in ONE place.
    ///
    /// <para><b>THE PUBLISHED BYTE SURVIVES THE RE-OPEN.</b> Clearing it here would put every client
    /// in the session on the fixed table axis — a direction with no term for a human in it — for the
    /// 2 s to 20 s the settle gate then takes, and a window spawning in that interval would be seated
    /// worse than by the answer that was already standing. The old byte is a legal placement made for
    /// this party; it is replaced when <see cref="Decide"/> overwrites it, and every window seated on
    /// it is re-placed by <c>ModalFallback.ReseatSharedWindowsOnce</c> on the new one.</para>
    /// </summary>
    private static void ReopenDecision()
    {
        _decided = false;
        _gateClockAt = Time.unscaledTime;      // the gate measures the RE-OPEN, not the room's age
        _pendingMembershipSince = -1f;
        _staleSince = -1f;
        _gateBlockClass = 0;
    }

    /// <summary>
    /// <b>THE 2026-09-06 ITEM 1 FIX: A FROZEN DIRECTION THAT NOW SHOWS SOMEBODY ITS BACK RE-OPENS.</b>
    ///
    /// <para><b>THE READING THAT NAMES IT.</b> In the ModBuild 462 host log the sole player's round-1
    /// decision was taken at the 20 s cap ("1 of 1 seat(s) still moving") from a seat 2.47 m out on
    /// the +X side of the table, and predicted a worst seat of 0.2° — a correct search over a seat he
    /// was walking through. He then took his real place 3.2 m away on the opposite side, and every
    /// quest popup that followed was seated 0.06 m from his face with its back to him: SHARED WINDOW
    /// SEAT ANGLES read 121.5°, 145.5° and 119.1° over ONE head. Nothing re-opened, because a
    /// membership fold was the only trigger there was and a solo party's membership never changes.
    /// When the second player joined, the fold changed, round 2 ran with both of them actually at the
    /// table, and the same session's readings drop to 25-26° — which is exactly the user's "wenn ein
    /// weiterer Spieler dazu kommt, spawnt es so wie gewollt". The join was never the cure; the
    /// RE-DECISION the join happened to force was.</para>
    ///
    /// <para><b>WHY IT IS NOT THE PER-FRAME VALUE THE FREEZE FORBIDS.</b> Three terms have to hold
    /// together. The standing direction must be past <see cref="StaleReadingDegrees"/> for some seat
    /// (not merely imperfect — turned away); a re-decision must buy at least
    /// <see cref="StaleImprovementDegrees"/> on that worst seat (so a party whose optimum is itself
    /// bad cannot make the room churn, and so the trigger cannot re-arm on its own answer); and it
    /// must hold for <see cref="MembershipHoldSeconds"/>, the same hold a membership change serves.
    /// A party that merely MOVED still does not re-open anything — a party that moved somewhere the
    /// standing answer is broken for does.</para>
    ///
    /// <para>The full 256-step search only runs once the cheap standing-direction score has already
    /// failed, so a well-seated table costs <c>Seats.Count</c> dot products per extras sample.</para>
    /// </summary>
    private static void StaleDirectionReopen()
    {
        if (!_decided || !_decidedValid || Seats.Count == 0)
        {
            _staleSince = -1f;
            return;
        }
        if (!TryPrepareSearch(out Vector3 centreFlat, out float radiusWorld, out float minReadWorld,
                              out _))
        {
            _staleSince = -1f;
            return;
        }

        float standingRad = NetProtocol.DecodeMapRoomGazeYaw(_decidedYaw) * Mathf.Deg2Rad;
        var standingAhead = new Vector3(Mathf.Sin(standingRad), 0f, Mathf.Cos(standingRad));
        ScoreDirection(centreFlat, standingAhead, radiusWorld, minReadWorld, out float standingWorst,
                       out _);
        if (standingWorst < StaleReadingDegrees)
        {
            _staleSince = -1f;
            return;
        }

        SearchBestDirection(centreFlat, radiusWorld, minReadWorld, out byte bestStep,
                            out float bestWorst, out _, out _);
        if (standingWorst - bestWorst < StaleImprovementDegrees)
        {
            // The table cannot be served materially better than it already is. Say nothing and
            // change nothing: this is the guard that keeps a party whose own optimum is bad from
            // re-placing every window once a second onto the byte it already has.
            _staleSince = -1f;
            return;
        }

        if (_staleSince < 0f)
        {
            _staleSince = Time.unscaledTime;
            return;
        }
        if (Time.unscaledTime - _staleSince < MembershipHoldSeconds)
            return;

        // HW-VERIFY: the standing direction had turned its back on somebody and a better one existed.
        // At most once per re-open — the hold clock is cleared by ReopenDecision and the improvement
        // guard means the trigger cannot re-arm on its own answer — so a BURST of these is that guard
        // failing. Its ABSENCE on a one-seat table whose SHARED WINDOW SEAT ANGLES reads past 90° for
        // more than one spawn is this fix failing to run at all.
        VRLog.Note(Scope, "SHARED SEATS — the standing spawn direction has gone STALE against where "
                          + $"the party is now: worst seat {standingWorst:F1}° over {Seats.Count} "
                          + $"seat(s) against the {bestWorst:F1}° available from step {bestStep}, "
                          + $"held past {StaleReadingDegrees:F0}° for {MembershipHoldSeconds:F1} s. "
                          + "Past 90° that player is reading the BACK of the window, which is the "
                          + "2026-09-06 report's item 1 (\"spawnt das Multiplayer-Fenster falsch "
                          + "herum, also weg von einem gewandt\"). THE DECISION IS RE-OPENED. This is "
                          + "the trigger a SOLO party depends on: a membership fold cannot change for "
                          + "a player who is alone at the table, so before this a direction chosen "
                          + "while he was still walking in stood for the whole room visit and only a "
                          + "second player joining could dislodge it. The published byte is KEPT "
                          + $"until the new decision lands (step {_decidedYaw} stays on the wire), so "
                          + "nothing falls back to the fixed table axis in the meantime. Windows "
                          + "already standing that nobody has dragged are re-placed on the new "
                          + "answer; a dragged window keeps its pose.");
        ReopenDecision();
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
                + "bit — the room has just come up, the party is not seated yet, or the host found "
                + "no usable head at all)");
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
    /// room is a direction nobody is standing against any more, and it would go on seating windows
    /// for the rest of the session with nothing anywhere saying where it came from. A departed
    /// decider means NO decision, which is the fixed table axis — the same answer a session with an
    /// older host gets, and the same answer this client gave before the decision arrived.</para>
    /// </summary>
    private static void DropPeer(int senderId, string why)
    {
        SeatMotion.Remove(senderId);
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
        _gateClockAt = -1f;
        _decided = false;
        _decidedYaw = 0;
        _decidedValid = false;
        _decidedMembership = 0;
        _pendingMembership = 0;
        _pendingMembershipSince = -1f;
        _staleSince = -1f;
        _gateBlockClass = 0;
        _decisionRound = 0;
        Seats.Clear();
        SeatMotion.Clear();
        WorldUI.ModalFallback.NoteSharedGazeYaw(false, 0f, 0, why);
    }

    // ---- the party survey ------------------------------------------------------------------------

    /// <summary>Fill <see cref="Seats"/> with every head that is in this map room: the local camera
    /// plus every peer with a fresh in-room record and a live head holder. Read-only and strictly
    /// local — no sweep, no <c>FindObjectsOfType</c>.</summary>
    private static void CollectSeats()
    {
        Seats.Clear();
        Camera? cam = CanvasConversion.WorldCamera;
        if (cam != null)
            Seats.Add(new Seat(0, cam.transform.position));

        float now = Time.unscaledTime;
        foreach (KeyValuePair<int, PeerGaze> kv in Peers)
        {
            if (!kv.Value.InRoom || now - kv.Value.At > PeerStaleSeconds)
                continue;
            if (!NetAvatarDriver.TryGetPeerHeadHolder(kv.Key, out Transform holder) || holder == null)
                continue;
            Seats.Add(new Seat(kv.Key, holder.position));
        }
    }

    /// <summary>Advance the stillness bookkeeping for the seats just collected. A seat whose
    /// horizontal position has moved more than <see cref="SeatStillMeters"/> since it was last pinned
    /// re-pins here and re-starts its clock.</summary>
    private static void UpdateSeatMotion()
    {
        float now = Time.unscaledTime;
        for (int i = 0; i < Seats.Count; i++)
        {
            Seat s = Seats[i];
            Vector3 flat = new(s.HeadWorld.x, 0f, s.HeadWorld.z);
            if (SeatMotion.TryGetValue(s.PlayerId, out (Vector3 Pos, float MovedAt) held)
                && (flat - held.Pos).magnitude <= SeatStillMeters)
                continue;
            SeatMotion[s.PlayerId] = (flat, now);
        }
    }

    /// <summary>The seat set's MEMBERSHIP, folded to one order-independent number. Positions are
    /// deliberately absent: this decides when a frozen answer re-opens, and it must answer "somebody
    /// joined or left", never "somebody moved".</summary>
    private static int MembershipFold()
    {
        int fold = Seats.Count;
        for (int i = 0; i < Seats.Count; i++)
            unchecked { fold ^= (Seats[i].PlayerId + 1) * unchecked((int)0x9E3779B1); }
        return fold;
    }

    /// <summary>
    /// May the host decide yet? All three terms must hold together, or the cap must have expired.
    /// <paramref name="settled"/> is false for a decision forced by the cap, and the falsifier prints
    /// that word rather than claiming a settle it did not get.
    /// </summary>
    private static bool SettleGateOpen(out bool settled, out string why)
    {
        settled = false;
        float elapsed = Time.unscaledTime - _gateClockAt;
        if (elapsed < MinSettleSeconds)
        {
            why = $"the room has stood {elapsed:F1} s of the {MinSettleSeconds:F0} s floor";
            NoteGateBlocked(1, why);
            return false;
        }

        // (1) IS THE PARTY EVEN ASSEMBLED? Every player the session roster names must have published
        // a map-room record here. This is the term ModBuild 458 did not have and the one the 459 log
        // convicts it on: the decision was taken while MIXED SESSION CENSUS still read "1 player(s)".
        // An UNREADABLE roster is not a reason to block forever — the cap below still fires.
        RosterScratch.Clear();
        NetPlayerActors.CollectRoster(RosterScratch);
        int localId = NetPlayerActors.LocalPlayerId();
        int missing = 0;
        float now = Time.unscaledTime;
        for (int i = 0; i < RosterScratch.Count; i++)
        {
            int id = RosterScratch[i].Id;
            if (id <= 0 || id == localId)
                continue;
            if (!Peers.TryGetValue(id, out PeerGaze g) || now - g.At > PeerStaleSeconds)
                missing++;
        }

        // (2) HAS EVERYBODY STOPPED MOVING? "bevor alle Spieler am Tisch fertig gesetzt wurden",
        // measured rather than assumed after a fixed delay.
        int moving = 0;
        for (int i = 0; i < Seats.Count; i++)
        {
            if (!SeatMotion.TryGetValue(Seats[i].PlayerId, out (Vector3 Pos, float MovedAt) held)
                || now - held.MovedAt < SeatStillSeconds)
                moving++;
        }

        if (elapsed >= MaxSettleSeconds)
        {
            settled = false;
            why = $"THE {MaxSettleSeconds:F0} s CAP EXPIRED with {missing} roster player(s) still "
                  + $"silent and {moving} of {Seats.Count} seat(s) still moving — the party is NOT "
                  + "settled and this direction is the best answer available rather than the right "
                  + "one";
            return true;
        }
        if (missing > 0 || moving > 0)
        {
            why = $"{missing} roster player(s) have published no map-room record and {moving} of "
                  + $"{Seats.Count} seat(s) are still moving, at {elapsed:F1} s of the "
                  + $"{MaxSettleSeconds:F0} s cap";
            NoteGateBlocked(missing > 0 ? 2 : 3, why);
            return false;
        }
        settled = true;
        why = $"every roster player has a map-room record and all {Seats.Count} seat(s) have held "
              + $"within {SeatStillMeters:F2} m for {SeatStillSeconds:F1} s, at {elapsed:F1} s";
        return true;
    }

    /// <summary>
    /// Say WHICH term is holding the settle gate shut, once per change of term rather than at the
    /// 5 Hz sample rate.
    ///
    /// <para><b>WHY IT HAD TO EXIST.</b> Both decision rounds in the ModBuild 462 host log were taken
    /// on the 20 s cap — "1 of 1 seat(s) still moving" and then "2 of 2 seat(s) still moving" — so in
    /// two rounds on two machines the gate has never once been observed to OPEN. That is either a
    /// party that genuinely never stood still or a stillness term that cannot be satisfied, and
    /// nothing in either log could tell those apart, because a refusal returned false and printed
    /// nothing. This line is the difference: a run whose last gate line is class 3 and which then
    /// decides SETTLED proves the term is reachable.</para>
    /// </summary>
    private static void NoteGateBlocked(int blockClass, string why)
    {
        if (blockClass == _gateBlockClass)
            return;
        _gateBlockClass = blockClass;
        // HW-VERIFY: at most three of these per decision round (one per blocking term), and the LAST
        // one before a SHARED SEATS DECIDED line names what the gate was waiting on. Its total
        // absence in a room visit means the gate opened on the first sample past the floor.
        VRLog.Note(Scope, "SHARED SEATS — the settle gate is SHUT and the host has not decided a "
                          + $"spawn direction yet: {why}. Blocking term {blockClass} of 3 (1 = the "
                          + $"{MinSettleSeconds:F0} s floor, 2 = a roster player with no map-room "
                          + "record, 3 = a head that has not held still). Printed on the term's EDGE "
                          + "and never per sample. A room visit that reaches a SHARED SEATS DECIDED "
                          + "line saying SETTLE GATE OPENED proves every term here is reachable; one "
                          + "whose gate lines end at term 3 and then decides on the cap has a "
                          + "stillness test the party never satisfied, and THAT is the reading that "
                          + "would justify widening SeatStillMeters rather than guessing at it.");
    }

    // ---- the decision itself ---------------------------------------------------------------------

    /// <summary>
    /// DECIDE, for this membership, by SEARCH over the 256 yaws the wire byte can represent.
    ///
    /// <para>The search space IS the representable set, which is why the published byte carries no
    /// quantisation error at all: the winner is a step, not a float that has to be rounded to one.
    /// 256 candidates against at most four seats is a thousand dot products, once per room visit.</para>
    ///
    /// <para>THE ONE REFUSAL LEFT. No usable head at all — a host alone in a room whose camera has no
    /// tracked pose — publishes no bit and every client keeps the fixed table axis. ModBuild 458's
    /// other two refusals were properties of the mean of facings and are gone with it; see the class
    /// comment for why neither could survive contact with two people reading one table.</para>
    /// </summary>
    private static void Decide(int membership, bool settled, string gateWhy)
    {
        _decided = true;
        _decidedValid = false;
        _decidedYaw = 0;
        _decidedMembership = membership;

        if (!TryPrepareSearch(out Vector3 centreFlat, out float radiusWorld, out float minReadWorld,
                              out float frameScale))
        {
            // HW-VERIFY: the host could not decide at all because the shared frame is not there. Once
            // per decision round; its presence means every client in this session is on the fixed
            // table axis and the "im Sichtbereich" feature did nothing.
            VRLog.Note(Scope, "SHARED SEATS — the host cannot choose a spawn direction because "
                              + "MapRoomDriver.TryGetParchmentFrame has no frame yet, so no gaze bit "
                              + "is published for this decision round and EVERY client anchors a "
                              + "shared window on the fixed table axis, exactly as every build before "
                              + "the shared-direction round did. This is the correct fallback and not "
                              + "a degradation of anything that used to work.");
            return;
        }

        if (Seats.Count == 0)
        {
            // HW-VERIFY: once per decision round. Not an error — a host alone in a room whose head
            // camera has no tracked pose reaches it — but it is the line that says why the feature
            // stood down, and its absence is what says the feature ran.
            VRLog.Note(Scope, "SHARED SEATS — the host found NO head in the map room at all (no local "
                              + "head camera and no peer head holder), so it publishes no gaze bit "
                              + "for this decision round and every client anchors a shared window on "
                              + "the fixed table axis.");
            return;
        }

        SearchBestDirection(centreFlat, radiusWorld, minReadWorld, out byte bestStep,
                            out float bestWorst, out float bestMean, out float worstPossible);

        _decidedYaw = bestStep;
        _decidedValid = true;
        float yawDeg = NetProtocol.DecodeMapRoomGazeYaw(_decidedYaw);

        // HW-VERIFY: THE line this whole feature is verified by, once per decision round on the host.
        // WORST SEAT is the verdict: the largest reading angle any player at the table has to the
        // window's face, 0° dead square-on, 90° edge-on and past 90° the user's "nur von hinten
        // lesbar". Under ~70° is the fix working; over 90° on a party that is not two people facing
        // each other across a table is the fix failing.
        VRLog.Note(Scope, $"SHARED SEATS DECIDED (round {_decisionRound}, "
                          + (settled ? "SETTLED" : "*** MOVING ***")
                          + $") — {Seats.Count} seat(s) at the map table, and the host publishes step "
                          + $"{_decidedYaw} of {NetProtocol.MapRoomGazeYawSteps} = {yawDeg:F2}° as "
                          + "the direction every client seats a shared window in. WORST SEAT "
                          + $"{bestWorst:F1}°, MEAN {bestMean:F1}° — the reading angle between the "
                          + "window's own face and the line to that head, 0° square-on, 90° edge-on, "
                          + "past 90° the reader is behind it. THE WORST THIS TABLE COULD HAVE BEEN "
                          + $"was {worstPossible:F1}°, which is what a direction chosen from one "
                          + "player's glance can cost and is what ModBuild 459 shipped. THE OBJECTIVE "
                          + "IS THE WORST SEAT AND NOT THE MEAN: a mean lets one player be handed a "
                          + "170° back so long as the other gets a 5° front, which is exactly the "
                          + "picture the report is about. SEATS: " + SeatText(centreFlat, frameScale)
                          + $". SETTLE GATE: {gateWhy}. THE HOST DECIDES AND EVERY CLIENT OBEYS — "
                          + "this is what makes a shared window 1:1 here, not several machines "
                          + "searching over their own interpolated copies of the same heads and "
                          + "hoping. The step is FROZEN for this membership: it re-opens when a "
                          + "player joins or leaves the table, and — since ModBuild 463 and the "
                          + "2026-09-06 item 1 — when the standing direction has turned its back on "
                          + $"a seat ({StaleReadingDegrees:F0}°) and a re-decision would buy at least "
                          + $"{StaleImprovementDegrees:F0}° back. It never re-opens merely because "
                          + "somebody moved.");

        // The host never receives its own record, so it feeds its own decision in directly. Without
        // this the one client that MADE the decision would be the only one not using it.
        WorldUI.ModalFallback.NoteSharedGazeYaw(
            true, yawDeg, 0,
            $"this client is the HOST and decided in round {_decisionRound}: step {_decidedYaw} "
            + $"SCORED OVER {Seats.Count} SEAT(S), predicted worst seat {bestWorst:F1}°, SETTLE GATE "
            + (settled
                ? $"OPENED (all {Seats.Count} seat(s) held still and every roster player was present)"
                : $"TIMED OUT AT THE {MaxSettleSeconds:F0} s CAP, so the party was STILL MOVING and "
                  + "this direction was the best answer available rather than the right one"));
    }

    /// <summary>
    /// The geometry every score is measured in: the table centre flattened, the ring radius the
    /// anchor will actually seat a window on, and the reading-distance floor — all in WORLD units at
    /// the shared frame's own scale. Answers false when the parchment frame is not there, which is
    /// the one condition under which no direction can be chosen at all.
    ///
    /// <para>It exists as its own method so that <see cref="Decide"/> and
    /// <see cref="StaleDirectionReopen"/> cannot drift apart: a staleness test measured against a
    /// different radius from the one the decision used would arm on the difference between two
    /// geometries rather than on the party having moved.</para>
    /// </summary>
    private static bool TryPrepareSearch(out Vector3 centreFlat, out float radiusWorld,
                                         out float minReadWorld, out float frameScale)
    {
        centreFlat = Vector3.zero;
        radiusWorld = 0f;
        minReadWorld = 0f;
        if (!MapRoomDriver.TryGetParchmentFrame(out Vector3 centre, out frameScale)
            || frameScale <= 0f)
            return false;

        // The ring the anchor will seat this window on, in WORLD units, so the score is measured
        // against the place the window actually goes. ModalFallback.TrySharedAnchorOnTable builds the
        // same point: centre + ahead × radius, at the frame's own scale. The LATERAL step a second or
        // third window takes is not modelled here — it is a per-window half-width and this decision
        // is one direction for the whole room; the anchor's own SHARED WINDOW SEAT ANGLES line
        // measures the delivered angle per window, lateral step included, which is where a
        // disagreement between this prediction and the picture would show up.
        // THE SHIPPED CONSTANT, NOT THE LIVE DIAL, and that is the whole point of reading it here.
        // ModBuild 480 extended the shared-window law from SIZE to POSE: a shared window's spawn
        // radius may not be a function of anything client-local, so the SEAT now takes
        // Defaults.SharedWindowArcRadiusMeters and the viewer's [WorldUI] dial is inert for it.
        // This scorer predicts that seat. Reading the live dial here would make the prediction and
        // the delivered seat disagree by exactly the amount this viewer had tuned — an instrument
        // measuring a geometry nobody draws, which is the failure this project has paid for before.
        float radiusMeters = Defaults.SharedWindowArcRadiusMeters;
        radiusWorld = Mathf.Max(radiusMeters, 0.05f) * frameScale;
        minReadWorld = MinReadingMeters * frameScale;
        centreFlat = new Vector3(centre.x, 0f, centre.z);
        return true;
    }

    /// <summary>
    /// THE SEARCH, unchanged and in ONE place: over the 256 yaws the wire byte can represent, the
    /// candidate whose WORST seat is best off, ties broken on the mean. <paramref name="worstPossible"/>
    /// is the worst any candidate scored, which is what a direction chosen from one player's glance
    /// could have cost.
    ///
    /// <para>Lifted out of <see cref="Decide"/> verbatim so <see cref="StaleDirectionReopen"/> asks
    /// the SAME question the decision answers. Nothing about the objective, the tie-break or the
    /// 0.01° epsilons changed when it moved, and it has no term for how many seats there are: one
    /// seat, two adjacent, two opposed, three and four all fall out of the same loop, and zero seats
    /// never reaches it (both callers return before this on an empty seat list).</para>
    /// </summary>
    private static void SearchBestDirection(Vector3 centreFlat, float radiusWorld, float minReadWorld,
                                            out byte bestStep, out float bestWorst,
                                            out float bestMean, out float worstPossible)
    {
        int best = 0;
        bestWorst = float.MaxValue;
        bestMean = float.MaxValue;
        worstPossible = 0f;
        for (int step = 0; step < NetProtocol.MapRoomGazeYawSteps; step++)
        {
            float rad = NetProtocol.DecodeMapRoomGazeYaw((byte)step) * Mathf.Deg2Rad;
            var ahead = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            ScoreDirection(centreFlat, ahead, radiusWorld, minReadWorld, out float worst,
                           out float mean);
            if (worst > worstPossible)
                worstPossible = worst;
            if (worst < bestWorst - 0.01f || (worst < bestWorst + 0.01f && mean < bestMean - 0.01f))
            {
                best = step;
                bestWorst = worst;
                bestMean = mean;
            }
        }
        bestStep = (byte)best;
    }

    /// <summary>
    /// Score one candidate direction: the WORST and the MEAN reading angle over the party's seats.
    ///
    /// <para>The window would be seated at <c>centre + ahead × radius</c> and yawed square to
    /// <paramref name="ahead"/>; a uGUI canvas renders its front along −forward, so its readable face
    /// points along <c>−ahead</c> — the same convention <c>ModalFallback.TrySharedAnchorOnTable</c>
    /// writes the pose with. The angle for one seat is therefore the angle between <c>−ahead</c> and
    /// the line from the window to that head.</para>
    ///
    /// <para>Horizontal throughout, because the facing is yaw-only by standing ruling and the
    /// complaint is a yaw complaint. The height difference between a head and a window hung at
    /// grab-bar height is common to every candidate and cannot change which one wins.</para>
    /// </summary>
    private static void ScoreDirection(Vector3 centreFlat, Vector3 ahead, float radiusWorld,
                                       float minReadWorld, out float worst, out float mean)
    {
        worst = 0f;
        float sum = 0f;
        Vector3 windowAt = centreFlat + ahead * radiusWorld;
        for (int i = 0; i < Seats.Count; i++)
        {
            Vector3 head = Seats[i].HeadWorld;
            Vector3 d = new Vector3(head.x, 0f, head.z) - windowAt;
            float dist = d.magnitude;
            float angle = dist > 1e-4f ? Vector3.Angle(-ahead, d / dist) : 180f;
            // A seat INSIDE the reading distance is square-on to the window precisely because the
            // window is in its face and between it and the map. The angle cannot see that; this can.
            if (dist < minReadWorld)
                angle = Mathf.Max(angle, 180f * (1f - dist / Mathf.Max(minReadWorld, 1e-4f)));
            sum += angle;
            if (angle > worst)
                worst = angle;
        }
        mean = Seats.Count > 0 ? sum / Seats.Count : 0f;
    }

    /// <summary>The seats, in the shared frame's own metres about the table centre, for the
    /// falsifier. Frame-local so two logs are comparable without converting anything.</summary>
    private static string SeatText(Vector3 centreFlat, float frameScale)
    {
        var sb = new System.Text.StringBuilder(96);
        float scale = Mathf.Max(frameScale, 1e-4f);
        for (int i = 0; i < Seats.Count; i++)
        {
            Seat s = Seats[i];
            Vector3 off = (new Vector3(s.HeadWorld.x, 0f, s.HeadWorld.z) - centreFlat) / scale;
            if (i > 0)
                sb.Append(", ");
            sb.Append(s.PlayerId == 0 ? "THIS CLIENT" : $"player {s.PlayerId}");
            sb.Append($" at ({off.x:F2},{off.z:F2}) m, {off.magnitude:F2} m out");
        }
        return sb.ToString();
    }
}
