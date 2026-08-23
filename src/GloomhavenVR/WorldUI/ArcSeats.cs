using UnityEngine;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE MAP ROOM'S HALF CIRCLE — where a floated window is SEATED, as opposed to how big it may be.
///
/// <para>THE REPORT THIS ANSWERS (ModBuild 233, .planning/debug/Questauswahl.jpg), verbatim:
/// "Weiterhin ist auch die Questinfo in den Sichtbereich gespawned. Es macht Sinn dass es nochmal
/// spawned und das es ein eigenes Fenster ist, ABER es soll nicht alles auf einem Fleck spawnen
/// sondern am Besten in einem halbkreis innerhalb des sichtbereichs ausgerichtet."</para>
///
/// <para>THE MECHANISM, FROM HIS OWN LOG. ModBuild 193 gave every window an angular RESERVATION and
/// packed those reservations inside the headset's measured comfortable reading cone — ±32.0° on his
/// Quest 3 (binocular overlap 40.0° × the 0.80 comfort margin). That is 64° of arc in total. The
/// loadout burst he photographed asked for far more than 64°:</para>
/// <code>
///   -2°±29°  'Map Story Window'                 58°
///   22°±10°  'Quest Log Manager'      PERMANENT 20°
///  -18°±14°  'UI Quest Popup'                   28°
///    0°±44°  'New Party display'      PERMANENT 88°
///  -16°±16°  'UI Battle Goal Picker Window'     31°
///                                    ---------------
///                                    225° of window
/// </code>
/// <para>225° of window into 64° of arc. From the third window on, the packer's own log line reads
/// "NO free interval is left inside the cone (±32.0°)" on every single spawn, the overflow rule
/// fires every time, and the overflow rule's job is to keep the window IN THE CONE — so it puts it
/// back in the middle, on top of everything else. Five windows, five overflow lines, one pile. The
/// allocator was working exactly as designed and the design was answering the wrong question.</para>
///
/// <para>THE CORRECTION IS A SEPARATION OF TWO QUESTIONS THAT HAD ONE ANSWER:</para>
/// <list type="bullet">
/// <item>"HOW BIG MAY ONE WINDOW BE / IS THIS ONE COMFORTABLE TO READ WHERE IT IS?" — the measured
/// reading cone is the right answer and it keeps that job. It is still derived per session from the
/// projection matrix, it is still printed, and every placement is now graded against it
/// (<see cref="ArcSeatBand"/>): a window inside ±cone needs no head movement at all.</item>
/// <item>"WHERE DOES THE FIFTH WINDOW GO?" — the cone is the WRONG answer, because there is no fifth
/// place in it. Placement gets its own, much wider allowance: the HALF CIRCLE IN FRONT OF THE
/// PLAYER, <see cref="HalfCircleHalfDeg"/> = ±90°, edges included.</item>
/// </list>
///
/// <para>WHY ±90° AND NOT SOMETHING ELSE — the number, defended.</para>
/// <list type="number">
/// <item>It is what he asked for. "Halbkreis" is 180° and 180° is ±90°.</item>
/// <item>±90° is the SHOULDER LINE, and that is the honest boundary of "in front of me". A window
/// past it is behind the player, which is not "innerhalb des Sichtbereichs" under any reading. The
/// bound is applied to the window's EDGES, not its centre, so no part of any window ever crosses
/// behind the shoulder — a 58°-wide window can be seated no further out than 61°.</item>
/// <item>It is the smallest arc that actually HOLDS his loadout. 225° of window is more than 180°,
/// but only because one window (the party roster) reserves 88° for a 328 px column — see the
/// FINDING below. Every other window in that burst fits: 58 + 20 + 28 + 31 + three 2° gaps = 143°,
/// comfortably inside 180° and impossible inside 64°. A 120° arc (±60°, "comfortable neck turn")
/// would have held four of the five and put the fifth back on the pile; ±90° holds them all.</item>
/// <item>THE 192 OBJECTION IS ANSWERED BY THE SEARCH ORDER, NOT BY THE BOUND. ModBuild 192 had a
/// ±85° arc and produced "Neue Fenster spawnen irgendwo an der Seite wo man sie nicht sieht",
/// because it seated windows at FIXED steps 0°, ±34°, ±68° indexed by how many were already open —
/// the fourth window went to 68° whether or not the middle was empty. This allocator never does
/// that: it takes the FREE INTERVAL NEAREST THE CURRENT GAZE, so the outer arc is reached only when
/// everything closer in is genuinely occupied. A window is never put at 70° to avoid a 5° overlap;
/// it is put at 70° only when the alternative is being buried, which is the thing he is looking
/// at. And turning is free — it is the one thing this mod may never block.</item>
/// </list>
///
/// <para>THE FRAME IS THE WORLD, AND THAT IS A BUG FIX. ModBuild 193 stored each reservation as
/// "degrees from ITS OWN spawn gaze" and then compared those numbers to each other as if they
/// shared a frame. They do not: if the player turns 40° between two spawns, a claim at +10° and a
/// claim at +50° are the SAME world direction and the registry believes they are 40° apart. Inside
/// a ±32° cone the error was bounded by the cone and his 233 log happens to show only 2.6° of head
/// drift across the burst, so it never bit. Inside a ±90° arc it would bite hard and it would bite
/// as the exact symptom being fixed — two windows on one spot, with a log line swearing they are
/// apart. Seats are therefore held in ABSOLUTE WORLD YAW (<see cref="_arcSeatWorldYaw"/>), all
/// comparisons go through <see cref="Mathf.DeltaAngle"/>, and the search still runs relative to the
/// CURRENT spawn gaze so a new window still lands as close to where he is looking as the free space
/// allows. <see cref="ArcClaim.CentreDeg"/> keeps its documented meaning (offset from that window's
/// own spawn gaze) so every log line in ModalFallback.4.Tick.cs stays true.</para>
///
/// <para>WHAT IS DELIBERATELY UNCHANGED. Distance, height and every spawn clamp (board-top floor,
/// eye cap, pitch flatten) — a seat is a YAW and a depth and nothing else, exactly as in 193.
/// Facing is still yaw-only and still points at the player, applied once at spawn. A window is
/// placed ONCE and is then the player's: opening or closing anything never re-poses a standing
/// window, a grabbed window is never re-placed (<c>TickPoseRePlaceOne</c> refuses on
/// <c>grab.IsGrabbed</c> and on <c>!RevealPending</c>), and nothing here follows the head.</para>
///
/// <para>FINDING FOR THE NEXT ROUND, NOT FIXED HERE AND NOT MINE TO FIX. 'New Party display'
/// reserves 88° of the arc and DRAWS 14°. Its own FIXED FIT line: "host pinned at 1988x1080 px =
/// 2.09 x 1.13 m = 2087 mm wide = 82° of view at 1.2 m; the CHARACTER COLUMN renders 328x1080 px
/// from (-982,-540) … the spare width is 1648 px of empty (transparent) frame". The reservation is
/// measured from the host RECT because that is what the placement carries, so this one window eats
/// 88° of a 180° arc for a column that occupies 14° at the far LEFT edge of its own frame — which
/// is also why it appears at the side of the photograph while its reservation says 0°. The packer
/// cannot see that and must not guess it: the fix belongs where the rect is sized. Until then the
/// arc audit below prints the reservation next to the live measurement, so the cost is a number in
/// the log rather than a suspicion.</para>
/// </summary>
internal static partial class ModalFallback
{
    /// <summary>
    /// THE PLACEMENT ARC — half-extent in degrees each side of the player's spawn gaze, applied to
    /// a window's EDGES. 90° is the shoulder line: the layout is a half circle in front of him and
    /// nothing is ever seated behind it. See the class header for the full defence of this number.
    ///
    /// <para>THIS IS NOT A "FIELD OF VIEW" AND MUST NEVER BE CONFUSED WITH ONE. The headset's field
    /// of view is measured, not assumed (<see cref="UsableHalfConeDeg"/>), and it answers a
    /// different question — whether a seat needs a head turn to read. This number answers only "how
    /// far around may the layout reach before it stops being in front of me", which is a fact about
    /// a human being and not about a display, so a constant is the correct form for it. It is the
    /// one thing the ModBuild 193 tombstone forbids ("do not reintroduce a fixed half-angle") —
    /// which was written about a fixed SEAT SPACING and a fixed guess at the DISPLAY, both of which
    /// are still gone and still measured. What is fixed here is anatomy.</para>
    /// </summary>
    private const float HalfCircleHalfDeg = 90f;

    /// <summary>Two live intervals closer than this are called overlapping by the audit. Half a
    /// degree is ~1 cm at reading distance, i.e. below the width of the window's own frame art, so
    /// nothing is reported as an overlap that a player could not see as one.</summary>
    private const float ArcAuditOverlapToleranceDeg = 0.5f;

    /// <summary>
    /// SEAT CENTRES IN ABSOLUTE WORLD YAW, degrees, parallel to <see cref="_arcClaims"/> and read
    /// only where that entry's <see cref="ArcClaim.Panel"/> is non-null. See the class header for
    /// why the registry needed a world frame; <see cref="ArcClaim.CentreDeg"/> keeps its own
    /// (gaze-relative) meaning so the existing release/occupancy lines stay true.
    /// </summary>
    private static readonly float[] _arcSeatWorldYaw = new float[MaxWindowClaims];

    /// <summary>One-time geometry report (see <see cref="LogArcSeatGeometryOnce"/>).</summary>
    private static bool _arcSeatGeometryLogged;

    /// <summary>Placements made this session — printed on the audit line so a burst can be told
    /// apart from a single late arrival by grep alone.</summary>
    private static int _arcSeatGeneration;

    // Scratch for the arc audit. Static for the same reason <see cref="_arcCandidates"/> is: this
    // path runs a handful of times per session on the Unity main thread, and a per-placement
    // allocation would be pure litter. Every entry is overwritten before it is read (the audit
    // fills 0..n-1 and never looks past n).
    private static readonly string[] _auditNames = new string[MaxWindowClaims];
    private static readonly float[] _auditLiveYaw = new float[MaxWindowClaims];
    private static readonly float[] _auditLiveHalf = new float[MaxWindowClaims];
    private static readonly float[] _auditReservedYaw = new float[MaxWindowClaims];
    private static readonly float[] _auditReservedHalf = new float[MaxWindowClaims];
    private static readonly float[] _auditFrameYaw = new float[MaxWindowClaims];
    private static readonly float[] _auditFrameHalf = new float[MaxWindowClaims];
    private static readonly float[] _auditDist = new float[MaxWindowClaims];

    /// <summary>
    /// HOW MUCH DEEPER a window is seated when its own transparent frame hangs over a neighbour's
    /// visible content — one step of the existing depth ladder, real metres.
    ///
    /// <para>THE MEASURED PROBLEM. The rectangle the laser is tested against is the hit rect, and
    /// the hit rect is <c>Content ∪ Host</c> — it grows to cover content that escapes the frame and
    /// it NEVER shrinks below the frame. His own log, for the window in question:
    /// <c>HIT RECT 'New Party display': host rect 1988x1080 px at (0,0); DRAWN CONTENT 328x1080 px
    /// at (-818,0) from 135 visible graphic(s); LARGER = the host rect → HIT RECT 1988x1080 px …
    /// the interactive area is 2087x1134 mm</c>. So there is a 2.09 m invisible sheet around a
    /// 0.34 m column, and <c>RayUguiDriver</c> resolves competing canvases by NEAREST PLANE
    /// (<c>TryIntersect(canvas, origin, dir, bestDist, …)</c> with a shrinking budget). Seat a
    /// readable window inside that empty frame at the same reading distance and which one takes the
    /// click is a coin flip.</para>
    ///
    /// <para>ONE STEP AWAY FROM THE HEAD SETTLES IT, and it is the only remedy that never moves a
    /// window that is already standing — the standing ones keep their distance, the window with the
    /// oversized frame takes the step. 0.04 m at a 1.20 m reading distance is 3.2 % of apparent
    /// size, applied while the window is still render-hidden, and it is the same ladder step the
    /// overflow rule has always used. It is deliberately ONE step and not a ladder: the question is
    /// binary (is this plane behind the other one), and a ladder would walk a permanent window
    /// steadily away over a long session.</para>
    ///
    /// <para>WHY NOT SHRINK THE HIT RECT INSTEAD. That is the other cure and it is a bigger, riskier
    /// change than this lane should make: "always contains the host rect" is a stated contract of
    /// that measurement, the ray/poke plane and the mod's own grab and close-X geometry are built
    /// on it, and shrinking it would change input behaviour for every converted window in the mod,
    /// not only for the one with the phantom frame. Depth costs 3.2 % of one window's size and
    /// touches nothing else.</para>
    /// </summary>
    private const float FramePushStepMeters = 0.04f;

    /// <summary>A frame wider than its content by less than this is not worth a depth step — it is
    /// ordinary window chrome, not a phantom sheet. 4° at reading distance is ~8 cm.</summary>
    private const float PhantomFrameThresholdDeg = 4f;

    /// <summary>
    /// The DRAWN geometry of one window, in the angular terms the arc reasons in. Every field is
    /// measured at a stated distance; nothing here is assumed.
    /// </summary>
    private struct ArcDrawnGeometry
    {
        /// <summary>Half the VISIBLE content's angular width, degrees. Equals
        /// <see cref="FrameHalfDeg"/> when nothing could be measured.</summary>
        public float DrawnHalfDeg;

        /// <summary>Angular offset of the visible content's centre from the HOST RECT's centre,
        /// degrees, + = to the player's right. 0 when nothing could be measured.</summary>
        public float OffsetDeg;

        /// <summary>Half the HOST RECT's angular width, degrees — the collider footprint.</summary>
        public float FrameHalfDeg;

        /// <summary>True when the content was actually measured (false = fell back to the frame,
        /// which is the normal answer at spawn and the pre-234 behaviour).</summary>
        public bool Measured;

        /// <summary>Human-readable derivation for the placement line.</summary>
        public string Note;

        // The same three quantities in WORLD units, so the angles can be re-derived at a different
        // reading distance without a second subtree walk (the overflow rule moves the window before
        // it books the interval, and the interval has to be the one it will actually occupy).
        public float DrawnHalfWorld;
        public float OffsetWorld;
        public float FrameHalfWorld;

        /// <summary>Re-derive every angle at <paramref name="distWorld"/>. Pure arithmetic on the
        /// stored world extents — no measurement, no allocation.</summary>
        public void ReDeriveAt(float distWorld)
        {
            FrameHalfDeg = HalfAngleDeg(FrameHalfWorld, distWorld);
            DrawnHalfDeg = Measured ? HalfAngleDeg(DrawnHalfWorld, distWorld) : FrameHalfDeg;
            OffsetDeg = Measured && distWorld > 1e-4f
                ? Mathf.Atan2(OffsetWorld, distWorld) * Mathf.Rad2Deg
                : 0f;
        }
    }

    /// <summary>
    /// Measure what <paramref name="panel"/> DRAWS and express it as an angular interval at
    /// <paramref name="distWorld"/>, falling back to the host rect when nothing is measurable.
    ///
    /// <para>THE CONVERSION CARRIES NO UNITS OF ITS OWN, which is deliberate — this repo has
    /// shipped a bug where a bound named "…Meters" was compared against a world-unit product. The
    /// drawn rect arrives in host-local uGUI px and <paramref name="halfWidthWorld"/> is the same
    /// rect's half-width already in WORLD units, so the px→world factor is their ratio and every
    /// angle below is an atan of two world-unit quantities. Nothing is converted through a scale
    /// that could be the wrong one.</para>
    /// </summary>
    private static ArcDrawnGeometry MeasureArcDrawnGeometry(ConvertedPanel panel,
        float halfWidthWorld, float distWorld)
    {
        var g = new ArcDrawnGeometry
        {
            FrameHalfDeg = HalfAngleDeg(halfWidthWorld, distWorld),
            OffsetDeg = 0f,
            Measured = false,
            FrameHalfWorld = halfWidthWorld,
            DrawnHalfWorld = halfWidthWorld,
            OffsetWorld = 0f,
        };
        g.DrawnHalfDeg = g.FrameHalfDeg;
        g.Note = $"frame {g.FrameHalfDeg * 2f:F0}°, drawn extent NOT measurable yet (the window is "
                 + "still behind the reveal gate, so no graphic passes the visibility test) — the "
                 + "reservation falls back to the frame, exactly as every build before ModBuild 234";

        if (!CanvasConversion.TryMeasureDrawnContent(panel, out Rect content, out Rect host,
                out int contributors))
            return g;
        if (host.width < 1f || halfWidthWorld <= 1e-4f)
            return g;

        // px -> world from the SAME rect whose world half-width we were handed: a pure ratio.
        float pxToWorld = halfWidthWorld / (host.width * 0.5f);
        float drawnHalfWorld = content.width * 0.5f * pxToWorld;
        float offsetWorld = (content.center.x - host.center.x) * pxToWorld;
        if (drawnHalfWorld <= 1e-4f)
            return g;

        g.DrawnHalfWorld = drawnHalfWorld;
        g.OffsetWorld = offsetWorld;
        g.DrawnHalfDeg = HalfAngleDeg(drawnHalfWorld, distWorld);
        g.OffsetDeg = Mathf.Atan2(offsetWorld, distWorld) * Mathf.Rad2Deg;
        g.Measured = true;

        float widthDelta = (g.DrawnHalfDeg - g.FrameHalfDeg) * 2f;
        g.Note = $"frame {g.FrameHalfDeg * 2f:F0}° ({host.width:F0} px), drawn "
                 + $"{g.DrawnHalfDeg * 2f:F0}° ({content.width:F0} px from {contributors} visible "
                 + $"graphic(s)) at offset {g.OffsetDeg:F0}° "
                 + $"({content.center.x - host.center.x:F0} px from the frame's own centre)"
                 + (Mathf.Abs(widthDelta) < 1f && Mathf.Abs(g.OffsetDeg) < 1f
                     ? " — content fills its frame, so this reservation is what it always was"
                     : widthDelta < -PhantomFrameThresholdDeg
                         ? $" — PHANTOM FRAME: {-widthDelta:F0}° of this window's reservation is "
                           + "empty transparent frame and is handed back to the arc"
                         : widthDelta > 1f
                             ? $" — the window draws {widthDelta:F0}° WIDER than its own frame, so "
                               + "the reservation GROWS; booking only the frame would have let a "
                               + "neighbour sit on content that is really there"
                             : " — a small difference, booked as measured");
        return g;
    }

    /// <summary>
    /// Real metres this window must be pushed AWAY from the head so that its own transparent frame
    /// cannot steal the laser from a neighbour's visible content. 0 for the overwhelming majority
    /// of windows, whose frame is their content. See <see cref="FramePushStepMeters"/> for the
    /// measurement behind the rule and for why depth rather than hit-rect surgery.
    /// </summary>
    /// <param name="hostWorldYaw">Where the window's FRAME will be centred, world yaw.</param>
    /// <param name="frameHalfDeg">Half the frame's angular width.</param>
    /// <param name="drawnHalfDeg">Half the drawn content's angular width.</param>
    /// <param name="skipSlot">This window's own registry slot, excluded from the test (a window
    /// cannot overhang itself — the fuse-that-counted-the-player lesson).</param>
    /// <param name="victims">Names of the neighbours whose visible content the frame covers.</param>
    private static float ArcSeatFramePush(float hostWorldYaw, float frameHalfDeg, float drawnHalfDeg,
        int skipSlot, out string victims)
    {
        victims = "(none)";
        if (frameHalfDeg - drawnHalfDeg < PhantomFrameThresholdDeg * 0.5f)
            return 0f; // the frame IS the content — nothing invisible to trip over
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || i == skipSlot)
                continue;
            float ov = _arcClaims[i].HalfWidthDeg + frameHalfDeg
                       - Mathf.Abs(Mathf.DeltaAngle(hostWorldYaw, _arcSeatWorldYaw[i]));
            if (ov <= 0.5f)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(_arcClaims[i].Name ?? "?").Append("' by ")
              .Append(ov.ToString("F0")).Append('°');
        }
        if (sb.Length == 0)
            return 0f;
        victims = sb.ToString();
        return FramePushStepMeters;
    }

    /// <summary>
    /// Apply an arc window's depth term to <paramref name="pos"/>: positive
    /// <paramref name="pullWorld"/> moves it TOWARD the head (the overflow rule's foreground
    /// ladder), negative moves it AWAY (the phantom-frame push).
    ///
    /// <para>IT MOVES ALONG THE WINDOW'S OWN FLATTENED RADIAL, not along the raw gaze. Writing the
    /// offset as u·d with u the rotated unit direction, its horizontal part is flatU·(h·d) with
    /// h = cos(gaze pitch); subtracting p·flatU leaves flatU·(h·d − p) — the SAME flatU, so the
    /// claimed AZIMUTH is preserved exactly — and leaves the y term untouched, so the HEIGHT is
    /// bit-for-bit what the placement produced. It is a direction and not a rotation, so pitch and
    /// roll stay zero and the yaw-only ruling is untouched.</para>
    ///
    /// <para>Bounded at 60 % of the horizontal reach in BOTH directions: a steep gaze makes that
    /// reach small, and an unbounded pull there would drag the window through the player and out
    /// the other side.</para>
    /// </summary>
    private static void ApplyArcDepth(ref Vector3 pos, Vector3 headPos, Vector3 fwd, float yawDeg,
        float scale, float pullWorld)
    {
        if (Mathf.Abs(pullWorld) <= 1e-4f)
            return;
        Vector3 radial = Quaternion.AngleAxis(yawDeg, Vector3.up) * fwd;
        radial.y = 0f;
        float horizontal = radial.magnitude * (WindowDistanceMeters * scale);
        if (radial.sqrMagnitude <= 1e-6f || horizontal <= 1e-4f)
            return;
        float bound = horizontal * 0.6f;
        pos -= radial.normalized * Mathf.Clamp(pullWorld, -bound, bound);
    }

    /// <summary>The world yaw of a direction, degrees about world up, measured from world forward.
    /// Vertical (or zero) directions have no yaw and answer 0 — every caller flattens first and
    /// checks the magnitude, so this is a guard, not a code path.</summary>
    private static float WorldYawDeg(Vector3 dir)
    {
        Vector3 flat = new Vector3(dir.x, 0f, dir.z);
        return flat.sqrMagnitude < 1e-8f
            ? 0f
            : Vector3.SignedAngle(Vector3.forward, flat, Vector3.up);
    }

    /// <summary>
    /// The world yaw the spawn gaze points along. A gaze looking straight up or down carries no yaw
    /// in its forward vector, so it is recovered from the head's own up vector (horizontal exactly
    /// then) — the same degenerate-case rule <see cref="Upright"/> applies to a rotation.
    /// </summary>
    private static float SpawnGazeYawDeg(Vector3 forward, Vector3 up)
    {
        Vector3 flat = new Vector3(forward.x, 0f, forward.z);
        if (flat.sqrMagnitude >= 1e-6f)
            return WorldYawDeg(flat);
        // Straight up/down: the yaw lives in -up (looking down) / +up (looking up); either is the
        // same axis, and the sign only decides which way "forward" faces, which a seat search
        // covering both sides of the gaze does not care about.
        Vector3 fromUp = new Vector3(-up.x, 0f, -up.z);
        return fromUp.sqrMagnitude >= 1e-6f ? WorldYawDeg(fromUp) : 0f;
    }

    /// <summary>
    /// BRING EVERY STANDING RESERVATION UP TO DATE WITH WHAT ITS WINDOW NOW DRAWS, before a new
    /// window searches for a seat. It writes only the REGISTRY: no pose, no host, no transform.
    ///
    /// <para>WHY IT IS NEEDED, from the simulated burst. A window claims at the instant it
    /// converts, when its content is not yet measurable, so it books its host FRAME. It corrects
    /// itself at its own pre-reveal re-place — but the panels in his loadout open faster than they
    /// settle, so a window searching for a seat in the middle of the burst is looking at a room
    /// half-described by stale frame claims. Simulated against his exact sequence, that alone
    /// leaves the quest popup seated on top of the story window: the party roster was still
    /// advertising 88° of arc it does not use at the moment the popup went looking, so the popup
    /// was pushed to the only place left. Re-measuring first costs one subtree walk per standing
    /// window on an event-gated path and removes the whole class of that mistake.</para>
    ///
    /// <para>IT CANNOT MOVE ANYTHING, AND THAT IS THE POINT. The host yaw each refreshed interval
    /// is rebuilt around comes from the REGISTRY (seat − held offset), not from the window's live
    /// transform. Reading the transform would be more "truthful" and is deliberately not done: a
    /// window the player has grabbed and dragged would then feed its new position back into the
    /// packer, which is a coupling nobody has asked for and which would make the layout depend on
    /// where he happens to be holding something. The window's own pose is untouched either way —
    /// only the WIDTH and the OFFSET of what it books are corrected.</para>
    /// </summary>
    private static void RefreshStandingArcClaims(int skipSlot)
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (i == skipSlot)
                continue;
            ConvertedPanel? p = _arcClaims[i].Panel;
            if (p == null || !p.IsAlive || p.HostGo == null || p.HostRect == null)
                continue;
            float dist = _arcClaims[i].DistanceWorld;
            if (dist <= 1e-4f)
                continue;
            float halfWidthWorld = p.HostRect.rect.width * 0.5f
                                   * Mathf.Abs(p.HostRect.lossyScale.x);
            if (halfWidthWorld <= 1e-4f)
                continue;
            ArcDrawnGeometry g = MeasureArcDrawnGeometry(p, halfWidthWorld, dist);
            if (!g.Measured)
                continue; // still hidden — keep the frame-sized booking, which over-reserves safely
            if (Mathf.Abs(g.DrawnHalfDeg - _arcClaims[i].HalfWidthDeg) < 0.5f
                && Mathf.Abs(g.OffsetDeg - _arcClaims[i].DrawnOffsetDeg) < 0.5f)
                continue; // already current
            // The host has NOT moved: rebuild the booked interval around the same frame centre.
            float hostYawWorld = _arcSeatWorldYaw[i] - _arcClaims[i].DrawnOffsetDeg;
            float freed = (_arcClaims[i].HalfWidthDeg - g.DrawnHalfDeg) * 2f;
            _arcSeatWorldYaw[i] = hostYawWorld + g.OffsetDeg;
            _arcClaims[i].HalfWidthDeg = g.DrawnHalfDeg;
            _arcClaims[i].DrawnOffsetDeg = g.OffsetDeg;
            _arcClaims[i].FrameHalfWidthDeg = g.FrameHalfDeg;
            VRLog.Info("WorldUI", "MAP ROOM ARC RE-MEASURED: "
                                  + $"'{_arcClaims[i].Name ?? "?"}' now books "
                                  + $"{g.DrawnHalfDeg * 2f:F0}° at world yaw "
                                  + $"{_arcSeatWorldYaw[i]:F0}° instead of what it held — "
                                  + (freed > 0.5f
                                      ? $"{freed:F0}° of arc HANDED BACK to the room"
                                      : $"{-freed:F0}° of arc TAKEN, because it draws wider than it "
                                        + "had booked and a neighbour would otherwise have been "
                                        + "seated on content that is really there")
                                  + $". {g.Note}. NOTHING MOVED: this corrects the REGISTRY only — "
                                  + "the window's pose, size and distance are untouched, and the "
                                  + "correction is visible to the NEXT window that looks for a seat.");
        }
    }

    /// <summary>Half the FRAME's angular width held by <paramref name="slot"/>, degrees (0 for a
    /// free / out-of-range index) — the collider footprint the placement line has to print beside
    /// the drawn width so a phantom frame is visible at a glance instead of derived.</summary>
    private static float ArcClaimFrameHalfWidthDeg(int slot) =>
        slot >= 0 && slot < _arcClaims.Length ? _arcClaims[slot].FrameHalfWidthDeg : 0f;

    /// <summary>Angular offset of the drawn content from the frame's centre held by
    /// <paramref name="slot"/>, degrees (0 for a free / out-of-range index).</summary>
    private static float ArcClaimDrawnOffsetDeg(int slot) =>
        slot >= 0 && slot < _arcClaims.Length ? _arcClaims[slot].DrawnOffsetDeg : 0f;

    /// <summary>True when a window of half-width <paramref name="halfAngle"/> seated at world yaw
    /// <paramref name="worldYaw"/> clears every standing seat by the neighbour gap. Wrap-safe.</summary>
    private static bool ArcSeatIsFree(float worldYaw, float halfAngle)
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float needed = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees;
            if (Mathf.Abs(Mathf.DeltaAngle(worldYaw, _arcSeatWorldYaw[i])) < needed - 1e-3f)
                return false;
        }
        return true;
    }

    /// <summary>The worst overlap in degrees between a window of half-width
    /// <paramref name="halfAngle"/> at world yaw <paramref name="worldYaw"/> and any standing seat
    /// (0 = clear of all of them), plus the name of the seat it intrudes on most.</summary>
    private static float ArcSeatWorstOverlapDeg(float worldYaw, float halfAngle, out string withName)
    {
        float worst = 0f;
        withName = "";
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float ov = _arcClaims[i].HalfWidthDeg + halfAngle
                       - Mathf.Abs(Mathf.DeltaAngle(worldYaw, _arcSeatWorldYaw[i]));
            if (ov > worst)
            {
                worst = ov;
                withName = _arcClaims[i].Name ?? "?";
            }
        }
        return worst;
    }

    /// <summary>Total angular extent of PERMANENT standing seats a window at
    /// <paramref name="worldYaw"/> would cover, summed — covering two un-closable windows is twice
    /// the harm of covering one. See <see cref="ArcClaim.Permanent"/> for why this is ranked first
    /// when the arc is full.</summary>
    private static float ArcSeatPermanentOverlapDeg(float worldYaw, float halfAngle)
    {
        float total = 0f;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || !_arcClaims[i].Permanent)
                continue;
            float ov = _arcClaims[i].HalfWidthDeg + halfAngle
                       - Mathf.Abs(Mathf.DeltaAngle(worldYaw, _arcSeatWorldYaw[i]));
            if (ov > 0f)
                total += ov;
        }
        return total;
    }

    /// <summary>Names the permanent seats this angle would cover and the degrees each loses — the
    /// full-arc line has to say WHICH un-closable window is being buried and by how much.</summary>
    private static string ArcSeatPermanentOverlapText(float worldYaw, float halfAngle)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || !_arcClaims[i].Permanent)
                continue;
            float ov = _arcClaims[i].HalfWidthDeg + halfAngle
                       - Mathf.Abs(Mathf.DeltaAngle(worldYaw, _arcSeatWorldYaw[i]));
            if (ov <= 0.5f)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(_arcClaims[i].Name ?? "?").Append("' by ")
              .Append(ov.ToString("F0")).Append("° of its ")
              .Append((_arcClaims[i].HalfWidthDeg * 2f).ToString("F0")).Append('°');
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    /// <summary>
    /// The seats currently held, each as "offset±half 'name'" relative to
    /// <paramref name="referenceYaw"/> with its absolute world yaw beside it. Both numbers are
    /// printed because they answer different questions: the offset is what the player sees from
    /// where he is standing now, the world yaw is the frame the packer actually reasons in, and a
    /// disagreement between them across two lines is exactly how a head turn shows up in the log.
    /// </summary>
    private static string ArcSeatOccupancyText(float referenceYaw)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            float off = Mathf.DeltaAngle(referenceYaw, _arcSeatWorldYaw[i]);
            sb.Append(off.ToString("F0")).Append("°±")
              .Append(_arcClaims[i].HalfWidthDeg.ToString("F0")).Append("° (world ")
              .Append(_arcSeatWorldYaw[i].ToString("F0")).Append("°) '")
              .Append(_arcClaims[i].Name ?? "?").Append('\'');
            // The frame, only when it is materially wider than the content — otherwise the two are
            // the same number and printing it twice makes the list unreadable.
            if (_arcClaims[i].FrameHalfWidthDeg - _arcClaims[i].HalfWidthDeg > 2f)
                sb.Append(" frame ±").Append(_arcClaims[i].FrameHalfWidthDeg.ToString("F0"))
                  .Append("° centred ").Append((-_arcClaims[i].DrawnOffsetDeg).ToString("F0"))
                  .Append("° off");
            if (_arcClaims[i].Permanent)
                sb.Append(" PERMANENT/no-X");
        }
        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    /// <summary>Total degrees of window the standing seats demand, plus the gaps between them —
    /// the numerator of "is this arc genuinely oversubscribed, or did the packer waste it".</summary>
    private static float ArcSeatDemandDeg(float extraWindowDeg)
    {
        float sum = extraWindowDeg;
        int n = extraWindowDeg > 0f ? 1 : 0;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            sum += _arcClaims[i].HalfWidthDeg * 2f;
            n++;
        }
        return n > 1 ? sum + NeighbourGapDegrees * (n - 1) : sum;
    }

    /// <summary>
    /// WHICH COMFORT BAND A SEAT LANDED IN, as a sentence — the reading cone's remaining job.
    /// Every bound in it is measured off this session's projection matrix, never assumed.
    /// </summary>
    private static string ArcSeatBand(float offsetDeg, float halfAngle)
    {
        float cone = UsableHalfConeDeg();
        // The raw binocular overlap, i.e. the cone before the comfort margin: the outer edge of
        // "both eyes see it without moving". Recovered rather than re-derived so the two numbers
        // can never disagree about which matrix they came from.
        float binocular = cone / Mathf.Max(ViewConeComfortFraction, 1e-3f);
        float reach = Mathf.Abs(offsetDeg) + halfAngle;
        if (reach <= cone + 0.5f)
            return $"COMFORT BAND: its edges reach {reach:F0}°, inside the measured comfortable "
                   + $"reading cone (±{cone:F1}°) — readable with the head still";
        if (reach <= binocular + 0.5f)
            return $"IN VIEW: its edges reach {reach:F0}°, past the comfortable cone (±{cone:F1}°) "
                   + $"but inside the measured binocular overlap (±{binocular:F1}°) — on screen "
                   + "with the head still, at the edge of comfort";
        return $"HEAD TURN: its edges reach {reach:F0}°, past the measured binocular overlap "
               + $"(±{binocular:F1}°) — the player turns toward it, which is free and which is the "
               + "price of the half circle. It is IN FRONT of him (the arc is ±"
               + $"{HalfCircleHalfDeg:F0}°, edges included) and it is not buried, which is the trade "
               + "he asked for";
    }

    /// <summary>
    /// States the room's layout rule ONCE per session, so a hardware log can be read without any
    /// constant to hand. Called from the first claim, i.e. once the head camera exists — asking
    /// earlier would only record the cone fallback.
    /// </summary>
    private static void LogArcSeatGeometryOnce()
    {
        if (_arcSeatGeometryLogged)
            return;
        float cone = UsableHalfConeDeg();
        if (float.IsNaN(_usableHalfConeDeg))
            return; // still on the fallback: say nothing yet, retry when the rig is up
        _arcSeatGeometryLogged = true;
        float binocular = cone / Mathf.Max(ViewConeComfortFraction, 1e-3f);
        float legibility = WindowLegibilityLive();
        float fullWidthDeg = 2f * Mathf.Atan2(ModalTargetWidthMeters * legibility * 0.5f,
            WindowDistanceMeters) * Mathf.Rad2Deg;
        int fullWidthFit = fullWidthDeg > 0.1f
            ? Mathf.Max(1, Mathf.FloorToInt((2f * HalfCircleHalfDeg + NeighbourGapDegrees)
                                            / (fullWidthDeg + NeighbourGapDegrees)))
            : 0;
        VRLog.Info("WorldUI", "MAP ROOM ARC GEOMETRY: windows are seated on a HALF CIRCLE in front "
                              + $"of the player — ±{HalfCircleHalfDeg:F0}° of world yaw off the "
                              + "spawn gaze, applied to each window's EDGES, so nothing is ever "
                              + "placed behind the shoulder line (user request: 'es soll nicht "
                              + "alles auf einem Fleck spawnen sondern am Besten in einem halbkreis "
                              + "innerhalb des sichtbereichs ausgerichtet'). SIZE AND PLACEMENT ARE "
                              + "SEPARATE QUESTIONS AND THIS LINE CARRIES BOTH ANSWERS. Placement: "
                              + $"the ±{HalfCircleHalfDeg:F0}° arc above, filled NEAREST THE GAZE "
                              + "FIRST, so the outer arc is reached only when everything closer in "
                              + "is occupied. Comfort: the measured reading cone is ±"
                              + $"{cone:F1}° and the measured binocular overlap is ±{binocular:F1}°, "
                              + $"derived from {_coneSource} — every placement line grades itself "
                              + "COMFORT BAND / IN VIEW / HEAD TURN against those two. For scale: a "
                              + $"full-width 1920 px window is {ModalTargetWidthMeters * legibility:F2} m "
                              + $"across at the {WindowDistanceMeters:F2} m reading distance = "
                              + $"{fullWidthDeg:F0}° of view, so at most {fullWidthFit} of THOSE fit "
                              + $"in the half circle; only {Mathf.Max(1, Mathf.FloorToInt((2f * cone + NeighbourGapDegrees) / (fullWidthDeg + NeighbourGapDegrees)))} "
                              + "fit inside the comfortable cone, which is why placement no longer "
                              + "uses it. Neighbours are kept "
                              + $"{NeighbourGapDegrees:F0}° apart. WHEN THE HALF CIRCLE IS FULL the "
                              + "next window is placed inside it anyway and overlaps — it is never "
                              + "pushed behind the player and never hidden — at the angle that "
                              + "buries the least PERMANENT (un-closable) window surface, then the "
                              + "angle furthest from every neighbour, and it is pulled "
                              + $"{OverlapDepthStepMeters:F2} m nearer per overlap generation so it "
                              + $"draws in front, never nearer than {MinOverlapDistanceMeters:F2} m. "
                              + $"Capacity {MaxWindowClaims} seats. A window claims ONCE at spawn "
                              + "and keeps its angle until it stops floating; opening or closing a "
                              + "window never moves any other window (user ruling: 'einmal "
                              + "gespawned sind sie fix'), and a window the player has grabbed is "
                              + "never re-placed at all.");
    }

    /// <summary>
    /// Claim (or re-find) this window's seat on the half circle. Called from
    /// <see cref="ComputeHmdPose"/> — spawn and presence-regain refloat only, NEVER per frame.
    ///
    /// <para>THE CHOICE RULE — THE FREE INTERVAL NEAREST THE CURRENT GAZE. The candidates are the
    /// gaze itself plus, for every standing seat, the two angles that put this window exactly
    /// against that seat's left and right edge. One of those is always the optimum: the
    /// nearest-to-gaze feasible position is either the gaze itself or flush against something, so
    /// testing 1 + 2N angles finds it exactly, with no stepping and no search tolerance. The
    /// smallest |offset| that is inside the arc and clear of everything wins; a tie between the two
    /// sides goes RIGHT, the side every build since 183 has filled first.</para>
    ///
    /// <para>LATE ARRIVALS TAKE A FREE SEAT AND NOTHING RESHUFFLES. A window that opens ten seconds
    /// after the others runs this same search against whatever is standing at that moment and takes
    /// the nearest free interval — which is the correct behaviour for the burst he photographed,
    /// where the panels open over several seconds rather than in one frame. The windows already
    /// standing are NOT re-packed to make a better global layout: re-placing a window that has
    /// already been revealed is a visible jump, it is what ModBuild 183 shipped and what the
    /// standing ruling forbids ("ohne explizite Bewegung vom User, sollen sie ihre Position nicht
    /// verändern"), and it would move a window the player may have deliberately put where it is.
    /// The cost of that choice is fragmentation — five windows opened in a bad order can leave a
    /// hole nothing later fits into — and the audit line prints the hole, so the cost is visible
    /// instead of theoretical.</para>
    ///
    /// <para>WHEN THE HALF CIRCLE IS FULL — a stated policy, not an accident. The window is seated
    /// INSIDE the arc and allowed to overlap. It is never pushed behind the player and never
    /// deferred: "im Sichtfeld" is unconditional and not overlapping is "möglichst". The angle
    /// chosen buries the least PERMANENT surface first (a covered closable window costs one press
    /// of its X; a covered permanent one has no exit — ModBuild 194), then maximises the distance
    /// to every neighbour so each window still shows a readable strip, and the window is pulled one
    /// depth generation nearer so it draws in FRONT of what it covers rather than merging with it.
    /// The line says so, names the victims, and prints how many degrees of window were asked of the
    /// 180° available so the reader can tell an oversubscribed room from a packing mistake.</para>
    ///
    /// <para>EXCLUSIONS, unchanged from ModBuild 193: no arc outside the map room; a HOVER CARD
    /// never claims (<c>TickHoverCards</c> owns its pose and it churns on every mouseover); a LEVEL
    /// MESSAGE never claims (chain-continuity ruling); the global error box never claims (it is not
    /// a <see cref="Converted"/> window, so nothing would ever release its seat).</para>
    /// </summary>
    /// <param name="halfSizeWorld">The window's half-extent in WORLD units; only x (half-WIDTH) is
    /// read here.</param>
    /// <param name="scale">Diorama scale (world units per real metre) at this spawn.</param>
    /// <param name="gazeYawDeg">World yaw of this spawn's gaze — the frame the search is centred
    /// on and the frame the returned offset is expressed in.</param>
    /// <param name="slot">The claimed registry index, or −1 when the registry is full.</param>
    /// <param name="yawDeg">Degrees to rotate the spawn gaze by, + = right.</param>
    /// <param name="overlapRank">0 = it got a free interval; ≥1 = the k-th overlapping window,
    /// which is also its depth-ladder index.</param>
    /// <param name="foregroundPullWorld">World units to pull the window toward the head along the
    /// FLATTENED forward (y = 0, so the placement's height is untouched). 0 unless overlapping.</param>
    /// <param name="why">Human-readable reason for the log line.</param>
    /// <returns>true when the map room's arc governs this placement.</returns>
    private static bool TryClaimArcSeat(ConvertedPanel? panel, bool levelMessage,
        Vector2 halfSizeWorld, float scale, float gazeYawDeg,
        out int slot, out float yawDeg, out int overlapRank, out float foregroundPullWorld,
        out string why)
    {
        slot = -1;
        yawDeg = 0f;
        overlapRank = 0;
        foregroundPullWorld = 0f;
        why = "";
        if (panel == null || levelMessage || !MapRoom.MapRoomDriver.Active)
            return false;
        if (ReferenceEquals(panel, _errorPanel))
            return false; // not a Converted window — no release path would ever free its seat
        if (IsHoverCardPanel(panel))
            return false; // TickHoverCards owns its pose (and it churns on every mouseover)

        LogArcSeatGeometryOnce();

        // Already holds one? A presence-regain refloat re-places an EXISTING float and must land
        // back on ITS OWN WORLD DIRECTION — not on the same gaze-relative angle, which after a
        // doff/don with a turned head would be a different place in the room.
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (!ReferenceEquals(_arcClaims[i].Panel, panel))
                continue;
            slot = i;
            // The registry seats the DRAWN centre; the placement positions the HOST rect, so the
            // content's own offset inside its frame comes back off here.
            float hostYawWorld = _arcSeatWorldYaw[i] - _arcClaims[i].DrawnOffsetDeg;
            yawDeg = Mathf.DeltaAngle(gazeYawDeg, hostYawWorld);
            overlapRank = _arcClaims[i].OverlapRank;
            foregroundPullWorld = _arcClaims[i].DepthPullMeters * scale;
            why = $"re-uses the seat it already holds (drawn content at world yaw "
                  + $"{_arcSeatWorldYaw[i]:F0}°±{_arcClaims[i].HalfWidthDeg:F0}°, frame centred "
                  + $"{_arcClaims[i].DrawnOffsetDeg:F0}° off that, so the host goes to "
                  + $"{yawDeg:F0}° off the gaze this time) — a refloat must not take a second seat, "
                  + "and it returns to the same place in the ROOM even if the player has turned "
                  + "since";
            return true;
        }

        float nominalDist = WindowDistanceMeters * scale;  // WORLD units, both operands scaled
        float halfWidthWorld = halfSizeWorld.x > 1e-4f
            ? halfSizeWorld.x
            : FallbackHalfWidthWorld(scale);
        ArcDrawnGeometry geo = MeasureArcDrawnGeometry(panel, halfWidthWorld, nominalDist);
        // Everything already standing describes itself truthfully BEFORE this window looks for a
        // seat — see RefreshStandingArcClaims. It writes no pose; it only stops a stale frame-sized
        // booking from pushing this window into a corner it does not need.
        RefreshStandingArcClaims(-1);
        CountArcClaims(out int cleanBefore, out int overlapBefore);
        string standing = ArcSeatOccupancyText(gazeYawDeg);

        // THE ARC IS PACKED ON WHAT THE WINDOW DRAWS, NOT ON WHAT ITS FRAME SPANS (ModBuild 234).
        // `halfAngle` is the DRAWN half-width and every candidate below is a position for the DRAWN
        // CENTRE; the host rect the placement actually positions is recovered at the end by
        // subtracting the content's offset inside its own frame. Booking the frame is what let one
        // window reserve 88° of a 180° arc for a 14° column — see the header.
        float halfAngle = geo.DrawnHalfDeg;

        // ---- (a) IN FRONT OF THE PLAYER, ALWAYS: the centre bound that keeps the EDGES inside the
        //          half circle. A window wider than the whole half circle (nothing this room has
        //          ever produced) collapses this to 0 and is seated on the gaze.
        float centreLimit = HalfCircleHalfDeg - halfAngle;
        bool widerThanArc = centreLimit < 0f;
        if (widerThanArc)
            centreLimit = 0f;

        // ---- (b) NOT OVERLAPPING, IF POSSIBLE: the free interval nearest the gaze.
        int candidateCount = 0;
        _arcCandidates[candidateCount++] = 0f;
        for (int i = 0; i < _arcClaims.Length && candidateCount + 1 < _arcCandidates.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float standOffset = Mathf.DeltaAngle(gazeYawDeg, _arcSeatWorldYaw[i]);
            float edge = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees;
            _arcCandidates[candidateCount++] = standOffset + edge;
            _arcCandidates[candidateCount++] = standOffset - edge;
        }
        bool haveFree = false;
        float bestFree = 0f;
        for (int c = 0; c < candidateCount; c++)
        {
            float a = _arcCandidates[c];
            if (Mathf.Abs(a) > centreLimit + 1e-3f)
                continue;
            if (!ArcSeatIsFree(gazeYawDeg + a, halfAngle))
                continue;
            // Nearest the gaze wins; a tie between the two sides of a step goes RIGHT (+).
            if (!haveFree
                || Mathf.Abs(a) < Mathf.Abs(bestFree) - 1e-3f
                || (Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(bestFree)) <= 1e-3f && a > bestFree))
            {
                haveFree = true;
                bestFree = a;
            }
        }

        // The chosen position of the DRAWN CENTRE, degrees off the spawn gaze. `yawDeg` (the HOST
        // rect's angle, which is what the placement rotates to) is derived from it below.
        float seatOffset;

        if (haveFree)
        {
            seatOffset = bestFree;
            overlapRank = 0;
            foregroundPullWorld = 0f;
            why = cleanBefore + overlapBefore == 0
                ? $"the room was empty, so it took the gaze itself; the window draws {halfAngle * 2f:F0}° "
                  + $"wide and the half circle is ±{HalfCircleHalfDeg:F0}°"
                : $"the FREE INTERVAL NEAREST THE GAZE ({halfAngle * 2f:F0}°-wide DRAWN content, "
                  + $"seated at {seatOffset:F0}°±{halfAngle:F0}° inside the "
                  + $"±{HalfCircleHalfDeg:F0}° half circle with a {NeighbourGapDegrees:F0}° gap) — "
                  + "it does NOT overlap anything, and the windows already standing "
                  + $"[{standing}] were not touched";
        }
        else
        {
            // ---- THE HALF CIRCLE IS FULL. In front of him wins; overlap is the price, and it is
            //      stated, ranked and measured rather than allowed to happen.
            overlapRank = overlapBefore + 1;
            foregroundPullWorld = OverlapPullWorld(overlapRank, scale);
            float pulledDist = nominalDist - foregroundPullWorld;
            // The pull makes the window ANGULARLY WIDER (it is nearer), so the arc bound and the
            // reserved interval are both re-derived at the distance it will actually hang at —
            // width AND the content's offset inside its frame, which grows the same way.
            geo.ReDeriveAt(pulledDist);
            halfAngle = geo.DrawnHalfDeg;
            widerThanArc = HalfCircleHalfDeg - halfAngle < 0f;
            centreLimit = Mathf.Max(0f, HalfCircleHalfDeg - halfAngle);
            seatOffset = ArcSeatSpreadDeg(gazeYawDeg, centreLimit, halfAngle);
            float pullMeters = foregroundPullWorld / Mathf.Max(scale, 1e-4f);
            float pulledMeters = pulledDist / Mathf.Max(scale, 1e-4f);
            float demand = ArcSeatDemandDeg(halfAngle * 2f);
            why = $"THE HALF CIRCLE IS FULL — no free interval is left anywhere inside "
                  + $"±{HalfCircleHalfDeg:F0}°. This window is {halfAngle * 2f:F0}° wide and the "
                  + $"standing set is [{standing}]; together they ask for {demand:F0}° of the "
                  + $"{2f * HalfCircleHalfDeg:F0}° the half circle has, so this is "
                  + (demand > 2f * HalfCircleHalfDeg
                      ? "GEOMETRY, NOT A PACKING MISTAKE: the windows open in this room are wider "
                        + "than a half circle and something must overlap"
                      : "FRAGMENTATION: the total would fit, but the free space is in pieces too "
                        + "small for this window and standing windows are never re-packed (a "
                        + "re-place of a revealed window is a visible jump, user ruling)")
                  + $". THE STATED POLICY THEN APPLIES: it is seated INSIDE the half circle anyway "
                  + "and overlaps — never behind the player, never withheld — at the angle that "
                  + "buries the least PERMANENT window surface, then the angle furthest from every "
                  + $"neighbour, and pulled {pullMeters:F2} m nearer (overlap generation "
                  + $"{overlapRank}, reading distance {pulledMeters:F2} m) so it draws IN FRONT of "
                  + "what it covers instead of merging with it";
        }

        // THE SEAT IS THE DRAWN CENTRE; THE PLACEMENT POSITIONS THE HOST RECT. Subtracting the
        // content's own offset inside its frame is what makes the two agree — the visible column
        // lands in the interval that was booked for it, not the middle of an empty sheet.
        float worldYaw = gazeYawDeg + seatOffset;          // drawn centre, world
        float hostWorldYaw = worldYaw - geo.OffsetDeg;     // host rect centre, world
        yawDeg = Mathf.DeltaAngle(gazeYawDeg, hostWorldYaw);

        // DEPTH IS DECIDED BY WHAT THE WINDOW FRAMES, ANGLE BY WHAT IT DRAWS. A window whose
        // transparent frame hangs over a neighbour's visible content is pushed one step AWAY, so
        // the neighbour's plane is unambiguously nearer and keeps the laser (RayUguiDriver resolves
        // competing canvases by nearest plane). Applied ONLY when this window got a free interval:
        // an overflowing window is deliberately pulled FORWARD so it draws over what it covers, and
        // the two would otherwise cancel. The interval booked below is the one measured at the
        // NOMINAL distance, i.e. slightly wider than the pushed window really is — over-reserving
        // is the safe direction, it can only cost the NEXT window a degree it did not need.
        string pushVictims = "(none)";
        if (overlapRank == 0)
        {
            float push = ArcSeatFramePush(hostWorldYaw, geo.FrameHalfDeg, halfAngle, -1,
                out pushVictims);
            if (push > 0f)
            {
                foregroundPullWorld = -push * scale;
                why += $". PHANTOM-FRAME DEPTH PUSH: its {geo.FrameHalfDeg * 2f:F0}° frame is "
                       + $"{(geo.FrameHalfDeg - halfAngle) * 2f:F0}° wider than the "
                       + $"{halfAngle * 2f:F0}° it draws and that empty frame hangs over "
                       + $"{pushVictims}. The hit rect a laser is tested against never shrinks "
                       + "below the frame, so the window is seated "
                       + $"{push:F2} m FURTHER AWAY ({WindowDistanceMeters + push:F2} m instead of "
                       + $"{WindowDistanceMeters:F2} m, {push / WindowDistanceMeters * 100f:F0}% "
                       + "smaller) — the neighbours keep their distance and their clicks, and only "
                       + "the window with the oversized frame moves";
            }
        }

        if (widerThanArc)
        {
            why += $". NOTE: this window is WIDER than the whole half circle ({halfAngle * 2f:F0}° "
                   + $"vs ±{HalfCircleHalfDeg:F0}°), so it is centred on the gaze and its edges "
                   + $"reach {halfAngle - HalfCircleHalfDeg:F0}° behind the shoulder line each side "
                   + "no matter where it is put — nothing can be done about that from here; it has "
                   + "to be narrower or further away";
        }

        // Graded on the DRAWN interval: the band answers "can he read it without moving", and what
        // he reads is the content, not the frame.
        why += ". " + ArcSeatBand(seatOffset, halfAngle);
        why += ". GEOMETRY: " + geo.Note;

        float overlapDeg = ArcSeatWorstOverlapDeg(worldYaw, halfAngle, out string overlapWith);
        if (overlapDeg > 0.5f)
            why += $". MEASURED: it overlaps '{overlapWith}' by {overlapDeg:F0}° of the "
                   + $"{halfAngle * 2f:F0}° it spans";
        float permOverlap = ArcSeatPermanentOverlapDeg(worldYaw, halfAngle);
        if (permOverlap > 0.5f)
        {
            // ModBuild 194's lesson, kept verbatim: an overlap on a closable window is a press of
            // its X; an overlap on a permanent one has no exit at all, and the report that followed
            // the last time this log did not distinguish them cost him a whole test round.
            why += $". PERMANENT WINDOWS COVERED: {ArcSeatPermanentOverlapText(worldYaw, halfAngle)} "
                   + "— those windows have NO X and the player can neither close nor dismiss them "
                   + "(standing ruling), so this overlap is not one he can clear the way he clears "
                   + "any other. The packer already chose the angle that covers the LEAST permanent "
                   + "surface; what is left is geometry, not a placement mistake";
            float fitAt = DistanceThatWouldFitMeters(halfWidthWorld, scale, HalfCircleHalfDeg);
            why += fitAt > 0f
                ? $". THE TRADE, MEASURED: this window and the widest permanent one would BOTH fit "
                  + $"inside the ±{HalfCircleHalfDeg:F0}° half circle at a reading distance of "
                  + $"{fitAt:F2} m instead of {WindowDistanceMeters:F2} m — that is "
                  + $"{(1f - WindowDistanceMeters / fitAt) * 100f:F0}% less apparent size. "
                  + "WindowDistanceMeters is a tuned, accepted value and is NOT changed here; this "
                  + "line exists so the choice can be made on the number rather than on a guess"
                : ". THE TRADE, MEASURED: no reading distance up to 4.00 m makes this window and "
                  + "the widest permanent one both fit inside the half circle — the pair is wider "
                  + "than 180° of arc, and only a NARROWER window (a tighter content fit) can "
                  + "change that";
        }
        if (overlapDeg <= 0.5f && overlapRank > 0)
            why += $". MEASURED: it does NOT actually overlap anything after all — it only failed "
                   + $"to keep the full {NeighbourGapDegrees:F0}° breathing gap, so it was routed "
                   + "through the overlap rule and carries its depth offset. The windows are edge "
                   + "to edge, not on top of each other";

        int free = FirstFreeClaimIndex();
        if (free < 0)
        {
            // Registry full: still seated, still in front of him, but holding no reservation — so a
            // later window may land on the same angle, and the log says so rather than quietly
            // aliasing two windows onto one seat.
            slot = -1;
            why += $". THE REGISTRY IS FULL ({MaxWindowClaims} seats) — this window holds NONE, so "
                   + "a later window may land on the same angle. It is still in front of the player "
                   + "and grabbable; close a window to free a seat";
            _arcSeatGeneration++;
            return true;
        }

        slot = free;
        _arcSeatWorldYaw[free] = worldYaw;   // the DRAWN centre — what the arc is packed on
        _arcClaims[free] = new ArcClaim
        {
            Panel = panel,
            Name = PanelLogName(panel),
            // Documented meaning kept: degrees from THIS window's own spawn gaze. The packer reasons
            // in _arcSeatWorldYaw; this is what the release/occupancy lines in
            // ModalFallback.4.Tick.cs print, and they say "from its spawn gaze".
            CentreDeg = seatOffset,
            HalfWidthDeg = halfAngle,
            DrawnOffsetDeg = geo.OffsetDeg,
            FrameHalfWidthDeg = geo.FrameHalfDeg,
            DistanceWorld = nominalDist - foregroundPullWorld,
            OverlapRank = overlapRank,
            DepthPullMeters = foregroundPullWorld / Mathf.Max(scale, 1e-4f),
            OverlapDeg = overlapDeg,
            Permanent = IsPermanentPanel(panel),
        };
        _arcSeatGeneration++;
        return true;
    }

    /// <summary>
    /// RE-SEAT a window on its MEASURED DRAWN CONTENT, at the one moment that is free: the
    /// pre-reveal re-place. Returns false when nothing changed, and the caller then replays exactly
    /// as before.
    ///
    /// <para>WHY HERE AND NOWHERE ELSE. A reservation is claimed the instant a window converts, and
    /// at that instant the window is still behind the reveal gate — no graphic passes the
    /// visibility test, so the drawn extent is not measurable and the claim falls back to the host
    /// rect. It becomes measurable a few frames later, and the ordering is not a guess: in his
    /// ModBuild 233 log the party roster's fit APPLIES at line 15935, this re-place logs at 15939,
    /// its first hit-rect measurement finds 135 visible graphics at 15943, and the window is
    /// REVEALED at 15952. So there is a window of frames in which the content is measurable and the
    /// player cannot see the panel, and this is it. A move here is invisible by construction —
    /// which is the same premise <c>TickPoseRePlace</c> was built on.</para>
    ///
    /// <para>WHY IT RE-SEATS RATHER THAN ONLY NARROWING. ModBuild 234's first cut only shrank the
    /// booked interval and left the window where the frame-sized claim had put it. Simulated
    /// against his own burst that fixes nothing he can see: the panels open faster than the fit
    /// settles, so every window in the burst has already claimed and been placed before the first
    /// re-place runs — the freed arc arrives after the last window that could have used it. Only a
    /// re-seat moves the window itself onto the interval its content actually needs.</para>
    ///
    /// <para>IT MOVES EXACTLY ONE WINDOW — THIS ONE. Its own seat is released before the search so
    /// it cannot see itself as an obstacle (the fuse-that-counted-the-player lesson), and no other
    /// registry entry is read for anything but collision. Nothing standing is re-packed, nothing
    /// revealed is touched: the caller has already refused this path for a grabbed window
    /// (<c>grab.IsGrabbed</c>) and for a revealed one (<c>!RevealPending</c>).</para>
    /// </summary>
    private static bool TryReseatArcClaimOnDrawnContent(ConvertedPanel? panel, int slot,
        Vector2 halfSizeWorld, float scale, float gazeYawDeg,
        out float hostYawDeg, out int overlapRank, out float foregroundPullWorld, out string note)
    {
        hostYawDeg = 0f;
        overlapRank = 0;
        foregroundPullWorld = 0f;
        note = "";
        if (panel == null || slot < 0 || slot >= _arcClaims.Length)
            return false;
        if (!ReferenceEquals(_arcClaims[slot].Panel, panel))
            return false; // the seat was released and re-taken — do not write someone else's entry

        float nominalDist = WindowDistanceMeters * scale;
        float halfWidthWorld = halfSizeWorld.x > 1e-4f
            ? halfSizeWorld.x
            : FallbackHalfWidthWorld(scale);
        ArcDrawnGeometry geo = MeasureArcDrawnGeometry(panel, halfWidthWorld, nominalDist);
        if (!geo.Measured)
            return false; // still nothing visible — keep the frame-sized claim and the spawn pose

        float heldHalf = _arcClaims[slot].HalfWidthDeg;
        float heldOffset = _arcClaims[slot].DrawnOffsetDeg;
        // Nothing material changed? Then do not write a pose at all — a re-place that lands on the
        // same numbers is a write nobody needs (the "found IDENTICAL, nothing written" rule).
        if (Mathf.Abs(geo.DrawnHalfDeg - heldHalf) < 0.5f
            && Mathf.Abs(geo.OffsetDeg - heldOffset) < 0.5f)
            return false;

        // Everything else describes itself truthfully first (no pose is written by this), then our
        // own seat is released so the search cannot collide with the window it is re-seating.
        RefreshStandingArcClaims(slot);
        ArcClaim held = _arcClaims[slot];
        _arcClaims[slot] = default;

        float halfAngle = geo.DrawnHalfDeg;
        CountArcClaims(out _, out int overlapBefore);
        string standing = ArcSeatOccupancyText(gazeYawDeg);

        float centreLimit = Mathf.Max(0f, HalfCircleHalfDeg - halfAngle);
        int candidateCount = 0;
        _arcCandidates[candidateCount++] = 0f;
        for (int i = 0; i < _arcClaims.Length && candidateCount + 1 < _arcCandidates.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float standOffset = Mathf.DeltaAngle(gazeYawDeg, _arcSeatWorldYaw[i]);
            float edge = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees;
            _arcCandidates[candidateCount++] = standOffset + edge;
            _arcCandidates[candidateCount++] = standOffset - edge;
        }
        bool haveFree = false;
        float bestFree = 0f;
        for (int c = 0; c < candidateCount; c++)
        {
            float a = _arcCandidates[c];
            if (Mathf.Abs(a) > centreLimit + 1e-3f)
                continue;
            if (!ArcSeatIsFree(gazeYawDeg + a, halfAngle))
                continue;
            if (!haveFree
                || Mathf.Abs(a) < Mathf.Abs(bestFree) - 1e-3f
                || (Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(bestFree)) <= 1e-3f && a > bestFree))
            {
                haveFree = true;
                bestFree = a;
            }
        }

        float seatOffset;
        string how;
        if (haveFree)
        {
            seatOffset = bestFree;
            overlapRank = 0;
            foregroundPullWorld = 0f;
            how = $"took the FREE INTERVAL NEAREST THE GAZE at {seatOffset:F0}°±{halfAngle:F0}° "
                  + $"(standing set [{standing}]) and overlaps nothing";
        }
        else
        {
            overlapRank = overlapBefore + 1;
            foregroundPullWorld = OverlapPullWorld(overlapRank, scale);
            geo.ReDeriveAt(nominalDist - foregroundPullWorld);
            halfAngle = geo.DrawnHalfDeg;
            centreLimit = Mathf.Max(0f, HalfCircleHalfDeg - halfAngle);
            seatOffset = ArcSeatSpreadDeg(gazeYawDeg, centreLimit, halfAngle);
            how = "found THE HALF CIRCLE FULL even at its true drawn width, so it takes the "
                  + $"least-harmful overlapping angle {seatOffset:F0}° (standing set [{standing}])";
        }

        float worldYaw = gazeYawDeg + seatOffset;
        float hostWorldYaw = worldYaw - geo.OffsetDeg;
        hostYawDeg = Mathf.DeltaAngle(gazeYawDeg, hostWorldYaw);

        string pushVictims = "(none)";
        float pushNote = 0f;
        if (overlapRank == 0)
        {
            float push = ArcSeatFramePush(hostWorldYaw, geo.FrameHalfDeg, halfAngle, slot,
                out pushVictims);
            if (push > 0f)
            {
                pushNote = push;
                foregroundPullWorld = -push * scale;
            }
        }

        float overlapDeg = ArcSeatWorstOverlapDeg(worldYaw, halfAngle, out string overlapWith);
        _arcSeatWorldYaw[slot] = worldYaw;
        held.CentreDeg = seatOffset;
        held.HalfWidthDeg = halfAngle;
        held.DrawnOffsetDeg = geo.OffsetDeg;
        held.FrameHalfWidthDeg = geo.FrameHalfDeg;
        held.DistanceWorld = nominalDist - foregroundPullWorld;
        held.OverlapRank = overlapRank;
        held.DepthPullMeters = foregroundPullWorld / Mathf.Max(scale, 1e-4f);
        held.OverlapDeg = overlapDeg;
        _arcClaims[slot] = held;

        note = $"RE-SEATED ON ITS MEASURED DRAWN CONTENT while still render-hidden. It had reserved "
               + $"±{heldHalf:F0}° at offset {heldOffset:F0}° (the pre-fit frame, the only thing "
               + $"measurable at spawn); it actually draws ±{halfAngle:F0}° at offset "
               + $"{geo.OffsetDeg:F0}°, so it {how}. The host rect goes to {hostYawDeg:F0}° off the "
               + $"gaze so that the CONTENT lands on {seatOffset:F0}°. {geo.Note}"
               + (pushNote > 0f
                   ? $". PHANTOM-FRAME DEPTH PUSH: its empty frame hangs over {pushVictims}, so it "
                     + $"is seated {pushNote:F2} m further away and they keep the laser"
                   : "")
               + (overlapDeg > 0.5f
                   ? $". MEASURED: it still overlaps '{overlapWith}' by {overlapDeg:F0}°"
                   : ". MEASURED: it overlaps nothing")
               + ". No other window was read for anything but collision and none was moved";
        return true;
    }

    /// <summary>
    /// The in-arc angle for a window that cannot avoid overlapping — the overflow remedy, in world
    /// yaw. Sampled rather than solved because the objective has its optimum at an endpoint or a
    /// midpoint and a ~1° sweep finds it to within half a degree, far below anything the eye can
    /// judge; it runs once per spawn, never per frame. With nothing standing it answers 0°.
    ///
    /// <para>THE RANKING, in order: (1) LEAST PERMANENT SURFACE COVERED, because a covered
    /// un-closable window has no exit and a covered closable one costs a press of its X (ModBuild
    /// 194 — the merchant over the character screen); (2) the largest minimum distance to any
    /// standing centre, so each window still shows a readable strip of itself; (3) nearest the
    /// gaze; (4) RIGHT, so the ±limit tie is not settled by which end the sweep started at.</para>
    ///
    /// <para>The sweep is bounded at 241 samples, so widening the arc from ±32° to ±90° raises the
    /// step from ~0.3° to ~0.75° rather than the iteration count.</para>
    /// </summary>
    private static float ArcSeatSpreadDeg(float gazeYawDeg, float centreLimit, float halfAngle)
    {
        if (centreLimit <= 0.5f)
            return 0f;
        int samples = Mathf.Clamp(Mathf.CeilToInt(centreLimit * 2f) + 1, 3, 241);
        float best = 0f;
        float bestScore = -1f;
        float bestPerm = float.MaxValue;
        for (int s = 0; s < samples; s++)
        {
            float a = Mathf.Lerp(-centreLimit, centreLimit, s / (float)(samples - 1));
            float world = gazeYawDeg + a;
            float score = float.MaxValue;
            bool any = false;
            for (int i = 0; i < _arcClaims.Length; i++)
            {
                if (_arcClaims[i].Panel == null)
                    continue;
                any = true;
                score = Mathf.Min(score, Mathf.Abs(Mathf.DeltaAngle(world, _arcSeatWorldYaw[i])));
            }
            if (!any)
                return 0f;
            float perm = ArcSeatPermanentOverlapDeg(world, halfAngle);

            // (1) Less PERMANENT surface covered always wins, with 0.5° of slack so a rounding-level
            //     difference cannot override the separation rule the player actually sees.
            bool permTied = Mathf.Abs(perm - bestPerm) <= 0.5f;
            bool permBetter = perm < bestPerm - 0.5f;
            bool tied = Mathf.Abs(score - bestScore) <= 0.01f;
            bool better = permBetter
                          || (permTied
                              && (score > bestScore + 0.01f
                                  || (tied && Mathf.Abs(a) < Mathf.Abs(best) - 1e-3f)
                                  || (tied && Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(best)) <= 1e-3f
                                      && a > best)));
            if (better)
            {
                bestScore = score;
                bestPerm = perm;
                best = a;
            }
        }
        return best;
    }

    /// <summary>
    /// THE FALSIFIER. One line, measured LIVE off the standing windows' own transforms, saying
    /// whether any two of them overlap in angle.
    ///
    /// <para>WHY IT DOES NOT READ THE REGISTRY. The registry is what the packer BELIEVES; this line
    /// has to be able to contradict it. Every number here comes from the world instead: each
    /// window's host transform position gives its direction from the head and its distance, and its
    /// live <c>HostRect</c> width times its live lossy scale gives its true world half-width, so
    /// the interval printed is the one the player is looking at. A run in which four windows report
    /// the same or overlapping intervals is a run in which this change did not work — and if the
    /// live intervals are clean while the reserved ones are not (or the reverse), the line prints
    /// both and the disagreement is the lead.</para>
    ///
    /// <para>WHEN IT PRINTS. At the end of every arc-governed placement, spawn and pre-reveal
    /// re-place alike, tagged with <see cref="_arcSeatGeneration"/>. A burst therefore produces one
    /// line per window and the LAST of them is the settled state of the room; a late arrival
    /// produces one more. It never runs per frame.</para>
    ///
    /// <para>Diagnostics only, and on the spawn path: nothing here writes, and the whole body is
    /// guarded so a half-destroyed panel can never propagate an exception into a placement.</para>
    /// </summary>
    /// <param name="headPos">The head this placement measured from — the apex every angle is
    /// taken at.</param>
    /// <param name="trigger">What produced this audit, for the log line.</param>
    /// <param name="pending">The window whose pose has just been COMPUTED but not yet written by
    /// the caller (<c>ComputeHmdPose</c> returns a pose; <c>PlaceAtHmd</c> / the re-place write
    /// it). Its transform still carries the previous position, so measuring it would report the
    /// room one window out of date — exactly on the line whose job is to say whether the newest
    /// window landed clear. <paramref name="pendingPos"/> is substituted for it instead.</param>
    /// <param name="pendingPos">The pose about to be written for <paramref name="pending"/>.</param>
    private static void LogArcOverlapAudit(Vector3 headPos, string trigger,
        ConvertedPanel? pending, Vector3 pendingPos)
    {
        try
        {
            // Live measurement, one pass: direction, distance and true world width per window.
            string[] names = _auditNames;
            float[] liveYaw = _auditLiveYaw;
            float[] liveHalf = _auditLiveHalf;
            float[] reservedYaw = _auditReservedYaw;
            float[] reservedHalf = _auditReservedHalf;
            float[] liveFrameYaw = _auditFrameYaw;
            float[] liveFrameHalf = _auditFrameHalf;
            float[] liveDist = _auditDist;
            int n = 0;
            for (int i = 0; i < _arcClaims.Length; i++)
            {
                ConvertedPanel? p = _arcClaims[i].Panel;
                if (p == null || !p.IsAlive || p.HostGo == null || p.HostRect == null)
                    continue;
                bool isPending = pending != null && ReferenceEquals(p, pending);
                Vector3 where = isPending ? pendingPos : p.HostGo.transform.position;
                Vector3 flat = where - headPos;
                flat.y = 0f;
                float dist = flat.magnitude;
                if (dist < 1e-3f)
                    continue;
                float halfWidthWorld = p.HostRect.rect.width * 0.5f
                                       * Mathf.Abs(p.HostRect.lossyScale.x);
                // Measured fresh, both extents, at the distance this window is REALLY hanging at.
                ArcDrawnGeometry g = MeasureArcDrawnGeometry(p, halfWidthWorld, dist);
                float hostYaw = WorldYawDeg(flat);
                names[n] = (_arcClaims[i].Name ?? PanelLogName(p))
                           + (isPending ? " [the window being placed by this very line]" : "");
                // The interval the PLAYER sees is the frame's direction plus the content's own
                // offset inside it — the whole point of ModBuild 234.
                liveYaw[n] = hostYaw + g.OffsetDeg;
                liveHalf[n] = g.DrawnHalfDeg;
                liveFrameYaw[n] = hostYaw;
                liveFrameHalf[n] = g.FrameHalfDeg;
                liveDist[n] = dist;
                reservedYaw[n] = _arcSeatWorldYaw[i];
                reservedHalf[n] = _arcClaims[i].HalfWidthDeg;
                n++;
            }

            if (n == 0)
                return;

            var intervals = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (intervals.Length > 0)
                    intervals.Append("; ");
                intervals.Append('\'').Append(names[i]).Append("' live [")
                         .Append((liveYaw[i] - liveHalf[i]).ToString("F0")).Append("°,")
                         .Append((liveYaw[i] + liveHalf[i]).ToString("F0")).Append("°] world "
                             + "(centre ")
                         .Append(liveYaw[i].ToString("F0")).Append("°, width ")
                         .Append((liveHalf[i] * 2f).ToString("F0")).Append("°)");
                float yawDrift = Mathf.Abs(Mathf.DeltaAngle(liveYaw[i], reservedYaw[i]));
                float widthDrift = Mathf.Abs(liveHalf[i] - reservedHalf[i]) * 2f;
                if (yawDrift > 1f || widthDrift > 2f)
                    intervals.Append(" ≠ RESERVED ").Append(reservedYaw[i].ToString("F0"))
                             .Append("°±").Append(reservedHalf[i].ToString("F0"))
                             .Append("° (the registry and the room disagree by ")
                             .Append(yawDrift.ToString("F0")).Append("° of angle and ")
                             .Append(widthDrift.ToString("F0")).Append("° of width — the packer "
                                 + "reasoned about a window that is not the one standing there)");
            }

            var clashes = new System.Text.StringBuilder();
            int clashCount = 0;
            float worst = 0f;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float ov = liveHalf[i] + liveHalf[j]
                               - Mathf.Abs(Mathf.DeltaAngle(liveYaw[i], liveYaw[j]));
                    if (ov <= ArcAuditOverlapToleranceDeg)
                        continue;
                    clashCount++;
                    worst = Mathf.Max(worst, ov);
                    if (clashes.Length > 0)
                        clashes.Append(", ");
                    clashes.Append('\'').Append(names[i]).Append("' × '").Append(names[j])
                           .Append("' by ").Append(ov.ToString("F0")).Append('°');
                }
            }

            // SECOND FALSIFIER — THE INVISIBLE SHEET. A window's hit rect never shrinks below its
            // frame, and RayUguiDriver awards the click to the NEAREST plane. So a transparent
            // frame that hangs over a neighbour's content AND sits nearer than it will silently
            // eat that neighbour's clicks. This is the failure the phantom-frame depth push exists
            // to prevent, and this is the line that proves it did.
            var steals = new System.Text.StringBuilder();
            int stealCount = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                        continue;
                    if (liveFrameHalf[i] - liveHalf[i] < PhantomFrameThresholdDeg * 0.5f)
                        continue; // i's frame is its content — nothing invisible to trip over
                    float ov = liveFrameHalf[i] + liveHalf[j]
                               - Mathf.Abs(Mathf.DeltaAngle(liveFrameYaw[i], liveYaw[j]));
                    if (ov <= ArcAuditOverlapToleranceDeg)
                        continue;
                    // Covered, but harmless if i's plane is behind j's: the ray reaches j first.
                    if (liveDist[i] > liveDist[j] + 1e-3f)
                        continue;
                    stealCount++;
                    if (steals.Length > 0)
                        steals.Append(", ");
                    steals.Append('\'').Append(names[i]).Append("' empty frame covers '")
                          .Append(names[j]).Append("' by ").Append(ov.ToString("F0"))
                          .Append("° and is NOT behind it (")
                          .Append(liveDist[i].ToString("F1")).Append(" vs ")
                          .Append(liveDist[j].ToString("F1")).Append(" world units)");
                }
            }

            float span = 0f;
            float minYaw = float.MaxValue;
            float maxYaw = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                float off = Mathf.DeltaAngle(liveYaw[0], liveYaw[i]);
                minYaw = Mathf.Min(minYaw, off - liveHalf[i]);
                maxYaw = Mathf.Max(maxYaw, off + liveHalf[i]);
            }
            span = maxYaw - minYaw;

            VRLog.Info("WorldUI", $"MAP ROOM ARC AUDIT (placement #{_arcSeatGeneration}, {trigger}): "
                                  + $"{n} standing window(s), MEASURED LIVE off their own transforms "
                                  + "and a fresh walk of what each one DRAWS — not read back from "
                                  + "the registry, so this line can contradict it. Intervals are "
                                  + "the VISIBLE content, which is what the player judges "
                                  + "'überlappen' by: " + intervals + ". They "
                                  + $"occupy {span:F0}° of the {2f * HalfCircleHalfDeg:F0}° half "
                                  + "circle. VERDICT: "
                                  + (clashCount == 0
                                      ? "CLEAN — no two standing windows overlap in angle (tolerance "
                                        + $"{ArcAuditOverlapToleranceDeg:F1}°). This is the line the "
                                        + "change is falsified by: if it ever reads OVERLAPPING "
                                        + "while the half circle still has free arc, the seating "
                                        + "did not work."
                                      : $"OVERLAPPING — {clashCount} pair(s), worst {worst:F0}°: "
                                        + clashes + ". That is only acceptable if the placement "
                                        + "line for the newest window said THE HALF CIRCLE IS FULL; "
                                        + "if it said it took a free interval, this is the bug.")
                                  + " INVISIBLE FRAMES: "
                                  + (stealCount == 0
                                      ? "CLEAN — no window's transparent frame sits over another "
                                        + "window's content while being the nearer plane, so no "
                                        + "laser can be stolen by an empty sheet."
                                      : $"{stealCount} HAZARD(S) — " + steals + ". Aiming at the "
                                        + "covered window can land on the empty frame instead "
                                        + "(RayUguiDriver awards the hit to the nearest plane). The "
                                        + "phantom-frame depth push should have prevented this; if "
                                        + "this line is non-empty the push did not fire, and the "
                                        + "placement line for the covering window says why."));
        }
        catch (System.Exception ex)
        {
            // House rule for the spawn path: a diagnostic may never take a placement down with it.
            VRLog.Warn("WorldUI", "MAP ROOM ARC AUDIT: could not be taken this time "
                                  + $"({ex.GetType().Name}: {ex.Message}). The placement itself is "
                                  + "unaffected — only the falsifier is missing from this line.");
        }
    }
}
