using System.Collections.Generic;
using AStar;
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
internal sealed class FigureGrabbable : IGrabbable, IGrabHighlight, IGrabbableHandFilter
{
    /// <summary>Every grabbable currently held in a hand — the live-tune broadcast target.</summary>
    private static readonly HashSet<FigureGrabbable> Live = new();

    private readonly ActorBehaviour _actor;

    private VRHand? _holder;

    // Item 3 — offset-anchor nearest selection. When the hand hovers over MULTIPLE figures in
    // proximity reach, only the one nearest the OFFSET ANCHOR (where the held mini will appear)
    // should be grabbable; the losers are suppressed for THAT hand so the ProximityGrabber (which
    // otherwise picks nearest-to-palm) can only highlight/grab the offset-anchor winner. Set every
    // frame per hand by FigureGrabDriver.SelectByOffsetAnchor; consumed by AllowsHand below.
    private bool _suppressLeft;
    private bool _suppressRight;
    private Transform? _origParent;
    private Vector3 _origLocalPos;
    private Quaternion _origLocalRot;
    private Vector3 _origLocalScale;
    private bool _attached;

    // Live-pose bases captured at grab (so re-applying the config pose never compounds):
    // the mini's anchor-local scale at board size, and the hand anchor it rides. NOTE: the
    // held ROTATION is a FIXED CONSTANT local rotation relative to the anchor
    // (FigureGrabConfig.HeldUprightRotation) — NOT derived from world up, the head, the figure's
    // board rotation, or the grab-moment anchor orientation. So the mini sits the SAME way in the
    // palm regardless of the grab approach angle AND rides the hand — turn the hand and the mini
    // turns with it (user #3: "fixed relative to the hand, not the world"), while HOW it was
    // grabbed never changes the resting hold and it never clips into a downward-pointing palm.
    private Transform? _anchor;
    private Vector3 _heldBaseScale = Vector3.one;

    // Issue B — render-on-top state so a mini held in FRONT of the opaque control board (PlayTray)
    // is not painted over by the board's on-top HUD widgets (queue 4000, ZTest Always, ZWrite off).
    // On grab we snapshot each held renderer's ORIGINAL shared materials and swap in per-renderer
    // INSTANCE materials whose renderQueue is pushed just past those widgets; the shader's own ZTest
    // (LEqual) + ZWrite are left intact so the 3D mini still self-occludes correctly and is hidden
    // naturally when moved BEHIND real world geometry. Restored verbatim on release. ONLY the held
    // mini is affected — never the rest of the board's figures.
    private const int HeldRenderQueue = 4100;
    private Renderer[]? _heldRenderers;
    private Material[][]? _origSharedMats;

    // R2 hardening: the actor's authoritative board cell at grab time. If the game moves the
    // figure to a different cell while it is held (a remote player's or the server's networked
    // action on its turn), the held mini would otherwise ride the hand at a now-stale board
    // position and jump on release; we auto-release instead (polled by FigureGrabDriver).
    private Point _grabCell;

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

    private static string Fmt(Quaternion q)
    {
        Vector3 e = q.eulerAngles;
        return $"({e.x:0.#},{e.y:0.#},{e.z:0.#})";
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

    /// <summary>
    /// Item 3: per-hand gate (<see cref="IGrabbableHandFilter"/>). Returns false while this figure
    /// is a proximity-grab LOSER for <paramref name="hand"/> — i.e. another figure sits nearer the
    /// hand's offset anchor (the point where the held mini appears). Set each frame by
    /// <see cref="FigureGrabDriver"/>; the <see cref="ProximityGrabber"/> then skips the losers,
    /// leaving only the offset-anchor-nearest figure grabbable. Uncontested figures (single figure,
    /// or a far laser target out of proximity reach) are never suppressed, so far-grab is untouched.
    /// </summary>
    public bool AllowsHand(VRHand hand)
        => !(hand.Side == HandSide.Left ? _suppressLeft : _suppressRight);

    /// <summary>Driver hook: mark this figure suppressed (proximity loser) for a hand, or clear it.</summary>
    internal void SetProximitySuppressed(HandSide side, bool suppressed)
    {
        if (side == HandSide.Left)
            _suppressLeft = suppressed;
        else
            _suppressRight = suppressed;
    }

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

        // Snapshot the authoritative cell so we can auto-release if the game moves the figure
        // on the board while it is held (R2 hardening).
        CActor? ca = Character;
        _grabCell = ca != null ? ca.ArrayIndex : default;

        // Ride the hand's grab anchor. worldPositionStays keeps the mini at its board
        // world-scale as it enters the hand (no scale pop). Snapshot that scale as the LIVE-TUNE
        // base: HeldScale zooms on top of the board scale, re-derived (never compounded) every
        // time a tunable changes. The held ROTATION is NOT snapshotted from the board — it is a
        // fixed constant anchor-local rotation (FigureGrabConfig.HeldUprightRotation), so the mini
        // snaps to the same orientation in the palm regardless of the grab approach angle.
        Transform anchor = hand.Rig.GrabAnchor;
        t.SetParent(anchor, worldPositionStays: true);
        _anchor = anchor;
        _heldBaseScale = t.localScale;
        _attached = true;

        ApplyHeldPose();
        ApplyRenderOnTop(); // Issue B — draw the held mini over the opaque control board
        Live.Add(this);

        // Dock the SAME stat window shown on laser mouse-over next to the held figure.
        GameObject anchorGo = _actor.m_AnimatedGameObject != null ? _actor.m_AnimatedGameObject : root;
        StatPanelSurface.ShowHeldFigure(anchorGo.transform, Character, hand.Side);

        // Issue A — one-shot grab diagnostic: the FIXED anchor-LOCAL rotation chosen for the hold
        // (grab-angle-independent; rides the hand). World rotation shown for reference only.
        VRLog.Info("FigureGrab",
            $"{hand.Side} grabbed figure ({Describe()}); localRot={Fmt(t.localRotation)} " +
            $"(fixed constant relative to the hand anchor; grab-angle-independent, rides the hand) " +
            $"hand={Fmt(anchor.rotation)} worldHeld={Fmt(t.rotation)}.");
    }

    /// <summary>
    /// (Re-)apply the held pose from <see cref="FigureGrabConfig"/> — offset, rotation and
    /// scale — off the bases captured at grab. Idempotent, so it doubles as the live-tune
    /// path: a debug-menu stepper writes a config entry and this re-poses the mini in-hand.
    /// </summary>
    private void ApplyHeldPose()
    {
        GameObject? root = Root;
        if (!_attached || root == null || _anchor == null || _holder == null)
            return;
        Transform t = root.transform;

        // Item 2: the tuned offsets/rotation are canonical for the RIGHT hand; the LEFT hand gets
        // the MIRROR IMAGE (lateral offset + yaw/roll flip sign; forward/up/tilt unchanged) so the
        // user only tunes once and the mini sits in the left hand exactly mirrored.
        HandSide side = _holder.Side;

        // Pinch position: a small grab-anchor-local offset toward the thumb–index fingertips
        // (mirrored across the hand's left-right axis for the left hand).
        t.localPosition = FigureGrabConfig.HeldOffsetFor(side);

        // Issue A: a FIXED CONSTANT anchor-LOCAL rotation (grab-angle-independent) that rides the
        // hand — never a world rotation. Upright mode stands the mini out of the palm and faces it
        // (mirror-correct); legacy mode lays it flat (tilt only, mirror-invariant).
        t.localRotation = FigureGrabConfig.HeldUpright.Value
            ? FigureGrabConfig.HeldUprightRotation(side)
            : Quaternion.Euler(FigureGrabConfig.HeldEuler);

        t.localScale = _heldBaseScale * FigureGrabConfig.HeldScale.Value;
    }

    /// <summary>
    /// Issue B — push the held mini's renderers just past the control board's on-top HUD widgets so
    /// a mini held in FRONT of the opaque board is never occluded by it. Snapshots each renderer's
    /// original SHARED materials, then swaps in per-renderer INSTANCE materials (so no shared bundle
    /// material is mutated globally → the rest of that class's board minis are untouched) with the
    /// renderQueue bumped to <see cref="HeldRenderQueue"/>. ZTest/ZWrite are deliberately left at
    /// the shader's defaults (LEqual + on), so the mini still self-occludes correctly and is hidden
    /// when moved BEHIND real world geometry — only the draw ORDER changes, letting the nearer mini
    /// win over the board widgets (which draw ZWrite-off, so they never own the depth). Restored by
    /// <see cref="RestoreRenderers"/> on release.
    /// </summary>
    private void ApplyRenderOnTop()
    {
        GameObject? root = Root;
        if (root == null)
            return;
        _heldRenderers = root.GetComponentsInChildren<Renderer>(true);
        _origSharedMats = new Material[_heldRenderers.Length][];
        for (int i = 0; i < _heldRenderers.Length; i++)
        {
            Renderer r = _heldRenderers[i];
            if (r == null)
                continue;
            _origSharedMats[i] = r.sharedMaterials;      // snapshot the ORIGINAL shared assets
            Material[] instances = r.materials;          // per-renderer INSTANCES (no global mutation)
            for (int m = 0; m < instances.Length; m++)
            {
                if (instances[m] != null)
                    instances[m].renderQueue = HeldRenderQueue;
            }
        }
    }

    /// <summary>Issue B — restore the renderers' original shared materials (idempotent).</summary>
    private void RestoreRenderers()
    {
        if (_heldRenderers != null && _origSharedMats != null)
        {
            for (int i = 0; i < _heldRenderers.Length; i++)
            {
                Renderer r = _heldRenderers[i];
                if (r != null && _origSharedMats[i] != null)
                    r.sharedMaterials = _origSharedMats[i];
            }
        }
        _heldRenderers = null;
        _origSharedMats = null;
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
        RestoreRenderers(); // Issue B — undo the render-on-top swap (idempotent)
        if (_attached)
        {
            GameObject? root = Root;
            if (root != null)
            {
                Transform t = root.transform;
                // R2: the original parent may have been destroyed while the figure was held
                // (actor removed / scene teardown). Unity's `!= null` catches a destroyed object,
                // so we unparent to the scene root instead of passing a dead Transform to
                // SetParent (which would throw).
                Transform? parent = _origParent != null ? _origParent : null;
                t.SetParent(parent, worldPositionStays: false);
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

    /// <summary>
    /// R2 hardening: true when the game has moved this held figure to a DIFFERENT authoritative
    /// board cell since it was grabbed (a networked move on a remote/enemy turn). Polled by
    /// <see cref="FigureGrabDriver"/> each frame; a true result triggers an immediate
    /// <see cref="Restore"/> so the mini snaps to its real cell instead of riding the hand stale.
    /// Also true when the actor/character was destroyed under us. Cheap (one struct compare).
    /// </summary>
    internal bool AuthoritativeCellChanged()
    {
        if (!_attached)
            return false;
        CActor? ca = Character;
        if (ca == null || _actor == null || _actor.m_RootGameObject == null)
            return true; // actor/root gone — release and let the driver prune
        return ca.ArrayIndex != _grabCell;
    }

    private string Describe()
    {
        CActor? actor = Character;
        return actor != null && actor.Class != null ? actor.Class.ID : "?";
    }
}
