// FILLED BY WORKER A — the ghost card-hand fan.
//
// Cosmetic fan of card BACKS on a remote player's non-dominant hand. This file NEVER reads a
// single byte of game card data (no AbilityCardUI, no CardFace.Adopt, no CharacterClass): every
// card is a back-on-both-faces slab built from the mod's own procedural card-back material
// (CardMesh.CreateBackMaterial → CardMesh.GetBackTexture). In Gloomhaven you never see another
// player's hand, so backs-only is the whole feature — it opens no cheat vector (PLAN2 anti-cheat).
//
// Geometry mirrors Cards/CardFan.Relayout (arc radius, per-card step, curvature-by-fill, tilt,
// z-stagger) so a remote hand reads exactly like the local one, but with LOCAL constants seeded to
// the CardsConfig defaults — this stays self-contained and does not depend on the game's live Fan
// config being initialised.

using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A cosmetic fan of card BACKS (never fronts) on a remote player's non-dominant hand, sized to
/// <c>owner.HandCardCount</c>. Attached under <see cref="RemoteAvatar.NonDominantHandHolder"/>
/// (falling back to <see cref="RemoteAvatar.Root"/>), floated a palm standoff up the hand normal
/// and arced to face <see cref="RemoteAvatar.HeadHolder"/> — mirroring <see cref="CardFan"/>.
/// Purely visual: reads no game state, only the broadcast hand-card COUNT.
/// </summary>
internal sealed class RemoteHandFan
{
    // ---- fan geometry (real meters / degrees, seeded to CardsConfig Fan* defaults) -----------
    private const float CardWidth = 0.0635f;                       // CardsConfig.CardWidth default
    private const float CardHeight = CardWidth * (88f / 63.5f);    // CardsConfig.CardHeight ratio
    private const float PalmOffset = 0.09f;                        // CardsConfig.FanPalmOffset
    private const float Radius = 0.1792f;                          // CardsConfig.FanEffectiveRadius
    private const float ArcSweepDegrees = 91f;                     // CardsConfig.FanArcSweepDegrees
    private const float PerCardStepDegrees = 14f;                  // CardsConfig.FanPerCardStepDegrees
    private const float ArchFactor = 0.55f;                        // CardsConfig.FanFlatCurvatureFactor
    private const float TiltFactor = 0.85f;                        // CardsConfig.FanTiltFactor
    private const int MaxHandForCurve = 10;                        // CardsConfig.FanMaxHandForCurve
    private const float ZStagger = 0.004f;                         // CardFan.ZStagger (draw order)
    private const int MaxCards = 12;                               // hard clamp on the broadcast count

    /// <summary>Exponential follow/face sharpness (higher = snappier). Mirrors CardFan's eased follow.</summary>
    private const float Smoothing = 14f;

    private readonly RemoteAvatar _owner;

    private GameObject? _root;              // fan pivot; child of the current hand holder
    private Transform? _holder;            // the holder we are currently parented under
    private readonly System.Collections.Generic.List<GameObject> _cards = new(MaxCards);
    private int _builtCount = -1;          // how many card slabs currently exist (-1 = never built)
    private bool _poseInit;                // snap (no ease) on the first pose after (re)activation

    public RemoteHandFan(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        // Which hand does the fan hang off? owner.NonDominantHandHolder already resolves to the
        // LEFT holder when DominantRight is true (the sensible default), else RIGHT; fall back to
        // the avatar root when the holder is missing.
        // TODO(handedness): if fan is on wrong hand, foundation DominantRight predicate is inverted.
        Transform? holder = _owner.NonDominantHandHolder != null ? _owner.NonDominantHandHolder : _owner.Root;
        if (holder == null)
        {
            Hide();
            return;
        }

        // Strict no-op while the hand holder is inactive (hand not tracked this frame): hide the
        // fan and bail without posing or allocating. (The Root fallback is always active.)
        if (!holder.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        int count = Mathf.Clamp(_owner.HandCardCount, 0, MaxCards);
        if (count == 0)
        {
            Hide();
            return;
        }

        EnsureRoot(holder);
        if (_root == null)
            return;

        // Rebuild the card slabs only when the count actually changes (cheap; the sizes/poses of
        // existing slabs are refreshed every frame below and auto-inherit AppliedScale via the
        // scaled holder, so a scale change needs no rebuild).
        if (count != _builtCount)
            Rebuild(count);

        if (!_root.activeSelf)
        {
            _root.SetActive(true);
            _poseInit = true; // snap on the frame we (re)appear so we don't ease in from a stale pose
        }

        PoseFan(holder, dt);
        LayoutCards(count);
    }

    /// <summary>Float the fan a palm standoff up the hand normal and arc it to face the owner's head,
    /// easing smoothly (snapping on the first frame after a (re)activation).</summary>
    private void PoseFan(Transform holder, float dt)
    {
        if (_root == null)
            return;

        float scale = _owner.AppliedScale;
        Transform root = _root.transform;

        // Standoff up the hand normal (holder.up), in world meters. AppliedScale is multiplied in
        // explicitly here because we set the root's WORLD position (the card SIZES instead inherit
        // AppliedScale from the scaled holder we parent under).
        Vector3 target = holder.position + holder.up * (PalmOffset * scale);

        // Face the owner's head: the fan's +Z points AWAY from the head so the card fronts (-Z)
        // look toward the owner and their BACKS face everyone else — exactly like the local fan.
        Transform head = _owner.HeadHolder;
        Quaternion targetRot;
        if (head != null)
        {
            Vector3 away = target - head.position;
            targetRot = away.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(away.normalized, Vector3.up)
                : root.rotation;
        }
        else
        {
            targetRot = root.rotation;
        }

        if (_poseInit)
        {
            _poseInit = false;
            root.SetPositionAndRotation(target, targetRot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * Mathf.Max(dt, 0f));
            root.SetPositionAndRotation(
                Vector3.Lerp(root.position, target, k),
                Quaternion.Slerp(root.rotation, targetRot, k));
        }
    }

    /// <summary>Arc the card slabs in the fan-local frame, mirroring CardFan.Relayout. Positions are
    /// real meters and inherit AppliedScale from the scaled holder above the root.</summary>
    private void LayoutCards(int n)
    {
        if (n <= 0)
            return;

        float step = n > 1 ? Mathf.Min(PerCardStepDegrees, ArcSweepDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        // Curvature-by-fill: a few cards read nearly flat/untilted, a full hand arches and tilts.
        float fill = Mathf.Clamp01((float)n / MaxHandForCurve);
        float arch = ArchFactor * fill;
        float tilt = TiltFactor * fill;

        for (int i = 0; i < _cards.Count; i++)
        {
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * Radius,
                                  (Mathf.Cos(rad) - 1f) * Radius * arch,
                                  -ZStagger * i);
            var rot = Quaternion.Euler(0f, 0f, -angle * tilt);
            Transform t = _cards[i].transform;
            t.localPosition = pos;
            t.localRotation = rot;
        }
    }

    // ------------------------------------------------------------------ build / teardown --

    private void EnsureRoot(Transform holder)
    {
        if (_root == null)
        {
            _root = new GameObject($"GloomhavenVR.RemoteHandFan[{_owner.PlayerId}]");
            _root.transform.localScale = Vector3.one; // inherit AppliedScale from the holder
            _root.SetActive(false);
        }

        // (Re)parent when the non-dominant holder changes (e.g. the sender flips dominant hand).
        if (_holder != holder)
        {
            _holder = holder;
            _root.transform.SetParent(holder, worldPositionStays: false);
            _poseInit = true; // snap to the new hand rather than easing across the body
        }
    }

    /// <summary>Destroy and recreate exactly <paramref name="count"/> back-on-both-faces slabs. Only
    /// called when the count changes (cheap). Re-applies the mod layer so the owned head camera
    /// renders the new slabs.</summary>
    private void Rebuild(int count)
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();

        Mesh mesh = SharedCardMesh;
        Material back = CardMesh.CreateBackMaterial(); // shared: back texture on a Standard material

        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Card{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            card.transform.localScale = Vector3.one;
            var mf = card.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = card.AddComponent<MeshRenderer>();
            mr.sharedMaterial = back;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
        }

        _builtCount = count;

        // Owned head camera renders the mod layer only; put the whole fan subtree on it (no-op
        // when VR is not running, exactly like the local fan/hands).
        VRLayers.Apply(_root!);
    }

    private void Hide()
    {
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
    }

    public void Destroy()
    {
        _cards.Clear();
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        _holder = null;
        _builtCount = -1;
    }

    // ------------------------------------------------------------------ card-back slab mesh --

    private static Mesh? _sharedCardMesh;

    /// <summary>A thin card-back slab whose BOTH faces show the mod's card-back texture: a front
    /// quad (-Z, normal back) and a back quad (+Z, normal forward), each a hair off centre so it
    /// reads as a solid card from either side. Built once and shared by every ghost card.</summary>
    private static Mesh SharedCardMesh => _sharedCardMesh != null ? _sharedCardMesh : (_sharedCardMesh = BuildBackSlab(CardWidth, CardHeight));

    private static Mesh BuildBackSlab(float w, float h)
    {
        float hw = w * 0.5f, hh = h * 0.5f, t = CardMesh.Thickness * 0.5f;

        // 8 verts: front face (z = -t, faces the viewer/owner at -Z) and back face (z = +t).
        var vertices = new[]
        {
            // front (-Z)
            new Vector3(-hw, -hh, -t), new Vector3(-hw, hh, -t), new Vector3(hw, hh, -t), new Vector3(hw, -hh, -t),
            // back (+Z)
            new Vector3(-hw, -hh, t), new Vector3(-hw, hh, t), new Vector3(hw, hh, t), new Vector3(hw, -hh, t),
        };
        var normals = new[]
        {
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
        };
        // Planar card-space UVs; mirror X on the back copy so the (symmetric) lattice lines up.
        var uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f),
            new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
        };
        // Winding chosen (verified via right-hand normal) so the front is visible from -Z and the
        // back from +Z — matching CardMesh's convention (+Z points away from the viewer).
        var tris = new[]
        {
            0, 1, 2, 0, 2, 3,       // front: RH normal -> -Z
            4, 6, 5, 4, 7, 6,       // back:  RH normal -> +Z
        };

        var mesh = new Mesh { name = "GloomhavenVR.RemoteCardBack" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
