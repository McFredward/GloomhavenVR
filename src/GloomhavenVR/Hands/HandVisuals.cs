using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Hands;

/// <summary>
/// Builds the visible hand and its <see cref="HandRig"/> transform contract.
///
/// Source priority:
/// 1. Glove prefab from <c>BepInEx/plugins/GloomhavenVR/gloomhavenvr.bundle</c>
///    (companion Unity project; arrives via a later human step — unity/HANDS.md).
///    Expected asset paths (documented contract, docs/INTERFACES-P2.md §Hand assets):
///      <c>Assets/Bundle/Hands/VRHand_L.prefab</c> / <c>VRHand_R.prefab</c>
///    (legacy alias also probed: <c>HandLeft.prefab</c> / <c>HandRight.prefab</c>).
///    Rig mapping inside the prefab, by child name (first match wins):
///      anchors  <c>Anchor_Wrist</c>, <c>Anchor_Palm</c>, <c>Anchor_IndexTip</c>, <c>Anchor_Grab</c>,
///      fingers  <c>Anchor_{Thumb|Index|Middle|Ring|Pinky}_{Root|Mid|Tip}</c>,
///      fallback SteamVR skeleton bone names (<c>finger_index_0_r</c> …).
///    Any missing transform is synthesized at the procedural default position, so the
///    HandRig contract is ALWAYS complete regardless of asset quality.
/// 2. Procedural primitive hand (palm box + 5 capsule-segment fingers with correct
///    joint transforms) — zero-asset fallback so Phase 2+ is testable NOW.
///
/// All dimensions are meters at scale 1; the hand inherits the diorama scale from the
/// rig root above it.
/// </summary>
internal static class HandVisuals
{
    private const string BundleFileName = "gloomhavenvr.bundle";

    private static AssetBundle? _bundle;
    private static bool _bundleProbed;

    // ---- procedural hand dimensions (meters, right hand; X mirrored for left) --------

    private static readonly Vector3 PalmCenterPos = new(0f, -0.008f, 0.05f);
    private static readonly Vector3 PalmBoxSize = new(0.078f, 0.026f, 0.082f);
    private const float KnuckleZ = 0.088f;
    private const float FingerRadius = 0.0075f;

    // Per finger: knuckle X offset (right hand, thumb side = -X), segment lengths root/mid/tip.
    private static readonly float[] FingerX = { -0.038f, -0.026f, -0.008f, 0.010f, 0.028f };
    private static readonly Vector3[] SegmentLengths =
    {
        new(0.042f, 0.030f, 0.025f), // thumb (root sits at the palm edge, see BuildProceduralHand)
        new(0.036f, 0.024f, 0.020f), // index
        new(0.040f, 0.026f, 0.022f), // middle
        new(0.036f, 0.024f, 0.020f), // ring
        new(0.028f, 0.019f, 0.017f), // pinky
    };

    /// <summary>
    /// Build the hand visual + rig under <paramref name="handRoot"/> (the wrist-space
    /// child of the tracked pose). Returns a complete <see cref="HandRig"/> always.
    /// </summary>
    internal static HandRig Build(Transform handRoot, HandSide side)
    {
        var rig = new HandRig { Root = handRoot };

        GameObject? prefab = TryLoadPrefab(side);
        if (prefab != null)
        {
            GameObject instance = Object.Instantiate(prefab, handRoot, worldPositionStays: false);
            instance.name = $"Glove_{side}";
            MapPrefabRig(instance.transform, rig, side);
            VRLog.Info("Hands", $"{side}: glove prefab loaded from bundle.");
        }
        else
        {
            BuildProceduralHand(handRoot, rig, side);
        }

        // Synthesize whatever the asset did not provide so the contract always holds.
        FillMissingAnchors(handRoot, rig, side);
        return rig;
    }

    /// <summary>Release the cached bundle (module shutdown / hot reload).</summary>
    internal static void UnloadBundle()
    {
        if (_bundle != null)
        {
            _bundle.Unload(unloadAllLoadedObjects: false);
            _bundle = null;
        }
        _bundleProbed = false;
    }

    // ---- bundle loading ---------------------------------------------------------------

    private static GameObject? TryLoadPrefab(HandSide side)
    {
        AssetBundle? bundle = GetBundle();
        if (bundle == null)
            return null;

        string[] candidates = side == HandSide.Left
            ? new[] { "Assets/Bundle/Hands/VRHand_L.prefab", "Assets/Bundle/Hands/HandLeft.prefab" }
            : new[] { "Assets/Bundle/Hands/VRHand_R.prefab", "Assets/Bundle/Hands/HandRight.prefab" };

        foreach (string path in candidates)
        {
            var prefab = bundle.LoadAsset<GameObject>(path);
            if (prefab != null)
                return prefab;
        }

        VRLog.Info("Hands", $"{side}: no glove prefab in bundle (looked for {string.Join(", ", candidates)}) — using procedural hand.");
        return null;
    }

    private static AssetBundle? GetBundle()
    {
        if (_bundleProbed)
            return _bundle;
        _bundleProbed = true;

        string pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string bundlePath = Path.Combine(pluginDir, BundleFileName);
        if (!File.Exists(bundlePath))
        {
            VRLog.Info("Hands", $"Asset bundle not found ({bundlePath}) — procedural hands active.");
            return null;
        }

        _bundle = AssetBundle.LoadFromFile(bundlePath);
        if (_bundle == null)
            VRLog.Warn("Hands", $"AssetBundle.LoadFromFile failed for {bundlePath} — procedural hands active.");
        return _bundle;
    }

    // ---- prefab rig mapping -------------------------------------------------------------

    private static readonly string[][] FingerNameParts =
    {
        new[] { "Thumb", "thumb" },
        new[] { "Index", "index" },
        new[] { "Middle", "middle" },
        new[] { "Ring", "ring" },
        new[] { "Pinky", "pinky" },
    };

    private static void MapPrefabRig(Transform instance, HandRig rig, HandSide side)
    {
        string suffix = side == HandSide.Left ? "_l" : "_r";

        rig.Wrist = FindDeep(instance, "Anchor_Wrist") ?? FindDeep(instance, "wrist" + suffix) ?? instance;
        rig.PalmCenter = FindDeep(instance, "Anchor_Palm")!;
        rig.IndexTip = FindDeep(instance, "Anchor_IndexTip")!;
        rig.GrabAnchor = FindDeep(instance, "Anchor_Grab")!;

        for (int f = 0; f < 5; f++)
        {
            string finger = FingerNameParts[f][0];
            string steamVr = FingerNameParts[f][1];
            Transform? root = FindDeep(instance, $"Anchor_{finger}_Root") ?? FindDeep(instance, $"finger_{steamVr}_0{suffix}");
            Transform? mid = FindDeep(instance, $"Anchor_{finger}_Mid") ?? FindDeep(instance, $"finger_{steamVr}_1{suffix}");
            Transform? tip = FindDeep(instance, $"Anchor_{finger}_Tip") ?? FindDeep(instance, $"finger_{steamVr}_2{suffix}");
            rig.SetFinger((Finger)f, new FingerJoints(root!, mid!, tip!));
        }
    }

    private static Transform? FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform? found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    // ---- procedural fallback hand -----------------------------------------------------

    private static void BuildProceduralHand(Transform handRoot, HandRig rig, HandSide side)
    {
        float mirror = side == HandSide.Right ? 1f : -1f;
        Material material = CreateHandMaterial(side);

        var visualRoot = new GameObject("ProceduralHand");
        visualRoot.transform.SetParent(handRoot, worldPositionStays: false);

        // Palm slab.
        GameObject palm = CreatePrimitivePart(PrimitiveType.Cube, visualRoot.transform, material);
        palm.name = "Palm";
        palm.transform.localPosition = new Vector3(0f, -0.008f, 0.045f);
        palm.transform.localScale = PalmBoxSize;

        rig.Wrist = handRoot;

        // Fingers: three-joint chains; visual capsule per segment. Capsule primitives are
        // Y-aligned — rotate them 90° around X so the segment runs along local +Z.
        for (int f = 0; f < 5; f++)
        {
            Vector3 lengths = SegmentLengths[f];
            bool isThumb = f == (int)Finger.Thumb;

            var rootJoint = new GameObject($"{(Finger)f}_Root").transform;
            rootJoint.SetParent(visualRoot.transform, worldPositionStays: false);
            if (isThumb)
            {
                // Thumb: starts at the palm edge, splayed outward and pre-rolled so its
                // local X curl axis closes it across the palm.
                rootJoint.localPosition = new Vector3(mirror * -0.032f, -0.012f, 0.03f);
                rootJoint.localRotation = Quaternion.Euler(20f, mirror * -40f, mirror * 35f);
            }
            else
            {
                rootJoint.localPosition = new Vector3(mirror * FingerX[f], 0f, KnuckleZ);
                rootJoint.localRotation = Quaternion.identity;
            }

            Transform midJoint = CreateSegment(rootJoint, lengths.x, material, $"{(Finger)f}_Mid");
            Transform tipJoint = CreateSegment(midJoint, lengths.y, material, $"{(Finger)f}_Tip");
            CreateSegmentVisual(tipJoint, lengths.z, material);

            rig.SetFinger((Finger)f, new FingerJoints(rootJoint, midJoint, tipJoint));

            if (f == (int)Finger.Index)
            {
                var tipAnchor = new GameObject("Anchor_IndexTip").transform;
                tipAnchor.SetParent(tipJoint, worldPositionStays: false);
                tipAnchor.localPosition = new Vector3(0f, 0f, lengths.z);
                rig.IndexTip = tipAnchor;
            }
        }
    }

    /// <summary>Creates the next joint at the end of a segment and the segment's capsule visual.</summary>
    private static Transform CreateSegment(Transform parentJoint, float length, Material material, string nextJointName)
    {
        CreateSegmentVisual(parentJoint, length, material);
        var next = new GameObject(nextJointName).transform;
        next.SetParent(parentJoint, worldPositionStays: false);
        next.localPosition = new Vector3(0f, 0f, length);
        return next;
    }

    private static void CreateSegmentVisual(Transform joint, float length, Material material)
    {
        GameObject capsule = CreatePrimitivePart(PrimitiveType.Capsule, joint, material);
        capsule.name = "Segment";
        capsule.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        capsule.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        // Capsule primitive: height 2 along Y, radius 0.5 ⇒ scale to (2r, len/2, 2r).
        capsule.transform.localScale = new Vector3(FingerRadius * 2f, length * 0.5f + FingerRadius * 0.5f, FingerRadius * 2f);
    }

    private static GameObject CreatePrimitivePart(PrimitiveType type, Transform parent, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        // Interactors are registry-driven — hand visuals must not collide with anything.
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static Material CreateHandMaterial(HandSide side)
    {
        // Built-in shaders can be stripped from the game build — probe a few
        // (TOOLCHAIN §4.1). Worst case is Unity's magenta error shader: ugly but visible.
        Shader? shader = Shader.Find("Standard")
                         ?? Shader.Find("Legacy Shaders/Diffuse")
                         ?? Shader.Find("Sprites/Default");
        var material = shader != null ? new Material(shader) : new Material(Shader.Find("Hidden/InternalErrorShader"));
        material.color = side == HandSide.Left
            ? new Color(0.35f, 0.45f, 0.60f)
            : new Color(0.60f, 0.45f, 0.35f);
        return material;
    }

    // ---- contract completion -------------------------------------------------------------

    private static void FillMissingAnchors(Transform handRoot, HandRig rig, HandSide side)
    {
        float mirror = side == HandSide.Right ? 1f : -1f;

        if (rig.Wrist == null)
            rig.Wrist = handRoot;

        if (rig.PalmCenter == null)
        {
            var palm = new GameObject("Anchor_Palm").transform;
            palm.SetParent(handRoot, worldPositionStays: false);
            palm.localPosition = PalmCenterPos;
            // +Y of the hand frame is the BACK of the hand ⇒ palm normal is -Y:
            // rotate 180° around Z so the anchor's +Y points out of the palm.
            palm.localRotation = Quaternion.Euler(0f, 0f, 180f);
            rig.PalmCenter = palm;
        }

        if (rig.GrabAnchor == null)
        {
            var grab = new GameObject("Anchor_Grab").transform;
            grab.SetParent(rig.PalmCenter, worldPositionStays: false);
            grab.localPosition = new Vector3(0f, 0.02f, 0f); // slightly off the palm surface
            rig.GrabAnchor = grab;
        }

        for (int f = 0; f < 5; f++)
        {
            FingerJoints joints = rig.GetFinger((Finger)f);
            if (joints.IsValid)
                continue;

            // Invisible joint chain at the procedural default positions — keeps the
            // curler and interactors functional even with an unmapped art asset.
            Vector3 lengths = SegmentLengths[f];
            var root = new GameObject($"Anchor_{(Finger)f}_Root").transform;
            root.SetParent(handRoot, worldPositionStays: false);
            root.localPosition = new Vector3(mirror * FingerX[f], 0f, f == 0 ? 0.03f : KnuckleZ);
            var mid = new GameObject($"Anchor_{(Finger)f}_Mid").transform;
            mid.SetParent(root, worldPositionStays: false);
            mid.localPosition = new Vector3(0f, 0f, lengths.x);
            var tip = new GameObject($"Anchor_{(Finger)f}_Tip").transform;
            tip.SetParent(mid, worldPositionStays: false);
            tip.localPosition = new Vector3(0f, 0f, lengths.y);
            rig.SetFinger((Finger)f, new FingerJoints(root, mid, tip));
        }

        if (rig.IndexTip == null)
        {
            FingerJoints index = rig.GetFinger(Finger.Index);
            var tipAnchor = new GameObject("Anchor_IndexTip").transform;
            tipAnchor.SetParent(index.Tip, worldPositionStays: false);
            tipAnchor.localPosition = new Vector3(0f, 0f, SegmentLengths[(int)Finger.Index].z);
            rig.IndexTip = tipAnchor;
        }
    }
}
