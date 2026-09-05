using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Central registry for poke/grab targets (FROZEN Phase-2 API).
///
/// Interactors iterate these lists every frame with plain for-loops (no physics
/// queries, no allocations) — registration is explicit rather than layer/collider
/// discovery so VR interactables never fight the game's own physics setup.
///
/// Register in OnEnable, unregister in OnDisable (see <see cref="PokeableBehaviour"/> /
/// <see cref="GrabbableBehaviour"/> for ready-made bases). Destroyed-but-not-
/// unregistered colliders are skipped and pruned lazily.
/// </summary>
internal static class VRInteractables
{
    internal readonly struct PokeableEntry
    {
        public readonly IPokeable Target;
        public readonly Collider Collider;

        public PokeableEntry(IPokeable target, Collider collider)
        {
            Target = target;
            Collider = collider;
        }
    }

    internal readonly struct GrabbableEntry
    {
        public readonly IGrabbable Target;
        public readonly Collider Collider;

        public GrabbableEntry(IGrabbable target, Collider collider)
        {
            Target = target;
            Collider = collider;
        }
    }

    // Interactors index these directly (hot path); mutation only via Register/Unregister.
    internal static readonly List<PokeableEntry> Pokeables = new(32);
    internal static readonly List<GrabbableEntry> Grabbables = new(32);

    public static void RegisterPokeable(IPokeable target, Collider collider)
    {
        if (target == null || collider == null)
        {
            VRLog.Warn("Interact", "RegisterPokeable called with null target/collider — ignored.");
            return;
        }
        UnregisterPokeable(target);
        Pokeables.Add(new PokeableEntry(target, collider));
    }

    public static void UnregisterPokeable(IPokeable target)
    {
        for (int i = Pokeables.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Pokeables[i].Target, target))
                Pokeables.RemoveAt(i);
        }
    }

    public static void RegisterGrabbable(IGrabbable target, Collider collider)
    {
        if (target == null || collider == null)
        {
            VRLog.Warn("Interact", "RegisterGrabbable called with null target/collider — ignored.");
            return;
        }
        UnregisterGrabbable(target);
        Grabbables.Add(new GrabbableEntry(target, collider));
    }

    public static void UnregisterGrabbable(IGrabbable target)
    {
        for (int i = Grabbables.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Grabbables[i].Target, target))
                Grabbables.RemoveAt(i);
        }
    }

    /// <summary>
    /// IS THIS COLLIDER A USABLE PICK SHAPE — i.e. may a reach test believe the number
    /// <c>Collider.ClosestPoint</c> hands back for it?
    ///
    /// <para><b>Why the question has to be asked at all.</b> Every proximity test in this mod is
    /// <c>Vector3.Distance(point, collider.ClosestPoint(point))</c>. That expression has three
    /// inputs whose failure looks identical to a perfect hit: Unity's <c>ClosestPoint</c> returns
    /// THE QUERY POINT ITSELF — i.e. a distance of exactly zero, at every point in the world — for
    /// a collider that is disabled, for one whose GameObject is inactive, and for a non-convex
    /// <c>MeshCollider</c> (which additionally logs an error). A caller that does not ask this
    /// question therefore reads such a collider as touching the hand no matter where the hand is,
    /// and every rule downstream of that distance silently inverts.</para>
    ///
    /// <para><b>This is not hypothetical, it is the 2026-09-05 defect.</b> Two hardware logs, one
    /// per machine, carried hundreds of lines reading <c>'GoldPile' MoneyToken at 0 mm vs shell
    /// 77…72…67…61…58…53…48…44…40…36…32…28…23…19…15…11 mm</c> — one surface distance pinned at
    /// zero while the hand demonstrably travelled 66 mm past a second surface measured on the same
    /// line. An enemy-drop gold pile registers with a collider that is present but switched off,
    /// so <c>ClosestPoint</c> was degenerate for it from the moment it was registered. It cost
    /// two user-visible features at once: the pile could never be highlighted or picked up
    /// (<see cref="ProximityGrabber.UpdateHighlight"/> skipped it, correctly, on exactly this
    /// test), and the figure-resize gesture was disabled board-wide, because its "a prop under the
    /// hand beats the resize shell" veto consumed that zero and fired everywhere forever.</para>
    ///
    /// <para><b>So the predicate lives here, once.</b> <see cref="ProximityGrabber"/> already knew
    /// all of this and asked it inline in three places; <c>GrabbableProp</c> and <c>PropGrab</c>
    /// did not, and that DISAGREEMENT between two halves of the same election was the defect. One
    /// name, asked at registration and at every read, is what keeps them from drifting apart
    /// again.</para>
    ///
    /// <para>Pure, allocation-free, no scene query — safe on the per-frame election path.</para>
    /// </summary>
    internal static bool IsUsablePickShape(Collider? collider)
    {
        if (collider == null)
            return false;
        if (!collider.enabled || !collider.gameObject.activeInHierarchy)
            return false;
        // A non-convex MeshCollider has no ClosestPoint: Unity logs "Cannot cast a ray/query
        // against a non-convex MeshCollider" and returns the query point. Game content may be any
        // collider type, so the shape is asked as well as the switch.
        if (collider is MeshCollider mesh && !mesh.convex)
            return false;
        return true;
    }

    /// <summary>
    /// One clause naming WHY <see cref="IsUsablePickShape"/> said no — for log lines, so a refused
    /// candidate reports its own cause instead of being argued about. Never branched on.
    /// </summary>
    internal static string DescribePickShape(Collider? collider)
    {
        if (collider == null)
            return "its collider was destroyed";
        if (!collider.enabled)
            return "its collider is switched off (Collider.enabled=false), so ClosestPoint would "
                   + "hand back the query point and read as 0 mm everywhere";
        if (!collider.gameObject.activeInHierarchy)
            return "its object is deactivated (re-parked to a pool, or hidden), so ClosestPoint "
                   + "would hand back the query point and read as 0 mm everywhere";
        if (collider is MeshCollider mesh && !mesh.convex)
            return "its collider is a non-convex MeshCollider, which has no ClosestPoint at all — "
                   + "Unity logs an error and hands back the query point";
        return "it is a usable pick shape";
    }

    /// <summary>Drop entries whose colliders were destroyed (called opportunistically by interactors).</summary>
    internal static void Prune()
    {
        for (int i = Pokeables.Count - 1; i >= 0; i--)
        {
            if (Pokeables[i].Collider == null)
                Pokeables.RemoveAt(i);
        }
        for (int i = Grabbables.Count - 1; i >= 0; i--)
        {
            if (Grabbables[i].Collider == null)
                Grabbables.RemoveAt(i);
        }
    }

    internal static void Clear()
    {
        Pokeables.Clear();
        Grabbables.Clear();
    }
}

/// <summary>
/// Optional companion to <see cref="IGrabbable"/> (additive, ModBuild 445): a target that can
/// EXPLAIN its own <c>CanGrab=false</c>.
///
/// <para><b>Why it exists.</b> <c>ProximityGrabber.LogNoCandidateRefusal</c> printed
/// <c>nearest in-reach grabbable 'GrabbableProp' has CanGrab=false</c> — true, throttled,
/// tier-correct, and it answered nothing: <c>CanGrab</c> is a conjunction of four clauses and the
/// line named none of them. Deciding which one had fired cost a round of arguing from adjacent
/// counters. A target that implements this hands the grabber the clause instead.</para>
///
/// <para>Log only. Nothing branches on the string, and a target that does not implement this
/// interface keeps exactly the message it had.</para>
/// </summary>
internal interface IGrabRefusalNarrator
{
    /// <summary>Which clause of this target's <c>CanGrab</c> is false right now, as one short
    /// phrase, or null when it is in fact true (a race between the grabber's test and this call).
    /// Must be pure and allocation-light: it is called from a throttled log path only.</summary>
    string? DescribeGrabRefusal();
}

/// <summary>
/// Convenience base: implement <see cref="IPokeable"/> callbacks, get registration
/// (with this GameObject's Collider) for free. FROZEN Phase-2 API.
/// </summary>
internal abstract class PokeableBehaviour : MonoBehaviour, IPokeable
{
    protected virtual void OnEnable()
    {
        Collider? collider = GetComponent<Collider>();
        if (collider == null)
        {
            VRLog.Warn("Interact", $"{name}: PokeableBehaviour requires a Collider on the same GameObject.");
            return;
        }
        VRInteractables.RegisterPokeable(this, collider);
    }

    protected virtual void OnDisable() => VRInteractables.UnregisterPokeable(this);

    public virtual void OnPokeEnter(VRHand hand) { }
    public virtual void OnPokeExit(VRHand hand) { }
    public abstract void OnPoke(VRHand hand);
}

/// <summary>
/// The pose a <see cref="GrabbableBehaviour"/> takes relative to the hand's GrabAnchor
/// while held (P5, MISSION A.6). <see cref="Default"/> reproduces the Phase-2 snap
/// (anchor origin, identity rotation, scale untouched).
/// </summary>
internal readonly struct HeldPose
{
    /// <summary>Local position relative to the GrabAnchor.</summary>
    public readonly Vector3 LocalPosition;

    /// <summary>Local rotation relative to the GrabAnchor.</summary>
    public readonly Quaternion LocalRotation;

    /// <summary>Uniform local scale while held; null = leave the current scale untouched.</summary>
    public readonly float? LocalScale;

    public HeldPose(Vector3 localPosition, Quaternion localRotation, float? localScale = null)
    {
        LocalPosition = localPosition;
        LocalRotation = localRotation;
        LocalScale = localScale;
    }

    /// <summary>Anchor origin, identity rotation, scale untouched — the Phase-2 behavior.</summary>
    public static HeldPose Default { get; } = new(Vector3.zero, Quaternion.identity);
}

/// <summary>
/// Convenience base: implement <see cref="IGrabbable"/>, get registration and
/// (optional) snap-to-hand parenting for free. FROZEN Phase-2 API (P5 addition:
/// <see cref="GetHeldPose"/>). Set <see cref="snapToHand"/> = true to have the object
/// parented to the hand's GrabAnchor while held and restored on release.
/// </summary>
internal abstract class GrabbableBehaviour : MonoBehaviour, IGrabbable
{
    /// <summary>Parent this object to the hand's GrabAnchor while held.</summary>
    protected bool snapToHand = true;

    private Transform? _originalParent;
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private Vector3 _originalLocalScale;
    private bool _scaleOverridden;
    private bool _attached;

    /// <summary>The hand currently holding this object, if any.</summary>
    public VRHand? Holder { get; private set; }

    public virtual bool CanGrab => !_attached;

    /// <summary>
    /// False = not a grip-only grabbable — see <see cref="IGrabbable.GrabWithGrip"/>. For the
    /// card types this base serves that means TRIGGER-ONLY acquisition
    /// (<c>ProximityGrabber.IsTriggerOnly</c>, user 2026-08-11: "Die Karten sollen nur mit dem
    /// trigger nehmbar sein"). Grip-only grabbables (world panels/boards) override this to
    /// true instead.
    /// </summary>
    public virtual bool GrabWithGrip => false;

    protected virtual void OnEnable()
    {
        Collider? collider = GetComponent<Collider>();
        if (collider == null)
        {
            VRLog.Warn("Interact", $"{name}: GrabbableBehaviour requires a Collider on the same GameObject.");
            return;
        }
        VRInteractables.RegisterGrabbable(this, collider);
    }

    protected virtual void OnDisable()
    {
        VRInteractables.UnregisterGrabbable(this);
        if (_attached)
            DetachFromHand();
        // GRAB STATE hygiene (user bug A): a grabbable disabled/re-parked WHILE held used
        // to keep Holder set — IsHeld read true on a pooled card forever. The holding
        // hand's ProximityGrabber heals its own Held reference (HealDeadHeld) and calls
        // OnRelease, which tolerates an already-cleared Holder.
        Holder = null;
    }

    public virtual void OnGrab(VRHand hand)
    {
        Holder = hand;
        if (snapToHand)
            AttachToHand(hand);
    }

    public virtual void OnRelease(VRHand hand, Vector3 velocity)
    {
        if (_attached)
            DetachFromHand();
        Holder = null;
    }

    /// <summary>
    /// P5 (MISSION A.6): the pose this object takes relative to the GrabAnchor while
    /// held. Override instead of re-writing the transform after <c>base.OnGrab</c> —
    /// the base snap applies exactly this pose, so derived classes never fight it.
    /// Called once at grab time (per grab).
    /// </summary>
    protected virtual HeldPose GetHeldPose(VRHand hand) => HeldPose.Default;

    /// <summary>Snap to the hand's grab anchor (stores the original parent/pose).</summary>
    protected void AttachToHand(VRHand hand)
    {
        if (_attached)
            return;
        _originalParent = transform.parent;
        _originalLocalPos = transform.localPosition;
        _originalLocalRot = transform.localRotation;
        _originalLocalScale = transform.localScale;

        HeldPose pose = GetHeldPose(hand);
        transform.SetParent(hand.Rig.GrabAnchor, worldPositionStays: false);
        transform.localPosition = pose.LocalPosition;
        transform.localRotation = pose.LocalRotation;
        _scaleOverridden = pose.LocalScale.HasValue;
        if (pose.LocalScale.HasValue)
            transform.localScale = Vector3.one * pose.LocalScale.Value;
        _attached = true;
    }

    /// <summary>Restore the pre-grab parent and local pose (and scale, if the held pose changed it).</summary>
    protected void DetachFromHand()
    {
        if (!_attached)
            return;
        // NEVER detach INTO a dead or inactive hierarchy (fan-card hand-transfer bug
        // 2026-08-04, hardware log 5216-5297). ROOT CAUSE of "the card disappears": a fan
        // card's pre-grab parent is the CardFan root, and that root DEACTIVATES when the
        // palm gate closes — which is exactly the wrist roll of reaching over to receive
        // the card. Re-parenting the released card under that inactive root here fired the
        // card's OnDisable IN THE MIDDLE of the release call stack, wiping its interaction
        // state (VRCard.OnDisable parks AllowsGateHand back to false) BEFORE the Released
        // subscribers ran — so the hand-to-hand adoption's ForceGrab was refused by
        // AllowsHand and the card fell into the hidden fan. A released object must stay
        // ALIVE through its own Released routing: fall back to the scene root instead; the
        // routing that follows (fan re-add, tray dock, pool park, adopting re-grab) always
        // re-parents it to its next owner anyway. The local-pose restore below is then
        // world-space and transient — VRCard.OnRelease immediately re-asserts the true
        // world pose on top. A DESTROYED original parent (browse arc torn down mid-hold)
        // would even have thrown in SetParent — same guard covers it.
        Transform? restoreParent = _originalParent;
        if (restoreParent == null || !restoreParent.gameObject.activeInHierarchy)
            restoreParent = null;
        transform.SetParent(restoreParent, worldPositionStays: false);
        transform.localPosition = _originalLocalPos;
        transform.localRotation = _originalLocalRot;
        if (_scaleOverridden)
        {
            transform.localScale = _originalLocalScale;
            _scaleOverridden = false;
        }
        _attached = false;
    }
}
