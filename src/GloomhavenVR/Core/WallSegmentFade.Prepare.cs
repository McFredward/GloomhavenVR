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
    }
}
