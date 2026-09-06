using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// WHY A HELD PROP'S OWN ANIMATION RUNS SLOWER IN THE HAND — the A/B instrument, and NOTHING ELSE.
///
/// <para><b>THE REPORT (2026-09-03), verbatim.</b> <i>"Die Truhe spielt so eine 'aufblitz-animation'
/// ab, damit der Spieler sie besser sieht. Das ist vom Spiel selber. Ich will das diese Animation
/// auch normal weiter abspielt wenn man die Truhe in der Hand hat. Aktuell ist die Animation dort
/// etwas kaputt - deutlich langsamer und kommt mir auch nicht so flüssig vor."</i></para>
///
/// <para><b>THIS CLASS SHIPS NO REMEDY, DELIBERATELY.</b> The project's standing lesson is that a
/// fix gated behind the instrument shipped to test it never executes, so "no improvement" carries
/// zero information. Nothing here writes anything the game reads: it reads fields, accumulates
/// numbers and prints one line. If the next hardware round says the numbers are identical in both
/// windows, that is an ANSWER — it kills every candidate below at once and moves the question to
/// the one term this cannot see (named at the end of the verdict).</para>
///
/// <para><b>WHAT IS ALREADY KNOWN, so the round is not spent re-deriving it.</b></para>
/// <list type="bullet">
///   <item>The chest is the ONLY prop with an Animator. The ModBuild 356 hardware log's own
///   census says so: <c>MINIATURE AUDIT 8: ''Chest' Chest' (… 1 Animator(s) with a controller
///   under the actor)</c>, against <c>0 Animator(s)</c> for <c>'OneHexObstacle' Obstacle</c> and
///   <c>'GoldPile' MoneyToken</c>. So the flash is Animator-driven and the Animator terms below
///   are the ones that decide it.</item>
///   <item>The flash does NOT add a renderer while the prop is held. The same log's hold watch
///   read <c>present=4..4 renderer(s)</c> and <c>rebuilt=0</c> across 560 frames, so nothing
///   appeared under the chest for the whole hold. That matters because
///   <c>SpawnObjectAnimateMaterial_SMB.OnStateEnter</c> does
///   <c>Object.Destroy(Object.Instantiate(particles, animator.transform), lifetime)</c> — a
///   particle prefab parented under the prop, which IS a renderer. Either that state was never
///   entered during the hold, or that SMB is not what plays this flash. Hence
///   <see cref="_smb"/>: the watch reads the SMB directly rather than inferring it.</item>
///   <item>CHRONOS IS ALMOST CERTAINLY POSITION-BLIND IN THIS GAME. <c>Chronos.Timeline.Update</c>
///   (Timeline.cs:154-197) computes <c>timeScale = clock.timeScale</c> and then multiplies in
///   <c>IAreaClock.TimeScale(this)</c> for every area clock it is inside — and
///   <c>AreaClock.TimeScale</c> (AreaClock.cs:154-176) evaluates a curve over
///   <c>timeline.gameObject.transform.position</c> in the <c>PointToEdge</c> and
///   <c>DistanceFromEntry</c> modes. That is the only position-dependent term in the whole
///   framework, and moving a chest from its hex into a palm is the largest position change it
///   could get. BUT: no game code anywhere references <c>Chronos.Timeline</c>,
///   <c>AreaClock</c>, <c>AreaClock2D/3D</c>, <c>LocalClock</c> or <c>GlobalClock</c> as types —
///   105 files <c>using Chronos</c> and every one of them touches only
///   <c>Timekeeper.instance.m_GlobalClock</c>. Code cannot prove absence (both are
///   <c>[AddComponentMenu]</c> components and could be attached in scene data), so
///   <see cref="CensusScene"/> asks the runtime ONCE per session and settles it for good.</item>
///   <item>The two prop SMBs latch <c>animator.speed = Timekeeper.instance.m_GlobalClock.timeScale</c>
///   ONCE, in <c>OnStateEnter</c>, and never refresh it
///   (SpawnObjectAnimateMaterial_SMB.cs:24, DelayedDeactivatePropAnimSMB.cs:58). The global clock
///   is <c>0.25</c> during slow-mo (<c>TimeManager.SlowMoTimeScale</c>) and <c>0</c> while frozen.
///   So a state ENTERED inside a slow-mo window keeps a 4x-slow animator for the whole state, with
///   no position involved at all. That is why the animator SPEED and the global-clock terms are
///   sampled in BOTH windows as min/max ranges and not merely once at the grab.</item>
/// </list>
///
/// <para><b>THE TWO WINDOWS, AND WHY THEY ARE HOLD AND POST-LANDING.</b> A rate cannot be read off
/// one sample, so both halves of the comparison need frames. HAND is the hold itself. HOME is a
/// window that opens <see cref="HomeSettleFrames"/> frames AFTER the release glide lands — the same
/// prop, on its own hex, seconds later, with the Apparance thaw (<c>GrabbableProp.ThawDelayFrames</c>
/// = 3) already consumed. A hover window before the grab was considered and dropped: it is not
/// guaranteed to exist, it is not guaranteed to contain a flash, and while it lasts
/// <c>FigureHighlight</c> has parented glow clones under the very subtree this walks. Post-landing
/// has none of those problems and is a genuine home-cell measurement of the identical object.</para>
///
/// <para><b>EVERY TERM IS PRINTED FOR BOTH WINDOWS, INCLUDING THE ONES THAT DID NOT MOVE.</b> A
/// term that reads the same at the hex and in the palm is not the cause, and saying so in the line
/// is worth as much as finding the one that changed — it is what stops the next round re-testing
/// a candidate this round already killed.</para>
///
/// <para><b>COST.</b> One <c>GetComponentsInChildren</c> into a shared scratch list per frame while
/// a window is open, over a prop subtree of under two dozen nodes, for at most ONE prop at a time
/// and at most <see cref="Budget"/> verdicts per session — after which nothing arms and
/// <see cref="Tick"/> returns on its first field test. No allocation per frame. The scene census
/// in <see cref="CensusScene"/> is ONE <c>Resources.FindObjectsOfTypeAll</c> pair, once per
/// session, on the first arm; it is a scene sweep and it is named as one, and it buys the single
/// measurement that kills or confirms the Chronos candidate for every future round.</para>
///
/// <para><b>MULTIPLAYER.</b> Read-only and local: a diagnostic that samples this client's own
/// components and prints. Nothing goes on the wire and no peer behaviour changes.</para>
///
/// <para>=====================================================================================</para>
///
/// <para><b>ROUND 2 (2026-09-05) — WHAT THE ModBuild 435 LOG ACTUALLY SAID, AND THE FOUR BLIND
/// SPOTS IT EXPOSED.</b> The claim that this instrument printed nothing is FALSE: it printed
/// exactly one verdict, <c>[Props] HELD-PROP ANIMATION A/B for 'GoalChest' GoalChest (home window
/// complete): HAND 511 frame(s)/6.25s vs HOME 360 frame(s)/4.48s</c>, and every term in it read
/// the SAME in both windows — <c>clipRate hand=0.000/s home=0.000/s</c>,
/// <c>speed hand=1 home=1</c>, <c>globalClock.timeScale hand=1 home=1</c>,
/// <c>cullingMode CullUpdateTransforms</c> both sides, <c>evaluating 511/511</c> and
/// <c>360/360</c>, <c>stateChanges 0</c>, <c>restarts 0</c>, <c>poseStomps=0 of 511</c>,
/// <c>0 AreaClock(s) and 0 Timeline(s)</c> in the whole scene. ONE term differed:
/// <c>lossyScale.x hand=1..3.648 home=1</c>.</para>
///
/// <para>That reading kills four candidates permanently and it is worth writing down so no round
/// re-tests them: CHRONOS IS DEAD (0 area clocks in the scene — the position-dependent term does
/// not exist here), THE SMB LATCH IS DEAD FOR THIS PROP (speed 1 in both windows, clock 1 in both),
/// THE POSE WRITE IS DEAD (0 stomps in 511 frames, so <c>GrabbableProp.ApplyHeldPose</c> is not
/// erasing an animated transform channel), and the flash IS NOT LAYER 0 OF THIS ANIMATOR (its
/// normalizedTime did not move on 509 of 511 hand frames AND 359 of 360 home frames — it was
/// equally still on its own hex).</para>
///
/// <para><b>The last of those is the finding.</b> A window in which the animation is not running
/// AT HOME EITHER cannot answer a question about how the animation runs. The verdict was a
/// flawless measurement that never contained the thing being measured, and its own step (7) then
/// sent the next round at <c>SpawnObjectAnimateMaterial_SMB.animProperty</c> — a property of an
/// SMB the same line had just reported as <c>NONE on this animator's controller</c>. So the
/// instrument was blind in four specific ways, and this round fixes each one rather than tuning
/// what it could already see:</para>
/// <list type="number">
///   <item><b>ONE LAYER.</b> It read <c>GetCurrentAnimatorStateInfo(0)</c> and nothing else. An
///   attention flash layered over an idle base is the ordinary way to author exactly this, and it
///   would be invisible to every term above. Now every layer up to <see cref="LayerCap"/> is
///   sampled, and the line prints the animator's <c>layerCount</c> so a truncation is visible
///   rather than silent.</item>
///   <item><b>NO PICTURE.</b> It measured the DRIVERS (animator, clock, SMB) and never the
///   RESULT. A flash is a value on a material; if no driver term moves, the question "was the
///   flash even playing?" was unanswerable. Now the prop's own materials are read back:
///   <see cref="MatPropCap"/> float/colour properties off the shader's own property table, per
///   frame, in both windows, reported as the rate each one MOVED at. That is the ground truth the
///   whole report is about, and it does not care which mechanism drives it.</item>
///   <item><b>NO VISIBILITY.</b> It read <c>cullingMode</c> and never <c>Renderer.isVisible</c> —
///   the other half of what <c>cullingMode</c> means. <c>CullUpdateTransforms</c> withholds the
///   transform write on frames when NO renderer of that animator is visible, and a
///   <c>SkinnedMeshRenderer</c> with <c>updateWhenOffscreen=false</c> is culled against bounds
///   carried by its ROOT BONE, which a reparent into a palm at 3.648x is exactly the change those
///   bounds need not follow. Now visibility, enabled-ness, object activity and the drift between
///   <c>renderer.bounds.center</c> and the renderer's own transform are all sampled per frame.
///   <see cref="PropAnimBelt"/> shipped the remedy for that mechanism ungated, and these terms
///   are what said whether it was load-bearing. THEY SAID IT WAS NOT — see ROUND 3 below.</item>
///   <item><b>A GATE ONLY A CHEST COULD OPEN, SPENT BY AN OBSTACLE.</b> <see cref="NotifyGrab"/>
///   refused any prop with no <c>Animator</c>, so a TRAP with none could never arm — and the user
///   named traps. Worse, the budget was global: the ModBuild 435 session grabbed obstacles a dozen
///   times before a chest, and two of those would have spent the session's whole budget on props
///   that answer nothing. The budget is now per prop KIND (<see cref="KindBudget"/> verdict each,
///   <see cref="Budget"/> in total) and arming needs only a RENDERER, so a session in which he
///   holds a chest cannot come back without this line.</item>
/// </list>
///
/// <para><b>TWO SYMPTOMS, TWO VERDICTS.</b> "Viel langsamer" and "ploppt mitten drin einfach weg"
/// are different defects and were being folded together. The line now ends with a
/// <c>RATE VERDICT</c> (did the flash advance at the same rate per REAL second in the hand as on
/// the hex) and a separate <c>STOP VERDICT</c> (did it ever freeze mid-flash, for how many frames,
/// and was the object switched off underneath it) — each with the frame counts behind it.</para>
///
/// <para>=====================================================================================</para>
///
/// <para><b>ROUND 3 (2026-09-06) — READ THIS BEFORE ARMING THIS INSTRUMENT AGAIN. FOUR ROUNDS HAVE
/// MEASURED A WINDOW THAT NEVER CONTAINED THE FLASH.</b> Round 2's own fixes worked: every layer
/// was sampled, the materials were read back, visibility and bounds were measured, and the gate
/// widened so a trap could arm. The verdict it then produced on hardware — on BOTH machines of a
/// two-player session, anchored <c>[Props] HELD-PROP ANIMATION A/B for 'Chest' Chest</c> — is that
/// <b>nothing was animating in EITHER window</b>: <c>clipRate hand=0.000/s home=0.000/s</c>,
/// <c>anyLayerRate 0.000/s</c> on every layer, <c>advancing 0/665</c> and <c>0/360</c>,
/// <c>stateChanges 0</c>, and NOT ONE of the forty tracked material property slots moving on
/// either side. The instrument said so itself, in its own RATE VERDICT: <i>"this window did not
/// contain the thing the report is about."</i></para>
///
/// <para><b>So the flash the user sees is NOT this animator and NOT these material properties, and
/// no fifth round should spend itself finding that out again.</b> The remedy round 2 shipped
/// (<see cref="PropAnimBelt"/>) was confirmed to APPLY — <c>cullingMode hand=AlwaysAnimate
/// home=CullUpdateTransforms</c>, <c>updateWhenOffscreen hand=3/3 home=0/3</c> — and to change
/// nothing, and the one term its argument rested on read IDENTICALLY on both sides
/// (<c>worst bounds-vs-transform gap hand=0.237 wu home=0.237 wu</c>), so the reparent never left
/// the culling bounds behind in the first place.</para>
///
/// <para><b>WHERE THE ANSWER CANNOT BE, BY CONSTRUCTION.</b> This instrument reads animator state
/// and a material property table. Three whole mechanism classes are invisible to it and one of
/// them is now the leading candidate: (1) a SCREEN-SPACE POST-EFFECT — <c>EPOOutline</c> draws by
/// walking a static list in a post pass and writes no material property and no animator state, and
/// the game raises exactly such an outline on a hovered board object through
/// <c>WorldspaceUITools.EnableHoveredOutline</c>; (2) a <c>MaterialPropertyBlock</c> write, which
/// shows up in neither <c>material</c> nor <c>sharedMaterial</c>; (3) a write through
/// <c>.material</c>, which instantiates a per-renderer CLONE this class's <c>sharedMaterial</c>
/// read-back never looks at — and both <c>SpawnObjectAnimateMaterial_SMB</c> and <c>PosToMat</c>
/// write that way. The full record, the candidate list and what distinguishes each candidate are
/// in <c>.planning/held-prop-flash-experiments.md</c>.</para>
///
/// <para><b>THIS CLASS IS STILL USEFUL, BUT ONLY FOR A HOLD THAT VISIBLY FLASHES.</b> A verdict
/// from a hold in which nothing flashed is not evidence about the hand. The round's new evidence
/// comes instead from <see cref="PropAnimBelt"/>'s grab-edge census
/// (<c>[Props] HELD-PROP ANIMATION HUSH</c>), which ENUMERATES what a held prop actually carries —
/// including the <c>Outlinable</c> count nobody has ever measured, and the distinct MonoBehaviour
/// type histogram in which the driver's name will be if it is a component at all.</para>
/// </summary>
internal static class PropAnimWatch
{
    /// <summary>How many verdicts this session prints IN TOTAL, across all prop kinds. Raised from
    /// two on 2026-09-05: the budget is now spent per KIND (see <see cref="KindBudget"/>), and two
    /// slots could not cover a session that holds an obstacle, a money token, a chest and a
    /// trap — which is exactly the session the report describes.</summary>
    private const int Budget = 4;

    /// <summary>Verdicts per prop KIND. One is the whole point: the ModBuild 435 session grabbed
    /// <c>'OneHexObstacle' Obstacle</c> a dozen times before it ever touched the chest, and a
    /// GLOBAL budget of two would have been spent on obstacles long before the prop the report is
    /// about entered a hand. A per-kind budget makes "he held a chest and no line appeared"
    /// impossible for any reason except never holding one.</summary>
    private const int KindBudget = 1;

    /// <summary>How many distinct prop kinds the roster remembers. Past this the roster stops
    /// growing and the total <see cref="Budget"/> is the only remaining limit.</summary>
    private const int KindCap = 8;

    /// <summary>Animator layers sampled per animator. ModBuild 435 read layer 0 ONLY and reported
    /// a clip that had not moved in 511 hand frames AND 359 of 360 home frames — i.e. it measured
    /// an idle base layer in both windows and could not have seen a flash layered over it. The
    /// line prints the animator's real <c>layerCount</c> beside this cap so a truncation is
    /// visible rather than silent.</summary>
    private const int LayerCap = 3;

    /// <summary>Renderers sampled for the visibility / bounds census.</summary>
    private const int RendCap = 6;

    /// <summary>Materials whose property table is read back per frame.</summary>
    private const int MatCap = 2;

    /// <summary>Shader properties tracked per material — float, range and colour, taken off the
    /// shader's OWN property table (<c>Shader.GetPropertyCount/GetPropertyNameId/GetPropertyType</c>),
    /// so nothing is guessed and no property that does not exist is ever read.</summary>
    private const int MatPropCap = 20;

    /// <summary>Property slots: one per (material, property) pair.</summary>
    private const int MatSlots = MatCap * MatPropCap;

    /// <summary>How many MOVING properties the line names. The rest are counted, not listed — a
    /// term that never changed is worth one number, and forty dead terms is how a line stops being
    /// read at all.</summary>
    private const int MatListCap = 6;

    /// <summary>A value that has not moved for this many consecutive frames counts as FROZEN.
    /// Six frames is ~67 ms at 90 Hz — long enough that a curve genuinely holding a value for one
    /// or two frames is not called a stop, short enough that a stop is caught while it is still
    /// mid-flash rather than after it.</summary>
    private const int FreezeRunFrames = 6;

    /// <summary>Frames after the release glide lands before the HOME window opens. Past
    /// <c>GrabbableProp.ThawDelayFrames</c> (3), so the Apparance <c>MonitorMovement</c> restore
    /// and the one bounds re-sync it consumes are outside the measurement.</summary>
    private const int HomeSettleFrames = 8;

    /// <summary>Frames the HOME window stays open — about four seconds at 90 Hz. Generous on
    /// purpose: the flash is periodic, and a window too short to contain one would report an
    /// honest zero rate that reads exactly like a stopped animation.</summary>
    private const int HomeWindowFrames = 360;

    /// <summary>Frames between re-resolutions of the component set. The set is re-read rather than
    /// trusted because a prop's generated content can be destroyed and re-instantiated under us —
    /// that is the whole of ModBuild 349 — and a watch holding dead references would report a
    /// stopped animation for a prop that is animating fine.</summary>
    private const int RescanFrames = 30;

    /// <summary>How many animators / SMBs / timelines are reported. A prop has one animator; the
    /// cap is here so a pathological prefab cannot turn one log line into forty.</summary>
    private const int ReportCap = 3;

    private enum Phase
    {
        Idle,
        Hand,
        HomePending,
        Home,
    }

    // ---- the scratch lists, shared and cleared per use (no per-frame allocation) ---------------
    private static readonly List<Animator> AnimScratch = new(4);
    private static readonly List<ParticleSystem> ParticleScratch = new(8);
    private static readonly List<Chronos.Timeline> TimelineScratch = new(4);
    private static readonly List<Renderer> RendScratch = new(16);
    private static readonly List<MonoBehaviour> BehaviourScratch = new(16);

    private static int _left = Budget;

    /// <summary>Prop kinds that have already spent their <see cref="KindBudget"/>. Held as the
    /// LABEL the verdict prints, so the roster and the line agree by construction.</summary>
    private static readonly List<string> KindsDone = new(KindCap);

    private static Phase _phase = Phase.Idle;
    private static GameObject? _target;
    private static string _label = string.Empty;
    private static int _phaseFrame;

    // --- the resolved component set, re-read every RescanFrames.
    private static Animator[] _animators = System.Array.Empty<Animator>();
    private static SpawnObjectAnimateMaterial_SMB[] _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
    private static Chronos.Timeline[] _timelines = System.Array.Empty<Chronos.Timeline>();
    private static int _nextRescan;

    // --- ROUND 2: the picture side of the prop, and the POPULATION each capped list came from.
    //     A truncated list is not absence — the verdict prints found-vs-sampled for every one.
    private static Renderer[] _renderers = System.Array.Empty<Renderer>();
    private static readonly Material[] Mats = new Material[MatCap];
    private static int _matCount;
    private static readonly int[] MatPropId = new int[MatPropCap];
    private static readonly string[] MatPropName = new string[MatPropCap];
    private static readonly bool[] MatPropIsColor = new bool[MatPropCap];
    private static int _matPropCount;
    private static string _matShader = "<none>";
    private static int _foundAnimators, _foundRenderers, _foundSkins, _foundMats, _foundProps;
    private static int _modOwned;

    // --- THE PRE-BELT SNAPSHOT, taken in NotifyGrab and therefore BEFORE PropAnimBelt.Engage runs
    //     (GrabbableProp.OnGrab calls them in that order). It exists because THE REMEDY ERASES ITS
    //     OWN EVIDENCE: once the belt has written AlwaysAnimate and updateWhenOffscreen=true, the
    //     hand window's visibility and bounds terms are the belt's output and can no longer say
    //     whether the mechanism it removes was ever live. These five numbers are the state the
    //     prop arrived in, one frame after it entered the hand, with nothing of ours written yet.
    private static int _preRenderers, _preVisible, _preUwoFalse, _preAnimatorsOn, _preAnimators;
    private static float _preDrift;

    // --- ROUND 2: the WORLD-ANCHOR feeders, by type. These are the components that push the
    //     object's world position into a shader uniform and then stop (ZephyrAnim.cs:41-55,
    //     ObjectPosToMaterial.cs:15-25) — PropAnimBelt re-runs them while the prop is off its hex,
    //     and this counts them so a log with 0 of each says that strand made no writes at all.
    private static int _zephyrAnims, _objPosToMats, _posToMats, _customObjPos;

    // --- the SCENE census, taken once per session on the first arm (see CensusScene).
    private static bool _sceneCensusDone;
    private static int _sceneAreaClocks = -1;
    private static int _sceneTimelines = -1;

    /// <summary>
    /// One window's worth of accumulated terms. Two of these exist — HAND and HOME — and the whole
    /// verdict is a field-by-field diff of them. Everything is a min/max or a rate rather than a
    /// last-seen value, because the question is about a rate and a snapshot of a rate is a coin
    /// toss (the same mistake the ModBuild 340 one-shot census made about the flicker).
    /// </summary>
    private sealed class Window
    {
        internal int Frames;
        internal float RealSeconds;

        // per-animator (index-aligned with _animators, capped at ReportCap)
        internal readonly float[] SpeedMin = NewFilled(float.MaxValue);
        internal readonly float[] SpeedMax = NewFilled(float.MinValue);
        internal readonly int[] CullModes = new int[ReportCap];   // bitmask over AnimatorCullingMode
        internal readonly int[] UpdateModes = new int[ReportCap]; // bitmask over AnimatorUpdateMode
        internal readonly int[] EnabledFrames = new int[ReportCap];
        internal readonly float[] NormAdvance = new float[ReportCap];  // sum of d(normalizedTime)
        internal readonly float[] NormSeconds = new float[ReportCap];  // real seconds those deltas covered
        internal readonly int[] ZeroFrames = new int[ReportCap];       // frames the clip did not advance at all
        internal readonly int[] StateChanges = new int[ReportCap];
        internal readonly int[] Restarts = new int[ReportCap];         // normalizedTime went BACKWARDS

        // per SpawnObjectAnimateMaterial_SMB — the MATERIAL FLASH term, by name
        internal readonly float[] SmbAdvance = new float[ReportCap];   // sum of d(t)
        internal readonly float[] SmbSeconds = new float[ReportCap];
        internal readonly int[] SmbLiveFrames = new int[ReportCap];    // frames t moved at all

        // per Chronos.Timeline found under the prop — the AreaClock candidate's OUTPUT
        internal readonly float[] TimelineMin = NewFilled(float.MaxValue);
        internal readonly float[] TimelineMax = NewFilled(float.MinValue);

        // global clocks
        internal float ClockMin = float.MaxValue, ClockMax = float.MinValue;
        internal float LocalClockMin = float.MaxValue, LocalClockMax = float.MinValue;
        internal float UnityScaleMin = float.MaxValue, UnityScaleMax = float.MinValue;
        internal int ClockPausedFrames;
        internal int ClockMissingFrames;

        // geometry — proves whether the reparent really preserved the world size
        internal float LossyMin = float.MaxValue, LossyMax = float.MinValue;

        // static census, taken on the window's FIRST sample
        internal bool CensusTaken;
        internal int Particles;
        internal int ParticleScalingLocal;
        internal int ParticleSimWorld;
        internal int ParticlesPlaying;

        // HAND only: frames on which someone else had rewritten the local TRS since our last write
        internal int PoseStomps;

        // ---- ROUND 2 (2026-09-05) ------------------------------------------------------------
        // EVERY LAYER, not just layer 0. Index-aligned with _animators.
        internal readonly int[] LayerCounts = new int[ReportCap];
        internal readonly float[] AnyAdvance = new float[ReportCap];  // Σ over layers of d(normalizedTime)
        internal readonly float[] AnySeconds = new float[ReportCap];
        internal readonly int[] AnyMoveFrames = new int[ReportCap];   // frames SOME layer advanced
        internal readonly int[] LongestStall = new int[ReportCap];    // longest run with no layer advancing
        internal readonly int[] CurStall = new int[ReportCap];

        // THE PICTURE, not the driver: is the thing on screen at all, and does Unity think so?
        internal int RendFrames;
        internal int VisMin = int.MaxValue, VisMax = int.MinValue;     // renderers with isVisible
        internal int VisZeroFrames;                                    // frames with NOT ONE visible
        internal int LongestVisZeroRun, CurVisZeroRun;
        internal int DrawMin = int.MaxValue, DrawMax = int.MinValue;   // enabled AND activeInHierarchy
        internal int OffFrames;                                        // frames some tracked renderer was off
        internal int LongestOffRun, CurOffRun;
        /// <summary>The largest gap seen between a renderer's culling bounds centre and its own
        /// transform. A SkinnedMeshRenderer with updateWhenOffscreen=false carries authored bounds
        /// on its ROOT BONE; if this reads centimetres at home and METRES in the hand, the bounds
        /// were left behind by the reparent and Unity's visibility answer is about the hex.</summary>
        internal float BoundsDriftMax;
        internal bool SkinCensusTaken;
        internal int SkinCount, SkinUwoTrue;   // census, taken on the window's FIRST sample

        // THE FLASH ITSELF: every float/range/colour property of the prop's own materials.
        internal readonly float[] MatAbs = new float[MatSlots];        // Σ|Δv|
        internal readonly float[] MatSecs = new float[MatSlots];
        internal readonly int[] MatMoveFrames = new int[MatSlots];
        internal readonly float[] MatMin = NewFilledN(float.MaxValue);
        internal readonly float[] MatMax = NewFilledN(float.MinValue);
        internal readonly int[] MatReversals = new int[MatSlots];      // Δ changed sign — one blink is two
        internal readonly int[] MatMidFreezeFrames = new int[MatSlots];
        internal readonly int[] MatLongestMidFreeze = new int[MatSlots];
        internal readonly int[] MatCurFreeze = new int[MatSlots];

        private static float[] NewFilled(float v)
        {
            var a = new float[ReportCap];
            for (int i = 0; i < a.Length; i++)
                a[i] = v;
            return a;
        }

        private static float[] NewFilledN(float v)
        {
            var a = new float[MatSlots];
            for (int i = 0; i < a.Length; i++)
                a[i] = v;
            return a;
        }
    }

    private static readonly Window Hand = new();
    private static readonly Window Home = new();

    // --- per-animator carry state across frames (NOT per window: a rate needs the previous sample
    //     whichever window it fell in, and a window boundary simply drops one delta).
    private static readonly int[] PrevStateHash = new int[ReportCap];
    private static readonly float[] PrevNormTime = new float[ReportCap];
    private static readonly bool[] PrevValid = new bool[ReportCap];
    private static readonly float[] PrevSmbT = new float[ReportCap];
    private static readonly bool[] PrevSmbValid = new bool[ReportCap];

    // --- ROUND 2 carry: per (animator, layer) and per (material, property).
    private static readonly int[] PrevLayerHash = new int[ReportCap * LayerCap];
    private static readonly float[] PrevLayerNt = new float[ReportCap * LayerCap];
    private static readonly bool[] PrevLayerValid = new bool[ReportCap * LayerCap];
    private static readonly float[] PrevMatV = new float[MatSlots];
    private static readonly bool[] PrevMatValid = new bool[MatSlots];
    private static readonly sbyte[] PrevMatSign = new sbyte[MatSlots];

    /// <summary>True while the HAND window is open — read by <c>GrabbableProp.ApplyHeldPose</c> so
    /// the pose-stomp comparison costs nothing on a hold nobody is watching.</summary>
    internal static bool WatchingHand => _phase == Phase.Hand;

    /// <summary>
    /// Arm the watch on a prop entering a hand. Ignored when a watch is already running, when the
    /// TOTAL budget is spent, when this prop KIND has already produced its verdict, or when the
    /// prop draws nothing at all.
    ///
    /// <para><b>THE ANIMATOR REQUIREMENT WAS REMOVED ON 2026-09-05, and it was a gate narrower
    /// than its own question.</b> It read "the question is about an ANIMATION, and a gold pile has
    /// none", which was true of the DRIVER it then went on to measure and false of the SYMPTOM: the
    /// user's report names a TRAP as well as a chest, the ModBuild 356 census counted zero
    /// animators on the props it looked at, and the ModBuild 435 verdict then proved the chest's
    /// one animator was idle in both windows anyway. A flash can be a material property with no
    /// animator behind it at all, so the arming test is now the weakest thing that can possibly
    /// carry one: a Renderer. The per-KIND budget is what makes that affordable.</para>
    /// </summary>
    internal static void NotifyGrab(GameObject? visual, string label)
    {
        if (_left <= 0 || visual == null)
            return;

        // RE-GRAB DURING THE RELEASE GLIDE. GrabbableProp.OnGrab lands an in-flight glide first,
        // which runs FinishGlide and therefore NotifyLanded — so by the time this is called the
        // watch has already opened its HOME countdown on a prop that is going straight back into
        // the hand. Without this branch the "home" window would be sampled while the prop rides the
        // palm, and the A/B would compare the hand against itself. Resume the HAND window instead
        // and discard whatever the aborted home window collected.
        if (_phase != Phase.Idle)
        {
            if (!ReferenceEquals(visual, _target))
                return;
            _phase = Phase.Hand;
            _phaseFrame = 0;
            _nextRescan = 0;
            Reset(Home);
            for (int i = 0; i < ReportCap; i++)
            {
                PrevValid[i] = false;
                PrevSmbValid[i] = false;
            }
            ForgetRound2Carry();
            return;
        }

        if (KindSpent(label))
            return;

        RendScratch.Clear();
        visual.GetComponentsInChildren(includeInactive: true, RendScratch);
        if (RendScratch.Count == 0)
            return;   // nothing draws under this prop, so nothing about it can flash

        CensusScene();
        _target = visual;
        _label = label;
        _phase = Phase.Hand;
        _phaseFrame = 0;
        _nextRescan = 0;
        Reset(Hand);
        Reset(Home);
        for (int i = 0; i < ReportCap; i++)
        {
            PrevValid[i] = false;
            PrevSmbValid[i] = false;
        }
        ForgetRound2Carry();
        Resolve();
        CensusPreBelt();
    }

    /// <summary>
    /// What the prop looked like the instant it entered the hand, BEFORE this mod wrote anything to
    /// it. Called from <see cref="NotifyGrab"/>, which <c>GrabbableProp.OnGrab</c> runs one line
    /// ahead of <c>PropAnimBelt.Engage</c>.
    ///
    /// <para><b>WHY IT HAS TO EXIST.</b> The belt's whole job is to make Unity stop culling a held
    /// prop against bounds this mod invalidated — and the moment it does, every visibility term in
    /// the HAND window reads healthy whether or not the defect was ever there. A remedy that erases
    /// the evidence for its own cause leaves a round unable to say if it was load-bearing, which is
    /// the same trap as a fix gated behind its own instrument, arrived at from the other side. So
    /// the state is photographed first: how many skinned renderers shipped with
    /// <c>updateWhenOffscreen=false</c>, how many animators were NOT already
    /// <c>AlwaysAnimate</c>, how many renderers Unity thought were visible, and how far the culling
    /// bounds had already drifted from the transforms carrying them.</para>
    /// </summary>
    private static void CensusPreBelt()
    {
        _preRenderers = _renderers.Length;
        _preVisible = 0;
        _preUwoFalse = 0;
        _preDrift = 0f;
        _preAnimators = 0;
        _preAnimatorsOn = 0;

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null)
                continue;
            if (r.isVisible)
                _preVisible++;
            if (r is SkinnedMeshRenderer smr && smr != null && !smr.updateWhenOffscreen)
                _preUwoFalse++;
            float d = (r.bounds.center - r.transform.position).magnitude;
            if (d > _preDrift)
                _preDrift = d;
        }

        for (int i = 0; i < _animators.Length && i < ReportCap; i++)
        {
            Animator a = _animators[i];
            if (a == null)
                continue;
            _preAnimators++;
            // ROUND 3: the field the remedy now writes is Animator.enabled, so THAT is the
            // one worth photographing before it is written. cullingMode is no longer touched by
            // PropAnimBelt at all (Unity does not evaluate a disabled animator, so it decides
            // nothing) — it is still sampled per frame in both windows further down, as a
            // measurement, but it is no longer a precondition of anything this mod does.
            if (a.enabled)
                _preAnimatorsOn++;
        }
    }

    /// <summary>The prop has landed back on its hex: start counting down to the HOME window. Called
    /// from <c>GrabbableProp.FinishGlide</c>, which is the one place both release paths converge
    /// on. A landing for a prop this watch is not following is ignored.</summary>
    internal static void NotifyLanded(GameObject? visual)
    {
        if (_phase != Phase.Hand || visual == null || !ReferenceEquals(visual, _target))
            return;
        _phase = Phase.HomePending;
        _phaseFrame = 0;
    }

    /// <summary>The prop died under us (looted, broken, scenario teardown). Emit whatever both
    /// windows hold rather than lose the round's only measurement — an incomplete A/B still names
    /// which terms were sampled and which were not.</summary>
    internal static void NotifyGone(GameObject? visual)
    {
        // NO `visual == null` GUARD, and that is the point: the single caller
        // (GrabbableProp.TickHeld) reaches here precisely BECAUSE the visual is Unity-destroyed, so
        // a null test would swallow every call this method exists for. ReferenceEquals compares the
        // managed references, which survive the engine-side destruction, and answers false for a
        // genuinely null argument against a live target.
        if (_phase == Phase.Idle || !ReferenceEquals(visual, _target))
            return;
        Finish("the prop was DESTROYED before the home window finished");
    }

    /// <summary>One frame. Called from <c>PropGrab.Tick</c> ABOVE the feature gate, so a watch
    /// already running still finishes its home window when the dial is turned off mid-flight.
    /// Returns immediately in the steady state.</summary>
    internal static void Tick()
    {
        if (_phase == Phase.Idle)
            return;
        if (_target == null)
        {
            Finish("the prop was DESTROYED before the home window finished");
            return;
        }

        _phaseFrame++;
        switch (_phase)
        {
            case Phase.Hand:
                Sample(Hand);
                return;

            case Phase.HomePending:
                if (_phaseFrame < HomeSettleFrames)
                    return;
                _phase = Phase.Home;
                _phaseFrame = 0;
                _nextRescan = 0;              // the landing may have rebuilt the subtree
                for (int i = 0; i < ReportCap; i++)
                {
                    PrevValid[i] = false;     // never carry a delta ACROSS the window boundary
                    PrevSmbValid[i] = false;
                }
                ForgetRound2Carry();
                return;

            case Phase.Home:
                Sample(Home);
                if (_phaseFrame >= HomeWindowFrames)
                    Finish("home window complete");
                return;
        }
    }

    /// <summary>
    /// Report that the held pose we were about to write was NOT what the transform held — i.e.
    /// something else wrote the prop's local TRS since our last write. Called from
    /// <c>GrabbableProp.ApplyHeldPose</c>, and it is the term that decides whether the mod's own
    /// per-frame pose re-assert is STOMPING an animation that drives the transform. A flash that
    /// scales or bobs the chest would be erased by that write, and "erased every frame" and
    /// "playing slower" can look the same to an eye.
    /// </summary>
    internal static void NotePoseStomp()
    {
        if (_phase == Phase.Hand)
            Hand.PoseStomps++;
    }

    /// <summary>Has this prop KIND already produced its verdict? The roster is per SCENARIO and
    /// holds the LABEL the line prints, so "which kinds are spent" and "which kinds were reported"
    /// are the same list by construction.</summary>
    private static bool KindSpent(string label)
    {
        for (int i = 0; i < KindsDone.Count; i++)
        {
            if (string.Equals(KindsDone[i], label, System.StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Re-arm for a new scenario (a new scenario is a new hardware question), and drop any
    /// watch mid-flight so a dead prop from the last scenario is never sampled.</summary>
    internal static void Reset()
    {
        _left = Budget;
        KindsDone.Clear();
        _phase = Phase.Idle;
        _target = null;
        _animators = System.Array.Empty<Animator>();
        _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
        _timelines = System.Array.Empty<Chronos.Timeline>();
        _renderers = System.Array.Empty<Renderer>();
        _matCount = 0;
        _matPropCount = 0;
        for (int i = 0; i < MatCap; i++)
            Mats[i] = null!;
    }

    /// <summary>Drop every per-layer and per-material previous sample. A delta must never be
    /// carried ACROSS a window boundary — one frame of the new window would otherwise be charged
    /// with the whole gap since the old one, which on the HOME boundary is several seconds of
    /// glide and settle.</summary>
    private static void ForgetRound2Carry()
    {
        for (int i = 0; i < PrevLayerValid.Length; i++)
            PrevLayerValid[i] = false;
        for (int i = 0; i < PrevMatValid.Length; i++)
        {
            PrevMatValid[i] = false;
            PrevMatSign[i] = 0;
        }
    }

    // ---- the measurement ------------------------------------------------------------------------

    private static void Resolve()
    {
        GameObject? go = _target;
        if (go == null)
            return;

        AnimScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, AnimScratch);
        int n = Mathf.Min(AnimScratch.Count, ReportCap);
        if (_animators.Length != n)
            _animators = new Animator[n];
        for (int i = 0; i < n; i++)
            _animators[i] = AnimScratch[i];

        // The SMBs are instantiated by Unity PER ANIMATOR from the controller asset, so they are
        // reached through the animator and never through GetComponent. Only the first animator's
        // are read: a prop has one, and the array is index-aligned with nothing else.
        _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
        if (n > 0 && _animators[0] != null && _animators[0].runtimeAnimatorController != null)
        {
            try
            {
                SpawnObjectAnimateMaterial_SMB[] found =
                    _animators[0].GetBehaviours<SpawnObjectAnimateMaterial_SMB>();
                if (found != null)
                    _smb = found;
            }
            catch
            {
                // An animator with no controller bound yet throws rather than returning empty.
                _smb = System.Array.Empty<SpawnObjectAnimateMaterial_SMB>();
            }
        }

        TimelineScratch.Clear();
        try
        {
            go.GetComponentsInChildren(includeInactive: true, TimelineScratch);
        }
        catch
        {
            TimelineScratch.Clear();
        }
        int tn = Mathf.Min(TimelineScratch.Count, ReportCap);
        if (_timelines.Length != tn)
            _timelines = new Chronos.Timeline[tn];
        for (int i = 0; i < tn; i++)
            _timelines[i] = TimelineScratch[i];

        _foundAnimators = AnimScratch.Count;
        ResolveRenderers(go);
        ResolveAnchors(go);
    }

    /// <summary>The DRAWABLES and their materials — the side of the prop the eye actually sees.
    /// Renderers are taken in tree order and capped; the population is remembered so the verdict
    /// can print found-vs-sampled instead of a silently truncated list.</summary>
    private static void ResolveRenderers(GameObject go)
    {
        RendScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, RendScratch);

        // DROP THIS MOD'S OWN CLONES. FigureHighlight parents an additive glow clone under the
        // prop's own meshes and PropGhosts clones the subtree; both draw on 'GloomhavenVR/…'
        // shaders. Counting them would put OUR material in the property table and answer a question
        // about the game's flash with a reading of our own overlay.
        _foundRenderers = 0;
        _foundSkins = 0;
        _modOwned = 0;
        for (int i = RendScratch.Count - 1; i >= 0; i--)
        {
            Renderer r = RendScratch[i];
            if (r == null || IsModOwned(r))
            {
                if (r != null)
                    _modOwned++;
                RendScratch.RemoveAt(i);
                continue;
            }
            _foundRenderers++;
            if (r is SkinnedMeshRenderer)
                _foundSkins++;
        }

        int n = Mathf.Min(RendScratch.Count, RendCap);
        if (_renderers.Length != n)
            _renderers = new Renderer[n];
        for (int i = 0; i < n; i++)
            _renderers[i] = RendScratch[i];

        ResolveMaterials();
    }

    /// <summary>
    /// The property table, read ONCE off the shader's own metadata.
    ///
    /// <para><b><c>sharedMaterial</c>, NEVER <c>material</c>.</b> <c>Renderer.material</c>
    /// INSTANTIATES a copy the first time it is touched — a real change to the object, and the
    /// exact thing an instrument may not do. <c>sharedMaterial</c> returns whatever the renderer is
    /// actually drawing with, INCLUDING the instance the game itself created when its own code
    /// touched <c>.material</c>, so it reads the same numbers with no side effect.</para>
    ///
    /// <para>Only materials on the SAME shader as the first one are tracked: the ids come from that
    /// shader's table, and an id that a second shader does not declare would be read as a silent
    /// zero rather than a missing value.</para>
    /// </summary>
    private static void ResolveMaterials()
    {
        _matPropCount = 0;
        _foundMats = 0;
        _foundProps = 0;
        _matShader = "<none>";
        _matCount = 0;
        for (int i = 0; i < MatCap; i++)
            Mats[i] = null!;

        Shader? shader = null;
        for (int i = 0; i < _renderers.Length && _matCount < MatCap; i++)
        {
            Renderer r = _renderers[i];
            if (r == null)
                continue;
            Material m = r.sharedMaterial;
            if (m == null || m.shader == null)
                continue;
            _foundMats++;
            if (shader == null)
            {
                shader = m.shader;
                _matShader = shader.name;
            }
            else if (!ReferenceEquals(m.shader, shader))
            {
                continue;
            }
            Mats[_matCount++] = m;
        }
        if (shader == null)
            return;

        try
        {
            _foundProps = shader.GetPropertyCount();
            for (int i = 0; i < _foundProps && _matPropCount < MatPropCap; i++)
            {
                UnityEngine.Rendering.ShaderPropertyType t = shader.GetPropertyType(i);
                bool isColor = t == UnityEngine.Rendering.ShaderPropertyType.Color;
                if (!isColor
                    && t != UnityEngine.Rendering.ShaderPropertyType.Float
                    && t != UnityEngine.Rendering.ShaderPropertyType.Range)
                    continue;
                MatPropId[_matPropCount] = shader.GetPropertyNameId(i);
                MatPropName[_matPropCount] = shader.GetPropertyName(i);
                MatPropIsColor[_matPropCount] = isColor;
                _matPropCount++;
            }
        }
        catch
        {
            // A shader whose property table cannot be walked is a real state (a stripped or
            // procedurally-built one) and must not throw into a hold.
            _matPropCount = 0;
        }
    }

    /// <summary>Is this renderer one of ours? Every mod-drawn clone under a prop is on a shader
    /// this mod ships, and those all live under the <c>GloomhavenVR/</c> name prefix.</summary>
    private static bool IsModOwned(Renderer r)
    {
        Material m = r.sharedMaterial;
        Shader? sh = m != null ? m.shader : null;
        string? name = sh != null ? sh.name : null;
        return name != null && name.StartsWith("GloomhavenVR/", System.StringComparison.Ordinal);
    }

    /// <summary>Count the game's own WORLD-ANCHOR feeders under the prop. Read-only; the writing
    /// side is <c>PropAnimBelt</c>. Three counts, so a log can say whether the anchor strand had
    /// anything to act on at all.</summary>
    private static void ResolveAnchors(GameObject go)
    {
        _zephyrAnims = 0;
        _objPosToMats = 0;
        _posToMats = 0;
        _customObjPos = 0;
        BehaviourScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, BehaviourScratch);
        for (int i = 0; i < BehaviourScratch.Count; i++)
        {
            MonoBehaviour c = BehaviourScratch[i];
            if (c == null)
                continue;
            if (c is ZephyrAnim)
                _zephyrAnims++;
            else if (c is ObjectPosToMaterial)
                _objPosToMats++;
            else if (c is PosToMat)
                _posToMats++;
            // A GATE NARROWER THAN ITS CHOKE POINT, CLOSED. Until 2026-09-06 this counter named
            // three types while the histogram twenty words later in the same line named
            // CustomObjectPositionToChildMaterials — a per-frame VECTOR writer of the same family
            // — so a prop carrying one read x0 on a line standing next to its own evidence.
            else if (c is CustomObjectPositionToChildMaterials)
                _customObjPos++;
        }
        BehaviourScratch.Clear();
    }

    private static void Sample(Window w)
    {
        GameObject? go = _target;
        if (go == null)
            return;

        if (_phaseFrame >= _nextRescan)
        {
            _nextRescan = _phaseFrame + RescanFrames;
            Resolve();
        }

        float dt = Time.unscaledDeltaTime;
        w.Frames++;
        w.RealSeconds += dt;

        float lossy = go.transform.lossyScale.x;
        if (lossy < w.LossyMin) w.LossyMin = lossy;
        if (lossy > w.LossyMax) w.LossyMax = lossy;

        float unity = Time.timeScale;
        if (unity < w.UnityScaleMin) w.UnityScaleMin = unity;
        if (unity > w.UnityScaleMax) w.UnityScaleMax = unity;

        SampleGlobalClock(w);
        SampleAnimators(w, dt);
        SampleLayers(w, dt);
        SampleSmb(w, dt);
        SampleTimelines(w);
        SampleRenderers(w);
        SampleMaterials(w, dt);

        if (!w.CensusTaken)
        {
            w.CensusTaken = true;
            CensusParticles(go, w);
        }
    }

    private static void SampleGlobalClock(Window w)
    {
        try
        {
            Chronos.Timekeeper keeper = Chronos.Timekeeper.instance;
            Chronos.GlobalClock? clock = keeper != null ? keeper.m_GlobalClock : null;
            if (clock == null)
            {
                w.ClockMissingFrames++;
                return;
            }
            float ts = clock.timeScale, local = clock.localTimeScale;
            if (ts < w.ClockMin) w.ClockMin = ts;
            if (ts > w.ClockMax) w.ClockMax = ts;
            if (local < w.LocalClockMin) w.LocalClockMin = local;
            if (local > w.LocalClockMax) w.LocalClockMax = local;
            if (clock.paused)
                w.ClockPausedFrames++;
        }
        catch
        {
            // Chronos is a game singleton with a lazy FindObjectOfType getter; a scene without a
            // Timekeeper is a real state (the Intro scene has none) and must not throw into a hold.
            w.ClockMissingFrames++;
        }
    }

    private static void SampleAnimators(Window w, float dt)
    {
        for (int i = 0; i < _animators.Length && i < ReportCap; i++)
        {
            Animator a = _animators[i];
            if (a == null)
            {
                PrevValid[i] = false;
                continue;
            }

            float sp = a.speed;
            if (sp < w.SpeedMin[i]) w.SpeedMin[i] = sp;
            if (sp > w.SpeedMax[i]) w.SpeedMax[i] = sp;
            w.CullModes[i] |= 1 << (int)a.cullingMode;
            w.UpdateModes[i] |= 1 << (int)a.updateMode;

            if (!a.isActiveAndEnabled || a.runtimeAnimatorController == null || a.layerCount == 0)
            {
                PrevValid[i] = false;
                continue;
            }
            w.EnabledFrames[i]++;

            AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(0);
            int hash = info.fullPathHash;
            float nt = info.normalizedTime;

            if (!PrevValid[i])
            {
                PrevValid[i] = true;
                PrevStateHash[i] = hash;
                PrevNormTime[i] = nt;
                continue;
            }
            if (hash != PrevStateHash[i])
            {
                w.StateChanges[i]++;
                PrevStateHash[i] = hash;
                PrevNormTime[i] = nt;
                continue;
            }

            float d = nt - PrevNormTime[i];
            PrevNormTime[i] = nt;
            if (d < 0f)
            {
                // Unity does NOT wrap normalizedTime on a looping state (it counts 0,1,2,3…), so a
                // backwards step is a real restart — Animator.Play, a re-entry, or a rewind — and
                // its delta is not a rate sample.
                w.Restarts[i]++;
                continue;
            }
            if (d == 0f)
                w.ZeroFrames[i]++;
            w.NormAdvance[i] += d;
            w.NormSeconds[i] += dt;
        }
    }

    /// <summary>
    /// EVERY LAYER of every sampled animator — the blind spot that cost round 1.
    ///
    /// <para>Layer 0 keeps its own terms above, unchanged, because they are what the existing
    /// verdict tokens name. This adds the union: how much normalizedTime advanced across ALL
    /// layers per real second, how many frames SOME layer moved, and the longest run of frames on
    /// which none did. A flash authored as an additive layer over an idle base is invisible to
    /// layer 0 and obvious here.</para>
    /// </summary>
    private static void SampleLayers(Window w, float dt)
    {
        for (int i = 0; i < _animators.Length && i < ReportCap; i++)
        {
            Animator a = _animators[i];
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null)
            {
                for (int L = 0; L < LayerCap; L++)
                    PrevLayerValid[(i * LayerCap) + L] = false;
                continue;
            }

            int layers = a.layerCount;
            if (layers > w.LayerCounts[i])
                w.LayerCounts[i] = layers;

            bool movedThisFrame = false;
            bool sampledAny = false;
            for (int L = 0; L < layers && L < LayerCap; L++)
            {
                int k = (i * LayerCap) + L;
                AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(L);
                int hash = info.fullPathHash;
                float nt = info.normalizedTime;
                sampledAny = true;
                if (!PrevLayerValid[k])
                {
                    PrevLayerValid[k] = true;
                    PrevLayerHash[k] = hash;
                    PrevLayerNt[k] = nt;
                    continue;
                }
                if (hash != PrevLayerHash[k])
                {
                    PrevLayerHash[k] = hash;
                    PrevLayerNt[k] = nt;
                    movedThisFrame = true;   // a state change IS the animation doing something
                    continue;
                }
                float d = nt - PrevLayerNt[k];
                PrevLayerNt[k] = nt;
                if (d <= 0f)
                    continue;                // a rewind is not a rate sample (see SampleAnimators)
                w.AnyAdvance[i] += d;
                movedThisFrame = true;
            }

            if (!sampledAny)
                continue;
            w.AnySeconds[i] += dt;
            if (movedThisFrame)
            {
                w.AnyMoveFrames[i]++;
                w.CurStall[i] = 0;
            }
            else
            {
                w.CurStall[i]++;
                if (w.CurStall[i] > w.LongestStall[i])
                    w.LongestStall[i] = w.CurStall[i];
            }
        }
    }

    /// <summary>
    /// IS THE PICTURE THERE, AND DOES UNITY THINK SO — the other half of what <c>cullingMode</c>
    /// means, and the term round 1 never read.
    ///
    /// <para><c>Renderer.isVisible</c> is Unity's own culling answer. With
    /// <c>Animator.cullingMode = CullUpdateTransforms</c> — which is what the chest measured in
    /// BOTH windows — a frame on which NO renderer of that animator is visible is a frame on which
    /// the transform write is withheld while the state machine advances. <c>VisZeroFrames</c> is
    /// therefore the count of frames the animation was running and not being drawn, and
    /// <c>LongestVisZeroRun</c> is how long the longest such gap lasted: the "ploppt mitten drin
    /// weg" term, in frames. <c>PropAnimBelt</c> now removes that dependency for a held prop, so a
    /// HAND window that still reads zero-visible frames says the belt did not cover the case.</para>
    ///
    /// <para><c>OffFrames</c> is deliberately separate. A renderer that is <c>enabled=false</c> or
    /// whose object went inactive was switched off by SOMEBODY, and if that somebody is the game
    /// (a trap springing, a chest looted) it is a game decision this mod must report and not
    /// suppress. Culled-but-on and switched-off look identical to an eye and must never be summed
    /// into one number.</para>
    /// </summary>
    private static void SampleRenderers(Window w)
    {
        if (_renderers.Length == 0)
            return;

        int vis = 0, draw = 0, tracked = 0;
        float drift = 0f;
        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null)
                continue;
            tracked++;
            if (r.isVisible)
                vis++;
            if (r.enabled && r.gameObject.activeInHierarchy)
                draw++;
            float d = (r.bounds.center - r.transform.position).magnitude;
            if (d > drift)
                drift = d;
        }
        if (tracked == 0)
            return;

        w.RendFrames++;
        if (vis < w.VisMin) w.VisMin = vis;
        if (vis > w.VisMax) w.VisMax = vis;
        if (draw < w.DrawMin) w.DrawMin = draw;
        if (draw > w.DrawMax) w.DrawMax = draw;
        if (drift > w.BoundsDriftMax) w.BoundsDriftMax = drift;

        if (vis == 0)
        {
            w.VisZeroFrames++;
            w.CurVisZeroRun++;
            if (w.CurVisZeroRun > w.LongestVisZeroRun)
                w.LongestVisZeroRun = w.CurVisZeroRun;
        }
        else
        {
            w.CurVisZeroRun = 0;
        }

        if (draw < tracked)
        {
            w.OffFrames++;
            w.CurOffRun++;
            if (w.CurOffRun > w.LongestOffRun)
                w.LongestOffRun = w.CurOffRun;
        }
        else
        {
            w.CurOffRun = 0;
        }

        if (!w.SkinCensusTaken)
        {
            w.SkinCensusTaken = true;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] is SkinnedMeshRenderer smr && smr != null)
                {
                    w.SkinCount++;
                    if (smr.updateWhenOffscreen)
                        w.SkinUwoTrue++;
                }
            }
        }
    }

    /// <summary>
    /// THE FLASH ITSELF — every float/range/colour property of the prop's own materials, read back
    /// per frame and compared between the two windows.
    ///
    /// <para>This is the measurement round 1 was missing and the one the report is actually about.
    /// It does not care which mechanism drives the value: an animator layer, a state behaviour, a
    /// coroutine or a world-position uniform the game pushed once and never again all show up here
    /// as the same thing — a number that moves, or does not. A colour is reduced to its largest
    /// channel, which for a WHITE flash is the flash.</para>
    ///
    /// <para>Three terms per property, because the report has two halves and one of them needs a
    /// shape rather than a total. <c>MatAbs/MatSecs</c> is the RATE (sum of |dv| per REAL second, so
    /// a value driven at half speed reads half the home number). <c>MatReversals</c> is how many
    /// times the direction of travel changed — one blink up and back is two — which is what
    /// separates "a value that ramps once" from "a value that pulses". <c>MatMidFreezeFrames</c> is
    /// the STOP: frames on which the value did not move AT ALL while sitting away from the bottom
    /// of its own range, i.e. frozen in the middle of a flash rather than resting between two.</para>
    ///
    /// <para>The elevation test uses the window's RUNNING min/max, so the first frames of a window
    /// are judged against an incomplete range. That is stated rather than hidden: it can only
    /// under-report a freeze at the very start of a window, never invent one.</para>
    /// </summary>
    private static void SampleMaterials(Window w, float dt)
    {
        for (int m = 0; m < _matCount; m++)
        {
            Material mat = Mats[m];
            if (mat == null)
            {
                for (int k = 0; k < _matPropCount; k++)
                    PrevMatValid[(m * MatPropCap) + k] = false;
                continue;
            }

            for (int k = 0; k < _matPropCount; k++)
            {
                int slot = (m * MatPropCap) + k;
                float v;
                if (MatPropIsColor[k])
                {
                    Color c = mat.GetColor(MatPropId[k]);
                    v = Mathf.Max(Mathf.Max(c.r, c.g), Mathf.Max(c.b, c.a));
                }
                else
                {
                    v = mat.GetFloat(MatPropId[k]);
                }

                if (v < w.MatMin[slot]) w.MatMin[slot] = v;
                if (v > w.MatMax[slot]) w.MatMax[slot] = v;

                if (!PrevMatValid[slot])
                {
                    PrevMatValid[slot] = true;
                    PrevMatV[slot] = v;
                    PrevMatSign[slot] = 0;
                    continue;
                }

                float d = v - PrevMatV[slot];
                PrevMatV[slot] = v;
                w.MatSecs[slot] += dt;

                if (d == 0f)
                {
                    float span = w.MatMax[slot] - w.MatMin[slot];
                    if (span > 1e-4f && v > w.MatMin[slot] + (0.10f * span))
                    {
                        w.MatCurFreeze[slot]++;
                        if (w.MatCurFreeze[slot] >= FreezeRunFrames)
                        {
                            w.MatMidFreezeFrames[slot]++;
                            if (w.MatCurFreeze[slot] > w.MatLongestMidFreeze[slot])
                                w.MatLongestMidFreeze[slot] = w.MatCurFreeze[slot];
                        }
                    }
                    continue;
                }

                w.MatCurFreeze[slot] = 0;
                w.MatMoveFrames[slot]++;
                w.MatAbs[slot] += d < 0f ? -d : d;
                sbyte sign = (sbyte)(d > 0f ? 1 : -1);
                if (PrevMatSign[slot] != 0 && sign != PrevMatSign[slot])
                    w.MatReversals[slot]++;
                PrevMatSign[slot] = sign;
            }
        }
    }

    private static void SampleSmb(Window w, float dt)
    {
        for (int i = 0; i < _smb.Length && i < ReportCap; i++)
        {
            SpawnObjectAnimateMaterial_SMB s = _smb[i];
            if (s == null)
            {
                PrevSmbValid[i] = false;
                continue;
            }
            float t = s.t;
            if (!PrevSmbValid[i])
            {
                PrevSmbValid[i] = true;
                PrevSmbT[i] = t;
                continue;
            }
            float d = t - PrevSmbT[i];
            PrevSmbT[i] = t;
            if (d < 0f)
                continue;               // OnStateExit resets t to 0 — that is an exit, not a rate
            if (d > 0f)
                w.SmbLiveFrames[i]++;
            w.SmbAdvance[i] += d;
            w.SmbSeconds[i] += dt;
        }
    }

    private static void SampleTimelines(Window w)
    {
        for (int i = 0; i < _timelines.Length && i < ReportCap; i++)
        {
            Chronos.Timeline t = _timelines[i];
            if (t == null)
                continue;
            float ts = t.timeScale;
            if (ts < w.TimelineMin[i]) w.TimelineMin[i] = ts;
            if (ts > w.TimelineMax[i]) w.TimelineMax[i] = ts;
        }
    }

    private static void CensusParticles(GameObject go, Window w)
    {
        ParticleScratch.Clear();
        go.GetComponentsInChildren(includeInactive: true, ParticleScratch);
        for (int i = 0; i < ParticleScratch.Count; i++)
        {
            ParticleSystem p = ParticleScratch[i];
            if (p == null)
                continue;
            w.Particles++;
            ParticleSystem.MainModule main = p.main;
            if (main.scalingMode == ParticleSystemScalingMode.Local)
                w.ParticleScalingLocal++;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                w.ParticleSimWorld++;
            if (p.isPlaying)
                w.ParticlesPlaying++;
        }
    }

    /// <summary>
    /// ONE scene sweep, ONCE per session, on the first arm — and it is named as one.
    ///
    /// <para>This project's standing suspect for a per-frame cost is exactly this call, so it is
    /// worth stating what buys the exception: <c>Chronos.AreaClock</c> is the ONLY position-dependent
    /// term in the game's time framework, no C# anywhere references it, and both it and
    /// <c>Chronos.Timeline</c> are <c>[AddComponentMenu]</c> components that could still be attached
    /// in scene or prefab data that no decompile can see. A count of 0 kills that candidate for
    /// every future round in one number; a count above 0 promotes it to first place. Nothing else
    /// available can answer it, and it runs at most once per session.</para>
    /// </summary>
    private static void CensusScene()
    {
        if (_sceneCensusDone)
            return;
        _sceneCensusDone = true;
        try
        {
            // AreaClock itself is generic (AreaClock<TCollider, TVector>) and cannot be a Type
            // argument here; the two CONCRETE subclasses are the only things a scene can hold, and
            // between them they cover every area clock Chronos can instantiate.
            _sceneAreaClocks = Resources.FindObjectsOfTypeAll(typeof(Chronos.AreaClock3D)).Length
                               + Resources.FindObjectsOfTypeAll(typeof(Chronos.AreaClock2D)).Length;
            _sceneTimelines = Resources.FindObjectsOfTypeAll(typeof(Chronos.Timeline)).Length;
        }
        catch
        {
            _sceneAreaClocks = -1;
            _sceneTimelines = -1;
        }
    }

    // ---- the verdict ----------------------------------------------------------------------------

    /// <summary>
    /// END THE WATCH: disarm first, print second, and NOTHING is written by the printer.
    ///
    /// <para><b>THE SPLIT IS DELIBERATE AND IT IS THE PROJECT'S OWN SCAR.</b> Deleting a spent
    /// <c>Log*</c> method once nearly latched the wall fade off forever, because the method's body
    /// carried a load-bearing write — and <c>scripts/check-instrument-writes.py</c> exists to
    /// notice that shape. A verdict printer that also ran the state machine would be exactly it:
    /// gate the print and the watch never disarms, so the per-frame walk runs for the rest of the
    /// session after the last line it could ever emit. Here the transition happens unconditionally
    /// and first; <see cref="ReportVerdict"/> is a pure function of the two windows.</para>
    /// </summary>
    private static void Finish(string reason)
    {
        _phase = Phase.Idle;
        _target = null;
        if (_left <= 0 || (Hand.Frames == 0 && Home.Frames == 0))
            return;
        _left--;
        if (KindsDone.Count < KindCap && !KindSpent(_label))
            KindsDone.Add(_label);
        ReportVerdict(reason);
    }

    /// <summary>The one line this whole class exists to print — and it prints, full stop. Named
    /// <c>Report*</c> so <c>scripts/check-instrument-writes.py</c> seeds it into the instrument set
    /// (it iterates that set to a fixpoint from the name prefixes), which is what lets the scene
    /// census and the two window accumulators be recognised as diagnostic-only state rather than as
    /// something that can never be switched off.</summary>
    private static void ReportVerdict(string reason)
    {
        var sb = new System.Text.StringBuilder(8192);
        sb.Append("[Props] HELD-PROP ANIMATION A/B for ").Append(_label)
          .Append(" (").Append(reason).Append("): HAND ").Append(Hand.Frames)
          .Append(" frame(s)/").Append(Hand.RealSeconds.ToString("0.00")).Append("s vs HOME ")
          .Append(Home.Frames).Append(" frame(s)/").Append(Home.RealSeconds.ToString("0.00"))
          .Append("s. ");

        if (Home.Frames == 0)
            sb.Append("NO HOME WINDOW WAS SAMPLED, so every figure below is one-sided — read it as "
                      + "a census, not as a comparison. ");

        int animators = Mathf.Min(_animators.Length, ReportCap);
        sb.Append("ANIMATOR(S): ").Append(animators == 0 ? "none" : string.Empty);
        for (int i = 0; i < animators; i++)
        {
            Animator a = _animators[i];
            sb.Append(i > 0 ? " | " : string.Empty)
              .Append('\'').Append(a != null ? a.name : "<destroyed>").Append("' ")
              .Append("clipRate hand=").Append(Rate(Hand.NormAdvance[i], Hand.NormSeconds[i]))
              .Append("/s home=").Append(Rate(Home.NormAdvance[i], Home.NormSeconds[i]))
              .Append("/s; speed hand=").Append(Range(Hand.SpeedMin[i], Hand.SpeedMax[i]))
              .Append(" home=").Append(Range(Home.SpeedMin[i], Home.SpeedMax[i]))
              .Append("; cullingMode hand=").Append(Modes<AnimatorCullingMode>(Hand.CullModes[i]))
              .Append(" home=").Append(Modes<AnimatorCullingMode>(Home.CullModes[i]))
              .Append("; updateMode hand=").Append(Modes<AnimatorUpdateMode>(Hand.UpdateModes[i]))
              .Append(" home=").Append(Modes<AnimatorUpdateMode>(Home.UpdateModes[i]))
              .Append("; evaluating hand=").Append(Hand.EnabledFrames[i]).Append('/').Append(Hand.Frames)
              .Append(" home=").Append(Home.EnabledFrames[i]).Append('/').Append(Home.Frames)
              .Append("; stalled(no advance) hand=").Append(Hand.ZeroFrames[i])
              .Append(" home=").Append(Home.ZeroFrames[i])
              .Append("; stateChanges hand=").Append(Hand.StateChanges[i])
              .Append(" home=").Append(Home.StateChanges[i])
              .Append("; restarts hand=").Append(Hand.Restarts[i])
              .Append(" home=").Append(Home.Restarts[i]);
        }

        sb.Append(". MATERIAL FLASH TERM (SpawnObjectAnimateMaterial_SMB, the state behaviour that "
                  + "drives a named shader float from an AnimationCurve): ");
        int smbs = Mathf.Min(_smb.Length, ReportCap);
        if (smbs == 0)
        {
            sb.Append("NONE on this animator's controller — so this flash is not that SMB, and the "
                      + "clip rate above is the whole story");
        }
        for (int i = 0; i < smbs; i++)
        {
            SpawnObjectAnimateMaterial_SMB s = _smb[i];
            sb.Append(i > 0 ? " | " : string.Empty)
              .Append("animProperty='").Append(s != null ? (s.animProperty ?? "<null>") : "<destroyed>")
              .Append("' animTime=").Append(s != null ? s.animTime.ToString("0.###") : "?")
              .Append(" animStrength=").Append(s != null ? s.animStrength.ToString("0.###") : "?")
              .Append(" particles=").Append(s != null && s.particles != null ? s.particles.name : "<none>")
              .Append("; curveRate hand=").Append(Rate(Hand.SmbAdvance[i], Hand.SmbSeconds[i]))
              .Append("/s home=").Append(Rate(Home.SmbAdvance[i], Home.SmbSeconds[i]))
              .Append("/s; advancing hand=").Append(Hand.SmbLiveFrames[i]).Append('/').Append(Hand.Frames)
              .Append(" home=").Append(Home.SmbLiveFrames[i]).Append('/').Append(Home.Frames);
        }

        sb.Append(". CHRONOS: scene holds ").Append(Count(_sceneAreaClocks)).Append(" AreaClock(s) and ")
          .Append(Count(_sceneTimelines)).Append(" Timeline(s); this prop carries ")
          .Append(Mathf.Min(_timelines.Length, ReportCap)).Append(" Timeline(s)");
        for (int i = 0; i < _timelines.Length && i < ReportCap; i++)
        {
            sb.Append(" [timeScale hand=").Append(Range(Hand.TimelineMin[i], Hand.TimelineMax[i]))
              .Append(" home=").Append(Range(Home.TimelineMin[i], Home.TimelineMax[i])).Append(']');
        }
        sb.Append("; globalClock.timeScale hand=").Append(Range(Hand.ClockMin, Hand.ClockMax))
          .Append(" home=").Append(Range(Home.ClockMin, Home.ClockMax))
          .Append("; localTimeScale hand=").Append(Range(Hand.LocalClockMin, Hand.LocalClockMax))
          .Append(" home=").Append(Range(Home.LocalClockMin, Home.LocalClockMax))
          .Append("; paused frames hand=").Append(Hand.ClockPausedFrames)
          .Append(" home=").Append(Home.ClockPausedFrames)
          .Append("; clock unreachable on hand=").Append(Hand.ClockMissingFrames)
          .Append(" home=").Append(Home.ClockMissingFrames)
          .Append("; Time.timeScale hand=").Append(Range(Hand.UnityScaleMin, Hand.UnityScaleMax))
          .Append(" home=").Append(Range(Home.UnityScaleMin, Home.UnityScaleMax));

        sb.Append(". GEOMETRY AND PARTICLES: lossyScale.x hand=")
          .Append(Range(Hand.LossyMin, Hand.LossyMax)).Append(" home=")
          .Append(Range(Home.LossyMin, Home.LossyMax))
          .Append("; particle system(s) hand=").Append(Hand.Particles).Append(" (")
          .Append(Hand.ParticleScalingLocal).Append(" scalingMode=Local, ")
          .Append(Hand.ParticleSimWorld).Append(" simulationSpace=World, ")
          .Append(Hand.ParticlesPlaying).Append(" playing) home=").Append(Home.Particles).Append(" (")
          .Append(Home.ParticleScalingLocal).Append(" Local, ").Append(Home.ParticleSimWorld)
          .Append(" World, ").Append(Home.ParticlesPlaying).Append(" playing)")
          .Append("; poseStomps=").Append(Hand.PoseStomps).Append(" of ").Append(Hand.Frames)
          .Append(" hand frame(s)");

        // ---- ROUND 2 (2026-09-05): the four terms round 1 could not see -----------------------
        AppendPopulations(sb);
        AppendLayers(sb);
        AppendVisibility(sb);
        AppendAnchors(sb);
        AppendMaterials(sb);

        sb.Append(". READ IT LIKE THIS, IN THIS ORDER. (1) clipRate is the ground truth — "
            + "normalizedTime advanced per REAL second, so a clip that plays at half speed reads "
            + "half the home value and one that plays normally reads the same number in both "
            + "windows. If clipRate is EQUAL, the Animator is not slow and every candidate below "
            + "that acts through the animator is dead. (2) If clipRate fell, read speed next: "
            + "SpawnObjectAnimateMaterial_SMB.OnStateEnter and DelayedDeactivatePropAnimSMB"
            + ".OnStateEnter both latch animator.speed = Timekeeper.instance.m_GlobalClock.timeScale "
            + "ONCE and never refresh it, and TimeManager.SlowMoTimeScale is 0.25 — so a flash state "
            + "ENTERED during a slow-mo window stays 4x slow for the whole state with no position "
            + "involved. globalClock.timeScale differing between the windows confirms that; equal "
            + "clocks with unequal animator.speed means something else writes speed. (3) evaluating "
            + "below the frame count, or stalled climbing, means the animator is being SKIPPED on "
            + "some frames rather than slowed — read cullingMode (CullCompletely stops an animator "
            + "whose renderers no camera sees) and updateMode (AnimatePhysics advances on the 50 Hz "
            + "fixed step, which at 90 Hz looks choppy and is the 'nicht so flüssig' half without "
            + "being the 'langsamer' half). (4) curveRate is the FLASH ITSELF when the SMB exists: "
            + "it is d(t)/dt for t += m_GlobalClock.deltaTime/animTime, accumulated in OnStateUpdate, "
            + "so it falls if and only if the animator is being updated less often or the global "
            + "clock is slower — a curveRate that fell while clipRate held means OnStateUpdate is "
            + "firing on fewer frames. (5) CHRONOS: AreaClock is the ONLY position-dependent term in "
            + "the game's time framework (AreaClock.cs:154-176 evaluates a curve over the Timeline's "
            + "WORLD POSITION), and moving a chest into a palm is the largest position change it "
            + "could get — but no game code references those types at all, so a scene count of 0 "
            + "AreaClock(s) KILLS that candidate permanently and a count above 0 promotes it to "
            + "first place. (6) poseStomps above ~0 means something rewrote the held prop's local "
            + "TRS between our per-frame re-asserts — i.e. the flash animates the TRANSFORM and "
            + "GrabbableProp.ApplyHeldPose is erasing it every frame; that is a different defect "
            + "from a slow clock and needs the pose write to concede the animated channel. "
            + "(7) EVERYTHING EQUAL IN BOTH WINDOWS is itself the answer: it rules out the clock, "
            + "the animator, Chronos and the pose write together, and moves the question to the one "
            + "term this instrument cannot see — the SHADER's own value of animProperty, which the "
            + "SMB writes through Renderer.material (an INSTANCED material) on every renderer it "
            + "finds with GetComponentsInChildren each frame. The next measurement is then that "
            + "float, read back off the prop's materials by the name this line just printed.")
          .Append(" (").Append(_left).Append(" more animation A/B lines this session.)");

        AppendVerdicts(sb);

        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab", sb.ToString());
    }

    /// <summary>Found-vs-sampled for every capped list in this line. A truncated list is not
    /// absence, and round 1 printed three animators without ever saying three was the cap.</summary>
    private static void AppendPopulations(System.Text.StringBuilder sb)
    {
        sb.Append(". POPULATIONS (found under the prop vs sampled here — a capped list is not an "
                  + "absence): animator(s) ").Append(_foundAnimators).Append('/')
          .Append(Mathf.Min(_animators.Length, ReportCap))
          .Append("; renderer(s) ").Append(_foundRenderers).Append('/').Append(_renderers.Length)
          .Append(" of which ").Append(_foundSkins).Append(" skinned, ").Append(_modOwned)
          .Append(" mod-owned clone(s) dropped")
          .Append("; material(s) ").Append(_foundMats).Append('/').Append(_matCount)
          .Append(" on shader '").Append(_matShader).Append('\'')
          .Append("; shader propert(y/ies) ").Append(_foundProps).Append(" declared, ")
          .Append(_matPropCount).Append(" float/range/colour tracked");
    }

    /// <summary>EVERY LAYER, not just layer 0 — and the animator's real <c>layerCount</c> beside
    /// the cap, so a controller with more layers than this samples says so out loud.</summary>
    private static void AppendLayers(System.Text.StringBuilder sb)
    {
        sb.Append(". ALL ANIMATOR LAYERS (round 1 read layer 0 only, and layer 0 was idle in BOTH "
                  + "windows — a flash layered over an idle base would have been invisible to it): ");
        int n = Mathf.Min(_animators.Length, ReportCap);
        if (n == 0)
            sb.Append("no animator under this prop");
        for (int i = 0; i < n; i++)
        {
            Animator a = _animators[i];
            sb.Append(i > 0 ? " | " : string.Empty)
              .Append('\'').Append(a != null ? a.name : "<destroyed>").Append("' layerCount hand=")
              .Append(Hand.LayerCounts[i]).Append(" home=").Append(Home.LayerCounts[i])
              .Append(" (this samples at most ").Append(LayerCap).Append(')')
              .Append("; anyLayerRate hand=").Append(Rate(Hand.AnyAdvance[i], Hand.AnySeconds[i]))
              .Append("/s home=").Append(Rate(Home.AnyAdvance[i], Home.AnySeconds[i]))
              .Append("/s; advancing hand=").Append(Hand.AnyMoveFrames[i]).Append('/').Append(Hand.Frames)
              .Append(" home=").Append(Home.AnyMoveFrames[i]).Append('/').Append(Home.Frames)
              .Append("; longest run with NO layer advancing hand=").Append(Hand.LongestStall[i])
              .Append(" frame(s) home=").Append(Home.LongestStall[i]).Append(" frame(s)");
        }
    }

    /// <summary>Unity's own culling answer, and whether anyone switched the prop off. These are the
    /// terms that decide whether <c>PropAnimBelt</c> was load-bearing.</summary>
    private static void AppendVisibility(System.Text.StringBuilder sb)
    {
        sb.Append(". VISIBILITY AND BOUNDS (the other half of what cullingMode means, never read "
                  + "before this build): renderers reporting Renderer.isVisible hand=")
          .Append(IntRange(Hand.VisMin, Hand.VisMax)).Append(" home=")
          .Append(IntRange(Home.VisMin, Home.VisMax))
          .Append(" of ").Append(_renderers.Length).Append(" sampled")
          .Append("; frames with NOT ONE visible hand=").Append(Hand.VisZeroFrames).Append('/')
          .Append(Hand.RendFrames).Append(" home=").Append(Home.VisZeroFrames).Append('/')
          .Append(Home.RendFrames)
          .Append(", longest such run hand=").Append(Hand.LongestVisZeroRun)
          .Append(" frame(s) home=").Append(Home.LongestVisZeroRun).Append(" frame(s)")
          .Append("; renderers actually DRAWING (enabled and object active) hand=")
          .Append(IntRange(Hand.DrawMin, Hand.DrawMax)).Append(" home=")
          .Append(IntRange(Home.DrawMin, Home.DrawMax))
          .Append(", frames with at least one switched OFF hand=").Append(Hand.OffFrames)
          .Append(" home=").Append(Home.OffFrames)
          .Append(", longest such run hand=").Append(Hand.LongestOffRun)
          .Append(" frame(s) home=").Append(Home.LongestOffRun).Append(" frame(s)")
          .Append("; worst gap between a renderer's culling bounds centre and its own transform "
                  + "hand=").Append(Hand.BoundsDriftMax.ToString("0.000")).Append(" wu home=")
          .Append(Home.BoundsDriftMax.ToString("0.000")).Append(" wu")
          .Append("; skinned renderer(s) with updateWhenOffscreen=true hand=").Append(Hand.SkinUwoTrue)
          .Append('/').Append(Hand.SkinCount).Append(" home=").Append(Home.SkinUwoTrue).Append('/')
          .Append(Home.SkinCount)
          .Append(" (PropAnimBelt still sets updateWhenOffscreen for the hold and hands the "
                  + "originals back on landing, so the HAND figures here are what it produced and "
                  + "the HOME figures are the game's own defaults. It NO LONGER writes cullingMode: "
                  + "since 2026-09-06 it SUPPRESSES the held prop's animation instead of keeping it "
                  + "running, so any cullingMode read above is the game's own value)")
          .Append(". AT THE GRAB, BEFORE THE BELT WROTE ANYTHING (the state the prop arrived in — "
                  + "without this the belt would erase the evidence for its own cause): ")
          .Append(_preVisible).Append(" of ").Append(_preRenderers)
          .Append(" renderer(s) reported isVisible (READ IT AS THE PREVIOUS FRAME'S ANSWER — "
                  + "isVisible is last frame's culling result and the prop has only just been "
                  + "reparented, so it describes the hex, not the hand; the two counts after it are "
                  + "immediate and exact), ").Append(_preUwoFalse)
          .Append(" skinned renderer(s) had updateWhenOffscreen=false, ").Append(_preAnimatorsOn)
          .Append(" of ").Append(_preAnimators)
          .Append(" animator(s) were ENABLED, worst bounds-vs-transform gap ")
          .Append(_preDrift.ToString("0.000"))
          .Append(" wu. Those two exact counts are what say whether the SUPPRESSION had anything "
                  + "to do: enabled animators are what PropAnimBelt now switches off for the hold "
                  + "(2026-09-06 — it used to do the opposite; see .planning/"
                  + "held-prop-flash-experiments.md), and a zero there means it changed nothing "
                  + "and cannot be why anything got better OR worse");
    }

    /// <summary>The game's own world-anchor feeders, counted. Zero of each means
    /// <c>PropAnimBelt</c>'s second strand made no writes at all on this prop and cannot be what
    /// changed — which is as much an answer as finding them.</summary>
    private static void AppendAnchors(System.Text.StringBuilder sb)
    {
        sb.Append(". WORLD ANCHOR FEEDERS under this prop: ").Append(_zephyrAnims)
          .Append(" ZephyrAnim (pushes the object's WORLD POSITION into a shader uniform, but only "
                  + "from Update while its serialized moveUpdate bool is set — ZephyrAnim.cs:41-55), ")
          .Append(_objPosToMats)
          .Append(" ObjectPosToMaterial (pushes it in OnEnable and NEVER again — "
                  + "ObjectPosToMaterial.cs:15-25), ")
          .Append(_posToMats)
          .Append(" PosToMat (pushes _ObjPosY every frame and needs no help — PosToMat.cs:5-11), ")
          .Append(_customObjPos)
          .Append(" CustomObjectPositionToChildMaterials (pushes the VECTOR _FadeSourcePos into "
                  + "EVERY child material EVERY frame from a moving actor's world position — "
                  + "CustomObjectPositionToChildMaterials.cs:70-100; named here from 2026-09-06 "
                  + "because this counter listed three types while the MonoBehaviour histogram in "
                  + "this same line listed a fourth of the same family, and a gate narrower than "
                  + "its choke point is how that survived six rounds). "
                  + "The flat game never moved a prop off its hex, so 'written once at spawn' was "
                  + "always true there; this mod moves it into a palm, and a shader term anchored at "
                  + "a uniform still naming the HEX would arrive late and end early on a mesh a "
                  + "metre away. PropAnimBelt USED TO re-run the first two by toggling their own "
                  + "enabled flag while the prop was off its hex; that strand was REMOVED on "
                  + "2026-09-06 when the remedy was inverted into a suppression, because its whole "
                  + "purpose was to make a sweep look right in the hand and there is no longer a "
                  + "sweep to make look right. The counts above are therefore a pure census now. "
                  + "PosToMat and CustomObjectPositionToChildMaterials are the two worth reading: "
                  + "a non-zero count on either is a live per-frame material writer on a held prop, "
                  + "and PropAnimBelt's post-hush verdict now tracks VECTOR and TEXTURE properties "
                  + "so such a writer shows up there as a mover instead of as 'nothing moved'");
    }

    /// <summary>
    /// THE FLASH ITSELF: every tracked material property that MOVED in either window, with its rate
    /// on each side, how often it turned round, and how long it sat frozen mid-flash. Properties
    /// that never moved are counted rather than listed — a dead term is worth one number, not a
    /// line.
    /// </summary>
    private static void AppendMaterials(System.Text.StringBuilder sb)
    {
        sb.Append(". MATERIAL PROPERTIES READ BACK OFF THE PROP (this is the flash itself, whatever "
                  + "drives it — the measurement round 1 did not have): ");
        if (_matPropCount == 0 || _matCount == 0)
        {
            sb.Append("no material property table could be read on this prop");
            return;
        }

        int listed = 0, moved = 0;
        for (int m = 0; m < _matCount; m++)
        {
            for (int k = 0; k < _matPropCount; k++)
            {
                int slot = (m * MatPropCap) + k;
                if (Hand.MatMoveFrames[slot] == 0 && Home.MatMoveFrames[slot] == 0)
                    continue;
                moved++;
                if (listed >= MatListCap)
                    continue;
                sb.Append(listed > 0 ? " | " : string.Empty)
                  .Append("mat").Append(m).Append('.').Append(MatPropName[k] ?? "<unnamed>")
                  .Append(" rate hand=").Append(Rate(Hand.MatAbs[slot], Hand.MatSecs[slot]))
                  .Append("/s home=").Append(Rate(Home.MatAbs[slot], Home.MatSecs[slot]))
                  .Append("/s; moving hand=").Append(Hand.MatMoveFrames[slot]).Append('/').Append(Hand.Frames)
                  .Append(" home=").Append(Home.MatMoveFrames[slot]).Append('/').Append(Home.Frames)
                  .Append("; reversals hand=").Append(Hand.MatReversals[slot])
                  .Append(" home=").Append(Home.MatReversals[slot])
                  .Append("; range hand=").Append(Range(Hand.MatMin[slot], Hand.MatMax[slot]))
                  .Append(" home=").Append(Range(Home.MatMin[slot], Home.MatMax[slot]))
                  .Append("; frozen MID-FLASH hand=").Append(Hand.MatMidFreezeFrames[slot])
                  .Append(" frame(s) (longest run ").Append(Hand.MatLongestMidFreeze[slot])
                  .Append(") home=").Append(Home.MatMidFreezeFrames[slot])
                  .Append(" frame(s) (longest run ").Append(Home.MatLongestMidFreeze[slot]).Append(')');
                listed++;
            }
        }

        if (moved == 0)
            sb.Append("NOT ONE of the ").Append(_matPropCount * _matCount)
              .Append(" tracked property slot(s) changed value in EITHER window — so no flash was "
                      + "playing on this prop's own materials while it was held AND none was playing "
                      + "while it stood on its hex, and this window did not contain the thing the "
                      + "report is about");
        else
            sb.Append(". ").Append(moved).Append(" propert(y/ies) moved, ").Append(listed)
              .Append(" listed; the rest never changed value in either window");
    }

    /// <summary>
    /// THE TWO VERDICTS, SEPARATELY — because "viel langsamer" and "ploppt mitten drin einfach weg"
    /// are two defects and folding them into one number is what made round 1 unreadable.
    ///
    /// <para>RATE compares the fastest-moving material property's Σ|Δv| per REAL second in the hand
    /// against the same property on the hex, and falls back to the all-layer animator rate when no
    /// material property moved at all. STOP names every way the picture could have stopped while
    /// the driver ran: a value frozen mid-flash, a run of frames on which Unity thought nothing was
    /// visible, and a renderer somebody switched off — the last of which is reported and never
    /// suppressed, because switching a prop off is the GAME's decision about game state.</para>
    /// </summary>
    private static void AppendVerdicts(System.Text.StringBuilder sb)
    {
        int best = -1;
        float bestRate = 0f;
        for (int m = 0; m < _matCount; m++)
        {
            for (int k = 0; k < _matPropCount; k++)
            {
                int slot = (m * MatPropCap) + k;
                float h = Hand.MatSecs[slot] > 1e-4f ? Hand.MatAbs[slot] / Hand.MatSecs[slot] : 0f;
                float o = Home.MatSecs[slot] > 1e-4f ? Home.MatAbs[slot] / Home.MatSecs[slot] : 0f;
                float top = Mathf.Max(h, o);
                if (top <= bestRate)
                    continue;
                bestRate = top;
                best = slot;
            }
        }

        sb.Append(" RATE VERDICT: ");
        if (best < 0)
        {
            float ah = Hand.AnySeconds[0] > 1e-4f ? Hand.AnyAdvance[0] / Hand.AnySeconds[0] : 0f;
            float ao = Home.AnySeconds[0] > 1e-4f ? Home.AnyAdvance[0] / Home.AnySeconds[0] : 0f;
            if (ah <= 0f && ao <= 0f)
                sb.Append("NO ANIMATION WAS RUNNING IN EITHER WINDOW — no tracked material property "
                          + "moved and no animator layer advanced, on ").Append(Hand.Frames)
                  .Append(" hand frame(s) and ").Append(Home.Frames)
                  .Append(" home frame(s). That is NOT a verdict about the hand: it says this hold "
                          + "did not contain a flash at all, so the comparison has nothing in it. "
                          + "The next round needs a hold that visibly flashes");
            else
                sb.Append("no material property moved; on the ANIMATOR's all-layer rate the hand ran "
                          + "at ").Append(RatioText(ah, ao)).Append(" (hand ").Append(ah.ToString("0.000"))
                  .Append("/s vs home ").Append(ao.ToString("0.000")).Append("/s)");
        }
        else
        {
            float h = Hand.MatSecs[best] > 1e-4f ? Hand.MatAbs[best] / Hand.MatSecs[best] : 0f;
            float o = Home.MatSecs[best] > 1e-4f ? Home.MatAbs[best] / Home.MatSecs[best] : 0f;
            sb.Append("on the fastest-moving property (mat").Append(best / MatPropCap).Append('.')
              .Append(MatPropName[best % MatPropCap] ?? "<unnamed>").Append(") the hand ran at ")
              .Append(RatioText(h, o)).Append(" (hand ").Append(h.ToString("0.000"))
              .Append("/s over ").Append(Hand.MatMoveFrames[best]).Append(" moving frame(s) vs home ")
              .Append(o.ToString("0.000")).Append("/s over ").Append(Home.MatMoveFrames[best])
              .Append(" moving frame(s))");
        }

        sb.Append(". STOP VERDICT: ");
        int midHand = best >= 0 ? Hand.MatMidFreezeFrames[best] : 0;
        int midHome = best >= 0 ? Home.MatMidFreezeFrames[best] : 0;
        int midRunHand = best >= 0 ? Hand.MatLongestMidFreeze[best] : 0;
        int midRunHome = best >= 0 ? Home.MatLongestMidFreeze[best] : 0;
        sb.Append("frozen MID-FLASH hand=").Append(midHand).Append(" frame(s) (longest run ")
          .Append(midRunHand).Append(") home=").Append(midHome).Append(" frame(s) (longest run ")
          .Append(midRunHome).Append("); frames Unity saw NOTHING of this prop hand=")
          .Append(Hand.VisZeroFrames).Append(" (longest run ").Append(Hand.LongestVisZeroRun)
          .Append(") home=").Append(Home.VisZeroFrames).Append(" (longest run ")
          .Append(Home.LongestVisZeroRun).Append("); frames a renderer was SWITCHED OFF hand=")
          .Append(Hand.OffFrames).Append(" (longest run ").Append(Hand.LongestOffRun)
          .Append(") home=").Append(Home.OffFrames).Append(" (longest run ")
          .Append(Home.LongestOffRun).Append("). A switched-off renderer is the GAME deactivating "
                  + "the prop — a decision about game state, reported here and never suppressed; a "
                  + "zero-visible run with everything still switched on is Unity culling the prop "
                  + "against bounds this mod's reparent invalidated, which is what PropAnimBelt "
                  + "exists to remove. If both read 0 in the hand and the picture still stopped, the "
                  + "stop is in the value itself and MID-FLASH is the term that names it.");
    }

    /// <summary>"x0.27 of home" / "MATCHED (x1.00)" / "only the hand moved" — a ratio with a word
    /// for each degenerate case, because a bare 0 and a bare infinity both read as a bug.</summary>
    private static string RatioText(float hand, float home)
    {
        if (home <= 1e-6f && hand <= 1e-6f)
            return "NEITHER WINDOW MOVED";
        if (home <= 1e-6f)
            return "only the HAND window moved (home was still)";
        if (hand <= 1e-6f)
            return "x0.00 — the hand window did not move at all while the hex did";
        float r = hand / home;
        return Mathf.Abs(r - 1f) <= 0.10f
            ? $"MATCHED (x{r.ToString("0.00")} of home)"
            : $"x{r.ToString("0.00")} of home";
    }

    /// <summary>An int min/max range, with the "never sampled" sentinel printed as n/a rather than
    /// as int.MaxValue.</summary>
    private static string IntRange(int min, int max)
        => min > max ? "n/a" : (min == max ? min.ToString() : $"{min}..{max}");

    private static void Reset(Window w)
    {
        w.Frames = 0;
        w.RealSeconds = 0f;
        for (int i = 0; i < ReportCap; i++)
        {
            w.SpeedMin[i] = float.MaxValue;
            w.SpeedMax[i] = float.MinValue;
            w.CullModes[i] = 0;
            w.UpdateModes[i] = 0;
            w.EnabledFrames[i] = 0;
            w.NormAdvance[i] = 0f;
            w.NormSeconds[i] = 0f;
            w.ZeroFrames[i] = 0;
            w.StateChanges[i] = 0;
            w.Restarts[i] = 0;
            w.SmbAdvance[i] = 0f;
            w.SmbSeconds[i] = 0f;
            w.SmbLiveFrames[i] = 0;
            w.TimelineMin[i] = float.MaxValue;
            w.TimelineMax[i] = float.MinValue;
            w.LayerCounts[i] = 0;
            w.AnyAdvance[i] = 0f;
            w.AnySeconds[i] = 0f;
            w.AnyMoveFrames[i] = 0;
            w.LongestStall[i] = 0;
            w.CurStall[i] = 0;
        }
        for (int i = 0; i < MatSlots; i++)
        {
            w.MatAbs[i] = 0f;
            w.MatSecs[i] = 0f;
            w.MatMoveFrames[i] = 0;
            w.MatMin[i] = float.MaxValue;
            w.MatMax[i] = float.MinValue;
            w.MatReversals[i] = 0;
            w.MatMidFreezeFrames[i] = 0;
            w.MatLongestMidFreeze[i] = 0;
            w.MatCurFreeze[i] = 0;
        }
        w.RendFrames = 0;
        w.VisMin = int.MaxValue;
        w.VisMax = int.MinValue;
        w.VisZeroFrames = 0;
        w.LongestVisZeroRun = 0;
        w.CurVisZeroRun = 0;
        w.DrawMin = int.MaxValue;
        w.DrawMax = int.MinValue;
        w.OffFrames = 0;
        w.LongestOffRun = 0;
        w.CurOffRun = 0;
        w.BoundsDriftMax = 0f;
        w.SkinCensusTaken = false;
        w.SkinCount = 0;
        w.SkinUwoTrue = 0;
        w.ClockMin = float.MaxValue;
        w.ClockMax = float.MinValue;
        w.LocalClockMin = float.MaxValue;
        w.LocalClockMax = float.MinValue;
        w.UnityScaleMin = float.MaxValue;
        w.UnityScaleMax = float.MinValue;
        w.ClockPausedFrames = 0;
        w.ClockMissingFrames = 0;
        w.LossyMin = float.MaxValue;
        w.LossyMax = float.MinValue;
        w.CensusTaken = false;
        w.Particles = 0;
        w.ParticleScalingLocal = 0;
        w.ParticleSimWorld = 0;
        w.ParticlesPlaying = 0;
        w.PoseStomps = 0;
    }

    // ---- formatting -----------------------------------------------------------------------------

    /// <summary>A rate, or "n/a" when the window never collected a usable pair of samples — which
    /// is a real reading (the animator was never evaluating) and must not print as 0.000.</summary>
    private static string Rate(float advance, float seconds)
        => seconds > 1e-4f ? (advance / seconds).ToString("0.000") : "n/a";

    private static string Range(float min, float max)
    {
        if (min > max)
            return "n/a";
        return Mathf.Abs(max - min) < 1e-5f
            ? min.ToString("0.###")
            : $"{min.ToString("0.###")}..{max.ToString("0.###")}";
    }

    private static string Count(int n) => n < 0 ? "?" : n.ToString();

    /// <summary>Decode a bitmask of enum values back into the names, so the line says
    /// "CullCompletely" instead of a number nobody can read on a phone.</summary>
    private static string Modes<T>(int mask) where T : System.Enum
    {
        if (mask == 0)
            return "<none seen>";
        string s = string.Empty;
        System.Array values = System.Enum.GetValues(typeof(T));
        for (int i = 0; i < values.Length; i++)
        {
            object v = values.GetValue(i);
            int iv = System.Convert.ToInt32(v);
            if (iv < 0 || iv >= 31 || (mask & (1 << iv)) == 0)
                continue;
            s = s.Length == 0 ? v.ToString() : s + "+" + v;
        }
        return s.Length == 0 ? "<none seen>" : s;
    }
}
