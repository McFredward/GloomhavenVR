using System.Collections.Generic;
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
///   <item>the HELD POSE — the FIGURE's whole pose pipeline, not just its numbers:
///   <c>CaptureUprightBase</c> at the grab, then
///   <c>_uprightBase * (HeldUpright ? HeldUprightRotation(side) : HeldPalmRotation())</c> as a
///   FIXED CONSTANT anchor-local rotation, the grab-time anchor-local SIZE LATCH, and a per-frame
///   idempotent re-assert. See <see cref="ApplyHeldPose"/>;</item>
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
///   figure's <c>_uprightBase</c>, so with [FigureGrab] HeldUprightAtGrab on a prop was the one
///   thing in the hand that did NOT start upright. Now the figure's exact composition.</item>
///   <item>(c) RETURN ANIMATION. The glide was already here in 338 and was correct; it was
///   invisible because (a) hid the thing gliding. Unchanged mechanism, now with the release log
///   line the figure path has.</item>
///   <item>(d) INFO. 338 showed nothing at all. See <see cref="ShowInfo"/> for what a prop can
///   show and why <c>StatPanelSurface</c> is the wrong window for it.</item>
/// </list>
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

    private static int _highlightLogsLeft = LogBudget;
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

    // --- the pickup info panel (defect (d)).
    private bool _infoShown;
    private string _infoTitle = string.Empty;
    private string? _infoDescription;
    private float _nextInfoCheck;
    private int _infoLosses;
    private static bool _loggedInfoWriteWar;

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
    internal string Label => $"'{(_prop != null ? _prop.PrefabName : "?")}' {(_prop != null ? _prop.ObjectType.ToString() : "?")}";

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

        int side = (int)hand.Side;
        float scale = Mathf.Max(hand.WorldScale, 1e-4f);
        float admit = FigureGrabConfig.PickRadiusRealMeters * scale
                      * (_inReach[side] ? FigureGrabDriver.PickExitFactor : 1f);
        Vector3 pinch = hand.Rig.GrabAnchor.TransformPoint(FigureGrabConfig.HeldOffsetFor(hand.Side));
        bool inside = Vector3.Distance(pinch, _collider.ClosestPoint(pinch)) <= admit;
        _inReach[side] = inside;
        return inside;
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
        if (_highlightLogsLeft <= 0)
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
        _uprightBase = CaptureUprightBase(anchor);
        _attached = true;

        ApplyHeldPose();
        Live.Add(this);
        ShowInfo();
        _probeFrame = Time.frameCount + 2; // arm the held-visibility probe (see TickProbe)

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
            + $"rides the hand) from pitch={FigureGrabConfig.ActiveHeldTilt:0.#}° "
            + $"yaw={FigureGrabConfig.HeldFaceYawFor(hand.Side):0.#}° "
            + $"roll={FigureGrabConfig.HeldRollFor(hand.Side):0.#}° "
            + $"upright={FigureGrabConfig.HeldUpright.Value} atGrab={FigureGrabConfig.HeldUprightAtGrab.Value}; "
            + $"prop axes in anchor space: up=({up.x:0.00},{up.y:0.00},{up.z:0.00}) "
            + $"fwd=({fwd.x:0.00},{fwd.y:0.00},{fwd.z:0.00}) — anchor +Y is the palm normal, so up.y=+1 is "
            + "'standing straight out of the palm', which for a prop means its own model +Y (the axis it "
            + $"stands on its hex by) points out of the palm. heldLocalScale={_heldLocalScale.x:0.######} "
            + $"latched against anchorScale={anchor.lossyScale.x:0.###}. "
            + $"({_grabLogsLeft} more prop grab lines this session.)");
    }

    private static string Fmt(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({e.x:0.0},{e.y:0.0},{e.z:0.0})";
    }

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
        if (!FigureGrabConfig.HeldUprightAtGrab.Value)
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
    /// (Re-)apply the held pose from <see cref="FigureGrabConfig"/> — offset, rotation and the
    /// latched scale — off the bases captured at the grab. IDEMPOTENT, which is what lets it be
    /// both the grab-time write and the per-frame re-assert.
    ///
    /// <para><b>THIS IS THE FIGURE'S <c>ApplyHeldPose</c>, line for line</b> (user, defect (b):
    /// "Es soll sich so verhalten wie die Figuren auch - nutze den selben Code hier"). ModBuild 338
    /// wrote the pinch offset and <c>HeldUprightRotation(side)</c> but dropped
    /// <see cref="_uprightBase"/>, so with [FigureGrab] HeldUprightAtGrab ON — which is what the
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

        // Pinch position: the grab-anchor-local offset toward the thumb-index fingertips, mirrored
        // across the hand frame's left-right axis for the left hand.
        t.localPosition = FigureGrabConfig.HeldOffsetFor(side);

        // A FIXED CONSTANT anchor-LOCAL rotation (grab-angle-independent) that rides the hand —
        // never a world rotation. See CaptureUprightBase for what "upright" means on a prop.
        t.localRotation = _uprightBase * (FigureGrabConfig.HeldUpright.Value
            ? FigureGrabConfig.HeldUprightRotation(side)
            : FigureGrabConfig.HeldPalmRotation());

        // SIZE — the value latched at the grab, re-asserted and never re-derived.
        t.localScale = _heldLocalScale;
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
                Live.RemoveAt(i); // prop destroyed mid-hold (looted, broken, teardown)
                continue;
            }
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
        ClearInfo();
        ClearHighlight();
        Transform t = _visual.transform;
        t.SetParent(_origParent, worldPositionStays: true); // keep the in-hand world pose
        _attached = false;
        _anchor = null;

        _glideFromPos = t.localPosition;
        _glideFromRot = t.localRotation;
        _glideFromScale = t.localScale;
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
        RestoreLayers();
        _holder = null;
        HeldProps.Remove(_prop);
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
    /// <para>Gated on <c>[FigureGrab] HeldFigureInfo</c>, the SAME dial the figure's pickup panel
    /// obeys ("mach diese aber auch optional in dem VR Einstellungen deaktivierbar") — one switch
    /// for "show me what I picked up", not one per kind of thing picked up.</para>
    ///
    /// <para>NO GAME STATE IS WRITTEN. <c>UITextInfoPanel.Show</c> sets label text and shows a
    /// UIWindow; it does not touch the prop, the tile, looting or the turn.</para>
    /// </summary>
    private void ShowInfo()
    {
        if (!FigureGrabConfig.HeldFigureInfoEnabled || _prop == null)
            return;
        BuildInfoText(out _infoTitle, out _infoDescription);
        if (_infoTitle.Length == 0)
            return;
        _infoShown = PushInfo();
        _infoLosses = 0;
        _nextInfoCheck = Time.unscaledTime + InfoReassertSeconds;
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
        float now = Time.unscaledTime;
        if (now < _nextInfoCheck)
            return;
        _nextInfoCheck = now + InfoReassertSeconds;

        if (!Singleton<UITextInfoPanel>.IsInitialized)
            return;
        UITextInfoPanel? panel = Singleton<UITextInfoPanel>.Instance;
        if (panel == null)
            return;
        UIWindow? window = panel.GetComponent<UIWindow>();
        if (window == null || window.IsVisible)
        {
            _infoLosses = 0; // still up (IsVisible is alpha>0, so a fade-IN counts as up)
            return;
        }

        _infoLosses++;
        if (_infoLosses <= InfoLossBudget)
        {
            PushInfo();
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
            + "HeldProps.Count > 0 (a held prop's card is not a stale hover). Logged once per session.");
    }

    /// <summary>Take the card down (idempotent). Called from both release paths, so a prop that
    /// leaves the hand never leaves its card behind.</summary>
    private void ClearInfo()
    {
        if (!_infoShown)
            return;
        _infoShown = false;
        _infoLosses = 0;
        if (!Singleton<UITextInfoPanel>.IsInitialized)
            return;
        UITextInfoPanel? panel = Singleton<UITextInfoPanel>.Instance;
        if (panel != null)
            panel.Hide();
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
        _highlightLogsLeft = LogBudget;
        _grabLogsLeft = LogBudget;
        _probesLeft = ProbeBudget;
        _loggedInfoWriteWar = false;
    }
}
