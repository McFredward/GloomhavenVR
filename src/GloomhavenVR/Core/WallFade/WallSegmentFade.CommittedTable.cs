using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// PERF B, STEP 1 — THE COMMITTED STATE, IN ONE OBJECT.
///
/// <para><b>THE USER REPORT, verbatim (2026-08-25, after ModBuild 277).</b> <i>"Ich will aber
/// eigentlich gar keine spürbaren Ruckler - nicht nur seltenere. Kannst du das Neuaufbauen der
/// Tabelle nicht irgendwie in einen separaten Thread auslagern, so dass das Hauptspiel nicht in
/// Mitleidenschaft gezogen wird?"</i> He was offered four options and picked A and B. A shipped
/// in ModBuild 280 and is statistical: it makes the ~95 ms commit frame RARER, which is the one
/// thing he said is not enough. B removes it, by spreading the same work over ~63 frames at
/// 1.5 ms each — the budget the Prepare stage already proves invisible.</para>
///
/// <para><b>WHY A SEPARATE THREAD IS NOT THE ANSWER TO HIS QUESTION, and he should be told
/// so.</b> The commit reads <c>renderer.bounds</c>, <c>Transform.position</c>,
/// <c>Renderer.sharedMaterial</c> and <c>Renderer.enabled</c> on thousands of scene objects.
/// Unity's scene graph is main-thread only; every one of those reads throws
/// <c>UnityException: ... can only be called from the main thread</c> off it. Slicing reaches
/// the outcome he asked for — the main game is not held up — by the only route the engine
/// allows.</para>
///
/// <para><b>WHAT THIS FILE IS, AND WHAT IT DELIBERATELY IS NOT.</b> It is the enabling refactor
/// and nothing else: the committed state moves into one object behind one field, so that a
/// later build can swap it with one reference assignment. <b>In this build
/// <see cref="FadeDriver._live"/> is assigned exactly once, at construction, and never
/// reassigned</b> — there is no second table, no swap, and no behaviour change of any kind.
/// That is on purpose and it is the point: a pure rename is the one kind of change that can be
/// PROVEN to be a no-op, and it was proven for this one by normalising every renamed identifier
/// back and diffing the result against the parent commit (see the round's report). Everything
/// downstream of B rests on this move being invisible, so it is landed and verified before
/// anything is allowed to depend on it.</para>
///
/// <para><b>WHY ONE OBJECT AND NOT A DOUBLE-BUFFERED DICTIONARY.</b> <c>Tick()</c> is the
/// per-frame applier and it does not read <c>_segments</c> alone. It reads the room registry,
/// the floor planes, the sample grid, the split-anchor set, the water and arch protection
/// rects, and the board volume; <c>ApplyMounted</c> reads the union overlap map and its owner
/// set. Swapping the dictionary and leaving the rest behind ships a table whose segments
/// disagree with the rooms they are indexed into — a TORN table, and a defect with no
/// exception, no log line and no way for this subsystem to notice. Everything on the list below
/// is therefore one object with one lifetime.</para>
///
/// <para><b>ELEVEN FIELDS THE DESIGN NOTE'S LIST MISSED</b>
/// (.planning/perf/WALL-COMMIT-ARCHITECTURE.md §3.1 names sixteen; there are twenty-seven on the
/// decision path). Every one was found by tracing the per-frame READERS rather than by trusting
/// the list, and §3.1's own warning turns out to apply to §3.1. In three families:</para>
/// <list type="number">
/// <item><b>The board-volume family:</b> <see cref="WallSegmentFade.FadeDriver.CommittedTable.BoardVolumeValid"/> (without it
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.BoardVolume"/> is a stale AABB that reads as authoritative),
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.BoardCrestWU"/>, <see cref="WallSegmentFade.FadeDriver.CommittedTable.BoardFloorY"/> and <see cref="WallSegmentFade.FadeDriver.CommittedTable.BoardVolumeRooms"/> —
///   written by <c>CommitBoardVolume</c> and read every frame by <c>UpdateInsideBoard</c> (whose
///   Schmitt bars at BOTH ends are fractions of the crest) and <c>UpdateWalkInside</c> (whose
///   real-metre term is <c>BoardCrestWU / rigScale</c>). Leave those behind and the walk-in
///   stand-down — the thing that holds every wall solid while the player stands in the board,
///   and behaviour the user has just called perfect — runs on the retired table's geometry.</item>
/// <item><b>The sample INDICES, and this is the sharpest one:</b>
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.RoomSampleStart"/> and <see cref="WallSegmentFade.FadeDriver.CommittedTable.RoomSampleCount"/>. §3.1 has
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.AllSamples"/> and not the two indices INTO it. <c>RoomBlockedFraction</c> reads
///   <c>start = RoomSampleStart[room]</c> and then bounds the walk by <c>AllSamples.Count</c>;
///   <c>BlockedFraction</c> and <c>RoomDecisionValid</c> take the DENOMINATOR from
///   <c>RoomSampleCount</c>. Buffer the samples without their indices and, on the swap frame,
///   the coverage decision mixes an old start index with a new sample list — which is the
///   ModBuild 258 failure ("the denominator was a rectangle drawn around a hexagonal room")
///   re-armed, and it changes which walls fade.</item>
/// <item><b>Committed state living outside WallSegmentFade.cs:</b>
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.CornerPieces"/> (read every frame by <c>ApplyCornerPieces</c>),
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.PropUnitAnchors"/> and <see cref="WallSegmentFade.FadeDriver.CommittedTable.PropUnitRootMemo"/> (the latter read by
///   <c>StaggerRootOf</c> on every prop-frame), and <see cref="WallSegmentFade.FadeDriver.CommittedTable.PrepBoardProbePos"/> /
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.PrepBoardProbeValid"/>, which are gate 3 of the commit-SKIP decision — a stale
///   pair there does not tear the table, it decides not to rebuild one.</item>
/// </list>
///
/// <para><b>WHAT IS DELIBERATELY *NOT* IN HERE, and it is the harder half of build 2.</b> The
/// subsystem also keeps ledgers that are NOT committed state and must not be swapped, because
/// they are written by the appliers as well as by the commit and because several of them hold
/// references INTO the table:</para>
/// <list type="bullet">
/// <item><c>_mountedTouched</c> — every renderer this subsystem currently holds hidden or
///   ramped. Written by the commit AND by <c>ApplyBody</c> / <c>ApplyStacked</c>
///   (WallSegmentFade.Body.cs, .Stacked.cs). It is the orphan guard's ledger: the undo log at
///   PROP granularity, and it must survive a swap, not ride one.</item>
/// <item><c>_mountedAnchorLedger</c> — where each prop sat last rescan, <b>in its owning
///   Segment's frame</b>. It stores a <c>Segment</c> reference. After a swap that reference
///   names a segment in the DISCARDED table, so the mobility differential would be measured
///   against a frame nothing updates any more.</item>
/// <item><c>_attachmentOwned</c> (holds a <c>Segment</c>), <c>_mountedUnitHome</c>
///   (<c>Transform → Segment</c>), <c>_mountedMobile</c>, <c>_showEdgeUnitReturn</c>,
///   <c>_claimedRenderers</c>, <c>_siblingOwned</c>, <c>_propUnitOwnerLast</c> (the sticky
///   prop-unit owner, written at the END of one commit and read by the NEXT one) and the
///   <c>_bvSig*</c> announce-once signature.</item>
/// <item><b>And two that will break a naive swap outright:</b> <c>_driftRing</c>
///   (WallSegmentFade.Prepare.cs) and <c>_pathAuditWalls</c> both hold direct <c>Segment</c>
///   references ACROSS FRAMES, with their own cursors. Swap the table under them and the drift
///   probe measures the retired buffer while the wall-path audit reports on segments nobody
///   owns. Neither is committed state and neither may simply be carried; build 2 has to drop or
///   re-seat both AT the swap.</item>
/// <item><b>And one open question this build cannot settle:</b> <c>_sampleVisible</c> is
///   TICK-owned (so it is not in the table) but it is INDEX-ALIGNED to
///   <see cref="WallSegmentFade.FadeDriver.CommittedTable.AllSamples"/>, and no invalidation path for it could be found. On the swap frame
///   it is stale by construction. Build 2 must either force an <c>UpdateSampleVisibility</c>
///   pass before the next <c>BlockedFraction</c> or prove that it cannot matter.</item>
/// <item>and inside the table itself, <c>Segment.GateLift</c> is a reference to ANOTHER
///   <c>Segment</c>. A fresh table's gate lift must be re-pointed at the fresh partner, never
///   carried across — which is why the equality gate compares it by the partner's ANCHOR id
///   and not by reference (see <c>WallCommitDiff.FieldGateLift</c>).</item>
/// </list>
/// <para>None of that is solved here. It is written down here because the one way this change
/// goes badly wrong is for build 2 to discover it halfway.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>
        /// The committed state, as one object with one lifetime.
        ///
        /// <para>The collections are <c>readonly</c> and mutated in place, exactly as they were
        /// as driver fields — that is what makes the extraction a rename rather than a
        /// behaviour change. A future build that wants a SHADOW table allocates a second
        /// <see cref="CommittedTable"/> and assigns it to <see cref="_live"/>; nothing in this
        /// class needs to change for that, and nothing in this build does it.</para>
        /// </summary>
        internal sealed class CommittedTable
        {
            // ---- the segment table ----------------------------------------------------------

            /// <summary>Keyed by the ANCHOR component, and that key is the carry-forward
            /// identity: object identity is exact, unique, free, and already the thing any two
            /// tables agree on. <c>WireKey</c> is NOT an alternative — it is a 32-bit FNV over a
            /// name, a room label and a position quantised to 0.5 wu, lossy and collision-prone
            /// by construction and <c>0</c> for a dead anchor, built to survive across machines
            /// rather than to be unique inside one process. Use it for the wire and nothing
            /// else.</summary>
            internal readonly Dictionary<Component, Segment> Segments = new();

            /// <summary>Adopted-group anchors whose combined AABB was too FAT to act as a wall slab
            /// (both horizontal extents large — e.g. a tile whose wall pieces ring the room; the AABB
            /// would contain the room's own floor samples and read as 100% coverage forever). Their
            /// renderers are tracked as per-renderer segments instead; membership persists across
            /// rescans so those segments keep their smoothing state. Cleared on scene load.</summary>
            internal readonly HashSet<Component> SplitAnchors = new();

            // ---- the room registry ----------------------------------------------------------

            internal readonly List<Bounds> RoomBounds = new();
            internal readonly List<float> RoomFloorY = new();       // tile-anchored floor plane per room
            internal readonly List<bool> RoomFloorAnchored = new(); // true = from a CentralTile anchor
            internal readonly List<string> RoomLabels = new();      // per logical room (diag/census)

            /// <summary>Per-room floor-plane grid — the coverage decision's denominator.</summary>
            internal readonly List<Vector3> AllSamples = new();

            internal float SampleYMin, SampleYMax;                  // overall sample-height range (diag)
            internal int RoomsAnchored;                             // rooms with a tile-anchored plane (diag)

            /// <summary>How many room renderers the generator had when this table was built.
            /// The REVEAL edge: <c>Tick</c> opens an urgent cycle when the generator's count no
            /// longer matches it, and <c>BeginPrepareStage</c> uses the same comparison. It is
            /// committed state and not a cadence counter — a swap that left it behind would
            /// re-open a cycle every frame after a reveal.</summary>
            internal int BuiltRoomCount = -1;

            // ---- protection rects ------------------------------------------------------------

            internal readonly List<ArchRect> ArchRects = new();
            internal readonly List<WaterRect> WaterRects = new();

            // ---- the mounted union -----------------------------------------------------------

            internal readonly Dictionary<Renderer, UnionEntry> MountedUnion = new(64);

            /// <summary>Segments that OWN at least one renderer in the map — the per-frame
            /// short-circuit. Without it every applier would pay a dictionary probe per prop per
            /// frame just to learn that almost nothing overlaps anything; with it a segment none of
            /// whose dressing reaches into a foreign wall costs ONE hash lookup for the whole lane.
            /// A renderer is entered under the FIRST lane that offers it (one owner per renderer is
            /// already the subsystem's invariant), so a second owner would simply not see it — which
            /// is the status quo, never a wrong fade.</summary>
            internal readonly HashSet<Segment> UnionOwners = new();

            // ---- the board volume ------------------------------------------------------------
            //
            // ALL FOUR OF THESE, and three of them are the ones §3.1's list did not have. They
            // are written together by CommitBoardVolume and read together, every frame, by
            // UpdateInsideBoard and UpdateWalkInside. Splitting them across a swap puts the
            // walk-in stand-down on one table's geometry and its bars on another's.

            /// <summary>The board's own volume: union XZ footprint of every decision-valid room and
            /// its walls, Y from the board floor plane to the wall crest. Valid only when
            /// <see cref="BoardVolumeValid"/>.</summary>
            internal Bounds BoardVolume;
            internal bool BoardVolumeValid;

            /// <summary>Crest height above the floor plane (world units) — the yardstick both
            /// boundary bars are expressed in.</summary>
            internal float BoardCrestWU;

            /// <summary>Board floor plane (world units), the minimum over decision-valid rooms.</summary>
            internal float BoardFloorY;

            /// <summary>Decision-valid rooms the volume was built from. Read outside the commit
            /// by <c>UpdateWalkInside</c>, whose refusal line names it — a refusal that quotes a
            /// count from a table that is no longer in force is a diagnostic that lies.</summary>
            internal int BoardVolumeRooms;

            // ---- the sample indices ----------------------------------------------------------
            //
            // THESE TWO BELONG WITH AllSamples AND NOWHERE ELSE. They are indices INTO it, and
            // the coverage decision reads one from each side in the same expression
            // (RoomBlockedFraction: `start = RoomSampleStart[room]`, bounded by
            // `AllSamples.Count`). Split them across a swap and the decision that chooses which
            // walls fade runs on half of one table and half of another for one frame.

            internal readonly List<int> RoomSampleStart = new();    // first sample index per room
            internal readonly List<int> RoomSampleCount = new();    // grid size per room (denominator)

            // ---- committed state that lives in the other partials ----------------------------

            /// <summary>Fort/keep corner pieces. Read every frame by <c>ApplyCornerPieces</c>.
            ///
            /// <para>SWAP HAZARD, WRITTEN DOWN HERE BECAUSE IT HAS NO OTHER HOME: this list is
            /// also APPENDED TO BY AN APPLIER — <c>FastReclaimSweep</c> adds a corner piece
            /// between commits. So it is committed state that the tick mutates, and build 2's
            /// swap has to decide whether an applier's mid-cycle addition follows the table it
            /// was made against (it should) or is silently dropped by the swap (it must not
            /// be).</para></summary>
            internal readonly List<CornerPiece> CornerPieces = new();

            /// <summary>Segment anchors this rescan — the walk stops AT one rather than climbing
            /// through it, so a wall can never be swallowed into a "prop unit". Refreshed by
            /// <c>RefreshPropUnitAnchors</c> from BOTH scopes that need it: the prop-unit pass's,
            /// and — since the standing rule's FLOOR arm started using the same walk — the
            /// standing-prop scope, which opens at the very top of the rescan. That second writer
            /// is reached from the TICK's prepare stage as well as from the commit, which is the
            /// same hazard <see cref="CornerPieces"/> carries.</summary>
            internal readonly HashSet<Transform> PropUnitAnchors = new(64);

            /// <summary>Unit-root memo, keyed by the renderer's PARENT (siblings share an answer).
            /// Per-rescan only — Apparance rebirths these subtrees constantly, and a transform cached
            /// across rescans is a dangling reference within a couple of seconds
            /// (<c>WallSegmentFade.Standing.cs</c> pays for that lesson already). Read by
            /// <c>StaggerRootOf</c> on every prop-frame, which is what makes it committed state
            /// rather than commit scratch.</summary>
            internal readonly Dictionary<Transform, Transform?> PropUnitRootMemo = new(128);

            /// <summary>Where the first live room renderer stood when <c>CommitRoomRegistry</c> last
            /// built <see cref="RoomFloorY"/> — gate 3 of the commit-SKIP decision. See
            /// <c>BoardStillWhereTheFloorPlanesSayItIs</c>. A stale value here does not tear the
            /// table; it decides not to rebuild one, which is the quieter half of the same
            /// defect.</summary>
            internal Vector3 PrepBoardProbePos;
            internal bool PrepBoardProbeValid;
        }

        /// <summary>
        /// The table in force. <b>Assigned here and nowhere else in this build.</b>
        ///
        /// <para>It is not <c>readonly</c>, and that is the entire forward-looking content of
        /// this change: build 2 replaces this reference with a completed shadow table in one
        /// assignment, which is the only instant at which the appliers can observe a new table
        /// and is therefore the only place a tear could ever be introduced. Until then the
        /// single writer is this initialiser, and <c>check-frame-order.sh</c> can keep saying
        /// that committed state becomes visible exactly when the commit ends.</para>
        /// </summary>
        private CommittedTable _live = new();
    }
}
