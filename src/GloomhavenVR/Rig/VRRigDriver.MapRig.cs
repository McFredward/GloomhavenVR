using GloomhavenVR.Core;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

namespace GloomhavenVR.Rig;

internal sealed partial class VRRigDriver
{
    /// <summary>
    /// THE THIRD RIG FLAVOUR (<see cref="RigKind.Map"/>) — the player standing IN the campaign map
    /// instead of looking at a photograph of it. Same mechanism as the scenario diorama and
    /// deliberately so: the rig root is scaled (<c>WorldScale</c> = game units per real metre) and
    /// seated, and no game object is moved, re-parented or re-scaled.
    ///
    /// <para>WHAT MAKES THIS SAFE FOR THE MAIN MENU. The mask policy in
    /// <c>TickHeadCullingMask</c> is re-asserted every frame and "MENU rig = the mod layer ONLY"
    /// is load-bearing (test #10). This flavour is reached ONLY through
    /// <c>MapRoomDriver.Wanted</c>, which is a POSITIVE map-open signal — a live
    /// <c>MapChoreographer</c> with an active <c>worldMap</c>/<c>cityMap</c> — and never through
    /// "not a scenario". No <c>MapChoreographer</c> exists in the main menu, so the menu takes the
    /// <c>Menu</c> branch exactly as before, byte for byte.</para>
    ///
    /// <para>AND IT IS NOT TEST #8. Test #8 anchored the rig to
    /// <c>CameraController.s_CameraController</c> and got a giant map below the player. Here the
    /// seat and the scale come from the PARCHMENT RENDERER'S WORLD BOUNDS
    /// (<see cref="MapRoomSeat"/>); the orbit camera contributes one horizontal DIRECTION (which
    /// side of the table to stand on) and the culling mask is READ off the map camera rather than
    /// guessed. Neither is an anchor.</para>
    /// </summary>
    private void BuildMapRig()
    {
        if (!MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out string sideSource))
        {
            // NO PROVISIONAL SEAT, EVER. A seat that is corrected a second later IS a teleport
            // (the ModBuild-131 ruling, stated for rooms in Core/SkyAlternative.cs). The map GO is
            // active but its parchment is not measurable yet, so we simply do not build this
            // frame; UpdateBody retries next frame and the menu rig is what the player sees
            // meanwhile — the same picture they would get with the feature off.
            if (!_mapSeatWaitLogged)
            {
                _mapSeatWaitLogged = true;
                VRLog.Info("Rig", "Map rig: the campaign map is open but its parchment renderer has no "
                                  + "usable world bounds yet — NOT building a provisional rig (a seat that "
                                  + "is later corrected is a teleport). Retrying every frame; the menu rig "
                                  + "stands until then.");
            }
            return;
        }
        _mapSeatWaitLogged = false;

        // Anchor: the same camera the menu rig would take. It is a REFERENCE ONLY (far plane,
        // camera depth ordering) — never a pose source. Without one there is nothing to order our
        // camera against, so we wait rather than guess.
        Camera? anchor = ResolveMenuCamera();
        if (anchor == null)
            return;
        _anchor = anchor;

        int maskBefore = anchor.cullingMask;
        int wantedMask = MapRoomDriver.ResolveMapMask(anchor, out int sourceMask);
        string maskSource = MapRoomDriver.DescribeMapMaskSource(anchor);

        _mapSeat = seat;
        _rigRoot = CreateRigRoot();
        _rigRoot.transform.position = seat.FloorPosition;
        _rigRoot.transform.rotation = seat.Rotation;
        _rigRoot.transform.localScale = Vector3.one * seat.Scale;

        // Ring state: the map room seats by construction (the seat is a pure function of the
        // parchment), so the multiplayer join ring — which is board-derived — must stay inert.
        _ringPlaced = false;
        _ringSettled = true;
        _ringPeersAtPlacement = -1;
        _ringAttempts = 0;
        _ringWindowEnd = 0f;

        CreateHeadCamera(anchor, seat.Scale);
        _camera!.cullingMask = wantedMask;

        // FAR PLANE: the head camera is seeded from the ANCHOR's far plane, and the anchor is a
        // menu camera whose far plane was authored for a menu, not for a continent. The player now
        // stands at one edge of a map that can be hundreds of world units across and looks at the
        // far edge, so a menu far plane would slice the map in half — a failure that reads in the
        // headset as "the top of the map is missing", not as a clipping bug. Cover the whole
        // parchment from the seat, with margin; TickClipPlanes keeps this as its floor from here.
        Bounds pb = MapRoomDriver.ParchmentRenderer != null
            ? MapRoomDriver.ParchmentRenderer.bounds
            : new Bounds(seat.FloorPosition, Vector3.zero);
        float needFar = Vector3.Distance(seat.FloorPosition, pb.center) + pb.size.magnitude;
        float mapFar = Mathf.Max(_baseFarClip, needFar * MapFarPlaneMargin);
        _baseFarClip = mapFar;
        _camera.farClipPlane = mapFar;

        RigRoot = _rigRoot.transform;
        HeadCamera = _camera;
        BaseWorldScale = seat.Scale;
        _kind = RigKind.Map;
        RigPoseVersion++;
        _pendingRecenter = true;

        MapRoomDriver.Engage(maskBefore, wantedMask, maskSource, seat.Scale, seat.FloorPosition);

        VRLog.Info("Rig", $"MAP rig built — world scale {seat.Scale:F2} game units per real metre "
                          + $"(the map reads {seat.MapWidthMeters:F2} m across"
                          + $"{(seat.ScaleClamped ? ", SCALE CLAMPED — the parchment bounds are outside the sane range" : "")}), "
                          + $"tracking floor at {seat.FloorPosition}, facing the map centre from "
                          + $"{seat.ViewSide} — side source: {sideSource}. Head camera mask 0x{wantedMask:X8} "
                          + $"(source 0x{sourceMask:X8} from {maskSource}, OR mod layer 0x{VRLayers.ModLayerMask:X8}); "
                          + $"anchor '{anchor.name}' mask 0x{maskBefore:X8} is a REFERENCE ONLY. "
                          + $"Clip planes near {_camera.nearClipPlane:F2} / far {mapFar:F0} world units "
                          + $"(far raised from the anchor's {anchor.farClipPlane:F0} so the map's far edge "
                          + $"is not sliced off). Trigger: {_rebuildTrigger}.");
        VRCameraPolicy.Sweep("map rig built");
        RenderQuality.RequestEyeTargetDiagnostics("map rig built");
    }

    /// <summary>Headroom on the map-rig far plane, so map decoration outside the parchment's own
    /// bounds (mountains, sea, route props) is not clipped either.</summary>
    private const float MapFarPlaneMargin = 1.5f;

    /// <summary>The seat the live map rig was built on (re-used by <see cref="RecenterMap"/>).</summary>
    private MapRoomSeat.Seat _mapSeat;

    /// <summary>One-shot "waiting for measurable parchment bounds" line per wait.</summary>
    private bool _mapSeatWaitLogged;

    /// <summary>
    /// Put the player back at the map-room seat.
    ///
    /// <para>THE HORIZONTAL HEAD OFFSET IS NULLED, THE VERTICAL ONE IS NOT, and at this scale that
    /// distinction is the difference between standing at the table and standing 200 world units
    /// away from it. The rig root is scaled, so the player's own offset from their play-space
    /// origin is multiplied by <c>Scale</c>: half a metre of real offset becomes hundreds of world
    /// units. Nulling the horizontal part puts the HEAD exactly at the seat whatever corner of the
    /// room the player is physically standing in (the same correction <see cref="RecenterMenu"/>
    /// makes at 1:1). The VERTICAL part is deliberately KEPT: the tracking floor is the real floor,
    /// so the player's eyes land at their own real standing height above it — a tall player looks
    /// further down at the map, exactly as at a real table.</para>
    ///
    /// <para>THE HEAD'S OWN YAW IS ABSORBED TOO (user request 2026-09-03: "die Startausrichtung
    /// ist oft verdreht"). Until this build the rig root was written <c>seat.Rotation</c> and
    /// nothing else — but the player's VIEW is <c>rigRotation ∘ headLocalRotation</c>, so a player
    /// who happened to be standing turned 25° in their play space at the moment the room built
    /// arrived looking 25° past the table. The ModBuild 410 log shows it: the head at the seat
    /// (x −184, i.e. the −X end, table forward = world yaw 90°) placed its first window from a
    /// gaze at world yaw 113°. This is the same masked re-aim <see cref="ApplyRingSeat"/> already
    /// performs for the scenario ring and the one Demeo performs in
    /// <c>InputTracking.Recenter</c>: the rig yaw is <c>seat.Yaw ∘ headLocalYaw⁻¹</c>, so the
    /// HEAD's world yaw is exactly the seat's, whatever direction the body is facing. One-shot:
    /// this runs at the pending recentre of every map-rig build (main menu, back from a
    /// scenario, after a load — all of them build the map rig) and on the B+Y chord, and never
    /// per frame, so it cannot fight the player's own turning afterwards.</para>
    /// </summary>
    private void RecenterMap()
    {
        if (_rigRoot == null || _camera == null)
            return;
        float scale = _rigRoot.transform.localScale.x;
        Quaternion headYawLocal = YawOnly(_camera.transform.localRotation);
        float headYawBefore = YawOnly(_camera.transform.rotation).eulerAngles.y;
        Quaternion yaw = _mapSeat.Rotation * Quaternion.Inverse(headYawLocal);
        Vector3 headLocal = _camera.transform.localPosition;
        var flat = new Vector3(headLocal.x, 0f, headLocal.z);

        _rigRoot.transform.rotation = yaw;
        _rigRoot.transform.position = _mapSeat.FloorPosition - yaw * (flat * scale);
        RigPoseVersion++;

        MapRoomDriver.NoteEyeHeight(headLocal.y);
        VRLog.Info("Rig", $"Map rig recentered — head at {_camera.transform.position} "
                          + $"({headLocal.y:F2} m real above the tracking floor, "
                          + $"{headLocal.y - MapRoomSeat.TableTopHeightMeters:F2} m above the parchment "
                          + $"surface at y={_mapSeat.TopY:F2}), rig root at {_rigRoot.transform.position}, "
                          + $"scale {scale:F2}. Horizontal head offset ({flat.x:F2}, {flat.z:F2}) m nulled; "
                          + "vertical kept (the tracking floor is the real floor).");

        // THE FALSIFIER FOR THE ORIENTATION. The head's forward, flattened and read back off the
        // camera AFTER the write, against the seat's own forward (which by construction points from
        // the floor point at the map centre). The residual must read ~0° on every entry; a
        // non-zero residual is a head yaw this recentre did not absorb.
        Vector3 seatForward = _mapSeat.Rotation * Vector3.forward;
        Vector3 headForward = _camera.transform.forward;
        headForward.y = 0f;
        float residualDeg = headForward.sqrMagnitude > 1e-6f
            ? Vector3.SignedAngle(seatForward, headForward.normalized, Vector3.up)
            : 0f;
        float headYawAfter = YawOnly(_camera.transform.rotation).eulerAngles.y;
        Vector3 tableCentre = MapRoomDriver.ParchmentRenderer != null
            ? MapRoomDriver.ParchmentRenderer.bounds.center
            : _mapSeat.FloorPosition + seatForward;
        // HW-VERIFY
        VRLog.Note("Rig", $"MAP ROOM SPAWN: spawn point (tracking floor) "
                          + $"({_mapSeat.FloorPosition.x:F2},{_mapSeat.FloorPosition.y:F2},"
                          + $"{_mapSeat.FloorPosition.z:F2}) wu, table centre "
                          + $"({tableCentre.x:F2},{tableCentre.y:F2},{tableCentre.z:F2}) wu, "
                          + $"seat yaw {_mapSeat.YawDegrees:F1}° (faces the map centre from the "
                          + $"{_mapSeat.ViewSide} side). Head world yaw BEFORE the recentre "
                          + $"{headYawBefore:F1}°, AFTER {headYawAfter:F1}° — the head's own "
                          + $"{headYawLocal.eulerAngles.y:F1}° of play-space yaw was absorbed into the "
                          + $"rig root (rig yaw now {yaw.eulerAngles.y:F1}°). RESIDUAL between the "
                          + $"head's forward and the spawn→table direction: {residualDeg:F2}° "
                          + "(must read ~0°; anything else is a yaw this one-shot did not absorb). "
                          + "Spawn-only: the rig is never re-yawed per frame, so the player's own "
                          + "turning afterwards is untouched.");

        // The head now stands at the seat facing the table: the one moment the corner windows that
        // were already floating before this room was placed can be seated on their corners
        // (once per engage — MapRoomDriver spends the flag).
        MapRoomDriver.NoteRecentered();
    }
}
