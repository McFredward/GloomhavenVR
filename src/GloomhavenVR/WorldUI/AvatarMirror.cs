using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// A classic VR "mirror" preview of the LOCAL player's own avatar — the chosen head
/// <see cref="HeadMaskLibrary">mask</see> + two floating hands (the same visuals
/// <see cref="RemoteAvatar"/> renders for peers) — so the player can see how they look before
/// / while other VR players see them. Purely local + cosmetic: it is INDEPENDENT of the net
/// send, so it works in single-player and when the networking hook is off. Toggled live by
/// <c>[Net] MirrorEnabled</c>.
///
/// The reflection is a real planar mirror: the local head + hand world poses are reflected
/// across a VERTICAL plane <see cref="MirrorDistance"/> in front of the head (normal =
/// horizontal head-forward). Reflecting position AND the forward/up basis means that when the
/// player leans, tilts or reaches, the reflection mirrors it — exactly like looking in a glass.
/// The mask faces back toward the player (the reflected +Z points at the head), so they see the
/// front of their own mask.
///
/// Sources are the SAME ones <see cref="LocalRigSampler"/> samples (the owned head camera +
/// <see cref="VRHands"/>), so the mirror matches what is broadcast. The hands use the CURRENT
/// [Hands] HandStyle and rebuild live when it changes.
///
/// WHAT THE GLASS SHOWS (user report 1, "nur die Karten oder Figuren in der Hand, nicht auch die
/// Fächer"): ONLY what is physically IN A HAND — the held figure
/// (<see cref="HeldFigures.Current"/>, visual-only clone) and a single grip-held card per hand of
/// EITHER kind (ability <see cref="VRCard"/> or item <see cref="ItemsPile.ItemChip"/>, drawn as a
/// both-faces-BACK slab — a mirror shows card backs). The open card FANS are deliberately NOT
/// mirrored: the ability fan (<see cref="CardFan"/>) and the item fan (<see cref="ItemsPile"/>)
/// used to be reflected too, which filled the glass with a second wall of cards right where the
/// player is trying to look at themselves. Nothing about the fans is sampled or reflected here
/// any more — peers still see them through <see cref="RemoteHandFan"/> / <see cref="RemoteItemFan"/>,
/// which are driven by the extras packet and are entirely independent of this preview.
/// Rendered on the mod layer
/// (<see cref="VRLayers"/>) so the owned head camera draws it, unlit (the void has no lights).
/// Poses are written directly every frame (a mirror is 1:1, never eased) so the first enabled
/// frame is already correct — no origin streak. No per-frame allocations in the hot path; every
/// game reference is guarded so a torn-down rig is a silent no-op.
/// </summary>
internal sealed class AvatarMirror
{
    /// <summary>Distance (real metres, at scale 1) from the head to the mirror plane.</summary>
    private const float MirrorDistance = 0.7f;

    private GameObject? _root;
    private Transform? _headHolder;
    private Transform? _leftHolder;
    private Transform? _rightHolder;

    private HandRig? _leftRig;
    private HandRig? _rightRig;
    private FingerCurler? _leftCurler;
    private FingerCurler? _rightCurler;

    // Ghost hand ([Hands] GhostHandOnFan): the mirror must show the SAME hand faded as the real
    // rig does — otherwise the self-preview lies about what the ghost looks like (and about what
    // peers see). One HandGhost per side, driven from HandGhosts.LocalSide in Tick; they own
    // private material copies of the MIRROR's own hand renderers, so mirror and real hand never
    // share ghost state (that is exactly why this cannot be done by tinting a shared material).
    private readonly HandGhost _leftGhost = new("mirror Left");
    private readonly HandGhost _rightGhost = new("mirror Right");

    private int _appliedMaskId = -1;
    // Head-mask SIZE ([Net] MaskSize): written onto the "HeadVisual" CHILD the mask library builds,
    // NEVER onto the head holder — the holder carries the rig/diorama scale from Tick and would
    // stomp it (the exact stomping bug that made styled mirror hands render too big).
    private Transform? _headVisual;
    private float _appliedMaskSize = -1f;
    private int _appliedHandStyle = -1; // [Hands] HandStyle the mirror hands were built with
    private float _appliedScale = -1f;
    private float _appliedStyleScale = -1f; // per-style visual scale currently on the hand visual roots
    private Vector3 _lastNormal = Vector3.forward; // reused when head-forward is near-vertical

    // ---- held-interactable mirroring (visual-only, local) --------------------------------
    // Held FIGURE: a script/collider-stripped visual clone of the figure riding the hand
    // (FigureOverlay.BuildFrozenGhost recipe, but keeping the ORIGINAL materials), posed at
    // the reflection of the live figure every frame. Held CARDS: the single card grip-held
    // in each hand, reflected onto a both-faces-BACK slab — which is exactly what a real
    // mirror shows of a card whose face points at the player. The card FANS are NOT
    // mirrored (report 1); see the type doc.
    // _figureClone is a CONTAINER above the cloned animator object (FigureOverlay's "the
    // container should NOT be a child of the figure's Animator object" rule): the game's
    // clips animate ABSOLUTE root position curves on the animator GameObject itself, so a
    // pose written straight onto the clone root in Update was stomped by the clone's own
    // Animator every frame (the held mini's mirrored copy rendered at the clip's baked
    // pose — the "wrong spot"). The container absorbs the reflected pose; a LatePin
    // re-zeroes the clone's local position AFTER the Animator each frame, mirroring what
    // HeldFigures.PinAnimatedRoots does to the real held mini.
    private ActorBehaviour? _figureSource;
    private GameObject? _figureClone;   // container (reflected pose); animator clone is its child
    private Transform? _cloneAnimated;  // the clip-driven clone root inside the container
    private bool _figureAttachLogged;   // one-line hand-local diagnostic, once per grab
    // Held-card attach diagnostic, once per grab per hand. Typed Component (not VRCard) because a
    // held card is EITHER an ability card (Cards.VRCard) OR an item card
    // (Cards.ItemsPile.ItemChip) — see GripHeldCard's ROOT CAUSE note.
    private Component? _loggedCardLeft;
    private Component? _loggedCardRight;

    /// <summary>Slab pool ceiling: at most ONE grip-held card per hand now that the fans are no
    /// longer mirrored (report 1). The pool used to hold 12 ability + 12 item fan slabs.</summary>
    private const int MaxHeldCardSlabs = 2;
    private readonly List<GameObject> _cardSlabs = new(MaxHeldCardSlabs);
    private Mesh? _cardSlabMesh;
    private Material? _cardBackMat;
    private float _slabW;               // the mesh's built-in width/height (ability-card aspect) —
    private float _slabH;               // any other card shape is reached by scaling the slab.

    // Neutral tint for the placeholder head (until the real masks ship in the bundle).
    private static readonly Color PlaceholderTint = new(0.70f, 0.72f, 0.78f);

    /// <summary>Per-frame entry point. Builds/updates the mirror while enabled, tears it down when
    /// disabled or when no head camera exists. Guarded — never throws into the tick chain.</summary>
    public void Tick()
    {
        bool want = NetModule.MirrorEnabled != null && NetModule.MirrorEnabled.Value;
        Camera? head = VRRigDriver.HeadCamera;

        if (!want || head == null)
        {
            if (_root != null)
                Teardown();
            return;
        }

        if (_root == null)
            Build();
        if (_root == null || _headHolder == null || _leftHolder == null || _rightHolder == null)
            return;

        // Swap the head visual if the local mask choice changed while the mirror is open.
        int maskId = LocalRigSampler.LocalMaskId();
        if (maskId != _appliedMaskId)
            BuildHead(maskId);

        // Live re-apply of the local mask SIZE ([Net] MaskSize) so a stepper edit resizes the
        // reflection on the spot — this mirror is exactly where the user tunes the value, so it
        // has to follow without a rebuild or a restart. Same shape as the hand style-scale check
        // below; the receiving side does the identical write from the SENDER's transmitted size.
        float maskSize = LocalRigSampler.LocalMaskSize();
        if (!Mathf.Approximately(maskSize, _appliedMaskSize))
        {
            _appliedMaskSize = maskSize;
            HeadMaskLibrary.ApplySize(_headVisual, maskSize);
        }

        // Rebuild the mirror hands if the local [Hands] HandStyle changed while the mirror
        // is open (known gap: the mirror previously kept whatever style it was built with;
        // HandVisuals.Build reads the CURRENT style, but only at build time).
        if ((int)HandVisuals.LocalStyle() != _appliedHandStyle)
            BuildHands();

        // Live re-apply of the per-style visual scale ([Hands] GloveScale/PlateScale/
        // ArcaneScale, ≈0.62 for the styled pairs) so a stepper edit resizes the mirror
        // hands like the real ones (VRHand.SyncVisualOffset does the same live check).
        // The scale lives on the "HandVisual" child roots (see BuildHands), NEVER on the
        // holders — the holder write below used to stomp it, which is exactly why the
        // mirrored hands rendered ~1.6× bigger than the local styled hands.
        float styleScale = HandVisuals.StyleScale(
            _leftRig != null ? _leftRig.VisualStyle : HandVisuals.LocalStyle());
        if (!Mathf.Approximately(styleScale, _appliedStyleScale))
        {
            _appliedStyleScale = styleScale;
            if (_leftRig != null && _leftRig.Root != null)
                HandVisuals.ApplyStyleScale(_leftRig.Root, _leftRig, styleScale);
            if (_rightRig != null && _rightRig.Root != null)
                HandVisuals.ApplyStyleScale(_rightRig.Root, _rightRig, styleScale);
        }

        // Match the on-table size of the local rig (diorama zoom), like RemoteAvatar.
        // Seat offsets/trims need no handling here: the mirror reflects the local
        // hand's Rig.Root world pose, which already includes them.
        Transform? rigRoot = VRRigDriver.RigRoot;
        float scale = rigRoot != null ? rigRoot.lossyScale.x : 1f;
        if (!(scale > 0f))
            scale = 1f;
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            _headHolder.localScale = Vector3.one * scale;
            _leftHolder.localScale = Vector3.one * scale;
            _rightHolder.localScale = Vector3.one * scale;
        }

        // Build the mirror plane from the head: a point MirrorDistance ahead (scaled with the
        // diorama so the mirror sits a comfortable arm's length away at any zoom), normal =
        // horizontal head-forward.
        Transform ht = head.transform;
        Vector3 headPos = ht.position;
        Vector3 fwd = ht.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = _lastNormal; // looking near-straight up/down: keep the last stable normal
        else
            fwd.Normalize();
        _lastNormal = fwd;
        Vector3 planePoint = headPos + fwd * (MirrorDistance * scale);

        // Head: reflect the head-camera pose.
        Reflect(headPos, ht.rotation, planePoint, fwd, out Vector3 hp, out Quaternion hr);
        _headHolder.SetPositionAndRotation(hp, hr);

        UpdateHand(_leftHolder, _leftCurler, VRHands.Left, planePoint, fwd);
        UpdateHand(_rightHolder, _rightCurler, VRHands.Right, planePoint, fwd);

        // Ghost hand: fade the SAME side the real rig is fading (HandGhosts.LocalSide is the one
        // source of truth for the local hands, the mirror and the multiplayer wire). Apply() is a
        // no-op once engaged, restores on its own when the side goes away, and re-scans by itself
        // after BuildHands hands us a brand-new HandRig instance.
        HandSide? ghostSide = HandGhosts.LocalSide;
        float ghostAlpha = HandGhosts.Alpha;
        _leftGhost.Apply(ghostSide == HandSide.Left ? _leftRig : null, ghostAlpha);
        _rightGhost.Apply(ghostSide == HandSide.Right ? _rightRig : null, ghostAlpha);

        // Held interactables: what the player is HOLDING shows up in the glass too — the held
        // figure and a grip-held card per hand. The card fans deliberately do not (report 1).
        UpdateHeldFigure(planePoint, fwd);
        UpdateHeldCards(planePoint, fwd);
    }

    private void UpdateHand(Transform holder, FingerCurler? curler, VRHand? hand,
        Vector3 planePoint, Vector3 normal)
    {
        if (hand == null || !hand.IsTracked || hand.Rig == null || hand.Rig.Root == null)
        {
            if (holder.gameObject.activeSelf)
                holder.gameObject.SetActive(false);
            return;
        }

        Transform t = hand.Rig.Root;
        Reflect(t.position, t.rotation, planePoint, normal, out Vector3 p, out Quaternion r);
        if (!holder.gameObject.activeSelf)
            holder.gameObject.SetActive(true);
        holder.SetPositionAndRotation(p, r);

        if (curler != null)
        {
            curler.SetTarget(Finger.Thumb, hand.GetCurl(Finger.Thumb));
            curler.SetTarget(Finger.Index, hand.GetCurl(Finger.Index));
            curler.SetTarget(Finger.Middle, hand.GetCurl(Finger.Middle));
            curler.SetTarget(Finger.Ring, hand.GetCurl(Finger.Ring));
            curler.SetTarget(Finger.Pinky, hand.GetCurl(Finger.Pinky));
            curler.Tick(Time.unscaledDeltaTime);
        }
    }

    /// <summary>Reflect a world pose across the vertical plane (point, unit normal). Position uses
    /// the plane; orientation reflects the forward/up basis (a mirror flips handedness — for the
    /// rigid mask + hands this reads exactly like a glass reflection).</summary>
    private static void Reflect(Vector3 pos, Quaternion rot, Vector3 planePoint, Vector3 n,
        out Vector3 outPos, out Quaternion outRot)
    {
        outPos = pos - 2f * Vector3.Dot(pos - planePoint, n) * n;

        Vector3 fwd = rot * Vector3.forward;
        Vector3 up = rot * Vector3.up;
        Vector3 rFwd = fwd - 2f * Vector3.Dot(fwd, n) * n;
        Vector3 rUp = up - 2f * Vector3.Dot(up, n) * n;
        outRot = rFwd.sqrMagnitude > 1e-8f && rUp.sqrMagnitude > 1e-8f
            ? Quaternion.LookRotation(rFwd, rUp)
            : rot;
    }

    /// <summary>
    /// Mirror a HAND-ATTACHED item's world pose by carrying its REAL-hand-local pose over to the
    /// RENDERED mirror hand's frame — the only side-correct way to mirror something riding a hand.
    ///
    /// Transform math (why plain <see cref="Reflect"/> is wrong for hand-attached items):
    /// a true planar mirror M is an IMPROPER transform (det −1); a Transform can only carry a
    /// PROPER rotation, so <see cref="Reflect"/> rebuilds the mirrored orientation from the
    /// reflected forward/up via LookRotation. That proper rotation is R' = M·R·D with
    /// D = diag(−1, 1, 1): identical to the true reflection on the forward and up axes, but with
    /// the local X axis (the thumb↔pinky axis of a hand) pointing OPPOSITE to the true reflection
    /// of the real hand's X. The un-mirrored hand mesh rendered under R' is exactly how the mirror
    /// hand is drawn (holder pose, set in <see cref="UpdateHand"/>).
    ///
    /// A held item sits at handPos + R·o (hand-local offset o, thumb side ⇒ o.x has the thumb
    /// sign). Reflecting its WORLD pose exactly (the old code) lands it at holderPos + M·R·o —
    /// which, expressed in the rendered mirror hand's frame, is R'⁻¹·(M·R·o) = D·o: X flipped, so
    /// the figure/card rendered at the PINKY side of the mirrored hand (the reported bug).
    ///
    /// Fix: real hand frame → hand-local pose → re-emit under the mirrored hand's frame:
    ///   o        = R⁻¹·(itemPos − handPos)          (rigid, world-metre offset — scale-proof)
    ///   localRot = R⁻¹·itemRot
    ///   outPos   = holderPos + R'·o,   outRot = R'·localRot
    /// Now the item's offset in the rendered mirror hand's frame is o — the SAME hand-frame
    /// offset as on the real hand (thumb-side sign preserved), so it sits between thumb and
    /// index in the glass exactly like it does on the real hand. <paramref name="handLocal"/>
    /// returns o for the attach diagnostic. False when the hand/holder frame is unavailable
    /// (untracked hand) — caller falls back to plain reflection.
    /// </summary>
    private bool TryMirrorThroughHand(VRHand? hand, Vector3 itemPos, Quaternion itemRot,
        out Vector3 outPos, out Quaternion outRot, out Vector3 handLocal)
    {
        outPos = default;
        outRot = Quaternion.identity;
        handLocal = default;
        if (hand == null || !hand.IsTracked || hand.Rig == null || hand.Rig.Root == null)
            return false;
        Transform? holder = hand.Side == HandSide.Left ? _leftHolder : _rightHolder;
        if (holder == null || !holder.gameObject.activeSelf)
            return false; // mirror hand not rendered this frame — no frame to attach to

        Transform handT = hand.Rig.Root;
        Quaternion invHand = Quaternion.Inverse(handT.rotation);
        handLocal = invHand * (itemPos - handT.position);
        Quaternion localRot = invHand * itemRot;

        // Holder pose was written this frame by UpdateHand (Tick order guarantees it). Pure
        // quaternion math on the world-metre offset — the holder's localScale (rig/diorama
        // scale) must NOT rescale o, the offset is already in world units.
        outPos = holder.position + holder.rotation * handLocal;
        outRot = holder.rotation * localRot;
        return true;
    }

    // ---- lifecycle ----------------------------------------------------------------------

    private void Build()
    {
        _root = new GameObject("GloomhavenVR.AvatarMirror");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        _headHolder = new GameObject("Head").transform;
        _headHolder.SetParent(_root.transform, worldPositionStays: false);
        BuildHead(LocalRigSampler.LocalMaskId());

        _leftHolder = new GameObject("Hand_Left").transform;
        _leftHolder.SetParent(_root.transform, worldPositionStays: false);
        _leftHolder.gameObject.SetActive(false);

        _rightHolder = new GameObject("Hand_Right").transform;
        _rightHolder.SetParent(_root.transform, worldPositionStays: false);
        _rightHolder.gameObject.SetActive(false);

        BuildHands();

        _appliedScale = -1f;
        VRLayers.Apply(_root);
        VRLog.Info("WorldUI", "Avatar mirror enabled (local self-preview): head + hands + HELD items only "
            + "(held figure / grip-held ability + item cards). Card FANS are not mirrored (report 1) — "
            + "no fan proxy is built and no fan state is read.");
    }

    /// <summary>(Re)build both mirror hands with the CURRENT local [Hands] HandStyle —
    /// called at build time and again whenever the style changes while the mirror is
    /// open (mirrors <see cref="RemoteAvatar"/>.BuildHands). Each hand is built under a
    /// "HandVisual" CHILD of the holder so the per-style visual scale
    /// (<see cref="HandVisuals.ApplyStyleScale"/> writes it onto the transform passed to
    /// Build) lands on that child — the HOLDER keeps carrying only the rig/diorama scale
    /// from <see cref="Tick"/>, and the two writes can never stomp each other. This is
    /// what keeps a styled mirror hand the same ~0.62× visual size as the real one.</summary>
    private void BuildHands()
    {
        if (_leftHolder == null || _rightHolder == null)
            return;
        _appliedHandStyle = (int)HandVisuals.LocalStyle();

        // Release any ghost BEFORE the old hand objects go away: the cloned materials are
        // assets, and Unity does NOT free assets with the GameObject that referenced them — a
        // style switch under an open fan would leak one material per renderer, every switch.
        _leftGhost.Release();
        _rightGhost.Release();

        for (int i = _leftHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_leftHolder.GetChild(i).gameObject);
        for (int i = _rightHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_rightHolder.GetChild(i).gameObject);

        Transform leftVisual = new GameObject("HandVisual").transform;
        leftVisual.SetParent(_leftHolder, worldPositionStays: false);
        Transform rightVisual = new GameObject("HandVisual").transform;
        rightVisual.SetParent(_rightHolder, worldPositionStays: false);

        _leftRig = HandVisuals.Build(leftVisual, HandSide.Left);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig, HandSide.Left) : null;
        _rightRig = HandVisuals.Build(rightVisual, HandSide.Right);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig, HandSide.Right) : null;

        // Build applied the style scale for the built style; remember it so the live
        // check in Tick only re-applies on an actual config edit.
        _appliedStyleScale = HandVisuals.StyleScale(
            _leftRig != null ? _leftRig.VisualStyle : HandVisuals.LocalStyle());

        if (_root != null)
            VRLayers.Apply(_root);
    }

    // ---- held figure mirroring ------------------------------------------------------------

    /// <summary>
    /// Mirror the figure the player physically holds (<see cref="HeldFigures.Current"/> — the
    /// same figure the MP wire syncs): a visual-only clone (no scripts/colliders/physics, own
    /// Animator idling in place, ORIGINAL materials) posed at the reflection of the live
    /// hand-riding figure every frame. Rebuilt only when the held figure changes.
    /// </summary>
    private void UpdateHeldFigure(Vector3 planePoint, Vector3 normal)
    {
        ActorBehaviour? held = HeldFigures.Current;
        if (held == null)
        {
            ClearFigureClone();
            return;
        }

        if (!ReferenceEquals(held, _figureSource) || _figureClone == null)
        {
            ClearFigureClone();
            GameObject animated = held.m_AnimatedGameObject;
            _figureClone = animated != null ? BuildFigureClone(animated) : null;
            if (_figureClone == null)
                return;
            _figureSource = held;
        }

        GameObject source = held.m_AnimatedGameObject;
        if (source == null)
        {
            ClearFigureClone();
            return;
        }

        // Mirror the RENDERED pose of the held mini, not the raw animated transform:
        // the grab drives the ROOT (ActorBehaviour_HeldTransform_Patch suppresses the
        // game's writers; FigureGrabbable poses the root under the hand anchor), and
        // HeldFigures.PinAnimatedRoots re-zeroes the animated child's localPosition every
        // LateUpdate AFTER the Animator — so what the player actually sees each frame is
        // the animated object AT ITS PARENT'S position with the animated object's own
        // rotation. st.position itself can be a mid-frame clip value (the clips write
        // ABSOLUTE root position curves between our Update sample and the pin).
        //
        // The mini rides a HAND, so it must go through the hand-frame path
        // (TryMirrorThroughHand): a plain world-pose reflection lands it X-flipped in the
        // rendered mirror hand's frame — at the PINKY instead of between thumb and index
        // (see the math doc on TryMirrorThroughHand). Plain Reflect stays as the fallback
        // for the frame-gap cases (holding hand untracked / holder not rendered).
        Transform st = source.transform;
        Vector3 renderedPos = st.parent != null ? st.parent.position : st.position;
        VRHand? holdingHand = FindHoldingHand(held);
        Vector3 p;
        Quaternion r;
        if (TryMirrorThroughHand(holdingHand, renderedPos, st.rotation, out p, out r, out Vector3 lp))
        {
            if (!_figureAttachLogged)
            {
                _figureAttachLogged = true;
                // Attach diagnostic: o is the item's offset in BOTH the real and the mirrored
                // hand's frame by construction — thumb-side sign preserved.
                VRLog.Info("WorldUI", $"Mirror held-figure attach: hand={holdingHand!.Side}, "
                    + $"handLocalOffset={lp.ToString("F3")} (same in mirrored hand frame; thumb-side X sign preserved).");
            }
        }
        else
        {
            Reflect(renderedPos, st.rotation, planePoint, normal, out p, out r);
        }
        Transform ct = _figureClone!.transform;
        ct.SetPositionAndRotation(p, r);
        ct.localScale = st.lossyScale; // container parent (_root) is unit scale ⇒ local == world
    }

    /// <summary>The <see cref="VRHand"/> whose grabber physically holds <paramref name="held"/>
    /// (the grab is a <see cref="FigureGrabbable"/> in the hand's ProximityGrabber), or null
    /// (e.g. released this frame while still registered, or grabber torn down).</summary>
    private static VRHand? FindHoldingHand(ActorBehaviour held)
    {
        if (IsHolding(VRHands.Left, held))
            return VRHands.Left;
        if (IsHolding(VRHands.Right, held))
            return VRHands.Right;
        return null;
    }

    private static bool IsHolding(VRHand? hand, ActorBehaviour held) =>
        hand != null && hand.Grabber != null
        && hand.Grabber.Held is FigureGrabbable fg && ReferenceEquals(fg.Actor, held);

    /// <summary>
    /// Visual-only clone of the held figure's animated subtree — the
    /// <see cref="FigureOverlay.BuildFrozenGhost"/> stripping recipe (Cloth/Collider/Rigidbody/
    /// ParticleSystem/MonoBehaviour gone; Animator kept, root motion + events off; VFX-shader/
    /// trail/particle renderers destroyed) but with the figure's ORIGINAL materials, because a
    /// mirror shows the real thing, not a ghost.
    ///
    /// Returns a CONTAINER holding the clone. The clone root IS the Animator's GameObject, and
    /// the game's clips animate absolute root position curves on it — a pose written straight
    /// onto the clone in Update gets overwritten by its own Animator the same frame (this was
    /// the mirrored-figure-at-the-wrong-spot bug). The container takes the reflected pose;
    /// <see cref="LatePin"/> re-zeroes the clone's localPosition after the Animator each frame,
    /// exactly like <see cref="HeldFigures.PinAnimatedRoots"/> does for the real held mini.
    /// </summary>
    private GameObject? BuildFigureClone(GameObject animatedRoot)
    {
        var container = new GameObject("MirrorHeldFigure");
        container.transform.SetParent(_root!.transform, worldPositionStays: false);

        GameObject clone = Object.Instantiate(animatedRoot);
        clone.name = "Animated";
        clone.transform.SetParent(container.transform, worldPositionStays: false);
        // Seat the clone at the container origin (the container carries the reflected world
        // pose + the source's lossyScale, so the clone must contribute identity locals).
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;

        foreach (Animator a in clone.GetComponentsInChildren<Animator>(true))
        {
            if (a == null)
                continue;
            a.applyRootMotion = false;
            a.fireEvents = false;
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        foreach (Cloth c in clone.GetComponentsInChildren<Cloth>(true))
            if (c != null) Object.Destroy(c);
        foreach (Collider col in clone.GetComponentsInChildren<Collider>(true))
            if (col != null) Object.Destroy(col);
        foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>(true))
            if (rb != null) Object.Destroy(rb);
        foreach (ParticleSystem ps in clone.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null)
                continue;
            ps.Stop(withChildren: true, ParticleSystemStopBehavior.StopEmittingAndClear);
            Object.Destroy(ps);
        }
        foreach (MonoBehaviour mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;
            mb.enabled = false; // stop it ticking before the deferred Destroy lands
            Object.Destroy(mb);
        }

        int kept = 0;
        foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            if (r is ParticleSystemRenderer or TrailRenderer or LineRenderer || HasVfxShader(r))
            {
                r.enabled = false;
                Object.Destroy(r);
                continue;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            kept++;
        }
        if (kept == 0)
        {
            Object.Destroy(container);
            return null;
        }

        // Mirror of HeldFigures.PinAnimatedRoots for the clone: re-zero the clip-driven
        // clone root at its container every LateUpdate (after the Animator has run).
        _cloneAnimated = clone.transform;
        container.AddComponent<LatePin>().Target = _cloneAnimated;

        VRLayers.Apply(container);
        return container;
    }

    /// <summary>
    /// Re-pins <see cref="Target"/> at its parent's origin every LateUpdate — after the
    /// Animator, which writes ABSOLUTE root position curves onto the cloned animated object
    /// each frame. The exact counterpart of <see cref="HeldFigures.PinAnimatedRoots"/> (which
    /// runs from FigureGrabDriver.LateUpdate for the REAL held mini). Without it the mirrored
    /// figure rides the clip's baked root pose instead of the reflected hand pose.
    /// </summary>
    private sealed class LatePin : MonoBehaviour
    {
        public Transform? Target;

        private void LateUpdate()
        {
            if (Target != null)
                Target.localPosition = Vector3.zero;
        }
    }

    /// <summary>Same VFX-family test as FigureOverlay: distort/particle/fog shaders must be
    /// destroyed, not kept — they render as a mist blob on a static clone.</summary>
    private static bool HasVfxShader(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null || m.shader == null)
                continue;
            string shaderName = m.shader.name;
            if (shaderName.Contains("Distort") || shaderName.Contains("Particle")
                || shaderName.Contains("Fog") || shaderName.Contains("FX"))
                return true;
        }
        return false;
    }

    private void ClearFigureClone()
    {
        if (_figureClone != null)
            Object.Destroy(_figureClone);
        _figureClone = null;
        _cloneAnimated = null;
        _figureSource = null;
        _figureAttachLogged = false;
    }

    // ---- held-card mirroring ------------------------------------------------------------------

    /// <summary>
    /// Mirror the cards the player is physically HOLDING: one both-faces-BACK slab
    /// (<see cref="RemoteHandFan.BuildBackSlab"/>) per grip-held card — ability
    /// (<see cref="VRCard"/>) or item (<see cref="ItemsPile.ItemChip"/>), grabbed out of a fan or
    /// plucked with the board laser. Backs are exactly what a real mirror shows of a card whose
    /// face points at the player — and they cost nothing (no card art cloning). Slabs are pooled;
    /// inactive when nothing is held.
    ///
    /// REPORT 1 ("Im Spiegel wird auch der Fächer der Items dargestellt — das will ich nicht"):
    /// the open ability fan (<see cref="CardFan.Current"/>) and the open item fan
    /// (<see cref="ItemsPile.Current"/>) used to be walked here and reflected slab-per-card. Both
    /// loops are gone — the mirror is an explicit proxy builder, so "not mirrored" is simply "no
    /// proxy built": no fan is read, nothing subscribes to fan state, and the pool now tops out at
    /// one slab per hand. Nothing else in the mod changes; peers keep seeing the fans through the
    /// extras packet (<see cref="RemoteHandFan"/> / <see cref="RemoteItemFan"/>).
    /// </summary>
    private void UpdateHeldCards(Vector3 planePoint, Vector3 normal)
    {
        int used = 0;

        // Grip-held cards are HAND-ATTACHED: they must go through the hand-frame path
        // (TryMirrorThroughHand) or they render X-flipped in the mirrored hand — at the
        // pinky instead of between thumb and index (same bug as the held figure; see the
        // math doc on TryMirrorThroughHand).
        Component? heldLeft = GripHeldCard(VRHands.Left);
        Component? heldRight = GripHeldCard(VRHands.Right);
        if (!ReferenceEquals(_loggedCardLeft, heldLeft))
            _loggedCardLeft = null;   // released / swapped — re-arm the attach diagnostic
        if (!ReferenceEquals(_loggedCardRight, heldRight))
            _loggedCardRight = null;

        used = MirrorHeldCard(VRHands.Left, heldLeft, used, planePoint, normal);
        used = MirrorHeldCard(VRHands.Right, heldRight, used, planePoint, normal);

        for (int i = used; i < _cardSlabs.Count; i++)
        {
            if (_cardSlabs[i] != null && _cardSlabs[i].activeSelf)
                _cardSlabs[i].SetActive(false);
        }
    }

    /// <summary>
    /// The single card-like object this hand grip-holds, or null. Deliberately typed
    /// <see cref="Component"/>: an ABILITY card is a <see cref="VRCard"/> while an ITEM card is a
    /// <see cref="ItemsPile.ItemChip"/> — two unrelated <c>GrabbableBehaviour</c>s with no common
    /// card base type. Matching only VRCard here was the whole reason a held item card never
    /// appeared in the mirror (see <see cref="UpdateHeldCards"/>'s ROOT CAUSE note); the MP sampler
    /// (<see cref="LocalRigSampler.TryHeldCard"/>) already matches both.
    /// </summary>
    private static Component? GripHeldCard(VRHand? hand)
    {
        if (hand == null || hand.Grabber == null)
            return null;
        return hand.Grabber.Held switch
        {
            VRCard card when card != null => card,
            ItemsPile.ItemChip chip when chip != null => chip,
            _ => null,
        };
    }

    /// <summary>
    /// Mirror the single grip-held card of this hand (if any — ability card or item chip) as one
    /// more back slab. POSITION comes from the hand-frame path so the card keeps its thumb-side
    /// placement in the glass (plain reflection is the fallback when the hand frame is
    /// unavailable); ORIENTATION is re-derived as a billboard against the MIRRORED head — see
    /// <see cref="TryBillboardToMirrorHead"/> for the root cause. Returns the updated used-slab
    /// count.
    /// </summary>
    private int MirrorHeldCard(VRHand? hand, Component? card, int used, Vector3 planePoint, Vector3 normal)
    {
        if (hand == null || card == null)
            return used;

        // The slab takes the held card's OWN face size: an item chip is near-square, an ability
        // card is 63.5×88 — stretching an item onto the ability ratio read as a wrong card.
        bool isItem = card is ItemsPile.ItemChip;
        float w = isItem ? ((ItemsPile.ItemChip)card).FaceWidth : _slabW;
        float h = isItem ? ((ItemsPile.ItemChip)card).FaceHeight : _slabH;

        Transform ct = card.transform;
        Vector3 p;
        Quaternion carried;
        Vector3 lp = default;
        bool viaHand = TryMirrorThroughHand(hand, ct.position, ct.rotation, out p, out carried, out lp);
        if (!viaHand)
            Reflect(ct.position, ct.rotation, planePoint, normal, out p, out carried);

        // REPORT 2: reproduce the real card's "always facing" rule against the MIRRORED viewpoint.
        Quaternion r = TryBillboardToMirrorHead(p, out Quaternion billboard) ? billboard : carried;

        bool logged = hand.Side == HandSide.Left
            ? ReferenceEquals(_loggedCardLeft, card)
            : ReferenceEquals(_loggedCardRight, card);
        if (!logged)
        {
            if (hand.Side == HandSide.Left) _loggedCardLeft = card; else _loggedCardRight = card;
            VRLog.Info("WorldUI", $"Mirror held-card attach: hand={hand.Side}, kind={(isItem ? "Item" : "Ability")}, "
                + $"pos={(viaHand ? "hand-frame" : "plane-reflect")}, handLocalOffset={lp.ToString("F3")} "
                + $"(thumb-side X sign preserved); orient=billboard->mirrored head "
                + $"(delta vs wrist-carry {Quaternion.Angle(carried, r):F1} deg).");
        }
        return PlaceSlabAt(used, p, r, ct.lossyScale, w, h) ? used + 1 : used;
    }

    /// <summary>
    /// The orientation a MIRRORED held card must take.
    ///
    /// ROOT CAUSE of user report 2 ("die Karte, die man in der Hand hält, [wird] an einer falschen
    /// Position und falschen Ausrichtung dargestellt"): a real held card is NOT rigidly attached to
    /// the wrist. <see cref="VRCard.TickHeldPose"/> (and its verbatim twin
    /// <see cref="ItemsPile.ItemChip"/>.TickHeldPose) keeps only the POSITION hand-local — the card
    /// centre eases toward the fixed pinch-point offset in GrabAnchor space — while the ROTATION is
    /// re-billboarded to the head EVERY FRAME: <c>LookRotation(cardPos − headPos, head.up)</c>, so
    /// the face stares at the player's eyes no matter how the wrist is twisted. The mirror, though,
    /// carried the card's WORLD rotation through the hand frame
    /// (<see cref="TryMirrorThroughHand"/>): <c>R' · (R⁻¹ · cardRot)</c>. That expression is only
    /// correct for something RIGIDLY parented to the hand (the held figure); for the card it
    /// re-attaches a head-derived rotation to the wrist, so the mirrored card tumbled with every
    /// wrist twist instead of holding still, and — because a card's visible slab hangs off its
    /// centre along its own up axis — it read as displaced as well.
    ///
    /// FIX: the mirror image is a real planar reflection, so the reflected card's face must point
    /// away from the reflected HEAD — and the reflected head is exactly the head proxy this class
    /// already renders (<c>_headHolder</c>, written earlier this same frame in
    /// <see cref="Tick"/>). Re-running the card's own rule against that proxy —
    /// <c>LookRotation(slabPos − mirrorHeadPos, mirrorHeadUp)</c> — is therefore both the physically
    /// correct reflection AND literally "the mirrored card orients itself to the mirror image", which
    /// is what the report asks for. It is self-consistent with the hand-frame POSITION too: the
    /// billboard is computed from the slab's ACTUAL mirrored position, so the card faces the head
    /// proxy exactly where it is drawn rather than where a pure world reflection would have put it.
    /// (Sanity check that this agrees with the old diagnostic path: <see cref="Reflect"/> preserves
    /// forward and up exactly and flips only local X, so a pure reflection of the real card would
    /// produce this same LookRotation — the two agree wherever the position paths agree.)
    ///
    /// False when there is no head proxy yet, or when the card sits straight above/below the
    /// mirrored head (forward ∥ up makes LookRotation undefined) — the caller then keeps the
    /// carried rotation for that frame rather than letting Unity log and return garbage.
    /// </summary>
    private bool TryBillboardToMirrorHead(Vector3 slabPos, out Quaternion rot)
    {
        rot = Quaternion.identity;
        if (_headHolder == null)
            return false;
        // Card +Z points AWAY from the viewer (CardMesh / BuildBackSlab convention), so the
        // look direction is head → card, matching TickHeldPose's `away` vector verbatim.
        Vector3 away = slabPos - _headHolder.position;
        if (away.sqrMagnitude < 1e-6f)
            return false;
        away.Normalize();
        Vector3 up = _headHolder.up; // reflected head-up: the card rolls with the mirrored head
        if (Mathf.Abs(Vector3.Dot(away, up)) > 0.9995f)
            return false;
        rot = Quaternion.LookRotation(away, up);
        return true;
    }

    /// <summary>Pose pooled slab <paramref name="index"/> at an already-mirrored world pose, sized
    /// to a card of <paramref name="w"/>×<paramref name="h"/> metres. The pooled MESH is built once
    /// at the ability-card aspect, so a differently-shaped card (an item chip) is reached by
    /// scaling the slab — one shared mesh, any card shape. False when the slab assets are
    /// unavailable.</summary>
    private bool PlaceSlabAt(int index, Vector3 p, Quaternion r, Vector3 scale, float w, float h)
    {
        GameObject slab = GetOrCreateSlab(index);
        if (slab == null)
            return false;
        Transform slabT = slab.transform;
        slabT.SetPositionAndRotation(p, r);
        // slab parent (_root) is unit scale; fold the card-shape ratio into the local scale.
        float kx = _slabW > 1e-5f && w > 1e-5f ? w / _slabW : 1f;
        float ky = _slabH > 1e-5f && h > 1e-5f ? h / _slabH : 1f;
        slabT.localScale = new Vector3(scale.x * kx, scale.y * ky, scale.z);
        if (!slab.activeSelf)
            slab.SetActive(true);
        return true;
    }

    private GameObject GetOrCreateSlab(int index)
    {
        while (_cardSlabs.Count <= index)
        {
            if (_cardSlabMesh == null)
            {
                float w, h;
                try
                {
                    w = CardsConfig.CardWidth.Value;
                    h = CardsConfig.CardHeight;
                }
                catch
                {
                    w = 0.0635f;             // CardsConfig defaults (config not bound yet)
                    h = w * (88f / 63.5f);
                }
                _slabW = w;
                _slabH = h;
                _cardSlabMesh = RemoteHandFan.BuildBackSlab(w, h);
                _cardBackMat = CardMesh.CreateBackMaterial();
            }
            var slab = new GameObject($"MirrorCard{_cardSlabs.Count}");
            slab.transform.SetParent(_root!.transform, worldPositionStays: false);
            var mf = slab.AddComponent<MeshFilter>();
            mf.sharedMesh = _cardSlabMesh;
            var mr = slab.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _cardBackMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            slab.SetActive(false);
            VRLayers.Apply(slab);
            _cardSlabs.Add(slab);
        }
        return _cardSlabs[index];
    }

    private void BuildHead(int maskId)
    {
        if (_headHolder == null)
            return;
        _appliedMaskId = Mathf.Clamp(maskId, 0, HeadMaskLibrary.MaskCount - 1);
        for (int i = _headHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_headHolder.GetChild(i).gameObject);
        // Born at the current size so switching masks never flashes at 1× for one frame.
        _appliedMaskSize = LocalRigSampler.LocalMaskSize();
        _headVisual = HeadMaskLibrary.BuildHead(_headHolder, _appliedMaskId, PlaceholderTint, _appliedMaskSize);
        if (_root != null)
            VRLayers.Apply(_root);
    }

    private void Teardown()
    {
        // The figure clone + card slabs are children of _root (destroyed with it); the slab
        // MESH is ours (Unity never destroys assets with a GameObject) — free it. The back
        // MATERIAL is NOT ours: CardMesh.CreateBackMaterial returns the cached material
        // shared by every live card (and RemoteHandFan), so destroying it here would turn
        // all card backs pink after the mirror closes — just drop the reference.
        _figureClone = null;
        _cloneAnimated = null;
        _figureSource = null;
        _figureAttachLogged = false;
        _loggedCardLeft = null;
        _loggedCardRight = null;
        // Ghost hands: restore + free the cloned materials before the hand tree is destroyed
        // (same asset-lifetime rule as the slab mesh below).
        _leftGhost.Release();
        _rightGhost.Release();
        _cardSlabs.Clear();
        if (_cardSlabMesh != null)
            Object.Destroy(_cardSlabMesh);
        _cardSlabMesh = null;
        _cardBackMat = null;
        _slabW = 0f;
        _slabH = 0f;

        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        _headHolder = null;
        _leftHolder = null;
        _rightHolder = null;
        _leftRig = null;
        _rightRig = null;
        _leftCurler = null;
        _rightCurler = null;
        _headVisual = null;
        _appliedMaskId = -1;
        _appliedMaskSize = -1f;
        _appliedHandStyle = -1;
        _appliedScale = -1f;
        _appliedStyleScale = -1f;
        VRLog.Info("WorldUI", "Avatar mirror disabled.");
    }

    /// <summary>Module-shutdown / hot-reload teardown.</summary>
    public void Shutdown()
    {
        if (_root != null)
            Teardown();
    }
}
