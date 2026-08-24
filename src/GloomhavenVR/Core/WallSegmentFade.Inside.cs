using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// BOARD VOLUME — an OBSERVATION of where the player's head is relative to the board, and a
/// RETIREMENT RECORD for the two policies that were built on it. Nothing in this file gates a
/// fade any more. Read the retirement record before proposing a third one.
///
/// <para>WHAT WAS TRIED, AND WHAT THE HARDWARE SAID.
/// <list type="number">
/// <item>ModBuild 241-250 — A RAISED BAR. While the head read INSIDE, the coverage pair
///   0.98/0.90 was substituted for the live bars. It was inert: <c>BlockedFraction</c>'s
///   head-inside-AABB shortcut returned a hard <c>1f</c> from OPEN FLOOR, because the AABB it
///   tested was the segment's union box, and 1.00 clears 0.98 exactly as easily as it clears
///   0.10. 75 diag samples in the ModBuild 250 log carry that signature.</item>
/// <item>ModBuild 251 — A HARD STAND-DOWN. While the head read INSIDE, this client's own
///   occlusion decision was forced OFF for every wall at once, with a mesh-keyed carve-out. It
///   worked exactly as designed and the user rejected it in one build:
///   <i>"Seit deiner letzten Änderung gibt es in der Map nur noch zwei Stati: Entweder alle
///   grünen Wände verschwinden auf einmal, oder alle sind da. Ich will aber das jede Wand
///   einzeln verschwinden kann und andere bleiben."</i></item>
/// </list></para>
///
/// <para>WHY BOTH FAILED — ONE CAUSE, TWO SYMPTOMS. A scene-wide switch cannot express a
/// per-wall fact, and the trigger was never measuring what it claimed to. In the ModBuild 251
/// log the verdict flips YES/no four times in one session, and the deciding term is
/// <c>FOOTPRINT</c> as often as <c>HEIGHT</c> (27 against 26):
/// <c>INSIDE THE MAP: YES — head (0.1, 2.78, 12.4) … deciding term FOOTPRINT (the head is over
/// the board) … 0.34 m above the floor plane, walls 0.61 m tall</c>. Those are real metres: the
/// board is a diorama with 61 cm walls and his eyes are a third of a metre above its floor
/// plane. "INSIDE THE MAP" was firing when he leaned over his own table. Each rising edge then
/// ran a release across ALL segments and each pass forced them ALL solid —
/// <c>WALLS FORCED SOLID (INSIDE): 6 wall(s) held … 6 wall(s) were released on entry</c> — which
/// is exactly the "alle auf einmal" he reported, and is also a prime suspect for the un-animated
/// return he reported in the same breath.</para>
///
/// <para>WHAT REPLACES IT: NOTHING. The per-wall coverage metric is the whole decision, which is
/// what the user asked for in his own words — <i>"Es sollte anhand der verdeckten Boden-tiles des
/// jeweiligen Raumes berechnet werden, oder?"</i> — and what the subsystem was always designed to
/// do. The narrow case the stand-down existed to protect (his head genuinely inside masonry) is
/// already handled PER WALL by the ordinary ray test: a ray whose origin is inside a piece's
/// bounds intersects it at distance 0, so a wall the head is buried in blocks exactly the
/// samples it really covers and dissolves on its own coverage. ModBuild 255 deleted the hard
/// <c>1f</c> shortcut that used to assert this instead of measuring it —
/// <see cref="FadeDriver.HeadInsideWallMesh"/> survives only as the observation printed on the
/// PER-WALL line. A global rule was never needed for any of it.</para>
///
/// <para>THE STANDING RULE FOR ANYONE READING THIS LATER: this subsystem may not acquire a
/// scene-wide fade switch. If a future symptom seems to want one, it is a per-wall measurement
/// that is wrong. Two builds have now been spent proving that.</para>
///
/// <para>WHAT SURVIVES, AND WHY. The board volume and the INSIDE verdict are kept as a pure
/// DIAGNOSTIC. Their commit phase is already priced in the BUDGET line, the test is ~15 float
/// ops, and "where was his head relative to the board, in real metres" is the single question
/// that would have caught both failures above in round one — the metre column is what shows a
/// 0.61 m wall and a head 0.34 m up. The <c>INSIDE THE MAP</c> line says explicitly that it
/// gates nothing, so it can never again be mistaken for a policy.</para>
///
/// <para>THE TEST ITSELF (unchanged, diagnostic only). INSIDE means the head is inside the BOARD
/// VOLUME — the union XZ footprint of every decision-valid room and its walls, from the board's
/// floor plane up to the median WALL CREST. The measured quantity is the signed distance of the
/// head to that box (negative inside), through a Schmitt pair expressed as fractions of the
/// crest height C so they scale with the board: enter at <c>sd &lt;= -0.10·C</c>, leave at
/// <c>sd &gt;= +0.35·C</c>, dwells 0.20 s in and <see cref="WallFadeTuning.DwellMoved"/> out.
/// The median (not the max) crest keeps a keep's stacked superstructure from putting the crest
/// plane above every wall in the scenario. The 3D map room has no occlusion volumes at all, so
/// the volume is never valid there.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // --- INSIDE-THE-MAP constants (see class header) -------------------------------------
        // Both bars are FRACTIONS OF THE CREST HEIGHT C (the board's own median wall height in
        // world units), never absolute distances: the board is the yardstick, so the boundary
        // means the same thing on a low ruin and on a keep.
        /// <summary>Schmitt high bar: enter INSIDE when the head is at least this fraction of
        /// the crest height INSIDE the board volume (signed distance is negative there).</summary>
        private const float InsideEnterDepthFraction = 0.10f;
        /// <summary>Schmitt low bar: leave INSIDE only once the head is this fraction of the
        /// crest height OUTSIDE the volume — a 0.45·C band the head must cross to flip back.</summary>
        private const float InsideExitDepthFraction = 0.35f;
        /// <summary>How many walls the per-wall falsifier names individually before it starts
        /// counting the rest. The whole point of that line is to show the walls DISAGREEING, so
        /// it has to name enough of them to see a split.</summary>
        private const int PerWallNameCap = 8;
        /// <summary>Floor under the derived crest height, so a degenerate or half-generated
        /// board can never produce a zero-height volume that the head is trivially outside.</summary>
        private const float BoardCrestMinWU = 0.5f;
        /// <summary>Cadence of the falsifier line while INSIDE holds. Edges print unthrottled.</summary>
        private const float InsideLogIntervalSeconds = 2f;

        // --- cached board volume (rebuilt once per rescan, read O(1) per frame) --------------
        /// <summary>The board's own volume: union XZ footprint of every decision-valid room and
        /// its walls, Y from the board floor plane to the wall crest. Valid only when
        /// <see cref="_boardVolumeValid"/>.</summary>
        private Bounds _boardVolume;
        private bool _boardVolumeValid;
        /// <summary>Crest height above the floor plane (world units) — the yardstick both
        /// boundary bars are expressed in.</summary>
        private float _boardCrestWU;
        /// <summary>Board floor plane (world units), the minimum over decision-valid rooms.</summary>
        private float _boardFloorY;
        private int _boardVolumeRooms;
        private int _boardVolumeWalls;
        /// <summary>Per-segment crest samples; a field so the commit phase allocates nothing.</summary>
        private readonly List<float> _crestScratch = new();
        // Announce-once signature (rooms, walls, and the volume quantized to 0.1 wu) so the
        // BOARD VOLUME line prints on a real change — a reveal, a fresh scenario — and not
        // every two seconds forever.
        private int _bvSigRooms = -1, _bvSigWalls = -1, _bvSigCrest, _bvSigX, _bvSigZ;

        // --- INSIDE state ------------------------------------------------------------------
        private bool _insideBoard;
        private bool _insidePending;
        private float _insidePendingSince;
        /// <summary>Last signed distance of the head to the board volume (wu, negative inside).</summary>
        private float _lastInsideSigned;
        /// <summary>Last per-axis slabs of that distance — which TERM decided the verdict.</summary>
        private float _lastInsideMarginY;
        private float _lastInsideMarginXZ;
        private float _nextInsideLogTime;

        // --- R2 PER-WALL INDEPENDENCE CENSUS (measured per pass, never intended) ------------
        /// <summary>Fadeable walls judged this pass, and how many of them ended up faded. The
        /// whole point of the falsifier is that these two disagree: 0 or N means every wall
        /// reached the same verdict, anything between them is the independence the user asked
        /// for, demonstrated rather than asserted.</summary>
        private int _pwTotal;
        private int _pwFaded;
        /// <summary>Per-wall detail, in the order the segment table yields it: name, own room,
        /// own smoothed coverage, own verdict. This is the line that answers "jede Wand
        /// einzeln".</summary>
        private readonly List<string> _pwNames = new();
        /// <summary>Session tallies: passes where the walls DISAGREED versus passes where they
        /// were unanimous. A session with zero mixed passes is the defect he reported.</summary>
        private int _pwMixedPasses;
        private int _pwUniformPasses;
        /// <summary>Widest and narrowest smoothed coverage seen this pass, with names — the
        /// spread that proves the walls are being measured separately.</summary>
        private float _pwMaxSmooth, _pwMinSmooth;
        private string _pwMaxWall = "-", _pwMinWall = "-";
        /// <summary>Walls whose fade came from somewhere other than their own decision.</summary>
        private int _pwPeerDriven, _pwGateDriven;
        private float _nextPerWallLogTime;
        /// <summary>Split-run members named on the PER-WALL line under the run that decides them
        /// (ModBuild 259). Their own coverage is still measured and still printed; what they no
        /// longer do is count as independent walls in "faded X of Y".</summary>
        private readonly List<string> _pwRunPieceNames = new();
        private const int PerWallRunPieceCap = 4;

        // --- R1 ANIMATION-PATH CENSUS ------------------------------------------------------
        /// <summary>Wall renderers mid-DISSOLVE on the noise map — the only path that produces
        /// intermediate pixels — versus those sitting in the HELD state on the occluded map,
        /// where the noise term is multiplied by zero and the result is binary. The residual
        /// discontinuity in the subsystem is the boundary between the two at Fade == 1.</summary>
        private int _animSmooth;
        private int _animStepped;
        private readonly List<string> _animSteppedNames = new();

        // --- R1 FOLIAGE CHANNEL CENSUS -----------------------------------------------------
        /// <summary>THE BLIND SPOT THIS ROUND CLOSED. The ModBuild 253 ANIMATION line read
        /// "0 on the stepped two-texture path: every transition in flight is animated end to
        /// end" while the user was watching walls pop. It was true and it was not the picture:
        /// it counted only the WALL renderers, and the DISSOLVE CENSUS it sat next to has no
        /// foliage bucket either. 345 foliage attachments — the visible mass of every scrub
        /// wall — were animated by nobody's measurement. These count them by the channel each
        /// piece actually got.</summary>
        private int _folNative, _folSwapped, _folOwnChannel, _folNoChannel;
        private readonly List<string> _folNoChannelNames = new();

        // --- R1 STEP CENSUS: discontinuities BOLTED ONTO the ramp ---------------------------
        /// <summary>
        /// WHY A SECOND ANIMATION INSTRUMENT. The ANIMATION line measures the SLOPE — it proves
        /// every fade in flight is moving through intermediate values. It cannot see a step
        /// applied on the ramp's first or last frame, and in ModBuild 254 it read
        /// "every foliage piece in flight is animated, not switched" while the user was watching
        /// walls pop. A symmetric ramp cannot produce an asymmetric artifact, so the pop had to
        /// be an EDGE, and no instrument could see edges. These count them, by which end of the
        /// transition they land on.
        ///
        /// <para>OUT-EDGE events happen as the fade leaves 0 — while the wall still looks solid,
        /// so anything applied here is maximally visible. IN-EDGE events happen as it reaches 0,
        /// when the wall is already solid and nobody can see them. A build where OUT is non-zero
        /// and IN is non-zero for the same cause is the asymmetry the user reported.</para>
        /// </summary>
        private int _stepOutBlockInstalled, _stepInBlockCleared;
        private int _stepOutSwapInstalled, _stepInSwapRemoved;
        private readonly List<string> _stepOutNames = new();

        // --- R2 NUMERATOR ADMISSION CENSUS -------------------------------------------------
        /// <summary>Per wall: how many of its pieces the standing test admitted to the occlusion
        /// numerator and how many it excluded as ground dressing, with the widest excluded piece
        /// named. This is what settles "which renderer inflated Wall 2" without another round.
        /// </summary>
        private readonly List<string> _admitNames = new();
        /// <summary>Which wall's masonry currently contains the head, if any — an observation
        /// since ModBuild 255, when the hard-1f shortcut it used to feed was deleted.</summary>
        private string _headInMasonryWall = "-";
        /// <summary>Floor-grid cell count of the room the censused walls were judged against —
        /// the denominator whose reciprocal is the smallest coverage difference the metric can
        /// express at all.</summary>
        private int _pwCells;
        /// <summary>Head position of the pass being censused, so the observation above is taken
        /// against the same pose the verdicts were.</summary>
        private Vector3 _lastHeadPos;

        // --- SPLIT RUNS: one wall, one verdict (ModBuild 259) --------------------------------
        /// <summary>
        /// THE DECISION UNIT OF A SPLIT WALL. <see cref="NeutralizeEngulfingSegments"/> carves a
        /// room-engulfing wall run into one segment per renderer so that its union AABB — the box
        /// that CONTAINS the room's own floor samples — can never be the occluder box. That fixed
        /// the ground defect and created this one: ~40 pieces of ONE wall each ran their own
        /// Schmitt trigger, so the wall stopped being a thing that appears or disappears.
        ///
        /// <para>THE USER'S RULING (2026-08-24): <i>"Entweder verschwindet die ganze Wand mit
        /// ALLEM was dazu gehört (Bäume, Gestrüp, etc.) oder sie ist vollständig da. So ein
        /// Zwischending soll es nicht geben."</i> Both of his rulings hold at once: walls decide
        /// INDEPENDENTLY of each other (2026-08-24, ModBuild 252) and a wall takes everything
        /// belonging to it with it. A run is one wall; its pieces are not other walls.</para>
        ///
        /// <para>WHAT THE HARDWARE SAID. ModBuild 258 log, last diag: <c>'Wall 2' blk16/16 ON</c>
        /// beside <c>'FR_Pillar_Tree_Trunk_01' blk2/16 off</c> and <c>'FR_Tree_01 (1)' blk2/16
        /// off</c>. Session fade-ON counts: the four unsplit walls switch 7 times BETWEEN THEM
        /// ('Wall 4' 3, 'Wall 3' 3, 'Wall 2' 1) while individual trunks, stumps and verges switch
        /// 69. The heartbeat names the mechanism outright — <c>171 from the wall cache + 4 ADOPTED
        /// by shader … 1 room-engulfing wall(s) split per renderer</c>: only FOUR renderers in the
        /// whole session were adopted, so this population is the SPLIT, not the adoption sweep.
        /// </para>
        ///
        /// <para>WHAT THIS CHANGES AND WHAT IT DOES NOT. Every piece still measures its own
        /// coverage against its own renderer — <c>BlockedFraction</c> is called exactly as before,
        /// no renderer moves between lists, no numerator is re-based. What changes is the number
        /// the Schmitt trigger reads: the RUN's coverage is the UNION of its members' blocked
        /// cells over the room grid, which is precisely what the unsplit wall's mesh-accurate
        /// narrow phase (<see cref="RayHitsWallMesh"/>, per-renderer AABBs) would have produced —
        /// minus the <c>Contains()</c> claims the engulfing union box used to make. Strictly the
        /// same metric, strictly the same bars, one decider instead of forty.</para>
        ///
        /// <para>THE MAX OVER ROOMS is not a new rule: it is the seam-wall precedent already in
        /// <see cref="BlockedFraction"/> — a run bordering two rooms fades from either side.</para>
        ///
        /// <para>ONE-WAY BY CONSTRUCTION. A run reads its members' cells; a member reads its run's
        /// state. An unsplit <c>ProceduralWall</c> has no <see cref="Segment.RunOwner"/>, is never
        /// visited by <see cref="EvaluateSplitRuns"/>, and cannot be reached by any piece of any
        /// run. 'Wall 4' — which the user confirmed correct this round — is in that class.</para>
        /// </summary>
        private sealed class WallRun
        {
            public Component? Anchor;
            public float Smooth;
            public bool SmoothInit;
            public bool PendingRaw;
            public float PendingSince;
            public bool State;
            /// <summary>Pieces that contributed a measurement this pass (fail-safe-held members
            /// are excluded — they are not deciders and are named by the leftover audit).</summary>
            public int Deciders;
            /// <summary>Pieces carrying this run's key at all, deciders or not.</summary>
            public int Members;
            /// <summary>ModBuild 261: of the non-deciders, the ones that own NO renderer because
            /// the wall choke point refused them (<see cref="Segment.GeometryRefusedWhy"/>). They
            /// are NOT "held solid by a fail-safe" — they were never wall geometry, and calling
            /// them fail-safe-held is what pointed ModBuild 260's report at the wrong rule.</summary>
            public int Refused;
            /// <summary>ModBuild 261: members recruited by <c>SplitRunAdoptGroundScenery</c>.
            /// They ride the verdict and never contribute a cell to it — see
            /// <see cref="Segment.RunPassenger"/>.</summary>
            public int Passengers;
            /// <summary>Union size, room total and room index of the winning room.</summary>
            public int Blocked;
            public int Total;
            public int Room = -1;
            /// <summary>Widest single-member reading this pass — the number that proves the union
            /// is doing work: a run whose union equals its best member gained nothing.</summary>
            public int BestMember;
            public string BestMemberName = "-";
            /// <summary>Evaluation generation this run was last seen in (pruning).</summary>
            public int Seen = -1;
            /// <summary>ModBuild 261 IMMEDIACY AUDIT (user ruling 2026-08-24: "direkt wieder
            /// unfaded wenn es keine spielbaren tiles verdeckt"). Wall-clock this run has spent
            /// FADED with its UNION at or above the exit bar while its widest single member is
            /// already BELOW it — i.e. time the union alone is holding the wall down. Continuous
            /// stretch and session worst. Raw coverage on both sides, deliberately not the EMA:
            /// the members have no run-comparable EMA, and an unmatched pair would be a
            /// comparison of two different filters. Zero here means the union costs nothing on
            /// the exit and the delay is entirely bar + dwell.</summary>
            public float UnionHoldSeconds;
            public float UnionHoldWorst;
            /// <summary>Last evaluation time, for the dt this audit integrates.</summary>
            public float LastEval;
            /// <summary>Highest fade any member has reached — the audit's "is this run actually
            /// gone from the picture yet", so a sweep landing mid-ramp reports nothing.</summary>
            public float MaxFade;
            /// <summary>Blocked cells, packed as (room &lt;&lt; 20 | cell) so one set spans every
            /// room a seam run borders without a second dictionary.</summary>
            public readonly HashSet<long> Cells = new();
            /// <summary>Distinct blocked cells per room — the per-room numerators.</summary>
            public readonly Dictionary<int, int> RoomHits = new();
        }

        private readonly Dictionary<Component, WallRun> _runs = new();
        private readonly List<Component> _runDeadKeys = new();
        private int _runGeneration;
        /// <summary>Split pieces whose run anchor is gone (Apparance destroyed the wall between
        /// rescans). They fall back to their OWN decision — never to a latch — and are counted
        /// here so an unexpectedly large orphan population is visible instead of silent.</summary>
        private int _runOrphans;
        private int _runsTotal, _runsFaded, _runMembersTotal, _runMembersHeld;
        /// <summary>ModBuild 261: the refused subset of <see cref="_runMembersHeld"/> — pieces
        /// that own no renderer at all. See <see cref="WallRun.Refused"/>.</summary>
        private int _runMembersRefused;
        /// <summary>ModBuild 261: members recruited by <c>SplitRunAdoptGroundScenery</c> this
        /// pass — zero while the dial is off, which is how it ships.</summary>
        private int _runMembersPassenger;
        /// <summary>ModBuild 261 immediacy audit, scene-level: the widest run's live union
        /// numerator/denominator and its widest single member, sampled EVERY evaluation so the
        /// 2 s census carries the pair instead of only the edges carrying it. Plus the current
        /// and session-worst seconds a union alone has held a faded run down (WallRun).</summary>
        private int _runUnionBlocked, _runUnionTotal, _runUnionBest;
        private float _runUnionHoldNow, _runUnionHoldWorst;
        private float _nextRunLogTime;
        /// <summary>Session tally of run-level fade edges — the counterpart of the per-piece
        /// `fade ON` census that showed 69 switches across 12 pieces of ONE wall.</summary>
        private int _runFadeEdges;

        /// <summary>The state a run-driven member must take. Falls back to the member's own last
        /// state if its run vanished between the pre-pass and the loop (impossible in one frame,
        /// but this may not be the thing that latches a wall).</summary>
        private bool RunStateOf(Segment seg)
        {
            if (seg.RunOwner != null && _runs.TryGetValue(seg.RunOwner, out WallRun? run))
                return run.State;
            return seg.State;
        }

        /// <summary>The run's smoothed coverage — the live number that decides a run-driven
        /// member, and therefore the one the latch watchdog must judge it against.</summary>
        private float RunSmoothOf(Segment seg) =>
            seg.RunOwner != null && _runs.TryGetValue(seg.RunOwner, out WallRun? run)
                ? run.Smooth
                : seg.Smooth;

        /// <summary>Dial turned off mid-session: hand every piece back its own decision and drop
        /// the run table, so nothing keeps driving a member from a stale verdict.</summary>
        private void ClearSplitRunDrive()
        {
            if (_runs.Count > 0)
                _runs.Clear();
            _runsTotal = 0;
            _runsFaded = 0;
            _runMembersTotal = 0;
            _runMembersHeld = 0;
            _runMembersRefused = 0;
            _runMembersPassenger = 0;
            _runOrphans = 0;
            _runLeftover = 0;
            _runLeftoverNames.Clear();
            _runLeftoverByReason.Clear();
            _runLeftoverByClass.Clear();
            _runLeftoverAllowed.Clear();
            foreach (Segment seg in _segments.Values)
                seg.RunDriven = false;
        }

        /// <summary>
        /// Measure every split-run member, union their blocked cells per run, and drive ONE
        /// Schmitt trigger + dwell per run. Called from the tick before the decision loop, on
        /// evaluation frames only. Allocation-free after warm-up: the sets and the per-run
        /// dictionaries keep their capacity.
        /// </summary>
        private void EvaluateSplitRuns(Vector3 headPos, float now, float fracStep,
            float onFraction, float offFraction, float exitDwell)
        {
            _runGeneration++;
            _runOrphans = 0;
            _runMembersTotal = 0;
            _runMembersHeld = 0;
            _runMembersRefused = 0;
            _runMembersPassenger = 0;

            // ---- pass 1: measure each piece, union its cells into its run ------------------
            foreach (Segment seg in _segments.Values)
            {
                seg.RunDriven = false;
                if (!seg.FromSplitRun)
                    continue;
                // The run must still BE a split run right now — `_splitAnchors` is the live
                // register and it is cleared on a scene load. A stale RunOwner from a group that
                // has since been re-merged must not keep driving anything.
                if (seg.RunOwner == null || !_splitAnchors.Contains(seg.RunOwner))
                {
                    // ORPHAN: the run this piece was carved from is destroyed. Keep its own
                    // decision (the pre-259 behaviour) rather than holding it solid — a piece
                    // frozen solid in front of the board is the very complaint, and a piece
                    // frozen faded would delete geometry with no owner to bring it back.
                    _runOrphans++;
                    continue;
                }
                _runMembersTotal++;
                if (!_runs.TryGetValue(seg.RunOwner, out WallRun? run))
                {
                    run = new WallRun { Anchor = seg.RunOwner };
                    _runs.Add(seg.RunOwner, run);
                }
                if (run.Seen != _runGeneration)
                {
                    run.Seen = _runGeneration;
                    run.Cells.Clear();
                    run.RoomHits.Clear();
                    run.Deciders = 0;
                    run.Members = 0;
                    run.Refused = 0;
                    run.Passengers = 0;
                    run.BestMember = 0;
                    run.BestMemberName = "-";
                }
                run.Members++;
                // The fail-safe branches of the decision loop own these pieces (boundless,
                // unsplittable-engulfing, doorway, room without a valid floor grid). They are
                // held SOLID by rules older than this one and must not vote — but a solid piece
                // beside a faded run IS a leftover, so the audit below names every one of them.
                if (!seg.HasBounds || seg.Engulfing || seg.DoorRoot != null
                    || !RoomDecisionValid(seg.RoomIndex))
                {
                    _runMembersHeld++;
                    // ModBuild 261: split the held population at its real seam. A piece the wall
                    // choke point REFUSED owns no renderer, so it is not a wall being held solid
                    // — it is a floor prop that never entered the wall path. Counting the two
                    // together is why ModBuild 260's report read "99 held by an older fail-safe".
                    if (seg.GeometryRefusedWhy != null)
                    {
                        _runMembersRefused++;
                        run.Refused++;
                    }
                    continue;
                }
                // PASSENGER (ModBuild 261, [WallFade] SplitRunAdoptGroundScenery). Recruited
                // ground scenery takes the run's verdict — that is the whole point of recruiting
                // it — but it must never steer one: a bush standing a metre inside the room would
                // otherwise hide floor it has no business voting on, and the trigger would have
                // moved in the same build as the population. RunDriven, deliberately NOT a
                // Decider, and no cells into the union. Falsified by: RUN FADE union/best-single
                // numbers that differ between the two dial positions on the same scenario.
                if (seg.RunPassenger)
                {
                    run.Passengers++;
                    _runMembersPassenger++;
                    seg.RunDriven = true;
                    continue;
                }
                float fraction = BlockedFraction(seg, headPos);
                seg.LastRaw = fraction;
                // The piece's OWN EMA keeps running so the diag and the PER-WALL line still
                // report what each piece measures — the numbers that made this change are
                // exactly these, and they must stay readable after it.
                if (!seg.SmoothInit)
                {
                    seg.SmoothInit = true;
                    seg.Smooth = fraction;
                }
                else
                {
                    seg.Smooth += (fraction - seg.Smooth) * fracStep;
                }
                run.Deciders++;
                seg.RunDriven = true;
                // THE CELLS BELONG TO seg.RoomIndex, NOT TO seg.LastDecidingRoom. BlockedFraction
                // attributes cells during the OWN-room pass only (`_attributeCells = false` around
                // every alt-room pass), so for a seam piece whose ALT room won the max,
                // LastDecidingRoom names the alt room while LastBlockedCells still holds the own
                // room's indices. Unioning them under the alt room's key would mix two point sets.
                //
                // WHAT THIS COSTS, STATED: a run member's alt-room coverage does not reach the
                // union. It is not lost to the run as a whole — members standing on the other side
                // of a seam carry that room in their OWN RoomIndex and the per-room MAX below picks
                // it up — but a single piece that fades "from the other side" contributes nothing.
                // Fixing that properly means per-alt-room cell attribution in BlockedFraction,
                // which is a change to the shared metric and not this round's.
                int room = seg.RoomIndex;
                if (room < 0)
                    continue;
                for (int i = 0; i < seg.LastBlockedCells.Count; i++)
                {
                    long key = ((long)room << 20) | (uint)seg.LastBlockedCells[i];
                    if (!run.Cells.Add(key))
                        continue;
                    run.RoomHits.TryGetValue(room, out int hits);
                    run.RoomHits[room] = hits + 1;
                }
                // Comparable to the union by construction: both count OWN-room attributed cells.
                // seg.LastBlocked would not be — for a seam piece it is the alt room's count.
                if (seg.LastBlockedCells.Count > run.BestMember)
                {
                    run.BestMember = seg.LastBlockedCells.Count;
                    run.BestMemberName = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                }
            }

            // ---- pass 2: one verdict per run ----------------------------------------------
            _runsTotal = 0;
            _runsFaded = 0;
            _runUnionBlocked = 0;
            _runUnionTotal = 0;
            _runUnionBest = 0;
            _runUnionHoldNow = 0f; // per pass; the WORST is a session figure and never resets
            _runDeadKeys.Clear();
            foreach (KeyValuePair<Component, WallRun> kv in _runs)
            {
                WallRun run = kv.Value;
                if (run.Seen != _runGeneration)
                {
                    _runDeadKeys.Add(kv.Key); // no live member carries this key any more
                    continue;
                }
                // MAX over the rooms this run borders — the seam-wall precedent from
                // BlockedFraction, not a new rule: whichever room the run is currently hiding
                // is the room the player wants opened.
                float fraction = 0f;
                run.Blocked = 0;
                run.Total = 0;
                run.Room = -1;
                foreach (KeyValuePair<int, int> hit in run.RoomHits)
                {
                    if (hit.Key < 0 || hit.Key >= _roomSampleCount.Count)
                        continue;
                    int total = _roomSampleCount[hit.Key];
                    if (total <= 0)
                        continue;
                    float f = hit.Value / (float)total;
                    if (run.Room >= 0 && f <= fraction)
                        continue;
                    fraction = f;
                    run.Blocked = hit.Value;
                    run.Total = total;
                    run.Room = hit.Key;
                }
                if (!run.SmoothInit)
                {
                    run.SmoothInit = true;
                    run.Smooth = fraction;
                }
                else
                {
                    run.Smooth += (fraction - run.Smooth) * fracStep;
                }
                // ---- IMMEDIACY AUDIT (see WallRun.UnionHoldSeconds) -------------------------
                // Does the UNION delay the un-fade? The RUN FADE line answers this only AT an
                // edge, where the union has already dropped under the bar by construction — so
                // it can never show the delay it is being asked about. Integrated here instead,
                // every evaluation, and reported on the 2 s census.
                float bestRaw = run.Total > 0 ? run.BestMember / (float)run.Total : 0f;
                float dtRun = run.LastEval > 0f ? Mathf.Min(now - run.LastEval, 0.5f) : 0f;
                run.LastEval = now;
                if (run.State && fraction >= offFraction && bestRaw < offFraction)
                {
                    run.UnionHoldSeconds += dtRun;
                    if (run.UnionHoldSeconds > run.UnionHoldWorst)
                        run.UnionHoldWorst = run.UnionHoldSeconds;
                }
                else
                {
                    run.UnionHoldSeconds = 0f;
                }
                if (run.Blocked > _runUnionBlocked || run.Total != _runUnionTotal)
                {
                    _runUnionBlocked = run.Blocked;
                    _runUnionTotal = run.Total;
                    _runUnionBest = run.BestMember;
                }
                if (run.UnionHoldSeconds > _runUnionHoldNow)
                    _runUnionHoldNow = run.UnionHoldSeconds;
                if (run.UnionHoldWorst > _runUnionHoldWorst)
                    _runUnionHoldWorst = run.UnionHoldWorst;
                bool raw = run.Smooth >= (run.State ? offFraction : onFraction);
                if (raw != run.PendingRaw)
                {
                    run.PendingRaw = raw;
                    run.PendingSince = now;
                }
                if (run.PendingRaw != run.State)
                {
                    float dwell = run.PendingRaw ? EnterDwellSeconds : exitDwell;
                    if (now - run.PendingSince >= dwell)
                    {
                        run.State = run.PendingRaw;
                        _runFadeEdges++;
                        LogRunFadeEdge(run);
                    }
                }
                _runsTotal++;
                if (run.State)
                    _runsFaded++;
            }
            for (int i = 0; i < _runDeadKeys.Count; i++)
                _runs.Remove(_runDeadKeys[i]);
            _runDeadKeys.Clear();
        }

        /// <summary>
        /// ONE line per RUN edge, replacing the ~40 per-piece `fade ON` lines the same event used
        /// to emit. Deliberately shaped so the next log answers the ModBuild 258 question — "how
        /// many things switched, and were they one wall or forty" — in a single grep.
        /// </summary>
        private void LogRunFadeEdge(WallRun run)
        {
            string wall = run.Anchor != null ? run.Anchor.name : "<dead>";
            VRLog.Info(Name,
                $"RUN FADE {(run.State ? "ON" : "OFF")} '{wall}' — {run.Members} piece(s) of ONE "
                + $"split wall run move together ({run.Deciders} of them measured, "
                + $"{run.Passengers} riding as PASSENGERS (recruited ground scenery — they take "
                + "this verdict and never vote on it), "
                + $"{run.Refused} REFUSED as wall geometry and owning no renderer at all, "
                + $"{run.Members - run.Deciders - run.Refused - run.Passengers} held solid by an "
                + "older fail-safe WITH geometry — ModBuild 261 split those apart, because 260 "
                + "reported them as one number and that pointed the whole round at the boundless "
                + "fail-safe instead of at the choke point). Run coverage "
                + $"{run.Blocked}/{run.Total} cell(s) of room {run.Room} = "
                + (run.Total > 0 ? (run.Blocked / (float)run.Total).ToString("F2") : "n/a")
                + $" (ema {run.Smooth:F2}) against bars {WallFadeTuning.On:F2}/"
                + $"{WallFadeTuning.Off:F2}; the widest SINGLE piece read {run.BestMember}/"
                + $"{run.Total} ('{run.BestMemberName}') — the gap between those two numbers is "
                + "exactly what the union bought, and a run where they are equal gained nothing. "
                + $"Session run edges: {_runFadeEdges}. Before ModBuild 259 this event was "
                + "up to one edge PER PIECE, which is the 69-switch tree census of the 258 log.");
        }

        /// <summary>
        /// THE FALSIFIER, READ OFF THE RENDERERS (requirement of this round; the ModBuild 252
        /// mistake was an instrument that watched the driver). For every run at full fade, ask
        /// each of its members' renderers — through <see cref="IsActuallyDrawing"/>, the same
        /// predicate the LEFTOVER audit already uses — whether it is still putting pixels on the
        /// screen while the run is gone. A non-empty list IS the photograph
        /// (neues_wandproblem.jpg), and each entry carries the REASON, because every remaining
        /// way to be left standing is a named older rule rather than this one.
        ///
        /// <para>Run at the mounted-dressing cadence (once per rescan) and reported on the SAME
        /// <c>LEFTOVER OVER A FADED WALL</c> line, so one grep still finds every leftover class.
        /// </para>
        ///
        /// <para><b>MODBUILD 261 — THE INSTRUMENT WAS BLIND TO THE POPULATION IT EXISTED FOR.</b>
        /// The ModBuild 260 log printed <c>SPLIT-RUN PIECES: 0</c> on all 122 sweeps while the
        /// same log's own RUN FADE line said 99 of 140 pieces of 'Wall 1' never move, and the
        /// video (wände_problem4.mp4) shows a standing hedge. Both were true: this sweep asked
        /// <c>seg.Renderers</c> and <c>seg.Foliage</c>, and a refused split piece has BOTH LISTS
        /// EMPTY — <see cref="RefreshSplitWall"/> adds the renderer only when
        /// <see cref="CollectWallFadeInfo"/> accepts it, and the nearest-piece foliage bind skips
        /// pieces without bounds. So the sweep polled exactly the 41 members that already work
        /// and answered honestly about them: the ModBuild 252 scar, one layer further in.</para>
        ///
        /// <para>THE FIX IS STRUCTURAL, NOT A THRESHOLD: a split piece's dictionary key AND its
        /// <c>Anchor</c> ARE its MeshRenderer (both split sites construct it that way), so the
        /// piece's geometry is reachable without any list. The sweep now asks the anchor whenever
        /// the lists are empty. A ZERO from this method now means the picture is clean; before it
        /// only meant the ledger was.</para>
        ///
        /// <para><b>MODBUILD 262 — TWO LISTS WAS STILL NOT THE SEGMENT.</b> The 261 sweep read
        /// <c>seg.Renderers</c>, <c>seg.Foliage</c> and (only when both were empty) the anchor,
        /// and it STOPPED AT THE FIRST DRAWING ENTRY — one verdict per segment, describing one
        /// renderer. A segment owns five more ledgers: <c>Mounted</c>, <c>Stacked</c>,
        /// <c>Body</c>, <c>UnitDressing</c> and <c>Siblings</c>, and the ModBuild 260 leftover
        /// population lives in the MOUNTED one. So the sweep answered honestly about at most one
        /// renderer of eight possible sources — the ModBuild 252 scar for the third time. It now
        /// enumerates every list the segment owns plus the anchor, deduplicated, and issues a
        /// class verdict for EVERY renderer that is drawing. <c>_runLeftover</c> therefore counts
        /// RENDERERS from this build on; <c>_runLeftoverSegments</c> keeps the old per-piece
        /// number so the two logs stay comparable.</para>
        /// </summary>
        private void SweepRunLeftovers()
        {
            _runLeftover = 0;
            _runLeftoverSegments = 0;
            _runLeftoverNames.Clear();
            _runLeftoverByReason.Clear();
            _runLeftoverByClass.Clear();
            _runLeftoverAllowed.Clear();
            if (_runs.Count == 0)
                return;
            // PASS 1: how far has each run actually got? The audit may only fire for a run some
            // member of which has COMPLETED its ramp — otherwise a sweep landing mid-dissolve
            // would report every piece of a perfectly healthy fade as a leftover, and the
            // headline claim of this round would be noise. Same >=0.99 predicate the mounted
            // leftover audit already uses for "this wall is gone".
            foreach (WallRun r in _runs.Values)
                r.MaxFade = 0f;
            foreach (Segment s in _segments.Values)
            {
                if (s.FromSplitRun && s.RunOwner != null
                    && _runs.TryGetValue(s.RunOwner, out WallRun? owner) && s.Fade > owner.MaxFade)
                    owner.MaxFade = s.Fade;
            }
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.FromSplitRun || seg.RunOwner == null)
                    continue;
                if (!_runs.TryGetValue(seg.RunOwner, out WallRun? run) || !run.State)
                    continue;
                if (run.MaxFade < FoliageHideFade)
                    continue; // the whole run is still mid-ramp — nothing has been left behind yet
                if (seg.Fade >= FoliageHideFade)
                    continue; // this piece went with its run — nothing to report
                // EVERY LIST THE SEGMENT OWNS (ModBuild 262 — see the method doc), plus the
                // anchor, which is the whole piece for a choke-point refusal that owns nothing.
                // Deduplicated because a renderer can legitimately sit in two ledgers across a
                // rescan boundary, and a double-count here would inflate the very number the
                // user's verdict is read off.
                CollectSegmentLeftoverCandidates(seg);
                bool anyDrawing = false;
                for (int ci = 0; ci < _leftoverCandidates.Count; ci++)
                {
                    Renderer shown = _leftoverCandidates[ci];
                    if (!IsActuallyDrawing(shown))
                        continue;
                    anyDrawing = true;
                    _runLeftover++;
                    // ORDER IS THE DIAGNOSIS. The refusal is asked FIRST because boundless is its
                    // CONSEQUENCE — a refused renderer is never collected, so there is nothing to
                    // build an AABB from, and no room association either. ModBuild 260 asked
                    // HasBounds first and therefore reported the symptom as the cause for every
                    // one of these pieces. Falsified by: a piece printing the refusal reason that
                    // the STANDING PROP census does not also name.
                    //
                    // TWO STRINGS, ON PURPOSE. `tag` is the tally KEY and must be short and free
                    // of any per-piece number — a fade value in a key turns one class of 99 into
                    // 99 classes of one, and the distribution is the whole point of the line.
                    // `why` is the long form for the (capped) names list.
                    string tag, why;
                    if (seg.GeometryRefusedWhy != null)
                    {
                        tag = ReferenceEquals(seg.GeometryRefusedWhy, SplitPieceFigureRefusalReason)
                            ? "REFUSED as wall geometry (FIGURE arm, never relaxed)"
                            : "REFUSED as wall geometry (floor-standing prop)";
                        why = seg.GeometryRefusedWhy;
                    }
                    else if (!seg.HasBounds)
                    {
                        // ModBuild 262: 261 asserted here that such a piece "owns renderers or
                        // foliage", which was a guess the code could not make — the piece may be
                        // drawing out of any of the six ledgers this sweep now reads, or be the
                        // bare anchor. State what is known and nothing else.
                        tag = "no decision AABB, and NOT a choke-point refusal";
                        why = "no decision AABB — boundless fail-safe (round 14) with NO recorded "
                              + "choke-point refusal. Since ModBuild 262 the third split-run "
                              + "creation site (AdoptShaderMatchedWalls) records its refusals too, "
                              + "so a piece still landing here was refused by nothing and lost its "
                              + "bounds some other way";
                    }
                    else if (seg.Engulfing)
                    {
                        tag = "ENGULFING single mesh — undecidable as one unit";
                        why = "single mesh that ENGULFS its own room — held solid, undecidable as "
                              + "one unit (NeutralizeEngulfingSegments)";
                    }
                    else if (seg.DoorRoot != null)
                    {
                        tag = "doorway — never fades (user ruling 2026-08-02)";
                        why = tag;
                    }
                    else if (!RoomDecisionValid(seg.RoomIndex))
                    {
                        tag = "room has no valid floor grid — FAIL-SAFE solid";
                        why = $"room {seg.RoomIndex} has no valid floor grid — FAIL-SAFE solid";
                    }
                    else if (!seg.RunDriven)
                    {
                        tag = "NOT run-driven though it carries the run key";
                        why = "NOT run-driven though it carries the run key — the distribution "
                              + "missed it, and THAT would be a distribution bug";
                    }
                    else
                    {
                        tag = "run-driven, below full fade — mid-ramp or no dissolve channel";
                        why = tag + " (see the DISSOLVE CENSUS for this piece)";
                    }
                    _runLeftoverByReason.TryGetValue(tag, out int seen);
                    _runLeftoverByReason[tag] = seen + 1;
                    string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    // THE USER'S CLASSES. A leftover is not automatically a defect since the
                    // 2026-08-24 refinement — measured against the room the RUN decided on,
                    // because that is the floor he is looking at when the wall goes.
                    int room = run.Room >= 0 ? run.Room : seg.RoomIndex;
                    string cls = ClassifyLeftover(shown, room, out int blockedSamples,
                                                  out float foot, out float top,
                                                  out int visibleSamples);
                    _runLeftoverByClass.TryGetValue(cls, out int clsSeen);
                    _runLeftoverByClass[cls] = clsSeen + 1;
                    // THE RENDERER IS NAMED, NOT JUST ITS SEGMENT (ModBuild 262). 261 printed the
                    // segment anchor's name for a verdict measured on some renderer inside it,
                    // which for a mounted prop is a different object entirely.
                    string geom = $"'{shown.name}' foot {foot:F2} wu / top {top:F2} wu over room "
                                  + $"{room}'s floor, hides {blockedSamples} of "
                                  + $"{visibleSamples} in-view playable-tile sample(s)"
                                  + LeftoverExemptionNote(shown);
                    // ALLOWED pieces get their own list so a large ALLOWED population can never be
                    // read as a large defect — which is precisely the mistake the previous two
                    // rounds made with the 99 and the 111.
                    if (cls == "ALLOWED")
                    {
                        if (_runLeftoverAllowed.Count < MountedLeftoverCap)
                            _runLeftoverAllowed.Add($"on '{wall}': {geom}");
                        continue;
                    }
                    if (_runLeftoverNames.Count >= MountedLeftoverCap)
                        continue;
                    string owner = run.Anchor != null ? run.Anchor.name : "<dead>";
                    _runLeftoverNames.Add(
                        $"[{cls}] {geom} — carried by piece '{wall}' at fade {seg.Fade:F2} while "
                        + $"its run '{owner}' is at {run.MaxFade:F2} — {why}");
                }
                if (anyDrawing)
                    _runLeftoverSegments++;
            }
        }

        /// <summary>
        /// EVERY RENDERER ONE SEGMENT OWNS — the eight sources a leftover can be drawing out of,
        /// deduplicated and in a stable order. ModBuild 261's sweep read two of them and stopped
        /// at the first hit; the steady ModBuild 260 leftover population lives in
        /// <see cref="Segment.Mounted"/>, which it never touched.
        ///
        /// <para>COST: this walks lists the segment already holds — no scene query, no
        /// <c>GetComponent</c>, no allocation past the two scratch containers, which are fields.
        /// It runs once per split-run piece per rescan (the ModBuild 260 scenario: 140 pieces of
        /// the largest run, at the 2 s rescan cadence), so it is not in the frame.</para>
        /// </summary>
        private void CollectSegmentLeftoverCandidates(Segment seg)
        {
            _leftoverCandidates.Clear();
            _leftoverSeen.Clear();
            AddLeftoverCandidate(seg.Anchor as Renderer);
            for (int i = 0; i < seg.Renderers.Count; i++)
                AddLeftoverCandidate(seg.Renderers[i]);
            for (int i = 0; i < seg.Foliage.Count; i++)
                AddLeftoverCandidate(seg.Foliage[i]);
            for (int i = 0; i < seg.Siblings.Count; i++)
                AddLeftoverCandidate(seg.Siblings[i]);
            for (int i = 0; i < seg.Mounted.Count; i++)
                AddLeftoverCandidate(seg.Mounted[i].Renderer);
            for (int i = 0; i < seg.Stacked.Count; i++)
                AddLeftoverCandidate(seg.Stacked[i].Renderer);
            for (int i = 0; i < seg.Body.Count; i++)
                AddLeftoverCandidate(seg.Body[i].Renderer);
            for (int i = 0; i < seg.UnitDressing.Count; i++)
                AddLeftoverCandidate(seg.UnitDressing[i].Renderer);
        }

        private void AddLeftoverCandidate(Renderer? r)
        {
            if (r == null || !_leftoverSeen.Add(r))
                return;
            _leftoverCandidates.Add(r);
        }

        private readonly List<Renderer> _leftoverCandidates = new();
        private readonly HashSet<Renderer> _leftoverSeen = new();

        /// <summary>
        /// A standing NOTE, never a class. Two user rulings already say a specific piece may stay
        /// — the fountain (2026-08-09, brunnen.png) and the doorway arch (2026-08-02) — and both
        /// are enforced as spatial rects that pull the piece back off its wall. A geometric
        /// FLOATING verdict on such a piece is CORRECT about the geometry and moot as a defect,
        /// and without this note the next round would chase it. Deliberately not folded into the
        /// class: the classes are the user's three words and nothing else may be smuggled into
        /// them.
        ///
        /// <para>MODBUILD 264: the two rects here are also the two exclusion terms of
        /// <see cref="IsWallGeneratedMember"/>, so an EXEMPT piece can never be classified WALL
        /// MEMBER. That is not a special case bolted onto the class — an exemption rect IS a
        /// statement that some other owner holds the piece, which is the same question the
        /// membership test asks. If a third exemption rect is ever added, add it in BOTH places or
        /// the new one will be reported as the defect it is exempt from.</para>
        /// </summary>
        private string LeftoverExemptionNote(Renderer r)
        {
            Bounds b = r.bounds;
            if (IsWaterProtected(b))
                return " [EXEMPT: water feature, user ruling 2026-08-09 — allowed to stay]";
            if (IsArchProtected(b, r.name))
                return " [EXEMPT: doorway arch, user ruling 2026-08-02 — allowed to stay]";
            return string.Empty;
        }

        private int _runLeftover;
        /// <summary>ModBuild 262: how many split-run PIECES had at least one drawing renderer —
        /// the number ModBuild 261's <c>_runLeftover</c> actually held, kept so the two logs can
        /// be compared. <see cref="_runLeftover"/> now counts RENDERERS.</summary>
        private int _runLeftoverSegments;
        private readonly List<string> _runLeftoverNames = new();
        /// <summary>ModBuild 261: leftover count per REASON. The shared LEFTOVER line names at
        /// most <see cref="PerWallNameCap"/> pieces, which for a 99-piece population is a sample
        /// and not a distribution — the in-repo lesson is read-the-whole-distribution. This map
        /// is printed complete on the SPLIT-RUN LEFTOVER line below.</summary>
        private readonly Dictionary<string, int> _runLeftoverByReason = new();
        /// <summary>ModBuild 261: the same leftovers again, by the user's OWN three classes (see
        /// <see cref="ClassifyLeftover"/>). Separate from the reason tally because a reason says
        /// which rule holds a piece and a class says whether he minds.</summary>
        private readonly Dictionary<string, int> _runLeftoverByClass = new();
        private readonly List<string> _runLeftoverAllowed = new();

        /// <summary>
        /// THE USER'S THREE CLASSES (ruling 2026-08-24, refining the same day's "alles muss
        /// faden"): "Es gibt Dinge die stehen bleiben dürfen. zB der Brunnen … oder auch dieses
        /// Steingebilde … weil es auch niedrig ist und nicht die Sicht verdeckt. Aber es dürfen
        /// keine Elemente 'herumfliegen' weil die Wand die es gehalten hat nicht mehr da ist."
        ///
        /// <list type="bullet">
        /// <item>FLOATING — its foot does not reach the floor band. It was carried by the wall and
        ///   the wall is gone. He calls this out most sharply; it must never happen.</item>
        /// <item>WALL MEMBER — the wall generator BUILT it (ModBuild 264). A defect whatever its
        ///   height and whatever it hides; see <see cref="IsWallGeneratedMember"/>.</item>
        /// <item>OBSTRUCTING — it stands on the floor and still hides a playable tile.</item>
        /// <item>ALLOWED — it belongs to NO wall, stands on the floor and hides nothing. A well, a
        ///   low stone formation, a crystal on the ground. Not part of the wall at all, in his
        ///   words. Since ModBuild 264 that first clause is a test and not a hope.</item>
        /// </list>
        ///
        /// <para>NO NEW CONSTANT IS INTRODUCED, deliberately — four thresholds in this subsystem
        /// have been falsified by the next hardware log. FLOATING reuses
        /// <see cref="WallStandingProp.FootBandWU"/>, the incumbent foot band every standing-prop
        /// verdict is already measured with. OBSTRUCTING is not a height at all: it asks the
        /// subsystem's own question — does this piece intercept the head→sample ray of any
        /// FRUSTUM-VISIBLE playable-tile sample of the room? A piece that blocks zero samples does
        /// not obstruct, by the definition the fade trigger itself runs on, and "low enough" then
        /// needs no number. The blocked-sample COUNT is reported rather than a bare bool, so the
        /// next log shows how many pieces sit at the 0/1 boundary — i.e. whether a rule built on
        /// this test would need hysteresis or is comfortably separated.</para>
        ///
        /// <para><b>MODBUILD 262 — IT DEGRADES OUT LOUD, BECAUSE THIS IS ONE SCENARIO OF MANY.</b>
        /// (User, 2026-08-24: <i>"Das Level was ich jetzt gerade die ganze Zeit teste ist nur
        /// eines von vielen — natürlich erwarte ich dass der Code generisch auf alle Szenarios und
        /// Räume im gesamten Spiel funktioniert."</i>) The two CLASS DEFINITIONS are generic —
        /// they are pure geometry against the room's own floor and the room's own playable-tile
        /// samples, and neither mentions a tileset, a shader family or a room size. The INPUTS are
        /// not guaranteed, and every one of them fails toward a confident wrong ALLOWED:
        /// <list type="number">
        /// <item>a room with no ANCHORED floor plane — <c>_roomFloorY</c> holds a number for every
        ///   room, anchored or not, so reading it blind produces a foot height measured against a
        ///   guess. Asked with <see cref="RoomDecisionValid"/>, the same predicate the fade
        ///   decision itself runs on.</item>
        /// <item>a room with NO sample grid at all — a scenario with more revealed rooms than the
        ///   <c>MaxTotalSamples</c> budget covers hands late rooms <c>_roomSampleCount = 0</c> and
        ///   holds their walls solid. Zero samples means zero blocked, which would have read as
        ///   ALLOWED for every piece in the room.</item>
        /// <item>no sample of the room FRUSTUM-VISIBLE this tick — "it hides nothing from him"
        ///   is then a statement about where his head happens to point, not about the piece.
        ///   ALLOWED is a permanent-sounding verdict and must not be issued from a momentary
        ///   one.</item>
        /// </list>
        /// Each returns its own UNJUDGED string naming the missing input. A wrong ALLOWED is worse
        /// than an explicit refusal to judge: ALLOWED is the class that says "leave it alone".
        /// </para>
        /// </summary>
        private string ClassifyLeftover(Renderer r, int room, out int blockedSamples,
            out float foot, out float top)
            => ClassifyLeftover(r, room, out blockedSamples, out foot, out top, out _);

        private string ClassifyLeftover(Renderer r, int room, out int blockedSamples,
            out float foot, out float top, out int visibleSamples)
        {
            Bounds b = r.bounds;
            blockedSamples = 0;
            visibleSamples = 0;
            foot = 0f;
            top = 0f;
            if (room < 0 || room >= _roomFloorY.Count || !RoomDecisionValid(room))
                return "UNJUDGED (no anchored floor plane for this room)";
            float floorY = _roomFloorY[room];
            foot = b.min.y - floorY;
            top = b.max.y - floorY;
            if (foot > WallStandingProp.FootBandWU)
                return "FLOATING";
            // MODBUILD 264 — MEMBERSHIP BEFORE GEOMETRY, and deliberately ahead of BOTH remaining
            // UNJUDGED arms as well as the sample test: provenance needs no sample grid and no
            // frustum-visible sample to be true, so a wall member is judged even in the two states
            // ModBuild 262 rightly refuses to judge OBSTRUCTING/ALLOWED in. It cannot be asked
            // before the anchored-floor arm above, because both of its exclusion terms are
            // measured against that plane. See IsWallGeneratedMember for the number that forced
            // this ordering.
            if (IsWallGeneratedMember(r, floorY))
                return "WALL MEMBER";
            visibleSamples = PieceBlockedSamples(r, room, out blockedSamples);
            if (visibleSamples < 0)
                return "UNJUDGED (this room has no playable-tile sample grid)";
            if (visibleSamples == 0)
                return "UNJUDGED (no playable-tile sample of this room is in view this tick)";
            return blockedSamples > 0 ? "OBSTRUCTING" : "ALLOWED";
        }

        /// <summary>
        /// MODBUILD 264 — DOES A WALL GENERATOR OWN THIS RENDERER? The one test that decides
        /// whether a leftover is ALLOWED or a defect, and it is a MEMBERSHIP question rather than
        /// a geometric one.
        ///
        /// <para><b>THE NUMBER THAT FORCED IT.</b> The ModBuild-261 log classifies its leftovers
        /// <c>BY THE USER'S THREE CLASSES: 99 × ALLOWED</c> on 21 of 22 passes and
        /// <c>98 × ALLOWED; 1 × OBSTRUCTING</c> on the twenty-second — a healthy scene by the
        /// geometric rule — while mauerproblem_erneut2.jpg shows a wall run at fade 1.00 behind a
        /// solid band of ferns, ivy curtains and vine-covered stumps. The user's reply:
        /// <i>"Das Gestrüp an der hinteren Wand ist immer noch nicht weg — das soll vollständig
        /// alles mit-weg-faden"</i>, and <i>"Leite von den Regeln dieses Raumes weitere ab für
        /// alle Szenarios … sondern überall funktioniert"</i>. So "is it low and does it block a
        /// sample" is not his question. His is: does this belong to the wall? Scrub, ivy, ferns
        /// and trunks the wall generator produced belong to it and go with it however low they
        /// are; a crystal formation standing on the floor does not.</para>
        ///
        /// <para>ModBuild 262 and 263 widened WHAT this classifier sees (all eight lists a piece
        /// owns, a verdict per drawing renderer, and the UNJUDGED arms that stop a missing input
        /// reading as ALLOWED) and every one of those numbers stands. They do not touch the
        /// question this test asks, which is the one the geometric classes cannot express.</para>
        ///
        /// <para><b>WHY IT IS GENERIC AND NOT A NAME LIST.</b> The discriminator is the tileset's
        /// OWN CONSTRUCTION, not an asset family: every biome's wall generator parents its
        /// dressing under <c>Walls/Wall N/Generated Content/…</c>. That is where the same log
        /// finds the offenders — <c>'CR_FR_Wall_Grassy_Verge_Ivy_01'</c>,
        /// <c>'CR_FR_Wall_LS_PlantsBushes_03'</c>, <c>'CR_FR_Wall_Log_Structure_Stump_03'</c> and
        /// 180 more ancestry rows under <c>'Walls/Wall 1/Generated Content/…'</c> — and it is NOT
        /// where it finds the one piece he says may stay: <c>'CV_Ice_Crystal_Form_02'</c> appears
        /// in no ancestry row, in no <c>FADE WRITE</c> row and in no prop unit of any wall. The
        /// test names no tileset, no shader family, no size and no coordinate.</para>
        ///
        /// <para><b>THE TRAP, AND THE TWO TERMS THAT DISARM IT — a hierarchy path is not
        /// membership.</b> Floor hexes are ALSO parented under <c>Wall N/Generated Content/</c>;
        /// <see cref="WallStandingProp"/> records the exact path
        /// (<c>Wall N/Generated Content/PCG_CR_Floor_BaseHex_Plain/EN_CR_Floor_BaseHex_Plain</c>)
        /// and the ModBuild-261 STANDING PROP line catches them being claimed —
        /// <c>848 claim(s) refused this rescan: 'FR_Floor_Grass_Half_02' → 'Wall 1',
        /// 'FR_Floor_Scatter_Grass_Medium_05' → 'Wall 1', 'FR_Floor_Grass' → 'Wall 1', …</c>.
        /// So the test carries the SAME two terms the fade path itself already uses to decide a
        /// wall does not own a piece, and introduces no third:
        /// <list type="number">
        /// <item>the ground band <see cref="FadeDriver.GroundExclusionHeightWU"/> — the rule
        ///   <see cref="FadeDriver.StripGroundRenderers"/> removes ground-lying renderers from a
        ///   segment by, verbatim. Every floor piece the 261 log names is comfortably inside it:
        ///   tops −0.24, −0.20, −0.15, −0.19 wu over the floor, and
        ///   <c>PCG_FR_Floor_Grass_Hex_Split_PR</c> at foot −0.3 / height 0.5, against 1.0 wu;</item>
        /// <item>the water rect, <see cref="FadeDriver.IsWaterProtected"/> — the pond, its basin,
        ///   bank, rim and emitters are the water feature's own unit (user ruling 2026-08-09);
        ///   </item>
        /// <item>the doorway-arch rect, <see cref="FadeDriver.IsArchProtected"/> — a doorway never
        ///   fades (user ruling 2026-08-02) and that rect already pulls its masonry back off the
        ///   wall, so the wall does not own it either. Without this term a correctly-behaving
        ///   doorway would be reported as the defect this class exists to find, and the user says
        ///   the masonry at the door behaves exactly as he wants.</item>
        /// </list>
        /// NO NEW CONSTANT, which is the standing discipline in this file: four thresholds here
        /// have been shipped from a single scenario's numbers and each was falsified by the next
        /// hardware log.</para>
        ///
        /// <para>MULTIPLAYER: this is a diagnostic classification of local scene hierarchy. No
        /// decision, no wire record, no peer-visible state.</para>
        ///
        /// <para>COST: one hierarchy climb per LEFTOVER — never per frame and never a scene sweep.
        /// It is the same call the sibling adoption sweep already makes per candidate
        /// (<c>r.GetComponentInParent&lt;ProceduralWall&gt;()</c>), and it runs inside the audit
        /// ModBuild 262 already sliced against the 1.5 ms budget.</para>
        ///
        /// <para>FALSIFIED BY: a WALL MEMBER entry naming a floor hex or a pond rim — then the
        /// band or the rect is the wrong term, not the membership rule; or the user reporting
        /// scrub while this class reads zero — then the wall generator did not build that scrub
        /// and provenance is not what identifies it.</para>
        /// </summary>
        private bool IsWallGeneratedMember(Renderer r, float floorY)
        {
            Bounds b = r.bounds;
            if (b.max.y <= floorY + GroundExclusionHeightWU)
                return false;               // ground band — the wall never owned it
            if (IsWaterProtected(b))
                return false;               // the water feature's own unit owns it
            if (IsArchProtected(b, r.name))
                return false;               // the doorway owns it, and doorways never fade
            return r.GetComponentInParent<ProceduralWall>() != null;
        }

        /// <summary>
        /// How many of a room's FRUSTUM-VISIBLE playable-tile samples this one renderer hides from
        /// the head — the broad phase of <c>RoomBlockedFraction</c> applied to the renderer's own
        /// world AABB instead of a segment's union box, with the same "clearly before the point"
        /// rule and the SAME constants (<c>BlockEpsDistFraction</c>, <c>BlockEpsMin/MaxWorld</c>).
        /// Nothing new is tuned here. Sixteen samples per room in the ModBuild 260 scenario — but
        /// the count is <c>min(grid², playable hexes)</c> and <c>grid</c> itself falls to 3, 2 and
        /// finally 1 as the revealed room count rises, so it is 16 in that scenario and NOT a
        /// property of this code. Run once per leftover per rescan, so the cost is not in the
        /// frame at any of those sizes.
        ///
        /// <para>RETURNS the number of samples that were VISIBLE and therefore askable, or -1 when
        /// the room has no grid at all; <paramref name="blocked"/> is how many of those this
        /// renderer intercepts. Two numbers because 0-of-0 and 0-of-16 are opposite findings and
        /// the ModBuild 261 signature could not tell them apart.</para>
        /// </summary>
        private int PieceBlockedSamples(Renderer r, int room, out int blocked)
        {
            blocked = 0;
            if (room < 0 || room >= _roomSampleCount.Count)
                return -1;
            int total = _roomSampleCount[room];
            if (total <= 0)
                return -1;
            Bounds b = r.bounds;
            float thicknessEps = Mathf.Clamp(0.5f * Mathf.Min(b.size.x, b.size.z),
                                             BlockEpsMinWorld, BlockEpsMaxWorld);
            int start = _roomSampleStart[room];
            int end = Mathf.Min(start + total,
                                Mathf.Min(_allSamples.Count, _sampleVisible.Length));
            Vector3 headPos = _lastHeadPos;
            int visible = 0;
            for (int i = start; i < end; i++)
            {
                if (!_sampleVisible[i])
                    continue; // out of view direction — it cannot be hiding this from him
                Vector3 sample = _allSamples[i];
                Vector3 to = sample - headPos;
                float dist = to.magnitude;
                if (dist < 0.001f)
                    continue;
                visible++;
                float eps = Mathf.Max(thicknessEps, BlockEpsDistFraction * dist);
                var ray = new Ray(headPos, to / dist);
                if (b.IntersectRay(ray, out float d) && (d < dist - eps || b.Contains(sample)))
                    blocked++;
            }
            return visible;
        }

        /// <summary>
        /// ModBuild 261 — THE COMPLETE DISTRIBUTION of everything still drawing beside a faded
        /// run, one entry per reason with its count, nothing truncated. It is a separate line
        /// from the shared <c>LEFTOVER OVER A FADED WALL</c> warn on purpose: that line caps its
        /// names at <see cref="PerWallNameCap"/> = 8, and 8 of 99 is a mode, not a distribution
        /// (the in-repo lesson that cost a build: "sort -u | head shows the mode").
        ///
        /// <para>WHAT TO READ OFF IT. If the refusal reason dominates, the boundless fail-safe is
        /// innocent and the lever is the choke point's FLOOR arm — those pieces own no renderer,
        /// so no change to the fail-safe can move them. If any OTHER reason carries a large
        /// count, that reason is the lever instead and this line says so by name. Silent when
        /// nothing is left over, so a clean session prints nothing.</para>
        /// </summary>
        private void LogSplitRunLeftoverBreakdown()
        {
            if (_runLeftoverByReason.Count == 0)
                return;
            _runReasonSb.Length = 0;
            foreach (KeyValuePair<string, int> kv in _runLeftoverByReason)
            {
                if (_runReasonSb.Length > 0)
                    _runReasonSb.Append("; ");
                _runReasonSb.Append(kv.Value).Append(" × ").Append(kv.Key);
            }
            _runClassSb.Length = 0;
            foreach (KeyValuePair<string, int> kv in _runLeftoverByClass)
            {
                if (_runClassSb.Length > 0)
                    _runClassSb.Append("; ");
                _runClassSb.Append(kv.Value).Append(" × ").Append(kv.Key);
            }
            VRLog.Warn(Name,
                $"SPLIT-RUN LEFTOVER: {_runLeftover} RENDERER(S) across {_runLeftoverSegments} "
                + "piece(s) of a FADED run are actually drawing — read off every renderer the "
                + "piece owns (anchor + Renderers + Foliage + Siblings + Mounted + Stacked + "
                + "Body + UnitDressing, deduplicated), never off one list, which is the ModBuild "
                + "261 blind spot this line closes: that build read two of the eight sources and "
                + "STOPPED AT THE FIRST HIT, so one verdict stood for a whole piece and the "
                + "MOUNTED ledger — where the steady 260 leftover population lives — was never "
                + "asked. The renderer count is the new number; the piece count is what ModBuild "
                + "261 printed here. "
                // THE CLASSIFICATION IS THE HEADLINE, NOT THE COUNT (user ruling 2026-08-24, and
                // it retires "0 leftovers" as a target): FLOATING and OBSTRUCTING are the defect,
                // ALLOWED is not. A big ALLOWED number here is a HEALTHY reading.
                + $"BY THE USER'S THREE CLASSES: {_runClassSb}. FLOATING = its foot is above the "
                + $"{WallStandingProp.FootBandWU:0.0} wu floor band, i.e. the wall was holding it "
                + "up and is gone — the one he says must never happen. OBSTRUCTING = on the floor "
                + "and still hiding at least one FRUSTUM-VISIBLE playable-tile sample, measured "
                + "with the fade trigger's own ray test, so 'low enough' needs no height constant. "
                + "WALL MEMBER = the wall generator built it (inside a ProceduralWall subtree, "
                + "above the ground band, outside every water rect) — a defect at fade "
                + $"≥{FoliageHideFade:0.00} whatever its height and whatever it hides, which is "
                + "the class ModBuild 264 added because 99 x ALLOWED read as a healthy scene "
                + "while the photograph showed a solid hedge. "
                + "ALLOWED = belongs to NO wall, on the floor, hides nothing — the well, the low "
                + "stone formation and the crystal on the ground, "
                + $"which he does not regard as part of the wall: {_runLeftoverAllowed.Count} "
                + "named ["
                + string.Join("; ", _runLeftoverAllowed)
                + "]. COMPLETE per-reason distribution over ALL three classes (which RULE holds "
                + "each piece, as opposed to whether he minds), nothing truncated "
                + $"({_runLeftoverByReason.Count} distinct reason(s)): {_runReasonSb}. Named "
                + $"defects (up to {MountedLeftoverCap}): "
                + string.Join("; ", _runLeftoverNames)
                + ". A renderer tagged [EXEMPT] carries a standing user ruling of its own (the "
                + "fountain 2026-08-09, the doorway arch 2026-08-02) and is allowed to stay "
                + "whatever its geometry says.");
        }

        private readonly System.Text.StringBuilder _runReasonSb = new();
        private readonly System.Text.StringBuilder _runClassSb = new();

        /// <summary>
        /// The SPLIT RUN census, printed on the PER-WALL cadence. Its whole job is to make the
        /// difference between "one wall decided" and "forty pieces decided" a number rather than
        /// an intention — and to expose the orphan population, which is the one class this
        /// mechanism cannot own.
        /// </summary>
        private void LogSplitRuns(float now)
        {
            if (_runsTotal == 0 && _runOrphans == 0)
                return;
            if (now < _nextRunLogTime)
                return;
            _nextRunLogTime = now + InsideLogIntervalSeconds;
            LogSplitRunLeftoverBreakdown();
            VRLog.Info(Name,
                $"SPLIT RUN: {_runsFaded} of {_runsTotal} split wall run(s) faded, driving "
                + $"{_runMembersTotal} piece(s) as whole walls ({_runMembersRefused} of those "
                + "own NO renderer because the wall choke point REFUSED them as floor-standing "
                + "props — they were never wall geometry and no fail-safe is holding them; a "
                + "further "
                + $"{_runMembersHeld - _runMembersRefused} are held solid by an OLDER fail-safe "
                + "WITH geometry — boundless for some other reason, room-engulfing single mesh, "
                + "doorway, or a room with no valid floor grid — and are the only members that "
                + "can legitimately stay while their run goes; the SPLIT-RUN LEFTOVER line below "
                + "gives the complete per-reason distribution, the LEFTOVER line a sample). "
                + $"{_runOrphans} orphan(s): pieces whose run anchor is destroyed, which keep "
                + "their OWN decision (the pre-259 behaviour, fail-open — never a latch). A run "
                + "is a ProceduralWall/tile-observer group carved up by "
                + "NeutralizeEngulfingSegments; every member is a child of that one object, so "
                + "no unsplit wall can be reached from here. Unsplit walls ('Wall 2', 'Wall 3', "
                + "'Wall 4' in the ModBuild 258 log) are absent from this line BY CONSTRUCTION "
                + "and their code path is byte-for-byte the ModBuild 258 one. Live dial "
                + $"[WallFade] SplitRunUnified = {WallFadeTuning.SplitRunUnifiedOn} — a false "
                + "here means this whole mechanism did not run and any verdict about it is void. "
                + "Live dial [WallFade] SplitRunAdoptGroundScenery = "
                + $"{WallFadeTuning.AdoptGroundScenery} ({_runMembersPassenger} passenger(s) "
                + "recruited this pass — the scrub/stones/low bushes the FLOOR arm used to refuse; "
                + "a FALSE here with a large refused count above means the recruitment did not "
                + "run and any verdict about the standing hedge is void). "
                // IMMEDIACY (user ruling 2026-08-24: "direkt wieder unfaded wenn es keine
                // spielbaren tiles verdeckt"). Three delays sit between "covers nothing" and
                // "solid again", and this clause states all three so the next log can rank them
                // instead of a lane guessing. The samples ARE playable tiles already: the grid is
                // CNode.Walkable and not CNode.Blocked (see the SAMPLE GRID line).
                + $"UN-FADE: union {_runUnionBlocked}/{_runUnionTotal} vs widest single member "
                + $"{_runUnionBest}/{_runUnionTotal}, sampled NOW and not at an edge (at an edge "
                + "the union is under the bar by construction, which is why the RUN FADE pair "
                + "could never measure this). The union alone has held a faded run down for "
                + $"{_runUnionHoldNow:F1}s right now, worst {_runUnionHoldWorst:F1}s this "
                + $"session — zero means the union costs the un-fade nothing. Exit bar "
                + $"{WallFadeTuning.Off:F2} of the room's playable hexes (a run stays faded while "
                + "it still covers that much, so 'covers nothing' is NOT the release condition), "
                + $"then dwell {WallFadeTuning.DwellMoved:0.0}s if the perspective moved / "
                + $"{WallFadeTuning.DwellStationary:0.0}s if the head only rotated, on top of the "
                + "EMA lag. All three are live [WallFade] dials.");
        }

        /// <summary>
        /// COMMIT PHASE 24 — see <c>RescanCore</c>. Rebuild the cached board volume. Deliberately
        /// LAST: it reads the room registry AND every segment's final decision AABB, so it must
        /// run after the gate-bounds guarantee that is itself deliberately last.
        /// </summary>
        private void CommitBoardVolume()
        {
            _boardVolumeValid = false;
            _boardVolumeRooms = 0;
            _boardVolumeWalls = 0;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float floorY = float.MaxValue;
            for (int i = 0; i < _roomBounds.Count; i++)
            {
                // Only rooms the coverage decision itself trusts (tile-anchored plane, non-empty
                // grid). A guessed frame must not define where the player is standing either.
                if (!RoomDecisionValid(i))
                    continue;
                Bounds b = _roomBounds[i];
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
                if (i < _roomFloorY.Count && _roomFloorY[i] < floorY) floorY = _roomFloorY[i];
                _boardVolumeRooms++;
            }
            if (_boardVolumeRooms == 0 || floorY == float.MaxValue)
            {
                // No decision-valid room: the 3D map room and every pre-generation frame. The
                // volume stays invalid and the INSIDE rule can never fire.
                AnnounceBoardVolume();
                return;
            }

            // The walls belong to the footprint too — the room renderers are the FLOOR proxies,
            // so a player standing inside a perimeter wall would otherwise read as outside the
            // board, which is the one place the head-in-stone escape hatch has to work.
            _crestScratch.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds || !RoomDecisionValid(seg.RoomIndex))
                    continue;
                Bounds b = seg.Bounds;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
                _boardVolumeWalls++;
                // Doorways and engulfing pieces are in the FOOTPRINT but not in the crest
                // sample: an arch top is not a wall height, and an engulfing AABB is the very
                // shape the coverage metric already refuses to trust.
                if (seg.DoorRoot == null && !seg.Engulfing)
                    _crestScratch.Add(b.max.y - floorY);
            }
            if (_crestScratch.Count == 0)
            {
                AnnounceBoardVolume();
                return;
            }

            // MEDIAN, not max: a keep's stacked superstructure is a legitimate segment AABB
            // three stories tall, and taking the tallest would put the crest plane above every
            // wall in the scenario — the volume would then swallow a player who is plainly
            // looking down at the board from outside.
            _crestScratch.Sort();
            float crest = Mathf.Max(_crestScratch[_crestScratch.Count / 2], BoardCrestMinWU);
            _boardCrestWU = crest;
            _boardFloorY = floorY;
            _boardVolume = new Bounds(
                new Vector3((minX + maxX) * 0.5f, floorY + crest * 0.5f, (minZ + maxZ) * 0.5f),
                new Vector3(Mathf.Max(maxX - minX, 0f), crest, Mathf.Max(maxZ - minZ, 0f)));
            _boardVolumeValid = true;
            AnnounceBoardVolume();
        }

        /// <summary>
        /// Print the board volume whenever it materially changes (new scenario, room reveal).
        /// This is the line that proves the rule was ARMED with real numbers even in a session
        /// where the player never went inside — an instrument that only speaks when it triggers
        /// cannot distinguish "did not happen" from "never ran".
        /// </summary>
        private void AnnounceBoardVolume()
        {
            int sigCrest = Mathf.RoundToInt(_boardCrestWU * 10f);
            int sigX = _boardVolumeValid ? Mathf.RoundToInt(_boardVolume.size.x * 10f) : 0;
            int sigZ = _boardVolumeValid ? Mathf.RoundToInt(_boardVolume.size.z * 10f) : 0;
            if (_boardVolumeRooms == _bvSigRooms && _boardVolumeWalls == _bvSigWalls
                && sigCrest == _bvSigCrest && sigX == _bvSigX && sigZ == _bvSigZ)
                return;
            _bvSigRooms = _boardVolumeRooms;
            _bvSigWalls = _boardVolumeWalls;
            _bvSigCrest = sigCrest;
            _bvSigX = sigX;
            _bvSigZ = sigZ;
            if (!_boardVolumeValid)
            {
                VRLog.Info(Name,
                    "BOARD VOLUME: none — "
                    + $"{_boardVolumeRooms} decision-valid room(s), {_boardVolumeWalls} wall(s) "
                    + "with a decision AABB. The INSIDE-the-map stand-down cannot fire until a "
                    + "room is tile-anchored with a non-empty floor grid; this is also the "
                    + "steady state of the 3D map room, which has no occlusion volumes at all.");
                return;
            }
            Vector3 mn = _boardVolume.min, mx = _boardVolume.max;
            VRLog.Info(Name,
                $"BOARD VOLUME: x {mn.x:F1}..{mx.x:F1}, y {mn.y:F2}..{mx.y:F2}, "
                + $"z {mn.z:F1}..{mx.z:F1} world units — union footprint of "
                + $"{_boardVolumeRooms} decision-valid room(s) and {_boardVolumeWalls} wall(s), "
                + $"floor plane {_boardFloorY:F2}, MEDIAN wall crest {_boardCrestWU:F2} wu over "
                + $"{_crestScratch.Count} wall(s). INSIDE bars: enter at signed distance "
                + $"<= {-InsideEnterDepthFraction * _boardCrestWU:F2} wu, leave at "
                + $">= {InsideExitDepthFraction * _boardCrestWU:F2} wu (dwell "
                + $"{EnterDwellSeconds:F2}s in / {WallFadeTuning.DwellMoved:F2}s out). The "
                + "verdict is DIAGNOSTIC — it gates no fade; every wall is decided solely by its "
                + $"own coverage against the live bars {WallFadeTuning.On:F2}/"
                + $"{WallFadeTuning.Off:F2}.");
        }

        /// <summary>
        /// THE INSIDE TEST — signed distance of the head to the cached board volume, through the
        /// Schmitt bars and dwells. O(1) and allocation-free by construction (see class header),
        /// so it is called every frame rather than on the evaluation cadence: the boundary
        /// crossing is a deliberate act and its edge must not wait out a skipped evaluation.
        /// Returns the debounced verdict.
        /// </summary>
        private bool UpdateInsideBoard(Vector3 headPos, float now, float rigScale)
        {
            if (!_boardVolumeValid)
            {
                if (_insideBoard || _insidePending)
                    ResetInsideBoardState();
                return false;
            }

            Vector3 c = _boardVolume.center, e = _boardVolume.extents;
            float qx = Mathf.Abs(headPos.x - c.x) - e.x;
            float qy = Mathf.Abs(headPos.y - c.y) - e.y;
            float qz = Mathf.Abs(headPos.z - c.z) - e.z;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f), oz = Mathf.Max(qz, 0f);
            // Exact point-to-box signed distance: the outside part is Euclidean, the inside part
            // is the least-negative slab (how far the head is from the NEAREST face).
            float sd = Mathf.Sqrt(ox * ox + oy * oy + oz * oz)
                       + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f);
            _lastInsideSigned = sd;
            _lastInsideMarginY = qy;
            _lastInsideMarginXZ = Mathf.Max(qx, qz);

            bool want = _insideBoard
                ? sd < InsideExitDepthFraction * _boardCrestWU
                : sd <= -InsideEnterDepthFraction * _boardCrestWU;
            if (want != _insidePending)
            {
                _insidePending = want;
                _insidePendingSince = now;
            }
            if (_insidePending != _insideBoard)
            {
                float dwell = _insidePending ? EnterDwellSeconds : WallFadeTuning.DwellMoved;
                if (now - _insidePendingSince >= dwell)
                {
                    _insideBoard = _insidePending;
                    // NO RELEASE. ModBuild 251 handed every faded wall back here in one step;
                    // that is the "alle auf einmal" the user rejected, and it bypassed each
                    // wall's own exit dwell. Crossing this boundary is now an observation with
                    // no side effects at all.
                    LogInsideState(headPos, rigScale, edge: true);
                    _nextInsideLogTime = now + InsideLogIntervalSeconds;
                }
            }
            return _insideBoard;
        }

        /// <summary>
        /// Drop the cached volume AND the verdict that was measured against it. Called on a
        /// scene load: a box measured in the previous scenario is not evidence about this one,
        /// and between the load and the first commit the head could sit anywhere relative to it.
        /// The next commit rebuilds both and the BOARD VOLUME line re-announces them.
        /// </summary>
        private void InvalidateBoardVolume()
        {
            _boardVolumeValid = false;
            _boardVolumeRooms = 0;
            _boardVolumeWalls = 0;
            _boardCrestWU = 0f;
            _bvSigRooms = -1;
            _bvSigWalls = -1;
            ResetInsideBoardState();
        }

        /// <summary>Drop the INSIDE verdict (scenario teardown, toggle off, no valid board).
        /// Purely diagnostic state now — nothing downstream reads it.</summary>
        private void ResetInsideBoardState()
        {
            _insideBoard = false;
            _insidePending = false;
            _insidePendingSince = 0f;
        }

        /// <summary>
        /// Reset the per-pass independence census. Called every frame from the tick, so the
        /// numbers the falsifier prints always describe the pass that just ran.
        /// </summary>
        private void BeginPerWallCensus()
        {
            _pwTotal = 0;
            _pwFaded = 0;
            _pwNames.Clear();
            _pwMaxSmooth = 0f;
            _pwMinSmooth = float.MaxValue;
            _pwMaxWall = "-";
            _pwMinWall = "-";
            _pwPeerDriven = 0;
            _pwGateDriven = 0;
            _animSmooth = 0;
            _animStepped = 0;
            _animSteppedNames.Clear();
            _folNative = 0;
            _folSwapped = 0;
            _folOwnChannel = 0;
            _folNoChannel = 0;
            _folNoChannelNames.Clear();
            _admitNames.Clear();
            _pwRunPieceNames.Clear();
            // NB: the STEP counters are deliberately NOT reset here. An edge is a rare event —
            // seven fade-ON events in the whole ModBuild 254 session — and this census resets
            // every frame while the falsifier prints every two seconds, so per-frame counters
            // would miss essentially every step that ever happened. They are SESSION totals;
            // only the freshest names are rotated, so the line always carries the history.
            _headInMasonryWall = "-";
        }

        /// <summary>Record a discontinuity applied to a wall's own renderers at one END of the
        /// ramp: our property block going on (fade leaving 0) or coming off (fade reaching 0).
        /// The block asserts ToggleWallFade / _ToggleWallfade / _WallFade_On in one frame, so if
        /// opening that branch changes how the material shades, this is where it shows.</summary>
        private void NoteBlockEdge(Segment seg, bool installed)
        {
            if (installed)
            {
                _stepOutBlockInstalled++;
                if (_stepOutNames.Count >= PerWallNameCap)
                    _stepOutNames.RemoveAt(0); // keep the freshest, never grow unbounded
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _stepOutNames.Add($"'{wall}' block installed at fade {seg.Fade:F3}");
            }
            else
            {
                _stepInBlockCleared++;
            }
        }

        /// <summary>Record a MATERIAL SWAP taking effect or being undone. A swap replaces the
        /// piece's shader outright, so it is the largest possible single-frame change and the
        /// prime suspect whenever a transition reads as a pop in one direction only.</summary>
        private void NoteSwapEdge(Segment seg, MountedProp p, bool installed)
        {
            if (installed)
            {
                _stepOutSwapInstalled++;
                if (_stepOutNames.Count >= PerWallNameCap)
                    _stepOutNames.RemoveAt(0);
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                string piece = p.Renderer != null ? p.Renderer.name : "<dead>";
                _stepOutNames.Add($"'{piece}' on '{wall}' SWAPPED to the masonry fade shader "
                    + $"at fade {seg.Fade:F3}");
            }
            else
            {
                _stepInSwapRemoved++;
            }
        }

        /// <summary>
        /// R1 FALSIFIER, THE HALF THAT SEES EDGES. Silent when no step happened, so the line's
        /// presence is itself the finding.
        /// </summary>
        private void LogStepEdges()
        {
            int outs = _stepOutBlockInstalled + _stepOutSwapInstalled;
            int ins = _stepInBlockCleared + _stepInSwapRemoved;
            if (outs == 0 && ins == 0)
                return;
            VRLog.Info(Name,
                $"STEP (session totals): {outs} discontinuit(y/ies) applied at the START of a fade-OUT "
                + $"({_stepOutBlockInstalled} property block installed, {_stepOutSwapInstalled} "
                + $"material swap) and {ins} at the END of a fade-IN ({_stepInBlockCleared} block "
                + $"cleared, {_stepInSwapRemoved} swap removed). A step at the START of a "
                + "fade-out is visible on solid geometry and is what reads as a POP; the same "
                + "step at the end of a fade-in lands on already-solid geometry and is invisible. "
                + "ModBuild 256 made the block install itself VISUALLY inert: the cutoff ramp "
                + "now starts at -0.15, so the first observable frame (Fade ≈ 0.09, since "
                + "fadeStep is ~0.088 at 90 Hz) still carries a NEGATIVE cutoff and clips no "
                + "fragment at all. A non-zero out-edge count here is therefore expected and no "
                + "longer means a visible pop — what would still mean one is the material-swap "
                + "column being non-zero."
                + (_stepOutNames.Count > 0
                    ? " Most recent out-edges: " + string.Join(", ", _stepOutNames) + "."
                    : " No out-edge has been recorded yet this session."));
        }

        /// <summary>
        /// Record the dissolve channel ONE foliage piece actually received this frame — read off
        /// the record <c>EnsureDissolveChannel</c> just filled in, so it reports what the piece
        /// got rather than what it was meant to get. A piece with no channel at all cannot
        /// dissolve; it can only switch off, and it is named.
        /// </summary>
        private void NoteFoliageChannel(Segment seg, MountedProp p)
        {
            if (p.NativeFade)
            {
                if (p.SwapCopies != null)
                    _folSwapped++;
                else
                    _folNative++;
                return;
            }
            if (p.ColorId >= 0 || p.CutoffId >= 0 || p.DissolveControlId >= 0 || p.System != null)
            {
                _folOwnChannel++;
                return;
            }
            _folNoChannel++;
            if (_folNoChannelNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                string piece = p.Renderer != null ? p.Renderer.name : "<dead>";
                _folNoChannelNames.Add($"'{piece}' on '{wall}'");
            }
        }

        /// <summary>
        /// R2 FALSIFIER, second half. Record how ONE wall's pieces split between the occlusion
        /// numerator and the ground dressing the standing test excludes, and name the widest
        /// excluded piece — the one the user asked about directly
        /// (<i>"größere nicht begehbare Flächen … kann es sein, dass diese Flächen irgendeine
        /// Rolle bei dem Problem spielen?"</i>). Measured by re-reading the live bounds, so it
        /// reports the same verdict the numerator used.
        /// </summary>
        private void NoteAdmission(Segment seg)
        {
            if (!seg.HasBounds || seg.DoorRoot != null || _admitNames.Count >= PerWallNameCap)
                return;
            int admitted = 0, excluded = 0;
            float widestExcluded = 0f;
            string widestName = "-";
            CountAdmission(seg.Renderers, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            CountAdmission(seg.Foliage, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            CountAdmission(seg.Siblings, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            if (admitted == 0 && excluded == 0)
                return;
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            _admitNames.Add($"'{wall}' {admitted + excluded} piece(s), {excluded} FLAT"
                + (excluded > 0 ? $", widest flat '{widestName}' {widestExcluded:F1} wu" : ""));
        }

        private static void CountAdmission(List<MeshRenderer> list, ref int admitted,
            ref int excluded, ref float widestExcluded, ref string widestName)
        {
            for (int i = 0; i < list.Count; i++)
            {
                MeshRenderer r = list[i];
                if (r == null)
                    continue;
                Bounds b = r.bounds;
                if (IsStandingPiece(b))
                {
                    admitted++;
                    continue;
                }
                excluded++;
                float widest = Mathf.Max(b.size.x, b.size.z);
                if (widest > widestExcluded)
                {
                    widestExcluded = widest;
                    widestName = r.name;
                }
            }
        }

        /// <summary>
        /// Record ONE wall's own verdict, taken from its own coverage of its own room. Only
        /// decision-eligible walls are counted: a doorway, an engulfing segment, a boundless one
        /// or a wall whose room has no valid floor grid is held solid by a different rule, and
        /// counting those would let the falsifier claim independence it did not measure.
        /// </summary>
        private void NotePerWallVerdict(Segment seg)
        {
            if (!seg.HasBounds || seg.Engulfing || seg.DoorRoot != null
                || !RoomDecisionValid(seg.RoomIndex))
                return;
            // ModBuild 259: a split-run member is NOT an independent decider and counting it as
            // one is how "faded 1 of 46" came to describe four walls and forty-two fragments of a
            // fifth. Its own measurement is still printed — that is the number this round turned
            // on — but under the run that owns it, and the SPLIT RUN line carries the verdict.
            if (seg.RunDriven)
            {
                if (_pwRunPieceNames.Count < PerWallRunPieceCap)
                {
                    string piece = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    string owner = seg.RunOwner != null ? seg.RunOwner.name : "<orphan>";
                    _pwRunPieceNames.Add($"'{piece}' of run '{owner}' r{seg.RoomIndex} ema "
                        + $"{seg.Smooth:F2} blk {seg.LastBlocked}/{seg.LastRoomTotal} "
                        + (seg.State ? "FADED" : "solid") + " (verdict from the run)");
                }
                return;
            }
            _pwTotal++;
            if (seg.LastRoomTotal > 0)
                _pwCells = seg.LastRoomTotal;
            if (seg.State)
                _pwFaded++;
            if (seg.Smooth > _pwMaxSmooth)
            {
                _pwMaxSmooth = seg.Smooth;
                _pwMaxWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
            if (seg.Smooth < _pwMinSmooth)
            {
                _pwMinSmooth = seg.Smooth;
                _pwMinWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
            if (_pwNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                // PER-CELL ATTRIBUTION. "blocks 2 of 16" has been the unanswerable question in
                // every round since ModBuild 250; naming the cells and the piece that took the
                // first one settles whether a low reading is real floor or geometry residue.
                //
                // WHAT A CELL INDEX MEANS CHANGED IN ModBuild 258. It is still the room-relative
                // sample index in lattice order (ix outer, iz inner), but the sample now sits on
                // the nearest PLAYABLE HEX to that lattice position rather than on the lattice
                // position itself — so a cell can no longer be a corner of the room's bounding
                // rectangle with no tile under it. That is the whole ModBuild 257 defect: all
                // four green walls pinned at a different corner of that rectangle (#0,#1,#4,#8 /
                // #7,#11,#14,#15 / #2,#3 / #12), each taken by a prop standing off the field.
                // Cross-build comparisons of these indices against a pre-258 log are therefore
                // comparisons of two different point sets; the SAMPLE GRID line reports how far
                // each room's positions had to move. See WallSegmentFade.cs, RebuildSamples.
                string cells = seg.LastBlockedCells.Count == 0
                    ? "none"
                    : "#" + string.Join(",#", seg.LastBlockedCells);
                // WHICH LIST AND WHICH KIND OF HIT (ModBuild 257). ModBuild 256's name settled
                // WHAT decides each wall and could not settle WHERE the fix goes — 'Renderers'
                // means the tileset parented the piece under the wall run and the membership
                // classifier owns it, 'Stacked'/'Siblings' means an adoption sweep claimed it and
                // that sweep owns it. 'contains' vs 'ray' is the ratchet: a Contains hit does not
                // depend on the head, so every cell taken that way is a floor this wall can never
                // fall below. See Segment.LastBlockerList and Segment.LastContainsCells.
                string blocker = seg.LastBlockerPiece != null
                    ? $" first by '{seg.LastBlockerPiece.name}' [{seg.LastBlockerList}/"
                      + (seg.LastBlockerByContains ? "contains" : "ray") + "]"
                    : seg.LastBlockerList != "-" ? $" first by [{seg.LastBlockerList}]" : "";
                // The head-INDEPENDENT part of this wall's coverage, spelled out next to the bars
                // it is measured against. A wall whose contains-count alone clears the exit bar
                // is latched by construction and no amount of walking around can release it.
                string latch = seg.LastContainsCells > 0
                    ? $" ({seg.LastContainsCells} of them by Contains — head-INDEPENDENT, "
                      + $"a coverage floor of "
                      + (seg.LastRoomTotal > 0
                          ? (seg.LastContainsCells / (float)seg.LastRoomTotal).ToString("F2")
                          : "n/a")
                      + $" against exit bar {WallFadeTuning.Off:F2})"
                    : "";
                _pwNames.Add($"'{wall}' r{seg.RoomIndex} ema {seg.Smooth:F2} "
                    + $"blk {seg.LastBlocked}/{seg.LastRoomTotal} "
                    + (seg.State ? "FADED" : "solid")
                    + $" cells {cells}{blocker}{latch}");
            }
            NoteAdmission(seg);
            // Kept as an OBSERVATION after ModBuild 255 deleted the hard-1f shortcut it used to
            // feed. "Is his head actually in the masonry" is still the right question to be able
            // to answer when a wall dissolves unexpectedly — it just may not override a
            // measurement any more.
            if (_headInMasonryWall == "-" && seg.Bounds.Contains(_lastHeadPos)
                && HeadInsideWallMesh(seg, _lastHeadPos, out string hitMesh))
            {
                string w = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _headInMasonryWall = $"'{w}' (mesh '{hitMesh}')";
            }
        }

        /// <summary>Record where a wall's fade came from when it was not its own decision.</summary>
        private void NotePerWallOutcome(Segment seg, bool remoteFade, bool gateLift, int peerId)
        {
            if (seg.DoorRoot != null)
                return;
            if (remoteFade)
                _pwPeerDriven++;
            else if (gateLift && !seg.State)
                _pwGateDriven++;
        }

        /// <summary>
        /// Record which animation path a fade in flight actually took this frame. Called from
        /// <c>Apply</c>, so it reports the branch that ran rather than the branch intended.
        /// </summary>
        private void NoteAnimationPath(Segment seg, bool smooth)
        {
            if (smooth)
            {
                _animSmooth++;
                return;
            }
            _animStepped++;
            if (_animSteppedNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _animSteppedNames.Add($"'{wall}' "
                    + (seg.VariantLow && seg.VariantHigh ? "HIGH+LOW" : "LOW")
                    + $" fade {seg.Fade:F2}");
            }
        }

        /// <summary>
        /// R2 FALSIFIER (user ruling 2026-08-24: <i>"Ich will aber das jede Wand einzeln
        /// verschwinden kann und andere bleiben"</i>). One line that shows the walls DISAGREEING
        /// — every number measured on the pass that just ran, and the per-wall detail printed so
        /// the disagreement is visible rather than asserted.
        ///
        /// <para>THE NUMBER THAT MATTERS is "faded X of Y". The defect he reported has X equal to
        /// 0 or to Y on every single pass; independence means X spends time strictly between
        /// them. The session tallies below count exactly that, so one grep answers it: a session
        /// with <c>0 mixed</c> has reproduced the defect, whatever the rest of the line says.</para>
        ///
        /// <para>The coverage SPREAD (widest against narrowest, with names) is the second half of
        /// the proof: walls can only disagree about the verdict if they are being measured
        /// separately, and a spread of zero would mean they are not.</para>
        /// </summary>
        private void LogPerWallIndependence()
        {
            if (_pwTotal == 0 && _runsTotal == 0)
                return;
            if (_pwTotal == 0)
            {
                // Every decision-eligible segment this pass belongs to a split run. The
                // independence claim is then entirely the SPLIT RUN line's to make, and asserting
                // a spread over an empty population would be the ModBuild 252 mistake again.
                VRLog.Info(Name,
                    $"PER-WALL: 0 independently-deciding wall(s) this pass — all "
                    + $"{_runMembersTotal} decision-eligible piece(s) belong to "
                    + $"{_runsTotal} split run(s), of which {_runsFaded} faded. See the SPLIT RUN "
                    + "line; per-wall independence is a statement about RUNS, not about the "
                    + "fragments one run was carved into.");
                return;
            }
            bool mixed = _pwFaded > 0 && _pwFaded < _pwTotal;
            if (mixed)
                _pwMixedPasses++;
            else
                _pwUniformPasses++;
            float minSmooth = _pwMinSmooth == float.MaxValue ? 0f : _pwMinSmooth;
            VRLog.Info(Name,
                $"PER-WALL: faded {_pwFaded} of {_pwTotal} independently-deciding wall(s) this "
                + $"pass (plus {_runsFaded} of {_runsTotal} SPLIT RUN(s) driving "
                + $"{_runMembersTotal} piece(s) — ModBuild 259; the pieces are named at the end "
                + "of this line and no longer inflate the count, which is why Y dropped from 46) "
                + "— "
                + (mixed
                    ? "MIXED, so the walls are disagreeing and each one is deciding for itself"
                    : _pwFaded == 0
                        ? "all solid (unanimous: no wall's own coverage clears its bar)"
                        : "all faded (unanimous — check the spread below before calling it a "
                          + "global switch; unanimity is legitimate when every wall really is "
                          + "in the way)")
                + $". Session so far: {_pwMixedPasses} mixed pass(es) vs {_pwUniformPasses} "
                + "unanimous — a session that never goes mixed is the defect reported on "
                + "2026-08-24. Coverage spread this pass: widest "
                + $"'{_pwMaxWall}' {_pwMaxSmooth:F2}, narrowest '{_pwMinWall}' {minSmooth:F2}, "
                + $"bars {WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2}"
                // THE ARITHMETIC, SPELLED OUT. A threshold finer than the grid's own quantum
                // cannot be expressed: with 16 cells the quantum is 0.0625, so an exit bar of
                // 0.10 really means "at most ONE blocked cell", which is what made the old band
                // a one-way ratchet. Printing both numbers means the next reader checks this in
                // one line instead of deriving it from the source.
                + $" against a {_pwCells}-cell floor grid (quantum "
                + (_pwCells > 0 ? (1f / _pwCells).ToString("F4") : "n/a")
                + $" — the exit bar is {(_pwCells > 0 ? Mathf.CeilToInt(WallFadeTuning.Off * _pwCells) : 0)} "
                + "cell(s), the enter bar "
                + $"{(_pwCells > 0 ? Mathf.CeilToInt(WallFadeTuning.On * _pwCells) : 0)}). "
                + "Head inside masonry: "
                + $"{_headInMasonryWall} — an OBSERVATION; since ModBuild 255 this no longer "
                + "forces any wall's coverage to 1.00, the ray test measures what such a wall "
                + "actually covers. Not its own decision: "
                + $"{_pwPeerDriven} peer-driven, {_pwGateDriven} gate-lifted. Per wall: "
                + string.Join(" | ", _pwNames)
                + (_pwTotal > _pwNames.Count ? $" | +{_pwTotal - _pwNames.Count} more" : "")
                + ". Shape census — EVERY piece below is in the numerator (the standing-ratio "
                + "gate was retired in ModBuild 255: StripGroundRenderers already removes "
                + "anything topping out within 1.0 wu of the floor, and the ratio only ever "
                + "excluded real capstones). 'flat' here is a WARNING, not an exclusion: it "
                + "means something under 0.5 height-per-width survived the ground strip and can "
                + "claim samples through Contains(): " + string.Join(" | ", _admitNames)
                + (_pwRunPieceNames.Count > 0
                    ? ". Split-run pieces (measured individually, DECIDED by their run — "
                      + "ModBuild 259): " + string.Join(" | ", _pwRunPieceNames)
                    : ". No split-run piece was decision-eligible this pass.")
                + ".");
        }

        /// <summary>
        /// R1 FALSIFIER (user ruling 2026-08-24: <i>"Wände sollen niemals einfach so auftauchen
        /// und wieder verschwinden … IMMER mit der Animation, niemals ohne"</i>). Reports which
        /// animation path every fade IN FLIGHT actually took this frame — measured in
        /// <c>Apply</c>, not inferred.
        ///
        /// <para>A wall on the CONTINUOUS path sweeps one cutoff across the occluded map's
        /// world-Y gradient from fully solid to exactly the held look, with no texture swap and
        /// no step. A wall on the STEPPED path is a LOW or mixed shader variant, whose two
        /// branches do not meet at <c>Fade == 1</c>: it is fully clipped just below 1 and gets
        /// its foundation band back at 1. Those are named, because a non-zero stepped count is
        /// the remaining un-animated transition in the subsystem and it needs a shader change
        /// rather than a tuning.</para>
        ///
        /// <para>Silent when nothing is fading, so the line's presence already means a
        /// transition was in flight when it printed.</para>
        /// </summary>
        private void LogAnimationPaths()
        {
            int foliage = _folNative + _folSwapped + _folOwnChannel + _folNoChannel;
            if (_animSmooth == 0 && _animStepped == 0 && foliage == 0)
                return;
            VRLog.Info(Name,
                $"ANIMATION: {_animSmooth + _animStepped} wall renderer fade(s) in flight this "
                + $"frame — {_animSmooth} mid-DISSOLVE on the noise map (the only path that "
                + "yields intermediate pixels: m = 1-r varies per texel, so a rising cutoff "
                + $"retires the wall progressively), {_animStepped} sitting in the HELD state on "
                + "the occluded map, where the noise is multiplied by ZERO and the picture is "
                + "binary in the shader's world-Y term. The residual discontinuity is the "
                + "boundary between those two, at Fade==1: the foundation band winks as the map "
                + "swaps. ModBuild 252 tried to remove it by running the whole ramp on the "
                + "occluded map and made the ENTIRE wall binary instead — reverted in 255, "
                + "falsified by frame-by-frame video (single 33 ms step, zero intermediate "
                + "frames, in BOTH directions)"
                + (_animStepped > 0 && _animSteppedNames.Count > 0
                    ? ": " + string.Join(", ", _animSteppedNames)
                    : "")
                // THE HALF THE ModBuild 253 LINE DID NOT COUNT. It reported only the numbers
                // above, said "every transition in flight is animated end to end", and was
                // believed — while the largest population in the scene was measured by nobody.
                + $". FOLIAGE this frame: {foliage} attachment(s) — {_folNative} on the wall's "
                + $"own native ramp, {_folSwapped} on swapped masonry-fade copies, "
                + $"{_folOwnChannel} on their own alpha/cutoff/particle channel, "
                + (_folNoChannel == 0
                    ? "and 0 on the STAGGERED path."
                    : $"and {_folNoChannel} on the STAGGERED path — no material channel of their "
                      + "own, so each switches off at its own stable point of the ramp instead "
                      + "of being material-swapped (ModBuild 255: the swap was the pop). A high "
                      + "count here is EXPECTED and not a fault; what would be a fault is a "
                      + "non-zero swap count on the STEP line. Sample: "
                      + string.Join(", ", _folNoChannelNames)
                      + (_folNoChannel > _folNoChannelNames.Count
                          ? $", +{_folNoChannel - _folNoChannelNames.Count} more" : "")));
        }
        /// <summary>
        /// THE FALSIFIER. One line that can disagree with itself: the verdict, the term that
        /// decided it, the head against the board volume in world units AND in real metres, the
        /// live bars, how many walls the raised bar spared this evaluation, and the coverage of
        /// the worst offender among them. Every number is an outcome, not an intention.
        ///
        /// <para>The real-metre column is a CONVERSION of the world-unit numbers by the live rig
        /// scale and nothing in the decision reads it — the decision's bars are world units
        /// compared against world-unit geometry. It is here because "wie tief stehe ich drin" is
        /// unanswerable in world units at a zoom the player rides from 8.28 to 1.15.</para>
        /// </summary>
        private void LogInsideState(Vector3 headPos, float rigScale, bool edge)
        {
            if (!_boardVolumeValid)
                return;
            Vector3 mn = _boardVolume.min, mx = _boardVolume.max;
            bool byY = _lastInsideMarginY >= _lastInsideMarginXZ;
            string term = _insideBoard
                ? (byY ? "HEIGHT (the eye is below the wall crest, and that is the tighter of "
                       + "the two terms)"
                       : "FOOTPRINT (the head is over the board, and that is the tighter of the "
                       + "two terms)")
                : (byY ? "HEIGHT (the eye is at or above the wall crest)"
                       : "FOOTPRINT (the head is beyond the board edge)");
            string metres = rigScale > 1e-4f
                ? $"{(headPos.y - _boardFloorY) / rigScale:F2} m above the floor plane, walls "
                  + $"{_boardCrestWU / rigScale:F2} m tall, signed distance "
                  + $"{_lastInsideSigned / rigScale:F2} m"
                : "n/a (rig scale unreadable)";
            VRLog.Info(Name,
                $"INSIDE THE MAP: {(_insideBoard ? "YES" : "no")}"
                + $"{(edge ? " [EDGE]" : "")} — head "
                + $"({headPos.x:F1}, {headPos.y:F2}, {headPos.z:F1}) vs board volume "
                + $"[x {mn.x:F1}..{mx.x:F1}, y {mn.y:F2}..{mx.y:F2}, z {mn.z:F1}..{mx.z:F1}] "
                + $"world units, signed distance {_lastInsideSigned:F2} wu "
                + $"(slabs: Y {_lastInsideMarginY:F2}, XZ {_lastInsideMarginXZ:F2}); "
                + $"deciding term {term}. Crest {_boardCrestWU:F2} wu, bars enter "
                + $"<= {-InsideEnterDepthFraction * _boardCrestWU:F2} / leave "
                + $">= {InsideExitDepthFraction * _boardCrestWU:F2} wu. In real metres at rig "
                + $"scale {rigScale:F2} wu per metre: {metres}. "
                + "THIS VERDICT GATES NOTHING — it is an observation, not a policy (two builds "
                + "were spent proving a scene-wide switch cannot express a per-wall fact; see "
                + "the retirement record on WallSegmentFade.Inside.cs). Every wall is decided "
                + $"solely by its own coverage against the live bars {WallFadeTuning.On:F2}/"
                + $"{WallFadeTuning.Off:F2} — the PER-WALL line carries that pass's verdicts.");
        }
    }
}
