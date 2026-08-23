using UnityEngine;
using GloomhavenVR.Core;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE MAP ROOM'S FIELD OF VIEW — where a floated window is SEATED, as opposed to how big it may be.
///
/// <para>THE RULING THIS ANSWERS, verbatim, and it OVERRULES ModBuild 234: "Der Halbkreis gefällt
/// mir nicht so, da viele Fenster außerhalb des direkten Sichtfelds spawnen. Das ist die wichtigste
/// Regel: Im SIchtfeld! Prio zwei ist dann so wenig kollisionen wie möglich - wenn das nicht
/// vermeidbar ist dann sollte das neue Fenster näher heran vor dem anderen Fenster spawnen, das es
/// keine direkte Kollision gibt."</para>
///
/// <para>HIS PRIORITY ORDER IS NOW THE CONTRACT, AND ModBuild 234 HAD 1 AND 2 THE WRONG WAY ROUND:</para>
/// <list type="number">
/// <item>IN THE FIELD OF VIEW. Unconditional, and the most important rule. A window he has to TURN
/// TO FIND is a failure even if the layout is perfectly spread.</item>
/// <item>AS FEW ANGULAR COLLISIONS AS POSSIBLE. Second, and no longer allowed to buy itself arc.</item>
/// <item>WHEN A COLLISION IS UNAVOIDABLE, THE NEW WINDOW COMES NEARER — one depth step in front of
/// the one it collides with, so there is no direct collision. Depth resolves what angle cannot.</item>
/// </list>
///
/// <para>WHAT 234 DID AND WHY IT IS BEING UNDONE. It took the whole ±90° half circle as the
/// placement arc precisely so that angles would not collide, and by that measure it SUCCEEDED —
/// nine overlapping pairs in his burst down to one. It bought that by seating windows up to 90°
/// from his spawn gaze, i.e. at the shoulder line, which is the thing he is now reporting. The
/// trade is refused: overlap is cheap (depth fixes it), a window he cannot see is not.</para>
///
/// <para>THE ARC IS NOW MEASURED, NOT CHOSEN — <see cref="ArcPlacementHalfDeg"/>. Two numbers come
/// off this session's own stereo projection matrices and both are already printed on the MAP ROOM
/// ARC GEOMETRY line: the BINOCULAR OVERLAP half-angle (±40.0° on his Quest 3 over VDXR) and the
/// COMFORTABLE READING CONE, which is that same number × the 0.80 comfort margin (±32.0°). The
/// placement arc is the BINOCULAR OVERLAP, applied to each window's EDGES.</para>
/// <list type="number">
/// <item>IT IS THE HONEST OUTER EDGE OF "IM SICHTFELD". Inside it a direction is seen by BOTH eyes
/// with the head still — that is literally what <c>UsableHalfConeDeg</c> measures it as, per side,
/// as the smaller of the two eyes' extents. Outside it a window is MONOCULAR: it sits where the
/// other eye's nose occlusion begins, and this mod renders MultiPass, where that region has shipped
/// stereo bugs before. A window there is not "im Sichtfeld" under any reading, and it is exactly
/// the region ±90° was reaching into.</item>
/// <item>WHY NOT THE COMFORT CONE (±32°). That is ModBuild 193's arc and it is what produced the
/// pile in Questauswahl.jpg: five windows, five "NO free interval is left inside the cone" lines,
/// every one of them dumped back in the middle. The comfort margin answers a different question —
/// "is this comfortable to READ where it is" — and it keeps that job, as the grading on every
/// placement line (<see cref="ArcSeatBand"/>). It is not a placement bound.</item>
/// <item>IT IS DERIVED PER SESSION AND CARRIES THE SAME SANITY BOUNDS. It is
/// <c>UsableHalfConeDeg() / ViewConeComfortFraction</c>, i.e. the margined cone recovered to its
/// raw value — recovered rather than re-derived so the two numbers can never disagree about which
/// matrix they came from. The cone is clamped to [20°, 55°], so the arc is bounded to
/// [25°, 68.75°]; a headset that legitimately reports a 110°+ binocular overlap legitimately gets a
/// wider arc, and one that reports nonsense is clamped and says so on the geometry line.</item>
/// <item>WHAT A WINDOW AT THE BOUND LOOKS LIKE HEAD-STILL. Its outer EDGE sits exactly where
/// binocular overlap ends: on screen, in both eyes, without moving — but in the outer fifth of the
/// field, past the comfort margin, read by turning the EYES through the part of the lens with the
/// worst blur. The placement line grades it "IN VIEW" rather than "COMFORT BAND", and it is never
/// somewhere he has to turn his HEAD to discover.</item>
/// </list>
///
/// <para>SO OVERLAP IS NOW THE NORMAL CASE, AND THAT IS ARITHMETIC. His five-window loadout draws
/// 159.8° of content by 234's own measurement; the field of view supplies 80°. Two hundred per cent
/// oversubscribed — no packer can seat that without collisions, so "no overlap" stops being a
/// success criterion and DEPTH becomes the remedy, which is what he asked for in his own words.</para>
///
/// <para>ONE DEPTH LADDER, NOT TWO. ModBuild 234 had a foreground ladder for overflow (pull TOWARD
/// the head, per overlap generation) and a separate one-step phantom-frame PUSH away from the head
/// for a window whose oversized transparent frame overhung a neighbour's content. His rule 3 is the
/// same mechanism stated more broadly, so they are now one rule, in one direction:</para>
/// <code>
///   depth level = 1 + max(level of every standing window this one's FOOTPRINT intersects)
///   distance    = WindowDistanceMeters − OverlapDepthStepMeters × level, floored at
///                 MinOverlapDistanceMeters
/// </code>
/// <para>The FOOTPRINT is the union of what the window DRAWS and what its FRAME spans, because that
/// is exactly what the hit rect is (<c>Content ∪ Host</c>, a contract that never shrinks below the
/// frame) and therefore what both the eye and the laser can be confused by. Level 0 is a window
/// that intersects nothing and hangs at the nominal reading distance. The level is a MAX over the
/// windows actually intersected, not a running count, so two windows that do not touch each other
/// can share a level and the ladder never marches away for no reason.</para>
///
/// <para>THREE ModBuild 234 FINDINGS SURVIVE THIS CHANGE UNTOUCHED, because they were expensive:</para>
/// <list type="bullet">
/// <item>SEATS ARE HELD IN ABSOLUTE WORLD YAW (<see cref="_arcSeatWorldYaw"/>). 193 stored them
/// gaze-relative and compared them to each other as if they shared a frame; a 40° head turn between
/// two spawns makes +10° and +50° the SAME world direction while the registry swears they are 40°
/// apart. All comparisons go through <see cref="Mathf.DeltaAngle"/>; the search still runs relative
/// to the CURRENT spawn gaze so a new window still lands as near his gaze as the room allows.</item>
/// <item>A RESERVATION COSTS WHAT A WINDOW DRAWS (width AND offset), NOT WHAT IT FRAMES. 'New Party
/// display' booked 88° to draw 14° — a 328 px character column at x −818 inside a 1988 px frame —
/// while three of the other four in that burst draw WIDER than their frame. Angle is packed on the
/// drawn union, offset included, with no per-window knowledge anywhere.</item>
/// <item>THE FRAME IS STILL THE COLLIDER. The hit rect is <c>Content ∪ Host</c> and RayUguiDriver
/// awards a click to the NEAREST plane, so an oversized transparent frame catches the laser in
/// front of whatever is behind it. That is why the depth ladder is fed by the FOOTPRINT and not by
/// the drawn interval — see <see cref="ArcSeatDepthLevel"/>.</item>
/// </list>
///
/// <para>ModBuild 241 — THE MAP IS NOT A WINDOW, AND UNTIL NOW THE PACKER DID NOT KNOW THAT. His
/// report, verbatim: "Ich mag den Halbkreis würde aber bei der Map gerne noch mehr das es so zu
/// beginn spawned wie ideale_position.jpg zeigt." He asked whether that is compatible with the
/// half-circle logic. It is, with ONE new term, and the term is named honestly here rather than
/// special-cased: <see cref="TryMapChannelDeg"/>, the MAP CHANNEL.</para>
///
/// <para>WHAT THE PHOTOGRAPH ACTUALLY CONSTRAINS, MEASURED OFF IT RATHER THAN INFERRED.
/// <c>.planning/debug/ideale_position.jpg</c> is the map room with two windows hand-placed either
/// side of the table. Its horizontal scale is not guessed: the same two windows appear in the
/// 2026-08-24 hardware log's ARC AUDIT at world yaw 67° and 119°, i.e. 52° apart, and they are
/// 942 screen px apart in the photograph ⇒ 0.0552°/px ⇒ 110° across the frame, which is the
/// headset FOV <c>PanelSamplingProbe</c> independently quotes. On that scale:</para>
/// <list type="bullet">
/// <item>THE TWO WINDOWS SIT AT ±26° and their common bisector falls on screen x 908 while the
/// parchment's own centre falls on 905 — 0.2° apart. HE CENTRED THE PAIR ON THE MAP; the arc
/// centre and the arc span are both exactly what this file already uses.</item>
/// <item>THEIR INNER EDGES ARE AT ±21.4°, and the parchment's near edge subtends ±21.3° from where
/// he is standing. That is the whole rule: the windows clear the MAP'S OWN SILHOUETTE, to within
/// a tenth of a degree, and nothing else in the photograph is that tight a coincidence.</item>
/// <item>THEY ARE FURTHER AWAY THAN THEY SPAWN — 10.0° and 8.7° wide against 0.388 m and 0.315 m
/// of real window ⇒ ~2.2 m, against the 1.20 m the packer then used. And they read LOWER, which is
/// the SAME fact: the spawn pose is <c>headPos + fwd × distance</c>, so with the gaze 16-21° below
/// eye level (18 MODAL SPAWN CLAMP lines in that log) a longer distance IS a lower window. The
/// photograph cannot separate "lower" from "further" without the camera pitch, and this build
/// therefore changes ONE of them (see <c>WindowDistanceMeters</c>, 1.20 → 1.40 m) and not both.</item>
/// </list>
///
/// <para>THE MAP CHANNEL, AND WHY IT IS A NEW TERM RATHER THAN AN EXISTING DIAL. It was worth
/// looking: a different arc CENTRE is wrong (the photograph is symmetric about the gaze), a
/// different arc SPAN is wrong (±26° is well inside the measured ±40°), a fixed "first two seats at
/// ±N" is a rule this packer does not have and could not degrade from, and the height reference is
/// not what the photograph is about. The choice rule is the thing that cannot express it: "the free
/// interval NEAREST THE GAZE wins" is CENTRE-SEEKING and his layout is CENTRE-AVOIDING, and no
/// setting of any existing constant turns one into the other. So one term is added: the parchment's
/// own angular interval, measured per placement off <c>MapRoomDriver.ParchmentRenderer.bounds</c>
/// and the live head, is treated as occupied. It is MEASURED, NOT PICKED — walk closer to the table
/// and the map subtends more and the windows move further out, which is the same sentence as "do
/// not cover the map" and needs no second constant.</para>
///
/// <para>AND IT IS A DEMAND, NOT A RESERVATION — the degradation rule. The channel is honoured only
/// when a seat exists that satisfies it AND every standing seat AND the field-of-view bound. When
/// no such seat exists — three, four, six windows open, or one window wider than the room left
/// beside the map — the search falls through to EXACTLY the code that ran before this build, ties
/// and all, and the placement line says which of the two answered. A busy room therefore behaves
/// as it always did, and the map is kept clear only while keeping it clear is free.</para>
///
/// <para>WHAT IS DELIBERATELY UNCHANGED. Height and every spawn clamp (board-top floor, eye cap,
/// pitch flatten) — a seat is a YAW and a DEPTH and nothing else. Facing is still yaw-only and
/// still points at the player, applied once at spawn. A window is placed ONCE and is then the
/// player's: opening or closing anything never re-poses a standing window, a grabbed window is
/// never re-placed (<c>TickPoseRePlaceOne</c> refuses on <c>grab.IsGrabbed</c> and on
/// <c>!RevealPending</c>), a revealed window is never moved (ModBuild 183's shipped bug), and
/// nothing here follows the head. Only the ARRIVING window ever moves.</para>
/// </summary>
internal static partial class ModalFallback
{
    /// <summary>
    /// THE PLACEMENT ARC — half-extent in degrees each side of the player's spawn gaze, applied to
    /// a window's EDGES. IT IS MEASURED, NEVER PICKED: it is this session's BINOCULAR OVERLAP
    /// half-angle, read off the head camera's own stereo projection matrices.
    ///
    /// <para>THE DERIVATION IS A RECOVERY, NOT A SECOND MEASUREMENT. <see cref="UsableHalfConeDeg"/>
    /// measures the raw binocular half-field (per side, the SMALLER of the two eyes' extents — a
    /// direction only one eye can see is not one that can be read) and then multiplies it by
    /// <see cref="ViewConeComfortFraction"/> to get the comfortable READING cone. Dividing that
    /// product back out returns the raw measurement exactly, so the arc and the comfort cone can
    /// never disagree about which matrix they came from. On his Quest 3 over VDXR the geometry line
    /// prints ±40.0° here and ±32.0° for the cone.</para>
    ///
    /// <para>WHY THE BINOCULAR OVERLAP IS THE RIGHT BOUND, AND ±90° IS NOT. See the class header:
    /// inside this angle a window is on screen in both eyes WITH THE HEAD STILL, which is the only
    /// honest reading of "im Sichtfeld"; outside it a window is monocular, in the nose-occlusion
    /// region, in a MultiPass renderer where that region has shipped stereo bugs. ModBuild 234's
    /// half circle reached to the shoulder line and that is the report being answered here.</para>
    ///
    /// <para>IT INHERITS THE CONE'S SANITY CLAMP. <c>UsableHalfConeDeg</c> clamps to
    /// [<see cref="MinUsableHalfConeDeg"/>, <see cref="MaxUsableHalfConeDeg"/>] = [20°, 55°], so
    /// this is bounded to [25°, 68.75°] and a misread projection matrix cannot scatter windows
    /// behind the player — the clamp is named on the geometry line when it engages.</para>
    /// </summary>
    private static float ArcPlacementHalfDeg() =>
        UsableHalfConeDeg() / Mathf.Max(ViewConeComfortFraction, 1e-3f);

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
    private static readonly float[] _auditDist = new float[MaxWindowClaims];
    // The window's OWN spawn gaze — the frame in which "im Sichtfeld" was promised to it, and the
    // reference the field-of-view assertion grades its LIVE direction against.
    private static readonly float[] _auditSpawnGaze = new float[MaxWindowClaims];
    // The FOOTPRINT (drawn ∪ frame) each window occupies — what the depth assertion is taken on.
    private static readonly float[] _auditFootYaw = new float[MaxWindowClaims];
    private static readonly float[] _auditFootHalf = new float[MaxWindowClaims];

    /// <summary>A frame wider than its content by less than this is ordinary window chrome, not a
    /// phantom sheet, and the log lines do not call it one. 4° at reading distance is ~8 cm. It is
    /// a REPORTING threshold only — the depth ladder is fed by the footprint union and needs no
    /// threshold at all.</summary>
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
    /// THE ANGULAR FOOTPRINT of one window: the union of what it DRAWS and what its FRAME spans,
    /// as a centre and a half-width in world yaw.
    ///
    /// <para>WHY THE UNION AND NOT EITHER HALF. It is exactly the hit rect. <c>Content ∪ Host</c>
    /// is that measurement's stated contract — it grows to cover content that escapes the frame and
    /// it never shrinks below the frame — and <c>RayUguiDriver</c> awards a click to the NEAREST
    /// plane. So the union is what the laser can be confused by, and it is also the widest thing
    /// the eye can mistake for this window. The DEPTH ladder is therefore taken on the union, while
    /// the ANGLE search stays on the drawn interval alone, which is what the player judges
    /// "Kollision" by. Two different questions, two different intervals, one window.</para>
    /// </summary>
    /// <param name="frameWorldYaw">World yaw of the HOST RECT's centre.</param>
    /// <param name="frameHalfDeg">Half the host rect's angular width.</param>
    /// <param name="drawnOffsetDeg">Drawn content's offset from the frame centre.</param>
    /// <param name="drawnHalfDeg">Half the drawn content's angular width.</param>
    private static void ArcSeatFootprint(float frameWorldYaw, float frameHalfDeg,
        float drawnOffsetDeg, float drawnHalfDeg, out float footYaw, out float footHalf)
    {
        float lo = Mathf.Min(-frameHalfDeg, drawnOffsetDeg - drawnHalfDeg);
        float hi = Mathf.Max(frameHalfDeg, drawnOffsetDeg + drawnHalfDeg);
        footYaw = frameWorldYaw + (lo + hi) * 0.5f;
        footHalf = (hi - lo) * 0.5f;
    }

    /// <summary>The footprint of the standing claim in <paramref name="slot"/>. The registry seats
    /// the DRAWN centre, so the frame centre comes back off by subtracting the content's own offset
    /// inside its frame.</summary>
    private static void ArcSeatFootprint(int slot, out float footYaw, out float footHalf)
    {
        float frameYaw = _arcSeatWorldYaw[slot] - _arcClaims[slot].DrawnOffsetDeg;
        ArcSeatFootprint(frameYaw, _arcClaims[slot].FrameHalfWidthDeg,
            _arcClaims[slot].DrawnOffsetDeg, _arcClaims[slot].HalfWidthDeg, out footYaw,
            out footHalf);
    }

    /// <summary>
    /// THE DEPTH LADDER, AND THERE IS ONLY ONE OF THEM — the level this window must hang at so that
    /// it is IN FRONT of everything its footprint intersects. His rule 3: "wenn das nicht
    /// vermeidbar ist dann sollte das neue Fenster näher heran vor dem anderen Fenster spawnen, das
    /// es keine direkte Kollision gibt."
    ///
    /// <para>0 = it intersects nothing and hangs at the nominal reading distance. Otherwise it is
    /// <c>1 + max(level of every standing window it intersects)</c>. A MAX and not a running count,
    /// deliberately: two windows that do not touch each other may share a level, so the ladder does
    /// not march away from the player for collisions it is not part of. ONE step of separation is
    /// all the question needs — <c>CanvasConversion.8.Order</c> rewrites every floated panel's
    /// sorting order each frame from its MEASURED eye distance, so any consistent separation makes
    /// the nearer window draw in front, and a bigger one would only make the window angularly wider
    /// (see <see cref="OverlapDepthStepMeters"/>, whose own doc records the four-generation
    /// simulation that forced 0.14 m down to 0.04 m).</para>
    ///
    /// <para>IT MOVES ONLY THE ARRIVING WINDOW. Standing windows keep their level and their
    /// distance; nothing here writes any registry entry but the caller's own.</para>
    /// </summary>
    /// <param name="footYaw">World yaw of the arriving window's footprint centre.</param>
    /// <param name="footHalf">Half-width of the arriving window's footprint.</param>
    /// <param name="skipSlot">The arriving window's own registry slot, excluded — a window cannot
    /// collide with itself (the fuse-that-counted-the-player lesson).</param>
    /// <param name="blockers">The windows it intersects, with the degrees and their levels.</param>
    private static int ArcSeatDepthLevel(float footYaw, float footHalf, int skipSlot,
        out string blockers)
    {
        int level = 0;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || i == skipSlot)
                continue;
            ArcSeatFootprint(i, out float otherYaw, out float otherHalf);
            float ov = otherHalf + footHalf - Mathf.Abs(Mathf.DeltaAngle(footYaw, otherYaw));
            if (ov <= ArcAuditOverlapToleranceDeg)
                continue;
            level = Mathf.Max(level, _arcClaims[i].OverlapRank + 1);
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(_arcClaims[i].Name ?? "?").Append("' by ")
              .Append(ov.ToString("F0")).Append("° (that one is at depth level ")
              .Append(_arcClaims[i].OverlapRank).Append(')');
        }
        blockers = sb.Length == 0 ? "(none)" : sb.ToString();
        return level;
    }

    /// <summary>How many standing windows' DRAWN intervals a window of half-width
    /// <paramref name="halfAngle"/> at <paramref name="worldYaw"/> would collide with — his
    /// priority 2 as a countable number, ranked ahead of everything but the field of view.</summary>
    private static int ArcSeatCollisionCount(float worldYaw, float halfAngle)
    {
        int n = 0;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float ov = _arcClaims[i].HalfWidthDeg + halfAngle
                       - Mathf.Abs(Mathf.DeltaAngle(worldYaw, _arcSeatWorldYaw[i]));
            if (ov > ArcAuditOverlapToleranceDeg)
                n++;
        }
        return n;
    }

    /// <summary>
    /// Apply an arc window's depth term to <paramref name="pos"/>: positive
    /// <paramref name="pullWorld"/> moves it TOWARD the head, which since this build is the ONLY
    /// direction the ladder ever travels (his rule 3 — the new window comes nearer). The negative
    /// branch is kept because the arithmetic is symmetric and a refloat replays a stored term, but
    /// ModBuild 234's phantom-frame PUSH away from the head is retired: it was a second ladder for
    /// the same question and it is now folded into <see cref="ArcSeatDepthLevel"/>.
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

    /// <summary>
    /// Sanity bound on either side of the MAP CHANNEL, degrees. A parchment that measures wider
    /// than this from the head is one we have misread or a player leaning bodily over the table,
    /// and a channel that swallows the arc would only make every window fall through to the
    /// pre-ModBuild-241 path anyway — this bounds the number that gets PRINTED so the log stays
    /// readable when that happens.
    /// </summary>
    private const float MapChannelMaxHalfDeg = 55f;

    /// <summary>
    /// THE MAP CHANNEL — the angular interval, in degrees off <paramref name="gazeYawDeg"/>, that
    /// the PARCHMENT ITSELF occupies from where the player is standing. It is the one thing in this
    /// room that is not a window and that no window may cover; see the class header for the
    /// photograph it comes from.
    ///
    /// <para>IT IS MEASURED, NOT PICKED, and from the room's own authority: the four horizontal
    /// corners of <c>MapRoomDriver.ParchmentRenderer.bounds</c> — the same renderer bounds
    /// <c>MapRoomSeat</c> solves the whole room from and <c>TryGetParchmentFrame</c> publishes as
    /// the multiplayer shared frame — projected from the live head position. Only the head's
    /// HORIZONTAL position is read, which is exactly the part <c>HeadEyeHeight</c>'s spawn-time
    /// correction never touches ("only the height — the horizontal position and the forward stay
    /// the camera's own"), so this cannot inherit the ModBuild 198 head-on-the-floor failure.
    /// Because it is derived per placement, it needs no constant: standing closer to the table
    /// widens it and pushes the windows further out, which is the same rule, and a diorama re-scale
    /// carries it along for free. NOTHING HERE IS IN METRES: every quantity is a world POSITION and
    /// every result is a DEGREE, so there is no "…Meters compared against a world-unit product" to
    /// get wrong. It is also the same number on every client — the parchment bounds are shared
    /// (see <c>MapRoomDriver.TryGetParchmentFrame</c>) — so nothing here needs a wire field.</para>
    ///
    /// <para>WHAT IT CANNOT DO, STATED SO THE NEXT ROUND DOES NOT RE-DISCOVER IT. A window WIDER
    /// than the room left beside the map cannot be moved off it: a full-screen 1920 px window
    /// subtends ~45° at the reading distance, the map takes ~42°, and 45 + 42 does not fit in the
    /// 80° the eye covers. The channel yields for those (the loadout and event windows) and they
    /// are still seated over the map, exactly as before. Only a NARROWER window — which is what the
    /// character column and the quest list are, and what his photograph shows — can flank it.</para>
    ///
    /// <para>IT IS ASYMMETRIC ON PURPOSE. The player does not always stand square to the table, and
    /// a half-angle would then keep clear of a map that is not there on one side while allowing a
    /// window onto it on the other. <c>lo</c> and <c>hi</c> are what the corners actually measure.</para>
    ///
    /// <para>EVERY FAILURE ANSWERS "NO CHANNEL", which is exactly the pre-ModBuild-241 behaviour:
    /// no parchment (the room is standing down), no head camera, a degenerate bounds, or any corner
    /// past ±90° — that last one means the map wraps around the player, where "the interval it
    /// occupies" stops being a single interval and a wrap-safe answer would be a fiction. The
    /// caller degrades to the old search and the placement line says so.</para>
    /// </summary>
    private static bool TryMapChannelDeg(float gazeYawDeg, out float loDeg, out float hiDeg,
        out string note)
    {
        loDeg = 0f;
        hiDeg = 0f;
        note = "NO MAP CHANNEL this placement — the parchment could not be measured (the room is "
               + "standing down, the head camera is not up yet, or the map wraps past ±90° from "
               + "the gaze, which is not one interval), so this window was seated by exactly the "
               + "search every build before ModBuild 241 used";
        MeshRenderer? parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
        Camera? head = CanvasConversion.WorldCamera;
        if (parchment == null || head == null)
            return false;
        Bounds b = parchment.bounds;
        if (b.size.x <= 1e-3f || b.size.z <= 1e-3f)
            return false;

        Vector3 headPos = head.transform.position;
        float lo = float.MaxValue;
        float hi = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float cx = (i & 1) == 0 ? b.min.x : b.max.x;
            float cz = (i & 2) == 0 ? b.min.z : b.max.z;
            Vector3 flat = new Vector3(cx - headPos.x, 0f, cz - headPos.z);
            if (flat.sqrMagnitude < 1e-6f)
                return false; // standing exactly on a corner: no honest angle to take
            float d = Mathf.DeltaAngle(gazeYawDeg, WorldYawDeg(flat));
            if (Mathf.Abs(d) > 90f)
                return false; // the map is not one interval from here
            lo = Mathf.Min(lo, d);
            hi = Mathf.Max(hi, d);
        }

        loDeg = Mathf.Max(lo, -MapChannelMaxHalfDeg);
        hiDeg = Mathf.Min(hi, MapChannelMaxHalfDeg);
        if (hiDeg - loDeg < 1f)
            return false; // the map is a sliver from here: nothing to keep clear of
        note = $"THE MAP CHANNEL IS [{loDeg:F0}°,{hiDeg:F0}°] off this spawn's gaze — the "
               + $"{hiDeg - loDeg:F0}° the parchment itself occupies from where the player is "
               + "standing, measured off the map renderer's own world bounds and the live head, "
               + "never a constant. Nothing may be seated on it: the map is what he is looking at "
               + "in this room ('ideale_position.jpg'), and it is the only thing here that is not "
               + "a window and cannot be moved or closed";
        return true;
    }

    /// <summary>True when a window of half-width <paramref name="halfAngle"/> seated at
    /// <paramref name="offsetDeg"/> off the gaze lies WHOLLY outside the map channel — i.e. beside
    /// the map rather than over any part of it. No neighbour gap is added, deliberately:
    /// <see cref="NeighbourGapDegrees"/> exists so two WINDOWS do not read as one wide window with
    /// a seam, and a window whose edge touches the map's silhouette does not read as the map. The
    /// photograph says the same thing — his inner edges measure ±21.4° against a ±21.3° map.</summary>
    private static bool ArcSeatClearsMapChannel(float offsetDeg, float halfAngle, float loDeg,
        float hiDeg) =>
        offsetDeg + halfAngle <= loDeg + 1e-3f || offsetDeg - halfAngle >= hiDeg - 1e-3f;

    /// <summary>
    /// THE FREE-INTERVAL SEARCH, shared by the spawn claim and the pre-reveal re-seat so the two
    /// can never drift apart (they were a copy-paste pair before ModBuild 241).
    ///
    /// <para>THE CANDIDATES. The gaze itself, the two edges of the MAP CHANNEL, and for every
    /// standing seat the two angles that put this window exactly against that seat's left and right
    /// edge. One of those is always the optimum: the nearest-to-gaze feasible position is either the
    /// gaze, or flush against something. No stepping and no search tolerance.</para>
    ///
    /// <para>TWO PASSES, AND THE SECOND ONE IS THE OLD CODE UNCHANGED. Pass 0 runs only when a map
    /// channel was measured and additionally demands that the seat clear it; pass 1 is the search
    /// exactly as it stood before this build. A candidate at a channel edge always clears the
    /// channel by construction, so a candidate that could win pass 1 but not pass 0 does not exist —
    /// pass 1 is reached only when the channel is genuinely unsatisfiable, and it then behaves as
    /// though the channel had never been measured.</para>
    ///
    /// <para>THE TIE GOES LEFT IN PASS 0 AND RIGHT IN PASS 1, and the asymmetry is deliberate. With
    /// an empty room and a symmetric map the two channel edges are equally near the gaze, so the
    /// tie decides which side of the map the FIRST window takes and the second window then takes
    /// the other. In his loadout the character screen claims first and 'ideale_position.jpg' has it
    /// on the LEFT, so pass 0 fills left first and the pair reproduces the photograph. Nothing here
    /// knows WHICH window it is seating — this file holds no per-window knowledge anywhere and is
    /// not about to start — so a burst that opens in a different order swaps the two sides, and
    /// that is the honest limit of what a side preference can promise. Pass 1's RIGHT-first tie,
    /// the side every build since 183 has filled first, is untouched.</para>
    /// </summary>
    /// <param name="clearsChannel">True when the returned seat is one that keeps the map clear
    /// (pass 0); false when the channel had to be given up (pass 1).</param>
    private static bool ArcSeatFreeInterval(float gazeYawDeg, float centreLimit, float halfAngle,
        bool haveChannel, float channelLo, float channelHi,
        out float best, out bool clearsChannel)
    {
        best = 0f;
        clearsChannel = false;

        int n = 0;
        _arcCandidates[n++] = 0f;
        if (haveChannel)
        {
            _arcCandidates[n++] = channelLo - halfAngle;
            _arcCandidates[n++] = channelHi + halfAngle;
        }
        for (int i = 0; i < _arcClaims.Length && n + 1 < _arcCandidates.Length; i++)
        {
            if (_arcClaims[i].Panel == null)
                continue;
            float standOffset = Mathf.DeltaAngle(gazeYawDeg, _arcSeatWorldYaw[i]);
            float edge = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees;
            _arcCandidates[n++] = standOffset + edge;
            _arcCandidates[n++] = standOffset - edge;
        }

        for (int pass = haveChannel ? 0 : 1; pass <= 1; pass++)
        {
            bool have = false;
            float pick = 0f;
            for (int c = 0; c < n; c++)
            {
                float a = _arcCandidates[c];
                if (Mathf.Abs(a) > centreLimit + 1e-3f)
                    continue;
                if (!ArcSeatIsFree(gazeYawDeg + a, halfAngle))
                    continue;
                if (pass == 0 && !ArcSeatClearsMapChannel(a, halfAngle, channelLo, channelHi))
                    continue;
                bool nearer = Mathf.Abs(a) < Mathf.Abs(pick) - 1e-3f;
                bool tied = Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(pick)) <= 1e-3f;
                bool sideWins = pass == 0 ? a < pick : a > pick;
                if (!have || nearer || (tied && sideWins))
                {
                    have = true;
                    pick = a;
                }
            }
            if (!have)
                continue;
            best = pick;
            clearsChannel = pass == 0;
            return true;
        }
        return false;
    }

    /// <summary>Push a seat that was chosen to clear the MAP CHANNEL back out to the channel's edge
    /// after a depth step has made the window angularly WIDER. One step is 0.04 m against a reading
    /// distance of well over a metre, so this is a fraction of a degree in practice — but a seat
    /// that was chosen for a property has to keep it, and the alternative is a window whose inner
    /// edge creeps onto the map by exactly the amount the ladder bought.</summary>
    private static float ArcSeatReClearChannel(float seatOffset, float halfAngle, float loDeg,
        float hiDeg) =>
        seatOffset >= 0f
            ? Mathf.Max(seatOffset, hiDeg + halfAngle)
            : Mathf.Min(seatOffset, loDeg - halfAngle);

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
        // "both eyes see it without moving", and since this build also the PLACEMENT ARC.
        float binocular = ArcPlacementHalfDeg();
        float reach = Mathf.Abs(offsetDeg) + halfAngle;
        if (reach <= cone + 0.5f)
            return $"COMFORT BAND: its edges reach {reach:F0}°, inside the measured comfortable "
                   + $"reading cone (±{cone:F1}°) — readable with the head still";
        if (reach <= binocular + 0.5f)
            return $"IN VIEW: its edges reach {reach:F0}°, past the comfortable cone (±{cone:F1}°) "
                   + $"but inside the measured binocular overlap (±{binocular:F1}°) — on screen in "
                   + "BOTH eyes with the head still, read by turning the eyes. This is the outer "
                   + "fifth of the field and it is the bound the placement arc is set to";
        // Since the placement bound IS ±binocular applied to the EDGES, this branch cannot be
        // reached by a window this packer seated. It is left in as a falsifier: if it ever prints,
        // the arc bound did not hold and the FIRST rule of the ruling was broken.
        return $"OUT OF THE FIELD OF VIEW — THIS MUST NOT HAPPEN: its edges reach {reach:F0}°, past "
               + $"the measured binocular overlap (±{binocular:F1}°), which is the placement arc "
               + "itself. The player would have to TURN HIS HEAD to find this window, and 'im "
               + "Sichtfeld' is the ruling's first and unconditional rule. Either the window is "
               + "wider than the whole field of view (the line says so separately) or the centre "
               + "bound was not applied";
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
        float arcHalf = ArcPlacementHalfDeg();
        float legibility = WindowLegibilityLive();
        float fullWidthDeg = 2f * Mathf.Atan2(ModalTargetWidthMeters * legibility * 0.5f,
            WindowDistanceMeters) * Mathf.Rad2Deg;
        int fullWidthFit = fullWidthDeg > 0.1f
            ? Mathf.Max(1, Mathf.FloorToInt((2f * arcHalf + NeighbourGapDegrees)
                                            / (fullWidthDeg + NeighbourGapDegrees)))
            : 0;
        int maxLevels = OverlapDepthStepMeters > 1e-4f
            ? Mathf.FloorToInt((WindowDistanceMeters - MinOverlapDistanceMeters)
                               / OverlapDepthStepMeters)
            : 0;
        VRLog.Info("WorldUI", "MAP ROOM ARC GEOMETRY: windows are seated INSIDE THE MEASURED FIELD "
                              + $"OF VIEW — ±{arcHalf:F1}° of world yaw off the spawn gaze, applied "
                              + "to each window's EDGES, so no part of any window is ever placed "
                              + "where the player would have to TURN HIS HEAD to find it. THE "
                              + "PRIORITY ORDER IS THE USER'S, VERBATIM: 'Das ist die wichtigste "
                              + "Regel: Im SIchtfeld! Prio zwei ist dann so wenig kollisionen wie "
                              + "möglich - wenn das nicht vermeidbar ist dann sollte das neue "
                              + "Fenster näher heran vor dem anderen Fenster spawnen, das es keine "
                              + "direkte Kollision gibt.' THE ARC IS MEASURED, NOT PICKED: it is "
                              + $"this headset's BINOCULAR OVERLAP half-angle (±{arcHalf:F1}°), "
                              + $"i.e. the comfortable reading cone ±{cone:F1}° recovered through "
                              + $"the {ViewConeComfortFraction:F2} comfort margin, both derived "
                              + $"from {_coneSource}. Inside it a window is on screen in BOTH eyes "
                              + "with the head still; outside it a window is monocular, in the "
                              + "nose-occlusion region, in a MultiPass renderer — which is not 'im "
                              + "Sichtfeld' under any reading. The comfort cone keeps its own job "
                              + "and grades every placement COMFORT BAND / IN VIEW. THIS REPLACES "
                              + "ModBuild 234's ±90° HALF CIRCLE, which was rejected: 'Der "
                              + "Halbkreis gefällt mir nicht so, da viele Fenster außerhalb des "
                              + "direkten Sichtfelds spawnen.' For scale: a full-width 1920 px "
                              + $"window is {ModalTargetWidthMeters * legibility:F2} m across at "
                              + $"the {WindowDistanceMeters:F2} m reading distance = "
                              + $"{fullWidthDeg:F0}° of view, so at most {fullWidthFit} of THOSE "
                              + "fit side by side in the whole field of view. AND THE MAP IS NOT A "
                              + "WINDOW (ModBuild 241): the parchment's own angular interval, "
                              + "measured per placement off its renderer bounds and the live head, "
                              + "is kept CLEAR — 'bei der Map gerne noch mehr das es so zu beginn "
                              + "spawned wie ideale_position.jpg zeigt', a photograph in which two "
                              + "windows sit at ±26° either side of a map that subtends ±21° from "
                              + "where he stands. It is a DEMAND, not a reservation: when no angle "
                              + "can satisfy it and the standing windows at once, it yields and "
                              + "the search is the one that ran before, which is what keeps three, "
                              + "four and six open windows behaving exactly as they did. Every "
                              + "placement line states the channel it measured and whether it was "
                              + "honoured. THEREFORE OVERLAP IS "
                              + "THE NORMAL CASE AND IS NOT A FAILURE — his five-window loadout "
                              + $"draws ~160° into {2f * arcHalf:F0}° of arc. ANGLE IS STILL THE "
                              + "FIRST LEVER: the free interval NEAREST THE GAZE wins, neighbours "
                              + $"kept {NeighbourGapDegrees:F0}° apart, and the depth ladder is "
                              + "reached only when NO free interval remains. THE LADDER, ONE STEP "
                              + $"OF {OverlapDepthStepMeters:F2} m PER LEVEL: a window's level is "
                              + "1 + the deepest level among the standing windows its FOOTPRINT "
                              + "(drawn ∪ frame — the hit rect) intersects, so it draws IN FRONT "
                              + "of each of them instead of merging with it. It is floored at "
                              + $"{MinOverlapDistanceMeters:F2} m, i.e. {maxLevels} levels — a "
                              + "window nearer than that is unreadable and is physically inside "
                              + "the player's reach of the control board. Reaching the floor "
                              + $"needs a chain of {maxLevels + 1} standing windows each in front "
                              + $"of the last, and the registry holds {MaxWindowClaims}, so the "
                              + "floor sits at the very edge of what this room can produce; when "
                              + "it binds, the placement line says so in as many words and the "
                              + "audit line reports the two windows as sharing a plane. Capacity "
                              + $"{MaxWindowClaims} seats. A window claims ONCE at "
                              + "spawn and keeps its angle and its depth until it stops floating; "
                              + "opening or closing a window never moves any other window (user "
                              + "ruling: 'einmal gespawned sind sie fix'), a window the player has "
                              + "grabbed is never re-placed at all, and a window already REVEALED "
                              + "is never moved.");
    }

    /// <summary>
    /// Claim (or re-find) this window's seat inside the measured field of view. Called from
    /// <see cref="ComputeHmdPose"/> — spawn and presence-regain refloat only, NEVER per frame.
    ///
    /// <para>ANGLE IS THE FIRST LEVER AND DEPTH IS THE FALLBACK, in that order and never the other
    /// way round. Every candidate angle is inside ±<see cref="ArcPlacementHalfDeg"/> applied to the
    /// window's EDGES, so his first rule holds for every branch below without a special case. Only
    /// when NO free interval remains anywhere in the field of view does the depth ladder engage.</para>
    ///
    /// <para>THE CHOICE RULE — THE FREE INTERVAL NEAREST THE CURRENT GAZE, EXCEPT THAT THE MAP IS
    /// ALSO A THING TO BE CLEAR OF. The candidates are the gaze itself, the two edges of the MAP
    /// CHANNEL, and for every standing seat the two angles that put this window exactly against that
    /// seat's left and right edge; one of those is always the optimum, so testing 3 + 2N angles
    /// finds it exactly, with no stepping and no search tolerance. The smallest |offset| that is
    /// inside the arc and clear of everything wins. See <see cref="ArcSeatFreeInterval"/> for the
    /// two passes and for why the map-clearing pass breaks its tie LEFT while the fallback pass
    /// keeps the RIGHT-first tie every build since 183 has used.</para>
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
    /// <para>WHEN THE FIELD OF VIEW IS FULL — the normal case, and a stated policy. The window is
    /// seated INSIDE the field of view anyway and allowed to collide. It is never pushed outside to
    /// avoid a collision and never deferred: "Im SIchtfeld" is the first rule and "so wenig
    /// kollisionen wie möglich" is the second. The angle chosen collides with the FEWEST standing
    /// windows first (his priority 2 as a countable number), then buries the least PERMANENT
    /// surface (a covered closable window costs one press of its X; a covered permanent one has no
    /// exit — ModBuild 194), then maximises the distance to every neighbour, then sits nearest the
    /// gaze. The window then takes ONE DEPTH STEP in front of everything its footprint intersects
    /// (his rule 3) so it does not merge with what it covers. The line says so, names what it
    /// collides with, and prints how many degrees of window were asked of the field of view so the
    /// reader can tell an oversubscribed room from a packing mistake.</para>
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
    /// <param name="overlapRank">THE DEPTH LEVEL. 0 = its footprint intersects nothing and it hangs
    /// at the nominal reading distance; k ≥ 1 = it is one step in front of the deepest window it
    /// intersects. See <see cref="ArcSeatDepthLevel"/>.</param>
    /// <param name="foregroundPullWorld">World units to pull the window toward the head along the
    /// FLATTENED forward (y = 0, so the placement's height is untouched). 0 at depth level 0.</param>
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

        // ---- (a) IN THE FIELD OF VIEW, ALWAYS — his first and unconditional rule. The centre bound
        //          keeps the window's EDGES, not merely its centre, inside the measured binocular
        //          overlap. A window wider than the whole field of view collapses this to 0 and is
        //          centred on the gaze, which is the least-bad thing available and is said out loud.
        float arcHalf = ArcPlacementHalfDeg();
        float centreLimit = arcHalf - halfAngle;
        bool widerThanArc = centreLimit < 0f;
        if (widerThanArc)
            centreLimit = 0f;

        // ---- (b) AS FEW COLLISIONS AS POSSIBLE — his second rule: the free interval nearest the
        //          gaze, if one exists at all inside the bound above. Since ModBuild 241 the MAP
        //          ITSELF is one of the things a seat has to be clear of, and that demand is tried
        //          first and given up when it cannot be met (see ArcSeatFreeInterval).
        bool haveChannel = TryMapChannelDeg(gazeYawDeg, out float channelLo, out float channelHi,
            out string channelNote);
        bool haveFree = ArcSeatFreeInterval(gazeYawDeg, centreLimit, halfAngle, haveChannel,
            channelLo, channelHi, out float bestFree, out bool clearsChannel);

        // The chosen position of the DRAWN CENTRE, degrees off the spawn gaze. `yawDeg` (the HOST
        // rect's angle, which is what the placement rotates to) is derived from it below.
        float seatOffset;

        float demandAll = ArcSeatDemandDeg(halfAngle * 2f);
        if (haveFree)
        {
            seatOffset = bestFree;
            why = cleanBefore + overlapBefore == 0 && !clearsChannel
                ? $"the room was empty, so it took the gaze itself; the window draws {halfAngle * 2f:F0}° "
                  + $"wide and the measured field of view is ±{arcHalf:F1}°"
                : cleanBefore + overlapBefore == 0
                ? $"the room was empty BUT THE MAP IS NOT NOTHING, so it took the nearest angle "
                  + $"BESIDE the map instead of the gaze itself: {seatOffset:F0}°±{halfAngle:F0}° "
                  + $"of {halfAngle * 2f:F0}°-wide DRAWN content, inside the measured "
                  + $"±{arcHalf:F1}° field of view. THIS IS THE ModBuild 241 CHANGE and it is the "
                  + "whole of his report ('bei der Map gerne noch mehr das es so zu beginn spawned "
                  + "wie ideale_position.jpg zeigt') — the space in front of and above the map "
                  + "stays clear, because the map is what he is looking at in this room"
                : $"the FREE INTERVAL NEAREST THE GAZE ({halfAngle * 2f:F0}°-wide DRAWN content, "
                  + $"seated at {seatOffset:F0}°±{halfAngle:F0}° inside the measured "
                  + $"±{arcHalf:F1}° field of view with a {NeighbourGapDegrees:F0}° gap) — ANGLE "
                  + "ALONE SOLVED THE VISIBLE COLLISION, which is the first lever and the one "
                  + "always tried first; whether it also needs a depth step for its FRAME is "
                  + "decided below. The windows already standing "
                  + $"[{standing}] were not touched";
        }
        else
        {
            // ---- THE FIELD OF VIEW IS FULL, WHICH IS THE NORMAL CASE AND NOT A FAILURE. His first
            //      rule wins outright: the window is seated inside the field of view anyway. His
            //      second rule then picks WHICH collision (fewest windows, least permanent surface,
            //      furthest from every neighbour, nearest the gaze) and his third rule — the depth
            //      ladder, below — makes sure it is not a DIRECT collision.
            seatOffset = ArcSeatSpreadDeg(gazeYawDeg, centreLimit, halfAngle);
            int collisions = ArcSeatCollisionCount(gazeYawDeg + seatOffset, halfAngle);
            why = "THE FIELD OF VIEW IS FULL — no free interval is left anywhere inside "
                  + $"±{arcHalf:F1}°. This window draws {halfAngle * 2f:F0}° and the standing set "
                  + $"is [{standing}]; together they ask for {demandAll:F0}° of the "
                  + $"{2f * arcHalf:F0}° the field of view supplies, so this is "
                  + (demandAll > 2f * arcHalf
                      ? "GEOMETRY, NOT A PACKING MISTAKE: the windows open in this room are wider "
                        + "than the field of view and something MUST collide. THAT IS THE EXPECTED "
                        + "STATE since the arc was cut back to what the eye actually covers"
                      : "FRAGMENTATION: the total would fit, but the free space is in pieces too "
                        + "small for this window and standing windows are never re-packed (a "
                        + "re-place of a revealed window is a visible jump, user ruling)")
                  + ". HIS PRIORITY ORDER THEN APPLIES IN ORDER: (1) it stays IN THE FIELD OF VIEW "
                  + "— it is never parked outside to avoid a collision and never withheld; (2) it "
                  + $"takes the angle that collides with the FEWEST windows ({collisions} here), "
                  + "tie-broken by least PERMANENT surface buried, then furthest from every "
                  + "neighbour, then nearest the gaze; (3) the depth ladder below puts it in FRONT "
                  + "of what it collides with"
                  + (haveChannel
                      ? ". THE MAP CHANNEL WAS GIVEN UP, WHICH IS ITS STATED DEGRADATION: no angle "
                        + $"inside ±{arcHalf:F1}° both cleared the map's own "
                        + $"[{channelLo:F0}°,{channelHi:F0}°] and cleared every standing window, so "
                        + "the ModBuild 241 demand yielded and this window was seated by exactly "
                        + "the search that ran before it. Keeping the map clear is a demand, never "
                        + "a reservation — it is honoured while it is free and never at the cost of "
                        + "his first rule"
                      : "");
        }

        // THE SEAT IS THE DRAWN CENTRE; THE PLACEMENT POSITIONS THE HOST RECT. Subtracting the
        // content's own offset inside its frame is what makes the two agree — the visible column
        // lands in the interval that was booked for it, not the middle of an empty sheet.
        float worldYaw = gazeYawDeg + seatOffset;          // drawn centre, world
        float hostWorldYaw = worldYaw - geo.OffsetDeg;     // host rect centre, world

        // ---- (c) THE DEPTH LADDER — his rule 3, and the ONE ladder in this file since ModBuild
        //          234's separate phantom-frame push was folded into it. The level is taken on the
        //          FOOTPRINT (drawn ∪ frame = the hit rect), because that is what both the eye and
        //          the laser can confuse, so a window whose transparent frame overhangs a
        //          neighbour's content takes a step for exactly the same reason a visibly
        //          overlapping one does. Level 0 windows never move.
        ArcSeatFootprint(hostWorldYaw, geo.FrameHalfDeg, geo.OffsetDeg, halfAngle,
            out float footYaw, out float footHalf);
        overlapRank = ArcSeatDepthLevel(footYaw, footHalf, -1, out string blockers);
        foregroundPullWorld = OverlapPullWorld(overlapRank, scale);
        if (overlapRank > 0)
        {
            // Nearer is ANGULARLY WIDER, so the booked interval and the arc bound are both
            // re-derived at the distance the window will really hang at, and the seat is re-clamped
            // so the EDGES are still inside the field of view at that final width. The angle itself
            // is not re-searched: it was already the least-harmful one available.
            float pulledDist = nominalDist - foregroundPullWorld;
            geo.ReDeriveAt(pulledDist);
            halfAngle = geo.DrawnHalfDeg;
            widerThanArc = arcHalf - halfAngle < 0f;
            centreLimit = Mathf.Max(0f, arcHalf - halfAngle);
            // A seat chosen to clear the map keeps that property at its final width: the extra
            // degrees the step bought are spent moving AWAY from the map, not onto it. The in-view
            // clamp still has the last word on the line below — the field of view is never given up.
            if (clearsChannel)
                seatOffset = ArcSeatReClearChannel(seatOffset, halfAngle, channelLo, channelHi);
            float reclamped = Mathf.Clamp(seatOffset, -centreLimit, centreLimit);
            bool clampBit = Mathf.Abs(reclamped - seatOffset) > 0.05f;
            seatOffset = reclamped;
            worldYaw = gazeYawDeg + seatOffset;
            hostWorldYaw = worldYaw - geo.OffsetDeg;
            float pullMeters = foregroundPullWorld / Mathf.Max(scale, 1e-4f);
            float pulledMeters = pulledDist / Mathf.Max(scale, 1e-4f);
            float wanted = OverlapDepthStepMeters * overlapRank;
            why += $". DEPTH STEP — IT SPAWNS NEARER, IN FRONT: its footprint (what it draws UNION "
                   + $"what its frame spans, {footHalf * 2f:F0}° — the hit rect, which is what the "
                   + $"laser is tested against) intersects {blockers}, so it takes DEPTH LEVEL "
                   + $"{overlapRank} = one step in front of the deepest of them. Pulled "
                   + $"{pullMeters:F2} m nearer, reading distance {pulledMeters:F2} m instead of "
                   + $"{WindowDistanceMeters:F2} m, which is "
                   + $"{pullMeters / WindowDistanceMeters * 100f:F0}% larger on screen. The window "
                   + "behind keeps its own distance and is NOT moved — only the arriving window "
                   + "ever moves. AND NEARER MEANS IT ALSO TAKES THE RAY, DELIBERATELY: the hit "
                   + "rect is the HOST rect (Content ∪ Host, never smaller than the frame) and "
                   + "RayUguiDriver awards a click to the NEAREST plane, so wherever these "
                   + "footprints overlap this window swallows clicks aimed at the one behind it. "
                   + "That is the correct owner — it is the window the player just opened — and it "
                   + "is temporary: a hit rect lives on its own host, so when this window closes "
                   + "it takes its rect with it and the one behind is the nearest plane again from "
                   + "the next ray onward, with nothing to unwind"
                   + (wanted - pullMeters > 0.005f
                       ? $". LADDER AT ITS LIMIT: level {overlapRank} wanted {wanted:F2} m but the "
                         + $"floor is {MinOverlapDistanceMeters:F2} m (a window nearer than that is "
                         + "unreadable and physically inside the player's reach of the control "
                         + "board), so this window SHARES A PLANE with the one it should have been "
                         + "in front of. Which of the two takes a click is then decided by the "
                         + "sorter, not by this rule — this is the one state the ladder cannot fix "
                         + "and it is reported rather than hidden"
                       : "")
                   + (clampBit
                       ? $". Being nearer made it {halfAngle * 2f:F0}° wide, so its seat was pulled "
                         + $"back to {seatOffset:F0}° to keep both edges inside ±{arcHalf:F1}° — "
                         + "the field of view is never given up to buy a depth step"
                       : "");
        }
        else
        {
            why += ". DEPTH LEVEL 0: its footprint (drawn ∪ frame, i.e. the hit rect) intersects "
                   + "nothing standing, so it hangs at the nominal reading distance and no depth "
                   + "step was spent";
        }

        yawDeg = Mathf.DeltaAngle(gazeYawDeg, hostWorldYaw);

        if (widerThanArc)
        {
            why += $". NOTE: this window is WIDER than the whole measured field of view "
                   + $"({halfAngle * 2f:F0}° vs ±{arcHalf:F1}°), so it is centred on the gaze and "
                   + $"its edges reach {halfAngle - arcHalf:F0}° past the binocular overlap each "
                   + "side no matter where it is put — nothing can be done about that from here; "
                   + "it has to be narrower or further away";
        }

        // Graded on the DRAWN interval: the band answers "can he read it without moving", and what
        // he reads is the content, not the frame.
        why += ". " + ArcSeatBand(seatOffset, halfAngle);
        why += ". " + channelNote
               + (haveChannel
                   ? clearsChannel
                       ? $" — HONOURED: this window's drawn interval is "
                         + $"[{seatOffset - halfAngle:F0}°,{seatOffset + halfAngle:F0}°] and lies "
                         + "wholly beside it"
                       : " — NOT honoured this time (see above)"
                   : "");
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
            float fitAt = DistanceThatWouldFitMeters(halfWidthWorld, scale, arcHalf);
            why += fitAt > 0f
                ? $". THE TRADE, MEASURED: this window and the widest permanent one would BOTH fit "
                  + $"inside the measured ±{arcHalf:F1}° field of view at a reading distance of "
                  + $"{fitAt:F2} m instead of {WindowDistanceMeters:F2} m — that is "
                  + $"{(1f - WindowDistanceMeters / fitAt) * 100f:F0}% less apparent size. The "
                  + "reading distance was last moved on the user's own instruction (ModBuild 241, "
                  + "1.20 -> 1.40 m: 'die Fenster zB die Questinfo immer bisschen zu nah spawnen, "
                  + "gerne ein bisschen (nicht viel) weiter weg'), so this line no longer describes "
                  + "an untouchable constant — it is the number for the NEXT such decision, and it "
                  + "is measured from the CURRENT distance, so a figure from a pre-241 log is not "
                  + "comparable with one from a later log"
                : ". THE TRADE, MEASURED: no reading distance up to 4.00 m makes this window and "
                  + "the widest permanent one both fit inside the field of view — the pair is wider "
                  + $"than the {2f * arcHalf:F0}° the eye covers, and only a NARROWER window (a "
                  + "tighter content fit) can change that";
        }
        if (overlapDeg <= 0.5f && overlapRank > 0)
            why += ". MEASURED: its DRAWN content does not overlap anything after all — the depth "
                   + "step was taken because its FOOTPRINT (the hit rect, which never shrinks below "
                   + "the frame) does. Nothing looks stacked; the step is there so the laser cannot "
                   + "be caught by an empty sheet";

        int free = FirstFreeClaimIndex();
        if (free < 0)
        {
            // Registry full: still seated, still in the field of view, but holding no reservation —
            // so a later window may land on the same angle AND at the same depth, and the log says
            // so rather than quietly aliasing two windows onto one seat.
            slot = -1;
            why += $". THE REGISTRY IS FULL ({MaxWindowClaims} seats) — this window holds NONE, so "
                   + "a later window can neither see it in the angle search nor step in front of it "
                   + "on the depth ladder, and may land on the same angle at the same distance. It "
                   + "is still in the field of view and grabbable; close a window to free a seat";
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
            // The frame the promise "im Sichtfeld" was made in. The audit grades this window's LIVE
            // direction against it — see LogArcOverlapAudit — because the player is free to turn
            // afterwards and a falsifier that punished him for turning would be lying.
            SpawnGazeWorldYaw = gazeYawDeg,
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
        string standing = ArcSeatOccupancyText(gazeYawDeg);

        float arcHalf = ArcPlacementHalfDeg();
        float centreLimit = Mathf.Max(0f, arcHalf - halfAngle);
        // THE SAME SEARCH THE SPAWN PATH RUNS, and since ModBuild 241 literally the same method:
        // this is the placement that decides where a map-room window actually ends up (the spawn
        // claim books the FRAME, because at that instant nothing is measurable yet), so the map
        // channel has to be honoured HERE above all.
        bool haveChannel = TryMapChannelDeg(gazeYawDeg, out float channelLo, out float channelHi,
            out string channelNote);
        bool haveFree = ArcSeatFreeInterval(gazeYawDeg, centreLimit, halfAngle, haveChannel,
            channelLo, channelHi, out float bestFree, out bool clearsChannel);

        float seatOffset;
        string how;
        if (haveFree)
        {
            seatOffset = bestFree;
            how = $"took the FREE INTERVAL NEAREST THE GAZE at {seatOffset:F0}°±{halfAngle:F0}° "
                  + $"inside the measured ±{arcHalf:F1}° field of view (standing set [{standing}]) "
                  + "and collides with nothing"
                  + (clearsChannel
                      ? ", BESIDE THE MAP rather than over it (ModBuild 241 — his "
                        + "'ideale_position.jpg')"
                      : "");
        }
        else
        {
            seatOffset = ArcSeatSpreadDeg(gazeYawDeg, centreLimit, halfAngle);
            how = $"found THE FIELD OF VIEW (±{arcHalf:F1}°) FULL even at its true drawn width, so "
                  + $"it stays inside it — his first rule — and takes the angle {seatOffset:F0}° "
                  + "that collides with the fewest windows (standing set [" + standing + "])"
                  + (haveChannel
                      ? ", the map channel having yielded because no angle could satisfy it and "
                        + "the standing set at once"
                      : "");
        }

        float worldYaw = gazeYawDeg + seatOffset;
        float hostWorldYaw = worldYaw - geo.OffsetDeg;

        // THE ONE DEPTH LADDER, identical to the spawn path: the level is taken on the FOOTPRINT
        // (drawn ∪ frame = the hit rect) and this window's own seat is already released above, so
        // it cannot see itself as an obstacle.
        ArcSeatFootprint(hostWorldYaw, geo.FrameHalfDeg, geo.OffsetDeg, halfAngle,
            out float footYaw, out float footHalf);
        overlapRank = ArcSeatDepthLevel(footYaw, footHalf, slot, out string blockers);
        foregroundPullWorld = OverlapPullWorld(overlapRank, scale);
        string depthNote;
        if (overlapRank > 0)
        {
            geo.ReDeriveAt(nominalDist - foregroundPullWorld);
            halfAngle = geo.DrawnHalfDeg;
            centreLimit = Mathf.Max(0f, arcHalf - halfAngle);
            // A seat chosen to clear the map keeps that property at its final, wider size — same
            // rule as the spawn path, and the in-view clamp below still has the last word.
            if (clearsChannel)
                seatOffset = ArcSeatReClearChannel(seatOffset, halfAngle, channelLo, channelHi);
            seatOffset = Mathf.Clamp(seatOffset, -centreLimit, centreLimit);
            worldYaw = gazeYawDeg + seatOffset;
            hostWorldYaw = worldYaw - geo.OffsetDeg;
            float pullMeters = foregroundPullWorld / Mathf.Max(scale, 1e-4f);
            depthNote = $". DEPTH STEP: its {footHalf * 2f:F0}° footprint intersects {blockers}, so "
                        + $"it goes to DEPTH LEVEL {overlapRank} — {pullMeters:F2} m NEARER, i.e. "
                        + "IN FRONT of them, which is the user's rule 3. Nothing behind it moved";
        }
        else
        {
            depthNote = ". DEPTH LEVEL 0: its footprint intersects nothing, so it keeps the nominal "
                        + "reading distance";
        }
        hostYawDeg = Mathf.DeltaAngle(gazeYawDeg, hostWorldYaw);

        float overlapDeg = ArcSeatWorstOverlapDeg(worldYaw, halfAngle, out string overlapWith);
        _arcSeatWorldYaw[slot] = worldYaw;
        held.CentreDeg = seatOffset;
        held.SpawnGazeWorldYaw = gazeYawDeg;
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
               + depthNote
               + (overlapDeg > 0.5f
                   ? $". MEASURED: it still overlaps '{overlapWith}' by {overlapDeg:F0}° in ANGLE — "
                     + "which is expected in a room asking for more arc than the eye covers, and is "
                     + "what the depth level above is for"
                   : ". MEASURED: it overlaps nothing in angle")
               + ". " + ArcSeatBand(seatOffset, halfAngle)
               + ". " + channelNote
               + (haveChannel && clearsChannel
                   ? $" — HONOURED: this window's drawn interval is "
                     + $"[{seatOffset - halfAngle:F0}°,{seatOffset + halfAngle:F0}°] and lies "
                     + "wholly beside it"
                   : "")
               + ". No other window was read for anything but collision and none was moved";
        return true;
    }

    /// <summary>
    /// The in-view angle for a window that cannot avoid colliding — in world yaw. Sampled rather
    /// than solved because the objective has its optimum at an endpoint or a midpoint and a ~1°
    /// sweep finds it to within half a degree, far below anything the eye can judge; it runs once
    /// per spawn, never per frame. With nothing standing it answers 0°.
    ///
    /// <para>EVERY SAMPLE IS INSIDE THE FIELD OF VIEW BY CONSTRUCTION, because
    /// <paramref name="centreLimit"/> is the arc bound minus this window's own half-width. So the
    /// user's FIRST rule is not one of the criteria below — it is the domain of the search, and no
    /// ranking here can trade it away.</para>
    ///
    /// <para>THE RANKING, in his order: (1) FEWEST WINDOWS COLLIDED WITH — "Prio zwei ist dann so
    /// wenig kollisionen wie möglich", taken literally as a count, because one collision that
    /// depth can resolve cleanly is better than three; (2) LEAST PERMANENT SURFACE COVERED, which
    /// decides WHICH collision when the count ties, because a covered un-closable window has no
    /// exit and a covered closable one costs a press of its X (ModBuild 194 — the merchant over the
    /// character screen); (3) the largest minimum distance to any standing centre, so each window
    /// still shows a readable strip of itself; (4) nearest the gaze, which is his first rule again
    /// as a tie-break now that every candidate satisfies it; (5) RIGHT, so the ±limit tie is not
    /// settled by which end the sweep started at.</para>
    ///
    /// <para>The sweep is bounded at 241 samples, so the arc width sets the step (~0.3° over a
    /// ±40° field of view) rather than the iteration count.</para>
    /// </summary>
    private static float ArcSeatSpreadDeg(float gazeYawDeg, float centreLimit, float halfAngle)
    {
        if (centreLimit <= 0.5f)
            return 0f;
        int samples = Mathf.Clamp(Mathf.CeilToInt(centreLimit * 2f) + 1, 3, 241);
        float best = 0f;
        float bestScore = -1f;
        float bestPerm = float.MaxValue;
        int bestHits = int.MaxValue;
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
            int hits = ArcSeatCollisionCount(world, halfAngle);
            float perm = ArcSeatPermanentOverlapDeg(world, halfAngle);

            // (1) FEWER COLLISIONS always wins — his priority 2, ahead of everything except being
            //     in the field of view, which every sample already is.
            // (2) then less PERMANENT surface covered, with 0.5° of slack so a rounding-level
            //     difference cannot override the separation rule the player actually sees.
            bool hitsBetter = hits < bestHits;
            bool hitsTied = hits == bestHits;
            bool permTied = Mathf.Abs(perm - bestPerm) <= 0.5f;
            bool permBetter = perm < bestPerm - 0.5f;
            bool tied = Mathf.Abs(score - bestScore) <= 0.01f;
            bool better = hitsBetter
                          || (hitsTied
                              && (permBetter
                                  || (permTied
                                      && (score > bestScore + 0.01f
                                          || (tied && Mathf.Abs(a) < Mathf.Abs(best) - 1e-3f)
                                          || (tied
                                              && Mathf.Abs(Mathf.Abs(a) - Mathf.Abs(best)) <= 1e-3f
                                              && a > best)))));
            if (better)
            {
                bestHits = hits;
                bestScore = score;
                bestPerm = perm;
                best = a;
            }
        }
        return best;
    }

    /// <summary>
    /// THE FALSIFIER, AND IT ASSERTS HIS PRIORITY ORDER. One line, measured LIVE off the standing
    /// windows' own transforms, carrying two verdicts in his order:
    /// <list type="number">
    /// <item>IN THE FIELD OF VIEW — every standing window's live CENTRE is inside the measured
    /// binocular overlap of the gaze IT WAS PLACED FROM. This is the loudest thing in the line and
    /// a run in which any window fails it is a run in which this change did not work, no matter how
    /// clean everything else reads.</item>
    /// <item>NO DIRECT COLLISION — every pair that overlaps in angle is separated in DEPTH by at
    /// least one ladder step, so the nearer one draws in front instead of merging with it and the
    /// laser has an unambiguous nearest plane. Overlap ITSELF is no longer a verdict: the room asks
    /// for roughly twice the arc the eye covers, so pairs are expected and the line prints the
    /// demand-vs-supply figure right beside them so the reader can see that at a glance.</item>
    /// </list>
    ///
    /// <para>WHY IT DOES NOT READ THE REGISTRY. The registry is what the packer BELIEVES; this line
    /// has to be able to contradict it. Every number here comes from the world instead: each
    /// window's host transform position gives its direction from the head and its distance, and its
    /// live <c>HostRect</c> width times its live lossy scale gives its true world half-width, so
    /// the interval printed is the one the player is looking at. The ONE thing that cannot be
    /// measured is a historical fact — which way he was looking when a given window arrived — and
    /// that comes from <see cref="ArcClaim.SpawnGazeWorldYaw"/>, is named as such, and is the only
    /// registry read on the assertion path.</para>
    ///
    /// <para>A WINDOW THE PLAYER HAS MOVED IS EXCLUDED FROM THE FIRST VERDICT AND SAID SO. A grabbed
    /// window is his forever; if he has dragged one behind him, the packer is not answerable for it
    /// and a falsifier that shouted about it would be crying wolf. The test is the live-vs-reserved
    /// disagreement this line already measures — no new plumbing, and the drift is printed.</para>
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
                liveDist[n] = dist;
                reservedYaw[n] = _arcSeatWorldYaw[i];
                reservedHalf[n] = _arcClaims[i].HalfWidthDeg;
                // The FOOTPRINT (drawn ∪ frame) — what the depth assertion is taken on, because it
                // is what the hit rect is and therefore what the laser can be confused by.
                ArcSeatFootprint(hostYaw, g.FrameHalfDeg, g.OffsetDeg, g.DrawnHalfDeg,
                    out _auditFootYaw[n], out _auditFootHalf[n]);
                // The frame the promise was made in — the only registry read on the assertion path,
                // because which way he was looking at that moment cannot be measured now.
                _auditSpawnGaze[n] = _arcClaims[i].SpawnGazeWorldYaw;
                n++;
            }

            if (n == 0)
                return;

            float[] footYaw = _auditFootYaw;
            float[] footHalf = _auditFootHalf;
            float arcHalf = ArcPlacementHalfDeg();
            float worldScale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);

            // ---- VERDICT 1: IN THE FIELD OF VIEW. His first and unconditional rule, and the one
            //      this whole build exists to satisfy.
            //
            //      THE REFERENCE IS EACH WINDOW'S OWN SPAWN GAZE, not the current one: turning is
            //      free and a window placed before a 90° turn is legitimately behind him now, so
            //      grading against where he is looking THIS instant would make the falsifier cry
            //      wolf. The question asked is the only one the packer is answerable for — was it
            //      in the field of view WHEN IT ARRIVED.
            //
            //      AND A WINDOW THE PLAYER MOVED IS EXCLUDED, BY AN EXACT TEST RATHER THAN A GUESS:
            //      the reservation says where the packer PUT it. If the reserved centre was inside
            //      the field of view and the live one is not, then something other than the packer
            //      moved it (his own grab is the only thing allowed to) and the verdict is not
            //      about this code. If the RESERVED centre is itself outside, that is a packer
            //      failure and nothing excuses it.
            var outside = new System.Text.StringBuilder();
            var moved = new System.Text.StringBuilder();
            int outsideCount = 0;
            int excused = 0;
            for (int i = 0; i < n; i++)
            {
                float liveOff = Mathf.DeltaAngle(_auditSpawnGaze[i], liveYaw[i]);
                if (Mathf.Abs(liveOff) <= arcHalf + 0.5f)
                    continue;
                float reservedOff = Mathf.DeltaAngle(_auditSpawnGaze[i], reservedYaw[i]);
                if (Mathf.Abs(reservedOff) <= arcHalf + 0.5f)
                {
                    excused++;
                    if (moved.Length > 0)
                        moved.Append(", ");
                    moved.Append('\'').Append(names[i]).Append("' reserved ")
                         .Append(reservedOff.ToString("F0")).Append("° but stands at ")
                         .Append(liveOff.ToString("F0")).Append('°');
                    continue; // his window, his position — the packer put it inside the field
                }
                outsideCount++;
                if (outside.Length > 0)
                    outside.Append(", ");
                outside.Append('\'').Append(names[i]).Append("' centre ")
                       .Append(liveOff.ToString("F0"))
                       .Append("° off the gaze it was placed from (RESERVED at ")
                       .Append(reservedOff.ToString("F0")).Append("°, so the packer itself put it "
                           + "there)");
            }

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

            // ---- VERDICT 2: NO DIRECT COLLISION. Every pair whose FOOTPRINTS intersect must be at
            //      least one ladder step apart in depth, so the nearer one draws in front of the
            //      other instead of merging with it and RayUguiDriver has an unambiguous nearest
            //      plane. This subsumes ModBuild 234's separate INVISIBLE FRAMES check: the
            //      footprint is drawn ∪ frame, so an empty transparent sheet lying over a
            //      neighbour's content is one of the pairs tested here, on the same rule.
            float stepWorld = OverlapDepthStepMeters * worldScale;
            var merged = new System.Text.StringBuilder();
            int mergedCount = 0;
            int separated = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    float ov = footHalf[i] + footHalf[j]
                               - Mathf.Abs(Mathf.DeltaAngle(footYaw[i], footYaw[j]));
                    if (ov <= ArcAuditOverlapToleranceDeg)
                        continue;
                    float gap = Mathf.Abs(liveDist[i] - liveDist[j]);
                    if (gap >= stepWorld * 0.9f)
                    {
                        separated++;
                        continue;
                    }
                    mergedCount++;
                    if (merged.Length > 0)
                        merged.Append(", ");
                    merged.Append('\'').Append(names[i]).Append("' × '").Append(names[j])
                          .Append("' footprints overlap by ").Append(ov.ToString("F0"))
                          .Append("° but are only ").Append((gap / worldScale).ToString("F2"))
                          .Append(" m apart in depth (one step is ")
                          .Append(OverlapDepthStepMeters.ToString("F2")).Append(" m)");
                }
            }

            float span;
            float minYaw = float.MaxValue;
            float maxYaw = float.MinValue;
            float drawnDemand = 0f;
            for (int i = 0; i < n; i++)
            {
                float off = Mathf.DeltaAngle(liveYaw[0], liveYaw[i]);
                minYaw = Mathf.Min(minYaw, off - liveHalf[i]);
                maxYaw = Mathf.Max(maxYaw, off + liveHalf[i]);
                drawnDemand += liveHalf[i] * 2f;
            }
            span = maxYaw - minYaw;
            float supply = 2f * arcHalf;

            VRLog.Info("WorldUI", $"MAP ROOM ARC AUDIT (placement #{_arcSeatGeneration}, {trigger}): "
                                  // ---- HIS RULE 1, FIRST AND LOUDEST.
                                  + "IN THE FIELD OF VIEW: "
                                  + (outsideCount == 0
                                      ? $"YES for all {n} — every standing window's live centre is "
                                        + $"inside the measured ±{arcHalf:F1}° binocular overlap of "
                                        + "the gaze it was placed from. This is the ruling's first "
                                        + "and unconditional rule and this clause is what falsifies "
                                        + "it: a run where any window reads OUTSIDE is a run where "
                                        + "this did not work, whatever else the line says."
                                      : $"*** NO — {outsideCount} WINDOW(S) OUTSIDE THE MEASURED "
                                        + $"±{arcHalf:F1}° FIELD OF VIEW: " + outside + ". THIS IS "
                                        + "THE FAILURE THE RULING NAMES ('Das ist die wichtigste "
                                        + "Regel: Im SIchtfeld!') — the player has to turn his head "
                                        + "to find these windows. Nothing else on this line matters "
                                        + "until it reads YES. ***")
                                  + (excused > 0
                                      ? $" ({excused} window(s) excluded from that verdict — the "
                                        + "packer seated them INSIDE the field of view and "
                                        + "something else moved them out, which only the player's "
                                        + "own grab is allowed to do, and a grabbed window is his "
                                        + "forever: " + moved + ".)"
                                      : "")
                                  // ---- HIS RULE 3: collisions resolved by depth, not by angle.
                                  + " NO DIRECT COLLISION: "
                                  + (mergedCount == 0
                                      ? $"YES — all {separated} intersecting pair(s) are at least "
                                        + $"one {OverlapDepthStepMeters:F2} m ladder step apart in "
                                        + "depth, so the newer window draws IN FRONT of the older "
                                        + "one rather than merging with it, and the laser has an "
                                        + "unambiguous nearest plane. Footprints are drawn ∪ frame, "
                                        + "i.e. the hit rect, so an empty transparent sheet lying "
                                        + "over a neighbour counts as a collision here."
                                      : $"NO — {mergedCount} pair(s) share a plane: " + merged
                                        + ". Those windows are visually merged and which one takes "
                                        + "a click is decided by the sorter. Either the depth "
                                        + $"ladder hit its {MinOverlapDistanceMeters:F2} m floor "
                                        + "(the placement line says so in as many words) or the "
                                        + "level was computed against a registry entry that is not "
                                        + "the window standing there.")
                                  // ---- HIS RULE 2, as a measurement rather than a verdict.
                                  + $" COLLISIONS: {clashCount} pair(s) overlap in ANGLE"
                                  + (clashCount == 0
                                      ? " — none."
                                      : $" (worst {worst:F0}°): " + clashes + ".")
                                  + $" DEMAND vs SUPPLY: the {n} standing window(s) draw "
                                  + $"{drawnDemand:F0}° of content into the {supply:F0}° the "
                                  + "measured field of view supplies ("
                                  + $"{drawnDemand / Mathf.Max(supply, 1e-3f) * 100f:F0}% "
                                  + "subscribed), spanning "
                                  + $"{span:F0}° end to end. AT OVER 100% OVERLAP IS GEOMETRY, NOT "
                                  + "A PACKING MISTAKE — the arc was deliberately cut back from "
                                  + "ModBuild 234's ±90° half circle to what the eye actually "
                                  + "covers, and depth is what resolves what angle cannot. "
                                  + $"INTERVALS, MEASURED LIVE off {n} transform(s) and a fresh "
                                  + "walk of what each window DRAWS — not read back from the "
                                  + "registry, so this line can contradict it: " + intervals);
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
