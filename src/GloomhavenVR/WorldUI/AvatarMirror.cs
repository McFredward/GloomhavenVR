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
/// [Hands] HandStyle and rebuild live when it changes; held interactables that also ride the MP
/// wire show up too — the held figure (<see cref="HeldFigures.Current"/>, visual-only clone), the
/// open card fan and any single grip-held card (back-slabs at the reflected card poses — a
/// mirror shows card backs).
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

    private int _appliedMaskId = -1;
    private int _appliedHandStyle = -1; // [Hands] HandStyle the mirror hands were built with
    private float _appliedScale = -1f;
    private float _appliedStyleScale = -1f; // per-style visual scale currently on the hand visual roots
    private Vector3 _lastNormal = Vector3.forward; // reused when head-forward is near-vertical

    // ---- held-interactable mirroring (visual-only, local) --------------------------------
    // Held FIGURE: a script/collider-stripped visual clone of the figure riding the hand
    // (FigureOverlay.BuildFrozenGhost recipe, but keeping the ORIGINAL materials), posed at
    // the reflection of the live figure every frame. Held CARDS: the local CardFan's card
    // poses — plus any single card grip-held in a hand — reflected onto both-faces-BACK
    // slabs, which is exactly what a real mirror shows of cards whose faces point at the
    // player.
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

    private const int MaxMirrorCards = 12; // matches RemoteHandFan's clamp
    private readonly List<GameObject> _cardSlabs = new(MaxMirrorCards);
    private Mesh? _cardSlabMesh;
    private Material? _cardBackMat;

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

        // Held interactables: what the player is holding shows up in the glass too.
        UpdateHeldFigure(planePoint, fwd);
        UpdateCardFan(planePoint, fwd);
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
        VRLog.Info("WorldUI", "Avatar mirror enabled (local self-preview).");
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

        // Reflect the RENDERED pose of the held mini, not the raw animated transform:
        // the grab drives the ROOT (ActorBehaviour_HeldTransform_Patch suppresses the
        // game's writers; FigureGrabbable poses the root under the hand anchor), and
        // HeldFigures.PinAnimatedRoots re-zeroes the animated child's localPosition every
        // LateUpdate AFTER the Animator — so what the player actually sees each frame is
        // the animated object AT ITS PARENT'S position with the animated object's own
        // rotation. st.position itself can be a mid-frame clip value (the clips write
        // ABSOLUTE root position curves between our Update sample and the pin).
        Transform st = source.transform;
        Vector3 renderedPos = st.parent != null ? st.parent.position : st.position;
        Reflect(renderedPos, st.rotation, planePoint, normal, out Vector3 p, out Quaternion r);
        Transform ct = _figureClone!.transform;
        ct.SetPositionAndRotation(p, r);
        ct.localScale = st.lossyScale; // container parent (_root) is unit scale ⇒ local == world
    }

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
    }

    // ---- card fan mirroring -----------------------------------------------------------------

    /// <summary>
    /// Mirror the local cards: one both-faces-BACK slab (<see cref="RemoteHandFan.BuildBackSlab"/>)
    /// per fanned card — plus one for a single card GRIP-HELD in either hand (grabbed out of the
    /// fan or a pile viewer), which previously never showed in the glass — posed at the reflection
    /// of the live card every frame. Backs are exactly what a real mirror shows of cards whose
    /// faces point at the player — and they cost nothing (no card art cloning). Slabs are pooled;
    /// inactive when neither the fan nor a held card is present.
    /// </summary>
    private void UpdateCardFan(Vector3 planePoint, Vector3 normal)
    {
        CardFan? fan = CardFan.Current;
        int used = 0;

        if (fan != null && fan.IsOpen)
        {
            IReadOnlyList<VRCard> cards = fan.Cards;
            int count = Mathf.Min(cards.Count, MaxMirrorCards);
            for (int i = 0; i < count; i++)
            {
                VRCard card = cards[i];
                if (card == null)
                    continue;
                if (!PlaceSlab(used, card.transform, planePoint, normal))
                    return; // card assets unavailable (no CardMesh material) — skip quietly
                used++;
            }
        }

        // A card held IN THE HAND shows up in the glass too (the fan loop above only covers
        // it while the fan is open AND still lists it).
        used = MirrorHeldCard(VRHands.Left, fan, used, planePoint, normal);
        used = MirrorHeldCard(VRHands.Right, fan, used, planePoint, normal);

        for (int i = used; i < _cardSlabs.Count; i++)
        {
            if (_cardSlabs[i] != null && _cardSlabs[i].activeSelf)
                _cardSlabs[i].SetActive(false);
        }
    }

    /// <summary>
    /// Mirror the single <see cref="VRCard"/> this hand grip-holds (if any) as one more back
    /// slab. Skips cards the open-fan loop already mirrored this frame (a fan card stays in
    /// <c>fan.Cards</c> while held). Returns the updated used-slab count.
    /// </summary>
    private int MirrorHeldCard(VRHand? hand, CardFan? fan, int used, Vector3 planePoint, Vector3 normal)
    {
        if (hand == null || hand.Grabber == null || hand.Grabber.Held is not VRCard card || card == null)
            return used;

        if (fan != null && fan.IsOpen)
        {
            IReadOnlyList<VRCard> cards = fan.Cards;
            int count = Mathf.Min(cards.Count, MaxMirrorCards);
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(cards[i], card))
                    return used; // already mirrored by the fan loop
            }
        }

        return PlaceSlab(used, card.transform, planePoint, normal) ? used + 1 : used;
    }

    /// <summary>Pose pooled slab <paramref name="index"/> at the reflection of a live card
    /// transform. False when the slab assets are unavailable.</summary>
    private bool PlaceSlab(int index, Transform ct, Vector3 planePoint, Vector3 normal)
    {
        GameObject slab = GetOrCreateSlab(index);
        if (slab == null)
            return false;
        Reflect(ct.position, ct.rotation, planePoint, normal, out Vector3 p, out Quaternion r);
        Transform slabT = slab.transform;
        slabT.SetPositionAndRotation(p, r);
        slabT.localScale = ct.lossyScale; // slab parent (_root) is unit scale
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
        HeadMaskLibrary.BuildHead(_headHolder, _appliedMaskId, PlaceholderTint);
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
        _cardSlabs.Clear();
        if (_cardSlabMesh != null)
            Object.Destroy(_cardSlabMesh);
        _cardSlabMesh = null;
        _cardBackMat = null;

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
        _appliedMaskId = -1;
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
