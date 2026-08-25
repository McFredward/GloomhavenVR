using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// PERF S4 (2026-08-25) — THE COMMIT'S READ HALF, TAKEN OFF THE COMMIT FRAME.
///
/// <para>WHAT THE MEASUREMENT SAID. PERF S3 shipped the per-phase instrument and the ModBuild
/// 271 hardware log answered it in one line: of a 123.78 ms commit, three phases own about
/// 90 % — <c>WallCache 61.89ms</c>, <c>PropUnits 29.94ms</c>, <c>Mounted 25.14ms</c> — and the
/// other twenty-one cost 32.2 ms across a whole five-second window between them. So the target
/// was never "the commit", it was those three, and this file is the answer to the two biggest.
/// </para>
///
/// <para>WHY NOT SLICE THE COMMIT ITSELF — the decision, stated once, here. The appliers run
/// EVERY frame off the segment table. A commit spread over frames is a table the appliers can
/// observe mid-rebuild, and this subsystem's whole defect history is that failure class: a wall
/// gone and the torch that hung on it still there, a floor stripped by a pass that had not run
/// yet, a prop with two owners. Concretely: <c>BeginRefresh</c> puts a wall's GROUND renderers
/// back into <c>seg.Renderers</c> and <c>StripGroundRenderers</c> (phase 12) is what takes them
/// out again, so a segment refreshed on frame N and stripped on frame N+9 is a wall that would
/// take its own floor with it for nine frames if anything looked. Holding the appliers off for
/// the duration does prevent that — and it costs the SAME wall-clock stall it removes, because
/// spreading work does not shrink it: 107 ms of commit at a 1.5 ms/frame budget is a 0.8 s
/// window in which no wall may change its fade. That is a worse artefact than the one being
/// fixed, in a subsystem whose fade time constant is 0.12 s.
///
/// So the commit stays ATOMIC — all twenty-four phases, exactly as PERF S3 left them — and what
/// moves off the commit frame is the work that does not have to be there at all: the pure READ
/// derivation the commit performs on its way to a decision. See THE PREPARE INVARIANT below.
/// </para>
///
/// <para>WHERE THE 62 ms IN <c>WallCache</c> ACTUALLY GOES, and this is read off the source, not
/// guessed. <c>CommitWallCache</c> → <c>RefreshSegment</c> → <c>CollectWallFadeInfo</c> asks
/// <see cref="FadeDriver.IsStandingFigureProp"/> for EVERY child renderer of EVERY cache wall —
/// the one choke point every wall-renderer collection path goes through. That predicate's
/// prologue is four memo-backed derivations, and the expensive one is
/// <c>MeasureStandingUnit</c>: a <c>GetComponentsInChildren(includeInactive: true)</c> over the
/// prop's whole subtree plus a material walk and a bounds union per piece. Its memo
/// (<c>_standingUnitMemo</c>) is per-rescan, so every one of those walks is paid inside the
/// commit frame. The unit-root climb underneath it (<c>PropUnitRootOf</c>) is two subtree walks
/// PER LEVEL, and it is the same climb <c>PropUnits</c> pays for a second time through
/// <c>_propUnitRootMemo</c>.</para>
///
/// <para>THE PREPARE INVARIANT — <b>PREPARE MAY WRITE NOTHING BUT MEMOS.</b> The stage below
/// calls the SAME predicates the commit calls, with the SAME arguments, and throws every answer
/// away. What it keeps is what those predicates cache on the way: <c>_standingRootMemo</c>,
/// <c>_standingRootCutMemo</c>, <c>_standingUnitMemo</c>, <c>FigureAncestryMemo</c>,
/// <c>_shaderVerdict</c> / <c>_shaderFoliageVerdict</c>, and this file's own
/// <see cref="FadeDriver._propUnitRootPrewarm"/>. Every one of those is a pure function of the
/// scene; not one of them is a renderer, a material, a property block or a segment field. The
/// commit therefore reads exactly what it read before — its inputs are all still taken live —
/// and simply finds the expensive derivations already answered.
///
/// <para>HOW A REVIEWER CHECKS IT. Read <see cref="FadeDriver.StepPrepare"/> top to bottom: its
/// only statements are cursor arithmetic, a clock read, and calls to
/// <see cref="FadeDriver.WarmStandingUnit"/> and <see cref="FadeDriver.WarmPropUnitRoot"/>.
/// Follow those two: the first is <see cref="FadeDriver.ResolveStandingUnit"/>, which is
/// literally the first five statements of <see cref="FadeDriver.IsStandingProp"/> extracted so
/// there is ONE copy (this project has paid for a second fan wearing the same name); the second
/// writes one dictionary. Neither reaches a <c>Segment</c>, a <c>Renderer</c> or a
/// <c>MaterialPropertyBlock</c>. The commit's atomicity is unchanged, so the applier question
/// does not arise at all — which is why it is answered by construction rather than by a
/// gate.</para></para>
///
/// <para>THE THREE ORDERING GATES, and why the answers are bit-identical rather than merely
/// similar. Gates 2 and 3 are asked at the top of the stage AND again on the commit frame that
/// consumes it (<see cref="FadeDriver.VerifyPrepareStillValid"/>), because a stage that spans
/// frames must not be let through by a capability test taken before the world moved.</para>
/// <list type="number">
/// <item>THE STANDING WARM SEES THE SAME RECTS THE COMMIT WOULD. <c>MeasureStandingUnit</c>
///   consults <c>_waterRects</c> (rebuilt in phase 9, <c>Water</c>) and <c>_archRects</c> (phase
///   7, <c>GateSeed</c>) — both AFTER <c>WallCache</c> (phase 5). So a unit first measured by
///   the wall-cache pass already reads the PREVIOUS cycle's rects today, and a prepare-stage
///   measurement reads the same previous cycle's rects. The warm is deliberately restricted to
///   the wall cache's own subtree renderers for exactly this reason: warming the adoption
///   sweep's candidates as well would move units that are first measured in phase 8 from fresh
///   rects to stale ones, which is a look change and is refused.</item>
/// <item>THE FLOOR PLANES MUST NOT HAVE MOVED. <c>MeasureStandingUnit</c> also reads
///   <c>_roomFloorY</c>, which <c>CommitTileAnchors</c>/<c>CommitRoomRegistry</c> (phases 2–3)
///   rebuild BEFORE <c>WallCache</c> — so here the commit does see fresh planes and a prepare
///   stage would not. The planes only move when a room is revealed, and a reveal shows up as
///   <c>m_RoomRenderers.Count != _builtRoomCount</c>. <see cref="FadeDriver.BeginPrepareStage"/>
///   therefore refuses to warm at all while that inequality holds, and the budget line COUNTS
///   the refusals. A refused cycle costs one slow commit, which is what every cycle costs
///   today.</item>
/// <item>AND THE BOARD MUST NOT HAVE MOVED UNDER THOSE PLANES — the same argument for the
///   other way the pair can come apart, with its own probe and its own count. See
///   <see cref="FadeDriver.BoardStillWhereTheFloorPlanesSayItIs"/>.</item>
/// </list>
///
/// <para>THE PROP-UNIT PREWARM IS SEPARATELY GATED. <c>PropUnitRootOf</c>'s answer depends on
/// <c>_propUnitAnchors</c> — the walk stops AT a segment anchor — and that set is re-read from
/// the FINAL table at the top of the prop-unit pass, which is why
/// <c>BeginPropUnitScope</c> drops <c>_propUnitRootMemo</c> wholesale. Prepare cannot know the
/// final table, so it warms into a SEPARATE dictionary against the last committed table's
/// anchors and records that set. <c>BeginPropUnitScope</c> adopts the prewarm only when the two
/// anchor sets are equal — the steady-state case — and drops it otherwise, again counted. The
/// memo's own lifetime is unchanged: it is still cleared once per rescan and still holds
/// transform keys for no longer than one cycle.</para>
///
/// <para>WHAT THIS DOES NOT DO, said plainly so the next round does not have to rediscover it.
/// <c>Mounted</c> (25 ms) is untouched: its cost is a sweep over the census whose every input —
/// <c>_mountedOwned</c>, <c>_attachmentOwned</c>, the segment reach rect — is produced by the
/// phases immediately before it, so there is nothing to hoist ahead of the commit. The residue
/// of <c>WallCache</c> and <c>PropUnits</c> (material walks, the census notes, the list
/// arithmetic) stays on the commit frame too. The budget line's new PREPARE clause is what says
/// how much actually moved, and its <c>WORST SINGLE FRAME across all stages</c> is still the
/// number the whole exercise is judged by.</para>
///
/// <para>MULTIPLAYER: presentation only. Nothing here reads or writes the wire, and no game
/// state is written from any of it — the stage's entire output is six dictionaries.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver : MonoBehaviour
    {
        /// <summary>Millisecond budget the PREPARE stage may spend on one frame. Deliberately
        /// the SAME number as <see cref="ClassifyBudgetMillis"/>, and for the same reason: this
        /// is the same shape of work (a per-renderer derivation over a fixed population, with no
        /// externally visible effect), and 1.5 ms against an 11.11 ms frame is the figure this
        /// project has already validated on hardware for that shape — the ModBuild 228 and 271
        /// logs both show the census holding at it over 8–18 frames with no stall attributed to
        /// it. A budget is used rather than an item count because the population is not fixed
        /// across scenarios (this one classifies 5803 renderers and 1888 fade-capable), and a
        /// count tuned on one scenario is a stall on the next.</summary>
        private const float PrepareBudgetMillis = 1.5f;

        /// <summary>Budget on a ROOM-REVEAL cycle, mirroring
        /// <see cref="ClassifyUrgentBudgetMillis"/> — a reveal already coincides with the game's
        /// own room-generation hitch, so spending more there is free. Note that a reveal cycle
        /// normally does no warming at all (gate 2 in the class header); this only bounds the
        /// frames on which the stage still has bookkeeping to do.</summary>
        private const float PrepareUrgentBudgetMillis = 6f;

        /// <summary>How many renderers to warm between two clock reads. Far smaller than
        /// <see cref="ClassifyChunk"/> on purpose: a classify item is a handful of field reads,
        /// a warm item can be a whole subtree walk, so the same chunk size would overshoot the
        /// budget by an order of magnitude on its first miss.</summary>
        private const int PrepareChunk = 8;

        /// <summary>The wall cache as it stood when this cycle's prepare stage opened. Copied
        /// rather than iterated live because the stage suspends across frames; the commit reads
        /// <c>ProceduralWall.m_WallCache</c> LIVE exactly as before, so a wall built while the
        /// stage was running is not missed by anything — it merely warms nothing and pays its
        /// derivation on the commit frame, which is today's cost.</summary>
        private readonly List<ProceduralWall> _prepWalls = new(64);

        /// <summary>The current wall's child renderers, refilled when the wall cursor advances.
        /// Its own list: <c>_subtreeScratch</c> and <c>_propUnitWalkScratch</c> are both walked
        /// by code this stage calls into.</summary>
        private readonly List<MeshRenderer> _prepRenderers = new(64);

        /// <summary>Cursor into <see cref="_prepWalls"/> and into <see cref="_prepRenderers"/>.
        /// The pair IS the stage's whole resumable state.</summary>
        private int _prepWallCursor;
        private int _prepRendererCursor;

        /// <summary>False until the cursor's wall has had its renderer list taken.</summary>
        private bool _prepRenderersTaken;

        /// <summary>Set by <see cref="BeginPrepareStage"/> when gate 2 (the room registry has
        /// not moved since the last commit) holds. False means the stage does no warming at all
        /// this cycle and the commit runs exactly as it does today.</summary>
        private bool _prepWarmArmed;

        /// <summary>The prop-unit root answers this cycle's prepare stage derived, against
        /// <see cref="_prepAnchors"/>. Adopted into <c>_propUnitRootMemo</c> by
        /// <c>BeginPropUnitScope</c> only if the final table's anchor set is equal to it — see
        /// the class header. Separate from the memo so an unequal set costs a discard rather
        /// than a wrong answer.</summary>
        private readonly Dictionary<Transform, Transform?> _propUnitRootPrewarm = new(256);

        /// <summary>The segment-anchor set <see cref="_propUnitRootPrewarm"/> was derived
        /// against. Compared, not trusted.</summary>
        private readonly HashSet<Transform> _prepAnchors = new(64);

        // ---- cycle accounting for the budget line ------------------------------------------

        /// <summary>Frames this window's prepare stages were spread over, and the worst single
        /// one. Reset by <see cref="LogRescanBudget"/> with every other counter it prints.</summary>
        private int _cyclePrepareFrames;
        private float _cycleWorstPrepareMillis;

        /// <summary>Total milliseconds the prepare stage spent in this window — the ONE number
        /// that says how much work actually moved off the commit frame, and therefore the number
        /// the next hardware log is read against: if the commit's own total does not fall by
        /// roughly this much, the warm is being recomputed rather than hit and the design is
        /// wrong. It is the falsifier for this whole change.</summary>
        private float _cyclePrepareTotalMillis;

        /// <summary>Renderers warmed and prop-unit roots derived in this window.</summary>
        private int _cyclePrepWarmedRenderers;
        private int _cyclePrepWarmedRoots;

        /// <summary>FAILURES, counted separately so the line can say which gate refused rather
        /// than merely that nothing happened. A dead instrument and a live one that found
        /// nothing must never print the same text.</summary>
        private int _cyclePrepRefusedReveal;   // gate 2 — the room registry moved
        private int _cyclePrepRefusedBoard;    // gate 3 — the board moved under the planes
        private int _cyclePrepDroppedAnchors;  // the prewarm was discarded: anchor set changed

        /// <summary>Where the first live room renderer stood when <c>CommitRoomRegistry</c> last
        /// built <c>_roomFloorY</c> — gate 3's reference. See
        /// <see cref="BoardStillWhereTheFloorPlanesSayItIs"/>.</summary>
        private Vector3 _prepBoardProbePos;
        private bool _prepBoardProbeValid;

        /// <summary>
        /// GATE 3 — HAS THE BOARD MOVED SINCE THE FLOOR PLANES WERE MEASURED?
        ///
        /// <para>WHY IT IS NEEDED AND GATE 2 IS NOT ENOUGH. The standing rule is a COMPARISON:
        /// <c>MeasureStandingUnit</c> takes a prop's live union AABB and asks
        /// <c>NearestAnchoredFloorY</c> for the plane to measure it against. Inside the commit
        /// both sides are from the same instant, because <c>CommitRoomRegistry</c> rebuilt the
        /// planes four phases earlier. A prepare-stage measurement pairs a LIVE prop box with
        /// the planes cached at the LAST commit — up to two seconds old — and this mod moves the
        /// board (table height, tilt, the board-move detection at the end of
        /// <c>CommitRoomRegistry</c> exists for exactly that). Pair a moved prop with an unmoved
        /// plane and a floor-standing statue reads as airborne, which is a statue the wall
        /// system may then claim as masonry: the most expensive defect class this subsystem
        /// has.</para>
        ///
        /// <para>THE PROBE. One <c>Transform.position</c> read on the first live room renderer —
        /// board geometry, the same list the planes are derived from — against the reading taken
        /// in the phase that built those planes. The epsilon is the one the board-move detector
        /// beside it already uses. It FAILS CLOSED: no probe, no warm.</para>
        /// </summary>
        private bool BoardStillWhereTheFloorPlanesSayItIs(TilesOcclusionGenerator gen)
        {
            if (!_prepBoardProbeValid)
                return false;
            foreach (MeshRenderer probe in gen.m_RoomRenderers)
            {
                if (probe == null)
                    continue;
                return (probe.transform.position - _prepBoardProbePos).sqrMagnitude <= 0.0001f;
            }
            return false;
        }

        /// <summary>Open the stage. Cheap and atomic: one list copy and one gate.</summary>
        private void BeginPrepareStage(TilesOcclusionGenerator gen)
        {
            _prepWallCursor = 0;
            _prepRendererCursor = 0;
            _prepRenderersTaken = false;
            _prepWalls.Clear();
            _prepRenderers.Clear();
            _propUnitRootPrewarm.Clear();
            _prepAnchors.Clear();

            // GATE 2 (class header): the standing rule measures a prop's foot against its room's
            // anchored floor plane, and those planes are rebuilt by phases 2–3 — i.e. AFTER this
            // stage but BEFORE the wall-cache pass that would consume the warm. They only move
            // when a room is revealed, and that is exactly the inequality below. Refuse rather
            // than warm against a plane the commit will not agree with: a mis-measured standing
            // unit is a statue the wall system may claim as masonry, which is the single most
            // expensive defect class this subsystem has.
            if (gen.m_RoomRenderers.Count != _builtRoomCount)
            {
                _prepWarmArmed = false;
                _cyclePrepRefusedReveal++;
                return;
            }
            // GATE 3 — and the planes must still be under the board they were measured on.
            if (!BoardStillWhereTheFloorPlanesSayItIs(gen))
            {
                _prepWarmArmed = false;
                _cyclePrepRefusedBoard++;
                return;
            }
            _prepWarmArmed = true;

            // The MEMO half of the standing-prop scope opens HERE rather than at the top of the
            // commit, because the memos it clears are the ones this stage fills. It is a pure
            // state reset — no renderer, material or segment is touched by it — and the table it
            // reads (RefreshPropUnitAnchors, over _segments) is the last COMMITTED one, which is
            // bit-identically what it read at the top of the commit, since every writer of
            // _segments is a commit phase. Two things deliberately do NOT move with it: the
            // CENSUS half (it would leave the heartbeat's rolls empty for the length of the
            // stage — see BeginStandingMemoScope) and the per-node fact window (see StepPrepare
            // for the per-frame one it opens instead).
            BeginStandingMemoScope();
            _standingScopeOpen = true;
            foreach (Transform t in _propUnitAnchors)
                _prepAnchors.Add(t);

            for (int i = 0; i < ProceduralWall.m_WallCache.Count; i++)
            {
                ProceduralWall w = ProceduralWall.m_WallCache[i];
                if (w != null)
                    _prepWalls.Add(w);
            }
        }

        /// <summary>
        /// Advance the warm within this frame's budget. Returns true when the stage is done and
        /// the cycle may move on to the commit.
        ///
        /// <para>READ THE INVARIANT IN THE CLASS HEADER BEFORE ADDING A STATEMENT HERE. Nothing
        /// in this method — or in anything it calls — may write a <c>Segment</c> field, a
        /// renderer, a material or a property block. The commit's atomicity is what keeps the
        /// appliers from ever seeing a half-built table, and it only holds while this stage
        /// stays a pure derivation.</para>
        /// </summary>
        private bool StepPrepare(float frameStart, float budget)
        {
            if (!_prepWarmArmed)
                return true;

            // The two ancestry memos this stage's predicates use are contracted to be EMPTY at
            // every frame boundary (see FigureAncestryMemo and _nodeRendererCount): they are
            // valid only within one synchronous pass, because nothing can re-parent an actor or
            // activate a subtree while one is running. A prepare slice IS one such pass, so it
            // opens and closes both windows around itself. The commit continues to open its own,
            // and neither window is widened by a single frame.
            BeginFigureMemo();
            ClearNodeFactMemos();
            try
            {
                int sinceClock = 0;
                while (_prepWallCursor < _prepWalls.Count)
                {
                    if (!_prepRenderersTaken)
                    {
                        _prepRenderers.Clear();
                        ProceduralWall wall = _prepWalls[_prepWallCursor];
                        if (wall != null)
                            wall.GetComponentsInChildren(includeInactive: false, _prepRenderers);
                        _prepRenderersTaken = true;
                        _prepRendererCursor = 0;
                    }
                    while (_prepRendererCursor < _prepRenderers.Count)
                    {
                        MeshRenderer r = _prepRenderers[_prepRendererCursor++];
                        if (r == null)
                            continue;
                        // (1) The standing-prop prologue — the choke point every wall-renderer
                        //     collection path goes through, and the expensive half of the
                        //     wall-cache phase. Restricted to the wall cache's own subtrees; see
                        //     gate 1 in the class header for why the adoption sweep's candidates
                        //     are deliberately NOT warmed.
                        WarmStandingUnit(r);
                        _cyclePrepWarmedRenderers++;
                        // (2) The unit-root climb the prop-unit pass pays a second time. Its own
                        //     dictionary, adopted only against an equal anchor set.
                        if (r.transform.parent != null)
                            WarmPropUnitRoot(r.transform.parent);
                        if (++sinceClock < PrepareChunk)
                            continue;
                        sinceClock = 0;
                        if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget)
                            return false;
                    }
                    _prepWallCursor++;
                    _prepRenderersTaken = false;
                    if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget)
                        return false;
                }
            }
            finally
            {
                EndNodeFactMemos();
                EndFigureMemo();
            }
            // Accumulated, like every other counter the budget line owns — a window figure that
            // showed only the last cycle would disagree with the renderer count beside it.
            _cyclePrepWarmedRoots += _propUnitRootPrewarm.Count;
            return true;
        }

        /// <summary>Derive and cache one parent's unit root against
        /// <see cref="_propUnitAnchors"/>, into the prewarm rather than into the live memo.
        /// The ONE writer of <see cref="_propUnitRootPrewarm"/>.</summary>
        private void WarmPropUnitRoot(Transform parent)
        {
            if (_propUnitRootPrewarm.ContainsKey(parent))
                return;
            _propUnitRootPrewarm[parent] = PropUnitRootOf(parent);
        }

        /// <summary>
        /// RE-ASK GATE 3 ON THE COMMIT FRAME, and drop the warm if the answer changed.
        ///
        /// <para>The stage spans frames, so a board move can land BETWEEN the gate and the
        /// commit that consumes what the gate allowed. Asking once at the top would be a
        /// capability test standing in for a policy — a mistake with its own entry in this
        /// project's ledger. The remedy is total and cheap: re-opening the standing scope drops
        /// exactly the three memos the warm filled, and the commit then derives every one of
        /// them live, which is what every commit did before PERF S4. Counted, so a log in which
        /// this fires constantly says so rather than quietly costing the whole saving.</para>
        /// </summary>
        private void VerifyPrepareStillValid(TilesOcclusionGenerator gen)
        {
            if (!_prepWarmArmed || BoardStillWhereTheFloorPlanesSayItIs(gen))
                return;
            _cyclePrepRefusedBoard++;
            BeginStandingMemoScope();   // drops _standingUnitMemo / _standingRootMemo / …
        }

        /// <summary>Drop this cycle's prepare state. Called from <c>AbandonRescanCycle</c> (a
        /// scene change, a teardown) and after the commit has consumed the warm — a stage that
        /// held transform references past its cycle would be the dangling-reference defect
        /// <c>WallSegmentFade.Standing.cs</c> already paid for.</summary>
        private void ClearPrepareState()
        {
            _prepWalls.Clear();
            _prepRenderers.Clear();
            _prepWallCursor = 0;
            _prepRendererCursor = 0;
            _prepRenderersTaken = false;
            _prepWarmArmed = false;
        }

        /// <summary>Append the PREPARE clause to the budget line. Live numbers on every field —
        /// a change-gated line with a constant reason prints once and then reads as a dead
        /// instrument, so every term here is a count taken this window, including the two
        /// refusal counts (a stage that never ran and a stage that ran and found nothing must
        /// not print the same text).</summary>
        private void AppendPrepareClause(System.Text.StringBuilder sb)
        {
            sb.Append(" PREPARE (read-only warm, off the commit frame): ")
              .Append(_cyclePrepareFrames).Append(" frame(s) at ")
              .Append(PrepareBudgetMillis.ToString("0.0")).Append("ms/frame, worst ")
              .Append(_cycleWorstPrepareMillis.ToString("F2")).Append("ms, ")
              .Append(_cyclePrepareTotalMillis.ToString("F1"))
              .Append("ms total MOVED OFF THE COMMIT FRAME (read the commit's phase totals "
                    + "below against this: they must fall by about this much, or the warm is "
                    + "being recomputed instead of hit); ")
              .Append(_cyclePrepWarmedRenderers).Append(" wall-cache renderer(s) warmed, ")
              .Append(_cyclePrepWarmedRoots).Append(" unit root(s) prewarmed; REFUSED ")
              .Append(_cyclePrepRefusedReveal)
              .Append(" cycle(s) because the room registry moved (a reveal — the floor planes "
                    + "the standing rule measures against are rebuilt inside the commit) and ")
              .Append(_cyclePrepRefusedBoard)
              .Append(" because the board itself had moved out from under those planes; ")
              .Append("DROPPED the prop-unit prewarm on ")
              .Append(_cyclePrepDroppedAnchors)
              .Append(" cycle(s) because the final table's segment-anchor set differed from the "
                    + "one it was derived against. A refused or dropped cycle costs one slow "
                    + "commit, which is what every cycle cost before PERF S4.");
        }

        // =================================================================================
        // PERF S5 (2026-08-25) — THE CYCLE THAT DOES NOT COMMIT
        // =================================================================================
        //
        // WHAT THE ModBuild 274 HARDWARE LOG ACTUALLY SAYS, and it is not what PERF S4 was
        // briefed against. Over 22 BUDGET windows in the big scenario the commit's three
        // expensive phases report, per window of 3 cycles:
        //
        //     WallCache  94.2 - 95.3ms total   (31.5ms per cycle, spread under 1ms)
        //     PropUnits  89.6 - 90.8ms total   (30.0ms per cycle, spread under 1ms)
        //     Mounted    64.3 - 77.2ms total   (22 - 26ms per cycle)
        //     other 21   ~29ms total           (~10ms per cycle)
        //
        // That is ~95ms EVERY cycle, not a spike with a cheap median: the 92 / 104 / 120ms
        // spread in WORST COMMIT is variance around a constant, not an occasional stall. And
        // in the same 22 windows the PREPARE clause reports 12819 wall-cache renderers warmed
        // and 3105 unit roots prewarmed — the SAME two integers in every single window, for
        // 66 consecutive cycles. The scene population (5803 renderers, 1888 fade-capable) does
        // not move either.
        //
        // So the subsystem spends ~95ms every two seconds rebuilding a table that sixty-six
        // times running came out the same. PERF S4 correctly hoisted 49.8ms of derivation off
        // the commit frame and the stall did not go away, because hoisting shrinks the work a
        // frame does, not the work that has to be done. THIS stage removes the work.
        //
        // THE SKIP INVARIANT — A CYCLE MAY BE SKIPPED ONLY WHEN NOTHING THE COMMIT READS HAS
        // MOVED. The survey below re-derives a 64-bit signature of every input the commit's
        // OUTPUT is a function of, compares it against the signature the table IN FORCE was
        // built from, and skips the commit only on equality. The comparison is conservative in
        // one direction by construction: both halves of the signature are taken EARLIER in the
        // cycle than the commit that banks them, so a world that moves between the survey and
        // the commit banks a stale hash — and the next cycle's live survey then disagrees with
        // it and commits. The hash can cause an unnecessary commit; it cannot cause a wrong
        // skip.
        //
        // WHAT IS IN THE SIGNATURE, and where each term is taken:
        //   (1) THE SCENE HALF, folded in ClassifySlice (WallSegmentFade.cs) over all 5803
        //       snapshot entries: each renderer's instance ID, its liveness, its `enabled`, and
        //       the seven verdict bits the four collection passes read off the fact table. This
        //       is the input to Adopt, Water, Stacked and Mounted. Combined COMMUTATIVELY — see
        //       _sceneFactSigSum for why an ordered fold would be defeated by the sweep.
        //   (2) THE WALL HALF, folded by StepSurvey below over the ProceduralWall cache: each
        //       wall's identity, whether it is a split anchor, its live child-renderer count and
        //       every child's identity. GetComponentsInChildren(includeInactive: false) is the
        //       same call CommitWallCache makes, so a subtree that was activated or deactivated
        //       moves this hash. This is the input to WallCache and PropUnits.
        //
        //   NEITHER HALF FOLDS `renderer.enabled`, and that is the single most important
        //   decision in this file after the invariant itself: this subsystem HIDES THINGS BY
        //   WRITING `enabled` and never calls SetActive, so a signature that read `enabled`
        //   would move on every fade and unfade and the skip would fire almost never. Both
        //   halves therefore key on identity plus activeInHierarchy — what the GAME changes —
        //   and are blind to what the fade system itself writes. See the long note at the fold
        //   site in ClassifySlice.
        //   (3) THE THREE WORLD GATES, asked as scalars: the room registry has not moved (no
        //       reveal), the board is still where the floor planes say it is (the probe PERF S4
        //       already ships), and nothing zeroed _nextRescan to ask for this cycle.
        //   (4) THE DRIFT PROBE, which measures rather than assumes — see
        //       SegmentBoundsStillWhereTheCommitLeftThem.
        //
        // WHY NOT DOUBLE-BUFFER THE COMMIT INSTEAD, said once, here. Building the new table in
        // a second structure and swapping it atomically requires all twenty-four phases to
        // write into that structure rather than into _segments. Two of them —
        // EnforcePropUnitCohesion (30ms) and CollectWallMountedProps (24ms), i.e. 54 of the
        // 95ms — live in WallSegmentFade.PropUnit.cs and WallSegmentFade.Mounted.cs, and both
        // mutate segments in place (moving renderers between owners, hiding and restoring
        // pieces). A "second table" that hands those two phases the same Segment objects the
        // appliers are reading is not double-buffered at all; it is the mid-rebuild table the
        // commit's atomicity exists to prevent, with a swap bolted on. The skip needs none of
        // that: it does not change what a commit does, only how often one is needed.
        //
        // MULTIPLAYER: presentation only. Nothing here reads or writes the wire, and the skip
        // path writes no game state, no renderer, no material and no segment field.

        /// <summary>Millisecond budget the SURVEY stage may spend on one frame — the same
        /// figure as <see cref="ClassifyBudgetMillis"/> and <see cref="PrepareBudgetMillis"/>,
        /// and for the same reason: it is the same shape of work (a per-renderer read over a
        /// fixed population with no externally visible effect) at the value this project has
        /// already validated on hardware for that shape.</summary>
        private const float SurveyBudgetMillis = 1.5f;

        /// <summary>Renderers folded between two clock reads. Much larger than
        /// <see cref="PrepareChunk"/> because a survey item is an instance-ID read and two
        /// multiplies, not a subtree walk — the same argument that sets
        /// <see cref="ClassifyChunk"/>, in the same direction.</summary>
        private const int SurveyChunk = 64;

        /// <summary>
        /// THE STALENESS CEILING — how many cycles in a row may be skipped before one commit is
        /// forced whatever the signature says.
        ///
        /// <para>It is a FAIL-SAFE for a blind spot in the signature, not part of the design. If
        /// the signature is complete this never fires and the next hardware log's SKIP clause
        /// says so with a live count; if it fires, that count is the first evidence that some
        /// input is not covered, and it names the one number to chase. Thirty cycles is 60s
        /// against a table the subsystem already accepts as up to 2s stale.</para>
        ///
        /// <para>IT IS ALSO THE ONE THING THAT CAN STILL PRODUCE A PERIODIC STALL, so it is
        /// stated plainly rather than buried: while it fires, the player sees one ~95ms commit
        /// per minute instead of one per two seconds. Read the ceiling count on the BUDGET line
        /// before concluding anything about the remaining hitches.</para>
        /// </summary>
        private const int MaxSkippedCyclesInARow = 30;

        /// <summary>How many segments the drift probe re-measures per skipped cycle. Sixteen
        /// segments of ~10 members is ~160 <c>Renderer.bounds</c> reads, tens of microseconds —
        /// and the ring is walked round-robin, so a table of N segments is covered completely
        /// every ceil(N/16) skipped cycles, well inside the ceiling above.</summary>
        private const int BoundsProbeSegmentsPerCycle = 16;

        /// <summary>World units a segment's collection AABB may drift before the table is
        /// declared stale. Half of <see cref="BlockEpsMinWorld"/>: below that the blocked-test
        /// epsilon the bounds feed does not change at all, so a smaller value would commit on
        /// motion no decision can see.</summary>
        private const float BoundsDriftEpsilonWU = 0.05f;

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        /// <summary>One FNV-1a step. Order-SENSITIVE on purpose: both halves of the signature
        /// are folded by deterministic walks (snapshot order, wall-cache order, subtree order),
        /// so a reordering is a real change and must move the hash. An order-independent
        /// combiner would miss exactly the case where two renderers swap owners.</summary>
        private static ulong FoldSig(ulong sig, int value)
        {
            unchecked
            {
                sig ^= (uint)value;
                sig *= FnvPrime;
            }
            return sig;
        }

        /// <summary>The wall cache as it stood when this cycle's survey opened. Copied for the
        /// same reason <see cref="_prepWalls"/> is: the stage suspends across frames.</summary>
        private readonly List<ProceduralWall> _surveyWalls = new(64);
        private readonly List<MeshRenderer> _surveyRenderers = new(64);
        private int _surveyWallCursor;
        private int _surveyRendererCursor;
        private bool _surveyRenderersTaken;

        /// <summary>The wall half of this cycle's signature, folded as the walk proceeds.</summary>
        private ulong _surveySig;

        /// <summary>
        /// The scene half, folded by <c>ClassifySlice</c> and reset by <c>BeginRescanCycle</c>.
        ///
        /// <para>TWO ACCUMULATORS, AND COMMUTATIVE ONES, unlike the wall half. The scene half is
        /// folded over the <c>FindObjectsOfType&lt;Renderer&gt;</c> snapshot, and Unity does not
        /// specify that sweep's ORDER. A warm cycle re-reads the same array so the order is
        /// stable there, but one cycle in three takes a fresh sweep (the ModBuild 274 log:
        /// "1 of them from a FRESH ... sweep" in every window of three), and an order-sensitive
        /// fold over a re-ordered array of the SAME renderers reads as a changed scene — which
        /// would make this term refuse on every sweep cycle and cap the whole change at a third
        /// of its value. Each renderer therefore contributes one self-contained hash, combined
        /// by ADD and by XOR: the sum alone can be defeated by a compensating pair, the xor
        /// alone by a repeated one, and neither failure survives both.</para>
        /// </summary>
        private ulong _sceneFactSigSum;
        private ulong _sceneFactSigXor;

        /// <summary>ModBuild 279 (Option A) — THE NARROWED SCENE HALF, always computed, used
        /// only while <see cref="WallFadeTuning.FigureExemptSkipOn"/> is set.
        ///
        /// <para>Identical to the full half except for one class of renderer: for a
        /// <see cref="RendererFact.Figure"/> the <c>activeInHierarchy</c> bit is replaced by a
        /// FIGURE bit. See the fold site in <c>ClassifySlice</c> for the term, for what it
        /// deliberately does NOT drop, and for the honest limits of the exemption.</para>
        ///
        /// <para>COST, so it is a number rather than a claim: two 64-bit multiplies and two
        /// accumulator updates per renderer, inside the CLASSIFY stage, which is resumable and
        /// budgeted at a millisecond and a half a frame. It is not on the commit frame. The
        /// Figure predicate itself is the real cost and it is measured by the existing
        /// <c>WallFade.Classify</c> step — no headset was available to this lane, so that number
        /// comes from the next hardware run and is not estimated here.</para></summary>
        private ulong _narrowSceneSigSum;
        private ulong _narrowSceneSigXor;

        /// <summary>ModBuild 279 — identity plus the FIGURE verdict alone, for one question:
        /// when the narrowed signature moves and the full one does not, did a renderer's figure
        /// verdict CROSS (legitimate) or is the instrument wrong (loud)? See the scene term in
        /// <see cref="CommitWouldChangeNothing"/>.</summary>
        private ulong _figureSetSigSum;
        private ulong _figureSetSigXor;

        /// <summary>The per-hole term for a snapshot entry whose renderer has died. An
        /// arbitrary odd constant — it only has to be distinct from any real renderer's hash and
        /// to accumulate like one.</summary>
        private const ulong DeadRendererSigTerm = 0xD1CE_D1CE_D1CE_D1CFUL;

        private void ResetSceneFactSignature()
        {
            _sceneFactSigSum = FnvOffset;
            _sceneFactSigXor = FnvOffset;
            _narrowSceneSigSum = FnvOffset;
            _narrowSceneSigXor = FnvOffset;
            _figureSetSigSum = FnvOffset;
            _figureSetSigXor = FnvOffset;
        }

        /// <summary>Accumulate one renderer's self-contained hash into the scene half.</summary>
        private void FoldSceneFact(ulong h)
        {
            unchecked
            {
                _sceneFactSigSum += h;
                _sceneFactSigXor ^= h;
            }
        }

        /// <summary>ModBuild 279: the same fold into the NARROWED half.</summary>
        private void FoldNarrowSceneFact(ulong h)
        {
            unchecked
            {
                _narrowSceneSigSum += h;
                _narrowSceneSigXor ^= h;
            }
        }

        /// <summary>ModBuild 279: the same fold into the FIGURE-SET half.</summary>
        private void FoldFigureSetFact(ulong h)
        {
            unchecked
            {
                _figureSetSigSum += h;
                _figureSetSigXor ^= h;
            }
        }

        /// <summary>The signatures the table IN FORCE was built from, and whether one has ever
        /// been banked. Compared, never trusted — see THE SKIP INVARIANT above.</summary>
        private ulong _committedWallSig;
        private ulong _committedSceneSum;
        private ulong _committedSceneXor;
        /// <summary>ModBuild 279 — the NARROWED and FIGURE-SET halves the table in force was
        /// built from. Banked together with the full pair, from the same instant and the same
        /// array, for the reason AdoptCommittedSignature already gives: a pair split across two
        /// cycles compares two different populations and produces a confident wrong answer.
        /// </summary>
        private ulong _committedNarrowSum;
        private ulong _committedNarrowXor;
        private ulong _committedFigureSum;
        private ulong _committedFigureXor;
        private bool _committedSigValid;

        /// <summary>Consecutive skipped cycles, and the clock the table has stood on.</summary>
        private int _skipRun;
        private float _lastCommitAt = float.NegativeInfinity;
        private float _lastCycleOpenedAt = float.NegativeInfinity;

        /// <summary>True when this cycle opened sooner than the cadence allows, i.e. some site
        /// zeroed <c>_nextRescan</c> to ask for it. Such a cycle always commits.</summary>
        private bool _cycleOpenedEarly;

        /// <summary>True when the dissolve reassigned materials since the last cycle.</summary>
        private bool _cycleMaterialsDirty;

        /// <summary>Round-robin ring for the drift probe, rebuilt at every commit. Every writer
        /// of <c>_segments</c> is a commit phase (grep: the five Remove sites are all inside one,
        /// and Clear is teardown), so between two commits this list cannot go stale.</summary>
        private readonly List<Segment> _driftRing = new(128);
        private int _driftCursor;

        /// <summary>False from the moment a commit lands until the drift probe has taken its
        /// baseline off the table that commit produced. See
        /// <see cref="SegmentBoundsStillWhereTheCommitLeftThem"/>.</summary>
        private bool _driftBaselineTaken;

        // ---- cycle accounting for the budget line ------------------------------------------

        private int _cycleSurveyFrames;
        private float _cycleWorstSurveyMillis;
        private float _cycleSurveyTotalMillis;
        private int _cycleSurveyRenderers;
        private int _cycleSkipped;
        private int _cycleCommitted;
        private int _cycleWorstSkipRun;
        private float _cycleWorstTableAgeSeconds;
        private int _cycleProbedSegments;

        /// <summary>WHY a cycle had to commit, one counter per term of the decision. FAILURES,
        /// counted separately so the line can name the term that refused rather than merely
        /// report that a commit happened — a skip that never fires and a skip that fires and is
        /// overruled must never print the same text.</summary>
        private int _noSkipNoTable;
        private int _noSkipEarly;
        private int _noSkipReveal;
        private int _noSkipBoard;
        private int _noSkipScene;
        private int _noSkipWalls;
        private int _noSkipCeiling;
        private int _noSkipDrift;
        private int _noSkipMaterials;

        /// <summary>ModBuild 279 (Option A) — THE NARROWING'S OWN THREE COUNTERS, taken on every
        /// cycle that reaches the scene term, whatever the dial says.
        ///
        /// <list type="bullet">
        /// <item><c>_narrowWouldSkip</c>: the full half moved and the narrowed half did not. With
        ///   the dial OFF this is the YIELD the narrowing would have delivered, measured on his
        ///   hardware instead of projected from a 17-sample decode; with the dial ON it is the
        ///   yield it did deliver. Either way the culprit census names the renderers behind it,
        ///   which is the list that decides whether the dial may be turned on.</item>
        /// <item><c>_narrowOnlyFigureCrossing</c>: the narrowed half moved and the full one did
        ///   not, with a figure verdict having crossed. Legitimate and conservative — see the
        ///   scene term.</item>
        /// <item><c>_narrowOnlyUnexplained</c>: the same, with NO figure verdict crossing. An
        ///   instrument bug; it is also warned about at the moment it happens.</item>
        /// </list></summary>
        private int _narrowWouldSkip;
        private int _narrowOnlyFigureCrossing;
        private int _narrowOnlyUnexplained;

        /// <summary>How many "inconsistent with itself" WARNINGS this session has printed, and the
        /// ceiling on them. The ceiling bounds the LINE, never the COUNT: the FIGURE EXEMPTION
        /// clause reports every occurrence, so a reader can still tell four occurrences from four
        /// hundred. NOT a window counter — a per-window reset would make the cap meaningless.
        /// </summary>
        private int _narrowWarnsIssued;
        private const int NarrowWarnCap = 4;

        /// <summary>The last refusal, WITH ITS NUMBERS. Never a constant: a change-gated line
        /// carrying a fixed reason string prints once and then reads as a dead instrument, and
        /// this project has an entry in its ledger for exactly that.</summary>
        private string _lastNoSkipDetail = "no cycle has been judged yet";

        /// <summary>Open the survey. Cheap and atomic: one list copy over the wall cache.</summary>
        private void BeginSurveyStage()
        {
            _surveyWalls.Clear();
            _surveyRenderers.Clear();
            _surveyWallCursor = 0;
            _surveyRendererCursor = 0;
            _surveyRenderersTaken = false;
            _surveySig = FnvOffset;
            List<ProceduralWall> cache = ProceduralWall.m_WallCache;
            for (int i = 0; i < cache.Count; i++)
            {
                ProceduralWall w = cache[i];
                if (w == null)
                    continue;
                _surveyWalls.Add(w);
                _surveySig = FoldSig(_surveySig, w.GetInstanceID());
                // A wall that became a split anchor is refreshed down a different path
                // (RefreshSplitWall) and produces a different table from the same subtree.
                _surveySig = FoldSig(_surveySig, _splitAnchors.Contains(w) ? 1 : 0);
            }
            _surveySig = FoldSig(_surveySig, _surveyWalls.Count);
        }

        /// <summary>
        /// Advance the survey within this frame's budget. Returns true when the whole wall
        /// cache has been folded.
        ///
        /// <para>PURE READS. Nothing in this method or anything it calls writes a
        /// <c>Segment</c>, a renderer, a material or a property block — the same invariant the
        /// prepare stage holds, and for a stronger reason: this stage runs on cycles that will
        /// never reach a commit at all, so a write here would be a mutation with no pass behind
        /// it to make it consistent.</para>
        /// </summary>
        private bool StepSurvey(float frameStart, float budget)
        {
            int sinceClock = 0;
            while (_surveyWallCursor < _surveyWalls.Count)
            {
                if (!_surveyRenderersTaken)
                {
                    _surveyRenderers.Clear();
                    ProceduralWall wall = _surveyWalls[_surveyWallCursor];
                    if (wall != null)
                        wall.GetComponentsInChildren(includeInactive: false, _surveyRenderers);
                    _surveyRenderersTaken = true;
                    _surveyRendererCursor = 0;
                    // The COUNT as well as the members: a subtree that lost one renderer and
                    // gained another with a recycled instance ID would otherwise be silent. It
                    // is also where activeInHierarchy enters the wall half — includeInactive is
                    // false, so a subtree the game switched off shortens this list.
                    _surveySig = FoldSig(_surveySig, _surveyRenderers.Count);
                }
                while (_surveyRendererCursor < _surveyRenderers.Count)
                {
                    MeshRenderer r = _surveyRenderers[_surveyRendererCursor++];
                    // IDENTITY ONLY, and that is exact rather than a compromise. The wall half
                    // stands for what CommitWallCache would collect, and that pass's membership
                    // is GetComponentsInChildren(includeInactive: false) filtered by
                    // CollectWallFadeInfo — neither of which reads `renderer.enabled` (the
                    // predicate's whole body is the standing-prop guard, the name tests and the
                    // material walk; grep it for `enabled` and there is nothing). The
                    // includeInactive flag already folds activeInHierarchy into the member LIST
                    // above, so a subtree the game switched off changes the count and the ids.
                    // Folding `enabled` on top would only add this subsystem's OWN writes —
                    // hiding a faded wall's siblings and shell pieces is r.enabled = false — and
                    // make the skip refuse on every fade transition.
                    _surveySig = FoldSig(_surveySig, r == null ? 0 : r.GetInstanceID());
                    _cycleSurveyRenderers++;
                    if (++sinceClock < SurveyChunk)
                        continue;
                    sinceClock = 0;
                    if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget)
                        return false;
                }
                _surveyWallCursor++;
                _surveyRenderersTaken = false;
                if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget)
                    return false;
            }
            return true;
        }

        /// <summary>Fold the census's own totals into the wall half, once the census is known to
        /// be complete. They are cheap cross-checks on the scene half rather than new
        /// information, and they make a truncated census impossible to mistake for a quiet
        /// one.</summary>
        private void FinishSurveySignature()
        {
            _surveySig = FoldSig(_surveySig, _factCount);
            _surveySig = FoldSig(_surveySig, _factWallFade.Count);
            _surveySig = FoldSig(_surveySig, _factWater.Count);
        }

        /// <summary>Drop this cycle's survey state. Held references would keep a scene's worth
        /// of walls and renderers alive across the gap to the next cycle.</summary>
        private void ClearSurveyState()
        {
            _surveyWalls.Clear();
            _surveyRenderers.Clear();
            _surveyWallCursor = 0;
            _surveyRendererCursor = 0;
            _surveyRenderersTaken = false;
            _cycleOpenedEarly = false;
            _cycleMaterialsDirty = false;
        }

        // =================================================================================
        // ModBuild 278 — WHICH RENDERERS MOVED THE SCENE SIGNATURE
        // =================================================================================
        //
        // THE LEAD, taken straight out of the ModBuild 277 hardware log. Across the whole
        // session the SKIP clause totals read:
        //
        //     80 of 113 judged cycles SKIPPED the commit; WHY A CYCLE COMMITTED:
        //     2 no table yet, 1 room reveal, 0 asked for, 0 dissolve material swap,
        //     0 board moved, 28 SCENE SIGNATURE MOVED, 2 wall signature moved,
        //     0 STALENESS CEILING, 0 segment AABB drift.
        //
        // 28 of 33. The scene-signature refusal's own text ends by naming the next question —
        // "IF THIS IS THE COUNT THAT DOMINATES … the next round's question is WHICH renderers,
        // not whether to skip" — and this is that instrument. Everything about how it groups,
        // ranks and elides lives in WallSegmentFadeCulprits.cs, which is free of Unity so the
        // wire suite can drive it against a NULL input and a known-positive control; what lives
        // here is only the banking and the gating.
        //
        // WHY THE BANKED TABLE IS TAKEN AT THE COMMIT AND NOWHERE ELSE. The question is "what
        // has changed since the table in force was built", so the comparison baseline has to be
        // the census the commit consumed — the same one AdoptCommittedSignature banks the hash
        // from, taken at the same instant, from the same array. Any other moment would compare
        // two different populations and produce a confident wrong answer, which is the failure
        // mode this project's ledger calls "a ratio with two populations".
        //
        // WHAT IT COSTS AND HOW THAT IS KNOWN RATHER THAN ASSUMED. Banking is one dictionary
        // write per classified renderer (~5800) on a frame that is already the ~90 ms commit,
        // with no allocation after the first cycle — the dictionary is Cleared, never rebuilt,
        // and the names are references the census already owns. The diagnosis is one dictionary
        // probe per renderer plus one pass over the banked table, and it runs ONLY on a cycle
        // that has already decided to commit, at most once per throttle window. Both report
        // under the 'WallFade.SigDiag' step, so the next log prices this instrument instead of
        // taking its author's word for it — an always-on probe that nobody priced has cost this
        // project a round before.

        /// <summary>Instance ID → what the last commit's census saw. Cleared and refilled at
        /// each commit; never rebuilt, so it allocates once and then reuses its capacity.</summary>
        private readonly Dictionary<int, WallSegmentFadeCulprits.Banked> _bankedFacts = new(8192);

        /// <summary>Live scratch handed to the diff. Reused for the same reason.</summary>
        private readonly List<WallSegmentFadeCulprits.Entry> _culpritLive = new(8192);

        /// <summary>False until a commit has banked a census — see the "no banked census"
        /// branch of <see cref="WallSegmentFadeCulprits.Format"/>, which says so rather than
        /// printing an empty diff that would read as "nothing changed".</summary>
        private bool _bankedFactsValid;

        /// <summary>Throttle for the census. It is not free and it runs on a frame that is about
        /// to be expensive anyway, so it prints at the same cadence as the BUDGET line rather
        /// than on every refusal.</summary>
        private float _nextCulpritLogTime;

        /// <summary>How many name groups each of ENTERED / LEFT / CHANGED lists. The rest are
        /// COUNTED and the line says how many — see the elision fields on the census.</summary>
        private const int CulpritTopGroups = 10;

        /// <summary>Bank the census this commit consumed, so the next scene-signature refusal
        /// can say WHICH renderers moved rather than only that some did.</summary>
        private void BankFactCensus()
        {
            _bankedFacts.Clear();
            for (int i = 0; i < _factCount; i++)
            {
                ref RendererFact f = ref _facts[i];
                if (f.R == null)
                    continue;
                // The same eight bits ClassifySlice folds into the scene half, in the same
                // order. They are re-derived here rather than cached at the fold site because a
                // second copy of the bit layout is a second thing to keep in sync; if these ever
                // disagree the census reports "NOTHING MOVED", which is loud and self-accusing
                // by design.
                _bankedFacts[f.R.GetInstanceID()] =
                    new WallSegmentFadeCulprits.Banked(f.Name, FactBits(ref f));
            }
            _bankedFactsValid = true;
        }

        /// <summary>The eight verdict bits of one fact, exactly as the scene half folds them,
        /// plus (ModBuild 279) the FIGURE bit, which the scene half deliberately does NOT fold —
        /// see the ninth term.</summary>
        private static int FactBits(ref RendererFact f)
        {
            Renderer? r = f.R;
            return (f.Mesh != null ? WallSegmentFadeCulprits.BitMesh : 0)
                 | (f.Particles ? WallSegmentFadeCulprits.BitParticles : 0)
                 | (f.Mountable ? WallSegmentFadeCulprits.BitMountable : 0)
                 | (f.Mod ? WallSegmentFadeCulprits.BitMod : 0)
                 | (f.WallFadeShader ? WallSegmentFadeCulprits.BitWallFade : 0)
                 | (f.FoliageShader ? WallSegmentFadeCulprits.BitFoliage : 0)
                 | (f.WaterSurface ? WallSegmentFadeCulprits.BitWater : 0)
                 | (r != null && r.gameObject.activeInHierarchy
                        ? WallSegmentFadeCulprits.BitActive : 0)
                 // ModBuild 279 (Option A) — the NINTH bit, which the SIGNATURE does not fold
                 // and the CENSUS must carry. The narrowing's whole safety argument is "these
                 // renderers are figures", and the only way to check it rather than assert it is
                 // for the culprit line to say, renderer by renderer, whether the thing that
                 // moved the full signature was one.
                 | (f.Figure ? WallSegmentFadeCulprits.BitFigure : 0);
        }

        /// <summary>Name the renderers that moved the scene half, throttled and measured.</summary>
        private void LogSignatureCulprits(float now)
        {
            if (!WallFadeTuning.SignatureCulpritCensusOn)
                return;
            if (now < _nextCulpritLogTime)
                return;
            _nextCulpritLogTime = now + BudgetLogIntervalSeconds;
            using (PerfMonitor.Scope("WallFade.SigDiag"))
            {
                if (!_bankedFactsValid)
                {
                    VRLog.Info(Name, WallSegmentFadeCulprits.Format(
                        new WallSegmentFadeCulprits.Census(), _factCount, 0, tableBanked: false));
                    return;
                }
                _culpritLive.Clear();
                for (int i = 0; i < _factCount; i++)
                {
                    ref RendererFact f = ref _facts[i];
                    if (f.R == null)
                        continue;
                    _culpritLive.Add(new WallSegmentFadeCulprits.Entry(
                        f.R.GetInstanceID(), f.Name, FactBits(ref f)));
                }
                WallSegmentFadeCulprits.Census census = WallSegmentFadeCulprits.Diff(
                    _bankedFacts, _culpritLive, CulpritTopGroups);
                VRLog.Info(Name, WallSegmentFadeCulprits.Format(
                    census, _culpritLive.Count, _bankedFacts.Count, tableBanked: true));
                _culpritLive.Clear(); // do not hold a scene's worth of names to the next cycle
            }
        }

        /// <summary>Bank the signatures this cycle's commit was built against, and rebuild the
        /// drift ring over the table it produced.</summary>
        private void AdoptCommittedSignature(float now)
        {
            _committedSceneSum = _sceneFactSigSum;
            _committedSceneXor = _sceneFactSigXor;
            _committedNarrowSum = _narrowSceneSigSum;   // ModBuild 279 — same instant, same array
            _committedNarrowXor = _narrowSceneSigXor;
            _committedFigureSum = _figureSetSigSum;
            _committedFigureXor = _figureSetSigXor;
            _committedWallSig = _surveySig;
            _committedSigValid = true;
            BankFactCensus();
            _skipRun = 0;
            _lastCommitAt = now;
            _cycleCommitted++;
            _driftRing.Clear();
            foreach (Segment seg in _segments.Values)
            {
                seg.ProbeBoundsValid = false;
                _driftRing.Add(seg);
            }
            _driftCursor = 0;
            _driftBaselineTaken = false;
        }

        /// <summary>Record how long the table in force has stood — the DECISION LATENCY this
        /// change actually incurs, measured rather than argued.</summary>
        private void NoteTableAge(float now)
        {
            if (float.IsNegativeInfinity(_lastCommitAt))
                return;
            float age = now - _lastCommitAt;
            if (age > _cycleWorstTableAgeSeconds)
                _cycleWorstTableAgeSeconds = age;
        }

        /// <summary>
        /// THE DRIFT PROBE — the one term of the decision that MEASURES instead of comparing.
        ///
        /// <para>WHY IT IS NEEDED. Everything else in the signature is membership: which
        /// renderer belongs to which segment. Membership can be identical while the GEOMETRY
        /// underneath it has moved, and a segment's AABB is what every coverage decision is
        /// taken against. The reveal gate and the board probe cover the two ways the whole
        /// board moves; this covers the one they do not — a piece that moved on its own.</para>
        ///
        /// <para>WHY IT KEEPS ITS OWN BASELINE. Neither <c>seg.Bounds</c> nor a copy of it taken
        /// at collection time is comparable with a live re-union of <c>Renderers</c>: the first
        /// is EXTENDED afterwards by the stacked and gate phases, the second is taken before
        /// <c>StripGroundRenderers</c> and the prop-unit pass have REMOVED members from the list
        /// it would be compared against. Either would read as permanent drift and the skip would
        /// never fire. See <see cref="Segment.ProbeBounds"/>.</para>
        ///
        /// <para>A DEAD MEMBER IS DRIFT, not a skip: a renderer that died out from under a
        /// segment must reach FinishRefresh's leaver path, and only a commit runs that.</para>
        /// </summary>
        private bool SegmentBoundsStillWhereTheCommitLeftThem(out int probed, out string detail)
        {
            probed = 0;
            detail = "";
            // THE BASELINE, taken once per commit over the WHOLE table rather than round-robin.
            // It runs on the first cycle that would otherwise have skipped — a frame whose only
            // other work is the survey — and costs one Renderer.bounds read per tracked member,
            // low single-digit thousands. Taking it here rather than inside the commit keeps it
            // off the frame this whole exercise exists to shrink, and taking it in one pass
            // rather than sixteen segments at a time means no member is ever compared against a
            // baseline from a different commit.
            if (!_driftBaselineTaken)
            {
                for (int i = 0; i < _driftRing.Count; i++)
                {
                    if (TakeProbeBounds(_driftRing[i]))
                        probed++;
                }
                _driftBaselineTaken = true;
                _driftCursor = 0;
                return true;
            }
            int n = Mathf.Min(BoundsProbeSegmentsPerCycle, _driftRing.Count);
            for (int i = 0; i < n; i++)
            {
                if (_driftCursor >= _driftRing.Count)
                    _driftCursor = 0;
                Segment seg = _driftRing[_driftCursor++];
                if (!seg.ProbeBoundsValid)
                    continue;
                bool has = false;
                Bounds live = default;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r == null)
                    {
                        detail = "a member renderer of a tracked segment had died (the leaver "
                               + "path that clears its property block only runs inside a commit)";
                        return false;
                    }
                    if (!has)
                    {
                        live = r.bounds;
                        has = true;
                    }
                    else
                    {
                        live.Encapsulate(r.bounds);
                    }
                }
                if (!has)
                    continue;
                probed++;
                float dc = (live.center - seg.ProbeBounds.center).sqrMagnitude;
                float de = (live.extents - seg.ProbeBounds.extents).sqrMagnitude;
                float eps2 = BoundsDriftEpsilonWU * BoundsDriftEpsilonWU;
                if (dc > eps2 || de > eps2)
                {
                    detail = $"a tracked segment's {seg.Renderers.Count}-renderer AABB had "
                           + $"drifted {Mathf.Sqrt(Mathf.Max(dc, de)):F3}wu against the "
                           + $"{BoundsDriftEpsilonWU:0.00}wu bar";
                    return false;
                }
            }
            return true;
        }

        /// <summary>Measure one segment's live member union into its baseline. Returns false for
        /// a segment the probe cannot judge (no members, or a dead one — the latter is left for
        /// the compare pass above to report as drift rather than silently baselined).</summary>
        private static bool TakeProbeBounds(Segment seg)
        {
            seg.ProbeBoundsValid = false;
            bool has = false;
            Bounds live = default;
            foreach (MeshRenderer r in seg.Renderers)
            {
                if (r == null)
                    return false;
                if (!has)
                {
                    live = r.bounds;
                    has = true;
                }
                else
                {
                    live.Encapsulate(r.bounds);
                }
            }
            if (!has)
                return false;
            seg.ProbeBounds = live;
            seg.ProbeBoundsValid = true;
            return true;
        }

        /// <summary>
        /// THE DECISION: would running the commit now produce the table that is already in
        /// force? Every term is a comparison against what that table was built from, every
        /// refusal is counted, and the refusal that fired carries its own numbers into
        /// <see cref="_lastNoSkipDetail"/>.
        ///
        /// <para>ORDER IS ATTRIBUTION, not correctness: the terms are independent, and the
        /// first one that refuses is the one the line names. The cheap scalars come first so a
        /// refused cycle does not pay for the drift probe.</para>
        /// </summary>
        private bool CommitWouldChangeNothing(TilesOcclusionGenerator gen, float now)
        {
            if (!_committedSigValid || _segments.Count == 0)
            {
                _noSkipNoTable++;
                _lastNoSkipDetail =
                    $"there was no table in force to compare against ({_segments.Count} "
                    + "segment(s), signature banked: "
                    + (_committedSigValid ? "yes" : "no") + ")";
                return false;
            }
            if (_rescanUrgent || gen.m_RoomRenderers.Count != _builtRoomCount)
            {
                _noSkipReveal++;
                _lastNoSkipDetail =
                    $"a room reveal — {gen.m_RoomRenderers.Count} room renderer(s) against the "
                    + $"{_builtRoomCount} the table was built from";
                return false;
            }
            if (_cycleOpenedEarly)
            {
                _noSkipEarly++;
                _lastNoSkipDetail =
                    "the cycle was ASKED FOR: it opened "
                    + $"{now - _lastCycleOpenedAt:F2}s after the last one against the "
                    + $"{_scheduledRescanInterval:0.00}s cadence IT WAS SCHEDULED WITH "
                    + $"(the live [WallFade] RescanIntervalSeconds reads "
                    + $"{RescanIntervalSeconds:0.00}s), which is a site zeroing _nextRescan for "
                    + "a mid-fade regeneration. If these two differ, the dial moved between the "
                    + "two cycles and at most ONE cycle can be misread as asked-for";
                return false;
            }
            if (_cycleMaterialsDirty)
            {
                _noSkipMaterials++;
                _lastNoSkipDetail =
                    "the dissolve had reassigned sharedMaterials since the last cycle — our own "
                    + "copies carry the same shader family, so the fact bits cannot see it";
                return false;
            }
            if (!BoardStillWhereTheFloorPlanesSayItIs(gen))
            {
                _noSkipBoard++;
                _lastNoSkipDetail =
                    "the board had moved out from under the floor planes the standing and "
                    + "ground rules measure against (the PERF S4 probe, "
                    + (_prepBoardProbeValid ? "live" : "NOT ARMED — no probe was taken") + ")";
                return false;
            }
            // ================================================================================
            // THE SCENE TERM, AND ModBuild 279's SELF-ACCUSING FALSIFIER UNDER IT.
            // ================================================================================
            //
            // BOTH signatures are compared on EVERY cycle that reaches this term, whatever the
            // dial says. The dial decides only WHICH ONE REFUSES; the other one is still read,
            // still compared and still counted, so the log states what the narrowing did — with
            // names — instead of reassuring anyone that it was safe.
            //
            // WHY THE COUNTING LIVES HERE AND NOT EARLIER. A cycle that already refused on a
            // reveal, an asked-for cadence, a material swap or a board move is going to commit
            // whatever the scene half says, so the narrowing buys nothing on it and must not be
            // credited for it. Counting at this term counts exactly the cycles the narrowing can
            // act on.
            bool fullMoved = _sceneFactSigSum != _committedSceneSum
                             || _sceneFactSigXor != _committedSceneXor;
            bool narrowMoved = _narrowSceneSigSum != _committedNarrowSum
                               || _narrowSceneSigXor != _committedNarrowXor;
            if (fullMoved && !narrowMoved)
            {
                // THE YIELD, measured rather than projected: this cycle would have rebuilt the
                // whole table under the shipped signature and does not need to under the narrowed
                // one. The culprit census names WHICH renderers moved the full half — that is the
                // list a reader has to look at before this dial is ever turned on, because it is
                // exactly the list the narrowing is dropping.
                _narrowWouldSkip++;
                LogSignatureCulprits(now);
            }
            else if (narrowMoved && !fullMoved)
            {
                // THE DESIGN DOCUMENT CALLS THIS IMPOSSIBLE. IT IS NOT, AND THE THIRD
                // ACCUMULATOR IS HERE TO SEPARATE THE TWO CASES RATHER THAN SHOUT AT BOTH.
                //
                // The narrowed term differs from the full term ONLY through f.Figure. So if a
                // renderer's FIGURE VERDICT crosses — the game reparents it under an actor, or
                // out from under one — while none of the eight full bits move, the narrowed half
                // moves and the full half does not. That is the narrowing WORKING: the crossing
                // is exactly what the FIGURE bit exists to catch, and committing on it is the
                // conservative answer. It is counted, not alarmed about.
                //
                // With the figure-set half unchanged as well, no figure verdict crossed, and
                // there is no mechanism left that can move one accumulator and not the other.
                // That is an instrument bug — a fold that disagrees with itself — and it is
                // counted separately and printed as a WARNING, because a signature nobody can
                // trust must never be allowed to quietly gate a remedy.
                bool figureCrossed = _figureSetSigSum != _committedFigureSum
                                     || _figureSetSigXor != _committedFigureXor;
                if (figureCrossed)
                {
                    _narrowOnlyFigureCrossing++;
                }
                else
                {
                    // THE COUNT IS UNBOUNDED, THE LINE IS NOT. A per-cycle warning that fires
                    // forever is a log nobody can read past; a capped COUNT is a number nobody
                    // can trust. So the counter rises on every occurrence and the WARNING stops
                    // after four, saying so.
                    _narrowOnlyUnexplained++;
                    if (_narrowWarnsIssued < NarrowWarnCap)
                    {
                        _narrowWarnsIssued++;
                        VRLog.Warn(Name,
                            "SIGNATURE NARROWING IS INCONSISTENT WITH ITSELF: the NARROWED scene "
                            + "half moved while the FULL half did not, and no renderer's FIGURE "
                            + "verdict changed either — which the fold makes impossible, because "
                            + "the narrowed term differs from the full term only through that "
                            + $"verdict. banked full {_committedSceneSum:X16}/"
                            + $"{_committedSceneXor:X16}, narrowed {_committedNarrowSum:X16}/"
                            + $"{_committedNarrowXor:X16}, figure-set {_committedFigureSum:X16}/"
                            + $"{_committedFigureXor:X16}; live full {_sceneFactSigSum:X16}/"
                            + $"{_sceneFactSigXor:X16}, narrowed {_narrowSceneSigSum:X16}/"
                            + $"{_narrowSceneSigXor:X16}, figure-set {_figureSetSigSum:X16}/"
                            + $"{_figureSetSigXor:X16} over {_factCount} classified renderer(s). "
                            + "THE NARROWING MUST NOT BE TRUSTED WHILE THIS COUNT IS NON-ZERO — "
                            + "leave [WallFade] FigureExemptSkip off and read this line, not the "
                            + $"yield figure beside it. (Warning {_narrowWarnsIssued} of "
                            + $"{NarrowWarnCap} per session; the COUNT keeps rising in the BUDGET "
                            + "line's FIGURE EXEMPTION clause after this stops printing.)");
                    }
                }
            }
            if (WallFadeTuning.FigureExemptSkipOn ? narrowMoved : fullMoved)
            {
                _noSkipScene++;
                _lastNoSkipDetail =
                    $"the SCENE signature moved over {_factCount} classified renderer(s) "
                    + $"(banked {_committedSceneSum:X16}/{_committedSceneXor:X16}, live "
                    + $"{_sceneFactSigSum:X16}/{_sceneFactSigXor:X16}) — a renderer appeared, "
                    + "died, was deactivated by the game or changed shader family. IF THIS IS "
                    + "THE COUNT THAT DOMINATES, the scene is churning under the snapshot and "
                    + "the next round's question is WHICH renderers, not whether to skip"
                    // ModBuild 279: which half actually refused, and the OTHER half's numbers
                    // beside it — plus _factCount, which the design asked for by name so the
                    // ModBuild-277 log's one unattributable 'sum unchanged, xor moved' line is
                    // attributable the next time it happens.
                    + $". ModBuild 279: the term that refused was the "
                    + (WallFadeTuning.FigureExemptSkipOn ? "NARROWED" : "FULL")
                    + $" half ([WallFade] FigureExemptSkip is "
                    + (WallFadeTuning.FigureExemptSkipOn ? "ON" : "OFF")
                    + $"); the narrowed half reads banked {_committedNarrowSum:X16}/"
                    + $"{_committedNarrowXor:X16} against live {_narrowSceneSigSum:X16}/"
                    + $"{_narrowSceneSigXor:X16}, and the figure-set half banked "
                    + $"{_committedFigureSum:X16}/{_committedFigureXor:X16} against live "
                    + $"{_figureSetSigSum:X16}/{_figureSetSigXor:X16}";
                // ModBuild 278: it dominates (28 of 33 in the ModBuild 277 log), so the question
                // is answered here rather than deferred to another round. Throttled and measured
                // — see LogSignatureCulprits.
                LogSignatureCulprits(now);
                return false;
            }
            if (_surveySig != _committedWallSig)
            {
                _noSkipWalls++;
                _lastNoSkipDetail =
                    $"the WALL signature moved over {_surveyWalls.Count} cache wall(s) / "
                    + $"{_cycleSurveyRenderers} subtree renderer(s) this window "
                    + $"({_committedWallSig:X16} banked, {_surveySig:X16} live)";
                return false;
            }
            if (_skipRun >= MaxSkippedCyclesInARow)
            {
                _noSkipCeiling++;
                _lastNoSkipDetail =
                    $"THE STALENESS CEILING: {_skipRun} cycle(s) had been skipped in a row "
                    + $"({now - _lastCommitAt:F1}s of table age) and the fail-safe forced one "
                    + "commit. This is the ONE term that can still produce a periodic stall — "
                    + "if this count is the only non-zero refusal, the signature is complete "
                    + "and the ceiling is what the next round should raise or remove";
                return false;
            }
            if (!SegmentBoundsStillWhereTheCommitLeftThem(out int probed, out string driftWhy))
            {
                _noSkipDrift++;
                _cycleProbedSegments += probed;
                _lastNoSkipDetail = "the geometry had moved under an unchanged membership: "
                                  + driftWhy;
                return false;
            }
            _cycleProbedSegments += probed;
            return true;
        }

        /// <summary>Append the SKIP clause to the budget line. Every field is a count taken this
        /// window — including the nine refusal counters and the last refusal's own numbers — so
        /// a stage that never ran, a stage that ran and skipped nothing, and a stage that
        /// skipped everything all print visibly different text.</summary>
        private void AppendSkipClause(System.Text.StringBuilder sb)
        {
            int judged = _cycleSkipped + _cycleCommitted;
            sb.Append(" SKIP (PERF S5 — the cycle that does not commit): ")
              .Append(_cycleSkipped).Append(" of ").Append(judged)
              .Append(" judged cycle(s) SKIPPED the commit outright, ")
              .Append(_cycleCommitted).Append(" committed; longest run of skips ")
              .Append(_cycleWorstSkipRun).Append(" (ceiling ").Append(MaxSkippedCyclesInARow)
              .Append("); DECISION LATENCY — the table in force stood at most ")
              .Append(_cycleWorstTableAgeSeconds.ToString("F1"))
              .Append("s without a rebuild in this window, against the ")
              .Append(RescanIntervalSeconds.ToString("0.00"))
              .Append("s cadence a committing cycle gives it ([WallFade] "
                    + "RescanIntervalSeconds, live — raising it divides the NUMBER of ~90ms "
                    + "commits and shortens not one of them, at the price of exactly this "
                    + "latency figure). SURVEY: ")
              .Append(_cycleSurveyFrames).Append(" frame(s) at ")
              .Append(SurveyBudgetMillis.ToString("0.0")).Append("ms/frame, worst ")
              .Append(_cycleWorstSurveyMillis.ToString("F2")).Append("ms, ")
              .Append(_cycleSurveyTotalMillis.ToString("F1")).Append("ms total over ")
              .Append(_cycleSurveyRenderers)
              .Append(" wall-subtree renderer(s) — this is the PRICE of the skip and it is "
                    + "paid on every cycle, committing or not; ")
              .Append(_cycleProbedSegments)
              .Append(" segment AABB(s) re-measured by the drift probe at ")
              .Append(BoundsDriftEpsilonWU.ToString("0.00")).Append("wu. WHY A CYCLE COMMITTED: ")
              .Append(_noSkipNoTable).Append(" no table yet, ")
              .Append(_noSkipReveal).Append(" room reveal, ")
              .Append(_noSkipEarly).Append(" asked for (a mid-fade regeneration zeroed the "
                    + "cadence — THIS IS THE FALSIFIER FOR ModBuild 278's cadence dial: the "
                    + "'asked for' test compares the gap since the last cycle against the "
                    + "cadence THAT CYCLE WAS SCHEDULED WITH, currently ")
              .Append(_scheduledRescanInterval.ToString("0.00"))
              .Append("s against a live dial of ")
              .Append(RescanIntervalSeconds.ToString("0.00"))
              .Append("s. Reading the LIVE value instead would make an ordinary cycle look "
                    + "asked-for whenever the dial had just been raised, and if it were ever "
                    + "systematically true it would mark EVERY cycle as asked-for and switch "
                    + "the whole skip off silently. This counter read 0 across the entire "
                    + "ModBuild 277 log and must stay 0 in a session where nothing regenerates, "
                    + "whatever the dial is set to), ")
              .Append(_noSkipMaterials).Append(" dissolve material swap, ")
              .Append(_noSkipBoard).Append(" board moved, ")
              .Append(_noSkipScene).Append(" scene signature moved, ")
              .Append(_noSkipWalls).Append(" wall signature moved, ")
              .Append(_noSkipCeiling).Append(" STALENESS CEILING, ")
              .Append(_noSkipDrift).Append(" segment AABB drift. ")
              // ModBuild 279 (Option A) — THE NARROWING, AND ITS OWN FALSIFIER. Printed whether
              // the dial is on or off and whether the counts are zero or not: a clause that
              // disappears when it has nothing to say is a clause a reader cannot tell from a
              // stage that never ran.
              .Append("FIGURE EXEMPTION ([WallFade] FigureExemptSkip is ")
              .Append(WallFadeTuning.FigureExemptSkipOn ? "ON — the NARROWED half decides"
                                                        : "OFF — the FULL half decides, the "
                                                          + "narrowed one is measured only")
              .Append("): ").Append(_narrowWouldSkip)
              .Append(" cycle(s) this window moved the FULL scene signature but NOT the narrowed "
                    + "one — with the dial off that is the number of ~95ms rebuilds this "
                    + "narrowing WOULD have removed, and the SIGNATURE CULPRITS line names the "
                    + "renderers it would have stopped listening to; with it on, the number it "
                    + "did remove. ")
              .Append(_narrowOnlyFigureCrossing)
              .Append(" cycle(s) moved the narrowed half while the full half stood still WITH a "
                    + "figure verdict crossing — that is the FIGURE bit doing its job (a renderer "
                    + "was reparented into or out of the round-7 class) and it commits, which is "
                    + "the conservative answer. ")
              .Append(_narrowOnlyUnexplained)
              .Append(" cycle(s) did the same with NO figure verdict crossing, WHICH THE FOLD "
                    + "MAKES IMPOSSIBLE: the narrowed term differs from the full term only "
                    + "through that verdict. A NON-ZERO COUNT HERE INDICTS THIS INSTRUMENT AND "
                    + "NOT THE SCENE — do not read the yield figure above it, and do not turn "
                    + "the dial on. Each occurrence also prints a WARNING with all six "
                    + "accumulators. LAST REFUSAL: ")
              .Append(_lastNoSkipDetail)
              .Append(". READ THIS AGAINST 'WORST SINGLE FRAME' ABOVE: the ModBuild 274 log "
                    + "measured ~95ms of commit on EVERY cycle (WallCache 31.5, PropUnits 30.0, "
                    + "Mounted 24, other 21 phases 10) with the SAME 12819 warmed renderers and "
                    + "3105 unit roots in 22 consecutive windows — a table rebuilt 66 times to "
                    + "the same answer. A skipped cycle costs the survey and nothing else.");
        }
    }
}
