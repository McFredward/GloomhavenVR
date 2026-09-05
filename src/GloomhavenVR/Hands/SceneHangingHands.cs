using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// THE BANNERS MOVE WHEN YOU TOUCH THEM — the second mechanism, for the hangings that carry no
/// <c>Cloth</c> and that <see cref="SceneClothHands"/> therefore could never reach.
///
/// <para><b>THE REPORT (user, ModBuild 431).</b> "Die Flaggen reagieren immer noch nicht auf meine
/// Hand. Check die Logs nach dem Szenario was ich gerade teste und finde heraus welche Flaggen dort
/// sind und fix es dann für alle Flaggen."</para>
///
/// <para><b>WHY THE PREVIOUS MECHANISM COULD NOT HAVE WORKED, MEASURED NOT INFERRED.</b> The 431
/// scenery-cloth census reads <c>6 simulating scenery cloth(s) of 6 found</c> and names all six:
/// three Mindthief cloths, a Brute skirt, a Brute cloak, a Prime Demon cloth — every one of them a
/// FIGURE, at palm distances of 11-14 world units (5.4-6.5 real metres). In the very same
/// scenario the wall-fade census names <c>EN_CR_Hanging_01_Cloth_Post</c> (39 rows),
/// <c>EN_CR_Hanging_01_Mesh</c>, <c>EN_CR_Hanging_01_Cloth (1)</c>, <c>PCG_CR_Curtain_Red</c>,
/// <c>EN_CR_Curtain_Cloth</c> and <c>CR_ThemeBanner_04</c>. None of those is in the Cloth
/// registry, and <c>FindObjectsOfType&lt;Cloth&gt;</c> is not a sampling instrument — it returns
/// every one. So the scenery banners carry no <c>UnityEngine.Cloth</c> whatsoever, and no radius,
/// no reach and no bounds fix in that file could ever have moved one.</para>
///
/// <para><b>WHAT A BANNER ACTUALLY IS.</b> Read out of the same wall-fade census, which prints the
/// renderer KIND beside each name:</para>
/// <code>
/// Wall 1/Generated Content/PCG_CR_Banner_Grey/EN_CR_Hanging_01_Cloth (1)   ← the prop, 2 renderers
///     EN_CR_Hanging_01_Cloth_Post [skinned]  AABB s(0.9, 0.1, 0.1) at y 2.49   ← the RAIL
///     EN_CR_Hanging_01_Mesh       [skinned]  foot 0.68 / top 2.52 over the floor ← the FABRIC
/// </code>
/// <para>Two things fall out of that. First, the <c>s(0.9, 0.1, 0.1)</c> slab that looked like a
/// stale or unskinned bound is not the banner at all — it is the rail the banner hangs from, and
/// its sibling has a full 1.84 wu vertical drop with entirely correct bounds. The "wrong bounds"
/// hypothesis is dead. Second, the fabric is a <c>SkinnedMeshRenderer</c>: the art was RIGGED, and
/// a rigged mesh is a mesh somebody built so it could deform.</para>
///
/// <para><b>THE MECHANISM: DRIVE THE BIND BONES.</b> Each of the renderer's bones is rotated about
/// the hanging's own top rail by an angle proportional to how far BELOW that rail the bone sits.
/// A bone at the rail moves by nothing, a bone at the hem moves by the full angle, and everything
/// between bends like a sheet of cloth pushed from one side. It is presentation only: no physics,
/// no colliders, no cook, no allocation per frame.</para>
///
/// <para><b>AND THE SAME SOLVER SWINGS A PLAIN MESH.</b> A whole-object swing (rotate the
/// renderer's transform about its top edge) is INERT on a skinned mesh — a
/// <c>SkinnedMeshRenderer</c>'s vertices follow <c>bones[]</c> and ignore the renderer's own
/// transform entirely. It is very much ALIVE on a <c>MeshRenderer</c>, whose vertices follow
/// nothing else. So the two routes are not two mechanisms: they are one solver over a DRIVEN SET,
/// which is the bind bones for a rigged hanging and the renderer's own transform for a plain one.
/// Same spring, same dials, same pivot, same exact restore, same census. A rigged hanging with a
/// single bone lands in the middle of that and is handled by the same line of code. Adding a
/// <c>Cloth</c> at runtime was rejected outright: it cooks (linear in vertex count, on the frame
/// the hand arrives), it fights the game's own skinning, and it would need the same bones
/// anyway.</para>
///
/// <para><b>ROTATE ABOUT A PIVOT, NOT ALONG A CHAIN.</b> The obvious implementation walks the bone
/// chain and hinges each bone in its parent's frame. It is wrong here for a reason worth writing
/// down: <c>SkinnedMeshRenderer.bones</c> is a FLAT array in bind order and says nothing about
/// parentage — a cloth rig may be a chain, a grid, or a fan off one root, and a "walk the chain"
/// solver silently does nothing on two of those three. Writing each bone's WORLD pose from its own
/// captured home pose is topology-independent and order-independent: whatever a parent bone does
/// to its children is overwritten when those children are written from their own homes, so the
/// result is identical however the array is ordered.</para>
///
/// <para><b>RESTORED EXACTLY.</b> The home pose is captured per bone as LOCAL position and
/// rotation, and restore writes those back — local, so it is correct however the wall, the room or
/// the board has moved since. (The DEFLECTION maths needs world poses, and those are re-captured on
/// the registry sweep whenever the hanging is at rest, so a moved prop cannot leave the solver
/// working from a stale frame.) Restore happens when the hands leave, when the feature is switched
/// off, on scene change and on module teardown.</para>
///
/// <para><b>NOTHING FIGHTS US FOR THE BONES, AND WE CONCEDE ANYWAY.</b> This ticks in
/// <c>HandsDriver.LateUpdate</c>, after every <c>Animator</c> in the frame has written. If a
/// hanging ever does get an Animator driving its rig, our value is simply the last one written —
/// this project's standing rule (concede the flag, own the number). The census prints whether each
/// hanging has an Animator above it so the question is answerable rather than assumed.</para>
///
/// <para><b>THE POPULATION IS FOUND BY WHAT THESE OBJECTS ARE, NEVER BY A NAME.</b> This project
/// has shipped a name-substring rule twice on doors and paid for it twice. The rule here is a
/// conjunction of component and geometry terms, and the census prints how many candidates were
/// seen, how many qualified, and how many were rejected BY WHICH TERM with the first several
/// named:</para>
/// <list type="number">
/// <item><description>a renderer this class can actually move: a <c>SkinnedMeshRenderer</c> with
/// bones, or a <c>MeshRenderer</c> with a mesh. Skinned is the art's own declaration that a mesh
/// was built to deform — in the whole ModBuild-431 log the non-actor skinned renderers are exactly
/// the two banner parts and two door light meshes — and that single term is why the SKINNED lane
/// needs no defence against selecting a wall: a wall is not rigged. The MESH lane has no such
/// gift and pays for it with two extra terms (7 and 8).</description></item>
/// <item><description>not actor-owned (<see cref="SceneryActors"/>) — a Brute's cloak and a
/// demon's foliage are skinned too, and they belong to
/// <c>Board.FigureGrab.FigureClothHands</c>.</description></item>
/// <item><description>no <c>Cloth</c> on it — that is <see cref="SceneClothHands"/>'s, and two
/// owners writing one object is the defect this file exists beside.</description></item>
/// <item><description>no <c>Rigidbody</c> above it — a physics body owns its own transform and we
/// do not take a field somebody else writes every frame.</description></item>
/// <item><description>opaque: render queue below <see cref="TransparentQueue"/>. A light shaft or
/// a god-ray is a transparent skinned quad and is not fabric; this is what keeps the doorway light
/// meshes out without naming them.</description></item>
/// <item><description>a SHEET: thinnest oriented extent at most
/// <see cref="SheetThicknessWu"/> world units. Measured in the renderer's OWN frame
/// (<c>localBounds</c> x <c>lossyScale</c>), never from a world AABB — a banner hung at 45 degrees
/// to the world axes has a fat AABB in both horizontal axes and would fail a world-space
/// thickness test while being exactly as thin as it looks.</description></item>
/// <item><description>a DROP: at least <see cref="MinDropWu"/> world units tall, at least
/// <see cref="DropShareOfSpan"/> of its own width, and no wider than
/// <see cref="MaxSpanWu"/>. The banner's rail fails this on its first clause (0.1 wu tall) and
/// therefore never swings on its own, which is correct — a rail is masonry.</description></item>
/// <item><description>PLAIN MESH ONLY — AIRBORNE: the renderer's foot clears its room's floor by
/// <see cref="HangFootClearanceWu"/>. This is the term that separates a hanging from a WALL FACE,
/// and it is needed only in the mesh lane. It has to exist, because geometry alone cannot do it:
/// the ModBuild-431 wall census measures wall faces at <c>s(2.0,3.3,0.1)</c> and
/// <c>s(1.5,3.4,0.1)</c>, which pass every sheet-and-drop test that a curtain at
/// <c>s(1.6,2.6,0.2)</c> passes. What differs is where their FEET are: a wall face reaches the
/// floor, and a hanging does not — <c>EN_CR_Curtain_Mesh</c> has its foot 0.57 wu over the floor
/// and <c>EN_CR_Hanging_01_Mesh</c> 0.68 wu. The floor height comes from the room's own central
/// tile, which is where the wall fade gets its floors too.</description></item>
/// <item><description>PLAIN MESH ONLY — A LEAF, AND A SINGLE SUBMESH. The mesh route rotates a
/// TRANSFORM, so anything else parented under that transform is dragged with it; requiring that no
/// descendant carries a renderer makes that unrepresentable. A single submesh refuses a combined
/// or batched prop — several hangings welded into one renderer would all swing off one pivot. A
/// hanging welded to its OWN rail is fine and is not refused: the pivot IS the top edge, so the
/// rail end of such a mesh does not move.</description></item>
/// </list>
///
/// <para><b>THE PLAIN-MESH POPULATION COSTS NO SWEEP, AND THAT IS THE WHOLE DESIGN OF IT.</b>
/// <c>EN_CR_Curtain_Mesh</c> and <c>CR_BT_BanditBanner_Wall</c> are hangings the art shipped as
/// plain <c>MeshRenderer</c>s. The obvious way to reach them is
/// <c>FindObjectsOfType&lt;Renderer&gt;()</c>, and this build refuses it on a MEASUREMENT rather
/// than on a feeling: the ModBuild-431 log carries the wall fade's own worst-case field for
/// exactly that call, <c>FRESH FindObjectsOfType&lt;Renderer&gt; sweep (worst 0.60ms … 2.95ms)</c>.
/// A 2.95 ms spike every 3 s is a quarter of an 11.11 ms frame, handed to a player who asked for a
/// flag to move.</para>
///
/// <para>So the mesh lane reads a population the GAME already maintains and nobody has to sweep
/// for: <c>SceneRegistry.Volumes</c> — the mod's self-maintaining registry of
/// <c>TilesOcclusionVolume</c>, enrolled by a Harmony postfix on the volume's own <c>Start</c> —
/// hands over each room's <c>Renderers</c> array AND its <c>CentralTile</c>, which is the floor
/// height term 7 needs; and <c>TilesOcclusionGenerator.s_Instance.m_ObjectRenderers</c> is the
/// game's own live list of prop renderers. Neither read costs a scan. The recurring cost of the
/// whole lane is one walk of those lists with a memoized verdict lookup per entry — at the top of
/// the range the wall fade logs (2540 renderers) that is a few thousand dictionary probes, in the
/// same order as the 0.012-0.023 ms this project already measures for
/// <c>FindObjectsOfType&lt;Cloth&gt;</c>, and 20-100x under the sweep it replaces.</para>
///
/// <para>THE LANE STANDS DOWN RATHER THAN FALLING BACK. <c>ComponentRegistry.Collect</c> fails
/// OPEN — a disarmed registry does the very <c>FindObjectsOfType</c> it exists to replace — so
/// calling it blind would smuggle that sweep in through the back door. The lane is therefore gated
/// on <c>SceneRegistry.Volumes.Count &gt; 0</c>, which is reachable only through the enrolment
/// postfix (<c>Arm(false)</c> returns before seeding), so a non-zero count PROVES the registry is
/// armed. When it is zero the mesh lane does nothing at all and the census says which of the two
/// reasons it was.</para>
///
/// <para><b>MULTIPLAYER: ZERO WIRE BYTES.</b> Bone poses have never been a wire field and nothing
/// here is authoritative. Each client's own hands stir their own copy of the room, exactly as with
/// scenery cloth and scenery VFX; a peer on an older build sees their banners hang still.</para>
///
/// <para><b>SCALE.</b> The reach is REAL METRES AT THE HAND multiplied by
/// <c>VRHand.WorldScale</c> — the mod zooms the RIG, so a hand-anchored reach is the only kind
/// that still means "my hand is in the banner" at every zoom. The census prints every distance in
/// BOTH units with the scale beside them, because this project has already shipped a bound named
/// <c>...Meters</c> clamped against a world-unit product at 198x rig scale.</para>
/// </summary>
internal static class SceneHangingHands
{
    /// <summary>Everything at or above this render queue is transparent — a light shaft, a
    /// god-ray, a glow card. Fabric is opaque or alpha-tested (2000 / 2450), never this. Unity's
    /// own <c>Transparent</c> queue value, not a number of ours.</summary>
    private const int TransparentQueue = 3000;

    /// <summary>Thickest a sheet may be, in world units, measured along its own thinnest axis. The
    /// scenario's curtain fabric measures 0.2 wu and the banner rail 0.1; a wall is 0.1-1.4 but a
    /// wall is not rigged and never reaches this term.</summary>
    private const float SheetThicknessWu = 0.45f;

    /// <summary>Shortest drop that reads as a hanging. Below this it is a valance, a rail or a
    /// trim strip, and swinging it looks like a bug rather than like cloth.</summary>
    private const float MinDropWu = 0.5f;

    /// <summary>A hanging drops at least this share of its own width. A banner is 1.84 wu over 0.9;
    /// a wide low strip is a pelmet.</summary>
    private const float DropShareOfSpan = 0.7f;

    /// <summary>A plain-mesh hanging's foot must clear its room's floor by this much. THE term
    /// that separates a hanging from a wall face, which geometry alone cannot: the 431 census
    /// measures wall faces at s(2.0,3.3,0.1) with their feet ON the floor, and the curtain and the
    /// banner at 0.57 and 0.68 wu over it. 0.35 sits between the two populations with margin on
    /// both sides. (The wall fade's own "airborne bar" is 1.00 wu and answers a different question
    /// — "is this thing floating?" — which both of these hangings fail.)</summary>
    private const float HangFootClearanceWu = 0.35f;

    /// <summary>Widest a single hanging may be. Past this it is a wall of fabric and one pivot at
    /// its centre would swing the whole thing from a touch at one corner.</summary>
    private const float MaxSpanWu = 3.5f;

    /// <summary>How close the palm must come to the hanging's own oriented box before it pushes,
    /// in real metres at the hand. Generous on purpose: being early costs a distance test, being
    /// late costs the hand passing through a banner untouched, which is the entire report.</summary>
    private const float ReachRealMeters = 0.2f;

    /// <summary>The reach never drops below this multiple of the hand sphere
    /// ([Hands] SceneryClothHandRadiusMillimeters), so a player who turns the hand up to its
    /// maximum thickness still gets a gate ahead of the thing it gates.</summary>
    private const float ReachAtLeastTimesRadius = 4f;

    /// <summary>How much of the push a SWIPE contributes on top of simple presence, as a share of
    /// the full swing. Not a dial: it is the ratio that makes a moving hand read as a moving hand
    /// while a hand held still in the fabric still holds it aside.</summary>
    private const float HandMotionShare = 0.5f;

    /// <summary>Palm speed, in real metres per second, at which the motion term saturates. A
    /// deliberate swipe is around this.</summary>
    private const float HandMotionFullSpeed = 1.5f;

    /// <summary>Seconds between registry sweeps. The sweep is a <c>FindObjectsOfType</c>, which
    /// this project has been burned by three times — including once where an early-out clause
    /// bypassed the cadence on an EMPTY population and ran the sweep every frame. There is
    /// therefore NO population term in the cadence test below, and the clamp under it cannot be
    /// dialled to zero.</summary>
    private const float RescanSeconds = 3f;

    /// <summary>How long the census may stay quiet while nothing about the population changes. A
    /// heartbeat, not a cadence.</summary>
    private const float CensusHeartbeatSeconds = 30f;

    /// <summary>How many hangings the census names in full, accepted and rejected each. A
    /// truncated list is not absence, so the line always states the total it was drawn from.</summary>
    private const int CensusNamed = 5;

    /// <summary>Longest frame the spring integrator will accept. A loading hitch integrated at its
    /// true length throws the solver across the room; clamping the step makes a hitch a slow frame
    /// rather than an event.</summary>
    private const float MaxStepSeconds = 0.05f;

    /// <summary>Below this world displacement, and with the target at zero, a hanging is declared
    /// home: it is restored exactly once and then written no more.</summary>
    private const float RestEpsilonWu = 0.0005f;

    /// <summary>Shader properties that mean "this surface was authored to move in wind". Reported
    /// by the census because the answer changes what a future round would build; nothing gates on
    /// it. These are SHADER PROPERTY names — API surface — and not asset names.</summary>
    private static readonly string[] WindProperties =
    {
        "_AddVertexAnim", "_VertexAnim_Intensity", "_NoiseSpeed", "_WindStrength", "_Wind",
    };

    private static readonly List<Hanging> _scene = new(8);

    // Verdict memo: every SkinnedMeshRenderer this session has already been judged, so a sweep
    // re-judges nothing. Rejections are memoized WITH their reason and their line — the memo is
    // what the census counts from, so the term counts describe the WHOLE live population on every
    // sweep and not just whatever happened to be new on that one.
    private static readonly Dictionary<Renderer, Judgement> _verdict = new(256);
    private static readonly List<Renderer> _verdictDrop = new(8);

    // THE PLAIN-MESH LANE'S SOURCES, both free. _volumeScratch is refilled from the mod's
    // self-maintaining occlusion-volume registry (no sweep); _floors is each room's central-tile
    // position, which is where the wall fade gets its floor heights too; _seen stops a renderer
    // that appears in BOTH lists from being counted twice in one sweep's term totals.
    private static readonly List<TilesOcclusionVolume> _volumeScratch = new(16);
    private static readonly List<Vector3> _floors = new(16);
    private static readonly HashSet<Renderer> _seen = new();
    private static readonly List<Renderer> _rendererScratch = new(8);

    private static float _nextScanAt;

    // LIVENESS. Incremented by EVERY sweep, whatever the sweep finds, and printed in every census
    // line. A frozen number means the sweep stopped; it can never mean "the room has no banners".
    private static int _sweeps;

    private static int _lastCandidates;
    private static int _lastSkinnedCandidates;
    private static int _lastMeshCandidates;
    private static int _lastVolumes;
    private static string _meshLaneStandDown = "";
    private static float _lastSweepMs;
    private static readonly int[] _rejected = new int[(int)Reject.Count];
    private static readonly List<string> _rejectNamed = new(CensusNamed);

    private static int _lastPopulation = -1;
    private static float _lastCensusAt = float.NegativeInfinity;

    private static readonly System.Text.StringBuilder _census = new(1024);
    private static bool _loggedFirstTouch;

    /// <summary>Why a candidate is not a hanging. Ordered as the test runs, so the census reads as
    /// a funnel.</summary>
    private enum Reject
    {
        Actor,
        HasCloth,
        HasRigidbody,
        NoRig,
        Transparent,
        NotASheet,
        NotADrop,
        NotAirborne,
        NotALeaf,
        Count,
    }

    /// <summary>One memoized verdict. <see cref="Reason"/> is <c>-1</c> for an accepted hanging and
    /// otherwise the <see cref="Reject"/> term that stopped it; <see cref="Note"/> is the line the
    /// census prints for a near miss.</summary>
    private readonly struct Judgement
    {
        internal readonly int Reason;
        internal readonly string Note;

        internal Judgement(int reason, string note)
        {
            Reason = reason;
            Note = note;
        }
    }

    /// <summary>The feature switch — the SAME switch as scenery cloth ([Hands]
    /// HandsDisturbScenery). One user-facing idea ("meine Hände bewegen die Umgebung") stays one
    /// setting: a second toggle for the same sentence is a dial nobody can find. Defensive against
    /// a null config because this is called from a per-frame path that runs before and after the
    /// config's lifetime.</summary>
    internal static bool Enabled
    {
        get
        {
            try
            {
                return HandsConfig.HandsDisturbScenery != null
                       && HandsConfig.HandsDisturbScenery.Value
                       && HandsConfig.SceneryHangingSwingDegreesClamped > 0.01f;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Per-frame step, called from <c>HandsDriver.LateUpdate</c> under its own
    /// <see cref="TickGuard"/>. Strict no-op when the feature is off or no hand is tracked — and
    /// "no-op" means every hanging is restored first, because a feature switched off mid-swing
    /// must leave the room exactly as it found it.
    /// </summary>
    internal static void Tick()
    {
        if (!Enabled)
        {
            RestoreAll();
            return;
        }

        VRHand? left = VRHands.Left;
        VRHand? right = VRHands.Right;
        bool leftLive = left != null && left.IsTracked && left.Rig != null && left.Rig.IsComplete;
        bool rightLive = right != null && right.IsTracked && right.Rig != null
                         && right.Rig.IsComplete;
        if (!leftLive && !rightLive)
        {
            // The hands are gone, but a banner may be mid-swing. Let the springs run home rather
            // than snapping — a snap on tracking loss is a visible pop — and stop only once the
            // room is at rest.
            Settle();
            return;
        }

        bool swept = Rescan();
        Drive(leftLive ? left : null, rightLive ? right : null);
        if (swept)
            Census();
    }

    /// <summary>Module shutdown / hot reload / scene change: every hanging back to its authored
    /// pose, immediately and exactly, and the registry emptied.</summary>
    internal static void Shutdown()
    {
        RestoreAll();
        _scene.Clear();
        _verdict.Clear();
        _volumeScratch.Clear();
        _floors.Clear();
        _seen.Clear();
        _rendererScratch.Clear();
        _meshLaneStandDown = "";
        _nextScanAt = 0f;
        _loggedFirstTouch = false;
        // _sweeps is NOT reset. It is the liveness field, and a counter that restarts at zero
        // cannot distinguish a fresh scene from a stalled sweep — which is the one thing it is for.
        _lastPopulation = -1;
        _lastCensusAt = float.NegativeInfinity;
    }

    /// <summary>Write every deflected hanging back to its authored pose and drop the spring state.
    /// Idempotent, and cheap when nothing is deflected.</summary>
    private static void RestoreAll()
    {
        for (int i = 0; i < _scene.Count; i++)
            _scene[i].Restore();
    }

    /// <summary>Run the springs home with no hand input at all. Used while no hand is tracked, so
    /// a banner already pushed aside eases back instead of snapping.</summary>
    private static void Settle()
    {
        float dt = Mathf.Clamp(Time.deltaTime, 0f, MaxStepSeconds);
        for (int i = 0; i < _scene.Count; i++)
            _scene[i].Step(Vector3.zero, dt);
    }

    /// <summary>Rebuild the registry if the cadence is due. Returns true when a sweep actually
    /// ran.</summary>
    private static bool Rescan()
    {
        float now = Time.unscaledTime;
        // NO POPULATION TERM in this test, on purpose. Three classes in this codebase have been
        // found running FindObjectsOfType every frame because an early-out clause bypassed its own
        // cadence on an empty population. This is not the fourth.
        if (now < _nextScanAt)
            return false;
        _nextScanAt = now + Mathf.Max(0.5f, RescanSeconds);
        _sweeps++;

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < _rejected.Length; i++)
            _rejected[i] = 0;
        _rejectNamed.Clear();
        _seen.Clear();
        _floors.Clear();

        // Keep the hangings that are still alive; drop dead rows (their bones went with them).
        for (int i = _scene.Count - 1; i >= 0; i--)
        {
            if (!_scene[i].Alive)
                _scene.RemoveAt(i);
        }

        // LANE 1 — RIGGED HANGINGS. FindObjectsOfType<SkinnedMeshRenderer>() is the one sweep this
        // class takes, on this 3 s cadence and only while a hand is tracked. It is affordable
        // because the type is rare: the sibling class measures the same call for Cloth at
        // 0.012-0.023 ms on hardware, against 0.60-2.95 ms for the Renderer sweep the wall fade
        // logs. There is no cheaper enumeration of skinned renderers in the mod, and the mesh lane
        // below shows what one looks like when there is.
        SkinnedMeshRenderer[] found = Object.FindObjectsOfType<SkinnedMeshRenderer>();
        _lastSkinnedCandidates = found.Length;
        for (int i = 0; i < found.Length; i++)
            Judge(found[i], false);

        // LANE 2 — PLAIN-MESH HANGINGS, OFF A POPULATION SOMEBODY ELSE ALREADY MAINTAINS. No
        // sweep: the mod's occlusion-volume registry enrolls every room through a Harmony postfix
        // on the game's own TilesOcclusionVolume.Start, and each volume carries both the room's
        // MeshRenderer array and the CentralTile whose height is the floor. The game's own
        // m_ObjectRenderers list is added on top for props that enrol themselves that way.
        _lastMeshCandidates = 0;
        _lastVolumes = 0;
        // GATED ON A NON-ZERO COUNT, NOT ON A TRY. ComponentRegistry.Collect FAILS OPEN — a
        // disarmed registry runs the very FindObjectsOfType it exists to replace — so calling it
        // blind would smuggle that sweep in through the back door. Count is reachable only through
        // the enrolment postfix (Arm(false) returns before seeding), so non-zero PROVES armed.
        if (SceneRegistry.Volumes.Count <= 0)
        {
            _meshLaneStandDown =
                "STOOD DOWN: SceneRegistry.Volumes.Count is 0, which is either the menu (no room "
                + "exists) or a disarmed registry. This lane READS that registry and never sweeps, "
                + "so it does nothing rather than fall back to FindObjectsOfType<Renderer> — the "
                + "call the wall fade measures at 0.60-2.95 ms.";
        }
        else
        {
            _meshLaneStandDown = "";
            SceneRegistry.Volumes.Collect(_volumeScratch);
            _lastVolumes = _volumeScratch.Count;
            for (int i = 0; i < _volumeScratch.Count; i++)
            {
                TilesOcclusionVolume v = _volumeScratch[i];
                if (v != null && v.CentralTile != null)
                    _floors.Add(v.CentralTile.transform.position);
            }
            for (int i = 0; i < _volumeScratch.Count; i++)
            {
                TilesOcclusionVolume v = _volumeScratch[i];
                MeshRenderer[]? rs = v != null ? v.Renderers : null;
                if (rs == null)
                    continue;
                for (int k = 0; k < rs.Length; k++)
                {
                    _lastMeshCandidates++;
                    Judge(rs[k], true);
                }
            }
            TilesOcclusionGenerator gen = TilesOcclusionGenerator.s_Instance;
            List<MeshRenderer>? objects = gen != null ? gen.m_ObjectRenderers : null;
            if (objects != null)
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    _lastMeshCandidates++;
                    Judge(objects[i], true);
                }
            }
        }
        _lastCandidates = _lastSkinnedCandidates + _lastMeshCandidates;

        // Re-anchor every hanging that is at rest: its world home pose, bounds and pivot are what
        // the deflection maths works from, and a prop that has been moved (a room re-entered, a
        // board re-laid) would otherwise be solved against a frame that no longer exists. Only at
        // REST, because re-anchoring a hanging mid-swing would bake the swing into its home.
        for (int i = 0; i < _scene.Count; i++)
            _scene[i].ReanchorIfHome();

        PruneVerdicts();

        _lastSweepMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
                               / System.Diagnostics.Stopwatch.Frequency);
        return true;
    }

    /// <summary>Judge one candidate: memo hit, or classify once and remember. The seen-set is
    /// what stops a renderer that appears in BOTH plain-mesh sources from being counted twice in
    /// one sweep's term totals — a ratio with two populations in one buffer is a defect this
    /// project has already paid for.</summary>
    private static void Judge(Renderer? candidate, bool plainMesh)
    {
        if (candidate == null || !_seen.Add(candidate))
            return;
        if (!_verdict.TryGetValue(candidate, out Judgement judged))
        {
            // JUDGED ONCE, EVER. The component walks, the material reads and the string are paid
            // for on the sweep that first sees a renderer and never again; from then on this is a
            // dictionary probe, which is what makes the plain-mesh lane's thousands affordable.
            Hanging? built = Classify(candidate, plainMesh, out Reject why, out string note);
            judged = built == null ? new Judgement((int)why, note) : new Judgement(-1, "");
            _verdict[candidate] = judged;
            if (built != null)
                _scene.Add(built);
        }
        if (judged.Reason < 0)
            return;
        _rejected[judged.Reason]++;
        if (_rejectNamed.Count < CensusNamed)
            _rejectNamed.Add(judged.Note);
    }

    /// <summary>The floor height under a point: the CentralTile of the nearest room volume in XZ,
    /// which is exactly where <c>WallSegmentFade</c> gets its per-renderer floor. With no volumes
    /// this returns the point's own height, so the airborne term below measures zero clearance and
    /// REFUSES — a missing floor must never read as "it is off the ground".</summary>
    private static float FloorUnder(Vector3 p)
    {
        float bestSq = float.PositiveInfinity;
        float y = p.y;
        for (int i = 0; i < _floors.Count; i++)
        {
            Vector3 f = _floors[i];
            float dx = f.x - p.x;
            float dz = f.z - p.z;
            float d = dx * dx + dz * dz;
            if (d < bestSq)
            {
                bestSq = d;
                y = f.y;
            }
        }
        return y;
    }

    /// <summary>Forget memoized verdicts whose renderer has been destroyed. Bounded work: it runs
    /// on the sweep cadence and only when the memo has outgrown the live candidate count.</summary>
    private static void PruneVerdicts()
    {
        if (_verdict.Count <= _lastCandidates)
            return;
        _verdictDrop.Clear();
        foreach (KeyValuePair<Renderer, Judgement> kv in _verdict)
        {
            // The Unity-null test and the reference test disagree here on purpose, and that
            // disagreement IS the destroyed case: the key is still a live C# reference (so it can
            // still be removed) while the renderer behind it is gone.
            Renderer key = kv.Key;
            if (key == null)
                _verdictDrop.Add(key!);
        }
        for (int i = 0; i < _verdictDrop.Count; i++)
            _verdict.Remove(_verdictDrop[i]);
        _verdictDrop.Clear();
    }

    /// <summary>
    /// The population test, term by term, exactly as the class doc lists it. Returns the built
    /// hanging, or null with the term that rejected it and a line naming what was measured.
    ///
    /// <para>Runs ONCE per renderer, ever — the verdict is memoized — so it may afford the
    /// component walks and the string it produces. Nothing here runs per frame.</para>
    /// </summary>
    private static Hanging? Classify(Renderer r, bool plainMesh, out Reject why, out string note)
    {
        why = Reject.Actor;
        string path = PathOf(r.transform);

        if (SceneryActors.IsActorOwned(r.transform, out string actorWhy))
        {
            note = $"'{r.name}' at '{path}' — {actorWhy} (a figure's cloak belongs to "
                   + "FigureClothHands)";
            return null;
        }

        if (r.GetComponent<Cloth>() != null)
        {
            why = Reject.HasCloth;
            note = $"'{r.name}' at '{path}' — carries a UnityEngine.Cloth, which SceneClothHands "
                   + "owns; two writers on one object is the defect this class sits beside";
            return null;
        }

        if (r.GetComponentInParent<Rigidbody>(true) != null)
        {
            why = Reject.HasRigidbody;
            note = $"'{r.name}' at '{path}' — a Rigidbody above it owns this transform every frame";
            return null;
        }

        // WHICH TRANSFORM IS localBounds EXPRESSED IN? Unity's answer is "the root bone if one is
        // assigned, otherwise the renderer's own transform" — and this class is not going to take
        // that on trust, because if it is wrong every distance gate below is measured in the wrong
        // frame and the feature looks dead for a reason no census would name. So both candidates
        // are TESTED against the one number Unity computes itself: the world-space bounds centre.
        // The frame whose transform of localBounds.center lands closer to r.bounds.center is the
        // frame Unity used. No doc is trusted; the residual is measured.
        Transform frame = r.transform;
        Transform[]? bones = null;
        int boneCount = 0;
        Bounds local;
        var skinned = r as SkinnedMeshRenderer;
        if (skinned != null)
        {
            try
            {
                Vector3 truth = r.bounds.center;
                Vector3 localCentre = skinned.localBounds.center;
                float own = (r.transform.TransformPoint(localCentre) - truth).sqrMagnitude;
                if (skinned.rootBone != null
                    && (skinned.rootBone.TransformPoint(localCentre) - truth).sqrMagnitude < own)
                {
                    frame = skinned.rootBone;
                }
            }
            catch
            {
                // A renderer mid-teardown throws out of the bounds accessors. Fall back to the
                // documented answer rather than dropping a candidate over an instrument.
                frame = skinned.rootBone != null ? skinned.rootBone : r.transform;
            }
            bones = skinned.bones;
            if (bones != null)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null)
                        boneCount++;
                }
            }
            if (boneCount == 0 && skinned.rootBone == null)
            {
                // A SkinnedMeshRenderer with neither bones nor a root bone has nothing whose
                // motion the vertices follow, so there is no write that could move it. Counted,
                // not silently dropped: "no rig" and "not a hanging" are different verdicts.
                why = Reject.NoRig;
                note = $"'{r.name}' at '{path}' — no bones and no root bone: nothing to drive";
                return null;
            }
            local = skinned.localBounds;
        }
        else
        {
            // A PLAIN MESH'S RIG IS ITS TRANSFORM. Its vertices follow nothing else, which is
            // exactly why the whole-object swing that is inert on a skinned mesh works here.
            var filter = r.GetComponent<MeshFilter>();
            Mesh? shared = filter != null ? filter.sharedMesh : null;
            if (shared == null)
            {
                why = Reject.NoRig;
                note = $"'{r.name}' at '{path}' — a MeshRenderer with no shared mesh: no bounds "
                       + "to judge and nothing to drive";
                return null;
            }
            local = shared.bounds;

            // A LEAF, AND A SINGLE SUBMESH. The mesh route rotates a TRANSFORM, so any renderer
            // parented under it would be dragged along; and a multi-submesh mesh is the shape a
            // combined or batched prop takes, where one pivot would swing several props at once.
            // A hanging welded to its OWN rail is NOT refused here and does not need to be: the
            // pivot is the top edge, so the rail end of such a mesh does not move.
            _rendererScratch.Clear();
            r.GetComponentsInChildren(true, _rendererScratch);
            if (_rendererScratch.Count > 1 || shared.subMeshCount > 1)
            {
                why = Reject.NotALeaf;
                note = $"'{r.name}' at '{path}' — {_rendererScratch.Count} renderer(s) in its own "
                       + $"subtree and {shared.subMeshCount} submesh(es): swinging this transform "
                       + "would move things that are not this hanging";
                _rendererScratch.Clear();
                return null;
            }
            _rendererScratch.Clear();

            // AIRBORNE. THE term that separates a hanging from a wall face, and the reason the
            // plain-mesh lane needs the room registry rather than only a type filter: the 431
            // census measures wall faces at s(2.0,3.3,0.1) with their feet ON the floor, and this
            // scenario's curtain and banner at 0.57 and 0.68 wu over it.
            Bounds world = r.bounds;
            float floorY = FloorUnder(world.center);
            float clearance = world.min.y - floorY;
            if (clearance < HangFootClearanceWu)
            {
                why = Reject.NotAirborne;
                note = $"'{r.name}' at '{path}' — foot {clearance:0.##} wu over its room floor, "
                       + $"under the {HangFootClearanceWu:0.##} wu bar: it reaches the ground, so "
                       + "it is masonry or floor dressing and not a hanging";
                return null;
            }
        }

        Material? mat = null;
        try
        {
            mat = r.sharedMaterial;
        }
        catch
        {
            // A renderer mid-teardown throws out of the material accessor. An instrument must
            // never be the thing that breaks; treat it as unknown and let the geometry decide.
        }
        int queue = mat != null ? mat.renderQueue : 2000;
        if (queue >= TransparentQueue)
        {
            why = Reject.Transparent;
            note = $"'{r.name}' at '{path}' — render queue {queue} is transparent "
                   + "(a light shaft or a glow card, not fabric)";
            return null;
        }

        // THE SHAPE, MEASURED IN THE RENDERER'S OWN FRAME. localBounds x lossyScale, never a world
        // AABB: a banner hung at 45 degrees to the world axes has a fat AABB in both horizontal
        // axes and would fail a world-space thickness test while being exactly as thin as it looks.
        Vector3 scale = frame.lossyScale;
        var extent = new Vector3(Mathf.Abs(local.size.x * scale.x),
                                 Mathf.Abs(local.size.y * scale.y),
                                 Mathf.Abs(local.size.z * scale.z));

        int thinAxis = extent.x <= extent.y && extent.x <= extent.z ? 0
                       : extent.y <= extent.z ? 1 : 2;
        float thickness = extent[thinAxis];
        if (thickness > SheetThicknessWu)
        {
            why = Reject.NotASheet;
            note = $"'{r.name}' at '{path}' — thinnest oriented extent {thickness:0.###} wu is "
                   + $"over the {SheetThicknessWu:0.##} wu sheet limit "
                   + $"(oriented size {extent.x:0.##}x{extent.y:0.##}x{extent.z:0.##} wu)";
            return null;
        }

        // THE DROP is measured along whichever of the frame's own axes points most steeply down,
        // because a banner's rig need not have its Y up. The span is the widest of the other two.
        int dropAxis = MostVerticalAxis(frame, out float dropSign);
        float drop = extent[dropAxis];
        float span = 0f;
        for (int a = 0; a < 3; a++)
        {
            if (a != dropAxis && extent[a] > span)
                span = extent[a];
        }
        if (drop < MinDropWu || span > MaxSpanWu || drop < DropShareOfSpan * span)
        {
            why = Reject.NotADrop;
            note = $"'{r.name}' at '{path}' — drop {drop:0.##} wu over span {span:0.##} wu is not "
                   + $"a hanging (needs >= {MinDropWu:0.##} wu, <= {MaxSpanWu:0.##} wu wide, and "
                   + $">= {DropShareOfSpan:0.##} of its own width). A rail measures 0.1 wu tall "
                   + "and is meant to fail here";
            return null;
        }

        why = Reject.Count;
        note = "";
        return Hanging.Build(r, plainMesh || skinned == null, frame, bones, boneCount, local,
                             thinAxis, dropAxis, dropSign, thickness, drop, span, path, mat,
                             queue);
    }

    /// <summary>Which of a transform's own axes points most steeply along world down, and with
    /// which sign. A banner's rig is not obliged to have its Y up.</summary>
    private static int MostVerticalAxis(Transform t, out float sign)
    {
        float x = Vector3.Dot(t.right, Vector3.up);
        float y = Vector3.Dot(t.up, Vector3.up);
        float z = Vector3.Dot(t.forward, Vector3.up);
        float ax = Mathf.Abs(x), ay = Mathf.Abs(y), az = Mathf.Abs(z);
        if (ay >= ax && ay >= az) { sign = Mathf.Sign(y == 0f ? 1f : y); return 1; }
        if (ax >= az) { sign = Mathf.Sign(x == 0f ? 1f : x); return 0; }
        sign = Mathf.Sign(z == 0f ? 1f : z);
        return 2;
    }

    /// <summary>The hierarchy path, capped at six levels. It is the field that says WHICH prop a
    /// renderer belongs to, and the one that lets a row here be matched against a row in the
    /// wall-fade census.</summary>
    private static string PathOf(Transform t)
    {
        string path = t.name;
        Transform? p = t.parent;
        for (int depth = 0; p != null && depth < 6; depth++, p = p.parent)
            path = p.name + "/" + path;
        return path;
    }

    /// <summary>The attach reach for the current hand sphere, in real metres at the hand.</summary>
    private static float ReachRealMetersNow()
        => Mathf.Max(ReachRealMeters,
                     HandsConfig.SceneryClothHandRadiusRealMeters * ReachAtLeastTimesRadius);

    /// <summary>One frame of push and spring for every hanging in the registry. Allocation-free,
    /// and write-free for any hanging that is home and staying home.</summary>
    private static void Drive(VRHand? left, VRHand? right)
    {
        float dt = Mathf.Clamp(Time.deltaTime, 0f, MaxStepSeconds);
        float reachReal = ReachRealMetersNow();
        float swingDeg = HandsConfig.SceneryHangingSwingDegreesClamped;

        for (int i = 0; i < _scene.Count; i++)
        {
            Hanging h = _scene[i];
            if (!h.Alive)
                continue;
            Vector3 target = Vector3.zero;
            float maxOffset = h.Drop * Mathf.Tan(swingDeg * Mathf.Deg2Rad);
            target += h.Push(left, reachReal, maxOffset);
            target += h.Push(right, reachReal, maxOffset);
            if (target.sqrMagnitude > maxOffset * maxOffset)
                target = target.normalized * maxOffset;
            h.Step(target, dt);

            if (!_loggedFirstTouch && target.sqrMagnitude > 0f)
            {
                _loggedFirstTouch = true;
                LogFirstTouch(h, reachReal, swingDeg, maxOffset);
            }
        }
    }

    private static void LogFirstTouch(Hanging h, float reachReal, float swingDeg, float maxOffset)
    {
        float scale = Mathf.Max(VRHands.Left != null ? VRHands.Left.WorldScale : 1f,
                                VRHands.Right != null ? VRHands.Right.WorldScale : 1f);
        // HW-VERIFY
        VRLog.Note("Hands", "Scenery hanging: a hand is inside a banner for the first time — its "
            + "bind bones are being rotated about its own top rail and will be restored to the "
            + "authored local pose the moment the swing settles. No wire field: every client "
            + "stirs its own copy of the room. "
            + $"SWEEP {_sweeps}. THE HANGING: {h.Describe()} "
            + $"REACH {reachReal * 1000f:0.#} mm real = {reachReal * scale:0.###} wu at rig scale "
            + $"x{scale:0.###}; swing cap {swingDeg:0.#} deg = {maxOffset:0.###} wu at the hem. "
            + "IF THIS LINE APPEARS AND NOTHING VISIBLY MOVED, the bones were written and the "
            + "answer is in the numbers above — one bone (so the whole sheet turns rigidly and "
            + "may be hidden by the wall behind it), a drop far larger than the swing, or a rig "
            + "whose bones do not weight the visible fabric. "
            + "Dials: [Hands] SceneryHangingSwingDegrees, SceneryHangingSettleSeconds.");
    }

    /// <summary>
    /// THE LINE THAT DECIDES THE FEATURE. Emitted whenever the POPULATION signature moves and
    /// otherwise every <see cref="CensusHeartbeatSeconds"/>, so it can go quiet only while nothing
    /// changes and never for a whole session. Every line carries the sweep counter, which moves on
    /// EVERY sweep whatever is found: a frozen counter means the sweep stopped, and an unchanged
    /// population with a moving counter means the room really has no banners.
    /// </summary>
    private static void Census()
    {
        int population = (_lastCandidates * 397) ^ (_scene.Count * 131)
                         ^ (_lastSkinnedCandidates * 73) ^ (_lastMeshCandidates * 11)
                         ^ (_lastVolumes * 7) ^ _meshLaneStandDown.Length;
        for (int i = 0; i < _rejected.Length; i++)
            population ^= _rejected[i] * (17 + i);
        float now = Time.unscaledTime;
        if (population == _lastPopulation && now - _lastCensusAt < CensusHeartbeatSeconds)
            return;
        _lastPopulation = population;
        _lastCensusAt = now;

        float scale = Mathf.Max(VRHands.Left != null ? VRHands.Left.WorldScale : 1f,
                                VRHands.Right != null ? VRHands.Right.WorldScale : 1f, 1e-4f);
        float reachReal = ReachRealMetersNow();
        float swingDeg = HandsConfig.SceneryHangingSwingDegreesClamped;

        _census.Length = 0;
        _census.Append("Scenery hanging scan #").Append(_sweeps).Append(": ")
               .Append(_scene.Count).Append(" hanging(s) driven of ").Append(_lastCandidates)
               .Append(" skinned candidate(s) in ").Append(_lastSweepMs.ToString("F3"))
               .Append(" ms. This runs every ").Append(RescanSeconds.ToString("0.#"))
               .Append(" s while a hand is tracked, never per frame.");
        // APPENDED. The 'skinned candidate(s)' token above is kept word for word so a grep that
        // found the first version of this line still finds it; the number behind it is now BOTH
        // lanes, and this clause splits it.
        _census.Append(" TWO LANES, ONE SOLVER: RIGGED ").Append(_lastSkinnedCandidates)
               .Append(" candidate(s) from the one FindObjectsOfType<SkinnedMeshRenderer> this "
                       + "class takes (a rare type — the sibling class measures the same call for "
                       + "Cloth at 0.012-0.023 ms); PLAIN MESH ").Append(_lastMeshCandidates)
               .Append(" candidate(s) from ").Append(_lastVolumes)
               .Append(" occlusion volume(s) plus the game's own m_ObjectRenderers list, WITH NO "
                       + "SWEEP AT ALL — the wall fade measures FindObjectsOfType<Renderer> at "
                       + "0.60-2.95 ms and this lane refuses to pay it.");
        if (_meshLaneStandDown.Length > 0)
            _census.Append(" PLAIN-MESH LANE ").Append(_meshLaneStandDown);
        _census.Append(" SWEEP ").Append(_sweeps)
               .Append(" (this counter moves on EVERY sweep whatever is found, so a frozen number "
                       + "means the sweep STOPPED and never means the room has no banners).");
        _census.Append(" SCALE x").Append(scale.ToString("0.###"))
               .Append(" world units per real metre: reach ")
               .Append((reachReal * 1000f).ToString("0.#")).Append(" mm real = ")
               .Append((reachReal * scale).ToString("0.###")).Append(" wu; swing cap ")
               .Append(swingDeg.ToString("0.#")).Append(" deg.");

        _census.Append(" REJECTED BY TERM (the population is found by WHAT these objects are and "
                       + "never by a name; every candidate that did not qualify is counted under "
                       + "the term that stopped it): actor-owned ")
               .Append(_rejected[(int)Reject.Actor])
               .Append(", carries a Cloth ").Append(_rejected[(int)Reject.HasCloth])
               .Append(", Rigidbody above ").Append(_rejected[(int)Reject.HasRigidbody])
               .Append(", no rig ").Append(_rejected[(int)Reject.NoRig])
               .Append(", transparent ").Append(_rejected[(int)Reject.Transparent])
               .Append(", not a sheet ").Append(_rejected[(int)Reject.NotASheet])
               .Append(", not a drop ").Append(_rejected[(int)Reject.NotADrop])
               .Append(", not airborne ").Append(_rejected[(int)Reject.NotAirborne])
               .Append(" (plain mesh only — a foot on the floor is a wall face, not a hanging)")
               .Append(", not a leaf ").Append(_rejected[(int)Reject.NotALeaf])
               .Append(" (plain mesh only — swinging that transform would move something else)")
               .Append('.');
        if (_rejectNamed.Count > 0)
        {
            _census.Append(" NEAR MISSES (naming ").Append(_rejectNamed.Count)
                   .Append(", and a truncated list is not absence)");
            for (int i = 0; i < _rejectNamed.Count; i++)
                _census.Append(i == 0 ? ": " : "; ").Append(_rejectNamed[i]);
            _census.Append('.');
        }

        _census.Append(" HANGINGS (naming ").Append(Mathf.Min(CensusNamed, _scene.Count))
               .Append(" of ").Append(_scene.Count)
               .Append(", and a truncated list is not absence)");
        if (_scene.Count == 0)
        {
            _census.Append(": none. With SWEEP moving and both lanes reporting candidates, this "
                           + "room really has no hanging either lane can drive — not a stalled "
                           + "instrument. If the PLAIN MESH candidate count is 0 as well, the "
                           + "mesh lane never had anything to judge and the stand-down clause "
                           + "above says why.");
        }
        for (int i = 0; i < _scene.Count && i < CensusNamed; i++)
            _census.Append(i == 0 ? ": " : "; ").Append(_scene[i].Describe());

        _census.Append(" READ IT LIKE THIS: 'skinned candidate(s)' 0 means neither lane saw "
                       + "anything and the room is not loaded; candidates non-zero with 0 driven "
                       + "means every one was rejected and the term counts above say which test "
                       + "to loosen — and read them PER LANE, because 'not airborne' and 'not a "
                       + "leaf' can only ever come from the plain-mesh lane while a wall can only "
                       + "ever reach the plain-mesh lane; driven non-zero with nothing visibly "
                       + "moving means the rig was written and the per-hanging numbers say why — "
                       + "a one-bone rig turns the sheet rigidly, and a drop far larger than the "
                       + "swing moves the hem by very little. Dials: [Hands] "
                       + "SceneryHangingSwingDegrees, SceneryHangingSettleSeconds, and the master "
                       + "switch [Hands] HandsDisturbScenery.");
        // HW-VERIFY
        VRLog.Note("Hands", _census.ToString());
    }

    /// <summary>
    /// One hanging: its renderer, the bones we drive, the authored pose we restore to, and the
    /// spring state.
    ///
    /// <para>TWO CAPTURES, ON PURPOSE. The RESTORE capture is per-bone LOCAL position and rotation,
    /// so writing it back is exact however the wall, the room or the board has moved. The SOLVE
    /// capture is per-bone WORLD position and rotation plus the pivot and bounds, because the
    /// deflection is a rotation about a world pivot; it is re-taken on the registry sweep whenever
    /// the hanging is at rest, so a moved prop cannot leave the solver working from a frame that no
    /// longer exists.</para>
    /// </summary>
    private sealed class Hanging
    {
        private Renderer _r = null!;
        private bool _plainMesh;
        private Transform _frame = null!;
        private Transform[] _bones = null!;

        private Vector3[] _homeLocalPos = null!;
        private Quaternion[] _homeLocalRot = null!;
        private Vector3[] _homeWorldPos = null!;
        private Quaternion[] _homeWorldRot = null!;
        private float[] _weight = null!;      // 0 at the rail, 1 at the hem

        private Bounds _localBounds;
        private int _thinAxis;
        private Vector3 _pivot;               // world, the top rail
        private Vector3 _normal;              // world unit normal of the sheet

        // THE HOME FRAME, FROZEN AS A MATRIX PAIR, and the reason it is not read live.
        // _frame is very often a transform this class DRIVES — always so for a plain mesh, and for
        // any rigged hanging whose rootBone is one of its own bones. Reading it live inside the
        // reach gate would make the gate chase the object it is deflecting: a hand that pushes a
        // banner away would find it out of reach next frame, the spring would return it, and the
        // pair would oscillate. The gate asks "is the hand where this banner HANGS", which is a
        // question about the authored pose, so it is answered against the authored pose. Re-taken
        // by Anchor, i.e. only while the hanging is at rest.
        private Matrix4x4 _homeToWorld = Matrix4x4.identity;
        private Matrix4x4 _worldToHome = Matrix4x4.identity;

        private Vector3 _offset;
        private Vector3 _velocity;
        private bool _deflected;

        private string _path = "";
        private string _components = "";
        private string _shader = "";
        private string _wind = "";
        private int _queue;
        private int _blendShapes;
        private bool _animatorAbove;
        private float _thickness;
        private float _span;

        internal float Drop { get; private set; }

        internal bool Alive => _r != null && _frame != null;

        internal static Hanging Build(Renderer r, bool plainMesh, Transform frame,
                                      Transform[]? bones, int boneCount, Bounds localBounds,
                                      int thinAxis, int dropAxis, float dropSign, float thickness,
                                      float drop, float span, string path, Material? mat, int queue)
        {
            var h = new Hanging
            {
                _r = r,
                _plainMesh = plainMesh,
                _frame = frame,
                _localBounds = localBounds,
                _thinAxis = thinAxis,
                _thickness = thickness,
                _span = span,
                Drop = Mathf.Max(drop, 1e-3f),
                _path = path,
                _queue = queue,
            };

            // THE DRIVEN SET — the one place the two routes differ, and they differ by one line.
            // A rigged hanging is driven by its bind bones; a plain mesh is driven by its own
            // transform, whose motion its vertices follow directly (bones is null for it, so the
            // fallback below picks the frame, which for a plain mesh IS r.transform). A rig of ONE
            // bone is not a failure case either: rotating that bone about the pivot with full
            // weight IS the whole-object swing, and a SkinnedMeshRenderer ignores its own
            // transform, so for a one-bone rig this is the only write that can move the sheet.
            var driven = new List<Transform>(Mathf.Max(boneCount, 1));
            if (bones != null)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] != null)
                        driven.Add(bones[i]);
                }
            }
            if (driven.Count == 0)
                driven.Add(frame);
            h._bones = driven.ToArray();

            h._homeLocalPos = new Vector3[h._bones.Length];
            h._homeLocalRot = new Quaternion[h._bones.Length];
            h._homeWorldPos = new Vector3[h._bones.Length];
            h._homeWorldRot = new Quaternion[h._bones.Length];
            h._weight = new float[h._bones.Length];
            for (int i = 0; i < h._bones.Length; i++)
            {
                h._homeLocalPos[i] = h._bones[i].localPosition;
                h._homeLocalRot[i] = h._bones[i].localRotation;
            }

            h._blendShapes = 0;
            try
            {
                Mesh? mesh = (r as SkinnedMeshRenderer)?.sharedMesh;
                h._blendShapes = mesh != null ? mesh.blendShapeCount : 0;
            }
            catch
            {
                h._blendShapes = -1;
            }

            h._animatorAbove = r.GetComponentInParent<Animator>(true) != null;
            h._shader = mat != null && mat.shader != null ? mat.shader.name : "(no material)";
            h._wind = WindReport(mat);
            h._components = ComponentReport(r.transform);

            h.Anchor(dropAxis, dropSign);
            return h;
        }

        /// <summary>Take the SOLVE capture: world home poses, the pivot at the top of the drop, the
        /// sheet normal and each bone's depth weight. Called at build and re-taken on the sweep
        /// whenever the hanging is home.</summary>
        private void Anchor(int dropAxis, float dropSign)
        {
            Vector3 axisUnit = dropAxis == 0 ? Vector3.right
                               : dropAxis == 1 ? Vector3.up : Vector3.forward;
            Vector3 up = _frame.TransformDirection(axisUnit * dropSign).normalized;
            if (up.sqrMagnitude < 1e-6f)
                up = Vector3.up;

            Vector3 normalUnit = _thinAxis == 0 ? Vector3.right
                                 : _thinAxis == 1 ? Vector3.up : Vector3.forward;
            _normal = _frame.TransformDirection(normalUnit).normalized;
            if (_normal.sqrMagnitude < 1e-6f)
                _normal = Vector3.forward;

            _homeToWorld = _frame.localToWorldMatrix;
            _worldToHome = _frame.worldToLocalMatrix;

            Vector3 centre = _homeToWorld.MultiplyPoint3x4(_localBounds.center);
            _pivot = centre + up * (Drop * 0.5f);

            float anchorAlong = Vector3.Dot(_pivot, up);
            for (int i = 0; i < _bones.Length; i++)
            {
                Transform b = _bones[i];
                if (b == null)
                    continue;
                _homeWorldPos[i] = b.position;
                _homeWorldRot[i] = b.rotation;
                _weight[i] = _bones.Length == 1
                    ? 1f
                    : Mathf.Clamp01((anchorAlong - Vector3.Dot(b.position, up)) / Drop);
            }
        }

        /// <summary>Re-take the solve capture, but only while the hanging is home. Re-anchoring
        /// mid-swing would bake the swing into the authored pose.</summary>
        internal void ReanchorIfHome()
        {
            if (_deflected || !Alive)
                return;
            int dropAxis = MostVerticalAxis(_frame, out float sign);
            Anchor(dropAxis, sign);
        }

        /// <summary>One hand's contribution to the push target, in world units. Zero when the hand
        /// is null, out of reach, or on the far side of nothing.</summary>
        internal Vector3 Push(VRHand? hand, float reachRealMeters, float maxOffset)
        {
            if (hand == null || !Alive || hand.Rig == null || !hand.Rig.IsComplete)
                return Vector3.zero;

            float scale = Mathf.Max(hand.WorldScale, 1e-4f);
            float reach = reachRealMeters * scale;
            if (reach <= 0f)
                return Vector3.zero;

            // AGAINST THE HOME FRAME, never the live one — see _homeToWorld for why a live read
            // here is an oscillator rather than a gate.
            Vector3 palm = hand.Rig.PalmCenter.position;
            Vector3 localPalm = _worldToHome.MultiplyPoint3x4(palm);
            Vector3 clamped = Vector3.Min(Vector3.Max(localPalm, _localBounds.min),
                                          _localBounds.max);
            float d = Vector3.Distance(palm, _homeToWorld.MultiplyPoint3x4(clamped));
            if (d >= reach)
                return Vector3.zero;

            float t = 1f - d / reach;

            // WHICH WAY. The fabric moves AWAY from the hand, so the push is along the sheet's
            // normal pointing from the palm THROUGH the sheet — the OPPOSITE of the side the palm
            // is on. (Getting this backwards is not a subtle bug: the banner would fold into the
            // hand and read as the fabric grabbing you.)
            float side = localPalm[_thinAxis] - _localBounds.center[_thinAxis];
            float sign = side < 0f ? 1f : -1f;

            // The swipe term: how fast the palm is actually moving THROUGH the sheet, converted to
            // real metres per second by this hand's own rig scale. A hand held still in the fabric
            // still holds it aside; a hand sweeping across sends it.
            float through = Vector3.Dot(hand.PalmVelocity, _normal) / scale;
            float motion = Mathf.Clamp(through / HandMotionFullSpeed, -1f, 1f) * HandMotionShare;

            return _normal * (maxOffset * t * Mathf.Clamp(sign + motion, -1f, 1f));
        }

        /// <summary>One spring step, then write the bones — or restore once and stop writing when
        /// the hanging has come home.</summary>
        internal void Step(Vector3 target, float dt)
        {
            if (!Alive)
                return;

            float settle = Mathf.Max(HandsConfig.SceneryHangingSettleSecondsClamped, 0.05f);
            float omega = 6f / settle;             // ~4 time constants inside the settle time
            Vector3 accel = (target - _offset) * (omega * omega) - _velocity * (2f * omega);
            _velocity += accel * dt;
            _offset += _velocity * dt;

            // BOUNDED, so a hand parked inside a banner cannot accumulate. The target is already
            // capped at the swing dial; these cap the integrator's overshoot and its speed, and
            // they are derived from the same dial rather than from the target — a target of zero
            // (settling) must not licence an unbounded offset.
            float cap = Drop * Mathf.Tan(HandsConfig.SceneryHangingSwingDegreesClamped
                                         * Mathf.Deg2Rad) * 1.5f;
            if (_offset.sqrMagnitude > cap * cap)
                _offset = _offset.normalized * cap;
            float speedCap = cap * 20f;
            if (_velocity.sqrMagnitude > speedCap * speedCap)
                _velocity = _velocity.normalized * speedCap;

            if (target.sqrMagnitude <= 0f
                && _offset.sqrMagnitude < RestEpsilonWu * RestEpsilonWu
                && _velocity.sqrMagnitude < RestEpsilonWu * RestEpsilonWu)
            {
                _offset = Vector3.zero;
                _velocity = Vector3.zero;
                Restore();
                return;
            }

            Apply();
        }

        /// <summary>Rotate every bone about the top rail by the angle its own depth earns. Written
        /// as a WORLD pose per bone from that bone's own home, which is topology-independent and
        /// order-independent: whatever a parent bone does to its children is overwritten when the
        /// children are written from their own homes.</summary>
        private void Apply()
        {
            float swing = _offset.magnitude;
            if (swing <= 0f)
                return;
            Vector3 dir = _offset / swing;
            // cross(dir, up), in that order. A point a metre BELOW the pivot, rotated about this
            // axis by a positive angle, moves along +dir — which is what "the hem swings the way
            // it was pushed" means. cross(up, dir) is the same axis negated and swings the hem
            // backwards through the wall.
            Vector3 axis = Vector3.Cross(dir, Vector3.up);
            if (axis.sqrMagnitude < 1e-8f)
                return;
            axis.Normalize();
            float angle = Mathf.Atan2(swing, Drop) * Mathf.Rad2Deg;

            for (int i = 0; i < _bones.Length; i++)
            {
                Transform b = _bones[i];
                if (b == null)
                    continue;
                float w = _weight[i];
                if (w <= 0f)
                    continue;
                Quaternion rot = Quaternion.AngleAxis(angle * w, axis);
                b.SetPositionAndRotation(_pivot + rot * (_homeWorldPos[i] - _pivot),
                                         rot * _homeWorldRot[i]);
            }
            _deflected = true;
        }

        /// <summary>Back to the authored pose, exactly, from the LOCAL capture. Idempotent and
        /// free when the hanging was never deflected.</summary>
        internal void Restore()
        {
            _offset = Vector3.zero;
            _velocity = Vector3.zero;
            if (!_deflected)
                return;
            _deflected = false;
            for (int i = 0; i < _bones.Length; i++)
            {
                Transform b = _bones[i];
                if (b == null)
                    continue;
                b.localPosition = _homeLocalPos[i];
                b.localRotation = _homeLocalRot[i];
            }
        }

        /// <summary>Everything the report asked for about one hanging, in one clause: what it is,
        /// what drives it, what it is made of, and where it is right now.</summary>
        internal string Describe()
        {
            float swing = _offset.magnitude;
            float angle = Mathf.Atan2(swing, Drop) * Mathf.Rad2Deg;
            string mech = _plainMesh
                ? "WHOLE-OBJECT SWING — a plain mesh's vertices follow its transform, so the "
                  + "transform IS the rig; it turns about its own top edge and nothing else moves "
                  + "with it, which the leaf term guarantees"
                : _bones.Length >= 2
                    ? $"BONE CHAIN, {_bones.Length} bones rotated about the rail by depth"
                    : "SINGLE BONE — the whole sheet turns rigidly about the rail (a "
                      + "SkinnedMeshRenderer ignores its own transform, so this is the only move "
                      + "available to a one-bone rig)";
            // THE FRAME RESIDUAL. Zero (at rest) proves localBounds really is expressed in _frame,
            // which is what every distance gate in this class depends on. Non-zero at rest means
            // the reach is being measured in the wrong place and would be the first thing to fix.
            float residual = -1f;
            try
            {
                if (_r != null && _frame != null)
                    residual = (_frame.TransformPoint(_localBounds.center) - _r.bounds.center)
                        .magnitude;
            }
            catch
            {
                residual = -1f;
            }
            return $"'{(_r != null ? _r.name : "(destroyed)")}' at '{_path}' "
                   + (_plainMesh ? "[mesh] " : "[skinned] ")
                   + $"frame '{(_frame != null ? _frame.name : "(none)")}' residual "
                   + $"{residual:0.###} wu (0 at rest = localBounds read in the right frame) "
                   + $"{_bones.Length} bone(s), {_blendShapes} blend shape(s), "
                   + $"Animator above={_animatorAbove}, shader '{_shader}' queue {_queue}, "
                   + $"wind properties {_wind}, components {_components} | "
                   + $"drop {Drop:0.##} wu, span {_span:0.##} wu, thickness {_thickness:0.###} wu "
                   + $"| MECHANISM: {mech} | now offset {swing:0.###} wu = {angle:0.#} deg, "
                   + $"deflected={_deflected}";
        }

        private static string WindReport(Material? mat)
        {
            if (mat == null)
                return "(no material)";
            var found = new List<string>(2);
            for (int i = 0; i < WindProperties.Length; i++)
            {
                try
                {
                    if (mat.HasProperty(WindProperties[i]))
                        found.Add(WindProperties[i]);
                }
                catch
                {
                    // A destroyed material throws; an instrument must never be the thing that
                    // breaks.
                }
            }
            return found.Count == 0 ? "none" : string.Join("+", found);
        }

        /// <summary>The component list on the renderer's own object and on its parent — the field
        /// the report asked for, and the one that would have shown at a glance that a banner has
        /// no Cloth on it.</summary>
        private static string ComponentReport(Transform t)
        {
            var names = new List<string>(8);
            Append(t, names, "self");
            if (t.parent != null)
                Append(t.parent, names, "parent");
            return names.Count == 0 ? "(none)" : string.Join(",", names);
        }

        private static void Append(Transform t, List<string> into, string label)
        {
            Component[] all;
            try
            {
                all = t.GetComponents<Component>();
            }
            catch
            {
                return;
            }
            for (int i = 0; i < all.Length && into.Count < 12; i++)
            {
                if (all[i] != null)
                    into.Add(label + ":" + all[i].GetType().Name);
            }
        }
    }
}
