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
    /// <c>VRRigDriver.ApplyRingSeat</c> — ring seats only; the deliberate B+Y recenter keeps the
    /// plain table-edge height. 0.35 m lifts the arrival vantage noticeably above the board plane
    /// without reading as flying; from there stick flight puts the player wherever they like.
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
    /// Seat radius ceiling, real metres. Only a sanity bound against an absurd measurement (a
    /// stray tile parked far off the map would otherwise fling the seat across the room); a
    /// normally-zoomed board never reaches it, because the radius is now edge-relative rather
    /// than circumscribed. When it DOES bite, the log line shows both the measured edge distance
    /// and the clamped radius, so the clamp is never a silent surprise.
    /// </summary>
    internal const float MaxRadiusMeters = 3.0f;

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

    /// <summary>The computed seat, in world space. All angles are world azimuths in degrees
    /// (<c>Atan2(x, z)</c>, normalised to [0,360)), i.e. the same convention on every client.</summary>
    internal struct Seat
    {
        /// <summary>True when this seat was solved WITHOUT peers (single player): the azimuth is
        /// the ordinary scenario base yaw and only the radius came from the board footprint.</summary>
        public bool Solo;

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

        /// <summary>True when <see cref="MinRadiusMeters"/> / <see cref="MaxRadiusMeters"/>
        /// actually changed the radius (logged, never silent).</summary>
        public bool RadiusClamped;

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

        if (!TryBoardFootprint(focusPoint.y, out Vector3 center, out Vector2 halfWorld, out float tileHalfWorld))
        {
            HeadScratch.Clear();
            return Outcome.BoardPending; // scenario tiles not spawned yet — the caller retries
        }

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

        Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

        // ---- HOW FAR: at the table edge on THAT side ----------------------------------------
        // Edge distance along the chosen direction (not the circumscribing radius — see
        // EdgeClearanceMeters), plus a standing clearance, then clamped for sanity only.
        float edgeWorld = EdgeDistanceWorld(halfWorld, tileHalfWorld, dir);
        float edgeMeters = edgeWorld / scale;
        float wanted = edgeMeters + EdgeClearanceMeters;
        float radiusMeters = Mathf.Clamp(wanted, MinRadiusMeters, MaxRadiusMeters);
        float radiusWorld = radiusMeters * scale;

        seat.Center = center;
        seat.BoardHalfMeters = new Vector2(halfWorld.x / scale, halfWorld.y / scale);
        seat.EdgeMeters = edgeMeters;
        seat.RadiusMeters = radiusMeters;
        seat.RadiusWorld = radiusWorld;
        seat.RadiusClamped = !Mathf.Approximately(radiusMeters, wanted);
        seat.AngleDegrees = angle;
        seat.MinGapDegrees = minGap;
        seat.FromIndexFallback = fallback;
        seat.HeadFlat = center + dir * radiusWorld;
        // FACE THE BOARD, AND THEREFORE THE PEER BEYOND IT: forward = seat → centre, world up ⇒
        // yaw only. With one peer this is literally "look at their mask across the table".
        seat.Yaw = Quaternion.LookRotation(-dir, Vector3.up);
        return Outcome.Placed;
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
    /// the ray/axis-aligned-rectangle intersection, plus half a hex for the outer tile's own
    /// extent (the footprint is measured from tile ORIGINS).
    ///
    /// <para>This is what makes the seat read as "at the table" on a board that is not square: on
    /// the long side it is further out, on the short side closer in, exactly like a real player
    /// walking around a rectangular table. The circumscribed radius the round-1 solver used is the
    /// corner distance and is wrong everywhere except the corners.</para>
    /// </summary>
    private static float EdgeDistanceWorld(Vector2 halfWorld, float tileHalfWorld, Vector3 dir)
    {
        float ax = Mathf.Abs(dir.x);
        float az = Mathf.Abs(dir.z);
        float t = float.MaxValue;
        if (ax > 1e-4f)
            t = Mathf.Min(t, halfWorld.x / ax);
        if (az > 1e-4f)
            t = Mathf.Min(t, halfWorld.y / az);
        if (t >= float.MaxValue)
            t = 0f; // degenerate direction (purely vertical) — the clearance below carries it
        return t + tileHalfWorld;
    }

    /// <summary>
    /// The board's footprint, measured from the scenario's own hex tiles — the authoritative
    /// source, and the same set the game's camera derives <c>CameraController.m_FocalBounds</c>
    /// from (decompiled CameraController.InitCamera). Read-only: we look at transforms only.
    ///
    /// <para>NOTE ON WHAT THIS MEASURES: <c>TileBehaviour</c> registers in the cache on
    /// <c>OnEnable</c> and de-registers on <c>OnDisable</c> (decompiled TileBehaviour:34-46), so
    /// the set is the CURRENTLY REVEALED board, not the whole map. That is the right footprint for
    /// a seat: players stand around the part of the dungeon that exists.</para>
    ///
    /// <para>Returns false while no tile is in the cache (board not built yet / not in a
    /// scenario), which is the caller's "defer" signal.</para>
    /// </summary>
    private static bool TryBoardFootprint(float planeY, out Vector3 center, out Vector2 halfWorld,
                                          out float tileHalfWorld)
    {
        center = Vector3.zero;
        halfWorld = Vector2.zero;
        tileHalfWorld = 0f;

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

        center = new Vector3((minX + maxX) * 0.5f, planeY, (minZ + maxZ) * 0.5f);
        halfWorld = new Vector2((maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f);
        // s_TileSize.x is the runtime hex WIDTH in world units (BOARD-INPUT §2); a single-tile
        // board (half = 0) therefore still yields a sane non-zero edge distance.
        tileHalfWorld = Mathf.Max(UnityGameEditorRuntime.s_TileSize.x, 0f) * 0.5f;
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
