using System.Collections.Generic;
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
/// Grab paths (both the TRIGGER, since <see cref="GrabWithGrip"/> is false — the user's
/// hardware pass moved figures onto the trigger, exactly like the hand-card fan, and the
/// grip is now free): near reach-and-close is handled automatically by
/// <see cref="ProximityGrabber"/> (its trigger path, arbitrated vs a UI/board click via
/// <c>Ray.HasFreshUiHit</c>); the far laser point-and-grab is driven by
/// <see cref="FigureGrabDriver"/> via <c>hand.Grabber.ForceGrab</c> (the same trigger
/// pluck the card fan uses). Release (trigger-up) restores the real transform; the game
/// snaps the mini back to its cell on the next frame.
///
/// The held pose (offset / rotation / scale) is LIVE-TUNABLE: every currently-held
/// grabbable registers in <see cref="Live"/> and re-applies its pose from
/// <see cref="FigureGrabConfig"/> whenever a tunable changes (<see cref="ReapplyAll"/>,
/// wired to each entry's SettingChanged in <see cref="FigureGrabConfig.Bind"/>), so the
/// in-headset debug-menu steppers nudge the mini in your hand in real time.
/// </summary>
internal sealed class FigureGrabbable : IGrabbable, IGrabHighlight
{
    /// <summary>Every grabbable currently held in a hand — the live-tune broadcast target.</summary>
    private static readonly HashSet<FigureGrabbable> Live = new();

    private readonly ActorBehaviour _actor;

    private VRHand? _holder;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private bool _attached;

    // Live-pose bases captured at grab (so re-applying the config pose never compounds):
    // the mini's anchor-local scale at board size, its upright board world-rotation, and
    // the hand anchor it rides.
    private Transform? _anchor;
    private Vector3 _heldBaseScale = Vector3.one;
    private Quaternion _heldBoardRot = Quaternion.identity;

    /// <summary>
    /// Re-apply the held pose from <see cref="FigureGrabConfig"/> to every held mini — the
    /// live-tune hook (wired to the config entries' SettingChanged). Called on the main
    /// thread from a stepper write, so it may touch transforms.
    /// </summary>
    internal static void ReapplyAll()
    {
        foreach (FigureGrabbable g in Live)
            g.ApplyHeldPose();
    }

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

    /// <summary>
    /// False → figures obey the shared card grab button (the TRIGGER by default, per the
    /// user's hardware pass), so the <see cref="ProximityGrabber"/> near-grab and the laser
    /// pluck use the SAME trigger + <c>Ray.HasFreshUiHit</c> arbitration as the hand cards.
    /// The grip is now free.
    /// </summary>
    public bool GrabWithGrip => false;

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
        // world-scale AND its upright board rotation as it enters the hand (no pop). Snapshot
        // those two as the LIVE-TUNE bases: HeldScale zooms on top of the board scale, and the
        // upright pose faces the player from the board rotation — re-derived (never compounded)
        // every time a tunable changes.
        Transform anchor = hand.Rig.GrabAnchor;
        t.SetParent(anchor, worldPositionStays: true);
        _anchor = anchor;
        _heldBaseScale = t.localScale;
        _heldBoardRot = t.rotation;
        _attached = true;

        ApplyHeldPose();
        Live.Add(this);

        // Dock the SAME stat window shown on laser mouse-over next to the held figure.
        GameObject anchorGo = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        StatPanelSurface.ShowHeldFigure(anchorGo.transform, Character);

        VRLog.Info("FigureGrab", $"{hand.Side} grabbed figure ({Describe()}).");
    }

    /// <summary>
    /// (Re-)apply the held pose from <see cref="FigureGrabConfig"/> — offset, rotation and
    /// scale — off the bases captured at grab. Idempotent, so it doubles as the live-tune
    /// path: a debug-menu stepper writes a config entry and this re-poses the mini in-hand.
    /// </summary>
    private void ApplyHeldPose()
    {
        GameObject? root = Root;
        if (!_attached || root == null || _anchor == null)
            return;
        Transform t = root.transform;

        // Pinch position: a small grab-anchor-local offset toward the thumb–index fingertips.
        t.localPosition = FigureGrabConfig.HeldOffset;

        if (FigureGrabConfig.HeldUpright.Value)
            ApplyUprightPose(t, _anchor, _heldBoardRot);
        else
            t.localRotation = Quaternion.Euler(FigureGrabConfig.HeldEuler); // legacy flat-on-palm

        t.localScale = _heldBaseScale * FigureGrabConfig.HeldScale.Value;
    }

    /// <summary>
    /// Stand the mini UPRIGHT in WORLD space (feet→head along world up, exactly as it stands
    /// on the board) and yaw it to face the player — instead of inheriting the hand's palm tilt
    /// that lays it flat. Because <c>SetParent(worldPositionStays:true)</c> preserved the board
    /// rotation, <paramref name="t"/>.rotation is already the upright board pose; we only rotate
    /// it about WORLD UP so the up-alignment is untouched (no model-axis assumption), then bake
    /// it into localRotation. Baked once, so the mini still tracks natural wrist rotation while
    /// inspecting (like turning a chess piece in your fingers).
    /// </summary>
    private static void ApplyUprightPose(Transform t, Transform anchor, Quaternion boardRot)
    {
        // boardRot: the upright pose the mini stands in on its cell, captured at grab so live
        // re-tuning re-derives the facing from the true board rotation (never from an already
        // posed transform, which would drift).

        // Mini's current front in the horizontal plane (assume local +Z = front; a tunable yaw
        // corrects models whose readable side differs — see HeldFaceYawDegrees).
        Vector3 curFront = boardRot * Vector3.forward;
        curFront.y = 0f;
        if (curFront.sqrMagnitude < 1e-6f)
        {
            curFront = anchor.forward;
            curFront.y = 0f;
            if (curFront.sqrMagnitude < 1e-6f)
                curFront = Vector3.forward;
        }
        curFront.Normalize();

        // Direction from the pinch point to the player's head (horizontal) → where the front
        // should point so the card face is readable.
        Camera? head = GloomhavenVR.Rig.VRRigDriver.HeadCamera;
        Vector3 toHead = head != null ? head.transform.position - t.position : curFront;
        toHead.y = 0f;
        if (toHead.sqrMagnitude < 1e-6f)
            toHead = curFront;
        toHead.Normalize();

        // Pure world-up yaw (both vectors horizontal) — preserves the upright up-alignment.
        Quaternion faceYaw = Quaternion.FromToRotation(curFront, toHead);
        Quaternion worldRot = faceYaw * boardRot;

        // User inspection adjustments, in the mini's own frame: tilt tips it toward the face,
        // yaw spins the readable front toward the player.
        worldRot *= Quaternion.Euler(
            FigureGrabConfig.HeldTiltDegrees.Value,
            FigureGrabConfig.HeldFaceYawDegrees.Value,
            0f);

        t.rotation = worldRot; // baked into localRotation (child of the moving anchor)
    }

    public void OnRelease(VRHand hand, Vector3 velocity)
    {
        Restore();
        VRLog.Info("FigureGrab", $"{hand.Side} released figure ({Describe()}).");
    }

    /// <summary>Restore the real transform and resume the game's transform writes (idempotent).</summary>
    internal void Restore()
    {
        Live.Remove(this);
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
            _anchor = null;
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
