using System.Collections.Generic;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using ScenarioRuleLibrary;
using UnityEngine;

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
///   <item>the HELD POSE — <c>FigureGrabConfig.HeldOffsetFor</c> /
///   <c>HeldUprightRotation</c> / <c>HeldPalmRotation</c>, so a chest sits in the palm where a
///   mini sits and follows the same tuning;</item>
///   <item>the RELEASE GLIDE — the same 0.28 s cubic ease-out, so putting a chest down looks like
///   putting a mini down.</item>
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

        HeldProps.Add(_prop, hand.Side);
        ClearHighlight();     // belt: the grabber clears the hover before OnGrab, but a laser pluck
                              // and a re-grab mid-glide are both paths that need not have done so
        ParkLayers();

        // Ride the hand's grab anchor. worldPositionStays keeps the prop at its board world size as
        // it enters the hand (no scale pop) and leaves the resulting anchor-LOCAL scale in place —
        // which is the same size latch a figure gets: constant relative to the hand, so a zoom
        // mid-hold no longer resizes what is in the palm.
        t.SetParent(hand.Rig.GrabAnchor, worldPositionStays: true);
        t.localPosition = FigureGrabConfig.HeldOffsetFor(hand.Side);
        t.localRotation = FigureGrabConfig.HeldUpright.Value
            ? FigureGrabConfig.HeldUprightRotation(hand.Side)
            : FigureGrabConfig.HeldPalmRotation();
        _attached = true;

        if (_grabLogsLeft <= 0)
            return;
        _grabLogsLeft--;
        // HW-VERIFY: a standing hardware question is waiting on this line — it must stay at a tier
        // the DEFAULT log level prints (Note/Alert/Error). scripts/check-hw-verify.py enforces it.
        VRLog.Note("FigureGrab",
            $"[Props] {hand.Side} grabbed {Label} — the prop VISUAL rides the hand (resolved through "
            + "ObjectCacheService, not through an actor: it has none). Home pose captured and a ghost "
            + $"left at the cell; colliders parked on Ignore Raycast for the hold. "
            + $"({_grabLogsLeft} more prop grab lines this session.)");
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
            return;
        Restore();
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

        ClearHighlight();
        Transform t = _visual.transform;
        t.SetParent(_origParent, worldPositionStays: true); // keep the in-hand world pose
        _attached = false;

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
        RestoreLayers();
        _holder = null;
        HeldProps.Remove(_prop);
    }

    // ---- layer parking ------------------------------------------------------------------------

    /// <summary>
    /// Move every collider host in the prop's subtree (plus the root, which is the object
    /// <c>UnityGameEditorObject.Start</c> puts on <c>"Hovering"</c>) to Ignore Raycast, recording
    /// what each one was. Renderers are deliberately NOT touched — see the class doc.
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
    }
}
