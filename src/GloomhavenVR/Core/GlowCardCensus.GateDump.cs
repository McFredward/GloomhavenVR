using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// GO TO WHERE IT LIVES — the unconditional, once-per-scene dump of everything at the gate.
/// Third partial of <see cref="GlowCardCensus"/>; emits <c>[Perf] GATE DUMP</c>, one log line per
/// record, and it is a DIFFERENT INSTRUMENT from <c>[Perf] GLOW CARDS</c> even though it shares the
/// formatting.
///
/// <para>WHY THIS EXISTS, AND WHAT IS WRONG WITH THE THING ABOVE IT. Five builds of
/// <c>[Perf] GLOW CARDS</c> have refined a RANKING — pixel-span, then shape, then a subject band,
/// then a self-lit requirement, then recurrence across windows — and the subject of the report has
/// never once appeared in the output. Refining a ranking cannot find an object that is not in the
/// population being ranked, and there are two independent ways for the subject to be absent, which
/// a ranking cannot tell apart:</para>
/// <list type="number">
/// <item><b>NEVER SAMPLED.</b> The census only collects on windows the <c>[Perf]</c> monitor
///   chooses to pay for. ModBuild 255's whole session produced five sampled windows, and the
///   best-sample latch shows they were pointed at figures, not at the gate.</item>
/// <item><b>REFUSED.</b> This one is structural and it is worth writing down, because nothing in
///   the census prints it. <see cref="GlowCardCensus.EndRenderer"/> pools a renderer only when
///   <c>_curMarks != Mark.None</c>. A renderer earns no mark at all when it is on an unmarked
///   layer, is drawn by the game camera too (so not <c>VrOnly</c>), carries a shader whose name
///   matches none of <c>ShaderMarks</c>, sits below queue 2900, is not unlit, and is either under
///   <see cref="GlowCardCensus.SubjectMinPx"/> = 24 px or not plate-shaped. Such a renderer is
///   dropped BEFORE scoring, is counted by no counter, and is therefore invisible in the log —
///   indistinguishable from a scene that does not contain it. An emissive-but-lit quad of 30 px on
///   a stone pier is exactly that shape.</item>
/// </list>
///
/// <para>WHAT THIS DOES INSTEAD. It stops describing the object and goes to its ADDRESS. The
/// wall-fade subsystem already resolves the gate props by identity; this walks the same population
/// from the same handle and dumps it WHOLE — no shape test, no size test, no self-lit test, no
/// queue test, no score, no ranking, no quota. Two tiers:</para>
/// <list type="bullet">
/// <item><b>Tier A — the gate subtree.</b> EVERY renderer under EVERY registered
///   <c>UnityGameEditorDoorProp</c>, printed with the full property/keyword/tag/texture dump. If
///   the rectangles are children of the gate they are in this tier and the report closes.</item>
/// <item><b>Tier B — the gate's neighbourhood.</b> Every renderer NOT in tier A whose bounds
///   centre lies within <see cref="GateNearRadiusWU"/> of any gate anchor, ordered by distance.
///   This is the tier that answers the OTHER branch: if tier A comes back and nothing in it can be
///   the subject, tier B already names what stands in front of the gate, in the SAME hardware run,
///   so the search does not need a sixth build to move.</item>
/// </list>
///
/// <para>HOW THE GATES ARE FOUND, WITHOUT TOUCHING THE WALL-FADE LANE.
/// <see cref="SceneRegistry.DoorProps"/> is a <c>ComponentRegistry&lt;UnityGameEditorDoorProp&gt;</c>
/// maintained by a Harmony postfix on the component's own <c>Start</c> and seeded once at install.
/// It is the SAME handle <c>WallSegmentFade.SeedGateColumns</c> reads to key its GATE COLUMNs, so
/// the prop set here and the prop set in the <c>GATE COLUMN 'ThickDoor : (guid)'</c> lines are the
/// same set by construction — and reading it costs a walk of ~10 entries rather than a
/// <c>FindObjectsOfType</c>. No file under <c>WallSegmentFade.*</c> is read or edited to get this.
/// Note that this dump does NOT apply the fade's arch/sliver logic: four of the six door props in
/// the ModBuild 255 log are SKIPPED by the fade as floor-level slivers, and they are dumped here
/// anyway. A prop the fade declines to protect is still a prop the subject could hang from.</para>
///
/// <para>WHY THE SCENE-WIDE HALF OF TIER B DOES NOT USE <c>FindObjectsOfType</c>. It cannot:
/// Apparance's generated containers are <c>HideAndDontSave</c>, and <c>FindObjectsOfType</c> skips
/// <c>DontSave</c> objects — the round-6 failure recorded in <see cref="MaterialLoaderHeal"/>, where
/// the sweep found nothing at all. The gate content lives under an Apparance
/// <c>Generated Content/Preview/…</c> subtree, so a sweep would miss precisely the objects this dump
/// is about. Tier B therefore walks HIERARCHIES: the active scene's root GameObjects, unioned with
/// the roots of every registered door prop, map tile and occlusion volume (which covers roots that
/// are themselves hidden or moved out of the scene list). A hierarchy walk has no hideFlags filter.</para>
///
/// <para>SELF-TIMED, ONCE, AND HONEST ABOUT TRUNCATION. The walk runs at most once per scene and
/// prints its own millisecond cost, the number of roots walked, the number of renderers walked, the
/// number matched in each tier and the number actually PRINTED. Every cap is a printed number: a
/// truncated dump can never be read as a complete one. If the door registry is empty or the gate
/// subtrees hold no renderers yet — Apparance generation is asynchronous and the first
/// <c>[Perf]</c> window can easily precede it — the attempt does NOT consume the once-per-scene
/// latch; it retries on the next window up to <see cref="GateMaxAttempts"/> times and then says in
/// words that it gave up. A gate dump that fired too early and printed an empty scene would be the
/// worst possible outcome, because it looks exactly like a decisive negative.</para>
///
/// <para>MP-SAFE: reads only. Nothing here writes a material, a property block, a transform or a
/// wire message.</para>
/// </summary>
internal static partial class GlowCardCensus
{
    // ==========================================================================================
    //  dials — deliberately consts and not PerfConfig entries
    // ==========================================================================================
    //
    // A PerfConfig entry needs a matching field in Defaults/, and Defaults/ belongs to another lane
    // mid-round on exactly the files that resolve the gate props. A one-shot diagnostic is also the
    // wrong shape for a user-facing dial: there is nothing to tune, it runs once and prints.

    /// <summary>Radius around a gate anchor that counts as "at the gate" for tier B, in world units.
    /// The ModBuild 255 arch rects are ~1.8 x 2.3 wu with a top at y 4.7, so 5 wu reaches the piers,
    /// the wall courses either side, and anything hovering in front of the doors without reaching
    /// across the room.</summary>
    private const float GateNearRadiusWU = 5f;

    /// <summary>Tier A records that get the full property/keyword/tag/texture dump. Tier A is the
    /// tier that must not be filtered, so this is set well above any plausible gate subtree; if it
    /// ever binds, the number that did not get a dump is printed.</summary>
    private const int GateMaxFullDumps = 64;

    /// <summary>Tier B records printed at all, nearest first.</summary>
    private const int GateMaxNearRecords = 48;

    /// <summary>Tier B records that get the full dump, nearest first. This is an ordering cap, not a
    /// property filter: no renderer is excluded for what it IS, only for being further from the gate
    /// than sixteen others.
    ///
    /// <para>WHERE THE COST OF THIS DUMP ACTUALLY IS, since it is not where it looks. The walk is
    /// cheap — the census's own standalone sweep priced 3040 renderers at 7.4 ms on this hardware
    /// (ModBuild 255 log) while doing strictly more per renderer than this walk does. The expensive
    /// part is the OUTPUT: a full dump reads up to <see cref="MaxProps"/> = 44 shader properties
    /// through the material marshal and then writes a multi-kilobyte line to BepInEx's log. That is
    /// why the full-dump caps are two figures and not three. The dump is a deliberate ONE-FRAME
    /// HITCH, once per scene, in an instrumented build; the footer prints the millisecond number so
    /// it is a measurement and not this comment's opinion.</para></summary>
    private const int GateMaxNearFullDumps = 16;

    /// <summary>Hard ceiling on renderers visited, so a pathological scene cannot turn a one-shot
    /// diagnostic into a hitch. Printed when it binds.</summary>
    private const int GateMaxWalk = 40_000;

    /// <summary>Windows the dump may wait for Apparance to generate the gate before giving up.</summary>
    private const int GateMaxAttempts = 12;

    // ==========================================================================================
    //  state
    // ==========================================================================================

    private static int _gateScene = int.MinValue + 1;
    private static bool _gateDone;
    private static int _gateAttempts;

    private static readonly List<UnityGameEditorDoorProp> GateDoors = new(8);
    private static readonly List<TilesOcclusionVolume> GateVolumes = new(16);
    private static readonly List<ProceduralMapTile> GateTiles = new(64);
    private static readonly List<Renderer> GateSubtree = new(64);
    private static readonly List<Material> GateSlots = new(8);
    private static readonly HashSet<int> GatePrinted = new(256);
    private static readonly List<Transform> GateRoots = new(64);
    private static readonly HashSet<int> GateRootIds = new(64);
    private static readonly List<Bounds> GateAnchors = new(8);
    private static readonly List<NearRecord> GateNear = new(256);

    private readonly struct NearRecord
    {
        public NearRecord(Renderer r, float dist)
        {
            R = r;
            Dist = dist;
        }

        public readonly Renderer R;
        public readonly float Dist;
    }

    private static readonly Comparison<NearRecord> ByDistance =
        (a, b) => a.Dist.CompareTo(b.Dist);

    // ---- borrowed window state, saved and put back ------------------------------------------
    private static Camera? _gateSavedHead;
    private static Vector3 _gateSavedHeadPos;
    private static float _gateSavedPixelsPerUnit;
    private static float _gateSavedEyePxW;
    private static float _gateSavedEyePxH;
    private static bool _gateSavedProjectionKnown;
    private static int _gateSavedHeadMask;
    private static bool _gateSavedHeadKnown;
    private static bool _gateSavedScenarioKnown;
    private static int _gateSavedScenarioMask;
    private static string _gateProjNote = string.Empty;

    /// <summary>The census's last <c>FindObjectsOfType&lt;Renderer&gt;</c> count, on a latch
    /// <see cref="Reset"/> does not clear — see the note at its assignment in
    /// <see cref="RunStandalone"/>. 0 means the sweep has not run in this session.</summary>
    private static int _lastSweepRenderers;

    /// <summary>
    /// Take the head camera's projection FOR THIS INSTANT, and remember what was there before.
    ///
    /// <para>THIS IS NOT DEFENSIVE PADDING, IT IS A CORRECTNESS FIX. The screen rectangle is the
    /// single field that identifies a record against the photograph, and it is computed from
    /// <c>_head</c>, <c>_headPos</c> and <c>_pixelsPerUnit</c> — window state that
    /// <see cref="Begin"/> latches. But <see cref="Begin"/> does not run on every window: when
    /// <c>PerfSceneProfile.AppendSceneLine</c> rations itself away AND
    /// <see cref="RunStandalone"/> is also rationed off (<c>_ownSkip &gt; 0</c>), nothing re-latches
    /// and those fields still hold a camera pose from several windows ago. A dump that inherited
    /// that would print rectangles for where the player's head USED TO BE, and they would look
    /// exactly like real ones. So the dump latches its own and puts the old values back, which also
    /// guarantees it cannot perturb the census line it shares a file with.</para>
    /// </summary>
    private static void LatchGateProjection()
    {
        _gateSavedHead = _head;
        _gateSavedHeadPos = _headPos;
        _gateSavedPixelsPerUnit = _pixelsPerUnit;
        _gateSavedEyePxW = _eyePxW;
        _gateSavedEyePxH = _eyePxH;
        _gateSavedProjectionKnown = _projectionKnown;
        _gateSavedHeadMask = _headMask;
        _gateSavedHeadKnown = _headKnown;
        _gateSavedScenarioKnown = _scenarioKnown;
        _gateSavedScenarioMask = _scenarioMask;

        // THE SCENARIO CAMERA MUST BE RE-RESOLVED HERE, and this is not housekeeping either.
        // Log()'s finally calls Reset(), which sets _scenarioKnown = false — and the dump runs after
        // Log(). Left alone, the VrOnly mark could never be set on any record, every VR-only
        // renderer would come back marks[None], and the "why the census never had these" count would
        // over-report REFUSAL for a reason that is an artefact of the reset rather than a property of
        // the scene. That count is the whole separator this dump adds; it must not be manufactured.
        try
        {
            Camera? scenario = ResolveScenarioCamera();
            if (scenario != null)
            {
                _scenarioMask = scenario.cullingMask;
                _scenarioKnown = true;
            }
            else
            {
                _scenarioKnown = false;
            }
        }
        catch (Exception)
        {
            _scenarioKnown = false;
        }

        Camera? head = null;
        try { head = Rig.VRRigDriver.HeadCamera; }
        catch (Exception) { head = null; }

        if (head == null)
        {
            _projectionKnown = false;
            _head = null;
            _gateProjNote = "NO HEAD CAMERA at dump time, so every 'screen:' field below reads n/a "
                            + "and every span is 0px — the AABBs are still exact and are the only "
                            + "way to match these records to the photograph";
            return;
        }

        _head = head;
        try
        {
            _headMask = head.cullingMask;
            _headKnown = true;
        }
        catch (Exception)
        {
            _headKnown = false;
        }
        LatchProjection(head);
        _gateProjNote = _projectionKnown
            ? "head '" + SafeName(head) + "' at (" + _headPos.x.ToString("F2") + ","
              + _headPos.y.ToString("F2") + "," + _headPos.z.ToString("F2")
              + "), projection latched AT DUMP TIME (not inherited from an older [Perf] window), "
              + _pixelsPerUnit.ToString("F0") + " px per world unit at 1 wu"
            : "head camera found but its projection is UNREADABLE, so 'screen:' reads n/a below";
    }

    private static void RestoreGateProjection()
    {
        _head = _gateSavedHead;
        _headPos = _gateSavedHeadPos;
        _pixelsPerUnit = _gateSavedPixelsPerUnit;
        _eyePxW = _gateSavedEyePxW;
        _eyePxH = _gateSavedEyePxH;
        _projectionKnown = _gateSavedProjectionKnown;
        _headMask = _gateSavedHeadMask;
        _headKnown = _gateSavedHeadKnown;
        _scenarioKnown = _gateSavedScenarioKnown;
        _scenarioMask = _gateSavedScenarioMask;
    }

    // ==========================================================================================
    //  entry
    // ==========================================================================================

    /// <summary>
    /// Called once per <c>[Perf]</c> window from <c>PerfSceneProfile.AppendGfxLine</c>, and does
    /// nothing on all but one of them. Its own try/catch on purpose: the caller's catch latches
    /// <c>_sceneProfileFaulted</c> and would take the SCENE and GFX lines down with this one for the
    /// rest of the session.
    /// </summary>
    internal static void GateDump()
    {
        try
        {
            RunGateDump();
        }
        catch (Exception e)
        {
            _gateDone = true;
            VRLog.Error(Scope, "GATE DUMP threw and will not be retried in this scene — the rest of "
                               + "the [Perf] lines are unaffected: " + e);
        }
    }

    private static void RunGateDump()
    {
        int scene;
        string sceneName;
        try
        {
            UnityEngine.SceneManagement.Scene s =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            scene = s.handle;
            sceneName = s.name;
        }
        catch (Exception)
        {
            return;
        }

        if (scene != _gateScene)
        {
            _gateScene = scene;
            _gateDone = false;
            _gateAttempts = 0;
        }
        if (_gateDone)
            return;

        _gateAttempts++;
        Stopwatch clock = Stopwatch.StartNew();

        SceneRegistry.DoorProps.Collect(GateDoors);
        if (GateDoors.Count == 0)
        {
            NoteGateNotReady(sceneName, "the door-prop registry is EMPTY (no live, active, "
                                        + "non-DontSave UnityGameEditorDoorProp)");
            return;
        }

        LatchGateProjection();
        try
        {
            DumpGateBody(sceneName, clock);
        }
        finally
        {
            RestoreGateProjection();
        }
    }

    private static void DumpGateBody(string sceneName, Stopwatch clock)
    {
        // ---- tier A: every renderer under every door prop, unconditionally --------------------
        GatePrinted.Clear();
        GateAnchors.Clear();
        var sb = new StringBuilder(4096);
        int tierAWalked = 0;
        int tierAPrinted = 0;
        int tierAFullDumps = 0;

        // THE SEPARATOR BETWEEN "NEVER SAMPLED" AND "REFUSED", counted while the records are built
        // and costing nothing extra. A renderer whose Marks come out Mark.None is one that
        // GlowCardCensus.EndRenderer would have dropped BEFORE scoring — not ranked low, DROPPED,
        // and counted by no counter in that line. If the gate's renderers come back marked, the
        // census could have seen them and never did (a sampling problem). If they come back
        // Mark.None, the census could never have seen them however often it sampled (a structural
        // refusal). Those are different bugs with different fixes and five builds could not tell
        // them apart.
        int unmarkedA = 0;
        int unmarkedB = 0;

        // The header goes out BEFORE the records so that a log truncated by a crash still says what
        // was being attempted and how many props it had.
        VRLog.Info(Scope, "GATE DUMP — attempt " + _gateAttempts + " in scene '" + sceneName
                          + "': " + GateDoors.Count + " door prop(s) from SceneRegistry.DoorProps "
                          + "(the same handle WallSegmentFade keys its GATE COLUMNs on). TIER A is "
                          + "every renderer under those props with NO filter of any kind — no shape "
                          + "test, no size test, no self-lit test, no queue test, no ranking, no "
                          + "quota. TIER B is everything else within " + GateNearRadiusWU.ToString("F1")
                          + " wu of a gate, nearest first. Read the FOOTER line for the counts: "
                          + "walked, matched and PRINTED are three different numbers. Projection: "
                          + _gateProjNote + ".");

        for (int d = 0; d < GateDoors.Count; d++)
        {
            UnityGameEditorDoorProp dp = GateDoors[d];
            if (dp == null)
                continue;
            Transform dt;
            try { dt = dp.transform; }
            catch (Exception) { continue; }

            GateSubtree.Clear();
            try { dt.GetComponentsInChildren(true, GateSubtree); }
            catch (Exception) { GateSubtree.Clear(); }

            Bounds anchor = new(dt.position, Vector3.zero);
            bool anchorSeeded = false;

            VRLog.Info(Scope, "GATE DUMP prop[" + d + "] '" + PathOf(dt) + "' at ("
                              + dt.position.x.ToString("F2") + "," + dt.position.y.ToString("F2")
                              + "," + dt.position.z.ToString("F2") + ") — " + GateSubtree.Count
                              + " renderer(s) in its subtree, including inactive. This prop is "
                              + "dumped whether or not the wall fade gave it a GATE COLUMN: four of "
                              + "the six props in the ModBuild 255 log are SKIPPED there as "
                              + "floor-level slivers, and a prop the fade declines to protect is "
                              + "still a prop the subject could hang from.");

            for (int i = 0; i < GateSubtree.Count; i++)
            {
                Renderer r = GateSubtree[i];
                if (r == null)
                    continue;
                tierAWalked++;
                if (!GatePrinted.Add(r.GetInstanceID()))
                    continue;   // two door props sharing a subtree — print it once

                Candidate c = BuildGateCandidate(r);
                if (c.Marks == Mark.None)
                    unmarkedA++;
                if (c.B.size != Vector3.zero)
                {
                    if (!anchorSeeded)
                    {
                        anchor = c.B;
                        anchorSeeded = true;
                    }
                    else
                    {
                        anchor.Encapsulate(c.B);
                    }
                }

                bool full = tierAFullDumps < GateMaxFullDumps;
                sb.Length = 0;
                sb.Append("GATE DUMP A[").Append(d).Append('.').Append(i).Append("] '")
                  .Append(PathOf(r.transform)).Append("' ");
                AppendIdentity(sb, c, r);
                AppendScreen(sb, c);
                AppendBlend(sb, c);
                AppendTint(sb, c, r);
                if (full)
                {
                    AppendProps(sb, c);
                    AppendPasses(sb, c);
                    tierAFullDumps++;
                }
                else
                {
                    sb.Append(" | (full property/pass dump withheld — the first ")
                      .Append(GateMaxFullDumps).Append(" tier-A records got one and this is past "
                                                       + "that cap, NOT past a filter)");
                }
                VRLog.Info(Scope, sb.ToString());
                tierAPrinted++;
            }

            GateAnchors.Add(anchor);
        }

        if (tierAWalked == 0)
        {
            NoteGateNotReady(sceneName, "the " + GateDoors.Count + " door prop(s) resolved but their "
                                        + "subtrees hold ZERO renderers (Apparance generation has "
                                        + "not produced the gate geometry yet)");
            return;
        }

        // ---- tier B: everything else near a gate anchor ----------------------------------------
        GateNear.Clear();
        int rootsWalked = 0;
        int tierBWalked = 0;
        bool walkCapped = false;
        CollectGateRoots();

        for (int i = 0; i < GateRoots.Count; i++)
        {
            Transform root = GateRoots[i];
            if (root == null)
                continue;
            rootsWalked++;
            GateSubtree.Clear();
            try { root.GetComponentsInChildren(true, GateSubtree); }
            catch (Exception) { continue; }

            for (int j = 0; j < GateSubtree.Count; j++)
            {
                Renderer r = GateSubtree[j];
                if (r == null)
                    continue;
                tierBWalked++;
                if (tierBWalked > GateMaxWalk)
                {
                    walkCapped = true;
                    break;
                }
                if (GatePrinted.Contains(r.GetInstanceID()))
                    continue;

                float dist = DistanceToNearestGate(r);
                if (dist > GateNearRadiusWU)
                    continue;
                if (!GatePrinted.Add(r.GetInstanceID()))
                    continue;
                GateNear.Add(new NearRecord(r, dist));
            }
            if (walkCapped)
                break;
        }

        GateNear.Sort(ByDistance);
        int nearPrinted = Mathf.Min(GateMaxNearRecords, GateNear.Count);
        for (int i = 0; i < nearPrinted; i++)
        {
            Renderer r = GateNear[i].R;
            if (r == null)
                continue;
            Candidate c = BuildGateCandidate(r);
            if (c.Marks == Mark.None)
                unmarkedB++;
            sb.Length = 0;
            sb.Append("GATE DUMP B[").Append(i).Append("] d=")
              .Append(GateNear[i].Dist.ToString("F2")).Append("wu '")
              .Append(PathOf(r.transform)).Append("' ");
            AppendIdentity(sb, c, r);
            AppendScreen(sb, c);
            AppendBlend(sb, c);
            AppendTint(sb, c, r);
            if (i < GateMaxNearFullDumps)
            {
                AppendProps(sb, c);
                AppendPasses(sb, c);
            }
            else
            {
                sb.Append(" | (full property/pass dump withheld — the nearest ")
                  .Append(GateMaxNearFullDumps).Append(" tier-B records got one; this is an "
                                                       + "ORDERING cap by distance, not a filter on "
                                                       + "what the renderer is)");
            }
            VRLog.Info(Scope, sb.ToString());
        }

        clock.Stop();
        double ms = clock.Elapsed.TotalMilliseconds;
        _gateDone = true;

        sb.Length = 0;
        sb.Append("GATE DUMP FOOTER — scene '").Append(sceneName).Append("', attempt ")
          .Append(_gateAttempts).Append(", ONE-SHOT, cost ").Append(ms.ToString("F1"))
          .Append("ms of ONE frame — a deliberate single hitch, mostly log I/O and material property "
                  + "marshalling, not the walk; if the headset stuttered once as this room opened, "
                  + "this line is why, and it will not run again in this scene")
          .Append(" | TIER A: ").Append(GateDoors.Count).Append(" door prop(s), ")
          .Append(tierAWalked).Append(" renderer(s) walked, ").Append(tierAPrinted)
          .Append(" PRINTED (").Append(tierAFullDumps).Append(" with a full property dump)")
          .Append(" | TIER B: ").Append(rootsWalked).Append(" hierarchy root(s), ")
          .Append(tierBWalked).Append(" renderer(s) walked, ").Append(GateNear.Count)
          .Append(" within ").Append(GateNearRadiusWU.ToString("F1")).Append("wu of a gate, ")
          .Append(nearPrinted).Append(" PRINTED");
        if (GateNear.Count > nearPrinted)
        {
            sb.Append(" ⇒ ").Append(GateNear.Count - nearPrinted)
              .Append(" NEAR RENDERER(S) WERE NOT PRINTED (past the ").Append(GateMaxNearRecords)
              .Append("-record cap, nearest kept) — THIS DUMP IS TRUNCATED and a subject could be "
                      + "in the part that was cut");
        }
        else
        {
            sb.Append(" ⇒ nothing was cut: every renderer within the radius is above");
        }
        if (walkCapped)
        {
            sb.Append(" | WALK CAPPED at ").Append(GateMaxWalk)
              .Append(" renderers — the hierarchy walk stopped early and tier B is INCOMPLETE");
        }

        // ---- why the census never had these, in two numbers -------------------------------------
        sb.Append(" | WHY [Perf] GLOW CARDS NEVER HAD THESE — ").Append(unmarkedA).Append(" of ")
          .Append(tierAPrinted).Append(" tier-A and ").Append(unmarkedB).Append(" of ")
          .Append(nearPrinted).Append(" printed tier-B record(s) carry marks[None]. A marks[None] "
                  + "renderer is REFUSED by GlowCardCensus.EndRenderer before it is ever scored, and "
                  + "no counter in that line records it, so it is invisible there however often the "
                  + "census samples. Anything NOT marks[None] here was merely NEVER SAMPLED — that is "
                  + "a rationing problem, not a filter problem, and the two want opposite fixes");

        if (_lastSweepRenderers > 0)
        {
            sb.Append(" | POPULATION CONTRAST: this hierarchy walk visited ").Append(tierBWalked)
              .Append(" renderer(s); the census's own FindObjectsOfType sweep last reported ")
              .Append(_lastSweepRenderers).Append(". FindObjectsOfType SKIPS DontSave-flagged objects "
                      + "and Apparance's generated containers are HideAndDontSave, so a hierarchy "
                      + "count MATERIALLY LARGER than the sweep count is direct evidence of a "
                      + "population the census cannot reach by construction");
        }
        else
        {
            sb.Append(" | POPULATION CONTRAST unavailable — the census's standalone "
                      + "FindObjectsOfType sweep has not run in this session, so there is no sweep "
                      + "count to compare this hierarchy count against");
        }

        sb.Append(" | THE THREE TARGETS, MEASURED OFF THE PHOTOGRAPH so the match is arithmetic and "
                  + "not an impression. schwebende_lichter.jpg is 3840x2160 and the pale pixels form "
                  + "exactly three connected regions, top-left origin: [1] x1626..1655 y1130..1205 "
                  + "(30x76 px), [2] x1922..2013 y1178..1281 (92x104 px), [3] x2440..2492 "
                  + "y1264..1384 (53x121 px). Mean colour of all three is near (225,220,175) — R and "
                  + "G equal, B about 50 lower: a pale cream with a slight yellow cast, NOT the warm "
                  + "orange of the candle rendering correctly in the same frame two metres away. "
                  + "Their height:width ratios are 2.5, 1.1 and 2.3, which is the fact worth carrying "
                  + "into the records: three copies of one object seen at one distance on one flat "
                  + "wall cannot foreshorten that differently, so either they are NOT coplanar with "
                  + "the wall or they are NOT three copies of one thing. The 7x crops agree — the "
                  + "middle one straddles the door planks AND the stone pier at a tilt neither "
                  + "surface has, which is what the user's word 'schwebend' is describing"
                  + " | HOW TO READ THIS. If a tier-A record's 'screen:' rect lands on one of those "
                  + "three boxes, the rectangles are GATE CHILDREN and the report closes on identity "
                  + "— that record's shader, its _MainTex (watch for the <NULL> case, which draws as "
                  + "a flat opaque white rectangle by itself), its blend and its keywords are all on "
                  + "the same line. If NO tier-A record can be the subject, that is EQUALLY decisive: "
                  + "the rectangles are not gate children, and tier B above has already named, in "
                  + "distance order, everything that stands in front of the gate instead. The one "
                  + "reading that means NOTHING is a footer with tier-A walked = 0, or a 'GATE DUMP "
                  + "GAVE UP' line: that is an empty gate, not an absent subject.");
        VRLog.Info(Scope, sb.ToString());
    }

    /// <summary>The dump is not ready yet. Does NOT consume the once-per-scene latch until the
    /// attempt budget is spent — see the class header on why an early empty dump is the worst
    /// possible outcome. Logs on the first attempt and on the last, and stays quiet in between so a
    /// slow-loading room does not fill the log with waiting.</summary>
    private static void NoteGateNotReady(string sceneName, string why)
    {
        if (_gateAttempts >= GateMaxAttempts)
        {
            _gateDone = true;
            VRLog.Info(Scope, "GATE DUMP GAVE UP in scene '" + sceneName + "' after "
                              + _gateAttempts + " attempt(s): " + why + ". NOTHING WAS DUMPED — this "
                              + "is not a negative result about the rectangles, it is the instrument "
                              + "saying it never got a gate to look at.");
            return;
        }
        if (_gateAttempts == 1)
        {
            VRLog.Info(Scope, "GATE DUMP waiting in scene '" + sceneName + "': " + why
                              + ". Retrying on later [Perf] windows (up to " + GateMaxAttempts
                              + "); the once-per-scene latch is NOT consumed by a not-ready attempt.");
        }
    }

    /// <summary>
    /// The hierarchy roots tier B walks. The active scene's roots, unioned with the roots of every
    /// registered door prop, occlusion volume and map tile — the last three because a root that has
    /// been moved out of the scene list or hidden would otherwise be missed, and because they are
    /// the roots the scenario content demonstrably hangs from. Union by transform instance id, so a
    /// root reached twice is walked once.
    /// </summary>
    private static void CollectGateRoots()
    {
        GateRoots.Clear();
        GateRootIds.Clear();

        try
        {
            GameObject[] roots =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] != null)
                    AddGateRoot(roots[i].transform);
            }
        }
        catch (Exception)
        {
            // an unreadable scene root list simply leaves the registry roots below
        }

        for (int i = 0; i < GateDoors.Count; i++)
        {
            if (GateDoors[i] != null)
                AddGateRoot(GateDoors[i].transform.root);
        }

        try
        {
            SceneRegistry.Volumes.Collect(GateVolumes);
            for (int i = 0; i < GateVolumes.Count; i++)
            {
                if (GateVolumes[i] != null)
                    AddGateRoot(GateVolumes[i].transform.root);
            }
        }
        catch (Exception)
        {
            // optional source
        }

        try
        {
            SceneRegistry.MapTiles.Collect(GateTiles);
            for (int i = 0; i < GateTiles.Count; i++)
            {
                if (GateTiles[i] != null)
                    AddGateRoot(GateTiles[i].transform.root);
            }
        }
        catch (Exception)
        {
            // optional source
        }
    }

    private static void AddGateRoot(Transform? t)
    {
        if (t == null)
            return;
        if (GateRootIds.Add(t.GetInstanceID()))
            GateRoots.Add(t);
    }

    /// <summary>Distance from this renderer to the nearest gate anchor box, 0 inside it. Uses
    /// <c>Bounds.SqrDistance</c> against the anchor rather than centre-to-centre, so a long wall
    /// course that reaches the gate is near it even though its centre is not.</summary>
    private static float DistanceToNearestGate(Renderer r)
    {
        Vector3 p;
        try { p = r.bounds.center; }
        catch (Exception) { return float.MaxValue; }

        float best = float.MaxValue;
        for (int i = 0; i < GateAnchors.Count; i++)
        {
            float sq = GateAnchors[i].SqrDistance(p);
            if (sq < best)
                best = sq;
        }
        return best >= float.MaxValue ? float.MaxValue : Mathf.Sqrt(Mathf.Max(best, 0f));
    }

    /// <summary>
    /// Build the record for one renderer, with the SAME fields the census's own records carry so the
    /// two outputs read the same — but with none of the census's gating. Every field is filled from
    /// the renderer itself; nothing here can refuse a renderer.
    ///
    /// <para>The representative material slot is chosen exactly as <see cref="Offer"/> chooses it
    /// (most diagnostic slot, not slot 0): a two-slot prop whose second material is the glowing card
    /// must not be described by its first, boring material.</para>
    /// </summary>
    private static Candidate BuildGateCandidate(Renderer r)
    {
        var c = new Candidate { R = r };

        bool submitted;
        try { submitted = r.enabled && r.gameObject.activeInHierarchy && r.isVisible; }
        catch (Exception) { submitted = false; }
        c.Submitted = submitted;

        int layer;
        try { layer = r.gameObject.layer; }
        catch (Exception) { layer = 0; }

        Mark marks = Mark.None;
        for (int i = 0; i < MarkedLayers.Length; i++)
        {
            if (MarkedLayers[i] == layer)
            {
                marks |= Mark.Layer;
                break;
            }
        }
        if (_scenarioKnown && (_headMask & (1 << layer)) != 0 && (_scenarioMask & (1 << layer)) == 0)
            marks |= Mark.VrOnly | Mark.Layer;

        try
        {
            Bounds b = r.bounds;
            c.B = b;
            Vector3 s = b.size;
            float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            float thinnest = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            if (longest > 0f && !float.IsNaN(longest) && _projectionKnown)
            {
                float dist = (b.center - _headPos).magnitude;
                if (dist > 0.0001f && !float.IsNaN(dist))
                {
                    float px = longest * _pixelsPerUnit / dist;
                    if (!float.IsNaN(px) && !float.IsInfinity(px))
                        c.Span = px;
                }
            }
            if (c.Span >= SubjectMinPx && thinnest <= longest * PlateRatio)
                marks |= Mark.Plate;
        }
        catch (Exception)
        {
            // a renderer without usable bounds still gets its layer marks and its material
        }

        GateSlots.Clear();
        try { r.GetSharedMaterials(GateSlots); }
        catch (Exception) { GateSlots.Clear(); }

        int bestRank = -1;
        for (int i = 0; i < GateSlots.Count; i++)
        {
            Material? mat = GateSlots[i];
            if (mat == null)
                continue;
            Shader? sh;
            try { sh = mat.shader; }
            catch (Exception) { sh = null; }
            if (sh == null)
                continue;

            int shaderId = sh.GetInstanceID();
            if (!ShaderFactsCache.TryGetValue(shaderId, out ShaderFacts facts))
            {
                facts = new ShaderFacts(IsGlowShader(sh), IsUnlitShader(sh));
                ShaderFactsCache[shaderId] = facts;
            }

            int queue;
            try { queue = mat.renderQueue; }
            catch (Exception) { queue = -1; }

            if (facts.Glow)
                marks |= Mark.Shader;
            if (queue >= 2900)
                marks |= Mark.Queue;
            if (facts.Unlit)
                marks |= Mark.Unlit;

            int rank = (facts.Unlit ? 4 : 0) + (queue >= 2450 ? 2 : 0) + (facts.Glow ? 1 : 0);
            if (rank <= bestRank && c.Mat != null)
                continue;
            bestRank = rank;
            c.Mat = mat;
            c.Sh = sh;
            c.Queue = queue;
            c.Unlit = facts.Unlit;
        }

        c.Emissive = IsEmissive(c.Mat);
        c.Marks = marks;
        return c;
    }
}
