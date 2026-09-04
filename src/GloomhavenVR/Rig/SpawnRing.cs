using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using Script.Controller;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// MULTIPLAYER JOIN COMFORT — where a VR player is put down when they arrive at the table.
///
/// <para>THE REQUIREMENT (user, MP session 2026-08, verbatim): "Ich hätte erwartet, dass ich
/// GEGENÜBER des Mitspielers spawne und direkt seine Maske sehe, aber mit etwas Abstand, so dass
/// ich nicht direkt über dem Spielfeld und auch nicht vor dem Spielfeld spawne — nur eben
/// gegenüber." Three separate constraints, and the geometry below answers them one by one:
/// ACROSS the board from the peer (azimuth), FACING them / the board centre (yaw), and AT THE
/// TABLE EDGE — neither hovering over the diorama nor parked a room's length in front of it
/// (radius).</para>
///
/// <para>WHY THE ROUND-1 RING NEVER RAN (hardware log 2026-08-02, ModBuild 20, proven — not
/// inferred). The client's log holds exactly one scenario recenter line:
/// <c>"Recentered — … (table-edge seat, spawn ring deferred: SinglePlayer)"</c>, and four lines
/// earlier <c>"Broadcast WAITING: session online but our NetworkPlayer has no id yet (join
/// handshake)"</c>. The old solver's FIRST statement was
/// <c>NetPlayerActors.LocalStableIndex(out total); if (total &lt;= 1) return SinglePlayer;</c> —
/// and <c>total</c> comes from <c>FFSNet.PlayerRegistry.Participants</c>, which is
/// <c>AllPlayers.FindAll(x =&gt; x.IsParticipant)</c> gated on <c>PlayerRegistry.MyPlayer</c>
/// existing. During a JOIN that list is still empty: the local NetworkPlayer entity is attached
/// several seconds later (the same log's "Broadcast LIVE as player 2" lands AFTER the recenter).
/// So on the one client that needed it, the ring measured "single player" while a peer's head
/// packets were already arriving (<c>"FIRST PACKET RECEIVED — from player 1"</c> precedes the
/// scenario rig build), took the strict no-op branch, and every later retry was blocked by the
/// same participant-count probe. Nothing else logged because the give-up line was written only
/// for the <c>BoardPending</c> outcome.</para>
///
/// <para>THE FIX IS THE INPUT, NOT THE MATH: the participant registry is no longer allowed to
/// decide anything. The mod already knows the one fact the seat actually needs — WHERE THE OTHER
/// PLAYERS ARE — because peer head poses ride the embodiment rig packets
/// (<see cref="NetAvatarDriver.CollectPeerHeads"/>). A single known peer pose is both necessary
/// and sufficient to seat someone opposite them; the registry is kept only as CONTEXT in the log
/// lines, and <c>FFSNetwork.IsOnline</c> (the session flag the whole net layer already trusts) is
/// what separates "single player, do nothing" from "multiplayer, keep waiting for a peer".</para>
///
/// <para>EVERY OUTCOME LOGS (user requirement: a future hardware test must be diagnosable from the
/// log alone). <see cref="Solve"/> is pure and allocation-light so the caller can poll it and
/// report; <see cref="Probe"/> carries the raw counts each decision was made on, so a log line
/// says not only WHAT happened but on what evidence.</para>
///
/// <para>MULTIPLAYER SAFETY: read-only and purely LOCAL. Nothing here mutates game state, nothing
/// is sent, and no wire field was added — the peer poses come from packets the embodiment sync
/// receives anyway. The resulting head pose is broadcast like any other head pose, so peers see us
/// arrive at the free seat for free.</para>
/// </summary>
internal static class SpawnRing
{
    /// <summary>
    /// Standing clearance between the board's own EDGE and the player, real metres.
    ///
    /// <para>THE ROUND-1 RADIUS WAS MEASURED FROM THE WRONG THING. It took the CIRCUMSCRIBING
    /// radius of the whole footprint (<c>sqrt(halfX² + halfZ²)</c>) and used it in every
    /// direction, so on the short side of any non-square board the seat sat that much too far
    /// out — the "nicht vor dem Spielfeld" half of the requirement — and on a big board the 2.2 m
    /// ceiling then dropped the player INSIDE the footprint instead, i.e. "über dem Spielfeld".
    /// The radius is now measured ALONG THE CHOSEN DIRECTION (<see cref="EdgeDistanceWorld"/>):
    /// where the board really ends on the side we are seating on, plus this clearance. That reads
    /// as standing at the table on every board shape, size and zoom level.</para>
    /// </summary>
    internal const float EdgeClearanceMeters = 0.35f;

    /// <summary>
    /// Extra height a RING seat adds on top of the standing eye preset, real metres (user ruling
    /// 2026-08-04: every player spawns "etwas höher als das Spielfeld selber"). Consumed by
    /// <c>VRRigDriver.ApplyRingSeat</c>, which since 2026-09-04 is also the B+Y recenter's path —
    /// the "ring seats only" carve-out is gone, because the user asked for the recenter to land at
    /// the arrival seat "der bereits die Regeln enthält" and this height is one of those rules.
    /// 0.35 m lifts the vantage noticeably above the board plane without reading as flying; from
    /// there stick flight puts the player wherever they like.
    /// </summary>
    internal const float RingSeatLiftMeters = 0.35f;

    /// <summary>
    /// Seat radius floor, real metres — never closer to the board centre than the ordinary
    /// table-edge seat has always been (<see cref="ComfortSettings.EffectiveEyeBackMeters"/>,
    /// 0.70 m). A scenario that has revealed only its starting room has a footprint a few
    /// centimetres across at table scale; without this floor two players would be seated nose to
    /// nose over it, which is the very complaint this feature exists for.
    /// </summary>
    internal static float MinRadiusMeters => ComfortSettings.EffectiveEyeBackMeters;

    /// <summary>
    /// Ceiling on the CLEARANCE BEYOND THE BOARD EDGE, real metres — never on the absolute radius.
    ///
    /// <para>THE ROUND-3 CEILING COULD SEAT THE PLAYER INSIDE THE BOARD. It read
    /// <c>Mathf.Clamp(edge + clearance, 0.70f, 3.0f)</c>: an ABSOLUTE 3 m ceiling on a radius whose
    /// floor is the board's own edge distance, so on any board whose edge distance exceeded 2.65
    /// apparent metres it pulled the seat back INSIDE the footprint it exists to sit outside of.
    /// BE PRECISE ABOUT WHAT THAT IS AND IS NOT: it is a LATENT defect, not the mechanism behind
    /// the 2026-09-02 report — the boards in that day's logs have edge distances of 0.34 m and
    /// 0.56 m and never came near the ceiling; that report is the ring not running at all
    /// (<c>VRRigDriver.TickSpawnRingSettle</c>). It is fixed here anyway for two reasons: a ceiling
    /// whose failure mode is the exact defect it sits next to is the wrong shape, and the framing
    /// back-off below now deliberately drives the radius well past 3 m on a large board (a 40x26
    /// hex dungeon at rig scale 8 solves to 4.07 m), which would have walked straight into it.</para>
    ///
    /// <para>THE CEILING IS NOW EDGE-RELATIVE, so it can only ever limit how far OUT we go. The
    /// stray-tile case it was written for is still covered and is now covered in the SAFE
    /// direction: a tile parked far off the map inflates <c>edgeMeters</c>, which puts the seat
    /// further back — a bad view, never a seat in the middle of the diorama.</para>
    /// </summary>
    internal const float MaxEdgeClearanceMeters = 3.0f;

    /// <summary>
    /// The largest angle the board's own footprint may subtend from the seat, degrees — the
    /// "Sicht auf das ganze Spielfeld" half of the requirement, expressed as something that can be
    /// MEASURED on the resulting pose rather than assumed from the fact that a seat was computed.
    ///
    /// <para>WHY A SUBTENDED ANGLE AND NOT AN OFFSET FROM FORWARD. The rig is yaw-only by design
    /// (a pitched rig tilts the horizon), so the seat's forward is horizontal while the board lies
    /// below it — every board is therefore "below the centre of the frame", and a cone drawn about
    /// forward would fail every seat the user has already accepted. What actually decides whether
    /// the whole play field is visible is how WIDE it is from where you stand: a VR player pitches
    /// their head freely, so the board only stops fitting when its angular DIAMETER exceeds what a
    /// glance covers. 100° is generous against a Quest 3's ~110° horizontal FOV and leaves the
    /// already-accepted arrival poses untouched — the 2026-09-02 seat that DID work subtends 65°,
    /// so this bound never fires on it (<see cref="Framing"/>).</para>
    /// </summary>
    internal const float MaxViewSpanDegrees = 100f;

    /// <summary>Step by which the seat backs away from the board when
    /// <see cref="MaxViewSpanDegrees"/> is exceeded, real metres. Backing off is MONOTONE in the
    /// subtended angle (further away ⇒ smaller), so the search below always converges or hits
    /// <see cref="MaxEdgeClearanceMeters"/> — and either way the result is logged.</summary>
    private const float FramingStepMeters = 0.10f;

    /// <summary>Hard iteration cap for the framing back-off (defence in depth: the step and the
    /// ceiling already bound it). Runs at most a handful of times per scenario.</summary>
    private const int MaxFramingSteps = 40;

    /// <summary>
    /// Head height a RING seat ends at, real metres above the orbit focus plane — the standing eye
    /// preset plus <see cref="RingSeatLiftMeters"/>.
    ///
    /// <para>IT LIVES HERE, NOT ONLY AT THE CALL SITE, because the solver has to be able to judge
    /// its OWN result: the picture the player gets depends on the head's height, and a framing test
    /// run on the flat seat point rather than on the head would be a flawless measurement of the
    /// wrong stage. <c>VRRigDriver.ApplyRingSeat</c> reads the same property, so there is exactly
    /// one definition of how high a ring arrival is.</para>
    /// </summary>
    internal static float RingHeadAboveFocusMeters =>
        ComfortSettings.EffectiveEyeHeightMeters + RingSeatLiftMeters;

    /// <summary>What <see cref="Solve"/> could do with the world it found. Every value has its
    /// own log line at the call site — there is no unlogged path.</summary>
    internal enum Outcome
    {
        /// <summary>Retired. Single player used to be a strict no-op here — until a solo test
        /// spawned the player INSIDE the board (user ruling 2026-08-03: "nutze auch im Singleplayer
        /// den Spawnring"). Solo now takes the SAME radius solve, so this value is never returned;
        /// it is kept so the enum's numbering and the log switch below stay stable.</summary>
        Offline,

        /// <summary>Multiplayer, but nobody else's position is known yet — no peer has sent a rig
        /// packet and the participant registry does not (yet) admit to a second player. The caller
        /// keeps the ordinary seat and retries while its window is open.</summary>
        PeersUnknown,

        /// <summary>Multiplayer with peers, but the scenario's hex tiles are not in the object
        /// cache yet, so there is no board to sit around. The caller retries.</summary>
        BoardPending,

        /// <summary>A seat was computed; <c>seat</c> is filled in.</summary>
        Placed,
    }

    /// <summary>
    /// The RAW EVIDENCE a decision was made on, so every log line can state it. Cheap to fill and
    /// filled on EVERY path (including the ones that place nothing) — that is the point: a future
    /// hardware log must show not just "did nothing" but "did nothing because online=False,
    /// participants=1, peer poses=0, tiles=0".
    /// </summary>
    internal struct Probe
    {
        /// <summary>The game's own session flag (<c>FFSNetwork.IsOnline</c>).</summary>
        public bool SessionOnline;

        /// <summary>Whether we are the Bolt host (log context only — the seat never depends on it,
        /// which is exactly the "works regardless of who is host" requirement).</summary>
        public bool IsHost;

        /// <summary>FFSNet participant count. CONTEXT ONLY — it lags the join handshake by
        /// seconds and is what silently disabled the round-1 ring (class doc).</summary>
        public int Participants;

        /// <summary>How many peers we hold a replicated head pose for. THE decisive input.</summary>
        public int PeerPoses;

        /// <summary>Live hex tiles in the scenario object cache (0 = no board built yet).</summary>
        public int Tiles;

        /// <summary>One-line rendering for the log.</summary>
        public override string ToString() =>
            $"online={SessionOnline}, host={IsHost}, peer pose(s)={PeerPoses}, " +
            $"FFSNet participants={Participants}, board tiles={Tiles}";
    }

    /// <summary>
    /// THE BOARD AS A RECTANGLE, measured from the scenario's own hex tiles — the one input every
    /// decision in this file is made on, hoisted out of the solver so the CALLER can ask the same
    /// question about a pose it did not compute (namely: is the player's own current head inside
    /// the diorama?). <see cref="TryFootprint"/> fills it; <see cref="Valid"/> is false while the
    /// scenario has no tiles in the object cache.
    /// </summary>
    internal struct Footprint
    {
        /// <summary>False while the board does not exist yet — nothing else is meaningful then.</summary>
        public bool Valid;

        /// <summary>Board centre, xz from the tile footprint, y on the orbit focus plane.</summary>
        public Vector3 Center;

        /// <summary>Half-extents (x, z) in WORLD units, measured from tile ORIGINS.</summary>
        public Vector2 HalfWorld;

        /// <summary>Half a hex, world units — the outer tiles' own extent, which tile origins
        /// do not include.</summary>
        public float TileHalfWorld;

        /// <summary>Live tile count. The caller watches this for "the board has stopped growing"
        /// and for "a room opened, re-measure".</summary>
        public int TileCount;

        /// <summary>Outer half-extents (x, z), world units — origins plus half a hex. THIS is the
        /// rectangle a head is inside or outside of.</summary>
        public Vector2 OuterHalfWorld => new(HalfWorld.x + TileHalfWorld, HalfWorld.y + TileHalfWorld);
    }

    /// <summary>
    /// WHAT THE PLAYER ACTUALLY SEES from a given head pose — the acceptance test, and the only
    /// thing in this file that judges a RESULT rather than producing one.
    ///
    /// <para>WHY IT EXISTS. The user's requirement has always been two statements, and round 3
    /// verified neither: "nicht IM Spielfeld" and "mit der Sicht auf das ganze Spielfeld". The old
    /// proof line reported the radius, the clearance and the footprint — everything about how the
    /// seat was DERIVED, and nothing about whether the derivation achieved anything. A seat clamped
    /// to 3 m on a 7 m board printed a perfectly healthy line while standing in the middle of the
    /// dungeon. Both predicates below are computed from the FINAL head pose, including the ring
    /// lift, and both are printed.</para>
    ///
    /// <para>IT IS ALSO THE CONSENT TEST. <c>VRRigDriver</c> runs it against the player's OWN head
    /// after they have moved themselves, which is how the ring tells "I chose this vantage" apart
    /// from "I am flying because I was dropped inside the board" — see
    /// <c>VRRigDriver.NotifyPlayerLocomotion</c>.</para>
    /// </summary>
    internal struct Framing
    {
        /// <summary>Signed distance from the head's HORIZONTAL position to the footprint rectangle,
        /// real metres: positive = clear of the board by this much, negative = that far inside it.
        /// (Box distance: the exterior term is the true euclidean distance to the rectangle, the
        /// interior term is the distance to the nearest edge.)</summary>
        public float EdgeMarginMeters;

        /// <summary>Head height above the board plane, real metres.</summary>
        public float AboveBoardMeters;

        /// <summary>True when the head is horizontally within the footprint — "über/IM Spielfeld".
        /// A SEAT is never allowed to be here.</summary>
        public bool OverBoard;

        /// <summary>Angular DIAMETER of the footprint as seen from the head, degrees: the largest
        /// angle between any two of its four corners. This is the "ganze Spielfeld im Blick"
        /// number, and it is what <see cref="MaxViewSpanDegrees"/> bounds.</summary>
        public float SpanDegrees;

        /// <summary>Depression of the NEAREST footprint corner below the horizon, degrees — how far
        /// down the player has to look to see the near edge. Reported, not bounded: a VR player
        /// pitches their head freely, so this is context for reading a complaint, not a gate.</summary>
        public float NearDepressionDegrees;

        /// <summary>The whole requirement in one bool: outside the footprint AND the board fits in
        /// a glance. What a SEAT this class produces must satisfy.</summary>
        public bool Acceptable => !OverBoard && SpanDegrees <= MaxViewSpanDegrees;

        /// <summary>
        /// DOWN AMONG THE TILES — horizontally over the play field AND lower than the height a ring
        /// arrival itself would have given. This is the ONE state in which the ring is allowed to
        /// overrule the player's own locomotion, and the height term is what makes that safe: a
        /// player hovering high over the board has an overview and is left alone, a player standing
        /// in the middle of the dungeon at figure height is the 2026-09-02 complaint.
        /// </summary>
        public bool InsideTheDiorama => OverBoard && AboveBoardMeters < RingHeadAboveFocusMeters;

        /// <summary>One-line rendering for the log — the PICTURE, in numbers.</summary>
        public override string ToString() =>
            $"{(OverBoard ? "OVER THE BOARD" : "clear of the board")} by {EdgeMarginMeters:F2} m, " +
            $"{AboveBoardMeters:F2} m above the board plane, footprint subtends {SpanDegrees:F0}deg " +
            $"(bound {MaxViewSpanDegrees:F0}deg), near edge {NearDepressionDegrees:F0}deg below the horizon";
    }

    /// <summary>The computed seat, in world space. All angles are world azimuths in degrees
    /// (<c>Atan2(x, z)</c>, normalised to [0,360)), i.e. the same convention on every client.</summary>
    internal struct Seat
    {
        /// <summary>True when this seat was solved WITHOUT peers (single player): the azimuth is
        /// the ordinary scenario base yaw and only the radius came from the board footprint.</summary>
        public bool Solo;

        /// <summary>RECENTER SEATS ONLY (<see cref="TryRecenterSeat"/>): true when the azimuth is
        /// the one the spawn ring seated this player at on arrival, false when no ring seat was
        /// ever placed and the seat kept the side of the table the player was already on. Log
        /// material — it is the field that says WHICH of the two rules the chord took.</summary>
        public bool AzimuthRemembered;

        /// <summary>Board centre, xz from the tile footprint, y on the orbit focus plane (the
        /// plane the eye height is measured from — unchanged from the ordinary seat).</summary>
        public Vector3 Center;

        /// <summary>Board half-extents (x, z) in real metres — the footprint the seat was derived
        /// from, logged so a wrong seat can be blamed on the right thing.</summary>
        public Vector2 BoardHalfMeters;

        /// <summary>Distance from the board centre to the footprint EDGE along
        /// <see cref="AngleDegrees"/>, real metres. The seat is this + <see cref="EdgeClearanceMeters"/>
        /// before clamping.</summary>
        public float EdgeMeters;

        /// <summary>Seat radius, real metres — what the config/log talks about.</summary>
        public float RadiusMeters;

        /// <summary>Seat radius in world units (<see cref="RadiusMeters"/> × rig scale).</summary>
        public float RadiusWorld;

        /// <summary>Clearance the seat ended up with BEYOND the board edge, real metres. Starts at
        /// <see cref="EdgeClearanceMeters"/> and is only ever grown, by the framing pass.</summary>
        public float ClearanceMeters;

        /// <summary>How many <see cref="FramingStepMeters"/> back-off steps the framing pass had to
        /// take to fit the board in a glance. 0 on every board that already fitted — which is every
        /// board the user has accepted so far, so this is also the "did anything change?" field.</summary>
        public int FramingSteps;

        /// <summary>True when <see cref="MinRadiusMeters"/> raised the radius (a board so small the
        /// ordinary table-edge distance is still further out). Logged, never silent.</summary>
        public bool RadiusRaisedToFloor;

        /// <summary>WHAT THE PLAYER WILL SEE from this seat — measured on the final head pose,
        /// lift included. The seat is only returned as <see cref="Outcome.Placed"/> when this is
        /// <see cref="Framing.Acceptable"/>, or when the back-off ran out of room and said so.</summary>
        public Framing Framing;

        /// <summary>The world head position this seat resolves to, lift included — the pose
        /// <see cref="Framing"/> was measured at, so the log's picture and the log's coordinates
        /// can never describe two different places.</summary>
        public Vector3 HeadWorld;

        /// <summary>Chosen world azimuth, degrees.</summary>
        public float AngleDegrees;

        /// <summary>Angular distance to the NEAREST peer at <see cref="AngleDegrees"/> (degrees).
        /// This is the quantity the solver maximises; 180 for the single-peer case, which IS
        /// "opposite them". 360 when no peer pose is known.</summary>
        public float MinGapDegrees;

        /// <summary>How many peers had a usable replicated head pose when this was solved.</summary>
        public int PeerCount;

        /// <summary>True when no peer pose was available and the seat is the deterministic
        /// participant-index guess rather than a measured largest-gap bisector.</summary>
        public bool FromIndexFallback;

        /// <summary>Live hex-tile count the footprint was measured on. The caller watches it for
        /// "a room opened, the board grew, re-measure".</summary>
        public int TileCount;

        /// <summary>Where the head should end up, on the focus plane (the caller adds eye height).</summary>
        public Vector3 HeadFlat;

        /// <summary>Yaw-only rotation that looks from <see cref="HeadFlat"/> at
        /// <see cref="Center"/> — "face the board", and therefore the peer beyond it.</summary>
        public Quaternion Yaw;
    }

    /// <summary>Scratch list for peer head positions. Reused: the solve runs a few dozen times per
    /// scenario at most, but allocating in a rig path is a habit worth not having.</summary>
    private static readonly List<Vector3> HeadScratch = new(8);

    /// <summary>Scratch list for peer azimuths (parallel to <see cref="HeadScratch"/>).</summary>
    private static readonly List<float> AngleScratch = new(8);

    /// <summary>Scratch for the footprint's four corners (<see cref="Frame"/>). Reused so the
    /// acceptance test allocates nothing — it runs inside the framing back-off loop.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>
    /// Solve the join seat. Pure and read-only: safe to call on a poll, and the caller applies
    /// the result only on <see cref="Outcome.Placed"/>.
    /// </summary>
    /// <param name="focusPoint">The orbit focus (<c>CameraController.FocusPoint</c>) — used ONLY
    /// for the seat's ground-plane height, so the vertical convention is byte-for-byte the one the
    /// ordinary seat has always used. The horizontal centre comes from the board itself.</param>
    /// <param name="baseYaw">The scenario rig's frozen flat board yaw. Only feeds the
    /// zero-peer index fallback, so that "seat 0" is the vantage the flat game would have given
    /// the player.</param>
    /// <param name="rigScale">Rig root scale (world units per real metre).</param>
    /// <param name="seat">The computed seat (only meaningful on <see cref="Outcome.Placed"/>).</param>
    /// <param name="probe">The raw evidence, filled on EVERY outcome — for the log line.</param>
    internal static Outcome Solve(Vector3 focusPoint, Quaternion baseYaw, float rigScale,
                                  out Seat seat, out Probe probe)
    {
        // NEVER LET A COMFORT FEATURE COST THE RECENTER. The caller does nothing on anything but
        // Placed, so a throw in here degrades to "no ring", never to "no seat" — this is the whole
        // reason the try/catch sits at the entry point.
        try
        {
            return SolveCore(focusPoint, baseYaw, rigScale, out seat, out probe);
        }
        catch (System.Exception e)
        {
            HeadScratch.Clear();
            AngleScratch.Clear();
            seat = default;
            probe = default;
            VRLog.Warn("Rig", $"Spawn ring: solve threw ({e.Message}) — treating it as 'no session' " +
                              "and keeping the ordinary table-edge seat.");
            return Outcome.Offline;
        }
    }

    private static Outcome SolveCore(Vector3 focusPoint, Quaternion baseYaw, float rigScale,
                                     out Seat seat, out Probe probe)
    {
        seat = default;
        probe = default;

        // ---- evidence ---------------------------------------------------------------------
        // Gathered FIRST and in full, even on the paths that place nothing, so the caller's log
        // line can always state what the decision was made on.
        probe.SessionOnline = FFSNetwork.IsOnline;
        probe.IsHost = probe.SessionOnline && FFSNetwork.IsHost;
        probe.Tiles = TileCount();
        int localIndex = NetPlayerActors.LocalStableIndex(out int participants);
        probe.Participants = participants;

        HeadScratch.Clear();
        probe.PeerPoses = NetAvatarDriver.CollectPeerHeads(HeadScratch);

        // SINGLE PLAYER TAKES THE SAME RING (user ruling 2026-08-03: "Ich bin in meinem Test
        // (Singleplayer) IN dem Spielfeld gespawned, das darf nicht sein — nutze auch im
        // Singleplayer den Spawnring"). It used to return Offline here and keep the ordinary seat,
        // whose distance came from the comfort eye-back constant and knows nothing about how big
        // THIS scenario's board is — so on a large board that seat lands on the play field.
        //
        // What solo needs from the ring is only the RADIUS half: there is nobody to sit across
        // from, so the azimuth stays the one the ordinary seat would have used (the scenario base
        // yaw), and the player is simply pushed out to the board edge on that side plus the
        // standing clearance. Same footprint measurement, same clamps, same log line — the only
        // difference from multiplayer is where the angle comes from.
        bool solo = !probe.SessionOnline;

        // NOBODY TO SIT ACROSS FROM YET. A seat needs either a peer's actual position (the good
        // case) or at least the knowledge that a second participant exists (the index guess).
        // Neither ⇒ keep the ordinary seat and let the caller retry; a placement made now would be
        // a coin flip that the one allowed correction then has to undo.
        if (!solo && probe.PeerPoses == 0 && probe.Participants <= 1)
        {
            HeadScratch.Clear();
            return Outcome.PeersUnknown;
        }

        if (!TryFootprint(focusPoint.y, out Footprint board))
        {
            HeadScratch.Clear();
            return Outcome.BoardPending; // scenario tiles not spawned yet — the caller retries
        }

        Vector3 center = board.Center;

        float scale = rigScale > 0.0001f ? rigScale : 1f;

        // ---- WHICH WAY: across the board from everyone already there ------------------------
        float angle;
        float minGap;
        bool fallback;

        AngleScratch.Clear();
        if (solo)
            HeadScratch.Clear(); // no peers by definition — the azimuth comes from the base yaw
        for (int i = 0; i < HeadScratch.Count; i++)
        {
            Vector3 d = HeadScratch[i] - center;
            d.y = 0f;
            if (d.sqrMagnitude < 1e-6f)
                continue; // a peer standing exactly on the centre has no azimuth to avoid
            AngleScratch.Add(Azimuth(d));
        }
        HeadScratch.Clear();

        if (solo)
        {
            // SOLO: keep the direction the ordinary seat faces from; only the distance changes.
            Vector3 fwd = baseYaw * Vector3.forward;
            fwd.y = 0f;
            angle = Azimuth(fwd.sqrMagnitude > 1e-6f ? fwd : Vector3.forward);
            minGap = 360f;
            fallback = false;
            seat.PeerCount = 0;
            seat.Solo = true;
        }
        else if (AngleScratch.Count > 0)
        {
            angle = LargestGapBisector(AngleScratch, out minGap);
            fallback = false;
            seat.PeerCount = AngleScratch.Count;
        }
        else
        {
            // BEST GUESS WHEN NOBODY HAS ARRIVED ON THE WIRE YET (we joined faster than the peers'
            // 15 Hz rig packets, or the other participants are flat players who never send one).
            // The deterministic participant index gives distinct azimuths WITHOUT any knowledge,
            // laid out in the shared WORLD frame. The caller's one correction replaces it with a
            // measured seat the instant a real peer pose lands.
            angle = IndexFallbackAngle(baseYaw, localIndex, participants);
            minGap = 360f / Mathf.Max(participants, 1);
            fallback = true;
            seat.PeerCount = 0;
        }
        AngleScratch.Clear();

        FillSeatGeometry(board, angle, scale, ref seat);
        seat.MinGapDegrees = minGap;
        seat.FromIndexFallback = fallback;
        return Outcome.Placed;
    }

    /// <summary>
    /// THE GEOMETRY RULE — how far out the seat sits, how high it is and which way it faces —
    /// at an azimuth the CALLER has already chosen. Everything about a seat except WHICH SIDE of
    /// the table it is on.
    ///
    /// <para>IT IS A SEPARATE METHOD BECAUSE IT HAS TWO CALLERS AND MUST HAVE ONE DEFINITION.
    /// <see cref="SolveCore"/> reaches it after picking the azimuth across from the peers, and
    /// <see cref="TryRecenterSeat"/> reaches it with an azimuth that was decided long ago — the
    /// B+Y recenter chord (user, 2026-09-04: "allerdings ist der Spawnpunkt IN dem Spielfeld wenn
    /// man sich so recentered. Ich will das der Spawnpunkt derselbe ist an dem man am Anfang auch
    /// reingespawnt ist, der bereits die Regeln enthält"). "Die Regeln" are exactly the three
    /// below, and copying them into the recenter instead of sharing them is how the two would
    /// drift apart on the next board-shape change.</para>
    ///
    /// <para>UNITS, STATED ONCE. <paramref name="scale"/> is the rig root's scale: WORLD UNITS PER
    /// REAL METRE. Everything named <c>...Meters</c> in here is a REAL metre (what the player's
    /// body feels) and everything named <c>...World</c> is a world unit (what the board is measured
    /// in). The board arrives in world units and is divided by the scale exactly once
    /// (<c>edgeMeters</c>); the radius is decided in real metres and multiplied back exactly once
    /// (<c>radiusWorld</c>). That is why a zoomed-in player still ends up standing 0.35 real metres
    /// beyond the board's edge rather than 0.35 world units from it.</para>
    /// </summary>
    /// <param name="board">The measured footprint.</param>
    /// <param name="angleDegrees">World azimuth of the side of the table to sit on.</param>
    /// <param name="scale">Rig root scale, world units per real metre (already sanitised).</param>
    /// <param name="seat">Filled in; fields the caller owns (Solo, PeerCount, MinGapDegrees,
    /// FromIndexFallback, AzimuthRemembered) are left untouched.</param>
    private static void FillSeatGeometry(in Footprint board, float angleDegrees, float scale,
                                         ref Seat seat)
    {
        Vector3 center = board.Center;
        Vector3 dir = Quaternion.AngleAxis(angleDegrees, Vector3.up) * Vector3.forward;

        // ---- HOW FAR: at the table edge on THAT side, then FAR ENOUGH TO SEE IT ALL ---------
        // Edge distance along the chosen direction (not the circumscribing radius — see
        // EdgeClearanceMeters), plus a standing clearance. TWO RULES, in this order, and the order
        // is the whole point:
        //
        //   1. NEVER INSIDE THE BOARD. The radius floor is the board's own edge plus the clearance,
        //      and no ceiling may undercut it — that is the defect of 2026-09-02 (MaxEdgeClearanceMeters).
        //   2. THE WHOLE FIELD IN ONE GLANCE. Back away in FramingStepMeters steps while the
        //      footprint subtends more than MaxViewSpanDegrees from the resulting HEAD pose, lift
        //      included. Backing off shrinks the subtended angle monotonically, so this converges
        //      or exhausts MaxEdgeClearanceMeters — and either way the achieved picture is in the
        //      seat and therefore in the log. On every board small enough to already fit (which is
        //      every board the user has accepted so far) the loop body never runs and the seat is
        //      byte-for-byte the hand-tuned arrival pose of ModBuild 31.
        float edgeMeters = EdgeDistanceWorld(board.OuterHalfWorld, dir) / scale;
        float headAbove = RingHeadAboveFocusMeters;

        float clearance = EdgeClearanceMeters;
        float radiusMeters = SeatRadius(edgeMeters, clearance, out bool raisedToFloor);
        Vector3 headWorld = HeadAt(center, dir, radiusMeters, headAbove, scale);
        Framing framing = Frame(board, headWorld, scale);

        int framingSteps = 0;
        while (!framing.Acceptable && clearance < MaxEdgeClearanceMeters && framingSteps < MaxFramingSteps)
        {
            clearance = Mathf.Min(clearance + FramingStepMeters, MaxEdgeClearanceMeters);
            radiusMeters = SeatRadius(edgeMeters, clearance, out raisedToFloor);
            headWorld = HeadAt(center, dir, radiusMeters, headAbove, scale);
            framing = Frame(board, headWorld, scale);
            framingSteps++;
        }

        float radiusWorld = radiusMeters * scale;

        seat.Center = center;
        seat.BoardHalfMeters = new Vector2(board.HalfWorld.x / scale, board.HalfWorld.y / scale);
        seat.EdgeMeters = edgeMeters;
        seat.RadiusMeters = radiusMeters;
        seat.RadiusWorld = radiusWorld;
        seat.ClearanceMeters = clearance;
        seat.FramingSteps = framingSteps;
        seat.RadiusRaisedToFloor = raisedToFloor;
        seat.Framing = framing;
        seat.HeadWorld = headWorld;
        seat.TileCount = board.TileCount;
        seat.AngleDegrees = angleDegrees;
        seat.HeadFlat = center + dir * radiusWorld;
        // FACE THE BOARD, AND THEREFORE THE PEER BEYOND IT: forward = seat → centre, world up ⇒
        // yaw only. With one peer this is literally "look at their mask across the table".
        seat.Yaw = Quaternion.LookRotation(-dir, Vector3.up);
    }

    /// <summary>
    /// THE RECENTER SEAT — the ring's geometry, at an azimuth NOBODY RE-SOLVES.
    ///
    /// <para>THE REPORT (user, hardware 2026-09-04, verbatim): "Wenn man Y und B gedrückt hält
    /// re-spawnt man an eine Stelle. Soweit so gut - allerdings ist der Spawnpunkt IN dem Spielfeld
    /// wenn man sich so recentered. Ich will das der Spawnpunkt derselbe ist an dem man am Anfang
    /// auch reingespawnt ist, der bereits die Regeln enthält." The old recenter solved its seat from
    /// <c>CameraController.FocusPoint</c> — a point that MOVES with the player's view over a
    /// scenario — with a fixed 0.70 m set-back that knows nothing about how big this board is. After
    /// any amount of play that lands in the middle of the diorama, which is the whole complaint.</para>
    ///
    /// <para>WHAT IS AND IS NOT TAKEN FROM THE RING. Taken: the RADIUS (the measured board edge
    /// along the seat direction plus the standing clearance, backed off until the play field fits in
    /// a glance), the HEIGHT (<see cref="RingHeadAboveFocusMeters"/> above the board plane) and the
    /// FACING (the board centre) — the three things the user calls "die Regeln". NOT taken: the
    /// AZIMUTH. Re-solving that against the peers here would teleport a player around the table
    /// because somebody ELSE moved, on a request that had nothing to do with them; see the
    /// <c>VRRigDriver.RequestRecenter</c> doc, which is unchanged and still governs.</para>
    ///
    /// <para>PURE AND READ-ONLY, and guarded like <see cref="Solve"/>: a throw in a comfort
    /// geometry helper must degrade to "no ring geometry", never to "no recenter" — the recenter is
    /// the player's way out of a bad pose and is the one thing that may not fail.</para>
    /// </summary>
    /// <param name="focusPoint">Orbit focus — used ONLY for the board plane's height, the same
    /// vertical convention the ordinary seat has always used.</param>
    /// <param name="rigScale">Rig root scale NOW (world units per real metre), so a player who has
    /// zoomed the table still ends up standing at its edge rather than somewhere in the room.</param>
    /// <param name="headWorld">The player's current head, world units — only consulted when there
    /// is no remembered arrival azimuth, to keep them on the side of the table they are already on.</param>
    /// <param name="baseYaw">The scenario rig's frozen flat board yaw — the last-resort direction,
    /// identical to the one the solo ring uses, for a head sitting exactly on the board centre.</param>
    /// <param name="hasRememberedAzimuth">True when the spawn ring actually seated this player in
    /// this scenario and <paramref name="rememberedAzimuthDegrees"/> is that seat's azimuth.</param>
    /// <param name="rememberedAzimuthDegrees">The arrival seat's world azimuth.</param>
    /// <param name="seat">The solved seat (only meaningful when this returns true).</param>
    /// <param name="board">The footprint the seat was solved on — the caller re-runs
    /// <see cref="Frame"/> against it on the pose it ACTUALLY ends up at.</param>
    /// <param name="whyNot">Empty on success; otherwise the reason there is no measurable board,
    /// printed verbatim in the caller's fallback log line.</param>
    internal static bool TryRecenterSeat(Vector3 focusPoint, float rigScale, Vector3 headWorld,
                                         Quaternion baseYaw, bool hasRememberedAzimuth,
                                         float rememberedAzimuthDegrees,
                                         out Seat seat, out Footprint board, out string whyNot)
    {
        seat = default;
        board = default;
        whyNot = "";

        try
        {
            if (!TryFootprint(focusPoint.y, out board))
            {
                whyNot = $"the scenario board is not measurable ({TileCount()} hex tile(s) in the " +
                         "object cache), so there is no footprint to sit outside of";
                return false;
            }

            float scale = rigScale > 0.0001f ? rigScale : 1f;

            // ---- WHICH SIDE OF THE TABLE, and why it is never re-solved here -------------------
            // FIRST CHOICE: the azimuth the spawn ring seated this player at when the scenario
            // started. That is what "derselbe [Punkt] an dem man am Anfang reingespawnt ist" asks
            // for literally, and remembering it also survives everything the alternatives do not: a
            // peer joining or leaving, the player flying somewhere odd before pulling the chord, and
            // a board that has stopped being measurable in between.
            //
            // SECOND CHOICE, when no ring seat was ever placed (single-player scenario whose tiles
            // arrived after the window, a rig rebuilt mid-scenario, [Rig] SpawnInCircle off): the
            // side of the table the player is standing on RIGHT NOW. Rejected alternative: the rig's
            // own yaw, which is where they are LOOKING — a player who has turned to look at their
            // hand would be re-seated on a different side of the board by a gesture that means
            // "put me back at the table".
            bool remembered = hasRememberedAzimuth;
            float angle;
            if (remembered)
            {
                angle = Mathf.Repeat(rememberedAzimuthDegrees, 360f);
            }
            else
            {
                Vector3 fromCenter = headWorld - board.Center;
                fromCenter.y = 0f;
                if (fromCenter.sqrMagnitude > 1e-6f)
                {
                    angle = Azimuth(fromCenter);
                }
                else
                {
                    // The head is exactly over the board centre — no side to keep. Fall back to the
                    // very direction the SOLO ring uses, so the two rules agree at the degenerate
                    // point instead of each inventing an answer.
                    Vector3 fwd = baseYaw * Vector3.forward;
                    fwd.y = 0f;
                    angle = Azimuth(fwd.sqrMagnitude > 1e-6f ? fwd : Vector3.forward);
                }
            }

            seat.AzimuthRemembered = remembered;
            seat.Solo = true;          // no peer was consulted, by design
            seat.PeerCount = 0;
            seat.MinGapDegrees = 360f; // "no peer entered into this seat" — never a measured gap
            FillSeatGeometry(board, angle, scale, ref seat);
            return true;
        }
        catch (System.Exception e)
        {
            seat = default;
            board = default;
            whyNot = $"the recenter seat solve threw ({e.Message}), so the board could not be used";
            return false;
        }
    }

    /// <summary>
    /// Seat radius from the board edge and a clearance, real metres. THE FLOOR ALWAYS WINS: the
    /// result is never smaller than the edge plus the (capped) clearance, so no bound in this file
    /// can put a seat inside the diorama. <see cref="MinRadiusMeters"/> can only push it further
    /// out, on a board so small that the ordinary table-edge distance already clears it.
    /// </summary>
    private static float SeatRadius(float edgeMeters, float clearanceMeters, out bool raisedToFloor)
    {
        float wanted = edgeMeters + Mathf.Min(clearanceMeters, MaxEdgeClearanceMeters);
        raisedToFloor = wanted < MinRadiusMeters;
        return Mathf.Max(wanted, MinRadiusMeters);
    }

    /// <summary>The world head pose a seat resolves to — the flat seat point plus the ring arrival
    /// height. One definition, used by the solver's own framing test and by
    /// <c>VRRigDriver.ApplyRingSeat</c> alike.</summary>
    private static Vector3 HeadAt(Vector3 center, Vector3 dir, float radiusMeters,
                                  float aboveMeters, float scale) =>
        center + dir * (radiusMeters * scale) + Vector3.up * (aboveMeters * scale);

    /// <summary>
    /// MEASURE THE PICTURE, NOT THE SEAT. Given a footprint and a head position in world space,
    /// answer the user's two requirements as numbers: is the head clear of the play field, and does
    /// the whole play field fit in a glance from there.
    ///
    /// <para>Pure, allocation-free and independent of who produced the pose — which is why the
    /// caller can run it on the PLAYER'S OWN head after they have flown somewhere, and get an
    /// answer that means the same thing as the one the solver got for its seat.</para>
    /// </summary>
    /// <param name="board">The footprint (<see cref="TryFootprint"/>).</param>
    /// <param name="headWorld">The head position to judge, world units.</param>
    /// <param name="rigScale">Rig root scale (world units per real metre) — the report is in real
    /// metres so it can be compared against the clearance constants above.</param>
    internal static Framing Frame(in Footprint board, Vector3 headWorld, float rigScale)
    {
        Framing f = default;
        if (!board.Valid)
            return f;

        float scale = rigScale > 0.0001f ? rigScale : 1f;
        Vector2 outer = board.OuterHalfWorld;

        // Signed distance to an axis-aligned rectangle (the standard box SDF): outside is the
        // euclidean distance to the rectangle, inside is the distance to the nearest edge.
        float dx = Mathf.Abs(headWorld.x - board.Center.x) - outer.x;
        float dz = Mathf.Abs(headWorld.z - board.Center.z) - outer.y;
        float marginWorld = dx > 0f || dz > 0f
            ? Mathf.Sqrt(Mathf.Max(dx, 0f) * Mathf.Max(dx, 0f) + Mathf.Max(dz, 0f) * Mathf.Max(dz, 0f))
            : Mathf.Max(dx, dz);

        f.EdgeMarginMeters = marginWorld / scale;
        f.AboveBoardMeters = (headWorld.y - board.Center.y) / scale;
        f.OverBoard = marginWorld < 0f;

        // The four footprint corners, on the board plane.
        CornerScratch[0] = new Vector3(board.Center.x - outer.x, board.Center.y, board.Center.z - outer.y);
        CornerScratch[1] = new Vector3(board.Center.x + outer.x, board.Center.y, board.Center.z - outer.y);
        CornerScratch[2] = new Vector3(board.Center.x + outer.x, board.Center.y, board.Center.z + outer.y);
        CornerScratch[3] = new Vector3(board.Center.x - outer.x, board.Center.y, board.Center.z + outer.y);

        float span = 0f;
        float nearDepression = 0f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 di = CornerScratch[i] - headWorld;
            float li = di.magnitude;
            if (li < 1e-5f)
            {
                // The head is ON a corner: the board fills the view by definition. Report it as the
                // worst possible span rather than dividing by zero.
                f.SpanDegrees = 180f;
                f.NearDepressionDegrees = 90f;
                return f;
            }
            nearDepression = Mathf.Max(nearDepression, Mathf.Asin(Mathf.Clamp(-di.y / li, -1f, 1f)) * Mathf.Rad2Deg);
            for (int j = i + 1; j < 4; j++)
            {
                Vector3 dj = CornerScratch[j] - headWorld;
                float lj = dj.magnitude;
                if (lj < 1e-5f)
                    continue;
                float cos = Mathf.Clamp(Vector3.Dot(di, dj) / (li * lj), -1f, 1f);
                span = Mathf.Max(span, Mathf.Acos(cos) * Mathf.Rad2Deg);
            }
        }

        f.SpanDegrees = span;
        f.NearDepressionDegrees = nearDepression;
        return f;
    }

    /// <summary>
    /// World azimuth of a horizontal direction, degrees in [0,360). <c>Atan2(x, z)</c> (not the
    /// maths convention <c>Atan2(y, x)</c>) so 0° is +Z / <see cref="Vector3.forward"/> and the
    /// angle composes directly with <c>Quaternion.AngleAxis(a, Vector3.up)</c>.
    /// </summary>
    private static float Azimuth(Vector3 horizontal) =>
        Mathf.Repeat(Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg, 360f);

    /// <summary>
    /// The classic largest-gap placement: sort the occupied azimuths, find the widest empty wedge
    /// between two neighbours (wrapping), and sit on its BISECTOR. That bisector is, by
    /// construction, the azimuth whose distance to the NEAREST occupant is the largest achievable
    /// — i.e. it maximises the minimum angular distance to everyone present. For ONE peer it
    /// degenerates to exactly the user's wording: 180° away, straight across the board.
    ///
    /// <para>DETERMINISM: the scan takes a gap only when it is STRICTLY wider than the best so
    /// far, so an exact tie always resolves to the wedge with the lowest starting azimuth. Same
    /// peer set in ⇒ same seat out, on every client and on every retry.</para>
    /// </summary>
    /// <param name="angles">Occupied azimuths, degrees; sorted in place.</param>
    /// <param name="minGap">Angular distance from the returned azimuth to its nearest neighbour.</param>
    private static float LargestGapBisector(List<float> angles, out float minGap)
    {
        angles.Sort();

        if (angles.Count == 1)
        {
            // One peer: the whole circle is one wedge — sit opposite them.
            minGap = 180f;
            return Mathf.Repeat(angles[0] + 180f, 360f);
        }

        int bestStart = 0;
        float bestGap = -1f;
        for (int i = 0; i < angles.Count; i++)
        {
            float next = i + 1 < angles.Count ? angles[i + 1] : angles[0] + 360f;
            float gap = next - angles[i];
            if (gap > bestGap)
            {
                bestGap = gap;
                bestStart = i;
            }
        }

        minGap = bestGap * 0.5f;
        return Mathf.Repeat(angles[bestStart] + bestGap * 0.5f, 360f);
    }

    /// <summary>
    /// Zero-knowledge seat: the flat game's own vantage rotated by the local player's slice of the
    /// circle. <paramref name="baseYaw"/><c> · back</c> is the direction the ordinary solo seat
    /// sits in, so slice 0 lands exactly where a single player has always spawned — and a second
    /// participant lands opposite it, which is the right guess when the host has not moved.
    /// </summary>
    private static float IndexFallbackAngle(Quaternion baseYaw, int idx, int total)
    {
        Vector3 seatDir = baseYaw * Vector3.back;
        seatDir.y = 0f;
        float baseAngle = seatDir.sqrMagnitude < 1e-6f ? 0f : Azimuth(seatDir);
        return Mathf.Repeat(baseAngle + 360f * idx / Mathf.Max(total, 1), 360f);
    }

    /// <summary>
    /// Distance from the footprint centre to its EDGE along <paramref name="dir"/>, world units —
    /// the ray/axis-aligned-rectangle intersection against the OUTER rectangle
    /// (<see cref="Footprint.OuterHalfWorld"/>: tile origins plus half a hex).
    ///
    /// <para>This is what makes the seat read as "at the table" on a board that is not square: on
    /// the long side it is further out, on the short side closer in, exactly like a real player
    /// walking around a rectangular table. The circumscribed radius the round-1 solver used is the
    /// corner distance and is wrong everywhere except the corners.</para>
    ///
    /// <para>IT TAKES THE OUTER RECTANGLE, not the origin rectangle plus a scalar half-hex. Those
    /// two differ on a diagonal direction, and <see cref="Frame"/> judges "inside the board" against
    /// the outer rectangle — so measuring the edge against anything else would let the solver and
    /// its own acceptance test disagree about where the board ends.</para>
    /// </summary>
    private static float EdgeDistanceWorld(Vector2 outerHalfWorld, Vector3 dir)
    {
        float ax = Mathf.Abs(dir.x);
        float az = Mathf.Abs(dir.z);
        float t = float.MaxValue;
        if (ax > 1e-4f)
            t = Mathf.Min(t, outerHalfWorld.x / ax);
        if (az > 1e-4f)
            t = Mathf.Min(t, outerHalfWorld.y / az);
        if (t >= float.MaxValue)
            t = 0f; // degenerate direction (purely vertical) — the clearance carries it
        return t;
    }

    /// <summary>
    /// The board's footprint, measured from the scenario's own hex tiles — the authoritative
    /// source, and the same set the game's camera derives <c>CameraController.m_FocalBounds</c>
    /// from (decompiled CameraController.InitCamera). Read-only: we look at transforms only.
    ///
    /// <para>WHAT THIS MEASURES — CORRECTED 2026-09-02, the previous note here was wrong.
    /// It said "the set is the CURRENTLY REVEALED board, not the whole map". It is the WHOLE MAP.
    /// <c>TileBehaviour</c> does register on <c>OnEnable</c> (decompiled TileBehaviour:33-47), but
    /// <c>Choreographer.ScenarioCreateClientBoardAndCharacters</c> force-activates every descendant
    /// of <c>RoomVisibilityManager.Maps</c> with <c>includeInactive: true</c> — revealed rooms and
    /// unrevealed rooms alike (decompiled Choreographer.cs:1730-1734) — and nothing ever disables
    /// them again. Opening a door in play only flips <c>ProceduralMapTile.visibility</c> and
    /// renderers (decompiled RoomVisibilityTracker.cs:24-60, ProceduralMapTile.cs:127-176); no
    /// TileBehaviour is created or enabled by a reveal. The seat is therefore solved for the whole
    /// dungeon from the first frame it exists, which is the RIGHT footprint for "Sicht auf das
    /// ganze Spielfeld" and is also why the seat radius may not be clamped by an absolute
    /// ceiling (<see cref="MaxEdgeClearanceMeters"/>).</para>
    ///
    /// <para>AND IT ARRIVES ALL AT ONCE. <c>GenerateProcgenLevel</c> → <c>InitialiseScenario</c> →
    /// <c>ClientScenarioManager.Create</c> → <c>PostProcessScenario</c> →
    /// <c>ScenarioCreateClientBoardAndCharacters</c> is one synchronous call stack, so the cache
    /// goes from empty to complete inside a single frame. <see cref="Footprint.TileCount"/> is kept
    /// in the result anyway, and the caller still requires two agreeing polls before it seats: a
    /// map-alignment retry <c>DestroyImmediate</c>s and rebuilds every map (decompiled
    /// Choreographer.cs:14844-14850), which churns the set, and a footprint measured mid-churn is a
    /// flawless measurement of the wrong board.</para>
    ///
    /// <para>Returns false while no tile is in the cache (board not built yet / not in a
    /// scenario), which is the caller's "defer" signal.</para>
    /// </summary>
    /// <param name="planeY">The board plane's world height — the orbit focus plane, so the
    /// vertical convention is byte-for-byte the one the ordinary seat has always used.</param>
    /// <param name="board">The measured footprint; <c>Valid</c> is false when this returns false.</param>
    internal static bool TryFootprint(float planeY, out Footprint board)
    {
        board = default;

        if (!Singleton<ObjectCacheService>.IsInitialized)
            return false;
        ObjectCacheService cache = Singleton<ObjectCacheService>.Instance;
        if (cache == null)
            return false;

        HashSet<TileBehaviour> tiles = cache.GetTileBehaviors();
        if (tiles == null || tiles.Count == 0)
            return false;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        int counted = 0;
        foreach (TileBehaviour tile in tiles)
        {
            if (tile == null)
                continue;
            Vector3 p = tile.transform.position;
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.z < minZ) minZ = p.z;
            if (p.z > maxZ) maxZ = p.z;
            counted++;
        }
        if (counted == 0)
            return false;

        board.Valid = true;
        board.Center = new Vector3((minX + maxX) * 0.5f, planeY, (minZ + maxZ) * 0.5f);
        board.HalfWorld = new Vector2((maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);
        // s_TileSize.x is the runtime hex WIDTH in world units (BOARD-INPUT §2); a single-tile
        // board (half = 0) therefore still yields a sane non-zero edge distance.
        board.TileHalfWorld = Mathf.Max(UnityGameEditorRuntime.s_TileSize.x, 0f) * 0.5f;
        board.TileCount = counted;
        return true;
    }

    /// <summary>Live hex-tile count (0 when no board is built). Cheap: a HashSet count.</summary>
    private static int TileCount()
    {
        if (!Singleton<ObjectCacheService>.IsInitialized)
            return 0;
        ObjectCacheService cache = Singleton<ObjectCacheService>.Instance;
        HashSet<TileBehaviour>? tiles = cache == null ? null : cache.GetTileBehaviors();
        return tiles == null ? 0 : tiles.Count;
    }
}
