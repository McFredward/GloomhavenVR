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
/// wire show up too — the held figure (<see cref="HeldFigures.Current"/>, visual-only clone) and
/// the open card fan (back-slabs at the reflected card poses — a mirror shows card backs).
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
    private Vector3 _lastNormal = Vector3.forward; // reused when head-forward is near-vertical

    // ---- held-interactable mirroring (visual-only, local) --------------------------------
    // Held FIGURE: a script/collider-stripped visual clone of the figure riding the hand
    // (FigureOverlay.BuildFrozenGhost recipe, but keeping the ORIGINAL materials), posed at
    // the reflection of the live figure every frame. Held CARDS: the local CardFan's card
    // poses reflected onto both-faces-BACK slabs — which is exactly what a real mirror
    // shows of cards whose faces point at the player.
    private ActorBehaviour? _figureSource;
    private GameObject? _figureClone;

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

        // Match the on-table size of the local rig (diorama zoom), like RemoteAvatar.
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
    /// open (mirrors <see cref="RemoteAvatar"/>.BuildHands). The scale re-applies via
    /// the holder scale check in <see cref="Tick"/> (holders keep their localScale).</summary>
    private void BuildHands()
    {
        if (_leftHolder == null || _rightHolder == null)
            return;
        _appliedHandStyle = (int)HandVisuals.LocalStyle();

        for (int i = _leftHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_leftHolder.GetChild(i).gameObject);
        for (int i = _rightHolder.childCount - 1; i >= 0; i--)
            Object.Destroy(_rightHolder.GetChild(i).gameObject);

        _leftRig = HandVisuals.Build(_leftHolder, HandSide.Left);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig) : null;
        _rightRig = HandVisuals.Build(_rightHolder, HandSide.Right);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig) : null;

        // HandVisuals.Build wrote the per-style visual scale onto the HOLDERS
        // (ApplyStyleScale) — force the rig-scale match in Tick to re-apply, or a
        // mid-session rebuild leaves the mirror hands at the wrong size.
        _appliedScale = -1f;

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

        Transform st = source.transform;
        Reflect(st.position, st.rotation, planePoint, normal, out Vector3 p, out Quaternion r);
        Transform ct = _figureClone!.transform;
        ct.SetPositionAndRotation(p, r);
        ct.localScale = st.lossyScale; // clone parent (_root) is unit scale ⇒ local == world
    }

    /// <summary>
    /// Visual-only clone of the held figure's animated subtree — the
    /// <see cref="FigureOverlay.BuildFrozenGhost"/> stripping recipe (Cloth/Collider/Rigidbody/
    /// ParticleSystem/MonoBehaviour gone; Animator kept, root motion + events off; VFX-shader/
    /// trail/particle renderers destroyed) but with the figure's ORIGINAL materials, because a
    /// mirror shows the real thing, not a ghost.
    /// </summary>
    private GameObject? BuildFigureClone(GameObject animatedRoot)
    {
        GameObject clone = Object.Instantiate(animatedRoot);
        clone.name = "MirrorHeldFigure";
        clone.transform.SetParent(_root!.transform, worldPositionStays: false);

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
            Object.Destroy(clone);
            return null;
        }

        VRLayers.Apply(clone);
        return clone;
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
        _figureSource = null;
    }

    // ---- card fan mirroring -----------------------------------------------------------------

    /// <summary>
    /// Mirror the local card fan: one both-faces-BACK slab (<see cref="RemoteHandFan.BuildBackSlab"/>)
    /// per fanned card, posed at the reflection of the live card every frame. Backs are exactly what
    /// a real mirror shows of cards whose faces point at the player — and they cost nothing (no card
    /// art cloning). Slabs are pooled; inactive when the fan is closed.
    /// </summary>
    private void UpdateCardFan(Vector3 planePoint, Vector3 normal)
    {
        CardFan? fan = CardFan.Current;
        int count = 0;
        IReadOnlyList<VRCard>? cards = null;
        if (fan != null && fan.IsOpen)
        {
            cards = fan.Cards;
            count = Mathf.Min(cards.Count, MaxMirrorCards);
        }

        for (int i = 0; i < count; i++)
        {
            VRCard card = cards![i];
            if (card == null)
            {
                if (i < _cardSlabs.Count && _cardSlabs[i].activeSelf)
                    _cardSlabs[i].SetActive(false);
                continue;
            }
            GameObject slab = GetOrCreateSlab(i);
            if (slab == null)
                return; // card assets unavailable (no CardMesh material) — skip quietly
            Transform ct = card.transform;
            Reflect(ct.position, ct.rotation, planePoint, normal, out Vector3 p, out Quaternion r);
            Transform slabT = slab.transform;
            slabT.SetPositionAndRotation(p, r);
            slabT.localScale = ct.lossyScale; // slab parent (_root) is unit scale
            if (!slab.activeSelf)
                slab.SetActive(true);
        }

        for (int i = count; i < _cardSlabs.Count; i++)
        {
            if (_cardSlabs[i] != null && _cardSlabs[i].activeSelf)
                _cardSlabs[i].SetActive(false);
        }
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
        // mesh/material are OURS (Unity never destroys those with a GameObject) — free them.
        _figureClone = null;
        _figureSource = null;
        _cardSlabs.Clear();
        if (_cardSlabMesh != null)
            Object.Destroy(_cardSlabMesh);
        _cardSlabMesh = null;
        if (_cardBackMat != null)
            Object.Destroy(_cardBackMat);
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
        VRLog.Info("WorldUI", "Avatar mirror disabled.");
    }

    /// <summary>Module-shutdown / hot-reload teardown.</summary>
    public void Shutdown()
    {
        if (_root != null)
            Teardown();
    }
}
