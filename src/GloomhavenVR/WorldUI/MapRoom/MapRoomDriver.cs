using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE 3D MAP ROOM — phase 1: the player stands in the campaign map at a table-sized scale and
/// sees the parchment in stereo. Owner of the mode's on/off decision, of the held parchment
/// override and of the head-camera icon layer. It does NOT own the rig; <c>VRRigDriver</c> does,
/// and it asks this class two questions (is the mode wanted, and where is the seat).
///
/// <para>THE ARCHITECTURE IN ONE LINE: do not move the map — scale the rig to it. The game writes
/// ABSOLUTE world positions into every map location (<c>MapChoreographer</c> does it at eight
/// separate spawn sites, and the party token likewise), so a mod transform on a shared ancestor is
/// correct only until the next <c>InitMap</c>, quest unlock, city↔world switch or travel
/// animation. Scaling the player instead touches no game state at all, which is what makes the
/// whole feature multiplayer-safe by construction: two players on different settings are looking
/// at the same map, and the toggle is a PRESENTATION change, not a state migration.</para>
///
/// <para>THE PREDICATE IS POSITIVE, AND THAT IS THE HIGHEST-RISK DECISION IN THE FILE.
/// <c>VRRigDriver</c> re-asserts "MENU rig = the mod layer ONLY" EVERY FRAME, and hardware test
/// #10 is why: on the campaign map the anchor's mask is the whole 3D world, and following it
/// rendered the giant map 1:1 below the player while the flat quad showed on top of it. A map-rig
/// flavour that leaked into the MAIN MENU would therefore break the menu — the loudest possible
/// regression. So the mode is gated on a POSITIVELY DECIDED map-open signal — a
/// <c>MapChoreographer</c> whose <c>worldMap</c> or <c>cityMap</c> is <c>activeInHierarchy</c>,
/// the same signal <c>FlatScreenStereo.TickFastMapEngage</c> already trusts as proof — and NEVER
/// on "not a scenario". In the main menu there is no <c>MapChoreographer</c> at all, so
/// <see cref="Wanted"/> is false, the rig stays <c>Menu</c>, and the menu path is byte-identical
/// to a build without this feature.</para>
///
/// <para>AND IT IS NOT ANCHORED TO THE ORBIT CAMERA. Test #8 anchored the rig to
/// <c>CameraController.s_CameraController</c> on the map scene and produced "giant map below the
/// player, black flat window"; that failure is written into the <c>[Rig] Experimental3DMap</c>
/// config description, and <c>VRRigDriver.UpdateBody</c> carries the standing warning that the
/// switch "must never silently re-enable the broken orbit-camera anchoring". The seat here comes
/// from the PARCHMENT RENDERER'S WORLD BOUNDS (<see cref="MapRoomSeat"/>); the orbit camera is
/// read for one horizontal direction and one culling mask, both with pure fallbacks.</para>
///
/// <para>MULTIPLAYER: phase 1 changes nothing on the wire. Peers are already visible on the map
/// today, unconditionally, and every client's menu rig sits at the same authored vantage — so
/// avatars pile up. That is phase 8's problem, deliberately not fixed here.</para>
/// </summary>
internal static class MapRoomDriver
{
    private const string Scope = "MapRoom";

    /// <summary>
    /// Frames of "no active map" tolerated before the mode stands down. A world↔city switch
    /// deactivates one map GameObject before it activates the other, so a zero-tolerance predicate
    /// would tear the rig down and rebuild it — i.e. TELEPORT the player — every time they press
    /// the city button. ~0.4 s at 72 Hz, far shorter than any real map exit.
    /// </summary>
    private const int StandDownGraceFrames = 30;

    /// <summary>Frames between <c>FindObjectOfType</c> attempts while no choreographer is cached.
    /// Near-free when the scene has none (the scan is type-indexed).</summary>
    private const int FindIntervalFrames = 10;

    private static global::MapChoreographer? _choreo;
    private static int _findFrame = int.MinValue;
    private static int _absentFrames;
    private static string _verdict = "not evaluated";

    private static readonly MapParchment Parchment = new();
    private static readonly MapIconLayer Icons = new();
    private static readonly MapLocationInteractor Locations = new();
    private static readonly MapButtonRail Buttons = new();
    private static readonly MapRoomHand Hand = new();

    // Facts the rig hands over at build time so the ONE map-room line can state them all together
    // (a diagnostic split across two lines is a diagnostic a log reader has to correlate by hand).
    private static bool _reportPending;
    private static int _maskBefore;
    private static int _maskAfter;
    private static string _maskSource = "?";
    private static float _reportScale;
    private static Vector3 _reportFloor;
    private static float _eyeHeightMeters;

    /// <summary>True when the mode is wanted THIS frame: the switch is on and a campaign map is
    /// provably open. Read by <c>VRRigDriver</c> to choose the rig flavour.</summary>
    internal static bool Wanted { get; private set; }

    /// <summary>True while the MAP rig is actually standing. This — not <see cref="Wanted"/> — is
    /// what other subsystems (the flat screen) test, so nothing hides before there is a room.</summary>
    internal static bool Active { get; private set; }

    /// <summary>Why <see cref="Wanted"/> currently reads as it does (log material).</summary>
    internal static string Verdict => _verdict;

    /// <summary>The parchment renderer while the room owns one — the bounds the seat came from.</summary>
    internal static MeshRenderer? ParchmentRenderer => Parchment.Renderer;

    /// <summary>The live map choreographer, or null. Exposed so the room's own parts can ask it the
    /// questions it alone can answer (which parents hold the locations, which map is shown) instead
    /// of sweeping the scene.</summary>
    internal static global::MapChoreographer? Choreographer => _choreo;

    /// <summary>The world point a hover card should fly at — just above the hovered location icon —
    /// or false while nothing is hovered. See <see cref="MapLocationInteractor.TryHoverAnchor"/>.</summary>
    internal static bool TryHoverAnchor(out Vector3 world)
    {
        if (Active)
            return Locations.TryHoverAnchor(out world);
        world = default;
        return false;
    }

    /// <summary>
    /// Press one of the game's own guildmaster bar buttons by mode, through the table rail's
    /// single dispatch (<c>ExecuteEvents.pointerClickHandler</c> on the real Toggle). Used by
    /// <see cref="GuildmasterDestinations.LeaveMode"/> to return to the map, which is what runs
    /// the game's mode Exit. False when the room is down or the bar carries no such button.
    /// </summary>
    internal static bool PressGuildmasterMode(EGuildmasterMode mode, string source) =>
        Active && Buttons.PressMode(mode, source);

    /// <summary>
    /// Evaluate the mode predicate. Called once per frame from <c>VRRigDriver.UpdateBody</c>
    /// BEFORE the rig kind is resolved, because the rig kind depends on the answer.
    /// </summary>
    internal static void TickPredicate()
    {
        if (Plugin.Experimental3DMap == null || !Plugin.Experimental3DMap.Value)
        {
            if (Wanted)
                _verdict = "[Rig] Experimental3DMap is off";
            Wanted = false;
            _absentFrames = 0;
            return;
        }
        if (!VRSession.IsRunning)
        {
            Wanted = false;
            _verdict = "no VR session";
            return;
        }

        if (_choreo == null)
        {
            if (_findFrame != int.MinValue && Time.frameCount - _findFrame < FindIntervalFrames)
            {
                DecayAbsence("MapChoreographer lookup throttled");
                return;
            }
            _findFrame = Time.frameCount;
            _choreo = Object.FindObjectOfType<global::MapChoreographer>();
            if (_choreo == null)
            {
                DecayAbsence("no MapChoreographer in the loaded scene(s) — this is the MAIN MENU case, "
                             + "and it is why the gate is POSITIVE and never 'not a scenario'");
                return;
            }
        }

        GameObject? world = _choreo.worldMap;
        GameObject? city = _choreo.cityMap;
        GameObject? shown = world != null && world.activeInHierarchy ? world
                          : city != null && city.activeInHierarchy ? city : null;
        if (shown == null)
        {
            DecayAbsence("a MapChoreographer exists but neither worldMap nor cityMap is "
                         + "active in the hierarchy");
            return;
        }

        // AND THE PARCHMENT MUST BE MEASURABLE. "The map GameObject is active" is not the same
        // fact as "there is something to stand on": the mesh can arrive a frame or several later.
        // If the rig flavour changed on the first fact alone, the menu rig would be torn down into
        // that gap and the headset would go untracked. Cheap — the renderer is cached and only
        // re-found across a world↔city switch — and it touches no materials.
        if (!Parchment.Acquire(_choreo))
        {
            DecayAbsence($"campaign map '{shown.name}' is active but its parchment renderer has no "
                         + "usable world bounds yet (no seat can be solved, so the menu rig stands)");
            return;
        }

        _absentFrames = 0;
        // The verdict string is rebuilt only when the DECISION changes — this method runs every
        // frame of every session, including the main menu, and a per-frame interpolated string
        // that nobody reads until the next log line is pure garbage.
        if (!Wanted || !ReferenceEquals(shown, _verdictShown))
        {
            _verdictShown = shown;
            _verdict = $"[Rig] Experimental3DMap on AND MapChoreographer map '{shown.name}' is active in "
                       + "the hierarchy AND its parchment has measurable world bounds";
        }
        Wanted = true;
    }

    /// <summary>The map GameObject <see cref="_verdict"/> was last written for (change detector).</summary>
    private static GameObject? _verdictShown;

    /// <summary>
    /// Grace handling for a transient absence: keep the mode up for
    /// <see cref="StandDownGraceFrames"/> frames so a world↔city switch is not a teleport.
    /// </summary>
    private static void DecayAbsence(string why)
    {
        if (!Wanted)
        {
            _verdict = why;
            return;
        }
        if (++_absentFrames < StandDownGraceFrames)
        {
            _verdict = $"{why} — holding for {StandDownGraceFrames - _absentFrames} more frame(s) "
                       + "(world↔city switch grace)";
            return;
        }
        Wanted = false;
        _absentFrames = 0;
        _verdictShown = null;
        _verdict = why;
    }

    /// <summary>
    /// Solve the seat for the rig build. The view side comes from the game's own map camera —
    /// its HORIZONTAL OFFSET from the focal point is the direction the flat game looks at the map
    /// from, so the player is seated on the side the map was authored to be read from. Only that
    /// direction is taken; position, height and FOV are not (test #8). Falls back to world −Z.
    /// </summary>
    internal static bool TrySolveSeat(out MapRoomSeat.Seat seat, out string sideSource)
    {
        seat = default;
        sideSource = "world -Z (fallback: no readable map camera)";
        if (!Parchment.Ensure(_choreo))
            return false;

        Vector3 side = MapRoomSeat.FallbackViewSide;
        CameraController cc = CameraController.s_CameraController;
        if (cc != null)
        {
            Vector3 diff = cc.m_CameraToFocalTargetDiff;
            if (new Vector3(diff.x, 0f, diff.z).sqrMagnitude > 1e-6f)
            {
                side = diff;
                sideSource = $"CameraController.m_CameraToFocalTargetDiff {diff} (DIRECTION ONLY — "
                             + "no position, no height, no FOV: that is test #8's mistake)";
            }
            else if (cc.m_Camera != null)
            {
                // ModBuild 181 — SECOND SOURCE, AND IT IS THE ONE THAT USUALLY ANSWERS. User: "Der
                // Spawnpunkt soll auch direkt vor dem Tisch sein, so dass man ihn richtig rum
                // direkt sehen kann." The focal DIFF is zero whenever the orbit camera happens to
                // sit on its focus, so on hardware this solve kept landing on the world -Z
                // fallback — an arbitrary edge that has nothing to do with how the map is authored
                // to be read. The camera's own ROTATION is never degenerate: its forward IS the
                // direction the flat game looks at the map from (logged at euler (80, 90, 0), i.e.
                // reading the map from its -X side), so the player belongs on the opposite side of
                // the centre from where that forward points. Still DIRECTION ONLY — no position,
                // no height, no FOV. That remains test #8's mistake and this does not repeat it.
                Vector3 fwd = cc.m_Camera.transform.forward;
                var flat = new Vector3(fwd.x, 0f, fwd.z);
                if (flat.sqrMagnitude > 1e-6f)
                {
                    side = -flat;
                    sideSource = $"the map camera's own FORWARD {fwd} negated (DIRECTION ONLY) — the "
                                 + "focal diff was degenerate, and this is the direction the flat game "
                                 + "reads the map from, so the seat is on the side it is authored for";
                }
            }
        }
        return MapRoomSeat.Solve(Parchment.WorldBounds, side, out seat);
    }

    /// <summary>
    /// The culling mask the map rig's head camera must use: the GAME MAP CAMERA'S OWN MASK, read
    /// and not guessed, OR'd with the mod layer. Falls back to the anchor's mask, and finally to
    /// the mod layer alone (i.e. exactly the menu policy) so a failure here can only ever make the
    /// map invisible — never the menu broken.
    /// </summary>
    /// <remarks>
    /// ALLOCATION-FREE, because this runs inside the per-frame mask re-assert. The human-readable
    /// half lives in <see cref="DescribeMapMaskSource"/>, which is called only at rig build.
    /// </remarks>
    internal static int ResolveMapMask(Camera? anchor, out int sourceMask)
    {
        CameraController cc = CameraController.s_CameraController;
        Camera? mapCam = cc != null ? cc.m_Camera : null;
        if (mapCam != null && mapCam.cullingMask != 0)
            sourceMask = mapCam.cullingMask;
        else if (anchor != null && anchor.cullingMask != 0)
            sourceMask = anchor.cullingMask;
        else
        {
            sourceMask = 0;
            return VRLayers.ModLayerMask;
        }
        return sourceMask | VRLayers.ModLayerMask;
    }

    /// <summary>Where <see cref="ResolveMapMask"/> took its source mask from — log material only,
    /// built once per rig build so the per-frame re-assert allocates nothing.</summary>
    internal static string DescribeMapMaskSource(Camera? anchor)
    {
        CameraController cc = CameraController.s_CameraController;
        Camera? mapCam = cc != null ? cc.m_Camera : null;
        if (mapCam != null && mapCam.cullingMask != 0)
            return $"the game map camera '{mapCam.name}'";
        if (anchor != null && anchor.cullingMask != 0)
            return $"the rig anchor camera '{anchor.name}' (no readable map camera)";
        return "NOTHING readable — falling back to the mod layer alone (i.e. exactly the menu policy, "
               + "so the worst case here is an invisible map, never a broken menu)";
    }

    /// <summary>
    /// The map rig has been built. Raises <see cref="Active"/>, tells the flat map render to stand
    /// down (the two must never both own the parchment) and banks the facts the report line needs.
    /// </summary>
    internal static void Engage(int maskBefore, int maskAfter, string maskSource,
                                float scale, Vector3 floorPosition)
    {
        FlatScreenStereo.MapRoomOwnsParchment = true;
        Active = true;
        // ModBuild 190: the travel confirmation. Installed from HERE rather than from
        // WorldUIModule because the room is the only thing it applies to, and because its prefix
        // must be live before the first location can be pressed. Idempotent.
        MapTravelConfirm.Install();
        // The selected character's loadout hand + wrist plate ([WorldUI] MapRoomHand, default on).
        // Nothing is built here — Engage only runs the one capability probe and arms the poll,
        // because the party display arrives with the map HUD several frames after the room does.
        Hand.Engage();
        // TELL THE MODE MACHINE THERE IS A TABLE HERE. Not a mode change — a correction to the
        // premise the three locomotion guards state in their own comments ("no table exists").
        // Without it the player stands in the room and cannot walk, fly, turn or zoom, because
        // every one of those subsystems stands down in Menu2D and the map screen IS Menu2D
        // (no Choreographer ⇒ no scenario). See Core/Events/VRModeStateMachine.TableInFrontOfPlayer.
        Core.Events.VRModeStateMachine.SetModRoom(true);
        _maskBefore = maskBefore;
        _maskAfter = maskAfter;
        _maskSource = maskSource;
        _reportScale = scale;
        _reportFloor = floorPosition;
        _eyeHeightMeters = 0f;
        _reportPending = true;
        // Re-arm the ONE map dump (FlatScreenStereo.LogMapSceneReport) so the room's own entry
        // produces a report too — the flat path's copy describes the same scene through a
        // different camera, and comparing the two is exactly how a later phase sizes what is
        // still invisible. Deliberately NOT a second logger.
        FlatScreenStereo.ArmMapSceneReport();
    }

    /// <summary>Record the player's measured eye height (real metres above the tracking floor) so
    /// the report can state where their eyes actually ended up relative to the parchment.</summary>
    internal static void NoteEyeHeight(float meters)
    {
        if (meters > 0.2f)
            _eyeHeightMeters = meters;
    }

    /// <summary>
    /// Per-frame upkeep while the map rig stands: keep the parchment acquired and overridden
    /// (it is re-found across a world↔city switch) and keep the icon buffer on the head camera.
    /// </summary>
    internal static void TickActive(Camera? head)
    {
        if (!Active)
            return;
        bool have = Parchment.Ensure(_choreo);
        Icons.Tick(have ? head : null, Parchment.Renderer, _choreo);
        // Laser + fingertip on the location icons (phase 4). Runs whether or not the parchment is
        // momentarily unmeasurable: the icons are their own GameObjects with their own colliders,
        // and losing input for the frames of a world↔city switch would be a worse bug than a
        // hover on an icon whose parchment is being swapped underneath it.
        Locations.Tick();
        // The guildmaster bar as physical table buttons (phase 6). Same reasoning as above for
        // running it unconditionally: the buttons are their own GameObjects and do not depend on
        // the parchment being measurable this frame.
        Buttons.Tick();
        // The game's own Reisen/Abbrechen buttons ride the floated quest window (phase 4b) — they
        // live in the flat map HUD, which this room does not draw, so without this they exist and
        // cannot be reached. Level-triggered; see MapTravelConfirm.
        MapTravelConfirm.Reconcile(ModalFallback.FloatedWindowWithId(UIWindowID.QuestPopup));
        // The loadout hand. Unconditional for the same reason as the two above: it hangs off the
        // player's own hand, not off the parchment, so a world<->city switch must not blink it. It
        // is entirely self-guarding (its own dial, its own capability latch, its own try) and never
        // throws into this call.
        Hand.Tick();
        if (have)
        {
            // The ONE map dump, from the room's own vantage (the flat path calls the same method
            // from its capture camera). One-shot per arm; free after that.
            FlatScreenStereo.LogMapSceneReport(
                CameraController.s_CameraController != null ? CameraController.s_CameraController.m_Camera : null,
                Parchment.Renderer,
                head != null ? head.cullingMask : 0,
                "3D map room");
        }
        if (_reportPending && have && _eyeHeightMeters > 0f)
            EmitReport();
    }

    /// <summary>
    /// Leave the map room: restore the parchment's own materials, detach the icon buffer, release
    /// the flat path's stand-down. Idempotent, and the ONLY exit — <c>VRRigDriver.TearDownRig</c>
    /// and <c>OnDestroy</c> both come here, so "leave nothing standing" has one implementation.
    /// </summary>
    internal static void StandDown(string reason)
    {
        if (!Active)
            return;
        Active = false;
        // Drop the table premise FIRST, before anything else is released: from this line on the
        // locomotion guards must read the plain Menu2D rule again, and the environment (which is
        // gated on the same predicate) must stand down with the room rather than one frame after it.
        Core.Events.VRModeStateMachine.SetModRoom(false);
        _reportPending = false;
        // FIRST: a borrowed card in the player's hand must never outlive the room it was read from.
        Hand.StandDown(reason);
        Locations.Release(reason);
        Buttons.Release(reason);
        Icons.Release(reason);
        // Hand the game's travel options back before the room disappears under them — a container
        // left parented into a host we are about to destroy would take the Reisen button with it.
        MapTravelConfirm.Reset();
        Parchment.Release(reason);
        FlatScreenStereo.MapRoomOwnsParchment = false;
        VRLog.Info(Scope, $"MAP ROOM stood down ({reason}) — parchment materials restored, icon command "
                          + "buffer detached, the flat map render is free to take over again.");
    }

    /// <summary>Drop the cached choreographer on a scene change (Unity fake-null revives the find).</summary>
    internal static void ForgetScene()
    {
        _choreo = null;
        _findFrame = int.MinValue;
    }

    /// <summary>
    /// THE map-room line. One line, everything a hardware round needs to decide whether the
    /// architecture survived: the predicate's verdict, the bounds it anchored to, the scale and
    /// eye height it produced IN REAL METRES, the culling mask before and after, and what it
    /// overrode. Measured values are labelled measured; the two derived heights say what they are
    /// derived from.
    /// </summary>
    private static void EmitReport()
    {
        _reportPending = false;
        MeshRenderer? r = Parchment.Renderer;
        Bounds b = Parchment.WorldBounds;
        float scale = Mathf.Max(_reportScale, 0.0001f);
        float widest = Mathf.Max(Mathf.Abs(b.size.x), Mathf.Abs(b.size.z));
        float eyeAboveTop = _eyeHeightMeters - MapRoomSeat.TableTopHeightMeters;
        VRLog.Info(Scope,
            "MAP ROOM ENGAGED.\n"
            + $"  predicate : WANTED — {_verdict}. (The gate is positive by construction: no "
            + "MapChoreographer ⇒ no map rig, so the MAIN MENU is untouched and keeps the "
            + "mod-layer-only mask of test #10.)\n"
            + $"  parchment : '{(r != null ? r.name : "<none>")}' on layer "
            + $"{(r != null ? r.gameObject.layer : -1)}, {(Parchment.IsCity ? "CITY" : "WORLD")} map. "
            + $"World bounds center {b.center} size {b.size} (thickness {Mathf.Abs(b.size.y):F3} world "
            + $"units, widest horizontal extent {widest:F2}). MEASURED off the live renderer.\n"
            + $"  scale     : {scale:F2} game units per real metre = widest extent {widest:F2} / target "
            + $"{MapRoomSeat.TargetMapWidthMeters:F2} m. The map therefore reads "
            + $"{widest / scale:F2} m across.\n"
            + $"  seat      : tracking floor at {_reportFloor}, i.e. "
            + $"{MapRoomSeat.TableTopHeightMeters:F2} m (real) below the parchment top y={b.max.y:F2} and "
            + $"{MapRoomSeat.EdgeStandoffMeters:F2} m (real) outside the map's near edge.\n"
            + $"  eye       : {_eyeHeightMeters:F2} m above the tracking floor (MEASURED from the tracked "
            + $"head pose), i.e. {eyeAboveTop:F2} m above the parchment surface — a person standing at a "
            + "table.\n"
            + $"  mask      : 0x{_maskBefore:X8} → 0x{_maskAfter:X8}, source = {_maskSource} (READ off the "
            + "live camera, not guessed).\n"
            + $"  overrode  : the parchment renderer's {(r != null ? r.sharedMaterials.Length : 0)} submesh "
            + $"material(s) — override {(Parchment.Applied ? "HELD" : "NOT APPLIED, see the warning above")}. "
            + "Nothing else on the map is overridden, so anything ELSE that is invisible is a deferred "
            + "renderer with no forward pass — read the MAP SCENE REPORT's pass-name census to see which.\n"
            + "  DISPROOF  : if the next log shows this line but the headset shows a black or absent map, "
            + "the parchment is not the thing being looked at — compare the seat above with the MAP SCENE "
            + "REPORT's map-root bounds. If the map is visible but EMPTY, the icon layer is the suspect "
            + "(MAP ROOM icons line). If the MAIN MENU regressed, this line must be absent there; if it is "
            + "present in the menu the predicate is wrong, not the mask.");
    }
}
