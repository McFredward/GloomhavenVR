using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
    // TOMBSTONE — ArcStepDegrees (34f) and MaxArcHalfDegrees (85f), removed in ModBuild 193.
    //
    // They were the map room's window arc: neighbours 34° apart, the layout reaching to ±85°, which
    // produced the five fixed seats 0°, ±34°, ±68°. BOTH NUMBERS WERE GUESSES AND BOTH WERE WRONG,
    // and the user reported the consequence of the second one:
    //
    //   "Neue Fenster spawnen irgendwo an der Seite wo man sie nicht sieht - sie sollen IM
    //    SICHTFELD spawnen, möglichst so das sie nicht mit einem anderen Fenster überlappen, aber
    //    IM SICHTFELD."
    //
    // 34° was a guess about how wide a window is. It is not: the hardware log's own MODAL WINDOW
    // SIZE lines measure the party roster at 0.29 m (≈14° at reading distance) and a full-width
    // 1920 px window at 1.00 m (≈45°), so the step was simultaneously twice too wide for the
    // narrow family and far too narrow for the wide one. 85° was a guess about the headset. A
    // Quest 3 over Virtual Desktop shows roughly ±55° monocular and less binocularly, so the fourth
    // window — placed at 68° by ModBuild 192, in that build's own log — was off the display.
    //
    // BOTH ARE NOW MEASURED AT RUNTIME instead of assumed: the usable cone off the head camera's
    // projection matrix, the window's width off the half-size the placement already carries. See
    // the ModBuild 193 header in ModalFallback.4.Tick.cs. Do not reintroduce a fixed step or a
    // fixed half-angle here — the whole point is that neither number is knowable at compile time.

    /// <summary>
    /// Item 2: lateral+vertical stagger (real meters) between successive floated windows so a
    /// secondary window opened FROM the primary spawns OVERLAPPING but not perfectly coincident
    /// with it — the user can then grab and separate them. Scaled by the diorama scale + capped.
    /// </summary>
    private const float SecondaryStaggerMeters = 0.08f;

    /// <summary>
    /// Item 3b: how much CLOSER to the head (real meters) each successive stacked window is pulled
    /// along the gaze, so a sub-menu opened from the pause menu sits clearly in the FOREGROUND of
    /// its parent (nearer → among the equal-order modal hosts it also depth-sorts in front).
    /// Scaled by the diorama scale like the lateral/vertical stagger.
    ///
    /// <para>ModBuild 181/193/234: in the 3D map room neither this nor the lateral stagger applies —
    /// windows are seated on angular reservations spread around a HALF CIRCLE in front of the
    /// player instead (see <see cref="TryClaimArcSeat"/> and the header of ArcSeats.cs), and when
    /// that arc is full the remedy is a depth pull with NO vertical term. The magnitude of that
    /// pull is <see cref="OverlapDepthStepMeters"/>, deliberately a separate constant from this
    /// one.</para>
    /// </summary>
    private const float SecondaryForegroundMeters = 0.14f;

    /// <summary>
    /// BOARD-SAFE SPAWN CLAMP (user request B; tightened for the board-cover fix): steepest
    /// allowed DOWNWARD placement angle (degrees below eye level) for the head→panel direction.
    /// Info/actor windows (and dialogs) open while the player looks DOWN at the control board, so
    /// raw-gaze placement landed the window LOW — right where the board sits — and the board
    /// covered it at spawn (the reported issue). Clamping the placement direction to this shallow
    /// pitch biases the window UP toward eye level so it lands in the readable view ABOVE the
    /// board's near edge; past this angle the direction is clamped and the window pulled slightly
    /// toward the head. Well inside <see cref="PanelPlacement.MinPitchDeg"/> (−30°) — modals are
    /// larger than the settings panel and must clear the board, so they sit closer to eye level
    /// (was 25°, which still landed a window ~0.5 m×scale below eye, i.e. down at the board).
    /// </summary>
    private const float MaxSpawnPitchDeg = 15f;

    /// <summary>How far a map-room window's final azimuth may drift from the angle it reserved
    /// before the hard cone clamp pulls it back, degrees. 1° is below anything the eye can judge at
    /// reading distance (~2 cm of lateral shift), so the clamp only ever fires on a real swing and
    /// never on float noise — and when it does fire it says so on the MODAL SPAWN CLAMP line.</summary>
    private const float MapConeDriftToleranceDeg = 1f;

    /// <summary>Request B: distance factor applied when the steep-gaze pitch clamp engages —
    /// the window is PULLED TOWARD THE HEAD so it stays near where the player is looking
    /// instead of sailing off along the flattened direction.</summary>
    private const float SteepGazePullFactor = 0.85f;

    /// <summary>
    /// LEVEL-MESSAGE family (tutorial boxes / action strips): steeper allowed downward
    /// placement pitch than the shared <see cref="MaxSpawnPitchDeg"/>. The 15° flatten was
    /// tuned for LARGE modals that must clear the board; the small tutorial windows open
    /// while the player reads the board looking 15–40° DOWN, and flattening their placement
    /// to 15° parked them near the horizon — the top edge of (or outside) the downward view
    /// (hardware log 2026-08-02, TB_12: raw gaze pose y 0.49 → clamped/raised to 20.37 ≈
    /// above eye level while the gaze was 17° down). These follow the gaze deeper; the
    /// board-cover risk is handled by the floor clamp + overlap resolve as before, with the
    /// VIEW-CONE guarantee below having the final word.
    /// </summary>
    private const float LevelMsgMaxSpawnPitchDeg = 30f;

    /// <summary>
    /// HARD view-cone guarantee for level-message spawns (user requirement: tutorial windows
    /// must ALWAYS spawn inside the current view): after ALL soft clamps ran (pitch flatten,
    /// board-plane floor, overlap raise/swing), the head→window direction is rotated back to
    /// within this angle of the CURRENT gaze forward, distance preserved. Well inside any HMD
    /// FOV half-angle (~45–50°), so a window at the cone edge is still comfortably on screen.
    /// Readability/visibility deliberately WINS over full board clearance here — a window
    /// partially over the board but in view beats one at the horizon nobody sees. Runs only
    /// from <see cref="ComputeHmdPose"/> (spawn / refloat / recall — never per frame).
    /// </summary>
    private const float LevelMsgMaxOffGazeDeg = 25f;

    /// <summary>
    /// Request B (board-cover fix): height of the board/table's TOP edge above the orbit-focus
    /// plane (<c>CameraController.FocusPoint</c>, the anchor <see cref="PanelLayout"/> measures all
    /// slot heights from — slots sit 0.02–0.55 m above it), real meters × diorama scale. The board
    /// floor clamp raises a freshly spawned modal so its BOTTOM edge (center − half-height) sits
    /// this far above the focus plane PLUS the window's own half-height — i.e. the window bottom
    /// clears the board's top edge with margin, instead of only its pivot clearing the plane.
    /// Accounting for the window height is the actual fix: a TALL window (results / actor info)
    /// whose CENTER was above the old fixed 0.35 m offset still hung its bottom DOWN into the board,
    /// so the board covered it. Now the taller the window, the higher its center is floored.
    /// </summary>
    private const float BoardTopClearanceMeters = 0.30f;

    /// <summary>Request B: the board floor / overlap raise never lifts a window above eye level +
    /// this margin — still BELOW the top of the head, so the window stays in comfortable view and
    /// is never pushed overhead. A little headroom (was 0.05 m) lets a tall window rise far enough
    /// to clear a tall board's top edge before this cap wins; at/near eye level the window is
    /// readable, which is the actual goal (readability always beats full board clearance in the
    /// degenerate case of a table plane above the head).</summary>
    private const float MaxAboveEyeMeters = 0.10f;

    // MaxSpawnTiltDeg (15° upward tilt, top toward the player) IS GONE, AND THE ABSENCE IS THE
    // INVARIANT — user ruling ModBuild 189, verbatim:
    //
    //   "Die Fenster die von Begin an spawnen sind in der pitch-achse zu eine gedreht, das soll
    //    nicht sein. Ausschließlich die yaw achse."
    //
    // The constant existed for request B: when the board-plane / steep-gaze clamp RAISED a window
    // while the player was reading the table, its top was tipped back toward the eyes so the raised
    // panel still faced them. The reasoning was sound and the effect is exactly what he is
    // rejecting — and it fired far more often than "occasionally", because looking down at the
    // board (or at the map parchment) is the normal reading posture, so the steep-gaze clamp
    // engages on most spawns and every clamped spawn came out pitched. It is visible in
    // .planning/debug/flackern_story.jpg: the story window's top edge and inner picture frame both
    // slope, and the quest log on the right is tipped out of upright.
    //
    // A NEW TILT TUNABLE HERE WOULD BE THE SAME BUG WITH A DIAL ON IT. What the tilt bought —
    // "the raised panel still faces the eyes" — is bought instead by the placement clamps
    // themselves: MaxSpawnPitchDeg (15°) already keeps a window near eye level rather than down at
    // the board, so an upright panel there IS square to the gaze within a few degrees. The residual
    // foreshortening of a perfectly upright panel seen from 15° above is cos 15° = 0.97 of its
    // height, which nobody can see and which costs nothing in legibility.
    //
    // ENFORCED, NOT MERELY REMOVED: every pose this file produces goes through
    // <see cref="Upright"/> as its LAST step, which strips any pitch/roll a future writer adds and
    // prints what it measured on the MODAL SPAWN CLAMP line. See there.

    /// <summary>
    /// Request B: the board/table plane height, world units — the camera orbit focus the whole
    /// panel layout is anchored on (<see cref="PanelLayout.TryGetAnchor"/> uses the same
    /// <c>CameraController.FocusPoint</c> as "table center"; slot heights are measured from it).
    /// False outside a room of the mod's own (no board and no map table → no floor to clamp against).
    /// </summary>
    private static bool TryGetBoardPlaneY(out float y)
    {
        // ModBuild 178: the 3D map room counts too. Its orbit camera IS readable and its focus
        // point is the map's own centre, i.e. the plane of the parchment the player stands at —
        // the same relationship the board plane has to a scenario table, so the clamp means the
        // same thing. (The map rig is scaled, but this is a world-space y and PanelLayout.WorldScale
        // carries the scale on the other side, so nothing double-counts.)
        CameraController controller = CameraController.s_CameraController;
        if (controller != null && VRModeStateMachine.TableInFrontOfPlayer)
        {
            y = controller.FocusPoint.y;
            return true;
        }
        y = 0f;
        return false;
    }

    /// <summary>
    /// Request B: clamp a candidate modal spawn position so the window NEVER lands below /
    /// inside the board plane and never down a steep gaze. Two independent clamps, both
    /// event-gated (this runs only from <see cref="ComputeHmdPose"/>, i.e. at spawn, presence-
    /// regain refloat and lost-menu recall — NEVER per frame, the placement-healing regression
    /// rule):
    /// 1. STEEP-GAZE pitch clamp — if the head→panel direction points more than
    ///    <see cref="MaxSpawnPitchDeg"/> below eye level (the player is reading the board),
    ///    the direction is clamped to that pitch and the distance shortened by
    ///    <see cref="SteepGazePullFactor"/> (pulled toward the head).
    /// 2. BOARD-PLANE floor — the position's Y is raised so the window's BOTTOM edge
    ///    (center − half-height) clears the board's top edge: table plane +
    ///    <see cref="BoardTopClearanceMeters"/> × scale + the window's half-height (capped at eye
    ///    level + <see cref="MaxAboveEyeMeters"/> so the readability goal always wins). Passing the
    ///    half-height is what keeps a tall window from hanging its bottom down into the board.
    /// Returns the human-readable clamp reason, or null when the pose passed through unchanged.
    /// </summary>
    private static string? ClampSpawnPose(Vector3 headPos, Vector3 headForward, ref Vector3 pos,
        float scale, Vector2 half, float maxPitchDeg, out HeightDecision height)
    {
        string? reason = null;
        height = HeightDecision.None(headPos.y, pos.y, half.y);

        // 1. Steep-gaze pitch clamp (placement direction, not the panel's own rotation).
        //    The limit is per-family now: level-message windows follow the gaze deeper
        //    (LevelMsgMaxSpawnPitchDeg) so they stay in the downward view.
        Vector3 to = pos - headPos;
        float dist = to.magnitude;
        if (dist > 1e-4f)
        {
            float pitchDeg = Mathf.Asin(Mathf.Clamp(to.y / dist, -1f, 1f)) * Mathf.Rad2Deg;
            if (pitchDeg < -maxPitchDeg)
            {
                Vector3 flatDir = to;
                flatDir.y = 0f;
                if (flatDir.sqrMagnitude < 1e-6f)
                {
                    flatDir = headForward;
                    flatDir.y = 0f;
                    if (flatDir.sqrMagnitude < 1e-6f)
                        flatDir = Vector3.forward;
                }
                flatDir.Normalize();
                float rad = maxPitchDeg * Mathf.Deg2Rad;
                Vector3 dir = flatDir * Mathf.Cos(rad) - Vector3.up * Mathf.Sin(rad);
                pos = headPos + dir * (dist * SteepGazePullFactor);
                reason = $"gaze {-pitchDeg:F0}° below eye level (limit {maxPitchDeg:F0}°) — " +
                         $"pitch-clamped and pulled toward the head (x{SteepGazePullFactor:F2})";
            }
        }

        // 2. Board-plane floor: the window's BOTTOM edge (center − half-height) must clear the
        //    board's TOP edge — not just its pivot the plane — so a tall window is never buried in
        //    the board. Raise the CENTER to boardTop + half-height; cap near eye level so it never
        //    rises overhead (readability wins in the degenerate table-above-head case).
        //
        //    THE REASON STRING NAMES THE TERM THAT ACTUALLY DECIDED. Until ModBuild 199 it printed
        //    the board-top formula and appended ", capped near eye level" when the cap had won — so
        //    the sentence quoted an expression it had NOT evaluated, and the ModBuild 198 log
        //    contains five lines stating "board plane 0.00 + top-clear 0.30 m × scale 198.12 +
        //    half-height 55.72" (= 115.16) beside the result −134.74. Nobody could check that line
        //    against its own arithmetic, which is why a window at ankle height shipped. Every
        //    candidate is now printed with its value and the winner is named; see HeightDecision.
        if (TryGetBoardPlaneY(out float boardY))
        {
            float boardTopFloorY = boardY + BoardTopClearanceMeters * scale + half.y;
            float eyeCapY = headPos.y + MaxAboveEyeMeters * scale;
            bool eyeCapWon = eyeCapY < boardTopFloorY; // readability wins in the degenerate case
            float targetY = eyeCapWon ? eyeCapY : boardTopFloorY;
            bool raised = pos.y < targetY;
            height = new HeightDecision
            {
                Have = true,
                EyeY = headPos.y,
                BoardY = boardY,
                HalfHeight = half.y,
                BoardTopFloorY = boardTopFloorY,
                EyeCapY = eyeCapY,
                TargetY = targetY,
                EyeCapWon = eyeCapWon,
                Raised = raised,
                FromY = pos.y,
            };
            if (raised)
            {
                string floor = eyeCapWon
                    ? $"raised y {pos.y:F2} → {targetY:F2} — THE EYE CAP DECIDED IT: eye level "
                      + $"{headPos.y:F2} + {MaxAboveEyeMeters:F2} m × scale {scale:F2} = {eyeCapY:F2}, "
                      + $"which is BELOW the board-top floor of {boardTopFloorY:F2} (board plane "
                      + $"{boardY:F2} + top-clear {BoardTopClearanceMeters:F2} m × scale {scale:F2} + "
                      + $"half-height {half.y:F2}), so the window is readable rather than fully clear "
                      + "of the board"
                    : $"raised y {pos.y:F2} → {targetY:F2} — THE BOARD-TOP FLOOR DECIDED IT: board "
                      + $"plane {boardY:F2} + top-clear {BoardTopClearanceMeters:F2} m × scale "
                      + $"{scale:F2} + half-height {half.y:F2} = {boardTopFloorY:F2}, which is below "
                      + $"the eye cap of {eyeCapY:F2}, so its bottom clears the board top";
                reason = reason == null ? floor : $"{reason}; {floor}";
                pos.y = targetY;
            }
        }
        return reason;
    }

    /// <summary>
    /// EVERY TERM THE SPAWN HEIGHT WAS DECIDED FROM, so the MODAL SPAWN CLAMP line can be checked
    /// against the room rather than only against itself. Both candidates are carried whether or not
    /// they fired, together with the eye level and the board plane they were built from — the "print
    /// the baseline too" rule, applied to the one number the user photographs when it is wrong.
    /// </summary>
    private struct HeightDecision
    {
        /// <summary>False when no board/table plane was found, i.e. no height clamp ran at all.</summary>
        public bool Have;

        /// <summary>The eye level the clamp measured from, world units. THE FIELD THE ModBuild 198
        /// LINE DID NOT PRINT, and the one that was wrong.</summary>
        public float EyeY;

        public float BoardY;
        public float HalfHeight;

        /// <summary>Candidate 1 — the centre height at which the window's BOTTOM clears the board top.</summary>
        public float BoardTopFloorY;

        /// <summary>Candidate 2 — eye level plus <see cref="MaxAboveEyeMeters"/>, the readability cap.</summary>
        public float EyeCapY;

        /// <summary>The lower of the two, i.e. the floor the pose was actually held to.</summary>
        public float TargetY;

        /// <summary>Which candidate won — true = the eye cap, false = the board-top floor.</summary>
        public bool EyeCapWon;

        /// <summary>Whether the pose was below <see cref="TargetY"/> and therefore actually moved.</summary>
        public bool Raised;

        public float FromY;

        internal static HeightDecision None(float eyeY, float fromY, float halfHeight) =>
            new() { Have = false, EyeY = eyeY, FromY = fromY, HalfHeight = halfHeight };
    }

    // ---- SPAWN OVERLAP AVOIDANCE (user request A) ---------------------------------------------
    //
    // A freshly spawned/recalled modal must not open INSIDE another mod-placed object — the
    // narrator/story dialog routinely spawned interpenetrating the control board (both are
    // head-relative at similar reach). Obstacles considered: the Cards control board / play
    // tray (discovered READ-ONLY at runtime by its root object name — Cards code is not
    // touched) and the other currently-open converted modals. Resolution order per the spawn
    // rules: raise up first (respecting the same eye-level readability cap as the board-plane
    // floor clamp), then swing laterally around the head toward free space, never leaving
    // ±OverlapMaxYawDeg of the original placement direction; best-effort (least-overlap
    // candidate) when nothing clears. These are SPAWN RULES ONLY — this path runs exclusively
    // from ComputeHmdPose (spawn / presence-regain refloat / lost-menu recall), NEVER per
    // frame (the placement-healing regression rule), so the user can freely move everything
    // afterwards.

    /// <summary>Safety margin (real meters × scale) padded around the new modal's bounds.</summary>
    private const float OverlapMarginMeters = 0.04f;

    /// <summary>Clearance (real meters × scale) between an obstacle's top and the raised modal's bottom.</summary>
    private const float OverlapRaiseClearanceMeters = 0.05f;

    /// <summary>Max lateral deviation from the original placement direction (degrees) — the
    /// window must still spawn in the view area, roughly along the gaze.</summary>
    private const float OverlapMaxYawDeg = 30f;

    /// <summary>Lateral search step (degrees) — free side first, then the other side.</summary>
    private const float OverlapYawStepDeg = 10f;

    /// <summary>Assumed thickness of a floated modal (real meters) for the box test.</summary>
    private const float OverlapPanelDepthMeters = 0.03f;

    // Spawn-time scratch (this path never runs per frame).
    private static readonly List<string> ObstacleNames = new(4);
    private static readonly List<Bounds> ObstacleBoundsList = new(4);
    private static readonly Vector3[] ObstacleCorners = new Vector3[4];

    /// <summary>
    /// Gather the world-space AABBs the new modal must not intersect. The control board is
    /// discovered by its root name ("GloomhavenVR.PlayTray" — read-only; GameObject.Find only
    /// returns it while it is active, i.e. actually visible) and measured via its renderers
    /// (board slab + docked cards/buttons). Other floated modals come from <see cref="Converted"/>;
    /// <paramref name="self"/> is excluded (refloat/recall re-places an EXISTING panel).
    /// <paramref name="includeModals"/> is false for staggered secondaries — those overlap their
    /// parent DELIBERATELY (item 2/3b foreground stacking), only the board is avoided.
    /// </summary>
    private static void CollectSpawnObstacles(ConvertedPanel? self, bool includeModals, float scale)
    {
        ObstacleNames.Clear();
        ObstacleBoundsList.Clear();

        GameObject tray = GameObject.Find("GloomhavenVR.PlayTray");
        if (tray != null)
        {
            bool has = false;
            var b = new Bounds();
            Renderer[] renderers = tray.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled)
                    continue;
                if (!has)
                {
                    b = r.bounds;
                    has = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }
            if (has)
            {
                ObstacleNames.Add(tray.name);
                ObstacleBoundsList.Add(b);
            }
        }

        if (!includeModals)
            return;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (!wp.Panel.IsAlive || wp.Panel.HostGo == null || !wp.Panel.HostGo.activeInHierarchy
                || ReferenceEquals(wp.Panel, self))
                continue;
            RectTransform host = wp.Panel.HostRect;
            if (host == null)
                continue;
            host.GetWorldCorners(ObstacleCorners);
            var b = new Bounds(ObstacleCorners[0], Vector3.zero);
            for (int c = 1; c < 4; c++)
                b.Encapsulate(ObstacleCorners[c]);
            b.Expand(OverlapPanelDepthMeters * scale); // flat rect → thin slab
            ObstacleNames.Add(wp.Window != null ? wp.Window.name : host.name);
            ObstacleBoundsList.Add(b);
        }
    }

    /// <summary>
    /// World-axis AABB of the new modal at a candidate center: the panel is an upright thin
    /// slab yawed to face the head (the same yaw <see cref="ComputeHmdPose"/> derives from the
    /// head→position direction), conservatively boxed with <see cref="OverlapMarginMeters"/>.
    /// </summary>
    private static Bounds CandidateBounds(Vector3 pos, Vector3 headPos, Vector2 half, float scale)
    {
        Vector3 flat = pos - headPos;
        flat.y = 0f;
        float yaw = flat.sqrMagnitude < 1e-6f ? 0f : Mathf.Atan2(flat.x, flat.z);
        float cos = Mathf.Abs(Mathf.Cos(yaw));
        float sin = Mathf.Abs(Mathf.Sin(yaw));
        float halfDepth = OverlapPanelDepthMeters * 0.5f * scale;
        float margin = OverlapMarginMeters * scale;
        var ext = new Vector3(
            cos * half.x + sin * halfDepth + margin,
            half.y + margin,
            sin * half.x + cos * halfDepth + margin);
        return new Bounds(pos, ext * 2f);
    }

    /// <summary>
    /// Total penetration across all obstacles (sum of per-obstacle minimal separation depths,
    /// 0 = clear) + the index of the most-penetrated obstacle.
    /// </summary>
    private static float OverlapAmount(Bounds candidate, out int worstIdx)
    {
        float total = 0f;
        float worst = 0f;
        worstIdx = -1;
        for (int i = 0; i < ObstacleBoundsList.Count; i++)
        {
            Bounds b = ObstacleBoundsList[i];
            float px = Mathf.Min(candidate.max.x, b.max.x) - Mathf.Max(candidate.min.x, b.min.x);
            float py = Mathf.Min(candidate.max.y, b.max.y) - Mathf.Max(candidate.min.y, b.min.y);
            float pz = Mathf.Min(candidate.max.z, b.max.z) - Mathf.Max(candidate.min.z, b.min.z);
            if (px <= 0f || py <= 0f || pz <= 0f)
                continue;
            float pen = Mathf.Min(px, Mathf.Min(py, pz));
            total += pen;
            if (pen > worst)
            {
                worst = pen;
                worstIdx = i;
            }
        }
        return total;
    }

    /// <summary>
    /// Spawn-time overlap resolution (runs AFTER the pitch/board-plane clamps, spawn/refloat/
    /// recall only): if the modal's projected bounds intersect an obstacle, first RAISE it so
    /// its bottom clears the tallest overlapped obstacle (capped at eye level +
    /// <see cref="MaxAboveEyeMeters"/> — the readability rule), then SWING it laterally around
    /// the head (constant distance, free side first, ≤ ±<see cref="OverlapMaxYawDeg"/>°).
    /// Falls back to the least-overlapping candidate. Returns the human-readable resolution
    /// note for the MODAL SPAWN CLAMP log line, or null when the pose was already clear.
    /// </summary>
    private static string? ResolveSpawnOverlap(Vector3 headPos, ref Vector3 pos, float scale,
        Vector2 half, ConvertedPanel? self, bool includeModals)
    {
        if (half.x <= 1e-5f || half.y <= 1e-5f)
            return null;
        CollectSpawnObstacles(self, includeModals, scale);
        if (ObstacleBoundsList.Count == 0)
            return null;

        Bounds cand = CandidateBounds(pos, headPos, half, scale);
        float startPen = OverlapAmount(cand, out int worstIdx);
        if (startPen <= 0f)
            return null;
        string obstacle = ObstacleNames[worstIdx];
        Vector3 original = pos;
        Vector3 best = pos;
        float bestPen = startPen;

        // Step 1 — raise: lift the center until the bottom edge clears every overlapped
        // obstacle's top, capped at eye level (same cap as the board-plane floor clamp).
        float eyeCap = headPos.y + MaxAboveEyeMeters * scale;
        float targetY = pos.y;
        for (int i = 0; i < ObstacleBoundsList.Count; i++)
        {
            if (cand.Intersects(ObstacleBoundsList[i]))
                targetY = Mathf.Max(targetY,
                    ObstacleBoundsList[i].max.y + half.y + OverlapRaiseClearanceMeters * scale);
        }
        Vector3 raised = pos;
        raised.y = Mathf.Max(pos.y, Mathf.Min(targetY, eyeCap));
        float pen = OverlapAmount(CandidateBounds(raised, headPos, half, scale), out _);
        if (pen < bestPen)
        {
            bestPen = pen;
            best = raised;
        }
        if (pen <= 0f)
        {
            pos = raised;
            return $"intersected '{obstacle}' — raised +{(raised.y - original.y) / scale:F2} m";
        }

        // Step 2 — lateral: swing around the head at constant distance (raised height kept),
        // free side (away from the blocking obstacle) first, 10° steps up to ±30°.
        Vector3 flat = new Vector3(pos.x - headPos.x, 0f, pos.z - headPos.z);
        if (flat.sqrMagnitude > 1e-6f)
        {
            Vector3 toObs = ObstacleBoundsList[worstIdx].center - headPos;
            toObs.y = 0f;
            // Cross(flat, toObs).y > 0 → obstacle sits to the RIGHT of the placement
            // direction → free space is to the LEFT (negative yaw), and vice versa.
            float freeSide = Vector3.Cross(flat, toObs).y > 0f ? -1f : 1f;
            for (int pass = 0; pass < 2; pass++)
            {
                float side = pass == 0 ? freeSide : -freeSide;
                for (float step = OverlapYawStepDeg; step <= OverlapMaxYawDeg + 0.01f;
                     step += OverlapYawStepDeg)
                {
                    Vector3 swung = headPos + Quaternion.Euler(0f, side * step, 0f) * flat;
                    swung.y = raised.y;
                    pen = OverlapAmount(CandidateBounds(swung, headPos, half, scale), out _);
                    if (pen < bestPen)
                    {
                        bestPen = pen;
                        best = swung;
                    }
                    if (pen <= 0f)
                    {
                        pos = swung;
                        return $"intersected '{obstacle}' — raised +{(raised.y - original.y) / scale:F2} m, " +
                               $"swung {step:F0}° {(side < 0f ? "left" : "right")}";
                    }
                }
            }
        }

        // Best-effort: nothing cleared inside the view cone — take the least-overlapping pose.
        pos = best;
        return $"intersected '{obstacle}' — best-effort least-overlap pose " +
               $"(residual {bestPen / Mathf.Max(scale, 1e-4f):F2} m, moved " +
               $"{(best - original).magnitude / Mathf.Max(scale, 1e-4f):F2} m)";
    }

    /// <summary>
    /// The new modal's world half-extents (width/height halves, world units) as
    /// <see cref="CanvasConversion.PlaceHost"/> will render it: pixels × CanvasScaleMm ×
    /// worldScale (= diorama scale × the window's board-relative extra scale).
    /// </summary>
    private static Vector2 PanelWorldHalfSize(ConvertedPanel panel, float worldScale)
    {
        RectTransform rect = panel.HostRect;
        if (rect == null)
            return default;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        return new Vector2(rect.rect.width, rect.rect.height) * (0.5f * metersPerPixel * worldScale);
    }

    /// <summary>
    /// The EXACT placement inputs one <see cref="ComputeHmdPose"/> call consumed, so the very same
    /// placement can be REPLAYED later against different panel geometry.
    ///
    /// <para>WHY IT EXISTS (first-open pose bug, 2026-08-02): a floated window is placed BEFORE its
    /// content fit and its board-relative scale re-derivation have run — <see cref="PlaceAtHmd"/>
    /// measures <see cref="PanelWorldHalfSize"/> from the PRE-fit host rect (typically the whole
    /// 1920x1080 window) at the PRE-fit extraScale. Every clamp in <see cref="ComputeHmdPose"/>
    /// (board-top clearance, eye cap, overlap resolve, view cone) is a function of that half-size,
    /// so the window is clamped as if it were metres tall and lands somewhere the FINAL, fitted
    /// window never needed to be. The fix re-runs the placement once the geometry is final
    /// (<see cref="TickPoseRePlace"/>) — but re-reading the LIVE head would also fold in ~150 ms of
    /// head motion, which would move windows whose pose was already right. Replaying from the
    /// stored anchor keeps the placement a pure function of (spawn gaze, final geometry): a window
    /// whose clamps do not change lands on the byte-identical pose and is never written at all.</para>
    ///
    /// <para><see cref="Valid"/> is false for any window NOT placed through
    /// <see cref="ComputeHmdPose"/> — above all the level-message rule-2 branch, which restores a
    /// verbatim stored chain pose and must never be re-placed.</para>
    /// </summary>
    private struct SpawnAnchor
    {
        public bool Valid;

        /// <summary>Head position at spawn (world) — the origin every clamp measures from.</summary>
        public Vector3 HeadPos;

        /// <summary>Head forward at spawn — the gaze the view-cone clamp rotates back toward.</summary>
        public Vector3 HeadForward;

        /// <summary>The RAW gaze pose (reading distance + stagger) BEFORE any clamp ran.</summary>
        public Vector3 RawPos;

        /// <summary>Diorama scale at spawn — <see cref="GrabbableModal"/> also snapshots it, so
        /// replaying with it keeps the clamp math matching what the panel actually renders at.</summary>
        public float Scale;

        /// <summary>Stacking index the raw pose already carries (replay must not stagger twice).</summary>
        public int StaggerIndex;

        /// <summary>Level-message family (closer distance, deeper pitch, hard view cone).</summary>
        public bool LevelMessage;

        /// <summary>
        /// The map room's claimed arc slot (−1 = this spawn is not arc-placed). The arc ANGLE is
        /// already baked into <see cref="RawPos"/>, so a replay reproduces the slot direction for
        /// free; what this field carries is the FACT of it, which the replay still needs for two
        /// decisions: the modal-vs-modal overlap resolve must stay switched off (the slot, not a
        /// box test, decides where an arc window stands), and the log line has to be able to name
        /// the slot the re-placed window is sitting in.
        /// </summary>
        public int ArcSlot;

        /// <summary>
        /// True when the MAP ROOM'S CONE decided this placement — which is NOT the same question as
        /// "does it hold a reservation" (<see cref="ArcSlot"/> ≥ 0). A ninth window in a room whose
        /// registry is full is placed by the cone and holds nothing, and it must still be treated as
        /// cone-governed on the replay: it must keep the box test switched off (the reservation, not
        /// a measurement, is what deconflicts a map-room window) and it must still be pulled back
        /// into the cone by the hard clamp. Deriving that from ArcSlot ≥ 0 got it wrong for exactly
        /// that window — the one case where being wrong means a window the player cannot see, i.e.
        /// the reported bug.
        /// </summary>
        public bool ArcGoverned;

        /// <summary>The claimed azimuth, degrees from <see cref="HeadForward"/>, + = right. Stored
        /// so the replay can re-apply the hard cone clamp against the SAME angle the spawn claimed
        /// rather than re-deriving it from a registry that may have been narrowed since.</summary>
        public float ArcYawDeg;

        /// <summary><see cref="HeadPos"/>'s HEIGHT was substituted at spawn because the head camera
        /// carried no tracked XR pose (see <see cref="HeadEyeHeight"/>). Carried so the replay's log
        /// line repeats the fact instead of presenting the replayed head as a fresh measurement.</summary>
        public bool HeadSubstituted;
    }

    /// <summary>
    /// The anchor the LAST <see cref="ComputeHmdPose"/> call used (spawn-time scratch — this path
    /// never runs per frame). Read by <see cref="TryConvertWindow"/> straight after
    /// <see cref="PlaceAtHmd"/> to park it on the window record; invalidated when the pose could
    /// not be computed, so a stale anchor can never be inherited by the next window.
    /// </summary>
    private static SpawnAnchor s_lastSpawnAnchor;

    /// <summary>
    /// HMD-anchored pose at reading distance (DialogSurface pattern); false if no head camera.
    /// <paramref name="staggerIndex"/> nudges the window right+down so stacked secondary windows
    /// overlap rather than coincide (item 2).
    ///
    /// UPRIGHT / YAW-ONLY (item 2): the window faces the player's YAW only — never the full gaze
    /// pitch. The player looks DOWN at the board, so facing the full HMD forward tilted every
    /// floated window backward ("spawned with a pitch angle"). Flattening the forward to the
    /// horizontal plane makes every window stand vertically upright like the settings panel /
    /// combat log, while still being placed along the gaze so it lands in the foreground.
    ///
    /// BOARD-SAFE SPAWN CLAMP (request B): the raw gaze-following position is then clamped by
    /// <see cref="ClampSpawnPose"/> — never below/inside the board plane, never down a steep
    /// gaze. (Until ModBuild 189 a clamped spawn ALSO got a small upward tilt so the raised panel
    /// faced the eyes; that is the pitch the user rejected — see the MaxSpawnTiltDeg tombstone at
    /// the top of this file. The final rotation now goes through <see cref="Upright"/> and is
    /// yaw-only, measured, on every call.) Event-gated ONLY:
    /// this method runs at spawn, presence-regain refloat and lost-menu recall — never per
    /// frame — so grabbed placements persist (the placement-healing regression rule). One
    /// diagnostic line per call states the clamp decision for hardware-log verification.
    ///
    /// <para>REPLAY (<paramref name="replay"/>, first-open pose fix 2026-08-02): with an anchor the
    /// method skips the live head read and re-runs the ENTIRE clamp chain from that spawn's stored
    /// gaze inputs — same raw pose, same head, same diorama scale — against the caller's (now
    /// final) <paramref name="halfSize"/>. See <see cref="SpawnAnchor"/> for why the head is
    /// replayed rather than re-read. Without an anchor the behavior is exactly as before.</para>
    /// </summary>
    private static bool ComputeHmdPose(out Vector3 pos, out Quaternion rot, out float scale,
        int staggerIndex = 0, Vector2 halfSize = default, ConvertedPanel? self = null,
        bool levelMessage = false, SpawnAnchor? replay = null)
    {
        Vector3 headPos, fwd;
        // The world yaw this spawn's gaze points along — the frame the half-circle allocator seats
        // windows in (ModBuild 234; see the header of ArcSeats.cs for why a world frame and not a
        // per-window gaze-relative one). Set on both branches below.
        float gazeYawDeg;
        // The map room's claimed angular reservation for this spawn (−1 = not arc-placed). See the
        // ArcSeats.cs header for the ruling and the seating rule.
        int arcSlot = -1;
        float arcYawDeg = 0f;
        string arcWhy = "";
        bool arcPlaced = false;   // holds a reservation → the reservation, not a box test, deconflicts it
        bool arcGoverned = false; // the map room's cone decided this placement
        // Did this spawn have to substitute the player's eye height because the head camera carried
        // no tracked pose? Stored on the anchor so a REPLAY reports it too — the replay uses the
        // corrected head by construction (it replays HeadPos), and a log line that did not say so
        // would look like a second, independent measurement agreeing with the first.
        bool headSubstituted = false;
        string headEyeNote = string.Empty;

        // ---- THE SHARED WINDOW ANCHOR, AND IT IS TRIED BEFORE ANYTHING ELSE (ModBuild 243) -------
        //
        // USER RULING (2026-08-24, verbatim): "'niemand entscheidet das' bei den blauen Fenstern ist
        // nicht was ich will. Multiplayer Fenster, also 'blaue' Fenster, sollen komplett 1:1
        // synchronisiert werden von Anfang an. D.h. dass ihre Position von Anfang an auch für alle
        // synchronisiert sein muss. Gut, verankere es am Tisch bzw. über dem Spielfeld innerhalb
        // eines Szenarios." — narrowed by him the same day to the SPAWN POSE ONLY: "'verankert am
        // Tisch' - ich meine nur die initiale Spawnposition - es soll weiterhin von jedem
        // verschiebbar sein wie du es bereits implementiert hast (voll synchronisiert)".
        //
        // A SHARED WINDOW RETURNS FROM HERE AND MEETS NONE OF THE CLAMPS BELOW, AND THAT IS THE
        // POINT. Everything from this line down is HEAD-RELATIVE by construction — the arc seat is
        // an angle off THIS player's gaze, ClampSpawnPose floors and caps against THIS player's eye
        // and board plane, ResolveSpawnOverlap swings against THIS client's open windows, and the
        // facing yaw points at THIS player's head. Every one of them would turn a pose that is 1:1
        // by construction into one that agrees with nobody, and would do it silently. The anchored
        // pose is a pure function of geometry every client already shares (see the SHARED WINDOW
        // ANCHOR block at the end of ArcSeats.cs for the frames, the identity-keyed home table, the
        // facing decision and why no wire field was needed), so it is returned verbatim.
        //
        // A LOCAL WINDOW IS UNTOUCHED, BIT FOR BIT. TrySharedWindowAnchor returns false for
        // SharedWindowKind.None and for a shared kind this client does not participate in, which is
        // every window this method has ever placed before this build, and the code below is
        // character-for-character what it was.
        // halfSize goes in from ModBuild 244: the anchor's height is a BOTTOM-EDGE clearance, so it
        // has to know how tall the window is. 243 assumed a half-height and put a 1.1 m window's
        // bottom edge 153 mm inside the table (multiplayer_fesnter_position.jpg).
        if (TrySharedWindowAnchor(self, replay.HasValue, halfSize, out Vector3 sharedPos,
                out Quaternion sharedRot, out float sharedScale, out string sharedLine))
        {
            pos = sharedPos;
            rot = sharedRot;
            scale = sharedScale;
            // The anchor is DERIVED, so a replay recomputes the identical pose and the stored anchor
            // only has to name the same inputs. ArcSlot −1 keeps this window out of the angular
            // registry: it is not seated by angle at all, and booking arc for it would let a LOCAL
            // window's search treat a fixed place in the room as a gaze-relative reservation.
            s_lastSpawnAnchor = new SpawnAnchor
            {
                Valid = true,
                HeadPos = pos,
                HeadForward = rot * Vector3.forward,
                RawPos = pos,
                Scale = scale,
                StaggerIndex = 0,
                LevelMessage = false,
                ArcSlot = -1,
                ArcGoverned = false,
                ArcYawDeg = 0f,
                HeadSubstituted = false,
            };
            VRLog.Info("WorldUI", sharedLine);
            return true;
        }

        if (replay.HasValue)
        {
            // Replay: the placement inputs are frozen, only the geometry changed.
            SpawnAnchor a = replay.Value;
            headPos = a.HeadPos;
            fwd = a.HeadForward;
            // Reference frame for the occupancy/audit text only: the replay re-uses the STORED
            // ArcYawDeg for the hard clamp (below) and never re-runs the seat search, so nothing
            // about this window's placement depends on this number.
            gazeYawDeg = WorldYawDeg(fwd);
            scale = a.Scale;
            staggerIndex = a.StaggerIndex;
            levelMessage = a.LevelMessage;
            pos = a.RawPos;
            // The arc angle is already inside RawPos; carry the FACT so the overlap resolve stays
            // off and the log can still name the reservation (see SpawnAnchor.ArcSlot).
            arcSlot = a.ArcSlot;
            arcPlaced = arcSlot >= 0;
            arcGoverned = a.ArcGoverned;
            arcYawDeg = a.ArcYawDeg;
            headSubstituted = a.HeadSubstituted;
            if (headSubstituted)
                headEyeNote = "HEAD HEIGHT WAS SUBSTITUTED AT SPAWN and this replay inherits that "
                              + "same corrected head by construction — it is not a second, "
                              + "independent measurement agreeing with the first";
            arcWhy = "replayed against the final fitted geometry — the ANGLE is unchanged";
            // AND THE RESERVATION IS RE-TAKEN ON WHAT THE WINDOW ACTUALLY DRAWS (ModBuild 234).
            // This call is the one moment the window's final geometry exists while it is still
            // render-hidden, and it is also the first moment its DRAWN extent can be measured at
            // all — at spawn the window sits behind the reveal gate and no graphic passes the
            // visibility test, so the claim it made there was sized from the host RECT. For most
            // windows the two agree and this does nothing. For the one where they do not (the party
            // roster: a 1988 px frame around a 328 px column at x −818) the difference is 88° of
            // booked arc against 14° of drawn content, and re-seating it here is what hands the
            // other 74° back to the room. A move at this instant is invisible by construction —
            // the same premise TickPoseRePlace itself is built on.
            if (arcPlaced && TryReseatArcClaimOnDrawnContent(self, arcSlot, halfSize, scale,
                    gazeYawDeg, out float reseatYaw, out int reseatRank, out float reseatPull,
                    out string reseatNote))
            {
                arcYawDeg = reseatYaw;
                staggerIndex = reseatRank;
                arcWhy = reseatNote;
                // Rebuild the raw pose exactly the way the spawn branch builds it, so every clamp
                // below sees the same shape of input it always has.
                pos = headPos + Quaternion.AngleAxis(arcYawDeg, Vector3.up)
                    * (fwd * (WindowDistanceMeters * scale));
                ApplyArcDepth(ref pos, headPos, fwd, arcYawDeg, scale, reseatPull);
            }
        }
        else
        {
            Camera? head = CanvasConversion.WorldCamera;
            if (head == null)
            {
                s_lastSpawnAnchor = default; // never let the next window inherit a stale anchor
                pos = default;
                rot = Quaternion.identity;
                scale = 1f;
                return false;
            }
            scale = PanelLayout.WorldScale;
            Transform h = head.transform;
            headPos = h.position;
            fwd = h.forward;
            gazeYawDeg = SpawnGazeYawDeg(fwd, h.up);
            // THE VERTICAL REFERENCE MUST COME FROM A HEAD, NOT FROM A CAMERA THAT HAPPENS TO BE
            // CALLED ONE. ModBuild 198's report — "Alle Fenster spawnen jetzt UNTER dem Tisch",
            // photograph Tischbeine.jpg — is this read: the map room converts its permanent windows
            // during the map scene's LOAD, and for those frames the head camera still sits at the
            // rig root with an untouched localPosition, which in the map rig IS the tracking floor.
            // Every clamp below measures from headPos, so a head on the floor floors the whole
            // placement; the eye cap then held the window 0.10 m above the FLOOR and the log line
            // said "capped near eye level" while meaning ankle level. Correct the height (only the
            // height — the horizontal position and the forward stay the camera's own) and SAY SO on
            // this spawn's clamp line, so the substitution can never be silent.
            headSubstituted = HeadEyeHeight.CorrectVerticalReference(head, ref headPos, out headEyeNote);
            // Placement follows the full gaze (so it lands where the player is looking, overlapping
            // the primary), with a small right+down stagger per stacked window. Level-message
            // windows (tutorial boxes/strips) float CLOSER for readability (user report 2026-08-02);
            // every other family keeps the shared reading distance.
            pos = headPos + fwd * ((levelMessage ? LevelMessageDistanceMeters : WindowDistanceMeters)
                                   * scale);
            // THE MAP ROOM ARRANGES, IT DOES NOT STACK (ModBuild 181). User: "Ich möchte das die
            // Fenster die zu beginn spawnen im Halbkreis um einen gespawned werden, so dass man
            // alle direkt perfekt im Überblick hat." The stagger below is a right+down nudge that
            // deliberately OVERLAPS a secondary onto its parent — correct at a scenario table where
            // one window answers another, and wrong in a room where several independent windows
            // (merchant, temple, quest log) stand open at once and all of them must be readable
            // without moving anything.
            //
            // THE ARC IS BACK IN THE SPAWN PATH, AND THIS TIME IT IS A RESERVATION. ModBuild 181
            // indexed the arc by "how many windows are already open", which put every fresh window
            // at the OUTSIDE of the set (181's own bug: "so weit neben mir, dass ich es zuerst
            // nicht bemerkt habe"). 183 answered that by moving the arc OUT of the spawn path into
            // a relayout that re-posed the whole set on every add/remove — which is the behaviour
            // the current ruling rejects outright:
            //
            //   "Die Fenster verändern ständig ihre Position wenn ein neues Fenster gespawned wird
            //    oder schließt. Das soll nicht sein - ohne explizite Bewegung vom User, sollen sie
            //    ihre Position nicht verändern. Spawne die Fenster so, das alle im Sichtfeld passen
            //    aber einmal gespawned sind sie fix."
            //
            // The index is now a CLAIM on a free ANGULAR INTERVAL, chosen nearest the centre, so a
            // new window lands as close to the gaze as the free space allows and NOTHING already
            // standing is touched — 181's defect and 183's defect are both answered, by the
            // registry in ModalFallback.4.Tick.cs.
            //
            // AND SINCE ModBuild 193 THE INTERVAL IS MEASURED, NOT ASSUMED: how wide the window is
            // (2·atan(halfSize.x / distance), both world units — halfSize arrives in world units
            // and so does WindowDistanceMeters × scale) and how wide the readable cone is (off the
            // headset's own projection matrix). ModBuild 192's fixed 0°, ±34°, ±68° put the fourth
            // window at 68°, off the side of the display, which is the report that answered:
            // "Neue Fenster spawnen irgendwo an der Seite wo man sie nicht sieht - sie sollen IM
            // SICHTFELD spawnen, möglichst so das sie nicht mit einem anderen Fenster überlappen,
            // aber IM SICHTFELD." In view is unconditional; not overlapping is "möglichst".
            //
            // AND THE ARC IS THE MEASURED FIELD OF VIEW, WHICH OVERRULES ModBuild 234's HALF CIRCLE.
            // 234 answered "es soll nicht alles auf einem Fleck spawnen sondern am Besten in einem
            // halbkreis innerhalb des sichtbereichs ausgerichtet" by taking the whole ±90° half
            // circle as the placement arc. It worked on its own terms — nine overlapping pairs in
            // his burst down to one — and it was rejected for what it cost: "Der Halbkreis gefällt
            // mir nicht so, da viele Fenster außerhalb des direkten Sichtfelds spawnen. Das ist die
            // wichtigste Regel: Im SIchtfeld! Prio zwei ist dann so wenig kollisionen wie möglich -
            // wenn das nicht vermeidbar ist dann sollte das neue Fenster näher heran vor dem
            // anderen Fenster spawnen, das es keine direkte Kollision gibt."
            //
            // SO THE ARC IS NOW THE HEADSET'S MEASURED BINOCULAR OVERLAP (ArcPlacementHalfDeg —
            // ±40.0° on his Quest 3, read off the stereo projection matrices and NOT a chosen
            // number), applied to each window's EDGES. Inside it a window is on screen in BOTH eyes
            // with the head still; outside it a window is monocular. The comfort cone (±32°) keeps
            // its own job and grades each seat. His five windows draw ~160° into 80° of arc, so
            // OVERLAP IS THE NORMAL CASE and is resolved by DEPTH: the arriving window steps one
            // ladder rung nearer, in front of whatever its footprint (drawn ∪ frame = the hit rect)
            // intersects. Angle is still the first lever — the free interval NEAREST THE GAZE wins
            // and the ladder is reached only when no free interval remains. The seat is held in
            // ABSOLUTE WORLD YAW; see the ArcSeats.cs header for why a gaze-relative registry
            // aliases two windows onto one spot after a head turn.
            //
            // THE SLOT IS STILL A YAW, AND THE ONLY OTHER TERM IS A DEPTH. The raw pose is the
            // ordinary gaze-following spawn above, ROTATED ABOUT WORLD UP by the claimed angle — so
            // every other property of the placement is untouched and keeps producing exactly what
            // it produced before: the same reading distance, the same downward gaze bias, and
            // therefore the same HEIGHT out of ClampSpawnPose (steep-gaze flatten + board-top floor
            // + eye cap). Rotating about world up cannot introduce pitch or roll, so the yaw-only
            // ruling (ModBuild 189) is untouched and the Upright guard below still reports 0.0/0.0.
            //
            // A window that had to OVERLAP (the cone was full) additionally gets pulled toward the
            // head along the FLATTENED forward so it draws in front of what it covers. That vector
            // has y = 0, so THE HEIGHT IS STILL BIT-FOR-BIT ModBuild 192's, and it is a direction,
            // not a rotation, so pitch and roll are still zero. ModBuild 192's right+down stagger
            // is deliberately GONE: down is where the control board is, and ClampSpawnPose's
            // board-top floor would have put two windows back on the same height.
            if (TryClaimArcSeat(self, levelMessage, halfSize, scale, gazeYawDeg, out arcSlot,
                    out arcYawDeg, out int arcOverlapRank, out float arcPullWorld, out arcWhy))
            {
                arcGoverned = true;
                arcPlaced = arcSlot >= 0;
                staggerIndex = arcOverlapRank;
                pos = headPos + Quaternion.AngleAxis(arcYawDeg, Vector3.up)
                    * (fwd * (WindowDistanceMeters * scale));
                // The depth term — the ONE ladder: positive pulls toward the head, one step per
                // depth level, so an arriving window that collides draws IN FRONT of what it
                // collides with (the user's rule 3). It goes through the one helper so the
                // azimuth-preserving, height-preserving construction is written once; see
                // ApplyArcDepth for why it moves along the window's own flattened radial.
                ApplyArcDepth(ref pos, headPos, fwd, arcYawDeg, scale, arcPullWorld);
            }
            else if (staggerIndex > 0)
            {
                float step = SecondaryStaggerMeters * scale;
                pos += h.right * (step * staggerIndex) - Vector3.up * (step * staggerIndex);
                // Item 3b: pull each stacked secondary window CLOSER to the head so it sits clearly
                // in the foreground of its parent (nearer → also draws in front among equal-order hosts).
                pos -= fwd * (SecondaryForegroundMeters * scale * staggerIndex);
            }
        }
        float distanceMeters = levelMessage ? LevelMessageDistanceMeters : WindowDistanceMeters;

        // Request B: never below/inside the board plane, never down a steep gaze (see
        // ClampSpawnPose — spawn/refloat/recall only, never per frame). Level-message
        // windows may follow the gaze deeper before the flatten engages.
        Vector3 rawPos = pos;
        // Publish the anchor BEFORE the clamps mutate `pos` — it stores the RAW pose by contract.
        s_lastSpawnAnchor = new SpawnAnchor
        {
            Valid = true,
            HeadPos = headPos,
            HeadForward = fwd,
            RawPos = rawPos,
            Scale = scale,
            StaggerIndex = staggerIndex,
            LevelMessage = levelMessage,
            ArcSlot = arcSlot,
            ArcGoverned = arcGoverned,
            ArcYawDeg = arcYawDeg,
            HeadSubstituted = headSubstituted,
        };
        float maxPitchDeg = levelMessage ? LevelMsgMaxSpawnPitchDeg : MaxSpawnPitchDeg;
        string? clampReason = ClampSpawnPose(headPos, fwd, ref pos, scale, halfSize, maxPitchDeg,
            out HeightDecision height);

        // User request A: never spawn INSIDE the control board or another open modal —
        // raise / swing laterally toward free space (spawn/refloat/recall only, never per
        // frame). Staggered secondaries deliberately overlap their parent window (item 2/3b),
        // so they only avoid the board.
        //
        // AND A CONE-PLACED WINDOW ONLY AVOIDS THE BOARD TOO — because in the map room the
        // RESERVATION is what deconflicts, and it does so by reservation rather than by
        // measurement. Letting the box test swing a map-room window up to ±30° would make its final
        // angle a function of the geometry of the windows already standing, i.e. exactly the
        // coupling this whole change removes: the same window would land somewhere else depending
        // on what else happened to be open, and neighbours DO overlap at reading distance whenever
        // the cone is full (a full-width window is ~45° across), so the swing would fire on nearly
        // every second spawn. ModBuild 183 had the same property for a different reason — the
        // relayout ran after the resolve and overwrote whatever it decided.
        //
        // THE GATE IS arcGoverned, NOT arcPlaced (ModBuild 193). They differ for exactly one
        // window: the one that opens when the registry is already full, which the cone still places
        // but which holds no reservation. Gating on arcPlaced let the box test swing THAT window up
        // to ±30° — off the side of the display, in a room that by definition already has eight
        // windows competing for the cone. That is the reported bug, in the one case where it is
        // least excusable.
        string? overlapNote = ResolveSpawnOverlap(headPos, ref pos, scale, halfSize, self,
            includeModals: !arcGoverned && staggerIndex == 0);

        // THE HARD CONE CLAMP — "IM SICHTFELD" IS UNCONDITIONAL, AND THIS IS WHERE THAT IS ENFORCED
        // RATHER THAN INTENDED. Everything above is a SOFT clamp optimising for board clearance:
        // ClampSpawnPose flattens a steep gaze and floors the window above the board top, and
        // ResolveSpawnOverlap may still swing a window laterally to clear the board. Any of them can
        // move the window off the azimuth the seat chose — and the seat is the only thing that
        // knows what else is standing in the room and where the measured field of view ends. So the
        // LAST word
        // belongs to the seat: the head→window direction is rotated about WORLD UP back to the claimed
        // azimuth, keeping the horizontal distance and the height (both already clamped) exactly as
        // they are. It is a yaw and nothing else, so it cannot introduce pitch or roll and the
        // Upright guard below still reports 0.0/0.0.
        //
        // WHY RESTORING THE CLAIMED ANGLE AND NOT MERELY CLAMPING TO THE CONE EDGE: two windows
        // clamped to the same edge would land on the SAME angle, which is the ±85° aliasing defect
        // this whole line of work exists to remove. Claimed angles are distinct by construction, so
        // restoring them cannot alias anything. This is the same shape of rule as the level-message
        // view cone below, applied to the family that actually reported the fault.
        string? mapConeNote = null;
        if (arcGoverned && !levelMessage)
        {
            Vector3 offset = pos - headPos;
            Vector3 flatOffset = new Vector3(offset.x, 0f, offset.z);
            Vector3 flatGaze = new Vector3(fwd.x, 0f, fwd.z);
            if (flatOffset.sqrMagnitude > 1e-6f && flatGaze.sqrMagnitude > 1e-6f)
            {
                float actual = Vector3.SignedAngle(flatGaze, flatOffset, Vector3.up);
                float drift = Mathf.DeltaAngle(arcYawDeg, actual);
                if (Mathf.Abs(drift) > MapConeDriftToleranceDeg)
                {
                    Vector3 corrected = Quaternion.AngleAxis(-drift, Vector3.up) * flatOffset;
                    pos = new Vector3(headPos.x + corrected.x, pos.y, headPos.z + corrected.z);
                    mapConeNote = $"a soft clamp had swung it to {actual:F0}° from the spawn gaze, "
                                  + $"{drift:F0}° off the {arcYawDeg:F0}° it reserved — rotated back "
                                  + "about world up (height and distance untouched) so it stays "
                                  + "inside the measured field of view, which is unconditional";
                }
            }
        }

        // ---- ModBuild 251 — THE MAP ROOM'S ONE BAR HEIGHT, AND IT IS THE LAST WORD ON Y ----------
        //
        // USER RULING (2026-08-24, with zu_tief.jpg): "Die Begegnung ist zu tief gespawned … das
        // darf nie passieren - die Höhe soll beim Spawn am Besten bei allen Fenster gleich sein
        // gemessen am Greifbalken!"
        //
        // WHY THIS BLOCK EXISTS AT ALL — the map room had TWO height rules and this is the one that
        // was not a rule. A SHARED window returns from TrySharedWindowAnchor at the top of this
        // method with a table-relative height; everything that reaches HERE was seated at the RAW
        // GAZE HEIGHT with only a floor under it, so its bar landed wherever the player's head
        // happened to be pitched. In the ModBuild 250 log that is 0.521 m above the table for
        // 'New Party display' and 0.677 m for 'Quest Log Manager' — against 0.092 m for all three
        // shared windows — and the SAME window spawns anywhere between 1.42 m and 2.11 m above the
        // tracking floor across that one session. Both halves of "bei allen Fenstern gleich" are
        // broken: between the families, and within this one.
        //
        // IT OVERRIDES, IT DOES NOT FLOOR. ClampSpawnPose's board-top floor and eye cap are a
        // MINIMUM ("never in the furniture, never overhead"); a common height is an EQUALITY, and a
        // floor cannot produce one. Their numbers are still computed and still printed on the
        // clamp line below, so the reason a window used to sit where it did stays readable.
        //
        // IT IS ONLY THE HEIGHT. X and Z are untouched, so the ModBuild 250 arc, the reservation
        // and the hard cone clamp above all keep the window exactly where they put it; a pure Y
        // write cannot change an azimuth. It runs AFTER the cone clamp for that reason — the clamp
        // preserves height and this preserves azimuth, so neither can undo the other.
        //
        // THE GATE IS arcGoverned, WHICH IS EXACTLY "the map room seated this window". It is false
        // outside the map room, false for a level message, and false for the HOVER CARD, whose pose
        // MapRoom.HoverCardPose rewrites every tick from the map icon it describes — a spawn height
        // written for that family would be overwritten the same frame and would look, in the log,
        // exactly like a rule that worked.
        string? barHeightNote = null;
        if (arcGoverned && !levelMessage && TryMapTableTopWorldY(out float tableTopY,
                out float tableScale))
        {
            // The SAME half-height floor the shared path applies, for the same reason: a panel
            // measured before its content fit can report a degenerate rect, and hanging a
            // zero-height window from the bar would put its centre ON the bar. The floor is a
            // shared constant so the two families cannot disagree about the degenerate case
            // either.
            float halfWinYm = Mathf.Max(halfSize.y / tableScale, SharedAnchorMinHalfHeightMeters);
            float barYm = ResolveMapRoomBarHeightMeters(halfWinYm, out string barRule);
            float targetY = tableTopY + (barYm + GrabBarDropMeters + halfWinYm) * tableScale;
            float wasY = pos.y;
            pos.y = targetY;
            barHeightNote = $"seated at the map room's one bar height — bar {barYm:F3} m above the "
                            + $"table top (world y {tableTopY:F2} wu at {tableScale:F2} wu/m), so "
                            + $"the centre goes to {targetY:F2} wu, moving it "
                            + $"{(targetY - wasY) / tableScale:+0.000;-0.000} m from the "
                            + $"{wasY:F2} wu the gaze clamps had left it at. {barRule}";
            LogMapRoomBarHeight(self != null ? PanelLogName(self) : "<panel>", "LOCAL", barYm,
                halfWinYm, barRule);
        }

        // LEVEL-MESSAGE VIEW-CONE (user requirement, torbogen report): the tutorial window must
        // ALWAYS spawn inside the CURRENT view. The soft clamps above optimize for board
        // clearance and can sum to a large angular offset from the gaze — the log showed boxes
        // raised from a 14–17°-down gaze pose to ABOVE eye level (TB_10: y 2.14 → 25.66), i.e.
        // 20–30° off the gaze center and outside the downward view. This final, HARD clamp
        // rotates the head→window direction back to ≤ LevelMsgMaxOffGazeDeg off the gaze
        // (distance preserved) — in-view beats board clearance for this small family. It also
        // guarantees "never behind/above the player": the pose can never leave the gaze cone.
        string? viewConeNote = null;
        if (levelMessage)
        {
            Vector3 off = pos - headPos;
            float offDist = off.magnitude;
            if (offDist > 1e-4f)
            {
                float offAngle = Vector3.Angle(fwd, off);
                if (offAngle > LevelMsgMaxOffGazeDeg)
                {
                    Vector3 dir = Vector3.RotateTowards(off / offDist, fwd,
                        (offAngle - LevelMsgMaxOffGazeDeg) * Mathf.Deg2Rad, 0f);
                    pos = headPos + dir * offDist;
                    viewConeNote = $"was {offAngle:F0}° off the gaze after the soft clamps — rotated " +
                                   $"back to {LevelMsgMaxOffGazeDeg:F0}° so it spawns inside the current view";
                }
            }
        }

        // Facing is YAW-ONLY (upright) and points the readable face AT THE HEAD — same
        // convention as PanelPlacement.Facing: flatten the vector FROM the head TO the placed
        // position (NOT the raw gaze forward). For a centred primary window the two coincide,
        // but a STAGGERED secondary (a confirmation dialog over the pause menu, offset right+
        // down) turned parallel to the gaze read as "not facing the player"; yawing to the
        // panel-to-head direction turns it to actually face them. Canvas front faces -forward,
        // so pointing +Z away from the head makes the panel face them. Applied ONCE at placement
        // (spawn / presence-regain refloat) — never per frame, so a later grab-rotation persists.
        Vector3 flat = pos - headPos;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-4f)
        {
            flat = fwd;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
        }
        rot = Quaternion.LookRotation(flat.normalized, Vector3.up);

        // YAW ONLY (user ruling ModBuild 189, see the MaxSpawnTiltDeg tombstone at the top of this
        // file). The request-B upward tilt used to be applied HERE, on exactly the condition that a
        // clamp had engaged. It is gone; what stands in its place is a MEASUREMENT, so the ruling is
        // enforced by the code rather than remembered by a reader: Upright strips any pitch/roll the
        // rotation carries and reports what it stripped, and the numbers go on the log line below
        // whether or not anything was wrong (the "print the baseline too" rule).
        float strippedPitch = 0f;
        float strippedRoll = 0f;
        rot = Upright(rot, ref strippedPitch, ref strippedRoll);

        // Request B diagnostic: ONE line per spawn/refloat/recall (this method is never called
        // per frame) stating the clamp decision — original pose → clamped pose, reason.
        //
        // THE HEIGHT DECISION IS PRINTED IN FULL AND IN BOTH UNITS, and that is a ModBuild 199
        // requirement rather than a nicety. The 198 line carried "boardPlaneY=0.00,
        // boardTopClear=0.30m+halfH55.72 (window-bottom floorY=115.16, eyeCap +0.10m)" beside a
        // result of −134.74: every term of the losing candidate, none of the winning one. It could
        // not be checked against its own output, so five spawns at ankle height passed review. What
        // follows prints the eye level, the board plane, the half-height, BOTH candidates, the
        // winner and the result — each as a world y AND as real metres above the player's own
        // tracking floor, which is the only frame in which "under the table" is a statement about
        // the room. Recomputing nothing: every number comes off the HeightDecision the clamp filled.
        bool haveFloor = HeadEyeHeight.TryTrackingFloorY(out float floorRefY);
        float unitScale = scale; // `scale` is an out parameter and cannot be captured by the local function
        string Wu(float y) => haveFloor && unitScale > 1e-4f
            ? $"{y:F2} wu ({(y - floorRefY) / unitScale:F2} m)"
            : $"{y:F2} wu";
        string heightBlock = height.Have
            ? " HEIGHT DECISION"
              + (haveFloor
                  ? $" (world units, and real metres above the tracking floor at y={floorRefY:F2}, "
                    + $"scale {scale:F2})"
                  : " (world units; no rig root, so no tracking floor to express metres against)")
              + $": raw gaze y {Wu(rawPos.y)}"
              + $" | y at the plane clamp {Wu(height.FromY)} (after any steep-gaze flatten)"
              + $" | eye level {Wu(height.EyeY)}"
              + $" | board plane {Wu(height.BoardY)}"
              + $" | half-height {height.HalfHeight:F2} wu"
              + $" | CANDIDATE board-top floor = board plane + {BoardTopClearanceMeters:F2} m × scale + "
              + $"half-height = {Wu(height.BoardTopFloorY)}"
              + $" | CANDIDATE eye cap = eye level + {MaxAboveEyeMeters:F2} m × scale = {Wu(height.EyeCapY)}"
              + $" | WINNER {(height.EyeCapWon ? "EYE CAP" : "BOARD-TOP FLOOR")} (the lower of the two) "
              + $"⇒ target {Wu(height.TargetY)}"
              + $" | {(height.Raised ? "RAISED to the target" : "NOT raised (the raw pose was already at or above it)")}"
              + $" ⇒ RESULT {Wu(pos.y)}."
            : $" HEIGHT DECISION: no board/table plane in reach, so no height clamp ran — the pose "
              + $"kept its raw height, RESULT {Wu(pos.y)}.";
        VRLog.Info("WorldUI", "MODAL SPAWN CLAMP" +
                              (replay.HasValue ? " (RE-PLACE at the FINAL fitted geometry)" : "") + ": pose " +
                              $"({rawPos.x:F2},{rawPos.y:F2},{rawPos.z:F2}) → " +
                              $"({pos.x:F2},{pos.y:F2},{pos.z:F2})" +
                              (clampReason == null && overlapNote == null && viewConeNote == null
                                  ? " — unchanged (above the board plane, gaze within limits, no overlap)."
                                  : $" — {clampReason ?? "no plane/gaze clamp"}.") +
                              // ModBuild 189: the spawn orientation is yaw-only by ruling, and this
                              // is the proof, printed on every spawn/refloat/re-place whether or not
                              // anything was stripped. A non-zero pitch/roll here means a NEW writer
                              // upstream of Upright started authoring one — the log names it before
                              // anybody has to photograph a sloping window again.
                              $" UPRIGHT: yaw {rot.eulerAngles.y:F1}°, pitch/roll stripped " +
                              $"{strippedPitch:F1}°/{strippedRoll:F1}° (yaw-only by user ruling)." +
                              (overlapNote == null
                                  ? " OVERLAP: none."
                                  : $" OVERLAP: {overlapNote}.") +
                              (viewConeNote == null
                                  ? ""
                                  : $" VIEW-CONE: {viewConeNote}.") +
                              // The map room's hard cone clamp. Silent when it did not fire, which
                              // is the normal case — a line that appears means a soft clamp tried
                              // to move a window off its reserved azimuth and was overruled.
                              (mapConeNote == null
                                  ? ""
                                  : $" MAP-CONE: {mapConeNote}.") +
                              // ModBuild 251. Silent outside the map room. When it prints, the
                              // HEIGHT DECISION below describes candidates that were computed and
                              // then OVERRIDDEN — they are kept because they still explain where
                              // the window used to land, and this clause is what says they lost.
                              (barHeightNote == null
                                  ? ""
                                  : $" MAP-ROOM BAR HEIGHT (this is the FINAL word on y, and the "
                                    + $"HEIGHT DECISION below is what it overrode): {barHeightNote}.") +
                              // The head this whole line measured from — stated whenever it was not
                              // the one the headset reported, and silent otherwise.
                              (headSubstituted ? $" {headEyeNote}." : "") +
                              heightBlock +
                              $" maxPitch {maxPitchDeg:F0}°, " +
                              $"dist={distanceMeters:F2}m{(levelMessage ? " (level-message)" : "")}, " +
                              $"scale={scale:F2}, stagger={staggerIndex}.");

        // THE SLOT LINE — HOW TO READ IT, AND WHAT EACH READING MEANS.
        //
        // "A WINDOW I COULD NOT SEE" IS ANSWERABLE OFF THIS ONE LINE, which is the whole reason it
        // carries the numbers it does. Read it left to right:
        //   * measured field of view ±F° — the PLACEMENT arc, and it is MEASURED, not chosen: this
        //     headset's binocular overlap half-angle off its own stereo projection matrices (±40.0°
        //     on his Quest 3). Applied to the window's EDGES, so no part of any window is ever put
        //     where he would have to TURN HIS HEAD to find it. It replaced ModBuild 234's ±90° half
        //     circle, which he rejected for exactly that reason.
        //   * width W° — how wide THIS window is in angle, measured from its own half-size and its
        //     own distance.
        //   * angle A° / world yaw — where it was seated, + = right of the SPAWN GAZE, and the same
        //     seat as an absolute world yaw. The packer reasons in the world number; the offset is
        //     what the player saw from where he was standing. Two windows with different offsets
        //     but the SAME world yaw is a head turn between spawns, which is the aliasing bug the
        //     world frame exists to prevent — it must never appear.
        //   * COMFORT BAND / IN VIEW — the reading cone's remaining job, graded against this
        //     headset's MEASURED comfortable cone and binocular overlap (the MAP ROOM ARC GEOMETRY
        //     line, once per session, says where both numbers came from). IN VIEW is legitimate: it
        //     means the space closer in was taken and this seat is in the outer fifth of the field,
        //     on screen in both eyes but read by turning the eyes. A third grade, "OUT OF THE FIELD
        //     OF VIEW", is a falsifier and must never print.
        //   * DEPTH LEVEL k — how many 0.04 m steps NEARER than the nominal reading distance this
        //     window hangs, one per rung of the single depth ladder. k ≥ 1 means its footprint
        //     (drawn ∪ frame = the hit rect) intersects something standing and it was deliberately
        //     put IN FRONT of it — the user's rule 3. It also means this window takes the ray where
        //     the two overlap, which is correct: it is the one he just opened.
        //   * overlaps N° with '…' — how far it intrudes on its worst neighbour. A non-zero number
        //     is EXPECTED now (the room asks for roughly twice the arc the eye covers); what must
        //     accompany it is a DEPTH LEVEL ≥ 1, not a free seat.
        //   * occupancy — how many seats are held and where they are, so the next window's
        //     choice can be replayed by hand from the log.
        //   * and the MAP ROOM ARC AUDIT line that follows it is the FALSIFIER: the same room
        //     measured LIVE off the windows' own transforms rather than off the registry.
        //
        // AND HOW IT ANSWERS "A WINDOW STILL MOVED". Exactly TWO of these lines per window per open
        // are legitimate, and they are distinguishable:
        //   1. the spawn itself — 'claimed reservation N';
        //   2. AT MOST one more carrying 'replayed against the final fitted geometry', the
        //      pre-reveal re-place (TickPoseRePlace), which happens while the window is still
        //      render-hidden and therefore cannot be seen as movement. Its companion line is
        //      'MODAL POSE RE-PLACE'. That line may also report the reservation being NARROWED —
        //      that changes no pose at all, only how much angle later windows see as taken.
        // A THIRD line for the same panel means something re-placed a VISIBLE window and is the
        // bug: the only remaining caller that can produce one is the presence-regain refloat
        // (RefloatOpenWindows — it prints nothing else, so a doff/don is the thing to ask about).
        // A window that visibly moves with NO second line at all was moved by something that does
        // not go through ComputeHmdPose: the player's own grab (look for 'grabbed - its pose is now
        // PLAYER-OWNED'), or a new writer that must be found and stopped. And a window that moves
        // WHENEVER ANOTHER WINDOW OPENS OR CLOSES, with no line of its own, is ModBuild 183's bug
        // returning — a per-set relayout has been re-introduced somewhere.
        if (arcGoverned)
        {
            CountArcClaims(out int claimsClean, out int claimsOverlapping);
            float logHalfAngle = arcSlot >= 0
                ? ArcClaimHalfWidthDeg(arcSlot)
                : HalfAngleDeg(halfSize.x, WindowDistanceMeters * scale);
            // BOTH FIGURES, ALWAYS, FOR EVERY WINDOW (ModBuild 234). The reservation is what the
            // window DRAWS; the frame is what its collider spans. Printing only one of them is how
            // a window came to book 88° for a 14° column for four builds without anybody seeing it.
            float logFrameHalf = arcSlot >= 0 ? ArcClaimFrameHalfWidthDeg(arcSlot) : logHalfAngle;
            float logDrawnOffset = arcSlot >= 0 ? ArcClaimDrawnOffsetDeg(arcSlot) : 0f;
            float logSeatOffset = arcYawDeg + logDrawnOffset;   // where the CONTENT sits
            float logReach = Mathf.Abs(logSeatOffset) + logHalfAngle;
            float logArcHalf = ArcPlacementHalfDeg();
            VRLog.Info("WorldUI", "MAP ROOM WINDOW SLOT: "
                                  + $"'{(self != null ? PanelLogName(self) : "<panel>")}' "
                                  + (arcSlot < 0
                                      ? "placed WITHOUT a reservation"
                                      : $"claimed reservation {arcSlot}")
                                  + $" at {arcYawDeg:F0}° from the spawn gaze (+ = right), world "
                                  + $"yaw {gazeYawDeg + arcYawDeg:F0}°, "
                                  + $"frame {logFrameHalf * 2f:F0}°, "
                                  + $"drawn {logHalfAngle * 2f:F0}° at offset {logDrawnOffset:F0}°"
                                  + (logFrameHalf - logHalfAngle > 2f
                                      ? $" (PHANTOM FRAME: {(logFrameHalf - logHalfAngle) * 2f:F0}° "
                                        + "of it is empty and was handed back to the arc)"
                                      : "")
                                  + $", so its content reaches to {logReach:F0}° — measured field "
                                  + $"of view ±{logArcHalf:F1}° ⇒ "
                                  + (logReach <= logArcHalf + 0.5f
                                      ? "IN THE FIELD OF VIEW"
                                      : $"*** OUTSIDE THE FIELD OF VIEW by "
                                        + $"{logReach - logArcHalf:F0}° — the window is wider than "
                                        + "the whole binocular overlap and is centred as far as it "
                                        + "can be; nothing here can fix that, it has to be narrower "
                                        + "or further away ***")
                                  + $". {arcWhy}. Occupancy now {claimsClean + claimsOverlapping}/"
                                  + $"{MaxWindowClaims} seats ({claimsClean} at depth level 0, "
                                  + $"{claimsOverlapping} one or more steps nearer): "
                                  + $"[{ArcSeatOccupancyText(gazeYawDeg)}]. Pose "
                                  + $"({pos.x:F2},{pos.y:F2},{pos.z:F2}) world units at "
                                  + $"{distanceMeters:F2} m × scale {scale:F2}, yaw {rot.eulerAngles.y:F1}°"
                                  + (staggerIndex > 0
                                      ? $", DEPTH LEVEL {staggerIndex} (that many "
                                        + "steps nearer than the nominal reading distance, so it "
                                        + "draws in front of what it collides with)"
                                      : ", depth level 0 (nominal reading distance)")
                                  + ". After the one pre-reveal re-place this window is never posed "
                                  + "again while it floats: opening or closing any other window "
                                  + "moves nothing (user ruling), and only the player's own grab "
                                  + "can move it.");
            // BOOKED DISTANCE vs DELIVERED DISTANCE, RECONCILED — and the registry corrected to the
            // one that is true. This is the only point in the whole path where both numbers exist:
            // TryClaimArcSeat computed every angle at WindowDistanceMeters × scale, and every clamp
            // above has now had its say, including two that move the window ALONG the gaze (the
            // steep-gaze pull ×0.85 and the board-top floor's raise, which lengthens or shortens the
            // hypotenuse). Nothing is moved here; only the booking a LATER window reads is fixed,
            // and the line prints the live overlap census so "no overlap" is a measurement.
            //
            // WHAT THE 2026-08-24 LOGS SAY ABOUT THIS, because the premise it was written against
            // turned out to be false and a corrected premise is worth recording. The carried-forward
            // note in Net/NetProtocol.cs says the steep-gaze pull "fires on EVERY map-room window";
            // it does not. In the second 2026-08-24 hardware log ALL 22 pitch-clamped placements are
            // 'UI Quest Preview Popup', the map room's HOVER CARD, which TryClaimArcSeat refuses a
            // seat to outright (IsHoverCardPanel) — so not one arc-claiming window is pitch-clamped
            // in that session, and every one of them is delivered within 2.6 % of what it booked.
            // The "90° of window into 80° of field" line that session prints was computed on two
            // windows whose clamp lines both read "unchanged … NOT raised": the oversubscription is
            // real at face value and is NOT inflated by this coupling. The line below exists so that
            // conclusion can be checked per placement instead of reconstructed from a 12 MB log.
            string deliveredNote = ArcSeatDeliveredNote(arcSlot, headPos, pos, scale,
                distanceMeters);
            if (deliveredNote.Length > 0)
                VRLog.Info("WorldUI", deliveredNote);
            // THE FALSIFIER, on the same event and never per frame: one live measurement of every
            // standing window's real angular interval. See LogArcOverlapAudit — a burst prints one
            // of these per window and the LAST one is the settled state of the room.
            LogArcOverlapAudit(headPos,
                replay.HasValue
                    ? "after the pre-reveal re-place"
                    : "after a spawn / refloat placement",
                self, pos);
        }

        // ANNOUNCE THE WRITE THAT IS ABOUT TO HAPPEN (ModBuild 197). This method computes a pose; it
        // is its CALLERS that write it — PlaceAtHmd (spawn), RefloatOpenWindows (presence regain)
        // and TickPoseRePlaceOne (the one pre-reveal re-place). Announcing HERE covers all three
        // from the one funnel they share, so none of them needs a line of its own and none of them
        // can be forgotten. On a window that is not yet revealed this is a no-op (the lock is not
        // armed); on a revealed one it is what distinguishes the sanctioned doff/don rescue from an
        // anonymous writer, which the lock refuses. See PanelPoseWatch.
        PanelPoseWatch.Announce(self, PanelPoseWatch.Writer.Placement,
            replay.HasValue
                ? "the one pre-reveal re-place at the final fitted geometry"
                : "a spawn / presence-regain refloat placement");
        return true;
    }

    /// <summary>True once the upright guard has already reported a non-yaw spawn rotation, so the
    /// Warn below is a ONE-TIME regression alarm and not a per-spawn log flood.</summary>
    private static bool _uprightWarned;

    /// <summary>A stripped pitch/roll below this is measurement noise (quaternion round-trip through
    /// LookRotation), not a writer authoring a tilt. Above it, something upstream regressed.</summary>
    private const float UprightNoiseDeg = 0.5f;

    /// <summary>
    /// THE YAW-ONLY GUARANTEE, ENFORCED (user ruling ModBuild 189 — see the MaxSpawnTiltDeg
    /// tombstone at the top of this file). Returns <paramref name="rot"/> with any pitch and roll
    /// removed, and reports through <paramref name="pitchDeg"/> / <paramref name="rollDeg"/> exactly
    /// how much it had to remove.
    ///
    /// <para>WHY A GUARD AND NOT JUST A DELETION. The 15° tilt was ONE of several things that write
    /// a floated window's rotation, and the reason the report took nine builds to become actionable
    /// is that nobody could say from the code which of them was responsible. Now they cannot
    /// disagree: <see cref="ComputeHmdPose"/> is the single rotation authority for spawn, presence-
    /// regain refloat and the final-geometry re-place, this is its last step, and the residual it
    /// strips is printed on the MODAL SPAWN CLAMP line every single time. A future writer that
    /// re-introduces a tilt is not "found by reading" — it is announced by its own log line, and
    /// once by a Warn naming what the player will see.</para>
    ///
    /// <para>DEGENERATE CASE: a rotation looking straight up or down has no yaw to keep in its
    /// forward vector, so the yaw is recovered from the panel's OWN up vector (which is horizontal
    /// exactly then). Both degenerate at once is impossible for a unit quaternion, but the world
    /// forward is there as the final fallback so this can never divide by zero.</para>
    /// </summary>
    private static Quaternion Upright(Quaternion rot, ref float pitchDeg, ref float rollDeg)
    {
        Vector3 flat = rot * Vector3.forward;
        flat.y = 0f;
        if (flat.sqrMagnitude < 1e-6f)
        {
            // Looking straight up/down: the face's own up vector carries the yaw instead.
            flat = -(rot * Vector3.up);
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f)
                flat = Vector3.forward;
        }

        Quaternion upright = Quaternion.LookRotation(flat.normalized, Vector3.up);
        // The residual is what `upright` does NOT contain: express rot in the upright frame and read
        // its x (pitch) and z (roll) back, wrapped to a signed ±180° so "2° up" does not print 358.
        Vector3 residual = (Quaternion.Inverse(upright) * rot).eulerAngles;
        pitchDeg = Mathf.DeltaAngle(0f, residual.x);
        rollDeg = Mathf.DeltaAngle(0f, residual.z);

        if (!_uprightWarned
            && (Mathf.Abs(pitchDeg) > UprightNoiseDeg || Mathf.Abs(rollDeg) > UprightNoiseDeg))
        {
            _uprightWarned = true;
            VRLog.Warn("WorldUI", "MODAL SPAWN: a floated window's spawn rotation arrived with "
                                  + $"pitch {pitchDeg:F1}° / roll {rollDeg:F1}° — some writer upstream "
                                  + "of the upright guard is authoring a tilt again. It was stripped, "
                                  + "so the window still stands upright and nothing is lost this "
                                  + "session; but the yaw-only ruling is now being maintained by this "
                                  + "guard alone instead of by the placement code, and the next writer "
                                  + "to bypass it will not be caught. Reported ONCE per session.");
        }
        return upright;
    }

    /// <summary>Upright with the measurement discarded — for the one caller that places a STORED
    /// rotation (the level-message chain pose, <c>ModalFallback.8.Convert.cs</c>) and has no clamp
    /// line of its own to print the residual on. The one-shot Warn above still fires.</summary>
    private static Quaternion Upright(Quaternion rot)
    {
        float pitch = 0f;
        float roll = 0f;
        return Upright(rot, ref pitch, ref roll);
    }

    /// <summary>
    /// Item 1 (size): derive the board-relative host shrink for a freshly converted window.
    /// The host renders at <c>widthPx × CanvasScaleMm × WorldScale × extraScale</c>; dividing by
    /// WorldScale (position carries it) gives the REAL width <c>widthPx × mpp × extraScale</c>,
    /// so <c>extraScale = ModalTargetWidthMeters / (widthPx × mpp)</c> lands the window at the
    /// board-sized target — independent of table zoom, exactly like the settings panel. Returned
    /// as a CAP on <see cref="WindowScaleFactor"/>: only windows wider than the board shrink;
    /// smaller dialogs keep 0.7. Floored so a huge window never collapses to nothing.
    /// </summary>
    private static float DeriveWindowScale(ConvertedPanel panel)
    {
        // ModBuild 189: the legibility dial multiplies BOTH terms — the small-dialog cap and the
        // board-relative target — because either can be the binding one. Multiplying only the target
        // would leave every window already narrower than the board (the party roster, the quest log,
        // the shop item card: all of them, i.e. exactly the ones measured worst) at the untouched
        // 0.7 cap and the dial would look broken to the one person who moved it.
        float legibility = WindowLegibilityLive();
        float cap = WindowScaleFactor * legibility;
        float widthPx = panel.HostRect != null ? panel.HostRect.rect.width : 0f;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        if (widthPx < 1f || metersPerPixel <= 0f)
            return cap;
        // ModBuild 202: the map room's permanent character screen sizes its host FROM ITS CONTENT —
        // the character column plus the widest sub-view — precisely so that every sub-view is drawn at
        // the same scale as the character images beside it (user ruling: "ich will dass das Sub-Menü
        // genau die Größe des gesamten Fensters hat und zu der Größe der linken Characterbilder passt,
        // so dass es als EIN Fenster wahrgenommen wird"). Re-negotiating that width against the board
        // target answers the wider host BY SHRINKING EVERYTHING IN IT: at 1988 px the board-relative
        // term returns 1.00/1.988 = 0.503 against today's 0.875, i.e. an exact match at 57.5 % of the
        // size he has already approved — the character images would shrink 42 % to meet the sub-menu
        // instead of the other way round, which is the opposite of what the width was widened for.
        // So this ONE window keeps the small-dialog cap and lets its own content decide its metres.
        // The cost is stated rather than mitigated: 1988 px x 0.875 mm = 1.74 m, about 72° at the
        // 1.20 m reading distance, permanently, on a window that is non-closable by user ruling.
        if (CanvasConversion.IsFixedSizeWindow(panel))
            return cap;
        float boardRelative = ModalTargetWidthMeters * legibility / (widthPx * metersPerPixel);
        return Mathf.Clamp(Mathf.Min(cap, boardRelative), MinWindowScaleFactor, cap);
    }

    // ---- THE LEGIBILITY DIAL (ModBuild 189) --------------------------------------------------

    /// <summary>
    /// Shipped default for <c>[WorldUI] WindowLegibility</c>. 1.25 puts the widest family (a
    /// 1920 px window, which the board-relative rule pins at <see cref="ModalTargetWidthMeters"/>)
    /// at 1.00 m instead of 0.80 m — ~45° across at the 1.2 m reading distance.
    ///
    /// <para>WHY EXACTLY 1.25, AND NOT MORE. The number is bounded from above by a ruling, not by
    /// taste: <see cref="ModalTargetWidthMeters"/> exists because the previous ~1.3 m full-screen
    /// slab read "too big" (see its doc). 1.3 m at 1.2 m is ~57° across; 1.25 lands at ~45°,
    /// clearly on the accepted side of that line, and 1.6 would land back exactly on the rejected
    /// one. It is bounded from below by the measurement: at 1.0 the hardware log's own PANEL
    /// SAMPLING lines read 1.44-2.6 authored px per rendered px for the windows he photographed.</para>
    ///
    /// <para>AND IT CANNOT REACH 1:1 ON ITS OWN, WHICH IS THE HONEST HALF. Rendered pixels are
    /// bought with SOLID ANGLE and nothing else — moving a window nearer at the same physical size
    /// is the same purchase, not a cheaper one. A 1920-authored-px window drawn at one rendered
    /// pixel per authored pixel on a 3072 px eye needs about 57° of view, i.e. precisely the size
    /// he rejected. So the dial's job is to let HIM place that trade, and the default's job is to
    /// take the free half of it without spending his ruling.</para>
    /// </summary>
    // NOT annotated `// => [WorldUI] WindowLegibility`: those annotations live only under
    // src/GloomhavenVR/Defaults/ (scripts/rebase-defaults.py joins on them), and this lane does not
    // own Defaults.WorldUI.cs. Until the line is moved there, a tuned WindowLegibility in a dropped
    // cfg will be reported as UNMAPPED by `rebase-defaults.py check` instead of being applied.
    internal const float DefaultWindowLegibility = GloomhavenVR.Defaults.WindowLegibility;

    /// <summary>Dial floor: 1.0 reproduces the pre-dial geometry EXACTLY, so "off" is the size the
    /// user already approved and nothing about the flow depends on the dial being raised. Shrinking
    /// below it is deliberately not offered here — the two-hand resize already shrinks any single
    /// window (<see cref="PanelGrabHandle.MinScale"/>), and a global setting whose low end makes a
    /// measured legibility defect worse is not a comfort option.</summary>
    private const float MinWindowLegibility = 1.0f;

    /// <summary>Dial ceiling. 1.65 is where a 1920 px window reaches ~1:1 sampling AND where it is
    /// back at the ~1.3 m width that read "too big"; 1.75 is a little past both, on purpose, so the
    /// choice is genuinely his and the clamp is not secretly the answer.</summary>
    private const float MaxWindowLegibility = 1.75f;

    private static ConfigEntry<float>? _windowLegibility;
    private static float _loggedWindowLegibility = float.NaN;

    /// <summary>
    /// Bind-once for this file's own <c>[WorldUI]</c> entry. Late binder on
    /// <see cref="WorldUIConfig.FileHandle"/>, the same arrangement
    /// <see cref="FlatScreenStereo.BindConfig"/> uses and for the same reason: the entry belongs to
    /// the world-UI config file but is owned by the code that reads it. Force-bound by
    /// <c>ConfigCatalog.EnsureBound</c> so the in-VR browser shows it even in a session where no
    /// window has floated yet. Pure — binding touches no scene object.
    /// </summary>
    internal static void BindWindowConfig()
    {
        if (_windowLegibility != null)
            return;
        _windowLegibility = WorldUIConfig.FileHandle.Bind("WorldUI", "WindowLegibility",
            DefaultWindowLegibility,
            new ConfigDescription(
                "How large floated windows (menus, story/event boxes, the quest log, the merchant "
                + "and character screens) are drawn, as a factor of their shipped size. This is a "
                + "LEGIBILITY dial, not a taste dial: a floated window is game UI authored at "
                + "1920x1080 pixels, and how many of your headset's pixels each of those authored "
                + "pixels gets is decided purely by how much of your view the window covers. At the "
                + "shipped size the hardware measurement (PANEL SAMPLING in the log) reads 1.4-2.6 "
                + "authored pixels crammed into one rendered pixel, which is what makes thin strokes "
                + "and small glyphs drop in and out as your head moves ('Flackern') and what puts "
                + "the moire cross-hatch on portrait art. 1.0 = exactly the pre-dial size; 1.25 "
                + "(default) makes a full-width window ~1.0 m across at reading distance instead of "
                + "0.80 m; ~1.65 is where such a window reaches one rendered pixel per authored "
                + "pixel — and also where it covers ~57 degrees of your view, which is large. "
                + "Bigger windows are more legible and more intrusive; there is no setting that is "
                + "both. Read live at every spawn and re-fit, so open a window again to see a "
                + "change. Your two-hand resize still rides on top of this and still wins.",
                new AcceptableValueRange<float>(MinWindowLegibility, MaxWindowLegibility)));
    }

    /// <summary>
    /// The dial's live value, clamped, with a ONE line per change (never per read — this runs from
    /// every scale derivation). Falls back to the shipped default if the bind has not happened yet,
    /// which is a real case: a window can convert before the config browser ever force-binds.
    /// </summary>
    private static float WindowLegibilityLive()
    {
        if (_windowLegibility == null)
        {
            try
            {
                BindWindowConfig();
            }
            catch (System.Exception ex)
            {
                // Reflection/lookup-failure house rule: ONE Warn naming the consequence, stand down.
                if (float.IsNaN(_loggedWindowLegibility))
                {
                    _loggedWindowLegibility = DefaultWindowLegibility;
                    VRLog.Warn("WorldUI", $"[WorldUI] WindowLegibility could not be bound "
                                          + $"({ex.GetType().Name}) — floated windows use the shipped "
                                          + $"{DefaultWindowLegibility:0.00}x and the dial has no "
                                          + "effect this session. Nothing else changes.");
                }
                return DefaultWindowLegibility;
            }
        }
        float v = Mathf.Clamp(_windowLegibility?.Value ?? DefaultWindowLegibility,
            MinWindowLegibility, MaxWindowLegibility);
        if (float.IsNaN(_loggedWindowLegibility) || Mathf.Abs(v - _loggedWindowLegibility) > 0.001f)
        {
            _loggedWindowLegibility = v;
            VRLog.Info("WorldUI", $"MODAL WINDOW SIZE: legibility {v:0.00}x (default "
                                  + $"{DefaultWindowLegibility:0.00}x, range {MinWindowLegibility:0.00}-"
                                  + $"{MaxWindowLegibility:0.00}) — a full-width 1920 px window now "
                                  + $"targets {ModalTargetWidthMeters * v:0.00} m across at the "
                                  + $"{WindowDistanceMeters:0.0} m reading distance, i.e. "
                                  + $"{2f * Mathf.Atan2(ModalTargetWidthMeters * v * 0.5f, WindowDistanceMeters) * Mathf.Rad2Deg:F0}° "
                                  + "of view; narrower windows keep their own width and take the same "
                                  + "factor. Applies to windows opened from now on.");
        }
        return v;
    }

    /// <summary>HMD-anchored placement at reading distance (DialogSurface pattern).
    /// <paramref name="levelMessage"/> selects the closer, view-cone-guaranteed
    /// level-message placement (tutorial boxes/strips). Returns false when no head pose was
    /// available (nothing was written, and <see cref="s_lastSpawnAnchor"/> is invalid).</summary>
    private static bool PlaceAtHmd(ConvertedPanel panel, float extraScale, int staggerIndex = 0,
        bool levelMessage = false)
    {
        // User request A: hand the panel's projected world size to the pose computation so
        // the spawn-time overlap resolution can box-test it against the control board and
        // the other open modals (self excluded — refloat/recall re-places an existing panel).
        Vector2 half = PanelWorldHalfSize(panel, PanelLayout.WorldScale * extraScale);
        if (!ComputeHmdPose(out Vector3 pos, out Quaternion rot, out float scale, staggerIndex,
                half, panel, levelMessage))
            return false;
        CanvasConversion.PlaceHost(panel, pos, rot, scale * extraScale);
        return true;
    }

    // ---- ONE-SHOT POSE RE-PLACE AT FINAL GEOMETRY (first-open pose bug, 2026-08-02) ----------
    //
    // THE BUG. A floated window is placed the instant it converts, i.e. BEFORE the two things that
    // decide how big it actually is have happened:
    //   * the content fit (CanvasConversion.SettleOneShotFit / SettlePreRevealFirstFit) shrinks the
    //     host rect from the captured window rect to the visible content — 1920x1080 → 259x294 px
    //     for the scenario ESC menu (hardware log ModBuild 17);
    //   * the board-relative scale re-derivation (ModalFallback.Tick step 5b) re-derives extraScale
    //     from that fitted width and pushes it to the grab (0.404 → 0.700 in the same log).
    // PlaceAtHmd feeds ComputeHmdPose the PRE-fit half-size, and EVERY placement clamp is a
    // function of it: the board-top floor is `boardY + 0.30 m × scale + half.y`, the eye-level cap,
    // the overlap box test, the level-message view cone. Measured on that log: half.y 8.36 world
    // units at placement vs 3.83 for the finished window, i.e. the board-top floor sits 4.5 wu
    // (~0.12 m) higher than the real window ever needed — and the overlap resolve box-tests a slab
    // roughly 4x too large against the control board, which can raise/swing it for nothing. How
    // much of that reaches the final pose depends on the raw gaze pose and the eye-level cap (in
    // that ESC-menu open the cap absorbed most of it and the residual was ~0.06 m); a taller raw
    // pose, a shorter window or a board overlap turns the same defect into a large one.
    //
    // WHY IT SURFACED WITH THE REVEAL GATE. Before the pre-reveal hide the window was visible early
    // and visibly corrected itself, so a wrong spawn pose was hidden inside the jump the user asked
    // us to remove. With the gate the first VISIBLE frame is the settled one — and it shows a pose
    // computed from geometry that no longer exists. (The gate did not CREATE the wrong pose: the
    // ModBuild-17 log shows the identical pre-fit clamp inputs on the second open too. What differs
    // between a cold first open and a warm re-open in that log is the FIT RESULT itself —
    // 259x294 px vs 396x1080 px — which is a separate, still-open question about the ESC menu's
    // cold layout, not something this re-place can or should paper over.)
    //
    // THE FIX. While the window is still render-hidden behind the reveal gate, re-run the SAME
    // placement ONCE with the final rect + final extraScale (ComputeHmdPose replay, see
    // SpawnAnchor: same stored gaze inputs, so a window whose clamps do not change is not written
    // at all and keeps a byte-identical pose). Costs nothing visually — nothing of the window is
    // drawable at that moment.

    /// <summary>Re-place threshold, world units: a recomputed pose closer than this to the current
    /// one is NOT written. Mirrors <c>CanvasConversion.RevealPosEpsilon</c> (0.01 wu ≈ 0.3 mm real),
    /// i.e. exactly the movement the reveal gate itself would dismiss as jitter — so "no change"
    /// means the already-correct cases keep their spawn pose untouched AND their stillness
    /// counter unreset.</summary>
    private const float RePlacePosEpsilon = 0.01f;

    /// <summary>Re-place threshold, degrees (mirrors <c>CanvasConversion.RevealRotEpsilonDeg</c>).</summary>
    private const float RePlaceRotEpsilonDeg = 0.25f;

    /// <summary>
    /// ONE re-place per floated window per open, evaluated from <see cref="Tick"/> (step 5b-pose,
    /// straight after the scale re-derivation). See the block comment above for the bug.
    ///
    /// <para>ORDERING — the re-place must happen BEFORE the reveal, never after. Two guarantees:
    /// (1) <c>ModalFallback.Tick</c> runs EARLIER in the WorldUI module's Update than
    /// <c>CanvasConversion.Tick</c>, which is where the reveal gate lives, so a re-place decided
    /// this frame is applied before the gate is even evaluated this frame; (2) the panel must
    /// still be <see cref="ConvertedPanel.RevealPending"/> — a window already popped in by the
    /// 0.6 s deadline path is left alone forever, because moving a VISIBLE window is precisely the
    /// jump this whole mechanism exists to remove. That trade is deliberate: the deadline path is
    /// the "never stay invisible" escape hatch and keeps its historic pose.</para>
    ///
    /// <para>EXCLUSIONS, each latching so the window is evaluated exactly once (no loop, and the
    /// reveal gate's stillness counter can be reset at most one single time):
    /// <list type="bullet">
    /// <item>NO SPAWN ANCHOR — above all the level-message rule-2 branch of
    /// <see cref="TryConvertWindow"/>, which restores the verbatim stored chain pose. That pose is
    /// authoritative by user ruling (the player approved that exact spot); it is not a gaze
    /// placement and must never be recomputed.</item>
    /// <item>GRABBED — the player's grab always wins over any mod placement. (While render-hidden a
    /// grab is impossible by construction — <c>GrabbableModal.GrabVisible</c> is false — so this is
    /// belt-and-braces against future ordering changes.)</item>
    /// <item>REVEAL ALREADY OPEN — see the ordering paragraph.</item>
    /// </list></para>
    ///
    /// <para>GEOMETRY-FINAL criterion (mirrors the reveal gate's own): the content fit is committed
    /// (or the host has no fit at all — the Options family floats at its captured rect) AND, for a
    /// one-shot-fitted window, the 5b scale re-derivation has run. Until then the window is simply
    /// left pending; the reveal deadline bounds the wait.</para>
    /// </summary>
    private static void TickPoseRePlace()
    {
        // THE POSE LOCK'S TICK (ModBuild 197). Runs here and nowhere else, for one reason that has
        // to hold: this method is called from ModalFallback.Tick step 5b-pose, i.e. AFTER step 5's
        // grab follow (Grab.Tick copies the mod-owned frame onto the game-owned host) and after the
        // hover-card pose pass. Sampling any earlier would read a pose that is still one writer
        // short of the one the player is about to be shown. See PanelPoseWatch for the ruling, the
        // three verdict lines and why the GRAB FRAME rather than the host is the subject.
        PanelPoseWatch.BeginTick();
        // The story window is the ONE window a remote player may legitimately move (wire record 19,
        // Net.RemoteStorySync mirrors their grab onto its frame). Resolved once per tick through the
        // same lookup the sync itself uses, so the two cannot disagree about which window that is.
        // ...AND SINCE ModBuild 222 THAT IS NO LONGER JUST THE STORY WINDOW. The map room adds two
        // more windows whose pose a peer may legitimately move (the map story box and the quest
        // popup — see WorldUI/SharedWindows), so the exemption is asked of the shared-window
        // predicate rather than of one hard-coded grab. Without this, PanelPoseWatch treats every
        // remote pose on those two as drift: it REFUSES the first eight (MaxCorrections = 8,
        // PanelPlacement.cs:202) and only then concedes — which on hardware reads exactly like
        // network jitter and would have cost a build round to diagnose.
        float watchScale = PanelLayout.WorldScale;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            PanelPoseWatch.Track(wp.Panel, wp.Grab,
                revealed: !wp.Panel.RevealPending,
                poseOwnedExternally: wp.HoverCard || wp.Panel.PoseOwnedExternally,
                peerOwned: wp.Grab != null && SharedWindows.IsShared(wp.Window),
                worldScale: watchScale);
            // Round 3 (first-open size bug): a one-shot VERIFY correction re-fits a committed rect
            // that turned out not to contain its own content. That advances the panel's applied-fit
            // generation, which RE-ARMS this latch so the placement is replayed against the
            // corrected half-size — otherwise the window would keep the spawn clamps computed from
            // the rejected rect. TickPoseRePlaceOne itself refuses to move an already REVEALED
            // window, so a late correction can never yank a visible window around.
            if (wp.PoseRePlaceDone && wp.PoseRePlacedAtFit != wp.Panel.FitAppliedGeneration)
                wp.PoseRePlaceDone = false;
            // ModBuild 189, BEFORE the re-place so the clamps below see the corrected geometry in
            // the SAME tick: give every content-fitted window the scale its own rule asks for, not
            // only the one-shot-fitted ones step 5b covers. See TickWindowScaleRefit.
            TickWindowScaleRefit(wp);
            TickPoseRePlaceOne(wp.Panel, wp.Grab, wp.Window, wp.ExtraScale,
                wp.OneShotFitted && wp.ScaleReDerivedAtFit != wp.Panel.FitAppliedGeneration,
                ref wp.SpawnAnchor, ref wp.PoseRePlaceDone);
            if (wp.PoseRePlaceDone)
                wp.PoseRePlacedAtFit = wp.Panel.FitAppliedGeneration;
        }
        // Part 10: the GlobalErrorMessage float is NOT a UIWindow and therefore has no WindowPanel
        // record — it carries its own anchor/latch pair so the identical re-place applies to it.
        if (_errorPanel != null)
        {
            PanelPoseWatch.Track(_errorPanel, _errorGrab,
                revealed: !_errorPanel.RevealPending,
                poseOwnedExternally: _errorPanel.PoseOwnedExternally,
                peerOwned: false, worldScale: watchScale);
            TickPoseRePlaceOne(_errorPanel, _errorGrab, null, _errorExtraScale,
                scalePending: false, ref _errorSpawnAnchor, ref _errorPoseRePlaceDone);
        }
        // Retire (and print the ONE verdict line for) every window that stopped floating this tick.
        PanelPoseWatch.EndTick();
    }

    /// <summary>
    /// THE HALF OF THE SCALE RE-DERIVATION THAT WAS NEVER WIRED (ModBuild 189) — and the single
    /// biggest term in the reported "Flackern", measured, from his own hardware log.
    ///
    /// <para>WHAT THE LOG SAYS. <c>DeriveWindowScale</c> turns a window's WIDTH into its world size,
    /// so it has to run on the width the window actually ends up with. It runs at CONVERT time, on
    /// the captured pre-fit rect — for most windows the full 1920x1080 game canvas. Step 5b in
    /// <c>ModalFallback.4.Tick.cs</c> re-runs it after the content fit, but only for
    /// <c>OneShotFitted</c> windows (the ESC menu and the pause/options confirmations). Every OTHER
    /// floated window keeps the scale derived from 1920 px forever, however narrow its fitted rect
    /// turns out to be. From <c>.planning/debug/LogOutput.log</c>, one window, three lines:</para>
    /// <code>
    ///   Host rect fit '…New Party display': 1920x1080 → 328x1080 px
    ///   MODAL DIAG '…New Party display' (age 101.0s): … scale=0.083 rect=328x1080
    ///   PANEL SAMPLING 'New Party display': host 328x1080 … = panel scale 2.6-2.8
    /// </code>
    /// <para>0.083 is <c>0.001 m/px (CanvasScaleMm 1) × 198.6 world-scale × 0.417</c>, and 0.417 is
    /// <c>ModalTargetWidthMeters / 1.92 m</c> — the scale for a 1920 px window, still in force on a
    /// 328 px one. Its own rule asks for 0.700 there (328 px is far narrower than the board, so the
    /// board-relative cap should never have engaged at all). The window is therefore drawn 0.137 m
    /// wide where the shipped rule says 0.230 m: a 1.68x loss of rendered pixels per authored pixel,
    /// on the family he photographed, for nothing. That is not a trade-off anybody chose and it is
    /// not intrusiveness anybody bought — it is a stale intermediate.</para>
    ///
    /// <para>WHY IT IS SAFE TO WIDEN THE GATE. This is the SAME call step 5b makes, keyed on the
    /// SAME <see cref="ConvertedPanel.FitAppliedGeneration"/>, and it runs immediately after 5b in
    /// the same tick — so a one-shot window has already been handled and this sees the latch closed
    /// and does nothing. For everyone else it fires once per APPLIED fit, and a window whose fitted
    /// WIDTH did not change (the quest-preview popup grows only in height) derives the identical
    /// number and is not written or logged at all. The user's own two-hand resize is untouched by
    /// construction: <c>GrabbableModal</c> multiplies the grab factor ONTO this base
    /// (<c>SyncHostToFrame</c>), so a window the player shrank to 0.5x stays at 0.5x of the
    /// corrected size.</para>
    ///
    /// <para>WHAT IT DELIBERATELY DOES NOT DO: move the window. A late fit (this one landed at age
    /// 101 s, long after the reveal) resizes the panel about its own centre and leaves the pose
    /// alone — <see cref="TickPoseRePlaceOne"/> refuses to move a window that is already visible,
    /// which is the standing ruling, and re-running the board clamps here would break it. A window
    /// that grows late can therefore end up reaching lower toward the table than its spawn clamp
    /// intended — and it CAN grow: the same log shows this window's rect going 328x1080 → 1920x1080
    /// again when its nested character display expands, which correctly takes the scale back down.
    /// That is the accepted cost of never yanking a visible window (the ModBuild 149 ruling), and
    /// the escape from a window that ends up badly placed is the one it has always been: grab it,
    /// or close it with the X / the escape chord.</para>
    /// </summary>
    private static void TickWindowScaleRefit(WindowPanel wp)
    {
        if (wp.Grab == null || !wp.Panel.IsAlive)
            return;
        // STRICTLY ADDITIVE, and this line is what makes it so: step 5b owns the one-shot family
        // (ESC menu, pause/options confirmations) COMPLETELY, including the case where its own
        // `FitOneShotApplied` guard holds it back because the fit landed inside the 2 % no-op
        // tolerance. Re-deriving those here would also clear the `scalePending` flag
        // TickPoseRePlaceOne waits on, i.e. it would change WHEN a window whose scale this lane
        // never touched gets re-placed. This lane exists for the families that had no
        // re-derivation at all; it does not get to reinterpret the one that did.
        if (wp.OneShotFitted)
            return;
        if (wp.ScaleReDerivedAtFit == wp.Panel.FitAppliedGeneration)
            return; // already derived for this exact fit generation (a previous tick's)
        // A window whose fit has never produced a measurement has nothing to re-derive FROM: its
        // rect is still the captured one, which is what convert already used.
        if (!wp.Panel.FitEnabled || !wp.Panel.FitMeasuredOnce)
            return;

        float before = wp.ExtraScale;
        float refit = DeriveWindowScale(wp.Panel);
        wp.ScaleReDerivedAtFit = wp.Panel.FitAppliedGeneration;
        if (Mathf.Abs(refit - before) <= 0.001f)
            return; // identical (the usual case: the fit changed the height, not the width)

        wp.ExtraScale = refit;
        wp.Grab.SetExtraScale(refit);
        Rect rect = wp.Panel.HostRect != null ? wp.Panel.HostRect.rect : default;
        float mpp = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        // ARM THE SIZE-CHANGE ASSERTION (ModBuild 197, user report: "die POSITION springt wenn neue
        // Overlays aufgehen"). This lane changes how BIG the window is drawn, never where it stands,
        // and the mechanism that guarantees it is structural rather than intentional: the host's
        // RectTransform pivot is (0.5,0.5) (CanvasConversion.Convert), the content fit converges the
        // measured content centre to the host's own origin (ApplyFitConverging), and
        // GrabbableModal.SyncHostToFrame writes `host.position = frame.position` UNCONDITIONALLY
        // every Update and every LateUpdate — so a localScale or sizeDelta change can only ever grow
        // the window about its own centre. Structural is not the same as proven, and the character
        // window's rect swings between 328 and 1920 px every time a sub-view opens, so the next
        // sample MEASURES it: if the pose moves in the frame after this write, the lock says so in
        // millimetres and names this lane. Silent when it does not, which is the expected case.
        PanelPoseWatch.NoteSizeChange(wp.Panel,
            $"the board-relative scale re-derivation, extraScale {before:F3} → {refit:F3} at rect "
            + $"{rect.width:F0}x{rect.height:F0} px");
        VRLog.Info("WorldUI", "MODAL WINDOW SIZE: "
                              + $"'{(wp.Window != null ? wp.Window.name : "<menu>")}' re-scaled to its "
                              + $"FITTED rect {rect.width:F0}x{rect.height:F0} px (extraScale "
                              + $"{before:F3} → {refit:F3}) — real size "
                              + $"{rect.width * mpp * before:F2}x{rect.height * mpp * before:F2} m → "
                              + $"{rect.width * mpp * refit:F2}x{rect.height * mpp * refit:F2} m, i.e. "
                              + $"{refit / Mathf.Max(before, 1e-4f):F2}x the rendered pixels per "
                              + "authored pixel. The convert-time scale was derived from the PRE-fit "
                              + "rect; without this the window keeps a full-screen window's shrink on "
                              + "a narrow one and is drawn well below its authored resolution.");
    }

    /// <summary>
    /// One floated panel's single re-place evaluation — see <see cref="TickPoseRePlace"/> for the
    /// bug, the ordering guarantee and the exclusions. <paramref name="anchor"/> and
    /// <paramref name="done"/> are the caller's own state fields (WindowPanel for a floated window,
    /// the dedicated statics for the error box), passed by reference so both callers share ONE
    /// implementation. <paramref name="scalePending"/> is true while a one-shot-fitted window is
    /// still waiting for the step-5b board-relative scale re-derivation.
    /// </summary>
    private static void TickPoseRePlaceOne(ConvertedPanel panel, GrabbableModal? grab,
        UIWindow? window, float extraScale, bool scalePending, ref SpawnAnchor anchor, ref bool done)
    {
        if (done)
            return;
        if (panel == null || !panel.IsAlive || panel.HostGo == null)
        {
            done = true; // dead panel — nothing to place, never revisit
            return;
        }
        if (!anchor.Valid)
        {
            LatchPoseRePlace(panel, ref done, "kept verbatim (stored chain/user pose / no gaze " +
                                              "placement) — the stored pose is authoritative");
            return;
        }
        if (!panel.RevealPending)
        {
            // ROUTED THROUGH THE SHARED LOCK (ModBuild 197) rather than kept as a private guard
            // clause: the refusal is then logged ONCE per window naming this caller, in the same
            // POSE LOCK family every other refusal in the mod prints, and the verdict line at the
            // end of the float carries the totals. The behaviour is byte-for-byte what it was — a
            // revealed window is never re-placed — the difference is that the refusal is now
            // VISIBLE in a hardware log instead of being a silent early return.
            PanelPoseWatch.MayMove(panel, "ModalFallback.TickPoseRePlaceOne (the one pre-reveal "
                                          + "re-place, arriving after the reveal deadline fired)");
            LatchPoseRePlace(panel, ref done, "skipped — the window was already revealed " +
                                              "(deadline path); a re-place would be a VISIBLE jump");
            return;
        }
        if (grab != null && grab.IsGrabbed)
        {
            LatchPoseRePlace(panel, ref done, "skipped — the player is holding the window " +
                                              "(grab is authoritative)");
            return;
        }

        // Geometry final? Same criterion the reveal gate applies, so the two can never disagree
        // about what "final" means.
        bool fitDone = !panel.FitEnabled || panel.FitMeasuredOnce;
        if (!fitDone || scalePending)
            return; // still settling — retry next tick, bounded by the reveal deadline

        // A STORED WINDOW HANDOVER OUTRANKS THE ARC REPLAY, AND IT IS THE SAME ONE-SHOT RATHER THAN
        // A SECOND WRITER (ModBuild 242 — see the handover block at the end of ArcSeats.cs). When
        // one floated window has just been withdrawn and another was told to take its exact place,
        // replaying the arc seat here would put the arriving window straight back beside the window
        // it was supposed to replace: that is literally what his ModBuild 241 log records, the
        // Character-UI re-confirming world yaw 140° at :9100/:9101 fifty lines after the story
        // window was withdrawn at :9050. Consuming the handover here instead re-measures the ink
        // against the FINAL rect — the one moment the real geometry exists while the window is
        // still render-hidden — so the correction is invisible for the same reason this whole
        // method is.
        Vector3 handoverFrom = panel.HostGo.transform.position;
        if (TryConsumeHandover(panel, grab, out string handoverNote))
        {
            panel.PoseRePlaced = true;
            panel.PoseRePlacedFrom = handoverFrom;
            panel.PoseRePlacedTo = panel.HostGo.transform.position;
            panel.PoseRePlaceReason = handoverNote;
            done = true;
            return;
        }
        if (handoverNote.Length > 0)
        {
            // NEVER SILENTLY: a handover that could not be consumed leaves the window on the pose
            // written at the withdrawal edge and lets the arc replay below run over it, which looks
            // exactly like the fix not working.
            VRLog.Warn("WorldUI", $"MODAL POSE RE-PLACE: '{panel.HostGo.name}' — {handoverNote}. The "
                                  + "ordinary arc re-place runs from here, so this window may be "
                                  + "moved off the place it was handed.");
        }

        // Replay the spawn placement against the FINAL rect + FINAL extraScale. The grab carries
        // the scale (SetExtraScale, step 5b), so only position/rotation are re-derived here.
        Vector2 half = PanelWorldHalfSize(panel, anchor.Scale * extraScale);
        if (!ComputeHmdPose(out Vector3 pos, out Quaternion rot, out _, 0, half, panel, false, anchor))
        {
            LatchPoseRePlace(panel, ref done, "skipped — no head pose available");
            return;
        }

        Transform host = panel.HostGo.transform;
        Vector3 from = host.position;
        if ((pos - from).sqrMagnitude <= RePlacePosEpsilon * RePlacePosEpsilon
            && Quaternion.Angle(rot, host.rotation) <= RePlaceRotEpsilonDeg)
        {
            LatchPoseRePlace(panel, ref done, "recomputed from the final rect/scale and found " +
                                              "IDENTICAL to the spawn pose — nothing written");
            return;
        }

        // Move the mod-owned grab FRAME, not the host: the host follows the frame every tick
        // (GrabbableModal.Tick), so a direct host write would be snapped straight back — the
        // RefloatOpenWindows lesson. PlaceFrameAt syncs the host in the same call.
        if (grab != null)
            grab.PlaceFrameAt(pos, rot);
        else
            CanvasConversion.PlaceHost(panel, pos, rot, anchor.Scale * extraScale);

        // CHAIN CONTINUITY: a rule-1 level-message spawn seeded the shared chain store with its
        // ORIGINAL pose at convert time — refresh it, or the next scripted window of the chain
        // would inherit a spot this window no longer occupies. No-op for every other family.
        StoreChainPose(window, panel);

        panel.PoseRePlaced = true;
        panel.PoseRePlacedFrom = from;
        panel.PoseRePlacedTo = pos;
        panel.PoseRePlaceReason = "re-placed from the final fitted geometry";
        done = true;
        Rect rect = panel.HostRect != null ? panel.HostRect.rect : default;
        VRLog.Info("WorldUI", $"MODAL POSE RE-PLACE: '{panel.HostGo.name}' re-placed while still " +
                              $"render-hidden — pose ({from.x:F2},{from.y:F2},{from.z:F2}) → " +
                              $"({pos.x:F2},{pos.y:F2},{pos.z:F2}), moved " +
                              $"{(pos - from).magnitude / Mathf.Max(anchor.Scale, 1e-4f):F3} m. The spawn " +
                              "clamps had been computed from the PRE-fit rect; the final geometry is " +
                              $"{rect.width:F0}x{rect.height:F0} px at extraScale {extraScale:F3} " +
                              $"(half-height {half.y:F2} wu — compare the first MODAL SPAWN CLAMP line). " +
                              "Invisible at this moment, so the first VISIBLE frame already shows the " +
                              "pose derived from the window's real size.");
    }

    /// <summary>Close a panel's one-shot re-place evaluation without moving anything, recording
    /// <paramref name="reason"/> for the MODAL REVEAL line. Deliberately silent — the reveal line
    /// is the ONE place the decision is reported, so a window costs exactly one line either way.</summary>
    private static void LatchPoseRePlace(ConvertedPanel panel, ref bool done, string reason)
    {
        done = true;
        panel.PoseRePlaced = false;
        panel.PoseRePlaceReason = reason;
    }

    private static void ReleaseAllWindows(string reason)
    {
        // ModBuild 231: hand the parked quest picture back and unlock the guildmaster destinations
        // BEFORE any host is destroyed, for the reason spelled out at the StoryComposite.Tick call
        // site — a subtree parked under a destroyed host has nowhere to go home to, and a merchant
        // left grey across a scene change is a presentation bug that outlives its own gate.
        StoryComposite.Reset();
        // Every shared pose dies with the windows that carried it, so every spent shared anchor is
        // re-armed here (ModBuild 243) — see the SHARED WINDOW ANCHOR block at the end of
        // ArcSeats.cs. A latch that outlived its edge would refuse the anchor in the NEXT scenario
        // for a drag nobody there made ([[gate-outliving-its-edge]]).
        ResetSharedAnchors($"every floated window is being released: {reason}");
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            wp.Grab?.Destroy(); // sub-item B: drop the mod-owned grab holder before releasing the host
            CanvasConversion.Release(wp.Panel);
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released ({reason}) — restored to its 2D home.");
        }
        Converted.Clear();
        // ModBuild 230: and then the net, because a grab holder is a SCENE-ROOT tree that nothing
        // above can reach except through the WindowPanel that owned it. The loop above destroyed
        // every holder it could see; anything still registered after Converted is empty is by
        // definition owned by nobody, and on a module shutdown it would outlive the module itself.
        // Silent when there is nothing to do, which is every ordinary teardown.
        for (int i = GrabbableModal.LiveHolders.Count - 1; i >= 0; i--)
        {
            GrabbableModal stray = GrabbableModal.LiveHolders[i];
            VRLog.Warn("WorldUI", $"ORPHAN CHROME DESTROYED at teardown ({reason}): the grab holder "
                                  + $"'{stray.LogName}' was still registered after every floated window "
                                  + "had been released, so no WindowPanel owned it. It has been "
                                  + "destroyed. This sweep compares two live sets; it cannot observe "
                                  + "which path dropped the holder, and the name above is the window to "
                                  + "trace back through the MODAL WINDOW release lines.");
            stray.Destroy();
        }
        // Flush the pose lock in the same breath: a bulk release (scenario exit, VR off) may be the
        // last thing that ever happens to these windows, and the per-window POSE WATCH verdict must
        // not be lost just because ModalFallback.Tick is never called again. Mark-and-sweep with
        // nothing marked retires and reports every tracked window.
        PanelPoseWatch.BeginTick();
        PanelPoseWatch.EndTick();
    }

    /// <summary>
    /// THE MAP ROOM TAKES ITS WINDOWS WITH IT (ModBuild 226). Called once from
    /// <see cref="MapRoom.MapRoomDriver.StandDown"/>, after <c>MapTravelConfirm.Reset</c> has handed
    /// the game's travel container back and before the room's own furniture is released.
    ///
    /// <para>USER REPORT, verbatim: <i>"Als ich dann zu einem Szenario gejoint bin, habe ich dort
    /// zwei Fenster gesehen, die dort NICHT hingehören: Die Character-UI aus der Map-Umgebung und
    /// die Quest-Beschreibung. Beides Fenster aus der 3D-Map-Umgebung, die hier nicht auftauchen
    /// sollen."</i> (.planning/debug/unerwünschte_fenster.jpg.)</para>
    ///
    /// <para>ROOT CAUSE, FROM THE ModBuild 225 HARDWARE LOG AND NOT FROM READING THE CODE. The
    /// room's floats DID release at teardown — six of them, in one burst right after
    /// <c>MAP ROOM stood down</c> — but three of those release lines end in <c>open=True</c>:</para>
    /// <code>
    ///   MODAL WINDOW: 'UI Loadout Window'  released — restored to its 2D home (open=True,  …)
    ///   MODAL WINDOW: 'UI Quest Popup'     released — restored to its 2D home (open=True,  …)
    ///   MODAL WINDOW: 'New Party display'  released — restored to its 2D home (open=True,  …)
    /// </code>
    /// <para>i.e. the mod let go, but THE GAME STILL HAD THEM OPEN. <c>PartyPanel</c> and
    /// <c>QuestPopup</c> are both in <see cref="FallbackIds"/>, so the moment the scenario's rig came
    /// up and <c>convertWanted</c> went true again the enrolled path floated the very same two
    /// windows a second time — the log shows exactly that, two lines apart, with no
    /// <c>UIWindow SHOWN</c> in between because they had never been hidden:</para>
    /// <code>
    ///   MODAL WINDOW: 'New Party display' (ID PartyPanel) floated in front of the HMD …
    ///   MODAL WINDOW: 'UI Quest Popup' (ID QuestPopup)   floated in front of the HMD …
    /// </code>
    /// <para>They are the two windows in his screenshot. The teardown was never incomplete on the
    /// mod's side; it was incomplete on the GAME's side, and releasing a float restores the window
    /// to a 2D home it is still open in, which is an invitation to be re-adopted rather than an end.
    /// </para>
    ///
    /// <para>WHAT THIS DOES. Release every float the room was hosting, and — for the ones the game
    /// still reports OPEN — call the window's own <c>Hide()</c>, which is byte-for-byte what the mod
    /// X already does through <see cref="CloseFloatedWindow"/> and what the game's own map flow does
    /// when it leaves the HQ. Then say so, ALWAYS, including when it finds nothing: a sweep that only
    /// speaks on success hides that it never ran.</para>
    ///
    /// <para>WHY EVERY FLOAT AND NOT A NAMED LIST. While the room stands there is no scenario board
    /// (<c>MapRoomDriver.Active</c> and the scenario predicate are mutually exclusive by
    /// construction), so everything in <see cref="Converted"/> at this instant is a window the ROOM
    /// was hosting. A named list would be a second place to keep in step with
    /// <see cref="FallbackIds"/> and the catch-all, and this project has paid for those twice.</para>
    ///
    /// <para>MULTIPLAYER — AND THIS WAS CHECKED RATHER THAN ASSUMED. A shared window
    /// (<see cref="SharedWindows"/>) may still be driven by a peer who is in the room while this
    /// client is not, so the teardown must not fight that and a peer's record must not resurrect a
    /// window here. Neither can happen: <c>UIWindow.Hide()</c> is a local CanvasGroup tween with no
    /// wire surface, and BOTH remote map paths gate their apply on <c>MapRoomDriver.Active</c> —
    /// <c>Net/RemoteMapRoom.cs:214/244/347</c> and <c>Net/RemoteMapStory.cs:246/320/660</c> — which
    /// <see cref="MapRoom.MapRoomDriver.StandDown"/> sets false on its FIRST line, before this runs.
    /// The pose paths cannot resurrect anything either: they reach a window only through
    /// <c>SharedWindows.TryGetGrab</c>, which walks the very list this method empties.</para>
    /// </summary>
    internal static void ReleaseMapRoomFloats(string reason)
    {
        // Every shared window in this room is about to stop existing, so every pose that spent a
        // shared anchor is about to stop existing with it. A latch that outlived the room would
        // refuse the anchor on the next entry for a drag nobody in that session made — the same
        // failure shape as [[gate-outliving-its-edge]].
        ResetSharedAnchors($"the map room floats are being released: {reason}");
        int released = 0;
        int hidden = 0;
        string names = string.Empty;
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            UIWindow? window = wp.Window;
            string name = window != null ? window.name : "<destroyed>";
            bool wasOpen = window != null && window.IsOpen;
            names = names.Length == 0
                ? $"'{name}' (ID {(window != null ? window.ID.ToString() : "?")}, game open={wasOpen})"
                : names + $"; '{name}' (ID {(window != null ? window.ID.ToString() : "?")}, game open={wasOpen})";
            Converted.RemoveAt(i);
            wp.Grab?.Destroy();
            CanvasConversion.Release(wp.Panel);
            released++;
            // The game's own state is the thing that survives the room, so it is the thing that has
            // to be closed. Hide() and not Escape(): Escape runs the window's escape ACTION, which
            // for several of these windows opens or focuses something else, and this is a teardown,
            // not a user gesture.
            if (wasOpen)
            {
                window!.Hide();
                hidden++;
            }
        }
        VRLog.Info("WorldUI", $"MAP ROOM WINDOW SWEEP ({reason}): {released} floated window(s) released, "
                              + $"{hidden} of them also HIDDEN in the game because it still reported them "
                              + "open — those are the ones that would otherwise be re-adopted by the "
                              + "scenario's converter the moment the next rig comes up (the ModBuild 225 "
                              + "log shows 'New Party display' and 'UI Quest Popup' doing exactly that, "
                              + "which is the pair in unerwünschte_fenster.jpg). "
                              + (released == 0
                                  ? "NOTHING WAS FLOATED — this line is printed anyway, so a silent sweep "
                                    + "can never be mistaken for a sweep that did not run."
                                  : $"Windows: {names}."));
    }

    // ---- THE EMPTY-WINDOW INVARIANT (ModBuild 226) ---------------------------------------------
    //
    // USER RULING, verbatim: "Als mein Mitspieler gejoint ist, kam ein leeres Fenster auf - sowas
    // soll per se niemals passieren." (.planning/debug/leeres_fenster.jpg.)
    //
    // The CAUSE of the bars in that screenshot is fixed at its source — ModalFallback.8.Convert no
    // longer manufactures a GrabbableModal for a hover card, which is where the 204 orphan
    // "MODAL GRAB: 'Menu'" holders in the ModBuild 225 log came from. This is the INVARIANT that
    // holds whatever the cause: no panel with nothing drawn in it may be revealed. It exists
    // separately on purpose. The map room makes every window STICKY (MapRoomParallel), and this
    // class's own WindowPanel.WindowCanvas doc records the other way an empty shell is produced —
    // "a sticky menu the game single-window-toggled off … kept its float + CanvasGroup alpha but
    // rendered as an EMPTY shell — only the mod-drawn grab bar / X remained." One fix does not cover
    // the other, and a peer joining rebuilds several of these windows at once.
    //
    // WHAT COUNTS AS EMPTY, AND WHY IT IS NOT "NO VISIBLE GRAPHIC". The obvious test — count the
    // graphics the content fit calls visible — is WRONG here and the log proves it: the perfectly
    // real scenario story window's first fit commit reads "DRAWN CONTENT 1920x1080 px at (0,0) from
    // 0 visible graphic(s)". Alpha, culling and inherited CanvasGroup alpha all zero that count for
    // a window that is merely mid-fade, and this project has a documented history of instruments
    // that measure a subset of what the eye sees and then agree with every broken build. So the test
    // here is STRUCTURAL and deliberately conservative: are there any active Graphics with a
    // non-degenerate rect, or any enabled Renderers (the 3D character/enemy previews draw through
    // those and carry no Graphic at all), anywhere under the conversion target? A window that is
    // fading, tinted, masked or fully transparent still has all of them and is never touched. Only a
    // host with genuinely nothing under it is refused.

    private static readonly List<Graphic> EmptyCheckGraphics = new(64);
    private static readonly List<Renderer> EmptyCheckRenderers = new(16);

    /// <summary>Windows already refused once — so the Warn is one line per window per session rather
    /// than one per re-open. Cleared with the rest of the catch-all latches.</summary>
    private static readonly HashSet<string> EmptyFloatWarned = new();

    /// <summary>
    /// NON-BLOCKING windows refused for being empty. Consulted by <c>CatchAllEligible</c> so the
    /// window is not floated again on the very next tick, and PRUNED there the moment the game stops
    /// reporting it open — i.e. exactly the "retry after a close/re-open" rule <c>Failed</c> uses,
    /// without <c>Failed</c>'s second meaning (it is a term of <c>ScreenWanted</c>, and an empty
    /// non-blocking panel must not raise the flat screen because some unrelated blocker is up).
    /// </summary>
    private static readonly HashSet<UIWindow> EmptyRefused = new();

    /// <summary>Has this window been refused as empty and not yet closed? Read by the catch-all;
    /// the prune lives there too, next to the <c>Failed</c> prune it mirrors.</summary>
    private static bool EmptyRefusedNow(UIWindow window) => EmptyRefused.Contains(window);

    /// <summary>Forget a refusal — the window closed, so its next open is judged afresh.</summary>
    private static void ClearEmptyRefusal(UIWindow window) => EmptyRefused.Remove(window);

    /// <summary>
    /// Called from <c>CanvasConversion.CompleteReveal</c> on the reveal edge, once per float.
    /// Returns TRUE when this panel is a modal float whose host has no content at all and has
    /// therefore been RELEASED rather than shown; false for every other panel, including every
    /// surface this class does not own.
    /// </summary>
    internal static bool RefuseEmptyFloat(ConvertedPanel? panel)
    {
        if (panel == null || panel.HostGo == null)
            return false;
        WindowPanel? wp = null;
        int index = -1;
        for (int i = 0; i < Converted.Count; i++)
        {
            if (!ReferenceEquals(Converted[i].Panel, panel))
                continue;
            wp = Converted[i];
            index = i;
            break;
        }
        if (wp == null)
            return false; // not a modal float — surfaces reveal exactly as they always did
        Transform? root = panel.Target != null ? panel.Target : panel.HostGo.transform;
        if (root == null || HasAnyDrawnContent(root))
            return false;

        string name = wp.Window != null ? wp.Window.name : "<destroyed>";
        Converted.RemoveAt(index);
        wp.Grab?.Destroy();
        CanvasConversion.Release(wp.Panel);
        // RETRY ONLY AFTER A CLOSE/RE-OPEN — and through TWO different sets, because `Failed` carries
        // a second meaning that must not be borrowed by accident.
        //
        //   BLOCKING windows go into `Failed`. That set already means "retry only after a close/
        //   re-open" (ModalFallback.4.Tick step 2) AND it is one of the terms of `ScreenWanted`, so a
        //   blocking dialog that is empty raises the full flat screen instead of leaving the player
        //   with an unanswerable prompt. That is the DurabilityPanel rule and it is the right
        //   fail-safe: a wrongly-screened window is recoverable, a dropped blocker is a deadlock.
        //
        //   NON-BLOCKING windows — every case in his report — go into `EmptyRefused` instead, which
        //   the catch-all consults and which has NO screen term. Putting them in `Failed` would make
        //   one empty map-room panel raise the whole flat screen the moment any unrelated blocking
        //   window opened, which would be a far louder bug than the one being fixed.
        if (wp.Window != null)
        {
            if (IsBlockingWindow(wp.Window))
            {
                if (!ContainsWindow(Failed, wp.Window))
                    Failed.Add(wp.Window);
            }
            else
            {
                EmptyRefused.Add(wp.Window);
            }
        }
        if (EmptyFloatWarned.Add(name))
            VRLog.Warn("WorldUI", $"EMPTY WINDOW REFUSED: '{name}' (ID "
                                  + $"{(wp.Window != null ? wp.Window.ID.ToString() : "?")}, host "
                                  + $"'{panel.HostGo.name}') reached its reveal edge with NOTHING drawn "
                                  + "under it — no active Graphic with a non-degenerate rect and no "
                                  + "enabled Renderer anywhere in the conversion target's subtree. It has "
                                  + "been RELEASED, not merely hidden: the grab holder is destroyed, the "
                                  + "host is gone, the window is back in its 2D home and it will be "
                                  + "reconsidered only after it closes and re-opens. USER RULING (ModBuild "
                                  + "226): \"Als mein Mitspieler gejoint ist, kam ein leeres Fenster auf - "
                                  + "sowas soll per se niemals passieren.\" IF THIS LINE APPEARS, THE NAME "
                                  + "IN IT IS THE ANSWER TO 'which one was it' — that question cost a "
                                  + "round because the bars in leeres_fenster.jpg carried no identity at "
                                  + "all. The test is structural (presence, not visibility) so a window "
                                  + "that is merely faded, masked or fully transparent is NEVER refused.");
        return true;
    }

    /// <summary>
    /// Is there anything at all under <paramref name="root"/> that could draw? PRESENCE, not
    /// visibility — see the block above for why the visibility form of this question is the wrong
    /// one and how the log proved it. Mod-owned children (the "GloomhavenVR." prefix the fit path
    /// already skips) do not count: a bar of our own is exactly what must not keep an empty window
    /// alive.
    /// </summary>
    private static bool HasAnyDrawnContent(Transform root)
    {
        EmptyCheckGraphics.Clear();
        root.GetComponentsInChildren(includeInactive: false, EmptyCheckGraphics);
        for (int i = 0; i < EmptyCheckGraphics.Count; i++)
        {
            // `g.enabled` IS read here, unlike the Renderer arm below, and the asymmetry is checked
            // rather than assumed: the panel's render hide collects Canvases and Renderers, and a
            // uGUI Graphic is neither (it draws through a CanvasRenderer, which does not derive from
            // Renderer). So a disabled Graphic is the GAME's statement that this element does not
            // draw, never an echo of the mod's own hide.
            Graphic g = EmptyCheckGraphics[i];
            if (g == null || !g.enabled)
                continue;
            if (g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue;
            Rect r = g.rectTransform != null ? g.rectTransform.rect : default;
            if (r.width > 0.5f && r.height > 0.5f)
                return true;
        }
        EmptyCheckRenderers.Clear();
        root.GetComponentsInChildren(includeInactive: false, EmptyCheckRenderers);
        for (int i = 0; i < EmptyCheckRenderers.Count; i++)
        {
            // NOTE THE ABSENCE OF `rend.enabled`, AND IT IS NOT AN OVERSIGHT. The reveal gate keeps
            // the panel render-hidden until this very moment, and the hide works by disabling the
            // recorded Canvases AND Renderers (SetPanelRenderVisible / HideTree — the MODAL REVEAL
            // line reports "unhid N canvas(es) + M renderer(s)"). Reading `enabled` here would
            // therefore read OUR OWN hide back and call every 3D preview absent. Presence is the
            // question; the mod's own hide is not an answer to it.
            Renderer rend = EmptyCheckRenderers[i];
            if (rend != null
                && !rend.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // ---- THE LIVENESS RULE (ModBuild 230) ------------------------------------------------------
    //
    // USER RULING, verbatim (2026-08-23, .planning/debug/leeres_fenster2.jpg): "Es darf niemals
    // leere Fenster geben - verschwindet das Objekt das in dem Fenster dargestellt wird, soll auch
    // das Fenster verschwinden."
    //
    // WHAT THE ModBuild 226 TEST ABOVE ACTUALLY TESTS, AND WHY IT DID NOT COVER THIS. Two things,
    // and BOTH of them have to be extended rather than one:
    //
    //   1. IT RUNS ONCE. RefuseEmptyFloat is called from CanvasConversion.CompleteReveal — the
    //      reveal EDGE, once per float. Its own comment says so and gives the reason ("a per-frame
    //      check would cost a component walk on every panel forever"). So it can only ever answer
    //      "was this window born empty?". The window in the report was NOT born empty: the ModBuild
    //      229 log has it convert, fit to 540x149 px and commit a HIT RECT "from 6 visible
    //      graphic(s)" — it had content, the player clicked it, and the content left AFTERWARDS.
    //      Nothing looks at a floated window again from that moment until the game closes it.
    //
    //   2. IT IS A PRESENCE TEST, NOT A DRAWN TEST — deliberately, and the deliberation is the
    //      point. HasAnyDrawnContent asks "is there any active Graphic with a non-degenerate rect,
    //      or any Renderer, under the target?" and its doc comment argues at length why the
    //      VISIBILITY form of the question would be wrong AT THE REVEAL EDGE (a window mid-fade has
    //      every graphic at alpha 0 and is about to be perfectly fine). That argument is correct
    //      there and useless here: the popup this report is about is hidden by a GUIAnimator
    //      (UILocationPopup.Hide → showAnimator.Stop + GoInitState, decompiled/GH.Runtime/
    //      UILocationPopup.cs:24-28), which leaves every Graphic ACTIVE, ENABLED and full-size and
    //      merely takes its alpha away. HasAnyDrawnContent returns TRUE for that window forever.
    //      Re-running the ModBuild 226 test every frame would therefore have caught NOTHING.
    //
    // So the rule below is a genuinely different measurement, gated by time instead of by hope:
    //
    //   SHAPE "GONE"  — the conversion target (or the UIWindow) was DESTROYED under us. Nothing can
    //                   bring it back, so it releases immediately, with no dwell.
    //   SHAPE "DARK"  — the target still exists and DRAWS NOTHING: either its subtree is inactive,
    //                   or not one Graphic under it passes the content fit's own visibility verdict
    //                   (CanvasConversion.CountsAsFitContent — enabled, un-culled, effective alpha
    //                   ≥ 0.05, non-degenerate rect, not clipped out of its scroll viewport, not
    //                   mod-owned) and no enabled Renderer draws either.
    //
    // WHY CountsAsFitContent AND NOT A FRESH PREDICATE. This project has shipped a second copy of a
    // visibility test twice and both times the copy was weaker than the original and accepted what
    // the original had rejected (the MR plate's GlyphTrueRect, documented in that method). The fit
    // is the one instrument in this mod that is calibrated against real windows, its verdict is
    // already exposed for exactly this reason, and the fit log prints the same rejects
    // ("rejected 0 culled/disabled, 1 faint") next to the release line, so the two can be read
    // against each other in one grep.
    //
    // THE THREE GUARDS AGAINST A FALSE POSITIVE, because the failure mode of a liveness rule is
    // eating a working window:
    //   * NOT BEFORE THE REVEAL. While ConvertedPanel.RevealPending is set the MOD ITSELF has the
    //     panel switched off (SetPanelRenderVisible(false), re-applied every frame of the gate) —
    //     measuring then would read our own hide back, which is the exact mistake the ModBuild 226
    //     Renderer arm above documents.
    //   * NOT BEFORE THE RULE IS ARMED. A float arms on the first frame it is measured drawing
    //     something, OR when LivenessGraceSeconds have elapsed — whichever is first. The second
    //     clause is what makes this a bounded grace rather than a settle gate that can never open;
    //     this project has shipped one of those (the supersample settle gate) and the lesson was
    //     that a gate whose opening depends on the thing it is gating never opens.
    //   * NOT BEFORE THE DWELL. A window must draw nothing for EmptyDwellSeconds without a break.
    //     That is not padding: the unlock flow this report comes from hides its popup and runs a
    //     ~1 s camera focus between two unlocked locations (UIUnlockLocationFlowManager.cs:150-158,
    //     focusMoveDuration initialiser :22), so a shorter dwell would release the window in the
    //     GAP between two announcements and the second one would never be seen.
    //
    // AND ONE GUARD AGAINST A LOOP. A window released for DARKness may still be reported open by
    // the game, so the convert loop would re-float it on the very next tick, into the same dark
    // state, forever. EmptyHold holds it out of the float set until it is measured DRAWING again
    // (or until it closes) — the same "retry only after the condition changes" shape Failed and
    // EmptyRefused use, keyed on the condition that actually matters here.

    /// <summary>
    /// Bounded grace after a float is created during which the DARK shape is not judged at all,
    /// seconds. A float that is measured drawing something arms earlier; this is the ceiling, so a
    /// window can never sit un-armed forever. 1.5 s is comfortably past the 0.6 s reveal deadline
    /// (CanvasConversion.RevealMaxWaitSeconds) that bounds how long a window may stay hidden, so
    /// every float is revealed and judged well inside it.
    /// </summary>
    private const float LivenessGraceSeconds = 1.5f;

    /// <summary>
    /// How long a floated window must draw NOTHING, without a break, before it is released,
    /// seconds. See the dwell paragraph above for why this is 2 s and not a frame: the named case
    /// legitimately blanks its own content for the duration of a ~1 s camera focus between two
    /// announcements, and releasing in that gap would lose the second announcement.
    /// </summary>
    private const float EmptyDwellSeconds = 2f;

    /// <summary>
    /// The dwell for a window whose SCRIPTED LEVEL MESSAGE the game still considers displayed,
    /// seconds — longer, and the asymmetry is the DurabilityPanel rule rather than caution.
    ///
    /// <para>The release loop's do-no-harm guard (<c>ScriptedLevelMessageActive</c>, part 4 step 1)
    /// says such a window is "NEVER released, whatever the poll flags momentarily read", because
    /// releasing a tutorial box mid-message restores it to the invisible 2D stack and the
    /// dismiss-chained tutorial dies there. That guard is backed by a user ruling and the liveness
    /// rule may not silently overturn it — but "never" and "even when it is provably drawing
    /// nothing" are different claims, and only the first one was ever agreed. So the rule still
    /// applies, at a dwell long enough that no inter-message gap can reach it: a stranded tutorial
    /// frame leaves after 6 s, and a chain with a DisplayDelay between messages is untouched. The
    /// cost of being wrong in each direction is not symmetric — a wrongly-released blocker is
    /// recoverable (it re-floats when its content returns, at the stored chain pose), a dropped one
    /// is a deadlock — which is why this number is generous rather than tight.</para>
    /// </summary>
    private const float ScriptedMessageDwellSeconds = 6f;

    /// <summary>Frames between DARK measurements of one float. The walk is a
    /// GetComponentsInChildren over the window subtree; at 6 frames it runs ~12x/second per floated
    /// window, which the census line below reports the measured cost of.</summary>
    private const int LivenessCheckStride = 6;

    /// <summary>Seconds between MODAL LIVENESS CENSUS lines. The census exists so a population that
    /// is permanently in grace (or a rule that silently never runs) cannot be mistaken for a
    /// working check — a scan that only logs on success hides that it never ran.</summary>
    private const float LivenessCensusSeconds = 20f;

    /// <summary>Seconds between orphan-chrome sweeps (grab holders whose window is gone).</summary>
    private const float ChromeSweepSeconds = 5f;

    private static readonly List<Graphic> LiveCheckGraphics = new(128);
    private static readonly List<Renderer> LiveCheckRenderers = new(16);

    /// <summary>
    /// Windows released by the DARK shape, held out of the float set until they are measured
    /// drawing again or the game closes them. See the loop guard above.
    /// </summary>
    private static readonly HashSet<UIWindow> EmptyHold = new();

    private static float _nextEmptyHoldProbe;
    private static float _nextLivenessCensus;
    private static float _nextChromeSweep;

    // Self-cost accumulators for the census line (the instrument states its own price).
    private static long _livenessTicks;
    private static int _livenessFrames;
    private static int _livenessWalks;

    // Orphan-chrome sweep results, reported by the census rather than by a line of their own.
    private static int _chromeSweeps;
    private static int _chromeLive;
    private static int _chromeOrphansSinceCensus;

    /// <summary>
    /// Is <paramref name="window"/> currently held out of the float set because it was released for
    /// drawing nothing? Consulted by the convert loop (part 4 step 3) and by the catch-all, and
    /// PRUNED here — this is the one place the set is read, so the prune cannot drift from it.
    /// </summary>
    private static bool EmptyHeldNow(UIWindow? window)
    {
        if (window == null || EmptyHold.Count == 0 || !EmptyHold.Contains(window))
            return false;
        // A close/re-open clears the hold outright, exactly like Failed and EmptyRefused: the
        // player asked for the window again and nothing this rule measured survives that.
        if (!window.IsOpen)
        {
            EmptyHold.Remove(window);
            VRLog.Info("WorldUI", $"MODAL LIVENESS: hold on '{window.name}' (ID {window.ID}) released "
                                  + "— the game has closed the window, so its next open is judged afresh.");
            return false;
        }
        // Still open: the hold lifts the moment its content comes back. Probed at 6 Hz on the
        // GAME-side window (it is back at its 2D home, so there is no panel to ask the fit about) —
        // deliberately the LOOSER of the two tests, because a false "it is back" only causes a
        // re-float, which the floated-side measurement then judges properly.
        float now = Time.unscaledTime;
        if (now < _nextEmptyHoldProbe)
            return true;
        _nextEmptyHoldProbe = now + 1f / 6f;
        if (!DrawsAnythingLoose(window.transform))
            return true;
        EmptyHold.Remove(window);
        VRLog.Info("WorldUI", $"MODAL LIVENESS: hold on '{window.name}' (ID {window.ID}) released — "
                              + "the window is drawing content again (at least one enabled, un-culled "
                              + "Graphic above the 0.05 effective-alpha floor with a non-degenerate "
                              + "rect), so it may float again. It was held out of the float set "
                              + "because it had been released for drawing nothing.");
        return false;
    }

    /// <summary>
    /// Does anything under <paramref name="root"/> draw right now? The panel-free form of the
    /// question (no host to take host-local bounds against, no clipper walk), used ONLY to decide
    /// whether a held-out window's content has come back. Enabled + un-culled + effective alpha
    /// above the fit's own floor + non-degenerate rect; mod-owned children never count.
    ///
    /// <para>ModBuild 233 — INTERNAL, and deliberately shared rather than copied.
    /// <see cref="SubViewRevival.ShouldReviveHost"/> asks the same question about a nested sub-view
    /// ("is the host drawing one level down?") and must get the same answer as
    /// <see cref="EmptyHeldNow"/> does about the host, or the two halves of the hold could disagree
    /// about the same subtree. This project has shipped a second copy of a visibility test twice and
    /// both copies were weaker than the original.</para>
    /// </summary>
    internal static bool DrawsAnythingLoose(Transform root)
    {
        LiveCheckGraphics.Clear();
        root.GetComponentsInChildren(includeInactive: false, LiveCheckGraphics);
        for (int i = 0; i < LiveCheckGraphics.Count; i++)
        {
            Graphic g = LiveCheckGraphics[i];
            if (g == null || !g.enabled || g.canvasRenderer == null || g.canvasRenderer.cull)
                continue;
            if (g.color.a * g.canvasRenderer.GetInheritedAlpha() < 0.05f)
                continue;
            Rect r = g.rectTransform != null ? g.rectTransform.rect : default;
            if (r.width < 0.5f || r.height < 0.5f)
                continue;
            if (g.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                continue;
            return true;
        }
        LiveCheckRenderers.Clear();
        root.GetComponentsInChildren(includeInactive: false, LiveCheckRenderers);
        for (int i = 0; i < LiveCheckRenderers.Count; i++)
        {
            Renderer rend = LiveCheckRenderers[i];
            if (rend != null && rend.enabled
                && !rend.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this float's content draw anything RIGHT NOW, judged with the content fit's own
    /// visibility verdict? <paramref name="darkReason"/> carries the sub-reason when the answer is
    /// no, so the release line states what was measured rather than asserting a mechanism.
    /// </summary>
    private static bool MeasureDrawsSomething(WindowPanel wp, out string darkReason)
    {
        darkReason = string.Empty;
        ConvertedPanel panel = wp.Panel;
        Transform root = panel.Target;
        if (!root.gameObject.activeInHierarchy)
        {
            // UIWindow.ChangeActive deactivates the whole window GameObject at zero alpha when its
            // serialized m_DisableOnZeroAlpha is set (decompiled/GH.Runtime/UnityEngine.UI/
            // UIWindow.cs:742-747). An inactive subtree draws nothing at all, so this IS the dark
            // shape — and it is a separate sub-reason because it is the only one a reader can act
            // on without opening the scene (the game switched the object off; nothing is broken).
            darkReason = "its subtree is INACTIVE (a UIWindow with m_DisableOnZeroAlpha "
                         + "deactivates itself at zero alpha, UIWindow.cs:742-747)";
            return false;
        }
        _livenessWalks++;
        // Per-pass memo hygiene: CountsAsFitContent caches clipper/authored-offset answers keyed by
        // Transform and a fit pass clears them at its own start. An outside caller must do the same
        // or it reads answers cached against a layout that has since moved (that contract is stated
        // on BeginContentQuery itself).
        CanvasConversion.BeginContentQuery();
        LiveCheckGraphics.Clear();
        root.GetComponentsInChildren(includeInactive: false, LiveCheckGraphics);
        int graphics = LiveCheckGraphics.Count;
        for (int i = 0; i < graphics; i++)
        {
            if (CanvasConversion.CountsAsFitContent(panel, LiveCheckGraphics[i]))
                return true;
        }
        LiveCheckRenderers.Clear();
        root.GetComponentsInChildren(includeInactive: false, LiveCheckRenderers);
        for (int i = 0; i < LiveCheckRenderers.Count; i++)
        {
            // `enabled` IS read here, unlike in the ModBuild 226 reveal-edge test, and the asymmetry
            // is deliberate: that test runs while the mod's own render hide is still on, this one
            // runs only after RevealPending has cleared, so a disabled Renderer here is the GAME's
            // statement and not an echo of ours. The 3D character/enemy previews draw through these
            // and carry no Graphic at all, so they must be counted.
            Renderer rend = LiveCheckRenderers[i];
            if (rend != null && rend.enabled
                && !rend.gameObject.name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal))
                return true;
        }
        darkReason = $"not one of {graphics} Graphic(s) under it passes the content fit's own "
                     + $"visibility verdict and none of {LiveCheckRenderers.Count} Renderer(s) is "
                     + "enabled";
        return false;
    }

    /// <summary>
    /// THE LIVENESS PASS. Called from <see cref="Tick"/>'s release phase, immediately BEFORE the
    /// release loop, so a window judged dead this tick leaves through that ONE existing teardown —
    /// panel, grab holder, X, collider, arc slot and pose bookkeeping included. It never releases
    /// anything itself and it never touches the game's window state: this is local presentation
    /// only, and a transient popup's flow state stays entirely the game's (nothing goes on the wire
    /// for it either — see the multiplayer note on <see cref="IsTransientAnnouncement"/>).
    /// </summary>
    private static void TickWindowLiveness()
    {
        // ModBuild 231 — THE QUEST-INTRO COMPOSITE AND THE POINT-OF-NO-RETURN GATE RUN HERE, AND THE
        // POSITION IS LOAD-BEARING RATHER THAN CONVENIENT.
        //
        // StoryComposite parks the loadout screen's quest PICTURE under the map story window so the
        // two halves of the intro are ONE panel (user: "Dialog und Bild soll ein einziges 'blaues'
        // Fenster sein, mit dem Dialog unter dem Bild"). The hand-back — Unpark — MUST run before
        // the release loop that follows this call in ModalFallback.Tick: that loop calls
        // CanvasConversion.Release, which DESTROYS the host GameObject the story window was
        // re-parented under, and a subtree still parked under a destroyed host cannot be given back
        // to the loadout screen. This liveness step is the earliest per-tick point of the modal
        // pipeline that runs before that loop.
        //
        // It is also the right side of the CONVERT loop: the park happens on the tick the story
        // window OPENS, i.e. one tick before its conversion measures it, so the panel's first
        // content fit already unions picture + dialog and the composite never has to grow into
        // place with a visible jump.
        StoryComposite.Tick();

        long begin = System.Diagnostics.Stopwatch.GetTimestamp();
        float now = Time.unscaledTime;
        int frame = Time.frameCount;
        int armed = 0;
        int inGrace = 0;
        float oldestGraceAge = 0f;

        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.EmptyReleasePending)
                continue;

            // SHAPE "GONE" — the target or the window was destroyed under us. No dwell: a destroyed
            // object never comes back, and every tick we wait is a tick of chrome with nothing in it.
            if (wp.Window == null || wp.Panel == null || !wp.Panel.IsAlive
                || wp.Panel.HostGo == null || wp.Panel.HostRect == null)
            {
                wp.EmptyReleasePending = true;
                wp.EmptyReleaseShape =
                    wp.Window == null
                        ? "GONE — the game UIWindow object was DESTROYED"
                        : wp.Panel == null || !wp.Panel.IsAlive
                            ? "GONE — the conversion target (the window rect we moved onto the host) was DESTROYED"
                            : "GONE — the mod-owned world host this window was moved onto no longer exists";
                continue;
            }

            if (wp.Panel.RevealPending)
            {
                // The mod itself has this panel switched off. Not a measurement, not a grace: there
                // is nothing to measure yet, and the reveal gate's own 0.6 s deadline bounds it.
                inGrace++;
                float ageHidden = now - wp.FloatedAt;
                if (ageHidden > oldestGraceAge) oldestGraceAge = ageHidden;
                continue;
            }

            if (frame < wp.LivenessNextCheckFrame)
            {
                if (wp.LivenessArmed) armed++;
                else
                {
                    inGrace++;
                    float a = now - wp.FloatedAt;
                    if (a > oldestGraceAge) oldestGraceAge = a;
                }
                continue;
            }
            wp.LivenessNextCheckFrame = frame + LivenessCheckStride;

            bool draws = MeasureDrawsSomething(wp, out string darkReason);
            if (draws)
            {
                wp.LastDrawnAt = now;
                wp.EmptySince = 0f;
                if (!wp.LivenessArmed)
                {
                    wp.LivenessArmed = true;
                    wp.LivenessArmedAt = now;
                    wp.LivenessArmReason = "FIRST PAINT";
                    VRLog.Info("WorldUI", $"MODAL LIVENESS ARMED: '{wp.Window.name}' (ID {wp.Window.ID}) "
                                          + $"after {(now - wp.FloatedAt) * 1000f:F0} ms — it has been "
                                          + "measured drawing content, so from here on it is watched: if "
                                          + "it stops drawing for "
                                          + $"{EmptyDwellSeconds:F1} s the whole float is released.");
                }
                armed++;
                continue;
            }

            if (!wp.LivenessArmed)
            {
                // Bounded grace. The ceiling is what makes a permanently-un-armed window impossible;
                // the census below prints the population either way so the claim can be checked.
                if (now - wp.FloatedAt < LivenessGraceSeconds)
                {
                    inGrace++;
                    float a = now - wp.FloatedAt;
                    if (a > oldestGraceAge) oldestGraceAge = a;
                    continue;
                }
                wp.LivenessArmed = true;
                wp.LivenessArmedAt = now;
                wp.LivenessArmReason = "GRACE EXPIRY";
                VRLog.Info("WorldUI", $"MODAL LIVENESS ARMED: '{wp.Window.name}' (ID {wp.Window.ID}) "
                                      + $"after the bounded {LivenessGraceSeconds:F1} s grace — it has "
                                      + "NEVER been measured drawing anything since it floated. That is "
                                      + "not yet a verdict: the dwell still has to run, and this line "
                                      + "exists so a window that arms this way is distinguishable in the "
                                      + "log from one that armed by painting.");
            }
            armed++;

            // ModBuild 233 — REQUIREMENT (a): THE CHARACTER UI MAY NOT CLOSE WHILE THE PRIVATE
            // QUESTS ARE BEING CHOSEN. User: "Das Fenster [soll] sich nicht schließen und
            // Character-UI sichtbar bleiben bis die private Quests vollständig ausgewählt wurde."
            //
            // Placed AHEAD of the dwell clock rather than beside the dwell BAR, and the difference
            // is the whole guard: EmptySince is left at 0 while a selection is live, so when the
            // selection ends the dwell starts from that moment instead of retro-counting the
            // seconds it was held. A blank the rule was told to ignore must not be bankable.
            //
            // The predicate is the GAME's own state, four live terms including the party display's
            // own UIWindow.IsOpen, so the hold cannot outlive the selection — see
            // SubViewRevival.SelectionHoldsRelease for each citation and for the honest statement of
            // what this does NOT do (it would not have fired at Player.log:4635, where the panel was
            // already closed and the release was correct).
            if (SubViewRevival.SelectionHoldsRelease(wp.Window, out _))
            {
                wp.EmptySince = 0f;
                continue;
            }

            if (wp.EmptySince <= 0f)
            {
                wp.EmptySince = now;
                continue;
            }
            // The scripted-level-message dwell is read LIVE, not latched at the start of the run: a
            // chain whose current message ends mid-dwell must fall back to the ordinary bar rather
            // than keep the longer one for a window that is no longer protected by anything.
            bool scripted = ScriptedLevelMessageActive(wp.Window);
            float dwell = scripted ? ScriptedMessageDwellSeconds : EmptyDwellSeconds;
            if (now - wp.EmptySince < dwell)
                continue;

            wp.EmptyReleasePending = true;
            wp.EmptyReleaseShape = $"DARK — {darkReason}"
                                   + $" (dwell {dwell:F1} s"
                                   + (scripted
                                       ? ", the longer bar: the game still considers this window's "
                                         + "scripted level message displayed"
                                       : string.Empty)
                                   + ")";
        }

        // ---- orphan chrome, then census + self-cost ---------------------------------------------
        if (now >= _nextChromeSweep)
        {
            _nextChromeSweep = now + ChromeSweepSeconds;
            _chromeSweeps++;
            SweepOrphanChrome();
        }

        _livenessTicks += System.Diagnostics.Stopwatch.GetTimestamp() - begin;
        _livenessFrames++;
        if (now >= _nextLivenessCensus)
        {
            _nextLivenessCensus = now + LivenessCensusSeconds;
            // A HashSet keyed by UIWindow compares by REFERENCE, not by Unity's overloaded ==, so a
            // window destroyed with its scene stays in the hold as a dead key forever. EmptyHeldNow
            // never trips over one (it null-checks with the Unity operator first), but the count
            // this line prints would drift upward and stop meaning anything — and a census that
            // reports a number nobody can act on is worse than no census.
            EmptyHold.RemoveWhere(w => w == null);
            double usPerFrame = _livenessFrames > 0
                ? _livenessTicks * 1_000_000.0 / System.Diagnostics.Stopwatch.Frequency / _livenessFrames
                : 0.0;
            // SILENT WHEN THERE IS NOTHING TO FALSIFY — no float standing, nothing held out, no
            // orphan destroyed — because a line that says "0 of 0" every 20 s for a whole session
            // trains a reader to skip the one that says something. The CADENCE is unconditional
            // (the timer and the accumulators are reset either way), so every line that IS printed
            // still covers exactly one 20 s window and the numbers stay comparable.
            if (Converted.Count > 0 || EmptyHold.Count > 0 || _chromeOrphansSinceCensus > 0)
            {
                VRLog.Info("WorldUI", $"MODAL LIVENESS CENSUS: {Converted.Count} floated window(s) — "
                                      + $"{armed} ARMED, {inGrace} still in the bounded "
                                      + $"{LivenessGraceSeconds:F1} s grace (oldest {oldestGraceAge * 1000f:F0} ms). "
                                      + $"{EmptyHold.Count} window(s) held out of the float set for having been "
                                      + "released dark. THIS LINE IS THE FALSIFIER: the grace has a ceiling, so an "
                                      + "'in grace' age above it, or a population that is never ARMED, means the "
                                      + "rule is not running rather than that every window is fine. COST: "
                                      + $"{usPerFrame:F1} us/frame averaged over {_livenessFrames} tick(s), of "
                                      + $"which {_livenessWalks} subtree walk(s) (stride {LivenessCheckStride} "
                                      + "frames per window; a window behind the reveal gate is not walked at "
                                      + $"all). CHROME: {_chromeSweeps} orphan sweep(s) in this window saw "
                                      + $"{_chromeLive} live grab holder(s) and destroyed "
                                      + $"{_chromeOrphansSinceCensus} orphan(s).");
            }
            _livenessTicks = 0;
            _livenessFrames = 0;
            _livenessWalks = 0;
            _chromeSweeps = 0;
            _chromeOrphansSinceCensus = 0;
        }
    }

    /// <summary>
    /// ORPHANED CHROME: a mod-owned grab holder that no floated window owns any more.
    ///
    /// <para>WHY THIS EXISTS SEPARATELY FROM THE RULE ABOVE. The grab holder is a SCENE-ROOT tree
    /// (GrabbableModal.EnsureFrame builds <c>GloomhavenVR.ModalGrab_*</c> at the scene root — the
    /// host follows the frame, not the other way round), so it is the one piece of window chrome
    /// that does NOT die with the host when a panel is released. Every release path in this class
    /// calls <c>wp.Grab?.Destroy()</c> first and is therefore correct today; this sweep is the net
    /// under all of them, because the artefact it catches — a tan bar hanging in mid-air with no
    /// window on it — is exactly what the user photographed THREE of in one frame
    /// (.planning/debug/leeres_fenster2.jpg) and there is no other place in the mod that could
    /// notice one. It states what it found rather than asserting how it got there: this sweep
    /// cannot observe which release path dropped a holder, and a verdict that named one would be a
    /// mechanism the instrument cannot see.</para>
    /// </summary>
    private static void SweepOrphanChrome()
    {
        _chromeLive = GrabbableModal.LiveHolders.Count;
        int destroyed = 0;
        for (int i = GrabbableModal.LiveHolders.Count - 1; i >= 0; i--)
        {
            GrabbableModal holder = GrabbableModal.LiveHolders[i];
            bool owned = false;
            for (int j = 0; j < Converted.Count; j++)
            {
                if (ReferenceEquals(Converted[j].Grab, holder))
                {
                    owned = true;
                    break;
                }
            }
            if (owned)
                continue;
            VRLog.Warn("WorldUI", $"ORPHAN CHROME DESTROYED: the grab holder '{holder.LogName}' is not "
                                  + "owned by any of the "
                                  + $"{Converted.Count} floated window(s), so it is a brass bar with no "
                                  + "window on it — the artefact of .planning/debug/leeres_fenster2.jpg. "
                                  + "It has been destroyed. WHAT THIS LINE DOES NOT SAY: which release "
                                  + "path dropped it. This sweep compares two live sets and cannot "
                                  + "observe that; if it appears, the holder's name is the window to "
                                  + "trace back through the MODAL WINDOW release lines.");
            holder.Destroy();
            destroyed++;
        }
        _chromeOrphansSinceCensus += destroyed;
        // Deliberately SILENT when nothing was orphaned. The counts are reported by the census line
        // instead, on its own throttle, so "the sweep found nothing" and "the sweep never ran" are
        // still distinguishable without a line every 5 s.
    }

    private static void LogPollTransition(ref bool state, bool now, string what)
    {
        if (now == state)
            return;
        state = now;
        VRLog.Info("WorldUI", now
            ? $"MODAL FALLBACK poll: {what} OPEN → ModalUI (floating window or screen) until it closes."
            : $"Modal fallback poll: {what} closed.");
    }
}
