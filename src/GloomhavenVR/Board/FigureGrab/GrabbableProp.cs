using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A grabbable board PROP — a chest, a gold pile, a trap, a loose obstacle, a quest item or a
/// resource. The sibling of <see cref="FigureGrabbable"/>, and deliberately a sibling rather than
/// a widening of it.
///
/// <para><b>WHY IT CANNOT BE A <see cref="FigureGrabbable"/>.</b> That class is an
/// <c>ActorBehaviour</c> wrapper end to end: its root is <c>_actor.m_RootGameObject</c>, its hold
/// goes through <see cref="HeldFigures"/>, its gates ask <c>FigureBusy.IsBusy(_actor)</c>, its
/// ghost calls <c>FigureGhosts.NotifyHeld(_actor, …)</c> and both its wire slots key on an
/// <c>ActorGuid</c>. A prop has NO <c>ActorBehaviour</c> anywhere in its subtree — the ModBuild
/// 335 hardware census said so in one line ("actorBehaviour=NO" on every prop it sampled) and the
/// rule library says why: a prop is given a <c>CObjectActor</c> only when it is configured for
/// HEALTH (CMap.cs:502-518, and <c>CObjectActor.SetAttachedToProp</c> refuses independently at
/// CObjectActor.cs:99-104). Chests carry items, gold and XP and no health at all; the same holds
/// for gold piles, quest items, resources and every obstacle a scenario did not make destructible.
/// So the same census read <c>Choreographer.m_ClientObjects</c> as holding ZERO props while
/// <c>ScenarioState.Props</c> held fifteen, fourteen of them liftable.</para>
///
/// <para><b>WHAT IT REUSES ANYWAY.</b> Everything that is not actor-shaped:</para>
/// <list type="bullet">
///   <item>the interaction layer itself — <see cref="IGrabbable"/>, <see cref="IGrabHighlight"/>,
///   <see cref="IGrabbableHandFilter"/> and the <see cref="ITriggerOnlyGrabbable"/> marker, so the
///   <c>ProximityGrabber</c> elects, highlights, buzzes and grabs a prop through the same code
///   path as a mini and a card;</item>
///   <item>the HOVER GLOW — <see cref="FigureHighlight"/>, whose <c>Apply</c> already takes a
///   plain <c>GameObject</c> root; a prop passes its visual root and <c>null</c> for the animated
///   root and the ring subtree it does not have;</item>
///   <item>the GHOST — <see cref="FigureOverlay.BuildFrozenGhost"/> through
///   <see cref="PropGhosts"/>, again a plain <c>GameObject</c> in;</item>
///   <item>the PICK VOLUME dial — <c>FigureGrabConfig.PickRadiusRealMeters</c> and its exit
///   hysteresis, so a prop lights up at exactly the reach a mini does;</item>
///   <item>the HELD POSE — the FIGURE's whole pose PIPELINE, driven by the MAP ITEM's OWN dials:
///   <c>CaptureUprightBase</c> at the grab, then
///   <c>_uprightBase * (HeldUpright ? HeldUprightRotation(side) : HeldPalmRotation())</c> as a
///   FIXED CONSTANT anchor-local rotation, the grab-time anchor-local SIZE LATCH, and a per-frame
///   idempotent re-assert. The MACHINERY is the figure's, line for line; the NUMBERS come from
///   <see cref="PropHeldPose"/> since ModBuild 350 ("ich will genau das selbe nun auch für
///   Map-Items … separat einstellen können"). See <see cref="ApplyHeldPose"/>;</item>
///   <item>the RELEASE GLIDE — the same 0.28 s cubic ease-out, so putting a chest down looks like
///   putting a mini down;</item>
///   <item>the PICKUP INFO PANEL — the figure docks the game's own stat window on grab and
///   re-asserts it while held; a prop docks the game's own prop/text info window the same way.
///   See <see cref="ShowInfo"/>.</item>
/// </list>
///
/// <para><b>THE ModBuild 339 ROUND — four defects, one of them structural.</b> The user tested
/// 338 and reported, verbatim: <i>"a) ich sehe zwar einen Geist aber in der Hand ist es garnicht
/// oder nur immer ganz kurz für einen Frame sichtbar, b) … es ist nicht in der richtigen
/// Ausrichtung … nutze den selben Code hier, c) Es soll wie die Figuren auch in einer Animation
/// zurück gehen wenn man loslässt, d) Die Info die da sein sollte … ist nicht sichtbar."</i></para>
/// <list type="bullet">
///   <item>(a) INVISIBLE IN HAND. Not a layer, not a culling mask, not stale bounds — the mod's
///   OWN <c>WallSegmentFade</c> hid it. Its wall-mounted-dressing pass adopts scenery that is
///   AIRBORNE over the room floor and its stacked-shell pass adopts anything whose live bounds
///   clear the ground band; both end in <c>renderer.enabled = false</c>. Their single exemption is
///   <c>IsFigureOrActorRenderer</c> (skinned, or <c>ActorBehaviour</c>/<c>CInteractableActor</c>/
///   <c>Animator</c> on an ancestor), which a held MINI passes and a held PROP cannot: a prop has
///   no actor, its body is usually a plain <c>MeshRenderer</c>, and the walk is
///   <c>GetComponentInParent</c>, so reparenting into the hand also discards whatever a board
///   ancestor contributed. Lifting a prop makes it airborne, un-exempt and re-parented in one
///   step. The fix is <see cref="HeldProps.OwnsRendererOf"/> — the question the wall systems have
///   to ask — plus one term in that guard. See that method for the full account.</item>
///   <item>(b) ORIENTATION. 338 wrote <c>HeldUprightRotation(side)</c> alone and skipped the
///   figure's <c>_uprightBase</c>, so with the upright-at-grab option on a prop was the one
///   thing in the hand that did NOT start upright. Now the figure's exact composition.</item>
///   <item>(c) RETURN ANIMATION. The glide was already here in 338 and was correct; it was
///   invisible because (a) hid the thing gliding. Unchanged mechanism, now with the release log
///   line the figure path has.</item>
///   <item>(d) INFO. 338 showed nothing at all. See <see cref="ShowInfo"/> for what a prop can
///   show and why <c>StatPanelSurface</c> is the wrong window for it.</item>
/// </list>
///
/// <para><b>THE ModBuild 349 ROUND — one defect, three builds, and it was ours.</b> The user
/// tested 341 and then 348 with the same sentence: <i>"Das Problem mit den flackerten assets die
/// ich in die Hand nehme ist weiterhin unverändert."</i> The hold watch 341 shipped for exactly
/// this question answered it in 348: every renderer captured at the grab was DESTROYED during the
/// hold and new ones took its place, while the root on the hand stayed alive. The cause is not a
/// write war and not a wall pass — a prop's root carries an <c>ApparanceEntity</c>, and an
/// Apparance entity destroys and re-instantiates its own generated content whenever it is
/// TRANSFORMED. A prop in a moving hand is transformed every frame, so it was rebuilding itself
/// every frame, and every new renderer is born <c>Renderer.enabled = false</c>. The fix is
/// <see cref="FreezeApparance"/> — one documented plugin field, off for the length of the hold —
/// and it leaves the hand holding the real prop and the home cell showing the ghost, exactly as
/// before. Read that method for the full chain and for why a copy in the hand loses.</para>
///
/// <para><b>WHAT IT MUST NOT DO.</b> Nothing here writes game state. Lifting a chest does not
/// move it on the board, does not loot it, does not touch pathing and does not touch whose turn
/// it is: the only things this class writes are the prop VISUAL's transform, its colliders'
/// LAYER, and mod-owned objects. The home pose is captured at the grab and written back verbatim
/// on release, so the prop ends where the game left it.</para>
///
/// <para><b>THE LAYER, AND WHY IT MOVES.</b> <c>UnityGameEditorObject.Start</c> puts every prop
/// on the <c>"Hovering"</c> layer (UnityGameEditorObject.cs:59-63) — the layer the game's own
/// mouse/hover picking looks at. A prop riding the hand sits centimetres from the camera, so the
/// game's hover ray would hit it constantly and light up a board cell the player is not pointing
/// at. While held, the prop's colliders are parked on Ignore Raycast (layer 2) and restored
/// EXACTLY on landing. Only collider hosts move — renderers keep their authored layers, because a
/// layer is also a camera culling mask and moving a mesh off its layer can make it vanish for a
/// camera that filters on it.</para>
///
/// <para><b>MULTIPLAYER.</b> Local-only in this build and additive by construction: no packet is
/// sent and none is expected, so an unmodded or older peer sees a chest that never moves. The
/// seam is named in <see cref="HeldProps"/>; the identity a held-prop record needs is
/// <c>CObjectProp.PropGuid</c>.</para>
/// </summary>
internal sealed class GrabbableProp : IGrabbable, IGrabHighlight, IGrabbableHandFilter, ITriggerOnlyGrabbable
{
    /// <summary>Unity's built-in Ignore Raycast layer. Parked here for the duration of a hold; see
    /// the class doc for why the game's own <c>"Hovering"</c> layer cannot stay.</summary>
    private const int IgnoreRaycastLayer = 2;

    /// <summary>Same 0.28 s cubic ease-out as <c>FigureGrabbable.GlideDurationSeconds</c> — a prop
    /// being put down must look like a mini being put down. MIRRORED value: if one is ever tuned,
    /// tune both.</summary>
    private const float GlideDurationSeconds = 0.28f;

    /// <summary>Props currently mid-release-glide. Advanced by <see cref="TickGlides"/>.</summary>
    private static readonly List<GrabbableProp> Gliding = new(2);

    /// <summary>Props currently IN A HAND — the figure path's <c>Live</c> set, on props. Walked
    /// once per frame by <see cref="TickHeld"/> to re-assert the held pose (which makes the pose
    /// live-tunable exactly like a figure's, and makes any foreign write to the transform last one
    /// frame instead of forever) and to keep the info panel docked. Never more than two.</summary>
    private static readonly List<GrabbableProp> Live = new(2);

    /// <summary>Seconds between held-info re-assert checks. Not per-frame: the check exists to
    /// survive the game's own hover logic hiding the panel, and that happens at hover rate, not at
    /// frame rate. Cheap either way — one float compare when the cadence has not elapsed.</summary>
    private const float InfoReassertSeconds = 0.25f;

    /// <summary>How many consecutive re-assert attempts may find the panel hidden again before
    /// this hold STOPS re-asserting and prints one line naming the fight. Standing project rule:
    /// do not win a write war — concede and name the writer, because a panel we re-show four times
    /// a second against something that hides it four times a second is a strobe, which is worse
    /// than no panel. See <see cref="TickInfo"/>.</summary>
    private const int InfoLossBudget = 6;

    /// <summary>How many hover lines and how many grab lines this session prints before going
    /// quiet. The hardware question is "does a prop highlight and lift AT ALL", which the first
    /// few answer completely; a per-hover line over a board of fourteen props would drown the log
    /// it exists to make readable.</summary>
    private const int LogBudget = 4;

    /// <summary>
    /// How many DISTINCT prop kinds the hover census names per session (ModBuild 366).
    ///
    /// <para>The 2026-09-03 question is "which prop kinds still glow correctly, and how many
    /// renderers does each one contribute", and a budget of four CONSECUTIVE lines answers it for
    /// whichever prop the hand happened to sweep first — the 2026-09-03 log spent all four of them
    /// on two obstacles. Keyed on <see cref="CObjectProp.PrefabName"/> the same four lines become a
    /// roster instead, which is the shape <see cref="FigureHighlight.AuditFigure"/> already uses for
    /// figures and for the same reason. Twelve, because a scenario carries chests, gold, traps,
    /// obstacles, quest items, resources and terrain and the roster is worth nothing truncated.</para>
    /// </summary>
    private const int HighlightKindBudget = 12;

    /// <summary>Prop kinds whose hover census has already been printed this session — one line per
    /// KIND, never one per hover. Never read outside the census.</summary>
    private static readonly HashSet<string> HighlightKinds = new();

    private static int _highlightLogsLeft = HighlightKindBudget;
    private static int _grabLogsLeft = LogBudget;

    private readonly CObjectProp _prop;
    private readonly GameObject _visual;
    private readonly Collider _collider;

    private readonly FigureHighlight _highlight = new();

    private VRHand? _holder;

    /// <summary>Per-hand reach latch for the pick-radius Schmitt trigger (see
    /// <see cref="AllowsHand"/>). Indexed by <see cref="HandSide"/>.</summary>
    private readonly bool[] _inReach = new bool[2];

    // --- the HOME pose, captured at the grab and written back verbatim on release.
    private bool _attached;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot = Quaternion.identity;
    private Vector3 _origLocalScale = Vector3.one;
    private Vector3 _homeWorldPos;
    private Quaternion _homeWorldRot = Quaternion.identity;
    private Vector3 _homeWorldScale = Vector3.one;

    // --- the parked layers, restored object-for-object on landing.
    private GameObject[]? _layerHosts;
    private int[]? _layerValues;

    // --- the held-pose bases, captured at the grab. All three are the FIGURE's, field for field
    //     (FigureGrabbable._anchor / _uprightBase / _heldLocalScale); see ApplyHeldPose.
    private Transform? _anchor;
    private Quaternion _uprightBase = Quaternion.identity;
    private Vector3 _heldLocalScale = Vector3.one;

    // --- THE MAP-ITEM STRETCH (ModBuild 362). The figure's four fields, field for field, for the
    //     same reasons; see the Stretch region at the end of this class for the account and for
    //     which lesson each one carries.
    private float _stretch = 1f;
    private float _latchTotalRatio = 1f;
    private Renderer[]? _stretchBoundsRenderers;
    private float _captureBodyRadiusReal = float.NaN;
    private float _captureCeilingReal = float.NaN;

    // --- the last held pose this class WROTE, so the animation A/B instrument can tell whether
    //     anything else rewrote it between our re-asserts. Diagnostic-only (PropAnimWatch reads
    //     it; nothing else does), and three struct stores on a path that already writes them.
    private Vector3 _wrotePos;
    private Quaternion _wroteRot = Quaternion.identity;
    private Vector3 _wroteScale = Vector3.one;

    // --- the pickup info panel (defect (d)).
    private bool _infoShown;
    private string _infoTitle = string.Empty;
    private string? _infoDescription;
    private float _nextInfoCheck;
    private int _infoLosses;
    private static bool _loggedInfoWriteWar;

    // --- WHICH of the two info windows this hold's card is in, and how it got there (ModBuild
    //     364). The window is a per-HOLD decision, taken once at the grab and re-asserted, never
    //     re-elected; the route string is diagnostic only and is what the card-route roster prints.
    private HeldPropCardWindow _infoWindow;
    private string _infoRoute = string.Empty;
    private static bool _loggedRichThrow;

    /// <summary>Prop kinds whose card-route line has already been printed this scenario — see
    /// <see cref="LogCardRoute"/>. One line per KIND, never one per grab.</summary>
    private static readonly HashSet<string> CardRouteKinds = new();

    private static int _cardRouteLogsLeft = HighlightKindBudget;

    // --- release glide.
    private bool _glideActive;
    private float _glideStartTime;
    private Vector3 _glideFromPos;
    private Quaternion _glideFromRot = Quaternion.identity;
    private Vector3 _glideFromScale = Vector3.one;

    internal GrabbableProp(CObjectProp prop, GameObject visual, Collider collider)
    {
        _prop = prop;
        _visual = visual;
        _collider = collider;
    }

    internal CObjectProp Prop => _prop;

    internal GameObject Visual => _visual;

    internal Collider PickCollider => _collider;

    internal bool IsHeld => _holder != null;

    /// <summary>The prop's prefab name plus its import type — the vocabulary every other
    /// <c>[Props]</c> line uses, so a hardware log reads as one story.</summary>
    /// <remarks>Cached on first use (the prop's prefab name and import type never change for the
    /// life of this wrapper): since the two-props build the dock edge test reads it EVERY FRAME of
    /// a hold (<c>PropInfoSurface.ReportDock</c>), and an interpolation there would be a per-frame
    /// allocation on a VR hot path.</remarks>
    internal string Label => _label ??= $"'{(_prop != null ? _prop.PrefabName : "?")}' {(_prop != null ? _prop.ObjectType.ToString() : "?")}";

    private string? _label;

    /// <summary>The hand this prop rides, or null between holds. Read by <see cref="HeldPropCard"/>
    /// to dock a displaced prop's snapshot card beside ITS hand, not the later grab's.</summary>
    internal HandSide? HolderSide => _holder != null ? _holder.Side : null;

    // ---- IGrabbable ---------------------------------------------------------------------------

    public bool CanGrab
    {
        get
        {
            if (!FigureGrabConfig.GrabPropsEnabled || _holder != null)
                return false;
            // The remote grab-lock goes here when the sync lands: `if (NetHeldProps.Owns(_prop))
            // return false;` — the same shape FigureGrabbable uses against NetHeldFigures.
            return _visual != null && _collider != null;
        }
    }

    /// <summary>False → props obey the shared trigger, exactly like figures and cards. The
    /// <see cref="ITriggerOnlyGrabbable"/> marker additionally withholds the grabber's "a closing
    /// fist grabs the highlighted candidate" fallback, so a grip squeeze over a crowded board
    /// never lifts a chest by accident.</summary>
    public bool GrabWithGrip => false;

    /// <summary>
    /// THE PICK VOLUME, done per-prop instead of by a driver-side election.
    ///
    /// <para>A figure's election is run centrally (<c>FigureGrabDriver.SelectByOffsetAnchor</c>)
    /// because a figure carries a mod-owned reach extension, a busy predicate, a stretch gesture
    /// and a refusal probe that all have to agree within one frame. A prop has none of that, so
    /// the only rule left is the one the user can feel: the PINCH POINT — where a held object
    /// appears, not the palm — has to come within <c>[FigureGrab] PickRadiusMillimeters</c> of the
    /// prop's own surface. Everything past that is left to the <c>ProximityGrabber</c>, which
    /// already picks the single nearest surviving candidate and already has its own switch
    /// hysteresis. So props and figures compete on equal terms in one election instead of two.</para>
    ///
    /// <para>Hysteresis, same shape as the figure's (<c>FigureGrabDriver.PickExitFactor</c>): a
    /// prop has to come INSIDE the configured radius to become a candidate, but only stops being
    /// one past a wider exit ring. A hand drifting on the boundary therefore cannot chatter the
    /// highlight — the defect that cost the figure path several rounds.</para>
    /// </summary>
    public bool AllowsHand(VRHand hand)
    {
        if (_holder != null)
            return ReferenceEquals(_holder, hand); // the holder keeps its hold; nobody else joins
        if (_collider == null || hand == null)
            return false;

        // THE STRETCH VETO, and this is the only place a prop can publish one (ModBuild 362). A
        // hand inside the resize zone of the OTHER hand's held object elects nobody and grabs
        // nothing — the figure path gets that from FigureGrabDriver.SelectByOffsetAnchor's
        // ApplySuppression, but a prop is not elected centrally: this method IS the election. So
        // the veto lives here, and answering false removes this prop from the ProximityGrabber's
        // candidate list for the frame, which takes the hover highlight with it. Without it a hand
        // resizing a chest would light up every chest it swept past mid-gesture. Note the ORDER:
        // the holder's own early-out is above, so this can never refuse the hand that is holding.
        if (FigureStretch.Engaged(hand.Side))
        {
            // ModBuild 404: the veto is COUNTED and NAMED (StretchCaptureWatch) — the 396 log
            // could not say how often this branch fired, only that the trigger-level gesture did.
            StretchCaptureWatch.NotePropVeto(hand.Side, Label, _inReach[(int)hand.Side]);
            _inReach[(int)hand.Side] = false; // and drop the hysteresis latch with the candidacy
            return false;
        }

        int side = (int)hand.Side;
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        float admit = FigureGrabConfig.PickRadiusRealMeters * scale
                      * (_inReach[side] ? FigureGrabDriver.PickExitFactor : 1f);
        Vector3 pinch = hand.Rig.GrabAnchor.TransformPoint(PropHeldPose.HeldOffsetFor(hand.Side));
        bool inside = Vector3.Distance(pinch, _collider.ClosestPoint(pinch)) <= admit;
        _inReach[side] = inside;
        return inside;
    }

    /// <summary>
    /// Is <paramref name="hand"/>'s pinch point inside this prop's pick volume RIGHT NOW — the
    /// same distance and the same admit radius <see cref="AllowsHand"/> uses, with no side effect
    /// (the hysteresis latch is not read and not written). Read by <c>FigureStretch</c> BEFORE it
    /// captures a hand: a hand physically at a prop is reaching for the prop, not for the other
    /// hand's miniature (ModBuild 404). <paramref name="realMetres"/> is the pinch-to-surface
    /// distance in real metres at the hand.
    /// </summary>
    internal bool InReachOf(VRHand hand, out float realMetres)
    {
        realMetres = float.PositiveInfinity;
        if (_holder != null || _collider == null || hand == null)
            return false;
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        Vector3 pinch = hand.Rig.GrabAnchor.TransformPoint(PropHeldPose.HeldOffsetFor(hand.Side));
        float world = Vector3.Distance(pinch, _collider.ClosestPoint(pinch));
        realMetres = world / scale;
        return realMetres <= FigureGrabConfig.PickRadiusRealMeters;
    }

    // ---- IGrabHighlight -----------------------------------------------------------------------

    /// <summary>
    /// Pre-grab hover glow — the FIGURE's glow, on a prop (user, 2026-09-02: "highlighting beim
    /// drüberfahren mit der Hand"). <see cref="FigureHighlight.Apply"/> takes a plain
    /// <c>GameObject</c> root and clones its renderers into an additive overlay, so a prop passes
    /// its visual root, <c>null</c> for the animated root it does not have, and <c>null</c> for the
    /// selection-ring subtree it does not have either. Walk-in suppression is honoured for the
    /// same reason it is on figures: standing inside the board, a proximity glow answers a
    /// question nobody is asking.
    /// </summary>
    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        if (_visual == null)
            return;

        if (!highlighted)
        {
            ClearHighlight();
            return;
        }
        if (_highlight.Active || !FigureGrabConfig.HighlightAllowedHere)
            return;

        // The `label` argument arrived with the clone census (same build): it is what the
        // SEEN? follow-up line names, so a prop's glow is attributable exactly like a
        // figure's rather than reporting as an anonymous overlay.
        bool glow = _highlight.Apply(_visual, null, null, Label, out string overlay);
        // ONE LINE PER PROP KIND, NOT THE FIRST FOUR HOVERS (ModBuild 366) — see
        // HighlightKindBudget. The Add is what gates the line, so a kind already censused costs one
        // hash probe per hover and nothing else.
        if (_highlightLogsLeft <= 0 || !HighlightKinds.Add(_prop != null ? _prop.PrefabName : "?"))
            return;
        _highlightLogsLeft--;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] pre-grab highlight ENGAGED ({hand.Side} near {Label}) — animated additive glow "
            + (glow ? $"overlaid on the prop's own meshes (wall-occluded, no scale change): {overlay}."
                    : $"UNAVAILABLE — no highlight: {overlay}.")
            + $" ({_highlightLogsLeft} more prop highlight lines this session.)");
    }

    private void ClearHighlight()
    {
        if (_highlight.Active)
            _highlight.Clear();
    }

    // ---- the hold -----------------------------------------------------------------------------

    public void OnGrab(VRHand hand)
    {
        if (_visual == null)
            return;

        // Re-grab during a release glide: land it instantly FIRST so the home pose captured below
        // is the true board pose and never a mid-glide sample. HeldProps.Add re-enters the held
        // set in the same call, so PropGhosts never sees a released frame.
        if (_glideActive)
            FinishGlide();

        // THE APPARANCE FREEZE, and it runs BEFORE anything that moves the prop, because moving an
        // Apparance entity is what makes it destroy and re-instantiate its own content — the whole
        // defect. See FreezeApparance for the chain, quoted from the plugin's own source.
        FreezeApparance();

        Transform t = _visual.transform;
        _holder = hand;
        _origParent = t.parent;
        _origLocalPos = t.localPosition;
        _origLocalRot = t.localRotation;
        _origLocalScale = t.localScale;
        _homeWorldPos = t.position;
        _homeWorldRot = t.rotation;
        _homeWorldScale = t.lossyScale;

        // The GHOST, built from the board pose BEFORE anything is reparented (user: "wenn man es
        // in der Hand hat einen Geist hinterlassen").
        PropGhosts.NotifyHeld(_prop, _visual, _homeWorldPos, _homeWorldRot, _homeWorldScale);

        // The visual goes in with the prop: HeldProps.OwnsRendererOf is what tells the mod's
        // scenery systems that these renderers are in a hand and may not be touched — defect (a).
        // It is registered BEFORE the reparent so no pass can see a frame in which the prop is
        // already airborne and not yet known to be held.
        HeldProps.Add(_prop, _visual, hand.Side);
        ClearHighlight();     // belt: the grabber clears the hover before OnGrab, but a laser pluck
                              // and a re-grab mid-glide are both paths that need not have done so
        ParkLayers();

        // Ride the hand's grab anchor. worldPositionStays keeps the prop at its board world size as
        // it enters the hand (no scale pop) and leaves the resulting anchor-LOCAL scale in place.
        Transform anchor = hand.Rig.GrabAnchor;
        t.SetParent(anchor, worldPositionStays: true);
        _anchor = anchor;

        // THE SIZE LATCH, and it is the FIGURE's, arrived at by the shorter road. FigureGrabbable
        // computes homeWorldScale / anchor.lossyScale; the worldPositionStays reparent one line up
        // has already SOLVED that same quotient into localScale, so reading it back is the same
        // number with no second opinion about which transform in the chain carries the zoom. What
        // matters is that it is LATCHED: from here it is re-asserted, never re-derived, so a
        // diorama zoom mid-hold cannot resize what is in the palm ("die Größe soll nur abhängig
        // sein wann sie greift und dann fix in der Hand sein - auch wenn man dabei zoomed").
        _heldLocalScale = t.localScale;

        // THE MAP-ITEM STRETCH, per-hold state. Reset at every grab for the figure's own reason:
        // the user asked to change "die Größe ... in der Hand", not to set a standing preference.
        _stretch = 1f;
        _stretchBoundsRenderers = null;      // the visual may differ between holds
        _captureBodyRadiusReal = float.NaN;  // "never measured" until a free hand runs the test
        _captureCeilingReal = float.NaN;
        ApplyGrabTimeStretchClamp(anchor);   // …then the TOTAL size bound may trim the latch itself

        _uprightBase = CaptureUprightBase(anchor);
        _attached = true;

        ApplyHeldPose();
        Live.Add(this);
        ShowInfo();
        _probeFrame = Time.frameCount + 2; // arm the held-visibility probe (see TickProbe)
        BeginWatch();                      // arm the PER-FRAME hold watch (see BeginWatch)
        PropAnimWatch.NotifyGrab(_visual, Label); // …and the ANIMATION A/B (see PropAnimWatch)

        if (_grabLogsLeft <= 0)
            return;
        _grabLogsLeft--;
        Quaternion lr = t.localRotation;
        Vector3 up = lr * Vector3.up, fwd = lr * Vector3.forward;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] {hand.Side} grabbed {Label} — the prop VISUAL rides the hand (resolved through "
            + "ObjectCacheService, not through an actor: it has none). Home pose captured and a ghost "
            + "left at the cell; colliders parked on Ignore Raycast for the hold. "
            + $"localRot={Fmt(lr)} (fixed constant relative to the hand anchor; grab-angle-independent, "
            + $"rides the hand) from [FigureGrab] PropHeldRotPitch={PropHeldPose.Pitch:0.#}° "
            + $"PropHeldRotYaw={PropHeldPose.YawFor(hand.Side):0.#}° "
            + $"PropHeldRotRoll={PropHeldPose.RollFor(hand.Side):0.#}° "
            + $"PropHeldUpright={PropHeldPose.HeldUpright} PropHeldUprightAtGrab={PropHeldPose.HeldUprightAtGrab} "
            + "(the MAP-ITEM dials, separate from the figures'); "
            + $"prop axes in anchor space: up=({up.x:0.00},{up.y:0.00},{up.z:0.00}) "
            + $"fwd=({fwd.x:0.00},{fwd.y:0.00},{fwd.z:0.00}) — anchor +Y is the palm normal, so up.y=+1 is "
            + "'standing straight out of the palm', which for a prop means its own model +Y (the axis it "
            + $"stands on its hex by) points out of the palm. heldLocalScale={_heldLocalScale.x:0.######} "
            + $"latched against anchorScale={anchor.lossyScale.x:0.###}. "
            + $"({_grabLogsLeft} more prop grab lines this session.)");

        EmitHandednessLine(hand.Side);
    }

    /// <summary>
    /// THE HANDEDNESS LINE (2026-09-05, the handedness round) — the map item's hold and the FIGURE's hold for the SAME
    /// hand, printed side by side, plus what each of them would be in the other hand.
    ///
    /// <para><b>THE QUESTION IT SETTLES.</b> The user asked why the mirror works for figures and
    /// not for map items (<i>"warum klappt das bei den Figuren, aber Props nicht?"</i>). Reading
    /// the source answers it — <see cref="HeldPoseMirror"/> is now the ONE place either path
    /// computes the mirror, so the two cannot differ — but "the code is the same" is a claim, and
    /// the thing that decides the report is the SIZE of the swing each path's own tuned yaw
    /// produces. <c>mirrorSwing</c> is that number: the angle between the pose this hand gets and
    /// the pose the other hand gets, for the map item and for the figure, in one line. A figure
    /// swing near 0° beside a map-item swing near 90° IS the whole explanation and needs no
    /// further round.</para>
    ///
    /// <para>Angles are printed SIGNED (±180) and in the HAND's own frame, because that is the
    /// frame the dials are written in and the one a person can hold their hand up and check. A new
    /// line rather than a clause on the one above: that line's tokens are what earlier rounds
    /// grep for, and this appends beside them instead of rewording them.</para>
    /// </summary>
    private static void EmitHandednessLine(HandSide side)
    {
        HandSide other = side == HandSide.Left ? HandSide.Right : HandSide.Left;

        bool propUpright = PropHeldPose.HeldUpright;
        Quaternion propThis = propUpright ? PropHeldPose.HeldUprightRotation(side)
                                          : PropHeldPose.HeldPalmRotation();
        Quaternion propOther = propUpright ? PropHeldPose.HeldUprightRotation(other)
                                           : PropHeldPose.HeldPalmRotation();
        Vector3 propOff = PropHeldPose.HeldOffsetFor(side);

        // The figure half is READ-ONLY and guarded: these entries are bound at plugin start, long
        // before any hand exists, but a diagnostic must never be the thing that throws inside a
        // grab. A null entry reads as the shipped mode rather than refusing the whole line.
        ConfigEntry<bool>? figUprightEntry = FigureGrabConfig.HeldUpright;
        bool figUpright = figUprightEntry != null ? figUprightEntry.Value : Defaults.HeldUpright;
        Quaternion figThis = figUpright ? FigureGrabConfig.HeldUprightRotation(side)
                                        : FigureGrabConfig.HeldPalmRotation();
        Quaternion figOther = figUpright ? FigureGrabConfig.HeldUprightRotation(other)
                                         : FigureGrabConfig.HeldPalmRotation();
        Vector3 figOff = FigureGrabConfig.HeldOffsetFor(side);

        // HW-VERIFY: the line that answers "why the figures and not the props". It must stay at a
        // tier the DEFAULT log level prints (Note/Alert/Error) — scripts/check-hw-verify.py.
        VRLog.Note("FigureGrab",
            $"[Props] HANDEDNESS {side} hand — [FigureGrab] PropHeldMirrorHands="
            + $"{PropHeldPose.Mirrored} (the figures are always mirrored; there is no figure key). "
            + $"MAP ITEM: applied rot {FmtSigned(propThis)}° in the hand's own frame, from authored "
            + $"pitch={PropHeldPose.Pitch:0.#}° yaw={PropHeldPose.Yaw:0.#}° roll={PropHeldPose.Roll:0.#}° "
            + $"(this hand takes yaw={PropHeldPose.YawFor(side):0.#}° roll={PropHeldPose.RollFor(side):0.#}°), "
            + $"offset=({propOff.x:0.###},{propOff.y:0.###},{propOff.z:0.###}) m, upright={propUpright}, "
            + $"mirrorSwing={Quaternion.Angle(propThis, propOther):0.#}° vs the {other} hand. "
            + $"FIGURE, same hand, for comparison: applied rot {FmtSigned(figThis)}° from authored "
            + $"pitch={FigureGrabConfig.ActiveHeldTilt:0.#}° yaw={FigureGrabConfig.ActiveHeldFaceYaw:0.#}° "
            + $"roll={FigureGrabConfig.ActiveHeldRoll:0.#}° (this hand takes "
            + $"yaw={FigureGrabConfig.HeldFaceYawFor(side):0.#}° roll={FigureGrabConfig.HeldRollFor(side):0.#}°), "
            + $"offset=({figOff.x:0.###},{figOff.y:0.###},{figOff.z:0.###}) m, upright={figUpright}, "
            + $"mirrorSwing={Quaternion.Angle(figThis, figOther):0.#}°. "
            + "Both swings come from ONE shared mirror (HeldPoseMirror): it flips the yaw, the roll "
            + "and the sideways offset and leaves the pitch alone, so the swing is set by the tuned "
            + "YAW and by nothing else — 0° at yaw 0 or ±180, widest near ±90.");
    }

    private static string Fmt(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({e.x:0.0},{e.y:0.0},{e.z:0.0})";
    }

    /// <summary>Euler angles wrapped to ±180 — the form the [FigureGrab] dials are written in, so
    /// a tuned −133° reads back as −133 and not as 227. <see cref="Fmt"/> keeps Unity's raw 0..360
    /// because earlier rounds' lines are read against it.</summary>
    private static string FmtSigned(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({Signed(e.x):0.#},{Signed(e.y):0.#},{Signed(e.z):0.#})";
    }

    private static float Signed(float degrees) => degrees > 180f ? degrees - 360f : degrees;

    /// <summary>
    /// THE UPRIGHT BASE — <c>FigureGrabbable.CaptureUprightBase</c>, verbatim in behaviour, on a
    /// prop. Captured ONCE at the grab and then left alone: the prop starts standing however you
    /// reached for it (palm down, from the side, upside down) and from then on it is an ordinary
    /// fixed rotation relative to the hand, so turning your wrist still turns it through every
    /// angle. It is not a constraint that keeps re-righting the prop, which would fight you the
    /// moment you tried to look at its underside.
    ///
    /// <para><b>WHAT "UPRIGHT" MEANS FOR A PROP, stated because it is a fair question (the user
    /// asked for the figure's behaviour, and a gold pile is not a humanoid).</b> It means the same
    /// thing it means for a mini and for exactly the same reason: the mod's GrabAnchor is authored
    /// with +Y out of the palm, and EVERY board object — mini, chest, gold pile, ice obstacle — is
    /// authored standing on its hex with its own model +Y up. So an identity base already puts the
    /// prop's standing axis out of the palm, which is "the right way up" for a chest in the same
    /// sense it is for a mini: the lid is up, the coins face up, the crystal points up. Nothing
    /// here needs a humanoid, only a model whose +Y is the axis it stands on, and the game's own
    /// placement guarantees that for props (they are dropped onto hexes with a yaw-only rotation —
    /// <c>CObjectProp.Rotation</c>, snapped by <c>UnityGameEditorObject.m_ShouldSnapRotation</c>).
    /// The prop's BOARD yaw is deliberately discarded, exactly as a mini's is: the held rotation is
    /// an ABSOLUTE anchor-local rotation, so which way the chest happened to face on its hex does
    /// not change how it sits in your hand.</para>
    /// </summary>
    private static Quaternion CaptureUprightBase(Transform anchor)
    {
        if (!PropHeldPose.HeldUprightAtGrab)
            return Quaternion.identity;

        Vector3 flat = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.ProjectOnPlane(anchor.up, Vector3.up);
        if (flat.sqrMagnitude < 1e-6f)
            flat = Vector3.forward;

        Quaternion world = Quaternion.LookRotation(flat.normalized, Vector3.up);
        return Quaternion.Inverse(anchor.rotation) * world;
    }

    /// <summary>
    /// (Re-)apply the held pose from <see cref="PropHeldPose"/> — offset, rotation and the
    /// latched scale — off the bases captured at the grab. IDEMPOTENT, which is what lets it be
    /// both the grab-time write and the per-frame re-assert (and that per-frame re-assert is also
    /// what makes the eight [FigureGrab] PropHeld* dials live-tunable without a hook of their own).
    ///
    /// <para><b>THIS IS THE FIGURE'S <c>ApplyHeldPose</c>, line for line</b> (user, defect (b):
    /// "Es soll sich so verhalten wie die Figuren auch - nutze den selben Code hier"). ModBuild 338
    /// wrote the pinch offset and <c>HeldUprightRotation(side)</c> but dropped
    /// <see cref="_uprightBase"/>, so with [FigureGrab] PropHeldUprightAtGrab ON — which is what the
    /// figure path ships with — a prop was the only thing in the hand that did not start upright:
    /// it inherited the palm's tilt at the instant of the grab and kept it for the whole hold.
    /// Composed the same way for the same reason: the tuned angles stay OFFSETS either way, from
    /// "standing up" with the option on and from the hand with it off.</para>
    ///
    /// <para>Being idempotent and re-asserted also buys the thing a prop has no
    /// <c>ActorBehaviour_HeldTransform_Patch</c> for: if anything ever does write a held prop's
    /// transform, the write lasts one frame instead of the whole hold.</para>
    /// </summary>
    private void ApplyHeldPose()
    {
        if (!_attached || _visual == null || _anchor == null || _holder == null)
            return;

        HandSide side = _holder.Side;
        Transform t = _visual.transform;

        // THE POSE-STOMP PROBE (2026-09-03). Before overwriting, ask whether the transform still
        // holds what WE last wrote. If it does not, something else wrote it between our
        // re-asserts — which for a prop whose own Animator drives a transform channel means this
        // very line is erasing the game's animation every frame. Read-only, gated on a watch
        // actually running, and the answer is reported by PropAnimWatch's one verdict line.
        if (PropAnimWatch.WatchingHand
            && (t.localPosition != _wrotePos || t.localScale != _wroteScale
                || Quaternion.Angle(t.localRotation, _wroteRot) > 0.01f))
            PropAnimWatch.NotePoseStomp();

        // Pinch position: the grab-anchor-local offset toward the thumb-index fingertips, mirrored
        // across the hand frame's left-right axis for the left hand.
        t.localPosition = PropHeldPose.HeldOffsetFor(side);

        // A FIXED CONSTANT anchor-LOCAL rotation (grab-angle-independent) that rides the hand —
        // never a world rotation. See CaptureUprightBase for what "upright" means on a prop.
        t.localRotation = _uprightBase * (PropHeldPose.HeldUpright
            ? PropHeldPose.HeldUprightRotation(side)
            : PropHeldPose.HeldPalmRotation());

        // SIZE — the value LATCHED at the grab times the manual two-hand stretch of THIS hold
        // (ModBuild 362). The latch is re-asserted and never re-derived, so a diorama zoom mid-hold
        // still cannot resize what is in the palm; the stretch is a separate, factorable term for
        // the reason FigureGrabbable._stretch states — folding it into the latch would make "the
        // size it entered the hand at" unrecoverable, and the size BOUNDS are stated against the
        // product of the two.
        t.localScale = HeldLocalScale();

        _wrotePos = t.localPosition;
        _wroteRot = t.localRotation;
        _wroteScale = t.localScale;
    }

    /// <summary>
    /// One call per frame for every prop currently in a hand — the pose re-assert, the info
    /// re-assert and the one-shot held-visibility probe. Called from <see cref="PropGrab.Tick"/>.
    /// Bounded by the number of hands: this list never holds more than two.
    /// </summary>
    internal static void TickHeld()
    {
        for (int i = Live.Count - 1; i >= 0; i--)
        {
            GrabbableProp p = Live[i];
            if (p._visual == null)
            {
                // Prop destroyed mid-hold (looted, broken, teardown — or the engine
                // re-instantiating Apparance-placed content out from under the hand, which is
                // the ModBuild 341 question). This used to be silent; the watch is the record.
                p.EmitWatch("visual DESTROYED mid-hold");
                PropAnimWatch.NotifyGone(p._visual);
                Live.RemoveAt(i);
                continue;
            }
            p.TickWatch();
            p.ApplyHeldPose();
            p.TickInfo();
            p.TickProbe();
        }
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        // Already on its way home: a driver prune or a re-grab may have started the glide this
        // frame, and falling through would cancel it into an instant snap.
        if (_glideActive)
            return;
        // Stale-hand hardening, same rule as the figure path: only the hand that owns the hold may
        // end it (or no hand at all, which is the belt-path cleanup through Restore's idempotence).
        if (_holder != null && !ReferenceEquals(_holder, hand))
            return;

        if (TryBeginGlide())
        {
            VRLog.Info("FigureGrab",
                $"[Props] {hand.Side} released {Label} — gliding home ({GlideDurationSeconds:0.00}s), "
                + "the same cubic ease-out a released mini takes.");
            return;
        }
        Restore();
        VRLog.Info("FigureGrab", $"[Props] {hand.Side} released {Label} — home INSTANTLY "
            + "(glide impossible: dead visual or dead original parent).");
    }

    /// <summary>
    /// Begin the release glide: reparent under the original parent KEEPING the in-hand world pose,
    /// then let <see cref="TickGlides"/> ease the LOCAL pose to the captured original TRS — an end
    /// state bit-identical to <see cref="Restore"/>, so no drift is possible. The hand is freed
    /// immediately (re-grab works), but the prop stays in <see cref="HeldProps"/> until arrival so
    /// the home ghost lives for the whole flight. False when a glide is impossible (dead visual or
    /// dead original parent — scenario teardown mid-hold); the caller then restores instantly.
    /// </summary>
    private bool TryBeginGlide()
    {
        if (!_attached || _glideActive || _visual == null || _origParent == null)
            return false;

        Live.Remove(this);
        EmitWatch("released — glide home"); // the hold is over; the watch reports what it saw
        ClearInfo();
        ClearHighlight();
        Transform t = _visual.transform;
        t.SetParent(_origParent, worldPositionStays: true); // keep the in-hand world pose
        _attached = false;
        _anchor = null;

        _glideFromPos = t.localPosition;
        _glideFromRot = t.localRotation;
        _glideFromScale = t.localScale;
        _stretchBoundsRenderers = null; // per-hold, like the figure's — the next hold re-walks
        _glideStartTime = Time.unscaledTime;
        _glideActive = true;
        Gliding.Add(this);
        _holder = null;
        return true;
    }

    /// <summary>Advance every in-flight prop release glide. Called once per frame from
    /// <see cref="PropGrab.Tick"/>, ahead of the feature gate so a glide already in the air still
    /// lands when the dial is turned off mid-flight.</summary>
    internal static void TickGlides()
    {
        for (int i = Gliding.Count - 1; i >= 0; i--)
            Gliding[i].TickGlide();
    }

    private void TickGlide()
    {
        if (_visual == null)
        {
            FinishGlide(); // prop destroyed mid-glide (looted, broken) — just release the bookkeeping
            return;
        }
        float u = (Time.unscaledTime - _glideStartTime) / GlideDurationSeconds; // unscaled: pause-proof
        if (u >= 1f)
        {
            FinishGlide();
            return;
        }
        float e = 1f - (1f - u) * (1f - u) * (1f - u); // cubic ease-out — fast start, soft landing
        Transform t = _visual.transform;
        t.localPosition = Vector3.LerpUnclamped(_glideFromPos, _origLocalPos, e);
        t.localRotation = Quaternion.SlerpUnclamped(_glideFromRot, _origLocalRot, e);
        t.localScale = Vector3.LerpUnclamped(_glideFromScale, _origLocalScale, e);
    }

    /// <summary>Complete (or cancel) the glide instantly: snap to the exact home TRS, hand the
    /// layers back and drop out of <see cref="HeldProps"/> so the next <see cref="PropGhosts.Tick"/>
    /// tears the ghost down. Idempotent.</summary>
    private void FinishGlide()
    {
        if (!_glideActive)
            return;
        _glideActive = false;
        Gliding.Remove(this);

        if (_visual != null)
        {
            Transform t = _visual.transform;
            t.localPosition = _origLocalPos;
            t.localRotation = _origLocalRot;
            t.localScale = _origLocalScale;
        }
        RestoreLayers();
        HeldProps.Remove(_prop);
        PropAnimWatch.NotifyLanded(_visual); // the prop is on its hex again — open the HOME window
        ScheduleThaw(); // home again — hand MonitorMovement back once the bounds have re-synced
    }

    /// <summary>
    /// Put the prop back exactly where the game left it, immediately (idempotent). The safety
    /// path: scenario teardown, a driver prune, the feature dial going off, or a glide that could
    /// not start. Never writes game state — only the prop visual's own transform and the layers
    /// this class parked.
    /// </summary>
    internal void Restore()
    {
        Live.Remove(this);
        EmitWatch("restored instantly"); // no-op when the glide path already reported this hold
        ClearInfo();
        if (_glideActive)
            FinishGlide();
        ClearHighlight();

        if (_attached && _visual != null)
        {
            Transform t = _visual.transform;
            if (_origParent != null)
            {
                t.SetParent(_origParent, worldPositionStays: false);
                t.localPosition = _origLocalPos;
                t.localRotation = _origLocalRot;
                t.localScale = _origLocalScale;
            }
            else
            {
                // The original parent died while the prop was held (scenario teardown). Unity's
                // `!= null` catches a destroyed object, so unparent to the scene root and write the
                // captured WORLD pose rather than hand SetParent a dead Transform (which throws).
                t.SetParent(null, worldPositionStays: false);
                t.SetPositionAndRotation(_homeWorldPos, _homeWorldRot);
                t.localScale = _homeWorldScale;
            }
        }
        _attached = false;
        _anchor = null;
        _stretchBoundsRenderers = null;
        RestoreLayers();
        _holder = null;
        HeldProps.Remove(_prop);
        PropAnimWatch.NotifyLanded(_visual); // the prop is on its hex again — open the HOME window
        ScheduleThaw(); // home again — hand MonitorMovement back once the bounds have re-synced
    }

    // ---- the pickup info panel (defect (d)) -----------------------------------------------------

    /// <summary>
    /// Dock the game's own info card for this prop, the way a figure docks its stat card on pickup
    /// (user, defect (d): "Die Info die da sein sollte (wie bei den Figuren auch) ist nicht
    /// sichtbar").
    ///
    /// <para><b>WHY NOT <c>StatPanelSurface.ShowHeldFigure</c>, THE FIGURE'S CALL.</b> Because it
    /// cannot take a prop, and not for a fixable reason: its whole job is to bind the game's real
    /// <c>ActorStatPanel</c> singleton to a <c>CActor</c> (its registration record, its
    /// promote/snapshot arbitration and its <c>ClearHeldFigure</c> identity check are all
    /// <c>CActor</c>-keyed), and it is that PANEL — hit points, initiative, conditions, portrait —
    /// that has nothing to show for a chest. A prop is not an under-featured actor; it has a
    /// different card in the game, and the game already shows it.</para>
    ///
    /// <para><b>THE RIGHT WINDOW, AND THE MOD ALREADY DOCKS IT.</b> Hovering a hex with a prop on
    /// it makes <c>WorldspaceStarHexDisplay.ShowTooltipForTile</c> call
    /// <c>UITextInfoPanel.Show(title, description)</c> (WSHD.cs:3607) — "2 Gold", "Chest",
    /// "Obstacle", plus who may loot it. The mod converts and docks exactly that window in world
    /// space already, in <c>WorldUI/Surfaces/PropInfoSurface.cs</c>, at its own layout slot. So the
    /// pickup card needs no new surface and no new panel: it needs the same window populated for
    /// the prop in the hand instead of the prop under the pointer. The text is built here to the
    /// game's own recipe — see <see cref="BuildInfoText"/>.</para>
    ///
    /// <para><b>AND THAT PARAGRAPH NAMED THE WRONG WINDOW (ModBuild 366).</b> It is right that the
    /// pickup card needs no new surface and no new panel; it is wrong that there is only one panel.
    /// User, 2026-09-03, verbatim: "Wenn ich mit dem laser über eine Falle hovere sehe ich noch
    /// weitere 'Effekte' der Falle. Wenn ich die Falle allerdings in die Hand nehme mit dem neuen
    /// Feature sehen ich nur den Hinweis 'Falle' ohne die Effekte darunter. Ich will, dass wenn man
    /// etwas in die Hand nimmt immer die detaillierteste Info angezeigt wird inkl. aller effekte bei
    /// allen props die man in die Hand nehmen kann."</para>
    ///
    /// <para>The game has TWO hover-info windows and picks between them per prop:</para>
    /// <list type="bullet">
    ///   <item><b>The RICH one</b> — <c>UIPropInfoPanel</c> ('Prop Info Panel', UIWindowID
    ///   TrapInfoPanel), which carries the EFFECT rows the user is missing (damage, move cost, and
    ///   one row per <c>CCondition.ENegativeCondition</c>). It is raised NOT from the tile hover but
    ///   from the prop's own <c>IHoverable</c> component on a raycast hover
    ///   (<c>HoverRegisterer.Update</c> → <c>OnCursorEnter</c>), and exactly three components do it:
    ///   <c>UnityGameEditorTrapProp</c> → <c>ShowTrap(gameObject)</c>,
    ///   <c>UnityGameEditorHazardousTerrainProp</c> → <c>ShowHazardousTerrain(gameObject)</c>,
    ///   <c>UnityGameEditorDifficultTerrainProp</c> → <c>ShowDifficultTerrain(gameObject)</c>. A
    ///   fourth route, <c>ShowQuestItem(title, description)</c>, comes from the TILE path for a
    ///   <c>CarryableQuestItem</c> (WSHD.cs:3599-3602).</item>
    ///   <item><b>The PLAIN one</b> — <c>UITextInfoPanel</c>, everything else: chests, gold piles,
    ///   obstacles, doors, pressure plates, portals, resources (WSHD.cs:3604-3607).</item>
    /// </list>
    ///
    /// <para><b>THE GAME'S OWN PATH IS CALLED, NOT REPRODUCED.</b> For the three
    /// <c>IHoverable</c> kinds this invokes <c>OnCursorEnter()</c> on the prop's own component —
    /// the very method the pointer hover invokes — so the panel is populated by the game's code,
    /// from the game's data, through the game's <c>InstanceName</c> lookup, including the game's own
    /// LevelEditor guard. A hand-built description would drift from the game's the first time a
    /// keyword or a loc key changes; this cannot. Only the quest-item route composes anything, and
    /// it composes the same two loc keys the tile path does (<c>PrefabName + "_TOOLTIP"</c> /
    /// <c>+ "_DESCR_TOOLTIP"</c>, WSHD.cs:3455-3459) because <c>PropInfo</c> is a private nested
    /// class with no entry point.</para>
    ///
    /// <para><b>A PROP WITH NO RICH CARD STILL GETS THE PLAIN ONE.</b> The rich attempt is
    /// MEASURED, not assumed: <see cref="TryPushRichInfo"/> hides the panel first (so
    /// <c>UIWindow.IsOpen</c> is false whatever a previous hover left behind), calls the route, and
    /// reads <c>IsOpen</c> back. A route that silently bailed — no matching <c>CObjectTrap</c> in
    /// <c>ScenarioState.Props</c>, a transition in flight, the results screen up, gamepad tooltips
    /// suppressed — therefore reports failure and the plain card is shown instead. Showing nothing
    /// is never an outcome.</para>
    ///
    /// <para>Gated on <c>[FigureGrab] HeldFigureInfo</c>, the SAME dial the figure's pickup panel
    /// obeys ("mach diese aber auch optional in dem VR Einstellungen deaktivierbar") — one switch
    /// for "show me what I picked up", not one per kind of thing picked up.</para>
    ///
    /// <para>NO GAME STATE IS WRITTEN, on either route. <c>UITextInfoPanel.Show</c> sets label text
    /// and shows a UIWindow. <c>UIPropInfoPanel.Show*</c> sets its own <c>propType</c> /
    /// <c>_lastSelected*</c> UI fields, rebuilds its effect rows and shows a UIWindow; every
    /// scenario-side touch it makes is a READ (<c>ScenarioState.Props</c>,
    /// <c>Scenario.SLTE.TrapDamage</c>, the localisation tables). Neither touches the prop, the
    /// tile, looting or the turn — and neither does this class.</para>
    /// </summary>
    private void ShowInfo()
    {
        if (!FigureGrabConfig.HeldFigureInfoEnabled || _prop == null)
            return;

        // THE DETAILED CARD FIRST — "immer die detaillierteste Info". The plain card is the
        // fallback, never the default.
        if (TryPushRichInfo(out string route))
        {
            _infoWindow = HeldPropCardWindow.PropInfo;
            _infoRoute = route;
            _infoShown = true;
        }
        else
        {
            BuildInfoText(out _infoTitle, out _infoDescription);
            if (_infoTitle.Length == 0)
            {
                HeldPropCard.EndClaim(); // a rich-window snapshot may be standing; nothing claims it
                return;
            }
            _infoWindow = HeldPropCardWindow.TextInfo;
            _infoRoute = "UITextInfoPanel.Show — " + route;
            _infoShown = PushInfo();
        }

        if (_infoShown)
            HeldPropCard.Claim(this, _infoWindow);
        else
            _infoWindow = HeldPropCardWindow.None;
        // TWO PROPS, ONE WINDOW EACH (user 2026-09-03: a prop in each hand showed only ONE info).
        // Whichever window this hold repopulated was snapshotted for its previous owner just before
        // the overwrite (HeldPropCard.BeforePopulate, called inside both push routes); the Claim
        // above committed that copy if this hold landed in the same window, and a copy taken for a
        // window this hold then did NOT take is destroyed here.
        HeldPropCard.EndClaim();

        _infoLosses = 0;
        _nextInfoCheck = Time.unscaledTime + InfoReassertSeconds;
        LogCardRoute();
    }

    /// <summary>
    /// ONE LINE PER PROP KIND SAYING WHICH WINDOW IT RESOLVED TO — the field that decides the
    /// 2026-09-03 round.
    ///
    /// <para>"All props show the detailed info" is a claim about a ROSTER, and the only honest way
    /// to check it is a log that names, per prop kind, which of the two windows the held card
    /// actually landed in and by which route. A boolean "the panel came up" cannot tell a trap that
    /// got its effect rows from a trap that fell back to a title.</para>
    /// </summary>
    private void LogCardRoute()
    {
        if (_prop == null || _cardRouteLogsLeft <= 0)
            return;
        string kind = $"{_prop.PrefabName}|{_prop.ObjectType}";
        if (!CardRouteKinds.Add(kind))
            return;
        _cardRouteLogsLeft--;
        // HW-VERIFY: this is the roster line for "wenn man etwas in die Hand nimmt immer die
        // detaillierteste Info ... bei allen props". It must stay at a tier the DEFAULT log level
        // prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] held-prop CARD ROUTE for {Label}: "
            + (_infoWindow == HeldPropCardWindow.PropInfo
                ? "the RICH window UIPropInfoPanel ('Prop Info Panel', ID TrapInfoPanel) — it "
                  + "carries the effect rows"
                : _infoWindow == HeldPropCardWindow.TextInfo
                    ? "the PLAIN window UITextInfoPanel ('Text Info Panel', ID TextInfoPanel) — this "
                      + "prop kind has no rich card in the game either, so a laser hover shows the "
                      + "same thing"
                    : "NO CARD AT ALL — neither window could be raised")
            + $"; route = {_infoRoute}. "
            + $"({_cardRouteLogsLeft} more card-route lines this scenario.)");
    }

    /// <summary>
    /// Populate the RICH card the way the pointer hover does, and report whether it took.
    ///
    /// <para>The <c>Hide()</c> that opens this is not cosmetic: it is what makes the
    /// <c>IsOpen</c> read afterwards a MEASUREMENT rather than a leftover. Without it a panel still
    /// standing open from the laser hover that preceded the grab would answer "yes" for a prop whose
    /// route bailed out silently, and the user would get the previous prop's effect rows on the
    /// thing in his hand.</para>
    /// </summary>
    private bool TryPushRichInfo(out string route)
    {
        route = "no rich card for this prop kind";
        if (_visual == null || _prop == null || !Singleton<UIPropInfoPanel>.IsInitialized)
            return false;
        UIPropInfoPanel? panel = Singleton<UIPropInfoPanel>.Instance;
        if (panel == null)
            return false;
        UIWindow? window = panel.GetComponent<UIWindow>();
        if (window == null)
            return false;

        // Resolved from the prop's OWN subtree, so whatever we call passes its own gameObject to
        // the panel and the panel's InstanceName lookup is self-consistent by construction. The
        // prop visual is named exactly CObjectProp.InstanceName ("PrefabName : (guid)") — the game
        // writes that name itself (Choreographer.cs:13161, DelayedDropSMB.cs:182).
        var trap = _visual.GetComponentInChildren<UnityGameEditorTrapProp>(includeInactive: true);
        var hazard = trap != null
            ? null
            : _visual.GetComponentInChildren<UnityGameEditorHazardousTerrainProp>(includeInactive: true);
        var difficult = trap != null || hazard != null
            ? null
            : _visual.GetComponentInChildren<UnityGameEditorDifficultTerrainProp>(includeInactive: true);
        bool questItem = trap == null && hazard == null && difficult == null
                         && _prop.ObjectType == ScenarioManager.ObjectImportType.CarryableQuestItem;

        if (trap == null && hazard == null && difficult == null && !questItem)
            return false;

        // BEFORE the Hide: if the OTHER hand's prop is showing in this window, that Hide destroys
        // its effect rows. The snapshot copy that becomes its card has to be taken from the window
        // as it stands now (HeldPropCard doc, "the displaced writer keeps a copy").
        HeldPropCard.BeforePopulate(this, HeldPropCardWindow.PropInfo);

        try
        {
            panel.Hide(); // clear any previous hover's content AND close the window — see the doc
            if (trap != null)
            {
                route = "UnityGameEditorTrapProp.OnCursorEnter → UIPropInfoPanel.ShowTrap";
                trap.OnCursorEnter();
            }
            else if (hazard != null)
            {
                route = "UnityGameEditorHazardousTerrainProp.OnCursorEnter → "
                        + "UIPropInfoPanel.ShowHazardousTerrain";
                hazard.OnCursorEnter();
            }
            else if (difficult != null)
            {
                route = "UnityGameEditorDifficultTerrainProp.OnCursorEnter → "
                        + "UIPropInfoPanel.ShowDifficultTerrain";
                difficult.OnCursorEnter();
            }
            else
            {
                route = "UIPropInfoPanel.ShowQuestItem (the tile path's recipe, WSHD.cs:3455-3459)";
                BuildQuestItemText(out string qTitle, out string qDescription);
                panel.ShowQuestItem(qTitle, qDescription);
            }
        }
        catch (System.Exception e)
        {
            // A half-built scenario state must never cost the player a card: fall through to the
            // plain one. Logged once, because a route that throws on EVERY grab is a different
            // defect from one that never matched.
            route = $"{route} THREW ({e.GetType().Name}: {e.Message})";
            if (!_loggedRichThrow)
            {
                _loggedRichThrow = true;
                VRLog.Alert("FigureGrab",
                    $"[Props] the RICH held-prop card route threw for {Label} — falling back to the "
                    + $"plain UITextInfoPanel for this hold and every later one this scenario. {route}");
            }
            return false;
        }

        if (window.IsOpen)
            return true;
        route = $"{route} declined (the panel stayed closed: no matching CObject in "
                + "ScenarioState.Props, a transition in flight, the results screen up, or gamepad "
                + "tooltips suppressed)";
        return false;
    }

    /// <summary>The quest-item card's two strings, to the tile path's own recipe
    /// (WSHD.cs:3455-3459 for the loc keys, <c>PropInfo.Get</c> at WSHD.cs:83-107 for the
    /// <c>LootedBy</c> suffix). Composed rather than called because <c>PropInfo</c> is a private
    /// nested class of <c>WorldspaceStarHexDisplay</c> with no entry point — it is the ONE route of
    /// the four that the game does not expose.</summary>
    private void BuildQuestItemText(out string title, out string description)
    {
        string prefab = _prop != null ? _prop.PrefabName : string.Empty;
        title = Translate(prefab + "_TOOLTIP");
        description = Translate(prefab + "_DESCR_TOOLTIP");
        if (_prop == null || string.IsNullOrEmpty(_prop.CanLootLocKey))
            return;
        string fmt = Translate("GUI_PROP_CAN_BE_LOOTED_BY");
        string who = Translate(_prop.CanLootLocKey);
        string lootedBy = fmt.Contains("{0}") ? string.Format(fmt, who) : $"{fmt} {who}";
        description = string.IsNullOrEmpty(description) ? lootedBy : description + "\n" + lootedBy;
    }

    /// <summary>Populate and show the game's text info window. False when the singleton is not up
    /// yet (early scenario load), which simply means no panel for this hold.</summary>
    private bool PushInfo()
    {
        if (!Singleton<UITextInfoPanel>.IsInitialized)
            return false;
        UITextInfoPanel? panel = Singleton<UITextInfoPanel>.Instance;
        if (panel == null)
            return false;
        // Same seam as the rich route: the other hand's prop may be showing in this window, and
        // Show() overwrites its rows in place — snapshot them first.
        HeldPropCard.BeforePopulate(this, HeldPropCardWindow.TextInfo);
        panel.Show(_infoTitle, _infoDescription);
        return true;
    }

    /// <summary>
    /// Keep the card up for the length of the hold — the prop-side twin of
    /// <c>StatPanelSurface.ReassertHeld</c>, which exists for the same reason: the panel is SHARED
    /// with the game's board hover, and the hover keeps taking it back.
    ///
    /// <para><b>AND THE POINT AT WHICH IT CONCEDES.</b> Two writers hide this window while a prop
    /// is held — the game's own <c>ShowTooltipForTile</c> (which hides both info panels on every
    /// hover change, WSHD.cs:3574-3575) and the mod's <c>Board/Patches/HexHoverClear</c>, whose
    /// <c>HideStaleTooltips</c> hides it on every frame the VR pick is not on a hex. Holding a prop
    /// turns the ray OFF, so that second one fires continuously. Re-showing four times a second
    /// against a writer that hides four times a second is a strobe, and this project's standing
    /// rule is not to win a write war but to name the writer: after
    /// <see cref="InfoLossBudget"/> consecutive losses this hold stops re-asserting and prints one
    /// line saying so. The one-line fix on the other side is named in that line.</para>
    /// </summary>
    private void TickInfo()
    {
        if (!_infoShown || _infoLosses > InfoLossBudget)
            return;
        // DISPLACED BY THE OTHER HAND'S PROP: this hold's card is PropInfoSurface's frozen copy,
        // and the live window is showing the later grab. A re-assert here would be the two hands
        // rewriting one window against each other four times a second — the write war this
        // method exists to refuse. The copy needs no re-assert; nothing hides it.
        if (HeldPropCard.IsUnderstudy(this))
            return;
        float now = Time.unscaledTime;
        if (now < _nextInfoCheck)
            return;
        _nextInfoCheck = now + InfoReassertSeconds;

        // WATCH THE WINDOW THIS HOLD ACTUALLY RAISED (ModBuild 366). Watching UITextInfoPanel while
        // the card lives in UIPropInfoPanel would read a permanently-hidden window and concede on
        // the sixth tick of every trap hold — a re-assert aimed at the wrong panel is exactly the
        // "gated remedy that never ran" with the sign flipped.
        UIWindow? window = InfoWindowComponent();
        if (window == null)
            return;
        if (window.IsVisible)
        {
            _infoLosses = 0; // still up (IsVisible is alpha>0, so a fade-IN counts as up)
            return;
        }

        _infoLosses++;
        if (_infoLosses <= InfoLossBudget)
        {
            RePushInfo();
            return;
        }
        if (_loggedInfoWriteWar)
            return;
        _loggedInfoWriteWar = true;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] held-prop INFO CONCEDED for {Label}: the card was re-shown {InfoLossBudget} times in a "
            + $"row and something hid it again inside {InfoReassertSeconds:0.00}s each time, so this hold "
            + "stopped re-asserting rather than strobe the panel. THE WRITER IS ALMOST CERTAINLY "
            + "Board/Patches/HexHoverClear.HideStaleTooltips, which hides UITextInfoPanel on every frame "
            + "the VR pick is not on a hex — and holding a prop turns the hand's ray OFF, so it fires "
            + "every frame for the whole hold. The fix is one line there: return early while "
            + "HeldProps.Count > 0 (a held prop's card is not a stale hover). Logged once per session."
            // ModBuild 366 — the window is no longer always the text one, and the guard above is no
            // longer the only candidate writer. Say which window this hold was actually fighting for.
            + $" THIS HOLD'S CARD WAS IN {_infoWindow} (route {_infoRoute}); HexHoverClear's early "
            + "return covers BOTH panels, so if this line appears with PropInfo the writer is "
            + "elsewhere — most likely the game's own IHoverable OnCursorExit from HoverRegisterer.");
    }

    /// <summary>The <c>UIWindow</c> of whichever panel THIS hold's card is in, or null when that
    /// singleton is not up. One accessor, so the re-assert, the concede test and the take-down can
    /// never disagree about which window they are talking about.</summary>
    private UIWindow? InfoWindowComponent()
    {
        if (_infoWindow == HeldPropCardWindow.PropInfo)
        {
            if (!Singleton<UIPropInfoPanel>.IsInitialized)
                return null;
            UIPropInfoPanel? rich = Singleton<UIPropInfoPanel>.Instance;
            return rich != null ? rich.GetComponent<UIWindow>() : null;
        }
        if (!Singleton<UITextInfoPanel>.IsInitialized)
            return null;
        UITextInfoPanel? plain = Singleton<UITextInfoPanel>.Instance;
        return plain != null ? plain.GetComponent<UIWindow>() : null;
    }

    /// <summary>Re-show the card in the window this hold already resolved to. It never re-runs the
    /// rich/plain ELECTION: that decision was made and logged at the grab, and re-deciding it four
    /// times a second against a panel somebody else keeps hiding is how a card starts alternating
    /// between two windows.</summary>
    /// <remarks>Internal since the two-props build: <see cref="HeldPropCard.Release"/> calls it
    /// on the understudy it promotes when the later prop leaves the hand, so the earlier prop's
    /// content is back in the live window on the same frame its frozen copy goes away.</remarks>
    internal void RePushInfo()
    {
        if (_infoWindow == HeldPropCardWindow.PropInfo)
        {
            if (TryPushRichInfo(out _))
                return;
            // The rich route stopped taking mid-hold (the prop was looted out of ScenarioState, a
            // transition started). Concede to the plain card rather than leave the hand empty.
            BuildInfoText(out _infoTitle, out _infoDescription);
            if (_infoTitle.Length == 0)
                return;
            _infoWindow = HeldPropCardWindow.TextInfo;
            _infoRoute = "UITextInfoPanel.Show — the rich route stopped taking mid-hold";
            if (PushInfo())
                HeldPropCard.Claim(this, _infoWindow);
            HeldPropCard.EndClaim(); // same election hygiene as ShowInfo: no orphaned snapshot
            return;
        }
        PushInfo();
    }

    /// <summary>Take the card down (idempotent). Called from both release paths, so a prop that
    /// leaves the hand never leaves its card behind. Takes down whichever of the two windows THIS
    /// hold raised, and drops the <see cref="HeldPropCard"/> claim so the dock stops following a
    /// prop that is no longer in a hand.</summary>
    private void ClearInfo()
    {
        if (!_infoShown)
            return;
        _infoShown = false;
        _infoLosses = 0;
        HeldPropCardWindow was = _infoWindow;
        _infoWindow = HeldPropCardWindow.None;

        // HIDE FIRST, RELEASE SECOND. Release may promote the other hand's prop back into this
        // very window and re-push its content at once; a Hide after that would take the promoted
        // card down on the frame it came back. The hide only touches the window THIS hold raised,
        // and the other prop is the understudy of that same window, so the order is the whole fix.
        if (was == HeldPropCardWindow.PropInfo)
        {
            if (Singleton<UIPropInfoPanel>.IsInitialized)
            {
                UIPropInfoPanel? rich = Singleton<UIPropInfoPanel>.Instance;
                if (rich != null)
                    rich.Hide(); // the same untyped Hide the game's own IHoverable OnCursorExit calls
            }
        }
        else if (Singleton<UITextInfoPanel>.IsInitialized)
        {
            UITextInfoPanel? panel = Singleton<UITextInfoPanel>.Instance;
            if (panel != null)
                panel.Hide();
        }
        HeldPropCard.Release(this);
    }

    /// <summary>
    /// The card's text, built to the GAME's own recipe so a picked-up prop reads exactly like a
    /// hovered one (<c>WorldspaceStarHexDisplay.PropInfo.Get</c>, WSHD.cs:83-107, and the
    /// collection loop at :3396-3420 — that type is private, so the recipe is reproduced, not
    /// called).
    ///
    /// <para>WHAT IS WORTH SHOWING, and why exactly these three. A prop's identity is its NAME
    /// (localized from <c>PrefabName</c>, which is what the hover card titles itself with);
    /// money is worth its AMOUNT rather than its name, because "GoldPile" tells the player nothing
    /// they cannot see and "2 Gold" is the entire content of the game's own gold card; and a prop
    /// that can only be looted by someone in particular says so, because that is a rule the player
    /// cannot read off the model. <c>PropHealthDetails</c> is included when the prop actually has
    /// health — a destructible obstacle — since that is the one prop stat that behaves like a
    /// figure's. Nothing else on <c>CObjectProp</c> is player-facing: the GUID, the tile index and
    /// the owner GUID are bookkeeping.</para>
    /// </summary>
    private void BuildInfoText(out string title, out string? description)
    {
        title = string.Empty;
        description = null;
        if (_prop == null)
            return;

        string name = Translate(_prop.PrefabName);
        if (_prop.ObjectType == ScenarioManager.ObjectImportType.MoneyToken)
        {
            // The game prices a money token by the scenario's gold conversion, defaulting to 1
            // when the scenario carries no level-table entry (CAbilityLoot.cs:302, CActor.cs:1930).
            int gold = 1;
            try
            {
                CScenario? scenario = ScenarioManager.Scenario;
                if (scenario != null && scenario.SLTE != null)
                    gold = scenario.SLTE.GoldConversion;
            }
            catch
            {
                gold = 1; // a half-built scenario state must never cost the player their card
            }
            title = $"{gold} {Translate("Gold")}";
        }
        else
        {
            title = name;
        }

        var lines = new List<string>(2);
        PropHealthDetails? health = _prop.PropHealthDetails;
        if (health != null && health.HasHealth && health.CurrentHealth > 0)
            lines.Add($"{Translate("HP")}: {health.CurrentHealth}");
        if (!string.IsNullOrEmpty(_prop.CanLootLocKey))
        {
            string fmt = Translate("GUI_PROP_CAN_BE_LOOTED_BY");
            string who = Translate(_prop.CanLootLocKey);
            lines.Add(fmt.Contains("{0}") ? string.Format(fmt, who) : $"{fmt} {who}");
        }
        if (lines.Count > 0)
            description = string.Join("\n", lines);
    }

    /// <summary>Localize, falling back to the raw term. <c>TryGetTranslation</c> rather than
    /// <c>GetTranslation</c> so a missing key returns quietly instead of writing a game-side
    /// error line for every pickup.</summary>
    private static string Translate(string? term)
    {
        if (string.IsNullOrEmpty(term))
            return string.Empty;
        try
        {
            return GLOOM.LocalizationManager.TryGetTranslation(term, out string t)
                   && !string.IsNullOrEmpty(t)
                ? t
                : term!;
        }
        catch
        {
            return term!;
        }
    }

    // ---- the held-visibility WATCH (ModBuild 341) ------------------------------------------------

    /// <summary>
    /// A FLICKER CANNOT BE MEASURED BY A SNAPSHOT, and <see cref="TickProbe"/> is a snapshot.
    ///
    /// <para><b>WHY THIS EXISTS.</b> ModBuild 340 shipped a one-shot census two frames after the
    /// grab. The user came back with "Das Item flackert in der Hand" — a value that ALTERNATES,
    /// which a single sample answers with a coin toss. This watch runs EVERY FRAME for the whole
    /// hold and reports run-lengths and TRANSITION COUNTS instead: how many frames each renderer
    /// was actually drawing, how many times it changed its mind, and — the field that decides the
    /// round — whether the object in the hand is still the SAME OBJECT it was at the grab.</para>
    ///
    /// <para><b>THE DECIDING FIELD IS <c>sameInstance</c>.</b> Two candidate causes survive the
    /// 2026-09-02 log and they need opposite fixes:</para>
    /// <list type="bullet">
    ///   <item><b>RE-INSTANTIATION.</b> Grabbing an Apparance-placed prop makes its map tile
    ///   re-synthesize (eight of the eleven ApparanceEntity-busy events in that session were
    ///   exactly the four DarkPitObstacle grabs and the four releases), and a re-synthesis
    ///   destroys and re-instantiates the tile's placed content. Every new instance is born
    ///   <c>Renderer.enabled = false</c> by <c>MaterialLoaderData.LoadMaterials</c> and only the
    ///   mod's healer ever turns it back on. If that is what is happening, the renderer instance
    ///   ids CHANGE during the hold, or the visual root dies outright — and the fix is that a
    ///   prop like this must be held as a COPY, never as the engine's own object.</item>
    ///   <item><b>PURE WRITE WAR.</b> The same object, toggled. Then the instance ids are STABLE
    ///   while <c>flips</c> climbs — and the fix is an ownership guard on whoever writes it, not
    ///   a copy.</item>
    /// </list>
    ///
    /// <para>COST: one pass over a cached renderer array (never more than two props are live, and
    /// a prop has under two dozen renderers), no allocation per frame; the subtree is re-walked
    /// once every <see cref="WatchRescanFrames"/> frames into a shared scratch list purely to
    /// notice renderers appearing or disappearing.</para>
    /// </summary>
    private const int WatchRescanFrames = 30;

    private static readonly List<Renderer> WatchScratch = new(32);

    private Renderer[]? _watch;
    private bool[]? _watchLast;
    private int[]? _watchFlips;
    private int[]? _watchDrawn;
    private int[]? _watchIds;
    private int _watchFrames;
    private int _watchAllDrawnFrames;
    private int _watchNoneDrawnFrames;
    private int _watchLost;      // renderers that died under us
    private int _watchAppeared;  // renderers that were not there at the grab
    private int _watchRootId;
    private string _watchParent = string.Empty;

    // --- THE LIVE SUBTREE COUNTERS (ModBuild 349). Everything above measures the renderer set
    //     captured AT THE GRAB, which is exactly the set the engine destroys; these measure what
    //     is under the visual root RIGHT NOW, which is what the player is actually looking at.
    private int _liveFrames;         // frames the live pass actually counted (see LiveSettleFrames)
    private int _liveDarkFrames;     // frames on which NOTHING under the root was drawing
    private int _liveRebuiltFrames;  // frames on which the renderer SET was replaced — the churn
    private int _liveMin;
    private int _liveMax;
    private int _liveKey;

    /// <summary>
    /// Frames at the start of a hold whose live census is DISCARDED, and it is not padding.
    ///
    /// <para><c>FigureHighlight</c> parents its glow clones under the prop's own visual root, and
    /// <c>OnGrab</c> tears them down with <c>Object.Destroy</c> — which Unity defers to the end of
    /// the frame. So the first frames of a hold see ~9 clone renderers under the root that are
    /// about to vanish by design, and counting them would report a renderer-set replacement that
    /// nothing is wrong with. This is also the arithmetic behind the ModBuild 348 line reading
    /// "18 renderer(s)" for a prop whose own subtree holds 9: half of that census was the glow.</para>
    /// </summary>
    private const int LiveSettleFrames = 3;

    /// <summary>How many hold-watch verdicts this session prints.</summary>
    private const int WatchBudget = 3;

    private static int _watchesLeft = WatchBudget;

    /// <summary>Arm the watch at the grab: cache the renderer set and its identity.</summary>
    private void BeginWatch()
    {
        // A PROBE THAT HAS ANSWERED IS SPENT. The verdict budget used to gate only the EMIT, so the
        // per-frame walk below went on running for every hold of the session after the last line it
        // could ever print. It gates the ARMING too now: once the budget is out no watch exists at
        // all and TickWatch returns on its first null check.
        if (_visual == null || _watchesLeft <= 0)
            return;
        WatchScratch.Clear();
        _visual.GetComponentsInChildren(includeInactive: true, WatchScratch);
        int n = WatchScratch.Count;
        _watch = new Renderer[n];
        _watchLast = new bool[n];
        _watchFlips = new int[n];
        _watchDrawn = new int[n];
        _watchIds = new int[n];
        for (int i = 0; i < n; i++)
        {
            Renderer r = WatchScratch[i];
            _watch[i] = r;
            _watchIds[i] = r != null ? r.GetInstanceID() : 0;
            _watchLast[i] = r != null && r.enabled && r.gameObject.activeInHierarchy;
        }
        _watchFrames = 0;
        _watchAllDrawnFrames = 0;
        _watchNoneDrawnFrames = 0;
        _watchLost = 0;
        _watchAppeared = 0;
        _watchRootId = _visual.GetInstanceID();
        Transform? p = _visual.transform.parent;
        _watchParent = p != null ? p.name : "<scene root>";
        _liveFrames = 0;
        _liveDarkFrames = 0;
        _liveRebuiltFrames = 0;
        _liveMin = int.MaxValue;
        _liveMax = 0;
        _liveKey = 0;
    }

    /// <summary>One frame of the watch. Counts a TRANSITION whenever a renderer's drawing state
    /// differs from the previous frame — that count, not any single reading, is the flicker.</summary>
    private void TickWatch()
    {
        Renderer[]? watch = _watch;
        if (watch == null || _watchLast == null || _watchFlips == null || _watchDrawn == null)
            return;

        _watchFrames++;
        int drawing = 0, alive = 0;
        for (int i = 0; i < watch.Length; i++)
        {
            Renderer r = watch[i];
            if (r == null)
            {
                if (_watchLast[i])
                {
                    _watchLast[i] = false;
                    _watchFlips[i]++;
                }
                continue;
            }
            alive++;
            bool now = r.enabled && r.gameObject.activeInHierarchy;
            if (now)
            {
                drawing++;
                _watchDrawn[i]++;
            }
            if (now != _watchLast[i])
            {
                _watchLast[i] = now;
                _watchFlips[i]++;
            }
        }
        if (alive > 0 && drawing == alive)
            _watchAllDrawnFrames++;
        if (drawing == 0)
            _watchNoneDrawnFrames++;

        if (_visual == null)
            return;

        // ---- THE LIVE SUBTREE, the half the ModBuild 341 instrument could not see -------------
        //
        // Everything above measures the renderer set captured AT THE GRAB — which is precisely the
        // set the engine destroys. Once it is gone every entry reads null, and the counters above
        // report "nothing drawing" for the rest of the hold: correct, and useless, because the
        // player is looking at the NEW renderers the watch never armed on. "348 of 351 frames with
        // NOTHING drawing" was that blind spot, not a measurement of the hand.
        //
        // So the subtree is re-read every frame and asked the two questions that decide the round:
        // was ANYTHING drawing, and was the renderer SET replaced since last frame. The second one
        // is the churn itself, finally expressed as a number instead of as an absence.
        //
        // COST: one allocation-free GetComponentsInChildren over a sub-two-dozen-node subtree, for
        // at most two held props, and ONLY while a verdict budget remains (see BeginWatch).
        WatchScratch.Clear();
        _visual.GetComponentsInChildren(includeInactive: true, WatchScratch);
        int liveTotal = 0, liveDrawn = 0, key = 17;
        for (int i = 0; i < WatchScratch.Count; i++)
        {
            Renderer live = WatchScratch[i];
            if (live == null)
                continue;
            liveTotal++;
            if (live.enabled && live.gameObject.activeInHierarchy)
                liveDrawn++;
            unchecked { key = (key * 31) + live.GetInstanceID(); }
        }
        if (_watchFrames > LiveSettleFrames)
        {
            _liveFrames++;
            if (liveDrawn == 0)
                _liveDarkFrames++;
            if (liveTotal < _liveMin)
                _liveMin = liveTotal;
            if (liveTotal > _liveMax)
                _liveMax = liveTotal;
            if (_liveFrames > 1 && key != _liveKey)
                _liveRebuiltFrames++;
        }
        _liveKey = key;

        if (_watchFrames % WatchRescanFrames != 0)
            return;
        // Cheap structural re-check on the list the live pass just filled: has the subtree gained
        // or lost renderers under us? A re-instantiated Apparance object shows up as BOTH at once.
        int lost = 0;
        for (int i = 0; i < watch.Length; i++)
        {
            if (watch[i] == null)
                lost++;
        }
        _watchLost = lost;
        int appeared = WatchScratch.Count - (watch.Length - lost);
        if (appeared > _watchAppeared)
            _watchAppeared = appeared;
    }

    /// <summary>
    /// The verdict, printed once per hold. <paramref name="reason"/> names how the hold ended, and
    /// "visual DESTROYED mid-hold" is itself an answer — see the class doc for what
    /// <c>sameInstance</c> decides.
    /// </summary>
    private void EmitWatch(string reason)
    {
        Renderer[]? watch = _watch;
        int[]? flips = _watchFlips;
        int[]? drawn = _watchDrawn;
        int[]? ids = _watchIds;
        _watch = null;
        _watchLast = null;
        _watchFlips = null;
        _watchDrawn = null;
        _watchIds = null;
        if (watch == null || flips == null || drawn == null || ids == null || _watchFrames <= 0)
            return;
        if (_watchesLeft <= 0)
            return;
        _watchesLeft--;

        int totalFlips = 0, worst = -1, worstFlips = -1, sameInstance = 0, checkedIds = 0;
        for (int i = 0; i < watch.Length; i++)
        {
            totalFlips += flips[i];
            if (flips[i] > worstFlips)
            {
                worstFlips = flips[i];
                worst = i;
            }
            Renderer r = watch[i];
            if (r == null)
                continue;
            checkedIds++;
            if (r.GetInstanceID() == ids[i])
                sameInstance++;
        }
        string worstName = worst >= 0 && watch[worst] != null ? watch[worst]!.name : "<destroyed>";
        int worstDrawn = worst >= 0 ? drawn[worst] : 0;
        bool rootAlive = _visual != null && _visual.GetInstanceID() == _watchRootId;
        string parentNow = _visual != null
            ? (_visual.transform.parent != null ? _visual.transform.parent.name : "<scene root>")
            : "<destroyed>";
        int liveMin = _liveMin == int.MaxValue ? 0 : _liveMin;

        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] HOLD WATCH for {Label} ({reason}) over {_watchFrames} frame(s): "
            + $"{totalFlips} enable/disable TRANSITION(s) across {watch.Length} renderer(s); "
            + $"worst '{worstName}' flipped {worstFlips}x and drew on {worstDrawn} of {_watchFrames} "
            + $"frame(s); {_watchAllDrawnFrames} frame(s) with EVERYTHING drawing, "
            + $"{_watchNoneDrawnFrames} frame(s) with NOTHING drawing. "
            + $"sameInstance={sameInstance}/{checkedIds} renderer(s) kept their instance id, "
            + $"rootAlive={rootAlive}, lost={_watchLost}, appeared={_watchAppeared}; "
            + $"parent '{_watchParent}' -> '{parentNow}'. "
            + $"LIVE SUBTREE (what the player could actually see): rebuilt={_liveRebuiltFrames}, "
            + $"dark={_liveDarkFrames} of {_liveFrames}, present={liveMin}..{_liveMax} renderer(s), "
            + $"apparanceFrozen={_frozenCount}. "
            + "READ IT LIKE THIS, AND READ rebuilt FIRST. rebuilt is the number of frames on which "
            + "the prop's renderer SET was REPLACED — frames on which the ENGINE DESTROYED AND "
            + "RE-INSTANTIATED the content in the hand, each new renderer born Renderer.enabled=false "
            + "from MaterialLoaderData.LoadMaterials. That, not a write war, is what made a held prop "
            + "flicker: an Apparance prop rebuilds itself whenever it is TRANSFORMED (the plugin's "
            + "own tooltip on ApparanceEntity.MonitorMovement says so in those words), and a prop in "
            + "a moving hand is transformed every frame. ModBuild 349 turns that flag off for the "
            + "hold, so a FIXED hold reads rebuilt=0 with apparanceFrozen>=1 and dark far below the "
            + "frame count. rebuilt still climbing WITH apparanceFrozen>=1 means the rebuild has a "
            + "second trigger and the next place to look is ApparanceEngine.RequestRebuild, which "
            + "rebuilds every entity in the scene at once. apparanceFrozen=0 means this prop is not "
            + "an Apparance prefab at all and its flicker has another cause — then read sameInstance: "
            + "below its total (or rootAlive=False, or lost/appeared above 0) something ELSE is "
            + "re-instantiating it; at full with transitions climbing it is a plain write war on "
            + "stable renderers and the fix is an ownership guard on the writer. rebuilt=0 with "
            + "dark=0 means the hold is clean and the complaint is about something other than "
            + "whether the prop was drawn. "
            + $"({_watchesLeft} more hold watches this session.)");
    }

    // ---- the held-visibility probe (defect (a)) --------------------------------------------------

    /// <summary>Frame at which the held-visibility probe fires, or 0 when it is not armed.</summary>
    private int _probeFrame;

    /// <summary>How many held-visibility probes this session prints. The question it answers is
    /// "is the prop in the hand actually being DRAWN", which the first few grabs settle
    /// completely.</summary>
    private const int ProbeBudget = 3;

    private static int _probesLeft = ProbeBudget;

    /// <summary>
    /// TWO FRAMES AFTER THE GRAB, READ BACK THE PICTURE — not the state we just wrote.
    ///
    /// <para>ModBuild 338 shipped a grab line that proved the mod had DONE the grab and proved
    /// nothing at all about whether the player could see the result; the user's answer was "in der
    /// Hand ist es garnicht oder nur immer ganz kurz für einen Frame sichtbar". Two frames is
    /// deliberately after the first wall-fade apply pass, which is the writer this round found.
    /// The three fields below discriminate the three candidate causes in one line, so a follow-up
    /// round never has to guess:</para>
    /// <list type="bullet">
    ///   <item><c>enabled=false</c> on renderers we never touched ⇒ something DISABLED them; the
    ///   only thing in this mod that does that to airborne scenery is <c>WallSegmentFade</c>.</item>
    ///   <item>a MATERIAL PROPERTY BLOCK present with <c>_Toggle_Dissolve</c> at 1 ⇒ the same
    ///   system, through its dissolve channel rather than the enabled flag — and that latch
    ///   survives the reparent, so it can hide a prop that was adopted BEFORE the grab.</item>
    ///   <item>everything enabled, no block, and the player still sees nothing ⇒ neither; look at
    ///   the shader's own screen-space occlusion term (<c>_TilesOcclusionMap</c> /
    ///   <c>_ObjectOcclusion</c>, generated from the PARKED flat camera and therefore wrong for the
    ///   head camera) — see <c>Compat/WallFadeDisable</c>.</item>
    /// </list>
    /// </summary>
    private void TickProbe()
    {
        if (_probeFrame == 0 || Time.frameCount < _probeFrame)
            return;
        _probeFrame = 0;
        if (_probesLeft <= 0 || _visual == null)
            return;
        _probesLeft--;

        Renderer[] renderers = _visual.GetComponentsInChildren<Renderer>(includeInactive: true);
        int total = renderers.Length, drawn = 0, active = 0, blocked = 0, dissolved = 0;
        int minLayer = int.MaxValue, maxLayer = int.MinValue;
        var block = new MaterialPropertyBlock();
        int dissolveId = Shader.PropertyToID("_Toggle_Dissolve");
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            if (r == null)
                continue;
            if (r.gameObject.activeInHierarchy)
                active++;
            if (r.enabled && r.gameObject.activeInHierarchy)
                drawn++;
            int layer = r.gameObject.layer;
            if (layer < minLayer) minLayer = layer;
            if (layer > maxLayer) maxLayer = layer;
            if (!r.HasPropertyBlock())
                continue;
            blocked++;
            r.GetPropertyBlock(block);
            if (block.GetFloat(dissolveId) > 0.5f)
                dissolved++;
        }

        Camera? head = Camera.main;
        Vector3 pos = _visual.transform.position;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] HELD? two frames after the grab of {Label}: {drawn} of {total} renderer(s) are "
            + $"actually drawing ({active} on active objects); layer(s) {minLayer}..{maxLayer}; "
            + $"{blocked} carry a MaterialPropertyBlock, {dissolved} of those with _Toggle_Dissolve ON. "
            + $"World position {pos.x:0.00},{pos.y:0.00},{pos.z:0.00}"
            + (head != null ? $", {Vector3.Distance(pos, head.transform.position):0.00} wu from the head camera" : string.Empty)
            + ". READ IT LIKE THIS: drawn < total on renderers this class never touched means something "
            + "DISABLED them, and the only thing in this mod that disables airborne scenery is "
            + "WallSegmentFade; a property block with _Toggle_Dissolve ON is the same system going "
            + "through its dissolve channel instead, and that latch survives the reparent so it can "
            + "predate the grab; all drawn, no block, and still nothing visible means neither, and the "
            + "next suspect is the prop shader's own screen-space occlusion term. "
            + $"({_probesLeft} more held-prop probes this session.)");
    }

    // ---- THE APPARANCE FREEZE (ModBuild 349) ----------------------------------------------------

    /// <summary>
    /// WHY A HELD PROP FLICKERED FOR THREE ROUNDS, AND THE ONE FIELD THAT ENDS IT.
    ///
    /// <para><b>THE READING (ModBuild 348 hardware log, three holds, three identical verdicts).</b>
    /// <c>sameInstance=0/0 … rootAlive=True, lost=18, appeared=9; parent 'Socket_Grab' -&gt;
    /// 'Socket_Grab'</c>, and beside it <c>MaterialLoaderHeal FAST LANE: finished 9 renderer(s)
    /// THIS FRAME (first 'CV_Generic_Rock_03')</c> printed on three CONSECUTIVE frames. Every
    /// renderer the watch armed on had been destroyed by the end of the hold and new ones stood in
    /// their place, while the root this class reparented stayed alive on the hand. The content in
    /// the palm was being destroyed and re-instantiated, over and over, for the whole hold.</para>
    ///
    /// <para><b>THE CAUSE, read out of the engine's own source rather than inferred from
    /// correlation.</b> A prop is spawned from
    /// <c>GlobalSettings.GetApparancePropPrefab(PrefabName)</c> (Choreographer.cs:13140) — the
    /// APPARANCE prefab first, a plain prefab only as the fallback — and that prefab's root carries
    /// <c>ProceduralProp</c>, which is <c>[RequireComponent(typeof(ApparanceEntity))]</c>. So the
    /// GameObject <c>ObjectCacheService.GetPropObject</c> hands us, the one this class puts in the
    /// hand, IS an Apparance entity, and its visible meshes are that entity's generated content.
    /// Then, in Apparance.Unity, verbatim:</para>
    /// <code>
    /// [Tooltip("EXPERIMENTAL USE ONLY: By default, transforming an Entity causes a re-build
    ///          if it's procedural content.")]
    /// public bool MonitorMovement = true;
    /// </code>
    /// <para>and the machinery that sentence describes, running every frame:
    /// <c>ApparanceEngine.Update</c> → <c>EntitiesGameTick</c> → <c>ApparanceEntity.GameTick</c> →
    /// <c>CheckEntity</c> → <c>MonitorBounds(force_apply: false)</c>, which fires on
    /// <c>m_BoundsComponent.transform.hasChanged</c> — the ENTITY'S OWN transform — and calls
    /// <c>SetProcedureBoundsFromEntityBounds(force_apply: false, allow_refresh: MonitorMovement)</c>,
    /// whose only job when <c>allow_refresh</c> is true is to set <c>m_RequestRefresh</c>. The next
    /// <c>CheckEntity</c> runs <c>m_Entity.Refresh()</c>, and a refresh destroys the placed objects
    /// and instantiates them again — there is no pooling anywhere in Apparance.Unity, the finding
    /// <c>Core/Water/WaterTerrainVR.cs</c> already recorded in ModBuild 159. Every new instance is
    /// born <c>Renderer.enabled = false</c> from <c>MaterialLoaderData.LoadMaterials</c>, and only
    /// this mod's healer ever turns it back on.</para>
    ///
    /// <para><b>SO WE WERE THE CHURN.</b> Holding a prop moves it on every single frame — the hand
    /// moves, and <see cref="ApplyHeldPose"/> re-asserts the anchor-local TRS on top of that — so
    /// <c>Transform.hasChanged</c> is true every frame, a refresh is requested every frame, and the
    /// thing in the palm is rebuilt from scratch faster than anything can finish making it visible.
    /// "348 of 351 frames with nothing drawing" was never a strobe; it was a rebuild loop, and the
    /// mod's own per-frame pose write was one half of it.</para>
    ///
    /// <para><b>THE FIX IS THE FIELD THE PLUGIN AUTHOR WROTE FOR EXACTLY THIS.</b> For the length
    /// of the hold, <c>MonitorMovement</c> is false on every Apparance entity in the prop's
    /// subtree, and the value each one had is handed back on landing. Nothing else is touched:
    /// <c>GameTick</c> / <c>CheckEntity</c> / <c>MonitorBounds</c> all still run, so the entity's
    /// <c>m_EntityBounds</c> stays in step with wherever the prop actually is, and a refresh
    /// requested for a REAL reason (a style rebuild, an <c>IsPopulated</c> flip) still happens. The
    /// only thing suppressed is "this moved, therefore rebuild it".</para>
    ///
    /// <para><b>WHY NOT <c>Frozen</c>, the other candidate flag.</b> <c>ApparanceEngine
    /// .EntitiesGameTick</c> skips a frozen entity's <c>GameTick</c> outright, so its bounds would
    /// go stale against the hand and every legitimate refresh would be lost with it — and the flag
    /// is CLEARED out from under its owner by <c>RequestEntityRefresh</c>, so it is not even a
    /// latch we could rely on. A blunt instrument where a precise one exists.</para>
    ///
    /// <para><b>WHY NOT A COPY IN THE HAND</b>, which is the fix the ModBuild 341 note predicted
    /// this reading would call for. A copy removes nothing: the engine's own object would still be
    /// standing on its hex in full view, so the player would see the prop, its ghost AND the copy —
    /// three things where there should be two. Hiding the original is not available either, because
    /// hiding means <c>Renderer.enabled = false</c> on renderers the engine destroys and replaces
    /// several times a second, against a healer whose whole job is to re-enable what it finds
    /// disabled. And it would cost an <c>Instantiate</c> of the entire subtree inside one 11.11 ms
    /// frame. Freezing the rebuild removes the CAUSE, and the hand goes on holding the real prop —
    /// so the home cell keeps exactly what it has today: the ghost, and nothing else.</para>
    ///
    /// <para><b>NO GAME STATE IS WRITTEN.</b> <c>MonitorMovement</c> is a local presentation switch
    /// on a procedural-content component: it decides when THIS client re-synthesizes a mesh. It is
    /// not on the wire, not in <c>ScenarioState</c>, and no rule reads it. Peers run their own
    /// engines against their own viewpoints — the same ground <c>Core/Environment
    /// /ApparanceDetailFocus</c> already stands on. The prop hold stays LOCAL-ONLY in this build.</para>
    ///
    /// <para><b>COST.</b> One <c>GetComponentsInChildren</c> at the grab and one array walk at the
    /// thaw, on at most two props. Nothing per frame, and no scene query ever.</para>
    /// </summary>
    private ApparanceEntity[]? _frozenEntities;
    private bool[]? _frozenMonitor;

    /// <summary>How many Apparance entities this hold froze. Reported by the hold watch, because
    /// "the fix ran and did not work" and "the fix found nothing to freeze" look identical in every
    /// other field of that line.</summary>
    private int _frozenCount;

    /// <summary>
    /// Frames to wait after the prop is home before <c>MonitorMovement</c> goes back on.
    ///
    /// <para>NOT COSMETIC PADDING. While the flag is off, <c>MonitorBounds</c> still runs and keeps
    /// <c>m_EntityBounds</c> tracking the prop — which during a hold means it tracks the HAND.
    /// Restoring the flag on the same frame the prop lands would let the landing write's own
    /// <c>hasChanged</c> request one final refresh, i.e. a visible rebuild of the prop on its cell
    /// on every single release. Waiting instead lets Apparance's next tick consume that
    /// <c>hasChanged</c> while <c>allow_refresh</c> is still false: the bounds are re-synced to the
    /// HOME pose, the flag is cleared, and nothing is left pending when monitoring resumes. Two
    /// frames is the minimum that can do it; three is the margin.</para>
    /// </summary>
    private const int ThawDelayFrames = 3;

    /// <summary>Props whose <c>MonitorMovement</c> restore is pending. Never more than two.</summary>
    private static readonly List<GrabbableProp> Thawing = new(2);

    private static readonly List<ApparanceEntity> EntityScratch = new(4);

    private int _thawFrame;

    private static bool _loggedFreeze;

    /// <summary>Turn rebuild-on-move off for the whole hold. Idempotent, and a re-grab during a
    /// pending thaw simply cancels the thaw and keeps the freeze it already has.</summary>
    private void FreezeApparance()
    {
        Thawing.Remove(this);
        _thawFrame = 0;
        if (_frozenEntities != null || _visual == null)
            return;

        EntityScratch.Clear();
        _visual.GetComponentsInChildren(includeInactive: true, EntityScratch);
        _frozenEntities = EntityScratch.Count > 0
            ? EntityScratch.ToArray()
            : System.Array.Empty<ApparanceEntity>();
        _frozenMonitor = new bool[_frozenEntities.Length];
        _frozenCount = 0;
        for (int i = 0; i < _frozenEntities.Length; i++)
        {
            ApparanceEntity e = _frozenEntities[i];
            if (e == null)
                continue;
            _frozenMonitor[i] = e.MonitorMovement;
            e.MonitorMovement = false;
            _frozenCount++;
        }

        if (_loggedFreeze)
            return;
        _loggedFreeze = true;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] Apparance rebuild-on-move FROZEN for the hold of {Label}: {_frozenCount} "
            + "ApparanceEntity(s) in this prop's subtree had MonitorMovement turned off (restored "
            + $"{ThawDelayFrames} frame(s) after it lands). THIS IS THE ModBuild 349 FIX, and this "
            + "is the line that says whether it applied. A prop spawns from GetApparancePropPrefab, "
            + "so its root carries ProceduralProp and therefore an ApparanceEntity, and the plugin's "
            + "own tooltip on that field reads 'By default, transforming an Entity causes a re-build "
            + "if it's procedural content' — which is why a prop in a moving hand destroyed and "
            + "re-instantiated its own meshes every frame, each new one born Renderer.enabled=false. "
            + "A COUNT OF 0 HERE MEANS THE FIX DID NOT APPLY to this prop (it fell back to a plain "
            + "prefab) and its flicker, if any, has a different cause — read the HOLD WATCH line's "
            + "rebuilt/sameInstance fields next. Logged once per scenario.");
    }

    /// <summary>Schedule the <c>MonitorMovement</c> restore for <see cref="ThawDelayFrames"/> frames
    /// from now — see that constant for why it is not immediate. A no-op for a prop that never
    /// froze anything.</summary>
    private void ScheduleThaw()
    {
        if (_frozenEntities == null)
            return;
        _thawFrame = Time.frameCount + ThawDelayFrames;
        if (!Thawing.Contains(this))
            Thawing.Add(this);
    }

    /// <summary>Hand every captured <c>MonitorMovement</c> back, now. Idempotent.</summary>
    private void ThawApparance()
    {
        Thawing.Remove(this);
        _thawFrame = 0;
        ApparanceEntity[]? entities = _frozenEntities;
        bool[]? monitor = _frozenMonitor;
        _frozenEntities = null;
        _frozenMonitor = null;
        if (entities == null || monitor == null)
            return;
        for (int i = 0; i < entities.Length && i < monitor.Length; i++)
        {
            ApparanceEntity e = entities[i];
            if (e != null)
                e.MonitorMovement = monitor[i];
        }
    }

    /// <summary>One call per frame from <see cref="PropGrab.Tick"/>: give back the
    /// <c>MonitorMovement</c> of any prop whose settle delay has elapsed. One <c>List.Count</c>
    /// compare in the steady state, and it is deliberately ABOVE the feature gate for the same
    /// reason the glide is — a dial turned off mid-flight must not strand a frozen entity.</summary>
    internal static void TickThaws()
    {
        for (int i = Thawing.Count - 1; i >= 0; i--)
        {
            GrabbableProp p = Thawing[i];
            if (Time.frameCount >= p._thawFrame)
                p.ThawApparance();
        }
    }

    /// <summary>Give every pending <c>MonitorMovement</c> back IMMEDIATELY — scenario teardown, the
    /// feature dial going off, module shutdown. A pending restore must never outlive the registry
    /// that would have completed it, or a prop would be left unable to rebuild for the rest of the
    /// session. One rebuild on the cell is the price, and it is the right one.</summary>
    internal static void FlushThaws()
    {
        for (int i = Thawing.Count - 1; i >= 0; i--)
            Thawing[i].ThawApparance();
        Thawing.Clear();
    }

    // ---- THE MAP-ITEM STRETCH (ModBuild 362) ----------------------------------------------------

    /// <summary>
    /// RESIZE A HELD MAP ITEM WITH THE OTHER HAND — the figure's gesture, on a prop, through the
    /// figure's own code.
    ///
    /// <para><b>THE REPORT (2026-09-03), verbatim.</b> <i>"Die props in der Hand soll man wie die
    /// Figuren entsprechend auf skallieren können! (mit exakt den selben Lösungen auf die Probleme
    /// die dafür schon implementiert wurden, nuzte am Besten denselben Code wenmöglich)."</i></para>
    ///
    /// <para><b>WHAT IS REUSED, WHICH IS ALMOST ALL OF IT.</b> The gesture itself —
    /// <see cref="FigureStretch"/> — is not duplicated, not forked and not parameterised: it was
    /// re-typed onto <see cref="StretchTarget"/>, a ten-member adapter, and a held prop answers the
    /// same ten questions a held mini does. So a chest inherits every lesson that file paid for,
    /// unchanged:</para>
    /// <list type="bullet">
    ///   <item><b>REAL METRES AT THE HAND</b> (FigureStretch.cs:21-26). Both gesture distances and
    ///   the capture test divide by the hand's rig scale, so a diorama zoom mid-gesture cannot
    ///   masquerade as hand motion. Nothing prop-specific: the divisor is the HAND's.</item>
    ///   <item><b>SURFACE-BASED CAPTURE, CENTRE-BASED RATIO</b> (FigureStretch.cs:59-64). The zone
    ///   is measured to the nearest point of the item's visible body (so it scales with the item
    ///   by construction — <see cref="HeldRenderers"/> feeds it), while d0/d stay measured to
    ///   <see cref="TryGetHeldCenter"/>, because a surface point moves WITH the scale being written
    ///   and would feed the output back into the input. This matters MORE for a chest than for a
    ///   mini: a hex-sized box has a surface far from its centre, so a centre-based zone would sit
    ///   inside the model.</item>
    ///   <item><b>THE CLAMP BOUNDS THE TOTAL, NOT THE BARE FACTOR</b> (FigureStretch.cs:28-33).
    ///   <see cref="GetStretchFactorBounds"/> converts the total bounds at this hold's own latch
    ///   ratio, and <see cref="ApplyGrabTimeStretchClamp"/> is the grab-time half — clamping the
    ///   factor alone let a figure grabbed at 2× reach 6×, and would do exactly the same to a
    ///   chest grabbed while zoomed in.</item>
    ///   <item><b>THE INTERACTION ZONE SCALES IN BOTH DIRECTIONS</b> (FigureStretch.cs:41-48,
    ///   FigureStretchMath.cs:18-31,53-58). <see cref="TotalHeldSizeRatio"/> feeds
    ///   <c>FigureStretchMath.CaptureCeilingRealMeters</c>, whose <c>Max(1, …)</c> floor is the
    ///   shrink-side symmetry. That file is pure arithmetic and is reused VERBATIM — it never knew
    ///   what a figure was.</item>
    ///   <item><b>THE ANCHOR-LOCAL LATCH</b> (FigureGrabbable.cs:140-150). Already the prop's own
    ///   rule since ModBuild 349: <see cref="_heldLocalScale"/> is read back off the
    ///   <c>worldPositionStays</c> reparent, so no opinion about which transform in the chain
    ///   carries the zoom is needed.</item>
    ///   <item><b>THE GESTURE FACTOR STAYS SEPARATE AND FACTORABLE</b> (FigureGrabbable.cs:173-180).
    ///   <see cref="_stretch"/> is never folded into the latch, because the latch is "the size it
    ///   entered the hand at" and the size BOUNDS are stated against the product of the two.</item>
    /// </list>
    ///
    /// <para><b>THE DIALS ARE THE FIGURES', AND THAT IS A DECISION, NOT AN OMISSION.</b> ModBuild
    /// 350 gave map items their own eight held-POSE keys because he asked for them and because
    /// those numbers are ABSOLUTE GEOMETRY — metres out of the palm and degrees of pitch, and a
    /// hex-sized box does not want a 30 mm miniature's offsets. The four stretch dials are not
    /// that kind of number. <c>[FigureGrab] StretchScaleMin/Max</c> bound the TOTAL size as a
    /// RATIO of the object's own board size at the default zoom, so 0.5×..3× means "half to three
    /// times this chest" exactly as it means "half to three times this mini";
    /// <c>StretchReachMillimeters</c> is real millimetres from the object's own SURFACE, with a
    /// sanity ceiling that already scales with the held size. Every one of the four is size-neutral
    /// by construction, so a separate prop copy would be four more settings whose correct value is
    /// provably the same number — and this project has already paid for a dial the player could not
    /// find. The captions and the German help text were widened instead, so the menu says these
    /// govern figures AND map items. Splitting them later is purely additive and the exact patch is
    /// written out in <c>.planning/LANE-PROPS-357-NEEDED-OUTSIDE.md</c>.</para>
    ///
    /// <para><b>MULTIPLAYER — NO WIRE RECORD IS NEEDED, and this is a conclusion rather than a
    /// deferral.</b> A figure's stretch needs record 30 because a peer RENDERS the held figure and
    /// cannot derive the manual factor from anything it has. A prop hold is local-only by
    /// construction: <see cref="HeldProps"/> sends nothing, <see cref="CanGrab"/> consults no
    /// remote lock, <see cref="PropHeldPose"/> states the same, and a peer therefore never draws a
    /// held prop AT ALL — there is no mirrored visual for a factor to be wrong on. Sending one
    /// would be a number nothing reads. When prop holds DO go on the wire, the stretch is part of
    /// that record from the start (the standing ruling: it syncs fully or not at all, and the
    /// OWNER's value drives every viewer).</para>
    ///
    /// <para><b>THE APPARANCE FREEZE IS UNAFFECTED</b> (see <see cref="FreezeApparance"/> and
    /// <see cref="ThawDelayFrames"/>). Writing the scale every frame adds nothing new while
    /// <c>MonitorMovement</c> is false — <see cref="ApplyHeldPose"/> was already the per-frame
    /// <c>Transform.hasChanged</c> producer the freeze exists to neutralise. And the thaw's
    /// three-frame settle still holds, because the stretch cannot be pending at landing: the
    /// gesture ends the instant the hold does (<c>FigureStretch.TickActive</c> checks
    /// <see cref="IsHeld"/> first), <see cref="SetStretch"/> is a no-op once <c>_attached</c> is
    /// false, and <see cref="FinishGlide"/> writes the exact home <c>_origLocalScale</c> BEFORE
    /// scheduling the thaw. So the landing pose is still the only pending change when
    /// <c>MonitorMovement</c> comes back.</para>
    /// </summary>
    private Vector3 HeldLocalScale() => _heldLocalScale * _stretch;

    /// <summary>The manual in-hand stretch factor of this hold (1 = untouched).</summary>
    internal float Stretch => _stretch;

    /// <summary>Write the factor and re-assert the held pose in the same call, so the item tracks
    /// the gesture hand within the frame. A dumb store on purpose — <see cref="FigureStretch"/>
    /// owns the clamp, so nothing can ever see two differently-clamped values. Harmless once the
    /// hold has ended: <see cref="ApplyHeldPose"/> returns on its own <c>_attached</c> test, which
    /// is what keeps a last gesture frame from writing an anchor-local scale onto a prop that has
    /// already been reparented for its release glide.</summary>
    internal void SetStretch(float factor)
    {
        _stretch = factor;
        ApplyHeldPose();
    }

    /// <summary>
    /// The GESTURE half of the total size bound: the per-hold factor envelope
    /// <see cref="FigureStretch"/> clamps into, derived by converting the TOTAL bounds
    /// (<c>[FigureGrab] StretchScaleMin/Max</c>) at this latch's ratio — total = ratio × factor, so
    /// factor is in [Min/ratio .. Max/ratio]. Recomputed per call so a live dial edit governs the
    /// very next gesture frame. <c>FigureGrabbable.GetStretchFactorBounds</c>, line for line.
    /// </summary>
    internal void GetStretchFactorBounds(out float min, out float max)
    {
        if (!FigureGrabConfig.StretchLimitsEnabled)
        {
            min = FigureGrabConfig.StretchHardFloor;
            max = float.MaxValue;
            return;
        }
        float ratio = Mathf.Max(_latchTotalRatio, 1e-6f);
        min = FigureGrabConfig.StretchScaleMinValue / ratio;
        max = FigureGrabConfig.StretchScaleMaxValue / ratio;
    }

    /// <summary>
    /// GRAB-TIME half of the total size bound — <c>FigureGrabbable.ApplyGrabTimeStretchClamp</c>,
    /// on a prop. Computes the zoom ratio the fresh latch stands at (the item's held size relative
    /// to its board-home size at the DEFAULT diorama zoom) and, while
    /// <c>[FigureGrab] StretchLimits</c> is on, scales the latch so that ratio lands exactly ON the
    /// violated bound. Runs BEFORE the first <see cref="ApplyHeldPose"/> of the hold, so the
    /// clamped size is the first held frame ever rendered — no pop.
    ///
    /// <para>Degenerate input (dead rig, zero anchor scale, non-finite ratio) leaves the latch
    /// alone: a bounds feature must never be the thing that breaks a grab.</para>
    /// </summary>
    private void ApplyGrabTimeStretchClamp(Transform anchor)
    {
        _latchTotalRatio = 1f;
        float baseScale = GloomhavenVR.Rig.RigTarget.BaseScale;
        float anchorScale = anchor.lossyScale.x;
        if (baseScale <= 1e-6f || anchorScale <= 1e-6f)
            return;
        float ratio = baseScale / anchorScale;
        if (float.IsNaN(ratio) || float.IsInfinity(ratio) || ratio <= 0f)
            return;
        _latchTotalRatio = ratio;

        if (!FigureGrabConfig.StretchLimitsEnabled)
            return; // limits off: the latch keeps the true grab-zoom size, whatever it is
        float min = FigureGrabConfig.StretchScaleMinValue;
        float max = FigureGrabConfig.StretchScaleMaxValue;
        float clamped = Mathf.Clamp(ratio, min, max);
        if (Mathf.Approximately(clamped, ratio))
            return;
        _heldLocalScale *= clamped / ratio; // uniform trim — the latch's own frame, no reparent
        _latchTotalRatio = clamped;
    }

    /// <summary>This hold's TOTAL size in default-zoom units — the very product
    /// <c>[FigureGrab] StretchScaleMin/Max</c> bound, and the ZOOM-INDEPENDENT size variable the
    /// capture ceiling scales by (<c>FigureStretchMath.CaptureCeilingRealMeters</c>).</summary>
    internal float TotalHeldSizeRatio => _latchTotalRatio * _stretch;

    /// <summary>The held item's centre in world space — its visual root's position, deliberately
    /// NOT a surface point (which would move with the gesture and feed the scale back into the
    /// distance that drives it). False when not attached to a hand.</summary>
    internal bool TryGetHeldCenter(out Vector3 world)
    {
        world = default;
        if (!_attached || _visual == null)
            return false;
        world = _visual.transform.position;
        return true;
    }

    /// <summary>
    /// The held visual's renderers, for the surface-based capture test — walked ONCE per hold and
    /// cached, because that test runs every frame for a free hand and
    /// <c>GetComponentsInChildren</c> allocates. Renderer WORLD bounds are read by the caller per
    /// frame, so the cached array stays correct as the item stretches.
    ///
    /// <para>DELIBERATELY NOT SHARED WITH <see cref="_watch"/>, the hold watch's array, even though
    /// both walk the same subtree: that one is armed only while a verdict budget remains and is
    /// nulled the moment it reports, so leaning on it would make the gesture's reach depend on how
    /// many diagnostics a session had already printed. Entries can go Unity-null mid-hold if the
    /// engine rebuilds the content (see <see cref="FreezeApparance"/> for when that happens); the
    /// consumer null-checks each.</para>
    /// </summary>
    internal Renderer[]? HeldRenderers()
    {
        if (!_attached || _visual == null)
            return null;
        return _stretchBoundsRenderers ??= _visual.GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>Diagnostic bookkeeping written by the capture test — the interaction volume the
    /// player is really reaching into. Nothing reads it to make a decision.</summary>
    internal void NoteCaptureVolume(float bodyRadiusRealMeters, float ceilingRealMeters)
    {
        _captureBodyRadiusReal = bodyRadiusRealMeters;
        _captureCeilingReal = ceilingRealMeters;
    }

    /// <summary>Radius of the visible body the capture test last trusted, real metres at the hand.
    /// NaN = not measured this hold; +Inf = every renderer was excluded (centre fallback).</summary>
    internal float CaptureBodyRadiusRealMeters => _captureBodyRadiusReal;

    /// <summary>The sanity ceiling the capture test last applied, real metres.</summary>
    internal float CaptureCeilingRealMeters => _captureCeilingReal;

    /// <summary>The prop currently ATTACHED to <paramref name="side"/>'s hand, or null. Walks
    /// <see cref="Live"/> (never more than two entries). Gliding props are deliberately absent — a
    /// released item cannot be stretched, which is the same rule <c>FigureGrabbable.HeldBy</c>
    /// states.</summary>
    internal static GrabbableProp? HeldBy(HandSide side)
    {
        for (int i = 0; i < Live.Count; i++)
        {
            GrabbableProp p = Live[i];
            if (p._attached && p._holder != null && p._holder.Side == side)
                return p;
        }
        return null;
    }

    // ---- layer parking ------------------------------------------------------------------------

    /// <summary>
    /// Move every collider host in the prop's subtree (plus the root, which is the object
    /// <c>UnityGameEditorObject.Start</c> puts on <c>"Hovering"</c>) to Ignore Raycast, recording
    /// what each one was.
    ///
    /// <para><b>CORRECTION TO THE ModBuild 338 CLAIM (this doc used to say "renderers are
    /// deliberately NOT touched", and that was not true).</b> The walk moves GAMEOBJECTS, and a
    /// prop's collider very often sits on the same GameObject as one of its renderers — the root
    /// always does, and it is in this list unconditionally. So renderer-bearing objects DO move to
    /// layer 2. That is safe, and it is safe for a reason this session's own log measured rather
    /// than assumed: the VR head camera's culling mask is <c>0xFFFFFFFF</c> (the ghost and glow
    /// census lines print it on every build), so layer 2 is rendered exactly like layer 0 or 8.
    /// It was never the cause of the invisible-in-hand defect either — that was
    /// <c>WallSegmentFade</c> (see the class doc), and a wall pass does not care what layer a
    /// renderer is on. The parking stays because it is what keeps the game's own <c>"Hovering"</c>
    /// pick ray off a prop held centimetres from the eye.</para>
    /// </summary>
    private void ParkLayers()
    {
        if (_layerHosts != null || _visual == null)
            return;

        var hosts = new List<GameObject>(4) { _visual };
        Collider[] colliders = _visual.GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider c = colliders[i];
            if (c == null)
                continue;
            GameObject go = c.gameObject;
            if (go == null || ReferenceEquals(go, _visual) || hosts.Contains(go))
                continue;
            hosts.Add(go);
        }

        _layerHosts = hosts.ToArray();
        _layerValues = new int[_layerHosts.Length];
        for (int i = 0; i < _layerHosts.Length; i++)
        {
            _layerValues[i] = _layerHosts[i].layer;
            _layerHosts[i].layer = IgnoreRaycastLayer;
        }
    }

    /// <summary>Give every parked object its authored layer back, object for object. Idempotent.</summary>
    private void RestoreLayers()
    {
        if (_layerHosts == null || _layerValues == null)
            return;
        for (int i = 0; i < _layerHosts.Length; i++)
        {
            GameObject go = _layerHosts[i];
            if (go != null)
                go.layer = _layerValues[i];
        }
        _layerHosts = null;
        _layerValues = null;
    }

    /// <summary>Re-arm the per-session log budgets (a new scenario is a new hardware question).</summary>
    internal static void ResetLogBudgets()
    {
        _highlightLogsLeft = HighlightKindBudget;
        HighlightKinds.Clear(); // the roster is per SCENARIO — a new one carries different props
        _grabLogsLeft = LogBudget;
        _probesLeft = ProbeBudget;
        _watchesLeft = WatchBudget;
        _loggedInfoWriteWar = false;
        _loggedFreeze = false;
        _loggedRichThrow = false;
        _cardRouteLogsLeft = HighlightKindBudget;
        CardRouteKinds.Clear();
        PropAnimWatch.Reset();
    }
}
