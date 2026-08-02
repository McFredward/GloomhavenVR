using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using Script.Controller;
using UnityEngine;

namespace GloomhavenVR.Rig;

/// <summary>
/// MULTIPLAYER JOIN COMFORT — where a VR player is put down when they arrive at the table.
///
/// <para>THE DEFECT THIS REPLACES (user, MP session 2026-08: "Wenn man zu einem Spieler in den MP
/// joint, spawnt man direkt hinter oder IN der anderen Maske und muss sich erst ausrichten").
/// The previous spawn circle handed every participant a slice index and rotated the rig by
/// <c>360·idx/total</c> around <b>its own</b> <c>_scenarioBaseYaw</c> — a yaw read from the LOCAL
/// scenario camera. Two things broke:</para>
/// <list type="number">
///   <item>the azimuth was measured in a frame that is not guaranteed identical across clients,
///         so "slice 1" on one machine and "slice 1" on another are not the same place — the
///         circle was never actually shared;</item>
///   <item>the index says nothing about where the other players ACTUALLY are. A peer who walked
///         (world grab / snap turn / physical steps) has left their slice, and the joiner is
///         dropped straight into them anyway.</item>
/// </list>
///
/// <para>THIS SOLVER instead works entirely in the BOARD's own world frame — the frame every
/// client already shares (Gloomhaven is server-authoritative; see <see cref="IBoardAnchor"/>) and
/// the frame peer head poses are already replicated in. It answers three questions:</para>
/// <list type="bullet">
///   <item><b>Where is the table?</b> Centre + footprint radius are measured from the scenario's
///         own hex tiles (<c>ObjectCacheService.GetTileBehaviors()</c> — the same set the game's
///         camera uses for <c>m_FocalBounds</c>), never from a hardcoded metre value, so the seat
///         survives any board size, any world scale and any pinch zoom.</item>
///   <item><b>Which way is free?</b> The azimuths of every peer head we have a replicated pose for
///         are sorted, the LARGEST ANGULAR GAP is found, and we are seated on its BISECTOR. That
///         is exactly "maximise the minimum angular distance to everyone already here", and it is
///         deterministic for a given peer set (ties resolve to the lowest starting azimuth).</item>
///   <item><b>How far out?</b> Just outside the footprint at a comfortable reach — see
///         <see cref="ReachMarginMeters"/>.</item>
/// </list>
///
/// <para>MULTIPLAYER SAFETY: read-only and purely LOCAL. Nothing here mutates game state, nothing
/// is sent, and no wire field was added — peer positions come from the rig packets the embodiment
/// sync already receives (<see cref="NetAvatarDriver.CollectPeerHeads"/>), and the participant
/// registry is read through the existing reflection-only bridge
/// (<see cref="NetPlayerActors.LocalStableIndex"/>). The resulting head pose is broadcast like any
/// other head pose, so peers see us arrive at the free seat for free.</para>
/// </summary>
internal static class SpawnRing
{
    /// <summary>
    /// How far OUTSIDE the board footprint the ring sits, in real metres — a table player's
    /// standing clearance, so the diorama's outer hexes stay inside comfortable arm's reach
    /// instead of under the player's chin. Real metres (not world units) on purpose: the rig root
    /// is scaled, so this reads the same at every zoom level.
    /// </summary>
    internal const float ReachMarginMeters = 0.25f;

    /// <summary>
    /// Hard ceiling on the ring radius, real metres. A very large scenario board would otherwise
    /// push the seat metres away from anything the player can touch — the user's explicit
    /// requirement was "trotzdem nah am Spielbrett dran". Past this the seat sits just inside the
    /// footprint edge, which is still the near-the-table read; the player's own zoom/grab is the
    /// tool for a huge map, not the spawn seat.
    /// </summary>
    internal const float MaxRadiusMeters = 2.2f;

    /// <summary>What <see cref="Solve"/> could do with the world it found.</summary>
    internal enum Outcome
    {
        /// <summary>No multiplayer session (or the FFSNet registry is not readable): strict no-op,
        /// the caller keeps the ordinary solo seat. This is the single-player contract.</summary>
        SinglePlayer,

        /// <summary>Multiplayer, but the board's tiles are not in the scene yet — the caller must
        /// keep the ordinary seat and retry while its settle window is open.</summary>
        BoardPending,

        /// <summary>A seat was computed; <c>seat</c> is filled in.</summary>
        Placed,
    }

    /// <summary>The computed seat, in world space. All angles are world azimuths in degrees
    /// (<c>Atan2(x, z)</c>, normalised to [0,360)), i.e. the same convention on every client.</summary>
    internal struct Seat
    {
        /// <summary>Board centre, xz from the tile footprint, y on the orbit focus plane (the
        /// plane the eye height is measured from — unchanged from the ordinary seat).</summary>
        public Vector3 Center;

        /// <summary>Board footprint radius, real metres (tile spread + half a hex).</summary>
        public float BoardRadiusMeters;

        /// <summary>Ring radius, real metres — what the config/log talks about.</summary>
        public float RadiusMeters;

        /// <summary>Ring radius in world units (<see cref="RadiusMeters"/> × rig scale).</summary>
        public float RadiusWorld;

        /// <summary>Chosen world azimuth, degrees.</summary>
        public float AngleDegrees;

        /// <summary>Angular distance to the NEAREST peer at <see cref="AngleDegrees"/> (degrees).
        /// This is the quantity the solver maximises. 360 when no peer pose is known.</summary>
        public float MinGapDegrees;

        /// <summary>How many peers had a usable replicated head pose when this was solved.</summary>
        public int PeerCount;

        /// <summary>Total FFSNet participants (VR and flat) at solve time.</summary>
        public int ParticipantCount;

        /// <summary>True when no peer pose was available and the seat is the deterministic
        /// index fallback rather than a measured largest-gap bisector.</summary>
        public bool FromIndexFallback;

        /// <summary>Where the head should end up, on the focus plane (the caller adds eye height).</summary>
        public Vector3 HeadFlat;

        /// <summary>Yaw-only rotation that looks from <see cref="HeadFlat"/> at
        /// <see cref="Center"/> — "face the board".</summary>
        public Quaternion Yaw;
    }

    /// <summary>Scratch list for peer head positions. Reused: the solve runs at most a handful of
    /// times per scenario, but allocating in a rig path is a habit worth not having.</summary>
    private static readonly List<Vector3> HeadScratch = new(8);

    /// <summary>Scratch list for peer azimuths (parallel to <see cref="HeadScratch"/>).</summary>
    private static readonly List<float> AngleScratch = new(8);

    /// <summary>
    /// Cheap pre-check for the driver's settle poll: true when a <see cref="Solve"/> would get
    /// past <see cref="Outcome.SinglePlayer"/> / <see cref="Outcome.BoardPending"/>. Exists so the
    /// poll never calls Recenter speculatively — a Recenter that cannot place the ring would
    /// re-seat (and therefore fight) a player who is already moving.
    /// </summary>
    internal static bool Ready()
    {
        try
        {
            NetPlayerActors.LocalStableIndex(out int total);
            return total > 1 && TileCount() > 0;
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Rig", $"Spawn ring readiness probe failed: {e.Message}");
            return false;
        }
    }

    /// <summary>How many peers we currently hold a replicated head pose for (0 offline).</summary>
    internal static int KnownPeerCount()
    {
        try
        {
            HeadScratch.Clear();
            int n = NetAvatarDriver.CollectPeerHeads(HeadScratch);
            HeadScratch.Clear();
            return n;
        }
        catch (System.Exception e)
        {
            HeadScratch.Clear();
            VRLog.Warn("Rig", $"Spawn ring peer count failed: {e.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Solve the join seat.
    /// </summary>
    /// <param name="focusPoint">The orbit focus (<c>CameraController.FocusPoint</c>) — used ONLY
    /// for the seat's ground-plane height, so the vertical convention is byte-for-byte the one the
    /// ordinary seat has always used. The horizontal centre comes from the board itself.</param>
    /// <param name="baseYaw">The scenario rig's frozen flat board yaw. Only feeds the
    /// zero-peer index fallback, so that "seat 0" is the vantage the flat game would have given
    /// the player.</param>
    /// <param name="rigScale">Rig root scale (world units per real metre).</param>
    /// <param name="seat">The computed seat (only meaningful on <see cref="Outcome.Placed"/>).</param>
    internal static Outcome Solve(Vector3 focusPoint, Quaternion baseYaw, float rigScale, out Seat seat)
    {
        // NEVER LET A COMFORT FEATURE COST THE RECENTER. The caller's fall-through on anything but
        // Placed is the ordinary table-edge seat, so a throw in here degrades to "no ring", never
        // to "no seat" — this is the whole reason the try/catch sits at the entry point.
        try
        {
            return SolveCore(focusPoint, baseYaw, rigScale, out seat);
        }
        catch (System.Exception e)
        {
            HeadScratch.Clear();
            AngleScratch.Clear();
            seat = default;
            VRLog.Warn("Rig", $"Spawn ring solve failed ({e.Message}) — keeping the ordinary table-edge seat.");
            return Outcome.SinglePlayer;
        }
    }

    private static Outcome SolveCore(Vector3 focusPoint, Quaternion baseYaw, float rigScale, out Seat seat)
    {
        seat = default;

        // SINGLE-PLAYER IS A STRICT NO-OP (requirement, and the reason nothing about the solo
        // seat changes): one participant — or an unreadable registry — means there is nobody to
        // avoid, so the caller keeps the seat it has always used.
        int idx = NetPlayerActors.LocalStableIndex(out int total);
        if (total <= 1)
            return Outcome.SinglePlayer;

        if (!TryBoardFootprint(focusPoint.y, out Vector3 center, out float boardRadiusWorld))
            return Outcome.BoardPending; // scenario tiles not spawned yet — the caller retries

        float scale = rigScale > 0.0001f ? rigScale : 1f;
        float boardRadiusMeters = boardRadiusWorld / scale;

        // RADIUS FROM THE BOARD'S OWN SIZE: stand just outside the footprint at a comfortable
        // reach, never closer than the authored table-edge distance and never past the "still near
        // the table" ceiling. Everything is in REAL metres and converted back once, so the seat is
        // identical at every world scale and every pinch zoom.
        float radiusMeters = Mathf.Clamp(boardRadiusMeters + ReachMarginMeters,
                                         ComfortSettings.EffectiveEyeBackMeters,
                                         MaxRadiusMeters);

        // ANGLE. Peers first: their replicated head poses are the only truth about where anyone
        // actually stands.
        HeadScratch.Clear();
        int peerCount = NetAvatarDriver.CollectPeerHeads(HeadScratch);

        float angle;
        float minGap;
        bool fallback;
        if (peerCount > 0)
        {
            AngleScratch.Clear();
            for (int i = 0; i < HeadScratch.Count; i++)
            {
                Vector3 d = HeadScratch[i] - center;
                d.y = 0f;
                if (d.sqrMagnitude < 1e-6f)
                    continue; // a peer standing exactly on the centre has no azimuth to avoid
                AngleScratch.Add(Azimuth(d));
            }
            HeadScratch.Clear();

            if (AngleScratch.Count > 0)
            {
                angle = LargestGapBisector(AngleScratch, out minGap);
                fallback = false;
                peerCount = AngleScratch.Count;
            }
            else
            {
                angle = IndexFallbackAngle(baseYaw, idx, total);
                minGap = 360f / total;
                fallback = true;
                peerCount = 0;
            }
            AngleScratch.Clear();
        }
        else
        {
            HeadScratch.Clear();
            // BEST GUESS WHEN NOBODY HAS ARRIVED ON THE WIRE YET (we joined faster than the peers'
            // 15 Hz rig packets, or every other participant is a flat player). The deterministic
            // participant index gives distinct azimuths WITHOUT any knowledge — but unlike the old
            // circle it is laid out in the shared WORLD frame, so two clients guessing at the same
            // moment still guess differently. The driver's one correction refines this the instant
            // a real peer pose lands.
            angle = IndexFallbackAngle(baseYaw, idx, total);
            minGap = 360f / total;
            fallback = true;
        }

        float radiusWorld = radiusMeters * scale;
        Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

        seat.Center = center;
        seat.BoardRadiusMeters = boardRadiusMeters;
        seat.RadiusMeters = radiusMeters;
        seat.RadiusWorld = radiusWorld;
        seat.AngleDegrees = angle;
        seat.MinGapDegrees = minGap;
        seat.PeerCount = peerCount;
        seat.ParticipantCount = total;
        seat.FromIndexFallback = fallback;
        seat.HeadFlat = center + dir * radiusWorld;
        // FACE THE BOARD: forward = from the seat toward the centre, world up ⇒ yaw only.
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
    /// — i.e. it maximises the minimum angular distance to everyone present.
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
    /// sits in, so slice 0 lands exactly where a single player has always spawned.
    /// </summary>
    private static float IndexFallbackAngle(Quaternion baseYaw, int idx, int total)
    {
        Vector3 seatDir = baseYaw * Vector3.back;
        seatDir.y = 0f;
        float baseAngle = seatDir.sqrMagnitude < 1e-6f ? 0f : Azimuth(seatDir);
        return Mathf.Repeat(baseAngle + 360f * idx / Mathf.Max(total, 1), 360f);
    }

    /// <summary>
    /// The board's footprint, measured from the scenario's own hex tiles — the authoritative
    /// source, and the same set the game's camera derives <c>CameraController.m_FocalBounds</c>
    /// from (decompiled CameraController.InitCamera). Read-only: we look at transforms only.
    ///
    /// <para>Returns false while no tile is in the cache (board not built yet / not in a
    /// scenario), which is the caller's "defer" signal. The radius is the largest horizontal
    /// distance from the centre to a tile ORIGIN plus half a hex, so it reaches the outer tiles'
    /// outer edge rather than their centres.</para>
    /// </summary>
    private static bool TryBoardFootprint(float planeY, out Vector3 center, out float radiusWorld)
    {
        center = Vector3.zero;
        radiusWorld = 0f;

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

        float halfX = (maxX - minX) * 0.5f;
        float halfZ = (maxZ - minZ) * 0.5f;
        // Circumscribing radius of the footprint rectangle + half a hex for the outer tiles' own
        // extent. s_TileSize.x is the runtime hex WIDTH in world units (BOARD-INPUT §2); a
        // single-tile board (halfX = halfZ = 0) therefore still yields a sane non-zero radius.
        float tileHalf = Mathf.Max(UnityGameEditorRuntime.s_TileSize.x, 0f) * 0.5f;
        radiusWorld = Mathf.Sqrt(halfX * halfX + halfZ * halfZ) + tileHalf;
        return radiusWorld > 0.0001f;
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
