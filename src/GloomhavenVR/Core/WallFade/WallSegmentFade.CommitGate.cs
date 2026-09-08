using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// PERF B, STEP 3 — HOW MUCH THE TABLE ACTUALLY CHURNS, MEASURED ON HIS HARDWARE.
///
/// <para><b>WHAT THIS IS NOT, FIRST, BECAUSE THE DIFFERENCE IS THE WHOLE HONESTY OF THE
/// BUILD.</b> The design note's build-1 plan
/// (.planning/perf/WALL-COMMIT-ARCHITECTURE.md §3.8) is to run the sliced builder producing a
/// SHADOW table beside the live one, discard the shadow, and have the gate compare
/// fresh-against-incremental. <b>That is not what this does, and it could not have been built
/// safely in this round.</b> The reason is in the source and not in the schedule:</para>
/// <list type="bullet">
/// <item>The twenty-four commit phases are <b>not pure functions of the scene</b>. They mutate
///   at least fifteen driver-level ledgers that live outside the table — <c>_claimedRenderers</c>,
///   <c>_siblingOwned</c>, <c>_mountedTouched</c>, <c>_mountedAnchorLedger</c>,
///   <c>_mountedMobile</c>, <c>_attachmentOwned</c>, <c>_mountedOwned</c>,
///   <c>_mountedUnitHome</c>, <c>_propUnitOwnerLast</c>, the standing and node memos, the census
///   accumulators — and a second run in the same cycle would see every one of them already
///   rewritten by the first.</item>
/// <item><c>CollectWallMountedProps</c> clears <c>_mountedOwned</c>, <c>_attachmentOwned</c> and
///   <c>_mountedReleased</c> at the top of its pass and RESTORES (via <c>RestoreProp</c>, which
///   removes the <c>_mountedTouched</c> entry) every prop it no longer owns.
///   <c>_mountedTouched</c>, <c>_mountedAnchorLedger</c> and <c>_mountedMobile</c> are CROSS-COMMIT
///   ledgers — cleared only on teardown (<c>RestoreAllMountedProps</c>) or at their caps
///   (WallSegmentFade.Mounted.cs). Running the pass twice per cycle therefore restores against a
///   half-updated ownership and re-baselines the mobility differential: the second run restores
///   props the first run is still holding, or fails to restore props nothing else will.</item>
/// <item>Six phases perform Unity WRITES — <c>SetPropertyBlock(null)</c>, <c>RestoreProp</c>,
///   <c>Object.Destroy</c> via <c>RestorePropSwap</c>. A shadow run repeats every one of them.</item>
/// <item>And <c>IsMobileProp</c> is a CROSS-COMMIT differential whose baseline the shadow run
///   would consume.</item>
/// </list>
/// <para>Making a shadow run safe therefore needs a write-suppression flag threaded through
/// every phase plus a save/restore of ~15 collections, on a subsystem whose fade behaviour the
/// user called perfect two days ago — and it needs the slicing machinery to already exist, or it
/// doubles the 95 ms hitch instead of removing it. <b>A build advertised as "changes nothing on
/// screen" that carries fifteen chances to silently corrupt the live ledger is not the safe
/// half of a two-build plan.</b> So this build does not claim to answer "does a fresh build
/// equal an incremental one". It answers the question the carry-forward actually has to survive,
/// with zero risk, and it says so in its own log line rather than letting a number stand in for
/// an answer it did not measure.</para>
///
/// <para><b>WHAT IT DOES MEASURE, AND WHY THAT IS THE RIGHT BUILD-1 NUMBER.</b> It snapshots the
/// committed table immediately BEFORE the commit and immediately AFTER it, and runs
/// <see cref="WallCommitDiff"/> over the pair. That is a read-only walk; it cannot change a
/// pixel. What it reports is the CHURN one commit puts into the table — which is precisely the
/// input the four snap cases are defined over:</para>
/// <list type="number">
/// <item><b>Case 1 frequency:</b> how many segments a commit DROPS while they are mid-fade.
///   Every one of those, under B without a carry-forward, is a renderer left holding a
///   MaterialPropertyBlock that nothing will ever take back — a permanently half-transparent
///   wall. If this count is routinely zero, case 1 is a theoretical risk. If it is not, it is
///   the shipping blocker and the number says how big.</item>
/// <item><b>Case 2 population:</b> how many anchors survive a commit and therefore need their
///   animation state carried.</item>
/// <item><b>Case 3 frequency:</b> how many surviving segments change their RENDERER SET, and how
///   many renderers leave and join per commit.</item>
/// <item><b>Case 4 frequency:</b> how many renderers change OWNER between two segments — the
///   class the design note does not name, and the one no per-segment carry can see.</item>
/// </list>
///
/// <para><b>WHAT IT CANNOT TELL US, STATED PLAINLY.</b> Churn between two INCREMENTAL tables is
/// not the same question as agreement between an incremental table and a FRESH one. A commit
/// that mutates in place carries state a from-scratch build would not have. This measures the
/// first and not the second, and no reading of it licenses build 2 on its own. It does two other
/// things that build 2 needs and that nothing else can supply: it exercises
/// <see cref="WallCommitDiff"/> on REAL tables at REAL sizes on the target CPU (so its 0.14 ms
/// CI figure stops being a desktop number), and it puts the four case counts in front of a human
/// before anyone writes the carry-forward that has to handle them.</para>
///
/// <para><b>NO HEADSET WAS INVOLVED IN ANY NUMBER IN THIS FILE.</b> The cost figures quoted
/// anywhere in this round come from a desktop CI box. What this instrument costs on a Quest 3
/// is unmeasured, which is why it reports its own cost under its own <c>PerfMonitor</c> step
/// rather than asserting it is cheap.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>The table as it stood immediately BEFORE the commit.</summary>
        private readonly WallCommitDiff.TableFacts _gateBefore = new();

        /// <summary>The table as it stands immediately AFTER it.</summary>
        private readonly WallCommitDiff.TableFacts _gateAfter = new();

        private readonly WallCommitDiff.Diff _gateDiff = new();
        private readonly WallCommitDiff.Scratch _gateScratch = new();

        /// <summary>How many name groups each class of the report lists. Eight is what the
        /// signature-culprit census uses, for the same reason: enough that a real pattern is
        /// visible, and every group beyond it is COUNTED rather than dropped.</summary>
        private const int GateTopGroups = 8;

        /// <summary>Change-gate for the line. A commit happens roughly every two seconds and the
        /// report is long, so it prints when its SHAPE changes — and, whatever happens, at least
        /// once every <see cref="GateHeartbeatSeconds"/>, because a change-gated line with a
        /// constant value prints once and then reads as a stopped instrument. This project has a
        /// ledger entry for exactly that.</summary>
        private string _gateLastShape = string.Empty;
        private float _gateNextHeartbeat;
        private const float GateHeartbeatSeconds = 20f;

        /// <summary>Commits this gate has judged, and how many of them found any difference at
        /// all — the denominator pair that turns "it found nothing" from a claim into a
        /// reading.</summary>
        private int _gateCommitsJudged;
        private int _gateCommitsWithChurn;

        /// <summary>Worst per-commit case counts seen since the last line. WORST and not mean:
        /// one commit that drops four mid-fade walls and forty that drop none are the same mean
        /// and completely different risks, and a summary stat that hides the tail is a defect
        /// class this project has paid for.</summary>
        private int _gateWorstMidFadeDrops;
        private int _gateWorstMoved;
        private int _gateWorstLeaving;
        private float _gateWorstMillis;

        /// <summary>Is the churn gate armed? Live, and safe before Bind.</summary>
        private static bool CommitTableGateOn => WallFadeTuning.CommitTableGateOn;

        /// <summary>
        /// Take the BEFORE snapshot. Called from <c>Rescan</c>, before the commit runs. Returns
        /// false when the gate is off, so the caller skips the AFTER half too — an AFTER with no
        /// BEFORE would compare a full table against an empty one and print a spectacular,
        /// entirely fictional churn figure.
        /// </summary>
        private bool BeginCommitTableGate()
        {
            if (!CommitTableGateOn || _gateDisarmed)
                return false;
            // A DIAGNOSTIC MAY NEVER TAKE THE COMMIT DOWN WITH IT. This runs inside Rescan, and
            // the AFTER half runs inside its finally; an exception escaping either one aborts a
            // commit and, in the finally's case, replaces whatever the commit was already
            // throwing. This project's ledger has both halves of that lesson — an unguarded
            // Update starving input on one NRE, and a thrown listener amputating the rest of a
            // chain. The catch DISARMS the gate for the session rather than retrying, because a
            // gate that throws once will throw every two seconds forever, and 19,853 frames of
            // the same exception is a defect class this repository has already paid for.
            try
            {
                using (PerfMonitor.Scope("WallFade.TableGate"))
                    SnapshotCommittedTable(_gateBefore);
                return true;
            }
            catch (System.Exception e)
            {
                DisarmCommitTableGate("taking the BEFORE snapshot", e);
                return false;
            }
        }

        /// <summary>Take the AFTER snapshot, diff it against the BEFORE one, and report.</summary>
        private void EndCommitTableGate(float now)
        {
            try { EndCommitTableGateCore(now); }
            catch (System.Exception e) { DisarmCommitTableGate("taking the AFTER snapshot or "
                                                              + "diffing the pair", e); }
        }

        /// <summary>Set when the gate has thrown. It is a SESSION latch and not a retry counter:
        /// see the note in <see cref="BeginCommitTableGate"/>.</summary>
        private bool _gateDisarmed;

        private void DisarmCommitTableGate(string what, System.Exception e)
        {
            if (_gateDisarmed)
                return;
            _gateDisarmed = true;
            VRLog.Warn("WALL TABLE GATE DISARMED FOR THIS SESSION: it threw while " + what
                + ". The wall fade itself is untouched — this instrument reads the table and "
                + "writes nothing — but every WALL TABLE GATE line after this point is missing, "
                + "so a session that ends without one has NOT reported agreement, it has "
                + "reported nothing. " + e);
        }

        private void EndCommitTableGateCore(float now)
        {
            using (PerfMonitor.Scope("WallFade.TableGate"))
            {
                float startMs = (float)RescanClock.Elapsed.TotalMilliseconds;
                SnapshotCommittedTable(_gateAfter);
                WallCommitDiff.Compare(_gateBefore, _gateAfter, _gateDiff, _gateScratch,
                    GateTopGroups);
                float ms = (float)RescanClock.Elapsed.TotalMilliseconds - startMs;

                _gateCommitsJudged++;
                if (!_gateDiff.FoundNothing)
                    _gateCommitsWithChurn++;
                if (_gateDiff.OnlyInOldMidFade > _gateWorstMidFadeDrops)
                    _gateWorstMidFadeDrops = _gateDiff.OnlyInOldMidFade;
                if (_gateDiff.Moved.Count > _gateWorstMoved)
                    _gateWorstMoved = _gateDiff.Moved.Count;
                if (_gateDiff.Leaving.Count > _gateWorstLeaving)
                    _gateWorstLeaving = _gateDiff.Leaving.Count;
                if (ms > _gateWorstMillis)
                    _gateWorstMillis = ms;

                // The SHAPE, not the whole line: two commits that drop the same counts of the
                // same classes are the same finding, and reprinting a 900-character paragraph
                // every two seconds is how a useful instrument becomes a thing people filter out.
                string shape = _gateDiff.OnlyInOld + "/" + _gateDiff.OnlyInNew + "/"
                             + _gateDiff.OwnershipChanged + "/" + _gateDiff.OnlyInOldMidFade + "/"
                             + _gateDiff.Leaving.Count + "/" + _gateDiff.Joining.Count + "/"
                             + _gateDiff.Moved.Count + "/" + _gateDiff.StructuralFieldDiffs;
                bool due = now >= _gateNextHeartbeat;
                if (shape == _gateLastShape && !due)
                    return;
                _gateLastShape = shape;
                _gateNextHeartbeat = now + GateHeartbeatSeconds;

                VRLog.Info(WallCommitDiff.Format(_gateDiff, "the table BEFORE this commit vs the "
                        + "table AFTER it", _gateBefore.Count, _gateAfter.Count)
                    + " WHAT THIS IS AND IS NOT: this is the CHURN one commit puts into the "
                    + "table, measured read-only. It is NOT the sliced-vs-atomic comparison PERF "
                    + "B ultimately needs — a commit that mutates in place carries state a "
                    + "from-scratch build would not have, and nothing here has run a from-scratch "
                    + "build. It IS the exact population the carry-forward has to survive. "
                    + "OVER THIS WINDOW: " + _gateCommitsJudged + " commit(s) judged, "
                    + _gateCommitsWithChurn + " of them churned the table; WORST SINGLE COMMIT "
                    + "(not the mean — one commit that drops four mid-fade walls and forty that "
                    + "drop none share a mean and share no risk) dropped "
                    + _gateWorstMidFadeDrops + " mid-fade segment(s), unowned "
                    + _gateWorstLeaving + " renderer(s) and moved " + _gateWorstMoved
                    + " renderer(s) between owners. THE GATE ITSELF cost at worst "
                    + _gateWorstMillis.ToString("F2") + " ms of a commit that costs ~95 ms, and "
                    + "reports as the 'WallFade.TableGate' step. READ IT LIKE THIS: a worst-case "
                    + "mid-fade drop of 0 across a long session means snap case 1 is theoretical "
                    + "and the carry-forward is cheap insurance; anything above 0 is the number "
                    + "of permanently half-transparent walls PERF B would ship without one.");

                _gateCommitsJudged = 0;
                _gateCommitsWithChurn = 0;
                _gateWorstMidFadeDrops = 0;
                _gateWorstMoved = 0;
                _gateWorstLeaving = 0;
                _gateWorstMillis = 0f;
            }
        }

        /// <summary>
        /// Copy the live committed table into a Unity-free snapshot.
        ///
        /// <para>IDENTITY IS <c>GetInstanceID</c> AND THE GUARD IS A MANAGED-NULL GUARD, NOT
        /// Unity's <c>==</c>. That is deliberate and it is the one judgement call in this
        /// method. Unity's <c>==</c> reports a DESTROYED object as null; using it here would
        /// silently drop a renderer the table still references, and the table's references are
        /// exactly what the carry-forward operates on — a destroyed renderer that a segment
        /// still owns is a real entry in the undo log and must appear in the snapshot. So the
        /// snapshot records what the table POINTS AT, and a renderer destroyed between the two
        /// snapshots shows up as identical rather than as a leaver, which is the truthful
        /// answer to "did the commit change this table".</para>
        ///
        /// <para>COST: one pass over every owned renderer, ~2,500 at the logged table size, of
        /// an int read and a list append into pooled buffers. No allocation in the steady state
        /// — <see cref="WallCommitDiff.TableFacts.Reset"/> keeps the records and their backing
        /// arrays.</para>
        /// </summary>
        private void SnapshotCommittedTable(WallCommitDiff.TableFacts into)
        {
            into.Reset();
            foreach (KeyValuePair<Component, Segment> kv in _live.Segments)
            {
                Segment seg = kv.Value;
                WallCommitDiff.SegmentFacts f = into.Rent();
                // The DICTIONARY KEY, not seg.Anchor: the key is what the table is indexed by
                // and what a carry-forward would match on, and it is a live managed reference by
                // construction (a dictionary cannot hold a null key), so its instance id is
                // always readable even after Unity has destroyed the object behind it.
                // The `!` is not a shortcut: Unity's `==` below is a LIFETIME test ("has the
                // native object been destroyed"), not a reference test, and the compiler's flow
                // analysis reads it as the latter and decides the key might be managed-null
                // three lines up. It cannot be — a Dictionary refuses a null key — so the id
                // read is safe and the suppression states exactly that.
                f.AnchorId = kv.Key!.GetInstanceID();
                // AND HERE UNITY'S `==` IS EXACTLY RIGHT, WHICH IS WHY THE TWO LINES DISAGREE.
                // GetInstanceID() reads a field cached in the managed wrapper and is safe on a
                // destroyed object; `.name` is an interop call into a native object that no
                // longer exists and throws MissingReferenceException. The BEFORE snapshot runs
                // ahead of CommitDeadSegments, so it WILL see destroyed anchors — that is the
                // normal case, not an edge one, and an unguarded read here would throw inside
                // the commit's own finally block every time a room is torn down.
                f.Name = kv.Key == null ? "<destroyed anchor>" : kv.Key!.name;

                AddRenderers(f.Owned[WallCommitDiff.KindRenderers], seg.Renderers);
                AddRenderers(f.Owned[WallCommitDiff.KindFoliage], seg.Foliage);
                AddRenderers(f.Owned[WallCommitDiff.KindSiblings], seg.Siblings);
                AddProps(f.Owned[WallCommitDiff.KindMounted], seg.Mounted);
                AddProps(f.Owned[WallCommitDiff.KindBody], seg.Body);
                AddProps(f.Owned[WallCommitDiff.KindStacked], seg.Stacked);
                AddProps(f.Owned[WallCommitDiff.KindUnitDressing], seg.UnitDressing);

                f.Fade = seg.Fade;
                f.State = seg.State;
                f.PendingRaw = seg.PendingRaw;
                f.PendingSince = seg.PendingSince;
                f.Smooth = seg.Smooth;
                f.SmoothInit = seg.SmoothInit;
                f.HasBlock = seg.HasBlock;
                f.FoliageState = seg.FoliageState;
                f.SiblingState = seg.SiblingState;
                f.MountedState = seg.MountedState;
                f.BodyState = seg.BodyState;
                f.StackedState = seg.StackedState;
                f.UnitDressingState = seg.UnitDressingState;
                f.RunDriven = seg.RunDriven;

                f.Bounds = seg.Bounds;
                f.HasBounds = seg.HasBounds;
                f.RoomIndex = seg.RoomIndex;
                f.Engulfing = seg.Engulfing;
                f.IsGateColumn = seg.IsGateColumn;
                f.FromSplitRun = seg.FromSplitRun;
                f.RunOwnerId = IdOf(seg.RunOwner);
                f.DoorRootId = IdOf(seg.DoorRoot);
                // BY THE PARTNER'S ANCHOR, NEVER BY THE Segment REFERENCE. Two tables never
                // share Segment instances, so comparing references would report every linked
                // gate column as different and make the gate useless in exactly the
                // configuration it exists for.
                f.GateLiftAnchorId = seg.GateLift != null ? IdOf(seg.GateLift.Anchor) : 0;
                f.HeldCutoff = seg.HeldCutoff;
                f.WireKey = seg.WireKey;

                f.Seal();
            }
        }

        /// <summary>Managed-null guard only — see the note on
        /// <see cref="SnapshotCommittedTable"/> for why Unity's <c>==</c> is wrong here.</summary>
        private static int IdOf(Object? o) => o is null ? 0 : o.GetInstanceID();

        private static void AddRenderers(List<int> into, List<MeshRenderer> from)
        {
            for (int i = 0; i < from.Count; i++)
            {
                MeshRenderer r = from[i];
                if (r is null)
                    continue;
                into.Add(r.GetInstanceID());
            }
        }

        private static void AddProps(List<int> into, List<MountedProp> from)
        {
            for (int i = 0; i < from.Count; i++)
            {
                MountedProp p = from[i];
                if (p is null || p.Renderer is null)
                    continue;
                into.Add(p.Renderer.GetInstanceID());
            }
        }
    }
}
