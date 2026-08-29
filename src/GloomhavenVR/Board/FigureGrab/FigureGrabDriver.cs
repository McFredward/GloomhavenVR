using ScenarioRuleLibrary;
using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// Registers a <see cref="FigureGrabbable"/> against every live board figure and drives
/// the far laser point-and-grab (P8). Enumeration mirrors <c>WorldUI/ActorBars.Tick</c>:
/// it walks the game's own <c>WorldspaceUITools.Instance._panelUIControllers</c> registry,
/// takes each controller's tracked figure GameObject (<c>m_ObjectToTrack</c>), resolves the
/// figure's <c>CInteractableActor</c> collider + <c>ActorBehaviour</c>, and adopts/prunes
/// one grabbable per actor.
///
/// Near reach-and-close grabs are handled entirely by <see cref="ProximityGrabber"/>
/// (the TRIGGER, since the grabbable is no longer <c>GrabWithGrip</c> — same button +
/// <c>Ray.HasFreshUiHit</c> arbitration as the hand cards). This driver only adds the FAR
/// path: while the hand's ray points at a grabbable figure it clamps the beam to the mini
/// (<c>Ray.UiHitOverride</c>) so the trigger over a figure grabs it instead of doubling as
/// a board far-click, and on TriggerDown <c>ForceGrab</c> plucks it (released on
/// TriggerUp) — the exact trigger pluck the card fan uses. A fan card / world-UI panel that
/// clamped the beam closer this frame keeps the trigger (we defer on a FOREIGN
/// <c>HasFreshUiHit</c>, ignoring our own figure clamp).
/// </summary>
internal sealed class FigureGrabDriver : MonoBehaviour
{
    private sealed class Adopted
    {
        public FigureGrabbable Grabbable = null!;

        /// <summary>THE VOLUME THE ELECTION MEASURES AGAINST, and the one this figure is
        /// registered with. Usually the game's own authored pick collider — but on a figure whose
        /// authored collider stops well below the miniature the player can see, this is the
        /// mod-owned capsule in <see cref="Reach"/> instead. Everything that gates a grab reads
        /// THIS field, so extending the reach is one assignment and not a second code path.
        /// </summary>
        public Collider Collider = null!;

        /// <summary>The game's own authored pick collider, always — kept beside
        /// <see cref="Collider"/> so the diagnostics can print BOTH and a reader can tell a figure
        /// the mod extended from one it did not.</summary>
        public Collider GameCollider = null!;

        /// <summary>The character's head joint (<c>m_HeadBonePoint</c>), when it has one: the
        /// FLOOR under <see cref="FigureBody.LiveTopY"/>, never the anchor. Null is normal and
        /// costs nothing.</summary>
        public Transform? HeadBone;

        /// <summary>The mod-owned reach extension, or null when the game's collider already
        /// covers this figure — which is every figure in the ModBuild 291 log except the boss.
        /// </summary>
        public ReachVolume? Reach;

        /// <summary>Remaining re-measurements of the reach volume, and when the next one is due.
        /// Same reason and same schedule as the health bar's anchor resample: the boss's own mesh
        /// bounds grew 2.89x in the half second after ActorBars adopted it, so ONE measurement at
        /// adoption is a measurement of whatever the figure happened to be mid-assembly.</summary>
        public int ReachSamplesLeft;

        public float NextReachSample;

        /// <summary>Set once when this figure's pick collider is a shape the extension refuses to
        /// copy, so the refusal is stated exactly once per figure and never per sample.</summary>
        public bool ReachShapeRefused;

        /// <summary>The figure's own GameObject (the controller's <c>m_ObjectToTrack</c>) — the
        /// root the reach diagnostics walk for the miniature's SURFACE geometry. Held here rather
        /// than re-resolved so the miss probe describes exactly the object the adoption measured.
        /// </summary>
        public GameObject Figure = null!;
    }

    // Keyed by the figure's interactable collider component (Unity-nullable key, like
    // ActorBars' controller map — a destroyed key stays a valid CLR dictionary key).
    // KEYED BY Component, NOT BY CInteractableActor, since ModBuild 323 — and the key is only
    // ever an IDENTITY here: nothing in this file reads a member off it, it is compared to null
    // and used to look a row up. That is what made the widening safe.
    //
    // WHY IT HAD TO WIDEN: figures are keyed by their CInteractableActor, and props do not have
    // one. CInteractableActor.Start resolves its actor from GetComponentInParent<CharacterManager>(),
    // so the component exists for CHARACTERS AND MONSTERS ONLY — the game reaches a chest or a
    // trap through its HEX (CInteractableTile) instead. Props are therefore keyed by their
    // ActorBehaviour, which every client object carries (Choreographer.FindClientObjectActor
    // resolves them with ActorBehaviour.GetActor). One dictionary, one set of per-frame loops,
    // one FigureGrabbable class: the highlight, the haptics, the hold gate, the info panel, the
    // ghosts and both multiplayer slots come to props for free because none of them ever knew
    // what the key was.
    private readonly Dictionary<Component, Adopted> _adoptions = new();
    private readonly List<Component> _scratch = new(32);

    /// <summary>
    /// [Optimize] FigureScanCache: figure GameObject instance id → its CInteractableActor, so the
    /// per-frame registry sweep does not re-run a deep includeInactive hierarchy walk for figures
    /// it already knows. Keyed by INSTANCE ID (not the object) so a destroyed figure can never keep
    /// a Unity-null key alive in a way that hides a rebuilt one; a stale/destroyed value simply
    /// falls back to the full walk. Cleared with the adoptions.
    /// </summary>
    private readonly Dictionary<int, CInteractableActor> _figureInteractables = new(64);

    // Last frame THIS driver clamped the beam to a figure, per hand — so the far-grab
    // arbitration can tell our own fresh figure clamp apart from a FOREIGN UI/card clamp.
    private int _leftClampFrame = int.MinValue;
    private int _rightClampFrame = int.MinValue;

    // Item 3 — proximity reach for the offset-anchor selection. Mirrors ProximityGrabber's
    // private ReachMeters (0.13 m at scale 1) so the candidate set matches the palm reach the
    // grabber itself would consider before we narrow it to the offset-anchor-nearest figure.
    private const float ReachMeters = 0.13f;

    // Log dedupe (see LogPickVolume / LogElection): the last pick-volume line printed, and the
    // figure each hand last elected as its pinch candidate. Diagnostics only — nothing reads these
    // to decide anything.
    private string _lastPickVolumeLog = string.Empty;
    private FigureGrabbable? _lastElectedLeft;
    private FigureGrabbable? _lastElectedRight;

    /// <summary>
    /// How far past <see cref="FigureGrabConfig.PickRadiusRealMeters"/> the figure that ALREADY
    /// holds the election keeps it — the release ring of the Schmitt trigger in
    /// <see cref="TickOffsetAnchorSelect"/>. A factor and not a second dial on purpose: a player
    /// tunes "how close must I get", never "how much slack does letting go get", and two dials that
    /// can be set to cross each other would need a third rule to sort them out.
    ///
    /// <para>1.25 = a quarter of the reach again. At the shipped 40 mm that is a 10 mm dead band,
    /// which is wider than the boundary drift the hardware log shows (39 → 38 → 37 mm on one
    /// figure) and far narrower than the distance between two minis on neighbouring hexes, so it
    /// cannot make a NEIGHBOUR sticky.</para>
    /// </summary>
    private const float PickExitFactor = 1.25f;

    /// <summary>
    /// Consecutive frames a NEW nearest figure must stay nearest before it is allowed to light up
    /// (the dwell in <see cref="TickOffsetAnchorSelect"/>). Six frames is ~67 ms at 90 Hz — under
    /// the ~100 ms at which a delay starts being felt as lag, and far longer than the one or two
    /// frames a figure owns the nearest slot while a hand sweeps past it.
    /// </summary>
    private const int PickDwellFrames = 6;

    /// <summary>The figure each hand's election currently rests on — the hysteresis state, indexed
    /// by <see cref="HandSide"/>. Distinct from <see cref="_lastElectedLeft"/>/<see cref="_lastElectedRight"/>,
    /// which are the LOG's dedupe and are written by <see cref="LogElection"/> itself: sharing them
    /// would make every election look unchanged to the log and silence it.</summary>
    private readonly FigureGrabbable?[] _electedBySide = new FigureGrabbable?[2];

    /// <summary>The candidate currently serving its dwell, per hand, and how many consecutive
    /// frames it has served. Reset by any change of candidate, so two figures alternating can never
    /// accumulate a dwell between them.</summary>
    private readonly FigureGrabbable?[] _pendingBySide = new FigureGrabbable?[2];

    private readonly int[] _dwellBySide = new int[2];

    // TURN-DEADLOCK GATE refusal feedback (see NoteBusyRefusal): one "no" per second per hand, and
    // the game's own negative ping as the last resort behind its invalid-option item.
    private const float BusyRefusalIntervalSeconds = 1f;
    private const string NegativePingFallback = "PlaySound_UIPingRewardNegative";
    private static readonly string[] BusyRefusalFallbacks = new string[1];
    private float _nextBusyRefusalLeft;
    private float _nextBusyRefusalRight;

    // REACH-MISS probe (see NoteReachMiss): the same one-per-second-per-hand throttle the busy
    // refusal uses, so a player mashing the trigger at a spot that cannot be grabbed gets one
    // legible line and not a rattle.
    private float _nextReachMissLeft;
    private float _nextReachMissRight;

    /// <summary>Trigger pulls that elected nothing while the hand was not near any figure's DRAWN
    /// body — counted rather than printed (see <see cref="NoteReachMiss"/>), and reported as a
    /// field on the next line that IS a real reach miss.</summary>
    private int _reachMissSuppressed;

    /// <summary>Renderer scratch for <see cref="DescribeRenderedBody"/> — the figure's own
    /// surface geometry, walked at ADOPTION and on a throttled reach miss only, never per frame.
    /// Static because the driver is a singleton behaviour and the walks never nest.</summary>
    private static readonly List<Renderer> BodyScratch = new(32);

    /// <summary>Collider scratch for the adoption census; see <see cref="LogFigureReach"/>.</summary>
    private static readonly List<Collider> ColliderScratch = new(8);

    /// <summary>The colliders inside the figure's <c>CInteractableActor</c> subtree, so the census
    /// can say of each collider under the actor root whether the pick search can even see it.
    /// </summary>
    private static readonly HashSet<Collider> InteractableColliders = new();

    /// <summary>How many colliders the adoption census names before it says how many it left out.
    /// A hero carries 13; naming all of them once per figure is fine, and the cap only exists so a
    /// pathological prefab cannot write a kilobyte line.</summary>
    private const int ColliderRollCap = 16;

    // -- REACH EXTENSION (boss-dragon round, ModBuild 292) --------------------------------
    //
    // WHAT THE ModBuild 291 INSTRUMENT SETTLED. FIGURE REACH printed five figures. Four of them
    // are covered by the collider the flat game authored for a mouse click:
    //
    //   BruteID              capsule on 'HE_Brute'      y -0.05..2.05   covers  82 %
    //   MindthiefID          capsule on 'HE_Mindthief'  y -0.15..1.65   covers  96 %
    //   SpittingDrakeID      capsule on 'Actor(Clone)'  y  0.00..2.00   covers  97 %
    //   RendingDrakeEliteID  capsule on 'Actor(Clone)'  y  0.00..2.00   covers  95 %
    //   ElderDrakeID         capsule on 'Actor(Clone)'  y  0.00..2.00   covers  30 %
    //
    // The three monsters carry the IDENTICAL capsule -- 1.00 x 2.00 x 1.00 wu on the actor root.
    // It is not authored per figure; it is the actor prefab's, and it is a hex wide and two units
    // tall whatever is standing in it. Two of the three monsters are shorter than two units, so it
    // fits them. The boss is 6.72 units of drawn dragon and everything above y = 2.00 is inert --
    // which is the report, in the user's words: "muss es dann aber am unteren Bereich tun ... ueber
    // der Healthbar geht das nicht".
    //
    // WHY NOT THE OTHER COLLIDERS THE FIGURE CARRIES. The brief for this round expected the fix to
    // be "elect across all of the figure's colliders", on the strength of the instrument's line
    // "the figure carries 1 collider(s) under its CInteractableActor and 4 under the actor root".
    // That line prints a COUNT. It does not print what those colliders ARE, where they are, or how
    // big they are, and nothing else in the log does either -- so "use them" would be a fix built
    // on a number that cannot support it. They are outside the CInteractableActor subtree entirely
    // (1 under it, 4 under the root), which is the one thing the count DOES tell us, and it makes
    // them likelier to belong to some other system (a ground probe, a targeting volume, an aggro
    // trigger) than to the miniature's body. Electing across an unknown trigger volume is how a
    // figure becomes grabbable from a metre away. LogFigureReach now NAMES every one of them so
    // the next round can decide on evidence instead; the fix below does not depend on the answer.
    //
    // WHAT THIS IS. A mod-owned capsule with the game capsule's OWN horizontal profile, extended
    // upward to the figure's LIVE top (FigureBody.LiveTopY -- the world extent of its bone
    // transforms, raised to the head joint where the skeleton falls short; the ModBuild 292 slack
    // correction it replaced is retired, see FigureBody). Same radius, same axis, same centre:
    // the volume gains height and nothing else, so no figure becomes easier to grab from the side
    // and no figure can steal an election from its neighbour. Built ONLY for a figure whose
    // trusted top clears the game collider by more than ReachExtensionMinGapFraction of its own
    // height -- for the four covered figures above the gap is NEGATIVE and no volume is created,
    // so their behaviour is not merely unchanged but untouched.
    //
    // WHAT IT IS NOT: it is not a wider pick radius. [FigureGrab] PickRadiusMillimeters stays
    // where it is, for the reasons recorded in FigureGrabConfig (the accidental-grab leak fires at
    // the palm reach, and the card fan is measured in the same number). This changes WHERE a
    // figure is, not how far a hand may reach for one.

    /// <summary>Unity's built-in "Ignore Raycast" layer -- the one layer
    /// <c>Physics.DefaultRaycastLayers</c> excludes. Same choice, and the same reason, as
    /// <c>FigureClothHands</c>: the volume must be invisible to every raycast in the game and in
    /// the mod (the flat game's mouse pick, the VR laser) while still answering
    /// <c>Collider.ClosestPoint</c>, which does not consult layers at all. It is additionally a
    /// TRIGGER, so it can never push, block or carry anything even if some object with a
    /// Rigidbody passes through it.</summary>
    private const int IgnoreRaycastLayer = 2;

    /// <summary>Name of the mod-owned reach volume's GameObject -- greppable in a hierarchy dump,
    /// and the string that keeps <see cref="LogFigureReach"/>'s collider census honest about which
    /// colliders are the game's.</summary>
    private const string ReachVolumeName = "VR_FigureReach";

    /// <summary>
    /// How far the figure's trusted top must clear the game's pick collider, AS A FRACTION OF THE
    /// FIGURE'S OWN HEIGHT, before a reach extension is built.
    ///
    /// <para>Not a tuning dial -- a floor under "is this what refused the grab?". A slab of figure
    /// thinner than a twentieth of the figure is thinner than the pick radius bridges anyway
    /// (40 mm real is 0.38 wu at the rig scale in the ModBuild 291 log), so extending for it would
    /// buy nothing and cost a GameObject. On the five measured figures the gap is 2.28 wu of 6.72
    /// for the boss (34 %) and NEGATIVE for the other four; nothing measured so far lands anywhere
    /// near this line.</para>
    /// </summary>
    private const float ReachExtensionMinGapFraction = 0.05f;

    /// <summary>Re-measurements of the reach volume after adoption, and the seconds between them --
    /// the same 8 x 0.5 s budget <c>ActorBars</c> gives the health-bar anchor, for the same
    /// measured reason: in the ModBuild 291 log the boss's mesh bounds grew from 2.37 wu to
    /// 6.84 wu in the half second after it was first measured, with a BIT-IDENTICAL renderer
    /// census either side. A figure adopted mid-assembly must not keep a volume sized to whatever
    /// it briefly was.</summary>
    private const int ReachSampleBudget = 8;

    private const float ReachSampleIntervalSeconds = 0.5f;

    /// <summary>
    /// A mod-owned grab volume standing in for a game pick collider that stops below the figure.
    /// One per extended figure; the four ordinary figures in the ModBuild 291 log have none.
    /// </summary>
    private sealed class ReachVolume
    {
        /// <summary>The mod-owned GameObject, parented to the game collider's transform so it
        /// inherits every move, rotation and scale the figure makes -- including the held-size
        /// scaling, which then scales the volume and the body by the same factor.</summary>
        public GameObject Host = null!;

        public CapsuleCollider Capsule = null!;

        /// <summary>World y the volume currently reaches to -- the number the resample compares
        /// against, and the one the log line prints.</summary>
        public float TopY;
    }

    // Cached per-frame tick delegates ([Optimize] CacheTickDelegates — see Update).
    private System.Action? _tickRegistry;
    private System.Action? _tickAutoRelease;
    private System.Action? _tickAnchorSelect;
    private System.Action? _tickLaserGrab;

    private void OnDestroy()
    {
        ReleaseAll();
        FigureGrabbable.FinishAllGlides(); // land any in-flight release glide (no stale suppression)
        FigureGhosts.Clear();
        FigureRingSuppressor.Clear();
        FigureStallWatchdog.Reset();
        FigureCloth.Clear(); // no stale per-figure cloth bookkeeping across a scene change
        FigureClothHands.Clear(); // and the free-hand probe is restored off its cloths and destroyed
    }

    private void Update()
    {
        // FRAME-ORDER FigureGrabDriver.Update [FigureGrab.StallWatchdog, FigureGrab.Ghosts, FigureGrab.Glide, FigureGrab.HeldSize, GATE:FigureGrabConfig.GrabFigures, FigureGrab.Registry, FigureGrab.AutoRelease, FigureGrab.Stretch, FigureGrab.OffsetAnchorSelect, FigureGrab.LaserGrab]
        //   The GATE token is load-bearing, not decoration: StallWatchdog, Ghosts and Glide must run
        //   BEFORE the config gate's early-out. Ghosts so REMOTE-held ghosts still appear and clear
        //   while local figure-grab is off, Glide so a release glide already in flight still lands
        //   when the toggle is flipped mid-air, StallWatchdog so a turn already stalled before the
        //   toggle was flipped off still gets repaired (turning figure-grab off must not strand the
        //   session in a dead turn machine). Moving any of them below the gate strands a mini in the
        //   air on a remote peer — a bug that is invisible in single-player and invisible to
        //   refactor-guard.sh (it is an ordinary in-type diff). Locked; reordering is Tier 3.
        // TURN-DEADLOCK BACKSTOP (user, 2026-08-11) — repair a choreographer wait that a killed
        // AttackModBar coroutine has left blocked forever. Strict no-op unless a figure was held
        // during the stalled wait; see FigureStallWatchdog and FigureBusy for the whole account.
        TickGuard.Run("FigureGrab.StallWatchdog", FigureStallWatchdog.Tick);

        // TASK #3 — reconcile home-spot ghosts against the local + remote held-sets (spawn is done at
        // grab time; this only tears down ghosts whose figure was released, incl. remote releases).
        // Runs even when local figure-grab is disabled so REMOTE-held ghosts still appear/clear.
        TickGuard.Run("FigureGrab.Ghosts", FigureGhosts.Tick);

        // GLIDE-BACK — advance every in-flight release glide (released mini easing home). Before
        // the config gate so a glide started just before GrabFigures was toggled off still lands
        // (the toggle's ReleaseAll → Restore also finishes glides instantly as a backstop).
        TickGuard.Run("FigureGrab.Glide", FigureGrabbable.TickGlides);

        // HELD SIZE — re-assert every held mini's LATCHED grab-time size (user, 2026-08-11: "die
        // Größe soll nur abhängig sein wann sie greift und dann fix in der Hand sein - auch wenn man
        // dabei zoomed"). It writes the vector frozen at the grab, so a zoom moves nothing; the root
        // cause it fixed is in FigureGrabbable._heldLocalScale. Kept above the config gate for the
        // same reason as Glide: a mini still in the hand when GrabFigures is toggled off is released
        // by the gate's ReleaseAll on THIS frame, and it must not be rendered at a size some other
        // writer touched for the frame in between. Cheap (one vector store per held figure) and a
        // strict no-op when nothing is held.
        TickGuard.Run("FigureGrab.HeldSize", FigureGrabbable.TickHeldScale);

        if (!FigureGrabConfig.GrabFigures.Value)
        {
            if (_adoptions.Count > 0)
                ReleaseAll();
            return;
        }

        // [Optimize] CacheTickDelegates (2026-07 perf pass): these four used to allocate a fresh
        // Action from an instance method group EVERY FRAME — four of the mod's seven such sites,
        // ~256 B/frame from this driver alone, all of it gen0 garbage whose collection pauses show
        // up as exactly the head-turn judder being investigated. Cached now; the toggle re-creates
        // them per frame so the cost can be A/B'd against the [Perf] gc/alloc counters.
        bool cache = PerfConfig.CacheDelegates;
        TickGuard.Run("FigureGrab.Registry", cache ? _tickRegistry ??= RefreshRegistry : RefreshRegistry);
        TickGuard.Run("FigureGrab.AutoRelease", cache ? _tickAutoRelease ??= AutoReleaseMovedFigures : AutoReleaseMovedFigures);
        // HELD-FIGURE STRETCH — the two-hand resize gesture (user, 2026-08-11: "mit der anderen
        // Hand zu der Figur … Trigger gedrückt halte und nach innen oder außen schiebe"). MUST run
        // BEFORE OffsetAnchorSelect and LaserGrab: its engagement state is the gate both consult
        // this same frame (an engaged hand elects nobody and plucks nothing), and computing it
        // after them would leave the veto one frame stale — the exact defect class ApplySuppression
        // exists to end. Below the config gate on purpose: the gesture needs a locally-held figure,
        // which cannot exist while GrabFigures is off (the gate's ReleaseAll also clears the
        // gesture state via FigureStretch.Clear). See FigureStretch for the gesture itself.
        TickGuard.Run("FigureGrab.Stretch", FigureStretch.Tick);
        TickGuard.Run("FigureGrab.OffsetAnchorSelect", cache ? _tickAnchorSelect ??= TickOffsetAnchorSelect : TickOffsetAnchorSelect);
        TickGuard.Run("FigureGrab.LaserGrab", cache ? _tickLaserGrab ??= TickLaserGrab : TickLaserGrab);

        // (Issue A) There is deliberately NO per-frame rotation step here: the held rotation is a
        // fixed anchor-local rotation applied once at grab — see FigureGrabConfig.HeldUprightRotation.

        // Keep the held figure's stat panel locked to that figure even if the laser sweeps
        // another figure on the board (risk #5 — the game's hover would otherwise re-target).
        if (HeldFigures.Count > 0)
            StatPanelSurface.ReassertHeld();
    }

    /// <summary>
    /// Issue C — freeze the animation-driven position of every held figure (local + remote) AFTER
    /// the Animator has run this frame. A figure grabbed mid-walk (or in any animation) would
    /// otherwise translate straight out of the hand: the suppressed <c>ActorBehaviour.ApplyMotion</c>
    /// is what normally re-zeros the animated mesh each LateUpdate. Re-pinning it here reinstates
    /// exactly that one write so the mesh rides the hand while the clip keeps playing. Runs in
    /// LateUpdate precisely so it lands after the animation update; a strict no-op when nothing is
    /// held (offline or otherwise).
    /// </summary>
    private void LateUpdate()
    {
        // FRAME-ORDER FigureGrabDriver.LateUpdate LateUpdate-required [HeldFigures.PinAnimatedRoots, NetHeldFigures.PinAnimatedRoots, FigureRingSuppressor.Tick]
        //   These three MUST run in LateUpdate, after the Animator. The doc comment above says
        //   why; this line is the machine-checked form of it. The change it exists to stop is
        //   "merge Update and LateUpdate for symmetry" — which would put the pin BEFORE the
        //   animation update and translate every held figure straight out of the hand.
        if (HeldFigures.Count > 0)
            TickGuard.Run("FigureGrab.PinHeld", HeldFigures.PinAnimatedRoots);
        if (NetHeldFigures.Count > 0)
            TickGuard.Run("FigureGrab.PinNetHeld", NetHeldFigures.PinAnimatedRoots);

        // TASK #2 — keep the game's selection ring OFF under every in-hand figure (local AND
        // remote-held), restoring the game's intent on release; the ghost's own ring copy at the
        // home cell is the only ring the player sees while a figure is held. Runs in LateUpdate so
        // it lands after the game's Update writes and before render; unconditional (not gated on
        // GrabFigures) so remote-held figures stay covered, and a strict no-op when nothing is held.
        TickGuard.Run("FigureGrab.RingSuppress", FigureRingSuppressor.Tick);
    }

    private void RefreshRegistry()
    {
        WorldspaceUITools tools = WorldspaceUITools.Instance;
        if (tools == null || Choreographer.s_Choreographer == null)
            return;

        // Adopt new figures.
        List<WorldspacePanelUIController> controllers = tools._panelUIControllers;
        for (int i = 0; i < controllers.Count; i++)
        {
            WorldspacePanelUIController controller = controllers[i];
            if (controller == null)
                continue;
            GameObject figure = controller.m_ObjectToTrack;
            if (figure == null)
                continue;

            // [Optimize] FigureScanCache (2026-07 perf pass): the GetComponentInChildren below is a
            // DEEP hierarchy walk (includeInactive, so it visits every disabled child of a rigged
            // character mesh), and it used to run for EVERY registered figure on EVERY frame —
            // including the ones already adopted, whose result is thrown away one line later by the
            // ContainsKey check. A figure never changes its CInteractableActor, so remembering the
            // resolution per figure turns the steady state (all figures adopted) into a dictionary
            // lookup per figure. Behaviour is unchanged: a destroyed/cleared entry falls back to the
            // full walk, so a figure that is rebuilt is picked up exactly as before.
            bool lean = Core.PerfConfig.FigureScanCacheOn;
            CInteractableActor? interactable = null;
            int figureId = 0;
            if (lean)
            {
                figureId = figure.GetInstanceID();
                if (_figureInteractables.TryGetValue(figureId, out CInteractableActor cached))
                {
                    if (cached != null && _adoptions.ContainsKey(cached))
                        continue;      // already adopted — nothing left to resolve this frame
                    interactable = cached; // may be Unity-null (destroyed) → re-resolved below
                }
            }
            if (interactable == null)
                interactable = figure.GetComponentInChildren<CInteractableActor>(includeInactive: true);
            if (interactable == null || _adoptions.ContainsKey(interactable))
                continue;
            if (lean)
                _figureInteractables[figureId] = interactable;

            Collider? collider = interactable.GetComponent<Collider>();
            if (collider == null)
                collider = interactable.GetComponentInChildren<Collider>();
            ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(figure);
            if (collider == null || actor == null)
                continue;

            // The collider rides along for the highlight diagnostic only (FigureGrabbable.DescribeReach) —
            // the same one the election measures against, so the two readings are commensurable.
            var grabbable = new FigureGrabbable(actor, collider);
            // LOGGED BEFORE THE EXTENSION EXISTS, deliberately: the collider census in that line
            // must count the game's colliders and not ours, or the next round reads our own volume
            // back as evidence about the game's prefab.
            LogFigureReach(grabbable, interactable, figure, collider);
            var adopted = new Adopted
            {
                Grabbable = grabbable, Collider = collider, GameCollider = collider,
                Figure = figure, HeadBone = controller.m_HeadBonePoint,
                ReachSamplesLeft = ReachSampleBudget,
                NextReachSample = Time.unscaledTime + ReachSampleIntervalSeconds,
            };
            SizeReachVolume(adopted);
            VRInteractables.RegisterGrabbable(grabbable, adopted.Collider);
            _adoptions[interactable] = adopted;
        }

        // Prune figures whose collider/actor died (actor removed / scene unloading).
        if (_adoptions.Count == 0)
            return;
        _scratch.Clear();
        foreach (KeyValuePair<Component, Adopted> pair in _adoptions)
        {
            // GameCollider as well as Collider: on an extended figure the two are different
            // objects, and it is the GAME's collider dying that means the figure is gone.
            if (pair.Key == null || pair.Value.Collider == null || pair.Value.GameCollider == null)
                _scratch.Add(pair.Key!);
        }
        for (int i = 0; i < _scratch.Count; i++)
            Drop(_scratch[i]);

        AdoptProps();

        // REACH VOLUMES ride the registry tick rather than becoming an eighth step in
        // FigureGrabDriver.Update's locked frame order: they are part of keeping the adoption set
        // current, which is exactly this method's job, and a new locked step is a Tier 3 change
        // that this fix does not need. Self-limiting -- 8 samples per figure over its first 4 s
        // and then never again (see TickReachVolumes).
        TickReachVolumes();
    }

    /// <summary>
    /// PROPS — chests, gold piles, traps and obstacles go in the hand too (user, 2026-08-29: "ich
    /// finde ich es eine gute Idee, dass man auch Geldhaufen, Fallen und co in die Hand heben kann,
    /// statt nur Figuren").
    ///
    /// <para>THEY NEED THEIR OWN DISCOVERY PASS, and finding out why is what made this small. The
    /// loop above walks <c>WorldspaceUITools._panelUIControllers</c> and then demands a
    /// <c>CInteractableActor</c> — and that component resolves its actor from
    /// <c>GetComponentInParent&lt;CharacterManager&gt;()</c>, so it exists for CHARACTERS AND
    /// MONSTERS ONLY. The game reaches a chest through its HEX (<c>CInteractableTile</c>), never
    /// through the chest. That is the whole reason props have been invisible to this registry, and
    /// no widening of the existing loop could have found them.</para>
    ///
    /// <para>The right source is <c>Choreographer.m_ClientObjects</c> — the client's object actors,
    /// the very list <c>FindClientObjectActor</c> searches, and every entry carries an
    /// <c>ActorBehaviour</c> (that method resolves them with <c>ActorBehaviour.GetActor</c>). So a
    /// prop needs no new grabbable, no new highlight and no new info panel: it is adopted into the
    /// SAME dictionary as a figure, wrapped in the SAME <see cref="FigureGrabbable"/>, and every
    /// per-frame loop in this file already iterates it. The hover tint, the haptic, the hold gate,
    /// the ghost, the stat panel and both multiplayer figure slots arrive with it.</para>
    ///
    /// <para>ONLY WHAT A HAND COULD LIFT, which is a WHITELIST and not a filter — the user drew the
    /// line himself ("Beschränke dich auf Dinge die man in die Hand nehmen kann, Gelände in dem man
    /// mehr laufen muss kann man nicht in die Hand nehmen"). Chest, gold pile, trap, obstacle,
    /// quest item and loose resource are in. Difficult and hazardous terrain are not objects you
    /// lift, a door is part of the wall, a pressure plate is part of the floor and a portal is not
    /// a thing at all. A whitelist also means a prop type the game adds later is NOT liftable until
    /// somebody decides it is, which is the safe default for a list nobody will remember to check.</para>
    ///
    /// <para>A PROP MAY HAVE NO COLLIDER, precisely because it was never interactable. When it has
    /// none, one is built from its renderer bounds on a child object this driver owns, on the
    /// Ignore Raycast layer and as a TRIGGER, so it can never enter the game's physics or its
    /// picking — it exists only for the proximity election to measure against. That mirrors what
    /// <see cref="SizeReachVolume"/> already does for a figure whose authored collider is too
    /// small.</para>
    /// </summary>
    private void AdoptProps()
    {
        if (!FigureGrabConfig.GrabPropsEnabled)
            return;
        Choreographer? ch = Choreographer.s_Choreographer;
        if (ch == null)
            return;
        List<GameObject> objects = ch.m_ClientObjects;
        if (objects == null)
            return;

        for (int i = 0; i < objects.Count; i++)
        {
            GameObject go = objects[i];
            if (go == null)
                continue;
            ActorBehaviour actor = ActorBehaviour.GetActorBehaviour(go);
            if (actor == null || _adoptions.ContainsKey(actor))
                continue;
            if (!IsLiftable(ActorBehaviour.GetActor(go)))
                continue;

            Collider? game = go.GetComponentInChildren<Collider>();
            Collider? collider = game != null ? game : BuildPropCollider(go);
            if (collider == null)
                continue;

            var grabbable = new FigureGrabbable(actor, collider);
            var adopted = new Adopted
            {
                Grabbable = grabbable, Collider = collider, GameCollider = collider,
                Figure = go, HeadBone = null,   // a chest has no head bone; every reader guards it
                ReachSamplesLeft = ReachSampleBudget,
                NextReachSample = Time.unscaledTime + ReachSampleIntervalSeconds,
            };
            SizeReachVolume(adopted);
            VRInteractables.RegisterGrabbable(grabbable, adopted.Collider);
            _adoptions[actor] = adopted;
            if (!_loggedProp)
            {
                _loggedProp = true;
                VRLog.Info("FigureGrab", $"Props are grabbable: adopted '{go.name}' "
                    + $"({ActorBehaviour.GetActor(go)?.GetType().Name}) through Choreographer."
                    + "m_ClientObjects, with the SAME FigureGrabbable a miniature uses — so it gets "
                    + "the same highlight, haptic, hold gate, stat panel and MP slot. Collider: "
                    + (game != null ? "the prop's own." : "built from its renderer bounds (it had none)."));
            }
        }
    }

    private bool _loggedProp;

    /// <summary>Is this actor a prop a hand could pick up? See <see cref="AdoptProps"/> for why
    /// this is a whitelist.</summary>
    private static bool IsLiftable(CActor? actor)
    {
        if (actor is not CObjectActor obj)
            return false;
        CObjectProp? prop = obj.AttachedProp;
        return prop is CObjectChest or CObjectGoldPile or CObjectTrap or CObjectObstacle
                    or CObjectQuestItem or CObjectResource;
    }

    /// <summary>A trigger collider sized to what the prop DRAWS, for a prop the game never gave
    /// one. Ignore Raycast + isTrigger: invisible to the game's physics and to every picking path,
    /// mod and vanilla, so it can only ever be measured against by the proximity election.</summary>
    private static Collider? BuildPropCollider(GameObject go)
    {
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0)
            return null;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);
        if (b.size.sqrMagnitude <= 1e-10f)
            return null;

        var holder = new GameObject("GloomhavenVR.PropReach") { layer = 2 };
        holder.transform.SetParent(go.transform, worldPositionStays: true);
        holder.transform.position = b.center;
        holder.transform.rotation = Quaternion.identity;
        var box = holder.AddComponent<BoxCollider>();
        box.isTrigger = true;
        // Sized in WORLD units and then divided by the prop's own lossyScale, because the holder
        // inherits that scale and a size written raw would be multiplied by it a second time.
        Vector3 lossy = go.transform.lossyScale;
        box.size = new Vector3(
            b.size.x / Mathf.Max(1e-4f, Mathf.Abs(lossy.x)),
            b.size.y / Mathf.Max(1e-4f, Mathf.Abs(lossy.y)),
            b.size.z / Mathf.Max(1e-4f, Mathf.Abs(lossy.z)));
        return box;
    }

    /// <summary>
    /// R2 hardening: if the game moves a HELD figure to a new authoritative board cell (a networked
    /// move on a remote/enemy turn, or the actor is destroyed under us), restore it immediately so
    /// it never rides the hand at a stale board position and jumps on release. Restore is idempotent
    /// and leaves the grabber's logical hold to end normally on trigger-up (a no-op re-Restore).
    /// </summary>
    private void AutoReleaseMovedFigures()
    {
        foreach (Adopted adopted in _adoptions.Values)
        {
            FigureGrabbable grabbable = adopted.Grabbable;
            if (!grabbable.IsHeld)
                continue;
            if (grabbable.AuthoritativeCellChanged())
            {
                // Deliberately INSTANT (no glide): the game already moved the figure to a NEW
                // cell, and the glide's landing pose is the grab-time home — easing to a stale
                // cell and snapping from there would be worse than the one-frame hand-off.
                grabbable.Restore();
                continue;
            }

            // HOLD GATE (user ruling 2026-08-11), second belt — PER-FIGURE, not global.
            // HoldMustEnd fires only when the game depends on THIS figure or the figure itself
            // leaves idle — "Solange diese eine figure idle its soll sie auch in der Hand bleiben
            // können, egal was passiert." TRAP: asking the GRAB gate FigureBusy.IsBusy here, as
            // the old code did, dumps a held idle figure the moment ANY attack resolves anywhere
            // (its global unbounded-wait clause). See FigureBusy for the predicate split.
            //
            // FigureGrabbable.AllowsHand already refuses the holder on the same predicate, which
            // makes ProximityGrabber.HealDeadHeld force-release it through the normal OnRelease
            // path — that is the parallel route and it also cleans up the hand's grab state. This
            // belt stays because the suppression the game could hang on lives in HeldFigures, not
            // in the grabber: if the grabber ever fails to tick (mode policy, interactor disabled,
            // a hand going untracked in the same frame) the actor must STILL leave HeldFigures —
            // via the glide's landing now, 0.28 s, which is safe (no deactivation edge; see
            // FigureBusy's deadlock-safety notes) — or ActorBars keeps its bar host deactivated.
            // Both routes are glide-guarded and idempotent, so they cannot fight (OnRelease
            // absorbs a release for a figure already gliding).
            if (FigureBusy.HoldMustEnd(grabbable.Actor, out string why))
                grabbable.AutoReleaseToBoard(why);
        }
    }

    private void TickLaserGrab()
    {
        TryLaserGrab(VRHands.Left);
        TryLaserGrab(VRHands.Right);
    }

    private void TryLaserGrab(VRHand? hand)
    {
        if (hand == null || !hand.HasPose || !hand.Ray.Enabled)
            return;
        // HELD-FIGURE STRETCH: an engaged hand's trigger belongs to the gesture — no far pluck.
        // Belt to the arbitration below: FigureStretch's beam clamp is a FOREIGN fresh UI hit to
        // this method (the clamp-frame bookkeeping is deliberately not shared), so the trigger
        // branch would defer anyway; the early-out also stops the beam clamp fight (both writers
        // would otherwise re-aim Ray.UiHitOverride in the same frame).
        if (FigureStretch.Engaged(hand.Side))
            return;
        // Near reach-grab (a highlighted figure in the palm) belongs to the ProximityGrabber;
        // the far pluck only runs when the grabber is idle this frame.
        if (hand.Grabber.Held != null || hand.Grabber.Highlighted != null)
            return;
        if (!hand.Ray.TryGetPick(out PickPose pick) || !pick.HasHit || pick.HitCollider == null)
            return;

        CInteractableActor interactable = pick.HitCollider.GetComponentInParent<CInteractableActor>();
        if (interactable == null
            || !_adoptions.TryGetValue(interactable, out Adopted adopted)
            || !adopted.Grabbable.CanGrab)
            return;

        // ARBITRATION: a fan card / world-UI panel that clamped the beam this frame owns the
        // trigger. HasFreshUiHit is their signal; ignore our OWN figure clamp from last frame
        // (recorded below) so we never defer to ourselves.
        int myClampFrame = hand.Side == HandSide.Left ? _leftClampFrame : _rightClampFrame;
        bool foreignUi = hand.Ray.HasFreshUiHit && Time.frameCount - myClampFrame > 1;
        if (foreignUi)
            return;

        // Clamp the beam to the mini (reticle on the figure) AND suppress the board far-click /
        // game actor-select for this trigger press — the same UiHitOverride the fan laser uses.
        //
        // NO FRAME-ORDER MARKER, DELIBERATELY. This is the producer; consumers read it through
        // RayInteractor.HasFreshUiHit, on a different GameObject, so Unity's relative Update
        // order is undefined. That is by design: HasFreshUiHit's window is TWO frames
        // (`frameCount - _uiHitOverrideFrame <= 1`) PRECISELY so producer and consumer may sit
        // in either phase. Adding [DefaultExecutionOrder] to pin the phase would freeze an order
        // nothing relies on, and would quietly narrow that window's job to nothing.
        // See .planning/refactor/REVIEW-Hands-Board-Core.md §P2.
        hand.Ray.UiHitOverride = pick.HitPoint;
        if (hand.Side == HandSide.Left)
            _leftClampFrame = Time.frameCount;
        else
            _rightClampFrame = Time.frameCount;

        if (hand.TriggerDown)
        {
            // THE LASER IS A DELIBERATE AIM, so it is not subject to the proximity arbitration —
            // clear this hand's suppression on the target before plucking. It is load-bearing since
            // the pinch-radius gate: a figure can now be inside the palm reach with NO winner
            // elected, so the laser can reach a suppressed one, and ForceGrab consults the same
            // AllowsHand filter — without this it would refuse a pluck the player aimed at. Safe to
            // clear for exactly one frame: on the next one the holding branch of
            // SelectByOffsetAnchor elects THIS figure (it is the one in the hand) and vetoes every
            // other, so the clear cannot widen into a second grabbable figure.
            adopted.Grabbable.SetProximitySuppressed(hand.Side, false);
            hand.Grabber.ForceGrab(adopted.Grabbable, releaseOnTriggerUp: true);
        }
    }

    /// <summary>
    /// Item 3 — when a hand hovers over MULTIPLE figures, grab the one nearest the OFFSET ANCHOR
    /// (the point where the held mini appears, <c>GrabAnchor.TransformPoint(HeldOffsetFor(side))</c>)
    /// rather than nearest to the palm. We can't change <see cref="ProximityGrabber"/>'s palm-based
    /// metric, so instead we mark every figure EXCEPT the offset-anchor winner as suppressed for
    /// that hand (per-hand <see cref="IGrabbableHandFilter"/>). Runs every frame per hand, and the
    /// veto covers EVERY adopted figure, near or far — see <see cref="ApplySuppression"/> for the
    /// flashing-highlight defect that a distance-gated veto caused. The laser far-grab is untouched
    /// because it clears its own target's veto at the pluck (<see cref="TryLaserGrab"/>).
    ///
    /// <para>IT IS ALSO THE PICK VOLUME (user report 2026-08, accidental grabs): a candidate has to
    /// be within <see cref="FigureGrabConfig.PickRadiusRealMeters"/> of the pinch point or every
    /// figure is suppressed and nothing lights up. Doing it here rather than in
    /// <see cref="ProximityGrabber"/> is deliberate: that reach is 13 cm because a CARD is a
    /// hand-span wide, and narrowing it there would narrow the card fan with it.</para>
    /// </summary>
    private void TickOffsetAnchorSelect()
    {
        SelectByOffsetAnchor(VRHands.Left);
        SelectByOffsetAnchor(VRHands.Right);
    }

    private void SelectByOffsetAnchor(VRHand? hand)
    {
        if (_adoptions.Count == 0 || hand == null)
            return;

        // No usable hand (untracked) or already holding → there is no election to run, but the
        // veto still has to be WRITTEN this frame (see ApplySuppression: a frame in which a hand's
        // flags are not written is a frame in which the ProximityGrabber may highlight on stale
        // ones). So instead of clearing the veto — which would leave EVERY figure grabbable for a
        // hand that is not even tracked — we elect the figure this hand is HOLDING, if any, and
        // suppress the rest.
        //
        // Why the held figure has to stay allowed: ProximityGrabber.HealDeadHeld force-releases a
        // hold whose target starts refusing its holder (`AllowsHand`), so vetoing the mini in your
        // own hand would drop it with a warning. A held CARD (not one of ours) elects nobody, which
        // is also right — a hand with a card in it is not hovering a mini.
        if (!hand.HasPose || hand.Grabber.Held != null)
        {
            ApplySuppression(hand, hand.Grabber.Held as FigureGrabbable);
            LogElection(hand, null, 0f); // forget the candidate so re-entering it logs again
            // …and drop the hysteresis with it. A stale holder would otherwise keep the WIDE
            // exit ring across a grab or a tracking dropout, so the first figure met after it
            // would be admitted on the release radius instead of the reach radius.
            int idle = (int)hand.Side;
            _electedBySide[idle] = null;
            _pendingBySide[idle] = null;
            _dwellBySide[idle] = 0;
            return;
        }

        // HELD-FIGURE STRETCH capture: while this hand sits inside the stretch zone of the mini
        // in the OTHER hand (or is mid-gesture), it elects NOBODY — so the ProximityGrabber can
        // neither highlight nor grab a board figure standing behind the held mini, and the trigger
        // belongs to the gesture (FigureStretch clamps the beam, which already makes every
        // HasFreshUiHit consumer defer). Same two rules as the branch above: the veto is PUBLISHED
        // rather than skipped, and the hysteresis is dropped with it.
        if (FigureStretch.Engaged(hand.Side))
        {
            ApplySuppression(hand, null);
            LogElection(hand, null, 0f);
            int captured = (int)hand.Side;
            _electedBySide[captured] = null;
            _pendingBySide[captured] = null;
            _dwellBySide[captured] = 0;
            return;
        }

        Vector3 palm = hand.Rig.PalmCenter.position;
        Vector3 offsetAnchor = hand.Rig.GrabAnchor.TransformPoint(FigureGrabConfig.HeldOffsetFor(hand.Side));
        float reach = ReachMeters * hand.WorldScale;
        // THE PICK VOLUME, in REAL METRES AT THE HAND, converted to world units with the rig's own
        // lossyScale — the mod's zoom is a scale on the RIG, so "one real metre at the hand" is
        // `1 × hand.WorldScale` world units and nothing else. See FigureGrabConfig.PickRadiusRealMeters
        // for why a figure may not keep the interactor's card-sized 0.13 m palm reach.
        float pickWorld = FigureGrabConfig.PickRadiusRealMeters * hand.WorldScale;
        LogPickVolume(hand, pickWorld);

        // Pass 1: among figures within PALM reach (the grabber's own candidate set) AND inside the
        // pinch-radius volume, find the one nearest the OFFSET ANCHOR — the figure the user is
        // aiming the pinch at. The palm reach is the interactor's own gate and stays as the outer
        // filter (a figure it never considers can never be highlighted anyway); the pinch radius is
        // the tight one that decides what the player can actually pick up.
        // ---- NO FLASHING (user, ModBuild 105) -------------------------------------------------
        //
        // "das highlighting blitzt immer mal wieder auf bei verschiedenen Figuren, obwohl ich nach
        // deinem letzten fix zu weit weg sein sollte. Wenn ich mit der Hand richtig zu den Figuren
        // gehe ist es auch wie ich es will - verhindere dieses 'Aufblitzen'."
        //
        // The hardware log says he was NOT too far away — it says he was exactly ON the line:
        // "'Actor(Clone)' at 39 mm real from the pinch point (radius 40 mm)", then 38, then 37.
        // A bare radius is a step function, so a hand drifting along the boundary crosses it many
        // times a second, and each crossing is one highlight. Two mechanisms, because the report
        // describes two different flashes and one lever cannot answer both:
        //
        //   ENTER/EXIT (a Schmitt trigger, PickExitFactor) kills the chatter of ONE figure at the
        //   boundary: the winner has to come inside the configured radius, but it only LOSES the
        //   election past a wider exit radius. Between the two the answer is whatever it already
        //   was, so drift cannot toggle it.
        //
        //   DWELL (PickDwellFrames) kills the sweep across SEVERAL figures ("bei verschiedenen
        //   Figuren"): a NEW candidate must hold the election for a few consecutive frames before
        //   it is allowed to light up. Sweeping a hand across the board never clears that, because
        //   each figure owns the nearest slot for only a frame or two on the way past.
        //
        // Neither weakens the deliberate grab the user says already works: he ends up INSIDE the
        // radius and STAYS there, which is precisely the case both mechanisms are built to pass.
        float exitWorld = pickWorld * PickExitFactor;
        int side = (int)hand.Side;
        FigureGrabbable? held = _electedBySide[side];
        FigureGrabbable? winner = null;
        float bestAnchorDist = float.MaxValue;

        // TURN-DEADLOCK GATE, the LEGIBLE half. A busy figure is simply not a candidate (CanGrab is
        // false, like the MP grab-lock), so nothing lights up and the pinch does nothing — which on
        // its own reads as "the grab is broken". So on a TRIGGER EDGE ONLY we note the busy figure
        // the player was actually reaching for and answer it the way this project already answers a
        // refused click: the game's own invalid-option item through GameAudio, plus one log line.
        // Zero per-frame cost — the extra work happens on the frames the trigger goes down and on
        // no others, and only until the first busy figure inside the pick volume is found.
        bool scanRefusal = hand.TriggerDown;
        FigureGrabbable? refusedBusy = null;
        string refusedWhy = string.Empty;

        // REACH MISS (boss-dragon round, ModBuild 291). The report that produced this was "wenn ich
        // den Drachen nehmen will kommt kein Overlay/Highlight … ich muss es am unteren Bereich tun"
        // — a trigger pull that elected NOTHING, which until now left no trace whatsoever: the
        // election line only ever printed a WINNER, so "I reached for it and nothing happened" was
        // the one outcome this subsystem could not describe. Tracked on trigger frames only.
        Adopted? nearestMiss = null;
        float nearestMissDist = float.MaxValue;

        foreach (Adopted adopted in _adoptions.Values)
        {
            Collider collider = adopted.Collider;
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;
            // THE GAME KEEPS ITS VETO. `collider` above may be the mod's reach extension, which is
            // ours to enable and never gets switched off by the game — so a figure whose authored
            // pick collider the game DISABLES (its own "this cannot be clicked right now") would
            // otherwise stay grabbable through our volume. The extension may add reach; it may not
            // add permission. Object-active state needs no second check: the volume is parented
            // under the game collider's transform and inherits it.
            if (adopted.Reach != null && (adopted.GameCollider == null || !adopted.GameCollider.enabled))
                continue;
            if (!adopted.Grabbable.CanGrab)
            {
                if (scanRefusal && refusedBusy == null
                    && FigureBusy.IsBusy(adopted.Grabbable.Actor, out string busyWhy)
                    && Vector3.Distance(offsetAnchor, collider.ClosestPoint(offsetAnchor)) <= pickWorld)
                {
                    refusedBusy = adopted.Grabbable;
                    refusedWhy = busyWhy;
                }
                continue;
            }
            if (scanRefusal)
            {
                // BEFORE the palm gate on purpose: the miss probe must be able to name a figure
                // the palm reach itself excluded, which is exactly the case a player reaching for
                // a boss's head is in.
                float missDist = Vector3.Distance(offsetAnchor, collider.ClosestPoint(offsetAnchor));
                if (missDist < nearestMissDist)
                {
                    nearestMissDist = missDist;
                    nearestMiss = adopted;
                }
            }
            if (Vector3.Distance(palm, collider.ClosestPoint(palm)) > reach)
                continue; // not a proximity candidate this frame
            float anchorDist = Vector3.Distance(offsetAnchor, collider.ClosestPoint(offsetAnchor));
            // The figure that already holds the election keeps it out to the EXIT ring; anything
            // else has to come inside the configured radius to take it.
            float admit = ReferenceEquals(adopted.Grabbable, held) ? exitWorld : pickWorld;
            if (anchorDist > admit)
                continue; // in the palm's reach, but not in the PINCH — you have to reach for a mini
            if (anchorDist < bestAnchorDist)
            {
                bestAnchorDist = anchorDist;
                winner = adopted.Grabbable;
            }
        }

        // DWELL. A candidate that is not the one already lit has to be asked for
        // PickDwellFrames consecutive frames before it becomes the winner; until then the
        // PREVIOUS winner stands (or nothing does). Counted per hand, reset by any change of
        // candidate, so the count can never be accumulated by two figures alternating.
        if (!ReferenceEquals(winner, held))
        {
            if (!ReferenceEquals(winner, _pendingBySide[side]))
            {
                _pendingBySide[side] = winner;
                _dwellBySide[side] = 1;
            }
            else
            {
                _dwellBySide[side]++;
            }
            // Letting GO is immediate — only ARRIVING has to be earned. A hand that has genuinely
            // left every figure must stop highlighting in the same frame, or the release would
            // itself become a lag the player feels as stickiness.
            if (winner != null && _dwellBySide[side] < PickDwellFrames)
                winner = held;
        }
        else
        {
            _pendingBySide[side] = winner;
            _dwellBySide[side] = PickDwellFrames;
        }
        _electedBySide[side] = winner;
        LogElection(hand, winner, bestAnchorDist);

        ApplySuppression(hand, winner);

        if (refusedBusy != null && winner == null)
            NoteBusyRefusal(hand, refusedBusy, refusedWhy);
        else if (scanRefusal && winner == null && nearestMiss != null)
            NoteReachMiss(hand, nearestMiss, nearestMissDist, offsetAnchor);
    }

    /// <summary>
    /// WHY THAT PINCH GRABBED NOTHING — written on a trigger edge that elected no figure, once per
    /// second per hand.
    ///
    /// <para>This is the instrument the boss-dragon report needed and did not have. The user's (b)
    /// and (c) are one sentence apart — "no highlight appears" and "it only works on the lower
    /// part" — and the hardware log for ModBuild 290 contains a dozen SUCCESSFUL highlights and
    /// grabs on the very same <c>ElderDrakeID</c>, so "the boss cannot be highlighted" was already
    /// false. What no line could say is where the hand WAS on the pulls that did nothing.</para>
    ///
    /// <para>So the line states the geometry that decides it: the distance from the pinch to the
    /// nearest figure's PICK COLLIDER against the same radius the election gates on, the distance
    /// to that figure's RENDERED body for comparison, and — the field the report is actually about
    /// — whether the pinch was ABOVE the top of that collider. A pinch above the collider can never
    /// elect the figure however close it looks to the miniature, and that is a property of the
    /// game's authored collider, not of the pick radius.</para>
    /// </summary>
    private void NoteReachMiss(VRHand hand, Adopted nearest, float anchorDistWorld, Vector3 pinch)
    {
        Collider collider = nearest.Collider;
        GameObject body = nearest.Figure;
        if (collider == null)
            return;
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);

        // ── THE GATE THIS PROBE SHIPPED WITHOUT, AND WHY 192 LINES SAID NOTHING ────────────
        //
        // In the ModBuild 291 hardware log this probe fired 192 times and every single line was
        // noise. Not one named the boss. The nearest figure was between 406 mm and 2 424 mm real
        // from the pinch — ten to sixty times the 40 mm pick radius — because the probe fires on
        // ANY trigger pull that elects nothing, and a player pressing the trigger for a card, a
        // panel or a teleport is not reaching for a miniature. Worse, 190 of the 192 printed
        // "ABOVE its top … a pinch above the collider can NEVER elect this figure", which is
        // literally true and completely irrelevant about a hand two metres away: the probe was
        // asserting the round's leading hypothesis on evidence that only supported "nothing was
        // near the hand". An instrument that agrees with you at two metres will agree with you
        // about anything.
        //
        // So the gate is the question the probe is FOR: did the hand reach into the figure the
        // player can SEE? Measured against the figure's own drawn surface, not its collider —
        // gating on the collider would re-import the very under-coverage being diagnosed. The
        // bar is the palm reach, the outer gate the election itself uses. Every one of the 192
        // would have been suppressed by it, and a genuine reach for the boss's head measures 0.
        //
        // The suppressed pulls are COUNTED, not discarded: "a truncated list is not absence", and
        // the count rides the next line that does print.
        float reachWorld = ReachMeters * hand.WorldScale;
        float minY = 0f;
        float maxY = 0f;
        float nearestSurface = float.MaxValue;
        int used = 0;
        if (body != null)
            used = MeasureBody(body, pinch, includePinch: true, out minY, out maxY,
                               out nearestSurface, out _, out _, out _);
        if (used == 0 || nearestSurface > reachWorld)
        {
            _reachMissSuppressed++;
            return;
        }

        bool left = hand.Side == HandSide.Left;
        float next = left ? _nextReachMissLeft : _nextReachMissRight;
        float now = Time.unscaledTime;
        if (now < next)
            return;
        if (left)
            _nextReachMissLeft = now + BusyRefusalIntervalSeconds;
        else
            _nextReachMissRight = now + BusyRefusalIntervalSeconds;

        Bounds cb = collider.bounds;
        // HOW FAR OFF TO THE SIDE. Without this a reader cannot tell "the player reached over the
        // top of the mini" from "the player's hand was somewhere else entirely and happened to be
        // higher", and the old line could not either.
        float dx = pinch.x - cb.center.x;
        float dz = pinch.z - cb.center.z;
        float lateral = Mathf.Sqrt(dx * dx + dz * dz);

        string vertical =
            pinch.y > cb.max.y
                ? $"ABOVE the top of the pick volume by {(pinch.y - cb.max.y):F2} wu"
                  + $"{HexOf(pinch.y - cb.max.y)} — the height is what refused this pinch"
                : pinch.y < cb.min.y
                    ? $"BELOW its bottom by {(cb.min.y - pinch.y):F2} wu{HexOf(cb.min.y - pinch.y)}"
                    : "INSIDE its vertical span, so height is not what refused this pinch";

        // WHICH VOLUME THIS IS. On a figure the mod extended, the numbers above are the MOD's
        // capsule and not the game's — saying so is the difference between reading this line as
        // "the fix is not in" and "the fix is in and something else refused the pinch".
        Collider game = nearest.GameCollider;
        string volume = nearest.Reach != null && game != null
            ? $"a MOD-EXTENDED reach capsule (the game's own {game.GetType().Name} on "
              + $"'{game.name}' stops at y {game.bounds.max.y:F2})"
            : $"the game's own {collider.GetType().Name} on '{collider.name}'";

        string suppressed = _reachMissSuppressed > 0
            ? $" ({_reachMissSuppressed} earlier trigger pull(s) this session were NOT reported: "
              + "no figure's drawn body was within the palm reach, i.e. the hand was not reaching "
              + "for a miniature at all.)"
            : string.Empty;

        float bodyHeight = maxY - minY;
        VRLog.Info("FigureGrab",
            $"REACHED AND MISSED ({hand.Side}): the trigger went down and NO figure was elected. "
            + $"Nearest is '{nearest.Grabbable.Label}', {nearestSurface / scale * 1000f:F0} mm real "
            + $"from its DRAWN SURFACE and {anchorDistWorld / scale * 1000f:F0} mm from its pick "
            + $"volume (pick radius {FigureGrabConfig.PickRadiusRealMeters * 1000f:F0} mm). The "
            + $"volume is {volume}, world y {cb.min.y:F2}..{cb.max.y:F2}; the pinch was at y "
            + $"{pinch.y:F2} and {lateral:F2} wu{HexOf(lateral)} off its axis — {vertical}. "
            + $"RENDERED BODY ({used} mesh renderer(s)): world y {minY:F2}..{maxY:F2}, height "
            + $"{bodyHeight:F2} wu{HexOf(bodyHeight)}.{suppressed}");
    }

    /// <summary>
    /// The figure's drawn extent, as NUMBERS. Mesh and skinned renderers only, for the same reason
    /// <c>ActorBars</c> excludes the rest: a particle or trail renderer reports an effect VOLUME
    /// and says nothing about where the miniature is.
    ///
    /// <para>Split out of <see cref="DescribeRenderedBody"/> because the reach volume needs the
    /// same measurement and must not get it from a string. Returns the renderer count; 0 means
    /// nothing usable was found and the out values are meaningless.</para>
    ///
    /// <para><b>THE VERTICAL EXTENT IS LIVE SINCE ModBuild 294</b> — bone transforms for skinned
    /// meshes, own transform for props (<see cref="FigureBody.TryLiveExtentY"/>). <paramref
    /// name="boxMinY"/>/<paramref name="boxMaxY"/> carry the BAKED <c>Renderer.bounds</c> union
    /// beside it, for the log line only: it is what this subsystem used to decide with, and the
    /// ModBuild 293 log showed it is not the figure (a drake whose baked box never moved while it
    /// flew 1.5 wu into the air). <paramref name="liveBones"/> is the number of bone transforms
    /// that produced the live answer — 0 on a figure whose skinned renderers have no bone array,
    /// the one state in which this degrades back to the stale box.</para>
    ///
    /// <para>The nearest-SURFACE distance still comes from the baked box, and deliberately: it is a
    /// diagnostic distance to a volume, <c>Bounds.ClosestPoint</c> is the only cheap way to get
    /// one, and a bone cloud has no surface to close on.</para>
    /// </summary>
    private static int MeasureBody(
        GameObject body, Vector3 pinch, bool includePinch,
        out float minY, out float maxY, out float nearestSurface,
        out float boxMinY, out float boxMaxY, out int liveBones)
    {
        BodyScratch.Clear();
        body.GetComponentsInChildren(includeInactive: false, BodyScratch);
        maxY = float.MinValue;
        minY = float.MaxValue;
        boxMaxY = float.MinValue;
        boxMinY = float.MaxValue;
        nearestSurface = float.MaxValue;
        liveBones = 0;
        int used = 0;
        for (int i = 0; i < BodyScratch.Count; i++)
        {
            Renderer r = BodyScratch[i];
            if (r.isPartOfStaticBatch || (r is not MeshRenderer && r is not SkinnedMeshRenderer))
                continue;
            Bounds b = r.bounds;
            if (b.max.y > boxMaxY) boxMaxY = b.max.y;
            if (b.min.y < boxMinY) boxMinY = b.min.y;
            if (includePinch)
            {
                float d = Vector3.Distance(pinch, b.ClosestPoint(pinch));
                if (d < nearestSurface) nearestSurface = d;
            }
            if (!FigureBody.TryLiveExtentY(r, out float rMinY, out float rMaxY, out int bones))
                continue;
            liveBones += bones;
            if (rMaxY > maxY) maxY = rMaxY;
            if (rMinY < minY) minY = rMinY;
            used++;
        }
        BodyScratch.Clear();
        // A degenerate live extent (one bone, or every bone at one height) leaves the caller with
        // nothing to reason about; hand back the baked box in that case and let the count stand, so
        // the failure is a number the log prints rather than a silent zero-height figure.
        if (used > 0 && maxY - minY <= 1e-3f && boxMaxY - boxMinY > 1e-3f)
        {
            minY = boxMinY;
            maxY = boxMaxY;
        }
        return used;
    }

    /// <summary>
    /// Build, resize or leave alone this figure's mod-owned reach extension. See the REACH
    /// EXTENSION block beside <see cref="ReachExtensionMinGapFraction"/> for the whole argument.
    ///
    /// <para>Strictly additive: it can only ever make a figure reachable HIGHER, never wider,
    /// never lower, and never at all for a figure the game's own collider already covers. Every
    /// figure in the ModBuild 291 hardware log except <c>ElderDrakeID</c> leaves this method with
    /// <c>adopted.Collider</c> still pointing at the game's collider and no GameObject created.
    /// </para>
    /// </summary>
    private void SizeReachVolume(Adopted adopted)
    {
        Collider game = adopted.GameCollider;
        GameObject figure = adopted.Figure;
        if (game == null || figure == null)
            return;

        Bounds cb = game.bounds;
        int meshes = MeasureBody(figure, Vector3.zero, includePinch: false,
                                 out float minY, out float maxY, out _,
                                 out float boxMinY, out float boxMaxY, out int liveBones);
        if (meshes == 0)
            return;

        // THE FIGURE'S LIVE TOP, raised to the head joint if the skeleton did not reach it. The
        // ModBuild 292 slack correction that used to stand here — "subtract however far the baked
        // box reaches below the figure's own base" — is GONE: ModBuild 293's own falsifier field
        // showed that on all three drakes the lowest and the tallest renderer are the same object,
        // so the underhang and the top are two corners of ONE authored box. See FigureBody.
        float top = FigureBody.LiveTopY(
            maxY,
            adopted.HeadBone != null ? adopted.HeadBone.position.y : 0f,
            headKnown: adopted.HeadBone != null,
            out _);

        float bodyHeight = Mathf.Max(maxY - minY, 1e-3f);
        float gap = top - cb.max.y;
        if (gap <= ReachExtensionMinGapFraction * bodyHeight)
        {
            // The game's collider already covers this figure. If we built a volume earlier (a
            // figure that shrank, or an adoption taken mid-assembly), hand the election back to
            // the game's collider and drop ours -- the mod owns no reach it cannot justify.
            if (adopted.Reach != null)
            {
                VRLog.Info("FigureGrab",
                    $"FIGURE REACH EXTENSION DROPPED '{adopted.Grabbable.Label}': the figure's "
                    + $"trusted top is now {top:F2} wu against a pick collider that reaches "
                    + $"{cb.max.y:F2} wu, so the game's own volume covers it again.");
                DestroyReachVolume(adopted);
                adopted.Collider = game;
                adopted.Grabbable.SetPickVolume(game);
                VRInteractables.RegisterGrabbable(adopted.Grabbable, game);
            }
            return;
        }

        // ── ONLY AN UPRIGHT CAPSULE IS EXTENDED, AND THAT IS A REFUSAL, NOT AN OVERSIGHT ──
        // The whole safety of this fix is "same horizontal profile, taller" -- it is what makes
        // the extension unable to widen a figure, steal a neighbour's election, or make anything
        // easier to grab from the side. That guarantee exists only because the game's collider is
        // a Y-axis capsule, whose world AABB is exactly 2r x h x 2r under the yaw-only rotation an
        // upright mini has: the extension can then copy its radius EXACTLY. Every one of the five
        // figures measured on hardware is such a capsule.
        //
        // For any other shape there is no radius to copy and every substitute is wrong in a way
        // that matters: an inscribed cylinder would make the figure HARDER to grab from the side
        // than the game made it, and a circumscribed one would make it easier and could reach into
        // the next hex. So an unmeasured shape gets no extension and says so once. A figure that
        // is not grabbable high up is a smaller defect than one whose reach the mod silently
        // reshaped on a guess.
        if (game is not CapsuleCollider gameCapsule || gameCapsule.direction != 1)
        {
            if (!adopted.ReachShapeRefused)
            {
                adopted.ReachShapeRefused = true;
                VRLog.Info("FigureGrab",
                    $"FIGURE REACH NOT EXTENDED '{adopted.Grabbable.Label}': its pick collider is "
                    + $"a {game.GetType().Name} on '{game.name}', not the upright CapsuleCollider "
                    + $"every measured figure carries, and the extension may only ever copy a "
                    + $"radius it can read exactly. The figure is reachable to y {cb.max.y:F2} "
                    + $"while it is drawn to y {top:F2} (trusted) / {maxY:F2} (raw box) -- "
                    + $"{gap:F2} wu{HexOf(gap)} of it cannot be grabbed. If this line ever appears "
                    + "on hardware, the shape it names is what the next round has to handle.");
            }
            return;
        }

        // Same axis, same radius, same centre in X and Z as the game's capsule -- only taller.
        float bottomWorld = cb.min.y;
        Transform frame = game.transform;
        // ONE SCALE FACTOR, because a figure only ever has one. Unity scales a Y-axis capsule's
        // radius by max(|sx|,|sz|) and its height by |sy|; taking the largest component of the
        // lossy scale is exactly right while those agree, and figures are uniformly scaled by both
        // the game and by FigureStretch's held-size latch. A non-uniformly scaled figure would get
        // a volume slightly too large on the squashed axis -- generous in the direction that costs
        // nothing here, since the extension may only add reach.
        Vector3 lossy = frame.lossyScale;
        float unit = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Max(Mathf.Abs(lossy.y), Mathf.Abs(lossy.z)));
        if (unit < 1e-4f)
            return;

        bool created = adopted.Reach == null;
        if (created)
        {
            var host = new GameObject(ReachVolumeName) { layer = IgnoreRaycastLayer };
            // worldPositionStays:false -- the volume is defined in the collider's own frame, so it
            // rides every move, turn and (uniform) rescale the figure makes without a per-frame
            // writer. That is also why a figure that finishes scaling up AFTER this runs is safe:
            // the body and the volume scale by the same factor.
            host.transform.SetParent(frame, worldPositionStays: false);
            host.transform.localPosition = Vector3.zero;
            host.transform.localRotation = Quaternion.identity;
            host.transform.localScale = Vector3.one;
            var capsule = host.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y
            capsule.isTrigger = true;
            adopted.Reach = new ReachVolume { Host = host, Capsule = capsule };
        }

        ReachVolume reach = adopted.Reach!;
        if (reach.Host == null || reach.Capsule == null)
            return;
        Vector3 worldCenter = new(cb.center.x, 0.5f * (bottomWorld + top), cb.center.z);
        reach.Capsule.center = frame.InverseTransformPoint(worldCenter);
        // The game capsule's OWN radius, in the same local units -- copied, never re-derived.
        reach.Capsule.radius = gameCapsule.radius;
        reach.Capsule.height = (top - bottomWorld) / unit;
        float was = reach.TopY;
        reach.TopY = top;

        if (created)
        {
            adopted.Collider = reach.Capsule;
            adopted.Grabbable.SetPickVolume(reach.Capsule);
            VRInteractables.RegisterGrabbable(adopted.Grabbable, reach.Capsule);
            VRLog.Info("FigureGrab",
                $"FIGURE REACH EXTENDED '{adopted.Grabbable.Label}': the game's "
                + $"{game.GetType().Name} on '{game.name}' reaches y {cb.min.y:F2}..{cb.max.y:F2} "
                + $"while the figure's LIVE extent ({liveBones} bone(s)) is y {minY:F2}..{maxY:F2} "
                + $"and its top, raised to the head joint where the skeleton falls short, is "
                + $"{top:F2}. Its BAKED Renderer.bounds box — which decides nothing since "
                + $"ModBuild 294 and is printed only for comparison — is y {boxMinY:F2}.."
                + $"{boxMaxY:F2}. A mod-owned trigger capsule on the Ignore Raycast layer now covers y "
                + $"{bottomWorld:F2}..{top:F2} at the game capsule's OWN radius "
                + $"({gameCapsule.radius * unit:F2} wu) and its own X/Z centre -- taller only, "
                + $"never wider. Reach above the old collider top gains "
                + $"{gap:F2} wu{HexOf(gap)}; the pick radius is unchanged at "
                + $"{FigureGrabConfig.PickRadiusRealMeters * 1000f:F0} mm real.");
        }
        else if (Mathf.Abs(top - was) > ReachExtensionMinGapFraction * bodyHeight)
        {
            VRLog.Info("FigureGrab",
                $"FIGURE REACH RESIZED '{adopted.Grabbable.Label}': trusted top {was:F2} -> "
                + $"{top:F2} wu (the figure was still assembling when it was first measured); the "
                + $"volume now covers y {bottomWorld:F2}..{top:F2}.");
        }
    }

    /// <summary>
    /// Re-measure every extended-or-extendable figure while its budget lasts. Off the critical
    /// path in every sense: it runs at most 8 times per figure over the 4 s after adoption, at
    /// 0.5 s intervals, never for a HELD figure (a mini tilted in the hand has an AABB that is not
    /// about the mini), and never again after that.
    /// </summary>
    private void TickReachVolumes()
    {
        float now = Time.unscaledTime;
        foreach (Adopted adopted in _adoptions.Values)
        {
            if (adopted.ReachSamplesLeft <= 0 || now < adopted.NextReachSample)
                continue;
            if (adopted.Grabbable.IsHeld)
                continue;
            adopted.ReachSamplesLeft--;
            adopted.NextReachSample = now + ReachSampleIntervalSeconds;
            SizeReachVolume(adopted);
        }
    }

    /// <summary>Destroy this figure's mod-owned reach volume, if it has one. Idempotent; safe on a
    /// figure whose hierarchy has already gone.</summary>
    private static void DestroyReachVolume(Adopted adopted)
    {
        ReachVolume? reach = adopted.Reach;
        adopted.Reach = null;
        if (reach == null || reach.Host == null)
            return;
        Destroy(reach.Host);
    }

    /// <summary>
    /// The figure's own SURFACE geometry next to the collider that gates the pick — mesh and
    /// skinned renderers only, for the same reason <c>ActorBars</c> excludes the rest: a particle
    /// or trail renderer reports an effect VOLUME and says nothing about where the miniature is.
    /// Reports the coverage as a percentage and as the gap at the top, because "the collider stops
    /// here and the mini goes on to there" is the whole of symptom (c).
    /// </summary>
    private static string DescribeRenderedBody(
        GameObject body, Bounds colliderBounds, float scale, Vector3 pinch, bool includePinch)
    {
        int used = MeasureBody(body, pinch, includePinch,
                               out float minY, out float maxY, out float nearestSurface,
                               out float boxMinY, out float boxMaxY, out int liveBones);
        if (used == 0)
            return "RENDERED BODY: no mesh renderer found on the figure.";

        float bodyHeight = maxY - minY;
        float coverage = bodyHeight > 1e-4f
            ? Mathf.Clamp01((Mathf.Min(colliderBounds.max.y, maxY) - Mathf.Max(colliderBounds.min.y, minY))
                            / bodyHeight) * 100f
            : 0f;
        float gap = maxY - colliderBounds.max.y;
        string surface = includePinch
            ? $", nearest surface {nearestSurface / scale * 1000f:F0} mm real from the pinch"
            : string.Empty;
        return $"RENDERED BODY ({used} mesh renderer(s), {liveBones} live bone(s)): world y "
               + $"{minY:F2}..{maxY:F2}, height {bodyHeight:F2} wu{HexOf(bodyHeight)}{surface} "
               + $"— measured LIVE; its BAKED Renderer.bounds box, which decides nothing and is "
               + $"printed only so the two can be compared, is y {boxMinY:F2}..{boxMaxY:F2}. "
               + $"COVERAGE: the pick collider spans {coverage:F0}% of that height and its top "
               + $"sits {gap:F2} wu{HexOf(gap)} below the rendered top.";
    }

    /// <summary>
    /// ONE LINE PER FIGURE ADOPTION: the volume the player may actually reach into, next to the
    /// figure they can see.
    ///
    /// <para>The pick election measures against ONE collider — <c>CInteractableActor</c>'s own, or
    /// the FIRST one found under it — because that is the collider the flat game authored for a
    /// mouse click. Whether that volume covers the whole miniature has never been checked by
    /// anything, and on an ordinary humanoid mini it does not matter. On a boss it decides whether
    /// half the figure is inert.</para>
    ///
    /// <para><b>THE COUNT WAS NOT ENOUGH, AND THAT COST THIS ROUND A FIX.</b> ModBuild 291's
    /// version of this line printed "the figure carries 1 collider(s) under its CInteractableActor
    /// and 4 under the actor root; ONLY THE FIRST is used by the pick" — and the round that read
    /// it was expected to decide, from those two integers, whether electing across all of a
    /// figure's colliders was the fix. It could not: a count says nothing about what a collider
    /// IS, where it sits, or how big it is, and "4" is equally consistent with four body volumes
    /// and with four aggro triggers a metre wide. A summary stat is not the field. So the line now
    /// NAMES every collider the figure carries — type, object, world y span, size, trigger flag,
    /// and whether it is inside the CInteractableActor subtree the pick searches. The list is
    /// capped, and when it is capped it says by how much, because a truncated list is not
    /// absence.</para>
    /// </summary>
    private static void LogFigureReach(
        FigureGrabbable grabbable, CInteractableActor interactable, GameObject figure, Collider collider)
    {
        Bounds cb = collider.bounds;

        ColliderScratch.Clear();
        interactable.GetComponentsInChildren(includeInactive: true, ColliderScratch);
        int underInteractable = ColliderScratch.Count;
        InteractableColliders.Clear();
        for (int i = 0; i < ColliderScratch.Count; i++)
            InteractableColliders.Add(ColliderScratch[i]);
        ColliderScratch.Clear();
        figure.GetComponentsInChildren(includeInactive: true, ColliderScratch);
        int underFigure = ColliderScratch.Count;

        var roll = new System.Text.StringBuilder(256);
        int listed = 0;
        for (int i = 0; i < ColliderScratch.Count; i++)
        {
            Collider c = ColliderScratch[i];
            if (c == null || c.gameObject.name == ReachVolumeName)
                continue;   // never report our own volume back as evidence about the game's prefab
            if (listed >= ColliderRollCap)
                break;
            Bounds b = c.bounds;
            roll.Append(listed == 0 ? " THEY ARE: " : "; ");
            roll.Append(ReferenceEquals(c, collider) ? "[USED] " : string.Empty)
                .Append(c.GetType().Name).Append(" on '").Append(c.gameObject.name).Append("' y ")
                .Append(b.min.y.ToString("F2")).Append("..").Append(b.max.y.ToString("F2"))
                .Append(" size (").Append(b.size.x.ToString("F2")).Append(',')
                .Append(b.size.y.ToString("F2")).Append(',').Append(b.size.z.ToString("F2"))
                .Append(')');
            if (c.isTrigger)
                roll.Append(" TRIGGER");
            if (!c.enabled)
                roll.Append(" disabled");
            roll.Append(InteractableColliders.Contains(c)
                ? " [inside CInteractableActor]"
                : " [OUTSIDE CInteractableActor — the pick search never reaches it]");
            listed++;
        }
        if (listed >= ColliderRollCap && underFigure > listed)
            roll.Append("; … ").Append(underFigure - listed).Append(" more NOT listed");
        roll.Append('.');
        ColliderScratch.Clear();
        InteractableColliders.Clear();

        string rendered = DescribeRenderedBody(figure, cb, 1f, cb.center, includePinch: false);

        VRLog.Info("FigureGrab",
            $"FIGURE REACH '{grabbable.Label}': pick collider is a {collider.GetType().Name} on "
            + $"'{collider.name}' — world y {cb.min.y:F2}..{cb.max.y:F2}, size ({cb.size.x:F2}, "
            + $"{cb.size.y:F2}, {cb.size.z:F2}) wu. The figure carries {underInteractable} "
            + $"collider(s) under its CInteractableActor and {underFigure} under the actor root; "
            + $"ONLY THE FIRST is used by the pick.{roll} {rendered} Pick radius "
            + $"{FigureGrabConfig.PickRadiusRealMeters * 1000f:F0} mm real at the hand.");
    }

    /// <summary>A world-unit length re-stated in HEX WIDTHS, or nothing when the game's tile size
    /// is not resolvable — the line never invents a number.</summary>
    private static string HexOf(float worldUnits)
    {
        float hex = UnityGameEditorRuntime.s_TileSize.x;
        return hex > 1e-4f ? $" ({worldUnits / hex:F2} hex)" : string.Empty;
    }

    /// <summary>
    /// Say NO out loud when a pinch lands on a figure the turn machine is currently depending on
    /// (<see cref="FigureBusy"/>). Uses the project's EXISTING refusal recipe rather than a new one:
    /// the game's own invalid-option audio item
    /// (<c>UIInfoTools.generalAudioButtonProfile.nonInteractableMouseDownAudioItem</c>) played
    /// listener-anchored through <see cref="Core.GameAudio"/>, falling back to the game's negative
    /// ping — exactly the chain <c>WorldUI/Surfaces/InitiativePortraitClickSound</c> resolved for the
    /// initiative-track refusal (see that file for why the positional overload is silent in VR).
    ///
    /// <para>Throttled to once per second per hand, like <c>ProximityGrabber.LogRefusal</c>, so a
    /// player mashing the trigger at a resolving enemy hears one clear "no" and not a rattle.
    /// Strict no-op offline and online alike: it plays a local sound and writes a local line.</para>
    /// </summary>
    private void NoteBusyRefusal(VRHand hand, FigureGrabbable figure, string why)
    {
        bool left = hand.Side == HandSide.Left;
        float next = left ? _nextBusyRefusalLeft : _nextBusyRefusalRight;
        if (Time.unscaledTime < next)
            return;
        if (left)
            _nextBusyRefusalLeft = Time.unscaledTime + BusyRefusalIntervalSeconds;
        else
            _nextBusyRefusalRight = Time.unscaledTime + BusyRefusalIntervalSeconds;

        // Read field-by-field, never through UIInfoTools.InvalidOptionAudioItem: that property is an
        // unchecked `generalAudioButtonProfile.nonInteractableMouseDownAudioItem` and NREs before a
        // scene has assigned the profile (UIInfoTools.cs:467).
        UIInfoTools tools = UIInfoTools.Instance;
        AudioButtonProfile? general = tools != null ? tools.generalAudioButtonProfile : null;
        string preferred = general != null ? general.nonInteractableMouseDownAudioItem ?? string.Empty : string.Empty;
        BusyRefusalFallbacks[0] = NegativePingFallback;
        bool played = Core.GameAudio.PlayListenerAnchored(preferred, BusyRefusalFallbacks,
                                                          out string item, out bool valid, out string note);

        VRLog.Info("FigureGrab",
            $"{hand.Side} grab REFUSED on {figure.Label} — {why}. This is the turn-deadlock gate "
            + "(user report 2026-08-11): a figure the game is waiting on may not be picked up, "
            + "because hiding its actor bar under the hand kills the very coroutine the "
            + "choreographer's untimed wait needs. Refusal sound: item "
            + $"'{item}' valid={valid} played={played}{note}.");
    }

    /// <summary>
    /// Pass 2 — publish this frame's election as a per-hand veto: EVERY adopted figure except
    /// <paramref name="winner"/> refuses <paramref name="hand"/> (<see cref="FigureGrabbable.AllowsHand"/>),
    /// so <see cref="ProximityGrabber"/> can only ever highlight and grab the one figure this driver
    /// elected.
    ///
    /// ---- WHY THIS IS UNCONDITIONAL (user, ModBuild 106) -------------------------------------
    ///
    /// "Wenn ich meine Hand über die Spielfiguren halte werden sie zwar nicht mehr dauerhaft
    /// ausgewählt sondern das highlighting blitzt immer mal wieder auf bei verschiedenen Figuren,
    /// obwohl ich nach deinem letzten fix zu weit weg sein sollte. Wenn ich mit der hand richtug zu
    /// den figuren gehe ist es auch wie ich es will - verhindere dieses 'Aufblitzen'."
    ///
    /// ROOT CAUSE, and it is NOT the election. This driver does not raise the highlight at all —
    /// <see cref="ProximityGrabber.UpdateHighlight"/> does, on the nearest registered grabbable
    /// inside its CARD-sized 13 cm palm reach that still allows the hand. The election is only a
    /// VETO, and it used to be published for palm-reach figures ONLY:
    ///
    ///     bool inReach = … ≤ reach;
    ///     SetProximitySuppressed(side, inReach &amp;&amp; !winner);   // ← everything else: NOT suppressed
    ///
    /// A figure outside the palm sphere was therefore actively written back to "allowed", and the
    /// grabber ticks in its own driver (Hands) — one frame's flags in arrears. So on the frame a
    /// figure CROSSED INTO the 13 cm sphere it was still un-vetoed, while every figure that had
    /// been in the sphere longer was already vetoed; the newcomer was thus the only ALLOWED
    /// candidate and won the grabber's "nearest" by default. One frame of amber glow on the figure
    /// at the far EDGE of the palm reach — repeating on figure after figure as a hovering hand
    /// drifts. That is "blitzt auf bei VERSCHIEDENEN Figuren", and it is why the hand felt "zu weit
    /// weg": the leak fires at the 130 mm palm reach, not at the 40 mm pick radius.
    ///
    /// The hardware log of ModBuild 106 (5f3e3807f) is unambiguous about the mechanism: 137
    /// "pre-grab highlight ENGAGED" lines against 9 "pinch candidate" elections. The hysteresis and
    /// the dwell added in 76daf29 stabilise the ELECTION, and the election was never what lit those
    /// 128 other figures.
    ///
    /// The veto is now written for every adopted figure on every frame a hand is ticked, so it can
    /// never be stale and it FAILS CLOSED: a figure is grabbable only because this driver said so
    /// THIS frame. That also makes the fix independent of the Update order between HandsDriver and
    /// this driver, which Unity does not define.
    ///
    /// REJECTED — shrinking [FigureGrab] PickRadiusMillimeters. The leak fires on the palm reach,
    /// so the dial the player would be told to turn is not the one in the causal chain; it would
    /// have made deliberate grabs harder and left the flashing exactly where it was.
    /// REJECTED — narrowing ProximityGrabber's 13 cm reach. That reach is a CARD's reach; the hand
    /// fan is measured in it (see FigureGrabConfig.PickRadiusRealMeters), and figures may not drag
    /// the card fan's ergonomics along behind them.
    /// REJECTED — pinning the two drivers with [DefaultExecutionOrder]. It would hide this bug
    /// behind an ordering the project explicitly refuses to freeze (see the note in TryLaserGrab),
    /// and the veto would still be one frame old the moment anything else moved.
    ///
    /// The far LASER grab is untouched: it clears its own target's veto immediately before
    /// <c>ForceGrab</c> (see <see cref="TryLaserGrab"/>, which already had to, and says why).
    /// </summary>
    private void ApplySuppression(VRHand hand, FigureGrabbable? winner)
    {
        foreach (Adopted adopted in _adoptions.Values)
            adopted.Grabbable.SetProximitySuppressed(hand.Side, !ReferenceEquals(adopted.Grabbable, winner));
    }

    /// <summary>
    /// THE NUMBERS BEHIND THE PICK VOLUME, printed whenever they change — the resolved radius in
    /// REAL millimetres at the hand, the world units that comes to at the current zoom, the zoom
    /// itself, and the width of one hex in the same real millimetres.
    ///
    /// <para>Written because "der Bereich ist zu groß" is unanswerable without them: the radius is
    /// a constant at the HAND and a variable on the BOARD, and those two readings of the same
    /// number are what the report and the code disagreed about. Hence the hex column —
    /// radius-in-hexes is the ratio the player actually sees next to a mini.</para>
    ///
    /// <para>Deduped on the formatted line, so it costs one line per zoom change or config edit —
    /// both of which are human acts — and nothing at all while the player just plays.</para>
    /// </summary>
    private void LogPickVolume(VRHand hand, float pickWorld)
    {
        float mm = FigureGrabConfig.PickRadiusRealMeters * 1000f;
        float scale = hand.WorldScale;
        float hexMm = scale > 1e-4f
            ? UnityGameEditorRuntime.s_TileSize.x / scale * 1000f
            : 0f;
        string line = $"PICK VOLUME: {mm:F0} mm real at the hand = {pickWorld:F2} world units "
                      + $"(rig world scale {scale:F2}); one hex is {hexMm:F0} mm real at this zoom, "
                      + $"so the volume spans {(hexMm > 1e-3f ? mm / hexMm : 0f):F2} hexes. Fixed at "
                      + "the hand — zooming the table changes the hex column, never the first.";
        if (line == _lastPickVolumeLog)
            return;
        _lastPickVolumeLog = line;
        VRLog.Info("FigureGrab", line);
    }

    /// <summary>
    /// One line per NEWLY elected pinch winner (per hand): which figure lit up and how far its own
    /// surface was from the pinch point, in the same real millimetres the dial is set in. This is
    /// the half that answers "it is STILL too big" — if a figure lights up at 38 mm the dial is
    /// simply set too wide, and if one lights up at 300 mm something else is wrong.
    /// </summary>
    private void LogElection(VRHand hand, FigureGrabbable? winner, float anchorDistWorld)
    {
        bool left = hand.Side == HandSide.Left;
        FigureGrabbable? last = left ? _lastElectedLeft : _lastElectedRight;
        if (ReferenceEquals(last, winner))
            return;
        if (left)
            _lastElectedLeft = winner;
        else
            _lastElectedRight = winner;
        if (winner == null)
            return; // losing the candidate is not news; only a new one carries a distance

        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        ActorBehaviour actor = winner.Actor;
        VRLog.Info("FigureGrab",
            $"{hand.Side} pinch candidate '{(actor != null ? actor.name : "?")}' at "
            + $"{anchorDistWorld / scale * 1000f:F0} mm real from the pinch point "
            + $"(radius {FigureGrabConfig.PickRadiusRealMeters * 1000f:F0} mm).");
    }

    // NOTE — there is deliberately no ClearSuppression(side) any more. It existed for the
    // "untracked hand / already holding" branch of SelectByOffsetAnchor, and clearing the veto for
    // a whole hand is precisely the state this round's defect was made of: the next frame the
    // ProximityGrabber ticks (it ticks first) it would see every figure allowed and light up the
    // nearest one inside its 13 cm palm reach. That branch now publishes a veto like any other —
    // see ApplySuppression, which also explains why the held figure is the winner there.

    private void Drop(Component key)
    {
        if (_adoptions.TryGetValue(key, out Adopted adopted))
        {
            adopted.Grabbable.Restore();
            VRInteractables.UnregisterGrabbable(adopted.Grabbable);
            DestroyReachVolume(adopted);
        }
        _adoptions.Remove(key);
    }

    private void ReleaseAll()
    {
        foreach (KeyValuePair<Component, Adopted> pair in _adoptions)
        {
            pair.Value.Grabbable.Restore();
            VRInteractables.UnregisterGrabbable(pair.Value.Grabbable);
            DestroyReachVolume(pair.Value);
        }
        _adoptions.Clear();
        // [Optimize] FigureScanCache: the resolution cache is only ever a shortcut to the walk it
        // replaces, so dropping it with the adoptions keeps it bounded per scenario and guarantees
        // the next sweep re-resolves everything from scratch.
        _figureInteractables.Clear();
        HeldFigures.Clear();
        // Every hold just ended, so no stretch gesture can be live either — clear its state so a
        // re-enable (or the next scenario) starts with no captured hand.
        FigureStretch.Clear();
        // The hold-gate's per-actor animator cache is a shortcut over these same adoptions —
        // dropped with them, exactly like _figureInteractables above.
        FigureBusy.ClearCache();
    }
}
