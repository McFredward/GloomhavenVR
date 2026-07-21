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

    /// <summary>
    /// Per-finger base radius (test #13 upgrade): thumb/middle thicker, pinky
    /// thinner — the old constant 0.0075 for all five read as sausage fingers.
    /// Segments taper toward the tip (see <see cref="SegmentTaper"/>).
    /// </summary>
    private static readonly float[] FingerRadii = { 0.0090f, 0.0078f, 0.0080f, 0.0073f, 0.0063f };

    /// <summary>Radius multiplier per segment (root, mid, tip) — real fingers taper.</summary>
    private static readonly float[] SegmentTaper = { 1f, 0.88f, 0.78f };

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
            // The glove is instantiated AFTER HandsDriver's tree-wide VRLayers.Apply, so
            // it would stay on layer 0 and get CULLED by the menu head camera (which
            // renders the mod layer only) — the hands vanished in front of the menu.
            // Re-layer the whole glove subtree onto the mod layer here.
            Core.VRLayers.Apply(instance);
            // Pin best per-renderer skin quality (4 bones/vertex) + updateWhenOffscreen
            // (avoids stale-bounds frustum culling). NOTE: this pin alone is NOT enough —
            // the global QualitySettings.skinWeights is a hard CAP that per-renderer
            // quality can only lower (the intro boots on the 'Fastest' level = ONE bone,
            // which is why the original 5b119e0 pin "didn't hold" there). The global
            // 4-bone floor is enforced per frame by HandsDriver.EnforceGlobalSkinWeights.
            foreach (var smr in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.quality = SkinQuality.Bone4;
                smr.updateWhenOffscreen = true;
            }
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
            if (f == (int)Finger.Index)
                rig.IndexKnuckle = root; // curl-independent beam origin
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
        Material nailMaterial = CreateNailMaterial(material);

        var visualRoot = new GameObject("ProceduralHand");
        visualRoot.transform.SetParent(handRoot, worldPositionStays: false);

        BuildPalm(visualRoot.transform, material, mirror);

        rig.Wrist = handRoot;

        // Fingers: three-joint chains; visual capsule per segment (tapered radii) +
        // a sphere at every joint so bends stay continuous when the curler rotates
        // them. Capsule primitives are Y-aligned — rotated 90° around X so the
        // segment runs along local +Z. Joint POSITIONS/rotations are unchanged from
        // Phase 2 (frozen rig contract) — only the visuals got better (test #13).
        for (int f = 0; f < 5; f++)
        {
            Vector3 lengths = SegmentLengths[f];
            float radius = FingerRadii[f];
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

            Transform midJoint = CreateSegment(rootJoint, lengths.x, radius * SegmentTaper[0], material, $"{(Finger)f}_Mid");
            Transform tipJoint = CreateSegment(midJoint, lengths.y, radius * SegmentTaper[1], material, $"{(Finger)f}_Tip");
            float tipRadius = radius * SegmentTaper[2];
            CreateSegmentVisual(tipJoint, lengths.z, tipRadius, material);
            CreateFingertip(tipJoint, lengths.z, tipRadius, material, nailMaterial);

            rig.SetFinger((Finger)f, new FingerJoints(rootJoint, midJoint, tipJoint));

            if (f == (int)Finger.Index)
            {
                var tipAnchor = new GameObject("Anchor_IndexTip").transform;
                tipAnchor.SetParent(tipJoint, worldPositionStays: false);
                tipAnchor.localPosition = new Vector3(0f, 0f, lengths.z);
                rig.IndexTip = tipAnchor;
                rig.IndexKnuckle = rootJoint; // curl-independent beam origin
            }
        }
    }

    /// <summary>
    /// Rounded palm (test #13): the single hard-edged slab read as a brick. Bevel
    /// approximation by stacking — two interpenetrating boxes (each smaller than
    /// the other on one axis) chamfer the edges, a capsule ridge fills the knuckle
    /// line, a mound rounds the thumb base and a capsule heel rounds the wrist end.
    /// Static visuals only: built once, zero per-frame cost.
    /// </summary>
    private static void BuildPalm(Transform parent, Material material, float mirror)
    {
        Vector3 center = new(0f, -0.008f, 0.045f);

        GameObject slabA = CreatePrimitivePart(PrimitiveType.Cube, parent, material);
        slabA.name = "Palm";
        slabA.transform.localPosition = center;
        slabA.transform.localScale = new Vector3(PalmBoxSize.x, PalmBoxSize.y * 0.72f, PalmBoxSize.z);

        GameObject slabB = CreatePrimitivePart(PrimitiveType.Cube, parent, material);
        slabB.name = "PalmBevel";
        slabB.transform.localPosition = center;
        slabB.transform.localScale = new Vector3(PalmBoxSize.x * 0.88f, PalmBoxSize.y, PalmBoxSize.z * 0.90f);

        // Knuckle ridge: capsule across the palm just behind the finger roots.
        GameObject knuckles = CreatePrimitivePart(PrimitiveType.Capsule, parent, material);
        knuckles.name = "KnuckleRidge";
        knuckles.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // Y-capsule → X-aligned
        knuckles.transform.localPosition = new Vector3(mirror * -0.005f, -0.006f, KnuckleZ - 0.006f);
        knuckles.transform.localScale = new Vector3(0.022f, 0.033f, 0.022f); // r 0.011, len 0.066

        // Thumb-base mound (thenar): the palm visibly thickens toward the thumb.
        GameObject thenar = CreatePrimitivePart(PrimitiveType.Sphere, parent, material);
        thenar.name = "ThumbMound";
        thenar.transform.localPosition = new Vector3(mirror * -0.026f, -0.012f, 0.032f);
        thenar.transform.localScale = new Vector3(0.030f, 0.020f, 0.042f);

        // Heel: rounded wrist end instead of a raw box edge.
        GameObject heel = CreatePrimitivePart(PrimitiveType.Capsule, parent, material);
        heel.name = "PalmHeel";
        heel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        heel.transform.localPosition = new Vector3(0f, -0.008f, 0.008f);
        heel.transform.localScale = new Vector3(0.024f, 0.026f, 0.024f);
    }

    /// <summary>Creates the next joint at the end of a segment and the segment's capsule visual.</summary>
    private static Transform CreateSegment(Transform parentJoint, float length, float radius,
        Material material, string nextJointName)
    {
        CreateSegmentVisual(parentJoint, length, radius, material);
        var next = new GameObject(nextJointName).transform;
        next.SetParent(parentJoint, worldPositionStays: false);
        next.localPosition = new Vector3(0f, 0f, length);
        // Joint sphere ON the new joint: when the curler bends it, the sphere keeps
        // the knuckle continuous instead of showing a gap between two capsules.
        GameObject joint = CreatePrimitivePart(PrimitiveType.Sphere, next, material);
        joint.name = "Joint";
        joint.transform.localScale = Vector3.one * (radius * 2.05f);
        return next;
    }

    private static void CreateSegmentVisual(Transform joint, float length, float radius, Material material)
    {
        GameObject capsule = CreatePrimitivePart(PrimitiveType.Capsule, joint, material);
        capsule.name = "Segment";
        capsule.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        capsule.transform.localPosition = new Vector3(0f, 0f, length * 0.5f);
        // Capsule primitive: height 2 along Y, radius 0.5 ⇒ scale to (2r, len/2, 2r).
        capsule.transform.localScale = new Vector3(radius * 2f, length * 0.5f + radius * 0.5f, radius * 2f);
    }

    /// <summary>
    /// Fingertip cap + fingernail hint (test #13): a slightly squashed sphere caps
    /// the distal segment; a small flattened, lighter-tinted box on the BACK of the
    /// segment (+Y = back of hand) reads as a nail at a glance.
    /// </summary>
    private static void CreateFingertip(Transform tipJoint, float length, float radius,
        Material material, Material nailMaterial)
    {
        GameObject cap = CreatePrimitivePart(PrimitiveType.Sphere, tipJoint, material);
        cap.name = "TipCap";
        cap.transform.localPosition = new Vector3(0f, 0f, length);
        cap.transform.localScale = new Vector3(radius * 1.9f, radius * 1.7f, radius * 2.0f);

        GameObject nail = CreatePrimitivePart(PrimitiveType.Cube, tipJoint, material);
        nail.name = "Nail";
        nail.GetComponent<Renderer>().sharedMaterial = nailMaterial;
        nail.transform.localPosition = new Vector3(0f, radius * 0.72f, length * 0.72f);
        nail.transform.localScale = new Vector3(radius * 1.2f, radius * 0.30f, length * 0.5f);
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
        // UNLIT ONLY (hardware test #7): the VR void and menu scenes have NO lights,
        // so a lit shader (Standard / Legacy Diffuse) renders pitch black — hands were
        // visible as dark silhouettes on the old grey void and vanished completely on
        // the black one. Sprites/Default is unlit (vertex-color tinted) and verified
        // shipped (decompiled ThirdParty GraphProgress/VertexView Shader.Find's it).
        Shader? shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("UI/Default")
                         ?? Shader.Find("Hidden/InternalErrorShader");
        var material = new Material(shader);
        Color baseColor = Plugin.ParseHandColor();
        // Slight per-side tint so L/R stay distinguishable at a glance.
        material.color = side == HandSide.Left
            ? baseColor * new Color(0.92f, 0.96f, 1.05f, 1f)
            : baseColor;
        material.color = new Color(
            Mathf.Clamp01(material.color.r), Mathf.Clamp01(material.color.g),
            Mathf.Clamp01(material.color.b), 1f);
        return material;
    }

    /// <summary>Lighter tint of the hand material — the fingernail hint (unlit, like the hand).</summary>
    private static Material CreateNailMaterial(Material handMaterial)
    {
        var material = new Material(handMaterial);
        Color c = handMaterial.color;
        material.color = new Color(
            Mathf.Clamp01(c.r * 1.10f + 0.12f),
            Mathf.Clamp01(c.g * 1.10f + 0.12f),
            Mathf.Clamp01(c.b * 1.08f + 0.10f),
            1f);
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

        rig.IndexKnuckle ??= rig.GetFinger(Finger.Index).Root;
    }
}
