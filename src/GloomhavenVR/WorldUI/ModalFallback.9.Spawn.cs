using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using Script.GUI.Popups;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class ModalFallback
{
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

    /// <summary>Request B: max upward tilt (top toward the player, degrees) applied when the
    /// clamp raised/pulled the window while the player looks steeply down — so the raised
    /// panel still faces the eyes. Small enough to keep the upright reading look.</summary>
    private const float MaxSpawnTiltDeg = 15f;

    /// <summary>
    /// Request B: the board/table plane height, world units — the camera orbit focus the whole
    /// panel layout is anchored on (<see cref="PanelLayout.TryGetAnchor"/> uses the same
    /// <c>CameraController.FocusPoint</c> as "table center"; slot heights are measured from it).
    /// False outside a scenario (no board → no floor to clamp against).
    /// </summary>
    private static bool TryGetBoardPlaneY(out float y)
    {
        CameraController controller = CameraController.s_CameraController;
        if (controller != null && VRModeStateMachine.ScenarioBoardExists)
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
        float scale, Vector2 half, float maxPitchDeg)
    {
        string? reason = null;

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
        if (TryGetBoardPlaneY(out float boardY))
        {
            float floorY = boardY + BoardTopClearanceMeters * scale + half.y;
            float eyeCap = headPos.y + MaxAboveEyeMeters * scale;
            float minY = Mathf.Min(floorY, eyeCap); // readability wins in the degenerate case
            if (pos.y < minY)
            {
                string floor = $"raised y {pos.y:F2} → {minY:F2} so its bottom clears the board top " +
                               $"(board plane {boardY:F2} + top-clear {BoardTopClearanceMeters:F2} m × scale " +
                               $"{scale:F2} + half-height {half.y:F2}" +
                               (minY < floorY ? ", capped near eye level" : "") + ")";
                reason = reason == null ? floor : $"{reason}; {floor}";
                pos.y = minY;
            }
        }
        return reason;
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
    /// gaze — and when the clamp engaged, a small upward tilt (top toward the player, capped at
    /// <see cref="MaxSpawnTiltDeg"/>) keeps the raised panel facing the eyes. Event-gated ONLY:
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
        if (replay.HasValue)
        {
            // Replay: the placement inputs are frozen, only the geometry changed.
            SpawnAnchor a = replay.Value;
            headPos = a.HeadPos;
            fwd = a.HeadForward;
            scale = a.Scale;
            staggerIndex = a.StaggerIndex;
            levelMessage = a.LevelMessage;
            pos = a.RawPos;
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
            // Placement follows the full gaze (so it lands where the player is looking, overlapping
            // the primary), with a small right+down stagger per stacked window. Level-message
            // windows (tutorial boxes/strips) float CLOSER for readability (user report 2026-08-02);
            // every other family keeps the shared reading distance.
            pos = headPos + fwd * ((levelMessage ? LevelMessageDistanceMeters : WindowDistanceMeters)
                                   * scale);
            if (staggerIndex > 0)
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
        };
        float maxPitchDeg = levelMessage ? LevelMsgMaxSpawnPitchDeg : MaxSpawnPitchDeg;
        string? clampReason = ClampSpawnPose(headPos, fwd, ref pos, scale, halfSize, maxPitchDeg);

        // User request A: never spawn INSIDE the control board or another open modal —
        // raise / swing laterally toward free space (spawn/refloat/recall only, never per
        // frame). Staggered secondaries deliberately overlap their parent window (item 2/3b),
        // so they only avoid the board.
        string? overlapNote = ResolveSpawnOverlap(headPos, ref pos, scale, halfSize, self,
            includeModals: staggerIndex == 0);

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

        // Request B: when the clamp raised/pulled the window while the player looks steeply
        // down, tilt its top slightly toward the player (positive local-X pitch tips the front
        // face UP toward a head above the panel — PanelLayout's slot-tilt convention) so the
        // raised panel still faces the eyes. Spawn-only, capped, never applied unclamped so the
        // default upright look is untouched.
        float tiltDeg = 0f;
        if (clampReason != null || overlapNote != null || viewConeNote != null)
        {
            Vector3 toHead = headPos - pos;
            float flatDist = Mathf.Sqrt(toHead.x * toHead.x + toHead.z * toHead.z);
            float elevDeg = Mathf.Atan2(toHead.y, Mathf.Max(flatDist, 1e-3f)) * Mathf.Rad2Deg;
            tiltDeg = Mathf.Clamp(elevDeg, 0f, MaxSpawnTiltDeg);
            if (tiltDeg > 0.5f)
                rot *= Quaternion.Euler(tiltDeg, 0f, 0f);
            else
                tiltDeg = 0f;
        }

        // Request B diagnostic: ONE line per spawn/refloat/recall (this method is never called
        // per frame) stating the clamp decision — original pose → clamped pose, reason.
        bool haveBoard = TryGetBoardPlaneY(out float by);
        // Board-top clearance actually applied: the CENTER floor that keeps the window BOTTOM above
        // the board top (boardY + top-clear×scale + half-height), capped near eye level.
        float boardTopFloorY = haveBoard ? by + BoardTopClearanceMeters * scale + halfSize.y : float.NaN;
        VRLog.Info("WorldUI", "MODAL SPAWN CLAMP" +
                              (replay.HasValue ? " (RE-PLACE at the FINAL fitted geometry)" : "") + ": pose " +
                              $"({rawPos.x:F2},{rawPos.y:F2},{rawPos.z:F2}) → " +
                              $"({pos.x:F2},{pos.y:F2},{pos.z:F2})" +
                              (clampReason == null && overlapNote == null && viewConeNote == null
                                  ? " — unchanged (above the board plane, gaze within limits, no overlap)."
                                  : $" — {clampReason ?? "no plane/gaze clamp"}; upward tilt {tiltDeg:F0}°.") +
                              (overlapNote == null
                                  ? " OVERLAP: none."
                                  : $" OVERLAP: {overlapNote}.") +
                              (viewConeNote == null
                                  ? ""
                                  : $" VIEW-CONE: {viewConeNote}.") +
                              $" boardPlaneY={(haveBoard ? by.ToString("F2") : "n/a")}, " +
                              $"boardTopClear={BoardTopClearanceMeters:F2}m+halfH{halfSize.y:F2} " +
                              $"(window-bottom floorY={(haveBoard ? boardTopFloorY.ToString("F2") : "n/a")}, " +
                              $"eyeCap +{MaxAboveEyeMeters:F2}m, maxPitch {maxPitchDeg:F0}°), " +
                              $"dist={distanceMeters:F2}m{(levelMessage ? " (level-message)" : "")}, " +
                              $"scale={scale:F2}, stagger={staggerIndex}.");
        return true;
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
        float widthPx = panel.HostRect != null ? panel.HostRect.rect.width : 0f;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        if (widthPx < 1f || metersPerPixel <= 0f)
            return WindowScaleFactor;
        float boardRelative = ModalTargetWidthMeters / (widthPx * metersPerPixel);
        return Mathf.Clamp(Mathf.Min(WindowScaleFactor, boardRelative),
            MinWindowScaleFactor, WindowScaleFactor);
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
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            // Round 3 (first-open size bug): a one-shot VERIFY correction re-fits a committed rect
            // that turned out not to contain its own content. That advances the panel's applied-fit
            // generation, which RE-ARMS this latch so the placement is replayed against the
            // corrected half-size — otherwise the window would keep the spawn clamps computed from
            // the rejected rect. TickPoseRePlaceOne itself refuses to move an already REVEALED
            // window, so a late correction can never yank a visible window around.
            if (wp.PoseRePlaceDone && wp.PoseRePlacedAtFit != wp.Panel.FitAppliedGeneration)
                wp.PoseRePlaceDone = false;
            TickPoseRePlaceOne(wp.Panel, wp.Grab, wp.Window, wp.ExtraScale,
                wp.OneShotFitted && wp.ScaleReDerivedAtFit != wp.Panel.FitAppliedGeneration,
                ref wp.SpawnAnchor, ref wp.PoseRePlaceDone);
            if (wp.PoseRePlaceDone)
                wp.PoseRePlacedAtFit = wp.Panel.FitAppliedGeneration;
        }
        // Part 10: the GlobalErrorMessage float is NOT a UIWindow and therefore has no WindowPanel
        // record — it carries its own anchor/latch pair so the identical re-place applies to it.
        if (_errorPanel != null)
            TickPoseRePlaceOne(_errorPanel, _errorGrab, null, _errorExtraScale,
                scalePending: false, ref _errorSpawnAnchor, ref _errorPoseRePlaceDone);
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
        for (int i = Converted.Count - 1; i >= 0; i--)
        {
            WindowPanel wp = Converted[i];
            string name = wp.Window != null ? wp.Window.name : "<destroyed>";
            wp.Grab?.Destroy(); // sub-item B: drop the mod-owned grab holder before releasing the host
            CanvasConversion.Release(wp.Panel);
            VRLog.Info("WorldUI", $"MODAL WINDOW: '{name}' released ({reason}) — restored to its 2D home.");
        }
        Converted.Clear();
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
