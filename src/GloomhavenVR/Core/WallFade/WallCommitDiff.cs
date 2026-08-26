using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WHETHER TWO WALL TABLES ARE THE SAME TABLE — the equality gate for PERF B (slice the
/// ~95 ms commit), and the renderer-level carry-forward plan that the slice cannot ship
/// without.
///
/// <para><b>THE USER REPORT THIS ROUND SERVES, verbatim (2026-08-25, after ModBuild 277).</b>
/// <i>"Ich will aber eigentlich gar keine spürbaren Ruckler - nicht nur seltenere. Kannst du
/// das Neuaufbauen der Tabelle nicht irgendwie in einen separaten Thread auslagern, so dass
/// das Hauptspiel nicht in Mitleidenschaft gezogen wird?"</i> He was offered four options and
/// picked A and B. A shipped in ModBuild 280 and is statistical — it makes the hitch rarer,
/// which is the thing he said is not enough. B is the one that removes it: the commit costs
/// 94.8 ms in ONE atomic frame at CV under 1 % (WallCache 30.8, PropUnits 29.1, Mounted 24.5,
/// the other 21 phases ≈ 10.4), and spreading that over ~63 frames at 1.5 ms each puts it
/// under the 11.11 ms budget of every one of them. Unity's scene graph is single-threaded, so
/// the literal answer to his question ("a separate thread") is no; slicing is the same result
/// by the only route the engine allows, and that is what he should be told.</para>
///
/// <para><b>WHY B CANNOT SIMPLY DOUBLE-BUFFER THE DICTIONARY.</b> The segment table is not a
/// derived view of the scene. <b>It is the undo log for every write this subsystem has made to
/// it.</b> <c>HasBlock</c>, <c>FoliageState</c>, <c>SiblingState</c>, <c>MountedState</c>,
/// <c>BodyState</c>, <c>StackedState</c>, <c>UnitDressingState</c>, <c>PrevRenderers</c>,
/// <c>PrevFoliage</c>, <c>PrevSiblings</c>, <c>PrevMounted</c>, <c>PrevBody</c>,
/// <c>PrevStacked</c>, <c>PrevUnitDressing</c> are all records of mutations ALREADY APPLIED to
/// live Unity renderers. Build a fresh table and throw the old one away, and the undo log goes
/// with it: every renderer the old table was holding mid-dissolve keeps the
/// MaterialPropertyBlock it last had, for the rest of the session. A permanently
/// half-transparent wall, and nothing in the subsystem can ever notice.</para>
///
/// <para><b>THE FOUR SNAP CASES.</b> The design note (.planning/perf/WALL-COMMIT-ARCHITECTURE.md
/// §3.4) names three. Working through the seven ownership classes below turned up a fourth,
/// and it is the one with no symptom until a prop changes wall:</para>
/// <list type="number">
/// <item><b>Case 1 — a segment in OLD and not in NEW, mid-fade.</b> Nothing animates it,
///   nothing restores it, the renderer freezes at whatever cutoff it had.
///   Answered by <see cref="Diff.Leaving"/>.</item>
/// <item><b>Case 2 — a segment in BOTH whose animation state is not carried.</b> <c>Fade</c>
///   resets to 0 while the renderer still carries a cutoff MPB: the wall snaps solid, then
///   fades again. Answered by <see cref="Diff.Carried"/>.</item>
/// <item><b>Case 3 — a segment in BOTH whose RENDERER LIST changed.</b> Old owns {A,B}, new
///   owns {A,C}. Carrying <c>Fade</c> fixes A. B is now unowned and freezes (case 1 at
///   renderer granularity). C has no MPB, while a carried <c>HasBlock = true</c> tells the
///   applier one is already there, so C never fades. Answered by <see cref="Diff.Leaving"/>
///   plus <see cref="Diff.Joining"/> — which is why the carry-forward is a RENDERER-level
///   operation and not a segment-level one.</item>
/// <item><b>Case 4 — a renderer owned in BOTH tables, by DIFFERENT segments.</b> Not in the
///   design note. It is invisible to both of the sets above (the renderer is owned throughout,
///   so it is neither a leaver nor a joiner) and it is not fixed by the per-segment carry
///   either: the block state that says whether this renderer carries an MPB lives on the
///   segment, so a renderer moving from a segment with <c>HasBlock = true</c> to one carrying
///   a carried <c>HasBlock = false</c> is a renderer nobody will ever restore. The wall
///   changes owner every time <c>EnforcePropUnitCohesion</c> re-adjudicates a statue between
///   two walls, which is a photographed, four-times-reported defect class in this subsystem
///   already (.planning/debug/skelet.jpg). Answered by <see cref="Diff.Moved"/>.</item>
/// </list>
///
/// <para><b>SEVEN OWNERSHIP CLASSES, NOT FOUR.</b> §3.8 proposes comparing <c>Renderers</c> /
/// <c>Foliage</c> / <c>Siblings</c> / <c>Mounted</c>. A segment also owns <c>Body</c> (the
/// plain child meshes of a cache wall with no fade-capable renderer), <c>Stacked</c> (fort and
/// keep superstructures) and <c>UnitDressing</c> (the prop-unit cohesion pass's regrouped
/// pieces) — each with its own <c>Prev*</c> list and its own <c>*State</c> undo flag, i.e.
/// each is an independent undo log with exactly the same three failure modes. A gate that
/// walks four of seven is an instrument that agrees with a torn table, which is the shape of
/// defect this project has a standing ledger entry for.</para>
///
/// <para><b>WHY THE ANCHOR AND NOT <c>WireKey</c>.</b> The carry-forward identity is the
/// dictionary key: <c>_live.Segments</c> is keyed by the anchor <c>Component</c>, and object
/// identity is exact, unique, free, and already the thing both tables agree on. <c>WireKey</c>
/// is a 32-bit FNV over an anchor name, a room label and a position quantised to 0.5 world
/// units (WallSegmentFade.Net.cs) — lossy and collision-prone by construction, <c>0</c> for a
/// dead anchor, and designed to survive ACROSS MACHINES, which is a far weaker requirement
/// than uniqueness inside one process. It is on the comparison list below as a FIELD (a wire
/// key that changed is a real difference a peer would see) and it is never used as identity.
/// This file speaks in <c>GetInstanceID</c> values for the same reason
/// <c>WallSegmentFadeCulprits</c> does: an id is comparable after the object it named has been
/// destroyed, and it keeps the file free of Unity's object model so it can be driven in CI.
/// </para>
///
/// <para><b>WHY IT IS A PURE FILE.</b> This project's ledger says a new instrument's first
/// output is a hypothesis, and it has lost sixteen builds on one defect to instruments that
/// were believed on sight. The verdict this one produces — "the sliced table and the atomic
/// table agree" — is the whole safety argument for changing a fade behaviour the user has just
/// called perfect. So it is written free of Unity's object model (Bounds, a value type, is the
/// one exception and comes from the game's own CoreModule) and driven in the wire suite
/// against a NULL input (two identical tables ⇒ it must find nothing AND say that finding
/// nothing is a statement about itself) and a known positive of every difference class — see
/// <c>WallCommitDiffVectors</c>. And, for the same ledger reasons — <i>a truncated list is not
/// absence</i>, <i>a summary stat is not the field</i> — every list it prints carries a group
/// count and an explicit elision count, never a bare top-N ending in an ellipsis.</para>
///
/// <para><b>WHAT IT DOES NOT DO.</b> It never swaps a table and it never writes to a renderer.
/// It compares, and it produces a PLAN. Executing that plan — restoring the leavers, marking
/// the joiners, re-homing the movers — is the driver's job, in a later build, once this one's
/// output has come back from hardware saying the two tables agree.</para>
/// </summary>
internal static class WallCommitDiff
{
    // ---- ownership classes ---------------------------------------------------------------

    internal const int KindRenderers = 0;
    internal const int KindFoliage = 1;
    internal const int KindSiblings = 2;
    internal const int KindMounted = 3;
    internal const int KindBody = 4;
    internal const int KindStacked = 5;
    internal const int KindUnitDressing = 6;

    /// <summary>How many ownership classes a segment has. Named so the loops and the name
    /// array below cannot drift from each other — which is exactly how a subsystem ends up
    /// walking four of its seven undo logs and calling the answer equality.</summary>
    internal const int KindCount = 7;

    /// <summary>Human names, index = kind.</summary>
    internal static readonly string[] KindNames =
    {
        "wall renderers", "foliage", "asset siblings", "mounted dressing",
        "plain wall body", "stacked shell", "unit dressing",
    };

    // ---- the fields the gate compares on a segment present in both tables ------------------
    //
    // TWO GROUPS, AND THE DISTINCTION IS LOAD-BEARING. The ANIMATION fields (Fade..RunDriven)
    // are what case 2 carries forward; a difference in them between a fresh build and the
    // incremental one is EXPECTED and is not a defect — a fresh table starts at Fade 0 by
    // construction. The STRUCTURAL fields (Bounds..WireKey) are what the fresh build is
    // supposed to reproduce exactly; a difference in one of those is the gate firing for real.
    // They are counted separately for that reason and the report says which group moved.

    internal const int FieldFade = 0;
    internal const int FieldState = 1;
    internal const int FieldPendingRaw = 2;
    internal const int FieldPendingSince = 3;
    internal const int FieldSmooth = 4;
    internal const int FieldSmoothInit = 5;
    internal const int FieldHasBlock = 6;
    internal const int FieldFoliageState = 7;
    internal const int FieldSiblingState = 8;
    internal const int FieldMountedState = 9;
    internal const int FieldBodyState = 10;
    internal const int FieldStackedState = 11;
    internal const int FieldUnitDressingState = 12;
    internal const int FieldRunDriven = 13;
    /// <summary>Index of the first STRUCTURAL field — everything below this is animation
    /// state, everything from here up is what a fresh build must reproduce.</summary>
    internal const int FirstStructuralField = 14;
    internal const int FieldBounds = 14;
    internal const int FieldHasBounds = 15;
    internal const int FieldRoomIndex = 16;
    internal const int FieldEngulfing = 17;
    internal const int FieldIsGateColumn = 18;
    internal const int FieldFromSplitRun = 19;
    internal const int FieldRunOwner = 20;
    internal const int FieldDoorRoot = 21;
    internal const int FieldGateLift = 22;
    internal const int FieldHeldCutoff = 23;
    internal const int FieldWireKey = 24;
    internal const int FieldCount = 25;

    /// <summary>Human names, index = field.</summary>
    internal static readonly string[] FieldNames =
    {
        "Fade", "State", "PendingRaw", "PendingSince", "Smooth", "SmoothInit", "HasBlock",
        "FoliageState", "SiblingState", "MountedState", "BodyState", "StackedState",
        "UnitDressingState", "RunDriven",
        "Bounds", "HasBounds", "RoomIndex", "Engulfing", "IsGateColumn", "FromSplitRun",
        "RunOwner", "DoorRoot", "GateLift", "HeldCutoff", "WireKey",
    };

    // ---- the snapshot ---------------------------------------------------------------------

    /// <summary>
    /// One segment, as the gate sees it. A plain class rather than a struct because the driver
    /// POOLS these — a snapshot is taken every commit and the steady state must allocate
    /// nothing (see <see cref="TableFacts.Rent"/>).
    ///
    /// <para>Deliberately not a Unity type. Every renderer is its <c>GetInstanceID</c>, every
    /// component reference is one too, and the only Unity type here is <c>Bounds</c>, a value
    /// type from the game's own CoreModule that runs perfectly well outside Unity.</para>
    /// </summary>
    internal sealed class SegmentFacts
    {
        /// <summary><c>Anchor.GetInstanceID()</c> — the dictionary key, and the identity the
        /// carry-forward matches on. 0 is not a legal Unity instance id, so 0 means "no anchor",
        /// which is itself a difference worth reporting rather than a value to match on.</summary>
        internal int AnchorId;

        /// <summary>The anchor's name, for the grouped report only. Never compared: two walls
        /// can share a name and a rename is not a table difference.</summary>
        internal string? Name;

        /// <summary>Owned renderer instance ids, per ownership class. Sorted ascending by
        /// <see cref="Seal"/> so set comparison is a linear merge with no allocation.</summary>
        internal readonly List<int>[] Owned = NewOwned();

        // --- animation / undo-log state (the case-2 carry set) ---
        internal float Fade;
        internal bool State;
        internal bool PendingRaw;
        internal float PendingSince;
        internal float Smooth;
        internal bool SmoothInit;
        internal bool HasBlock;
        internal int FoliageState;
        internal int SiblingState;
        internal int MountedState;
        internal int BodyState;
        internal int StackedState;
        internal int UnitDressingState;
        internal bool RunDriven;

        // --- structural state (what a fresh build must reproduce) ---
        internal Bounds Bounds;
        internal bool HasBounds;
        internal int RoomIndex;
        internal bool Engulfing;
        internal bool IsGateColumn;
        internal bool FromSplitRun;
        /// <summary>Instance id of the run owner anchor, 0 = none.</summary>
        internal int RunOwnerId;
        /// <summary>Instance id of the door root transform, 0 = none.</summary>
        internal int DoorRootId;
        /// <summary>Instance id of the gate-lift partner's ANCHOR, 0 = none. The anchor and not
        /// the Segment object: two tables never share Segment instances, so comparing the
        /// references would report every segment as different.</summary>
        internal int GateLiftAnchorId;
        internal float HeldCutoff;
        internal uint WireKey;

        internal void Reset()
        {
            AnchorId = 0;
            Name = null;
            for (int k = 0; k < KindCount; k++)
                Owned[k].Clear();
            Fade = 0f; State = false; PendingRaw = false; PendingSince = 0f;
            Smooth = 0f; SmoothInit = false; HasBlock = false;
            FoliageState = 0; SiblingState = 0; MountedState = 0;
            BodyState = 0; StackedState = 0; UnitDressingState = 0; RunDriven = false;
            Bounds = default; HasBounds = false; RoomIndex = -1;
            Engulfing = false; IsGateColumn = false; FromSplitRun = false;
            RunOwnerId = 0; DoorRootId = 0; GateLiftAnchorId = 0;
            HeldCutoff = 0f; WireKey = 0u;
        }

        /// <summary>Put every ownership list in ascending order. Called once per segment after
        /// the driver has filled it, so the set comparisons below are order-free — a table
        /// built by a different phase order owns the same renderers in a different sequence,
        /// and a gate that called that a difference would fire on every single cycle.</summary>
        internal void Seal()
        {
            for (int k = 0; k < KindCount; k++)
                Owned[k].Sort();
        }

        private static List<int>[] NewOwned()
        {
            var a = new List<int>[KindCount];
            for (int k = 0; k < KindCount; k++)
                a[k] = new List<int>(8);
            return a;
        }
    }

    /// <summary>
    /// One whole committed table, as the gate sees it. Pooled: <see cref="Reset"/> keeps the
    /// <see cref="SegmentFacts"/> objects and their list backing arrays and only empties them,
    /// so after the first commit of a session a snapshot allocates nothing at all.
    /// </summary>
    internal sealed class TableFacts
    {
        private readonly List<SegmentFacts> _pool = new(160);
        private int _used;

        /// <summary>Segments in this snapshot, in whatever order the driver walked them.</summary>
        internal IReadOnlyList<SegmentFacts> Segments => _ordered;

        private readonly List<SegmentFacts> _ordered = new(160);

        internal int Count => _used;

        internal void Reset()
        {
            for (int i = 0; i < _used; i++)
                _pool[i].Reset();
            _used = 0;
            _ordered.Clear();
        }

        /// <summary>Take the next pooled segment record, already cleared.</summary>
        internal SegmentFacts Rent()
        {
            if (_used == _pool.Count)
                _pool.Add(new SegmentFacts());
            SegmentFacts s = _pool[_used++];
            _ordered.Add(s);
            return s;
        }
    }

    // ---- the report -----------------------------------------------------------------------

    /// <summary>A group of differing segments sharing one anchor name, and how many.</summary>
    internal readonly struct Group
    {
        internal Group(string name, int count)
        {
            Name = name;
            Count = count;
        }

        internal readonly string Name;
        internal readonly int Count;
    }

    /// <summary>One renderer whose ownership changed between the two tables.</summary>
    internal readonly struct OwnedMove
    {
        internal OwnedMove(int rendererId, int kind, int fromAnchorId, int toAnchorId)
        {
            RendererId = rendererId;
            Kind = kind;
            FromAnchorId = fromAnchorId;
            ToAnchorId = toAnchorId;
        }

        internal readonly int RendererId;
        internal readonly int Kind;
        /// <summary>0 when the renderer is a JOINER (owned by nobody in the old table).</summary>
        internal readonly int FromAnchorId;
        /// <summary>0 when the renderer is a LEAVER (owned by nobody in the new table).</summary>
        internal readonly int ToAnchorId;
    }

    /// <summary>
    /// What the two tables disagree about, and the plan that would reconcile them. Every count
    /// is taken over the WHOLE population; the lists are a presentation cap and the elision
    /// counters state exactly what they left out.
    /// </summary>
    internal sealed class Diff
    {
        // --- segment-level census ---
        /// <summary>Anchors in the OLD table only. Every one is a case-1 candidate.</summary>
        internal int OnlyInOld;
        /// <summary>Anchors in the NEW table only.</summary>
        internal int OnlyInNew;
        /// <summary>Anchors in both whose OWNERSHIP sets differ in any class (case 3).</summary>
        internal int OwnershipChanged;
        /// <summary>Anchors in both whose compared FIELDS differ (either group).</summary>
        internal int FieldsChanged;
        /// <summary>Anchors in both that are identical in every respect this gate compares.</summary>
        internal int Identical;

        /// <summary>Anchors in the OLD table only that were mid-fade — <c>Fade &gt; 0</c> or
        /// any non-zero undo flag. THIS is the number that decides whether B is safe: each one
        /// is a renderer left holding a MaterialPropertyBlock that nothing will ever take back.
        /// A table where this is always 0 is a table the carry-forward barely has to work for;
        /// a table where it is routinely non-zero is the case-1 defect waiting to ship.</summary>
        internal int OnlyInOldMidFade;

        /// <summary>How many segments differ on each field. Index = field; a segment that
        /// differs on three fields counts under all three, so these sum to at least
        /// <see cref="FieldsChanged"/> by construction. Split at
        /// <see cref="FirstStructuralField"/>: below it is animation state a fresh build is
        /// EXPECTED to differ on, above it is what the gate is actually asking about.</summary>
        internal readonly int[] FieldDiffs = new int[FieldCount];

        /// <summary>How many segments differ in each ownership class. Index = kind.</summary>
        internal readonly int[] OwnershipDiffs = new int[KindCount];

        // --- renderer-level plan (the carry-forward, §3.4) ---
        /// <summary>Renderers owned in the OLD table and by NOBODY in the new one. The restore
        /// list: cases 1 and 3. <see cref="OwnedMove.ToAnchorId"/> is 0 for all of these.</summary>
        internal readonly List<OwnedMove> Leaving = new();
        /// <summary>Renderers owned in the NEW table and by nobody in the old one. The
        /// mark-dirty list: case 3's other half. <see cref="OwnedMove.FromAnchorId"/> is 0.</summary>
        internal readonly List<OwnedMove> Joining = new();
        /// <summary>Renderers owned in BOTH tables by DIFFERENT anchors — case 4. Neither a
        /// leaver nor a joiner, and the one class the design note did not name.</summary>
        internal readonly List<OwnedMove> Moved = new();
        /// <summary>Anchors present in both tables: the case-2 carry-forward list.</summary>
        internal readonly List<int> Carried = new();

        /// <summary>Total owned renderers on each side — the denominator for everything above,
        /// and the number that says whether a walk of 0 leavers means "nothing left" or "the
        /// snapshot was empty". A ratio with two populations is a defect class this project has
        /// paid for; both populations are therefore printed.</summary>
        internal int OwnedInOld, OwnedInNew;

        // --- grouped, capped, and honest about the cap ---
        internal readonly List<Group> OnlyInOldGroups = new();
        internal readonly List<Group> OnlyInNewGroups = new();
        internal readonly List<Group> ChangedGroups = new();
        internal int OnlyInOldGroupCount, OnlyInNewGroupCount, ChangedGroupCount;
        internal int OnlyInOldElidedGroups, OnlyInNewElidedGroups, ChangedElidedGroups;
        internal int OnlyInOldElidedSegments, OnlyInNewElidedSegments, ChangedElidedSegments;

        /// <summary>True when the gate found NO difference of ANY kind. The caller must print
        /// this LOUDLY and with the instrument's own denominators beside it — see
        /// <see cref="Format"/>. Silence from a gate is the single most dangerous output any
        /// instrument in this subsystem can produce.</summary>
        internal bool FoundNothing =>
            OnlyInOld == 0 && OnlyInNew == 0 && OwnershipChanged == 0 && FieldsChanged == 0;

        /// <summary>Differences in the STRUCTURAL half only — the ones that indict the fresh
        /// build rather than describing the animation state it has not inherited yet.</summary>
        internal int StructuralFieldDiffs
        {
            get
            {
                int n = 0;
                for (int f = FirstStructuralField; f < FieldCount; f++)
                    n += FieldDiffs[f];
                return n;
            }
        }

        internal void Reset()
        {
            OnlyInOld = OnlyInNew = OwnershipChanged = FieldsChanged = Identical = 0;
            OnlyInOldMidFade = 0;
            OwnedInOld = OwnedInNew = 0;
            Array.Clear(FieldDiffs, 0, FieldCount);
            Array.Clear(OwnershipDiffs, 0, KindCount);
            Leaving.Clear();
            Joining.Clear();
            Moved.Clear();
            Carried.Clear();
            OnlyInOldGroups.Clear();
            OnlyInNewGroups.Clear();
            ChangedGroups.Clear();
            OnlyInOldGroupCount = OnlyInNewGroupCount = ChangedGroupCount = 0;
            OnlyInOldElidedGroups = OnlyInNewElidedGroups = ChangedElidedGroups = 0;
            OnlyInOldElidedSegments = OnlyInNewElidedSegments = ChangedElidedSegments = 0;
        }
    }

    /// <summary>
    /// Reusable scratch for <see cref="Compare"/>, owned by the caller so the gate can run
    /// every commit without allocating. Nothing in it survives a call.
    /// </summary>
    internal sealed class Scratch
    {
        internal readonly Dictionary<int, SegmentFacts> ByAnchor = new(160);
        internal readonly Dictionary<long, int> OwnerInOld = new(4096);
        internal readonly Dictionary<long, int> OwnerInNew = new(4096);
        internal readonly Dictionary<string, int> OnlyOldBy = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> OnlyNewBy = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> ChangedBy = new(StringComparer.Ordinal);
        /// <summary>Anchor ids present in the NEW table — the leaver walk's membership test.
        /// A field and not a local so the gate can run every commit without allocating.</summary>
        internal readonly HashSet<int> InNew = new(160);

        internal void Reset()
        {
            ByAnchor.Clear();
            InNew.Clear();
            OwnerInOld.Clear();
            OwnerInNew.Clear();
            OnlyOldBy.Clear();
            OnlyNewBy.Clear();
            ChangedBy.Clear();
        }
    }

    /// <summary>Ownership is keyed by (renderer, class) and not by renderer alone: the same
    /// renderer legitimately appears as one segment's wall renderer and never as another's
    /// foliage, but the seven classes have seven independent undo logs and a piece that moves
    /// BETWEEN classes needs the old class restored and the new one marked. Packing the pair
    /// into a long keeps that exact without a tuple allocation per renderer.</summary>
    private static long OwnKey(int rendererId, int kind) =>
        ((long)(uint)rendererId << 3) | (uint)kind;

    private static int KindOf(long key) => (int)(key & 7L);

    private static int RendererOf(long key) => (int)(uint)(key >> 3);

    /// <summary>
    /// Compare two committed tables and produce both the difference census and the
    /// renderer-level reconciliation plan. <paramref name="oldTable"/> is the one currently in
    /// force (the undo log); <paramref name="newTable"/> is the candidate.
    ///
    /// <para>ORDER-FREE in both directions: segments are matched by anchor id and ownership by
    /// sorted set merge, so two tables built by different phase orders compare equal. That is
    /// not a nicety — a sliced build visits its phases on different frames and a gate that
    /// reported a reordering as a difference would fire on every cycle and mean nothing.</para>
    ///
    /// <para>COST: one dictionary insert and one probe per owned renderer, plus one merge pass
    /// per segment per ownership class. At the observed table sizes (99–127 segments, ~1,900
    /// owned renderers) that is a few thousand operations. Measured, not assumed — see
    /// <c>WallCommitDiffVectors</c>, which times it on a synthetic table of the logged size.</para>
    /// </summary>
    /// <param name="topGroups">How many name groups each class lists. The rest are counted,
    /// never dropped silently.</param>
    internal static void Compare(
        TableFacts oldTable, TableFacts newTable, Diff into, Scratch scratch, int topGroups)
    {
        into.Reset();
        scratch.Reset();

        // --- index the old table by anchor, and both tables by owned renderer ---
        IReadOnlyList<SegmentFacts> olds = oldTable.Segments;
        IReadOnlyList<SegmentFacts> news = newTable.Segments;

        for (int i = 0; i < olds.Count; i++)
        {
            SegmentFacts s = olds[i];
            scratch.ByAnchor[s.AnchorId] = s;
            for (int k = 0; k < KindCount; k++)
            {
                List<int> ids = s.Owned[k];
                for (int j = 0; j < ids.Count; j++)
                {
                    into.OwnedInOld++;
                    scratch.OwnerInOld[OwnKey(ids[j], k)] = s.AnchorId;
                }
            }
        }
        for (int i = 0; i < news.Count; i++)
        {
            SegmentFacts s = news[i];
            for (int k = 0; k < KindCount; k++)
            {
                List<int> ids = s.Owned[k];
                for (int j = 0; j < ids.Count; j++)
                {
                    into.OwnedInNew++;
                    scratch.OwnerInNew[OwnKey(ids[j], k)] = s.AnchorId;
                }
            }
        }

        // --- segment-level census ---
        for (int i = 0; i < news.Count; i++)
        {
            SegmentFacts n = news[i];
            if (!scratch.ByAnchor.TryGetValue(n.AnchorId, out SegmentFacts? o))
            {
                into.OnlyInNew++;
                Tally(scratch.OnlyNewBy, GroupKey(n.Name));
                continue;
            }
            into.Carried.Add(n.AnchorId);
            bool ownershipDiff = false;
            for (int k = 0; k < KindCount; k++)
            {
                if (SameSet(o.Owned[k], n.Owned[k]))
                    continue;
                ownershipDiff = true;
                into.OwnershipDiffs[k]++;
            }
            int fieldsBefore = CountFieldDiffs(o, n, into.FieldDiffs);
            if (ownershipDiff)
                into.OwnershipChanged++;
            if (fieldsBefore > 0)
                into.FieldsChanged++;
            if (!ownershipDiff && fieldsBefore == 0)
                into.Identical++;
            else
                Tally(scratch.ChangedBy, GroupKey(n.Name));
        }

        // Leavers at SEGMENT granularity — walked over the old table, so a new table that is
        // empty (a build that produced nothing) is loudly visible rather than silently equal.
        for (int i = 0; i < news.Count; i++)
            scratch.InNew.Add(news[i].AnchorId);
        for (int i = 0; i < olds.Count; i++)
        {
            SegmentFacts o = olds[i];
            if (scratch.InNew.Contains(o.AnchorId))
                continue;
            into.OnlyInOld++;
            if (MidFade(o))
                into.OnlyInOldMidFade++;
            Tally(scratch.OnlyOldBy, GroupKey(o.Name));
        }

        // --- renderer-level plan: the union of owned renderers across BOTH tables ---
        foreach (KeyValuePair<long, int> kv in scratch.OwnerInOld)
        {
            if (!scratch.OwnerInNew.TryGetValue(kv.Key, out int toAnchor))
            {
                into.Leaving.Add(
                    new OwnedMove(RendererOf(kv.Key), KindOf(kv.Key), kv.Value, 0));
            }
            else if (toAnchor != kv.Value)
            {
                into.Moved.Add(
                    new OwnedMove(RendererOf(kv.Key), KindOf(kv.Key), kv.Value, toAnchor));
            }
        }
        foreach (KeyValuePair<long, int> kv in scratch.OwnerInNew)
        {
            if (!scratch.OwnerInOld.ContainsKey(kv.Key))
            {
                into.Joining.Add(
                    new OwnedMove(RendererOf(kv.Key), KindOf(kv.Key), 0, kv.Value));
            }
        }

        Rank(scratch.OnlyOldBy, topGroups, into.OnlyInOldGroups,
            out into.OnlyInOldGroupCount, out into.OnlyInOldElidedGroups,
            out into.OnlyInOldElidedSegments);
        Rank(scratch.OnlyNewBy, topGroups, into.OnlyInNewGroups,
            out into.OnlyInNewGroupCount, out into.OnlyInNewElidedGroups,
            out into.OnlyInNewElidedSegments);
        Rank(scratch.ChangedBy, topGroups, into.ChangedGroups,
            out into.ChangedGroupCount, out into.ChangedElidedGroups,
            out into.ChangedElidedSegments);
    }

    /// <summary>Is this segment holding anything the subsystem would have to take back? Any
    /// non-zero undo flag counts, not only <c>Fade</c> — a wall at Fade 0 whose
    /// <c>MountedState</c> is still 2 is holding a torch hidden, and dropping it is exactly as
    /// permanent as dropping a half-dissolved wall.</summary>
    private static bool MidFade(SegmentFacts s) =>
        s.Fade > 0f || s.HasBlock || s.FoliageState != 0 || s.SiblingState != 0
        || s.MountedState != 0 || s.BodyState != 0 || s.StackedState != 0
        || s.UnitDressingState != 0;

    /// <summary>Linear merge over two ascending id lists. Both were put in order by
    /// <see cref="SegmentFacts.Seal"/>; if a caller forgets, this reports a difference rather
    /// than silently agreeing, which is the failure direction a gate should have.</summary>
    private static bool SameSet(List<int> a, List<int> b)
    {
        if (a.Count != b.Count)
            return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    private static int CountFieldDiffs(SegmentFacts o, SegmentFacts n, int[] tally)
    {
        int diffs = 0;
        // Every scalar is compared BITWISE, not with a tolerance. A fresh build that lands a
        // bounds centre one ULP away from the incremental one has done something different and
        // the gate exists to say so; a tolerance here would be a dial that quietly decides how
        // wrong the sliced build is allowed to be.
        Note(ref diffs, tally, FieldFade, o.Fade.Equals(n.Fade));
        Note(ref diffs, tally, FieldState, o.State == n.State);
        Note(ref diffs, tally, FieldPendingRaw, o.PendingRaw == n.PendingRaw);
        Note(ref diffs, tally, FieldPendingSince, o.PendingSince.Equals(n.PendingSince));
        Note(ref diffs, tally, FieldSmooth, o.Smooth.Equals(n.Smooth));
        Note(ref diffs, tally, FieldSmoothInit, o.SmoothInit == n.SmoothInit);
        Note(ref diffs, tally, FieldHasBlock, o.HasBlock == n.HasBlock);
        Note(ref diffs, tally, FieldFoliageState, o.FoliageState == n.FoliageState);
        Note(ref diffs, tally, FieldSiblingState, o.SiblingState == n.SiblingState);
        Note(ref diffs, tally, FieldMountedState, o.MountedState == n.MountedState);
        Note(ref diffs, tally, FieldBodyState, o.BodyState == n.BodyState);
        Note(ref diffs, tally, FieldStackedState, o.StackedState == n.StackedState);
        Note(ref diffs, tally, FieldUnitDressingState, o.UnitDressingState == n.UnitDressingState);
        Note(ref diffs, tally, FieldRunDriven, o.RunDriven == n.RunDriven);
        Note(ref diffs, tally, FieldBounds, SameBounds(o.Bounds, n.Bounds));
        Note(ref diffs, tally, FieldHasBounds, o.HasBounds == n.HasBounds);
        Note(ref diffs, tally, FieldRoomIndex, o.RoomIndex == n.RoomIndex);
        Note(ref diffs, tally, FieldEngulfing, o.Engulfing == n.Engulfing);
        Note(ref diffs, tally, FieldIsGateColumn, o.IsGateColumn == n.IsGateColumn);
        Note(ref diffs, tally, FieldFromSplitRun, o.FromSplitRun == n.FromSplitRun);
        Note(ref diffs, tally, FieldRunOwner, o.RunOwnerId == n.RunOwnerId);
        Note(ref diffs, tally, FieldDoorRoot, o.DoorRootId == n.DoorRootId);
        Note(ref diffs, tally, FieldGateLift, o.GateLiftAnchorId == n.GateLiftAnchorId);
        Note(ref diffs, tally, FieldHeldCutoff, o.HeldCutoff.Equals(n.HeldCutoff));
        Note(ref diffs, tally, FieldWireKey, o.WireKey == n.WireKey);
        return diffs;
    }

    private static void Note(ref int diffs, int[] tally, int field, bool same)
    {
        if (same)
            return;
        diffs++;
        tally[field]++;
    }

    /// <summary>Bitwise, component by component. <c>Bounds.Equals</c> would do, but it goes
    /// through <c>Vector3.Equals</c>, which is a tolerance comparison in some Unity versions;
    /// this asks the question the gate means.</summary>
    private static bool SameBounds(Bounds a, Bounds b) =>
        a.center.x.Equals(b.center.x) && a.center.y.Equals(b.center.y)
        && a.center.z.Equals(b.center.z)
        && a.size.x.Equals(b.size.x) && a.size.y.Equals(b.size.y) && a.size.z.Equals(b.size.z);

    /// <summary>Unity appends this to an instantiated prefab's name; two copies of one prefab
    /// group together, so it is stripped for the grouping key only.</summary>
    private const string CloneSuffix = "(Clone)";

    /// <summary>The grouping key for an anchor name. Deliberately almost nothing — strip a
    /// trailing <c>(Clone)</c> and otherwise group by the EXACT name. Stripping trailing digits
    /// would merge 'Wall 2' and 'Wall 3', which are different walls and exactly the distinction
    /// this report exists to make.</summary>
    internal static string GroupKey(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return "<unnamed>";
        string n = name!.Trim();
        if (n.EndsWith(CloneSuffix, StringComparison.Ordinal))
            n = n.Substring(0, n.Length - CloneSuffix.Length).TrimEnd();
        return n.Length == 0 ? "<unnamed>" : n;
    }

    private static void Tally(Dictionary<string, int> by, string key)
    {
        by.TryGetValue(key, out int n);
        by[key] = n + 1;
    }

    /// <summary>Sort by count descending, then by name so two runs of the same scene print the
    /// same line, take the top N — and report how many groups AND how many segments were left
    /// out. That last part is the whole difference between this and a list ending in an
    /// ellipsis, which this project has a ledger entry about.</summary>
    private static void Rank(
        Dictionary<string, int> by, int topGroups, List<Group> into,
        out int groupCount, out int elidedGroups, out int elidedSegments)
    {
        groupCount = by.Count;
        elidedGroups = 0;
        elidedSegments = 0;
        if (by.Count == 0)
            return;
        var all = new List<Group>(by.Count);
        foreach (KeyValuePair<string, int> kv in by)
            all.Add(new Group(kv.Key, kv.Value));
        all.Sort(static (a, b) => a.Count != b.Count
            ? b.Count.CompareTo(a.Count)
            : string.CompareOrdinal(a.Name, b.Name));
        int take = topGroups < 0 ? 0 : Math.Min(topGroups, all.Count);
        for (int i = 0; i < take; i++)
            into.Add(all[i]);
        for (int i = take; i < all.Count; i++)
        {
            elidedGroups++;
            elidedSegments += all[i].Count;
        }
    }

    // ---- the line ---------------------------------------------------------------------------

    /// <summary>
    /// The report. One string, self-describing, and it says something meaningful in all three
    /// states this instrument can be in: it found differences, it found none (which is a
    /// statement about the instrument and is printed as one), or it had nothing to compare.
    /// </summary>
    /// <param name="label">What the two tables ARE — "atomic vs sliced", "before vs after this
    /// commit". The reader cannot judge a difference count without knowing what was compared.</param>
    internal static string Format(Diff d, string label, int oldSegments, int newSegments)
    {
        var sb = new System.Text.StringBuilder(1024);
        sb.Append("WALL TABLE GATE (").Append(label).Append("): ");
        if (oldSegments == 0 && newSegments == 0)
        {
            sb.Append("BOTH TABLES WERE EMPTY, so this run compared nothing and proves nothing. "
                    + "It is not agreement. The commit had no table to snapshot — a scene load, "
                    + "the first cycle of a session, or a gate armed after the last commit ran.");
            return sb.ToString();
        }
        sb.Append(oldSegments).Append(" segment(s) / ").Append(d.OwnedInOld)
          .Append(" owned renderer(s) on the OLD side against ").Append(newSegments)
          .Append(" / ").Append(d.OwnedInNew).Append(" on the NEW side — ")
          .Append(d.OnlyInOld).Append(" only in OLD (").Append(d.OnlyInOldMidFade)
          .Append(" of them mid-fade), ").Append(d.OnlyInNew).Append(" only in NEW, ")
          .Append(d.OwnershipChanged).Append(" changed ownership, ")
          .Append(d.FieldsChanged).Append(" changed a compared field, ")
          .Append(d.Identical).Append(" identical.");

        if (d.FoundNothing)
        {
            sb.Append(" NO DIFFERENCE OF ANY KIND, AND WHAT THAT MEANS DEPENDS ENTIRELY ON THE "
                    + "DENOMINATORS ABOVE. With ").Append(d.OwnedInOld).Append('/')
              .Append(d.OwnedInNew).Append(" owned renderers on the two sides it is a real "
                    + "reading; with a denominator near zero it says only that the snapshot was "
                    + "empty. This gate compares ").Append(FieldCount).Append(" field(s) across ")
              .Append(KindCount).Append(" ownership class(es); anything outside that set could "
                    + "differ and this line would still print. Finding nothing is a statement "
                    + "about the instrument as much as about the tables.");
            return sb.ToString();
        }

        AppendClass(sb, "ONLY IN OLD", d.OnlyInOld, d.OnlyInOldGroups, d.OnlyInOldGroupCount,
            d.OnlyInOldElidedGroups, d.OnlyInOldElidedSegments);
        AppendClass(sb, "ONLY IN NEW", d.OnlyInNew, d.OnlyInNewGroups, d.OnlyInNewGroupCount,
            d.OnlyInNewElidedGroups, d.OnlyInNewElidedSegments);
        AppendClass(sb, "CHANGED", d.OwnershipChanged + d.FieldsChanged, d.ChangedGroups,
            d.ChangedGroupCount, d.ChangedElidedGroups, d.ChangedElidedSegments);

        bool any = false;
        for (int k = 0; k < KindCount; k++)
        {
            if (d.OwnershipDiffs[k] == 0)
                continue;
            if (!any)
                sb.Append(" WHICH OWNERSHIP CLASS MOVED (per-class tally over the segments "
                        + "present in both; a segment that moved two classes counts under "
                        + "both):");
            sb.Append(any ? ", " : " ").Append(d.OwnershipDiffs[k]).Append(" x '")
              .Append(KindNames[k]).Append('\'');
            any = true;
        }
        if (any)
            sb.Append('.');

        // The field tally, split at the structural boundary — the two halves mean opposite
        // things and a single ranked list would let an expected animation difference outrank
        // the one structural difference that indicts the build.
        AppendFields(sb, d, " ANIMATION FIELDS (a fresh table starts at rest, so differences "
                          + "here are EXPECTED and are what the carry-forward exists to fix):",
            0, FirstStructuralField);
        AppendFields(sb, d, " STRUCTURAL FIELDS (what a fresh build is supposed to reproduce "
                          + "exactly — every count here is a real disagreement):",
            FirstStructuralField, FieldCount);

        sb.Append(" CARRY-FORWARD PLAN: ").Append(d.Carried.Count)
          .Append(" anchor(s) carry animation state (case 2), ")
          .Append(d.Leaving.Count).Append(" renderer(s) LEAVE and must be restored (cases 1 "
                + "and 3), ").Append(d.Joining.Count)
          .Append(" JOIN and must be marked dirty (case 3), ").Append(d.Moved.Count)
          .Append(" are owned by BOTH tables under DIFFERENT anchors and must be re-homed "
                + "(case 4 — the class the design note did not name; a renderer that changes "
                + "owner is neither a leaver nor a joiner and no per-segment carry can see it).");
        return sb.ToString();
    }

    private static void AppendFields(
        System.Text.StringBuilder sb, Diff d, string heading, int from, int to)
    {
        bool any = false;
        for (int f = from; f < to; f++)
        {
            if (d.FieldDiffs[f] == 0)
                continue;
            if (!any)
                sb.Append(heading);
            sb.Append(any ? ", " : " ").Append(d.FieldDiffs[f]).Append(" x ")
              .Append(FieldNames[f]);
            any = true;
        }
        if (any)
            sb.Append('.');
        else if (from >= FirstStructuralField)
            sb.Append(" NO STRUCTURAL FIELD DIFFERED across the ")
              .Append(d.Carried.Count).Append(" shared anchor(s) — the strongest single "
                    + "statement this gate makes, and the one build 2 needs.");
    }

    private static void AppendClass(
        System.Text.StringBuilder sb, string label, int total, List<Group> groups,
        int groupCount, int elidedGroups, int elidedSegments)
    {
        if (total == 0)
            return;
        sb.Append(' ').Append(label).Append(" (").Append(total).Append(" segment(s) in ")
          .Append(groupCount).Append(" name group(s)):");
        for (int i = 0; i < groups.Count; i++)
            sb.Append(i == 0 ? " " : ", ").Append(groups[i].Count).Append(" x '")
              .Append(groups[i].Name).Append('\'');
        if (elidedGroups > 0)
            sb.Append(" — and ").Append(elidedGroups).Append(" further group(s) holding ")
              .Append(elidedSegments).Append(" segment(s) NOT LISTED (a cap, not an absence).");
        else
            sb.Append(" (every group listed).");
    }
}
