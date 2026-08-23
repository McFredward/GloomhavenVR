using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP-ROOM SEAT, AS PURE ARITHMETIC — the whole "where does the player stand and at what
/// scale" decision, with no Unity scene access, so it can be checked by a wire test instead of
/// only by a headset (<c>tests/GloomhavenVR.WireTests/MapRoomSeatVectors.cs</c>).
///
/// <para>WHAT THIS IS ANCHORED TO, AND WHY THAT IS THE WHOLE POINT. The 3D map does not move the
/// map; it scales the PLAYER to it, exactly as the scenario diorama already does
/// (<c>VRRigDriver</c> sets <c>_rigRoot.transform.localScale</c>, and <c>WorldScale</c> is
/// documented there as "game units per real meter … so the board reads as a table"). The one
/// input that decides everything here is the PARCHMENT RENDERER'S WORLD BOUNDS.</para>
///
/// <para>IT IS DELIBERATELY NOT ANCHORED TO <c>CameraController.s_CameraController</c>. That is
/// the mistake hardware test #8 already made and it is written into the config description of
/// <c>[Rig] Vanilla2DMap</c> in those words (it was <c>[Rig] Experimental3DMap</c> until
/// ModBuild 230 renamed and inverted it): anchoring the rig to the orbit camera on the map
/// scene produced "giant map below the player, black flat window", and
/// <c>VRRigDriver.UpdateBody</c> carries the standing warning that the switch "must never silently
/// re-enable the broken orbit-camera anchoring". The orbit camera is read here for exactly ONE
/// scalar — the horizontal DIRECTION the flat game views the map from, so the player is seated on
/// the side the map was authored to be read from — and even that has a pure fallback
/// (<see cref="FallbackViewSide"/>). No position, no height, no field of view, no parenting.</para>
///
/// <para>UNITS. Everything named <c>*Meters</c> is REAL metres (the player's body); everything
/// else is game world units (the map's own coordinates). <see cref="Seat.Scale"/> is the exchange
/// rate: game units per real metre, i.e. the value written to the rig root's localScale.</para>
/// </summary>
internal static class MapRoomSeat
{
    /// <summary>
    /// How wide the map's LARGER horizontal extent should read, in real metres. 1.2 m is a
    /// tabletop you can lean over and still reach the far edge — the same reasoning as the
    /// scenario diorama's 15 cm hex (<c>VRRigDriver.TargetHexSizeMeters</c>), applied to the one
    /// object the map phase actually has.
    /// </summary>
    internal const float TargetMapWidthMeters = 1.20f;

    /// <summary>
    /// Height of the parchment's TOP face above the player's tracking floor, real metres. A real
    /// dining table is 0.72–0.78 m; the map reads as something lying ON such a table, so the
    /// parchment's own top surface — not its underside and not its centre — is what sits here.
    ///
    /// <para>AND THERE REALLY IS A TABLE UNDER IT — a fact, since ModBuild 198, rather than the
    /// plan it used to be. The game's map scene contains <c>GH_Map_TableTop_Lg</c>, a 1.55 x 2.30 m
    /// slab 0.15 m thick whose TOP FACE measures 6 mm under the parchment (found by
    /// <c>MapTableLegs.TryFindTable</c>, which sweeps the whole scene rather than the map roots the
    /// <c>MAP SCENE REPORT</c>'s census is scoped to). So this constant is not the mod standing the
    /// player at an imaginary table: it is the mod agreeing with the game's own furniture, and the
    /// two agree to within the map's thickness. Nothing here changes because of that — the eye line
    /// was already right — but a later round must not "add" a table that is already there.</para>
    /// </summary>
    internal const float TableTopHeightMeters = 0.78f;

    /// <summary>
    /// Standing clearance between the map's own EDGE and the player, real metres — the same
    /// quantity <c>SpawnRing.EdgeClearanceMeters</c> is for the scenario board. Small enough that
    /// the near edge is within arm's reach (Phase 4 wants to poke icons), large enough that the
    /// player's real torso is not inside the tabletop.
    /// </summary>
    internal const float EdgeStandoffMeters = 0.45f;

    /// <summary>
    /// Absolute bounds on the derived rig scale. The lower bound keeps a degenerate (tiny) bounds
    /// read from inverting the world; the upper bound is the diorama-scale sanity ceiling — a map
    /// several thousand units across would otherwise produce a scale at which the near clip plane
    /// (<c>BaseNearMeters × scale</c>) swallows the player's own hands.
    /// </summary>
    internal const float MinScale = 1f;

    /// <inheritdoc cref="MinScale"/>
    internal const float MaxScale = 2000f;

    /// <summary>Below this the parchment bounds are treated as unusable (map not built yet).</summary>
    internal const float MinUsableExtent = 0.01f;

    /// <summary>
    /// The side the player is seated on when the game's orbit camera cannot be read: world −Z,
    /// i.e. the map is looked at from "south". Pure, so the solve never fails for want of a game
    /// camera — a wrong facing is a turn of the head; a failed solve is a black map.
    /// </summary>
    internal static readonly Vector3 FallbackViewSide = new(0f, 0f, -1f);

    /// <summary>The solved seat. All fields are outputs; nothing here reads the head.</summary>
    internal readonly struct Seat
    {
        /// <summary>Game units per real metre — written to the rig root's localScale.</summary>
        internal readonly float Scale;

        /// <summary>
        /// World position of the TRACKING FLOOR under the seat. The rig root goes here (minus the
        /// player's own horizontal head offset — see <c>VRRigDriver.RecenterMap</c>), so the
        /// player's eyes land at their own real standing height above this point.
        /// </summary>
        internal readonly Vector3 FloorPosition;

        /// <summary>
        /// Yaw in DEGREES that faces the map centre from <see cref="FloorPosition"/>.
        ///
        /// <para>DEGREES AND NOT A QUATERNION, ON PURPOSE. <c>Quaternion.LookRotation</c> is an
        /// engine ECall — it cannot run outside a Unity player, and a solve that cannot run
        /// outside a Unity player cannot be checked by the wire test that exists to keep test #8
        /// from happening again. The rotation is composed at the one call site that needs it
        /// (<see cref="Rotation"/>), where a Unity runtime is guaranteed.</para>
        /// </summary>
        internal readonly float YawDegrees;

        /// <summary>World Y of the parchment's top face — the "tabletop" plane.</summary>
        internal readonly float TopY;

        /// <summary>What the map's larger horizontal extent actually reads as, real metres.
        /// Equals <see cref="TargetMapWidthMeters"/> unless <see cref="ScaleClamped"/>.</summary>
        internal readonly float MapWidthMeters;

        /// <summary>True when <see cref="MinScale"/>/<see cref="MaxScale"/> had to bite.</summary>
        internal readonly bool ScaleClamped;

        /// <summary>Unit horizontal direction from the map centre toward the player.</summary>
        internal readonly Vector3 ViewSide;

        /// <summary>The seat yaw as a rotation. Unity-runtime only — see <see cref="YawDegrees"/>.</summary>
        internal Quaternion Rotation => Quaternion.Euler(0f, YawDegrees, 0f);

        internal Seat(float scale, Vector3 floorPosition, float yawDegrees, float topY,
                      float mapWidthMeters, bool scaleClamped, Vector3 viewSide)
        {
            Scale = scale;
            FloorPosition = floorPosition;
            YawDegrees = yawDegrees;
            TopY = topY;
            MapWidthMeters = mapWidthMeters;
            ScaleClamped = scaleClamped;
            ViewSide = viewSide;
        }
    }

    /// <summary>
    /// Normalise a candidate view-side direction to a unit HORIZONTAL vector, falling back to
    /// <see cref="FallbackViewSide"/> when it is vertical or degenerate. Separate from
    /// <see cref="Solve"/> so the fallback is testable on its own.
    /// </summary>
    internal static Vector3 NormalizeViewSide(Vector3 candidate)
    {
        var flat = new Vector3(candidate.x, 0f, candidate.z);
        return flat.sqrMagnitude < 1e-6f ? FallbackViewSide : flat.normalized;
    }

    /// <summary>
    /// Half-extent of an axis-aligned box along a unit horizontal direction — the support function
    /// of the AABB, i.e. how far the map reaches toward the player along the side they stand on.
    /// (Using the circumscribing radius instead is the round-1 spawn-ring mistake:
    /// <c>SpawnRing</c> records that it seats a player far too far out on a long thin footprint.)
    /// </summary>
    internal static float HalfExtentAlong(Vector3 boundsSize, Vector3 unitDir) =>
        0.5f * (Mathf.Abs(unitDir.x) * Mathf.Abs(boundsSize.x)
                + Mathf.Abs(unitDir.z) * Mathf.Abs(boundsSize.z));

    /// <summary>
    /// Solve the seat from the parchment's world bounds. Returns false only when the bounds are
    /// degenerate (the map GameObject exists but its mesh has not been built yet) — the caller
    /// must then keep the menu rig and retry, never place a provisional seat, because a seat that
    /// is later corrected IS a teleport (the ModBuild-131 never-re-seat ruling, stated for rooms
    /// in <c>Core/SkyAlternative.cs</c> and just as true for a player).
    /// </summary>
    internal static bool Solve(Bounds parchmentWorld, Vector3 viewSideCandidate, out Seat seat)
    {
        seat = default;
        Vector3 size = parchmentWorld.size;
        float widest = Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z));
        if (widest < MinUsableExtent)
            return false;

        float rawScale = widest / TargetMapWidthMeters;
        float scale = Mathf.Clamp(rawScale, MinScale, MaxScale);
        bool clamped = !Mathf.Approximately(scale, rawScale);

        Vector3 side = NormalizeViewSide(viewSideCandidate);
        Vector3 center = parchmentWorld.center;
        float topY = parchmentWorld.max.y;

        // Stand OUTSIDE the map's own edge along the viewing side, at a clearance that is a REAL
        // distance (hence × scale) rather than a fraction of the map — a bigger map must not push
        // the player further away from it in metres.
        float reach = HalfExtentAlong(size, side) + EdgeStandoffMeters * scale;
        var floor = new Vector3(center.x + side.x * reach,
                                topY - TableTopHeightMeters * scale,
                                center.z + side.z * reach);

        // Face the map centre — i.e. along -side. Yaw only: the map room is a place, and a place
        // has a level horizon (the same reasoning the billboard convention gives in
        // Net/RemoteNameTag.cs). Unity's yaw convention is forward=(sin y, 0, cos y), so
        // y = Atan2(forward.x, forward.z).
        float yawDegrees = Mathf.Atan2(-side.x, -side.z) * Mathf.Rad2Deg;

        seat = new Seat(scale, floor, yawDegrees, topY, widest / scale, clamped, side);
        return true;
    }
}
