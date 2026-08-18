using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.WireTests;

/// <summary>
/// Where the player stands in the 3D campaign map, and at what scale.
///
/// <para>WHY THIS IS PINNED. The one failure this arithmetic can produce is hardware test #8's:
/// a giant map below the player. That test is the reason <c>[Rig] Experimental3DMap</c> sat bound
/// but unimplemented, and its cause was an ANCHOR — the rig was hung on
/// <c>CameraController.s_CameraController</c>. The replacement anchors on the parchment's world
/// bounds instead, which means the whole question "is the player standing at a table or floating
/// above a continent" is decidable from four numbers with no headset in the room. So it is decided
/// here, once, and the vectors below are the shapes a real campaign map takes: a big flat sheet, a
/// small one, a long thin one, and a degenerate one that has not been built yet.</para>
///
/// <para>The assertions deliberately check REAL-METRE consequences ("the map reads 1.2 m across",
/// "the eyes end up 0.87 m above the parchment") rather than the intermediate scale factor —
/// a scale is not something anyone can sanity-check, and a table height is.</para>
/// </summary>
internal static class MapRoomSeatVectors
{
    /// <summary>Two floats are the same to within a millimetre of real-world consequence.</summary>
    private static bool Near(float a, float b, float tol = 1e-3f) => Mathf.Abs(a - b) <= tol;

    internal static void Run(Harness t)
    {
        // ---- a big flat sheet: the world map ---------------------------------------------------
        // 600 x 0.13 x 400 world units centred at the origin — the shape the campaign parchment
        // actually is (one mesh, four quadrant submeshes, thin in Y).
        t.Case("maproom/world-map");
        var world = new Bounds(new Vector3(0f, 10f, 0f), new Vector3(600f, 0.13f, 400f));
        t.True(MapRoomSeat.Solve(world, MapRoomSeat.FallbackViewSide, out MapRoomSeat.Seat s),
               "a real parchment solves");
        t.True(Near(s.Scale, 600f / MapRoomSeat.TargetMapWidthMeters),
               "scale = widest horizontal extent / target width");
        t.True(Near(s.MapWidthMeters, MapRoomSeat.TargetMapWidthMeters),
               "the map reads exactly the target width in real metres");
        t.True(!s.ScaleClamped, "500 units-per-metre is inside the sane range");

        // The seat's FLOOR is one table height (real) below the parchment's TOP face — not below
        // its centre, and not below its underside. The map lies ON a table; the table top is the
        // parchment top.
        t.True(Near(s.TopY, world.max.y), "the tabletop plane is the parchment's TOP face");
        t.True(Near(s.TopY - s.FloorPosition.y, MapRoomSeat.TableTopHeightMeters * s.Scale),
               "the tracking floor sits one real table height below the parchment top");

        // A 1.70 m player's eyes therefore end up (1.70 - 0.78) m above the map. That is the whole
        // claim of the feature expressed as one number, so it is the one asserted.
        const float EyeMeters = 1.70f;
        t.True(Near((s.FloorPosition.y + EyeMeters * s.Scale - s.TopY) / s.Scale,
                    EyeMeters - MapRoomSeat.TableTopHeightMeters),
               "a 1.70 m player's eyes land 0.92 m (real) above the parchment surface");

        // Standing on the -Z side: outside the map's own edge by the standoff, in REAL metres, so
        // a bigger map does not push the player further away in metres.
        t.True(Near(s.FloorPosition.z,
                    world.center.z - (200f + MapRoomSeat.EdgeStandoffMeters * s.Scale)),
               "the seat is the map's half-extent along the view side plus a real standoff");
        t.True(Near(s.FloorPosition.x, world.center.x), "no lateral drift on an axis-aligned side");
        // Unity yaw convention: forward = (sin y, 0, cos y). Standing on the -Z side means looking
        // along +Z, i.e. yaw 0. (Quaternion.LookRotation is an engine ECall and cannot run here —
        // which is exactly why the seat carries DEGREES and composes the rotation at the rig.)
        t.True(Near(s.YawDegrees, 0f), "from the -Z side the seat faces +Z, i.e. the map centre");

        // ---- the same map viewed from the +X side ----------------------------------------------
        // The support function must use the extent ALONG THE VIEW DIRECTION, not the circumscribing
        // radius. On a 600x400 sheet those differ by 160 units — a whole map's worth of error, and
        // exactly the round-1 spawn-ring mistake.
        t.Case("maproom/view-side-uses-the-support-function");
        t.True(MapRoomSeat.Solve(world, Vector3.right, out MapRoomSeat.Seat sx), "solves from +X");
        t.True(Near(sx.FloorPosition.x, world.center.x + (300f + MapRoomSeat.EdgeStandoffMeters * sx.Scale)),
               "from +X the reach is the X half-extent (300), not the Z one and not the diagonal");
        t.True(Near(sx.FloorPosition.z, world.center.z), "no drift along the unviewed axis");
        t.True(Near(sx.YawDegrees, -90f), "from the +X side the seat faces -X, i.e. yaw -90 degrees");
        t.True(Near(MapRoomSeat.HalfExtentAlong(world.size, Vector3.right), 300f), "support along +X");
        t.True(Near(MapRoomSeat.HalfExtentAlong(world.size, Vector3.forward), 200f), "support along +Z");
        t.True(Near(MapRoomSeat.HalfExtentAlong(world.size, new Vector3(1f, 0f, 1f).normalized),
                    0.5f * (600f + 400f) * 0.70710678f),
               "support along a diagonal is the AABB support, not the circumscribing radius");

        // ---- a long thin map: the widest extent decides the scale ------------------------------
        t.Case("maproom/long-thin-map");
        var thin = new Bounds(new Vector3(5f, 0f, -5f), new Vector3(120f, 0.1f, 20f));
        t.True(MapRoomSeat.Solve(thin, MapRoomSeat.FallbackViewSide, out MapRoomSeat.Seat st), "solves");
        t.True(Near(st.Scale, 120f / MapRoomSeat.TargetMapWidthMeters),
               "the LONGER side is what reads as the target width — the map must fit on the table");
        t.True(Near(st.MapWidthMeters, MapRoomSeat.TargetMapWidthMeters), "reads at the target width");

        // ---- the city map: small enough that the scale clamp bites -----------------------------
        // A 0.5-unit map would want a scale of 0.42, i.e. the player would be scaled UP relative to
        // the world. The clamp holds at 1 and says so, rather than silently inverting the world.
        t.Case("maproom/scale-clamp");
        var tiny = new Bounds(Vector3.zero, new Vector3(0.5f, 0.01f, 0.5f));
        t.True(MapRoomSeat.Solve(tiny, MapRoomSeat.FallbackViewSide, out MapRoomSeat.Seat sc), "solves");
        t.True(sc.ScaleClamped, "a sub-metre map reports its clamp instead of hiding it");
        t.True(Near(sc.Scale, MapRoomSeat.MinScale), "clamped to the floor, not to zero");

        // ---- degenerate bounds: refuse, do not guess -------------------------------------------
        // The map GameObject can be active before its mesh exists. A provisional seat that is
        // corrected a second later IS a teleport, so the only correct answer is "not yet".
        t.Case("maproom/degenerate-bounds-refuse");
        t.True(!MapRoomSeat.Solve(new Bounds(Vector3.zero, Vector3.zero),
                                  MapRoomSeat.FallbackViewSide, out MapRoomSeat.Seat _),
               "a zero-size parchment does NOT produce a seat");
        t.True(!MapRoomSeat.Solve(new Bounds(Vector3.zero, new Vector3(0.001f, 100f, 0.001f)),
                                  MapRoomSeat.FallbackViewSide, out MapRoomSeat.Seat _),
               "a tall thin sliver is not a map either — the HORIZONTAL extent is what counts");

        // ---- the view-side fallback is pure -----------------------------------------------------
        // The orbit camera supplies only a direction, and it must never be able to fail the solve:
        // a wrong facing is a turn of the head, a failed solve is a black map.
        t.Case("maproom/view-side-normalisation");
        t.True(MapRoomSeat.NormalizeViewSide(Vector3.zero) == MapRoomSeat.FallbackViewSide,
               "a zero direction falls back to world -Z");
        t.True(MapRoomSeat.NormalizeViewSide(Vector3.up) == MapRoomSeat.FallbackViewSide,
               "a purely VERTICAL direction falls back too (the map camera looks down)");
        Vector3 flattened = MapRoomSeat.NormalizeViewSide(new Vector3(0f, 99f, 3f));
        t.True(Near(flattened.y, 0f) && Near(flattened.z, 1f),
               "the vertical component is dropped and the rest normalised");
        t.True(Near(MapRoomSeat.NormalizeViewSide(new Vector3(7f, 0f, 0f)).x, 1f),
               "an already-horizontal direction is just normalised");

        // ---- the constants are the feature, so pin them ------------------------------------------
        // These four numbers ARE the picture the user gets. Changing one is a design decision, and
        // it should have to walk past this line to happen.
        t.Case("maproom/constants");
        t.True(Near(MapRoomSeat.TargetMapWidthMeters, 1.20f), "the map reads as a 1.2 m tabletop");
        t.True(Near(MapRoomSeat.TableTopHeightMeters, 0.78f), "the parchment sits at table height");
        t.True(Near(MapRoomSeat.EdgeStandoffMeters, 0.45f), "the player stands 45 cm off the edge");
        t.True(MapRoomSeat.TableTopHeightMeters < 1.4f,
               "the tabletop must stay below eye height or the player looks at the map edge-on");
    }
}
