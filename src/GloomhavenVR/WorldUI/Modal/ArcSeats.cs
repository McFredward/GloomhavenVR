using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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
/// <para>ModBuild 243 — THAT READING OF THE PHOTOGRAPH WAS WRONG AND THE USER SAID SO. Verbatim
/// (2026-08-24): "Du hast meinen Idealzustand falsch interpretiert. Schau nochmal auf das Bild. Mir
/// ging es darum, dass ein Fenster (die Character-UI) auf der linken Ecke des Tisches und das andere
/// Fenster (die Weltquests) auf der oberen rechten Ecke des Tisches spawnen. Aktuell spawnen sie
/// zusammen ineinander." The layout is anchored to the MAP TABLE'S TWO FAR CORNERS, not to an angle
/// off his gaze. <see cref="TryTableFarCornersDeg"/> carries the re-measurement of the same
/// photograph, the pixel evidence that separates the two readings, and the reason 241's ±26° looked
/// right anyway (he was standing square to the table, where the corner separation and that angle are
/// the same number). NOBODY MAY RESTORE THE 241 READING FROM THE CHANNEL CODE BELOW.</para>
///
/// <para>WHAT SURVIVES 241 UNCHANGED. The reading distance (1.20 → 1.40 m) stands: it was justified
/// by how much arc a window needs beside SOMETHING ELSE, and a corner is at least as demanding as a
/// channel edge. The MAP CHANNEL itself also stays — demoted, not deleted. It is the parchment's own
/// angular interval, measured per placement off <c>MapRoomDriver.ParchmentRenderer.bounds</c> and
/// the live head, and it is now the SECOND demand: tried only when neither table corner is free and
/// inside the field of view, and reported rather than enforced whenever a corner answered.</para>
///
/// <para>WHY THE CHANNEL WAS KEPT AS A FALLBACK AND NOT DELETED — the alternative, considered and
/// rejected. Deleting it would drop straight to the pre-241 "free interval nearest the gaze", which
/// is CENTRE-SEEKING, i.e. it would seat a third window over the map. His 241 report ("bei der Map
/// gerne noch mehr das es so zu beginn spawned wie ideale_position.jpg zeigt") and his 243
/// correction agree on that much: nothing covers the map in that picture. So the channel keeps a
/// job — "when you cannot have a corner, at least stay off the parchment" — and loses its claim to
/// be what the photograph showed. Note that from where he actually stood in the second 2026-08-24
/// log the channel measures 102°, wider than the whole 80° binocular overlap, so it is already
/// inert there and its own line says it was given up.</para>
///
/// <para>AND BOTH ARE DEMANDS, NOT RESERVATIONS — the degradation rule, unchanged. A corner is
/// honoured only when a seat on it clears every standing seat AND the field-of-view bound; the
/// channel likewise. When neither can be met the search falls through to EXACTLY the code that ran
/// before ModBuild 241, ties and all, and the placement line says which of the three answered.</para>
///
/// <para>ModBuild 243 — AND THE DEPTH LADDER LEARNED TO GO OUTWARD, which is his own suggestion for
/// the collision report: "Wenn du manche Fenster etwas (ein klein bisschen) weiter weg spawnst ist
/// der Halbkreis zu spawnen auch größer." The shipped ladder only ever pulled a colliding window
/// NEARER, which makes it angularly WIDER — the 242 log line for his collision photograph says so
/// itself ("Being nearer made it 49° wide, so its seat was pulled back to 16°"). Since this build,
/// when no free interval exists the window is first walked OUT in
/// <see cref="ArcOutwardStepMeters"/> rungs (bounded by <see cref="MaxArcOutwardMeters"/>), and the
/// first distance at which a seat is clean in ANGLE and in FOOTPRINT wins. Only when that fails does
/// the old nearer-step behaviour run. See <see cref="TryArcSeatFurtherOut"/>.</para>
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
    /// THE CHOOSER GAP (user report 2026-09-03): extra clearance, degrees, a window keeps from a
    /// standing claim whose interval includes a RESERVED SUB-VIEW SLOT (the character screen with
    /// the battle-goal picker forecast beside its column — see
    /// <c>CanvasConversion.TryForecastSubViewSlot</c>). Added to <see cref="NeighbourGapDegrees"/>,
    /// so the info window stands 6° off the chooser's picker instead of the 2° every other pair
    /// keeps: at the 1.40 m reading distance that is ~15 cm of visible air between the quest info
    /// and the goal cards, which is what "Vergroessere den Abstand zwischen den beiden Fenstern"
    /// asks for. It is a demand, never a reservation: when the field of view holds no free seat at
    /// this clearance the search retries at the plain neighbour gap, and the line says so.
    /// </summary>
    private const float ChooserSlotExtraGapDegrees = 4f;

    /// <summary>True while the seat search is re-run with every <see cref="ArcClaim.ExtraGapDeg"/>
    /// waived — the one retry <see cref="ArcSeatFreeInterval"/> makes before declaring the field
    /// full. Never set outside that method.</summary>
    private static bool _relaxChooserGap;

    /// <summary>Set by the last <see cref="ArcSeatFreeInterval"/> that found a seat: whether it had
    /// to waive the chooser gap to find one. Read by the callers for their line.</summary>
    private static bool _lastSeatWaivedChooserGap;

    /// <summary>
    /// THE BESIDE CLAUSE of the last arc placement — appended verbatim to the MAP ROOM WINDOW SLOT
    /// line by <c>ModalFallback.9.Spawn</c>: which standing chooser this window was seated beside,
    /// the measured gap in degrees and millimetres, and the overlap (0° is the claim being proved).
    /// Empty when no chooser with a reserved slot stands. Written by every claim/re-seat path,
    /// cleared at the start of each.
    /// </summary>
    private static string? _arcLastBesideClause;

    /// <summary>
    /// The DRAWN geometry of one window, in the angular terms the arc reasons in. Every field is
    /// measured at a stated distance; nothing here is assumed.
    /// </summary>
    private struct ArcDrawnGeometry
    {
        /// <summary>True when the drawn extent was widened to cover the sub-view slot a fixed-fit
        /// host is about to fill (part 9f). The claim written from this geometry carries
        /// <see cref="ArcClaim.SlotReserved"/> and the chooser gap.</summary>
        public bool SlotReserved;

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

        bool viaInk = false;
        if (!CanvasConversion.TryMeasureDrawnContent(panel, out Rect content, out Rect host,
                out int contributors))
        {
            // ---- ModBuild 245: THE VISIBILITY TEST IS THE WRONG INSTRUMENT BEFORE THE REVEAL, AND
            //      THAT IS WHY HIS TWO MAP WINDOWS KEPT LANDING ON TOP OF EACH OTHER.
            //
            // TryMeasureDrawnContent asks which graphics are VISIBLE, and a window behind the reveal
            // gate has none — so on a COLD first open this fell through to the frame and the window
            // booked its whole 1920 px rect. In the ModBuild 244 hardware log that is 72° of an 80°
            // field for the character screen (LogOutput.log:1233), which leaves the quest list no
            // free interval anywhere (:1261, "together they ask for 90° of the 80° the field of view
            // supplies") and stacks the two — 'kartenraum,spawn.jpg', exactly the fault the corner
            // rule exists to prevent.
            //
            // TryReseatArcClaimOnDrawnContent was written for precisely this and could not save it:
            // it runs at the pre-reveal re-place and bails on the SAME !Measured test, so on a cold
            // open the frame-sized claim stands for the window's whole life. The proof that the
            // machinery is otherwise sound is in the same log: the SECOND time that window opens its
            // ink IS visible, the re-seat fires, and the line reads "PHANTOM FRAME: 59° of it is
            // empty and was handed back to the arc" (:3307) — 14° drawn against the 72° booked.
            //
            // PanelInkBounds measures the same union WITHOUT a visibility test, which is why the
            // grab bar and the close X are already sized correctly on a window nobody can see yet.
            // Using it here makes the reservation right on the FIRST placement instead of the
            // second, so the corners are free when the corner pass runs. [[open-is-not-drawing]],
            // [[reserve-what-is-drawn]] — this is the third time this class of defect has shipped.
            if (!PanelInkBounds.TryMeasure(panel, out PanelInkBounds.Ink ink) || !ink.Valid)
                return g;
            RectTransform? hostRect = panel.HostRect;
            if (hostRect == null)
                return g;
            content = ink.Rect;
            host = hostRect.rect;
            contributors = ink.Graphics;
            viaInk = true;
        }
        if (host.width < 1f || halfWidthWorld <= 1e-4f)
            return g;

        // ---- 2026-09-03: THE SUB-VIEW SLOT A CHOOSER IS ABOUT TO FILL IS BOOKED WITH ITS INK.
        //      The character screen draws a 328 px column and hands the rest of its frame back as
        //      phantom; the quest popup then took the free interval nearest the gaze — exactly the
        //      566 px the battle-goal picker is seated into a moment later (LogOutput.log:2345 vs
        //      :2412, the user's "Quest-Info-Fenster ... innerhalb des Fensters"). The forecast
        //      (CanvasConversion part 9f) names the slot in host px; it is unioned into the drawn
        //      rect HERE, in the one measurement every claim, refresh, re-seat and audit share, so
        //      the registry and the room cannot disagree about it. Answers false — and changes
        //      nothing — for every window that is not a fixed-fit host with a slot to forecast.
        string slotNote = string.Empty;
        if (CanvasConversion.TryForecastSubViewSlot(panel, out float slotLeft, out float slotRight,
                out string slotHow))
        {
            float wasMin = content.xMin, wasMax = content.xMax;
            float newMin = Mathf.Min(wasMin, slotLeft);
            float newMax = Mathf.Max(wasMax, slotRight);
            if (newMax - newMin > content.width + 0.5f)
            {
                content = Rect.MinMaxRect(newMin, content.yMin, newMax, content.yMax);
                slotNote = $" — SUB-VIEW SLOT RESERVED WITH THE INK: the drawn union "
                           + $"[{wasMin:F0}..{wasMax:F0}] px was widened to "
                           + $"[{newMin:F0}..{newMax:F0}] px to cover the slot "
                           + $"[{slotLeft:F0}..{slotRight:F0}] px its fixed fit seats a sub-view "
                           + $"into ({slotHow}); a window seated beside this one keeps "
                           + $"{NeighbourGapDegrees + ChooserSlotExtraGapDegrees:F0}° of clearance "
                           + "from that edge instead of the usual "
                           + $"{NeighbourGapDegrees:F0}°";
                g.SlotReserved = true;
            }
            else
            {
                slotNote = $" — the sub-view slot [{slotLeft:F0}..{slotRight:F0}] px "
                           + $"({slotHow}) already lies inside the drawn union, nothing added";
                g.SlotReserved = true;
            }
        }

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
                 + $"{g.DrawnHalfDeg * 2f:F0}° ({content.width:F0} px from {contributors} "
                 + (viaInk
                     ? "graphic(s) — MEASURED THROUGH PanelInkBounds, i.e. the window is still "
                       + "behind the reveal gate and the VISIBILITY test found nothing; before "
                       + "ModBuild 245 this booked the whole frame and is what stacked his two map "
                       + "windows"
                     : "visible graphic(s)")
                 + $") at offset {g.OffsetDeg:F0}° "
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
                             : " — a small difference, booked as measured")
                 + slotNote;
        return g;
    }

    /// <summary>The chooser gap this claim asks of its neighbours: the extra clearance while its
    /// slot is reserved, else nothing.</summary>
    private static float ChooserGapFor(in ArcDrawnGeometry g) =>
        g.SlotReserved ? ChooserSlotExtraGapDegrees : 0f;

    /// <summary>The extra clearance standing claim <paramref name="slot"/> asks for right now — 0
    /// while the search is in its waived retry (see <see cref="_relaxChooserGap"/>).</summary>
    private static float ExtraGapOf(int slot) =>
        _relaxChooserGap ? 0f : _arcClaims[slot].ExtraGapDeg;

    /// <summary>True when any standing claim asks for a chooser gap, i.e. when a waived retry
    /// could change the search's answer at all.</summary>
    private static bool AnyChooserGapStanding()
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel != null && _arcClaims[i].ExtraGapDeg > 0.01f)
                return true;
        }
        return false;
    }

    /// <summary>
    /// THE BESIDE CLAUSE — the proof the user report of 2026-09-03 is answered, measured on the
    /// registry the moment a seat is written. Names the standing chooser (a claim with a reserved
    /// sub-view slot; failing that, the nearest standing window) this window was seated beside,
    /// the gap between the two drawn intervals in degrees and in millimetres at this window's
    /// reading distance, and the overlap — which must read 0° for the fix to have held. Empty when
    /// nothing stands. Written to <see cref="_arcLastBesideClause"/> for the SLOT line and returned
    /// for the caller's own text.
    /// </summary>
    private static string BesideClause(float worldYaw, float halfAngle, int skipSlot,
        float distWorld, float scale)
    {
        int pick = -1;
        float pickGap = float.MaxValue;
        bool pickIsChooser = false;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || i == skipSlot)
                continue;
            float gap = Mathf.Abs(Mathf.DeltaAngle(worldYaw, _arcSeatWorldYaw[i]))
                        - (halfAngle + _arcClaims[i].HalfWidthDeg);
            bool chooser = _arcClaims[i].SlotReserved;
            // A chooser outranks a nearer non-chooser: the clause exists to prove THAT pair.
            if (pick < 0 || (chooser && !pickIsChooser)
                || (chooser == pickIsChooser && gap < pickGap))
            {
                pick = i;
                pickGap = gap;
                pickIsChooser = chooser;
            }
        }
        if (pick < 0)
        {
            _arcLastBesideClause = string.Empty;
            return string.Empty;
        }
        float distMeters = distWorld / Mathf.Max(scale, 1e-4f);
        float gapDeg = Mathf.Max(0f, pickGap);
        float overlapDeg = Mathf.Max(0f, -pickGap);
        float gapMm = 2f * distMeters * Mathf.Tan(gapDeg * 0.5f * Mathf.Deg2Rad) * 1000f;
        float wanted = NeighbourGapDegrees + (pickIsChooser && !_lastSeatWaivedChooserGap
            ? ChooserSlotExtraGapDegrees : 0f);
        string clause = $" BESIDE: '{_arcClaims[pick].Name ?? "?"}' — gap {gapDeg:F0}° "
                        + $"({gapMm:F0} mm at {distMeters:F2} m), OVERLAP {overlapDeg:F0}°"
                        + (pickIsChooser
                            ? " — that window is a CHOOSER whose reservation covers the sub-view "
                              + "slot its fixed fit is about to fill (the battle-goal picker beside "
                              + "the character column), so the gap is measured from the picker's "
                              + "edge, not from the column's"
                            : " (no chooser with a reserved sub-view slot stands; this is the "
                              + "nearest window)")
                        + (overlapDeg > 0.5f
                            ? ". THE GAP WAS NOT KEPT: no seat inside the field of view kept it at "
                              + "any clearance, so the two drawn intervals overlap — read the line "
                              + "above for which rule seated this window and whether it stands in "
                              + "front on the depth ladder"
                            : _lastSeatWaivedChooserGap && pickIsChooser
                                ? $". The {NeighbourGapDegrees + ChooserSlotExtraGapDegrees:F0}° "
                                  + "chooser clearance could not be kept inside the field of view "
                                  + $"and was WAIVED to the plain {NeighbourGapDegrees:F0}° gap"
                                : $" (asked for at least {wanted:F0}°)")
                        + ".";
        _arcLastBesideClause = clause;
        return clause;
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
            // A corner claim's drawn centre is a fixed place (the corner) and its HOST moves with
            // the content offset — the opposite of the "host has not moved" premise below. Its
            // registry is kept by the corner path (TryRefreshCornerClaim) instead.
            if (_arcClaims[i].Corner)
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
            _arcClaims[i].SlotReserved = g.SlotReserved;
            _arcClaims[i].ExtraGapDeg = ChooserGapFor(g);
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
            float needed = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees
                           + ExtraGapOf(i);
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
    /// a seam, and a window whose edge touches the map's silhouette does not read as the map.
    /// (ModBuild 241 justified this clause with "his inner edges measure ±21.4° against a ±21.3°
    /// map". THAT READING OF THE PHOTOGRAPH WAS CORRECTED BY THE USER — see
    /// <see cref="TryTableFarCornersDeg"/>. The clause survives on its own merit as the rule for a
    /// window that is NOT on a corner; nothing may re-derive the corner rule from it.)</summary>
    private static bool ArcSeatClearsMapChannel(float offsetDeg, float halfAngle, float loDeg,
        float hiDeg) =>
        offsetDeg + halfAngle <= loDeg + 1e-3f || offsetDeg - halfAngle >= hiDeg - 1e-3f;

    /// <summary>
    /// THE TABLE SLAB'S HALF-WIDTH AS A MULTIPLE OF THE PARCHMENT'S, world X axis. It is a RATIO of
    /// two surveyed real-metre lengths and is therefore applied to a live world-unit bounds without
    /// converting anything: 1.55 m of table over 0.96 m of map. Deliberately NOT a "…Meters"
    /// constant multiplied by a scale — this repo has shipped a bound named that way compared
    /// against a world-unit product — and the table is co-centred with the map on both horizontal
    /// axes (survey line in <c>MapRoom/MapTableLegs.cs</c>: <c>'GH_Map_TableTop_Lg' L0 size
    /// (306.88, 29.29, 454.81) = 1.55 x 0.15 x 2.30 m, centre offset (0.00, -0.08, 0.00) m from the
    /// map's centre/top</c>), so a ratio about that shared centre IS the whole transform.
    /// </summary>
    private const float TableToMapWidthRatio = 1.55f / 0.96f;

    /// <summary>The same ratio on the world Z axis — 2.30 m of table over 1.20 m of map. It is NOT
    /// the same number as <see cref="TableToMapWidthRatio"/> and the two must never be "unified":
    /// the slab is far deeper than it is wide, which is exactly why the two corners the player sees
    /// across the map are its FAR ones and why the near ones are beside him.</summary>
    private const float TableToMapDepthRatio = 2.30f / 1.20f;

    /// <summary>The surveyed map's own aspect, world x / world z = 0.96 / 1.20. Measured live and
    /// compared, so a room whose parchment is not the one that was surveyed answers "no corners"
    /// instead of inventing a table around it.</summary>
    private const float MapAspectXOverZ = 0.96f / 1.20f;

    /// <summary>How far the live parchment aspect may differ from <see cref="MapAspectXOverZ"/>
    /// before the corner rule stands down. 0.08 is 10 % of the aspect — well past renderer-bounds
    /// padding and well short of a different asset.</summary>
    private const float MapAspectTolerance = 0.08f;

    /// <summary>Two "corners" nearer than this to each other in angle are one corner seen twice (the
    /// player is far away, or square on to a single corner), and seating two windows on them would
    /// stack them — which is the fault being fixed. The corner rule then stands down.</summary>
    private const float MinCornerSeparationDeg = 8f;

    /// <summary>
    /// THE TABLE'S TWO FAR CORNERS, as angles off <paramref name="gazeYawDeg"/> — the ANCHORS his
    /// ideal photograph is actually built on.
    ///
    /// <para>HIS CORRECTION, VERBATIM (2026-08-24): "Du hast meinen Idealzustand falsch
    /// interpretiert. Schau nochmal auf das Bild. Mir ging es darum, dass ein Fenster (die
    /// Character-UI) auf der linken Ecke des Tisches und das andere Fenster (die Weltquests) auf der
    /// oberen rechten Ecke des Tisches spawnen. Aktuell spawnen sie zusammen ineinander."</para>
    ///
    /// <para>ModBuild 241 READ THAT PHOTOGRAPH AS AN ANGLE AND THE USER HAS SAID IT IS WRONG. Its
    /// reading — recorded in the class header above and corrected there — was "the two windows sit
    /// at ±26° and their inner edges clear the parchment's own ±21° silhouette", i.e. a MAP CHANNEL
    /// measured off the head. Re-measured on the photograph itself at full resolution (3840x2160),
    /// the pixels say something else and say it tightly:</para>
    /// <list type="bullet">
    /// <item>The table's far-LEFT corner is at x 832 px and the LEFT window's grab bar — the bar IS
    /// the window's bottom edge — is centred on x 826 px. SIX PIXELS out of the 1835 px the table's
    /// far edge spans, i.e. 0.3 %.</item>
    /// <item>The table's far-RIGHT corner is at x 2667 px and the RIGHT window's bar is centred on
    /// x 2611 px — 56 px, 3 %.</item>
    /// <item>ModBuild 241's rule predicts something else and the difference is measurable: an INNER
    /// EDGE on the map silhouette puts each window's CENTRE a half-width further out, and the left
    /// window measures 298 px across, so 241 predicts its centre at x 683 px against the 826 px that
    /// is there. The photograph separates the two readings by 143 px and picks the corner.</item>
    /// <item>Both bars are horizontal in world (their image slopes converge on one vanishing point)
    /// and sit at a COMMON height, ~130 px above the table plane — 7 % of the far edge's own span. A
    /// seat is a yaw and a depth, so this file does not set height; the number is recorded because
    /// it is the one thing the photograph fixes that nothing here reads, and because the shipped
    /// board-top clearance (0.30 m) is about 2.5x it. Changing that constant would move every
    /// SCENARIO window too and is not taken on one photograph.</item>
    /// <item>WHAT THE PHOTOGRAPH DOES NOT CONSTRAIN: depth. 241 derived "~2.2 m" from an assumed
    /// 110° frame FOV; solving the frame scale from the two objects whose real sizes are known (the
    /// 1.55 m table edge and the 0.96 m map edge, 0.55 m apart in depth) is ill-conditioned to the
    /// point of uselessness — a 1 % pixel error moves the answer by tens of metres. No distance is
    /// claimed from this image, and none is taken from it.</item>
    /// </list>
    ///
    /// <para>WHY 241's ±26° "AGREED" WITH THE PHOTOGRAPH ANYWAY, WHICH IS THE TRAP. The two bar
    /// centres are 1785 px apart and the two table corners are 1835 px apart — the SAME separation.
    /// He was standing square to the table, so the corner separation and the angle 241 fitted are
    /// the same number from that one spot. They are the same number nowhere else, and that is the
    /// whole practical difference: an angular rule keeps the windows in front of his gaze wherever
    /// he stands; a corner rule attaches them to a place in the room. He asked for the second.</para>
    ///
    /// <para>SO THEY STAY ON THEIR CORNERS IF HE WALKS ROUND THE TABLE, AND THAT IS DELIBERATE. A
    /// seat is computed ONCE, at spawn, and a spawn is not a re-orientation: nothing here follows
    /// the head, and the standing ruling ("ohne explizite Bewegung vom User, sollen sie ihre
    /// Position nicht verändern") forbids moving a window because the player moved. A corner-seated
    /// window can therefore end up behind him — exactly as every window in this room already can
    /// once he turns, which the arc audit already reports as the player's own doing rather than a
    /// placement fault. What the corner rule may NOT do is put a window out of view AT SPAWN, and it
    /// cannot: the corner is a CANDIDATE inside the same ±<see cref="ArcPlacementHalfDeg"/> bound
    /// applied to the window's EDGES that every other candidate lives in, and a corner that fails
    /// that bound is simply not offered.</para>
    ///
    /// <para>WHY THE FAR CORNERS AND NOT THE NEAR ONES. "Obere rechte Ecke" is the far right, and
    /// the near corners are beside the player: with the slab 2.30 m deep and the player at the near
    /// short end (<c>MapRoomSeat.EdgeStandoffMeters</c> 0.45 m) the near corners subtend ±60° from
    /// him and would fail the field-of-view bound anyway. The two FURTHEST corners are picked by
    /// measurement rather than by an assumed side, so a player standing at the long edge gets the
    /// two corners that are far from HIM.</para>
    ///
    /// <para>IT IS MEASURED FROM THE ROOM'S OWN AUTHORITY AND COSTS NO SWEEP. The table slab belongs
    /// to the game and <c>MapTableLegs.TryFindTable</c> finds it with a
    /// <c>FindObjectsOfType&lt;MeshRenderer&gt;()</c>, which is exactly the call this repo has
    /// removed three times on a perf round. It is not needed: the slab is co-centred with the
    /// parchment and is a fixed multiple of it on each axis, so the corners come off
    /// <c>MapRoomDriver.ParchmentRenderer.bounds</c> — the same bounds the map channel, the seat
    /// solve and the multiplayer shared frame all already use, and therefore the same number on
    /// every client with no wire field of its own. Only the head's HORIZONTAL position is read,
    /// which is the part <c>HeadEyeHeight</c>'s spawn-time correction never touches.</para>
    /// </summary>
    /// <param name="leftDeg">The left far corner, degrees off the gaze (+ = right).</param>
    /// <param name="rightDeg">The right far corner, degrees off the gaze.</param>
    /// <param name="note">Human-readable derivation for the placement line.</param>
    private static bool TryTableFarCornersDeg(float gazeYawDeg, out float leftDeg, out float rightDeg,
        out string note)
    {
        leftDeg = 0f;
        rightDeg = 0f;
        note = "NO TABLE CORNERS this placement — the parchment could not be measured (the room is "
               + "standing down or the head camera is not up yet), so this window was seated by the "
               + "angular search and NOT on one of his corners";
        MeshRenderer? parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
        Camera? head = CanvasConversion.WorldCamera;
        if (parchment == null || head == null)
            return false;
        Bounds b = parchment.bounds;
        if (b.size.x <= 1e-3f || b.size.z <= 1e-3f)
            return false;

        // THE FALSIFIER FOR THE SURVEY ITSELF. The ratios below describe ONE slab around ONE map; if
        // the parchment standing in the room is not the shape that was surveyed, the table they
        // would build around it is a fiction, and the rule stands down rather than guessing.
        float aspect = b.size.x / b.size.z;
        if (Mathf.Abs(aspect - MapAspectXOverZ) > MapAspectTolerance)
        {
            note = $"NO TABLE CORNERS this placement — the live parchment measures {b.size.x:F1} x "
                   + $"{b.size.z:F1} world units, aspect {aspect:F2}, and the surveyed map is "
                   + $"{MapAspectXOverZ:F2}. The slab ratios ({TableToMapWidthRatio:F2}x wide, "
                   + $"{TableToMapDepthRatio:F2}x deep) describe a table around THAT map, so a table "
                   + "built around this one would be invented. The angular search seats this window "
                   + "instead";
            return false;
        }

        float halfX = b.size.x * 0.5f * TableToMapWidthRatio;
        float halfZ = b.size.z * 0.5f * TableToMapDepthRatio;
        Vector3 headPos = head.transform.position;

        // The two FURTHEST corners, found by measurement. Corners more than 90° off the gaze are
        // beside or behind the player and are not offered at all — the same bound TryMapChannelDeg
        // uses, for the same reason.
        float d1 = -1f, y1 = 0f;
        float d2 = -1f, y2 = 0f;
        for (int i = 0; i < 4; i++)
        {
            float cx = b.center.x + ((i & 1) == 0 ? -halfX : halfX);
            float cz = b.center.z + ((i & 2) == 0 ? -halfZ : halfZ);
            Vector3 flat = new Vector3(cx - headPos.x, 0f, cz - headPos.z);
            float d = flat.magnitude;
            if (d < 1e-3f)
                continue; // standing exactly on a corner: no honest angle to take
            float off = Mathf.DeltaAngle(gazeYawDeg, WorldYawDeg(flat));
            if (Mathf.Abs(off) > 90f)
                continue;
            if (d > d1)
            {
                d2 = d1;
                y2 = y1;
                d1 = d;
                y1 = off;
            }
            else if (d > d2)
            {
                d2 = d;
                y2 = off;
            }
        }
        if (d2 <= 0f)
            return false; // fewer than two corners are in front of him

        if (y1 <= y2)
        {
            leftDeg = y1;
            rightDeg = y2;
        }
        else
        {
            leftDeg = y2;
            rightDeg = y1;
        }
        if (rightDeg - leftDeg < MinCornerSeparationDeg)
        {
            note = $"NO TABLE CORNERS this placement — the two far corners measure {leftDeg:F0}° and "
                   + $"{rightDeg:F0}° off the gaze, only {rightDeg - leftDeg:F0}° apart (floor "
                   + $"{MinCornerSeparationDeg:F0}°). From where he is standing they are one corner "
                   + "seen twice, and seating two windows on them would stack them — which is the "
                   + "fault being fixed. The angular search seats this window instead";
            return false;
        }

        note = $"HIS TWO TABLE CORNERS ARE AT {leftDeg:F0}° (left) AND {rightDeg:F0}° (right) off "
               + $"this spawn's gaze, {rightDeg - leftDeg:F0}° apart — the far corners of the "
               + $"{b.size.x * TableToMapWidthRatio:F0} x {b.size.z * TableToMapDepthRatio:F0} "
               + "world-unit slab the parchment lies on, projected from the live head. THIS REPLACES "
               + "ModBuild 241's map-channel reading of the same photograph, which the user rejected "
               + "in as many words ('Du hast meinen Idealzustand falsch interpretiert … auf der "
               + "linken Ecke des Tisches … auf der oberen rechten Ecke des Tisches'). A corner is a "
               + "PLACE IN THE ROOM, not an angle off his gaze: walk round the table and it stays "
               + "where the table is";
        return true;
    }

    // =============================================================================================
    //  THE CORNER SEATS (2026-09-03) — the two far corners of the map table belong to two windows
    //  BY IDENTITY, and a corner is a POINT, not an angle.
    //
    //  USER REQUEST, translated: "I want the two windows (character UI and quest list) to spawn
    //  ABOVE THE TWO FAR CORNERS of the table: one above the left corner, one above the right
    //  corner. The height is fine as it is, but the windows do not spawn exactly above those
    //  corners, and that must change."
    //
    //  WHAT THE ModBuild 243 CORNER RULE GOT WRONG, measured off the ModBuild 410 log. It offered the
    //  corners as ANGLES off the live gaze, first come first served, at the NOMINAL reading distance:
    //    * the angle was right but the DISTANCE was 1.40 m along the gaze whatever the corner's own
    //      distance (≈2 m from the seat), so the window hung short of the corner, over the map;
    //    * "left is tried first" seated the CHARACTER SCREEN on the RIGHT corner (:2557 "the RIGHT
    //      far corner … at 12°") because the gaze at that instant was 23° off the table's forward
    //      and the left corner fell outside ±40° — and the quest log then found no corner at all
    //      (:2590, the free interval nearest the gaze);
    //    * a corner freed by the game hiding the quest log went to whichever window opened next
    //      (:4148 'UI Quest Popup', :4286 'UI Event Window'), so the two windows he named had no
    //      fixed home.
    //  All three are the same mistake: a corner was a RESERVATION SOMEBODY COULD WIN, not a place a
    //  named window lives. So:
    //    * the LEFT far corner is the CHARACTER SCREEN's (UIWindowID.PartyPanel) and the RIGHT far
    //      corner is the QUEST LOG's (matched by its QuestLogManager component, its ID is None) —
    //      the same two windows IsMapRoomPermanent names, and in the order the user wrote them;
    //    * left/right are taken FROM THE SPAWN POINT (MapRoomDriver.SeatFloor) facing the table
    //      centre — the frame the recentre now guarantees the head is in (VRRigDriver.RecenterMap),
    //      and a pure function of the parchment bounds and the seat, so the same on every entry;
    //    * the window's CENTRE is written exactly over the corner point (its X/Z), at the room's one
    //      bar height (unchanged), facing the spawn point, and NO seat search, no ladder and no
    //      soft clamp may move it horizontally — ComputeHmdPose re-asserts the corner after the
    //      clamp chain and prints the residual in millimetres;
    //    * the corners are NEVER offered to any other window (ArcSeatFreeInterval's pass −1 is off
    //      for everything else), whether or not the two are standing, so a window that opens while
    //      the quest log is hidden by the game cannot take its corner and be landed on when it
    //      comes back. Every other window keeps the reservation system exactly as it was.
    //  The corner window still BOOKS its angular interval in the registry, so the other windows'
    //  search sees it as taken and steps in front of it on the depth ladder where they must overlap.
    //  Grab-rotation stays authoritative afterwards, as for every spawn.
    // =============================================================================================

    /// <summary>
    /// A CORNER SEAT as <c>ComputeHmdPose</c> needs it: which corner, its world point, and the
    /// spawn point the window is faced at. Carried on the <c>SpawnAnchor</c> so the pre-reveal
    /// re-place and a presence-regain refloat re-derive the same pose from the same place.
    /// </summary>
    private struct ArcCornerSeat
    {
        /// <summary>True when this placement is a corner seat; every other field is then valid.</summary>
        public bool Seated;

        /// <summary>"LEFT" or "RIGHT", as seen from the spawn point facing the table.</summary>
        public string Which;

        /// <summary>The corner of the table top, world units (y = the table top surface).</summary>
        public Vector3 Point;

        /// <summary>The spawn point (the seat's tracking floor), world units — the facing target.</summary>
        public Vector3 SpawnPoint;

        /// <summary>Filled by <c>ComputeHmdPose</c>: how far the clamp chain had moved the window
        /// off the corner horizontally before the corner was re-asserted, mm at room scale.</summary>
        public float ClampedOffMm;

        /// <summary>
        /// Lateral offset of the window's DRAWN content from its HOST rect centre, WORLD units,
        /// + = the player's right — 0 until the content is measurable. THE CORNER IS FOR THE DRAWN
        /// CENTRE: the ModBuild 411 log (:4428) has the character screen drawing a 14° column at
        /// −32° inside a 73° frame, so a host centred on the corner would stand its visible column
        /// a metre left of it. <c>ComputeHmdPose</c> writes the host at
        /// <c>corner − offset × right</c>, which puts the content over the corner; the pre-reveal
        /// re-place refreshes this from the fitted geometry and moves the host accordingly.
        /// </summary>
        public float DrawnOffsetWorld;
    }

    /// <summary>
    /// Which corner window <paramref name="panel"/> is, if either. IS-A on the converted root's own
    /// <c>UIWindow</c> (the same identity the permanence rule uses): the character screen by its
    /// ID, the quest log by its own component because its ID is None. The party ASSEMBLY window
    /// (also permanent) is nested inside the character screen and never floats on its own in the
    /// map room, so it is deliberately not a corner window — two windows on one corner would be the
    /// stacking fault the corner rule exists to prevent.
    /// </summary>
    private static bool TryCornerWindowSide(ConvertedPanel? panel, out bool right)
    {
        right = false;
        if (panel == null)
            return false;
        // ModBuild 411 LESSON (LogOutput.log :1195/:1233 and :4282/:4309 — no CORNER SEAT clause
        // on either entry): this used WindowForPanel, which looks the panel up in `Converted`, and
        // ModalFallback.8.Convert calls PlaceAtHmd BEFORE Converted.Add — so at the one moment the
        // claim is made the lookup answered null for both windows, both fell through to the
        // angular search, and the corner path never ran. Same IS-A read IsPermanentPanel uses.
        UIWindow? window = panel.Target != null ? panel.Target.GetComponent<UIWindow>() : null;
        if (window == null)
            window = WindowForPanel(panel);
        if (window == null)
            return false;
        if (IsQuestLogWindow(window))
        {
            right = true;
            return true;
        }
        return window.ID == UIWindowID.PartyPanel;
    }

    /// <summary>
    /// THE CORNER AS A WORLD POINT — the far-left or far-right corner of the table top as seen
    /// from the SPAWN POINT facing the table centre. The slab is the surveyed multiple of the
    /// parchment about their shared centre (the same ratios <see cref="TryTableFarCornersDeg"/> and
    /// the shared anchor use, with the same aspect falsifier); "far" is the two corners with the
    /// largest reach along the spawn→centre direction, and left/right is the sign of their lateral
    /// offset from that line. Nothing here reads the head: a corner is a place in the room.
    /// </summary>
    private static bool TryTableFarCornerWorld(bool right, out Vector3 corner, out Vector3 spawnPoint,
        out Vector3 tableCentre, out string note)
    {
        corner = Vector3.zero;
        spawnPoint = Vector3.zero;
        tableCentre = Vector3.zero;
        note = "NO CORNER SEAT this placement — the parchment could not be measured (the room is "
               + "standing down), so this corner window was seated by the angular search instead";
        if (!MapRoom.MapRoomDriver.TryGetParchmentFrame(out Vector3 centre, out float _))
            return false;
        MeshRenderer? parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return false;
        Bounds b = parchment.bounds;
        if (b.size.x <= 1e-3f || b.size.z <= 1e-3f)
            return false;
        float aspect = b.size.x / b.size.z;
        if (Mathf.Abs(aspect - MapAspectXOverZ) > MapAspectTolerance)
        {
            note = $"NO CORNER SEAT this placement — the live parchment measures {b.size.x:F1} x "
                   + $"{b.size.z:F1} world units, aspect {aspect:F2}, and the surveyed map is "
                   + $"{MapAspectXOverZ:F2}, so the table the corner would belong to would be "
                   + "invented. The angular search seats this corner window instead";
            return false;
        }

        spawnPoint = MapRoom.MapRoomDriver.SeatFloor;
        tableCentre = centre;
        Vector3 forward = new Vector3(centre.x - spawnPoint.x, 0f, centre.z - spawnPoint.z);
        if (forward.sqrMagnitude < 1e-6f)
        {
            note = "NO CORNER SEAT this placement — the spawn point coincides with the table "
                   + "centre, so 'far' and 'left' have no meaning here. The angular search seats "
                   + "this corner window instead";
            return false;
        }
        forward.Normalize();
        Vector3 lateralAxis = Vector3.Cross(Vector3.up, forward); // + = the player's right

        float halfX = b.size.x * 0.5f * TableToMapWidthRatio;
        float halfZ = b.size.z * 0.5f * TableToMapDepthRatio;
        float topY = b.max.y;

        // The two corners furthest along the spawn→centre line are the far pair; among them the
        // requested side is the sign of the lateral offset. Ties in reach are the normal case (an
        // axis-aligned seat) and are broken by the lateral sign alone.
        float bestScore = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            float cx = centre.x + ((i & 1) == 0 ? -halfX : halfX);
            float cz = centre.z + ((i & 2) == 0 ? -halfZ : halfZ);
            Vector3 rel = new Vector3(cx - centre.x, 0f, cz - centre.z);
            float reach = Vector3.Dot(rel, forward);
            float lateral = Vector3.Dot(rel, lateralAxis);
            // Reach dominates (a far corner beats a near one by the whole table depth); the
            // lateral sign selects the side among the far pair.
            float score = reach * 1000f + (right ? lateral : -lateral);
            if (score > bestScore)
            {
                bestScore = score;
                corner = new Vector3(cx, topY, cz);
            }
        }
        note = $"THE {(right ? "RIGHT" : "LEFT")} FAR CORNER of the map table is at "
               + $"({corner.x:F2},{corner.y:F2},{corner.z:F2}) wu — the "
               + $"{b.size.x * TableToMapWidthRatio:F0} x {b.size.z * TableToMapDepthRatio:F0} "
               + "world-unit slab the parchment lies on, seen from the spawn point "
               + $"({spawnPoint.x:F2},{spawnPoint.y:F2},{spawnPoint.z:F2}) wu facing the table "
               + $"centre ({centre.x:F2},{centre.y:F2},{centre.z:F2}) wu";
        return true;
    }

    /// <summary>
    /// The world y the map room's bar-height rule will put THIS window's centre at — the same
    /// arithmetic <c>ComputeHmdPose</c>'s bar-height block applies after the clamps, computed here
    /// so a corner claim can book the TRUE distance from the head to the window's final centre
    /// (the delivered line then reads 0.0 % instead of "corrected" on every corner window).
    /// </summary>
    private static bool TryPredictMapRoomCentreY(Vector2 halfSizeWorld, out float centreY)
    {
        centreY = 0f;
        if (!TryMapTableTopWorldY(out float tableTopY, out float tableScale))
            return false;
        float halfWinYm = Mathf.Max(halfSizeWorld.y / tableScale, SharedAnchorMinHalfHeightMeters);
        float barYm = ResolveMapRoomBarHeightMeters(halfWinYm, out string _);
        centreY = tableTopY + (barYm + GrabBarDropMeters + halfWinYm) * tableScale;
        return true;
    }

    /// <summary>
    /// CLAIM A CORNER SEAT for a corner window. Returns true with the corner and the registry entry
    /// written; false with an EMPTY <paramref name="why"/> for a window that is not a corner window
    /// (the ordinary search runs), and false with a NON-empty <paramref name="why"/> for a corner
    /// window whose corner could not be measured this time (the ordinary search runs and the note
    /// is appended to its line, so a corner window seated elsewhere always says why).
    /// </summary>
    /// <param name="headPos">The head <c>ComputeHmdPose</c> measures from (height-corrected).</param>
    /// <param name="yawDeg">Degrees to rotate the spawn gaze by to face the corner from the head
    /// (the HOST rect's angle), + = right — what the raw pose is built from before the corner is
    /// re-asserted.</param>
    private static bool TryClaimCornerSeat(ConvertedPanel panel, Vector2 halfSizeWorld, float scale,
        float gazeYawDeg, Vector3 headPos, out int slot, out float yawDeg, out ArcCornerSeat corner,
        out string why)
    {
        slot = -1;
        yawDeg = 0f;
        corner = default;
        why = "";
        if (!TryCornerWindowSide(panel, out bool right))
            return false;
        if (!TryTableFarCornerWorld(right, out Vector3 point, out Vector3 spawnPoint,
                out Vector3 tableCentre, out string cornerNote))
        {
            why = cornerNote;
            return false;
        }

        Vector3 flat = new Vector3(point.x - headPos.x, 0f, point.z - headPos.z);
        if (flat.sqrMagnitude < 1e-6f)
        {
            why = "NO CORNER SEAT this placement — the head is standing exactly over the corner, "
                  + "so there is no honest direction to it. The angular search seats this corner "
                  + "window instead";
            return false;
        }
        // THE CORNER IS WHERE THE DRAWN CENTRE GOES; the host rect is recovered from it by the
        // content's own offset inside its frame (see ArcCornerSeat.DrawnOffsetWorld).
        float drawnWorldYaw = WorldYawDeg(flat);

        // The distance the registry books is the TRUE head→centre distance at the height the
        // bar-height rule will deliver, so the drawn interval is measured where the window hangs.
        float centreY = TryPredictMapRoomCentreY(halfSizeWorld, out float predictedY)
            ? predictedY
            : headPos.y;
        Vector3 centreWorld = new Vector3(point.x, centreY, point.z);
        float distWorld = Mathf.Max((centreWorld - headPos).magnitude, 1e-3f);
        float nominalDist = WindowDistanceMeters * scale;
        float halfWidthWorld = halfSizeWorld.x > 1e-4f
            ? halfSizeWorld.x
            : FallbackHalfWidthWorld(scale);
        ArcDrawnGeometry geo = MeasureArcDrawnGeometry(panel, halfWidthWorld, distWorld);

        // Everything standing describes itself truthfully first (registry only, no pose), so the
        // occupancy and overlap this line prints are measured against the room as it is.
        RefreshStandingArcClaims(-1);
        string standing = ArcSeatOccupancyText(gazeYawDeg);
        float hostWorldYaw = drawnWorldYaw - geo.OffsetDeg;
        yawDeg = Mathf.DeltaAngle(gazeYawDeg, hostWorldYaw);
        float halfAngle = geo.DrawnHalfDeg;
        ArcSeatFootprint(hostWorldYaw, geo.FrameHalfDeg, geo.OffsetDeg, halfAngle,
            out float footYaw, out float footHalf);
        int wouldBeLevel = ArcSeatDepthLevel(footYaw, footHalf, -1, out string blockers);
        float overlapDeg = ArcSeatWorstOverlapDeg(drawnWorldYaw, halfAngle, out string overlapWith);
        _lastSeatWaivedChooserGap = false;
        BesideClause(drawnWorldYaw, halfAngle, -1, distWorld, scale);

        corner = new ArcCornerSeat
        {
            Seated = true,
            Which = right ? "RIGHT" : "LEFT",
            Point = point,
            SpawnPoint = spawnPoint,
            DrawnOffsetWorld = geo.OffsetWorld,
        };

        why = $"IT IS A CORNER WINDOW — the {corner.Which} far corner of the map table is ITS BY "
              + "IDENTITY (character screen LEFT, quest log RIGHT — user ruling 2026-09-03), a "
              + "PLACE IN THE ROOM and not an angle off his gaze. " + cornerNote
              + $". From the head that corner is {distWorld / Mathf.Max(scale, 1e-4f):F2} m away at "
              + $"{Mathf.DeltaAngle(gazeYawDeg, drawnWorldYaw):F0}° off the spawn gaze (nominal "
              + $"reading distance {WindowDistanceMeters:F2} m — the difference is the corner's, "
              + "not a ladder step); the window's DRAWN CENTRE goes exactly over the corner point "
              + $"(its host rect {geo.OffsetDeg:F0}° / "
              + $"{geo.OffsetWorld / Mathf.Max(scale, 1e-4f):F3} m to the other side of that, "
              + "which is the content's own offset inside its frame) and it faces the spawn "
              + "point. NO SEAT SEARCH RAN and no clamp may move it sideways: ComputeHmdPose "
              + "re-asserts the corner after the clamp chain and prints the residual in mm. "
              + $"It books {halfAngle * 2f:F0}° of drawn content at world yaw {drawnWorldYaw:F0}° so "
              + $"the other windows' search sees it as taken. Standing set [{standing}]"
              + (wouldBeLevel > 0
                  ? $". ITS FOOTPRINT INTERSECTS {blockers} — a corner window takes NO depth step "
                    + "(the corner is the place), so it stands at the corner's distance behind or "
                    + "beside whatever is already there and NOTHING IS MOVED (user ruling). A "
                    + "window opened after it steps in front of it on the ladder as usual"
                  : ". Its footprint intersects nothing standing")
              + (overlapDeg > 0.5f
                  ? $". MEASURED: its drawn interval overlaps '{overlapWith}' by {overlapDeg:F0}° "
                    + "in angle"
                  : ". MEASURED: it overlaps nothing in angle")
              + ". GEOMETRY: " + geo.Note;

        int free = FirstFreeClaimIndex();
        if (free < 0)
        {
            why += $". THE REGISTRY IS FULL ({MaxWindowClaims} seats) — this corner window holds no "
                   + "reservation, so a later window's search cannot see it; it still stands on "
                   + "its corner";
            _arcSeatGeneration++;
            return true;
        }
        slot = free;
        _arcSeatWorldYaw[free] = drawnWorldYaw;
        _arcClaims[free] = new ArcClaim
        {
            Panel = panel,
            Name = PanelLogName(panel),
            CentreDeg = Mathf.DeltaAngle(gazeYawDeg, drawnWorldYaw),
            SpawnGazeWorldYaw = gazeYawDeg,
            HalfWidthDeg = halfAngle,
            DrawnOffsetDeg = geo.OffsetDeg,
            FrameHalfWidthDeg = geo.FrameHalfDeg,
            SlotReserved = geo.SlotReserved,
            ExtraGapDeg = ChooserGapFor(geo),
            DistanceWorld = distWorld,
            OverlapRank = 0,
            // Negative = further than nominal; ApplyArcDepth expresses it as an outward push, and
            // the corner re-assert in ComputeHmdPose makes the exact point irrelevant to the pose.
            DepthPullMeters = (nominalDist - distWorld) / Mathf.Max(scale, 1e-4f),
            OverlapDeg = overlapDeg,
            Permanent = IsPermanentPanel(panel),
            Corner = true,
            CornerPoint = point,
            CornerWhich = corner.Which,
        };
        _arcSeatGeneration++;
        return true;
    }

    /// <summary>
    /// The pre-reveal re-place of a CORNER window: the pose is the corner's and does not move, so
    /// only the REGISTRY is brought up to date with what the window now draws (the fitted width
    /// and its content offset, at the corner's true distance). Returns false when nothing
    /// material changed. Never runs the seat search — that is the whole point of a corner.
    /// </summary>
    private static bool TryRefreshCornerClaim(ConvertedPanel? panel, int slot, Vector2 halfSizeWorld,
        float scale, Vector3 headPos, ref ArcCornerSeat corner, out string note)
    {
        note = "";
        if (panel == null || slot < 0 || slot >= _arcClaims.Length)
            return false;
        if (!ReferenceEquals(_arcClaims[slot].Panel, panel) || !_arcClaims[slot].Corner)
            return false;
        Vector3 point = _arcClaims[slot].CornerPoint;
        float centreY = TryPredictMapRoomCentreY(halfSizeWorld, out float predictedY)
            ? predictedY
            : headPos.y;
        Vector3 centreWorld = new Vector3(point.x, centreY, point.z);
        float distWorld = Mathf.Max((centreWorld - headPos).magnitude, 1e-3f);
        float halfWidthWorld = halfSizeWorld.x > 1e-4f
            ? halfSizeWorld.x
            : FallbackHalfWidthWorld(scale);
        ArcDrawnGeometry geo = MeasureArcDrawnGeometry(panel, halfWidthWorld, distWorld);
        if (!geo.Measured)
            return false;
        float heldHalf = _arcClaims[slot].HalfWidthDeg;
        float heldOffset = _arcClaims[slot].DrawnOffsetDeg;
        float heldOffsetWorld = corner.DrawnOffsetWorld;
        if (Mathf.Abs(geo.DrawnHalfDeg - heldHalf) < 0.5f
            && Mathf.Abs(geo.OffsetDeg - heldOffset) < 0.5f)
            return false;
        // THE DRAWN CENTRE STAYS ON THE CORNER; it is the HOST that moves by the newly measured
        // content offset (ComputeHmdPose writes host = corner − offset × right). So the registry's
        // drawn yaw is re-read from the corner and the head, never derived from the old host.
        Vector3 flat = new Vector3(point.x - headPos.x, 0f, point.z - headPos.z);
        if (flat.sqrMagnitude >= 1e-6f)
            _arcSeatWorldYaw[slot] = WorldYawDeg(flat);
        _arcClaims[slot].HalfWidthDeg = geo.DrawnHalfDeg;
        _arcClaims[slot].DrawnOffsetDeg = geo.OffsetDeg;
        _arcClaims[slot].FrameHalfWidthDeg = geo.FrameHalfDeg;
        _arcClaims[slot].SlotReserved = geo.SlotReserved;
        _arcClaims[slot].ExtraGapDeg = ChooserGapFor(geo);
        _arcClaims[slot].DistanceWorld = distWorld;
        _arcClaims[slot].DepthPullMeters = (WindowDistanceMeters * scale - distWorld)
                                           / Mathf.Max(scale, 1e-4f);
        corner.DrawnOffsetWorld = geo.OffsetWorld;
        note = $"RE-MEASURED ON ITS DRAWN CONTENT while still render-hidden — the CORNER is "
               + $"unchanged. It had booked ±{heldHalf:F0}° at offset {heldOffset:F0}° (the pre-fit "
               + $"frame); it draws ±{geo.DrawnHalfDeg:F0}° at offset {geo.OffsetDeg:F0}°, so the "
               + $"registry now holds that at world yaw {_arcSeatWorldYaw[slot]:F0}°. THE HOST MOVES "
               + $"BY THE CONTENT OFFSET ({heldOffsetWorld / Mathf.Max(scale, 1e-4f):F3} m → "
               + $"{geo.OffsetWorld / Mathf.Max(scale, 1e-4f):F3} m) so that the DRAWN centre stays "
               + $"over the {_arcClaims[slot].CornerWhich} far corner — invisible at this moment. "
               + geo.Note;
        return true;
    }

    /// <summary>
    /// Forget the arc reservation <paramref name="panel"/> holds, if any, so its next placement
    /// claims afresh. Used by the one-shot corner re-seat: a corner window that was seated by the
    /// angular search (the room was not standing when it converted, or an earlier build placed it)
    /// must not "re-use the seat it already holds".
    /// </summary>
    private static void ReleaseArcClaimOf(ConvertedPanel panel, string why)
    {
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (!ReferenceEquals(_arcClaims[i].Panel, panel))
                continue;
            string name = _arcClaims[i].Name ?? "<window>";
            _arcClaims[i] = default;
            _arcSeatGeneration++;
            VRLog.Info("WorldUI", $"MAP ROOM WINDOW SLOT DROPPED: '{name}' gave up reservation {i} "
                                  + $"— {why}. The window is about to be placed again and claims "
                                  + "afresh; nothing else standing is touched.");
        }
    }

    /// <summary>
    /// ONE-SHOT: seat the two corner windows on their corners when the room was placed AFTER they
    /// were already floating (user request 2026-09-03 — "auch wenn man zum Tisch aus einem
    /// Szenario zurückkehrt … aktuell spawnen dann beide in der Mitte übereinander"). Called by
    /// <c>MapRoomDriver.NoteRecentered</c> exactly once per room engage, after the map rig's
    /// recentre, i.e. when the seat, the spawn point and the table are all measurable and the head
    /// is at the seat. On the ordinary entry (the ModBuild 411 log: both entries convert the
    /// windows after MAP ROOM ENGAGED) it finds nothing floating and does nothing.
    ///
    /// <para>Same recipe as the presence-regain refloat: the grab FRAME is re-seated through
    /// <c>ComputeHmdPose</c> (which announces itself to the pose watch as a placement), a window
    /// the player or a peer has moved is left alone, and a window already on a corner claim is
    /// left alone. A window seated elsewhere first drops its reservation so the claim runs the
    /// corner path instead of re-using the old seat.</para>
    /// </summary>
    internal static void ReseatCornerWindowsOnce(string why)
    {
        int seen = 0, moved = 0;
        for (int i = 0; i < Converted.Count; i++)
        {
            WindowPanel wp = Converted[i];
            if (wp.Panel == null || !wp.Panel.IsAlive)
                continue;
            if (!TryCornerWindowSide(wp.Panel, out bool right))
                continue;
            seen++;
            string name = wp.Window != null ? wp.Window.name : PanelLogName(wp.Panel);
            string side = right ? "RIGHT" : "LEFT";
            if (wp.Grab != null && (wp.Grab.UserMoved || wp.Grab.PeerPlaced))
            {
                // HW-VERIFY
                VRLog.Note("WorldUI", $"MAP ROOM CORNER: '{name}' ({side} corner window) NOT re-seated "
                                      + $"on the corner ({why}) — "
                                      + (wp.Grab.UserMoved ? "the player moved it" : "a peer placed it")
                                      + ", so its pose is not the mod's to write (user ruling: parked "
                                      + "windows stay put).");
                continue;
            }
            bool onCorner = false;
            for (int s = 0; s < _arcClaims.Length; s++)
            {
                if (ReferenceEquals(_arcClaims[s].Panel, wp.Panel) && _arcClaims[s].Corner)
                    onCorner = true;
            }
            if (onCorner)
                continue; // already where it belongs — a second placement would be a visible jump
            ReleaseArcClaimOf(wp.Panel, why);
            bool levelMessage = IsLevelMessageWindow(wp.Window);
            bool placed = false;
            if (wp.Grab != null)
            {
                Vector2 half = PanelWorldHalfSize(wp.Panel, PanelLayout.WorldScale * wp.ExtraScale);
                if (ComputeHmdPose(out Vector3 pos, out Quaternion rot, out _, 0, half, wp.Panel,
                        levelMessage))
                {
                    wp.Grab.PlaceFrameAt(pos, rot);
                    placed = true;
                }
            }
            else
            {
                placed = PlaceAtHmd(wp.Panel, wp.ExtraScale, 0, levelMessage);
            }
            if (placed)
                moved++;
            // HW-VERIFY
            VRLog.Note("WorldUI", $"MAP ROOM CORNER: '{name}' ({side} corner window) was already "
                                  + $"floating when the room was placed ({why}) — re-seated ONCE "
                                  + (placed
                                      ? "onto its corner through the ordinary placement (its MAP ROOM "
                                        + "WINDOW SLOT line above carries the CORNER SEAT clause; if it "
                                        + "does not, that line says which path placed it and why)."
                                      : "was ATTEMPTED but no pose could be computed (no head camera?) — "
                                        + "it keeps the pose it had."));
        }
        if (seen == 0)
            VRLog.Info("WorldUI", $"MAP ROOM CORNER: no corner window was floating when the room was "
                                  + $"placed ({why}) — nothing to re-seat; the windows that open from "
                                  + "here on take their corners at spawn.");
        else if (moved == 0)
            VRLog.Info("WorldUI", $"MAP ROOM CORNER: {seen} corner window(s) were floating when the "
                                  + $"room was placed ({why}) and none needed re-seating.");
    }

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
    /// <param name="source">Which rule answered — see <see cref="ArcSeatSource"/>.</param>
    private static bool ArcSeatFreeInterval(float gazeYawDeg, float centreLimit, float halfAngle,
        bool haveCorners, float cornerLeftDeg, float cornerRightDeg,
        bool haveChannel, float channelLo, float channelHi,
        out float best, out ArcSeatSource source)
    {
        best = 0f;
        source = ArcSeatSource.None;
        if (!_relaxChooserGap)
            _lastSeatWaivedChooserGap = false;

        // ---- PASS −1: HIS TWO TABLE CORNERS, AND THEY ARE TRIED BEFORE ANYTHING ELSE.
        //      The window's DRAWN CENTRE goes ON the corner — not its inner edge beside the map,
        //      which is the ModBuild 241 reading the user corrected (see TryTableFarCornersDeg).
        //      LEFT IS TRIED FIRST, so the first window to claim takes the left corner and the
        //      second takes the right, which reproduces 'ideale_position.jpg' when the character
        //      screen claims first. This file holds no per-window knowledge and is not about to
        //      start, so a burst that opens in a different order swaps the two sides — the same
        //      honest limit the ModBuild 241 tie rule already carried.
        if (haveCorners)
        {
            //      ModBuild 245 — A CORNER IS BOUNDED BY THE FIELD, NOT BY THE WINDOW'S EDGES.
            //      Until 244 this tested the corner against `centreLimit` = arcHalf − halfAngle,
            //      i.e. it demanded that the window's whole WIDTH fit inside the field once seated
            //      on the corner. For the character screen (36° half) that limit is 4°, so BOTH
            //      corners at −33°/+35° were refused before they were ever looked at, and the log
            //      said so in as many words: "neither corner was both free of every standing window
            //      and inside ±40.0° for a window of this width (72°)". The narrower reservation
            //      above removes most of that, but the rule was wrong on its own terms: his ruling
            //      is that these two windows BELONG on the corners of the table, and a corner is a
            //      place in the room. The bound that survives is that the corner's own CENTRE is in
            //      the field — a place he cannot see at all is not a place — and an outer edge that
            //      reaches past ±40° is read by turning the head, which is what one does at a table.
            for (int k = 0; k < 2; k++)
            {
                float a = k == 0 ? cornerLeftDeg : cornerRightDeg;
                if (Mathf.Abs(a) > ArcPlacementHalfDeg() + 1e-3f)
                    continue; // the corner itself is behind him — not a place he can be offered
                if (!ArcSeatIsFree(gazeYawDeg + a, halfAngle))
                    continue; // the other window is already on it
                best = a;
                source = ArcSeatSource.TableCorner;
                return true;
            }
        }

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
            float edge = _arcClaims[i].HalfWidthDeg + halfAngle + NeighbourGapDegrees
                         + ExtraGapOf(i);
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
            source = pass == 0 ? ArcSeatSource.BesideTheMap : ArcSeatSource.NearestFreeInterval;
            return true;
        }

        // ---- THE ONE RETRY (2026-09-03): the chooser gap is a demand, never a reservation. When
        //      no seat inside the field of view keeps the extra clearance off a chooser's slot,
        //      the same search runs once more at the plain neighbour gap — the pre-existing rule —
        //      before the field is declared full, so his first rule (in view, always) is never
        //      traded for the gap, and the placement line says which of the two answered.
        if (!_relaxChooserGap && AnyChooserGapStanding())
        {
            _relaxChooserGap = true;
            try
            {
                bool relaxed = ArcSeatFreeInterval(gazeYawDeg, centreLimit, halfAngle,
                    haveCorners, cornerLeftDeg, cornerRightDeg, haveChannel, channelLo, channelHi,
                    out best, out source);
                if (relaxed)
                    _lastSeatWaivedChooserGap = true;
                return relaxed;
            }
            finally
            {
                _relaxChooserGap = false;
            }
        }
        return false;
    }

    /// <summary>Which rule produced a seat. Printed on the placement line, and the reason the
    /// map-channel re-clear (<see cref="ArcSeatReClearChannel"/>) is applied to a
    /// <see cref="BesideTheMap"/> seat and NEVER to a <see cref="TableCorner"/> one: a corner is a
    /// point, so a width change must not slide the window off it.</summary>
    private enum ArcSeatSource
    {
        /// <summary>No free seat anywhere inside the field of view.</summary>
        None,

        /// <summary>Seated on one of the map table's two far corners — his ideal photograph.</summary>
        TableCorner,

        /// <summary>Seated beside the parchment's own silhouette (the ModBuild 241 map channel).</summary>
        BesideTheMap,

        /// <summary>The free interval nearest the gaze, the search every build since 183 has run.</summary>
        NearestFreeInterval,
    }

    /// <summary>
    /// TRUE WHEN THIS WINDOW'S WHOLE FOOTPRINT (drawn ∪ frame = the hit rect) CLEARS EVERY STANDING
    /// FOOTPRINT. Stricter than <see cref="ArcSeatIsFree"/>, which tests only the DRAWN intervals.
    ///
    /// <para>IT EXISTS FOR THE OUTWARD LADDER AND FOR NOTHING ELSE. Going further away is only worth
    /// taking when it produces a genuinely clean seat: a window that is further and still overlaps a
    /// neighbour's transparent frame is BEHIND the thing that will catch the laser, which is the
    /// wrong side of the ladder and strictly worse than today's "step in front". So the outward
    /// search demands this, and when it cannot be met the placement falls through to the existing
    /// nearer-step behaviour unchanged.</para>
    /// </summary>
    private static bool ArcSeatFootprintIsFree(float hostWorldYaw, float frameHalfDeg,
        float drawnOffsetDeg, float drawnHalfDeg, int skipSlot)
    {
        ArcSeatFootprint(hostWorldYaw, frameHalfDeg, drawnOffsetDeg, drawnHalfDeg,
            out float footYaw, out float footHalf);
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel == null || i == skipSlot)
                continue;
            ArcSeatFootprint(i, out float otherYaw, out float otherHalf);
            if (otherHalf + footHalf - Mathf.Abs(Mathf.DeltaAngle(footYaw, otherYaw))
                > ArcAuditOverlapToleranceDeg)
                return false;
        }
        return true;
    }

    /// <summary>
    /// THE OUTWARD LADDER — buy arc by standing the window FURTHER AWAY instead of letting it
    /// collide. His own suggestion, verbatim (2026-08-24): "Wenn du manche Fenster etwas (ein klein
    /// bisschen) weiter weg spawnst ist der Halbkreis zu spawnen auch größer."
    ///
    /// <para>HE IS RIGHT AND THE SHIPPED LADDER RAN THE OTHER WAY. Until this build the ONE ladder
    /// pulled an overflowing window TOWARD the head, which makes it ANGULARLY WIDER — the log line
    /// from his own collision photograph says so out loud: "Being nearer made it 49° wide, so its
    /// seat was pulled back to 16°". Nearer buys draw order and costs arc; further costs apparent
    /// size and BUYS arc. When the complaint is "sie spawnen zusammen ineinander", arc is the thing
    /// that is short.</para>
    ///
    /// <para>MEASURED AGAINST THE PHOTOGRAPHED CASE. In the ModBuild 242 hardware log the encounter
    /// window drew 46° at 1.40 m against a standing quest popup of ±13°, "together they ask for 74°
    /// of the 80° the field of view supplies", and it took a 19°-of-49° overlap. Three outward steps
    /// (0.12 m, 8.6 % further, 8 % smaller on screen) take it to 42.6°, which fits the free interval
    /// with the neighbour gap to spare. That is what "ein klein bisschen" buys.</para>
    ///
    /// <para>IT IS TRIED ONLY WHEN THE ALTERNATIVE IS A COLLISION, and it must produce a CLEAN seat:
    /// both the drawn interval and the whole FOOTPRINT have to clear everything standing
    /// (<see cref="ArcSeatFootprintIsFree"/>). A window that is further away and still overlaps a
    /// neighbour's transparent frame sits BEHIND the plane that will take the laser, which is worse
    /// than today's behaviour, so that candidate is refused and the placement falls through to the
    /// existing nearer-step path with nothing changed.</para>
    ///
    /// <para>IT IS BOUNDED IN REAL METRES, NOT IN STEPS, so the cost is readable: at most
    /// <see cref="MaxArcOutwardMeters"/> past the reading distance. Apparent size scales as
    /// 1/distance, so the worst case is a stated percentage on the placement line rather than an
    /// open-ended drift — and the reading distance itself is a value the user moved by hand in
    /// ModBuild 241, so nothing here may quietly re-tune it for every window.</para>
    /// </summary>
    /// <param name="geo">The geometry measured at the nominal distance; re-derived per step, by
    /// value, so the caller's copy is untouched until it accepts a step.</param>
    /// <param name="skipSlot">The window's own registry slot (−1 at spawn, when it holds none).</param>
    /// <param name="steps">How many steps out were spent (0 = the ladder found nothing).</param>
    /// <param name="distWorld">The reading distance the accepted step sits at, world units.</param>
    private static bool TryArcSeatFurtherOut(ArcDrawnGeometry geo, float nominalDist, float scale,
        float gazeYawDeg, float arcHalf, int skipSlot,
        bool haveCorners, float cornerLeftDeg, float cornerRightDeg,
        bool haveChannel, float channelLo, float channelHi,
        out int steps, out float distWorld, out float seatOffset, out ArcSeatSource source,
        out float wouldFitMeters)
    {
        steps = 0;
        distWorld = nominalDist;
        seatOffset = 0f;
        source = ArcSeatSource.None;
        wouldFitMeters = 0f;
        if (scale <= 1e-4f || nominalDist <= 1e-4f)
            return false;
        float stepWorld = ArcOutwardStepMeters * scale;
        int maxSteps = Mathf.FloorToInt(MaxArcOutwardMeters / Mathf.Max(ArcOutwardStepMeters, 1e-4f));
        int sweepSteps = Mathf.FloorToInt(OutwardReportCeilingMeters
                                          / Mathf.Max(ArcOutwardStepMeters, 1e-4f));
        for (int k = 1; k <= sweepSteps; k++)
        {
            float d = nominalDist + stepWorld * k;
            ArcDrawnGeometry g = geo;
            g.ReDeriveAt(d);
            float halfAngle = g.DrawnHalfDeg;
            float centreLimit = arcHalf - halfAngle;
            if (centreLimit < 0f)
                continue; // still wider than the whole field of view: another step may fix it
            if (!ArcSeatFreeInterval(gazeYawDeg, centreLimit, halfAngle, haveCorners, cornerLeftDeg,
                    cornerRightDeg, haveChannel, channelLo, channelHi, out float pick,
                    out ArcSeatSource src))
                continue;
            if (!ArcSeatFootprintIsFree(gazeYawDeg + pick - g.OffsetDeg, g.FrameHalfDeg, g.OffsetDeg,
                    halfAngle, skipSlot))
                continue; // clean in ANGLE but its hit rect still lands on someone: not worth it
            // PAST THE BUDGET THIS IS A REPORT, NOT A PLACEMENT. The sweep keeps running past
            // MaxArcOutwardMeters for one reason only: so the line can name the distance that WOULD
            // have worked and what it would have cost, instead of saying "it did not fit" and
            // leaving the next round to re-derive the number by hand. Nothing is moved by it.
            if (k > maxSteps)
            {
                wouldFitMeters = d / scale;
                return false;
            }
            steps = k;
            distWorld = d;
            seatOffset = pick;
            source = src;
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
            if (_arcClaims[i].Corner)
                sb.Append(" CORNER/").Append(_arcClaims[i].CornerWhich ?? "?");
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
    /// <para>THE CHOICE RULE — HIS TWO TABLE CORNERS FIRST, THEN THE FREE INTERVAL NEAREST THE
    /// GAZE. Since ModBuild 243 the first candidates offered are the map table's two FAR CORNERS,
    /// with the window's drawn centre ON the corner — that is what
    /// <c>.planning/debug/ideale_position.jpg</c> actually shows and what the user said in words
    /// after ModBuild 241 read the same photograph as an angle ("Du hast meinen Idealzustand falsch
    /// interpretiert … auf der linken Ecke des Tisches … auf der oberen rechten Ecke des Tisches").
    /// See <see cref="TryTableFarCornersDeg"/> for the pixels. If neither corner is free and inside
    /// the field of view, the search falls through to exactly what ran before: the gaze itself, the
    /// two edges of the MAP CHANNEL, and for every standing seat the two angles that put this window
    /// against that seat's left and right edge; one of those is always the optimum, so testing
    /// 3 + 2N angles finds it exactly, with no stepping and no search tolerance. See
    /// <see cref="ArcSeatFreeInterval"/> for the passes and for why the map-clearing pass breaks its
    /// tie LEFT while the fallback pass keeps the RIGHT-first tie every build since 183 has used.</para>
    ///
    /// <para>AND WHEN NOTHING IS FREE, THE FIRST REMEDY IS TO STAND FURTHER BACK, NOT TO COLLIDE.
    /// His own suggestion (2026-08-24): "Wenn du manche Fenster etwas (ein klein bisschen) weiter weg
    /// spawnst ist der Halbkreis zu spawnen auch größer." <see cref="TryArcSeatFurtherOut"/> walks
    /// the window out in <see cref="ArcOutwardStepMeters"/> rungs, re-deriving its angular width at
    /// each one, and takes the first distance at which a genuinely clean seat exists — clean in the
    /// drawn interval AND in the whole footprint. Only when that fails too does the old behaviour
    /// run: seat it inside the field of view anyway and step it NEARER so it draws in front.</para>
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
    /// <param name="headPos">The head <c>ComputeHmdPose</c> measures from (height-corrected) —
    /// read only by the corner seat, which books the true head→corner distance.</param>
    /// <param name="corner">The corner seat when this window is one of the two corner windows and
    /// its corner could be measured (<see cref="ArcCornerSeat.Seated"/>); default otherwise.</param>
    /// <param name="cornerRefusal">Non-empty only for a CORNER window that could not be seated on
    /// its corner this time — the reason, for the MAP ROOM CORNER line.</param>
    /// <param name="why">Human-readable reason for the log line.</param>
    /// <returns>true when the map room's arc governs this placement.</returns>
    private static bool TryClaimArcSeat(ConvertedPanel? panel, bool levelMessage,
        Vector2 halfSizeWorld, float scale, float gazeYawDeg, Vector3 headPos,
        out int slot, out float yawDeg, out int overlapRank, out float foregroundPullWorld,
        out ArcCornerSeat corner, out string cornerRefusal, out string why)
    {
        slot = -1;
        yawDeg = 0f;
        overlapRank = 0;
        foregroundPullWorld = 0f;
        corner = default;
        cornerRefusal = "";
        why = "";
        if (panel == null || levelMessage || !MapRoom.MapRoomDriver.Active)
            return false;
        if (ReferenceEquals(panel, _errorPanel))
            return false; // not a Converted window — no release path would ever free its seat
        if (IsHoverCardPanel(panel))
            return false; // TickHoverCards owns its pose (and it churns on every mouseover)

        LogArcSeatGeometryOnce();
        _arcLastBesideClause = string.Empty;
        _lastSeatWaivedChooserGap = false;

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
            BesideClause(_arcSeatWorldYaw[i], _arcClaims[i].HalfWidthDeg, i,
                _arcClaims[i].DistanceWorld, scale);
            why = $"re-uses the seat it already holds (drawn content at world yaw "
                  + $"{_arcSeatWorldYaw[i]:F0}°±{_arcClaims[i].HalfWidthDeg:F0}°, frame centred "
                  + $"{_arcClaims[i].DrawnOffsetDeg:F0}° off that, so the host goes to "
                  + $"{yawDeg:F0}° off the gaze this time) — a refloat must not take a second seat, "
                  + "and it returns to the same place in the ROOM even if the player has turned "
                  + "since";
            if (_arcClaims[i].Corner)
            {
                // A corner window's place is the corner, not the angle: hand the point back so the
                // refloat re-asserts it after the clamps exactly as the spawn did.
                corner = new ArcCornerSeat
                {
                    Seated = true,
                    Which = _arcClaims[i].CornerWhich ?? "?",
                    Point = _arcClaims[i].CornerPoint,
                    SpawnPoint = MapRoom.MapRoomDriver.SeatFloor,
                    // The content offset the registry holds, back in world units at the booked
                    // distance (the same inversion ArcSeatDeliveredNote uses).
                    DrawnOffsetWorld = Mathf.Tan(Mathf.Clamp(_arcClaims[i].DrawnOffsetDeg, -89f, 89f)
                                                 * Mathf.Deg2Rad) * _arcClaims[i].DistanceWorld,
                };
                why += $" — and it is a CORNER SEAT: its centre goes back over the {corner.Which} "
                       + "far corner";
            }
            return true;
        }

        // ---- THE CORNER WINDOWS FIRST (2026-09-03). The character screen and the quest log own
        //      the two far corners by identity; no search, no ladder. A corner window whose corner
        //      could not be measured falls through to the ordinary search below and carries the
        //      reason on its line.
        if (TryClaimCornerSeat(panel, halfSizeWorld, scale, gazeYawDeg, headPos, out slot,
                out yawDeg, out corner, out why))
        {
            overlapRank = 0;
            foregroundPullWorld = slot >= 0 ? _arcClaims[slot].DepthPullMeters * scale : 0f;
            return true;
        }
        cornerRefusal = why;
        why = "";

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

        // ---- (b) AS FEW COLLISIONS AS POSSIBLE — his second rule. HIS TWO TABLE CORNERS ARE TRIED
        //          FIRST (ModBuild 243, the correction to 241's reading of his photograph), then the
        //          free interval nearest the gaze, with the MAP ITSELF as a thing to be clear of.
        bool cornersMeasured = TryTableFarCornersDeg(gazeYawDeg, out float cornerLeftDeg,
            out float cornerRightDeg, out string cornerNote);
        // THE CORNERS ARE RESERVED BY IDENTITY (2026-09-03) and are never offered to the search:
        // the LEFT one is the character screen's and the RIGHT one the quest log's, whether or not
        // they are standing, so a window that opens while the game has hidden the quest log cannot
        // take its corner and be landed on when it returns. The measurement still prints so the
        // corners' angles stay readable on every line.
        bool haveCorners = false;
        if (cornersMeasured)
            cornerNote += " — RESERVED BY IDENTITY (character screen LEFT, quest log RIGHT, user "
                          + "ruling 2026-09-03): neither corner is offered to this window";
        bool haveChannel = TryMapChannelDeg(gazeYawDeg, out float channelLo, out float channelHi,
            out string channelNote);
        bool haveFree = ArcSeatFreeInterval(gazeYawDeg, centreLimit, halfAngle, haveCorners,
            cornerLeftDeg, cornerRightDeg, haveChannel, channelLo, channelHi, out float bestFree,
            out ArcSeatSource source);

        // ---- (b2) NOTHING FREE AT THE READING DISTANCE? THEN STAND FURTHER BACK BEFORE COLLIDING.
        //           His own suggestion, and the one lever that makes the same arc hold more windows.
        //           It never moves a window that already had a seat, and it is refused unless the
        //           result is clean in ANGLE and in FOOTPRINT both — see TryArcSeatFurtherOut.
        int outwardSteps = 0;
        float seatDistWorld = nominalDist;
        float outwardWouldFit = 0f;
        string outwardNote = "";
        if (!haveFree
            && TryArcSeatFurtherOut(geo, nominalDist, scale, gazeYawDeg, arcHalf, -1, haveCorners,
                cornerLeftDeg, cornerRightDeg, haveChannel, channelLo, channelHi, out outwardSteps,
                out seatDistWorld, out bestFree, out source, out outwardWouldFit))
        {
            float wasHalf = halfAngle;
            geo.ReDeriveAt(seatDistWorld);
            halfAngle = geo.DrawnHalfDeg;
            centreLimit = Mathf.Max(0f, arcHalf - halfAngle);
            widerThanArc = arcHalf - halfAngle < 0f;
            haveFree = true;
            float outMeters = (seatDistWorld - nominalDist) / Mathf.Max(scale, 1e-4f);
            float finalMeters = seatDistWorld / Mathf.Max(scale, 1e-4f);
            outwardNote = $". OUTWARD LADDER — IT SPAWNS A LITTLE FURTHER AWAY INSTEAD OF COLLIDING: "
                          + $"no free interval existed at {WindowDistanceMeters:F2} m, where this "
                          + $"window draws {wasHalf * 2f:F0}°. {outwardSteps} step(s) of "
                          + $"{ArcOutwardStepMeters:F2} m put it at {finalMeters:F2} m, where it "
                          + $"draws {halfAngle * 2f:F0}° — {wasHalf * 2f - halfAngle * 2f:F0}° "
                          + "narrower, which is what opened the seat. THE PRICE, MEASURED: "
                          + $"{outMeters / finalMeters * 100f:F0}% smaller on screen (apparent size "
                          + "scales as 1/distance). This is his own remedy, verbatim: 'Wenn du "
                          + "manche Fenster etwas (ein klein bisschen) weiter weg spawnst ist der "
                          + "Halbkreis zu spawnen auch größer'. The step is spent ONLY against a "
                          + "collision — a window with a free seat never moves — and it was taken "
                          + "only because the result is clean in ANGLE and in FOOTPRINT both, so "
                          + "nothing behind it can catch this window's laser or the other way round";
        }

        // The chosen position of the DRAWN CENTRE, degrees off the spawn gaze. `yawDeg` (the HOST
        // rect's angle, which is what the placement rotates to) is derived from it below.
        float seatOffset;
        bool clearsChannel = source == ArcSeatSource.BesideTheMap;

        float demandAll = ArcSeatDemandDeg(halfAngle * 2f);
        if (haveFree)
        {
            seatOffset = bestFree;
            why = source == ArcSeatSource.TableCorner
                ? $"IT TOOK ONE OF HIS TABLE CORNERS — the {(Mathf.Abs(seatOffset - cornerLeftDeg) < Mathf.Abs(seatOffset - cornerRightDeg) ? "LEFT" : "RIGHT")} "
                  + $"far corner of the map table, at {seatOffset:F0}°±{halfAngle:F0}° off this "
                  + $"spawn's gaze, inside the measured ±{arcHalf:F1}° field of view. The window's "
                  + "DRAWN CENTRE sits ON the corner, which is what 'ideale_position.jpg' measures "
                  + "(the left window's bar is centred 6 px from the corner out of the 1835 px the "
                  + "far edge spans) and what he said after ModBuild 241 read the same picture as an "
                  + "angle: 'auf der linken Ecke des Tisches … auf der oberen rechten Ecke des "
                  + "Tisches'. The corner is a PLACE IN THE ROOM: it was computed once, here, and "
                  + "this window will not follow his head or his feet afterwards"
                : cleanBefore + overlapBefore == 0 && !clearsChannel
                ? $"the room was empty, so it took the gaze itself; the window draws {halfAngle * 2f:F0}° "
                  + $"wide and the measured field of view is ±{arcHalf:F1}°"
                : cleanBefore + overlapBefore == 0
                ? $"the room was empty BUT THE MAP IS NOT NOTHING, so it took the nearest angle "
                  + $"BESIDE the map instead of the gaze itself: {seatOffset:F0}°±{halfAngle:F0}° "
                  + $"of {halfAngle * 2f:F0}°-wide DRAWN content, inside the measured "
                  + $"±{arcHalf:F1}° field of view. NOTE: this is the ModBuild 241 fallback, NOT his "
                  + "corner rule — no table corner was free and inside the field of view this time, "
                  + "and the corner line below says why"
                : $"the FREE INTERVAL NEAREST THE GAZE ({halfAngle * 2f:F0}°-wide DRAWN content, "
                  + $"seated at {seatOffset:F0}°±{halfAngle:F0}° inside the measured "
                  + $"±{arcHalf:F1}° field of view with a {NeighbourGapDegrees:F0}° gap) — ANGLE "
                  + "ALONE SOLVED THE VISIBLE COLLISION, which is the first lever and the one "
                  + "always tried first; whether it also needs a depth step for its FRAME is "
                  + "decided below. The windows already standing "
                  + $"[{standing}] were not touched";
            why += outwardNote;
            if (_lastSeatWaivedChooserGap)
                why += $". CHOOSER GAP WAIVED: no seat inside ±{arcHalf:F1}° kept the "
                       + $"{NeighbourGapDegrees + ChooserSlotExtraGapDegrees:F0}° clearance off the "
                       + "standing chooser's reserved sub-view slot, so the search ran once more at "
                       + $"the plain {NeighbourGapDegrees:F0}° neighbour gap and this is that "
                       + "answer — in the field of view outranks the gap (his first rule)";
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
                  + ". THE OUTWARD LADDER WAS TRIED FIRST AND COULD NOT PAY: walking this window out "
                  + $"to {WindowDistanceMeters + MaxArcOutwardMeters:F2} m in "
                  + $"{ArcOutwardStepMeters:F2} m rungs never produced an interval that was free in "
                  + "ANGLE and in FOOTPRINT at once, so 'ein klein bisschen weiter weg' does not "
                  + "reach here and the nearer step below is what is left"
                  + (outwardWouldFit > 0f
                      ? $". THE DISTANCE THAT WOULD HAVE WORKED, MEASURED RATHER THAN LEFT OPEN: "
                        + $"{outwardWouldFit:F2} m, which is "
                        + $"{(1f - WindowDistanceMeters / outwardWouldFit) * 100f:F0}% smaller on "
                        + $"screen than {WindowDistanceMeters:F2} m — past the "
                        + $"{MaxArcOutwardMeters:F2} m this build is allowed to spend, and past what "
                        + "'nicht viel weiter weg' can mean. It is printed so the trade is HIS to "
                        + "take or refuse rather than one this file made silently"
                      : ". AND NO DISTANCE UP TO "
                        + $"{WindowDistanceMeters + OutwardReportCeilingMeters:F2} m would have "
                        + "worked either — the sweep ran past its own budget purely to check. That "
                        + "means the room is oversubscribed in a way DISTANCE cannot fix, and only a "
                        + "NARROWER window can")
                  + (haveChannel
                      ? ". THE MAP CHANNEL WAS GIVEN UP, WHICH IS ITS STATED DEGRADATION: no angle "
                        + $"inside ±{arcHalf:F1}° both cleared the map's own "
                        + $"[{channelLo:F0}°,{channelHi:F0}°] and cleared every standing window, so "
                        + "that demand yielded and this window was seated by exactly the search that "
                        + "ran before it. Keeping the map clear is a demand, never a reservation — "
                        + "it is honoured while it is free and never at the cost of his first rule"
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
        if (outwardSteps > 0)
        {
            // THE TWO LADDERS ARE MUTUALLY EXCLUSIVE BY CONSTRUCTION. The outward search only
            // accepts a step whose FOOTPRINT clears everything standing, so the level above is 0 and
            // the depth term is the outward push instead — a NEGATIVE pull, which ApplyArcDepth has
            // always been able to express. If the level is not 0 here, ArcSeatFootprintIsFree and
            // ArcSeatDepthLevel disagree about the same two rectangles and the line says so rather
            // than silently taking a step in the opposite direction to the one just bought.
            string contradiction = overlapRank > 0
                ? $". FALSIFIER TRIPPED: the outward step was accepted as footprint-clean and the "
                  + $"depth ladder then reported level {overlapRank} against {blockers} on the same "
                  + "geometry. The outward distance is kept and the nearer step is NOT taken — two "
                  + "ladders in opposite directions would cancel — but these two tests must agree "
                  + "and one of them is wrong"
                : "";
            overlapRank = 0;
            foregroundPullWorld = -(seatDistWorld - nominalDist);
            why += contradiction;
            why += $". DEPTH: level 0 at a reading distance of "
                   + $"{seatDistWorld / Mathf.Max(scale, 1e-4f):F2} m — the OUTWARD ladder above set "
                   + "this distance, and the nearer ladder is not used on top of it";
        }
        else if (overlapRank > 0)
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
        // THE CORNER RULE ALWAYS REPORTS, WHETHER OR NOT IT ANSWERED. A rule that only logs on
        // success is one nobody can tell from a rule that never ran — this repo has shipped that
        // twice, and ModBuild 241's window-grouping rule sat in the code for six builds firing zero
        // times because its own line only printed when it fired.
        why += ". " + cornerNote
               + (haveCorners
                   ? source == ArcSeatSource.TableCorner
                       ? $" — TAKEN: this window's drawn centre sits on the "
                         + $"{(Mathf.Abs(seatOffset - cornerLeftDeg) < Mathf.Abs(seatOffset - cornerRightDeg) ? "left" : "right")} "
                         + $"one, interval [{seatOffset - halfAngle:F0}°,{seatOffset + halfAngle:F0}°]"
                       : $" — NOT TAKEN this time: neither corner was both free of every standing "
                         + $"window and inside ±{arcHalf:F1}° for a window of this width "
                         + $"({halfAngle * 2f:F0}°). A corner is a demand, never a reservation"
                   : "");
        why += ". " + channelNote
               + (haveChannel
                   ? clearsChannel
                       ? $" — HONOURED: this window's drawn interval is "
                         + $"[{seatOffset - halfAngle:F0}°,{seatOffset + halfAngle:F0}°] and lies "
                         + "wholly beside it"
                       : source == ArcSeatSource.TableCorner
                           ? " — SUPERSEDED BY THE CORNER: the corner rule is the user's own "
                             + "correction of the reading this channel came from, so where the two "
                             + "disagree the corner wins and the channel is reported, not enforced"
                           : " — NOT honoured this time (see above)"
                   : "");
        why += ". GEOMETRY: " + geo.Note;
        // A CORNER WINDOW that reached the ordinary search always says why its corner was refused —
        // a corner window seated elsewhere with no reason on its line would be a rule nobody can
        // tell from a rule that never ran.
        if (cornerRefusal.Length > 0)
            why += ". " + cornerRefusal;

        float overlapDeg = ArcSeatWorstOverlapDeg(worldYaw, halfAngle, out string overlapWith);
        // The BESIDE clause, measured on the final seat and distance (the outward ladder stores its
        // push as a negative pull, so this one expression is the delivered distance either way).
        BesideClause(worldYaw, halfAngle, -1, nominalDist - foregroundPullWorld, scale);
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
            SlotReserved = geo.SlotReserved,
            ExtraGapDeg = ChooserGapFor(geo),
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
        // claim books the FRAME, because at that instant nothing is measurable yet), so HIS TABLE
        // CORNERS have to be honoured HERE above all — this is the call that reproduces his
        // photograph, and it is why the corner rule is a parameter of the shared search rather than
        // a special case in the spawn path.
        bool cornersMeasured = TryTableFarCornersDeg(gazeYawDeg, out float cornerLeftDeg,
            out float cornerRightDeg, out string cornerNote);
        // RESERVED BY IDENTITY (2026-09-03) — the same rule as the spawn path, for the same reason:
        // a corner is the character screen's or the quest log's, never this window's.
        bool haveCorners = false;
        if (cornersMeasured)
            cornerNote += " — RESERVED BY IDENTITY (character screen LEFT, quest log RIGHT, user "
                          + "ruling 2026-09-03): neither corner is offered to this window";
        bool haveChannel = TryMapChannelDeg(gazeYawDeg, out float channelLo, out float channelHi,
            out string channelNote);
        bool haveFree = ArcSeatFreeInterval(gazeYawDeg, centreLimit, halfAngle, haveCorners,
            cornerLeftDeg, cornerRightDeg, haveChannel, channelLo, channelHi, out float bestFree,
            out ArcSeatSource source);

        // The outward ladder, on the same terms as the spawn path: further away is angularly
        // narrower, and a window that has to move anyway is exactly the one that may as well move
        // out rather than collide. Its own slot is already released above, so it cannot see itself.
        int outwardSteps = 0;
        float seatDistWorld = nominalDist;
        string outwardNote = "";
        if (!haveFree
            && TryArcSeatFurtherOut(geo, nominalDist, scale, gazeYawDeg, arcHalf, slot, haveCorners,
                cornerLeftDeg, cornerRightDeg, haveChannel, channelLo, channelHi, out outwardSteps,
                out seatDistWorld, out bestFree, out source, out float reseatWouldFit))
        {
            _ = reseatWouldFit; // only the spawn line reports it; a re-place would print it twice
            float wasHalf = halfAngle;
            geo.ReDeriveAt(seatDistWorld);
            halfAngle = geo.DrawnHalfDeg;
            centreLimit = Mathf.Max(0f, arcHalf - halfAngle);
            haveFree = true;
            outwardNote = $". OUTWARD LADDER: {outwardSteps} step(s) of {ArcOutwardStepMeters:F2} m "
                          + $"took it from {WindowDistanceMeters:F2} m to "
                          + $"{seatDistWorld / Mathf.Max(scale, 1e-4f):F2} m, narrowing it from "
                          + $"{wasHalf * 2f:F0}° to {halfAngle * 2f:F0}° — which is what opened a "
                          + "clean seat. His own remedy: 'ein klein bisschen weiter weg'";
        }

        float seatOffset;
        string how;
        bool clearsChannel = source == ArcSeatSource.BesideTheMap;
        if (haveFree)
        {
            seatOffset = bestFree;
            how = source == ArcSeatSource.TableCorner
                ? $"took ONE OF HIS TABLE CORNERS — the "
                  + $"{(Mathf.Abs(seatOffset - cornerLeftDeg) < Mathf.Abs(seatOffset - cornerRightDeg) ? "LEFT" : "RIGHT")} "
                  + $"far corner at {seatOffset:F0}°±{halfAngle:F0}°, inside the measured "
                  + $"±{arcHalf:F1}° field of view (standing set [{standing}]) and clear of "
                  + "everything. This is the placement that reproduces 'ideale_position.jpg'"
                : $"took the FREE INTERVAL NEAREST THE GAZE at {seatOffset:F0}°±{halfAngle:F0}° "
                  + $"inside the measured ±{arcHalf:F1}° field of view (standing set [{standing}]) "
                  + "and collides with nothing"
                  + (clearsChannel
                      ? ", BESIDE THE MAP rather than over it — the fallback rule, not his corner one"
                      : "");
            how += outwardNote;
            if (_lastSeatWaivedChooserGap)
                how += $". CHOOSER GAP WAIVED: no seat inside ±{arcHalf:F1}° kept the "
                       + $"{NeighbourGapDegrees + ChooserSlotExtraGapDegrees:F0}° clearance off the "
                       + "standing chooser's reserved sub-view slot, so the search ran once more at "
                       + $"the plain {NeighbourGapDegrees:F0}° neighbour gap and this is that answer";
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
        if (outwardSteps > 0)
        {
            // The outward step already owns this window's distance and it was accepted only because
            // its FOOTPRINT is clean, so the nearer ladder must not run on top of it. Same rule and
            // same falsifier as the spawn path.
            depthNote = overlapRank > 0
                ? $". FALSIFIER TRIPPED: the outward step was accepted as footprint-clean and the "
                  + $"depth ladder then reported level {overlapRank} against {blockers}. The outward "
                  + "distance is kept and no nearer step is taken, but the two tests must agree"
                : $". DEPTH: level 0 at "
                  + $"{seatDistWorld / Mathf.Max(scale, 1e-4f):F2} m — the outward ladder set this "
                  + "distance and the nearer ladder is not used on top of it";
            overlapRank = 0;
            foregroundPullWorld = -(seatDistWorld - nominalDist);
        }
        else if (overlapRank > 0)
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
        BesideClause(worldYaw, halfAngle, slot, nominalDist - foregroundPullWorld, scale);
        _arcSeatWorldYaw[slot] = worldYaw;
        held.CentreDeg = seatOffset;
        held.SpawnGazeWorldYaw = gazeYawDeg;
        held.HalfWidthDeg = halfAngle;
        held.DrawnOffsetDeg = geo.OffsetDeg;
        held.FrameHalfWidthDeg = geo.FrameHalfDeg;
        held.SlotReserved = geo.SlotReserved;
        held.ExtraGapDeg = ChooserGapFor(geo);
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
               + ". " + cornerNote
               + (haveCorners && source != ArcSeatSource.TableCorner
                   ? " — NOT TAKEN this time: neither corner was both free and inside the field of "
                     + "view for a window of this width"
                   : "")
               + ". " + channelNote
               + (haveChannel && clearsChannel
                   ? $" — HONOURED: this window's drawn interval is "
                     + $"[{seatOffset - halfAngle:F0}°,{seatOffset + halfAngle:F0}°] and lies "
                     + "wholly beside it"
                   : "")
               + ". No other window was read for anything but collision and none was moved";
        return true;
    }

    /// <summary>A delivered distance within this fraction of the booked one is the same distance —
    /// float noise plus the sub-degree residue of the yaw restore. 1 % of 1.40 m is 14 mm, and the
    /// widest window in this room changes by 0.4° over that, well under the half-degree the audit
    /// itself calls an overlap.</summary>
    private const float ArcDeliveredDistanceTolerance = 0.01f;

    /// <summary>
    /// THE BOOKED DISTANCE AND THE DELIVERED ONE, RECONCILED — and the registry corrected to the
    /// one that is true. Called once per arc-governed placement, AFTER every spawn clamp has run,
    /// from <c>ComputeHmdPose</c>. It writes NO pose and moves NOTHING.
    ///
    /// <para>THE DEFECT IT ANSWERS, carried forward from ModBuild 241 and 242 and recorded in
    /// <c>Net/NetProtocol.cs</c>. <see cref="TryClaimArcSeat"/> computes every angle at
    /// <c>WindowDistanceMeters × scale</c>; <c>ClampSpawnPose</c> then runs, and it can move the
    /// pose in ways that change the head→window distance — the steep-gaze clamp multiplies it by
    /// <c>SteepGazePullFactor</c> outright, and the board-top floor raises y, which lengthens or
    /// shortens the hypotenuse. A window that hangs nearer than the packer believes is angularly
    /// WIDER than the interval it booked, and the next window is packed against a lie.</para>
    ///
    /// <para>WHAT THE ModBuild 242 LOG ACTUALLY SHOWS, because the carried-forward note overstates
    /// it and a wrong premise is worth correcting explicitly. The steep-gaze pull fires on ONE
    /// window in that whole 15 MB session (the quest log at 19° below eye level, 1.40 → 1.17 m,
    /// −17 %), not on every map-room window: raising the reading distance from 1.20 m to 1.40 m in
    /// ModBuild 241 flattened almost every placement below the 15° limit, so the 18 clamp lines of
    /// the 241 log became 2. The board-top floor fires on one more, and there it costs 1.2 %
    /// (1.40 → 1.38 m). So the coupling is REAL, LARGE WHEN IT FIRES, and RARE — which is exactly
    /// the shape of defect that survives a session of eyeballing.</para>
    ///
    /// <para>WHY THE REGISTRY IS CORRECTED AND THE WINDOW IS NOT MOVED. Moving it would fight the
    /// clamp that just ran (the board-top floor exists so a window's bottom clears the table, and
    /// the steep-gaze pull is a readability rule for a player looking down), and a second corrective
    /// write on a spawn path is how ModBuild 183's visible jump came back. The booking is the thing
    /// that was wrong, the booking is what other windows read, and the booking is what is fixed.</para>
    ///
    /// <para>IT IS ALSO THE FALSIFIER FOR THE WHOLE PLACEMENT. It prints the booked interval, the
    /// delivered interval, and the live overlap in degrees against every other standing seat — so
    /// "no overlap" is a measurement on this line and not the absence of a complaint. A line with a
    /// non-zero OVERLAPS clause and a depth level of 0 is a failure of this build's stated rule.</para>
    /// </summary>
    /// <param name="slot">The registry index the placement claimed, or −1 when it holds none.</param>
    /// <param name="headPos">The head the placement measured from (already height-corrected).</param>
    /// <param name="finalPos">The window's pose AFTER every clamp.</param>
    /// <param name="nominalMeters">The reading distance the packer assumed, real metres.</param>
    private static string ArcSeatDeliveredNote(int slot, Vector3 headPos, Vector3 finalPos,
        float scale, float nominalMeters)
    {
        if (slot < 0 || slot >= _arcClaims.Length || _arcClaims[slot].Panel == null
            || scale <= 1e-4f)
            return "";
        float booked = _arcClaims[slot].DistanceWorld;
        float delivered = (finalPos - headPos).magnitude;
        if (booked <= 1e-4f || delivered <= 1e-4f)
            return "";

        float bookedHalf = _arcClaims[slot].HalfWidthDeg;
        float bookedFrame = _arcClaims[slot].FrameHalfWidthDeg;
        float bookedOffset = _arcClaims[slot].DrawnOffsetDeg;
        float ratio = delivered / booked;
        // A CORNER claim books the distance to its DRAWN centre (the corner) and the pose delivered
        // is the HOST centre, offset sideways by the content's own offset inside its frame — the
        // two are different points by design, so the correction below must not run on it.
        bool corrected = !_arcClaims[slot].Corner
                         && Mathf.Abs(ratio - 1f) > ArcDeliveredDistanceTolerance;
        string fix = _arcClaims[slot].Corner
            ? $" A CORNER SEAT: the booked figure is the distance to its DRAWN centre (the "
              + $"{_arcClaims[slot].CornerWhich} far corner) and the delivered figure is the HOST "
              + $"centre's, which stands {_arcClaims[slot].DrawnOffsetDeg:F1}° to the side of it "
              + "(the content's offset inside its frame); the registry is kept by the corner path "
              + "and is not corrected here."
            : "";
        if (corrected)
        {
            // Recover the world extents the booked angles were derived from — pure arithmetic, no
            // second subtree walk — and re-derive every one of them at the distance that was
            // actually delivered. The SEAT (the world yaw) does not move: the hard cone clamp in
            // ComputeHmdPose has already restored the claimed azimuth, so only the WIDTH and the
            // content OFFSET can have changed, and those are exactly what a neighbour reads.
            // Clamped short of the asymptote: every angle here came out of an atan2 of two positive
            // world lengths and so is already below 90°, but tan() at 90° is an infinity that would
            // be written straight into the registry, and a guard costs nothing on a path that runs
            // once per spawn.
            float Extent(float deg) =>
                Mathf.Tan(Mathf.Clamp(deg, -89f, 89f) * Mathf.Deg2Rad) * booked;
            float drawnHalfWorld = Extent(bookedHalf);
            float frameHalfWorld = Extent(bookedFrame);
            float offsetWorld = Extent(bookedOffset);
            float newHalf = HalfAngleDeg(drawnHalfWorld, delivered);
            float newFrame = HalfAngleDeg(frameHalfWorld, delivered);
            float newOffset = Mathf.Atan2(offsetWorld, delivered) * Mathf.Rad2Deg;
            // The registry seats the DRAWN centre, so a changed content offset moves that centre
            // even though the HOST rect has not turned at all.
            _arcSeatWorldYaw[slot] += newOffset - bookedOffset;
            _arcClaims[slot].HalfWidthDeg = newHalf;
            _arcClaims[slot].FrameHalfWidthDeg = newFrame;
            _arcClaims[slot].DrawnOffsetDeg = newOffset;
            _arcClaims[slot].DistanceWorld = delivered;
            fix = $" REGISTRY CORRECTED: it had booked {bookedHalf * 2f:F1}° and really occupies "
                  + $"{newHalf * 2f:F1}° ({(newHalf - bookedHalf) * 2f:F1}° more), frame "
                  + $"{bookedFrame * 2f:F1}° → {newFrame * 2f:F1}°, content offset "
                  + $"{bookedOffset:F1}° → {newOffset:F1}°. NOTHING MOVED — the window keeps the "
                  + "pose the clamps gave it; what is fixed is what the NEXT window reads when it "
                  + "looks for a seat, which is where the error was actually spent.";
        }

        // THE OVERLAP CENSUS — the part of this line that can FAIL.
        var sb = new System.Text.StringBuilder();
        int overlapping = 0;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (i == slot || _arcClaims[i].Panel == null)
                continue;
            float ov = _arcClaims[i].HalfWidthDeg + _arcClaims[slot].HalfWidthDeg
                       - Mathf.Abs(Mathf.DeltaAngle(_arcSeatWorldYaw[slot], _arcSeatWorldYaw[i]));
            if (ov <= ArcAuditOverlapToleranceDeg)
                continue;
            overlapping++;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('\'').Append(_arcClaims[i].Name ?? "?").Append("' by ")
              .Append(ov.ToString("F1")).Append("° (that one is at depth level ")
              .Append(_arcClaims[i].OverlapRank).Append(", this one at ")
              .Append(_arcClaims[slot].OverlapRank).Append(')');
        }

        return "MAP ROOM SEAT DELIVERED '" + (_arcClaims[slot].Name ?? "?") + "': booked "
               + $"{booked / scale:F2} m, DELIVERED {delivered / scale:F2} m "
               + $"({(ratio - 1f) * 100f:+0.0;-0.0;0.0}% — the spawn clamps run AFTER the angles are "
               + $"computed and can move the window along the gaze; the nominal is "
               + $"{nominalMeters:F2} m)."
               + fix
               + $" INTERVAL NOW [{_arcSeatWorldYaw[slot] - _arcClaims[slot].HalfWidthDeg:F1}°,"
               + $"{_arcSeatWorldYaw[slot] + _arcClaims[slot].HalfWidthDeg:F1}°] world, at depth "
               + $"level {_arcClaims[slot].OverlapRank} and "
               + (_arcClaims[slot].Corner
                   ? $"{Mathf.Abs(_arcClaims[slot].DepthPullMeters):F2} m "
                     + $"{(_arcClaims[slot].DepthPullMeters > 0f ? "NEARER" : "FURTHER")} than "
                     + $"nominal — a CORNER SEAT over the {_arcClaims[slot].CornerWhich} far corner: "
                     + "the distance is the corner's own, not a ladder step"
                   : _arcClaims[slot].DepthPullMeters > 0.001f
                   ? $"{_arcClaims[slot].DepthPullMeters:F2} m NEARER than nominal (the inward "
                     + "ladder)"
                   : _arcClaims[slot].DepthPullMeters < -0.001f
                       ? $"{-_arcClaims[slot].DepthPullMeters:F2} m FURTHER than nominal (the "
                         + "OUTWARD ladder — his own remedy for collisions)"
                       : "no ladder step at all")
               + ". OVERLAPS: "
               + (overlapping == 0
                   ? "NONE — measured against all " + CountStandingArcClaims()
                     + " standing seat(s), not merely unreported. THIS CLAUSE IS THE FALSIFIER: a "
                     + "run where it names a window while the depth level is 0 is a run where this "
                     + "build's rule did not hold."
                   : sb + " — " + overlapping + " seat(s). This is only acceptable with a depth "
                     + "level above zero on THIS window; at level 0 it is the reported defect.");
    }

    /// <summary>How many registry seats are held right now — printed by the delivered line so
    /// "OVERLAPS: NONE" states what it was measured against rather than asserting a negative.</summary>
    private static int CountStandingArcClaims()
    {
        int n = 0;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (_arcClaims[i].Panel != null)
                n++;
        }
        return n;
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

    // ---- THE HANDOVER: ONE WINDOW TAKES ANOTHER'S PLACE (ModBuild 242) --------------------------
    //
    // USER REPORT (2026-08-24, testing ModBuild 241), verbatim:
    //
    //   "Nachdem die Story vorbei ist verschwindet das Fenster und stattdessen kommt die Character-UI
    //    wieder (was gewollt ist) — ich hätte gerne dass sie sich an exakt der selben Stelle
    //    auswechseln. Aktuell spawnt die Character-UI noch im Halbkreis daneben — obwohl das Fenster
    //    ja bereits verschwunden ist."
    //
    // THE MECHANISM, READ OFF HIS OWN ModBuild 241 LOG (.planning/debug/LogOutput.log). The two
    // events happen in the wrong order and neither one knows about the other:
    //   :9036-9042  the Character-UI converts and floats. The arc registry it asks for a seat still
    //               holds the story window at `-4°±24° (world 76°)`, so the ONLY thing the packer
    //               can do is put it somewhere else: `'New Party display' claimed reservation 0 at
    //               60° from the spawn gaze, world yaw 140°, frame 72°, drawn 14° at offset -32°`,
    //               i.e. its DRAWN column lands at world yaw 108° — 32° beside the story window.
    //   :9050       LOADOUT BACKDROP CLAIM RAISED — the story window is withdrawn.
    //   :9101       the one pre-reveal re-place runs at the final fitted geometry and confirms the
    //               same seat: world yaw 140°, and the audit stamps it
    //               `*** OUTSIDE THE FIELD OF VIEW by 16° ***`.
    // So "im Halbkreis daneben" is not a bug in the packer. The packer did the only correct thing
    // available to it; NOBODY EVER TOLD IT THE OTHER WINDOW WAS ABOUT TO LEAVE. There is no
    // seat-inheritance line anywhere in that 13,366-line log: all twelve MAP ROOM WINDOW SLOT
    // RELEASED lines are pure give-ups and no window ever claims a released interval.
    //
    // WHICH POSE IS CAPTURED, AND AT WHICH EDGE. The LIVE pose of the story window at the instant
    // StoryComposite raises the backdrop claim (StoryComposite.TickLoadoutBackdrop, the
    // `LOADOUT BACKDROP CLAIM RAISED` branch) — the last tick on which that window is still floated
    // and its transform is still real. NOT its spawn pose and NOT its registry entry: it is a SHARED
    // (blue) window whose pose is synced by wire record 21 (Net.RemoteMapStory), a PEER may have
    // dragged it, the local player may have dragged it, and a shared window deliberately never
    // re-faces. The registry would answer where the packer PUT it; the transform answers where it
    // IS. [[measure-the-picture-not-the-state]].
    //
    // WHAT "EXACTLY THE SAME PLACE" RESOLVES TO, AND WHY IT IS NOT THE FRAME. The two windows are
    // not the same size and never will be — the story window fills its frame, the Character-UI draws
    // a narrow character column inside a 1988 px sheet. His own log measures the consequence:
    // `FIXED FIT 'GloomhavenVR.Panel_Modal_New Party display' APPLIED: host pinned at 1988x1080 px =
    // ... the CHARACTER COLUMN renders 328x1080 px from (-984,-540)`, and the arc line puts that
    // column at `offset -22°`, which at the 1.40 m reading distance is 1.40·tan(22°) = 566 mm from
    // the point the window is positioned by. So matching FRAME ORIGINS would leave the thing he
    // actually looks at more than half a metre off, and it would look exactly like the fix not
    // working. The invariant is therefore THE DRAWN CENTRE — the same choice, for the same reason
    // and through the same instrument, that ModBuild 241 made when it moved the release re-face
    // pivot to `HostRect.TransformPoint(ink.center)` (NetProtocol's build-241 note 4a: 859 mm of
    // frame-origin error swung the picture 639 mm sideways on a 43.7° turn).
    //
    // TWO INSTRUMENTS, TWO QUESTIONS, ON PURPOSE. The POSITION is matched on PanelInkBounds' ink
    // union, because that class's whole job is "what does the player actually see"; the
    // RESERVATION is re-derived through MeasureArcDrawnGeometry, because a seat has to be
    // comparable with the other seven seats in the registry and those are all measured that way.
    // Mixing them would put a number from one instrument into a comparison built for the other
    // ([[instrument-measures-one-term]]). Nothing here reads a NUMBER out of PanelInkBounds' docs —
    // only the property — because that file is being changed this same round.
    //
    // THE SEAT IS TRANSFERRED, NOT RE-CLAIMED, AND THAT IS THE WHOLE POINT. A pose without a
    // reservation is worse than the bug: the next window to open would be seated straight through
    // the Character-UI, because ArcSeatIsFree/ArcSeatFreeInterval only ever consult _arcClaims. A
    // FRESH claim is what ModBuild 241 already does and it is precisely "im Halbkreis daneben". So
    // the leaving window's slot is released FIRST — so the depth ladder cannot see a window that is
    // going away — and the arriving window's OWN slot is then rewritten in place with the inherited
    // direction, distance, depth pull and promise-frame, keeping its own angular widths (they are a
    // property of what IT draws, not of the seat). The registry needed no new field and no new
    // array: a transfer is one release plus one in-place rewrite of an entry that already exists.
    //
    // WHY IT IS APPLIED TWICE, AND WHY THAT IS NOT A WRITE WAR. The arriving window is usually still
    // behind the reveal gate at the withdrawal edge, and ModalFallback's ONE pre-reveal re-place
    // (TickPoseRePlaceOne) is what runs the placement again at the final fitted geometry — in his
    // log that is :9100/:9101, fifty lines AFTER the edge, and it would put the Character-UI straight
    // back on its arc seat. So the handover is applied immediately at the edge (correct even if no
    // re-place ever comes, e.g. a Character-UI that was already revealed) and the anchor is left
    // standing so that the ONE re-place CONSUMES it instead of replaying the arc. The re-place is
    // not a second, competing writer — it is the same writer, once, with better inputs: the ink is
    // re-measured at the final rect, so the drawn centre lands on the captured point at the geometry
    // the player will actually see. After that the anchor is cleared and nothing re-places again.
    //
    // NOTHING HERE FOLLOWS THE HEAD, AND NOTHING HERE BLOCKS TURNING. Both writes are ONE-SHOT at a
    // named edge; there is no per-frame term anywhere in this block, no head sampling outside the
    // single seat re-derivation, and the arriving window is left standing in the room exactly as
    // every other floated window is. The standing project rule is untouched.
    //
    // A WINDOW THE PLAYER HAS PLACED IS HIS FOREVER. If the Character-UI was already open and the
    // player had moved it himself, the swap DOES NOT HAPPEN and says so: GrabbableModal.UserMoved is
    // the same latch the presence-regain refloat honours, and the standing ruling is that a window
    // he touched stays where he put it. A window the MOD placed is fair game — that is the case he
    // is reporting. A window he is holding RIGHT NOW is refused for the same reason.
    //
    // MULTIPLAYER: NOTHING GOES ON THE WIRE, AND BOTH CLIENTS LAND IN THE SAME PLACE. The captured
    // pose is the SHARED story window's, which record 21 already keeps 1:1 across clients; the
    // Character-UI is a private, per-client window that is never synced. Each client therefore
    // computes the same target from the same shared input and moves its own local window onto it —
    // consistency without a single new byte, and without this class ever asking who the host is. The
    // one legitimate divergence is a client whose player has moved HIS Character-UI: that client
    // keeps his pose, which is the correct answer for a private window and not a desync.
    //
    // ALTERNATIVES REJECTED.
    //   (a) Seat the Character-UI on top of the story window when it FLOATS (:9036) instead of at
    //       the withdrawal edge. It floats BEFORE the withdrawal by construction — the backdrop
    //       claim's deadlock clause requires the continue control to already be parked inside it —
    //       so this would put two windows in one spot for as long as the player takes to click
    //       through the last story page, which is worse than the bug.
    //   (b) Copy the story window's SCALE too. Its scale is a legibility contract derived per window
    //       from its own fitted width (DeriveWindowScale / the 5b re-derivation); copying it would
    //       resize his character sheet, which he did not ask for. "Dieselbe Stelle" is a place, not
    //       a size.
    //   (c) Match host rects instead of drawn centres — the 566 mm error measured above.
    //   (d) Add a Transfer() to the registry as a new operation. There is nothing to add: the two
    //       primitives already exist and a third one would be a second way to write _arcClaims,
    //       i.e. a second writer of one truth ([[a-remedy-knows-one-writer]]).
    //   (e) Let ReleaseFinishedArcSlots free the leaving seat on its own a tick or two later. Then
    //       the depth ladder would rank the arriving window against a window that is on its way out
    //       and push it one rung nearer for a collision that does not exist.

    /// <summary>The panel a handover is waiting to be re-applied to at its final fitted geometry, or
    /// null. Reference-compared, never name-compared: a panel is destroyed and rebuilt on every
    /// convert, so a stale anchor can never match a later open of the same window.</summary>
    private static ConvertedPanel? _handoverPanel;

    /// <summary>WORLD point the arriving window's DRAWN CENTRE must land on — the leaving window's
    /// drawn centre, sampled live at the withdrawal edge.</summary>
    private static Vector3 _handoverCentreWorld;

    /// <summary>WORLD rotation the arriving window inherits — the leaving window's live rotation.</summary>
    private static Quaternion _handoverRot = Quaternion.identity;

    /// <summary>The leaving window's seat, captured before its slot was released: world yaw of its
    /// DRAWN centre, its reading distance in WORLD units, the gaze its placement promise was made
    /// in, and its depth term in real metres. −1 in <see cref="_handoverSeatSlot"/> means the
    /// leaving window held no reservation at all (the registry was full when it opened).</summary>
    private static int _handoverSeatSlot = -1;
    private static float _handoverSeatWorldYaw;
    private static float _handoverSeatDistWorld;
    private static float _handoverSeatGazeYaw;
    private static float _handoverSeatDepthPull;

    /// <summary>Log name of the window that left, kept because the re-place happens long after its
    /// host has been destroyed.</summary>
    private static string _handoverFrom = "?";

    /// <summary>What raised the handover, verbatim on both log lines.</summary>
    private static string _handoverTrigger = "?";

    /// <summary>Frame after which a stored anchor is dropped unconsumed.</summary>
    private static int _handoverExpiresFrame = -1;

    /// <summary>
    /// How long a stored handover may wait for the ONE pre-reveal re-place, in frames. It is a
    /// CEILING and not a schedule: the re-place is itself bounded by the reveal deadline (0.6 s),
    /// so ten seconds at 60 Hz is two orders of magnitude of headroom, and past it the anchor is
    /// dropped rather than applied to a window the player has been looking at for ten seconds.
    /// </summary>
    private const int HandoverGraceFrames = 600;

    /// <summary>
    /// THE DRAWN CENTRE of <paramref name="panel"/> in its host rect's OWN local space, so the same
    /// point can be re-projected through the transform after it has been moved — which is what makes
    /// the read-back on the log line a falsifier of the arithmetic rather than a second measurement
    /// of the ink.
    ///
    /// <para>Returns false (and <c>local</c> = the frame origin) when nothing is measurable. That is
    /// never silent: the caller prints the note.</para>
    /// </summary>
    private static bool TryDrawnCentreLocal(ConvertedPanel panel, out Vector3 local, out string note)
    {
        local = Vector3.zero;
        if (panel.HostRect == null)
        {
            note = "THE FRAME ORIGIN (this window has no host RectTransform to map anything through)";
            return false;
        }
        if (PanelInkBounds.TryMeasure(panel, out PanelInkBounds.Ink ink) && ink.Valid
            && ink.Rect.width > 0.5f && ink.Rect.height > 0.5f)
        {
            local = new Vector3(ink.Rect.center.x, ink.Rect.center.y, 0f);
            // ONLY Valid / Rect / Graphics ARE READ, and that is deliberate: PanelInkBounds' census
            // fields are being reworked in a parallel lane, so this codes against the three
            // properties that ARE its contract and against no number at all.
            note = $"THE DRAWN CENTRE ({local.x:F0},{local.y:F0} px in this window's own authored "
                   + $"pixels; ink union {ink.Rect.width:F0}x{ink.Rect.height:F0} px over "
                   + $"{ink.Graphics} visible graphic(s))";
            return true;
        }
        // TIER 2 — THE ARC'S OWN MEASURE OF DRAWN CONTENT, and it exists because of a real gap
        // rather than as belt and braces: PanelInkBounds counts a graphic that fills the frame as a
        // PLATE and leaves it out of the union, so a window whose only remaining content IS a
        // full-frame picture — which is precisely what the story window is at the withdrawal edge,
        // "nichts als das Hintergrundbild" — can answer "no ink at all". CanvasConversion's fit
        // verdict has no plate rule and answers for that window. The offset is taken as
        // content.center − host.center, i.e. as a DIFFERENCE, so it does not assume where either
        // rect's origin sits.
        if (CanvasConversion.TryMeasureDrawnContent(panel, out Rect content, out Rect hostRect2,
                out int contributors)
            && content.width > 0.5f && content.height > 0.5f)
        {
            local = new Vector3(content.center.x - hostRect2.center.x,
                content.center.y - hostRect2.center.y, 0f);
            note = $"THE DRAWN CENTRE ({local.x:F0},{local.y:F0} px), measured through "
                   + "CanvasConversion's fit verdict because PanelInkBounds had no union for this "
                   + $"window — {content.width:F0}x{content.height:F0} px of content from "
                   + $"{contributors} graphic(s) inside a {hostRect2.width:F0}x{hostRect2.height:F0} "
                   + "px frame. THE USUAL CAUSE IS THE PLATE RULE: a graphic that fills the frame is "
                   + "not ink, and a window showing nothing but a full-frame picture is all plate";
            return true;
        }
        note = "THE FRAME ORIGIN (neither PanelInkBounds nor CanvasConversion's fit verdict could "
               + "measure anything this window draws, so what it DRAWS is unknown and only the frame "
               + "origins can be matched — if the window's content is not centred in its frame this "
               + "WILL be visibly off, which is the ModBuild 241 defect and is said out loud here "
               + "rather than discovered later)";
        return false;
    }

    /// <summary>
    /// HAND ONE FLOATED WINDOW'S PLACE TO ANOTHER. See the block comment above for the whole design.
    /// Called once per withdrawal edge from <c>StoryComposite.TickLoadoutBackdrop</c>.
    /// </summary>
    /// <param name="leaving">The window whose float is being withdrawn THIS tick — still floated,
    /// still transformed, which is the entire reason the edge is the right moment.</param>
    /// <param name="arriving">The window that must take its place.</param>
    /// <param name="trigger">Why, verbatim, for the log line.</param>
    /// <returns>True when the arriving window was actually moved.</returns>
    internal static bool BeginWindowHandover(UIWindow? leaving, UIWindow? arriving, string trigger)
    {
        string refusal;
        try
        {
            if (leaving == null || arriving == null || ReferenceEquals(leaving, arriving))
            {
                refusal = "one of the two windows is not there, or they are the same window";
            }
            else
            {
                WindowPanel? wpLeave = FindPanel(leaving);
                WindowPanel? wpArrive = FindPanel(arriving);
                ConvertedPanel? pLeave = wpLeave != null && wpLeave.Panel.IsAlive ? wpLeave.Panel : null;
                ConvertedPanel? pArrive = wpArrive != null && wpArrive.Panel.IsAlive ? wpArrive.Panel : null;
                GrabbableModal? grab = wpArrive != null ? wpArrive.Grab : null;
                if (pLeave == null || pLeave.HostRect == null)
                {
                    refusal = $"'{leaving.name}' is not a live floated panel at this edge, so there "
                              + "is no live pose to hand over. THE EDGE IS THE POINT: this is asked "
                              + "on the tick the claim is RAISED, which is the last tick that window "
                              + "is still floated — if this fires, the claim is being raised later "
                              + "than the withdrawal instead of before it";
                }
                else if (pArrive == null || pArrive.HostRect == null)
                {
                    refusal = $"'{arriving.name}' is not a live floated panel, so there is nothing to "
                              + "put in the leaving window's place. The backdrop claim's own deadlock "
                              + "clause should make this unreachable — it only stands while the "
                              + "continue control is parked inside this very window";
                }
                else if (grab == null)
                {
                    refusal = $"'{arriving.name}' has no grab frame, and the frame is the ONE entry "
                              + "point every external pose writer uses (writing the host directly "
                              + "would be snapped straight back next tick)";
                }
                else if (grab.IsGrabbed)
                {
                    refusal = $"THE PLAYER IS HOLDING '{arriving.name}' RIGHT NOW. His grab outranks "
                              + "every mod placement; the swap is abandoned, not deferred";
                }
                else if (grab.UserMoved || grab.PeerPlaced)
                {
                    refusal = $"'{arriving.name}' IS A WINDOW THE PLAYER HAS PLACED HIMSELF "
                              + $"(UserMoved={grab.UserMoved}, PeerPlaced={grab.PeerPlaced}) and a "
                              + "window he has moved is his forever — the same latch the "
                              + "presence-regain refloat honours. NOT A FAILURE: he asked for the "
                              + "Character-UI to take the story window's place, not for it to be "
                              + "taken away from wherever he parked it";
                }
                else
                {
                    // CAPTURE FIRST, from the world and not from the registry — a peer or the local
                    // player may have dragged this window and the registry would not know.
                    RectTransform hostLeave = pLeave.HostRect;
                    TryDrawnCentreLocal(pLeave, out Vector3 localLeave, out string leaveNote);
                    _handoverCentreWorld = hostLeave.TransformPoint(localLeave);
                    _handoverRot = hostLeave.rotation;
                    _handoverFrom = leaving.name;
                    _handoverTrigger = trigger;

                    // THE SEAT, captured and then RELEASED — in that order, and before the arriving
                    // window's own entry is touched, so the depth ladder below cannot rank it
                    // against a window that is on its way out.
                    _handoverSeatSlot = -1;
                    _handoverSeatWorldYaw = 0f;
                    _handoverSeatDistWorld = 0f;
                    _handoverSeatGazeYaw = 0f;
                    _handoverSeatDepthPull = 0f;
                    for (int i = 0; i < _arcClaims.Length; i++)
                    {
                        if (!ReferenceEquals(_arcClaims[i].Panel, pLeave))
                            continue;
                        _handoverSeatSlot = i;
                        _handoverSeatWorldYaw = _arcSeatWorldYaw[i];
                        _handoverSeatDistWorld = _arcClaims[i].DistanceWorld;
                        _handoverSeatGazeYaw = _arcClaims[i].SpawnGazeWorldYaw;
                        _handoverSeatDepthPull = _arcClaims[i].DepthPullMeters;
                        _arcClaims[i] = default;
                        _arcSeatWorldYaw[i] = 0f;
                        break;
                    }

                    _handoverPanel = pArrive;
                    _handoverExpiresFrame = Time.frameCount + HandoverGraceFrames;
                    ApplyWindowHandover(pArrive, grab, arriving.name,
                        "AT THE WITHDRAWAL EDGE, from the pose the leaving window is standing in "
                        + "this very tick (" + leaveNote + ")");
                    return true;
                }
            }
        }
        catch (System.Exception ex)
        {
            refusal = $"the handover threw ({ex.GetType().Name}: {ex.Message}) and was abandoned; "
                      + "nothing was moved and no reservation was written";
            _handoverPanel = null;
        }
        VRLog.Warn("WorldUI", $"WINDOW HANDOVER: NOT DONE ({trigger}) — {refusal}. CONSEQUENCE: the "
                              + "arriving window keeps the seat the arc allocator gave it, which is "
                              + "the ModBuild 241 presentation the user reported as \"im Halbkreis "
                              + "daneben\". Nothing is broken by this — it is the previous "
                              + "behaviour, said out loud.");
        return false;
    }

    /// <summary>
    /// Is <paramref name="panel"/> carrying a handover the ONE pre-reveal re-place should consume
    /// instead of replaying its arc seat? Pure read; called from
    /// <c>ModalFallback.TickPoseRePlaceOne</c>.
    /// </summary>
    private static bool HandoverPending(ConvertedPanel panel) =>
        _handoverPanel != null && ReferenceEquals(_handoverPanel, panel)
        && Time.frameCount <= _handoverExpiresFrame;

    /// <summary>
    /// Consume the stored handover at the arriving window's FINAL fitted geometry — the one moment
    /// its real rect exists while it is still render-hidden, so the correction is invisible by
    /// construction. Clears the anchor either way: this runs at most once per open.
    /// </summary>
    private static bool TryConsumeHandover(ConvertedPanel panel, GrabbableModal? grab, out string note)
    {
        note = string.Empty;
        if (!HandoverPending(panel))
        {
            if (_handoverPanel != null && ReferenceEquals(_handoverPanel, panel))
            {
                _handoverPanel = null;
                note = "the stored window handover EXPIRED unconsumed — the pre-reveal re-place did "
                       + "not arrive within " + HandoverGraceFrames + " frames, so the pose written "
                       + "at the withdrawal edge stands as it is";
                return false;
            }
            return false;
        }
        _handoverPanel = null;
        if (grab == null || panel.HostRect == null)
        {
            note = "the stored window handover could not be re-applied at the final geometry: this "
                   + "window has no "
                   + (grab == null ? "grab frame" : "host RectTransform")
                   + " any more. The edge-time pose stands";
            return false;
        }
        ApplyWindowHandover(panel, grab, panel.HostGo != null ? panel.HostGo.name : "?",
            "AT THE FINAL FITTED GEOMETRY, replacing the arc re-place — the ink is re-measured "
            + "against the rect the player will actually see, and the window is still render-hidden, "
            + "so this correction is invisible by construction");
        note = "TOOK THE STORED WINDOW HANDOVER instead of replaying its arc seat — see WINDOW "
               + "HANDOVER: DONE for the millimetres";
        return true;
    }

    /// <summary>
    /// Move <paramref name="panel"/> so that its DRAWN CENTRE lands exactly on the captured point at
    /// the captured rotation, then rewrite its reservation in place, then READ THE RESULT BACK and
    /// print the difference. The read-back is the falsifier and it can fail: it re-projects the SAME
    /// host-local point through the transform after the write, so a non-zero millimetre figure means
    /// the arithmetic or the frame→host pin is wrong, not that the ink moved.
    /// </summary>
    private static void ApplyWindowHandover(ConvertedPanel panel, GrabbableModal grab, string name,
        string stage)
    {
        RectTransform hostRect = panel.HostRect;
        float worldScale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);
        bool inked = TryDrawnCentreLocal(panel, out Vector3 local, out string inkNote);

        Vector3 fromFrame = hostRect.position;
        Quaternion fromRot = hostRect.rotation;
        Vector3 fromCentre = hostRect.TransformPoint(local);

        // ROTATE THE RIGID BODY ABOUT ITS OWN DRAWN CENTRE, THEN TRANSLATE THAT CENTRE ONTO THE
        // TARGET. Written as one expression so the drawn centre is a fixed point of the rotation by
        // construction — the same shape as the release re-face's pivot arithmetic, and the reason a
        // window whose ink fills its frame sees the identity here.
        Quaternion delta = _handoverRot * Quaternion.Inverse(fromRot);
        Vector3 toFrame = _handoverCentreWorld - delta * (fromCentre - fromFrame);
        // ANNOUNCE IT TO THE POSE LOCK, as a PLACEMENT, which is what it is. Without this the lock
        // classifies the write as Unattributed and writes the locked pose straight back — and it
        // would do that only in the ALREADY-REVEALED case, i.e. the "the Character-UI was already
        // open" branch, which is exactly the one a pre-reveal test would never reach. Writer
        // .Placement is the same token the spawn, the presence-regain refloat and the pre-reveal
        // re-place carry: allowed, and NOTED once per window. The narrower ruling that a window the
        // PLAYER moved is his forever is honoured a level up, by refusing the whole handover on
        // GrabbableModal.UserMoved.
        PanelPoseWatch.Announce(panel, PanelPoseWatch.Writer.Placement,
            "ModalFallback's window handover — the arriving window is taking the place of "
            + $"'{_handoverFrom}' at the user's 2026-08-24 request");
        grab.PlaceFrameAt(toFrame, _handoverRot);

        // ---- THE FALSIFIER. Same host-local point, transform written; nothing re-measured. -------
        Vector3 landedCentre = hostRect.TransformPoint(local);
        float missMm = Vector3.Distance(landedCentre, _handoverCentreWorld) / worldScale * 1000f;
        float missYawDeg = Quaternion.Angle(hostRect.rotation, _handoverRot);
        float centreMovedMm = Vector3.Distance(fromCentre, landedCentre) / worldScale * 1000f;
        float frameMovedMm = Vector3.Distance(fromFrame, hostRect.position) / worldScale * 1000f;
        float originGapMm = Vector3.Distance(hostRect.position, _handoverCentreWorld)
                            / worldScale * 1000f;

        string seatNote = RewriteHandoverSeat(panel, hostRect, local, worldScale);

        VRLog.Info("WorldUI",
            $"WINDOW HANDOVER: DONE ({_handoverTrigger}) — '{name}' has taken the place of "
            + $"'{_handoverFrom}', {stage}. CAPTURED POSE of the leaving window: drawn centre "
            + $"({_handoverCentreWorld.x:F2},{_handoverCentreWorld.y:F2},{_handoverCentreWorld.z:F2}) "
            + $"world units, yaw {_handoverRot.eulerAngles.y:F1}°. THE ARRIVING WINDOW'S RESULT, "
            + $"READ BACK OFF ITS TRANSFORM AFTER THE WRITE (not asserted): drawn centre "
            + $"({landedCentre.x:F2},{landedCentre.y:F2},{landedCentre.z:F2}), yaw "
            + $"{hostRect.rotation.eulerAngles.y:F1}° — DIFFERENCE {missMm:F1} mm and "
            + $"{missYawDeg:F2}° of yaw. ANYTHING ABOVE ~1 mm HERE IS A BUG IN THIS METHOD: the "
            + "landing point is the SAME host-local point re-projected through the transform, so it "
            + "measures the arithmetic and the frame→host pin, never the ink. IT MOVED: the drawn "
            + $"centre travelled {centreMovedMm:F0} mm and the frame origin {frameMovedMm:F0} mm — "
            + "the two differ by exactly the offset of this window's content inside its own frame, "
            + $"which is why the frame is NOT the invariant (its origin now sits {originGapMm:F0} mm "
            + $"from the point the player is looking at). MEASURED ON: {inkNote}"
            + (inked ? string.Empty
                     : ". WARNING — THE INK FALLBACK IS IN USE and this swap is only as good as the "
                       + "assumption that this window's content is centred in its frame")
            + ". THE ARC RESERVATION: " + seatNote
            + ". USER REQUEST THIS LINE ANSWERS: \"ich hätte gerne dass sie sich an exakt der selben "
            + "Stelle auswechseln. Aktuell spawnt die Character-UI noch im Halbkreis daneben\". "
            + "NOTHING WENT ON THE WIRE: the captured pose is the SHARED window's, which record 21 "
            + "already keeps 1:1 on every client, and the arriving window is private — so every "
            + "client computes this same target from the same input and needs no new traffic.");
    }

    /// <summary>
    /// Rewrite the arriving panel's registry entry IN PLACE with the inherited direction, distance,
    /// depth term and promise-frame, keeping its own angular widths. Returns the text the handover
    /// line prints. Writes nothing when the arriving window holds no slot — a placement with no
    /// reservation is a real state (the registry can be full) and it is reported, not invented.
    /// </summary>
    private static string RewriteHandoverSeat(ConvertedPanel panel, RectTransform hostRect,
        Vector3 inkLocal, float worldScale)
    {
        int slot = -1;
        for (int i = 0; i < _arcClaims.Length; i++)
        {
            if (ReferenceEquals(_arcClaims[i].Panel, panel))
            {
                slot = i;
                break;
            }
        }
        if (slot < 0)
        {
            return "NOT TRANSFERRED — the arriving window holds no reservation of its own to rewrite "
                   + (_handoverSeatSlot >= 0
                       ? $"(the leaving window's slot {_handoverSeatSlot} was released and is now "
                         + "free). CONSEQUENCE: the next window to open can be seated through this "
                         + "one. This is reachable only with a full registry"
                       : "and the leaving window held none either, so there was nothing to transfer");
        }

        // WHERE IT NOW IS, measured from the head — not from what was asked for. A seat is a
        // direction and a distance, and the window has just been written; reading the registry back
        // would be reading our own claim [[a-claim-must-not-measure-itself]].
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null && _handoverSeatSlot < 0)
        {
            // BOTH SOURCES OF A DIRECTION ARE GONE — no head to measure the landed one from and no
            // inherited one to copy. Writing world yaw 0° here would book a seat pointing at world
            // forward, which is a lie the packer would then honour for every later window. Leaving
            // the entry exactly as it is books a seat that is merely STALE, and the arc audit's
            // live-vs-reserved drift is the instrument that already reports that.
            return "NOT REWRITTEN — there is no head camera to measure the landed direction from and "
                   + "the leaving window held no reservation to inherit one from, so the arriving "
                   + "window keeps the interval it already had. The POSE was still handed over; only "
                   + "the reservation is stale, and MAP ROOM ARC AUDIT's live-vs-reserved drift is "
                   + "where that shows up";
        }
        Vector3 centre = hostRect.TransformPoint(inkLocal);
        float seatYaw = _handoverSeatWorldYaw;
        float seatDist = _handoverSeatDistWorld;
        string measuredFrom;
        if (head != null)
        {
            Vector3 toCentre = centre - head.transform.position;
            Vector3 flat = new Vector3(toCentre.x, 0f, toCentre.z);
            if (flat.sqrMagnitude > 1e-6f)
                seatYaw = WorldYawDeg(flat);
            seatDist = Mathf.Max(toCentre.magnitude, 1e-3f);
            measuredFrom = "measured live from the head to the landed drawn centre";
        }
        else
        {
            measuredFrom = "copied from the leaving window's registry entry — no head camera was "
                           + "available to measure the landed direction, which is the degraded path";
        }
        if (seatDist <= 1e-3f)
            seatDist = Mathf.Max(_handoverSeatDistWorld, WindowDistanceMeters * worldScale);

        // The angular widths stay THIS window's own: they are a property of what it draws, measured
        // through the same instrument every other seat in the registry is measured through.
        float halfWidthWorld = Mathf.Max(hostRect.rect.width * 0.5f * Mathf.Abs(hostRect.lossyScale.x),
            1e-4f);
        ArcDrawnGeometry geo = MeasureArcDrawnGeometry(panel, halfWidthWorld, seatDist);

        ArcClaim held = _arcClaims[slot];
        float heldYaw = _arcSeatWorldYaw[slot];
        float heldHalf = held.HalfWidthDeg;
        // Release our own entry before the depth ladder and the overlap reader run, or this window
        // collides with itself — the fuse-that-counted-the-player lesson, in miniature.
        _arcClaims[slot] = default;

        float hostWorldYaw = seatYaw - geo.OffsetDeg;
        ArcSeatFootprint(hostWorldYaw, geo.FrameHalfDeg, geo.OffsetDeg, geo.DrawnHalfDeg,
            out float footYaw, out float footHalf);
        int rank = ArcSeatDepthLevel(footYaw, footHalf, slot, out string blockers);
        float overlapDeg = ArcSeatWorstOverlapDeg(seatYaw, geo.DrawnHalfDeg, out string overlapWith);

        float gazeRef = _handoverSeatSlot >= 0 ? _handoverSeatGazeYaw : held.SpawnGazeWorldYaw;
        held.CentreDeg = Mathf.DeltaAngle(gazeRef, seatYaw);
        held.SpawnGazeWorldYaw = gazeRef;
        held.HalfWidthDeg = geo.DrawnHalfDeg;
        held.DrawnOffsetDeg = geo.OffsetDeg;
        held.FrameHalfWidthDeg = geo.FrameHalfDeg;
        held.SlotReserved = geo.SlotReserved;
        held.ExtraGapDeg = ChooserGapFor(geo);
        held.DistanceWorld = seatDist;
        held.OverlapRank = rank;
        // The depth term a presence-regain refloat replays. Derived from the distance this window
        // now actually hangs at rather than inherited verbatim, so a refloat reproduces the DISTANCE
        // and not merely the leaving window's ladder rung.
        held.DepthPullMeters = (WindowDistanceMeters * worldScale - seatDist) / worldScale;
        held.OverlapDeg = overlapDeg;
        _arcClaims[slot] = held;
        _arcSeatWorldYaw[slot] = seatYaw;

        return $"TRANSFERRED, not re-claimed. Slot {_handoverSeatSlot} (the leaving window's) was "
               + $"released at the edge and slot {slot} — the arriving window's OWN entry — was "
               + $"rewritten in place: its drawn centre moves from world yaw {heldYaw:F0}°±"
               + $"{heldHalf:F0}° to {seatYaw:F0}°±{geo.DrawnHalfDeg:F0}° at {seatDist / worldScale:F2} m "
               + $"({measuredFrom}), inheriting the leaving window's promise frame (spawn gaze "
               + $"{gazeRef:F0}°) so the arc audit grades this seat in the frame it was promised in. "
               + $"ITS DEPTH TERM IS DERIVED FROM THE DISTANCE IT NOW HANGS AT, {held.DepthPullMeters:F2} m "
               + $"against the leaving window's {_handoverSeatDepthPull:F2} m — a presence-regain "
               + "refloat therefore reproduces the PLACE, not the leaving window's ladder rung. "
               + $"ITS ANGULAR WIDTHS ARE STILL ITS OWN: {geo.Note}. Depth level {rank} against "
               + $"[{blockers}]"
               + (overlapDeg > 0.5f
                   ? $", and it still overlaps '{overlapWith}' by {overlapDeg:F0}° in angle, which "
                     + "is what the depth level is for"
                   : ", overlapping nothing in angle")
               + ". NO SECOND SEAT WAS TAKEN and no other window was moved";
    }

    // ---- THE SHARED WINDOW ANCHOR (ModBuild 243) ------------------------------------------------
    //
    // USER RULING (2026-08-24, verbatim), which OVERRULES "nobody decides where a shared window
    // opens":
    //
    //   "'niemand entscheidet das' bei den blauen Fenstern ist nicht was ich will. Multiplayer
    //    Fenster, also 'blaue' Fenster, sollen komplett 1:1 synchronisiert werden von Anfang an.
    //    D.h. dass ihre Position von Anfang an auch für alle synchronisiert sein muss. Gut,
    //    verankere es am Tisch bzw. über dem Spielfeld innerhalb eines Szenarios."
    //
    // AND HIS OWN NARROWING OF IT, same day, verbatim — this is the whole scope of this block:
    //
    //   "'verankert am Tisch' - ich meine nur die initiale Spawnposition - es soll weiterhin von
    //    jedem verschiebbar sein wie du es bereits implementiert hast (voll synchronisiert)"
    //
    // SO: THE ANCHOR IS THE SPAWN POSE AND NOTHING ELSE. Record 21 / record 19 keep the drag
    // exactly as they are — parchment-local position, absolute world rotation, size, last-mover-wins
    // ranked by the local receive time of a CHANGED stamp, applied verbatim and eased, a local hand
    // always outranking the wire. Nothing in this block touches any of that, and the anchor is SPENT
    // the moment a pose exists (see SharedAnchorSpent below).
    //
    // ---------------------------------------------------------------------------------------------
    // WHY THIS NEEDS NO WIRE FIELD, WHICH IS THE WHOLE DESIGN.
    //
    // A pose that is DERIVED FROM GEOMETRY EVERY CLIENT ALREADY SHARES is 1:1 by construction and
    // has nothing to send. Record 21 already proves the frame exists: it expresses a DRAGGED pose as
    // parchment-LOCAL precisely because the parchment is the common reference
    // (Net/RemoteMapStory.ToShared, MapRoom/MapRoomDriver.TryGetParchmentFrame — "Nothing per-player
    // enters it"). This block expresses the SPAWN pose in the same frame, by the same call, so the
    // two agree by construction and the wire is untouched. NO RECORD, NO FIELD, NO NEW BYTE.
    //
    // ---------------------------------------------------------------------------------------------
    // THE TWO FRAMES, AND WHERE EACH IS READ FROM.
    //
    //  * MAP ROOM → THE MAP TABLE. MapRoomDriver.TryGetParchmentFrame gives (centre, scale) off the
    //    parchment renderer's own world bounds; the table slab around it is a fixed multiple of that
    //    map on each axis (TableToMapWidthRatio / TableToMapDepthRatio, surveyed in
    //    MapRoom/MapTableLegs.cs and already used by TryTableFarCornersDeg). Frame-local units are
    //    REAL METRES, because the frame scale IS game-units-per-real-metre. The frame carries NO
    //    rotation: the parchment bounds are an AABB and world axes are already shared, which is the
    //    same reason record 21 sends rotation as an ABSOLUTE world rotation.
    //
    //  * SCENARIO → THE BOARD. PlayTray.Current.Root is the board frame and
    //    PlayTray.MeasureBoardLocalExtents gives the VISIBLE board's top (far) edge and half-width
    //    in board-LOCAL metres — the same measurement WorldTooltips.TryGetBoardAreaPose and
    //    EnemyRevealSurface already anchor to. "Über dem Spielfeld" is read as: centred on the
    //    board's X, one clearance above its measured TOP edge, in the board's own plane.
    //
    // THE ASYMMETRY BETWEEN THE TWO IS REAL AND IS NOT A BUG. The parchment frame is a place in the
    // ROOM and every client's map room is built from the same parchment, so a map-room anchor is 1:1
    // in WORLD. The board is a piece of furniture each player has posed, tilted and resized for
    // HIMSELF (WorldGrab, the tray grab-resize, the per-board config offsets), so a scenario anchor
    // is 1:1 in BOARD-LOCAL and every player reads it square-on above his own board. Both are "the
    // same place" in the only frame in which that sentence means anything.
    //
    // ---------------------------------------------------------------------------------------------
    // THE SLOT IS A FUNCTION OF THE WINDOW'S IDENTITY, NEVER OF ARRIVAL ORDER. THIS IS THE HAZARD.
    //
    // ModBuild 243's corner rule says "left is tried first, so the FIRST CLAIMER takes the left
    // corner" — and first claimer is LOCAL claim order. Two clients can claim in different orders
    // (different frame timing, a peer who opened a local window first, a client that joined late),
    // and then client A has the story window on the left and client B has it on the right, both
    // "correct" locally. That rule is right for LOCAL windows, where there is nobody to disagree
    // with, and it is fatal for a shared one.
    //
    // So a shared window's home is SharedHomeIndex(kind) — the rank of its SharedWindowKind byte
    // among the kinds that can stand in that room, computed with no reference to what is already
    // open. TWO KINDS CAN THEREFORE NEVER WANT THE SAME HOME: the index is reserved by identity, not
    // claimed, so "what happens when the anchor is occupied" has no case to answer. The price is
    // that a home reserved for a window that is not open stays empty; that is the correct trade,
    // because the alternative is precisely the divergence above.
    //
    //   SharedWindowKind      room       home  where it lands
    //   ------------------------------------------------------------------------------------------
    //   ScenarioStory  (1)    scenario   0     centred above the board's measured TOP (far) edge
    //   MapStory       (2)    map room   0     dead ahead on the arc over the table centre
    //   QuestConfirm   (3)    map room   1     the same arc, one lateral step toward +Z
    //   Encounter      (4)    map room   2     the same arc, one lateral step toward −Z
    //
    // The two rooms never coexist (ParticipatesHere gates 2/3/4 behind MapRoomDriver.Active and
    // ScenarioStory is never in the map room), so the two tables are independent and kind 1 taking
    // index 0 costs the map kinds nothing.
    //
    // ---------------------------------------------------------------------------------------------
    // ModBuild 250 — THE THREE MAP-ROOM HOMES ARE A HALF-RING ABOUT THE TABLE CENTRE, NOT A RANK AT
    // THE FAR EDGE. User ruling, verbatim, after .planning/debug/window_spawn_problem.mp4:
    //
    //   "Es ist ok das es nicht bei jedem nah steht. Ich möchte aber, das die Fenster zentral ÜBER
    //    dem Tisch mittig spawnen dort in einem halbkreis."
    //
    // The paragraphs below still describe WHICH END is the reading end and WHY the sign is a
    // constant — that is unchanged and still the cost of 1:1. What changed is the shape:
    //
    //   * THE ARC CENTRE is the parchment frame centre (the table centre), not a far edge.
    //   * THE RADIUS is [WorldUI] SharedWindowArcRadiusMeters, default 0.80 m — the first tunable
    //     number in this whole block, so the next hardware round turns a dial instead of asking for
    //     a build. 0.80 m is the parchment's own CIRCUMRADIUS (sqrt(0.48² + 0.60²) = 0.768 m on the
    //     surveyed 0.96 x 1.20 m map) rounded up: at or above it, NO point of the ring can stand
    //     over the map, whatever the lateral step. Below it the map-occlusion floor starts pulling
    //     wide slots off the arc, and the falsifier says so in millimetres.
    //   * THE OPENING FACES THE ROOM. The depth is the POSITIVE root of the circle, so the ring
    //     wraps the FAR half of the table and no shared window is ever seated between the reader
    //     and the map. A window on the near half would hide the thing he is playing on.
    //   * THE SEPARATION RULE IS UNTOUCHED — halfA + halfB + 0.12 m, measured along the LATERAL
    //     axis, because every slot is yawed the same way and two parallel planes clear each other
    //     laterally. Arc LENGTH would have been the wrong measure: on the shipped pair it leaves
    //     0.038 m of air where the rule asks for 0.120 m.
    //   * THE ARC IS NOT ModBuild 234's REJECTED HALBKREIS ("viele Fenster außerhalb des direkten
    //     Sichtfelds"). That one was the LOCAL seat ring about the PLAYER, with a fixed sweep that
    //     put windows behind his shoulders. This one is about the TABLE, it has at most three
    //     slots, and the widest slot the shipped pair produces stands 19.4° off the reading axis as
    //     seen from the seat — inside the field of view, not beside it.
    //
    // WHY THE FAR SHORT END AND NOT ONE OF HIS TWO TABLE CORNERS. The corners are SPOKEN FOR: his
    // ModBuild 243 ruling put the Character-UI on the left corner and the Weltquests on the upper
    // right corner, and both of those are LOCAL windows (PartyPanel / Quest Log Manager — see the
    // MAP ROOM WINDOW SLOT lines of the ModBuild 242 session). Seating the shared windows there
    // would evict the two windows he photographed. The far short end is the free place that is still
    // "am Tisch", it is between the two corners rather than on either, and — measured against the
    // ModBuild 242 session's own numbers — it is CLEAR OF THE MAP in the view: with the seat 0.93 m
    // out along −X and the eye 1.12 m above the parchment, the map occupies 38°–68° below the
    // horizon and this home sits at 23° below it.
    //
    // WHICH END IS "FAR" IS A CONSTANT AND NOT A MEASUREMENT, AND THAT IS THE COST OF 1:1. The
    // table's long axis is its DEEP one (2.30 m against 1.55 m), so the two SHORT ends are the ends
    // a player stands at — on the surveyed map that is the world X axis, the map's own shorter
    // horizontal extent. The sign is fixed at +: a measurement ("the end furthest from MY head",
    // which is what TryTableFarCornersDeg does and must do) is per-client by definition and would
    // put the window at opposite ends of the table for two players standing opposite each other.
    // MapRoomSeat seats every client from the GAME's own map camera, so in practice every player in
    // a session stands at the same end and + is that end; a client seated at the other end gets the
    // window across the table facing away from him. The falsifier prints this client's seat side so
    // that case is one grep, not a mystery.
    //
    // ---------------------------------------------------------------------------------------------
    // FACING: FULLY 1:1, OPTION (a) — AND SINCE ModBuild 250 THE SAME YAW FOR EVERY SLOT, square to
    // the table's reading axis rather than along each slot's own radius. Radial facing is what a
    // circle invites and it is what 245 shipped; it faces the middle of the table, where nobody
    // stands, and on his own log it turned two windows 68.4° apart. Both variants are constants of
    // the shared frame, so this is a change of geometry and not of the 1:1 property below.
    // CONSEQUENCE, IN ONE SENTENCE HE CAN READ:
    //
    //   "Ein blaues Fenster hängt für alle an derselben Stelle am Tisch und ist auch für alle gleich
    //    gedreht — wer auf der anderen Seite des Tisches steht, sieht es dadurch schräg oder von
    //    hinten; das ist der Preis für 'komplett 1:1'."
    //
    // WHY NOT (b), a shared ANCHOR with per-client facing, which the previous lane recommended: it
    // is contradicted by the SHIPPED SYNC, not merely by a ruling. Record 21 sends rotation as an
    // ABSOLUTE WORLD ROTATION (RemoteMapStory.ToShared: "world axes are already shared … nothing
    // per-client to rotate back in"), so the FIRST drag makes rotation 1:1 for everybody anyway. A
    // per-client spawn facing would therefore be a rotation that silently snaps to the first
    // dragger's on the first drag — a jump nobody asked for, at the worst possible moment. It also
    // sits badly beside the user's own earlier ruling that a shared window must NOT re-face on
    // release ("Da es ein Fenster für alle ist, sollen diese Fenster nach dem Greifen auch nicht die
    // Orientierung nach dem Spieler ändern"): that ruling exists because a per-player rotation on a
    // window that belongs to everybody is wrong, and a per-player rotation at SPAWN is the same
    // thing one moment earlier. FUTURE ROUNDS: an edge-on view for a player on the far side is the
    // ACCEPTED consequence of this ruling. Do NOT "fix" it by re-facing a shared window.
    //
    // The scenario case is 1:1 in the board frame instead, for the reason stated above — the board
    // is not shared furniture, it is each player's own copy — so nothing there faces away from
    // anybody and no ruling is strained.
    //
    // ---------------------------------------------------------------------------------------------
    // THE ANCHOR MUST NOT RE-ASSERT, AND HOW THAT IS ENFORCED.
    //
    // Once a shared window has a dragged pose the anchor is spent for the life of that window.
    // Three independent guards, and none of them is a second writer:
    //   1. THE ANCHOR IS DERIVED, NOT REMEMBERED. It is a pure function of the shared frame and two
    //      constants, with no term that depends on the head, the window's fitted size or anything
    //      that changes between the spawn and the pre-reveal re-place. Replaying it is therefore a
    //      NO-OP by construction — the same shape as ModBuild 242's handover, which the re-place
    //      CONSUMES rather than racing.
    //   2. A GRABBED WINDOW IS NEVER RE-PLACED AT ALL. ModalFallback.TickPoseRePlaceOne returns at
    //      `grab.IsGrabbed` before it ever reaches ComputeHmdPose, and a REVEALED window is never
    //      re-placed either. So a local hand ends the anchor's life with no code here.
    //   3. A POSE THAT EXISTS SPENDS THE ANCHOR OUTRIGHT. NoteSharedAnchorSpent is called by
    //      Net/RemoteMapStory both when a peer's pose is APPLIED here and when this client PUBLISHES
    //      one, and from then on the anchor stands down for that kind and SAYS SO on every
    //      subsequent placement opportunity. A silent no-op would be indistinguishable from the
    //      anchor never having been built.
    //
    //      Kind 1 (ScenarioStory) travels on record 19 in Net/RemoteStorySync instead, and that file
    //      calls it on the same two edges: a peer pose applied, and a local hand taking the window.
    //      So all four kinds spend, on both edges — there is no path left that reaches a pose
    //      without standing the anchor down.
    //
    // GREP: `SHARED WINDOW ANCHOR` — one line per shared spawn/re-place, carrying the frame, the
    // identity-earned home, THE POSE IN THE FRAME (the number that must be identical on two
    // clients, to the millimetre) and the pose in world (which may legitimately differ). Two players
    // diff those two lines: frame-local equal ⇒ this is 1:1; frame-local different ⇒ it is not, and
    // the frame is the suspect, not this table.

    // ModBuild 251 TOMBSTONE — SharedAnchorTableClearanceMeters (0.11 m of BOTTOM-EDGE clearance
    // over the table) IS DELETED, AND THE PHOTOGRAPH IT WAS READ OFF WAS MISREAD. Keep the history,
    // because the mistake is repeatable and the correction is a measurement:
    //
    //  * WHAT IT WAS. ModBuild 243 hung a shared window's CENTRE 0.40 m over the table and refused
    //    to read the fitted half-height; the quest-confirm window is 1.105 m tall, so its bottom
    //    edge landed 153 mm INSIDE the table (multiplayer_fesnter_position.jpg, "Es soll ÜBER dem
    //    Tisch spawnen"). ModBuild 244 answered that correctly in SHAPE — a clearance can only be
    //    honoured by a term that knows how tall the window is, so it became
    //    `clearance + own half-height` — and wrongly in MAGNITUDE.
    //
    //  * WHY 0.11 m WAS WRONG. It was derived from ideale_position.jpg as "both grab bars sit ~7 %
    //    of the far edge's own 1.55 m span above the table plane", i.e. a PIXEL OFFSET converted
    //    with the scale of the table's FAR EDGE. The two windows in that photograph stand well
    //    BEHIND the far edge, so a pixel there is worth far more than a pixel on the edge; the
    //    conversion is not a height at all. A length measured in a photograph needs a DEPTH before
    //    it is a length in the room.
    //
    //  * WHAT THOSE TWO WINDOWS WERE ACTUALLY SEATED AT, from their own placement lines in the
    //    ModBuild 250 hardware log: 'New Party display' centre 219.12 wu with half-height 112.33 wu
    //    and 'Quest Log Manager' centre 229.18 wu with half-height 91.53 wu, over a table top at
    //    0.00 wu at 198.12 wu/m — frame bottoms 0.539 m and 0.695 m above the table, BARS 0.521 m
    //    and 0.677 m. The "ideal" photograph's own windows sat FIVE TIMES higher than the number
    //    read out of it, and every shared window has been seated at that number since ModBuild 244.
    //    That is the whole of zu_tief.jpg.
    //
    //  * WHAT REPLACES IT: MapRoomWindowBarHeightMeters, one bar height for every window in the
    //    room, with MapRoomWindowMinBarHeightMeters as the hard table floor. Do not reintroduce a
    //    bottom-edge constant read off a picture.

    /// <summary>The floor under the computed half-height, real metres. A panel measured before its
    /// content fit can report a degenerate rect; without this the window would sit ON the table
    /// rather than above it, which is the defect this constant exists to prevent, in miniature.</summary>
    private const float SharedAnchorMinHalfHeightMeters = 0.15f;

    // =============================================================================================
    //  ModBuild 251 — ONE SPAWN HEIGHT FOR EVERY MAP-ROOM WINDOW, DEFINED AT THE GRAB BAR.
    //
    //  USER REPORT (2026-08-24, verbatim, with zu_tief.jpg): "Die Begegnung ist zu tief gespawned
    //  siehe zu_tief.jpg - das darf nie passieren - die Höhe soll beim Spawn am Besten bei allen
    //  Fenster gleich sein gemessen am Greifbalken!" — and, in the same message, the ModBuild 250
    //  half-ring is KEPT: "Die neue Position von den remote-Fenster gefällt mir". This is a HEIGHT
    //  change and nothing else: the arc, the radius, the lateral step, the facing and the
    //  map-occlusion floor are untouched.
    //
    //  WHAT THE ModBuild 250 LOG ACTUALLY SHOWS — the map room had TWO height rules, and only one
    //  of them was a rule at all. All figures below are real metres above the map table's top
    //  surface (world y 0.00 wu, frame scale 198.12 wu/m), read off that session's own lines:
    //
    //    FAMILY                      WINDOW                BAR ABOVE THE TABLE   HOW IT WAS SEATED
    //    ---------------------------------------------------------------------------------------
    //    SHARED (SharedWindowKind)   'Map Story Window'    0.092 m               BOTTOM EDGE at a
    //                                'UI Event Window'     0.092 m               constant +0.110 m
    //                                'UI Quest Popup'      0.092 m               over the table
    //    LOCAL  (arc-seated)         'New Party display'   0.521 m               RAW GAZE HEIGHT,
    //                                'Quest Log Manager'   0.677 m               floored only
    //
    //  So the two families disagreed by 0.43–0.59 m AT THE BAR — and the LOCAL family did not even
    //  agree with ITSELF: its height is whatever the head happened to be pitched at when the window
    //  opened, so that one session seats windows anywhere between 1.42 m and 2.11 m above the
    //  tracking floor. "Bei allen Fenstern gleich" is therefore two fixes, not one.
    //
    //  AND THE PHOTOGRAPH IS THE SHARED NUMBER. 'UI Event Window' fits its frame to its ink (the
    //  250 log: "the LOWEST drawn graphic 'Image Container' ends at y=-384 px … to the ink 29 px =
    //  31 mm"), so with the frame bottom at +0.110 m its VISIBLE content bottom is 0.123 m over a
    //  table the player reads from 1.17 m above and ~2.0 m away: 28° below the horizon, its lower
    //  edge grazing the far edge of the wood. That is zu_tief.jpg exactly.
    //
    //  THE RULE. Every map-room window's GRAB BAR is seated at ONE height above the table top,
    //  [WorldUI] MapRoomWindowBarHeightMeters, and the window's body hangs from it:
    //
    //      bottom edge = bar + GrabBarDropMeters
    //      centre      = table top + bottom edge + own half-height
    //      top edge    = bottom edge + own height           (see the ceiling rule below)
    //
    //  MEASURED AT THE BAR AND NOT AT THE BOTTOM EDGE, because that is what he asked for and
    //  because the two differ by a CONSTANT: GrabbableModal hangs the bar a fixed real-metre gap
    //  below the FRAME, not below the ink, and the 250 log proves the gap is the same number for
    //  windows with wildly different authored pixel scales — 'Map Story Window' "below the window's
    //  own frame 29 px = 18 mm" at 0.625 mm per authored px, 'New Party display' and 'UI Event
    //  Window' "17 px = 18 mm" at 1.050 mm per authored px. Equal bar ⇔ equal bottom edge, so the
    //  rule is ENFORCED on the bottom edge (which the placement code owns) and STATED at the bar
    //  (which is what he sees and grabs).
    //
    //  WHY THIS IS STILL 1:1 FOR THE SHARED WINDOWS. Every term is a constant of the shared
    //  parchment frame or a cfg value; not one of them reads a head, and nothing re-orients. The
    //  ONE divergence surface is the dial itself — two clients holding different values seat a
    //  shared window at different heights until somebody drags it — which is exactly the property
    //  SharedWindowArcRadiusMeters already has, and the falsifier prints the resolved number for
    //  the same reason.
    //
    //  WHAT IS DELIBERATELY *NOT* IN THIS RULE. The map room's HOVER CARD ('UI Quest Preview
    //  Popup') is pinned to the map icon it describes and re-posed EVERY TICK by
    //  MapRoom.HoverCardPose (TickHoverCards); TryClaimArcSeat refuses it a seat outright for that
    //  reason. Forcing a spawn height on it would be overwritten the same frame —
    //  [[gated-remedy-never-ran]] with a log line that looks like it worked — so it is left alone
    //  and this comment is the record that the omission is a decision.
    // =============================================================================================

    /// <summary>How far BELOW a floated window's own FRAME the grab bar's top edge hangs, real
    /// metres. MEASURED, NOT ASSUMED: <c>GRAB BAR CLEARS THE INK</c> reports it on every window it
    /// confirms, and in the ModBuild 250 log it is 18 mm for every one of them regardless of the
    /// window's authored pixel scale ('Map Story Window' 29 px at 0.625 mm/px, 'New Party display'
    /// and 'UI Event Window' 17 px at 1.050 mm/px). It is the world-space consequence of
    /// <c>GrabbableModal</c>'s bar gap (0.030 m to the bar's CENTRE) less half the bar's own
    /// thickness.
    ///
    /// <para>THIS FILE DOES NOT OWN THAT CONSTANT and deliberately does not reach for it: widening
    /// a private const in another class to read it would make this a copy of an INPUT, where what
    /// is wanted is a measurement of the shipped RESULT. If a future build changes the bar geometry
    /// the <c>MAP ROOM WINDOW BAR HEIGHT</c> line and the <c>GRAB BAR CLEARS THE INK</c> line will
    /// disagree by exactly the drift, in millimetres, on every window — a falsifier, not a silent
    /// skew.</para></summary>
    private const float GrabBarDropMeters = 0.018f;

    /// <summary>Hard floor under the resolved bar height, real metres above the table top. With the
    /// bar here the window's BOTTOM EDGE is still <c>+ GrabBarDropMeters</c> above the table, so the
    /// ModBuild 243 defect (a window standing IN the table, <c>multiplayer_fesnter_position.jpg</c>)
    /// is impossible by construction and not merely by a range check somebody can widen in a cfg
    /// file. The cfg range starts at this same number so the two can never disagree.</summary>
    private const float MapRoomWindowMinBarHeightMeters = 0.05f;

    /// <summary>Ceiling on a map-room window's TOP EDGE, real metres above the table top. A common
    /// bar height means a TALL window's top rides higher than a short one's, and past some height
    /// that stops being "one height for every window" and becomes a window nobody can read without
    /// tipping their head back.
    ///
    /// <para>IT IS A CONSTANT OF THE TABLE AND NOT OF THE HEAD, and that is not a detail: a ceiling
    /// derived from THIS player's eye height would make a SHARED window's pose per-client, which is
    /// the one thing the shared anchor exists to prevent. 1.90 m is set against the surveyed eye
    /// height over this table (ModBuild 250 log: eye 231.57 wu, table top 0.00 wu, 198.12 wu/m ⇒
    /// 1.169 m), i.e. ~0.73 m above the eye — around +25° at the map room's reading distances. At
    /// the shipped default it is UNREACHED by every window that session placed: the tallest is
    /// 'New Party display' at 1.134 m, whose top lands at 1.752 m. It exists for the window that has
    /// not been measured yet, and when it fires it SAYS SO, by name, on its own line.</para></summary>
    private const float MapRoomWindowTopCeilingMeters = 1.90f;

    /// <summary>
    /// THE ONE HEIGHT, resolved for one window: how far above the map table's top surface this
    /// window's GRAB BAR is seated, in real metres.
    ///
    /// <para>Returns the dial's value for every window that fits under
    /// <see cref="MapRoomWindowTopCeilingMeters"/>, which is the whole point — a room full of
    /// windows whose bars are on one line. A window too tall for that is LOWERED, by the least
    /// amount that brings its top edge under the ceiling, and never below
    /// <see cref="MapRoomWindowMinBarHeightMeters"/>. <paramref name="rule"/> names which of the
    /// three branches decided, in the words the log line prints; it is never empty, so a reader can
    /// always tell "the dial" from "the ceiling overruled the dial" without re-deriving
    /// anything.</para>
    /// </summary>
    /// <param name="halfHeightMeters">The window's own half-height in REAL METRES (its world
    /// half-size divided by the shared frame scale) — the units everything else here is in.</param>
    /// <param name="rule">Which branch decided, in the words the falsifier prints.</param>
    internal static float ResolveMapRoomBarHeightMeters(float halfHeightMeters, out string rule)
    {
        float dial = Mathf.Max(WorldUIConfig.MapRoomWindowBarHeightMeters.Value,
            MapRoomWindowMinBarHeightMeters);
        float height = 2f * Mathf.Max(halfHeightMeters, 0f);
        float top = dial + GrabBarDropMeters + height;
        if (top <= MapRoomWindowTopCeilingMeters)
        {
            rule = "THE DIAL DECIDED IT: [WorldUI] MapRoomWindowBarHeightMeters = "
                   + $"{dial:F3} m above the table top, and this window's top edge lands at "
                   + $"{top:F3} m, under the {MapRoomWindowTopCeilingMeters:F2} m ceiling — so it "
                   + "takes the common height and its bar is level with every other window here";
            return dial;
        }

        float lowered = MapRoomWindowTopCeilingMeters - GrabBarDropMeters - height;
        if (lowered >= MapRoomWindowMinBarHeightMeters)
        {
            rule = $"*** THE TOP-EDGE CEILING OVERRULED THE DIAL: this window is {height:F3} m "
                   + $"tall, so the common bar height {dial:F3} m would put its top edge at "
                   + $"{top:F3} m, above the {MapRoomWindowTopCeilingMeters:F2} m ceiling "
                   + "(MapRoomWindowTopCeilingMeters — a constant of the TABLE and of no head, so "
                   + "this decision is identical on every client). The bar is lowered to "
                   + $"{lowered:F3} m, the least that brings the top under the ceiling, and THIS "
                   + "WINDOW'S BAR IS THEREFORE NOT LEVEL WITH THE OTHERS: a stated trade, not a "
                   + "silent one ***";
            return lowered;
        }

        rule = $"*** THE CEILING COULD NOT BE HONOURED: this window is {height:F3} m tall, more "
               + $"than the {MapRoomWindowTopCeilingMeters:F2} m ceiling leaves above the table "
               + $"once the {MapRoomWindowMinBarHeightMeters:F2} m table floor is taken, so no bar "
               + "height satisfies both. THE TABLE FLOOR WINS — a window standing IN the table is "
               + "the ModBuild 243 defect and is never traded away — so the bar sits at "
               + $"{MapRoomWindowMinBarHeightMeters:F3} m and the top edge at "
               + $"{MapRoomWindowMinBarHeightMeters + GrabBarDropMeters + height:F3} m, OVER the "
               + "ceiling. A window this tall has to be made shorter; nothing here can fix it ***";
        return MapRoomWindowMinBarHeightMeters;
    }

    /// <summary>Every map-room window this room has placed, in placement order, with the bar height
    /// it was placed at — so the falsifier can print the WHOLE room on ONE line and a disagreement
    /// is a difference between two numbers side by side rather than a diff between two log lines a
    /// thousand entries apart.</summary>
    private static readonly List<string> MapRoomBarNames = new(8);

    /// <summary>Bar heights above the table top, parallel to <see cref="MapRoomBarNames"/>.</summary>
    private static readonly List<float> MapRoomBarHeights = new(8);

    /// <summary>Scratch for the roster text. One instance, cleared per call: this runs on the spawn
    /// / re-place path only, never per frame.</summary>
    private static readonly System.Text.StringBuilder MapRoomBarRoster = new(192);

    /// <summary>
    /// THE FALSIFIER FOR "die Höhe soll bei allen Fenstern gleich sein gemessen am Greifbalken".
    /// One line per map-room placement carrying THIS window's resolved bar height, the rule that
    /// decided it, the body it hangs, and the bar height of EVERY map-room window placed since the
    /// room opened. Grep <c>MAP ROOM WINDOW BAR HEIGHT</c>: if the roster at the end of the line
    /// does not read the same number for every entry, the rule did not hold — and the entry whose
    /// number differs carries its own reason on its own line.
    /// </summary>
    /// <param name="windowName">The window as the rest of the log names it.</param>
    /// <param name="family">SHARED or LOCAL — which placement path seated it.</param>
    /// <param name="barYm">Bar height above the table top, real metres.</param>
    /// <param name="halfHeightMeters">The window's own half-height, real metres.</param>
    /// <param name="rule">The branch <see cref="ResolveMapRoomBarHeightMeters"/> took.</param>
    internal static void LogMapRoomBarHeight(string windowName, string family, float barYm,
        float halfHeightMeters, string rule)
    {
        int at = MapRoomBarNames.IndexOf(windowName);
        if (at < 0)
        {
            MapRoomBarNames.Add(windowName);
            MapRoomBarHeights.Add(barYm);
        }
        else
        {
            MapRoomBarHeights[at] = barYm;
        }

        float own = 2f * Mathf.Max(halfHeightMeters, 0f);
        float bottom = barYm + GrabBarDropMeters;
        float top = bottom + own;
        float lowest = float.MaxValue;
        float highest = float.MinValue;
        MapRoomBarRoster.Length = 0;
        for (int i = 0; i < MapRoomBarNames.Count; i++)
        {
            float h = MapRoomBarHeights[i];
            if (h < lowest)
                lowest = h;
            if (h > highest)
                highest = h;
            if (i > 0)
                MapRoomBarRoster.Append(" | ");
            MapRoomBarRoster.Append('\'').Append(MapRoomBarNames[i]).Append("' ")
                .Append(h.ToString("F3")).Append(" m");
        }
        float spreadMm = (highest - lowest) * 1000f;

        VRLog.Info("WorldUI",
            $"MAP ROOM WINDOW BAR HEIGHT — '{windowName}' ({family}) bar seated {barYm:F3} m above "
            + "the map table's top surface, and its body hangs from there: bottom edge "
            + $"{bottom:F3} m (= bar + the measured {GrabBarDropMeters:F3} m frame-to-bar drop), "
            + $"top edge {top:F3} m, own height {own:F3} m. {rule}. A BOTTOM EDGE AT OR BELOW "
            + "0.000 m WOULD BE THE ModBuild 243 DEFECT (the window standing IN the table, "
            + "multiplayer_fesnter_position.jpg) and cannot happen while the "
            + $"{MapRoomWindowMinBarHeightMeters:F2} m floor holds. EVERY MAP-ROOM WINDOW PLACED "
            + $"SINCE THIS ROOM OPENED, bar above the table: [{MapRoomBarRoster}] — SPREAD "
            + $"{spreadMm:F0} mm. "
            + (spreadMm <= 1f
                ? "ALL LEVEL, which is the user ruling \"die Höhe soll beim Spawn am Besten bei "
                  + "allen Fenster gleich sein gemessen am Greifbalken\" holding."
                : "*** NOT LEVEL — the entries above disagree, and the ONLY sanctioned reason is a "
                  + "window whose own line says the TOP-EDGE CEILING overruled the dial. If no "
                  + "line says that, this build's rule did not hold and a THIRD path is seating "
                  + "map-room windows. ***")
            + " NOT IN THIS ROSTER, BY DESIGN: the hover card ('UI Quest Preview Popup'), which is "
            + "pinned to the map icon it describes and re-posed every tick by HoverCardPose, so it "
            + "has no spawn height to set. MULTIPLAYER: the height is [WorldUI] "
            + "MapRoomWindowBarHeightMeters, a cfg value, so for a SHARED (blue-barred) window it "
            + "is 1:1 only while both clients hold the same number — the same divergence surface "
            + "SharedWindowArcRadiusMeters has, and the reason this line prints the number it "
            + "used. Nothing here reads a head, so nothing here can differ for any other reason.");
    }

    /// <summary>Forget the roster. Called on the same teardown edge that resets the shared anchors —
    /// a roster that outlived the room would print windows that are not standing and make the
    /// spread unreadable.</summary>
    private static void ResetMapRoomBarRoster()
    {
        MapRoomBarNames.Clear();
        MapRoomBarHeights.Clear();
    }

    /// <summary>
    /// THE MAP TABLE'S TOP SURFACE IN WORLD Y, and the frame scale that turns real metres into
    /// world units on it — the two numbers a LOCAL map-room window needs to be seated at the same
    /// bar height as the shared ones.
    ///
    /// <para>IT IS THE SAME MEASUREMENT THE SHARED ANCHOR USES, so the two families cannot drift
    /// apart by construction: <c>TrySharedAnchorOnTable</c> computes the top as
    /// <c>(b.max.y − centre.y) / scale</c> in frame-local metres and then converts back with
    /// <c>centre + local × scale</c>, which is <c>b.max.y</c> exactly. Read directly here so a
    /// local window is measured against the SAME surface and not against
    /// <c>CameraController.FocusPoint</c>, which happens to coincide in the shipped map room and is
    /// a different object.</para>
    ///
    /// <para>NO SURVEY CHECK, DELIBERATELY. The shared anchor refuses when the parchment's aspect
    /// is not the surveyed one, because it INVENTS a table slab around the map from surveyed
    /// ratios and an invented table is not a shared reference. This function invents nothing: the
    /// top of the parchment's own bounds is the top of the parchment's own bounds whatever shape it
    /// is.</para>
    /// </summary>
    internal static bool TryMapTableTopWorldY(out float topWorldY, out float frameScale)
    {
        topWorldY = 0f;
        frameScale = 1f;
        if (!MapRoom.MapRoomDriver.Active)
            return false;
        if (!MapRoom.MapRoomDriver.TryGetParchmentFrame(out Vector3 _, out float scale))
            return false;
        MeshRenderer? parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return false;
        Bounds b = parchment.bounds;
        if (b.size.x <= 1e-3f || b.size.z <= 1e-3f)
            return false;
        topWorldY = b.max.y;
        frameScale = Mathf.Max(scale, 1e-4f);
        return true;
    }

    // ModBuild 245 TOMBSTONE — SharedAnchorLateralFraction (0.45 of the table's half-depth) is
    // DELETED, not merely unused. It seated a shared window by a fraction of the FURNITURE, which
    // has no term for how wide the window is, and taking a max with it is what pushed his two blue
    // windows out to the ends of the table (kartenraum_remotespawn.jpg, "nicht zentral mittig über
    // dem Tisch wie erwartet"). The step is now derived from the window itself and nothing else —
    // see SharedAnchorLateralGapMeters. Do not reintroduce a table-relative floor: "nur
    // Überlappungen sollen vermieden werden" is the whole rule.

    /// <summary>Clear air between two shared windows' facing edges, real metres. Not cosmetic: two
    /// windows that merely touch read as one wide window with a seam. Each of a pair steps by HALF
    /// of this, so the pair separates by exactly halfA + halfB + this and by nothing more — "nur
    /// Überlappungen sollen vermieden werden", taken literally.</summary>
    private const float SharedAnchorLateralGapMeters = 0.12f;

    // ModBuild 250 TOMBSTONE — SharedAnchorMapEdgeMarginMeters (0.22 m past the map's own far edge)
    // is DELETED. It set the DEPTH of a single far-edge home, and the depth is no longer a constant:
    // every shared window now sits on an ARC of radius WorldUIConfig.SharedWindowArcRadiusMeters
    // about the table centre, so the depth of a slot is derived from that radius and the slot's own
    // lateral step. What the margin actually guaranteed — "nothing is drawn over the thing he is
    // looking at" — survives as SharedAnchorArcParchmentClearanceMeters below, which is a HARD floor
    // on the depth rather than a seat.

    /// <summary>The least clear air, real metres, between the parchment's own far edge and the plane
    /// a shared window stands in.
    ///
    /// <para>THIS IS THE MAP-OCCLUSION GUARD, and it is a floor and not a seat. A window on the arc
    /// is a PLANE at one depth spanning the lateral axis, so it hides the map exactly when its depth
    /// falls inside the parchment's own footprint — which the arc can only do for a slot whose
    /// lateral step is large enough to pull it round the side of the circle. The floor pushes such a
    /// slot OFF the arc, straight out to the map's far edge plus this, and the falsifier line says
    /// by how many millimetres it left the arc. A radius above the parchment's own CIRCUMRADIUS
    /// (0.768 m on the surveyed 0.96 x 1.20 m map) makes the floor unreachable for every lateral
    /// step, which is why the shipped default is 0.80 m.</para></summary>
    private const float SharedAnchorArcParchmentClearanceMeters = 0.02f;

    // ModBuild 290 TOMBSTONE — SharedAnchorBoardMarginLocal (0.30 board-LOCAL above
    // PlayTray.MeasureBoardLocalExtents' top edge) IS DELETED, AND THE MISTAKE WAS THE FRAME AND
    // NOT THE NUMBER. Keep the history, because the name that caused it is still in the codebase:
    //
    //  * WHAT IT WAS. The scenario home was seated in the frame of `Cards.PlayTray.Current.Root`,
    //    one margin past the top edge that class MEASURES. Its own doc called that "the board",
    //    which it is — PlayTray IS "the control board", the player's chest-height card desk. It is
    //    NOT das Spielfeld. The user ruling the anchor was built from says both in one sentence
    //    ("verankere es am Tisch bzw. ÜBER DEM SPIELFELD innerhalb eines Szenarios") and the
    //    implementation took the word "board" to the wrong object.
    //
    //  * WHAT THAT COST, from his own log (spawn_scenario.jpg, ModBuild 289 LogOutput.log:4662).
    //    The tray was IN HIS HAND at that moment ("FIXIERT, HELD — the hand is carrying it"), so
    //    the anchor followed it: local (0, 0.610, 0) in a frame at world (1.17,−1.41,−1.49) put the
    //    window at world y 2.39 wu with the play surface at 0.00 wu and the diorama at 9.57 wu/m —
    //    i.e. its BOTTOM EDGE 0.169 m above the hexes, down among the figures, inside the room.
    //    THE FALSIFIER LINE READ +0.300 AND WAS CORRECT: the bottom edge really was 0.300 above the
    //    top edge OF THE CARD TRAY. A number that clears the wrong object cannot be tuned into a
    //    number that clears the right one, which is why this is a frame change and not a bigger
    //    margin. [[a-remedy-knows-one-writer]] — the ModBuild 244 clearance was authored against
    //    the map table and the scenario never got the same treatment, only the same word.
    //
    //  * ONE MORE THING IT WAS SILENTLY DECIDING: the SIZE. worldScale came from the tray's own
    //    lossyScale (6.32 in that log) while every other scenario window is built at
    //    PanelLayout.WorldScale (9.57), so the shared story box shipped 34 % smaller than its
    //    neighbours for no stated reason. The play-field frame below restores it.
    //
    //  * WHAT REPLACES IT: ScenarioWindowBoardClearanceMeters, a GRAB-BAR height in real metres
    //    above the play field's own surface, in the seat-anchor frame — see
    //    TrySharedAnchorOverBoard. Do not reintroduce a length in board-local units: the tray is a
    //    hand-carried object and a length in its frame is a length in the player's hand.

    /// <summary>The floor under the resolved scenario bar height, real metres above the play
    /// surface. The dial's range starts here too; this constant is the guarantee. It is not zero:
    /// the play surface is the ORBIT-FOCUS PLANE, and the board's own furniture stands
    /// <c>BoardTopClearanceMeters</c> (0.30 m) above it, so a bar below that is a bar in the
    /// scenery — the ModBuild 243 defect, in the other room.</summary>
    private const float ScenarioWindowMinBarHeightMeters = BoardTopClearanceMeters;

    /// <summary>Which shared kinds have had a real pose and may no longer be anchored. Keyed by the
    /// KIND and not by the window, because the kind is what the pose is addressed to on the wire and
    /// because <see cref="SharedWindowIdentity"/> may legitimately move a kind to a different
    /// window while the pose stays.</summary>
    private static readonly HashSet<SharedWindowKind> SharedAnchorSpent = new();

    /// <summary>Why each spent kind is spent, in the words the refusal line prints.</summary>
    private static readonly Dictionary<SharedWindowKind, string> SharedAnchorSpentWhy = new();

    /// <summary>
    /// A REAL POSE NOW EXISTS FOR THIS KIND — this client published one, or a peer's was applied
    /// here — so the spawn anchor is spent for the rest of this window's life.
    ///
    /// <para>Called from <c>Net.RemoteMapStory</c> on both edges. The dependency direction is the
    /// one this project already has (<c>Net → WorldUI</c>), the same mailbox shape as
    /// <see cref="SharedWindowIdentity.NotePoseApplied"/>.</para>
    /// </summary>
    internal static void NoteSharedAnchorSpent(SharedWindowKind kind, string why)
    {
        if (kind == SharedWindowKind.None || !SharedAnchorSpent.Add(kind))
            return;
        SharedAnchorSpentWhy[kind] = why;
        VRLog.Info("WorldUI", $"SHARED WINDOW ANCHOR SPENT — {kind}: {why}. From here on the spawn "
                              + "anchor stands down for this kind and every further placement "
                              + "opportunity prints REFUSED with this reason. The user's own "
                              + "narrowing is what this enforces: \"'verankert am Tisch' - ich meine "
                              + "nur die initiale Spawnposition - es soll weiterhin von jedem "
                              + "verschiebbar sein wie du es bereits implementiert hast (voll "
                              + "synchronisiert)\". NOTHING IS MOVED BY THIS LINE — it only decides "
                              + "whether a FUTURE placement is allowed to use the anchor.");
    }

    /// <summary>Forget every spend. Called on the same teardown edges that release the floats: a
    /// latch that outlived the room would refuse the anchor on the next entry for a drag nobody in
    /// that session made.</summary>
    internal static void ResetSharedAnchors(string reason)
    {
        // The bar-height roster dies with the room, and it dies BEFORE the early return below: it
        // fills up on ordinary placements that never spend an anchor, so gating its clear on
        // SharedAnchorSpent.Count would leave last room's windows in the next room's falsifier and
        // make the SPREAD read as a disagreement that nobody can see.
        ResetMapRoomBarRoster();
        if (SharedAnchorSpent.Count == 0)
            return;
        VRLog.Info("WorldUI", $"SHARED WINDOW ANCHOR RESET ({reason}) — {SharedAnchorSpent.Count} "
                              + "kind(s) had a pose and had spent their anchor; they may be anchored "
                              + "again on the next spawn. A latch that outlived the room would "
                              + "refuse the anchor for a drag nobody in the new session made.");
        SharedAnchorSpent.Clear();
        SharedAnchorSpentWhy.Clear();
    }

    /// <summary>The home this kind owns, by IDENTITY and never by arrival order. −1 = this kind has
    /// no home in the room that is standing. See the block comment for the whole table.</summary>
    private static int SharedHomeIndex(SharedWindowKind kind) => kind switch
    {
        SharedWindowKind.ScenarioStory => 0,   // its own room; the map kinds are not in it
        SharedWindowKind.MapStory => 0,
        SharedWindowKind.QuestConfirm => 1,
        SharedWindowKind.Encounter => 2,
        _ => -1,
    };

    /// <summary>The shared kinds, in the order their homes are reserved. A static array so the
    /// lookup below allocates nothing.</summary>
    private static readonly SharedWindowKind[] SharedKinds =
    {
        SharedWindowKind.ScenarioStory,
        SharedWindowKind.MapStory,
        SharedWindowKind.QuestConfirm,
        SharedWindowKind.Encounter,
    };

    /// <summary>
    /// The game window this converted panel was built for, or null. Taken ONLY on the spawn /
    /// re-place path — never per frame.
    ///
    /// <para><b>TWO LOOKUPS, AND THE SECOND ONE IS NOT OPTIONAL.</b> The float list answers on the
    /// pre-reveal re-place, when the <c>WindowPanel</c> exists. IT DOES NOT ANSWER AT THE SPAWN:
    /// <c>ModalFallback.8.Convert</c> calls <c>PlaceAtHmd</c> BEFORE <c>Converted.Add(wp)</c>
    /// (Convert.cs:443 against :677), so at the moment the pose is first computed this panel is in
    /// no list at all. Relying on the list alone would have made the anchor a re-place-only effect
    /// that never printed a spawn line — [[gated-remedy-never-ran]] with a plausible-looking log.
    /// So the fallback asks each SHARED KIND for its own window and tests this panel's converted
    /// rect against it.</para>
    ///
    /// <para>The second test is a containment question about ONE NAMED INSTANCE, not the
    /// "is this an X?" question [[containment-is-not-identity]] is about: identity is still decided
    /// by <see cref="SharedWindows.KindOf"/> against the game's own singletons, and this only asks
    /// whether the rect that was converted is that window's own rect or a rect inside it — which is
    /// exactly what a conversion of that window produces.</para>
    /// </summary>
    private static UIWindow? WindowForPanel(ConvertedPanel? panel)
    {
        if (panel == null)
            return null;
        for (int i = 0; i < Converted.Count; i++)
        {
            if (ReferenceEquals(Converted[i].Panel, panel))
                return Converted[i].Window;
        }
        RectTransform? target = panel.Target;
        if (target == null)
            return null;
        for (int i = 0; i < SharedKinds.Length; i++)
        {
            SharedWindowKind kind = SharedKinds[i];
            if (!SharedWindows.ParticipatesHere(kind))
                continue;
            UIWindow? window = kind == SharedWindowKind.QuestConfirm
                ? QuestPopupWindow()
                : SharedWindows.WindowOf(kind);
            if (window == null)
                continue;
            if (ReferenceEquals(target.gameObject, window.gameObject)
                || target.IsChildOf(window.transform))
                return window;
        }
        return null;
    }

    /// <summary>The quest popup has no singleton — its identity IS its id, the same rule
    /// <c>SharedWindows.TryGetGrab</c> applies to it. Found on the OPEN set rather than by a scene
    /// sweep ([[findobjectsoftype-is-the-default-suspect]]).</summary>
    private static UIWindow? QuestPopupWindow()
    {
        for (int i = 0; i < Converted.Count; i++)
        {
            UIWindow w = Converted[i].Window;
            if (w != null && w.ID == UIWindowID.QuestPopup)
                return w;
        }
        foreach (UIWindow w in Open)
        {
            if (w != null && w.ID == UIWindowID.QuestPopup)
                return w;
        }
        return null;
    }

    /// <summary>
    /// THE SHARED SPAWN POSE, or false when this window is not a shared one / the frame cannot be
    /// measured / the anchor is spent.
    ///
    /// <para>Returning false is the ONLY way a local window is affected by any of this, and it
    /// leaves <c>ComputeHmdPose</c> on exactly the path it took before ModBuild 243 — the arc seat,
    /// the clamps, the head-facing yaw, bit for bit.</para>
    /// </summary>
    /// <param name="worldPos">The anchored world position of the HOST rect.</param>
    /// <param name="worldRot">The anchored world rotation, yaw-only.</param>
    /// <param name="worldScale">The world scale to build the window at — taken from the SHARED
    /// frame, so the window is the same size relative to the furniture on every client.</param>
    /// <param name="line">The whole falsifier line, ready to print.</param>
    private static bool TrySharedWindowAnchor(ConvertedPanel? panel, bool replay, Vector2 halfSize,
        out Vector3 worldPos, out Quaternion worldRot, out float worldScale, out string line)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;
        worldScale = 1f;
        line = string.Empty;

        UIWindow? window = WindowForPanel(panel);
        if (window == null)
            return false;
        SharedWindowKind kind = SharedWindows.KindOf(window);
        if (kind == SharedWindowKind.None || !SharedWindows.ParticipatesHere(kind))
            return false;   // a LOCAL window, or a shared kind this client does not sync — untouched

        string stage = replay ? "RE-PLACE at the final fitted geometry" : "SPAWN";
        if (SharedAnchorSpent.Contains(kind))
        {
            SharedAnchorSpentWhy.TryGetValue(kind, out string? spentWhy);
            VRLog.Info("WorldUI",
                $"SHARED WINDOW ANCHOR REFUSED ({stage}) — '{window.name}' carries "
                + $"SharedWindowKind.{kind}, and a real pose already exists for that kind: "
                + $"{spentWhy ?? "a pose was published or applied"}. The anchor is the INITIAL spawn "
                + "position and nothing else (\"ich meine nur die initiale Spawnposition\"), so it "
                + "stands down and the window keeps the pose the drag gave it. THIS IS THE EXPECTED "
                + "LINE after anybody has moved the window; its ABSENCE on a re-place after a drag "
                + "is the bug, because a silent no-op looks exactly like the anchor never having "
                + "been built.");
            return false;
        }

        int home = SharedHomeIndex(kind);
        if (home < 0)
            return false;

        bool seated = MapRoom.MapRoomDriver.Active
            ? TrySharedAnchorOnTable(window, kind, home, stage, halfSize, out worldPos, out worldRot,
                                     out worldScale, out line)
            : TrySharedAnchorOverBoard(window, kind, home, stage, halfSize, out worldPos, out worldRot,
                                       out worldScale, out line);
        if (seated && !replay)
            GrantPoseCriticalRevealGrace(panel!, window, kind);
        return seated;
    }

    /// <summary>How much longer than <c>CanvasConversion.RevealMaxWaitSeconds</c> a SHARED window
    /// whose spawn pose came from the table anchor may stay render-hidden while its content fit is
    /// still pending, seconds. 0.9 s on top of the 0.6 s bound = a 1.5 s hard budget.
    ///
    /// <para>The number is a budget and not a guess about the game: the falsifier prints the budget
    /// it used and what the gate was still waiting on, so ONE hardware log settles whether 1.5 s is
    /// enough (a MODAL REVEAL "shown after settle" inside it) or whether the content simply never
    /// becomes measurable for this family (a MODAL REVEAL "FORCED after ~1500 ms ... still waiting
    /// on first content fit"), which is a different bug in a different place.</para></summary>
    private const float SharedAnchorRevealGraceSeconds = 0.9f;

    /// <summary>
    /// THE POSE THIS WINDOW IS ABOUT TO BE REVEALED AT DEPENDS ON A RECT IT HAS NOT MEASURED YET —
    /// buy the pre-reveal re-place time to run, ONCE, while nothing is visible.
    ///
    /// <para>See <see cref="ConvertedPanel.RevealPoseCriticalGraceGiven"/> for the bug this answers,
    /// for the two rulings that collide over it and for why the deadline moves rather than the
    /// window. Deliberately narrow: a shared window, at its SPAWN placement, whose fit is enabled
    /// and has not measured. A local window, a re-place, or a window that is already fitted gets
    /// nothing and keeps the 0.6 s bound bit for bit.</para>
    /// </summary>
    private static void GrantPoseCriticalRevealGrace(ConvertedPanel panel, UIWindow window,
                                                     SharedWindowKind kind)
    {
        if (panel == null || panel.RevealPoseCriticalGraceGiven)
            return;
        // Not render-hidden (no reveal gate armed) ⇒ there is no deadline to extend and no invisible
        // window to protect: the pose is whatever it is and the re-place path is not in play.
        if (!panel.RevealPending || panel.RevealDeadline <= 0f)
            return;
        // Already geometry-final ⇒ the pose about to be written IS the final one and the re-place
        // will find it identical. Extending the bound would buy nothing and delay a ready window.
        if (!panel.FitEnabled || panel.FitMeasuredOnce)
            return;

        panel.RevealPoseCriticalGraceGiven = true;
        float was = panel.RevealDeadline;
        panel.RevealDeadline = was + SharedAnchorRevealGraceSeconds;
        VRLog.Info("WorldUI",
            $"SHARED WINDOW REVEAL GRACE — '{window.name}' ({kind}) was just seated by the shared "
            + "table/board anchor, and that pose is computed from the window's own half-size. Its "
            + "content fit has NOT measured yet, so the pose written now is derived from the PRE-fit "
            + "rect and only the one pre-reveal re-place can correct it — and that re-place is "
            + "PERMANENTLY REFUSED the moment the reveal deadline fires (grep POSE LOCK / 'MODAL "
            + "REVEAL: ... FORCED'). On .planning/debug/LogOutput.log that is exactly what happened "
            + "to 'UI Event Window' at 601 ms, while its sibling's re-place was worth 0.260 m. So "
            + $"this panel's hard reveal bound moves ONCE, from {was - panel.RevealRequestedAt:F2} s "
            + $"to {panel.RevealDeadline - panel.RevealRequestedAt:F2} s after convert, and NOTHING "
            + "ELSE changes: the window still never moves once it is visible (that ruling is "
            + "absolute — a shared window that jumps jumps for every player at once), and it still "
            + "cannot stay invisible, because the bound is still hard. If the next MODAL REVEAL line "
            + "for this window says 'shown after settle', the grace did its job; if it says 'FORCED "
            + "... still waiting on first content fit', 1.5 s is not the answer and the fit itself "
            + "is the suspect.");
    }

    /// <summary>THE MAP ROOM HOME — parchment frame, real metres, absolute world rotation. Every
    /// term is a pure function of the parchment renderer's own world bounds, so two clients compute
    /// the same frame-local numbers with nothing sent.</summary>
    private static bool TrySharedAnchorOnTable(UIWindow window, SharedWindowKind kind, int home,
        string stage, Vector2 halfSize, out Vector3 worldPos, out Quaternion worldRot,
        out float worldScale, out string line)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;
        worldScale = 1f;
        line = string.Empty;

        if (!MapRoom.MapRoomDriver.TryGetParchmentFrame(out Vector3 centre, out float frameScale))
            return false;
        MeshRenderer? parchment = MapRoom.MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return false;
        Bounds b = parchment.bounds;
        if (b.size.x <= 1e-3f || b.size.z <= 1e-3f)
            return false;

        // THE SAME SURVEY FALSIFIER THE CORNER RULE USES. A parchment that is not the shape that was
        // surveyed would have a table INVENTED around it, and an invented table is not a shared
        // reference. The anchor stands down rather than guessing, and the window falls back to the
        // ordinary per-client placement — worse, but honest, and the log says which happened.
        float aspect = b.size.x / b.size.z;
        if (Mathf.Abs(aspect - MapAspectXOverZ) > MapAspectTolerance)
        {
            VRLog.Info("WorldUI",
                $"SHARED WINDOW ANCHOR UNAVAILABLE ({stage}) — '{window.name}' ({kind}): the live "
                + $"parchment measures {b.size.x:F1} x {b.size.z:F1} world units, aspect "
                + $"{aspect:F2}, and the surveyed map is {MapAspectXOverZ:F2}. The slab ratios "
                + "describe a table around THAT map, so the table this anchor would hang on is a "
                + "fiction. The window takes the ordinary per-client placement instead, which means "
                + "IT IS NOT 1:1 THIS TIME — and this line, not a silence, is what says so.");
            return false;
        }

        float scale = Mathf.Max(frameScale, 1e-4f);
        // Frame-local metres. The table half-extents come off the same surveyed ratios
        // TryTableFarCornersDeg uses, about the centre the two objects share.
        float halfXm = b.size.x * 0.5f * TableToMapWidthRatio / scale;
        float halfZm = b.size.z * 0.5f * TableToMapDepthRatio / scale;
        float topYm = (b.max.y - centre.y) / scale;

        // The SHORT horizontal axis of the map is the axis the player reads it from — the table is
        // deeper than it is wide, so its two SHORT ends are the ends a person stands at. The sign is
        // a CONSTANT (+) and not a measurement; see the block comment for why, and for what it costs
        // a player standing at the other end.
        bool shortAxisIsX = b.size.x <= b.size.z;
        float farHalf = shortAxisIsX ? halfXm : halfZm;
        float lateralHalf = shortAxisIsX ? halfZm : halfXm;

        // THE WINDOW'S OWN SIZE, IN THE SHARED FRAME'S METRES (ModBuild 244). halfSize arrives in
        // WORLD units from ComputeHmdPose's caller, which measured it at PanelLayout.WorldScale x
        // extraScale; dividing by the frame scale puts it in the same real metres every other term
        // here is in. On the SPAWN call this is the pre-fit host rect and on the RE-PLACE it is the
        // fitted one — that is why the re-place exists, and why the pose it lands on is the one the
        // player sees. See the ModBuild 251 block above (and the ModBuild 244 tombstone in it) for
        // why reading it at all is the fix and not a hazard.
        float halfWinYm = Mathf.Max(halfSize.y / scale, SharedAnchorMinHalfHeightMeters);
        float halfWinXm = Mathf.Max(halfSize.x / scale, 0f);

        // ---- ModBuild 245 — THE STEP WAS TWICE WHAT IT NEEDED TO BE, AND THE FLOOR PUSHED THEM TO
        //      THE ENDS OF THE TABLE. "nicht zentral mittig über dem Tisch wie erwartet ... der
        //      Abstand ist viel zu groß - nur Überlappungen sollen vermieden werden."
        //
        //      244 stepped each window by 2 x its own half-width + a gap, which separates a PAIR by
        //      TWICE the width it needs (each side already carries a half-width), and then took the
        //      MAX with 0.45 x the table half-depth, which is a floor that has nothing to do with
        //      the windows at all and is what shoved them out to the ends in
        //      kartenraum_remotespawn.jpg. Both are gone. Each window steps by its OWN half-width
        //      plus HALF the gap, so a pair separates by exactly halfA + halfB + gap: touching plus
        //      the clear air, and not one millimetre more. Two same-size windows straddle the centre
        //      of the table, which is what he asked for.
        float step = halfWinXm + SharedAnchorLateralGapMeters * 0.5f;
        float lateral = home switch
        {
            1 => +step,
            2 => -step,
            _ => 0f,
        };
        // ---- ModBuild 251 — THE HEIGHT IS THE ROOM'S ONE BAR HEIGHT, NOT A BOTTOM-EDGE CLEARANCE.
        //      "die Höhe soll beim Spawn am Besten bei allen Fenster gleich sein gemessen am
        //      Greifbalken!" The bar is seated at barYm above the table top and the body hangs from
        //      it; see the ModBuild 251 block above for why the 0.110 m this replaces was a misread
        //      photograph and what the same photograph's windows were actually seated at.
        float barYm = ResolveMapRoomBarHeightMeters(halfWinYm, out string barRule);
        float bottomYm = barYm + GrabBarDropMeters;
        float centreYm = topYm + bottomYm + halfWinYm;

        // ---- ModBuild 250 — THE HALF-RING OVER THE TABLE CENTRE. User ruling, verbatim:
        //
        //      "Es ist ok das es nicht bei jedem nah steht. Ich möchte aber, das die Fenster
        //       zentral ÜBER dem Tisch mittig spawnen dort in einem halbkreis."
        //
        //      SO THE ARC IS CENTRED ON THE TABLE AND NOT ON A FAR EDGE. Every shared window's
        //      centre sits at the SAME radius from the parchment frame centre; the ring's OPENING
        //      faces the room (the depth term is the POSITIVE root, so no window is ever seated on
        //      the reader's own side of the table, which would put it between him and the map).
        //      The lateral step above is untouched — it is still the window's OWN half-width plus
        //      half the gap, so a pair still separates by exactly halfA + halfB + 0.12 m — and the
        //      ARC now supplies the DEPTH that goes with that step:
        //
        //          depth = sqrt(radius² − lateral²)
        //
        //      which is the circle, solved for the slot the separation rule already chose. A slot
        //      further out sideways is therefore automatically further FORWARD, i.e. nearer the
        //      reader, and the three slots wrap around him instead of standing in a flat rank.
        //
        //      WHY THE SEPARATION IS MEASURED ALONG THE LATERAL AXIS AND NOT AS ARC LENGTH. The
        //      windows are all yawed the SAME way (see the facing block below), so two neighbours
        //      are two PARALLEL planes and the air between them is their lateral gap, full stop.
        //      Stepping by arc length instead would have left the shipped pair 0.038 m apart where
        //      the rule asks for 0.120 — the chord of an arc is shorter than the arc — i.e. it
        //      would have quietly broken the one invariant ModBuild 245 established. The rule is
        //      the rule; the arc only decides how far back each slot stands.
        float mapFarHalf = shortAxisIsX
            ? b.size.x * 0.5f / scale
            : b.size.z * 0.5f / scale;
        float radius = Mathf.Max(WorldUIConfig.SharedWindowArcRadiusMeters.Value, 0.05f);
        float arcDepth = Mathf.Sqrt(Mathf.Max(radius * radius - lateral * lateral, 0f));
        // THE MAP-OCCLUSION GUARD. A slot whose lateral step swings it round the side of the circle
        // comes to stand at a depth INSIDE the parchment's own footprint, and a window plane there
        // hides the far strip of the map the player is trying to click. The floor pushes it back out
        // to the map's far edge; the line below reports how far it left the arc.
        //
        // THE TEST IS TWO-DIMENSIONAL, because the window is a PLANE and not a point. A slot whose
        // whole lateral SPAN — centre ± its own half-width — clears the map's lateral half is beside
        // the parchment, not over it, however shallow its depth, and pushing it back would cost
        // reading distance for nothing. `innerLateral` is the closest that span comes to the table's
        // centre line, and it is 0 for a window wide enough to straddle it.
        float mapLateralHalf = shortAxisIsX
            ? b.size.z * 0.5f / scale
            : b.size.x * 0.5f / scale;
        float innerLateral = Mathf.Max(Mathf.Abs(lateral) - halfWinXm, 0f);
        bool overMapLaterally = innerLateral < mapLateralHalf;
        float depthFloor = mapFarHalf + SharedAnchorArcParchmentClearanceMeters;
        float depthHalf = overMapLaterally ? Mathf.Max(arcDepth, depthFloor) : arcDepth;
        float offArcMm = (depthHalf - arcDepth) * 1000f;
        Vector3 localPos = shortAxisIsX
            ? new Vector3(depthHalf, centreYm, lateral)
            : new Vector3(lateral, centreYm, depthHalf);

        // FACING IS 1:1 AND IT IS THE SAME FOR EVERY SLOT (ModBuild 250). The window is yawed square
        // to the READING AXIS — the short horizontal axis of the table, whose negative end is the
        // end every client is seated at — and NOT along its own radius. Canvas front faces
        // −forward, so pointing +forward at the POSITIVE (far) end is what faces the reader at the
        // negative one; the same convention ComputeHmdPose's head-facing branch and
        // PanelPlacement.Facing use. Yaw only (ModBuild 189's ruling): a place has a level horizon.
        //
        // WHY NOT RADIAL, WHICH IS WHAT A CIRCLE INVITES AND WHAT ModBuild 245 SHIPPED. A window
        // facing its own radius line faces the ARC CENTRE, and the arc centre is the middle of the
        // TABLE — a place nobody stands. On his own log that turned two windows 64.87° and 133.28°,
        // i.e. 68.4° apart, and a pair 68° apart is not a row: it is the "die Fenster müssen immer
        // erst wieder so angeordnet werden, dass man es lesen kann" in the report. Facing the
        // reading axis makes every slot square-on to a reader anywhere on the axis, at any
        // distance, and costs the off-centre slots only the angle their own lateral step subtends
        // (19.4° for the widest slot the shipped pair produces — a flat panel is fully legible
        // there). It is still a CONSTANT of the shared frame, so it is bit-identical on every
        // client and the ModBuild 243 "never re-face a shared window on the player" ruling is
        // untouched: nothing here reads a head.
        Vector3 facing = shortAxisIsX ? Vector3.right : Vector3.forward;
        worldRot = Quaternion.LookRotation(facing, Vector3.up);
        worldPos = centre + localPos * scale;
        // THE SIZE IS TAKEN FROM THE SHARED FRAME TOO, not from PanelLayout.WorldScale, because that
        // one carries this player's own pinch-zoom — the same reason MapRoomDriver derives the frame
        // scale from the parchment and says so in as many words ("a shared frame must not move when
        // one player zooms"). A window that is 1:1 in place and not in size is not 1:1.
        worldScale = scale;

        // WHICH END THIS PLAYER IS STANDING AT, measured off the live head rather than re-solved:
        // MapRoomDriver.TrySolveSeat would re-run the parchment ENSURE on a spawn path, and the
        // only thing needed here is the sign, which the head's own offset from the shared centre
        // already carries. Read-only, no sweep, [[findobjectsoftype-is-the-default-suspect]].
        Camera? headCam = CanvasConversion.WorldCamera;
        string seatSide = "(no head camera)";
        if (headCam != null)
        {
            Vector3 fromCentre = headCam.transform.position - centre;
            float along = shortAxisIsX ? fromCentre.x : fromCentre.z;
            seatSide = $"{along / scale:F2} m along the short axis ⇒ this player stands at the "
                       + $"{(along >= 0f ? "POSITIVE (same as the home — he will see it edge-on or "
                                          + "from behind)" : "NEGATIVE (opposite the home — he reads "
                                          + "it square-on, which is the intended case)")} end";
        }
        line = $"SHARED WINDOW ANCHOR APPLIED ({stage}) — '{window.name}' carries "
               + $"SharedWindowKind.{kind}, which owns HOME {home} of the map table BY ITS IDENTITY "
               + "and not by the order anything opened. "
               + $"FRAME=PARCHMENT (MapRoomDriver.TryGetParchmentFrame: centre "
               + $"({centre.x:F2},{centre.y:F2},{centre.z:F2}) wu, scale {scale:F2} wu/m). "
               + "FRAME-LOCAL POSE, IN REAL METRES — THIS IS THE NUMBER THAT MUST BE IDENTICAL ON "
               + $"TWO CLIENTS: pos ({localPos.x:F4},{localPos.y:F4},{localPos.z:F4}) m, yaw "
               + $"{worldRot.eulerAngles.y:F2}°, size {worldScale:F2} wu/m. WORLD POSE, WHICH MAY "
               + $"LEGITIMATELY DIFFER: ({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}) wu. "
               + $"TABLE: half-width {halfXm:F3} m, half-depth {halfZm:F3} m, top surface "
               + $"{topYm:F3} m above the frame centre, derived from the parchment's own "
               + $"{b.size.x:F1}x{b.size.z:F1} wu bounds by the surveyed ratios "
               + $"({TableToMapWidthRatio:F2}x wide, {TableToMapDepthRatio:F2}x deep). "
               + "ABOVE THE TABLE, AND THIS IS THE ARITHMETIC THAT MUST BE CHECKED AGAINST THE "
               + $"PICTURE: window half-height {halfWinYm:F3} m, half-width {halfWinXm:F3} m; "
               + $"GRAB BAR at {barYm:F3} m ([WorldUI] MapRoomWindowBarHeightMeters — ModBuild 251, "
               + "\"die Höhe soll beim Spawn am Besten bei allen Fenster gleich sein gemessen am "
               + $"Greifbalken\"), so bottom edge = bar {barYm:F3} + the measured frame-to-bar drop "
               + $"{GrabBarDropMeters:F3} = {bottomYm:F3} m and centre = top {topYm:F3} + "
               + $"{bottomYm:F3} + half-height {halfWinYm:F3} = {centreYm:F3} m. THE BOTTOM EDGE "
               + $"SITS {centreYm - halfWinYm - topYm:+0.000;-0.000} m above the table surface — a "
               + "NEGATIVE number here is the ModBuild 243 defect (the window standing IN the "
               + $"table, multiplayer_fesnter_position.jpg) and nothing else. {barRule}. "
               + $"LATERAL STEP {step:F3} m = own half-width {halfWinXm:F3} + half the gap "
               + $"{SharedAnchorLateralGapMeters * 0.5f:F3}, so a PAIR separates by exactly "
               + $"halfA + halfB + {SharedAnchorLateralGapMeters:F2} m and by nothing more "
               + "(ModBuild 244 stepped by TWICE that and then took a max with a table fraction, "
               + "which is what pushed them to the ends of the table in "
               + "kartenraum_remotespawn.jpg). "
               + "ARC (ModBuild 250, \"zentral ÜBER dem Tisch mittig ... in einem halbkreis\"): "
               + $"radius {radius:F3} m ([WorldUI] SharedWindowArcRadiusMeters) about the TABLE "
               + $"CENTRE, so this slot's depth = sqrt({radius:F3}² − {lateral:F3}²) = "
               + $"{arcDepth:F3} m and it stands {Mathf.Atan2(lateral, Mathf.Max(depthHalf, 1e-4f)) * Mathf.Rad2Deg:F1}° "
               + "round the ring from dead ahead; the ring's OPENING faces the room (the depth is "
               + "the POSITIVE root, so no shared window is ever seated between the reader and the "
               + "map). SEATED AT DEPTH "
               + $"{depthHalf:F3} m, {(offArcMm > 0.5f ? $"which is {offArcMm:F0} mm OFF THE ARC — the map-occlusion floor (parchment far edge {mapFarHalf:F3} + {SharedAnchorArcParchmentClearanceMeters:F2} m) pulled it back out, which happens when a window is wide enough for its lateral step to swing it round the side of the circle while its own span still crosses the map; raise SharedWindowArcRadiusMeters to put it back on the ring" : $"on the arc (map-occlusion floor {depthFloor:F3} m, {(overMapLaterally ? "armed and not reached" : "not armed — this slot's whole span clears the map sideways")})")}. "
               + $"CLEAR OF THE MAP: the parchment is {mapFarHalf:F3} m deep and {mapLateralHalf:F3} m "
               + $"wide from the centre; this plane stands at depth {depthHalf:F3} m and its lateral "
               + $"span comes no closer than {innerLateral:F3} m to the centre line, so it is "
               + $"{(depthHalf > mapFarHalf || !overMapLaterally ? "CLEAR of the parchment" : "OVER THE PARCHMENT — this is the map-occlusion defect and nothing else")}. TABLE "
               + $"OVERHANG: the table's own far half is {farHalf:F3} m, so this slot hangs "
               + $"{depthHalf - farHalf:+0.000;-0.000} m past the table edge (positive = over the "
               + $"room, not over wood), and its outer edge reaches {Mathf.Abs(lateral) + halfWinXm:F3} m "
               + $"sideways against the table's own {lateralHalf:F3} m half-depth. "
               + $"The short horizontal axis is {(shortAxisIsX ? "X" : "Z")} "
               + "and the far end is its POSITIVE one, fixed; EVERY SLOT IS YAWED THE SAME "
               + $"{worldRot.eulerAngles.y:F2}° square to that axis, which is the ModBuild 250 fix "
               + "for two windows that pointed 68.4° apart. This client's own head sits at "
               + $"{seatSide}. "
               + "NO WIRE FIELD WAS NEEDED: both terms are pure functions of the parchment bounds, "
               + "which is the same frame record 21 already sends a DRAGGED pose in. THE DRAG STILL "
               + "WINS — this is the initial spawn pose only, and the first published or applied "
               + "pose spends the anchor (grep SHARED WINDOW ANCHOR SPENT / REFUSED).";
        // THE ONE-LINE FALSIFIER FOR THE HEIGHT RULE, on the same event and never per frame. It is
        // separate from the line above because it is about the ROOM and not about this window: it
        // carries every map-room window's bar height side by side, so "sind alle gleich hoch?" is
        // one grep and one glance instead of a diff between two placements.
        LogMapRoomBarHeight(window.name, "SHARED", barYm, halfWinYm, barRule);
        return true;
    }

    /// <summary>
    /// THE SCENARIO HOME — floating over the PLAY FIELD, in the SEAT-ANCHOR frame, at a grab-bar
    /// height above the play surface.
    ///
    /// <para><b>ModBuild 290 — THE FRAME MOVED FROM THE CARD TRAY TO THE PLAY FIELD, AND THAT IS
    /// THE WHOLE FIX.</b> USER REPORT (2026-08-25, verbatim, with spawn_scenario.jpg): <i>"Das
    /// 'blaue' Multiplayer Fenster ist IN dem Spielfeld gespawned … Das darf nicht passieren, es
    /// muss viel höher spawnen damit es über dem Spielfeld schwebt."</i> See the
    /// <c>SharedAnchorBoardMarginLocal</c> tombstone above for what the old frame was, why its own
    /// falsifier read <c>+0.300</c> while the window stood among the figures, and why no margin in
    /// that frame could have fixed it.</para>
    ///
    /// <para><b>THE FRAME IS THE ONE RECORD 19 ALREADY TRAVELS IN, WHICH CLOSES A SEAM THIS LINE
    /// USED TO PRINT AS A CAVEAT.</b> <c>Net.RemoteStorySync.TryToAnchor</c> expresses a dragged
    /// pose as the offset from <c>PanelLayout.TryGetAnchor</c> — the orbit focus plus the CACHED
    /// seat yaw — divided by <c>PanelLayout.WorldScale</c>. This anchor is built from exactly those
    /// three terms, so for the first time the spawn pose and the dragged pose are expressed in the
    /// same frame and in the same units: the frame-local numbers below are what a peer would
    /// receive for the same place, to the millimetre, and the window is built at the same size
    /// divisor the record already uses.</para>
    ///
    /// <para><b>WHERE "THE PLAY SURFACE" COMES FROM, honestly.</b> There is no measurement of the
    /// scenario diorama's rendered bounds anywhere in this mod — nothing walks the dungeon's
    /// renderers the way <c>PlayTray.MeasureBoardLocalExtents</c> walks the tray's. What the mod
    /// DOES already use, in four places, is <c>CameraController.FocusPoint</c>: the orbit focus,
    /// which <c>VRRigDriver</c> parks the rig at, which <c>PanelLayout</c> calls "table center" and
    /// measures every slot height from, which <c>Comfort</c> measures eye height from, and which
    /// <c>ModalFallback.TryGetBoardPlaneY</c> calls the board plane. Its Y is the plane the hexes
    /// lie in — NOT the top of what stands on them — and this file makes no attempt to pretend
    /// otherwise: the height below is measured from that plane and the falsifier prints the
    /// residual over <see cref="BoardTopClearanceMeters"/>, which is the mod's own standing estimate
    /// of how far the board's furniture reaches above it.</para>
    /// </summary>
    private static bool TrySharedAnchorOverBoard(UIWindow window, SharedWindowKind kind, int home,
        string stage, Vector2 halfSize, out Vector3 worldPos, out Quaternion worldRot,
        out float worldScale, out string line)
    {
        worldPos = Vector3.zero;
        worldRot = Quaternion.identity;
        worldScale = 1f;
        line = string.Empty;

        // THE GUARD IS THE ROOM AND NOT THE ANCHOR CALL. PanelLayout.TryGetAnchor has a dev-preview
        // fallback that seats things 1.5 m in front of Camera.main; requiring the rig, the camera
        // controller and a table in front of the player is what keeps this path on the branch that
        // returns FocusPoint + the cached seat yaw — the same three conditions TryGetBoardPlaneY
        // tests before it will call anything a board plane.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        CameraController controller = CameraController.s_CameraController;
        if (rig == null || controller == null || !Core.Events.VRModeStateMachine.TableInFrontOfPlayer)
            return false;
        if (!PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion seatYaw))
            return false;
        float scale = Mathf.Max(PanelLayout.WorldScale, 0.01f);

        // Real metres in the seat-anchor frame, which is what record 19 speaks. halfSize is world,
        // so it is divided by the SAME scale the record divides by — a window that is 1:1 in place
        // and not in size is not 1:1 (the map-table path's own words). AND IT IS THE SAME SCALE THE
        // CALLER MEASURED halfSize AT: PlaceAtHmd computes it as PanelWorldHalfSize(panel,
        // PanelLayout.WorldScale × extraScale) and then builds the host at whatever scale THIS
        // method returns, so the old tray-frame answer (6.32 against 9.57 in his log) was both
        // shrinking the shared window by a third against every other window in the scenario AND
        // feeding this arithmetic a half-height 1.51x larger than the window it would build.
        float halfWinXm = Mathf.Max(halfSize.x / scale, 0f);
        // THE HALF-HEIGHT FLOOR IS NARROWER HERE THAN ON THE TABLE PATH, AND ON PURPOSE. There it
        // guards a real hazard: that path's height is a BOTTOM-EDGE sum in which a degenerate rect
        // would seat the window ON the table. Here the bottom edge is pinned by the bar rule
        // whatever the rect says, so a floor can only push a SHORT window's bar ABOVE the number the
        // user tuned — 28 mm for the story box, which is exactly the kind of silent disagreement
        // between a dial and its effect this file keeps having to apologise for. So the floor
        // catches only a DEGENERATE rect (a panel measured before its content fit), and a real
        // measurement, however small, is used as measured.
        float trueHalfYm = halfSize.y / scale;
        float halfWinYm = trueHalfYm > 0.001f ? trueHalfYm : SharedAnchorMinHalfHeightMeters;

        // THE HEIGHT IS A GRAB-BAR HEIGHT, exactly as the map room's is (ModBuild 251, "die Höhe
        // soll beim Spawn am Besten bei allen Fenster gleich sein gemessen am Greifbalken!"). It is
        // the bar the user photographed lying among the figures, so the bar is the thing the number
        // must be about; the body hangs from it, so a taller window reaches higher and never dips.
        float barYm = ResolveScenarioBarHeightMeters(out string barRule);
        float bottomYm = barYm + GrabBarDropMeters;
        float centreYm = bottomYm + halfWinYm;

        // ModBuild 245's separation rule, unchanged and re-used verbatim: each window steps by its
        // OWN half-width plus half the gap, so a pair separates by exactly halfA + halfB + gap.
        // home is 0 for the only shared kind a scenario has; the other two exist so a second kind
        // never has to reinvent this.
        float step = halfWinXm + SharedAnchorLateralGapMeters * 0.5f;
        float lateral = home switch
        {
            1 => +step,
            2 => -step,
            _ => 0f,
        };

        // OVER THE FIELD'S CENTRE, at depth 0 in the seat frame: "über dem Spielfeld schwebt" read
        // literally. A depth term would be a second invented number, and the one thing the report
        // is about is the height.
        var localPos = new Vector3(lateral, centreYm, 0f);
        worldPos = anchor + seatYaw * (localPos * scale);
        // The seat yaw IS the facing: PanelLayout's own convention is that +Z points away from the
        // player across the table, and a uGUI canvas renders its front along −forward, so a window
        // yawed with the seat faces the reader. The same convention PanelPlacement.Facing and
        // ComputeHmdPose's head-facing branch use. Yaw only (ModBuild 189).
        worldRot = seatYaw;
        worldScale = scale;

        // THE FALSIFIER IS READ BACK OFF THE RESULT, not restated from the inputs. Both heights are
        // recomputed from worldPos and the TRUE halfSize, so if the half-height floor above changed
        // the answer, these numbers say so instead of agreeing with the intent.
        float bottomAboveM = (worldPos.y - halfSize.y - anchor.y) / scale;
        float barAboveM = bottomAboveM - GrabBarDropMeters;
        float overFurnitureM = barAboveM - BoardTopClearanceMeters;

        line = $"SHARED WINDOW ANCHOR APPLIED ({stage}) — '{window.name}' carries "
               + $"SharedWindowKind.{kind}, which owns HOME {home} over the play field BY ITS "
               + "IDENTITY and not by the order anything opened. FRAME=PLAY FIELD (the SEAT ANCHOR: "
               + "CameraController.FocusPoint plus the cached seat yaw, PanelLayout.TryGetAnchor — "
               + "THE SAME FRAME record 19 sends a dragged pose in, so spawn and drag finally agree "
               + $"on what 'the same place' means). Play surface (orbit-focus plane) y {anchor.y:F2} "
               + $"wu at {scale:F2} wu/m; seat yaw {seatYaw.eulerAngles.y:F2}°. FRAME-LOCAL POSE, IN "
               + "REAL METRES — THIS IS THE NUMBER THAT MUST BE IDENTICAL ON TWO CLIENTS: pos "
               + $"({localPos.x:F4},{localPos.y:F4},{localPos.z:F4}), yaw 0.00° seat-local. "
               + $"THE ARITHMETIC: bar {barYm:F3} + frame-to-bar drop {GrabBarDropMeters:F3} + own "
               + $"half-height {halfWinYm:F3} = centre {centreYm:F3} m above the play surface. "
               + "READ BACK OFF THE RESULT — IS IT ABOVE THE BOARD? bottom edge "
               + $"{bottomAboveM:+0.000;-0.000} m, GRAB BAR {barAboveM:+0.000;-0.000} m above the "
               + "play surface, and the bar stands "
               + $"{overFurnitureM:+0.000;-0.000} m clear of the board's own top edge (the mod's "
               + $"standing estimate of it, BoardTopClearanceMeters {BoardTopClearanceMeters:F2} m "
               + "above the orbit-focus plane). A NEGATIVE number in EITHER of those two is this "
               + "report — the bar down among the figures, spawn_scenario.jpg — and nothing else. "
               + $"{barRule}. Lateral step {step:F3} m, half-width {halfWinXm:F3} m. WORLD POSE, "
               + "WHICH MUST DIFFER BETWEEN CLIENTS AND IS NOT A FAULT WHEN IT DOES — every player "
               + "sits at his own seat and at his own zoom: "
               + $"({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}) wu, yaw "
               + $"{worldRot.eulerAngles.y:F2}°, scale {worldScale:F2}. NO WIRE FIELD WAS NEEDED. "
               + "THE DRAG STILL WINS — this is the initial spawn pose only.";
        return true;
    }

    /// <summary>
    /// THE SCENARIO SPAWN HEIGHT, resolved: how far above the play field's surface this window's
    /// GRAB BAR is seated, in real metres.
    ///
    /// <para>One dial, floored in code at <see cref="ScenarioWindowMinBarHeightMeters"/> so no value
    /// a player can type can put a bar into the scenery. <paramref name="rule"/> names which of the
    /// two branches decided, in the words the falsifier prints — a reader can always tell "the dial"
    /// from "the floor overruled the dial" without re-deriving anything.</para>
    /// </summary>
    private static float ResolveScenarioBarHeightMeters(out string rule)
    {
        float wanted = WorldUIConfig.ScenarioWindowBoardClearanceMeters != null
            ? WorldUIConfig.ScenarioWindowBoardClearanceMeters.Value
            : Defaults.ScenarioWindowBoardClearanceMeters;   // a spawn before Bind: ship the default
        if (wanted >= ScenarioWindowMinBarHeightMeters)
        {
            rule = "THE DIAL DECIDED IT: [WorldUI] ScenarioWindowBoardClearanceMeters = "
                   + $"{wanted:F3} m of grab-bar height above the play surface";
            return wanted;
        }

        rule = $"*** THE CODE FLOOR OVERRULED THE DIAL: {wanted:F3} m would put the bar at or under "
               + $"the board's own top edge ({BoardTopClearanceMeters:F2} m above the orbit-focus "
               + $"plane), so it is raised to {ScenarioWindowMinBarHeightMeters:F3} m. A window "
               + "standing IN the scenery is never traded away ***";
        return ScenarioWindowMinBarHeightMeters;
    }
}
