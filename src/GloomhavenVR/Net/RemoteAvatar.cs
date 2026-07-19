using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Visual proxy for one remote VR player: a floating head "mask" + two floating hands
/// (Demeo style — no humanoid IK), anchored in the SHARED world frame and eased toward the
/// latest received pose with Lerp/Slerp (never snapped). Purely cosmetic; touches no game
/// state. Built lazily by <see cref="NetAvatarDriver"/> on the first rig packet from a peer
/// and destroyed on player-left or staleness.
///
/// Hands reuse the mod's own <see cref="HandVisuals"/> (bundle glove or procedural fallback)
/// so remote hands look exactly like the local ones. The head mask loads
/// <c>Assets/Bundle/Head/VRHeadMask.prefab</c> from the ALREADY-LOADED mod bundle when the
/// user ships one (see PLAN.md "mask asset contract"); until then a low-poly placeholder head
/// stands in so the feature is testable now.
///
/// Sender scale: each part holder is scaled by the sender's rig <c>WorldScale</c> so a remote
/// player's 15 cm hand reads the same physical size above the shared board regardless of the
/// sender's diorama zoom. Positions are absolute world (game units).
/// </summary>
internal sealed class RemoteAvatar
{
    private const string HeadMaskPrefabPath = "Assets/Bundle/Head/VRHeadMask.prefab";

    private readonly GameObject _root;
    private readonly Transform _headHolder;
    private readonly Transform _leftHolder;
    private readonly Transform _rightHolder;

    private readonly HandRig? _leftRig;
    private readonly HandRig? _rightRig;
    private readonly FingerCurler? _leftCurler;
    private readonly FingerCurler? _rightCurler;

    private AvatarState _target;
    private bool _hasTarget;
    private float _appliedScale = -1f;

    /// <summary>Seconds since the last accepted packet (staleness bookkeeping).</summary>
    public float TimeSinceUpdate { get; private set; }

    public int PlayerId { get; }

    public RemoteAvatar(int playerId)
    {
        PlayerId = playerId;

        _root = new GameObject($"GloomhavenVR.RemoteAvatar[{playerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.position = Vector3.zero;
        _root.transform.rotation = Quaternion.identity;
        _root.transform.localScale = Vector3.one;

        Color tint = TintFor(playerId);

        _headHolder = new GameObject("Head").transform;
        _headHolder.SetParent(_root.transform, worldPositionStays: false);
        BuildHeadMask(_headHolder, tint);
        _headHolder.gameObject.SetActive(false);

        _leftHolder = new GameObject("Hand_Left").transform;
        _leftHolder.SetParent(_root.transform, worldPositionStays: false);
        _leftRig = HandVisuals.Build(_leftHolder, HandSide.Left);
        _leftCurler = _leftRig != null ? new FingerCurler(_leftRig) : null;
        _leftHolder.gameObject.SetActive(false);

        _rightHolder = new GameObject("Hand_Right").transform;
        _rightHolder.SetParent(_root.transform, worldPositionStays: false);
        _rightRig = HandVisuals.Build(_rightHolder, HandSide.Right);
        _rightCurler = _rightRig != null ? new FingerCurler(_rightRig) : null;
        _rightHolder.gameObject.SetActive(false);

        // Whole subtree onto the mod layer so the owned head camera renders it (no-op when
        // VR is not running, exactly like the local hands).
        VRLayers.Apply(_root);

        VRLog.Info("Net", $"Remote avatar created for player {playerId}.");
    }

    /// <summary>Accept a freshly-decoded state as the new interpolation target.</summary>
    public void SetTarget(in AvatarState state)
    {
        _target = state;
        _hasTarget = true;
        TimeSinceUpdate = 0f;

        // Apply sender scale to the part holders when it changes (cosmetic sizing only).
        float scale = state.WorldScale > 0f ? state.WorldScale : 1f;
        if (!Mathf.Approximately(scale, _appliedScale))
        {
            _appliedScale = scale;
            _headHolder.localScale = Vector3.one * scale;
            _leftHolder.localScale = Vector3.one * scale;
            _rightHolder.localScale = Vector3.one * scale;
        }
    }

    /// <summary>Per-frame interpolation toward the latest target. Call from the driver's Update.</summary>
    public void Tick(float deltaTime)
    {
        TimeSinceUpdate += deltaTime;
        // Defensive: if our root was destroyed out from under us (should not happen — it is
        // DontDestroyOnLoad + HideAndDontSave and owned solely by us) skip rather than throw.
        if (_root == null || !_hasTarget)
            return;

        float dt = Mathf.Max(deltaTime, 0f);
        float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * dt);

        UpdatePart(_headHolder, _target.HeadValid, in _target.Head, k);
        UpdateHand(_leftHolder, _leftCurler, in _target.Left, _target.HasFingers, k, dt);
        UpdateHand(_rightHolder, _rightCurler, in _target.Right, _target.HasFingers, k, dt);
    }

    private static void UpdatePart(Transform holder, bool valid, in RigPose pose, float k)
    {
        if (!valid)
        {
            if (holder.gameObject.activeSelf) holder.gameObject.SetActive(false);
            return;
        }
        if (!holder.gameObject.activeSelf)
        {
            // First activation: snap to avoid a lerp streak from the origin.
            holder.SetPositionAndRotation(pose.Position, pose.Rotation);
            holder.gameObject.SetActive(true);
            return;
        }
        holder.SetPositionAndRotation(
            Vector3.Lerp(holder.position, pose.Position, k),
            Quaternion.Slerp(holder.rotation, pose.Rotation, k));
    }

    private static void UpdateHand(Transform holder, FingerCurler? curler, in HandStateSample hand, bool fingers, float k, float dt)
    {
        UpdatePart(holder, hand.Tracked, in hand.Pose, k);
        if (!hand.Tracked || curler == null)
            return;
        if (fingers)
        {
            curler.SetTarget(Finger.Thumb, hand.Curl0);
            curler.SetTarget(Finger.Index, hand.Curl1);
            curler.SetTarget(Finger.Middle, hand.Curl2);
            curler.SetTarget(Finger.Ring, hand.Curl3);
            curler.SetTarget(Finger.Pinky, hand.Curl4);
        }
        curler.Tick(dt);
    }

    public void Destroy()
    {
        if (_root != null)
            Object.Destroy(_root);
        VRLog.Info("Net", $"Remote avatar destroyed for player {PlayerId}.");
    }

    // ---- head mask ----------------------------------------------------------------------

    private static void BuildHeadMask(Transform parent, Color tint)
    {
        GameObject? prefab = TryLoadHeadPrefab();
        if (prefab != null)
        {
            GameObject inst = Object.Instantiate(prefab, parent, worldPositionStays: false);
            inst.name = "HeadMask";
            return;
        }
        BuildPlaceholderHead(parent, tint);
    }

    /// <summary>Load the head-mask prefab from the mod bundle if it is already loaded (never
    /// re-open the file — <see cref="HandVisuals"/> owns the single handle).</summary>
    private static GameObject? TryLoadHeadPrefab()
    {
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles())
        {
            if (bundle == null)
                continue;
            try
            {
                if (bundle.Contains(HeadMaskPrefabPath))
                    return bundle.LoadAsset<GameObject>(HeadMaskPrefabPath);
            }
            catch { /* not our bundle / API quirk — ignore */ }
        }
        return null;
    }

    /// <summary>Low-poly placeholder head: a cranium sphere + a flatter "visor" plate facing
    /// +Z (the mask's forward / where the eyes look), unlit so it reads in the lightless void.</summary>
    private static void BuildPlaceholderHead(Transform parent, Color tint)
    {
        Material mat = UnlitMaterial(tint);
        Material visorMat = UnlitMaterial(tint * new Color(0.6f, 0.65f, 0.75f, 1f));

        GameObject cranium = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(cranium.GetComponent<Collider>());
        cranium.name = "Cranium";
        cranium.transform.SetParent(parent, worldPositionStays: false);
        cranium.transform.localPosition = Vector3.zero;
        cranium.transform.localScale = new Vector3(0.17f, 0.20f, 0.21f); // ~human head, +Z long
        cranium.GetComponent<Renderer>().sharedMaterial = mat;

        // Visor/mask plate on the face (+Z forward) — a landmark so orientation reads clearly.
        GameObject visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(visor.GetComponent<Collider>());
        visor.name = "Visor";
        visor.transform.SetParent(parent, worldPositionStays: false);
        visor.transform.localPosition = new Vector3(0f, 0.01f, 0.10f);
        visor.transform.localScale = new Vector3(0.14f, 0.055f, 0.03f);
        visor.GetComponent<Renderer>().sharedMaterial = visorMat;
    }

    private static Material UnlitMaterial(Color color)
    {
        // Same rationale as HandVisuals: the void/menu have no lights, so use an unlit shader.
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                        ?? Shader.Find("Hidden/InternalErrorShader");
        var m = new Material(shader);
        m.color = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), 1f);
        return m;
    }

    /// <summary>Stable per-player tint so avatars are distinguishable at a glance.</summary>
    private static Color TintFor(int playerId)
    {
        float hue = (playerId * 0.61803398875f) % 1f; // golden-ratio hashing → spread hues
        return Color.HSVToRGB(hue, 0.45f, 1f);
    }
}
