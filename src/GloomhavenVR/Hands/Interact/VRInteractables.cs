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
/// Convenience base: implement <see cref="IGrabbable"/>, get registration and
/// (optional) snap-to-hand parenting for free. FROZEN Phase-2 API.
/// Set <see cref="snapToHand"/> = true to have the object parented to the hand's
/// GrabAnchor while held and restored on release.
/// </summary>
internal abstract class GrabbableBehaviour : MonoBehaviour, IGrabbable
{
    /// <summary>Parent this object to the hand's GrabAnchor while held.</summary>
    protected bool snapToHand = true;

    private Transform? _originalParent;
    private Vector3 _originalLocalPos;
    private Quaternion _originalLocalRot;
    private bool _attached;

    /// <summary>The hand currently holding this object, if any.</summary>
    public VRHand? Holder { get; private set; }

    public virtual bool CanGrab => !_attached;

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

    /// <summary>Snap to the hand's grab anchor (stores the original parent/pose).</summary>
    protected void AttachToHand(VRHand hand)
    {
        if (_attached)
            return;
        _originalParent = transform.parent;
        _originalLocalPos = transform.localPosition;
        _originalLocalRot = transform.localRotation;
        transform.SetParent(hand.Rig.GrabAnchor, worldPositionStays: false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        _attached = true;
    }

    /// <summary>Restore the pre-grab parent and local pose.</summary>
    protected void DetachFromHand()
    {
        if (!_attached)
            return;
        transform.SetParent(_originalParent, worldPositionStays: false);
        transform.localPosition = _originalLocalPos;
        transform.localRotation = _originalLocalRot;
        _attached = false;
    }
}
