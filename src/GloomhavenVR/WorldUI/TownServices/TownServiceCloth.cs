using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using GloomhavenVR.Rig;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Native Unity cloth for the priestess and enchantress furniture.
/// The original visible mesh and materials remain in place; an invisible
/// <see cref="Cloth"/> driver supplies their simulated vertices. This is the
/// same PhysX cloth and tapered palm-to-fingertip collider mechanism used by
/// figure garments, with an authored chain along the real curved table lip.</summary>
internal sealed class TownServiceCloth : IDisposable
{
    private const int DriverColumns = 13;
    private const int DriverRows = 25;
    private const int DriverVertexCount = DriverColumns * DriverRows;
    private const int IgnoreRaycastLayer = 2;
    private const int LocalHandCount = 2;
    private const int MaximumPeerHands = 6;
    private const int MaximumHands = LocalHandCount + MaximumPeerHands;
    private const int MaximumHeads = 4;
    private const float PalmRadiusRealMeters = .035f;
    private const float TipRadiusRealMeters = .010f;
    private const float FingerLengthRealMeters = .09f;

    private sealed class Decoration
    {
        internal Mesh Mesh = null!;
        internal Transform Transform = null!;
        internal Vector3[] Rest = Array.Empty<Vector3>();
        internal Vector3[] Deformed = Array.Empty<Vector3>();
        internal int[] DriverVertex = Array.Empty<int>();
    }

    private sealed class Runner
    {
        internal MeshFilter Filter = null!;
        internal Mesh VisibleMesh = null!;
        internal Vector3[] Rest = Array.Empty<Vector3>();
        internal Vector3[] Shown = Array.Empty<Vector3>();
        internal float[] Freedom = Array.Empty<float>();
        internal float[] Side = Array.Empty<float>();
        internal int[] VisibleDriverVertex = Array.Empty<int>();
        internal Vector3[] DriverRest = Array.Empty<Vector3>();
        internal float[] DriverFreedom = Array.Empty<float>();
        internal float[] DriverSide = Array.Empty<float>();
        internal GameObject DriverRoot = null!;
        internal Mesh DriverMesh = null!;
        internal Cloth Cloth = null!;
        internal readonly List<Decoration> Decorations = new();
        internal readonly List<GameObject> Supports = new();
        internal readonly List<ClothSphereColliderPair> SupportPairs = new();
        internal TownClothRunnerState State;
        internal float NextRender;
    }

    private sealed class HandProbe
    {
        internal GameObject Root = null!;
        internal Transform Palm = null!;
        internal Transform Tip = null!;
        internal SphereCollider PalmSphere = null!;
        internal SphereCollider TipSphere = null!;
        internal ClothSphereColliderPair Pair;
    }

    private readonly Transform _station;
    private Runner[] _runners = Array.Empty<Runner>();
    private readonly HandProbe[] _hands = new HandProbe[MaximumHands];
    private readonly HandProbe[] _heads = new HandProbe[MaximumHeads];
    private readonly List<int> _peers = new(4);
    private readonly byte _service;
    private bool _disposed;

    internal TownServiceCloth(Transform station, byte service)
    {
        _station = station;
        _service = service;
        var filters = new List<MeshFilter>(2);
        foreach (MeshFilter filter in station.GetComponentsInChildren<MeshFilter>(true))
            if (filter.name.StartsWith("ClothRunner_", StringComparison.Ordinal)) filters.Add(filter);
        filters.Sort((a, b) => _station.InverseTransformPoint(a.transform.TransformPoint(a.sharedMesh.bounds.center)).x
            .CompareTo(_station.InverseTransformPoint(b.transform.TransformPoint(b.sharedMesh.bounds.center)).x));
        if (filters.Count != (service == 2 ? 2 : service == 3 ? 1 : 0))
            throw new InvalidOperationException("Town cloth mesh count does not match the authored furniture for service " + service);

        try
        {
            for (int i = 0; i < _hands.Length; i++) _hands[i] = BuildProbe("HandProbe", i);
            for (int i = 0; i < _heads.Length; i++) _heads[i] = BuildProbe("HeadProbe", i);
            _runners = new Runner[filters.Count];
            for (int i = 0; i < filters.Count; i++) _runners[i] = Build(filters[i]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Runner Build(MeshFilter filter)
    {
        // Build 562's hand-written spring moved only two edge values and could
        // neither fold nor collide with the table. The headset therefore showed
        // a rigid rectangle passing through the workbench. Keep the approved
        // visible renderer, but drive every vertex from the same native Cloth
        // solver that figure capes use.
        Mesh visible = filter.mesh;
        visible.MarkDynamic();
        Vector3[] rest = visible.vertices;
        var runner = new Runner
        {
            Filter = filter,
            VisibleMesh = visible,
            Rest = rest,
            Shown = new Vector3[rest.Length],
            Freedom = new float[rest.Length],
            Side = new float[rest.Length],
            VisibleDriverVertex = new int[rest.Length]
        };
        Array.Copy(rest, runner.Shown, rest.Length);

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        for (int n = 0; n < rest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(filter.transform.TransformPoint(rest[n]));
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y);
        }
        if (maxY - minY < .3f || maxX - minX < .1f)
            throw new InvalidOperationException("Town cloth dimensions do not match the native cloth source.");
        for (int n = 0; n < rest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(filter.transform.TransformPoint(rest[n]));
            runner.Freedom[n] = Freedom(p, maxY);
            runner.Side[n] = Mathf.Clamp01((p.x - minX) / (maxX - minX));
        }

        BuildNativeDriver(runner);
        BuildDecorations(runner);
        BuildTableSupports(runner, minX, maxX, maxY);
        RewriteColliders(runner);
        return runner;
    }

    private void BuildNativeDriver(Runner runner)
    {
        runner.DriverRoot = new GameObject("GloomhavenVR.NativeTownCloth") { layer = IgnoreRaycastLayer };
        Transform driver = runner.DriverRoot.transform;
        driver.SetParent(runner.Filter.transform, false);

        // Blender's Solidify output contains a second surface and side walls.
        // Feeding that closed shell to PhysX makes two nearly coincident sheets
        // fight each other and produces the rigid board-like motion reported in
        // headset. The authored source grid is deliberately retained in the
        // first 25x13 vertices; simulate that single manifold surface and map
        // its displacement back onto every rendered thickness vertex.
        if (runner.Rest.Length < DriverVertexCount)
            throw new InvalidOperationException("Town cloth is missing its authored 25x13 source grid.");
        runner.DriverRest = new Vector3[DriverVertexCount];
        Array.Copy(runner.Rest, runner.DriverRest, DriverVertexCount);
        var triangles = new int[(DriverRows - 1) * (DriverColumns - 1) * 6];
        int triangle = 0;
        for (int row = 0; row < DriverRows - 1; row++)
        for (int column = 0; column < DriverColumns - 1; column++)
        {
            int a = row * DriverColumns + column, b = a + 1;
            int c = a + DriverColumns, d = c + 1;
            triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
            triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
        }
        var mesh = new Mesh { name = runner.VisibleMesh.name + " (native cloth driver)" };
        mesh.vertices = runner.DriverRest;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var weights = new BoneWeight[mesh.vertexCount];
        for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { Matrix4x4.identity };
        runner.DriverMesh = mesh;

        var skin = runner.DriverRoot.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh;
        skin.rootBone = driver;
        skin.bones = new[] { driver };
        skin.updateWhenOffscreen = true;
        skin.forceRenderingOff = true;

        Cloth cloth = runner.DriverRoot.AddComponent<Cloth>();
        cloth.useGravity = true;
        cloth.useTethers = true;
        cloth.damping = .23f;
        cloth.friction = .48f;
        cloth.bendingStiffness = .42f;
        cloth.stretchingStiffness = .82f;
        cloth.clothSolverFrequency = 120f;
        cloth.enableContinuousCollision = true;
        cloth.worldVelocityScale = .35f;
        cloth.worldAccelerationScale = .35f;
        cloth.sleepThreshold = .05f;

        // Cloth coefficients are world-unit distances. The town resident is born
        // at map-room scale (roughly two hundred game units per perceived metre),
        // unlike a normal 1x authoring preview. FigureCloth established that both
        // fabric and coefficients have to agree at the live scale; because this
        // component is created after placement, the fabric is cooked correctly
        // here and only the coefficient distance needs the same scale conversion.
        float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        runner.DriverFreedom = new float[runner.DriverRest.Length];
        runner.DriverSide = new float[runner.DriverRest.Length];
        for (int n = 0; n < runner.DriverRest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(runner.Filter.transform.TransformPoint(runner.DriverRest[n]));
            minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y);
        }
        for (int n = 0; n < runner.DriverRest.Length; n++)
        {
            Vector3 p = _station.InverseTransformPoint(runner.Filter.transform.TransformPoint(runner.DriverRest[n]));
            runner.DriverFreedom[n] = Freedom(p, maxY);
            runner.DriverSide[n] = Mathf.Clamp01((p.x - minX) / Mathf.Max(.0001f, maxX - minX));
        }
        for (int n = 0; n < runner.Rest.Length; n++)
            runner.VisibleDriverVertex[n] = Nearest(runner.Filter.transform.TransformPoint(runner.Rest[n]),
                runner.Filter.transform, runner.DriverRest);

        float worldScale = Mathf.Max(.0001f, Mathf.Abs(driver.lossyScale.x));
        var coefficients = new ClothSkinningCoefficient[runner.DriverRest.Length];
        for (int n = 0; n < coefficients.Length; n++)
        {
            coefficients[n].maxDistance = runner.DriverFreedom[n] * .34f * worldScale;
            coefficients[n].collisionSphereDistance = .004f * worldScale;
        }
        cloth.coefficients = coefficients;
        cloth.ClearTransformMotion();
        runner.Cloth = cloth;
    }

    private static int Nearest(Vector3 worldPoint, Transform driverTransform, Vector3[] vertices)
    {
        float best = float.MaxValue;
        int nearest = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = (driverTransform.TransformPoint(vertices[i]) - worldPoint).sqrMagnitude;
            if (distance < best) { best = distance; nearest = i; }
        }
        return nearest;
    }

    private void BuildTableSupports(Runner runner, float minX, float maxX, float topY)
    {
        // Unity Cloth intentionally collides only with spheres/capsules. A chain
        // following the measured elliptical front edge is therefore the native
        // equivalent of the table's solid lip. It prevents an inward finger push
        // from carrying the runner through the stone/wood while leaving its free
        // lower edge fully movable.
        const int samples = 9;
        // One curved row catches the free edge; the second carries the upper
        // woven section across the tabletop. Without the rear row the solver
        // could legally use its maxDistance to sag through the solid worktop
        // before reaching the lip. Two rows keep the total at 16 table capsule
        // pairs + 12 hand/head pairs, below Unity Cloth's 32-capsule ceiling.
        for (int row = 0; row < 2; row++)
        {
            SphereCollider? previous = null;
            for (int i = 0; i < samples; i++)
            {
                float x = Mathf.Lerp(minX, maxX, i / (samples - 1f));
                var go = new GameObject("GloomhavenVR.TownCloth.TableSupport." + row + "." + i)
                    { layer = IgnoreRaycastLayer };
                go.transform.SetParent(_station, false);
                float rearward = row == 0 ? .012f : .122f;
                go.transform.localPosition = new Vector3(x, topY - .018f, TableFront(x) + rearward);
                var sphere = go.AddComponent<SphereCollider>();
                sphere.radius = .027f;
                runner.Supports.Add(go);
                if (previous != null) runner.SupportPairs.Add(new ClothSphereColliderPair(previous, sphere));
                previous = sphere;
            }
        }
    }

    private float TableFront(float x)
    {
        float radius = _service == 2 ? .81f : .86f;
        float depth = _service == 2 ? .38f : .46f;
        float center = _service == 2 ? 0f : .03f;
        return center - depth * Mathf.Sqrt(Mathf.Max(0f, 1f - x * x / (radius * radius)));
    }

    private void BuildDecorations(Runner runner)
    {
        foreach (MeshFilter child in runner.Filter.GetComponentsInChildren<MeshFilter>(true))
        {
            if (child == runner.Filter || !child.name.StartsWith("ClothDecoration_", StringComparison.Ordinal)) continue;
            Mesh mesh = child.mesh;
            mesh.MarkDynamic();
            Vector3[] rest = mesh.vertices;
            var decoration = new Decoration
            {
                Mesh = mesh,
                Transform = child.transform,
                Rest = rest,
                Deformed = new Vector3[rest.Length],
                DriverVertex = new int[rest.Length]
            };
            Array.Copy(rest, decoration.Deformed, rest.Length);
            for (int n = 0; n < rest.Length; n++)
            {
                Vector3 point = child.transform.TransformPoint(rest[n]);
                decoration.DriverVertex[n] = Nearest(point, runner.Filter.transform, runner.Rest);
            }
            runner.Decorations.Add(decoration);
        }
    }

    private float Freedom(Vector3 p, float top)
    {
        float edge = TableFront(p.x);
        float topWeave = Mathf.Clamp01((edge + .145f - p.z) / .145f);
        float hanging = Mathf.Clamp01((top - p.y) / .38f);
        return Mathf.Pow(Mathf.Max(.55f * topWeave, hanging), 1.25f);
    }

    private HandProbe BuildProbe(string kind, int index)
    {
        var root = new GameObject("GloomhavenVR.TownCloth." + kind + "." + index) { layer = IgnoreRaycastLayer };
        var palm = new GameObject("Palm") { layer = IgnoreRaycastLayer };
        var tip = new GameObject("Tip") { layer = IgnoreRaycastLayer };
        palm.transform.SetParent(root.transform, false);
        tip.transform.SetParent(root.transform, false);
        var palmSphere = palm.AddComponent<SphereCollider>();
        var tipSphere = tip.AddComponent<SphereCollider>();
        palmSphere.radius = PalmRadiusRealMeters;
        tipSphere.radius = TipRadiusRealMeters;
        var probe = new HandProbe
        {
            Root = root,
            Palm = palm.transform,
            Tip = tip.transform,
            PalmSphere = palmSphere,
            TipSphere = tipSphere,
            Pair = new ClothSphereColliderPair(palmSphere, tipSphere)
        };
        Park(probe);
        return probe;
    }

    private static void Park(HandProbe probe)
    {
        probe.Palm.position = new Vector3(0f, -100000f, 0f);
        probe.Tip.position = probe.Palm.position;
    }

    private static void Place(HandProbe probe, Vector3 palm, Vector3 direction, float scale)
    {
        if (direction.sqrMagnitude < .001f) direction = Vector3.forward;
        probe.Palm.position = palm;
        probe.Tip.position = palm + direction.normalized * (FingerLengthRealMeters * scale);
        probe.PalmSphere.radius = PalmRadiusRealMeters * scale;
        probe.TipSphere.radius = TipRadiusRealMeters * scale;
    }

    private static void PlaceHead(HandProbe probe, Vector3 center, float scale)
    {
        probe.Palm.position = center;
        probe.Tip.position = center;
        probe.PalmSphere.radius = .14f * scale;
        probe.TipSphere.radius = .14f * scale;
    }

    private void UpdateHands()
    {
        int at = 0;
        float scale = VRRigDriver.RigRoot != null
            ? Mathf.Max(.0001f, Mathf.Abs(VRRigDriver.RigRoot.lossyScale.x))
            : Mathf.Max(.0001f, VRRigDriver.BaseWorldScale);
        if (VRHands.Left?.HasPose == true && at < _hands.Length)
            Place(_hands[at++], VRHands.Left.Rig.PalmCenter.position, VRHands.Left.Rig.PalmCenter.forward, scale);
        if (VRHands.Right?.HasPose == true && at < _hands.Length)
            Place(_hands[at++], VRHands.Right.Rig.PalmCenter.position, VRHands.Right.Rig.PalmCenter.forward, scale);

        _peers.Clear();
        NetAvatarDriver.CollectTownFacePeers(_peers);
        foreach (int peer in _peers)
        {
            if (at >= _hands.Length) break;
            if (!NetAvatarDriver.TryGetTownClothHandProbes(peer, out Vector3 left, out Vector3 leftDirection,
                    out Vector3 right, out Vector3 rightDirection, out bool leftValid, out bool rightValid)) continue;
            if (leftValid && at < _hands.Length) Place(_hands[at++], left, leftDirection, scale);
            if (rightValid && at < _hands.Length) Place(_hands[at++], right, rightDirection, scale);
        }
        while (at < _hands.Length) Park(_hands[at++]);

        int headAt = 0;
        if (VRRigDriver.HeadCamera != null)
            PlaceHead(_heads[headAt++], VRRigDriver.HeadCamera.transform.position, scale);
        foreach (int peer in _peers)
        {
            if (headAt >= _heads.Length) break;
            if (NetAvatarDriver.TryGetTownFaceHead(peer, out Vector3 head))
                PlaceHead(_heads[headAt++], head, scale);
        }
        while (headAt < _heads.Length) Park(_heads[headAt++]);
    }

    private void RewriteColliders(Runner runner)
    {
        var pairs = new ClothSphereColliderPair[runner.SupportPairs.Count + _hands.Length + _heads.Length];
        for (int i = 0; i < runner.SupportPairs.Count; i++) pairs[i] = runner.SupportPairs[i];
        for (int i = 0; i < _hands.Length; i++) pairs[runner.SupportPairs.Count + i] = _hands[i].Pair;
        for (int i = 0; i < _heads.Length; i++)
            pairs[runner.SupportPairs.Count + _hands.Length + i] = _heads[i].Pair;
        runner.Cloth.sphereColliders = pairs;
    }

    internal void TickAuthor(float age, float dt, bool visible)
    {
        if (_disposed) return;
        UpdateHands();
        foreach (Runner runner in _runners)
        {
            if (!visible) continue;
            TownClothRunnerState previous = runner.State;
            TownClothRunnerState measured = Render(runner, default, false);
            float inverse = dt > .0001f ? 1f / dt : 0f;
            measured.LeftVelocity = Vector2.ClampMagnitude((measured.Left - previous.Left) * inverse, .25f);
            measured.RightVelocity = Vector2.ClampMagnitude((measured.Right - previous.Right) * inverse, .25f);
            runner.State = measured;
        }
    }

    internal void TickObserver(float age, float elapsed, in TownClothRunnerState first,
        in TownClothRunnerState second, bool visible)
    {
        if (_disposed) return;
        UpdateHands();
        for (int i = 0; i < _runners.Length; i++)
        {
            Runner runner = _runners[i];
            TownClothRunnerState state = i == 0 ? first : second;
            float prediction = Mathf.Clamp(elapsed, 0f, .12f);
            state.Left += state.LeftVelocity * prediction;
            state.Right += state.RightVelocity * prediction;
            if (visible) Render(runner, state, true);
            runner.State = state;
        }
    }

    private TownClothRunnerState Measure(Runner runner, Vector3[] vertices)
    {
        TownClothRunnerState state = default;
        if (vertices.Length != runner.DriverRest.Length) return state;
        Vector2 left = Vector2.zero, right = Vector2.zero;
        float leftWeight = 0f, rightWeight = 0f;
        Vector3 localX = runner.Filter.transform.InverseTransformVector(_station.TransformVector(Vector3.right));
        Vector3 localZ = runner.Filter.transform.InverseTransformVector(_station.TransformVector(Vector3.forward));
        float xx = Mathf.Max(.0001f, Vector3.Dot(localX, localX));
        float zz = Mathf.Max(.0001f, Vector3.Dot(localZ, localZ));
        for (int n = 0; n < vertices.Length; n++)
        {
            float weight = runner.DriverFreedom[n] * runner.DriverFreedom[n];
            if (weight < .1f) continue;
            Vector3 delta = vertices[n] - runner.DriverRest[n];
            var offset = new Vector2(Vector3.Dot(delta, localX) / xx, Vector3.Dot(delta, localZ) / zz);
            float side = runner.DriverSide[n];
            float lw = weight * (1f - side), rw = weight * side;
            left += offset * lw; right += offset * rw;
            leftWeight += lw; rightWeight += rw;
        }
        state.Left = Vector2.ClampMagnitude(left / Mathf.Max(.001f, leftWeight), .09f);
        state.Right = Vector2.ClampMagnitude(right / Mathf.Max(.001f, rightWeight), .09f);
        return state;
    }

    private TownClothRunnerState Render(Runner runner, in TownClothRunnerState owner, bool correctToOwner)
    {
        if (Time.unscaledTime < runner.NextRender) return runner.State;
        runner.NextRender = Time.unscaledTime + 1f / 90f;
        // Cloth.vertices allocates. Capture it exactly once and use the same
        // solver snapshot for rendering, owner measurement and peer correction.
        Vector3[] simulated = runner.Cloth.vertices;
        if (simulated.Length != runner.DriverRest.Length) return runner.State;

        TownClothRunnerState measured = Measure(runner, simulated);
        Vector3 localX = runner.Filter.transform.InverseTransformVector(_station.TransformVector(Vector3.right));
        Vector3 localZ = runner.Filter.transform.InverseTransformVector(_station.TransformVector(Vector3.forward));
        for (int n = 0; n < runner.Rest.Length; n++)
        {
            int source = runner.VisibleDriverVertex[n];
            Vector3 shown = runner.Rest[n] + simulated[source] - runner.DriverRest[source];
            if (correctToOwner)
            {
                float side = runner.Side[n];
                Vector2 wanted = Vector2.Lerp(owner.Left, owner.Right, side);
                Vector2 actual = Vector2.Lerp(measured.Left, measured.Right, side);
                Vector2 correction = Vector2.ClampMagnitude(wanted - actual, .08f);
                shown += runner.Freedom[n] * (localX * correction.x + localZ * correction.y);
            }
            runner.Shown[n] = shown;
        }
        runner.VisibleMesh.vertices = runner.Shown;
        runner.VisibleMesh.RecalculateNormals();
        runner.VisibleMesh.RecalculateBounds();

        foreach (Decoration decoration in runner.Decorations)
        {
            for (int n = 0; n < decoration.Rest.Length; n++)
            {
                int source = decoration.DriverVertex[n];
                Vector3 worldDelta = runner.Filter.transform.TransformVector(runner.Shown[source] - runner.Rest[source]);
                decoration.Deformed[n] = decoration.Rest[n] + decoration.Transform.InverseTransformVector(worldDelta);
            }
            decoration.Mesh.vertices = decoration.Deformed;
            decoration.Mesh.RecalculateNormals();
            decoration.Mesh.RecalculateBounds();
        }
        return measured;
    }

    internal TownClothRunnerState First => _runners.Length > 0 ? _runners[0].State : default;
    internal TownClothRunnerState Second => _runners.Length > 1 ? _runners[1].State : default;

    internal void SetVisible(bool visible)
    {
        foreach (Runner runner in _runners)
        {
            if (runner.Cloth != null) runner.Cloth.enabled = visible;
            foreach (GameObject support in runner.Supports)
                if (support != null) support.SetActive(visible);
            if (visible && runner.Cloth != null) runner.Cloth.ClearTransformMotion();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Runner runner in _runners)
        {
            foreach (GameObject support in runner.Supports)
                if (support != null) UnityEngine.Object.Destroy(support);
            if (runner.DriverRoot != null) UnityEngine.Object.Destroy(runner.DriverRoot);
            if (runner.DriverMesh != null) UnityEngine.Object.Destroy(runner.DriverMesh);
            if (runner.VisibleMesh != null) UnityEngine.Object.Destroy(runner.VisibleMesh);
            foreach (Decoration decoration in runner.Decorations)
                if (decoration.Mesh != null) UnityEngine.Object.Destroy(decoration.Mesh);
        }
        foreach (HandProbe probe in _hands)
            if (probe?.Root != null) UnityEngine.Object.Destroy(probe.Root);
        foreach (HandProbe probe in _heads)
            if (probe?.Root != null) UnityEngine.Object.Destroy(probe.Root);
    }
}
