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
/// <para>MODBUILD 259 — WHAT THE FIRST TWO RUNS SETTLED, AND THE ONE THING THAT BROKE. Tier A did
/// its job: 103 records, every one printed, no cap bound, so "the rectangles are not gate children"
/// is now a measured negative rather than an absence. Tier B did not, and the reason was arithmetic
/// rather than budget — its distance key SATURATED. Every one of the 2657 matched renderers reported
/// <c>d=0.00wu</c>, because a gate anchor is the union AABB of a whole door-prop subtree and a five
/// metre radius around it swallows the room; "nearest first" therefore degenerated into hierarchy
/// order, and 40 of the 48 records it spent were <c>not-submitted</c> floor tiles that draw nothing.
/// Three changes, all in this file: the sort is now submitted-first then largest-first with the
/// centre distance as the tie-break the surface distance cannot be; the footer describes the WHOLE
/// matched population instead of the printed slice, and names how many SUBMITTED records were cut,
/// which is the only count that can still hide the subject; and every record carries a
/// <c>skinned:</c> clause, because the gate's plates are all <see cref="SkinnedMeshRenderer"/> and
/// every AABB and screen rect in this dump is a bind-pose number, not a measurement of the drawn
/// geometry.</para>
///
/// <para>MODBUILD 261 — THE QUESTION CHANGED, SO THE INSTRUMENT DID. Everything above asks WHAT
/// THE RECTANGLES ARE, and six instrumented builds have now asked it. The user reframed it on
/// 2026-08-24: "es kommt mir so vor als müsste eigentlich irgendwas darüber liegen, wird eventuell
/// ein Stück des Torbogens/Türmauer hier nicht richtig dargestellt?" — the pale rectangles may be an
/// inner surface a piece of the archway normally covers, and that piece may be missing. No field in
/// tiers A or B could answer that, because every record describes ONE renderer and the question is a
/// relation between two. Two passes were added, both after tier B so they can use the whole matched
/// population, and both answerable with <c>grep 'GATE COVER VERDICT'</c>:</para>
/// <list type="bullet">
/// <item><b>GATE COVER</b> — for every SUBMITTED tier-A renderer, how many neighbours overlap its
///   AABB (pose-independent) and how many the eye-to-centre segment passes through, each split into
///   DRAWING and SILENT. A silent occluder is geometry that would cover the subject and does not.</item>
/// <item><b>GATE FAMILY</b> — instances of the same prefab container compared against each other,
///   so a renderer that is not merely silent but ABSENT from one door prop is a printed count. This
///   is the only test in the dump that can see a piece tier A cannot print because it was never
///   created.</item>
/// </list>
/// <para>The ruling this must never break: doorway segments never fade (user 2026-08-02, reaffirmed
/// 2026-08-24, "was ich auch besser finde"). Nothing here fades anything.</para>
///
/// <para>MP-SAFE: reads only. Nothing here writes a material, a property block, a transform or a
/// wire message. No networked state is added or read; both new passes are local diagnostics over
/// local scene geometry.</para>
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

    /// <summary>Tier B records printed at all.
    ///
    /// <para>MODBUILD 259 — WHY THIS WENT UP AND NOT DOWN. Tier A answered: 103 records, all
    /// printed, no cap bound, and nothing in it can be the photographed rectangles. That makes
    /// tier B the only remaining population, and the ModBuild 258 footer says it printed 48 of
    /// 2657. The 48 it chose were worthless, and the reason is arithmetic rather than taste:
    /// EVERY ONE of them printed <c>d=0.00wu</c>. The gate anchor is the union AABB of a whole
    /// door-prop subtree — floor hexes included — so a five-metre radius around it puts most of
    /// the room INSIDE the box, <see cref="DistanceToNearestGate"/> returns 0 for all of them,
    /// and "nearest first" degenerates into hierarchy order. Forty of the forty-eight were
    /// <c>not-submitted</c>: the dump spent its entire budget on floor tiles that draw nothing.
    /// The cap was never the binding constraint; the ORDER was.</para></summary>
    private const int GateMaxNearRecords = 64;

    /// <summary>Ceiling on SUBMITTED tier-B records, ModBuild 261. Submitted records are no longer
    /// subject to <see cref="GateMaxNearRecords"/> at all.
    ///
    /// <para>WHY THIS EXISTS. The ModBuild 260 footer printed its own defeat: "2593 NEAR
    /// RENDERER(S) WERE NOT PRINTED … Of those cut, 726 were SUBMITTED: that is the only count that
    /// can still hide the subject, because a rectangle in a photograph is being drawn. If it reads
    /// 0 the truncation is harmless". It read 726, and the 64 records that WERE printed are all
    /// forest walls, tree pillars, root plates and crypt courses — every one of them architecture,
    /// none of them a small pale plate. Repairing the ORDER in 259 was necessary and did not
    /// suffice: an order can only decide which 64 of 790 drawn renderers get named, and the subject
    /// was not in the 64 either time. So the cap on drawn records is removed and the ceiling here
    /// is a safety stop against a pathological scene, not a budget: the number is printed when it
    /// binds, and <c>submittedCut</c> in the footer is now structurally 0 in any normal room.</para>
    ///
    /// <para>WHAT IT COSTS, said plainly because it is not free. The ModBuild 260 dump printed 151
    /// records for 23.9 ms of one frame; this prints roughly 790 in that scene, so the one-shot
    /// hitch grows to a low hundreds of milliseconds. It is once per scene, in an instrumented
    /// build, and the footer prints the measured number rather than this comment's estimate. A
    /// single stutter as a room opens is a cheaper price than a seventh build that cannot see the
    /// subject.</para></summary>
    private const int GateMaxNearSubmitted = 900;

    /// <summary>Tier B records that get the full dump, in the tier-B order. This is an ordering cap,
    /// not a property filter: no renderer is excluded for what it IS, only for being ordered behind
    /// twenty others.
    ///
    /// <para>WHERE THE COST OF THIS DUMP ACTUALLY IS, since it is not where it looks. The walk is
    /// cheap — the census's own standalone sweep priced 3040 renderers at 7.4 ms on this hardware
    /// (ModBuild 255 log) while doing strictly more per renderer than this walk does. The expensive
    /// part is the OUTPUT: a full dump reads up to <see cref="MaxProps"/> = 44 shader properties
    /// through the material marshal and then writes a multi-kilobyte line to BepInEx's log. That is
    /// why the full-dump caps are two figures and not three. The dump is a deliberate ONE-FRAME
    /// HITCH, once per scene, in an instrumented build; the footer prints the millisecond number so
    /// it is a measurement and not this comment's opinion.</para></summary>
    private const int GateMaxNearFullDumps = 20;

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

    // ---- ModBuild 261: the COVER pass and the FAMILY pass -------------------------------------
    //
    // Both exist to answer ONE question the user asked on 2026-08-24, which is not the question the
    // first six instrumented builds were asking: "es kommt mir so vor als müsste eigentlich irgendwas
    // darüber liegen, wird eventuell ein Stück des Torbogens/Türmauer hier nicht richtig
    // dargestellt?" Six builds asked WHAT THE RECTANGLES ARE. He is asking WHAT IS MISSING IN FRONT
    // OF THEM, and no field in this dump could answer that: every record describes one renderer in
    // isolation, and "is anything covering it" is a relation between two.

    private static readonly List<CoverSubject> GateCoverSubjects = new(32);
    private static readonly List<CoverCandidate> GateCoverPool = new(4096);
    private static readonly List<Material> GateCoverSlots = new(8);
    private static readonly List<FamilyMember> GateFamily = new(128);

    /// <summary>One SUBMITTED tier-A renderer, kept for the cover pass. Submitted is the right
    /// filter here and nowhere else in this file: a renderer that is not being drawn cannot be a
    /// pale rectangle in a photograph, so asking what covers it is asking about nothing. Small by
    /// construction: 29 of the 103 tier-A records in the ModBuild 260 log were submitted, and 17 of
    /// those were the two on-screen door props.</summary>
    private readonly struct CoverSubject
    {
        public CoverSubject(Renderer r, Bounds b, int door, int idx)
        {
            R = r;
            B = b;
            Door = door;
            Idx = idx;
        }

        public readonly Renderer R;
        public readonly Bounds B;
        public readonly int Door;
        public readonly int Idx;
    }

    /// <summary>A renderer the cover pass may test as an occluder, with its bounds and its ALIVE
    /// flag read once instead of once per subject.
    ///
    /// <para>ALIVE IS DELIBERATELY NOT <c>Submitted</c>. <c>Renderer.isVisible</c> is false for
    /// anything outside the frustum, and a piece of masonry behind the camera is not a missing
    /// piece of masonry. Alive means <c>enabled</c>, <c>activeInHierarchy</c> AND offering at least
    /// one non-null material — the three ways this codebase has actually seen geometry go silent
    /// (the fade's <c>renderer.enabled = false</c> delivery, a deactivated subtree, and the
    /// zero-material state <see cref="MaterialLoaderHeal"/> exists for). A renderer that is not
    /// alive is one that WOULD have drawn and does not.</para></summary>
    private readonly struct CoverCandidate
    {
        public CoverCandidate(Renderer r, Bounds b, bool alive)
        {
            R = r;
            B = b;
            Alive = alive;
        }

        public readonly Renderer R;
        public readonly Bounds B;
        public readonly bool Alive;
    }

    /// <summary>One tier-A renderer, filed under the name of its immediate parent — which for every
    /// door prop in the ModBuild 260 log is the prefab container
    /// ('CR_DoorFrame_01_Narrow_Thick', 'CR_ST_Door_01_Thick', 'CR_OS_DoorFrame_01_Narrow_Thick',
    /// 'CR_ST_Door_01'). The family pass compares instances of the SAME container against each
    /// other, so "a piece the prefab should have is absent" becomes a count rather than an
    /// impression.</summary>
    private readonly struct FamilyMember
    {
        public FamilyMember(string family, int instance, string rendererName)
        {
            Family = family;
            Instance = instance;
            RendererName = rendererName;
        }

        public readonly string Family;
        public readonly int Instance;
        public readonly string RendererName;
    }

    private readonly struct NearRecord
    {
        public NearRecord(Renderer r, float dist, float centreDist, bool submitted, float span)
        {
            R = r;
            Dist = dist;
            CentreDist = centreDist;
            Submitted = submitted;
            Span = span;
        }

        public readonly Renderer R;

        /// <summary>Surface distance to the nearest gate anchor box, 0 INSIDE it — which in this
        /// scene is nearly everything, see <see cref="GateMaxNearRecords"/>.</summary>
        public readonly float Dist;

        /// <summary>Centre-to-centre distance to the same anchor. Never saturates, so it is the
        /// tie-break the surface distance cannot be.</summary>
        public readonly float CentreDist;

        /// <summary><c>enabled &amp;&amp; activeInHierarchy &amp;&amp; isVisible</c>.</summary>
        public readonly bool Submitted;

        /// <summary>Angular size in pixels at the dump-time head pose.</summary>
        public readonly float Span;
    }

    /// <summary>
    /// TIER B ORDER, ModBuild 259. Submitted first, then biggest first, then nearest by centre.
    ///
    /// <para>THIS IS NOT THE RANKING THE HEADER OF THIS FILE CONDEMNS, and the difference is the
    /// whole point. The five builds of <c>[Perf] GLOW CARDS</c> ranked by guesses about what the
    /// subject IS — its shape, its queue, whether it lights itself. Both keys here are properties
    /// of the PHOTOGRAPH instead: a thing visible in a screenshot is being submitted, and a thing
    /// that measures 76 to 121 px tall in a 3840x2160 frame is not small. Nothing is excluded for
    /// what it is; the whole matched population is still counted in the footer, and the number cut
    /// is still printed.</para>
    ///
    /// <para>WHAT IT REPLACES: a pure surface-distance sort in which all 2657 records tied at 0.00
    /// and the printed 48 were therefore hierarchy order, 40 of them not-submitted.</para>
    /// </summary>
    private static readonly Comparison<NearRecord> ByPhotographability =
        (a, b) =>
        {
            if (a.Submitted != b.Submitted)
                return a.Submitted ? -1 : 1;
            int bySpan = b.Span.CompareTo(a.Span);
            if (bySpan != 0)
                return bySpan;
            int byDist = a.Dist.CompareTo(b.Dist);
            return byDist != 0 ? byDist : a.CentreDist.CompareTo(b.CentreDist);
        };

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
        GateCoverSubjects.Clear();
        GateCoverPool.Clear();
        GateFamily.Clear();
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

        // ModBuild 259: the tier-B breakdown is taken over the WHOLE matched population, not over
        // the printed slice. The 258 footer characterised 48 records and left 2609 undescribed,
        // which is the one number a truncated dump most needs to give back.
        int nearSubmitted = 0;
        int nearPlate = 0;
        int nearSubmittedPlate = 0;
        int nearSelfLit = 0;

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
                RecordGateFamily(r);
                GateCoverPool.Add(new CoverCandidate(r, c.B, IsAliveRenderer(r)));
                if (c.Submitted && c.B.size != Vector3.zero)
                    GateCoverSubjects.Add(new CoverSubject(r, c.B, d, i));
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
                AppendSkinned(sb, r);
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

                float dist = DistanceToNearestGate(r, out float centreDist);
                if (dist > GateNearRadiusWU)
                    continue;
                if (!GatePrinted.Add(r.GetInstanceID()))
                    continue;
                // Built ONCE here for every matched renderer, not just the printed ones: the
                // ordering keys and the footer's population breakdown both need it, and the
                // expensive part of a record is AppendProps, which still only runs on the
                // printed few.
                Candidate nc = BuildGateCandidate(r);
                if (nc.Marks == Mark.None)
                    unmarkedB++;
                if (nc.Submitted)
                    nearSubmitted++;
                if ((nc.Marks & Mark.Plate) != 0)
                {
                    nearPlate++;
                    if (nc.Submitted)
                        nearSubmittedPlate++;
                }
                if (nc.SelfLit)
                    nearSelfLit++;
                GateNear.Add(new NearRecord(r, dist, centreDist, nc.Submitted, nc.Span));
                // The cover pool is the WHOLE matched population, not the printed slice. The record
                // cap on tier B is an ordering cap for human reading; an occluder that is missing
                // does not care where it sorted.
                GateCoverPool.Add(new CoverCandidate(r, nc.B, IsAliveRenderer(r)));
            }
            if (walkCapped)
                break;
        }

        GateNear.Sort(ByPhotographability);

        // MODBUILD 261: submitted records are printed in full, not sampled. The sort already puts
        // every submitted record first, so taking the first max(64, submitted) entries prints all
        // of them plus the old 64-record window when the scene has fewer than 64 drawn neighbours.
        int nearPrinted = Mathf.Min(
            GateNear.Count,
            Mathf.Max(GateMaxNearRecords, Mathf.Min(nearSubmitted, GateMaxNearSubmitted)));
        int submittedCut = 0;
        for (int i = nearPrinted; i < GateNear.Count; i++)
        {
            if (GateNear[i].Submitted)
                submittedCut++;
        }
        for (int i = 0; i < nearPrinted; i++)
        {
            Renderer r = GateNear[i].R;
            if (r == null)
                continue;
            Candidate c = BuildGateCandidate(r);
            sb.Length = 0;
            sb.Append("GATE DUMP B[").Append(i).Append("] d=")
              .Append(GateNear[i].Dist.ToString("F2")).Append("wu centre=")
              .Append(GateNear[i].CentreDist.ToString("F2")).Append("wu '")
              .Append(PathOf(r.transform)).Append("' ");
            AppendIdentity(sb, c, r);
            AppendScreen(sb, c);
            AppendSkinned(sb, r);
            AppendBlend(sb, c);
            AppendTint(sb, c, r);
            if (i < GateMaxNearFullDumps)
            {
                AppendProps(sb, c);
                AppendPasses(sb, c);
            }
            else
            {
                sb.Append(" | (full property/pass dump withheld — the first ")
                  .Append(GateMaxNearFullDumps).Append(" tier-B records got one; this is an "
                                                       + "ORDERING cap, submitted and largest "
                                                       + "first, not a filter on what the renderer "
                                                       + "is)");
            }
            VRLog.Info(Scope, sb.ToString());
        }

        // ---- ModBuild 261: the two relational passes -------------------------------------------
        int familyShort = ReportGateFamilies(sb);
        ReportGateCover(sb, familyShort);

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
              .Append(" NEAR RENDERER(S) WERE NOT PRINTED — THIS DUMP IS TRUNCATED. Of those cut, ")
              .Append(submittedCut)
              .Append(" were SUBMITTED: that is the only count that can still hide the subject, "
                      + "because a rectangle in a photograph is being drawn. ModBuild 261 removed "
                      + "the cap on SUBMITTED records entirely — they are now printed to a ceiling "
                      + "of ").Append(GateMaxNearSubmitted)
              .Append(", so this number reads 0 unless that ceiling bound, and the records that "
                      + "were cut are ones that draw nothing. In ModBuild 260 it read 726 against "
                      + "a 64-record cap, and the 64 that were printed were all architecture");
        }
        else
        {
            sb.Append(" ⇒ nothing was cut: every renderer within the radius is above");
        }

        // ---- the whole matched population, described rather than sampled ------------------------
        sb.Append(" | TIER B POPULATION, ALL ").Append(GateNear.Count)
          .Append(" MATCHED RECORDS AND NOT JUST THE PRINTED ONES: ").Append(nearSubmitted)
          .Append(" submitted, ").Append(nearPlate).Append(" plate-shaped, ")
          .Append(nearSubmittedPlate).Append(" both submitted AND plate-shaped, ")
          .Append(nearSelfLit).Append(" self-lit. ORDER, changed in ModBuild 259: submitted first, "
                  + "then largest angular size, then nearest. The previous order was surface "
                  + "distance alone and it SATURATED — all 2657 records in the 258 log read "
                  + "d=0.00wu because the gate anchor is the union AABB of a whole door-prop "
                  + "subtree, so the 48 printed were hierarchy order and 40 of them drew nothing. "
                  + "Both new keys are properties of the photograph rather than guesses about the "
                  + "subject, and nothing is excluded: the counts on this line cover the lot");
        if (walkCapped)
        {
            sb.Append(" | WALK CAPPED at ").Append(GateMaxWalk)
              .Append(" renderers — the hierarchy walk stopped early and tier B is INCOMPLETE");
        }

        // ---- why the census never had these, in two numbers -------------------------------------
        sb.Append(" | WHY [Perf] GLOW CARDS NEVER HAD THESE — ").Append(unmarkedA).Append(" of ")
          .Append(tierAPrinted).Append(" tier-A and ").Append(unmarkedB).Append(" of ")
          .Append(GateNear.Count).Append(" MATCHED tier-B record(s) carry marks[None]. A marks[None] "
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
                  + " | WHAT TIER A ALREADY ANSWERED, AND THE IDENTIFICATION IT COST. ModBuild 258 "
                  + "printed all 103 tier-A records with no cap binding, and read the answer as "
                  + "'Door_Light_Front_Mesh' / 'Door_Light_Back_Mesh' — flat plates carrying the door "
                  + "body's opaque PBR material on queue 2000. ModBuild 259 FALSIFIES that from the "
                  + "same log's own numbers. The plate's bounds are 2.069 x 0.257 x 0.008 wu on an "
                  + "unrotated door and 1.041 x 0.257 x 1.796 on one turned about 60 degrees, which "
                  + "is one 8:1 STRIP measured twice; its world height never exceeds 0.258 wu, about "
                  + "12 px at the distance where its own reported span is 85 px. The photographed "
                  + "regions are 76 to 121 px TALL and taller than they are wide. A strip eight times "
                  + "wider than it is high cannot project as a square, so the plates are not the "
                  + "subject — the matching diagonals that made the case were a coincidence of two "
                  + "different shapes, and the 'screen:' rects were never comparable to the photo "
                  + "anyway because this dump latches the head pose when the ROOM OPENS. The one "
                  + "escape is a skinned pose that leaves the bind pose, which the new 'skinned:' "
                  + "clause on every record now settles."
                  + " | HOW TO READ THIS. Tier A is complete and can be read as a decisive negative: "
                  + "the rectangles are not gate children. That puts the subject in tier B, where 258 "
                  + "printed 48 of 2657 in a saturated order — so read the TIER B POPULATION line "
                  + "first, then the records, which are now submitted-and-largest first. The one "
                  + "reading that means NOTHING is a footer with tier-A walked = 0, or a 'GATE DUMP "
                  + "GAVE UP' line: that is an empty gate, not an absent subject."
                  + " | MODBUILD 261 ADDS A DIFFERENT QUESTION, AND IT IS ONE GREP. Six builds asked "
                  + "what the rectangles ARE. The user asked on 2026-08-24 what is MISSING IN FRONT "
                  + "of them. Read 'GATE COVER VERDICT' first — it carries three numbers and says in "
                  + "words which way they read: door-prop renderers with a SILENT renderer "
                  + "overlapping their own bounds, door-prop renderers with a SILENT renderer "
                  + "between the eye and them, and renderers ABSENT from a door prop that a sibling "
                  + "instance of the same prefab container has. All three zero falsifies the "
                  + "hypothesis; any of them non-zero names the missing piece. The per-record detail "
                  + "is on the 'GATE COVER A' and 'GATE FAMILY' lines above the footer.");
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
    /// course that reaches the gate is near it even though its centre is not.
    ///
    /// <para><paramref name="centreDist"/> is the centre-to-centre distance to the SAME anchor, and
    /// it exists because the surface distance SATURATES: the anchor is the union AABB of a whole
    /// door-prop subtree, so in the ModBuild 258 log all 2657 matched renderers returned 0.00 and
    /// the sort had nothing to order by. It is a tie-break, never a filter.</para></summary>
    private static float DistanceToNearestGate(Renderer r, out float centreDist)
    {
        centreDist = float.MaxValue;
        Vector3 p;
        try { p = r.bounds.center; }
        catch (Exception) { return float.MaxValue; }

        float best = float.MaxValue;
        float bestCentre = float.MaxValue;
        for (int i = 0; i < GateAnchors.Count; i++)
        {
            float sq = GateAnchors[i].SqrDistance(p);
            if (sq < best)
                best = sq;
            float cs = (GateAnchors[i].center - p).sqrMagnitude;
            if (cs < bestCentre)
                bestCentre = cs;
        }
        if (bestCentre < float.MaxValue)
            centreDist = Mathf.Sqrt(Mathf.Max(bestCentre, 0f));
        return best >= float.MaxValue ? float.MaxValue : Mathf.Sqrt(Mathf.Max(best, 0f));
    }

    // ==========================================================================================
    //  ModBuild 261 — WHAT IS MISSING IN FRONT OF THEM
    // ==========================================================================================
    //
    // THE QUESTION THIS ANSWERS, AND WHY IT IS A NEW ONE. User, 2026-08-24: "es kommt mir so vor
    // als müsste eigentlich irgendwas darüber liegen, wird eventuell ein Stück des
    // Torbogens/Türmauer hier nicht richtig dargestellt? Ich erinnere mich an diese Lichter in
    // einer frühen Phase als wir noch versucht hatten auch den Torbogen wegzufaden." Every field
    // in this dump before ModBuild 261 describes ONE renderer in isolation. "Is something that
    // should lie over it not being drawn" is a relation between TWO renderers, so no amount of
    // per-record detail could answer it and six builds of ranking never could have.
    //
    // FALSIFIER, so the next round does not have to re-derive it: if a future log shows a SILENT
    // occluder against a door-prop renderer, this pass names it and the hypothesis is alive. If it
    // shows 0 silent occluders and 0 family shortfalls — which is what the ModBuild 260 tier-A
    // records already imply, all 103 walked and all 103 printed with every on-screen door renderer
    // enabled+visible+submitted — the hypothesis is dead by measurement and the subject is not a
    // hole in the archway.

    /// <summary>File one tier-A renderer under its immediate parent's name, for the family pass.
    /// The parent is the prefab container in every door prop in the ModBuild 260 log; a renderer
    /// parented directly to the prop root files under the prop's own name, which is still a
    /// comparison across the instances of that prop.</summary>
    private static void RecordGateFamily(Renderer r)
    {
        try
        {
            Transform t = r.transform;
            Transform? p = t.parent;
            string family = p != null ? p.name : "<root>";
            int instance = p != null ? p.GetInstanceID() : t.GetInstanceID();
            GateFamily.Add(new FamilyMember(family, instance, t.name));
        }
        catch (Exception)
        {
            // a renderer whose transform is unreadable simply does not take part in the comparison
        }
    }

    /// <summary>
    /// THE FAMILY PASS — "is a piece the prefab should have absent?", turned into a count.
    ///
    /// <para>Groups every tier-A renderer by container NAME, then by container INSTANCE, and
    /// compares the instances of one name against each other. For every renderer name, the
    /// richest instance sets the expectation; any instance carrying fewer copies of that name is
    /// reported as SHORT by that many. This is the only test in the dump that can see a piece that
    /// is not merely silent but ABSENT — a renderer that was never created has no record to be
    /// enabled or disabled, and tier A can only print what exists.</para>
    ///
    /// <para>WHAT IT CANNOT SEE, stated so the negative is not over-read: three genuinely
    /// different frame prefabs sit in this scene ('CR_DoorFrame_01_Narrow_Thick' with one renderer
    /// per half, 'CR_ST_Door_01_Thick' with three, 'CR_OS_DoorFrame_01_Narrow_Thick' with four),
    /// and a leaner prefab is not a damaged one. The comparison is therefore WITHIN a container
    /// name and never across names.</para>
    /// </summary>
    /// <returns>Total shortfall across all families — 0 means every instance of every container is
    /// as complete as the richest instance of that container.</returns>
    private static int ReportGateFamilies(StringBuilder sb)
    {
        var families = new Dictionary<string, Dictionary<int, Dictionary<string, int>>>(16);
        for (int i = 0; i < GateFamily.Count; i++)
        {
            FamilyMember m = GateFamily[i];
            if (!families.TryGetValue(m.Family, out Dictionary<int, Dictionary<string, int>>? byInst))
            {
                byInst = new Dictionary<int, Dictionary<string, int>>(4);
                families[m.Family] = byInst;
            }
            if (!byInst.TryGetValue(m.Instance, out Dictionary<string, int>? counts))
            {
                counts = new Dictionary<string, int>(8);
                byInst[m.Instance] = counts;
            }
            counts.TryGetValue(m.RendererName, out int n);
            counts[m.RendererName] = n + 1;
        }

        int totalShort = 0;
        foreach (KeyValuePair<string, Dictionary<int, Dictionary<string, int>>> fam in families)
        {
            Dictionary<int, Dictionary<string, int>> byInst = fam.Value;

            // The expectation: the highest count of each renderer name seen on ANY instance.
            var expect = new Dictionary<string, int>(8);
            foreach (KeyValuePair<int, Dictionary<string, int>> inst in byInst)
            {
                foreach (KeyValuePair<string, int> kv in inst.Value)
                {
                    expect.TryGetValue(kv.Key, out int have);
                    if (kv.Value > have)
                        expect[kv.Key] = kv.Value;
                }
            }

            sb.Length = 0;
            sb.Append("GATE FAMILY '").Append(fam.Key).Append("': ").Append(byInst.Count)
              .Append(" instance(s) in this scene, expecting ").Append(expect.Count)
              .Append(" distinct renderer name(s) per instance");

            int famShort = 0;
            foreach (KeyValuePair<int, Dictionary<string, int>> inst in byInst)
            {
                foreach (KeyValuePair<string, int> want in expect)
                {
                    inst.Value.TryGetValue(want.Key, out int have);
                    if (have >= want.Value)
                        continue;
                    famShort += want.Value - have;
                    sb.Append(" | SHORT: instance ").Append(inst.Key).Append(" has ").Append(have)
                      .Append(" x '").Append(want.Key).Append("' where a sibling instance of the "
                              + "same container has ").Append(want.Value)
                      .Append(" — that many copies of this renderer are ABSENT from the scene, not "
                              + "merely silent");
                }
            }
            totalShort += famShort;
            if (famShort == 0)
            {
                sb.Append(" | COMPLETE: every instance carries every renderer name its richest "
                          + "sibling carries, in the same count. Nothing belonging to this "
                          + "container is missing from the scene");
            }
            VRLog.Info(Scope, sb.ToString());
        }

        if (families.Count == 0)
        {
            VRLog.Info(Scope, "GATE FAMILY — no tier-A renderer offered a readable parent, so the "
                              + "completeness comparison did not run. That is an instrument "
                              + "failure, not a complete gate.");
        }
        return totalShort;
    }

    /// <summary>
    /// THE COVER PASS — "is something that should lie over it not being drawn?"
    ///
    /// <para>For every SUBMITTED tier-A renderer it asks two independent questions against the
    /// whole tier-A ∪ tier-B population:</para>
    /// <list type="number">
    /// <item><b>IN FRONT</b> — whose AABB does the segment from the head to this renderer's centre
    ///   pass through, before it arrives? That is occlusion in the ordinary sense, and it is the
    ///   literal form of "etwas müsste darüber liegen".</item>
    /// <item><b>EMBEDDING</b> — whose AABB overlaps this renderer's own AABB? This one is
    ///   POSE-INDEPENDENT and it is the more important of the two, because a plate that is
    ///   normally buried inside a wall course is one whose bounds sit INSIDE that course whatever
    ///   the camera does. A dump latches the head pose when the room opens; the photograph was
    ///   taken somewhere else entirely, so an answer that depends on the pose is worth less than
    ///   one that does not.</item>
    /// </list>
    ///
    /// <para>Each question is answered with two numbers, drawing and SILENT, and the silent count
    /// is the whole point. An occluder that is alive is doing its job. An occluder that is
    /// <c>enabled = false</c>, deactivated, or offering no material is a piece of geometry that
    /// WOULD cover the subject and does not — which is exactly the report.</para>
    /// </summary>
    private static void ReportGateCover(StringBuilder sb, int familyShort)
    {
        int subjectsWithSilentFront = 0;
        int subjectsWithSilentEmbed = 0;
        bool haveEye = _projectionKnown && _head != null;
        Vector3 eye = _headPos;

        for (int s = 0; s < GateCoverSubjects.Count; s++)
        {
            CoverSubject subj = GateCoverSubjects[s];
            if (subj.R == null)
                continue;

            int frontAlive = 0;
            int frontSilent = 0;
            int embedAlive = 0;
            int embedSilent = 0;
            float nearestSilentT = float.MaxValue;
            string nearestSilentFront = string.Empty;
            string nearestSilentEmbed = string.Empty;
            float nearestSilentEmbedD = float.MaxValue;
            int subjId = subj.R.GetInstanceID();

            for (int i = 0; i < GateCoverPool.Count; i++)
            {
                CoverCandidate cc = GateCoverPool[i];
                if (cc.R == null || cc.R.GetInstanceID() == subjId)
                    continue;
                if (cc.B.size == Vector3.zero)
                    continue;

                if (cc.B.Intersects(subj.B))
                {
                    if (cc.Alive)
                    {
                        embedAlive++;
                    }
                    else
                    {
                        embedSilent++;
                        float d = (cc.B.center - subj.B.center).magnitude;
                        if (d < nearestSilentEmbedD)
                        {
                            nearestSilentEmbedD = d;
                            nearestSilentEmbed = SafeName(cc.R);
                        }
                    }
                }

                if (!haveEye)
                    continue;
                if (!SegmentHitsBox(eye, subj.B.center, cc.B, out float t))
                    continue;
                if (cc.Alive)
                {
                    frontAlive++;
                }
                else
                {
                    frontSilent++;
                    if (t < nearestSilentT)
                    {
                        nearestSilentT = t;
                        nearestSilentFront = SafeName(cc.R);
                    }
                }
            }

            if (frontSilent > 0)
                subjectsWithSilentFront++;
            if (embedSilent > 0)
                subjectsWithSilentEmbed++;

            sb.Length = 0;
            sb.Append("GATE COVER A[").Append(subj.Door).Append('.').Append(subj.Idx)
              .Append("] '").Append(SafeName(subj.R)).Append("' AABB c(")
              .Append(Fmt3(subj.B.center)).Append(") s(").Append(Fmt3(subj.B.size))
              .Append(") — EMBEDDING, pose-independent: ").Append(embedAlive)
              .Append(" drawing, ").Append(embedSilent).Append(" SILENT");
            if (embedSilent > 0)
            {
                sb.Append(" (nearest silent '").Append(nearestSilentEmbed).Append("' at ")
                  .Append(nearestSilentEmbedD.ToString("F2")).Append(" wu)");
            }
            sb.Append(" | IN FRONT, eye to centre: ");
            if (!haveEye)
            {
                sb.Append("NOT TESTED — no head camera at dump time, which is an instrument gap and "
                          + "not a finding");
            }
            else
            {
                sb.Append(frontAlive).Append(" drawing, ").Append(frontSilent).Append(" SILENT");
                if (frontSilent > 0)
                {
                    sb.Append(" (nearest silent '").Append(nearestSilentFront).Append("' at t=")
                      .Append(nearestSilentT.ToString("F2")).Append(" of the way from the eye)");
                }
            }
            VRLog.Info(Scope, sb.ToString());
        }

        sb.Length = 0;
        sb.Append("GATE COVER VERDICT — ").Append(GateCoverSubjects.Count)
          .Append(" SUBMITTED door-prop renderer(s) tested against ").Append(GateCoverPool.Count)
          .Append(" neighbour(s): ").Append(subjectsWithSilentEmbed)
          .Append(" have a SILENT renderer overlapping their own AABB, ")
          .Append(haveEye ? subjectsWithSilentFront.ToString() : "n/a")
          .Append(" have a SILENT renderer between the eye and their centre, and the family pass "
                  + "found ").Append(familyShort)
          .Append(" renderer(s) ABSENT from a door prop that a sibling instance of the same "
                  + "container has.");
        if (GateCoverSubjects.Count == 0)
        {
            sb.Append(" NO SUBJECTS: not one door-prop renderer was being submitted at dump time, so "
                      + "all three numbers are vacuous. That is an instrument that never got a "
                      + "drawn gate to look at, NOT a falsification.");
        }
        else if (!haveEye)
        {
            sb.Append(" THE EYE HALF DID NOT RUN — no head camera at dump time. The EMBEDDING count "
                      + "is pose-independent and still stands, but this line cannot falsify the "
                      + "hypothesis on its own until a dump lands with a head camera.");
        }
        else if (subjectsWithSilentEmbed == 0 && subjectsWithSilentFront == 0 && familyShort == 0)
        {
            sb.Append(" ALL THREE ZERO ⇒ the 2026-08-24 hypothesis is FALSIFIED for this scene: "
                      + "nothing belonging to the archway or the door wall is missing, disabled, "
                      + "deactivated or material-less, and no piece of geometry that would cover a "
                      + "drawn door-prop renderer is failing to draw. The pale rectangles are "
                      + "therefore not an inner surface exposed by a hole — look for a renderer "
                      + "that is drawn WHERE IT SHOULD NOT BE, not for one that is missing.");
        }
        else
        {
            sb.Append(" A NON-ZERO COUNT IS THE LEAD: the named renderer above is geometry that "
                      + "would cover a drawn door-prop renderer and does not. Restore it before "
                      + "looking anywhere else — and note that the doorway itself must not be made "
                      + "to fade to achieve it (user ruling 2026-08-02, reaffirmed 2026-08-24: "
                      + "'was ich auch besser finde').");
        }
        VRLog.Info(Scope, sb.ToString());
    }

    /// <summary>Is this renderer geometry that WOULD draw? See <see cref="CoverCandidate"/> on why
    /// this is not <c>Submitted</c>.</summary>
    private static bool IsAliveRenderer(Renderer r)
    {
        try
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy)
                return false;
            GateCoverSlots.Clear();
            r.GetSharedMaterials(GateCoverSlots);
            for (int i = 0; i < GateCoverSlots.Count; i++)
            {
                if (GateCoverSlots[i] != null)
                    return true;
            }
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Slab test for the segment <paramref name="a"/> to <paramref name="b"/> against an
    /// AABB. <paramref name="t"/> is where along the segment the box is first entered, 0 at the eye
    /// and 1 at the subject, so a hit with t in [0,1] is a box the view ray passes through BEFORE it
    /// arrives. A box containing the eye returns t = 0.</summary>
    private static bool SegmentHitsBox(Vector3 a, Vector3 b, Bounds box, out float t)
    {
        t = 0f;
        Vector3 d = b - a;
        Vector3 lo = box.min;
        Vector3 hi = box.max;
        float tMin = 0f;
        float tMax = 1f;

        for (int axis = 0; axis < 3; axis++)
        {
            float da = d[axis];
            float oa = a[axis];
            if (Mathf.Abs(da) < 1e-6f)
            {
                if (oa < lo[axis] || oa > hi[axis])
                    return false;
                continue;
            }
            float inv = 1f / da;
            float t1 = (lo[axis] - oa) * inv;
            float t2 = (hi[axis] - oa) * inv;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }
            if (t1 > tMin)
                tMin = t1;
            if (t2 < tMax)
                tMax = t2;
            if (tMin > tMax)
                return false;
        }

        t = tMin;
        return true;
    }

    /// <summary>
    /// The skinned clause, ModBuild 259. A <see cref="SkinnedMeshRenderer"/>'s <c>bounds</c> are its
    /// BIND-POSE <c>localBounds</c> pushed through the root bone; the geometry the GPU actually
    /// draws is wherever the bones put it. Every AABB and every screen rect in this dump comes from
    /// <c>bounds</c>, so for a skinned record those two fields are an assumption and not a
    /// measurement — and the gate's plates are all skinned.
    ///
    /// <para>WHY IT IS HERE. ModBuild 258 identified the photographed rectangles as
    /// <c>Door_Light_*_Mesh</c> and ModBuild 259 falsified that from these same numbers: the plate's
    /// bounds are 2.069 x 0.257 x 0.008 wu on an unrotated door and 1.041 x 0.257 x 1.796 on a door
    /// turned about 60 degrees — the same 8:1 strip twice — while the photographed regions are 30x76,
    /// 92x104 and 53x121 px, TALLER than wide. An 8:1 strip cannot project as a square. The one way
    /// out of that arithmetic is a skinned pose that departs from the bind pose, which is exactly
    /// what these three fields settle: if <c>localBounds</c> is the same 8:1 strip and
    /// <c>updateWhenOffscreen</c> is false, the drawn plate is that strip and the identification
    /// stays dead.</para>
    /// </summary>
    private static void AppendSkinned(StringBuilder sb, Renderer r)
    {
        if (r is not SkinnedMeshRenderer smr)
            return;
        try
        {
            Bounds lb = smr.localBounds;
            Transform? root = smr.rootBone;
            Mesh? mesh = smr.sharedMesh;
            sb.Append(" | skinned: localBounds c=").Append(Fmt3(lb.center))
              .Append(" s=").Append(Fmt3(lb.size))
              .Append(", rootBone='").Append(root != null ? root.name : "<none>")
              .Append("', bones=").Append(smr.bones != null ? smr.bones.Length : -1)
              .Append(", updateWhenOffscreen=").Append(smr.updateWhenOffscreen)
              .Append(", meshBounds s=")
              .Append(mesh != null ? Fmt3(mesh.bounds.size) : "<no mesh>")
              .Append(" — the AABB and the screen rect above are BIND-POSE numbers pushed through "
                      + "the root bone, not a measurement of the drawn geometry");
        }
        catch (Exception)
        {
            sb.Append(" | skinned: fields unreadable");
        }
    }

    private static string Fmt3(Vector3 v) =>
        v.x.ToString("F3") + "," + v.y.ToString("F3") + "," + v.z.ToString("F3");

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
