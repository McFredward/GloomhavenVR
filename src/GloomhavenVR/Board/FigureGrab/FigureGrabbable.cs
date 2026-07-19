using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI.Surfaces;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Board.FigureGrab;

/// <summary>
/// A grabbable board figure (hero OR monster) — grip-grab it into the hand to inspect it
/// (P8, immersion only, no gameplay effect). Implemented directly on
/// <see cref="IGrabbable"/> (NOT via <see cref="GrabbableBehaviour"/>) because the object
/// being moved is a live GAME object, not a mod-owned MonoBehaviour: we reparent the real
/// <c>m_RootGameObject</c> and suppress the game's own transform writes for the held actor
/// via <see cref="HeldFigures"/> / <see cref="ActorBehaviour_HeldTransform_Patch"/> — the
/// user's confirmed "move the real figure" choice (Approach A).
///
/// Grab paths (both GRIP, since <see cref="GrabWithGrip"/> is true and the world-grab
/// rebind freed the grip): near reach-and-close is handled automatically by
/// <see cref="ProximityGrabber"/>; the far laser point-and-grab is driven by
/// <see cref="FigureGrabDriver"/> via <c>hand.Grabber.ForceGrab</c> (the same pluck the
/// card fan uses). Release (grip-up) restores the real transform; the game snaps the mini
/// back to its cell on the next frame.
/// </summary>
internal sealed class FigureGrabbable : IGrabbable, IGrabHighlight
{
    private readonly ActorBehaviour _actor;

    private VRHand? _holder;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private bool _attached;

    internal FigureGrabbable(ActorBehaviour actor) => _actor = actor;

    internal ActorBehaviour Actor => _actor;

    internal bool IsHeld => _holder != null;

    private CActor? Character => _actor != null ? _actor.Actor : null;

    private GameObject? Root => _actor != null ? _actor.m_RootGameObject : null;

    public bool CanGrab
    {
        get
        {
            if (!FigureGrabConfig.GrabFigures.Value || _holder != null)
                return false;
            if (_actor == null || Root == null)
                return false;
            CActor? actor = Character;
            return actor != null && !actor.IsDead;
        }
    }

    /// <summary>Figures always take the GRIP button (Demeo parity), freed by the world-grab stick rebind.</summary>
    public bool GrabWithGrip => true;

    public void OnGrabHighlight(VRHand hand, bool highlighted)
    {
        // Reuse the game's own actor highlight ring — no new outline plumbing.
        GameObject? root = Root;
        if (root != null)
            ActorBehaviour.SetHilighted(root, highlighted);
    }

    public void OnGrab(VRHand hand)
    {
        GameObject? root = Root;
        if (_actor == null || root == null)
            return;

        _holder = hand;
        Transform t = root.transform;
        _origParent = t.parent;
        _origLocalPos = t.localPosition;
        _origLocalRot = t.localRotation;
        _origLocalScale = t.localScale;

        // Suppress the game's per-frame transform writes for THIS actor only.
        HeldFigures.Add(_actor);

        // Ride the hand's grab anchor. worldPositionStays keeps the mini at its board
        // world-scale as it enters the hand (no pop); HeldScale then zooms it for
        // inspection. Position/rotation come from the tunable held pose.
        t.SetParent(hand.Rig.GrabAnchor, worldPositionStays: true);
        t.localPosition = FigureGrabConfig.HeldOffset;
        t.localRotation = Quaternion.Euler(FigureGrabConfig.HeldEuler);
        t.localScale *= FigureGrabConfig.HeldScale.Value;
        _attached = true;

        // Dock the SAME stat window shown on laser mouse-over next to the held figure.
        GameObject anchorGo = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        StatPanelSurface.ShowHeldFigure(anchorGo.transform, Character);

        VRLog.Info("FigureGrab", $"{hand.Side} grabbed figure ({Describe()}).");
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        Restore();
        VRLog.Info("FigureGrab", $"{hand.Side} released figure ({Describe()}).");
    }

    /// <summary>Restore the real transform and resume the game's transform writes (idempotent).</summary>
    internal void Restore()
    {
        if (_attached)
        {
            GameObject? root = Root;
            if (root != null)
            {
                Transform t = root.transform;
                t.SetParent(_origParent, worldPositionStays: false);
                t.localPosition = _origLocalPos;
                t.localRotation = _origLocalRot;
                t.localScale = _origLocalScale;
            }
            _attached = false;
        }

        // Resume the game's transform writes → next Update snaps the mini back to its cell.
        if (_actor != null)
            HeldFigures.Remove(_actor);

        if (_holder != null)
        {
            StatPanelSurface.ClearHeldFigure(Character);
            _holder = null;
        }
    }

    private string Describe()
    {
        CActor? actor = Character;
        return actor != null && actor.Class != null ? actor.Class.ID : "?";
    }
}
